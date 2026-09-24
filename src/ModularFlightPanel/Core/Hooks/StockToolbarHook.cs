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
        private static readonly List<Outline> _addedOutlines = new List<Outline>();

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
        /// 全局统一调度工具栏三大视觉模式：
        /// 0 = 原版经典 (Stock)
        /// 1 = 黑晶重肤 (Reskin)
        /// 2 = 折叠收纳坞 (Dock)
        /// </summary>
        public static void ApplyStyleMode(int mode)
        {
            switch (mode)
            {
                case 0: // 原版经典 (Stock)
                    RestoreStockToolbar();
                    break;
                case 1: // 黑晶重肤 (Reskin)
                    RestoreStockToolbar();
                    ReskinStockToolbar();
                    break;
                case 2: // 折叠收纳坞 (Dock)
                    RestoreStockToolbar();
                    HideStockToolbar(true);
                    break;
                default:
                    RestoreStockToolbar();
                    break;
            }
        }

        /// <summary>
        /// 彻底隐藏/打开原版工具栏
        /// </summary>
        public static void HideStockToolbar(bool hide)
        {
            _isStockHidden = hide;
            try
            {
#if KSP_RUNTIME
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    GameObject go = KSP.UI.Screens.ApplicationLauncher.Instance.gameObject;
                    if (go != null)
                    {
                        var cg = go.GetComponent<CanvasGroup>();
                        if (cg == null) cg = go.AddComponent<CanvasGroup>();
                        cg.alpha = hide ? 0f : 1f;
                        cg.blocksRaycasts = !hide;
                        cg.interactable = true;
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockToolbar warning: {ex.Message}");
            }
        }

#if KSP_RUNTIME
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
#if KSP_RUNTIME
                if (KSP.UI.Screens.ApplicationLauncher.Instance == null) return;
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
                if (theme == null) return;

                // 确保原版工具栏处于显示状态 (解除 alpha = 0)
                HideStockToolbar(false);

                Color bgCol = (Color)theme.FrameBgColor;
                Color borderCol = (Color)theme.FrameBorderColor;
                Color accent = (Color)theme.AccentPrimary;
                Color tileNormal = (Color)theme.FrameBgColor;

                // 1. 重绘主分割线与翻页滚动按钮
                if (launcher.CurrentLayout is KSP.UI.Screens.SimpleLayout simpleLayout)
                {
                    var divider = simpleLayout.GetModListDivider();
                    if (divider != null)
                    {
                        if (!_cachedColors.ContainsKey(divider)) _cachedColors[divider] = divider.color;
                        divider.color = accent;
                    }

                    var upBtn = simpleLayout.GetModListBtnUp();
                    if (upBtn != null)
                    {
                        var imgs = upBtn.GetComponentsInChildren<Image>(true);
                        for (int i = 0; i < imgs.Length; i++)
                        {
                            var img = imgs[i];
                            if (img != null)
                            {
                                if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                                img.color = accent;
                            }
                        }
                    }

                    var downBtn = simpleLayout.GetModListBtnDown();
                    if (downBtn != null)
                    {
                        var imgs = downBtn.GetComponentsInChildren<Image>(true);
                        for (int i = 0; i < imgs.Length; i++)
                        {
                            var img = imgs[i];
                            if (img != null)
                            {
                                if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                                img.color = accent;
                            }
                        }
                    }
                }

                // 2. 扫描并重绘全部按钮 (Stock 与 Mod 按钮全覆盖)
                var buttons = new HashSet<KSP.UI.Screens.ApplicationLauncherButton>();
                var stockBtns = GetStockButtons(launcher);
                if (stockBtns != null)
                {
                    for (int i = 0; i < stockBtns.Count; i++) if (stockBtns[i] != null) buttons.Add(stockBtns[i]);
                }
                var modBtns = GetModButtons(launcher);
                if (modBtns != null)
                {
                    for (int i = 0; i < modBtns.Count; i++) if (modBtns[i] != null) buttons.Add(modBtns[i]);
                }
                var childBtns = launcher.GetComponentsInChildren<KSP.UI.Screens.ApplicationLauncherButton>(true);
                if (childBtns != null)
                {
                    for (int i = 0; i < childBtns.Length; i++) if (childBtns[i] != null) buttons.Add(childBtns[i]);
                }

                foreach (var btn in buttons)
                {
                    ReskinSingleButton(btn, theme, tileNormal, borderCol, accent);
                }

                // 3. 挂载常驻监听器，保证飞行中动态加载的 Mod 按钮自动同步重肤
                EnsureWatcher(launcher.gameObject);

                _isReskinned = true;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ReskinStockToolbar warning: {ex.Message}");
            }
        }

#if KSP_RUNTIME
        private static void ReskinSingleButton(KSP.UI.Screens.ApplicationLauncherButton btn, ThemeConfig theme, Color tileNormal, Color borderCol, Color accent)
        {
            if (btn == null) return;

            bool isActive = (btn.toggleButton != null && btn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
            Color effectiveBorder = isActive ? accent : borderCol;
            Color activeTile = Color.Lerp(tileNormal, accent, 0.25f);
            Color effectiveTile = isActive ? activeTile : tileNormal;

            // 获取按钮背景底图 (通过 btn 子级 Image，排除 sprite 因为它是 RawImage 图标)
            var images = btn.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null) continue;

                if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                img.color = effectiveTile;

                var outline = img.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = img.gameObject.AddComponent<Outline>();
                    _addedOutlines.Add(outline);
                }
                outline.effectColor = effectiveBorder;
                outline.effectDistance = new Vector2(1f, -1f);
            }

            // 若 container 不在自身子级中，单独检查 container
            if (btn.container != null && !btn.container.transform.IsChildOf(btn.transform))
            {
                var containerImages = btn.container.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < containerImages.Length; i++)
                {
                    Image img = containerImages[i];
                    if (img == null) continue;

                    if (!_cachedColors.ContainsKey(img)) _cachedColors[img] = img.color;
                    img.color = effectiveTile;

                    var outline = img.GetComponent<Outline>();
                    if (outline == null)
                    {
                        outline = img.gameObject.AddComponent<Outline>();
                        _addedOutlines.Add(outline);
                    }
                    outline.effectColor = effectiveBorder;
                    outline.effectDistance = new Vector2(1f, -1f);
                }
            }
        }

        private static ToolbarReskinWatcher _watcher;

        private static void EnsureWatcher(GameObject launcherGo)
        {
            if (launcherGo == null) return;
            if (_watcher == null)
            {
                _watcher = launcherGo.GetComponent<ToolbarReskinWatcher>() ?? launcherGo.AddComponent<ToolbarReskinWatcher>();
            }
        }

        private class ToolbarReskinWatcher : MonoBehaviour
        {
            private float _lastCheck = 0f;
            private int _lastButtonCount = -1;

            private void Update()
            {
                if (!_isReskinned || _isStockHidden) return;

                float now = Time.unscaledTime;
                if (now - _lastCheck < 0.5f) return;
                _lastCheck = now;

                if (KSP.UI.Screens.ApplicationLauncher.Instance == null) return;
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                var stockBtns = GetStockButtons(launcher);
                var modBtns = GetModButtons(launcher);
                int count = (stockBtns != null ? stockBtns.Count : 0) + (modBtns != null ? modBtns.Count : 0);

                if (count != _lastButtonCount)
                {
                    _lastButtonCount = count;
                    ReskinStockToolbar();
                    return;
                }

                ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
                if (theme == null) return;
                Color borderCol = (Color)theme.FrameBorderColor;
                Color accent = (Color)theme.AccentPrimary;

                if (stockBtns != null)
                {
                    for (int i = 0; i < stockBtns.Count; i++) UpdateOutline(stockBtns[i], borderCol, accent);
                }
                if (modBtns != null)
                {
                    for (int i = 0; i < modBtns.Count; i++) UpdateOutline(modBtns[i], borderCol, accent);
                }
            }

            private void UpdateOutline(KSP.UI.Screens.ApplicationLauncherButton btn, Color borderCol, Color accent)
            {
                if (btn == null || btn.toggleButton == null) return;
                bool isActive = (btn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
                var outlines = btn.GetComponentsInChildren<Outline>(true);
                for (int i = 0; i < outlines.Length; i++)
                {
                    if (outlines[i] != null) outlines[i].effectColor = isActive ? accent : borderCol;
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
                    }
                }
                _cachedColors.Clear();

                for (int i = 0; i < _addedOutlines.Count; i++)
                {
                    if (_addedOutlines[i] != null)
                    {
                        UnityEngine.Object.Destroy(_addedOutlines[i]);
                    }
                }
                _addedOutlines.Clear();

                _isReskinned = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RestoreStockToolbar warning: {ex.Message}");
            }
        }
    }
}
