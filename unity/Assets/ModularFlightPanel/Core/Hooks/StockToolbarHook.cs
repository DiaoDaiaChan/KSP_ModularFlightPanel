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

        // 永久缓存原版初始颜色，绝不覆盖已有记录，杜绝重复重肤导致重绘颜色污染为原始颜色的恶性循环！
        private static readonly Dictionary<Graphic, Color> _originalStockColors = new Dictionary<Graphic, Color>();
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
#if KSP_RUNTIME
            if (!HighLogic.LoadedSceneIsFlight)
            {
                ApplyNonFlightStyleMode();
                return;
            }
#endif
            switch (mode)
            {
                case 0: // 原版经典 (Stock)
                    RestoreStockToolbar();
                    break;
                case 1: // 黑晶重肤 (Reskin)
                    ReskinStockToolbar();
                    break;
                case 2: // 折叠收纳坞 (Dock)
                    HideStockToolbar(true);
                    break;
                default:
                    RestoreStockToolbar();
                    break;
            }
        }

        /// <summary>
        /// 调度非飞行场景 (航天中心 / VAB / SPH / 追踪站等) 的工具栏视觉状态：
        /// 0 = 恢复原版经典 (Restore Stock)
        /// 1 = 保持黑晶重肤 (Keep Reskin Hook)
        /// 绝不隐藏原版工具栏，100% 保障按钮可点击与可交互。
        /// </summary>
        public static void ApplyNonFlightStyleMode()
        {
            // 确保原版工具栏解除隐藏
            HideStockToolbar(false);

            int globalMode = ThemeManager.Instance != null ? ThemeManager.Instance.ToolbarStyleMode : 1;
            int nonFlightMode = ThemeManager.Instance != null ? ThemeManager.Instance.NonFlightToolbarMode : 1;

            if (globalMode == 0 || nonFlightMode == 0)
            {
                RestoreStockToolbar();
            }
            else
            {
                ReskinStockToolbar();
            }
        }

        /// <summary>
        /// 彻底隐藏/打开原版工具栏 (纯净 CanvasGroup 控制，绝不添加 Sub-Canvas 以免破坏摄像机绑定引发闪烁与丢失)
        /// </summary>
        public static void HideStockToolbar(bool hide)
        {
#if KSP_RUNTIME
            // 关键安全防线：非飞行场景 (航天中心 / VAB / SPH / 追踪站等) 绝对禁止隐藏原版工具栏！
            // 因为收纳坞 (ModernToolbarWidget) 仅在飞行场景运行，非飞行场景隐藏将导致玩家无工具栏可用。
            if (hide && !HighLogic.LoadedSceneIsFlight)
            {
                Debug.LogWarning("[ModularFlightPanel] Blocked attempt to hide stock toolbar in non-flight scene.");
                _isStockHidden = false;
                hide = false;
            }
#endif
            _isStockHidden = hide;

#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    GameObject go = KSP.UI.Screens.ApplicationLauncher.Instance.gameObject;
                    if (go != null)
                    {
                        // 1. 彻底销毁历史可能附着的 Sub-Canvas，恢复原生画布继承，彻底杜绝摄像机丢失引发的逐帧闪烁与黑洞
                        Canvas subCanvas = go.GetComponent<Canvas>();
                        if (subCanvas != null)
                        {
                            UnityEngine.Object.Destroy(subCanvas);
                        }

                        // 2. 纯净 CanvasGroup 隐蔽与显隐：零开销、零摄像机绑定风险、零剔除冲突
                        var cg = go.GetComponent<CanvasGroup>();
                        if (hide)
                        {
                            if (cg == null) cg = go.AddComponent<CanvasGroup>();
                            cg.alpha = 0f;
                            cg.blocksRaycasts = false;
                            cg.interactable = false;
                        }
                        else
                        {
                            if (cg != null)
                            {
                                int curMode = ThemeManager.Instance != null ? ThemeManager.Instance.ToolbarStyleMode : 0;
                                if (curMode == 0)
                                {
                                    // 模式 0 下彻底移除 CanvasGroup，完全恢复官方原始运行环境
                                    UnityEngine.Object.Destroy(cg);
                                }
                                else
                                {
                                    cg.alpha = 1f;
                                    cg.blocksRaycasts = true;
                                    cg.interactable = true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockToolbar warning: {ex.Message}");
            }
#endif
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
                        if (!_originalStockColors.ContainsKey(divider)) _originalStockColors[divider] = divider.color;
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
                                if (!_originalStockColors.ContainsKey(img)) _originalStockColors[img] = img.color;
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
                                if (!_originalStockColors.ContainsKey(img)) _originalStockColors[img] = img.color;
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
                if (_watcher != null)
                {
                    _watcher.SetButtonCount(buttons.Count);
                }

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

            // 安全重绘底图：优先仅修改 toggleButton 背景 Image，坚决不染黑图标 (RawImage / 模组自定义 Icon Image)
            Image bgImg = null;
            if (btn.toggleButton != null)
            {
                bgImg = btn.toggleButton.GetComponent<Image>();
            }
            if (bgImg == null)
            {
                var imgs = btn.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < imgs.Length; i++)
                {
                    if (imgs[i] != null && !imgs[i].name.ToLowerInvariant().Contains("icon"))
                    {
                        bgImg = imgs[i];
                        break;
                    }
                }
            }

            if (bgImg != null)
            {
                if (!_originalStockColors.ContainsKey(bgImg)) _originalStockColors[bgImg] = bgImg.color;
                bgImg.color = effectiveTile;

                var outline = bgImg.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = bgImg.gameObject.AddComponent<Outline>();
                    _addedOutlines.Add(outline);
                }
                outline.enabled = true;
                outline.effectColor = effectiveBorder;
                outline.effectDistance = new Vector2(1f, -1f);
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

            public void SetButtonCount(int count)
            {
                _lastButtonCount = count;
                _lastCheck = Time.unscaledTime;
            }

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

                ThemeConfig theme = ThemeManager.Instance != null ? ThemeManager.Instance.CurrentTheme : null;
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
                Color targetCol = isActive ? accent : borderCol;
                var outlines = btn.GetComponentsInChildren<Outline>(false); // 仅扫描当前激活且生效的 Outline
                for (int i = 0; i < outlines.Length; i++)
                {
                    if (outlines[i] != null && outlines[i].enabled && outlines[i].effectColor != targetCol)
                    {
                        outlines[i].effectColor = targetCol;
                    }
                }
            }
        }
#endif

        /// <summary>
        /// 恢复官方原始外观与材质 (安全还原，零帧延迟，零 GC)
        /// </summary>
        public static void RestoreStockToolbar()
        {
            try
            {
                HideStockToolbar(false);
#if KSP_RUNTIME
                DockAnchorTracker.ClearAll();
#endif
                // 还原所有已缓存的原版初始颜色 (字典永久保留，绝不因重复切换而被重绘颜色污染)
                foreach (var kvp in _originalStockColors)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.color = kvp.Value;
                    }
                }

                // 禁用全部添加的外边框，无需销毁 GameObject 组件，消除帧末销毁延迟与闪烁
                for (int i = 0; i < _addedOutlines.Count; i++)
                {
                    if (_addedOutlines[i] != null)
                    {
                        _addedOutlines[i].enabled = false;
                    }
                }

                _isReskinned = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RestoreStockToolbar warning: {ex.Message}");
            }
        }
    }
}
