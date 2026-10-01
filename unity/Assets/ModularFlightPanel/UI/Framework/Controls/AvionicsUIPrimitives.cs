using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    #region AvionicsValueBox
    /// <summary>
    /// 航电等宽防抖数显窗 (Avionics Tabular Value Display Box)
    /// 统一治理高频跳变遥测数值的排版布局：
    /// 1. 自动数学负号对齐与等宽规范化 (FormatTabular)，彻底消除文字左右微颤抖动；
    /// 2. 主数值大字与右上/右侧小单位合璧，杜绝文字重叠；
    /// 3. 支持语义色阶跃（正常/高亮/警告/警报）。
    /// </summary>
    public class AvionicsValueBox : MonoBehaviour
    {
        public Image BackgroundImage;
        public Outline BorderOutline;
        public Text TitleLabel;
        public Text ValueText;
        public Text UnitLabel;

        private TextStyleRole _currentRole = TextStyleRole.PrimaryValue;
        public TextStyleRole VisualRole => _currentRole;

        private string _lastRawValue = null;
        private string _lastUnit = null;

        public void Initialize(string title, string initialValue, string unit, Vector2 size, ThemeConfig theme = null)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = UIFactory.GetScreenDpiScale();

            // 1. 嵌入式暗晶卡片底槽
            BackgroundImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            BorderOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            Color borderCol = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);
            BorderOutline.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Ghost);
            BorderOutline.effectDistance = new Vector2(1f * s, 1f * s);

            RectTransform rt = GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = size;

            // 2. 标题微标 (左上)
            if (!string.IsNullOrEmpty(title))
            {
                int titleFontSize = Mathf.Max(6, Mathf.RoundToInt(size.y * 0.28f));
                TitleLabel = UIFactory.CreateText(transform, "Title", title, titleFontSize, TextAnchor.UpperLeft,
                    WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                RectTransform titleRt = TitleLabel.GetComponent<RectTransform>();
                titleRt.anchorMin = new Vector2(0f, 0.5f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.offsetMin = new Vector2(4f * s, 0f);
                titleRt.offsetMax = new Vector2(-4f * s, -2f * s);
            }

            // 3. 中央主数值 (大字等宽)
            int valFontSize = Mathf.Max(8, Mathf.RoundToInt(size.y * 0.48f));
            ValueText = UIFactory.CreateText(transform, "Value", UIFactory.FormatTabular(initialValue ?? "--"), valFontSize,
                TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            ValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = ValueText.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0f, 0f);
            valRt.anchorMax = new Vector2(0.78f, TitleLabel != null ? 0.65f : 1f);
            valRt.offsetMin = new Vector2(4f * s, 2f * s);
            valRt.offsetMax = new Vector2(0f, 0f);

            // 4. 单位角标 (右下)
            if (!string.IsNullOrEmpty(unit))
            {
                int unitFontSize = Mathf.Max(6, Mathf.RoundToInt(size.y * 0.26f));
                UnitLabel = UIFactory.CreateText(transform, "Unit", unit, unitFontSize, TextAnchor.LowerRight,
                    theme.AccentSecondary);
                RectTransform unitRt = UnitLabel.GetComponent<RectTransform>();
                unitRt.anchorMin = new Vector2(0.72f, 0f);
                unitRt.anchorMax = new Vector2(1f, 0.65f);
                unitRt.offsetMin = new Vector2(0f, 2f * s);
                unitRt.offsetMax = new Vector2(-4f * s, 0f);
            }

            _lastRawValue = initialValue;
            _lastUnit = unit;

            ApplyTheme(theme);
        }

        public void SetValue(string valueText, string unit = null)
        {
            if (valueText != _lastRawValue && ValueText != null)
            {
                _lastRawValue = valueText;
                BaseFlightWidget.SetTextIfChanged(ValueText, UIFactory.FormatTabular(valueText));
            }

            if (unit != null && unit != _lastUnit && UnitLabel != null)
            {
                _lastUnit = unit;
                BaseFlightWidget.SetTextIfChanged(UnitLabel, unit);
            }
        }

        public void SetValue(double val, string format = "N1", string unit = null)
        {
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                SetValue("--", unit);
            }
            else
            {
                string str = CacheManager.Instance.FastDouble(gameObject.name + "_val", val, format, 0.05);
                SetValue(str, unit);
            }
        }

        public void SetRole(TextStyleRole role)
        {
            if (_currentRole != role)
            {
                _currentRole = role;
                ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
                if (ValueText != null && theme != null)
                {
                    ValueText.color = WidgetStyleManager.Instance.GetTextColor(role, theme);
                }
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            if (BackgroundImage != null)
            {
                BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }

            if (BorderOutline != null)
            {
                BorderOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            if (TitleLabel != null)
            {
                TitleLabel.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
            }

            if (ValueText != null)
            {
                ValueText.color = WidgetStyleManager.Instance.GetTextColor(_currentRole, theme);
            }

            if (UnitLabel != null)
            {
                UnitLabel.color = theme.AccentSecondary;
            }
        }
    }
    #endregion

    #region AvionicsAnnunciator
    public enum AnnunciatorState
    {
        Off,
        Active,
        Caution,
        Warning
    }

    /// <summary>
    /// 航电光字牌与状态告警指示器 (Avionics Annunciator / Korry Light)
    /// 统一治理座舱光字牌、告警警报（Master Caution / Warning）与状态指示灯：
    /// 1. 四态光电渲染（灭灯、就绪激活、琥珀色注意、红色危险）；
    /// 2. 具备全局座舱时钟锁相同步闪烁 (Synchronized Blinker)，杜绝各自乱闪造成的视觉混乱；
    /// 3. 支持点击消警 (Acknowledge) 回调；
    /// 4. 严格遵循 0 颜色字面量标准。
    /// </summary>
    public class AvionicsAnnunciator : MonoBehaviour
    {
        public Image BackgroundImage;
        public Outline BorderOutline;
        public Text TitleLabel;
        public Button ClickButton;

        public UnityAction OnAcknowledge;

        private AnnunciatorState _state = AnnunciatorState.Off;
        public AnnunciatorState State => _state;

        private bool _blinkEnabled = false;
        private bool _isBlinkPhaseOn = true;

        // 全局座舱指示灯同步时钟 (Master Cockpit Synchronizer)
        private static float _globalClock = 0f;
        private static bool _globalBlink1Hz = true;
        private static bool _globalBlink2Hz = true;

        public static void UpdateGlobalCockpitClock(float dt)
        {
            _globalClock += dt;
            _globalBlink1Hz = ((int)(_globalClock * 2f) % 2) == 0;
            _globalBlink2Hz = ((int)(_globalClock * 4f) % 2) == 0;
        }

        public void Initialize(string label, Vector2 size, AnnunciatorState initialState = AnnunciatorState.Off,
            bool blinkOnAlert = true, UnityAction onClick = null, ThemeConfig theme = null)
        {
            _state = initialState;
            _blinkEnabled = blinkOnAlert;
            OnAcknowledge = onClick;

            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = UIFactory.GetScreenDpiScale();

            BackgroundImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            BorderOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            BorderOutline.effectDistance = new Vector2(1f * s, 1f * s);

            if (onClick != null)
            {
                ClickButton = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
                ClickButton.transition = Selectable.Transition.None;
                ClickButton.onClick.AddListener(onClick);
            }

            int fontSize = Mathf.Max(6, Mathf.RoundToInt(size.y * 0.42f));
            TitleLabel = UIFactory.CreateText(transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                theme.TextPrimaryColor);
            TitleLabel.fontStyle = FontStyle.Bold;

            RectTransform textRt = TitleLabel.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            ApplyTheme(theme);
            RefreshVisual();
        }

        public void SetState(AnnunciatorState newState)
        {
            if (_state != newState)
            {
                _state = newState;
                RefreshVisual();
            }
        }

        public void SetLabel(string label)
        {
            if (TitleLabel != null) TitleLabel.text = label;
        }

        private void Update()
        {
            UpdateGlobalCockpitClock(Time.unscaledDeltaTime);

            if (_blinkEnabled && (_state == AnnunciatorState.Caution || _state == AnnunciatorState.Warning))
            {
                bool targetBlink = (_state == AnnunciatorState.Warning) ? _globalBlink2Hz : _globalBlink1Hz;
                if (_isBlinkPhaseOn != targetBlink)
                {
                    _isBlinkPhaseOn = targetBlink;
                    RefreshVisual();
                }
            }
        }

        private void RefreshVisual()
        {
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
            if (theme == null) return;

            Color targetBg;
            Color targetBorder;
            Color targetText;

            switch (_state)
            {
                case AnnunciatorState.Active:
                    targetBg = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Success, theme);
                    targetBorder = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.90f);
                    targetText = theme.AccentPrimary;
                    break;

                case AnnunciatorState.Caution:
                    if (_blinkEnabled && !_isBlinkPhaseOn)
                    {
                        targetBg = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                        targetBorder = WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Faint);
                        targetText = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.45f);
                    }
                    else
                    {
                        targetBg = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme);
                        targetBorder = theme.WarningColor;
                        targetText = theme.WarningColor;
                    }
                    break;

                case AnnunciatorState.Warning:
                    if (_blinkEnabled && !_isBlinkPhaseOn)
                    {
                        targetBg = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                        targetBorder = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Faint);
                        targetText = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.45f);
                    }
                    else
                    {
                        targetBg = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger, theme);
                        targetBorder = theme.DangerColor;
                        targetText = theme.DangerColor;
                    }
                    break;

                case AnnunciatorState.Off:
                default:
                    targetBg = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    targetBorder = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    targetText = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    break;
            }

            if (BackgroundImage != null) BackgroundImage.color = targetBg;
            if (BorderOutline != null) BorderOutline.effectColor = targetBorder;
            if (TitleLabel != null) TitleLabel.color = targetText;
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            RefreshVisual();
        }
    }
    #endregion

    #region AvionicsSegmentedControl
    /// <summary>
    /// 航电多段模式选择胶囊 (Avionics Segmented Control)
    /// 统一治理全系统多态单选切换（参考系 SURFACE/ORBIT/TARGET、时钟 MET/UT、SAS模式选择等）。
    /// 内置互斥状态机、高光胶囊选区与零色面字面量主题联动。
    /// </summary>
    public class AvionicsSegmentedControl : MonoBehaviour
    {
        public Action<int, string> OnSelectionChanged;

        private Image _frameBg;
        private Outline _frameOutline;

        private readonly List<Button> _segmentButtons = new List<Button>();
        private readonly List<Text> _segmentLabels = new List<Text>();
        private readonly List<string> _segmentTexts = new List<string>();

        private int _selectedIndex = 0;
        public int SelectedIndex => _selectedIndex;

        public string SelectedOption => (_selectedIndex >= 0 && _selectedIndex < _segmentTexts.Count)
            ? _segmentTexts[_selectedIndex]
            : null;

        public void Initialize(string[] options, int defaultIndex, Vector2 totalSize, Action<int, string> onSelect, ThemeConfig theme = null)
        {
            OnSelectionChanged = onSelect;
            _selectedIndex = Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, options.Length - 1));

            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = UIFactory.GetScreenDpiScale();

            // 1. 外层内凹胶囊槽
            _frameBg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            _frameBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            _frameOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            Color borderCol = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);
            _frameOutline.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Ghost);
            _frameOutline.effectDistance = new Vector2(1f * s, 1f * s);

            var hlg = gameObject.GetComponent<HorizontalLayoutGroup>() ?? gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(1, 1, 1, 1);
            hlg.spacing = 1f * s;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            // 2. 构建分段子按键
            _segmentButtons.Clear();
            _segmentLabels.Clear();
            _segmentTexts.Clear();

            int count = options.Length;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string label = options[i];
                _segmentTexts.Add(label);

                GameObject segGo = new GameObject($"Segment_{i}_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
                segGo.transform.SetParent(transform, false);

                Button btn = segGo.GetComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SetSelectedIndex(index, true));

                Image segImg = segGo.GetComponent<Image>();
                Outline segOutline = segGo.AddComponent<Outline>();
                segOutline.effectDistance = new Vector2(1f * s, 1f * s);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(totalSize.y * 0.44f));
                Text segText = UIFactory.CreateText(segGo.transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                    theme.TextPrimaryColor);
                segText.fontStyle = FontStyle.Bold;

                RectTransform textRt = segText.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;

                var fb = segGo.AddComponent<AvionicsButtonFeedback>();
                fb.Initialize(btn, segImg, segOutline, segText, theme);

                _segmentButtons.Add(btn);
                _segmentLabels.Add(segText);
            }

            ApplyTheme(theme);
            RefreshSelectionVisual();
        }

        public void SetSelectedIndex(int index, bool triggerCallback = true)
        {
            if (index < 0 || index >= _segmentButtons.Count) return;
            if (_selectedIndex != index)
            {
                _selectedIndex = index;
                RefreshSelectionVisual();
                if (triggerCallback)
                {
                    OnSelectionChanged?.Invoke(_selectedIndex, _segmentTexts[_selectedIndex]);
                }
            }
        }

        public void SetSelectedOption(string option, bool triggerCallback = true)
        {
            for (int i = 0; i < _segmentTexts.Count; i++)
            {
                if (string.Equals(_segmentTexts[i], option, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedIndex(i, triggerCallback);
                    return;
                }
            }
        }

        private void RefreshSelectionVisual()
        {
            for (int i = 0; i < _segmentButtons.Count; i++)
            {
                bool isSelected = (i == _selectedIndex);
                _segmentButtons[i].SetToggleActive(isSelected);
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            if (_frameBg != null)
            {
                _frameBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }

            if (_frameOutline != null)
            {
                _frameOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            for (int i = 0; i < _segmentButtons.Count; i++)
            {
                var fb = _segmentButtons[i].GetComponent<AvionicsButtonFeedback>();
                if (fb != null) fb.ApplyTheme(theme);
            }

            RefreshSelectionVisual();
        }
    }
    #endregion

    #region AvionicsButtonFeedback
    /// <summary>
    /// 航电标准化按钮交互状态机与高光反馈中枢 (Avionics Button Feedback)
    /// 统一治理全系统按钮的可操作性感知 (Affordance)、悬停高亮 (Hover)、按压微凹 (Pressed) 与激活态 (Toggle)。
    /// 彻底废除“各个小组件各自写一套 Hover 颜色与监听”的散落实现，所有组件经由 UIFactory 自动获得航电级交互反馈！
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class AvionicsButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Image BackgroundImage;
        public Outline BorderOutline;
        public Text LabelText;
        public Image LedIndicator;
        public Button Button;
        public ButtonVisualRole VisualRole = ButtonVisualRole.Normal;
        public bool IsToggleMode = false;
        public Color? CustomActiveColor = null;
        public Color? CustomInactiveColor = null;

        private bool _isToggleActive = false;
        public bool IsToggleActive
        {
            get => _isToggleActive;
            set
            {
                if (_isToggleActive != value)
                {
                    _isToggleActive = value;
                    RefreshVisualState();
                }
            }
        }

        private bool _isHovered = false;
        private bool _isPressed = false;

        // 缓存当前主题色彩
        private Color _normalBg;
        private Color _hoverBg;
        private Color _pressedBg;
        private Color _activeBg;

        private Color _normalOutline;
        private Color _hoverOutline;
        private Color _pressedOutline;
        private Color _activeOutline;

        private Color _normalText;
        private Color _hoverText;
        private Color _pressedText;
        private Color _activeText;

        private Color _normalLed;
        private Color _activeLed;

        private bool _isColorsInitialized = false;

        private void Awake()
        {
            if (Button == null) Button = GetComponent<Button>();
            if (BackgroundImage == null) BackgroundImage = GetComponent<Image>();
            if (BorderOutline == null) BorderOutline = GetComponent<Outline>();
            if (LabelText == null) LabelText = GetComponentInChildren<Text>();

            // 禁用 Unity 默认的浑浊 ColorTint，交由本交互状态机全权精确渲染
            if (Button != null)
            {
                Button.transition = Selectable.Transition.None;
            }
        }

        public void Initialize(Button btn, Image bg, Outline outline, Text label, ThemeConfig theme, Image led = null)
        {
            Button = btn;
            BackgroundImage = bg;
            BorderOutline = outline;
            LabelText = label;
            LedIndicator = led;

            if (Button != null)
            {
                Button.transition = Selectable.Transition.None;
            }

            ApplyTheme(theme);
        }

        public void SetToggleActive(bool active)
        {
            if (!IsToggleMode)
            {
                IsToggleMode = true;
                _isColorsInitialized = false;
            }
            IsToggleActive = active;
        }

        public void SetRole(ButtonVisualRole role)
        {
            if (VisualRole != role)
            {
                VisualRole = role;
                ApplyTheme(WidgetStyleManager.Instance?.CurrentTheme);
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            WidgetStyleManager style = WidgetStyleManager.Instance;
            if (style == null) return;

            Color baseBg = style.GetButtonBackgroundColor(VisualRole, false, theme);
            Color pri = CustomActiveColor ?? theme.AccentPrimary;
            Color sec = theme.AccentSecondary;

            // 1. 背景色分阶：
            // 静置态：如果是 Toggle 控件，使用沉浸暗槽底色 (Inset)，避免原版浅底与开启态混淆
            _normalBg = IsToggleMode ? WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme) : baseBg;
            _hoverBg = WidgetStyleManager.Tint(baseBg, pri, 0.28f, 0f, Mathf.Min(1.0f, baseBg.a + 0.22f));
            _pressedBg = WidgetStyleManager.Dim(baseBg, 0.35f, Mathf.Min(1.0f, baseBg.a + 0.30f));
            // 激活态：向高亮强调色 Tint 混色并赋予背光透明度，形成鲜明清晰的发光航电按键
            _activeBg = WidgetStyleManager.Tint(baseBg, pri, 0.40f, 0f, Mathf.Min(1.0f, baseBg.a + 0.25f));

            // 2. 边框发光分阶：静置幽灵微线、悬停科技蓝发光边线 (88% Alpha)、按压高亮、激活强光
            _normalOutline = WidgetStyleManager.Weighted(sec, LineWeight.Ghost);
            _hoverOutline = WidgetStyleManager.WithAlpha(pri, 0.88f);
            _pressedOutline = WidgetStyleManager.WithAlpha(pri, 1.0f);
            _activeOutline = WidgetStyleManager.WithAlpha(pri, 0.98f);

            // 3. 文字色彩分阶：
            // Toggle 关闭态：使用中度消光灰白 (0.50f alpha)，清晰易读但明确处于未点亮/待机状态，消除原版高亮纯白造成的“已激活”误解
            Color inactiveText = CustomInactiveColor ?? WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.50f);
            _normalText = IsToggleMode ? inactiveText : style.GetButtonTextColor(VisualRole, false, theme);
            _hoverText = theme.TextPrimaryColor;
            _pressedText = theme.TextPrimaryColor;
            _activeText = pri;

            // 4. LED 状态指示条色彩：
            _normalLed = WidgetStyleManager.Surface(SurfaceStyleRole.LedOff, theme);
            _activeLed = pri;

            _isColorsInitialized = true;
            RefreshVisualState();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Button != null && !Button.interactable) return;
            if (LabelText == null) LabelText = GetComponentInChildren<Text>();

            _isHovered = true;
            RefreshVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            _isPressed = false;
            RefreshVisualState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Button != null && !Button.interactable) return;
            _isPressed = true;
            RefreshVisualState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
            RefreshVisualState();
        }

        private void RefreshVisualState()
        {
            if (!_isColorsInitialized)
            {
                ApplyTheme(WidgetStyleManager.Instance?.CurrentTheme);
                if (!_isColorsInitialized) return;
            }

            bool isDisabled = Button != null && !Button.interactable;

            // 确定目标颜色
            Color targetBg;
            Color targetOutline;
            Color targetText;
            Color targetLed;

            if (isDisabled)
            {
                targetBg = WidgetStyleManager.WithAlpha(_normalBg, _normalBg.a * 0.40f);
                targetOutline = WidgetStyleManager.WithAlpha(_normalOutline, _normalOutline.a * 0.30f);
                targetText = WidgetStyleManager.WithAlpha(_normalText, _normalText.a * 0.40f);
                targetLed = WidgetStyleManager.WithAlpha(_normalLed, 0.20f);
            }
            else if (_isPressed)
            {
                if (_isToggleActive)
                {
                    targetBg = WidgetStyleManager.Dim(_activeBg, 0.80f, _activeBg.a);
                    targetOutline = _pressedOutline;
                    targetText = _activeText;
                    targetLed = _activeLed;
                }
                else
                {
                    targetBg = _pressedBg;
                    targetOutline = _pressedOutline;
                    targetText = _pressedText;
                    targetLed = _activeLed;
                }
            }
            else if (_isHovered)
            {
                if (_isToggleActive)
                {
                    targetBg = WidgetStyleManager.Tint(_activeBg, Color.white, 0.15f);
                    targetOutline = _pressedOutline;
                    targetText = WidgetStyleManager.Lighten(_activeText, 0.15f);
                    targetLed = _activeLed;
                }
                else
                {
                    targetBg = _hoverBg;
                    targetOutline = _hoverOutline;
                    targetText = _hoverText;
                    targetLed = WidgetStyleManager.Lighten(_normalLed, 0.25f);
                }
            }
            else if (_isToggleActive)
            {
                targetBg = _activeBg;
                targetOutline = _activeOutline;
                targetText = _activeText;
                targetLed = _activeLed;
            }
            else
            {
                targetBg = _normalBg;
                targetOutline = _normalOutline;
                targetText = _normalText;
                targetLed = _normalLed;
            }

            // 应用颜色
            if (BackgroundImage != null)
            {
                BackgroundImage.color = targetBg;
            }

            if (BorderOutline != null)
            {
                BorderOutline.effectColor = targetOutline;
                BorderOutline.enabled = true;
            }

            if (LabelText != null)
            {
                LabelText.color = targetText;
            }

            if (LedIndicator != null)
            {
                LedIndicator.color = targetLed;
            }
        }
    }

    /// <summary>
    /// Button 扩展方法：为现有组件提供极简 API
    /// </summary>
    public static class AvionicsButtonExtensions
    {
        public static void SetToggleActive(this Button btn, bool active)
        {
            if (btn == null) return;
            var fb = btn.GetComponent<AvionicsButtonFeedback>();
            if (fb != null) fb.SetToggleActive(active);
        }

        public static void SetVisualRole(this Button btn, ButtonVisualRole role)
        {
            if (btn == null) return;
            var fb = btn.GetComponent<AvionicsButtonFeedback>();
            if (fb != null) fb.SetRole(role);
        }

        public static AvionicsButtonFeedback GetFeedback(this Button btn)
        {
            if (btn == null) return null;
            return btn.GetComponent<AvionicsButtonFeedback>();
        }
    }
    #endregion
}
