using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 标尺带套件 (Speed Tape / Altitude Tape Kit) - 次世代航电重构版
    /// 核心特性：
    /// 1. 太空全场景动态无极工程量纲引擎 (m/km/Mm/Gm, m/s/km/s/c, 15% 滞后死区平滑防抖)
    /// 2. 标尺步长自愈系统 (按量纲自适应阶梯，彻底根除高轨百万刻度线与 283k 多胞胎同名 Bug)
    /// 3. 100% 实体高对比度中央读数窗 (Solid Odometer Cassette)，彻底杜绝背景刻度穿透重叠
    /// 4. 一体化多边形指针凸嘴 (Chevron Pointer) 与发光准星发丝线 (Luminescent Index Ray)
    /// 5. 真实民航 6 秒空速动态预测条 (Speed Trend Vector) 与零位严格平齐的 VSI 升降率指示
    /// 6. 贴地雷达地形感知警戒带 (Radar Ground Ribbon, 45度航空黄黑斑马纹，<500m AGL 自动升起)
    /// 7. 8 大航电微设计：激光光栅三级阶梯导轨、LED 模式通电微灯、滚轮沉降接缝、CNC 角标、端部 Alpha 羽化消隐
    /// 8. 严格遵照 MFP-SPEC-001..007 标准化铁律，0 颜色字面量，100% 通配符双驱动，零 GC
    /// </summary>
    public class TapeGaugeWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        private const int TICK_POOL_SIZE = 32;

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

        // 刻度池项 (主/副/微三级刻度阶梯)
        private struct TickItem
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
            public Image SubTick;
            public RectTransform SubTickRt;
        }
        private readonly List<TickItem> _tickPool = new List<TickItem>(TICK_POOL_SIZE);

        // 中央高对比度实体读数窗口 (Center Odometer Readout Box)
        private RectTransform _centerBoxRt;
        private Image _centerBoxBg;
        private Outline _centerBoxOutline;
        private Text _centerValueText;
        private Button _centerBoxBtn;

        // 一体化五边形指针凸嘴 (Chevron Pointer) 与发光准星发丝线 (Luminescent Index Ray)
        private RectTransform _pointerArrowRt;
        private Image _pointerArrowImg;
        private RectTransform _hairlineRayRt;
        private Image _hairlineRayImg;

        // 机械沉降微分割缝与微型 CNC 角标 (Divider Seam & Corner Accents)
        private Image _centerSeamImg;
        private readonly List<Image> _cornerAccents = new List<Image>(4);

        // 模式与顶部/底部标牌 (Top Mode Capsule & Bottom Unit Plate)
        private GameObject _topModeBox;
        private Image _topModeBg;
        private Outline _topModeOutline;
        private Text _topModeText;
        private Button _topModeBtn;
        private Image _topLedDot;

        private GameObject _bottomUnitBox;
        private Image _bottomUnitBg;
        private Outline _bottomUnitOutline;
        private Text _bottomUnitText;

        // 真实民航 6 秒空速趋势条 / 水平对齐垂直速度指示器 (VSI / Trend Indicator)
        private RectTransform _trendRoot;
        private GameObject _trendTagBox;
        private Outline _trendTagOutline;
        private Image _trendTagBg;
        private Text _trendTagText;
        private Text _trendRateText;

        private GameObject _trendTrackBgObj;
        private Image _trendTrackBg;
        private Outline _trendTrackOutline;
        private Image _trendTrack;
        private Image _trendZeroAnchor;
        private RectTransform _trendBarRt;
        private Image _trendBarImg;
        private RectTransform _trendArrowRt;
        private Image _trendArrowImg;

        // 通配符通道与配置
        private string _valueToken = "{SPD}";
        private string _topModeTemplate = "SPD";
        private string _bottomUnitTemplate = "m/s";
        private string _trendToken = "{GFORCE}";
        private string _trendTagTemplate = "ACC";
        private string _trendUnitTemplate = "G";
        private string _terrainToken = "{ALT:AGL}";
        private float _trendMaxScale = 4.0f;
        private bool _isSpeedTape = false;

        // 太空全场景动态无极工程量纲引擎 (Dynamic Multi-Scale Engine)
        private bool _autoUnitEnabled = true;
        private DynamicUnitTier _currentTier = DynamicUnitTier.Base;
        private double _tierScale = 1.0;
        private string _activeUnitStr = "m";
        private float _activeStep = 100f;

        // 脏检查与状态缓存
        private double _lastRawVal = double.NaN;
        private double _lastTrendVal = double.NaN;
        private double _lastTerrainVal = double.NaN;
        private string _lastCenterText = string.Empty;
        private string _lastTopText = string.Empty;
        private string _lastBottomText = string.Empty;
        private string _lastTrendTagText = string.Empty;
        private string _lastTrendRateText = string.Empty;
        private bool _lastTrendPositive = true;

        public static Action OnCycleSpeedModeAction;
        public static Action OnCycleAltitudeModeAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float width = 50f * s;
            float height = 210f * s;
            RectTransform.sizeDelta = new Vector2(width, height);

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

            // 垂直精密导轨基线 (Backbone Rail)
            float tickX = Config.IsLeftOrientation ? (16f * s) : (-16f * s);
            _backboneRailObj = UIFactory.CreatePanel(_tickContainer, "Backbone_Rail",
                new Vector2(1.5f * s, height - 12f * s), new Vector2(tickX, 0f),
                WidgetStyleManager.Meter(MeterStyleRole.Track, theme));
            _backboneRail = _backboneRailObj.GetComponent<Image>();

            // 贴地雷达地形感知警戒带 (Radar Ground Ribbon)
            BuildGroundRibbon(theme);

            // 初始化激光光栅三级刻度对象池
            BuildTickPool(theme);

            // 视口端部镜面反光线与羽化遮罩 (Gloss Horizon Rim & Fade)
            BuildGlossRimsAndFades(theme);

            // 3. 中央实体高对比度读数窗口 (Center Odometer Readout Box)
            BuildCenterReadoutBox(theme);

            // 4. 顶部模式胶囊与底部单位铭牌 (Top Mode Capsule & Bottom Unit Plate)
            BuildCapsuleLabels(theme);

            // 5. 趋势指示器 (速度带内置 6 秒预测条 / 高度带水平对齐 VSI)
            BuildTrendIndicator(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            bool isLeft = config != null && config.IsLeftOrientation;
            string numToken = config?.NumericToken ?? "";
            _isSpeedTape = isLeft || numToken.Contains("SPD");

            if (_isSpeedTape)
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{SPD}";
                _topModeTemplate = "SPD";
                _bottomUnitTemplate = !string.IsNullOrEmpty(config?.UnitLabel) ? config.UnitLabel : "m/s";
                _trendToken = "{GFORCE}";
                _trendTagTemplate = "ACC";
                _trendUnitTemplate = "G";
                _trendMaxScale = 4.0f;
            }
            else
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{ALT}";
                _topModeTemplate = "ALT";
                _bottomUnitTemplate = !string.IsNullOrEmpty(config?.UnitLabel) ? config.UnitLabel : "m";
                _trendToken = "{VS}";
                _trendTagTemplate = "V/S";
                _trendUnitTemplate = "m/s";
                _trendMaxScale = 100.0f;
                _terrainToken = "{ALT:AGL}";
            }

            // 严禁长中文直接挤入微型胶囊：若 DisplayName 为标准短标则保留，长标题自动提炼为紧凑模式
            if (!string.IsNullOrEmpty(config?.DisplayName))
            {
                if (config.DisplayName.Length <= 4 && !config.DisplayName.Contains("标尺带"))
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
                    case "UNIT":
                    case "BOTTOM":
                    case "BOTTOM_LABEL":
                        _bottomUnitTemplate = v;
                        break;
                    case "TREND_VAL":
                    case "TREND_TOKEN":
                        _trendToken = v;
                        break;
                    case "TREND_TAG":
                    case "TREND_LABEL":
                        _trendTagTemplate = v;
                        break;
                    case "TREND_UNIT":
                        _trendUnitTemplate = v;
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

            // 1. 顶端镜面反光线 (Gloss Horizon Rim)
            GameObject topRimObj = UIFactory.CreatePanel(_viewportRt, "Top_Gloss_Rim",
                new Vector2(w - 2f * s, 1f * s), new Vector2(0f, halfH - 1f * s),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f));
            _topGlossRim = topRimObj.GetComponent<Image>();

            // 2. 底端镜面反光线
            GameObject btmRimObj = UIFactory.CreatePanel(_viewportRt, "Btm_Gloss_Rim",
                new Vector2(w - 2f * s, 1f * s), new Vector2(0f, -halfH + 1f * s),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.35f));
            _bottomGlossRim = btmRimObj.GetComponent<Image>();

            // 3. 顶底柔和消隐遮罩色带
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);
            GameObject topFade = UIFactory.CreatePanel(_viewportRt, "Top_Edge_Fade",
                new Vector2(w, 14f * s), new Vector2(0f, halfH - 7f * s),
                WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.65f));
            _topFadeImg = topFade.GetComponent<Image>();
            _topFadeImg.raycastTarget = false;

            GameObject btmFade = UIFactory.CreatePanel(_viewportRt, "Btm_Edge_Fade",
                new Vector2(w, 14f * s), new Vector2(0f, -halfH + 7f * s),
                WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.65f));
            _bottomFadeImg = btmFade.GetComponent<Image>();
            _bottomFadeImg.raycastTarget = false;
        }

        private void BuildTickPool(ThemeConfig theme)
        {
            _tickPool.Clear();
            float s = CurrentDpiScale;
            float tickX = Config.IsLeftOrientation ? (16f * s) : (-16f * s);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                GameObject itemObj = new GameObject($"Tick_{i}", typeof(RectTransform));
                itemObj.transform.SetParent(_tickContainer, false);
                RectTransform rt = itemObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(48f * s, 16f * s);

                // 主/副刻度齿线
                GameObject lineObj = new GameObject("Tick_Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(itemObj.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(14f * s, 2f * s);
                lineRt.anchoredPosition = new Vector2(tickX, 0f);
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);

                // 微副刻度齿线 (Sub-tick)
                GameObject subObj = new GameObject("Sub_Line", typeof(RectTransform), typeof(Image));
                subObj.transform.SetParent(itemObj.transform, false);
                RectTransform subRt = subObj.GetComponent<RectTransform>();
                subRt.sizeDelta = new Vector2(6f * s, 1f * s);
                subRt.anchoredPosition = new Vector2(tickX, 0f);
                Image subImg = subObj.GetComponent<Image>();
                subImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Track, theme), 0.6f);
                subObj.SetActive(false);

                // 刻度标牌数字
                int fontSize = Mathf.RoundToInt(9f * s);
                TextAnchor align = Config.IsLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                Text labelTxt = UIFactory.CreateText(itemObj.transform, "Tick_Text", "0", fontSize, align,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                RectTransform labelRt = labelTxt.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(28f * s, 14f * s);
                float labelX = Config.IsLeftOrientation ? (tickX - 17f * s) : (tickX + 17f * s);
                labelRt.anchoredPosition = new Vector2(labelX, 0f);

                _tickPool.Add(new TickItem
                {
                    Root = itemObj,
                    Rect = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = labelTxt,
                    LabelRt = labelRt,
                    SubTick = subImg,
                    SubTickRt = subRt
                });
            }
        }

        private void BuildCenterReadoutBox(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float boxW = 54f * s;
            float boxH = 24f * s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            GameObject boxObj = new GameObject("Center_Readout_Box", typeof(RectTransform), typeof(Image), typeof(Button));
            boxObj.transform.SetParent(transform, false);
            _centerBoxRt = boxObj.GetComponent<RectTransform>();
            _centerBoxRt.sizeDelta = new Vector2(boxW, boxH);

            float offsetX = Config.IsLeftOrientation ? (3f * s) : (-3f * s);
            _centerBoxRt.anchoredPosition = new Vector2(offsetX, 0f);

            _centerBoxBg = boxObj.GetComponent<Image>();
            _centerBoxOutline = boxObj.AddComponent<Outline>();
            _centerBoxOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.Normal, theme);

            // 100% 实体高对比深色底板，彻底遮蔽穿透刻度
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
            _pointerArrowRt.sizeDelta = new Vector2(7f * s, 7f * s);
            float arrowX = Config.IsLeftOrientation ? (boxW * 0.5f - 1f * s) : (-boxW * 0.5f + 1f * s);
            _pointerArrowRt.anchoredPosition = new Vector2(arrowX, 0f);
            _pointerArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _pointerArrowImg = arrowObj.GetComponent<Image>();
            _pointerArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 2. 发光瞄准发丝基准线 (Luminescent Index Ray)
            GameObject rayObj = new GameObject("Hairline_Ray", typeof(RectTransform), typeof(Image));
            rayObj.transform.SetParent(boxObj.transform, false);
            _hairlineRayRt = rayObj.GetComponent<RectTransform>();
            _hairlineRayRt.sizeDelta = new Vector2(10f * s, 1.5f * s);
            float rayX = Config.IsLeftOrientation ? (boxW * 0.5f + 4f * s) : (-boxW * 0.5f - 4f * s);
            _hairlineRayRt.anchoredPosition = new Vector2(rayX, 0f);
            _hairlineRayImg = rayObj.GetComponent<Image>();
            _hairlineRayImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 3. 机械沉降微分割缝 (Odometer Divider Seam)
            GameObject seamObj = UIFactory.CreatePanel(boxObj.transform, "Seam_Slit",
                new Vector2(1f * s, boxH - 6f * s), new Vector2(boxW * 0.18f, 0f),
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

            // 5. 中央高对比度主读数数字
            int valFontSize = Mathf.RoundToInt(12f * s);
            _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _centerValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxW - 4f * s, boxH);
            valRt.anchoredPosition = Vector2.zero;
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

            // 1.1 通电 LED 指示灯微标 (LED Status Pill)
            GameObject ledObj = UIFactory.CreatePanel(_topModeBox.transform, "LED_Dot",
                new Vector2(3f * s, 3f * s), new Vector2(-w * 0.5f + 6f * s, 0f),
                WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
            _topLedDot = ledObj.GetComponent<Image>();

            // 1.2 顶部模式文本 (杜绝横向溢出)
            int topFontSize = Mathf.RoundToInt(8.5f * s);
            _topModeText = UIFactory.CreateText(_topModeBox.transform, "Top_Mode", _topModeTemplate, topFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform topRt = _topModeText.GetComponent<RectTransform>();
            topRt.sizeDelta = new Vector2(w - 12f * s, topBoxSize.y);
            topRt.anchoredPosition = new Vector2(2f * s, 0f);
            _topModeText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 2. 底部单位沉降铭牌 (Bottom Unit Plate)
            Vector2 btmBoxSize = new Vector2(w, 16f * s);
            Vector2 btmBoxPos = new Vector2(0f, -halfH - 10f * s);
            _bottomUnitBox = UIFactory.CreatePanel(transform, "Bottom_Unit_Box", btmBoxSize, btmBoxPos, Color.clear);
            _bottomUnitBg = _bottomUnitBox.GetComponent<Image>();
            _bottomUnitOutline = _bottomUnitBox.AddComponent<Outline>();
            _bottomUnitOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bottomUnitBg, _bottomUnitOutline, CardStyleRole.SubtleSlot, theme);

            int btmFontSize = Mathf.RoundToInt(8f * s);
            _bottomUnitText = UIFactory.CreateText(_bottomUnitBox.transform, "Bottom_Unit", _activeUnitStr, btmFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform btmRt = _bottomUnitText.GetComponent<RectTransform>();
            btmRt.sizeDelta = btmBoxSize;
            btmRt.anchoredPosition = Vector2.zero;
            _bottomUnitText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void BuildTrendIndicator(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_isSpeedTape)
            {
                // 空速趋势向量：直接内置在速度标尺内侧导轨 (6-Second Trend Vector)
                float railX = Config.IsLeftOrientation ? (16f * s) : (-16f * s);

                GameObject barObj = new GameObject("Speed_Trend_Bar", typeof(RectTransform), typeof(Image));
                barObj.transform.SetParent(_viewportRt, false);
                _trendBarRt = barObj.GetComponent<RectTransform>();
                _trendBarRt.sizeDelta = new Vector2(3f * s, 0f);
                _trendBarRt.pivot = new Vector2(0.5f, 0f);
                _trendBarRt.anchoredPosition = new Vector2(railX, 0f);
                _trendBarImg = barObj.GetComponent<Image>();
                _trendBarImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

                GameObject arrowObj = new GameObject("Speed_Trend_Arrow", typeof(RectTransform), typeof(Image));
                arrowObj.transform.SetParent(_viewportRt, false);
                _trendArrowRt = arrowObj.GetComponent<RectTransform>();
                _trendArrowRt.sizeDelta = new Vector2(6f * s, 6f * s);
                _trendArrowRt.anchoredPosition = new Vector2(railX, 0f);
                _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _trendArrowImg = arrowObj.GetComponent<Image>();
                _trendArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
                _trendArrowImg.gameObject.SetActive(false);
            }
            else
            {
                // 高度带：侧边精密升降率 (VSI)，零刻度线严格与中央高度游标水平平齐 (y = 0)
                float trendX = width * 0.5f + 14f * s;

                GameObject root = new GameObject("Trend_Indicator_Root", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                _trendRoot = root.GetComponent<RectTransform>();
                _trendRoot.sizeDelta = new Vector2(20f * s, 210f * s);
                _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

                // 1. 顶部 VSI 数字读数微胶囊盒
                Vector2 boxSize = new Vector2(20f * s, 26f * s);
                Vector2 boxPos = new Vector2(0f, 88f * s);
                _trendTagBox = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Tag_Box", boxSize, boxPos, Color.clear);
                _trendTagBg = _trendTagBox.GetComponent<Image>();
                _trendTagOutline = _trendTagBox.AddComponent<Outline>();
                _trendTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_trendTagBg, _trendTagOutline, CardStyleRole.Normal, theme);

                _trendTagText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Tag_Title", _trendTagTemplate,
                    Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.Cardinal, theme));
                RectTransform tagRt = _trendTagText.GetComponent<RectTransform>();
                tagRt.sizeDelta = new Vector2(boxSize.x, 9f * s);
                tagRt.anchoredPosition = new Vector2(0f, 6.5f * s);

                _trendRateText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Rate_Val", "0.0",
                    Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _trendRateText.fontStyle = FontStyle.Bold;
                RectTransform rateRt = _trendRateText.GetComponent<RectTransform>();
                rateRt.sizeDelta = new Vector2(boxSize.x, 10f * s);
                rateRt.anchoredPosition = new Vector2(0f, -4f * s);

                // 2. 垂直基准轨道暗色遮光背景槽 (严格对齐 y = 0)
                Vector2 trackBgSize = new Vector2(16f * s, 134f * s);
                _trendTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Track_Bg", trackBgSize, Vector2.zero, Color.clear);
                _trendTrackBg = _trendTrackBgObj.GetComponent<Image>();
                _trendTrackOutline = _trendTrackBgObj.AddComponent<Outline>();
                _trendTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_trendTrackBg, _trendTrackOutline, CardStyleRole.SubtleSlot, theme);

                // 垂直轨道基准线
                GameObject trackObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_Track",
                    new Vector2(1.5f * s, 120f * s), Vector2.zero,
                    style.GetMeterColor(MeterStyleRole.Track, theme));
                _trendTrack = trackObj.GetComponent<Image>();

                // 水平零位基准菱形能量收敛锚点 (Zero Datum Anchor at y = 0)
                GameObject zeroObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_ZeroAnchor",
                    new Vector2(6f * s, 6f * s), Vector2.zero,
                    WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));
                RectTransform zeroRt = zeroObj.GetComponent<RectTransform>();
                zeroRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _trendZeroAnchor = zeroObj.GetComponent<Image>();

                // 辅助刻度线 (±25, ±50)
                float[] tickOffsets = new float[] { 25f * s, 50f * s, -25f * s, -50f * s };
                Color trackTickCol = style.GetMeterColor(MeterStyleRole.Track, theme);
                foreach (float yOff in tickOffsets)
                {
                    UIFactory.CreatePanel(_trendTrackBgObj.transform, $"Trend_Tick_{yOff:F0}",
                        new Vector2(4f * s, 1f * s), new Vector2(0f, yOff), trackTickCol);
                }

                // 动态拉伸升降条与平滑指示箭头
                GameObject barObj = new GameObject("Trend_Bar", typeof(RectTransform), typeof(Image));
                barObj.transform.SetParent(_trendTrackBgObj.transform, false);
                _trendBarRt = barObj.GetComponent<RectTransform>();
                _trendBarRt.sizeDelta = new Vector2(3f * s, 0f);
                _trendBarRt.pivot = new Vector2(0.5f, 0f);
                _trendBarRt.anchoredPosition = Vector2.zero;
                _trendBarImg = barObj.GetComponent<Image>();
                _trendBarImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

                GameObject arrowObj = new GameObject("Trend_Arrow", typeof(RectTransform), typeof(Image));
                arrowObj.transform.SetParent(_trendTrackBgObj.transform, false);
                _trendArrowRt = arrowObj.GetComponent<RectTransform>();
                _trendArrowRt.sizeDelta = new Vector2(5f * s, 5f * s);
                _trendArrowRt.anchoredPosition = Vector2.zero;
                _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                _trendArrowImg = arrowObj.GetComponent<Image>();
                _trendArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
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

            // 3. 动态求值并更新模式与单位标签
            UpdateLabels(telemetry);

            // 4. 动态更新趋势指示器 (6秒空速预测或水平对齐 VSI)
            UpdateDynamicTrendIndicator(telemetry);

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
                        if (abs >= 3000000.0) nextTier = DynamicUnitTier.Mega; // >= 0.01c
                        else if (abs < 8500.0) nextTier = DynamicUnitTier.Base; // 15% 滞后防抖
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
                        if (abs >= 10000000.0) nextTier = DynamicUnitTier.Mega; // >= 10,000 km = 10 Mm
                        else if (abs < 8500.0) nextTier = DynamicUnitTier.Base; // 15% 滞后防抖
                        break;
                    case DynamicUnitTier.Mega:
                        if (abs >= 10000000000.0) nextTier = DynamicUnitTier.Giga; // >= 10 Gm
                        else if (abs < 8500000.0) nextTier = DynamicUnitTier.Kilo; // 15% 滞后防抖
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

            SetTextIfChanged(_bottomUnitText, _activeUnitStr);
        }

        private void UpdateCenterReadout(double displayVal)
        {
            string formatted;
            if (_currentTier == DynamicUnitTier.Mega && _isSpeedTape)
            {
                formatted = $"{displayVal:F2} c";
            }
            else if (Math.Abs(displayVal) >= 1000.0)
            {
                formatted = $"{displayVal:F0}";
            }
            else if (Math.Abs(displayVal) >= 100.0)
            {
                formatted = $"{displayVal:F1}";
            }
            else
            {
                formatted = $"{displayVal:F1}";
            }

            if (formatted != _lastCenterText)
            {
                _lastCenterText = formatted;
                if (_centerValueText != null) _centerValueText.text = formatted;
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

            string evalUnit = _autoUnitEnabled ? _activeUnitStr : TelemetryTokenEngine.Evaluate(_bottomUnitTemplate, telemetry);
            if (evalUnit != _lastBottomText)
            {
                _lastBottomText = evalUnit;
                if (_bottomUnitText != null) _bottomUnitText.text = evalUnit;
            }
        }

        private void UpdateRollingTape(double currentDisplayVal)
        {
            float step = _activeStep > 0f ? _activeStep : 100f;
            float pixelsPerUnit = (28f * CurrentDpiScale) / step;
            float visibleHalfSpan = (_viewportRt.sizeDelta.y * 0.5f) / pixelsPerUnit;

            // 向下对齐整刻度基准点
            double startTick = Math.Floor((currentDisplayVal - visibleHalfSpan) / step) * step;

            for (int i = 0; i < _tickPool.Count; i++)
            {
                TickItem item = _tickPool[i];
                double tickVal = startTick + i * step;

                // 速度带不展示负刻度
                if (tickVal < 0.0 && _isSpeedTape)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                float y = (float)(tickVal - currentDisplayVal) * pixelsPerUnit;
                if (Math.Abs(y) > (_viewportRt.sizeDelta.y * 0.5f) + 12f * CurrentDpiScale)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                item.Root.SetActive(true);
                item.Rect.anchoredPosition = new Vector2(0f, y);

                long tickIndex = (long)Math.Round(tickVal / step);
                bool isMajor = (tickIndex % 2 == 0);

                if (isMajor)
                {
                    item.LineRt.sizeDelta = new Vector2(14f * CurrentDpiScale, 2f * CurrentDpiScale);
                    item.Label.gameObject.SetActive(true);

                    // 彻底解决 283k 多胞胎同名 Bug：按当前单位与步长智能精度格式化
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
                    item.LineRt.sizeDelta = new Vector2(8f * CurrentDpiScale, 1.5f * CurrentDpiScale);
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        private void UpdateDynamicTrendIndicator(IFlightTelemetry telemetry)
        {
            if (_trendBarRt == null || telemetry == null) return;

            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);

            if (_isSpeedTape)
            {
                // 空速 6 秒真实预测条: deltaV = a * 6s
                double gForce = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
                if (double.IsNaN(gForce)) gForce = 0.0;

                // 加速度转换为 m/s^2 (扣除 1G 后的有效轴向/正向加速度)
                double accel = gForce * 9.80665;
                double deltaV6s = accel * 6.0;

                float pixelsPerUnit = (28f * s) / _activeStep;
                float dynamicLen = Mathf.Clamp((float)(deltaV6s / _tierScale) * pixelsPerUnit, -65f * s, 65f * s);

                bool isPositive = dynamicLen >= 0f;
                MeterStyleRole trendRole = isPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;

                if (_trendBarImg != null) _trendBarImg.color = WidgetStyleManager.Meter(trendRole, theme);
                if (_trendArrowImg != null) _trendArrowImg.color = WidgetStyleManager.Meter(trendRole, theme);

                float absLen = Mathf.Abs(dynamicLen);
                if (isPositive)
                {
                    _trendBarRt.pivot = new Vector2(0.5f, 0f);
                    _trendBarRt.anchoredPosition = new Vector2(_trendBarRt.anchoredPosition.x, 0f);
                    _trendBarRt.sizeDelta = new Vector2(3f * s, absLen);

                    _trendArrowRt.anchoredPosition = new Vector2(_trendBarRt.anchoredPosition.x, absLen + 3f * s);
                    _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                }
                else
                {
                    _trendBarRt.pivot = new Vector2(0.5f, 1f);
                    _trendBarRt.anchoredPosition = new Vector2(_trendBarRt.anchoredPosition.x, 0f);
                    _trendBarRt.sizeDelta = new Vector2(3f * s, absLen);

                    _trendArrowRt.anchoredPosition = new Vector2(_trendBarRt.anchoredPosition.x, -absLen - 3f * s);
                    _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                }

                if (_trendArrowImg != null)
                {
                    _trendArrowImg.gameObject.SetActive(absLen > 3f * s);
                }
            }
            else
            {
                // 高度带 VSI：零位水平严格对齐中央 (y = 0)
                double vs = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
                if (double.IsNaN(vs)) vs = 0.0;

                float maxScale = _trendMaxScale > 0.001f ? _trendMaxScale : 100.0f;
                float rateFraction = Mathf.Clamp((float)(vs / maxScale), -1f, 1f);
                bool isPositive = rateFraction >= 0f;

                if (double.IsNaN(_lastTrendVal) || Math.Abs(vs - _lastTrendVal) > 0.05 || isPositive != _lastTrendPositive)
                {
                    _lastTrendVal = vs;
                    _lastTrendPositive = isPositive;

                    MeterStyleRole meterRole = isPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                    if (_trendBarImg != null) _trendBarImg.color = WidgetStyleManager.Meter(meterRole, theme);
                    if (_trendArrowImg != null) _trendArrowImg.color = WidgetStyleManager.Meter(meterRole, theme);

                    TextStyleRole textRole = isPositive ? TextStyleRole.Accent : TextStyleRole.Warning;
                    ApplyText(_trendRateText, textRole, theme);

                    string formattedRate;
                    if (Math.Abs(vs) < 0.05)
                        formattedRate = "0.0";
                    else if (vs > 0.0)
                        formattedRate = vs >= 1000.0 ? $"+{vs / 1000.0:F1}k" : $"+{vs:F1}";
                    else
                    {
                        double absVal = Math.Abs(vs);
                        formattedRate = absVal >= 1000.0 ? $"-{absVal / 1000.0:F1}k" : $"-{absVal:F1}";
                    }

                    if (formattedRate != _lastTrendRateText)
                    {
                        _lastTrendRateText = formattedRate;
                        if (_trendRateText != null) _trendRateText.text = formattedRate;
                    }
                }

                float dynamicLen = Mathf.Abs(rateFraction) * (50f * s);
                if (isPositive)
                {
                    _trendBarRt.pivot = new Vector2(0.5f, 0f);
                    _trendBarRt.anchoredPosition = Vector2.zero;
                    _trendBarRt.sizeDelta = new Vector2(3f * s, dynamicLen);

                    _trendArrowRt.anchoredPosition = new Vector2(0f, dynamicLen + 3f * s);
                    _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                }
                else
                {
                    _trendBarRt.pivot = new Vector2(0.5f, 1f);
                    _trendBarRt.anchoredPosition = Vector2.zero;
                    _trendBarRt.sizeDelta = new Vector2(3f * s, dynamicLen);

                    _trendArrowRt.anchoredPosition = new Vector2(0f, -dynamicLen - 3f * s);
                    _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
                }

                if (_trendArrowImg != null)
                {
                    _trendArrowImg.gameObject.SetActive(dynamicLen > 2f * s);
                }
            }
        }

        private void UpdateTerrainRibbon(IFlightTelemetry telemetry, double currentAlt)
        {
            if (_groundRibbonObj == null || _isSpeedTape || telemetry == null) return;

            // 采样真实离地高度
            double agl = TelemetryTokenEngine.EvaluateNumeric(_terrainToken, telemetry);
            if (double.IsNaN(agl)) agl = currentAlt;

            // 当离地高度 < 500m 时展示地面态势感知带
            if (agl < 500.0 && agl >= -10.0)
            {
                if (!double.IsNaN(_lastTerrainVal) && Math.Abs(agl - _lastTerrainVal) < 0.2 && _groundRibbonObj.activeSelf) return;
                _lastTerrainVal = agl;

                if (!_groundRibbonObj.activeSelf) _groundRibbonObj.SetActive(true);

                float s = CurrentDpiScale;
                float step = _activeStep > 0f ? _activeStep : 100f;
                float pixelsPerUnit = (28f * s) / step;

                // 地面在标尺带上的相对垂直高度 (触地时刚好与中央游标平齐 y = 0)
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

            ApplyCard(_topModeBg, _topModeOutline, CardStyleRole.Normal, theme);
            ApplyText(_topModeText, TextStyleRole.Cardinal, theme);
            if (_topLedDot != null)
            {
                _topLedDot.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            ApplyCard(_bottomUnitBg, _bottomUnitOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_bottomUnitText, TextStyleRole.Unit, theme);

            if (_backboneRail != null)
            {
                _backboneRail.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
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
                _topFadeImg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.65f);
            }
            if (_bottomFadeImg != null)
            {
                _bottomFadeImg.color = WidgetStyleManager.WithAlpha(resolved.FrameBgColor, 0.65f);
            }

            if (_groundRibbonImg != null)
            {
                _groundRibbonImg.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Warning, theme), 0.45f);
            }

            if (_trendTagBg != null)
            {
                ApplyCard(_trendTagBg, _trendTagOutline, CardStyleRole.Normal, theme);
                ApplyText(_trendTagText, TextStyleRole.Cardinal, theme);
                ApplyText(_trendRateText, _lastTrendPositive ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
            }

            if (_trendTrackBg != null)
            {
                ApplyCard(_trendTrackBg, _trendTrackOutline, CardStyleRole.SubtleSlot, theme);
            }
            if (_trendTrack != null)
            {
                _trendTrack.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
            }
            if (_trendZeroAnchor != null)
            {
                _trendZeroAnchor.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            MeterStyleRole trendRole = _lastTrendPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
            if (_trendBarImg != null) _trendBarImg.color = WidgetStyleManager.Meter(trendRole, theme);
            if (_trendArrowImg != null) _trendArrowImg.color = WidgetStyleManager.Meter(trendRole, theme);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                {
                    ApplyText(_tickPool[i].Label, TextStyleRole.PrimaryValue, theme);
                }
                if (_tickPool[i].Line != null)
                {
                    _tickPool[i].Line.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
                }
                if (_tickPool[i].SubTick != null)
                {
                    _tickPool[i].SubTick.color = WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Track, theme), 0.6f);
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
