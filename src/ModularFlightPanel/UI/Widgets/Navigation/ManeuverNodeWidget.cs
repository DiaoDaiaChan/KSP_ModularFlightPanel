using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 无机动节点时的待命展示模态
    /// </summary>
    public enum ManeuverIdleMode
    {
        MinimalPill = 0,    // 极简胶囊待机 (24px 高度超薄航电徽标条，默认推荐)
        AutoHide = 1        // 自动完全隐形 (无节点且非编辑模式时彻底隐身)
    }

    /// <summary>
    /// 机动节点解算纯状态快照 (0 GC 纯值结构体)
    /// </summary>
    public struct ManeuverNodeState
    {
        public bool HasNode;
        public double DeltaV;
        public double TotalDeltaV;
        public float MeterFraction;
        public double TimeToNode;
        public double BurnTime;
        public double TimeToBurn;
        public string FormattedDeltaV;
        public string FormattedTotalDeltaV;
        public string UnitLabel;
        public string FormattedTNode;
        public string FormattedBurnTime;
        public string FormattedTimeToBurn;
        public string FormattedBurnIn;
        public string BadgeText;
        public CardStyleRole TargetCardRole;
        public bool IsBurning;
        public bool IsUrgent;
        public bool IsPreIgnition;
        public bool IsBurnComplete;
        public string FormattedPercent;
        public string VectorTag;
    }

    /// <summary>
    /// 机动节点解算纯逻辑大脑 (Headless Pure C# Logic Engine)
    /// 完全脱离 UnityEngine，支持无头仿真、离线单元测试与零 GC 解算。
    /// </summary>
    public class ManeuverNodeLogic : WidgetLogic<ManeuverNodeState>
    {
        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel || !telemetry.HasManeuverNode)
            {
                Reset();
                return;
            }

            double dv = telemetry.ManeuverDeltaV;
            double totalDv = telemetry.ManeuverTotalDeltaV;
            if (totalDv < dv) totalDv = dv;
            double timeToNode = telemetry.ManeuverTimeToNode;
            double burnTime = telemetry.ManeuverBurnTime;
            double timeToBurn = telemetry.ManeuverTimeToBurn;

            float fraction = 1.0f;
            if (totalDv > 0.1)
            {
                double ratio = dv / totalDv;
                fraction = (float)Math.Max(0.0, Math.Min(1.0, ratio));
            }

            bool isBurning = timeToBurn <= 0.0 && dv > 0.1;
            bool isBurnComplete = dv <= 0.1;
            bool isUrgent = timeToBurn > 0.0 && timeToBurn <= 15.0;
            bool isPreIgnition = timeToBurn > 0.0 && timeToBurn <= 60.0;
            string percentStr = AvionicsFastFormat.FastPercent(fraction);

            double proDv = telemetry.ManeuverDeltaVPrograde;
            double normDv = telemetry.ManeuverDeltaVNormal;
            double radDv = telemetry.ManeuverDeltaVRadial;
            string vectorTag;
            if (double.IsNaN(proDv) || (Math.Abs(proDv) < 0.1 && Math.Abs(normDv) < 0.1 && Math.Abs(radDv) < 0.1))
            {
                vectorTag = percentStr;
            }
            else if (Math.Abs(proDv) >= Math.Abs(normDv) && Math.Abs(proDv) >= Math.Abs(radDv))
            {
                vectorTag = (proDv >= 0 ? "PRO " : "RET ") + percentStr;
            }
            else if (Math.Abs(normDv) >= Math.Abs(radDv))
            {
                vectorTag = (normDv >= 0 ? "NORM " : "ANT ") + percentStr;
            }
            else
            {
                vectorTag = (radDv >= 0 ? "RAD " : "A-RAD ") + percentStr;
            }

            // 统一航电量纲制式换算 (公制/英制/航海制自动自适应)
            double convertedDv = AvionicsUnitSystem.Convert(
                dv,
                UnitDimension.Velocity,
                AvionicsUnitSystem.GlobalMode,
                out string unitSymbol);

            string formattedDv = convertedDv.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            double convertedTotalDv = AvionicsUnitSystem.Convert(
                totalDv,
                UnitDimension.Velocity,
                AvionicsUnitSystem.GlobalMode,
                out _);
            string formattedTotalDv = convertedTotalDv.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            CardStyleRole targetRole = CardStyleRole.Normal;
            string badgeText = I18n.Tr("WIDGET_ALERT_ARMED", "待命");

            if (isBurning)
            {
                targetRole = CardStyleRole.Emphasized;
                badgeText = I18n.Tr("WIDGET_NAV_BURNING", "燃烧中");
            }
            else if (isBurnComplete)
            {
                targetRole = CardStyleRole.Normal;
                badgeText = I18n.Tr("WIDGET_NAV_BURN_COMPLETE", "已完成");
            }
            else if (isUrgent)
            {
                targetRole = CardStyleRole.Emphasized;
                badgeText = I18n.Tr("WIDGET_NAV_BURN_IN", "点火准备");
            }
            else if (isPreIgnition)
            {
                targetRole = CardStyleRole.Warning;
                badgeText = I18n.Tr("WIDGET_NAV_COUNTDOWN", "倒计时");
            }

            string prefix = timeToNode < 0 ? "T+ " : "T- ";
            string formattedTNode = prefix + BaseFlightWidget.FormatDuration(Math.Abs(timeToNode));
            string formattedBurnTime = BaseFlightWidget.FormatDuration(Math.Max(0.0, burnTime));
            string formattedTimeToBurn = timeToBurn <= 0.0
                ? (I18n.Tr("WIDGET_NAV_BURNING", "燃烧中") + "!")
                : BaseFlightWidget.FormatDuration(timeToBurn);
            string burnIn = timeToBurn <= 0.0
                ? (I18n.Tr("WIDGET_NAV_BURNING", "燃烧中") + "!")
                : (I18n.Tr("WIDGET_NAV_BURN_IN", "点火") + " " + BaseFlightWidget.FormatDuration(timeToBurn));

            CurrentState = new ManeuverNodeState
            {
                HasNode = true,
                DeltaV = dv,
                TotalDeltaV = totalDv,
                MeterFraction = fraction,
                TimeToNode = timeToNode,
                BurnTime = burnTime,
                TimeToBurn = timeToBurn,
                FormattedDeltaV = formattedDv,
                FormattedTotalDeltaV = formattedTotalDv,
                UnitLabel = unitSymbol,
                FormattedTNode = formattedTNode,
                FormattedBurnTime = formattedBurnTime,
                FormattedTimeToBurn = formattedTimeToBurn,
                FormattedBurnIn = burnIn,
                BadgeText = badgeText,
                TargetCardRole = targetRole,
                IsBurning = isBurning,
                IsUrgent = isUrgent,
                IsPreIgnition = isPreIgnition,
                IsBurnComplete = isBurnComplete,
                FormattedPercent = percentStr,
                VectorTag = vectorTag
            };
        }
    }

    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 机动节点指示器 (Maneuver Node Indicator)
    /// ====================================================================================
    /// 实时呈现轨道机动规划、剩余变轨速度增量 (Δv)、总计划量 (TOT Δv)、
    /// 变轨进度标尺槽 (PROGRESS METER) 与高光游标、三列内嵌精密航电时钟矩阵 (T-NODE / EST BURN / IGN IN)，
    /// 并提供一键时间加速到点火点 (WARP) 与取消机动节点 (DEL) 动作芯片。
    ///
    /// 设计与动态升级：
    /// 1. 现代太空舱仪表 HUD 风格：精雕细琢的玻璃航电面板、内凹微卡片单元与发光边框。
    /// 2. 状态微型光字牌胶囊 (Annunciator Pill)：随待命、倒计时、紧迫点火准备与燃烧中动态变色并呼吸。
    /// 3. 动态变轨能量光标与喷流微光 (Thrust Energy Cursor & Burn Shimmer)：点火时游标呼吸高亮，进度条流光脉冲。
    /// 4. 点火倒计时临近警报 (Ignition Urgency Pulse)：点火前 15 秒琥珀色脉冲警示，进入燃烧中时绿光高亮。
    /// 5. 待命模式高质感微呼吸 (Standby Breath LED)：无节点时精致待命胶囊与微型绿色 LED 呼吸点。
    /// 6. 严格遵守 MFP 规范：0 裸颜色字面量 (MFP-SPEC-006)，0 场景查询 (MFP-SPEC-007)，Cached 智能死区缓存 (MFP-SPEC-009)。
    /// </summary>
    [FlightWidget("maneuver", "maneuver_node", Category = WidgetCategory.Navigation, DisplayName = "MANEUVER 轨道机动节点指示器", Description = "实时机动节点指示器：剩余 Delta-V 进度条、节点倒计时、燃烧时长与一键推演。", DefaultWidgetId = "core.maneuver", DefaultX = 440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "core.maneuver" })]
    public class ManeuverNodeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(200f, 105f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard; // 60Hz 动态平滑绘制
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed; // 10Hz 物理数据心跳

        // 根部画布组 (用于极简隐形淡入淡出)
        private CanvasGroup _canvasGroup;

        // 视口容器分离 (极简胶囊 vs 完整节点卡片)
        private GameObject _fullNodeContainer;
        private GameObject _standbyContainer;

        // 底板与外框
        private Image _bgImage;
        private Outline _bgOutline;

        // === 极简胶囊待命模式组件 ===
        private Text _standbyTagText;
        private Image _standbyLed;
        private Text _standbyStatusText;

        // === 完整机动节点卡片组件 ===
        // Row 1: 顶部 Header
        private Text _headerTitleText;
        private GameObject _statusBadgeGo;
        private Image _statusBadgeBg;
        private Outline _statusBadgeOutline;
        private Text _statusBadgeText;
        private Image _headerDivider;

        // Row 2: 核心主读数 (Δv + 单位 + 总计划量 + 矢量/百分比)
        private Text _deltaVValueText;
        private Text _unitText;
        private Text _totalDvText;
        private Text _vectorTagText;

        // Row 3: 变轨进度槽 + 动态光流游标
        private Image _meterTrack;
        private Image _meterTickLeft;
        private Image _meterTickMid;
        private Image _meterTickRight;
        private Image _meterFill;
        private RectTransform _meterPipRt;
        private Image _meterPipImg;

        // Row 4: 三列独立微卡片单元 (T-NODE | EST BURN | IGN IN)
        private Image _cell1Bg;
        private Outline _cell1Outline;
        private Text _tNodeLabel;
        private Text _tNodeValueText;

        private Image _cell2Bg;
        private Outline _cell2Outline;
        private Text _burnTimeLabel;
        private Text _burnTimeValueText;

        private Image _cell3Bg;
        private Outline _cell3Outline;
        private Text _burnInLabel;
        private Text _burnInValueText;

        // Row 5: 动作芯片 (WARP 跃迁 + DEL 删除)
        private Button _btnWarp;
        private Image _btnWarpImg;
        private Outline _btnWarpOutline;
        private Text _btnWarpText;

        private Button _btnDismiss;
        private Image _btnDismissImg;
        private Outline _btnDismissOutline;
        private Text _btnDismissText;

        // 模式与模板通道
        private readonly Cached<ManeuverIdleMode> _idleMode = new Cached<ManeuverIdleMode>(ManeuverIdleMode.MinimalPill);
        private string _titleTemplate = I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点");
        private string _deltaVToken = "{MN:DV}";
        private string _totalDvToken = "{MN:TOTAL_DV}";
        private string _tNodeToken = "{MN:TNODE}";
        private string _burnTimeToken = "{MN:BURN}";
        private string _timeToBurnToken = "{MN:BURNTIME}";
        private string _unitTemplate = "m/s";
        private string _warpTextTemplate = I18n.Tr("WIDGET_NAV_WARP_BTN", "跃迁 ⏭");
        private string _delTextTemplate = I18n.Tr("WIDGET_NAV_DEL_BTN", "✕ 删除");

        private readonly ManeuverNodeLogic _logic = new ManeuverNodeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // 智能私有缓存 (SPEC-009: 杜绝裸 _last 字段)
        private readonly Cached<bool?> _lastHasNode = new Cached<bool?>(null);
        private readonly CachedDouble _lastDeltaV = new CachedDouble(double.NaN);
        private readonly Cached<string> _lastBadgeStr = new Cached<string>(string.Empty);
        private readonly Cached<CardStyleRole> _lastCardRole = new Cached<CardStyleRole>(CardStyleRole.Normal);
        private readonly Cached<string> _lastTNodeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBurnTimeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBurnInStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastTotalDvStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastVectorTagStr = new Cached<string>(string.Empty);
        private readonly Cached<bool?> _lastEditMode = new Cached<bool?>(null);
        private readonly Cached<bool> _lastWarpInteractable = new Cached<bool>(true);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _bgImage = CardBackground;
            _bgOutline = CardOutline;

            // 1. 解析模板与配置
            _titleTemplate = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点"));
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.UnitLabel)) _unitTemplate = config.UnitLabel;
            }
            _deltaVToken = GetTemplateChannel(new[] { "DV", "DV_TOKEN" }, _deltaVToken);
            _totalDvToken = GetTemplateChannel(new[] { "TOTAL_DV", "TOTAL_DV_TOKEN" }, _totalDvToken);
            _tNodeToken = GetTemplateChannel(new[] { "TNODE", "TNODE_TOKEN" }, _tNodeToken);
            _burnTimeToken = GetTemplateChannel(new[] { "BURN", "BURN_TOKEN" }, _burnTimeToken);
            _timeToBurnToken = GetTemplateChannel(new[] { "BURNTIME", "BURNTIME_TOKEN" }, _timeToBurnToken);
            _warpTextTemplate = GetTemplateChannel("WARP_LABEL", _warpTextTemplate);
            _delTextTemplate = GetTemplateChannel("DEL_LABEL", _delTextTemplate);

            string idleSetting = GetTemplateChannel("IDLE", string.Empty);
            if (idleSetting.Equals("HIDE", StringComparison.OrdinalIgnoreCase))
            {
                _idleMode.Value = ManeuverIdleMode.AutoHide;
            }

            // 2. 创建视口容器
            var fullRt = CreateContainer("FullNodeContainer", transform);
            _fullNodeContainer = fullRt.gameObject;
            fullRt.anchorMin = Vector2.zero;
            fullRt.anchorMax = Vector2.one;
            fullRt.sizeDelta = Vector2.zero;
            fullRt.anchoredPosition = Vector2.zero;

            var standbyRt = CreateContainer("StandbyContainer", transform);
            _standbyContainer = standbyRt.gameObject;
            standbyRt.anchorMin = Vector2.zero;
            standbyRt.anchorMax = Vector2.one;
            standbyRt.sizeDelta = Vector2.zero;
            standbyRt.anchoredPosition = Vector2.zero;

            // 初始默认为极简胶囊待机 (避免第一帧双层重叠)
            _fullNodeContainer.SetActiveSafe(false);
            _standbyContainer.SetActiveSafe(true);
            RectTransform.SetSizeDeltaSafe(new Vector2(200f * s, 24f * s));

            // =========================================================================
            // 3. 构建极简胶囊待命视图 (_standbyContainer)
            // =========================================================================
            _standbyTagText = UIFactory.CreateText(_standbyContainer.transform, "Standby_Tag", I18n.Tr("WIDGET_NAV_MNV_TAG", "◆ 机动"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft,
                theme.AccentPrimary);
            _standbyTagText.fontStyle = FontStyle.Bold;
            RectTransform sTagRt = _standbyTagText.rectTransform;
            sTagRt.pivot = new Vector2(0f, 0.5f);
            sTagRt.anchorMin = sTagRt.anchorMax = new Vector2(0.5f, 0.5f);
            sTagRt.sizeDelta = new Vector2(52f * s, 18f * s);
            sTagRt.anchoredPosition = new Vector2(-92f * s, 0f);

            GameObject ledGo = UIFactory.CreatePanel(_standbyContainer.transform, "Standby_Led",
                new Vector2(4f * s, 4f * s), new Vector2(-36f * s, 0f),
                theme.AccentPrimary);
            _standbyLed = ledGo.GetComponent<Image>();

            _standbyStatusText = UIFactory.CreateText(_standbyContainer.transform, "Standby_Status", I18n.Tr("WIDGET_NAV_STANDBY_PILL", "待命 · 无机动规划"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform sStatRt = _standbyStatusText.rectTransform;
            sStatRt.pivot = new Vector2(1f, 0.5f);
            sStatRt.anchorMin = sStatRt.anchorMax = new Vector2(0.5f, 0.5f);
            sStatRt.sizeDelta = new Vector2(122f * s, 18f * s);
            sStatRt.anchoredPosition = new Vector2(92f * s, 0f);

            // =========================================================================
            // 4. 构建完整机动节点卡片视图 (_fullNodeContainer)
            // =========================================================================

            // --- Row 1: Header (标题 + 光字牌徽标胶囊 + 细分隔线) ---
            string initialTitle = _titleTemplate.StartsWith("◆", StringComparison.Ordinal) ? _titleTemplate : ("◆ " + _titleTemplate);
            _headerTitleText = UIFactory.CreateText(_fullNodeContainer.transform, "Header_Title", initialTitle, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            _headerTitleText.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _headerTitleText.rectTransform;
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(100f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-92f * s, 40.5f * s);

            // 状态光字牌胶囊
            GameObject badgeBox = UIFactory.CreatePanel(_fullNodeContainer.transform, "Status_Pill",
                new Vector2(66f * s, 15f * s), new Vector2(92f * s, 40.5f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _statusBadgeGo = badgeBox;
            _statusBadgeBg = badgeBox.GetComponent<Image>();
            _statusBadgeOutline = badgeBox.GetComponent<Outline>() ?? badgeBox.AddComponent<Outline>();
            _statusBadgeOutline.effectDistance = new Vector2(1f, 1f);
            _statusBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            RectTransform bBoxRt = badgeBox.GetComponent<RectTransform>();
            bBoxRt.pivot = new Vector2(1f, 0.5f);
            bBoxRt.anchorMin = bBoxRt.anchorMax = new Vector2(0.5f, 0.5f);
            bBoxRt.anchoredPosition = new Vector2(92f * s, 40.5f * s);

            _statusBadgeText = UIFactory.CreateText(badgeBox.transform, "Text", I18n.Tr("WIDGET_ALERT_ARMED", "待命"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _statusBadgeText.fontStyle = FontStyle.Bold;
            RectTransform badgeRt = _statusBadgeText.rectTransform;
            badgeRt.anchorMin = Vector2.zero;
            badgeRt.anchorMax = Vector2.one;
            badgeRt.sizeDelta = Vector2.zero;
            badgeRt.anchoredPosition = Vector2.zero;

            GameObject divGo = UIFactory.CreatePanel(_fullNodeContainer.transform, "Header_Divider",
                new Vector2(184f * s, 1f * s), new Vector2(0f, 31f * s),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _headerDivider = divGo.GetComponent<Image>();

            // --- Row 2: 核心主读数 (Δv + 单位 + 总计划量 + 矢量状态) ---
            _deltaVValueText = UIFactory.CreateText(_fullNodeContainer.transform, "DeltaV_Value", "---", Mathf.RoundToInt(22f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _deltaVValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = _deltaVValueText.rectTransform;
            valRt.pivot = new Vector2(0f, 0.5f);
            valRt.anchorMin = valRt.anchorMax = new Vector2(0.5f, 0.5f);
            valRt.sizeDelta = new Vector2(95f * s, 24f * s);
            valRt.anchoredPosition = new Vector2(-92f * s, 16.5f * s);

            _unitText = UIFactory.CreateText(_fullNodeContainer.transform, "Unit_Label", _unitTemplate, Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Unit, theme));
            _unitText.fontStyle = FontStyle.Bold;
            RectTransform unitRt = _unitText.rectTransform;
            unitRt.pivot = new Vector2(0f, 0.5f);
            unitRt.anchorMin = unitRt.anchorMax = new Vector2(0.5f, 0.5f);
            unitRt.sizeDelta = new Vector2(32f * s, 16f * s);
            unitRt.anchoredPosition = new Vector2(5f * s, 13.5f * s);

            _totalDvText = UIFactory.CreateText(_fullNodeContainer.transform, "TotalDv_Label", I18n.Tr("WIDGET_NAV_TOT_DV_PLACEHOLDER", "TOT ---"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform totRt = _totalDvText.rectTransform;
            totRt.pivot = new Vector2(1f, 0.5f);
            totRt.anchorMin = totRt.anchorMax = new Vector2(0.5f, 0.5f);
            totRt.sizeDelta = new Vector2(60f * s, 12f * s);
            totRt.anchoredPosition = new Vector2(92f * s, 21.5f * s);

            _vectorTagText = UIFactory.CreateText(_fullNodeContainer.transform, "Vector_Tag", "100%", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight,
                WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.85f));
            RectTransform vecRt = _vectorTagText.rectTransform;
            vecRt.pivot = new Vector2(1f, 0.5f);
            vecRt.anchorMin = vecRt.anchorMax = new Vector2(0.5f, 0.5f);
            vecRt.sizeDelta = new Vector2(60f * s, 12f * s);
            vecRt.anchoredPosition = new Vector2(92f * s, 10.5f * s);

            // --- Row 3: 变轨进度标尺槽 (Progress Meter) 与发光游标 ---
            GameObject trackGo = UIFactory.CreatePanel(_fullNodeContainer.transform, "Meter_Track",
                new Vector2(184f * s, 5f * s), new Vector2(0f, -0.5f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _meterTrack = trackGo.GetComponent<Image>();

            // 刻度微标线
            GameObject tickL = UIFactory.CreatePanel(trackGo.transform, "Tick_Left",
                new Vector2(1f * s, 7f * s), new Vector2(-92f * s, 0f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _meterTickLeft = tickL.GetComponent<Image>();

            GameObject tickM = UIFactory.CreatePanel(trackGo.transform, "Tick_Mid",
                new Vector2(1f * s, 5f * s), new Vector2(0f, 0f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _meterTickMid = tickM.GetComponent<Image>();

            GameObject tickR = UIFactory.CreatePanel(trackGo.transform, "Tick_Right",
                new Vector2(1f * s, 7f * s), new Vector2(92f * s, 0f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _meterTickRight = tickR.GetComponent<Image>();

            // 进度填充层
            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Meter_Fill",
                new Vector2(184f * s, 5f * s), Vector2.zero,
                theme.AccentPrimary);
            _meterFill = fillGo.GetComponent<Image>();
            _meterFill.type = Image.Type.Filled;
            _meterFill.fillMethod = Image.FillMethod.Horizontal;
            _meterFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _meterFill.fillAmount = 1.0f;
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            fillRt.anchoredPosition = Vector2.zero;

            // 动态光标游标 (Pip)
            GameObject pipGo = UIFactory.CreatePanel(trackGo.transform, "Meter_Pip",
                new Vector2(3f * s, 8f * s), new Vector2(92f * s, 0f),
                theme.AccentPrimary);
            _meterPipRt = pipGo.GetComponent<RectTransform>();
            _meterPipRt.pivot = new Vector2(0.5f, 0.5f);
            _meterPipImg = pipGo.GetComponent<Image>();

            // --- Row 4: 三列独立微卡片单元 (T-NODE | EST BURN | IGN IN) ---
            float cellW = 58f * s;
            float cellH = 22f * s;
            Vector2 cellSize = new Vector2(cellW, cellH);

            // Cell 1: T-NODE
            GameObject c1Go = UIFactory.CreatePanel(_fullNodeContainer.transform, "Cell_TNode", cellSize, new Vector2(-63f * s, -18f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _cell1Bg = c1Go.GetComponent<Image>();
            _cell1Outline = c1Go.GetComponent<Outline>() ?? c1Go.AddComponent<Outline>();
            _cell1Outline.effectDistance = new Vector2(1f, 1f);
            _cell1Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _tNodeLabel = UIFactory.CreateText(c1Go.transform, "Label", I18n.Tr("WIDGET_NAV_TNODE", "节点倒计时"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tnLblRt = _tNodeLabel.rectTransform;
            tnLblRt.sizeDelta = new Vector2(cellW, 8.5f * s);
            tnLblRt.anchoredPosition = new Vector2(0f, 4.8f * s);

            _tNodeValueText = UIFactory.CreateText(c1Go.transform, "Value", "--:--", Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _tNodeValueText.fontStyle = FontStyle.Bold;
            RectTransform tnValRt = _tNodeValueText.rectTransform;
            tnValRt.sizeDelta = new Vector2(cellW, 11.5f * s);
            tnValRt.anchoredPosition = new Vector2(0f, -4.5f * s);

            // Cell 2: EST BURN
            GameObject c2Go = UIFactory.CreatePanel(_fullNodeContainer.transform, "Cell_BurnTime", cellSize, new Vector2(0f, -18f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _cell2Bg = c2Go.GetComponent<Image>();
            _cell2Outline = c2Go.GetComponent<Outline>() ?? c2Go.AddComponent<Outline>();
            _cell2Outline.effectDistance = new Vector2(1f, 1f);
            _cell2Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _burnTimeLabel = UIFactory.CreateText(c2Go.transform, "Label", I18n.Tr("WIDGET_NAV_BURN_TIME", "燃烧时长"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform btLblRt = _burnTimeLabel.rectTransform;
            btLblRt.sizeDelta = new Vector2(cellW, 8.5f * s);
            btLblRt.anchoredPosition = new Vector2(0f, 4.8f * s);

            _burnTimeValueText = UIFactory.CreateText(c2Go.transform, "Value", "--:--", Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _burnTimeValueText.fontStyle = FontStyle.Bold;
            RectTransform btValRt = _burnTimeValueText.rectTransform;
            btValRt.sizeDelta = new Vector2(cellW, 11.5f * s);
            btValRt.anchoredPosition = new Vector2(0f, -4.5f * s);

            // Cell 3: IGN IN
            GameObject c3Go = UIFactory.CreatePanel(_fullNodeContainer.transform, "Cell_BurnIn", cellSize, new Vector2(63f * s, -18f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _cell3Bg = c3Go.GetComponent<Image>();
            _cell3Outline = c3Go.GetComponent<Outline>() ?? c3Go.AddComponent<Outline>();
            _cell3Outline.effectDistance = new Vector2(1f, 1f);
            _cell3Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _burnInLabel = UIFactory.CreateText(c3Go.transform, "Label", I18n.Tr("WIDGET_NAV_IGN_IN", "点火倒计时"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform biLblRt = _burnInLabel.rectTransform;
            biLblRt.sizeDelta = new Vector2(cellW, 8.5f * s);
            biLblRt.anchoredPosition = new Vector2(0f, 4.8f * s);

            _burnInValueText = UIFactory.CreateText(c3Go.transform, "Value", "--:--", Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _burnInValueText.fontStyle = FontStyle.Bold;
            RectTransform biValRt = _burnInValueText.rectTransform;
            biValRt.sizeDelta = new Vector2(cellW, 11.5f * s);
            biValRt.anchoredPosition = new Vector2(0f, -4.5f * s);

            // --- Row 5: 底栏 (全宽对称高级动作芯片) ---
            Vector2 btnSize = new Vector2(88f * s, 17f * s);

            // 动作芯片：WARP (跃迁)
            _btnWarp = UIFactory.CreateButton(_fullNodeContainer.transform, "Btn_Warp", btnSize, new Vector2(-47f * s, -41.5f * s), OnWarpClicked);
            _btnWarpImg = _btnWarp.GetComponent<Image>();
            _btnWarpImg.color = WidgetStyleManager.Tint(WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme), theme.AccentPrimary, 0.22f);
            _btnWarpOutline = _btnWarp.gameObject.GetComponent<Outline>() ?? _btnWarp.gameObject.AddComponent<Outline>();
            _btnWarpOutline.effectDistance = new Vector2(1f, 1f);
            _btnWarpOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.70f);

            _btnWarpText = UIFactory.CreateText(_btnWarp.transform, "Text", _warpTextTemplate, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter,
                theme.AccentPrimary);
            _btnWarpText.fontStyle = FontStyle.Bold;
            _btnWarpText.rectTransform.sizeDelta = btnSize;
            _btnWarpText.rectTransform.anchoredPosition = Vector2.zero;
            var warpFb = _btnWarp.GetComponent<AvionicsButtonFeedback>();
            if (warpFb != null)
            {
                warpFb.VisualRole = ButtonVisualRole.Primary;
                warpFb.LabelText = _btnWarpText;
                warpFb.CustomInactiveColor = theme.AccentPrimary;
            }

            // 动作芯片：DEL (删除)
            _btnDismiss = UIFactory.CreateButton(_fullNodeContainer.transform, "Btn_Del", btnSize, new Vector2(47f * s, -41.5f * s), OnDismissClicked);
            _btnDismissImg = _btnDismiss.GetComponent<Image>();
            _btnDismissImg.color = WidgetStyleManager.Tint(WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme), theme.DangerColor, 0.18f);
            _btnDismissOutline = _btnDismiss.gameObject.GetComponent<Outline>() ?? _btnDismiss.gameObject.AddComponent<Outline>();
            _btnDismissOutline.effectDistance = new Vector2(1f, 1f);
            _btnDismissOutline.effectColor = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.60f);

            _btnDismissText = UIFactory.CreateText(_btnDismiss.transform, "Text", _delTextTemplate, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Danger, theme));
            _btnDismissText.fontStyle = FontStyle.Bold;
            _btnDismissText.rectTransform.sizeDelta = btnSize;
            _btnDismissText.rectTransform.anchoredPosition = Vector2.zero;

            var delFb = _btnDismiss.GetComponent<AvionicsButtonFeedback>();
            if (delFb != null)
            {
                delFb.VisualRole = ButtonVisualRole.Danger;
                delFb.LabelText = _btnDismissText;
                delFb.CustomInactiveColor = theme.DangerColor;
            }

            // 注册微控件至标准化管理器 (保持兼容)
            WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, _lastCardRole.Value, t));
            this.Controls.Register(new WidgetHeaderControl("header", "标题栏", _headerTitleText != null ? _headerTitleText.gameObject : null, _headerTitleText, _statusBadgeText));
            this.Controls.Register(new WidgetReadoutControl("deltav_readout", "DeltaV读数", _deltaVValueText != null ? _deltaVValueText.gameObject : null, _deltaVValueText, _unitText, TextStyleRole.PrimaryValue, _deltaVToken));
            WidgetControlManager.WrapElement(this, "progress_meter", "变轨进度条", _meterTrack != null ? _meterTrack.gameObject : null, (t) =>
            {
                if (_meterTrack != null) _meterTrack.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, t);
                if (_meterFill != null) _meterFill.color = t.AccentPrimary;
            });
            this.Controls.Register(new WidgetReadoutControl("tnode_readout", "节点倒计时", _tNodeValueText != null ? _tNodeValueText.gameObject : null, _tNodeValueText, _tNodeLabel, TextStyleRole.PrimaryValue, _tNodeToken));
            this.Controls.Register(new WidgetReadoutControl("burntime_readout", "燃烧时长", _burnTimeValueText != null ? _burnTimeValueText.gameObject : null, _burnTimeValueText, _burnTimeLabel, TextStyleRole.PrimaryValue, _burnTimeToken));
            this.Controls.Register(new WidgetReadoutControl("burn_in_text", "点火倒计时", _burnInValueText != null ? _burnInValueText.gameObject : null, _burnInValueText, _burnInLabel, TextStyleRole.PrimaryValue, _timeToBurnToken));
            this.Controls.Register(new WidgetActionButtonControl(this, "warp_btn", "推演按键", _btnWarp != null ? _btnWarp.gameObject : null, _btnWarp, _btnWarpImg, _btnWarpOutline, _btnWarpText, null, _warpTextTemplate, OnWarpClicked, false) { VisualRole = ButtonVisualRole.Primary });
            this.Controls.Register(new WidgetActionButtonControl(this, "dismiss_btn", "取消按键", _btnDismiss != null ? _btnDismiss.gameObject : null, _btnDismiss, _btnDismissImg, _btnDismissOutline, _btnDismissText, null, _delTextTemplate, OnDismissClicked, false) { VisualRole = ButtonVisualRole.Danger });

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 极简待命
            if (_standbyTagText != null) _standbyTagText.color = theme.AccentPrimary;
            if (_standbyLed != null) _standbyLed.color = theme.AccentPrimary;
            ApplyText(_standbyStatusText, TextStyleRole.SecondaryValue, theme);

            // 完整节点 Header
            ApplyText(_headerTitleText, TextStyleRole.Label, theme);
            if (_statusBadgeBg != null) _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_statusBadgeOutline != null) _statusBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            if (_headerDivider != null) _headerDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 核心读数
            ApplyText(_deltaVValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);
            ApplyText(_totalDvText, TextStyleRole.SecondaryValue, theme);
            if (_vectorTagText != null) _vectorTagText.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.85f);

            // 进度槽
            if (_meterTrack != null) _meterTrack.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_meterTickLeft != null) _meterTickLeft.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_meterTickMid != null) _meterTickMid.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_meterTickRight != null) _meterTickRight.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_meterFill != null) _meterFill.color = theme.AccentPrimary;
            if (_meterPipImg != null) _meterPipImg.color = theme.AccentPrimary;

            // 微卡片 1
            if (_cell1Bg != null) _cell1Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_cell1Outline != null) _cell1Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_tNodeLabel, TextStyleRole.Label, theme);
            ApplyText(_tNodeValueText, TextStyleRole.PrimaryValue, theme);

            // 微卡片 2
            if (_cell2Bg != null) _cell2Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_cell2Outline != null) _cell2Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_burnTimeLabel, TextStyleRole.Label, theme);
            ApplyText(_burnTimeValueText, TextStyleRole.PrimaryValue, theme);

            // 微卡片 3
            if (_cell3Bg != null) _cell3Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_cell3Outline != null) _cell3Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_burnInLabel, TextStyleRole.Label, theme);
            ApplyText(_burnInValueText, TextStyleRole.PrimaryValue, theme);

            // 动作芯片按键
            if (_btnWarpImg != null)
            {
                _btnWarpImg.color = WidgetStyleManager.Tint(WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme), theme.AccentPrimary, 0.22f);
            }
            if (_btnWarpOutline != null)
            {
                _btnWarpOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.70f);
            }
            if (_btnDismissImg != null)
            {
                _btnDismissImg.color = WidgetStyleManager.Tint(WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme), theme.DangerColor, 0.18f);
            }
            if (_btnDismissOutline != null)
            {
                _btnDismissOutline.effectColor = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.60f);
            }

            if (_btnWarp != null)
            {
                var fb = _btnWarp.GetComponent<AvionicsButtonFeedback>();
                if (fb != null) fb.ApplyTheme(theme);
            }
            if (_btnDismiss != null)
            {
                var fb = _btnDismiss.GetComponent<AvionicsButtonFeedback>();
                if (fb != null) fb.ApplyTheme(theme);
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context) => base.OnUIDrawLoop(ref context);

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            var theme = WidgetStyleManager.Instance?.CurrentTheme;
            float s = CurrentDpiScale;
            bool isEdit = WidgetDragHandler.IsEditModeActive;

            bool hasNodeChanged = _lastHasNode.Update(state.HasNode);
            bool editChanged = _lastEditMode.Update(isEdit);

            // 状态机流转：有节点展开 vs 无节点极简隐藏/收拢
            if (hasNodeChanged || editChanged)
            {
                if (!state.HasNode)
                {
                    if (_idleMode.Value == ManeuverIdleMode.AutoHide && !isEdit)
                    {
                        if (_canvasGroup != null)
                        {
                            _canvasGroup.alpha = 0f;
                            _canvasGroup.blocksRaycasts = false;
                        }
                        _fullNodeContainer.SetActiveSafe(false);
                        _standbyContainer.SetActiveSafe(false);
                        RectTransform.SetSizeDeltaSafe(new Vector2(200f * s, 24f * s));
                        return;
                    }
                    else
                    {
                        if (_canvasGroup != null)
                        {
                            _canvasGroup.alpha = 1f;
                            _canvasGroup.blocksRaycasts = true;
                        }
                        _fullNodeContainer.SetActiveSafe(false);
                        _standbyContainer.SetActiveSafe(true);
                        RectTransform.SetSizeDeltaSafe(new Vector2(200f * s, 24f * s));

                        if (isEdit)
                        {
                            _standbyStatusText.SetTextSafe(I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点"));
                        }
                        else
                        {
                            _standbyStatusText.SetTextSafe(I18n.Tr("WIDGET_NAV_STANDBY_PILL", "待命 · 无机动规划"));
                        }

                        ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
                        return;
                    }
                }
                else
                {
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.alpha = 1f;
                        _canvasGroup.blocksRaycasts = true;
                    }
                    _standbyContainer.SetActiveSafe(false);
                    _fullNodeContainer.SetActiveSafe(true);
                    RectTransform.SetSizeDeltaSafe(new Vector2(200f * s, 105f * s));
                }
            }

            if (!state.HasNode)
            {
                // 待命模式呼吸指示灯
                if (_standbyLed != null && theme != null)
                {
                    float standbyPulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
                    _standbyLed.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, standbyPulse);
                }
                return;
            }

            // 1. 核心 Δv 读数与单位
            if (_lastDeltaV.Update(state.DeltaV))
            {
                _deltaVValueText.SetTextSafe(state.FormattedDeltaV);
                _unitText.SetTextSafe(state.UnitLabel);
            }

            if (_lastTotalDvStr.Update(state.FormattedTotalDeltaV))
            {
                _totalDvText.SetTextSafe("Σ " + state.FormattedTotalDeltaV);
            }

            if (_lastVectorTagStr.Update(state.VectorTag))
            {
                _vectorTagText.SetTextSafe(state.VectorTag);
            }

            // 2. 变轨进度槽与光标位置
            if (_meterFill != null)
            {
                _meterFill.fillAmount = state.MeterFraction;
            }

            if (_meterPipRt != null)
            {
                // 进度光标锚定：进度条宽 184px，中心点 0，起点 -92px，终点 +92px
                float pipX = (-92f + 184f * state.MeterFraction) * s;
                _meterPipRt.anchoredPosition = new Vector2(pipX, 0f);
            }

            // 3. 三列独立精密微卡片
            if (_lastTNodeStr.Update(state.FormattedTNode))
            {
                _tNodeValueText.SetTextSafe(state.FormattedTNode);
            }

            if (_lastBurnTimeStr.Update(state.FormattedBurnTime))
            {
                _burnTimeValueText.SetTextSafe(state.FormattedBurnTime);
            }

            if (_lastBurnInStr.Update(state.FormattedTimeToBurn))
            {
                _burnInValueText.SetTextSafe(state.FormattedTimeToBurn);
            }

            // 4. 状态徽标文字
            if (_lastBadgeStr.Update(state.BadgeText))
            {
                _statusBadgeText.SetTextSafe(state.BadgeText);
            }

            // 5. 跃迁按键可交互状态 (点火中或小于5秒时自动禁用)
            bool canWarp = !state.IsBurning && !state.IsBurnComplete && state.TimeToBurn > 5.0;
            if (_lastWarpInteractable.Update(canWarp))
            {
                if (_btnWarp != null) _btnWarp.interactable = canWarp;
            }

            // 6. 动态微动效渲染 (60Hz 视觉反馈)
            if (theme != null)
            {
                if (state.IsBurning)
                {
                    // 点火推力高频能量微流光 (8Hz)
                    float burnPulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);

                    if (_meterFill != null)
                    {
                        _meterFill.color = WidgetStyleManager.Tint(theme.AccentPrimary, theme.TextPrimaryColor, 0.25f * burnPulse);
                    }
                    if (_meterPipImg != null)
                    {
                        _meterPipImg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.70f + 0.30f * burnPulse);
                    }

                    // 光字牌胶囊活跃绿光呼吸
                    if (_statusBadgeBg != null)
                    {
                        _statusBadgeBg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Success, theme), 0.70f + 0.30f * burnPulse);
                    }
                    if (_statusBadgeOutline != null)
                    {
                        _statusBadgeOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.85f + 0.15f * burnPulse);
                    }
                    if (_statusBadgeText != null)
                    {
                        _statusBadgeText.color = theme.AccentPrimary;
                    }

                    // Cell 3 (IGN IN) 燃烧中高光反馈
                    if (_cell3Bg != null)
                    {
                        _cell3Bg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Success, theme), 0.50f + 0.30f * burnPulse);
                    }
                    if (_cell3Outline != null)
                    {
                        _cell3Outline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.75f + 0.25f * burnPulse);
                    }
                    if (_burnInValueText != null)
                    {
                        _burnInValueText.color = theme.AccentPrimary;
                    }
                }
                else if (state.IsUrgent)
                {
                    // 点火临界预警琥珀脉冲 (2.5Hz)
                    float warnPulse = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5.0f));

                    if (_statusBadgeBg != null)
                    {
                        _statusBadgeBg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme), 0.60f * warnPulse);
                    }
                    if (_statusBadgeOutline != null)
                    {
                        _statusBadgeOutline.effectColor = WidgetStyleManager.WithAlpha(theme.WarningColor, warnPulse);
                    }
                    if (_statusBadgeText != null)
                    {
                        _statusBadgeText.color = theme.WarningColor;
                    }

                    if (_cell3Bg != null)
                    {
                        _cell3Bg.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.16f * warnPulse);
                    }
                    if (_cell3Outline != null)
                    {
                        _cell3Outline.effectColor = WidgetStyleManager.WithAlpha(theme.WarningColor, warnPulse);
                    }
                    if (_burnInValueText != null)
                    {
                        _burnInValueText.color = theme.WarningColor;
                    }

                    if (_meterFill != null) _meterFill.color = theme.AccentPrimary;
                    if (_meterPipImg != null) _meterPipImg.color = WidgetStyleManager.WithAlpha(theme.WarningColor, warnPulse);
                }
                else
                {
                    // 常规平稳态
                    if (_statusBadgeBg != null)
                    {
                        _statusBadgeBg.color = state.IsPreIgnition
                            ? WidgetStyleManager.WithAlpha(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme), 0.40f)
                            : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    }
                    if (_statusBadgeOutline != null)
                    {
                        _statusBadgeOutline.effectColor = state.IsPreIgnition
                            ? WidgetStyleManager.WithAlpha(theme.WarningColor, 0.60f)
                            : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    }
                    if (_statusBadgeText != null)
                    {
                        _statusBadgeText.color = state.IsPreIgnition
                            ? theme.WarningColor
                            : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    }

                    if (_cell3Bg != null) _cell3Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    if (_cell3Outline != null) _cell3Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    if (_burnInValueText != null) _burnInValueText.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);

                    if (_meterFill != null) _meterFill.color = theme.AccentPrimary;
                    if (_meterPipImg != null) _meterPipImg.color = theme.AccentPrimary;
                }
            }

            // 7. 卡片语义角色动态反光 (Normal / Warning / Emphasized)
            if (_lastCardRole.Update(state.TargetCardRole))
            {
                ApplyCard(_bgImage, _bgOutline, state.TargetCardRole, theme);
            }
        }

        public override void PopulateContextMenu(Action<string, Action> registerAction)
        {
            base.PopulateContextMenu(registerAction);

            string idleLabel = _idleMode.Value == ManeuverIdleMode.MinimalPill
                ? I18n.Tr("CTX_MNV_MODE_AUTOHIDE", "⚡ 无节点模式: 切换为【自动完全隐身】")
                : I18n.Tr("CTX_MNV_MODE_PILL", "⚡ 无节点模式: 切换为【极简胶囊待命】");

            registerAction?.Invoke(idleLabel, ToggleIdleMode);
        }

        private void ToggleIdleMode()
        {
            _idleMode.Value = (_idleMode.Value == ManeuverIdleMode.MinimalPill) ? ManeuverIdleMode.AutoHide : ManeuverIdleMode.MinimalPill;
            string modeStr = ((int)_idleMode.Value).ToString();
            if (Config != null)
            {
                Config.CustomTemplate = string.IsNullOrEmpty(Config.CustomTemplate) ? $"IDLE={modeStr}" : $"IDLE={modeStr};" + Config.CustomTemplate;
            }
            string tip = _idleMode.Value == ManeuverIdleMode.AutoHide
                ? I18n.Tr("TIP_MNV_MODE_AUTOHIDE", "已切换至自动完全隐身模式 (无机动节点时自动隐形)")
                : I18n.Tr("TIP_MNV_MODE_PILL", "已切换至极简胶囊模式 (无机动节点时收起为微型待命条)");
            MFPToastBridge.Show(tip);
            _lastHasNode.Reset(null);
        }

        private void OnWarpClicked()
        {
            FlightTelemetryContext.Current?.WarpToManeuverNode();
        }

        private void OnDismissClicked()
        {
            FlightTelemetryContext.Current?.DeleteManeuverNode();
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            _titleTemplate = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点"));
            _warpTextTemplate = GetTemplateChannel("WARP_LABEL", I18n.Tr("WIDGET_NAV_WARP_BTN", "跃迁 ⏭"));
            _delTextTemplate = GetTemplateChannel("DEL_LABEL", I18n.Tr("WIDGET_NAV_DEL_BTN", "✕ 删除"));
            if (_headerTitleText != null)
            {
                string title = _titleTemplate.StartsWith("◆", StringComparison.Ordinal) ? _titleTemplate : ("◆ " + _titleTemplate);
                _headerTitleText.text = title;
            }
            if (_btnWarpText != null) _btnWarpText.text = _warpTextTemplate;
            if (_btnDismissText != null) _btnDismissText.text = _delTextTemplate;
            if (_tNodeLabel != null) _tNodeLabel.text = I18n.Tr("WIDGET_NAV_TNODE", "节点倒计时");
            if (_burnTimeLabel != null) _burnTimeLabel.text = I18n.Tr("WIDGET_NAV_BURN_TIME", "燃烧时长");
            if (_burnInLabel != null) _burnInLabel.text = I18n.Tr("WIDGET_NAV_IGN_IN", "点火倒计时");
            if (_standbyStatusText != null) _standbyStatusText.text = I18n.Tr("WIDGET_NAV_STANDBY_PILL", "待命 · 无机动规划");
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_btnWarp != null)
            {
                _btnWarp.onClick.RemoveListener(OnWarpClicked);
                _btnWarp = null;
            }
            if (_btnDismiss != null)
            {
                _btnDismiss.onClick.RemoveListener(OnDismissClicked);
                _btnDismiss = null;
            }
            base.OnDestroy();
        }
    }
}

