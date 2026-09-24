using System;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新游戏内机载遥测仿真测试沙盒 (In-Game Telemetry Simulation Sandbox)
    /// 核心功能：
    /// 1. 原地测试全量动态仪表与飞行组件，无需等待火箭发射。
    /// 2. 真实飞行工况阶段瞬时跳转（音障、Max-Q、关机分级、入轨、再入黑障）。
    /// 3. 全量接入 MFPGuiSkin 现代黑晶设计系统 2.0。
    /// </summary>
    public static class TabSimulation
    {
        private static Vector2 _scrollPos = Vector2.zero;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            if (TelemetryHub.Instance == null)
            {
                GUILayout.Label("<color=#7088A8>正在初始化 TelemetryHub...</color>");
                return;
            }

            TelemetryHub hub = TelemetryHub.Instance;
            TelemetrySimulationEngine sim = hub.SimulationEngine;

            GUILayout.BeginVertical(GUILayout.Height(SettingsGUI.ContentHeight));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight));

            // 1. 仿真模式总开关卡片
            MFPGuiSkin.BeginCard();
            bool isSim = hub.IsSimulationMode;
            GUIStyle simBtnStyle = isSim ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string simToggleBtn = isSim ? "▶ [仿真模式已激活] 正在喂送高保真物理仿真流" : "▶ [开启遥测仿真模式] 原地测试全量动态仪表";
            if (GUILayout.Button(simToggleBtn, simBtnStyle, GUILayout.Height(32f), GUILayout.ExpandWidth(true)))
            {
                hub.IsSimulationMode = !hub.IsSimulationMode;
            }
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 2. 真实工况阶段一键跳转卡片
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🚀 真实飞行工况阶段瞬时跳转 (Flight Scenarios)", "点击即可将全船动力学推演至真实工况");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🚀 发射预备\n<size=10>0m/s | 1G | 满油</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PadHold);
            }
            if (GUILayout.Button("⚡ 音障爬升\n<size=10>340m/s | 动压爬升</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.AscentTransonic);
            }
            if (GUILayout.Button("💥 Max-Q 极限\n<size=10>Q=34kPa | 3.5G</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MaxQ);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 关机分级\n<size=10>下级点火 | 级间切分</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MECOAndStaging);
            }
            if (GUILayout.Button("🌐 入轨微重力\n<size=10>120km | 0G | 净充电</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.OrbitalCruise);
            }
            if (GUILayout.Button("🌑 暗面断电\n<size=10>无光照 | 电压跌破</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PowerCrisis);
            }
            if (GUILayout.Button("🔥 黑障再入\n<size=10>-420m/s | 断网离线</size>", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(38f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.ReentryBlackout);
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 3. 连续时间轴物理播放控制
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("⏱ 连续物理推演时序控制器 (Timeline Player)");

            GUILayout.BeginHorizontal();
            string playBtn = sim.IsPlaying ? "❚❚ 暂停推演" : "▶ 继续播放";
            GUIStyle pStyle = sim.IsPlaying ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.WarningButtonStyle;
            if (GUILayout.Button(playBtn, pStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                sim.IsPlaying = !sim.IsPlaying;
            }

            GUILayout.Label($"<b>推演进度:</b> {sim.TimelineTime:F1}s / {sim.MaxTimelineTime:F0}s", GUILayout.Width(170f));

            GUILayout.Label("倍速:", GUILayout.Width(40f));
            float[] speeds = new float[] { 0.5f, 1.0f, 2.0f, 5.0f };
            for (int i = 0; i < speeds.Length; i++)
            {
                bool isSel = Mathf.Approximately(sim.PlaybackSpeed, speeds[i]);
                GUIStyle sStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button($"{speeds[i]}x", sStyle, GUILayout.Width(38f), GUILayout.Height(22f)))
                {
                    sim.PlaybackSpeed = speeds[i];
                }
            }
            GUILayout.EndHorizontal();

            // 时间轴滑块
            float newTime = GUILayout.HorizontalSlider(sim.TimelineTime, 0f, sim.MaxTimelineTime);
            if (Math.Abs(newTime - sim.TimelineTime) > 0.1f)
            {
                sim.TimelineTime = newTime;
            }
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 4. 当前仿真遥测即时仪表读数监视器
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("📊 仿真物理参量即时监视器 (Live Telemetry Monitor)");

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"地表速度: <color=#00E5FF><b>{hub.SurfaceSpeed:F1} m/s</b></color> (Mach {hub.Mach:F2})", GUILayout.Width(250f));
            GUILayout.Label($"显示高度: <color=#00FF88><b>{hub.DisplayAltitude:F0} m</b></color>", GUILayout.Width(200f));
            GUILayout.Label($"垂直速度: <color=#00E5FF><b>{hub.VerticalSpeed:F1} m/s</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"大气动压: <color=#FFAA00><b>{hub.DynamicPressure:F1} kPa</b></color>", GUILayout.Width(250f));
            GUILayout.Label($"过载 G力: <color=#FFAA00><b>{hub.GForce:F2} G</b></color>", GUILayout.Width(200f));
            GUILayout.Label($"推重比: <color=#00FF88><b>{hub.TWR:F2}</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"蓄电量: <color=#FFE34F><b>{hub.ElectricCharge:F0}/{hub.MaxElectricCharge:F0} EC ({hub.EcPercent:F1}%)</b></color>", GUILayout.Width(250f));
            GUILayout.Label($"母线电压: <color=#FFE34F><b>{hub.BusVoltage:F1} V</b></color>", GUILayout.Width(200f));
            GUILayout.Label($"净电荷率: <b>{hub.NetEcRate:F2} EC/s</b>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"推进剂余量: <color=#00FF88><b>{hub.StagePropellantFraction * 100f:F1}%</b></color> (STG {hub.CurrentStage})", GUILayout.Width(250f));
            GUILayout.Label($"通信信号: <color=#00E5FF><b>{hub.CommSignal * 100f:F0}% ({hub.ControlLevelStr})</b></color>", GUILayout.Width(200f));
            GUILayout.Label($"乘员数: <b>CREW {hub.CrewCount}/{hub.CrewCapacity}</b>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
