using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 标准航电标题栏控件 (Header Control: 标题 + 副标题 + 状态胶囊 + 装饰分割线)
    /// </summary>
    public class WidgetHeaderControl : BaseWidgetControl
    {
        public Text TitleText { get; private set; }
        public Text SubtitleText { get; private set; }
        public Text StatusBadgeText { get; private set; }
        public Image DividerLine { get; private set; }

        public string TitleTemplate { get; set; }
        public string SubtitleTemplate { get; set; }
        public string StatusBadgeTemplate { get; set; }

        private string _lastTitle = null;
        private string _lastSubtitle = null;
        private string _lastBadge = null;

        public WidgetHeaderControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Text title, Text subtitle, Text badge, Image divider, string defaultTitle, string defaultSubtitle = "", string defaultBadge = "")
            : base(parent, id, displayName, WidgetControlCategory.Header, rootGo)
        {
            TitleText = title;
            SubtitleText = subtitle;
            StatusBadgeText = badge;
            DividerLine = divider;
            TitleTemplate = defaultTitle ?? "";
            SubtitleTemplate = defaultSubtitle ?? "";
            StatusBadgeTemplate = defaultBadge ?? "";
        }

        public WidgetHeaderControl(string id, string displayName, GameObject rootGo, Text title, Text badge = null)
            : base(null, id, displayName, WidgetControlCategory.Header, rootGo)
        {
            TitleText = title;
            StatusBadgeText = badge;
        }

        public WidgetHeaderControl(Text title, Text badge, string displayName = "Header", string description = "")
            : base(null, (title != null ? title.name : "header"), displayName, WidgetControlCategory.Header, title != null ? title.gameObject : null)
        {
            TitleText = title;
            StatusBadgeText = badge;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (TitleText != null) style.ApplyTextStyle(TitleText, TextStyleRole.Label, theme);
            if (SubtitleText != null) style.ApplyTextStyle(SubtitleText, TextStyleRole.SecondaryValue, theme);
            if (StatusBadgeText != null) style.ApplyTextStyle(StatusBadgeText, TextStyleRole.Accent, theme);
            if (DividerLine != null) DividerLine.color = style.GetLineColor(LineWeight.Faint, theme);
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null) return;

            if (TitleText != null && !string.IsNullOrEmpty(TitleTemplate))
            {
                string eval = TelemetryTokenEngine.Evaluate(TitleTemplate, telemetry);
                if (eval != _lastTitle) { _lastTitle = eval; BaseFlightWidget.SetTextIfChanged(TitleText, eval); }
            }

            if (SubtitleText != null && !string.IsNullOrEmpty(SubtitleTemplate))
            {
                string eval = TelemetryTokenEngine.Evaluate(SubtitleTemplate, telemetry);
                if (eval != _lastSubtitle) { _lastSubtitle = eval; BaseFlightWidget.SetTextIfChanged(SubtitleText, eval); }
            }

            if (StatusBadgeText != null && !string.IsNullOrEmpty(StatusBadgeTemplate))
            {
                string eval = TelemetryTokenEngine.Evaluate(StatusBadgeTemplate, telemetry);
                if (eval != _lastBadge) { _lastBadge = eval; BaseFlightWidget.SetTextIfChanged(StatusBadgeText, eval); }
            }
        }

        public override bool NeedsTelemetryUpdate =>
            (!string.IsNullOrEmpty(TitleTemplate) && TitleTemplate.IndexOf('{') >= 0) ||
            (!string.IsNullOrEmpty(SubtitleTemplate) && SubtitleTemplate.IndexOf('{') >= 0) ||
            (!string.IsNullOrEmpty(StatusBadgeTemplate) && StatusBadgeTemplate.IndexOf('{') >= 0);

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null && ParentWidget != null)
            {
                string customTitle = ParentWidget.GetTemplateChannel("TITLE", "");
                if (!string.IsNullOrEmpty(customTitle)) TitleTemplate = customTitle;

                string customSub = ParentWidget.GetTemplateChannel("SUBTITLE", "");
                if (!string.IsNullOrEmpty(customSub)) SubtitleTemplate = customSub;

                string customBadge = ParentWidget.GetTemplateChannel("BADGE", "");
                if (!string.IsNullOrEmpty(customBadge)) StatusBadgeTemplate = customBadge;
            }
        }
    }

    /// <summary>
    /// 标准数显读数盒控件 (Value Readout Box Control: 标签 + 等宽防抖数显 + 单位角标 + 嵌槽底衬)
    /// </summary>
    public class WidgetReadoutControl : BaseWidgetControl
    {
        public Image BackgroundImage { get; private set; }
        public Outline BorderOutline { get; private set; }
        public Text TitleLabel { get; private set; }
        public Text ValueText { get; private set; }
        public Text UnitLabel { get; private set; }

        public string Token { get; set; }
        public string Title { get; set; }
        public string Unit { get; set; }
        public string Fallback { get; set; } = "---";

        private string _lastRawValue = null;
        private string _lastUnit = null;
        private string _lastTitle = null;

        public TextStyleRole StyleRole { get; set; } = TextStyleRole.PrimaryValue;

        public WidgetReadoutControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Image bg, Outline outline, Text title, Text value, Text unit, string token, string titleStr = "", string unitStr = "")
            : base(parent, id, displayName, WidgetControlCategory.Readout, rootGo)
        {
            BackgroundImage = bg;
            BorderOutline = outline;
            TitleLabel = title;
            ValueText = value;
            UnitLabel = unit;
            Token = token ?? "{SPD}";
            Title = titleStr ?? "";
            Unit = unitStr ?? "";
        }

        public WidgetReadoutControl(string id, string displayName, GameObject rootGo, Text value, Text unit = null, TextStyleRole role = TextStyleRole.PrimaryValue)
            : base(null, id, displayName, WidgetControlCategory.Readout, rootGo)
        {
            ValueText = value;
            UnitLabel = unit;
            StyleRole = role;
        }

        public WidgetReadoutControl(Text value, Text label, TextStyleRole role, string displayName = "Readout", string description = "")
            : base(null, (value != null ? value.name : "readout"), displayName, WidgetControlCategory.Readout, value != null ? value.gameObject : null)
        {
            ValueText = value;
            TitleLabel = label;
            StyleRole = role;
        }

        public WidgetReadoutControl(string id, string displayName, GameObject rootGo, Text value, Text label, TextStyleRole role, string token = null)
            : base(null, id, displayName, WidgetControlCategory.Readout, rootGo)
        {
            ValueText = value;
            TitleLabel = label;
            StyleRole = role;
            Token = token;
        }

        public TextStyleRole LabelRole { get; set; } = TextStyleRole.SecondaryValue;
        public TextStyleRole UnitRole { get; set; } = TextStyleRole.Unit;

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (BackgroundImage != null)
            {
                BackgroundImage.material = style.GetUiMaterial(isText: false);
                BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (BorderOutline != null) BorderOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (TitleLabel != null) style.ApplyTextStyle(TitleLabel, LabelRole, theme);
            if (ValueText != null) style.ApplyTextStyle(ValueText, StyleRole, theme);
            if (UnitLabel != null) style.ApplyTextStyle(UnitLabel, UnitRole, theme);
        }

        private double _lastNumericValue = double.NaN;

        public void SetRole(TextStyleRole role)
        {
            if (StyleRole == role) return;
            StyleRole = role;
            if (ValueText != null)
            {
                WidgetStyleManager.Instance.ApplyTextStyle(ValueText, role, null);
            }
        }

        public void SetValue(string val, string unit = null)
        {
            if (ValueText != null && val != _lastRawValue)
            {
                _lastRawValue = val;
                string displayStr = (unit != null && UnitLabel == null) ? (val + unit) : val;
                BaseFlightWidget.SetTextIfChanged(ValueText, UIFactory.FormatTabular(displayStr ?? Fallback));
            }
            if (unit != null && UnitLabel != null && unit != _lastUnit)
            {
                _lastUnit = unit;
                BaseFlightWidget.SetTextIfChanged(UnitLabel, unit);
            }
        }

        public void SetFormattedValue(double val, string format = "F1", string unit = null)
        {
            string str = double.IsNaN(val) ? Fallback : CacheManager.Instance.FastDouble(Id ?? "readout", val, format, 0.05);
            SetValue(str, unit);
        }

        public bool SetNumeric(double val, string format = "F1", double deadband = 0.05, string unit = null)
        {
            if (!double.IsNaN(_lastNumericValue) && !double.IsNaN(val) && Math.Abs(val - _lastNumericValue) < deadband)
                return false;

            _lastNumericValue = val;
            SetFormattedValue(val, format, unit);
            return true;
        }

        public bool SetMetricDistance(double meters, double deadband = 1.0, string unit = null)
        {
            if (!double.IsNaN(_lastNumericValue) && !double.IsNaN(meters) && Math.Abs(meters - _lastNumericValue) < deadband)
                return false;

            _lastNumericValue = meters;
            SetValue(BaseFlightWidget.FormatMetricDistance(meters), unit);
            return true;
        }

        public bool SetMetricSpeed(double mps, double deadband = 0.5, string unit = null)
        {
            if (!double.IsNaN(_lastNumericValue) && !double.IsNaN(mps) && Math.Abs(mps - _lastNumericValue) < deadband)
                return false;

            _lastNumericValue = mps;
            SetValue(BaseFlightWidget.FormatMetricSpeed(mps), unit);
            return true;
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null) return;

            if (TitleLabel != null && Title != _lastTitle)
            {
                _lastTitle = Title;
                BaseFlightWidget.SetTextIfChanged(TitleLabel, Title);
            }

            if (ValueText != null && !string.IsNullOrEmpty(Token))
            {
                string eval = TelemetryTokenEngine.Evaluate(Token, telemetry);
                if (string.IsNullOrEmpty(eval)) eval = Fallback;
                if (eval != _lastRawValue)
                {
                    _lastRawValue = eval;
                    BaseFlightWidget.SetTextIfChanged(ValueText, UIFactory.FormatTabular(eval));
                }
            }

            if (UnitLabel != null && Unit != _lastUnit)
            {
                _lastUnit = Unit;
                BaseFlightWidget.SetTextIfChanged(UnitLabel, Unit);
            }
        }

        public override bool NeedsTelemetryUpdate => !string.IsNullOrEmpty(Token);

        public override bool HasTelemetryBinding => true;
        public override string TelemetryToken
        {
            get => Token;
            set => Token = value;
        }
        public override string TelemetryUnit
        {
            get => Unit;
            set => Unit = value;
        }
        public override bool SupportsRange => false;

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null && ParentWidget != null)
            {
                string customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TOKEN", "");
                if (string.IsNullOrEmpty(customToken)) customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TPL", "");
                if (string.IsNullOrEmpty(customToken)) customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant(), "");
                if (!string.IsNullOrEmpty(customToken)) Token = customToken;

                string customUnit = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_UNIT", "");
                if (!string.IsNullOrEmpty(customUnit)) Unit = customUnit;

                string customTitle = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TITLE", "");
                if (!string.IsNullOrEmpty(customTitle)) Title = customTitle;
            }
        }
    }

    /// <summary>
    /// 标准线性柱状仪表控件 (Linear Bar Gauge Control: 标尺槽底色 + 动态填充柱条 + 警戒阈值着色)
    /// </summary>
    public class WidgetLinearBarControl : BaseWidgetControl
    {
        public Image TrackImage { get; private set; }
        public Image FillImage { get; private set; }
        public RectTransform FillRectTransform => FillImage != null ? FillImage.rectTransform : null;

        public string NumericToken { get; set; }
        public override bool HasTelemetryBinding => true;
        public override string TelemetryToken
        {
            get => NumericToken;
            set => NumericToken = value;
        }
        public override bool SupportsRange => true;
        public override double MinValue { get; set; } = 0.0;
        public override double MaxValue { get; set; } = 100.0;
        public double CautionThreshold { get; set; } = 80.0;
        public double WarningThreshold { get; set; } = 95.0;
        public bool IsVertical { get; set; } = false;
        public float MaxSpanLength { get; set; } = 100f;

        private double _lastValue = double.NaN;
        private int _lastAlertState = -1;

        public MeterStyleRole MeterRole { get; set; } = MeterStyleRole.Primary;

        public WidgetLinearBarControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Image track, Image fill, string token, double min, double max, float spanLength, bool isVertical = false)
            : base(parent, id, displayName, WidgetControlCategory.LinearGauge, rootGo)
        {
            TrackImage = track;
            FillImage = fill;
            NumericToken = token ?? "{THR}";
            MinValue = min;
            MaxValue = max > min ? max : min + 1.0;
            MaxSpanLength = spanLength;
            IsVertical = isVertical;
        }

        public WidgetLinearBarControl(string id, string displayName, GameObject rootGo, Image fill, Image track = null, MeterStyleRole role = MeterStyleRole.Primary, bool isVertical = false)
            : base(null, id, displayName, WidgetControlCategory.LinearGauge, rootGo)
        {
            FillImage = fill;
            TrackImage = track;
            MeterRole = role;
            IsVertical = isVertical;
        }

        public WidgetLinearBarControl(Image fill, Image track, MeterStyleRole role = MeterStyleRole.Primary, bool isVertical = false, string displayName = "Bar Gauge", string description = "")
            : base(null, (fill != null ? fill.name : "bar_gauge"), displayName, WidgetControlCategory.LinearGauge, fill != null ? fill.gameObject : null)
        {
            FillImage = fill;
            TrackImage = track;
            MeterRole = role;
            IsVertical = isVertical;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (TrackImage != null)
            {
                TrackImage.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }
            UpdateBarColor(theme);
        }

        public void SetFillAmount(float ratio, MeterStyleRole role = MeterStyleRole.Primary)
        {
            if (FillImage == null) return;
            ratio = Mathf.Clamp01(ratio);
            if (FillImage.type == Image.Type.Filled)
            {
                FillImage.fillAmount = ratio;
            }
            else if (FillRectTransform != null)
            {
                float s = ParentWidget != null ? ParentWidget.CurrentDpiScale : 1.0f;
                float curLen = MaxSpanLength * s * ratio;
                if (IsVertical)
                    FillRectTransform.sizeDelta = new Vector2(FillRectTransform.sizeDelta.x, curLen);
                else
                    FillRectTransform.sizeDelta = new Vector2(curLen, FillRectTransform.sizeDelta.y);
            }
            MeterRole = role;
            ThemeConfig currentTheme = WidgetStyleManager.Instance.CurrentTheme;
            if (currentTheme != null)
            {
                WidgetStyleManager.Instance.ApplyMeterStyle(null, FillImage, null, role, currentTheme);
            }
        }

        private void UpdateBarColor(ThemeConfig theme)
        {
            if (FillImage == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (_lastAlertState == 2)
            {
                FillImage.color = style.GetMeterColor(MeterStyleRole.Danger, theme);
            }
            else if (_lastAlertState == 1)
            {
                FillImage.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            }
            else
            {
                FillImage.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null || FillImage == null) return;

            double val = TelemetryTokenEngine.EvaluateNumeric(NumericToken, telemetry);
            if (double.IsNaN(val)) val = MinValue;

            double delta = Math.Abs(val - _lastValue);
            if (double.IsNaN(_lastValue) || delta > 0.001)
            {
                _lastValue = val;
                double denom = MaxValue - MinValue;
                float frac = Mathf.Clamp01((float)((val - MinValue) / (denom > 0.0001 ? denom : 1.0)));

                float s = ParentWidget != null ? ParentWidget.CurrentDpiScale : 1.0f;
                float curLen = MaxSpanLength * s * frac;

                if (IsVertical)
                {
                    FillRectTransform.sizeDelta = new Vector2(FillRectTransform.sizeDelta.x, curLen);
                }
                else
                {
                    FillRectTransform.sizeDelta = new Vector2(curLen, FillRectTransform.sizeDelta.y);
                }

                int alert = 0;
                if (val >= WarningThreshold) alert = 2;
                else if (val >= CautionThreshold) alert = 1;

                if (alert != _lastAlertState)
                {
                    _lastAlertState = alert;
                    UpdateBarColor(ThemeManager.Instance?.CurrentTheme);
                }
            }
        }

        public override bool NeedsTelemetryUpdate => !string.IsNullOrEmpty(NumericToken);

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null)
            {
                if (config.MaxValue > config.MinValue)
                {
                    MinValue = config.MinValue;
                    MaxValue = config.MaxValue;
                    CautionThreshold = config.CautionThreshold;
                    WarningThreshold = config.WarningThreshold;
                }
                if (ParentWidget != null)
                {
                    string customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TOKEN", "");
                    if (!string.IsNullOrEmpty(customToken)) NumericToken = customToken;

                    string minStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MIN", "");
                    if (double.TryParse(minStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMin))
                        MinValue = cMin;

                    string maxStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MAX", "");
                    if (double.TryParse(maxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMax))
                        MaxValue = cMax;
                }
            }
        }
    }

    /// <summary>
    /// 标准机载交互按键控件 (Action / Toggle Button Control)
    /// </summary>
    public class WidgetActionButtonControl : BaseWidgetControl
    {
        public Button Button { get; private set; }
        public Image BackgroundImage { get; private set; }
        public Outline BorderOutline { get; private set; }
        public Text LabelText { get; private set; }
        public Image ActiveLed { get; private set; }
        public AvionicsButtonFeedback Feedback { get; private set; }

        public Action OnClick { get; set; }
        public string Label { get; set; }
        public bool IsToggle { get; set; } = false;

        private bool _isActive = false;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                if (Feedback != null) Feedback.SetToggleActive(value);
            }
        }

        public ButtonVisualRole VisualRole { get; set; } = ButtonVisualRole.Normal;

        public WidgetActionButtonControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Button btn, Image bg, Outline outline, Text label, Image led, string labelStr, Action onClick, bool isToggle = false)
            : base(parent, id, displayName, WidgetControlCategory.ActionButton, rootGo)
        {
            Button = btn;
            BackgroundImage = bg;
            BorderOutline = outline;
            LabelText = label;
            ActiveLed = led;
            Label = labelStr ?? "";
            OnClick = onClick;
            IsToggle = isToggle;

            if (Button != null && OnClick != null)
            {
                Button.onClick.RemoveAllListeners();
                Button.onClick.AddListener(() => OnClick?.Invoke());
            }

            if (rootGo != null)
            {
                Feedback = rootGo.GetComponent<AvionicsButtonFeedback>() ?? rootGo.AddComponent<AvionicsButtonFeedback>();
            }
        }

        public WidgetActionButtonControl(string id, string displayName, GameObject rootGo, Button btn, Text label = null, Image icon = null, ButtonVisualRole role = ButtonVisualRole.Normal, Action onClick = null)
            : base(null, id, displayName, WidgetControlCategory.ActionButton, rootGo)
        {
            Button = btn;
            LabelText = label;
            ActiveLed = icon;
            VisualRole = role;
            OnClick = onClick;
            if (Button != null && OnClick != null)
            {
                Button.onClick.RemoveAllListeners();
                Button.onClick.AddListener(() => OnClick?.Invoke());
            }
        }

        public WidgetActionButtonControl(Button btn, Text label, Image icon, ButtonVisualRole role, string displayName = "Action Button", string description = "", Action onClick = null)
            : base(null, (btn != null ? btn.name : "action_btn"), displayName, WidgetControlCategory.ActionButton, btn != null ? btn.gameObject : null)
        {
            Button = btn;
            LabelText = label;
            ActiveLed = icon;
            VisualRole = role;
            OnClick = onClick;
            if (Button != null && OnClick != null)
            {
                Button.onClick.RemoveAllListeners();
                Button.onClick.AddListener(() => OnClick?.Invoke());
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (Button != null)
            {
                style.ApplyButtonStyle(Button, BackgroundImage, LabelText, VisualRole, false, theme);
            }
            else
            {
                if (BackgroundImage != null) BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (BorderOutline != null) BorderOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (LabelText != null) style.ApplyTextStyle(LabelText, TextStyleRole.SecondaryValue, theme);
            }
            if (ActiveLed != null) ActiveLed.color = style.GetTextColor(TextStyleRole.Accent, theme);
            if (Feedback != null) Feedback.ApplyTheme(theme);
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (LabelText != null && LabelText.text != Label)
            {
                LabelText.text = Label;
            }
        }

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null && ParentWidget != null)
            {
                string customLabel = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_LABEL", "");
                if (!string.IsNullOrEmpty(customLabel)) Label = customLabel;
            }
        }
    }

    /// <summary>
    /// 标准状态光字牌与指示灯控件 (Annunciator / Status Lamp Control)
    /// </summary>
    public class WidgetAnnunciatorControl : BaseWidgetControl
    {
        public Image LampBg { get; private set; }
        public Outline LampOutline { get; private set; }
        public Text LampLabel { get; private set; }

        public string Label { get; set; }
        public AnnunciatorState State { get; set; } = AnnunciatorState.Off;
        public bool Blinking { get; set; } = false;

        public Text SubLabel { get; private set; }

        public WidgetAnnunciatorControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Image bg, Outline outline, Text label, string labelStr)
            : base(parent, id, displayName, WidgetControlCategory.Annunciator, rootGo)
        {
            LampBg = bg;
            LampOutline = outline;
            LampLabel = label;
            Label = labelStr ?? "";
        }

        public WidgetAnnunciatorControl(string id, string displayName, GameObject rootGo, Text title, Text sub = null, Image bg = null, Outline outline = null)
            : base(null, id, displayName, WidgetControlCategory.Annunciator, rootGo)
        {
            LampLabel = title;
            SubLabel = sub;
            LampBg = bg;
            LampOutline = outline;
        }

        public WidgetAnnunciatorControl(Text title, Text sub, Image bg, Outline outline, string displayName = "Annunciator", string description = "")
            : base(null, (title != null ? title.name : "annunciator"), displayName, WidgetControlCategory.Annunciator, title != null ? title.gameObject : null)
        {
            LampLabel = title;
            SubLabel = sub;
            LampBg = bg;
            LampOutline = outline;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (LampBg != null) LampBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (LampOutline != null) LampOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (LampLabel != null) style.ApplyTextStyle(LampLabel, TextStyleRole.Label, theme);
            if (SubLabel != null) style.ApplyTextStyle(SubLabel, TextStyleRole.Accent, theme);
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!string.IsNullOrEmpty(Label) && LampLabel != null && LampLabel.text != Label)
            {
                LampLabel.text = Label;
            }
        }
    }

    /// <summary>
    /// 标准极坐标弧形表盘控件 (Arc / Radial Meter Control)
    /// </summary>
    public class WidgetArcMeterControl : BaseWidgetControl
    {
        public Image MeterImage { get; private set; }
        public Material MeterMaterial { get; private set; }
        public string NumericToken { get; set; }
        public override bool HasTelemetryBinding => true;
        public override string TelemetryToken
        {
            get => NumericToken;
            set => NumericToken = value;
        }
        public override bool SupportsRange => true;
        public override double MinValue { get; set; } = 0.0;
        public override double MaxValue { get; set; } = 100.0;

        private double _lastValue = double.NaN;

        public WidgetArcMeterControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Image meterImg, Material mat, string token, double min, double max)
            : base(parent, id, displayName, WidgetControlCategory.ArcGauge, rootGo)
        {
            MeterImage = meterImg;
            MeterMaterial = mat;
            NumericToken = token ?? "{SPD}";
            MinValue = min;
            MaxValue = max > min ? max : min + 1.0;
        }

        public WidgetArcMeterControl(Image fill, Image track, MeterStyleRole role = MeterStyleRole.Primary, string displayName = "Arc Meter", string description = "")
            : base(null, (fill != null ? fill.name : "arc_meter"), displayName, WidgetControlCategory.ArcGauge, fill != null ? fill.gameObject : null)
        {
            MeterImage = fill;
        }

        public WidgetArcMeterControl(string id, string displayName, GameObject rootGo, Image fill, Image track, MeterStyleRole role = MeterStyleRole.Primary)
            : base(null, id, displayName, WidgetControlCategory.ArcGauge, rootGo)
        {
            MeterImage = fill;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (MeterMaterial != null)
            {
                MeterMaterial.SetColor("_TrackColor", WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Track, theme));
                MeterMaterial.SetColor("_FillColor", WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Primary, theme));
                MeterMaterial.SetColor("_WarningColor", WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Warning, theme));
                MeterMaterial.SetColor("_DangerColor", WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Danger, theme));
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null || MeterMaterial == null) return;
            double val = TelemetryTokenEngine.EvaluateNumeric(NumericToken, telemetry);
            if (double.IsNaN(val)) val = MinValue;

            if (double.IsNaN(_lastValue) || Math.Abs(val - _lastValue) > 0.001)
            {
                _lastValue = val;
                float frac = Mathf.Clamp01((float)((val - MinValue) / (MaxValue - MinValue)));
                MeterMaterial.SetFloat("_FillAmount", frac);
            }
        }

        public override bool NeedsTelemetryUpdate => !string.IsNullOrEmpty(NumericToken);

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null && ParentWidget != null)
            {
                string customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TOKEN", "");
                if (!string.IsNullOrEmpty(customToken)) NumericToken = customToken;

                string minStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MIN", "");
                if (double.TryParse(minStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMin))
                    MinValue = cMin;

                string maxStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MAX", "");
                if (double.TryParse(maxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMax))
                    MaxValue = cMax;
            }
        }
    }

    /// <summary>
    /// 标准动态旋转指针控件 (Needle Pointer Control)
    /// </summary>
    public class WidgetNeedleControl : BaseWidgetControl
    {
        public RectTransform PivotRt { get; private set; }
        public Image NeedleImage { get; private set; }
        public string NumericToken { get; set; }
        public override bool HasTelemetryBinding => true;
        public override string TelemetryToken
        {
            get => NumericToken;
            set => NumericToken = value;
        }
        public override bool SupportsRange => true;
        public float StartAngle { get; set; } = 225f;
        public float EndAngle { get; set; } = -45f;
        public override double MinValue { get; set; } = 0.0;
        public override double MaxValue { get; set; } = 100.0;

        private double _lastValue = double.NaN;

        public WidgetNeedleControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            RectTransform pivot, Image needle, string token, float startAngle, float endAngle, double min, double max)
            : base(parent, id, displayName, WidgetControlCategory.NeedlePointer, rootGo)
        {
            PivotRt = pivot;
            NeedleImage = needle;
            NumericToken = token ?? "{SPD}";
            StartAngle = startAngle;
            EndAngle = endAngle;
            MinValue = min;
            MaxValue = max > min ? max : min + 1.0;
        }

        public WidgetNeedleControl(string id, string displayName, GameObject rootGo, RectTransform pivot = null, Image needle = null, Image bg = null)
            : base(null, id, displayName, WidgetControlCategory.NeedlePointer, rootGo)
        {
            PivotRt = pivot;
            NeedleImage = needle;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (NeedleImage != null)
            {
                NeedleImage.color = WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Accent, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null || PivotRt == null) return;
            double val = TelemetryTokenEngine.EvaluateNumeric(NumericToken, telemetry);
            if (double.IsNaN(val)) val = MinValue;

            if (double.IsNaN(_lastValue) || Math.Abs(val - _lastValue) > 0.001)
            {
                _lastValue = val;
                float frac = Mathf.Clamp01((float)((val - MinValue) / (MaxValue - MinValue)));
                float angle = Mathf.Lerp(StartAngle, EndAngle, frac);
                BaseFlightWidget.SetLocalRotationIfChanged(PivotRt, Quaternion.Euler(0f, 0f, angle), 0.05f);
            }
        }

        public override bool NeedsTelemetryUpdate => !string.IsNullOrEmpty(NumericToken);

        public override void BindConfig(WidgetConfig config)
        {
            base.BindConfig(config);
            if (config != null && ParentWidget != null)
            {
                string customToken = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_TOKEN", "");
                if (!string.IsNullOrEmpty(customToken)) NumericToken = customToken;

                string minStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MIN", "");
                if (double.TryParse(minStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMin))
                    MinValue = cMin;

                string maxStr = ParentWidget.GetTemplateChannel(Id.ToUpperInvariant() + "_MAX", "");
                if (double.TryParse(maxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cMax))
                    MaxValue = cMax;
            }
        }
    }

    /// <summary>
    /// 标准图形视口控件 (Graphic / Viewport Control: 2D剪影、雷达光环、视口纹理)
    /// </summary>
    public class WidgetGraphicViewportControl : BaseWidgetControl
    {
        public RawImage ViewportRawImage { get; private set; }
        public Image BorderImage { get; private set; }

        public WidgetGraphicViewportControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            RawImage rawImage, Image border)
            : base(parent, id, displayName, WidgetControlCategory.Viewport, rootGo)
        {
            ViewportRawImage = rawImage;
            BorderImage = border;
        }

        public WidgetGraphicViewportControl(string id, string displayName, GameObject rootGo, RawImage rawImage = null, Image border = null)
            : base(null, id, displayName, WidgetControlCategory.Viewport, rootGo)
        {
            ViewportRawImage = rawImage;
            BorderImage = border;
        }

        public WidgetGraphicViewportControl(RawImage rawImage, string displayName = "Viewport", string description = "")
            : base(null, (rawImage != null ? rawImage.name : "viewport"), displayName, WidgetControlCategory.Viewport, rawImage != null ? rawImage.gameObject : null)
        {
            ViewportRawImage = rawImage;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (BorderImage != null)
            {
                BorderImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
        }
    }

    /// <summary>
    /// 通用命名子元素包装控件 (Generic Named Sub-Element Control: 自动接入遮罩与显隐管理)
    /// </summary>
    public class WidgetGenericSubElementControl : BaseWidgetControl
    {
        public Action<ThemeConfig> OnApplyThemeAction { get; set; }
        public Action<IFlightTelemetry> OnUpdateTelemetryAction { get; set; }

        public WidgetGenericSubElementControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
            : base(parent, id, displayName, WidgetControlCategory.GenericElement, rootGo)
        {
            OnApplyThemeAction = onApplyTheme;
            OnUpdateTelemetryAction = onUpdateTelemetry;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            OnApplyThemeAction?.Invoke(theme);
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible) return;
            OnUpdateTelemetryAction?.Invoke(telemetry);
        }

        public override bool NeedsTelemetryUpdate => OnUpdateTelemetryAction != null;
    }
}
