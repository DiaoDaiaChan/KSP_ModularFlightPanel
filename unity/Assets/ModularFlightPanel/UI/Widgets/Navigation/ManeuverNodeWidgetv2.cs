using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
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
    /// 第二代机动节点指示器视图组件 (Maneuver Node Widget v2)
    /// 严格遵循 WidgetLogic 大脑与 BaseFlightWidget 视图渲染解耦规范。
    /// </summary>
    [FlightWidget("maneuver_v2", "maneuver_node_v2", Category = WidgetCategory.Navigation, DisplayName = "MANEUVER v2 轨道机动节点指示器", Description = "第二代航电机动指示器：纯 C# WidgetLogic 解算大脑与全舱量纲换算中枢联动。", DefaultWidgetId = "core.maneuver_v2", DefaultX = 440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "core.maneuver_v2" })]
    public class ManeuverNodeWidgetv2 : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(200f, 105f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        private readonly ManeuverNodeLogic _logic = new ManeuverNodeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // UI 层次节点
        private Image _bgImage;
        private Outline _bgOutline;
        private Text _headerTitleText;
        private Text _statusBadgeText;
        private Text _deltaVValueText;
        private Text _unitText;
        private Image _meterTrack;
        private Image _meterFill;
        private Text _tNodeLabel;
        private Text _tNodeValueText;
        private Text _burnTimeLabel;
        private Text _burnTimeValueText;
        private Text _burnInText;
        private Button _btnWarp;
        private Image _btnWarpImg;
        private Text _btnWarpText;
        private Button _btnDismiss;
        private Image _btnDismissImg;
        private Text _btnDismissText;

        // 智能私有缓存 (SPEC-009)
        private readonly Cached<bool> _lastHasNode = new Cached<bool>(false);
        private readonly CachedDouble _lastDeltaV = new CachedDouble(double.NaN);
        private readonly Cached<string> _lastBadgeStr = new Cached<string>(string.Empty);
        private readonly Cached<CardStyleRole> _lastCardRole = new Cached<CardStyleRole>(CardStyleRole.Normal);
        private readonly Cached<string> _lastTNodeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBurnTimeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBurnInStr = new Cached<string>(string.Empty);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Vector2 cardSize = BaseSize * s;
            RectTransform.sizeDelta = cardSize;

            _bgImage = CardBackground;
            _bgOutline = CardOutline;

            // 1. 顶部 Header
            _headerTitleText = UIFactory.CreateText(
                transform,
                "Header_Title",
                I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点"),
                Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform titleRt = _headerTitleText.rectTransform;
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(120f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-92f * s, 42f * s);

            _statusBadgeText = UIFactory.CreateText(
                transform,
                "Status_Badge",
                I18n.Tr("PHASE_STANDBY", "待机"),
                Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform badgeRt = _statusBadgeText.rectTransform;
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0.5f, 0.5f);
            badgeRt.sizeDelta = new Vector2(60f * s, 16f * s);
            badgeRt.anchoredPosition = new Vector2(92f * s, 42f * s);

            // 2. 核心读数
            _deltaVValueText = UIFactory.CreateText(
                transform,
                "DeltaV_Value",
                "---",
                Mathf.RoundToInt(22f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valRt = _deltaVValueText.rectTransform;
            valRt.pivot = new Vector2(0f, 0.5f);
            valRt.anchorMin = valRt.anchorMax = new Vector2(0.5f, 0.5f);
            valRt.sizeDelta = new Vector2(130f * s, 26f * s);
            valRt.anchoredPosition = new Vector2(-92f * s, 22f * s);

            _unitText = UIFactory.CreateText(
                transform,
                "Unit_Label",
                "m/s",
                Mathf.RoundToInt(10f * s),
                TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform unitRt = _unitText.rectTransform;
            unitRt.pivot = new Vector2(1f, 0.5f);
            unitRt.anchorMin = unitRt.anchorMax = new Vector2(0.5f, 0.5f);
            unitRt.sizeDelta = new Vector2(40f * s, 18f * s);
            unitRt.anchoredPosition = new Vector2(92f * s, 20f * s);

            // 3. 进度条
            GameObject meterTrackGo = UIFactory.CreatePanel(
                transform,
                "Meter_Track",
                new Vector2(184f * s, 4f * s),
                new Vector2(0f, 4f * s),
                style.GetMeterColor(MeterStyleRole.Track, theme));
            _meterTrack = meterTrackGo.GetComponent<Image>();

            GameObject meterFillGo = UIFactory.CreatePanel(
                meterTrackGo.transform,
                "Meter_Fill",
                new Vector2(184f * s, 4f * s),
                Vector2.zero,
                style.GetMeterColor(MeterStyleRole.Primary, theme));
            _meterFill = meterFillGo.GetComponent<Image>();
            _meterFill.type = Image.Type.Filled;
            _meterFill.fillMethod = Image.FillMethod.Horizontal;
            _meterFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _meterFill.fillAmount = 1.0f;

            // 4. 辅助遥测 (T-NODE 与 BURN TIME)
            _tNodeLabel = UIFactory.CreateText(
                transform,
                "TNode_Label",
                I18n.Tr("WIDGET_NAV_TNODE", "节点倒计时"),
                Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tnLblRt = _tNodeLabel.rectTransform;
            tnLblRt.pivot = new Vector2(0f, 0.5f);
            tnLblRt.anchorMin = tnLblRt.anchorMax = new Vector2(0.5f, 0.5f);
            tnLblRt.sizeDelta = new Vector2(48f * s, 14f * s);
            tnLblRt.anchoredPosition = new Vector2(-92f * s, -11f * s);

            _tNodeValueText = UIFactory.CreateText(
                transform,
                "TNode_Value",
                "--:--",
                Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform tnValRt = _tNodeValueText.rectTransform;
            tnValRt.pivot = new Vector2(0f, 0.5f);
            tnValRt.anchorMin = tnValRt.anchorMax = new Vector2(0.5f, 0.5f);
            tnValRt.sizeDelta = new Vector2(50f * s, 14f * s);
            tnValRt.anchoredPosition = new Vector2(-44f * s, -11f * s);

            _burnTimeLabel = UIFactory.CreateText(
                transform,
                "BurnTime_Label",
                I18n.Tr("WIDGET_NAV_BURN_TIME", "燃烧时长"),
                Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform btLblRt = _burnTimeLabel.rectTransform;
            btLblRt.pivot = new Vector2(0f, 0.5f);
            btLblRt.anchorMin = btLblRt.anchorMax = new Vector2(0.5f, 0.5f);
            btLblRt.sizeDelta = new Vector2(48f * s, 14f * s);
            btLblRt.anchoredPosition = new Vector2(8f * s, -11f * s);

            _burnTimeValueText = UIFactory.CreateText(
                transform,
                "BurnTime_Value",
                "--:--",
                Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform btValRt = _burnTimeValueText.rectTransform;
            btValRt.pivot = new Vector2(0f, 0.5f);
            btValRt.anchorMin = btValRt.anchorMax = new Vector2(0.5f, 0.5f);
            btValRt.sizeDelta = new Vector2(50f * s, 14f * s);
            btValRt.anchoredPosition = new Vector2(56f * s, -11f * s);

            // 5. 底部操作栏
            _burnInText = UIFactory.CreateText(
                transform,
                "BurnIn_Status",
                I18n.Tr("WIDGET_NAV_BURN_IN_PLACEHOLDER", "点火 --:--"),
                Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform biRt = _burnInText.rectTransform;
            biRt.pivot = new Vector2(0f, 0.5f);
            biRt.anchorMin = biRt.anchorMax = new Vector2(0.5f, 0.5f);
            biRt.sizeDelta = new Vector2(100f * s, 18f * s);
            biRt.anchoredPosition = new Vector2(-92f * s, -33f * s);

            Vector2 warpBtnSize = new Vector2(36f * s, 16f * s);
            _btnWarp = UIFactory.CreateButton(transform, "Btn_Warp", warpBtnSize, new Vector2(34f * s, -40f * s), OnWarpClicked);
            _btnWarpImg = _btnWarp.GetComponent<Image>();
            _btnWarpText = UIFactory.CreateText(_btnWarp.transform, "Text", I18n.Tr("WIDGET_MANEUVER_WARP", "跃迁"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnWarpText.rectTransform.sizeDelta = warpBtnSize;
            _btnWarpText.rectTransform.anchoredPosition = Vector2.zero;

            Vector2 delBtnSize = new Vector2(30f * s, 16f * s);
            _btnDismiss = UIFactory.CreateButton(transform, "Btn_Del", delBtnSize, new Vector2(74f * s, -40f * s), OnDismissClicked);
            _btnDismissImg = _btnDismiss.GetComponent<Image>();
            _btnDismissText = UIFactory.CreateText(_btnDismiss.transform, "Text", I18n.Tr("WIDGET_MANEUVER_DEL", "删除"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnDismissText.rectTransform.sizeDelta = delBtnSize;
            _btnDismissText.rectTransform.anchoredPosition = Vector2.zero;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            ApplyText(_headerTitleText, TextStyleRole.Label, theme);
            ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_deltaVValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);

            ApplyMeter(_meterTrack, _meterFill, null, MeterStyleRole.Primary, theme);

            ApplyText(_tNodeLabel, TextStyleRole.Label, theme);
            ApplyText(_tNodeValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_burnTimeLabel, TextStyleRole.Label, theme);
            ApplyText(_burnTimeValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_burnInText, TextStyleRole.SecondaryValue, theme);

            if (_btnWarp != null) ApplyButton(_btnWarp, _btnWarpImg, _btnWarpText, ButtonVisualRole.Normal, false, theme);
            if (_btnDismiss != null) ApplyButton(_btnDismiss, _btnDismissImg, _btnDismissText, ButtonVisualRole.Normal, false, theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            var theme = WidgetStyleManager.Instance?.CurrentTheme;

            if (_lastHasNode.Update(state.HasNode))
            {
                if (!state.HasNode)
                {
                    _deltaVValueText.text = "---";
                    _statusBadgeText.text = I18n.Tr("WIDGET_NAV_NO_NODE", "无节点");
                    _tNodeValueText.text = "--:--";
                    _burnTimeValueText.text = "--:--";
                    _burnInText.text = I18n.Tr("WIDGET_NAV_AWAITING_PLAN", "等待机动飞行计划");
                    _meterFill.fillAmount = 0f;
                    if (_btnWarp != null) _btnWarp.interactable = false;
                    if (_btnDismiss != null) _btnDismiss.interactable = false;
                    ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
                    return;
                }
                else
                {
                    if (_btnWarp != null) _btnWarp.interactable = true;
                    if (_btnDismiss != null) _btnDismiss.interactable = true;
                }
            }

            if (!state.HasNode) return;

            // 遥测读数与进度
            if (_lastDeltaV.Update(state.DeltaV))
            {
                _deltaVValueText.text = state.FormattedDeltaV;
                _unitText.text = state.UnitLabel;
                _meterFill.fillAmount = state.MeterFraction;
            }

            if (_lastTNodeStr.Update(state.FormattedTNode))
            {
                _tNodeValueText.text = state.FormattedTNode;
            }

            if (_lastBurnTimeStr.Update(state.FormattedBurnTime))
            {
                _burnTimeValueText.text = state.FormattedBurnTime;
            }

            if (_lastBurnInStr.Update(state.FormattedBurnIn))
            {
                _burnInText.text = state.FormattedBurnIn;
            }

            if (_lastBadgeStr.Update(state.BadgeText))
            {
                _statusBadgeText.text = state.BadgeText;
            }

            if (_lastCardRole.Update(state.TargetCardRole))
            {
                ApplyCard(_bgImage, _bgOutline, state.TargetCardRole, theme);
            }
        }

        private void OnWarpClicked()
        {
            FlightTelemetryContext.Current?.WarpToManeuverNode();
        }

        private void OnDismissClicked()
        {
            FlightTelemetryContext.Current?.DeleteManeuverNode();
        }

        protected override void OnDestroy()
        {
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
