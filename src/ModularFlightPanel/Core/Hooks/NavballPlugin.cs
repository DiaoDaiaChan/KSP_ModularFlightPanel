using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
using ModularFlightPanel.Core.Diagnostics;

namespace ModularFlightPanel.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class NavballPlugin : MonoBehaviour
    {
        private TelemetryHub _telemetry;
        private FlightHUDManager _hud;
        private SettingsGUI _settings;
        private MFPToolbarButton _toolbarButton;

        private void Awake()
        {
            GlobalExceptionSentinel.Install();
            MFPLogger.Info(MFPLogger.CatCore, "Initializing Modular Flight Panel (模块化飞行面板)...");

            try
            {
                // 1. 加载着色器与 AssetBundle
                AssetLoader.LoadBundle();

                // 1.5. 初始化外部第三方 Mod 遥测数据探针 (FAR / KER / MechJeb)
                Probes.TelemetryProbeManager.InitializeAll();

                // 2. 初始化主题与配置
                ThemeManager.Instance.Initialize();

                // 2.5. 确保持久化 NavBall 桥接器与数学后备就绪
                NavBallHookService.Provider = new StockNavBallVisualHook();
                NavBallHookService.MarkerDirectionFallback = StockNavBallHook.GetMarkerDirection;
                StockStageIconService.Provider = new StockStageIconHook();
                StockStageActionService.Provider = new StockStageActionHook();

                // 3. 应用 Harmony 补丁隐藏原版 Navball
                HarmonyPatches.ApplyPatches();

                // 4. 挂载遥测数据中心
                _telemetry = gameObject.AddComponent<TelemetryHub>();

                // 4.5. 挂载飞船二维剪影与三维视图烘焙器 (零常驻开销 / 15 FPS 动态变动捕获)
                var silhouetteBaker = VesselSilhouetteBaker.Instance;
                var vessel3DBaker = Vessel3DBaker.Instance;

                // 5. 挂载 UGUI 表现层
                _hud = gameObject.AddComponent<FlightHUDManager>();
                try
                {
                    _hud.Initialize();
                }
                catch (Exception ex)
                {
                    MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager initialization error: {ex}");
                    MFPSafetyFallback.TriggerFaultFallback(I18n.Tr("ERR_HUD_INIT_FATAL", "HUD 初始化致命异常 (HUD Initialization Error)"), ex);
                }
            }
            catch (Exception fatalEx)
            {
                MFPLogger.Error(MFPLogger.CatCore, $"Fatal error during NavballPlugin Awake: {fatalEx}");
                MFPSafetyFallback.TriggerFaultFallback(I18n.Tr("ERR_PLUGIN_STARTUP_FATAL", "插件启动阶段发生致命异常 (Plugin Startup Fatal Error)"), fatalEx);
            }
            finally
            {
                // 6. 挂载设置面板 (保障即便 HUD 异常崩溃，Alt+N 设置面板仍 100% 可呼出以供排障与恢复)
                if (_settings == null)
                {
                    _settings = gameObject.AddComponent<SettingsGUI>();
                }

                // 7. 挂载原版工具栏应用按钮 (ApplicationLauncher)
                if (_toolbarButton == null)
                {
                    _toolbarButton = gameObject.AddComponent<MFPToolbarButton>();
                }
            }

            MFPLogger.Info(MFPLogger.CatCore, "Modular Flight Panel initialized successfully!");
        }

        private void OnDestroy()
        {
            MFPLogger.Info(MFPLogger.CatCore, "Shutting down Modular Flight Panel...");

            try
            {
                ModularFlightPanel.UI.Settings.MFPInputLock.ReleaseAllLocks();
            }
            catch { }

            try { HarmonyPatches.RemovePatches(); } catch (Exception ex) { MFPLogger.Warn(MFPLogger.CatCore, $"Error removing patches: {ex.Message}"); }
            try { AssetLoader.UnloadBundle(); } catch (Exception ex) { MFPLogger.Warn(MFPLogger.CatCore, $"Error unloading bundle: {ex.Message}"); }

            try { if (_toolbarButton != null) Destroy(_toolbarButton); } catch { }
            try { if (_hud != null) Destroy(_hud); } catch { }
            try { if (_telemetry != null) Destroy(_telemetry); } catch { }
            try { if (_settings != null) Destroy(_settings); } catch { }

            try { StockToolbarHook.ApplyNonFlightStyleMode(); } catch (Exception ex) { MFPLogger.Warn(MFPLogger.CatCore, $"Error applying non-flight toolbar: {ex.Message}"); }

            GlobalExceptionSentinel.Uninstall();
        }
    }
}
