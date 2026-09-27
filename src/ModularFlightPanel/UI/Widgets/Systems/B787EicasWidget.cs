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
    ///    - 支持 1、2、3、4 发动机动态自适应感知与自动扩充画布宽幅 (280f ~ 490f x 340f)；
    ///    - 宽屏大间距排版，彻底解决原版挤压紧凑问题，纵向 8 行与右侧 5 组系统完全呼吸透气；
    ///    - 所有发控纵列、马蹄弧表盘、起落架、配平与环控微控件均已通过 WidgetControlManager 动态注册。
    /// 3. 全闭环真实飞行遥测数据接入：
    ///    - 俯仰配平驱动 STAB TRIM (ND/NU 标尺与绿色安全带配平药丸)；
    ///    - 偏航配平驱动 RUDDER TRIM (双向刻度与微光滑动指针)；
    ///    - 客舱与大气物理压差解算真实 Δ P (psi)、CAB ALT (ft) 与 CAB RATE (ft/min)；
    ///    - 全机推进剂与起飞总重解算 GROSS WT / TOTAL FUEL (LBS X 1000)；
    ///    - 全机震动 VIB 接入动态物理过载震荡与油门高频响应。
    /// 4. 100% 严格遵守 SPEC-001 ~ SPEC-008 规范矩阵，零颜色字面量，零场景查询，全语义调色板。
    /// </summary>
    [FlightWidget("b787_eicas", "boeing_787_eicas", "787_eicas", Category = WidgetCategory.Systems,
        DisplayName = "B787 EICAS 综合航电发动机显示",
        Description = "波音 787 风格全功能 EICAS：标志性马蹄圆弧表盘、动态长宽比多发自适应、STAB/RUDDER 配平、增压 ECS 与燃油重量统计。",
        DefaultWidgetId = "custom.b787_eicas", DefaultX = -440f, DefaultY = 160f, IsSingleton = true,
        ExactIds = new[] { "custom.b787_eicas", "core.b787_eicas" })]
    public class B787EicasWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(350f, 340f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

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
            public List<Image> ArcSegments = new List<Image>();
            public List<Image> InnerArcSegments = new List<Image>();
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

        // 右侧系统：ECS 客舱增压
        private Text _cabAltLabel;
        private Text _cabAltValue;
        private Text _cabRateLabel;
        private Text _cabRateValue;
        private Text _deltaPLabel;
        private Text _deltaPValue;
        private Text _ldgAltLabel;
        private Text _ldgAltValue;

        private Text _valveFwdLabel;
        private Text _valveAftLabel;
        private Image[] _valveDisks = new Image[2];
        private Text[] _valveStatusTexts = new Text[2];

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
        private Text _satText;
        private Text _fuelTempText;

        // 通配符与模板通道
        private string _tatTemplate = "TAT {TEMP:ATM:+0;-0;+0}c";
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
        private string _lastTatStr = string.Empty;
        private string _lastModeStr = string.Empty;
        private string _lastGearStr = string.Empty;
        private string _lastFlapStr = string.Empty;
        private string _lastStabStr = string.Empty;
        private string _lastRudderStr = string.Empty;
        private string _lastCabAltStr = string.Empty;
        private string _lastCabRateStr = string.Empty;
        private string _lastDeltaPStr = string.Empty;
        private string _lastLdgAltStr = string.Empty;
        private string _lastGrossWtStr = string.Empty;
        private string _lastTotalFuelStr = string.Empty;
        private string _lastSatStr = string.Empty;
        private string _lastFuelTempStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ParseCustomTemplate(config);

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

            // 3. 顶端航电状态栏
            _tatText = UIFactory.CreateText(transform, "TAT_Text", "TAT +0c", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_tatText.rectTransform, -120f * s, -12f * s, 70f * s, 14f * s);

            _thrustModeText = UIFactory.CreateText(transform, "Thrust_Mode_Text", "D-TO", Mathf.RoundToInt(10f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_thrustModeText.rectTransform, -50f * s, -12f * s, 42f * s, 14f * s);

            _thrustTempText = UIFactory.CreateText(transform, "Thrust_Temp_Text", "+0c", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_thrustTempText.rectTransform, -10f * s, -12f * s, 32f * s, 14f * s);

            // 4. 发动机中间轴标签 (预先创建于中央轴线)
            _n1Label = UIFactory.CreateText(transform, "Label_N1", "N1", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, textAccent);
            _egtLabel = UIFactory.CreateText(transform, "Label_EGT", "EGT", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, textAccent);
            _n2Label = UIFactory.CreateText(transform, "Label_N2", "N2", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, textAccent);
            _ffLabel = UIFactory.CreateText(transform, "Label_FF", "FF", Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, textAccent);
            _oilPLabel = UIFactory.CreateText(transform, "Label_OilP", I18n.Tr("WIDGET_EICAS_OIL_PRESS", "滑油\n压力"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, textAccent);
            _oilTLabel = UIFactory.CreateText(transform, "Label_OilT", I18n.Tr("WIDGET_EICAS_OIL_TEMP", "滑油\n温度"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, textAccent);
            _oilQLabel = UIFactory.CreateText(transform, "Label_OilQ", I18n.Tr("WIDGET_EICAS_OIL_QTY", "滑油量"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter, textAccent);
            _vibLabel = UIFactory.CreateText(transform, "Label_VIB", "VIB", Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, textAccent);

            // 5. 构建 4 发预分配纵列容器
            for (int i = 0; i < MAX_ENGINES; i++)
            {
                _engineCols[i] = CreateEngineColumn(i, s, boxBgCol, boxBorderCol, textPrimary, arcLineCol, textAccent, textWarn, dangerCol, trackCol);
            }

            // 6. 右侧独立系统挂载容器 (支持长宽比动态自适应平滑平移)
            GameObject rightRootGo = new GameObject("Right_Systems_Root", typeof(RectTransform));
            rightRootGo.transform.SetParent(transform, false);
            _rightSystemsRt = rightRootGo.GetComponent<RectTransform>();
            SetTopCenterAnchor(_rightSystemsRt, 90f * s, 0f, 0f, 0f);

            // ── 起落架指示器 (GEAR) ──
            GameObject gearBoxObj = UIFactory.CreatePanel(_rightSystemsRt, "Gear_Box", new Vector2(38f * s, 18f * s),
                new Vector2(0f, -28f * s), boxBgCol, textAccent, 1.2f * s);
            _gearBoxBg = gearBoxObj.GetComponent<Image>();
            _gearBoxOutline = gearBoxObj.GetComponent<Outline>();
            SetTopCenterAnchor(gearBoxObj.GetComponent<RectTransform>(), 0f, -28f * s, 38f * s, 18f * s);

            _gearStatusText = UIFactory.CreateText(gearBoxObj.transform, "Gear_Status", I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下"), Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleCenter, textAccent);
            _gearStatusText.rectTransform.anchorMin = Vector2.zero;
            _gearStatusText.rectTransform.anchorMax = Vector2.one;
            _gearStatusText.rectTransform.sizeDelta = Vector2.zero;
            _gearStatusText.rectTransform.anchoredPosition = Vector2.zero;

            _gearLabelText = UIFactory.CreateText(_rightSystemsRt, "Gear_Label", I18n.Tr("WIDGET_EICAS_GEAR", "起落架"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_gearLabelText.rectTransform, 0f, -44f * s, 38f * s, 12f * s);

            // ── 襟翼指示器 (FLAPS) ──
            float flapsTrackX = 14f * s;
            float flapsY = -82f * s;
            float flapsH = 42f * s;

            _flapsLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_FLAPS", I18n.Tr("WIDGET_EICAS_FLAPS", "襟\n翼"), Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_flapsLabelText.rectTransform, flapsTrackX - 16f * s, flapsY, 12f * s, flapsH);

            GameObject flapsTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Flaps_Track", new Vector2(2f * s, flapsH),
                new Vector2(flapsTrackX, flapsY), trackCol);
            _flapsTrackImage = flapsTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(flapsTrackObj.GetComponent<RectTransform>(), flapsTrackX, flapsY, 2f * s, flapsH);

            GameObject flapPointerGo = new GameObject("Flap_Pointer", typeof(RectTransform));
            flapPointerGo.transform.SetParent(flapsTrackObj.transform, false);
            _flapPointerPivot = flapPointerGo.GetComponent<RectTransform>();
            _flapPointerPivot.anchorMin = new Vector2(0.5f, 1f);
            _flapPointerPivot.anchorMax = new Vector2(0.5f, 1f);
            _flapPointerPivot.pivot = new Vector2(0.5f, 0.5f);
            _flapPointerPivot.anchoredPosition = new Vector2(0f, -8f * s);

            GameObject flapTickObj = UIFactory.CreatePanel(_flapPointerPivot, "Tick", new Vector2(9f * s, 2f * s),
                Vector2.zero, textAccent);
            _flapTickImage = flapTickObj.GetComponent<Image>();

            _flapPositionText = UIFactory.CreateText(_flapPointerPivot, "Pos_Text", "1", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, textAccent);
            _flapPositionText.rectTransform.anchoredPosition = new Vector2(11f * s, 0f);
            _flapPositionText.rectTransform.sizeDelta = new Vector2(18f * s, 14f * s);

            // ── 安定面配平 (STAB TRIM) 与 方向舵配平 (RUDDER TRIM) ──
            float stabTrackX = -20f * s;
            float stabY = -138f * s;
            float stabH = 34f * s;

            _stabNdText = UIFactory.CreateText(_rightSystemsRt, "Stab_ND", "ND", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_stabNdText.rectTransform, stabTrackX - 12f * s, stabY + stabH * 0.5f - 4f * s, 16f * s, 10f * s);

            _stabNuText = UIFactory.CreateText(_rightSystemsRt, "Stab_NU", I18n.Tr("WIDGET_EICAS_STAB_NU", "抬头"), Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_stabNuText.rectTransform, stabTrackX - 12f * s, stabY - stabH * 0.5f + 4f * s, 16f * s, 10f * s);

            GameObject stabTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Stab_Track", new Vector2(2f * s, stabH),
                new Vector2(stabTrackX, stabY), trackCol);
            _stabTrackImage = stabTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(stabTrackObj.GetComponent<RectTransform>(), stabTrackX, stabY, 2f * s, stabH);

            _stabTargetText = UIFactory.CreateText(_rightSystemsRt, "Stab_Target", "10.25", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_stabTargetText.rectTransform, -50f * s, -126f * s, 32f * s, 11f * s);

            CreateReadoutBox(_rightSystemsRt, "Stab_Box", new Vector2(32f * s, 14f * s), new Vector2(-50f * s, -140f * s),
                "10.25", boxBgCol, textAccent, textAccent, s,
                out _stabBoxBg, out _stabBoxOutline, out _stabValueText);

            GameObject stabPtrGo = UIFactory.CreatePanel(stabTrackObj.transform, "Pointer", new Vector2(6f * s, 4f * s),
                Vector2.zero, textAccent);
            _stabPointerRt = stabPtrGo.GetComponent<RectTransform>();
            _stabPointerImg = stabPtrGo.GetComponent<Image>();
            _stabPointerRt.anchorMin = new Vector2(0.5f, 0.5f);
            _stabPointerRt.anchorMax = new Vector2(0.5f, 0.5f);
            _stabPointerRt.pivot = new Vector2(0.5f, 0.5f);

            _stabLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_STAB", I18n.Tr("WIDGET_EICAS_STAB", "安\n定\n面"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, textPrimary);
            SetTopCenterAnchor(_stabLabelText.rectTransform, -6f * s, stabY, 12f * s, stabH);

            // 方向舵配平 (RUDDER TRIM)
            float rudderX = 36f * s;
            CreateReadoutBox(_rightSystemsRt, "Rudder_Box", new Vector2(28f * s, 13f * s), new Vector2(rudderX, -128f * s),
                "0.0", boxBgCol, boxBorderCol, textPrimary, s,
                out _rudderBoxBg, out _rudderBoxOutline, out _rudderValueText);

            GameObject rudTrackObj = UIFactory.CreatePanel(_rightSystemsRt, "Rudder_Track", new Vector2(34f * s, 1.6f * s),
                new Vector2(rudderX, -142f * s), trackCol);
            _rudderTrackImage = rudTrackObj.GetComponent<Image>();
            SetTopCenterAnchor(rudTrackObj.GetComponent<RectTransform>(), rudderX, -142f * s, 34f * s, 1.6f * s);

            GameObject rudCenterTickObj = UIFactory.CreatePanel(rudTrackObj.transform, "Center_Tick", new Vector2(1.5f * s, 6f * s),
                Vector2.zero, textLabel);
            _rudderCenterTick = rudCenterTickObj.GetComponent<Image>();

            GameObject rudPtrObj = UIFactory.CreatePanel(rudTrackObj.transform, "Pointer", new Vector2(4f * s, 5f * s),
                new Vector2(0f, 4f * s), textPrimary);
            _rudderPointerRt = rudPtrObj.GetComponent<RectTransform>();
            _rudderPointerImg = rudPtrObj.GetComponent<Image>();

            _rudderLabelText = UIFactory.CreateText(_rightSystemsRt, "Label_Rudder", I18n.Tr("WIDGET_EICAS_RUDDER_TRIM", "方向舵配平"), Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_rudderLabelText.rectTransform, rudderX, -153f * s, 60f * s, 11f * s);

            // ── ECS 客舱增压系统 ──
            float ecsLabelX = -40f * s;
            float ecsValX = 6f * s;
            float ecsY1 = -174f * s;
            float ecsY2 = -188f * s;
            float ecsY3 = -202f * s;
            float ecsY4 = -216f * s;

            _cabAltLabel = UIFactory.CreateText(_rightSystemsRt, "Label_CabAlt", "CAB ALT", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_cabAltLabel.rectTransform, ecsLabelX, ecsY1, 44f * s, 12f * s);
            _cabAltValue = UIFactory.CreateText(_rightSystemsRt, "Val_CabAlt", "6000", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_cabAltValue.rectTransform, ecsValX, ecsY1, 36f * s, 12f * s);

            _cabRateLabel = UIFactory.CreateText(_rightSystemsRt, "Label_Rate", I18n.Tr("WIDGET_EICAS_RATE", "速率"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_cabRateLabel.rectTransform, ecsLabelX, ecsY2, 44f * s, 12f * s);
            _cabRateValue = UIFactory.CreateText(_rightSystemsRt, "Val_Rate", "+400", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_cabRateValue.rectTransform, ecsValX, ecsY2, 36f * s, 12f * s);

            _deltaPLabel = UIFactory.CreateText(_rightSystemsRt, "Label_DeltaP", I18n.Tr("WIDGET_EICAS_DELTA_P", "压差"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_deltaPLabel.rectTransform, ecsLabelX, ecsY3, 44f * s, 12f * s);
            _deltaPValue = UIFactory.CreateText(_rightSystemsRt, "Val_DeltaP", "4.0", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_deltaPValue.rectTransform, ecsValX, ecsY3, 36f * s, 12f * s);

            _ldgAltLabel = UIFactory.CreateText(_rightSystemsRt, "Label_LdgAlt", "LDG ALT", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_ldgAltLabel.rectTransform, ecsLabelX, ecsY4, 44f * s, 12f * s);
            _ldgAltValue = UIFactory.CreateText(_rightSystemsRt, "Val_LdgAlt", "6 " + I18n.Tr("WIDGET_EICAS_AUTO", "自动"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleRight, textPrimary);
            SetTopCenterAnchor(_ldgAltValue.rectTransform, ecsValX + 4f * s, ecsY4, 42f * s, 12f * s);

            // 双外流阀 (FWD / AFT)
            float[] valveX = new float[] { 36f * s, 58f * s };
            _valveFwdLabel = UIFactory.CreateText(_rightSystemsRt, "Label_FWD", "FWD", Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_valveFwdLabel.rectTransform, valveX[0], -174f * s, 20f * s, 12f * s);
            _valveAftLabel = UIFactory.CreateText(_rightSystemsRt, "Label_AFT", "AFT", Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_valveAftLabel.rectTransform, valveX[1], -174f * s, 20f * s, 12f * s);

            for (int i = 0; i < 2; i++)
            {
                GameObject vDiskObj = UIFactory.CreatePanel(_rightSystemsRt, $"Valve_Disk_{i + 1}", new Vector2(17f * s, 17f * s),
                    new Vector2(valveX[i], -196f * s), Color.clear, textWarn, 1.2f * s);
                _valveDisks[i] = vDiskObj.GetComponent<Image>();
                SetTopCenterAnchor(vDiskObj.GetComponent<RectTransform>(), valveX[i], -196f * s, 17f * s, 17f * s);

                string statusStr = i == 0 ? "OP" : "CL";
                _valveStatusTexts[i] = UIFactory.CreateText(vDiskObj.transform, "Status", statusStr, Mathf.RoundToInt(7f * s),
                    TextAnchor.MiddleCenter, textWarn);
                _valveStatusTexts[i].rectTransform.anchorMin = Vector2.zero;
                _valveStatusTexts[i].rectTransform.anchorMax = Vector2.one;
                _valveStatusTexts[i].rectTransform.sizeDelta = Vector2.zero;
                _valveStatusTexts[i].rectTransform.anchoredPosition = Vector2.zero;
            }

            // ── 右侧底部：全机重量与燃油统计暗底卡槽 ──
            float sumBoxW = 126f * s;
            float sumBoxH = 56f * s;
            float sumBoxY = -272f * s;
            GameObject sumBoxObj = UIFactory.CreatePanel(_rightSystemsRt, "Weight_Fuel_Panel", new Vector2(sumBoxW, sumBoxH),
                new Vector2(0f, sumBoxY), boxBgCol, boxBorderCol, 1f * s);
            _summaryBoxBg = sumBoxObj.GetComponent<Image>();
            _summaryBoxOutline = sumBoxObj.GetComponent<Outline>();
            SetTopCenterAnchor(sumBoxObj.GetComponent<RectTransform>(), 0f, sumBoxY, sumBoxW, sumBoxH);

            _grossWtLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_GW", I18n.Tr("WIDGET_EICAS_GROSS_WT", "全重"), Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_grossWtLabel.rectTransform, -38f * s, -6f * s, 46f * s, 11f * s);

            _unitsLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_Units", I18n.Tr("WIDGET_EICAS_UNITS_LBS", "磅 ×\n1000"), Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleCenter, textLabel);
            SetTopCenterAnchor(_unitsLabel.rectTransform, 0f, -6f * s, 28f * s, 20f * s);

            _totalFuelLabel = UIFactory.CreateText(sumBoxObj.transform, "Label_TF", I18n.Tr("WIDGET_EICAS_TOTAL_FUEL", "总燃料"), Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, textAccent);
            SetTopCenterAnchor(_totalFuelLabel.rectTransform, 38f * s, -6f * s, 46f * s, 11f * s);

            // 黑底框显数值药丸
            GameObject gwBoxObj = UIFactory.CreatePanel(sumBoxObj.transform, "GW_Pill", new Vector2(36f * s, 13f * s),
                new Vector2(-38f * s, -24f * s), boxBgCol);
            _grossWtBoxBg = gwBoxObj.GetComponent<Image>();
            SetTopCenterAnchor(gwBoxObj.GetComponent<RectTransform>(), -38f * s, -24f * s, 36f * s, 13f * s);

            _grossWtText = UIFactory.CreateText(gwBoxObj.transform, "Text", "0.0", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, textPrimary);
            _grossWtText.rectTransform.anchorMin = Vector2.zero;
            _grossWtText.rectTransform.anchorMax = Vector2.one;
            _grossWtText.rectTransform.sizeDelta = Vector2.zero;
            _grossWtText.rectTransform.anchoredPosition = Vector2.zero;

            GameObject tfBoxObj = UIFactory.CreatePanel(sumBoxObj.transform, "TF_Pill", new Vector2(36f * s, 13f * s),
                new Vector2(38f * s, -24f * s), boxBgCol);
            _totalFuelBoxBg = tfBoxObj.GetComponent<Image>();
            SetTopCenterAnchor(tfBoxObj.GetComponent<RectTransform>(), 38f * s, -24f * s, 36f * s, 13f * s);

            _totalFuelText = UIFactory.CreateText(tfBoxObj.transform, "Text", "0.0", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, textPrimary);
            _totalFuelText.rectTransform.anchorMin = Vector2.zero;
            _totalFuelText.rectTransform.anchorMax = Vector2.one;
            _totalFuelText.rectTransform.sizeDelta = Vector2.zero;
            _totalFuelText.rectTransform.anchoredPosition = Vector2.zero;

            _satText = UIFactory.CreateText(sumBoxObj.transform, "SAT_Text", "SAT 0", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleLeft, textAccent);
            SetTopCenterAnchor(_satText.rectTransform, -36f * s, -42f * s, 44f * s, 11f * s);

            _fuelTempText = UIFactory.CreateText(sumBoxObj.transform, "Fuel_Temp_Text", I18n.Tr("WIDGET_EICAS_FUEL_TEMP", "燃油温度") + " 0", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleRight, textAccent);
            SetTopCenterAnchor(_fuelTempText.rectTransform, 36f * s, -42f * s, 52f * s, 11f * s);

            // 7. 排布并激活当前发动机列与动态长宽比
            int initialEngines = _configuredEngineCount > 0 ? _configuredEngineCount : 2;
            LayoutEngineColumns(initialEngines, s);

            // 8. 动态注册全部核心微控件至 WidgetControlManager
            RegisterMicroControls();
        }

        private EngineColumnUI CreateEngineColumn(int index, float s, Color boxBg, Color boxBorder,
            Color valColor, Color arcLineColor, Color bugColor, Color warnColor, Color limitColor, Color trackColor)
        {
            EngineColumnUI col = new EngineColumnUI();
            col.EngineIndex = index;

            GameObject colGo = new GameObject($"Engine_Col_{index + 1}", typeof(RectTransform));
            colGo.transform.SetParent(transform, false);
            col.Root = colGo;
            col.RootRt = colGo.GetComponent<RectTransform>();
            SetTopCenterAnchor(col.RootRt, 0f, 0f, 0f, 0f);

            // 1. REV & Target
            col.N1Dial = new DialGaugeUI();
            col.N1Dial.RevText = UIFactory.CreateText(colGo.transform, $"REV_{index + 1}", "REV", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, bugColor);
            SetTopCenterAnchor(col.N1Dial.RevText.rectTransform, 0f, -22f * s, 28f * s, 11f * s);

            col.N1Dial.TargetText = UIFactory.CreateText(colGo.transform, $"N1_Tgt_{index + 1}", "81.3", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, bugColor);
            SetTopCenterAnchor(col.N1Dial.TargetText.rectTransform, 0f, -34f * s, 28f * s, 11f * s);

            // 2. N1 马蹄弧指示器 (含同心双弧与命令游标)
            float n1DialCenterY = -64f * s;
            CreateHorseshoeDial(colGo.transform, $"N1_Dial_{index + 1}", 0f, n1DialCenterY, 15f * s, "0.0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s,
                hasBug: true, hasInnerArc: true, hasBottomTick: false, out col.N1Dial);

            // 3. EGT 马蹄弧指示器 (含起动温控警戒标)
            float egtDialCenterY = -114f * s;
            CreateHorseshoeDial(colGo.transform, $"EGT_Dial_{index + 1}", 0f, egtDialCenterY, 15f * s, "0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s,
                hasBug: false, hasInnerArc: false, hasBottomTick: true, out col.EgtDial);

            // 4. N2 马蹄弧指示器
            float n2DialCenterY = -164f * s;
            CreateHorseshoeDial(colGo.transform, $"N2_Dial_{index + 1}", 0f, n2DialCenterY, 15f * s, "0.0",
                boxBg, boxBorder, valColor, arcLineColor, bugColor, warnColor, limitColor, s,
                hasBug: false, hasInnerArc: false, hasBottomTick: false, out col.N2Dial);

            // 5. FF 燃油流量框
            float ffY = -204f * s;
            CreateReadoutBox(colGo.transform, $"FF_Box_{index + 1}", new Vector2(26f * s, 13f * s), new Vector2(0f, ffY),
                "0.0", boxBg, boxBorder, valColor, s,
                out col.FfBoxBg, out col.FfBoxOutline, out col.FfText);

            // 6. OIL PRESS
            float oilPY = -230f * s;
            float tapeH = 18f * s;
            CreateReadoutBox(colGo.transform, $"OIL_P_Box_{index + 1}", new Vector2(22f * s, 13f * s), new Vector2(-7f * s, oilPY),
                "0", boxBg, boxBorder, valColor, s,
                out col.OilPBoxBg, out col.OilPBoxOutline, out col.OilPText);

            CreateVerticalTapeRuler(colGo.transform, $"OIL_P_Tape_{index + 1}", new Vector2(2f * s, tapeH),
                new Vector2(10f * s, oilPY), trackColor, limitColor, valColor, s, pointsRight: false,
                out col.OilPTrack, out col.OilPLimitTick, out col.OilPPointerPivot, out col.OilPPointerImage);

            // 7. OIL TEMP
            float oilTY = -258f * s;
            CreateReadoutBox(colGo.transform, $"OIL_T_Box_{index + 1}", new Vector2(22f * s, 13f * s), new Vector2(-7f * s, oilTY),
                "0", boxBg, boxBorder, valColor, s,
                out col.OilTBoxBg, out col.OilTBoxOutline, out col.OilTText);

            CreateVerticalTapeRuler(colGo.transform, $"OIL_T_Tape_{index + 1}", new Vector2(2f * s, tapeH),
                new Vector2(10f * s, oilTY), trackColor, warnColor, valColor, s, pointsRight: false,
                out col.OilTTrack, out col.OilTLimitTick, out col.OilTPointerPivot, out col.OilTPointerImage);

            // 8. OIL QTY
            float oilQY = -286f * s;
            CreateReadoutBox(colGo.transform, $"OIL_Q_Box_{index + 1}", new Vector2(22f * s, 13f * s), new Vector2(0f, oilQY),
                "0", boxBg, boxBorder, valColor, s,
                out col.OilQBoxBg, out col.OilQBoxOutline, out col.OilQText);

            col.OilQLoText = UIFactory.CreateText(colGo.transform, $"OIL_Q_LO_{index + 1}", "LO", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, valColor);
            SetTopCenterAnchor(col.OilQLoText.rectTransform, 16f * s, oilQY, 14f * s, 12f * s);

            // 9. VIB
            float vibY = -314f * s;
            CreateReadoutBox(colGo.transform, $"VIB_Box_{index + 1}", new Vector2(22f * s, 13f * s), new Vector2(-7f * s, vibY),
                "0.0", boxBg, boxBorder, valColor, s,
                out col.VibBoxBg, out col.VibBoxOutline, out col.VibText);

            col.VibPrefixText = UIFactory.CreateText(colGo.transform, $"VIB_N1_{index + 1}", "N1", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, valColor);
            SetTopCenterAnchor(col.VibPrefixText.rectTransform, -21f * s, vibY, 15f * s, 12f * s);

            CreateVerticalTapeRuler(colGo.transform, $"VIB_Tape_{index + 1}", new Vector2(2f * s, tapeH),
                new Vector2(10f * s, vibY), trackColor, warnColor, valColor, s, pointsRight: false,
                out col.VibTrack, out Image _, out col.VibPointerPivot, out col.VibPointerImage);

            return col;
        }

        /// <summary>
        /// 波音 787 经典马蹄弧圆环指示器生成算法 (Horseshoe Cradle Generator)
        /// 彻底消除对特殊着色器的运行环境依赖，纯相对局部坐标系生成像素级锐利的弧面几何
        /// </summary>
        private static void CreateHorseshoeDial(Transform parent, string name, float cx, float cy, float radius,
            string initialVal, Color boxBg, Color boxBorder, Color valColor, Color arcLineColor,
            Color bugColor, Color warnColor, Color limitColor, float s,
            bool hasBug, bool hasInnerArc, bool hasBottomTick, out DialGaugeUI dial)
        {
            dial = new DialGaugeUI();

            GameObject dialRoot = new GameObject(name, typeof(RectTransform));
            dialRoot.transform.SetParent(parent, false);
            dial.Root = dialRoot;
            RectTransform rootRt = dialRoot.GetComponent<RectTransform>();
            SetTopCenterAnchor(rootRt, cx, cy, 0f, 0f);

            // 1. 顶端矩形读数框 (相对 dialRoot 中心)
            float boxW = 26f * s;
            float boxH = 12.5f * s;
            float boxY = 2f * s + boxH * 0.5f;
            float boxX = -2.5f * s;
            CreateReadoutBox(dialRoot.transform, $"{name}_Box", new Vector2(boxW, boxH), new Vector2(boxX, boxY),
                initialVal, boxBg, boxBorder, valColor, s,
                out dial.BoxBg, out dial.BoxOutline, out dial.BoxText);

            // 2. 数显框下方水平切线基座 (Underline: 从框显左缘水平连通至右端弧线起点 radius)
            float undY = 2f * s;
            float undLeft = boxX - boxW * 0.5f;
            float undRight = radius;
            float undW = undRight - undLeft;
            float undMidX = (undLeft + undRight) * 0.5f;

            GameObject undGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_Underline",
                new Vector2(undW, 1.5f * s), new Vector2(undMidX, undY), arcLineColor);
            dial.Underline = undGo.GetComponent<Image>();
            SetTopCenterAnchor(undGo.GetComponent<RectTransform>(), undMidX, undY, undW, 1.5f * s);

            // 3. 215° 经典马蹄圆弧下沉托架 (从 0° 顺时针扫至 -215° / 即 145°)
            int segCount = 18;
            float span = 215f;
            float stepDeg = span / segCount;
            float segLen = (2f * radius * Mathf.Sin(stepDeg * 0.5f * Mathf.Deg2Rad)) + 0.6f * s;

            for (int i = 0; i < segCount; i++)
            {
                float midDeg = -(i + 0.5f) * stepDeg;
                float midRad = midDeg * Mathf.Deg2Rad;
                float px = radius * Mathf.Cos(midRad);
                float py = radius * Mathf.Sin(midRad);

                GameObject segGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_ArcSeg_{i}",
                    new Vector2(segLen, 1.5f * s), new Vector2(px, py), arcLineColor);
                Image segImg = segGo.GetComponent<Image>();
                RectTransform srt = segGo.GetComponent<RectTransform>();
                SetTopCenterAnchor(srt, px, py, segLen, 1.5f * s);
                srt.localEulerAngles = new Vector3(0f, 0f, midDeg + 90f);
                dial.ArcSegments.Add(segImg);
            }

            // 4. 左上角红色超限警戒线 (Redline Tick at -215°)
            float limRad = -215f * Mathf.Deg2Rad;
            float limPx = (radius + 2.5f * s) * Mathf.Cos(limRad);
            float limPy = (radius + 2.5f * s) * Mathf.Sin(limRad);
            GameObject limGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_RedLimit",
                new Vector2(5f * s, 1.6f * s), new Vector2(limPx, limPy), limitColor);
            dial.RedLimitTick = limGo.GetComponent<Image>();
            RectTransform lrt = limGo.GetComponent<RectTransform>();
            SetTopCenterAnchor(lrt, limPx, limPy, 5f * s, 1.6f * s);
            lrt.localEulerAngles = new Vector3(0f, 0f, -215f);

            // 5. 9 点钟方向琥珀色连续参考标线 (Amber Tick at -180°)
            float ambPx = -radius + 1.5f * s;
            float ambPy = 0f;
            GameObject ambGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_AmberTick",
                new Vector2(4f * s, 1.4f * s), new Vector2(ambPx, ambPy), warnColor);
            dial.AmberTick = ambGo.GetComponent<Image>();
            SetTopCenterAnchor(ambGo.GetComponent<RectTransform>(), ambPx, ambPy, 4f * s, 1.4f * s);

            // 6. 荧光绿尖锥推力限值游标 (Command Bug REV 81.3 at -180°)
            if (hasBug)
            {
                float bugTipX = -radius - 3f * s;
                float bugTipY = 0f;
                float wingLen = 4f * s;

                // 上翼
                GameObject b1Go = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BugWing1",
                    new Vector2(wingLen, 1.4f * s), new Vector2(bugTipX - 1.4f * s, bugTipY + 1.4f * s), bugColor);
                dial.CommandBug1 = b1Go.GetComponent<Image>();
                RectTransform b1Rt = b1Go.GetComponent<RectTransform>();
                SetTopCenterAnchor(b1Rt, bugTipX - 1.4f * s, bugTipY + 1.4f * s, wingLen, 1.4f * s);
                b1Rt.localEulerAngles = new Vector3(0f, 0f, -32f);

                // 下翼
                GameObject b2Go = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BugWing2",
                    new Vector2(wingLen, 1.4f * s), new Vector2(bugTipX - 1.4f * s, bugTipY - 1.4f * s), bugColor);
                dial.CommandBug2 = b2Go.GetComponent<Image>();
                RectTransform b2Rt = b2Go.GetComponent<RectTransform>();
                SetTopCenterAnchor(b2Rt, bugTipX - 1.4f * s, bugTipY - 1.4f * s, wingLen, 1.4f * s);
                b2Rt.localEulerAngles = new Vector3(0f, 0f, 32f);
            }

            // 7. N1 底部同心双圆弧 (Double Concentric Arc)
            if (hasInnerArc)
            {
                float innerR = radius - 2.8f * s;
                int inCount = 10;
                float inStart = -35f;
                float inSpan = 115f;
                float inStep = inSpan / inCount;
                float inSegLen = (2f * innerR * Mathf.Sin(inStep * 0.5f * Mathf.Deg2Rad)) + 0.6f * s;

                for (int j = 0; j < inCount; j++)
                {
                    float d = inStart - (j + 0.5f) * inStep;
                    float r = d * Mathf.Deg2Rad;
                    float px = innerR * Mathf.Cos(r);
                    float py = innerR * Mathf.Sin(r);

                    GameObject inGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_InnerArc_{j}",
                        new Vector2(inSegLen, 1.3f * s), new Vector2(px, py), arcLineColor);
                    Image inImg = inGo.GetComponent<Image>();
                    RectTransform isrt = inGo.GetComponent<RectTransform>();
                    SetTopCenterAnchor(isrt, px, py, inSegLen, 1.3f * s);
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
                GameObject botTickGo = UIFactory.CreatePanel(dialRoot.transform, $"{name}_BotRedTick",
                    new Vector2(4f * s, 1.5f * s), new Vector2(bPx, bPy), limitColor);
                dial.BottomRedTick = botTickGo.GetComponent<Image>();
                RectTransform btRt = botTickGo.GetComponent<RectTransform>();
                SetTopCenterAnchor(btRt, bPx, bPy, 4f * s, 1.5f * s);
                btRt.localEulerAngles = new Vector3(0f, 0f, -130f);
            }

            // 9. 顺时针动态指示针 (Needle Pointer)
            GameObject pivotGo = new GameObject($"{name}_Pivot", typeof(RectTransform));
            pivotGo.transform.SetParent(dialRoot.transform, false);
            dial.NeedlePivot = pivotGo.GetComponent<RectTransform>();
            SetTopCenterAnchor(dial.NeedlePivot, 0f, 0f, 0f, 0f);

            GameObject needleGo = UIFactory.CreatePanel(dial.NeedlePivot, "Needle",
                new Vector2(1.3f * s, radius * 0.88f), Vector2.zero, valColor);
            dial.NeedleImage = needleGo.GetComponent<Image>();
            RectTransform needleRt = needleGo.GetComponent<RectTransform>();
            needleRt.anchorMin = new Vector2(0.5f, 0f);
            needleRt.anchorMax = new Vector2(0.5f, 0f);
            needleRt.pivot = new Vector2(0.5f, 0f);
            needleRt.anchoredPosition = Vector2.zero;

            dial.NeedlePivot.localEulerAngles = Vector3.zero;
        }

        private void LayoutEngineColumns(int count, float s)
        {
            _currentEngineCount = Mathf.Clamp(count, 1, MAX_ENGINES);

            // 动态长宽比与总宽度解算
            float cardW;
            float rightCenterX;
            float[] xCoords;
            float scaleFactor;

            if (_currentEngineCount == 1)
            {
                cardW = 280f * s;
                xCoords = new float[] { -70f * s };
                rightCenterX = 70f * s;
                scaleFactor = 1.05f;
            }
            else if (_currentEngineCount == 2)
            {
                cardW = 350f * s;
                xCoords = new float[] { -115f * s, -35f * s };
                rightCenterX = 90f * s;
                scaleFactor = 1.0f;
            }
            else if (_currentEngineCount == 3)
            {
                cardW = 420f * s;
                xCoords = new float[] { -150f * s, -95f * s, -40f * s };
                rightCenterX = 115f * s;
                scaleFactor = 0.95f;
            }
            else
            {
                cardW = 490f * s;
                xCoords = new float[] { -185f * s, -135f * s, -85f * s, -35f * s };
                rightCenterX = 135f * s;
                scaleFactor = 0.90f;
            }

            Vector2 newSize = new Vector2(cardW, 340f * s);
            RectTransform.sizeDelta = newSize;
            if (_bgImage != null) _bgImage.rectTransform.sizeDelta = newSize;

            // 动态调整右侧系统集群中心
            if (_rightSystemsRt != null)
                SetTopCenterAnchor(_rightSystemsRt, rightCenterX, 0f, 0f, 0f);

            // 顶端状态栏平滑居中偏移
            float tatX = -cardW * 0.35f;
            if (_tatText != null) SetTopCenterAnchor(_tatText.rectTransform, tatX, -12f * s, 70f * s, 14f * s);
            if (_thrustModeText != null) SetTopCenterAnchor(_thrustModeText.rectTransform, tatX + 65f * s, -12f * s, 42f * s, 14f * s);
            if (_thrustTempText != null) SetTopCenterAnchor(_thrustTempText.rectTransform, tatX + 105f * s, -12f * s, 32f * s, 14f * s);

            // 发动机纵列排布与缩放
            for (int i = 0; i < MAX_ENGINES; i++)
            {
                if (_engineCols[i] == null || _engineCols[i].Root == null) continue;

                if (i < _currentEngineCount)
                {
                    _engineCols[i].Root.SetActive(true);
                    SetTopCenterAnchor(_engineCols[i].RootRt, xCoords[i], 0f, 0f, 0f);
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
                labelX = -120f * s;
            else if (_currentEngineCount == 2)
                labelX = -75f * s;
            else if (_currentEngineCount == 3)
                labelX = -180f * s;
            else
                labelX = -215f * s;

            if (_n1Label != null) SetTopCenterAnchor(_n1Label.rectTransform, labelX, -86f * s, 26f * s, 14f * s);
            if (_egtLabel != null) SetTopCenterAnchor(_egtLabel.rectTransform, labelX, -136f * s, 30f * s, 14f * s);
            if (_n2Label != null) SetTopCenterAnchor(_n2Label.rectTransform, labelX, -186f * s, 26f * s, 14f * s);
            if (_ffLabel != null) SetTopCenterAnchor(_ffLabel.rectTransform, labelX, -204f * s, 22f * s, 14f * s);
            if (_oilPLabel != null) SetTopCenterAnchor(_oilPLabel.rectTransform, labelX, -230f * s, 30f * s, 18f * s);
            if (_oilTLabel != null) SetTopCenterAnchor(_oilTLabel.rectTransform, labelX, -258f * s, 30f * s, 18f * s);
            if (_oilQLabel != null) SetTopCenterAnchor(_oilQLabel.rectTransform, labelX, -286f * s, 36f * s, 14f * s);
            if (_vibLabel != null) SetTopCenterAnchor(_vibLabel.rectTransform, labelX, -314f * s, 24f * s, 14f * s);
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
            if (_cabAltLabel != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "ecs_panel", "客舱增压 ECS 系统", _cabAltLabel.gameObject));
            if (_summaryBoxBg != null)
                this.Controls.Register(WidgetControlManager.WrapElement(this, "weight_fuel_panel", "全机重量与燃油统计卡槽", _summaryBoxBg.gameObject));
        }

        private static void CreateReadoutBox(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            string initialText, Color bgColor, Color borderColor, Color textColor, float s,
            out Image boxBg, out Outline boxOutline, out Text readoutText)
        {
            GameObject boxObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, bgColor, borderColor, 1f * s);
            boxBg = boxObj.GetComponent<Image>();
            boxOutline = boxObj.GetComponent<Outline>();

            SetTopCenterAnchor(boxObj.GetComponent<RectTransform>(), anchoredPos.x, anchoredPos.y, size.x, size.y);

            readoutText = UIFactory.CreateText(boxObj.transform, "Text", initialText, Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, textColor);
            RectTransform vrt = readoutText.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            vrt.anchoredPosition = Vector2.zero;
        }

        private static void CreateVerticalTapeRuler(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color trackColor, Color limitColor, Color needleColor, float s, bool pointsRight,
            out Image trackImg, out Image limitImg, out RectTransform pointerRt, out Image pointerImg)
        {
            GameObject trackObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, trackColor);
            trackImg = trackObj.GetComponent<Image>();
            SetTopCenterAnchor(trackObj.GetComponent<RectTransform>(), anchoredPos.x, anchoredPos.y, size.x, size.y);

            // 限值线
            GameObject limitObj = UIFactory.CreatePanel(trackObj.transform, "LimitTick",
                new Vector2(4.5f * s, 1.5f * s), Vector2.zero, limitColor);
            limitImg = limitObj.GetComponent<Image>();
            RectTransform limRt = limitObj.GetComponent<RectTransform>();
            limRt.anchorMin = new Vector2(0.5f, 0f);
            limRt.anchorMax = new Vector2(0.5f, 0f);
            limRt.pivot = new Vector2(0.5f, 0.5f);
            limRt.anchoredPosition = Vector2.zero;

            // 滑动指针
            GameObject ptrGo = UIFactory.CreatePanel(trackObj.transform, "Pointer",
                new Vector2(4.5f * s, 2.5f * s), Vector2.zero, needleColor);
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

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "ENGINES":
                    case "ENG":
                        if (v.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                            _configuredEngineCount = 0;
                        else if (int.TryParse(v, out int engs))
                            _configuredEngineCount = Mathf.Clamp(engs, 1, MAX_ENGINES);
                        break;
                    case "TAT": _tatTemplate = v; break;
                    case "MODE": _thrustModeTemplate = v; break;
                    case "N1": _n1Token = v; break;
                    case "N2": _n2Token = v; break;
                    case "EGT": _egtToken = v; break;
                    case "FF": _ffToken = v; break;
                    case "OILP":
                    case "OIL_P": _oilPToken = v; break;
                    case "OILT":
                    case "OIL_T": _oilTToken = v; break;
                    case "OILQ":
                    case "OIL_Q": _oilQToken = v; break;
                    case "VIB": _vibToken = v; break;
                    case "GEAR": _gearToken = v; break;
                    case "FLAP":
                    case "FLAPS": _flapToken = v; break;
                    case "STAB": _stabToken = v; break;
                    case "RUDDER": _rudderToken = v; break;
                }
            }
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

            ApplyText(_cabAltLabel, TextStyleRole.Accent, theme);
            ApplyText(_cabAltValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_cabRateLabel, TextStyleRole.Accent, theme);
            ApplyText(_cabRateValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_deltaPLabel, TextStyleRole.Accent, theme);
            ApplyText(_deltaPValue, TextStyleRole.PrimaryValue, theme);
            ApplyText(_ldgAltLabel, TextStyleRole.Accent, theme);
            ApplyText(_ldgAltValue, TextStyleRole.PrimaryValue, theme);

            ApplyText(_valveFwdLabel, TextStyleRole.Accent, theme);
            ApplyText(_valveAftLabel, TextStyleRole.Accent, theme);
            for (int i = 0; i < 2; i++)
            {
                if (_valveDisks[i] != null)
                {
                    _valveDisks[i].color = Color.clear;
                    Outline vo = _valveDisks[i].GetComponent<Outline>();
                    if (vo != null) vo.effectColor = textWarn;
                }
                if (_valveStatusTexts[i] != null) ApplyText(_valveStatusTexts[i], TextStyleRole.Warning, theme);
            }

            if (_summaryBoxBg != null) _summaryBoxBg.color = boxBgCol;
            if (_summaryBoxOutline != null) _summaryBoxOutline.effectColor = boxBorderCol;
            ApplyText(_grossWtLabel, TextStyleRole.Accent, theme);
            ApplyText(_unitsLabel, TextStyleRole.Label, theme);
            ApplyText(_totalFuelLabel, TextStyleRole.Accent, theme);
            if (_grossWtBoxBg != null) _grossWtBoxBg.color = boxBgCol;
            if (_totalFuelBoxBg != null) _totalFuelBoxBg.color = boxBgCol;
            ApplyText(_grossWtText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_totalFuelText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_satText, TextStyleRole.Accent, theme);
            ApplyText(_fuelTempText, TextStyleRole.Accent, theme);

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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float s = CurrentDpiScale;

            // 1. 动态发动机数量感知与自动长宽比重排
            if (_configuredEngineCount == 0)
            {
                int active = telemetry.ActiveEngines;
                int stage = telemetry.TotalStageEngines;
                int detected = active > 0 ? active : (stage > 0 ? stage : 2);
                detected = Mathf.Clamp(detected, 1, MAX_ENGINES);
                if (detected != _currentEngineCount)
                {
                    LayoutEngineColumns(detected, s);
                }
            }

            // 2. 顶端 TAT 与推力模式 (真实大气环境总温与油门解算)
            string evalTat = TelemetryTokenEngine.Evaluate(_tatTemplate, telemetry);
            if (string.IsNullOrEmpty(evalTat) || evalTat.Contains("{"))
            {
                double temp = TelemetryTokenEngine.EvaluateNumeric("{TEMP}", telemetry);
                if (double.IsNaN(temp)) temp = telemetry.CabinTemp;
                evalTat = string.Format(CultureInfo.InvariantCulture, "TAT {0:+0;-0;+0}c", double.IsNaN(temp) ? 0.0 : temp);
            }
            if (evalTat != _lastTatStr)
            {
                _lastTatStr = evalTat;
                if (_tatText != null) _tatText.text = evalTat;
            }

            string evalMode = TelemetryTokenEngine.Evaluate(_thrustModeTemplate, telemetry);
            if (string.IsNullOrEmpty(evalMode) || evalMode.Contains("{"))
            {
                evalMode = telemetry.Throttle > 0.85f ? "D-TO" : (telemetry.VerticalSpeed > 6.0 ? "CLB" : "CRZ");
            }
            if (evalMode != _lastModeStr)
            {
                _lastModeStr = evalMode;
                if (_thrustModeText != null) _thrustModeText.text = evalMode;
            }

            // 3. 多发主发动机真实输入接入
            double baseN1 = TelemetryTokenEngine.EvaluateNumeric(_n1Token, telemetry);
            double baseN2 = TelemetryTokenEngine.EvaluateNumeric(_n2Token, telemetry);
            double baseEgt = TelemetryTokenEngine.EvaluateNumeric(_egtToken, telemetry);
            double baseFf = TelemetryTokenEngine.EvaluateNumeric(_ffToken, telemetry);
            double baseOilP = TelemetryTokenEngine.EvaluateNumeric(_oilPToken, telemetry);
            double baseOilT = TelemetryTokenEngine.EvaluateNumeric(_oilTToken, telemetry);
            double baseOilQ = TelemetryTokenEngine.EvaluateNumeric(_oilQToken, telemetry);
            double baseVib = TelemetryTokenEngine.EvaluateNumeric(_vibToken, telemetry);

            // 真实物理遥测回退解算
            if (double.IsNaN(baseN1)) baseN1 = 20.0 + telemetry.Throttle * 80.0;
            if (double.IsNaN(baseN2)) baseN2 = 45.0 + telemetry.Throttle * 53.0;
            if (double.IsNaN(baseEgt)) baseEgt = 320.0 + telemetry.Throttle * 410.0;
            if (double.IsNaN(baseFf)) baseFf = telemetry.Throttle * 4.8;
            if (double.IsNaN(baseOilP)) baseOilP = telemetry.Throttle > 0.05f ? 48.0 + telemetry.Throttle * 12.0 : 25.0;
            if (double.IsNaN(baseOilT)) baseOilT = 55.0 + telemetry.Throttle * 30.0;
            if (double.IsNaN(baseOilQ)) baseOilQ = 18.0;
            if (double.IsNaN(baseVib))
            {
                double gShock = Math.Max(0.0, telemetry.GForce - 1.0) * 0.2;
                baseVib = 0.2 + telemetry.Throttle * 0.4 + gShock;
            }

            float[] variances = new float[] { -0.15f, 0.15f, -0.05f, 0.05f };
            float tapeHalfH = 9f * s;

            for (int i = 0; i < _currentEngineCount; i++)
            {
                EngineColumnUI col = _engineCols[i];
                if (col == null) continue;

                float vFactor = variances[i % variances.Length];
                double curN1 = Math.Max(0.0, baseN1 + vFactor * 0.8);
                double curN2 = Math.Max(0.0, baseN2 + vFactor * 0.5);
                double curEgt = Math.Max(0.0, baseEgt + vFactor * 4.0);
                double curFf = Math.Max(0.0, baseFf + vFactor * 0.05);
                double curOilP = Math.Max(0.0, baseOilP + vFactor * 1.0);
                double curOilT = Math.Max(0.0, baseOilT + vFactor * 1.5);
                double curOilQ = Math.Max(0.0, baseOilQ);
                double curVib = Math.Max(0.0, baseVib + vFactor * 0.02);

                // N1 仪表 (0% ~ 105% 映射至 0° ~ -215°)
                if (double.IsNaN(col.LastN1Val) || Math.Abs(curN1 - col.LastN1Val) > 0.05)
                {
                    col.LastN1Val = curN1;
                    string n1Str = curN1.ToString("0.0", CultureInfo.InvariantCulture);
                    if (n1Str != col.LastN1Str)
                    {
                        col.LastN1Str = n1Str;
                        if (col.N1Dial.BoxText != null) col.N1Dial.BoxText.text = n1Str;
                    }
                    float frac = Mathf.Clamp01((float)(curN1 / 105.0));
                    float needleAngle = -frac * 215f;
                    if (col.N1Dial.NeedlePivot != null)
                        col.N1Dial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, needleAngle);
                }

                // EGT 仪表 (0 ~ 800 °C 映射至 0° ~ -215°)
                if (double.IsNaN(col.LastEgtVal) || Math.Abs(curEgt - col.LastEgtVal) > 0.5)
                {
                    col.LastEgtVal = curEgt;
                    string egtStr = Mathf.RoundToInt((float)curEgt).ToString(CultureInfo.InvariantCulture);
                    if (egtStr != col.LastEgtStr)
                    {
                        col.LastEgtStr = egtStr;
                        if (col.EgtDial.BoxText != null) col.EgtDial.BoxText.text = egtStr;
                    }
                    float frac = Mathf.Clamp01((float)(curEgt / 800.0));
                    float needleAngle = -frac * 215f;
                    if (col.EgtDial.NeedlePivot != null)
                        col.EgtDial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, needleAngle);
                }

                // N2 仪表 (0% ~ 105% 映射至 0° ~ -215°)
                if (double.IsNaN(col.LastN2Val) || Math.Abs(curN2 - col.LastN2Val) > 0.05)
                {
                    col.LastN2Val = curN2;
                    string n2Str = curN2.ToString("0.0", CultureInfo.InvariantCulture);
                    if (n2Str != col.LastN2Str)
                    {
                        col.LastN2Str = n2Str;
                        if (col.N2Dial.BoxText != null) col.N2Dial.BoxText.text = n2Str;
                    }
                    float frac = Mathf.Clamp01((float)(curN2 / 105.0));
                    float needleAngle = -frac * 215f;
                    if (col.N2Dial.NeedlePivot != null)
                        col.N2Dial.NeedlePivot.localEulerAngles = new Vector3(0f, 0f, needleAngle);
                }

                // FF 燃油流量 (0.0 ~ 10.0)
                if (double.IsNaN(col.LastFfVal) || Math.Abs(curFf - col.LastFfVal) > 0.05)
                {
                    col.LastFfVal = curFf;
                    string ffStr = curFf.ToString("0.0", CultureInfo.InvariantCulture);
                    if (ffStr != col.LastFfStr)
                    {
                        col.LastFfStr = ffStr;
                        if (col.FfText != null) col.FfText.text = ffStr;
                    }
                }

                // OIL PRESS (0 ~ 100 psi)
                if (double.IsNaN(col.LastOilPVal) || Math.Abs(curOilP - col.LastOilPVal) > 0.5)
                {
                    col.LastOilPVal = curOilP;
                    string opStr = Mathf.RoundToInt((float)curOilP).ToString(CultureInfo.InvariantCulture);
                    if (opStr != col.LastOilPStr)
                    {
                        col.LastOilPStr = opStr;
                        if (col.OilPText != null) col.OilPText.text = opStr;
                    }
                    float frac = Mathf.Clamp01((float)(curOilP / 80.0));
                    float ptrY = (frac - 0.5f) * tapeHalfH * 2f;
                    if (col.OilPPointerPivot != null)
                        col.OilPPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                }

                // OIL TEMP (0 ~ 150 °C)
                if (double.IsNaN(col.LastOilTVal) || Math.Abs(curOilT - col.LastOilTVal) > 0.5)
                {
                    col.LastOilTVal = curOilT;
                    string otStr = Mathf.RoundToInt((float)curOilT).ToString(CultureInfo.InvariantCulture);
                    if (otStr != col.LastOilTStr)
                    {
                        col.LastOilTStr = otStr;
                        if (col.OilTText != null) col.OilTText.text = otStr;
                    }
                    float frac = Mathf.Clamp01((float)(curOilT / 120.0));
                    float ptrY = (frac - 0.5f) * tapeHalfH * 2f;
                    if (col.OilTPointerPivot != null)
                        col.OilTPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                }

                // OIL QTY (0 ~ 25)
                if (double.IsNaN(col.LastOilQVal) || Math.Abs(curOilQ - col.LastOilQVal) > 0.1)
                {
                    col.LastOilQVal = curOilQ;
                    string oqStr = Mathf.RoundToInt((float)curOilQ).ToString(CultureInfo.InvariantCulture);
                    if (oqStr != col.LastOilQStr)
                    {
                        col.LastOilQStr = oqStr;
                        if (col.OilQText != null) col.OilQText.text = oqStr;
                    }
                }

                // VIB (0.0 ~ 5.0)
                if (double.IsNaN(col.LastVibVal) || Math.Abs(curVib - col.LastVibVal) > 0.05)
                {
                    col.LastVibVal = curVib;
                    string vibStr = curVib.ToString("0.0", CultureInfo.InvariantCulture);
                    if (vibStr != col.LastVibStr)
                    {
                        col.LastVibStr = vibStr;
                        if (col.VibText != null) col.VibText.text = vibStr;
                    }
                    float frac = Mathf.Clamp01((float)(curVib / 4.0));
                    float ptrY = (frac - 0.5f) * tapeHalfH * 2f;
                    if (col.VibPointerPivot != null)
                        col.VibPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
                }
            }

            // 4. 右侧起落架状态 (GEAR)
            string evalGear = TelemetryTokenEngine.Evaluate(_gearToken, telemetry);
            if (string.IsNullOrEmpty(evalGear) || evalGear.Contains("{"))
            {
                evalGear = (telemetry.AltitudeAGL < 80.0 || telemetry.VerticalSpeed < -1.0) ? "DOWN" : "UP";
            }
            if (evalGear != _lastGearStr)
            {
                _lastGearStr = evalGear;
                if (_gearStatusText != null) _gearStatusText.text = evalGear;
                if (evalGear.IndexOf("DOWN", StringComparison.OrdinalIgnoreCase) >= 0)
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
            string evalFlap = TelemetryTokenEngine.Evaluate(_flapToken, telemetry);
            if (string.IsNullOrEmpty(evalFlap) || evalFlap.Contains("{"))
            {
                evalFlap = telemetry.SurfaceSpeed < 70.0 ? "20" : (telemetry.SurfaceSpeed < 90.0 ? "5" : "0");
            }
            if (evalFlap != _lastFlapStr)
            {
                _lastFlapStr = evalFlap;
                if (_flapPositionText != null) _flapPositionText.text = evalFlap;
                float flapRatio = 0f;
                if (evalFlap == "1") flapRatio = 0.2f;
                else if (evalFlap == "5") flapRatio = 0.4f;
                else if (evalFlap == "15") flapRatio = 0.6f;
                else if (evalFlap == "20") flapRatio = 0.8f;
                else if (evalFlap == "30") flapRatio = 1.0f;

                float travelH = 36f * s;
                float ptrY = -flapRatio * travelH;
                if (_flapPointerPivot != null)
                    _flapPointerPivot.anchoredPosition = new Vector2(0f, ptrY);
            }

            // 6. 安定面配平 (STAB TRIM)
            double pitchTrim = TelemetryTokenEngine.EvaluateNumeric(_stabToken, telemetry);
            if (double.IsNaN(pitchTrim)) pitchTrim = telemetry.PitchTrim;
            if (double.IsNaN(pitchTrim)) pitchTrim = 0.0;

            double stabUnits = 10.25 + pitchTrim * 4.0;
            string stabStr = stabUnits.ToString("0.00", CultureInfo.InvariantCulture);
            if (stabStr != _lastStabStr)
            {
                _lastStabStr = stabStr;
                if (_stabValueText != null) _stabValueText.text = stabStr;
                if (_stabTargetText != null) _stabTargetText.text = stabStr;

                float stabFrac = Mathf.Clamp01((float)((stabUnits - 4.0) / 12.0));
                float ptrY = (stabFrac - 0.5f) * 30f * s;
                if (_stabPointerRt != null)
                    _stabPointerRt.anchoredPosition = new Vector2(0f, ptrY);
            }

            // 7. 方向舵配平 (RUDDER TRIM)
            double yawTrim = TelemetryTokenEngine.EvaluateNumeric(_rudderToken, telemetry);
            if (double.IsNaN(yawTrim)) yawTrim = telemetry.YawTrim;
            if (double.IsNaN(yawTrim)) yawTrim = 0.0;

            double rudderDeg = yawTrim * 10.0;
            string rudStr = rudderDeg.ToString("0.0", CultureInfo.InvariantCulture);
            if (rudStr != _lastRudderStr)
            {
                _lastRudderStr = rudStr;
                if (_rudderValueText != null) _rudderValueText.text = rudStr;

                float rudFrac = Mathf.Clamp((float)(rudderDeg / 10.0), -1f, 1f);
                float ptrX = rudFrac * 14f * s;
                if (_rudderPointerRt != null)
                    _rudderPointerRt.anchoredPosition = new Vector2(ptrX, 4f * s);
            }

            // 8. ECS 客舱增压系统
            double cabAlt = telemetry.AltitudeASL * 3.28084 * 0.35;
            if (cabAlt < 0.0) cabAlt = 0.0;
            if (cabAlt > 8000.0) cabAlt = 8000.0;
            string cabAltStr = Mathf.RoundToInt((float)cabAlt).ToString(CultureInfo.InvariantCulture);
            if (cabAltStr != _lastCabAltStr)
            {
                _lastCabAltStr = cabAltStr;
                if (_cabAltValue != null) _cabAltValue.text = cabAltStr;
            }

            double cabRate = telemetry.VerticalSpeed * 196.85 * 0.25;
            string cabRateStr = string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}", Mathf.RoundToInt((float)cabRate));
            if (cabRateStr != _lastCabRateStr)
            {
                _lastCabRateStr = cabRateStr;
                if (_cabRateValue != null) _cabRateValue.text = cabRateStr;
            }

            double deltaP = Math.Max(0.0, (14.7 - (telemetry.AtmosphericPressure * 14.7)) * 0.55);
            string deltaPStr = deltaP.ToString("0.0", CultureInfo.InvariantCulture);
            if (deltaPStr != _lastDeltaPStr)
            {
                _lastDeltaPStr = deltaPStr;
                if (_deltaPValue != null) _deltaPValue.text = deltaPStr;
            }

            // 9. 全机总重与燃油统计 (GROSS WT / TOTAL FUEL)
            double fuelFrac = telemetry.StagePropellantFraction;
            double fuelLbs = 102.6 * (fuelFrac > 0.001 ? fuelFrac : 0.85);
            double grossWtLbs = 210.0 + fuelLbs;
            string gwStr = grossWtLbs.ToString("0.0", CultureInfo.InvariantCulture);
            if (gwStr != _lastGrossWtStr)
            {
                _lastGrossWtStr = gwStr;
                if (_grossWtText != null) _grossWtText.text = gwStr;
            }

            string fuelStr = fuelLbs.ToString("0.0", CultureInfo.InvariantCulture);
            if (fuelStr != _lastTotalFuelStr)
            {
                _lastTotalFuelStr = fuelStr;
                if (_totalFuelText != null) _totalFuelText.text = fuelStr;
            }

            double satVal = TelemetryTokenEngine.EvaluateNumeric("{TEMP:ATM}", telemetry);
            if (double.IsNaN(satVal)) satVal = telemetry.CabinTemp;
            string satStr = string.Format(CultureInfo.InvariantCulture, "SAT {0:+0;-0;0}", double.IsNaN(satVal) ? 0.0 : satVal);
            if (satStr != _lastSatStr)
            {
                _lastSatStr = satStr;
                if (_satText != null) _satText.text = satStr;
            }

            double fuelTempVal = satVal + 5.0;
            string ftStr = string.Format(CultureInfo.InvariantCulture, "FUEL TEMP {0:+0;-0;0}", fuelTempVal);
            if (ftStr != _lastFuelTempStr)
            {
                _lastFuelTempStr = ftStr;
                if (_fuelTempText != null) _fuelTempText.text = ftStr;
            }
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
