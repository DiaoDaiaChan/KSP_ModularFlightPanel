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

    public class ArcMeterWidget : BaseFlightWidget
    {
        private ArcMeterType _type;
        private Image _meterImage;
        private Material _meterMaterial;
        private Text _topLabelText;
        private Text _bottomLabelText;
        private Text _throttleValueText;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            if (config.WidgetId == "core.vsi" || config.WidgetType == "vsi")
            {
                _type = ArcMeterType.VerticalSpeed;
            }
            else if (config.WidgetId == "core.propellant" || config.WidgetType == "propellant")
            {
                _type = ArcMeterType.StagePropellant;
            }
            else
            {
                _type = ArcMeterType.Throttle;
            }

            float ballDiameter = 150f * CurrentDpiScale;
            float meterDiameter = ballDiameter * 1.30f;
            RectTransform.sizeDelta = new Vector2(meterDiameter, meterDiameter);

            _meterImage = gameObject.AddComponent<Image>();
            _meterMaterial = new Material(AssetLoader.RadialMeterShader);
            _meterImage.material = _meterMaterial;

            ConfigureMeterParameters(_type, theme);

            if (_type == ArcMeterType.Throttle)
            {
                CreateThrottleTopTag(transform, CurrentDpiScale, theme);
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
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 16.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentPrimary);
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;

                case ArcMeterType.VerticalSpeed:
                    // 右上方圆弧 (从底部 312° 逆时针向上到顶部 405° 即 45°)
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 312.0f);
                    _meterMaterial.SetFloat("_EndAngle", 405.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 16.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentSecondary);
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.FrameBorderColor);
                    break;

                case ArcMeterType.StagePropellant:
                    // 右下方圆弧 (从 258° 到 302°)
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 258.0f);
                    _meterMaterial.SetFloat("_EndAngle", 302.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 8.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentSecondary);
                    _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;
            }
        }

        private void CreateThrottleTopTag(Transform parent, float dpiScale, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(58f * dpiScale, 34f * dpiScale);
            Vector2 pos = new Vector2(-108f * dpiScale, 105f * dpiScale);

            GameObject tagBox = UIFactory.CreatePanel(parent, "Throttle_Tag", tagSize, pos, theme.FrameBgColor);
            GameObject border = UIFactory.CreatePanel(tagBox.transform, "Tag_Border", tagSize, Vector2.zero, Color.clear);
            Outline outline = border.AddComponent<Outline>();
            outline.effectColor = theme.FrameBorderColor;
            outline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _topLabelText = UIFactory.CreateText(tagBox.transform, "Tag_Text", "THR", Mathf.RoundToInt(8f * dpiScale), TextAnchor.UpperCenter, theme.TextAccentColor);
            RectTransform textRt = _topLabelText.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(tagSize.x, tagSize.y * 0.45f);
            textRt.anchoredPosition = new Vector2(0f, tagSize.y * 0.18f);

            _throttleValueText = UIFactory.CreateText(tagBox.transform, "Throttle_Value", "0%", Mathf.RoundToInt(13f * dpiScale), TextAnchor.LowerCenter, theme.TextPrimaryColor);
            RectTransform valueRt = _throttleValueText.GetComponent<RectTransform>();
            valueRt.sizeDelta = new Vector2(tagSize.x, tagSize.y * 0.62f);
            valueRt.anchoredPosition = new Vector2(0f, -tagSize.y * 0.12f);

            _bottomLabelText = UIFactory.CreateText(parent, "Throttle_Min", "0", Mathf.RoundToInt(9f * dpiScale), TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform bottomRt = _bottomLabelText.GetComponent<RectTransform>();
            bottomRt.sizeDelta = new Vector2(26f * dpiScale, 14f * dpiScale);
            bottomRt.anchoredPosition = new Vector2(-108f * dpiScale, -108f * dpiScale);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (_meterMaterial == null || telemetry == null) return;

            float fill = 0f;
            switch (_type)
            {
                case ArcMeterType.Throttle:
                    fill = (float)(TelemetryTokenEngine.EvaluateNumeric("{THROTTLE}", telemetry) / 100.0);
                    if (_throttleValueText != null)
                    {
                        _throttleValueText.text = TelemetryTokenEngine.Evaluate("{THROTTLE}", telemetry);
                    }
                    break;
                case ArcMeterType.VerticalSpeed:
                    fill = (float)telemetry.NormalizedVSI;
                    break;
                case ArcMeterType.StagePropellant:
                    fill = (float)(TelemetryTokenEngine.EvaluateNumeric("{PROP}", telemetry) / 100.0);
                    break;
            }

            _meterMaterial.SetFloat("_FillAmount", Mathf.Clamp01(fill));
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            ConfigureMeterParameters(_type, theme);
            if (_topLabelText != null) _topLabelText.color = theme.TextAccentColor;
            if (_bottomLabelText != null) _bottomLabelText.color = theme.TextAccentColor;
            if (_throttleValueText != null) _throttleValueText.color = theme.TextPrimaryColor;
        }

        private void OnDestroy()
        {
            if (_meterMaterial != null)
            {
                Destroy(_meterMaterial);
                _meterMaterial = null;
            }
        }
    }
}
