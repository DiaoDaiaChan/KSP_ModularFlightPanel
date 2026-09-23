using System;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 游戏内机载遥测仿真测试沙盒 (In-Game Telemetry Simulation Sandbox)
    /// 无需进入飞行场景或发射飞船，在航天中心、发射台或任意场景均可原地进行动态 UI 效果调校
    /// </summary>
    public static class TabSimulation
    {
        private static Vector2 _scrollPos = Vector2.zero;

        public static void Draw()
        {
            if (TelemetryHub.Instance == null)
            {
                GUILayout.Label("正在初始化 TelemetryHub...");
                return;
            }

            TelemetryHub hub = TelemetryHub.Instance;
            TelemetrySimulationEngine sim = hub.SimulationEngine;

            GUILayout.BeginVertical();

            // 1. 仿真模式总开关
            GUILayout.BeginHorizontal("box");
            bool isSim = hub.IsSimulationMode;
            GUI.color = isSim ? Color.green : Color.white;
            string simToggleBtn = isSim ? "▶ [仿真模式已激活] 正在喂送物理仿真流" : "▶ [开启遥测仿真模式] 原地测试全量动态仪表";
            if (GUILayout.Button(simToggleBtn, GUILayout.Height(30f), GUILayout.ExpandWidth(true)))
            {
                hub.IsSimulationMode = !hub.IsSimulationMode;
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 2. 真实工况阶段一键跳转
            GUILayout.Label("<b>▼ 真实飞行工况阶段瞬时跳转 (Flight Scenarios)</b>");
            GUILayout.Label("<color=#AAAAAA><size=11>点击任意按钮即可瞬间将全船动力学、气动、电量、通信推演至指定真实工况：</size></color>");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🚀 发射预备\n<size=10>0m/s | 1G | 满油</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PadHold);
            }
            if (GUILayout.Button("⚡ 音障爬升\n<size=10>340m/s | 动压爬升</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.AscentTransonic);
            }
            if (GUILayout.Button("💥 Max-Q 极限\n<size=10>Q=34kPa | 3.5G</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MaxQ);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 关机分级\n<size=10>下级点火 | 级间切分</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.MECOAndStaging);
            }
            if (GUILayout.Button("🌐 入轨微重力\n<size=10>120km | 0G | 净充电</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.OrbitalCruise);
            }
            if (GUILayout.Button("🌑 暗面断电\n<size=10>无光照 | 电压跌破</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.PowerCrisis);
            }
            if (GUILayout.Button("🔥 黑障再入\n<size=10>-420m/s | 断网离线</size>", GUILayout.Height(36f)))
            {
                hub.IsSimulationMode = true;
                sim.ApplyScenario(FlightScenario.ReentryBlackout);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);

            // 3. 连续时间轴物理播放控制
            GUILayout.Label("<b>▼ 连续物理推演时序控制器 (Timeline Player)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            string playBtn = sim.IsPlaying ? "❚❚ 暂停推演" : "▶ 继续播放";
            GUI.color = sim.IsPlaying ? Color.cyan : Color.yellow;
            if (GUILayout.Button(playBtn, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                sim.IsPlaying = !sim.IsPlaying;
            }
            GUI.color = Color.white;

            GUILayout.Label($"<b>推演进度:</b> {sim.TimelineTime:F1}s / {sim.MaxTimelineTime:F0}s", GUILayout.Width(160f));

            // 倍速切换
            GUILayout.Label("<b>倍速:</b>", GUILayout.Width(45f));
            float[] speeds = new float[] { 0.5f, 1.0f, 2.0f, 5.0f };
            for (int i = 0; i < speeds.Length; i++)
            {
                bool isSel = Mathf.Approximately(sim.PlaybackSpeed, speeds[i]);
                GUI.color = isSel ? Color.green : Color.white;
                if (GUILayout.Button($"{speeds[i]}x", GUILayout.Width(38f), GUILayout.Height(22f)))
                {
                    sim.PlaybackSpeed = speeds[i];
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // 时间轴滑块
            float newTime = GUILayout.HorizontalSlider(sim.TimelineTime, 0f, sim.MaxTimelineTime);
            if (Math.Abs(newTime - sim.TimelineTime) > 0.1f)
            {
                sim.TimelineTime = newTime;
            }
            GUILayout.EndVertical();

            GUILayout.Space(12f);

            // 4. 当前仿真遥测即时仪表读数监视器
            GUILayout.Label("<b>▼ 仿真物理参量即时监视器 (Live Telemetry Monitor)</b>");
            GUILayout.BeginVertical("box");

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

            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
