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
    /// 告别粗糙块状与像素齿印，采用微光玻璃轨道、平滑连续渐变光柱、
    /// 亚像素激光微刻度以及数字化技术铭牌，
    /// 专为紧密嵌合在速度带与高度带外侧打造 (间隙仅 1px)。
    /// 严格继承 BaseFlightWidget，所有样式、量程与数据全生命周期数据驱动。
    /// </summary>
    public class AvionicsBarGaugeWidget : BaseFlightWidget
    {
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

        // 底部挡位与读数标牌盒
        private GameObject _bottomTagBox;
        private Image _bottomTagBg;
        private Outline _bottomTagOutline;
        private Text _bottomTagText;

        // 警戒线 (例如 Max Q 跨音速动压缓冲线)
        private GameObject _cautionLineObj;
        private RectTransform _cautionLineRt;
        private Image _cautionLineImg;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float barWidth = 20f * s;
            float barHeight = 180f * s;

            // 依据 WidgetId 或 NumericToken 判定仪表面向
            if (config.NumericToken == "{ATM}" || config.WidgetId == "gauge.barometer" || config.WidgetId.Contains("baro") || config.WidgetId.Contains("atm"))
            {
                _kind = BarGaugeKind.AtmosphericPressure;
            }
            else if (config.NumericToken == "{Q}" || config.WidgetId == "gauge.q" || config.WidgetId.Contains("q"))
            {
                _kind = BarGaugeKind.DynamicPressure;
            }
            else
            {
                _kind = BarGaugeKind.Throttle;
            }

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

            // 7. 如为动压气压计，构建 Max Q 警戒基准线
            if (_kind == BarGaugeKind.DynamicPressure)
            {
                BuildMaxQCue(barWidth, barHeight, s, theme);
            }

            ApplyTheme(theme);
        }

        private void BuildTrack(float w, float h, float s, ThemeConfig theme)
        {
            GameObject trackObj = new GameObject("Gauge_Track", typeof(RectTransform), typeof(Image));
            trackObj.transform.SetParent(transform, false);

            _trackRt = trackObj.GetComponent<RectTransform>();
            _trackRt.sizeDelta = new Vector2(w, h);
            _trackRt.anchoredPosition = Vector2.zero;

            _trackBg = trackObj.GetComponent<Image>();
            _trackBg.color = theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.85f);

            _trackOutline = trackObj.AddComponent<Outline>();
            Color border = theme != null ? (Color)theme.FrameBorderColor : new Color(0.2f, 0.5f, 0.8f, 0.5f);
            _trackOutline.effectColor = new Color(border.r, border.g, border.b, 0.35f);
            _trackOutline.effectDistance = new Vector2(1f * s, 1f * s);
        }

        private void BuildFillBar(float w, float h, float s, ThemeConfig theme)
        {
            // 光柱本体 (从底部向上平滑伸缩，无分段积木空隙)
            GameObject fillObj = new GameObject("Gauge_FillBar", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(_trackRt, false);

            _fillBarRt = fillObj.GetComponent<RectTransform>();
            _fillBarRt.anchorMin = new Vector2(0f, 0f);
            _fillBarRt.anchorMax = new Vector2(1f, 0f);
            _fillBarRt.pivot = new Vector2(0.5f, 0f);
            _fillBarRt.sizeDelta = new Vector2(-4f * s, 0f); // 左右留 2px 微边框
            _fillBarRt.anchoredPosition = new Vector2(0f, 2f * s);

            _fillBarImage = fillObj.GetComponent<Image>();
            if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                _fillBarImage.color = new Color(0.15f, 0.58f, 0.95f, 0.92f); // 现代航空天蓝大气带
            }
            else
            {
                _fillBarImage.color = theme != null ? (Color)theme.AccentPrimary : Color.cyan;
            }
        }

        private void BuildTickGraduations(float w, float h, float s, ThemeConfig theme)
        {
            Color tickCol = theme != null ? (Color)theme.FrameBorderColor : Color.gray;
            Color tickColorWithAlpha = new Color(tickCol.r, tickCol.g, tickCol.b, 0.45f);

            // 5 级基准刻度线 (0%, 25%, 50%, 75%, 100%)
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
            _caretRt.localEulerAngles = new Vector3(0f, 0f, 45f); // 45度菱形光标

            float xOffset = Config.IsLeftOrientation ? (w * 0.5f + 1f * s) : (-w * 0.5f - 1f * s);
            _caretRt.anchoredPosition = new Vector2(xOffset, 0f);

            _caretImage = caretObj.GetComponent<Image>();
            _caretImage.color = _kind == BarGaugeKind.AtmosphericPressure ? Color.white : (theme != null ? (Color)theme.AccentPrimary : Color.white);
            _caretImage.raycastTarget = false;
        }

        private void BuildTopTag(float w, float s, ThemeConfig theme)
        {
            // 顶部技术铭牌：宽 36px，高 22px，水平对齐速度/高度带标头 (Y = 102px)
            Vector2 tagSize = new Vector2(36f * s, 22f * s);
            Vector2 tagPos = new Vector2(0f, (180f * s * 0.5f) + 12f * s);

            _topTagBox = UIFactory.CreatePanel(transform, "Top_Tag_Box", tagSize, tagPos,
                theme != null ? (Color)theme.FrameBgColor : Color.black);

            Outline tagOutline = _topTagBox.AddComponent<Outline>();
            Color border = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            tagOutline.effectColor = new Color(border.r, border.g, border.b, 0.45f);
            tagOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 铭牌标题 (如 THR / ATM / Q)
            string titleStr = _kind == BarGaugeKind.Throttle ? "THR" : (_kind == BarGaugeKind.AtmosphericPressure ? "ATM" : "Q");
            _topTagTitle = UIFactory.CreateText(_topTagBox.transform, "Title", titleStr,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.UpperCenter,
                theme != null ? (Color)theme.AccentSecondary : Color.cyan);
            RectTransform trt = _topTagTitle.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(tagSize.x, 11f * s);
            trt.anchoredPosition = new Vector2(0f, tagSize.y * 0.22f);

            // 铭牌数值 (如 85% / 1.00 / 24.5)
            string initVal = _kind == BarGaugeKind.Throttle ? "0%" : (_kind == BarGaugeKind.AtmosphericPressure ? "1.00" : "0.0");
            _topTagValue = UIFactory.CreateText(_topTagBox.transform, "Value", initVal,
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.LowerCenter,
                theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            RectTransform vrt = _topTagValue.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(tagSize.x, 14f * s);
            vrt.anchoredPosition = new Vector2(0f, -tagSize.y * 0.18f);
        }

        private void BuildBottomTag(float w, float h, float s, ThemeConfig theme)
        {
            // 底部标牌盒：宽 36px，高 16px，与顶部 TopTag 风格统一
            Vector2 tagSize = new Vector2(36f * s, 16f * s);
            Vector2 tagPos = new Vector2(0f, (-h * 0.5f) - 10f * s);

            _bottomTagBox = UIFactory.CreatePanel(transform, "Bottom_Tag_Box", tagSize, tagPos,
                theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.06f, 0.09f, 0.90f));
            _bottomTagBg = _bottomTagBox.GetComponent<Image>();

            _bottomTagOutline = _bottomTagBox.AddComponent<Outline>();
            Color border = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            _bottomTagOutline.effectColor = new Color(border.r, border.g, border.b, 0.45f);
            _bottomTagOutline.effectDistance = new Vector2(1f * s, 1f * s);

            string botStr = _kind == BarGaugeKind.Throttle ? "IDLE" : (_kind == BarGaugeKind.AtmosphericPressure ? "SEA" : "ATM");
            _bottomTagText = UIFactory.CreateText(_bottomTagBox.transform, "Bottom_Tag_Text", botStr,
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.TextAccentColor : Color.gray);

            RectTransform brt = _bottomTagText.GetComponent<RectTransform>();
            brt.sizeDelta = tagSize;
            brt.anchoredPosition = Vector2.zero;
            _bottomTagText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildMaxQCue(float w, float h, float s, ThemeConfig theme)
        {
            // Max Q 跨音速动压参考警戒线 (标称 25 kPa 在 35 kPa 满量程中约占 71.4%)
            float maxQFraction = 25f / 35f;
            float y = (-h * 0.5f) + (h * maxQFraction);

            _cautionLineObj = UIFactory.CreatePanel(_trackRt, "MaxQ_Cue",
                new Vector2(w + 4f * s, 1.8f * s), new Vector2(0f, y),
                theme != null ? (Color)theme.WarningColor : Color.yellow);

            _cautionLineRt = _cautionLineObj.GetComponent<RectTransform>();
            _cautionLineImg = _cautionLineObj.GetComponent<Image>();
            _cautionLineImg.raycastTarget = false;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            float s = CurrentDpiScale;
            float totalH = 180f * s;
            float usableH = totalH - (4f * s);

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color primaryCol = theme != null ? (Color)theme.AccentPrimary : Color.cyan;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.yellow;
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;

            if (_kind == BarGaugeKind.Throttle)
            {
                // 油门推力 (0..100%)
                float thr = telemetry.Throttle;
                if (float.IsNaN(thr)) thr = 0f;
                thr = Mathf.Clamp01(thr);

                float fillHeight = usableH * thr;
                if (_fillBarRt != null)
                {
                    _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
                }

                // 游标位置
                if (_caretRt != null)
                {
                    float caretY = (-totalH * 0.5f) + (2f * s) + fillHeight;
                    float xOffset = Config.IsLeftOrientation ? (10f * s + 1f * s) : (-10f * s - 1f * s);
                    _caretRt.anchoredPosition = new Vector2(xOffset, caretY);
                    if (_caretImage != null) _caretImage.color = primaryCol;
                }

                if (_topTagValue != null)
                {
                    _topTagValue.text = $"{Mathf.RoundToInt(thr * 100f)}%";
                }

                if (_bottomTagText != null)
                {
                    _bottomTagText.text = thr > 0.01f ? "ON" : "IDLE";
                }
            }
            else if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                // 大气压强指示 (0..1.00 atm) - 对标经典大气仪表，海平面 1.00 atm，真空 0.00 atm
                double atm = telemetry.AtmosphericPressure;
                if (double.IsNaN(atm) || atm < 0.0) atm = 0.0;
                float frac = Mathf.Clamp01((float)atm);

                float fillHeight = usableH * frac;
                if (_fillBarRt != null)
                {
                    _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
                }

                if (_fillBarImage != null)
                {
                    // 纯粹清澈的航空现代蓝渐变带 (0.15, 0.58, 0.95)
                    _fillBarImage.color = new Color(0.15f, 0.58f, 0.95f, 0.92f);
                }

                if (_caretRt != null)
                {
                    float caretY = (-totalH * 0.5f) + (2f * s) + fillHeight;
                    float xOffset = Config.IsLeftOrientation ? (10f * s + 1f * s) : (-10f * s - 1f * s);
                    _caretRt.anchoredPosition = new Vector2(xOffset, caretY);
                    if (_caretImage != null) _caretImage.color = Color.white;
                }

                if (_topTagValue != null)
                {
                    _topTagValue.text = $"{atm:F2}";
                }

                if (_bottomTagText != null)
                {
                    _bottomTagText.text = atm < 0.01 ? "VAC" : (atm >= 0.99 ? "1.00" : $"{atm:F2}");
                }
            }
            else
            {
                // 动压气压计 Q (0..35 kPa)
                double qVal = telemetry.DynamicPressure;
                if (double.IsNaN(qVal)) qVal = 0.0;
                float qFraction = Mathf.Clamp01((float)(qVal / (Config.MaxValue > 0 ? Config.MaxValue : 35.0)));

                float fillHeight = usableH * qFraction;
                if (_fillBarRt != null)
                {
                    _fillBarRt.sizeDelta = new Vector2(-4f * s, fillHeight);
                }

                // 色彩动态分级：正常青绿 -> Max Q 跨音速震颤 (20~28 kPa) 琥珀橙 -> 危险红 (>28 kPa)
                Color barCol = primaryCol;
                if (qVal > 28.0)
                {
                    barCol = new Color(1f, 0.25f, 0.25f, 1f); // 结构警示红
                }
                else if (qVal > 20.0)
                {
                    barCol = warnCol; // Max Q 琥珀警示
                }

                if (_fillBarImage != null) _fillBarImage.color = barCol;

                if (_caretRt != null)
                {
                    float caretY = (-totalH * 0.5f) + (2f * s) + fillHeight;
                    float xOffset = Config.IsLeftOrientation ? (10f * s + 1f * s) : (-10f * s - 1f * s);
                    _caretRt.anchoredPosition = new Vector2(xOffset, caretY);
                    if (_caretImage != null) _caretImage.color = barCol;
                }

                if (_topTagValue != null)
                {
                    _topTagValue.text = $"{qVal:F1}";
                }

                if (_bottomTagText != null)
                {
                    _bottomTagText.text = $"{qVal:F0}k";
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            if (_trackBg != null) _trackBg.color = theme.FrameBgColor;
            if (_trackOutline != null)
            {
                Color border = theme.FrameBorderColor;
                _trackOutline.effectColor = new Color(border.r, border.g, border.b, 0.35f);
            }

            if (_topTagBox != null)
            {
                var img = _topTagBox.GetComponent<Image>();
                if (img != null) img.color = theme.FrameBgColor;
                var ol = _topTagBox.GetComponent<Outline>();
                Color border = theme.FrameBorderColor;
                if (ol != null) ol.effectColor = new Color(border.r, border.g, border.b, 0.45f);
            }

            if (_topTagTitle != null) _topTagTitle.color = theme.AccentSecondary;
            if (_topTagValue != null) _topTagValue.color = theme.TextPrimaryColor;

            if (_bottomTagBg != null) _bottomTagBg.color = theme.FrameBgColor;
            if (_bottomTagOutline != null)
            {
                Color border = theme.FrameBorderColor;
                _bottomTagOutline.effectColor = new Color(border.r, border.g, border.b, 0.45f);
            }
            if (_bottomTagText != null) _bottomTagText.color = theme.TextAccentColor;

            if (_fillBarImage != null)
            {
                if (_kind == BarGaugeKind.AtmosphericPressure)
                {
                    _fillBarImage.color = new Color(0.15f, 0.58f, 0.95f, 0.92f);
                }
                else
                {
                    _fillBarImage.color = theme.AccentPrimary;
                }
            }
            if (_caretImage != null)
            {
                _caretImage.color = (_kind == BarGaugeKind.AtmosphericPressure) ? Color.white : (Color)theme.AccentPrimary;
            }
            if (_cautionLineImg != null) _cautionLineImg.color = theme.WarningColor;
        }
    }
}
