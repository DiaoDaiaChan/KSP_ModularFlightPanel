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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

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
            theme = WidgetStyleManager.ResolveTheme(theme);
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

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            this.Controls.Register(new WidgetHeaderControl("header", "标题栏", _titleText != null ? _titleText.gameObject : null, _titleText, _statusText));
            this.Controls.Register(new WidgetReadoutControl("battery_nodes", "蓄电池组", _bat1ValText != null ? _bat1ValText.gameObject : null, _bat1ValText, _bat2ValText, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetReadoutControl("dc_bus", "直流总线母线", _dcBusValText != null ? _dcBusValText.gameObject : null, _dcBusValText, _dcBusSubText, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetReadoutControl("generation_load", "发电与负载监控", _genValText != null ? _genValText.gameObject : null, _genValText, _loadValText, TextStyleRole.PrimaryValue));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            double currentEc = TelemetryTokenEngine.EvaluateNumeric("{EC}", telemetry);
            double netRate = TelemetryTokenEngine.EvaluateNumeric("{EC:RATE}", telemetry);
            float busVoltage = (float)TelemetryTokenEngine.EvaluateNumeric("{VOLT}", telemetry);
            if (float.IsNaN(busVoltage)) busVoltage = 28.0f;
            int solarActiveCount = (int)TelemetryTokenEngine.EvaluateNumeric("{SOLAR:ACTIVE}", telemetry);

            // BAT 1 & BAT 2
            string bat1Template = GetTemplateChannel("BAT1_VAL", "{VOLT}");
            string b1Val = TelemetryTokenEngine.Evaluate(bat1Template, telemetry);
            string b1Sub = currentEc > 1.0 ? "ONLINE" : "DEPLETED";
            if (b1Val != _lastBat1Val)
            {
                _lastBat1Val = b1Val;
                _bat1ValText.text = b1Val;
            }
            if (b1Sub != _lastBat1Sub)
            {
                _lastBat1Sub = b1Sub;
                _bat1SubText.text = b1Sub;
                ApplyText(_bat1SubText, currentEc > 1.0 ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
            }

            string b2Val = $"{busVoltage * 0.995f:F1} V";
            string b2Sub = currentEc > 1.0 ? "STANDBY" : "OFFLINE";
            if (b2Val != _lastBat2Val)
            {
                _lastBat2Val = b2Val;
                _bat2ValText.text = b2Val;
            }
            if (b2Sub != _lastBat2Sub)
            {
                _lastBat2Sub = b2Sub;
                _bat2SubText.text = b2Sub;
                ApplyText(_bat2SubText, currentEc > 1.0 ? TextStyleRole.Label : TextStyleRole.Warning, theme);
            }

            // DC ESS BUS
            string dcBusValTpl = GetTemplateChannel("DCBUS_VAL", "{EC:PCT}%");
            string dcBusSubTpl = GetTemplateChannel("DCBUS_SUB", "{EC}/{EC:MAX} EC");
            string dcVal = TelemetryTokenEngine.Evaluate(dcBusValTpl, telemetry);
            string dcSub = TelemetryTokenEngine.Evaluate(dcBusSubTpl, telemetry);
            if (dcVal != _lastDcBusVal)
            {
                _lastDcBusVal = dcVal;
                _dcBusValText.text = dcVal;
            }
            if (dcSub != _lastDcBusSub)
            {
                _lastDcBusSub = dcSub;
                _dcBusSubText.text = dcSub;
            }

            // POWER SOURCES
            string gVal = solarActiveCount > 0 ? TelemetryTokenEngine.Evaluate("+{SOLAR}", telemetry) : "NO SOLAR";
            string gSub = solarActiveCount > 0 ? TelemetryTokenEngine.Evaluate("SOLAR ({SOLAR:ACTIVE} ACTIVE)", telemetry) : "BATTERY ONLY";
            if (gVal != _lastGenVal)
            {
                _lastGenVal = gVal;
                _genValText.text = gVal;
                ApplyText(_genValText, solarActiveCount > 0 ? TextStyleRole.Accent : TextStyleRole.Label, theme);
            }
            if (gSub != _lastGenSub)
            {
                _lastGenSub = gSub;
                _genSubText.text = gSub;
            }

            // LOAD & FLOW
            string lVal;
            string lSub;
            TextStyleRole loadRole;
            if (Math.Abs(netRate) < 0.01)
            {
                lVal = "0.00 e/s";
                lSub = "BALANCED";
                loadRole = TextStyleRole.PrimaryValue;
            }
            else if (netRate > 0)
            {
                lVal = TelemetryTokenEngine.Evaluate("+{EC:RATE}", telemetry);
                lSub = "CHARGING";
                loadRole = TextStyleRole.Accent;
            }
            else
            {
                lVal = TelemetryTokenEngine.Evaluate("{EC:RATE}", telemetry);
                lSub = "DRAINING";
                loadRole = TextStyleRole.Warning;
            }

            if (lVal != _lastLoadVal)
            {
                _lastLoadVal = lVal;
                _loadValText.text = lVal;
                ApplyText(_loadValText, loadRole, theme);
            }
            if (lSub != _lastLoadSub)
            {
                _lastLoadSub = lSub;
                _loadSubText.text = lSub;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "ELEC");
                ApplyText(_titleText, TextStyleRole.PrimaryValue, theme);
            }
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "POWER DISTRIBUTION");
                ApplyText(_subTitleText, TextStyleRole.Label, theme);
            }
            if (_statusText != null) ApplyText(_statusText, TextStyleRole.Accent, theme);

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
