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
        StagePropellant,
        Custom
    }

    /// <summary>
    /// 紧凑型精密圆弧度量仪表 (Compact Precision Arc Meter Widget)
    /// 现代航电独立单项圆弧仪表：
    /// 1. 240° 极坐标高对比度度量环 (支持程序化 RadialMeterShader 与平滑填充)；
    /// 2. 中央高清晰度主读数与工程单位；
    /// 3. 顶部系统/通道标签与底部两端量程微标；
    /// 4. 完美支持油门 (THR)、垂直速度 (VSI)、本级推进剂 (PROP) 与通用通配符绑定。
    /// 彻底废除旧版脱离姿态球后的硬编码漂移偏移，具备规范的独立卡片底板与微控件纳管。
    /// 100% 遵照 SPEC-001..008 核心架构规范。
    /// </summary>
    [FlightWidget("arc_meter", "meter_arc", "arc_gauge",
        Category = WidgetCategory.Gauges,
        DisplayName = "圆弧计量仪表",
        Description = "高精度独立 240° 圆弧指示仪表，适用于油门、升降率 (VSI) 或单项推进剂实时监测。",
        DefaultWidgetId = "core.throttle",
        DefaultX = 0f,
        DefaultY = 0f,
        HighFrequency = true,
        ExactIds = new[] { "core.throttle", "core.vsi", "core.propellant" })]
    public class ArcMeterWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(104f, 104f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private ArcMeterType _type = ArcMeterType.Throttle;
        private Image _meterImage;
        private Material _meterMaterial;

        // ── 头部集中声明微控件 ──
        public TextWidget Title = TextWidget.Title("THR");
        public TextWidget Value = TextWidget.Value("{THROTTLE:PERCENT}", "0%");
        public TextWidget MinScale = new TextWidget(TextStyleRole.Muted, -38f, -38f, 26f, 14f, 8f, TextAnchor.MiddleLeft, "0");
        public TextWidget MaxScale = new TextWidget(TextStyleRole.Muted, 12f, -38f, 26f, 14f, 8f, TextAnchor.MiddleRight, "100");

        private string _valueToken = "{THROTTLE}";
        private float _lastFill = -1f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            base.OnInitialize(config, theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            // 识别预设类型
            if (config != null && (config.WidgetId == "core.vsi" || config.WidgetType == "vsi"))
            {
                _type = ArcMeterType.VerticalSpeed;
                _valueToken = !string.IsNullOrEmpty(config.NumericToken) ? config.NumericToken : "{VSI:NORM}";
                Title.Text = "VSI";
                MinScale.Text = "-";
                MaxScale.Text = "+";
            }
            else if (config != null && (config.WidgetId == "core.propellant" || config.WidgetType == "propellant"))
            {
                _type = ArcMeterType.StagePropellant;
                _valueToken = !string.IsNullOrEmpty(config.NumericToken) ? config.NumericToken : "{PROP}";
                Title.Text = "PROP";
                MinScale.Text = "0";
                MaxScale.Text = "100";
            }
            else
            {
                _type = ArcMeterType.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THROTTLE}";
                Title.Text = "THR";
                MinScale.Text = "0";
                MaxScale.Text = "100";
            }

            // 构建圆弧着色器 GameObject
            GameObject meterObj = new GameObject("Arc_Meter_Ring", typeof(RectTransform), typeof(Image));
            meterObj.transform.SetParent(transform, false);
            RectTransform meterRt = meterObj.GetComponent<RectTransform>();
            float ringSize = 92f * s;
            meterRt.sizeDelta = new Vector2(ringSize, ringSize);
            meterRt.anchoredPosition = new Vector2(0f, -2f * s);

            _meterImage = meterObj.GetComponent<Image>();
            _meterImage.color = Color.clear;
            if (AssetLoader.RadialMeterShader != null)
            {
                _meterMaterial = new Material(AssetLoader.RadialMeterShader);
                _meterImage.material = _meterMaterial;
            }

            ConfigureMeterShader(theme);
        }

        private void ConfigureMeterShader(ThemeConfig theme)
        {
            if (_meterMaterial == null || theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 240 度开门圆弧 (左下 210° 顺时针至 右下 330°)
            _meterMaterial.SetFloat("_Clockwise", 1.0f);
            _meterMaterial.SetFloat("_StartAngle", 210.0f);
            _meterMaterial.SetFloat("_EndAngle", 330.0f);
            _meterMaterial.SetFloat("_InnerRadius", 0.78f);
            _meterMaterial.SetFloat("_OuterRadius", 0.92f);
            _meterMaterial.SetFloat("_SegmentCount", 16.0f);
            _meterMaterial.SetFloat("_SegmentGap", 0.06f);

            Color activeCol = _type == ArcMeterType.VerticalSpeed ? style.GetMeterColor(MeterStyleRole.Secondary, theme) : style.GetMeterColor(MeterStyleRole.Primary, theme);
            _meterMaterial.SetColor("_ActiveColor", activeCol);
            _meterMaterial.SetColor("_InactiveColor", style.GetMeterColor(MeterStyleRole.Track, theme));
            _meterMaterial.SetColor("_BorderColor", style.GetCardBorderColor(CardStyleRole.Normal, theme));
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                Value.Text = "---";
                if (_meterMaterial != null) _meterMaterial.SetFloat("_FillAmount", 0f);
                return;
            }

            float fill = 0f;
            if (_type == ArcMeterType.VerticalSpeed)
            {
                double vsi = telemetry.VerticalSpeed;
                Value.Text = (vsi >= 0.0 ? "+" : "") + vsi.ToString("F1");
                fill = (float)telemetry.NormalizedVSI;
            }
            else if (_type == ArcMeterType.StagePropellant)
            {
                double prop = telemetry.StagePropellantFraction * 100.0;
                Value.Text = $"{prop:F0}%";
                fill = Mathf.Clamp01(telemetry.StagePropellantFraction);
                Value.SetRole(fill < 0.15f ? TextStyleRole.Danger : (fill < 0.30f ? TextStyleRole.Warning : TextStyleRole.PrimaryValue));
            }
            else
            {
                double thr = telemetry.Throttle * 100.0;
                Value.Text = $"{thr:F0}%";
                fill = Mathf.Clamp01((float)telemetry.Throttle);
            }

            if (_meterMaterial != null && Math.Abs(fill - _lastFill) > 0.002f)
            {
                _lastFill = fill;
                _meterMaterial.SetFloat("_FillAmount", fill);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            ConfigureMeterShader(theme);
        }

        protected override void OnDestroy()
        {
            if (_meterMaterial != null)
            {
                Destroy(_meterMaterial);
                _meterMaterial = null;
            }
            base.OnDestroy();
        }
    }
}
