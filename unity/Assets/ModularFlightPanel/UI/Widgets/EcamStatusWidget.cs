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
            _background.color = theme.FrameBgColor;
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
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

        private string _lastStateText = "";
        private string _lastMetricsText = "";

        public override float DefaultUpdateInterval => 0.1f; // 10Hz 状态监控刷新，消除每帧 Text 重绘

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.red;
            Color okCol = theme != null ? (Color)theme.AccentPrimary : Color.green;
            Color cautionCol = theme != null ? (Color)theme.CautionColor : Color.yellow;

            bool caution = telemetry.IsTouchdownAlert;
            string stateStr = caution ? "TERRAIN  /  PULL UP" : (telemetry.IsSASEnabled ? "SAS ACTIVE" : "MANUAL FLIGHT");
            if (stateStr != _lastStateText)
            {
                _lastStateText = stateStr;
                _state.text = stateStr;
            }

            Color stateColor = caution ? warnCol : (telemetry.IsSASEnabled ? okCol : cautionCol);
            if (_state.color != stateColor) _state.color = stateColor;
            if (_stateBar != null && _stateBar.color != stateColor) _stateBar.color = stateColor;

            if (_metrics != null)
            {
                string metricsStr = TelemetryTokenEngine.Evaluate("SPD {SPD:F1}   ALT {ALT:N0}   VSI {VSI:F1}", telemetry);
                if (metricsStr != _lastMetricsText)
                {
                    _lastMetricsText = metricsStr;
                    _metrics.text = metricsStr;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_background != null) _background.color = theme.FrameBgColor;
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_title != null) _title.color = theme.TextAccentColor;
            if (_metrics != null) _metrics.color = theme.TextPrimaryColor;
            if (_stateBar != null) _stateBar.color = theme.AccentPrimary;
        }
    }
}
