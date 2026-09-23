using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 视觉风格、姿态球开关与全局偏好配置 (Themes, Navball & Preferences)
    /// </summary>
    public static class TabThemeSettings
    {
        private static Vector2 _scrollPos = Vector2.zero;

        public static void Draw()
        {
            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 1. 核心姿态球显隐与系统原生控制 (用户重点需求: 关闭/显示自定义导航球及原版原生组件)
            GUILayout.Label("<b>1. 仪表系统显隐与原生 UI 深度融合控制 (Display & Stock UI Controls)</b>");

            var navballCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isNavballEnabled = navballCfg == null || navballCfg.IsEnabled;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>自定义 3D 姿态球 (Modular Navball):</b>", GUILayout.Width(220f));

            GUI.color = isNavballEnabled ? Color.green : Color.yellow;
            string ballBtnText = isNavballEnabled ? "● [显示中] 点击立即隐藏自定义姿态球" : "○ [已隐藏] 点击开启并显示自定义姿态球";
            if (GUILayout.Button(ballBtnText, GUILayout.Height(28f)))
            {
                if (navballCfg != null)
                {
                    navballCfg.IsEnabled = !navballCfg.IsEnabled;
                }
                else
                {
                    navballCfg = new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navballCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance.RebuildHUD();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版 Navball 视觉模型隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生底栏导航球:</b>", GUILayout.Width(220f));
            bool hideStockBall = HarmonyPatches.IsStockNavballHidden;
            GUI.color = hideStockBall ? Color.cyan : Color.white;
            string stockBallBtnText = hideStockBall ? "✔ [已彻底隐藏] 原版导航球外壳与滑块已屏蔽" : "✖ [显示原生] 原版屏幕底栏导航球正常显示";
            if (GUILayout.Button(stockBallBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockNavballHidden = !HarmonyPatches.IsStockNavballHidden;
                StockNavBallHook.HideStockNavballCompletely(HarmonyPatches.IsStockNavballHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版顶部高度计隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生顶部高度计盒:</b>", GUILayout.Width(220f));
            bool hideStockAlti = HarmonyPatches.IsStockAltimeterHidden;
            GUI.color = hideStockAlti ? Color.cyan : Color.white;
            string stockAltiBtnText = hideStockAlti ? "✔ [已彻底隐藏] 顶部高度滚轮/大气计/垂直速度表已屏蔽" : "✖ [显示原生] 原版顶部高度计与仪表盒正常显示";
            if (GUILayout.Button(stockAltiBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockAltimeterHidden = !HarmonyPatches.IsStockAltimeterHidden;
                StockNavBallHook.HideStockAltimeter(HarmonyPatches.IsStockAltimeterHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左下角分级与控制台隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生左下操纵分级台:</b>", GUILayout.Width(220f));
            bool hideStockBottom = HarmonyPatches.IsStockBottomLeftHidden;
            GUI.color = hideStockBottom ? Color.cyan : Color.white;
            string stockBottomBtnText = hideStockBottom ? "✔ [已彻底隐藏] 左下分级框/操纵量标尺/堆栈已屏蔽" : "✖ [显示原生] 原版左下角操纵与分级正常显示";
            if (GUILayout.Button(stockBottomBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockBottomLeftHidden = !HarmonyPatches.IsStockBottomLeftHidden;
                StockNavBallHook.HideStockBottomLeft(HarmonyPatches.IsStockBottomLeftHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左上角时间加速与时钟隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生时间加速/时钟:</b>", GUILayout.Width(220f));
            bool hideStockTime = HarmonyPatches.IsStockTimeWarpHidden;
            GUI.color = hideStockTime ? Color.cyan : Color.white;
            string stockTimeBtnText = hideStockTime ? "✔ [已彻底隐藏] 原生时间加速条/MET时钟已屏蔽" : "✖ [显示原生] 原生时间加速条与时钟正常显示";
            if (GUILayout.Button(stockTimeBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockTimeWarpHidden = !HarmonyPatches.IsStockTimeWarpHidden;
                StockNavBallHook.HideStockTimeWarp(HarmonyPatches.IsStockTimeWarpHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左上角 CommNet 信号栏隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生通信信号栏:</b>", GUILayout.Width(220f));
            bool hideStockComm = HarmonyPatches.IsStockCommNetHidden;
            GUI.color = hideStockComm ? Color.cyan : Color.white;
            string stockCommBtnText = hideStockComm ? "✔ [已彻底隐藏] 原生 CommNet 信号条/浮窗已屏蔽" : "✖ [显示原生] 原生通信信号指示正常显示";
            if (GUILayout.Button(stockCommBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockCommNetHidden = !HarmonyPatches.IsStockCommNetHidden;
                StockNavBallHook.HideStockCommNet(HarmonyPatches.IsStockCommNetHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 右侧工具栏现代化风格控制
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>右侧工具栏现代化风格:</b>", GUILayout.Width(220f));
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;
            if (GUILayout.Toggle(curTbMode == 0, "原版经典", "Button", GUILayout.Height(26f)))
            {
                if (curTbMode != 0)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 0;
                    StockToolbarHook.RestoreStockToolbar();
                    ThemeManager.Instance.SaveSettings();
                }
            }
            if (GUILayout.Toggle(curTbMode == 1, "黑晶重肤 (Reskin)", "Button", GUILayout.Height(26f)))
            {
                if (curTbMode != 1)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 1;
                    StockToolbarHook.ReskinStockToolbar();
                    ThemeManager.Instance.SaveSettings();
                }
            }
            if (GUILayout.Toggle(curTbMode == 2, "折叠收纳坞 (Dock)", "Button", GUILayout.Height(26f)))
            {
                if (curTbMode != 2)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 2;
                    StockToolbarHook.HideStockToolbar(true);
                    ThemeManager.Instance.SaveSettings();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // 性能诊断与全局主干旁路 (Master Bypass & Hardware Profiler)
            GUILayout.Label("<b>2. 性能诊断与主干旁路 (Performance Profiler & Master Bypass)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUI.color = bypassed ? Color.red : Color.green;
            string bypassLabel = bypassed ? "● [已完全旁路 Bypass] 所有 MFP 逻辑/渲染已关闭 (0.00ms 开销)" : "○ [正常运行中] 点击完全 Bypass (或按 F11) 查看原生纯净性能";
            if (GUILayout.Button(bypassLabel, GUILayout.Height(28f)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>实时 MFP 帧耗时:</b> <color=#00E5FF>{MFPProfiler.AvgTotalMs:F2} ms</color> (整帧占比: {MFPProfiler.FrameBudgetPercent:F1}%) | <b>游戏帧率:</b> {MFPProfiler.CurrentFPS:F0} FPS", GUILayout.ExpandWidth(true));
            GUI.color = MFPProfiler.ShowOverlay ? Color.cyan : Color.white;
            if (GUILayout.Button(MFPProfiler.ShowOverlay ? "✔ 隐藏性能探针 HUD (F10)" : "显示性能探针 HUD (F10)", GUILayout.Width(170f), GUILayout.Height(24f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // 2. 姿态球渲染生成模式
            GUILayout.Label("<b>2. 姿态球生成模式 (Navball Render Mode)</b>");
            GUILayout.BeginHorizontal();
            bool isTex = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Texture;
            GUI.color = isTex ? Color.cyan : Color.gray;
            if (GUILayout.Button(isTex ? "● 贴图模式 (原版/Principia 素材高动态增强)" : "○ 贴图模式 (原版/Principia 素材高动态增强)", GUILayout.Height(30f)))
            {
                if (!isTex)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Texture;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            bool isProc = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            GUI.color = isProc ? Color.cyan : Color.gray;
            if (GUILayout.Button(isProc ? "● 程序化模式 (现代超清矢量)" : "○ 程序化模式 (现代超清矢量)", GUILayout.Height(30f)))
            {
                if (!isProc)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 3. 视觉主题预设
            GUILayout.Label("<b>3. 视觉主题预设风格 (Aero Themes)</b>");
            var themes = ThemeManager.Instance.AvailableThemes;
            for (int i = 0; i < themes.Count; i++)
            {
                var t = themes[i];
                bool isCurrent = ThemeManager.Instance.CurrentTheme?.ThemeId == t.ThemeId;

                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();
                string label = isCurrent ? $"✔ <b>{t.DisplayName}</b> (当前使用中)" : $"   {t.DisplayName}";
                GUI.color = isCurrent ? Color.green : Color.white;
                if (GUILayout.Button(label, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    ThemeManager.Instance.SetTheme(t.ThemeId);
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(10f);

            // 4. 大屏物理缩放比例
            GUILayout.Label("<b>4. 界面全局物理缩放 (Display Scale)</b>");
            if (NavballHUD.Instance != null)
            {
                float currentScale = NavballHUD.Instance.CustomScale;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"尺寸比例: {currentScale:F2}x", GUILayout.Width(110f));
                float newScale = GUILayout.HorizontalSlider(currentScale, 0.7f, 1.8f);
                GUILayout.EndHorizontal();

                if (Mathf.Abs(newScale - currentScale) > 0.01f)
                {
                    NavballHUD.Instance.CustomScale = newScale;
                    NavballHUD.Instance.RebuildHUD();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
