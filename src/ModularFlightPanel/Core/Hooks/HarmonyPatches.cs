using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.Core
{
    public static class HarmonyPatches
    {
        private static Harmony _harmony;
        public static bool IsStockNavballHidden
        {
            get => ThemeManager.IsStockNavballHidden;
            set => ThemeManager.IsStockNavballHidden = value;
        }
        public static bool IsStockAltimeterHidden
        {
            get => ThemeManager.IsStockAltimeterHidden;
            set => ThemeManager.IsStockAltimeterHidden = value;
        }
        public static bool IsStockBottomLeftHidden
        {
            get => ThemeManager.IsStockBottomLeftHidden;
            set => ThemeManager.IsStockBottomLeftHidden = value;
        }
        public static bool IsStockTimeWarpHidden
        {
            get => ThemeManager.IsStockTimeWarpHidden;
            set => ThemeManager.IsStockTimeWarpHidden = value;
        }
        public static bool IsStockCommNetHidden
        {
            get => ThemeManager.IsStockCommNetHidden;
            set => ThemeManager.IsStockCommNetHidden = value;
        }

        public static void ApplyPatches()
        {
            if (_harmony != null) return;

            try
            {
                _harmony = new Harmony("com.antigravity.modularflightpanel");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
#if KSP_RUNTIME
                DockAnchorTracker.ApplyDynamicHarmonyPatches(_harmony);
#endif
                Debug.Log("[ModularFlightPanel] Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to apply Harmony patches: {ex.Message}");
            }
        }

        public static void RemovePatches()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll("com.antigravity.modularflightpanel");
                _harmony = null;
                Debug.Log("[ModularFlightPanel] Harmony patches unpatched.");
            }
        }
    }

    [HarmonyPatch(typeof(NavBall), "Start")]
    public static class Patch_NavBall_Start
    {
        [HarmonyPostfix]
        public static void Postfix(NavBall __instance)
        {
            if (__instance == null) return;
            StockNavBallHook.RegisterStockNavBall(__instance);
            if (HarmonyPatches.IsStockNavballHidden)
            {
                StockNavBallHook.HideStockNavballCompletely(true);
            }
            if (HarmonyPatches.IsStockAltimeterHidden)
            {
                StockNavBallHook.HideStockAltimeter(true);
            }
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockNavBallHook.HideStockBottomLeft(true);
            }
            if (HarmonyPatches.IsStockTimeWarpHidden)
            {
                StockNavBallHook.HideStockTimeWarp(true);
            }
            if (HarmonyPatches.IsStockCommNetHidden)
            {
                StockNavBallHook.HideStockCommNet(true);
            }
            StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
        }
    }

    [HarmonyPatch(typeof(NavBall), "Update")]
    public static class Patch_NavBall_Update
    {
        private static bool _prevNavballHidden = false;
        private static bool _prevAltimeterHidden = false;
        private static bool _prevBottomLeftHidden = false;
        private static bool _prevTimeWarpHidden = false;
        private static bool _prevCommNetHidden = false;
        private static bool _prevBypassed = false;
        private static float _lastWatchdogTime = -1f;

        [HarmonyPrefix]
        public static bool Prefix(NavBall __instance)
        {
            if (__instance == null) return true;

            StockNavBallHook.RegisterStockNavBall(__instance);

            bool bypassed = MFPProfiler.IsMasterBypassed;
            if (bypassed) return true;

            bool isStockDirect = ThemeManager.Instance != null && ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.StockDirect;
            bool shouldHideNavball = HarmonyPatches.IsStockNavballHidden && !isStockDirect;

            if (!shouldHideNavball) return true;

            // 姿态球处于隐身态：纯数学纳秒级轻量驱动万向节与标记 Transform，跳过官方 Update (彻底消除 0.09ms CPU 耗时与 8 次堆材质分配)
            StockNavBallHook.UpdateStockNavballGymbalsLightweight(__instance);
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(NavBall __instance)
        {
            if (__instance == null) return;

            StockNavBallHook.RegisterStockNavBall(__instance);

            bool bypassed = MFPProfiler.IsMasterBypassed;
            if (bypassed)
            {
                if (!_prevBypassed)
                {
                    _prevBypassed = true;
                    StockNavBallHook.RestoreAllStockUI();
                    StockToolbarHook.RestoreStockToolbar();
                }
                return;
            }

            if (_prevBypassed)
            {
                _prevBypassed = false;
                _lastWatchdogTime = -1f; // 强制刷新
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
            }

            float now = Time.unscaledTime;
            bool force = (now - _lastWatchdogTime) > 1.0f;
            if (force) _lastWatchdogTime = now;

            MFPProfiler.BeginSample(ProfilerSection.Hooks);
            try
            {
                bool isStockDirect = ThemeManager.Instance != null && ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.StockDirect;
                bool shouldHideNavball = HarmonyPatches.IsStockNavballHidden && !isStockDirect;
                if (force || shouldHideNavball != _prevNavballHidden)
                {
                    _prevNavballHidden = shouldHideNavball;
                    if (isStockDirect)
                    {
                        StockNavBallHook.SetStockNavballClean(true);
                    }
                    else
                    {
                        StockNavBallHook.HideStockNavballCompletely(_prevNavballHidden);
                    }
                }
                if (force || HarmonyPatches.IsStockAltimeterHidden != _prevAltimeterHidden)
                {
                    _prevAltimeterHidden = HarmonyPatches.IsStockAltimeterHidden;
                    StockNavBallHook.HideStockAltimeter(_prevAltimeterHidden);
                }
                if (force || HarmonyPatches.IsStockBottomLeftHidden != _prevBottomLeftHidden)
                {
                    _prevBottomLeftHidden = HarmonyPatches.IsStockBottomLeftHidden;
                    StockNavBallHook.HideStockBottomLeft(_prevBottomLeftHidden);
                }
                if (force || HarmonyPatches.IsStockTimeWarpHidden != _prevTimeWarpHidden)
                {
                    _prevTimeWarpHidden = HarmonyPatches.IsStockTimeWarpHidden;
                    StockNavBallHook.HideStockTimeWarp(_prevTimeWarpHidden);
                }
                if (force || HarmonyPatches.IsStockCommNetHidden != _prevCommNetHidden)
                {
                    _prevCommNetHidden = HarmonyPatches.IsStockCommNetHidden;
                    StockNavBallHook.HideStockCommNet(_prevCommNetHidden);
                }
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Hooks);
            }
        }
    }

    [HarmonyPatch(typeof(SpeedDisplay), "LateUpdate")]
    public static class Patch_SpeedDisplay_LateUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix(SpeedDisplay __instance)
        {
            if (__instance == null) return true;

            bool bypassed = MFPProfiler.IsMasterBypassed;
            if (bypassed) return true;

            bool isStockDirect = ThemeManager.Instance != null && ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.StockDirect;
            bool shouldHideNavball = HarmonyPatches.IsStockNavballHidden && !isStockDirect;

            if (!shouldHideNavball) return true;

            // 原版 SpeedDisplay.LateUpdate 每帧执行 StringBuilderCache 与 TextMeshPro 文本网格重构 (约 0.05ms)。
            // 隐藏态下彻底切断其 LateUpdate，所有速度读取均由遥测中心底层双精度遥测与 Principia C++ 探针直接供给。
            return false;
        }
    }

    [HarmonyPatch(typeof(ActionGroupToggleButton), "LateUpdate")]
    public static class Patch_ActionGroupToggleButton_LateUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix(ActionGroupToggleButton __instance)
        {
            if (__instance == null) return true;
            if (!__instance.enabled || !__instance.gameObject.activeInHierarchy) return false;

            bool bypassed = MFPProfiler.IsMasterBypassed;
            if (bypassed) return true;

            // 若按钮属于已隐藏隔离的原生画布（如 altimeterFrame 或 navBall），切断其逐帧动作组轮询 (约 0.01ms)
            var canvas = __instance.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.enabled)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(StageManager), "ActivateStage", new Type[] { typeof(int) })]
    public static class Patch_StageManager_ActivateStage
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
    }

    [HarmonyPatch(typeof(StageManager), "ActivateNextStage")]
    public static class Patch_StageManager_ActivateNextStage
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
    }

    [HarmonyPatch(typeof(StageManager), "SortIcons", new Type[] { typeof(bool), typeof(Part), typeof(bool), typeof(bool) })]
    public static class Patch_StageManager_SortIcons
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
    }

    [HarmonyPatch(typeof(StageManager), "AddNewStageGroupsIfNeeded", new Type[] { typeof(Part) })]
    public static class Patch_StageManager_AddNewStageGroupsIfNeeded
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
    }

    [HarmonyPatch(typeof(StageManager), "OnGUIStageSequenceModified")]
    public static class Patch_StageManager_OnGUIStageSequenceModified
    {
        [HarmonyPostfix]
        public static void Postfix(StageManager __instance)
        {
            if (__instance == null || MFPProfiler.IsMasterBypassed) return;
            if (HarmonyPatches.IsStockBottomLeftHidden)
            {
                if (__instance.deltaVTotalSection != null && __instance.deltaVTotalSection.gameObject.activeSelf)
                {
                    __instance.deltaVTotalSection.gameObject.SetActive(false);
                }
                StockUIHider.HideStockBottomLeft(true);
            }
        }
    }
}
