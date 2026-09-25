using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 经典波音 747-400 下部辅助发动机与系统显示屏 (Boeing 747 Lower EICAS / Secondary Engine Display)
    /// 忠实还原波音 747 (RB211-524 三转子高涵道比发动机) 经典 CRT 航电布局：
    /// 1. Row 1: N2 (中压压气机转速 %) 框显 + 青色 N2 居中标签
    /// 2. Row 2: N3 (高压压气机转速 %) 框显 + 外框垂直柱状表 + 顶端红色限值线 + 青色 N3 标签
    /// 3. Row 3: FF (燃油流量 Fuel Flow) 框显 + 青色 FF 标签
    /// 4. Row 4: OIL P (滑油压力 PSI) 左右双轴垂直中心标尺 + 红色最低油压警告线 + 对向三角形指针 + OIL P 标签
    /// 5. Row 5: OIL T (滑油温度 °C) 左右双轴垂直中心标尺 + 黄色超温限值线 + 对向三角形指针 + OIL T 标签
    /// 6. Row 6: OIL Q (滑油余量) 四发数字读数 + OIL Q 标签
    /// 7. Row 7: VIB (机械震动等级) 宽频 BB / 转子 N2 读数 + 真实双 U 型括号标尺与滑动指针 + VIB 标签
    /// 8. 100% 由 TelemetryTokenEngine 与 CustomTemplate 双驱动，零硬编码，统一样式管道。
    /// </summary>
    [FlightWidget("b747_lower_eicas", "eicas_lower", Category = WidgetCategory.Systems, DisplayName = "B747 下部辅助发动机 EICAS", Description = "经典波音 747 四发下部系统 CRT：N2/N3 转速表条、燃油流量 FF、滑油压力/温度双轴游标表与震动监控。", DefaultWidgetId = "custom.b747_lower_eicas", DefaultX = -440f, DefaultY = -120f, IsSingleton = true, ExactIds = new[] { "custom.b747_lower_eicas", "core.b747_lower_eicas" })]
    public class B747LowerEicasWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 基础外框与背景
        private Image _bgImage;
        private Outline _bgOutline;

        // Row 1: N2
        private Text[] _n2ReadoutTexts = new Text[4];
        private Image[] _n2ReadoutBoxes = new Image[4];
        private Outline[] _n2ReadoutOutlines = new Outline[4];
        private Text _n2Label;

        // Row 2: N3
        private Text[] _n3ReadoutTexts = new Text[4];
        private Image[] _n3ReadoutBoxes = new Image[4];
        private Outline[] _n3ReadoutOutlines = new Outline[4];
        private Image[] _n3GaugeTracks = new Image[4];
        private Outline[] _n3GaugeTrackOutlines = new Outline[4];
        private Image[] _n3GaugeFills = new Image[4];
        private Image[] _n3LimitTicks = new Image[4];
        private Image[] _n3CautionTicks = new Image[4];
        private Text _n3Label;

        // Row 3: FF
        private Text[] _ffReadoutTexts = new Text[4];
        private Image[] _ffReadoutBoxes = new Image[4];
        private Outline[] _ffReadoutOutlines = new Outline[4];
        private Text _ffLabel;

        // Row 4: OIL P
        private Text[] _oilPReadoutTexts = new Text[4];
        private RectTransform[] _oilPPointerTransforms = new RectTransform[4];
        private Text[] _oilPPointerTexts = new Text[4];
        private Image[] _oilPTrackLines = new Image[2];
        private Image[] _oilPLimitTicks = new Image[2];
        private Text _oilPLabel;

        // Row 5: OIL T
        private Text[] _oilTReadoutTexts = new Text[4];
        private RectTransform[] _oilTPointerTransforms = new RectTransform[4];
        private Text[] _oilTPointerTexts = new Text[4];
        private Image[] _oilTTrackLines = new Image[2];
        private Image[] _oilTLimitTicks = new Image[2];
        private Text _oilTLabel;

        // Row 6: OIL Q
        private Text[] _oilQReadoutTexts = new Text[4];
        private Text _oilQLabel;

        // Row 7: VIB
        private Text[] _vibPrefixTexts = new Text[4];
        private Text[] _vibReadoutTexts = new Text[4];
        private Image[] _vibLeftRails = new Image[2];
        private Image[] _vibRightRails = new Image[2];
        private Image[] _vibBottomRails = new Image[2];
        private Image[] _vibCautionTicks = new Image[2];
        private RectTransform[] _vibPointerTransforms = new RectTransform[4];
        private Text[] _vibPointerTexts = new Text[4];
        private Text _vibLabel;

        // 通配符通道与模板
        private string _n2Token = "{ENG:N2}";
        private string _n3Token = "{ENG:N3}";
        private string _ffToken = "{ENG:FF}";
        private string _oilPToken = "{ENG:OIL_P}";
        private string _oilTToken = "{ENG:OIL_T}";
        private string _oilQToken = "{ENG:OIL_Q}";
        private string _vibToken = "{ENG:VIB}";

        private string _n2LabelText = "N2";
        private string _n3LabelText = "N3";
        private string _ffLabelText = "FF";
        private string _oilPLabelText = "OIL P";
        private string _oilTLabelText = "OIL T";
        private string _oilQLabelText = "OIL Q";
        private string _vibLabelText = "VIB";

        // 脏检查文本缓存
        private string[] _lastN2Strs = new string[4];
        private string[] _lastN3Strs = new string[4];
        private string[] _lastFfStrs = new string[4];
        private string[] _lastOilPStrs = new string[4];
        private string[] _lastOilTStrs = new string[4];
        private string[] _lastOilQStrs = new string[4];
        private string[] _lastVibStrs = new string[4];

        private double[] _lastN2Vals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };
        private double[] _lastN3Vals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };
        private double[] _lastFfVals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Vector2 cardSize = new Vector2(260f * s, 275f * s);
            RectTransform.sizeDelta = cardSize;

            ParseCustomTemplate(config);

            // 1. 底板与边框 (CRT 质感)
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _bgOutline.effectColor, s);

            // 2. 几何参数
            float[] engXCoords = new float[] { -102f * s, -76f * s, -28f * s, -2f * s };
            float[] pairCenters = new float[] { -89f * s, -15f * s };
            float labelX = -52f * s;
            float boxWidth = 22f * s;
            float boxHeight = 13f * s;

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color valCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color labelCol = style.GetTextColor(TextStyleRole.Cardinal, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);

            // Row 1: N2
            float n2Y = -18f * s;
            for (int i = 0; i < 4; i++)
            {
                CreateReadoutBox(transform, $"N2_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(engXCoords[i], n2Y),
                    "50", boxBgCol, boxBorderCol, valCol, s,
                    out _n2ReadoutBoxes[i], out _n2ReadoutOutlines[i], out _n2ReadoutTexts[i]);
                _lastN2Strs[i] = string.Empty;
            }
            _n2Label = UIFactory.CreateText(transform, "Label_N2", _n2LabelText, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform n2lRt = _n2Label.rectTransform;
            n2lRt.anchorMin = new Vector2(0.5f, 1f);
            n2lRt.anchorMax = new Vector2(0.5f, 1f);
            n2lRt.pivot = new Vector2(0.5f, 0.5f);
            n2lRt.sizeDelta = new Vector2(24f * s, 12f * s);
            n2lRt.anchoredPosition = new Vector2(labelX, n2Y - boxHeight * 0.5f);

            // Row 2: N3
            float n3BoxY = -34f * s;
            float n3GaugeTopY = -48f * s;
            float n3GaugeW = 4.5f * s;
            float n3GaugeH = 22f * s;

            for (int i = 0; i < 4; i++)
            {
                CreateReadoutBox(transform, $"N3_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(engXCoords[i], n3BoxY),
                    "61", boxBgCol, boxBorderCol, valCol, s,
                    out _n3ReadoutBoxes[i], out _n3ReadoutOutlines[i], out _n3ReadoutTexts[i]);

                CreateVerticalN3Gauge(transform, $"N3_Gauge_{i + 1}", new Vector2(n3GaugeW, n3GaugeH), new Vector2(engXCoords[i], n3GaugeTopY),
                    trackCol, meterFillCol, dangerCol, warnCol, s,
                    out _n3GaugeTracks[i], out _n3GaugeTrackOutlines[i], out _n3GaugeFills[i], out _n3LimitTicks[i], out _n3CautionTicks[i]);
                _lastN3Strs[i] = string.Empty;
            }

            _n3Label = UIFactory.CreateText(transform, "Label_N3", _n3LabelText, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform n3lRt = _n3Label.rectTransform;
            n3lRt.anchorMin = new Vector2(0.5f, 1f);
            n3lRt.anchorMax = new Vector2(0.5f, 1f);
            n3lRt.pivot = new Vector2(0.5f, 0.5f);
            n3lRt.sizeDelta = new Vector2(24f * s, 12f * s);
            n3lRt.anchoredPosition = new Vector2(labelX, n3GaugeTopY - n3GaugeH * 0.5f);

            // Row 3: FF
            float ffY = -74f * s;
            for (int i = 0; i < 4; i++)
            {
                CreateReadoutBox(transform, $"FF_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(engXCoords[i], ffY),
                    "06", boxBgCol, boxBorderCol, valCol, s,
                    out _ffReadoutBoxes[i], out _ffReadoutOutlines[i], out _ffReadoutTexts[i]);
                _lastFfStrs[i] = string.Empty;
            }
            _ffLabel = UIFactory.CreateText(transform, "Label_FF", _ffLabelText, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform fflRt = _ffLabel.rectTransform;
            fflRt.anchorMin = new Vector2(0.5f, 1f);
            fflRt.anchorMax = new Vector2(0.5f, 1f);
            fflRt.pivot = new Vector2(0.5f, 0.5f);
            fflRt.sizeDelta = new Vector2(24f * s, 12f * s);
            fflRt.anchoredPosition = new Vector2(labelX, ffY - boxHeight * 0.5f);

            // Row 4: OIL P
            float oilPY = -92f * s;
            BuildDualPointerScale(transform, "OilP", oilPY, pairCenters, engXCoords, labelX, _oilPLabelText,
                trackCol, dangerCol, valCol, labelCol, s,
                out _oilPReadoutTexts, out _oilPPointerTransforms, out _oilPPointerTexts,
                out _oilPTrackLines, out _oilPLimitTicks, out _oilPLabel);

            // Row 5: OIL T
            float oilTY = -128f * s;
            BuildDualPointerScale(transform, "OilT", oilTY, pairCenters, engXCoords, labelX, _oilTLabelText,
                trackCol, warnCol, valCol, labelCol, s,
                out _oilTReadoutTexts, out _oilTPointerTransforms, out _oilTPointerTexts,
                out _oilTTrackLines, out _oilTLimitTicks, out _oilTLabel);

            // Row 6: OIL Q
            float oilQY = -164f * s;
            for (int i = 0; i < 4; i++)
            {
                _oilQReadoutTexts[i] = UIFactory.CreateText(transform, $"OILQ_Val_{i + 1}", "12", Mathf.RoundToInt(9f * s),
                    TextAnchor.MiddleCenter, valCol);
                RectTransform oqrt = _oilQReadoutTexts[i].rectTransform;
                oqrt.anchorMin = new Vector2(0.5f, 1f);
                oqrt.anchorMax = new Vector2(0.5f, 1f);
                oqrt.pivot = new Vector2(0.5f, 0.5f);
                oqrt.sizeDelta = new Vector2(boxWidth, 14f * s);
                oqrt.anchoredPosition = new Vector2(engXCoords[i], oilQY);
                _lastOilQStrs[i] = string.Empty;
            }
            _oilQLabel = UIFactory.CreateText(transform, "Label_OilQ", _oilQLabelText, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform oqlRt = _oilQLabel.rectTransform;
            oqlRt.anchorMin = new Vector2(0.5f, 1f);
            oqlRt.anchorMax = new Vector2(0.5f, 1f);
            oqlRt.pivot = new Vector2(0.5f, 0.5f);
            oqlRt.sizeDelta = new Vector2(30f * s, 12f * s);
            oqlRt.anchoredPosition = new Vector2(labelX, oilQY);

            // Row 7: VIB
            float vibY = -184f * s;
            BuildVibBracketScale(transform, vibY, pairCenters, engXCoords, labelX, _vibLabelText,
                trackCol, warnCol, valCol, labelCol, s,
                out _vibPrefixTexts, out _vibReadoutTexts, out _vibPointerTransforms, out _vibPointerTexts,
                out _vibLeftRails, out _vibRightRails, out _vibBottomRails, out _vibCautionTicks, out _vibLabel);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, t)));
            if (_n2Label != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "n2_readouts", "N2转速组", _n2Label.gameObject));
            if (_n3Label != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "n3_gauges", "N3表柱组", _n3Label.gameObject));
            if (_ffLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "ff_readouts", "燃油流量FF", _ffLabel.gameObject));
            if (_oilPLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "oil_p_meters", "滑油压力OIL P", _oilPLabel.gameObject));
            if (_oilTLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "oil_t_meters", "滑油温度OIL T", _oilTLabel.gameObject));
            if (_oilQLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "oil_q_readouts", "滑油余量OIL Q", _oilQLabel.gameObject));
            if (_vibLabel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "vib_meters", "震动监控VIB", _vibLabel.gameObject));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "N2": _n2Token = v; break;
                    case "N3": _n3Token = v; break;
                    case "FF": _ffToken = v; break;
                    case "OIL_P": _oilPToken = v; break;
                    case "OIL_T": _oilTToken = v; break;
                    case "OIL_Q": _oilQToken = v; break;
                    case "VIB": _vibToken = v; break;
                    case "N2_LABEL": _n2LabelText = v; break;
                    case "N3_LABEL": _n3LabelText = v; break;
                    case "FF_LABEL": _ffLabelText = v; break;
                    case "OIL_P_LABEL": _oilPLabelText = v; break;
                    case "OIL_T_LABEL": _oilTLabelText = v; break;
                    case "OIL_Q_LABEL": _oilQLabelText = v; break;
                    case "VIB_LABEL": _vibLabelText = v; break;
                }
            }
        }

        private static void CreateReadoutBox(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            string defaultText, Color bgColor, Color borderColor, Color textColor, float s,
            out Image boxBg, out Outline boxOutline, out Text readoutText)
        {
            GameObject boxObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, bgColor, borderColor, 1f * s);
            boxBg = boxObj.GetComponent<Image>();
            boxOutline = boxObj.GetComponent<Outline>();

            RectTransform brt = boxObj.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 1f);
            brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = anchoredPos;

            readoutText = UIFactory.CreateText(boxObj.transform, "Value", defaultText, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, textColor);
            RectTransform vrt = readoutText.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            vrt.anchoredPosition = Vector2.zero;
        }

        private static void CreateVerticalN3Gauge(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color trackColor, Color fillColor, Color limitTickColor, Color cautionTickColor, float s,
            out Image trackImg, out Outline trackOutline, out Image fillImg, out Image limitTickImg, out Image cautionTickImg)
        {
            GameObject trackObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, trackColor);
            trackImg = trackObj.GetComponent<Image>();
            trackOutline = trackObj.AddComponent<Outline>();
            trackOutline.effectColor = trackColor;
            trackOutline.effectDistance = new Vector2(0.7f * s, 0.7f * s);

            RectTransform trt = trackObj.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = anchoredPos;

            GameObject fillObj = UIFactory.CreatePanel(trackObj.transform, "Fill", new Vector2(size.x, 0f), Vector2.zero, fillColor);
            fillImg = fillObj.GetComponent<Image>();
            RectTransform frt = fillObj.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.anchoredPosition = Vector2.zero;

            GameObject limitTick = UIFactory.CreatePanel(trackObj.transform, "LimitTick", new Vector2(size.x * 2.2f, 1.8f * s), Vector2.zero, limitTickColor);
            limitTickImg = limitTick.GetComponent<Image>();
            RectTransform ltrt = limitTick.GetComponent<RectTransform>();
            ltrt.anchorMin = new Vector2(0.5f, 1f);
            ltrt.anchorMax = new Vector2(0.5f, 1f);
            ltrt.pivot = new Vector2(0.5f, 0.5f);
            ltrt.anchoredPosition = Vector2.zero;

            GameObject cautionTick = UIFactory.CreatePanel(trackObj.transform, "CautionTick", new Vector2(size.x * 1.8f, 1.2f * s), Vector2.zero, cautionTickColor);
            cautionTickImg = cautionTick.GetComponent<Image>();
            RectTransform ctrt = cautionTick.GetComponent<RectTransform>();
            ctrt.anchorMin = new Vector2(0.5f, 1f);
            ctrt.anchorMax = new Vector2(0.5f, 1f);
            ctrt.pivot = new Vector2(0.5f, 0.5f);
            ctrt.anchoredPosition = new Vector2(0f, -size.y * 0.15f);
        }

        private static void BuildDualPointerScale(Transform parent, string prefix, float topY, float[] pairCenters,
            float[] engXCoords, float labelX, string labelStr,
            Color trackCol, Color tickCol, Color valCol, Color labelCol, float s,
            out Text[] readouts, out RectTransform[] pointerRts, out Text[] pointerTexts,
            out Image[] trackLines, out Image[] limitTicks, out Text label)
        {
            readouts = new Text[4];
            pointerRts = new RectTransform[4];
            pointerTexts = new Text[4];
            trackLines = new Image[2];
            limitTicks = new Image[2];

            float axisHalfH = 9f * s;
            float axisW = 1.2f * s;

            for (int i = 0; i < 4; i++)
            {
                readouts[i] = UIFactory.CreateText(parent, $"{prefix}_Val_{i + 1}", "0", Mathf.RoundToInt(9f * s),
                    TextAnchor.MiddleCenter, valCol);
                RectTransform vrt = readouts[i].rectTransform;
                vrt.anchorMin = new Vector2(0.5f, 1f);
                vrt.anchorMax = new Vector2(0.5f, 1f);
                vrt.pivot = new Vector2(0.5f, 0.5f);
                vrt.sizeDelta = new Vector2(22f * s, 13f * s);
                vrt.anchoredPosition = new Vector2(engXCoords[i], topY - axisHalfH);

                bool isLeftOfPair = (i == 0 || i == 2);
                string arrowStr = isLeftOfPair ? ">" : "<";
                pointerTexts[i] = UIFactory.CreateText(parent, $"{prefix}_Ptr_{i + 1}", arrowStr, Mathf.RoundToInt(8f * s),
                    TextAnchor.MiddleCenter, valCol);
                pointerRts[i] = pointerTexts[i].rectTransform;
                pointerRts[i].anchorMin = new Vector2(0.5f, 1f);
                pointerRts[i].anchorMax = new Vector2(0.5f, 1f);
                pointerRts[i].pivot = new Vector2(0.5f, 0.5f);
                pointerRts[i].sizeDelta = new Vector2(6f * s, 8f * s);

                int pairIdx = i < 2 ? 0 : 1;
                float ptrX = pairCenters[pairIdx] + (isLeftOfPair ? -3.5f * s : 3.5f * s);
                pointerRts[i].anchoredPosition = new Vector2(ptrX, topY - axisHalfH);
            }

            for (int p = 0; p < 2; p++)
            {
                GameObject trk = UIFactory.CreatePanel(parent, $"{prefix}_Trk_{p + 1}", new Vector2(axisW, axisHalfH * 2f),
                    new Vector2(pairCenters[p], topY - axisHalfH), trackCol);
                trackLines[p] = trk.GetComponent<Image>();

                GameObject lmt = UIFactory.CreatePanel(trk.transform, "Limit", new Vector2(4f * s, 1.2f * s),
                    new Vector2(0f, -axisHalfH + 1f * s), tickCol);
                limitTicks[p] = lmt.GetComponent<Image>();
            }

            label = UIFactory.CreateText(parent, $"Label_{prefix}", labelStr, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 1f);
            lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(30f * s, 12f * s);
            lrt.anchoredPosition = new Vector2(labelX, topY - axisHalfH);
        }

        private static void BuildVibBracketScale(Transform parent, float topY, float[] pairCenters,
            float[] engXCoords, float labelX, string labelStr,
            Color trackCol, Color cautionCol, Color valCol, Color labelCol, float s,
            out Text[] prefixTexts, out Text[] readouts, out RectTransform[] pointerRts, out Text[] pointerTexts,
            out Image[] leftRails, out Image[] rightRails, out Image[] bottomRails, out Image[] cautionTicks, out Text label)
        {
            prefixTexts = new Text[4];
            readouts = new Text[4];
            pointerRts = new RectTransform[4];
            pointerTexts = new Text[4];
            leftRails = new Image[2];
            rightRails = new Image[2];
            bottomRails = new Image[2];
            cautionTicks = new Image[2];

            float railHalfH = 5.5f * s;
            float railW = 1f * s;
            float bracketSpan = 14f * s;

            for (int i = 0; i < 4; i++)
            {
                prefixTexts[i] = UIFactory.CreateText(parent, $"Vib_Pre_{i + 1}", "N2", Mathf.RoundToInt(6.5f * s),
                    TextAnchor.MiddleCenter, labelCol);
                RectTransform prt = prefixTexts[i].rectTransform;
                prt.anchorMin = new Vector2(0.5f, 1f);
                prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(12f * s, 8f * s);
                prt.anchoredPosition = new Vector2(engXCoords[i] - 6f * s, topY - railHalfH);

                readouts[i] = UIFactory.CreateText(parent, $"Vib_Val_{i + 1}", "0.4", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleCenter, valCol);
                RectTransform vrt = readouts[i].rectTransform;
                vrt.anchorMin = new Vector2(0.5f, 1f);
                vrt.anchorMax = new Vector2(0.5f, 1f);
                vrt.pivot = new Vector2(0.5f, 0.5f);
                vrt.sizeDelta = new Vector2(16f * s, 12f * s);
                vrt.anchoredPosition = new Vector2(engXCoords[i] + 7f * s, topY - railHalfH);

                bool isLeftOfPair = (i == 0 || i == 2);
                string arrowStr = isLeftOfPair ? ">" : "<";
                pointerTexts[i] = UIFactory.CreateText(parent, $"Vib_Ptr_{i + 1}", arrowStr, Mathf.RoundToInt(7f * s),
                    TextAnchor.MiddleCenter, valCol);
                pointerRts[i] = pointerTexts[i].rectTransform;
                pointerRts[i].anchorMin = new Vector2(0.5f, 1f);
                pointerRts[i].anchorMax = new Vector2(0.5f, 1f);
                pointerRts[i].pivot = new Vector2(0.5f, 0.5f);
                pointerRts[i].sizeDelta = new Vector2(5f * s, 6f * s);

                int pairIdx = i < 2 ? 0 : 1;
                float ptrX = pairCenters[pairIdx] + (isLeftOfPair ? -(bracketSpan * 0.5f) + 1f * s : (bracketSpan * 0.5f) - 1f * s);
                pointerRts[i].anchoredPosition = new Vector2(ptrX, topY - railHalfH);
            }

            for (int p = 0; p < 2; p++)
            {
                GameObject root = new GameObject($"Vib_Bracket_{p + 1}", typeof(RectTransform));
                root.transform.SetParent(parent, false);
                RectTransform rrt = root.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0.5f, 1f);
                rrt.anchorMax = new Vector2(0.5f, 1f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.sizeDelta = new Vector2(bracketSpan, railHalfH * 2f);
                rrt.anchoredPosition = new Vector2(pairCenters[p], topY - railHalfH);

                leftRails[p] = UIFactory.CreatePanel(root.transform, "L_Rail", new Vector2(railW, railHalfH * 2f),
                    new Vector2(-bracketSpan * 0.5f, 0f), trackCol).GetComponent<Image>();
                rightRails[p] = UIFactory.CreatePanel(root.transform, "R_Rail", new Vector2(railW, railHalfH * 2f),
                    new Vector2(bracketSpan * 0.5f, 0f), trackCol).GetComponent<Image>();
                bottomRails[p] = UIFactory.CreatePanel(root.transform, "B_Rail", new Vector2(bracketSpan, railW),
                    new Vector2(0f, -railHalfH), trackCol).GetComponent<Image>();

                cautionTicks[p] = UIFactory.CreatePanel(root.transform, "Caution", new Vector2(bracketSpan * 0.6f, 1.2f * s),
                    new Vector2(0f, railHalfH - 1f * s), cautionCol).GetComponent<Image>();
            }

            label = UIFactory.CreateText(parent, "Label_Vib", labelStr, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 1f);
            lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(24f * s, 12f * s);
            lrt.anchoredPosition = new Vector2(labelX, topY - railHalfH);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);

            for (int i = 0; i < 4; i++)
            {
                if (_n2ReadoutBoxes[i] != null) _n2ReadoutBoxes[i].color = boxBgCol;
                if (_n2ReadoutOutlines[i] != null) _n2ReadoutOutlines[i].effectColor = boxBorderCol;
                if (_n2ReadoutTexts[i] != null) ApplyText(_n2ReadoutTexts[i], TextStyleRole.PrimaryValue, theme);

                if (_n3ReadoutBoxes[i] != null) _n3ReadoutBoxes[i].color = boxBgCol;
                if (_n3ReadoutOutlines[i] != null) _n3ReadoutOutlines[i].effectColor = boxBorderCol;
                if (_n3ReadoutTexts[i] != null) ApplyText(_n3ReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_n3GaugeTracks[i] != null) _n3GaugeTracks[i].color = trackCol;
                if (_n3GaugeTrackOutlines[i] != null) _n3GaugeTrackOutlines[i].effectColor = trackCol;
                if (_n3GaugeFills[i] != null) _n3GaugeFills[i].color = meterFillCol;
                if (_n3LimitTicks[i] != null) _n3LimitTicks[i].color = dangerCol;
                if (_n3CautionTicks[i] != null) _n3CautionTicks[i].color = warnCol;

                if (_ffReadoutBoxes[i] != null) _ffReadoutBoxes[i].color = boxBgCol;
                if (_ffReadoutOutlines[i] != null) _ffReadoutOutlines[i].effectColor = boxBorderCol;
                if (_ffReadoutTexts[i] != null) ApplyText(_ffReadoutTexts[i], TextStyleRole.PrimaryValue, theme);

                if (_oilPReadoutTexts[i] != null) ApplyText(_oilPReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_oilPPointerTexts[i] != null) ApplyText(_oilPPointerTexts[i], TextStyleRole.PrimaryValue, theme);

                if (_oilTReadoutTexts[i] != null) ApplyText(_oilTReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_oilTPointerTexts[i] != null) ApplyText(_oilTPointerTexts[i], TextStyleRole.PrimaryValue, theme);

                if (_oilQReadoutTexts[i] != null) ApplyText(_oilQReadoutTexts[i], TextStyleRole.PrimaryValue, theme);

                if (_vibPrefixTexts[i] != null) ApplyText(_vibPrefixTexts[i], TextStyleRole.Cardinal, theme);
                if (_vibReadoutTexts[i] != null) ApplyText(_vibReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_vibPointerTexts[i] != null) ApplyText(_vibPointerTexts[i], TextStyleRole.PrimaryValue, theme);
            }

            for (int p = 0; p < 2; p++)
            {
                if (_oilPTrackLines[p] != null) _oilPTrackLines[p].color = trackCol;
                if (_oilPLimitTicks[p] != null) _oilPLimitTicks[p].color = dangerCol;
                if (_oilTTrackLines[p] != null) _oilTTrackLines[p].color = trackCol;
                if (_oilTLimitTicks[p] != null) _oilTLimitTicks[p].color = warnCol;

                if (_vibLeftRails[p] != null) _vibLeftRails[p].color = trackCol;
                if (_vibRightRails[p] != null) _vibRightRails[p].color = trackCol;
                if (_vibBottomRails[p] != null) _vibBottomRails[p].color = trackCol;
                if (_vibCautionTicks[p] != null) _vibCautionTicks[p].color = warnCol;
            }

            ApplyText(_n2Label, TextStyleRole.Cardinal, theme);
            ApplyText(_n3Label, TextStyleRole.Cardinal, theme);
            ApplyText(_ffLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_oilPLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_oilTLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_oilQLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_vibLabel, TextStyleRole.Cardinal, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            float s = CurrentDpiScale;
            float n3GaugeMaxH = 22f * s;
            float oilAxisHalfH = 9f * s;
            float vibRailHalfH = 5.5f * s;

            double baseN2 = TelemetryTokenEngine.EvaluateNumeric(_n2Token, telemetry);
            double baseN3 = TelemetryTokenEngine.EvaluateNumeric(_n3Token, telemetry);
            double baseFf = TelemetryTokenEngine.EvaluateNumeric(_ffToken, telemetry);
            double baseOilP = TelemetryTokenEngine.EvaluateNumeric(_oilPToken, telemetry);
            double baseOilT = TelemetryTokenEngine.EvaluateNumeric(_oilTToken, telemetry);
            double baseOilQ = TelemetryTokenEngine.EvaluateNumeric(_oilQToken, telemetry);
            double baseVib = TelemetryTokenEngine.EvaluateNumeric(_vibToken, telemetry);

            if (double.IsNaN(baseN2)) baseN2 = 50.0 + telemetry.Throttle * 46.0;
            if (double.IsNaN(baseN3)) baseN3 = 61.2 + telemetry.Throttle * 39.0;
            if (double.IsNaN(baseFf)) baseFf = 0.6 + telemetry.Throttle * 4.8;
            if (double.IsNaN(baseOilP)) baseOilP = 80.5 + telemetry.Throttle * 5.5;
            if (double.IsNaN(baseOilT)) baseOilT = 46.0 + telemetry.Throttle * 46.0;
            if (double.IsNaN(baseOilQ)) baseOilQ = 12.0;
            if (double.IsNaN(baseVib)) baseVib = 0.4 + telemetry.Throttle * 0.4;

            float[] variances = new float[] { -0.1f, 0.2f, -0.05f, 0.1f };

            for (int i = 0; i < 4; i++)
            {
                double n2Val = baseN2 + variances[i] * 0.8;
                double n3Val = baseN3 + variances[i] * 0.5;
                double ffVal = baseFf + variances[i] * 0.1;
                double oilPVal = baseOilP;
                double oilTVal = baseOilT;
                double oilQVal = baseOilQ;
                double vibVal = baseVib;

                // 1. N2 读数框
                if (double.IsNaN(_lastN2Vals[i]) || Math.Abs(n2Val - _lastN2Vals[i]) > 0.05)
                {
                    _lastN2Vals[i] = n2Val;
                    string n2Str = Mathf.RoundToInt((float)n2Val).ToString(CultureInfo.InvariantCulture);
                    if (n2Str != _lastN2Strs[i])
                    {
                        _lastN2Strs[i] = n2Str;
                        if (_n2ReadoutTexts[i] != null) _n2ReadoutTexts[i].text = n2Str;
                    }
                }

                // 2. N3 读数框与垂直柱
                if (double.IsNaN(_lastN3Vals[i]) || Math.Abs(n3Val - _lastN3Vals[i]) > 0.05)
                {
                    _lastN3Vals[i] = n3Val;
                    string n3Str = n3Val >= 100.0 ? Mathf.RoundToInt((float)n3Val).ToString(CultureInfo.InvariantCulture) :
                        (n3Val >= 50.0 ? Mathf.RoundToInt((float)n3Val * 10f).ToString(CultureInfo.InvariantCulture) : Mathf.RoundToInt((float)n3Val).ToString(CultureInfo.InvariantCulture));
                    if (n3Str != _lastN3Strs[i])
                    {
                        _lastN3Strs[i] = n3Str;
                        if (_n3ReadoutTexts[i] != null) _n3ReadoutTexts[i].text = n3Str;
                    }

                    float n3Frac = Mathf.Clamp01((float)(n3Val / 105.0));
                    if (_n3GaugeFills[i] != null)
                        _n3GaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, n3GaugeMaxH * n3Frac);
                }

                // 3. FF 燃油流量框
                if (double.IsNaN(_lastFfVals[i]) || Math.Abs(ffVal - _lastFfVals[i]) > 0.02)
                {
                    _lastFfVals[i] = ffVal;
                    string ffStr = ffVal < 1.0 ? $"0{Mathf.RoundToInt((float)ffVal * 10f)}" : Mathf.RoundToInt((float)ffVal * 10f).ToString(CultureInfo.InvariantCulture);
                    if (ffStr != _lastFfStrs[i])
                    {
                        _lastFfStrs[i] = ffStr;
                        if (_ffReadoutTexts[i] != null) _ffReadoutTexts[i].text = ffStr;
                    }
                }

                // 4. OIL P 读数与指针位移
                string oilPStr = Mathf.RoundToInt((float)oilPVal).ToString(CultureInfo.InvariantCulture);
                if (oilPStr != _lastOilPStrs[i])
                {
                    _lastOilPStrs[i] = oilPStr;
                    if (_oilPReadoutTexts[i] != null) _oilPReadoutTexts[i].text = oilPStr;
                }
                float oilPFrac = Mathf.Clamp01((float)(oilPVal / 100.0));
                if (_oilPPointerTransforms[i] != null)
                {
                    float ptrY = -92f * s - oilAxisHalfH + ((oilPFrac - 0.5f) * oilAxisHalfH * 1.6f);
                    _oilPPointerTransforms[i].anchoredPosition = new Vector2(_oilPPointerTransforms[i].anchoredPosition.x, ptrY);
                }

                // 5. OIL T 读数与指针位移
                string oilTStr = Mathf.RoundToInt((float)oilTVal).ToString(CultureInfo.InvariantCulture);
                if (oilTStr != _lastOilTStrs[i])
                {
                    _lastOilTStrs[i] = oilTStr;
                    if (_oilTReadoutTexts[i] != null) _oilTReadoutTexts[i].text = oilTStr;
                }
                float oilTFrac = Mathf.Clamp01((float)(oilTVal / 140.0));
                if (_oilTPointerTransforms[i] != null)
                {
                    float ptrY = -128f * s - oilAxisHalfH + ((oilTFrac - 0.5f) * oilAxisHalfH * 1.6f);
                    _oilTPointerTransforms[i].anchoredPosition = new Vector2(_oilTPointerTransforms[i].anchoredPosition.x, ptrY);
                }

                // 6. OIL Q 读数
                string oilQStr = Mathf.RoundToInt((float)oilQVal).ToString(CultureInfo.InvariantCulture);
                if (oilQStr != _lastOilQStrs[i])
                {
                    _lastOilQStrs[i] = oilQStr;
                    if (_oilQReadoutTexts[i] != null) _oilQReadoutTexts[i].text = oilQStr;
                }

                // 7. VIB 读数与滑块位移
                string vibStr = vibVal.ToString("0.0", CultureInfo.InvariantCulture);
                if (vibStr != _lastVibStrs[i])
                {
                    _lastVibStrs[i] = vibStr;
                    if (_vibReadoutTexts[i] != null) _vibReadoutTexts[i].text = vibStr;
                }
                float vibFrac = Mathf.Clamp01((float)(vibVal / 2.0));
                if (_vibPointerTransforms[i] != null)
                {
                    float ptrY = -184f * s - vibRailHalfH + ((vibFrac - 0.5f) * vibRailHalfH * 1.6f);
                    _vibPointerTransforms[i].anchoredPosition = new Vector2(_vibPointerTransforms[i].anchoredPosition.x, ptrY);
                }
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
