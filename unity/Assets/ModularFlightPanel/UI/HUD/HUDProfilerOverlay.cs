using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Workbench;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 高精度性能探针现代悬浮诊断窗口 (HUD Profiler Modern Glass Overlay)
    /// 核心架构职责：
    /// 1. 全面采用新一代 GPU 现代暗晶玻璃着色器 (ModernWorkbenchGlass.shader) 与 WorkbenchStyleEngine 材质管线；
    /// 2. 100% 消除 Unity IMGUI OnGUI 引擎轮询开销与内存垃圾 (0 GC 运行)；
    /// 3. 支持自由拖拽移动 (WorkbenchWindowDragHandler) 与展开/折叠 (F10)；
    /// 4. 完整还原 F10 性能探针双 Tab 全套诊断功能：
    ///    - [📊 负载概览]: 6大子系统精准微秒采样、自适应静息节流统计、逐组件活跃耗时降序明细表 (含历史峰值与重置)；
    ///    - [⏱️ 更新时序轴]: 满帧/切片调度统计、动态水平时序甘特图 (Visual Timeline Bar)、切片预算红线、执行时序与图层明细表、实时跟踪与逐帧单步分析。
    /// </summary>
    public class HUDProfilerOverlay : MonoBehaviour
    {
        private GameObject _rootObj;
        private RectTransform _windowRt;

        // 顶栏拖拽与核心操作
        private RectTransform _headerRt;
        private Text _titleText;
        private Button _bypassBtn;
        private Text _bypassBtnText;
        private Button _expandBtn;
        private Text _expandBtnText;
        private Button _closeBtn;

        // 核心统计指标摘要行
        private Text _statusTimeText;
        private Text _statusFpsText;
        private Text _statusMemText;

        // 展开的详细诊断面板容器
        private GameObject _detailsContainer;
        private RectTransform _detailsContainerRt;
        private Button _tabOverviewBtn;
        private Button _tabTimelineBtn;
        private int _selectedTab = 0; // 0: 负载概览, 1: 更新时序轴
        private bool _isExpanded = true;

        // Tab 0: 负载概览
        private GameObject _overviewPanel;
        private Text _telemetryText;
        private Text _probesText;
        private Text _widgetsText;
        private Text _silhouetteText;
        private Text _hooksText;
        private Text _quiescentText;
        private Text _breakdownTitleText;
        private Button _resetPeakBtn;
        private Transform _breakdownScrollContent;
        private readonly List<BreakdownRowUI> _breakdownRows = new List<BreakdownRowUI>();

        // Tab 1: 更新时序轴
        private GameObject _timelinePanel;
        private Text _timelineStatusText;
        private Button _timelineFreezeBtn;
        private Text _timelineFreezeBtnText;
        private Button _timelineStepBtn;
        private Text _timelineSummaryText;
        private RectTransform _timelineBarContainer;
        private RectTransform _budgetLineRt;
        private Text _timelineRulerLeft;
        private Text _timelineRulerMid;
        private Text _timelineRulerRight;
        private Transform _timelineScrollContent;
        private readonly List<RectTransform> _ganttBlocks = new List<RectTransform>();
        private readonly List<TimelineRowUI> _timelineRows = new List<TimelineRowUI>();

        // 刷新节流计时器 (10Hz 定频刷新，零开销保证)
        private float _updateTimer = 0f;
        private const float UpdateInterval = 0.10f;

        private class BreakdownRowUI
        {
            public GameObject Root;
            public Text NameText;
            public Text AvgText;
            public Text LastMaxText;
        }

        private class TimelineRowUI
        {
            public GameObject Root;
            public Text SeqText;
            public Text LayerText;
            public Text TierText;
            public Text NameText;
            public Text TimingText;
            public Text DurationText;
        }

        private void Awake()
        {
            enabled = false; // 默认严格休眠
        }

        private void OnEnable()
        {
            EnsureUIHierarchy();
            if (_rootObj != null) _rootObj.SetActive(true);
            RefreshOverlayData();
        }

        private void OnDisable()
        {
            if (_rootObj != null) _rootObj.SetActive(false);
            FlightHUDManager.IsMouseOverFloatingToolbar = false;
            MFPInputLock.SetWindowHoverLock(false);
        }

        private void Update()
        {
            if (!MFPProfiler.ShowOverlay)
            {
                if (_rootObj != null && _rootObj.activeSelf) _rootObj.SetActive(false);
                return;
            }

            if (_rootObj != null && !_rootObj.activeSelf) _rootObj.SetActive(true);

            _updateTimer += Time.unscaledDeltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                RefreshOverlayData();
            }
        }

        private void EnsureUIHierarchy()
        {
            if (_rootObj != null) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FlightHUDManager.Instance?.Canvas;
            }
            if (canvas == null) return;

            // 1. 创建主窗体磨砂底盘
            _rootObj = new GameObject("MFP_HUDProfilerWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _rootObj.transform.SetParent(canvas.transform, false);

            _windowRt = _rootObj.GetComponent<RectTransform>();
            _windowRt.anchorMin = new Vector2(0f, 1f);
            _windowRt.anchorMax = new Vector2(0f, 1f);
            _windowRt.pivot = new Vector2(0f, 1f);
            _windowRt.anchoredPosition = new Vector2(20f, -20f);
            _windowRt.sizeDelta = new Vector2(460f, _isExpanded ? 520f : 68f);

            var winImg = _rootObj.GetComponent<Image>();
            winImg.material = WorkbenchStyleEngine.GetWindowGlassMaterial();
            winImg.color = WorkbenchStyleEngine.ColorWindowBg;
            winImg.raycastTarget = true;

            AddHoverCatcher(_rootObj);

            var rootVl = _rootObj.AddComponent<VerticalLayoutGroup>();
            rootVl.childAlignment = TextAnchor.UpperLeft;
            rootVl.spacing = 4f;
            rootVl.padding = new RectOffset(8, 8, 6, 6);
            rootVl.childForceExpandWidth = true;
            rootVl.childForceExpandHeight = false;

            // 2. 顶栏标题与操作手柄
            BuildHeader(_rootObj.transform);

            // 3. 核心统计摘要行
            BuildStatusBar(_rootObj.transform);

            // 4. 详细诊断面板 (可折叠)
            BuildDetailsContainer(_rootObj.transform);
        }

        private void BuildHeader(Transform parent)
        {
            GameObject headerObj = new GameObject("HeaderBar", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image));
            headerObj.transform.SetParent(parent, false);

            _headerRt = headerObj.GetComponent<RectTransform>();
            var le = headerObj.AddComponent<LayoutElement>();
            le.minHeight = 28f;
            le.preferredHeight = 28f;
            le.flexibleWidth = 1f;

            var hImg = headerObj.GetComponent<Image>();
            hImg.color = new Color(0.1f, 0.15f, 0.22f, 0.6f);
            hImg.raycastTarget = true;

            var dragHandler = headerObj.AddComponent<WorkbenchWindowDragHandler>();
            dragHandler.TargetWindow = _windowRt;

            var hl = headerObj.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 6f;
            hl.padding = new RectOffset(8, 4, 2, 2);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // 标题
            _titleText = CreateCrispText(headerObj.transform, "Title", $"🛠️ <b>{I18n.Tr("PROF_WINDOW_TITLE", "MFP 航电性能探针")}</b>", 12, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorAccentPrimary);
            var titleLe = _titleText.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;

            // 完全旁路按钮
            _bypassBtn = CreateToolbarButton(headerObj.transform, "BypassBtn", I18n.Tr("PROF_BTN_BYPASS", "⏸ 完全旁路 (F11)"), new Vector2(110f, 22f), () =>
            {
                MFPProfiler.ToggleMasterBypass();
                RefreshOverlayData();
            }, false, WorkbenchStyleEngine.ColorWarning, 10);
            _bypassBtnText = _bypassBtn.GetComponentInChildren<Text>();

            // 详情展开/收起
            _expandBtn = CreateToolbarButton(headerObj.transform, "ExpandBtn", _isExpanded ? I18n.Tr("PROF_BTN_COLLAPSE", "▲ 收起") : I18n.Tr("PROF_BTN_DETAILS", "▼ 详情"), new Vector2(56f, 22f), () =>
            {
                _isExpanded = !_isExpanded;
                _windowRt.sizeDelta = new Vector2(460f, _isExpanded ? 520f : 68f);
                if (_detailsContainer != null) _detailsContainer.SetActive(_isExpanded && !MFPProfiler.IsMasterBypassed);
                if (_expandBtnText != null) _expandBtnText.text = _isExpanded ? I18n.Tr("PROF_BTN_COLLAPSE", "▲ 收起") : I18n.Tr("PROF_BTN_DETAILS", "▼ 详情");
            }, false, WorkbenchStyleEngine.ColorTextPrimary, 10);
            _expandBtnText = _expandBtn.GetComponentInChildren<Text>();

            // 关闭按钮
            _closeBtn = CreateToolbarButton(headerObj.transform, "CloseBtn", "✕", new Vector2(24f, 22f), () =>
            {
                MFPProfiler.ShowOverlay = false;
            }, false, WorkbenchStyleEngine.ColorTextMuted, 11);
        }

        private void BuildStatusBar(Transform parent)
        {
            GameObject statusObj = new GameObject("StatusBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            statusObj.transform.SetParent(parent, false);

            var le = statusObj.AddComponent<LayoutElement>();
            le.minHeight = 22f;
            le.preferredHeight = 22f;
            le.flexibleWidth = 1f;

            var hl = statusObj.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 8f;
            hl.padding = new RectOffset(6, 6, 0, 0);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            _statusTimeText = CreateCrispText(statusObj.transform, "Time", $"{I18n.Tr("PROF_TIME_COST", "MFP 耗时:")} 0.00 ms", 11, TextAnchor.MiddleLeft, Color.white);
            var tLe = _statusTimeText.gameObject.AddComponent<LayoutElement>();
            tLe.flexibleWidth = 1f;

            _statusFpsText = CreateCrispText(statusObj.transform, "Fps", "FPS: 60", 11, TextAnchor.MiddleCenter, WorkbenchStyleEngine.ColorTextAccent);
            var fLe = _statusFpsText.gameObject.AddComponent<LayoutElement>();
            fLe.minWidth = 65f;
            fLe.preferredWidth = 65f;

            _statusMemText = CreateCrispText(statusObj.transform, "Mem", "-- MB", 10, TextAnchor.MiddleRight, WorkbenchStyleEngine.ColorTextMuted);
            var mLe = _statusMemText.gameObject.AddComponent<LayoutElement>();
            mLe.minWidth = 135f;
            mLe.preferredWidth = 135f;
        }

        private void BuildDetailsContainer(Transform parent)
        {
            _detailsContainer = new GameObject("DetailsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _detailsContainer.transform.SetParent(parent, false);

            _detailsContainerRt = _detailsContainer.GetComponent<RectTransform>();
            var le = _detailsContainer.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;

            var vl = _detailsContainer.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 4f;
            vl.padding = new RectOffset(0, 0, 2, 2);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 1. 分割线
            CreateDividerLine(_detailsContainer.transform);

            // 2. Tab 切换栏: [📊 负载概览] | [⏱️ 更新时序轴]
            GameObject tabBar = new GameObject("TabBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabBar.transform.SetParent(_detailsContainer.transform, false);

            var tbLe = tabBar.AddComponent<LayoutElement>();
            tbLe.minHeight = 24f;
            tbLe.preferredHeight = 24f;

            var tbHl = tabBar.GetComponent<HorizontalLayoutGroup>();
            tbHl.childAlignment = TextAnchor.MiddleCenter;
            tbHl.spacing = 8f;
            tbHl.childForceExpandWidth = true;
            tbHl.childForceExpandHeight = true;

            _tabOverviewBtn = CreateToolbarButton(tabBar.transform, "TabOverview", I18n.Tr("PROF_TAB_OVERVIEW", "📊 负载概览"), Vector2.zero, () => SwitchTab(0), _selectedTab == 0, null, 11);
            _tabOverviewBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _tabTimelineBtn = CreateToolbarButton(tabBar.transform, "TabTimeline", I18n.Tr("PROF_TAB_TIMELINE", "⏱️ 更新时序轴"), Vector2.zero, () => SwitchTab(1), _selectedTab == 1, null, 11);
            _tabTimelineBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // 3. 构建负载概览面板
            BuildOverviewPanel(_detailsContainer.transform);

            // 4. 构建更新时序轴面板
            BuildTimelinePanel(_detailsContainer.transform);

            _detailsContainer.SetActive(_isExpanded && !MFPProfiler.IsMasterBypassed);
            SwitchTab(_selectedTab);
        }

        private void SwitchTab(int tabIndex)
        {
            _selectedTab = tabIndex;
            if (_overviewPanel != null) _overviewPanel.SetActive(_selectedTab == 0);
            if (_timelinePanel != null) _timelinePanel.SetActive(_selectedTab == 1);

            UpdateTabButtonHighlight(_tabOverviewBtn, _selectedTab == 0);
            UpdateTabButtonHighlight(_tabTimelineBtn, _selectedTab == 1);
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

        #region Tab 0: 负载概览构建
        private void BuildOverviewPanel(Transform parent)
        {
            _overviewPanel = new GameObject("OverviewPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _overviewPanel.transform.SetParent(parent, false);

            var vl = _overviewPanel.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(4, 4, 4, 4);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 6大子系统耗时指标行
            _telemetryText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_TELEMETRY", "遥测核心:"));
            _probesText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_PROBES", "外部探针 (FAR/RA/MJ):"));
            _widgetsText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_WIDGETS", "组件管线呈现:"));
            _silhouetteText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_SILHOUETTE", "飞船剪影烘焙:"));
            _hooksText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_HOOKS", "原版界面挂钩:"));
            _quiescentText = CreateStatRow(_overviewPanel.transform, I18n.Tr("PROF_ROW_QUIESCENCE", "自适应静息节流:"));

            CreateDividerLine(_overviewPanel.transform);

            // 组件耗时明细表头
            GameObject bdHeader = new GameObject("BreakdownHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bdHeader.transform.SetParent(_overviewPanel.transform, false);
            var bdhLe = bdHeader.AddComponent<LayoutElement>();
            bdhLe.minHeight = 22f;
            bdhLe.preferredHeight = 22f;

            var bdhHl = bdHeader.GetComponent<HorizontalLayoutGroup>();
            bdhHl.childAlignment = TextAnchor.MiddleLeft;
            bdhHl.spacing = 4f;
            bdhHl.childForceExpandWidth = false;
            bdhHl.childForceExpandHeight = false;

            _breakdownTitleText = CreateCrispText(bdHeader.transform, "Title", $"<b>{I18n.Tr("PROF_WIDGET_BREAKDOWN", "组件耗时明细 (Widget Breakdown)")}</b>", 11, TextAnchor.MiddleLeft, Color.white);
            var btLe = _breakdownTitleText.gameObject.AddComponent<LayoutElement>();
            btLe.flexibleWidth = 1f;

            _resetPeakBtn = CreateToolbarButton(bdHeader.transform, "ResetPeakBtn", I18n.Tr("PROF_BTN_RESET_PEAK", "重置峰值"), new Vector2(68f, 20f), () =>
            {
                MFPProfiler.ResetPeakStats();
                RefreshOverlayData();
            }, false, WorkbenchStyleEngine.ColorTextAccent, 10);

            // 表头栏
            GameObject colHeader = new GameObject("ColHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            colHeader.transform.SetParent(_overviewPanel.transform, false);
            var chLe = colHeader.AddComponent<LayoutElement>();
            chLe.minHeight = 18f;
            chLe.preferredHeight = 18f;

            var chHl = colHeader.GetComponent<HorizontalLayoutGroup>();
            chHl.childAlignment = TextAnchor.MiddleLeft;
            chHl.spacing = 4f;
            chHl.childForceExpandWidth = false;
            chHl.childForceExpandHeight = false;

            var c1 = CreateCrispText(colHeader.transform, "C1", I18n.Tr("PROF_COL_NAME", "组件名称 / ID"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            c1.gameObject.AddComponent<LayoutElement>().preferredWidth = 210f;
            var c2 = CreateCrispText(colHeader.transform, "C2", I18n.Tr("PROF_COL_AVG", "均值"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            c2.gameObject.AddComponent<LayoutElement>().preferredWidth = 85f;
            var c3 = CreateCrispText(colHeader.transform, "C3", I18n.Tr("PROF_COL_LAST", "单次 / 峰值"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            c3.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // 滚动列表
            _breakdownScrollContent = CreateScrollView(_overviewPanel.transform, 175f);
        }

        private Text CreateStatRow(Transform parent, string label)
        {
            GameObject row = new GameObject("StatRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 18f;
            le.preferredHeight = 18f;

            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 6f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            var lbl = CreateCrispText(row.transform, "Lbl", label, 11, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            var lblLe = lbl.gameObject.AddComponent<LayoutElement>();
            lblLe.minWidth = 180f;
            lblLe.preferredWidth = 180f;

            var val = CreateCrispText(row.transform, "Val", "0.000 ms", 11, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorAccentPrimary);
            var valLe = val.gameObject.AddComponent<LayoutElement>();
            valLe.flexibleWidth = 1f;

            return val;
        }
        #endregion

        #region Tab 1: 更新时序轴构建
        private void BuildTimelinePanel(Transform parent)
        {
            _timelinePanel = new GameObject("TimelinePanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _timelinePanel.transform.SetParent(parent, false);

            var vl = _timelinePanel.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.spacing = 3f;
            vl.padding = new RectOffset(4, 4, 4, 4);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // 1. 状态与冻结控制栏
            GameObject ctrlRow = new GameObject("CtrlRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            ctrlRow.transform.SetParent(_timelinePanel.transform, false);
            var crLe = ctrlRow.AddComponent<LayoutElement>();
            crLe.minHeight = 22f;
            crLe.preferredHeight = 22f;

            var crHl = ctrlRow.GetComponent<HorizontalLayoutGroup>();
            crHl.childAlignment = TextAnchor.MiddleLeft;
            crHl.spacing = 6f;
            crHl.childForceExpandWidth = false;
            crHl.childForceExpandHeight = false;

            _timelineStatusText = CreateCrispText(ctrlRow.transform, "Status", $"<b>{I18n.Tr("PROF_TIMELINE_TITLE", "UI 更新时序与切片调度")}</b> <color=#00E5FF>{I18n.Tr("PROF_STATUS_LIVE", "[实时跟踪]")}</color>", 11, TextAnchor.MiddleLeft, Color.white);
            var stLe = _timelineStatusText.gameObject.AddComponent<LayoutElement>();
            stLe.flexibleWidth = 1f;

            _timelineFreezeBtn = CreateToolbarButton(ctrlRow.transform, "FreezeBtn", I18n.Tr("PROF_BTN_FREEZE", "⏸ 冻结"), new Vector2(56f, 20f), () =>
            {
                MFPProfiler.IsTimelineFrozen = !MFPProfiler.IsTimelineFrozen;
                if (_timelineFreezeBtnText != null) _timelineFreezeBtnText.text = MFPProfiler.IsTimelineFrozen ? I18n.Tr("PROF_BTN_LIVE", "▶ 跟踪") : I18n.Tr("PROF_BTN_FREEZE", "⏸ 冻结");
                if (_timelineStepBtn != null) _timelineStepBtn.gameObject.SetActive(MFPProfiler.IsTimelineFrozen);
                RefreshOverlayData();
            }, false, WorkbenchStyleEngine.ColorWarning, 10);
            _timelineFreezeBtnText = _timelineFreezeBtn.GetComponentInChildren<Text>();

            _timelineStepBtn = CreateToolbarButton(ctrlRow.transform, "StepBtn", I18n.Tr("PROF_BTN_STEP", "↺ 单步"), new Vector2(48f, 20f), () =>
            {
                MFPProfiler.CaptureSingleStepSnapshot();
                RefreshOverlayData();
            }, false, WorkbenchStyleEngine.ColorAccentPrimary, 10);
            _timelineStepBtn.gameObject.SetActive(MFPProfiler.IsTimelineFrozen);

            // 2. 切片调度与预算摘要
            GameObject sumRow = new GameObject("SummaryRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            sumRow.transform.SetParent(_timelinePanel.transform, false);
            var srLe = sumRow.AddComponent<LayoutElement>();
            srLe.minHeight = 18f;
            srLe.preferredHeight = 18f;

            _timelineSummaryText = CreateCrispText(sumRow.transform, "Summary", string.Empty, 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            _timelineSummaryText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // 3. 水平时序甘特图容器 (Visual Timeline Bar)
            GameObject barObj = new GameObject("GanttBarContainer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barObj.transform.SetParent(_timelinePanel.transform, false);
            _timelineBarContainer = barObj.GetComponent<RectTransform>();
            var bLe = barObj.AddComponent<LayoutElement>();
            bLe.minHeight = 24f;
            bLe.preferredHeight = 24f;
            bLe.flexibleWidth = 1f;

            var barImg = barObj.GetComponent<Image>();
            barImg.color = new Color(0.04f, 0.06f, 0.10f, 0.95f);
            barImg.raycastTarget = false;

            // 切片预算红线
            GameObject budgetLineObj = new GameObject("BudgetLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            budgetLineObj.transform.SetParent(barObj.transform, false);
            _budgetLineRt = budgetLineObj.GetComponent<RectTransform>();
            _budgetLineRt.anchorMin = new Vector2(0f, 0f);
            _budgetLineRt.anchorMax = new Vector2(0f, 1f);
            _budgetLineRt.pivot = new Vector2(0.5f, 0.5f);
            _budgetLineRt.sizeDelta = new Vector2(2f, 0f);
            var blImg = budgetLineObj.GetComponent<Image>();
            blImg.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            blImg.raycastTarget = false;

            // 标尺读数行
            GameObject rulerRow = new GameObject("RulerRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rulerRow.transform.SetParent(_timelinePanel.transform, false);
            var rrLe = rulerRow.AddComponent<LayoutElement>();
            rrLe.minHeight = 16f;
            rrLe.preferredHeight = 16f;

            var rrHl = rulerRow.GetComponent<HorizontalLayoutGroup>();
            rrHl.childAlignment = TextAnchor.MiddleLeft;
            rrHl.childForceExpandWidth = false;
            rrHl.childForceExpandHeight = false;

            _timelineRulerLeft = CreateCrispText(rulerRow.transform, "RL", "0.00ms", 9, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            _timelineRulerLeft.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            _timelineRulerMid = CreateCrispText(rulerRow.transform, "RM", string.Empty, 9, TextAnchor.MiddleCenter, WorkbenchStyleEngine.ColorDanger);
            _timelineRulerMid.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _timelineRulerRight = CreateCrispText(rulerRow.transform, "RR", "0.20ms", 9, TextAnchor.MiddleRight, WorkbenchStyleEngine.ColorTextMuted);
            _timelineRulerRight.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            // 4. 时序执行表头
            GameObject tColHeader = new GameObject("TimelineColHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tColHeader.transform.SetParent(_timelinePanel.transform, false);
            var tchLe = tColHeader.AddComponent<LayoutElement>();
            tchLe.minHeight = 18f;
            tchLe.preferredHeight = 18f;

            var tchHl = tColHeader.GetComponent<HorizontalLayoutGroup>();
            tchHl.childAlignment = TextAnchor.MiddleLeft;
            tchHl.spacing = 4f;
            tchHl.childForceExpandWidth = false;
            tchHl.childForceExpandHeight = false;

            var tc1 = CreateCrispText(tColHeader.transform, "TC1", I18n.Tr("PROF_COL_SEQ", "#"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc1.gameObject.AddComponent<LayoutElement>().preferredWidth = 26f;
            var tc2 = CreateCrispText(tColHeader.transform, "TC2", I18n.Tr("PROF_COL_LAYER", "图层"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc2.gameObject.AddComponent<LayoutElement>().preferredWidth = 38f;
            var tc3 = CreateCrispText(tColHeader.transform, "TC3", I18n.Tr("PROF_COL_TIER", "阶梯"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc3.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;
            var tc4 = CreateCrispText(tColHeader.transform, "TC4", I18n.Tr("PROF_COL_NAME", "组件名称 / ID"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc4.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            var tc5 = CreateCrispText(tColHeader.transform, "TC5", I18n.Tr("PROF_COL_TIMING", "时序区间"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc5.gameObject.AddComponent<LayoutElement>().preferredWidth = 75f;
            var tc6 = CreateCrispText(tColHeader.transform, "TC6", I18n.Tr("PROF_COL_DURATION", "耗时"), 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
            tc6.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // 5. 时序执行列表
            _timelineScrollContent = CreateScrollView(_timelinePanel.transform, 175f);
        }
        #endregion

        #region 数据刷新核心 (0 GC 运行时更新)
        private void RefreshOverlayData()
        {
            if (_rootObj == null || !_rootObj.activeSelf) return;

            bool bypassed = MFPProfiler.IsMasterBypassed;

            // 1. 刷新顶栏旁路状态
            if (_bypassBtnText != null)
            {
                _bypassBtnText.text = bypassed ? I18n.Tr("PROF_BTN_RESUME", "▶ 恢复 MFP") : I18n.Tr("PROF_BTN_BYPASS", "⏸ 完全旁路 (F11)");
            }
            if (_titleText != null)
            {
                _titleText.color = bypassed ? WorkbenchStyleEngine.ColorDanger : WorkbenchStyleEngine.ColorAccentPrimary;
            }

            // 2. 刷新摘要状态
            if (_statusTimeText != null)
            {
                if (bypassed)
                {
                    _statusTimeText.text = $"<b>{I18n.Tr("PROF_TIME_COST", "MFP 耗时:")}</b> <color=#FF3B30><b>[0.00ms]</b></color>";
                }
                else
                {
                    double avgMs = MFPProfiler.AvgTotalMs;
                    string statusColor = avgMs < 0.5 ? "#00E5FF" : (avgMs < 1.5 ? "#FFCC00" : "#FF3B30");
                    _statusTimeText.text = $"<b>{I18n.Tr("PROF_TIME_COST", "MFP 耗时:")}</b> <color={statusColor}><b>{avgMs:F2} ms</b></color> ({MFPProfiler.FrameBudgetPercent:F1}%)";
                }
            }

            if (_statusFpsText != null)
            {
                _statusFpsText.text = $"FPS: {MFPProfiler.CurrentFPS:F0}";
            }

            if (_statusMemText != null)
            {
                _statusMemText.text = $"{MFPProfiler.TotalMemoryMB:F1} MB ({MFPProfiler.Gc0Collections}/{MFPProfiler.Gc1Collections}/{MFPProfiler.Gc2Collections})";
            }

            if (bypassed)
            {
                if (_detailsContainer != null && _detailsContainer.activeSelf) _detailsContainer.SetActive(false);
                return;
            }
            else
            {
                if (_detailsContainer != null && _detailsContainer.activeSelf != _isExpanded) _detailsContainer.SetActive(_isExpanded);
            }

            if (!_isExpanded) return;

            // 3. 刷新展开的 Tab 内容
            if (_selectedTab == 0)
            {
                RefreshOverviewTab();
            }
            else
            {
                RefreshTimelineTab();
            }
        }

        private void RefreshOverviewTab()
        {
            if (_telemetryText != null) _telemetryText.text = FormatMsColor(MFPProfiler.AvgTelemetryMs);
            if (_probesText != null) _probesText.text = FormatMsColor(MFPProfiler.AvgProbesMs);
            if (_widgetsText != null) _widgetsText.text = FormatMsColor(MFPProfiler.AvgWidgetsMs);
            if (_silhouetteText != null) _silhouetteText.text = FormatMsColor(MFPProfiler.AvgSilhouetteMs);
            if (_hooksText != null) _hooksText.text = FormatMsColor(MFPProfiler.AvgHooksMs);

            if (_quiescentText != null)
            {
                int totalActive = Math.Max(1, MFPProfiler.ActiveWidgetCount);
                int resting = MFPProfiler.RestingWidgetCount;
                double restingPct = (double)resting / totalActive * 100.0;
                string restColor = resting > 0 ? "#00E5FF" : "#888888";
                _quiescentText.text = $"<color={restColor}><b>{resting} / {totalActive} ({restingPct:F0}%)</b></color>";
            }

            var profiles = MFPProfiler.ActiveWidgetProfiles;
            int count = profiles != null ? profiles.Count : 0;
            if (_breakdownTitleText != null)
            {
                _breakdownTitleText.text = $"<b>{I18n.Tr("PROF_WIDGET_BREAKDOWN", "组件耗时明细 (Widget Breakdown)")}</b> ({count})";
            }

            // 同步池化行
            EnsureBreakdownRows(count);
            for (int i = 0; i < count; i++)
            {
                var p = profiles[i];
                var row = _breakdownRows[i];
                row.Root.SetActive(true);

                string name = !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : p.WidgetId;
                if (p.IsResting) name += " <color=#00E5FF>[IDLE]</color>";
                row.NameText.text = name;

                double frameMs = Math.Max(0.0, p.FrameAvgMs);
                string col = frameMs < 0.05 ? "#00E5FF" : (frameMs < 0.2 ? "#34C759" : (frameMs < 0.5 ? "#FFE000" : "#FF5555"));
                row.AvgText.text = $"<color={col}><b>{frameMs:F3} ms</b></color>";

                row.LastMaxText.text = $"{p.AvgMs:F2} / {p.MaxMs:F2}";
            }

            for (int i = count; i < _breakdownRows.Count; i++)
            {
                _breakdownRows[i].Root.SetActive(false);
            }
        }

        private void RefreshTimelineTab()
        {
            if (_timelineStatusText != null)
            {
                string tag = MFPProfiler.IsTimelineFrozen
                    ? $"<color=#FFCC00><b>{I18n.Tr("PROF_STATUS_FROZEN", "[已冻结]")}</b></color>"
                    : $"<color=#00E5FF><b>{I18n.Tr("PROF_STATUS_LIVE", "[实时跟踪]")}</b></color>";
                _timelineStatusText.text = $"<b>{I18n.Tr("PROF_TIMELINE_TITLE", "UI 更新时序与切片调度")}</b> {tag}";
            }

            var timeline = MFPProfiler.SnapshotTimeline;
            int executedCount = timeline != null ? timeline.Count : 0;

            if (_timelineSummaryText != null)
            {
                _timelineSummaryText.text =
                    $"<color=#AAAAAA>{I18n.Tr("PROF_STAGE1_CRIT", "阶段1 满帧:")}</color> <color=#00E5FF>{MFPProfiler.SnapshotCritCount}</color>  " +
                    $"<color=#AAAAAA>{I18n.Tr("PROF_STAGE2_SLICE", "阶段2 切片:")}</color> <color=#34C759>{Math.Max(0, executedCount - MFPProfiler.SnapshotCritCount)}</color>/{MFPProfiler.SnapshotNonCritCount}  " +
                    $"<color=#AAAAAA>{I18n.Tr("PROF_CURSOR_INDEX", "游标:")}</color> <color=#FFCC00>#{MFPProfiler.SnapshotSliceCursor}</color>  " +
                    $"<color=#AAAAAA>{I18n.Tr("PROF_BUDGET_LIMIT", "预算:")}</color> <color=#FF8800>{MFPProfiler.SnapshotMaxBudgetMs:F2}ms</color>";
            }

            // 刷新甘特图 Bar
            double totalSpan = Math.Max(0.10, MFPProfiler.SnapshotTotalWidgetsMs > 0.001 ? MFPProfiler.SnapshotTotalWidgetsMs * 1.15 : 0.10);
            if (executedCount > 0)
            {
                double lastEnd = timeline[executedCount - 1].EndOffsetMs;
                if (lastEnd > totalSpan) totalSpan = lastEnd * 1.05;
            }

            if (_timelineRulerMid != null) _timelineRulerMid.text = $"| {I18n.Tr("PROF_BUDGET_LIMIT", "预算:")} {MFPProfiler.SnapshotMaxBudgetMs:F2}ms";
            if (_timelineRulerRight != null) _timelineRulerRight.text = $"{totalSpan:F2}ms";

            if (_timelineBarContainer != null)
            {
                float barW = _timelineBarContainer.rect.width > 10f ? _timelineBarContainer.rect.width : 440f;
                float usableW = barW - 4f;

                // 预算红线位置
                if (_budgetLineRt != null)
                {
                    if (MFPProfiler.SnapshotMaxBudgetMs > 0f && MFPProfiler.SnapshotMaxBudgetMs < totalSpan)
                    {
                        _budgetLineRt.gameObject.SetActive(true);
                        float budgetX = 2f + (float)(MFPProfiler.SnapshotMaxBudgetMs / totalSpan) * usableW;
                        _budgetLineRt.anchoredPosition = new Vector2(budgetX, 0f);
                    }
                    else
                    {
                        _budgetLineRt.gameObject.SetActive(false);
                    }
                }

                // 甘特方块池化更新
                EnsureGanttBlocks(executedCount);
                for (int i = 0; i < executedCount; i++)
                {
                    var entry = timeline[i];
                    var blkRt = _ganttBlocks[i];
                    blkRt.gameObject.SetActive(true);

                    float startX = 2f + (float)(entry.StartOffsetMs / totalSpan) * usableW;
                    float blockW = Mathf.Max(3f, (float)(entry.DurationMs / totalSpan) * usableW);
                    if (startX + blockW > barW - 2f)
                    {
                        blockW = Mathf.Max(2f, barW - 2f - startX);
                    }

                    blkRt.anchoredPosition = new Vector2(startX, 0f);
                    blkRt.sizeDelta = new Vector2(blockW, 0f);

                    var bImg = blkRt.GetComponent<Image>();
                    if (bImg != null)
                    {
                        bImg.color = MFPProfiler.GetTierColorValue(entry.Tier);
                    }

                    var bTxt = blkRt.GetComponentInChildren<Text>();
                    if (bTxt != null)
                    {
                        bTxt.text = blockW >= 20f ? $"#{entry.ExecutionIndex}" : "";
                    }
                }

                for (int i = executedCount; i < _ganttBlocks.Count; i++)
                {
                    _ganttBlocks[i].gameObject.SetActive(false);
                }
            }

            // 刷新时序明细列表
            EnsureTimelineRows(executedCount);
            for (int i = 0; i < executedCount; i++)
            {
                var entry = timeline[i];
                var row = _timelineRows[i];
                row.Root.SetActive(true);

                row.SeqText.text = entry.ExecutionIndex.ToString();

                string lCol = entry.DrawOrder < 3 ? "#00E5FF" : (entry.DrawOrder < 8 ? "#34C759" : "#FFCC00");
                row.LayerText.text = $"<color={lCol}>L{entry.DrawOrder}</color>";

                string tColor = MFPProfiler.GetTierColor(entry.Tier);
                string tName = MFPProfiler.GetTierShortName(entry.Tier);
                row.TierText.text = $"<color={tColor}>{tName}</color>";

                string name = !string.IsNullOrEmpty(entry.DisplayName) ? entry.DisplayName : entry.WidgetId;
                row.NameText.text = name;

                row.TimingText.text = $"+{entry.StartOffsetMs:F2}ms";

                string costCol = entry.DurationMs < 0.05 ? "#00E5FF" : (entry.DurationMs < 0.20 ? "#FFE000" : "#FF5555");
                string sliceTag = entry.WasSliced
                    ? $"<color=#34C759>{I18n.Tr("PROF_STATUS_SLICED", "[切片]")}</color>"
                    : $"<color=#00E5FF>{I18n.Tr("PROF_STATUS_FULL_PASS", "[直通]")}</color>";
                if (entry.IsResting) sliceTag += " <color=#00E5FF>[IDLE]</color>";
                row.DurationText.text = $"<color={costCol}><b>{entry.DurationMs:F3}</b></color> {sliceTag}";
            }

            for (int i = executedCount; i < _timelineRows.Count; i++)
            {
                _timelineRows[i].Root.SetActive(false);
            }
        }

        private static string FormatMsColor(double ms)
        {
            ms = Math.Max(0.0, ms);
            string col = ms < 0.2 ? "#00E5FF" : (ms < 0.8 ? "#FFE000" : "#FF5555");
            return $"<color={col}><b>{ms:F3} ms</b></color>";
        }
        #endregion

        #region 对象池与辅助创建
        private void EnsureBreakdownRows(int targetCount)
        {
            if (_breakdownScrollContent == null) return;
            while (_breakdownRows.Count < targetCount)
            {
                GameObject rowObj = new GameObject($"BreakdownRow_{_breakdownRows.Count}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                rowObj.transform.SetParent(_breakdownScrollContent, false);

                var le = rowObj.AddComponent<LayoutElement>();
                le.minHeight = 20f;
                le.preferredHeight = 20f;

                var hl = rowObj.GetComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.spacing = 4f;
                hl.childForceExpandWidth = false;
                hl.childForceExpandHeight = false;

                var nameTxt = CreateCrispText(rowObj.transform, "Name", "", 10, TextAnchor.MiddleLeft, Color.white);
                nameTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 210f;

                var avgTxt = CreateCrispText(rowObj.transform, "Avg", "", 10, TextAnchor.MiddleLeft, Color.white);
                avgTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 85f;

                var lastMaxTxt = CreateCrispText(rowObj.transform, "LastMax", "", 10, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
                lastMaxTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                _breakdownRows.Add(new BreakdownRowUI
                {
                    Root = rowObj,
                    NameText = nameTxt,
                    AvgText = avgTxt,
                    LastMaxText = lastMaxTxt
                });
            }
        }

        private void EnsureGanttBlocks(int targetCount)
        {
            if (_timelineBarContainer == null) return;
            while (_ganttBlocks.Count < targetCount)
            {
                GameObject blkObj = new GameObject($"GanttBlock_{_ganttBlocks.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                blkObj.transform.SetParent(_timelineBarContainer, false);

                var rt = blkObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(10f, -4f);

                var img = blkObj.GetComponent<Image>();
                img.raycastTarget = false;

                var txtObj = new GameObject("IdxLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                txtObj.transform.SetParent(blkObj.transform, false);
                var txtRt = txtObj.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.sizeDelta = Vector2.zero;

                var txt = txtObj.GetComponent<Text>();
                txt.font = WorkbenchControls.MainFont;
                txt.fontSize = 9;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.black;
                txt.raycastTarget = false;

                _ganttBlocks.Add(rt);
            }
        }

        private void EnsureTimelineRows(int targetCount)
        {
            if (_timelineScrollContent == null) return;
            while (_timelineRows.Count < targetCount)
            {
                GameObject rowObj = new GameObject($"TimelineRow_{_timelineRows.Count}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                rowObj.transform.SetParent(_timelineScrollContent, false);

                var le = rowObj.AddComponent<LayoutElement>();
                le.minHeight = 20f;
                le.preferredHeight = 20f;

                var hl = rowObj.GetComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.spacing = 4f;
                hl.childForceExpandWidth = false;
                hl.childForceExpandHeight = false;

                var seqTxt = CreateCrispText(rowObj.transform, "Seq", "", 9, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
                seqTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 26f;

                var layerTxt = CreateCrispText(rowObj.transform, "Layer", "", 9, TextAnchor.MiddleLeft, Color.white);
                layerTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 38f;

                var tierTxt = CreateCrispText(rowObj.transform, "Tier", "", 9, TextAnchor.MiddleLeft, Color.white);
                tierTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

                var nameTxt = CreateCrispText(rowObj.transform, "Name", "", 10, TextAnchor.MiddleLeft, Color.white);
                nameTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

                var timingTxt = CreateCrispText(rowObj.transform, "Timing", "", 9, TextAnchor.MiddleLeft, WorkbenchStyleEngine.ColorTextMuted);
                timingTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 75f;

                var durTxt = CreateCrispText(rowObj.transform, "Dur", "", 10, TextAnchor.MiddleLeft, Color.white);
                durTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                _timelineRows.Add(new TimelineRowUI
                {
                    Root = rowObj,
                    SeqText = seqTxt,
                    LayerText = layerTxt,
                    TierText = tierTxt,
                    NameText = nameTxt,
                    TimingText = timingTxt,
                    DurationText = durTxt
                });
            }
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

        private static void CreateDividerLine(Transform parent)
        {
            GameObject line = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            line.transform.SetParent(parent, false);

            var le = line.GetComponent<LayoutElement>();
            le.minHeight = 1f;
            le.preferredHeight = 1f;
            le.flexibleWidth = 1f;

            var img = line.GetComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.45f, 0.5f);
            img.raycastTarget = false;
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
