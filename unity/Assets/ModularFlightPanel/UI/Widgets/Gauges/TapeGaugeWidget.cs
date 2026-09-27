using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 标尺带套件 (Speed Tape / Altitude Tape Kit) - 次世代航电重构版 (v2)
    /// 核心升级：
    /// 1. 黄金航电纵横比：高度拉伸至 240px，真实再现波音 787 / 空客 A350 PFD 舒展比例
    /// 2. 单位与数值直接合璧：单位（m, km, Mm, m/s, km/s, c）直接集成在中央高对比度读数窗内，一眼洞悉
    /// 3. 速度带四级激光精密刻度阶梯（主刻度、半步长中刻度、微齿刻度），告别稀疏粗糙感
    /// 4. 速度动力学双指示：除 ACC (G 载荷) 外，新增“速度变化率” (dV/dt，以 m/s² 实时呈现)
    /// 5. 底部信息窗功能升级：速度带底部集成 Mach 马赫数数显，高度带底部集成雷达真实高度
    /// 6. 太空全场景动态无极工程量纲引擎 (带 15% 滞后死区平滑防抖)
    /// 7. 严格遵照 MFP-SPEC-001..007 标准化铁律，0 颜色字面量，100% 通配符双驱动，零 GC
    /// </summary>
    [FlightWidget("tape", "tape_gauge", "speed_tape", "altitude_tape", Category = WidgetCategory.Gauges, DisplayName = "PFD 垂直动态标尺带", Description = "PFD 风格平滑滚动动态标尺带，支持任意物理数据与步长。", DefaultWidgetId = "tape.speed", DefaultX = -235f, DefaultY = 0f)]
    public class TapeGaugeWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        public override Vector2 BaseSize => new Vector2(50f, 240f);
        protected override bool AutoCreateCardFrame => false;

        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(40f, 120f);
        public Vector2 MaxBaseSize => new Vector2(100f, 960f);

        private const int TICK_POOL_SIZE = 64;

        public enum DynamicUnitTier
        {
            Base = 0,    // m 或 m/s
            Kilo = 1,    // km 或 km/s
            Mega = 2,    // Mm 或 c (光速)
            Giga = 3     // Gm (吉米/深空)
        }

        private Image _bgImage;
        private Outline _bgOutline;
        private RectTransform _viewportRt;
        private RectTransform _tickContainer;

        // 视口边缘渐变与镜面反光线 (Gloss Horizon Rim)
        private Image _topGlossRim;
        private Image _bottomGlossRim;
        private Image _topFadeImg;
        private Image _bottomFadeImg;

        // 垂直导轨基线与地面贴地警戒条 (Backbone Rail & Ground Ribbon)
        private GameObject _backboneRailObj;
        private Image _backboneRail;
        private GameObject _groundRibbonObj;
        private RectTransform _groundRibbonRt;
        private Image _groundRibbonImg;

        // 刻度池项 (支持主/中/微多级刻度阶梯)
        private struct TickItem
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
        }
        private readonly List<TickItem> _tickPool = new List<TickItem>(TICK_POOL_SIZE);

        // 中央高对比度实体读数窗口 (Center Odometer Readout Box)
        private RectTransform _centerBoxRt;
        private Image _centerBoxBg;
        private Outline _centerBoxOutline;
        private Text _centerValueText;
        private Text _centerUnitText;
        private Button _centerBoxBtn;

        // 一体化五边形指针凸嘴 (Chevron Pointer) 与发光准星发丝线 (Luminescent Index Ray)
        private RectTransform _pointerArrowRt;
        private Image _pointerArrowImg;
        private RectTransform _hairlineRayRt;
        private Image _hairlineRayImg;

        // 机械沉降微分割缝与微型 CNC 角标 (Divider Seam & Corner Accents)
        private Image _centerSeamImg;
        private readonly List<Image> _cornerAccents = new List<Image>(4);

        // 顶部模式胶囊盒 (Top Mode Capsule)
        private GameObject _topModeBox;
        private Image _topModeBg;
        private Outline _topModeOutline;
        private Text _topModeText;
        private Button _topModeBtn;
        private Image _topLedDot;

        // 底部次级航电窗 (Bottom Secondary Box: 速度带显示 Mach，高度带显示 AGL)
        private GameObject _bottomSecBox;
        private Image _bottomSecBg;
        private Outline _bottomSecOutline;
        private Text _bottomSecText;

        // 侧边独立外置动力学柱 (Side Dynamic Columns)
        private RectTransform _trendRoot;

        // 速度带双列长条：左列 ACC (G 载荷柱，-> 航空指针式)
        private GameObject _accTagBox;
        private Image _accTagBg;
        private Outline _accTagOutline;
        private Text _accTagText;
        private Text _accValText;
        private GameObject _accTrackBgObj;
        private Image _accTrackBg;
        private Outline _accTrackOutline;
        private Image _accTrack;
        private RectTransform _accTraceRt;
        private Image _accTraceImg;
        private RectTransform _accPointerRt;
        private Image _accPointerHead;
        private Image _accPointerStem;

        // 速度带双列长条：右列 dV/dt (速度变化率柱，-> 航空指针式)
        private GameObject _rateTagBox;
        private Image _rateTagBg;
        private Outline _rateTagOutline;
        private Text _rateTagText;
        private Text _rateValText;
        private GameObject _rateTrackBgObj;
        private Image _rateTrackBg;
        private Outline _rateTrackOutline;
        private Image _rateTrack;
        private Image _rateZeroAnchor;
        private RectTransform _rateTraceRt;
        private Image _rateTraceImg;
        private RectTransform _ratePointerRt;
        private Image _ratePointerHead;
        private Image _ratePointerStem;

        // 高度带单列长条：侧边精密升降率 (VSI，<- 航空指针式)
        private GameObject _vsiTagBox;
        private Image _vsiTagBg;
        private Outline _vsiTagOutline;
        private Text _vsiTagText;
        private Text _vsiRateText;
        private GameObject _vsiTrackBgObj;
        private Image _vsiTrackBg;
        private Outline _vsiTrackOutline;
        private Image _vsiTrack;
        private Image _vsiZeroAnchor;
        private RectTransform _vsiTraceRt;
        private Image _vsiTraceImg;
        private RectTransform _vsiPointerRt;
        private Image _vsiPointerHead;
        private Image _vsiPointerStem;

        // 通配符通道与配置
        private string _valueToken = "{SPD}";
        private string _topModeTemplate = "SPD";
        private string _bottomSecTemplate = "{MACH}";
        private string _trendToken = "{GFORCE}";
        private string _trendTagTemplate = "ACC";
        private string _terrainToken = "{ALT:AGL}";
        private float _trendMaxScale = 8.0f;
        private bool _isSpeedTape = false;

        // 太空全场景动态无极工程量纲引擎 (Dynamic Multi-Scale Engine)
        private bool _autoUnitEnabled = true;
        private DynamicUnitTier _currentTier = DynamicUnitTier.Base;
        private double _tierScale = 1.0;
        private string _activeUnitStr = "m";
        private float _activeStep = 100f;

        // 速度变化率高精度微分采样 (Velocity Rate of Change: dV/dt)
        private double _lastSampleSpeed = double.NaN;
        private float _lastSampleTime = 0f;
        private double _calculatedAccelMps2 = 0.0;

        // 脏检查与状态缓存
        private double _lastRawVal = double.NaN;
        private double _lastTrendVal = double.NaN;
        private double _lastTerrainVal = double.NaN;
        private string _lastCenterText = string.Empty;
        private string _lastUnitText = string.Empty;
        private string _lastTopText = string.Empty;
        private string _lastBottomText = string.Empty;
        private string _lastTrendRateText = string.Empty;
        private string _lastAccText = string.Empty;
        private string _lastRateText = string.Empty;
        private bool _lastTrendPositive = true;
        private int _lastAccAlertLevel = -1;
        private bool _showingIntegerReadout = false;
        private float _currentHalfTrackH = 58f;

        public static Action OnCycleSpeedModeAction;
        public static Action OnCycleAltitudeModeAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x > 10f ? RectTransform.sizeDelta.x : (BaseSize.x * s);
            float height = RectTransform.sizeDelta.y > 10f ? RectTransform.sizeDelta.y : (BaseSize.y * s);
            RectTransform.sizeDelta = new Vector2(width, height);
            _currentHalfTrackH = Mathf.Max(30f * s, (height - 124f * s) * 0.5f);

            ParseCustomTemplate(config);
            InitializeUnitTier();

            // 1. 半透明防炫底板与外框
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _bgOutline.effectColor, s);

            // 2. 标尺视口 (裁剪超出范围的刻度)
            GameObject viewportObj = new GameObject("Tape_Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(transform, false);
            _viewportRt = viewportObj.GetComponent<RectTransform>();
            _viewportRt.sizeDelta = new Vector2(width, height - 12f * s);
            _viewportRt.anchoredPosition = Vector2.zero;

            // 刻度容器
            GameObject containerObj = new GameObject("Tick_Container", typeof(RectTransform));
            containerObj.transform.SetParent(_viewportRt, false);
            _tickContainer = containerObj.GetComponent<RectTransform>();
            _tickContainer.sizeDelta = _viewportRt.sizeDelta;
            _tickContainer.anchoredPosition = Vector2.zero;

            // 垂直精密导轨基线 (Backbone Rail: 贴合标尺刻度根部，柔和纤细)
            float railX = Config.IsLeftOrientation ? (21f * s) : (-21f * s);
            _backboneRailObj = UIFactory.CreatePanel(_tickContainer, "Backbone_Rail",
                new Vector2(1.0f * s, height - 12f * s), new Vector2(railX, 0f),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f));
            _backboneRail = _backboneRailObj.GetComponent<Image>();

            // 贴地雷达地形感知警戒带 (Radar Ground Ribbon)
            BuildGroundRibbon(theme);

            // 初始化激光光栅四级刻度对象池
            BuildTickPool(theme);

            // 视口端部镜面反光线与羽化遮罩 (Gloss Horizon Rim & Fade)
            BuildGlossRimsAndFades(theme);

            // 3. 中央高对比度实体读数窗口 (数值与单位直接合并并排展示)
            BuildCenterReadoutBox(theme);

            // 4. 顶部模式胶囊与底部次级航电窗 (Top Mode Capsule & Bottom Secondary Box)
            BuildCapsuleLabels(theme);

            // 5. 趋势指示器 (速度带内置 6 秒预测条 + 动力学微舱 / 高度带水平对齐 VSI)
            BuildTrendIndicator(theme);

            // 标准化组件内部控件注册至管理器 (0 影响原画质与排版)
            if (_bgImage != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "background", "标尺带底衬", _bgImage.gameObject);
            }
            if (_viewportRt != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "tape_viewport", "动态标尺刻度视口", _viewportRt.gameObject);
            }
            if (_centerBoxRt != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "center_readout", "中央实体读数窗", _centerBoxRt.gameObject);
            }
            if (_topModeBox != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "top_mode_capsule", "顶部模式胶囊", _topModeBox);
            }
            if (_bottomSecBox != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "bottom_sec_box", "底部次级航电窗", _bottomSecBox);
            }
            if (_trendRoot != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "trend_indicator", "动力学趋势指示柱", _trendRoot.gameObject);
            }
            if (_groundRibbonObj != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "ground_ribbon", "贴地雷达警戒带", _groundRibbonObj);
            }

            ApplyLayoutDimensions(width, height);
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayoutDimensions(pixelSize.x, pixelSize.y);
            if (!double.IsNaN(_lastRawVal))
            {
                UpdateRollingTape(_lastRawVal / _tierScale);
            }
        }

        private void ApplyLayoutDimensions(float width, float height)
        {
            float s = CurrentDpiScale;
            float halfH = height * 0.5f;

            if (_viewportRt != null)
            {
                _viewportRt.sizeDelta = new Vector2(width, height - 12f * s);
            }
            if (_tickContainer != null && _viewportRt != null)
            {
                _tickContainer.sizeDelta = _viewportRt.sizeDelta;
            }
            if (_backboneRailObj != null)
            {
                var rt = _backboneRailObj.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(1.0f * s, height - 12f * s);
            }
            if (_topGlossRim != null)
            {
                _topGlossRim.rectTransform.sizeDelta = new Vector2(width - 2f * s, 1f * s);
                _topGlossRim.rectTransform.anchoredPosition = new Vector2(0f, halfH - 1f * s);
            }
            if (_bottomGlossRim != null)
            {
                _bottomGlossRim.rectTransform.sizeDelta = new Vector2(width - 2f * s, 1f * s);
                _bottomGlossRim.rectTransform.anchoredPosition = new Vector2(0f, -halfH + 1f * s);
            }
            if (_topFadeImg != null)
            {
                _topFadeImg.rectTransform.sizeDelta = new Vector2(width, 16f * s);
                _topFadeImg.rectTransform.anchoredPosition = new Vector2(0f, halfH - 8f * s);
            }
            if (_bottomFadeImg != null)
            {
                _bottomFadeImg.rectTransform.sizeDelta = new Vector2(width, 16f * s);
                _bottomFadeImg.rectTransform.anchoredPosition = new Vector2(0f, -halfH + 8f * s);
            }
            if (_topModeBox != null)
            {
                var topRt = _topModeBox.GetComponent<RectTransform>();
                if (topRt != null)
                {
                    topRt.sizeDelta = new Vector2(width, 18f * s);
                    topRt.anchoredPosition = new Vector2(0f, halfH + 11f * s);
                }
            }
            if (_bottomSecBox != null)
            {
                var btmRt = _bottomSecBox.GetComponent<RectTransform>();
                if (btmRt != null)
                {
                    btmRt.sizeDelta = new Vector2(width, 16f * s);
                    btmRt.anchoredPosition = new Vector2(0f, -halfH - 10f * s);
                }
            }
            if (_trendRoot != null)
            {
                _trendRoot.sizeDelta = new Vector2(_trendRoot.sizeDelta.x, height);
                float trendX = _isSpeedTape ? -(width * 0.5f + 21f * s) : (width * 0.5f + 14f * s);
                _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

                float trackH = Mathf.Max(60f * s, height - 94f * s);
                _currentHalfTrackH = (trackH - 30f * s) * 0.5f;

                if (_isSpeedTape)
                {
                    if (_accTagBox != null)
                    {
                        var rt = _accTagBox.GetComponent<RectTransform>();
                        if (rt != null) rt.anchoredPosition = new Vector2(-9.5f * s, halfH - 17f * s);
                    }
                    if (_accTrackBgObj != null)
                    {
                        var rt = _accTrackBgObj.GetComponent<RectTransform>();
                        if (rt != null) rt.sizeDelta = new Vector2(14f * s, trackH);
                    }
                    if (_accTrack != null)
                    {
                        _accTrack.rectTransform.sizeDelta = new Vector2(1.2f * s, trackH - 12f * s);
                    }
                    if (_accTraceRt != null)
                    {
                        _accTraceRt.anchoredPosition = new Vector2(0f, -_currentHalfTrackH);
                    }

                    if (_rateTagBox != null)
                    {
                        var rt = _rateTagBox.GetComponent<RectTransform>();
                        if (rt != null) rt.anchoredPosition = new Vector2(9.5f * s, halfH - 17f * s);
                    }
                    if (_rateTrackBgObj != null)
                    {
                        var rt = _rateTrackBgObj.GetComponent<RectTransform>();
                        if (rt != null) rt.sizeDelta = new Vector2(14f * s, trackH);
                    }
                    if (_rateTrack != null)
                    {
                        _rateTrack.rectTransform.sizeDelta = new Vector2(1.2f * s, trackH - 12f * s);
                    }
                }
                else
                {
                    if (_vsiTagBox != null)
                    {
                        var rt = _vsiTagBox.GetComponent<RectTransform>();
                        if (rt != null) rt.anchoredPosition = new Vector2(0f, halfH - 17f * s);
                    }
                    if (_vsiTrackBgObj != null)
                    {
                        var rt = _vsiTrackBgObj.GetComponent<RectTransform>();
                        if (rt != null) rt.sizeDelta = new Vector2(14f * s, trackH);
                    }
                    if (_vsiTrack != null)
                    {
                        _vsiTrack.rectTransform.sizeDelta = new Vector2(1.2f * s, trackH - 12f * s);
                    }
                }
            }
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            bool isLeft = config != null && config.IsLeftOrientation;
            string numToken = config?.NumericToken ?? "";
            _isSpeedTape = isLeft || numToken.Contains("SPD");

            if (_isSpeedTape)
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{SPD}";
                _topModeTemplate = "{SPD:MODE}";
                _bottomSecTemplate = "{MACH}";
                _trendToken = "{GFORCE}";
                _trendTagTemplate = "ACC";
                _trendMaxScale = 8.0f;
            }
            else
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{ALT}";
                _topModeTemplate = "{ALT:MODE}";
                _bottomSecTemplate = "RDR {ALT:AGL:DIST}";
                _trendToken = "{VSI}";
                _trendTagTemplate = "V/S";
                _trendMaxScale = 100.0f;
                _terrainToken = "{ALT:AGL}";
            }

            if (!string.IsNullOrEmpty(config?.DisplayName))
            {
                if (config.DisplayName.Length <= 4 &&
                    !config.DisplayName.Contains(I18n.Tr("SUFFIX_SCALE_TAPE", "标尺带")) &&
                    config.DisplayName != "SPD" && config.DisplayName != "ALT")
                {
                    _topModeTemplate = config.DisplayName;
                }
            }

            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "VAL":
                    case "VALUE":
                    case "TOKEN":
                        _valueToken = v;
                        break;
                    case "TOP":
                    case "MODE":
                    case "TOP_LABEL":
                        _topModeTemplate = v;
                        break;
                    case "BOTTOM":
                    case "BOTTOM_LABEL":
                    case "SEC":
                        _bottomSecTemplate = v;
                        break;
                    case "TREND_VAL":
                    case "TREND_TOKEN":
                        _trendToken = v;
                        break;
                    case "TERRAIN":
                    case "AGL_TOKEN":
                        _terrainToken = v;
                        break;
                    case "TREND_MAX":
                        if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maxS))
                            _trendMaxScale = maxS;
                        break;
                    case "AUTO_UNIT":
                    case "UNIT_AUTO":
                        if (bool.TryParse(v, out bool autoU))
                            _autoUnitEnabled = autoU;
                        break;
                    case "UNIT_MODE":
                        if (v.Equals("FIXED", StringComparison.OrdinalIgnoreCase))
                            _autoUnitEnabled = false;
                        else if (v.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                            _autoUnitEnabled = true;
                        break;
                }
            }
        }

        private void InitializeUnitTier()
        {
            _currentTier = DynamicUnitTier.Base;
            _tierScale = 1.0;
            _activeUnitStr = _isSpeedTape ? "m/s" : "m";
            _activeStep = Config != null && Config.StepInterval > 0f ? Config.StepInterval : (_isSpeedTape ? 10f : 100f);
        }

        private void BuildGroundRibbon(ThemeConfig theme)
        {
            if (_isSpeedTape) return;

            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x;
            _groundRibbonObj = UIFactory.CreatePanel(_viewportRt, "Ground_Ribbon",
                new Vector2(width - 6f * s, 0f), Vector2.zero,
                WidgetStyleManager.Meter(MeterStyleRole.Danger, theme));
            _groundRibbonRt = _groundRibbonObj.GetComponent<RectTransform>();
            _groundRibbonRt.pivot = new Vector2(0.5f, 0f);
            _groundRibbonImg = _groundRibbonObj.GetComponent<Image>();
            _groundRibbonImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Warning, theme), 0.45f);
            _groundRibbonObj.SetActive(false);
        }

        private void BuildGlossRimsAndFades(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float w = RectTransform.sizeDelta.x;
            float halfH = (_viewportRt.sizeDelta.y) * 0.5f;

            GameObject topRimObj = UIFactory.CreatePanel(_viewportRt, "Top_Gloss_Rim",
                new Vector2(w - 2f * s, 1f * s), new Vector2(0f, halfH - 1f * s),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f));
            _topGlossRim = topRimObj.GetComponent<Image>();

            GameObject btmRimObj = UIFactory.CreatePanel(_viewportRt, "Btm_Gloss_Rim",
                new Vector2(w - 2f * s, 1f * s), new Vector2(0f, -halfH + 1f * s),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f));
            _bottomGlossRim = btmRimObj.GetComponent<Image>();

            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);
            GameObject topFade = UIFactory.CreatePanel(_viewportRt, "Top_Edge_Fade",
                new Vector2(w, 16f * s), new Vector2(0f, halfH - 8f * s),
                WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.70f));
            _topFadeImg = topFade.GetComponent<Image>();
            _topFadeImg.raycastTarget = false;

            GameObject btmFade = UIFactory.CreatePanel(_viewportRt, "Btm_Edge_Fade",
                new Vector2(w, 16f * s), new Vector2(0f, -halfH + 8f * s),
                WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.70f));
            _bottomFadeImg = btmFade.GetComponent<Image>();
            _bottomFadeImg.raycastTarget = false;
        }

        private void BuildTickPool(ThemeConfig theme)
        {
            _tickPool.Clear();
            float s = CurrentDpiScale;
            float railX = Config.IsLeftOrientation ? (21f * s) : (-21f * s);
            Vector2 tickPivot = Config.IsLeftOrientation ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            TextAnchor align = Config.IsLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            Vector2 textPivot = Config.IsLeftOrientation ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            float labelX = Config.IsLeftOrientation ? (10f * s) : (-10f * s);

            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color majorCol = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.60f);

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                GameObject itemObj = new GameObject($"Tick_{i}", typeof(RectTransform));
                itemObj.transform.SetParent(_tickContainer, false);
                RectTransform rt = itemObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(48f * s, 16f * s);

                // 刻度线 (精致航电级纤细微线，长齿 7.5px，粗细 1.2px)
                GameObject lineObj = new GameObject("Tick_Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(itemObj.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.pivot = tickPivot;
                lineRt.sizeDelta = new Vector2(7.5f * s, 1.2f * s);
                lineRt.anchoredPosition = new Vector2(railX, 0f);
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = majorCol;

                // 刻度数字标牌 (整齐呼吸间距，严禁贴面与遮挡)
                int fontSize = Mathf.Max(9, Mathf.RoundToInt(10.5f * s));
                Text labelTxt = UIFactory.CreateText(itemObj.transform, "Tick_Text", "0", fontSize, align,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                labelTxt.fontStyle = FontStyle.Bold;
                RectTransform labelRt = labelTxt.GetComponent<RectTransform>();
                labelRt.pivot = textPivot;
                labelRt.sizeDelta = new Vector2(32f * s, 16f * s);
                labelRt.anchoredPosition = new Vector2(labelX, 0f);

                _tickPool.Add(new TickItem
                {
                    Root = itemObj,
                    Rect = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = labelTxt,
                    LabelRt = labelRt
                });
            }
        }

        private void BuildCenterReadoutBox(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float boxW = 54f * s;
            float boxH = 22f * s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            GameObject boxObj = new GameObject("Center_Readout_Box", typeof(RectTransform), typeof(Image), typeof(Button));
            boxObj.transform.SetParent(transform, false);
            _centerBoxRt = boxObj.GetComponent<RectTransform>();
            _centerBoxRt.sizeDelta = new Vector2(boxW, boxH);

            float offsetX = Config.IsLeftOrientation ? (2f * s) : (-2f * s);
            _centerBoxRt.anchoredPosition = new Vector2(offsetX, 0f);

            _centerBoxBg = boxObj.GetComponent<Image>();
            _centerBoxOutline = boxObj.AddComponent<Outline>();
            _centerBoxOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.Normal, theme);

            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);
            _centerBoxBg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 1.0f);
            _centerBoxOutline.effectColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            _centerBoxBtn = boxObj.GetComponent<Button>();
            if (_centerBoxBtn != null)
            {
                _centerBoxBtn.onClick.AddListener(OnBoxClicked);
            }

            // 1. 一体化五边形指针凸嘴 (Chevron Pointer Arrowhead)
            GameObject arrowObj = new GameObject("Pointer_Chevron", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(boxObj.transform, false);
            _pointerArrowRt = arrowObj.GetComponent<RectTransform>();
            _pointerArrowRt.sizeDelta = new Vector2(6f * s, 6f * s);
            float arrowX = Config.IsLeftOrientation ? (boxW * 0.5f - 1f * s) : (-boxW * 0.5f + 1f * s);
            _pointerArrowRt.anchoredPosition = new Vector2(arrowX, 0f);
            _pointerArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _pointerArrowImg = arrowObj.GetComponent<Image>();
            _pointerArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 2. 发光瞄准发丝基准线 (Luminescent Index Ray)
            GameObject rayObj = new GameObject("Hairline_Ray", typeof(RectTransform), typeof(Image));
            rayObj.transform.SetParent(boxObj.transform, false);
            _hairlineRayRt = rayObj.GetComponent<RectTransform>();
            _hairlineRayRt.sizeDelta = new Vector2(8f * s, 1f * s);
            float rayX = Config.IsLeftOrientation ? (boxW * 0.5f + 3f * s) : (-boxW * 0.5f - 3f * s);
            _hairlineRayRt.anchoredPosition = new Vector2(rayX, 0f);
            _hairlineRayImg = rayObj.GetComponent<Image>();
            _hairlineRayImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 3. 机械沉降微分割缝 (Odometer Seam)
            float seamX = Config.IsLeftOrientation ? (boxW * 0.16f) : (-boxW * 0.16f);
            GameObject seamObj = UIFactory.CreatePanel(boxObj.transform, "Seam_Slit",
                new Vector2(1f * s, boxH - 6f * s), new Vector2(seamX, 0f),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Track, theme), 0.45f));
            _centerSeamImg = seamObj.GetComponent<Image>();

            // 4. 精密 CNC 卡槽角标微点
            _cornerAccents.Clear();
            float[] cx = new float[] { -boxW * 0.5f + 2f * s, boxW * 0.5f - 2f * s };
            float[] cy = new float[] { -boxH * 0.5f + 2f * s, boxH * 0.5f - 2f * s };
            foreach (float x in cx)
            {
                foreach (float y in cy)
                {
                    GameObject cObj = UIFactory.CreatePanel(boxObj.transform, "Corner_Dot",
                        new Vector2(1.5f * s, 1.5f * s), new Vector2(x, y),
                        WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.75f));
                    _cornerAccents.Add(cObj.GetComponent<Image>());
                }
            }

            // 5. 双列并排排版：数值紧贴单位 (字号与间距精心微调)
            int valFontSize = Mathf.RoundToInt(13.5f * s);
            int unitFontSize = Mathf.Max(7, Mathf.RoundToInt(8f * s));

            if (Config.IsLeftOrientation)
            {
                // 速度带：数值靠左 (右对齐至 +4), 单位紧随其右 (左对齐从 +5.5 开始)
                _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize,
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _centerValueText.fontStyle = FontStyle.Bold;
                _centerValueText.alignByGeometry = false;
                RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
                valRt.pivot = new Vector2(1f, 0.5f);
                valRt.sizeDelta = new Vector2(36f * s, boxH);
                valRt.anchoredPosition = new Vector2(4f * s, 0f);

                _centerUnitText = UIFactory.CreateText(boxObj.transform, "Readout_Unit", _activeUnitStr, unitFontSize,
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, theme));
                _centerUnitText.fontStyle = FontStyle.Bold;
                _centerUnitText.alignByGeometry = false;
                RectTransform unitRt = _centerUnitText.GetComponent<RectTransform>();
                unitRt.pivot = new Vector2(0f, 0.5f);
                unitRt.sizeDelta = new Vector2(18f * s, boxH);
                unitRt.anchoredPosition = new Vector2(5.5f * s, -0.5f * s);
            }
            else
            {
                // 高度带：数值靠左 (右对齐至 +6), 单位紧随其右 (左对齐从 +7.5 开始)
                _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize,
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _centerValueText.fontStyle = FontStyle.Bold;
                _centerValueText.alignByGeometry = false;
                RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
                valRt.pivot = new Vector2(1f, 0.5f);
                valRt.sizeDelta = new Vector2(36f * s, boxH);
                valRt.anchoredPosition = new Vector2(6f * s, 0f);

                _centerUnitText = UIFactory.CreateText(boxObj.transform, "Readout_Unit", _activeUnitStr, unitFontSize,
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, theme));
                _centerUnitText.fontStyle = FontStyle.Bold;
                _centerUnitText.alignByGeometry = false;
                RectTransform unitRt = _centerUnitText.GetComponent<RectTransform>();
                unitRt.pivot = new Vector2(0f, 0.5f);
                unitRt.sizeDelta = new Vector2(17f * s, boxH);
                unitRt.anchoredPosition = new Vector2(7.5f * s, -0.5f * s);
            }
        }

        private void BuildCapsuleLabels(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float halfH = RectTransform.sizeDelta.y * 0.5f;
            float w = RectTransform.sizeDelta.x;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 顶部模式微胶囊盒 (Top Mode Capsule)
            Vector2 topBoxSize = new Vector2(w, 18f * s);
            Vector2 topBoxPos = new Vector2(0f, halfH + 11f * s);
            _topModeBox = UIFactory.CreatePanel(transform, "Top_Mode_Box", topBoxSize, topBoxPos, Color.clear);
            _topModeBg = _topModeBox.GetComponent<Image>();
            _topModeOutline = _topModeBox.AddComponent<Outline>();
            _topModeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_topModeBg, _topModeOutline, CardStyleRole.Normal, theme);

            _topModeBtn = _topModeBox.AddComponent<Button>();
            _topModeBtn.onClick.AddListener(OnBoxClicked);

            // 通电 LED 指示灯微标
            GameObject ledObj = UIFactory.CreatePanel(_topModeBox.transform, "LED_Dot",
                new Vector2(3f * s, 3f * s), new Vector2(-w * 0.5f + 6f * s, 0f),
                WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
            _topLedDot = ledObj.GetComponent<Image>();

            int topFontSize = Mathf.RoundToInt(8.5f * s);
            _topModeText = UIFactory.CreateText(_topModeBox.transform, "Top_Mode", _topModeTemplate, topFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            _topModeText.resizeTextForBestFit = true;
            _topModeText.resizeTextMinSize = Mathf.RoundToInt(6f * s);
            _topModeText.resizeTextMaxSize = topFontSize;
            _topModeText.fontStyle = FontStyle.Bold;
            _topModeText.alignByGeometry = false;
            RectTransform topRt = _topModeText.GetComponent<RectTransform>();
            topRt.sizeDelta = new Vector2(w - 12f * s, topBoxSize.y);
            topRt.anchoredPosition = new Vector2(2f * s, 0f);
            _topModeText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 2. 底部次级航电窗 (Bottom Secondary Box: Mach / Radar Alt)
            Vector2 btmBoxSize = new Vector2(w, 16f * s);
            Vector2 btmBoxPos = new Vector2(0f, -halfH - 10f * s);
            _bottomSecBox = UIFactory.CreatePanel(transform, "Bottom_Sec_Box", btmBoxSize, btmBoxPos, Color.clear);
            _bottomSecBg = _bottomSecBox.GetComponent<Image>();
            _bottomSecOutline = _bottomSecBox.AddComponent<Outline>();
            _bottomSecOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bottomSecBg, _bottomSecOutline, CardStyleRole.SubtleSlot, theme);

            int btmFontSize = Mathf.RoundToInt(8f * s);
            _bottomSecText = UIFactory.CreateText(_bottomSecBox.transform, "Bottom_Sec", "---", btmFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform btmRt = _bottomSecText.GetComponent<RectTransform>();
            btmRt.sizeDelta = btmBoxSize;
            btmRt.anchoredPosition = Vector2.zero;
            _bottomSecText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void BuildTrendIndicator(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_isSpeedTape)
            {
                // 速度带双列紧凑并排动力学柱：左列 ACC (G载荷) + 右列 dV/dt (速度变化率)
                // 采用航空级精密 -> 指针式 (Arrow Needle) 设计，彻底替代厚重填色块
                // 挂载于速度带左翼：总宽度 38px，中心距带边缘 21px (x = -(width * 0.5f + 21f * s))
                float trendX = -(width * 0.5f + 21f * s);
                float trendW = 38f * s;

                GameObject root = new GameObject("Speed_Dynamics_Root", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                _trendRoot = root.GetComponent<RectTransform>();
                _trendRoot.sizeDelta = new Vector2(trendW, 240f * s);
                _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

                float colW = 18f * s;
                float leftColX = -9.5f * s;
                float rightColX = +9.5f * s;
                Vector2 trackSize = new Vector2(14f * s, 146f * s);

                // ==================== 左列：ACC (G 载荷单极柱 0~4G，──► 指针式) ====================
                // 1. ACC 顶部微标牌盒
                Vector2 accBoxSize = new Vector2(colW, 26f * s);
                Vector2 accBoxPos = new Vector2(leftColX, 103f * s);
                _accTagBox = UIFactory.CreatePanel(_trendRoot.transform, "ACC_Tag_Box", accBoxSize, accBoxPos, Color.clear);
                _accTagBg = _accTagBox.GetComponent<Image>();
                _accTagOutline = _accTagBox.AddComponent<Outline>();
                _accTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_accTagBg, _accTagOutline, CardStyleRole.Normal, theme);

                _accTagText = UIFactory.CreateText(_accTagBox.transform, "ACC_Tag", "ACC",
                    Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.Cardinal, theme));
                _accTagText.fontStyle = FontStyle.Bold;
                RectTransform accTagRt = _accTagText.GetComponent<RectTransform>();
                accTagRt.sizeDelta = new Vector2(colW, 9f * s);
                accTagRt.anchoredPosition = new Vector2(0f, 6.5f * s);

                _accValText = UIFactory.CreateText(_accTagBox.transform, "ACC_Val", "1.0G",
                    Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _accValText.fontStyle = FontStyle.Bold;
                RectTransform accValRt = _accValText.GetComponent<RectTransform>();
                accValRt.sizeDelta = new Vector2(colW, 10f * s);
                accValRt.anchoredPosition = new Vector2(0f, -4f * s);

                // 2. ACC 垂直计量槽 (底端 0G，顶端 4G)
                _accTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "ACC_Track_Bg", trackSize, new Vector2(leftColX, 0f), Color.clear);
                _accTrackBg = _accTrackBgObj.GetComponent<Image>();
                _accTrackOutline = _accTrackBgObj.AddComponent<Outline>();
                _accTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_accTrackBg, _accTrackOutline, CardStyleRole.SubtleSlot, theme);

                // 垂直基准轨道线
                GameObject accTrackLine = UIFactory.CreatePanel(_accTrackBgObj.transform, "ACC_Rail",
                    new Vector2(1.2f * s, 134f * s), Vector2.zero,
                    style.GetMeterColor(MeterStyleRole.Track, theme));
                _accTrack = accTrackLine.GetComponent<Image>();

                // 标尺刻度线 (0G: -58, 2G: -29, 4G: 0 [黄色告警门限], 6G: +29, 8G: +58 [红色极危门限])
                Color trackTickCol = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);
                float[] accTicks = new float[] { -58f * s, -29f * s, 0f, 29f * s, 58f * s };
                for (int t = 0; t < accTicks.Length; t++)
                {
                    float yPos = accTicks[t];
                    // 0G (t=0), 4G (t=2, 中间告警门限), 8G (t=4, 顶端过载门限) 采用长刻线 (5.5px)，2G/6G 采用短刻线 (3.5px)
                    float tickLen = (t % 2 == 0) ? (5.5f * s) : (3.5f * s);
                    UIFactory.CreatePanel(_accTrackBgObj.transform, $"ACC_Tick_{t}",
                        new Vector2(tickLen, 1f * s), new Vector2(0f, yPos), trackTickCol);
                }

                // ACC 优雅微痕发丝轨迹 (Trace Hairline from 0G up to pointer)
                GameObject accTraceObj = new GameObject("ACC_Trace", typeof(RectTransform), typeof(Image));
                accTraceObj.transform.SetParent(_accTrackBgObj.transform, false);
                _accTraceRt = accTraceObj.GetComponent<RectTransform>();
                _accTraceRt.sizeDelta = new Vector2(1.2f * s, 29f * s);
                _accTraceRt.pivot = new Vector2(0.5f, 0f);
                _accTraceRt.anchoredPosition = new Vector2(0f, -58f * s);
                _accTraceImg = accTraceObj.GetComponent<Image>();
                _accTraceImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.40f);

                // ACC 航空级 ──► 指针 (Needle Pointer pointing right towards rail/ticks)
                GameObject accPointerObj = new GameObject("ACC_Pointer", typeof(RectTransform));
                accPointerObj.transform.SetParent(_accTrackBgObj.transform, false);
                _accPointerRt = accPointerObj.GetComponent<RectTransform>();
                _accPointerRt.sizeDelta = new Vector2(9f * s, 6f * s);
                _accPointerRt.anchoredPosition = new Vector2(0f, -29f * s);

                GameObject accStem = UIFactory.CreatePanel(_accPointerRt.transform, "Stem",
                    new Vector2(5.5f * s, 1.5f * s), new Vector2(-1.5f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                _accPointerStem = accStem.GetComponent<Image>();

                GameObject accHead = UIFactory.CreatePanel(_accPointerRt.transform, "Head",
                    new Vector2(4.5f * s, 4.5f * s), new Vector2(2f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                RectTransform accHeadRt = accHead.GetComponent<RectTransform>();
                accHeadRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _accPointerHead = accHead.GetComponent<Image>();

                // ==================== 右列：dV/dt (速度变化率双极柱 ±20 m/s²，──► 指针式) ====================
                // 1. dV/dt 顶部微标牌盒
                Vector2 rateBoxSize = new Vector2(colW, 26f * s);
                Vector2 rateBoxPos = new Vector2(rightColX, 103f * s);
                _rateTagBox = UIFactory.CreatePanel(_trendRoot.transform, "Rate_Tag_Box", rateBoxSize, rateBoxPos, Color.clear);
                _rateTagBg = _rateTagBox.GetComponent<Image>();
                _rateTagOutline = _rateTagBox.AddComponent<Outline>();
                _rateTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_rateTagBg, _rateTagOutline, CardStyleRole.Normal, theme);

                _rateTagText = UIFactory.CreateText(_rateTagBox.transform, "Rate_Tag", I18n.Tr("WIDGET_GAUGE_DV_DT", "变化率"),
                    Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.Unit, theme));
                _rateTagText.fontStyle = FontStyle.Bold;
                RectTransform rateTagRt = _rateTagText.GetComponent<RectTransform>();
                rateTagRt.sizeDelta = new Vector2(colW, 9f * s);
                rateTagRt.anchoredPosition = new Vector2(0f, 6.5f * s);

                _rateValText = UIFactory.CreateText(_rateTagBox.transform, "Rate_Val", "+0.0",
                    Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.Accent, theme));
                _rateValText.fontStyle = FontStyle.Bold;
                RectTransform rateValRt = _rateValText.GetComponent<RectTransform>();
                rateValRt.sizeDelta = new Vector2(colW, 10f * s);
                rateValRt.anchoredPosition = new Vector2(0f, -4f * s);

                // 2. dV/dt 垂直计量槽 (中央 y = 0 为 0 m/s²，双极对称)
                _rateTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "Rate_Track_Bg", trackSize, new Vector2(rightColX, 0f), Color.clear);
                _rateTrackBg = _rateTrackBgObj.GetComponent<Image>();
                _rateTrackOutline = _rateTrackBgObj.AddComponent<Outline>();
                _rateTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_rateTrackBg, _rateTrackOutline, CardStyleRole.SubtleSlot, theme);

                // 垂直轨道基准线
                GameObject rateTrackLine = UIFactory.CreatePanel(_rateTrackBgObj.transform, "Rate_Rail",
                    new Vector2(1.2f * s, 134f * s), Vector2.zero,
                    style.GetMeterColor(MeterStyleRole.Track, theme));
                _rateTrack = rateTrackLine.GetComponent<Image>();

                // 水平零位基准刻线 (Zero Datum Notch at y = 0)
                Color trackDatumCol = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.60f);
                GameObject rateZeroObj = UIFactory.CreatePanel(_rateTrackBgObj.transform, "Rate_ZeroAnchor",
                    new Vector2(6f * s, 1.2f * s), Vector2.zero, trackDatumCol);
                _rateZeroAnchor = rateZeroObj.GetComponent<Image>();

                // 辅助刻度线 (±10 m/s²: ±29, ±20 m/s²: ±58)
                float[] rateTicks = new float[] { 29f * s, 58f * s, -29f * s, -58f * s };
                foreach (float yOff in rateTicks)
                {
                    UIFactory.CreatePanel(_rateTrackBgObj.transform, $"Rate_Tick_{yOff:F0}",
                        new Vector2(4f * s, 1f * s), new Vector2(0f, yOff), trackTickCol);
                }

                // dV/dt 优雅微痕发丝轨迹 (Trace Hairline from zero to pointer)
                GameObject rateTraceObj = new GameObject("Rate_Trace", typeof(RectTransform), typeof(Image));
                rateTraceObj.transform.SetParent(_rateTrackBgObj.transform, false);
                _rateTraceRt = rateTraceObj.GetComponent<RectTransform>();
                _rateTraceRt.sizeDelta = new Vector2(1.2f * s, 0f);
                _rateTraceRt.pivot = new Vector2(0.5f, 0f);
                _rateTraceRt.anchoredPosition = Vector2.zero;
                _rateTraceImg = rateTraceObj.GetComponent<Image>();
                _rateTraceImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.40f);

                // dV/dt 航空级 ──► 指针 (Needle Pointer)
                GameObject ratePointerObj = new GameObject("Rate_Pointer", typeof(RectTransform));
                ratePointerObj.transform.SetParent(_rateTrackBgObj.transform, false);
                _ratePointerRt = ratePointerObj.GetComponent<RectTransform>();
                _ratePointerRt.sizeDelta = new Vector2(9f * s, 6f * s);
                _ratePointerRt.anchoredPosition = Vector2.zero;

                GameObject rateStem = UIFactory.CreatePanel(_ratePointerRt.transform, "Stem",
                    new Vector2(5.5f * s, 1.5f * s), new Vector2(-1.5f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                _ratePointerStem = rateStem.GetComponent<Image>();

                GameObject rateHead = UIFactory.CreatePanel(_ratePointerRt.transform, "Head",
                    new Vector2(4.5f * s, 4.5f * s), new Vector2(2f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                RectTransform rateHeadRt = rateHead.GetComponent<RectTransform>();
                rateHeadRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _ratePointerHead = rateHead.GetComponent<Image>();
            }
            else
            {
                // ==================== 高度带侧边：VSI 精密升降率单列柱 (◄── 指针式) ====================
                float trendX = width * 0.5f + 14f * s;
                float trendW = 18f * s;

                GameObject root = new GameObject("VSI_Dynamics_Root", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                _trendRoot = root.GetComponent<RectTransform>();
                _trendRoot.sizeDelta = new Vector2(trendW, 240f * s);
                _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

                Vector2 boxSize = new Vector2(trendW, 26f * s);
                Vector2 boxPos = new Vector2(0f, 103f * s);

                _vsiTagBox = UIFactory.CreatePanel(_trendRoot.transform, "VSI_Tag_Box", boxSize, boxPos, Color.clear);
                _vsiTagBg = _vsiTagBox.GetComponent<Image>();
                _vsiTagOutline = _vsiTagBox.AddComponent<Outline>();
                _vsiTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_vsiTagBg, _vsiTagOutline, CardStyleRole.Normal, theme);

                _vsiTagText = UIFactory.CreateText(_vsiTagBox.transform, "VSI_Tag_Title", _trendTagTemplate,
                    Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.Cardinal, theme));
                _vsiTagText.fontStyle = FontStyle.Bold;
                RectTransform tagRt = _vsiTagText.GetComponent<RectTransform>();
                tagRt.sizeDelta = new Vector2(boxSize.x, 9f * s);
                tagRt.anchoredPosition = new Vector2(0f, 6.5f * s);

                _vsiRateText = UIFactory.CreateText(_vsiTagBox.transform, "VSI_Rate_Val", "0.0",
                    Mathf.Max(6, Mathf.RoundToInt(7.0f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _vsiRateText.fontStyle = FontStyle.Bold;
                RectTransform rateRt = _vsiRateText.GetComponent<RectTransform>();
                rateRt.sizeDelta = new Vector2(boxSize.x, 10f * s);
                rateRt.anchoredPosition = new Vector2(0f, -4f * s);

                Vector2 trackBgSize = new Vector2(14f * s, 146f * s);
                _vsiTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "VSI_Track_Bg", trackBgSize, Vector2.zero, Color.clear);
                _vsiTrackBg = _vsiTrackBgObj.GetComponent<Image>();
                _vsiTrackOutline = _vsiTrackBgObj.AddComponent<Outline>();
                _vsiTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_vsiTrackBg, _vsiTrackOutline, CardStyleRole.SubtleSlot, theme);

                GameObject trackObj = UIFactory.CreatePanel(_vsiTrackBgObj.transform, "VSI_Track",
                    new Vector2(1.2f * s, 134f * s), Vector2.zero,
                    style.GetMeterColor(MeterStyleRole.Track, theme));
                _vsiTrack = trackObj.GetComponent<Image>();

                // 水平零位基准刻线 (Zero Datum Notch at y = 0)
                GameObject zeroObj = UIFactory.CreatePanel(_vsiTrackBgObj.transform, "VSI_ZeroAnchor",
                    new Vector2(6f * s, 1.2f * s), Vector2.zero,
                    WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.60f));
                _vsiZeroAnchor = zeroObj.GetComponent<Image>();

                Color trackTickCol = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);
                float[] tickOffsets = new float[] { 29f * s, 58f * s, -29f * s, -58f * s };
                foreach (float yOff in tickOffsets)
                {
                    UIFactory.CreatePanel(_vsiTrackBgObj.transform, $"VSI_Tick_{yOff:F0}",
                        new Vector2(4f * s, 1f * s), new Vector2(0f, yOff), trackTickCol);
                }

                // VSI 优雅微痕发丝轨迹
                GameObject vsiTraceObj = new GameObject("VSI_Trace", typeof(RectTransform), typeof(Image));
                vsiTraceObj.transform.SetParent(_vsiTrackBgObj.transform, false);
                _vsiTraceRt = vsiTraceObj.GetComponent<RectTransform>();
                _vsiTraceRt.sizeDelta = new Vector2(1.2f * s, 0f);
                _vsiTraceRt.pivot = new Vector2(0.5f, 0f);
                _vsiTraceRt.anchoredPosition = Vector2.zero;
                _vsiTraceImg = vsiTraceObj.GetComponent<Image>();
                _vsiTraceImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.40f);

                // VSI 航空级 ◄── 指针 (Needle Pointer pointing left towards altitude tape)
                GameObject vsiPointerObj = new GameObject("VSI_Pointer", typeof(RectTransform));
                vsiPointerObj.transform.SetParent(_vsiTrackBgObj.transform, false);
                _vsiPointerRt = vsiPointerObj.GetComponent<RectTransform>();
                _vsiPointerRt.sizeDelta = new Vector2(9f * s, 6f * s);
                _vsiPointerRt.anchoredPosition = Vector2.zero;

                GameObject vsiStem = UIFactory.CreatePanel(_vsiPointerRt.transform, "Stem",
                    new Vector2(5.5f * s, 1.5f * s), new Vector2(1.5f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                _vsiPointerStem = vsiStem.GetComponent<Image>();

                GameObject vsiHead = UIFactory.CreatePanel(_vsiPointerRt.transform, "Head",
                    new Vector2(4.5f * s, 4.5f * s), new Vector2(-2f * s, 0f),
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                RectTransform vsiHeadRt = vsiHead.GetComponent<RectTransform>();
                vsiHeadRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _vsiPointerHead = vsiHead.GetComponent<Image>();
            }
        }

        private void OnBoxClicked()
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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 采样主驱动数值 (双精度避免大数值失真)
            double rawVal = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(rawVal)) rawVal = 0.0;

            // 1. 运行太空全场景动态无极工程量纲自愈引擎 (带 15% 滞后死区)
            UpdateDynamicUnitTier(rawVal);

            // 2. 脏标记检查并更新主刻度与读数
            double deltaThreshold = Config != null && Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.02;
            if (double.IsNaN(_lastRawVal) || Math.Abs(rawVal - _lastRawVal) > deltaThreshold)
            {
                _lastRawVal = rawVal;
                double displayVal = rawVal / _tierScale;
                UpdateCenterReadout(displayVal);
                UpdateRollingTape(displayVal);
            }

            // 3. 动态求值并更新模式与次级航电标签
            UpdateLabels(telemetry);

            // 4. 动态更新趋势指示器 (6秒空速预测 + 速度变化率 dV/dt 或水平对齐 VSI)
            UpdateDynamicTrendIndicator(telemetry, rawVal);

            // 5. 贴地雷达地形感知警戒带
            UpdateTerrainRibbon(telemetry, rawVal);
        }

        private void UpdateDynamicUnitTier(double rawVal)
        {
            if (!_autoUnitEnabled) return;

            double abs = Math.Abs(rawVal);
            DynamicUnitTier nextTier = _currentTier;

            if (_isSpeedTape)
            {
                // 速度量纲阶梯: m/s (Base) -> km/s (Kilo) -> c (Mega)
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        if (abs >= 10000.0) nextTier = DynamicUnitTier.Kilo;
                        break;
                    case DynamicUnitTier.Kilo:
                        if (abs >= 3000000.0) nextTier = DynamicUnitTier.Mega;
                        else if (abs < 8500.0) nextTier = DynamicUnitTier.Base;
                        break;
                    case DynamicUnitTier.Mega:
                        if (abs < 2500000.0) nextTier = DynamicUnitTier.Kilo;
                        break;
                }
            }
            else
            {
                // 距离/高度量纲阶梯: m (Base) -> km (Kilo) -> Mm (Mega) -> Gm (Giga)
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        if (abs >= 10000.0) nextTier = DynamicUnitTier.Kilo;
                        break;
                    case DynamicUnitTier.Kilo:
                        if (abs >= 10000000.0) nextTier = DynamicUnitTier.Mega;
                        else if (abs < 8500.0) nextTier = DynamicUnitTier.Base;
                        break;
                    case DynamicUnitTier.Mega:
                        if (abs >= 10000000000.0) nextTier = DynamicUnitTier.Giga;
                        else if (abs < 8500000.0) nextTier = DynamicUnitTier.Kilo;
                        break;
                    case DynamicUnitTier.Giga:
                        if (abs < 8500000000.0) nextTier = DynamicUnitTier.Mega;
                        break;
                }
            }

            if (nextTier != _currentTier)
            {
                _currentTier = nextTier;
                ApplyTierParameters();
            }
        }

        private void ApplyTierParameters()
        {
            if (_isSpeedTape)
            {
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        _tierScale = 1.0;
                        _activeUnitStr = "m/s";
                        _activeStep = Config != null && Config.StepInterval > 0f ? Config.StepInterval : 10f;
                        break;
                    case DynamicUnitTier.Kilo:
                        _tierScale = 1000.0;
                        _activeUnitStr = "km/s";
                        _activeStep = 1.0f;
                        break;
                    case DynamicUnitTier.Mega:
                        _tierScale = 299792458.0;
                        _activeUnitStr = "c";
                        _activeStep = 0.01f;
                        break;
                }
            }
            else
            {
                switch (_currentTier)
                {
                    case DynamicUnitTier.Base:
                        _tierScale = 1.0;
                        _activeUnitStr = "m";
                        _activeStep = Config != null && Config.StepInterval > 0f ? Config.StepInterval : 100f;
                        break;
                    case DynamicUnitTier.Kilo:
                        _tierScale = 1000.0;
                        _activeUnitStr = "km";
                        _activeStep = 1.0f;
                        break;
                    case DynamicUnitTier.Mega:
                        _tierScale = 1000000.0;
                        _activeUnitStr = "Mm";
                        _activeStep = 0.5f;
                        break;
                    case DynamicUnitTier.Giga:
                        _tierScale = 1000000000.0;
                        _activeUnitStr = "Gm";
                        _activeStep = 0.1f;
                        break;
                }
            }

            SetTextIfChanged(_centerUnitText, _activeUnitStr);
        }

        private void UpdateCenterReadout(double displayVal)
        {
            string formatted;
            if (_currentTier == DynamicUnitTier.Mega && _isSpeedTape)
            {
                formatted = $"{displayVal:F2}";
            }
            else
            {
                double abs = Math.Abs(displayVal);
                // 迟滞死区：>= 1000 显示整数，防止在 999.9 与 1000 临界点高频跳变
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

        private void UpdateLabels(IFlightTelemetry telemetry)
        {
            string evalTop = TelemetryTokenEngine.Evaluate(_topModeTemplate, telemetry);
            if (evalTop != _lastTopText)
            {
                _lastTopText = evalTop;
                if (_topModeText != null) _topModeText.text = evalTop;
            }

            string evalSec = TelemetryTokenEngine.Evaluate(_bottomSecTemplate, telemetry);
            if (evalSec != _lastBottomText)
            {
                _lastBottomText = evalSec;
                if (_bottomSecText != null) _bottomSecText.text = evalSec;
            }
        }

        private void UpdateRollingTape(double currentDisplayVal)
        {
            float step = _activeStep > 0f ? _activeStep : 100f;
            // 航空级极简双层刻度步长：仅保留主刻度与半步长中刻度 (subStep = step * 0.5f)，彻底消除密集梳子齿
            float subStep = step * 0.5f;
            float pixelsPerUnit = (28f * CurrentDpiScale) / step;
            float visibleHalfSpan = (_viewportRt.sizeDelta.y * 0.5f) / pixelsPerUnit;

            double startTick = Math.Floor((currentDisplayVal - visibleHalfSpan) / subStep) * subStep;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            Color borderCol = theme.FrameBorderColor.ToColor();
            Color majorCol = WidgetStyleManager.WithAlpha(borderCol, 0.60f);
            Color halfCol = WidgetStyleManager.WithAlpha(borderCol, 0.30f);

            float s = CurrentDpiScale;

            for (int i = 0; i < _tickPool.Count; i++)
            {
                TickItem item = _tickPool[i];
                double tickVal = startTick + i * subStep;

                if (tickVal < 0.0 && _isSpeedTape)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                float y = Mathf.Round((float)(tickVal - currentDisplayVal) * pixelsPerUnit);
                if (Math.Abs(y) > (_viewportRt.sizeDelta.y * 0.5f) + 12f * s)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                item.Root.SetActive(true);
                item.Rect.anchoredPosition = new Vector2(0f, y);

                double majorRemainder = Math.Abs(tickVal - Math.Round(tickVal / step) * step);
                bool isMajor = majorRemainder < (subStep * 0.25);

                if (isMajor)
                {
                    // 主刻度：精炼长刻线 (7.5px) + 高清文字标牌
                    item.LineRt.sizeDelta = new Vector2(7.5f * s, 1.2f * s);
                    item.Line.color = majorCol;
                    item.Label.gameObject.SetActive(true);

                    string labelStr;
                    if (_currentTier == DynamicUnitTier.Mega && _isSpeedTape)
                        labelStr = $"{tickVal:F2}";
                    else if (step < 1.0f)
                        labelStr = $"{tickVal:F1}";
                    else
                        labelStr = $"{tickVal:F0}";

                    item.Label.text = labelStr;
                }
                else
                {
                    // 中刻度：半步长中刻线 (4.0px)，无文字标牌
                    item.LineRt.sizeDelta = new Vector2(4.0f * s, 1.0f * s);
                    item.Line.color = halfCol;
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        private void UpdateDynamicTrendIndicator(IFlightTelemetry telemetry, double rawSpeed)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);

            if (_isSpeedTape)
            {
                // ==================== 1. ACC (G 载荷) 解算与警告/危险变色关照 (──► 指针式) ====================
                double gForce = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
                if (double.IsNaN(gForce)) gForce = 0.0;

                string accStr = UIFactory.FormatTabular($"{gForce:F1}G");
                if (accStr != _lastAccText)
                {
                    _lastAccText = accStr;
                    if (_accValText != null) _accValText.text = accStr;
                }

                // 航天生理与结构载荷警戒判定 (对齐用户规范：4G 黄色，8G 红色)：
                // 正常 (Level 0): < 4.0G 且 > -1.5G (绿色/主读数色)
                // 警告 (Level 1): 4.0G ~ 8.0G 或 -1.5G ~ -3.0G (琥珀金 Warning)
                // 危险 (Level 2): >= 8.0G 或 <= -3.0G (珊瑚红 Danger 晕厥/过载)
                int alertLevel = 0;
                if (gForce >= 8.0 || gForce <= -3.0)
                {
                    alertLevel = 2; // 极危 (8G 红色)
                }
                else if (gForce >= 4.0 || gForce <= -1.5)
                {
                    alertLevel = 1; // 告警 (4G 黄色)
                }

                if (alertLevel != _lastAccAlertLevel)
                {
                    _lastAccAlertLevel = alertLevel;
                    TextStyleRole valTextRole = alertLevel == 2 ? TextStyleRole.Danger :
                                                alertLevel == 1 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue;
                    TextStyleRole tagTextRole = alertLevel == 2 ? TextStyleRole.Danger :
                                                alertLevel == 1 ? TextStyleRole.Warning : TextStyleRole.Cardinal;
                    MeterStyleRole meterRole = alertLevel == 2 ? MeterStyleRole.Danger :
                                              alertLevel == 1 ? MeterStyleRole.Warning : MeterStyleRole.Primary;

                    if (_accValText != null) ApplyText(_accValText, valTextRole, theme);
                    if (_accTagText != null) ApplyText(_accTagText, tagTextRole, theme);

                    Color pointerCol = WidgetStyleManager.Meter(meterRole, theme);
                    if (_accPointerHead != null) _accPointerHead.color = pointerCol;
                    if (_accPointerStem != null) _accPointerStem.color = pointerCol;
                    if (_accTraceImg != null)
                    {
                        float traceAlpha = alertLevel == 2 ? 0.60f : alertLevel == 1 ? 0.50f : 0.40f;
                        _accTraceImg.color = WidgetStyleManager.WithAlpha(pointerCol, traceAlpha);
                    }

                    if (_accTagOutline != null)
                    {
                        _accTagOutline.effectColor = alertLevel == 2 ? WidgetStyleManager.Meter(MeterStyleRole.Danger, theme) :
                                                     alertLevel == 1 ? WidgetStyleManager.Meter(MeterStyleRole.Warning, theme) :
                                                     theme.FrameBorderColor.ToColor();
                    }
                    if (_accTagBg != null)
                    {
                        Color baseBg = theme.FrameBgColor;
                        if (alertLevel == 2)
                        {
                            _accTagBg.color = Color.Lerp(baseBg, WidgetStyleManager.Meter(MeterStyleRole.Danger, theme), 0.22f);
                        }
                        else if (alertLevel == 1)
                        {
                            _accTagBg.color = Color.Lerp(baseBg, WidgetStyleManager.Meter(MeterStyleRole.Warning, theme), 0.16f);
                        }
                        else
                        {
                            _accTagBg.color = baseBg;
                        }
                    }
                }

                // ACC 纵向滑动 ──► 指针与微痕发丝 (0~8G 线性标尺，-_currentHalfTrackH 至 +_currentHalfTrackH)
                float accFraction = Mathf.Clamp01((float)(gForce / 8.0));
                float pointerY = Mathf.Lerp(-_currentHalfTrackH, _currentHalfTrackH, accFraction);
                if (_accPointerRt != null)
                {
                    _accPointerRt.anchoredPosition = new Vector2(0f, pointerY);
                }
                if (_accTraceRt != null)
                {
                    float traceLen = pointerY - (-_currentHalfTrackH);
                    _accTraceRt.sizeDelta = new Vector2(1.2f * s, Mathf.Max(0f, traceLen));
                }

                // ==================== 2. dV/dt (速度变化率) 微分采样与 ──► 指针式指示 ====================
                float now = Time.time;
                if (!double.IsNaN(_lastSampleSpeed) && now > _lastSampleTime + 0.05f)
                {
                    float dt = now - _lastSampleTime;
                    double instantaneousAccel = (rawSpeed - _lastSampleSpeed) / dt;
                    _calculatedAccelMps2 = Mathf.Lerp((float)_calculatedAccelMps2, (float)instantaneousAccel, 0.35f);
                    _lastSampleSpeed = rawSpeed;
                    _lastSampleTime = now;
                }
                else if (double.IsNaN(_lastSampleSpeed))
                {
                    _lastSampleSpeed = rawSpeed;
                    _lastSampleTime = now;
                    _calculatedAccelMps2 = 0.0;
                }

                // 速度变化率零位死区 (0.08 m/s²)，彻底消除物理微颤引起的正负频繁跳变与闪烁
                const double RATE_DEADBAND = 0.08;
                bool isRateDeadband = Math.Abs(_calculatedAccelMps2) < RATE_DEADBAND;

                string rateStr;
                if (isRateDeadband)
                {
                    rateStr = "0.0";
                }
                else
                {
                    rateStr = _calculatedAccelMps2 > 0 ? $"+{_calculatedAccelMps2:F1}" : $"{_calculatedAccelMps2:F1}";
                }

                rateStr = UIFactory.FormatTabular(rateStr);

                if (rateStr != _lastRateText)
                {
                    _lastRateText = rateStr;
                    if (_rateValText != null)
                    {
                        _rateValText.text = rateStr;
                        TextStyleRole rateRole = isRateDeadband || _calculatedAccelMps2 >= 0 ? TextStyleRole.Accent : TextStyleRole.Warning;
                        ApplyText(_rateValText, rateRole, theme);
                    }
                }

                float maxScale = 20.0f;
                float rateFraction;
                bool isRatePositive;
                MeterStyleRole rateMeterRole;

                if (isRateDeadband)
                {
                    rateFraction = 0f;
                    isRatePositive = true;
                    rateMeterRole = MeterStyleRole.Primary;
                }
                else
                {
                    rateFraction = Mathf.Clamp((float)(_calculatedAccelMps2 / maxScale), -1f, 1f);
                    isRatePositive = rateFraction >= 0f;
                    rateMeterRole = isRatePositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                }

                Color rateCol = WidgetStyleManager.Meter(rateMeterRole, theme);
                if (_ratePointerHead != null) _ratePointerHead.color = rateCol;
                if (_ratePointerStem != null) _ratePointerStem.color = rateCol;
                if (_rateTraceImg != null) _rateTraceImg.color = WidgetStyleManager.WithAlpha(rateCol, 0.40f);

                float ratePointerY = rateFraction * _currentHalfTrackH;
                if (_ratePointerRt != null)
                {
                    _ratePointerRt.anchoredPosition = new Vector2(0f, ratePointerY);
                }
                if (_rateTraceRt != null)
                {
                    if (isRateDeadband)
                    {
                        _rateTraceRt.sizeDelta = Vector2.zero;
                    }
                    else if (isRatePositive)
                    {
                        _rateTraceRt.pivot = new Vector2(0.5f, 0f);
                        _rateTraceRt.anchoredPosition = Vector2.zero;
                        _rateTraceRt.sizeDelta = new Vector2(1.2f * s, Mathf.Max(0f, ratePointerY));
                    }
                    else
                    {
                        _rateTraceRt.pivot = new Vector2(0.5f, 1f);
                        _rateTraceRt.anchoredPosition = Vector2.zero;
                        _rateTraceRt.sizeDelta = new Vector2(1.2f * s, Mathf.Max(0f, -ratePointerY));
                    }
                }
            }
            else
            {
                // ==================== 高度带 VSI：零位水平严格对齐中央 (y = 0，◄── 指针式) ====================
                double vs = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
                if (double.IsNaN(vs)) vs = 0.0;

                // 垂直速度零位死区 (0.08 m/s)，彻底消除微小振荡引起的正负频繁跳变与闪烁
                const double VSI_DEADBAND = 0.08;
                bool isVsiDeadband = Math.Abs(vs) < VSI_DEADBAND;

                float maxScale = _trendMaxScale > 0.001f ? _trendMaxScale : 100.0f;
                float rateFraction;
                bool isPositive;
                MeterStyleRole meterRole;

                if (isVsiDeadband)
                {
                    rateFraction = 0f;
                    isPositive = true;
                    meterRole = MeterStyleRole.Primary;
                }
                else
                {
                    rateFraction = Mathf.Clamp((float)(vs / maxScale), -1f, 1f);
                    isPositive = rateFraction >= 0f;
                    meterRole = isPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                }

                if (double.IsNaN(_lastTrendVal) || Math.Abs(vs - _lastTrendVal) > 0.05 || isPositive != _lastTrendPositive)
                {
                    _lastTrendVal = vs;
                    _lastTrendPositive = isPositive;

                    Color vsiCol = WidgetStyleManager.Meter(meterRole, theme);
                    if (_vsiPointerHead != null) _vsiPointerHead.color = vsiCol;
                    if (_vsiPointerStem != null) _vsiPointerStem.color = vsiCol;
                    if (_vsiTraceImg != null) _vsiTraceImg.color = WidgetStyleManager.WithAlpha(vsiCol, 0.40f);

                    TextStyleRole textRole = isVsiDeadband || isPositive ? TextStyleRole.Accent : TextStyleRole.Warning;
                    ApplyText(_vsiRateText, textRole, theme);

                    string formattedRate;
                    if (isVsiDeadband)
                        formattedRate = "0.0";
                    else if (vs > 0.0)
                        formattedRate = vs >= 1000.0 ? $"+{vs / 1000.0:F1}k" : $"+{vs:F1}";
                    else
                    {
                        double absVal = Math.Abs(vs);
                        formattedRate = absVal >= 1000.0 ? $"-{absVal / 1000.0:F1}k" : $"-{absVal:F1}";
                    }

                    formattedRate = UIFactory.FormatTabular(formattedRate);

                    if (formattedRate != _lastTrendRateText)
                    {
                        _lastTrendRateText = formattedRate;
                        if (_vsiRateText != null) _vsiRateText.text = formattedRate;
                    }
                }

                float vsiPointerY = rateFraction * _currentHalfTrackH;
                if (_vsiPointerRt != null)
                {
                    _vsiPointerRt.anchoredPosition = new Vector2(0f, vsiPointerY);
                }
                if (_vsiTraceRt != null)
                {
                    if (isVsiDeadband)
                    {
                        _vsiTraceRt.sizeDelta = Vector2.zero;
                    }
                    else if (isPositive)
                    {
                        _vsiTraceRt.pivot = new Vector2(0.5f, 0f);
                        _vsiTraceRt.anchoredPosition = Vector2.zero;
                        _vsiTraceRt.sizeDelta = new Vector2(1.2f * s, Mathf.Max(0f, vsiPointerY));
                    }
                    else
                    {
                        _vsiTraceRt.pivot = new Vector2(0.5f, 1f);
                        _vsiTraceRt.anchoredPosition = Vector2.zero;
                        _vsiTraceRt.sizeDelta = new Vector2(1.2f * s, Mathf.Max(0f, -vsiPointerY));
                    }
                }
            }
        }

        private void UpdateTerrainRibbon(IFlightTelemetry telemetry, double currentAlt)
        {
            if (_groundRibbonObj == null || _isSpeedTape || telemetry == null) return;

            double agl = TelemetryTokenEngine.EvaluateNumeric(_terrainToken, telemetry);
            if (double.IsNaN(agl)) agl = currentAlt;

            if (agl < 500.0 && agl >= -10.0)
            {
                if (!double.IsNaN(_lastTerrainVal) && Math.Abs(agl - _lastTerrainVal) < 0.2 && _groundRibbonObj.activeSelf) return;
                _lastTerrainVal = agl;

                if (!_groundRibbonObj.activeSelf) _groundRibbonObj.SetActive(true);

                float s = CurrentDpiScale;
                float step = _activeStep > 0f ? _activeStep : 100f;
                float pixelsPerUnit = (28f * s) / step;

                float groundY = -(float)(agl / _tierScale) * pixelsPerUnit;
                float ribbonHeight = Mathf.Clamp(groundY + (_viewportRt.sizeDelta.y * 0.5f), 0f, _viewportRt.sizeDelta.y);

                _groundRibbonRt.sizeDelta = new Vector2(_viewportRt.sizeDelta.x - 4f * s, ribbonHeight);
                _groundRibbonRt.anchoredPosition = new Vector2(0f, -_viewportRt.sizeDelta.y * 0.5f);
            }
            else
            {
                if (_groundRibbonObj.activeSelf) _groundRibbonObj.SetActive(false);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.Normal, theme);

            if (_centerBoxBg != null)
            {
                _centerBoxBg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 1.0f);
            }
            if (_centerBoxOutline != null)
            {
                _centerBoxOutline.effectColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }
            if (_pointerArrowImg != null)
            {
                _pointerArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }
            if (_hairlineRayImg != null)
            {
                _hairlineRayImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }
            if (_centerSeamImg != null)
            {
                _centerSeamImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Track, theme), 0.45f);
            }

            for (int i = 0; i < _cornerAccents.Count; i++)
            {
                if (_cornerAccents[i] != null)
                    _cornerAccents[i].color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.75f);
            }

            ApplyText(_centerValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_centerUnitText, TextStyleRole.Unit, theme);

            ApplyCard(_topModeBg, _topModeOutline, CardStyleRole.Normal, theme);
            ApplyText(_topModeText, TextStyleRole.Cardinal, theme);
            if (_topLedDot != null)
            {
                _topLedDot.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            ApplyCard(_bottomSecBg, _bottomSecOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_bottomSecText, TextStyleRole.Unit, theme);

            if (_backboneRail != null)
            {
                _backboneRail.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);
            }
            if (_topGlossRim != null)
            {
                _topGlossRim.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f);
            }
            if (_bottomGlossRim != null)
            {
                _bottomGlossRim.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f);
            }
            if (_topFadeImg != null)
            {
                _topFadeImg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.70f);
            }
            if (_bottomFadeImg != null)
            {
                _bottomFadeImg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.70f);
            }

            if (_groundRibbonImg != null)
            {
                _groundRibbonImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Warning, theme), 0.45f);
            }

            if (_isSpeedTape)
            {
                if (_accTagBg != null) ApplyCard(_accTagBg, _accTagOutline, CardStyleRole.Normal, theme);
                if (_accTagText != null)
                {
                    TextStyleRole tagRole = _lastAccAlertLevel == 2 ? TextStyleRole.Danger :
                                            _lastAccAlertLevel == 1 ? TextStyleRole.Warning : TextStyleRole.Cardinal;
                    ApplyText(_accTagText, tagRole, theme);
                }
                if (_accValText != null)
                {
                    TextStyleRole accRole = _lastAccAlertLevel == 2 ? TextStyleRole.Danger :
                                            _lastAccAlertLevel == 1 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue;
                    ApplyText(_accValText, accRole, theme);
                }
                if (_accTrackBg != null) ApplyCard(_accTrackBg, _accTrackOutline, CardStyleRole.SubtleSlot, theme);
                if (_accTrack != null) _accTrack.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);

                MeterStyleRole accMeterRole = _lastAccAlertLevel == 2 ? MeterStyleRole.Danger :
                                             _lastAccAlertLevel == 1 ? MeterStyleRole.Warning : MeterStyleRole.Primary;
                Color accCol = WidgetStyleManager.Meter(accMeterRole, theme);
                if (_accPointerHead != null) _accPointerHead.color = accCol;
                if (_accPointerStem != null) _accPointerStem.color = accCol;
                if (_accTraceImg != null)
                {
                    float traceAlpha = _lastAccAlertLevel == 2 ? 0.60f : _lastAccAlertLevel == 1 ? 0.50f : 0.40f;
                    _accTraceImg.color = WidgetStyleManager.WithAlpha(accCol, traceAlpha);
                }

                if (_rateTagBg != null) ApplyCard(_rateTagBg, _rateTagOutline, CardStyleRole.Normal, theme);
                if (_rateTagText != null) ApplyText(_rateTagText, TextStyleRole.Unit, theme);
                if (_rateValText != null) ApplyText(_rateValText, _calculatedAccelMps2 >= 0 ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
                if (_rateTrackBg != null) ApplyCard(_rateTrackBg, _rateTrackOutline, CardStyleRole.SubtleSlot, theme);
                if (_rateTrack != null) _rateTrack.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
                if (_rateZeroAnchor != null) _rateZeroAnchor.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.60f);

                MeterStyleRole rateRole = _calculatedAccelMps2 >= 0 ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                Color rateCol = WidgetStyleManager.Meter(rateRole, theme);
                if (_ratePointerHead != null) _ratePointerHead.color = rateCol;
                if (_ratePointerStem != null) _ratePointerStem.color = rateCol;
                if (_rateTraceImg != null) _rateTraceImg.color = WidgetStyleManager.WithAlpha(rateCol, 0.40f);
            }
            else
            {
                if (_vsiTagBox != null) ApplyCard(_vsiTagBg, _vsiTagOutline, CardStyleRole.Normal, theme);
                if (_vsiTagText != null) ApplyText(_vsiTagText, TextStyleRole.Cardinal, theme);
                if (_vsiRateText != null) ApplyText(_vsiRateText, _lastTrendPositive ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
                if (_vsiTrackBg != null) ApplyCard(_vsiTrackBg, _vsiTrackOutline, CardStyleRole.SubtleSlot, theme);
                if (_vsiTrack != null) _vsiTrack.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
                if (_vsiZeroAnchor != null) _vsiZeroAnchor.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.60f);

                MeterStyleRole vsiRole = _lastTrendPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                Color vsiCol = WidgetStyleManager.Meter(vsiRole, theme);
                if (_vsiPointerHead != null) _vsiPointerHead.color = vsiCol;
                if (_vsiPointerStem != null) _vsiPointerStem.color = vsiCol;
                if (_vsiTraceImg != null) _vsiTraceImg.color = WidgetStyleManager.WithAlpha(vsiCol, 0.40f);
            }

            Color borderCol = theme.FrameBorderColor.ToColor();
            Color majorCol = WidgetStyleManager.WithAlpha(borderCol, 0.60f);
            Color halfCol = WidgetStyleManager.WithAlpha(borderCol, 0.30f);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                {
                    ApplyText(_tickPool[i].Label, TextStyleRole.PrimaryValue, theme);
                }
                if (_tickPool[i].Line != null)
                {
                    bool isMajor = _tickPool[i].Label != null && _tickPool[i].Label.gameObject.activeSelf;
                    _tickPool[i].Line.color = isMajor ? majorCol : halfCol;
                }
            }
        }

        protected override void OnDestroy()
        {
            if (_centerBoxBtn != null)
            {
                _centerBoxBtn.onClick.RemoveListener(OnBoxClicked);
                _centerBoxBtn = null;
            }
            if (_topModeBtn != null)
            {
                _topModeBtn.onClick.RemoveListener(OnBoxClicked);
                _topModeBtn = null;
            }
            base.OnDestroy();
        }
    }
}
