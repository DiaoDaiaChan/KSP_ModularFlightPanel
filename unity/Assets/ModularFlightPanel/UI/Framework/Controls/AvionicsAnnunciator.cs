using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
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
}
