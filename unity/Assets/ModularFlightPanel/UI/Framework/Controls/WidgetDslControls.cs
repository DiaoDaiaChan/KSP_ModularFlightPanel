using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Rendering;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 声明式文本与读数微控件 (Declarative Text & Readout Widget)
    /// 封装 UGUI Text，内置脏检查 (SetTextIfChanged)、多主题响应与布局自适应
    /// </summary>
    public class TextWidget : BaseWidgetControl, IWidgetDslControl
    {
        public WidgetDock Dock { get; set; } = WidgetDock.Custom;
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

        public TextWidget(string defaultText, WidgetDock dock, float font = 0f, TextStyleRole role = TextStyleRole.PrimaryValue)
            : base("text_ctrl", "Text Control", WidgetControlCategory.Readout, null)
        {
            Dock = dock;
            StyleRole = role;
            DefaultText = defaultText;
            _text = defaultText;
            if (font > 0f) FontSize = font;
        }

        #region Semantic Factory Helpers

        public static TextWidget Title(string defaultText = "", string token = null, float font = 10f)
            => new TextWidget(defaultText, WidgetDock.TopLeft, font, TextStyleRole.Label) { Token = token };

        public static TextWidget Badge(string defaultText = "", string token = null, float font = 8f)
            => new TextWidget(defaultText, WidgetDock.TopRight, font, TextStyleRole.SecondaryValue) { Token = token };

        public static TextWidget Value(string defaultTextOrToken = "", string unit = null, float font = 20f)
            => new TextWidget(defaultTextOrToken, WidgetDock.Center, font, TextStyleRole.PrimaryValue) { Token = defaultTextOrToken };

        public static TextWidget Unit(string unitText = "", float font = 10f)
            => new TextWidget(unitText, WidgetDock.BottomRight, font, TextStyleRole.Unit);

        #endregion

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "text_ctrl") Id = fieldName;
            DisplayName = fieldName;
            Category = WidgetControlCategory.Readout;

            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            Vector2 cardSz = parent.RectTransform != null ? parent.RectTransform.sizeDelta : Vector2.zero;
            if (cardSz.x <= 0f && parent.BaseSize.x > 0f)
            {
                cardSz = parent.BaseSize * dpiScale;
            }

            int sz = Mathf.Max(6, Mathf.RoundToInt((FontSize > 0f ? FontSize : 12f) * dpiScale));
            string initialText = !string.IsNullOrEmpty(_text) ? _text : (DefaultText ?? Token ?? "---");
            TextComponent = UIFactory.CreateText(parent.transform, Id, initialText, sz, Alignment, style.GetTextColor(StyleRole, theme));
            TextComponent.fontStyle = FontStyle;
            RootGameObject = TextComponent.gameObject;

            RectTransform rt = TextComponent.rectTransform;

            if (Dock == WidgetDock.TopLeft)
            {
                TextComponent.alignment = TextAnchor.MiddleLeft;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0.7f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(8f * dpiScale, -6f * dpiScale);
                rt.sizeDelta = new Vector2(0f, 18f * dpiScale);
            }
            else if (Dock == WidgetDock.TopRight)
            {
                TextComponent.alignment = TextAnchor.MiddleRight;
                rt.anchorMin = new Vector2(0.6f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-8f * dpiScale, -6f * dpiScale);
                rt.sizeDelta = new Vector2(0f, 18f * dpiScale);
            }
            else if (Dock == WidgetDock.Center)
            {
                TextComponent.alignment = TextAnchor.MiddleLeft;
                rt.anchorMin = new Vector2(0f, 0.25f);
                rt.anchorMax = new Vector2(0.72f, 0.82f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(8f * dpiScale, 0f);
                rt.sizeDelta = Vector2.zero;
            }
            else if (Dock == WidgetDock.BottomRight)
            {
                TextComponent.alignment = TextAnchor.LowerLeft;
                rt.anchorMin = new Vector2(0.72f, 0.3f);
                rt.anchorMax = new Vector2(1f, 0.65f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }
            else if (Dock == WidgetDock.BottomLeft)
            {
                TextComponent.alignment = TextAnchor.LowerLeft;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0.35f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(8f * dpiScale, 6f * dpiScale);
                rt.sizeDelta = Vector2.zero;
            }
            else if (Dock == WidgetDock.Fill)
            {
                TextComponent.alignment = TextAnchor.MiddleCenter;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(-12f * dpiScale, -12f * dpiScale);
            }
            else
            {
                float w = Width > 0f ? Width * dpiScale : (cardSz.x > 0f ? cardSz.x - 12f * dpiScale : 100f * dpiScale);
                float h = Height > 0f ? Height * dpiScale : (sz + 6f * dpiScale);
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = new Vector2(X * dpiScale, Y * dpiScale);
            }
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
    /// 声明式导航参考系胶囊微控件 (Declarative Reference Frame Capsule Button Widget)
    /// 封装高反差矢量微标 (ReferenceFrameIconAtlasGenerator)、权威参考系全称、左键切换/右键呼出、语义主题着色与抗抖脏检查
    /// </summary>
    public class ReferenceFrameButtonWidget : BaseWidgetControl, IWidgetDslControl
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float FontSize { get; set; } = 8f;
        public float IconSize { get; set; } = 15f;

        public Button ButtonComponent { get; private set; }
        public Image BackgroundComponent { get; private set; }
        public Outline OutlineComponent { get; private set; }
        public RawImage IconComponent { get; private set; }
        public Text LabelComponent { get; private set; }

        public Action OnClick { get; set; }
        public Action OnRightClick { get; set; }

        private string _frameCategory = string.Empty;
        private string _frameTitle = string.Empty;
        private int _iconIndex = ReferenceFrameIconAtlasGenerator.INDEX_INERTIAL;
        private TextStyleRole _currentRole = TextStyleRole.Cardinal;

        public string FrameCategory => _frameCategory;
        public string FrameTitle => _frameTitle;
        public int IconIndex => _iconIndex;

        private string _tooltipTitle;
        private string _tooltipDesc;
        private string _tooltipKey;

        public ReferenceFrameButtonWidget(float x = 0f, float y = 0f, float w = 92f, float h = 18f, float font = 8f, float iconSize = 15f)
            : base("ref_frame_ctrl", "Reference Frame Button", WidgetControlCategory.ActionButton, null)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
            FontSize = font;
            IconSize = iconSize;
        }

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

        public void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme)
        {
            ParentWidget = parent;
            if (string.IsNullOrEmpty(Id) || Id == "ref_frame_ctrl") Id = fieldName;
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

            var handler = ButtonComponent.gameObject.AddComponent<DslButtonClickHandler>();
            handler.OnLeftClick = () => OnClick?.Invoke();
            handler.OnRightClick = () => OnRightClick?.Invoke();

            // 1. 左侧矢量微标图标
            float iconDpi = Mathf.Max(12f, IconSize * dpiScale);
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            iconObj.transform.SetParent(ButtonComponent.transform, false);
            IconComponent = iconObj.GetComponent<RawImage>();
            IconComponent.texture = ReferenceFrameIconAtlasGenerator.GetAtlas();
            IconComponent.uvRect = ReferenceFrameIconAtlasGenerator.GetIconUv(ReferenceFrameIconAtlasGenerator.INDEX_INERTIAL);

            RectTransform iconRt = IconComponent.rectTransform;
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(4f * dpiScale, 0f);
            iconRt.sizeDelta = new Vector2(iconDpi, iconDpi);

            // 2. 右侧权威参考系名称
            int fontSz = Mathf.Max(6, Mathf.RoundToInt(FontSize * dpiScale));
            float leftPad = (4f + IconSize + 3f) * dpiScale;
            float rightPad = 3f * dpiScale;
            float textW = sz.x - leftPad - rightPad;

            LabelComponent = UIFactory.CreateText(ButtonComponent.transform, "Text", I18n.Tr("WIDGET_FW_SURFACE", "表面"), fontSz, TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            LabelComponent.fontStyle = FontStyle.Bold;
            LabelComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            LabelComponent.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform labelRt = LabelComponent.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0f, 0.5f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = new Vector2(leftPad, 0f);
            labelRt.sizeDelta = new Vector2(textW, sz.y);

            if (!string.IsNullOrEmpty(_tooltipTitle))
            {
                ButtonComponent.SetTooltip(_tooltipTitle, _tooltipDesc, _tooltipKey);
            }

            ApplyTheme(theme);
        }

        public void UpdateFrame(string category, string title, ThemeConfig theme = null)
        {
            if (string.IsNullOrEmpty(category)) category = "SURFACE";
            if (string.IsNullOrEmpty(title)) title = category;

            bool categoryChanged = _frameCategory != category;
            bool titleChanged = _frameTitle != title;

            if (!categoryChanged && !titleChanged) return;

            _frameCategory = category;
            _frameTitle = title;

            _iconIndex = ReferenceFrameIconAtlasGenerator.GetIconIndex(category);

            switch (_iconIndex)
            {
                case ReferenceFrameIconAtlasGenerator.INDEX_SURFACE:
                case ReferenceFrameIconAtlasGenerator.INDEX_BODY_FIXED:
                case ReferenceFrameIconAtlasGenerator.INDEX_LAGRANGE:
                    _currentRole = TextStyleRole.Accent;
                    break;
                case ReferenceFrameIconAtlasGenerator.INDEX_TARGET:
                    _currentRole = TextStyleRole.Warning;
                    break;
                case ReferenceFrameIconAtlasGenerator.INDEX_ORBITAL:
                case ReferenceFrameIconAtlasGenerator.INDEX_INERTIAL:
                default:
                    _currentRole = TextStyleRole.Cardinal;
                    break;
            }

            if (IconComponent != null)
            {
                IconComponent.uvRect = ReferenceFrameIconAtlasGenerator.GetIconUv(_iconIndex);
            }

            if (LabelComponent != null && titleChanged)
            {
                BaseFlightWidget.SetTextIfChanged(LabelComponent, title);
            }

            ApplyVisualRole(theme);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (ButtonComponent != null)
            {
                style.ApplyButtonStyle(ButtonComponent, BackgroundComponent, LabelComponent, ButtonVisualRole.Normal, false, theme);
            }
            ApplyVisualRole(theme);
        }

        private void ApplyVisualRole(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            Color clr = style.GetTextColor(_currentRole, theme);
            if (IconComponent != null)
            {
                IconComponent.color = clr;
            }
            if (LabelComponent != null)
            {
                LabelComponent.color = clr;
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !IsVisible) return;
            string category = TelemetryTokenEngine.Evaluate("{FRAME:TYPE}", telemetry);
            string title = TelemetryTokenEngine.Evaluate("{FRAME}", telemetry);
            UpdateFrame(category, title);
        }
    }

    /// <summary>
    /// 声明式线性柱条微控件 (Declarative Linear Bar Widget)
    /// 封装槽轨 (Track) 与填充 (Fill)、归一化进度 (FillAmount)、横竖双向与主题自适应
    /// </summary>
    public class LinearBarWidget : BaseWidgetControl, IWidgetDslControl
    {
        public WidgetDock Dock { get; set; } = WidgetDock.Custom;
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool IsVertical { get; set; }
        public MeterStyleRole MeterRole { get; set; } = MeterStyleRole.Primary;
        public string NumericToken { get; set; }
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;

        public Image TrackImage { get; private set; }
        public Image FillImage { get; private set; }
        public RectTransform FillRectTransform => FillImage != null ? FillImage.rectTransform : null;

        private float _currentRatio = 0f;
        public float FillAmount
        {
            get => _currentRatio;
            set => SetFillAmount(value, MeterRole);
        }

        public static LinearBarWidget BottomBar(MeterStyleRole role = MeterStyleRole.Primary, float height = 4f, string token = null, double min = 0.0, double max = 100.0)
        {
            return new LinearBarWidget(role, h: height, token: token)
            {
                Dock = WidgetDock.Bottom,
                MinValue = min,
                MaxValue = max
            };
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

            Vector2 cardSz = parent.RectTransform != null ? parent.RectTransform.sizeDelta : Vector2.zero;
            if (cardSz.x <= 0f && parent.BaseSize.x > 0f)
            {
                cardSz = parent.BaseSize * dpiScale;
            }

            Vector2 sz;
            Vector2 pos;

            if (Dock == WidgetDock.Bottom)
            {
                float barW = cardSz.x > 0f ? (cardSz.x - 16f * dpiScale) : 144f * dpiScale;
                float barH = (Height > 0f ? Height : 4f) * dpiScale;
                sz = new Vector2(barW, barH);
                pos = new Vector2(0f, -cardSz.y * 0.5f + 10f * dpiScale);
                IsVertical = false;
                Width = barW / (dpiScale > 0.001f ? dpiScale : 1f);
                Height = barH / (dpiScale > 0.001f ? dpiScale : 1f);
            }
            else
            {
                sz = new Vector2(Width * dpiScale, Height * dpiScale);
                pos = new Vector2(X * dpiScale, Y * dpiScale);
            }

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
                TrackImage.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }
            if (FillImage != null)
            {
                FillImage.color = style.GetMeterColor(MeterRole, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !IsVisible || string.IsNullOrEmpty(NumericToken)) return;
            double val = TelemetryTokenEngine.EvaluateNumeric(NumericToken, telemetry);
            if (!double.IsNaN(val))
            {
                double denom = MaxValue - MinValue;
                float frac = denom > 0.0001 ? (float)((val - MinValue) / denom) : 0f;
                FillAmount = frac;
            }
        }
    }
}
