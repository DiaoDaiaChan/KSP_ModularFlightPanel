using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    public enum ArcMeterType
    {
        Throttle,
        VerticalSpeed,
        StagePropellant
    }

    public class ArcMeterWidget : MonoBehaviour
    {
        private ArcMeterType _type;
        private Image _meterImage;
        private Material _meterMaterial;
        private Text _topLabelText;

        public void Initialize(Transform parent, ArcMeterType type, LayoutConfig layout, ThemeConfig theme, float scale)
        {
            _type = type;
            float dpiScale = UIFactory.GetScreenDpiScale() * scale;
            float ballDiameter = layout.SphereRadius * 2f * dpiScale;
            float meterDiameter = ballDiameter * 1.32f;

            GameObject meterObj = new GameObject($"ArcMeter_{type}", typeof(RectTransform), typeof(Image));
            meterObj.transform.SetParent(parent, false);

            RectTransform rt = meterObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(meterDiameter, meterDiameter);
            rt.anchoredPosition = Vector2.zero;

            _meterMaterial = new Material(AssetLoader.RadialMeterShader);
            _meterImage = meterObj.GetComponent<Image>();
            _meterImage.material = _meterMaterial;

            ConfigureMeterParameters(type, theme);

            if (type == ArcMeterType.Throttle)
            {
                CreateThrottleTopTag(meterObj.transform, dpiScale, theme);
            }
        }

        private void ConfigureMeterParameters(ArcMeterType type, ThemeConfig theme)
        {
            if (_meterMaterial == null) return;

            switch (type)
            {
                case ArcMeterType.Throttle:
                    // 左侧圆弧 (从底部 236° 顺时针向上填充到顶部 124°)
                    _meterMaterial.SetFloat("_Clockwise", 1.0f);
                    _meterMaterial.SetFloat("_StartAngle", 236.0f);
                    _meterMaterial.SetFloat("_EndAngle", 124.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.76f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.94f);
                    _meterMaterial.SetFloat("_SegmentCount", 10.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.16f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentPrimary);
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;

                case ArcMeterType.VerticalSpeed:
                    // 右上方圆弧 (从底部 312° 逆时针向上到顶部 405° 即 45°)
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 312.0f);
                    _meterMaterial.SetFloat("_EndAngle", 405.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.78f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.92f);
                    _meterMaterial.SetFloat("_SegmentCount", 12.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.15f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentSecondary);
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.FrameBorderColor);
                    break;

                case ArcMeterType.StagePropellant:
                    // 右下方圆弧 (从 258° 到 302°)
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 258.0f);
                    _meterMaterial.SetFloat("_EndAngle", 302.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.78f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.92f);
                    _meterMaterial.SetFloat("_SegmentCount", 6.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.18f);
                    _meterMaterial.SetColor("_ActiveColor", new Color(0.1f, 0.5f, 1.0f, 1.0f));
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;
            }
        }

        private void CreateThrottleTopTag(Transform parent, float dpiScale, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(38f * dpiScale, 20f * dpiScale);
            Vector2 pos = new Vector2(-110f * dpiScale, 115f * dpiScale);

            GameObject tagBox = UIFactory.CreatePanel(parent, "Throttle_Tag", tagSize, pos, theme.FrameBgColor);
            GameObject border = UIFactory.CreatePanel(tagBox.transform, "Tag_Border", tagSize, Vector2.zero, Color.clear);
            Outline outline = border.AddComponent<Outline>();
            outline.effectColor = theme.FrameBorderColor;
            outline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            int fontSize = Mathf.RoundToInt(11f * dpiScale);
            _topLabelText = UIFactory.CreateText(tagBox.transform, "Tag_Text", "100", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform textRt = _topLabelText.GetComponent<RectTransform>();
            textRt.sizeDelta = tagSize;
            textRt.anchoredPosition = Vector2.zero;
        }

        private void Update()
        {
            if (_meterMaterial == null || TelemetryHub.Instance == null) return;

            float fill = 0f;
            switch (_type)
            {
                case ArcMeterType.Throttle:
                    fill = TelemetryHub.Instance.Throttle;
                    break;
                case ArcMeterType.VerticalSpeed:
                    fill = TelemetryHub.Instance.NormalizedVSI;
                    break;
                case ArcMeterType.StagePropellant:
                    fill = TelemetryHub.Instance.StagePropellantFraction;
                    break;
            }

            _meterMaterial.SetFloat("_FillAmount", Mathf.Clamp01(fill));
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            ConfigureMeterParameters(_type, theme);
        }

        private void OnDestroy()
        {
            if (_meterMaterial != null)
            {
                Destroy(_meterMaterial);
            }
        }
    }
}
