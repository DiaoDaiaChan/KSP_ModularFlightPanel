using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    public enum BarGaugeKind
    {
        Throttle,
        AtmosphericPressure,
        DynamicPressure
    }

    /// <summary>
    /// 现代航电垂直高精光柱带 (Avionics Precision Vertical Bar Gauge)
    /// 100% 遵照 MFP 标准：通配符驱动、主题语义管道、脏标记保护、0 颜色字面量。
    /// </summary>
    public class AvionicsBarGaugeWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        private BarGaugeKind _kind;

        private RectTransform _trackRt;
        private Image _trackBg;
        private Outline _trackOutline;

        private RectTransform _fillBarRt;
        private Image _fillBarImage;

        private RectTransform _caretRt;
        private Image _caretImage;

        // 顶部铭牌与数显
        private GameObject _topTagBox;
        private Text _topTagTitle;
        private Text _topTagValue;

        // 底部档位与读数标牌盒
        private GameObject _bottomTagBox;
        private Image _bottomTagBg;
        private Outline _bottomTagOutline;
        private Text _bottomTagText;

        // 警戒线 (例如 Max Q 跨音速动压缓冲线)
        private GameObject _cautionLineObj;
        private RectTransform _cautionLineRt;
        private Image _cautionLineImg;

        // 通配符通道与模板
        private string _valueToken = "{THR}";
        private string _titleTemplate = "THR";
        private string _bottomTagTemplate = "IDLE";
        private double _minVal = 0.0;
        private double _maxVal = 100.0;
        private double _cautionVal = 0.0;
        private double _warningVal = 0.0;

        // 运行时脏检查缓存
        private double _lastValue = double.NaN;
        private float _lastFillHeight = -1f;
        private string _lastTitleStr = string.Empty;
        private string _lastValueStr = string.Empty;
        private string _lastBottomStr = string.Empty;
        private CardStyleRole _currentRole = CardStyleRole.Normal;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float barWidth = 20f * s;
            float barHeight = 180f * s;

            // 依据 WidgetId 或 NumericToken 判定初始预设类型
            if (config != null && (config.NumericToken == "{ATM}" || config.WidgetId == "gauge.barometer" || config.WidgetId.Contains("baro") || config.WidgetId.Contains("atm")))
            {
                _kind = BarGaugeKind.AtmosphericPressure;
                _valueToken = "{ATM}";
                _titleTemplate = "ATM";
                _bottomTagTemplate = "SEA";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 1.0;
            }
            else if (config != null && (config.NumericToken == "{Q}" || config.WidgetId == "gauge.q" || config.WidgetId.Contains("q")))
            {
                _kind = BarGaugeKind.DynamicPressure;
                _valueToken = "{Q}";
                _titleTemplate = "Q";
                _bottomTagTemplate = "MAX Q";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 35.0;
                _cautionVal = config.CautionThreshold > 0 ? config.CautionThreshold : 20.0;
                _warningVal = config.WarningThreshold > 0 ? config.WarningThreshold : 28.0;
            }
            else
            {
                _kind = BarGaugeKind.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THR}";
                _titleTemplate = "THR";
                _bottomTagTemplate = "IDLE";
                _minVal = config != null && config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config != null && config.MaxValue > 0 ? config.MaxValue : 100.0;
            }

            if (!string.IsNullOrEmpty(config?.DisplayName) && config.DisplayName.Length <= 4 && !config.DisplayName.Contains("带"))
            {
                _titleTemplate = config.DisplayName;
            }

            ParseCustomTemplate(config?.CustomTemplate);

            RectTransform.sizeDelta = new Vector2(barWidth, barHeight);

            // 1. 构建光柱玻璃底轨 (Track)
            BuildTrack(barWidth, barHeight, s, theme);

            // 2. 构建平滑动态充填光柱 (Fill Bar)
            BuildFillBar(barWidth, barHeight, s, theme);

            // 3. 构建激光微刻度线 (0%, 25%, 50%, 75%, 100%)
            BuildTickGraduations(barWidth, barHeight, s, theme);

            // 4. 构建动态游标 (Caret Indicator)
            BuildCaret(barWidth, s, theme);

            // 5. 构建顶部技术铭牌与数值显示
            BuildTopTag(barWidth, s, theme);

            // 6. 构建底部档位与辅助标签
            BuildBottomTag(barWidth, barHeight, s, theme);

            // 7. 如为动压气压计或配置了警戒线，构建警戒基准线
            if (_kind == BarGaugeKind.DynamicPressure || _cautionVal > 0)
            {
                BuildCautionCue(barWidth, barHeight, s, theme);
            }

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
                else if (k == "TITLE" || k == "LABEL" || k == "NAME") _titleTemplate = v;
                else if (k == "TAG" || k == "BOTTOM" || k == "BTM") _bottomTagTemplate = v;
                else if (k == "MIN") { if (double.TryParse(v, out double mn)) _minVal = mn; }
                else if (k == "MAX") { if (double.TryParse(v, out double mx)) _maxVal = mx; }
                else if (k == "CAUTION") { if (double.TryParse(v, out double c)) _cautionVal = c; }
                else if (k == "WARNING") { if (double.TryParse(v, out double w)) _warningVal = w; }
            }
        }

        private void BuildTrack(float w, float h, float s, ThemeConfig theme)
        {
            GameObject trackObj = new GameObject("Gauge_Track", typeof(RectTransform), typeof(Image));
            trackObj.transform.SetParent(transform, false);

            _trackRt = trackObj.GetComponent<RectTransform>();
            _trackRt.sizeDelta = new Vector2(w, h);
            _trackRt.anchoredPosition = Vector2.zero;

            _trackBg = trackObj.GetComponent<Image>();
            _trackBg.color = Color.clear;

            _trackOutline = trackObj.AddComponent<Outline>();
            _trackOutline.effectDistance = new Vector2(1f * s, 1f * s);
        }

        private void BuildFillBar(float w, float h, float s, ThemeConfig theme)
        {
            GameObject fillObj = new GameObject("Gauge_FillBar", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(_trackRt, false);

            _fillBarRt = fillObj.GetComponent<RectTransform>();
            _fillBarRt.anchorMin = new Vector2(0f, 0f);
            _fillBarRt.anchorMax = new Vector2(1f, 0f);
            _fillBarRt.pivot = new Vector2(0.5f, 0f);
            _fillBarRt.sizeDelta = new Vector2(-4f * s, 0f);
            _fillBarRt.anchoredPosition = new Vector2(0f, 2f * s);

            _fillBarImage = fillObj.GetComponent<Image>();
            _fillBarImage.color = WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Primary, theme);
        }

        private void BuildTickGraduations(float w, float h, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color tickColorWithAlpha = style.GetLineColor(LineWeight.Strong, theme);

            for (int i = 0; i <= 4; i++)
            {
                float frac = i / 4f;
                float y = (-h * 0.5f) + (h * frac);

                GameObject tick = UIFactory.CreatePanel(_trackRt, $"Tick_{i * 25}",
                    new Vector2(w * 0.45f, 1.2f * s),
                    new Vector2(Config.IsLeftOrientation ? (-w * 0.25f) : (w * 0.25f), y),
                    tickColorWithAlpha);
                tick.GetComponent<Image>().raycastTarget = false;
            }
        }

        private void BuildCaret(float w, float s, ThemeConfig theme)
        {
            GameObject caretObj = new GameObject("Gauge_Caret", typeof(RectTransform), typeof(Image));
            caretObj.transform.SetParent(_trackRt, false);

            _caretRt = caretObj.GetComponent<RectTransform>();
            _caretRt.sizeDelta = new Vector2(6f * s, 6f * s);
            _caretRt.localEulerAngles = new Vector3(0f, 0f, 45f);

            float xOffset = Config.IsLeftOrientation ? (w * 0.5f + 1f * s) : (-w * 0.5f - 1f * s);
            _caretRt.anchoredPosition = new Vector2(xOffset, 0f);

            _caretImage = caretObj.GetComponent<Image>();
            _caretImage.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);
            _caretImage.raycastTarget = false;
        }

        private void BuildTopTag(float w, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Vector2 tagSize = new Vector2(36f * s, 22f * s);
            Vector2 tagPos = new Vector2(0f, (180f * s * 0.5f) + 12f * s);

            _topTagBox = UIFactory.CreatePanel(transform, "Top_Tag_Box", tagSize, tagPos, Color.clear);
            Outline tagOutline = _topTagBox.AddComponent<Outline>();
            tagOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _topTagTitle = UIFactory.CreateText(_topTagBox.transform, "Title", _titleTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trt = _topTagTitle.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(tagSize.x, 11f * s);
            trt.anchoredPosition = new Vector2(0f, tagSize.y * 0.22f);

            _topTagValue = UIFactory.CreateText(_topTagBox.transform, "Value", "---",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform vrt = _topTagValue.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(tagSize.x, 14f * s);
            vrt.anchoredPosition = new Vector2(0f, -tagSize.y * 0.18f);
        }

        private void BuildBottomTag(float w, float h, float s, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(36f * s, 16f * s);
            Vector2 tagPos = new Vector2(0f, (-h * 0.5f) - 10f * s);

            _bottomTagBox = UIFactory.CreatePanel(transform, "Bottom_Tag_Box", tagSize, tagPos, Color.clear);
            _bottomTagBg = _bottomTagBox.GetComponent<Image>();

            _bottomTagOutline = _bottomTagBox.AddComponent<Outline>();
            _bottomTagOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _bottomTagText = UIFactory.CreateText(_bottomTagBox.transform, "Bottom_Tag_Text", _bottomTagTemplate,
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Muted, theme));

            RectTransform brt = _bottomTagText.GetComponent<RectTransform>();
            brt.sizeDelta = tagSize;
            brt.anchoredPosition = Vector2.zero;
            _bottomTagText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildCautionCue(float w, float h, float s, ThemeConfig theme)
        {
            double range = _maxVal - _minVal;
            float cautionFrac = range > 0.001 ? Mathf.Clamp01((float)((_cautionVal - _minVal) / range)) : 0.7f;
            float y = (-h * 0.5f) + (h * cautionFrac);

            _cautionLineObj = UIFactory.CreatePanel(_trackRt, "Caution_Cue",
                new Vector2(w + 4f * s, 1.8f * s), new Vector2(0f, y),
                WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Warning, theme));

            _cautionLineRt = _cautionLineObj.GetComponent<RectTransform>();
            _cautionLineImg = _cautionLineObj.GetComponent<Image>();
            _cautionLineImg.raycastTarget = false;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ThemeConfig theme = style.CurrentTheme;

            float s = CurrentDpiScale;
            float totalH = 180f * s;
            float usableH = totalH - (4f * s);

            // 1. 动态标题与底部标签求值 (文案 100% 通配符可自定义)
            if (_topTagTitle != null)
            {
                string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
                if (evalTitle != _lastTitleStr)
                {
                    _lastTitleStr = evalTitle;
                    _topTagTitle.text = evalTitle;
                }
            }

            // 2. 数值求值 (驱动光柱高度与游标)
            double val = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(val)) val = 0.0;

            double delta = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            bool valueSignificantlyChanged = double.IsNaN(_lastValue) || Math.Abs(val - _lastValue) > delta;

            if (valueSignificantlyChanged)
            {
                _lastValue = val;
                double range = _maxVal - _minVal;
                float fraction = range > 0.0001 ? Mathf.Clamp01((float)((val - _minVal) / range)) : 0f;
                float fillHeight = usableH * fraction;

                if (Mathf.Abs(fillHeight - _lastFillHeight) > 0.5f)
                {
                    _lastFillHeight = fillHeight;
                    if (_fillBarRt != null)
                    {
                        _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
                    }
                    if (_caretRt != null)
                    {
                        float caretY = (-totalH * 0.5f) + (2f * s) + fillHeight;
                        float xOffset = Config.IsLeftOrientation ? (10f * s + 1f * s) : (-10f * s - 1f * s);
                        _caretRt.anchoredPosition = new Vector2(xOffset, caretY);
                    }
                }

                // 语义颜色角色流转
                CardStyleRole targetRole = CardStyleRole.Normal;
                if (_warningVal > 0 && val >= _warningVal) targetRole = CardStyleRole.Danger;
                else if (_cautionVal > 0 && val >= _cautionVal) targetRole = CardStyleRole.Warning;

                if (targetRole != _currentRole)
                {
                    _currentRole = targetRole;
                    MeterStyleRole meterRole = targetRole == CardStyleRole.Danger
                        ? MeterStyleRole.Danger
                        : (targetRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                    Color meterCol = style.GetMeterColor(meterRole, theme);
                    if (_fillBarImage != null) _fillBarImage.color = meterCol;
                    if (_caretImage != null) _caretImage.color = meterCol;
                }
            }

            // 3. 读数文本更新与脏检查
            if (_topTagValue != null)
            {
                string str = TelemetryTokenEngine.Evaluate(_valueToken, telemetry);
                if (str != _lastValueStr)
                {
                    _lastValueStr = str;
                    _topTagValue.text = str;
                }
            }

            // 4. 底部微型标牌求值与更新
            if (_bottomTagText != null)
            {
                string btmStr = TelemetryTokenEngine.Evaluate(_bottomTagTemplate, telemetry);
                if (btmStr != _lastBottomStr)
                {
                    _lastBottomStr = btmStr;
                    _bottomTagText.text = btmStr;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_trackBg != null) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, theme);

            if (_topTagBox != null)
            {
                Image topImg = _topTagBox.GetComponent<Image>();
                Outline topOutline = _topTagBox.GetComponent<Outline>();
                ApplyCard(topImg, topOutline, CardStyleRole.Normal, theme);
            }

            if (_topTagTitle != null) ApplyText(_topTagTitle, TextStyleRole.Label, theme);
            if (_topTagValue != null) ApplyText(_topTagValue, TextStyleRole.PrimaryValue, theme);

            if (_bottomTagBg != null) ApplyCard(_bottomTagBg, _bottomTagOutline, CardStyleRole.Normal, theme);
            if (_bottomTagText != null) ApplyText(_bottomTagText, TextStyleRole.Muted, theme);

            MeterStyleRole meterRole = _currentRole == CardStyleRole.Danger
                ? MeterStyleRole.Danger
                : (_currentRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color meterCol = style.GetMeterColor(meterRole, theme);
            if (_fillBarImage != null) _fillBarImage.color = meterCol;
            if (_caretImage != null) _caretImage.color = meterCol;
            if (_cautionLineImg != null) _cautionLineImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
