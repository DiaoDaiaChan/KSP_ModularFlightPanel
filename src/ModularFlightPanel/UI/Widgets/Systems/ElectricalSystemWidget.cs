using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
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

        private string _lastBat1Val;
        private string _lastBat1Sub;
        private string _lastBat2Val;
        private string _lastBat2Sub;
        private string _lastDcBusVal;
        private string _lastDcBusSub;
        private string _lastGenVal;
        private string _lastGenSub;
        private string _lastLoadVal;
        private string _lastLoadSub;

        private string _dataBat1Val;
        private string _dataBat1Sub;
        private TextStyleRole _dataBat1SubRole;
        private string _dataBat2Val;
        private string _dataBat2Sub;
        private TextStyleRole _dataBat2SubRole;
        private string _dataDcVal;
        private string _dataDcSub;
        private string _dataGenVal;
        private string _dataGenSub;
        private TextStyleRole _dataGenRole;
        private string _dataLoadVal;
        private string _dataLoadSub;
        private TextStyleRole _dataLoadRole;
        private bool _dataHasVessel;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataHasVessel = false;
                return;
            }
            _dataHasVessel = true;

            double currentEc = TelemetryTokenEngine.EvaluateNumeric("{EC}", telemetry);
            double netRate = TelemetryTokenEngine.EvaluateNumeric("{EC:RATE}", telemetry);
            float busVoltage = (float)TelemetryTokenEngine.EvaluateNumeric("{VOLT}", telemetry);
            if (float.IsNaN(busVoltage)) busVoltage = 28.0f;
            int solarActiveCount = (int)TelemetryTokenEngine.EvaluateNumeric("{SOLAR:ACTIVE}", telemetry);

            // BAT 1 & BAT 2
            string bat1Template = GetTemplateChannel("BAT1_VAL", "{VOLT}");
            _dataBat1Val = TelemetryTokenEngine.Evaluate(bat1Template, telemetry);
            _dataBat1Sub = currentEc > 1.0 ? I18n.Tr("WIDGET_ELEC_BATT_ONLINE", "在线") : I18n.Tr("WIDGET_ELEC_BATT_DEPLETED", "耗尽");
            _dataBat1SubRole = currentEc > 1.0 ? TextStyleRole.Accent : TextStyleRole.Warning;

            _dataBat2Val = $"{busVoltage * 0.995f:F1} V";
            _dataBat2Sub = currentEc > 1.0 ? "STANDBY" : "OFFLINE";
            _dataBat2SubRole = currentEc > 1.0 ? TextStyleRole.Label : TextStyleRole.Warning;

            // DC ESS BUS
            string dcBusValTpl = GetTemplateChannel("DCBUS_VAL", "{EC:PCT}%");
            string dcBusSubTpl = GetTemplateChannel("DCBUS_SUB", "{EC}/{EC:MAX} EC");
            _dataDcVal = TelemetryTokenEngine.Evaluate(dcBusValTpl, telemetry);
            _dataDcSub = TelemetryTokenEngine.Evaluate(dcBusSubTpl, telemetry);

            // POWER SOURCES
            _dataGenVal = solarActiveCount > 0 ? TelemetryTokenEngine.Evaluate("+{SOLAR}", telemetry) : I18n.Tr("WIDGET_ELEC_NO_SOLAR", "无太阳能");
            _dataGenSub = solarActiveCount > 0 ? I18n.TrFormat("WIDGET_ELEC_SOLAR_ACTIVE", TelemetryTokenEngine.Evaluate("{SOLAR:ACTIVE}", telemetry)) : I18n.Tr("WIDGET_ELEC_BATTERY_ONLY", "仅电池");
            _dataGenRole = solarActiveCount > 0 ? TextStyleRole.Accent : TextStyleRole.Label;

            // LOAD & FLOW
            if (Math.Abs(netRate) < 0.01)
            {
                _dataLoadVal = "0.00 e/s";
                _dataLoadSub = "BALANCED";
                _dataLoadRole = TextStyleRole.PrimaryValue;
            }
            else if (netRate > 0)
            {
                _dataLoadVal = TelemetryTokenEngine.Evaluate("+{EC:RATE}", telemetry);
                _dataLoadSub = "CHARGING";
                _dataLoadRole = TextStyleRole.Accent;
            }
            else
            {
                _dataLoadVal = TelemetryTokenEngine.Evaluate("{EC:RATE}", telemetry);
                _dataLoadSub = "DRAINING";
                _dataLoadRole = TextStyleRole.Warning;
            }
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_dataHasVessel) return;

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;

            if (_dataBat1Val != _lastBat1Val)
            {
                _lastBat1Val = _dataBat1Val;
                _bat1ValText.text = _dataBat1Val;
            }
            if (_dataBat1Sub != _lastBat1Sub)
            {
                _lastBat1Sub = _dataBat1Sub;
                _bat1SubText.text = _dataBat1Sub;
                ApplyText(_bat1SubText, _dataBat1SubRole, theme);
            }

            if (_dataBat2Val != _lastBat2Val)
            {
                _lastBat2Val = _dataBat2Val;
                _bat2ValText.text = _dataBat2Val;
            }
            if (_dataBat2Sub != _lastBat2Sub)
            {
                _lastBat2Sub = _dataBat2Sub;
                _bat2SubText.text = _dataBat2Sub;
                ApplyText(_bat2SubText, _dataBat2SubRole, theme);
            }

            if (_dataDcVal != _lastDcBusVal)
            {
                _lastDcBusVal = _dataDcVal;
                _dcBusValText.text = _dataDcVal;
            }
            if (_dataDcSub != _lastDcBusSub)
            {
                _lastDcBusSub = _dataDcSub;
                _dcBusSubText.text = _dataDcSub;
            }

            if (_dataGenVal != _lastGenVal)
            {
                _lastGenVal = _dataGenVal;
                _genValText.text = _dataGenVal;
                ApplyText(_genValText, _dataGenRole, theme);
            }
            if (_dataGenSub != _lastGenSub)
            {
                _lastGenSub = _dataGenSub;
                _genSubText.text = _dataGenSub;
            }

            if (_dataLoadVal != _lastLoadVal)
            {
                _lastLoadVal = _dataLoadVal;
                _loadValText.text = _dataLoadVal;
                ApplyText(_loadValText, _dataLoadRole, theme);
            }
            if (_dataLoadSub != _lastLoadSub)
            {
                _lastLoadSub = _dataLoadSub;
                _loadSubText.text = _dataLoadSub;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

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
