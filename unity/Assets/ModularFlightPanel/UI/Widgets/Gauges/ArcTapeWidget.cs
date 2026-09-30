using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Gauges
{
    /// <summary>
    /// PFD 弧形标尺带组件状态快照 (0 GC struct)
    /// </summary>
    public struct ArcTapeState : IEquatable<ArcTapeState>
    {
        public bool HasVessel;
        public double RawVal;
        public double DisplayVal;
        public ArcTapeWidget.DynamicUnitTier CurrentTier;
        public string ActiveUnitStr;
        public float ActiveStep;
        public double ActiveScale;
        public string TopText;
        public string BottomText;
        public int AlertLevel;
        public string BadgePrimary;
        public string BadgeSecondary;
        public float TargetAngle;
        public bool IsDeadband;
        public bool TrendRatePositive;
        public double Agl;

        public bool Equals(ArcTapeState other)
        {
            return HasVessel == other.HasVessel &&
                   Math.Abs(RawVal - other.RawVal) < 0.05 &&
                   Math.Abs(DisplayVal - other.DisplayVal) < 0.05 &&
                   CurrentTier == other.CurrentTier &&
                   ActiveUnitStr == other.ActiveUnitStr &&
                   Math.Abs(ActiveStep - other.ActiveStep) < 0.01f &&
                   TopText == other.TopText &&
                   BottomText == other.BottomText &&
                   AlertLevel == other.AlertLevel &&
                   BadgePrimary == other.BadgePrimary &&
                   BadgeSecondary == other.BadgeSecondary &&
                   Math.Abs(TargetAngle - other.TargetAngle) < 0.05f &&
                   IsDeadband == other.IsDeadband &&
                   TrendRatePositive == other.TrendRatePositive &&
                   Math.Abs(Agl - other.Agl) < 0.1;
        }

        public override bool Equals(object obj) => obj is ArcTapeState other && Equals(other);
        public override int GetHashCode() => (RawVal, DisplayVal, AlertLevel).GetHashCode();
    }

    /// <summary>
    /// 弧形标尺带纯 C# 业务解耦中枢大脑 (MFP-SPEC-012)
    /// </summary>
    public class ArcTapeLogic : WidgetLogic<ArcTapeState>
    {
        public bool IsSpeedTape = true;
        public string ValueToken = "{SPD}";
        public string TopModeTemplate = "SPD";
        public string BottomSecTemplate = "{MACH}";
        public string TrendToken = "{DV_DT}";
        public string AccToken = "{ACC}";
        public string TerrainToken = "{ALT:AGL}";
        public float BaseStep = 10f;
        public float TrendMaxScale = 20f;
        public float AngularSpan = 80f;

        private ArcTapeWidget.DynamicUnitTier _currentTier = ArcTapeWidget.DynamicUnitTier.Base;
        private string _activeUnitStr = "m/s";
        private float _activeStep = 10f;
        private double _activeScale = 1.0;
        private double _filteredTrendRate = 0.0;
        private const double SPEED_OF_LIGHT = 299792458.0;

        public override void Reset()
        {
            CurrentState = default;
            _currentTier = ArcTapeWidget.DynamicUnitTier.Base;
            _activeUnitStr = IsSpeedTape ? "m/s" : "m";
            _activeStep = BaseStep > 0f ? BaseStep : (IsSpeedTape ? 10f : 100f);
            _activeScale = 1.0;
            _filteredTrendRate = 0.0;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    CurrentState = new ArcTapeState { HasVessel = false };
                }
                return;
            }

            // 1. 数值双精度求值 (速度或高度)
            double rawVal = BaseFlightWidget.EvalNumeric(ValueToken, telemetry);
            if (double.IsNaN(rawVal)) rawVal = 0.0;
            if (IsSpeedTape && rawVal < 0.0) rawVal = 0.0;

            // 2. 动态工程量纲换算 (m/s, km/s, c 或 m, km, Mm, Gm)
            UpdateDynamicUnits(rawVal);
            double displayVal = rawVal * _activeScale;

            // 3. 模式标签与次级信息窗求值
            string topText = BaseFlightWidget.EvalToken(TopModeTemplate, telemetry);
            string bottomText = BaseFlightWidget.EvalToken(BottomSecTemplate, telemetry);

            // 4. 同心伴随弧轨动力学系统求值 (ACC、dV/dt 或 VSI)
            double rawRate = BaseFlightWidget.EvalNumeric(TrendToken, telemetry);
            if (double.IsNaN(rawRate)) rawRate = 0.0;

            if (Math.Abs(rawRate - _filteredTrendRate) > 0.08)
            {
                _filteredTrendRate = rawRate;
            }
            else
            {
                rawRate = _filteredTrendRate;
            }

            bool trendRatePositive = rawRate >= 0;
            int alertLevel = 0;
            string badgePrimary;
            string badgeSecondary = string.Empty;

            if (IsSpeedTape)
            {
                double gForce = BaseFlightWidget.EvalNumeric(AccToken, telemetry);
                if (double.IsNaN(gForce)) gForce = 1.0;

                if (gForce >= 8.0) alertLevel = 2;
                else if (gForce >= 4.0) alertLevel = 1;

                badgePrimary = UIFactory.FormatTabular($"ACC {gForce:F1}G");
                string rateSign = rawRate > 0 ? "+" : "";
                badgeSecondary = Math.Abs(rawRate) < 0.05 ? "dV 0.0" : UIFactory.FormatTabular($"dV {rateSign}{rawRate:F1}");
            }
            else
            {
                if (rawRate < -25.0) alertLevel = 2;
                else if (rawRate < -15.0) alertLevel = 1;

                string vsiSign = rawRate > 0 ? "+" : "";
                string vsiStr;
                if (Math.Abs(rawRate) < 0.05)
                    vsiStr = "V/S 0.0";
                else if (Math.Abs(rawRate) >= 1000.0)
                    vsiStr = $"{vsiSign}{rawRate / 1000.0:F1}k m/s";
                else
                    vsiStr = $"{vsiSign}{rawRate:F1} m/s";

                badgePrimary = UIFactory.FormatTabular(vsiStr);
            }

            float halfSpan = AngularSpan * 0.5f;
            float maxScale = TrendMaxScale > 0.1f ? TrendMaxScale : 20.0f;
            float rateFraction = Mathf.Clamp((float)(rawRate / maxScale), -1f, 1f);
            float pendingTargetAngle = rateFraction * (halfSpan * 0.82f);
            bool pendingIsDeadband = Math.Abs(rawRate) < 0.05;

            // 5. 若为高度带，计算地面防撞警戒
            double agl = double.NaN;
            if (!IsSpeedTape)
            {
                agl = BaseFlightWidget.EvalNumeric(TerrainToken, telemetry);
                if (double.IsNaN(agl)) agl = rawVal;
            }

            CurrentState = new ArcTapeState
            {
                HasVessel = true,
                RawVal = rawVal,
                DisplayVal = displayVal,
                CurrentTier = _currentTier,
                ActiveUnitStr = _activeUnitStr,
                ActiveStep = _activeStep,
                ActiveScale = _activeScale,
                TopText = topText,
                BottomText = bottomText,
                AlertLevel = alertLevel,
                BadgePrimary = badgePrimary,
                BadgeSecondary = badgeSecondary,
                TargetAngle = pendingTargetAngle,
                IsDeadband = pendingIsDeadband,
                TrendRatePositive = trendRatePositive,
                Agl = agl
            };
        }

        private void UpdateDynamicUnits(double rawVal)
        {
            if (IsSpeedTape)
            {
                switch (_currentTier)
                {
                    case ArcTapeWidget.DynamicUnitTier.Base:
                        if (rawVal >= 1000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Kilo;
                            _activeUnitStr = "km/s";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else
                        {
                            _activeUnitStr = "m/s";
                            _activeScale = 1.0;
                            _activeStep = BaseStep > 0f ? BaseStep : 10f;
                        }
                        break;

                    case ArcTapeWidget.DynamicUnitTier.Kilo:
                        if (rawVal < 850.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Base;
                            _activeUnitStr = "m/s";
                            _activeScale = 1.0;
                            _activeStep = BaseStep > 0f ? BaseStep : 10f;
                        }
                        else if (rawVal >= 1000000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Mega;
                            _activeUnitStr = "c";
                            _activeScale = 1.0 / SPEED_OF_LIGHT;
                            _activeStep = 0.01f;
                        }
                        else
                        {
                            _activeUnitStr = "km/s";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        break;

                    case ArcTapeWidget.DynamicUnitTier.Mega:
                        if (rawVal < 850000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Kilo;
                            _activeUnitStr = "km/s";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else
                        {
                            _activeUnitStr = "c";
                            _activeScale = 1.0 / SPEED_OF_LIGHT;
                            _activeStep = 0.01f;
                        }
                        break;
                }
            }
            else
            {
                switch (_currentTier)
                {
                    case ArcTapeWidget.DynamicUnitTier.Base:
                        if (rawVal >= 1000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Kilo;
                            _activeUnitStr = "km";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else
                        {
                            _activeUnitStr = "m";
                            _activeScale = 1.0;
                            _activeStep = BaseStep > 0f ? BaseStep : 100f;
                        }
                        break;

                    case ArcTapeWidget.DynamicUnitTier.Kilo:
                        if (rawVal < 850.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Base;
                            _activeUnitStr = "m";
                            _activeScale = 1.0;
                            _activeStep = BaseStep > 0f ? BaseStep : 100f;
                        }
                        else if (rawVal >= 1000000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Mega;
                            _activeUnitStr = "Mm";
                            _activeScale = 0.000001;
                            _activeStep = 0.5f;
                        }
                        else
                        {
                            _activeUnitStr = "km";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        break;

                    case ArcTapeWidget.DynamicUnitTier.Mega:
                        if (rawVal < 850000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Kilo;
                            _activeUnitStr = "km";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else if (rawVal >= 1000000000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Giga;
                            _activeUnitStr = "Gm";
                            _activeScale = 0.000000001;
                            _activeStep = 0.1f;
                        }
                        else
                        {
                            _activeUnitStr = "Mm";
                            _activeScale = 0.000001;
                            _activeStep = 0.5f;
                        }
                        break;

                    case ArcTapeWidget.DynamicUnitTier.Giga:
                        if (rawVal < 850000000.0)
                        {
                            _currentTier = ArcTapeWidget.DynamicUnitTier.Mega;
                            _activeUnitStr = "Mm";
                            _activeScale = 0.000001;
                            _activeStep = 0.5f;
                        }
                        else
                        {
                            _activeUnitStr = "Gm";
                            _activeScale = 0.000000001;
                            _activeStep = 0.1f;
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// PFD 弧形标尺带组件套件 (Arc Tape Gauge Widget - Speed & Altitude All-in-One)
    /// </summary>
    [FlightWidget("arc_tape", "arc_speed_tape", "arc_altitude_tape", "arc_alt_tape", "curved_tape", "curved_speed_tape", "curved_altitude_tape", "curved_alt_tape", Category = WidgetCategory.Gauges, DisplayName = "HUD 弧形滚动标尺带", Description = "次世代 HUD / 玻璃座舱弧形标尺带，支持可调曲率、半径与垂直升降率/动压指示。", DefaultWidgetId = "custom.arc_speed_tape", DefaultX = -235f, DefaultY = 0f, HighFrequency = true, ExactIds = new[] { "custom.arc_speed_tape", "core.arc_speed_tape", "custom.arc_altitude_tape", "core.arc_altitude_tape" })]
    public class ArcTapeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(198f, 346f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private readonly ArcTapeLogic _logic = new ArcTapeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private readonly Cached<string> _lastTopText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBottomText = new Cached<string>(string.Empty);

        // 声明式微控件
        public TextWidget ModeTag = TextWidget.Title("SPD");
        public TextWidget CenterValue = TextWidget.Value("{SPD}");

        private const int TICK_POOL_SIZE = 28;
        private const int ARC_SEGMENT_COUNT = 24;
        private const int ESCORT_SEGMENT_COUNT = 20;

        public enum DynamicUnitTier
        {
            Base = 0,    // m/s 或 m
            Kilo = 1,    // km/s 或 km
            Mega = 2,    // c 或 Mm
            Giga = 3     // Gm (高度极值)
        }

        // ==================== 弧形几何参数 ====================
        private float _curvature = 0.5f;
        private float _baseRadius = 200f;
        private float _angularSpan = 80f;
        private bool _isLeftOrientation = true;
        private bool _isSpeedTape = true;

        // ==================== 视图层节点 ====================
        private GameObject _arcBandRoot;
        private readonly List<Image> _bandBgImages = new List<Image>();
        private readonly List<Image> _bandRimImages = new List<Image>();

        private struct ArcTickItem
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
        }
        private readonly List<ArcTickItem> _tickPool = new List<ArcTickItem>(TICK_POOL_SIZE);

        private GameObject _centerBoxObj;
        private RectTransform _centerBoxRt;
        private Image _centerBoxBg;
        private Outline _centerBoxOutline;
        private Text _centerValueText;
        private Text _centerUnitText;
        private Button _centerBoxBtn;
        private Image _pointerArrow;
        private RectTransform _pointerArrowRt;

        private GameObject _modeTagObj;
        private Image _modeTagBg;
        private Outline _modeTagOutline;
        private Text _modeTagText;

        private GameObject _bottomSecObj;
        private Image _bottomSecBg;
        private Outline _bottomSecOutline;
        private Text _bottomSecText;

        private GameObject _escortRailRoot;
        private readonly List<Image> _escortTrackImages = new List<Image>();
        private readonly List<Image> _escortRibbonSegments = new List<Image>();
        private GameObject _escortDatumTick;
        private Image _escortDatumImg;

        private GameObject _escortPointerObj;
        private RectTransform _escortPointerRt;
        private Image _escortPointerLine;
        private Image _escortPointerHead;

        private GameObject _escortBadgeObj;
        private Image _escortBadgeBg;
        private Outline _escortBadgeOutline;
        private Text _escortBadgeTextPrimary;
        private Text _escortBadgeTextSecondary;

        private bool _showingIntegerReadout = false;
        private float _trendAngle = 0f;

        public static Action OnCycleSpeedModeAction;
        public static Action OnCycleAltitudeModeAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            ApplyCanvasIsolation(true);

            bool isLeft = config != null && config.IsLeftOrientation;
            string numToken = config?.NumericToken ?? "";
            string widgetId = config?.WidgetId ?? "";

            if (numToken.Contains("ALT") || widgetId.Contains("alt") || widgetId.Contains("altitude"))
            {
                _isSpeedTape = false;
                _isLeftOrientation = isLeft;
            }
            else if (numToken.Contains("SPD") || widgetId.Contains("speed"))
            {
                _isSpeedTape = true;
                _isLeftOrientation = true;
            }
            else
            {
                _isSpeedTape = isLeft;
                _isLeftOrientation = isLeft;
            }

            // 初始化默认通道
            string valueToken;
            string topModeTemplate;
            string bottomSecTemplate;
            string trendToken;
            string accToken;
            string terrainToken = "{ALT:AGL}";
            float baseStep;
            float trendMaxScale;

            if (_isSpeedTape)
            {
                valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{SPD}";
                topModeTemplate = "{SPD:MODE}";
                bottomSecTemplate = "{MACH}";
                trendToken = "{DV_DT}";
                accToken = "{ACC}";
                baseStep = config != null && config.StepInterval > 0.01f ? config.StepInterval : 10f;
                trendMaxScale = 20.0f;
            }
            else
            {
                valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{ALT}";
                topModeTemplate = "{ALT:MODE}";
                bottomSecTemplate = "{ALT:AGL:DIST}";
                trendToken = "{VSI}";
                accToken = "{ACC}";
                terrainToken = "{ALT:AGL}";
                baseStep = config != null && config.StepInterval > 0.01f ? config.StepInterval : 100f;
                trendMaxScale = 100.0f;
            }

            float cVal = GetTemplateChannelFloat(new[] { "CURVATURE", "CURVE" }, -1f);
            if (cVal > 0f) _curvature = Mathf.Clamp(cVal, 0.05f, 1.0f);
            float rVal = GetTemplateChannelFloat(new[] { "RADIUS", "R" }, -1f);
            if (rVal > 0f) _baseRadius = Mathf.Clamp(rVal, 80f, 600f);
            float spanVal = GetTemplateChannelFloat(new[] { "SPAN", "ANGLE" }, -1f);
            if (spanVal > 0f) _angularSpan = Mathf.Clamp(spanVal, 40f, 120f);
            string side = GetTemplateChannel("SIDE", null);
            if (!string.IsNullOrEmpty(side)) _isLeftOrientation = !side.Equals("RIGHT", StringComparison.OrdinalIgnoreCase);
            string typeVal = GetTemplateChannel(new[] { "TYPE", "MODE_TYPE" }, null);
            if (!string.IsNullOrEmpty(typeVal)) _isSpeedTape = typeVal.Equals("SPEED", StringComparison.OrdinalIgnoreCase) || typeVal.Equals("SPD", StringComparison.OrdinalIgnoreCase);

            valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, valueToken);
            topModeTemplate = GetTemplateChannel(new[] { "TOP", "MODE" }, topModeTemplate);
            bottomSecTemplate = GetTemplateChannel(new[] { "BOTTOM", "SEC" }, bottomSecTemplate);
            trendToken = GetTemplateChannel(new[] { "TREND", "VSI", "RATE", "DV" }, trendToken);
            accToken = GetTemplateChannel("ACC", accToken);
            terrainToken = GetTemplateChannel(new[] { "TERRAIN", "AGL" }, terrainToken);
            trendMaxScale = GetTemplateChannelFloat("TREND_MAX", trendMaxScale);

            _logic.IsSpeedTape = _isSpeedTape;
            _logic.ValueToken = valueToken;
            _logic.TopModeTemplate = topModeTemplate;
            _logic.BottomSecTemplate = bottomSecTemplate;
            _logic.TrendToken = trendToken;
            _logic.AccToken = accToken;
            _logic.TerrainToken = terrainToken;
            _logic.BaseStep = baseStep;
            _logic.TrendMaxScale = trendMaxScale;
            _logic.AngularSpan = _angularSpan;

            if (_baseRadius <= 80f || _baseRadius >= 599f)
            {
                _baseRadius = Mathf.Lerp(420f, 140f, Mathf.Clamp01(_curvature));
            }

            float s = CurrentDpiScale;
            float r = _baseRadius * s;

            float halfSpanRad = (_angularSpan * 0.5f) * Mathf.Deg2Rad;
            float totalH = 2f * (r + 26f * s) * Mathf.Sin(halfSpanRad) + 56f * s;
            float totalW = (r + 26f * s) * (1f - Mathf.Cos(halfSpanRad)) + 145f * s;
            RectTransform.sizeDelta = new Vector2(totalW, totalH);

            BuildArcBand(r, s, theme);
            BuildTickPool(r, s, theme);
            BuildCenterReadoutBox(r, s, theme);
            BuildTopModeCapsule(r, s, theme);
            BuildBottomSecondaryBox(r, s, theme);
            BuildConcentricEscortSystem(r, s, theme);

            if (_arcBandRoot != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "arc_band", "弧形标尺基板", _arcBandRoot);
            }
            if (_centerBoxObj != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "center_readout", "中央实体读数窗", _centerBoxObj);
            }
            if (_modeTagObj != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "top_mode_capsule", "顶部模式微胶囊", _modeTagObj);
            }
            if (_bottomSecObj != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "bottom_sec_box", "底部次级航电窗", _bottomSecObj);
            }
            if (_escortRailRoot != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "escort_rail_system", "同心伴随弧轨系统", _escortRailRoot);
            }

            ApplyTheme(theme);
        }

        private void BuildArcBand(float radius, float s, ThemeConfig theme)
        {
            _arcBandRoot = CreateContainer("Arc_Band_Root", transform).gameObject;

            _bandBgImages.Clear();
            _bandRimImages.Clear();

            Color bandCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);

            float halfSpan = _angularSpan * 0.5f;
            float step = _angularSpan / ARC_SEGMENT_COUNT;
            float arcSegW = ((2f * Mathf.PI * radius * (_angularSpan / 360f)) / ARC_SEGMENT_COUNT) + 2f * s;
            float bandThickness = 28f * s;

            for (int i = 0; i <= ARC_SEGMENT_COUNT; i++)
            {
                float ang = -halfSpan + (i * step);
                Vector2 pt = EvalArcPoint(radius, ang);
                float rotAngle = _isLeftOrientation ? -ang : ang;

                GameObject plate = UIFactory.CreatePanel(_arcBandRoot.transform, $"ArcPlate_{i}",
                    new Vector2(arcSegW, bandThickness), pt, bandCol);
                plate.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _bandBgImages.Add(plate.GetComponent<Image>());

                float rimOffset = 14f * s;
                Vector2 outerPt = EvalArcPoint(radius + rimOffset, ang);
                GameObject outerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"OuterRim_{i}",
                    new Vector2(arcSegW, 1.2f * s), outerPt, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
                outerRim.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _bandRimImages.Add(outerRim.GetComponent<Image>());

                Vector2 innerPt = EvalArcPoint(radius - rimOffset, ang);
                GameObject innerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"InnerRim_{i}",
                    new Vector2(arcSegW, 1.0f * s), innerPt, WidgetStyleManager.Weighted(borderCol, LineWeight.Subtle));
                innerRim.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _bandRimImages.Add(innerRim.GetComponent<Image>());
            }
        }

        private Vector2 EvalArcPoint(float radius, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            if (_isLeftOrientation)
            {
                float x = radius * (1f - cos);
                float y = radius * sin;
                return new Vector2(x, y);
            }
            else
            {
                float x = -radius * (1f - cos);
                float y = radius * sin;
                return new Vector2(x, y);
            }
        }

        private void BuildTickPool(float radius, float s, ThemeConfig theme)
        {
            _tickPool.Clear();
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color textCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color tickCol = WidgetStyleManager.Weighted(style.GetCardBorderColor(CardStyleRole.Normal, theme), LineWeight.Normal);

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                RectTransform rt = CreateContainer($"ArcTick_{i}", transform);
                GameObject root = rt.gameObject;

                float lineW = 9f * s;
                float lineH = 1.2f * s;
                GameObject lineObj = UIFactory.CreatePanel(root.transform, "Line", new Vector2(lineW, lineH), Vector2.zero, tickCol);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                Image lineImg = lineObj.GetComponent<Image>();

                float labelX = _isLeftOrientation ? (-18f * s) : (18f * s);
                Text label = UIFactory.CreateText(root.transform, "Label", "0", Mathf.RoundToInt(9.5f * s),
                    _isLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, textCol);
                label.fontStyle = FontStyle.Bold;
                label.alignByGeometry = false;
                RectTransform labelRt = label.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(36f * s, 16f * s);
                labelRt.anchoredPosition = new Vector2(labelX, 0f);

                _tickPool.Add(new ArcTickItem
                {
                    Root = root,
                    Rect = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = label,
                    LabelRt = labelRt
                });
            }
        }

        private void BuildCenterReadoutBox(float radius, float s, ThemeConfig theme)
        {
            float boxW = 86f * s;
            float boxH = 26f * s;
            float offsetX = _isLeftOrientation ? (-boxW * 0.44f) : (boxW * 0.44f);
            Vector2 boxPos = new Vector2(offsetX, 0f);

            _centerBoxObj = UIFactory.CreatePanel(transform, "Center_Readout_Box", new Vector2(boxW, boxH), boxPos, theme.FrameBgColor);
            _centerBoxRt = _centerBoxObj.GetComponent<RectTransform>();
            _centerBoxBg = _centerBoxObj.GetComponent<Image>();
            _centerBoxOutline = _centerBoxObj.AddComponent<Outline>();
            _centerBoxOutline.effectColor = theme.FrameBorderColor.ToColor();
            _centerBoxOutline.effectDistance = new Vector2(1.2f * s, -1.2f * s);

            float arrowW = 9f * s;
            float arrowH = 14f * s;
            float arrowX = _isLeftOrientation ? (boxW * 0.5f + arrowW * 0.4f) : (-boxW * 0.5f - arrowW * 0.4f);

            GameObject arrowObj = UIFactory.CreatePanel(_centerBoxObj.transform, "PointerArrow",
                new Vector2(arrowW, arrowH), new Vector2(arrowX, 0f), WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Accent, theme));
            _pointerArrow = arrowObj.GetComponent<Image>();
            _pointerArrowRt = arrowObj.GetComponent<RectTransform>();
            _pointerArrowRt.localEulerAngles = new Vector3(0f, 0f, _isLeftOrientation ? -90f : 90f);

            _centerValueText = UIFactory.CreateText(_centerBoxObj.transform, "CenterValue", "0.0",
                Mathf.RoundToInt(15f * s), TextAnchor.MiddleRight, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _centerValueText.fontStyle = FontStyle.Bold;
            _centerValueText.alignByGeometry = false;
            RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0f, 0f);
            valRt.anchorMax = new Vector2(0.72f, 1f);
            valRt.offsetMin = new Vector2(4f * s, 0f);
            valRt.offsetMax = new Vector2(-2f * s, 0f);

            _centerUnitText = UIFactory.CreateText(_centerBoxObj.transform, "CenterUnit", "m/s",
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, theme));
            _centerUnitText.fontStyle = FontStyle.Normal;
            _centerUnitText.alignByGeometry = false;
            RectTransform unitRt = _centerUnitText.GetComponent<RectTransform>();
            unitRt.anchorMin = new Vector2(0.72f, 0f);
            unitRt.anchorMax = new Vector2(1f, 1f);
            unitRt.offsetMin = new Vector2(2f * s, 0f);
            unitRt.offsetMax = new Vector2(-4f * s, 0f);

            _centerBoxBtn = _centerBoxObj.AddComponent<Button>();
            _centerBoxBtn.transition = Selectable.Transition.ColorTint;
            _centerBoxBtn.targetGraphic = _centerBoxBg;
            _centerBoxBtn.onClick.AddListener(OnCenterBoxClicked);
        }

        private void BuildTopModeCapsule(float radius, float s, ThemeConfig theme)
        {
            float halfSpan = _angularSpan * 0.5f;
            Vector2 topArcPt = EvalArcPoint(radius, halfSpan);

            float capsuleW = 54f * s;
            float capsuleH = 17f * s;
            float posX = _isLeftOrientation ? (topArcPt.x - capsuleW * 0.40f) : (topArcPt.x + capsuleW * 0.40f);
            float posY = topArcPt.y + capsuleH * 0.70f;

            _modeTagObj = UIFactory.CreatePanel(transform, "Top_Mode_Capsule",
                new Vector2(capsuleW, capsuleH), new Vector2(posX, posY), theme.FrameBgColor);
            _modeTagBg = _modeTagObj.GetComponent<Image>();
            _modeTagOutline = _modeTagObj.AddComponent<Outline>();
            _modeTagOutline.effectColor = theme.FrameBorderColor.ToColor();
            _modeTagOutline.effectDistance = new Vector2(1f * s, -1f * s);

            _modeTagText = UIFactory.CreateText(_modeTagObj.transform, "Mode_Text", _isSpeedTape ? I18n.Tr("WIDGET_GAUGE_SURF", "表面") : "ALT",
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, theme));
            _modeTagText.fontStyle = FontStyle.Bold;
            _modeTagText.alignByGeometry = false;
            RectTransform txtRt = _modeTagText.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            Button btn = _modeTagObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = _modeTagBg;
            btn.onClick.AddListener(OnModeTagClicked);
        }

        private void BuildBottomSecondaryBox(float radius, float s, ThemeConfig theme)
        {
            float halfSpan = _angularSpan * 0.5f;
            Vector2 btmArcPt = EvalArcPoint(radius, -halfSpan);

            float boxW = 68f * s;
            float boxH = 18f * s;
            float posX = _isLeftOrientation ? (btmArcPt.x - boxW * 0.35f) : (btmArcPt.x + boxW * 0.35f);
            float posY = btmArcPt.y - boxH * 0.70f;

            _bottomSecObj = UIFactory.CreatePanel(transform, "Bottom_Secondary_Box",
                new Vector2(boxW, boxH), new Vector2(posX, posY), theme.FrameBgColor);
            _bottomSecBg = _bottomSecObj.GetComponent<Image>();
            _bottomSecOutline = _bottomSecObj.AddComponent<Outline>();
            _bottomSecOutline.effectColor = theme.FrameBorderColor.ToColor();
            _bottomSecOutline.effectDistance = new Vector2(1f * s, -1f * s);

            _bottomSecText = UIFactory.CreateText(_bottomSecObj.transform, "Text", _isSpeedTape ? "M 0.00" : "RDR ---",
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _bottomSecText.fontStyle = FontStyle.Bold;
            _bottomSecText.alignByGeometry = false;
            RectTransform txtRt = _bottomSecText.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
        }

        private void BuildConcentricEscortSystem(float radius, float s, ThemeConfig theme)
        {
            _escortRailRoot = CreateContainer("Concentric_Escort_Rail_Root", transform).gameObject;

            _escortTrackImages.Clear();
            _escortRibbonSegments.Clear();

            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);
            Color trackCol = WidgetStyleManager.WithAlpha(borderCol, 0.22f);
            Color ribbonCol = WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Primary, theme);

            float escortRadius = radius + 20f * s;
            float halfSpan = _angularSpan * 0.5f;
            float step = _angularSpan / ESCORT_SEGMENT_COUNT;
            float segW = ((2f * Mathf.PI * escortRadius * (_angularSpan / 360f)) / ESCORT_SEGMENT_COUNT) + 1.5f * s;
            float railThick = 3.5f * s;

            for (int i = 0; i <= ESCORT_SEGMENT_COUNT; i++)
            {
                float ang = -halfSpan + (i * step);
                Vector2 pt = EvalArcPoint(escortRadius, ang);
                float rotAngle = _isLeftOrientation ? -ang : ang;

                GameObject trk = UIFactory.CreatePanel(_escortRailRoot.transform, $"EscortTrack_{i}",
                    new Vector2(segW, 1.2f * s), pt, trackCol);
                trk.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _escortTrackImages.Add(trk.GetComponent<Image>());

                GameObject rib = UIFactory.CreatePanel(_escortRailRoot.transform, $"EscortRibbon_{i}",
                    new Vector2(segW, railThick), pt, ribbonCol);
                rib.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                Image ribImg = rib.GetComponent<Image>();
                rib.SetActive(false);
                _escortRibbonSegments.Add(ribImg);
            }

            Vector2 datumPt = EvalArcPoint(escortRadius, 0f);
            _escortDatumTick = UIFactory.CreatePanel(_escortRailRoot.transform, "EscortDatumTick",
                new Vector2(7f * s, 1.8f * s), datumPt, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
            _escortDatumImg = _escortDatumTick.GetComponent<Image>();

            float ptrW = 8f * s;
            float ptrH = 8f * s;
            _escortPointerObj = UIFactory.CreatePanel(_escortRailRoot.transform, "EscortPointer",
                new Vector2(ptrW, ptrH), datumPt, Color.clear);
            _escortPointerRt = _escortPointerObj.GetComponent<RectTransform>();

            GameObject pHead = UIFactory.CreatePanel(_escortPointerObj.transform, "Head",
                new Vector2(4.5f * s, 4.5f * s), Vector2.zero, ribbonCol);
            pHead.transform.localEulerAngles = new Vector3(0f, 0f, 45f);
            _escortPointerHead = pHead.GetComponent<Image>();

            GameObject pLine = UIFactory.CreatePanel(_escortPointerObj.transform, "TailLine",
                new Vector2(10f * s, 1.6f * s), new Vector2(_isLeftOrientation ? (-6f * s) : (6f * s), 0f), ribbonCol);
            _escortPointerLine = pLine.GetComponent<Image>();
            _escortPointerObj.SetActive(false);

            float bW = 62f * s;
            float bH = 22f * s;
            Vector2 tipPt = EvalArcPoint(escortRadius, _isSpeedTape ? (halfSpan * 0.88f) : (halfSpan * 0.88f));
            float badgeX = _isLeftOrientation ? (tipPt.x - bW * 0.48f) : (tipPt.x + bW * 0.48f);
            float badgeY = tipPt.y + (_isSpeedTape ? (bH * 0.45f) : (bH * 0.45f));

            _escortBadgeObj = UIFactory.CreatePanel(transform, "Escort_Dynamics_Badge",
                new Vector2(bW, bH), new Vector2(badgeX, badgeY), theme.FrameBgColor);
            _escortBadgeBg = _escortBadgeObj.GetComponent<Image>();
            _escortBadgeOutline = _escortBadgeObj.AddComponent<Outline>();
            _escortBadgeOutline.effectColor = theme.FrameBorderColor.ToColor();
            _escortBadgeOutline.effectDistance = new Vector2(1f * s, -1f * s);

            if (_isSpeedTape)
            {
                _escortBadgeTextPrimary = UIFactory.CreateText(_escortBadgeObj.transform, "ACC_Text", "ACC 1.0G",
                    Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _escortBadgeTextPrimary.fontStyle = FontStyle.Bold;
                _escortBadgeTextPrimary.alignByGeometry = false;
                RectTransform accRt = _escortBadgeTextPrimary.GetComponent<RectTransform>();
                accRt.anchorMin = new Vector2(0f, 0.48f);
                accRt.anchorMax = new Vector2(1f, 1f);
                accRt.offsetMin = Vector2.zero;
                accRt.offsetMax = Vector2.zero;

                _escortBadgeTextSecondary = UIFactory.CreateText(_escortBadgeObj.transform, "DV_Text", "dV +0.0",
                    Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Accent, theme));
                _escortBadgeTextSecondary.fontStyle = FontStyle.Normal;
                _escortBadgeTextSecondary.alignByGeometry = false;
                RectTransform dvRt = _escortBadgeTextSecondary.GetComponent<RectTransform>();
                dvRt.anchorMin = new Vector2(0f, 0f);
                dvRt.anchorMax = new Vector2(1f, 0.52f);
                dvRt.offsetMin = Vector2.zero;
                dvRt.offsetMax = Vector2.zero;
            }
            else
            {
                _escortBadgeTextPrimary = UIFactory.CreateText(_escortBadgeObj.transform, "VSI_Text", "V/S 0.0",
                    Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _escortBadgeTextPrimary.fontStyle = FontStyle.Bold;
                _escortBadgeTextPrimary.alignByGeometry = false;
                RectTransform vsiRt = _escortBadgeTextPrimary.GetComponent<RectTransform>();
                vsiRt.anchorMin = Vector2.zero;
                vsiRt.anchorMax = Vector2.one;
                vsiRt.offsetMin = Vector2.zero;
                vsiRt.offsetMax = Vector2.zero;
            }
        }

        private void OnCenterBoxClicked()
        {
            if (_isSpeedTape)
            {
                OnCycleSpeedModeAction?.Invoke();
            }
            else
            {
                OnCycleAltitudeModeAction?.Invoke();
            }
        }

        private void OnModeTagClicked()
        {
            OnCenterBoxClicked();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;

            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color borderCol = style.GetCardBorderColor(CardStyleRole.Normal, theme);
            Color bandCol = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);

            foreach (var bg in _bandBgImages) if (bg != null) bg.color = bandCol;
            foreach (var rim in _bandRimImages) if (rim != null) rim.color = borderCol;

            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.Normal, theme);
            ApplyText(_centerValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_centerUnitText, TextStyleRole.Unit, theme);

            if (_pointerArrow != null) _pointerArrow.color = style.GetMeterColor(MeterStyleRole.Accent, theme);

            ApplyCard(_modeTagBg, _modeTagOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_modeTagText, TextStyleRole.Cardinal, theme);

            ApplyCard(_bottomSecBg, _bottomSecOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_bottomSecText, TextStyleRole.SecondaryValue, theme);

            Color tickCol = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
            Color textCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            foreach (var t in _tickPool)
            {
                if (t.Line != null) t.Line.color = tickCol;
                if (t.Label != null) t.Label.color = textCol;
            }

            foreach (var trk in _escortTrackImages)
            {
                if (trk != null) trk.color = WidgetStyleManager.WithAlpha(borderCol, 0.22f);
            }
            if (_escortDatumImg != null) _escortDatumImg.color = WidgetStyleManager.Weighted(borderCol, LineWeight.Strong);

            ApplyCard(_escortBadgeBg, _escortBadgeOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_escortBadgeTextPrimary, TextStyleRole.PrimaryValue, theme);
            if (_escortBadgeTextSecondary != null) ApplyText(_escortBadgeTextSecondary, TextStyleRole.Accent, theme);
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
            if (!state.HasVessel) return;

            UpdateCenterReadout(state.DisplayVal, state.CurrentTier, state.ActiveUnitStr);
            UpdateArcTicks(state.DisplayVal, state.ActiveStep, state.CurrentTier);

            if (_lastTopText.Update(state.TopText) && _modeTagText != null) _modeTagText.SetTextSafe(state.TopText);
            if (_lastBottomText.Update(state.BottomText) && _bottomSecText != null) _bottomSecText.SetTextSafe(state.BottomText);

            DrawConcentricEscortDynamics(state);

            if (!_isSpeedTape)
            {
                DrawTerrainGroundHighlight(state.Agl);
            }
        }

        private void UpdateCenterReadout(double displayVal, DynamicUnitTier tier, string unitStr)
        {
            string formatted;
            if (tier == DynamicUnitTier.Mega && _isSpeedTape)
            {
                formatted = $"{displayVal:F3}";
            }
            else
            {
                double abs = Math.Abs(displayVal);
                if (_showingIntegerReadout)
                {
                    if (abs < 995.0) _showingIntegerReadout = false;
                }
                else
                {
                    if (abs >= 1000.0) _showingIntegerReadout = true;
                }

                formatted = _showingIntegerReadout ? $"{displayVal:F0}" : $"{displayVal:F1}";
            }

            formatted = UIFactory.FormatTabular(formatted);
            if (_centerValueText != null) _centerValueText.SetTextSafe(formatted);
            if (_centerUnitText != null) _centerUnitText.SetTextSafe(unitStr);
        }

        private void UpdateArcTicks(double displayVal, float activeStep, DynamicUnitTier tier)
        {
            float s = CurrentDpiScale;
            float r = _baseRadius * s;
            float halfSpan = _angularSpan * 0.5f;

            float step = activeStep > 0f ? activeStep : 10f;
            float subStep = step * 0.5f;

            float degPerUnit = (halfSpan * 0.75f) / (step * 2.5f);
            double startTick = Math.Floor((displayVal - (halfSpan / degPerUnit)) / subStep) * subStep;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);
            Color majorCol = WidgetStyleManager.WithAlpha(borderCol, 0.75f);
            Color subCol = WidgetStyleManager.WithAlpha(borderCol, 0.35f);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                ArcTickItem item = _tickPool[i];
                double tickVal = startTick + (i * subStep);

                if (tickVal < 0.0 && _isSpeedTape)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                float ang = (float)(tickVal - displayVal) * degPerUnit;
                if (Mathf.Abs(ang) > halfSpan)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                item.Root.SetActive(true);

                Vector2 pt = EvalArcPoint(r, ang);
                item.Rect.SetAnchoredPositionSafe(pt);
                float rotAngle = _isLeftOrientation ? -ang : ang;
                item.Rect.localEulerAngles = new Vector3(0f, 0f, rotAngle);

                float edgeDist = halfSpan - Mathf.Abs(ang);
                float alphaMul = Mathf.Clamp01(edgeDist / 8f);
                bool isOccludedByBox = Mathf.Abs(ang) < 6.8f;

                double majorRemainder = Math.Abs(tickVal - Math.Round(tickVal / step) * step);
                bool isMajor = majorRemainder < (subStep * 0.25);

                if (isMajor)
                {
                    item.LineRt.sizeDelta = new Vector2(9f * s, 1.2f * s);
                    item.Line.SetColor(WidgetStyleManager.WithAlpha(majorCol, majorCol.a * alphaMul));
                    item.Label.gameObject.SetActive(!isOccludedByBox);

                    if (!isOccludedByBox)
                    {
                        string lbl = (tier == DynamicUnitTier.Mega && _isSpeedTape) ? $"{tickVal:F2}" :
                                     (step < 1.0f ? $"{tickVal:F1}" : $"{tickVal:F0}");
                        item.Label.SetTextSafe(lbl);

                        Color lCol = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);
                        item.Label.SetColor(WidgetStyleManager.WithAlpha(lCol, lCol.a * alphaMul));
                    }
                }
                else
                {
                    item.LineRt.sizeDelta = new Vector2(4.5f * s, 1.0f * s);
                    item.Line.SetColor(WidgetStyleManager.WithAlpha(subCol, subCol.a * alphaMul));
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        private void DrawConcentricEscortDynamics(in ArcTapeState state)
        {
            if (_escortRailRoot == null) return;

            float s = CurrentDpiScale;
            float escortRadius = (_baseRadius + 20f) * s;
            float halfSpan = _angularSpan * 0.5f;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            int alertLevel = state.AlertLevel;
            Color dynamicCol = alertLevel == 2 ? style.GetMeterColor(MeterStyleRole.Danger, theme) :
                               alertLevel == 1 ? style.GetMeterColor(MeterStyleRole.Warning, theme) :
                               (_isSpeedTape ? style.GetMeterColor(MeterStyleRole.Primary, theme) :
                                (state.TrendRatePositive ? style.GetMeterColor(MeterStyleRole.Primary, theme) :
                                                           style.GetMeterColor(MeterStyleRole.Accent, theme)));

            if (_escortBadgeTextPrimary != null) _escortBadgeTextPrimary.SetTextSafe(state.BadgePrimary);
            if (_isSpeedTape && _escortBadgeTextSecondary != null) _escortBadgeTextSecondary.SetTextSafe(state.BadgeSecondary);

            if (_escortBadgeOutline != null)
            {
                Color outlineCol = alertLevel == 2 ? style.GetMeterColor(MeterStyleRole.Danger, theme) :
                                   alertLevel == 1 ? style.GetMeterColor(MeterStyleRole.Warning, theme) :
                                                     theme.FrameBorderColor.ToColor();
                _escortBadgeOutline.SetColor(outlineCol);
            }
            if (_escortBadgeBg != null)
            {
                Color baseBg = theme.FrameBgColor;
                Color targetBg = alertLevel == 2 ? Color.Lerp(baseBg, style.GetMeterColor(MeterStyleRole.Danger, theme), 0.22f) :
                                 alertLevel == 1 ? Color.Lerp(baseBg, style.GetMeterColor(MeterStyleRole.Warning, theme), 0.16f) :
                                                   baseBg;
                _escortBadgeBg.SetColor(targetBg);
            }

            _trendAngle = Mathf.Lerp(_trendAngle, state.TargetAngle, 0.25f);
            bool isDeadband = state.IsDeadband;

            float step = _angularSpan / ESCORT_SEGMENT_COUNT;
            for (int i = 0; i < _escortRibbonSegments.Count; i++)
            {
                Image seg = _escortRibbonSegments[i];
                if (seg == null) continue;

                if (isDeadband)
                {
                    seg.gameObject.SetActive(false);
                    continue;
                }

                float segAng = -halfSpan + (i * step);
                bool inRange = false;
                if (_trendAngle >= 0f)
                {
                    inRange = (segAng >= -step * 0.5f && segAng <= _trendAngle + step * 0.5f);
                }
                else
                {
                    inRange = (segAng <= step * 0.5f && segAng >= _trendAngle - step * 0.5f);
                }

                if (inRange)
                {
                    seg.gameObject.SetActive(true);
                    float distFraction = Mathf.Clamp01(Mathf.Abs(segAng) / (Mathf.Abs(_trendAngle) + 0.1f));
                    float segAlpha = Mathf.Lerp(0.40f, 0.95f, distFraction);
                    seg.SetColor(WidgetStyleManager.WithAlpha(dynamicCol, segAlpha));
                }
                else
                {
                    seg.gameObject.SetActive(false);
                }
            }

            if (isDeadband)
            {
                if (_escortPointerObj.activeSelf) _escortPointerObj.SetActive(false);
            }
            else
            {
                if (!_escortPointerObj.activeSelf) _escortPointerObj.SetActive(true);

                Vector2 needlePos = EvalArcPoint(escortRadius, _trendAngle);
                _escortPointerRt.SetAnchoredPositionSafe(needlePos);

                float rotAngle = _isLeftOrientation ? -_trendAngle : _trendAngle;
                _escortPointerRt.localEulerAngles = new Vector3(0f, 0f, rotAngle);

                if (_escortPointerLine != null) _escortPointerLine.SetColor(dynamicCol);
                if (_escortPointerHead != null) _escortPointerHead.SetColor(dynamicCol);
            }
        }

        private void DrawTerrainGroundHighlight(double agl)
        {
            if (_bandBgImages.Count == 0 || double.IsNaN(agl)) return;

            if (agl < 300.0 && agl >= -5.0)
            {
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                Color warnCol = WidgetStyleManager.WithAlpha(WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Warning, theme), 0.35f);
                Color baseCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);

                int groundPlates = Mathf.Min(6, _bandBgImages.Count);
                for (int i = 0; i < groundPlates; i++)
                {
                    if (_bandBgImages[i] != null)
                    {
                        float blend = (1f - (float)i / groundPlates) * Mathf.Clamp01((float)((300.0 - agl) / 300.0));
                        _bandBgImages[i].SetColor(Color.Lerp(baseCol, warnCol, blend));
                    }
                }
            }
            else
            {
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                Color baseCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);
                foreach (var img in _bandBgImages)
                {
                    if (img != null) img.SetColor(baseCol);
                }
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastTopText.Reset(string.Empty);
            _lastBottomText.Reset(string.Empty);
            _trendAngle = 0f;
            _showingIntegerReadout = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }

    [Obsolete("Use ArcTapeWidget with custom.arc_speed_tape instead.")]
    public class ArcSpeedTapeWidget : ArcTapeWidget
    {
    }

    [Obsolete("Use ArcTapeWidget with custom.arc_altitude_tape instead.")]
    public class ArcAltitudeTapeWidget : ArcTapeWidget
    {
    }
}
