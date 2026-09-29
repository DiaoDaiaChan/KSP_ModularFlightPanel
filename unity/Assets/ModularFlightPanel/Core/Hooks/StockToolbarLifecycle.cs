using System;
using UnityEngine;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 全局常驻工具栏生命周期与场景状态中枢 (Global Stock Toolbar Lifecycle & Scene Dispatcher)
    /// 解决痛点：KSP 原版 ApplicationLauncher 为跨场景持久化单例 (DontDestroyOnLoad)。
    /// 若在飞行场景被收纳坞隐蔽，退出至航天中心 (Space Center)、VAB/SPH 装配大楼或追踪站后，
    /// 原版工具栏极易因残留 alpha=0 / subCanvas.enabled=false 导致完全隐身失能。
    /// 
    /// 本生命周期中枢在 MainMenu 全局启动一次并跨场景常驻：
    /// 1. 绝对杜绝非飞行场景原版工具栏被隐藏，100% 保障建筑/设施及所有第三方 MOD 按钮可见、可用与可点击。
    /// 2. 智能调度 "保持黑晶重肤 Hook" 与 "恢复原版经典 Stock" 策略。
    /// 3. 全自动跨场景监听 (onLevelWasLoadedGUIReady / onGUIApplicationLauncherReady / onGameSceneLoadRequested)。
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class StockToolbarLifecycle : MonoBehaviour
    {
        private static StockToolbarLifecycle _instance;
        public static StockToolbarLifecycle Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 确保持久配置与主题引擎预热就绪
            try
            {
                ThemeManager.Instance.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] StockToolbarLifecycle ThemeManager.Initialize error: {ex.Message}");
            }

#if KSP_RUNTIME
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
#endif
            MFPLogger.Info(MFPLogger.CatCore, "StockToolbarLifecycle dispatcher initialized successfully.");
        }

        private void OnDestroy()
        {
#if KSP_RUNTIME
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelWasLoadedGUIReady);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
#endif
            if (_instance == this) _instance = null;
        }

#if KSP_RUNTIME
        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            // 场景即将切换：若目标是非飞行场景，立即提前解除隐藏，避免跨场景残留隐藏态
            if (scene != GameScenes.FLIGHT)
            {
                StockToolbarHook.ApplyNonFlightStyleMode();
            }
        }

        private void OnLevelWasLoadedGUIReady(GameScenes scene)
        {
            if (scene != GameScenes.FLIGHT)
            {
                StockToolbarHook.ApplyNonFlightStyleMode();
            }
            else
            {
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
            }
        }

        private void OnAppLauncherReady()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                StockToolbarHook.ApplyNonFlightStyleMode();
            }
            else
            {
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
            }
        }
#endif
    }
}
