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
                Title.Text = I18n.Tr("WIDGET_GAUGE_PROP", "推进剂");
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
            float ringSize = 92f * s;
            _meterImage = CreateChild<Image>("Arc_Meter_Ring", transform, new Vector2(ringSize, ringSize), new Vector2(0f, -2f * s));
            GameObject meterObj = _meterImage.gameObject;
            RectTransform meterRt = _meterImage.rectTransform;
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

        private string _dataValueText = "---";
        private float _dataFill = 0f;
        private TextStyleRole _dataRole = TextStyleRole.PrimaryValue;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataValueText = "---";
                _dataFill = 0f;
                _dataRole = TextStyleRole.PrimaryValue;
                return;
            }

            float fill = 0f;
            string customTok = Config?.NumericToken;
            bool isCustomBound = !string.IsNullOrEmpty(customTok) && 
                                 customTok != "{VSI:NORM}" && 
                                 customTok != "{PROP}" && 
                                 customTok != "{THROTTLE}";

            if (isCustomBound)
            {
                double val = EvalNumeric(customTok, telemetry);
                _dataValueText = EvalToken(customTok, telemetry, "---");
                double min = Config.MinValue;
                double max = Config.MaxValue;
                if (max > min)
                {
                    fill = Mathf.Clamp01((float)((val - min) / (max - min)));
                }
                else
                {
                    fill = 0f;
                }

                if (Config.WarningThreshold > Config.CautionThreshold)
                {
                    if (val >= Config.WarningThreshold) _dataRole = TextStyleRole.Danger;
                    else if (val >= Config.CautionThreshold) _dataRole = TextStyleRole.Warning;
                    else _dataRole = TextStyleRole.PrimaryValue;
                }
            }
            else if (_type == ArcMeterType.VerticalSpeed || customTok == "{VSI:NORM}")
            {
                double vsi = telemetry.VerticalSpeed;
                _dataValueText = (vsi >= 0.0 ? "+" : "") + vsi.ToString("F1");
                fill = (float)telemetry.NormalizedVSI;
                _dataRole = TextStyleRole.PrimaryValue;
            }
            else if (_type == ArcMeterType.StagePropellant || customTok == "{PROP}")
            {
                double prop = telemetry.StagePropellantFraction * 100.0;
                _dataValueText = $"{prop:F0}%";
                fill = Mathf.Clamp01(telemetry.StagePropellantFraction);
                _dataRole = fill < 0.15f ? TextStyleRole.Danger : (fill < 0.30f ? TextStyleRole.Warning : TextStyleRole.PrimaryValue);
            }
            else
            {
                double thr = telemetry.Throttle * 100.0;
                _dataValueText = $"{thr:F0}%";
                fill = Mathf.Clamp01((float)telemetry.Throttle);
                _dataRole = TextStyleRole.PrimaryValue;
            }

            _dataFill = fill;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            Value.Text = _dataValueText;
            Value.SetRole(_dataRole);

            if (_meterMaterial != null && Math.Abs(_dataFill - _lastFill) > 0.002f)
            {
                _lastFill = _dataFill;
                _meterMaterial.SetFloat("_FillAmount", _dataFill);
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
