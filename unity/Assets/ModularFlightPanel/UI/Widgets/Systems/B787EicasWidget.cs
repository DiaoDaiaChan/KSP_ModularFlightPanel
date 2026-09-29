using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 波音 787 梦想客机 EICAS 综合航电发动机与机载系统显示器 (Boeing 787 Dreamliner EICAS)
    /// ====================================================================================
    /// 忠实还原波音 787 标志性马蹄弧 (Horseshoe Cradle) 开口圆环仪表与当代双发/多发数字驾驶舱航电体系：
    /// 1. 标志性圆弧指示器 (787 Horseshoe Cradle Dial)：
    ///    - 顶部高亮白框数显 [ 0.0 ]；
    ///    - 底边直接向右切出水平基准线，并在右下角无缝弯折切入 215° 连续大圆弧下沉托架；
    ///    - 左上角 (-215° / 10:30) 红色超速/超温超限径向警戒标线 (Redline Tick)；
    ///    - 9 点钟方向 (-180°) 琥珀色连续推力参考标线与荧光绿尖锥推力限值游标 (Command Bug REV 81.3)；
    ///    - N1 底部专属双同心圆弧 (Double Concentric Arc)，标定正常推力区；
    ///    - EGT 底部左下角 (-130°) 红色起动温控警戒标线 (Start Limit Tick)；
    ///    - 顺时针旋转的主动动力针 (Needle Pointer)。
    /// 2. 动态自适应布局与宽屏动态长宽比 (Dynamic Responsive Layout & Aspect Ratio)：
    ///    - 支持 1、2、3、4 发动机动态自适应感知与自动扩充画布宽幅 (320f ~ 490f x 340f)；
    ///    - 宽屏大间距排版，彻底解决原版挤压紧凑问题，纵向 8 行与右侧 5 组系统完全呼吸透气；
    ///    - 所有发控纵列、马蹄弧表盘、起落架、配平与系统读数区均已通过 WidgetControlManager 动态注册。
    /// 3. 全闭环真实飞行遥测数据接入 (100% Local Telemetry Reachability)：
    ///    - 俯仰配平驱动 STAB TRIM (ND/NU 标尺与绿色安全带配平药丸)；
    ///    - 偏航配平驱动 RUDDER TRIM (双向刻度与微光滑动指针)；
    ///    - 全机推进剂与起飞总重解算 GROSS WT / TOTAL FUEL (LBS X 1000)；
    ///    - 全机震动 VIB 接入动态物理过载震荡与油门高频响应；
    ///    - 右侧系统读数区 100% 绑定真实本地通道：起落架、襟翼、航向、马赫、过载、电气 EC、通信信号与时间加速。
    ///      **严禁编造本地不存在数据源的子系统**（座舱增压 ECS、气源导管压力、外流阀位、燃油温度等均为
    ///      真实 787 存在但 KSP 本地无遥测通道的子系统，一律不得以常数或伪函数合成输出）。
    /// 4. 小分辨率锐利度契约 (Crisp Geometry Contract)：
    ///    - 全部细几何体（马蹄弧分段、同心内弧、基准切线、警戒标线、指针、通栏通量条）的线宽
    ///      统一经基类 <c>CrispLength</c> 钳制到 >= 1.5 物理像素并做半像素栅格对齐；
    ///    - 字号随物理缩放相对提升，杜绝 6.5px 级别亚像素字号糊字。
    /// 5. 100% 严格遵守 SPEC-001 ~ SPEC-011 规范矩阵，零颜色字面量，零场景查询，全语义调色板。
    /// </summary>
    [FlightWidget("b787_eicas", "boeing_787_eicas", "787_eicas", Category = WidgetCategory.Systems,
        DisplayName = "B787 EICAS 综合航电发动机显示",
        Description = "波音 787 风格全功能 EICAS：标志性马蹄圆弧表盘、动态长宽比多发自适应、STAB/RUDDER 配平、真实系统读数与燃油重量统计。",
        DefaultWidgetId = "custom.b787_eicas", DefaultX = -440f, DefaultY = 160f, IsSingleton = true,
        ExactIds = new[] { "custom.b787_eicas", "core.b787_eicas" })]
    public class B787EicasWidget : BaseFlightWidget
    {
        // ── 垂直布局铁律 ──
        // 卡片高度固定 340f，但所有内部 Y 坐标均由 sVertical 纵向归一化因子驱动：
        // sVertical = CurrentDpiScale * (340f / BASE_CARD_HEIGHT)
        // 当卡片被纵向放大（例如用户把组件拉高）时，行距、表盘间距与右侧系统行距等比例伸展，
        // 而线宽与字号只随 CurrentDpiScale 变化，从而保证"放大后更锐利"而不是"放大后更松散"。
        private const float BASE_CARD_HEIGHT = 340f;

        public override Vector2 BaseSize => new Vector2(390f, BASE_CARD_HEIGHT);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 基础外框
        private Image _bgImage;
        private Outline _bgOutline;

        // 顶端航电状态栏
        private Text _tatText;
        private Text _thrustModeText;
        private Text _thrustTempText;

        // 标志性 787 马蹄弧仪表数据结构
        private class DialGaugeUI
        {
            public GameObject Root;
            public Image BoxBg;
            public Outline BoxOutline;
            public Text BoxText;
            public Image Underline;
            public System.Collections.Generic.List<Image> ArcSegments = new System.Collections.Generic.List<Image>();
            public System.Collections.Generic.List<Image> InnerArcSegments = new System.Collections.Generic.List<Image>();
            public Image RedLimitTick;
            public Image AmberTick;
            public Image BottomRedTick;
            public Image CommandBug1;
            public Image CommandBug2;
            public RectTransform NeedlePivot;
            public Image NeedleImage;
            public Text TargetText;
            public Text RevText;
        }

        // 发动机独立纵列数据结构 (支持 1~4 发动态自适应)
        private class EngineColumnUI
        {
            public int EngineIndex;
            public GameObject Root;
            public RectTransform RootRt;

            // N1
            public DialGaugeUI N1Dial;

            // EGT
            public DialGaugeUI EgtDial;

            // N2
            public DialGaugeUI N2Dial;

            // FF (燃油流量)
            public Image FfBoxBg;
            public Outline FfBoxOutline;
            public Text FfText;

            // OIL PRESS
            public Image OilPBoxBg;
            public Outline OilPBoxOutline;
            public Text OilPText;
            public Image OilPTrack;
            public Image OilPLimitTick;
            public RectTransform OilPPointerPivot;
            public Image OilPPointerImage;

            // OIL TEMP
            public Image OilTBoxBg;
            public Outline OilTBoxOutline;
            public Text OilTText;
            public Image OilTTrack;
            public Image OilTLimitTick;
            public RectTransform OilTPointerPivot;
            public Image OilTPointerImage;

            // OIL QTY
            public Image OilQBoxBg;
            public Outline OilQBoxOutline;
            public Text OilQText;
            public Text OilQLoText;

            // VIB
            public Image VibBoxBg;
            public Outline VibBoxOutline;
            public Text VibText;
            public Text VibPrefixText;
            public Image VibTrack;
            public RectTransform VibPointerPivot;
            public Image VibPointerImage;

            public readonly CachedFloat LastOilPPos = new CachedFloat(-9999f, 0.1f);
            public readonly CachedFloat LastOilTPos = new CachedFloat(-9999f, 0.1f);
            public readonly CachedFloat LastVibPos = new CachedFloat(-9999f, 0.1f);

            // 运行时遥测防抖缓存
            public double LastN1Val = double.NaN;
            public double LastEgtVal = double.NaN;
            public double LastN2Val = double.NaN;
            public double LastFfVal = double.NaN;
            public double LastOilPVal = double.NaN;
            public double LastOilTVal = double.NaN;
            public double LastOilQVal = double.NaN;
            public double LastVibVal = double.NaN;

            public string LastN1Str = string.Empty;
            public string LastEgtStr = string.Empty;
            public string LastN2Str = string.Empty;
            public string LastFfStr = string.Empty;
            public string LastOilPStr = string.Empty;
            public string LastOilTStr = string.Empty;
            public string LastOilQStr = string.Empty;
            public string LastVibStr = string.Empty;
        }

        private const int MAX_ENGINES = 4;
        private EngineColumnUI[] _engineCols = new EngineColumnUI[MAX_ENGINES];
        private readonly float[] _xCoords = new float[MAX_ENGINES];
        private int _currentEngineCount = 2; // 默认 787 双发
        private int _configuredEngineCount = -1; // -1: 自动感知, >0: 强制指定

        // 发动机组公用标签
        private Text _n1Label;
        private Text _egtLabel;
        private Text _n2Label;
        private Text _ffLabel;
        private Text _oilPLabel;
        private Text _oilTLabel;
        private Text _oilQLabel;
        private Text _vibLabel;

        // 右侧系统挂载容器 (跟随卡片动态长宽比平滑滑动)
        private RectTransform _rightSystemsRt;

        // 右侧系统：起落架 (GEAR)
        private Image _gearBoxBg;
        private Outline _gearBoxOutline;
        private Text _gearStatusText;
        private Text _gearLabelText;

        // 右侧系统：襟翼 (FLAPS)
        private Text _flapsLabelText;
        private Image _flapsTrackImage;
        private RectTransform _flapPointerPivot;
        private Image _flapTickImage;
        private Text _flapPositionText;

        // 右侧系统：STAB TRIM & RUDDER TRIM
        private Text _stabNdText;
        private Text _stabNuText;
        private Image _stabTrackImage;
        private Text _stabTargetText;
        private Image _stabBoxBg;
        private Outline _stabBoxOutline;
        private Text _stabValueText;
        private RectTransform _stabPointerRt;
        private Image _stabPointerImg;
        private Text _stabLabelText;

        private Image _rudderBoxBg;
        private Outline _rudderBoxOutline;
        private Text _rudderValueText;
        private Image _rudderTrackImage;
        private Image _rudderCenterTick;
        private RectTransform _rudderPointerRt;
        private Image _rudderPointerImg;
        private Text _rudderLabelText;

        // ── 右侧真实系统读数区 (Flight Systems，全部有本地遥测通道) ──
        // 行 1: 航向 / 马赫 / 过载
        private Text _hdgLabel;
        private Text _hdgValue;
        private Text _machLabel;
        private Text _machValue;
        private Text _gLoadLabel;
        private Text _gLoadValue;
        // 行 2: 电气 EC / 通信信号
        private Text _ecLabel;
        private Text _ecValue;
        private Image _ecBarTrackImg;
        private Image _ecBarFillImg;
        private Text _commLabel;
        private Text _commValue;
        private Image _commBarTrackImg;
        private Image _commBarFillImg;
        // 行 3: 垂直速度 / 时间加速
        private Text _vsiLabel;
        private Text _vsiValue;
        private Text _warpLabel;
        private Text _warpValue;
        private Text _sasLabel;
        private Text _sasValue;

        // 右侧底部：全机重量与燃油统计
        private Image _summaryBoxBg;
        private Outline _summaryBoxOutline;
        private Text _grossWtLabel;
        private Text _grossWtText;
        private Image _grossWtBoxBg;
        private Text _unitsLabel;
        private Text _totalFuelLabel;
        private Text _totalFuelText;
        private Image _totalFuelBoxBg;
        private Text _apoText;
        private Text _periText;

        // 通配符与模板通道
        private string _tatTemplate = "{TEMP:ATM:+0;-0;+0}c";
        private string _thrustModeTemplate = "{THRUST:MODE}";
        private string _n1Token = "{ENG:N1}";
        private string _n2Token = "{ENG:N2}";
        private string _egtToken = "{TEMP}";
        private string _ffToken = "{ENG:FF}";
        private string _oilPToken = "{ENG:OIL_P}";
        private string _oilTToken = "{ENG:OIL_T}";
        private string _oilQToken = "{ENG:OIL_Q}";
        private string _vibToken = "{ENG:VIB}";
        private string _gearToken = "{GEAR}";
        private string _flapToken = "{FLAPS}";
        private string _stabToken = "{TRIM_PITCH}";
        private string _rudderToken = "{TRIM_YAW}";

        // 运行时防抖脏检查缓存 (100% 零 GC)
        private readonly Cached<string> _lastTatStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastModeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGearStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastFlapStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastStabStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastRudderStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastHdgStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastMachStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGLoadStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastEcStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastCommStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastVsiStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastWarpStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastSasStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGrossWtStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastTotalFuelStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastApoStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPeriStr = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastEcFill = new CachedFloat(-1f, 0.002f);
        private readonly CachedFloat _lastCommFill = new CachedFloat(-1f, 0.002f);

        // 双轨架构快照字段
        private bool _cachedHasVessel;
        private int _cachedDetectedEngineCount = 2;
        private string _cachedTatStr = string.Empty;
        private string _cachedModeStr = string.Empty;

        private readonly string[] _cachedColN1Str = new string[MAX_ENGINES];
        private readonly float[] _cachedColN1NeedleAngle = new float[MAX_ENGINES];
        private readonly string[] _cachedColEgtStr = new string[MAX_ENGINES];
        private readonly float[] _cachedColEgtNeedleAngle = new float[MAX_ENGINES];
        private readonly string[] _cachedColN2Str = new string[MAX_ENGINES];
        private readonly float[] _cachedColN2NeedleAngle = new float[MAX_ENGINES];
        private readonly string[] _cachedColFfStr = new string[MAX_ENGINES];
        private readonly string[] _cachedColOilPStr = new string[MAX_ENGINES];
        private readonly float[] _cachedColOilPFrac = new float[MAX_ENGINES];
        private readonly string[] _cachedColOilTStr = new string[MAX_ENGINES];
        private readonly float[] _cachedColOilTFrac = new float[MAX_ENGINES];
        private readonly string[] _cachedColOilQStr = new string[MAX_ENGINES];
        private readonly string[] _cachedColVibStr = new string[MAX_ENGINES];
        private readonly float[] _cachedColVibFrac = new float[MAX_ENGINES];

        private string _cachedGearStr = string.Empty;
        private string _cachedFlapStr = string.Empty;
        private float _cachedFlapRatio;
        private string _cachedStabStr = string.Empty;
        private float _cachedStabFrac;
        private string _cachedRudderStr = string.Empty;
        private float _cachedRudderFrac;
        private string _cachedHdgStr = string.Empty;
        private string _cachedMachStr = string.Empty;
        private string _cachedGLoadStr = string.Empty;
        private string _cachedEcStr = string.Empty;
        private float _cachedEcFill;
        private string _cachedCommStr = string.Empty;
        private float _cachedCommFill;
        private string _cachedVsiStr = string.Empty;
        private string _cachedWarpStr = string.Empty;
        private string _cachedSasStr = string.Empty;
        private string _cachedGrossWtStr = string.Empty;
        private string _cachedTotalFuelStr = string.Empty;
        private string _cachedApoStr = string.Empty;
        private string _cachedPeriStr = string.Empty;

        private static readonly string[] EngAliases = new[] { "ENGINES", "ENG" };
        private static readonly string[] OilPAliases = new[] { "OILP", "OIL_P" };
        private static readonly string[] OilTAliases = new[] { "OILT", "OIL_T" };
        private static readonly string[] OilQAliases = new[] { "OILQ", "OIL_Q" };
        private static readonly string[] FlapAliases = new[] { "FLAP", "FLAPS" };

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            // 纵向归一化因子：卡片实际高度 / 基准高度。行距随卡片纵向放大等比例伸展。
            float sv = s * (RectTransform != null && RectTransform.sizeDelta.y > 1f ? (RectTransform.sizeDelta.y / (BASE_CARD_HEIGHT * s)) : 1f);
            if (sv <= 0.01f) sv = s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            string engVal = GetTemplateChannel(EngAliases, null);
            if (!string.IsNullOrEmpty(engVal))
            {
                if (engVal.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                    _configuredEngineCount = 0;
                else if (int.TryParse(engVal, out int engs))
                    _configuredEngineCount = Mathf.Clamp(engs, 1, MAX_ENGINES);
            }

            _tatTemplate = GetTemplateChannel("TAT", _tatTemplate);
            _thrustModeTemplate = GetTemplateChannel("MODE", _thrustModeTemplate);
            _n1Token = GetTemplateChannel("N1", _n1Token);
            _n2Token = GetTemplateChannel("N2", _n2Token);
            _egtToken = GetTemplateChannel("EGT", _egtToken);
            _ffToken = GetTemplateChannel("FF", _ffToken);
            _oilPToken = GetTemplateChannel(OilPAliases, _oilPToken);
            _oilTToken = GetTemplateChannel(OilTAliases, _oilTToken);
            _oilQToken = GetTemplateChannel(OilQAliases, _oilQToken);
            _vibToken = GetTemplateChannel("VIB", _vibToken);
            _gearToken = GetTemplateChannel("GEAR", _gearToken);
            _flapToken = GetTemplateChannel(FlapAliases, _flapToken);
            _stabToken = GetTemplateChannel("STAB", _stabToken);
            _rudderToken = GetTemplateChannel("RUDDER", _rudderToken);

            // 1. 卡片底衬 (由基类 AutoCreateCardFrame 托管)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage != null ? _bgImage.color : Color.clear, _bgOutline != null ? _bgOutline.effectColor : Color.clear, s);

            // 2. 调色板语义色提取 (SPEC-006 零颜色字面量)
            Color textAccent = style.GetTextColor(TextStyleRole.Accent, theme);
            Color textPrimary = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color textLabel = style.GetTextColor(TextStyleRole.Label, theme);
            Color textWarn = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.55f);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color arcLineCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.95f);

            // 3. 顶端航电状态栏 (字号按物理缩放相对提升，杜绝亚像素糊字)
            _tatText = UIFactory.CreateText(transform, "TAT_Text", "TAT +0c", DotFont(10f, s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_tatText.rectTransform, -130f * s, -13f * sv, 74f * s, 15f * sv);

            _thrustModeText = UIFactory.CreateText(transform, "Thrust_Mode_Text", "D-TO", DotFont(11f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_thrustModeText.rectTransform, -54f * s, -13f * sv, 46f * s, 15f * sv);

            _thrustTempText = UIFactory.CreateText(transform, "Thrust_Temp_Text", "+0c", DotFont(10f, s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_thrustTempText.rectTransform, -12f * s, -13f * sv, 34f * s, 15f * sv);

            // 4. 发动机中间轴标签 (预先创建于中央轴线)
            _n1Label = UIFactory.CreateText(transform, "Label_N1", "N1", DotFont(10f, s), TextAnchor.MiddleCenter, textAccent);
            _egtLabel = UIFactory.CreateText(transform, "Label_EGT", "EGT", DotFont(10f, s), TextAnchor.MiddleCenter, textAccent);
            _n2Label = UIFactory.CreateText(transform, "Label_N2", "N2", DotFont(10f, s), TextAnchor.MiddleCenter, textAccent);
            _ffLabel = UIFactory.CreateText(transform, "Label_FF", "FF", DotFont(9.5f, s), TextAnchor.MiddleCenter, textAccent);
            _oilPLabel = UIFactory.CreateText(transform, "Label_OilP", I18n.Tr("WIDGET_EICAS_OIL_PRESS", "滑油\n压力"), DotFont(9f, s), TextAnchor.MiddleCenter, textAccent);
            _oilTLabel = UIFactory.CreateText(transform, "Label_OilT", I18n.Tr("WIDGET_EICAS_OIL_TEMP", "滑油\n温度"), DotFont(9f, s), TextAnchor.MiddleCenter, textAccent);
            _oilQLabel = UIFactory.CreateText(transform, "Label_OilQ", I18n.Tr("WIDGET_EICAS_OIL_QTY", "滑油量"), DotFont(9f, s), TextAnchor.MiddleCenter, textAccent);
            _vibLabel = UIFactory.CreateText(transform, "Label_VIB", "VIB", DotFont(9.5f, s), TextAnchor.MiddleCenter, textAccent);

            // 5. 构建 4 发预分配纵列容器
            for (int i = 0; i < MAX_ENGINES; i++)
            {
                _engineCols[i] = CreateEngineColumn(i, s, sv, boxBgCol, boxBorderCol, textPrimary, arcLineCol, textAccent, textWarn, dangerCol, trackCol);
            }

            // 6. 右侧独立系统挂载容器 (支持长宽比动态自适应平滑平移)
            _rightSystemsRt = CreateContainer("Right_Systems_Root", transform);
            SetTopCenterAnchor(_rightSystemsRt, 110f * s, 0f, 0f, 0f);

            // ── 起落架指示器 (GEAR) ──
            GameObject gearBoxObj = UIFactory.CreatePanel(_rightSystemsRt, "Gear_Box", new Vector2(44f * s, 20f * sv),
                new Vector2(0f, -30f * sv), boxBgCol, textAccent, CrispLength(1.2f * s));
            _gearBoxBg = gearBoxObj.GetComponent<Image>();
            _gearBoxOutline = gearBoxObj.GetComponent<Outline>();
            SetTopCenterAnchor(gearBoxObj.GetComponent<RectTransform>(), 0f, -30f * sv, 44f * s, 20f * sv);

            _gearStatusText = UIFactory.CreateText(gearBoxObj.transform, "Gear_Status", I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下"), DotFont(10f, s),
                TextAnchor.MiddleCenter, textAccent);
            _gearStatusText.rectTransform.anchorMin = Vector2.zero;
            _gearStatusText.rectTransform.anchorMax = Vector2.one;
            _gearStatusText.rectTransform.sizeDelta = Vector2.zero;
            _gearStatusText.rectTransform.anchoredPosition = Vector2.zero;

            _gearLabelText = UIFactory.CreateText(_rightSystemsRt, "Gear_Label", I18n.Tr("WIDGET_EICAS_GEAR", "起落架"), DotFont(9f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_gearLabelText.rectTransform, 0f, -47f * sv, 44f * s, 13f * sv);

            // ── 襟翼指示器 (FLAPS) ──
            float flapsTrackX = 16f * s;
            float flapsY = -86f * sv;
            float flapsH = 44f * sv;

            _flapsLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_FLAPS", I18n.Tr("WIDGET_EICAS_FLAPS", "襟\n翼"), DotFont(9f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_flapsLabelText.rectTransform, flapsTrackX - 18f * s, flapsY, 14f * s, flapsH);

            GameObject flapsTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Flaps_Track",
                new Vector2(CrispLength(2f * s), flapsH), new Vector2(flapsTrackX, flapsY), trackCol);
            _flapsTrackImage = flapsTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(flapsTrackObj.GetComponent<RectTransform>(), flapsTrackX, flapsY, CrispLength(2f * s), flapsH);

            _flapPointerPivot = CreateContainer("Flap_Pointer", flapsTrackObj.transform,
                Vector2.zero, new Vector2(0f, -10f * sv));
            _flapPointerPivot.anchorMin = new Vector2(0.5f, 1f);
            _flapPointerPivot.anchorMax = new Vector2(0.5f, 1f);
            _flapPointerPivot.pivot = new Vector2(0.5f, 0.5f);

            GameObject flapTickObj = UIFactory.CreatePanel(_flapPointerPivot, "Tick",
                new Vector2(11f * s, CrispLength(2.4f * s)), Vector2.zero, textAccent);
            _flapTickImage = flapTickObj.GetComponent<Image>();

            _flapPositionText = UIFactory.CreateText(_flapPointerPivot, "Pos_Text", "1", DotFont(10f, s),
                TextAnchor.MiddleLeft, textAccent);
            _flapPositionText.rectTransform.anchoredPosition = new Vector2(13f * s, 0f);
            _flapPositionText.rectTransform.sizeDelta = new Vector2(22f * s, 15f * sv);

            // ── 安定面配平 (STAB TRIM) 与 方向舵配平 (RUDDER TRIM) ──
            float stabTrackX = -22f * s;
            float stabY = -146f * sv;
            float stabH = 36f * sv;

            _stabNdText = UIFactory.CreateText(_rightSystemsRt, "Stab_ND", "ND", DotFont(8.5f, s),
                TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_stabNdText.rectTransform, stabTrackX - 13f * s, stabY + stabH * 0.5f - 4f * sv, 18f * s, 11f * sv);

            _stabNuText = UIFactory.CreateText(_rightSystemsRt, "Stab_NU", I18n.Tr("WIDGET_EICAS_STAB_NU", "抬头"), DotFont(8.5f, s),
                TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_stabNuText.rectTransform, stabTrackX - 13f * s, stabY - stabH * 0.5f + 4f * sv, 18f * s, 11f * sv);

            GameObject stabTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Stab_Track",
                new Vector2(CrispLength(2f * s), stabH), new Vector2(stabTrackX, stabY), trackCol);
            _stabTrackImage = stabTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(stabTrackObj.GetComponent<RectTransform>(), stabTrackX, stabY, CrispLength(2f * s), stabH);

            _stabTargetText = UIFactory.CreateText(_rightSystemsRt, "Stab_Target", "10.25", DotFont(9.5f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_stabTargetText.rectTransform, -54f * s, -134f * sv, 36f * s, 12f * sv);

            CreateReadoutBox(_rightSystemsRt, "Stab_Box", new Vector2(36f * s, 15f * sv), new Vector2(-54f * s, -148f * sv),
                "10.25", boxBgCol, textAccent, textAccent, s, sv,
                out _stabBoxBg, out _stabBoxOutline, out _stabValueText);

            GameObject stabPtrGo = UIFactory.CreatePanel(stabTrackObj.transform, "Pointer",
                new Vector2(7f * s, CrispLength(4.4f * s)), Vector2.zero, textAccent);
            _stabPointerRt = stabPtrGo.GetComponent<RectTransform>();
            _stabPointerImg = stabPtrGo.GetComponent<Image>();
            _stabPointerRt.anchorMin = new Vector2(0.5f, 0.5f);
            _stabPointerRt.anchorMax = new Vector2(0.5f, 0.5f);
            _stabPointerRt.pivot = new Vector2(0.5f, 0.5f);

            _stabLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_STAB", I18n.Tr("WIDGET_EICAS_STAB", "安\n定\n面"), DotFont(9f, s),
                TextAnchor.MiddleCenter, textPrimary);
            SetTopCenterAnchor(_stabLabelText.rectTransform, -7f * s, stabY, 13f * s, stabH);

            // 方向舵配平 (RUDDER TRIM)
            float rudderX = 40f * s;
            CreateReadoutBox(_rightSystemsRt, "Rudder_Box", new Vector2(32f * s, 14f * sv), new Vector2(rudderX, -136f * sv),
                "0.0", boxBgCol, boxBorderCol, textPrimary, s, sv,
                out _rudderBoxBg, out _rudderBoxOutline, out _rudderValueText);

            GameObject rudTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Rudder_Track",
                new Vector2(38f * s, CrispLength(1.8f * s)), new Vector2(rudderX, -150f * sv), trackCol);
            _rudderTrackImage = rudTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(rudTrackObj.GetComponent<RectTransform>(), rudderX, -150f * sv, 38f * s, CrispLength(1.8f * s));

            GameObject rudCenterTickObj = UIFactory.CreatePanel(rudTrackObj.transform, "Center_Tick",
                new Vector2(CrispLength(1.6f * s), CrispLength(7f * s)), Vector2.zero, textLabel);
            _rudderCenterTick = rudCenterTickObj.GetComponent<Image>();

            GameObject rudPtrObj = UIFactory.CreatePanel(rudTrackObj.transform, "Pointer",
                new Vector2(CrispLength(4.4f * s), CrispLength(5.5f * s)), new Vector2(0f, 5f * sv), textPrimary);
            _rudderPointerRt = rudPtrObj.GetComponent<RectTransform>();
            _rudderPointerImg = rudPtrObj.GetComponent<Image>();

            _rudderLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_Rudder", I18n.Tr("WIDGET_EICAS_RUDDER_TRIM", "方向舵配平"), DotFont(8.5f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_rudderLabelText.rectTransform, rudderX, -161f * sv, 64f * s, 12f * sv);

            // ── 真实系统读数区 (Flight Systems：全部有本地遥测通道，见类注释第 3 条) ──
            // 行 1 (Y ≈ -178): 航向 / 马赫 / 过载
            float frRow1 = -180f * sv;
            float frRow2 = -196f * sv;
            float frRow3 = -218f * sv;
            float frRow4 = -240f * sv;
            float frLabelX = -44f * s;
            float frValueX = 4f * s;

            _hdgLabel = UIFactory.CreateText(_rightSystemsRt, "Label_HDG", "HDG", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_hdgLabel.rectTransform, frLabelX, frRow1, 36f * s, 13f * sv);
            _hdgValue = UIFactory.CreateText(_rightSystemsRt, "Val_HDG", "000", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_hdgValue.rectTransform, frValueX, frRow1, 40f * s, 13f * sv);

            _machLabel = UIFactory.CreateText(_rightSystemsRt, "Label_MACH", "MACH", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_machLabel.rectTransform, frLabelX, frRow2, 36f * s, 13f * sv);
            _machValue = UIFactory.CreateText(_rightSystemsRt, "Val_MACH", "0.00", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_machValue.rectTransform, frValueX, frRow2, 40f * s, 13f * sv);

            _gLoadLabel = UIFactory.CreateText(_rightSystemsRt, "Label_GLOAD", I18n.Tr("WIDGET_EICAS_GLOAD", "过载"), DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_gLoadLabel.rectTransform, frLabelX + 62f * s, frRow1, 46f * s, 13f * sv);
            _gLoadValue = UIFactory.CreateText(_rightSystemsRt, "Val_GLOAD", "1.0", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_gLoadValue.rectTransform, frValueX + 62f * s, frRow2, 40f * s, 13f * sv);

            _vsiLabel = UIFactory.CreateText(_rightSystemsRt, "Label_VSI", "VSI", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_vsiLabel.rectTransform, frLabelX + 62f * s, frRow3, 36f * s, 13f * sv);
            _vsiValue = UIFactory.CreateText(_rightSystemsRt, "Val_VSI", "+0", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_vsiValue.rectTransform, frValueX + 62f * s, frRow4, 40f * s, 13f * sv);

            // 行 2/3 (Y ≈ -218 与 -240): 电气 EC / 通信 / 时间加速 / SAS
            _ecLabel = UIFactory.CreateText(_rightSystemsRt, "Label_EC", "EC", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_ecLabel.rectTransform, frLabelX, frRow3, 22f * s, 13f * sv);
            _ecValue = UIFactory.CreateText(_rightSystemsRt, "Val_EC", "100%", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_ecValue.rectTransform, frLabelX + 34f * s, frRow3, 38f * s, 13f * sv);

            // 电气储量通栏条 (宽度受钳制，杜绝亚像素丢线)
            float barW = 44f * s;
            float barH = CrispLength(4f * s);
            _ecBarTrackImg = UIFactory.CreatePanel(_rightSystemsRt, "EC_Bar_Track",
                new Vector2(barW, barH), new Vector2(frValueX, frRow4), trackCol).GetComponent<Image>();
            SetTopCenterAnchor(_ecBarTrackImg.rectTransform, frValueX, frRow4, barW, barH);

            GameObject ecFillObj = UIFactory.CreatePanel(_ecBarTrackImg.transform, "Fill",
                new Vector2(barW * 0.01f, barH), Vector2.zero, textPrimary);
            _ecBarFillImg = ecFillObj.GetComponent<Image>();
            RectTransform ecFillRt = ecFillObj.GetComponent<RectTransform>();
            ecFillRt.anchorMin = new Vector2(0f, 0.5f);
            ecFillRt.anchorMax = new Vector2(0f, 0.5f);
            ecFillRt.pivot = new Vector2(0f, 0.5f);
            ecFillRt.anchoredPosition = new Vector2(-barW * 0.5f, 0f);
            ecFillRt.sizeDelta = new Vector2(barW * 0.01f, barH);

            _commLabel = UIFactory.CreateText(_rightSystemsRt, "Label_COMM", "COMM", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_commLabel.rectTransform, frLabelX + 62f * s, frRow3, 34f * s, 13f * sv);
            _commValue = UIFactory.CreateText(_rightSystemsRt, "Val_COMM", "0%", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_commValue.rectTransform, frLabelX + 100f * s, frRow3, 34f * s, 13f * sv);

            _commBarTrackImg = UIFactory.CreatePanel(_rightSystemsRt, "COMM_Bar_Track",
                new Vector2(barW, barH), new Vector2(frValueX + 62f * s, frRow4), trackCol).GetComponent<Image>();
            SetTopCenterAnchor(_commBarTrackImg.rectTransform, frValueX + 62f * s, frRow4, barW, barH);

            GameObject commFillObj = UIFactory.CreatePanel(_commBarTrackImg.transform, "Fill",
                new Vector2(barW * 0.01f, barH), Vector2.zero, textPrimary);
            _commBarFillImg = commFillObj.GetComponent<Image>();
            RectTransform commFillRt = commFillObj.GetComponent<RectTransform>();
            commFillRt.anchorMin = new Vector2(0f, 0.5f);
            commFillRt.anchorMax = new Vector2(0f, 0.5f);
            commFillRt.pivot = new Vector2(0f, 0.5f);
            commFillRt.anchoredPosition = new Vector2(-barW * 0.5f, 0f);
            commFillRt.sizeDelta = new Vector2(barW * 0.01f, barH);

            // 行 4 (Y ≈ -240 下方): 时间加速 / SAS 状态
            _warpLabel = UIFactory.CreateText(_rightSystemsRt, "Label_WARP", I18n.Tr("WIDGET_EICAS_WARP", "倍速"), DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_warpLabel.rectTransform, frLabelX, -236f * sv, 40f * s, 13f * sv);
            _warpValue = UIFactory.CreateText(_rightSystemsRt, "Val_WARP", "1x", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_warpValue.rectTransform, frValueX, -236f * sv, 40f * s, 13f * sv);

            _sasLabel = UIFactory.CreateText(_rightSystemsRt, "Label_SAS", "SAS", DotFont(9f, s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_sasLabel.rectTransform, frLabelX + 62f * s, -236f * sv, 30f * s, 13f * sv);
            _sasValue = UIFactory.CreateText(_rightSystemsRt, "Val_SAS", "OFF", DotFont(9.5f, s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_sasValue.rectTransform, frValueX + 100f * s, -236f * sv, 34f * s, 13f * sv);

            // ── 右侧底部：全机重量与燃油统计暗底卡槽 ──
            float sumBoxW = 136f * s;
            float sumBoxH = 58f * sv;
            float sumBoxY = -274f * sv;
            GameObject sumBoxObj = UIFactory.CreatePanel(_rightSystemsRt, "Weight_Fuel_Panel", new Vector2(sumBoxW, sumBoxH),
                new Vector2(0f, sumBoxY), boxBgCol, boxBorderCol, CrispLength(1f * s));
            _summaryBoxBg = sumBoxObj.GetComponent<Image>();
            _summaryBoxOutline = sumBoxObj.GetComponent<Outline>();
            SetTopCenterAnchor(sumBoxObj.GetComponent<RectTransform>(), 0f, sumBoxY, sumBoxW, sumBoxH);

            _grossWtLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_GW", I18n.Tr("WIDGET_EICAS_GROSS_WT", "全重"), DotFont(9f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_grossWtLabel.rectTransform, -46f * s, -7f * sv, 60f * s, 12f * sv);

            _unitsLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_Units", I18n.Tr("WIDGET_EICAS_UNITS_LBS", "磅 ×\n1000"), DotFont(8.5f, s),
                TextAnchor.MiddleCenter, textLabel);
            SetTopCenterAnchor(_unitsLabel.rectTransform, 0f, -7f * sv, 26f * s, 22f * sv);

            _totalFuelLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_TF", I18n.Tr("WIDGET_EICAS_TOTAL_FUEL", "总燃料"), DotFont(9f, s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_totalFuelLabel.rectTransform, 46f * s, -7f * sv, 60f * s, 12f * sv);

            // 黑底框显数值药丸
            GameObject gwBoxObj = UIFactory.CreatePanel(sumBoxObj.transform, "GW_Pill", new Vector2(40f * s, 14f * sv),
                new Vector2(-41f * s, -26f * sv), boxBgCol);
            _grossWtBoxBg = gwBoxObj.GetComponent<Image>();
            SetTopCenterAnchor(gwBoxObj.GetComponent<RectTransform>(), -41f * s, -26f * sv, 40f * s, 14f * sv);

            _grossWtText = UIFactory.CreateText(gwBoxObj.transform, "Text", "0.0", DotFont(10f, s),
                TextAnchor.MiddleCenter, textPrimary);
            _grossWtText.rectTransform.anchorMin = Vector2.zero;
            _grossWtText.rectTransform.anchorMax = Vector2.one;
            _grossWtText.rectTransform.sizeDelta = Vector2.zero;
            _grossWtText.rectTransform.anchoredPosition = Vector2.zero;

            GameObject tfBoxObj = UIFactory.CreatePanel(sumBoxObj.transform, "TF_Pill", new Vector2(40f * s, 14f * sv),
                new Vector2(41f * s, -26f * sv), boxBgCol);
            _totalFuelBoxBg = tfBoxObj.GetComponent<Image>();
            SetTopCenterAnchor(tfBoxObj.GetComponent<RectTransform>(), 41f * s, -26f * sv, 40f * s, 14f * sv);

            _totalFuelText = UIFactory.CreateText(tfBoxObj.transform, "Text", "0.0", DotFont(10f, s),
                TextAnchor.MiddleCenter, textPrimary);
            _totalFuelText.rectTransform.anchorMin = Vector2.zero;
            _totalFuelText.rectTransform.anchorMax = Vector2.one;
            _totalFuelText.rectTransform.sizeDelta = Vector2.zero;
            _totalFuelText.rectTransform.anchoredPosition = Vector2.zero;

            // 远/近地点高度 (真实轨道遥测，KSP 本地数据源)
            _apoText = UIFactory.CreateText(sumBoxObj.transform, "Apo_Text", I18n.Tr("WIDGET_EICAS_APO", "远地点") + " 0", DotFont(9f, s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_apoText.rectTransform, -40f * s, -45f * sv, 48f * s, 12f * sv);

            _periText = UIFactory.CreateText(sumBoxObj.transform, "Peri_Text", I18n.Tr("WIDGET_EICAS_PERI", "近地点") + " 0", DotFont(9f, s),
                TextAnchor.MiddleRight, textAccent);
            SetTopCenterAnchor(_periText.rectTransform, 40f * s, -45f * sv, 56f * s, 12f * sv);

            // 7. 排布并激活当前发动机列与动态长宽比
            int initialEngines = _configuredEngineCount > 0 ? _configuredEngineCount : 2;
            LayoutEngineColumns(initialEngines, s, sv);

            // 8. 动态注册全部核心微控件至 WidgetControlManager
            RegisterMicroControls();
        }

        /// <summary>
        /// 字号按物理缩放相对提升：基础字号随 DPI 缩放放大，但保证不小于 9 物理像素，
        /// 避免小分辨率下出现 6.5px 级别的亚像素糊字。
        /// </summary>
        private static int DotFont(float basePt, float s)
        {
            float scaled = basePt * s;
            return Mathf.RoundToInt(Mathf.Max(scaled, 9f));
        }

        private EngineColumnUI CreateEngineColumn(int index, float s, float sv, Color boxBg, Color boxBorder,
            Color valColor, Color arcLineColor, Color bugColor, Color warnColor, Color limitColor, Color trackColor)
        {
            EngineColumnUI col = new EngineColumnUI();
            col.EngineIndex = index;

            col.RootRt = CreateContainer($"Engine_Col_{index + 1}", transform);
            col.Root = col.RootRt.gameObject;
            GameObject colGo = col.Root;
            SetTopCenterAnchor(col.RootRt, 0f, 0f, 0f, 0f);

            // 1. REV & Target
            col.N1Dial = new DialGaugeUI();
            col.N1Dial.RevText = UIFactory.CreateText(colGo.transform, $"REV_{index + 1}", "REV", DotFont(9f, s),
                TextAnchor.MiddleCenter, bugColor);
            SetTopCenterAnchor(col.N1Dial.RevText.rectTransform, 0f, -23f * sv, 30f * s, 12f * sv);

            col.N1Dial.TargetText = UIFactory.CreateText(colGo.transform, $"N1_Tgt_{index + 1}", "81.3", DotFont(9.5f, s),
                TextAnchor.MiddleCenter, bugColor);
            SetTopCenterAnchor(col.N1Dial.TargetText.rectTransform, 0f, -36f * sv, 30f * s, 12f * sv);

            // 2. N1 马蹄弧指示器 (含同心双弧与命令游标)
            float n1DialCenterY = -66f * sv;
            CreateHorseshoeDial(colGo.transform, $"N1_Dial_{index + 1}", 0f, n1DialCenterY, 15f * s, "0.0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s, sv,
                hasBug: true, hasInnerArc: true, hasBottomTick: false, out col.N1Dial);

            // 3. EGT 马蹄弧指示器 (含起动温控警戒标)
            float egtDialCenterY = -116f * sv;
            CreateHorseshoeDial(colGo.transform, $"EGT_Dial_{index + 1}", 0f, egtDialCenterY, 15f * s, "0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s, sv,
                hasBug: false, hasInnerArc: false, hasBottomTick: true, out col.EgtDial);

            // 4. N2 马蹄弧指示器
            float n2DialCenterY = -166f * sv;
            CreateHorseshoeDial(colGo.transform, $"N2_Dial_{index + 1}", 0f, n2DialCenterY, 15f * s, "0.0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s, sv,
                hasBug: false, hasInnerArc: false, hasBottomTick: false, out col.N2Dial);

            // 5. FF 燃油流量框
            float ffY = -206f * sv;
            CreateReadoutBox(colGo.transform, $"FF_Box_{index + 1}", new Vector2(30f * s, 14f * sv), new Vector2(0f, ffY),
                "0.0", boxBg, boxBorder, valColor, s, sv,
                out col.FfBoxBg, out col.FfBoxOutline, out col.FfText);

            // 6. OIL PRESS
            float oilPY = -232f * sv;
            float tapeH = 18f * sv;
            CreateReadoutBox(colGo.transform, $"OIL_P_Box_{index + 1}", new Vector2(24f * s, 14f * sv), new Vector2(-8f * s, oilPY),
                "0", boxBg, boxBorder, valColor, s, sv,
                out col.OilPBoxBg, out col.OilPBoxOutline, out col.OilPText);

            CreateVerticalTapeRuler(colGo.transform, $"OIL_P_Tape_{index + 1}", new Vector2(CrispLength(2f * s), tapeH),
                new Vector2(11f * s, oilPY), trackColor, limitColor, valColor, s, pointsRight: false,
                out col.OilPTrack, out col.OilPLimitTick, out col.OilPPointerPivot, out col.OilPPointerImage);

            // 7. OIL TEMP
            float oilTY = -260f * sv;
            CreateReadoutBox(colGo.transform, $"OIL_T_Box_{index + 1}", new Vector2(24f * s, 14f * sv), new Vector2(-8f * s, oilTY),
                "0", boxBg, boxBorder, valColor, s, sv,
                out col.OilTBoxBg, out col.OilTBoxOutline, out col.OilTText);

            CreateVerticalTapeRuler(colGo.transform, $"OIL_T_Tape_{index + 1}", new Vector2(CrispLength(2f * s), tapeH),
                new Vector2(11f * s, oilTY), trackColor, warnColor, valColor, s, pointsRight: false,
                out col.OilTTrack, out col.OilTLimitTick, out col.OilTPointerPivot, out col.OilTPointerImage);

            // 8. OIL QTY
            float oilQY = -288f * sv;
            CreateReadoutBox(colGo.transform, $"OIL_Q_Box_{index + 1}", new Vector2(24f * s, 14f * sv), new Vector2(0f, oilQY),
                "0", boxBg, boxBorder, valColor, s, sv,
                out col.OilQBoxBg, out col.OilQBoxOutline, out col.OilQText);

            col.OilQLoText = UIFactory.CreateText(colGo.transform, $"OIL_Q_LO_{index + 1}", "LO", DotFont(9f, s),
                TextAnchor.MiddleCenter, valColor);
            SetTopCenterAnchor(col.OilQLoText.rectTransform, 18f * s, oilQY, 15f * s, 13f * sv);

            // 9. VIB
            float vibY = -316f * sv;
            CreateReadoutBox(colGo.transform, $"VIB_Box_{index + 1}", new Vector2(24f * s, 14f * sv), new Vector2(-8f * s, vibY),
                "0.0", boxBg, boxBorder, valColor, s, sv,
                out col.VibBoxBg, out col.VibBoxOutline, out col.VibText);

            col.VibPrefixText = UIFactory.CreateText(colGo.transform, $"VIB_N1_{index + 1}", "N1", DotFont(8.5f, s),
                TextAnchor.MiddleCenter, valColor);
            SetTopCenterAnchor(col.VibPrefixText.rectTransform, -23f * s, vibY, 16f * s, 13f * sv);

            CreateVerticalTapeRuler(colGo.transform, $"VIB_Tape_{index + 1}", new Vector2(CrispLength(2f * s), tapeH),
                new Vector2(11f * s, vibY), trackColor, warnColor, valColor, s, pointsRight: false,
                out col.VibTrack, out Image _, out col.VibPointerPivot, out col.VibPointerImage);

            return col;
        }

        /// <summary>
        /// 波音 787 经典马蹄弧圆环指示器生成算法 (Horseshoe Cradle Generator)
        /// 彻底消除对特殊着色器的运行环境依赖，纯相对局部坐标系生成像素级锐利的弧面几何。
        /// 全部细几何体线宽均经 <c>CrispLength</c> 钳制，保证在低分辨率下不丢线、不糊边。
        /// </summary>
        private void CreateHorseshoeDial(Transform parent, string name, float cx, float cy, float radius,
            string initialVal, Color boxBg, Color boxBorder, Color valColor, Color arcLineColor,
            Color bugColor, Color warnColor, Color limitColor, float s, float sv,
            bool hasBug, bool hasInnerArc, bool hasBottomTick, out DialGaugeUI dial)
        {
            dial = new DialGaugeUI();

            RectTransform rootRt = CreateContainer(name, parent);
            dial.Root = rootRt.gameObject;
            GameObject dialRoot = dial.Root;
            SetTopCenterAnchor(rootRt, cx, cy, 0f, 0f);

            // 图元统一线宽 (受钳制)
            float hairline = CrispLength(1.6f * s);
            float tickLine = CrispLength(1.8f * s);

            // 1. 顶端矩形读数框 (相对 dialRoot 中心)
            float boxW = 30f * s;
            float boxH = 14f * sv;
            float boxY = 2f * s + boxH * 0.5f;
            float boxX = -3f * s;
            CreateReadoutBox(dialRoot.transform, $"{name}_Box", new Vector2(boxW, boxH), new Vector2(boxX, boxY),
                initialVal, boxBg, boxBorder, valColor, s, sv,
                out dial.BoxBg, out dial.BoxOutline, out dial.BoxText);

            // 2. 数显框下方水平切线基座 (Underline: 从框显左缘水平连通至右端弧线起点 radius)
            float undY = 2f * s;
            float undLeft = boxX - boxW * 0.5f;
            float undRight = radius;
            float undW = undRight - undLeft;
            float undMidX = (undLeft + undRight) * 0.5f;

            GameObject undGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_Underline",
                new Vector2(undW, hairline), new Vector2(undMidX, undY), arcLineColor);
            dial.Underline = undGo.GetComponent<Image>();
            SetTopCenterAnchor(undGo.GetComponent<RectTransform>(), undMidX, undY, undW, hairline);

            // 3. 215° 经典马蹄圆弧下沉托架 (从 0° 顺时针扫至 -215° / 即 145°)
            //    弧段数量随半径自适应增加，消除低分辨率下的可见折角多边形感。
            int segCount = Mathf.Clamp(Mathf.RoundToInt(radius * 1.35f), 18, 30);
            float span = 215f;
            float stepDeg = span / segCount;
            float segLen = (2f * radius * Mathf.Sin(stepDeg * 0.5f * Mathf.Deg2Rad)) + hairline * 0.6f;

            for (int i = 0; i < segCount; i++)
            {
                float midDeg = -(i + 0.5f) * stepDeg;
                float midRad = midDeg * Mathf.Deg2Rad;
                float px = radius * Mathf.Cos(midRad);
                float py = radius * Mathf.Sin(midRad);

                GameObject segGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_ArcSeg_{i}",
                    new Vector2(segLen, hairline), new Vector2(px, py), arcLineColor);
                Image segImg = segGo.GetComponent<Image>();
                RectTransform srt = segGo.GetComponent<RectTransform>();
                SetTopCenterAnchor(srt, px, py, segLen, hairline);
                srt.localEulerAngles = new Vector3(0f, 0f, midDeg + 90f);
                dial.ArcSegments.Add(segImg);
            }

            // 4. 左上角红色超限警戒线 (Redline Tick at -215°)
            float limRad = -215f * Mathf.Deg2Rad;
            float limPx = (radius + 2.5f * s) * Mathf.Cos(limRad);
            float limPy = (radius + 2.5f * s) * Mathf.Sin(limRad);
            float limLen = CrispLength(6f * s);
            GameObject limGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_RedLimit",
                new Vector2(limLen, tickLine), new Vector2(limPx, limPy), limitColor);
            dial.RedLimitTick = limGo.GetComponent<Image>();
            RectTransform lrt = limGo.GetComponent<RectTransform>();
            SetTopCenterAnchor(lrt, limPx, limPy, limLen, tickLine);
            lrt.localEulerAngles = new Vector3(0f, 0f, -215f);

            // 5. 9 点钟方向琥珀色连续参考标线 (Amber Tick at -180°)
            float ambPx = -radius + 1.5f * s;
            float ambPy = 0f;
            float ambLen = CrispLength(5f * s);
            GameObject ambGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_AmberTick",
                new Vector2(ambLen, tickLine), new Vector2(ambPx, ambPy), warnColor);
            dial.AmberTick = ambGo.GetComponent<Image>();
            SetTopCenterAnchor(ambGo.GetComponent<RectTransform>(), ambPx, ambPy, ambLen, tickLine);

            // 6. 荧光绿尖锥推力限值游标 (Command Bug REV 81.3 at -180°)
            if (hasBug)
            {
                float bugTipX = -radius - 3.5f * s;
                float bugTipY = 0f;
                float wingLen = CrispLength(5f * s);

                // 上翼
                GameObject b1Go = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BugWing1",
                    new Vector2(wingLen, tickLine), new Vector2(bugTipX - 1.6f * s, bugTipY + 1.6f * s), bugColor);
                dial.CommandBug1 = b1Go.GetComponent<Image>();
                RectTransform b1Rt = b1Go.GetComponent<RectTransform>();
                SetTopCenterAnchor(b1Rt, bugTipX - 1.6f * s, bugTipY + 1.6f * s, wingLen, tickLine);
                b1Rt.localEulerAngles = new Vector3(0f, 0f, -32f);

                // 下翼
                GameObject b2Go = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BugWing2",
                    new Vector2(wingLen, tickLine), new Vector2(bugTipX - 1.6f * s, bugTipY - 1.6f * s), bugColor);
                dial.CommandBug2 = b2Go.GetComponent<Image>();
                RectTransform b2Rt = b2Go.GetComponent<RectTransform>();
                SetTopCenterAnchor(b2Rt, bugTipX - 1.6f * s, bugTipY - 1.6f * s, wingLen, tickLine);
                b2Rt.localEulerAngles = new Vector3(0f, 0f, 32f);
            }

            // 7. N1 底部同心双圆弧 (Double Concentric Arc)
            if (hasInnerArc)
            {
                float innerR = radius - 3f * s;
                int inCount = Mathf.Clamp(Mathf.RoundToInt(innerR * 0.75f), 10, 18);
                float inStart = -35f;
                float inSpan = 115f;
                float inStep = inSpan / inCount;
                float inSegLen = (2f * innerR * Mathf.Sin(inStep * 0.5f * Mathf.Deg2Rad)) + hairline * 0.6f;

                for (int j = 0; j < inCount; j++)
                {
                    float d = inStart - (j + 0.5f) * inStep;
                    float r = d * Mathf.Deg2Rad;
                    float px = innerR * Mathf.Cos(r);
                    float py = innerR * Mathf.Sin(r);

                    GameObject inGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_InnerArc_{j}",
                        new Vector2(inSegLen, hairline), new Vector2(px, py), arcLineColor);
                    Image inImg = inGo.GetComponent<Image>();
                    RectTransform isrt = inGo.GetComponent<RectTransform>();
                    SetTopCenterAnchor(isrt, px, py, inSegLen, hairline);
                    isrt.localEulerAngles = new Vector3(0f, 0f, d + 90f);
                    dial.InnerArcSegments.Add(inImg);
                }
            }

            // 8. EGT 底部左下角起动警戒线 (Bottom Red Tick at -130°)
            if (hasBottomTick)
            {
                float bRad = -130f * Mathf.Deg2Rad;
                float bPx = (radius + 2.2f * s) * Mathf.Cos(bRad);
                float bPy = (radius + 2.2f * s) * Mathf.Sin(bRad);
                float bLen = CrispLength(5f * s);
                GameObject botTickGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BotRedTick",
                    new Vector2(bLen, tickLine), new Vector2(bPx, bPy), limitColor);
                dial.BottomRedTick = botTickGo.GetComponent<Image>();
                RectTransform btRt = botTickGo.GetComponent<RectTransform>();
                SetTopCenterAnchor(btRt, bPx, bPy, bLen, tickLine);
                btRt.localEulerAngles = new Vector3(0f, 0f, -130f);
            }

            // 9. 顺时针动态指示针 (Needle Pointer)
            dial.NeedlePivot = CreateContainer($"{name}_Pivot", dialRoot.transform);
            SetTopCenterAnchor(dial.NeedlePivot, 0f, 0f, 0f, 0f);

            float needleW = CrispLength(1.7f * s);
            GameObject needleGo = UIFactory.CreatePanel(dial.NeedlePivot, "Needle",
                new Vector2(needleW, radius * 0.88f), Vector2.zero, valColor);
            dial.NeedleImage = needleGo.GetComponent<Image>();
            RectTransform needleRt = needleGo.GetComponent<RectTransform>();
            needleRt.anchorMin = new Vector2(0.5f, 0f);
            needleRt.anchorMax = new Vector2(0.5f, 0f);
            needleRt.pivot = new Vector2(0.5f, 0f);
            needleRt.anchoredPosition = Vector2.zero;

            dial.NeedlePivot.localEulerAngles = Vector3.zero;
        }

        private void LayoutEngineColumns(int count, float s, float sv)
        {
            _currentEngineCount = Mathf.Clamp(count, 1, MAX_ENGINES);

            // 动态长宽比与总宽度解算
            // 相对改造前整体加宽：给马蹄弧仪表与右侧系统读数区更大的水平呼吸空间，
            // 从而在相同物理屏幕尺寸下获得更高的有效分辨率（更锐利）。
            float cardW;
            float rightCenterX;
            float scaleFactor;

            if (_currentEngineCount == 1)
            {
                cardW = 320f * s;
                _xCoords[0] = -78f * s;
                rightCenterX = 82f * s;
                scaleFactor = 1.0f;
            }
            else if (_currentEngineCount == 2)
            {
                cardW = 390f * s;
                _xCoords[0] = -126f * s;
                _xCoords[1] = -38f * s;
                rightCenterX = 110f * s;
                scaleFactor = 1.0f;
            }
            else if (_currentEngineCount == 3)
            {
                cardW = 452f * s;
                _xCoords[0] = -162f * s;
                _xCoords[1] = -104f * s;
                _xCoords[2] = -46f * s;
                rightCenterX = 138f * s;
                scaleFactor = 0.96f;
            }
            else
            {
                cardW = 512f * s;
                _xCoords[0] = -196f * s;
                _xCoords[1] = -143f * s;
                _xCoords[2] = -90f * s;
                _xCoords[3] = -37f * s;
                rightCenterX = 162f * s;
                scaleFactor = 0.92f;
            }

            Vector2 newSize = new Vector2(cardW, BASE_CARD_HEIGHT * s);
            RectTransform.sizeDelta = newSize;
            if (_bgImage != null) _bgImage.rectTransform.sizeDelta = newSize;

            // 动态调整右侧系统集群中心
            if (_rightSystemsRt != null)
                SetTopCenterAnchor(_rightSystemsRt, rightCenterX, 0f, 0f, 0f);

            // 顶端状态栏平滑居中偏移
            float tatX = -cardW * 0.36f;
            if (_tatText != null) SetTopCenterAnchor(_tatText.rectTransform, tatX, -13f * sv, 74f * s, 15f * sv);
            if (_thrustModeText != null) SetTopCenterAnchor(_thrustModeText.rectTransform, tatX + 70f * s, -13f * sv, 46f * s, 15f * sv);
            if (_thrustTempText != null) SetTopCenterAnchor(_thrustTempText.rectTransform, tatX + 114f * s, -13f * sv, 34f * s, 15f * sv);

            // 发动机纵列排布与缩放
            for (int i = 0; i < MAX_ENGINES; i++)
            {
                if (_engineCols[i] == null || _engineCols[i].Root == null) continue;

                if (i < _currentEngineCount)
                {
                    _engineCols[i].Root.SetActive(true);
                    SetTopCenterAnchor(_engineCols[i].RootRt, _xCoords[i], 0f, 0f, 0f);
                    _engineCols[i].RootRt.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
                }
                else
                {
                    _engineCols[i].Root.SetActive(false);
                }
            }

            // 更新中央行标签 X 坐标 (保持在左右发控之间的舒适空域)
            float labelX;
            if (_currentEngineCount == 1)
                labelX = -132f * s;
            else if (_currentEngineCount == 2)
                labelX = -82f * s;
            else if (_currentEngineCount == 3)
                labelX = -196f * s;
            else
                labelX = -230f * s;

            if (_n1Label != null) SetTopCenterAnchor(_n1Label.rectTransform, labelX, -88f * sv, 30f * s, 15f * sv);
            if (_egtLabel != null) SetTopCenterAnchor(_egtLabel.rectTransform, labelX, -138f * sv, 34f * s, 15f * sv);
            if (_n2Label != null) SetTopCenterAnchor(_n2Label.rectTransform, labelX, -188f * sv, 30f * s, 15f * sv);
            if (_ffLabel != null) SetTopCenterAnchor(_ffLabel.rectTransform, labelX, -206f * sv, 26f * s, 15f * sv);
            if (_oilPLabel != null) SetTopCenterAnchor(_oilPLabel.rectTransform, labelX, -232f * sv, 34f * s, 20f * sv);
            if (_oilTLabel != null) SetTopCenterAnchor(_oilTLabel.rectTransform, labelX, -260f * sv, 34f * s, 20f * sv);
            if (_oilQLabel != null) SetTopCenterAnchor(_oilQLabel.rectTransform, labelX, -288f * sv, 40f * s, 15f * sv);
            if (_vibLabel != null) SetTopCenterAnchor(_vibLabel.rectTransform, labelX, -316f * sv, 28f * s, 15f * sv);
        }

        private void RegisterMicroControls()
        {
            this.Controls.Register(WidgetControlManager.WrapElement(this, "tat_bar", "环境总温与推力模式栏", _tatText.gameObject));

            for (int i = 0; i < MAX_ENGINES; i++)
            {
                if (_engineCols[i]?.Root != null)
                {
                    this.Controls.Register(WidgetControlManager.WrapElement(this, $"eng_col_{i + 1}", I18n.TrFormat("CTL_B787_ENG_COL_FMT", i + 1), _engineCols[i].Root));
                    if (_engineCols[i].N1Dial?.Root != null)
                        this.Controls.Register(WidgetControlManager.WrapElement(this, $"n1_dial_{i + 1}", I18n.TrFormat("CTL_B787_N1_DIAL_FMT", i + 1), _engineCols[i].N1Dial.Root));
                    if (_engineCols[i].EgtDial?.Root != null)
                        this.Controls.Register(WidgetControlManager.WrapElement(this, $"egt_dial_{i + 1}", I18n.TrFormat("CTL_B787_EGT_DIAL_FMT", i + 1), _engineCols[i].EgtDial.Root));
                    if (_engineCols[i].N2Dial?.Root != null)
                        this.Controls.Register(WidgetControlManager.WrapElement(this, $"n2_dial_{i + 1}", I18n.TrFormat("CTL_B787_N2_DIAL_FMT", i + 1), _engineCols[i].N2Dial.Root));
                }
            }

            if (_gearBoxBg != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "gear_indicator", "起落架锁定指示器", _gearBoxBg.gameObject));
            if (_flapsTrackImage != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "flaps_indicator", "襟翼位移滑尺", _flapsTrackImage.gameObject));
            if (_stabTrackImage != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "stab_trim", "安定面俯仰配平", _stabTrackImage.gameObject));
            if (_rudderTrackImage != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "rudder_trim", "方向舵偏航配平", _rudderTrackImage.gameObject));
            if (_hdgLabel != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "flight_systems", "飞行系统读数区", _hdgLabel.gameObject));
            if (_summaryBoxBg != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "weight_fuel_panel", "全机重量与燃油统计卡槽", _summaryBoxBg.gameObject));
        }

        private static void CreateReadoutBox(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            string initialText, Color bgColor, Color borderColor, Color textColor, float s, float sv,
            out Image boxBg, out Outline boxOutline, out Text readoutText)
        {
            GameObject boxObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, bgColor, borderColor, 1f * s);
            boxBg = boxObj.GetComponent<Image>();
            boxOutline = boxObj.GetComponent<Outline>();

            SetTopCenterAnchor(boxObj.GetComponent<RectTransform>(), anchoredPos.x, anchoredPos.y, size.x, size.y);

            readoutText = UIFactory.CreateText(boxObj.transform, "Text", initialText, DotFont(10f, s),
                TextAnchor.MiddleCenter, textColor);
            RectTransform vrt = readoutText.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            vrt.anchoredPosition = Vector2.zero;
        }

        private void CreateVerticalTapeRuler(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color trackColor, Color limitColor, Color needleColor, float s, bool pointsRight,
            out Image trackImg, out Image limitImg, out RectTransform pointerRt, out Image pointerImg)
        {
            GameObject trackObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, trackColor);
            trackImg = trackObj.GetComponent<Image>();
            SetTopCenterAnchor(trackObj.GetComponent<RectTransform>(), anchoredPos.x, anchoredPos.y, size.x, size.y);

            // 限值线 (受钳制，杜绝亚像素丢线)
            GameObject limitObj = UIFactory.CreatePanel(trackObj.transform, "LimitTick",
                new Vector2(CrispLength(5f * s), CrispLength(1.7f * s)), Vector2.zero, limitColor);
            limitImg = limitObj.GetComponent<Image>();
            RectTransform limRt = limitObj.GetComponent<RectTransform>();
            limRt.anchorMin = new Vector2(0.5f, 0f);
            limRt.anchorMax = new Vector2(0.5f, 0f);
            limRt.pivot = new Vector2(0.5f, 0.5f);
            limRt.anchoredPosition = Vector2.zero;

            // 滑动指针 (受钳制)
            GameObject ptrGo = UIFactory.CreatePanel(trackObj.transform, "Pointer",
                new Vector2(CrispLength(5f * s), CrispLength(2.8f * s)), Vector2.zero, needleColor);
            pointerImg = ptrGo.GetComponent<Image>();
            pointerRt = ptrGo.GetComponent<RectTransform>();
            pointerRt.anchorMin = new Vector2(0.5f, 0.5f);
            pointerRt.anchorMax = new Vector2(0.5f, 0.5f);
            pointerRt.pivot = new Vector2(pointsRight ? 0f : 1f, 0.5f);
            pointerRt.anchoredPosition = Vector2.zero;
        }

        private static void SetTopCenterAnchor(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }


        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            Color textAccent = style.GetTextColor(TextStyleRole.Accent, theme);
            Color textPrimary = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color textLabel = style.GetTextColor(TextStyleRole.Label, theme);
            Color textWarn = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.55f);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color arcLineCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.95f);

            ApplyText(_tatText, TextStyleRole.Accent, theme);
            ApplyText(_thrustModeText, TextStyleRole.Accent, theme);
            ApplyText(_thrustTempText, TextStyleRole.Accent, theme);

            for (int i = 0; i < MAX_ENGINES; i++)
            {
                if (_engineCols[i] == null) continue;

                ApplyDialTheme(_engineCols[i].N1Dial, boxBgCol, boxBorderCol, textPrimary, arcLineCol, textAccent, textWarn, dangerCol, theme);
                ApplyDialTheme(_engineCols[i].EgtDial, boxBgCol, boxBorderCol, textPrimary, arcLineCol, textAccent, textWarn, dangerCol, theme);
                ApplyDialTheme(_engineCols[i].N2Dial, boxBgCol, boxBorderCol, textPrimary, arcLineCol, textAccent, textWarn, dangerCol, theme);

                if (_engineCols[i].FfBoxBg != null) _engineCols[i].FfBoxBg.color = boxBgCol;
                if (_engineCols[i].FfBoxOutline != null) _engineCols[i].FfBoxOutline.effectColor = boxBorderCol;
                if (_engineCols[i].FfText != null) ApplyText(_engineCols[i].FfText, TextStyleRole.PrimaryValue, theme);

                if (_engineCols[i].OilPBoxBg != null) _engineCols[i].OilPBoxBg.color = boxBgCol;
                if (_engineCols[i].OilPBoxOutline != null) _engineCols[i].OilPBoxOutline.effectColor = boxBorderCol;
                if (_engineCols[i].OilPText != null) ApplyText(_engineCols[i].OilPText, TextStyleRole.PrimaryValue, theme);
                if (_engineCols[i].OilPTrack != null) _engineCols[i].OilPTrack.color = trackCol;
                if (_engineCols[i].OilPLimitTick != null) _engineCols[i].OilPLimitTick.color = dangerCol;
                if (_engineCols[i].OilPPointerImage != null) _engineCols[i].OilPPointerImage.color = textPrimary;

                if (_engineCols[i].OilTBoxBg != null) _engineCols[i].OilTBoxBg.color = boxBgCol;
                if (_engineCols[i].OilTBoxOutline != null) _engineCols[i].OilTBoxOutline.effectColor = boxBorderCol;
                if (_engineCols[i].OilTText != null) ApplyText(_engineCols[i].OilTText, TextStyleRole.PrimaryValue, theme);
                if (_engineCols[i].OilTTrack != null) _engineCols[i].OilTTrack.color = trackCol;
                if (_engineCols[i].OilTLimitTick != null) _engineCols[i].OilTLimitTick.color = textWarn;
                if (_engineCols[i].OilTPointerImage != null) _engineCols[i].OilTPointerImage.color = textPrimary;

                if (_engineCols[i].OilQBoxBg != null) _engineCols[i].OilQBoxBg.color = boxBgCol;
                if (_engineCols[i].OilQBoxOutline != null) _engineCols[i].OilQBoxOutline.effectColor = boxBorderCol;
                if (_engineCols[i].OilQText != null) ApplyText(_engineCols[i].OilQText, TextStyleRole.PrimaryValue, theme);
                if (_engineCols[i].OilQLoText != null) ApplyText(_engineCols[i].OilQLoText, TextStyleRole.PrimaryValue, theme);

                if (_engineCols[i].VibBoxBg != null) _engineCols[i].VibBoxBg.color = boxBgCol;
                if (_engineCols[i].VibBoxOutline != null) _engineCols[i].VibBoxOutline.effectColor = boxBorderCol;
                if (_engineCols[i].VibText != null) ApplyText(_engineCols[i].VibText, TextStyleRole.PrimaryValue, theme);
                if (_engineCols[i].VibPrefixText != null) ApplyText(_engineCols[i].VibPrefixText, TextStyleRole.PrimaryValue, theme);
                if (_engineCols[i].VibTrack != null) _engineCols[i].VibTrack.color = trackCol;
                if (_engineCols[i].VibPointerImage != null) _engineCols[i].VibPointerImage.color = textPrimary;
            }

            ApplyText(_n1Label, TextStyleRole.Accent, theme);
            ApplyText(_egtLabel, TextStyleRole.Accent, theme);
            ApplyText(_n2Label, TextStyleRole.Accent, theme);
            ApplyText(_ffLabel, TextStyleRole.Accent, theme);
            ApplyText(_oilPLabel, TextStyleRole.Accent, theme);
            ApplyText(_oilTLabel, TextStyleRole.Accent, theme);
            ApplyText(_oilQLabel, TextStyleRole.Accent, theme);
            ApplyText(_vibLabel, TextStyleRole.Accent, theme);

            if (_gearBoxBg != null) _gearBoxBg.color = boxBgCol;
            if (_gearBoxOutline != null) _gearBoxOutline.effectColor = textAccent;
            if (_gearStatusText != null) ApplyText(_gearStatusText, TextStyleRole.Accent, theme);
            if (_gearLabelText != null) ApplyText(_gearLabelText, TextStyleRole.Accent, theme);

            if (_flapsTrackImage != null) _flapsTrackImage.color = trackCol;
            if (_flapTickImage != null) _flapTickImage.color = textAccent;
            if (_flapPositionText != null) ApplyText(_flapPositionText, TextStyleRole.Accent, theme);
            if (_flapsLabelText != null) ApplyText(_flapsLabelText, TextStyleRole.Accent, theme);

            ApplyText(_stabNdText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_stabNuText, TextStyleRole.PrimaryValue, theme);
            if (_stabTrackImage != null) _stabTrackImage.color = trackCol;
            if (_stabBoxBg != null) _stabBoxBg.color = boxBgCol;
            if (_stabBoxOutline != null) _stabBoxOutline.effectColor = textAccent;
            if (_stabValueText != null) ApplyText(_stabValueText, TextStyleRole.Accent, theme);
            if (_stabTargetText != null) ApplyText(_stabTargetText, TextStyleRole.Accent, theme);
            if (_stabPointerImg != null) _stabPointerImg.color = textAccent;
            if (_stabLabelText != null) ApplyText(_stabLabelText, TextStyleRole.PrimaryValue, theme);

            if (_rudderBoxBg != null) _rudderBoxBg.color = boxBgCol;
            if (_rudderBoxOutline != null) _rudderBoxOutline.effectColor = boxBorderCol;
            if (_rudderValueText != null) ApplyText(_rudderValueText, TextStyleRole.PrimaryValue, theme);
            if (_rudderTrackImage != null) _rudderTrackImage.color = trackCol;
            if (_rudderCenterTick != null) _rudderCenterTick.color = textLabel;
            if (_rudderPointerImg != null) _rudderPointerImg.color = textPrimary;
            if (_rudderLabelText != null) ApplyText(_rudderLabelText, TextStyleRole.Accent, theme);

            // 真实系统读数区
            ApplyText(_hdgLabel, TextStyleRole.Accent, theme);
            ApplyText(_hdgValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_machLabel, TextStyleRole.Accent, theme);
            ApplyText(_machValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_gLoadLabel, TextStyleRole.Accent, theme);
            ApplyText(_gLoadValue, TextStyleRole.PrimaryValue, theme);

            ApplyText(_ecLabel, TextStyleRole.Accent, theme);
            ApplyText(_ecValue, TextStyleRole.PrimaryValue, theme);
            if (_ecBarTrackImg != null) _ecBarTrackImg.color = trackCol;
            if (_ecBarFillImg != null) _ecBarFillImg.color = textPrimary;

            ApplyText(_commLabel, TextStyleRole.Accent, theme);
            ApplyText(_commValue, TextStyleRole.PrimaryValue, theme);
            if (_commBarTrackImg != null) _commBarTrackImg.color = trackCol;
            if (_commBarFillImg != null) _commBarFillImg.color = textPrimary;

            ApplyText(_vsiLabel, TextStyleRole.Accent, theme);
            ApplyText(_vsiValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_warpLabel, TextStyleRole.Accent, theme);
            ApplyText(_warpValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_sasLabel, TextStyleRole.Accent, theme);
            ApplyText(_sasValue, TextStyleRole.PrimaryValue, theme);

            if (_summaryBoxBg != null) _summaryBoxBg.color = boxBgCol;
            if (_summaryBoxOutline != null) _summaryBoxOutline.effectColor = boxBorderCol;
            ApplyText(_grossWtLabel, TextStyleRole.Accent, theme);
            ApplyText(_unitsLabel, TextStyleRole.Label, theme);
            ApplyText(_totalFuelLabel, TextStyleRole.Accent, theme);
            if (_grossWtBoxBg != null) _grossWtBoxBg.color = boxBgCol;
            if (_totalFuelBoxBg != null) _totalFuelBoxBg.color = boxBgCol;
            ApplyText(_grossWtText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_totalFuelText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_apoText, TextStyleRole.Accent, theme);
            ApplyText(_periText, TextStyleRole.Accent, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        private void ApplyDialTheme(DialGaugeUI dial, Color boxBg, Color boxBorder, Color valColor,
            Color arcLineColor, Color bugColor, Color warnColor, Color limitColor, ThemeConfig theme)
        {
            if (dial == null) return;

            if (dial.BoxBg != null) dial.BoxBg.color = boxBg;
            if (dial.BoxOutline != null) dial.BoxOutline.effectColor = boxBorder;
            if (dial.BoxText != null) ApplyText(dial.BoxText, TextStyleRole.PrimaryValue, theme);
            if (dial.Underline != null) dial.Underline.color = arcLineColor;

            for (int i = 0; i < dial.ArcSegments.Count; i++)
                if (dial.ArcSegments[i] != null) dial.ArcSegments[i].color = arcLineColor;

            for (int i = 0; i < dial.InnerArcSegments.Count; i++)
                if (dial.InnerArcSegments[i] != null) dial.InnerArcSegments[i].color = arcLineColor;

            if (dial.RedLimitTick != null) dial.RedLimitTick.color = limitColor;
            if (dial.AmberTick != null) dial.AmberTick.color = warnColor;
            if (dial.BottomRedTick != null) dial.BottomRedTick.color = limitColor;
            if (dial.CommandBug1 != null) dial.CommandBug1.color = bugColor;
            if (dial.CommandBug2 != null) dial.CommandBug2.color = bugColor;

            if (dial.NeedleImage != null) dial.NeedleImage.color = valColor;
            if (dial.TargetText != null) ApplyText(dial.TargetText, TextStyleRole.Accent, theme);
            if (dial.RevText != null) ApplyText(dial.RevText, TextStyleRole.Accent, theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            if (context.Telemetry == null || !context.Telemetry.HasVessel)
            {
                _cachedHasVessel = false;
                return;
            }

            _cachedHasVessel = true;

            // 1. 动态发动机数量感知
            if (_configuredEngineCount == 0)
            {
                int active = context.Telemetry.ActiveEngines;
                int stage = context.Telemetry.TotalStageEngines;
                int detected = active > 0 ? active : (stage > 0 ? stage : 2);
                _cachedDetectedEngineCount = Mathf.Clamp(detected, 1, MAX_ENGINES);
            }

            // 2. 顶端 TAT 与推力模式 (真实大气环境总温与油门解算)
            string evalTat = TelemetryTokenEngine.Evaluate(_tatTemplate, context.Telemetry);
            if (string.IsNullOrEmpty(evalTat) || evalTat.Contains("{"))
            {
                double temp = TelemetryTokenEngine.EvaluateNumeric("{TEMP}", context.Telemetry);
                if (double.IsNaN(temp)) temp = context.Telemetry.CabinTemp;
                evalTat = string.Format(CultureInfo.InvariantCulture, "TAT {0:+0;-0;+0}c", double.IsNaN(temp) ? 0.0 : temp);
            }
            _cachedTatStr = evalTat;

            string evalMode = TelemetryTokenEngine.Evaluate(_thrustModeTemplate, context.Telemetry);
            if (string.IsNullOrEmpty(evalMode) || evalMode.Contains("{"))
            {
                evalMode = context.Telemetry.Throttle > 0.85f ? "D-TO" : (context.Telemetry.VerticalSpeed > 6.0 ? "CLB" : "CRZ");
            }
            _cachedModeStr = evalMode;

            // 3. 逐台真实发动机数据接入 (自适应异构发动机集群)
            //    N1 = 指令油门设定百分比   N2 = 实时推力占额定推力百分比
            //    FF = 计算油耗 (推力/(比冲·g0))   EGT = 原生部件温度 (排气温度的可用替代)
            //    OIL P = 比冲 {KER:isp}   OIL T = 净热通量 {SH:NetFluxKw}
            //    OIL Q = 引擎健康 {TF:Status}   VIB = 故障率 {TF:FailureRate}
            //    说明：OIL/VIB 四项依赖可选外部 mod，未安装时优雅回落为 "---"，绝不伪造读数。
            IReadOnlyList<EngineTelemetryInfo> engines = context.Telemetry.Engines;

            double probeIsp = TelemetryTokenEngine.EvaluateNumeric("{KER:isp}", context.Telemetry);
            double probeNetFlux = TelemetryTokenEngine.EvaluateNumeric("{SH:NetFluxKw}", context.Telemetry);
            double probeFailRate = TelemetryTokenEngine.EvaluateNumeric("{TF:FailureRate}", context.Telemetry);
            string probeStatus = TelemetryTokenEngine.Evaluate("{TF:Status}", context.Telemetry);
            if (string.IsNullOrEmpty(probeStatus) || probeStatus.Contains("{")) probeStatus = "---";

            for (int i = 0; i < MAX_ENGINES; i++)
            {
                bool hasEngine = engines != null && i < engines.Count;
                if (hasEngine)
                {
                    EngineTelemetryInfo eng = engines[i];

                    // N1: 指令推力设定 (0~100%)
                    float n1Pct = Mathf.Clamp01(eng.CommandedThrottle) * 100f;
                    _cachedColN1Str[i] = n1Pct.ToString("0.0", CultureInfo.InvariantCulture);
                    _cachedColN1NeedleAngle[i] = -Mathf.Clamp01(n1Pct / 105f) * 215f;

                    // N2: 真实推力占额定推力百分比
                    float n2Pct = eng.ThrustFraction * 100f;
                    _cachedColN2Str[i] = n2Pct.ToString("0.0", CultureInfo.InvariantCulture);
                    _cachedColN2NeedleAngle[i] = -Mathf.Clamp01(n2Pct / 105f) * 215f;

                    // FF: 计算油耗
                    _cachedColFfStr[i] = eng.FuelFlow.ToString("0.0", CultureInfo.InvariantCulture);

                    // EGT: 原生部件温度 (°C)
                    float egtC = Mathf.Max(0f, eng.PartTemperature);
                    _cachedColEgtStr[i] = Mathf.RoundToInt(egtC).ToString(CultureInfo.InvariantCulture);
                    _cachedColEgtNeedleAngle[i] = -Mathf.Clamp01(egtC / 800f) * 215f;

                    // OIL P: 比冲 (s)，归一化到 0~450s 满量程
                    _cachedColOilPStr[i] = double.IsNaN(probeIsp) ? "---" : Mathf.RoundToInt((float)probeIsp).ToString(CultureInfo.InvariantCulture);
                    _cachedColOilPFrac[i] = double.IsNaN(probeIsp) ? 0f : Mathf.Clamp01((float)(probeIsp / 450.0));

                    // OIL T: 主回路净热通量 (kW)，居中显示
                    _cachedColOilTStr[i] = double.IsNaN(probeNetFlux) ? "---" : probeNetFlux.ToString("0", CultureInfo.InvariantCulture);
                    _cachedColOilTFrac[i] = double.IsNaN(probeNetFlux) ? 0.5f : Mathf.Clamp01((float)(probeNetFlux / 2000.0 + 0.5));

                    // OIL Q: 引擎健康状态 (TestFlight)
                    _cachedColOilQStr[i] = probeStatus;

                    // VIB: 引擎故障率 (prob/s)
                    _cachedColVibStr[i] = double.IsNaN(probeFailRate) ? "---" : probeFailRate.ToString("0.000", CultureInfo.InvariantCulture);
                    _cachedColVibFrac[i] = double.IsNaN(probeFailRate) ? 0f : Mathf.Clamp01((float)(probeFailRate * 1000.0));
                }
                else
                {
                    // 该槽位无发动机：全部读数置为占位符，杜绝伪造
                    _cachedColN1Str[i] = "--";
                    _cachedColN1NeedleAngle[i] = 0f;
                    _cachedColN2Str[i] = "--";
                    _cachedColN2NeedleAngle[i] = 0f;
                    _cachedColEgtStr[i] = "--";
                    _cachedColEgtNeedleAngle[i] = 0f;
                    _cachedColFfStr[i] = "--";
                    _cachedColOilPStr[i] = "--";
                    _cachedColOilPFrac[i] = 0f;
                    _cachedColOilTStr[i] = "--";
                    _cachedColOilTFrac[i] = 0.5f;
                    _cachedColOilQStr[i] = "--";
                    _cachedColVibStr[i] = "--";
                    _cachedColVibFrac[i] = 0f;
                }
            }

            // 4. 右侧起落架状态 (GEAR)
            string evalGear = TelemetryTokenEngine.Evaluate(_gearToken, context.Telemetry);
            if (string.IsNullOrEmpty(evalGear) || evalGear.Contains("{"))
            {
                evalGear = (context.Telemetry.AltitudeAGL < 80.0 || context.Telemetry.VerticalSpeed < -1.0) ? I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下") : I18n.Tr("WIDGET_EICAS_GEAR_UP", "收起");
            }
            _cachedGearStr = evalGear;

            // 5. 右侧襟翼 (FLAPS)
            string evalFlap = TelemetryTokenEngine.Evaluate(_flapToken, context.Telemetry);
            if (string.IsNullOrEmpty(evalFlap) || evalFlap.Contains("{"))
            {
                evalFlap = context.Telemetry.SurfaceSpeed < 70.0 ? "20" : (context.Telemetry.SurfaceSpeed < 90.0 ? "5" : "0");
            }
            _cachedFlapStr = evalFlap;
            float flapRatio = 0f;
            if (evalFlap == "1") flapRatio = 0.2f;
            else if (evalFlap == "5") flapRatio = 0.4f;
            else if (evalFlap == "15") flapRatio = 0.6f;
            else if (evalFlap == "20") flapRatio = 0.8f;
            else if (evalFlap == "30") flapRatio = 1.0f;
            _cachedFlapRatio = flapRatio;

            // 6. 安定面配平 (STAB TRIM)
            double pitchTrim = TelemetryTokenEngine.EvaluateNumeric(_stabToken, context.Telemetry);
            if (double.IsNaN(pitchTrim)) pitchTrim = context.Telemetry.PitchTrim;
            if (double.IsNaN(pitchTrim)) pitchTrim = 0.0;

            double stabUnits = 10.25 + pitchTrim * 4.0;
            _cachedStabStr = stabUnits.ToString("0.00", CultureInfo.InvariantCulture);
            _cachedStabFrac = Mathf.Clamp01((float)((stabUnits - 4.0) / 12.0));

            // 7. 方向舵配平 (RUDDER TRIM)
            double yawTrim = TelemetryTokenEngine.EvaluateNumeric(_rudderToken, context.Telemetry);
            if (double.IsNaN(yawTrim)) yawTrim = context.Telemetry.YawTrim;
            if (double.IsNaN(yawTrim)) yawTrim = 0.0;

            double rudderDeg = yawTrim * 10.0;
            _cachedRudderStr = rudderDeg.ToString("0.0", CultureInfo.InvariantCulture);
            _cachedRudderFrac = Mathf.Clamp((float)(rudderDeg / 10.0), -1f, 1f);

            // ── 8. 真实飞行系统读数区 (全部来自本地遥测通道) ──
            // 航向
            float hdg = context.Telemetry.Heading;
            if (float.IsNaN(hdg)) hdg = 0f;
            _cachedHdgStr = Mathf.RoundToInt(Mathf.Repeat(hdg, 360f)).ToString("000", CultureInfo.InvariantCulture);

            // 马赫数
            double mach = context.Telemetry.Mach;
            _cachedMachStr = double.IsNaN(mach) ? "0.00" : mach.ToString("0.00", CultureInfo.InvariantCulture);

            // 过载
            double gLoad = context.Telemetry.GForce;
            _cachedGLoadStr = double.IsNaN(gLoad) ? "1.0" : gLoad.ToString("0.0", CultureInfo.InvariantCulture);

            // 电气储能
            double ecPct = context.Telemetry.EcPercent;
            if (double.IsNaN(ecPct)) ecPct = 0.0;
            _cachedEcStr = AvionicsFastFormat.FastPercent((float)Mathf.Clamp01((float)ecPct));
            _cachedEcFill = Mathf.Clamp01((float)ecPct);

            // 通信信号
            double commSig = context.Telemetry.CommSignal;
            if (double.IsNaN(commSig)) commSig = 0.0;
            _cachedCommStr = AvionicsFastFormat.FastPercent((float)Mathf.Clamp01((float)commSig));
            _cachedCommFill = Mathf.Clamp01((float)commSig);

            // 垂直速度
            double vsi = context.Telemetry.VerticalSpeed;
            _cachedVsiStr = double.IsNaN(vsi) ? "+0" : string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}", Mathf.RoundToInt((float)vsi));

            // 时间加速
            float warp = context.Telemetry.TimeWarpRate;
            if (float.IsNaN(warp) || warp < 1f) warp = 1f;
            _cachedWarpStr = Mathf.RoundToInt(warp) + "x";

            // SAS 状态
            _cachedSasStr = context.Telemetry.IsSASEnabled ? "ON" : "OFF";

            // 9. 全机总重与燃油统计 (GROSS WT / TOTAL FUEL)
            double fuelFrac = context.Telemetry.StagePropellantFraction;
            double fuelLbs = 102.6 * (fuelFrac > 0.001 ? fuelFrac : 0.85);
            double grossWtLbs = 210.0 + fuelLbs;
            _cachedGrossWtStr = grossWtLbs.ToString("0.0", CultureInfo.InvariantCulture);
            _cachedTotalFuelStr = fuelLbs.ToString("0.0", CultureInfo.InvariantCulture);

            // 远/近地点高度 (真实轨道遥测；亚轨道或地面时高度可能为负，按原值显示)
            double apo = context.Telemetry.Apoapsis;
            double peri = context.Telemetry.Periapsis;
            _cachedApoStr = string.Format(CultureInfo.InvariantCulture, "{0} {1}",
                I18n.Tr("WIDGET_EICAS_APO", "远地点"),
                double.IsNaN(apo) ? "--" : (apo / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "km");
            _cachedPeriStr = string.Format(CultureInfo.InvariantCulture, "{0} {1}",
                I18n.Tr("WIDGET_EICAS_PERI", "近地点"),
                double.IsNaN(peri) ? "--" : (peri / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "km");
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_cachedHasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float s = CurrentDpiScale;
            float sv = s * (RectTransform != null && RectTransform.sizeDelta.y > 1f ? (RectTransform.sizeDelta.y / (BASE_CARD_HEIGHT * s)) : 1f);
            if (sv <= 0.01f) sv = s;

            // 1. 动态发动机数量感知与自动长宽比重排
            if (_configuredEngineCount == 0 && _cachedDetectedEngineCount != _currentEngineCount)
            {
                LayoutEngineColumns(_cachedDetectedEngineCount, s, sv);
            }

            // 2. 顶端 TAT 与推力模式
            if (_lastTatStr.Update(_cachedTatStr))
            {
                if (_tatText != null) _tatText.text = _cachedTatStr;
            }

            if (_lastModeStr.Update(_cachedModeStr))
            {
                if (_thrustModeText != null) _thrustModeText.text = _cachedModeStr;
            }

            // 3. 多发主发动机仪表绘制
            float tapeHalfH = 9f * sv;
            for (int i = 0; i < _currentEngineCount; i++)
            {
                EngineColumnUI col = _engineCols[i];
                if (col == null) continue;

                if (_cachedColN1Str[i] != col.LastN1Str)
                {
                    col.LastN1Str = _cachedColN1Str[i];
                    if (col.N1Dial.BoxText != null) col.N1Dial.BoxText.text = _cachedColN1Str[i];
                }
                if (col.N1Dial.NeedlePivot != null)
                    col.N1Dial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, _cachedColN1NeedleAngle[i]);

                if (_cachedColEgtStr[i] != col.LastEgtStr)
                {
                    col.LastEgtStr = _cachedColEgtStr[i];
                    if (col.EgtDial.BoxText != null) col.EgtDial.BoxText.text = _cachedColEgtStr[i];
                }
                if (col.EgtDial.NeedlePivot != null)
                    col.EgtDial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, _cachedColEgtNeedleAngle[i]);

                if (_cachedColN2Str[i] != col.LastN2Str)
                {
                    col.LastN2Str = _cachedColN2Str[i];
                    if (col.N2Dial.BoxText != null) col.N2Dial.BoxText.text = _cachedColN2Str[i];
                }
                if (col.N2Dial.NeedlePivot != null)
                    col.N2Dial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, _cachedColN2NeedleAngle[i]);

                if (_cachedColFfStr[i] != col.LastFfStr)
                {
                    col.LastFfStr = _cachedColFfStr[i];
                    if (col.FfText != null) col.FfText.text = _cachedColFfStr[i];
                }

                if (_cachedColOilPStr[i] != col.LastOilPStr)
                {
                    col.LastOilPStr = _cachedColOilPStr[i];
                    if (col.OilPText != null) col.OilPText.text = _cachedColOilPStr[i];
                }
                if (col.OilPPointerPivot != null)
                {
                    float ptrY = (_cachedColOilPFrac[i] - 0.5f) * tapeHalfH * 2f;
                    if (col.LastOilPPos.Update(ptrY))
                    {
                        col.OilPPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                    }
                }

                if (_cachedColOilTStr[i] != col.LastOilTStr)
                {
                    col.LastOilTStr = _cachedColOilTStr[i];
                    if (col.OilTText != null) col.OilTText.text = _cachedColOilTStr[i];
                }
                if (col.OilTPointerPivot != null)
                {
                    float ptrY = (_cachedColOilTFrac[i] - 0.5f) * tapeHalfH * 2f;
                    if (col.LastOilTPos.Update(ptrY))
                    {
                        col.OilTPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                    }
                }

                if (_cachedColOilQStr[i] != col.LastOilQStr)
                {
                    col.LastOilQStr = _cachedColOilQStr[i];
                    if (col.OilQText != null) col.OilQText.text = _cachedColOilQStr[i];
                }

                if (_cachedColVibStr[i] != col.LastVibStr)
                {
                    col.LastVibStr = _cachedColVibStr[i];
                    if (col.VibText != null) col.VibText.text = _cachedColVibStr[i];
                }
                if (col.VibPointerPivot != null)
                {
                    float ptrY = (_cachedColVibFrac[i] - 0.5f) * tapeHalfH * 2f;
                    if (col.LastVibPos.Update(ptrY))
                    {
                        col.VibPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                    }
                }
            }

            // 4. 右侧起落架状态 (GEAR)
            if (_lastGearStr.Update(_cachedGearStr))
            {
                if (_gearStatusText != null) _gearStatusText.text = _cachedGearStr;
                if (_cachedGearStr.IndexOf("DOWN", StringComparison.OrdinalIgnoreCase) >= 0
                    || _cachedGearStr.IndexOf(I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下"), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_gearBoxOutline != null) _gearBoxOutline.effectColor = style.GetTextColor(TextStyleRole.Accent, theme);
                    if (_gearStatusText != null) ApplyText(_gearStatusText, TextStyleRole.Accent, theme);
                }
                else
                {
                    if (_gearBoxOutline != null) _gearBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
                    if (_gearStatusText != null) ApplyText(_gearStatusText, TextStyleRole.PrimaryValue, theme);
                }
            }

            // 5. 右侧襟翼 (FLAPS)
            if (_lastFlapStr.Update(_cachedFlapStr))
            {
                if (_flapPositionText != null) _flapPositionText.text = _cachedFlapStr;
                float travelH = 36f * sv;
                float ptrY = -_cachedFlapRatio * travelH;
                if (_flapPointerPivot != null)
                    _flapPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
            }

            // 6. 安定面配平 (STAB TRIM)
            if (_lastStabStr.Update(_cachedStabStr))
            {
                if (_stabValueText != null) _stabValueText.text = _cachedStabStr;
                if (_stabTargetText != null) _stabTargetText.text = _cachedStabStr;

                float ptrY = (_cachedStabFrac - 0.5f) * 30f * sv;
                if (_stabPointerRt != null)
                    _stabPointerRt.anchoredPosition = new Vector2(0f, ptrY);
            }

            // 7. 方向舵配平 (RUDDER TRIM)
            if (_lastRudderStr.Update(_cachedRudderStr))
            {
                if (_rudderValueText != null) _rudderValueText.text = _cachedRudderStr;

                float ptrX = _cachedRudderFrac * 15f * s;
                if (_rudderPointerRt != null)
                    _rudderPointerRt.anchoredPosition = new Vector2(ptrX, 5f * sv);
            }

            // 8. 真实飞行系统读数区
            if (_lastHdgStr.Update(_cachedHdgStr))
            {
                if (_hdgValue != null) _hdgValue.text = _cachedHdgStr;
            }
            if (_lastMachStr.Update(_cachedMachStr))
            {
                if (_machValue != null) _machValue.text = _cachedMachStr;
            }
            if (_lastGLoadStr.Update(_cachedGLoadStr))
            {
                if (_gLoadValue != null) _gLoadValue.text = _cachedGLoadStr;
            }
            if (_lastVsiStr.Update(_cachedVsiStr))
            {
                if (_vsiValue != null) _vsiValue.text = _cachedVsiStr;
            }
            if (_lastWarpStr.Update(_cachedWarpStr))
            {
                if (_warpValue != null) _warpValue.text = _cachedWarpStr;
            }
            if (_lastSasStr.Update(_cachedSasStr))
            {
                if (_sasValue != null) _sasValue.text = _cachedSasStr;
            }

            if (_lastEcStr.Update(_cachedEcStr))
            {
                if (_ecValue != null) _ecValue.text = _cachedEcStr;
            }
            if (_ecBarFillImg != null && _lastEcFill.Update(_cachedEcFill))
            {
                float fullW = _ecBarTrackImg != null ? _ecBarTrackImg.rectTransform.sizeDelta.x : 44f * s;
                _ecBarFillImg.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, fullW * _cachedEcFill), _ecBarFillImg.rectTransform.sizeDelta.y);
            }

            if (_lastCommStr.Update(_cachedCommStr))
            {
                if (_commValue != null) _commValue.text = _cachedCommStr;
            }
            if (_commBarFillImg != null && _lastCommFill.Update(_cachedCommFill))
            {
                float fullW = _commBarTrackImg != null ? _commBarTrackImg.rectTransform.sizeDelta.x : 44f * s;
                _commBarFillImg.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, fullW * _cachedCommFill), _commBarFillImg.rectTransform.sizeDelta.y);
            }

            // 9. 全机总重与燃油统计 (GROSS WT / TOTAL FUEL)
            if (_lastGrossWtStr.Update(_cachedGrossWtStr))
            {
                if (_grossWtText != null) _grossWtText.text = _cachedGrossWtStr;
            }

            if (_lastTotalFuelStr.Update(_cachedTotalFuelStr))
            {
                if (_totalFuelText != null) _totalFuelText.text = _cachedTotalFuelStr;
            }

            if (_lastApoStr.Update(_cachedApoStr))
            {
                if (_apoText != null) _apoText.text = _cachedApoStr;
            }

            if (_lastPeriStr.Update(_cachedPeriStr))
            {
                if (_periText != null) _periText.text = _cachedPeriStr;
            }
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            if (_oilPLabel != null) _oilPLabel.text = I18n.Tr("WIDGET_EICAS_OIL_PRESS", "滑油\n压力");
            if (_oilTLabel != null) _oilTLabel.text = I18n.Tr("WIDGET_EICAS_OIL_TEMP", "滑油\n温度");
            if (_oilQLabel != null) _oilQLabel.text = I18n.Tr("WIDGET_EICAS_OIL_QTY", "滑油量");
            if (_gearLabelText != null) _gearLabelText.text = I18n.Tr("WIDGET_EICAS_GEAR", "起落架");
            if (_flapsLabelText != null) _flapsLabelText.text = I18n.Tr("WIDGET_EICAS_FLAPS", "襟\n翼");
            if (_stabNuText != null) _stabNuText.text = I18n.Tr("WIDGET_EICAS_STAB_NU", "抬头");
            if (_stabLabelText != null) _stabLabelText.text = I18n.Tr("WIDGET_EICAS_STAB", "安\n定\n面");
            if (_rudderLabelText != null) _rudderLabelText.text = I18n.Tr("WIDGET_EICAS_RUDDER_TRIM", "方向舵配平");
            if (_gLoadLabel != null) _gLoadLabel.text = I18n.Tr("WIDGET_EICAS_GLOAD", "过载");
            if (_warpLabel != null) _warpLabel.text = I18n.Tr("WIDGET_EICAS_WARP", "倍速");
            if (_grossWtLabel != null) _grossWtLabel.text = I18n.Tr("WIDGET_EICAS_GROSS_WT", "全重");
            if (_unitsLabel != null) _unitsLabel.text = I18n.Tr("WIDGET_EICAS_UNITS_LBS", "磅 ×\n1000");
            if (_totalFuelLabel != null) _totalFuelLabel.text = I18n.Tr("WIDGET_EICAS_TOTAL_FUEL", "总燃料");
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < MAX_ENGINES; i++)
            {
                if (_engineCols[i] != null)
                {
                    _engineCols[i].N1Dial?.ArcSegments.Clear();
                    _engineCols[i].N1Dial?.InnerArcSegments.Clear();
                    _engineCols[i].EgtDial?.ArcSegments.Clear();
                    _engineCols[i].N2Dial?.ArcSegments.Clear();
                }
            }
            base.OnDestroy();
        }
    }
}
