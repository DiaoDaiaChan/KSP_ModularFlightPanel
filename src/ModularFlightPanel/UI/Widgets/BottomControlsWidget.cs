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

        public void Initialize(Transform parent, LayoutConfig layout, ThemeConfig theme, float scale)
        {
            float dpiScale = UIFactory.GetScreenDpiScale() * scale;
            Vector2 btnSize = new Vector2(48f * dpiScale, 26f * dpiScale);
            float yPos = -layout.SphereRadius * dpiScale - 18f * dpiScale;

            Vector2 rcsPos = new Vector2(-42f * dpiScale, yPos);
            _rcsBtn = UIFactory.CreateButton(parent, "RCS_Button", btnSize, rcsPos, OnRCSToggle);
            _rcsImg = _rcsBtn.GetComponent<Image>();
            _rcsOutline = _rcsBtn.gameObject.AddComponent<Outline>();
            _rcsOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            int fontSize = Mathf.RoundToInt(11f * dpiScale);
            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "RCS_Text", "RCS", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = btnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            Vector2 sasPos = new Vector2(42f * dpiScale, yPos);
            _sasBtn = UIFactory.CreateButton(parent, "SAS_Button", btnSize, sasPos, OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasOutline = _sasBtn.gameObject.AddComponent<Outline>();
            _sasOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _sasText = UIFactory.CreateText(_sasBtn.transform, "SAS_Text", "SAS", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform sasRt = _sasText.GetComponent<RectTransform>();
            sasRt.sizeDelta = btnSize;
            sasRt.anchoredPosition = Vector2.zero;

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
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            if (_rcsOutline != null) _rcsOutline.effectColor = theme.FrameBorderColor;
            if (_sasOutline != null) _sasOutline.effectColor = theme.FrameBorderColor;
        }
    }
}
