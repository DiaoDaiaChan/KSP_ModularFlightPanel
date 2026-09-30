using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 维生系统仪表槽位状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct GaugeSlotSnapshot : IEquatable<GaugeSlotSnapshot>
    {
        public float Fraction;
        public string ValueStr;
        public string StatusStr;

        public bool Equals(GaugeSlotSnapshot other)
        {
            return Math.Abs(Fraction - other.Fraction) < 0.001f &&
                   ValueStr == other.ValueStr &&
                   StatusStr == other.StatusStr;
        }

        public override bool Equals(object obj) => obj is GaugeSlotSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Fraction.GetHashCode();
                hash = (hash * 397) ^ (ValueStr != null ? ValueStr.GetHashCode() : 0);
                hash = (hash * 397) ^ (StatusStr != null ? StatusStr.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 生命维持与居住环境状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct LifeSupportState : IEquatable<LifeSupportState>
    {
        public bool HasVessel;
        public string CrewStr;
        public string PresStr;
        public string TempStr;
        public int BadgeState;
        public GaugeSlotSnapshot Gauge0;
        public GaugeSlotSnapshot Gauge1;
        public GaugeSlotSnapshot Gauge2;
        public GaugeSlotSnapshot Gauge3;

        public GaugeSlotSnapshot GetGauge(int index)
        {
            switch (index)
            {
                case 0: return Gauge0;
                case 1: return Gauge1;
                case 2: return Gauge2;
                case 3: return Gauge3;
                default: return default;
            }
        }

        public bool Equals(LifeSupportState other)
        {
            return HasVessel == other.HasVessel &&
                   CrewStr == other.CrewStr &&
                   PresStr == other.PresStr &&
                   TempStr == other.TempStr &&
                   BadgeState == other.BadgeState &&
                   Gauge0.Equals(other.Gauge0) &&
                   Gauge1.Equals(other.Gauge1) &&
                   Gauge2.Equals(other.Gauge2) &&
                   Gauge3.Equals(other.Gauge3);
        }

        public override bool Equals(object obj) => obj is LifeSupportState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ BadgeState;
                hash = (hash * 397) ^ (CrewStr != null ? CrewStr.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 生命维持与居住环境纯业务逻辑大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class LifeSupportLogic : WidgetLogic<LifeSupportState>
    {
        public string Slot0Token { get; set; } = "{O2}";
        public string Slot1Token { get; set; } = "{EC:PCT}";
        public string Slot2Token { get; set; } = "{MONO}";
        public string Slot3Token { get; set; } = "{WATER}";
        public string CrewTemplate { get; set; } = "{CREW}";
        public string AtmTemplate { get; set; } = "{ATM}";
        public string TempTemplate { get; set; } = "{TEMP}";

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            string s0 = Slot0Token ?? "{O2}";
            string s1 = Slot1Token ?? "{EC:PCT}";
            string s2 = Slot2Token ?? "{MONO}";
            string s3 = Slot3Token ?? "{WATER}";

            float o2Fraction = Mathf.Clamp01((float)(TelemetryTokenEngine.EvaluateNumeric(s0, telemetry) / 100.0));
            float ecFraction = Mathf.Clamp01((float)(TelemetryTokenEngine.EvaluateNumeric(s1, telemetry) / 100.0));
            float monoFraction = Mathf.Clamp01((float)(TelemetryTokenEngine.EvaluateNumeric(s2, telemetry) / 100.0));
            float h2oFraction = Mathf.Clamp01((float)(TelemetryTokenEngine.EvaluateNumeric(s3, telemetry) / 100.0));

            string g0Text = TelemetryTokenEngine.Evaluate(s0, telemetry);
            string g1Text = TelemetryTokenEngine.Evaluate(s1 + "%", telemetry);
            string g2Text = TelemetryTokenEngine.Evaluate(s2, telemetry);
            string g3Text = TelemetryTokenEngine.Evaluate(s3, telemetry);

            string crewStr = TelemetryTokenEngine.Evaluate(CrewTemplate ?? "{CREW}", telemetry);
            string presStr = TelemetryTokenEngine.Evaluate(AtmTemplate ?? "{ATM}", telemetry);
            string tempStr = TelemetryTokenEngine.Evaluate(TempTemplate ?? "{TEMP}", telemetry);

            int badgeState = (ecFraction < 0.1f || o2Fraction < 0.15f) ? 2 : ((ecFraction < 0.25f) ? 1 : 0);

            CurrentState = new LifeSupportState
            {
                HasVessel = true,
                CrewStr = crewStr,
                PresStr = presStr,
                TempStr = tempStr,
                BadgeState = badgeState,
                Gauge0 = new GaugeSlotSnapshot { Fraction = o2Fraction, ValueStr = g0Text, StatusStr = o2Fraction < 0.15f ? "WARN" : "OK" },
                Gauge1 = new GaugeSlotSnapshot { Fraction = ecFraction, ValueStr = g1Text, StatusStr = ecFraction < 0.15f ? "WARN" : "OK" },
                Gauge2 = new GaugeSlotSnapshot { Fraction = monoFraction, ValueStr = g2Text, StatusStr = monoFraction < 0.15f ? "WARN" : "OK" },
                Gauge3 = new GaugeSlotSnapshot { Fraction = h2oFraction, ValueStr = g3Text, StatusStr = h2oFraction < 0.15f ? "WARN" : "OK" }
            };
        }
    }

    /// <summary>
    /// 原生 UGUI 生命维持与居住舱环境监控卡片 (Life Support / Habitat Monitor)
    /// 监控乘员数、舱压环境、氧气/水/电力/姿控维生储备进度条
    /// </summary>
    [FlightWidget("life_support", "life", "ecls", Category = WidgetCategory.Systems, DisplayName = "LIFE SUPPORT 维生消耗品监控", Description = "乘员居住舱压环境、氧气/电力/RCS/维生消耗品 2x2 进度仪表。", DefaultWidgetId = "custom.life", DefaultX = -440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "custom.life", "custom.life_support", "core.life_support" })]
    public class LifeSupportWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(270f, 160f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private readonly LifeSupportLogic _logic = new LifeSupportLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // 声明式微控件头部与状态徽标
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_LIFE_TITLE", "生命维持"));
        public TextWidget StatusBadge = TextWidget.Badge("● " + I18n.Tr("WIDGET_LIFE_NOMINAL", "正常"));

        private Text _subTitleText;

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
            Vector2 panelSize = BaseSize * s;

            // 1. 顶部 Header (Title & StatusBadge 已由基类微控件 DSL 自动构建)
            _subTitleText = UIFactory.CreateText(transform, "SubTitle", I18n.Tr("WIDGET_LIFE_HABITAT_CREW", "居住舱与乘员"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(85f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(25f * s, 64f * s);

            // 分割线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 52f * s), theme.FrameBorderColor);

            // 2. 乘员与舱压环境摘要行
            _crewText = UIFactory.CreateText(transform, "Sum_Crew", I18n.Tr("WIDGET_LIFE_CREW_PLACEHOLDER", "乘员 0/0"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, theme.WarningColor);
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

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetReadoutControl("environment_summary", "乘员环境摘要", _crewText != null ? _crewText.gameObject : null, _crewText, _pressureText, TextStyleRole.SecondaryValue, "{CREW}"));
            if (_gauges[0].BoxObj != null) this.Controls.Register(new WidgetLinearBarControl(this, "o2_gauge", "氧气余量", _gauges[0].BoxObj, null, _gauges[0].BarFill != null ? _gauges[0].BarFill.GetComponent<Image>() : null, "{O2}", 0.0, 100.0, _gauges[0].BarWidth, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });
            if (_gauges[1].BoxObj != null) this.Controls.Register(new WidgetLinearBarControl(this, "power_gauge", "电力储备", _gauges[1].BoxObj, null, _gauges[1].BarFill != null ? _gauges[1].BarFill.GetComponent<Image>() : null, "{EC:PCT}", 0.0, 100.0, _gauges[1].BarWidth, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });
            if (_gauges[2].BoxObj != null) this.Controls.Register(new WidgetLinearBarControl(this, "rcs_gauge", "RCS姿控", _gauges[2].BoxObj, null, _gauges[2].BarFill != null ? _gauges[2].BarFill.GetComponent<Image>() : null, "{MONO}", 0.0, 100.0, _gauges[2].BarWidth, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });
            if (_gauges[3].BoxObj != null) this.Controls.Register(new WidgetLinearBarControl(this, "water_gauge", "水/燃料储备", _gauges[3].BoxObj, null, _gauges[3].BarFill != null ? _gauges[3].BarFill.GetComponent<Image>() : null, "{WATER}", 0.0, 100.0, _gauges[3].BarWidth, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });

            _logic.Slot0Token = GetTemplateChannel("SLOT0_TOKEN", "{O2}");
            _logic.Slot1Token = GetTemplateChannel("SLOT1_TOKEN", "{EC:PCT}");
            _logic.Slot2Token = GetTemplateChannel("SLOT2_TOKEN", "{MONO}");
            _logic.Slot3Token = GetTemplateChannel("SLOT3_TOKEN", "{WATER}");
            _logic.CrewTemplate = GetTemplateChannel("CREW_TPL", "{CREW}");
            _logic.AtmTemplate = GetTemplateChannel("ATM_TPL", "{ATM}");
            _logic.TempTemplate = GetTemplateChannel("TEMP_TPL", "{TEMP}");
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

        private readonly CachedFloat[] _lastGaugeFractions = new CachedFloat[]
        {
            new CachedFloat(-1f, 0.005f),
            new CachedFloat(-1f, 0.005f),
            new CachedFloat(-1f, 0.005f),
            new CachedFloat(-1f, 0.005f)
        };
        private readonly Cached<string> _lastCrewText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPressureText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastTempText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastStatusBadgeState = new Cached<int>(-1);
        private readonly Cached<string>[] _lastGaugeValues = new Cached<string>[]
        {
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty)
        };
        private readonly Cached<string>[] _lastGaugeStatuses = new Cached<string>[]
        {
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty),
            new Cached<string>(string.Empty)
        };

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            if (!state.HasVessel) return;

            if (_lastCrewText.Update(state.CrewStr))
            {
                _crewText.text = state.CrewStr;
            }

            if (_lastPressureText.Update(state.PresStr))
            {
                _pressureText.text = state.PresStr;
            }

            if (_lastTempText.Update(state.TempStr))
            {
                _tempText.text = state.TempStr;
            }

            for (int i = 0; i < 4; i++)
            {
                var gSnap = state.GetGauge(i);
                ResourceGaugeUI g = _gauges[i];
                float clamped = Mathf.Clamp01(gSnap.Fraction);

                if (_lastGaugeFractions[i].Update(clamped))
                {
                    g.BarFill.SetSizeDeltaSafe(new Vector2(g.BarWidth * clamped, g.BarFill.sizeDelta.y));
                }

                if (_lastGaugeValues[i].Update(gSnap.ValueStr))
                {
                    g.PercentText.text = gSnap.ValueStr;
                }

                if (_lastGaugeStatuses[i].Update(gSnap.StatusStr))
                {
                    g.StatusText.text = gSnap.StatusStr;
                }
            }

            if (_lastStatusBadgeState.Update(state.BadgeState))
            {
                if (state.BadgeState == 2)
                {
                    StatusBadge.Text = "▲ " + I18n.Tr("WIDGET_LIFE_WARNING", "警告");
                    StatusBadge.SetRole(TextStyleRole.Danger);
                }
                else if (state.BadgeState == 1)
                {
                    StatusBadge.Text = "● " + I18n.Tr("WIDGET_LIFE_CAUTION", "注意");
                    StatusBadge.SetRole(TextStyleRole.Warning);
                }
                else
                {
                    StatusBadge.Text = "● " + I18n.Tr("WIDGET_LIFE_NOMINAL", "正常");
                    StatusBadge.SetRole(TextStyleRole.Accent);
                }
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastCrewText.Reset(string.Empty);
            _lastPressureText.Reset(string.Empty);
            _lastTempText.Reset(string.Empty);
            _lastStatusBadgeState.Reset(-1);
            for (int i = 0; i < 4; i++)
            {
                _lastGaugeFractions[i].Reset(-1f);
                _lastGaugeValues[i].Reset(string.Empty);
                _lastGaugeStatuses[i].Reset(string.Empty);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            _logic.Slot0Token = GetTemplateChannel("SLOT0_TOKEN", "{O2}");
            _logic.Slot1Token = GetTemplateChannel("SLOT1_TOKEN", "{EC:PCT}");
            _logic.Slot2Token = GetTemplateChannel("SLOT2_TOKEN", "{MONO}");
            _logic.Slot3Token = GetTemplateChannel("SLOT3_TOKEN", "{WATER}");
            _logic.CrewTemplate = GetTemplateChannel("CREW_TPL", "{CREW}");
            _logic.AtmTemplate = GetTemplateChannel("ATM_TPL", "{ATM}");
            _logic.TempTemplate = GetTemplateChannel("TEMP_TPL", "{TEMP}");

            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", I18n.Tr("WIDGET_LIFE_HABITAT_CREW", "居住舱与乘员"));
                ApplyText(_subTitleText, TextStyleRole.Label, theme);
            }
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
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
