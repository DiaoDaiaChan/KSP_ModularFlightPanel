using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;
using ModularFlightPanel.UI.Settings;
using ModularFlightPanel.UI.Workbench.Tabs;

namespace ModularFlightPanel.UI.Workbench
{
    /// <summary>
    /// 全新现代航电暗晶工作台 3.0 主视图控制器 (WorkbenchCanvasView)
    /// 核心职责：
    /// 1. 托管独立高刷 UGUI Canvas 与 GPU 程序化暗晶着色器；
    /// 2. 构建现代 IDE 级 Activity Bar、Top App Bar、自适应工作台视口与底部极简悬浮药丸 Dock；
    /// 3. 彻底取代 Unity 遗留 IMGUI，实现 0 GC、0 掉帧与全硬件加速渲染。
    /// </summary>
    public class WorkbenchCanvasView : MonoBehaviour
    {
        private static WorkbenchCanvasView _instance;
        public static WorkbenchCanvasView Instance => _instance;

        // 独立子 GameObject 托管 Canvas，彻底避免污染 NavballPlugin 宿主
        private GameObject _canvasObj;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private CanvasGroup _windowCanvasGroup;
        private RectTransform _windowRt;
        private Image _windowBgImg;

        // 顶栏实时遥测胶囊引用
        private Text _vesselText;
        private Text _frameText;
        private Text _fpsText;
        private float _telemetryTimer = 0f;

        // 左侧 Activity Bar 与标签页系统
        private int _currentTabIndex = 0;
        private IWorkbenchTabView[] _tabViews;
        private GameObject[] _activityButtons;
        private RectTransform _tabContentContainer;

        // 极简悬浮药丸 Dock (画布排版模式)
        private GameObject _floatingDockObj;
        private Text _dockSelInfoText;

        // 窗体拉伸、拖拽与最大化几何
        private float _windowWidth = 1060f;
        private float _windowHeight = 670f;
        private Vector2 _windowPos = Vector2.zero;
        private bool _isMaximized = false;
        private Vector2 _preMaximizePos;
        private Vector2 _preMaximizeSize;
        private Text _maximizeBtnText;

        // 全屏射线穿透拦截底板
        private GameObject _blockerObj;

        // 全局命令面板 (Ctrl+K)
        private GameObject _cmdPaletteObj;
        private bool _isCmdPaletteOpen = false;
        private InputField _cmdPaletteInput;
        private RectTransform _cmdPaletteResults;
        private readonly List<Action> _cmdPaletteActions = new List<Action>();

        private void Awake()
        {
            _instance = this;
            BuildWorkbenchUI();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_canvasObj != null)
            {
                Destroy(_canvasObj);
                _canvasObj = null;
            }
        }

        public void EnsureBuilt()
        {
            if (_canvas == null)
            {
                BuildWorkbenchUI();
            }
        }

        private void BuildWorkbenchUI()
        {
            WorkbenchStyleEngine.EnsureInitialized();

            // 1. 独立 Overlay 画布，完全隔离于 NavballPlugin 宿主 GameObject
            _canvasObj = new GameObject("MFP_ModernWorkbench_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasObj.transform.SetParent(transform, false);

            _canvas = _canvasObj.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 8500;
            _canvas.pixelPerfect = false;

            _scaler = _canvasObj.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // 2. 全屏射线拦截器 (阻止点击穿透到 3D 飞船零件)
            _blockerObj = new GameObject("RaycastBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _blockerObj.transform.SetParent(_canvasObj.transform, false);
            RectTransform bRt = _blockerObj.GetComponent<RectTransform>();
            bRt.anchorMin = Vector2.zero;
            bRt.anchorMax = Vector2.one;
            bRt.sizeDelta = Vector2.zero;
            Image bImg = _blockerObj.GetComponent<Image>();
            bImg.color = new Color(0f, 0f, 0f, 0.005f); // 极弱透明阻断
            bImg.raycastTarget = true;

            // 3. 读取并同步持久化窗口几何尺寸
            ApplyWindowGeometryFromSettings();

            // 4. 工作台主窗体
            GameObject winObj = new GameObject("MainWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            winObj.transform.SetParent(_canvasObj.transform, false);

            _windowRt = winObj.GetComponent<RectTransform>();
            _windowRt.sizeDelta = new Vector2(_windowWidth, _windowHeight);
            _windowRt.anchoredPosition = _windowPos;

            _windowBgImg = winObj.GetComponent<Image>();
            _windowBgImg.material = WorkbenchStyleEngine.GetWindowGlassMaterial();
            _windowBgImg.color = WorkbenchStyleEngine.ColorWindowBg;

            _windowCanvasGroup = winObj.GetComponent<CanvasGroup>();

            // 窗体内部垂直主布局
            VerticalLayoutGroup winVlg = winObj.AddComponent<VerticalLayoutGroup>();
            winVlg.childForceExpandWidth = true;
            winVlg.childForceExpandHeight = false;
            winVlg.childControlWidth = true;
            winVlg.childControlHeight = true;
            winVlg.spacing = 0f;
            winVlg.padding = new RectOffset(4, 4, 4, 4);

            BuildTopHeader(winObj.transform);
            BuildBodyArea(winObj.transform);
            BuildFooterBar(winObj.transform);

            // 5. 画布排版模式极简悬浮药丸 Dock
            BuildFloatingCanvasDock();

            // 6. 全局命令面板 (Ctrl+K)
            BuildCommandPalette();

            // 7. 初始化 5 大标签页视图
            _tabViews = new IWorkbenchTabView[]
            {
                new WorkbenchTabStudio(),
                new WorkbenchTabThemes(),
                new WorkbenchTabProfiles(),
                new WorkbenchTabDiagnostics(),
                new WorkbenchTabPreferences()
            };

            SwitchTab(0);
            SetVisible(false, false);
        }

        private void ApplyWindowGeometryFromSettings()
        {
            if (SettingsGUI.Instance != null)
            {
                var r = SettingsGUI.Instance.WindowRect;
                if (r.width > 100f && r.height > 100f)
                {
                    _windowWidth = r.width;
                    _windowHeight = r.height;
                    float posX = r.x + r.width * 0.5f - Screen.width * 0.5f;
                    float posY = Screen.height * 0.5f - (r.y + r.height * 0.5f);
                    _windowPos = new Vector2(posX, posY);
                    return;
                }
            }

            _windowWidth = SettingsGUI.DefaultWindowWidth;
            _windowHeight = SettingsGUI.DefaultWindowHeight;
            _windowPos = Vector2.zero;
        }

        private void SyncWindowGeometryToSettings()
        {
            if (_windowRt == null) return;
            float guiX = (Screen.width * 0.5f + _windowRt.anchoredPosition.x) - _windowRt.sizeDelta.x * 0.5f;
            float guiY = Screen.height * 0.5f - _windowRt.anchoredPosition.y - _windowRt.sizeDelta.y * 0.5f;
            SettingsGUI.Instance?.UpdateWindowGeometry(guiX, guiY, _windowRt.sizeDelta.x, _windowRt.sizeDelta.y);
        }

        public void ToggleMaximizeWindow()
        {
            if (_windowRt == null) return;

            if (!_isMaximized)
            {
                _preMaximizePos = _windowRt.anchoredPosition;
                _preMaximizeSize = _windowRt.sizeDelta;
                _isMaximized = true;

                float maxW = Mathf.Max(860f, Screen.width - 40f);
                float maxH = Mathf.Max(480f, Screen.height - 40f);
                _windowRt.sizeDelta = new Vector2(maxW, maxH);
                _windowRt.anchoredPosition = Vector2.zero;

                if (_maximizeBtnText != null) _maximizeBtnText.text = "⧉";
                Settings.MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_MAXIMIZE_WINDOW", "⛶ 已最大化窗口"));
            }
            else
            {
                _isMaximized = false;
                _windowRt.sizeDelta = (_preMaximizeSize.x > 100f) ? _preMaximizeSize : new Vector2(SettingsGUI.DefaultWindowWidth, SettingsGUI.DefaultWindowHeight);
                _windowRt.anchoredPosition = _preMaximizePos;

                if (_maximizeBtnText != null) _maximizeBtnText.text = "⛶";
                Settings.MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_RESTORE_WINDOW", "⧉ 已还原窗口尺寸"));
            }

            SyncWindowGeometryToSettings();
        }


        #region Top Header Bar

        private void BuildTopHeader(Transform parent)
        {
            GameObject header = WorkbenchControls.CreatePanel(parent, "TopHeader", new Vector2(0f, 44f));
            var hLe = header.GetComponent<LayoutElement>() ?? header.AddComponent<LayoutElement>();
            hLe.preferredHeight = 44f;
            hLe.minHeight = 44f;
            hLe.flexibleHeight = 0f;

            var hHlg = header.AddComponent<HorizontalLayoutGroup>();
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = true;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.spacing = 10f;
            hHlg.padding = new RectOffset(12, 12, 6, 6);

            // 允许拖拽移动整个窗口
            var dragHandler = header.AddComponent<WorkbenchWindowDragHandler>();
            dragHandler.TargetWindow = _windowRt;
            dragHandler.OnDragEnd = SyncWindowGeometryToSettings;

            // 品牌标题与版本徽标
            GameObject titleObj = new GameObject("BrandTitle", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(header.transform, false);
            var tLe = titleObj.AddComponent<LayoutElement>();
            tLe.preferredWidth = 230f;
            tLe.minWidth = 200f;
            tLe.flexibleWidth = 0f;

            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = WorkbenchControls.MainFont;
            titleTxt.fontSize = 13;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            titleTxt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            titleTxt.text = "MODULAR FLIGHT PANEL 3.0";
            titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 弹性空白
            GameObject flex = new GameObject("FlexSpace", typeof(RectTransform));
            flex.transform.SetParent(header.transform, false);
            var le = flex.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            // 顶栏实时遥测状态胶囊
            _vesselText = CreateHeaderChip(header.transform, "🚀 载具: ---");
            _frameText = CreateHeaderChip(header.transform, "🌐 参考系: ---");
            _fpsText = CreateHeaderChip(header.transform, "⚡ 60 FPS | 0.1ms");

            // 全局命令面板快捷入口 (Ctrl+K)
            WorkbenchControls.CreateButton(header.transform, "CmdPaletteBtn", I18n.Tr("CMD_PALETTE_BTN_OPEN", "🔍 命令 (Ctrl+K)"), new Vector2(115f, 28f), ToggleCommandPalette, false, 11);

            // 画布自由排版模式快捷入口 (次级按钮，不占用主视觉焦点)
            WorkbenchControls.CreateButton(header.transform, "CanvasModeBtn", "📐 画布自由排版", new Vector2(125f, 28f), () =>
            {
                SettingsGUI.Instance?.EnterCanvasLayoutMode();
            }, false, 11);

            // 最大化 / 还原按钮
            GameObject maxBtn = WorkbenchControls.CreateButton(header.transform, "MaximizeBtn", "⛶", new Vector2(28f, 28f), ToggleMaximizeWindow, false, 12);
            _maximizeBtnText = maxBtn.GetComponentInChildren<Text>();

            // 顶栏关闭按钮
            WorkbenchControls.CreateButton(header.transform, "CloseBtn", "✕", new Vector2(28f, 28f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, false, 12);
        }

        private Text CreateHeaderChip(Transform parent, string initialText)
        {
            GameObject chip = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            chip.transform.SetParent(parent, false);

            var cLe = chip.AddComponent<LayoutElement>();
            cLe.preferredWidth = 140f;
            cLe.minWidth = 120f;
            cLe.preferredHeight = 24f;
            cLe.flexibleWidth = 0f;

            RectTransform cRt = chip.GetComponent<RectTransform>();
            cRt.sizeDelta = new Vector2(140f, 24f);

            Image cImg = chip.GetComponent<Image>();
            cImg.material = WorkbenchStyleEngine.GetPillDockMaterial(false);
            cImg.color = new Color(0.05f, 0.08f, 0.12f, 0.70f);

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(chip.transform, false);
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            txtRt.offsetMin = new Vector2(6f, 0f);
            txtRt.offsetMax = new Vector2(-6f, 0f);

            Text txt = txtObj.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = 11;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = WorkbenchStyleEngine.ColorTextPrimary;
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = initialText;
            txt.raycastTarget = false;

            return txt;
        }

        #endregion

        #region Body Area (Activity Bar + Content)

        private void BuildBodyArea(Transform parent)
        {
            GameObject body = new GameObject("BodyArea", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            body.transform.SetParent(parent, false);

            RectTransform bodyRt = body.GetComponent<RectTransform>();
            bodyRt.sizeDelta = new Vector2(0f, _windowHeight - 92f);
            var le = body.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.flexibleWidth = 1f;

            HorizontalLayoutGroup bHlg = body.GetComponent<HorizontalLayoutGroup>();
            bHlg.childForceExpandWidth = false;
            bHlg.childForceExpandHeight = true;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.spacing = 6f;
            bHlg.padding = new RectOffset(6, 6, 6, 6);

            // =========================================================================
            // 左侧 Activity Bar 图钉导航栏 (52px 定宽，采用垂直导航轨按钮设计)
            // =========================================================================
            GameObject activityBar = WorkbenchControls.CreatePanel(body.transform, "ActivityBar", new Vector2(52f, 0f));
            var abLe = activityBar.GetComponent<LayoutElement>() ?? activityBar.AddComponent<LayoutElement>();
            abLe.preferredWidth = 52f;
            abLe.minWidth = 52f;
            abLe.flexibleWidth = 0f;
            abLe.flexibleHeight = 1f;

            VerticalLayoutGroup aVlg = activityBar.AddComponent<VerticalLayoutGroup>();
            aVlg.childForceExpandWidth = true;
            aVlg.childForceExpandHeight = false;
            aVlg.childControlWidth = true;
            aVlg.childControlHeight = true;
            aVlg.spacing = 6f;
            aVlg.padding = new RectOffset(2, 2, 8, 8);

            string[] tabIcons = new string[] { "🛠️", "🎨", "💾", "🚀", "⚙️" };
            string[] tabNames = new string[] { "工坊", "主题", "档案", "遥测", "偏好" };
            _activityButtons = new GameObject[tabIcons.Length];

            for (int i = 0; i < tabIcons.Length; i++)
            {
                int index = i;
                _activityButtons[i] = WorkbenchControls.CreateActivityRailButton(activityBar.transform, "TabBtn_" + i, tabIcons[i], tabNames[i], () =>
                {
                    SwitchTab(index);
                }, i == 0);
            }

            // =========================================================================
            // 右侧标签页视口容器
            // =========================================================================
            GameObject contentContainer = new GameObject("ContentContainer", typeof(RectTransform));
            contentContainer.transform.SetParent(body.transform, false);
            _tabContentContainer = contentContainer.GetComponent<RectTransform>();
            var cLe = contentContainer.AddComponent<LayoutElement>();
            cLe.flexibleWidth = 1f;
            cLe.flexibleHeight = 1f;
        }

        #endregion

        #region Footer Bar

        private void BuildFooterBar(Transform parent)
        {
            GameObject footer = WorkbenchControls.CreatePanel(parent, "FooterBar", new Vector2(0f, 36f));
            var fLe = footer.GetComponent<LayoutElement>() ?? footer.AddComponent<LayoutElement>();
            fLe.preferredHeight = 36f;
            fLe.minHeight = 36f;
            fLe.flexibleHeight = 0f;

            var fHlg = footer.AddComponent<HorizontalLayoutGroup>();
            fHlg.childForceExpandWidth = false;
            fHlg.childForceExpandHeight = true;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.spacing = 10f;
            fHlg.padding = new RectOffset(12, 12, 4, 4);

            GameObject statusText = new GameObject("StatusText", typeof(RectTransform), typeof(Text));
            statusText.transform.SetParent(footer.transform, false);
            var stLe = statusText.AddComponent<LayoutElement>();
            stLe.flexibleWidth = 1f;

            Text st = statusText.GetComponent<Text>();
            st.font = WorkbenchControls.MainFont;
            st.fontSize = 11;
            st.alignment = TextAnchor.MiddleLeft;
            st.color = WorkbenchStyleEngine.ColorTextMuted;
            st.text = "快捷键: Alt+N / ESC 随时唤出与关闭 | F2 隐藏全屏 UI | 0 GC 高性能硬件加速模式";
            st.horizontalOverflow = HorizontalWrapMode.Overflow;

            GameObject flex = new GameObject("FlexSpace", typeof(RectTransform));
            flex.transform.SetParent(footer.transform, false);
            var le = flex.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            WorkbenchControls.CreateButton(footer.transform, "SaveCloseBtn", "✔ 保存配置并关闭 (Alt+N)", new Vector2(200f, 26f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, true, 11);

            // 右下角拉伸手柄
            GameObject resizeGrip = new GameObject("ResizeGrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            resizeGrip.transform.SetParent(footer.transform, false);
            resizeGrip.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            var rgLe = resizeGrip.AddComponent<LayoutElement>();
            rgLe.preferredWidth = 24f;
            rgLe.minWidth = 24f;
            rgLe.preferredHeight = 24f;
            rgLe.minHeight = 24f;

            Text gt = resizeGrip.GetComponent<Text>();
            gt.font = WorkbenchControls.MainFont;
            gt.fontSize = 14;
            gt.alignment = TextAnchor.MiddleCenter;
            gt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            gt.text = "◢";

            var resizeHandler = resizeGrip.AddComponent<WorkbenchWindowResizeHandler>();
            resizeHandler.TargetWindow = _windowRt;
            resizeHandler.OnResized = (w, h) =>
            {
                _windowWidth = w;
                _windowHeight = h;
            };
            resizeHandler.OnResizeEnd = SyncWindowGeometryToSettings;
        }

        #endregion

        #region Floating Canvas Dock (画布排版模式)

        private void BuildFloatingCanvasDock()
        {
            _floatingDockObj = new GameObject("FloatingCanvasDock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _floatingDockObj.transform.SetParent(_canvasObj.transform, false);

            RectTransform dockRt = _floatingDockObj.GetComponent<RectTransform>();
            dockRt.anchorMin = new Vector2(0.5f, 0f);
            dockRt.anchorMax = new Vector2(0.5f, 0f);
            dockRt.pivot = new Vector2(0.5f, 0f);
            dockRt.anchoredPosition = new Vector2(0f, 16f); // 牢牢吸附屏幕底端
            dockRt.sizeDelta = new Vector2(540f, 44f);

            Image dockImg = _floatingDockObj.GetComponent<Image>();
            dockImg.material = WorkbenchStyleEngine.GetPillDockMaterial(false);
            dockImg.color = WorkbenchStyleEngine.ColorPillDarkBg;

            var dHlg = _floatingDockObj.AddComponent<HorizontalLayoutGroup>();
            dHlg.childForceExpandWidth = false;
            dHlg.childForceExpandHeight = true;
            dHlg.spacing = 10f;
            dHlg.padding = new RectOffset(14, 14, 6, 6);

            // 标题
            GameObject dTitle = new GameObject("DockTitle", typeof(RectTransform), typeof(Text));
            dTitle.transform.SetParent(_floatingDockObj.transform, false);
            Text dt = dTitle.GetComponent<Text>();
            dt.font = WorkbenchControls.MainFont;
            dt.fontSize = 12;
            dt.fontStyle = FontStyle.Bold;
            dt.alignment = TextAnchor.MiddleLeft;
            dt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            dt.text = "📐 画布自由排版";

            // 已选计数
            GameObject selInfo = new GameObject("SelInfo", typeof(RectTransform), typeof(Text));
            selInfo.transform.SetParent(_floatingDockObj.transform, false);
            selInfo.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 32f);
            _dockSelInfoText = selInfo.GetComponent<Text>();
            _dockSelInfoText.font = WorkbenchControls.MainFont;
            _dockSelInfoText.fontSize = 11;
            _dockSelInfoText.alignment = TextAnchor.MiddleLeft;
            _dockSelInfoText.color = WorkbenchStyleEngine.ColorWarning;
            _dockSelInfoText.text = "自由拖拽/对齐中";

            // 快捷画板
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "NewArtboardBtn", "🎨 +画板", new Vector2(80f, 28f), () =>
            {
                WidgetLayoutManager.CreateArtboard(false);
            }, false, 11);

            // 返回工坊
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "ReturnBtn", "✔ 返回工坊 (Alt+N)", new Vector2(150f, 28f), () =>
            {
                SettingsGUI.Instance?.ExitCanvasLayoutMode();
            }, true, 11);

            // 关闭
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "CloseBtn", "✕", new Vector2(28f, 28f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, false, 12);
        }

        #endregion

        #region Command Palette (Ctrl+K)

        private void BuildCommandPalette()
        {
            _cmdPaletteObj = new GameObject("CommandPalette_Modal", typeof(RectTransform), typeof(CanvasRenderer));
            _cmdPaletteObj.transform.SetParent(_canvasObj.transform, false);

            RectTransform modalRt = _cmdPaletteObj.GetComponent<RectTransform>();
            modalRt.anchorMin = Vector2.zero;
            modalRt.anchorMax = Vector2.one;
            modalRt.sizeDelta = Vector2.zero;

            // 背景全屏变暗阻断板
            GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(_cmdPaletteObj.transform, false);
            RectTransform bdRt = backdrop.GetComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.sizeDelta = Vector2.zero;
            Image bdImg = backdrop.GetComponent<Image>();
            bdImg.color = new Color(0.02f, 0.05f, 0.08f, 0.65f);
            Button bdBtn = backdrop.GetComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(CloseCommandPalette);

            // 居中暗晶玻璃悬浮弹窗
            GameObject cardObj = new GameObject("PaletteCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            cardObj.transform.SetParent(_cmdPaletteObj.transform, false);

            RectTransform cardRt = cardObj.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(640f, 440f);
            cardRt.anchoredPosition = new Vector2(0f, 30f);

            Image cardImg = cardObj.GetComponent<Image>();
            cardImg.material = WorkbenchStyleEngine.GetCardGlassMaterial(true);
            cardImg.color = WorkbenchStyleEngine.ColorCardBg;

            VerticalLayoutGroup vlg = cardObj.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            // 1. 顶部检索行
            GameObject searchRow = new GameObject("SearchRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            searchRow.transform.SetParent(cardObj.transform, false);
            searchRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);
            var srLe = searchRow.AddComponent<LayoutElement>();
            srLe.preferredHeight = 36f;
            srLe.minHeight = 36f;
            srLe.flexibleHeight = 0f;

            var srHlg = searchRow.GetComponent<HorizontalLayoutGroup>();
            srHlg.childForceExpandWidth = false;
            srHlg.childForceExpandHeight = true;
            srHlg.spacing = 8f;

            GameObject inputObj = WorkbenchControls.CreateTextField(searchRow.transform, "PaletteInput", "", I18n.Tr("CMD_PALETTE_PLACEHOLDER", "🔍 搜索标签页、快速操作、航电组件、遥测参数 (Ctrl+K)..."), new Vector2(560f, 36f), OnCommandPaletteInputChanged);
            var inLe = inputObj.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f;
            _cmdPaletteInput = inputObj.GetComponent<InputField>();

            WorkbenchControls.CreateButton(searchRow.transform, "ClosePaletteBtn", "✕", new Vector2(28f, 28f), CloseCommandPalette, false, 12);

            // 2. 搜索结果滚动列表
            _cmdPaletteResults = WorkbenchControls.CreateScrollView(cardObj.transform, "ResultsScrollView", new Vector2(0f, 340f), out GameObject scrollObj);
            var scLe = scrollObj.AddComponent<LayoutElement>();
            scLe.flexibleHeight = 1f;

            // 3. 底部操作提示栏
            GameObject footerRow = new GameObject("FooterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            footerRow.transform.SetParent(cardObj.transform, false);
            footerRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 22f);
            var frLe = footerRow.AddComponent<LayoutElement>();
            frLe.preferredHeight = 22f;
            frLe.minHeight = 22f;
            frLe.flexibleHeight = 0f;

            var frHlg = footerRow.GetComponent<HorizontalLayoutGroup>();
            frHlg.childForceExpandWidth = false;
            frHlg.childForceExpandHeight = true;

            GameObject hintObj = new GameObject("HintText", typeof(RectTransform), typeof(Text));
            hintObj.transform.SetParent(footerRow.transform, false);
            var hntLe = hintObj.AddComponent<LayoutElement>();
            hntLe.flexibleWidth = 1f;
            Text hntTxt = hintObj.GetComponent<Text>();
            hntTxt.font = WorkbenchControls.MainFont;
            hntTxt.fontSize = 10;
            hntTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            hntTxt.text = I18n.Tr("CMD_PALETTE_HINT", "↑/↓ 键选择 | Enter 确认执行 | ESC 关闭");

            _cmdPaletteObj.SetActive(false);
        }

        public void ToggleCommandPalette()
        {
            if (_isCmdPaletteOpen)
            {
                CloseCommandPalette();
            }
            else
            {
                OpenCommandPalette();
            }
        }

        public void OpenCommandPalette()
        {
            EnsureBuilt();
            if (_cmdPaletteObj == null) return;

            _isCmdPaletteOpen = true;
            _cmdPaletteObj.SetActive(true);
            if (_cmdPaletteInput != null)
            {
                _cmdPaletteInput.text = "";
                _cmdPaletteInput.ActivateInputField();
            }
            PopulateCommandPalette("");
        }

        public void CloseCommandPalette()
        {
            if (_cmdPaletteObj == null) return;
            _isCmdPaletteOpen = false;
            _cmdPaletteObj.SetActive(false);
            MFPInputLock.SetKeyboardFocusLock(false);
        }

        private void OnCommandPaletteInputChanged(string query)
        {
            PopulateCommandPalette(query);
        }

        private void PopulateCommandPalette(string query)
        {
            if (_cmdPaletteResults == null) return;

            _cmdPaletteActions.Clear();
            for (int i = _cmdPaletteResults.childCount - 1; i >= 0; i--)
            {
                var c = _cmdPaletteResults.GetChild(i).gameObject;
                c.SetActive(false);
                Destroy(c);
            }

            int count = 0;
            const int maxResults = 30;

            // 1. 标签页导航 (Tabs)
            string[] tabNames = new string[]
            {
                I18n.Tr("UI_TAB_STUDIO", "🛠️ 航电工坊"),
                I18n.Tr("UI_TAB_THEMES", "🎨 视觉风格"),
                I18n.Tr("UI_TAB_PROFILES", "💾 档案与配置"),
                I18n.Tr("UI_TAB_DIAGNOSTICS", "🚀 诊断与沙盒"),
                I18n.Tr("UI_TAB_PREFERENCES", "⚙️ 偏好设置")
            };
            for (int i = 0; i < tabNames.Length; i++)
            {
                if (count >= maxResults) break;
                int tabIdx = i;
                string tName = tabNames[i];
                if (string.IsNullOrEmpty(query) || tName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddCommandPaletteRow(I18n.Tr("CMD_PALETTE_TAG_TAB", "标签页"), WorkbenchStyleEngine.ColorAccentPrimary, tName, "TAB", () =>
                    {
                        SwitchTab(tabIdx);
                    });
                    count++;
                }
            }

            // 2. 快速操作 (Actions)
            var actions = new (string Title, Action Act)[]
            {
                (I18n.Tr("CMD_ACTION_CANVAS_MODE", "📐 进入画布自由排版模式"), () => SettingsGUI.Instance?.EnterCanvasLayoutMode()),
                (I18n.Tr("CMD_ACTION_SAVE_LAYOUT", "💾 保存当前布局与配置"), () => { WidgetLayoutManager.Instance?.SaveLayout(); Settings.MFPGuiSkin.ShowToast(I18n.Tr("TOAST_LAYOUT_SAVED", "✔ 布局配置已成功保存！")); }),
                (I18n.Tr("CMD_ACTION_TOGGLE_MAX", "⛶ 最大化 / 还原工作台窗口"), ToggleMaximizeWindow),
                (I18n.Tr("CMD_ACTION_RESET_WINDOW", "🔄 还原工作台默认尺寸 (1060×670)"), () => { SettingsGUI.Instance?.ResetToDefault(); ApplyWindowGeometryFromSettings(); if (_windowRt != null) { _windowRt.sizeDelta = new Vector2(_windowWidth, _windowHeight); _windowRt.anchoredPosition = _windowPos; } }),
                (I18n.Tr("CMD_ACTION_CREATE_ARTBOARD", "🎨 新建自由航电画板"), () => { WidgetLayoutManager.CreateArtboard(false); Settings.MFPGuiSkin.ShowToast(I18n.Tr("LIB_TOAST_ARTBOARD_ADDED", "已创建自由航电画板！可在右侧工坊开始自由布局")); }),
                (I18n.Tr("CMD_ACTION_CLEAR_SEL", "🧹 清除画布组件选中"), () => WidgetSelectionManager.ClearSelection()),
                (I18n.Tr("CMD_ACTION_CLOSE", "✕ 关闭航电工作台"), () => SettingsGUI.Instance?.ToggleWindow())
            };

            foreach (var act in actions)
            {
                if (count >= maxResults) break;
                if (string.IsNullOrEmpty(query) || act.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddCommandPaletteRow(I18n.Tr("CMD_PALETTE_TAG_ACTION", "快速操作"), WorkbenchStyleEngine.ColorAccentSecondary, act.Title, "ACTION", act.Act);
                    count++;
                }
            }

            // 3. 航电组件库 (Widgets)
            var widgetDefs = WidgetRegistry.AllDescriptors;
            if (widgetDefs != null)
            {
                foreach (var w in widgetDefs)
                {
                    if (count >= maxResults) break;
                    if (w == null) continue;
                    bool match = !string.IsNullOrEmpty(query) && (
                        (w.DisplayName != null && w.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (w.TypeName != null && w.TypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (w.Description != null && w.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));

                    if (match)
                    {
                        string wType = w.TypeName;
                        string wName = w.DisplayName ?? w.TypeName;
                        string wDesc = w.Description ?? wType;
                        AddCommandPaletteRow(I18n.Tr("CMD_PALETTE_TAG_WIDGET", "航电组件"), WorkbenchStyleEngine.ColorWarning, $"➕ {wName}", $"{wType} | {wDesc}", () =>
                        {
                            WorkbenchTabStudio.AddWidgetToHud(wType);
                            SwitchTab(0);
                        });
                        count++;
                    }
                }
            }

            // 4. 736+ 遥测参数 (Telemetry)
            if (!string.IsNullOrEmpty(query) && query.Length >= 2)
            {
                var telemParams = TelemetryCatalog.Parameters;
                var ctx = FlightTelemetryContext.Current;

                foreach (var p in telemParams)
                {
                    if (count >= maxResults) break;
                    if (p == null) continue;
                    bool match = (p.Token != null && p.Token.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (p.DisplayName != null && p.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (p.Category != null && p.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (match)
                    {
                        string tok = p.Token;
                        string sample = TelemetryTokenEngine.Evaluate(tok, ctx);
                        if (string.IsNullOrEmpty(sample)) sample = "---";
                        string sub = $"{p.DisplayName} [{p.DefaultUnit}] | {sample}";

                        AddCommandPaletteRow(I18n.Tr("CMD_PALETTE_TAG_TELEM", "遥测参数"), WorkbenchStyleEngine.ColorSuccess, tok, sub, () =>
                        {
                            var sel = WidgetSelectionManager.SelectedWidgets.FirstOrDefault();
                            if (sel != null && sel.Config != null)
                            {
                                sel.Config.NumericToken = tok;
                                WidgetLayoutManager.Instance?.SaveLayout();
                                Settings.MFPGuiSkin.ShowToast(string.Format(I18n.Tr("DRAWER_TOAST_APPLIED", "已填入参数: {0}"), tok));
                            }
                            else
                            {
                                GUIUtility.systemCopyBuffer = tok;
                                Settings.MFPGuiSkin.ShowToast(string.Format(I18n.Tr("DRAWER_TOAST_COPIED", "已复制 {0} 到剪贴板"), tok));
                            }
                        });
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                GameObject noMatch = new GameObject("NoMatch", typeof(RectTransform), typeof(Text));
                noMatch.transform.SetParent(_cmdPaletteResults, false);
                Text nmTxt = noMatch.GetComponent<Text>();
                nmTxt.font = WorkbenchControls.MainFont;
                nmTxt.fontSize = 12;
                nmTxt.alignment = TextAnchor.MiddleCenter;
                nmTxt.color = WorkbenchStyleEngine.ColorTextMuted;
                nmTxt.text = I18n.Tr("CMD_PALETTE_NO_MATCH", "未找到匹配的命令或参数");
            }
        }

        private void AddCommandPaletteRow(string tag, Color tagColor, string title, string subtitle, Action onExecute)
        {
            _cmdPaletteActions.Add(onExecute);

            GameObject card = WorkbenchControls.CreateCard(_cmdPaletteResults, "Row_" + title, new Vector2(0f, 36f), true);
            var hlg = card.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 4);

            WorkbenchControls.CreatePill(card.transform, "Tag", tag, tagColor, 10);

            GameObject txtCol = new GameObject("TxtCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            txtCol.transform.SetParent(card.transform, false);
            var tcLe = txtCol.AddComponent<LayoutElement>();
            tcLe.flexibleWidth = 1f;

            var tcVlg = txtCol.GetComponent<VerticalLayoutGroup>();
            tcVlg.childForceExpandWidth = true;
            tcVlg.childForceExpandHeight = false;
            tcVlg.spacing = 1f;

            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(txtCol.transform, false);
            Text tt = titleObj.GetComponent<Text>();
            tt.font = WorkbenchControls.MainFont;
            tt.fontSize = 11;
            tt.fontStyle = FontStyle.Bold;
            tt.color = WorkbenchStyleEngine.ColorTextPrimary;
            tt.text = title;

            GameObject subObj = new GameObject("Sub", typeof(RectTransform), typeof(Text));
            subObj.transform.SetParent(txtCol.transform, false);
            Text st = subObj.GetComponent<Text>();
            st.font = WorkbenchControls.MainFont;
            st.fontSize = 9;
            st.color = WorkbenchStyleEngine.ColorTextMuted;
            st.text = subtitle;

            // 点击触发
            WorkbenchControls.CreateButton(card.transform, "RunBtn", "▶", new Vector2(30f, 24f), () =>
            {
                onExecute?.Invoke();
                CloseCommandPalette();
            }, false, 11);
        }

        #endregion

        #region Public Control & State Machine

        public void SetVisible(bool isOpen, bool isCanvasLayoutMode)
        {
            EnsureBuilt();

            if (!isOpen)
            {
                CloseCommandPalette();
                _blockerObj?.SetActive(false);
                _windowRt?.gameObject.SetActive(false);
                _floatingDockObj?.SetActive(false);
                _canvasObj?.SetActive(false);
                return;
            }

            _canvasObj?.SetActive(true);

            if (isCanvasLayoutMode)
            {
                CloseCommandPalette();
                _blockerObj?.SetActive(false); // 画布排版模式不阻断飞船操作
                _windowRt?.gameObject.SetActive(false);
                _floatingDockObj?.SetActive(true);
            }
            else
            {
                _blockerObj?.SetActive(true);
                _windowRt?.gameObject.SetActive(true);
                _floatingDockObj?.SetActive(false);
                RefreshActiveTab();
            }
        }

        public void SwitchTab(int index)
        {
            if (_tabViews == null || index < 0 || index >= _tabViews.Length) return;

            _currentTabIndex = index;

            // 更新 Activity Bar 导航轨按钮激活状态
            if (_activityButtons != null)
            {
                for (int i = 0; i < _activityButtons.Length; i++)
                {
                    if (_activityButtons[i] != null)
                    {
                        var rail = _activityButtons[i].GetComponent<ModernRailButtonEffect>();
                        rail?.SetActiveState(i == _currentTabIndex);
                    }
                }
            }

            if (_tabContentContainer != null)
            {
                _tabViews[_currentTabIndex].Build(_tabContentContainer);
            }
        }

        public void RefreshActiveTab()
        {
            if (_tabViews != null && _currentTabIndex >= 0 && _currentTabIndex < _tabViews.Length)
            {
                _tabViews[_currentTabIndex]?.Refresh();
            }
        }

        private void Update()
        {
            // 全局快捷键 Ctrl+K 唤出/收起命令面板
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.K))
            {
                if (_canvasObj != null && _canvasObj.activeInHierarchy && _windowRt != null && _windowRt.gameObject.activeSelf)
                {
                    ToggleCommandPalette();
                }
            }
            else if (_isCmdPaletteOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseCommandPalette();
                return;
            }
            else if (_isCmdPaletteOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (_cmdPaletteActions.Count > 0 && _cmdPaletteActions[0] != null)
                {
                    _cmdPaletteActions[0].Invoke();
                    CloseCommandPalette();
                    return;
                }
            }

            if (_canvas == null || !_canvas.gameObject.activeInHierarchy) return;

            // 顶栏遥测胶囊 4Hz 节流平滑刷新
            _telemetryTimer += Time.unscaledDeltaTime;
            if (_telemetryTimer >= 0.25f)
            {
                _telemetryTimer = 0f;

                var ctx = FlightTelemetryContext.Current;
                if (_vesselText != null)
                {
                    _vesselText.text = $"🚀 载具: {ctx?.VesselName ?? "---"}";
                }
                if (_frameText != null)
                {
                    string f = TelemetryTokenEngine.Evaluate("{FRAME}", ctx);
                    _frameText.text = $"🌐 参考系: {(string.IsNullOrEmpty(f) ? "ORBIT" : f)}";
                }
                if (_fpsText != null)
                {
                    _fpsText.text = $"⚡ {MFPProfiler.CurrentFPS:F0} FPS | {MFPProfiler.AvgTotalMs:F2}ms";
                }

                if (_floatingDockObj != null && _floatingDockObj.activeSelf && _dockSelInfoText != null)
                {
                    int selCount = WidgetSelectionManager.Count;
                    _dockSelInfoText.text = selCount > 0 ? $"已选 {selCount} 项" : "自由调整中";
                }
            }

            // 激活标签页生命周期帧驱动
            if (_tabViews != null && _currentTabIndex >= 0 && _currentTabIndex < _tabViews.Length)
            {
                _tabViews[_currentTabIndex]?.OnUpdate();
            }
        }

        #endregion
    }

    #region Window Drag & Resize Handlers

    public class WorkbenchWindowDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform TargetWindow;
        public Action OnDragEnd;
        private Vector2 _dragOffset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                _dragOffset = TargetWindow.anchoredPosition - eventData.position;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                TargetWindow.anchoredPosition = eventData.position + _dragOffset;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OnDragEnd?.Invoke();
        }
    }

    public class WorkbenchWindowResizeHandler : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        public RectTransform TargetWindow;
        public Action<float, float> OnResized;
        public Action OnResizeEnd;

        public void OnDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                float newW = Mathf.Clamp(TargetWindow.sizeDelta.x + eventData.delta.x, 860f, Screen.width - 40f);
                float newH = Mathf.Clamp(TargetWindow.sizeDelta.y - eventData.delta.y, 480f, Screen.height - 40f);
                TargetWindow.sizeDelta = new Vector2(newW, newH);
                OnResized?.Invoke(newW, newH);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OnResizeEnd?.Invoke();
        }
    }

    #endregion
}
