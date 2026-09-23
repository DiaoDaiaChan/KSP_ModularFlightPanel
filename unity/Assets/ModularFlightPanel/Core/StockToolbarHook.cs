using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版右侧工具栏 (ApplicationLauncher / ToolbarControl) 现代化挂钩与视觉重绘引擎
    /// 模式 A: 原位超现代黑晶玻璃重肤 (In-Place Glass Reskin)
    /// 模式 B: 隐蔽原版以供全新模块化折叠航电收纳坞 (ModernToolbarWidget) 全权接管
    /// </summary>
    public static class StockToolbarHook
    {
        private static bool _isReskinned = false;
        private static bool _isStockHidden = false;

        public static bool IsReskinned => _isReskinned;
        public static bool IsStockHidden => _isStockHidden;

        private static readonly Dictionary<Graphic, Color> _cachedColors = new Dictionary<Graphic, Color>();

        static StockToolbarHook()
        {
            NavBallHookService.HideStockToolbarAction = HideStockToolbar;
            NavBallHookService.ReskinStockToolbarAction = ReskinStockToolbar;
            NavBallHookService.RestoreStockToolbarAction = RestoreStockToolbar;
        }

        public static void Initialize()
        {
            // 确保静态构造函数触发
        }

        /// <summary>
        /// 彻底隐藏/打开原版工具栏
        /// </summary>
        public static void HideStockToolbar(bool hide)
        {
            _isStockHidden = hide;
            try
            {
#if !UNITY_EDITOR
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    GameObject go = KSP.UI.Screens.ApplicationLauncher.Instance.gameObject;
                    if (go != null)
                    {
                        var cg = go.GetComponent<CanvasGroup>();
                        if (cg == null) cg = go.AddComponent<CanvasGroup>();
                        cg.alpha = hide ? 0f : 1f;
                        cg.blocksRaycasts = !hide;
                        cg.interactable = !hide;
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockToolbar warning: {ex.Message}");
            }
        }

#if !UNITY_EDITOR
        private static readonly System.Reflection.FieldInfo AppListField = typeof(KSP.UI.Screens.ApplicationLauncher).GetField("appList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo AppListModField = typeof(KSP.UI.Screens.ApplicationLauncher).GetField("appListMod", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        public static List<KSP.UI.Screens.ApplicationLauncherButton> GetStockButtons(KSP.UI.Screens.ApplicationLauncher launcher)
        {
            if (launcher == null || AppListField == null) return null;
            return AppListField.GetValue(launcher) as List<KSP.UI.Screens.ApplicationLauncherButton>;
        }

        public static List<KSP.UI.Screens.ApplicationLauncherButton> GetModButtons(KSP.UI.Screens.ApplicationLauncher launcher)
        {
            if (launcher == null || AppListModField == null) return null;
            return AppListModField.GetValue(launcher) as List<KSP.UI.Screens.ApplicationLauncherButton>;
        }
#endif

        /// <summary>
        /// 原位超现代黑晶玻璃重肤 (In-Place Reskin)
        /// 替换老旧拟物边框为暗夜磨砂黑晶，黄黑施工斑马线替换为纳米激光分割线
        /// </summary>
        public static void ReskinStockToolbar()
        {
            try
            {
#if !UNITY_EDITOR
                if (KSP.UI.Screens.ApplicationLauncher.Instance == null) return;
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
                if (theme == null) return;

                Color bgCol = (Color)theme.FrameBgColor;
                Color borderCol = (Color)theme.FrameBorderColor;
                Color accent = (Color)theme.AccentSecondary;

                // 1. 重绘主背景框
                var images = launcher.GetComponentsInChildren<Image>(true);
                if (images != null)
                {
                    for (int i = 0; i < images.Length; i++)
                    {
                        Image img = images[i];
                        if (img == null) continue;

                        string goName = img.gameObject.name.ToLowerInvariant();
                        if (goName.Contains("background") || goName.Contains("frame") || goName.Contains("panel"))
                        {
                            if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                            img.color = bgCol;

                            var outline = img.GetComponent<Outline>();
                            if (outline == null) outline = img.gameObject.AddComponent<Outline>();
                            outline.effectColor = borderCol;
                            outline.effectDistance = new Vector2(1f, -1f);
                        }
                        else if (goName.Contains("divider") || goName.Contains("separator") || goName.Contains("stripe"))
                        {
                            if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                            img.color = accent; // 纳米激光分割线
                        }
                    }
                }

                // 2. 重绘全部按钮
                ReskinButtons(GetStockButtons(launcher), theme);
                ReskinButtons(GetModButtons(launcher), theme);

                _isReskinned = true;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ReskinStockToolbar warning: {ex.Message}");
            }
        }

#if !UNITY_EDITOR
        private static void ReskinButtons(List<KSP.UI.Screens.ApplicationLauncherButton> buttons, ThemeConfig theme)
        {
            if (buttons == null) return;

            Color tileNormal = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            Color borderCol = (Color)theme.FrameBorderColor;

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;

                // 去除拟物灰白底纹
                if (btn.container != null)
                {
                    var img = btn.container.GetComponent<Image>() ?? btn.container.GetComponentInChildren<Image>(true);
                    if (img != null)
                    {
                        if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                        img.color = tileNormal;

                        var outline = img.GetComponent<Outline>();
                        if (outline == null) outline = img.gameObject.AddComponent<Outline>();
                        outline.effectColor = borderCol;
                        outline.effectDistance = new Vector2(1f, -1f);
                    }
                }
            }
        }
#endif

        /// <summary>
        /// 恢复官方原始外观与材质
        /// </summary>
        public static void RestoreStockToolbar()
        {
            try
            {
                HideStockToolbar(false);

                foreach (var kvp in _cachedColors)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.color = kvp.Value;
                        var outline = kvp.Key.GetComponent<Outline>();
                        if (outline != null) UnityEngine.Object.Destroy(outline);
                    }
                }
                _cachedColors.Clear();
                _isReskinned = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RestoreStockToolbar warning: {ex.Message}");
            }
        }
    }
}
