using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Controls
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 现代化多级火箭分级序列仪 (Avionics Staging Sequence Widget)
    /// ====================================================================================
    /// 全面实现原版分级操作与交互中枢能力，极致精简优雅，完美替代 KSP 原版分级列表：
    /// 1. 深度交互操作中枢 (Interactive Staging Manipulation)：
    ///    - [+] 增加分级：顶栏快捷插入新空分级 (InsertStage)；
    ///    - [×] 快捷删除：仅在空白分级上显示删除图标，杜绝误触与每行按钮视觉噪音；
    ///    - 拖拽重排分级：拖动分级徽章直接上下交换分级顺序 (MoveStage)；
    ///    - 拖拽部件跨级移动：拖拽部件芯片跨级投放，零件与其对称体立即同步移至目标分级 (MovePartToStage)；
    ///    - 零件场景高亮与悬停提示：鼠标悬停在芯片上，3D 视口内飞船上对应零件及对称体高亮发光 (SetHighlight)，同时弹出悬停信息卡；
    ///    - 分级触发与安全锁：底部直接点击 ARMED/LOCKED 切换安全锁，点击 STAGE 按键触发分级。
    /// 2. 深度挂钩原版分级部件图标 (Stock Stage Icons Deep Hook & Modern Redraw)：
    ///    通过 StockStageIconService 与 StageIconAtlasGenerator 获取图集与 UV，以高反差 HUD 航电微芯片形式重绘；
    ///    支持原版 Atlas 与程序化备用 Atlas，显示对称数量角标 (×4, ×6)。
    /// 3. 单级高精动力学遥测 (Stage Dynamics Telemetry)：
    ///    展示单级可用 ΔV (m/s)、发动机工作时间 (⏱ mm:ss) 及推重比 (TWR)；动作级（降落伞、分离器）无缝伸展图标，不显示冗余占位符。
    /// 4. 单级推进剂微量程光条 (Hairline Propellant Gauge)：
    ///    行底部微米级推进剂安全指示条 (20% 琥珀色、5% 红色脉冲)。
    /// 5. 动态长宽比自适应 (IAdaptiveSizeWidget)：
    ///    支持非等比自由缩放与多级流畅滚动，完美适应紧凑小卡片与宽幅航电大屏。
    /// 6. 严格遵守 MFP 规范：
    ///    0 颜色字面量 (MFP-SPEC-006)、0 场景查询 (MFP-SPEC-007)、纯 C# 服务解耦。
    /// </summary>
    [FlightWidget("staging_sequence", "stage_sequence", Category = WidgetCategory.Controls, DisplayName = "STAGE 垂直分级时序序列仪", Description = "垂直火箭分级序列仪：逐级剩余 ΔV、燃烧时间、推重比与单级推进剂微量程，重构原版左侧分级。", DefaultWidgetId = "custom.staging_sequence", DefaultX = -440f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "custom.staging_sequence", "custom.stage_sequence", "core.staging_sequence" })]
    public class StagingSequenceWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(DefaultWidth, DefaultHeight);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(140f, 120f);
        public Vector2 MaxBaseSize => new Vector2(360f, 960f);

        // 声明式微控件
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_CTRL_STAGING", "分级序列"), null, 8.5f);

        // UI 根与卡片
        private Image _bgImage;
        private Outline _bgOutline;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 顶栏总览
        private Button _addStageTopBtn;
        private Image _addStageTopBg;
        private Text _addStageTopText;
        private Button _toggleAllBtn;
        private Image _toggleAllBg;
        private Text _toggleAllText;
        private bool _allExpanded = true;
        private Text _totalDvText;
        private Image _topDivider;

        // 分级拖放插入指示微线
        private GameObject _stageDropIndicatorGo;
        private RectTransform _stageDropIndicatorRt;
        private Image _stageDropIndicatorImg;

        // 部件图标微芯片 UI 结构
        internal class StageIconChipUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image ChipBg;
            public Outline ChipOutline;
            public RawImage IconRawImage;
            public Text MultiplierText;
            public StagePartDragHandler DragHandler;
            public uint PartFlightId;
            public int StageNumber;
            public int PartIndex;
            public StagePartIconData PartData;
            public bool IsHovered;
            public float CurrentScale = 1f;
        }

        // 单级行 UI 结构 (精简 24px / 展开 46px 动态自适应排版)
        internal class StageItemUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image RowHighlightBg;
            public Button ToggleExpandBtn;
            public Image ToggleExpandBg;
            public Text ToggleExpandText;
            public Image BadgeBg;
            public Text BadgeText;
            public StageBadgeDragHandler BadgeDragHandler;
            public Button DeleteStageBtn;
            public Image DeleteStageBg;
            public Text DeleteStageText;
            public Text StageDvText;
            public Text StageMetaText;
            public GameObject IconsContainer;
            public RectTransform IconsContainerRt;
            public readonly List<StageIconChipUI> IconChips = new List<StageIconChipUI>();
            public GameObject PropBarRoot;
            public RectTransform PropBarRootRt;
            public Image PropTrack;
            public Image PropFill;
            public Image Separator;
            public Text EmptySlotText;
            public int StageNumber;
            public bool IsActiveStage;
            public bool IsBurning;
            public bool IsExpanded = true;
            public bool HasUserToggled = false;
            public float TargetPropFrac;
            public float CurrentPropFrac;
            public float TransitionFlashTimer;

            // Dirty tracking & Microsecond Caching
            public int LastPartHash = -1;
            public float LastRenderedAvailW = -1f;
            public bool LastRenderedExpanded = false;
            public bool LastRenderedActive = false;
            public double LastRenderedDv = -9999.0;
            public int LastRenderedBurnSec = -1;
            public double LastRenderedTwr = -9999.0;
            public string CachedDvStr;
            public string CachedMetaStr;
        }

        private const int InitialPooledStages = 10;
        private const int MaxDisplayedStages = 32;
        private const int MaxChipsPerStage = 24;
        private readonly List<StageItemUI> _stageItems = new List<StageItemUI>();
        private readonly CachedFloat _lastLayoutW = new CachedFloat(-1f);
        private readonly CachedFloat _lastLayoutH = new CachedFloat(-1f);
        private readonly List<StageDeltaVInfo> _reusableSortedStages = new List<StageDeltaVInfo>();
        private static readonly Comparison<StageDeltaVInfo> _stageOrderAscending = (a, b) => a.Stage.CompareTo(b.Stage);
        private static readonly Comparison<StageDeltaVInfo> _stageOrderDescending = (a, b) => b.Stage.CompareTo(a.Stage);

        // 可滚动分级视口组件
        private ScrollRect _scrollRect;
        private RectTransform _scrollViewportRt;
        private RectTransform _scrollContentRt;
        private string _stageOrder = "STOCK";

        // 底栏安全与触发指示
        private Image _bottomDivider;
        private Button _statusBadgeBtn;
        private Image _statusBadgeBg;
        private Text _statusBadgeText;
        private Button _stageTriggerBtn;
        private Image _stageTriggerBg;
        private Text _stageTriggerText;

        // 悬浮信息提示框 (Floating HUD Tooltip)
        private GameObject _tooltipRoot;
        private RectTransform _tooltipRt;
        private Image _tooltipBg;
        private Outline _tooltipOutline;
        private Text _tooltipTitle;
        private Text _tooltipSub;

        // 拖拽影子图标 (Drag Ghost Avatar)
        private GameObject _dragGhostRoot;
        private RectTransform _dragGhostRt;
        private Image _dragGhostBg;
        private RawImage _dragGhostIcon;
        private Text _dragGhostMult;

        private static readonly string[] _cachedStageBadges = GenerateStageBadges();
        private static readonly string[] _cachedStageTriggers = GenerateStageTriggers();

        private static string[] GenerateStageBadges()
        {
            var arr = new string[100];
            for (int i = 0; i < 100; i++) arr[i] = $"S{i:00}";
            return arr;
        }

        private static string[] GenerateStageTriggers()
        {
            var arr = new string[100];
            for (int i = 0; i < 100; i++) arr[i] = $"STAGE S{i:00}";
            return arr;
        }

        private static string GetStageBadge(int stage)
        {
            if ((uint)stage < (uint)_cachedStageBadges.Length) return _cachedStageBadges[stage];
            return $"S{stage:00}";
        }

        private static string GetStageTriggerText(int stage)
        {
            if ((uint)stage < (uint)_cachedStageTriggers.Length) return _cachedStageTriggers[stage];
            return $"STAGE S{stage:00}";
        }

        // 缓存与脏检查标记
        private ThemeConfig _cachedTheme;
        private readonly Cached<bool> _lastStageLocked = new Cached<bool>(false);
        private readonly Cached<string> _lastTotalDvStr = new Cached<string>(string.Empty);
        private readonly CachedDouble _lastTotalDv = new CachedDouble(double.NaN);

        private int _highestStageNumber = 0;
        private readonly Cached<int> _lastActiveStage = new Cached<int>(-1);
        private float _stageTriggerRecoilTimer = 0f;
        private float _stageTriggerFlashTimer = 0f;
        public static float CustomAnimationTime = -1f;
        public static float CustomAnimationDeltaTime = -1f;

        // 几何参数 (基准像素)
        private const float DefaultWidth = 160f;
        private const float DefaultHeight = 260f;

        // 风格配置
        private string _frameMode = "FAINT";
        private string _titleTemplate = "STAGING";
        private string _totalDvToken = "{DV:TOTAL}";
        private string _stageDvToken = "{DV:STAGE}";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            string frame = GetTemplateChannel("FRAME", null);
            _frameMode = !string.IsNullOrEmpty(frame) ? frame.ToUpperInvariant() : "FAINT";
            _titleTemplate = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_CTRL_STAGING", "STAGING"));
            _totalDvToken = GetTemplateChannel("TOTAL_DV_TOKEN", "{DV:TOTAL}");
            _stageDvToken = GetTemplateChannel("STAGE_DV_TOKEN", "{DV:STAGE}");
            string order = GetTemplateChannel("ORDER", null);
            _stageOrder = !string.IsNullOrEmpty(order) ? order.ToUpperInvariant() : "STOCK";

            // 1. 组件包围盒 (基准 160x260 逻辑像素)
            Vector2 size = BaseSize * s;
            RectTransform.sizeDelta = size;

            // 2. 底板卡片 (现代化暗晶毛玻璃背板 0.75 Alpha)
            _bgImage = CardBackground;
            if (_bgImage != null)
            {
                _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
                _bgImage.raycastTarget = false;
            }
            _bgOutline = CardOutline;
            if (_bgOutline != null)
            {
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
                _bgOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }

            // 3. 顶部总览行 (标题 + 插入级 [+] + 全级总 ΔV)
            if (Title != null)
            {
                Title.Text = _titleTemplate;
                Title.SetRole(TextStyleRole.Cardinal);
                if (Title.TextComponent != null)
                {
                    Title.TextComponent.fontSize = Mathf.RoundToInt(8.5f * s);
                    Title.TextComponent.fontStyle = FontStyle.Bold;
                    Title.TextComponent.alignment = TextAnchor.MiddleLeft;
                }
            }

            // 顶栏快速插入新分级 [+] 按键
            _addStageTopBtn = CreateButton("Btn_Add_Top", transform, out RectTransform addTopRt, out _addStageTopBg,
                new Vector2(14f * s, 13f * s), Vector2.zero);
            _addStageTopBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _addStageTopBtn.onClick.AddListener(OnAddStageTopClicked);

            _addStageTopText = UIFactory.CreateText(_addStageTopBtn.transform, "Text", "+", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _addStageTopText.fontStyle = FontStyle.Bold;
            _addStageTopText.rectTransform.sizeDelta = addTopRt.sizeDelta;

            // 顶栏全部展开/折叠按键 [▼ / ▶]
            _toggleAllBtn = CreateButton("Btn_Toggle_All", transform, out RectTransform togTopRt, out _toggleAllBg,
                new Vector2(14f * s, 13f * s), Vector2.zero);
            _toggleAllBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _toggleAllBtn.onClick.AddListener(OnToggleAllExpanded);

            _toggleAllText = UIFactory.CreateText(_toggleAllBtn.transform, "Text", "▼", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _toggleAllText.fontStyle = FontStyle.Bold;
            _toggleAllText.rectTransform.sizeDelta = togTopRt.sizeDelta;

            _totalDvText = UIFactory.CreateText(transform, "Total_Dv_Text", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _totalDvText.fontStyle = FontStyle.Bold;

            // 顶部分割微线
            _topDivider = CreateChild<Image>("Top_Divider", transform);
            _topDivider.raycastTarget = false;

            // 4. 创建可滚动分级视口 (Scroll View + RectMask2D)
            _scrollViewportRt = CreateViewport("Stages_Scroll_View", transform);
            _scrollViewportRt.pivot = new Vector2(0.5f, 0.5f);
            GameObject scrollRootGo = _scrollViewportRt.gameObject;

            Image viewBg = scrollRootGo.AddComponent<Image>();
            viewBg.color = Color.clear;
            viewBg.raycastTarget = true;

            _scrollRect = scrollRootGo.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 25f;

            _scrollContentRt = CreateContainer("Scroll_Content", _scrollViewportRt);
            _scrollContentRt.anchorMin = new Vector2(0.5f, 1f);
            _scrollContentRt.anchorMax = new Vector2(0.5f, 1f);
            _scrollContentRt.pivot = new Vector2(0.5f, 1f);

            _scrollRect.content = _scrollContentRt;
            _scrollRect.viewport = _scrollViewportRt;

            // 拖拽插入高光指示微线 (Stage Drop Insertion Indicator)
            _stageDropIndicatorRt = CreateContainer("Stage_Drop_Indicator",
                _scrollContentRt != null ? (Transform)_scrollContentRt : transform,
                new Vector2((DefaultWidth - 10f) * s, 2.5f * s), Vector2.zero);
            _stageDropIndicatorGo = _stageDropIndicatorRt.gameObject;
            _stageDropIndicatorRt.anchorMin = new Vector2(0.5f, 1f);
            _stageDropIndicatorRt.anchorMax = new Vector2(0.5f, 1f);
            _stageDropIndicatorRt.pivot = new Vector2(0.5f, 0.5f);

            _stageDropIndicatorImg = _stageDropIndicatorGo.AddComponent<Image>();
            _stageDropIndicatorImg.color = theme.AccentPrimary;
            _stageDropIndicatorImg.raycastTarget = false;

            Outline dropOutl = _stageDropIndicatorGo.AddComponent<Outline>();
            dropOutl.effectDistance = new Vector2(1f * s, 1f * s);
            dropOutl.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.5f);

            _stageDropIndicatorGo.SetActive(false);

            // 5. 构建分级行对象池 (初始预分配 10 级)
            for (int i = 0; i < InitialPooledStages; i++)
            {
                StageItemUI item = CreateStageItem(i, s, theme);
                _stageItems.Add(item);
            }

            // 6. 底部分割微线与可交互操作栏
            _bottomDivider = CreateChild<Image>("Bottom_Divider", transform);
            _bottomDivider.raycastTarget = false;

            // 分级锁切换按键 [ARMED / LOCKED]
            _statusBadgeBtn = CreateButton("Btn_Status_Lock", transform, out RectTransform statusRt, out _statusBadgeBg,
                new Vector2(48f * s, 15f * s), Vector2.zero);
            _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _statusBadgeBtn.onClick.AddListener(OnStatusLockClicked);

            _statusBadgeText = UIFactory.CreateText(_statusBadgeBtn.transform, "Status_Text", I18n.Tr("WIDGET_ALERT_ARMED", "待发"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _statusBadgeText.fontStyle = FontStyle.Bold;
            _statusBadgeText.rectTransform.sizeDelta = statusRt.sizeDelta;

            // 分级触发按键 [SPACE TO STAGE / ▶ STAGE]
            _stageTriggerBtn = CreateButton("Btn_Stage_Trigger", transform, out RectTransform trigRt, out _stageTriggerBg,
                new Vector2(85f * s, 15f * s), Vector2.zero);
            _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stageTriggerBtn.onClick.AddListener(OnStageTriggerClicked);

            _stageTriggerText = UIFactory.CreateText(_stageTriggerBtn.transform, "Trigger_Text", I18n.Tr("WIDGET_CTRL_SPACE_STAGE", "空格分级"), Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            _stageTriggerText.rectTransform.sizeDelta = trigRt.sizeDelta;

            // 7. 创建悬浮信息提示卡 (Tooltip)
            CreateTooltipPanel(s, theme);

            // 8. 创建拖拽虚拟影子 (Drag Ghost)
            CreateDragGhost(s, theme);

            // 标准化组件内部控件注册至管理器
            if (_bgImage != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "background", "分级序列底板", _bgImage.gameObject);
            }
            if (_scrollViewportRt != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "stages_viewport", "分级序列视口列表", _scrollViewportRt.gameObject);
            }
            if (_stageTriggerBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_trigger_btn", "分级触发指示键", _stageTriggerBtn.gameObject, _stageTriggerBtn, _stageTriggerBg, null, _stageTriggerText, null, "SPACE TO STAGE", OnStageTriggerClicked, false));
            }

            ApplyLayout(size.x, size.y);
            ApplyTheme(theme);
        }

        // ==========================================
        // 动态长宽比排版自适应 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayout(pixelSize.x, pixelSize.y);
        }

        private void ApplyLayout(float width, float height)
        {
            float s = CurrentDpiScale;
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            // 1. 顶栏排版
            float headerH = 22f * s;
            float titleW = Mathf.Clamp(width * 0.32f, 38f * s, 65f * s);
            if (Title != null && Title.RootGameObject != null)
            {
                RectTransform tRt = Title.RootGameObject.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchorMin = new Vector2(0.5f, 0.5f);
                    tRt.anchorMax = new Vector2(0.5f, 0.5f);
                    tRt.pivot = new Vector2(0.5f, 0.5f);
                    tRt.SetSizeDeltaSafe(new Vector2(titleW, 16f * s));
                    tRt.SetAnchoredPositionSafe(new Vector2(-halfW + titleW * 0.5f + 8f * s, halfH - 12f * s));
                }
            }

            if (_addStageTopBtn != null)
            {
                RectTransform addRt = _addStageTopBtn.GetComponent<RectTransform>();
                float addX = -halfW + titleW + 16f * s;
                addRt.SetAnchoredPositionSafe(new Vector2(addX, halfH - 12f * s));
            }

            if (_toggleAllBtn != null)
            {
                RectTransform togRt = _toggleAllBtn.GetComponent<RectTransform>();
                float togX = -halfW + titleW + 32f * s;
                togRt.SetAnchoredPositionSafe(new Vector2(togX, halfH - 12f * s));
            }

            if (_totalDvText != null)
            {
                RectTransform dvRt = _totalDvText.rectTransform;
                float dvW = Mathf.Clamp(width * 0.35f, 45f * s, 85f * s);
                dvRt.SetSizeDeltaSafe(new Vector2(dvW, 16f * s));
                dvRt.SetAnchoredPositionSafe(new Vector2(halfW - dvW * 0.5f - 8f * s, halfH - 12f * s));
            }

            if (_topDivider != null)
            {
                RectTransform topDivRt = _topDivider.rectTransform;
                topDivRt.SetSizeDeltaSafe(new Vector2(width - 12f * s, 1f * s));
                topDivRt.SetAnchoredPositionSafe(new Vector2(0f, halfH - headerH));
            }

            // 2. 底栏排版
            float footerH = 24f * s;
            if (_bottomDivider != null)
            {
                RectTransform botDivRt = _bottomDivider.rectTransform;
                botDivRt.SetSizeDeltaSafe(new Vector2(width - 12f * s, 1f * s));
                botDivRt.SetAnchoredPositionSafe(new Vector2(0f, -halfH + footerH));
            }

            if (_statusBadgeBtn != null && _stageTriggerBtn != null)
            {
                float lockW = Mathf.Clamp(width * 0.32f, 44f * s, 68f * s);
                float trigW = Mathf.Max(60f * s, (width - 24f * s) - lockW);
                float btnY = -halfH + 11f * s;

                RectTransform statRt = _statusBadgeBtn.GetComponent<RectTransform>();
                statRt.SetSizeDeltaSafe(new Vector2(lockW, 15f * s));
                statRt.SetAnchoredPositionSafe(new Vector2(-halfW + 8f * s + lockW * 0.5f, btnY));
                if (_statusBadgeText != null) _statusBadgeText.rectTransform.SetSizeDeltaSafe(statRt.sizeDelta);

                RectTransform trigRt = _stageTriggerBtn.GetComponent<RectTransform>();
                trigRt.SetSizeDeltaSafe(new Vector2(trigW, 15f * s));
                trigRt.SetAnchoredPositionSafe(new Vector2(halfW - 8f * s - trigW * 0.5f, btnY));
                if (_stageTriggerText != null) _stageTriggerText.rectTransform.SetSizeDeltaSafe(trigRt.sizeDelta);
            }

            // 3. 中间视口 (ScrollView Viewport)
            float vpH = Mathf.Max(30f * s, height - headerH - footerH);
            if (_scrollViewportRt != null)
            {
                _scrollViewportRt.SetSizeDeltaSafe(new Vector2(width - 8f * s, vpH));
                float vpCenterY = (halfH - headerH) - (vpH * 0.5f);
                _scrollViewportRt.SetAnchoredPositionSafe(new Vector2(0f, vpCenterY));
            }
        }

        private StageItemUI CreateStageItem(int index, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Transform parent = _scrollContentRt != null ? (Transform)_scrollContentRt : transform;
            RectTransform rootRt = CreateContainer($"Stage_Item_{index}", parent, new Vector2((DefaultWidth - 10f) * s, 28f * s));
            GameObject root = rootRt.gameObject;
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);

            // 拖放悬停发光底板 (Drop Target Glow / Row Highlight)
            Image rowHlImg = CreateChild<Image>("Row_Highlight_Bg", root.transform, new Vector2((DefaultWidth - 10f) * s, 26f * s));
            GameObject rowHlGo = rowHlImg.gameObject;
            RectTransform rowHlRt = rowHlImg.rectTransform;
            rowHlImg.SetColor(Color.clear);
            rowHlImg.raycastTarget = false;

            // 行展开 / 折叠切换微按键 (▼ / ▶)
            Button toggleBtn = CreateButton("Btn_Toggle_Expand", root.transform, out RectTransform toggleRt, out Image toggleImg, new Vector2(12f * s, 14f * s));
            GameObject toggleGo = toggleBtn.gameObject;
            toggleImg.SetColor(Color.clear);

            Text toggleTxt = UIFactory.CreateText(toggleGo.transform, "Text", "▼", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            toggleTxt.fontStyle = FontStyle.Bold;
            toggleTxt.rectTransform.SetSizeDeltaSafe(toggleRt.sizeDelta);

            // 分级徽章 (可拖拽调序，如 S03 / S02)
            Button badgeBtn = CreateButton("Badge_Bg", root.transform, out RectTransform badgeRt, out Image badgeImg, new Vector2(22f * s, 16f * s));
            GameObject badgeGo = badgeBtn.gameObject;
            badgeImg.raycastTarget = true;

            StageBadgeDragHandler badgeDrag = badgeGo.AddComponent<StageBadgeDragHandler>();
            badgeDrag.OwnerWidget = this;

            Text badgeText = UIFactory.CreateText(badgeGo.transform, "Badge_Text", $"S{index:00}", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.rectTransform.SetSizeDeltaSafe(badgeRt.sizeDelta);

            // 空分级删除 [ × ] 按键 (仅在空分级时显示)
            Button delBtn = CreateButton("Btn_Delete", root.transform, out RectTransform delRt, out Image delImg, new Vector2(14f * s, 14f * s));
            GameObject delGo = delBtn.gameObject;
            delImg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));

            Text delTxt = UIFactory.CreateText(delGo.transform, "Text", "×", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            delTxt.rectTransform.SetSizeDeltaSafe(delRt.sizeDelta);
            delGo.SetActive(false);

            // 单级 ΔV 数值 (右对齐，动力级时显示)
            Text dvText = UIFactory.CreateText(root.transform, "Stage_Dv", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            dvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = dvText.rectTransform;
            dvRt.sizeDelta = new Vector2(65f * s, 14f * s);

            // 单级元数据副行 (⏱ 00:52 · 1.65 T)
            Text metaText = UIFactory.CreateText(root.transform, "Stage_Meta", "---", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform metaRt = metaText.rectTransform;
            metaRt.sizeDelta = new Vector2(70f * s, 10f * s);

            // 空分级占位提示文本 [EMPTY STAGE]
            Text emptyTxt = UIFactory.CreateText(root.transform, "Empty_Slot_Text", I18n.Tr("WIDGET_CTRL_EMPTY_STAGE", "[空级]"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform emptyRt = emptyTxt.rectTransform;
            emptyRt.pivot = new Vector2(0f, 0.5f);
            emptyRt.sizeDelta = new Vector2(90f * s, 14f * s);
            emptyTxt.gameObject.SetActive(false);

            // 部件图标托盘容器 (Icons Container)
            RectTransform iconsContainerRt = CreateContainer("Icons_Container", root.transform, new Vector2(70f * s, 20f * s));
            GameObject iconsContainer = iconsContainerRt.gameObject;

            var chips = new List<StageIconChipUI>();
            Texture initialAtlas = StockStageIconService.Provider?.StockAtlas ?? StageIconAtlasGenerator.GetAtlas();

            for (int c = 0; c < MaxChipsPerStage; c++)
            {
                StageIconChipUI chip = CreateIconChip(iconsContainer.transform, c, s, theme, initialAtlas);
                chips.Add(chip);
            }

            // 推进剂监测微条 (Propellant Bar)
            RectTransform propRootRt = CreateContainer("Prop_Bar_Root", root.transform, new Vector2((DefaultWidth - 10f) * s, 4f * s));
            GameObject propRoot = propRootRt.gameObject;

            Image trackImg = CreateChild<Image>("Track", propRoot.transform, new Vector2((DefaultWidth - 10f) * s, 1.5f * s));
            GameObject trackGo = trackImg.gameObject;
            RectTransform trackRt = trackImg.rectTransform;
            trackImg.raycastTarget = false;

            Image fillImg = CreateChild<Image>("Fill", propRoot.transform, new Vector2((DefaultWidth - 10f) * s, 2f * s), new Vector2(-(DefaultWidth - 10f) * 0.5f * s, 0f));
            GameObject fillGo = fillImg.gameObject;
            RectTransform fillRt = fillImg.rectTransform;
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillImg.raycastTarget = false;

            // 分割微线
            Image sepImg = CreateChild<Image>("Sep", root.transform, new Vector2((DefaultWidth - 10f) * s, 1f * s));
            GameObject sepGo = sepImg.gameObject;
            RectTransform sepRt = sepImg.rectTransform;
            sepImg.raycastTarget = false;

            var item = new StageItemUI
            {
                Root = root,
                RootRt = rootRt,
                RowHighlightBg = rowHlImg,
                ToggleExpandBtn = toggleBtn,
                ToggleExpandBg = toggleImg,
                ToggleExpandText = toggleTxt,
                BadgeBg = badgeImg,
                BadgeText = badgeText,
                BadgeDragHandler = badgeDrag,
                DeleteStageBtn = delBtn,
                DeleteStageBg = delImg,
                DeleteStageText = delTxt,
                StageDvText = dvText,
                StageMetaText = metaText,
                EmptySlotText = emptyTxt,
                IconsContainer = iconsContainer,
                IconsContainerRt = iconsContainerRt,
                PropBarRoot = propRoot,
                PropBarRootRt = propRootRt,
                PropTrack = trackImg,
                PropFill = fillImg,
                Separator = sepImg,
                StageNumber = index,
                IsExpanded = true,
                HasUserToggled = false
            };
            badgeDrag.StageItem = item;
            item.IconChips.AddRange(chips);

            toggleBtn.onClick.AddListener(() => OnToggleStageExpanded(item));
            delBtn.onClick.AddListener(() => OnDeleteStageClicked(item.StageNumber));

            return item;
        }

        private StageIconChipUI CreateIconChip(Transform parent, int chipIndex, float s, ThemeConfig theme, Texture atlas)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Image chipBg = CreateChild<Image>($"Chip_{chipIndex}", parent, new Vector2(18f * s, 18f * s), new Vector2((-40f + chipIndex * 20f) * s, 0f));
            GameObject chipGo = chipBg.gameObject;
            RectTransform chipRt = chipBg.rectTransform;

            chipBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            chipBg.raycastTarget = true;

            Outline chipOutline = chipGo.AddComponent<Outline>();
            chipOutline.effectDistance = new Vector2(1f * s, 1f * s);
            chipOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));

            // 挂载专用零件拖拽交互处理器
            StagePartDragHandler drag = chipGo.AddComponent<StagePartDragHandler>();
            drag.OwnerWidget = this;

            // 图标 RawImage (16x16 居中)
            RawImage rawImg = CreateChild<RawImage>("Icon_Raw", chipGo.transform, new Vector2(16f * s, 16f * s), Vector2.zero);
            GameObject rawGo = rawImg.gameObject;
            RectTransform rawRt = rawImg.rectTransform;

            rawImg.raycastTarget = false;
            rawImg.texture = atlas;
            rawImg.uvRect = StageIconAtlasGenerator.GetIconUv(2);
            rawImg.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));

            // 数量倍率文本 (如 ×4, ×6)
            Text multText = UIFactory.CreateText(chipGo.transform, "Mult_Text", "×1", Mathf.RoundToInt(6.5f * s),
                TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            multText.fontStyle = FontStyle.Bold;
            RectTransform multRt = multText.rectTransform;
            multRt.SetSizeDeltaSafe(new Vector2(16f * s, 10f * s));
            multRt.SetAnchoredPositionSafe(new Vector2(2f * s, -5f * s));

            chipGo.SetActive(false);

            var chipUI = new StageIconChipUI
            {
                Root = chipGo,
                RootRt = chipRt,
                ChipBg = chipBg,
                ChipOutline = chipOutline,
                IconRawImage = rawImg,
                MultiplierText = multText,
                DragHandler = drag,
                PartIndex = chipIndex
            };
            drag.Chip = chipUI;

            return chipUI;
        }

        private void CreateTooltipPanel(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _tooltipBg = CreateChild<Image>("Floating_Tooltip", transform, new Vector2(136f * s, 30f * s));
            _tooltipRoot = _tooltipBg.gameObject;
            _tooltipRt = _tooltipBg.rectTransform;
            _tooltipRt.pivot = new Vector2(0.5f, 0f);

            _tooltipBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.90f);
            _tooltipBg.raycastTarget = false;

            _tooltipOutline = _tooltipRoot.AddComponent<Outline>();
            _tooltipOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _tooltipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);

            _tooltipTitle = UIFactory.CreateText(_tooltipRoot.transform, "Title", I18n.Tr("WIDGET_CTRL_PART_INFO", "部件信息"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _tooltipTitle.fontStyle = FontStyle.Bold;
            _tooltipTitle.rectTransform.sizeDelta = new Vector2(128f * s, 12f * s);
            _tooltipTitle.rectTransform.anchoredPosition = new Vector2(0f, 6f * s);

            _tooltipSub = UIFactory.CreateText(_tooltipRoot.transform, "Sub", I18n.Tr("STG_TOOLTIP_HINT", "[拖拽跨级 · 悬停高亮]"), Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _tooltipSub.rectTransform.sizeDelta = new Vector2(128f * s, 10f * s);
            _tooltipSub.rectTransform.anchoredPosition = new Vector2(0f, -6f * s);

            _tooltipRoot.SetActive(false);
        }

        private void CreateDragGhost(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _dragGhostBg = CreateChild<Image>("Drag_Ghost_Avatar", transform, new Vector2(22f * s, 22f * s));
            _dragGhostRoot = _dragGhostBg.gameObject;
            _dragGhostRt = _dragGhostBg.rectTransform;

            _dragGhostBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.40f);
            _dragGhostBg.raycastTarget = false;

            Outline outl = _dragGhostRoot.AddComponent<Outline>();
            outl.effectDistance = new Vector2(1f * s, 1f * s);
            outl.effectColor = theme.AccentPrimary;

            _dragGhostIcon = CreateChild<RawImage>("Ghost_Raw", _dragGhostRoot.transform, new Vector2(18f * s, 18f * s));
            GameObject rawGo = _dragGhostIcon.gameObject;
            RectTransform rawRt = _dragGhostIcon.rectTransform;
            _dragGhostIcon.raycastTarget = false;
            _dragGhostIcon.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);

            _dragGhostMult = UIFactory.CreateText(_dragGhostRoot.transform, "Mult", "", Mathf.RoundToInt(7f * s),
                TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _dragGhostMult.fontStyle = FontStyle.Bold;
            _dragGhostMult.rectTransform.sizeDelta = new Vector2(16f * s, 10f * s);
            _dragGhostMult.rectTransform.anchoredPosition = new Vector2(2f * s, -5f * s);

            _dragGhostRoot.SetActive(false);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 外框模式着色 (现代化暗晶毛玻璃背板 0.75 Alpha)
            if (_frameMode == "NONE")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null) _bgOutline.enabled = false;
            }
            else if (_frameMode == "FAINT")
            {
                if (_bgImage != null) _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
                if (_bgOutline != null)
                {
                    _bgOutline.enabled = true;
                    _bgOutline.effectColor = _currentCardRole == CardStyleRole.Emphasized
                        ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Medium)
                        : WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                }
            }
            else
            {
                ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);
            }

            // 2. 顶栏与底栏
            ApplyText(_totalDvText, TextStyleRole.PrimaryValue, theme);
            if (_addStageTopBg != null) _addStageTopBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_addStageTopText != null) ApplyText(_addStageTopText, TextStyleRole.PrimaryValue, theme);
            if (_toggleAllBg != null) _toggleAllBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_toggleAllText != null) ApplyText(_toggleAllText, TextStyleRole.SecondaryValue, theme);
            if (_stageDropIndicatorImg != null) _stageDropIndicatorImg.color = theme.AccentPrimary;

            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            if (_statusBadgeBg != null) _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_statusBadgeText != null) ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            if (_stageTriggerBg != null) _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageTriggerText != null) ApplyText(_stageTriggerText, TextStyleRole.Label, theme);

            // 3. 各分级项着色
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (item.ToggleExpandText != null) ApplyText(item.ToggleExpandText, TextStyleRole.SecondaryValue, theme);
                if (item.Separator != null) item.Separator.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (item.PropTrack != null) item.PropTrack.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
                if (item.PropFill != null) item.PropFill.color = theme.AccentPrimary;

                if (item.DeleteStageBg != null) item.DeleteStageBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (item.DeleteStageText != null) ApplyText(item.DeleteStageText, TextStyleRole.Label, theme);
                if (item.EmptySlotText != null) ApplyText(item.EmptySlotText, TextStyleRole.Label, theme);

                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    StageIconChipUI chip = item.IconChips[c];
                    if (chip.ChipBg != null) chip.ChipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    if (chip.ChipOutline != null) chip.ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    if (chip.MultiplierText != null) ApplyText(chip.MultiplierText, TextStyleRole.PrimaryValue, theme);
                }
            }

            // 4. Tooltip & Drag Ghost
            if (_tooltipBg != null) _tooltipBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.90f);
            if (_tooltipOutline != null) _tooltipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            if (_tooltipTitle != null) ApplyText(_tooltipTitle, TextStyleRole.PrimaryValue, theme);
            if (_tooltipSub != null) ApplyText(_tooltipSub, TextStyleRole.SecondaryValue, theme);
        }

        private bool _cachedHasVessel;
        private string _cachedTitle;
        private double _cachedTotalDv;
        private bool _cachedIsLocked;
        private int _cachedCurStage;
        private float _cachedThrottle;
        private double _cachedVerticalSpeed;
        private float _cachedPropFrac;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            if (context.Telemetry == null || !context.Telemetry.HasVessel)
            {
                _cachedHasVessel = false;
                return;
            }

            _cachedHasVessel = true;
            _cachedTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, context.Telemetry);

            double totalDv = TelemetryTokenEngine.EvaluateNumeric(_totalDvToken, context.Telemetry);
            if (double.IsNaN(totalDv)) totalDv = context.Telemetry.TotalDeltaV;
            _cachedTotalDv = totalDv;

            _cachedIsLocked = StockStageActionService.IsStagingLocked || context.Telemetry.IsStageLocked;

            int curStage = context.Telemetry.CurrentStage;
            _cachedCurStage = curStage;
            _cachedThrottle = (float)context.Telemetry.Throttle;
            _cachedVerticalSpeed = context.Telemetry.VerticalSpeed;
            _cachedPropFrac = Mathf.Clamp01((float)context.Telemetry.StagePropellantFraction);

            IReadOnlyList<StageDeltaVInfo> stages = context.Telemetry.StageDeltaVList;
            int stageCount = stages != null ? stages.Count : 0;

            _highestStageNumber = curStage;
            if (stages != null && stages.Count > 0)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].Stage > _highestStageNumber) _highestStageNumber = stages[i].Stage;
                }
            }

            _reusableSortedStages.Clear();
            if (stageCount > 0)
            {
                _reusableSortedStages.AddRange(stages);
                if (_stageOrder == "REVERSE")
                {
                    _reusableSortedStages.Sort(_stageOrderDescending);
                }
                else
                {
                    _reusableSortedStages.Sort(_stageOrderAscending);
                }
            }
            else
            {
                _reusableSortedStages.Add(new StageDeltaVInfo(curStage, context.Telemetry.StageDeltaV, context.Telemetry.StageBurnTime, context.Telemetry.TWR, 310.0, true));
            }
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_cachedHasVessel) return;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(context.Theme ?? _cachedTheme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 读取当前物理尺寸并自适应布局 (仅在尺寸变化时才调用 ApplyLayout)
            float currentW = RectTransform.rect.width > 10f ? RectTransform.rect.width : DefaultWidth * s;
            float currentH = RectTransform.rect.height > 10f ? RectTransform.rect.height : DefaultHeight * s;
            if (Mathf.Abs(currentW - _lastLayoutW.Value) > 0.5f || Mathf.Abs(currentH - _lastLayoutH.Value) > 0.5f)
            {
                _lastLayoutW.Update(currentW);
                _lastLayoutH.Update(currentH);
                ApplyLayout(currentW, currentH);
            }

            // 2. 动态标题与全级总 ΔV
            if (Title != null && Title.Text != _cachedTitle)
            {
                Title.Text = _cachedTitle;
            }

            double totalDv = _cachedTotalDv;
            if (Math.Abs(totalDv - _lastTotalDv.Value) >= 0.5 || string.IsNullOrEmpty(_lastTotalDvStr.Value))
            {
                _lastTotalDv.Update(totalDv);
                _lastTotalDvStr.Update($"{totalDv:N0} m/s");
                SetTextIfChanged(_totalDvText, _lastTotalDvStr.Value);
            }

            // 3. 分级安全锁与状态
            bool isLocked = _cachedIsLocked;
            if (_lastStageLocked.Update(isLocked))
            {
                string statusText = isLocked ? I18n.Tr("WIDGET_STAGE_LOCKED", "锁定") : I18n.Tr("WIDGET_ALERT_ARMED", "待发");
                SetTextIfChanged(_statusBadgeText, statusText);
                _statusBadgeText.color = isLocked 
                    ? style.GetTextColor(TextStyleRole.Warning, theme)
                    : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }

            int curStage = _cachedCurStage;

            // 获取当前有效图集
            Texture stockAtlas = StockStageIconService.Provider?.StockAtlas;
            bool isUsingStockAtlas = stockAtlas != null;
            Texture currentAtlas = isUsingStockAtlas ? stockAtlas : StageIconAtlasGenerator.GetAtlas();

            List<StageDeltaVInfo> sortedStages = _reusableSortedStages;

            if (_lastActiveStage.Value >= 0 && _lastActiveStage.Value != curStage)
            {
                for (int m = 0; m < _stageItems.Count; m++)
                {
                    if (_stageItems[m].StageNumber == curStage)
                    {
                        _stageItems[m].TransitionFlashTimer = 0.4f;
                    }
                }
            }
            _lastActiveStage.Update(curStage);

            int displayCount = Mathf.Min(sortedStages.Count, MaxDisplayedStages);

            // 动态扩充对象池
            while (_stageItems.Count < displayCount)
            {
                _stageItems.Add(CreateStageItem(_stageItems.Count, s, theme));
            }

            // 更新触发按键文案与可用状态
            string trigText = isLocked ? "LOCKED" : (curStage >= 0 ? GetStageTriggerText(curStage) : "NO STAGE");
            SetTextIfChanged(_stageTriggerText, trigText);
            _stageTriggerBtn.interactable = !isLocked && curStage >= 0;

            // 单级行尺寸与视口排版 (精简 24px / 展开自适应多行网格仓)
            float rowMargin = 2f;
            float totalItemsHeight = 0f;
            float rowW = currentW - 10f * s;
            float halfRowW = rowW * 0.5f;
            float availW = rowW - 16f * s;
            float chipSize = 20f * s;
            float chipGap = 3f * s;
            int chipsPerRow = Mathf.Max(1, Mathf.FloorToInt((availW + chipGap) / (chipSize + chipGap)));

            for (int i = 0; i < displayCount; i++)
            {
                StageItemUI it = _stageItems[i];
                StageDeltaVInfo stg = sortedStages[i];
                bool isAct = stg.IsActive || (stg.Stage == curStage);
                if (!it.HasUserToggled)
                {
                    it.IsExpanded = isAct;
                }
                
                float h;
                if (!it.IsExpanded)
                {
                    h = 24f;
                }
                else
                {
                    int pCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                    if (pCount == 0)
                    {
                        h = 36f;
                    }
                    else
                    {
                        int rows = Mathf.CeilToInt((float)pCount / chipsPerRow);
                        float bayH = rows * chipSize + (rows - 1) * chipGap;
                        h = 24f + (bayH / s) + 6f;
                    }
                }
                totalItemsHeight += (h + rowMargin) * s;
            }

            float headerH = 22f * s;
            float footerH = 24f * s;
            float vpH = Mathf.Max(30f * s, currentH - headerH - footerH);

            if (_scrollContentRt != null)
            {
                float contentH = Mathf.Max(vpH, totalItemsHeight);
                SetSizeDeltaIfChanged(_scrollContentRt, new Vector2(currentW - 10f * s, contentH));
                if (totalItemsHeight <= vpH + 1f)
                {
                    SetAnchoredPositionIfChanged(_scrollContentRt, Vector2.zero);
                }
            }

            float currentY = 0f;

            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (i >= displayCount)
                {
                    SetActiveIfChanged(item.Root, false);
                    continue;
                }

                StageDeltaVInfo stg = sortedStages[i];
                item.StageNumber = stg.Stage;
                bool isActive = stg.IsActive || (stg.Stage == curStage);
                item.IsActiveStage = isActive;
                item.IsBurning = isActive && (_cachedThrottle > 0.01f || _cachedVerticalSpeed > 1f || stg.BurnTime > 0.01);

                if (!item.HasUserToggled)
                {
                    item.IsExpanded = isActive;
                }

                int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                bool hasIcons = partIconCount > 0;
                bool hasDv = stg.DeltaV > 0.01 || stg.BurnTime > 0.01;

                float itemH;
                float partsBayH = 0f;
                if (!item.IsExpanded)
                {
                    itemH = 24f;
                }
                else
                {
                    if (partIconCount == 0)
                    {
                        itemH = 36f;
                    }
                    else
                    {
                        int rows = Mathf.CeilToInt((float)partIconCount / chipsPerRow);
                        partsBayH = rows * chipSize + (rows - 1) * chipGap;
                        itemH = 24f + (partsBayH / s) + 6f;
                    }
                }

                bool hasProp = isActive;
                float propFrac = isActive ? _cachedPropFrac : 0f;

                item.TargetPropFrac = propFrac;
                if (item.CurrentPropFrac < 0.001f && propFrac > 0.001f)
                {
                    item.CurrentPropFrac = propFrac;
                }

                SetActiveIfChanged(item.Root, true);
                SetSizeDeltaIfChanged(item.RootRt, new Vector2(rowW, itemH * s));
                SetAnchoredPositionIfChanged(item.RootRt, new Vector2(0f, -currentY - (itemH * 0.5f * s)));
                currentY += (itemH + rowMargin) * s;

                // 顶栏元素垂直锚定 (展开模式固定于单级卡片顶部，精简模式居中)
                float topElementsY = item.IsExpanded ? (itemH * 0.5f * s) - (12f * s) : 0f;

                // 展开折叠切换按钮
                if (item.ToggleExpandText != null)
                {
                    SetTextIfChanged(item.ToggleExpandText, item.IsExpanded ? "▼" : "▶");
                }
                if (item.ToggleExpandBtn != null)
                {
                    SetAnchoredPositionIfChanged(item.ToggleExpandBtn.GetComponent<RectTransform>(), new Vector2(-halfRowW + 7f * s, topElementsY));
                }

                // 行背景高亮与分级徽章
                SetTextIfChanged(item.BadgeText, GetStageBadge(stg.Stage));
                SetSizeDeltaIfChanged(item.RowHighlightBg.rectTransform, new Vector2(rowW, (itemH - 2f) * s));
                SetColorIfChanged(item.RowHighlightBg, isActive ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear);

                if (isActive)
                {
                    SetColorIfChanged(item.BadgeBg, theme.AccentPrimary);
                    SetColorIfChanged(item.BadgeText, style.GetTextColor(TextStyleRole.InverseOnAccent, theme));
                }
                else
                {
                    SetColorIfChanged(item.BadgeBg, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                    SetColorIfChanged(item.BadgeText, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                }

                // Badge 位置 (紧随折叠按钮右侧)
                SetAnchoredPositionIfChanged(item.BadgeBg.rectTransform, new Vector2(-halfRowW + 24f * s, topElementsY));

                // 推进剂微量程光条 (贴附单级行下边缘)
                if (hasProp)
                {
                    SetActiveIfChanged(item.PropBarRoot, true);
                    SetSizeDeltaIfChanged(item.PropBarRootRt, new Vector2(rowW, 3f * s));
                    SetAnchoredPositionIfChanged(item.PropBarRootRt, new Vector2(0f, -itemH * 0.5f * s + 1.5f * s));
                    SetSizeDeltaIfChanged(item.PropTrack.rectTransform, new Vector2(rowW, 1.5f * s));
                    SetSizeDeltaIfChanged(item.PropFill.rectTransform, new Vector2(rowW * propFrac, 2f * s));
                    SetAnchoredPositionIfChanged(item.PropFill.rectTransform, new Vector2(-halfRowW, 0f));
                }
                else
                {
                    SetActiveIfChanged(item.PropBarRoot, false);
                }

                // 分割微线
                SetSizeDeltaIfChanged(item.Separator.rectTransform, new Vector2(rowW, 1f * s));
                SetAnchoredPositionIfChanged(item.Separator.rectTransform, new Vector2(0f, -itemH * 0.5f * s));

                // 根据分级形态排布三类状态：
                if (!hasIcons && !hasDv)
                {
                    // === Case A: 空分级 (Empty Stage) ===
                    if (item.EmptySlotText != null)
                    {
                        SetActiveIfChanged(item.EmptySlotText.gameObject, true);
                        SetAnchoredPositionIfChanged(item.EmptySlotText.rectTransform, new Vector2(-halfRowW + 40f * s, topElementsY));
                        SetTextIfChanged(item.EmptySlotText, I18n.Tr("WIDGET_CTRL_EMPTY_STAGE", "[空级]"));
                    }
                    SetActiveIfChanged(item.StageDvText.gameObject, false);
                    SetActiveIfChanged(item.StageMetaText.gameObject, false);
                    SetActiveIfChanged(item.IconsContainer, false);

                    // 仅在空白分级右侧显示专属删除按键
                    SetActiveIfChanged(item.DeleteStageBtn.gameObject, !isActive);
                    SetAnchoredPositionIfChanged(item.DeleteStageBtn.GetComponent<RectTransform>(), new Vector2(halfRowW - 12f * s, topElementsY));
                }
                else if (!hasDv)
                {
                    // === Case B: 纯动作级 (分离器、降落伞等纯功能级，无 ΔV) ===
                    if (item.EmptySlotText != null) SetActiveIfChanged(item.EmptySlotText.gameObject, false);
                    SetActiveIfChanged(item.DeleteStageBtn.gameObject, false);
                    SetActiveIfChanged(item.StageDvText.gameObject, false);

                    int partHash = ComputePartHash(stg.PartIcons);
                    if (item.IsExpanded)
                    {
                        SetActiveIfChanged(item.StageMetaText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageMetaText.rectTransform, new Vector2(75f * s, 12f * s));
                        SetAnchoredPositionIfChanged(item.StageMetaText.rectTransform, new Vector2(halfRowW - 40f * s, topElementsY));
                        SetTextIfChanged(item.StageMetaText, $"{partIconCount} PARTS");

                        SetActiveIfChanged(item.IconsContainer, true);
                        float bayCenterY = (itemH * 0.5f * s) - (24f * s) - (partsBayH * 0.5f);
                        SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(availW, partsBayH));
                        SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(0f, bayCenterY));

                        bool chipsDirty = partHash != item.LastPartHash ||
                                          Mathf.Abs(item.LastRenderedAvailW - availW) > 0.5f ||
                                          item.LastRenderedExpanded != item.IsExpanded ||
                                          item.LastRenderedActive != isActive;
                        if (chipsDirty)
                        {
                            item.LastPartHash = partHash;
                            item.LastRenderedAvailW = availW;
                            item.LastRenderedExpanded = item.IsExpanded;
                            item.LastRenderedActive = isActive;
                            ArrangeIconChipsGrid(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, availW, partsBayH, chipSize, chipGap, chipsPerRow);
                        }
                    }
                    else
                    {
                        // 精简模式：展示主要部件芯片 + 数量概要
                        SetActiveIfChanged(item.StageMetaText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageMetaText.rectTransform, new Vector2(75f * s, 12f * s));
                        SetAnchoredPositionIfChanged(item.StageMetaText.rectTransform, new Vector2(halfRowW - 40f * s, 0f));
                        SetTextIfChanged(item.StageMetaText, $"{partIconCount} PARTS");

                        SetActiveIfChanged(item.IconsContainer, true);
                        float compAvailW = rowW - 44f * s - 80f * s;
                        SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(compAvailW, 18f * s));
                        SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(-halfRowW + 38f * s + compAvailW * 0.5f, 0f));

                        bool chipsDirty = partHash != item.LastPartHash ||
                                          Mathf.Abs(item.LastRenderedAvailW - compAvailW) > 0.5f ||
                                          item.LastRenderedExpanded != item.IsExpanded ||
                                          item.LastRenderedActive != isActive;
                        if (chipsDirty)
                        {
                            item.LastPartHash = partHash;
                            item.LastRenderedAvailW = compAvailW;
                            item.LastRenderedExpanded = item.IsExpanded;
                            item.LastRenderedActive = isActive;
                            ArrangeIconChipsCompact(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, compAvailW);
                        }
                    }
                }
                else
                {
                    // === Case C: 动力推进级 (有 ΔV 与发动机) ===
                    if (item.EmptySlotText != null) SetActiveIfChanged(item.EmptySlotText.gameObject, false);
                    SetActiveIfChanged(item.DeleteStageBtn.gameObject, false);

                    int partHash = ComputePartHash(stg.PartIcons);
                    int burnSec = Mathf.Max(0, (int)stg.BurnTime);
                    if (burnSec != item.LastRenderedBurnSec || Math.Abs(stg.TWR - item.LastRenderedTwr) >= 0.02 || item.CachedMetaStr == null)
                    {
                        item.LastRenderedBurnSec = burnSec;
                        item.LastRenderedTwr = stg.TWR;
                        int min = burnSec / 60;
                        int sec = burnSec % 60;
                        item.CachedMetaStr = stg.TWR > 0.01 
                            ? $"⏱ {min:00}:{sec:00} · {stg.TWR:F2}T" 
                            : $"⏱ {min:00}:{sec:00} · {stg.Isp:F0}s";
                    }
                    string metaStr = item.CachedMetaStr;

                    if (Math.Abs(stg.DeltaV - item.LastRenderedDv) >= 1.0 || item.CachedDvStr == null)
                    {
                        item.LastRenderedDv = stg.DeltaV;
                        item.CachedDvStr = $"{stg.DeltaV:N0} m/s";
                    }
                    string dvStr = item.CachedDvStr;

                    if (item.IsExpanded)
                    {
                        // 展开模式：顶行是高反差 ΔV 与推重比/时间，底行是多行流式零件网格仓
                        float rightColW = Mathf.Clamp(rowW * 0.45f, 55f * s, 95f * s);
                        SetActiveIfChanged(item.StageDvText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageDvText.rectTransform, new Vector2(rightColW, 13f * s));
                        SetAnchoredPositionIfChanged(item.StageDvText.rectTransform, new Vector2(halfRowW - rightColW * 0.5f - 4f * s, topElementsY + 3f * s));
                        SetTextIfChanged(item.StageDvText, dvStr);
                        ApplyText(item.StageDvText, isActive ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

                        SetActiveIfChanged(item.StageMetaText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageMetaText.rectTransform, new Vector2(rightColW, 10f * s));
                        SetAnchoredPositionIfChanged(item.StageMetaText.rectTransform, new Vector2(halfRowW - rightColW * 0.5f - 4f * s, topElementsY - 7f * s));
                        SetTextIfChanged(item.StageMetaText, metaStr);

                        if (hasIcons)
                        {
                            SetActiveIfChanged(item.IconsContainer, true);
                            float bayCenterY = (itemH * 0.5f * s) - (24f * s) - (partsBayH * 0.5f);
                            SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(availW, partsBayH));
                            SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(0f, bayCenterY));

                            bool chipsDirty = partHash != item.LastPartHash ||
                                              Mathf.Abs(item.LastRenderedAvailW - availW) > 0.5f ||
                                              item.LastRenderedExpanded != item.IsExpanded ||
                                              item.LastRenderedActive != isActive;
                            if (chipsDirty)
                            {
                                item.LastPartHash = partHash;
                                item.LastRenderedAvailW = availW;
                                item.LastRenderedExpanded = item.IsExpanded;
                                item.LastRenderedActive = isActive;
                                ArrangeIconChipsGrid(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, availW, partsBayH, chipSize, chipGap, chipsPerRow);
                            }
                        }
                        else
                        {
                            SetActiveIfChanged(item.IconsContainer, false);
                        }
                    }
                    else
                    {
                        // 精简模式 (24px)：单行徽章 + 紧凑主要部件/溢出计数 + 极简 ΔV
                        SetActiveIfChanged(item.StageMetaText.gameObject, false);
                        float dvW = Mathf.Clamp(rowW * 0.42f, 45f * s, 75f * s);

                        SetActiveIfChanged(item.StageDvText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageDvText.rectTransform, new Vector2(dvW, 14f * s));
                        SetAnchoredPositionIfChanged(item.StageDvText.rectTransform, new Vector2(halfRowW - dvW * 0.5f - 4f * s, 0f));
                        SetTextIfChanged(item.StageDvText, dvStr);
                        ApplyText(item.StageDvText, isActive ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

                        if (hasIcons)
                        {
                            float compAvailW = rowW - 38f * s - dvW - 4f * s;
                            if (compAvailW >= 18f * s)
                            {
                                SetActiveIfChanged(item.IconsContainer, true);
                                SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(compAvailW, 18f * s));
                                SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(-halfRowW + 38f * s + compAvailW * 0.5f, 0f));

                                bool chipsDirty = partHash != item.LastPartHash ||
                                                  Mathf.Abs(item.LastRenderedAvailW - compAvailW) > 0.5f ||
                                                  item.LastRenderedExpanded != item.IsExpanded ||
                                                  item.LastRenderedActive != isActive;
                                if (chipsDirty)
                                {
                                    item.LastPartHash = partHash;
                                    item.LastRenderedAvailW = compAvailW;
                                    item.LastRenderedExpanded = item.IsExpanded;
                                    item.LastRenderedActive = isActive;
                                    ArrangeIconChipsCompact(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, compAvailW);
                                }
                            }
                            else
                            {
                                SetActiveIfChanged(item.IconsContainer, false);
                            }
                        }
                        else
                        {
                            SetActiveIfChanged(item.IconsContainer, false);
                        }
                    }
                }
            }
        }

        private static int ComputePartHash(IReadOnlyList<StagePartIconData> partIcons)
        {
            if (partIcons == null || partIcons.Count == 0) return 0;
            int hash = partIcons.Count;
            for (int k = 0; k < partIcons.Count; k++)
            {
                hash = unchecked((hash * 31) ^ (int)partIcons[k].PartFlightId ^ (partIcons[k].Count << 16));
            }
            return hash;
        }

        private void ArrangeIconChipsGrid(StageItemUI item, StageDeltaVInfo stg, Texture currentAtlas, bool isUsingStockAtlas, bool isActive, ThemeConfig theme, WidgetStyleManager style, float s, float containerWidth, float containerHeight, float chipSize, float chipGap, int chipsPerRow)
        {
            int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
            int displayedChips = Mathf.Min(partIconCount, item.IconChips.Count);

            for (int c = 0; c < item.IconChips.Count; c++)
            {
                StageIconChipUI chip = item.IconChips[c];
                if (c < displayedChips)
                {
                    chip.Root.SetActive(true);
                    StagePartIconData partData = stg.PartIcons[c];
                    chip.PartData = partData;
                    chip.PartFlightId = partData.PartFlightId;
                    chip.StageNumber = stg.Stage;
                    chip.PartIndex = c;

                    chip.RootRt.SetSizeDeltaSafe(new Vector2(chipSize, chipSize));
                    int row = c / chipsPerRow;
                    int col = c % chipsPerRow;
                    float chipX = -containerWidth * 0.5f + (chipSize * 0.5f) + col * (chipSize + chipGap);
                    float chipY = (containerHeight * 0.5f) - (chipSize * 0.5f) - row * (chipSize + chipGap);
                    chip.RootRt.SetAnchoredPositionSafe(new Vector2(chipX, chipY));

                    float rawSize = chipSize - 2f * s;
                    chip.IconRawImage.rectTransform.SetSizeDeltaSafe(new Vector2(rawSize, rawSize));

                    if (chip.IconRawImage.texture != currentAtlas)
                    {
                        chip.IconRawImage.texture = currentAtlas;
                    }

                    Rect uv;
                    int iconIndex = partData.IconTypeIndex > 0 
                        ? partData.IconTypeIndex 
                        : StageIconAtlasGenerator.GetIconIndex(partData.IconType);

                    if (isUsingStockAtlas)
                    {
                        if (partData.HasStockUv && partData.StockUvRect.width > 0.01f && partData.StockUvRect.width < 0.5f)
                        {
                            uv = partData.StockUvRect;
                        }
                        else if (StockStageIconService.Provider != null)
                        {
                            uv = StockStageIconService.Provider.GetStockIconUv(iconIndex);
                        }
                        else
                        {
                            uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                        }
                    }
                    else
                    {
                        uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                    }
                    if (chip.IconRawImage.uvRect != uv) chip.IconRawImage.uvRect = uv;
                    Color targetCol = isActive 
                        ? theme.AccentPrimary 
                        : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    if (chip.IconRawImage.color != targetCol) chip.IconRawImage.color = targetCol;

                    if (partData.Count > 1)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        SetTextIfChanged(chip.MultiplierText, $"×{partData.Count}");
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                    }
                    else
                    {
                        chip.MultiplierText.gameObject.SetActive(false);
                    }
                }
                else
                {
                    chip.Root.SetActive(false);
                }
            }
        }

        private void ArrangeIconChipsCompact(StageItemUI item, StageDeltaVInfo stg, Texture currentAtlas, bool isUsingStockAtlas, bool isActive, ThemeConfig theme, WidgetStyleManager style, float s, float containerWidth)
        {
            int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
            float chipSize = 16f * s;
            float chipSpacing = 18f * s;
            int maxVisible = Mathf.Min(2, Mathf.FloorToInt(containerWidth / chipSpacing));
            int displayedChips = Mathf.Min(partIconCount, Mathf.Max(1, maxVisible));

            for (int c = 0; c < item.IconChips.Count; c++)
            {
                StageIconChipUI chip = item.IconChips[c];
                if (c < displayedChips)
                {
                    chip.Root.SetActive(true);
                    StagePartIconData partData = stg.PartIcons[c];
                    chip.PartData = partData;
                    chip.PartFlightId = partData.PartFlightId;
                    chip.StageNumber = stg.Stage;
                    chip.PartIndex = c;

                    chip.RootRt.SetSizeDeltaSafe(new Vector2(chipSize, chipSize));
                    float chipX = -containerWidth * 0.5f + (chipSize * 0.5f) + c * chipSpacing;
                    chip.RootRt.SetAnchoredPositionSafe(new Vector2(chipX, 0f));

                    float rawSize = chipSize - 2f * s;
                    chip.IconRawImage.rectTransform.SetSizeDeltaSafe(new Vector2(rawSize, rawSize));

                    if (chip.IconRawImage.texture != currentAtlas)
                    {
                        chip.IconRawImage.texture = currentAtlas;
                    }

                    Rect uv;
                    int iconIndex = partData.IconTypeIndex > 0 
                        ? partData.IconTypeIndex 
                        : StageIconAtlasGenerator.GetIconIndex(partData.IconType);

                    if (isUsingStockAtlas)
                    {
                        if (partData.HasStockUv && partData.StockUvRect.width > 0.01f && partData.StockUvRect.width < 0.5f)
                        {
                            uv = partData.StockUvRect;
                        }
                        else if (StockStageIconService.Provider != null)
                        {
                            uv = StockStageIconService.Provider.GetStockIconUv(iconIndex);
                        }
                        else
                        {
                            uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                        }
                    }
                    else
                    {
                        uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                    }
                    if (chip.IconRawImage.uvRect != uv) chip.IconRawImage.uvRect = uv;
                    Color targetCol = isActive 
                        ? theme.AccentPrimary 
                        : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    if (chip.IconRawImage.color != targetCol) chip.IconRawImage.color = targetCol;

                    if (c == displayedChips - 1 && partIconCount > displayedChips)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        SetTextIfChanged(chip.MultiplierText, $"+{partIconCount - displayedChips + 1}");
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                    }
                    else if (partData.Count > 1)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        SetTextIfChanged(chip.MultiplierText, $"×{partData.Count}");
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                    }
                    else
                    {
                        chip.MultiplierText.gameObject.SetActive(false);
                    }
                }
                else
                {
                    chip.Root.SetActive(false);
                }
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeInHierarchy) return;

            float dt = CustomAnimationDeltaTime >= 0f ? CustomAnimationDeltaTime : Time.unscaledDeltaTime;
            float time = CustomAnimationTime >= 0f ? CustomAnimationTime : Time.unscaledTime;
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color inverseText = style.GetTextColor(TextStyleRole.InverseOnAccent, theme);

            // 1. 底栏分级点火触发按键击发回弹与战备状态
            if (_stageTriggerBtn != null && _stageTriggerBg != null)
            {
                if (_stageTriggerRecoilTimer > 0f)
                {
                    _stageTriggerRecoilTimer -= dt;
                    float recoilProgress = Mathf.Clamp01(_stageTriggerRecoilTimer / 0.22f);
                    float recoilScale = Mathf.Lerp(1.0f, 0.94f, recoilProgress);
                    _stageTriggerBtn.transform.SetLocalScaleSafe(new Vector3(recoilScale, recoilScale, 1f));
                }
                else if (_stageTriggerBtn.transform.localScale.x != 1f)
                {
                    _stageTriggerBtn.transform.SetLocalScaleSafe(Vector3.one);
                }

                if (_stageTriggerFlashTimer > 0f)
                {
                    _stageTriggerFlashTimer -= dt;
                    float flashP = Mathf.Clamp01(_stageTriggerFlashTimer / 0.28f);
                    _stageTriggerBg.SetColor(Color.Lerp(theme.AccentPrimary, inverseText, flashP * 0.70f));
                }
                else if (_stageTriggerBtn.interactable)
                {
                    _stageTriggerBg.SetColor(theme.AccentPrimary);
                }
                else
                {
                    _stageTriggerBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                }
            }

            // 2. 分级行推进剂阻尼与平滑预警
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;

                // A. 激活级背景稳态高光与点火冲击淡出
                if (item.IsActiveStage)
                {
                    if (item.TransitionFlashTimer > 0f)
                    {
                        item.TransitionFlashTimer -= dt;
                        float flashP = Mathf.Clamp01(item.TransitionFlashTimer / 0.4f);
                        item.RowHighlightBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, Mathf.Lerp(0.12f, 0.38f, flashP)));
                    }
                    else
                    {
                        item.RowHighlightBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f));
                    }
                }

                // B. 推进剂平滑阻尼与柔和警示
                if (item.PropBarRoot != null && item.PropBarRoot.activeSelf)
                {
                    if (Mathf.Abs(item.CurrentPropFrac - item.TargetPropFrac) > 0.001f)
                    {
                        item.CurrentPropFrac = Mathf.MoveTowards(item.CurrentPropFrac, item.TargetPropFrac, dt * 2.2f);
                        float barW = item.PropTrack.rectTransform.sizeDelta.x;
                        item.PropFill.rectTransform.SetSizeDeltaSafe(new Vector2(barW * item.CurrentPropFrac, 2f * s));
                    }

                    if (item.CurrentPropFrac <= 0.05f)
                    {
                        // 极低液位：平滑 1.5Hz 红色柔和告警脉冲 (优雅不刺眼)
                        float dangerPulse = 0.65f + 0.35f * Mathf.Sin(time * 9.4f);
                        Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
                        item.PropFill.SetColor(WidgetStyleManager.WithAlpha(dangerCol, dangerPulse));
                    }
                    else if (item.CurrentPropFrac <= 0.20f)
                    {
                        // 20% 低液位：柔和 0.8Hz 琥珀色提示 (Calm Amber Breath)
                        float warnPulse = 0.75f + 0.25f * Mathf.Sin(time * 5.0f);
                        Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                        item.PropFill.SetColor(WidgetStyleManager.WithAlpha(warnCol, warnPulse));
                    }
                    else
                    {
                        item.PropFill.SetColor(theme.AccentPrimary);
                    }
                }

                // C. 部件芯片悬停浮起微动效
                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    StageIconChipUI chip = item.IconChips[c];
                    if (!chip.Root.activeSelf) continue;

                    float targetScale = chip.IsHovered ? 1.12f : 1.0f;
                    if (Mathf.Abs(chip.CurrentScale - targetScale) > 0.002f)
                    {
                        chip.CurrentScale = Mathf.MoveTowards(chip.CurrentScale, targetScale, dt * 7.5f);
                        chip.RootRt.SetLocalScaleSafe(new Vector3(chip.CurrentScale, chip.CurrentScale, 1f));
                    }

                    if (chip.IsHovered)
                    {
                        chip.ChipOutline.SetColor(theme.AccentPrimary);
                        chip.ChipBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.30f));
                    }
                    else
                    {
                        chip.ChipOutline.SetColor(WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost));
                        chip.ChipBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                    }
                }
            }
        }

        #region 交互响应回调 (Interactive Callbacks)

        private void OnAddStageTopClicked()
        {
            StockStageActionService.InsertStage(_highestStageNumber + 1);
        }

        private void OnToggleAllExpanded()
        {
            _allExpanded = !_allExpanded;
            if (_toggleAllText != null)
            {
                _toggleAllText.text = _allExpanded ? "▼" : "▶";
            }
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                item.IsExpanded = _allExpanded;
                item.HasUserToggled = true;
                if (item.ToggleExpandText != null)
                {
                    item.ToggleExpandText.text = _allExpanded ? "▼" : "▶";
                }
            }
        }

        private void OnToggleStageExpanded(StageItemUI item)
        {
            if (item == null) return;
            item.IsExpanded = !item.IsExpanded;
            item.HasUserToggled = true;
            if (item.ToggleExpandText != null)
            {
                item.ToggleExpandText.text = item.IsExpanded ? "▼" : "▶";
            }
        }

        private void OnDeleteStageClicked(int stageIndex)
        {
            StockStageActionService.DeleteStage(stageIndex);
        }

        private void OnStatusLockClicked()
        {
            _stageTriggerFlashTimer = 0.16f;
            StockStageActionService.ToggleStagingLock();
        }

        private void OnStageTriggerClicked()
        {
            _stageTriggerRecoilTimer = 0.22f;
            _stageTriggerFlashTimer = 0.28f;
            StockStageActionService.ActivateNextStage();
        }

        internal void OnChipPointerEnter(StageIconChipUI chip)
        {
            if (chip == null) return;
            chip.IsHovered = true;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;

            // 1. 场景 3D 高亮
            if (chip.PartFlightId > 0)
            {
                StockStageActionService.SetPartHighlight(chip.PartFlightId, true, theme.AccentPrimary);
            }

            // 2. 悬浮提示框 (Tooltip)
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(true);
                string title = !string.IsNullOrEmpty(chip.PartData.PartTitle) ? chip.PartData.PartTitle : chip.PartData.IconType;
                if (chip.PartData.Count > 1) title += $" (×{chip.PartData.Count})";
                SetTextIfChanged(_tooltipTitle, title);

                string dragHint = I18n.Tr("STG_DRAG_MOVE_HINT", "[拖拽跨级移动]");
                string sub = chip.PartData.PropellantFraction >= 0f 
                    ? $"{chip.PartData.PropellantName?.ToUpperInvariant()} {(chip.PartData.PropellantFraction * 100f):F0}% · {dragHint}" 
                    : $"STAGE S{chip.StageNumber:00} · {dragHint}";
                SetTextIfChanged(_tooltipSub, sub);

                Vector3 chipWorld = chip.RootRt.position;
                _tooltipRt.position = chipWorld + new Vector3(0f, 22f * s, 0f);
            }
        }

        internal void OnChipPointerExit(StageIconChipUI chip)
        {
            if (chip == null) return;
            chip.IsHovered = false;
            if (chip.PartFlightId > 0)
            {
                StockStageActionService.SetPartHighlight(chip.PartFlightId, false);
            }
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(false);
            }
        }

        internal void OnChipBeginDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (chip == null) return;
            if (_tooltipRoot != null) _tooltipRoot.SetActive(false);

            if (_dragGhostRoot != null)
            {
                _dragGhostRoot.SetActive(true);
                _dragGhostIcon.texture = chip.IconRawImage.texture;
                _dragGhostIcon.uvRect = chip.IconRawImage.uvRect;
                if (chip.PartData.Count > 1)
                {
                    _dragGhostMult.gameObject.SetActive(true);
                    SetTextIfChanged(_dragGhostMult, $"×{chip.PartData.Count}");
                }
                else
                {
                    _dragGhostMult.gameObject.SetActive(false);
                }
                _dragGhostRt.position = eventData.position;
            }
        }

        internal void OnChipDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null && _dragGhostRoot.activeSelf)
            {
                _dragGhostRt.position = eventData.position;
            }

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(item.RootRt, eventData.position, eventData.pressEventCamera);
                if (isHovered && item.StageNumber != chip.StageNumber)
                {
                    item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.20f);
                }
                else
                {
                    item.RowHighlightBg.color = item.IsActiveStage ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear;
                }
            }
        }

        internal void OnChipEndDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null) _dragGhostRoot.SetActive(false);

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            StageItemUI targetItem = null;
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                item.RowHighlightBg.color = item.IsActiveStage ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear;
                if (RectTransformUtility.RectangleContainsScreenPoint(item.RootRt, eventData.position, eventData.pressEventCamera))
                {
                    targetItem = item;
                }
            }

            if (targetItem != null && targetItem.StageNumber != chip.StageNumber)
            {
                StockStageActionService.MovePartToStage(chip.PartFlightId, chip.StageNumber, chip.PartIndex, targetItem.StageNumber);
            }

            StockStageActionService.ClearAllHighlights();
        }

        internal void OnStageBadgeBeginDrag(StageItemUI item, PointerEventData eventData)
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.35f);
            if (_stageDropIndicatorGo != null)
            {
                _stageDropIndicatorGo.SetActive(true);
                _stageDropIndicatorGo.transform.SetAsLastSibling();
            }
        }

        internal void OnStageBadgeDrag(StageItemUI item, PointerEventData eventData)
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;
            StageItemUI hoveredItem = null;
            bool isUpperHalf = false;

            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI other = _stageItems[i];
                if (!other.Root.activeSelf) continue;

                bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(other.RootRt, eventData.position, eventData.pressEventCamera);
                if (isHovered)
                {
                    hoveredItem = other;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(other.RootRt, eventData.position, eventData.pressEventCamera, out Vector2 localPt);
                    isUpperHalf = localPt.y > 0f;
                    other.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.25f);
                }
                else
                {
                    other.RowHighlightBg.color = (other == item)
                        ? WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.35f)
                        : (other.IsActiveStage ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear);
                }
            }

            if (hoveredItem != null && _stageDropIndicatorRt != null)
            {
                float actualH = hoveredItem.RootRt.sizeDelta.y;
                float indicatorY = isUpperHalf
                    ? hoveredItem.RootRt.anchoredPosition.y + (actualH * 0.5f)
                    : hoveredItem.RootRt.anchoredPosition.y - (actualH * 0.5f);

                float rowW = RectTransform.rect.width > 10f ? RectTransform.rect.width - 10f * s : (DefaultWidth - 10f) * s;
                _stageDropIndicatorRt.sizeDelta = new Vector2(rowW, 2.5f * s);
                _stageDropIndicatorRt.anchoredPosition = new Vector2(0f, indicatorY);
                _stageDropIndicatorGo.SetActive(true);
                _stageDropIndicatorGo.transform.SetAsLastSibling();
            }
        }

        internal void OnStageBadgeEndDrag(StageItemUI item, PointerEventData eventData)
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            if (_stageDropIndicatorGo != null)
            {
                _stageDropIndicatorGo.SetActive(false);
            }

            item.RowHighlightBg.color = item.IsActiveStage ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear;
            StageItemUI targetItem = null;
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI other = _stageItems[i];
                if (!other.Root.activeSelf) continue;
                other.RowHighlightBg.color = other.IsActiveStage ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear;
                if (other != item && RectTransformUtility.RectangleContainsScreenPoint(other.RootRt, eventData.position, eventData.pressEventCamera))
                {
                    targetItem = other;
                }
            }

            if (targetItem != null && targetItem.StageNumber != item.StageNumber)
            {
                StockStageActionService.MoveStage(item.StageNumber, targetItem.StageNumber);
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            StockStageActionService.ClearAllHighlights();
            _stageItems.Clear();
            base.OnDestroy();
        }
    }

    #region 专用交互中继器 (Dedicated Event Handlers)

    public class StagePartDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageIconChipUI Chip;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OwnerWidget?.OnChipPointerEnter(Chip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OwnerWidget?.OnChipPointerExit(Chip);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipBeginDrag(Chip, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipDrag(Chip, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipEndDrag(Chip, eventData);
        }
    }

    public class StageBadgeDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageItemUI StageItem;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnBeginDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeBeginDrag(StageItem, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeDrag(StageItem, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeEndDrag(StageItem, eventData);
        }
    }

    #endregion
}
