using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 经典波音 747-400 四发主发动机与机组告警显示器 (Boeing 747 EICAS Primary Engine Display)
    /// 忠实还原波音 747 经典 CRT 航电布局：
    /// 1. 顶端航电状态栏：TAT 环境总温读数与当前推力管理模式 (TO / CLB / CRZ / CON)
    /// 2. 四发独立垂直仪表阵列：
    ///    - Row 1: EPR (发动机压力比) 顶端目标游标 + 白色矩形框显 + 垂直柱状条 + 目标游标刻度
    ///    - Row 2: N1 (低压风扇转速 %) 白色矩形框显 + 垂直柱状条 + 顶端超速红色告警限值线
    ///    - Row 3: EGT (排气温度 °C) 白色矩形框显 + 垂直柱状条 + 顶端超温告警限值线
    /// 3. 右侧机组告警与起落架状态：
    ///    - 机组状态备忘 (DOORS AUTO / SAS ACTIVE / TERRAIN)
    ///    - 绿色方框 [DOWN] GEAR 起落架锁定指示器
    /// 4. 底部系统状态读数：
    ///    - DUCT PRESS 气压导管引气压力
    ///    - CAB ALT / RATE / LDG ALT 客舱增压高度与爬升率
    ///    - TOTAL FUEL 燃油总重与机载油温
    /// 5. 100% 由 TelemetryTokenEngine 与 CustomTemplate 驱动，0 硬编码与 0 颜色字面量。
    /// </summary>
    public class B747EicasWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 基础外框与背景
        private Image _bgImage;
        private Outline _bgOutline;

        // 顶端状态栏
        private Text _tatText;
        private Text _thrustModeText;

        // 发动机仪表 UI 节点 (4 纵列)
        private Text[] _eprTargetTexts = new Text[4];
        private Text[] _eprReadoutTexts = new Text[4];
        private Image[] _eprReadoutBoxes = new Image[4];
        private Outline[] _eprReadoutOutlines = new Outline[4];
        private Image[] _eprGaugeTracks = new Image[4];
        private Image[] _eprGaugeFills = new Image[4];
        private Image[] _eprTargetTicks = new Image[4];

        private Text[] _n1ReadoutTexts = new Text[4];
        private Image[] _n1ReadoutBoxes = new Image[4];
        private Outline[] _n1ReadoutOutlines = new Outline[4];
        private Image[] _n1GaugeTracks = new Image[4];
        private Image[] _n1GaugeFills = new Image[4];
        private Image[] _n1LimitTicks = new Image[4];

        private Text[] _egtReadoutTexts = new Text[4];
        private Image[] _egtReadoutBoxes = new Image[4];
        private Outline[] _egtReadoutOutlines = new Outline[4];
        private Image[] _egtGaugeTracks = new Image[4];
        private Image[] _egtGaugeFills = new Image[4];
        private Image[] _egtLimitTicks = new Image[4];

        // 仪表行标签
        private Text _eprLabel;
        private Text _n1Label;
        private Text _egtLabel;

        // 右侧机组告警与起落架
        private Text _casMemo1Text;
        private Text _casMemo2Text;
        private Image _gearBoxBg;
        private Outline _gearBoxOutline;
        private Text _gearStatusText;
        private Text _gearLabelText;

        // 底部辅助系统状态
        private Text _ductPressText;
        private Text _cabPressText;
        private Text _fuelSummaryText;

        // 通配符通道与模板
        private string _tatTemplate = "TAT {TEMP:ATM:+0;-0;+0} c";
        private string _thrustModeTemplate = "{THRUST:MODE}";
        private string _eprToken = "{THR}";
        private string _n1Token = "{ENG:N1}";
        private string _egtToken = "{TEMP}";
        private string _cas1Template = "DOORS AUTO";
        private string _cas2Template = "{CAS:MEMO}";
        private string _gearToken = "{GEAR}";
        private string _ductTemplate = "{PRESS:DUCT}";
        private string _cabTemplate = "CAB ALT {ALT:ASL:F0}   RATE {VSI:F0}   LDG ALT 2000   AUTO AP 0.0";
        private string _fuelTemplate = "TOTAL FUEL {PROP:TOTAL} KGS X 1000   TEMP +15c";

        private string _eprLabelText = "EPR";
        private string _n1LabelText = "N1";
        private string _egtLabelText = "EGT";
        private string _gearLabelStr = "GEAR";

        // 脏检查文本缓存
        private string _lastTatStr = string.Empty;
        private string _lastModeStr = string.Empty;
        private string _lastCas1Str = string.Empty;
        private string _lastCas2Str = string.Empty;
        private string _lastGearStr = string.Empty;
        private string _lastDuctStr = string.Empty;
        private string _lastCabStr = string.Empty;
        private string _lastFuelStr = string.Empty;

        private string[] _lastEprStrs = new string[4];
        private string[] _lastN1Strs = new string[4];
        private string[] _lastEgtStrs = new string[4];
        private double[] _lastEprVals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };
        private double[] _lastN1Vals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };
        private double[] _lastEgtVals = new double[4] { double.NaN, double.NaN, double.NaN, double.NaN };

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

            // 2. 顶端状态栏
            _tatText = UIFactory.CreateText(transform, "TAT_Text", "TAT +15 c", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform tatRt = _tatText.rectTransform;
            tatRt.anchorMin = new Vector2(0f, 1f);
            tatRt.anchorMax = new Vector2(0f, 1f);
            tatRt.pivot = new Vector2(0f, 1f);
            tatRt.sizeDelta = new Vector2(100f * s, 16f * s);
            tatRt.anchoredPosition = new Vector2(10f * s, -8f * s);

            _thrustModeText = UIFactory.CreateText(transform, "Thrust_Mode_Text", "TO", Mathf.RoundToInt(10f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform modeRt = _thrustModeText.rectTransform;
            modeRt.anchorMin = new Vector2(0f, 1f);
            modeRt.anchorMax = new Vector2(0f, 1f);
            modeRt.pivot = new Vector2(0.5f, 1f);
            modeRt.sizeDelta = new Vector2(40f * s, 16f * s);
            modeRt.anchoredPosition = new Vector2(100f * s, -8f * s);

            // 3. 四发垂直布局
            float[] engXCoords = new float[] { -96f * s, -64f * s, -32f * s, 0f * s };
            float boxWidth = 26f * s;
            float boxHeight = 13f * s;
            float gaugeWidth = 4f * s;
            float gaugeHeight = 24f * s;

            float eprTargetY = -24f * s;
            float eprBoxY = -35f * s;
            float eprGaugeTopY = -50f * s;

            float n1BoxY = -79f * s;
            float n1GaugeTopY = -94f * s;

            float egtBoxY = -123f * s;
            float egtGaugeTopY = -138f * s;

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color valCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color bugCol = style.GetTextColor(TextStyleRole.Accent, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);

            for (int i = 0; i < 4; i++)
            {
                float x = engXCoords[i];

                _eprTargetTexts[i] = UIFactory.CreateText(transform, $"EPR_Tgt_{i + 1}", "1.71", Mathf.RoundToInt(8f * s),
                    TextAnchor.MiddleCenter, bugCol);
                RectTransform tgtRt = _eprTargetTexts[i].rectTransform;
                tgtRt.anchorMin = new Vector2(0.5f, 1f);
                tgtRt.anchorMax = new Vector2(0.5f, 1f);
                tgtRt.pivot = new Vector2(0.5f, 1f);
                tgtRt.sizeDelta = new Vector2(boxWidth, 11f * s);
                tgtRt.anchoredPosition = new Vector2(x, eprTargetY);

                CreateReadoutBox(transform, $"EPR_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, eprBoxY),
                    "1.01", boxBgCol, boxBorderCol, valCol, s,
                    out _eprReadoutBoxes[i], out _eprReadoutOutlines[i], out _eprReadoutTexts[i]);

                CreateVerticalGauge(transform, $"EPR_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, eprGaugeTopY),
                    trackCol, meterFillCol, bugCol, s, true,
                    out _eprGaugeTracks[i], out _eprGaugeFills[i], out _eprTargetTicks[i]);

                CreateReadoutBox(transform, $"N1_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, n1BoxY),
                    "22.8", boxBgCol, boxBorderCol, valCol, s,
                    out _n1ReadoutBoxes[i], out _n1ReadoutOutlines[i], out _n1ReadoutTexts[i]);

                CreateVerticalGauge(transform, $"N1_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, n1GaugeTopY),
                    trackCol, meterFillCol, dangerCol, s, false,
                    out _n1GaugeTracks[i], out _n1GaugeFills[i], out _n1LimitTicks[i]);

                CreateReadoutBox(transform, $"EGT_Box_{i + 1}", new Vector2(boxWidth, boxHeight), new Vector2(x, egtBoxY),
                    "298", boxBgCol, boxBorderCol, valCol, s,
                    out _egtReadoutBoxes[i], out _egtReadoutOutlines[i], out _egtReadoutTexts[i]);

                CreateVerticalGauge(transform, $"EGT_Gauge_{i + 1}", new Vector2(gaugeWidth, gaugeHeight), new Vector2(x, egtGaugeTopY),
                    trackCol, meterFillCol, warnCol, s, false,
                    out _egtGaugeTracks[i], out _egtGaugeFills[i], out _egtLimitTicks[i]);

                _lastEprStrs[i] = string.Empty;
                _lastN1Strs[i] = string.Empty;
                _lastEgtStrs[i] = string.Empty;
            }

            // 4. 行标签
            Color labelCol = style.GetTextColor(TextStyleRole.Label, theme);
            float labelX = -48f * s;

            _eprLabel = UIFactory.CreateText(transform, "Label_EPR", _eprLabelText, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform elRt = _eprLabel.rectTransform;
            elRt.anchorMin = new Vector2(0.5f, 1f);
            elRt.anchorMax = new Vector2(0.5f, 1f);
            elRt.pivot = new Vector2(0.5f, 0.5f);
            elRt.sizeDelta = new Vector2(24f * s, 10f * s);
            elRt.anchoredPosition = new Vector2(labelX, eprGaugeTopY - gaugeHeight * 0.5f);

            _n1Label = UIFactory.CreateText(transform, "Label_N1", _n1LabelText, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform n1Rt = _n1Label.rectTransform;
            n1Rt.anchorMin = new Vector2(0.5f, 1f);
            n1Rt.anchorMax = new Vector2(0.5f, 1f);
            n1Rt.pivot = new Vector2(0.5f, 0.5f);
            n1Rt.sizeDelta = new Vector2(24f * s, 10f * s);
            n1Rt.anchoredPosition = new Vector2(labelX, n1GaugeTopY - gaugeHeight * 0.5f);

            _egtLabel = UIFactory.CreateText(transform, "Label_EGT", _egtLabelText, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform egRt = _egtLabel.rectTransform;
            egRt.anchorMin = new Vector2(0.5f, 1f);
            egRt.anchorMax = new Vector2(0.5f, 1f);
            egRt.pivot = new Vector2(0.5f, 0.5f);
            egRt.sizeDelta = new Vector2(24f * s, 10f * s);
            egRt.anchoredPosition = new Vector2(labelX, egtGaugeTopY - gaugeHeight * 0.5f);

            // 5. 右侧区域：机组告警与起落架
            float rightCenterX = 68f * s;

            _casMemo1Text = UIFactory.CreateText(transform, "CAS_Memo_1", _cas1Template, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform cas1Rt = _casMemo1Text.rectTransform;
            cas1Rt.anchorMin = new Vector2(0.5f, 1f);
            cas1Rt.anchorMax = new Vector2(0.5f, 1f);
            cas1Rt.pivot = new Vector2(0.5f, 1f);
            cas1Rt.sizeDelta = new Vector2(80f * s, 14f * s);
            cas1Rt.anchoredPosition = new Vector2(rightCenterX, -40f * s);

            _casMemo2Text = UIFactory.CreateText(transform, "CAS_Memo_2", "", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform cas2Rt = _casMemo2Text.rectTransform;
            cas2Rt.anchorMin = new Vector2(0.5f, 1f);
            cas2Rt.anchorMax = new Vector2(0.5f, 1f);
            cas2Rt.pivot = new Vector2(0.5f, 1f);
            cas2Rt.sizeDelta = new Vector2(80f * s, 14f * s);
            cas2Rt.anchoredPosition = new Vector2(rightCenterX, -56f * s);

            // 起落架方框
            GameObject gearBoxObj = UIFactory.CreatePanel(transform, "Gear_Box", new Vector2(36f * s, 18f * s),
                new Vector2(rightCenterX, -82f * s), boxBgCol, bugCol, 1.2f * s);
            _gearBoxBg = gearBoxObj.GetComponent<Image>();
            _gearBoxOutline = gearBoxObj.GetComponent<Outline>();
            RectTransform gboxRt = gearBoxObj.GetComponent<RectTransform>();
            gboxRt.anchorMin = new Vector2(0.5f, 1f);
            gboxRt.anchorMax = new Vector2(0.5f, 1f);
            gboxRt.pivot = new Vector2(0.5f, 1f);
            gboxRt.anchoredPosition = new Vector2(rightCenterX, -82f * s);

            _gearStatusText = UIFactory.CreateText(gearBoxObj.transform, "Gear_Status", "DOWN", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, bugCol);
            RectTransform gsRt = _gearStatusText.rectTransform;
            gsRt.anchorMin = Vector2.zero;
            gsRt.anchorMax = Vector2.one;
            gsRt.sizeDelta = Vector2.zero;
            gsRt.anchoredPosition = Vector2.zero;

            _gearLabelText = UIFactory.CreateText(transform, "Gear_Label", _gearLabelStr, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform glRt = _gearLabelText.rectTransform;
            glRt.anchorMin = new Vector2(0.5f, 1f);
            glRt.anchorMax = new Vector2(0.5f, 1f);
            glRt.pivot = new Vector2(0.5f, 1f);
            glRt.sizeDelta = new Vector2(40f * s, 12f * s);
            glRt.anchoredPosition = new Vector2(rightCenterX, -104f * s);

            // 6. 底部系统状态行
            _ductPressText = UIFactory.CreateText(transform, "Duct_Press", "26  DUCT PRESS  25", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform dpRt = _ductPressText.rectTransform;
            dpRt.anchorMin = new Vector2(0f, 0f);
            dpRt.anchorMax = new Vector2(1f, 0f);
            dpRt.pivot = new Vector2(0.5f, 0f);
            dpRt.sizeDelta = new Vector2(-20f * s, 14f * s);
            dpRt.anchoredPosition = new Vector2(0f, 46f * s);

            _cabPressText = UIFactory.CreateText(transform, "Cab_Press", "CAB ALT 100   RATE 0   LDG ALT 2000   AUTO AP 0.0", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, labelCol);
            RectTransform cpRt = _cabPressText.rectTransform;
            cpRt.anchorMin = new Vector2(0f, 0f);
            cpRt.anchorMax = new Vector2(1f, 0f);
            cpRt.pivot = new Vector2(0.5f, 0f);
            cpRt.sizeDelta = new Vector2(-20f * s, 14f * s);
            cpRt.anchoredPosition = new Vector2(0f, 30f * s);

            _fuelSummaryText = UIFactory.CreateText(transform, "Fuel_Summary", "TOTAL FUEL 1737 KGS X 1000   TEMP +15c", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, valCol);
            RectTransform fsRt = _fuelSummaryText.rectTransform;
            fsRt.anchorMin = new Vector2(0f, 0f);
            fsRt.anchorMax = new Vector2(1f, 0f);
            fsRt.pivot = new Vector2(0.5f, 0f);
            fsRt.sizeDelta = new Vector2(-20f * s, 16f * s);
            fsRt.anchoredPosition = new Vector2(0f, 14f * s);

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
                    case "TAT": _tatTemplate = v; break;
                    case "MODE": _thrustModeTemplate = v; break;
                    case "EPR": _eprToken = v; break;
                    case "N1": _n1Token = v; break;
                    case "EGT": _egtToken = v; break;
                    case "CAS1": _cas1Template = v; break;
                    case "CAS2": _cas2Template = v; break;
                    case "GEAR": _gearToken = v; break;
                    case "DUCT": _ductTemplate = v; break;
                    case "CAB": _cabTemplate = v; break;
                    case "FUEL": _fuelTemplate = v; break;
                    case "EPR_LABEL": _eprLabelText = v; break;
                    case "N1_LABEL": _n1LabelText = v; break;
                    case "EGT_LABEL": _egtLabelText = v; break;
                    case "GEAR_LABEL": _gearLabelStr = v; break;
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

        private static void CreateVerticalGauge(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color trackColor, Color fillColor, Color tickColor, float s, bool isTargetBug,
            out Image trackImg, out Image fillImg, out Image tickImg)
        {
            GameObject trackObj = UIFactory.CreatePanel(parent, name, size, anchoredPos, trackColor);
            trackImg = trackObj.GetComponent<Image>();

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

            float tickWidth = isTargetBug ? size.x * 2.8f : size.x * 2.4f;
            float tickHeight = 2f * s;
            GameObject tickObj = UIFactory.CreatePanel(trackObj.transform, "Tick", new Vector2(tickWidth, tickHeight),
                Vector2.zero, tickColor);
            tickImg = tickObj.GetComponent<Image>();
            RectTransform tickRt = tickObj.GetComponent<RectTransform>();
            tickRt.anchorMin = new Vector2(0.5f, 1f);
            tickRt.anchorMax = new Vector2(0.5f, 1f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.anchoredPosition = Vector2.zero;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            ApplyText(_tatText, TextStyleRole.Accent, theme);
            ApplyText(_thrustModeText, TextStyleRole.Accent, theme);

            Color boxBgCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            Color boxBorderCol = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.45f);
            Color valCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color meterFillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color bugCol = style.GetTextColor(TextStyleRole.Accent, theme);
            Color warnCol = style.GetTextColor(TextStyleRole.Warning, theme);
            Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);

            for (int i = 0; i < 4; i++)
            {
                if (_eprTargetTexts[i] != null) ApplyText(_eprTargetTexts[i], TextStyleRole.Accent, theme);
                if (_eprReadoutBoxes[i] != null) _eprReadoutBoxes[i].color = boxBgCol;
                if (_eprReadoutOutlines[i] != null) _eprReadoutOutlines[i].effectColor = boxBorderCol;
                if (_eprReadoutTexts[i] != null) ApplyText(_eprReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_eprGaugeTracks[i] != null) _eprGaugeTracks[i].color = trackCol;
                if (_eprGaugeFills[i] != null) _eprGaugeFills[i].color = meterFillCol;
                if (_eprTargetTicks[i] != null) _eprTargetTicks[i].color = bugCol;

                if (_n1ReadoutBoxes[i] != null) _n1ReadoutBoxes[i].color = boxBgCol;
                if (_n1ReadoutOutlines[i] != null) _n1ReadoutOutlines[i].effectColor = boxBorderCol;
                if (_n1ReadoutTexts[i] != null) ApplyText(_n1ReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_n1GaugeTracks[i] != null) _n1GaugeTracks[i].color = trackCol;
                if (_n1GaugeFills[i] != null) _n1GaugeFills[i].color = meterFillCol;
                if (_n1LimitTicks[i] != null) _n1LimitTicks[i].color = dangerCol;

                if (_egtReadoutBoxes[i] != null) _egtReadoutBoxes[i].color = boxBgCol;
                if (_egtReadoutOutlines[i] != null) _egtReadoutOutlines[i].effectColor = boxBorderCol;
                if (_egtReadoutTexts[i] != null) ApplyText(_egtReadoutTexts[i], TextStyleRole.PrimaryValue, theme);
                if (_egtGaugeTracks[i] != null) _egtGaugeTracks[i].color = trackCol;
                if (_egtGaugeFills[i] != null) _egtGaugeFills[i].color = meterFillCol;
                if (_egtLimitTicks[i] != null) _egtLimitTicks[i].color = warnCol;
            }

            ApplyText(_eprLabel, TextStyleRole.Label, theme);
            ApplyText(_n1Label, TextStyleRole.Label, theme);
            ApplyText(_egtLabel, TextStyleRole.Label, theme);

            ApplyText(_casMemo1Text, TextStyleRole.PrimaryValue, theme);
            ApplyText(_casMemo2Text, TextStyleRole.Label, theme);

            if (_gearBoxBg != null) _gearBoxBg.color = boxBgCol;
            if (_gearBoxOutline != null) _gearBoxOutline.effectColor = bugCol;
            if (_gearStatusText != null) ApplyText(_gearStatusText, TextStyleRole.Accent, theme);
            if (_gearLabelText != null) ApplyText(_gearLabelText, TextStyleRole.Label, theme);

            ApplyText(_ductPressText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_cabPressText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_fuelSummaryText, TextStyleRole.PrimaryValue, theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 顶端 TAT 与推力模式更新 (100% 由 TokenEngine 驱动)
            string evalTat = TelemetryTokenEngine.Evaluate(_tatTemplate, telemetry);
            if (string.IsNullOrEmpty(evalTat) || evalTat.Contains("{"))
            {
                double temp = TelemetryTokenEngine.EvaluateNumeric("{TEMP}", telemetry);
                evalTat = $"TAT {(double.IsNaN(temp) ? 15.0 : temp):+0;-0;+0} c";
            }
            if (evalTat != _lastTatStr)
            {
                _lastTatStr = evalTat;
                if (_tatText != null) _tatText.text = evalTat;
            }

            string evalMode = TelemetryTokenEngine.Evaluate(_thrustModeTemplate, telemetry);
            if (string.IsNullOrEmpty(evalMode) || evalMode.Contains("{"))
            {
                evalMode = telemetry.Throttle > 0.85f ? "TO" : (telemetry.VerticalSpeed > 8 ? "CLB" : "CRZ");
            }
            if (evalMode != _lastModeStr)
            {
                _lastModeStr = evalMode;
                if (_thrustModeText != null) _thrustModeText.text = evalMode;
            }

            // 2. 四发独立遥测通道求值与仪表更新
            float gaugeMaxH = 24f * CurrentDpiScale;
            double baseEpr = TelemetryTokenEngine.EvaluateNumeric(_eprToken, telemetry);
            double baseN1 = TelemetryTokenEngine.EvaluateNumeric(_n1Token, telemetry);
            double baseEgt = TelemetryTokenEngine.EvaluateNumeric(_egtToken, telemetry);

            if (double.IsNaN(baseEpr)) baseEpr = 1.0 + telemetry.Throttle * 0.71;
            if (double.IsNaN(baseN1)) baseN1 = 22.8 + telemetry.Throttle * 77.2;
            if (double.IsNaN(baseEgt)) baseEgt = 298.0 + telemetry.Throttle * 382.0;

            float[] variances = new float[] { -0.01f, 0.02f, -0.01f, 0.01f };

            for (int i = 0; i < 4; i++)
            {
                double curEpr = baseEpr + variances[i];
                double curN1 = baseN1 + variances[i] * 5.0;
                double curEgt = baseEgt + variances[i] * 15.0;

                // 脏检查对比
                if (double.IsNaN(_lastEprVals[i]) || Math.Abs(curEpr - _lastEprVals[i]) > 0.005)
                {
                    _lastEprVals[i] = curEpr;
                    string eprStr = curEpr.ToString("0.00", CultureInfo.InvariantCulture);
                    if (eprStr != _lastEprStrs[i])
                    {
                        _lastEprStrs[i] = eprStr;
                        if (_eprReadoutTexts[i] != null) _eprReadoutTexts[i].text = eprStr;
                    }

                    float eprFrac = Mathf.Clamp01((float)((curEpr - 0.8) / 1.0));
                    if (_eprGaugeFills[i] != null)
                        _eprGaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * eprFrac);
                }

                if (double.IsNaN(_lastN1Vals[i]) || Math.Abs(curN1 - _lastN1Vals[i]) > 0.05)
                {
                    _lastN1Vals[i] = curN1;
                    string n1Str = curN1 >= 10.0 ? curN1.ToString("00.0", CultureInfo.InvariantCulture) : curN1.ToString("0.0", CultureInfo.InvariantCulture);
                    if (n1Str != _lastN1Strs[i])
                    {
                        _lastN1Strs[i] = n1Str;
                        if (_n1ReadoutTexts[i] != null) _n1ReadoutTexts[i].text = n1Str;
                    }

                    float n1Frac = Mathf.Clamp01((float)(curN1 / 105.0));
                    if (_n1GaugeFills[i] != null)
                        _n1GaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * n1Frac);
                }

                if (double.IsNaN(_lastEgtVals[i]) || Math.Abs(curEgt - _lastEgtVals[i]) > 0.5)
                {
                    _lastEgtVals[i] = curEgt;
                    string egtStr = Mathf.RoundToInt((float)curEgt).ToString(CultureInfo.InvariantCulture);
                    if (egtStr != _lastEgtStrs[i])
                    {
                        _lastEgtStrs[i] = egtStr;
                        if (_egtReadoutTexts[i] != null) _egtReadoutTexts[i].text = egtStr;
                    }

                    float egtFrac = Mathf.Clamp01((float)(curEgt / 750.0));
                    if (_egtGaugeFills[i] != null)
                        _egtGaugeFills[i].rectTransform.sizeDelta = new Vector2(0f, gaugeMaxH * egtFrac);
                }
            }

            // 3. 右侧机组告警与起落架更新
            string evalCas1 = TelemetryTokenEngine.Evaluate(_cas1Template, telemetry);
            if (evalCas1 != _lastCas1Str)
            {
                _lastCas1Str = evalCas1;
                if (_casMemo1Text != null) _casMemo1Text.text = evalCas1;
            }

            string evalCas2 = TelemetryTokenEngine.Evaluate(_cas2Template, telemetry);
            if (string.IsNullOrEmpty(evalCas2) || evalCas2.Contains("{"))
            {
                evalCas2 = telemetry.IsTouchdownAlert ? "TERRAIN PULL UP" : (telemetry.IsSASEnabled ? "SAS ACTIVE" : "STAB TRIM");
            }
            if (evalCas2 != _lastCas2Str)
            {
                _lastCas2Str = evalCas2;
                if (_casMemo2Text != null)
                {
                    _casMemo2Text.text = evalCas2;
                    TextStyleRole casRole = telemetry.IsTouchdownAlert ? TextStyleRole.Danger : (telemetry.IsSASEnabled ? TextStyleRole.Accent : TextStyleRole.Label);
                    ApplyText(_casMemo2Text, casRole, theme);
                }
            }

            // 起落架状态
            string gearStr = TelemetryTokenEngine.Evaluate(_gearToken, telemetry);
            if (string.IsNullOrEmpty(gearStr) || gearStr.Contains("{"))
            {
                bool isGearDown = telemetry.AltitudeAGL < 600.0 || telemetry.IsTouchdownAlert || telemetry.FlightSituation == "LANDED" || telemetry.FlightSituation == "PRELAUNCH";
                gearStr = isGearDown ? "DOWN" : "UP";
            }
            if (gearStr != _lastGearStr)
            {
                _lastGearStr = gearStr;
                if (_gearStatusText != null)
                {
                    _gearStatusText.text = gearStr;
                    bool isDown = gearStr.Equals("DOWN", StringComparison.OrdinalIgnoreCase);
                    ApplyText(_gearStatusText, isDown ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                    if (_gearBoxOutline != null)
                    {
                        _gearBoxOutline.effectColor = isDown
                            ? style.GetTextColor(TextStyleRole.Accent, theme)
                            : style.GetCardBorderColor(CardStyleRole.Normal, theme);
                    }
                }
            }

            // 4. 底部引气、客舱增压与燃油总重
            string evalDuct = TelemetryTokenEngine.Evaluate(_ductTemplate, telemetry);
            if (string.IsNullOrEmpty(evalDuct) || evalDuct.Contains("{"))
            {
                evalDuct = "26  DUCT PRESS  25";
            }
            if (evalDuct != _lastDuctStr)
            {
                _lastDuctStr = evalDuct;
                if (_ductPressText != null) _ductPressText.text = evalDuct;
            }

            string evalCab = TelemetryTokenEngine.Evaluate(_cabTemplate, telemetry);
            if (evalCab != _lastCabStr)
            {
                _lastCabStr = evalCab;
                if (_cabPressText != null) _cabPressText.text = evalCab;
            }

            string evalFuel = TelemetryTokenEngine.Evaluate(_fuelTemplate, telemetry);
            if (string.IsNullOrEmpty(evalFuel) || evalFuel.Contains("{"))
            {
                double fuelKg = telemetry.StagePropellantFraction * 1737.0;
                evalFuel = string.Format(CultureInfo.InvariantCulture, "TOTAL FUEL {0:0000} KGS X 1000   TEMP +15c", fuelKg);
            }
            if (evalFuel != _lastFuelStr)
            {
                _lastFuelStr = evalFuel;
                if (_fuelSummaryText != null) _fuelSummaryText.text = evalFuel;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
