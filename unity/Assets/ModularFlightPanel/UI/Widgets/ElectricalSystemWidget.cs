using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 航电电气系统监控面板 (ELEC Power Distribution)
    /// 监控飞船蓄电池组、直流总线母线、太阳能/发电机电源供给与即时净充放电率
    /// </summary>
    public class ElectricalSystemWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _subTitleText;
        private Text _statusText;

        // 电源节点 UI 元素
        private Text _bat1ValText;
        private Text _bat1SubText;
        private Text _bat2ValText;
        private Text _bat2SubText;

        private Text _dcBusValText;
        private Text _dcBusSubText;

        private Text _genValText;
        private Text _genSubText;
        private Text _loadValText;
        private Text _loadSubText;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 panelSize = new Vector2(280f * CurrentDpiScale, 155f * CurrentDpiScale);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, CurrentDpiScale);

            float s = CurrentDpiScale;

            // 1. 顶部 Header
            _titleText = UIFactory.CreateText(transform, "Title", "ELEC", Mathf.RoundToInt(13f * s), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(50f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-100f * s, 62f * s);

            _subTitleText = UIFactory.CreateText(transform, "SubTitle", "POWER DISTRIBUTION", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(120f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(-15f * s, 62f * s);

            _statusText = UIFactory.CreateText(transform, "Status", "● LIVE", Mathf.RoundToInt(9f * s), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statRt = _statusText.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(60f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(100f * s, 62f * s);

            // 分割横线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 50f * s), theme.FrameBorderColor);

            // 2. 第一行：BAT 1 & BAT 2 蓄电池节点
            CreateNodeBox(transform, "Node_BAT1", new Vector2(75f * s, 36f * s), new Vector2(-88f * s, 26f * s), "BAT 1",
                theme.WarningColor, out _bat1ValText, out _bat1SubText);

            CreateNodeBox(transform, "Node_BAT2", new Vector2(75f * s, 36f * s), new Vector2(88f * s, 26f * s), "BAT 2",
                theme.WarningColor, out _bat2ValText, out _bat2SubText);

            // 中央主要 DC ESS BUS 母线节点
            CreateNodeBox(transform, "Node_DCBUS", new Vector2(90f * s, 36f * s), new Vector2(0f, 26f * s), "DC ESS BUS",
                theme.AccentPrimary, out _dcBusValText, out _dcBusSubText);

            // 3. 第二行：发电源 (SOLAR / GEN) 与 负载监控 (LOAD)
            CreateNodeBox(transform, "Node_GEN", new Vector2(122f * s, 40f * s), new Vector2(-65f * s, -22f * s), "POWER SOURCES",
                theme.AccentSecondary, out _genValText, out _genSubText);

            CreateNodeBox(transform, "Node_LOAD", new Vector2(122f * s, 40f * s), new Vector2(65f * s, -22f * s), "BUS TELEMETRY",
                theme.AccentPrimary, out _loadValText, out _loadSubText);

            // 底部状态提示微标
            Text tipText = UIFactory.CreateText(transform, "FooterTip", "28V DC BUS SYSTEM  ·  PRIMARY AVIONICS",
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform tipRt = tipText.GetComponent<RectTransform>();
            tipRt.sizeDelta = new Vector2(panelSize.x - 20f * s, 12f * s);
            tipRt.anchoredPosition = new Vector2(0f, -64f * s);
        }

        private void CreateNodeBox(Transform parent, string name, Vector2 size, Vector2 pos, string nodeTitle,
            Color accentColor, out Text valText, out Text subText)
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.gray;
            Color cellBg = new Color(0.06f, 0.09f, 0.14f, 0.45f);
            Color faintBorder = new Color(borderCol.r, borderCol.g, borderCol.b, 0.18f);
            GameObject box = UIFactory.CreatePanel(parent, name, size, pos, cellBg, faintBorder, 1f * s);

            // 顶部极细状态标示条 (Micro Accent Line)
            UIFactory.CreatePanel(box.transform, "AccentBar", new Vector2(size.x - 6f * s, 1.5f * s),
                new Vector2(0f, (size.y * 0.5f) - 1.5f * s), accentColor);

            Text title = UIFactory.CreateText(box.transform, "Title", nodeTitle, Mathf.RoundToInt(7.5f * s),
                TextAnchor.UpperCenter, theme != null ? (Color)theme.TextAccentColor : accentColor);
            RectTransform titRt = title.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(size.x - 4f * s, 12f * s);
            titRt.anchoredPosition = new Vector2(0f, (size.y * 0.5f) - 7f * s);

            valText = UIFactory.CreateText(box.transform, "Value", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleCenter, Color.white);
            RectTransform valRt = valText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(size.x - 4f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(0f, (size.y * 0.5f) - 18f * s);

            subText = UIFactory.CreateText(box.transform, "Sub", "---", Mathf.RoundToInt(7f * s),
                TextAnchor.LowerCenter, accentColor);
            RectTransform subRt = subText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(size.x - 4f * s, 11f * s);
            subRt.anchoredPosition = new Vector2(0f, -(size.y * 0.5f) + 6f * s);
        }

        public override float DefaultUpdateInterval => 0.2f; // 电力系统 5Hz 刷新足以满足监控需求，节省 CPU

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color primaryCol = theme != null ? (Color)theme.AccentPrimary : Color.green;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.red;
            Color textAcc = theme != null ? (Color)theme.TextAccentColor : Color.gray;

            double currentEc = TelemetryTokenEngine.EvaluateNumeric("{EC}", telemetry);
            double netRate = TelemetryTokenEngine.EvaluateNumeric("{EC:RATE}", telemetry);
            float busVoltage = (float)TelemetryTokenEngine.EvaluateNumeric("{VOLT}", telemetry);
            if (float.IsNaN(busVoltage)) busVoltage = 28.0f;
            int solarActiveCount = (int)TelemetryTokenEngine.EvaluateNumeric("{SOLAR:ACTIVE}", telemetry);

            // BAT 1 & BAT 2
            _bat1ValText.text = TelemetryTokenEngine.Evaluate("{VOLT}", telemetry);
            _bat1SubText.text = currentEc > 1.0 ? "ONLINE" : "DEPLETED";
            _bat1SubText.color = currentEc > 1.0 ? primaryCol : warnCol;

            _bat2ValText.text = $"{busVoltage * 0.995f:F1} V";
            _bat2SubText.text = currentEc > 1.0 ? "STANDBY" : "OFFLINE";
            _bat2SubText.color = currentEc > 1.0 ? textAcc : warnCol;

            // DC ESS BUS
            _dcBusValText.text = TelemetryTokenEngine.Evaluate("{EC:PCT}%", telemetry);
            _dcBusSubText.text = TelemetryTokenEngine.Evaluate("{EC}/{EC:MAX} EC", telemetry);

            // POWER SOURCES
            if (solarActiveCount > 0)
            {
                _genValText.text = TelemetryTokenEngine.Evaluate("+{SOLAR}", telemetry);
                _genValText.color = primaryCol;
                _genSubText.text = TelemetryTokenEngine.Evaluate("SOLAR ({SOLAR:ACTIVE} ACTIVE)", telemetry);
            }
            else
            {
                _genValText.text = "NO SOLAR";
                _genValText.color = textAcc;
                _genSubText.text = "BATTERY ONLY";
            }

            // LOAD & FLOW
            if (Math.Abs(netRate) < 0.01)
            {
                _loadValText.text = "0.00 e/s";
                _loadSubText.text = "BALANCED";
                _loadValText.color = Color.white;
            }
            else if (netRate > 0)
            {
                _loadValText.text = TelemetryTokenEngine.Evaluate("+{EC:RATE}", telemetry);
                _loadSubText.text = "CHARGING";
                _loadValText.color = primaryCol;
            }
            else
            {
                _loadValText.text = TelemetryTokenEngine.Evaluate("{EC:RATE}", telemetry);
                _loadSubText.text = "DRAINING";
                _loadValText.color = warnCol;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = theme.FrameBgColor;
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.TextPrimaryColor;
            if (_subTitleText != null) _subTitleText.color = theme.AccentSecondary;
            if (_statusText != null) _statusText.color = theme.AccentPrimary;
        }
    }
}
