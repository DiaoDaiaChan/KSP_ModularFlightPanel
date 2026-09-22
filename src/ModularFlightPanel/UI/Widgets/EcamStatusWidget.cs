using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>Compact ECAM-style flight status strip: readable at a glance without covering the navball.</summary>
    public class EcamStatusWidget : BaseFlightWidget
    {
        private Text _title;
        private Text _state;
        private Text _metrics;
        private Image _stateBar;
        private Image _background;
        private Outline _outline;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 size = new Vector2(270f * CurrentDpiScale, 58f * CurrentDpiScale);
            RectTransform.sizeDelta = size;
            _background = gameObject.AddComponent<Image>();
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f * CurrentDpiScale, 1f * CurrentDpiScale);

            GameObject bar = UIFactory.CreatePanel(transform, "State_Bar", new Vector2(4f, size.y),
                new Vector2(-size.x * 0.5f + 2f, 0f), theme.AccentPrimary);
            _stateBar = bar.GetComponent<Image>();

            _title = UIFactory.CreateText(transform, "Title", "FLIGHT STATUS", 10, TextAnchor.UpperLeft, theme.TextAccentColor);
            RectTransform titleRt = _title.rectTransform;
            titleRt.sizeDelta = new Vector2(size.x - 20f, 16f);
            titleRt.anchoredPosition = new Vector2(12f, size.y * 0.5f - 12f);

            _state = UIFactory.CreateText(transform, "State", "SYSTEMS NOMINAL", 14, TextAnchor.MiddleLeft, theme.AccentPrimary);
            RectTransform stateRt = _state.rectTransform;
            stateRt.sizeDelta = new Vector2(size.x - 20f, 20f);
            stateRt.anchoredPosition = new Vector2(12f, 4f);

            _metrics = UIFactory.CreateText(transform, "Metrics", "SPD 000.0   ALT 0000   VSI +0.0", 9, TextAnchor.LowerLeft, theme.TextPrimaryColor);
            RectTransform metricsRt = _metrics.rectTransform;
            metricsRt.sizeDelta = new Vector2(size.x - 20f, 14f);
            metricsRt.anchoredPosition = new Vector2(12f, -size.y * 0.5f + 9f);
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            bool caution = telemetry.IsTouchdownAlert;
            _state.text = caution ? "TERRAIN  /  PULL UP" : (telemetry.IsSASEnabled ? "SAS ACTIVE" : "MANUAL FLIGHT");
            _state.color = caution ? Color.red : (telemetry.IsSASEnabled ? Color.green : Color.yellow);
            _stateBar.color = _state.color;
            _metrics.text = string.Format("SPD {0,6:0.0}   ALT {1,6:0}   VSI {2:+0.0;-0.0;0.0}",
                telemetry.CurrentSpeed, telemetry.DisplayAltitude, telemetry.VerticalSpeed);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            Color frame = theme.FrameBgColor.ToColor();
            if (_background != null) _background.color = new Color(frame.r * 0.55f, frame.g * 0.55f, frame.b * 0.55f, 0.92f);
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_title != null) _title.color = theme.TextAccentColor;
            if (_metrics != null) _metrics.color = theme.TextPrimaryColor;
            if (_stateBar != null) _stateBar.color = theme.AccentPrimary;
        }
    }
}
