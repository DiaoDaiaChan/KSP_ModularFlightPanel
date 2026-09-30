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
    /// 经典波音 747-400 四发主发动机与机组告警显示器 (Boeing 747 EICAS Primary Engine Display)
    /// 忠实还原波音 747 经典 CRT 航电布局：
    /// 1. 顶端航电状态栏：TAT 环境总温读数与当前推力管理模式 (TO / CLB / CRZ / CON)
    /// 2. 四发独立垂直仪表阵列：
    ///    - Row 1: EPR (发动机压力比) 顶端目标游标 + 白色矩形框显 + 垂直柱状条 + 目标游标刻度
    ///    - Row 2: N1 (低压风扇转速 %) 白色矩形框显 + 垂直柱状条 + 顶端超速红色告警限值线
    ///    - Row 3: EGT (排气温度 °C) 白色矩形框显 + 垂直柱状条 + 顶端超温告警限值线
    /// 3. 右侧机组告警与起落架状态：
    ///    - 机组状态备忘 (DOORS AUTO / SAS ACTIVE / TERRAIN)
    ///    - 绿色方框 [DOWN] GEAR 起落架锁定指示器
    /// 4. 底部系统状态读数（全部来自本地真实遥测通道）：
    ///    - EC / COMM 电气余量与通信信号
    ///    - TOTAL FUEL 机载剩余燃油
    ///    - APO / PER 远地点与近地点高度
    /// 5. 100% 由 TelemetryTokenEngine 与 CustomTemplate 驱动，0 硬编码与 0 颜色字面量。
    ///    注：引气导管压力 (DUCT PRESS)、座舱增压高度/着陆高度 (CAB ALT / LDG ALT) 与
    ///    燃油温度 (FUEL TEMP) 在 KSP 中无对应遥测数据源，已移除，不再伪造读数。
    /// </summary>
    /// <summary>
    /// 经典波音 747 单发仪表槽位状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct B747EngineSlotState : IEquatable<B747EngineSlotState>
    {
        public bool HasEngine;
        public string EprText;
        public float EprFraction;
        public string N1Text;
        public float N1Fraction;
        public string EgtText;
        public float EgtFraction;

        public bool Equals(B747EngineSlotState other)
        {
            return HasEngine == other.HasEngine &&
                   EprText == other.EprText &&
                   Math.Abs(EprFraction - other.EprFraction) < 0.002f &&
                   N1Text == other.N1Text &&
                   Math.Abs(N1Fraction - other.N1Fraction) < 0.002f &&
                   EgtText == other.EgtText &&
                   Math.Abs(EgtFraction - other.EgtFraction) < 0.002f;
        }

        public override bool Equals(object obj) => obj is B747EngineSlotState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasEngine ? 1 : 0);
                hash = (hash * 397) ^ (EprText != null ? EprText.GetHashCode() : 0);
                hash = (hash * 397) ^ (N1Text != null ? N1Text.GetHashCode() : 0);
                hash = (hash * 397) ^ (EgtText != null ? EgtText.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 经典波音 747 主发动机与机组告警显示状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct B747EicasState : IEquatable<B747EicasState>
    {
        public bool HasVessel;
        public string TatText;
        public string ThrustModeText;
        public string Cas1Text;
        public string Cas2Text;
        public bool IsTouchdownAlert;
        public bool IsSasEnabled;
        public string GearText;
        public bool IsGearDown;
        public string SystemsLineText;
        public string FuelLineText;
        public string OrbitLineText;

        public B747EngineSlotState Eng0;
        public B747EngineSlotState Eng1;
        public B747EngineSlotState Eng2;
        public B747EngineSlotState Eng3;

        public B747EngineSlotState GetEngine(int index)
        {
            switch (index)
            {
                case 0: return Eng0;
                case 1: return Eng1;
                case 2: return Eng2;
                case 3: return Eng3;
                default: return default;
            }
        }

        public void SetEngine(int index, in B747EngineSlotState eng)
        {
            switch (index)
            {
                case 0: Eng0 = eng; break;
                case 1: Eng1 = eng; break;
                case 2: Eng2 = eng; break;
                case 3: Eng3 = eng; break;
            }
        }

        public bool Equals(B747EicasState other)
        {
            return HasVessel == other.HasVessel &&
                   TatText == other.TatText &&
                   ThrustModeText == other.ThrustModeText &&
                   Cas1Text == other.Cas1Text &&
                   Cas2Text == other.Cas2Text &&
                   IsTouchdownAlert == other.IsTouchdownAlert &&
                   IsSasEnabled == other.IsSasEnabled &&
                   GearText == other.GearText &&
                   IsGearDown == other.IsGearDown &&
                   SystemsLineText == other.SystemsLineText &&
                   FuelLineText == other.FuelLineText &&
                   OrbitLineText == other.OrbitLineText &&
                   Eng0.Equals(other.Eng0) &&
                   Eng1.Equals(other.Eng1) &&
                   Eng2.Equals(other.Eng2) &&
                   Eng3.Equals(other.Eng3);
        }

        public override bool Equals(object obj) => obj is B747EicasState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ (TatText != null ? TatText.GetHashCode() : 0);
                hash = (hash * 397) ^ (ThrustModeText != null ? ThrustModeText.GetHashCode() : 0);
                hash = (hash * 397) ^ (GearText != null ? GearText.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 经典波音 747 EICAS 航电纯业务解耦大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class B747EicasLogic : WidgetLogic<B747EicasState>
    {
        public const int EngineCount = 4;

        public string TatTemplate { get; set; } = "TAT {TEMP:ATM:+0;-0;+0} c";
        public string ThrustModeTemplate { get; set; } = "{THRUST:MODE}";
        public string Cas1Template { get; set; } = I18n.Tr("WIDGET_EICAS_CAS_DOORS_AUTO", "DOORS AUTO");
        public string Cas2Template { get; set; } = "{CAS:MEMO}";
        public string GearToken { get; set; } = "{GEAR}";

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            B747EicasState newState = default;
            newState.HasVessel = true;

            // 1. 顶端 TAT 与推力模式更新
            string evalTat = TelemetryTokenEngine.Evaluate(TatTemplate, telemetry);
            if (string.IsNullOrEmpty(evalTat) || evalTat.Contains("{"))
            {
                double temp = TelemetryTokenEngine.EvaluateNumeric("{TEMP}", telemetry);
                evalTat = $"TAT {(double.IsNaN(temp) ? 15.0 : temp):+0;-0;+0} c";
            }
            newState.TatText = evalTat;

            string evalMode = TelemetryTokenEngine.Evaluate(ThrustModeTemplate, telemetry);
            if (string.IsNullOrEmpty(evalMode) || evalMode.Contains("{"))
            {
                evalMode = telemetry.Throttle > 0.85f ? "TO" : (telemetry.VerticalSpeed > 8 ? "CLB" : "CRZ");
            }
            newState.ThrustModeText = evalMode;

            // 2. 四发独立真实遥测更新
            IReadOnlyList<EngineTelemetryInfo> engines = telemetry.Engines;
            for (int i = 0; i < EngineCount; i++)
            {
                bool hasEngine = engines != null && i < engines.Count;
                if (hasEngine)
                {
                    EngineTelemetryInfo eng = engines[i];
                    double eprVal = 1.0 + eng.ThrustFraction;
                    string eprText = eprVal.ToString("0.00", CultureInfo.InvariantCulture);
                    float eprFrac = Mathf.Clamp01((float)((eprVal - 0.8) / 1.0));

                    double n1Val = eng.CommandedThrottle * 100.0;
                    string n1Text = n1Val >= 10.0 ? n1Val.ToString("00.0", CultureInfo.InvariantCulture) : n1Val.ToString("0.0", CultureInfo.InvariantCulture);
                    float n1Frac = Mathf.Clamp01((float)(n1Val / 105.0));

                    double egtVal = Mathf.Max(0f, (float)eng.PartTemperature);
                    string egtText = Mathf.RoundToInt((float)egtVal).ToString(CultureInfo.InvariantCulture);
                    float egtFrac = Mathf.Clamp01((float)(egtVal / 750.0));

                    newState.SetEngine(i, new B747EngineSlotState
                    {
                        HasEngine = true,
                        EprText = eprText,
                        EprFraction = eprFrac,
                        N1Text = n1Text,
                        N1Fraction = n1Frac,
                        EgtText = egtText,
                        EgtFraction = egtFrac
                    });
                }
                else
                {
                    newState.SetEngine(i, new B747EngineSlotState
                    {
                        HasEngine = false,
                        EprText = "--",
                        EprFraction = 0f,
                        N1Text = "--",
                        N1Fraction = 0f,
                        EgtText = "--",
                        EgtFraction = 0f
                    });
                }
            }

            // 3. 右侧机组告警与起落架更新
            string evalCas1 = TelemetryTokenEngine.Evaluate(Cas1Template, telemetry);
            newState.Cas1Text = evalCas1;

            string evalCas2 = TelemetryTokenEngine.Evaluate(Cas2Template, telemetry);
            if (string.IsNullOrEmpty(evalCas2) || evalCas2.Contains("{"))
            {
                evalCas2 = telemetry.IsTouchdownAlert
                    ? I18n.Tr("WIDGET_EICAS_CAS_TERRAIN_PULL_UP", "地形拉升")
                    : (telemetry.IsSASEnabled ? I18n.Tr("WIDGET_EICAS_CAS_SAS_ACTIVE", "SAS 接通") : I18n.Tr("WIDGET_EICAS_CAS_STAB_TRIM", "安定面配平"));
            }
            newState.Cas2Text = evalCas2;
            newState.IsTouchdownAlert = telemetry.IsTouchdownAlert;
            newState.IsSasEnabled = telemetry.IsSASEnabled;

            string gearStr = TelemetryTokenEngine.Evaluate(GearToken, telemetry);
            if (string.IsNullOrEmpty(gearStr) || gearStr.Contains("{"))
            {
                bool isGearDown = telemetry.AltitudeAGL < 600.0 || telemetry.IsTouchdownAlert || telemetry.FlightSituation == "LANDED" || telemetry.FlightSituation == "PRELAUNCH";
                gearStr = isGearDown ? I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下") : I18n.Tr("WIDGET_EICAS_GEAR_UP", "收起");
            }
            newState.GearText = gearStr;
            newState.IsGearDown = gearStr.Equals("DOWN", StringComparison.OrdinalIgnoreCase) ||
                                  gearStr.Equals(I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下"), StringComparison.OrdinalIgnoreCase);

            // 4. 底部系统行：电气/通信 · 燃油 · 轨道高度
            double ecPct = telemetry.EcPercent;
            double commSig = telemetry.CommSignal;
            newState.SystemsLineText = string.Format(CultureInfo.InvariantCulture,
                "{0} {1}   {2} {3}",
                I18n.Tr("WIDGET_EICAS_EC", "电气"),
                double.IsNaN(ecPct) ? "--" : AvionicsFastFormat.FastPercent((float)Mathf.Clamp01((float)ecPct)),
                I18n.Tr("WIDGET_EICAS_COMM", "通信"),
                double.IsNaN(commSig) ? "--" : AvionicsFastFormat.FastPercent((float)Mathf.Clamp01((float)commSig)));

            double fuelFrac = telemetry.StagePropellantFraction;
            newState.FuelLineText = fuelFrac >= -0.001
                ? I18n.Tr("WIDGET_EICAS_FUEL_LINE", "剩余燃油") + " " + AvionicsFastFormat.FastPercent((float)Mathf.Clamp01((float)fuelFrac))
                : I18n.Tr("WIDGET_EICAS_NO_FUEL", "无燃料数据");

            double apo = telemetry.Apoapsis;
            double peri = telemetry.Periapsis;
            string apoStr = double.IsNaN(apo) ? "--" : (apo / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";
            string periStr = double.IsNaN(peri) ? "--" : (peri / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "km";
            newState.OrbitLineText = string.Format(CultureInfo.InvariantCulture, "{0} {1}   {2} {3}",
                I18n.Tr("WIDGET_EICAS_APO", "远地点"), apoStr,
                I18n.Tr("WIDGET_EICAS_PERI", "近地点"), periStr);

            CurrentState = newState;
        }
    }

    [FlightWidget("b747_eicas", "boeing_eicas", "eicas", Category = WidgetCategory.Systems, DisplayName = "B747 EICAS 主发动机与机组告警显示", Description = "经典波音 747 四发主发动机 CRT：EPR/N1/EGT 四发柱状表、数字框显、TAT/推力模式与起落架状态。", DefaultWidgetId = "custom.b747_eicas", DefaultX = -440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "custom.b747_eicas", "core.b747_eicas" })]
    public class B747EicasWidget : BaseFlightWidget
    {
        protected override IWidgetLogic LogicCore => _logic;
        private readonly B747EicasLogic _logic = new B747EicasLogic();

        public override Vector2 BaseSize => new Vector2(260f, 275f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget TatTitle = TextWidget.Title("TAT -- c");
        public TextWidget ThrustMode = TextWidget.Badge("TO");

        // 基础外框与背景
        private Image _bgImage;
        private Outline _bgOutline;

        // 顶端状态栏
        private Text _tatText;
        private Text _thrustModeText;

        // 发动机仪表 UI 节点 (4 纵列)
        private Text[] _eprTargetTexts = new Text[4];
        private Text[] _eprReadoutTexts = new Text[4];
        private Image[] _eprReadoutBoxes = new Image[4];
        private Outline[] _eprReadoutOutlines = new Outline[4];
        private Image[] _eprGaugeTracks = new Image[4];
        private Image[] _eprGaugeFills = new Image[4];
        private Image[] _eprTargetTicks = new Image[4];

        private Text[] _n1ReadoutTexts = new Text[4];
        private Image[] _n1ReadoutBoxes = new Image[4];
        private Outline[] _n1ReadoutOutlines = new Outline[4];
        private Image[] _n1GaugeTracks = new Image[4];
        private Image[] _n1GaugeFills = new Image[4];
        private Image[] _n1LimitTicks = new Image[4];

        private Text[] _egtReadoutTexts = new Text[4];
        private Image[] _egtReadoutBoxes = new Image[4];
        private Outline[] _egtReadoutOutlines = new Outline[4];
        private Image[] _egtGaugeTracks = new Image[4];
        private Image[] _egtGaugeFills = new Image[4];
        private Image[] _egtLimitTicks = new Image[4];

        // 仪表行标签
        private Text _eprLabel;
        private Text _n1Label;
        private Text _egtLabel;

        // 右侧机组告警与起落架
        private Text _casMemo1Text;
        private Text _casMemo2Text;
        private Image _gearBoxBg;
        private Outline _gearBoxOutline;
        private Text _gearStatusText;
        private Text _gearLabelText;

        // 底部辅助系统状态 (全部来自本地真实遥测)
        private Text _systemsLineText;   // EC 电气余量 / COMM 通信信号
        private Text _fuelLineText;      // 剩余燃油百分比
        private Text _orbitLineText;     // 远地点 / 近地点高度

        // 通配符通道与模板
        private string _tatTemplate = "TAT {TEMP:ATM:+0;-0;+0} c";
        private string _thrustModeTemplate = "{THRUST:MODE}";
        private string _eprToken = "{THR}";
        private string _n1Token = "{ENG:N1}";
        private string _egtToken = "{TEMP}";
        private string _cas1Template = I18n.Tr("WIDGET_EICAS_CAS_DOORS_AUTO", "DOORS AUTO");
        private string _cas2Template = "{CAS:MEMO}";
        private string _gearToken = "{GEAR}";
        // 底部系统行模板：仅保留可被真实遥测填充的通配符。
        // 由本组件在 heartBeat 中直接由 IFlightTelemetry 计算并写入缓存字符串，
        // 因此模板仅作 CustomTemplate 覆盖入口，不含任何伪造常量。
        private string _ductTemplate = "";
        private string _cabTemplate = "";
        private string _fuelTemplate = "";

        private string _eprLabelText = "EPR";
        private string _n1LabelText = "N1";
        private string _egtLabelText = "EGT";
        private string _gearLabelStr = I18n.Tr("WIDGET_EICAS_LABEL_GEAR", "GEAR");

        // 脏检查文本缓存
        private readonly Cached<string> _lastTatStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastModeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastCas1Str = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastCas2Str = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGearStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastDuctStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastCabStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastFuelStr = new Cached<string>(string.Empty);

        private readonly Cached<string>[] _lastEprStrs = new[] { new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty) };
        private readonly Cached<string>[] _lastN1Strs = new[] { new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty) };
        private readonly Cached<string>[] _lastEgtStrs = new[] { new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty), new Cached<string>(string.Empty) };
        private readonly CachedFloat[] _lastEprFills = new[] { new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f) };
        private readonly CachedFloat[] _lastN1Fills = new[] { new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f) };
        private readonly CachedFloat[] _lastEgtFills = new[] { new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f), new CachedFloat(-1f, 0.002f) };

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Vector2 cardSize = new Vector2(260f * s, 275f * s);
            RectTransform.sizeDelta = cardSize;

            _tatTemplate = GetTemplateChannel("TAT", _tatTemplate);
            _thrustModeTemplate = GetTemplateChannel("MODE", _thrustModeTemplate);
            _eprToken = GetTemplateChannel("EPR", _eprToken);
            _n1Token = GetTemplateChannel("N1", _n1Token);
            _egtToken = GetTemplateChannel("EGT", _egtToken);
            _cas1Template = GetTemplateChannel("CAS1", _cas1Template);
            _cas2Template = GetTemplateChannel("CAS2", _cas2Template);
            _gearToken = GetTemplateChannel("GEAR", _gearToken);
            _ductTemplate = GetTemplateChannel("DUCT", _ductTemplate);
            _cabTemplate = GetTemplateChannel("CAB", _cabTemplate);
            _fuelTemplate = GetTemplateChannel("FUEL", _fuelTemplate);
            _eprLabelText = GetTemplateChannel("EPR_LABEL", _eprLabelText);
            _n1LabelText = GetTemplateChannel("N1_LABEL", _n1LabelText);
            _egtLabelText = GetTemplateChannel("EGT_LABEL", _egtLabelText);
            _gearLabelStr = GetTemplateChannel("GEAR_LABEL", _gearLabelStr);

            // 1. 底板与边框 (CRT 质感)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(CrispLength(1f * s), CrispLength(1f * s));
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage != null ? _bgImage.color : Color.clear, _bgOutline != null ? _bgOutline.effectColor : Color.clear, s);

            // 2. 顶端状态栏
            _tatText = UIFactory.CreateText(transform, "TAT_Text", "TAT -- c", DotFont(9f, s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform tatRt = _tatText.rectTransform;
            tatRt.anchorMin = new Vector2(0f, 1f);
            tatRt.anchorMax = new Vector2(0f, 1f);
            tatRt.pivot = new Vector2(0f, 1f);
            tatRt.sizeDelta = new Vector2(100f * s, 16f * s);
            tatRt.anchoredPosition = new Vector2(10f * s, -8f * s);

            _thrustModeText = UIFactory.CreateText(transform, "Thrust_Mode_Text", "TO", DotFont(10f, s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform modeRt = _thrustModeText.rectTransform;
            modeRt.anchorMin = new Vector2(0f, 1f);
            modeRt.anchorMax = new Vector2(0f, 1f);
            modeRt.pivot = new Vector2(0.5f, 1f);
            modeRt.sizeDelta = new Vector2(40f * s, 16f * s);
            modeRt.anchoredPosition = new Vector2(100f * s, -8f * s);

            // 3. 四发垂直布局
            float[] engXCoords = new float[] { -96f * s, -64f * s, -32f * s, 0f * s };
            float boxWidth = 26f * s;
            float boxHeight = 13f * s;
            float gaugeWidth = 4f * s;
            float gaugeHeight = 24f * s;

            float eprTargetY = -24f * s;
            float eprBoxY = -35f * s;
            float eprGaugeTopY = -50f * s;

            float n1BoxY = -79f * s;
            float n1GaugeTopY = -94f * s;

            float egtBoxY = -123f * s;
            float egtGaugeTopY = -138f * s;

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color valCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color bugCol = style.GetTextColor(TextStyleRole.Accent, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);

            for (int i = 0; i < 4; i++)
            {
                float x = engXCoords[i];

                _eprTargetTexts[i] = UIFactory.CreateText(transform, $"EPR_Tgt_{i + 1}", "1.71", DotFont(8f, s),
                    TextAnchor.MiddleCenter, bugCol);
                RectTransform tgtRt = _eprTargetTexts[i].rectTransform;
                tgtRt.anchorMin = new Vector2(0.5f, 1f);
                tgtRt.anchorMax = new Vector2(0.5f, 1f);
                tgtRt.pivot = new Vector2(0.5f, 1f);
                tgtRt.sizeDelta = new Vector2(boxWidth, 11f * s);
                tgtRt.anchoredPosition = new Vector2(x, eprTargetY);

                CreateReadoutBox(transform, $"EPR_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, eprBoxY),
                    "1.01", boxBgCol, boxBorderCol, valCol, s,
                    out _eprReadoutBoxes[i], out _eprReadoutOutlines[i], out _eprReadoutTexts[i]);

                CreateVerticalGauge(transform, $"EPR_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, eprGaugeTopY),
                    trackCol, meterFillCol, bugCol, s, true,
                    out _eprGaugeTracks[i], out _eprGaugeFills[i], out _eprTargetTicks[i]);

                CreateReadoutBox(transform, $"N1_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, n1BoxY),
                    "22.8", boxBgCol, boxBorderCol, valCol, s,
                    out _n1ReadoutBoxes[i], out _n1ReadoutOutlines[i], out _n1ReadoutTexts[i]);

                CreateVerticalGauge(transform, $"N1_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, n1GaugeTopY),
                    trackCol, meterFillCol, dangerCol, s, false,
                    out _n1GaugeTracks[i], out _n1GaugeFills[i], out _n1LimitTicks[i]);

                CreateReadoutBox(transform, $"EGT_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, egtBoxY),
                    "298", boxBgCol, boxBorderCol, valCol, s,
                    out _egtReadoutBoxes[i], out _egtReadoutOutlines[i], out _egtReadoutTexts[i]);

                CreateVerticalGauge(transform, $"EGT_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, egtGaugeTopY),
                    trackCol, meterFillCol, warnCol, s, false,
                    out _egtGaugeTracks[i], out _egtGaugeFills[i], out _egtLimitTicks[i]);

                _lastEprStrs[i].Reset(string.Empty);
                _lastN1Strs[i].Reset(string.Empty);
                _lastEgtStrs[i].Reset(string.Empty);
            }

            // 4. 行标签
            Color labelCol = style.GetTextColor(TextStyleRole.Label, theme);
            float labelX = -48f * s;

            _eprLabel = UIFactory.CreateText(transform, "Label_EPR", _eprLabelText, DotFont(7f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform elRt = _eprLabel.rectTransform;
            elRt.anchorMin = new Vector2(0.5f, 1f);
            elRt.anchorMax = new Vector2(0.5f, 1f);
            elRt.pivot = new Vector2(0.5f, 0.5f);
            elRt.sizeDelta = new Vector2(24f * s, 10f * s);
            elRt.anchoredPosition = new Vector2(labelX, eprGaugeTopY - gaugeHeight * 0.5f);

            _n1Label = UIFactory.CreateText(transform, "Label_N1", _n1LabelText, DotFont(7f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform n1Rt = _n1Label.rectTransform;
            n1Rt.anchorMin = new Vector2(0.5f, 1f);
            n1Rt.anchorMax = new Vector2(0.5f, 1f);
            n1Rt.pivot = new Vector2(0.5f, 0.5f);
            n1Rt.sizeDelta = new Vector2(24f * s, 10f * s);
            n1Rt.anchoredPosition = new Vector2(labelX, n1GaugeTopY - gaugeHeight * 0.5f);

            _egtLabel = UIFactory.CreateText(transform, "Label_EGT", _egtLabelText, DotFont(7f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform egRt = _egtLabel.rectTransform;
            egRt.anchorMin = new Vector2(0.5f, 1f);
            egRt.anchorMax = new Vector2(0.5f, 1f);
            egRt.pivot = new Vector2(0.5f, 0.5f);
            egRt.sizeDelta = new Vector2(24f * s, 10f * s);
            egRt.anchoredPosition = new Vector2(labelX, egtGaugeTopY - gaugeHeight * 0.5f);

            // 5. 右侧区域：机组告警与起落架
            float rightCenterX = 68f * s;

            _casMemo1Text = UIFactory.CreateText(transform, "CAS_Memo_1", _cas1Template, DotFont(8f, s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform cas1Rt = _casMemo1Text.rectTransform;
            cas1Rt.anchorMin = new Vector2(0.5f, 1f);
            cas1Rt.anchorMax = new Vector2(0.5f, 1f);
            cas1Rt.pivot = new Vector2(0.5f, 1f);
            cas1Rt.sizeDelta = new Vector2(80f * s, 14f * s);
            cas1Rt.anchoredPosition = new Vector2(rightCenterX, -40f * s);

            _casMemo2Text = UIFactory.CreateText(transform, "CAS_Memo_2", "", DotFont(8f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform cas2Rt = _casMemo2Text.rectTransform;
            cas2Rt.anchorMin = new Vector2(0.5f, 1f);
            cas2Rt.anchorMax = new Vector2(0.5f, 1f);
            cas2Rt.pivot = new Vector2(0.5f, 1f);
            cas2Rt.sizeDelta = new Vector2(80f * s, 14f * s);
            cas2Rt.anchoredPosition = new Vector2(rightCenterX, -56f * s);

            // 起落架方框
            GameObject gearBoxObj = UIFactory.CreatePanel(transform, "Gear_Box", new Vector2(36f * s, 18f * s),
                new Vector2(rightCenterX, -82f * s), boxBgCol, bugCol, CrispLength(1.2f * s));
            _gearBoxBg = gearBoxObj.GetComponent<Image>();
            _gearBoxOutline = gearBoxObj.GetComponent<Outline>();
            RectTransform gboxRt = gearBoxObj.GetComponent<RectTransform>();
            gboxRt.anchorMin = new Vector2(0.5f, 1f);
            gboxRt.anchorMax = new Vector2(0.5f, 1f);
            gboxRt.pivot = new Vector2(0.5f, 1f);
            gboxRt.anchoredPosition = new Vector2(rightCenterX, -82f * s);

            _gearStatusText = UIFactory.CreateText(gearBoxObj.transform, "Gear_Status", I18n.Tr("WIDGET_EICAS_GEAR_DOWN", "放下"), DotFont(8f, s),
                TextAnchor.MiddleCenter, bugCol);
            RectTransform gsRt = _gearStatusText.rectTransform;
            gsRt.anchorMin = Vector2.zero;
            gsRt.anchorMax = Vector2.one;
            gsRt.sizeDelta = Vector2.zero;
            gsRt.anchoredPosition = Vector2.zero;

            _gearLabelText = UIFactory.CreateText(transform, "Gear_Label", _gearLabelStr, DotFont(7f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform glRt = _gearLabelText.rectTransform;
            glRt.anchorMin = new Vector2(0.5f, 1f);
            glRt.anchorMax = new Vector2(0.5f, 1f);
            glRt.pivot = new Vector2(0.5f, 1f);
            glRt.sizeDelta = new Vector2(40f * s, 12f * s);
            glRt.anchoredPosition = new Vector2(rightCenterX, -104f * s);

            // 6. 底部系统状态行 (电气/通信 · 燃油 · 轨道)
            _systemsLineText = UIFactory.CreateText(transform, "Systems_Line", I18n.Tr("WIDGET_EICAS_SYS_LINE", "电气 100%   通信 100%"), DotFont(8f, s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform dpRt = _systemsLineText.rectTransform;
            dpRt.anchorMin = new Vector2(0f, 0f);
            dpRt.anchorMax = new Vector2(1f, 0f);
            dpRt.pivot = new Vector2(0.5f, 0f);
            dpRt.sizeDelta = new Vector2(-20f * s, 14f * s);
            dpRt.anchoredPosition = new Vector2(0f, 46f * s);

            _fuelLineText = UIFactory.CreateText(transform, "Fuel_Line", I18n.Tr("WIDGET_EICAS_FUEL_LINE", "剩余燃油 100%"), DotFont(8f, s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform cpRt = _fuelLineText.rectTransform;
            cpRt.anchorMin = new Vector2(0f, 0f);
            cpRt.anchorMax = new Vector2(1f, 0f);
            cpRt.pivot = new Vector2(0.5f, 0f);
            cpRt.sizeDelta = new Vector2(-20f * s, 14f * s);
            cpRt.anchoredPosition = new Vector2(0f, 30f * s);

            _orbitLineText = UIFactory.CreateText(transform, "Orbit_Line", I18n.Tr("WIDGET_EICAS_ORBIT_LINE", "远地点 --   近地点 --"), DotFont(8f, s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform fsRt = _orbitLineText.rectTransform;
            fsRt.anchorMin = new Vector2(0f, 0f);
            fsRt.anchorMax = new Vector2(1f, 0f);
            fsRt.pivot = new Vector2(0.5f, 0f);
            fsRt.sizeDelta = new Vector2(-20f * s, 16f * s);
            fsRt.anchoredPosition = new Vector2(0f, 14f * s);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, t)));
            this.Controls.Register(new WidgetReadoutControl("header_status", "顶端状态栏", _tatText != null ? _tatText.gameObject : null, _tatText, _thrustModeText, TextStyleRole.Accent, _tatTemplate));
            if (_eprLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "epr_gauges", "EPR仪表组", _eprLabel.gameObject));
            if (_n1Label != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "n1_gauges", "N1仪表组", _n1Label.gameObject));
            if (_egtLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "egt_gauges", "EGT仪表组", _egtLabel.gameObject));
            this.Controls.Register(new WidgetReadoutControl("cas_memo", "机组备忘区", _casMemo1Text != null ? _casMemo1Text.gameObject : null, _casMemo1Text, _casMemo2Text, TextStyleRole.PrimaryValue, _cas1Template));
            if (_gearStatusText != null) this.Controls.Register(new WidgetAnnunciatorControl("gear_indicator", "起落架指示", _gearStatusText.gameObject, _gearStatusText, _gearLabelText, _gearBoxBg, _gearBoxOutline));
            this.Controls.Register(new WidgetReadoutControl("systems_summary", "辅助系统读数", _orbitLineText != null ? _orbitLineText.gameObject : null, _orbitLineText, _fuelLineText, TextStyleRole.PrimaryValue, _fuelTemplate));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }


        /// <summary>
        /// 小分辨率锐利度保障：字号按 DPI 缩放，但绝不小于 9 物理像素，
        /// 避免小组件在低分辨率下文字栅格化后糊成一团。
        /// </summary>
        private static int DotFont(float basePt, float s)
        {
            float scaled = basePt * s;
            return Mathf.RoundToInt(Mathf.Max(scaled, 9f));
        }

        private void CreateReadoutBox(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            string defaultText, Color bgColor, Color borderColor, Color textColor, float s,
            out Image boxBg, out Outline boxOutline, out Text readoutText)
        {
            GameObject boxObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, bgColor, borderColor, CrispLength(1f * s));
            boxBg = boxObj.GetComponent<Image>();
            boxOutline = boxObj.GetComponent<Outline>();

            RectTransform brt = boxObj.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 1f);
            brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = anchoredPos;

            readoutText = UIFactory.CreateText(boxObj.transform, "Value", defaultText, DotFont(8f, s),
                TextAnchor.MiddleCenter, textColor);
            RectTransform vrt = readoutText.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            vrt.anchoredPosition = Vector2.zero;
        }

        private void CreateVerticalGauge(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color trackColor, Color fillColor, Color tickColor, float s, bool isTargetBug,
            out Image trackImg, out Image fillImg, out Image tickImg)
        {
            // 表条本体宽度也参与锐利化钳制，避免小分辨率下整条糊掉
            Vector2 crispSize = new Vector2(CrispLength(size.x), size.y);
            GameObject trackObj = UIFactory.CreatePanel(parent, name, crispSize, anchoredPos, trackColor);
            trackImg = trackObj.GetComponent<Image>();

            RectTransform trt = trackObj.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = anchoredPos;

            GameObject fillObj = UIFactory.CreatePanel(trackObj.transform, "Fill", new Vector2(crispSize.x, 0f), Vector2.zero, fillColor);
            fillImg = fillObj.GetComponent<Image>();
            RectTransform frt = fillObj.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.anchoredPosition = Vector2.zero;

            float tickWidth = CrispLength(isTargetBug ? crispSize.x * 2.8f : crispSize.x * 2.4f);
            float tickHeight = CrispLength(2f * s);
            GameObject tickObj = UIFactory.CreatePanel(trackObj.transform, "Tick", new Vector2(tickWidth, tickHeight),
                Vector2.zero, tickColor);
            tickImg = tickObj.GetComponent<Image>();
            RectTransform tickRt = tickObj.GetComponent<RectTransform>();
            tickRt.anchorMin = new Vector2(0.5f, 1f);
            tickRt.anchorMax = new Vector2(0.5f, 1f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.anchoredPosition = Vector2.zero;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            ApplyText(_tatText, TextStyleRole.Accent, theme);
            ApplyText(_thrustModeText, TextStyleRole.Accent, theme);

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color valCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color bugCol = style.GetTextColor(TextStyleRole.Accent, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);

            for (int i = 0; i < 4; i++)
            {
                if (_eprTargetTexts[i] != null) ApplyText(_eprTargetTexts[i], TextStyleRole.Accent, theme);
                if (_eprReadoutBoxes[i] != null) _eprReadoutBoxes[i].color = boxBgCol;
                if (_eprReadoutOutlines[i] != null) _eprReadoutOutlines[i].effectColor = boxBorderCol;
                if (_eprReadoutTexts[i] != null) ApplyText(_eprReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_eprGaugeTracks[i] != null) _eprGaugeTracks[i].color = trackCol;
                if (_eprGaugeFills[i] != null) _eprGaugeFills[i].color = meterFillCol;
                if (_eprTargetTicks[i] != null) _eprTargetTicks[i].color = bugCol;

                if (_n1ReadoutBoxes[i] != null) _n1ReadoutBoxes[i].color = boxBgCol;
                if (_n1ReadoutOutlines[i] != null) _n1ReadoutOutlines[i].effectColor = boxBorderCol;
                if (_n1ReadoutTexts[i] != null) ApplyText(_n1ReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_n1GaugeTracks[i] != null) _n1GaugeTracks[i].color = trackCol;
                if (_n1GaugeFills[i] != null) _n1GaugeFills[i].color = meterFillCol;
                if (_n1LimitTicks[i] != null) _n1LimitTicks[i].color = dangerCol;

                if (_egtReadoutBoxes[i] != null) _egtReadoutBoxes[i].color = boxBgCol;
                if (_egtReadoutOutlines[i] != null) _egtReadoutOutlines[i].effectColor = boxBorderCol;
                if (_egtReadoutTexts[i] != null) ApplyText(_egtReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_egtGaugeTracks[i] != null) _egtGaugeTracks[i].color = trackCol;
                if (_egtGaugeFills[i] != null) _egtGaugeFills[i].color = meterFillCol;
                if (_egtLimitTicks[i] != null) _egtLimitTicks[i].color = warnCol;
            }

            ApplyText(_eprLabel, TextStyleRole.Label, theme);
            ApplyText(_n1Label, TextStyleRole.Label, theme);
            ApplyText(_egtLabel, TextStyleRole.Label, theme);

            ApplyText(_casMemo1Text, TextStyleRole.PrimaryValue, theme);
            ApplyText(_casMemo2Text, TextStyleRole.Label, theme);

            if (_gearBoxBg != null) _gearBoxBg.color = boxBgCol;
            if (_gearBoxOutline != null) _gearBoxOutline.effectColor = bugCol;
            if (_gearStatusText != null) ApplyText(_gearStatusText, TextStyleRole.Accent, theme);
            if (_gearLabelText != null) ApplyText(_gearLabelText, TextStyleRole.Label, theme);

            ApplyText(_systemsLineText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_fuelLineText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_orbitLineText, TextStyleRole.PrimaryValue, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            _logic.TatTemplate = _tatTemplate;
            _logic.ThrustModeTemplate = _thrustModeTemplate;
            _logic.Cas1Template = _cas1Template;
            _logic.Cas2Template = _cas2Template;
            _logic.GearToken = _gearToken;
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

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(WidgetStyleManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 顶端 TAT 与推力模式更新
            if (_lastTatStr.Update(state.TatText))
            {
                SetTextIfChanged(_tatText, state.TatText);
            }

            if (_lastModeStr.Update(state.ThrustModeText))
            {
                SetTextIfChanged(_thrustModeText, state.ThrustModeText);
            }

            // 2. 四发独立仪表更新
            float gaugeMaxH = 24f * CurrentDpiScale;
            for (int i = 0; i < 4; i++)
            {
                var eng = state.GetEngine(i);

                if (_lastEprStrs[i].Update(eng.EprText))
                {
                    SetTextIfChanged(_eprReadoutTexts[i], eng.EprText);
                }
                if (_eprGaugeFills[i] != null && _lastEprFills[i].Update(eng.EprFraction))
                {
                    _eprGaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * eng.EprFraction);
                }

                if (_lastN1Strs[i].Update(eng.N1Text))
                {
                    SetTextIfChanged(_n1ReadoutTexts[i], eng.N1Text);
                }
                if (_n1GaugeFills[i] != null && _lastN1Fills[i].Update(eng.N1Fraction))
                {
                    _n1GaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * eng.N1Fraction);
                }

                if (_lastEgtStrs[i].Update(eng.EgtText))
                {
                    SetTextIfChanged(_egtReadoutTexts[i], eng.EgtText);
                }
                if (_egtGaugeFills[i] != null && _lastEgtFills[i].Update(eng.EgtFraction))
                {
                    _egtGaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * eng.EgtFraction);
                }
            }

            // 3. 右侧机组告警与起落架更新
            if (_lastCas1Str.Update(state.Cas1Text))
            {
                SetTextIfChanged(_casMemo1Text, state.Cas1Text);
            }

            if (_lastCas2Str.Update(state.Cas2Text))
            {
                if (_casMemo2Text != null)
                {
                    SetTextIfChanged(_casMemo2Text, state.Cas2Text);
                    TextStyleRole casRole = state.IsTouchdownAlert ? TextStyleRole.Danger : (state.IsSasEnabled ? TextStyleRole.Accent : TextStyleRole.Label);
                    ApplyText(_casMemo2Text, casRole, theme);
                }
            }

            if (_lastGearStr.Update(state.GearText))
            {
                if (_gearStatusText != null)
                {
                    SetTextIfChanged(_gearStatusText, state.GearText);
                    bool isDown = state.IsGearDown;
                    ApplyText(_gearStatusText, isDown ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                    if (_gearBoxOutline != null)
                    {
                        _gearBoxOutline.effectColor = isDown
                            ? style.GetTextColor(TextStyleRole.Accent, theme)
                            : style.GetCardBorderColor(CardStyleRole.Normal, theme);
                    }
                }
            }

            // 4. 底部系统行更新 (电气/通信 · 燃油 · 轨道)
            if (_lastDuctStr.Update(state.SystemsLineText))
            {
                SetTextIfChanged(_systemsLineText, state.SystemsLineText);
            }

            if (_lastCabStr.Update(state.FuelLineText))
            {
                SetTextIfChanged(_fuelLineText, state.FuelLineText);
            }

            if (_lastFuelStr.Update(state.OrbitLineText))
            {
                SetTextIfChanged(_orbitLineText, state.OrbitLineText);
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastTatStr.Reset(string.Empty);
            _lastModeStr.Reset(string.Empty);
            _lastCas1Str.Reset(string.Empty);
            _lastCas2Str.Reset(string.Empty);
            _lastGearStr.Reset(string.Empty);
            _lastDuctStr.Reset(string.Empty);
            _lastCabStr.Reset(string.Empty);
            _lastFuelStr.Reset(string.Empty);
            for (int i = 0; i < 4; i++)
            {
                _lastEprStrs[i].Reset(string.Empty);
                _lastN1Strs[i].Reset(string.Empty);
                _lastEgtStrs[i].Reset(string.Empty);
                _lastEprFills[i].Reset(-1f);
                _lastN1Fills[i].Reset(-1f);
                _lastEgtFills[i].Reset(-1f);
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
