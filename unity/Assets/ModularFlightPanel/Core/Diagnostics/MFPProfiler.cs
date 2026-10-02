using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

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
        public double LastMs;        // 最近一次单次执行耗时 (Exec Last)
        public double AvgMs;         // 单次触发指数平滑均值 (Exec Avg)
        public double FrameAvgMs;    // 60 帧每帧等效平摊耗时 (Frame Avg: 对齐组件管线呈现)
        public double MaxMs;
        public long StartTick;
        public int SampleCount;
        public int LastSampleFrame;
        public bool IsResting;       // 当前是否处于自适应静息节流状态 (Auto-Quiescent / Resting)

        // 内部 60 帧环形缓冲区与当前帧累加 (零 GC，避免每帧堆分配)
        public double CurrentFrameAccumMs;
        public readonly double[] HistoryFrameMs = new double[60];
        public int HistoryCount;
    }

    /// <summary>
    /// 单个航电组件在一帧内的更新时序节点 (Microsecond Widget Timeline Entry)
    /// 记录其在 UGUI Hierarchy 的图层绘制顺序 (DrawOrder)、刷新阶梯、切片调度状态以及起止时间戳
    /// </summary>
    public class WidgetTimelineEntry
    {
        public string WidgetId;
        public string DisplayName;
        public int DrawOrder;
        public WidgetRefreshTier Tier;
        public bool WasSliced;
        public bool IsResting;       // 是否处于静息节流状态
        public double StartOffsetMs;
        public double DurationMs;
        public int ExecutionIndex;
        public long StartTick;

        public double EndOffsetMs => StartOffsetMs + DurationMs;
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
        public static int RestingWidgetCount { get; set; }
        public static double QuiescenceEvalMs { get; set; }
        public static string TopOffenderWidgetId { get; private set; } = "---";
        public static double TopOffenderWidgetMs { get; private set; }

        public static bool EnableWidgetProfiling { get; set; } = false;
        public static bool IsWidgetProfilingActive => !_isMasterBypassed && (ShowOverlay || EnableWidgetProfiling);

        private static readonly Dictionary<string, WidgetProfileData> _widgetProfiles = new Dictionary<string, WidgetProfileData>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WidgetProfileData> _sortedWidgetList = new List<WidgetProfileData>(64);
        public static IReadOnlyList<WidgetProfileData> ActiveWidgetProfiles => _sortedWidgetList;

        private static string _currentFrameTopWidgetId = "---";
        private static double _currentFrameTopWidgetMs = 0.0;

        // ------------------ UI 更新时序时间轴 (Widget Update Timeline) ------------------
        private static readonly List<WidgetTimelineEntry> _currentFrameTimeline = new List<WidgetTimelineEntry>(64);
        private static readonly List<WidgetTimelineEntry> _snapshotTimeline = new List<WidgetTimelineEntry>(64);
        private static readonly List<WidgetTimelineEntry> _entryPool = new List<WidgetTimelineEntry>(64);
        private static readonly List<WidgetTimelineEntry> _snapshotTimelinePool = new List<WidgetTimelineEntry>(64);
        private static int _poolAllocIndex = 0;
        private static long _timelineStartTick = 0;

        public static bool IsTimelineFrozen { get; set; } = false;
        public static int SnapshotFrameCount { get; private set; }
        public static double SnapshotTotalWidgetsMs { get; private set; }
        public static int SnapshotSliceCursor { get; private set; }
        public static float SnapshotMaxBudgetMs { get; private set; } = 0.08f;
        public static int SnapshotCritCount { get; private set; }
        public static int SnapshotNonCritCount { get; private set; }
        public static IReadOnlyList<WidgetTimelineEntry> SnapshotTimeline => _snapshotTimeline;

        private static int _selectedTab = 0; // 0: 概览明细, 1: 更新时序时间轴
        private static Vector2 _timelineScrollPos = Vector2.zero;

        private static WidgetTimelineEntry AcquireEntry()
        {
            if (_poolAllocIndex < _entryPool.Count)
            {
                return _entryPool[_poolAllocIndex++];
            }
            var entry = new WidgetTimelineEntry();
            _entryPool.Add(entry);
            _poolAllocIndex++;
            return entry;
        }

        private static WidgetTimelineEntry AcquireSnapshotEntry(int index)
        {
            while (_snapshotTimelinePool.Count <= index)
            {
                _snapshotTimelinePool.Add(new WidgetTimelineEntry());
            }
            return _snapshotTimelinePool[index];
        }

        public static void BeginWidgetSample(string widgetId, string displayName = null, int drawOrder = -1, WidgetRefreshTier tier = WidgetRefreshTier.Standard, bool wasSliced = false, bool isResting = false)
        {
            if (!IsWidgetProfilingActive || string.IsNullOrEmpty(widgetId)) return;

            if (!_widgetProfiles.TryGetValue(widgetId, out var data))
            {
                data = new WidgetProfileData
                {
                    WidgetId = widgetId,
                    DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : widgetId,
                    IsResting = isResting
                };
                _widgetProfiles[widgetId] = data;
            }
            else
            {
                data.IsResting = isResting;
                if (!string.IsNullOrEmpty(displayName) && data.DisplayName == data.WidgetId)
                {
                    data.DisplayName = displayName;
                }
            }

            long nowTick = Stopwatch.GetTimestamp();
            data.StartTick = nowTick;
            Profiler.BeginSample(widgetId);

            // 记录当前帧时序时间轴节点
            if (_timelineStartTick == 0)
            {
                _timelineStartTick = nowTick;
            }
            double offsetMs = Math.Max(0.0, (nowTick - _timelineStartTick) * TicksToMs);

            var entry = AcquireEntry();
            entry.WidgetId = widgetId;
            entry.DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : widgetId;
            entry.DrawOrder = drawOrder >= 0 ? drawOrder : 0;
            entry.Tier = tier;
            entry.WasSliced = wasSliced;
            entry.IsResting = isResting;
            entry.StartOffsetMs = offsetMs;
            entry.DurationMs = 0.0;
            entry.ExecutionIndex = _currentFrameTimeline.Count + 1;
            entry.StartTick = nowTick;

            _currentFrameTimeline.Add(entry);
        }

        public static void EndWidgetSample(string widgetId)
        {
            if (!IsWidgetProfilingActive || string.IsNullOrEmpty(widgetId)) return;

            Profiler.EndSample();

            long endTick = Stopwatch.GetTimestamp();
            if (_widgetProfiles.TryGetValue(widgetId, out var data))
            {
                long elapsedTicks = endTick - data.StartTick;
                if (elapsedTicks > 0)
                {
                    double ms = elapsedTicks * TicksToMs;
                    data.LastMs = ms;
                    data.LastSampleFrame = Time.frameCount;
                    data.CurrentFrameAccumMs += ms;
                    if (ms > data.MaxMs) data.MaxMs = ms;

                    if (data.SampleCount == 0)
                        data.AvgMs = ms;
                    else
                        data.AvgMs = Math.Max(0.0, data.AvgMs * 0.90 + ms * 0.10); // 快速平滑指数衰减平均

                    data.SampleCount++;

                    if (ms > _currentFrameTopWidgetMs)
                    {
                        _currentFrameTopWidgetMs = ms;
                        _currentFrameTopWidgetId = !string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : widgetId;
                    }
                }
            }

            if (_currentFrameTimeline.Count > 0)
            {
                for (int i = _currentFrameTimeline.Count - 1; i >= 0; i--)
                {
                    var entry = _currentFrameTimeline[i];
                    if (entry.WidgetId == widgetId)
                    {
                        long elapsedTicks = endTick - entry.StartTick;
                        entry.DurationMs = elapsedTicks > 0 ? elapsedTicks * TicksToMs : 0.0;
                        break;
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

            // 注入时序时间轴模拟数据
            _snapshotTimeline.Clear();
            AddSimulatedTimelineEntry("custom.navball", I18n.Tr("WIDGET_NAVBALL_TITLE", "NAVBALL 姿态航向球"), 0, WidgetRefreshTier.Critical, false, 0.000, widgetsMs * 0.45, 1);
            AddSimulatedTimelineEntry("custom.stage_dv", I18n.Tr("WIDGET_STAGE_DV_TITLE", "STAGE ΔV 本级推演仪表"), 1, WidgetRefreshTier.Standard, true, widgetsMs * 0.45, widgetsMs * 0.25, 2);
            AddSimulatedTimelineEntry("custom.altitude", I18n.Tr("WIDGET_ALTITUDE_TITLE", "ALTITUDE 混合高度带"), 2, WidgetRefreshTier.Standard, true, widgetsMs * 0.70, widgetsMs * 0.15, 3);
            AddSimulatedTimelineEntry("custom.orbit_info", I18n.Tr("WIDGET_ORBIT_TITLE", "ORBIT 轨道六根数态势卡"), 3, WidgetRefreshTier.Relaxed, true, widgetsMs * 0.85, widgetsMs * 0.10, 4);
            AddSimulatedTimelineEntry("custom.signal", I18n.Tr("WIDGET_SIGNAL_TITLE", "COMMNET 天线通信网络"), 4, WidgetRefreshTier.UltraLow, true, widgetsMs * 0.95, widgetsMs * 0.05, 5);

            SnapshotFrameCount = 100;
            SnapshotTotalWidgetsMs = widgetsMs;
            SnapshotSliceCursor = 2;
            SnapshotMaxBudgetMs = 0.08f;
            SnapshotCritCount = 1;
            SnapshotNonCritCount = 4;

            _sortedWidgetList.Clear();
            _sortedWidgetList.AddRange(_widgetProfiles.Values);
            _sortedWidgetList.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            if (_sortedWidgetList.Count > 0)
            {
                TopOffenderWidgetId = _sortedWidgetList[0].DisplayName;
                TopOffenderWidgetMs = _sortedWidgetList[0].AvgMs;
            }
        }

        private static void AddSimulatedTimelineEntry(string id, string name, int drawOrder, WidgetRefreshTier tier, bool wasSliced, double offsetMs, double durationMs, int seq)
        {
            var entry = AcquireSnapshotEntry(_snapshotTimeline.Count);
            entry.WidgetId = id;
            entry.DisplayName = name;
            entry.DrawOrder = drawOrder;
            entry.Tier = tier;
            entry.WasSliced = wasSliced;
            entry.StartOffsetMs = offsetMs;
            entry.DurationMs = durationMs;
            entry.ExecutionIndex = seq;
            _snapshotTimeline.Add(entry);
        }

        private static void InjectSimulatedWidget(string id, string name, double lastMs, double avgMs, double maxMs)
        {
            var data = new WidgetProfileData
            {
                WidgetId = id,
                DisplayName = name,
                LastMs = lastMs,
                AvgMs = avgMs,
                FrameAvgMs = avgMs,
                MaxMs = maxMs,
                SampleCount = 100,
                LastSampleFrame = Time.frameCount
            };
            for (int i = 0; i < HistorySize; i++)
            {
                data.HistoryFrameMs[i] = avgMs;
            }
            data.HistoryCount = HistorySize;
            _widgetProfiles[id] = data;
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
            _poolAllocIndex = 0;
            _currentFrameTimeline.Clear();
            _timelineStartTick = 0;

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
            TopOffenderWidgetMs = Math.Max(0.0, _currentFrameTopWidgetMs);

            float dt = Time.unscaledDeltaTime;
            UnityFrameTimeMs = Math.Max(0.001, dt * 1000.0);
            CurrentFPS = dt > 0.0001f ? Math.Max(0f, 1.0f / dt) : 0f;

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
                _historyCount = 0;
                _historyIndex = 0;
                Array.Clear(_historyTotalMs, 0, HistorySize);
                Array.Clear(_historyTelemetryMs, 0, HistorySize);
                Array.Clear(_historyProbesMs, 0, HistorySize);
                Array.Clear(_historyWidgetsMs, 0, HistorySize);
                Array.Clear(_historySilhouetteMs, 0, HistorySize);
                Array.Clear(_historyHooksMs, 0, HistorySize);
                _sortedWidgetList.Clear();
                return;
            }

            double curTelem = Math.Max(0.0, _timings[(int)ProfilerSection.Telemetry].AccumulatedMs);
            double curProbes = Math.Max(0.0, _timings[(int)ProfilerSection.Probes].AccumulatedMs);
            double curWidgets = Math.Max(0.0, _timings[(int)ProfilerSection.Widgets].AccumulatedMs);
            double curSil = Math.Max(0.0, _timings[(int)ProfilerSection.Silhouette].AccumulatedMs);
            double curHooks = Math.Max(0.0, _timings[(int)ProfilerSection.Hooks].AccumulatedMs);
            double measuredTotal = Math.Max(0.0, _timings[(int)ProfilerSection.TotalMFP].AccumulatedMs);

            // 真实 MFP CPU 耗时：由 Update + LateUpdate 实测耗时与各子系统耗时之和严格校准，保证总耗时不低于子项之和
            double subSum = curTelem + curProbes + curWidgets + curSil + curHooks;
            double curTotal = Math.Max(measuredTotal, subSum);
            LastTotalMs = curTotal;

            // 写入环形缓冲区当前槽位
            _historyTotalMs[_historyIndex] = curTotal;
            _historyTelemetryMs[_historyIndex] = curTelem;
            _historyProbesMs[_historyIndex] = curProbes;
            _historyWidgetsMs[_historyIndex] = curWidgets;
            _historySilhouetteMs[_historyIndex] = curSil;
            _historyHooksMs[_historyIndex] = curHooks;

            // 同步写入每个活跃组件的当前帧耗时槽位并清空帧累加器
            if ((ShowOverlay || EnableWidgetProfiling) && _widgetProfiles.Count > 0)
            {
                int currentFrame = Time.frameCount;
                foreach (var kvp in _widgetProfiles)
                {
                    var d = kvp.Value;
                    if (currentFrame - d.LastSampleFrame < 180)
                    {
                        d.HistoryFrameMs[_historyIndex] = d.CurrentFrameAccumMs;
                        if (d.HistoryCount < HistorySize) d.HistoryCount++;
                    }
                    else
                    {
                        d.HistoryFrameMs[_historyIndex] = 0.0;
                    }
                    d.CurrentFrameAccumMs = 0.0;
                }
            }

            _historyIndex = (_historyIndex + 1) % HistorySize;
            if (_historyCount < HistorySize) _historyCount++;

            // 核心修复：直接无漂移快速求和 (Zero-Drift Direct Summation)
            // 彻底废除增量加减导致的 IEEE 754 精度漂移，杜绝 -0.001 ms 与总耗时下溢萎缩
            double sumTotal = 0.0;
            double sumTelem = 0.0;
            double sumProbes = 0.0;
            double sumWidgets = 0.0;
            double sumSil = 0.0;
            double sumHooks = 0.0;

            for (int i = 0; i < _historyCount; i++)
            {
                sumTotal += _historyTotalMs[i];
                sumTelem += _historyTelemetryMs[i];
                sumProbes += _historyProbesMs[i];
                sumWidgets += _historyWidgetsMs[i];
                sumSil += _historySilhouetteMs[i];
                sumHooks += _historyHooksMs[i];
            }

            int count = Math.Max(1, _historyCount);
            AvgTelemetryMs = Math.Max(0.0, sumTelem / count);
            AvgProbesMs = Math.Max(0.0, sumProbes / count);
            AvgWidgetsMs = Math.Max(0.0, sumWidgets / count);
            AvgSilhouetteMs = Math.Max(0.0, sumSil / count);
            AvgHooksMs = Math.Max(0.0, sumHooks / count);

            // 物理守恒：总耗时必须 >= 各子模块求和
            double calcAvgTotal = sumTotal / count;
            double calcSubSum = AvgTelemetryMs + AvgProbesMs + AvgWidgetsMs + AvgSilhouetteMs + AvgHooksMs;
            AvgTotalMs = Math.Max(0.0, Math.Max(calcAvgTotal, calcSubSum));

            FrameBudgetPercent = UnityFrameTimeMs > 0.001 ? Math.Max(0.0, (AvgTotalMs / UnityFrameTimeMs) * 100.0) : 0.0;

            // 维护活跃组件降序列表 (节流至每 5 帧排序一次)
            if ((ShowOverlay || EnableWidgetProfiling) && Time.frameCount % 5 == 0)
            {
                _sortedWidgetList.Clear();
                int currentFrame = Time.frameCount;
                foreach (var kvp in _widgetProfiles)
                {
                    var d = kvp.Value;
                    if (currentFrame - d.LastSampleFrame < 180)
                    {
                        // 计算该组件的 60 帧每帧平摊耗时 (FrameAvgMs)
                        double sumW = 0.0;
                        for (int j = 0; j < _historyCount; j++)
                        {
                            sumW += d.HistoryFrameMs[j];
                        }
                        d.FrameAvgMs = Math.Max(0.0, sumW / count);
                        _sortedWidgetList.Add(d);
                    }
                }
                // 优先按每帧平摊耗时排序，若平摊耗时相同则按单次触发均值排序
                _sortedWidgetList.Sort((a, b) =>
                {
                    int cmp = b.FrameAvgMs.CompareTo(a.FrameAvgMs);
                    return cmp != 0 ? cmp : b.AvgMs.CompareTo(a.AvgMs);
                });
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
                MinTotalMs = _historyCount > 0 ? Math.Max(0.0, min) : 0.0;
                MaxTotalMs = _historyCount > 0 ? Math.Max(0.0, max) : 0.0;
                SpikeCount = spikes;
            }

            // 同步时序时间轴快照 (零 GC 预热对象池复用)
            if (!IsTimelineFrozen && (ShowOverlay || EnableWidgetProfiling))
            {
                _snapshotTimeline.Clear();
                for (int i = 0; i < _currentFrameTimeline.Count; i++)
                {
                    var src = _currentFrameTimeline[i];
                    var dst = AcquireSnapshotEntry(i);
                    dst.WidgetId = src.WidgetId;
                    dst.DisplayName = src.DisplayName;
                    dst.DrawOrder = src.DrawOrder;
                    dst.Tier = src.Tier;
                    dst.WasSliced = src.WasSliced;
                    dst.IsResting = src.IsResting;
                    dst.StartOffsetMs = src.StartOffsetMs;
                    dst.DurationMs = src.DurationMs;
                    dst.ExecutionIndex = src.ExecutionIndex;
                    _snapshotTimeline.Add(dst);
                }

                SnapshotFrameCount = Time.frameCount;
                SnapshotTotalWidgetsMs = curWidgets;
                SnapshotSliceCursor = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.SliceCursor : 0;
                SnapshotMaxBudgetMs = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.MaxNonCriticalBudgetMs : 0.08f;
                SnapshotCritCount = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.CriticalWidgetCount : 0;
                SnapshotNonCritCount = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.NonCriticalWidgetCount : 0;
            }
        }

        /// <summary>
        /// 捕获单帧时序快照 (供冻结状态下逐帧步进分析使用)
        /// </summary>
        public static void CaptureSingleStepSnapshot()
        {
            _snapshotTimeline.Clear();
            for (int i = 0; i < _currentFrameTimeline.Count; i++)
            {
                var src = _currentFrameTimeline[i];
                var dst = AcquireSnapshotEntry(i);
                dst.WidgetId = src.WidgetId;
                dst.DisplayName = src.DisplayName;
                dst.DrawOrder = src.DrawOrder;
                dst.Tier = src.Tier;
                dst.WasSliced = src.WasSliced;
                dst.IsResting = src.IsResting;
                dst.StartOffsetMs = src.StartOffsetMs;
                dst.DurationMs = src.DurationMs;
                dst.ExecutionIndex = src.ExecutionIndex;
                _snapshotTimeline.Add(dst);
            }

            SnapshotFrameCount = Time.frameCount;
            SnapshotTotalWidgetsMs = _timings[(int)ProfilerSection.Widgets].AccumulatedMs;
            SnapshotSliceCursor = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.SliceCursor : 0;
            SnapshotMaxBudgetMs = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.MaxNonCriticalBudgetMs : 0.08f;
            SnapshotCritCount = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.CriticalWidgetCount : 0;
            SnapshotNonCritCount = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.NonCriticalWidgetCount : 0;
        }

        /// <summary>
        /// 绘制高性能悬浮 HUD 状态徽章 (可快捷键 F10 开启，或 Alt+N 设置内开启)
        /// </summary>
        public static void DrawGUI()
        {
            if (!ShowOverlay) return;

            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            GUI.color = theme != null ? (Color)theme.FrameBgColor : GUI.contentColor;
            float w = _showDetailedBreakdown ? 430f : 320f;
            float h = _showDetailedBreakdown ? 440f : 88f;
            _overlayRect.width = w;
            _overlayRect.height = h;

            _overlayRect = GUI.Window(923841, _overlayRect, DrawOverlayWindow, I18n.Tr("PROF_WINDOW_TITLE", "MFP 航电性能探针"));
            GUI.color = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
        }

        private static void DrawOverlayWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _overlayRect.width, 20));

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

            // 展开的子系统详细耗时与逐组件明细 / 时序时间轴
            if (_showDetailedBreakdown && !_isMasterBypassed)
            {
                GUILayout.Space(4f);
                GUILayout.Box("", GUILayout.Height(1f), GUILayout.ExpandWidth(true)); // 分割线

                // Tab 切换导航: [📊 负载概览] | [⏱️ 更新时序轴]
                GUILayout.BeginHorizontal();
                GUI.color = _selectedTab == 0 ? (theme != null ? (Color)theme.AccentPrimary : Color.cyan) : GUI.contentColor;
                if (GUILayout.Button(I18n.Tr("PROF_TAB_OVERVIEW", "📊 负载概览"), GUILayout.Height(22f)))
                {
                    _selectedTab = 0;
                }
                GUI.color = _selectedTab == 1 ? (theme != null ? (Color)theme.AccentPrimary : Color.cyan) : GUI.contentColor;
                if (GUILayout.Button(I18n.Tr("PROF_TAB_TIMELINE", "⏱️ 更新时序轴"), GUILayout.Height(22f)))
                {
                    _selectedTab = 1;
                }
                GUI.color = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
                GUILayout.EndHorizontal();

                if (_selectedTab == 0)
                {
                    DrawOverviewTab(theme);
                }
                else
                {
                    DrawTimelineTab(theme);
                }
            }

            GUILayout.EndVertical();
        }

        private static void DrawOverviewTab(ThemeConfig theme)
        {
            GUILayout.Space(4f);
            DrawStatRow(I18n.Tr("PROF_ROW_TELEMETRY", "遥测核心:"), AvgTelemetryMs);
            DrawStatRow(I18n.Tr("PROF_ROW_PROBES", "外部探针 (FAR/RA/MJ):"), AvgProbesMs);
            DrawStatRow(I18n.Tr("PROF_ROW_WIDGETS", "组件管线呈现:"), AvgWidgetsMs);
            DrawStatRow(I18n.Tr("PROF_ROW_SILHOUETTE", "飞船剪影烘焙:"), AvgSilhouetteMs);
            DrawStatRow(I18n.Tr("PROF_ROW_HOOKS", "原版界面挂钩:"), AvgHooksMs);

            int totalActive = Math.Max(1, ActiveWidgetCount);
            int resting = RestingWidgetCount;
            double restingPct = (double)resting / totalActive * 100.0;
            string restColor = resting > 0 ? "#00E5FF" : "#888888";
            DrawStatRow(I18n.Tr("PROF_ROW_QUIESCENCE", "自适应静息节流:"), $"{resting} / {totalActive} ({restingPct:F0}%)", restColor);

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
            DrawHeaderCell(I18n.Tr("PROF_COL_LAST", "单次 / 峰值"), -1f);
            GUILayout.EndHorizontal();

            _widgetScrollPos = GUILayout.BeginScrollView(_widgetScrollPos, GUILayout.Height(150f));
            for (int i = 0; i < activeCount; i++)
            {
                var w = _sortedWidgetList[i];
                GUILayout.BeginHorizontal();
                string displayName = !string.IsNullOrEmpty(w.DisplayName) && w.DisplayName != w.WidgetId
                    ? $"{w.DisplayName}"
                    : w.WidgetId;
                if (w.IsResting)
                {
                    displayName += $" <color=#00E5FF>{I18n.Tr("PROF_STATUS_RESTING", "[静息]")}</color>";
                }
                double frameMs = Math.Max(0.0, w.FrameAvgMs);
                double execAvg = Math.Max(0.0, w.AvgMs);
                double lastMs = Math.Max(0.0, w.LastMs);
                double maxMs = Math.Max(0.0, w.MaxMs);
                string tooltip = $"{w.WidgetId}\n" +
                                 $"{I18n.Tr("PROF_TOOLTIP_FRAME_AVG", "每帧平摊")}: {frameMs:F3} ms\n" +
                                 $"{I18n.Tr("PROF_TOOLTIP_EXEC_AVG", "单次触发")}: {execAvg:F3} ms\n" +
                                 $"{I18n.Tr("PROF_TOOLTIP_LAST", "实时单次")}: {lastMs:F3} ms\n" +
                                 $"{I18n.Tr("PROF_TOOLTIP_MAX", "历史峰值")}: {maxMs:F3} ms";
                GUILayout.Label(new GUIContent($"<color=#D0D0D0>{displayName}</color>", tooltip), GUILayout.Width(190f));

                string color = frameMs < 0.05 ? "#00E5FF" : (frameMs < 0.2 ? "#34C759" : (frameMs < 0.5 ? "#FFE000" : "#FF5555"));
                GUILayout.Label($"<color={color}><b>{frameMs:F3} ms</b></color>", GUILayout.Width(75f));
                GUILayout.Label($"<color=#888888>{execAvg:F2} / {maxMs:F2}</color>", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            if (activeCount == 0)
            {
                GUILayout.Label(I18n.Tr("PROF_NO_ACTIVE_WIDGETS", "<i>无活跃组件刷新采样...</i>"));
            }
            GUILayout.EndScrollView();
        }

        private static void DrawTimelineTab(ThemeConfig theme)
        {
            GUILayout.Space(4f);

            // 1. 状态与冻结控制栏
            GUILayout.BeginHorizontal();
            string statusTag = IsTimelineFrozen
                ? $"<color=#FFCC00><b>{I18n.Tr("PROF_STATUS_FROZEN", "[已冻结]")}</b></color>"
                : $"<color=#00E5FF><b>{I18n.Tr("PROF_STATUS_LIVE", "[实时跟踪]")}</b></color>";
            GUILayout.Label($"<b>{I18n.Tr("PROF_TIMELINE_TITLE", "UI 更新时序与切片调度")}</b> {statusTag}", GUILayout.ExpandWidth(true));

            string freezeBtnLabel = IsTimelineFrozen ? I18n.Tr("PROF_BTN_LIVE", "▶ 跟踪") : I18n.Tr("PROF_BTN_FREEZE", "⏸ 冻结");
            if (GUILayout.Button(freezeBtnLabel, GUILayout.Width(54f), GUILayout.Height(20f)))
            {
                IsTimelineFrozen = !IsTimelineFrozen;
            }
            if (IsTimelineFrozen)
            {
                if (GUILayout.Button(I18n.Tr("PROF_BTN_STEP", "↺ 单步"), GUILayout.Width(46f), GUILayout.Height(20f)))
                {
                    CaptureSingleStepSnapshot();
                }
            }
            GUILayout.EndHorizontal();

            // 2. 切片调度与预算摘要
            GUILayout.BeginHorizontal();
            int executedCount = _snapshotTimeline.Count;
            GUILayout.Label($"<color=#AAAAAA>{I18n.Tr("PROF_STAGE1_CRIT", "阶段1 满帧:")}</color> <color=#00E5FF>{SnapshotCritCount}</color>  " +
                            $"<color=#AAAAAA>{I18n.Tr("PROF_STAGE2_SLICE", "阶段2 切片:")}</color> <color=#34C759>{Math.Max(0, executedCount - SnapshotCritCount)}</color>/{SnapshotNonCritCount}  " +
                            $"<color=#AAAAAA>{I18n.Tr("PROF_CURSOR_INDEX", "游标:")}</color> <color=#FFCC00>#{SnapshotSliceCursor}</color>  " +
                            $"<color=#AAAAAA>{I18n.Tr("PROF_BUDGET_LIMIT", "预算:")}</color> <color=#FF8800>{SnapshotMaxBudgetMs:F2}ms</color>",
                            GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 3. 水平时序甘特图 (Visual Timeline Bar)
            DrawVisualTimelineBar();

            GUILayout.Space(4f);

            // 4. 时序执行与图层排序明细表头
            GUILayout.BeginHorizontal();
            DrawHeaderCell(I18n.Tr("PROF_COL_SEQ", "#"), 24f);
            DrawHeaderCell(I18n.Tr("PROF_COL_LAYER", "图层"), 38f);
            DrawHeaderCell(I18n.Tr("PROF_COL_TIER", "阶梯"), 42f);
            DrawHeaderCell(I18n.Tr("PROF_COL_NAME", "组件名称 / ID"), 145f);
            DrawHeaderCell(I18n.Tr("PROF_COL_TIMING", "时序区间"), 75f);
            DrawHeaderCell(I18n.Tr("PROF_COL_DURATION", "耗时"), -1f);
            GUILayout.EndHorizontal();

            // 5. 滚动明细列表 (按本帧实际执行时序排列，并标注 UGUI DrawOrder 图层)
            _timelineScrollPos = GUILayout.BeginScrollView(_timelineScrollPos, GUILayout.Height(150f));
            for (int i = 0; i < executedCount; i++)
            {
                var entry = _snapshotTimeline[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#888888>{entry.ExecutionIndex}</color>", GUILayout.Width(24f));

                string layerColor = entry.DrawOrder < 3 ? "#00E5FF" : (entry.DrawOrder < 8 ? "#34C759" : "#FFCC00");
                GUILayout.Label($"<color={layerColor}>{I18n.TrFormat("PROF_LAYER_TAG_FMT", entry.DrawOrder)}</color>", GUILayout.Width(38f));

                string tierName = GetTierShortName(entry.Tier);
                string tierColor = GetTierColor(entry.Tier);
                GUILayout.Label($"<color={tierColor}>{tierName}</color>", GUILayout.Width(42f));

                string name = !string.IsNullOrEmpty(entry.DisplayName) ? entry.DisplayName : entry.WidgetId;
                string tooltip = $"{entry.WidgetId}\n{I18n.Tr("PROF_COL_LAYER", "图层")}: L{entry.DrawOrder}\n{I18n.Tr("PROF_COL_TIMING", "时序")}: +{entry.StartOffsetMs:F3}ms ~ +{entry.EndOffsetMs:F3}ms\n{I18n.Tr("PROF_COL_DURATION", "耗时")}: {entry.DurationMs:F3}ms";
                GUILayout.Label(new GUIContent($"<color=#D0D0D0>{name}</color>", tooltip), GUILayout.Width(145f));

                GUILayout.Label($"<color=#888888>+{entry.StartOffsetMs:F2}ms</color>", GUILayout.Width(75f));

                string costColor = entry.DurationMs < 0.05 ? "#00E5FF" : (entry.DurationMs < 0.20 ? "#FFE000" : "#FF5555");
                string sliceTag = entry.WasSliced
                    ? $"<color=#34C759>{I18n.Tr("PROF_STATUS_SLICED", "[切片]")}</color>"
                    : $"<color=#00E5FF>{I18n.Tr("PROF_STATUS_FULL_PASS", "[直通]")}</color>";
                if (entry.IsResting)
                {
                    sliceTag += $" <color=#00E5FF>{I18n.Tr("PROF_STATUS_RESTING", "[静息]")}</color>";
                }
                GUILayout.Label($"<color={costColor}><b>{entry.DurationMs:F3}</b></color> {sliceTag}", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            if (executedCount == 0)
            {
                GUILayout.Label(I18n.Tr("PROF_TIMELINE_EMPTY", "<i>本帧无组件执行更新...</i>"));
            }
            GUILayout.EndScrollView();
        }

        private static void DrawVisualTimelineBar()
        {
            Rect barRect = GUILayoutUtility.GetRect(400f, 24f, GUILayout.ExpandWidth(true));
            GUI.Box(barRect, GUIContent.none);

            double totalSpan = Math.Max(0.10, SnapshotTotalWidgetsMs > 0.001 ? SnapshotTotalWidgetsMs * 1.15 : 0.10);
            if (_snapshotTimeline.Count > 0)
            {
                double lastEnd = _snapshotTimeline[_snapshotTimeline.Count - 1].EndOffsetMs;
                if (lastEnd > totalSpan) totalSpan = lastEnd * 1.05;
            }

            Color prevColor = GUI.color;
            for (int i = 0; i < _snapshotTimeline.Count; i++)
            {
                var entry = _snapshotTimeline[i];
                float startX = barRect.x + 2f + (float)(entry.StartOffsetMs / totalSpan) * (barRect.width - 4f);
                float blockW = Mathf.Max(3f, (float)(entry.DurationMs / totalSpan) * (barRect.width - 4f));
                if (startX + blockW > barRect.xMax - 2f)
                {
                    blockW = Mathf.Max(2f, barRect.xMax - 2f - startX);
                }

                Rect blockRect = new Rect(startX, barRect.y + 2f, blockW, barRect.height - 4f);
                GUI.color = GetTierColorValue(entry.Tier);
                GUI.DrawTexture(blockRect, Texture2D.whiteTexture);

                if (blockW >= 22f)
                {
                    GUI.color = Color.black;
                    GUI.Label(new Rect(blockRect.x, blockRect.y, blockRect.width, blockRect.height), $"#{entry.ExecutionIndex}", GUI.skin.label);
                }
            }

            // 绘制切片预算硬限阈值线 (例如 0.08ms)
            if (SnapshotMaxBudgetMs > 0f && SnapshotMaxBudgetMs < totalSpan)
            {
                float budgetX = barRect.x + 2f + (float)(SnapshotMaxBudgetMs / totalSpan) * (barRect.width - 4f);
                Rect lineRect = new Rect(budgetX - 1f, barRect.y, 2f, barRect.height);
                GUI.color = new Color(1f, 0.3f, 0.2f, 0.9f);
                GUI.DrawTexture(lineRect, Texture2D.whiteTexture);
            }

            GUI.color = prevColor;

            // 标尺读数行
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#666666>0.00ms</color>", GUILayout.Width(60f));
            GUILayout.Label($"<color=#FF5555>| {I18n.Tr("PROF_BUDGET_LIMIT", "预算:")} {SnapshotMaxBudgetMs:F2}ms</color>", GUILayout.ExpandWidth(true));
            GUILayout.Label($"<color=#666666>{totalSpan:F2}ms</color>", GUILayout.Width(60f));
            GUILayout.EndHorizontal();
        }

        public static string GetTierShortName(WidgetRefreshTier tier)
        {
            switch (tier)
            {
                case WidgetRefreshTier.Critical: return "CRIT";
                case WidgetRefreshTier.Standard: return "STD";
                case WidgetRefreshTier.Slow: return "SLOW";
                case WidgetRefreshTier.Relaxed: return "RLX";
                case WidgetRefreshTier.UltraLow: return "LOW";
                case WidgetRefreshTier.Custom: return "CUST";
                default: return "TIER";
            }
        }

        public static string GetTierColor(WidgetRefreshTier tier)
        {
            switch (tier)
            {
                case WidgetRefreshTier.Critical: return "#00E5FF";
                case WidgetRefreshTier.Standard: return "#34C759";
                case WidgetRefreshTier.Slow: return "#FFE000";
                case WidgetRefreshTier.Relaxed: return "#FFCC00";
                case WidgetRefreshTier.UltraLow: return "#FF9500";
                case WidgetRefreshTier.Custom: return "#AF52DE";
                default: return "#FFFFFF";
            }
        }

        public static Color GetTierColorValue(WidgetRefreshTier tier)
        {
            switch (tier)
            {
                case WidgetRefreshTier.Critical: return new Color(0f, 0.9f, 1f, 0.85f);
                case WidgetRefreshTier.Standard: return new Color(0.2f, 0.8f, 0.35f, 0.85f);
                case WidgetRefreshTier.Slow: return new Color(1f, 0.88f, 0f, 0.85f);
                case WidgetRefreshTier.Relaxed: return new Color(1f, 0.8f, 0f, 0.85f);
                case WidgetRefreshTier.UltraLow: return new Color(1f, 0.58f, 0f, 0.85f);
                case WidgetRefreshTier.Custom: return new Color(0.7f, 0.3f, 0.9f, 0.85f);
                default: return new Color(0.8f, 0.8f, 0.8f, 0.85f);
            }
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
            ms = Math.Max(0.0, ms);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#AAAAAA>{label}</color>", GUILayout.Width(180f));
            string color = ms < 0.2 ? "#00E5FF" : (ms < 0.8 ? "#FFE000" : "#FF5555");
            GUILayout.Label($"<color={color}><b>{ms:F3} ms</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        private static void DrawStatRow(string label, string customValue, string valueColor = "#00E5FF")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#AAAAAA>{label}</color>", GUILayout.Width(180f));
            GUILayout.Label($"<color={valueColor}><b>{customValue}</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }
    }
}
