using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// ECAM 风格弧形/马蹄形仪表套件 (ECAM Dial Gauge Kit)
    /// 具备：
    /// 1. 270° 马蹄形分段极坐标圆弧度量环 (采用 RadialSegmentedMeter 着色器)
    /// 2. 旋转指针与高对比度数显
    /// 3. 双极限量程模型：
    ///    - 有上限型 (Hard Limit): 弧度与数显严格截断在 [Min, Max]
    ///    - 软上限/无上限型 (Soft Limit): 达到 Max 标称量程时弧线/指针卡满量程端并触发红色爆表告警，
    ///      但中央数显框绝不截断，持续精准呈现真实超标数值 (例如 15G 表盘显示 18.4 G)
    /// 4. 三色安全区间切换 (正常绿/青 -> 注意黄 -> 警告红/爆表闪烁)
    /// 5. 100% 通配符与 CustomTemplate 双驱动，零硬编码，统一样式管道
    /// </summary>
    [FlightWidget("ecam_dial", "ecam_gauge", "dial", Category = WidgetCategory.Gauges, DisplayName = "ECAM 圆弧通用仪表", Description = "270° 马蹄形高对比度圆弧表盘，支持动态指针、数显与软上限爆表模式。可在装配台绑定任意遥测通配符。", DefaultWidgetId = "ecam.dial", DefaultX = 0f, DefaultY = 0f)]
    public class EcamDialGaugeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(112f, 112f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title("N1");
        public TextWidget Value = TextWidget.Value("{ENG:N1}");

        private const float START_ANGLE = 225.0f; // 左下 225° 顺时针旋转
        private const float END_ANGLE = 315.0f;   // 右下 315° (即 -45°)
        private const float ANGLE_SPAN = 270.0f;  // 270 度大弧度马蹄形

        private Image _bgPanel;
        private Outline _bgOutline;

        private GameObject _meterObj;
        private Image _meterImage;
        private Material _meterMaterial;

        private RectTransform _needlePivot;
        private Image _needleImage;

        private Text _titleText;
        private Text _valueText;
        private Text _unitText;
        private Text _minScaleText;
        private Text _maxScaleText;
        private Text _limitModeText;

        // 通配符通道配置与模板
        private string _valueToken = "{ENG:N1}";
        private string _titleTemplate = "N1";
        private string _unitTemplate = "%";
        private double _overrideMin = double.NaN;
        private double _overrideMax = double.NaN;
        private double _overrideCaution = double.NaN;
        private double _overrideWarning = double.NaN;
        private string _overrideLimitMode = null;

        // 运行时脏检查缓存
        private double _lastValue = double.NaN;
        private string _lastFormattedVal = string.Empty;
        private string _lastTitleStr = string.Empty;
        private string _lastUnitStr = string.Empty;
        private int _lastAlertState = -1; // 0=Normal, 1=Caution, 2=Warning

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = BaseSize * s;
            float size = panelSize.x;
            RectTransform.sizeDelta = panelSize;

            // 1. 卡片底衬 (由基类 AutoCreateCardFrame 托管)
            _bgPanel = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null) _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            UIFactory.ApplyCockpitChrome(gameObject, _bgPanel != null ? _bgPanel.color : Color.clear, _bgOutline != null ? _bgOutline.effectColor : Color.clear, s);

            // 解析自定义通配符通道
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.NumericToken)) _valueToken = config.NumericToken;
                if (!string.IsNullOrEmpty(config.DisplayName))
                {
                    string name = config.DisplayName.Trim();
                    if (name.StartsWith("ECAM ", StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(5).Trim();
                    }
                    string sfxMonitor = I18n.Tr("SUFFIX_DIAL_MONITOR", "监控表");
                    string sfxG = I18n.Tr("SUFFIX_DIAL_G", "过载表");
                    string sfxThrust = I18n.Tr("SUFFIX_DIAL_THRUST", "推力表");
                    string sfxDial = I18n.Tr("SUFFIX_DIAL_CHAR", "表");

                    if (name.EndsWith(sfxMonitor)) name = name.Substring(0, name.Length - sfxMonitor.Length).Trim();
                    else if (name.EndsWith(sfxG)) name = name.Substring(0, name.Length - sfxG.Length).Trim();
                    else if (name.EndsWith(sfxThrust)) name = name.Substring(0, name.Length - sfxThrust.Length).Trim();
                    else if (name.EndsWith(sfxDial) && name.Length > 2) name = name.Substring(0, name.Length - sfxDial.Length).Trim();

                    _titleTemplate = name;
                }
                if (!string.IsNullOrEmpty(config.UnitLabel)) _unitTemplate = config.UnitLabel;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, _valueToken);
            _titleTemplate = GetTemplateChannel(new[] { "TITLE", "LABEL", "TAG", "NAME" }, _titleTemplate);
            _unitTemplate = GetTemplateChannel("UNIT", _unitTemplate);
            float minVal = GetTemplateChannelFloat("MIN", float.NaN);
            if (!float.IsNaN(minVal)) _overrideMin = minVal;
            float maxVal = GetTemplateChannelFloat("MAX", float.NaN);
            if (!float.IsNaN(maxVal)) _overrideMax = maxVal;
            float cVal = GetTemplateChannelFloat("CAUTION", float.NaN);
            if (!float.IsNaN(cVal)) _overrideCaution = cVal;
            float wVal = GetTemplateChannelFloat(new[] { "WARN", "WARNING" }, float.NaN);
            if (!float.IsNaN(wVal)) _overrideWarning = wVal;
            string limitMode = GetTemplateChannel(new[] { "LIMIT", "LIMITMODE" }, null);
            if (!string.IsNullOrEmpty(limitMode)) _overrideLimitMode = limitMode.ToLowerInvariant();

            // 2. 马蹄形弧线度量环 (RadialSegmentedMeter.shader)
            _meterImage = CreateChild<Image>("ECAM_Arc_Meter", transform, new Vector2(size * 0.92f, size * 0.92f), Vector2.zero);
            _meterObj = _meterImage.gameObject;
            RectTransform meterRt = _meterImage.rectTransform;

            if (AssetLoader.RadialMeterShader != null)
            {
                _meterMaterial = new Material(AssetLoader.RadialMeterShader);
                _meterImage.material = _meterMaterial;
            }

            ConfigureMeterMaterial(theme);

            // 3. 动态指针 (Needle Pointer)
            _needlePivot = CreateContainer("Needle_Pivot", transform, Vector2.zero, Vector2.zero);

            _needleImage = CreateChild<Image>("Needle_Bar", _needlePivot, new Vector2(1.5f * s, 16f * s), new Vector2(0f, (size * 0.46f) - (18f * s)));
            GameObject needleObj = _needleImage.gameObject;
            RectTransform needleRt = _needleImage.rectTransform;
            needleRt.pivot = new Vector2(0.5f, 0f);
            _needleImage.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);

            // 4. 标题、数显与单位 (ECAM 风格排版)
            int titleSize = Mathf.RoundToInt(8.5f * s);
            _titleText = UIFactory.CreateText(transform, "ECAM_Title", _titleTemplate, titleSize, TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform trt = _titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(size - 14f * s, 14f * s);
            trt.anchoredPosition = new Vector2(0f, 22f * s);

            int valSize = Mathf.RoundToInt(16f * s);
            _valueText = UIFactory.CreateText(transform, "ECAM_Value", "0.0", valSize, TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform vrt = _valueText.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(size - 14f * s, 22f * s);
            vrt.anchoredPosition = new Vector2(0f, -3f * s);

            int unitSize = Mathf.RoundToInt(8.5f * s);
            _unitText = UIFactory.CreateText(transform, "ECAM_Unit", _unitTemplate, unitSize, TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform urt = _unitText.GetComponent<RectTransform>();
            urt.sizeDelta = new Vector2(size - 14f * s, 12f * s);
            urt.anchoredPosition = new Vector2(0f, -22f * s);

            // 刻度两端标称数字 (左下起点与右下满格)
            int scaleFontSize = Mathf.RoundToInt(9f * s);
            double effectiveMin = GetEffectiveMin();
            double effectiveMax = GetEffectiveMax();
            _minScaleText = UIFactory.CreateText(transform, "Min_Scale", $"{effectiveMin:F0}", scaleFontSize, TextAnchor.MiddleLeft,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform minRt = _minScaleText.GetComponent<RectTransform>();
            minRt.sizeDelta = new Vector2(30f * s, 14f * s);
            minRt.anchoredPosition = new Vector2(-28f * s, -38f * s);

            _maxScaleText = UIFactory.CreateText(transform, "Max_Scale", $"{effectiveMax:F0}", scaleFontSize, TextAnchor.MiddleRight,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform maxRt = _maxScaleText.GetComponent<RectTransform>();
            maxRt.sizeDelta = new Vector2(30f * s, 14f * s);
            maxRt.anchoredPosition = new Vector2(28f * s, -38f * s);

            _limitModeText = UIFactory.CreateText(transform, "Limit_Mode", GetLimitModeLabel(), Mathf.RoundToInt(6f * s), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform limitRt = _limitModeText.GetComponent<RectTransform>();
            limitRt.sizeDelta = new Vector2(36f * s, 12f * s);
            limitRt.anchoredPosition = new Vector2(0f, 34f * s);

            // 标准化组件内部控件注册至管理器 (0 影响原画质与排版)
            if (_bgPanel != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "background", "表盘底板", _bgPanel.gameObject);
            }
            if (_meterObj != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "arc_meter", "270度圆弧度量环", _meterObj);
            }
            if (_needlePivot != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "needle_pointer", "动态指针", _needlePivot.gameObject);
            }
            if (_titleText != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "title", "表盘标题", _titleText.gameObject);
            }
            if (_valueText != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "value_readout", "主数显读数", _valueText.gameObject);
            }
            if (_unitText != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "unit_label", "单位标签", _unitText.gameObject);
            }
            if (_minScaleText != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "scale_labels", "标尺量程限值", _minScaleText.gameObject);
            }
            if (_limitModeText != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "limit_badge", "爆表模式标识", _limitModeText.gameObject);
            }
        }

        private double GetEffectiveMin() => !double.IsNaN(_overrideMin) ? _overrideMin : (Config != null ? Config.MinValue : 0.0);
        private double GetEffectiveMax() => !double.IsNaN(_overrideMax) ? _overrideMax : (Config != null && Config.MaxValue > 0 ? Config.MaxValue : 100.0);
        private double GetEffectiveCaution() => !double.IsNaN(_overrideCaution) ? _overrideCaution : (Config != null && Config.CautionThreshold > 0 ? Config.CautionThreshold : double.MaxValue);
        private double GetEffectiveWarning() => !double.IsNaN(_overrideWarning) ? _overrideWarning : (Config != null && Config.WarningThreshold > 0 ? Config.WarningThreshold : double.MaxValue);

        private string GetLimitMode()
        {
            if (!string.IsNullOrEmpty(_overrideLimitMode)) return _overrideLimitMode;
            if (Config == null) return "hard";
            string mode = Config.LimitMode;
            if (string.IsNullOrEmpty(mode)) mode = Config.IsSoftLimit ? "soft" : "hard";
            mode = mode.ToLowerInvariant();
            return mode == "soft" || mode == "none" ? mode : "hard";
        }

        private string GetLimitModeLabel()
        {
            string mode = GetLimitMode();
            return mode == "soft" ? I18n.Tr("WIDGET_GAUGE_LIMIT_SOFT", "软限") : mode == "none" ? I18n.Tr("WIDGET_GAUGE_LIMIT_OPEN", "无限制") : "MAX";
        }

        private void ConfigureMeterMaterial(ThemeConfig theme)
        {
            if (_meterMaterial == null) return;

            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color normalColor = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color inactiveColor = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color borderColor = style.GetCardBorderColor(CardStyleRole.Normal, theme);

            _meterMaterial.SetFloat("_Clockwise", 1.0f);
            _meterMaterial.SetFloat("_StartAngle", START_ANGLE);
            _meterMaterial.SetFloat("_EndAngle", END_ANGLE);
            _meterMaterial.SetFloat("_InnerRadius", 0.83f);
            _meterMaterial.SetFloat("_OuterRadius", 0.92f);
            _meterMaterial.SetFloat("_SegmentCount", 36.0f);
            _meterMaterial.SetFloat("_SegmentGap", 0.08f);
            _meterMaterial.SetColor("_ActiveColor", normalColor);
            _meterMaterial.SetColor("_InactiveColor", inactiveColor);
            _meterMaterial.SetColor("_BorderColor", borderColor);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 动态标题与单位求值
            string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            if (evalTitle != _lastTitleStr)
            {
                _lastTitleStr = evalTitle;
                if (_titleText != null) _titleText.text = evalTitle;
            }

            string evalUnit = TelemetryTokenEngine.Evaluate(_unitTemplate, telemetry);
            if (evalUnit != _lastUnitStr)
            {
                _lastUnitStr = evalUnit;
                if (_unitText != null) _unitText.text = evalUnit;
            }

            // 2. 数值通道求值
            double currentVal = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(currentVal)) currentVal = 0.0;

            double minVal = GetEffectiveMin();
            double maxVal = GetEffectiveMax();
            double range = maxVal - minVal;
            if (range <= 0.0001) range = 1.0;

            double normalized = (currentVal - minVal) / range;
            float visualFraction = Mathf.Clamp01((float)normalized);

            string limitMode = GetLimitMode();
            bool isOverflow = (limitMode == "soft" && currentVal > maxVal);
            double displayValue;

            if (limitMode == "soft" || limitMode == "none")
            {
                displayValue = currentVal;
            }
            else
            {
                displayValue = Math.Min(Math.Max(currentVal, minVal), maxVal);
            }

            // 3. 告警区间判定 (0=Normal, 1=Caution, 2=Warning)
            double cautionThresh = GetEffectiveCaution();
            double warningThresh = GetEffectiveWarning();
            bool isWarning = isOverflow || (limitMode != "none" && currentVal >= warningThresh);
            bool isCaution = !isWarning && (currentVal >= cautionThresh);
            int alertState = isWarning ? 2 : (isCaution ? 1 : 0);

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 状态变更或初次运行时更新语义色彩
            if (alertState != _lastAlertState)
            {
                _lastAlertState = alertState;
                if (isWarning)
                {
                    ApplyCard(_bgPanel, _bgOutline, CardStyleRole.Danger, theme);
                    ApplyText(_valueText, TextStyleRole.Danger, theme);
                    if (_needleImage != null) _needleImage.color = WidgetStyleManager.Meter(MeterStyleRole.Danger, theme);
                }
                else if (isCaution)
                {
                    ApplyCard(_bgPanel, _bgOutline, CardStyleRole.Warning, theme);
                    ApplyText(_valueText, TextStyleRole.Warning, theme);
                    if (_needleImage != null) _needleImage.color = WidgetStyleManager.Meter(MeterStyleRole.Warning, theme);
                }
                else
                {
                    ApplyCard(_bgPanel, _bgOutline, CardStyleRole.Normal, theme);
                    ApplyText(_valueText, TextStyleRole.PrimaryValue, theme);
                    if (_needleImage != null) _needleImage.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);
                }
            }

            // 4. 几何与指针角度更新 (脏标记保护)
            double deltaThreshold = Config != null && Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (double.IsNaN(_lastValue) || Math.Abs(currentVal - _lastValue) > deltaThreshold)
            {
                _lastValue = currentVal;

                if (_meterMaterial != null)
                {
                    Color activeMeterCol = isWarning ? style.GetMeterColor(MeterStyleRole.Danger, theme)
                        : (isCaution ? style.GetMeterColor(MeterStyleRole.Warning, theme)
                        : style.GetMeterColor(MeterStyleRole.Primary, theme));

                    _meterMaterial.SetFloat("_FillAmount", visualFraction);
                    _meterMaterial.SetColor("_ActiveColor", activeMeterCol);
                }

                if (_needlePivot != null)
                {
                    float needleAngle = START_ANGLE - visualFraction * ANGLE_SPAN - 90f;
                    _needlePivot.localEulerAngles = new Vector3(0f, 0f, needleAngle);
                }

                // 5. 更新中央数字显示
                UpdateDisplayText(displayValue);
            }
        }

        private void UpdateDisplayText(double val)
        {
            string formatted;
            if (Math.Abs(val) >= 10000.0)
            {
                formatted = $"{val / 1000.0:F1}k";
            }
            else if (Math.Abs(val) >= 100.0)
            {
                formatted = $"{val:F1}";
            }
            else if (Math.Abs(val) >= 10.0)
            {
                formatted = $"{val:F1}";
            }
            else
            {
                formatted = $"{val:F2}";
            }

            if (formatted != _lastFormattedVal)
            {
                _lastFormattedVal = formatted;
                if (_valueText != null) _valueText.text = formatted;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            CardStyleRole cardRole = _lastAlertState == 2 ? CardStyleRole.Danger : (_lastAlertState == 1 ? CardStyleRole.Warning : CardStyleRole.Normal);
            ApplyCard(_bgPanel, _bgOutline, cardRole, theme);
            ConfigureMeterMaterial(theme);

            ApplyText(_titleText, TextStyleRole.Cardinal, theme);
            TextStyleRole valRole = _lastAlertState == 2 ? TextStyleRole.Danger : (_lastAlertState == 1 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue);
            ApplyText(_valueText, valRole, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);
            ApplyText(_minScaleText, TextStyleRole.Unit, theme);
            ApplyText(_maxScaleText, TextStyleRole.Unit, theme);

            MeterStyleRole needleRole = _lastAlertState == 2 ? MeterStyleRole.Danger : (_lastAlertState == 1 ? MeterStyleRole.Warning : MeterStyleRole.Secondary);
            if (_needleImage != null) _needleImage.color = WidgetStyleManager.Meter(needleRole, theme);

            if (_limitModeText != null)
            {
                _limitModeText.text = GetLimitModeLabel();
                TextStyleRole limitRole = GetLimitMode() == "soft" ? TextStyleRole.Warning : TextStyleRole.Unit;
                ApplyText(_limitModeText, limitRole, theme);
            }
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
