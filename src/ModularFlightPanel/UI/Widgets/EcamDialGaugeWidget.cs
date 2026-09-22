using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

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
    /// </summary>
    public class EcamDialGaugeWidget : BaseFlightWidget
    {
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

        private Color _normalColor;
        private Color _cautionColor;
        private Color _warningColor;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float size = 112f * CurrentDpiScale;
            RectTransform.sizeDelta = new Vector2(size, size);

            // 1. 卡片底衬 (轻微半透明圆角底板)
            _bgPanel = gameObject.AddComponent<Image>();
            Color bgCol = theme.FrameBgColor;
            _bgPanel.color = new Color(bgCol.r, bgCol.g, bgCol.b, 0.78f);

            _bgOutline = gameObject.AddComponent<Outline>();
            Color borderCol = theme.FrameBorderColor;
            _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            _bgOutline.effectDistance = new Vector2(1f * CurrentDpiScale, 1f * CurrentDpiScale);

            // 2. 马蹄形弧线度量环 (RadialSegmentedMeter.shader)
            _meterObj = new GameObject("ECAM_Arc_Meter", typeof(RectTransform), typeof(Image));
            _meterObj.transform.SetParent(transform, false);
            RectTransform meterRt = _meterObj.GetComponent<RectTransform>();
            meterRt.sizeDelta = new Vector2(size * 0.92f, size * 0.92f);
            meterRt.anchoredPosition = Vector2.zero;

            _meterImage = _meterObj.GetComponent<Image>();
            _meterMaterial = new Material(AssetLoader.RadialMeterShader);
            _meterImage.material = _meterMaterial;

            ConfigureMeterMaterial(theme);

            // 3. 动态指针 (Needle Pointer)
            GameObject pivotObj = new GameObject("Needle_Pivot", typeof(RectTransform));
            pivotObj.transform.SetParent(transform, false);
            _needlePivot = pivotObj.GetComponent<RectTransform>();
            _needlePivot.sizeDelta = Vector2.zero;
            _needlePivot.anchoredPosition = Vector2.zero;

            GameObject needleObj = new GameObject("Needle_Bar", typeof(RectTransform), typeof(Image));
            needleObj.transform.SetParent(_needlePivot, false);
            RectTransform needleRt = needleObj.GetComponent<RectTransform>();
            needleRt.sizeDelta = new Vector2(2.5f * CurrentDpiScale, 18f * CurrentDpiScale);
            needleRt.pivot = new Vector2(0.5f, 0f);
            needleRt.anchoredPosition = new Vector2(0f, (size * 0.46f) - (20f * CurrentDpiScale));
            _needleImage = needleObj.GetComponent<Image>();
            _needleImage.color = theme.AccentPrimary;

            // 4. 标题、数显与单位 (ECAM 风格排版)
            int titleSize = Mathf.RoundToInt(10f * CurrentDpiScale);
            _titleText = UIFactory.CreateText(transform, "ECAM_Title", config.DisplayName, titleSize, TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform trt = _titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(size - 10f * CurrentDpiScale, 16f * CurrentDpiScale);
            trt.anchoredPosition = new Vector2(0f, 22f * CurrentDpiScale);

            int valSize = Mathf.RoundToInt(17f * CurrentDpiScale);
            _valueText = UIFactory.CreateText(transform, "ECAM_Value", "0.0", valSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform vrt = _valueText.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(size - 10f * CurrentDpiScale, 24f * CurrentDpiScale);
            vrt.anchoredPosition = new Vector2(0f, -2f * CurrentDpiScale);

            int unitSize = Mathf.RoundToInt(9f * CurrentDpiScale);
            _unitText = UIFactory.CreateText(transform, "ECAM_Unit", config.UnitLabel, unitSize, TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform urt = _unitText.GetComponent<RectTransform>();
            urt.sizeDelta = new Vector2(size - 10f * CurrentDpiScale, 14f * CurrentDpiScale);
            urt.anchoredPosition = new Vector2(0f, -22f * CurrentDpiScale);

            // 刻度两端标称数字 (左下起点与右下满格)
            int scaleFontSize = Mathf.RoundToInt(9f * CurrentDpiScale);
            _minScaleText = UIFactory.CreateText(transform, "Min_Scale", $"{config.MinValue:F0}", scaleFontSize, TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform minRt = _minScaleText.GetComponent<RectTransform>();
            minRt.sizeDelta = new Vector2(30f * CurrentDpiScale, 14f * CurrentDpiScale);
            minRt.anchoredPosition = new Vector2(-28f * CurrentDpiScale, -38f * CurrentDpiScale);

            _maxScaleText = UIFactory.CreateText(transform, "Max_Scale", $"{config.MaxValue:F0}", scaleFontSize, TextAnchor.MiddleRight, theme.TextAccentColor);
            RectTransform maxRt = _maxScaleText.GetComponent<RectTransform>();
            maxRt.sizeDelta = new Vector2(30f * CurrentDpiScale, 14f * CurrentDpiScale);
            maxRt.anchoredPosition = new Vector2(28f * CurrentDpiScale, -38f * CurrentDpiScale);
        }

        private void ConfigureMeterMaterial(ThemeConfig theme)
        {
            if (_meterMaterial == null) return;

            _normalColor = theme.AccentPrimary;
            _cautionColor = theme.WarningColor;
            _warningColor = Color.red;

            _meterMaterial.SetFloat("_Clockwise", 1.0f);
            _meterMaterial.SetFloat("_StartAngle", START_ANGLE);
            _meterMaterial.SetFloat("_EndAngle", END_ANGLE);
            _meterMaterial.SetFloat("_InnerRadius", 0.74f);
            _meterMaterial.SetFloat("_OuterRadius", 0.94f);
            _meterMaterial.SetFloat("_SegmentCount", 24.0f);
            _meterMaterial.SetFloat("_SegmentGap", 0.12f);
            _meterMaterial.SetColor("_ActiveColor", _normalColor);
            _meterMaterial.SetColor("_InactiveColor", theme.InactiveMeterColor);
            _meterMaterial.SetColor("_BorderColor", theme.FrameBorderColor);
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            if (telemetry == null || _meterMaterial == null) return;

            // 获取驱动数值
            double currentVal = TelemetryTokenEngine.EvaluateNumeric(Config.NumericToken, telemetry);
            if (double.IsNaN(currentVal)) currentVal = 0.0;

            double range = Config.MaxValue - Config.MinValue;
            if (range <= 0.0001) range = 1.0;

            double normalized = (currentVal - Config.MinValue) / range;
            float visualFraction = Mathf.Clamp01((float)normalized);

            // 1. 软上限 vs 有上限 核心判断
            bool isOverflow = false;
            double displayValue;

            if (Config.IsSoftLimit)
            {
                // 软上限模式：视觉卡在 1.0 满格，若当前值超过标称满量程触发爆表告警，但数显绝不截断
                displayValue = currentVal;
                if (currentVal > Config.MaxValue)
                {
                    isOverflow = true;
                }
            }
            else
            {
                // 有上限模式：严格截断在 [Min, Max]
                displayValue = Math.Min(Math.Max(currentVal, Config.MinValue), Config.MaxValue);
            }

            // 2. 状态色彩判定 (Normal -> Caution -> Warning / Overflow)
            bool isWarning = isOverflow || (currentVal >= Config.WarningThreshold);
            bool isCaution = !isWarning && (currentVal >= Config.CautionThreshold);

            Color activeColor = _normalColor;
            if (isWarning)
            {
                // 爆表或危险：红色脉冲闪烁
                float flash = Mathf.PingPong(Time.time * 5f, 1f);
                activeColor = Color.Lerp(Color.red, new Color(1f, 0.3f, 0.3f), flash);
            }
            else if (isCaution)
            {
                activeColor = _cautionColor;
            }

            // 3. 更新着色器与指针角度
            _meterMaterial.SetFloat("_FillAmount", visualFraction);
            _meterMaterial.SetColor("_ActiveColor", activeColor);

            // 指针角度: 从 225° 顺时针旋转 ANGLE_SPAN (270°)
            // Unity UI 旋转: 逆时针为正，顺时针为负，故从 225° 递减
            float needleAngle = START_ANGLE - visualFraction * ANGLE_SPAN - 90f; // -90 使指针朝向切线外侧
            _needlePivot.localEulerAngles = new Vector3(0f, 0f, needleAngle);
            if (_needleImage != null) _needleImage.color = activeColor;

            // 4. 更新中央数字显示 (格式化)
            UpdateDisplayText(displayValue, isWarning);
        }

        private void UpdateDisplayText(double val, bool isWarning)
        {
            if (Math.Abs(val) >= 10000.0)
            {
                _valueText.text = $"{val / 1000.0:F1}k";
            }
            else if (Math.Abs(val) >= 100.0)
            {
                _valueText.text = $"{val:F1}";
            }
            else if (Math.Abs(val) >= 10.0)
            {
                _valueText.text = $"{val:F1}";
            }
            else
            {
                _valueText.text = $"{val:F2}";
            }

            if (isWarning)
            {
                _valueText.color = Color.red;
                _bgOutline.effectColor = Color.red;
            }
            else
            {
                _valueText.color = ThemeManager.Instance.CurrentTheme.TextPrimaryColor;
                Color borderCol = ThemeManager.Instance.CurrentTheme.FrameBorderColor;
                _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            Color bgCol = theme.FrameBgColor;
            if (_bgPanel != null)
                _bgPanel.color = new Color(bgCol.r, bgCol.g, bgCol.b, 0.78f);

            Color borderCol = theme.FrameBorderColor;
            if (_bgOutline != null)
                _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);

            ConfigureMeterMaterial(theme);

            if (_titleText != null) _titleText.color = theme.AccentSecondary;
            if (_valueText != null) _valueText.color = theme.TextPrimaryColor;
            if (_unitText != null) _unitText.color = theme.TextAccentColor;
            if (_minScaleText != null) _minScaleText.color = theme.TextAccentColor;
            if (_maxScaleText != null) _maxScaleText.color = theme.TextAccentColor;
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
