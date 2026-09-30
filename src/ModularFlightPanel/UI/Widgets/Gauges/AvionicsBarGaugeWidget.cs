using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public enum BarGaugeKind
    {
        Throttle,
        AtmosphericPressure,
        DynamicPressure
    }

    /// <summary>
    /// 垂直柱状计量表状态快照 (0 GC struct)
    /// </summary>
    public struct AvionicsBarGaugeState : IEquatable<AvionicsBarGaugeState>
    {
        public bool HasVessel;
        public string TitleStr;
        public string ValueStr;
        public string BottomStr;
        public TextStyleRole BottomRole;
        public CardStyleRole Role;
        public float TargetThrottle;
        public float CommandedThrottle;
        public float SpoolThrottle;
        public float FillFraction;
        public float CmdFraction;
        public bool HasCmdPointer;

        public bool Equals(AvionicsBarGaugeState other)
        {
            return HasVessel == other.HasVessel &&
                   TitleStr == other.TitleStr &&
                   ValueStr == other.ValueStr &&
                   BottomStr == other.BottomStr &&
                   BottomRole == other.BottomRole &&
                   Role == other.Role &&
                   Math.Abs(FillFraction - other.FillFraction) < 0.001f &&
                   Math.Abs(CmdFraction - other.CmdFraction) < 0.001f &&
                   HasCmdPointer == other.HasCmdPointer;
        }

        public override bool Equals(object obj) => obj is AvionicsBarGaugeState other && Equals(other);
        public override int GetHashCode() => (TitleStr, ValueStr, Role).GetHashCode();
    }

    /// <summary>
    /// 垂直柱状计量表业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class AvionicsBarGaugeLogic : WidgetLogic<AvionicsBarGaugeState>
    {
        public BarGaugeKind Kind = BarGaugeKind.Throttle;
        public string ValueToken = "{THR}";
        public string TitleTemplate = "THR";
        public string BottomTagTemplate = "IDLE";
        public double MinVal = 0.0;
        public double MaxVal = 100.0;
        public double CautionVal = 0.0;
        public double WarningVal = 0.0;

        private float _commandedThrottle = float.NaN;
        private float _spoolThrottle = float.NaN;

        public override void Reset()
        {
            CurrentState = default;
            _commandedThrottle = float.NaN;
            _spoolThrottle = float.NaN;
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

            string titleStr = BaseFlightWidget.EvalToken(TitleTemplate, telemetry);
            double val = BaseFlightWidget.EvalNumeric(ValueToken, telemetry);
            if (double.IsNaN(val)) val = 0.0;

            double range = MaxVal - MinVal;
            if (range <= 0.0001) range = 1.0;

            if (Kind == BarGaugeKind.Throttle)
            {
                float targetThrottle = Mathf.Clamp((float)val, (float)MinVal, (float)MaxVal);
                _commandedThrottle = targetThrottle;

                if (float.IsNaN(_spoolThrottle))
                {
                    if (Application.isBatchMode && targetThrottle > 50f)
                    {
                        _spoolThrottle = targetThrottle * 0.82f;
                    }
                    else
                    {
                        _spoolThrottle = targetThrottle;
                    }
                }
                else
                {
                    float dt = deltaTime > 0f ? Mathf.Min(deltaTime, 0.1f) : 0.02f;
                    float spoolSpeed = 180f;
                    _spoolThrottle = Mathf.MoveTowards(_spoolThrottle, _commandedThrottle, spoolSpeed * dt);
                }

                float cmdFrac = Mathf.Clamp01((_commandedThrottle - (float)MinVal) / (float)range);
                float spoolFrac = Mathf.Clamp01((_spoolThrottle - (float)MinVal) / (float)range);
                string valueStr = $"{Mathf.RoundToInt(_spoolThrottle)}%";

                string statusTag;
                if (_spoolThrottle < 2f) statusTag = "IDLE";
                else if (Mathf.Abs(_spoolThrottle - _commandedThrottle) > 1.5f) statusTag = "SPOOL";
                else if (_spoolThrottle >= 98f) statusTag = "MAX";
                else statusTag = "MIL";

                TextStyleRole btmRole = (statusTag == "MAX" || statusTag == "SPOOL") ? TextStyleRole.Accent : TextStyleRole.Muted;
                CardStyleRole targetRole = CardStyleRole.Normal;
                if (WarningVal > 0 && targetThrottle >= WarningVal) targetRole = CardStyleRole.Danger;
                else if (CautionVal > 0 && targetThrottle >= CautionVal) targetRole = CardStyleRole.Warning;

                CurrentState = new AvionicsBarGaugeState
                {
                    HasVessel = true,
                    TitleStr = titleStr,
                    ValueStr = valueStr,
                    BottomStr = statusTag,
                    BottomRole = btmRole,
                    Role = targetRole,
                    TargetThrottle = targetThrottle,
                    CommandedThrottle = _commandedThrottle,
                    SpoolThrottle = _spoolThrottle,
                    FillFraction = spoolFrac,
                    CmdFraction = cmdFrac,
                    HasCmdPointer = true
                };
            }
            else if (Kind == BarGaugeKind.AtmosphericPressure)
            {
                float currentAtm = Mathf.Clamp((float)val, (float)MinVal, (float)MaxVal);
                float atmFrac = Mathf.Clamp01((currentAtm - (float)MinVal) / (float)range);

                string valueStr;
                if (val < 0.001) valueStr = "0.00 atm";
                else if (val < 0.10) valueStr = $"{val:F3} atm";
                else valueStr = $"{val:F2} atm";

                string layerTag;
                if (val >= 0.70) layerTag = "SEA";
                else if (val >= 0.30) layerTag = "TROP";
                else if (val >= 0.05) layerTag = "STRAT";
                else if (val > 0.001) layerTag = "MESO";
                else layerTag = "VAC";

                TextStyleRole btmRole = (layerTag == "VAC") ? TextStyleRole.Muted : TextStyleRole.Accent;

                CurrentState = new AvionicsBarGaugeState
                {
                    HasVessel = true,
                    TitleStr = titleStr,
                    ValueStr = valueStr,
                    BottomStr = layerTag,
                    BottomRole = btmRole,
                    Role = CardStyleRole.Normal,
                    TargetThrottle = 0f,
                    CommandedThrottle = 0f,
                    SpoolThrottle = 0f,
                    FillFraction = atmFrac,
                    CmdFraction = 0f,
                    HasCmdPointer = false
                };
            }
            else
            {
                float generalFrac = Mathf.Clamp01((float)((val - MinVal) / range));
                string valueStr = BaseFlightWidget.EvalToken(ValueToken, telemetry);
                string btmStr = BaseFlightWidget.EvalToken(BottomTagTemplate, telemetry);

                CurrentState = new AvionicsBarGaugeState
                {
                    HasVessel = true,
                    TitleStr = titleStr,
                    ValueStr = valueStr,
                    BottomStr = btmStr,
                    BottomRole = TextStyleRole.Accent,
                    Role = CardStyleRole.Normal,
                    TargetThrottle = 0f,
                    CommandedThrottle = 0f,
                    SpoolThrottle = 0f,
                    FillFraction = generalFrac,
                    CmdFraction = 0f,
                    HasCmdPointer = false
                };
            }
        }
    }

    /// <summary>
    /// 现代航电垂直高精光柱带 (Avionics Precision Vertical Bar Gauge) - 次世代重构版
    /// </summary>
    [FlightWidget("bar_gauge", "bar", "avionics_bar", Category = WidgetCategory.Gauges, DisplayName = "垂直柱状计量表", Description = "高刷新线性竖条计量标尺，适用于节流阀、过载或推进剂余量。", DefaultWidgetId = "gauge.bar", DefaultX = 0f, DefaultY = 0f)]
    public class AvionicsBarGaugeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(24f, 240f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        private readonly AvionicsBarGaugeLogic _logic = new AvionicsBarGaugeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private BarGaugeKind _kind;

        // 核心轨道与背景 (Track)
        private RectTransform _trackRt;
        private Image _trackBg;
        private Outline _trackOutline;
        private Image _topGlossRim;
        private Image _bottomGlossRim;
        private Image _backboneRail;

        // 动态充填条与顶部截面高光线 (Fill Bar & Cap Ray)
        private RectTransform _fillBarRt;
        private Image _fillBarImage;
        private RectTransform _capRayRt;
        private Image _capRayImg;

        // 零位迹线发丝 (Trace Hairline from Datum to Pointer)
        private RectTransform _traceRt;
        private Image _traceImg;

        // 游标指示器 (Needle Pointers)
        private GameObject _actPointerObj;
        private RectTransform _actPointerRt;
        private Image _actPointerStem;
        private Image _actPointerHead;

        private GameObject _cmdPointerObj;
        private RectTransform _cmdPointerRt;
        private Image _cmdPointerStem;
        private Image _cmdPointerHead;
        private RectTransform _cmdBugLineRt;
        private Image _cmdBugLineImg;

        // 顶部技术铭牌与数显盒 (Top Tag Box)
        private GameObject _topTagBox;
        private Image _topTagBg;
        private Outline _topTagOutline;
        private Image _topLedDot;
        private Text _topTagTitle;
        private Text _topTagValue;

        // 底部档位与环境标牌盒 (Bottom Tag Box)
        private GameObject _bottomTagBox;
        private Image _bottomTagBg;
        private Outline _bottomTagOutline;
        private Text _bottomTagText;

        // 警戒线
        private GameObject _cautionLineObj;
        private RectTransform _cautionLineRt;
        private Image _cautionLineImg;

        // 大气渐变矢量图元
        private AtmosphereGradientGraphic _atmosphereGraphic;

        // 配置缓存
        private string _valueToken = "{THR}";
        private string _titleTemplate = "THR";
        private string _bottomTagTemplate = "IDLE";
        private double _minVal = 0.0;
        private double _maxVal = 100.0;
        private double _cautionVal = 0.0;
        private double _warningVal = 0.0;

        private CardStyleRole _currentRole = CardStyleRole.Normal;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float barWidth = 22f * s;
            float barHeight = 240f * s;

            if (config != null && (config.NumericToken == "{ATM}" || config.WidgetId == "gauge.barometer" || config.WidgetId.Contains("baro") || config.WidgetId.Contains("atm")))
            {
                _kind = BarGaugeKind.AtmosphericPressure;
                _valueToken = "{ATM}";
                _titleTemplate = "ATM";
                _bottomTagTemplate = "SEA";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 1.0;
            }
            else if (config != null && (config.NumericToken == "{Q}" || config.WidgetId == "gauge.q" || config.WidgetId.Contains("q")))
            {
                _kind = BarGaugeKind.DynamicPressure;
                _valueToken = "{Q}";
                _titleTemplate = "Q";
                _bottomTagTemplate = "MAX Q";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 35.0;
                _cautionVal = config.CautionThreshold > 0 ? config.CautionThreshold : 20.0;
                _warningVal = config.WarningThreshold > 0 ? config.WarningThreshold : 28.0;
            }
            else
            {
                _kind = BarGaugeKind.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THR}";
                _titleTemplate = "THR";
                _bottomTagTemplate = "IDLE";
                _minVal = config != null && config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config != null && config.MaxValue > 0 ? config.MaxValue : 100.0;
            }

            if (!string.IsNullOrEmpty(config?.DisplayName) && config.DisplayName.Length <= 4 && !config.DisplayName.Contains(I18n.Tr("SUFFIX_TAPE_CHAR", "带")))
            {
                _titleTemplate = config.DisplayName;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, _valueToken);
            _titleTemplate = GetTemplateChannel(new[] { "TITLE", "LABEL", "NAME" }, _titleTemplate);
            _bottomTagTemplate = GetTemplateChannel(new[] { "TAG", "BOTTOM", "BTM" }, _bottomTagTemplate);
            _minVal = GetTemplateChannelFloat("MIN", (float)_minVal);
            _maxVal = GetTemplateChannelFloat("MAX", (float)_maxVal);
            _cautionVal = GetTemplateChannelFloat("CAUTION", (float)_cautionVal);
            _warningVal = GetTemplateChannelFloat("WARNING", (float)_warningVal);

            _logic.Kind = _kind;
            _logic.ValueToken = _valueToken;
            _logic.TitleTemplate = _titleTemplate;
            _logic.BottomTagTemplate = _bottomTagTemplate;
            _logic.MinVal = _minVal;
            _logic.MaxVal = _maxVal;
            _logic.CautionVal = _cautionVal;
            _logic.WarningVal = _warningVal;

            RectTransform.sizeDelta = new Vector2(barWidth, barHeight);

            float trackWidth = 24f * s;
            float trackHeight = 186f * s;
            BuildTrack(trackWidth, trackHeight, s, theme);
            BuildFillBar(trackWidth, trackHeight, s, theme);
            BuildTickGraduations(trackWidth, trackHeight, s, theme);
            BuildNeedlePointers(trackWidth, trackHeight, s, theme);
            BuildTopTag(barWidth, barHeight, s, theme);
            BuildBottomTag(barWidth, barHeight, s, theme);

            if (_kind == BarGaugeKind.DynamicPressure || _cautionVal > 0)
            {
                BuildCautionCue(trackWidth, trackHeight, s, theme);
            }

            this.Controls.Register(WidgetControlManager.WrapElement(this, "track", "刻度轨道", _trackRt != null ? _trackRt.gameObject : gameObject, (t) => {
                if (_trackBg != null && _kind != BarGaugeKind.AtmosphericPressure) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, t);
            }));
            if (_fillBarRt != null)
            {
                this.Controls.Register(new WidgetLinearBarControl(this, "fill_bar", "充填光柱", _fillBarRt.gameObject, null, _fillBarImage, _valueToken, _minVal, _maxVal, 100f, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });
            }
            if (_actPointerObj != null)
            {
                this.Controls.Register(new WidgetNeedleControl("act_pointer", "实际值指针", _actPointerObj, null, null, null));
            }
            if (_cmdPointerObj != null)
            {
                this.Controls.Register(new WidgetNeedleControl("cmd_pointer", "指令游标", _cmdPointerObj, null, null, null));
            }
            if (_topTagBox != null)
            {
                this.Controls.Register(new WidgetReadoutControl("top_tag", "顶部胶囊读数", _topTagBox, _topTagValue, _topTagTitle, TextStyleRole.PrimaryValue, _valueToken));
            }
            if (_bottomTagBox != null)
            {
                this.Controls.Register(new WidgetReadoutControl("bottom_tag", "底部档位标牌", _bottomTagBox, _bottomTagText, null, TextStyleRole.Accent, _valueToken));
            }
            if (_cautionLineObj != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "caution_line", "警戒标线", _cautionLineObj, (t) => {
                    if (_cautionLineImg != null) _cautionLineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Warning, t);
                }));
            }

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildTrack(float w, float h, float s, ThemeConfig theme)
        {
            if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                _atmosphereGraphic = CreateChild<AtmosphereGradientGraphic>("Atmosphere_Gradient_Track", transform, new Vector2(w, h), Vector2.zero);
                _trackRt = _atmosphereGraphic.rectTransform;
                _atmosphereGraphic.Theme = theme;
                _trackOutline = _atmosphereGraphic.gameObject.AddComponent<Outline>();
                _trackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                _trackOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.35f);
            }
            else
            {
                _trackBg = CreateChild<Image>("Gauge_Track", transform, new Vector2(w, h), Vector2.zero);
                _trackRt = _trackBg.rectTransform;
                _trackOutline = _trackBg.gameObject.AddComponent<Outline>();
                _trackOutline.effectDistance = new Vector2(1f * s, 1f * s);
                ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, theme);
            }

            GameObject topRim = UIFactory.CreatePanel(_trackRt, "Top_Gloss_Rim",
                new Vector2(w - 2f * s, 1.2f * s), new Vector2(0f, (h * 0.5f) - 1f * s),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.50f));
            _topGlossRim = topRim.GetComponent<Image>();

            GameObject bottomRim = UIFactory.CreatePanel(_trackRt, "Bottom_Gloss_Rim",
                new Vector2(w - 2f * s, 1.2f * s), new Vector2(0f, (-h * 0.5f) + 1f * s),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f));
            _bottomGlossRim = bottomRim.GetComponent<Image>();
        }

        private void BuildFillBar(float w, float h, float s, ThemeConfig theme)
        {
            _fillBarImage = CreateChild<Image>("Gauge_FillBar", _trackRt, new Vector2(-4f * s, 0f), new Vector2(0f, 2f * s));
            _fillBarRt = _fillBarImage.rectTransform;
            _fillBarRt.anchorMin = new Vector2(0f, 0f);
            _fillBarRt.anchorMax = new Vector2(1f, 0f);
            _fillBarRt.pivot = new Vector2(0.5f, 0f);

            if (_kind == BarGaugeKind.AtmosphericPressure || _kind == BarGaugeKind.Throttle)
            {
                _fillBarImage.color = Color.clear;
            }
            else
            {
                _fillBarImage.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            Color capCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? WidgetStyleManager.WithAlpha(theme.AccentSecondary.ToColor(), 0.90f)
                : WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.85f);
            GameObject capObj = UIFactory.CreatePanel(_fillBarRt, "Cap_Ray",
                new Vector2(w - 4f * s, 1.5f * s), Vector2.zero, capCol);
            _capRayRt = capObj.GetComponent<RectTransform>();
            _capRayRt.anchorMin = new Vector2(0.5f, 1f);
            _capRayRt.anchorMax = new Vector2(0.5f, 1f);
            _capRayRt.pivot = new Vector2(0.5f, 1f);
            _capRayRt.anchoredPosition = Vector2.zero;
            _capRayImg = capObj.GetComponent<Image>();

            Color traceCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? Color.clear
                : WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.45f);
            bool isLeft = Config.IsLeftOrientation;
            float railX = isLeft ? (-w * 0.5f + 2.5f * s) : (w * 0.5f - 2.5f * s);
            GameObject traceObj = UIFactory.CreatePanel(_trackRt, "Trace_Hairline",
                new Vector2(1.2f * s, 0f), new Vector2(railX, -h * 0.5f + 2f * s), traceCol);
            _traceRt = traceObj.GetComponent<RectTransform>();
            _traceRt.pivot = new Vector2(0.5f, 0f);
            _traceRt.anchoredPosition = new Vector2(railX, -h * 0.5f + 2f * s);
            _traceImg = traceObj.GetComponent<Image>();
        }

        private void BuildTickGraduations(float w, float h, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color majorTickCol = style.GetLineColor(LineWeight.Bold, theme);
            Color minorTickCol = style.GetLineColor(LineWeight.Subtle, theme);
            bool isLeft = Config.IsLeftOrientation;

            float railX = isLeft ? (-w * 0.5f + 2.5f * s) : (w * 0.5f - 2.5f * s);
            GameObject railObj = UIFactory.CreatePanel(_trackRt, "Backbone_Rail",
                new Vector2(1.0f * s, h - 8f * s), new Vector2(railX, 0f),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f));
            _backboneRail = railObj.GetComponent<Image>();

            for (int i = 0; i <= 8; i++)
            {
                float frac = i / 8f;
                float y = (-h * 0.5f + 4f * s) + ((h - 8f * s) * frac);
                bool isMajor = (i % 2 == 0);
                float tickLen = isMajor ? (6.0f * s) : (3.5f * s);
                float tickThick = isMajor ? (1.2f * s) : (1.0f * s);
                Color tickCol = isMajor ? majorTickCol : minorTickCol;

                float tickX = isLeft ? (railX + tickLen * 0.5f) : (railX - tickLen * 0.5f);

                GameObject tick = UIFactory.CreatePanel(_trackRt, $"Tick_{i}",
                    new Vector2(tickLen, tickThick), new Vector2(tickX, y), tickCol);
                tick.GetComponent<Image>().raycastTarget = false;
            }
        }

        private void BuildNeedlePointers(float w, float h, float s, ThemeConfig theme)
        {
            bool isLeft = Config.IsLeftOrientation;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color meterCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color cmdCol = theme.AccentSecondary.ToColor();

            _actPointerObj = UIFactory.CreatePanel(_trackRt, "Actual_Needle_Pointer",
                new Vector2(14f * s, 10f * s), Vector2.zero, Color.clear);
            _actPointerRt = _actPointerObj.GetComponent<RectTransform>();

            float stemW = 8f * s;
            float stemH = 1.4f * s;
            float stemX = isLeft ? (-stemW * 0.5f) : (stemW * 0.5f);
            GameObject actStem = UIFactory.CreatePanel(_actPointerRt, "Stem",
                new Vector2(stemW, stemH), new Vector2(stemX, 0f), meterCol);
            _actPointerStem = actStem.GetComponent<Image>();

            float headSize = 5.5f * s;
            float headX = isLeft ? (stemX + stemW * 0.5f) : (stemX - stemW * 0.5f);
            GameObject actHead = UIFactory.CreatePanel(_actPointerRt, "Head",
                new Vector2(headSize, headSize), new Vector2(headX, 0f), meterCol);
            actHead.transform.localEulerAngles = new Vector3(0f, 0f, 45f);
            _actPointerHead = actHead.GetComponent<Image>();

            if (_kind == BarGaugeKind.Throttle)
            {
                _cmdPointerObj = UIFactory.CreatePanel(_trackRt, "Command_Needle_Bug",
                    new Vector2(14f * s, 10f * s), Vector2.zero, Color.clear);
                _cmdPointerRt = _cmdPointerObj.GetComponent<RectTransform>();

                GameObject cmdStem = UIFactory.CreatePanel(_cmdPointerRt, "Cmd_Stem",
                    new Vector2(stemW, stemH), new Vector2(stemX, 0f), cmdCol);
                _cmdPointerStem = cmdStem.GetComponent<Image>();

                GameObject cmdHead = UIFactory.CreatePanel(_cmdPointerRt, "Cmd_Head",
                    new Vector2(headSize, headSize), new Vector2(headX, 0f), cmdCol);
                cmdHead.transform.localEulerAngles = new Vector3(0f, 0f, 45f);
                _cmdPointerHead = cmdHead.GetComponent<Image>();

                GameObject cmdLine = UIFactory.CreatePanel(_trackRt, "Cmd_Horizon_Line",
                    new Vector2(w - 6f * s, 1.0f * s), Vector2.zero, WidgetStyleManager.WithAlpha(cmdCol, 0.70f));
                _cmdBugLineRt = cmdLine.GetComponent<RectTransform>();
                _cmdBugLineImg = cmdLine.GetComponent<Image>();
            }
        }

        private void BuildTopTag(float w, float h, float s, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(26f * s, 21f * s);
            Vector2 tagPos = new Vector2(0f, (h * 0.5f) - (tagSize.y * 0.5f));

            _topTagBox = UIFactory.CreatePanel(transform, "Top_Tag_Box", tagSize, tagPos, theme.FrameBgColor);
            _topTagBg = _topTagBox.GetComponent<Image>();
            _topTagOutline = _topTagBox.AddComponent<Outline>();
            _topTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _topTagOutline.effectColor = theme.FrameBorderColor.ToColor();

            _topLedDot = CreateChild<Image>("Led_Dot", _topTagBox.transform, new Vector2(4f * s, 2.0f * s), new Vector2(0f, tagSize.y * 0.5f - 2f * s));
            _topLedDot.color = (_kind == BarGaugeKind.AtmosphericPressure) ? theme.AccentSecondary.ToColor() : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            _topTagTitle = UIFactory.CreateText(_topTagBox.transform, "Top_Tag_Title", _titleTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, theme));
            _topTagTitle.fontStyle = FontStyle.Bold;
            RectTransform trt = _topTagTitle.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(tagSize.x, 9f * s);
            trt.anchoredPosition = new Vector2(0f, 3.5f * s);

            _topTagValue = UIFactory.CreateText(_topTagBox.transform, "Top_Tag_Value", "0%",
                Mathf.Max(7, Mathf.RoundToInt(8.0f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _topTagValue.fontStyle = FontStyle.Normal;
            RectTransform vrt = _topTagValue.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(tagSize.x, 9f * s);
            vrt.anchoredPosition = new Vector2(0f, -4.5f * s);
            _topTagValue.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildBottomTag(float w, float h, float s, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(26f * s, 16f * s);
            Vector2 tagPos = new Vector2(0f, (-h * 0.5f) + (tagSize.y * 0.5f));

            _bottomTagBox = UIFactory.CreatePanel(transform, "Bottom_Tag_Box", tagSize, tagPos, theme.FrameBgColor);
            _bottomTagBg = _bottomTagBox.GetComponent<Image>();
            _bottomTagOutline = _bottomTagBox.AddComponent<Outline>();
            _bottomTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _bottomTagOutline.effectColor = theme.FrameBorderColor.ToColor();

            _bottomTagText = UIFactory.CreateText(_bottomTagBox.transform, "Bottom_Tag_Text", _bottomTagTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Accent, theme));
            _bottomTagText.fontStyle = FontStyle.Bold;

            RectTransform brt = _bottomTagText.GetComponent<RectTransform>();
            brt.sizeDelta = tagSize;
            brt.anchoredPosition = Vector2.zero;
            _bottomTagText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildCautionCue(float w, float h, float s, ThemeConfig theme)
        {
            double range = _maxVal - _minVal;
            float cautionFrac = range > 0.001 ? Mathf.Clamp01((float)((_cautionVal - _minVal) / range)) : 0.7f;
            float y = (-h * 0.5f) + (h * cautionFrac);

            _cautionLineObj = UIFactory.CreatePanel(_trackRt, "Caution_Cue",
                new Vector2(w + 4f * s, 1.8f * s), new Vector2(0f, y),
                WidgetStyleManager.Meter(MeterStyleRole.Warning, theme));

            _cautionLineRt = _cautionLineObj.GetComponent<RectTransform>();
            _cautionLineImg = _cautionLineObj.GetComponent<Image>();
            _cautionLineImg.raycastTarget = false;
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

            float s = CurrentDpiScale;
            float trackH = 186f * s;
            float usableH = trackH - (4f * s);
            bool isLeft = Config.IsLeftOrientation;
            float pointerX = isLeft ? (-16f * s) : (16f * s);

            if (_topTagTitle != null)
            {
                _topTagTitle.SetTextSafe(state.TitleStr);
            }

            if (state.HasCmdPointer)
            {
                float cmdY = (-usableH * 0.5f) + (usableH * state.CmdFraction);
                if (_cmdPointerRt != null) _cmdPointerRt.SetAnchoredPositionSafe(new Vector2(pointerX, cmdY));
                if (_cmdBugLineRt != null) _cmdBugLineRt.SetAnchoredPositionSafe(new Vector2(0f, cmdY));
            }

            float actY = (-usableH * 0.5f) + (usableH * state.FillFraction);
            float fillHeight = usableH * state.FillFraction;
            if (_actPointerRt != null) _actPointerRt.SetAnchoredPositionSafe(new Vector2(pointerX, actY));
            if (_fillBarRt != null) _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
            if (_traceRt != null) _traceRt.sizeDelta = new Vector2(1.2f * s, fillHeight);

            if (_topTagValue != null)
            {
                _topTagValue.SetTextSafe(state.ValueStr);
            }

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            if (_bottomTagText != null)
            {
                _bottomTagText.SetTextSafe(state.BottomStr);
                ApplyText(_bottomTagText, state.BottomRole, theme);
            }

            if (state.Role != _currentRole)
            {
                _currentRole = state.Role;
                MeterStyleRole meterRole = state.Role == CardStyleRole.Danger
                    ? MeterStyleRole.Danger
                    : (state.Role == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                Color meterCol = WidgetStyleManager.Meter(meterRole, theme);
                if (_fillBarImage != null && _kind != BarGaugeKind.Throttle && _kind != BarGaugeKind.AtmosphericPressure)
                    _fillBarImage.SetColor(meterCol);
                if (_actPointerStem != null) _actPointerStem.SetColor(meterCol);
                if (_actPointerHead != null) _actPointerHead.SetColor(meterCol);
                if (_capRayImg != null) _capRayImg.SetColor(WidgetStyleManager.WithAlpha(meterCol, 0.85f));
                if (_traceImg != null) _traceImg.SetColor(WidgetStyleManager.WithAlpha(meterCol, 0.45f));
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);

            if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                if (_atmosphereGraphic != null)
                {
                    _atmosphereGraphic.Theme = theme;
                    _atmosphereGraphic.SetVerticesDirty();
                }
                if (_trackOutline != null)
                {
                    _trackOutline.effectColor = WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.35f);
                }
            }
            else
            {
                if (_trackBg != null) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, theme);
            }

            if (_topGlossRim != null)
                _topGlossRim.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.50f);
            if (_bottomGlossRim != null)
                _bottomGlossRim.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);
            if (_backboneRail != null)
                _backboneRail.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);

            if (_topTagBg != null) ApplyCard(_topTagBg, _topTagOutline, CardStyleRole.Normal, theme);
            if (_topTagTitle != null) ApplyText(_topTagTitle, TextStyleRole.Label, theme);
            if (_topLedDot != null)
                _topLedDot.color = (_kind == BarGaugeKind.AtmosphericPressure) ? resolved.AccentSecondary.ToColor() : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            if (_bottomTagBg != null) ApplyCard(_bottomTagBg, _bottomTagOutline, CardStyleRole.Normal, theme);
            if (_bottomTagText != null) ApplyText(_bottomTagText, TextStyleRole.Accent, theme);

            MeterStyleRole meterRole = _currentRole == CardStyleRole.Danger
                ? MeterStyleRole.Danger
                : (_currentRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color meterCol = WidgetStyleManager.Meter(meterRole, theme);
            Color actCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? resolved.AccentSecondary.ToColor()
                : meterCol;

            if (_fillBarImage != null)
            {
                if (_kind == BarGaugeKind.AtmosphericPressure || _kind == BarGaugeKind.Throttle)
                    _fillBarImage.color = Color.clear;
                else
                    _fillBarImage.color = meterCol;
            }

            if (_capRayImg != null)
                _capRayImg.color = (_kind == BarGaugeKind.AtmosphericPressure)
                    ? WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.90f)
                    : WidgetStyleManager.WithAlpha(meterCol, 0.85f);

            if (_traceImg != null)
                _traceImg.color = (_kind == BarGaugeKind.AtmosphericPressure)
                    ? Color.clear
                    : WidgetStyleManager.WithAlpha(meterCol, 0.45f);

            if (_actPointerStem != null) _actPointerStem.color = actCol;
            if (_actPointerHead != null) _actPointerHead.color = actCol;

            if (_cmdPointerStem != null) _cmdPointerStem.color = resolved.AccentSecondary.ToColor();
            if (_cmdPointerHead != null) _cmdPointerHead.color = resolved.AccentSecondary.ToColor();

            if (_cmdBugLineImg != null)
                _cmdBugLineImg.color = WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.70f);

            if (_cautionLineImg != null) _cautionLineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Warning, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// UGUI GPU 矢量大气垂直多层色彩渐变网格 (零 CPU 软件光栅化，纯代码 GPU 顶点颜色插值)
    /// </summary>
    public class AtmosphereGradientGraphic : MaskableGraphic
    {
        public ThemeConfig Theme { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(Theme);

            Color vacSpace = WidgetStyleManager.Darken(resolved.FrameBgColor, 0.90f);
            vacSpace.a = 1.0f;
            Color mesoGlow = Color.Lerp(vacSpace, resolved.AccentSecondary, 0.22f);
            mesoGlow.a = 1.0f;
            Color stratIce = Color.Lerp(resolved.AccentSecondary, WidgetStyleManager.Lighten(resolved.SkyColor, 0.25f), 0.45f);
            stratIce.a = 1.0f;
            Color tropAzure = Color.Lerp(resolved.SkyColor, resolved.AccentSecondary, 0.35f);
            tropAzure.a = 1.0f;
            Color seaNavy = Color.Lerp(resolved.SkyColor, resolved.AccentSecondary, 0.12f);
            seaNavy = WidgetStyleManager.Lighten(seaNavy, 0.10f);
            seaNavy.a = 1.0f;

            float y0 = r.yMin;
            float y1 = Mathf.Lerp(r.yMin, r.yMax, 0.25f);
            float y2 = Mathf.Lerp(r.yMin, r.yMax, 0.55f);
            float y3 = Mathf.Lerp(r.yMin, r.yMax, 0.85f);
            float y4 = r.yMax;

            AddQuad(vh, r.xMin, r.xMax, y0, y1, seaNavy, tropAzure);
            AddQuad(vh, r.xMin, r.xMax, y1, y2, tropAzure, stratIce);
            AddQuad(vh, r.xMin, r.xMax, y2, y3, stratIce, mesoGlow);
            AddQuad(vh, r.xMin, r.xMax, y3, y4, mesoGlow, vacSpace);
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
    }
}
