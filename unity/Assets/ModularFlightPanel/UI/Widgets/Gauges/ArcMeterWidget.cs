using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public enum ArcMeterType
    {
        Throttle,
        VerticalSpeed,
        StagePropellant
    }

    /// <summary>
    /// 姿态球外缘弧度计量仪表 (Arc Meter Widget - Throttle / VSI / Propellant)
    /// 100% 遵照 MFP 标准：通配符驱动、主题语义管道、脏检查保护、0 颜色字面量。
    /// </summary>
    [FlightWidget("arc_meter", "meter_arc", Category = WidgetCategory.Gauges, DisplayName = "圆弧计量仪表", Description = "高精度 270° 圆弧指示仪表，适用于油门、升降率 (VSI) 或单项推进剂实时监测。", DefaultWidgetId = "core.throttle", DefaultX = 0f, DefaultY = 0f, ExactIds = new[] { "core.throttle", "core.vsi", "core.propellant" })]
    public class ArcMeterWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private ArcMeterType _type;
        private Image _meterImage;
        private Material _meterMaterial;
        private GameObject _tagBox;
        private Outline _tagOutline;
        private Text _topLabelText;
        private Text _bottomLabelText;
        private Text _throttleValueText;

        // 通配符通道与模板
        private string _valueToken = "{THROTTLE}";
        private string _topLabelTemplate = "THR";
        private string _bottomLabelTemplate = "0";

        // 运行时脏标记缓存
        private float _lastFill = -1f;
        private string _lastThrottleStr = string.Empty;
        private string _lastTopLabelStr = string.Empty;
        private string _lastBottomLabelStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (config != null && (config.WidgetId == "core.vsi" || config.WidgetType == "vsi"))
            {
                _type = ArcMeterType.VerticalSpeed;
                _valueToken = !string.IsNullOrEmpty(config.NumericToken) ? config.NumericToken : "{VSI:NORM}";
                _topLabelTemplate = !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : "VSI";
                _bottomLabelTemplate = "0";
            }
            else if (config != null && (config.WidgetId == "core.propellant" || config.WidgetType == "propellant"))
            {
                _type = ArcMeterType.StagePropellant;
                _valueToken = !string.IsNullOrEmpty(config.NumericToken) ? config.NumericToken : "{PROP}";
                _topLabelTemplate = !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : "PROP";
                _bottomLabelTemplate = "0";
            }
            else
            {
                _type = ArcMeterType.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THROTTLE}";
                _topLabelTemplate = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName : "THR";
                _bottomLabelTemplate = "0";
            }

            ParseCustomTemplate(config?.CustomTemplate);

            float ballDiameter = 150f * s;
            float meterDiameter = ballDiameter * 1.30f;
            RectTransform.sizeDelta = new Vector2(meterDiameter, meterDiameter);

            _meterImage = gameObject.AddComponent<Image>();
            _meterImage.color = Color.clear;
            _meterMaterial = new Material(AssetLoader.RadialMeterShader);
            _meterImage.material = _meterMaterial;

            ConfigureMeterParameters(_type, theme);

            if (_type == ArcMeterType.Throttle)
            {
                CreateThrottleTopTag(transform, s, theme);
            }

            // 注册子控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "arc_meter", "弧度计量表盘", _meterImage != null ? _meterImage.gameObject : gameObject, (t) => ConfigureMeterParameters(_type, t)));
            if (_tagBox != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "tag_box", "顶标容器", _tagBox, (t) => {
                    if (_tagBox != null)
                    {
                        Image tagImg = _tagBox.GetComponent<Image>();
                        ApplyCard(tagImg, _tagOutline, CardStyleRole.Normal, t);
                    }
                }));
            }
            if (_topLabelText != null)
            {
                this.Controls.Register(new WidgetReadoutControl("top_label", "顶部标签", _topLabelText.gameObject, _topLabelText, null, TextStyleRole.Label));
            }
            if (_throttleValueText != null)
            {
                this.Controls.Register(new WidgetReadoutControl("value_readout", "数值读数", _throttleValueText.gameObject, _throttleValueText, null, TextStyleRole.PrimaryValue));
            }
            if (_bottomLabelText != null)
            {
                this.Controls.Register(new WidgetReadoutControl("bottom_label", "底部标度", _bottomLabelText.gameObject, _bottomLabelText, null, TextStyleRole.Muted));
            }

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            string[] pairs = template.Split(';');
            foreach (string p in pairs)
            {
                string[] kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                if (k == "VAL" || k == "VALUE" || k == "TOKEN") _valueToken = v;
                else if (k == "LABEL" || k == "TOP" || k == "TAG") _topLabelTemplate = v;
                else if (k == "BOTTOM" || k == "MIN") _bottomLabelTemplate = v;
            }
        }

        private void ConfigureMeterParameters(ArcMeterType type, ThemeConfig theme)
        {
            if (_meterMaterial == null || theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            switch (type)
            {
                case ArcMeterType.Throttle:
                    _meterMaterial.SetFloat("_Clockwise", 1.0f);
                    _meterMaterial.SetFloat("_StartAngle", 236.0f);
                    _meterMaterial.SetFloat("_EndAngle", 124.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 16.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentPrimary);
                    _meterMaterial.SetColor("_InactiveColor", style.GetMeterColor(MeterStyleRole.Track, theme));
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;

                case ArcMeterType.VerticalSpeed:
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 312.0f);
                    _meterMaterial.SetFloat("_EndAngle", 405.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 16.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentSecondary);
                    _meterMaterial.SetColor("_InactiveColor", style.GetMeterColor(MeterStyleRole.Track, theme));
                    _meterMaterial.SetColor("_BorderColor", style.GetCardBorderColor(CardStyleRole.Normal, theme));
                    break;

                case ArcMeterType.StagePropellant:
                    _meterMaterial.SetFloat("_Clockwise", 0.0f);
                    _meterMaterial.SetFloat("_StartAngle", 258.0f);
                    _meterMaterial.SetFloat("_EndAngle", 302.0f);
                    _meterMaterial.SetFloat("_InnerRadius", 0.85f);
                    _meterMaterial.SetFloat("_OuterRadius", 0.93f);
                    _meterMaterial.SetFloat("_SegmentCount", 8.0f);
                    _meterMaterial.SetFloat("_SegmentGap", 0.08f);
                    _meterMaterial.SetColor("_ActiveColor", theme.AccentSecondary);
                    _meterMaterial.SetColor("_InactiveColor", style.GetMeterColor(MeterStyleRole.Track, theme));
                    _meterMaterial.SetColor("_BorderColor", theme.AccentSecondary);
                    break;
            }
        }

        private void CreateThrottleTopTag(Transform parent, float dpiScale, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Vector2 tagSize = new Vector2(58f * dpiScale, 34f * dpiScale);
            Vector2 pos = new Vector2(-108f * dpiScale, 105f * dpiScale);

            _tagBox = UIFactory.CreatePanel(parent, "Throttle_Tag", tagSize, pos, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            _tagOutline = _tagBox.AddComponent<Outline>();
            _tagOutline.effectColor = style.GetCardBorderColor(CardStyleRole.Normal, theme);
            _tagOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _topLabelText = UIFactory.CreateText(_tagBox.transform, "Tag_Text", _topLabelTemplate, Mathf.RoundToInt(8f * dpiScale), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform textRt = _topLabelText.rectTransform;
            textRt.sizeDelta = new Vector2(tagSize.x, tagSize.y * 0.45f);
            textRt.anchoredPosition = new Vector2(0f, tagSize.y * 0.18f);

            _throttleValueText = UIFactory.CreateText(_tagBox.transform, "Throttle_Value", "0%", Mathf.RoundToInt(13f * dpiScale), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valueRt = _throttleValueText.rectTransform;
            valueRt.sizeDelta = new Vector2(tagSize.x, tagSize.y * 0.62f);
            valueRt.anchoredPosition = new Vector2(0f, -tagSize.y * 0.12f);

            _bottomLabelText = UIFactory.CreateText(parent, "Throttle_Min", _bottomLabelTemplate, Mathf.RoundToInt(9f * dpiScale), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Muted, theme));
            RectTransform bottomRt = _bottomLabelText.rectTransform;
            bottomRt.sizeDelta = new Vector2(26f * dpiScale, 14f * dpiScale);
            bottomRt.anchoredPosition = new Vector2(-108f * dpiScale, -108f * dpiScale);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (_meterMaterial == null || telemetry == null || !telemetry.HasVessel) return;

            // 1. 动态标签求值 (文案 100% 通配符可自定义)
            if (_topLabelText != null)
            {
                string evalTop = TelemetryTokenEngine.Evaluate(_topLabelTemplate, telemetry);
                if (evalTop != _lastTopLabelStr)
                {
                    _lastTopLabelStr = evalTop;
                    _topLabelText.text = evalTop;
                }
            }
            if (_bottomLabelText != null)
            {
                string evalBtm = TelemetryTokenEngine.Evaluate(_bottomLabelTemplate, telemetry);
                if (evalBtm != _lastBottomLabelStr)
                {
                    _lastBottomLabelStr = evalBtm;
                    _bottomLabelText.text = evalBtm;
                }
            }

            // 2. 数值求值 (驱动圆弧填充)
            float fill;
            if (_type == ArcMeterType.VerticalSpeed && _valueToken == "{VSI:NORM}")
            {
                fill = (float)telemetry.NormalizedVSI;
            }
            else
            {
                double val = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
                if (double.IsNaN(val)) val = 0.0;
                double maxVal = Config.MaxValue > Config.MinValue ? Config.MaxValue : 100.0;
                fill = (float)Mathf.Clamp01((float)((val - Config.MinValue) / (maxVal - Config.MinValue)));
            }

            fill = Mathf.Clamp01(fill);
            double delta = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.001;
            if (Mathf.Abs(fill - _lastFill) > (float)delta)
            {
                _lastFill = fill;
                _meterMaterial.SetFloat("_FillAmount", fill);
            }

            // 3. 读数文本更新与脏检查
            if (_throttleValueText != null)
            {
                string str = TelemetryTokenEngine.Evaluate(_valueToken, telemetry);
                if (str != _lastThrottleStr)
                {
                    _lastThrottleStr = str;
                    _throttleValueText.text = str;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ConfigureMeterParameters(_type, theme);

            if (_tagBox != null)
            {
                Image tagImg = _tagBox.GetComponent<Image>();
                ApplyCard(tagImg, _tagOutline, CardStyleRole.Normal, theme);
            }

            if (_topLabelText != null) ApplyText(_topLabelText, TextStyleRole.Label, theme);
            if (_bottomLabelText != null) ApplyText(_bottomLabelText, TextStyleRole.Muted, theme);
            if (_throttleValueText != null) ApplyText(_throttleValueText, TextStyleRole.PrimaryValue, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_meterMaterial != null)
            {
                Destroy(_meterMaterial);
                _meterMaterial = null;
            }
            base.OnDestroy();
        }
    }
}
