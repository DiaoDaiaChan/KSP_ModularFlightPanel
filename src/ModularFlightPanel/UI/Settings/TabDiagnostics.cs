using System;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新航电效能诊断与遥测仿真中枢 (Avionics Diagnostics & Simulation Sandbox - TabDiagnostics)
    /// 核心功能：
    /// 1. 遥测解耦物理仿真沙盒：原地测试全舱动态仪表，7 大典型飞行工况瞬时跳转与连续物理时序回放。
    /// 2. 航电效能雷达：实时捕获总管线耗时 (AvgTotalMs)、FPS 帧率、性能 HUD 悬浮层 (F10)。
    /// 3. 安全熔断与紧急旁路：航电熔断状态机监控、故障异常分析、一键重试恢复与 Master Bypass (F11) 纯净原版接管。
    /// 4. 规范自检与主干校验：游戏内快速执行 AST 规范与颜色/死区审计。
    /// </summary>
    public class TabDiagnostics : ISettingsTab
    {
        public string TabId => "diagnostics";
        public string DisplayTitle => I18n.Tr("UI_TAB_DIAGNOSTICS", "🚀 诊断与沙盒");

        private Vector2 _scrollPos = Vector2.zero;
        private string _lastSpecAuditSummary = I18n.Tr("THM_SPEC_NOT_RUN", "未运行 (含源码级规范审计)");

        public void OnEnter()
        {
            _scrollPos = Vector2.zero;
        }
        public void OnExit() { }

        public void Draw(float availableHeight)
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(availableHeight));

            // =========================================================================
            // 卡片 1: 遥测解耦物理仿真沙盒 (Simulation Sandbox)
            // =========================================================================
            DrawSimulationSandboxCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 2: 航电效能雷达与耗时剖析 (Performance Radar & Profiler)
            // =========================================================================
            DrawPerformanceRadarCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 3: 安全熔断排障与原版旁路 (Safety Fallback & Master Bypass)
            // =========================================================================
            DrawSafetyFallbackAndBypassCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 4: 规范门禁与系统自检 (Avionics Specification Validator)
            // =========================================================================
            DrawSpecValidatorCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Card 1: Simulation Sandbox

        private void DrawSimulationSandboxCard()
        {
            if (TelemetryHub.Instance == null)
            {
                MFPGuiSkin.BeginCard();
                GUILayout.Label(I18n.Tr("SIM_INITIALIZING_HUB", "<color=#7088A8>正在初始化 TelemetryHub...</color>"));
                MFPGuiSkin.EndCard();
                return;
            }

            TelemetryHub hub = TelemetryHub.Instance;
            TelemetrySimulationEngine sim = hub.SimulationEngine;

            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("SIM_HEADER", "🚀 遥测解耦物理仿真沙盒"),
                I18n.Tr("SIM_SUBHEADER", "点击即可将全船动力学推演至真实工况，原地检验仪表读数")
            );

            // 仿真模式总开关
            bool isSim = hub.IsSimulationMode;
            GUIStyle simBtnStyle = isSim ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string simToggleBtn = isSim ? I18n.Tr("SIM_BTN_ACTIVE", "▶ [仿真模式已激活] 正在喂送高保真物理仿真流") : I18n.Tr("SIM_BTN_INACTIVE", "▶ [开启遥测仿真模式] 原地测试全量动态仪表");
            if (GUILayout.Button(simToggleBtn, simBtnStyle, GUILayout.Height(30f), GUILayout.ExpandWidth(true)))
            {
                hub.IsSimulationMode = !hub.IsSimulationMode;
            }

            GUILayout.Space(4f);

            // 7 大飞行工况快速跳转
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_PAD", "🚀 发射预备\n<size=10>0m/s | 1G | 满油</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PadHold);
            }
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_ASCENT", "⚡ 音障爬升\n<size=10>340m/s | 动压爬升</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.AscentTransonic);
            }
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_MAXQ", "💥 Max-Q 极限\n<size=10>Q=34kPa | 3.5G</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MaxQ);
            }
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_MECO", "🔄 关机分级\n<size=10>下级点火 | 级间切分</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MECOAndStaging);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_ORBIT", "🌐 入轨微重力\n<size=10>120km | 0G | 净充电</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.OrbitalCruise);
            }
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_POWER", "🌑 暗面断电\n<size=10>无光照 | 电压跌破</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PowerCrisis);
            }
            if (GUILayout.Button(I18n.Tr("SIM_SCENARIO_REENTRY", "🔥 黑障再入\n<size=10>-420m/s | 断网离线</size>"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.ReentryBlackout);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 连续物理时间轴控制器
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            string playBtn = sim.IsPlaying ? I18n.Tr("SIM_BTN_PAUSE", "❚❚ 暂停推演") : I18n.Tr("SIM_BTN_PLAY", "▶ 继续播放");
            GUIStyle pStyle = sim.IsPlaying ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.WarningButtonStyle;
            if (GUILayout.Button(playBtn, pStyle, GUILayout.Width(100f), GUILayout.Height(22f)))
            {
                sim.IsPlaying = !sim.IsPlaying;
            }

            GUILayout.Label($"<b>{I18n.Tr("SIM_LABEL_PROGRESS", "推演进度:")}</b> {sim.TimelineTime:F1}s / {sim.MaxTimelineTime:F0}s", GUILayout.Width(160f));

            GUILayout.Label(I18n.Tr("SIM_LABEL_SPEED", "倍速:"), GUILayout.Width(35f));
            float[] speeds = new float[] { 0.5f, 1.0f, 2.0f, 5.0f };
            for (int i = 0; i < speeds.Length; i++)
            {
                bool isSel = Mathf.Approximately(sim.PlaybackSpeed, speeds[i]);
                GUIStyle sStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button($"{speeds[i]}x", sStyle, GUILayout.Width(36f), GUILayout.Height(20f)))
                {
                    sim.PlaybackSpeed = speeds[i];
                }
            }
            GUILayout.EndHorizontal();

            float newTime = GUILayout.HorizontalSlider(sim.TimelineTime, 0f, sim.MaxTimelineTime);
            if (Math.Abs(newTime - sim.TimelineTime) > 0.1f)
            {
                sim.TimelineTime = newTime;
            }
            MFPGuiSkin.EndInset();

            // 即时物理读数快照
            GUILayout.Space(3f);
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{I18n.Tr("SIM_MON_SPD", "地表速度:")} <color=#00E5FF><b>{hub.SurfaceSpeed:F1} m/s</b></color> (Mach {hub.Mach:F2})", GUILayout.Width(220f));
            GUILayout.Label($"{I18n.Tr("SIM_MON_ALT", "显示高度:")} <color=#00FF88><b>{hub.DisplayAltitude:F0} m</b></color>", GUILayout.Width(180f));
            GUILayout.Label($"{I18n.Tr("SIM_MON_VSPD", "垂直速度:")} <color=#00E5FF><b>{hub.VerticalSpeed:F1} m/s</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{I18n.Tr("SIM_MON_Q", "大气动压:")} <color=#FFAA00><b>{hub.DynamicPressure:F1} kPa</b></color>", GUILayout.Width(220f));
            GUILayout.Label($"{I18n.Tr("SIM_MON_G", "过载 G力:")} <color=#FFAA00><b>{hub.GForce:F2} G</b></color>", GUILayout.Width(180f));
            GUILayout.Label($"{I18n.Tr("SIM_MON_TWR", "推重比:")} <color=#00FF88><b>{hub.TWR:F2}</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 2: Performance Radar

        private void DrawPerformanceRadarCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("THM_HEADER_PROFILER", "⚡ 航电效能雷达与耗时剖析"),
                I18n.Tr("DIAG_PROFILER_SUB", "极低 CPU 占用与零 GC 遥测流动保障")
            );

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();

            double totalMs = MFPProfiler.AvgTotalMs;
            float fps = MFPProfiler.CurrentFPS;
            string msCol = totalMs < 0.5 ? MFPGuiSkin.HexAccentGreen : (totalMs < 1.5 ? MFPGuiSkin.HexAccentAmber : MFPGuiSkin.HexAccentRed);
            string fpsCol = fps >= 55f ? MFPGuiSkin.HexAccentGreen : (fps >= 30f ? MFPGuiSkin.HexAccentAmber : MFPGuiSkin.HexAccentRed);

            GUILayout.Label(string.Format("<b>{0}</b> <color=#{1}><b>{2:F2} ms</b></color>", I18n.Tr("DIAG_LABEL_RENDER_TIME", "MFP 渲染耗时:"), msCol, totalMs), GUILayout.Width(180f));
            GUILayout.Label(string.Format("<b>{0}</b> <color=#{1}><b>{2:F0} FPS</b></color>", I18n.Tr("DIAG_LABEL_CURRENT_FPS", "当前帧率:"), fpsCol, fps), GUILayout.Width(150f));

            GUILayout.FlexibleSpace();

            // 悬浮性能 HUD 开关
            GUIStyle hudStyle = MFPProfiler.ShowOverlay ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
            string hudText = MFPProfiler.ShowOverlay ? I18n.Tr("THM_HIDE_PROFILER", "✔ 隐藏性能 HUD (F10)") : I18n.Tr("THM_SHOW_PROFILER", "显示性能 HUD (F10)");
            if (GUILayout.Button(hudText, hudStyle, GUILayout.Width(170f), GUILayout.Height(22f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance?.SaveSettings();
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // =========================================================================
            // 全局双轨刷新率与心跳调度 (Global Dual-Track Refresh & Heartbeat Scheduling)
            // =========================================================================
            var renderMgr = WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                MFPGuiSkin.DrawHeader(
                    I18n.Tr("DIAG_HEADER_REFRESH_SCHED", "⚡ 全局双轨刷新率与心跳调度"),
                    I18n.Tr("DIAG_SUB_REFRESH_SCHED", "统一管控全局显示刷新阶梯 (UIDrawLoop) 与遥测数据心跳 (DataHeartBeat)")
                );

                // 1. 全局显示刷新预设 (UIDrawLoop)
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("<b>{0}</b>", I18n.Tr("DIAG_GLOBAL_DRAW_PROFILE", "全局显示刷新预设 (UIDrawLoop):")), GUILayout.Width(220f));

                bool isUltra = renderMgr.CurrentProfile == GlobalRefreshProfile.Ultra60Hz;
                if (GUILayout.Button(I18n.Tr("DIAG_PROFILE_ULTRA60", "60Hz 极致满帧"), isUltra ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetRefreshProfile(GlobalRefreshProfile.Ultra60Hz);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isBal = renderMgr.CurrentProfile == GlobalRefreshProfile.Balanced;
                if (GUILayout.Button(I18n.Tr("DIAG_PROFILE_BALANCED", "智能阶梯 (推荐★)"), isBal ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetRefreshProfile(GlobalRefreshProfile.Balanced);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isEco = renderMgr.CurrentProfile == GlobalRefreshProfile.EcoPowerSaver;
                if (GUILayout.Button(I18n.Tr("DIAG_PROFILE_ECO", "省电节能"), isEco ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetRefreshProfile(GlobalRefreshProfile.EcoPowerSaver);
                    ThemeManager.Instance?.SaveSettings();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(2f);
                GUILayout.BeginHorizontal();
                bool isVsync = renderMgr.ControlMode == RefreshControlMode.VSync_GameFPS;
                if (GUILayout.Button(isVsync ? "● " + I18n.Tr("DIAG_MODE_VSYNC", "跟随垂直同步 (VSync)") : "○ " + I18n.Tr("DIAG_MODE_VSYNC", "跟随垂直同步 (VSync)"), isVsync ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetControlMode(RefreshControlMode.VSync_GameFPS);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isFree = renderMgr.ControlMode == RefreshControlMode.CustomHz_FreeTier;
                if (GUILayout.Button(isFree ? "● " + I18n.Tr("DIAG_MODE_CUSTOM_HZ", "自由定义 Hz (Free Tier)") : "○ " + I18n.Tr("DIAG_MODE_CUSTOM_HZ", "自由定义 Hz (Free Tier)"), isFree ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetControlMode(RefreshControlMode.CustomHz_FreeTier);
                    ThemeManager.Instance?.SaveSettings();
                }
                GUILayout.EndHorizontal();

                if (isFree)
                {
                    GUILayout.Space(2f);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.Format(I18n.Tr("DIAG_TIER_STANDARD_LABEL", "标准: <color=#{0}>{1:F0}Hz</color>"), MFPGuiSkin.HexAccentCyan, renderMgr.StandardHz), GUILayout.Width(95f));
                    float sHz = GUILayout.HorizontalSlider(renderMgr.StandardHz, 10f, 120f, GUILayout.Width(110f));
                    if (Math.Abs(sHz - renderMgr.StandardHz) > 0.5f) { renderMgr.StandardHz = Mathf.Round(sHz); ThemeManager.Instance?.SaveSettings(); }

                    GUILayout.Label(string.Format(I18n.Tr("DIAG_TIER_SLOW_LABEL", "慢速: <color=#{0}>{1:F0}Hz</color>"), MFPGuiSkin.HexAccentCyan, renderMgr.SlowHz), GUILayout.Width(75f));
                    float slHz = GUILayout.HorizontalSlider(renderMgr.SlowHz, 5f, 60f, GUILayout.Width(90f));
                    if (Math.Abs(slHz - renderMgr.SlowHz) > 0.5f) { renderMgr.SlowHz = Mathf.Round(slHz); ThemeManager.Instance?.SaveSettings(); }

                    GUILayout.Label(string.Format(I18n.Tr("DIAG_TIER_RELAXED_LABEL", "空闲: <color=#{0}>{1:F0}Hz</color>"), MFPGuiSkin.HexAccentCyan, renderMgr.RelaxedHz), GUILayout.Width(90f));
                    float rHz = GUILayout.HorizontalSlider(renderMgr.RelaxedHz, 1f, 30f, GUILayout.Width(80f));
                    if (Math.Abs(rHz - renderMgr.RelaxedHz) > 0.5f) { renderMgr.RelaxedHz = Mathf.Round(rHz); ThemeManager.Instance?.SaveSettings(); }
                    GUILayout.EndHorizontal();
                }
                MFPGuiSkin.EndInset();

                GUILayout.Space(3f);

                // 2. 全局数据心跳基准 (DataHeartBeat)
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("<b>{0}</b>", I18n.Tr("DIAG_GLOBAL_HB_PROFILE", "全局数据心跳基准 (DataHeartBeat):")), GUILayout.Width(220f));

                float curHb = renderMgr.GlobalDataHeartbeatHz;
                bool isHbAuto = curHb <= 0.001f;
                if (GUILayout.Button(I18n.Tr("DIAG_HB_AUTO", "各组件默认"), isHbAuto ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetGlobalDataHeartbeatHz(0f);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isHb20 = Math.Abs(curHb - 20f) < 0.5f;
                if (GUILayout.Button(I18n.Tr("DIAG_HB_20HZ", "20Hz 极速遥测"), isHb20 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetGlobalDataHeartbeatHz(20f);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isHb10 = Math.Abs(curHb - 10f) < 0.5f;
                if (GUILayout.Button(I18n.Tr("DIAG_HB_10HZ", "10Hz 推荐节拍★"), isHb10 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetGlobalDataHeartbeatHz(10f);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isHb5 = Math.Abs(curHb - 5f) < 0.5f;
                if (GUILayout.Button(I18n.Tr("DIAG_HB_5HZ", "5Hz 节能节拍"), isHb5 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetGlobalDataHeartbeatHz(5f);
                    ThemeManager.Instance?.SaveSettings();
                }

                bool isHb2 = Math.Abs(curHb - 2f) < 0.5f;
                if (GUILayout.Button(I18n.Tr("DIAG_HB_2HZ", "2Hz 深空巡航"), isHb2 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    renderMgr.SetGlobalDataHeartbeatHz(2f);
                    ThemeManager.Instance?.SaveSettings();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(2f);
                string statsText = string.Format(
                    I18n.Tr("DIAG_SCHED_STATS", "活动组件: {0} | 满血直通: {1} | 切片轮询: {2} | 帧预算: {3:F2}ms"),
                    renderMgr.ActiveWidgetCount,
                    renderMgr.CriticalWidgetCount,
                    renderMgr.NonCriticalWidgetCount,
                    renderMgr.MaxFrameBudgetMs
                );
                GUILayout.Label(string.Format("<color=#{0}><size=11>• {1}</size></color>", MFPGuiSkin.HexTextSecondary, statsText));
                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 3: Safety Fallback & Master Bypass

        private void DrawSafetyFallbackAndBypassCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("DIAG_HEADER_SAFETY", "🛡️ 安全熔断、原版旁路与故障恢复"),
                I18n.Tr("DIAG_SUB_SAFETY", "多重硬件级容错保护，任何异常均能 100% 回退至原生原版")
            );

            // 熔断状态指示
            MFPGuiSkin.BeginInset();
            if (MFPSafetyFallback.IsFaulted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("<color=#FF4444><b>{0}</b></color>", I18n.Tr("DIAG_LABEL_BREAKER_ACTIVE", "⚠ 航电安全熔断已激活 (Avionics Circuit Breaker Active)")), GUI.skin.label);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.Tr("UI_FAULT_BTN_RETRY", "🔄 尝试恢复装配"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(24f), GUILayout.Width(130f)))
                {
                    bool ok = MFPSafetyFallback.TryRecoverFromFault();
                    MFPGuiSkin.ShowToast(ok ? I18n.Tr("UI_TOAST_FAULT_RECOVERED", "✔ 已尝试恢复 MFP 航电系统") : I18n.Tr("UI_TOAST_FAULT_RETRY_FAIL", "✖ 恢复失败，系统保持原版降级模式"));
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(string.Format("<color=#{0}><size=11>{1}: {2}\n{3}</size></color>",
                    MFPGuiSkin.HexTextSecondary,
                    I18n.Tr("DIAG_FAULT_REASON_PREFIX", "原因"),
                    MFPSafetyFallback.FaultReason,
                    I18n.Tr("DIAG_FAULT_BYPASS_DESC", "当前已切断所有 MFP 渲染并 100% 切换至原生原版界面。")));
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("<color=#{0}><b>{1}</b></color>", MFPGuiSkin.HexAccentGreen, I18n.Tr("DIAG_STATUS_HEALTHY", "● 航电系统运行状态健全 (No Faults Detected)")), GUILayout.ExpandWidth(true));
                GUILayout.Label(string.Format("<color=#{0}><size=11>{1}</size></color>", MFPGuiSkin.HexTextSecondary, I18n.Tr("DIAG_STATUS_MONITOR_ONLINE", "熔断监视器实时在线")), GUILayout.Width(130f));
                GUILayout.EndHorizontal();
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 全局 Master Bypass 切换
            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUIStyle bypassStyle = bypassed ? MFPGuiSkin.DangerButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string bypassLabel = bypassed ? I18n.Tr("THM_BYPASS_ON", "● [已完全旁路] 所有 MFP 逻辑/渲染已切断 (F11)") : I18n.Tr("THM_BYPASS_OFF", "○ [航电正常运行] 点击一键纯净旁路至原版 (F11)");
            if (GUILayout.Button(bypassLabel, bypassStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance?.SaveSettings();
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 4: Spec Audit Validator

        private void DrawSpecValidatorCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("DIAG_HEADER_SPEC", "📋 航电规范门禁自检 (Avionics Spec Validator)"),
                I18n.Tr("DIAG_SUB_SPEC", "检验全量组件刷新阶梯、颜色管道、死区防抖与内存泄漏状态")
            );

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("THM_BTN_RUN_SPEC", "运行规范自检"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(130f), GUILayout.Height(24f)))
            {
                WidgetValidationReport auditReport = WidgetSpecificationValidator.RunDevelopmentAudit();
                _lastSpecAuditSummary = auditReport.IsCompliant
                    ? I18n.TrFormat("THM_SPEC_OK", auditReport.TotalWidgetsAudited, auditReport.TotalChecksPerformed, auditReport.WarningCount)
                    : I18n.TrFormat("THM_SPEC_FAIL", auditReport.ErrorCount, auditReport.WarningCount);
            }

            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("THM_SPEC_STATUS_PREFIX", "组件规范最新审计状态:")} {_lastSpecAuditSummary}</size></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        #endregion
    }
}
