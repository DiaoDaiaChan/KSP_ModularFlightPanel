using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 综合工况与 ECLSS 零-GC遥测快照 (MFP-SPEC-012)
    /// </summary>
    public struct SpaceXOverviewState : IEquatable<SpaceXOverviewState>
    {
        public bool HasVessel;
        public float Press;
        public float O2;
        public float Temp;
        public float Ec;
        public double Volt;
        public float Prop;
        public bool HasWarn;

        public string PressStr;
        public string O2Str;
        public string TempStr;
        public string PwrStr;
        public string PropStr;
        public string AirlockStr;
        public string ThermalStr;
        public string DockStr;
        public string StatusBadge;

        public bool Equals(SpaceXOverviewState other)
        {
            return HasVessel == other.HasVessel &&
                   HasWarn == other.HasWarn &&
                   Math.Abs(Press - other.Press) < 0.1f &&
                   Math.Abs(O2 - other.O2) < 0.5f &&
                   Math.Abs(Temp - other.Temp) < 0.2f &&
                   Math.Abs(Ec - other.Ec) < 0.5f &&
                   Math.Abs(Prop - other.Prop) < 0.5f &&
                   string.Equals(StatusBadge, other.StatusBadge, StringComparison.Ordinal) &&
                   string.Equals(AirlockStr, other.AirlockStr, StringComparison.Ordinal) &&
                   string.Equals(ThermalStr, other.ThermalStr, StringComparison.Ordinal) &&
                   string.Equals(DockStr, other.DockStr, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is SpaceXOverviewState other && Equals(other);
        public override int GetHashCode() => (HasVessel, HasWarn, Press, O2, Temp, Ec).GetHashCode();
    }

    /// <summary>
    /// SpaceX 综合工况与 ECLSS 业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class SpaceXOverviewLogic : WidgetLogic<SpaceXOverviewState>
    {
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

            // 1. 客舱气压 (Cabin Press)
            float press = (float)telemetry.CabinPressure;
            if (press <= 0.01f)
            {
                if (telemetry.AtmosphericPressure > 0.01)
                    press = (float)(telemetry.AtmosphericPressure * 101.325);
                else
                    press = 101.3f;
            }

            // 2. 氧气百分比 (O2)
            float o2 = telemetry.OxygenPercent;

            // 3. 客舱温度 (Cabin Temp)
            float temp = (float)telemetry.CabinTemp;

            // 4. 电力与总线电压 (Net Power)
            float ec = (float)telemetry.EcPercent;
            double volt = telemetry.BusVoltage;

            // 5. 推进剂储备
            float prop = telemetry.MonoPercent > 0.001f ? telemetry.MonoPercent : (telemetry.StagePropellantFraction * 100f);

            // 6. 四大子系统遥测状态动态解算
            string airlockState = telemetry.CrewCapacity == 0
                ? "UNCREWED"
                : (telemetry.AtmosphericPressure < 0.01 ? "SEALED / 1 ATM" : "EQUALIZED");

            string thermalState;
            if (telemetry.DynamicPressure > 20.0 || telemetry.VerticalSpeed < -100.0)
                thermalState = "REENTRY / HIGH AERO";
            else if (telemetry.CabinTemp > 35.0)
                thermalState = "ACTIVE COOLING HI";
            else if (telemetry.CabinTemp < 5.0)
                thermalState = "HEATERS ENGAGED";
            else
                thermalState = "LOOP NOMINAL [21°C]";

            string dockState;
            if (telemetry.IsDockingMode)
            {
                if (telemetry.HasTarget && telemetry.TargetDistance < 15.0) dockState = "CAPTURE / NEAR";
                else if (telemetry.HasTarget) dockState = "APPROACH / ARMED";
                else dockState = "DOCKING MODE";
            }
            else
            {
                dockState = "STANDBY / LATCHED";
            }

            // 7. 总体警告判定
            bool hasWarn = (telemetry.CrewCapacity > 0 && o2 < 20f) || ec < 15f || (telemetry.CrewCapacity > 0 && press < 30f);
            string badge = hasWarn ? "WARN" : "NOMINAL";

            CurrentState = new SpaceXOverviewState
            {
                HasVessel = true,
                Press = press,
                O2 = o2,
                Temp = temp,
                Ec = ec,
                Volt = volt,
                Prop = prop,
                HasWarn = hasWarn,
                PressStr = $"{press:F1} kPa",
                O2Str = $"{o2:F0}%",
                TempStr = $"{temp:F1}°C",
                PwrStr = $"{volt:F1}V / {ec:F0}%",
                PropStr = $"{prop:F0}%",
                AirlockStr = airlockState,
                ThermalStr = thermalState,
                DockStr = dockState,
                StatusBadge = badge
            };
        }
    }

    /// <summary>
    /// SpaceX 载人龙飞船综合工况与 ECLSS 环控维生监控面板 (SpaceX Vehicle Overview & ECLSS Panel)
    /// 包含：客舱气压 (Cabin Press)、氧分压 (O2 Level)、客舱温度 (Cabin Temp)、电网总线 (Net Power)，
    /// 以及气闸、推进剂、热控回路与机械对接口的 4 路状态矩阵。
    /// 严格遵循 MFP 规范，0 颜色字面量，10Hz Relaxed 阶梯刷新。
    /// </summary>
    [FlightWidget("spacex_overview", "dragon_overview", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 综合工况与 ECLSS 面板", Description = "飞船综合工况与维生监控：客舱压力、氧分压、客舱温度、电网功率与气闸/推进剂/热控状态。", DefaultWidgetId = "spacex.overview", DefaultX = -460f, DefaultY = 120f, IsSingleton = true, ExactIds = new[] { "spacex.overview" })]
    public class SpaceXOverviewWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(240f, 180f);
        protected override bool AutoCreateCardFrame => true;
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

        // 脏数据变动缓存 (MFP-SPEC-009)
        private readonly CachedFloat _lastPress = new CachedFloat(-1f);
        private readonly CachedFloat _lastO2 = new CachedFloat(-1f);
        private readonly CachedFloat _lastTemp = new CachedFloat(-1f);
        private readonly CachedFloat _lastEc = new CachedFloat(-1f);
        private readonly CachedFloat _lastProp = new CachedFloat(-1f);
        private readonly Cached<string> _lastStatusBadge = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPressStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastO2Str = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastTempStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPwrStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPropStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastAirlockStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastThermalStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastDockStr = new Cached<string>(string.Empty);

        // 业务大脑 (MFP-SPEC-012)
        private readonly SpaceXOverviewLogic _logic = new SpaceXOverviewLogic();
        protected override IWidgetLogic LogicCore => _logic;

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

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _titleCustom = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SPX_VEHICLE_OVERVIEW", "飞行器总览 / ECLSS"));
            _pressLabelStr = GetTemplateChannel("PRESS_LABEL", I18n.Tr("WIDGET_SPX_CABIN_PRESS", "客舱气压"));
            _o2LabelStr = GetTemplateChannel("O2_LABEL", I18n.Tr("WIDGET_SPX_O2_LEVEL", "氧分压"));
            _tempLabelStr = GetTemplateChannel("TEMP_LABEL", I18n.Tr("WIDGET_SPX_CABIN_TEMP", "客舱温度"));
            _pwrLabelStr = GetTemplateChannel("PWR_LABEL", I18n.Tr("WIDGET_SPX_NET_POWER", "电网功率"));
            _airlockLabelStr = GetTemplateChannel("AIRLOCK_LABEL", I18n.Tr("WIDGET_SPX_AIRLOCK_HATCH", "气闸舱门"));
            _propLabelStr = GetTemplateChannel("PROP_LABEL", I18n.Tr("WIDGET_SPX_DRACO_RCS_PROP", "RCS 推进剂"));
            _thermalLabelStr = GetTemplateChannel("THERMAL_LABEL", I18n.Tr("WIDGET_SPX_ACTIVE_THERMAL_LOOP", "主动热控回路"));
            _dockLabelStr = GetTemplateChannel("DOCK_LABEL", I18n.Tr("WIDGET_SPX_DOCKING_MECHANISM", "对接机构"));

            Vector2 panelSize = new Vector2(240f * s, 180f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = CardBackground;
            _outline = CardOutline;
            if (_outline != null)
                _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 1. 顶部标题与工况微标
            _titleText = UIFactory.CreateText(transform, "Title", _titleCustom, Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.rectTransform.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 14f * s + 75f * s, panelSize.y * 0.5f - 14f * s);
            _titleText.rectTransform.sizeDelta = new Vector2(150f * s, 14f * s);

            _statusBadge = UIFactory.CreateText(transform, "StatusBadge", "NOMINAL", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.Accent, theme));
            _statusBadge.fontStyle = FontStyle.Bold;
            _statusBadge.rectTransform.anchoredPosition = new Vector2(panelSize.x * 0.5f - 14f * s - 30f * s, panelSize.y * 0.5f - 14f * s);
            _statusBadge.rectTransform.sizeDelta = new Vector2(60f * s, 14f * s);

            // 2. 四组 ECLSS 监控卡槽 (横向 2x2 网格，每格配微型进度条)
            float row1Y = panelSize.y * 0.5f - 36f * s;
            float row2Y = row1Y - 32f * s;
            float col1X = -panelSize.x * 0.5f + 14f * s;
            float col2X = 8f * s;

            CreateEclssSlot("Slot_Press", col1X, row1Y, s, theme, _pressLabelStr, out _pressLabel, out _pressValue, out _pressTrack, out _pressFill);
            CreateEclssSlot("Slot_O2", col2X, row1Y, s, theme, _o2LabelStr, out _o2Label, out _o2Value, out _o2Track, out _o2Fill);
            CreateEclssSlot("Slot_Temp", col1X, row2Y, s, theme, _tempLabelStr, out _tempLabel, out _tempValue, out _tempTrack, out _tempFill);
            CreateEclssSlot("Slot_Power", col2X, row2Y, s, theme, _pwrLabelStr, out _pwrLabel, out _pwrValue, out _pwrTrack, out _pwrFill);

            // 水平分隔线
            float sepY = row2Y - 20f * s;
            UIFactory.CreatePanel(transform, "MidSep", new Vector2(panelSize.x - 28f * s, 1f * s), new Vector2(0f, sepY), style.GetLineColor(LineWeight.Faint, theme));

            // 3. 底部四大子系统状态矩阵 (4 行紧凑状态标签)
            float subY = sepY - 14f * s;
            float lineH = 13f * s;

            _subsysAirlock = CreateSubsystemRow("Subsys_Airlock", subY, s, theme, _airlockLabelStr, "SEALED / 1 ATM");
            subY -= lineH;
            _subsysProp = CreateSubsystemRow("Subsys_Prop", subY, s, theme, _propLabelStr, "100%");
            subY -= lineH;
            _subsysThermal = CreateSubsystemRow("Subsys_Thermal", subY, s, theme, _thermalLabelStr, "LOOP NOMINAL [21°C]");
            subY -= lineH;
            _subsysDock = CreateSubsystemRow("Subsys_Dock", subY, s, theme, _dockLabelStr, "STANDBY / LATCHED");

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Overview Background", _bgImage.gameObject, "SpaceX综合工况卡片底板", t => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "title", "Title", _titleText != null ? _titleText.gameObject : null));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "status_badge", "健康状态徽标", _statusBadge != null ? _statusBadge.gameObject : null));

            this.Controls.Register(new WidgetReadoutControl("cabin_press", "客舱压力读数", _pressValue != null ? _pressValue.gameObject : null, _pressValue, _pressLabel, TextStyleRole.PrimaryValue, "{CABIN:PRESS}"));
            this.Controls.Register(new WidgetReadoutControl("o2_level", "氧分压读数", _o2Value != null ? _o2Value.gameObject : null, _o2Value, _o2Label, TextStyleRole.PrimaryValue, "{CABIN:O2}"));
            this.Controls.Register(new WidgetReadoutControl("cabin_temp", "客舱温度读数", _tempValue != null ? _tempValue.gameObject : null, _tempValue, _tempLabel, TextStyleRole.PrimaryValue, "{CABIN:TEMP}"));
            this.Controls.Register(new WidgetReadoutControl("net_power", "电网功率读数", _pwrValue != null ? _pwrValue.gameObject : null, _pwrValue, _pwrLabel, TextStyleRole.PrimaryValue, "{ELEC}"));

            this.Controls.Register(WidgetControlManager.WrapElement(this, "subsys_airlock", "气闸舱门状态", _subsysAirlock != null ? _subsysAirlock.gameObject : null));
            this.Controls.Register(new WidgetReadoutControl("subsys_prop", "推进剂状态", _subsysProp != null ? _subsysProp.gameObject : null, _subsysProp, null, TextStyleRole.SecondaryValue, "{MONO}"));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "subsys_thermal", "热控回路状态", _subsysThermal != null ? _subsysThermal.gameObject : null));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "subsys_dock", "对接机构状态", _subsysDock != null ? _subsysDock.gameObject : null));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private Text CreateSubsystemRow(string name, float y, float s, ThemeConfig theme, string label, string defaultVal)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float w = 240f * s - 28f * s;
            float startX = -w * 0.5f;

            Text l = UIFactory.CreateText(transform, $"{name}_Label", label, Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Muted, theme));
            l.rectTransform.anchoredPosition = new Vector2(startX + 60f * s, y);
            l.rectTransform.sizeDelta = new Vector2(120f * s, 12f * s);

            Text v = UIFactory.CreateText(transform, $"{name}_Val", defaultVal, Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            v.fontStyle = FontStyle.Bold;
            v.rectTransform.anchoredPosition = new Vector2(startX + w - 45f * s, y);
            v.rectTransform.sizeDelta = new Vector2(90f * s, 12f * s);

            return v;
        }

        private void CreateEclssSlot(string name, float startX, float startY, float s, ThemeConfig theme, string labelStr,
            out Text label, out Text val, out Image track, out Image fill)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float slotW = 100f * s;

            label = UIFactory.CreateText(transform, $"{name}_Label", labelStr, Mathf.RoundToInt(6.5f * s), TextAnchor.UpperLeft,
                style.GetTextColor(TextStyleRole.Muted, theme));
            label.rectTransform.anchoredPosition = new Vector2(startX + slotW * 0.5f, startY);
            label.rectTransform.sizeDelta = new Vector2(slotW, 10f * s);

            val = UIFactory.CreateText(transform, $"{name}_Val", "---", Mathf.RoundToInt(10.5f * s), TextAnchor.LowerLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            val.fontStyle = FontStyle.Bold;
            val.rectTransform.anchoredPosition = new Vector2(startX + slotW * 0.5f, startY - 11f * s);
            val.rectTransform.sizeDelta = new Vector2(slotW, 14f * s);

            GameObject trkObj = UIFactory.CreatePanel(transform, $"{name}_Track", new Vector2(slotW, 3f * s),
                new Vector2(startX + slotW * 0.5f, startY - 22f * s), style.GetMeterColor(MeterStyleRole.Track, theme));
            track = trkObj.GetComponent<Image>();

            GameObject fillObj = UIFactory.CreatePanel(trkObj.transform, $"{name}_Fill", new Vector2(slotW * 0.5f, 3f * s),
                new Vector2(0f, 0f), style.GetMeterColor(MeterStyleRole.Primary, theme));
            fill = fillObj.GetComponent<Image>();
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = new Vector2(-slotW * 0.5f, 0f);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            SpaceXOverviewState state = _logic.CurrentState;
            if (!state.HasVessel) return;

            float s = CurrentDpiScale;

            // 1. 舱压
            if (Mathf.Abs(state.Press - _lastPress.Value) > 0.1f)
            {
                _lastPress.Update(state.Press);
                if (_lastPressStr.Update(state.PressStr) && _pressValue != null)
                {
                    _pressValue.SetTextSafe(state.PressStr);
                }
                if (_pressFill != null)
                {
                    float ratio = Mathf.Clamp01(state.Press / 105f);
                    _pressFill.rectTransform.SetSizeDeltaSafe(new Vector2(100f * s * ratio, 3f * s));
                }
            }

            // 2. 氧气百分比
            if (Mathf.Abs(state.O2 - _lastO2.Value) > 0.5f)
            {
                _lastO2.Update(state.O2);
                if (_lastO2Str.Update(state.O2Str) && _o2Value != null)
                {
                    _o2Value.SetTextSafe(state.O2Str);
                }
                if (_o2Fill != null)
                {
                    float ratio = Mathf.Clamp01(state.O2 * 0.01f);
                    _o2Fill.rectTransform.SetSizeDeltaSafe(new Vector2(100f * s * ratio, 3f * s));
                }
            }

            // 3. 客舱温度
            if (Mathf.Abs(state.Temp - _lastTemp.Value) > 0.2f)
            {
                _lastTemp.Update(state.Temp);
                if (_lastTempStr.Update(state.TempStr) && _tempValue != null)
                {
                    _tempValue.SetTextSafe(state.TempStr);
                }
                if (_tempFill != null)
                {
                    float ratio = Mathf.Clamp01(state.Temp / 40f);
                    _tempFill.rectTransform.SetSizeDeltaSafe(new Vector2(100f * s * ratio, 3f * s));
                }
            }

            // 4. 电力与总线电压
            if (Mathf.Abs(state.Ec - _lastEc.Value) > 0.5f)
            {
                _lastEc.Update(state.Ec);
                if (_lastPwrStr.Update(state.PwrStr) && _pwrValue != null)
                {
                    _pwrValue.SetTextSafe(state.PwrStr);
                }
                if (_pwrFill != null)
                {
                    float ratio = Mathf.Clamp01(state.Ec * 0.01f);
                    _pwrFill.rectTransform.SetSizeDeltaSafe(new Vector2(100f * s * ratio, 3f * s));
                }
            }

            // 5. 推进剂储备
            if (Mathf.Abs(state.Prop - _lastProp.Value) > 0.5f)
            {
                _lastProp.Update(state.Prop);
                if (_lastPropStr.Update(state.PropStr) && _subsysProp != null)
                {
                    _subsysProp.SetTextSafe(state.PropStr);
                }
            }

            // 6. 四大子系统
            if (_subsysAirlock != null && _lastAirlockStr.Update(state.AirlockStr)) _subsysAirlock.SetTextSafe(state.AirlockStr);
            if (_subsysThermal != null && _lastThermalStr.Update(state.ThermalStr)) _subsysThermal.SetTextSafe(state.ThermalStr);
            if (_subsysDock != null && _lastDockStr.Update(state.DockStr)) _subsysDock.SetTextSafe(state.DockStr);

            // 7. 总体警告判定
            if (_lastStatusBadge.Update(state.StatusBadge) && _statusBadge != null)
            {
                _statusBadge.SetTextSafe(state.StatusBadge);
                ThemeConfig th = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;
                ApplyText(_statusBadge, state.StatusBadge == "NOMINAL" ? TextStyleRole.Accent : TextStyleRole.Warning, th);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);

            ApplyText(_titleText, TextStyleRole.Label, theme);
            ApplyText(_statusBadge, _lastStatusBadge.Value == "WARN" ? TextStyleRole.Warning : TextStyleRole.Accent, theme);

            ApplyText(_pressLabel, TextStyleRole.Muted, theme);
            ApplyText(_pressValue, TextStyleRole.PrimaryValue, theme);
            if (_pressTrack != null) _pressTrack.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            if (_pressFill != null) _pressFill.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_o2Label, TextStyleRole.Muted, theme);
            ApplyText(_o2Value, TextStyleRole.PrimaryValue, theme);
            if (_o2Track != null) _o2Track.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            if (_o2Fill != null) _o2Fill.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_tempLabel, TextStyleRole.Muted, theme);
            ApplyText(_tempValue, TextStyleRole.PrimaryValue, theme);
            if (_tempTrack != null) _tempTrack.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            if (_tempFill != null) _tempFill.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_pwrLabel, TextStyleRole.Muted, theme);
            ApplyText(_pwrValue, TextStyleRole.PrimaryValue, theme);
            if (_pwrTrack != null) _pwrTrack.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            if (_pwrFill != null) _pwrFill.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_subsysAirlock, TextStyleRole.SecondaryValue, theme);
            ApplyText(_subsysProp, TextStyleRole.SecondaryValue, theme);
            ApplyText(_subsysThermal, TextStyleRole.SecondaryValue, theme);
            ApplyText(_subsysDock, TextStyleRole.SecondaryValue, theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
