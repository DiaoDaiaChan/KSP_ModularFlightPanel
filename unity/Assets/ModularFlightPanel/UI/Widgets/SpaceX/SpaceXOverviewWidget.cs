using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 载人龙飞船综合工况与 ECLSS 环控维生监控面板 (SpaceX Vehicle Overview & ECLSS Panel)
    /// 包含：客舱气压 (Cabin Press)、氧分压 (O2 Level)、客舱温度 (Cabin Temp)、电网总线 (Net Power)，
    /// 以及气闸、推进剂、热控回路与机械对接口的 4 路状态矩阵。
    /// 严格遵循 MFP 规范，0 颜色字面量，10Hz Relaxed 阶梯刷新。
    /// </summary>
    [FlightWidget("spacex_overview", "dragon_overview", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 综合工况与 ECLSS 面板", Description = "飞船综合工况与维生监控：客舱压力、氧分压、客舱温度、电网功率与气闸/推进剂/热控状态。", DefaultWidgetId = "spacex.overview", DefaultX = -460f, DefaultY = 120f, IsSingleton = true, ExactIds = new[] { "spacex.overview" })]
    public class SpaceXOverviewWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _statusBadge;

        // 4 组 ECLSS 监控卡槽
        private Text _pressLabel;
        private Text _pressValue;
        private Image _pressTrack;
        private Image _pressFill;

        private Text _o2Label;
        private Text _o2Value;
        private Image _o2Track;
        private Image _o2Fill;

        private Text _tempLabel;
        private Text _tempValue;
        private Image _tempTrack;
        private Image _tempFill;

        private Text _pwrLabel;
        private Text _pwrValue;
        private Image _pwrTrack;
        private Image _pwrFill;

        // 底部子系统状态列表 (4 行紧凑状态标签)
        private Text _subsysAirlock;
        private Text _subsysProp;
        private Text _subsysThermal;
        private Text _subsysDock;

        // 脏数据变动缓存
        private float _lastPress = -1f;
        private float _lastO2 = -1f;
        private float _lastTemp = -1f;
        private float _lastEc = -1f;
        private float _lastProp = -1f;
        private string _lastStatusBadge = string.Empty;
        private string _lastPressStr = string.Empty;
        private string _lastO2Str = string.Empty;
        private string _lastTempStr = string.Empty;
        private string _lastPwrStr = string.Empty;
        private string _lastPropStr = string.Empty;

        // CustomTemplate 自定义通道
        private string _titleCustom = "VEHICLE OVERVIEW / ECLSS";
        private string _pressLabelStr = "CABIN PRESS";
        private string _o2LabelStr = "O2 LEVEL";
        private string _tempLabelStr = "CABIN TEMP";
        private string _pwrLabelStr = "NET POWER";
        private string _airlockLabelStr = "AIRLOCK HATCH";
        private string _propLabelStr = "DRACO RCS PROP";
        private string _thermalLabelStr = "ACTIVE THERMAL LOOP";
        private string _dockLabelStr = "DOCKING MECHANISM";

        private void ParseCustomTemplate(string tpl)
        {
            _titleCustom = "VEHICLE OVERVIEW / ECLSS";
            _pressLabelStr = "CABIN PRESS";
            _o2LabelStr = "O2 LEVEL";
            _tempLabelStr = "CABIN TEMP";
            _pwrLabelStr = "NET POWER";
            _airlockLabelStr = "AIRLOCK HATCH";
            _propLabelStr = "DRACO RCS PROP";
            _thermalLabelStr = "ACTIVE THERMAL LOOP";
            _dockLabelStr = "DOCKING MECHANISM";

            if (string.IsNullOrEmpty(tpl)) return;
            string[] pairs = tpl.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i].Trim();
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                string k = p.Substring(0, eq).Trim().ToUpperInvariant();
                string v = p.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "TITLE": _titleCustom = v; break;
                    case "PRESS_LABEL": _pressLabelStr = v; break;
                    case "O2_LABEL": _o2LabelStr = v; break;
                    case "TEMP_LABEL": _tempLabelStr = v; break;
                    case "PWR_LABEL": _pwrLabelStr = v; break;
                    case "AIRLOCK_LABEL": _airlockLabelStr = v; break;
                    case "PROP_LABEL": _propLabelStr = v; break;
                    case "THERMAL_LABEL": _thermalLabelStr = v; break;
                    case "DOCK_LABEL": _dockLabelStr = v; break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            Vector2 panelSize = new Vector2(240f * s, 180f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 1. 顶部标题栏 (240 x 24)
            _titleText = UIFactory.CreateText(transform, "Title", _titleCustom, Mathf.RoundToInt(9f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            _titleText.rectTransform.anchoredPosition = new Vector2(-12f * s, panelSize.y * 0.5f - 14f * s);
            _titleText.rectTransform.sizeDelta = new Vector2(140f * s, 16f * s);

            _statusBadge = UIFactory.CreateText(transform, "StatusBadge", "NOMINAL", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            _statusBadge.rectTransform.anchoredPosition = new Vector2(panelSize.x * 0.5f - 40f * s, panelSize.y * 0.5f - 14f * s);
            _statusBadge.rectTransform.sizeDelta = new Vector2(70f * s, 16f * s);

            // 分割细线
            UIFactory.CreatePanel(transform, "HeaderSep", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, panelSize.y * 0.5f - 24f * s), style.GetLineColor(LineWeight.Faint, theme));

            // 2. 2x2 环控仪表矩阵
            float col1X = -58f * s;
            float col2X = 58f * s;
            float row1Y = 32f * s;
            float row2Y = -12f * s;
            float itemW = 100f * s;

            // (1) CABIN PRESS
            BuildMeterSlot("Press", col1X, row1Y, itemW, s, theme, _pressLabelStr, "101.3 kPa", out _pressLabel, out _pressValue, out _pressTrack, out _pressFill);

            // (2) O2 LEVEL
            BuildMeterSlot("O2", col2X, row1Y, itemW, s, theme, _o2LabelStr, "100%", out _o2Label, out _o2Value, out _o2Track, out _o2Fill);

            // (3) CABIN TEMP
            BuildMeterSlot("Temp", col1X, row2Y, itemW, s, theme, _tempLabelStr, "21.0°C", out _tempLabel, out _tempValue, out _tempTrack, out _tempFill);

            // (4) NET POWER
            BuildMeterSlot("Power", col2X, row2Y, itemW, s, theme, _pwrLabelStr, "28.2V / 100%", out _pwrLabel, out _pwrValue, out _pwrTrack, out _pwrFill);

            // 分割细线
            UIFactory.CreatePanel(transform, "MidSep", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, -34f * s), style.GetLineColor(LineWeight.Faint, theme));

            // 3. 底部 4 行子系统状态微标
            float btmY = -48f * s;
            float rowH = 12f * s;

            _subsysAirlock = CreateStatusRow("Airlock", -panelSize.x * 0.5f + 14f * s, btmY, panelSize.x - 28f * s, s, theme, _airlockLabelStr, "SECURED");
            _subsysProp = CreateStatusRow("Prop", -panelSize.x * 0.5f + 14f * s, btmY - rowH, panelSize.x - 28f * s, s, theme, _propLabelStr, "100%");
            _subsysThermal = CreateStatusRow("Thermal", -panelSize.x * 0.5f + 14f * s, btmY - rowH * 2, panelSize.x - 28f * s, s, theme, _thermalLabelStr, "AUTO");
            _subsysDock = CreateStatusRow("Dock", -panelSize.x * 0.5f + 14f * s, btmY - rowH * 3, panelSize.x - 28f * s, s, theme, _dockLabelStr, "READY");
        }

        private void BuildMeterSlot(string id, float x, float y, float width, float s, ThemeConfig theme, string labelStr, string defaultVal,
            out Text lbl, out Text val, out Image track, out Image fill)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            lbl = UIFactory.CreateText(transform, $"{id}_Lbl", labelStr, Mathf.RoundToInt(7.5f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
            lbl.rectTransform.anchoredPosition = new Vector2(x, y + 10f * s);
            lbl.rectTransform.sizeDelta = new Vector2(width, 12f * s);

            val = UIFactory.CreateText(transform, $"{id}_Val", defaultVal, Mathf.RoundToInt(10.5f * s), TextAnchor.UpperRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            val.rectTransform.anchoredPosition = new Vector2(x, y + 10f * s);
            val.rectTransform.sizeDelta = new Vector2(width, 14f * s);

            GameObject trkGo = UIFactory.CreatePanel(transform, $"{id}_Track", new Vector2(width, 3f * s), new Vector2(x, y - 6f * s), style.GetMeterColor(MeterStyleRole.Track, theme));
            track = trkGo.GetComponent<Image>();

            GameObject fillGo = UIFactory.CreatePanel(trkGo.transform, $"{id}_Fill", new Vector2(width, 3f * s), Vector2.zero, theme.AccentPrimary);
            fill = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
        }

        private Text CreateStatusRow(string id, float startX, float y, float width, float s, ThemeConfig theme, string name, string status)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float halfW = width * 0.5f;

            Text rowName = UIFactory.CreateText(transform, $"{id}_Name", name, Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Muted, theme));
            rowName.rectTransform.anchoredPosition = new Vector2(startX + halfW, y);
            rowName.rectTransform.sizeDelta = new Vector2(width, 12f * s);

            Text rowVal = UIFactory.CreateText(transform, $"{id}_Val", status, Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            rowVal.rectTransform.anchoredPosition = new Vector2(startX + halfW, y);
            rowVal.rectTransform.sizeDelta = new Vector2(width, 12f * s);

            return rowVal;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 舱压 (Cabin Pressure)
            float press = (float)telemetry.AtmosphericPressure * 101.325f;
            if (press <= 0.01f) press = 101.3f; // 默认封闭座舱压力
            if (Mathf.Abs(press - _lastPress) > 0.1f)
            {
                _lastPress = press;
                string pStr = $"{press:F1} kPa";
                if (pStr != _lastPressStr && _pressValue != null)
                {
                    _lastPressStr = pStr;
                    _pressValue.text = pStr;
                }
                if (_pressFill != null)
                {
                    float ratio = Mathf.Clamp01(press / 105f);
                    _pressFill.rectTransform.sizeDelta = new Vector2(100f * CurrentDpiScale * ratio, 3f * CurrentDpiScale);
                }
            }

            // 2. 氧气百分比 (O2)
            float o2 = telemetry.OxygenPercent > 0.001f ? telemetry.OxygenPercent : 100f;
            if (Mathf.Abs(o2 - _lastO2) > 0.5f)
            {
                _lastO2 = o2;
                string oStr = $"{o2:F0}%";
                if (oStr != _lastO2Str && _o2Value != null)
                {
                    _lastO2Str = oStr;
                    _o2Value.text = oStr;
                }
                if (_o2Fill != null)
                {
                    float ratio = Mathf.Clamp01(o2 * 0.01f);
                    _o2Fill.rectTransform.sizeDelta = new Vector2(100f * CurrentDpiScale * ratio, 3f * CurrentDpiScale);
                }
            }

            // 3. 客舱温度 (Cabin Temp)
            float temp = (float)telemetry.CabinTemp;
            if (temp <= 0.1f) temp = 21.5f;
            if (Mathf.Abs(temp - _lastTemp) > 0.2f)
            {
                _lastTemp = temp;
                string tStr = $"{temp:F1}°C";
                if (tStr != _lastTempStr && _tempValue != null)
                {
                    _lastTempStr = tStr;
                    _tempValue.text = tStr;
                }
                if (_tempFill != null)
                {
                    float ratio = Mathf.Clamp01(temp / 40f);
                    _tempFill.rectTransform.sizeDelta = new Vector2(100f * CurrentDpiScale * ratio, 3f * CurrentDpiScale);
                }
            }

            // 4. 电力与总线电压 (Net Power)
            float ec = (float)telemetry.EcPercent;
            float volt = telemetry.BusVoltage > 0f ? telemetry.BusVoltage : 28.2f;
            if (Mathf.Abs(ec - _lastEc) > 0.5f)
            {
                _lastEc = ec;
                string pwrStr = $"{volt:F1}V / {ec:F0}%";
                if (pwrStr != _lastPwrStr && _pwrValue != null)
                {
                    _lastPwrStr = pwrStr;
                    _pwrValue.text = pwrStr;
                }
                if (_pwrFill != null)
                {
                    float ratio = Mathf.Clamp01(ec * 0.01f);
                    _pwrFill.rectTransform.sizeDelta = new Vector2(100f * CurrentDpiScale * ratio, 3f * CurrentDpiScale);
                }
            }

            // 5. 推进剂储备 (Draco Prop)
            float prop = telemetry.StagePropellantFraction * 100f;
            if (Mathf.Abs(prop - _lastProp) > 0.5f)
            {
                _lastProp = prop;
                string propStr = $"{prop:F0}%";
                if (propStr != _lastPropStr && _subsysProp != null)
                {
                    _lastPropStr = propStr;
                    _subsysProp.text = propStr;
                }
            }

            // 6. 总体警告判定
            string badge = (o2 < 20f || ec < 15f) ? "WARN" : "NOMINAL";
            if (badge != _lastStatusBadge && _statusBadge != null)
            {
                _lastStatusBadge = badge;
                _statusBadge.text = badge;
                ThemeConfig th = WidgetStyleManager.Instance.CurrentTheme;
                ApplyText(_statusBadge, badge == "NOMINAL" ? TextStyleRole.Accent : TextStyleRole.Warning, th);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);

            if (_titleText != null) ApplyText(_titleText, TextStyleRole.Label, theme);
            if (_statusBadge != null) ApplyText(_statusBadge, _lastStatusBadge == "WARN" ? TextStyleRole.Warning : TextStyleRole.Accent, theme);

            if (_pressLabel != null) ApplyText(_pressLabel, TextStyleRole.Label, theme);
            if (_pressValue != null) ApplyText(_pressValue, TextStyleRole.PrimaryValue, theme);
            ApplyMeter(_pressTrack, _pressFill, null, MeterStyleRole.Primary, theme);

            if (_o2Label != null) ApplyText(_o2Label, TextStyleRole.Label, theme);
            if (_o2Value != null) ApplyText(_o2Value, TextStyleRole.PrimaryValue, theme);
            ApplyMeter(_o2Track, _o2Fill, null, MeterStyleRole.Primary, theme);

            if (_tempLabel != null) ApplyText(_tempLabel, TextStyleRole.Label, theme);
            if (_tempValue != null) ApplyText(_tempValue, TextStyleRole.PrimaryValue, theme);
            ApplyMeter(_tempTrack, _tempFill, null, MeterStyleRole.Primary, theme);

            if (_pwrLabel != null) ApplyText(_pwrLabel, TextStyleRole.Label, theme);
            if (_pwrValue != null) ApplyText(_pwrValue, TextStyleRole.PrimaryValue, theme);
            ApplyMeter(_pwrTrack, _pwrFill, null, MeterStyleRole.Primary, theme);

            if (_subsysAirlock != null) ApplyText(_subsysAirlock, TextStyleRole.Accent, theme);
            if (_subsysProp != null) ApplyText(_subsysProp, TextStyleRole.SecondaryValue, theme);
            if (_subsysThermal != null) ApplyText(_subsysThermal, TextStyleRole.Accent, theme);
            if (_subsysDock != null) ApplyText(_subsysDock, TextStyleRole.Accent, theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
