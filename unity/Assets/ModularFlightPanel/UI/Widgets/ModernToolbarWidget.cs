using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// UGUI 事件转发代理组件：精确转发左键、右键以及鼠标悬浮事件至原版/Mod工具栏按钮
    /// </summary>
    public class ModernToolbarButtonProxy : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;
        public Action OnHoverEnter;
        public Action OnHoverExit;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnLeftClick?.Invoke();
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnHoverEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnHoverExit?.Invoke();
        }
    }

    /// <summary>
    /// 现代化航电收纳坞工具栏组件 (Modern Avionics Toolbar Dock Suite - core.toolbar)
    /// 
    /// 解决 20+ MOD 导致的“贪吃蛇长龙”与老旧拟物边框：
    /// 1. 完整保留所有模组原始高清贴图图标 (RawImage Icon Texture Preservation)
    /// 2. 动态自动适配全量模组无上限 (Dynamic Mod Auto-Discovery & Hot-Plug)
    /// 3. 完整支持左键开关、右键设置菜单与悬浮工具提示 (Full Event Proxying: Left/Right Click & Tooltips)
    /// 4. 智能自适应高度与平滑滚动视口 (2-Column Auto-Sizing & ScrollRect Matrix)
    /// 5. 极简微光折叠胶囊 (Collapsed 36x36px Pill)：随时收纳，还给驾驶舱纯净视野
    /// </summary>
    public class ModernToolbarWidget : BaseFlightWidget
    {
        private Image _panelBg;
        private Outline _panelOutline;
        private Image _topStripe;

        // 折叠 / 展开状态机
        private bool _isCollapsed = false;
        private Button _collapseBtn;
        private Text _collapseBtnText;
        private GameObject _dockContent;
        private ScrollRect _scrollRect;
        private RectTransform _contentRt;

        // 模拟/真实按钮项模型
        private class ToolbarItemView
        {
            public GameObject Root;
            public Image Background;
            public RawImage IconRaw;
            public Text LabelText;
            public Image ActiveLed;
            public ModernToolbarButtonProxy Proxy;
            public string Name;
            public bool IsActive;
            public Action OnLeftClick;
            public Action OnRightClick;

#if !UNITY_EDITOR
            public KSP.UI.Screens.ApplicationLauncherButton KspButton;
#endif
        }

        private readonly List<ToolbarItemView> _itemViews = new List<ToolbarItemView>();
        private float _lastSyncTime = -1f;
        private int _cachedButtonCount = -1;
        private ThemeConfig _currentTheme;
        private float _currentPanelHeight = 240f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _currentTheme = theme;
            float s = CurrentDpiScale;

            Color bgCol = (theme != null) ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.92f);
            Color borderCol = (theme != null) ? (Color)theme.FrameBorderColor : Color.cyan;
            Color primaryAccent = (theme != null) ? (Color)theme.AccentPrimary : Color.green;

            // 初始尺寸 (双列宽度约 88px)
            _currentPanelHeight = 240f * s;
            Vector2 panelSize = new Vector2(88f * s, _currentPanelHeight);
            RectTransform.sizeDelta = panelSize;

            // 1. 主面板背板与外发光轮廓
            GameObject panel = UIFactory.CreatePanel(transform, "ToolbarPanel", panelSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
            _panelBg = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();

            // 2. 顶部微光装饰线
            _topStripe = UIFactory.CreatePanel(panel.transform, "TopStripe", new Vector2(panelSize.x, 2f * s),
                new Vector2(0f, panelSize.y * 0.5f - 1f * s), primaryAccent).GetComponent<Image>();

            // 3. 顶部收拢 / 展开按键
            float headerH = 22f * s;
            Button btn = UIFactory.CreateCockpitButton(panel.transform, "CollapseToggleBtn",
                "« DOCK",
                new Vector2(panelSize.x - 8f * s, headerH),
                new Vector2(0f, panelSize.y * 0.5f - (headerH * 0.5f + 4f * s)),
                new Color(0.08f, 0.14f, 0.22f, 0.9f), borderCol, primaryAccent,
                () => ToggleCollapse());
            _collapseBtn = btn;
            _collapseBtnText = btn.GetComponentInChildren<Text>();

            // 4. 按钮矩阵滚动视口 (Scroll View + Viewport + Content)
            _dockContent = new GameObject("DockScrollView", typeof(RectTransform), typeof(ScrollRect));
            _dockContent.transform.SetParent(panel.transform, false);
            RectTransform scrollRt = _dockContent.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(4f * s, 4f * s);
            scrollRt.offsetMax = new Vector2(-4f * s, -(headerH + 8f * s));

            _scrollRect = _dockContent.GetComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 18f * s;

            // Viewport with RectMask2D
            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(_dockContent.transform, false);
            RectTransform vpRt = viewportObj.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            _scrollRect.viewport = vpRt;

            // Content RectTransform
            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            _contentRt = contentObj.GetComponent<RectTransform>();
            _contentRt.anchorMin = new Vector2(0f, 1f);
            _contentRt.anchorMax = new Vector2(1f, 1f);
            _contentRt.pivot = new Vector2(0.5f, 1f);
            _contentRt.offsetMin = Vector2.zero;
            _contentRt.offsetMax = Vector2.zero;
            _scrollRect.content = _contentRt;

            // 5. 组装按钮列表 (真机读 ApplicationLauncher，离线读 Mock)
            PopulateToolbarButtons(_contentRt, s);

            // 隐藏原版工具栏
            StockToolbarHook.HideStockToolbar(true);
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            float s = CurrentDpiScale;

            if (_isCollapsed)
            {
                RectTransform.sizeDelta = new Vector2(38f * s, 38f * s);
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = new Vector2(38f * s, 38f * s);
                if (_topStripe != null) _topStripe.gameObject.SetActive(false);
                if (_dockContent != null) _dockContent.SetActive(false);
                if (_collapseBtn != null)
                {
                    _collapseBtn.image.rectTransform.sizeDelta = new Vector2(32f * s, 32f * s);
                    _collapseBtn.image.rectTransform.anchoredPosition = Vector2.zero;
                    if (_collapseBtnText != null) _collapseBtnText.text = "»";
                }
            }
            else
            {
                Vector2 panelSize = new Vector2(88f * s, _currentPanelHeight);
                RectTransform.sizeDelta = panelSize;
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                if (_topStripe != null)
                {
                    _topStripe.gameObject.SetActive(true);
                    _topStripe.rectTransform.sizeDelta = new Vector2(panelSize.x, 2f * s);
                    _topStripe.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - 1f * s);
                }
                if (_dockContent != null) _dockContent.SetActive(true);
                if (_collapseBtn != null)
                {
                    float headerH = 22f * s;
                    _collapseBtn.image.rectTransform.sizeDelta = new Vector2(panelSize.x - 8f * s, headerH);
                    _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - (headerH * 0.5f + 4f * s));
                    if (_collapseBtnText != null) _collapseBtnText.text = "« DOCK";
                }
            }
        }

        private void PopulateToolbarButtons(RectTransform contentParent, float s)
        {
            // 清理既有子物体
            for (int i = 0; i < _itemViews.Count; i++)
            {
                if (_itemViews[i]?.Root != null)
                {
                    Destroy(_itemViews[i].Root);
                }
            }
            _itemViews.Clear();

            bool hasRealLauncher = false;
#if !UNITY_EDITOR
            try
            {
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                    List<KSP.UI.Screens.ApplicationLauncherButton> allButtons = new List<KSP.UI.Screens.ApplicationLauncherButton>();
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    if (stockBtns != null) allButtons.AddRange(stockBtns);
                    if (modBtns != null) allButtons.AddRange(modBtns);

                    _cachedButtonCount = allButtons.Count;
                    if (allButtons.Count > 0)
                    {
                        hasRealLauncher = true;
                        BuildButtonsFromKsp(contentParent, allButtons, s);
                    }
                }
            }
            catch { }
#endif

            if (!hasRealLauncher)
            {
                BuildMockButtons(contentParent, s);
            }
        }

#if !UNITY_EDITOR
        private void BuildButtonsFromKsp(RectTransform parent, List<KSP.UI.Screens.ApplicationLauncherButton> buttons, float s)
        {
            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;
            float startX = -(btnW * 0.5f + spacing * 0.5f);

            int total = buttons.Count;
            int rows = Mathf.Max(1, Mathf.CeilToInt(total / 2f));
            float contentHeight = rows * (btnH + spacing) + 4f * s;
            parent.sizeDelta = new Vector2(parent.sizeDelta.x, contentHeight);

            // 自适应外框高度：在 140px ~ 420px 之间弹性调节
            float preferredH = Mathf.Clamp(contentHeight + 36f * s, 140f * s, 420f * s);
            _currentPanelHeight = preferredH;
            if (!_isCollapsed)
            {
                RectTransform.sizeDelta = new Vector2(88f * s, _currentPanelHeight);
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = RectTransform.sizeDelta;
                if (_topStripe != null) _topStripe.rectTransform.anchoredPosition = new Vector2(0f, _currentPanelHeight * 0.5f - 1f * s);
                if (_collapseBtn != null)
                {
                    float headerH = 22f * s;
                    _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, _currentPanelHeight * 0.5f - (headerH * 0.5f + 4f * s));
                }
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                var kspBtn = buttons[i];
                if (kspBtn == null) continue;

                int col = i % 2;
                int row = i / 2;
                float x = col == 0 ? startX : -startX;
                float y = -(row * (btnH + spacing) + btnH * 0.5f + 2f * s);

                // 获取真实模组贴图与状态
                Texture iconTex = (kspBtn.sprite != null) ? kspBtn.sprite.texture : null;
                bool active = (kspBtn.toggleButton != null && kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);

                CreateButtonItem(parent, x, y, btnW, btnH, kspBtn.name ?? $"App_{i}", iconTex, null,
                    onLeftClick: () =>
                    {
                        try
                        {
                            if (kspBtn.toggleButton != null)
                            {
                                if (kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True)
                                    kspBtn.SetFalse(true);
                                else
                                    kspBtn.SetTrue(true);
                            }
                            else if (kspBtn.onLeftClick != null)
                            {
                                kspBtn.onLeftClick.Invoke();
                            }
                        }
                        catch { }
                    },
                    onRightClick: () =>
                    {
                        try
                        {
                            if (kspBtn.onRightClick != null) kspBtn.onRightClick.Invoke();
                        }
                        catch { }
                    },
                    onHoverEnter: () =>
                    {
                        try
                        {
                            if (kspBtn.onHover != null) kspBtn.onHover.Invoke();
                        }
                        catch { }
                    },
                    onHoverExit: () =>
                    {
                        try
                        {
                            if (kspBtn.onHoverOut != null) kspBtn.onHoverOut.Invoke();
                        }
                        catch { }
                    },
                    s: s,
                    initialActive: active,
                    kspBtnRef: kspBtn);
            }
        }
#endif

        private void BuildMockButtons(RectTransform parent, float s)
        {
            string[] mockNames = { "RES", "COMM", "TIME", "MJ", "FAR", "RA", "ALARM", "MFP" };
            Color[] mockColors = { Color.cyan, Color.green, Color.yellow, Color.magenta, Color.cyan, new Color(0.2f, 0.6f, 1f), Color.red, Color.green };

            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;
            float startX = -(btnW * 0.5f + spacing * 0.5f);

            int total = mockNames.Length;
            int rows = Mathf.Max(1, Mathf.CeilToInt(total / 2f));
            float contentHeight = rows * (btnH + spacing) + 4f * s;
            parent.sizeDelta = new Vector2(parent.sizeDelta.x, contentHeight);

            float preferredH = Mathf.Clamp(contentHeight + 36f * s, 140f * s, 420f * s);
            _currentPanelHeight = preferredH;
            if (!_isCollapsed)
            {
                RectTransform.sizeDelta = new Vector2(88f * s, _currentPanelHeight);
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = RectTransform.sizeDelta;
                if (_topStripe != null) _topStripe.rectTransform.anchoredPosition = new Vector2(0f, _currentPanelHeight * 0.5f - 1f * s);
                if (_collapseBtn != null)
                {
                    float headerH = 22f * s;
                    _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, _currentPanelHeight * 0.5f - (headerH * 0.5f + 4f * s));
                }
            }

            for (int i = 0; i < mockNames.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = col == 0 ? startX : -startX;
                float y = -(row * (btnH + spacing) + btnH * 0.5f + 2f * s);

                string name = mockNames[i];
                Color ledCol = mockColors[i];
                bool active = (i % 3 == 0);

                CreateButtonItem(parent, x, y, btnW, btnH, name, null, ledCol, null, null, null, null, s, active);
            }
        }

        private void CreateButtonItem(RectTransform parent, float x, float y, float w, float h,
            string label, Texture iconTex, Color? accent,
            Action onLeftClick, Action onRightClick, Action onHoverEnter, Action onHoverExit,
            float s, bool initialActive = false
#if !UNITY_EDITOR
            , KSP.UI.Screens.ApplicationLauncherButton kspBtnRef = null
#endif
        )
        {
            Color tileBg = new Color(0.06f, 0.11f, 0.18f, 0.88f);
            Color borderCol = (_currentTheme != null) ? (Color)_currentTheme.FrameBorderColor : Color.cyan;
            Color ledCol = accent ?? ((_currentTheme != null) ? (Color)_currentTheme.AccentPrimary : Color.green);

            GameObject itemObj = UIFactory.CreatePanel(parent, $"Item_{label}", new Vector2(w, h), new Vector2(x, y), tileBg, borderCol, 1f * s);
            RectTransform itemRt = itemObj.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0.5f, 1f);
            itemRt.anchorMax = new Vector2(0.5f, 1f);
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.anchoredPosition = new Vector2(x, y);

            Image bg = itemObj.GetComponent<Image>();

            // 1. 真实图标贴图渲染层 (RawImage) - 完美保留所有模组原始 Icon 质感
            RawImage rawImg = null;
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            iconObj.transform.SetParent(itemObj.transform, false);
            rawImg = iconObj.GetComponent<RawImage>();
            rawImg.color = Color.white;
            RectTransform irt = rawImg.rectTransform;
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(w - 8f * s, h - 8f * s);
            irt.anchoredPosition = Vector2.zero;

            // 2. 备用文本缩写标签 (无贴图或贴图未载入时显示)
            Text lbl = UIFactory.CreateText(itemObj.transform, "Label", label, Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, Color.white);
            RectTransform lblRt = lbl.GetComponent<RectTransform>();
            lblRt.anchoredPosition = Vector2.zero;
            lblRt.sizeDelta = new Vector2(w - 4f * s, h - 4f * s);

            if (iconTex != null)
            {
                rawImg.texture = iconTex;
                rawImg.gameObject.SetActive(true);
                lbl.gameObject.SetActive(false);
            }
            else
            {
                rawImg.gameObject.SetActive(false);
                lbl.gameObject.SetActive(true);
            }

            // 3. 顶部激活状态微光 LED (3x3px)
            GameObject ledObj = UIFactory.CreatePanel(itemObj.transform, "ActiveLed", new Vector2(3.5f * s, 3.5f * s),
                new Vector2(-w * 0.5f + 3.5f * s, h * 0.5f - 3.5f * s), initialActive ? ledCol : new Color(0.2f, 0.25f, 0.3f, 0.45f));
            Image ledImg = ledObj.GetComponent<Image>();

            // 4. 事件转发代理 (左键开/关、右键菜单、悬浮提示)
            ModernToolbarButtonProxy proxy = itemObj.AddComponent<ModernToolbarButtonProxy>();
            proxy.OnLeftClick = () =>
            {
                onLeftClick?.Invoke();
            };
            proxy.OnRightClick = onRightClick;
            proxy.OnHoverEnter = onHoverEnter;
            proxy.OnHoverExit = onHoverExit;

            ToolbarItemView view = new ToolbarItemView
            {
                Root = itemObj,
                Background = bg,
                IconRaw = rawImg,
                LabelText = lbl,
                ActiveLed = ledImg,
                Proxy = proxy,
                Name = label,
                IsActive = initialActive,
                OnLeftClick = onLeftClick,
                OnRightClick = onRightClick
#if !UNITY_EDITOR
                , KspButton = kspBtnRef
#endif
            };

            _itemViews.Add(view);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (_isCollapsed) return;

            // 节流刷新 (3 Hz)，兼顾超低开销与模组热插拔瞬时捕获
            float now = Time.unscaledTime;
            if (now - _lastSyncTime < 0.33f) return;
            _lastSyncTime = now;

#if !UNITY_EDITOR
            try
            {
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    int currentCount = (stockBtns != null ? stockBtns.Count : 0) + (modBtns != null ? modBtns.Count : 0);

                    // 1. 动态自动适配：如果模组数量发生变化 (例如第三方 Mod 在飞行中动态注入或移除按钮)，实时自适应重构
                    if (currentCount != _cachedButtonCount)
                    {
                        PopulateToolbarButtons(_contentRt, CurrentDpiScale);
                        return;
                    }

                    // 2. 贴图与状态实时热同步
                    Color ledOn = (_currentTheme != null) ? (Color)_currentTheme.AccentPrimary : Color.green;
                    Color ledOff = new Color(0.2f, 0.25f, 0.3f, 0.45f);

                    for (int i = 0; i < _itemViews.Count; i++)
                    {
                        var view = _itemViews[i];
                        if (view == null || view.KspButton == null) continue;

                        var kspBtn = view.KspButton;

                        // 同步可能延迟加载或由 ToolbarControl 切换的贴图
                        if (kspBtn.sprite != null && kspBtn.sprite.texture != null)
                        {
                            if (view.IconRaw != null && view.IconRaw.texture != kspBtn.sprite.texture)
                            {
                                view.IconRaw.texture = kspBtn.sprite.texture;
                                view.IconRaw.gameObject.SetActive(true);
                                if (view.LabelText != null) view.LabelText.gameObject.SetActive(false);
                            }
                        }

                        // 同步激活状态
                        if (view.ActiveLed != null)
                        {
                            bool active = (kspBtn.toggleButton != null && kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
                            view.IsActive = active;
                            view.ActiveLed.color = active ? ledOn : ledOff;
                        }
                    }
                }
            }
            catch { }
#endif
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;

            Color bgCol = (Color)theme.FrameBgColor;
            Color borderCol = (Color)theme.FrameBorderColor;
            Color primaryAccent = (Color)theme.AccentPrimary;

            if (_panelBg != null) _panelBg.color = bgCol;
            if (_panelOutline != null) _panelOutline.effectColor = borderCol;
            if (_topStripe != null) _topStripe.color = primaryAccent;
            if (_collapseBtn != null)
            {
                var img = _collapseBtn.GetComponent<Image>();
                if (img != null) img.color = new Color(0.08f, 0.14f, 0.22f, 0.9f);
            }
            if (_collapseBtnText != null) _collapseBtnText.color = primaryAccent;
        }

        private void OnDestroy()
        {
            StockToolbarHook.HideStockToolbar(false);
        }
    }
}
