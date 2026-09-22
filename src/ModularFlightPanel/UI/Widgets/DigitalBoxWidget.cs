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
            Vector2 boxSize = new Vector2(104f * CurrentDpiScale, 42f * CurrentDpiScale);
            RectTransform.sizeDelta = boxSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.96f);

            Button btn = gameObject.AddComponent<Button>();
            btn.onClick.AddListener(OnBoxClicked);

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = (_type == DigitalBoxType.Speed) ? theme.WarningColor : theme.AccentMagenta;
            _outline.effectDistance = new Vector2(1.2f * CurrentDpiScale, 1.2f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, CurrentDpiScale);

            int modeFontSize = Mathf.Max(7, Mathf.RoundToInt(8.5f * CurrentDpiScale));
            _modeLabelText = UIFactory.CreateText(transform, "Mode_Label", "", modeFontSize, TextAnchor.UpperLeft, theme.WarningColor);
            RectTransform modeRt = _modeLabelText.GetComponent<RectTransform>();
            modeRt.sizeDelta = new Vector2(boxSize.x - 6f * CurrentDpiScale, 13f * CurrentDpiScale);
            modeRt.anchoredPosition = new Vector2(4f * CurrentDpiScale, (boxSize.y * 0.5f) - 7.5f * CurrentDpiScale);

            int valFontSize = Mathf.Max(10, Mathf.RoundToInt(16f * CurrentDpiScale));
            _valueText = UIFactory.CreateText(transform, "Value_Text", "0.0", valFontSize, TextAnchor.LowerRight, theme.TextPrimaryColor);
            RectTransform valRt = _valueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxSize.x - 6f * CurrentDpiScale, 20f * CurrentDpiScale);
            valRt.anchoredPosition = new Vector2(-4f * CurrentDpiScale, -(boxSize.y * 0.5f) + 10f * CurrentDpiScale);
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
            if (_bgImage != null) _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.96f);
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
