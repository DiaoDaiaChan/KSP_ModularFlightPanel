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
    /// PFD 弧形标尺带组件套件 (Arc Tape Gauge Widget - Speed & Altitude All-in-One)
    /// 次世代全景 HUD / 环抱式玻璃座舱流线曲率重构版
    /// 核心特性：
    /// 1. 速度带 / 高度带一体化合流：
    ///    - 位于姿态球左侧 (或配置 SPD) 自动呈现为【弧形速度带】：单位 (m/s, km/s, c)、ACC 载荷与速度变化率 (dV/dt) 同心伴随弧轨系统、模式切换 (SURF/ORBT/TGT)
    ///    - 位于姿态球右侧 (或配置 ALT) 自动呈现为【弧形高度带】：单位 (m, km, Mm, Gm)、垂直升降率 (VSI) 同心伴随弧轨系统、底部雷达真实高度 (RDR AGL) 与近地防撞着色
    /// 2. 彻底告别垂直长方形硬贴块 —— 【同心伴随弧轨动力学系统 (Concentric Dynamic Escort Arc System)】：
    ///    - 摒弃四方形长柱子，沿主圆弧曲率向外平行同心生成伴随副轨 (Concentric Radial Rail)
    ///    - 纯极坐标解析几何驱动：以中央水平法线 (θ = 0°) 为绝对零位基准，向上扬起加速/爬升同心发光弧束，向下沉落减速/俯冲同心制动弧束
    ///    - 速度带同时呈现 ACC (G 载荷 0~8G) 与 dV/dt (速度变化率) 切向流线光束与微胶囊，4G 黄色警告、8G 红色极值
    ///    - 高度带呈现 VSI (垂直速度 ±100m/s) 爬升/沉降流线弧柱与超沉降率 (Sink Rate > 20m/s) 危险告警
    /// 3. 动态可调曲率：支持通过 CustomTemplate 设定 CURVATURE (0.05~1.0)、RADIUS (80~600px)、SPAN (40°~120°)、SIDE (LEFT/RIGHT)
    /// 4. 激光极坐标刻度池：主刻度 (切向自适应微倾斜标牌) 与微刻度切向排布，中心读数窗 ±6.8° 遮挡剔除杜绝穿模重叠
    /// 5. 动态无极工程量纲引擎 (带 15% 滞后死区防抖) 与 Tabular Monospace 等宽定宽制表排版，彻底根除高频数字抖动与闪烁
    /// 6. 严格遵照 MFP-SPEC-001..007 标准化铁律，0 颜色字面量，100% 通配符双通道驱动，零 GC
    /// </summary>
    [FlightWidget("arc_tape", "arc_speed_tape", "arc_altitude_tape", "arc_alt_tape", "curved_tape", "curved_speed_tape", "curved_altitude_tape", "curved_alt_tape", Category = WidgetCategory.Gauges, DisplayName = "HUD 弧形滚动标尺带", Description = "次世代 HUD / 玻璃座舱弧形标尺带，支持可调曲率、半径与垂直升降率/动压指示。", DefaultWidgetId = "custom.arc_speed_tape", DefaultX = -235f, DefaultY = 0f, HighFrequency = true, ExactIds = new[] { "custom.arc_speed_tape", "core.arc_speed_tape", "custom.arc_altitude_tape", "core.arc_altitude_tape" })]
    public class ArcTapeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(198f, 346f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // 声明式微控件
        public TextWidget ModeTag = TextWidget.Title("SPD");
        public TextWidget CenterValue = TextWidget.Value("{SPD}");

        private const int TICK_POOL_SIZE = 28;
        private const int ARC_SEGMENT_COUNT = 24;
        private const int ESCORT_SEGMENT_COUNT = 20; // 同心伴随弧轨动力学微段数
        private const double SPEED_OF_LIGHT = 299792458.0;
        private const double DYNAMIC_DEADBAND = 0.08;

        public enum DynamicUnitTier
        {
            Base = 0,    // m/s 或 m
            Kilo = 1,    // km/s 或 km
            Mega = 2,    // c 或 Mm
            Giga = 3     // Gm (高度极值)
        }

        // ==================== 弧形几何参数 ====================
        private float _curvature = 0.5f;        // 0.05 (平缓近直) ~ 1.0 (紧凑)
        private float _baseRadius = 200f;       // 基础半径 (px)
        private float _angularSpan = 80f;       // 总视角跨度 (度)
        private bool _isLeftOrientation = true;  // 默认左侧 (左侧速度带向右环抱中枢姿态球；右侧高度带向左环抱)
        private bool _isSpeedTape = true;        // 速度带还是高度带

        // 通配符通道与文本模板
        private string _valueToken = "{SPD}";
        private string _topModeTemplate = "SPD";
        private string _bottomSecTemplate = "{MACH}";
        private string _trendToken = "{DV_DT}";
        private string _accToken = "{ACC}";
        private string _terrainToken = "{ALT:AGL}";
        private float _baseStep = 10f;
        private float _trendMaxScale = 20f;

        // ==================== 视图层节点 ====================
        // 1. 弧形背景与同心轮廓线
        private GameObject _arcBandRoot;
        private readonly List<Image> _bandBgImages = new List<Image>();
        private readonly List<Image> _bandRimImages = new List<Image>();

        // 2. 刻度对象池
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

        // 3. 中央高反差实体读数窗口与五边形指向凸嘴
        private GameObject _centerBoxObj;
        private RectTransform _centerBoxRt;
        private Image _centerBoxBg;
        private Outline _centerBoxOutline;
        private Text _centerValueText;
        private Text _centerUnitText;
        private Button _centerBoxBtn;
        private Image _pointerArrow;
        private RectTransform _pointerArrowRt;

        // 4. 顶部模式微胶囊
        private GameObject _modeTagObj;
        private Image _modeTagBg;
        private Outline _modeTagOutline;
        private Text _modeTagText;

        // 5. 底部次级航电窗 (速度带显示 Mach，高度带显示 Radar AGL)
        private GameObject _bottomSecObj;
        private Image _bottomSecBg;
        private Outline _bottomSecOutline;
        private Text _bottomSecText;

        // 6. 次世代同心伴随弧轨系统 (Concentric Dynamic Escort Arc System)
        // 彻底摆脱粗苯长方形！
        private GameObject _escortRailRoot;
        private readonly List<Image> _escortTrackImages = new List<Image>();    // 轨道底线
        private readonly List<Image> _escortRibbonSegments = new List<Image>(); // 动态弧光段
        private GameObject _escortDatumTick;                                    // θ=0° 水平零位刻痕
        private Image _escortDatumImg;

        // 切向流线动态游标尖角 (Tangential Dynamic Chevron)
        private GameObject _escortPointerObj;
        private RectTransform _escortPointerRt;
        private Image _escortPointerLine;
        private Image _escortPointerHead;

        // 动力学流线航电微胶囊 (依附同心弧端头切向自然延伸)
        private GameObject _escortBadgeObj;
        private Image _escortBadgeBg;
        private Outline _escortBadgeOutline;
        private Text _escortBadgeTextPrimary;
        private Text _escortBadgeTextSecondary;

        // ==================== 运行态数据与脏标记 ====================
        private DynamicUnitTier _currentTier = DynamicUnitTier.Base;
        private string _activeUnitStr = "m/s";
        private float _activeStep = 10f;
        private double _activeScale = 1.0;
        private bool _showingIntegerReadout = false;

        private double _lastRawVal = double.NaN;
        private double _lastDisplayVal = double.NaN;
        private string _lastCenterText = string.Empty;
        private string _lastUnitText = string.Empty;
        private string _lastTopText = string.Empty;
        private string _lastBottomText = string.Empty;

        private double _filteredTrendRate = 0.0;
        private double _lastRawTrendRate = 0.0;
        private float _lastTrendAngle = 0f;
        private double _lastTerrainVal = double.NaN;
        private string _lastBadgePrimaryText = string.Empty;
        private string _lastBadgeSecondaryText = string.Empty;
        private int _lastAlertLevel = -1;

        // 双轨状态快照
        private double _pendingRawVal = double.NaN;
        private double _pendingDisplayVal = double.NaN;
        private string _pendingTopText = string.Empty;
        private string _pendingBottomText = string.Empty;
        private int _pendingAlertLevel = 0;
        private string _pendingBadgePrimary = string.Empty;
        private string _pendingBadgeSecondary = string.Empty;
        private float _pendingTargetAngle = 0f;
        private bool _pendingIsDeadband = true;
        private double _pendingAgl = double.NaN;
        private bool _hasPendingHeartbeat = false;
        private bool _trendRatePositive = true;

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
            if (_isSpeedTape)
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{SPD}";
                _topModeTemplate = "{SPD:MODE}";
                _bottomSecTemplate = "{MACH}";
                _trendToken = "{DV_DT}";
                _accToken = "{ACC}";
                _baseStep = config != null && config.StepInterval > 0.01f ? config.StepInterval : 10f;
                _trendMaxScale = 20.0f;
                _activeUnitStr = "m/s";
            }
            else
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{ALT}";
                _topModeTemplate = "{ALT:MODE}";
                _bottomSecTemplate = "{ALT:AGL:DIST}";
                _trendToken = "{VSI}";
                _accToken = "{ACC}";
                _terrainToken = "{ALT:AGL}";
                _baseStep = config != null && config.StepInterval > 0.01f ? config.StepInterval : 100f;
                _trendMaxScale = 100.0f;
                _activeUnitStr = "m";
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
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, _valueToken);
            _topModeTemplate = GetTemplateChannel(new[] { "TOP", "MODE" }, _topModeTemplate);
            _bottomSecTemplate = GetTemplateChannel(new[] { "BOTTOM", "SEC" }, _bottomSecTemplate);
            _trendToken = GetTemplateChannel(new[] { "TREND", "VSI", "RATE", "DV" }, _trendToken);
            _accToken = GetTemplateChannel("ACC", _accToken);
            _terrainToken = GetTemplateChannel(new[] { "TERRAIN", "AGL" }, _terrainToken);
            _trendMaxScale = GetTemplateChannelFloat("TREND_MAX", _trendMaxScale);

            if (_baseRadius <= 80f || _baseRadius >= 599f)
            {
                _baseRadius = Mathf.Lerp(420f, 140f, Mathf.Clamp01(_curvature));
            }

            float s = CurrentDpiScale;
            float r = _baseRadius * s;

            // 根包围盒尺寸计算 (包括同心副轨的外延空间)
            float halfSpanRad = (_angularSpan * 0.5f) * Mathf.Deg2Rad;
            float totalH = 2f * (r + 26f * s) * Mathf.Sin(halfSpanRad) + 56f * s;
            float totalW = (r + 26f * s) * (1f - Mathf.Cos(halfSpanRad)) + 145f * s;
            RectTransform.sizeDelta = new Vector2(totalW, totalH);

            // 1. 构建分段深色有机玻璃主弧带与同心辉光边缘
            BuildArcBand(r, s, theme);

            // 2. 初始化极坐标刻度对象池
            BuildTickPool(r, s, theme);

            // 3. 构建中央高反差读数窗与五边形凸嘴
            BuildCenterReadoutBox(r, s, theme);

            // 4. 构建顶部模式微胶囊
            BuildTopModeCapsule(r, s, theme);

            // 5. 构建底部次级航电窗 (Mach / Radar Alt)
            BuildBottomSecondaryBox(r, s, theme);

            // 6. 构建次世代同心伴随弧轨动力学系统 (替代长方形)
            BuildConcentricEscortSystem(r, s, theme);

            // 标准化组件内部控件注册至管理器 (0 影响原画质与排版)
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

                // 1. 半透明暗色玻璃遮光弧板
                GameObject plate = UIFactory.CreatePanel(_arcBandRoot.transform, $"ArcPlate_{i}",
                    new Vector2(arcSegW, bandThickness), pt, bandCol);
                plate.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _bandBgImages.Add(plate.GetComponent<Image>());

                // 2. 外缘同心发光轮廓
                float rimOffset = 14f * s;
                Vector2 outerPt = EvalArcPoint(radius + rimOffset, ang);
                GameObject outerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"OuterRim_{i}",
                    new Vector2(arcSegW, 1.2f * s), outerPt, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
                outerRim.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _bandRimImages.Add(outerRim.GetComponent<Image>());

                // 3. 内缘同心细弱辅助线
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
                // 左侧弧带：圆心在右侧 (+radius, 0)
                // angleDeg = 0 位于 (0, 0)，向左凸起，向右环抱中枢姿态球
                float x = radius * (1f - cos);
                float y = radius * sin;
                return new Vector2(x, y);
            }
            else
            {
                // 右侧弧带：圆心在左侧 (-radius, 0)
                // angleDeg = 0 位于 (0, 0)，向右凸起，向左环抱中枢姿态球
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

                // 刻度线
                float lineW = 9f * s;
                float lineH = 1.2f * s;
                GameObject lineObj = UIFactory.CreatePanel(root.transform, "Line", new Vector2(lineW, lineH), Vector2.zero, tickCol);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                Image lineImg = lineObj.GetComponent<Image>();

                // 数字标牌 (位于刻度线外侧，依据左右方位自适应对齐)
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

                root.SetActive(false);
            }
        }

        private void BuildCenterReadoutBox(float radius, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 中央读数窗尺寸
            float boxW = 58f * s;
            float boxH = 26f * s;
            float boxX = _isLeftOrientation ? (-boxW * 0.5f - 14f * s) : (boxW * 0.5f + 14f * s);

            _centerBoxObj = UIFactory.CreatePanel(transform, "Center_Readout_Box", new Vector2(boxW, boxH), new Vector2(boxX, 0f), Color.clear);
            _centerBoxRt = _centerBoxObj.GetComponent<RectTransform>();
            _centerBoxBg = _centerBoxObj.GetComponent<Image>();
            _centerBoxOutline = _centerBoxObj.AddComponent<Outline>();
            _centerBoxOutline.effectDistance = new Vector2(1.2f * s, 1.2f * s);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.Normal, theme);

            _centerBoxBtn = _centerBoxObj.AddComponent<Button>();
            _centerBoxBtn.onClick.AddListener(OnCenterBoxClicked);

            // 指向弧带的五边形凸嘴 (Chevron Pointer)
            float chevronX = _isLeftOrientation ? (boxW * 0.5f + 4f * s) : (-boxW * 0.5f - 4f * s);
            GameObject ptrObj = UIFactory.CreatePanel(_centerBoxObj.transform, "Chevron_Pointer",
                new Vector2(8f * s, 8f * s), new Vector2(chevronX, 0f),
                style.GetMeterColor(MeterStyleRole.Accent, theme));
            _pointerArrowRt = ptrObj.GetComponent<RectTransform>();
            _pointerArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _pointerArrow = ptrObj.GetComponent<Image>();

            // 读数与单位
            int valFontSize = Mathf.RoundToInt(13.5f * s);
            int unitFontSize = Mathf.Max(7, Mathf.RoundToInt(8f * s));

            _centerValueText = UIFactory.CreateText(_centerBoxObj.transform, "ValueText", "0", valFontSize,
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _centerValueText.fontStyle = FontStyle.Bold;
            _centerValueText.alignByGeometry = false;
            RectTransform vRt = _centerValueText.GetComponent<RectTransform>();
            vRt.pivot = new Vector2(1f, 0.5f);
            vRt.sizeDelta = new Vector2(36f * s, boxH);
            vRt.anchoredPosition = new Vector2(3f * s, 0f);

            _centerUnitText = UIFactory.CreateText(_centerBoxObj.transform, "UnitText", _activeUnitStr, unitFontSize,
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, theme));
            _centerUnitText.fontStyle = FontStyle.Bold;
            _centerUnitText.alignByGeometry = false;
            RectTransform uRt = _centerUnitText.GetComponent<RectTransform>();
            uRt.pivot = new Vector2(0f, 0.5f);
            uRt.sizeDelta = new Vector2(18f * s, boxH);
            uRt.anchoredPosition = new Vector2(4.5f * s, -0.5f * s);
        }

        private void BuildTopModeCapsule(float radius, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float boxW = 50f * s;
            float boxH = 16f * s;
            float halfSpanRad = (_angularSpan * 0.5f) * Mathf.Deg2Rad;
            float topY = radius * Mathf.Sin(halfSpanRad) + 14f * s;
            float topX = _isLeftOrientation ? (radius * (1f - Mathf.Cos(halfSpanRad)) - 10f * s) : (-radius * (1f - Mathf.Cos(halfSpanRad)) + 10f * s);

            _modeTagObj = UIFactory.CreatePanel(transform, "Top_Mode_Capsule", new Vector2(boxW, boxH), new Vector2(topX, topY), Color.clear);
            _modeTagBg = _modeTagObj.GetComponent<Image>();
            _modeTagOutline = _modeTagObj.AddComponent<Outline>();
            _modeTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_modeTagBg, _modeTagOutline, CardStyleRole.SubtleSlot, theme);

            Button btn = _modeTagObj.AddComponent<Button>();
            btn.onClick.AddListener(OnCenterBoxClicked);

            _modeTagText = UIFactory.CreateText(_modeTagObj.transform, "Mode_Text", _isSpeedTape ? I18n.Tr("WIDGET_GAUGE_SURF", "表面") : "ALT", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _modeTagText.resizeTextForBestFit = true;
            _modeTagText.resizeTextMinSize = Mathf.RoundToInt(6f * s);
            _modeTagText.resizeTextMaxSize = Mathf.RoundToInt(8.5f * s);
            _modeTagText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modeTagText.fontStyle = FontStyle.Bold;
            _modeTagText.alignByGeometry = false;
            RectTransform mtRt = _modeTagText.GetComponent<RectTransform>();
            mtRt.sizeDelta = new Vector2(boxW, boxH);
            mtRt.anchoredPosition = Vector2.zero;
        }

        private void BuildBottomSecondaryBox(float radius, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float boxW = 56f * s;
            float boxH = 15f * s;
            float halfSpanRad = (_angularSpan * 0.5f) * Mathf.Deg2Rad;
            float btmY = -radius * Mathf.Sin(halfSpanRad) - 14f * s;
            float btmX = _isLeftOrientation ? (radius * (1f - Mathf.Cos(halfSpanRad)) - 10f * s) : (-radius * (1f - Mathf.Cos(halfSpanRad)) + 10f * s);

            _bottomSecObj = UIFactory.CreatePanel(transform, "Bottom_Sec_Box", new Vector2(boxW, boxH), new Vector2(btmX, btmY), Color.clear);
            _bottomSecBg = _bottomSecObj.GetComponent<Image>();
            _bottomSecOutline = _bottomSecObj.AddComponent<Outline>();
            _bottomSecOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bottomSecBg, _bottomSecOutline, CardStyleRole.SubtleSlot, theme);

            _bottomSecText = UIFactory.CreateText(_bottomSecObj.transform, "Bottom_Sec_Text", _isSpeedTape ? "0.0 M" : "RDR --", Mathf.RoundToInt(8.0f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _bottomSecText.fontStyle = FontStyle.Bold;
            _bottomSecText.alignByGeometry = false;
            RectTransform btRt = _bottomSecText.GetComponent<RectTransform>();
            btRt.sizeDelta = new Vector2(boxW, boxH);
            btRt.anchoredPosition = Vector2.zero;
        }

        // ==================== 次世代同心伴随弧轨动力学系统 ====================
        // 彻底摆脱长方形！沿着主弧外轮廓同心延伸平行弧光轨道
        private void BuildConcentricEscortSystem(float radius, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);
            Color primaryCol = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            float escortRadius = radius + 20f * s; // 位于主弧带外沿同心偏移 +20px

            _escortRailRoot = CreateContainer("Concentric_Escort_Rail", transform).gameObject;

            _escortTrackImages.Clear();
            _escortRibbonSegments.Clear();

            float halfSpan = _angularSpan * 0.5f;
            float step = _angularSpan / ESCORT_SEGMENT_COUNT;
            float arcSegW = ((2f * Mathf.PI * escortRadius * (_angularSpan / 360f)) / ESCORT_SEGMENT_COUNT) + 1.5f * s;

            // 1. 构建贯穿整个开角的极细同心基线导轨与发光流光弧段
            for (int i = 0; i <= ESCORT_SEGMENT_COUNT; i++)
            {
                float ang = -halfSpan + (i * step);
                Vector2 pt = EvalArcPoint(escortRadius, ang);
                float rotAngle = _isLeftOrientation ? -ang : ang;

                // 伴随导轨底线 (极弱同心线)
                GameObject trackObj = UIFactory.CreatePanel(_escortRailRoot.transform, $"EscortTrack_{i}",
                    new Vector2(arcSegW, 1.0f * s), pt, WidgetStyleManager.WithAlpha(borderCol, 0.22f));
                trackObj.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                _escortTrackImages.Add(trackObj.GetComponent<Image>());

                // 动态能量弧光带段 (用于充填加速/爬升/过载的圆弧流光)
                GameObject ribbonObj = UIFactory.CreatePanel(_escortRailRoot.transform, $"EscortRibbon_{i}",
                    new Vector2(arcSegW, 2.5f * s), pt, WidgetStyleManager.WithAlpha(primaryCol, 0.85f));
                ribbonObj.transform.localEulerAngles = new Vector3(0f, 0f, rotAngle);
                Image ribbonImg = ribbonObj.GetComponent<Image>();
                _escortRibbonSegments.Add(ribbonImg);
                ribbonObj.SetActive(false);
            }

            // 2. θ = 0° 处的径向零位基准微标线 (Zero Radial Datum Notch)
            Vector2 zeroPt = EvalArcPoint(escortRadius, 0f);
            float datumW = 7f * s;
            _escortDatumTick = UIFactory.CreatePanel(_escortRailRoot.transform, "Escort_Zero_Datum",
                new Vector2(datumW, 1.4f * s), zeroPt, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
            _escortDatumImg = _escortDatumTick.GetComponent<Image>();

            // 3. 伴随切向流线游标尖角 (Tangential Pointer Chevron)
            _escortPointerRt = CreateContainer("Escort_Dynamic_Pointer", _escortRailRoot.transform);
            _escortPointerObj = _escortPointerRt.gameObject;

            GameObject pLine = UIFactory.CreatePanel(_escortPointerObj.transform, "PointerLine",
                new Vector2(7f * s, 1.8f * s), Vector2.zero, primaryCol);
            _escortPointerLine = pLine.GetComponent<Image>();

            GameObject pHead = UIFactory.CreatePanel(_escortPointerObj.transform, "PointerHead",
                new Vector2(5.5f * s, 5.5f * s), new Vector2(_isLeftOrientation ? 4f * s : -4f * s, 0f), primaryCol);
            pHead.GetComponent<RectTransform>().localEulerAngles = new Vector3(0f, 0f, 45f);
            _escortPointerHead = pHead.GetComponent<Image>();

            _escortPointerObj.SetActive(false);

            // 4. 动力学流线航电微胶囊 (依附同心弧顶端切向自然延伸，杜绝直角方块)
            float badgeW = _isSpeedTape ? 64f * s : 54f * s;
            float badgeH = _isSpeedTape ? 22f * s : 16f * s;
            float halfSpanRad = (_angularSpan * 0.5f) * Mathf.Deg2Rad;
            float badgeY = (escortRadius + 10f * s) * Mathf.Sin(halfSpanRad * 0.85f);
            float badgeX = _isLeftOrientation
                ? ((escortRadius + 14f * s) * (1f - Mathf.Cos(halfSpanRad * 0.85f)) - badgeW * 0.5f - 8f * s)
                : (-(escortRadius + 14f * s) * (1f - Mathf.Cos(halfSpanRad * 0.85f)) + badgeW * 0.5f + 8f * s);

            _escortBadgeObj = UIFactory.CreatePanel(transform, "Escort_Dynamic_Badge",
                new Vector2(badgeW, badgeH), new Vector2(badgeX, badgeY), Color.clear);
            _escortBadgeBg = _escortBadgeObj.GetComponent<Image>();
            _escortBadgeOutline = _escortBadgeObj.AddComponent<Outline>();
            _escortBadgeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_escortBadgeBg, _escortBadgeOutline, CardStyleRole.SubtleSlot, theme);

            if (_isSpeedTape)
            {
                // 速度带胶囊：双排高密度排版 (ACC 与 dV/dt)
                int tagFontSize = Mathf.RoundToInt(7.5f * s);
                _escortBadgeTextPrimary = UIFactory.CreateText(_escortBadgeObj.transform, "Badge_ACC", "ACC 1.0G",
                    tagFontSize, TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _escortBadgeTextPrimary.fontStyle = FontStyle.Bold;
                _escortBadgeTextPrimary.alignByGeometry = false;
                RectTransform b1Rt = _escortBadgeTextPrimary.GetComponent<RectTransform>();
                b1Rt.sizeDelta = new Vector2(badgeW - 6f * s, badgeH * 0.5f);
                b1Rt.anchoredPosition = new Vector2(3f * s, badgeH * 0.25f);

                _escortBadgeTextSecondary = UIFactory.CreateText(_escortBadgeObj.transform, "Badge_DV", "dV 0.0",
                    tagFontSize, TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Accent, theme));
                _escortBadgeTextSecondary.fontStyle = FontStyle.Bold;
                _escortBadgeTextSecondary.alignByGeometry = false;
                RectTransform b2Rt = _escortBadgeTextSecondary.GetComponent<RectTransform>();
                b2Rt.sizeDelta = new Vector2(badgeW - 6f * s, badgeH * 0.5f);
                b2Rt.anchoredPosition = new Vector2(3f * s, -badgeH * 0.25f);
            }
            else
            {
                // 高度带胶囊：单排 VSI 精密读数
                int tagFontSize = Mathf.RoundToInt(8.5f * s);
                _escortBadgeTextPrimary = UIFactory.CreateText(_escortBadgeObj.transform, "Badge_VSI", "V/S 0.0",
                    tagFontSize, TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
                _escortBadgeTextPrimary.fontStyle = FontStyle.Bold;
                _escortBadgeTextPrimary.alignByGeometry = false;
                RectTransform b1Rt = _escortBadgeTextPrimary.GetComponent<RectTransform>();
                b1Rt.sizeDelta = new Vector2(badgeW, badgeH);
                b1Rt.anchoredPosition = Vector2.zero;
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

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Color bandCol = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            Color borderCol = style.GetCardBorderColor(CardStyleRole.Normal, theme);

            foreach (var img in _bandBgImages) if (img != null) img.color = bandCol;
            for (int i = 0; i < _bandRimImages.Count; i++)
            {
                if (_bandRimImages[i] != null)
                {
                    _bandRimImages[i].color = (i % 2 == 0)
                        ? WidgetStyleManager.Weighted(borderCol, LineWeight.Strong)
                        : WidgetStyleManager.Weighted(borderCol, LineWeight.Subtle);
                }
            }

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

            // 同心伴随轨道着色
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
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 数值双精度求值 (速度或高度)
            double rawVal = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(rawVal)) rawVal = 0.0;
            if (_isSpeedTape && rawVal < 0.0) rawVal = 0.0;

            // 2. 动态工程量纲换算 (m/s, km/s, c 或 m, km, Mm, Gm)
            UpdateDynamicUnits(rawVal);

            double displayVal = rawVal * _activeScale;
            _pendingRawVal = rawVal;
            _pendingDisplayVal = displayVal;

            // 3. 模式标签与次级信息窗求值
            _pendingTopText = TelemetryTokenEngine.Evaluate(_topModeTemplate, telemetry);
            _pendingBottomText = TelemetryTokenEngine.Evaluate(_bottomSecTemplate, telemetry);

            // 4. 同心伴随弧轨动力学系统求值 (ACC、dV/dt 或 VSI)
            double rawRate = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
            if (double.IsNaN(rawRate)) rawRate = 0.0;

            if (Math.Abs(rawRate - _lastRawTrendRate) < DYNAMIC_DEADBAND)
            {
                rawRate = _filteredTrendRate;
            }
            else
            {
                _filteredTrendRate = rawRate;
                _lastRawTrendRate = rawRate;
            }

            _trendRatePositive = rawRate >= 0;
            int alertLevel = 0;

            if (_isSpeedTape)
            {
                double gForce = TelemetryTokenEngine.EvaluateNumeric(_accToken, telemetry);
                if (double.IsNaN(gForce)) gForce = 1.0;

                if (gForce >= 8.0) alertLevel = 2;
                else if (gForce >= 4.0) alertLevel = 1;

                _pendingBadgePrimary = UIFactory.FormatTabular($"ACC {gForce:F1}G");
                string rateSign = rawRate > 0 ? "+" : "";
                _pendingBadgeSecondary = Math.Abs(rawRate) < 0.05 ? "dV 0.0" : UIFactory.FormatTabular($"dV {rateSign}{rawRate:F1}");
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

                _pendingBadgePrimary = UIFactory.FormatTabular(vsiStr);
            }
            _pendingAlertLevel = alertLevel;

            float halfSpan = _angularSpan * 0.5f;
            float maxScale = _trendMaxScale > 0.1f ? _trendMaxScale : 20.0f;
            float rateFraction = Mathf.Clamp((float)(rawRate / maxScale), -1f, 1f);
            _pendingTargetAngle = rateFraction * (halfSpan * 0.82f);
            _pendingIsDeadband = Math.Abs(rawRate) < 0.05;

            // 5. 若为高度带，计算地面防撞警戒
            if (!_isSpeedTape)
            {
                double agl = TelemetryTokenEngine.EvaluateNumeric(_terrainToken, telemetry);
                if (double.IsNaN(agl)) agl = rawVal;
                _pendingAgl = agl;
            }

            _hasPendingHeartbeat = true;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_hasPendingHeartbeat) return;

            // 1. 几何脏检查防抖 (死区小于阈值不重构弧度刻度)
            double deltaThreshold = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (double.IsNaN(_lastRawVal) || Math.Abs(_pendingRawVal - _lastRawVal) > deltaThreshold)
            {
                _lastRawVal = _pendingRawVal;
                _lastDisplayVal = _pendingDisplayVal;

                UpdateCenterReadout(_pendingDisplayVal);
                UpdateArcTicks(_pendingDisplayVal);
            }

            // 2. 模式标签与次级信息窗更新
            if (_pendingTopText != _lastTopText)
            {
                _lastTopText = _pendingTopText;
                SetTextIfChanged(_modeTagText, _pendingTopText);
            }

            if (_pendingBottomText != _lastBottomText)
            {
                _lastBottomText = _pendingBottomText;
                SetTextIfChanged(_bottomSecText, _pendingBottomText);
            }

            // 3. 同心伴随弧轨动力学绘制
            DrawConcentricEscortDynamics();

            // 4. 地面防撞警戒弧板绘制
            if (!_isSpeedTape)
            {
                DrawTerrainGroundHighlight();
            }
        }

        private void UpdateDynamicUnits(double rawVal)
        {
            if (_isSpeedTape)
            {
                // 速度带：Base (m/s) -> Kilo (km/s) -> Mega (c)
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        if (rawVal >= 1000.0)
                        {
                            _currentTier = DynamicUnitTier.Kilo;
                            _activeUnitStr = "km/s";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else
                        {
                            _activeUnitStr = "m/s";
                            _activeScale = 1.0;
                            _activeStep = _baseStep > 0f ? _baseStep : 10f;
                        }
                        break;

                    case DynamicUnitTier.Kilo:
                        if (rawVal < 850.0)
                        {
                            _currentTier = DynamicUnitTier.Base;
                            _activeUnitStr = "m/s";
                            _activeScale = 1.0;
                            _activeStep = _baseStep > 0f ? _baseStep : 10f;
                        }
                        else if (rawVal >= 1000000.0)
                        {
                            _currentTier = DynamicUnitTier.Mega;
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

                    case DynamicUnitTier.Mega:
                        if (rawVal < 850000.0)
                        {
                            _currentTier = DynamicUnitTier.Kilo;
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
                // 高度带：Base (m) -> Kilo (km) -> Mega (Mm) -> Giga (Gm)
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        if (rawVal >= 1000.0)
                        {
                            _currentTier = DynamicUnitTier.Kilo;
                            _activeUnitStr = "km";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else
                        {
                            _activeUnitStr = "m";
                            _activeScale = 1.0;
                            _activeStep = _baseStep > 0f ? _baseStep : 100f;
                        }
                        break;

                    case DynamicUnitTier.Kilo:
                        if (rawVal < 850.0)
                        {
                            _currentTier = DynamicUnitTier.Base;
                            _activeUnitStr = "m";
                            _activeScale = 1.0;
                            _activeStep = _baseStep > 0f ? _baseStep : 100f;
                        }
                        else if (rawVal >= 1000000.0)
                        {
                            _currentTier = DynamicUnitTier.Mega;
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

                    case DynamicUnitTier.Mega:
                        if (rawVal < 850000.0)
                        {
                            _currentTier = DynamicUnitTier.Kilo;
                            _activeUnitStr = "km";
                            _activeScale = 0.001;
                            _activeStep = 1.0f;
                        }
                        else if (rawVal >= 1000000000.0)
                        {
                            _currentTier = DynamicUnitTier.Giga;
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

                    case DynamicUnitTier.Giga:
                        if (rawVal < 850000000.0)
                        {
                            _currentTier = DynamicUnitTier.Mega;
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

        private void UpdateCenterReadout(double displayVal)
        {
            string formatted;
            if (_currentTier == DynamicUnitTier.Mega && _isSpeedTape)
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
            if (formatted != _lastCenterText)
            {
                _lastCenterText = formatted;
                if (_centerValueText != null) _centerValueText.text = formatted;
            }

            if (_activeUnitStr != _lastUnitText)
            {
                _lastUnitText = _activeUnitStr;
                if (_centerUnitText != null) _centerUnitText.text = _activeUnitStr;
            }
        }


        private void UpdateArcTicks(double displayVal)
        {
            float s = CurrentDpiScale;
            float r = _baseRadius * s;
            float halfSpan = _angularSpan * 0.5f;

            float step = _activeStep > 0f ? _activeStep : 10f;
            float subStep = step * 0.5f;

            // 每单位数值对应角度跨度 (在 halfSpan 内约容纳 4~5 个主刻度)
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

                // 极坐标坐标与切向旋转
                Vector2 pt = EvalArcPoint(r, ang);
                item.Rect.anchoredPosition = pt;
                float rotAngle = _isLeftOrientation ? -ang : ang;
                item.Rect.localEulerAngles = new Vector3(0f, 0f, rotAngle);

                // 边缘平滑渐隐 (视口端部淡出)
                float edgeDist = halfSpan - Mathf.Abs(ang);
                float alphaMul = Mathf.Clamp01(edgeDist / 8f);

                // 中央读数窗遮挡剔除 (防止刻度文字与中央读数框重叠干扰)
                bool isOccludedByBox = Mathf.Abs(ang) < 6.8f;

                double majorRemainder = Math.Abs(tickVal - Math.Round(tickVal / step) * step);
                bool isMajor = majorRemainder < (subStep * 0.25);

                if (isMajor)
                {
                    item.LineRt.sizeDelta = new Vector2(9f * s, 1.2f * s);
                    item.Line.color = WidgetStyleManager.WithAlpha(majorCol, majorCol.a * alphaMul);
                    item.Label.gameObject.SetActive(!isOccludedByBox);

                    if (!isOccludedByBox)
                    {
                        string lbl = (_currentTier == DynamicUnitTier.Mega && _isSpeedTape) ? $"{tickVal:F2}" :
                                     (step < 1.0f ? $"{tickVal:F1}" : $"{tickVal:F0}");
                        item.Label.text = lbl;

                        Color lCol = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);
                        item.Label.color = WidgetStyleManager.WithAlpha(lCol, lCol.a * alphaMul);
                    }
                }
                else
                {
                    item.LineRt.sizeDelta = new Vector2(4.5f * s, 1.0f * s);
                    item.Line.color = WidgetStyleManager.WithAlpha(subCol, subCol.a * alphaMul);
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        // ==================== 同心伴随弧轨动力学绘制 ====================
        private void DrawConcentricEscortDynamics()
        {
            if (_escortRailRoot == null) return;

            float s = CurrentDpiScale;
            float escortRadius = (_baseRadius + 20f) * s;
            float halfSpan = _angularSpan * 0.5f;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            int alertLevel = _pendingAlertLevel;
            Color dynamicCol = alertLevel == 2 ? style.GetMeterColor(MeterStyleRole.Danger, theme) :
                               alertLevel == 1 ? style.GetMeterColor(MeterStyleRole.Warning, theme) :
                               (_isSpeedTape ? style.GetMeterColor(MeterStyleRole.Primary, theme) :
                                (_trendRatePositive ? style.GetMeterColor(MeterStyleRole.Primary, theme) :
                                                      style.GetMeterColor(MeterStyleRole.Accent, theme)));

            if (_isSpeedTape)
            {
                if (_pendingBadgePrimary != _lastBadgePrimaryText)
                {
                    _lastBadgePrimaryText = _pendingBadgePrimary;
                    if (_escortBadgeTextPrimary != null) _escortBadgeTextPrimary.text = _pendingBadgePrimary;
                }
                if (_pendingBadgeSecondary != _lastBadgeSecondaryText)
                {
                    _lastBadgeSecondaryText = _pendingBadgeSecondary;
                    if (_escortBadgeTextSecondary != null) _escortBadgeTextSecondary.text = _pendingBadgeSecondary;
                }
            }
            else
            {
                if (_pendingBadgePrimary != _lastBadgePrimaryText)
                {
                    _lastBadgePrimaryText = _pendingBadgePrimary;
                    if (_escortBadgeTextPrimary != null) _escortBadgeTextPrimary.text = _pendingBadgePrimary;
                }
            }

            // 更新胶囊高亮外框
            if (alertLevel != _lastAlertLevel)
            {
                _lastAlertLevel = alertLevel;
                if (_escortBadgeOutline != null)
                {
                    _escortBadgeOutline.effectColor = alertLevel == 2 ? style.GetMeterColor(MeterStyleRole.Danger, theme) :
                                                      alertLevel == 1 ? style.GetMeterColor(MeterStyleRole.Warning, theme) :
                                                                        theme.FrameBorderColor.ToColor();
                }
                if (_escortBadgeBg != null)
                {
                    Color baseBg = theme.FrameBgColor;
                    if (alertLevel == 2)
                        _escortBadgeBg.color = Color.Lerp(baseBg, style.GetMeterColor(MeterStyleRole.Danger, theme), 0.22f);
                    else if (alertLevel == 1)
                        _escortBadgeBg.color = Color.Lerp(baseBg, style.GetMeterColor(MeterStyleRole.Warning, theme), 0.16f);
                    else
                        _escortBadgeBg.color = baseBg;
                }
            }

            _lastTrendAngle = Mathf.Lerp(_lastTrendAngle, _pendingTargetAngle, 0.25f);
            bool isDeadband = _pendingIsDeadband;

            // 1. 动态充填同心圆弧发光流线段 (Concentric Ribbon Fill)
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
                if (_lastTrendAngle >= 0f)
                {
                    inRange = (segAng >= -step * 0.5f && segAng <= _lastTrendAngle + step * 0.5f);
                }
                else
                {
                    inRange = (segAng <= step * 0.5f && segAng >= _lastTrendAngle - step * 0.5f);
                }

                if (inRange)
                {
                    seg.gameObject.SetActive(true);
                    float distFraction = Mathf.Clamp01(Mathf.Abs(segAng) / (Mathf.Abs(_lastTrendAngle) + 0.1f));
                    float segAlpha = Mathf.Lerp(0.40f, 0.95f, distFraction);
                    seg.color = WidgetStyleManager.WithAlpha(dynamicCol, segAlpha);
                }
                else
                {
                    seg.gameObject.SetActive(false);
                }
            }

            // 2. 切向流线动态游标尖角定位
            if (isDeadband)
            {
                if (_escortPointerObj.activeSelf) _escortPointerObj.SetActive(false);
            }
            else
            {
                if (!_escortPointerObj.activeSelf) _escortPointerObj.SetActive(true);

                Vector2 needlePos = EvalArcPoint(escortRadius, _lastTrendAngle);
                _escortPointerRt.anchoredPosition = needlePos;

                float rotAngle = _isLeftOrientation ? -_lastTrendAngle : _lastTrendAngle;
                _escortPointerRt.localEulerAngles = new Vector3(0f, 0f, rotAngle);

                if (_escortPointerLine != null) _escortPointerLine.color = dynamicCol;
                if (_escortPointerHead != null) _escortPointerHead.color = dynamicCol;
            }
        }

        private void DrawTerrainGroundHighlight()
        {
            if (_bandBgImages.Count == 0 || double.IsNaN(_pendingAgl)) return;

            double agl = _pendingAgl;
            if (agl < 300.0 && agl >= -5.0)
            {
                if (!double.IsNaN(_lastTerrainVal) && Math.Abs(agl - _lastTerrainVal) < 0.2) return;
                _lastTerrainVal = agl;

                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                Color warnCol = WidgetStyleManager.WithAlpha(WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Warning, theme), 0.35f);
                Color baseCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);

                int groundPlates = Mathf.Min(6, _bandBgImages.Count);
                for (int i = 0; i < groundPlates; i++)
                {
                    if (_bandBgImages[i] != null)
                    {
                        float blend = (1f - (float)i / groundPlates) * Mathf.Clamp01((float)((300.0 - agl) / 300.0));
                        _bandBgImages[i].color = Color.Lerp(baseCol, warnCol, blend);
                    }
                }
            }
            else if (!double.IsNaN(_lastTerrainVal) && _lastTerrainVal < 300.0)
            {
                _lastTerrainVal = agl;
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                Color baseCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);
                foreach (var img in _bandBgImages) if (img != null) img.color = baseCol;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }

    /// <summary>
    /// 兼容性别名类：保证历史反射与反序列化 100% 稳定向后兼容
    /// </summary>
    [Obsolete("Use ArcTapeWidget with custom.arc_speed_tape instead.")]
    public class ArcSpeedTapeWidget : ArcTapeWidget
    {
    }

    /// <summary>
    /// 兼容性别名类：弧形高度带专属别名
    /// </summary>
    [Obsolete("Use ArcTapeWidget with custom.arc_altitude_tape instead.")]
    public class ArcAltitudeTapeWidget : ArcTapeWidget
    {
    }
}
