using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets;

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
    /// <summary>
    /// 火箭分级序列仪零 GC 不可变遥测快照 (MFP-SPEC-012)
    /// </summary>
    public struct StagingSequenceState : IEquatable<StagingSequenceState>
    {
        public bool HasVessel;
        public string Title;
        public double TotalDv;
        public bool IsLocked;
        public int CurrentStage;
        public float Throttle;
        public double VerticalSpeed;
        public float PropFrac;
        public string PropName;
        public int StageCount;

        // ROCKET 2D & Diagram 模式遥测扩展 (0-GC 纯值类型)
        public float Pitch;
        public float TargetTilt;
        public bool IsFiring;
        public string TwrStr;
        public string BayTitleStr;
        public string BayFootStr;

        public bool Equals(StagingSequenceState other)
        {
            return HasVessel == other.HasVessel &&
                   string.Equals(Title, other.Title, StringComparison.Ordinal) &&
                   Math.Abs(TotalDv - other.TotalDv) < 0.5 &&
                   IsLocked == other.IsLocked &&
                   CurrentStage == other.CurrentStage &&
                   Math.Abs(Throttle - other.Throttle) < 0.01f &&
                   Math.Abs(VerticalSpeed - other.VerticalSpeed) < 0.1 &&
                   Math.Abs(PropFrac - other.PropFrac) < 0.005f &&
                   string.Equals(PropName, other.PropName, StringComparison.Ordinal) &&
                   StageCount == other.StageCount &&
                   Math.Abs(Pitch - other.Pitch) < 0.1f &&
                   Math.Abs(TargetTilt - other.TargetTilt) < 0.1f &&
                   IsFiring == other.IsFiring &&
                   string.Equals(TwrStr, other.TwrStr, StringComparison.Ordinal) &&
                   string.Equals(BayTitleStr, other.BayTitleStr, StringComparison.Ordinal) &&
                   string.Equals(BayFootStr, other.BayFootStr, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is StagingSequenceState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasVessel.GetHashCode();
                hash = (hash * 397) ^ (Title != null ? Title.GetHashCode() : 0);
                hash = (hash * 397) ^ TotalDv.GetHashCode();
                hash = (hash * 397) ^ IsLocked.GetHashCode();
                hash = (hash * 397) ^ CurrentStage;
                hash = (hash * 397) ^ Throttle.GetHashCode();
                hash = (hash * 397) ^ VerticalSpeed.GetHashCode();
                hash = (hash * 397) ^ PropFrac.GetHashCode();
                hash = (hash * 397) ^ (PropName != null ? PropName.GetHashCode() : 0);
                hash = (hash * 397) ^ StageCount;
                hash = (hash * 397) ^ Pitch.GetHashCode();
                hash = (hash * 397) ^ TargetTilt.GetHashCode();
                hash = (hash * 397) ^ IsFiring.GetHashCode();
                hash = (hash * 397) ^ (TwrStr != null ? TwrStr.GetHashCode() : 0);
                hash = (hash * 397) ^ (BayTitleStr != null ? BayTitleStr.GetHashCode() : 0);
                hash = (hash * 397) ^ (BayFootStr != null ? BayFootStr.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 火箭分级序列仪纯 C# 业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class StagingSequenceLogic : WidgetLogic<StagingSequenceState>
    {
        public string TitleTemplate = I18n.Tr("WIDGET_CTRL_STAGING", "STAGING");
        public string TotalDvToken = "{DV:TOTAL}";
        public string StageDvToken = "{DV:STAGE}";
        public string StageOrder = string.Empty;

        public readonly List<StageDeltaVInfo> ReusableSortedStages = new List<StageDeltaVInfo>();
        private static readonly Comparison<StageDeltaVInfo> _stageOrderAscending = (a, b) => a.Stage.CompareTo(b.Stage);
        private static readonly Comparison<StageDeltaVInfo> _stageOrderDescending = (a, b) => b.Stage.CompareTo(a.Stage);

        public int HighestStageNumber { get; private set; } = 0;

        public override void Reset()
        {
            CurrentState = default;
            ReusableSortedStages.Clear();
            HighestStageNumber = 0;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    Reset();
                }
                return;
            }

            string title = TelemetryTokenEngine.Evaluate(TitleTemplate, telemetry);
            double totalDv = TelemetryTokenEngine.EvaluateNumeric(TotalDvToken, telemetry);
            if (double.IsNaN(totalDv)) totalDv = telemetry.TotalDeltaV;

            bool isLocked = StockStageActionService.IsStagingLocked || telemetry.IsStageLocked;
            int curStage = telemetry.CurrentStage;
            float throttle = (float)telemetry.Throttle;
            double verticalSpeed = telemetry.VerticalSpeed;
            float rawProp = (float)telemetry.StagePropellantFraction;
            float propFrac = rawProp >= 0f ? Mathf.Clamp01(rawProp) : -1f;
            string propName = telemetry.StagePropellantName;

            IReadOnlyList<StageDeltaVInfo> stages = telemetry.StageDeltaVList;
            int stageCount = stages != null ? stages.Count : 0;

            HighestStageNumber = curStage;
            if (stages != null && stages.Count > 0)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].Stage > HighestStageNumber) HighestStageNumber = stages[i].Stage;
                }
            }

            ReusableSortedStages.Clear();
            if (stageCount > 0)
            {
                ReusableSortedStages.AddRange(stages);
                if (StageOrder == "REVERSE")
                {
                    ReusableSortedStages.Sort(_stageOrderDescending);
                }
                else
                {
                    ReusableSortedStages.Sort(_stageOrderAscending);
                }
            }
            else
            {
                ReusableSortedStages.Add(new StageDeltaVInfo(curStage, telemetry.StageDeltaV, telemetry.StageBurnTime, telemetry.TWR, 310.0, true));
            }

            // 姿态解算 (Attitude & Staging Orientation: 90° 直立, 重力转向顺势倾斜)
            float pitch = (float)telemetry.Pitch;
            float targetTilt = Math.Max(-50f, Math.Min(50f, 90f - pitch));
            bool isFiring = curStage >= 0 && (telemetry.ActiveEngines > 0 || telemetry.Throttle > 0.01);
            string twrVal = TelemetryTokenEngine.Evaluate("{TWR}", telemetry);
            int activeEng = telemetry.ActiveEngines;
            string engSuffix = activeEng > 0 ? $" ({activeEng} ENG)" : string.Empty;
            string twrStr = $"TWR {twrVal}{engSuffix}";
            string bayTitleStr = $"{I18n.Tr("WIDGET_AXIS_PITCH", "PITCH")} {pitch:F0}°";
            string bayFootStr = curStage >= 0 
                ? $"S{curStage:D2} · {(isFiring ? I18n.Tr("WIDGET_NAV_BURNING", "燃烧中") : I18n.Tr("WIDGET_ALERT_ARMED", "待发"))}" 
                : I18n.Tr("WIDGET_ROCKET_SAFED", "已保险");

            CurrentState = new StagingSequenceState
            {
                HasVessel = true,
                Title = title,
                TotalDv = totalDv,
                IsLocked = isLocked,
                CurrentStage = curStage,
                Throttle = throttle,
                VerticalSpeed = verticalSpeed,
                PropFrac = propFrac,
                PropName = propName,
                StageCount = ReusableSortedStages.Count,
                Pitch = pitch,
                TargetTilt = targetTilt,
                IsFiring = isFiring,
                TwrStr = twrStr,
                BayTitleStr = bayTitleStr,
                BayFootStr = bayFootStr
            };
        }
    }

    /// <summary>
    /// 火箭分级序列仪展示模态 (原版风格极简模式 / 标准模式 / 2D剪影实时高级模式)
    /// </summary>
    public enum StagingDisplayMode
    {
        Concise = 0,       // 原版风格极简模式 (KSP 原版极简风格：直出式 /// 橙底级标、零折叠干扰、直列式部件芯片与推进剂条)
        Standard = 1,      // 标准模式 (现代航电完整模式：双态折叠、ΔV/TWR/时序详表、战备底栏)
        Silhouette2D = 2,  // 2d剪影实时高级模式 (2D 火箭拓扑姿态卡 + 爆炸图解构 + 推进栈)

        // 兼容旧版命名别名
        Avionics = 1,
        Diagram = 2
    }

    [FlightWidget("staging_sequence", "stage_sequence", "rocket2d", "rocket", "staging_diagram", Category = WidgetCategory.Controls, DisplayName = "STAGE 垂直分级时序序列仪", Description = "垂直火箭分级序列仪：逐级剩余 ΔV、燃烧时间、推重比与单级推进剂微量程，重构原版左侧分级；包含原版风格极简模式、标准模式与 2d剪影实时高级模式。", DefaultWidgetId = "custom.staging_sequence", DefaultX = -440f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "custom.staging_sequence", "custom.stage_sequence", "core.staging_sequence", "custom.rocket", "custom.stage", "custom.staging", "core.rocket2d" })]
    public class StagingSequenceWidget : BaseFlightWidget, IAdaptiveSizeWidget, IPointerEnterHandler, IPointerExitHandler
    {
        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(100f, 48f);
        public Vector2 MaxBaseSize => new Vector2(400f, 1200f);

        // 三种模式标准物理基准设计尺寸 (Standard Canonical Reference Sizes)
        public static readonly Vector2 BaseConciseSize = new Vector2(150f, 240f);
        public static readonly Vector2 BaseStandardSize = new Vector2(160f, 260f);
        public static readonly Vector2 BaseSilhouetteSize = new Vector2(240f, 260f);

        // 极简模式底部基准线锁定 (Bottom-Baseline Anchor，向上生长杜绝向上下两端扩张漂移)
        private readonly CachedFloat _conciseBaselineY = new CachedFloat(float.NaN, 0.05f);

        // 鼠标悬停状态跟踪 (简洁模式悬浮呈现控制微键)
        private readonly Cached<bool> _isHovered = new Cached<bool>(false);

        public override Vector2 BaseSize
        {
            get
            {
                switch (_displayMode.Value)
                {
                    case StagingDisplayMode.Concise: return BaseConciseSize;
                    case StagingDisplayMode.Silhouette2D: return BaseSilhouetteSize;
                    default: return BaseStandardSize;
                }
            }
        }
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // 模态状态与模式切换控制
        private readonly Cached<StagingDisplayMode> _displayMode = new Cached<StagingDisplayMode>(StagingDisplayMode.Standard);
        public StagingDisplayMode DisplayMode => _displayMode.Value;
        private Button _modeToggleBtn;
        private Image _modeToggleBg;
        private Text _modeToggleText;

        // 左侧 2D 飞船剪影视窗与姿态机构 (Vehicle Silhouette & Attitude Assembly for Diagram mode)
        private GameObject _silhouetteBayObj;
        private Image _silhouetteBayBg;
        private Outline _silhouetteBayOutline;
        private Text _silhouetteBayTitle;
        private Button _explodedToggleBtn;
        private Image _explodedToggleBg;
        private Text _explodedToggleText;
        private GameObject _silhouetteBayFooterPill;
        private Text _silhouetteBayFooter;
        private RectTransform _rocketAssemblyRt;
        private RawImage _silhouetteRawImage;
        private ProceduralRocketSilhouetteGraphic _proceduralSilhouetteGraphic;

        // 视窗四角瞄准标框与水平刻度
        private RectTransform _retTL_H, _retTL_V, _retTR_H, _retTR_V;
        private RectTransform _retBL_H, _retBL_V, _retBR_H, _retBR_V;
        private RectTransform _tickL, _tickR;

        // 动态发动机喷流羽流 (Exhaust Plume)

        private GameObject _plumeRootObj;
        private RectTransform _plumeRt;
        private Image _plumeOuterImg;
        private Image _plumeCoreImg;

        // 姿态与视觉补间状态
        private readonly CachedFloat _currentTilt = new CachedFloat(0f, 0.05f);

        private readonly CachedFloat _targetExplodedFactor = new CachedFloat(0f, 0.01f);
        private readonly CachedFloat _currentExplodedFactor = new CachedFloat(0f, 0.01f);

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
        private readonly Cached<bool> _allExpanded = new Cached<bool>(true);
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
            public Text PropText;
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
            public StagingDisplayMode LastRenderedMode = (StagingDisplayMode)(-1);
            public double LastRenderedDv = -9999.0;
            public int LastRenderedBurnSec = -1;
            public double LastRenderedTwr = -9999.0;
            public string CachedDvStr;
            public string CachedMetaStr;
        }

        private readonly StagingSequenceLogic _logic = new StagingSequenceLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private const int InitialPooledStages = 10;
        private const int MaxDisplayedStages = 32;
        private const int MaxChipsPerStage = 24;
        private readonly List<StageItemUI> _stageItems = new List<StageItemUI>();
        private readonly CachedFloat _lastLayoutW = new CachedFloat(-1f);
        private readonly CachedFloat _lastLayoutH = new CachedFloat(-1f);
        private readonly CachedFloat _lastBayW = new CachedFloat(75f);
        private readonly CachedFloat _lastBayH = new CachedFloat(140f);


        // 可滚动分级视口组件
        private ScrollRect _scrollRect;
        private RectTransform _scrollViewportRt;
        private RectTransform _scrollContentRt;
        private string _stageOrderTemplate = string.Empty;

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
        private static string[] _cachedStageTriggers;
        private static string _cachedStageTriggerLang;

        private static string[] GenerateStageBadges()
        {
            var arr = new string[100];
            for (int i = 0; i < 100; i++) arr[i] = $"S{i:00}";
            return arr;
        }

        private static string GetStageBadge(int stage)
        {
            if ((uint)stage < (uint)_cachedStageBadges.Length) return _cachedStageBadges[stage];
            return $"S{stage:00}";
        }

        private static string GetStageTriggerText(int stage)
        {
            string curLang = I18nManager.Instance.CurrentLanguage;
            if (_cachedStageTriggers == null || _cachedStageTriggerLang != curLang)
            {
                _cachedStageTriggerLang = curLang;
                _cachedStageTriggers = new string[100];
                for (int i = 0; i < 100; i++)
                {
                    _cachedStageTriggers[i] = I18n.TrFormat("WIDGET_STAGE_TRIGGER_FMT", i);
                }
            }
            if ((uint)stage < (uint)_cachedStageTriggers.Length) return _cachedStageTriggers[stage];
            return I18n.TrFormat("WIDGET_STAGE_TRIGGER_FMT", stage);
        }

        // 缓存与脏检查标记
        private ThemeConfig _cachedTheme;
        private readonly Cached<bool> _lastStageLocked = new Cached<bool>(false);
        private readonly Cached<string> _lastTotalDvStr = new Cached<string>(string.Empty);
        private readonly CachedDouble _lastTotalDv = new CachedDouble(double.NaN);

        private int _highestStageNumber => _logic.HighestStageNumber;
        private readonly Cached<int> _lastActiveStage = new Cached<int>(-1);
        private readonly CachedFloat _stageTriggerRecoilTimer = new CachedFloat(0f, 0.001f);
        private readonly CachedFloat _stageTriggerFlashTimer = new CachedFloat(0f, 0.001f);
        public static float CustomAnimationTime = -1f;
        public static float CustomAnimationDeltaTime = -1f;

        // 几何参数 (基准像素)
        private const float DefaultWidth = 160f;
        private const float DefaultHeight = 260f;

        // 风格配置
        private string _frameModeTemplate = "FAINT";
        private string _titleTemplate = I18n.Tr("WIDGET_CTRL_STAGING", "STAGING");
        private string _totalDvToken = "{DV:TOTAL}";
        private string _stageDvToken = "{DV:STAGE}";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            string frame = GetTemplateChannel("FRAME", null);
            _frameModeTemplate = !string.IsNullOrEmpty(frame) ? frame.ToUpperInvariant() : "FAINT";
            _titleTemplate = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_CTRL_STAGING", "STAGING"));
            _totalDvToken = GetTemplateChannel("TOTAL_DV_TOKEN", "{DV:TOTAL}");
            _stageDvToken = GetTemplateChannel("STAGE_DV_TOKEN", "{DV:STAGE}");
            string order = GetTemplateChannel("ORDER", null);
            _stageOrderTemplate = !string.IsNullOrEmpty(order) ? order.ToUpperInvariant() : "STOCK";
            string mode = GetTemplateChannel("MODE", null);
            if (!string.IsNullOrEmpty(mode))
            {
                if (mode.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("CONCISE", StringComparison.OrdinalIgnoreCase) || 
                    mode.Equals("MINIMAL", StringComparison.OrdinalIgnoreCase) || 
                    mode.Equals("STOCK", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("SIMPLE", StringComparison.OrdinalIgnoreCase))
                {
                    _displayMode.Value = StagingDisplayMode.Concise;
                }
                else if (mode.Equals("2", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("DIAGRAM", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("ROCKET", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("SILHOUETTE2D", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("SILHOUETTE", StringComparison.OrdinalIgnoreCase) ||
                         mode.Equals("2D", StringComparison.OrdinalIgnoreCase))
                {
                    _displayMode.Value = StagingDisplayMode.Silhouette2D;
                }
                else
                {
                    _displayMode.Value = StagingDisplayMode.Standard;
                }
            }
            else if (config != null && !string.IsNullOrEmpty(config.WidgetId) &&
                    (config.WidgetId.Contains("rocket") || config.WidgetId.Contains("stage_diagram") || config.WidgetId.Contains("silhouette")))
            {
                _displayMode.Value = StagingDisplayMode.Silhouette2D;
            }

            _logic.TitleTemplate = _titleTemplate;
            _logic.TotalDvToken = _totalDvToken;
            _logic.StageDvToken = _stageDvToken;
            _logic.StageOrder = _stageOrderTemplate;

            // 1. 组件包围盒 (基准逻辑像素，根据模式与缩放系数进行初始几何布局)
            if (config != null)
            {
                // 自动修复旧会话可能残留的异常畸变缩放
                if (config.ScaleX > 3.0f || config.ScaleX < 0.2f) config.ScaleX = 1.0f;
                if (config.ScaleY > 3.0f || config.ScaleY < 0.2f) config.ScaleY = 1.0f;
            }
            float factorX = (config != null && config.ScaleX > 0.05f) ? config.ScaleX : 1.0f;
            float factorY = (config != null && config.ScaleY > 0.05f) ? config.ScaleY : 1.0f;
            Vector2 size = new Vector2(BaseSize.x * s * factorX, BaseSize.y * s * factorY);
            RectTransform.sizeDelta = size;
            if (_displayMode.Value == StagingDisplayMode.Concise)
            {
                _conciseBaselineY.Value = RectTransform.anchoredPosition.y - (size.y * 0.5f);
            }

            // 2. 底板卡片 (现代化暗晶毛玻璃背板 0.75 Alpha；极简模式全透明无框)
            _bgImage = CardBackground;
            bool enableFrame = _frameModeTemplate != "NONE" && _displayMode.Value != StagingDisplayMode.Concise;
            if (_bgImage != null)
            {
                _bgImage.enabled = enableFrame;
                _bgImage.color = enableFrame
                    ? WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f)
                    : Color.clear;
                _bgImage.raycastTarget = enableFrame;
            }
            _bgOutline = CardOutline;
            if (_bgOutline != null)
            {
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
                _bgOutline.enabled = enableFrame;
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
            _addStageTopText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _addStageTopText.verticalOverflow = VerticalWrapMode.Overflow;
            _addStageTopText.rectTransform.sizeDelta = addTopRt.sizeDelta;

            // 顶栏全部展开/折叠按键 [▼ / ▶]
            _toggleAllBtn = CreateButton("Btn_Toggle_All", transform, out RectTransform togTopRt, out _toggleAllBg,
                new Vector2(14f * s, 13f * s), Vector2.zero);
            _toggleAllBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _toggleAllBtn.onClick.AddListener(OnToggleAllExpanded);

            _toggleAllText = UIFactory.CreateText(_toggleAllBtn.transform, "Text", "▼", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _toggleAllText.fontStyle = FontStyle.Bold;
            _toggleAllText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _toggleAllText.verticalOverflow = VerticalWrapMode.Overflow;
            _toggleAllText.rectTransform.sizeDelta = togTopRt.sizeDelta;

            // 顶栏三态模式切换按键 [简 原版风格极简 / 标 标准模式 / 2D 2d剪影实时高级模式]
            _modeToggleBtn = CreateButton("Btn_Mode_Toggle", transform, out RectTransform modeTopRt, out _modeToggleBg,
                new Vector2(18f * s, 13f * s), Vector2.zero);
            _modeToggleBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeToggleBtn.onClick.AddListener(ToggleDisplayMode);

            string initialModeLabel;
            switch (_displayMode.Value)
            {
                case StagingDisplayMode.Concise: initialModeLabel = I18n.Tr("WIDGET_STAGING_BTN_CONCISE", "简"); break;
                case StagingDisplayMode.Silhouette2D: initialModeLabel = I18n.Tr("WIDGET_STAGING_BTN_SILHOUETTE2D", "2D"); break;
                default: initialModeLabel = I18n.Tr("WIDGET_STAGING_BTN_STANDARD", "标"); break;
            }
            _modeToggleText = UIFactory.CreateText(_modeToggleBtn.transform, "Text", initialModeLabel, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _modeToggleText.fontStyle = FontStyle.Bold;
            _modeToggleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _modeToggleText.verticalOverflow = VerticalWrapMode.Overflow;
            _modeToggleText.rectTransform.sizeDelta = modeTopRt.sizeDelta;

            _totalDvText = UIFactory.CreateText(transform, "Total_Dv_Text", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _totalDvText.fontStyle = FontStyle.Bold;

            // 顶部分割微线
            _topDivider = CreateChild<Image>("Top_Divider", transform);
            _topDivider.raycastTarget = false;

            // 构建 2D 拓扑剪影视窗 (Diagram 模式专享，初始按需激活)
            BuildSilhouetteBay(transform, size, s, theme);

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

            // 9. 注册全量微控件至标准化管理器 (支持编辑模式下钻、遮罩与 5px 网格吸附)
            RegisterLayoutControls(config, theme);

            ApplyLayout(size.x, size.y);
            ApplyTheme(theme);
        }

        private void BuildSilhouetteBay(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float bayW = 62f * s;
            float bayH = 138f * s;

            // 视窗深邃暗晶背板
            _silhouetteBayObj = UIFactory.CreatePanel(parent, "SilhouetteBay", new Vector2(bayW, bayH),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong), 1f * s);
            _silhouetteBayBg = _silhouetteBayObj.GetComponent<Image>();
            _silhouetteBayOutline = _silhouetteBayObj.GetComponent<Outline>();

            // 四角航电瞄准框标 (Corner Reticles)
            float retLen = 4.5f * s;
            float retW = 1.2f * s;
            Color retCol = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Heavy);
            _retTL_H = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol).GetComponent<RectTransform>();
            _retTL_V = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol).GetComponent<RectTransform>();
            _retTR_H = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol).GetComponent<RectTransform>();
            _retTR_V = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol).GetComponent<RectTransform>();
            _retBL_H = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol).GetComponent<RectTransform>();
            _retBL_V = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol).GetComponent<RectTransform>();
            _retBR_H = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol).GetComponent<RectTransform>();
            _retBR_V = UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol).GetComponent<RectTransform>();

            // 俯仰 0° 水平基准微刻度
            float tickW = 4f * s;
            float tickH = 1.2f * s;
            Color tickCol = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            _tickL = UIFactory.CreatePanel(_silhouetteBayObj.transform, "HorizTick_L", new Vector2(tickW, tickH), new Vector2(-bayW * 0.5f + tickW * 0.5f + 1f * s, 0f), tickCol).GetComponent<RectTransform>();
            _tickR = UIFactory.CreatePanel(_silhouetteBayObj.transform, "HorizTick_R", new Vector2(tickW, tickH), new Vector2(bayW * 0.5f - tickW * 0.5f - 1f * s, 0f), tickCol).GetComponent<RectTransform>();


            // 视窗顶部标牌 (动态呈现俯仰姿态 PITCH 72°)
            _silhouetteBayTitle = UIFactory.CreateText(_silhouetteBayObj.transform, "BayTitle", I18n.Tr("WIDGET_ROCKET_PROFILE", "剖面"),
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _silhouetteBayTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _silhouetteBayTitle.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(-6f * s, bayH * 0.5f - 8f * s);
            titleRt.sizeDelta = new Vector2(bayW - 24f * s, 12f * s);

            // 爆炸图快捷按键 [💥] / [🚀]
            _explodedToggleBtn = CreateButton("Btn_Exploded_Toggle", _silhouetteBayObj.transform, out RectTransform expRt, out _explodedToggleBg,
                new Vector2(16f * s, 13f * s), new Vector2(bayW * 0.5f - 11f * s, bayH * 0.5f - 8f * s));
            _explodedToggleBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _explodedToggleBtn.onClick.AddListener(ToggleExplodedView);

            _explodedToggleText = UIFactory.CreateText(_explodedToggleBtn.transform, "Text", I18n.Tr("WIDGET_STAGING_EXPLODED_SHORT", "散"), Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _explodedToggleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _explodedToggleText.verticalOverflow = VerticalWrapMode.Overflow;
            _explodedToggleText.rectTransform.sizeDelta = expRt.sizeDelta;

            // 中央飞船 2D 姿态云台机构 (Vessel Attitude & Staging Gimbal)
            _rocketAssemblyRt = CreateContainer("RocketAssembly", _silhouetteBayObj.transform,
                new Vector2(46f * s, 94f * s), new Vector2(0f, 0f));
            _rocketAssemblyRt.pivot = new Vector2(0.5f, 0.40f);

            // 2D 飞船剪影图元 (RawImage + 本地 GPU 矢量保底)
            _silhouetteRawImage = CreateChild<RawImage>("VesselSilhouette_RawImage", _rocketAssemblyRt,
                new Vector2(46f * s, 94f * s), Vector2.zero);
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = theme.AccentSecondary;

            _proceduralSilhouetteGraphic = CreateChild<ProceduralRocketSilhouetteGraphic>("ProceduralSilhouette", _rocketAssemblyRt,
                new Vector2(46f * s, 94f * s), Vector2.zero);
            _proceduralSilhouetteGraphic.raycastTarget = false;
            _proceduralSilhouetteGraphic.color = theme.AccentSecondary;

            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            bool hasBakerTex = tex != null;
            _silhouetteRawImage.gameObject.SetActive(hasBakerTex);
            _proceduralSilhouetteGraphic.gameObject.SetActive(!hasBakerTex);
            if (hasBakerTex) _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 发动机点火羽流 (Exhaust Plume)
            _plumeRt = CreateContainer("PlumeRoot", _rocketAssemblyRt,
                new Vector2(10f * s, 14f * s), new Vector2(0f, -38f * s));
            _plumeRt.pivot = new Vector2(0.5f, 1f);
            _plumeRootObj = _plumeRt.gameObject;

            _plumeOuterImg = CreateChild<Image>("PlumeOuter", _plumeRt,
                new Vector2(8f * s, 12f * s), Vector2.zero);
            _plumeOuterImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            _plumeOuterImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            _plumeOuterImg.raycastTarget = false;

            _plumeCoreImg = CreateChild<Image>("PlumeCore", _plumeRt,
                new Vector2(3.5f * s, 6f * s), Vector2.zero);
            _plumeCoreImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            _plumeCoreImg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f);
            _plumeCoreImg.raycastTarget = false;

            _plumeRootObj.SetActive(false);

            // 视窗底部状态胶囊底板 (Bottom Status Pill)
            _silhouetteBayFooterPill = UIFactory.CreatePanel(_silhouetteBayObj.transform, "BayFooterPill",
                new Vector2(bayW - 8f * s, 14f * s), new Vector2(0f, -bayH * 0.5f + 9.5f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal), 0.8f * s);

            _silhouetteBayFooter = UIFactory.CreateText(_silhouetteBayFooterPill.transform, "BayFooter", "S-- · " + I18n.Tr("WIDGET_ALERT_ARMED", "待发"),
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _silhouetteBayFooter.fontStyle = FontStyle.Bold;
            RectTransform footRt = _silhouetteBayFooter.GetComponent<RectTransform>();
            footRt.anchorMin = Vector2.zero;
            footRt.anchorMax = Vector2.one;
            footRt.sizeDelta = Vector2.zero;
            footRt.anchoredPosition = Vector2.zero;

            _silhouetteBayObj.SetActive(_displayMode.Value == StagingDisplayMode.Diagram);
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            if (_silhouetteRawImage != null)
            {
                bool hasBakerTex = tex != null;
                bool forceProcedural = _currentExplodedFactor.Value > 0.005f || !hasBakerTex;
                _silhouetteRawImage.gameObject.SetActiveSafe(!forceProcedural);
                if (_proceduralSilhouetteGraphic != null)
                {
                    _proceduralSilhouetteGraphic.gameObject.SetActiveSafe(forceProcedural);
                }
                if (hasBakerTex)
                {
                    _silhouetteRawImage.texture = tex;
                }
            }
        }

        public void SetExplodedView(bool exploded)
        {
            _targetExplodedFactor.Value = exploded ? 1f : 0f;
            _currentExplodedFactor.Value = _targetExplodedFactor.Value;
            if (_proceduralSilhouetteGraphic != null)
            {
                _proceduralSilhouetteGraphic.ExplodedFactor = _currentExplodedFactor.Value;
            }
            if (_explodedToggleText != null)
            {
                _explodedToggleText.text = exploded ? I18n.Tr("WIDGET_STAGING_ASSEMBLE_SHORT", "合") : I18n.Tr("WIDGET_STAGING_EXPLODED_SHORT", "散");
            }
        }

        public void ToggleExplodedView()
        {
            bool next = _targetExplodedFactor.Value <= 0.5f;
            SetExplodedView(next);
            string tip = next 
                ? I18n.Tr("WIDGET_STAGING_EXPLODED_BTN", "💥 爆炸图") 
                : I18n.Tr("WIDGET_STAGING_ASSEMBLE_BTN", "🚀 组装");
            MFPToastBridge.Show(tip);
        }

        private void RegisterLayoutControls(WidgetConfig config, ThemeConfig theme)
        {
            if (Title != null && Title.RootGameObject != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_title", I18n.Tr("CTRL_STAGING_TITLE", "顶栏分级标题"), Title.RootGameObject, "顶栏分级序列标题"));
            }
            if (_totalDvText != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_total_dv", I18n.Tr("CTRL_STAGING_TOTAL_DV", "总剩余ΔV标牌"), _totalDvText.gameObject, "顶栏火箭总剩余 ΔV 读数"));
            }
            if (_addStageTopBtn != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_add_btn", I18n.Tr("CTRL_STAGING_ADD_BTN", "添加分级按钮"), _addStageTopBtn.gameObject, "顶栏插入新分级按钮 [+]"));
            }
            if (_toggleAllBtn != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_toggle_all", I18n.Tr("CTRL_STAGING_TOGGLE_ALL", "全量展开折叠按钮"), _toggleAllBtn.gameObject, "顶栏全部展开/折叠切换按钮"));
            }
            if (_modeToggleBtn != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_mode_btn", I18n.Tr("CTRL_STAGING_MODE_BTN", "模式切换按钮"), _modeToggleBtn.gameObject, "顶栏模式切换按钮 (原版极简/标准/2D剪影)"));
            }
            if (_topDivider != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_top_div", I18n.Tr("CTRL_STAGING_TOP_DIV", "顶部分隔线"), _topDivider.gameObject, "顶栏与分级列表分隔微线"));
            }
            if (_scrollViewportRt != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_list", I18n.Tr("CTRL_STAGING_LIST", "分级时序列表视口"), _scrollViewportRt.gameObject, "多级火箭分级列表与部件芯片滚动视口"));
            }
            if (_silhouetteBayObj != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_2d_bay", I18n.Tr("CTRL_STAGING_2D_BAY", "2D飞船姿态与爆炸图视窗"), _silhouetteBayObj, "2D 飞船实时剪影、俯仰姿态云台与爆炸图视窗"));
            }
            if (_bottomDivider != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_bot_div", I18n.Tr("CTRL_STAGING_BOT_DIV", "底部分隔线"), _bottomDivider.gameObject, "底栏与分级列表分隔微线"));
            }
            if (_statusBadgeBtn != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "staging_status_lock", I18n.Tr("CTRL_STAGING_STATUS_LOCK", "底栏分级安全锁"), _statusBadgeBtn.gameObject, "底栏安全锁切换按键 [ARMED / LOCKED]"));
            }
            if (_stageTriggerBtn != null)
            {
                this.Controls.Register(new WidgetActionButtonControl(this, "staging_trigger_btn", I18n.Tr("CTRL_STAGING_TRIGGER_BTN", "底栏点火分级按钮"), _stageTriggerBtn.gameObject, _stageTriggerBtn, _stageTriggerBg, null, _stageTriggerText, null, "SPACE TO STAGE", OnStageTriggerClicked, false));
            }

            if (config != null)
            {
                this.Controls.BindConfigToControls(config);
            }
            if (theme != null)
            {
                this.Controls.ApplyThemeToControls(theme);
            }
        }

        private void UpdateSilhouetteBayInnerLayout(float bayW, float bayH, float s)
        {
            _lastBayW.Update(bayW);
            _lastBayH.Update(bayH);


            if (_silhouetteBayTitle != null)
            {
                RectTransform tRt = _silhouetteBayTitle.GetComponent<RectTransform>();
                tRt.anchoredPosition = new Vector2(-6f * s, bayH * 0.5f - 8f * s);
                tRt.sizeDelta = new Vector2(bayW - 24f * s, 12f * s);
            }
            if (_explodedToggleBtn != null)
            {
                RectTransform expRt = _explodedToggleBtn.GetComponent<RectTransform>();
                expRt.anchoredPosition = new Vector2(bayW * 0.5f - 11f * s, bayH * 0.5f - 8f * s);
            }
            if (_silhouetteBayFooterPill != null)
            {
                RectTransform fRt = _silhouetteBayFooterPill.GetComponent<RectTransform>();
                fRt.sizeDelta = new Vector2(bayW - 8f * s, 14f * s);
                fRt.anchoredPosition = new Vector2(0f, -bayH * 0.5f + 9.5f * s);
            }

            // 四角航电瞄准框标与水平基准刻度动态对齐视窗真实边缘
            float retLen = 4.5f * s;
            float retW = 1.2f * s;
            float tickW = 4f * s;
            float tickH = 1.2f * s;
            if (_retTL_H != null) SetAnchoredPositionIfChanged(_retTL_H, new Vector2(-bayW * 0.5f + retLen * 0.5f, bayH * 0.5f - retW * 0.5f));
            if (_retTL_V != null) SetAnchoredPositionIfChanged(_retTL_V, new Vector2(-bayW * 0.5f + retW * 0.5f, bayH * 0.5f - retLen * 0.5f));
            if (_retTR_H != null) SetAnchoredPositionIfChanged(_retTR_H, new Vector2(bayW * 0.5f - retLen * 0.5f, bayH * 0.5f - retW * 0.5f));
            if (_retTR_V != null) SetAnchoredPositionIfChanged(_retTR_V, new Vector2(bayW * 0.5f - retW * 0.5f, bayH * 0.5f - retLen * 0.5f));
            if (_retBL_H != null) SetAnchoredPositionIfChanged(_retBL_H, new Vector2(-bayW * 0.5f + retLen * 0.5f, -bayH * 0.5f + retW * 0.5f));
            if (_retBL_V != null) SetAnchoredPositionIfChanged(_retBL_V, new Vector2(-bayW * 0.5f + retW * 0.5f, -bayH * 0.5f + retLen * 0.5f));
            if (_retBR_H != null) SetAnchoredPositionIfChanged(_retBR_H, new Vector2(bayW * 0.5f - retLen * 0.5f, -bayH * 0.5f + retW * 0.5f));
            if (_retBR_V != null) SetAnchoredPositionIfChanged(_retBR_V, new Vector2(bayW * 0.5f - retW * 0.5f, -bayH * 0.5f + retLen * 0.5f));
            if (_tickL != null) SetAnchoredPositionIfChanged(_tickL, new Vector2(-bayW * 0.5f + tickW * 0.5f + 1f * s, 0f));
            if (_tickR != null) SetAnchoredPositionIfChanged(_tickR, new Vector2(bayW * 0.5f - tickW * 0.5f - 1f * s, 0f));

            UpdateSilhouetteAssemblyScale(bayW, bayH, s);
        }

        private void UpdateSilhouetteAssemblyScale(float bayW, float bayH, float s)
        {
            if (_rocketAssemblyRt == null) return;

            float availAssemblyW = Mathf.Max(24f * s, bayW - 14f * s);
            float availAssemblyH = Mathf.Max(36f * s, bayH - 34f * s);

            // 1. 获取真实火箭物理跨度与外接长宽比 (跟随火箭实际)
            float spanX = 5f;
            float spanY = 30f;
            var provider = VesselSilhouetteService.Provider;
            if (provider != null && provider.PhysicalSpanY > 0.1f)
            {
                spanX = provider.PhysicalSpanX;
                spanY = provider.PhysicalSpanY;
            }
#if KSP_RUNTIME
            else if (FlightGlobals.ActiveVessel != null)
            {
                Vector3 vSize = FlightGlobals.ActiveVessel.vesselSize;
                spanY = Mathf.Max(1f, vSize.y);
                spanX = Mathf.Max(0.5f, Mathf.Max(vSize.x, vSize.z));
            }
#endif

            float maxSpan = Mathf.Max(spanX, spanY);
            // 在 512x512 烘焙贴图中，正交相机宽度与高度均为 maxSpan * 1.15f
            float normW = Mathf.Clamp(spanX / (maxSpan * 1.15f), 0.05f, 0.95f);
            float normH = Mathf.Clamp(spanY / (maxSpan * 1.15f), 0.10f, 0.95f);

            // 2. 考虑当前实时俯仰角倾斜对视口外接矩形的影响
            float radTilt = Mathf.Abs(_currentTilt.Value) * Mathf.Deg2Rad;
            float sinT = Mathf.Sin(radTilt);
            float cosT = Mathf.Cos(radTilt);

            // 倾斜后火箭投影在屏幕上的归一化边界跨度
            float rotNormW = Mathf.Max(0.08f, normH * sinT + normW * cosT);
            float rotNormH = Mathf.Max(0.08f, normH * cosT + normW * sinT);

            // 3. 严格等比自适应缩放 (锁定 1:1，杜绝非等比拉伸)，并根据视口余量自动扩展最大填充
            float scaleFitW = (availAssemblyW * 0.92f) / rotNormW;
            float scaleFitH = (availAssemblyH * 0.92f) / rotNormH;
            float assemblyDim = Mathf.Min(scaleFitW, scaleFitH);

            assemblyDim = Mathf.Clamp(assemblyDim, 28f * s, Mathf.Max(availAssemblyW, availAssemblyH) * 2.2f);

            Vector2 assemblySize = new Vector2(assemblyDim, assemblyDim);
            SetSizeDeltaIfChanged(_rocketAssemblyRt, assemblySize);

            if (_silhouetteRawImage != null)
            {
                SetSizeDeltaIfChanged(_silhouetteRawImage.rectTransform, assemblySize);
            }
            if (_proceduralSilhouetteGraphic != null)
            {
                SetSizeDeltaIfChanged(_proceduralSilhouetteGraphic.rectTransform, assemblySize);
                float aspect = spanY / Mathf.Max(0.1f, spanX);
                _proceduralSilhouetteGraphic.RocketAspect = aspect;
            }
            if (_plumeRt != null)
            {
                float normEngineY = provider != null ? provider.NormalizedEngineBottomY : -0.85f;
                float plumeY = assemblyDim * 0.5f * (normEngineY - 0.03f);
                SetAnchoredPositionIfChanged(_plumeRt, new Vector2(0f, plumeY));
            }
        }


        // ==========================================
        // 动态长宽比排版自适应 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            if (_displayMode.Value == StagingDisplayMode.Concise)
            {
                _conciseBaselineY.Value = float.NaN;
            }

            if (_scrollViewportRt != null)
            {
                ApplyLayout(pixelSize.x, pixelSize.y);
            }
        }

        private void ApplyLayout(float width, float height)
        {
            float s = CurrentDpiScale > 0.01f ? CurrentDpiScale : 1f;
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            bool isConcise = _displayMode.Value == StagingDisplayMode.Concise;

            // 1. 顶栏排版 (极简模式不占位，悬浮微键按需呈现)
            float headerH = isConcise ? 0f : 22f * s;
            bool showTitle = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_title"));
            float titleW = showTitle ? Mathf.Clamp(width * 0.25f, 32f * s, 46f * s) : 0f;
            if (Title != null && Title.RootGameObject != null)
            {
                Title.RootGameObject.SetActive(showTitle);
                if (showTitle)
                {
                    RectTransform tRt = Title.RootGameObject.GetComponent<RectTransform>();
                    if (tRt != null)
                    {
                        tRt.anchorMin = new Vector2(0.5f, 0.5f);
                        tRt.anchorMax = new Vector2(0.5f, 0.5f);
                        tRt.pivot = new Vector2(0.5f, 0.5f);
                        tRt.SetSizeDeltaSafe(new Vector2(titleW, 16f * s));
                        tRt.SetAnchoredPositionSafe(new Vector2(-halfW + titleW * 0.5f + 6f * s, halfH - 12f * s));
                    }
                }
            }

            // 顶栏按钮弹性流式排版 (自动从左向右流动；极简模式悬浮于右上角)
            float curBtnX = -halfW + (titleW > 0f ? (titleW + 8f * s) : 6f * s);
            bool showFloating = _isHovered.Value || WidgetDragHandler.IsEditModeActive;

            if (_addStageTopBtn != null)
            {
                bool showAdd = isConcise
                    ? (showFloating && (Config == null || !Config.IsSubElementDisabled("staging_add_btn")))
                    : (Config == null || !Config.IsSubElementDisabled("staging_add_btn"));
                _addStageTopBtn.gameObject.SetActive(showAdd);
                if (showAdd)
                {
                    float addW = 12f * s;
                    RectTransform addRt = _addStageTopBtn.GetComponent<RectTransform>();
                    addRt.SetSizeDeltaSafe(new Vector2(addW, 13f * s));
                    if (_addStageTopText != null) _addStageTopText.rectTransform.SetSizeDeltaSafe(addRt.sizeDelta);
                    if (isConcise)
                    {
                        addRt.SetAnchoredPositionSafe(new Vector2(halfW - 20f * s - addW * 0.5f, halfH - 8f * s));
                    }
                    else
                    {
                        addRt.SetAnchoredPositionSafe(new Vector2(curBtnX + addW * 0.5f, halfH - 12f * s));
                        curBtnX += addW + 2f * s;
                    }
                }
            }

            if (_toggleAllBtn != null)
            {
                bool showTog = (!isConcise && _displayMode.Value == StagingDisplayMode.Standard) &&
                               (Config == null || !Config.IsSubElementDisabled("staging_toggle_all"));
                _toggleAllBtn.gameObject.SetActive(showTog);
                if (showTog)
                {
                    float togW = 12f * s;
                    RectTransform togRt = _toggleAllBtn.GetComponent<RectTransform>();
                    togRt.SetSizeDeltaSafe(new Vector2(togW, 13f * s));
                    if (_toggleAllText != null) _toggleAllText.rectTransform.SetSizeDeltaSafe(togRt.sizeDelta);
                    togRt.SetAnchoredPositionSafe(new Vector2(curBtnX + togW * 0.5f, halfH - 12f * s));
                    curBtnX += togW + 2f * s;
                }
            }

            if (_modeToggleBtn != null)
            {
                bool showMode = isConcise
                    ? (showFloating && (Config == null || !Config.IsSubElementDisabled("staging_mode_btn")))
                    : (Config == null || !Config.IsSubElementDisabled("staging_mode_btn"));
                _modeToggleBtn.gameObject.SetActive(showMode);
                if (showMode)
                {
                    float modeW = 16f * s;
                    RectTransform modeRt = _modeToggleBtn.GetComponent<RectTransform>();
                    modeRt.SetSizeDeltaSafe(new Vector2(modeW, 13f * s));
                    if (_modeToggleText != null) _modeToggleText.rectTransform.SetSizeDeltaSafe(modeRt.sizeDelta);
                    if (isConcise)
                    {
                        modeRt.SetAnchoredPositionSafe(new Vector2(halfW - modeW * 0.5f - 2f * s, halfH - 8f * s));
                    }
                    else
                    {
                        modeRt.SetAnchoredPositionSafe(new Vector2(curBtnX + modeW * 0.5f, halfH - 12f * s));
                        curBtnX += modeW + 2f * s;
                    }
                }
            }

            if (_totalDvText != null)
            {
                bool showDv = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_total_dv"));
                _totalDvText.gameObject.SetActive(showDv);
                if (showDv)
                {
                    RectTransform dvRt = _totalDvText.rectTransform;
                    float dvRightMargin = 6f * s;
                    float maxAvailDvW = (halfW - dvRightMargin) - curBtnX;
                    float dvW = Mathf.Clamp(maxAvailDvW, 36f * s, 65f * s);
                    dvRt.SetSizeDeltaSafe(new Vector2(dvW, 16f * s));
                    dvRt.SetAnchoredPositionSafe(new Vector2(halfW - dvW * 0.5f - dvRightMargin, halfH - 12f * s));
                }
            }

            if (_topDivider != null)
            {
                bool showTopDiv = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_top_div"));
                _topDivider.gameObject.SetActive(showTopDiv);
                if (showTopDiv)
                {
                    RectTransform topDivRt = _topDivider.rectTransform;
                    topDivRt.SetSizeDeltaSafe(new Vector2(width - 12f * s, 1f * s));
                    topDivRt.SetAnchoredPositionSafe(new Vector2(0f, halfH - headerH));
                }
            }

            // 2. 底栏排版 (极简模式完全去底栏)
            bool showBotDiv = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_bot_div"));
            bool showStatusLock = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_status_lock"));
            bool showTrigger = !isConcise && (Config == null || !Config.IsSubElementDisabled("staging_trigger_btn"));
            bool hasFooter = showStatusLock || showTrigger;

            float footerH = hasFooter ? 24f * s : 0f;
            if (_bottomDivider != null)
            {
                _bottomDivider.gameObject.SetActive(showBotDiv && hasFooter);
                if (showBotDiv && hasFooter)
                {
                    RectTransform botDivRt = _bottomDivider.rectTransform;
                    botDivRt.SetSizeDeltaSafe(new Vector2(width - 12f * s, 1f * s));
                    botDivRt.SetAnchoredPositionSafe(new Vector2(0f, -halfH + footerH));
                }
            }

            if (_statusBadgeBtn != null) _statusBadgeBtn.gameObject.SetActive(showStatusLock);
            if (_stageTriggerBtn != null) _stageTriggerBtn.gameObject.SetActive(showTrigger);

            if (hasFooter)
            {
                float btnY = -halfH + 11f * s;
                if (showStatusLock && showTrigger && _statusBadgeBtn != null && _stageTriggerBtn != null)
                {
                    float lockW = Mathf.Clamp(width * 0.32f, 44f * s, 68f * s);
                    float trigW = Mathf.Max(60f * s, (width - 24f * s) - lockW);

                    RectTransform statRt = _statusBadgeBtn.GetComponent<RectTransform>();
                    statRt.SetSizeDeltaSafe(new Vector2(lockW, 15f * s));
                    statRt.SetAnchoredPositionSafe(new Vector2(-halfW + 8f * s + lockW * 0.5f, btnY));
                    if (_statusBadgeText != null) _statusBadgeText.rectTransform.SetSizeDeltaSafe(statRt.sizeDelta);

                    RectTransform trigRt = _stageTriggerBtn.GetComponent<RectTransform>();
                    trigRt.SetSizeDeltaSafe(new Vector2(trigW, 15f * s));
                    trigRt.SetAnchoredPositionSafe(new Vector2(halfW - 8f * s - trigW * 0.5f, btnY));
                    if (_stageTriggerText != null) _stageTriggerText.rectTransform.SetSizeDeltaSafe(trigRt.sizeDelta);
                }
                else if (showStatusLock && _statusBadgeBtn != null)
                {
                    float lockW = width - 16f * s;
                    RectTransform statRt = _statusBadgeBtn.GetComponent<RectTransform>();
                    statRt.SetSizeDeltaSafe(new Vector2(lockW, 15f * s));
                    statRt.SetAnchoredPositionSafe(new Vector2(0f, btnY));
                    if (_statusBadgeText != null) _statusBadgeText.rectTransform.SetSizeDeltaSafe(statRt.sizeDelta);
                }
                else if (showTrigger && _stageTriggerBtn != null)
                {
                    float trigW = width - 16f * s;
                    RectTransform trigRt = _stageTriggerBtn.GetComponent<RectTransform>();
                    trigRt.SetSizeDeltaSafe(new Vector2(trigW, 15f * s));
                    trigRt.SetAnchoredPositionSafe(new Vector2(0f, btnY));
                    if (_stageTriggerText != null) _stageTriggerText.rectTransform.SetSizeDeltaSafe(trigRt.sizeDelta);
                }
            }

            // 3. 中间视口与 2D 拓扑剪影视窗排版
            float middleH = Mathf.Max(30f * s, height - headerH - footerH);
            float middleCenterY = (halfH - headerH) - (middleH * 0.5f);

            bool showBay = (!isConcise && _displayMode.Value == StagingDisplayMode.Silhouette2D) &&
                           (Config == null || !Config.IsSubElementDisabled("staging_2d_bay"));

            if (showBay && _silhouetteBayObj != null)
            {
                _silhouetteBayObj.SetActive(true);
                bool isWide = width >= 210f * s;
                if (isWide)
                {
                    // 宽屏模式：双列横向并列 (左侧 2D 剪影视窗，右侧分级序列列表)
                    float minBayW = 72f * s;
                    float maxBayW = Mathf.Max(minBayW, width - 128f * s);
                    float bayW = Mathf.Clamp(width * 0.40f, minBayW, Mathf.Min(maxBayW, 140f * s));
                    float bayH = middleH - 4f * s;
                    float bayCenterX = -halfW + 6f * s + (bayW * 0.5f);
                    SetSizeDeltaIfChanged(_silhouetteBayObj.GetComponent<RectTransform>(), new Vector2(bayW, bayH));
                    SetAnchoredPositionIfChanged(_silhouetteBayObj.GetComponent<RectTransform>(), new Vector2(bayCenterX, middleCenterY));

                    UpdateSilhouetteBayInnerLayout(bayW, bayH, s);

                    float scrollW = width - bayW - 16f * s;
                    float scrollCenterX = halfW - (scrollW * 0.5f) - 6f * s;
                    if (_scrollViewportRt != null)
                    {
                        SetSizeDeltaIfChanged(_scrollViewportRt, new Vector2(scrollW, middleH));
                        SetAnchoredPositionIfChanged(_scrollViewportRt, new Vector2(scrollCenterX, middleCenterY));
                    }
                }
                else
                {
                    // 窄屏模式：纵向上下堆叠 (上侧 2D 剪影视窗，下侧分级序列列表)
                    float bayH = Mathf.Clamp(middleH * 0.46f, 85f * s, 150f * s);
                    float bayW = width - 12f * s;

                    float bayCenterY = (halfH - headerH) - (bayH * 0.5f) - 2f * s;
                    SetSizeDeltaIfChanged(_silhouetteBayObj.GetComponent<RectTransform>(), new Vector2(bayW, bayH));
                    SetAnchoredPositionIfChanged(_silhouetteBayObj.GetComponent<RectTransform>(), new Vector2(0f, bayCenterY));

                    UpdateSilhouetteBayInnerLayout(bayW, bayH, s);

                    float scrollH = middleH - bayH - 4f * s;
                    float scrollCenterY = -halfH + footerH + (scrollH * 0.5f) + 2f * s;
                    if (_scrollViewportRt != null)
                    {
                        SetSizeDeltaIfChanged(_scrollViewportRt, new Vector2(width - 8f * s, scrollH));
                        SetAnchoredPositionIfChanged(_scrollViewportRt, new Vector2(0f, scrollCenterY));
                    }
                }
            }
            else
            {
                if (_silhouetteBayObj != null) _silhouetteBayObj.SetActive(false);
                if (_scrollViewportRt != null)
                {
                    SetSizeDeltaIfChanged(_scrollViewportRt, new Vector2(isConcise ? width : width - 8f * s, middleH));
                    SetAnchoredPositionIfChanged(_scrollViewportRt, new Vector2(0f, middleCenterY));
                }
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

            Text propTxt = UIFactory.CreateText(propRoot.transform, "Prop_Text", "", Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.InverseOnAccent, theme));
            propTxt.fontStyle = FontStyle.Bold;
            propTxt.raycastTarget = false;
            propTxt.gameObject.SetActive(false);

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
                PropText = propTxt,
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
            multRt.SetAnchoredPositionSafe(new Vector2(0f, -3f * s));

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

            // 1. 外框模式着色 (现代化暗晶毛玻璃背板 0.75 Alpha；极简模式完全去框)
            bool enableFrame = _frameModeTemplate != "NONE" && _displayMode.Value != StagingDisplayMode.Concise;
            if (_bgImage != null)
            {
                _bgImage.enabled = enableFrame;
                _bgImage.raycastTarget = enableFrame;
            }
            if (_bgOutline != null)
            {
                _bgOutline.enabled = enableFrame;
            }
            if (!enableFrame)
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
            }
            else if (_frameModeTemplate == "FAINT")
            {
                if (_bgImage != null) _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
                if (_bgOutline != null)
                {
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
            if (_modeToggleBg != null) _modeToggleBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_modeToggleText != null)
            {
                ApplyText(_modeToggleText, TextStyleRole.SecondaryValue, theme);
                switch (_displayMode.Value)
                {
                    case StagingDisplayMode.Concise:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_CONCISE", "简");
                        break;
                    case StagingDisplayMode.Silhouette2D:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_SILHOUETTE2D", "2D");
                        break;
                    default:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_STANDARD", "标");
                        break;
                }
            }
            if (_stageDropIndicatorImg != null) _stageDropIndicatorImg.color = theme.AccentPrimary;

            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            if (_statusBadgeBg != null) _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_statusBadgeText != null) ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            if (_stageTriggerBg != null) _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageTriggerText != null) ApplyText(_stageTriggerText, TextStyleRole.Label, theme);

            // 3. 2D 飞船剪影视窗与爆炸图着色
            if (_silhouetteBayBg != null)
            {
                _silhouetteBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            }
            if (_silhouetteBayOutline != null)
            {
                _silhouetteBayOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong);
            }
            if (_silhouetteBayTitle != null)
            {
                _silhouetteBayTitle.color = style.GetTextColor(TextStyleRole.Cardinal, theme);
            }
            if (_explodedToggleBg != null)
            {
                _explodedToggleBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (_explodedToggleText != null)
            {
                ApplyText(_explodedToggleText, TextStyleRole.PrimaryValue, theme);
            }
            if (_silhouetteRawImage != null)
            {
                _silhouetteRawImage.color = theme.AccentSecondary;
            }
            if (_proceduralSilhouetteGraphic != null)
            {
                _proceduralSilhouetteGraphic.color = theme.AccentSecondary;
            }
            if (_plumeOuterImg != null)
            {
                _plumeOuterImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            }
            if (_plumeCoreImg != null)
            {
                _plumeCoreImg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f);
            }
            if (_silhouetteBayFooterPill != null)
            {
                var pillBg = _silhouetteBayFooterPill.GetComponent<Image>();
                if (pillBg != null) pillBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (_silhouetteBayFooter != null)
            {
                ApplyText(_silhouetteBayFooter, TextStyleRole.PrimaryValue, theme);
            }

            // 4. 各分级项着色
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (item.ToggleExpandText != null) ApplyText(item.ToggleExpandText, TextStyleRole.SecondaryValue, theme);
                if (item.Separator != null) item.Separator.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (item.PropTrack != null) item.PropTrack.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (item.PropFill != null) item.PropFill.color = theme.AccentPrimary;
                if (item.PropText != null) ApplyText(item.PropText, TextStyleRole.InverseOnAccent, theme);

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

            // 5. Tooltip & Drag Ghost
            if (_tooltipBg != null) _tooltipBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.90f);
            if (_tooltipOutline != null) _tooltipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            if (_tooltipTitle != null) ApplyText(_tooltipTitle, TextStyleRole.PrimaryValue, theme);
            if (_tooltipSub != null) ApplyText(_tooltipSub, TextStyleRole.SecondaryValue, theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context) => base.OnUIDrawLoop(ref context);

        protected override void OnRenderState()
        {
            StagingSequenceState snap = _logic.CurrentState;
            if (!snap.HasVessel) return;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            bool isConcise = _displayMode.Value == StagingDisplayMode.Concise;

            // 1. 读取当前物理尺寸并自适应布局 (仅在尺寸变化时才调用 ApplyLayout)
            float effScaleX = (Config != null && Config.ScaleX > 0.05f) ? Config.ScaleX : 1.0f;
            float factorX = CommittedScale > 0.001f ? (effScaleX / CommittedScale) : 1.0f;
            float targetW = BaseSize.x * s * factorX;
            float currentW = isConcise ? targetW : ((RectTransform.rect.width > 10f) ? RectTransform.rect.width : targetW);
            float currentH = RectTransform.rect.height > 10f ? RectTransform.rect.height : DefaultHeight * s;
            if (Mathf.Abs(currentW - _lastLayoutW.Value) > 0.5f || Mathf.Abs(currentH - _lastLayoutH.Value) > 0.5f)
            {
                _lastLayoutW.Update(currentW);
                _lastLayoutH.Update(currentH);
                ApplyLayout(currentW, currentH);
            }

            // 2. 动态标题与全级总 ΔV
            if (Title != null && Title.Text != snap.Title)
            {
                Title.Text = snap.Title;
            }

            double totalDv = snap.TotalDv;
            if (Math.Abs(totalDv - _lastTotalDv.Value) >= 0.5 || string.IsNullOrEmpty(_lastTotalDvStr.Value))
            {
                _lastTotalDv.Update(totalDv);
                _lastTotalDvStr.Update($"{totalDv:N0} m/s");
                SetTextIfChanged(_totalDvText, _lastTotalDvStr.Value);
            }

            // 3. 分级安全锁与状态
            bool isLocked = snap.IsLocked;
            if (_lastStageLocked.Update(isLocked))
            {
                string statusText = isLocked ? I18n.Tr("WIDGET_STAGE_LOCKED", "锁定") : I18n.Tr("WIDGET_ALERT_ARMED", "待发");
                SetTextIfChanged(_statusBadgeText, statusText);
                _statusBadgeText.color = isLocked 
                    ? style.GetTextColor(TextStyleRole.Warning, theme)
                    : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }

            int curStage = snap.CurrentStage;

            // 获取当前有效图集
            Texture stockAtlas = StockStageIconService.Provider?.StockAtlas;
            bool isUsingStockAtlas = stockAtlas != null;
            Texture currentAtlas = isUsingStockAtlas ? stockAtlas : StageIconAtlasGenerator.GetAtlas();

            List<StageDeltaVInfo> sortedStages = _logic.ReusableSortedStages;

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
            string trigText = isLocked ? I18n.Tr("WIDGET_STAGE_LOCKED", "锁定") : (curStage >= 0 ? GetStageTriggerText(curStage) : I18n.Tr("WIDGET_STAGE_NO_STAGE", "无分级"));
            SetTextIfChanged(_stageTriggerText, trigText);
            _stageTriggerBtn.interactable = !isLocked && curStage >= 0;

            // 单级行尺寸与视口排版 (精简 24px / 展开自适应多行网格仓)
            float rowMargin = 2f;
            float totalItemsHeight = 0f;
            float scrollW = _scrollViewportRt != null && _scrollViewportRt.rect.width > 10f 
                ? _scrollViewportRt.rect.width 
                : (isConcise ? currentW : currentW - 8f * s);
            float rowW = isConcise ? scrollW : scrollW - 2f * s;
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
                if (isConcise)
                {
                    int pCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                    int cols = (pCount > 3) ? 2 : 1;
                    int rows = pCount == 0 ? 0 : Mathf.CeilToInt((float)pCount / (float)cols);
                    float rowChipH = (cols == 2) ? 19f : 22f;
                    float bayH = rows > 0 ? (rows * rowChipH + (rows - 1) * 2f + 4f) : 0f;

                    bool hasDv = stg.DeltaV > 0.01 || stg.BurnTime > 0.01;
                    float stgPropFrac = -1f;
                    string stgPropName = null;
                    if (stg.PartIcons != null && stg.PartIcons.Count > 0)
                    {
                        for (int p = 0; p < stg.PartIcons.Count; p++)
                        {
                            var pi = stg.PartIcons[p];
                            if (pi.PropellantFraction >= 0f)
                            {
                                if (stgPropFrac < 0f || pi.PropellantFraction < stgPropFrac)
                                {
                                    stgPropFrac = pi.PropellantFraction;
                                    stgPropName = pi.PropellantName;
                                }
                            }
                        }
                    }
                    bool hasProp = (pCount > 0 || hasDv) && (stgPropFrac >= 0f || (isAct && snap.PropFrac >= 0f));
                    float leftH = (pCount == 0) ? 20f : (16f + bayH);
                    float rightH = (hasDv ? 16f : 0f) + (hasProp ? 20f : 0f);

                    if (pCount == 0 && !hasDv && !hasProp)
                    {
                        h = 24f;
                    }
                    else
                    {
                        h = Mathf.Max(leftH, rightH);
                        if (hasProp) h = Mathf.Max(h, 40f);
                        else if (pCount == 0) h = 26f;
                    }
                }
                else if (!it.IsExpanded)
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
                totalItemsHeight += (h + (isConcise ? 4f : rowMargin)) * s;
            }

            // 极简模式动态自适应尺寸契约 (竖排原版风格 · 防内容溢出截断 · 底部基准线向上自适应延伸)：
            // 自动伸展物理高度以容纳纵向分级栈，锁定紧凑宽度 (145px)，并以底部基准线为锚点严格向上生长，杜绝向两端扩张漂移
            if (isConcise && displayCount > 0)
            {
                bool sizeChanged = false;

                // 基准线同步守卫 (Bottom-Baseline Anchor Guard):
                // 仅当基准线未初设，或检测到外部位移 (如用户在编辑模式拖拽了组件) 时，校准基准线
                float curPosY = RectTransform.anchoredPosition.y;
                float expectedCenterY = float.IsNaN(_conciseBaselineY.Value) ? curPosY : (_conciseBaselineY.Value + currentH * 0.5f);
                if (float.IsNaN(_conciseBaselineY.Value) || Mathf.Abs(curPosY - expectedCenterY) > 1.0f)
                {
                    _conciseBaselineY.Value = curPosY - currentH * 0.5f;
                }

                float autoH = Mathf.Clamp(totalItemsHeight + 6f * s, 48f * s, 1200f * s);
                if (Mathf.Abs(currentH - autoH) > 1.0f)
                {
                    currentH = autoH;
                    sizeChanged = true;
                }

                if (sizeChanged)
                {
                    // 底部基准线锁定：自适应尺寸变动时，锁定底部边缘在 _conciseBaselineY.Value 不动，
                    // 中心坐标自动向上提升，物理包围盒严格向上增长，彻底杜绝向上下两端扩张漂移
                    float newPosY = _conciseBaselineY.Value + currentH * 0.5f;
                    SetAnchoredPositionIfChanged(RectTransform, new Vector2(RectTransform.anchoredPosition.x, newPosY));
                    if (Config != null)
                    {
                        Config.PositionY = newPosY;
                    }

                    RectTransform.sizeDelta = new Vector2(currentW, currentH);

                    _lastLayoutW.Update(currentW);
                    _lastLayoutH.Update(currentH);
                    ApplyLayout(currentW, currentH);
                    scrollW = _scrollViewportRt != null && _scrollViewportRt.rect.width > 10f 
                        ? _scrollViewportRt.rect.width 
                        : currentW;
                    rowW = scrollW;
                    halfRowW = rowW * 0.5f;
                    availW = rowW - 16f * s;
                }
            }

            float headerH = isConcise ? 0f : 22f * s;
            float footerH = isConcise ? 0f : 24f * s;
            float vpH = Mathf.Max(30f * s, currentH - headerH - footerH);

            if (_scrollContentRt != null)
            {
                float contentH = Mathf.Max(vpH, totalItemsHeight);
                SetSizeDeltaIfChanged(_scrollContentRt, new Vector2(scrollW, contentH));
                if (totalItemsHeight <= vpH + 1f)
                {
                    SetAnchoredPositionIfChanged(_scrollContentRt, Vector2.zero);
                }
            }

            float currentY = 0f;

            // 极简模式检测全局列宽契约 (统一全栈分级框宽，杜绝单双列导致右侧文字锯齿漂移)
            bool hasAnyMultiCol = false;
            if (isConcise)
            {
                for (int k = 0; k < displayCount; k++)
                {
                    if (sortedStages[k].PartIcons != null && sortedStages[k].PartIcons.Count > 3)
                    {
                        hasAnyMultiCol = true;
                        break;
                    }
                }
            }
            float conciseBoxW = (hasAnyMultiCol ? 46f : 36f) * s;
            float conciseBoxX = -halfRowW + (conciseBoxW * 0.5f) + 4f * s;
            float conciseRightX = conciseBoxX + (conciseBoxW * 0.5f) + 6f * s;
            float conciseRightW = rowW - (conciseBoxW + 14f * s);

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
                item.IsBurning = isActive && (snap.Throttle > 0.01f || snap.VerticalSpeed > 1f || stg.BurnTime > 0.01);

                if (!item.HasUserToggled)
                {
                    item.IsExpanded = isActive;
                }

                int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                bool hasIcons = partIconCount > 0;
                bool hasDv = stg.DeltaV > 0.01 || stg.BurnTime > 0.01;
                float stgPropFrac = -1f;
                string stgPropName = null;
                if (stg.PartIcons != null && stg.PartIcons.Count > 0)
                {
                    for (int p = 0; p < stg.PartIcons.Count; p++)
                    {
                        var pi = stg.PartIcons[p];
                        if (pi.PropellantFraction >= 0f)
                        {
                            if (stgPropFrac < 0f || pi.PropellantFraction < stgPropFrac)
                            {
                                stgPropFrac = pi.PropellantFraction;
                                stgPropName = pi.PropellantName;
                            }
                        }
                    }
                }
                bool hasProp = (hasIcons || hasDv) && (stgPropFrac >= 0f || (isActive && snap.PropFrac >= 0f));
                float propFrac = stgPropFrac >= 0f ? stgPropFrac : (isActive && snap.PropFrac >= 0f ? snap.PropFrac : 0f);
                string propLabel = !string.IsNullOrEmpty(stgPropName) ? stgPropName : (!string.IsNullOrEmpty(snap.PropName) ? snap.PropName : I18n.Tr("WIDGET_PROP_PROPELLANT", "推进剂"));

                float itemH;
                float partsBayH = 0f;
                int cols = (partIconCount > 3) ? 2 : 1;
                int rows = partIconCount == 0 ? 0 : Mathf.CeilToInt((float)partIconCount / (float)cols);
                if (isConcise)
                {
                    float rowChipH = (cols == 2) ? 19f : 22f;
                    partsBayH = rows > 0 ? (rows * rowChipH + (rows - 1) * 2f + 4f) : 0f;

                    float leftH = (partIconCount == 0) ? 20f : (16f + partsBayH);
                    float rightH = (hasDv ? 16f : 0f) + (hasProp ? 20f : 0f);

                    if (partIconCount == 0 && !hasDv && !hasProp)
                    {
                        itemH = 24f;
                    }
                    else
                    {
                        itemH = Mathf.Max(leftH, rightH);
                        if (hasProp) itemH = Mathf.Max(itemH, 40f);
                        else if (partIconCount == 0) itemH = 26f;
                    }
                }
                else if (!item.IsExpanded)
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
                        int r = Mathf.CeilToInt((float)partIconCount / chipsPerRow);
                        partsBayH = r * chipSize + (r - 1) * chipGap;
                        itemH = 24f + (partsBayH / s) + 6f;
                    }
                }

                item.TargetPropFrac = propFrac;
                if (item.CurrentPropFrac < 0.001f && propFrac > 0.001f)
                {
                    item.CurrentPropFrac = propFrac;
                }

                SetActiveIfChanged(item.Root, true);
                SetSizeDeltaIfChanged(item.RootRt, new Vector2(rowW, itemH * s));
                SetAnchoredPositionIfChanged(item.RootRt, new Vector2(0f, -currentY - (itemH * 0.5f * s)));
                currentY += (itemH + (isConcise ? 4f : rowMargin)) * s;

                // 顶栏元素垂直锚定 (展开模式固定于单级卡片顶部，精简与简洁模式居中)
                bool isRowExpanded = !isConcise && item.IsExpanded;
                float topElementsY = isRowExpanded ? (itemH * 0.5f * s) - (12f * s) : 0f;

                // 展开折叠切换按钮
                if (item.ToggleExpandBtn != null)
                {
                    SetActiveIfChanged(item.ToggleExpandBtn.gameObject, !isConcise);
                    SetAnchoredPositionIfChanged(item.ToggleExpandBtn.GetComponent<RectTransform>(), new Vector2(-halfRowW + 7f * s, topElementsY));
                }
                if (item.ToggleExpandText != null)
                {
                    SetTextIfChanged(item.ToggleExpandText, item.IsExpanded ? "▼" : "▶");
                }

                if (isConcise)
                {
                    // 竖排原版风格：顶部 /// 级标与芯片深灰底板 (对齐图2原版)
                    float badgeY = (itemH * 0.5f * s) - (8f * s);

                    SetTextIfChanged(item.BadgeText, $"/// {stg.Stage}");
                    SetSizeDeltaIfChanged(item.BadgeBg.rectTransform, new Vector2(conciseBoxW, 16f * s));
                    SetAnchoredPositionIfChanged(item.BadgeBg.rectTransform, new Vector2(conciseBoxX, badgeY));

                    if (isActive)
                    {
                        SetColorIfChanged(item.BadgeBg, theme.WarningColor);
                        SetColorIfChanged(item.BadgeText, style.GetTextColor(TextStyleRole.InverseOnAccent, theme));
                    }
                    else
                    {
                        SetColorIfChanged(item.BadgeBg, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                        SetColorIfChanged(item.BadgeText, theme.WarningColor);
                    }

                    // 行背景底板：作为芯片仓的深灰底板 (对齐图2原版)
                    if (rows > 0)
                    {
                        float bayCenterY = (itemH * 0.5f * s) - (16f * s) - (partsBayH * 0.5f * s);
                        SetSizeDeltaIfChanged(item.RowHighlightBg.rectTransform, new Vector2(conciseBoxW, partsBayH * s));
                        SetAnchoredPositionIfChanged(item.RowHighlightBg.rectTransform, new Vector2(conciseBoxX, bayCenterY));
                        SetColorIfChanged(item.RowHighlightBg, WidgetStyleManager.WithAlpha(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme), 0.75f));
                    }
                    else
                    {
                        SetColorIfChanged(item.RowHighlightBg, Color.clear);
                    }
                }
                else
                {
                    SetSizeDeltaIfChanged(item.RowHighlightBg.rectTransform, new Vector2(rowW, (itemH - 2f) * s));
                    SetColorIfChanged(item.RowHighlightBg, isActive ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) : Color.clear);

                    SetTextIfChanged(item.BadgeText, GetStageBadge(stg.Stage));
                    SetSizeDeltaIfChanged(item.BadgeBg.rectTransform, new Vector2(22f * s, 16f * s));
                    SetAnchoredPositionIfChanged(item.BadgeBg.rectTransform, new Vector2(-halfRowW + 24f * s, topElementsY));

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
                }

                // 推进剂监测光条 (激活级显示在右侧部件高度区间，对齐图2原版)
                if (hasProp)
                {
                    SetActiveIfChanged(item.PropBarRoot, true);
                    if (isConcise)
                    {
                        float propBarW = Mathf.Clamp(conciseRightW, 40f * s, 100f * s);
                        float propBarH = 15f * s;
                        float propBarX = conciseRightX + (propBarW * 0.5f);
                        float propBarY = (itemH * 0.5f * s) - (16f * s) - (propBarH * 0.5f * s) - 2f * s;

                        SetSizeDeltaIfChanged(item.PropBarRootRt, new Vector2(propBarW, propBarH));
                        SetAnchoredPositionIfChanged(item.PropBarRootRt, new Vector2(propBarX, propBarY));
                        SetSizeDeltaIfChanged(item.PropTrack.rectTransform, new Vector2(propBarW, propBarH));
                        SetSizeDeltaIfChanged(item.PropFill.rectTransform, new Vector2(propBarW * propFrac, propBarH));
                        SetAnchoredPositionIfChanged(item.PropFill.rectTransform, new Vector2(-propBarW * 0.5f, 0f));

                        if (item.PropText != null)
                        {
                            item.PropText.gameObject.SetActive(true);
                            item.PropText.rectTransform.SetSizeDeltaSafe(new Vector2(propBarW, propBarH));
                            string activePropLabel = !string.IsNullOrEmpty(propLabel) ? propLabel : (!string.IsNullOrEmpty(snap.PropName) ? snap.PropName : I18n.Tr("WIDGET_PROP_PROPELLANT", "推进剂"));
                            SetTextIfChanged(item.PropText, $"{activePropLabel} {(propFrac * 100f):F0}%");
                        }
                    }
                    else
                    {
                        float barW = rowW;
                        float barH = 3f * s;
                        float barY = -itemH * 0.5f * s + 1.5f * s;
                        SetSizeDeltaIfChanged(item.PropBarRootRt, new Vector2(barW, barH));
                        SetAnchoredPositionIfChanged(item.PropBarRootRt, new Vector2(0f, barY));
                        SetSizeDeltaIfChanged(item.PropTrack.rectTransform, new Vector2(barW, 2f * s));
                        SetSizeDeltaIfChanged(item.PropFill.rectTransform, new Vector2(barW * propFrac, 2.5f * s));
                        SetAnchoredPositionIfChanged(item.PropFill.rectTransform, new Vector2(-barW * 0.5f, 0f));
                        if (item.PropText != null) item.PropText.gameObject.SetActive(false);
                    }
                }
                else
                {
                    SetActiveIfChanged(item.PropBarRoot, false);
                }

                // 分割微线 (极简模式隐藏)
                SetActiveIfChanged(item.Separator.gameObject, !isConcise);
                if (!isConcise)
                {
                    SetSizeDeltaIfChanged(item.Separator.rectTransform, new Vector2(rowW, 1f * s));
                    SetAnchoredPositionIfChanged(item.Separator.rectTransform, new Vector2(0f, -itemH * 0.5f * s));
                }

                // 根据分级形态排布三类状态：
                if (!hasIcons && !hasDv)
                {
                    // === Case A: 空分级 (Empty Stage) ===
                    if (item.EmptySlotText != null)
                    {
                        SetActiveIfChanged(item.EmptySlotText.gameObject, true);
                        SetAnchoredPositionIfChanged(item.EmptySlotText.rectTransform, new Vector2(-halfRowW + (isConcise ? 44f * s : 40f * s), topElementsY));
                        SetTextIfChanged(item.EmptySlotText, I18n.Tr("WIDGET_CTRL_EMPTY_STAGE", "[空级]"));
                    }
                    SetActiveIfChanged(item.StageDvText.gameObject, false);
                    SetActiveIfChanged(item.StageMetaText.gameObject, false);
                    SetActiveIfChanged(item.IconsContainer, false);

                    // 仅在空白分级右侧显示专属删除按键
                    SetActiveIfChanged(item.DeleteStageBtn.gameObject, !isActive && !isConcise);
                    SetAnchoredPositionIfChanged(item.DeleteStageBtn.GetComponent<RectTransform>(), new Vector2(halfRowW - 12f * s, topElementsY));
                }
                else if (!hasDv)
                {
                    // === Case B: 纯动作级 (分离器、降落伞等纯功能级，无 ΔV) ===
                    if (item.EmptySlotText != null) SetActiveIfChanged(item.EmptySlotText.gameObject, false);
                    SetActiveIfChanged(item.DeleteStageBtn.gameObject, false);
                    SetActiveIfChanged(item.StageDvText.gameObject, false);

                    int partHash = ComputePartHash(stg.PartIcons);
                    if (isRowExpanded)
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
                                          item.LastRenderedActive != isActive ||
                                          item.LastRenderedMode != _displayMode.Value;
                        if (chipsDirty)
                        {
                            item.LastPartHash = partHash;
                            item.LastRenderedAvailW = availW;
                            item.LastRenderedExpanded = item.IsExpanded;
                            item.LastRenderedActive = isActive;
                            item.LastRenderedMode = _displayMode.Value;
                            ArrangeIconChipsGrid(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, availW, partsBayH, chipSize, chipGap, chipsPerRow);
                        }
                    }
                    else if (isConcise)
                    {
                        // 竖排原版风格：无 ΔV 纯动作级（如降落伞/分离器），芯片竖排于级标下方
                        SetActiveIfChanged(item.StageMetaText.gameObject, false);
                        SetActiveIfChanged(item.StageDvText.gameObject, false);

                        if (hasIcons)
                        {
                            float bayCenterY = (itemH * 0.5f * s) - (16f * s) - (partsBayH * 0.5f * s);
                            SetActiveIfChanged(item.IconsContainer, true);
                            SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(conciseBoxW, partsBayH * s));
                            SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(conciseBoxX, bayCenterY));

                            bool chipsDirty = partHash != item.LastPartHash ||
                                              Mathf.Abs(item.LastRenderedAvailW - conciseBoxW) > 0.5f ||
                                              item.LastRenderedExpanded != item.IsExpanded ||
                                              item.LastRenderedActive != isActive ||
                                              item.LastRenderedMode != _displayMode.Value;
                            if (chipsDirty)
                            {
                                item.LastPartHash = partHash;
                                item.LastRenderedAvailW = conciseBoxW;
                                item.LastRenderedExpanded = item.IsExpanded;
                                item.LastRenderedActive = isActive;
                                item.LastRenderedMode = _displayMode.Value;
                                ArrangeIconChipsVertical(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, conciseBoxW, partsBayH * s, cols, rows);
                            }
                        }
                        else
                        {
                            SetActiveIfChanged(item.IconsContainer, false);
                        }
                    }
                    else
                    {
                        // 航电收纳模式：单行徽章 + 零件芯片
                        SetActiveIfChanged(item.StageMetaText.gameObject, false);

                        SetActiveIfChanged(item.IconsContainer, true);
                        float startX = 38f * s;
                        float compAvailW = rowW - startX - 4f * s;
                        SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(compAvailW, 18f * s));
                        SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(-halfRowW + startX + compAvailW * 0.5f, 0f));

                        bool chipsDirty = partHash != item.LastPartHash ||
                                          Mathf.Abs(item.LastRenderedAvailW - compAvailW) > 0.5f ||
                                          item.LastRenderedExpanded != item.IsExpanded ||
                                          item.LastRenderedActive != isActive ||
                                          item.LastRenderedMode != _displayMode.Value;
                        if (chipsDirty)
                        {
                            item.LastPartHash = partHash;
                            item.LastRenderedAvailW = compAvailW;
                            item.LastRenderedExpanded = item.IsExpanded;
                            item.LastRenderedActive = isActive;
                            item.LastRenderedMode = _displayMode.Value;
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

                    if (isRowExpanded)
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
                                              item.LastRenderedActive != isActive ||
                                              item.LastRenderedMode != _displayMode.Value;
                            if (chipsDirty)
                            {
                                item.LastPartHash = partHash;
                                item.LastRenderedAvailW = availW;
                                item.LastRenderedExpanded = item.IsExpanded;
                                item.LastRenderedActive = isActive;
                                item.LastRenderedMode = _displayMode.Value;
                                ArrangeIconChipsGrid(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, availW, partsBayH, chipSize, chipGap, chipsPerRow);
                            }
                        }
                        else
                        {
                            SetActiveIfChanged(item.IconsContainer, false);
                        }
                    }
                    else if (isConcise)
                    {
                        // 竖排原版风格：左侧级标与零件芯片纵向堆叠，右侧呈现 ΔV 与推进剂条
                        SetActiveIfChanged(item.StageMetaText.gameObject, false);

                        // 右侧分级 ΔV 数值：对齐顶部 /// 级标
                        if (hasDv)
                        {
                            float badgeY = (itemH * 0.5f * s) - (8f * s);
                            float dvW = conciseRightW;
                            float dvX = conciseRightX + (dvW * 0.5f);
                            SetActiveIfChanged(item.StageDvText.gameObject, true);
                            SetSizeDeltaIfChanged(item.StageDvText.rectTransform, new Vector2(dvW, 14f * s));
                            SetAnchoredPositionIfChanged(item.StageDvText.rectTransform, new Vector2(dvX, badgeY));
                            item.StageDvText.alignment = TextAnchor.MiddleLeft;
                            SetTextIfChanged(item.StageDvText, dvStr);
                            ApplyText(item.StageDvText, isActive ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);
                        }
                        else
                        {
                            SetActiveIfChanged(item.StageDvText.gameObject, false);
                        }

                        if (hasIcons)
                        {
                            float bayCenterY = (itemH * 0.5f * s) - (16f * s) - (partsBayH * 0.5f * s);
                            SetActiveIfChanged(item.IconsContainer, true);
                            SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(conciseBoxW, partsBayH * s));
                            SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(conciseBoxX, bayCenterY));

                            bool chipsDirty = partHash != item.LastPartHash ||
                                              Mathf.Abs(item.LastRenderedAvailW - conciseBoxW) > 0.5f ||
                                              item.LastRenderedExpanded != item.IsExpanded ||
                                              item.LastRenderedActive != isActive ||
                                              item.LastRenderedMode != _displayMode.Value;
                            if (chipsDirty)
                            {
                                item.LastPartHash = partHash;
                                item.LastRenderedAvailW = conciseBoxW;
                                item.LastRenderedExpanded = item.IsExpanded;
                                item.LastRenderedActive = isActive;
                                item.LastRenderedMode = _displayMode.Value;
                                ArrangeIconChipsVertical(item, stg, currentAtlas, isUsingStockAtlas, isActive, theme, style, s, conciseBoxW, partsBayH * s, cols, rows);
                            }
                        }
                        else
                        {
                            SetActiveIfChanged(item.IconsContainer, false);
                        }
                    }
                    else
                    {
                        // 航电收纳模式：单行徽章 + 零件芯片 + 极简 ΔV
                        SetActiveIfChanged(item.StageMetaText.gameObject, false);
                        float dvW = Mathf.Clamp(rowW * 0.38f, 42f * s, 68f * s);

                        SetActiveIfChanged(item.StageDvText.gameObject, true);
                        SetSizeDeltaIfChanged(item.StageDvText.rectTransform, new Vector2(dvW, 14f * s));
                        SetAnchoredPositionIfChanged(item.StageDvText.rectTransform, new Vector2(halfRowW - dvW * 0.5f - 4f * s, 0f));
                        SetTextIfChanged(item.StageDvText, dvStr);
                        ApplyText(item.StageDvText, isActive ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

                        if (hasIcons)
                        {
                            float startX = 38f * s;
                            float compAvailW = rowW - startX - dvW - 4f * s;
                            if (compAvailW >= 16f * s)
                            {
                                SetActiveIfChanged(item.IconsContainer, true);
                                SetSizeDeltaIfChanged(item.IconsContainerRt, new Vector2(compAvailW, 18f * s));
                                SetAnchoredPositionIfChanged(item.IconsContainerRt, new Vector2(-halfRowW + startX + compAvailW * 0.5f, 0f));

                                bool chipsDirty = partHash != item.LastPartHash ||
                                                  Mathf.Abs(item.LastRenderedAvailW - compAvailW) > 0.5f ||
                                                  item.LastRenderedExpanded != item.IsExpanded ||
                                                  item.LastRenderedActive != isActive ||
                                                  item.LastRenderedMode != _displayMode.Value;
                                if (chipsDirty)
                                {
                                    item.LastPartHash = partHash;
                                    item.LastRenderedAvailW = compAvailW;
                                    item.LastRenderedExpanded = item.IsExpanded;
                                    item.LastRenderedActive = isActive;
                                    item.LastRenderedMode = _displayMode.Value;
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
                        string countStr = _displayMode.Value == StagingDisplayMode.Concise 
                            ? $"{partData.Count}" 
                            : $"×{partData.Count}";
                        SetTextIfChanged(chip.MultiplierText, countStr);
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                        Color numCol = _displayMode.Value == StagingDisplayMode.Concise 
                            ? theme.AccentPrimary 
                            : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                        if (chip.MultiplierText.color != numCol) chip.MultiplierText.color = numCol;
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

            // 动态无损紧缩自适应：当芯片总宽超出容器时，平滑压减间距与尺寸，杜绝芯片被隐式丢弃截断
            if (partIconCount > 0 && partIconCount * chipSpacing > containerWidth)
            {
                chipSpacing = containerWidth / partIconCount;
                chipSize = Mathf.Clamp(chipSpacing - 1.5f * s, 11f * s, 16f * s);
            }
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

                    if (partData.Count > 1)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        string countStr = _displayMode.Value == StagingDisplayMode.Concise 
                            ? $"{partData.Count}" 
                            : $"×{partData.Count}";
                        SetTextIfChanged(chip.MultiplierText, countStr);
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                        Color numCol = _displayMode.Value == StagingDisplayMode.Concise 
                            ? theme.AccentPrimary 
                            : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                        if (chip.MultiplierText.color != numCol) chip.MultiplierText.color = numCol;
                    }
                    else if (c == displayedChips - 1 && partIconCount > displayedChips)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        SetTextIfChanged(chip.MultiplierText, $"+{partIconCount - displayedChips}");
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                        Color numCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                        if (chip.MultiplierText.color != numCol) chip.MultiplierText.color = numCol;
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

        private void ArrangeIconChipsVertical(StageItemUI item, StageDeltaVInfo stg, Texture currentAtlas, bool isUsingStockAtlas, bool isActive, ThemeConfig theme, WidgetStyleManager style, float s, float boxW, float bayH, int cols, int rows)
        {
            int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
            float chipSize = (cols == 2) ? 19f * s : 22f * s;
            float chipGap = 2f * s;
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
                    int col = (cols == 2) ? (c % 2) : 0;
                    int row = (cols == 2) ? (c / 2) : c;
                    float chipX = (cols == 2) 
                        ? (-boxW * 0.5f + (chipSize * 0.5f) + 3f * s + col * (chipSize + chipGap))
                        : 0f;
                    float chipY = (bayH * 0.5f) - (chipSize * 0.5f) - 2f * s - row * (chipSize + chipGap);
                    chip.RootRt.SetAnchoredPositionSafe(new Vector2(chipX, chipY));

                    float rawSize = chipSize - 2f * s;
                    chip.IconRawImage.rectTransform.SetSizeDeltaSafe(new Vector2(rawSize, rawSize));
                    if (chip.IconRawImage.texture != currentAtlas) chip.IconRawImage.texture = currentAtlas;

                    Rect uv;
                    int iconIndex = partData.IconTypeIndex > 0 ? partData.IconTypeIndex : StageIconAtlasGenerator.GetIconIndex(partData.IconType);
                    if (isUsingStockAtlas && partData.HasStockUv && partData.StockUvRect.width > 0.01f && partData.StockUvRect.width < 0.5f)
                    {
                        uv = partData.StockUvRect;
                    }
                    else if (isUsingStockAtlas && StockStageIconService.Provider != null)
                    {
                        uv = StockStageIconService.Provider.GetStockIconUv(iconIndex);
                    }
                    else
                    {
                        uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                    }
                    if (chip.IconRawImage.uvRect != uv) chip.IconRawImage.uvRect = uv;
                    Color targetCol = isActive ? theme.AccentPrimary : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    if (chip.IconRawImage.color != targetCol) chip.IconRawImage.color = targetCol;

                    if (partData.Count > 1)
                    {
                        chip.MultiplierText.gameObject.SetActive(true);
                        SetTextIfChanged(chip.MultiplierText, $"{partData.Count}");
                        chip.MultiplierText.rectTransform.SetSizeDeltaSafe(new Vector2(chipSize, 10f * s));
                        Color numCol = theme.AccentPrimary;
                        if (chip.MultiplierText.color != numCol) chip.MultiplierText.color = numCol;
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
                if (_stageTriggerRecoilTimer.Value > 0f)
                {
                    _stageTriggerRecoilTimer.Value -= dt;
                    float recoilProgress = Mathf.Clamp01(_stageTriggerRecoilTimer.Value / 0.22f);
                    float recoilScale = Mathf.Lerp(1.0f, 0.94f, recoilProgress);
                    _stageTriggerBtn.transform.SetLocalScaleSafe(new Vector3(recoilScale, recoilScale, 1f));
                }
                else if (_stageTriggerBtn.transform.localScale.x != 1f)
                {
                    _stageTriggerBtn.transform.SetLocalScaleSafe(Vector3.one);
                }

                if (_stageTriggerFlashTimer.Value > 0f)
                {
                    _stageTriggerFlashTimer.Value -= dt;
                    float flashP = Mathf.Clamp01(_stageTriggerFlashTimer.Value / 0.28f);
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

            // 2. 剪影姿态俯仰角旋转动画与爆炸图插值 (Diagram 模式)
            if (_displayMode.Value == StagingDisplayMode.Diagram)
            {
                StagingSequenceState state = _logic.CurrentState;
                if (state.HasVessel)
                {
                    if (_silhouetteBayTitle != null)
                    {
                        _silhouetteBayTitle.SetTextSafe(state.BayTitleStr);
                    }
                    if (_silhouetteBayFooter != null)
                    {
                        _silhouetteBayFooter.SetTextSafe(state.BayFootStr);
                        _silhouetteBayFooter.SetColor(state.IsFiring 
                            ? style.GetTextColor(TextStyleRole.PrimaryValue, theme) 
                            : style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    }

                    if (_rocketAssemblyRt != null)
                    {
                        float oldTilt = _currentTilt.Value;
                        _currentTilt.Value = Mathf.MoveTowards(_currentTilt.Value, state.TargetTilt, dt * 60f);
                        _rocketAssemblyRt.localRotation = Quaternion.Euler(0f, 0f, -_currentTilt.Value);
                        if (Mathf.Abs(oldTilt - _currentTilt.Value) > 0.05f)
                        {
                            UpdateSilhouetteAssemblyScale(_lastBayW.Value, _lastBayH.Value, s);
                        }

                    }


                    // 发动机点火羽流 (Exhaust Plume)
                    if (_plumeRootObj != null)
                    {
                        _plumeRootObj.SetActiveSafe(state.IsFiring);
                        if (state.IsFiring)
                        {
                            float flutter = 1.0f + Mathf.Sin(time * 26f) * 0.12f;
                            float thr = state.Throttle > 0.01f ? state.Throttle : 0.8f;
                            float plumeH = Mathf.Clamp((8f + 5f * thr) * s * flutter, 6f * s, 14f * s);
                            float plumeW = (7f + 2f * thr) * s * flutter;

                            if (_plumeOuterImg != null)
                            {
                                _plumeOuterImg.rectTransform.SetSizeDeltaSafe(new Vector2(plumeW, plumeH));
                                Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                                _plumeOuterImg.SetColor(WidgetStyleManager.WithAlpha(warnCol, 0.82f + Mathf.Sin(time * 28f) * 0.16f));
                            }
                            if (_plumeCoreImg != null)
                            {
                                _plumeCoreImg.rectTransform.SetSizeDeltaSafe(new Vector2(plumeW * 0.45f, plumeH * 0.55f));
                                _plumeCoreImg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f));
                            }
                        }
                    }

                    // 爆炸图平滑补间动画 (Exploded Factor Tweening)
                    if (_proceduralSilhouetteGraphic != null)
                    {
                        if (Mathf.Abs(_currentExplodedFactor.Value - _targetExplodedFactor.Value) > 0.001f)
                        {
                            _currentExplodedFactor.Value = Mathf.MoveTowards(_currentExplodedFactor.Value, _targetExplodedFactor.Value, dt * 3.5f);
                        }
                        if (Mathf.Abs(_proceduralSilhouetteGraphic.ExplodedFactor - _currentExplodedFactor.Value) > 0.0005f)
                        {
                            _proceduralSilhouetteGraphic.ExplodedFactor = _currentExplodedFactor.Value;
                        }
                    }

                    Texture silTex = VesselSilhouetteService.Provider?.SilhouetteTexture;
                    bool hasBakerTex = silTex != null;
                    bool forceProcedural = _currentExplodedFactor.Value > 0.005f || !hasBakerTex;
                    if (_silhouetteRawImage != null)
                    {
                        _silhouetteRawImage.gameObject.SetActiveSafe(!forceProcedural);
                        if (!forceProcedural && silTex != null && _silhouetteRawImage.texture != silTex)
                        {
                            _silhouetteRawImage.texture = silTex;
                        }
                    }
                    if (_proceduralSilhouetteGraphic != null)
                    {
                        _proceduralSilhouetteGraphic.gameObject.SetActiveSafe(forceProcedural);
                    }
                }
            }

            // 3. 分级行推进剂阻尼与平滑预警
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;

                // A. 激活级背景稳态高光与点火冲击淡出
                if (item.IsActiveStage && _displayMode.Value != StagingDisplayMode.Concise)
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
                        float fillH = item.PropFill.rectTransform.sizeDelta.y;
                        item.PropFill.rectTransform.SetSizeDeltaSafe(new Vector2(barW * item.CurrentPropFrac, fillH > 0.5f ? fillH : 2f * s));
                        if (item.PropText != null && item.PropText.gameObject.activeSelf)
                        {
                            string propLabel = !string.IsNullOrEmpty(_logic.CurrentState.PropName) ? _logic.CurrentState.PropName : I18n.Tr("WIDGET_PROP_PROPELLANT", "推进剂");
                            SetTextIfChanged(item.PropText, $"{propLabel} {(item.CurrentPropFrac * 100f):F0}%");
                        }
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

            // D. 简洁模式悬停微键动态显示同步
            UpdateHoverVisibility();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered.Value = true;
            UpdateHoverVisibility();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(RectTransform, eventData.position, eventData.enterEventCamera))
            {
                return;
            }
            _isHovered.Value = false;
            UpdateHoverVisibility();
        }

        internal void NotifyWidgetPointerEnter()
        {
            _isHovered.Value = true;
            UpdateHoverVisibility();
        }

        internal void NotifyWidgetPointerExit(PointerEventData eventData)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(RectTransform, eventData.position, eventData.enterEventCamera))
            {
                return;
            }
            _isHovered.Value = false;
            UpdateHoverVisibility();
        }

        private void UpdateHoverVisibility()
        {
            if (_displayMode.Value != StagingDisplayMode.Concise) return;
            bool showFloating = _isHovered.Value || WidgetDragHandler.IsEditModeActive;
            if (_modeToggleBtn != null && (Config == null || !Config.IsSubElementDisabled("staging_mode_btn")))
            {
                SetActiveIfChanged(_modeToggleBtn.gameObject, showFloating);
            }
            if (_addStageTopBtn != null && (Config == null || !Config.IsSubElementDisabled("staging_add_btn")))
            {
                SetActiveIfChanged(_addStageTopBtn.gameObject, showFloating);
            }
        }

        #region 交互响应回调 (Interactive Callbacks)

        private void OnAddStageTopClicked()
        {
            StockStageActionService.InsertStage(_highestStageNumber + 1);
        }

        private void OnToggleAllExpanded()
        {
            _allExpanded.Value = !_allExpanded.Value;
            if (_toggleAllText != null)
            {
                _toggleAllText.text = _allExpanded.Value ? "▼" : "▶";
            }
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                item.IsExpanded = _allExpanded.Value;
                item.HasUserToggled = true;
                if (item.ToggleExpandText != null)
                {
                    item.ToggleExpandText.text = _allExpanded.Value ? "▼" : "▶";
                }
            }
        }

        public void ToggleDisplayMode()
        {
            if (_displayMode.Value == StagingDisplayMode.Concise)
            {
                SetDisplayMode(StagingDisplayMode.Standard);
            }
            else if (_displayMode.Value == StagingDisplayMode.Standard)
            {
                SetDisplayMode(StagingDisplayMode.Silhouette2D);
            }
            else
            {
                SetDisplayMode(StagingDisplayMode.Concise);
            }
        }

        public void SetDisplayMode(StagingDisplayMode mode)
        {
            if (_displayMode.Value == mode) return;

            float s = CurrentDpiScale > 0.01f ? CurrentDpiScale : 1f;

            // 1. 切换模式
            _displayMode.Value = mode;
            _conciseBaselineY.Value = float.NaN;

            // 2. 提取目标模态物理基准尺寸
            Vector2 targetBaseSize = BaseSize;
            float effX = Config != null ? Config.EffectiveScaleX : 1f;
            float effY = Config != null ? Config.EffectiveScaleY : 1f;
            float factorX = CommittedScale > 0.001f ? (effX / CommittedScale) : 1.0f;
            float factorY = CommittedScale > 0.001f ? (effY / CommittedScale) : 1.0f;

            // 3. 应用新尺寸
            Vector2 newPixelSize = new Vector2(targetBaseSize.x * s * factorX, targetBaseSize.y * s * factorY);
            RectTransform.sizeDelta = newPixelSize;

            // 4. 持久化模态配置
            UpdateCustomTemplateMode();

            // 5. 执行全量 UI 刷新与自适应排版
            UpdateModeUI();
        }

        private void UpdateCustomTemplateMode()
        {
            if (Config == null) return;
            string modeVal = ((int)_displayMode.Value).ToString();
            var dict = ParseTemplateChannels(Config.CustomTemplate);
            dict["MODE"] = modeVal;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            Config.CustomTemplate = sb.ToString();
            WidgetLayoutManager.Instance?.SaveLayout();
        }

        private static Dictionary<string, string> ParseTemplateChannels(string template)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return dict;
            string[] pairs = template.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i].Trim();
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string v = p.Substring(eq + 1).Trim();
                    dict[k] = v;
                }
                else if (p.Length > 0)
                {
                    dict[p] = "1";
                }
            }
            return dict;
        }

        private void UpdateModeUI()
        {
            float s = CurrentDpiScale > 0.01f ? CurrentDpiScale : 1f;
            if (_modeToggleText != null)
            {
                switch (_displayMode.Value)
                {
                    case StagingDisplayMode.Concise:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_CONCISE", "简");
                        break;
                    case StagingDisplayMode.Silhouette2D:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_SILHOUETTE2D", "2D");
                        break;
                    default:
                        _modeToggleText.text = I18n.Tr("WIDGET_STAGING_BTN_STANDARD", "标");
                        break;
                }
            }

            bool isStandard = _displayMode.Value == StagingDisplayMode.Standard;
            bool is2D = _displayMode.Value == StagingDisplayMode.Silhouette2D;

            if (_toggleAllBtn != null)
            {
                _toggleAllBtn.gameObject.SetActive(isStandard && (Config == null || !Config.IsSubElementDisabled("staging_toggle_all")));
            }
            if (_silhouetteBayObj != null)
            {
                _silhouetteBayObj.SetActive(is2D && (Config == null || !Config.IsSubElementDisabled("staging_2d_bay")));
            }

            for (int i = 0; i < _stageItems.Count; i++)
            {
                _stageItems[i].LastPartHash = -1;
                _stageItems[i].LastRenderedAvailW = -1f;
                _stageItems[i].LastRenderedMode = (StagingDisplayMode)(-1);
            }
            _lastLayoutW.Update(-1f);
            _lastLayoutH.Update(-1f);

            ApplyTheme(_cachedTheme ?? WidgetTheme);

            float curW = RectTransform.rect.width > 10f ? RectTransform.rect.width : BaseSize.x * s;
            float curH = RectTransform.rect.height > 10f ? RectTransform.rect.height : BaseSize.y * s;
            ApplyLayout(curW, curH);

            OnRenderState();

            string tip;
            switch (_displayMode.Value)
            {
                case StagingDisplayMode.Concise:
                    tip = I18n.Tr("TIP_STAGING_MODE_CONCISE", "已切换至原版风格极简模式");
                    break;
                case StagingDisplayMode.Silhouette2D:
                    tip = I18n.Tr("TIP_STAGING_MODE_SILHOUETTE2D", "已切换至2D剪影实时高级模式 (火箭爆炸图)");
                    break;
                default:
                    tip = I18n.Tr("TIP_STAGING_MODE_STANDARD", "已切换至标准模式");
                    break;
            }
            MFPToastBridge.Show(tip);
        }

        public override void PopulateContextMenu(Action<string, Action> registerAction)
        {
            base.PopulateContextMenu(registerAction);
            registerAction?.Invoke(I18n.Tr("CTX_STAGING_MODE_CONCISE", "⚡ 切换原版风格极简模式"), () => SetDisplayMode(StagingDisplayMode.Concise));
            registerAction?.Invoke(I18n.Tr("CTX_STAGING_MODE_STANDARD", "⚡ 切换标准模式"), () => SetDisplayMode(StagingDisplayMode.Standard));
            registerAction?.Invoke(I18n.Tr("CTX_STAGING_MODE_SILHOUETTE2D", "⚡ 切换2D剪影实时高级模式 (火箭爆炸图)"), () => SetDisplayMode(StagingDisplayMode.Silhouette2D));
            if (_displayMode.Value == StagingDisplayMode.Silhouette2D)
            {
                registerAction?.Invoke(_targetExplodedFactor.Value > 0.5f 
                    ? I18n.Tr("WIDGET_STAGING_ASSEMBLE_BTN", "🚀 组装") 
                    : I18n.Tr("WIDGET_STAGING_EXPLODED_BTN", "💥 爆炸图"), ToggleExplodedView);
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
            _stageTriggerFlashTimer.Value = 0.16f;
            StockStageActionService.ToggleStagingLock();
        }

        private void OnStageTriggerClicked()
        {
            _stageTriggerRecoilTimer.Value = 0.22f;
            _stageTriggerFlashTimer.Value = 0.28f;
            StockStageActionService.ActivateNextStage();
        }

        private enum ChipDropAction
        {
            None,
            MoveToStage,
            InsertStage,
            ReorderInStage
        }

        private readonly Cached<ChipDropAction> _chipDropAction = new Cached<ChipDropAction>(ChipDropAction.None);
        private readonly Cached<int> _pendingTargetStage = new Cached<int>(-1);
        private readonly Cached<int> _pendingTargetChipIndex = new Cached<int>(-1);

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

                string dragHint = (chip.PartData.Count > 1 || chip.PartData.IsExpanded)
                    ? I18n.Tr("STG_DRAG_SYMMETRY_HINT", "[点击展开/折叠对称 · 拖拽跨级排布]")
                    : I18n.Tr("STG_DRAG_MOVE_HINT", "[拖拽跨级移动]");

                string sub = chip.PartData.PropellantFraction >= 0f 
                    ? $"{chip.PartData.PropellantName?.ToUpperInvariant()} {(chip.PartData.PropellantFraction * 100f):F0}% · {dragHint}" 
                    : I18n.TrFormat("WIDGET_STAGE_DRAG_HINT_FMT", chip.StageNumber, dragHint);
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

        internal void OnChipClicked(StageIconChipUI chip, PointerEventData eventData)
        {
            if (chip == null) return;
            if (chip.PartData.Count > 1 || chip.PartData.IsExpanded)
            {
                StockStageActionService.ToggleSymmetryExpansion(chip.PartFlightId, chip.StageNumber, chip.PartIndex);
            }
        }

        private void UpdateDragGhostPosition(PointerEventData eventData)
        {
            if (_dragGhostRt == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPt))
            {
                _dragGhostRt.anchoredPosition = localPt;
            }
            else
            {
                _dragGhostRt.position = eventData.position;
            }
        }

        internal void OnChipBeginDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (chip == null) return;
            if (_tooltipRoot != null) _tooltipRoot.SetActive(false);

            _chipDropAction.Value = ChipDropAction.None;
            _pendingTargetStage.Value = -1;
            _pendingTargetChipIndex.Value = -1;

            if (_dragGhostRoot != null)
            {
                _dragGhostRoot.SetActive(true);
                _dragGhostRoot.transform.SetAsLastSibling();
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
                UpdateDragGhostPosition(eventData);
            }
        }

        internal void OnChipDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null && _dragGhostRoot.activeSelf)
            {
                UpdateDragGhostPosition(eventData);
            }

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;

            _chipDropAction.Value = ChipDropAction.None;
            _pendingTargetStage.Value = -1;
            _pendingTargetChipIndex.Value = -1;

            StageItemUI hoveredItem = null;
            int hoveredDisplayIndex = -1;

            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(item.RootRt, eventData.position, eventData.pressEventCamera))
                {
                    hoveredItem = item;
                    hoveredDisplayIndex = i;
                    break;
                }
            }

            // 重置所有行的高亮色与微芯片边框
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                item.RowHighlightBg.color = item.IsActiveStage 
                    ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) 
                    : Color.clear;

                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    item.IconChips[c].ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }

            bool showDropLine = false;

            if (hoveredItem != null)
            {
                // 1. 悬停在另一分级行：明确判定为跨级移动 (Move Part To Stage)
                // 整行卡片均为该级的安全受体，杜绝边缘误触触发插入新级或打乱分级时序
                if (hoveredItem.StageNumber != chip.StageNumber)
                {
                    // === 拖拽至另一分级行中部：跨级移动 (Move Part To Stage) ===
                    _chipDropAction.Value = ChipDropAction.MoveToStage;
                    _pendingTargetStage.Value = hoveredItem.StageNumber;
                    hoveredItem.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.22f);
                }
                else
                {
                    // 2. 悬停在本级行内部：仅当明确悬停于其他部件芯片上方时触发本级调序 (Reorder In Stage)
                    StageIconChipUI targetChip = null;
                    for (int c = 0; c < hoveredItem.IconChips.Count; c++)
                    {
                        var otherChip = hoveredItem.IconChips[c];
                        if (otherChip.Root.activeSelf && otherChip != chip)
                        {
                            if (RectTransformUtility.RectangleContainsScreenPoint(otherChip.RootRt, eventData.position, eventData.pressEventCamera))
                            {
                                targetChip = otherChip;
                                break;
                            }
                        }
                    }

                    if (targetChip != null)
                    {
                        _chipDropAction.Value = ChipDropAction.ReorderInStage;
                        _pendingTargetChipIndex.Value = targetChip.PartIndex;
                        targetChip.ChipOutline.effectColor = theme.AccentPrimary;
                    }
                    else
                    {
                        _chipDropAction.Value = ChipDropAction.None;
                        _pendingTargetChipIndex.Value = -1;
                    }
                }
            }
            else if (_scrollContentRt != null && RectTransformUtility.RectangleContainsScreenPoint(RectTransform, eventData.position, eventData.pressEventCamera))
            {
                // 3. 游标未命中任何具体分级卡片，而是处于分级之间的物理缝隙/边界处：
                // 仅在明确落入分级间隙时才触发新建分级指示微线 (Drop In Gap Between Stages)
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_scrollContentRt, eventData.position, eventData.pressEventCamera, out Vector2 contentLocalPt);

                int activeCount = 0;
                for (int i = 0; i < _stageItems.Count; i++)
                {
                    if (_stageItems[i].Root.activeSelf) activeCount++;
                    else break;
                }

                if (activeCount > 0)
                {
                    bool isReverse = _logic.StageOrder == "REVERSE";
                    float rowW = RectTransform.rect.width > 10f ? RectTransform.rect.width - 10f * s : (DefaultWidth - 10f) * s;

                    StageItemUI firstItem = _stageItems[0];
                    float firstTopY = firstItem.RootRt.anchoredPosition.y + (firstItem.RootRt.rect.height * 0.5f);

                    StageItemUI lastItem = _stageItems[activeCount - 1];
                    float lastBottomY = lastItem.RootRt.anchoredPosition.y - (lastItem.RootRt.rect.height * 0.5f);

                    // A) 顶部上方缝隙 (Above first stage item)
                    if (contentLocalPt.y >= firstTopY && contentLocalPt.y <= firstTopY + 14f * s)
                    {
                        showDropLine = true;
                        _chipDropAction.Value = ChipDropAction.InsertStage;
                        _pendingTargetStage.Value = isReverse ? (_highestStageNumber + 1) : 0;
                        if (_stageDropIndicatorRt != null)
                        {
                            _stageDropIndicatorRt.sizeDelta = new Vector2(rowW, 2.5f * s);
                            _stageDropIndicatorRt.anchoredPosition = new Vector2(0f, firstTopY);
                        }
                    }
                    // B) 底部下方缝隙 (Below last stage item)
                    else if (contentLocalPt.y <= lastBottomY && contentLocalPt.y >= lastBottomY - 14f * s)
                    {
                        showDropLine = true;
                        _chipDropAction.Value = ChipDropAction.InsertStage;
                        _pendingTargetStage.Value = isReverse ? 0 : (_highestStageNumber + 1);
                        if (_stageDropIndicatorRt != null)
                        {
                            _stageDropIndicatorRt.sizeDelta = new Vector2(rowW, 2.5f * s);
                            _stageDropIndicatorRt.anchoredPosition = new Vector2(0f, lastBottomY);
                        }
                    }
                    // C) 相邻两级之间的缝隙 (Between two stage items)
                    else
                    {
                        for (int i = 0; i < activeCount - 1; i++)
                        {
                            StageItemUI upperItem = _stageItems[i];
                            StageItemUI lowerItem = _stageItems[i + 1];

                            float upperBottomY = upperItem.RootRt.anchoredPosition.y - (upperItem.RootRt.rect.height * 0.5f);
                            float lowerTopY = lowerItem.RootRt.anchoredPosition.y + (lowerItem.RootRt.rect.height * 0.5f);

                            if (contentLocalPt.y <= upperBottomY && contentLocalPt.y >= lowerTopY)
                            {
                                showDropLine = true;
                                _chipDropAction.Value = ChipDropAction.InsertStage;
                                int targetStage = isReverse ? upperItem.StageNumber : lowerItem.StageNumber;
                                _pendingTargetStage.Value = Mathf.Max(0, targetStage);

                                if (_stageDropIndicatorRt != null)
                                {
                                    float gapCenterY = (upperBottomY + lowerTopY) * 0.5f;
                                    _stageDropIndicatorRt.sizeDelta = new Vector2(rowW, 2.5f * s);
                                    _stageDropIndicatorRt.anchoredPosition = new Vector2(0f, gapCenterY);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            if (_stageDropIndicatorGo != null)
            {
                _stageDropIndicatorGo.SetActive(showDropLine);
                if (showDropLine)
                {
                    _stageDropIndicatorGo.transform.SetAsLastSibling();
                }
            }
        }

        internal void OnChipEndDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null) _dragGhostRoot.SetActive(false);
            if (_stageDropIndicatorGo != null) _stageDropIndicatorGo.SetActive(false);

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                item.RowHighlightBg.color = item.IsActiveStage 
                    ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) 
                    : Color.clear;

                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    item.IconChips[c].ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }

            if (chip != null)
            {
                switch (_chipDropAction.Value)
                {
                    case ChipDropAction.InsertStage:
                        if (_pendingTargetStage.Value >= 0)
                        {
                            StockStageActionService.InsertStageAndMovePart(chip.PartFlightId, chip.StageNumber, chip.PartIndex, _pendingTargetStage.Value);
                        }
                        break;

                    case ChipDropAction.MoveToStage:
                        if (_pendingTargetStage.Value >= 0 && _pendingTargetStage.Value != chip.StageNumber)
                        {
                            StockStageActionService.MovePartToStage(chip.PartFlightId, chip.StageNumber, chip.PartIndex, _pendingTargetStage.Value);
                        }
                        break;

                    case ChipDropAction.ReorderInStage:
                        if (_pendingTargetChipIndex.Value >= 0 && _pendingTargetChipIndex.Value != chip.PartIndex)
                        {
                            StockStageActionService.ReorderPartInStage(chip.PartFlightId, chip.StageNumber, chip.PartIndex, _pendingTargetChipIndex.Value);
                        }
                        break;
                }
            }

            _chipDropAction.Value = ChipDropAction.None;
            _pendingTargetStage.Value = -1;
            _pendingTargetChipIndex.Value = -1;

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
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
            _stageItems.Clear();
            base.OnDestroy();
        }
    }

    #region 专用交互中继器 (Dedicated Event Handlers)

    public class StagePartDragHandler : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageIconChipUI Chip;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!eventData.dragging)
            {
                OwnerWidget?.OnChipClicked(Chip, eventData);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OwnerWidget?.NotifyWidgetPointerEnter();
            OwnerWidget?.OnChipPointerEnter(Chip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OwnerWidget?.NotifyWidgetPointerExit(eventData);
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

    public class StageBadgeDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageItemUI StageItem;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount >= 2)
            {
                OwnerWidget?.ToggleDisplayMode();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OwnerWidget?.NotifyWidgetPointerEnter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OwnerWidget?.NotifyWidgetPointerExit(eventData);
        }

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

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 程序化 2D 火箭矢量剪影与爆炸图解构图元 (MFP 0-Texture 矢量图元引擎)
    /// 支持完整组装状态 (ExplodedFactor = 0) 与高保真爆炸图分离解构状态 (ExplodedFactor > 0)。
    /// </summary>
    public class ProceduralRocketSilhouetteGraphic : MaskableGraphic
    {
        [SerializeField]
        private float _explodedFactor = 0f;
        [SerializeField]
        private float _rocketAspect = 6.0f;

        public float ExplodedFactor
        {
            get => _explodedFactor;
            set
            {
                if (Mathf.Abs(_explodedFactor - value) > 0.0005f)
                {
                    _explodedFactor = Mathf.Clamp01(value);
                    SetVerticesDirty();
                }
            }
        }

        public float RocketAspect
        {
            get => _rocketAspect;
            set
            {
                float clamped = Mathf.Clamp(value, 2.0f, 20f);
                if (Mathf.Abs(_rocketAspect - clamped) > 0.05f)
                {
                    _rocketAspect = clamped;
                    SetVerticesDirty();
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float w = r.width;
            float h = r.height;
            float cx = r.center.x;
            float cy = r.center.y;

            Color c = color;
            Color lineCol = WidgetStyleManager.Weighted(c, LineWeight.Strong);
            Color sepCol = WidgetStyleManager.Weighted(c, LineWeight.Ghost);

            float exp = _explodedFactor;

            // 1. 头锥 / 整流罩 (Apex & Fairing)
            // 爆炸图下向上位移
            float fairingShiftY = exp * h * 0.16f;
            float rocketSpanH = h * 0.82f;
            float coreHalfW = Mathf.Clamp((rocketSpanH / Mathf.Clamp(_rocketAspect, 2.0f, 20f)) * 0.5f, w * 0.04f, w * 0.22f);
            float yTop = cy + h * 0.44f + fairingShiftY;
            float yFairingBase = cy + h * 0.28f + fairingShiftY;
            AddTri(vh, cx, yTop, cx - coreHalfW, yFairingBase, cx + coreHalfW, yFairingBase, c);


            // 整流罩与二级分离虚线
            if (exp > 0.01f)
            {
                float sepY1 = cy + h * 0.28f + fairingShiftY * 0.4f;
                AddDashedLine(vh, cx - coreHalfW * 1.2f, cx + coreHalfW * 1.2f, sepY1, sepCol, 1.2f);
            }

            // 2. 二级 / 上部级间段 (Upper Stage / Interstage)
            float upperShiftY = exp * h * 0.07f;
            float yUpperTop = cy + h * 0.28f + upperShiftY;
            float yStage1 = cy + h * 0.12f + upperShiftY;
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yStage1, yUpperTop, c, c);
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yStage1 - 0.75f, yStage1 + 0.75f, lineCol, lineCol);

            // 二级与芯级分离虚线
            if (exp > 0.01f)
            {
                float sepY2 = cy + h * 0.12f + (upperShiftY - exp * h * 0.02f) * 0.5f;
                AddDashedLine(vh, cx - coreHalfW * 1.2f, cx + coreHalfW * 1.2f, sepY2, sepCol, 1.2f);
            }

            // 3. 主芯级筒段 (Core Stage)
            float coreShiftY = -exp * h * 0.02f;
            float yCoreTop = cy + h * 0.12f + coreShiftY;
            float yEngineBase = cy - h * 0.28f + coreShiftY;
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yEngineBase, yCoreTop, c, c);

            float yStage2 = cy - h * 0.08f + coreShiftY;
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yStage2 - 0.75f, yStage2 + 0.75f, lineCol, lineCol);

            // 4. 底部主发动机喷管 (Nozzle)
            // 爆炸图下向下脱出分离
            float nozzleShiftY = coreShiftY - exp * h * 0.09f;
            float yNozzleTop = yEngineBase - exp * h * 0.04f;
            float yNozzleBase = cy - h * 0.38f + nozzleShiftY;
            float nozzleTopHalfW = coreHalfW * 0.65f;
            float nozzleBtmHalfW = coreHalfW * 0.90f;
            AddTrapezoid(vh, cx, yNozzleTop, nozzleTopHalfW, yNozzleBase, nozzleBtmHalfW, c);

            if (exp > 0.01f)
            {
                float sepY3 = (yEngineBase + yNozzleTop) * 0.5f;
                AddDashedLine(vh, cx - coreHalfW * 0.9f, cx + coreHalfW * 0.9f, sepY3, sepCol, 1.0f);
            }

            // 5. 两侧捆绑助推器 (Side Boosters)
            // 爆炸图下向外平移并略向下沉，呈现经典的科罗廖夫十字 (Korolev Cross) 分离解构姿势！
            float boosterHalfW = coreHalfW * 0.55f;
            float boosterSpreadX = exp * w * 0.22f;
            float boosterShiftY = -exp * h * 0.05f;
            float boosterDist = coreHalfW * 1.65f + boosterSpreadX;

            float bTop = cy + h * 0.15f + boosterShiftY;
            float bNose = cy + h * 0.22f + boosterShiftY;
            float bBtm = cy - h * 0.24f + boosterShiftY;
            float bNozzle = cy - h * 0.32f + boosterShiftY;

            // 左助推器
            float bxL = cx - boosterDist;
            AddTri(vh, bxL, bNose, bxL - boosterHalfW, bTop, bxL + boosterHalfW, bTop, c);
            AddQuad(vh, bxL - boosterHalfW, bxL + boosterHalfW, bBtm, bTop, c, c);
            AddTrapezoid(vh, bxL, bBtm, boosterHalfW * 0.8f, bNozzle, boosterHalfW * 1.1f, c);

            // 右助推器
            float bxR = cx + boosterDist;
            AddTri(vh, bxR, bNose, bxR - boosterHalfW, bTop, bxR + boosterHalfW, bTop, c);
            AddQuad(vh, bxR - boosterHalfW, bxR + boosterHalfW, bBtm, bTop, c, c);
            AddTrapezoid(vh, bxR, bBtm, boosterHalfW * 0.8f, bNozzle, boosterHalfW * 1.1f, c);

            // 两侧分离导引线与向外分离标记
            if (exp > 0.01f)
            {
                float sepTickY = cy - h * 0.05f;
                AddDashedLine(vh, bxL + boosterHalfW + 1f, cx - coreHalfW - 1f, sepTickY, sepCol, 1.0f);
                AddDashedLine(vh, cx + coreHalfW + 1f, bxR - boosterHalfW - 1f, sepTickY, sepCol, 1.0f);
            }

            // 6. 气动稳定翼 (Fins)
            float finSpan = coreHalfW * 2.2f;
            float finTop = cy - h * 0.20f + coreShiftY;
            float finBtm = cy - h * 0.28f + coreShiftY;
            AddTri(vh, cx - coreHalfW, finTop, cx - finSpan, finBtm, cx - coreHalfW, finBtm, c);
            AddTri(vh, cx + coreHalfW, finTop, cx + finSpan, finBtm, cx + coreHalfW, finBtm, c);
        }

        private static void AddTri(VertexHelper vh, float x0, float y0, float x1, float y1, float x2, float y2, Color c)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x2, y2), c, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
        }

        private static void AddQuad(VertexHelper vh, float xMin, float xMax, float yMin, float yMax, Color c0, Color c1)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(xMin, yMin), c0, Vector2.zero);
            vh.AddVert(new Vector3(xMin, yMax), c1, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMax), c1, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMin), c0, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private static void AddTrapezoid(VertexHelper vh, float cx, float yTop, float halfWTop, float yBtm, float halfWBtm, Color c)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(cx - halfWTop, yTop), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfWTop, yTop), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfWBtm, yBtm), c, Vector2.zero);
            vh.AddVert(new Vector3(cx - halfWBtm, yBtm), c, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private static void AddDashedLine(VertexHelper vh, float x0, float x1, float y, Color c, float thickness)
        {
            float dx = x1 - x0;
            if (Mathf.Abs(dx) < 2f) return;
            float dashLen = 2.5f;
            float gapLen = 2f;
            float total = Mathf.Abs(dx);
            float dir = Mathf.Sign(dx);
            float curr = 0f;
            float halfT = thickness * 0.5f;
            while (curr < total)
            {
                float segLen = Mathf.Min(dashLen, total - curr);
                float segX0 = x0 + dir * curr;
                float segX1 = segX0 + dir * segLen;
                float xMin = Mathf.Min(segX0, segX1);
                float xMax = Mathf.Max(segX0, segX1);
                AddQuad(vh, xMin, xMax, y - halfT, y + halfT, c, c);
                curr += dashLen + gapLen;
            }
        }
    }
}
