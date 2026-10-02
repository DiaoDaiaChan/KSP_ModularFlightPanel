using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Workbench;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 图形化编辑模式悬浮工具栏 (HUD Edit Mode Toolbar & Action Controller)
    /// 核心架构职责：
    /// 1. 采用新一代 GPU 现代暗晶玻璃着色器 (ModernWorkbenchGlass.shader) 与 WorkbenchStyleEngine 材质管线；
    /// 2. 100% 消除 Unity IMGUI OnGUI 引擎轮询开销与内存垃圾 (0 GC 运行)；
    /// 3. 完整复刻并现代化的交互体系：
    ///    - 顶部编辑主控制器 (TopBar): 撤销/重做、9项精准对齐、画板新建/分享/导入、磁吸/网格/图层/模组坞开关、图层/形变/旋转/隐藏、一键退出；
    ///    - 现场即时浮动属性盒 (Contextual Inspector): 紧密跟随选中组件几何边界，极速调节缩放、长宽比形变、透明度、配色主题、旋转角、图层深度与锁定；
    ///    - 深度属性扩展抽屉 (Inspector Drawer): 微控件逐项开关/微调步进、遥测参数装配与实时采样、6通道通配符绑定与动态槽位编排；
    ///    - 专业级图层管理器抽屉 (Layer Drawer): Photoshop 倒序图层堆叠视图、显隐锁定控制、点击穿透选择与快捷层级跃迁；
    ///    - 常用模组快捷坞 (Favorite Mod Quick Dock): 顶部收纳集成、一键启动第三方 Mod 界面与智能推荐。
    /// </summary>
    public class HUDEditModeToolbar : MonoBehaviour
    {
        private FlightHUDManager _hudManager;

        // 全局微控件定制入口标记 (供 WidgetDragHandler 引用)
        public static bool IsSubControlCustomizerOpen { get; set; } = false;

        private static bool _isFavoriteModDockOpen = false;
        private static bool _isLayerDrawerOpen = false;
        private static bool _isInspectorDrawerOpen = false;
        private static InspectorTab _activeInspectorTab = InspectorTab.MicroControls;

        public enum InspectorTab
        {
            MicroControls,
            Telemetry,
            Channels
        }

        // 根节点与画布组件
        private GameObject _rootObj;
        private CanvasGroup _rootCanvasGroup;
        private static readonly string[] CycleThemeIds = new[] { "", "boeing_787", "spacex_dragon", "cyber_neon", "diffractive_hud", "vintage_amber", "starship_mars", "sr71_blackbird" };

        // 顶部工具栏 (TopBar)
        private GameObject _topBarObj;
        private Text _selectionPillText;
        private Button _undoBtn;
        private Button _redoBtn;
        private Button[] _alignBtns;
        private Text _snapBtnText;
        private Image _snapBtnImg;
        private Text _gridBtnText;
        private Image _gridBtnImg;
        private Text _layerBtnText;
        private Image _layerBtnImg;
        private Text _modDockBtnText;
        private Image _modDockBtnImg;

        // 顶部动态动作区 (选中有组件 vs 未选中全选/提示)
        private GameObject _topSelActionsObj;
        private GameObject _topNoSelHintsObj;

        // 旁侧即时属性浮动盒 (Contextual Inspector)
        private GameObject _inspectorCardObj;
        private RectTransform _inspectorCardRt;
        private Text _inspectorTitleText;
        private Text _inspectorStatsText;
        private Text _inspectorOpacityText;
        private Text _inspectorLayerText;
        private Text _inspectorLockBtnText;
        private Text _microCtrlBtnText;
        private Text _telemBtnText;
        private GameObject _artboardStudioBtnObj;

        // 深度属性扩展抽屉 (Inspector Drawer)
        private GameObject _inspectorDrawerObj;
        private RectTransform _inspectorDrawerRt;
        private Text _drawerTitleText;
        private Button _drawerTabMicroBtn;
        private Button _drawerTabTelemBtn;
        private Button _drawerTabChanBtn;
        private GameObject _drawerMicroPanel;
        private GameObject _drawerTelemPanel;
        private GameObject _drawerChanPanel;
        private Transform _drawerMicroContent;
        private Transform _drawerTelemContent;
        private Transform _drawerChanContent;

        // 浮动图层管理器抽屉 (Layer Drawer)
        private GameObject _layerDrawerObj;
        private RectTransform _layerDrawerRt;
        private Text _layerDrawerTitleText;
        private Transform _layerDrawerContent;

        // 常用模组折叠坞 (Favorite Mod Dock)
        private GameObject _modDockObj;
        private RectTransform _modDockRt;
        private Transform _modDockContent;
        private GameObject _modDockEmptyHint;

        // 对象池缓存
        private readonly List<MicroControlRowUI> _microRowsPool = new List<MicroControlRowUI>();
        private readonly List<TelemetryBindRowUI> _telemRowsPool = new List<TelemetryBindRowUI>();
        private readonly List<ChannelRowUI> _chanRowsPool = new List<ChannelRowUI>();
        private readonly List<LayerRowUI> _layerRowsPool = new List<LayerRowUI>();
        private readonly List<GameObject> _modDockBtnPool = new List<GameObject>();

        // 刷新与状态追踪节流
        private float _lastUiRefreshTime = 0f;
        private int _cachedSelectedCount = -1;
        private BaseFlightWidget _cachedPrimaryWidget = null;
        private static float _lastEvalTime = 0f;
        private static readonly Dictionary<string, string> _channelEvalCache = new Dictionary<string, string>();

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
            RefreshAllStates(true);
        }

        private void OnDisable()
        {
            if (_rootObj != null) _rootObj.SetActive(false);
            FlightHUDManager.IsMouseOverFloatingToolbar = false;
            MFPInputLock.SetWindowHoverLock(false);
            IsSubControlCustomizerOpen = false;
        }

        private void OnDestroy()
        {
            if (_rootObj != null)
            {
                Destroy(_rootObj);
                _rootObj = null;
            }
        }

        private void Update()
        {
            if (!WidgetDragHandler.IsEditModeActive || MFPProfiler.IsMasterBypassed)
            {
                if (_rootObj != null && _rootObj.activeSelf) _rootObj.SetActive(false);
                return;
            }

            // 当全屏航电工坊工作台打开且未处于画布排版模式时，静默挂起 HUD 现场悬浮编辑栏与抽屉
            if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && !SettingsGUI.Instance.IsCanvasLayoutMode)
            {
                if (_rootObj != null && _rootObj.activeSelf) _rootObj.SetActive(false);
                return;
            }

            if (_rootObj != null && !_rootObj.activeSelf) _rootObj.SetActive(true);

            // 监听键盘快捷键 L 键切换图层抽屉
            if (Input.GetKeyDown(KeyCode.L))
            {
                _isLayerDrawerOpen = !_isLayerDrawerOpen;
                WidgetLayerManager.IsLayerPanelOpen = _isLayerDrawerOpen;
                if (_layerDrawerObj != null) _layerDrawerObj.SetActive(_isLayerDrawerOpen);
                RefreshTopBarStates();
            }

            // 追踪上下文检查器位置 (每帧平滑贴合组件)
            UpdateContextualInspectorPosition();

            // 定频 20Hz 刷新界面内容与交互状态
            _lastUiRefreshTime += Time.unscaledDeltaTime;
            if (_lastUiRefreshTime >= 0.05f)
            {
                _lastUiRefreshTime = 0f;
                RefreshAllStates(false);
            }
        }

        #region UGUI 层次构建
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
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = Vector2.zero;
            rootRt.anchoredPosition = Vector2.zero;

            _rootCanvasGroup = _rootObj.GetComponent<CanvasGroup>();

            // 2. 构建顶部主工具栏 (TopBar)
            BuildTopBar(rootRt);

            // 3. 构建常用模组抽屉 (Favorite Mod Dock)
            BuildFavoriteModDock(rootRt);

            // 4. 构建组件即时浮动属性盒 (Contextual Inspector)
            BuildContextualInspector(rootRt);

            // 5. 构建深度属性扩展抽屉 (Inspector Drawer)
            BuildInspectorDrawer(rootRt);

            // 6. 构建右侧图层管理器抽屉 (Layer Drawer)
            BuildLayerDrawer(rootRt);
        }

        private void BuildTopBar(RectTransform parent)
        {
            _topBarObj = CreateGlassPanel(parent, "TopBar", new Vector2(1260f, 68f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), false);
            AddHoverCatcher(_topBarObj);

            var vl = _topBarObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.spacing = 3f;
            vl.padding = new RectOffset(10, 10, 5, 5);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // ── 第一行：标题 + 撤销/重做 + 9项对齐 + 画板/分享/导入 ──
            GameObject row1 = CreateRowLayout(_topBarObj.transform, 28f);

            // 标题胶囊药丸
            GameObject pillObj = CreatePill(row1.transform, "SelectionPill", I18n.Tr("HUD_STUDIO_HEADER", "🛠️ MFP 设计工坊"), WorkbenchStyleEngine.ColorAccentPrimary, 11);
            _selectionPillText = pillObj.GetComponentInChildren<Text>();
            var pillLe = pillObj.GetComponent<LayoutElement>();
            pillLe.minWidth = 175f;
            pillLe.preferredWidth = 175f;

            // 历史控制
            _undoBtn = CreateToolbarButton(row1.transform, "UndoBtn", I18n.Tr("HUD_UNDO", "↶ 撤销"), new Vector2(52f, 26f), () => WidgetEditHistory.Undo());
            _redoBtn = CreateToolbarButton(row1.transform, "RedoBtn", I18n.Tr("HUD_REDO", "↷ 重做"), new Vector2(52f, 26f), () => WidgetEditHistory.Redo());

            CreateDivider(row1.transform);

            // 9 项对齐工具
            var alignList = new List<Button>();
            alignList.Add(CreateToolbarButton(row1.transform, "AlignLeft", I18n.Tr("ASM_ALIGN_LEFT", "⬅ 左"), new Vector2(44f, 26f), () => WidgetSelectionManager.AlignLeft()));
            alignList.Add(CreateToolbarButton(row1.transform, "AlignCenterX", I18n.Tr("ASM_ALIGN_CENTER_H", "⏸ 中X"), new Vector2(48f, 26f), () => WidgetSelectionManager.AlignCenterX()));
            alignList.Add(CreateToolbarButton(row1.transform, "AlignRight", I18n.Tr("ASM_ALIGN_RIGHT", "➡ 右"), new Vector2(44f, 26f), () => WidgetSelectionManager.AlignRight()));
            alignList.Add(CreateToolbarButton(row1.transform, "AlignTop", I18n.Tr("ASM_ALIGN_TOP", "⬆ 顶"), new Vector2(44f, 26f), () => WidgetSelectionManager.AlignTop()));
            alignList.Add(CreateToolbarButton(row1.transform, "AlignCenterY", I18n.Tr("ASM_ALIGN_CENTER_V", "⏵ 中Y"), new Vector2(48f, 26f), () => WidgetSelectionManager.AlignCenterY()));
            alignList.Add(CreateToolbarButton(row1.transform, "AlignBottom", I18n.Tr("ASM_ALIGN_BOTTOM", "⬇ 底"), new Vector2(44f, 26f), () => WidgetSelectionManager.AlignBottom()));
            alignList.Add(CreateToolbarButton(row1.transform, "DistH", I18n.Tr("HIST_DISTRIBUTE_H", "⇹ 等距H"), new Vector2(58f, 26f), () => WidgetSelectionManager.DistributeHorizontally()));
            alignList.Add(CreateToolbarButton(row1.transform, "DistV", I18n.Tr("HIST_DISTRIBUTE_V", "⇳ 等距V"), new Vector2(58f, 26f), () => WidgetSelectionManager.DistributeVertically()));
            alignList.Add(CreateToolbarButton(row1.transform, "CenterScreen", I18n.Tr("HIST_ALIGN_AXIS_X0", "⌖ X=0"), new Vector2(52f, 26f), () => WidgetSelectionManager.CenterToScreenX()));
            _alignBtns = alignList.ToArray();

            CreateDivider(row1.transform);

            // 画板与配置分享
            CreateToolbarButton(row1.transform, "ArtboardBtn", I18n.Tr("HUD_BTN_NEW_ARTBOARD", "🎨 新建画板"), new Vector2(82f, 26f), () => WidgetLayoutManager.CreateArtboard(false), true);
            CreateToolbarButton(row1.transform, "ShareBtn", I18n.Tr("PRF_HEADER_SHARE", "📋 分享码"), new Vector2(64f, 26f), () =>
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    MFPToastBridge.Show(I18n.Tr("PRF_TOAST_SHARE_COPIED", "✔ 已成功复制分享码至剪贴板！"));
                }
            });
            CreateToolbarButton(row1.transform, "ImportBtn", "📥 导入", new Vector2(52f, 26f), () =>
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

            // ── 第二行：磁吸/网格/图层/模组坞 + 动态动作/全选提示 + 退出 ──
            GameObject row2 = CreateRowLayout(_topBarObj.transform, 26f);

            var snapBtn = CreateToolbarButton(row2.transform, "SnapBtn", "🧲 磁吸: [开]", new Vector2(88f, 24f), () =>
            {
                WidgetDragHandler.EnableMagneticSnap = !WidgetDragHandler.EnableMagneticSnap;
                RefreshTopBarStates();
            });
            _snapBtnText = snapBtn.GetComponentInChildren<Text>();
            _snapBtnImg = snapBtn.GetComponent<Image>();

            var gridBtn = CreateToolbarButton(row2.transform, "GridBtn", "▦ 网格: [关]", new Vector2(88f, 24f), () =>
            {
                WidgetCanvasGrid.ToggleGrid();
                RefreshTopBarStates();
            });
            _gridBtnText = gridBtn.GetComponentInChildren<Text>();
            _gridBtnImg = gridBtn.GetComponent<Image>();

            var layerBtn = CreateToolbarButton(row2.transform, "LayerBtn", "📑 图层: [关]", new Vector2(88f, 24f), () =>
            {
                _isLayerDrawerOpen = !_isLayerDrawerOpen;
                WidgetLayerManager.IsLayerPanelOpen = _isLayerDrawerOpen;
                if (_layerDrawerObj != null) _layerDrawerObj.SetActive(_isLayerDrawerOpen);
                RefreshTopBarStates();
            });
            _layerBtnText = layerBtn.GetComponentInChildren<Text>();
            _layerBtnImg = layerBtn.GetComponent<Image>();

            var modDockBtn = CreateToolbarButton(row2.transform, "ModDockBtn", "★ 常用模组", new Vector2(98f, 24f), () =>
            {
                _isFavoriteModDockOpen = !_isFavoriteModDockOpen;
                if (_isFavoriteModDockOpen)
                {
                    ThemeManager.Instance?.EnsureDockRulesPopulated();
                }
                if (_modDockObj != null) _modDockObj.SetActive(_isFavoriteModDockOpen);
                RefreshTopBarStates();
            });
            _modDockBtnText = modDockBtn.GetComponentInChildren<Text>();
            _modDockBtnImg = modDockBtn.GetComponent<Image>();

            CreateDivider(row2.transform);

            // 动态选择动作容器 (selCount > 0 时激活)
            _topSelActionsObj = CreateRowLayout(row2.transform, 24f);
            var saLe = _topSelActionsObj.AddComponent<LayoutElement>();
            saLe.flexibleWidth = 1f;

            CreateToolbarButton(_topSelActionsObj.transform, "BringFront", "⤒", new Vector2(24f, 24f), () => WidgetSelectionManager.BringToFront());
            CreateToolbarButton(_topSelActionsObj.transform, "BringFwd", "▲", new Vector2(24f, 24f), () => WidgetSelectionManager.BringForward());
            CreateToolbarButton(_topSelActionsObj.transform, "SendBack", "▼", new Vector2(24f, 24f), () => WidgetSelectionManager.SendBackward());
            CreateToolbarButton(_topSelActionsObj.transform, "SendBtm", "⤓", new Vector2(24f, 24f), () => WidgetSelectionManager.SendToBack());
            CreateToolbarButton(_topSelActionsObj.transform, "DelBtn", "🗑 隐藏", new Vector2(50f, 24f), () => WidgetSelectionManager.DeleteSelected(), false, WorkbenchStyleEngine.ColorDanger);

            CreateToolbarButton(_topSelActionsObj.transform, "ScaleDown", "－", new Vector2(24f, 24f), () => WidgetSelectionManager.BatchScale(-0.1f));
            CreateToolbarButton(_topSelActionsObj.transform, "ScaleUp", "＋", new Vector2(24f, 24f), () => WidgetSelectionManager.BatchScale(+0.1f));
            CreateToolbarButton(_topSelActionsObj.transform, "Scale10", "1.0x", new Vector2(40f, 24f), () => WidgetSelectionManager.BatchSetScale(1.0f));

            CreateToolbarButton(_topSelActionsObj.transform, "RotL", "↺ 15°", new Vector2(46f, 24f), () => WidgetSelectionManager.BatchRotate(-15f));
            CreateToolbarButton(_topSelActionsObj.transform, "Rot0", "0°", new Vector2(30f, 24f), () => WidgetSelectionManager.ResetRotation());
            CreateToolbarButton(_topSelActionsObj.transform, "RotR", "↻ 15°", new Vector2(46f, 24f), () => WidgetSelectionManager.BatchRotate(+15f));

            CreateToolbarButton(_topSelActionsObj.transform, "ClearSel", "取消选择", new Vector2(66f, 24f), () => WidgetSelectionManager.ClearSelection());

            // 未选中时的全选按钮与快捷键提示容器 (selCount == 0 时激活)
            _topNoSelHintsObj = CreateRowLayout(row2.transform, 24f);
            var nshLe = _topNoSelHintsObj.AddComponent<LayoutElement>();
            nshLe.flexibleWidth = 1f;

            CreateToolbarButton(_topNoSelHintsObj.transform, "SelectAllBtn", "全选 (Ctrl+A)", new Vector2(96f, 24f), () =>
            {
                if (_hudManager != null && _hudManager.ModularWidgets != null)
                {
                    WidgetSelectionManager.SelectAll(_hudManager.ModularWidgets);
                }
            });

            var hintTxt = CreateCrispText(_topNoSelHintsObj.transform, "HintLabel", "<color=#94A3B8><size=10>快捷键: 方向微调(Shift+10px) | 拖拽缩放/旋转 | Shift轴向 | Ctrl+Z撤销 | G网格 | L图层 | []层级</size></color>", 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            hintTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            CreateDivider(row2.transform);

            // 完成退出按钮
            CreateToolbarButton(row2.transform, "ExitBtn", "✔ 完成退出", new Vector2(88f, 24f), () =>
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
                if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && SettingsGUI.Instance.IsCanvasLayoutMode)
                {
                    SettingsGUI.Instance.ToggleWindow();
                }
            }, true);
        }
        #endregion

        #region Contextual Inspector 构建
        private void BuildContextualInspector(RectTransform parent)
        {
            _inspectorCardObj = CreateGlassPanel(parent, "ContextualInspector", new Vector2(375f, 242f), Vector2.zero, new Vector2(0f, 1f), new Vector2(100f, 500f), true);
            _inspectorCardRt = _inspectorCardObj.GetComponent<RectTransform>();
            AddHoverCatcher(_inspectorCardObj);

            var vl = _inspectorCardObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(8, 8, 6, 6);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 1. 标题行
            GameObject hRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            _inspectorTitleText = CreateCrispText(hRow.transform, "Title", "<b>组件属性</b>", 12, TextAnchor.MiddleLeft, Color.white);
            _inspectorTitleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _inspectorStatsText = CreateCrispText(hRow.transform, "Stats", "1.00x | 0°", 11, TextAnchor.MiddleRight, WorkbenchStyleEngine.ColorAccentPrimary);
            var statsLe = _inspectorStatsText.gameObject.AddComponent<LayoutElement>();
            statsLe.minWidth = 110f;
            statsLe.preferredWidth = 110f;

            // 2. 缩放控制行
            GameObject sRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            CreateLabel(sRow.transform, "缩放:", 36f, WorkbenchStyleEngine.ColorAccentPrimary);
            CreateToolbarButton(sRow.transform, "S-", "－", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchScale(-0.1f));
            CreateToolbarButton(sRow.transform, "S+", "＋", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchScale(+0.1f));
            CreateToolbarButton(sRow.transform, "S08", "0.8x", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetScale(0.8f));
            CreateToolbarButton(sRow.transform, "S10", "1.0x", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetScale(1.0f));
            CreateToolbarButton(sRow.transform, "S12", "1.2x", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetScale(1.2f));
            CreateToolbarButton(sRow.transform, "S15", "1.5x", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetScale(1.5f));

            // 3. 形变/长宽比控制行
            GameObject dRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            CreateLabel(dRow.transform, "形变:", 36f, new Color(0.65f, 0.55f, 0.98f));
            CreateToolbarButton(dRow.transform, "W-", "宽－", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(-0.1f, 0f));
            CreateToolbarButton(dRow.transform, "W+", "宽＋", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(+0.1f, 0f));
            CreateToolbarButton(dRow.transform, "H-", "高－", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(0f, -0.1f));
            CreateToolbarButton(dRow.transform, "H+", "高＋", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchAdjustScaleXY(0f, +0.1f));
            CreateToolbarButton(dRow.transform, "R11", "1:1", new Vector2(32f, 20f), () => WidgetSelectionManager.BatchResetAspectRatio());
            CreateToolbarButton(dRow.transform, "R43", "4:3", new Vector2(32f, 20f), () => WidgetSelectionManager.BatchSetAspectRatio(4f / 3f));
            CreateToolbarButton(dRow.transform, "R169", "16:9", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetAspectRatio(16f / 9f));
            CreateToolbarButton(dRow.transform, "R21", "2:1", new Vector2(32f, 20f), () => WidgetSelectionManager.BatchSetAspectRatio(2f));

            // 4. 透明度控制行
            GameObject opRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            CreateLabel(opRow.transform, "透明:", 36f, new Color(0.22f, 0.74f, 0.97f));
            CreateToolbarButton(opRow.transform, "O-", "－", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchAdjustOpacity(-0.1f));
            CreateToolbarButton(opRow.transform, "O+", "＋", new Vector2(26f, 20f), () => WidgetSelectionManager.BatchAdjustOpacity(+0.1f));
            CreateToolbarButton(opRow.transform, "O40", "40%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.40f));
            CreateToolbarButton(opRow.transform, "O60", "60%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.60f));
            CreateToolbarButton(opRow.transform, "O80", "80%", new Vector2(38f, 20f), () => WidgetSelectionManager.BatchSetOpacity(0.80f));
            CreateToolbarButton(opRow.transform, "O100", "100%", new Vector2(44f, 20f), () => WidgetSelectionManager.BatchSetOpacity(1.0f));
            _inspectorOpacityText = CreateCrispText(opRow.transform, "OpVal", "100%", 10, TextAnchor.MiddleRight, new Color(0.22f, 0.74f, 0.97f));
            var opLe = _inspectorOpacityText.gameObject.AddComponent<LayoutElement>();
            opLe.minWidth = 38f;
            opLe.preferredWidth = 38f;

            // 5. 配色风格控制行
            GameObject thRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            CreateLabel(thRow.transform, "配色:", 36f, new Color(0.20f, 0.85f, 0.60f));
            CreateToolbarButton(thRow.transform, "ThDef", I18n.Tr("HUD_BATCH_DEFAULT", "默认"), new Vector2(46f, 20f), () => WidgetSelectionManager.BatchSetThemeOverride(""));
            CreateToolbarButton(thRow.transform, "Th787", I18n.Tr("THEME_787_NAME", "787晶蓝"), new Vector2(56f, 20f), () => WidgetSelectionManager.BatchSetThemeOverride("boeing_787"));
            CreateToolbarButton(thRow.transform, "ThSpX", I18n.Tr("THEME_SPACEX", "SpaceX"), new Vector2(54f, 20f), () => WidgetSelectionManager.BatchSetThemeOverride("spacex_dragon"));
            CreateToolbarButton(thRow.transform, "ThCyb", I18n.Tr("THEME_CYBER_NAME", "赛博"), new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetThemeOverride("cyber_neon"));
            CreateToolbarButton(thRow.transform, "ThAmb", I18n.Tr("THEME_VINTAGE_NAME", "琥珀"), new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetThemeOverride("vintage_amber"));
            CreateToolbarButton(thRow.transform, "ThCyc", "↺", new Vector2(24f, 20f), () =>
            {
                string cur = _cachedPrimaryWidget?.Config?.ThemeOverride ?? "";
                int idx = Array.IndexOf(CycleThemeIds, cur);
                string next = CycleThemeIds[(idx + 1) % CycleThemeIds.Length];
                WidgetSelectionManager.BatchSetThemeOverride(next);
            });

            // 6. 旋转控制行
            GameObject rotRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            CreateLabel(rotRow.transform, "旋转:", 36f, new Color(1f, 0.88f, 0f));
            CreateToolbarButton(rotRow.transform, "R-15", "↺ 15°", new Vector2(48f, 20f), () => WidgetSelectionManager.BatchRotate(-15f));
            CreateToolbarButton(rotRow.transform, "R0", "0°", new Vector2(30f, 20f), () => WidgetSelectionManager.ResetRotation());
            CreateToolbarButton(rotRow.transform, "R+15", "↻ 15°", new Vector2(48f, 20f), () => WidgetSelectionManager.BatchRotate(+15f));
            CreateToolbarButton(rotRow.transform, "R90", "90°", new Vector2(36f, 20f), () => WidgetSelectionManager.BatchSetRotation(90f));
            CreateToolbarButton(rotRow.transform, "R180", "180°", new Vector2(42f, 20f), () => WidgetSelectionManager.BatchSetRotation(180f));
            CreateToolbarButton(rotRow.transform, "RCX", "⤢ 居中", new Vector2(50f, 20f), () => WidgetSelectionManager.CenterToScreenX());

            // 7. 图层层级与锁定控制行
            GameObject layRow = CreateRowLayout(_inspectorCardObj.transform, 22f);
            _inspectorLayerText = CreateCrispText(layRow.transform, "LayLbl", "图层: #1/1", 10, TextAnchor.MiddleLeft, new Color(0.22f, 0.74f, 0.97f));
            var lLe = _inspectorLayerText.gameObject.AddComponent<LayoutElement>();
            lLe.minWidth = 92f;
            lLe.preferredWidth = 92f;

            CreateToolbarButton(layRow.transform, "LFront", "⤒", new Vector2(25f, 20f), () => WidgetLayerManager.BringToFront(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(layRow.transform, "LFwd", "▲", new Vector2(25f, 20f), () => WidgetLayerManager.BringForward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(layRow.transform, "LBack", "▼", new Vector2(25f, 20f), () => WidgetLayerManager.SendBackward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(layRow.transform, "LBtm", "⤓", new Vector2(25f, 20f), () => WidgetLayerManager.SendToBack(WidgetSelectionManager.SelectedWidgets));

            var lockBtn = CreateToolbarButton(layRow.transform, "LockBtn", "🔒 锁定", new Vector2(62f, 20f), () =>
            {
                if (_cachedPrimaryWidget != null)
                {
                    WidgetLayerManager.ToggleLock(_cachedPrimaryWidget);
                    RefreshContextualInspector();
                }
            });
            _inspectorLockBtnText = lockBtn.GetComponentInChildren<Text>();

            // 8. 扩展抽屉入口与克隆 (仅在单选时显示)
            GameObject extRow = CreateRowLayout(_inspectorCardObj.transform, 22f);

            var microBtn = CreateToolbarButton(extRow.transform, "MicroBtn", "⚙️ 控件", new Vector2(98f, 20f), () =>
            {
                if (_isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.MicroControls)
                {
                    _isInspectorDrawerOpen = false;
                }
                else
                {
                    _isInspectorDrawerOpen = true;
                    _activeInspectorTab = InspectorTab.MicroControls;
                }
                IsSubControlCustomizerOpen = _isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.MicroControls;
                if (_inspectorDrawerObj != null) _inspectorDrawerObj.SetActive(_isInspectorDrawerOpen);
                RefreshInspectorDrawer();
            });
            _microCtrlBtnText = microBtn.GetComponentInChildren<Text>();

            var telemBtn = CreateToolbarButton(extRow.transform, "TelemBtn", "📊 遥测装配", new Vector2(92f, 20f), () =>
            {
                if (_isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.Telemetry)
                {
                    _isInspectorDrawerOpen = false;
                }
                else
                {
                    _isInspectorDrawerOpen = true;
                    _activeInspectorTab = InspectorTab.Telemetry;
                }
                IsSubControlCustomizerOpen = _isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.MicroControls;
                if (_inspectorDrawerObj != null) _inspectorDrawerObj.SetActive(_isInspectorDrawerOpen);
                RefreshInspectorDrawer();
            });
            _telemBtnText = telemBtn.GetComponentInChildren<Text>();

            // 画板工坊快速跳转
            _artboardStudioBtnObj = CreateToolbarButton(extRow.transform, "ArtStudioBtn", "🎨 画板工坊", new Vector2(80f, 20f), () =>
            {
                if (SettingsGUI.Instance != null)
                {
                    if (SettingsGUI.Instance.IsCanvasLayoutMode)
                    {
                        SettingsGUI.Instance.ExitCanvasLayoutMode();
                    }
                    SettingsGUI.Instance.OpenToTab(0);
                    if (_cachedPrimaryWidget != null) WidgetSelectionManager.Select(_cachedPrimaryWidget, false);
                }
            }, true).gameObject;

            // 快速克隆
            CreateToolbarButton(extRow.transform, "CloneBtn", "📑 克隆", new Vector2(58f, 20f), () => WidgetClipboardManager.DuplicateSelected());
        }
        #endregion

        #region Inspector Drawer (微控件/遥测/通道抽屉)
        private void BuildInspectorDrawer(RectTransform parent)
        {
            _inspectorDrawerObj = CreateGlassPanel(parent, "InspectorDrawer", new Vector2(400f, 320f), Vector2.zero, new Vector2(0f, 1f), new Vector2(100f, 240f), true);
            _inspectorDrawerRt = _inspectorDrawerObj.GetComponent<RectTransform>();
            AddHoverCatcher(_inspectorDrawerObj);

            var vl = _inspectorDrawerObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(8, 8, 6, 6);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 1. 顶栏标签切换与关闭
            GameObject tabRow = CreateRowLayout(_inspectorDrawerObj.transform, 22f);
            _drawerTitleText = CreateCrispText(tabRow.transform, "Title", "🛠️ <b>定制微控件</b>", 11, TextAnchor.MiddleLeft, Color.white);
            _drawerTitleText.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;

            _drawerTabMicroBtn = CreateToolbarButton(tabRow.transform, "TabMicro", "⚙️ 控件", new Vector2(56f, 20f), () => SwitchInspectorTab(InspectorTab.MicroControls));
            _drawerTabTelemBtn = CreateToolbarButton(tabRow.transform, "TabTelem", "📊 遥测", new Vector2(56f, 20f), () => SwitchInspectorTab(InspectorTab.Telemetry));
            _drawerTabChanBtn = CreateToolbarButton(tabRow.transform, "TabChan", "📋 通道", new Vector2(56f, 20f), () => SwitchInspectorTab(InspectorTab.Channels));

            var flexSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            flexSpacer.transform.SetParent(tabRow.transform, false);
            flexSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

            CreateToolbarButton(tabRow.transform, "CloseBtn", "✕", new Vector2(22f, 20f), () =>
            {
                _isInspectorDrawerOpen = false;
                IsSubControlCustomizerOpen = false;
                if (_inspectorDrawerObj != null) _inspectorDrawerObj.SetActive(false);
            });

            CreateDividerLine(_inspectorDrawerObj.transform);

            // 2. Tab 0: 微控件面板
            _drawerMicroPanel = new GameObject("MicroPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _drawerMicroPanel.transform.SetParent(_inspectorDrawerObj.transform, false);
            var mpVl = _drawerMicroPanel.GetComponent<VerticalLayoutGroup>();
            mpVl.childAlignment = TextAnchor.UpperLeft;
            mpVl.spacing = 3f;
            mpVl.childForceExpandWidth = true;
            mpVl.childForceExpandHeight = false;

            // 微控件提示与批处理栏
            GameObject microBatchRow = CreateRowLayout(_drawerMicroPanel.transform, 20f);
            CreateToolbarButton(microBatchRow.transform, "AllVis", "✔ 全显", new Vector2(46f, 19f), () =>
            {
                if (_cachedPrimaryWidget != null)
                {
                    var ctrls = _cachedPrimaryWidget.Controls.All;
                    for (int i = 0; i < ctrls.Count; i++) _cachedPrimaryWidget.Controls.SetControlVisibility(ctrls[i].Id, true);
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshInspectorDrawer();
                }
            });
            CreateToolbarButton(microBatchRow.transform, "AllHide", "○ 全隐", new Vector2(46f, 19f), () =>
            {
                if (_cachedPrimaryWidget != null)
                {
                    var ctrls = _cachedPrimaryWidget.Controls.All;
                    for (int i = 0; i < ctrls.Count; i++) _cachedPrimaryWidget.Controls.SetControlVisibility(ctrls[i].Id, false);
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshInspectorDrawer();
                }
            });
            CreateToolbarButton(microBatchRow.transform, "ResetOff", "↺ 复位", new Vector2(46f, 19f), () =>
            {
                if (_cachedPrimaryWidget != null)
                {
                    _cachedPrimaryWidget.Controls.ResetAllOffsets();
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshInspectorDrawer();
                }
            });

            var hintLe = CreateCrispText(microBatchRow.transform, "Hint", "<color=#94A3B8><size=9>拖拽屏幕控件 / Shift步进</size></color>", 9, TextAnchor.MiddleRight, WorkbenchStyleEngine.ColorTextMuted);
            hintLe.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _drawerMicroContent = CreateScrollView(_drawerMicroPanel.transform, 235f);

            // 3. Tab 1: 遥测装配面板
            _drawerTelemPanel = new GameObject("TelemPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _drawerTelemPanel.transform.SetParent(_inspectorDrawerObj.transform, false);
            var tpVl = _drawerTelemPanel.GetComponent<VerticalLayoutGroup>();
            tpVl.childAlignment = TextAnchor.UpperLeft;
            tpVl.spacing = 3f;
            tpVl.childForceExpandWidth = true;
            tpVl.childForceExpandHeight = false;

            _drawerTelemContent = CreateScrollView(_drawerTelemPanel.transform, 255f);

            // 4. Tab 2: 通道绑定面板
            _drawerChanPanel = new GameObject("ChanPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _drawerChanPanel.transform.SetParent(_inspectorDrawerObj.transform, false);
            var cpVl = _drawerChanPanel.GetComponent<VerticalLayoutGroup>();
            cpVl.childAlignment = TextAnchor.UpperLeft;
            cpVl.spacing = 3f;
            cpVl.childForceExpandWidth = true;
            cpVl.childForceExpandHeight = false;

            _drawerChanContent = CreateScrollView(_drawerChanPanel.transform, 255f);

            _inspectorDrawerObj.SetActive(false);
        }

        private void SwitchInspectorTab(InspectorTab tab)
        {
            _activeInspectorTab = tab;
            IsSubControlCustomizerOpen = _isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.MicroControls;

            if (_drawerMicroPanel != null) _drawerMicroPanel.SetActive(_activeInspectorTab == InspectorTab.MicroControls);
            if (_drawerTelemPanel != null) _drawerTelemPanel.SetActive(_activeInspectorTab == InspectorTab.Telemetry);
            if (_drawerChanPanel != null) _drawerChanPanel.SetActive(_activeInspectorTab == InspectorTab.Channels);

            UpdateTabButtonHighlight(_drawerTabMicroBtn, _activeInspectorTab == InspectorTab.MicroControls);
            UpdateTabButtonHighlight(_drawerTabTelemBtn, _activeInspectorTab == InspectorTab.Telemetry);
            UpdateTabButtonHighlight(_drawerTabChanBtn, _activeInspectorTab == InspectorTab.Channels);

            RefreshInspectorDrawer();
        }
        #endregion

        #region Layer Drawer (图层管理器抽屉)
        private void BuildLayerDrawer(RectTransform parent)
        {
            _layerDrawerObj = CreateGlassPanel(parent, "LayerDrawer", new Vector2(280f, 440f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -90f), true);
            _layerDrawerRt = _layerDrawerObj.GetComponent<RectTransform>();
            AddHoverCatcher(_layerDrawerObj);

            var vl = _layerDrawerObj.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(8, 8, 6, 6);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 顶栏标题
            GameObject hRow = CreateRowLayout(_layerDrawerObj.transform, 22f);
            _layerDrawerTitleText = CreateCrispText(hRow.transform, "Title", "📑 <b>图层管理</b>", 12, TextAnchor.MiddleLeft, Color.white);
            _layerDrawerTitleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            CreateToolbarButton(hRow.transform, "CloseBtn", "✕", new Vector2(22f, 20f), () =>
            {
                _isLayerDrawerOpen = false;
                WidgetLayerManager.IsLayerPanelOpen = false;
                if (_layerDrawerObj != null) _layerDrawerObj.SetActive(false);
                RefreshTopBarStates();
            });

            // 快捷控制条
            GameObject actRow = CreateRowLayout(_layerDrawerObj.transform, 24f);
            CreateToolbarButton(actRow.transform, "LTop", "⤒ 顶", new Vector2(58f, 22f), () => WidgetLayerManager.BringToFront(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LUp", "▲ 升", new Vector2(58f, 22f), () => WidgetLayerManager.BringForward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LDn", "▼ 降", new Vector2(58f, 22f), () => WidgetLayerManager.SendBackward(WidgetSelectionManager.SelectedWidgets));
            CreateToolbarButton(actRow.transform, "LBtm", "⤓ 底", new Vector2(58f, 22f), () => WidgetLayerManager.SendToBack(WidgetSelectionManager.SelectedWidgets));

            CreateDividerLine(_layerDrawerObj.transform);

            // 倒序图层列表 (顶层在最上)
            _layerDrawerContent = CreateScrollView(_layerDrawerObj.transform, 350f);

            _layerDrawerObj.SetActive(_isLayerDrawerOpen);
        }
        #endregion

        #region Favorite Mod Dock 构建
        private void BuildFavoriteModDock(RectTransform parent)
        {
            _modDockObj = CreateGlassPanel(parent, "FavoriteModDock", new Vector2(1260f, 44f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), true);
            _modDockRt = _modDockObj.GetComponent<RectTransform>();
            AddHoverCatcher(_modDockObj);

            var hl = _modDockObj.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 6f;
            hl.padding = new RectOffset(10, 10, 6, 6);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            var title = CreateCrispText(_modDockObj.transform, "Header", "★ <b>常用模组:</b>", 11, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorWarning);
            title.gameObject.AddComponent<LayoutElement>().preferredWidth = 86f;

            // 滚动按钮区域
            GameObject scrollRoot = new GameObject("ModScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollRoot.transform.SetParent(_modDockObj.transform, false);

            var srLe = scrollRoot.AddComponent<LayoutElement>();
            srLe.flexibleWidth = 1f;
            srLe.preferredHeight = 32f;

            scrollRoot.GetComponent<Image>().color = Color.clear;
            var mask = scrollRoot.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            var sr = scrollRoot.GetComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.scrollSensitivity = 25f;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(scrollRoot.transform, false);

            var cRt = contentObj.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(0f, 1f);
            cRt.pivot = new Vector2(0f, 0.5f);

            var cHl = contentObj.GetComponent<HorizontalLayoutGroup>();
            cHl.childAlignment = TextAnchor.MiddleLeft;
            cHl.spacing = 6f;
            cHl.childForceExpandWidth = false;
            cHl.childForceExpandHeight = false;

            var csf = contentObj.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = cRt;
            _modDockContent = cRt;

            // 空提示
            _modDockEmptyHint = CreateCrispText(contentObj.transform, "EmptyHint", "<color=#94A3B8><size=11>未收藏常用模组，点击右侧「⚙️ 管理」或「★ 推荐」添加到快捷坞</size></color>", 11, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted).gameObject;

            CreateToolbarButton(_modDockObj.transform, "AutoRecBtn", "★ 推荐", new Vector2(58f, 24f), () =>
            {
                ThemeManager.Instance?.AutoRecommendFavorites();
                ThemeManager.Instance?.EnsureDockRulesPopulated();
                PopulateFavoriteModDock();
            }, false, WorkbenchStyleEngine.ColorWarning);

            CreateToolbarButton(_modDockObj.transform, "MgrBtn", "⚙️ 管理", new Vector2(58f, 24f), () =>
            {
                SettingsGUI.Instance?.OpenToTab(0);
            });

            CreateToolbarButton(_modDockObj.transform, "CloseBtn", "✕", new Vector2(24f, 24f), () =>
            {
                _isFavoriteModDockOpen = false;
                if (_modDockObj != null) _modDockObj.SetActive(false);
                RefreshTopBarStates();
            });

            _modDockObj.SetActive(_isFavoriteModDockOpen);
        }
        #endregion

        #region 动态位置计算与智能视口防溢出
        private void UpdateContextualInspectorPosition()
        {
            if (_cachedPrimaryWidget == null || _cachedPrimaryWidget.RectTransform == null || _inspectorCardRt == null)
            {
                if (_inspectorCardObj != null && _inspectorCardObj.activeSelf) _inspectorCardObj.SetActive(false);
                if (_inspectorDrawerObj != null && _inspectorDrawerObj.activeSelf) _inspectorDrawerObj.SetActive(false);
                return;
            }

            if (!_inspectorCardObj.activeSelf) _inspectorCardObj.SetActive(true);

            Vector3[] corners = new Vector3[4];
            _cachedPrimaryWidget.RectTransform.GetWorldCorners(corners);

            Canvas canvas = _hudManager != null ? _hudManager.Canvas : GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 p1 = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            Vector2 p3 = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            float badgeW = _inspectorCardRt.sizeDelta.x;
            float badgeH = _inspectorCardRt.sizeDelta.y;

            // 优先置于组件右侧，相距 10px
            float bx = maxX + 10f;
            float by = maxY;

            // 若右侧超出屏幕边缘，则自适应翻转至组件左侧
            if (bx + badgeW > Screen.width - 10f)
            {
                bx = minX - badgeW - 10f;
            }
            // 若左右两侧都超出，则置于组件正上方
            if (bx < 10f)
            {
                bx = Mathf.Clamp(minX, 10f, Screen.width - badgeW - 10f);
                by = maxY + badgeH + 10f;
            }

            // 屏幕安全边界截断约束
            bx = Mathf.Clamp(bx, 10f, Screen.width - badgeW - 10f);
            by = Mathf.Clamp(by, badgeH + 10f, Screen.height - 10f);

            _inspectorCardRt.anchoredPosition = new Vector2(bx, by - Screen.height);

            // 同步子属性抽屉位置
            if (_inspectorDrawerObj != null && _inspectorDrawerObj.activeSelf && _inspectorDrawerRt != null)
            {
                float subW = _inspectorDrawerRt.sizeDelta.x;
                float subH = _inspectorDrawerRt.sizeDelta.y;
                float subX = bx;
                float subY = by - badgeH - 6f;

                if (subY - subH < 10f)
                {
                    subY = by + subH + 6f;
                }
                subX = Mathf.Clamp(subX, 10f, Screen.width - subW - 10f);
                subY = Mathf.Clamp(subY, subH + 10f, Screen.height - 10f);

                _inspectorDrawerRt.anchoredPosition = new Vector2(subX, subY - Screen.height);
            }
        }
        #endregion

        #region 数据刷新核心
        private void RefreshAllStates(bool force)
        {
            int selCount = WidgetSelectionManager.Count;
            BaseFlightWidget primary = null;
            if (selCount > 0)
            {
                foreach (var w in WidgetSelectionManager.SelectedWidgets)
                {
                    if (w != null && w.RectTransform != null)
                    {
                        primary = w;
                        break;
                    }
                }
            }

            bool selChanged = force || (selCount != _cachedSelectedCount) || (primary != _cachedPrimaryWidget);
            _cachedSelectedCount = selCount;
            _cachedPrimaryWidget = primary;

            if (selChanged)
            {
                RefreshTopBarStates();
                RefreshContextualInspector();
                if (_isLayerDrawerOpen) RefreshLayerDrawer();
                if (_isInspectorDrawerOpen) RefreshInspectorDrawer();
                if (_isFavoriteModDockOpen) PopulateFavoriteModDock();
            }
            else
            {
                // 轻量刷新撤销重做状态与动态数值
                if (_undoBtn != null) _undoBtn.interactable = WidgetEditHistory.CanUndo;
                if (_redoBtn != null) _redoBtn.interactable = WidgetEditHistory.CanRedo;

                if (_cachedPrimaryWidget != null && _inspectorStatsText != null)
                {
                    float curScale = _cachedPrimaryWidget.Config?.Scale ?? 1.0f;
                    float curRot = _cachedPrimaryWidget.Config?.Rotation ?? 0f;
                    _inspectorStatsText.text = $"<color=#00E5FF><b>{curScale:F2}x</b></color> | <color=#FFE000><b>{curRot:F0}°</b></color>";
                }
            }
        }

        private void RefreshTopBarStates()
        {
            int selCount = _cachedSelectedCount;

            // 1. 标题与已选项数
            if (_selectionPillText != null)
            {
                _selectionPillText.text = selCount > 0
                    ? $"🛠️ MFP | <color=#FFE000><b>已选 {selCount} 项</b></color>"
                    : "🛠️ MFP | <color=#AAAAAA>未选中 (拉框多选)</color>";
            }

            // 2. 撤销/重做
            if (_undoBtn != null) _undoBtn.interactable = WidgetEditHistory.CanUndo;
            if (_redoBtn != null) _redoBtn.interactable = WidgetEditHistory.CanRedo;

            // 3. 对齐按钮禁用状态
            if (_alignBtns != null)
            {
                for (int i = 0; i < _alignBtns.Length; i++)
                {
                    if (_alignBtns[i] == null) continue;
                    if (i < 6) _alignBtns[i].interactable = selCount >= 2;
                    else if (i < 8) _alignBtns[i].interactable = selCount >= 3;
                    else _alignBtns[i].interactable = selCount >= 1;
                }
            }

            // 4. 第二行动作栏模式切换
            if (_topSelActionsObj != null) _topSelActionsObj.SetActive(selCount > 0);
            if (_topNoSelHintsObj != null) _topNoSelHintsObj.SetActive(selCount == 0);

            // 5. 开关状态高亮反馈
            bool snap = WidgetDragHandler.EnableMagneticSnap;
            if (_snapBtnText != null) _snapBtnText.text = snap ? "🧲 磁吸: [开]" : "🧲 磁吸: [关]";
            if (_snapBtnImg != null) _snapBtnImg.color = snap ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;

            bool grid = WidgetCanvasGrid.IsGridVisible;
            if (_gridBtnText != null) _gridBtnText.text = grid ? "▦ 网格: [开]" : "▦ 网格: [关]";
            if (_gridBtnImg != null) _gridBtnImg.color = grid ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;

            if (_layerBtnText != null) _layerBtnText.text = _isLayerDrawerOpen ? "📑 图层: [开]" : "📑 图层: [关]";
            if (_layerBtnImg != null) _layerBtnImg.color = _isLayerDrawerOpen ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;

            int favCount = GetFavoriteButtonCount();
            if (_modDockBtnText != null)
            {
                _modDockBtnText.text = _isFavoriteModDockOpen
                    ? $"★ 模组 ({favCount}): [开]"
                    : $"★ 模组 ({favCount})";
            }
            if (_modDockBtnImg != null) _modDockBtnImg.color = _isFavoriteModDockOpen ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;
        }

        private void RefreshContextualInspector()
        {
            if (_cachedPrimaryWidget == null || _cachedPrimaryWidget.Config == null)
            {
                if (_inspectorCardObj != null && _inspectorCardObj.activeSelf) _inspectorCardObj.SetActive(false);
                if (_inspectorDrawerObj != null && _inspectorDrawerObj.activeSelf) _inspectorDrawerObj.SetActive(false);
                return;
            }

            if (_inspectorCardObj != null && !_inspectorCardObj.activeSelf) _inspectorCardObj.SetActive(true);

            // 标题与角标
            if (_inspectorTitleText != null)
            {
                string prefix = _cachedSelectedCount > 1 ? $"<color=#FFE000>[已选 {_cachedSelectedCount} 项]</color> " : "";
                _inspectorTitleText.text = $"{prefix}<b>{_cachedPrimaryWidget.DisplayName}</b>";
            }

            // 实时缩放与角度
            float curScale = _cachedPrimaryWidget.Config.Scale;
            float curRot = _cachedPrimaryWidget.Config.Rotation;
            if (_inspectorStatsText != null)
            {
                _inspectorStatsText.text = $"<color=#00E5FF><b>{curScale:F2}x</b></color> | <color=#FFE000><b>{curRot:F0}°</b></color>";
            }

            // 透明度
            if (_inspectorOpacityText != null)
            {
                int curOpPct = Mathf.RoundToInt(_cachedPrimaryWidget.Opacity * 100f);
                _inspectorOpacityText.text = $"{curOpPct}%";
            }

            // 图层与锁定状态
            int curLayer = WidgetLayerManager.GetLayerNumber(_cachedPrimaryWidget);
            int totalLayers = WidgetLayerManager.TotalLayers;
            if (_inspectorLayerText != null)
            {
                _inspectorLayerText.text = $"<color=#38BDF8><b>图层: #{curLayer}/{totalLayers}</b></color>";
            }

            bool isLocked = _cachedPrimaryWidget.Config.IsLocked;
            if (_inspectorLockBtnText != null)
            {
                _inspectorLockBtnText.text = isLocked ? "<color=#FFB703>🔒 锁定</color>" : "<color=#94A3B8>🔓 解锁</color>";
            }

            // 微控件计数
            var ctrlList = _cachedPrimaryWidget.Controls.All;
            int totalCtrls = ctrlList.Count;
            int visCtrls = 0;
            for (int i = 0; i < totalCtrls; i++) if (ctrlList[i].IsVisible) visCtrls++;

            if (_microCtrlBtnText != null)
            {
                bool isMicroActive = _isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.MicroControls;
                _microCtrlBtnText.text = isMicroActive ? $"⚙️ 控件 ({visCtrls}/{totalCtrls}) [开]" : $"⚙️ 控件 ({visCtrls}/{totalCtrls})";
            }

            if (_telemBtnText != null)
            {
                bool isTelemActive = _isInspectorDrawerOpen && _activeInspectorTab == InspectorTab.Telemetry;
                _telemBtnText.text = isTelemActive ? "📊 遥测 [开]" : "📊 遥测装配";
            }

            if (_artboardStudioBtnObj != null)
            {
                bool isComposite = _cachedPrimaryWidget is Widgets.Gauges.CustomCompositePanelWidget;
                _artboardStudioBtnObj.SetActive(isComposite);
            }
        }

        private void RefreshInspectorDrawer()
        {
            if (_cachedPrimaryWidget == null || !_isInspectorDrawerOpen)
            {
                if (_inspectorDrawerObj != null && _inspectorDrawerObj.activeSelf) _inspectorDrawerObj.SetActive(false);
                return;
            }

            if (_inspectorDrawerObj != null && !_inspectorDrawerObj.activeSelf) _inspectorDrawerObj.SetActive(true);

            if (_drawerTitleText != null)
            {
                _drawerTitleText.text = $"🛠️ <b>{_cachedPrimaryWidget.DisplayName}</b>";
            }

            if (_activeInspectorTab == InspectorTab.MicroControls)
            {
                RefreshMicroControlsTab();
            }
            else if (_activeInspectorTab == InspectorTab.Telemetry)
            {
                RefreshTelemetryTab();
            }
            else if (_activeInspectorTab == InspectorTab.Channels)
            {
                RefreshChannelsTab();
            }
        }

        private void RefreshMicroControlsTab()
        {
            if (_cachedPrimaryWidget == null || _drawerMicroContent == null) return;
            var ctrlList = _cachedPrimaryWidget.Controls.All;
            int count = ctrlList.Count;

            EnsureMicroRows(count);

            for (int i = 0; i < count; i++)
            {
                var ctrl = ctrlList[i];
                var row = _microRowsPool[i];
                row.Root.SetActive(true);

                // LED
                row.LedText.text = ctrl.IsVisible ? "<color=#00FF88>●</color>" : "<color=#7088A8>○</color>";
                row.LedBtn.onClick.RemoveAllListeners();
                var curCtrl = ctrl;
                row.LedBtn.onClick.AddListener(() =>
                {
                    _cachedPrimaryWidget.Controls.SetControlVisibility(curCtrl.Id, !curCtrl.IsVisible);
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshContextualInspector();
                    RefreshMicroControlsTab();
                });

                // 类别徽章
                row.CatBadgeText.text = GetCategoryShortTag(ctrl.Category);

                // 名称
                row.NameText.text = ctrl.DisplayName;

                // 偏移读数
                Vector2 off = ctrl.CurrentOffset;
                bool hasOff = Mathf.Abs(off.x) > 0.01f || Mathf.Abs(off.y) > 0.01f;
                row.OffsetReadoutText.text = hasOff ? $"<color=#00E5FF>{off.x:+0;-0;0},{off.y:+0;-0;0}</color>" : "<color=#64748B>0,0</color>";

                // 方向步进
                row.LeftBtn.onClick.RemoveAllListeners();
                row.LeftBtn.onClick.AddListener(() => StepControlOffset(curCtrl, -1f, 0f));
                row.RightBtn.onClick.RemoveAllListeners();
                row.RightBtn.onClick.AddListener(() => StepControlOffset(curCtrl, 1f, 0f));
                row.UpBtn.onClick.RemoveAllListeners();
                row.UpBtn.onClick.AddListener(() => StepControlOffset(curCtrl, 0f, 1f));
                row.DownBtn.onClick.RemoveAllListeners();
                row.DownBtn.onClick.AddListener(() => StepControlOffset(curCtrl, 0f, -1f));

                // 复位
                row.ResetBtn.gameObject.SetActive(hasOff);
                row.ResetBtn.onClick.RemoveAllListeners();
                row.ResetBtn.onClick.AddListener(() =>
                {
                    _cachedPrimaryWidget.Controls.SetControlOffset(curCtrl.Id, Vector2.zero);
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshMicroControlsTab();
                });
            }

            for (int i = count; i < _microRowsPool.Count; i++)
            {
                _microRowsPool[i].Root.SetActive(false);
            }
        }

        private void StepControlOffset(IWidgetControl ctrl, float dx, float dy)
        {
            if (ctrl == null || _cachedPrimaryWidget == null) return;
            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 10f : 2f;
            Vector2 cur = ctrl.CurrentOffset;
            _cachedPrimaryWidget.Controls.SetControlOffset(ctrl.Id, cur + new Vector2(dx * step, dy * step));
            WidgetLayoutManager.Instance.SaveLayout();
            RefreshMicroControlsTab();
        }

        private void RefreshTelemetryTab()
        {
            if (_cachedPrimaryWidget == null || _drawerTelemContent == null) return;
            var w = _cachedPrimaryWidget.Config;
            if (w == null) return;

            var allControls = _cachedPrimaryWidget.Controls?.All;
            var bindableControls = new List<ITelemetryBindableControl>();
            if (allControls != null)
            {
                for (int i = 0; i < allControls.Count; i++)
                {
                    if (allControls[i] is ITelemetryBindableControl bindable && bindable.HasTelemetryBinding)
                    {
                        bindableControls.Add(bindable);
                    }
                }
            }

            int count = bindableControls.Count;
            EnsureTelemRows(count);

            for (int i = 0; i < count; i++)
            {
                var ctrl = bindableControls[i];
                var row = _telemRowsPool[i];
                row.Root.SetActive(true);

                // LED
                row.LedText.text = ctrl.IsVisible ? "<color=#00FF88>●</color>" : "<color=#7088A8>○</color>";
                row.LedBtn.onClick.RemoveAllListeners();
                var curCtrl = ctrl;
                row.LedBtn.onClick.AddListener(() =>
                {
                    _cachedPrimaryWidget.Controls.SetControlVisibility(curCtrl.Id, !curCtrl.IsVisible);
                    WidgetLayoutManager.Instance.SaveLayout();
                    RefreshContextualInspector();
                    RefreshTelemetryTab();
                });

                // 类别与名称
                row.CatBadgeText.text = GetCategoryShortTag(ctrl.Category);
                row.NameText.text = ctrl.DisplayName;

                // Token
                row.TokenInput.text = ctrl.TelemetryToken ?? "";
                row.TokenInput.onEndEdit.RemoveAllListeners();
                row.TokenInput.onEndEdit.AddListener(val =>
                {
                    curCtrl.TelemetryToken = val;
                    SetChannel(w, $"{curCtrl.Id.ToUpperInvariant()}_TOKEN", val);
                });

                // 实时采样值预览
                string sampleVal = GetSampledTokenValue(ctrl.TelemetryToken);
                string unit = !string.IsNullOrEmpty(ctrl.TelemetryUnit) ? $" {ctrl.TelemetryUnit}" : "";
                row.SampleText.text = $"<color=#00FF88><b>{sampleVal}</b></color><size=9>{unit}</size>";

                // 选参数搜索按钮
                row.SearchBtn.onClick.RemoveAllListeners();
                row.SearchBtn.onClick.AddListener(() =>
                {
                    TelemetryParamDrawer.Open($"{_cachedPrimaryWidget.DisplayName} - {curCtrl.DisplayName}", chosenToken =>
                    {
                        curCtrl.TelemetryToken = chosenToken;
                        SetChannel(w, $"{curCtrl.Id.ToUpperInvariant()}_TOKEN", chosenToken);
                        RefreshTelemetryTab();
                    });
                });

                // 重置按钮
                row.ResetBtn.onClick.RemoveAllListeners();
                row.ResetBtn.onClick.AddListener(() =>
                {
                    curCtrl.TelemetryToken = "";
                    SetChannel(w, $"{curCtrl.Id.ToUpperInvariant()}_TOKEN", "");
                    RefreshTelemetryTab();
                });
            }

            for (int i = count; i < _telemRowsPool.Count; i++)
            {
                _telemRowsPool[i].Root.SetActive(false);
            }
        }

        private static readonly string[] DefaultChannelKeys = new string[] { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6" };
        private static readonly string[] DefaultChannelNames = new string[] { "主速度", "推重比", "雷达高", "动压", "垂直速", "过载" };
        private static readonly string[] DefaultChannelTokens = new string[] { "{SPD}", "{TWR}", "{ALT:AGL}", "{Q}", "{VSI}", "{GFORCE}" };

        private void RefreshChannelsTab()
        {
            if (_cachedPrimaryWidget == null || _drawerChanContent == null) return;
            var w = _cachedPrimaryWidget.Config;
            if (w == null) return;

            var channelDict = ParseChannels(w.CustomTemplate);
            EnsureChanRows(6);

            for (int i = 0; i < 6; i++)
            {
                string chKey = DefaultChannelKeys[i];
                string chName = DefaultChannelNames[i];
                string defaultToken = DefaultChannelTokens[i];

                string currentVal;
                if (!channelDict.TryGetValue(chKey, out currentVal)) currentVal = defaultToken;

                var row = _chanRowsPool[i];
                row.Root.SetActive(true);

                row.NameText.text = $"<b>{chKey}</b> <size=9><color=#94A3B8>({chName})</color></size>";
                row.TokenInput.text = currentVal ?? "";
                string curKey = chKey;
                row.TokenInput.onEndEdit.RemoveAllListeners();
                row.TokenInput.onEndEdit.AddListener(val =>
                {
                    SetChannel(w, curKey, val);
                });

                string sampleVal = GetSampledTokenValue(currentVal);
                row.SampleText.text = $"<color=#00FF88><b>{sampleVal}</b></color>";

                row.SearchBtn.onClick.RemoveAllListeners();
                row.SearchBtn.onClick.AddListener(() =>
                {
                    TelemetryParamDrawer.Open($"{_cachedPrimaryWidget.DisplayName} {curKey}", chosenToken =>
                    {
                        SetChannel(w, curKey, chosenToken);
                        RefreshChannelsTab();
                    });
                });

                row.ResetBtn.onClick.RemoveAllListeners();
                row.ResetBtn.onClick.AddListener(() =>
                {
                    SetChannel(w, curKey, defaultToken);
                    RefreshChannelsTab();
                });
            }
        }

        private void RefreshLayerDrawer()
        {
            if (_layerDrawerContent == null) return;

            var widgetsTopDown = WidgetLayerManager.GetWidgetsTopToBottom();
            int count = widgetsTopDown.Count;
            if (_layerDrawerTitleText != null)
            {
                _layerDrawerTitleText.text = $"📑 <b>图层管理</b> <color=#94A3B8>({count})</color>";
            }

            EnsureLayerRows(count);

            for (int i = 0; i < count; i++)
            {
                var w = widgetsTopDown[i];
                var row = _layerRowsPool[i];
                row.Root.SetActive(true);

                bool isSel = WidgetSelectionManager.IsSelected(w);
                bool isLock = w.Config?.IsLocked == true;
                bool isVis = w.Config?.IsEnabled == true;

                // 显隐按钮 (Eye)
                row.EyeText.text = isVis ? "<color=#00FF88>👁</color>" : "<color=#64748B>○</color>";
                var curW = w;
                row.EyeBtn.onClick.RemoveAllListeners();
                row.EyeBtn.onClick.AddListener(() =>
                {
                    WidgetLayerManager.ToggleVisibility(curW);
                    RefreshLayerDrawer();
                });

                // 锁定按钮 (Lock)
                row.LockText.text = isLock ? "<color=#FFB703>🔒</color>" : "<color=#64748B>🔓</color>";
                row.LockBtn.onClick.RemoveAllListeners();
                row.LockBtn.onClick.AddListener(() =>
                {
                    WidgetLayerManager.ToggleLock(curW);
                    RefreshLayerDrawer();
                });

                // 层级标签
                int layerNum = (w.Config?.DrawOrder ?? 0) + 1;
                row.LayerBadgeText.text = $"<color=#38BDF8><b>#{layerNum}</b></color>";

                // 组件选择
                row.NameText.text = isSel ? $"<b>{w.DisplayName}</b>" : w.DisplayName;
                row.NameText.color = isSel ? WorkbenchStyleEngine.ColorAccentPrimary : (isLock ? WorkbenchStyleEngine.ColorTextMuted : Color.white);
                row.RowBgImage.color = isSel ? new Color(0.12f, 0.35f, 0.55f, 0.85f) : new Color(0.06f, 0.09f, 0.14f, 0.65f);

                row.SelectBtn.onClick.RemoveAllListeners();
                row.SelectBtn.onClick.AddListener(() =>
                {
                    bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                                      Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    if (isAdditive) WidgetSelectionManager.ToggleSelect(curW);
                    else WidgetSelectionManager.Select(curW, false);
                    RefreshAllStates(true);
                });
            }

            for (int i = count; i < _layerRowsPool.Count; i++)
            {
                _layerRowsPool[i].Root.SetActive(false);
            }
        }

        private void PopulateFavoriteModDock()
        {
            if (_modDockContent == null) return;

            var rules = ThemeManager.Instance?.DockRules;
            var favList = new List<DockButtonRule>();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i].IsFavorite) favList.Add(rules[i]);
                }
            }

            if (_modDockEmptyHint != null)
            {
                _modDockEmptyHint.SetActive(favList.Count == 0);
            }

            while (_modDockBtnPool.Count < favList.Count)
            {
                var btn = CreateToolbarButton(_modDockContent, $"ModBtn_{_modDockBtnPool.Count}", "", new Vector2(76f, 26f), null);
                _modDockBtnPool.Add(btn.gameObject);
            }

            for (int i = 0; i < favList.Count; i++)
            {
                var rule = favList[i];
                var btnObj = _modDockBtnPool[i];
                btnObj.SetActive(true);

                string label = !string.IsNullOrEmpty(rule.CustomLabel) ? rule.CustomLabel : rule.DefaultName;
                if (string.IsNullOrEmpty(label)) label = rule.Key;

                var btnText = btnObj.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = label;

                var btn = btnObj.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                var curRule = rule;
                btn.onClick.AddListener(() => TriggerFavoriteModClick(curRule, false));
            }

            for (int i = favList.Count; i < _modDockBtnPool.Count; i++)
            {
                _modDockBtnPool[i].SetActive(false);
            }
        }

        private void TriggerFavoriteModClick(DockButtonRule rule, bool isRightClick)
        {
            if (rule == null) return;
#if KSP_RUNTIME
            try
            {
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                if (launcher != null)
                {
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    var all = new List<KSP.UI.Screens.ApplicationLauncherButton>();
                    if (stockBtns != null) all.AddRange(stockBtns);
                    if (modBtns != null) all.AddRange(modBtns);

                    for (int i = 0; i < all.Count; i++)
                    {
                        var btn = all[i];
                        if (btn == null) continue;
                        ModernToolbarWidget.GetButtonIdentity(btn, i, out string k, out string _);
                        if (k.Equals(rule.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            ModernToolbarWidget.TriggerKspButtonClick(btn, null, isRightClick);
                            MFPToastBridge.Show(string.Format(I18n.Tr("UI_TOAST_MOD_TRIGGERED", "✔ 已触发 [{0}]"), rule.DisplayName));
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("HUDEdit_TriggerMod", $"Failed triggering mod button: {ex.Message}");
            }
#endif
            MFPToastBridge.Show(string.Format(I18n.Tr("UI_TOAST_MOD_TRIGGERED_SIM", "✔ 已触发 [{0}] (模拟)"), rule.DisplayName));
        }

        private int GetFavoriteButtonCount()
        {
            var rules = ThemeManager.Instance?.DockRules;
            if (rules == null) return 0;
            int count = 0;
            for (int i = 0; i < rules.Count; i++) if (rules[i].IsFavorite) count++;
            return count;
        }

        private static string GetSampledTokenValue(string token)
        {
            if (string.IsNullOrEmpty(token)) return "---";
            string val;
            if (_channelEvalCache.TryGetValue(token, out val) && Time.unscaledTime - _lastEvalTime <= 0.25f)
            {
                return val;
            }
            try
            {
                val = TelemetryTokenEngine.Evaluate(token, TelemetryHub.Instance);
                if (string.IsNullOrEmpty(val)) val = "---";
            }
            catch
            {
                val = "ERR";
            }
            _channelEvalCache[token] = val;
            _lastEvalTime = Time.unscaledTime;
            return val;
        }

        private static Dictionary<string, string> ParseChannels(string template)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return dict;
            string[] parts = template.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string v = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : "";
                    dict[k] = v;
                }
            }
            return dict;
        }

        private static void SetChannel(WidgetConfig w, string channelKey, string token)
        {
            var dict = ParseChannels(w.CustomTemplate);
            dict[channelKey] = token;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            w.CustomTemplate = sb.ToString();
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        public static string GetCategoryShortTag(WidgetControlCategory cat)
        {
            switch (cat)
            {
                case WidgetControlCategory.Header: return "标题";
                case WidgetControlCategory.Readout: return "数显";
                case WidgetControlCategory.LinearGauge: return "柱条";
                case WidgetControlCategory.ArcGauge: return "弧表";
                case WidgetControlCategory.NeedlePointer: return "指针";
                case WidgetControlCategory.ActionButton: return "按键";
                case WidgetControlCategory.Annunciator: return "灯珠";
                case WidgetControlCategory.Viewport: return "视口";
                case WidgetControlCategory.DataStack: return "列表";
                case WidgetControlCategory.ModeCapsule: return "胶囊";
                case WidgetControlCategory.TrendBar: return "趋势";
                default: return "图元";
            }
        }
        #endregion

        #region 行池化与 UI 工厂方法
        private class MicroControlRowUI
        {
            public GameObject Root;
            public Text LedText;
            public Button LedBtn;
            public Text CatBadgeText;
            public Text NameText;
            public Button LeftBtn;
            public Button RightBtn;
            public Button UpBtn;
            public Button DownBtn;
            public Text OffsetReadoutText;
            public Button ResetBtn;
        }

        private class TelemetryBindRowUI
        {
            public GameObject Root;
            public Text LedText;
            public Button LedBtn;
            public Text CatBadgeText;
            public Text NameText;
            public InputField TokenInput;
            public Button SearchBtn;
            public Text SampleText;
            public Button ResetBtn;
        }

        private class ChannelRowUI
        {
            public GameObject Root;
            public Text NameText;
            public InputField TokenInput;
            public Button SearchBtn;
            public Text SampleText;
            public Button ResetBtn;
        }

        private class LayerRowUI
        {
            public GameObject Root;
            public Image RowBgImage;
            public Button SelectBtn;
            public Text EyeText;
            public Button EyeBtn;
            public Text LockText;
            public Button LockBtn;
            public Text LayerBadgeText;
            public Text NameText;
        }

        private void EnsureMicroRows(int count)
        {
            if (_drawerMicroContent == null) return;
            while (_microRowsPool.Count < count)
            {
                GameObject row = CreateRowLayout(_drawerMicroContent, 22f);
                var rLe = row.AddComponent<LayoutElement>();
                rLe.minHeight = 22f;
                rLe.preferredHeight = 22f;
                rLe.flexibleWidth = 1f;

                var ledBtn = CreateToolbarButton(row.transform, "Led", "●", new Vector2(20f, 18f), null);
                var catPill = CreatePill(row.transform, "Cat", "数显", WorkbenchStyleEngine.ColorAccentPrimary, 9);
                catPill.GetComponent<LayoutElement>().preferredWidth = 36f;

                var nameTxt = CreateCrispText(row.transform, "Name", "", 10, TextAnchor.MiddleLeft, Color.white);
                nameTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 85f;

                var lBtn = CreateToolbarButton(row.transform, "L", "◀", new Vector2(18f, 18f), null);
                var rBtn = CreateToolbarButton(row.transform, "R", "▶", new Vector2(18f, 18f), null);
                var uBtn = CreateToolbarButton(row.transform, "U", "▲", new Vector2(18f, 18f), null);
                var dBtn = CreateToolbarButton(row.transform, "D", "▼", new Vector2(18f, 18f), null);

                var offTxt = CreateCrispText(row.transform, "Off", "0,0", 9, TextAnchor.MiddleCenter, WorkbenchStyleEngine.ColorTextMuted);
                offTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;

                var resBtn = CreateToolbarButton(row.transform, "Res", "↺", new Vector2(18f, 18f), null);

                _microRowsPool.Add(new MicroControlRowUI
                {
                    Root = row,
                    LedText = ledBtn.GetComponentInChildren<Text>(),
                    LedBtn = ledBtn,
                    CatBadgeText = catPill.GetComponentInChildren<Text>(),
                    NameText = nameTxt,
                    LeftBtn = lBtn,
                    RightBtn = rBtn,
                    UpBtn = uBtn,
                    DownBtn = dBtn,
                    OffsetReadoutText = offTxt,
                    ResetBtn = resBtn
                });
            }
        }

        private void EnsureTelemRows(int count)
        {
            if (_drawerTelemContent == null) return;
            while (_telemRowsPool.Count < count)
            {
                GameObject row = CreateRowLayout(_drawerTelemContent, 22f);
                var rLe = row.AddComponent<LayoutElement>();
                rLe.minHeight = 22f;
                rLe.preferredHeight = 22f;
                rLe.flexibleWidth = 1f;

                var ledBtn = CreateToolbarButton(row.transform, "Led", "●", new Vector2(18f, 18f), null);
                var catPill = CreatePill(row.transform, "Cat", "数显", WorkbenchStyleEngine.ColorAccentPrimary, 9);
                catPill.GetComponent<LayoutElement>().preferredWidth = 34f;

                var nameTxt = CreateCrispText(row.transform, "Name", "", 10, TextAnchor.MiddleLeft, Color.white);
                nameTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 75f;

                var inputObj = CreateInputField(row.transform, "TokenIn", new Vector2(100f, 20f));
                var searchBtn = CreateToolbarButton(row.transform, "Search", "🔍", new Vector2(24f, 20f), null);

                var sampleTxt = CreateCrispText(row.transform, "Sample", "---", 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorSuccess);
                sampleTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

                var resBtn = CreateToolbarButton(row.transform, "Res", "↺", new Vector2(18f, 18f), null);

                _telemRowsPool.Add(new TelemetryBindRowUI
                {
                    Root = row,
                    LedText = ledBtn.GetComponentInChildren<Text>(),
                    LedBtn = ledBtn,
                    CatBadgeText = catPill.GetComponentInChildren<Text>(),
                    NameText = nameTxt,
                    TokenInput = inputObj.GetComponent<InputField>(),
                    SearchBtn = searchBtn,
                    SampleText = sampleTxt,
                    ResetBtn = resBtn
                });
            }
        }

        private void EnsureChanRows(int count)
        {
            if (_drawerChanContent == null) return;
            while (_chanRowsPool.Count < count)
            {
                GameObject row = CreateRowLayout(_drawerChanContent, 24f);
                var rLe = row.AddComponent<LayoutElement>();
                rLe.minHeight = 24f;
                rLe.preferredHeight = 24f;
                rLe.flexibleWidth = 1f;

                var nameTxt = CreateCrispText(row.transform, "Name", "CH1", 10, TextAnchor.MiddleLeft, Color.white);
                nameTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 85f;

                var inputObj = CreateInputField(row.transform, "TokenIn", new Vector2(120f, 20f));
                var searchBtn = CreateToolbarButton(row.transform, "Search", "🔍", new Vector2(24f, 20f), null);

                var sampleTxt = CreateCrispText(row.transform, "Sample", "---", 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorSuccess);
                sampleTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

                var resBtn = CreateToolbarButton(row.transform, "Res", "↺", new Vector2(20f, 20f), null);

                _chanRowsPool.Add(new ChannelRowUI
                {
                    Root = row,
                    NameText = nameTxt,
                    TokenInput = inputObj.GetComponent<InputField>(),
                    SearchBtn = searchBtn,
                    SampleText = sampleTxt,
                    ResetBtn = resBtn
                });
            }
        }

        private void EnsureLayerRows(int count)
        {
            if (_layerDrawerContent == null) return;
            while (_layerRowsPool.Count < count)
            {
                GameObject row = CreateRowLayout(_layerDrawerContent, 22f);
                var rLe = row.AddComponent<LayoutElement>();
                rLe.minHeight = 22f;
                rLe.preferredHeight = 22f;
                rLe.flexibleWidth = 1f;

                var rowImg = row.AddComponent<Image>();
                rowImg.color = new Color(0.06f, 0.09f, 0.14f, 0.65f);
                rowImg.raycastTarget = true;

                var eyeBtn = CreateToolbarButton(row.transform, "Eye", "👁", new Vector2(22f, 20f), null);
                var lockBtn = CreateToolbarButton(row.transform, "Lock", "🔓", new Vector2(22f, 20f), null);

                var layerBadge = CreateCrispText(row.transform, "LayerBadge", "#1", 10, TextAnchor.MiddleCenter, WorkbenchStyleEngine.ColorAccentPrimary);
                layerBadge.gameObject.AddComponent<LayoutElement>().preferredWidth = 28f;

                var selBtn = CreateToolbarButton(row.transform, "Select", string.Empty, Vector2.zero, null, false, null, 10);
                selBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
                var nameTxt = selBtn.GetComponentInChildren<Text>();
                nameTxt.alignment = TextAnchor.MiddleLeft;

                _layerRowsPool.Add(new LayerRowUI
                {
                    Root = row,
                    RowBgImage = rowImg,
                    SelectBtn = selBtn,
                    EyeText = eyeBtn.GetComponentInChildren<Text>(),
                    EyeBtn = eyeBtn,
                    LockText = lockBtn.GetComponentInChildren<Text>(),
                    LockBtn = lockBtn,
                    LayerBadgeText = layerBadge,
                    NameText = nameTxt
                });
            }
        }

        private static GameObject CreateGlassPanel(Transform parent, string name, Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 pos, bool isCard = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.material = isCard ? WorkbenchStyleEngine.GetCardMaterial(false) : WorkbenchStyleEngine.GetWindowGlassMaterial();
            img.color = isCard ? WorkbenchStyleEngine.ColorCardBg : WorkbenchStyleEngine.ColorWindowBg;
            img.raycastTarget = true;

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
            hl.spacing = 4f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            return go;
        }

        private static Button CreateToolbarButton(Transform parent, string name, string label, Vector2 size, Action onClick, bool isPrimary = false, Color? customTextColor = null, int fontSize = 11)
        {
            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(ModernButtonEffect), typeof(LayoutElement));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            LayoutElement le = btnObj.GetComponent<LayoutElement>();
            if (size.x > 0) { le.minWidth = size.x; le.preferredWidth = size.x; }
            if (size.y > 0) { le.minHeight = size.y; le.preferredHeight = size.y; }
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;

            Image img = btnObj.GetComponent<Image>();
            img.type = Image.Type.Simple;
            img.material = WorkbenchStyleEngine.GetButtonMaterial(isPrimary, false);
            img.color = isPrimary ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;
            img.raycastTarget = true;

            Button btn = btnObj.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            GameObject txtObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            txtRt.offsetMin = new Vector2(2f, 0f);
            txtRt.offsetMax = new Vector2(-2f, 0f);

            Text txt = txtObj.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = customTextColor ?? (isPrimary ? WorkbenchStyleEngine.ColorTextPrimary : WorkbenchStyleEngine.ColorTextAccent);
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = label;
            txt.raycastTarget = false;

            ModernButtonEffect effect = btnObj.GetComponent<ModernButtonEffect>();
            effect.IsPrimary = isPrimary;
            effect.ButtonImage = img;
            effect.LabelText = txt;

            return btn;
        }

        private static GameObject CreatePill(Transform parent, string name, string labelText, Color accentColor, int fontSize = 11)
        {
            GameObject pillObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            pillObj.transform.SetParent(parent, false);

            float pillWidth = Mathf.Max(48f, labelText.Length * 8f + 20f);
            RectTransform rt = pillObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(pillWidth, 24f);

            var le = pillObj.GetComponent<LayoutElement>();
            le.preferredWidth = pillWidth;
            le.minWidth = pillWidth;
            le.preferredHeight = 24f;
            le.minHeight = 24f;
            le.flexibleWidth = 0f;

            Image img = pillObj.GetComponent<Image>();
            img.material = WorkbenchStyleEngine.GetPillDockMaterial(true);
            img.color = WorkbenchStyleEngine.ColorPillDarkBg;

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(pillObj.transform, false);

            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            Text txt = txtObj.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = labelText;
            txt.raycastTarget = false;

            return pillObj;
        }

        private static GameObject CreateInputField(Transform parent, string name, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var le = go.GetComponent<LayoutElement>();
            if (size.x > 0) { le.minWidth = size.x; le.preferredWidth = size.x; }
            if (size.y > 0) { le.minHeight = size.y; le.preferredHeight = size.y; }
            le.flexibleWidth = 0;

            var img = go.GetComponent<Image>();
            img.material = WorkbenchStyleEngine.GetCardMaterial(false);
            img.color = WorkbenchStyleEngine.ColorCardBg;

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtObj.transform.SetParent(go.transform, false);
            var txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(4f, 1f);
            txtRt.offsetMax = new Vector2(-4f, -1f);

            var txt = txtObj.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = 10;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = WorkbenchStyleEngine.ColorTextPrimary;
            txt.supportRichText = false;

            var input = go.GetComponent<InputField>();
            input.textComponent = txt;

            go.AddComponent<ModernInputFieldFocusLock>();
            return go;
        }

        private static Text CreateCrispText(Transform parent, string name, string text, int fontSize, TextAnchor align, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text txt = go.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.material = WorkbenchStyleEngine.GetCrispTextMaterial();
            txt.text = text;
            txt.raycastTarget = false;

            return txt;
        }

        private static void CreateLabel(Transform parent, string text, float width, Color color)
        {
            var txt = CreateCrispText(parent, "Lbl", text, 10, TextAnchor.MiddleLeft, color);
            var le = txt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
        }

        private static void CreateDivider(Transform parent)
        {
            GameObject div = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            div.transform.SetParent(parent, false);

            var le = div.AddComponent<LayoutElement>();
            le.minWidth = 1f;
            le.preferredWidth = 1f;
            le.preferredHeight = 20f;
            le.minHeight = 20f;

            var img = div.GetComponent<Image>();
            img.color = new Color(0.25f, 0.35f, 0.50f, 0.5f);
            img.raycastTarget = false;
        }

        private static void CreateDividerLine(Transform parent)
        {
            GameObject line = new GameObject("DividerLine", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            line.transform.SetParent(parent, false);

            var le = line.AddComponent<LayoutElement>();
            le.minHeight = 1f;
            le.preferredHeight = 1f;
            le.flexibleWidth = 1f;

            var img = line.GetComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.45f, 0.5f);
            img.raycastTarget = false;
        }

        private static Transform CreateScrollView(Transform parent, float height)
        {
            GameObject scrollObj = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
            scrollObj.transform.SetParent(parent, false);

            var sLe = scrollObj.AddComponent<LayoutElement>();
            sLe.minHeight = height;
            sLe.preferredHeight = height;
            sLe.flexibleWidth = 1f;

            var sImg = scrollObj.GetComponent<Image>();
            sImg.color = new Color(0.02f, 0.03f, 0.05f, 0.7f);

            var mask = scrollObj.GetComponent<Mask>();
            mask.showMaskGraphic = true;

            var sr = scrollObj.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 25f;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObj.transform.SetParent(scrollObj.transform, false);

            var contentRt = contentObj.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;

            var cvl = contentObj.GetComponent<VerticalLayoutGroup>();
            cvl.childAlignment = TextAnchor.UpperLeft;
            cvl.spacing = 2f;
            cvl.padding = new RectOffset(4, 4, 4, 4);
            cvl.childForceExpandWidth = true;
            cvl.childForceExpandHeight = false;

            var csf = contentObj.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = contentRt;
            return contentRt;
        }

        private static void UpdateTabButtonHighlight(Button btn, bool isActive)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.material = WorkbenchStyleEngine.GetButtonMaterial(isActive, false);
                img.color = isActive ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;
            }
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.color = isActive ? WorkbenchStyleEngine.ColorAccentPrimary : WorkbenchStyleEngine.ColorTextPrimary;
            }
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
