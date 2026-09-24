using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class NavballPlugin : MonoBehaviour
    {
        private TelemetryHub _telemetry;
        private NavballHUD _hud;
        private SettingsGUI _settings;

        private void Awake()
        {
            Debug.Log("[ModularFlightPanel] Initializing Modular Flight Panel (模块化飞行面板)...");

            // 1. 加载着色器与 AssetBundle
            AssetLoader.LoadBundle();

            // 1.5. 初始化外部第三方 Mod 遥测数据探针 (FAR / KER / MechJeb)
            Probes.TelemetryProbeManager.InitializeAll();

            // 2. 初始化主题与配置
            ThemeManager.Instance.Initialize();

            // 2.5. 确保持久化 NavBall 桥接器与数学后备就绪
            NavBallHookService.Provider = new StockNavBallVisualHook();
            NavBallHookService.MarkerDirectionFallback = StockNavBallHook.GetMarkerDirection;

            // 3. 应用 Harmony 补丁隐藏原版 Navball
            HarmonyPatches.ApplyPatches();

            // 4. 挂载遥测数据中心
            _telemetry = gameObject.AddComponent<TelemetryHub>();

            // 4.5. 挂载飞船二维剪影烘焙器 (零常驻开销 / 15 FPS 动态变动捕获)
            var silhouetteBaker = VesselSilhouetteBaker.Instance;

            // 5. 挂载 UGUI 表现层
            _hud = gameObject.AddComponent<NavballHUD>();
            _hud.Initialize();

            // 6. 挂载设置面板
            _settings = gameObject.AddComponent<SettingsGUI>();

            Debug.Log("[ModularFlightPanel] Modular Flight Panel initialized successfully!");
        }

        private void OnDestroy()
        {
            Debug.Log("[ModularFlightPanel] Shutting down Modular Flight Panel...");

            HarmonyPatches.RemovePatches();
            AssetLoader.UnloadBundle();

            if (_hud != null) Destroy(_hud);
            if (_telemetry != null) Destroy(_telemetry);
            if (_settings != null) Destroy(_settings);
        }
    }
}
