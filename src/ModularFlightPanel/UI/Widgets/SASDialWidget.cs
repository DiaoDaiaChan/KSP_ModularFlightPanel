using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public class SASDialWidget : BaseFlightWidget
    {
        private class SASButtonData
        {
            public VesselAutopilot.AutopilotMode Mode;
            public Button Button;
            public Image Image;
            public Outline Outline;
            public Text Label;
            public float Angle;
        }

        private List<SASButtonData> _buttons = new List<SASButtonData>();
        private GameObject _shipSilhouette;
        private Image _dialBgImage;
        private Outline _dialOutline;
        private RawImage _silhouetteRawImage;
        private Image _noseTipImage;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float dialRadius = 46f * CurrentDpiScale;
            float dialDiameter = dialRadius * 2f;
            RectTransform.sizeDelta = new Vector2(dialDiameter, dialDiameter);

            _dialBgImage = gameObject.AddComponent<Image>();
            _dialBgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.96f);

            _dialOutline = gameObject.AddComponent<Outline>();
            _dialOutline.effectColor = theme.FrameBorderColor;
            _dialOutline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _dialBgImage.color, _dialOutline.effectColor, CurrentDpiScale);

            CreateShipSilhouette(transform, CurrentDpiScale, theme);
            CreateSASModeButtons(transform, dialRadius, CurrentDpiScale, theme);
            CreateControlTag(transform, dialRadius, CurrentDpiScale, theme);
        }

        private void CreateShipSilhouette(Transform parent, float dpiScale, ThemeConfig theme)
        {
            _shipSilhouette = new GameObject("Ship_Silhouette", typeof(RectTransform));
            _shipSilhouette.transform.SetParent(parent, false);

            float silhouetteSize = 44f * dpiScale;

            GameObject rawImgObj = new GameObject("Silhouette_RawImage", typeof(RectTransform), typeof(RawImage));
            rawImgObj.transform.SetParent(_shipSilhouette.transform, false);
            RectTransform rawRt = rawImgObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(silhouetteSize, silhouetteSize);
            rawRt.anchoredPosition = Vector2.zero;

            _silhouetteRawImage = rawImgObj.GetComponent<RawImage>();
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = theme.AccentSecondary;

            if (VesselSilhouetteBaker.Instance != null)
            {
                _silhouetteRawImage.texture = VesselSilhouetteBaker.Instance.SilhouetteTexture;
                VesselSilhouetteBaker.Instance.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 机头指向微标 (保持 WarningColor 橙黄视觉，提示前缘朝向)
            float tipSize = 3.5f * dpiScale;
            Vector2 tipPos = new Vector2(0f, 18f * dpiScale);
            GameObject tipObj = UIFactory.CreatePanel(_shipSilhouette.transform, "Nose_Tip", new Vector2(tipSize, tipSize), tipPos, theme.WarningColor);
            _noseTipImage = tipObj.GetComponent<Image>();
            if (_noseTipImage != null) _noseTipImage.raycastTarget = false;
        }

        private void OnSilhouetteUpdated(RenderTexture rt)
        {
            if (_silhouetteRawImage != null)
            {
                _silhouetteRawImage.texture = rt;
            }
        }

        private void CreateSASModeButtons(Transform parent, float dialRadius, float dpiScale, ThemeConfig theme)
        {
            var modes = new (VesselAutopilot.AutopilotMode mode, string icon, float angle)[]
            {
                (VesselAutopilot.AutopilotMode.StabilityAssist, "SAS", 90f),
                (VesselAutopilot.AutopilotMode.Prograde, "PRO", 45f),
                (VesselAutopilot.AutopilotMode.Retrograde, "RET", 225f),
                (VesselAutopilot.AutopilotMode.Normal, "NRM", 0f),
                (VesselAutopilot.AutopilotMode.Antinormal, "ANT", 180f),
                (VesselAutopilot.AutopilotMode.RadialIn, "R-IN", 315f),
                (VesselAutopilot.AutopilotMode.RadialOut, "R-OUT", 135f),
                (VesselAutopilot.AutopilotMode.Target, "TGT", 270f),
                (VesselAutopilot.AutopilotMode.Maneuver, "MAN", 60f)
            };

            float ringRadius = dialRadius * 0.70f;
            float btnSize = 18f * dpiScale;

            foreach (var m in modes)
            {
                float rad = m.angle * Mathf.Deg2Rad;
                Vector2 btnPos = new Vector2(Mathf.Cos(rad) * ringRadius, Mathf.Sin(rad) * ringRadius);

                Button btn = UIFactory.CreateButton(parent, $"SAS_Btn_{m.mode}", new Vector2(btnSize, btnSize), btnPos, () => OnSASButtonClicked(m.mode));
                Image img = btn.GetComponent<Image>();
                img.color = new Color(0.08f, 0.12f, 0.16f, 0.85f);

                Outline ol = btn.gameObject.AddComponent<Outline>();
                ol.effectColor = theme.FrameBorderColor;
                ol.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * dpiScale));
                Text lbl = UIFactory.CreateText(btn.transform, "Label", m.icon, fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = new Vector2(btnSize, btnSize);
                lblRt.anchoredPosition = Vector2.zero;

                _buttons.Add(new SASButtonData
                {
                    Mode = m.mode,
                    Button = btn,
                    Image = img,
                    Outline = ol,
                    Label = lbl,
                    Angle = m.angle
                });
            }
        }

        private void CreateControlTag(Transform parent, float dialRadius, float dpiScale, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(76f * dpiScale, 16f * dpiScale);
            Vector2 pos = new Vector2(0f, -dialRadius - 10f * dpiScale);

            GameObject tagBox = UIFactory.CreatePanel(parent, "SAS_Control_Tag", tagSize, pos, theme.FrameBgColor);
            Outline ol = tagBox.AddComponent<Outline>();
            ol.effectColor = theme.FrameBorderColor;
            ol.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * dpiScale));
            Text t = UIFactory.CreateText(tagBox.transform, "Text", "SAS.CONTROL", fontSize, TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform trt = t.GetComponent<RectTransform>();
            trt.sizeDelta = tagSize;
            trt.anchoredPosition = Vector2.zero;
        }

        private void OnSASButtonClicked(VesselAutopilot.AutopilotMode mode)
        {
            if (TelemetryHub.Instance != null)
            {
                TelemetryHub.Instance.SetSASMode(mode);
            }
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            if (_shipSilhouette != null)
            {
                _shipSilhouette.transform.localRotation = Quaternion.Euler(0f, 0f, -telemetry.Roll);
            }

            if (_silhouetteRawImage != null && _silhouetteRawImage.texture == null)
            {
                if (VesselSilhouetteBaker.Instance != null && VesselSilhouetteBaker.Instance.SilhouetteTexture != null)
                {
                    _silhouetteRawImage.texture = VesselSilhouetteBaker.Instance.SilhouetteTexture;
                }
            }

            if (_noseTipImage != null && VesselSilhouetteBaker.Instance != null)
            {
                float halfSpan = 22f * CurrentDpiScale;
                float tipY = Mathf.Clamp(VesselSilhouetteBaker.Instance.NormalizedNoseTipY * (halfSpan * 2f), 6f * CurrentDpiScale, halfSpan - 2f * CurrentDpiScale);
                ((RectTransform)_noseTipImage.transform).anchoredPosition = new Vector2(0f, tipY);
            }

            VesselAutopilot.AutopilotMode currentMode = telemetry.CurrentSASMode;
            bool sasOn = telemetry.IsSASEnabled;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color activeCol = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color defaultBg = new Color(0.08f, 0.12f, 0.16f, 0.85f);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                bool isSelected = sasOn && (b.Mode == currentMode);

                if (b.Image != null)
                {
                    b.Image.color = isSelected ? activeCol : defaultBg;
                }
                if (b.Label != null)
                {
                    b.Label.color = isSelected ? Color.black : Color.white;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_dialBgImage != null) _dialBgImage.color = theme.FrameBgColor;
            if (_dialOutline != null) _dialOutline.effectColor = theme.FrameBorderColor;
            if (_silhouetteRawImage != null) _silhouetteRawImage.color = theme.AccentSecondary;
            if (_noseTipImage != null) _noseTipImage.color = theme.WarningColor;
        }

        private void OnDestroy()
        {
            if (VesselSilhouetteBaker.Instance != null)
            {
                VesselSilhouetteBaker.Instance.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
        }
    }
}
