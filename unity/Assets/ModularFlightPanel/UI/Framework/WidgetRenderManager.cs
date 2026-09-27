using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    public enum WidgetLifecycleState
    {
        Uninitialized,
        Active,
        Suspended,
        Culled,
        Disposed
    }

    public enum WidgetRefreshTier
    {
        Critical = 0, // 60 Hz / 每一帧更新 (3D姿态球, 航向指示弧, 速度/高度滚带, 油门柱, ECAM圆弧表盘)
        Standard = 1, // 30 Hz / 约 33ms 更新 (ND导航, SAS罗盘)
        Relaxed  = 2, // 10 Hz / 约 100ms 更新 (电力, 维生, 分级ΔV, 时钟, 工具栏, 轨道数据)
        UltraLow = 3  // 2 Hz  / 约 500ms 更新 (大尺度时间加速或后台低频监视)
    }

    public enum GlobalRefreshProfile
    {
        Ultra60Hz = 0,     // 极致满帧 (全组件 60Hz 每一帧刷新)
        Balanced = 1,      // 智能阶梯 (推荐默认: Critical 60Hz, Standard 30Hz, Relaxed 10Hz)
        EcoPowerSaver = 2  // 省电节能 (Critical 30Hz, Standard 15Hz, Relaxed 5Hz)
    }

    public enum RenderResolutionPreset
    {
        Low256 = 256,
        Medium512 = 512,
        High1024 = 1024
    }

    public enum RefreshControlMode
    {
        VSync_GameFPS = 0,     // 垂直同步：完全跟随游戏/显示器实时刷新率 (1:1 / 1:2 / 1:6)
        CustomHz_FreeTier = 1  // 自由定义：用户可任意输入任意浮点数 Hz (例如 11.2Hz, 59.94Hz, 120Hz)
    }

    public class WidgetRegistration
    {
        public BaseFlightWidget Widget;
        public WidgetRefreshTier Tier;
        public WidgetLifecycleState State;
        public float LastUpdateTime;
        public int PhaseOffset;

        public float CustomInterval
        {
            get
            {
                float configInterval = Widget?.Config?.EffectiveUpdateInterval ?? 0f;
                if (configInterval > 0f) return configInterval;
                return Widget?.DefaultUpdateInterval ?? 0f;
            }
        }

        public WidgetRegistration(BaseFlightWidget widget, WidgetRefreshTier tier)
        {
            Widget = widget;
            Tier = tier;
            State = WidgetLifecycleState.Active;
            LastUpdateTime = -10f;
            PhaseOffset = 0;
        }
    }

    /// <summary>
    /// 全局控件绘制与生命周期管理器 (WidgetRenderManager)
    /// 统一接管所有 BaseFlightWidget 组件的：
    /// 1. 完整生命周期（注册、挂起、视口裁剪、注销）
    /// 2. 阶梯式 Tick 刷新率（60Hz/30Hz/10Hz/2Hz）与主调度分发（避免 20+ 个独立 MonoBehaviour.Update 开销）
    /// 3. 3D 离屏 RenderTexture 绘制分辨率集中管控与热切换（256 / 512 / 1024）
    /// </summary>
    public class WidgetRenderManager
    {
        private static WidgetRenderManager _instance;
        public static WidgetRenderManager Instance => _instance ?? (_instance = new WidgetRenderManager());

        private readonly List<WidgetRegistration> _registrations = new List<WidgetRegistration>();
        private readonly List<WidgetRegistration> _criticalRegistrations = new List<WidgetRegistration>();
        private readonly List<WidgetRegistration> _nonCriticalRegistrations = new List<WidgetRegistration>();
        private readonly Dictionary<BaseFlightWidget, WidgetRegistration> _widgetLookup = new Dictionary<BaseFlightWidget, WidgetRegistration>();

        private void RebuildTierBuckets()
        {
            _criticalRegistrations.Clear();
            _nonCriticalRegistrations.Clear();
            for (int i = 0; i < _registrations.Count; i++)
            {
                var reg = _registrations[i];
                if (reg == null) continue;
                if (reg.Tier == WidgetRefreshTier.Critical)
                    _criticalRegistrations.Add(reg);
                else
                    _nonCriticalRegistrations.Add(reg);
            }
        }

        // 全局配置与状态
        public GlobalRefreshProfile CurrentProfile { get; set; } = GlobalRefreshProfile.Balanced;
        public int CurrentNavballResolution { get; private set; } = 512;

        // 渲染分辨率与超采样倍率设置 (关闭自适应动态缩放，采用稳定高保真固定点对点分辨率)
        public bool AutoAdaptResolution { get; set; } = false;
        public float GlobalRenderScaleMultiplier { get; set; } = 1.0f;

        // 垂直同步与自定义刷新率阶梯配置 (用户可自由填写任意浮点数 Hz，例如 11.2Hz，或选择跟随游戏垂直同步)
        public RefreshControlMode ControlMode { get; set; } = RefreshControlMode.VSync_GameFPS;
        public bool SyncWithGameFps
        {
            get => ControlMode == RefreshControlMode.VSync_GameFPS;
            set => ControlMode = value ? RefreshControlMode.VSync_GameFPS : RefreshControlMode.CustomHz_FreeTier;
        }

        public float CriticalHz { get; set; } = 60.0f;     // 用户可自由填写的任意浮点数 (如 120.0f)
        public float StandardHz { get; set; } = 30.0f;     // 用户可自由填写的任意浮点数 (如 30.0f)
        public float RelaxedHz { get; set; } = 10.0f;      // 阶梯注释标称 10Hz；需要时用户可改成任意浮点 (如 11.2f)
        public float UltraLowHz { get; set; } = 2.0f;      // 用户可自由填写的任意浮点数 (如 2.5f)

        // 硬限微秒级帧预算切片调度器配置 (Budgeted Frame Slicing)
        public bool EnableBudgetSlicing { get; set; } = true;
        public float MaxFrameBudgetMs { get; set; } = 0.15f;         // 全局参考最大帧预算 (150微秒)
        public float MaxNonCriticalBudgetMs { get; set; } = 0.08f;  // 阶段 2 非 Critical 组件独立微秒切片预算 (80微秒)，与姿态球彻底解耦，杜绝调度饥饿
        private int _sliceCursor = 0;                                // 非 Critical 组件公平轮询切片游标

        public event Action<int> OnRenderResolutionChanged;
        public event Action<float> OnGlobalRenderScaleChanged;
        public event Action OnRenderSettingChanged;
        public event Action<GlobalRefreshProfile> OnProfileChanged;

        public int TotalWidgetCount => _registrations.Count;
        public int ActiveWidgetCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _registrations.Count; i++)
                {
                    if (_registrations[i].State == WidgetLifecycleState.Active) count++;
                }
                return count;
            }
        }

        private WidgetRenderManager()
        {
        }

        #region Registration & Lifecycle Management

        public void RegisterWidget(BaseFlightWidget widget, WidgetRefreshTier tier = WidgetRefreshTier.Standard)
        {
            if (widget == null) return;
            if (_widgetLookup.ContainsKey(widget))
            {
                _widgetLookup[widget].Tier = tier;
                RebuildTierBuckets();
                return;
            }

            var reg = new WidgetRegistration(widget, tier)
            {
                PhaseOffset = _registrations.Count,
                LastUpdateTime = -10f - (_registrations.Count % 8) * 0.003f
            };
            _registrations.Add(reg);
            _widgetLookup[widget] = reg;
            RebuildTierBuckets();
        }

        public void UnregisterWidget(BaseFlightWidget widget)
        {
            if (widget == null) return;
            if (_widgetLookup.TryGetValue(widget, out var reg))
            {
                reg.State = WidgetLifecycleState.Disposed;
                _registrations.Remove(reg);
                _widgetLookup.Remove(widget);
                RebuildTierBuckets();
            }
        }

        public void SetWidgetActive(BaseFlightWidget widget, bool active)
        {
            if (widget == null) return;
            if (_widgetLookup.TryGetValue(widget, out var reg))
            {
                reg.State = active ? WidgetLifecycleState.Active : WidgetLifecycleState.Suspended;
            }
        }

        /// <summary>
        /// 批量启停全部组件：同时切换生命周期状态与 GameObject 激活状态（名实相符）。
        /// 若只想暂停刷新但保持组件可见，请使用 SetAllSuspended。
        /// </summary>
        public void SetAllActive(bool active)
        {
            var targetState = active ? WidgetLifecycleState.Active : WidgetLifecycleState.Suspended;
            for (int i = 0; i < _registrations.Count; i++)
            {
                var reg = _registrations[i];
                reg.State = targetState;
                if (reg.Widget != null && reg.Widget.gameObject.activeSelf != active)
                {
                    reg.Widget.gameObject.SetActive(active);
                }
            }
        }

        /// <summary>
        /// 批量暂停/恢复遥测刷新，但不改变组件的可见性与 GameObject 激活状态。
        /// </summary>
        public void SetAllSuspended(bool suspended)
        {
            var targetState = suspended ? WidgetLifecycleState.Suspended : WidgetLifecycleState.Active;
            for (int i = 0; i < _registrations.Count; i++)
            {
                _registrations[i].State = targetState;
            }
        }

        public void ClearAll()
        {
            _registrations.Clear();
            _criticalRegistrations.Clear();
            _nonCriticalRegistrations.Clear();
            _widgetLookup.Clear();
        }

        #endregion

        #region Resolution & Supersampling Adaptation

        public void SetNavballResolution(int resolution)
        {
            resolution = Mathf.Clamp(resolution, 128, 2048);
            if (CurrentNavballResolution != resolution)
            {
                CurrentNavballResolution = resolution;
                OnRenderResolutionChanged?.Invoke(resolution);
                OnRenderSettingChanged?.Invoke();
            }
        }

        public void SetRefreshProfile(GlobalRefreshProfile profile)
        {
            if (CurrentProfile != profile)
            {
                CurrentProfile = profile;
                OnProfileChanged?.Invoke(profile);
            }
        }

        /// <summary>
        /// 获取基于 1080p 基准参考高度的屏幕缩放系数 (Canvas Scale Factor)
        /// </summary>
        public float GetCanvasScaleFactor()
        {
            float screenH = Screen.height > 0 ? (float)Screen.height : 1080f;
            return screenH / 1080f;
        }

        /// <summary>
        /// 计算给定组件在物理屏幕上实际占据的像素大小 (Physical Pixels)
        /// </summary>
        public float CalculatePhysicalPixelSize(Vector2 uiSize, float widgetScale = 1.0f)
        {
            float globalScale = WidgetLayoutManager.Instance?.CurrentLayout?.GlobalScale ?? 1.0f;
            float maxDim = Mathf.Max(uiSize.x, uiSize.y);
            float canvasScale = GetCanvasScaleFactor();
            return maxDim * widgetScale * globalScale * canvasScale;
        }

        /// <summary>
        /// 核心算法：自适应刚刚好的渲染分辨率 (Just-Right Optimal Resolution)
        /// 根据当前屏幕物理分辨率、组件物理像素占用、单组件 RenderScale 与全局超采样倍率，
        /// 智能计算并量化为最匹配的 2^n 纹理尺寸 (128 / 256 / 512 / 1024 / 2048)。
        /// </summary>
        public int CalculateOptimalResolution(Vector2 uiSize, float widgetScale = 1.0f, float widgetRenderScale = 1.0f, int minRes = 512, int maxRes = 2048)
        {
            float effectiveMultiplier = Mathf.Clamp(widgetRenderScale, 0.2f, 3.0f) * Mathf.Clamp(GlobalRenderScaleMultiplier, 0.2f, 3.0f);

            if (!AutoAdaptResolution)
            {
                // 关闭自适应时，以手动指定的固定姿态球分辨率 * 倍率
                int raw = Mathf.RoundToInt(CurrentNavballResolution * effectiveMultiplier);
                return QuantizeToOptimalPowerOfTwo(raw, minRes, maxRes);
            }

            // 1. 计算组件在物理屏幕上的实际物理像素跨度
            float physicalPixels = CalculatePhysicalPixelSize(uiSize, widgetScale);

            // 2. 乘以超采样/降采样倍率
            float targetPixels = physicalPixels * effectiveMultiplier;

            // 3. 向上量化至最高效经济的 2^n 纹理尺寸
            return QuantizeToOptimalPowerOfTwo(Mathf.RoundToInt(targetPixels), minRes, maxRes);
        }

        /// <summary>
        /// 将目标像素量化为经济高效的 2^n 纹理贴图，带 10% 容差缓冲以防止临界像素波动引发贴图抖动
        /// </summary>
        public static int QuantizeToOptimalPowerOfTwo(int targetPixels, int minRes = 256, int maxRes = 2048)
        {
            int res;
            if (targetPixels <= 128) res = 128;
            else if (targetPixels <= 220) res = 256;
            else if (targetPixels <= 360) res = 512;
            else if (targetPixels <= 720) res = 1024;
            else res = 2048;

            return Mathf.Clamp(res, minRes, maxRes);
        }

        /// <summary>
        /// 动态调整全局渲染分辨率倍率 (0.5x ~ 2.0x，如 0.8x 节能，1.5x 视网膜超清)
        /// </summary>
        public void SetGlobalRenderScale(float multiplier)
        {
            multiplier = Mathf.Clamp(multiplier, 0.5f, 2.5f);
            if (Mathf.Abs(GlobalRenderScaleMultiplier - multiplier) > 0.001f)
            {
                GlobalRenderScaleMultiplier = multiplier;
                OnGlobalRenderScaleChanged?.Invoke(multiplier);
                OnRenderSettingChanged?.Invoke();
            }
        }

        /// <summary>
        /// 开关自适应刚刚好渲染分辨率模式
        /// </summary>
        public void SetAutoAdaptResolution(bool enabled)
        {
            if (AutoAdaptResolution != enabled)
            {
                AutoAdaptResolution = enabled;
                OnRenderSettingChanged?.Invoke();
            }
        }

        #endregion

        #region Master Update Loop Dispatch

        /// <summary>
        /// 由 FlightHUDManager.Update 单点调用的主分发循环
        /// 一次性拉取遥测上下文，按阶梯刷新率高效顺序分发，杜绝散乱帧开销
        /// 支持微秒级帧预算切片调度 (Budgeted Frame Slicing)，物理硬限锁死最大 CPU 耗时在 0.15ms 以内
        /// </summary>
        /// <param name="unscaledTime">未缩放时间戳，节流与自定义 Hz 的唯一时钟源</param>
        public void MasterUpdate(float unscaledTime)
        {
            if (MFPProfiler.IsMasterBypassed) return;

            IFlightTelemetry telem = FlightTelemetryContext.Current;
            if (telem == null || !telem.HasVessel) return;

            bool profileWidgets = MFPProfiler.IsWidgetProfilingActive;

            MFPProfiler.BeginSample(ProfilerSection.Widgets);
            long startTick = Stopwatch.GetTimestamp();
            double ticksToMs = 1000.0 / Stopwatch.Frequency;
            try
            {
                int critCount = _criticalRegistrations.Count;
                int nonCritCount = _nonCriticalRegistrations.Count;
                int activeCount = 0;

                if (!EnableBudgetSlicing)
                {
                    // 原始全量无切片调度回退分支
                    int count = _registrations.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var reg = _registrations[i];
                        if (reg == null || reg.Widget == null) continue;
                        if (reg.State != WidgetLifecycleState.Active) continue;
                        if (!reg.Widget.gameObject.activeSelf) continue;

                        activeCount++;

                        if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                        reg.LastUpdateTime = unscaledTime;
                        ExecuteWidgetUpdate(reg, telem, profileWidgets);
                    }
                    MFPProfiler.ActiveWidgetCount = activeCount;
                    return;
                }

                // -------------------------------------------------------------
                // 阶段 1：Critical 级核心姿态航电组件无条件保活直通 (姿态球/航向指示弧)
                // 仅遍历专用 Critical 桶，彻底消除全量线性扫描空转
                // -------------------------------------------------------------
                for (int i = 0; i < critCount; i++)
                {
                    var reg = _criticalRegistrations[i];
                    if (reg == null || reg.Widget == null) continue;
                    if (reg.State != WidgetLifecycleState.Active) continue;
                    if (!reg.Widget.gameObject.activeSelf) continue;

                    activeCount++;

                    if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                    reg.LastUpdateTime = unscaledTime;
                    ExecuteWidgetUpdate(reg, telem, profileWidgets);
                }

                // -------------------------------------------------------------
                // 阶段 2：Standard / Relaxed / UltraLow 组件公平轮询切片调度 (Round-Robin Slicing)
                // 仅遍历专用 NonCritical 桶，彻底消除游标跳空与无意义跳步
                // 采用独立计时管道与姿态球耗时解耦，每 3 个组件采样一次时间戳，彻底消除饥饿与高频计时抖动
                // -------------------------------------------------------------
                if (nonCritCount > 0)
                {
                    int startIndex = _sliceCursor % nonCritCount;
                    int scheduledNonCrit = 0;
                    long nonCritStartTick = Stopwatch.GetTimestamp();

                    for (int step = 0; step < nonCritCount; step++)
                    {
                        int i = (startIndex + step) % nonCritCount;
                        var reg = _nonCriticalRegistrations[i];
                        if (reg == null || reg.Widget == null) continue;
                        if (reg.State != WidgetLifecycleState.Active) continue;
                        if (!reg.Widget.gameObject.activeSelf) continue;

                        activeCount++;

                        if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                        reg.LastUpdateTime = unscaledTime;
                        ExecuteWidgetUpdate(reg, telem, profileWidgets);
                        scheduledNonCrit++;

                        // 独立微秒预算检查：每处理 3 个组件检查一次，若达到非 Critical 专属预算，记录游标并平滑让出至下一帧
                        if (scheduledNonCrit % 3 == 0)
                        {
                            double nonCritElapsedMs = (Stopwatch.GetTimestamp() - nonCritStartTick) * ticksToMs;
                            if (nonCritElapsedMs >= MaxNonCriticalBudgetMs)
                            {
                                _sliceCursor = (i + 1) % nonCritCount;
                                break;
                            }
                        }
                    }

                    if (scheduledNonCrit == 0 || (Stopwatch.GetTimestamp() - nonCritStartTick) * ticksToMs < MaxNonCriticalBudgetMs)
                    {
                        _sliceCursor = (startIndex + nonCritCount) % nonCritCount;
                    }
                }

                MFPProfiler.ActiveWidgetCount = activeCount;
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Widgets);
            }
        }

        private void ExecuteWidgetUpdate(WidgetRegistration reg, IFlightTelemetry telem, bool profileWidgets)
        {
            if (profileWidgets)
            {
                try
                {
                    MFPProfiler.BeginWidgetSample(reg.Widget.WidgetId, reg.Widget.DisplayName);
                    reg.Widget.MasterUpdateTelemetry(telem);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[ModularFlightPanel] Error in {reg.Widget.WidgetId}.MasterUpdateTelemetry: {ex.Message}");
                }
                finally
                {
                    MFPProfiler.EndWidgetSample(reg.Widget.WidgetId);
                }
            }
            else
            {
                try
                {
                    reg.Widget.MasterUpdateTelemetry(telem);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[ModularFlightPanel] Error in {reg.Widget.WidgetId}.MasterUpdateTelemetry: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 判定指定阶梯当前帧是否允许刷新（直接供 LateUpdate 离屏相机或特定调度复用，确保全链路节流对齐）
        /// 支持相位交错 (Phase Interleaving) 调度，杜绝同阶梯所有组件集中在同一帧触发造成的帧时间尖峰
        /// </summary>
        public bool ShouldUpdateTier(WidgetRefreshTier tier, float unscaledTime, ref float lastUpdateTime, float customInterval = 0f, int phaseOffset = 0)
        {
            // 0. Critical 级核心航电组件 (姿态球等) 强制 100% 满帧直通游戏 FPS，杜绝任何阶梯节流
            if (tier == WidgetRefreshTier.Critical)
            {
                lastUpdateTime = unscaledTime;
                return true;
            }

            // 1. 若组件定义了特定 UpdateInterval (或 CustomHz)，以组件自身设置为最高优先级
            if (customInterval > 0f)
            {
                if (unscaledTime - lastUpdateTime < customInterval) return false;
                lastUpdateTime = unscaledTime;
                return true;
            }

            // 2. 垂直同步模式 (完全同步当前游戏实际渲染帧率与帧同步节流)
            if (SyncWithGameFps)
            {
                bool allowed;
                switch (CurrentProfile)
                {
                    case GlobalRefreshProfile.Ultra60Hz:
                        allowed = true;
                        break;

                    case GlobalRefreshProfile.EcoPowerSaver:
                        switch (tier)
                        {
                            case WidgetRefreshTier.Critical: allowed = ((Time.frameCount + phaseOffset) % 2 == 0); break; // 1:2 降频 (如 60fps 时 30Hz)
                            case WidgetRefreshTier.Standard: allowed = ((Time.frameCount + phaseOffset) % 4 == 0); break; // 1:4 降频 (如 60fps 时 15Hz)
                            case WidgetRefreshTier.Relaxed:  allowed = ((Time.frameCount + phaseOffset) % 12 == 0); break; // 1:12 降频 (如 60fps 时 5Hz)
                            case WidgetRefreshTier.UltraLow: allowed = ((Time.frameCount + phaseOffset) % 30 == 0); break; // 1:30 降频 (如 60fps 时 2Hz)
                            default: allowed = ((Time.frameCount + phaseOffset) % 4 == 0); break;
                        }
                        break;

                    case GlobalRefreshProfile.Balanced:
                    default:
                        switch (tier)
                        {
                            case WidgetRefreshTier.Critical: allowed = true; break;                                       // 1:1 满帧垂直同步 (如 60/120/144Hz)
                            case WidgetRefreshTier.Standard: allowed = ((Time.frameCount + phaseOffset) % 2 == 0); break; // 1:2 垂直同步 (如 60fps 时 30Hz)
                            case WidgetRefreshTier.Relaxed:  allowed = ((Time.frameCount + phaseOffset) % 6 == 0); break; // 1:6 垂直同步 (如 60fps 时 10Hz)
                            case WidgetRefreshTier.UltraLow: allowed = ((Time.frameCount + phaseOffset) % 30 == 0); break;// 1:30 垂直同步 (约 2Hz)
                            default: allowed = ((Time.frameCount + phaseOffset) % 2 == 0); break;
                        }
                        break;
                }

                if (allowed)
                {
                    lastUpdateTime = unscaledTime;
                    return true;
                }
                return false;
            }

            // 3. 自由填写自定义 Hz 模式 (基于用户设定的目标 Hz 与未缩放时间戳精准节流)
            float targetHz = 30f;
            switch (tier)
            {
                case WidgetRefreshTier.Critical: targetHz = CriticalHz; break;
                case WidgetRefreshTier.Standard: targetHz = StandardHz; break;
                case WidgetRefreshTier.Relaxed:  targetHz = RelaxedHz; break;
                case WidgetRefreshTier.UltraLow: targetHz = UltraLowHz; break;
            }

            if (targetHz <= 0f) return true;
            float interval = 1f / Mathf.Max(0.1f, targetHz);
            if (unscaledTime - lastUpdateTime >= interval)
            {
                lastUpdateTime = unscaledTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判定当前组件是否应该刷新 (支持垂直同步跟随游戏实际帧率，或自由填写自定义 Hz)
        /// </summary>
        private bool ShouldUpdateWidget(WidgetRegistration reg, float unscaledTime)
        {
            return ShouldUpdateTier(reg.Tier, unscaledTime, ref reg.LastUpdateTime, reg.CustomInterval, reg.PhaseOffset);
        }

        /// <summary>
        /// 由 FlightHUDManager.LateUpdate 单点调用的后置处理分发
        /// </summary>
        public void MasterLateUpdate()
        {
            // 后置处理保留接口
        }

        #endregion
    }
}
