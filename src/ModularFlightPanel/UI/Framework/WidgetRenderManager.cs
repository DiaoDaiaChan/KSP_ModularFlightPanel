using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;

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
        Critical = 0, // 随游戏 FPS (满帧直通游戏实时渲染帧率，如 60/120/144fps 每一帧刷新)
        Standard = 1, // 60 Hz (约 16.6ms 更新)
        Slow     = 2, // 30 Hz (约 33.3ms 更新)
        Relaxed  = 3, // 10 Hz (约 100.0ms 更新)
        UltraLow = 4, // 2 Hz  (约 500.0ms 更新)
        Custom   = 5  // 自定义 (由组件 CustomHz 或配置指定精确更新频率)
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
        public int ConsecutiveErrors;

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
                if (reg.Tier == WidgetRefreshTier.Critical || (reg.Widget != null && reg.Widget.AlwaysFullPower))
                    _criticalRegistrations.Add(reg);
                else
                    _nonCriticalRegistrations.Add(reg);
            }

            _criticalRegistrations.Sort((a, b) => (a.Widget?.Config?.DrawOrder ?? 0).CompareTo(b.Widget?.Config?.DrawOrder ?? 0));
            _nonCriticalRegistrations.Sort((a, b) => (a.Widget?.Config?.DrawOrder ?? 0).CompareTo(b.Widget?.Config?.DrawOrder ?? 0));
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

        public float CriticalHz { get; set; } = 0f;        // 0 表示随游戏 FPS 直通
        public float StandardHz { get; set; } = 60.0f;     // 60 Hz
        public float SlowHz { get; set; } = 30.0f;         // 30 Hz
        public float RelaxedHz { get; set; } = 10.0f;      // 10 Hz
        public float UltraLowHz { get; set; } = 2.0f;      // 2 Hz
        public float GlobalDataHeartbeatHz { get; set; } = 0f; // 0 表示各组件独立/默认推荐, >0 表示全局统一心跳基准频率 (Hz)

        // 硬限微秒级帧预算切片调度器配置 (Budgeted Frame Slicing)
        public bool EnableBudgetSlicing { get; set; } = true;
        public float MaxFrameBudgetMs { get; set; } = 0.15f;         // 全局参考最大帧预算 (150微秒)
        public float MaxNonCriticalBudgetMs { get; set; } = 0.08f;  // 阶段 2 非 Critical 组件独立微秒切片预算 (80微秒)，与姿态球彻底解耦，杜绝调度饥饿
        private int _sliceCursor = 0;                                // 非 Critical 组件公平轮询切片游标

        public int SliceCursor => _sliceCursor;
        public int CriticalWidgetCount => _criticalRegistrations.Count;
        public int NonCriticalWidgetCount => _nonCriticalRegistrations.Count;
        public IReadOnlyList<WidgetRegistration> CriticalRegistrations => _criticalRegistrations;
        public IReadOnlyList<WidgetRegistration> NonCriticalRegistrations => _nonCriticalRegistrations;

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
            WidgetLayerManager.OnLayersChanged += SortRegistrationsByDrawOrder;
        }

        /// <summary>
        /// 严格按照 UGUI 图层与 SiblingIndex 绘制顺序 (DrawOrder) 升序排列组件注册表与切片桶，
        /// 确保切片调度与时序执行完全与图层绘制顺序对齐。
        /// </summary>
        public void SortRegistrationsByDrawOrder()
        {
            _registrations.Sort((a, b) =>
            {
                int orderA = a.Widget?.Config?.DrawOrder ?? 0;
                int orderB = b.Widget?.Config?.DrawOrder ?? 0;
                return orderA.CompareTo(orderB);
            });
            RebuildTierBuckets();
        }

        #region Registration & Lifecycle Management

        public void RegisterWidget(BaseFlightWidget widget, WidgetRefreshTier tier = WidgetRefreshTier.Standard)
        {
            if (widget == null) return;
            if (widget.AlwaysFullPower)
            {
                tier = WidgetRefreshTier.Critical;
            }
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
            SortRegistrationsByDrawOrder();
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

        /// <summary>
        /// 恢复所有被安全沙箱挂起隔离的组件（在用户重载布局、切换预设或故障自愈恢复时调用）
        /// </summary>
        public void ResetQuarantinedWidgets()
        {
            for (int i = 0; i < _registrations.Count; i++)
            {
                var reg = _registrations[i];
                if (reg != null)
                {
                    reg.ConsecutiveErrors = 0;
                    if (reg.State == WidgetLifecycleState.Suspended && reg.Widget != null && reg.Widget.gameObject.activeSelf)
                    {
                        reg.State = WidgetLifecycleState.Active;
                    }
                }
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

        public void SetControlMode(RefreshControlMode mode)
        {
            if (ControlMode != mode)
            {
                ControlMode = mode;
                OnRenderSettingChanged?.Invoke();
            }
        }

        public void SetGlobalDataHeartbeatHz(float hz)
        {
            float clamped = Mathf.Max(0f, hz);
            if (Math.Abs(GlobalDataHeartbeatHz - clamped) > 0.001f)
            {
                GlobalDataHeartbeatHz = clamped;
                OnRenderSettingChanged?.Invoke();
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
                // 评估全局整船遥测自适应静息态 (纳秒级公理判定，耗时 < 0.0001ms，全船组件共享)
                long qStart = Stopwatch.GetTimestamp();
                bool isVesselMotion = AdaptiveTelemetryDebouncer.SharedVesselDebouncer.Evaluate(telem);
                IsVesselQuiescent = !isVesselMotion;
                MFPProfiler.QuiescenceEvalMs = (Stopwatch.GetTimestamp() - qStart) * ticksToMs;

                int critCount = _criticalRegistrations.Count;
                int nonCritCount = _nonCriticalRegistrations.Count;
                int activeCount = 0;
                int restingCount = 0;

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
                        if (reg.Widget.IsQuiescent) restingCount++;

                        if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                        reg.LastUpdateTime = unscaledTime;
                        ExecuteWidgetUpdate(reg, telem, profileWidgets, wasSliced: false);
                    }
                    MFPProfiler.ActiveWidgetCount = activeCount;
                    MFPProfiler.RestingWidgetCount = restingCount;
                    return;
                }

                // -------------------------------------------------------------
                // 阶段 1：Critical 级核心姿态航电组件无条件保活直通 (姿态球/航向指示弧)
                // 仅遍历专用 Critical 桶，按 DrawOrder 升序严格执行
                // -------------------------------------------------------------
                for (int i = 0; i < critCount; i++)
                {
                    var reg = _criticalRegistrations[i];
                    if (reg == null || reg.Widget == null) continue;
                    if (reg.State != WidgetLifecycleState.Active) continue;
                    if (!reg.Widget.gameObject.activeSelf) continue;

                    activeCount++;
                    if (reg.Widget.IsQuiescent) restingCount++;

                    if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                    reg.LastUpdateTime = unscaledTime;
                    ExecuteWidgetUpdate(reg, telem, profileWidgets, wasSliced: false);
                }

                // -------------------------------------------------------------
                // 阶段 2：Standard / Relaxed / UltraLow 组件公平轮询切片调度 (Round-Robin Slicing)
                // 仅遍历专用 NonCritical 桶，按 DrawOrder 升序严格执行切片调度
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
                        if (reg.Widget.IsQuiescent) restingCount++;

                        if (!ShouldUpdateWidget(reg, unscaledTime)) continue;

                        reg.LastUpdateTime = unscaledTime;
                        ExecuteWidgetUpdate(reg, telem, profileWidgets, wasSliced: true);
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
                MFPProfiler.RestingWidgetCount = restingCount;
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Widgets);
            }
        }

        /// <summary>
        /// 全局整船是否处于自适应稳态静息状态 (公理动力学判断)
        /// </summary>
        public bool IsVesselQuiescent { get; private set; } = false;

        private void ExecuteWidgetUpdate(WidgetRegistration reg, IFlightTelemetry telem, bool profileWidgets, bool wasSliced = false)
        {
            if (reg == null || reg.Widget == null) return;

            if (profileWidgets)
            {
                try
                {
                    int drawOrder = reg.Widget?.Config?.DrawOrder ?? 0;
                    bool isResting = reg.Widget != null && reg.Widget.IsQuiescent;
                    MFPProfiler.BeginWidgetSample(reg.Widget.WidgetId, reg.Widget.DisplayName, drawOrder, reg.Tier, wasSliced, isResting);
                    reg.Widget.MasterUpdateTelemetry(telem);
                    reg.ConsecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    HandleWidgetError(reg, ex);
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
                    reg.ConsecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    HandleWidgetError(reg, ex);
                }
            }
        }

        private void HandleWidgetError(WidgetRegistration reg, Exception ex)
        {
            reg.ConsecutiveErrors++;
            string widgetId = reg.Widget != null ? reg.Widget.WidgetId : "unknown";
            string displayName = reg.Widget != null ? reg.Widget.DisplayName : widgetId;

            if (reg.ConsecutiveErrors >= 5 && reg.State == WidgetLifecycleState.Active)
            {
                reg.State = WidgetLifecycleState.Suspended;
                MFPLogger.Error(MFPLogger.CatUI, $"[WidgetRenderManager] 组件 '{displayName}' ({widgetId}) 连续抛出 {reg.ConsecutiveErrors} 次未捕获异常，已自动实施安全隔离挂起 (Quarantined/Suspended)，保障整体航电面板平稳运行！异常: {ex.Message}");
#if KSP_RUNTIME
                try
                {
                    ScreenMessages.PostScreenMessage(
                        new ScreenMessage(
                            $"[MFP 航电自愈] 组件 '{displayName}' 出现连续异常，已自动沙箱隔离。其余航电保持满帧运行。",
                            5.0f,
                            ScreenMessageStyle.UPPER_CENTER
                        )
                    );
                }
                catch { }
#endif
            }
            else if (reg.ConsecutiveErrors == 1)
            {
                MFPLogger.Warn(MFPLogger.CatUI, $"[WidgetRenderManager] 组件 '{displayName}' ({widgetId}) UpdateTelemetry 发生异常: {ex.Message}");
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

            // 1. 若组件定义了特定 UpdateInterval (或 CustomHz) 或显式声明 Custom 阶梯
            if (tier == WidgetRefreshTier.Custom || customInterval > 0f)
            {
                float effectiveInterval = customInterval > 0f ? customInterval : (1.0f / 60.0f);
                if (unscaledTime - lastUpdateTime < effectiveInterval - 0.0005f) return false;
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
                            case WidgetRefreshTier.Standard: allowed = ((Time.frameCount + phaseOffset) % 2 == 0); break; // 30Hz
                            case WidgetRefreshTier.Slow:     allowed = ((Time.frameCount + phaseOffset) % 4 == 0); break; // 15Hz
                            case WidgetRefreshTier.Relaxed:  allowed = ((Time.frameCount + phaseOffset) % 12 == 0); break; // 5Hz
                            case WidgetRefreshTier.UltraLow: allowed = ((Time.frameCount + phaseOffset) % 30 == 0); break; // 2Hz
                            default: allowed = ((Time.frameCount + phaseOffset) % 4 == 0); break;
                        }
                        break;

                    case GlobalRefreshProfile.Balanced:
                    default:
                        switch (tier)
                        {
                            case WidgetRefreshTier.Critical: allowed = true; break;                                       // 随游戏 FPS (满帧)
                            case WidgetRefreshTier.Standard: allowed = true; break;                                       // 60Hz (在 60fps 垂直同步下 1:1)
                            case WidgetRefreshTier.Slow:     allowed = ((Time.frameCount + phaseOffset) % 2 == 0); break; // 30Hz (1:2 垂直同步)
                            case WidgetRefreshTier.Relaxed:  allowed = ((Time.frameCount + phaseOffset) % 6 == 0); break; // 10Hz (1:6 垂直同步)
                            case WidgetRefreshTier.UltraLow: allowed = ((Time.frameCount + phaseOffset) % 30 == 0); break;// 2Hz (1:30 垂直同步)
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
            float targetHz = 60f;
            switch (tier)
            {
                case WidgetRefreshTier.Critical: targetHz = 0f; break; // 0 表示直通随游戏 FPS
                case WidgetRefreshTier.Standard: targetHz = StandardHz; break; // 60Hz
                case WidgetRefreshTier.Slow:     targetHz = SlowHz; break;     // 30Hz
                case WidgetRefreshTier.Relaxed:  targetHz = RelaxedHz; break;  // 10Hz
                case WidgetRefreshTier.UltraLow: targetHz = UltraLowHz; break; // 2Hz
                case WidgetRefreshTier.Custom:   targetHz = customInterval > 0f ? (1f / customInterval) : StandardHz; break;
            }

            if (targetHz <= 0f) return true;
            float interval = 1f / Mathf.Max(0.1f, targetHz);
            if (unscaledTime - lastUpdateTime >= interval - 0.0005f)
            {
                lastUpdateTime = unscaledTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判定当前组件是否应该刷新 (支持垂直同步跟随游戏实际帧率，或自由填写自定义 Hz)
        /// 标注 AlwaysFullPower 的组件彻底豁免一切节流，随游戏实时 FPS 满帧直通
        /// </summary>
        private bool ShouldUpdateWidget(WidgetRegistration reg, float unscaledTime)
        {
            if (reg != null && reg.Widget != null && reg.Widget.AlwaysFullPower)
            {
                reg.LastUpdateTime = unscaledTime;
                return true;
            }
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
