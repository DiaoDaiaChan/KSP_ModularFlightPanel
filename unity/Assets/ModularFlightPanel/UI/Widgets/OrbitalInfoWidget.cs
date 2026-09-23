using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public class OrbitalInfoWidget : BaseFlightWidget
    {
        private Text _titleText;
        private Text _apText;
        private Text _peText;
        private Image _bgImage;
        private Outline _outline;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 panelSize = new Vector2(260f * CurrentDpiScale, 54f * CurrentDpiScale);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, CurrentDpiScale);

            int infoFontSize = Mathf.RoundToInt(11f * CurrentDpiScale);
            int titleFontSize = Mathf.RoundToInt(9f * CurrentDpiScale);

            _titleText = UIFactory.CreateText(transform, "Title_Text", "ORBITAL PARAMETERS", titleFontSize, TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(240f * CurrentDpiScale, 14f * CurrentDpiScale);
            titRt.anchoredPosition = new Vector2(0f, 16f * CurrentDpiScale);

            _apText = UIFactory.CreateText(transform, "AP_Text", "AP 0 m in T-00:00:00", infoFontSize, TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform apRt = _apText.GetComponent<RectTransform>();
            apRt.sizeDelta = new Vector2(240f * CurrentDpiScale, 16f * CurrentDpiScale);
            apRt.anchoredPosition = new Vector2(0f, 0f);

            _peText = UIFactory.CreateText(transform, "PE_Text", "PE 0 m in T-00:00:00", infoFontSize, TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform peRt = _peText.GetComponent<RectTransform>();
            peRt.sizeDelta = new Vector2(240f * CurrentDpiScale, 16f * CurrentDpiScale);
            peRt.anchoredPosition = new Vector2(0f, -16f * CurrentDpiScale);
        }

        private string _lastApText = "";
        private string _lastPeText = "";

        public override float DefaultUpdateInterval => 0.2f; // 轨道参数 5Hz 刷新足以满足目视需求，降低 80% 字符串分配

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            if (_apText != null)
            {
                string newAp = TelemetryTokenEngine.Evaluate("AP {AP:DIST} in T-{TAP}", telemetry);
                if (newAp != _lastApText)
                {
                    _lastApText = newAp;
                    _apText.text = newAp;
                }
            }
            if (_peText != null)
            {
                string newPe = TelemetryTokenEngine.Evaluate("PE {PE:DIST} in T-{TPE}", telemetry);
                if (newPe != _lastPeText)
                {
                    _lastPeText = newPe;
                    _peText.text = newPe;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = theme.FrameBgColor;
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.AccentSecondary;
            if (_apText != null) _apText.color = theme.TextPrimaryColor;
            if (_peText != null) _peText.color = theme.TextPrimaryColor;
        }
    }
}
