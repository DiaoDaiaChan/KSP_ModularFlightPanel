using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>Compact ECAM-style flight status strip: readable at a glance without covering the navball.</summary>
    [FlightWidget("ecam_status", "status_memo", Category = WidgetCategory.Systems, DisplayName = "ECAM 飞行状态备忘录", Description = "单行状态备忘横条：飞行阶段徽标、关键警告速览与系统就绪摘要。", DefaultWidgetId = "core.ecam_status", DefaultX = 0f, DefaultY = 80f, IsSingleton = true, ExactIds = new[] { "core.ecam_status" })]
    public class EcamStatusWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Text _title;
        private Text _state;
        private Text _metrics;
        private Image _stateBar;
        private Image _background;
        private Outline _outline;

        private string _lastStateText = "";
        private string _lastMetricsText = "";
        private int _lastVisualState = -1;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 size = new Vector2(270f * s, 58f * s);
            RectTransform.sizeDelta = size;

            _background = gameObject.AddComponent<Image>();
            _background.color = theme.FrameBgColor;
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1f * s, 1f * s);

            GameObject bar = UIFactory.CreatePanel(transform, "State_Bar", new Vector2(4f * s, size.y),
                new Vector2(-size.x * 0.5f + 2f * s, 0f), theme.AccentPrimary);
            _stateBar = bar.GetComponent<Image>();

            string titleStr = GetTemplateChannel("TITLE", "FLIGHT STATUS");
            _title = UIFactory.CreateText(transform, "Title", titleStr, Mathf.RoundToInt(10f * s), TextAnchor.UpperLeft, theme.TextAccentColor);
            RectTransform titleRt = _title.rectTransform;
            titleRt.sizeDelta = new Vector2(size.x - 20f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(12f * s, size.y * 0.5f - 12f * s);

            _state = UIFactory.CreateText(transform, "State", "SYSTEMS NOMINAL", Mathf.RoundToInt(14f * s), TextAnchor.MiddleLeft, theme.AccentPrimary);
            RectTransform stateRt = _state.rectTransform;
            stateRt.sizeDelta = new Vector2(size.x - 20f * s, 20f * s);
            stateRt.anchoredPosition = new Vector2(12f * s, 4f * s);

            _metrics = UIFactory.CreateText(transform, "Metrics", "SPD 000.0   ALT 0000   VSI +0.0", Mathf.RoundToInt(9f * s), TextAnchor.LowerLeft, theme.TextPrimaryColor);
            RectTransform metricsRt = _metrics.rectTransform;
            metricsRt.sizeDelta = new Vector2(size.x - 20f * s, 14f * s);
            metricsRt.anchoredPosition = new Vector2(12f * s, -size.y * 0.5f + 9f * s);

            ApplyTheme(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            bool caution = telemetry.IsTouchdownAlert;
            int visualState = caution ? 2 : (telemetry.IsSASEnabled ? 1 : 0);

            string cautionText = GetTemplateChannel("CAUTION_TEXT", "TERRAIN  /  PULL UP");
            string sasText = GetTemplateChannel("SAS_TEXT", "SAS ACTIVE");
            string manualText = GetTemplateChannel("MANUAL_TEXT", "MANUAL FLIGHT");
            string stateStr = caution ? cautionText : (telemetry.IsSASEnabled ? sasText : manualText);

            if (stateStr != _lastStateText)
            {
                _lastStateText = stateStr;
                _state.text = stateStr;
            }

            if (visualState != _lastVisualState)
            {
                _lastVisualState = visualState;
                TextStyleRole textRole = caution ? TextStyleRole.Danger : (telemetry.IsSASEnabled ? TextStyleRole.Accent : TextStyleRole.Warning);
                ApplyText(_state, textRole, theme);

                MeterStyleRole barRole = caution ? MeterStyleRole.Danger : (telemetry.IsSASEnabled ? MeterStyleRole.Primary : MeterStyleRole.Warning);
                if (_stateBar != null)
                {
                    _stateBar.color = WidgetStyleManager.Meter(barRole, theme);
                }
            }

            if (_metrics != null)
            {
                string metricsTemplate = GetTemplateChannel("METRICS", "SPD {SPD:F1}   ALT {ALT:N0}   VSI {VSI:F1}");
                string metricsStr = TelemetryTokenEngine.Evaluate(metricsTemplate, telemetry);
                if (metricsStr != _lastMetricsText)
                {
                    _lastMetricsText = metricsStr;
                    _metrics.text = metricsStr;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            ApplyCard(_background, _outline, CardStyleRole.Normal, theme);
            ApplyText(_title, TextStyleRole.Label, theme);
            ApplyText(_state, TextStyleRole.Accent, theme);
            ApplyText(_metrics, TextStyleRole.SecondaryValue, theme);

            if (_stateBar != null) _stateBar.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
