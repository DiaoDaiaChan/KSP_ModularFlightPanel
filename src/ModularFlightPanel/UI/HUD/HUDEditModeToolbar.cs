using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 图形化编辑模式悬浮工具栏 (HUD Edit Mode Toolbar & Action Controller)
    /// 核心职责：
    /// 1. 宿主编辑模式顶部悬浮卡片 (撤销/重做、左中右顶底对齐、水平/垂直等距、网格/图层开关)；
    /// 2. 宿主直接吸附在选中组件旁侧的即时悬浮缩放、形变、旋转、图层快速操作盒；
    /// 3. 100% 采用现代化 UGUI + GPU 磨砂玻璃着色器 (ModernWorkbenchGlass.shader) 渲染，
    ///    彻底清除 Unity IMGUI OnGUI 引擎轮询与 GC 垃圾！
    /// </summary>
    public class HUDEditModeToolbar : MonoBehaviour
    {
        private FlightHUDManager _hudManager;

        // 全局微控件定制入口标记 (供 WidgetDragHandler 引用)
        public static bool IsSubControlCustomizerOpen { get; set; } = false;

        private static bool _isFavoriteModDockOpen = false;

        // UGUI 视觉图元根节点与卡片组件
        private GameObject _rootObj;
        private CanvasGroup _rootCanvasGroup;

        // 顶部工具栏
        private GameObject _topBarObj;
        private Text _selectionPillText;
        private Button _undoBtn;
        private Button _redoBtn;
        private Button[] _alignBtns;
        private Text _snapBtnText;
        private Text _gridBtnText;
        private Text _layerBtnText;
        private Text _modDockBtnText;

        // 组件旁侧即时浮动属性盒 (Contextual Inspector)
        private GameObject _inspectorCardObj;
        private RectTransform _inspectorCardRt;
        private Text _inspectorTitleText;
        private Text _inspectorStatsText;
        private Text _inspectorOpacityText;

        // 浮动图层抽屉 (Layer Drawer)
        private GameObject _layerDrawerObj;
        private Transform _layerContentTransform;
        private readonly List<GameObject> _layerRowPool = new List<GameObject>();

        // 常用模组抽屉 (Favorite Mod Dock)
        private GameObject _modDockObj;
        private Transform _modDockContentTransform;
        private readonly List<GameObject> _modDockBtnPool = new List<GameObject>();

        // 材质缓存
        private Material _glassMaterial;

        // 定频刷新节流
        private float _lastUiRefreshTime = 0f;
        private int _cachedSelectedCount = -1;

        public void Initialize(FlightHUDManager hudManager)
        {
            _hudManager = hudManager;
            enabled = false; // 默认严格休眠
        }

        private void Awake()
        {
            enabled = false;
        }

        private void OnEnable()
        {
            EnsureUIHierarchy();
            if (_rootObj != null) _rootObj.SetActive(true);
            RefreshAllVisuals(true);
        }

        private void OnDisable()
        {
            if (_rootObj != null) _rootObj.SetActive(false);
            FlightHUDManager.IsMouseOverFloatingToolbar = false;
            MFPInputLock.SetWindowHoverLock(false);
        }

        private void OnDestroy()
        {
            if (_rootObj != null)
            {
                Destroy(_rootObj);
                _rootObj = null;
            }
            if (_glassMaterial != null)
            {
                Destroy(_glassMaterial);
                _glassMaterial = null;
            }
        }

        private Material GetOrCreateGlassMaterial()
        {
            if (_glassMaterial != null) return _glassMaterial;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader ?? Shader.Find("UI/Default");
            if (s != null)
            {
                _glassMaterial = new Material(s);
                if (_glassMaterial.HasProperty("_GlassBgColor"))
                    _glassMaterial.SetColor("_GlassBgColor", new Color(0.05f, 0.08f, 0.13f, 0.94f));
                if (_glassMaterial.HasProperty("_BorderColor"))
                    _glassMaterial.SetColor("_BorderColor", new Color(0.20f, 0.35f, 0.52f, 0.85f));
                if (_glassMaterial.HasProperty("_AccentColor"))
                    _glassMaterial.SetColor("_AccentColor", new Color(0f, 0.88f, 1f, 1f));
                if (_glassMaterial.HasProperty("_CornerRadius"))
                    _glassMaterial.SetFloat("_CornerRadius", 0.035f);
                if (_glassMaterial.HasProperty("_BorderWidth"))
                    _glassMaterial.SetFloat("_BorderWidth", 0.008f);
            }
            return _glassMaterial;
        }

        private void EnsureUIHierarchy()
        {
            if (_rootObj != null) return;
            Canvas targetCanvas = _hudManager != null ? _hudManager.Canvas : GetComponentInParent<Canvas>();
            if (targetCanvas == null) return;

            // 1. 根节点挂载
            _rootObj = new GameObject("MFP_HUDEditModeToolbarRoot", typeof(RectTransform), typeof(CanvasGroup));
            _rootObj.transform.SetParent(targetCanvas.transform, false);

            var rootRt = _rootObj.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.sizeDelta = Vector2.zero;
            rootRt.anchoredPosition = Vector2.zero;

            _rootCanvasGroup = _rootObj.GetComponent<CanvasGroup>();

            // 2. 构建顶部主工具栏
            BuildTopBar(rootRt);

            // 3. 构建旁侧即时属性浮动盒
            BuildContextualInspector(rootRt);

            // 4. 构建右侧图层管理器抽屉
            BuildLayerDrawer(rootRt);

            // 5. 构建常用模组收纳坞
            BuildFavoriteModDock(rootRt);
        }

        #region TopBar 构建
        private void BuildTopBar(RectTransform parent)
        {
            _topBarObj = CreateGlassPanel(parent, "TopBar", new Vector2(1240f, 48f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f));
            AddHoverCatcher(_topBarObj);

            var hl = _topBarObj.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 4f;
            hl.padding = new RectOffset(10, 10, 6, 6);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // 标题与选择计数药丸
            var pillObj = new GameObject("SelectionPill", typeof(RectTransform), typeof(Image));
            pillObj.transform.SetParent(_topBarObj.transform, false);
            var pillRt = pillObj.GetComponent<RectTransform>();
            pillRt.sizeDelta = new Vector2(165f, 32f);
            pillObj.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.28f, 0.9f);

            var pillOutline = pillObj.AddComponent<Outline>();
            pillOutline.effectColor = new Color(0f, 0.88f, 1f, 0.4f);
            pillOutline.effectDistance = new Vector2(1f, -1f);

            _selectionPillText = UIFactory.CreateText(pillObj.transform, "Label", "🛠️ MFP 设计工坊", 11, TextAnchor.MiddleCenter, Color.white);
            var spRt = _selectionPillText.GetComponent<RectTransform>();
            spRt.anchorMin = Vector2.zero;
            spRt.anchorMax = Vector2.one;
            spRt.sizeDelta = Vector2.zero;

            // 历史控制
            _undoBtn = CreateToolbarButton(_topBarObj.transform, "UndoBtn", "↶ 撤销", new Vector2(50f, 32f), () => WidgetEditHistory.Undo());
            _redoBtn = CreateToolbarButton(_topBarObj.transform, "RedoBtn", "↷ 重做", new Vector2(50f, 32f), () => WidgetEditHistory.Redo());

            CreateDivider(_topBarObj.transform);

            // 对齐工具群
            var alignList = new List<Button>();
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignLeft", "⬅ 左", new Vector2(42f, 32f), () => WidgetSelectionManager.AlignLeft()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignCenterX", "⏸ 中X", new Vector2(46f, 32f), () => WidgetSelectionManager.AlignCenterX()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignRight", "➡ 右", new Vector2(42f, 32f), () => WidgetSelectionManager.AlignRight()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignTop", "⬆ 顶", new Vector2(42f, 32f), () => WidgetSelectionManager.AlignTop()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignCenterY", "⏵ 中Y", new Vector2(46f, 32f), () => WidgetSelectionManager.AlignCenterY()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "AlignBottom", "⬇ 底", new Vector2(42f, 32f), () => WidgetSelectionManager.AlignBottom()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "DistH", "⇹ 等距H", new Vector2(56f, 32f), () => WidgetSelectionManager.DistributeHorizontally()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "DistV", "⇳ 等距V", new Vector2(56f, 32f), () => WidgetSelectionManager.DistributeVertically()));
            alignList.Add(CreateToolbarButton(_topBarObj.transform, "CenterScreen", "⌖ 中轴", new Vector2(50f, 32f), () => WidgetSelectionManager.CenterToScreenX()));
            _alignBtns = alignList.ToArray();

            CreateDivider(_topBarObj.transform);

            // 画布与分享
            CreateToolbarButton(_topBarObj.transform, "ArtboardBtn", "🎨 画板", new Vector2(54f, 32f), () => WidgetLayoutManager.CreateArtboard(false), new Color(0.12f, 0.40f, 0.35f, 0.9f));
            CreateToolbarButton(_topBarObj.transform, "ShareBtn", "📋 分享", new Vector2(50f, 32f), () =>
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    MFPToastBridge.Show(I18n.Tr("PRF_TOAST_SHARE_COPIED", "✔ 已成功复制分享码至剪贴板！"));
                }
            });
            CreateToolbarButton(_topBarObj.transform, "ImportBtn", "📥 导入", new Vector2(50f, 32f), () =>
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip) && LayoutShareHub.TryImportShareCode(clip, out var imported, out _))
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.ApplyLayout(imported);
                    FlightHUDManager.Instance?.RebuildHUD();
                    MFPToastBridge.Show(I18n.TrFormat("PRF_TOAST_IMPORT_OK", imported.Widgets.Count));
                }
                else
                {
                    SettingsGUI.Instance?.OpenToTab(0);
                }
            });

            CreateDivider(_topBarObj.transform);

            // 辅助开关 (磁吸/网格/图层/模组坞)
            var snapBtn = CreateToolbarButton(_topBarObj.transform, "SnapBtn", "🧲 磁吸", new Vector2(62f, 32f), () =>
            {
                WidgetDragHandler.EnableMagneticSnap = !WidgetDragHandler.EnableMagneticSnap;
                RefreshTopBarStates();
            });
            _snapBtnText = snapBtn.GetComponentInChildren<Text>();

            var gridBtn = CreateToolbarButton(_topBarObj.transform, "GridBtn", "▦ 网格", new Vector2(62f, 32f), () =>
            {
                WidgetCanvasGrid.ToggleGrid();
                RefreshTopBarStates();
            });
            _gridBtnText = gridBtn.GetComponentInChildren<Text>();

            var layerBtn = CreateToolbarButton(_topBarObj.transform, "LayerBtn", "📑 图层", new Vector2(62f, 32f), () =>
            {
                WidgetLayerManager.ToggleLayerPanel();
                RefreshTopBarStates();
            });
            _layerBtnText = layerBtn.GetComponentInChildren<Text>();

            var modDockBtn = CreateToolbarButton(_topBarObj.transform, "ModDockBtn", "★ 模组", new Vector2(62f, 32f), () =>
            {
                _isFavoriteModDockOpen = !_isFavoriteModDockOpen;
                if (_isFavoriteModDockOpen)
                {
                    ThemeManager.Instance?.EnsureDockRulesPopulated();
                }
                RefreshTopBarStates();
            });
            _modDockBtnText = modDockBtn.GetComponentInChildren<Text>();

            CreateDivider(_topBarObj.transform);

            // 快捷退出编辑模式
            CreateToolbarButton(_topBarObj.transform, "ExitBtn", "✔ 退出编辑", new Vector2(88f, 32f), () =>
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
                if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && SettingsGUI.Instance.IsCanvasLayoutMode)
                {
                    SettingsGUI.Instance.ToggleWindow();
                }
            }, new Color(0.15f, 0.48f, 0.25f, 0.95f));
        }
        #endregion

        #region Contextual Inspector 构建
        private void BuildContextualInspector(RectTransform parent)
        {
            _inspectorCardObj = CreateGlassPanel(parent, "ContextualInspector", new Vector2(360f, 230f), Vector2.zero, new Vector2(0f, 1f), new Vector2(100f, 500f));
            _inspectorCardRt = _inspectorCardObj.GetComponent<RectTransform>();
            AddHoverCatcher(_inspectorCardObj);

            var vl = _inspectorCardObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(8, 8, 8, 8);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 标题行
            var headerRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            _inspectorTitleText = UIFactory.CreateText(headerRow.transform, "Title", "组件属性", 12, TextAnchor.MiddleLeft, Color.white);
            _inspectorTitleText.GetComponent<RectTransform>().sizeDelta = new Vector2(210f, 22f);

            _inspectorStatsText = UIFactory.CreateText(headerRow.transform, "Stats", "1.00x | 0°", 11, TextAnchor.MiddleRight, new Color(0f, 0.88f, 1f));
            _inspectorStatsText.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 22f);

            // 缩放控制行
            var scaleRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            UIFactory.CreateText(scaleRow.transform, "Lbl", "缩放:", 10, TextAnchor.MiddleLeft, new Color(0f, 0.88f, 1f)).GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 22f);
            CreateToolbarButton(scaleRow.transform, "S-", "－", new Vector2(28f, 20f), () => WidgetSelectionManager.BatchScale(-0.1f));
            CreateToolbarButton(scaleRow.transform, "S+", "＋", new Vector2(28f, 20f), () => WidgetSelectionManager.BatchScale(+0.1f));
            CreateToolbarButton(scaleRow.transform, "S08", "0.8x", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetScale(0.8f));
            CreateToolbarButton(scaleRow.transform, "S10", "1.0x", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetScale(1.0f));
            CreateToolbarButton(scaleRow.transform, "S12", "1.2x", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetScale(1.2f));
            CreateToolbarButton(scaleRow.transform, "S15", "1.5x", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetScale(1.5f));

            // 形变控制行
            var deformRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            UIFactory.CreateText(deformRow.transform, "Lbl", "形变:", 10, TextAnchor.MiddleLeft, new Color(0.65f, 0.55f, 0.98f)).GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 22f);
            CreateToolbarButton(deformRow.transform, "W-", "宽－", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(-0.1f, 0f));
            CreateToolbarButton(deformRow.transform, "W+", "宽＋", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(+0.1f, 0f));
            CreateToolbarButton(deformRow.transform, "H-", "高－", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(0f, -0.1f));
            CreateToolbarButton(deformRow.transform, "H+", "高＋", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(0f, +0.1f));
            CreateToolbarButton(deformRow.transform, "R11", "1:1", new Vector2(32f, 20f), () => WidgetSelectionManager.BatchResetAspectRatio());
            CreateToolbarButton(deformRow.transform, "R169", "16:9", new Vector2(40f, 20f), () => WidgetSelectionManager.BatchSetAspectRatio(16f / 9f));

            // 透明度控制行
            var opRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            UIFactory.CreateText(opRow.transform, "Lbl", "透明:", 10, TextAnchor.MiddleLeft, new Color(0.22f, 0.74f, 0.97f)).GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 22f);
            CreateToolbarButton(opRow.transform, "O-", "－", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchAdjustOpacity(-0.1f));
            CreateToolbarButton(opRow.transform, "O+", "＋", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchAdjustOpacity(+0.1f));
            CreateToolbarButton(opRow.transform, "O40", "40%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.40f));
            CreateToolbarButton(opRow.transform, "O60", "60%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.60f));
            CreateToolbarButton(opRow.transform, "O80", "80%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.80f));
            CreateToolbarButton(opRow.transform, "O100", "100%", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetOpacity(1.0f));
            _inspectorOpacityText = UIFactory.CreateText(opRow.transform, "OpVal", "100%", 10, TextAnchor.MiddleRight, new Color(0.22f, 0.74f, 0.97f));
            _inspectorOpacityText.GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 20f);

            // 旋转控制行
            var rotRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            UIFactory.CreateText(rotRow.transform, "Lbl", "旋转:", 10, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0f)).GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 22f);
            CreateToolbarButton(rotRow.transform, "R-15", "↺ 15°", new Vector2(46f, 20f), () => WidgetSelectionManager.BatchRotate(-15f));
            CreateToolbarButton(rotRow.transform, "R0", "0°", new Vector2(32f, 20f), () => WidgetSelectionManager.ResetRotation());
            CreateToolbarButton(rotRow.transform, "R+15", "↻ 15°", new Vector2(46f, 20f), () => WidgetSelectionManager.BatchRotate(+15f));
            CreateToolbarButton(rotRow.transform, "R90", "90°", new Vector2(36f, 20f), () => WidgetSelectionManager.BatchSetRotation(90f));
            CreateToolbarButton(rotRow.transform, "R180", "180°", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetRotation(180f));
            CreateToolbarButton(rotRow.transform, "RCX", "⤢ 居中", new Vector2(48f, 20f), () => WidgetSelectionManager.CenterToScreenX());

            // 图层与快捷动作行
            var actRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            UIFactory.CreateText(actRow.transform, "Lbl", "图层:", 10, TextAnchor.MiddleLeft, new Color(0.2f, 0.8f, 0.6f)).GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 22f);
            CreateToolbarButton(actRow.transform, "LFront", "⤒", new Vector2(26f, 20f), () => WidgetLayerManager.BringToFront(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LFwd", "▲", new Vector2(26f, 20f), () => WidgetLayerManager.BringForward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LBack", "▼", new Vector2(26f, 20f), () => WidgetLayerManager.SendBackward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LBottom", "⤓", new Vector2(26f, 20f), () => WidgetLayerManager.SendToBack(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "CloneBtn", "📑 克隆", new Vector2(52f, 20f), () => WidgetClipboardManager.DuplicateSelected());
            CreateToolbarButton(actRow.transform, "DelBtn", "🗑 隐藏", new Vector2(52f, 20f), () => WidgetSelectionManager.DeleteSelected(), new Color(0.45f, 0.12f, 0.15f, 0.9f));
            CreateToolbarButton(actRow.transform, "Deselect", "取消", new Vector2(42f, 20f), () => WidgetSelectionManager.ClearSelection());

            _inspectorCardObj.SetActive(false);
        }
        #endregion

        #region LayerDrawer 构建
        private void BuildLayerDrawer(RectTransform parent)
        {
            _layerDrawerObj = CreateGlassPanel(parent, "LayerDrawer", new Vector2(260f, 480f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f));
            AddHoverCatcher(_layerDrawerObj);

            var vl = _layerDrawerObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 4f;
            vl.padding = new RectOffset(8, 8, 8, 8);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 抽屉头部
            var header = CreateRowLayout(_layerDrawerObj.transform, 26f);
            var title = UIFactory.CreateText(header.transform, "Header", "📑 航电图层管理", 12, TextAnchor.MiddleLeft, Color.white);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 26f);
            CreateToolbarButton(header.transform, "CloseBtn", "✕", new Vector2(26f, 24f), () =>
            {
                WidgetLayerManager.IsLayerPanelOpen = false;
                RefreshTopBarStates();
            });

            // 滚动列表视口
            var scrollObj = new GameObject("ScrollArea", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollObj.transform.SetParent(_layerDrawerObj.transform, false);
            var sRt = scrollObj.GetComponent<RectTransform>();
            sRt.sizeDelta = new Vector2(244f, 430f);

            var sImg = scrollObj.GetComponent<Image>();
            sImg.color = new Color(0.04f, 0.06f, 0.10f, 0.4f);

            var sMask = scrollObj.GetComponent<Mask>();
            sMask.showMaskGraphic = false;

            var contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(scrollObj.transform, false);
            var cRt = contentObj.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0f, 1f);
            cRt.sizeDelta = new Vector2(0f, 0f);

            var cvl = contentObj.GetComponent<VerticalLayoutGroup>();
            cvl.childAlignment = TextAnchor.UpperLeft;
            cvl.spacing = 2f;
            cvl.childForceExpandWidth = true;
            cvl.childForceExpandHeight = false;

            var csf = contentObj.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.GetComponent<ScrollRect>();
            scroll.content = cRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _layerContentTransform = contentObj.transform;
            _layerDrawerObj.SetActive(false);
        }
        #endregion

        #region FavoriteModDock 构建
        private void BuildFavoriteModDock(RectTransform parent)
        {
            _modDockObj = CreateGlassPanel(parent, "FavoriteModDock", new Vector2(1240f, 38f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f));
            AddHoverCatcher(_modDockObj);

            var hl = _modDockObj.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 6f;
            hl.padding = new RectOffset(10, 10, 4, 4);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            var title = UIFactory.CreateText(_modDockObj.transform, "Header", "★ 常用模组:", 11, TextAnchor.MiddleLeft, new Color(1f, 0.88f, 0f));
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 26f);

            var listContainer = new GameObject("ModContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            listContainer.transform.SetParent(_modDockObj.transform, false);
            var lcRt = listContainer.GetComponent<RectTransform>();
            lcRt.sizeDelta = new Vector2(1060f, 30f);

            var lchl = listContainer.GetComponent<HorizontalLayoutGroup>();
            lchl.childAlignment = TextAnchor.MiddleLeft;
            lchl.spacing = 4f;
            lchl.childForceExpandWidth = false;
            lchl.childForceExpandHeight = false;

            _modDockContentTransform = listContainer.transform;

            CreateToolbarButton(_modDockObj.transform, "CloseModDock", "✕", new Vector2(26f, 26f), () =>
            {
                _isFavoriteModDockOpen = false;
                RefreshTopBarStates();
            });

            _modDockObj.SetActive(false);
        }
        #endregion

        #region 动态交互更新 (Update Loop)
        private void Update()
        {
            if (!WidgetDragHandler.IsEditModeActive || MFPProfiler.IsMasterBypassed)
            {
                if (_rootObj != null && _rootObj.activeSelf) _rootObj.SetActive(false);
                return;
            }

            if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && !SettingsGUI.Instance.IsCanvasLayoutMode)
            {
                if (_rootObj != null && _rootObj.activeSelf) _rootObj.SetActive(false);
                return;
            }

            if (_rootObj != null && !_rootObj.activeSelf)
            {
                _rootObj.SetActive(true);
            }

            // 节流状态检测
            int selCount = WidgetSelectionManager.Count;
            if (selCount != _cachedSelectedCount || Time.unscaledTime - _lastUiRefreshTime > 0.1f)
            {
                _cachedSelectedCount = selCount;
                _lastUiRefreshTime = Time.unscaledTime;
                RefreshAllVisuals(false);
            }

            // 更新即时属性浮动盒的位置 (跟随当前选中组件)
            UpdateInspectorPosition();
        }

        private void RefreshAllVisuals(bool forceRebuildLists)
        {
            RefreshTopBarStates();

            // 浮动面板显隐
            int selCount = WidgetSelectionManager.Count;
            if (_inspectorCardObj != null)
            {
                bool showInspector = selCount > 0;
                if (_inspectorCardObj.activeSelf != showInspector)
                {
                    _inspectorCardObj.SetActive(showInspector);
                }

                if (showInspector)
                {
                    BaseFlightWidget primary = GetPrimarySelectedWidget();
                    if (primary != null)
                    {
                        float sc = primary.Config?.Scale ?? 1f;
                        float rot = primary.Config?.Rotation ?? 0f;
                        float op = primary.Opacity;
                        string prefix = selCount > 1 ? $"[已选 {selCount} 项] " : "";
                        if (_inspectorTitleText != null) _inspectorTitleText.text = $"<b>{prefix}{primary.DisplayName}</b>";
                        if (_inspectorStatsText != null) _inspectorStatsText.text = $"{sc:F2}x | {rot:F0}°";
                        if (_inspectorOpacityText != null) _inspectorOpacityText.text = $"{Mathf.RoundToInt(op * 100f)}%";
                    }
                }
            }

            // 图层面板显隐与内容刷新
            if (_layerDrawerObj != null)
            {
                bool showLayer = WidgetLayerManager.IsLayerPanelOpen;
                if (_layerDrawerObj.activeSelf != showLayer)
                {
                    _layerDrawerObj.SetActive(showLayer);
                }
                if (showLayer && (forceRebuildLists || Time.frameCount % 30 == 0))
                {
                    PopulateLayerDrawer();
                }
            }

            // 模组坞显隐与内容刷新
            if (_modDockObj != null)
            {
                bool showModDock = _isFavoriteModDockOpen;
                if (_modDockObj.activeSelf != showModDock)
                {
                    _modDockObj.SetActive(showModDock);
                }
                if (showModDock && forceRebuildLists)
                {
                    PopulateFavoriteModDock();
                }
            }
        }

        private void RefreshTopBarStates()
        {
            int selCount = WidgetSelectionManager.Count;

            if (_selectionPillText != null)
            {
                _selectionPillText.text = selCount > 0
                    ? $"🛠️ MFP 工坊 | <b>已选 {selCount} 项</b>"
                    : "🛠️ MFP 工坊 | <b>未选中</b>";
            }

            if (_undoBtn != null) _undoBtn.interactable = WidgetEditHistory.CanUndo;
            if (_redoBtn != null) _redoBtn.interactable = WidgetEditHistory.CanRedo;

            if (_alignBtns != null)
            {
                for (int i = 0; i < _alignBtns.Length; i++)
                {
                    if (_alignBtns[i] != null)
                    {
                        // 0~5 对齐需要 >= 2 项，6~7 等距需要 >= 3 项，8 居中需要 >= 1 项
                        if (i < 6) _alignBtns[i].interactable = selCount >= 2;
                        else if (i < 8) _alignBtns[i].interactable = selCount >= 3;
                        else _alignBtns[i].interactable = selCount >= 1;
                    }
                }
            }

            if (_snapBtnText != null)
            {
                bool snap = WidgetDragHandler.EnableMagneticSnap;
                _snapBtnText.text = snap ? "<color=#00E5FF>🧲 磁吸[开]</color>" : "🧲 磁吸[关]";
            }

            if (_gridBtnText != null)
            {
                bool grid = WidgetCanvasGrid.IsGridVisible;
                _gridBtnText.text = grid ? "<color=#00E5FF>▦ 网格[开]</color>" : "▦ 网格[关]";
            }

            if (_layerBtnText != null)
            {
                bool layer = WidgetLayerManager.IsLayerPanelOpen;
                _layerBtnText.text = layer ? "<color=#00E5FF>📑 图层[开]</color>" : "📑 图层[关]";
            }

            if (_modDockBtnText != null)
            {
                _modDockBtnText.text = _isFavoriteModDockOpen ? "<color=#FFE000>★ 模组[开]</color>" : "★ 模组[关]";
            }
        }

        private void UpdateInspectorPosition()
        {
            if (_inspectorCardRt == null || !_inspectorCardObj.activeSelf) return;

            BaseFlightWidget primary = GetPrimarySelectedWidget();
            if (primary == null || primary.RectTransform == null) return;

            Vector3[] corners = new Vector3[4];
            primary.RectTransform.GetWorldCorners(corners);

            Canvas canvas = _hudManager != null ? _hudManager.Canvas : GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 p1 = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            Vector2 p3 = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            float badgeW = 360f;
            float badgeH = 230f;

            float bx = maxX + 12f;
            float by = maxY;

            if (bx + badgeW > Screen.width - 12f)
            {
                bx = minX - badgeW - 12f;
            }
            if (bx < 12f)
            {
                bx = Mathf.Clamp(minX, 12f, Screen.width - badgeW - 12f);
                by = maxY - 12f;
            }

            bx = Mathf.Clamp(bx, 12f, Screen.width - badgeW - 12f);
            by = Mathf.Clamp(by, badgeH + 12f, Screen.height - 12f);

            _inspectorCardRt.anchoredPosition = new Vector2(bx, by);
        }

        private BaseFlightWidget GetPrimarySelectedWidget()
        {
            foreach (var w in WidgetSelectionManager.SelectedWidgets)
            {
                if (w != null && w.RectTransform != null) return w;
            }
            return null;
        }

        private void PopulateLayerDrawer()
        {
            if (_layerContentTransform == null) return;

            var widgets = FlightHUDManager.Instance?.ModularWidgets;
            if (widgets == null) return;

            // 倒序排列：顶层组件在最上方
            var sorted = new List<BaseFlightWidget>(widgets);
            sorted.Sort((a, b) =>
            {
                int orderA = a.Config?.DrawOrder ?? 0;
                int orderB = b.Config?.DrawOrder ?? 0;
                return orderB.CompareTo(orderA);
            });

            // 隐藏复用池中超量对象
            for (int i = sorted.Count; i < _layerRowPool.Count; i++)
            {
                _layerRowPool[i].SetActive(false);
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                var w = sorted[i];
                GameObject rowObj;
                if (i < _layerRowPool.Count)
                {
                    rowObj = _layerRowPool[i];
                    rowObj.SetActive(true);
                }
                else
                {
                    rowObj = CreateRowLayout(_layerContentTransform, 26f);
                    rowObj.name = $"LayerRow_{i}";
                    _layerRowPool.Add(rowObj);
                }

                // 清除旧子节点
                for (int c = rowObj.transform.childCount - 1; c >= 0; c--)
                {
                    Destroy(rowObj.transform.GetChild(c).gameObject);
                }

                bool isVis = w.Config?.IsEnabled ?? true;
                bool isLock = w.Config?.IsLocked ?? false;
                bool isSel = WidgetSelectionManager.IsSelected(w);

                // 显隐开关
                CreateToolbarButton(rowObj.transform, "Vis", isVis ? "●" : "○", new Vector2(22f, 22f), () =>
                {
                    if (w.Config != null)
                    {
                        w.Config.IsEnabled = !w.Config.IsEnabled;
                        w.SetVisible(w.Config.IsEnabled);
                        WidgetLayoutManager.Instance.SaveLayout();
                    }
                }, isVis ? new Color(0.1f, 0.4f, 0.2f) : new Color(0.2f, 0.2f, 0.2f), isVis ? Color.green : Color.gray);

                // 锁定开关
                CreateToolbarButton(rowObj.transform, "Lock", isLock ? "🔒" : "🔓", new Vector2(22f, 22f), () =>
                {
                    WidgetLayerManager.ToggleLock(w);
                });

                // 组件选择按钮
                string nameLabel = !string.IsNullOrEmpty(w.DisplayName) ? w.DisplayName : w.WidgetId;
                Color nameCol = isSel ? new Color(0f, 0.88f, 1f) : Color.white;
                CreateToolbarButton(rowObj.transform, "Select", nameLabel, new Vector2(130f, 22f), () =>
                {
                    WidgetSelectionManager.Select(w, false);
                }, isSel ? new Color(0.12f, 0.35f, 0.5f, 0.9f) : new Color(0.08f, 0.12f, 0.18f, 0.8f), nameCol, 10);

                // 图层升降
                CreateToolbarButton(rowObj.transform, "Up", "▲", new Vector2(22f, 22f), () => WidgetLayerManager.BringForward(new[] { w }));
                CreateToolbarButton(rowObj.transform, "Down", "▼", new Vector2(22f, 22f), () => WidgetLayerManager.SendBackward(new[] { w }));
            }
        }

        private void PopulateFavoriteModDock()
        {
            if (_modDockContentTransform == null) return;

            var rules = ThemeManager.Instance?.DockRules;
            var favList = new List<DockButtonRule>();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i].IsFavorite) favList.Add(rules[i]);
                }
            }

            for (int i = favList.Count; i < _modDockBtnPool.Count; i++)
            {
                _modDockBtnPool[i].SetActive(false);
            }

            for (int i = 0; i < favList.Count; i++)
            {
                var rule = favList[i];
                GameObject btnObj;
                if (i < _modDockBtnPool.Count)
                {
                    btnObj = _modDockBtnPool[i];
                    btnObj.SetActive(true);
                }
                else
                {
                    btnObj = CreateToolbarButton(_modDockContentTransform, $"ModBtn_{i}", "", new Vector2(76f, 26f), null).gameObject;
                    _modDockBtnPool.Add(btnObj);
                }

                string label = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;
                if (string.IsNullOrEmpty(label)) label = rule.Key;

                var btnText = btnObj.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = label;

                var btn = btnObj.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                var curRule = rule;
                btn.onClick.AddListener(() =>
                {
                    MFPToastBridge.Show(string.Format(I18n.Tr("UI_TOAST_MOD_TRIGGERED", "✔ 已触发 [{0}]"), curRule.DisplayName));
                });
            }
        }
        #endregion

        #region UGUI 工具辅助方法
        private GameObject CreateGlassPanel(Transform parent, string name, Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            Material glassMat = GetOrCreateGlassMaterial();
            if (glassMat != null)
            {
                img.material = glassMat;
            }
            img.color = new Color(0.06f, 0.09f, 0.14f, 0.94f);
            img.raycastTarget = true;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0.88f, 1f, 0.35f);
            outline.effectDistance = new Vector2(1f, -1f);

            return go;
        }

        private static GameObject CreateRowLayout(Transform parent, float height)
        {
            GameObject go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, height);

            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 3f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            return go;
        }

        private static Button CreateToolbarButton(Transform parent, string name, string label, Vector2 size, UnityAction onClick, Color? bgColor = null, Color? textColor = null, int fontSize = 11)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = bgColor ?? new Color(0.12f, 0.17f, 0.25f, 0.92f);
            img.raycastTarget = true;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.40f, 0.60f, 0.60f);
            outline.effectDistance = new Vector2(1f, -1f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.2f, 0.8f, 1f, 1f);
            colors.pressedColor = new Color(0.0f, 0.6f, 0.9f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            btn.colors = colors;

            if (onClick != null) btn.onClick.AddListener(onClick);

            var txtObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(go.transform, false);

            var txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            txtRt.offsetMin = new Vector2(2f, 0f);
            txtRt.offsetMax = new Vector2(-2f, 0f);

            var txt = txtObj.GetComponent<Text>();
            txt.font = UIFactory.GetActiveFont();
            txt.text = label;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = textColor ?? Color.white;
            txt.raycastTarget = false;

            return btn;
        }

        private static void CreateDivider(Transform parent)
        {
            GameObject div = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            div.transform.SetParent(parent, false);
            var rt = div.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1f, 22f);
            div.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.50f, 0.5f);
        }

        private static void AddHoverCatcher(GameObject go)
        {
            var catcher = go.AddComponent<HoverCatcher>();
            catcher.OnHoverChanged = isHover =>
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = isHover;
                MFPInputLock.SetWindowHoverLock(isHover);
            };
        }

        private class HoverCatcher : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action<bool> OnHoverChanged;
            public void OnPointerEnter(PointerEventData eventData) => OnHoverChanged?.Invoke(true);
            public void OnPointerExit(PointerEventData eventData) => OnHoverChanged?.Invoke(false);
        }
        #endregion
    }
}
