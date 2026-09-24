using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
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
        public Button Button;
        public ButtonVisualRole VisualRole = ButtonVisualRole.Normal;

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

        public void Initialize(Button btn, Image bg, Outline outline, Text label, ThemeConfig theme)
        {
            Button = btn;
            BackgroundImage = bg;
            BorderOutline = outline;
            LabelText = label;

            if (Button != null)
            {
                Button.transition = Selectable.Transition.None;
            }

            ApplyTheme(theme);
        }

        public void SetToggleActive(bool active)
        {
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
            Color pri = theme.AccentPrimary;
            Color sec = theme.AccentSecondary;

            // 1. 背景色分阶：静置态、悬停高光（提亮25%并融入主强调色）、按压微凹（加深变暗）、激活亮灯
            _normalBg = baseBg;
            _hoverBg = WidgetStyleManager.Tint(baseBg, pri, 0.28f, 0f, Mathf.Min(1.0f, baseBg.a + 0.22f));
            _pressedBg = WidgetStyleManager.Dim(baseBg, 0.35f, Mathf.Min(1.0f, baseBg.a + 0.30f));
            _activeBg = style.GetButtonBackgroundColor(ButtonVisualRole.ActiveToggle, false, theme);

            // 2. 边框发光分阶：静置幽灵微线、悬停科技蓝发光边线 (88% Alpha)、按压高亮、激活强光
            _normalOutline = WidgetStyleManager.Weighted(sec, LineWeight.Ghost);
            _hoverOutline = WidgetStyleManager.WithAlpha(pri, 0.88f);
            _pressedOutline = WidgetStyleManager.WithAlpha(pri, 1.0f);
            _activeOutline = WidgetStyleManager.WithAlpha(pri, 0.95f);

            // 3. 文字色彩分阶：
            _normalText = style.GetButtonTextColor(VisualRole, false, theme);
            _hoverText = theme.TextPrimaryColor;
            _pressedText = theme.TextPrimaryColor;
            _activeText = style.GetButtonTextColor(ButtonVisualRole.ActiveToggle, false, theme);

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

            if (isDisabled)
            {
                targetBg = WidgetStyleManager.WithAlpha(_normalBg, _normalBg.a * 0.40f);
                targetOutline = WidgetStyleManager.WithAlpha(_normalOutline, _normalOutline.a * 0.30f);
                targetText = WidgetStyleManager.WithAlpha(_normalText, _normalText.a * 0.40f);
            }
            else if (_isPressed)
            {
                targetBg = _pressedBg;
                targetOutline = _pressedOutline;
                targetText = _pressedText;
            }
            else if (_isHovered)
            {
                targetBg = _hoverBg;
                targetOutline = _hoverOutline;
                targetText = _hoverText;
            }
            else if (_isToggleActive)
            {
                targetBg = _activeBg;
                targetOutline = _activeOutline;
                targetText = _activeText;
            }
            else
            {
                targetBg = _normalBg;
                targetOutline = _normalOutline;
                targetText = _normalText;
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
}
