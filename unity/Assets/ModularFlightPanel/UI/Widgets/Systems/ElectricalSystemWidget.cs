using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 航电电气分配与供电系统状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct ElectricalSystemState : IEquatable<ElectricalSystemState>
    {
        public bool HasVessel;
        public string Bat1Val;
        public string Bat1Sub;
        public TextStyleRole Bat1SubRole;
        public string Bat2Val;
        public string Bat2Sub;
        public TextStyleRole Bat2SubRole;
        public string DcVal;
        public string DcSub;
        public string GenVal;
        public string GenSub;
        public TextStyleRole GenRole;
        public string LoadVal;
        public string LoadSub;
        public TextStyleRole LoadRole;

        public bool Equals(ElectricalSystemState other)
        {
            return HasVessel == other.HasVessel &&
                   Bat1Val == other.Bat1Val &&
                   Bat1Sub == other.Bat1Sub &&
                   Bat1SubRole == other.Bat1SubRole &&
                   Bat2Val == other.Bat2Val &&
                   Bat2Sub == other.Bat2Sub &&
                   Bat2SubRole == other.Bat2SubRole &&
                   DcVal == other.DcVal &&
                   DcSub == other.DcSub &&
                   GenVal == other.GenVal &&
                   GenSub == other.GenSub &&
                   GenRole == other.GenRole &&
                   LoadVal == other.LoadVal &&
                   LoadSub == other.LoadSub &&
                   LoadRole == other.LoadRole;
        }

        public override bool Equals(object obj) => obj is ElectricalSystemState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ (Bat1Val != null ? Bat1Val.GetHashCode() : 0);
                hash = (hash * 397) ^ (DcVal != null ? DcVal.GetHashCode() : 0);
                hash = (hash * 397) ^ (LoadVal != null ? LoadVal.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电电气分配与供电系统纯业务逻辑大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class ElectricalSystemLogic : WidgetLogic<ElectricalSystemState>
    {
        public string Bat1Template { get; set; } = "{VOLT}";
        public string DcBusValTemplate { get; set; } = "{EC:PCT}%";
        public string DcBusSubTemplate { get; set; } = "{EC}/{EC:MAX} EC";

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

            double currentEc = TelemetryTokenEngine.EvaluateNumeric("{EC}", telemetry);
            double netRate = TelemetryTokenEngine.EvaluateNumeric("{EC:RATE}", telemetry);
            float busVoltage = (float)TelemetryTokenEngine.EvaluateNumeric("{VOLT}", telemetry);
            if (float.IsNaN(busVoltage)) busVoltage = 28.0f;
            int solarActiveCount = (int)TelemetryTokenEngine.EvaluateNumeric("{SOLAR:ACTIVE}", telemetry);

            // BAT 1 & BAT 2
            string bat1Val = TelemetryTokenEngine.Evaluate(Bat1Template ?? "{VOLT}", telemetry);
            string bat1Sub = currentEc > 1.0 ? I18n.Tr("WIDGET_ELEC_BATT_ONLINE", "在线") : I18n.Tr("WIDGET_ELEC_BATT_DEPLETED", "耗尽");
            TextStyleRole bat1SubRole = currentEc > 1.0 ? TextStyleRole.Accent : TextStyleRole.Warning;

            string bat2Val = $"{busVoltage * 0.995f:F1} V";
            string bat2Sub = currentEc > 1.0 ? "STANDBY" : "OFFLINE";
            TextStyleRole bat2SubRole = currentEc > 1.0 ? TextStyleRole.Label : TextStyleRole.Warning;

            // DC ESS BUS
            string dcVal = TelemetryTokenEngine.Evaluate(DcBusValTemplate ?? "{EC:PCT}%", telemetry);
            string dcSub = TelemetryTokenEngine.Evaluate(DcBusSubTemplate ?? "{EC}/{EC:MAX} EC", telemetry);

            // POWER SOURCES
            string genVal = solarActiveCount > 0 ? TelemetryTokenEngine.Evaluate("+{SOLAR}", telemetry) : I18n.Tr("WIDGET_ELEC_NO_SOLAR", "无太阳能");
            string genSub = solarActiveCount > 0 ? I18n.TrFormat("WIDGET_ELEC_SOLAR_ACTIVE", TelemetryTokenEngine.Evaluate("{SOLAR:ACTIVE}", telemetry)) : I18n.Tr("WIDGET_ELEC_BATTERY_ONLY", "仅电池");
            TextStyleRole genRole = solarActiveCount > 0 ? TextStyleRole.Accent : TextStyleRole.Label;

            // LOAD & FLOW
            string loadVal;
            string loadSub;
            TextStyleRole loadRole;
            if (Math.Abs(netRate) < 0.01)
            {
                loadVal = "0.00 e/s";
                loadSub = "BALANCED";
                loadRole = TextStyleRole.PrimaryValue;
            }
            else if (netRate > 0)
            {
                loadVal = TelemetryTokenEngine.Evaluate("+{EC:RATE}", telemetry);
                loadSub = "CHARGING";
                loadRole = TextStyleRole.Accent;
            }
            else
            {
                loadVal = TelemetryTokenEngine.Evaluate("{EC:RATE}", telemetry);
                loadSub = "DRAINING";
                loadRole = TextStyleRole.Warning;
            }

            CurrentState = new ElectricalSystemState
            {
                HasVessel = true,
                Bat1Val = bat1Val,
                Bat1Sub = bat1Sub,
                Bat1SubRole = bat1SubRole,
                Bat2Val = bat2Val,
                Bat2Sub = bat2Sub,
                Bat2SubRole = bat2SubRole,
                DcVal = dcVal,
                DcSub = dcSub,
                GenVal = genVal,
                GenSub = genSub,
                GenRole = genRole,
                LoadVal = loadVal,
                LoadSub = loadSub,
                LoadRole = loadRole
            };
        }
    }

    /// <summary>
    /// 原生 UGUI 航电电气系统监控面板 (ELEC Power Distribution)
    /// 监控飞船蓄电池组、直流总线母线、太阳能/发电机电源供给与即时净充放电率
    /// </summary>
    [FlightWidget("electrical", "elec", "power_grid", Category = WidgetCategory.Systems, DisplayName = "ELEC 电力分配与电网系统", Description = "蓄电池电压、DC ESS 总线负荷、太阳能帆板与即时净充放电率 (EC/s)。", DefaultWidgetId = "custom.electrical", DefaultX = -440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "custom.electrical", "custom.elec", "core.electrical" })]
    public class ElectricalSystemWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(280f, 155f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private readonly ElectricalSystemLogic _logic = new ElectricalSystemLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // 声明式微控件头部与状态徽标
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_ELEC_TITLE", "电源系统"));
        public TextWidget StatusBadge = TextWidget.Badge("● " + I18n.Tr("WIDGET_PERF_LIVE", "实时"));

        private Text _subTitleText;

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
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = BaseSize * s;

            // 1. 顶部 Header (Title & StatusBadge 已由基类微控件 DSL 自动构建)
            _subTitleText = UIFactory.CreateText(transform, "SubTitle", I18n.Tr("WIDGET_ELEC_POWER_DIST", "配电"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(120f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(-15f * s, 62f * s);

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
            Text tipText = UIFactory.CreateText(transform, "FooterTip", I18n.Tr("WIDGET_ELEC_BUS_TIP", "28V 直流母线系统  ·  主航电"),
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform tipRt = tipText.GetComponent<RectTransform>();
            tipRt.sizeDelta = new Vector2(panelSize.x - 20f * s, 12f * s);
            tipRt.anchoredPosition = new Vector2(0f, -64f * s);

            // 注册节点微控件至标准化管理器
            this.Controls.Register(new WidgetReadoutControl("battery_nodes", "蓄电池组", _bat1ValText != null ? _bat1ValText.gameObject : null, _bat1ValText, _bat2ValText, TextStyleRole.PrimaryValue, "{VOLT}"));
            this.Controls.Register(new WidgetReadoutControl("dc_bus", "直流总线母线", _dcBusValText != null ? _dcBusValText.gameObject : null, _dcBusValText, _dcBusSubText, TextStyleRole.PrimaryValue, "{EC:PCT}"));
            this.Controls.Register(new WidgetReadoutControl("generation_load", "发电与负载监控", _genValText != null ? _genValText.gameObject : null, _genValText, _loadValText, TextStyleRole.PrimaryValue, "{SOLAR}"));

            _logic.Bat1Template = GetTemplateChannel("BAT1_VAL", "{VOLT}");
            _logic.DcBusValTemplate = GetTemplateChannel("DCBUS_VAL", "{EC:PCT}%");
            _logic.DcBusSubTemplate = GetTemplateChannel("DCBUS_SUB", "{EC}/{EC:MAX} EC");
        }

        private void CreateNodeBox(Transform parent, string name, Vector2 size, Vector2 pos, string nodeTitle,
            Color accentColor, out Text valText, out Text subText)
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            Color borderCol = theme.FrameBorderColor;
            Color cellBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);
            Color faintBorder = WidgetStyleManager.Weighted(borderCol, LineWeight.Faint);
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
                TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            RectTransform valRt = valText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(size.x - 4f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(0f, (size.y * 0.5f) - 18f * s);

            subText = UIFactory.CreateText(box.transform, "Sub", "---", Mathf.RoundToInt(7f * s),
                TextAnchor.LowerCenter, accentColor);
            RectTransform subRt = subText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(size.x - 4f * s, 11f * s);
            subRt.anchoredPosition = new Vector2(0f, -(size.y * 0.5f) + 6f * s);
        }

        private readonly Cached<string> _lastBat1Val = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBat1Sub = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBat2Val = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBat2Sub = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastDcBusVal = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastDcBusSub = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGenVal = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastGenSub = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastLoadVal = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastLoadSub = new Cached<string>(string.Empty);

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

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;

            if (_lastBat1Val.Update(state.Bat1Val))
            {
                _bat1ValText.text = state.Bat1Val;
            }
            if (_lastBat1Sub.Update(state.Bat1Sub))
            {
                _bat1SubText.text = state.Bat1Sub;
                ApplyText(_bat1SubText, state.Bat1SubRole, theme);
            }

            if (_lastBat2Val.Update(state.Bat2Val))
            {
                _bat2ValText.text = state.Bat2Val;
            }
            if (_lastBat2Sub.Update(state.Bat2Sub))
            {
                _bat2SubText.text = state.Bat2Sub;
                ApplyText(_bat2SubText, state.Bat2SubRole, theme);
            }

            if (_lastDcBusVal.Update(state.DcVal))
            {
                _dcBusValText.text = state.DcVal;
            }
            if (_lastDcBusSub.Update(state.DcSub))
            {
                _dcBusSubText.text = state.DcSub;
            }

            if (_lastGenVal.Update(state.GenVal))
            {
                _genValText.text = state.GenVal;
                ApplyText(_genValText, state.GenRole, theme);
            }
            if (_lastGenSub.Update(state.GenSub))
            {
                _genSubText.text = state.GenSub;
            }

            if (_lastLoadVal.Update(state.LoadVal))
            {
                _loadValText.text = state.LoadVal;
                ApplyText(_loadValText, state.LoadRole, theme);
            }
            if (_lastLoadSub.Update(state.LoadSub))
            {
                _loadSubText.text = state.LoadSub;
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastBat1Val.Reset(string.Empty);
            _lastBat1Sub.Reset(string.Empty);
            _lastBat2Val.Reset(string.Empty);
            _lastBat2Sub.Reset(string.Empty);
            _lastDcBusVal.Reset(string.Empty);
            _lastDcBusSub.Reset(string.Empty);
            _lastGenVal.Reset(string.Empty);
            _lastGenSub.Reset(string.Empty);
            _lastLoadVal.Reset(string.Empty);
            _lastLoadSub.Reset(string.Empty);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            _logic.Bat1Template = GetTemplateChannel("BAT1_VAL", "{VOLT}");
            _logic.DcBusValTemplate = GetTemplateChannel("DCBUS_VAL", "{EC:PCT}%");
            _logic.DcBusSubTemplate = GetTemplateChannel("DCBUS_SUB", "{EC}/{EC:MAX} EC");

            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", I18n.Tr("WIDGET_ELEC_POWER_DIST", "配电"));
                ApplyText(_subTitleText, TextStyleRole.Label, theme);
            }

            if (_bat1ValText != null) ApplyText(_bat1ValText, TextStyleRole.PrimaryValue, theme);
            if (_bat1SubText != null) ApplyText(_bat1SubText, TextStyleRole.Label, theme);
            if (_bat2ValText != null) ApplyText(_bat2ValText, TextStyleRole.PrimaryValue, theme);
            if (_bat2SubText != null) ApplyText(_bat2SubText, TextStyleRole.Label, theme);
            if (_dcBusValText != null) ApplyText(_dcBusValText, TextStyleRole.PrimaryValue, theme);
            if (_dcBusSubText != null) ApplyText(_dcBusSubText, TextStyleRole.Label, theme);
            if (_genValText != null) ApplyText(_genValText, TextStyleRole.PrimaryValue, theme);
            if (_genSubText != null) ApplyText(_genSubText, TextStyleRole.Label, theme);
            if (_loadValText != null) ApplyText(_loadValText, TextStyleRole.PrimaryValue, theme);
            if (_loadSubText != null) ApplyText(_loadSubText, TextStyleRole.Label, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
