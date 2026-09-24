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
        public Action<PointerEventData> OnLeftClick;
        public Action<PointerEventData> OnRightClick;
        public Action<PointerEventData> OnHoverEnter;
        public Action<PointerEventData> OnHoverExit;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnLeftClick?.Invoke(eventData);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke(eventData);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnHoverEnter?.Invoke(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnHoverExit?.Invoke(eventData);
        }
    }

    /// <summary>
    /// 现代化航电收纳坞工具栏组件 (Modern Avionics Toolbar Dock Suite - core.toolbar)
    /// 
    /// 解决 20+ MOD 导致的“贪吃蛇长龙”与老旧拟物边框：
    /// 1. 完整保留所有模组原始高清贴图图标 (RawImage Icon Texture Preservation)
    /// 2. 动态自动适配全量模组无上限 (Dynamic Mod Auto-Discovery & Hot-Plug)
    /// 3. 完整支持左键开关、右键设置菜单与悬浮工具提示 (Full Event Proxying: Left/Right Click & Tooltips)
    /// 4. 多构型自适应矩阵：支持纵向双列、横向双行、横向单行 (Multi-Orientation Matrix Layout)
    /// 5. 极简微光折叠胶囊 (Collapsed 38x38px Pill)：随时收纳，还给驾驶舱纯净视野
    /// </summary>
    public class ModernToolbarWidget : BaseFlightWidget
    {
        public static ModernToolbarWidget Instance { get; private set; }

        public override bool IsInteractive => true;

        private Image _panelBg;
        private Outline _panelOutline;
        private Image _topStripe;

        // 折叠 / 展开状态机
        private bool _isCollapsed = false;
        private bool _drawerExpanded = false;
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
            public Action<PointerEventData> OnLeftClick;
            public Action<PointerEventData> OnRightClick;

#if KSP_RUNTIME
            public KSP.UI.Screens.ApplicationLauncherButton KspButton;
#endif
        }

        private readonly List<ToolbarItemView> _itemViews = new List<ToolbarItemView>();
        private float _lastSyncTime = -1f;
        private int _cachedButtonCount = -1;
        private ThemeConfig _currentTheme;
        private float _currentPanelWidth = 240f;
        private float _currentPanelHeight = 240f;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        public void RefreshToolbarButtons()
        {
            if (_contentRt != null)
            {
                PopulateToolbarButtons(_contentRt, CurrentDpiScale);
            }
        }

        public void RebuildDockLayout()
        {
            if (RectTransform == null) return;
            SetupDockStructure();
            PopulateToolbarButtons(_contentRt, CurrentDpiScale);
            AutoDetectInteractivityAndPruneRaycasts();
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Instance = this;
            theme = WidgetStyleManager.ResolveTheme(theme);
            _currentTheme = theme;
            float s = CurrentDpiScale;

            SetupDockStructure();
            PopulateToolbarButtons(_contentRt, s);

            // 模式 2 激活时隐藏原版工具栏
            if (ThemeManager.Instance.ToolbarStyleMode == 2)
            {
                StockToolbarHook.HideStockToolbar(true);
            }

            AutoDetectInteractivityAndPruneRaycasts();
        }

        private void SetupDockStructure()
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = _currentTheme ?? WidgetStyleManager.Instance.CurrentTheme;
            Color bgCol = theme.FrameBgColor;
            Color borderCol = theme.FrameBorderColor;
            Color primaryAccent = theme.AccentPrimary;
            int orient = ThemeManager.Instance.DockOrientation;

            // 1. 根据构型计算初始面板尺寸与外发光轮廓
            Vector2 initialSize;
            if (orient == 0)
            {
                // 纵向双列
                _currentPanelHeight = 240f * s;
                initialSize = new Vector2(88f * s, _currentPanelHeight);
            }
            else if (orient == 1)
            {
                // 横向双行
                _currentPanelWidth = 240f * s;
                initialSize = new Vector2(_currentPanelWidth, 88f * s);
            }
            else
            {
                // 横向单行
                _currentPanelWidth = 240f * s;
                initialSize = new Vector2(_currentPanelWidth, 48f * s);
            }

            RectTransform.sizeDelta = initialSize;

            if (_panelBg == null)
            {
                GameObject panel = UIFactory.CreatePanel(transform, "ToolbarPanel", initialSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
                _panelBg = panel.GetComponent<Image>();
                _panelOutline = panel.GetComponent<Outline>();
            }
            else
            {
                _panelBg.rectTransform.sizeDelta = initialSize;
            }

            // 2. 装饰微光边条
            if (_topStripe == null)
            {
                _topStripe = UIFactory.CreatePanel(_panelBg.transform, "AccentStripe", Vector2.one, Vector2.zero, primaryAccent).GetComponent<Image>();
            }
            if (orient == 0)
            {
                _topStripe.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _topStripe.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _topStripe.rectTransform.sizeDelta = new Vector2(initialSize.x, 2f * s);
                _topStripe.rectTransform.anchoredPosition = new Vector2(0f, initialSize.y * 0.5f - 1f * s);
            }
            else
            {
                _topStripe.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _topStripe.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, initialSize.y);
                _topStripe.rectTransform.anchoredPosition = new Vector2(-initialSize.x * 0.5f + 1f * s, 0f);
            }

            // 3. 折叠/展开按键
            if (_collapseBtn == null)
            {
                Button btn = UIFactory.CreateCockpitButton(_panelBg.transform, "CollapseToggleBtn",
                    orient == 0 ? "« DOCK" : "«",
                    new Vector2(32f * s, 22f * s),
                    Vector2.zero,
                    WidgetStyleManager.Surface(SurfaceStyleRole.SlotActive), borderCol, primaryAccent,
                    () => ToggleCollapse());
                _collapseBtn = btn;
                _collapseBtnText = btn.GetComponentInChildren<Text>();
            }

            if (orient == 0)
            {
                float headerH = 22f * s;
                _collapseBtn.image.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _collapseBtn.image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _collapseBtn.image.rectTransform.sizeDelta = new Vector2(initialSize.x - 8f * s, headerH);
                _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, initialSize.y * 0.5f - (headerH * 0.5f + 4f * s));
                if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "»" : "« DOCK";
            }
            else
            {
                float btnW = 22f * s;
                _collapseBtn.image.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _collapseBtn.image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnW, initialSize.y - 8f * s);
                _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-initialSize.x * 0.5f + (btnW * 0.5f + 4f * s), 0f);
                if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "»" : "«";
            }

            // 4. 滚动视口容器
            if (_dockContent == null)
            {
                _dockContent = new GameObject("DockScrollView", typeof(RectTransform), typeof(ScrollRect));
                _dockContent.transform.SetParent(_panelBg.transform, false);
                _scrollRect = _dockContent.GetComponent<ScrollRect>();
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
                _scrollRect.content = _contentRt;
            }

            RectTransform scrollRt = _dockContent.GetComponent<RectTransform>();
            if (orient == 0)
            {
                float headerH = 22f * s;
                scrollRt.anchorMin = new Vector2(0f, 0f);
                scrollRt.anchorMax = new Vector2(1f, 1f);
                scrollRt.offsetMin = new Vector2(4f * s, 4f * s);
                scrollRt.offsetMax = new Vector2(-4f * s, -(headerH + 8f * s));

                _scrollRect.horizontal = false;
                _scrollRect.vertical = true;

                _contentRt.anchorMin = new Vector2(0f, 1f);
                _contentRt.anchorMax = new Vector2(1f, 1f);
                _contentRt.pivot = new Vector2(0.5f, 1f);
                _contentRt.offsetMin = Vector2.zero;
                _contentRt.offsetMax = Vector2.zero;
            }
            else
            {
                float leftGripW = 28f * s;
                scrollRt.anchorMin = new Vector2(0f, 0f);
                scrollRt.anchorMax = new Vector2(1f, 1f);
                scrollRt.offsetMin = new Vector2(leftGripW, 4f * s);
                scrollRt.offsetMax = new Vector2(-4f * s, -4f * s);

                _scrollRect.horizontal = true;
                _scrollRect.vertical = false;

                _contentRt.anchorMin = new Vector2(0f, 0f);
                _contentRt.anchorMax = new Vector2(0f, 1f);
                _contentRt.pivot = new Vector2(0f, 0.5f);
                _contentRt.offsetMin = Vector2.zero;
                _contentRt.offsetMax = Vector2.zero;
            }
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            float s = CurrentDpiScale;
            int orient = ThemeManager.Instance.DockOrientation;

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
                if (orient == 0)
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
                else if (orient == 1)
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 88f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.gameObject.SetActive(true);
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_dockContent != null) _dockContent.SetActive(true);
                    if (_collapseBtn != null)
                    {
                        float btnW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnW * 0.5f + 4f * s), 0f);
                        if (_collapseBtnText != null) _collapseBtnText.text = "«";
                    }
                }
                else
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 48f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.gameObject.SetActive(true);
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_dockContent != null) _dockContent.SetActive(true);
                    if (_collapseBtn != null)
                    {
                        float btnW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnW * 0.5f + 4f * s), 0f);
                        if (_collapseBtnText != null) _collapseBtnText.text = "«";
                    }
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
#if KSP_RUNTIME
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

#if KSP_RUNTIME
        public static void GetButtonIdentity(KSP.UI.Screens.ApplicationLauncherButton btn, int index, out string key, out string defaultName)
        {
            string texName = (btn != null && btn.sprite != null && btn.sprite.texture != null) ? btn.sprite.texture.name : null;
            string goName = (btn != null && btn.gameObject != null) ? btn.gameObject.name : null;

            if (!string.IsNullOrEmpty(texName))
                key = texName;
            else if (!string.IsNullOrEmpty(goName))
                key = goName;
            else
                key = $"KSP_Btn_{index}";

            string tooltipName = null;
            if (btn != null)
            {
                var ttText = btn.GetComponent<KSP.UI.TooltipTypes.TooltipController_Text>();
                if (ttText != null && !string.IsNullOrEmpty(ttText.textString))
                {
                    tooltipName = ttText.textString;
                }
                else
                {
                    var ttTitle = btn.GetComponent<KSP.UI.TooltipTypes.TooltipController_TitleAndText>();
                    if (ttTitle != null)
                    {
                        if (!string.IsNullOrEmpty(ttTitle.titleString))
                            tooltipName = ttTitle.titleString;
                        else if (!string.IsNullOrEmpty(ttTitle.textString))
                            tooltipName = ttTitle.textString;
                    }
                }
            }

            if (!string.IsNullOrEmpty(tooltipName))
                defaultName = tooltipName.Trim();
            else if (!string.IsNullOrEmpty(texName))
                defaultName = texName;
            else if (!string.IsNullOrEmpty(goName))
                defaultName = goName;
            else
                defaultName = $"Button {index + 1}";
        }

        private void BuildButtonsFromKsp(RectTransform parent, List<KSP.UI.Screens.ApplicationLauncherButton> buttons, float s)
        {
            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;
            int orient = ThemeManager.Instance.DockOrientation;

            var primaryBtns = new List<KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>>();
            var hiddenBtns = new List<KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>>();

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;
                GetButtonIdentity(btn, i, out string key, out string defName);
                var rule = ThemeManager.Instance.GetOrCreateDockRule(key, defName);
                if (rule.IsVisible)
                {
                    primaryBtns.Add(new KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>(btn, rule));
                }
                else
                {
                    hiddenBtns.Add(new KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>(btn, rule));
                }
            }

            bool showDrawer = ThemeManager.Instance.DockShowHiddenDrawer && hiddenBtns.Count > 0;

            if (orient == 0)
            {
                // === 构型 0: 纵向双列 ===
                float drawerH = 18f * s;
                int pRows = Mathf.Max(0, Mathf.CeilToInt(primaryBtns.Count / 2f));
                float pHeight = pRows > 0 ? (pRows * (btnH + spacing)) : 0f;
                float totalContentHeight = pHeight + 4f * s;
                if (showDrawer)
                {
                    totalContentHeight += drawerH + spacing;
                    if (_drawerExpanded)
                    {
                        int hRows = Mathf.CeilToInt(hiddenBtns.Count / 2f);
                        totalContentHeight += hRows * (btnH + spacing);
                    }
                }
                if (totalContentHeight < 36f * s) totalContentHeight = 36f * s;

                parent.sizeDelta = new Vector2(parent.sizeDelta.x, totalContentHeight);
                float preferredH = Mathf.Clamp(totalContentHeight + 36f * s, 140f * s, 420f * s);
                _currentPanelHeight = preferredH;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(88f * s, _currentPanelHeight);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(panelSize.x, 2f * s);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - 1f * s);
                    }
                    if (_collapseBtn != null)
                    {
                        float headerH = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(panelSize.x - 8f * s, headerH);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - (headerH * 0.5f + 4f * s));
                    }
                }

                float startX = -(btnW * 0.5f + spacing * 0.5f);
                Vector2 anchor = new Vector2(0.5f, 1f);

                for (int i = 0; i < primaryBtns.Count; i++)
                {
                    var kvp = primaryBtns[i];
                    var kspBtn = kvp.Key;
                    var rule = kvp.Value;

                    int col = i % 2;
                    int row = i / 2;
                    float x = col == 0 ? startX : -startX;
                    float y = -(row * (btnH + spacing) + btnH * 0.5f + 2f * s);

                    RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerY = -(pRows * (btnH + spacing) + drawerH * 0.5f + 2f * s);
                    string drawerText = _drawerExpanded ? "▲ 收起" : $"▼ 更多 ({hiddenBtns.Count})";
                    CreateVerticalDrawerButton(parent, 0f, drawerY, 78f * s, drawerH, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartY = -(pRows * (btnH + spacing) + drawerH + spacing + 2f * s);
                        for (int j = 0; j < hiddenBtns.Count; j++)
                        {
                            var kvp = hiddenBtns[j];
                            var kspBtn = kvp.Key;
                            var rule = kvp.Value;

                            int col = j % 2;
                            int row = j / 2;
                            float x = col == 0 ? startX : -startX;
                            float y = hiddenStartY - (row * (btnH + spacing) + btnH * 0.5f);

                            RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
            else if (orient == 1)
            {
                // === 构型 1: 横向双行 ===
                float drawerW = 24f * s;
                int pCols = Mathf.Max(0, Mathf.CeilToInt(primaryBtns.Count / 2f));
                float pWidth = pCols > 0 ? (pCols * (btnW + spacing)) : 0f;
                float totalContentWidth = pWidth + 4f * s;
                if (showDrawer)
                {
                    totalContentWidth += drawerW + spacing;
                    if (_drawerExpanded)
                    {
                        int hCols = Mathf.CeilToInt(hiddenBtns.Count / 2f);
                        totalContentWidth += hCols * (btnW + spacing);
                    }
                }
                if (totalContentWidth < 36f * s) totalContentWidth = 36f * s;

                parent.sizeDelta = new Vector2(totalContentWidth, parent.sizeDelta.y);
                float preferredW = Mathf.Clamp(totalContentWidth + 36f * s, 160f * s, 680f * s);
                _currentPanelWidth = preferredW;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 88f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_collapseBtn != null)
                    {
                        float btnCollapseW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnCollapseW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnCollapseW * 0.5f + 4f * s), 0f);
                    }
                }

                float startY = (btnH * 0.5f + spacing * 0.5f);
                Vector2 anchor = new Vector2(0f, 0.5f);

                for (int i = 0; i < primaryBtns.Count; i++)
                {
                    var kvp = primaryBtns[i];
                    var kspBtn = kvp.Key;
                    var rule = kvp.Value;

                    int row = i % 2;
                    int col = i / 2;
                    float y = (row == 0) ? startY : -startY;
                    float x = col * (btnW + spacing) + btnW * 0.5f + 2f * s;

                    RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerX = (pCols * (btnW + spacing)) + drawerW * 0.5f + 2f * s;
                    string drawerText = _drawerExpanded ? "◀" : $"▶\n{hiddenBtns.Count}";
                    CreateHorizontalDrawerButton(parent, drawerX, 0f, drawerW, 76f * s, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartX = (pCols * (btnW + spacing)) + drawerW + spacing + 2f * s;
                        for (int j = 0; j < hiddenBtns.Count; j++)
                        {
                            var kvp = hiddenBtns[j];
                            var kspBtn = kvp.Key;
                            var rule = kvp.Value;

                            int row = j % 2;
                            int col = j / 2;
                            float y = (row == 0) ? startY : -startY;
                            float x = hiddenStartX + col * (btnW + spacing) + btnW * 0.5f;

                            RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
            else // orient == 2
            {
                // === 构型 2: 横向单行 ===
                float drawerW = 24f * s;
                int pCols = primaryBtns.Count;
                float pWidth = pCols * (btnW + spacing);
                float totalContentWidth = pWidth + 4f * s;
                if (showDrawer)
                {
                    totalContentWidth += drawerW + spacing;
                    if (_drawerExpanded)
                    {
                        int hCols = hiddenBtns.Count;
                        totalContentWidth += hCols * (btnW + spacing);
                    }
                }
                if (totalContentWidth < 36f * s) totalContentWidth = 36f * s;

                parent.sizeDelta = new Vector2(totalContentWidth, parent.sizeDelta.y);
                float preferredW = Mathf.Clamp(totalContentWidth + 36f * s, 140f * s, 760f * s);
                _currentPanelWidth = preferredW;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 48f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_collapseBtn != null)
                    {
                        float btnCollapseW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnCollapseW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnCollapseW * 0.5f + 4f * s), 0f);
                    }
                }

                Vector2 anchor = new Vector2(0f, 0.5f);

                for (int i = 0; i < primaryBtns.Count; i++)
                {
                    var kvp = primaryBtns[i];
                    var kspBtn = kvp.Key;
                    var rule = kvp.Value;

                    int col = i;
                    float y = 0f;
                    float x = col * (btnW + spacing) + btnW * 0.5f + 2f * s;

                    RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerX = (pCols * (btnW + spacing)) + drawerW * 0.5f + 2f * s;
                    string drawerText = _drawerExpanded ? "◀" : $"▶{hiddenBtns.Count}";
                    CreateHorizontalDrawerButton(parent, drawerX, 0f, drawerW, 36f * s, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartX = (pCols * (btnW + spacing)) + drawerW + spacing + 2f * s;
                        for (int j = 0; j < hiddenBtns.Count; j++)
                        {
                            var kvp = hiddenBtns[j];
                            var kspBtn = kvp.Key;
                            var rule = kvp.Value;

                            int col = j;
                            float y = 0f;
                            float x = hiddenStartX + col * (btnW + spacing) + btnW * 0.5f;

                            RenderKspButton(parent, kspBtn, rule, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
        }

        private void RenderKspButton(RectTransform parent, KSP.UI.Screens.ApplicationLauncherButton kspBtn, DockButtonRule rule,
            float x, float y, float w, float h, Vector2 anchor, float s)
        {
            Texture iconTex = (kspBtn.sprite != null) ? kspBtn.sprite.texture : null;
            bool active = (kspBtn.toggleButton != null && kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
            string displayLabel = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;

            CreateButtonItem(parent, x, y, w, h, anchor, displayLabel, iconTex, null,
                onLeftClick: (pe) => TriggerKspButtonClick(kspBtn, pe, false),
                onRightClick: (pe) => TriggerKspButtonClick(kspBtn, pe, true),
                onHoverEnter: (pe) => TriggerKspButtonHover(kspBtn, pe, true),
                onHoverExit: (pe) => TriggerKspButtonHover(kspBtn, pe, false),
                s: s,
                initialActive: active,
                hasExplicitCustomLabel: !string.IsNullOrEmpty(rule.CustomLabel),
                kspBtnRef: kspBtn);
        }

        private static void TriggerKspButtonClick(KSP.UI.Screens.ApplicationLauncherButton kspBtn, PointerEventData pe, bool isRightClick = false)
        {
            if (kspBtn == null) return;
            try
            {
                if (kspBtn.toggleButton != null)
                {
                    kspBtn.toggleButton.Interactable = true;
                }

                if (pe == null && EventSystem.current != null)
                {
                    pe = new PointerEventData(EventSystem.current)
                    {
                        button = isRightClick ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left
                    };
                }

                bool handled = false;
                if (kspBtn.toggleButton != null && pe != null)
                {
                    try
                    {
                        ((IPointerClickHandler)kspBtn.toggleButton).OnPointerClick(pe);
                        handled = true;
                    }
                    catch { }
                }

                if (!isRightClick)
                {
                    if (!handled)
                    {
                        if (kspBtn.toggleButton != null)
                        {
                            if (kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True)
                                kspBtn.SetFalse(true);
                            else
                                kspBtn.SetTrue(true);
                        }
                        kspBtn.onLeftClick?.Invoke();
                        if (kspBtn.toggleButton != null)
                        {
                            kspBtn.onLeftClickBtn?.Invoke(kspBtn.toggleButton);
                        }
                    }
                }
                else
                {
                    if (!handled)
                    {
                        kspBtn.onRightClick?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MFP] Error triggering KSP toolbar button: {ex.Message}");
            }
        }

        private static void TriggerKspButtonHover(KSP.UI.Screens.ApplicationLauncherButton kspBtn, PointerEventData pe, bool isEnter)
        {
            if (kspBtn == null) return;
            try
            {
                if (isEnter)
                {
                    if (pe != null && kspBtn.toggleButton != null)
                    {
                        try { ((IPointerEnterHandler)kspBtn.toggleButton).OnPointerEnter(pe); } catch { }
                    }
                    kspBtn.onHover?.Invoke();
                }
                else
                {
                    if (pe != null && kspBtn.toggleButton != null)
                    {
                        try { ((IPointerExitHandler)kspBtn.toggleButton).OnPointerExit(pe); } catch { }
                    }
                    kspBtn.onHoverOut?.Invoke();
                }
            }
            catch { }
        }
#endif

        private void BuildMockButtons(RectTransform parent, float s)
        {
            string[] mockNames = { "RES", "COMM", "TIME", "MJ", "FAR", "RA", "ALARM", "MFP" };
            ThemeConfig mockTheme = WidgetStyleManager.Instance.CurrentTheme;
            Color[] mockColors =
            {
                mockTheme.AccentSecondary, mockTheme.AccentPrimary, mockTheme.WarningColor, mockTheme.AccentMagenta,
                mockTheme.AccentSecondary, WidgetStyleManager.Lighten(mockTheme.AccentSecondary, 0.25f),
                mockTheme.DangerColor, mockTheme.AccentPrimary
            };

            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;
            int orient = ThemeManager.Instance.DockOrientation;

            var primaryItems = new List<int>();
            var hiddenItems = new List<int>();

            for (int i = 0; i < mockNames.Length; i++)
            {
                var rule = ThemeManager.Instance.GetOrCreateDockRule("MOCK_" + mockNames[i], mockNames[i]);
                if (rule.IsVisible)
                    primaryItems.Add(i);
                else
                    hiddenItems.Add(i);
            }

            bool showDrawer = ThemeManager.Instance.DockShowHiddenDrawer && hiddenItems.Count > 0;

            if (orient == 0)
            {
                // === 构型 0: 纵向双列 ===
                float drawerH = 18f * s;
                int pRows = Mathf.Max(0, Mathf.CeilToInt(primaryItems.Count / 2f));
                float pHeight = pRows > 0 ? (pRows * (btnH + spacing)) : 0f;
                float totalContentHeight = pHeight + 4f * s;
                if (showDrawer)
                {
                    totalContentHeight += drawerH + spacing;
                    if (_drawerExpanded)
                    {
                        int hRows = Mathf.CeilToInt(hiddenItems.Count / 2f);
                        totalContentHeight += hRows * (btnH + spacing);
                    }
                }
                if (totalContentHeight < 36f * s) totalContentHeight = 36f * s;

                parent.sizeDelta = new Vector2(parent.sizeDelta.x, totalContentHeight);
                float preferredH = Mathf.Clamp(totalContentHeight + 36f * s, 140f * s, 420f * s);
                _currentPanelHeight = preferredH;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(88f * s, _currentPanelHeight);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(panelSize.x, 2f * s);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - 1f * s);
                    }
                    if (_collapseBtn != null)
                    {
                        float headerH = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(panelSize.x - 8f * s, headerH);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(0f, panelSize.y * 0.5f - (headerH * 0.5f + 4f * s));
                    }
                }

                float startX = -(btnW * 0.5f + spacing * 0.5f);
                Vector2 anchor = new Vector2(0.5f, 1f);

                for (int i = 0; i < primaryItems.Count; i++)
                {
                    int idx = primaryItems[i];
                    int col = i % 2;
                    int row = i / 2;
                    float x = col == 0 ? startX : -startX;
                    float y = -(row * (btnH + spacing) + btnH * 0.5f + 2f * s);

                    RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerY = -(pRows * (btnH + spacing) + drawerH * 0.5f + 2f * s);
                    string drawerText = _drawerExpanded ? "▲ 收起" : $"▼ 更多 ({hiddenItems.Count})";
                    CreateVerticalDrawerButton(parent, 0f, drawerY, 78f * s, drawerH, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartY = -(pRows * (btnH + spacing) + drawerH + spacing + 2f * s);
                        for (int j = 0; j < hiddenItems.Count; j++)
                        {
                            int idx = hiddenItems[j];
                            int col = j % 2;
                            int row = j / 2;
                            float x = col == 0 ? startX : -startX;
                            float y = hiddenStartY - (row * (btnH + spacing) + btnH * 0.5f);

                            RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
            else if (orient == 1)
            {
                // === 构型 1: 横向双行 ===
                float drawerW = 24f * s;
                int pCols = Mathf.Max(0, Mathf.CeilToInt(primaryItems.Count / 2f));
                float pWidth = pCols > 0 ? (pCols * (btnW + spacing)) : 0f;
                float totalContentWidth = pWidth + 4f * s;
                if (showDrawer)
                {
                    totalContentWidth += drawerW + spacing;
                    if (_drawerExpanded)
                    {
                        int hCols = Mathf.CeilToInt(hiddenItems.Count / 2f);
                        totalContentWidth += hCols * (btnW + spacing);
                    }
                }
                if (totalContentWidth < 36f * s) totalContentWidth = 36f * s;

                parent.sizeDelta = new Vector2(totalContentWidth, parent.sizeDelta.y);
                float preferredW = Mathf.Clamp(totalContentWidth + 36f * s, 160f * s, 680f * s);
                _currentPanelWidth = preferredW;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 88f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_collapseBtn != null)
                    {
                        float btnCollapseW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnCollapseW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnCollapseW * 0.5f + 4f * s), 0f);
                    }
                }

                float startY = (btnH * 0.5f + spacing * 0.5f);
                Vector2 anchor = new Vector2(0f, 0.5f);

                for (int i = 0; i < primaryItems.Count; i++)
                {
                    int idx = primaryItems[i];
                    int row = i % 2;
                    int col = i / 2;
                    float y = (row == 0) ? startY : -startY;
                    float x = col * (btnW + spacing) + btnW * 0.5f + 2f * s;

                    RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerX = (pCols * (btnW + spacing)) + drawerW * 0.5f + 2f * s;
                    string drawerText = _drawerExpanded ? "◀" : $"▶\n{hiddenItems.Count}";
                    CreateHorizontalDrawerButton(parent, drawerX, 0f, drawerW, 76f * s, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartX = (pCols * (btnW + spacing)) + drawerW + spacing + 2f * s;
                        for (int j = 0; j < hiddenItems.Count; j++)
                        {
                            int idx = hiddenItems[j];
                            int row = j % 2;
                            int col = j / 2;
                            float y = (row == 0) ? startY : -startY;
                            float x = hiddenStartX + col * (btnW + spacing) + btnW * 0.5f;

                            RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
            else // orient == 2
            {
                // === 构型 2: 横向单行 ===
                float drawerW = 24f * s;
                int pCols = primaryItems.Count;
                float pWidth = pCols * (btnW + spacing);
                float totalContentWidth = pWidth + 4f * s;
                if (showDrawer)
                {
                    totalContentWidth += drawerW + spacing;
                    if (_drawerExpanded)
                    {
                        int hCols = hiddenItems.Count;
                        totalContentWidth += hCols * (btnW + spacing);
                    }
                }
                if (totalContentWidth < 36f * s) totalContentWidth = 36f * s;

                parent.sizeDelta = new Vector2(totalContentWidth, parent.sizeDelta.y);
                float preferredW = Mathf.Clamp(totalContentWidth + 36f * s, 140f * s, 760f * s);
                _currentPanelWidth = preferredW;

                if (!_isCollapsed)
                {
                    Vector2 panelSize = new Vector2(_currentPanelWidth, 48f * s);
                    RectTransform.sizeDelta = panelSize;
                    if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                    if (_topStripe != null)
                    {
                        _topStripe.rectTransform.sizeDelta = new Vector2(2f * s, panelSize.y);
                        _topStripe.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 1f * s, 0f);
                    }
                    if (_collapseBtn != null)
                    {
                        float btnCollapseW = 22f * s;
                        _collapseBtn.image.rectTransform.sizeDelta = new Vector2(btnCollapseW, panelSize.y - 8f * s);
                        _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + (btnCollapseW * 0.5f + 4f * s), 0f);
                    }
                }

                Vector2 anchor = new Vector2(0f, 0.5f);

                for (int i = 0; i < primaryItems.Count; i++)
                {
                    int idx = primaryItems[i];
                    int col = i;
                    float y = 0f;
                    float x = col * (btnW + spacing) + btnW * 0.5f + 2f * s;

                    RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                }

                if (showDrawer)
                {
                    float drawerX = (pCols * (btnW + spacing)) + drawerW * 0.5f + 2f * s;
                    string drawerText = _drawerExpanded ? "◀" : $"▶{hiddenItems.Count}";
                    CreateHorizontalDrawerButton(parent, drawerX, 0f, drawerW, 36f * s, drawerText, s);

                    if (_drawerExpanded)
                    {
                        float hiddenStartX = (pCols * (btnW + spacing)) + drawerW + spacing + 2f * s;
                        for (int j = 0; j < hiddenItems.Count; j++)
                        {
                            int idx = hiddenItems[j];
                            int col = j;
                            float y = 0f;
                            float x = hiddenStartX + col * (btnW + spacing) + btnW * 0.5f;

                            RenderMockButton(parent, idx, mockNames, mockColors, x, y, btnW, btnH, anchor, s);
                        }
                    }
                }
            }
        }

        private void RenderMockButton(RectTransform parent, int idx, string[] mockNames, Color[] mockColors,
            float x, float y, float w, float h, Vector2 anchor, float s)
        {
            var rule = ThemeManager.Instance.GetOrCreateDockRule("MOCK_" + mockNames[idx], mockNames[idx]);
            string label = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;
            Color ledCol = mockColors[idx % mockColors.Length];
            bool active = (idx % 3 == 0);

            CreateButtonItem(parent, x, y, w, h, anchor, label, null, ledCol, null, null, null, null, s, active, !string.IsNullOrEmpty(rule.CustomLabel));
        }

        private void CreateVerticalDrawerButton(RectTransform parent, float x, float y, float w, float h, string text, float s)
        {
            GameObject drawerBtnObj = UIFactory.CreatePanel(parent, "DrawerToggleBtn",
                new Vector2(w, h), new Vector2(x, y),
                WidgetStyleManager.Surface(SurfaceStyleRole.Tile),
                _currentTheme.FrameBorderColor, 1f * s);
            RectTransform drawerRt = drawerBtnObj.GetComponent<RectTransform>();
            drawerRt.anchorMin = new Vector2(0.5f, 1f);
            drawerRt.anchorMax = new Vector2(0.5f, 1f);
            drawerRt.pivot = new Vector2(0.5f, 0.5f);
            drawerRt.anchoredPosition = new Vector2(x, y);

            Button drawerBtn = drawerBtnObj.AddComponent<Button>();
            drawerBtn.transition = Selectable.Transition.ColorTint;
            drawerBtn.targetGraphic = drawerBtnObj.GetComponent<Image>();

            Text drawerLbl = UIFactory.CreateText(drawerBtnObj.transform, "Label", text,
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));
            drawerLbl.raycastTarget = false;
            drawerLbl.rectTransform.anchoredPosition = Vector2.zero;
            drawerLbl.rectTransform.sizeDelta = new Vector2(w - 2f * s, h);

            drawerBtn.onClick.AddListener(() =>
            {
                _drawerExpanded = !_drawerExpanded;
                PopulateToolbarButtons(parent, s);
            });
        }

        private void CreateHorizontalDrawerButton(RectTransform parent, float x, float y, float w, float h, string text, float s)
        {
            GameObject drawerBtnObj = UIFactory.CreatePanel(parent, "DrawerToggleBtn",
                new Vector2(w, h), new Vector2(x, y),
                WidgetStyleManager.Surface(SurfaceStyleRole.Tile),
                _currentTheme.FrameBorderColor, 1f * s);
            RectTransform drawerRt = drawerBtnObj.GetComponent<RectTransform>();
            drawerRt.anchorMin = new Vector2(0f, 0.5f);
            drawerRt.anchorMax = new Vector2(0f, 0.5f);
            drawerRt.pivot = new Vector2(0.5f, 0.5f);
            drawerRt.anchoredPosition = new Vector2(x, y);

            Button drawerBtn = drawerBtnObj.AddComponent<Button>();
            drawerBtn.transition = Selectable.Transition.ColorTint;
            drawerBtn.targetGraphic = drawerBtnObj.GetComponent<Image>();

            Text drawerLbl = UIFactory.CreateText(drawerBtnObj.transform, "Label", text,
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));
            drawerLbl.raycastTarget = false;
            drawerLbl.rectTransform.anchoredPosition = Vector2.zero;
            drawerLbl.rectTransform.sizeDelta = new Vector2(w - 2f * s, h - 2f * s);

            drawerBtn.onClick.AddListener(() =>
            {
                _drawerExpanded = !_drawerExpanded;
                PopulateToolbarButtons(parent, s);
            });
        }

        private void CreateButtonItem(RectTransform parent, float x, float y, float w, float h,
            Vector2 anchor, string label, Texture iconTex, Color? accent,
            Action<PointerEventData> onLeftClick, Action<PointerEventData> onRightClick,
            Action<PointerEventData> onHoverEnter, Action<PointerEventData> onHoverExit,
            float s, bool initialActive = false, bool hasExplicitCustomLabel = false
#if KSP_RUNTIME
            , KSP.UI.Screens.ApplicationLauncherButton kspBtnRef = null
#endif
        )
        {
            Color tileBg = WidgetStyleManager.Surface(SurfaceStyleRole.Tile);
            Color borderCol = _currentTheme.FrameBorderColor;
            Color ledCol = accent ?? (_currentTheme.AccentPrimary);

            GameObject itemObj = UIFactory.CreatePanel(parent, $"Item_{label}", new Vector2(w, h), new Vector2(x, y), tileBg, borderCol, 1f * s);
            RectTransform itemRt = itemObj.GetComponent<RectTransform>();
            itemRt.anchorMin = anchor;
            itemRt.anchorMax = anchor;
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.anchoredPosition = new Vector2(x, y);

            Image bg = itemObj.GetComponent<Image>();
            bg.raycastTarget = true;

            // 1. 图标渲染层 (RawImage) - 完美保留所有模组原始 Icon 质感
            RawImage rawImg = null;
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            iconObj.transform.SetParent(itemObj.transform, false);
            rawImg = iconObj.GetComponent<RawImage>();
            rawImg.color = WidgetStyleManager.NeutralOpaque; // 直通原始图标贴图，不做任何着色
            rawImg.raycastTarget = false; // 绝不阻拦点击穿透至 itemObj
            RectTransform irt = rawImg.rectTransform;
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);

            // 2. 文本标签
            Text lbl = UIFactory.CreateText(itemObj.transform, "Label", label, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            lbl.raycastTarget = false; // 绝不阻拦点击穿透至 itemObj
            RectTransform lblRt = lbl.GetComponent<RectTransform>();

            if (iconTex != null)
            {
                rawImg.texture = iconTex;
                rawImg.gameObject.SetActive(true);

                if (hasExplicitCustomLabel && !string.IsNullOrEmpty(label))
                {
                    // 复合模式：上方显示 20x20 图标，下方显示自定义缩写/别名 (MFD 软键质感)
                    irt.sizeDelta = new Vector2(20f * s, 20f * s);
                    irt.anchoredPosition = new Vector2(0f, 4f * s);

                    lbl.text = label;
                    lbl.fontSize = Mathf.RoundToInt(8f * s);
                    lblRt.sizeDelta = new Vector2(w - 2f * s, 10f * s);
                    lblRt.anchoredPosition = new Vector2(0f, -11f * s);
                    lbl.gameObject.SetActive(true);
                }
                else
                {
                    // 纯图标模式：居中 28x28 图标
                    irt.sizeDelta = new Vector2(w - 8f * s, h - 8f * s);
                    irt.anchoredPosition = Vector2.zero;
                    lbl.gameObject.SetActive(false);
                }
            }
            else
            {
                // 无贴图模式：居中文本
                rawImg.gameObject.SetActive(false);
                lbl.text = label;
                lbl.fontSize = Mathf.RoundToInt(9.5f * s);
                lblRt.sizeDelta = new Vector2(w - 4f * s, h - 4f * s);
                lblRt.anchoredPosition = Vector2.zero;
                lbl.gameObject.SetActive(true);
            }

            // 3. 顶部激活状态微光 LED (3.5x3.5px)
            GameObject ledObj = UIFactory.CreatePanel(itemObj.transform, "ActiveLed", new Vector2(3.5f * s, 3.5f * s),
                new Vector2(-w * 0.5f + 3.5f * s, h * 0.5f - 3.5f * s), initialActive ? ledCol : WidgetStyleManager.Surface(SurfaceStyleRole.LedOff));
            Image ledImg = ledObj.GetComponent<Image>();
            ledImg.raycastTarget = false;

            // 4. 标准 Button 与事件转发代理通道驱动
            Button itemBtn = itemObj.AddComponent<Button>();
            itemBtn.transition = Selectable.Transition.ColorTint;
            itemBtn.targetGraphic = bg;

            ModernToolbarButtonProxy proxy = itemObj.AddComponent<ModernToolbarButtonProxy>();
            proxy.OnLeftClick = onLeftClick;
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
#if KSP_RUNTIME
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

#if KSP_RUNTIME
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
                    Color ledOn = _currentTheme.AccentPrimary;
                    Color ledOff = WidgetStyleManager.Surface(SurfaceStyleRole.LedOff);

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

        private string GetTemplateChannel(string key, string fallback)
        {
            if (string.IsNullOrEmpty(Config?.CustomTemplate)) return fallback;
            string[] pairs = Config.CustomTemplate.Split(';');
            foreach (string pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            return fallback;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;

            ApplyCard(_panelBg, _panelOutline, CardStyleRole.Normal, theme);

            if (_topStripe != null) _topStripe.color = (Color)theme.AccentPrimary;
            if (_collapseBtn != null)
            {
                int orient = ThemeManager.Instance.DockOrientation;
                string dockLabel = (orient == 0) ? GetTemplateChannel("DOCK_LABEL", "« DOCK") : "«";
                string collIcon = GetTemplateChannel("COLLAPSE_ICON", "»");
                if (_collapseBtnText != null)
                {
                    _collapseBtnText.text = _isCollapsed ? collIcon : dockLabel;
                }
                ApplyButton(_collapseBtn, _collapseBtn.GetComponent<Image>(), _collapseBtnText, ButtonVisualRole.Normal, false, theme);
            }
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_collapseBtn != null) _collapseBtn.onClick.RemoveAllListeners();
            if (ThemeManager.Instance.ToolbarStyleMode != 2)
            {
                StockToolbarHook.HideStockToolbar(false);
            }
            base.OnDestroy();
        }
    }
}
