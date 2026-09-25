using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
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
    /// MFP 高精度硬件级性能分析探针与主干旁路控制器 (High-Precision Hardware Profiler & Master Bypass Controller)
    /// 使用 Stopwatch 纳秒级时钟计算 MFP 各子系统消耗的 FrameTime (ms) 与帧预算占比
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

        // 组件级负载监测 (Top Offending Widget)
        public static int ActiveWidgetCount { get; set; }
        public static string TopOffenderWidgetId { get; private set; } = "---";
        public static double TopOffenderWidgetMs { get; private set; }

        private static readonly Dictionary<string, long> _widgetStartTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static string _currentFrameTopWidgetId = "---";
        private static double _currentFrameTopWidgetMs = 0.0;

        public static void BeginWidgetSample(string widgetId)
        {
            if (_isMasterBypassed || string.IsNullOrEmpty(widgetId)) return;
            _widgetStartTicks[widgetId] = Stopwatch.GetTimestamp();
        }

        public static void EndWidgetSample(string widgetId)
        {
            if (_isMasterBypassed || string.IsNullOrEmpty(widgetId)) return;
            if (_widgetStartTicks.TryGetValue(widgetId, out long startTick))
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
                if (elapsedTicks > 0)
                {
                    double ms = elapsedTicks * TicksToMs;
                    if (ms > _currentFrameTopWidgetMs)
                    {
                        _currentFrameTopWidgetMs = ms;
                        _currentFrameTopWidgetId = widgetId;
                    }
                }
            }
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
        }

        // ------------------ Overlay 绘制控制 ------------------
        public static bool ShowOverlay { get; set; } = false;
        private static Rect _overlayRect = new Rect(16f, 16f, 320f, 130f);
        private static bool _showDetailedBreakdown = false;

        public static void BeginSample(ProfilerSection section)
        {
            if (_isMasterBypassed && section != ProfilerSection.TotalMFP) return;
            int idx = (int)section;
            _timings[idx].StartTick = Stopwatch.GetTimestamp();
        }

        public static void EndSample(ProfilerSection section)
        {
            if (_isMasterBypassed && section != ProfilerSection.TotalMFP) return;
            int idx = (int)section;
            long end = Stopwatch.GetTimestamp();
            long elapsedTicks = end - _timings[idx].StartTick;
            if (elapsedTicks > 0)
            {
                double ms = elapsedTicks * TicksToMs;
                _timings[idx].CurrentFrameMs = ms;
                _timings[idx].AccumulatedMs += ms;
            }
        }

        public static void BeginFrame()
        {
            BeginSample(ProfilerSection.TotalMFP);
        }

        public static void EndFrame()
        {
            EndSample(ProfilerSection.TotalMFP);

            // 采样宿主内存与 GC 频率
            try
            {
                TotalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                Gc0Collections = GC.CollectionCount(0);
                Gc1Collections = GC.CollectionCount(1);
                Gc2Collections = GC.CollectionCount(2);
            }
            catch { }

            TopOffenderWidgetId = _currentFrameTopWidgetId;
            TopOffenderWidgetMs = _currentFrameTopWidgetMs;
            _currentFrameTopWidgetId = "---";
            _currentFrameTopWidgetMs = 0.0;

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
                return;
            }

            LastTotalMs = _timings[(int)ProfilerSection.TotalMFP].CurrentFrameMs;

            // 写入环形缓冲区
            _historyTotalMs[_historyIndex] = LastTotalMs;
            _historyTelemetryMs[_historyIndex] = _timings[(int)ProfilerSection.Telemetry].AccumulatedMs;
            _historyProbesMs[_historyIndex] = _timings[(int)ProfilerSection.Probes].AccumulatedMs;
            _historyWidgetsMs[_historyIndex] = _timings[(int)ProfilerSection.Widgets].AccumulatedMs;
            _historySilhouetteMs[_historyIndex] = _timings[(int)ProfilerSection.Silhouette].AccumulatedMs;
            _historyHooksMs[_historyIndex] = _timings[(int)ProfilerSection.Hooks].AccumulatedMs;

            _historyIndex = (_historyIndex + 1) % HistorySize;
            if (_historyCount < HistorySize) _historyCount++;

            // 计算均值与极值
            double sumTotal = 0, sumTelem = 0, sumProbes = 0, sumWidgets = 0, sumSil = 0, sumHooks = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            int spikes = 0;

            for (int i = 0; i < _historyCount; i++)
            {
                double v = _historyTotalMs[i];
                sumTotal += v;
                if (v < min) min = v;
                if (v > max) max = v;

                sumTelem += _historyTelemetryMs[i];
                sumProbes += _historyProbesMs[i];
                sumWidgets += _historyWidgetsMs[i];
                sumSil += _historySilhouetteMs[i];
                sumHooks += _historyHooksMs[i];

                if (v > 16.6667) spikes++;
            }

            AvgTotalMs = sumTotal / _historyCount;
            MinTotalMs = min;
            MaxTotalMs = max;
            SpikeCount = spikes;

            AvgTelemetryMs = sumTelem / _historyCount;
            AvgProbesMs = sumProbes / _historyCount;
            AvgWidgetsMs = sumWidgets / _historyCount;
            AvgSilhouetteMs = sumSil / _historyCount;
            AvgHooksMs = sumHooks / _historyCount;

            FrameBudgetPercent = UnityFrameTimeMs > 0.001 ? (AvgTotalMs / UnityFrameTimeMs) * 100.0 : 0.0;

            // 采样统计结束后，重置累加器以供下一帧采样（确保 TelemetryHub 等先于 HUD 执行的脚本时间被完整计入，杜绝清零丢失）
            for (int i = 0; i < _timings.Length; i++)
            {
                _timings[i].AccumulatedMs = 0.0;
                _timings[i].CurrentFrameMs = 0.0;
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
            float h = _showDetailedBreakdown ? 185f : 88f;
            _overlayRect.height = h;

            _overlayRect = GUI.Window(923841, _overlayRect, DrawOverlayWindow, I18n.Tr("PROF_WINDOW_TITLE", "MFP 航电性能探针"));
            GUI.color = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
        }

        private static void DrawOverlayWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, 320, 20));

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

            // 展开的子系统详细耗时排查
            if (_showDetailedBreakdown && !_isMasterBypassed)
            {
                GUILayout.Space(4f);
                GUILayout.Box("", GUILayout.Height(1f), GUILayout.ExpandWidth(true)); // 分割线

                DrawStatRow(I18n.Tr("PROF_ROW_TELEMETRY", "遥测核心:"), AvgTelemetryMs);
                DrawStatRow(I18n.Tr("PROF_ROW_PROBES", "外部探针 (FAR/RA/MJ):"), AvgProbesMs);
                DrawStatRow(I18n.Tr("PROF_ROW_WIDGETS", "组件管线呈现:"), AvgWidgetsMs);
                DrawStatRow(I18n.Tr("PROF_ROW_SILHOUETTE", "飞船剪影烘焙:"), AvgSilhouetteMs);
                DrawStatRow(I18n.Tr("PROF_ROW_HOOKS", "原版界面挂钩:"), AvgHooksMs);
            }

            GUILayout.EndVertical();
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
