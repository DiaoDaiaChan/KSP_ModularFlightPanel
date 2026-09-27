using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.Core
{
    public enum ProfilerSection
    {
        Telemetry,
        Probes,
        Widgets,
        Silhouette,
        Hooks,
        TotalMFP
    }

    /// <summary>
    /// 单个航电组件的高精度性能分析数据包 (Microsecond Widget Profiling Data)
    /// </summary>
    public class WidgetProfileData
    {
        public string WidgetId;
        public string DisplayName;
        public double LastMs;
        public double AvgMs;
        public double MaxMs;
        public long StartTick;
        public int SampleCount;
        public int LastSampleFrame;
    }

    /// <summary>
    /// MFP 高精度硬件级性能分析探针与主干旁路控制器 (High-Precision Hardware Profiler & Master Bypass Controller)
    /// 1. 使用 Stopwatch 纳秒级时钟与 UnityEngine.Profiling.Profiler 双轨计算 MFP 各子系统 FrameTime (ms)
    /// 2. 精确采样 Update + LateUpdate 实测耗时，彻底杜绝跨帧统计污染
    /// 3. 支持组件级高精度负载监测 (Widget Breakdown)，实时追踪所有活跃组件的末次、均值与尖峰耗时
    /// </summary>
    public static class MFPProfiler
    {
        // ------------------ Master Bypass 控制 ------------------
        private static bool _isMasterBypassed = false;
        public static event Action<bool> OnMasterBypassChanged;

        /// <summary>
        /// 全局主干旁路开关：开启时完全切断 MFP 运算与渲染，100% 恢复原版 UI，MFP 帧耗时严格归零
        /// </summary>
        public static bool IsMasterBypassed
        {
            get => _isMasterBypassed;
            set
            {
                if (_isMasterBypassed != value)
                {
                    _isMasterBypassed = value;
                    OnMasterBypassChanged?.Invoke(_isMasterBypassed);
                    UnityEngine.Debug.Log($"[ModularFlightPanel] Master Bypass switched to: {(_isMasterBypassed ? "BYPASSED (Zero-Overhead Vanilla)" : "ACTIVE (Modular Avionics HUD)")}");
                }
            }
        }

        public static void ToggleMasterBypass()
        {
            IsMasterBypassed = !IsMasterBypassed;
        }

        // ------------------ 高精度耗时采样 ------------------
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private static readonly string[] _sectionSampleNames = new string[]
        {
            "MFP.Telemetry",
            "MFP.Probes",
            "MFP.Widgets",
            "MFP.Silhouette",
            "MFP.Hooks",
            "MFP.Total"
        };

        private struct SectionTiming
        {
            public long StartTick;
            public double CurrentFrameMs;
            public double AccumulatedMs;
        }

        private static readonly SectionTiming[] _timings = new SectionTiming[6];

        // 60 帧滑动窗口统计
        private const int HistorySize = 60;
        private static readonly double[] _historyTotalMs = new double[HistorySize];
        private static readonly double[] _historyTelemetryMs = new double[HistorySize];
        private static readonly double[] _historyProbesMs = new double[HistorySize];
        private static readonly double[] _historyWidgetsMs = new double[HistorySize];
        private static readonly double[] _historySilhouetteMs = new double[HistorySize];
        private static readonly double[] _historyHooksMs = new double[HistorySize];
        private static int _historyIndex = 0;
        private static int _historyCount = 0;
        private static double _runningSumTotal = 0.0;
        private static double _runningSumTelemetry = 0.0;
        private static double _runningSumProbes = 0.0;
        private static double _runningSumWidgets = 0.0;
        private static double _runningSumSilhouette = 0.0;
        private static double _runningSumHooks = 0.0;
        private static float _memSampleTimer = 0f;

        // 实时汇总统计属性 (供 UI/通配符/遥测面板读取)
        public static double LastTotalMs { get; private set; }
        public static double AvgTotalMs { get; private set; }
        public static double MinTotalMs { get; private set; }
        public static double MaxTotalMs { get; private set; }

        public static double AvgTelemetryMs { get; private set; }
        public static double AvgProbesMs { get; private set; }
        public static double AvgWidgetsMs { get; private set; }
        public static double AvgSilhouetteMs { get; private set; }
        public static double AvgHooksMs { get; private set; }

        public static double TelemetryPercent => AvgTotalMs > 0.0001 ? (AvgTelemetryMs / AvgTotalMs) * 100.0 : 0.0;
        public static double ProbesPercent => AvgTotalMs > 0.0001 ? (AvgProbesMs / AvgTotalMs) * 100.0 : 0.0;
        public static double WidgetsPercent => AvgTotalMs > 0.0001 ? (AvgWidgetsMs / AvgTotalMs) * 100.0 : 0.0;
        public static double SilhouettePercent => AvgTotalMs > 0.0001 ? (AvgSilhouetteMs / AvgTotalMs) * 100.0 : 0.0;
        public static double HooksPercent => AvgTotalMs > 0.0001 ? (AvgHooksMs / AvgTotalMs) * 100.0 : 0.0;

        public static float CurrentFPS { get; private set; }
        public static double FrameBudgetPercent { get; private set; } // MFP 占整帧渲染周期的百分比
        public static double UnityFrameTimeMs { get; private set; }
        public static int SpikeCount { get; private set; }

        // 内存与垃圾回收遥测
        public static double TotalMemoryMB { get; private set; }
        public static int Gc0Collections { get; private set; }
        public static int Gc1Collections { get; private set; }
        public static int Gc2Collections { get; private set; }

        // ------------------ 细化到每个组件的负载监测 (Per-Widget Breakdown) ------------------
        public static int ActiveWidgetCount { get; set; }
        public static string TopOffenderWidgetId { get; private set; } = "---";
        public static double TopOffenderWidgetMs { get; private set; }

        public static bool EnableWidgetProfiling { get; set; } = false;
        public static bool IsWidgetProfilingActive => !_isMasterBypassed && (ShowOverlay || EnableWidgetProfiling);

        private static readonly Dictionary<string, WidgetProfileData> _widgetProfiles = new Dictionary<string, WidgetProfileData>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WidgetProfileData> _sortedWidgetList = new List<WidgetProfileData>(64);
        public static IReadOnlyList<WidgetProfileData> ActiveWidgetProfiles => _sortedWidgetList;

        private static string _currentFrameTopWidgetId = "---";
        private static double _currentFrameTopWidgetMs = 0.0;

        public static void BeginWidgetSample(string widgetId, string displayName = null)
        {
            if (!IsWidgetProfilingActive || string.IsNullOrEmpty(widgetId)) return;

            if (!_widgetProfiles.TryGetValue(widgetId, out var data))
            {
                data = new WidgetProfileData
                {
                    WidgetId = widgetId,
                    DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : widgetId
                };
                _widgetProfiles[widgetId] = data;
            }
            else if (!string.IsNullOrEmpty(displayName) && data.DisplayName == data.WidgetId)
            {
                data.DisplayName = displayName;
            }

            data.StartTick = Stopwatch.GetTimestamp();
            Profiler.BeginSample(widgetId);
        }

        public static void EndWidgetSample(string widgetId)
        {
            if (!IsWidgetProfilingActive || string.IsNullOrEmpty(widgetId)) return;

            Profiler.EndSample();

            if (_widgetProfiles.TryGetValue(widgetId, out var data))
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - data.StartTick;
                if (elapsedTicks > 0)
                {
                    double ms = elapsedTicks * TicksToMs;
                    data.LastMs = ms;
                    data.LastSampleFrame = Time.frameCount;
                    if (ms > data.MaxMs) data.MaxMs = ms;

                    if (data.SampleCount == 0)
                        data.AvgMs = ms;
                    else
                        data.AvgMs = data.AvgMs * 0.90 + ms * 0.10; // 快速平滑指数衰减平均

                    data.SampleCount++;

                    if (ms > _currentFrameTopWidgetMs)
                    {
                        _currentFrameTopWidgetMs = ms;
                        _currentFrameTopWidgetId = !string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : widgetId;
                    }
                }
            }
        }

        /// <summary>
        /// 重置所有组件的历史峰值统计
        /// </summary>
        public static void ResetPeakStats()
        {
            foreach (var d in _widgetProfiles.Values)
            {
                d.MaxMs = d.LastMs;
            }
            MinTotalMs = AvgTotalMs;
            MaxTotalMs = AvgTotalMs;
            SpikeCount = 0;
        }

        /// <summary>
        /// 模拟/测试模式数据注入接口 (供无头渲染、自动化单元测试与装配台实时演示使用)
        /// </summary>
        public static void InjectSimulatedMetrics(double totalMs, double telemMs, double probesMs, double widgetsMs,
            double silMs, double hooksMs, float fps, double memMb, int gc0, int spikes)
        {
            AvgTotalMs = totalMs;
            LastTotalMs = totalMs;
            MinTotalMs = totalMs * 0.85;
            MaxTotalMs = totalMs * 1.45;

            AvgTelemetryMs = telemMs;
            AvgProbesMs = probesMs;
            AvgWidgetsMs = widgetsMs;
            AvgSilhouetteMs = silMs;
            AvgHooksMs = hooksMs;

            CurrentFPS = fps;
            UnityFrameTimeMs = fps > 0f ? (1000.0 / fps) : 16.6667;
            FrameBudgetPercent = UnityFrameTimeMs > 0.001 ? (totalMs / UnityFrameTimeMs) * 100.0 : 0.0;

            TotalMemoryMB = memMb;
            Gc0Collections = gc0;
            SpikeCount = spikes;

            InjectSimulatedWidget("custom.navball", I18n.Tr("WIDGET_NAVBALL_TITLE", "NAVBALL 姿态航向球"), widgetsMs * 0.45, widgetsMs * 0.50, widgetsMs * 0.70);
            InjectSimulatedWidget("custom.stage_dv", I18n.Tr("WIDGET_STAGE_DV_TITLE", "STAGE ΔV 本级推演仪表"), widgetsMs * 0.25, widgetsMs * 0.22, widgetsMs * 0.35);
            InjectSimulatedWidget("custom.altitude", I18n.Tr("WIDGET_ALTITUDE_TITLE", "ALTITUDE 混合高度带"), widgetsMs * 0.15, widgetsMs * 0.14, widgetsMs * 0.20);
            InjectSimulatedWidget("custom.orbit_info", I18n.Tr("WIDGET_ORBIT_TITLE", "ORBIT 轨道六根数态势卡"), widgetsMs * 0.10, widgetsMs * 0.09, widgetsMs * 0.15);
            InjectSimulatedWidget("custom.signal", I18n.Tr("WIDGET_SIGNAL_TITLE", "COMMNET 天线通信网络"), widgetsMs * 0.05, widgetsMs * 0.05, widgetsMs * 0.08);

            _sortedWidgetList.Clear();
            _sortedWidgetList.AddRange(_widgetProfiles.Values);
            _sortedWidgetList.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            if (_sortedWidgetList.Count > 0)
            {
                TopOffenderWidgetId = _sortedWidgetList[0].DisplayName;
                TopOffenderWidgetMs = _sortedWidgetList[0].AvgMs;
            }
        }

        private static void InjectSimulatedWidget(string id, string name, double lastMs, double avgMs, double maxMs)
        {
            _widgetProfiles[id] = new WidgetProfileData
            {
                WidgetId = id,
                DisplayName = name,
                LastMs = lastMs,
                AvgMs = avgMs,
                MaxMs = maxMs,
                SampleCount = 100,
                LastSampleFrame = Time.frameCount
            };
        }

        // ------------------ Overlay 绘制控制 ------------------
        public static bool ShowOverlay { get; set; } = false;
        private static Rect _overlayRect = new Rect(16f, 16f, 320f, 130f);
        private static bool _showDetailedBreakdown = false;
        private static Vector2 _widgetScrollPos = Vector2.zero;

        public static void BeginSample(ProfilerSection section)
        {
            if (_isMasterBypassed && section != ProfilerSection.TotalMFP) return;
            int idx = (int)section;
            _timings[idx].StartTick = Stopwatch.GetTimestamp();
            Profiler.BeginSample(_sectionSampleNames[idx]);
        }

        public static void EndSample(ProfilerSection section)
        {
            if (_isMasterBypassed && section != ProfilerSection.TotalMFP) return;
            int idx = (int)section;
            Profiler.EndSample();
            long end = Stopwatch.GetTimestamp();
            long elapsedTicks = end - _timings[idx].StartTick;
            if (elapsedTicks > 0)
            {
                double ms = elapsedTicks * TicksToMs;
                _timings[idx].CurrentFrameMs += ms;
                _timings[idx].AccumulatedMs += ms;
            }
        }

        public static void BeginFrame()
        {
            // 单帧开始：清零当前帧局部采样与累加值，确保每帧采样边界严谨隔离
            _currentFrameTopWidgetId = "---";
            _currentFrameTopWidgetMs = 0.0;
            for (int i = 0; i < _timings.Length; i++)
            {
                _timings[i].CurrentFrameMs = 0.0;
                _timings[i].AccumulatedMs = 0.0;
            }
        }

        public static void EndFrame()
        {
            // 节流采样宿主内存与 GC 频率 (每 1.0 秒执行一次，杜绝每帧遍历 CLR 堆造成的显著 CPU 开销)
            _memSampleTimer += Time.unscaledDeltaTime;
            if (_memSampleTimer >= 1.0f)
            {
                _memSampleTimer = 0f;
                try
                {
                    TotalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                    Gc0Collections = GC.CollectionCount(0);
                    Gc1Collections = GC.CollectionCount(1);
                    Gc2Collections = GC.CollectionCount(2);
                }
                catch { }
            }

            TopOffenderWidgetId = _currentFrameTopWidgetId;
            TopOffenderWidgetMs = _currentFrameTopWidgetMs;

            float dt = Time.unscaledDeltaTime;
            UnityFrameTimeMs = dt * 1000.0;
            CurrentFPS = dt > 0.0001f ? 1.0f / dt : 0f;

            if (_isMasterBypassed)
            {
                LastTotalMs = 0.0;
                AvgTotalMs = 0.0;
                AvgTelemetryMs = 0.0;
                AvgProbesMs = 0.0;
                AvgWidgetsMs = 0.0;
                AvgSilhouetteMs = 0.0;
                AvgHooksMs = 0.0;
                FrameBudgetPercent = 0.0;
                SpikeCount = 0;
                _runningSumTotal = 0.0;
                _runningSumTelemetry = 0.0;
                _runningSumProbes = 0.0;
                _runningSumWidgets = 0.0;
                _runningSumSilhouette = 0.0;
                _runningSumHooks = 0.0;
                _sortedWidgetList.Clear();
                return;
            }

            double curTelem = _timings[(int)ProfilerSection.Telemetry].AccumulatedMs;
            double curProbes = _timings[(int)ProfilerSection.Probes].AccumulatedMs;
            double curWidgets = _timings[(int)ProfilerSection.Widgets].AccumulatedMs;
            double curSil = _timings[(int)ProfilerSection.Silhouette].AccumulatedMs;
            double curHooks = _timings[(int)ProfilerSection.Hooks].AccumulatedMs;
            double measuredTotal = _timings[(int)ProfilerSection.TotalMFP].AccumulatedMs;

            // 真实 MFP CPU 耗时：由 Update + LateUpdate 实测耗时与各子系统耗时并集严格校准
            double curTotal = Math.Max(measuredTotal, curTelem + curProbes + curWidgets + curSil + curHooks);
            LastTotalMs = curTotal;

            double oldTotal = _historyTotalMs[_historyIndex];
            double oldTelem = _historyTelemetryMs[_historyIndex];
            double oldProbes = _historyProbesMs[_historyIndex];
            double oldWidgets = _historyWidgetsMs[_historyIndex];
            double oldSil = _historySilhouetteMs[_historyIndex];
            double oldHooks = _historyHooksMs[_historyIndex];

            // 写入环形缓冲区
            _historyTotalMs[_historyIndex] = curTotal;
            _historyTelemetryMs[_historyIndex] = curTelem;
            _historyProbesMs[_historyIndex] = curProbes;
            _historyWidgetsMs[_historyIndex] = curWidgets;
            _historySilhouetteMs[_historyIndex] = curSil;
            _historyHooksMs[_historyIndex] = curHooks;

            _runningSumTotal += curTotal - oldTotal;
            _runningSumTelemetry += curTelem - oldTelem;
            _runningSumProbes += curProbes - oldProbes;
            _runningSumWidgets += curWidgets - oldWidgets;
            _runningSumSilhouette += curSil - oldSil;
            _runningSumHooks += curHooks - oldHooks;

            _historyIndex = (_historyIndex + 1) % HistorySize;
            if (_historyCount < HistorySize) _historyCount++;

            AvgTotalMs = _runningSumTotal / _historyCount;
            AvgTelemetryMs = _runningSumTelemetry / _historyCount;
            AvgProbesMs = _runningSumProbes / _historyCount;
            AvgWidgetsMs = _runningSumWidgets / _historyCount;
            AvgSilhouetteMs = _runningSumSilhouette / _historyCount;
            AvgHooksMs = _runningSumHooks / _historyCount;

            FrameBudgetPercent = UnityFrameTimeMs > 0.001 ? (AvgTotalMs / UnityFrameTimeMs) * 100.0 : 0.0;

            // 维护活跃组件降序列表 (供悬浮 HUD 与监控屏读取，节流至每 10 帧排序一次，杜绝每帧 GC 与 CPU 尖峰)
            if ((ShowOverlay || EnableWidgetProfiling) && Time.frameCount % 10 == 0)
            {
                _sortedWidgetList.Clear();
                int currentFrame = Time.frameCount;
                foreach (var kvp in _widgetProfiles)
                {
                    var d = kvp.Value;
                    if (currentFrame - d.LastSampleFrame < 180) // 保持 3 秒内活跃过的组件
                    {
                        _sortedWidgetList.Add(d);
                    }
                }
                _sortedWidgetList.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            }

            // 仅在开启详细覆盖层时遍历极值与尖峰，彻底消除主循环常驻开销
            if (ShowOverlay)
            {
                double min = double.MaxValue;
                double max = double.MinValue;
                int spikes = 0;
                for (int i = 0; i < _historyCount; i++)
                {
                    double v = _historyTotalMs[i];
                    if (v < min) min = v;
                    if (v > max) max = v;
                    if (v > 16.6667) spikes++;
                }
                MinTotalMs = min;
                MaxTotalMs = max;
                SpikeCount = spikes;
            }
        }

        /// <summary>
        /// 绘制高性能悬浮 HUD 状态徽章 (可快捷键 F10 开启，或 Alt+N 设置内开启)
        /// </summary>
        public static void DrawGUI()
        {
            if (!ShowOverlay) return;

            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            GUI.color = theme != null ? (Color)theme.FrameBgColor : GUI.contentColor;
            float w = _showDetailedBreakdown ? 390f : 320f;
            float h = _showDetailedBreakdown ? 390f : 88f;
            _overlayRect.width = w;
            _overlayRect.height = h;

            _overlayRect = GUI.Window(923841, _overlayRect, DrawOverlayWindow, I18n.Tr("PROF_WINDOW_TITLE", "MFP 航电性能探针"));
            GUI.color = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
        }

        private static void DrawOverlayWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, 390, 20));

            GUILayout.BeginVertical();

            // 核心统计行
            GUILayout.BeginHorizontal();
            string statusColor = _isMasterBypassed ? "#FF3B30" : (AvgTotalMs < 0.5 ? "#00E5FF" : (AvgTotalMs < 1.5 ? "#FFCC00" : "#FF3B30"));
            string bypassText = _isMasterBypassed ? "<color=#FF3B30><b>[BYPASSED 0.00ms]</b></color>" : $"<color={statusColor}><b>{AvgTotalMs:F2} ms</b></color> ({FrameBudgetPercent:F1}%)";
            GUILayout.Label($"<b>{I18n.Tr("PROF_TIME_COST", "MFP 耗时:")}</b> {bypassText}", GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>FPS:</b> {CurrentFPS:F0}", GUILayout.Width(65f));
            GUILayout.EndHorizontal();

            // 操作控制行
            GUILayout.BeginHorizontal();
            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            string bypassBtnLabel = _isMasterBypassed ? I18n.Tr("PROF_BTN_RESUME", "▶ 恢复 MFP") : I18n.Tr("PROF_BTN_BYPASS", "⏸ 完全旁路 (F11)");
            GUI.color = _isMasterBypassed
                ? (theme != null ? (Color)theme.AccentPrimary : GUI.contentColor)
                : (theme != null ? (Color)theme.WarningColor : GUI.contentColor);
            if (GUILayout.Button(bypassBtnLabel, GUILayout.Height(22f)))
            {
                ToggleMasterBypass();
            }
            GUI.color = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;

            if (GUILayout.Button(_showDetailedBreakdown ? I18n.Tr("PROF_BTN_COLLAPSE", "▲ 收起") : I18n.Tr("PROF_BTN_DETAILS", "▼ 详情"), GUILayout.Width(60f), GUILayout.Height(22f)))
            {
                _showDetailedBreakdown = !_showDetailedBreakdown;
            }

            if (GUILayout.Button("✕", GUILayout.Width(25f), GUILayout.Height(22f)))
            {
                ShowOverlay = false;
            }
            GUILayout.EndHorizontal();

            // 展开的子系统详细耗时与逐组件明细
            if (_showDetailedBreakdown && !_isMasterBypassed)
            {
                GUILayout.Space(4f);
                GUILayout.Box("", GUILayout.Height(1f), GUILayout.ExpandWidth(true)); // 分割线

                DrawStatRow(I18n.Tr("PROF_ROW_TELEMETRY", "遥测核心:"), AvgTelemetryMs);
                DrawStatRow(I18n.Tr("PROF_ROW_PROBES", "外部探针 (FAR/RA/MJ):"), AvgProbesMs);
                DrawStatRow(I18n.Tr("PROF_ROW_WIDGETS", "组件管线呈现:"), AvgWidgetsMs);
                DrawStatRow(I18n.Tr("PROF_ROW_SILHOUETTE", "飞船剪影烘焙:"), AvgSilhouetteMs);
                DrawStatRow(I18n.Tr("PROF_ROW_HOOKS", "原版界面挂钩:"), AvgHooksMs);

                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                int activeCount = _sortedWidgetList.Count;
                GUILayout.Label($"<b>{I18n.Tr("PROF_WIDGET_BREAKDOWN", "组件耗时明细 (Widget Breakdown)")}</b> ({activeCount})", GUILayout.ExpandWidth(true));
                if (GUILayout.Button(I18n.Tr("PROF_BTN_RESET_PEAK", "重置峰值"), GUILayout.Width(72f), GUILayout.Height(20f)))
                {
                    ResetPeakStats();
                }
                GUILayout.EndHorizontal();

                // 表头
                GUILayout.BeginHorizontal();
                DrawHeaderCell(I18n.Tr("PROF_COL_NAME", "组件名称 / ID"), 190f);
                DrawHeaderCell(I18n.Tr("PROF_COL_AVG", "均值"), 75f);
                DrawHeaderCell(I18n.Tr("PROF_COL_LAST", "实时 / 峰值"), -1f);
                GUILayout.EndHorizontal();

                _widgetScrollPos = GUILayout.BeginScrollView(_widgetScrollPos, GUILayout.Height(150f));
                for (int i = 0; i < activeCount; i++)
                {
                    var w = _sortedWidgetList[i];
                    GUILayout.BeginHorizontal();
                    string displayName = !string.IsNullOrEmpty(w.DisplayName) && w.DisplayName != w.WidgetId
                        ? $"{w.DisplayName}"
                        : w.WidgetId;
                    string tooltip = $"{w.WidgetId}\n{I18n.Tr("PROF_TOOLTIP_LAST", "末次")}: {w.LastMs:F3} ms\n{I18n.Tr("PROF_TOOLTIP_AVG", "均值")}: {w.AvgMs:F3} ms\n{I18n.Tr("PROF_TOOLTIP_MAX", "峰值")}: {w.MaxMs:F3} ms";
                    GUILayout.Label(new GUIContent($"<color=#D0D0D0>{displayName}</color>", tooltip), GUILayout.Width(190f));

                    string color = w.AvgMs < 0.1 ? "#00E5FF" : (w.AvgMs < 0.4 ? "#FFE000" : "#FF5555");
                    GUILayout.Label($"<color={color}><b>{w.AvgMs:F3} ms</b></color>", GUILayout.Width(75f));
                    GUILayout.Label($"<color=#888888>{w.LastMs:F2} / {w.MaxMs:F2}</color>", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
                if (activeCount == 0)
                {
                    GUILayout.Label(I18n.Tr("PROF_NO_ACTIVE_WIDGETS", "<i>无活跃组件刷新采样...</i>"));
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private static void DrawHeaderCell(string text, float width)
        {
            if (width > 0f)
                GUILayout.Label($"<color=#888888>{text}</color>", GUILayout.Width(width));
            else
                GUILayout.Label($"<color=#888888>{text}</color>", GUILayout.ExpandWidth(true));
        }

        private static void DrawStatRow(string label, double ms)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#AAAAAA>{label}</color>", GUILayout.Width(180f));
            string color = ms < 0.2 ? "#00E5FF" : (ms < 0.8 ? "#FFE000" : "#FF5555");
            GUILayout.Label($"<color={color}><b>{ms:F3} ms</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }
    }
}
