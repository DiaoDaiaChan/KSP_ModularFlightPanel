using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 生命维持与居住舱环境监控卡片 (Life Support / Habitat Monitor)
    /// 监控乘员数、舱压环境、氧气/水/电力/姿控维生储备进度条
    /// </summary>
    public class LifeSupportWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _subTitleText;
        private Text _statusBadge;

        // 居住环境简报
        private Text _crewText;
        private Text _pressureText;
        private Text _tempText;

        // 4 项核心消耗品计量槽 (2x2 网格)
        private struct ResourceGaugeUI
        {
            public GameObject BoxObj;
            public Text SymbolText;
            public Text NameText;
            public Text StatusText;
            public RectTransform BarFill;
            public Text PercentText;
            public float BarWidth;
        }

        private ResourceGaugeUI[] _gauges = new ResourceGaugeUI[4];

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(270f * s, 160f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            // 1. 顶部 Header
            _titleText = UIFactory.CreateText(transform, "Title", "LIFE SUPPORT", Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(100f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-70f * s, 64f * s);

            _subTitleText = UIFactory.CreateText(transform, "SubTitle", "HABITAT & CREW", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(85f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(25f * s, 64f * s);

            _statusBadge = UIFactory.CreateText(transform, "Badge", "● NOMINAL", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statRt = _statusBadge.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(70f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(95f * s, 64f * s);

            // 分割线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 52f * s), theme.FrameBorderColor);

            // 2. 乘员与舱压环境摘要行
            _crewText = UIFactory.CreateText(transform, "Sum_Crew", "CREW 0/0", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, theme.WarningColor);
            RectTransform crewRt = _crewText.GetComponent<RectTransform>();
            crewRt.sizeDelta = new Vector2(75f * s, 14f * s);
            crewRt.anchoredPosition = new Vector2(-85f * s, 40f * s);

            _pressureText = UIFactory.CreateText(transform, "Sum_Pres", "ATM 101.3 kPa", Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform presRt = _pressureText.GetComponent<RectTransform>();
            presRt.sizeDelta = new Vector2(95f * s, 14f * s);
            presRt.anchoredPosition = new Vector2(0f, 40f * s);

            _tempText = UIFactory.CreateText(transform, "Sum_Temp", "21.0 °C", Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight, theme.TextAccentColor);
            RectTransform tempRt = _tempText.GetComponent<RectTransform>();
            tempRt.sizeDelta = new Vector2(65f * s, 14f * s);
            tempRt.anchoredPosition = new Vector2(95f * s, 40f * s);

            // 3. 2x2 维生资源仪表网格 (O2, EC, MONO, H2O/FUEL)
            Vector2 boxSize = new Vector2(122f * s, 42f * s);
            Vector2[] boxPositions = new Vector2[]
            {
                new Vector2(-65f * s, 9f * s),   // 左上: O2
                new Vector2(65f * s, 9f * s),    // 右上: EC
                new Vector2(-65f * s, -38f * s), // 左下: MONO
                new Vector2(65f * s, -38f * s)   // 右下: H2O
            };

            Color[] colors = new Color[]
            {
                theme.AccentSecondary,
                theme.WarningColor,
                theme.CautionColor,
                theme.AccentPrimary
            };

            string[] symbols = new string[] { "O₂", "EC", "RCS", "H₂O" };
            string[] names = new string[] { "OXYGEN", "POWER", "MONOPROP", "WATER" };

            for (int i = 0; i < 4; i++)
            {
                _gauges[i] = CreateResourceGauge(transform, $"Gauge_{i}", boxSize, boxPositions[i], symbols[i], names[i], colors[i]);
            }
        }

        private ResourceGaugeUI CreateResourceGauge(Transform parent, string name, Vector2 size, Vector2 pos,
            string symbol, string resName, Color accentColor)
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.gray;
            Color cellBg = new Color(0.06f, 0.09f, 0.14f, 0.45f);
            Color faintBorder = new Color(borderCol.r, borderCol.g, borderCol.b, 0.18f);

            ResourceGaugeUI g = new ResourceGaugeUI();
            g.BoxObj = UIFactory.CreatePanel(parent, name, size, pos, cellBg, faintBorder, 1f * s);

            g.SymbolText = UIFactory.CreateText(g.BoxObj.transform, "Symbol", symbol, Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleLeft, accentColor);
            RectTransform symRt = g.SymbolText.GetComponent<RectTransform>();
            symRt.sizeDelta = new Vector2(30f * s, 14f * s);
            symRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 18f * s, 11f * s);

            g.NameText = UIFactory.CreateText(g.BoxObj.transform, "Name", resName, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleLeft, theme != null ? (Color)theme.TextAccentColor : Color.gray);
            RectTransform nmRt = g.NameText.GetComponent<RectTransform>();
            nmRt.sizeDelta = new Vector2(45f * s, 12f * s);
            nmRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 52f * s, 11f * s);

            g.StatusText = UIFactory.CreateText(g.BoxObj.transform, "Stat", "OK", Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleRight, accentColor);
            RectTransform stRt = g.StatusText.GetComponent<RectTransform>();
            stRt.sizeDelta = new Vector2(35f * s, 12f * s);
            stRt.anchoredPosition = new Vector2((size.x * 0.5f) - 22f * s, 11f * s);

            g.BarWidth = size.x - 14f * s;
            GameObject barBg = UIFactory.CreatePanel(g.BoxObj.transform, "BarBg", new Vector2(g.BarWidth, 5f * s),
                new Vector2(0f, -4f * s), new Color(borderCol.r, borderCol.g, borderCol.b, 0.25f));

            GameObject barFill = UIFactory.CreatePanel(barBg.transform, "BarFill", new Vector2(g.BarWidth, 5f * s),
                Vector2.zero, accentColor);
            g.BarFill = barFill.GetComponent<RectTransform>();
            g.BarFill.pivot = new Vector2(0f, 0.5f);
            g.BarFill.anchoredPosition = new Vector2(-(g.BarWidth * 0.5f), 0f);

            g.PercentText = UIFactory.CreateText(g.BoxObj.transform, "Pct", "100.0%", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleRight, Color.white);
            RectTransform pctRt = g.PercentText.GetComponent<RectTransform>();
            pctRt.sizeDelta = new Vector2(size.x - 14f * s, 12f * s);
            pctRt.anchoredPosition = new Vector2(0f, -14f * s);

            return g;
        }

        public override float DefaultUpdateInterval => 0.5f; // 维生储备 2Hz 刷新，节省大量 CPU 开销

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color primaryCol = theme != null ? (Color)theme.AccentPrimary : Color.green;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.red;
            Color cautionCol = theme != null ? (Color)theme.CautionColor : Color.yellow;

            float o2Fraction = (float)(TelemetryTokenEngine.EvaluateNumeric("{O2}", telemetry) / 100.0);
            float ecFraction = (float)(TelemetryTokenEngine.EvaluateNumeric("{EC:PCT}", telemetry) / 100.0);
            float monoFraction = (float)(TelemetryTokenEngine.EvaluateNumeric("{MONO}", telemetry) / 100.0);
            float h2oFraction = (float)(TelemetryTokenEngine.EvaluateNumeric("{WATER}", telemetry) / 100.0);

            _crewText.text = TelemetryTokenEngine.Evaluate("{CREW}", telemetry);
            _pressureText.text = TelemetryTokenEngine.Evaluate("{ATM}", telemetry);
            _tempText.text = TelemetryTokenEngine.Evaluate("{TEMP}", telemetry);

            UpdateGauge(0, o2Fraction, TelemetryTokenEngine.Evaluate("{O2}", telemetry));
            UpdateGauge(1, ecFraction, TelemetryTokenEngine.Evaluate("{EC:PCT}%", telemetry));
            UpdateGauge(2, monoFraction, TelemetryTokenEngine.Evaluate("{MONO}", telemetry));
            UpdateGauge(3, h2oFraction, TelemetryTokenEngine.Evaluate("{WATER}", telemetry));

            if (ecFraction < 0.1f || o2Fraction < 0.15f)
            {
                _statusBadge.text = "▲ WARNING";
                _statusBadge.color = warnCol;
            }
            else if (ecFraction < 0.25f)
            {
                _statusBadge.text = "● CAUTION";
                _statusBadge.color = cautionCol;
            }
            else
            {
                _statusBadge.text = "● NOMINAL";
                _statusBadge.color = primaryCol;
            }
        }

        private void UpdateGauge(int index, float fraction, string valueStr)
        {
            ResourceGaugeUI g = _gauges[index];
            float clamped = Mathf.Clamp01(fraction);
            g.BarFill.sizeDelta = new Vector2(g.BarWidth * clamped, g.BarFill.sizeDelta.y);
            g.PercentText.text = valueStr;
            g.StatusText.text = clamped < 0.15f ? "WARN" : "OK";
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = theme.FrameBgColor;
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.TextPrimaryColor;
            if (_subTitleText != null) _subTitleText.color = theme.AccentSecondary;
            if (_statusBadge != null) _statusBadge.color = theme.AccentPrimary;
            if (_pressureText != null) _pressureText.color = theme.TextAccentColor;
            if (_tempText != null) _tempText.color = theme.TextAccentColor;
        }
    }
}
