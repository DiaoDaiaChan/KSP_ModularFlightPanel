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

            // 1. 核心姿态球显隐与系统原生控制 (用户重点需求: 关闭/显示自定义导航球)
            GUILayout.Label("<b>1. 姿态球与原生兼容控制 (Navball Display Controls)</b>");

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
            GUILayout.Label("<b>KSP 原生屏幕底栏导航球:</b>", GUILayout.Width(220f));
            bool hideStock = HarmonyPatches.IsStockNavballHidden;
            GUI.color = hideStock ? Color.cyan : Color.white;
            string stockBtnText = hideStock ? "✔ [已彻底隐藏] 原版导航球外壳与滑块已屏蔽" : "✖ [显示原生] 原版屏幕底栏导航球正常显示";
            if (GUILayout.Button(stockBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockNavballHidden = !HarmonyPatches.IsStockNavballHidden;
                StockNavBallHook.HideStockNavballCompletely(HarmonyPatches.IsStockNavballHidden);
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
