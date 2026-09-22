using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public enum DigitalBoxType
    {
        Speed,
        Altitude
    }

    public class DigitalBoxWidget : BaseFlightWidget
    {
        private DigitalBoxType _type;
        private Text _modeLabelText;
        private Text _valueText;
        private Image _bgImage;
        private Outline _outline;

        public void SetupType(DigitalBoxType type)
        {
            _type = type;
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 boxSize = new Vector2(105f * CurrentDpiScale, 46f * CurrentDpiScale);
            RectTransform.sizeDelta = boxSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            Button btn = gameObject.AddComponent<Button>();
            btn.onClick.AddListener(OnBoxClicked);

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = (_type == DigitalBoxType.Speed) ? theme.WarningColor : theme.AccentMagenta;
            _outline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);

            int modeFontSize = Mathf.RoundToInt(10f * CurrentDpiScale);
            _modeLabelText = UIFactory.CreateText(transform, "Mode_Label", "", modeFontSize, TextAnchor.UpperLeft, theme.WarningColor);
            RectTransform modeRt = _modeLabelText.GetComponent<RectTransform>();
            modeRt.sizeDelta = new Vector2(boxSize.x - 8f * CurrentDpiScale, 16f * CurrentDpiScale);
            modeRt.anchoredPosition = new Vector2(4f * CurrentDpiScale, (boxSize.y * 0.5f) - 10f * CurrentDpiScale);

            int valFontSize = Mathf.RoundToInt(15f * CurrentDpiScale);
            _valueText = UIFactory.CreateText(transform, "Value_Text", "0.0", valFontSize, TextAnchor.LowerRight, theme.TextPrimaryColor);
            RectTransform valRt = _valueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxSize.x - 8f * CurrentDpiScale, 26f * CurrentDpiScale);
            valRt.anchoredPosition = new Vector2(-4f * CurrentDpiScale, -(boxSize.y * 0.5f) + 13f * CurrentDpiScale);
        }

        private void OnBoxClicked()
        {
            if (TelemetryHub.Instance == null) return;

            if (_type == DigitalBoxType.Speed)
            {
                TelemetryHub.Instance.CycleSpeedMode();
            }
            else
            {
                TelemetryHub.Instance.CycleAltitudeMode();
            }
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            if (_type == DigitalBoxType.Speed)
            {
                string modeStr = telemetry.CurrentSpeedMode.ToString().ToUpper();
                _modeLabelText.text = $"{modeStr}";

                double spd = telemetry.CurrentSpeed;
                _valueText.text = spd > 9999.0 ? $"{spd / 1000.0:F1}k m/s" : $"{spd:F1} m/s";
            }
            else
            {
                string altModeStr = telemetry.CurrentAltMode == AltitudeDisplayMode.Ground ? "GROUND" : "SEA LEVEL";
                _modeLabelText.text = altModeStr;

                double alt = telemetry.DisplayAltitude;
                _valueText.text = alt > 99999.0 ? $"{alt / 1000.0:F1}k m" : $"{Mathf.RoundToInt((float)alt)} m";

                if (telemetry.IsTouchdownAlert)
                {
                    float flash = Mathf.PingPong(Time.time * 4f, 1f);
                    _outline.effectColor = Color.Lerp(Color.red, Color.yellow, flash);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = theme.FrameBgColor;
            if (_valueText != null) _valueText.color = theme.TextPrimaryColor;

            if (_type == DigitalBoxType.Speed)
            {
                if (_outline != null) _outline.effectColor = theme.WarningColor;
                if (_modeLabelText != null) _modeLabelText.color = theme.WarningColor;
            }
            else
            {
                if (_outline != null) _outline.effectColor = theme.AccentMagenta;
                if (_modeLabelText != null) _modeLabelText.color = theme.AccentMagenta;
            }
        }
    }
}
