using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 飞行状态下轻量级直接拖拽处理器：允许玩家在不进入 Alt+N 编辑模式的情况下，直接按住快捷坞标题/把手在屏幕上自由拖拽
    /// 并在松开时自动持久化坐标至 WidgetConfig 与 ThemeManager
    /// </summary>
    public class FavoritePanelInFlightDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FavoriteToolbarWidget Owner;
        private Canvas _rootCanvas;
        private Vector2 _lastPointerPos;

        public void Initialize(FavoriteToolbarWidget owner, Canvas canvas)
        {
            Owner = owner;
            _rootCanvas = canvas;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (WidgetDragHandler.IsEditModeActive) return; // 编辑模式交由全功能 WidgetDragHandler 接管
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Owner.RectTransform.parent as RectTransform,
                eventData.position,
                _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _rootCanvas.worldCamera : null,
                out _lastPointerPos);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (WidgetDragHandler.IsEditModeActive || Owner == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Owner.RectTransform.parent as RectTransform,
                eventData.position,
                _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _rootCanvas.worldCamera : null,
                out Vector2 currentPointerPos))
            {
                Vector2 delta = currentPointerPos - _lastPointerPos;
                _lastPointerPos = currentPointerPos;
                Owner.RectTransform.anchoredPosition += delta;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (WidgetDragHandler.IsEditModeActive || Owner == null) return;
            Vector2 finalPos = Owner.RectTransform.anchoredPosition;
            if (Owner.Config != null)
            {
                Owner.Config.PositionX = finalPos.x;
                Owner.Config.PositionY = finalPos.y;
                WidgetLayoutManager.Instance?.SaveLayout();
            }
            ThemeManager.Instance.DockFavoritePosX = finalPos.x;
            ThemeManager.Instance.DockFavoritePosY = finalPos.y;
            ThemeManager.Instance.SaveSettings();
        }
    }

    /// <summary>
    /// 常用 MOD 独立快捷面板 / 快速启动坞 (Favorite Mods Quick Dock - core.dock_favorites)
    /// 
    /// 专用于从 20+ MOD 工具栏中将核心常用模组 (如 MechJeb, KER, Trajectories, DPAI, Alarm) 独立置顶：
    /// 1. 独立浮动 / 停靠面板：可在屏幕任意位置放置 (默认屏幕下边缘 / 姿态球上方)，不与主工具栏抢占视口；
    /// 2. 100% 原始高清贴图与事件穿透：左键开关、右键菜单、悬浮提示完全兼容原版与第三方插件；
    /// 3. 多构型自适应：横向单行 (极简快速启动条)、纵向单列、横向双行自由切换；
    /// 4. 实时双向同步：状态 LED 微光灯、按钮激活状态、贴图热更新与主工具栏保持毫秒级一致；
    /// 5. 0 颜色字面量与 100% 纯 C# 解耦架构，符合 MFP-SPEC-001..007 全量航电标准。
    /// </summary>
    [FlightWidget("dock_favorites", "toolbar_favorites", "favorite_dock", "quick_dock", Category = WidgetCategory.Controls, DisplayName = "常用 MOD 独立快捷坞", Description = "将最常用 Mod (如 MechJeb, KER, Trajectories) 图标独立置顶的极简流线型快捷航电坞。", DefaultWidgetId = "core.dock_favorites", DefaultX = 0f, DefaultY = -260f, IsSingleton = true, ExactIds = new[] { "core.dock_favorites", "toolbar.favorites" })]
    public class FavoriteToolbarWidget : BaseFlightWidget
    {
        public static FavoriteToolbarWidget Instance { get; private set; }

        public override Vector2 BaseSize => new Vector2(220f, 46f);
        protected override bool AutoCreateCardFrame => false;
        public override bool IsInteractive => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.UltraLow;

        // 声明式微控件
        public TextWidget TitleWidget = TextWidget.Title(I18n.Tr("FAV_DOCK_TITLE", "★ 快速对接"));

        private Image _panelBg;
        private Outline _panelOutline;
        private Image _accentStripe;
        private GameObject _headerGrip;
        private Text _headerTitle;
        private Button _collapseBtn;
        private Text _collapseBtnText;
        private RectTransform _contentRt;
        private GameObject _emptyHintObj;

        private bool _isCollapsed = false;
        private ThemeConfig _currentTheme;
        private readonly CachedFloat _lastSyncTime = new CachedFloat(-1f);
        private readonly Cached<int> _cachedButtonCount = new Cached<int>(-1);

        private class FavItemView
        {
            public GameObject Root;
            public Image Background;
            public RawImage IconRaw;
            public Text LabelText;
            public Image ActiveLed;
            public ModernToolbarButtonProxy Proxy;
            public string Name;
            public bool IsActive;

#if KSP_RUNTIME
            public KSP.UI.Screens.ApplicationLauncherButton KspButton;
#endif
        }

        private readonly List<FavItemView> _itemViews = new List<FavItemView>();

        public void RefreshToolbarButtons()
        {
            if (_contentRt != null)
            {
                RebuildFavoritesLayout();
            }
        }

        public void ToggleVisibilityOrHighlight()
        {
            if (gameObject != null)
            {
                bool active = !gameObject.activeSelf;
                gameObject.SetActive(active);
            }
        }

        public void RebuildFavoritesLayout()
        {
            if (RectTransform == null) return;
            SetupStructure();
            PopulateButtons(_contentRt, CurrentDpiScale);
            AutoDetectInteractivityAndPruneRaycasts();
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Instance = this;
            theme = WidgetStyleManager.ResolveTheme(theme);
            _currentTheme = theme;
            float s = CurrentDpiScale;

            SetupStructure();
            PopulateButtons(_contentRt, s);

            // 标准化组件内部控件注册至管理器
            if (_panelBg != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "background", "快捷坞面板底衬", _panelBg.gameObject);
            }
            if (_collapseBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "collapse_btn", "折叠按键", _collapseBtn.gameObject, _collapseBtn, _collapseBtn.GetComponent<Image>(), null, _collapseBtnText, null, "«", ToggleCollapse, true));
            }
            if (_contentRt != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "favorites_content", "快捷图标容器", _contentRt.gameObject);
            }

            AutoDetectInteractivityAndPruneRaycasts();
        }

        private void SetupStructure()
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = _currentTheme ?? WidgetStyleManager.Instance.CurrentTheme;
            Color bgCol = theme.FrameBgColor;
            Color borderCol = theme.FrameBorderColor;
            Color primaryAccent = theme.AccentPrimary;
            int orient = ThemeManager.Instance.DockFavoriteOrientation; // 0 = 纵向单列, 1 = 横向单行, 2 = 横向双行

            Vector2 initialSize = GetEstimatedPanelSize(s, orient);
            RectTransform.sizeDelta = initialSize;

            if (_panelBg == null)
            {
                GameObject panel = UIFactory.CreatePanel(transform, "FavToolbarPanel", initialSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
                _panelBg = panel.GetComponent<Image>();
                _panelOutline = panel.GetComponent<Outline>();

                // 挂载飞行状态下的免编辑自由拖拽把手
                var dragHandler = _panelBg.gameObject.AddComponent<FavoritePanelInFlightDragHandler>();
                dragHandler.Initialize(this, RootCanvas);
            }
            else
            {
                _panelBg.rectTransform.sizeDelta = initialSize;
            }

            // 装饰性微光边线
            if (_accentStripe == null)
            {
                _accentStripe = UIFactory.CreatePanel(_panelBg.transform, "AccentStripe", Vector2.one, Vector2.zero, primaryAccent).GetComponent<Image>();
            }

            if (orient == 0)
            {
                // 纵向单列
                _accentStripe.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _accentStripe.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _accentStripe.rectTransform.sizeDelta = new Vector2(initialSize.x, 2f * s);
                _accentStripe.rectTransform.anchoredPosition = new Vector2(0f, initialSize.y * 0.5f - 1f * s);
            }
            else
            {
                // 横向单行或双行
                _accentStripe.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                _accentStripe.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _accentStripe.rectTransform.sizeDelta = new Vector2(2f * s, initialSize.y);
                _accentStripe.rectTransform.anchoredPosition = new Vector2(-initialSize.x * 0.5f + 1f * s, 0f);
            }

            // 标题与拖拽抓手条
            if (_headerGrip == null)
            {
                _headerGrip = CreateNode("HeaderGrip", _panelBg.transform);

                string titleText = I18n.Tr("FAV_DOCK_TITLE", "★ QUICK DOCK");
                _headerTitle = UIFactory.CreateText(_headerGrip.transform, "Title", titleText,
                    Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, primaryAccent);
                _headerTitle.raycastTarget = false;

                Button collBtn = UIFactory.CreateCockpitButton(_headerGrip.transform, "FavCollapseBtn",
                    _isCollapsed ? "»" : "«",
                    new Vector2(18f * s, 18f * s),
                    Vector2.zero,
                    WidgetStyleManager.Surface(SurfaceStyleRole.SlotActive), borderCol, primaryAccent,
                    () => ToggleCollapse());
                _collapseBtn = collBtn;
                _collapseBtnText = collBtn.GetComponentInChildren<Text>();
            }

            RectTransform headerRt = _headerGrip.GetComponent<RectTransform>();
            if (orient == 0)
            {
                float headerH = 20f * s;
                headerRt.anchorMin = new Vector2(0f, 1f);
                headerRt.anchorMax = new Vector2(1f, 1f);
                headerRt.pivot = new Vector2(0.5f, 1f);
                headerRt.sizeDelta = new Vector2(0f, headerH);
                headerRt.anchoredPosition = Vector2.zero;

                if (_headerTitle != null)
                {
                    _headerTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
                    _headerTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
                    _headerTitle.rectTransform.offsetMin = new Vector2(4f * s, 0f);
                    _headerTitle.rectTransform.offsetMax = new Vector2(-22f * s, 0f);
                    _headerTitle.text = _isCollapsed ? "★" : I18n.Tr("FAV_DOCK_TITLE", "★ QUICK DOCK");
                }
                if (_collapseBtn != null)
                {
                    _collapseBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.5f);
                    _collapseBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                    _collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-11f * s, 0f);
                    _collapseBtn.image.rectTransform.sizeDelta = new Vector2(18f * s, 16f * s);
                    if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "▼" : "▲";
                }
            }
            else
            {
                float headerW = 32f * s;
                headerRt.anchorMin = new Vector2(0f, 0f);
                headerRt.anchorMax = new Vector2(0f, 1f);
                headerRt.pivot = new Vector2(0f, 0.5f);
                headerRt.sizeDelta = new Vector2(headerW, 0f);
                headerRt.anchoredPosition = Vector2.zero;

                if (_headerTitle != null)
                {
                    _headerTitle.rectTransform.anchorMin = new Vector2(0f, 0.35f);
                    _headerTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
                    _headerTitle.rectTransform.offsetMin = Vector2.zero;
                    _headerTitle.rectTransform.offsetMax = Vector2.zero;
                    _headerTitle.text = "★";
                    _headerTitle.fontSize = Mathf.RoundToInt(11f * s);
                }
                if (_collapseBtn != null)
                {
                    _collapseBtn.image.rectTransform.anchorMin = new Vector2(0.5f, 0.18f);
                    _collapseBtn.image.rectTransform.anchorMax = new Vector2(0.5f, 0.18f);
                    _collapseBtn.image.rectTransform.anchoredPosition = Vector2.zero;
                    _collapseBtn.image.rectTransform.sizeDelta = new Vector2(22f * s, 14f * s);
                    if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "»" : "«";
                }
            }

            // 按钮内容容器
            if (_contentRt == null)
            {
                _contentRt = CreateContainer("FavContent", _panelBg.transform);
            }

            if (orient == 0)
            {
                float headerH = 22f * s;
                _contentRt.anchorMin = new Vector2(0f, 0f);
                _contentRt.anchorMax = new Vector2(1f, 1f);
                _contentRt.offsetMin = new Vector2(4f * s, 4f * s);
                _contentRt.offsetMax = new Vector2(-4f * s, -headerH);
            }
            else
            {
                float headerW = 34f * s;
                _contentRt.anchorMin = new Vector2(0f, 0f);
                _contentRt.anchorMax = new Vector2(1f, 1f);
                _contentRt.offsetMin = new Vector2(headerW, 4f * s);
                _contentRt.offsetMax = new Vector2(-4f * s, -4f * s);
            }
        }

        private Vector2 GetEstimatedPanelSize(float s, int orient)
        {
            if (_isCollapsed)
            {
                return new Vector2(36f * s, 36f * s);
            }

            int count = GetFavoriteButtonsCount();
            if (count == 0) count = 1; // 预留空提示宽度

            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;

            if (orient == 0)
            {
                // 纵向单列
                float h = 24f * s + count * (btnH + spacing) + 8f * s;
                return new Vector2(46f * s, Mathf.Clamp(h, 60f * s, 600f * s));
            }
            else if (orient == 2)
            {
                // 横向双行
                int cols = Mathf.Max(1, Mathf.CeilToInt(count / 2f));
                float w = 36f * s + cols * (btnW + spacing) + 8f * s;
                return new Vector2(Mathf.Clamp(w, 100f * s, 800f * s), 86f * s);
            }
            else
            {
                // 横向单行
                float w = 36f * s + count * (btnW + spacing) + 8f * s;
                return new Vector2(Mathf.Clamp(w, 90f * s, 800f * s), 46f * s);
            }
        }

        private int GetFavoriteButtonsCount()
        {
            int count = 0;
            var rules = ThemeManager.Instance.DockRules;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i].IsFavorite && rules[i].IsVisible) count++;
                }
            }
            return count;
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            float s = CurrentDpiScale;
            int orient = ThemeManager.Instance.DockFavoriteOrientation;

            if (_isCollapsed)
            {
                Vector2 pillSize = new Vector2(36f * s, 36f * s);
                RectTransform.sizeDelta = pillSize;
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = pillSize;
                if (_accentStripe != null) _accentStripe.gameObject.SetActive(false);
                if (_contentRt != null) _contentRt.gameObject.SetActive(false);
                if (_collapseBtn != null)
                {
                    _collapseBtn.image.rectTransform.sizeDelta = new Vector2(30f * s, 30f * s);
                    _collapseBtn.image.rectTransform.anchoredPosition = Vector2.zero;
                    if (_collapseBtnText != null) _collapseBtnText.text = "»";
                }
                if (_headerTitle != null) _headerTitle.gameObject.SetActive(false);
            }
            else
            {
                Vector2 panelSize = GetEstimatedPanelSize(s, orient);
                RectTransform.sizeDelta = panelSize;
                if (_panelBg != null) _panelBg.rectTransform.sizeDelta = panelSize;
                if (_accentStripe != null) _accentStripe.gameObject.SetActive(true);
                if (_contentRt != null) _contentRt.gameObject.SetActive(true);
                if (_headerTitle != null) _headerTitle.gameObject.SetActive(true);
                SetupStructure();
                PopulateButtons(_contentRt, s);
            }
        }

        private void PopulateButtons(RectTransform contentParent, float s)
        {
            for (int i = 0; i < _itemViews.Count; i++)
            {
                if (_itemViews[i]?.Root != null)
                {
                    Destroy(_itemViews[i].Root);
                }
            }
            _itemViews.Clear();
#if KSP_RUNTIME
            DockAnchorTracker.UnregisterWidget(this);
#endif

            if (_emptyHintObj != null)
            {
                Destroy(_emptyHintObj);
                _emptyHintObj = null;
            }

            bool hasRealButtons = false;

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

                    _cachedButtonCount.Update(allButtons.Count);
                    if (allButtons.Count > 0)
                    {
                        hasRealButtons = true;
                        BuildButtonsFromKsp(contentParent, allButtons, s);
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("FavoriteToolbar_BuildKsp", $"Failed reading ApplicationLauncher: {ex.Message}");
            }
#endif

            if (!hasRealButtons)
            {
                BuildMockButtons(contentParent, s);
            }
        }

#if KSP_RUNTIME
        private void BuildButtonsFromKsp(RectTransform parent, List<KSP.UI.Screens.ApplicationLauncherButton> buttons, float s)
        {
            var favList = new List<KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>>();

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;
                ModernToolbarWidget.GetButtonIdentity(btn, i, out string key, out string defName);
                var rule = ThemeManager.Instance.GetOrCreateDockRule(key, defName);
                if (rule.IsFavorite && rule.IsVisible)
                {
                    favList.Add(new KeyValuePair<KSP.UI.Screens.ApplicationLauncherButton, DockButtonRule>(btn, rule));
                }
            }

            if (favList.Count == 0)
            {
                ShowEmptyHint(parent, s);
                return;
            }

            RenderButtonList(parent, s, favList.Count, (idx, x, y, w, h, anchor) =>
            {
                var kvp = favList[idx];
                RenderKspButton(parent, kvp.Key, kvp.Value, x, y, w, h, anchor, s);
            });
        }

        private void RenderKspButton(RectTransform parent, KSP.UI.Screens.ApplicationLauncherButton kspBtn, DockButtonRule rule,
            float x, float y, float w, float h, Vector2 anchor, float s)
        {
            Texture iconTex = (kspBtn.sprite != null) ? kspBtn.sprite.texture : null;
            bool active = (kspBtn.toggleButton != null && kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
            string displayLabel = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;

            CreateButtonItem(parent, x, y, w, h, anchor, displayLabel, iconTex, null,
                onLeftClick: (pe) => ModernToolbarWidget.TriggerKspButtonClick(kspBtn, pe, false),
                onRightClick: (pe) => ModernToolbarWidget.TriggerKspButtonClick(kspBtn, pe, true),
                onHoverEnter: (pe) => ModernToolbarWidget.TriggerKspButtonHover(kspBtn, pe, true),
                onHoverExit: (pe) => ModernToolbarWidget.TriggerKspButtonHover(kspBtn, pe, false),
                s: s,
                initialActive: active,
                hasExplicitCustomLabel: !string.IsNullOrEmpty(rule.CustomLabel),
                kspBtnRef: kspBtn);

#if KSP_RUNTIME
            if (kspBtn != null && _itemViews.Count > 0)
            {
                var latest = _itemViews[_itemViews.Count - 1];
                if (latest != null && latest.Root != null)
                {
                    DockAnchorTracker.RegisterButton(kspBtn, latest.Root.GetComponent<RectTransform>(), this, true);
                }
            }
#endif
        }
#endif

        private void BuildMockButtons(RectTransform parent, float s)
        {
            string[] mockNames = { "MJ", "KER", "TRAJ", "MFP", "DPAI", "ALARM" };
            ThemeConfig mockTheme = WidgetStyleManager.Instance.CurrentTheme;
            Color[] mockColors =
            {
                mockTheme.AccentPrimary, mockTheme.AccentSecondary, mockTheme.AccentMagenta,
                mockTheme.AccentPrimary, mockTheme.AccentSecondary, mockTheme.WarningColor
            };

            var favIndices = new List<int>();
            for (int i = 0; i < mockNames.Length; i++)
            {
                var rule = ThemeManager.Instance.GetOrCreateDockRule("MOCK_" + mockNames[i], mockNames[i]);
                // 默认将前 4 个常用组件设为 Favorite
                if (!rule.IsFavorite && i < 4)
                {
                    rule.IsFavorite = true;
                }
                if (rule.IsFavorite && rule.IsVisible)
                {
                    favIndices.Add(i);
                }
            }

            if (favIndices.Count == 0)
            {
                ShowEmptyHint(parent, s);
                return;
            }

            RenderButtonList(parent, s, favIndices.Count, (idx, x, y, w, h, anchor) =>
            {
                int mockIdx = favIndices[idx];
                var rule = ThemeManager.Instance.GetOrCreateDockRule("MOCK_" + mockNames[mockIdx], mockNames[mockIdx]);
                string label = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;
                Color ledCol = mockColors[mockIdx % mockColors.Length];
                bool active = (mockIdx == 0 || mockIdx == 3);

                CreateButtonItem(parent, x, y, w, h, anchor, label, null, ledCol, null, null, null, null, s, active, !string.IsNullOrEmpty(rule.CustomLabel));
            });
        }

        private void RenderButtonList(RectTransform parent, float s, int totalCount, Action<int, float, float, float, float, Vector2> renderAction)
        {
            float btnW = 36f * s;
            float btnH = 36f * s;
            float spacing = 4f * s;
            int orient = ThemeManager.Instance.DockFavoriteOrientation;

            // 重新适配实际包围盒尺寸
            Vector2 dynamicSize = GetEstimatedPanelSize(s, orient);
            RectTransform.sizeDelta = dynamicSize;
            if (_panelBg != null) _panelBg.rectTransform.sizeDelta = dynamicSize;

            if (orient == 0)
            {
                // 纵向单列
                Vector2 anchor = new Vector2(0.5f, 1f);
                for (int i = 0; i < totalCount; i++)
                {
                    float y = -(i * (btnH + spacing) + btnH * 0.5f + 2f * s);
                    renderAction(i, 0f, y, btnW, btnH, anchor);
                }
            }
            else if (orient == 2)
            {
                // 横向双行
                Vector2 anchor = new Vector2(0f, 0.5f);
                float startY = (btnH * 0.5f + spacing * 0.5f);
                for (int i = 0; i < totalCount; i++)
                {
                    int row = i % 2;
                    int col = i / 2;
                    float y = (row == 0) ? startY : -startY;
                    float x = col * (btnW + spacing) + btnW * 0.5f + 2f * s;
                    renderAction(i, x, y, btnW, btnH, anchor);
                }
            }
            else
            {
                // 横向单行 (默认)
                Vector2 anchor = new Vector2(0f, 0.5f);
                for (int i = 0; i < totalCount; i++)
                {
                    float x = i * (btnW + spacing) + btnW * 0.5f + 2f * s;
                    renderAction(i, x, 0f, btnW, btnH, anchor);
                }
            }
        }

        private void ShowEmptyHint(RectTransform parent, float s)
        {
            string hintText = I18n.Tr("FAV_DOCK_EMPTY_HINT", "★ 点击设置添加常用 MOD");
            _emptyHintObj = UIFactory.CreatePanel(parent, "EmptyHint", new Vector2(160f * s, 32f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Tile), _currentTheme.FrameBorderColor, 1f * s);
            Button btn = _emptyHintObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = _emptyHintObj.GetComponent<Image>();

            Text txt = UIFactory.CreateText(_emptyHintObj.transform, "Text", hintText,
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.Cardinal));
            txt.raycastTarget = false;
            txt.rectTransform.anchoredPosition = Vector2.zero;
            txt.rectTransform.sizeDelta = new Vector2(150f * s, 28f * s);

            btn.onClick.AddListener(() =>
            {
#if KSP_RUNTIME
                ModularFlightPanel.UI.SettingsGUI.Instance?.OpenToTab(3); // 打开主题与收纳坞设置页
#endif
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

            GameObject itemObj = UIFactory.CreatePanel(parent, $"FavItem_{label}", new Vector2(w, h), new Vector2(x, y), tileBg, borderCol, 1f * s);
            RectTransform itemRt = itemObj.GetComponent<RectTransform>();
            itemRt.anchorMin = anchor;
            itemRt.anchorMax = anchor;
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.anchoredPosition = new Vector2(x, y);

            Image bg = itemObj.GetComponent<Image>();
            bg.raycastTarget = true;

            // 1. 图标渲染 (RawImage) - 完美呈现原始贴图
            RawImage rawImg = CreateChild<RawImage>("Icon", itemObj.transform);
            rawImg.color = WidgetStyleManager.NeutralOpaque;
            rawImg.raycastTarget = false;
            RectTransform irt = rawImg.rectTransform;
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);

            // 2. 文本标签
            Text lbl = UIFactory.CreateText(itemObj.transform, "Label", label, Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            lbl.raycastTarget = false;
            RectTransform lblRt = lbl.GetComponent<RectTransform>();

            if (iconTex != null)
            {
                rawImg.texture = iconTex;
                rawImg.gameObject.SetActive(true);

                if (hasExplicitCustomLabel && !string.IsNullOrEmpty(label))
                {
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
                    irt.sizeDelta = new Vector2(w - 8f * s, h - 8f * s);
                    irt.anchoredPosition = Vector2.zero;
                    lbl.gameObject.SetActive(false);
                }
            }
            else
            {
                rawImg.gameObject.SetActive(false);
                lbl.text = label;
                lbl.fontSize = Mathf.RoundToInt(9.5f * s);
                lblRt.sizeDelta = new Vector2(w - 4f * s, h - 4f * s);
                lblRt.anchoredPosition = Vector2.zero;
                lbl.gameObject.SetActive(true);
            }

            // 3. 顶部微光 LED 激活指示灯 (3.5x3.5px)
            GameObject ledObj = UIFactory.CreatePanel(itemObj.transform, "ActiveLed", new Vector2(3.5f * s, 3.5f * s),
                new Vector2(-w * 0.5f + 3.5f * s, h * 0.5f - 3.5f * s), initialActive ? ledCol : WidgetStyleManager.Surface(SurfaceStyleRole.LedOff));
            Image ledImg = ledObj.GetComponent<Image>();
            ledImg.raycastTarget = false;

            // 4. 事件转发代理
            Button itemBtn = itemObj.AddComponent<Button>();
            itemBtn.transition = Selectable.Transition.ColorTint;
            itemBtn.targetGraphic = bg;

            ModernToolbarButtonProxy proxy = itemObj.AddComponent<ModernToolbarButtonProxy>();
            proxy.OnLeftClick = onLeftClick;
            proxy.OnRightClick = onRightClick;
            proxy.OnHoverEnter = onHoverEnter;
            proxy.OnHoverExit = onHoverExit;

            FavItemView view = new FavItemView
            {
                Root = itemObj,
                Background = bg,
                IconRaw = rawImg,
                LabelText = lbl,
                ActiveLed = ledImg,
                Proxy = proxy,
                Name = label,
                IsActive = initialActive
#if KSP_RUNTIME
                , KspButton = kspBtnRef
#endif
            };

            _itemViews.Add(view);
        }

        private struct ButtonStateSnapshot
        {
            public Texture Texture;
            public bool HasTexture;
            public bool Active;
        }
        private ButtonStateSnapshot[] _cachedButtonStates;
        private bool _needsRepopulate;
        private int _snapshotCount;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            if (_isCollapsed) return;

            float now = Time.unscaledTime;
            if (now - _lastSyncTime.Value < 1.0f) return;
            _lastSyncTime.Update(now);

#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    int currentCount = (stockBtns != null ? stockBtns.Count : 0) + (modBtns != null ? modBtns.Count : 0);

                    if (currentCount != _cachedButtonCount.Value)
                    {
                        _needsRepopulate = true;
                        return;
                    }

                    if (_cachedButtonStates == null || _cachedButtonStates.Length < _itemViews.Count)
                    {
                        _cachedButtonStates = new ButtonStateSnapshot[_itemViews.Count];
                    }
                    _snapshotCount = _itemViews.Count;

                    for (int i = 0; i < _itemViews.Count; i++)
                    {
                        var view = _itemViews[i];
                        if (view == null || view.KspButton == null)
                        {
                            _cachedButtonStates[i] = default;
                            continue;
                        }

                        var kspBtn = view.KspButton;
                        Texture tex = (kspBtn.sprite != null) ? kspBtn.sprite.texture : null;
                        bool active = (kspBtn.toggleButton != null && kspBtn.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True);
                        _cachedButtonStates[i] = new ButtonStateSnapshot
                        {
                            Texture = tex,
                            HasTexture = tex != null,
                            Active = active
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("FavToolbar_Heartbeat", $"Failed heartbeat KSP button states: {ex.Message}");
            }
#endif
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_isCollapsed) return;

#if KSP_RUNTIME
            if (_needsRepopulate)
            {
                _needsRepopulate = false;
                PopulateButtons(_contentRt, CurrentDpiScale);
                return;
            }

            if (_cachedButtonStates == null || _itemViews == null) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(context.Theme ?? _currentTheme ?? WidgetStyleManager.Instance?.CurrentTheme);
            Color ledOn = theme.AccentPrimary;
            Color ledOff = WidgetStyleManager.Surface(SurfaceStyleRole.LedOff);

            for (int i = 0; i < _snapshotCount && i < _itemViews.Count; i++)
            {
                var view = _itemViews[i];
                if (view == null) continue;

                var state = _cachedButtonStates[i];
                if (state.HasTexture && view.IconRaw != null && view.IconRaw.texture != state.Texture)
                {
                    view.IconRaw.texture = state.Texture;
                    view.IconRaw.SetActiveSafe(true);
                    if (view.LabelText != null) view.LabelText.SetActiveSafe(false);
                }

                if (view.ActiveLed != null && view.IsActive != state.Active)
                {
                    view.IsActive = state.Active;
                    view.ActiveLed.SetColor(state.Active ? ledOn : ledOff);
                }
            }
#endif
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;
            base.ApplyTheme(theme);

            ApplyCard(_panelBg, _panelOutline, CardStyleRole.Normal, theme);
            if (_accentStripe != null) _accentStripe.color = (Color)theme.AccentPrimary;
            if (_headerTitle != null) ApplyText(_headerTitle, TextStyleRole.Cardinal, theme);

            if (_collapseBtn != null)
            {
                ApplyButton(_collapseBtn, _collapseBtn.GetComponent<Image>(), _collapseBtnText, ButtonVisualRole.Normal, false, theme);
            }
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_collapseBtn != null) _collapseBtn.onClick.RemoveAllListeners();
#if KSP_RUNTIME
            DockAnchorTracker.UnregisterWidget(this);
#endif
            base.OnDestroy();
        }
    }
}
