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
        public static bool IsStockNavballHidden { get; set; } = true;

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
        }
    }

    [HarmonyPatch(typeof(NavBall), "Update")]
    public static class Patch_NavBall_Update
    {
        [HarmonyPostfix]
        public static void Postfix(NavBall __instance)
        {
            if (__instance == null) return;

            StockNavBallHook.RegisterStockNavBall(__instance);

            // 当开启接管时：彻底隐藏官方底栏旧版姿态球与容器，由本 Mod 独立呈现高质感导航面板
            if (HarmonyPatches.IsStockNavballHidden)
            {
                StockNavBallHook.HideStockNavballCompletely(true);
            }
            else
            {
                StockNavBallHook.HideStockNavballCompletely(false);
            }
        }
    }

    [HarmonyPatch(typeof(NavBall), "OnDestroy")]
    public static class Patch_NavBall_OnDestroy
    {
        [HarmonyPostfix]
        public static void Postfix(NavBall __instance)
        {
            if (__instance == null) return;
            StockNavBallHook.UnregisterStockNavBall(__instance);
        }
    }
}
