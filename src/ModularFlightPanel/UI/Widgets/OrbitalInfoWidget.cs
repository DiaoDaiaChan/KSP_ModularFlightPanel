using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public class OrbitalInfoWidget : BaseFlightWidget
    {
        private Text _apText;
        private Text _peText;
        private Text _titleText;
        private Image _bgImage;
        private Outline _outline;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 panelSize = new Vector2(260f * CurrentDpiScale, 54f * CurrentDpiScale);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.96f);

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, CurrentDpiScale);

            int infoFontSize = Mathf.RoundToInt(11f * CurrentDpiScale);
            int titleFontSize = Mathf.RoundToInt(9f * CurrentDpiScale);

            _apText = UIFactory.CreateText(transform, "AP_Text", "AP 0 m in T-00:00:00", infoFontSize, TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform apRt = _apText.GetComponent<RectTransform>();
            apRt.sizeDelta = new Vector2(panelSize.x - 12f * CurrentDpiScale, 16f * CurrentDpiScale);
            apRt.anchoredPosition = new Vector2(6f * CurrentDpiScale, 10f * CurrentDpiScale);

            _peText = UIFactory.CreateText(transform, "PE_Text", "PE 0 m in T-00:00:00", infoFontSize, TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform peRt = _peText.GetComponent<RectTransform>();
            peRt.sizeDelta = new Vector2(panelSize.x - 12f * CurrentDpiScale, 16f * CurrentDpiScale);
            peRt.anchoredPosition = new Vector2(6f * CurrentDpiScale, -6f * CurrentDpiScale);

            _titleText = UIFactory.CreateText(transform, "Title_Text", "ORBITAL.INFO", titleFontSize, TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(panelSize.x, 12f * CurrentDpiScale);
            titleRt.anchoredPosition = new Vector2(0f, -(panelSize.y * 0.5f) - 6f * CurrentDpiScale);
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            double ap = telemetry.Apoapsis;
            double tAp = Math.Max(0.0, telemetry.TimeToAp);
            _apText.text = $"<color=#00E5FF>AP</color> {FormatDist(ap)}  in T-{FormatTime(tAp)}";

            double pe = telemetry.Periapsis;
            double tPe = Math.Max(0.0, telemetry.TimeToPe);
            _peText.text = $"<color=#00E5FF>PE</color> {FormatDist(pe)}  in T-{FormatTime(tPe)}";
        }

        private string FormatDist(double meters)
        {
            if (Math.Abs(meters) >= 1000000.0)
                return $"{meters / 1000000.0:F2}M m";
            if (Math.Abs(meters) >= 10000.0)
                return $"{meters / 1000.0:F1}k m";
            return $"{meters:N0} m";
        }

        private string FormatTime(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1.0)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.96f);
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.AccentSecondary;
            if (_apText != null) _apText.color = theme.TextPrimaryColor;
            if (_peText != null) _peText.color = theme.TextPrimaryColor;
        }
    }
}
