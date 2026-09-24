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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

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
            theme = WidgetStyleManager.ResolveTheme(theme);
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
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            Color borderCol = theme.FrameBorderColor;
            Color cellBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);
            Color faintBorder = WidgetStyleManager.Weighted(borderCol, LineWeight.Faint);

            ResourceGaugeUI g = new ResourceGaugeUI();
            g.BoxObj = UIFactory.CreatePanel(parent, name, size, pos, cellBg, faintBorder, 1f * s);

            g.SymbolText = UIFactory.CreateText(g.BoxObj.transform, "Symbol", symbol, Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleLeft, accentColor);
            RectTransform symRt = g.SymbolText.GetComponent<RectTransform>();
            symRt.sizeDelta = new Vector2(30f * s, 14f * s);
            symRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 18f * s, 11f * s);

            g.NameText = UIFactory.CreateText(g.BoxObj.transform, "Name", resName, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleLeft, theme.TextAccentColor);
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
                new Vector2(0f, -4f * s), WidgetStyleManager.Weighted(borderCol, LineWeight.Light));

            GameObject barFill = UIFactory.CreatePanel(barBg.transform, "BarFill", new Vector2(g.BarWidth, 5f * s),
                Vector2.zero, accentColor);
            g.BarFill = barFill.GetComponent<RectTransform>();
            g.BarFill.pivot = new Vector2(0f, 0.5f);
            g.BarFill.anchoredPosition = new Vector2(-(g.BarWidth * 0.5f), 0f);

            g.PercentText = UIFactory.CreateText(g.BoxObj.transform, "Pct", "100.0%", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleRight, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            RectTransform pctRt = g.PercentText.GetComponent<RectTransform>();
            pctRt.sizeDelta = new Vector2(size.x - 14f * s, 12f * s);
            pctRt.anchoredPosition = new Vector2(0f, -14f * s);

            return g;
        }

        private float[] _lastFractions = new float[] { -1f, -1f, -1f, -1f };
        private string[] _lastPercentTexts = new string[4];
        private string[] _lastStatusTexts = new string[4];
        private string _lastCrewText;
        private string _lastPressureText;
        private string _lastTempText;
        private int _lastStatusBadgeState = -1;

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            string slot0Token = GetTemplateChannel("SLOT0_TOKEN", "{O2}");
            string slot1Token = GetTemplateChannel("SLOT1_TOKEN", "{EC:PCT}");
            string slot2Token = GetTemplateChannel("SLOT2_TOKEN", "{MONO}");
            string slot3Token = GetTemplateChannel("SLOT3_TOKEN", "{WATER}");

            float o2Fraction = (float)(TelemetryTokenEngine.EvaluateNumeric(slot0Token, telemetry) / 100.0);
            float ecFraction = (float)(TelemetryTokenEngine.EvaluateNumeric(slot1Token, telemetry) / 100.0);
            float monoFraction = (float)(TelemetryTokenEngine.EvaluateNumeric(slot2Token, telemetry) / 100.0);
            float h2oFraction = (float)(TelemetryTokenEngine.EvaluateNumeric(slot3Token, telemetry) / 100.0);

            string crewTpl = GetTemplateChannel("CREW_TPL", "{CREW}");
            string atmTpl = GetTemplateChannel("ATM_TPL", "{ATM}");
            string tempTpl = GetTemplateChannel("TEMP_TPL", "{TEMP}");

            string crewStr = TelemetryTokenEngine.Evaluate(crewTpl, telemetry);
            if (crewStr != _lastCrewText)
            {
                _lastCrewText = crewStr;
                _crewText.text = crewStr;
            }

            string presStr = TelemetryTokenEngine.Evaluate(atmTpl, telemetry);
            if (presStr != _lastPressureText)
            {
                _lastPressureText = presStr;
                _pressureText.text = presStr;
            }

            string tempStr = TelemetryTokenEngine.Evaluate(tempTpl, telemetry);
            if (tempStr != _lastTempText)
            {
                _lastTempText = tempStr;
                _tempText.text = tempStr;
            }

            UpdateGauge(0, o2Fraction, TelemetryTokenEngine.Evaluate(slot0Token, telemetry));
            UpdateGauge(1, ecFraction, TelemetryTokenEngine.Evaluate(slot1Token + "%", telemetry));
            UpdateGauge(2, monoFraction, TelemetryTokenEngine.Evaluate(slot2Token, telemetry));
            UpdateGauge(3, h2oFraction, TelemetryTokenEngine.Evaluate(slot3Token, telemetry));

            int badgeState = (ecFraction < 0.1f || o2Fraction < 0.15f) ? 2 : ((ecFraction < 0.25f) ? 1 : 0);
            if (badgeState != _lastStatusBadgeState)
            {
                _lastStatusBadgeState = badgeState;
                if (badgeState == 2)
                {
                    _statusBadge.text = "▲ WARNING";
                    ApplyText(_statusBadge, TextStyleRole.Danger, theme);
                }
                else if (badgeState == 1)
                {
                    _statusBadge.text = "● CAUTION";
                    ApplyText(_statusBadge, TextStyleRole.Warning, theme);
                }
                else
                {
                    _statusBadge.text = "● NOMINAL";
                    ApplyText(_statusBadge, TextStyleRole.Accent, theme);
                }
            }
        }

        private void UpdateGauge(int index, float fraction, string valueStr)
        {
            ResourceGaugeUI g = _gauges[index];
            float clamped = Mathf.Clamp01(fraction);

            if (Math.Abs(clamped - _lastFractions[index]) > 0.005f)
            {
                _lastFractions[index] = clamped;
                g.BarFill.sizeDelta = new Vector2(g.BarWidth * clamped, g.BarFill.sizeDelta.y);
            }

            if (valueStr != _lastPercentTexts[index])
            {
                _lastPercentTexts[index] = valueStr;
                g.PercentText.text = valueStr;
            }

            string statStr = clamped < 0.15f ? "WARN" : "OK";
            if (statStr != _lastStatusTexts[index])
            {
                _lastStatusTexts[index] = statStr;
                g.StatusText.text = statStr;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "LIFE SUPPORT");
                ApplyText(_titleText, TextStyleRole.PrimaryValue, theme);
            }
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "HABITAT & CREW");
                ApplyText(_subTitleText, TextStyleRole.Label, theme);
            }
            if (_statusBadge != null) ApplyText(_statusBadge, TextStyleRole.Accent, theme);
            if (_pressureText != null) ApplyText(_pressureText, TextStyleRole.SecondaryValue, theme);
            if (_tempText != null) ApplyText(_tempText, TextStyleRole.SecondaryValue, theme);

            for (int i = 0; i < _gauges.Length; i++)
            {
                if (_gauges[i].SymbolText != null) ApplyText(_gauges[i].SymbolText, TextStyleRole.Accent, theme);
                if (_gauges[i].NameText != null) ApplyText(_gauges[i].NameText, TextStyleRole.Label, theme);
                if (_gauges[i].PercentText != null) ApplyText(_gauges[i].PercentText, TextStyleRole.PrimaryValue, theme);
                if (_gauges[i].StatusText != null) ApplyText(_gauges[i].StatusText, TextStyleRole.Label, theme);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
