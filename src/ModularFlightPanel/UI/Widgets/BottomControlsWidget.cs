using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public class BottomControlsWidget : MonoBehaviour
    {
        private Button _rcsBtn;
        private Image _rcsImg;
        private Outline _rcsOutline;
        private Text _rcsText;

        private Button _sasBtn;
        private Image _sasImg;
        private Outline _sasOutline;
        private Text _sasText;
        private Button _progradeBtn;
        private Button _retrogradeBtn;
        private Text _modeText;
        private Image _panelImage;
        private Outline _panelOutline;

        public void Initialize(Transform parent, LayoutConfig layout, ThemeConfig theme, float scale)
        {
            float dpiScale = UIFactory.GetScreenDpiScale() * scale;
            Vector2 btnSize = new Vector2(54f * dpiScale, 28f * dpiScale);
            float yPos = -layout.SphereRadius * dpiScale - 22f * dpiScale;

            GameObject panel = UIFactory.CreatePanel(parent, "FlightControlPanel", new Vector2(210f * dpiScale, 50f * dpiScale),
                new Vector2(0f, yPos - 18f * dpiScale), new Color(0.02f, 0.04f, 0.06f, 0.94f), theme.FrameBorderColor, 1f * dpiScale);
            _panelImage = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();
            _modeText = UIFactory.CreateText(panel.transform, "ControlMode", "FLIGHT CONTROLS", Mathf.RoundToInt(8f * dpiScale), TextAnchor.UpperCenter, theme.TextAccentColor);
            _modeText.rectTransform.sizeDelta = new Vector2(190f * dpiScale, 14f * dpiScale);
            _modeText.rectTransform.anchoredPosition = new Vector2(0f, 16f * dpiScale);

            Vector2 rcsPos = new Vector2(-68f * dpiScale, yPos);
            _rcsBtn = UIFactory.CreateButton(parent, "RCS_Button", btnSize, rcsPos, OnRCSToggle);
            _rcsImg = _rcsBtn.GetComponent<Image>();
            _rcsOutline = _rcsBtn.gameObject.AddComponent<Outline>();
            _rcsOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            int fontSize = Mathf.RoundToInt(11f * dpiScale);
            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "RCS_Text", "RCS", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = btnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            Vector2 sasPos = new Vector2(-10f * dpiScale, yPos);
            _sasBtn = UIFactory.CreateButton(parent, "SAS_Button", btnSize, sasPos, OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasOutline = _sasBtn.gameObject.AddComponent<Outline>();
            _sasOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _sasText = UIFactory.CreateText(_sasBtn.transform, "SAS_Text", "SAS", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform sasRt = _sasText.GetComponent<RectTransform>();
            sasRt.sizeDelta = btnSize;
            sasRt.anchoredPosition = Vector2.zero;

            _progradeBtn = UIFactory.CreateCockpitButton(parent, "Prograde_Button", "PRO", btnSize,
                new Vector2(48f * dpiScale, yPos), new Color(0.04f, 0.08f, 0.10f, 0.96f), theme.FrameBorderColor,
                theme.TextPrimaryColor, OnPrograde);
            _retrogradeBtn = UIFactory.CreateCockpitButton(parent, "Retrograde_Button", "RET", btnSize,
                new Vector2(106f * dpiScale, yPos), new Color(0.04f, 0.08f, 0.10f, 0.96f), theme.FrameBorderColor,
                theme.TextPrimaryColor, OnRetrograde);

            ApplyTheme(theme);
        }

        private void OnRCSToggle()
        {
            if (TelemetryHub.Instance != null)
            {
                TelemetryHub.Instance.ToggleRCS();
            }
        }

        private void OnSASToggle()
        {
            if (TelemetryHub.Instance != null)
            {
                TelemetryHub.Instance.ToggleSAS();
            }
        }

        private void OnPrograde()
        {
            if (TelemetryHub.Instance != null) TelemetryHub.Instance.SetSASMode(VesselAutopilot.AutopilotMode.Prograde);
        }

        private void OnRetrograde()
        {
            if (TelemetryHub.Instance != null) TelemetryHub.Instance.SetSASMode(VesselAutopilot.AutopilotMode.Retrograde);
        }

        private void Update()
        {
            if (TelemetryHub.Instance == null) return;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color activeCol = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color inactiveBg = new Color(0.05f, 0.08f, 0.12f, 0.85f);

            bool rcs = TelemetryHub.Instance.IsRCSEnabled;
            _rcsImg.color = rcs ? activeCol : inactiveBg;
            _rcsText.color = rcs ? Color.black : Color.white;

            bool sas = TelemetryHub.Instance.IsSASEnabled;
            _sasImg.color = sas ? activeCol : inactiveBg;
            _sasText.color = sas ? Color.black : Color.white;
            if (_modeText != null)
            {
                _modeText.text = sas ? "SAS / " + TelemetryHub.Instance.CurrentSASMode.ToString().ToUpper() : "MANUAL FLIGHT";
                _modeText.color = sas ? activeCol : theme.WarningColor;
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            if (_rcsOutline != null) _rcsOutline.effectColor = theme.FrameBorderColor;
            if (_sasOutline != null) _sasOutline.effectColor = theme.FrameBorderColor;
            Color frame = theme.FrameBgColor.ToColor();
            if (_panelImage != null) _panelImage.color = new Color(frame.r * 0.55f, frame.g * 0.55f, frame.b * 0.55f, 0.94f);
            if (_panelOutline != null) _panelOutline.effectColor = theme.FrameBorderColor;
        }
    }
}
