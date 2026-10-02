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
    /// 大气压强带在真空（气压为0）环境下的交互模态
    /// </summary>
    public enum BarGaugeVacuumMode
    {
        Standard = 0,       // 始终常显 (始终保持完整 240px 标尺)
        CollapsePill = 1,   // 极简折叠 (零压时收起为微型 VAC 胶囊标牌，默认推荐)
        AutoHide = 2        // 自动完全隐形 (零压时彻底隐形)
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
        public bool IsVacuum;
        public BarGaugeVacuumMode VacuumMode;

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
                   HasCmdPointer == other.HasCmdPointer &&
                   IsVacuum == other.IsVacuum &&
                   VacuumMode == other.VacuumMode;
        }

        public override bool Equals(object obj) => obj is AvionicsBarGaugeState other && Equals(other);
        public override int GetHashCode() => (TitleStr, ValueStr, Role, IsVacuum, VacuumMode).GetHashCode();
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
        public BarGaugeVacuumMode VacuumMode = BarGaugeVacuumMode.CollapsePill;

        private float _commandedThrottle = float.NaN;
        private float _spoolThrottle = float.NaN;
        private TelemetryTokenEngine.TelemetryNumericGetter _valueGetter;
        private string _lastCompiledValueToken;
        private string _cachedTitleStr = string.Empty;
        private string _lastTitleTemplate;
        private bool _hasTitleToken;

        public override void Reset()
        {
            CurrentState = default;
            _commandedThrottle = float.NaN;
            _spoolThrottle = float.NaN;
            _valueGetter = null;
            _lastCompiledValueToken = null;
            _cachedTitleStr = string.Empty;
            _lastTitleTemplate = null;
            _hasTitleToken = false;
        }

        private void EnsureGetters()
        {
            if (_valueGetter == null || _lastCompiledValueToken != ValueToken)
            {
                _valueGetter = TelemetryTokenEngine.CompileNumeric(ValueToken);
                _lastCompiledValueToken = ValueToken;
            }
            if (_lastTitleTemplate != TitleTemplate)
            {
                _lastTitleTemplate = TitleTemplate;
                _hasTitleToken = !string.IsNullOrEmpty(TitleTemplate) && TitleTemplate.IndexOf('{') >= 0;
                if (!_hasTitleToken)
                {
                    _cachedTitleStr = TitleTemplate ?? string.Empty;
                }
            }
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

            EnsureGetters();

            string titleStr = _hasTitleToken
                ? BaseFlightWidget.EvalToken(TitleTemplate, telemetry)
                : _cachedTitleStr;

            double val = _valueGetter != null ? _valueGetter(telemetry) : 0.0;
            if (double.IsNaN(val)) val = 0.0;

            double range = MaxVal - MinVal;
            if (range <= 0.0001) range = 1.0;

            if (Kind == BarGaugeKind.Throttle)
            {
                if (MaxVal >= 50.0 && val > 0.0 && val <= 1.0 && (ValueToken.Contains(":RAW") || ValueToken.Contains(":0..1") || ValueToken.Contains(":NORM")))
                {
                    val *= 100.0;
                }

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

                float effectivePct = (MaxVal <= 1.0) ? (_spoolThrottle * 100f) : _spoolThrottle;
                float effectiveCmd = (MaxVal <= 1.0) ? (_commandedThrottle * 100f) : _commandedThrottle;
                string valueStr = CacheManager.FastPercent(Mathf.Clamp(Mathf.RoundToInt(effectivePct), 0, 100));

                string statusTag;
                if (effectivePct < 2f) statusTag = "IDLE";
                else if (Mathf.Abs(effectivePct - effectiveCmd) > 1.5f) statusTag = "SPOOL";
                else if (effectivePct >= 98f) statusTag = "MAX";
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
                    HasCmdPointer = true,
                    IsVacuum = false,
                    VacuumMode = BarGaugeVacuumMode.Standard
                };
            }
            else if (Kind == BarGaugeKind.AtmosphericPressure)
            {
                bool isVac = val <= 0.0001;

                // 真空稳态快速短路 (Vacuum Steady-State Fast Bypass)
                if (isVac && CurrentState.HasVessel && CurrentState.IsVacuum && CurrentState.VacuumMode == VacuumMode && titleStr == CurrentState.TitleStr)
                {
                    // 在真空环境下，若已处于真空稳态且标题未变，直接复用当前快照，0 字符串构建，0 计算开销
                    return;
                }

                float currentAtm = Mathf.Clamp((float)val, (float)MinVal, (float)MaxVal);
                float atmFrac = Mathf.Clamp01((currentAtm - (float)MinVal) / (float)range);

                string valueStr;
                if (val < 0.001) valueStr = "0.00 atm";
                else if (val < 0.10) valueStr = CacheManager.Instance.FastDoubleWithAffix("atm_baro", val, "", " atm", "F3", 0.001);
                else valueStr = CacheManager.Instance.FastDoubleWithAffix("atm_baro", val, "", " atm", "F2", 0.01);

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
                    HasCmdPointer = false,
                    IsVacuum = isVac,
                    VacuumMode = VacuumMode
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
                    HasCmdPointer = false,
                    IsVacuum = false,
                    VacuumMode = BarGaugeVacuumMode.Standard
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
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

        private readonly AvionicsBarGaugeLogic _logic = new AvionicsBarGaugeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private readonly Cached<string> _lastValStr = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastFillHeight = new CachedFloat(-9999f, 0.05f);

        private readonly Cached<BarGaugeKind> _kind = new Cached<BarGaugeKind>(BarGaugeKind.Throttle);
        private readonly Cached<BarGaugeVacuumMode> _vacuumMode = new Cached<BarGaugeVacuumMode>(BarGaugeVacuumMode.CollapsePill);

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

        // 极简 VAC 胶囊标牌盒 (Vacuum Collapsed Pill Box)
        private GameObject _vacuumPillBox;
        private Image _vacuumPillBg;
        private Outline _vacuumPillOutline;
        private Image _vacuumLedDot;
        private Text _vacuumPillTitle;
        private Text _vacuumPillValue;

        // 折叠与模态防抖缓存 (SPEC-009)
        private readonly Cached<bool> _lastCollapsed = new Cached<bool>(false);
        private readonly Cached<bool> _lastHidden = new Cached<bool>(false);
        private readonly Cached<bool> _lastEditMode = new Cached<bool>(false);

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
        private readonly CachedDouble _minVal = new CachedDouble(0.0, 0.001);
        private readonly CachedDouble _maxVal = new CachedDouble(100.0, 0.001);
        private readonly CachedDouble _cautionVal = new CachedDouble(0.0, 0.001);
        private readonly CachedDouble _warningVal = new CachedDouble(0.0, 0.001);

        private CardStyleRole _currentRole = CardStyleRole.Normal;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float barWidth = 22f * s;
            float barHeight = 240f * s;

            if (config != null && (config.NumericToken == "{ATM}" || (config.WidgetId == "gauge.barometer" && config.NumericToken != "{Q}") || config.WidgetId.Contains("atm")))
            {
                _kind.Value = BarGaugeKind.AtmosphericPressure;
                _valueToken = "{ATM}";
                _titleTemplate = "ATM";
                _bottomTagTemplate = "SEA";
                _minVal.Value = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal.Value = config.MaxValue > 0 ? config.MaxValue : 1.0;
            }
            else if (config != null && (config.NumericToken == "{Q}" || config.WidgetId == "gauge.q" || config.WidgetId.Contains("q")))
            {
                _kind.Value = BarGaugeKind.DynamicPressure;
                _valueToken = "{Q}";
                _titleTemplate = "Q";
                _bottomTagTemplate = "MAX Q";
                _minVal.Value = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal.Value = config.MaxValue > 0 ? config.MaxValue : 35.0;
                _cautionVal.Value = config.CautionThreshold > 0 ? config.CautionThreshold : 20.0;
                _warningVal.Value = config.WarningThreshold > 0 ? config.WarningThreshold : 28.0;
            }
            else
            {
                _kind.Value = BarGaugeKind.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THR}";
                _titleTemplate = "THR";
                _bottomTagTemplate = "IDLE";
                _minVal.Value = config != null && config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal.Value = config != null && config.MaxValue > 0 ? config.MaxValue : 100.0;
            }

            if (!string.IsNullOrEmpty(config?.DisplayName) && config.DisplayName.Length <= 4 && !config.DisplayName.Contains(I18n.Tr("SUFFIX_TAPE_CHAR", "带")))
            {
                _titleTemplate = config.DisplayName;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, _valueToken);
            _titleTemplate = GetTemplateChannel(new[] { "TITLE", "LABEL", "NAME" }, _titleTemplate);
            _bottomTagTemplate = GetTemplateChannel(new[] { "TAG", "BOTTOM", "BTM" }, _bottomTagTemplate);
            _minVal.Value = GetTemplateChannelFloat("MIN", (float)_minVal.Value);
            _maxVal.Value = GetTemplateChannelFloat("MAX", (float)_maxVal.Value);
            _cautionVal.Value = GetTemplateChannelFloat("CAUTION", (float)_cautionVal.Value);
            _warningVal.Value = GetTemplateChannelFloat("WARNING", (float)_warningVal.Value);

            if (_kind.Value == BarGaugeKind.AtmosphericPressure)
            {
                string vacChannel = GetTemplateChannel(new[] { "VAC_MODE", "VAC", "VACUUM" }, string.Empty);
                if (vacChannel.Equals("STANDARD", StringComparison.OrdinalIgnoreCase) || vacChannel.Equals("OFF", StringComparison.OrdinalIgnoreCase) || vacChannel.Equals("ALWAYS", StringComparison.OrdinalIgnoreCase) || vacChannel == "0")
                {
                    _vacuumMode.Value = BarGaugeVacuumMode.Standard;
                }
                else if (vacChannel.Equals("AUTOHIDE", StringComparison.OrdinalIgnoreCase) || vacChannel.Equals("HIDE", StringComparison.OrdinalIgnoreCase) || vacChannel == "2")
                {
                    _vacuumMode.Value = BarGaugeVacuumMode.AutoHide;
                }
                else
                {
                    _vacuumMode.Value = BarGaugeVacuumMode.CollapsePill;
                }
            }
            else
            {
                _vacuumMode.Value = BarGaugeVacuumMode.Standard;
            }

            _logic.Kind = _kind.Value;
            _logic.ValueToken = _valueToken;
            _logic.TitleTemplate = _titleTemplate;
            _logic.BottomTagTemplate = _bottomTagTemplate;
            _logic.MinVal = _minVal.Value;
            _logic.MaxVal = _maxVal.Value;
            _logic.CautionVal = _cautionVal.Value;
            _logic.WarningVal = _warningVal.Value;
            _logic.VacuumMode = _vacuumMode.Value;

            RectTransform.sizeDelta = new Vector2(barWidth, barHeight);

            float trackWidth = 24f * s;
            float trackHeight = 186f * s;
            BuildTrack(trackWidth, trackHeight, s, theme);
            BuildFillBar(trackWidth, trackHeight, s, theme);
            BuildTickGraduations(trackWidth, trackHeight, s, theme);
            BuildNeedlePointers(trackWidth, trackHeight, s, theme);
            BuildTopTag(barWidth, barHeight, s, theme);
            BuildBottomTag(barWidth, barHeight, s, theme);

            if (_kind.Value == BarGaugeKind.AtmosphericPressure)
            {
                BuildVacuumPill(s, theme);
            }

            if (_kind.Value == BarGaugeKind.DynamicPressure || _cautionVal.Value > 0)
            {
                BuildCautionCue(trackWidth, trackHeight, s, theme);
            }

            this.Controls.Register(WidgetControlManager.WrapElement(this, "track", "刻度轨道", _trackRt != null ? _trackRt.gameObject : gameObject, (t) => {
                if (_trackBg != null && _kind.Value != BarGaugeKind.AtmosphericPressure) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, t);
            }));
            if (_fillBarRt != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "fill_bar", "充填光柱", _fillBarRt.gameObject, (t) => {
                    if (_fillBarImage != null)
                    {
                        if (_kind.Value == BarGaugeKind.AtmosphericPressure || _kind.Value == BarGaugeKind.Throttle)
                            _fillBarImage.color = Color.clear;
                        else
                            _fillBarImage.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, t);
                    }
                }));
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
                var topTag = new WidgetReadoutControl("top_tag", "顶部胶囊读数", _topTagBox, _topTagValue, _topTagTitle, TextStyleRole.PrimaryValue, _valueToken);
                topTag.AutoUpdateTelemetry = false;
                this.Controls.Register(topTag);
            }
            if (_bottomTagBox != null)
            {
                var btmTag = new WidgetReadoutControl("bottom_tag", "底部档位标牌", _bottomTagBox, _bottomTagText, null, TextStyleRole.Accent, _valueToken);
                btmTag.AutoUpdateTelemetry = false;
                this.Controls.Register(btmTag);
            }
            if (_vacuumPillBox != null)
            {
                var vacPill = new WidgetReadoutControl("vacuum_pill", I18n.Tr("CTRL_BARO_VACUUM_PILL", "真空极简胶囊"), _vacuumPillBox, _vacuumPillValue, _vacuumPillTitle, TextStyleRole.Accent, "{ATM}");
                vacPill.AutoUpdateTelemetry = false;
                this.Controls.Register(vacPill);
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
            if (_kind.Value == BarGaugeKind.AtmosphericPressure)
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

            if (_kind.Value == BarGaugeKind.AtmosphericPressure || _kind.Value == BarGaugeKind.Throttle)
            {
                _fillBarImage.color = Color.clear;
            }
            else
            {
                _fillBarImage.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            Color capCol = (_kind.Value == BarGaugeKind.AtmosphericPressure)
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

            Color traceCol = (_kind.Value == BarGaugeKind.AtmosphericPressure)
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

            if (_kind.Value == BarGaugeKind.Throttle)
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
            _topLedDot.color = (_kind.Value == BarGaugeKind.AtmosphericPressure) ? theme.AccentSecondary.ToColor() : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

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

        private void BuildVacuumPill(float s, ThemeConfig theme)
        {
            Vector2 pillSize = new Vector2(26f * s, 22f * s);
            _vacuumPillBox = UIFactory.CreatePanel(transform, "Vacuum_Pill_Box", pillSize, Vector2.zero, theme.FrameBgColor);
            _vacuumPillBg = _vacuumPillBox.GetComponent<Image>();
            _vacuumPillOutline = _vacuumPillBox.AddComponent<Outline>();
            _vacuumPillOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _vacuumPillOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.45f);

            _vacuumLedDot = CreateChild<Image>("Vacuum_Led_Dot", _vacuumPillBox.transform,
                new Vector2(4f * s, 2.0f * s), new Vector2(0f, (pillSize.y * 0.5f) - 2f * s));
            _vacuumLedDot.color = theme.AccentSecondary.ToColor();

            _vacuumPillTitle = UIFactory.CreateText(_vacuumPillBox.transform, "Vacuum_Pill_Title", _titleTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, theme));
            _vacuumPillTitle.fontStyle = FontStyle.Bold;
            RectTransform trt = _vacuumPillTitle.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(pillSize.x, 9f * s);
            trt.anchoredPosition = new Vector2(0f, 3.5f * s);

            _vacuumPillValue = UIFactory.CreateText(_vacuumPillBox.transform, "Vacuum_Pill_Value", "VAC",
                Mathf.Max(7, Mathf.RoundToInt(8.0f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Accent, theme));
            _vacuumPillValue.fontStyle = FontStyle.Bold;
            RectTransform vrt = _vacuumPillValue.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(pillSize.x, 9f * s);
            vrt.anchoredPosition = new Vector2(0f, -4.5f * s);
            _vacuumPillValue.horizontalOverflow = HorizontalWrapMode.Overflow;

            _vacuumPillBox.SetActiveSafe(false);
        }

        private void BuildCautionCue(float w, float h, float s, ThemeConfig theme)
        {
            double range = _maxVal.Value - _minVal.Value;
            float cautionFrac = range > 0.001 ? Mathf.Clamp01((float)((_cautionVal.Value - _minVal.Value) / range)) : 0.7f;
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
            if (_kind.Value == BarGaugeKind.AtmosphericPressure && _logic != null && _logic.CurrentState.IsVacuum)
            {
                // 真空环境下大气压为 0，气压带折叠为极简 VAC 胶囊或处于全隐模式；
                // 巡航期间遥测心跳自适应降频至 2Hz (每 500ms 检查一次是否再入大气层)，彻底根除背景算力空转
                if (!CheckChannelElapsed("BARO_VACUUM_HEARTBEAT", 0.5f))
                {
                    return;
                }
            }
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

            bool isEdit = WidgetDragHandler.IsEditModeActive;

            // 1. 真空极速短路拦截 (Zero Overhead Steady-State Vacuum Bypass)
            if (_kind.Value == BarGaugeKind.AtmosphericPressure && state.IsVacuum && !isEdit)
            {
                if (state.VacuumMode == BarGaugeVacuumMode.AutoHide && _lastHidden.Value)
                {
                    // 自动全隐稳态：全组件隐形且已静默，完全跳过 UGUI 运算
                    return;
                }
                if (state.VacuumMode == BarGaugeVacuumMode.CollapsePill && _lastCollapsed.Value)
                {
                    // 极简折叠胶囊稳态：标牌已展开为 VAC，仅在标题字符变更时防抖同步
                    if (_lastValStr.Update(state.TitleStr) && _vacuumPillTitle != null)
                    {
                        _vacuumPillTitle.SetTextSafe(state.TitleStr);
                    }
                    return;
                }
            }

            float s = CurrentDpiScale;
            float barWidth = 22f * s;
            float barHeight = 240f * s;

            // 2. 真空状态机与折叠判定
            bool shouldCollapse = false;
            bool shouldHide = false;

            if (_kind.Value == BarGaugeKind.AtmosphericPressure && !isEdit)
            {
                if (state.IsVacuum)
                {
                    if (state.VacuumMode == BarGaugeVacuumMode.CollapsePill)
                    {
                        shouldCollapse = true;
                    }
                    else if (state.VacuumMode == BarGaugeVacuumMode.AutoHide)
                    {
                        shouldHide = true;
                    }
                }
            }

            bool collapseChanged = _lastCollapsed.Update(shouldCollapse);
            bool hideChanged = _lastHidden.Update(shouldHide);
            bool editChanged = _lastEditMode.Update(isEdit);

            if (collapseChanged || hideChanged || editChanged)
            {
                if (shouldHide)
                {
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = 0f;
                        CanvasGroup.blocksRaycasts = false;
                    }
                    if (_vacuumPillBox != null) _vacuumPillBox.SetActiveSafe(false);
                    if (_trackRt != null) _trackRt.gameObject.SetActiveSafe(false);
                    if (_topTagBox != null) _topTagBox.SetActiveSafe(false);
                    if (_bottomTagBox != null) _bottomTagBox.SetActiveSafe(false);
                    return;
                }
                else
                {
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = Opacity;
                        CanvasGroup.blocksRaycasts = true;
                    }

                    if (shouldCollapse)
                    {
                        if (_vacuumPillBox != null) _vacuumPillBox.SetActiveSafe(true);
                        if (_trackRt != null) _trackRt.gameObject.SetActiveSafe(false);
                        if (_topTagBox != null) _topTagBox.SetActiveSafe(false);
                        if (_bottomTagBox != null) _bottomTagBox.SetActiveSafe(false);
                        RectTransform.SetSizeDeltaSafe(new Vector2(26f * s, 22f * s));
                    }
                    else
                    {
                        if (_vacuumPillBox != null) _vacuumPillBox.SetActiveSafe(false);
                        if (_trackRt != null) _trackRt.gameObject.SetActiveSafe(true);
                        if (_topTagBox != null) _topTagBox.SetActiveSafe(true);
                        if (_bottomTagBox != null) _bottomTagBox.SetActiveSafe(true);
                        RectTransform.SetSizeDeltaSafe(new Vector2(barWidth, barHeight));
                    }
                }
            }

            if (shouldHide) return;

            if (shouldCollapse)
            {
                if (collapseChanged)
                {
                    if (_vacuumPillTitle != null) _vacuumPillTitle.SetTextSafe(state.TitleStr);
                    if (_vacuumPillValue != null) _vacuumPillValue.SetTextSafe("VAC");
                }
                return;
            }

            float trackH = 186f * s;
            float usableH = trackH - (4f * s);
            bool isLeft = Config != null ? Config.IsLeftOrientation : true;
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
            if (_lastFillHeight.Update(fillHeight))
            {
                if (_fillBarRt != null) _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
                if (_traceRt != null) _traceRt.sizeDelta = new Vector2(1.2f * s, fillHeight);
            }

            if (_lastValStr.Update(state.ValueStr) && _topTagValue != null)
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
                if (_fillBarImage != null && _kind.Value != BarGaugeKind.Throttle && _kind.Value != BarGaugeKind.AtmosphericPressure)
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

            if (_kind.Value == BarGaugeKind.AtmosphericPressure)
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
                _topLedDot.color = (_kind.Value == BarGaugeKind.AtmosphericPressure) ? resolved.AccentSecondary.ToColor() : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            if (_bottomTagBg != null) ApplyCard(_bottomTagBg, _bottomTagOutline, CardStyleRole.Normal, theme);
            if (_bottomTagText != null) ApplyText(_bottomTagText, TextStyleRole.Accent, theme);

            if (_vacuumPillBg != null) ApplyCard(_vacuumPillBg, _vacuumPillOutline, CardStyleRole.Normal, theme);
            if (_vacuumPillOutline != null)
                _vacuumPillOutline.effectColor = WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.45f);
            if (_vacuumLedDot != null)
                _vacuumLedDot.color = resolved.AccentSecondary.ToColor();
            if (_vacuumPillTitle != null)
                ApplyText(_vacuumPillTitle, TextStyleRole.Label, theme);
            if (_vacuumPillValue != null)
                ApplyText(_vacuumPillValue, TextStyleRole.Accent, theme);

            MeterStyleRole meterRole = _currentRole == CardStyleRole.Danger
                ? MeterStyleRole.Danger
                : (_currentRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color meterCol = WidgetStyleManager.Meter(meterRole, theme);
            Color actCol = (_kind.Value == BarGaugeKind.AtmosphericPressure)
                ? resolved.AccentSecondary.ToColor()
                : meterCol;

            if (_fillBarImage != null)
            {
                if (_kind.Value == BarGaugeKind.AtmosphericPressure || _kind.Value == BarGaugeKind.Throttle)
                    _fillBarImage.color = Color.clear;
                else
                    _fillBarImage.color = meterCol;
            }

            if (_capRayImg != null)
                _capRayImg.color = (_kind.Value == BarGaugeKind.AtmosphericPressure)
                    ? WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.90f)
                    : WidgetStyleManager.WithAlpha(meterCol, 0.85f);

            if (_traceImg != null)
                _traceImg.color = (_kind.Value == BarGaugeKind.AtmosphericPressure)
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

        public override void PopulateContextMenu(Action<string, Action> registerAction)
        {
            base.PopulateContextMenu(registerAction);
            if (_kind.Value == BarGaugeKind.AtmosphericPressure)
            {
                string label;
                switch (_vacuumMode.Value)
                {
                    case BarGaugeVacuumMode.CollapsePill:
                        label = I18n.Tr("CTX_BARO_VAC_PILL", "⚡ 真空模式: 【极简折叠】 (点击切换)");
                        break;
                    case BarGaugeVacuumMode.AutoHide:
                        label = I18n.Tr("CTX_BARO_VAC_HIDE", "⚡ 真空模式: 【自动全隐】 (点击切换)");
                        break;
                    default:
                        label = I18n.Tr("CTX_BARO_VAC_STD", "⚡ 真空模式: 【始终常显】 (点击切换)");
                        break;
                }
                registerAction?.Invoke(label, CycleVacuumMode);
            }
        }

        private void CycleVacuumMode()
        {
            switch (_vacuumMode.Value)
            {
                case BarGaugeVacuumMode.CollapsePill:
                    _vacuumMode.Value = BarGaugeVacuumMode.AutoHide;
                    break;
                case BarGaugeVacuumMode.AutoHide:
                    _vacuumMode.Value = BarGaugeVacuumMode.Standard;
                    break;
                default:
                    _vacuumMode.Value = BarGaugeVacuumMode.CollapsePill;
                    break;
            }
            _logic.VacuumMode = _vacuumMode.Value;
            if (Config != null)
            {
                string modeStr = ((int)_vacuumMode.Value).ToString();
                SetCustomTemplateChannel("VAC_MODE", modeStr);
                WidgetLayoutManager.Instance?.SaveLayout();
            }
            string tip;
            switch (_vacuumMode.Value)
            {
                case BarGaugeVacuumMode.CollapsePill:
                    tip = I18n.Tr("TIP_BARO_VAC_PILL", "已切换至真空极简折叠模式 (气压为0时收起为微型VAC标牌)");
                    break;
                case BarGaugeVacuumMode.AutoHide:
                    tip = I18n.Tr("TIP_BARO_VAC_HIDE", "已切换至真空自动全隐模式 (气压为0时自动隐形)");
                    break;
                default:
                    tip = I18n.Tr("TIP_BARO_VAC_STD", "已切换至真空始终常显模式 (始终保持完整240px光柱标尺)");
                    break;
            }
            MFPToastBridge.Show(tip);
            _lastCollapsed.Reset(!_lastCollapsed.Value);
            _lastHidden.Reset(!_lastHidden.Value);
        }

        private void SetCustomTemplateChannel(string key, string value)
        {
            if (Config == null) return;
            string raw = Config.CustomTemplate ?? string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            bool updated = false;

            if (!string.IsNullOrEmpty(raw))
            {
                string[] pairs = raw.Split(';');
                foreach (var p in pairs)
                {
                    int eq = p.IndexOf('=');
                    if (eq > 0)
                    {
                        string k = p.Substring(0, eq).Trim();
                        if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add($"{key}={value}");
                            updated = true;
                            continue;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(p)) parts.Add(p);
                }
            }

            if (!updated)
            {
                parts.Add($"{key}={value}");
            }

            Config.CustomTemplate = string.Join(";", parts.ToArray());
            InvalidateTemplateChannels();
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastValStr.Reset(string.Empty);
            _lastFillHeight.Reset(-9999f);
            _lastCollapsed.Reset(false);
            _lastHidden.Reset(false);
            _lastEditMode.Reset(false);
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
