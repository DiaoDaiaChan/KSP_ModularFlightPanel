using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
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
}
