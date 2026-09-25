using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 组件内部控件大类 (Widget Control Categories)
    /// </summary>
    public enum WidgetControlCategory
    {
        Header,         // 标题栏、副标题、顶部分割线与状态徽标
        Readout,        // 读数框、等宽数值窗、单位角标
        LinearGauge,    // 线性柱条、进度条、标尺轨道
        ArcGauge,       // 极坐标弧形条、ECAM/SpaceX 度量环
        NeedlePointer,  // 动态旋转指针、导引针
        ActionButton,   // 交互开关、多态药丸按钮、控制键
        Annunciator,    // 光字牌、告警警示灯珠
        Viewport,       // 2D剪影、雷达对准光环、三维球视口
        DataStack,      // 垂直推进栈行、表格行、分级列表
        ModeCapsule,    // 模式切换胶囊
        TrendBar,       // 动力学趋势指示条
        GenericElement, // 通用命名子元素
        Misc            // 其他自定义零件
    }

    /// <summary>
    /// 标准化组件控件契约 (Standardized Widget Control Interface)
    /// </summary>
    public interface IWidgetControl
    {
        string Id { get; }
        string DisplayName { get; }
        WidgetControlCategory Category { get; }
        bool IsVisible { get; set; }
        GameObject RootGameObject { get; }
        RectTransform RectTransform { get; }
        void ApplyTheme(ThemeConfig theme);
        void UpdateTelemetry(IFlightTelemetry telemetry);
        void BindConfig(WidgetConfig config);
    }

    /// <summary>
    /// 标准化声明式 DSL 控件契约 (Standardized Declarative DSL Control Interface)
    /// 允许在派生小组件的类头部直接通过 new 声明实例字段，
    /// 由基类 BaseInitialize 通过反射自省自动感知、构建 UGUI 渲染节点并纳管至 Controls。
    /// 彻底消除 CS0649 警告、冗长特性参数与空的 OnInitialize 样板代码。
    /// </summary>
    public interface IWidgetDslControl : IWidgetControl
    {
        void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme);
    }

    /// <summary>
    /// 标准控件抽象基类
    /// </summary>
    public abstract class BaseWidgetControl : IWidgetControl
    {
        public string Id { get; protected set; }
        public string DisplayName { get; protected set; }
        public WidgetControlCategory Category { get; protected set; }
        public GameObject RootGameObject { get; protected set; }
        public RectTransform RectTransform => RootGameObject != null ? RootGameObject.GetComponent<RectTransform>() : null;

        private bool _isVisible = true;
        public virtual bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                if (RootGameObject != null && RootGameObject.activeSelf != value)
                {
                    RootGameObject.SetActive(value);
                }
            }
        }

        public BaseFlightWidget ParentWidget { get; protected set; }

        protected BaseWidgetControl(BaseFlightWidget parent, string id, string displayName, WidgetControlCategory category, GameObject rootGo)
        {
            ParentWidget = parent;
            Id = id ?? "control";
            DisplayName = displayName ?? Id;
            Category = category;
            RootGameObject = rootGo;
        }

        protected BaseWidgetControl(string id, string displayName, WidgetControlCategory category, GameObject rootGo)
            : this(null, id, displayName, category, rootGo)
        {
        }

        public abstract void ApplyTheme(ThemeConfig theme);
        public abstract void UpdateTelemetry(IFlightTelemetry telemetry);

        public virtual void BindConfig(WidgetConfig config)
        {
            if (config != null && config.IsSubElementDisabled(Id))
            {
                IsVisible = false;
            }
        }
    }

    #region Standard Control Implementations

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
                if (eval != _lastTitle) { _lastTitle = eval; TitleText.text = eval; }
            }

            if (SubtitleText != null && !string.IsNullOrEmpty(SubtitleTemplate))
            {
                string eval = TelemetryTokenEngine.Evaluate(SubtitleTemplate, telemetry);
                if (eval != _lastSubtitle) { _lastSubtitle = eval; SubtitleText.text = eval; }
            }

            if (StatusBadgeText != null && !string.IsNullOrEmpty(StatusBadgeTemplate))
            {
                string eval = TelemetryTokenEngine.Evaluate(StatusBadgeTemplate, telemetry);
                if (eval != _lastBadge) { _lastBadge = eval; StatusBadgeText.text = eval; }
            }
        }

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

        public void SetValue(string val, string unit = null)
        {
            if (ValueText != null && val != _lastRawValue)
            {
                _lastRawValue = val;
                ValueText.text = UIFactory.FormatTabular(val ?? Fallback);
            }
            if (unit != null && UnitLabel != null && unit != _lastUnit)
            {
                _lastUnit = unit;
                UnitLabel.text = unit;
            }
        }

        public void SetFormattedValue(double val, string format = "F1", string unit = null)
        {
            string str = double.IsNaN(val) ? Fallback : val.ToString(format);
            SetValue(str, unit);
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (!IsVisible || telemetry == null) return;

            if (TitleLabel != null && Title != _lastTitle)
            {
                _lastTitle = Title;
                TitleLabel.text = Title;
            }

            if (ValueText != null && !string.IsNullOrEmpty(Token))
            {
                string eval = TelemetryTokenEngine.Evaluate(Token, telemetry);
                if (string.IsNullOrEmpty(eval)) eval = Fallback;
                if (eval != _lastRawValue)
                {
                    _lastRawValue = eval;
                    ValueText.text = UIFactory.FormatTabular(eval);
                }
            }

            if (UnitLabel != null && Unit != _lastUnit)
            {
                _lastUnit = Unit;
                UnitLabel.text = Unit;
            }
        }

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
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;
        public double CautionThreshold { get; set; } = 80.0;
        public double WarningThreshold { get; set; } = 95.0;
        public bool IsVertical { get; set; } = false;
        public float MaxSpanLength { get; set; } = 100f;

        private double _lastValue = double.NaN;
        private int _lastAlertState = -1; // 0=Norm, 1=Caut, 2=Warn

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
                TrackImage.material = style.GetUiMaterial(isText: false);
                TrackImage.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }
            if (FillImage != null)
            {
                FillImage.material = style.GetUiMaterial(isText: false);
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
            if (LampLabel != null && LampLabel.text != Label)
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
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;

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
    }

    /// <summary>
    /// 标准动态旋转指针控件 (Needle Pointer Control)
    /// </summary>
    public class WidgetNeedleControl : BaseWidgetControl
    {
        public RectTransform PivotRt { get; private set; }
        public Image NeedleImage { get; private set; }
        public string NumericToken { get; set; }
        public float StartAngle { get; set; } = 225f;
        public float EndAngle { get; set; } = -45f;
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;

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
                PivotRt.localRotation = Quaternion.Euler(0f, 0f, angle);
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
            // Viewport specific frame updates handled by child or caller
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
    }

    #endregion

    /// <summary>
    /// 全局航电组件控件注册与装配管理器 (Avionics Widget Control Manager)
    /// 核心职责：
    /// 1. 集中管理所有组件内部的子控件 (Sub-Elements / Micro-Controls)；
    /// 2. 为所有 44 款组件提供标准化、零重复造轮子的通用控件装配能力；
    /// 3. 打通编辑模式 (Edit Mode) 的动态检查器 (Inspector) 与子部件按需裁剪屏蔽 (Sub-Element Masking)。
    /// </summary>
    public static class WidgetControlManager
    {
        private static readonly Dictionary<BaseFlightWidget, List<IWidgetControl>> _widgetControls =
            new Dictionary<BaseFlightWidget, List<IWidgetControl>>();

        public static void Register(BaseFlightWidget widget, IWidgetControl control)
        {
            if (widget == null || control == null) return;
            if (!_widgetControls.TryGetValue(widget, out var list))
            {
                list = new List<IWidgetControl>();
                _widgetControls[widget] = list;
            }
            if (!list.Contains(control))
            {
                list.Add(control);
            }
        }

        public static IReadOnlyList<IWidgetControl> GetControls(BaseFlightWidget widget)
        {
            if (widget != null && _widgetControls.TryGetValue(widget, out var list))
            {
                return list;
            }
            return Array.Empty<IWidgetControl>();
        }

        public static T GetControl<T>(BaseFlightWidget widget, string controlId) where T : class, IWidgetControl
        {
            if (widget == null || string.IsNullOrEmpty(controlId)) return null;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is T typed && string.Equals(list[i].Id, controlId, StringComparison.OrdinalIgnoreCase))
                    {
                        return typed;
                    }
                }
            }
            return null;
        }

        public static void UpdateControls(BaseFlightWidget widget, IFlightTelemetry telemetry)
        {
            if (widget == null || telemetry == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].UpdateTelemetry(telemetry);
                }
            }
        }

        public static void ApplyThemeToControls(BaseFlightWidget widget, ThemeConfig theme)
        {
            if (widget == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].ApplyTheme(theme);
                }
            }
        }

        public static void BindConfigToControls(BaseFlightWidget widget, WidgetConfig config)
        {
            if (widget == null || config == null) return;
            if (_widgetControls.TryGetValue(widget, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].BindConfig(config);
                }
            }
        }

        public static void UnregisterAll(BaseFlightWidget widget)
        {
            if (widget != null)
            {
                _widgetControls.Remove(widget);
            }
        }

        #region Factory Methods

        /// <summary>
        /// 创建标准航电标题栏控件
        /// </summary>
        public static WidgetHeaderControl CreateHeader(BaseFlightWidget parent, string id, string title, string subtitle = "",
            Vector2? pos = null, Vector2? size = null, string badge = "")
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            Vector2 actualSize = size ?? new Vector2(parent.RectTransform.sizeDelta.x - 16f * s, 20f * s);
            Vector2 actualPos = pos ?? new Vector2(0f, parent.RectTransform.sizeDelta.y * 0.5f - actualSize.y * 0.5f - 4f * s);

            GameObject headerGo = new GameObject(id + "_Header", typeof(RectTransform));
            headerGo.transform.SetParent(parent.transform, false);
            RectTransform headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.sizeDelta = actualSize;
            headerRt.anchoredPosition = actualPos;

            // 标题
            Text titleTxt = UIFactory.CreateText(headerGo.transform, "TitleText", title, Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform tRt = titleTxt.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0.2f);
            tRt.anchorMax = new Vector2(0.6f, 1f);
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // 副标题
            Text subTxt = null;
            if (!string.IsNullOrEmpty(subtitle))
            {
                subTxt = UIFactory.CreateText(headerGo.transform, "SubtitleText", subtitle, Mathf.RoundToInt(7.5f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform sRt = subTxt.GetComponent<RectTransform>();
                sRt.anchorMin = new Vector2(0f, 0f);
                sRt.anchorMax = new Vector2(0.6f, 0.45f);
                sRt.offsetMin = Vector2.zero;
                sRt.offsetMax = Vector2.zero;
            }

            // 状态徽标
            Text badgeTxt = null;
            if (!string.IsNullOrEmpty(badge))
            {
                badgeTxt = UIFactory.CreateText(headerGo.transform, "BadgeText", badge, Mathf.RoundToInt(8f * s),
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
                RectTransform bRt = badgeTxt.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.6f, 0.2f);
                bRt.anchorMax = new Vector2(1f, 1f);
                bRt.offsetMin = Vector2.zero;
                bRt.offsetMax = Vector2.zero;
            }

            // 装饰细线
            GameObject divGo = UIFactory.CreatePanel(headerGo.transform, "Divider", new Vector2(actualSize.x, 1f * s),
                new Vector2(0f, -actualSize.y * 0.5f), style.GetLineColor(LineWeight.Faint, theme));
            Image divImg = divGo.GetComponent<Image>();

            var ctrl = new WidgetHeaderControl(parent, id, "标题栏", headerGo, titleTxt, subTxt, badgeTxt, divImg, title, subtitle, badge);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准数显读数盒控件
        /// </summary>
        public static WidgetReadoutControl CreateReadout(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string token, string title = "", string unit = "")
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            GameObject boxGo = UIFactory.CreatePanel(parent.transform, id + "_Readout", size, pos,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);

            Image bg = boxGo.GetComponent<Image>();
            Outline outline = boxGo.GetComponent<Outline>();

            // 标题
            Text titleTxt = null;
            if (!string.IsNullOrEmpty(title))
            {
                titleTxt = UIFactory.CreateText(boxGo.transform, "Title", title, Mathf.Max(6, Mathf.RoundToInt(size.y * 0.28f)),
                    TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                RectTransform tRt = titleTxt.GetComponent<RectTransform>();
                tRt.anchorMin = new Vector2(0f, 0.5f);
                tRt.anchorMax = new Vector2(1f, 1f);
                tRt.offsetMin = new Vector2(4f * s, 0f);
                tRt.offsetMax = new Vector2(-4f * s, -2f * s);
            }

            // 主数显
            int valFontSize = Mathf.Max(8, Mathf.RoundToInt(size.y * 0.48f));
            Text valTxt = UIFactory.CreateText(boxGo.transform, "Value", "---", valFontSize,
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            valTxt.fontStyle = FontStyle.Bold;
            RectTransform vRt = valTxt.GetComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0f, 0f);
            vRt.anchorMax = new Vector2(0.78f, titleTxt != null ? 0.65f : 1f);
            vRt.offsetMin = new Vector2(4f * s, 2f * s);
            vRt.offsetMax = Vector2.zero;

            // 单位
            Text unitTxt = null;
            if (!string.IsNullOrEmpty(unit))
            {
                unitTxt = UIFactory.CreateText(boxGo.transform, "Unit", unit, Mathf.Max(6, Mathf.RoundToInt(size.y * 0.26f)),
                    TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.Unit, theme));
                RectTransform uRt = unitTxt.GetComponent<RectTransform>();
                uRt.anchorMin = new Vector2(0.72f, 0f);
                uRt.anchorMax = new Vector2(1f, 0.65f);
                uRt.offsetMin = new Vector2(0f, 2f * s);
                uRt.offsetMax = new Vector2(-4f * s, 0f);
            }

            var ctrl = new WidgetReadoutControl(parent, id, displayName, boxGo, bg, outline, titleTxt, valTxt, unitTxt, token, title, unit);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准线性柱状表控件
        /// </summary>
        public static WidgetLinearBarControl CreateLinearBar(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string token, double min = 0.0, double max = 100.0, bool isVertical = false)
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            GameObject trackGo = UIFactory.CreatePanel(parent.transform, id + "_Track", size, pos,
                style.GetMeterColor(MeterStyleRole.Track, theme));
            Image trackImg = trackGo.GetComponent<Image>();

            Vector2 fillSize = isVertical ? new Vector2(size.x, 0f) : new Vector2(0f, size.y);
            Vector2 fillPos = isVertical ? new Vector2(0f, -size.y * 0.5f) : new Vector2(-size.x * 0.5f, 0f);

            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, id + "_Fill", fillSize, fillPos,
                style.GetMeterColor(MeterStyleRole.Primary, theme));
            Image fillImg = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();

            if (isVertical)
            {
                fillRt.pivot = new Vector2(0.5f, 0f);
                fillRt.anchoredPosition = new Vector2(0f, -size.y * 0.5f);
            }
            else
            {
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.anchoredPosition = new Vector2(-size.x * 0.5f, 0f);
            }

            float span = isVertical ? size.y / s : size.x / s;
            var ctrl = new WidgetLinearBarControl(parent, id, displayName, trackGo, trackImg, fillImg, token, min, max, span, isVertical);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 创建标准交互按键控件
        /// </summary>
        public static WidgetActionButtonControl CreateButton(BaseFlightWidget parent, string id, string displayName,
            Vector2 size, Vector2 pos, string label, Action onClick, bool isToggle = false)
        {
            float s = parent.CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            Button btn = UIFactory.CreateButton(parent.transform, id + "_Btn", size, pos, onClick != null ? new UnityEngine.Events.UnityAction(onClick) : null);
            Image bg = btn.GetComponent<Image>();
            Outline outline = btn.GetComponent<Outline>();

            Text txt = UIFactory.CreateText(btn.transform, "Label", label, Mathf.Max(8, Mathf.RoundToInt(size.y * 0.45f)),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            txt.fontStyle = FontStyle.Bold;
            RectTransform tRt = txt.GetComponent<RectTransform>();
            tRt.sizeDelta = size;
            tRt.anchoredPosition = Vector2.zero;

            var ctrl = new WidgetActionButtonControl(parent, id, displayName, btn.gameObject, btn, bg, outline, txt, null, label, onClick, isToggle);
            Register(parent, ctrl);
            return ctrl;
        }

        /// <summary>
        /// 将已有子物体封装注册为标准化子部件 (支持遮罩、显隐与样式更新)
        /// </summary>
        public static WidgetGenericSubElementControl WrapElement(BaseFlightWidget parent, string id, string displayName,
            GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            var ctrl = new WidgetGenericSubElementControl(parent, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
            Register(parent, ctrl);
            return ctrl;
        }

        public static WidgetGenericSubElementControl WrapElement(BaseFlightWidget parent, string id, string displayName,
            GameObject rootGo, string description, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            var ctrl = new WidgetGenericSubElementControl(parent, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
            Register(parent, ctrl);
            return ctrl;
        }

        public static WidgetGenericSubElementControl WrapElement(string id, string displayName,
            GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
        {
            return new WidgetGenericSubElementControl(null, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);
        }

        #endregion
    }

    public enum AnnunciatorState
    {
        Off,
        Normal,
        Caution,
        Warning
    }

    #region Declarative DSL Widgets (TextWidget, ToggleButtonWidget, ActionButtonWidget, LinearBarWidget)

    /// <summary>
    /// 声明式文本与读数微控件 (Declarative Text & Readout Widget)
    /// 封装 UGUI Text，内置脏检查 (SetTextIfChanged)、多主题响应与布局自适应
    /// </summary>
    public class TextWidget : BaseWidgetControl, IWidgetDslControl
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float FontSize { get; set; } = 12f;
        public TextAnchor Alignment { get; set; } = TextAnchor.MiddleLeft;
        public FontStyle FontStyle { get; set; } = FontStyle.Normal;
        public TextStyleRole StyleRole { get; set; } = TextStyleRole.PrimaryValue;
        public string Token { get; set; }
        public string DefaultText { get; set; }

        public Text TextComponent { get; private set; }

        private string _text = string.Empty;
        public string Text
        {
            get => TextComponent != null ? TextComponent.text : _text;
            set
            {
                _text = value;
                if (TextComponent != null && value != null)
                {
                    BaseFlightWidget.SetTextIfChanged(TextComponent, value);
                }
            }
        }

        public TextStyleRole Role
        {
            get => StyleRole;
            set => SetRole(value);
        }

        public void SetRole(TextStyleRole role)
        {
            StyleRole = role;
            if (TextComponent != null)
            {
                WidgetStyleManager.Instance.ApplyTextStyle(TextComponent, role, null);
            }
        }

        public TextWidget(TextStyleRole role = TextStyleRole.PrimaryValue,
            float x = 0f, float y = 0f, float w = 0f, float h = 0f,
            float font = 12f, TextAnchor align = TextAnchor.MiddleLeft,
            string defaultText = "", string token = null)
            : base("text_ctrl", "Text Control", WidgetControlCategory.Readout, null)
        {
            StyleRole = role;
            X = x; Y = y; Width = w; Height = h;
            FontSize = font;
            Alignment = align;
            DefaultText = defaultText;
            _text = defaultText;
            Token = token;
        }

        public TextWidget(string defaultText,
            float x = 0f, float y = 0f, float w = 0f, float h = 0f,
            float font = 12f, TextAnchor align = TextAnchor.MiddleLeft,
            TextStyleRole role = TextStyleRole.PrimaryValue)
            : this(role, x, y, w, h, font, align, defaultText, null)
        {
        }

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "text_ctrl") Id = fieldName;
            DisplayName = fieldName;
            Category = WidgetControlCategory.Readout;

            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            int sz = Mathf.Max(6, Mathf.RoundToInt(FontSize * dpiScale));
            string initialText = !string.IsNullOrEmpty(_text) ? _text : (DefaultText ?? Token ?? "---");
            TextComponent = UIFactory.CreateText(parent.transform, Id, initialText, sz, Alignment, style.GetTextColor(StyleRole, theme));
            TextComponent.fontStyle = FontStyle;
            RootGameObject = TextComponent.gameObject;

            RectTransform rt = TextComponent.rectTransform;
            Vector2 cardSz = parent.RectTransform != null ? parent.RectTransform.sizeDelta : Vector2.zero;
            float w = Width > 0f ? Width * dpiScale : (cardSz.x > 0f ? cardSz.x - 12f * dpiScale : 100f * dpiScale);
            float h = Height > 0f ? Height * dpiScale : (sz + 6f * dpiScale);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(X * dpiScale, Y * dpiScale);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (TextComponent != null)
            {
                WidgetStyleManager.Instance.ApplyTextStyle(TextComponent, StyleRole, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !IsVisible) return;
            if (!string.IsNullOrEmpty(Token) && TextComponent != null)
            {
                string eval = TelemetryTokenEngine.Evaluate(Token, telemetry);
                BaseFlightWidget.SetTextIfChanged(TextComponent, eval);
            }
        }
    }

    /// <summary>
    /// 声明式开关式交互按键微控件 (Declarative Toggle Button Widget)
    /// 封装状态高亮 (IsActive)、防抖与脏检查、Tooltip 与主题响应
    /// </summary>
    public class ToggleButtonWidget : BaseWidgetControl, IWidgetDslControl
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float FontSize { get; set; } = 8f;
        public ButtonVisualRole VisualRole { get; set; } = ButtonVisualRole.ActiveToggle;
        public string DefaultLabel { get; set; }

        public Button ButtonComponent { get; private set; }
        public Text LabelComponent { get; private set; }
        public Outline OutlineComponent { get; private set; }
        public Image BackgroundComponent { get; private set; }

        public Action OnClick { get; set; }

        private bool _isActive = false;
        private bool _hasSetState = false;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (!_hasSetState || _isActive != value)
                {
                    _isActive = value;
                    _hasSetState = true;
                    if (ButtonComponent != null)
                    {
                        ButtonComponent.SetToggleActive(value);
                    }
                }
            }
        }

        private string _text;
        public string Text
        {
            get => LabelComponent != null ? LabelComponent.text : _text;
            set
            {
                _text = value;
                if (LabelComponent != null && value != null)
                {
                    BaseFlightWidget.SetTextIfChanged(LabelComponent, value);
                }
            }
        }

        private string _tooltipTitle;
        private string _tooltipDesc;
        private string _tooltipKey;

        public void SetTooltip(string title, string description = null, string shortcut = null)
        {
            _tooltipTitle = title;
            _tooltipDesc = description;
            _tooltipKey = shortcut;
            if (ButtonComponent != null)
            {
                ButtonComponent.SetTooltip(title, description, shortcut);
            }
        }

        public ToggleButtonWidget(string label = "",
            float x = 0f, float y = 0f, float w = 38f, float h = 18f,
            float font = 8f, ButtonVisualRole role = ButtonVisualRole.ActiveToggle, Action onClick = null)
            : base("toggle_ctrl", "Toggle Button", WidgetControlCategory.ActionButton, null)
        {
            DefaultLabel = label;
            _text = label;
            X = x; Y = y; Width = w; Height = h;
            FontSize = font;
            VisualRole = role;
            OnClick = onClick;
        }

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "toggle_ctrl") Id = fieldName;
            DisplayName = fieldName;
            Category = WidgetControlCategory.ActionButton;

            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            Vector2 sz = new Vector2(Width * dpiScale, Height * dpiScale);
            Vector2 pos = new Vector2(X * dpiScale, Y * dpiScale);

            ButtonComponent = UIFactory.CreateButton(parent.transform, Id, sz, pos, () => OnClick?.Invoke());
            RootGameObject = ButtonComponent.gameObject;
            BackgroundComponent = ButtonComponent.GetComponent<Image>();
            OutlineComponent = ButtonComponent.GetComponent<Outline>();

            int fontSz = Mathf.Max(6, Mathf.RoundToInt(FontSize * dpiScale));
            string initialLabel = !string.IsNullOrEmpty(_text) ? _text : (DefaultLabel ?? "");
            LabelComponent = UIFactory.CreateText(ButtonComponent.transform, "Text", initialLabel, fontSz, TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            LabelComponent.fontStyle = FontStyle.Bold;
            LabelComponent.rectTransform.sizeDelta = sz;

            if (_hasSetState && ButtonComponent != null)
            {
                ButtonComponent.SetToggleActive(_isActive);
            }

            if (!string.IsNullOrEmpty(_tooltipTitle))
            {
                ButtonComponent.SetTooltip(_tooltipTitle, _tooltipDesc, _tooltipKey);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (ButtonComponent != null)
            {
                style.ApplyButtonStyle(ButtonComponent, BackgroundComponent, LabelComponent, VisualRole, false, theme);
                if (_hasSetState)
                {
                    ButtonComponent.SetToggleActive(_isActive);
                }
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
        }
    }

    /// <summary>
    /// 声明式交互按键微控件 (Declarative Action Button Widget)
    /// 封装点击（支持左键/右键双响应）、文本与边框样式、Tooltip 与主题响应
    /// </summary>
    public class ActionButtonWidget : BaseWidgetControl, IWidgetDslControl
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float FontSize { get; set; } = 8f;
        public ButtonVisualRole VisualRole { get; set; } = ButtonVisualRole.Normal;
        public string DefaultLabel { get; set; }

        public Button ButtonComponent { get; private set; }
        public Text LabelComponent { get; private set; }
        public Outline OutlineComponent { get; private set; }
        public Image BackgroundComponent { get; private set; }

        public Action OnClick { get; set; }
        public Action OnRightClick { get; set; }

        private string _text;
        public string Text
        {
            get => LabelComponent != null ? LabelComponent.text : _text;
            set
            {
                _text = value;
                if (LabelComponent != null && value != null)
                {
                    BaseFlightWidget.SetTextIfChanged(LabelComponent, value);
                }
            }
        }

        public TextStyleRole TextRole
        {
            set
            {
                if (LabelComponent != null)
                {
                    WidgetStyleManager.Instance.ApplyTextStyle(LabelComponent, value, null);
                }
            }
        }

        public ButtonVisualRole Role
        {
            get => VisualRole;
            set
            {
                VisualRole = value;
                if (ButtonComponent != null)
                {
                    WidgetStyleManager.Instance.ApplyButtonStyle(ButtonComponent, BackgroundComponent, LabelComponent, value, false, null);
                }
            }
        }

        private string _tooltipTitle;
        private string _tooltipDesc;
        private string _tooltipKey;

        public void SetTooltip(string title, string description = null, string shortcut = null)
        {
            _tooltipTitle = title;
            _tooltipDesc = description;
            _tooltipKey = shortcut;
            if (ButtonComponent != null)
            {
                ButtonComponent.SetTooltip(title, description, shortcut);
            }
        }

        public ActionButtonWidget(string label = "",
            float x = 0f, float y = 0f, float w = 90f, float h = 18f,
            float font = 8f, ButtonVisualRole role = ButtonVisualRole.Normal,
            Action onClick = null, Action onRightClick = null)
            : base("action_ctrl", "Action Button", WidgetControlCategory.ActionButton, null)
        {
            DefaultLabel = label;
            _text = label;
            X = x; Y = y; Width = w; Height = h;
            FontSize = font;
            VisualRole = role;
            OnClick = onClick;
            OnRightClick = onRightClick;
        }

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "action_ctrl") Id = fieldName;
            DisplayName = fieldName;
            Category = WidgetControlCategory.ActionButton;

            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            Vector2 sz = new Vector2(Width * dpiScale, Height * dpiScale);
            Vector2 pos = new Vector2(X * dpiScale, Y * dpiScale);

            ButtonComponent = UIFactory.CreateButton(parent.transform, Id, sz, pos, null);
            RootGameObject = ButtonComponent.gameObject;
            BackgroundComponent = ButtonComponent.GetComponent<Image>();
            OutlineComponent = ButtonComponent.GetComponent<Outline>();

            if (OnRightClick != null)
            {
                var handler = ButtonComponent.gameObject.AddComponent<DslButtonClickHandler>();
                handler.OnLeftClick = () => OnClick?.Invoke();
                handler.OnRightClick = () => OnRightClick?.Invoke();
            }
            else if (OnClick != null)
            {
                ButtonComponent.onClick.AddListener(() => OnClick?.Invoke());
            }

            int fontSz = Mathf.Max(6, Mathf.RoundToInt(FontSize * dpiScale));
            string initialLabel = !string.IsNullOrEmpty(_text) ? _text : (DefaultLabel ?? "");
            LabelComponent = UIFactory.CreateText(ButtonComponent.transform, "Text", initialLabel, fontSz, TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            LabelComponent.fontStyle = FontStyle.Bold;
            LabelComponent.rectTransform.sizeDelta = sz;

            if (!string.IsNullOrEmpty(_tooltipTitle))
            {
                ButtonComponent.SetTooltip(_tooltipTitle, _tooltipDesc, _tooltipKey);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (ButtonComponent != null)
            {
                style.ApplyButtonStyle(ButtonComponent, BackgroundComponent, LabelComponent, VisualRole, false, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
        }
    }

    /// <summary>
    /// DSL 按钮双向点击处理器 (支持左键触发与右键辅助触发)
    /// </summary>
    public class DslButtonClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else
            {
                OnLeftClick?.Invoke();
            }
        }
    }

    /// <summary>
    /// 声明式线性柱条微控件 (Declarative Linear Bar Widget)
    /// 封装槽轨 (Track) 与填充 (Fill)、归一化进度 (FillAmount)、横竖双向与主题自适应
    /// </summary>
    public class LinearBarWidget : BaseWidgetControl, IWidgetDslControl
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool IsVertical { get; set; }
        public MeterStyleRole MeterRole { get; set; } = MeterStyleRole.Primary;
        public string NumericToken { get; set; }

        public Image TrackImage { get; private set; }
        public Image FillImage { get; private set; }
        public RectTransform FillRectTransform => FillImage != null ? FillImage.rectTransform : null;

        private float _currentRatio = 0f;
        public float FillAmount
        {
            get => _currentRatio;
            set => SetFillAmount(value, MeterRole);
        }

        public LinearBarWidget(MeterStyleRole role = MeterStyleRole.Primary,
            float x = 0f, float y = 0f, float w = 100f, float h = 4f, bool isVertical = false, string token = null)
            : base("bar_ctrl", "Linear Bar", WidgetControlCategory.LinearGauge, null)
        {
            MeterRole = role;
            X = x; Y = y; Width = w; Height = h;
            IsVertical = isVertical;
            NumericToken = token;
        }

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "bar_ctrl") Id = fieldName;
            DisplayName = fieldName;
            Category = WidgetControlCategory.LinearGauge;

            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            Vector2 sz = new Vector2(Width * dpiScale, Height * dpiScale);
            Vector2 pos = new Vector2(X * dpiScale, Y * dpiScale);

            GameObject trackGo = UIFactory.CreatePanel(parent.transform, Id + "_Track", sz, pos,
                style.GetMeterColor(MeterStyleRole.Track, theme));
            TrackImage = trackGo.GetComponent<Image>();
            RootGameObject = trackGo;

            Vector2 fillSize = IsVertical ? new Vector2(sz.x, 0f) : new Vector2(0f, sz.y);
            Vector2 fillPos = IsVertical ? new Vector2(0f, -sz.y * 0.5f) : new Vector2(-sz.x * 0.5f, 0f);

            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, Id + "_Fill", fillSize, fillPos,
                style.GetMeterColor(MeterRole, theme));
            FillImage = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();

            if (IsVertical)
            {
                fillRt.pivot = new Vector2(0.5f, 0f);
                fillRt.anchoredPosition = new Vector2(0f, -sz.y * 0.5f);
            }
            else
            {
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.anchoredPosition = new Vector2(-sz.x * 0.5f, 0f);
            }

            if (_currentRatio > 0f)
            {
                SetFillAmount(_currentRatio, MeterRole);
            }
        }

        public void SetFillAmount(float ratio, MeterStyleRole role = MeterStyleRole.Primary)
        {
            _currentRatio = Mathf.Clamp01(ratio);
            MeterRole = role;

            if (FillRectTransform != null)
            {
                float s = ParentWidget != null ? ParentWidget.CurrentDpiScale : 1.0f;
                if (IsVertical)
                {
                    float curLen = Height * s * _currentRatio;
                    FillRectTransform.sizeDelta = new Vector2(FillRectTransform.sizeDelta.x, curLen);
                }
                else
                {
                    float curLen = Width * s * _currentRatio;
                    FillRectTransform.sizeDelta = new Vector2(curLen, FillRectTransform.sizeDelta.y);
                }
            }

            if (FillImage != null)
            {
                ThemeConfig curTheme = WidgetStyleManager.Instance?.CurrentTheme;
                if (curTheme != null)
                {
                    FillImage.color = WidgetStyleManager.Instance.GetMeterColor(role, curTheme);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (TrackImage != null)
            {
                TrackImage.material = style.GetUiMaterial(isText: false);
                TrackImage.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }
            if (FillImage != null)
            {
                FillImage.material = style.GetUiMaterial(isText: false);
                FillImage.color = style.GetMeterColor(MeterRole, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
        }
    }

    #endregion

    /// <summary>
    /// 挂载在每个小组件上的微控件容器适配器，提供流式链式与声明式微控件生命周期访问
    /// </summary>
    public class WidgetControlContainer
    {
        private readonly BaseFlightWidget _owner;

        public WidgetControlContainer(BaseFlightWidget owner)
        {
            _owner = owner;
        }

        public void Register(IWidgetControl control) => WidgetControlManager.Register(_owner, control);
        public void ApplyThemeToControls(ThemeConfig theme) => WidgetControlManager.ApplyThemeToControls(_owner, theme);
        public void BindConfigToControls(WidgetConfig config) => WidgetControlManager.BindConfigToControls(_owner, config);
        public void UpdateControls(IFlightTelemetry telemetry) => WidgetControlManager.UpdateControls(_owner, telemetry);
        public void UnregisterAll() => WidgetControlManager.UnregisterAll(_owner);
        public IReadOnlyList<IWidgetControl> All => WidgetControlManager.GetControls(_owner);
        public T Get<T>(string id) where T : class, IWidgetControl => WidgetControlManager.GetControl<T>(_owner, id);

        public WidgetGenericSubElementControl Wrap(string id, string displayName, GameObject rootGo, Action<ThemeConfig> onApplyTheme = null, Action<IFlightTelemetry> onUpdateTelemetry = null)
            => WidgetControlManager.WrapElement(_owner, id, displayName, rootGo, onApplyTheme, onUpdateTelemetry);

        public WidgetHeaderControl AddHeader(string id, string title, string subtitle = "", Vector2? pos = null, Vector2? size = null, string badge = "")
            => WidgetControlManager.CreateHeader(_owner, id, title, subtitle, pos, size, badge);

        public WidgetReadoutControl AddReadout(string id, string displayName, Vector2 size, Vector2 pos, string token, string title = "", string unit = "")
            => WidgetControlManager.CreateReadout(_owner, id, displayName, size, pos, token, title, unit);

        public WidgetLinearBarControl AddLinearBar(string id, string displayName, Vector2 size, Vector2 pos, string token, double min = 0.0, double max = 100.0, bool isVertical = false)
            => WidgetControlManager.CreateLinearBar(_owner, id, displayName, size, pos, token, min, max, isVertical);

        public WidgetActionButtonControl AddButton(string id, string displayName, Vector2 size, Vector2 pos, string label, Action onClick, bool isToggle = false)
            => WidgetControlManager.CreateButton(_owner, id, displayName, size, pos, label, onClick, isToggle);
    }
}
