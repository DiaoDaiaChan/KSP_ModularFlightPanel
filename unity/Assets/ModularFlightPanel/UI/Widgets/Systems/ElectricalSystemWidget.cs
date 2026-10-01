using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 电网布局自适应模式 (根据全舰实际发电源与电池组数弹性扩充)
    /// </summary>
    public enum ElecGridMode : byte
    {
        Compact = 0,    // 单发电源 / 仅电池：基础高度 155f
        ExpandedDual,   // 双发电源 (如太阳能 + 燃料电池)：扩展高度 195f
        ExpandedMulti   // 多发电源 (太阳能 + RTG + 燃料电池/发电机)：全展开高度 235f
    }

    /// <summary>
    /// 蓄电池组排版拓扑
    /// </summary>
    public enum ElecBatteryLayout : byte
    {
        SingleBank = 0, // 单电池组或仅指令舱内置缓冲
        DualBank        // 多电池组独立分项
    }

    /// <summary>
    /// 航电电气分配与供电系统状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct ElectricalSystemState : IEquatable<ElectricalSystemState>
    {
        public bool HasVessel;
        public ElecGridMode GridMode;
        public ElecBatteryLayout BatteryLayout;
        public float TargetHeight;

        public string SubTitle;
        public string StatusBadge;
        public TextStyleRole StatusBadgeRole;

        // DC ESS BUS 主母线
        public string DcVal;
        public string DcSub;
        public TextStyleRole DcRole;

        // BAT 1
        public string Bat1Title;
        public string Bat1Val;
        public string Bat1Sub;
        public TextStyleRole Bat1SubRole;

        // BAT 2
        public bool ShowBat2;
        public string Bat2Title;
        public string Bat2Val;
        public string Bat2Sub;
        public TextStyleRole Bat2SubRole;

        // 发电源 1 (主电源: 太阳能 / RTG / 燃料电池)
        public string Source1Title;
        public string Source1Val;
        public string Source1Sub;
        public TextStyleRole Source1Role;

        // 发电源 2 (辅电源)
        public bool ShowSource2;
        public string Source2Title;
        public string Source2Val;
        public string Source2Sub;
        public TextStyleRole Source2Role;

        // 发电源 3 (扩展电源)
        public bool ShowSource3;
        public string Source3Title;
        public string Source3Val;
        public string Source3Sub;
        public TextStyleRole Source3Role;

        // 电网负载监控 (LOAD)
        public string LoadTitle;
        public string LoadVal;
        public string LoadSub;
        public TextStyleRole LoadRole;

        // 底部实时诊断提示
        public string FooterTip;
        public TextStyleRole FooterRole;

        public bool Equals(ElectricalSystemState other)
        {
            return HasVessel == other.HasVessel &&
                   GridMode == other.GridMode &&
                   BatteryLayout == other.BatteryLayout &&
                   Mathf.Abs(TargetHeight - other.TargetHeight) < 0.05f &&
                   SubTitle == other.SubTitle &&
                   StatusBadge == other.StatusBadge &&
                   StatusBadgeRole == other.StatusBadgeRole &&
                   DcVal == other.DcVal &&
                   DcSub == other.DcSub &&
                   DcRole == other.DcRole &&
                   Bat1Title == other.Bat1Title &&
                   Bat1Val == other.Bat1Val &&
                   Bat1Sub == other.Bat1Sub &&
                   Bat1SubRole == other.Bat1SubRole &&
                   ShowBat2 == other.ShowBat2 &&
                   Bat2Title == other.Bat2Title &&
                   Bat2Val == other.Bat2Val &&
                   Bat2Sub == other.Bat2Sub &&
                   Bat2SubRole == other.Bat2SubRole &&
                   Source1Title == other.Source1Title &&
                   Source1Val == other.Source1Val &&
                   Source1Sub == other.Source1Sub &&
                   Source1Role == other.Source1Role &&
                   ShowSource2 == other.ShowSource2 &&
                   Source2Title == other.Source2Title &&
                   Source2Val == other.Source2Val &&
                   Source2Sub == other.Source2Sub &&
                   Source2Role == other.Source2Role &&
                   ShowSource3 == other.ShowSource3 &&
                   Source3Title == other.Source3Title &&
                   Source3Val == other.Source3Val &&
                   Source3Sub == other.Source3Sub &&
                   Source3Role == other.Source3Role &&
                   LoadTitle == other.LoadTitle &&
                   LoadVal == other.LoadVal &&
                   LoadSub == other.LoadSub &&
                   LoadRole == other.LoadRole &&
                   FooterTip == other.FooterTip &&
                   FooterRole == other.FooterRole;
        }

        public override bool Equals(object obj) => obj is ElectricalSystemState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ (int)GridMode;
                hash = (hash * 397) ^ (int)BatteryLayout;
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

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0) return "---";
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 24) return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            double curEc = telemetry.ElectricCharge;
            double maxEc = telemetry.MaxElectricCharge;
            double ecPct = telemetry.EcPercent;
            double netRate = telemetry.NetEcRate;
            float busVoltage = telemetry.BusVoltage;

            // 1. 真实发电源探测
            bool hasSolar = telemetry.SolarPanelsTotal > 0;
            bool hasRtg = telemetry.RtgCount > 0;
            bool hasFuelCell = telemetry.FuelCellCount > 0;
            bool hasAlternator = telemetry.AlternatorCount > 0 && (telemetry.AlternatorPower > 0.001 || telemetry.TotalPowerGeneration > 0);

            int genSourcesCount = (hasSolar ? 1 : 0) + (hasRtg ? 1 : 0) + (hasFuelCell ? 1 : 0) + (hasAlternator ? 1 : 0);

            // 弹性布局高度决策
            ElecGridMode gridMode;
            float targetHeight;
            if (genSourcesCount <= 1)
            {
                gridMode = ElecGridMode.Compact;
                targetHeight = 155f;
            }
            else if (genSourcesCount == 2)
            {
                gridMode = ElecGridMode.ExpandedDual;
                targetHeight = 195f;
            }
            else
            {
                gridMode = ElecGridMode.ExpandedMulti;
                targetHeight = 235f;
            }

            // 2. 真实蓄电池组排版解算
            int batCount = telemetry.Batteries != null ? telemetry.Batteries.Count : 0;
            ElecBatteryLayout batLayout = batCount <= 1 ? ElecBatteryLayout.SingleBank : ElecBatteryLayout.DualBank;

            string bat1Title;
            string bat1Val;
            string bat1Sub;
            TextStyleRole bat1SubRole;

            bool showBat2 = batCount >= 2;
            string bat2Title = string.Empty;
            string bat2Val = string.Empty;
            string bat2Sub = string.Empty;
            TextStyleRole bat2SubRole = TextStyleRole.Label;

            if (batCount <= 1)
            {
                if (batCount == 1)
                {
                    var b = telemetry.Batteries[0];
                    bat1Title = b.IsDedicated ? I18n.Tr("WIDGET_ELEC_MAIN_BATT", "主电池") : I18n.Tr("WIDGET_ELEC_INTERNAL_BUFFER", "内置缓冲");
                    bat1Val = $"{b.Amount:F0}/{b.MaxAmount:F0} EC";
                    bat1Sub = b.IsFlowEnabled ? (b.Percent > 15f ? I18n.Tr("WIDGET_ELEC_BATT_ONLINE", "在线") : I18n.Tr("WIDGET_ELEC_BATT_DEPLETED", "耗尽")) : I18n.Tr("WIDGET_ELEC_BATT_LOCKED", "已隔离");
                    bat1SubRole = !b.IsFlowEnabled ? TextStyleRole.Label : (b.Percent < 15f ? TextStyleRole.Warning : TextStyleRole.Accent);
                }
                else
                {
                    bat1Title = I18n.Tr("WIDGET_ELEC_INTERNAL_BUFFER", "内置缓冲");
                    bat1Val = $"{curEc:F0}/{maxEc:F0} EC";
                    bat1Sub = curEc > 1.0 ? I18n.Tr("WIDGET_ELEC_BATT_ONLINE", "在线") : I18n.Tr("WIDGET_ELEC_BATT_DEPLETED", "耗尽");
                    bat1SubRole = curEc > 1.0 ? TextStyleRole.Accent : TextStyleRole.Warning;
                }
            }
            else
            {
                var b1 = telemetry.Batteries[0];
                bat1Title = I18n.Tr("WIDGET_ELEC_BATT_1", "电池 1");
                bat1Val = $"{b1.Amount:F0} EC";
                bat1Sub = b1.IsFlowEnabled ? $"{b1.Percent:F0}%" : I18n.Tr("WIDGET_ELEC_BATT_LOCKED", "已隔离");
                bat1SubRole = !b1.IsFlowEnabled ? TextStyleRole.Label : (b1.Percent < 15f ? TextStyleRole.Warning : TextStyleRole.Accent);

                double b2Amt = 0.0, b2Max = 0.0;
                bool b2Flow = true;
                for (int i = 1; i < batCount; i++)
                {
                    b2Amt += telemetry.Batteries[i].Amount;
                    b2Max += telemetry.Batteries[i].MaxAmount;
                    if (!telemetry.Batteries[i].IsFlowEnabled) b2Flow = false;
                }
                float b2Pct = b2Max > 0.001 ? (float)(b2Amt / b2Max * 100.0) : 0f;
                bat2Title = batCount > 2 ? I18n.TrFormat("WIDGET_ELEC_BATT_2_EXTRA", batCount - 2) : I18n.Tr("WIDGET_ELEC_BATT_2", "电池 2");
                bat2Val = $"{b2Amt:F0} EC";
                bat2Sub = b2Flow ? $"{b2Pct:F0}%" : I18n.Tr("WIDGET_ELEC_BATT_STANDBY", "待机");
                bat2SubRole = !b2Flow ? TextStyleRole.Label : (b2Pct < 15f ? TextStyleRole.Warning : TextStyleRole.Accent);
            }

            // 3. DC ESS BUS 母线指标
            string dcVal = $"{ecPct:F1}%";
            string dcSub = $"{busVoltage:F1} V";
            TextStyleRole dcRole = ecPct < 15.0 ? TextStyleRole.Warning : (netRate > 0.05 ? TextStyleRole.Accent : TextStyleRole.PrimaryValue);

            // 4. 发电源槽位自适应装配 (按优先级接入真实探针)
            string src1Title = string.Empty, src1Val = string.Empty, src1Sub = string.Empty;
            TextStyleRole src1Role = TextStyleRole.Label;

            bool showSrc2 = false;
            string src2Title = string.Empty, src2Val = string.Empty, src2Sub = string.Empty;
            TextStyleRole src2Role = TextStyleRole.Label;

            bool showSrc3 = false;
            string src3Title = string.Empty, src3Val = string.Empty, src3Sub = string.Empty;
            TextStyleRole src3Role = TextStyleRole.Label;

            int assignedSources = 0;

            void AssignSource(string title, string val, string sub, TextStyleRole role)
            {
                if (assignedSources == 0)
                {
                    src1Title = title; src1Val = val; src1Sub = sub; src1Role = role;
                    assignedSources++;
                }
                else if (assignedSources == 1)
                {
                    src2Title = title; src2Val = val; src2Sub = sub; src2Role = role;
                    showSrc2 = true;
                    assignedSources++;
                }
                else if (assignedSources == 2)
                {
                    src3Title = title; src3Val = val; src3Sub = sub; src3Role = role;
                    showSrc3 = true;
                    assignedSources++;
                }
            }

            if (hasSolar)
            {
                string solStatus = telemetry.SolarPanelsActive > 0 ? $"{telemetry.SolarPanelsActive}/{telemetry.SolarPanelsTotal} ACT" : I18n.Tr("WIDGET_ELEC_NO_SOLAR", "无太阳能");
                AssignSource(
                    I18n.Tr("WIDGET_ELEC_SRC_SOLAR", "太阳能阵列"),
                    $"+{telemetry.SolarPower:F2} e/s",
                    solStatus,
                    telemetry.SolarPanelsActive > 0 ? TextStyleRole.Accent : TextStyleRole.Label);
            }

            if (hasRtg)
            {
                AssignSource(
                    I18n.Tr("WIDGET_ELEC_SRC_RTG", "同位素温差"),
                    $"+{telemetry.RtgPower:F2} e/s",
                    I18n.TrFormat("WIDGET_ELEC_CONTINUOUS", telemetry.RtgCount),
                    TextStyleRole.Accent);
            }

            if (hasFuelCell)
            {
                AssignSource(
                    I18n.Tr("WIDGET_ELEC_SRC_FUELCELL", "燃料电池组"),
                    $"+{telemetry.FuelCellPower:F2} e/s",
                    $"{telemetry.FuelCellActiveCount}/{telemetry.FuelCellCount} ACT",
                    telemetry.FuelCellActiveCount > 0 ? TextStyleRole.Accent : TextStyleRole.Label);
            }

            if (hasAlternator)
            {
                AssignSource(
                    I18n.Tr("WIDGET_ELEC_SRC_ALTERNATOR", "主机发电机"),
                    $"+{telemetry.AlternatorPower:F2} e/s",
                    I18n.TrFormat("WIDGET_ELEC_ENGINES_COUNT", telemetry.AlternatorCount),
                    telemetry.AlternatorPower > 0.001 ? TextStyleRole.Accent : TextStyleRole.Label);
            }

            if (assignedSources == 0)
            {
                src1Title = I18n.Tr("WIDGET_ELEC_BATTERY_ONLY", "仅电池");
                src1Val = "0.00 e/s";
                src1Sub = I18n.Tr("WIDGET_ELEC_NO_SRC", "无电源");
                src1Role = TextStyleRole.Label;
            }

            // 5. 电网负载与能量流速监控 (LOAD & FLOW)
            string loadTitle = I18n.Tr("WIDGET_ELEC_GRID_LOAD", "全舰负载");
            string loadVal = $"-{telemetry.TotalPowerConsumption:F2} e/s";
            string loadSub;
            TextStyleRole loadRole;

            if (Math.Abs(netRate) < 0.05)
            {
                loadSub = I18n.Tr("WIDGET_ELEC_GRID_BALANCED", "供求平衡");
                loadRole = TextStyleRole.PrimaryValue;
            }
            else if (netRate > 0)
            {
                loadSub = $"+{netRate:F1} e/s";
                loadRole = TextStyleRole.Accent;
            }
            else
            {
                loadSub = $"{netRate:F1} e/s";
                loadRole = TextStyleRole.Warning;
            }

            // 6. 状态标牌与电网摘要
            string statusBadge;
            TextStyleRole badgeRole;
            if (ecPct < 15.0)
            {
                statusBadge = "▲ " + I18n.Tr("WIDGET_ELEC_FOOTER_CRITICAL", "危机");
                badgeRole = TextStyleRole.Warning;
            }
            else if (netRate > 0.05)
            {
                statusBadge = "● " + I18n.Tr("WIDGET_ELEC_GRID_CHARGING", "净充电");
                badgeRole = TextStyleRole.Accent;
            }
            else if (netRate < -0.05)
            {
                statusBadge = "▼ " + I18n.Tr("WIDGET_ELEC_GRID_DRAINING", "净放电");
                badgeRole = TextStyleRole.Warning;
            }
            else
            {
                statusBadge = "■ " + I18n.Tr("WIDGET_ELEC_GRID_BALANCED", "供求平衡");
                badgeRole = TextStyleRole.PrimaryValue;
            }

            string subTitle = I18n.TrFormat("WIDGET_ELEC_FOOTER_SUMMARY", $"{busVoltage:F1}", batCount, genSourcesCount);

            // 7. 底部系统实时诊断提示 (Footer Tip)
            string footerTip;
            TextStyleRole footerRole;
            if (netRate < -0.05 && !double.IsNaN(telemetry.TimeToDepletionSeconds) && telemetry.TimeToDepletionSeconds > 0)
            {
                footerTip = I18n.TrFormat("WIDGET_ELEC_FOOTER_DRAIN", FormatDuration(telemetry.TimeToDepletionSeconds));
                footerRole = TextStyleRole.Warning;
            }
            else if (netRate > 0.05 && ecPct < 99.5 && !double.IsNaN(telemetry.TimeToFullSeconds) && telemetry.TimeToFullSeconds > 0)
            {
                footerTip = I18n.TrFormat("WIDGET_ELEC_FOOTER_CHARGE", FormatDuration(telemetry.TimeToFullSeconds));
                footerRole = TextStyleRole.Accent;
            }
            else if (ecPct < 15.0)
            {
                footerTip = I18n.Tr("WIDGET_ELEC_FOOTER_CRITICAL", "总线欠压危机 · 电池严重匮乏");
                footerRole = TextStyleRole.Danger;
            }
            else
            {
                footerTip = I18n.Tr("WIDGET_ELEC_FOOTER_BALANCED", "总线稳态平衡 · 28V 母线正常");
                footerRole = TextStyleRole.Label;
            }

            CurrentState = new ElectricalSystemState
            {
                HasVessel = true,
                GridMode = gridMode,
                BatteryLayout = batLayout,
                TargetHeight = targetHeight,
                SubTitle = subTitle,
                StatusBadge = statusBadge,
                StatusBadgeRole = badgeRole,
                DcVal = dcVal,
                DcSub = dcSub,
                DcRole = dcRole,
                Bat1Title = bat1Title,
                Bat1Val = bat1Val,
                Bat1Sub = bat1Sub,
                Bat1SubRole = bat1SubRole,
                ShowBat2 = showBat2,
                Bat2Title = bat2Title,
                Bat2Val = bat2Val,
                Bat2Sub = bat2Sub,
                Bat2SubRole = bat2SubRole,
                Source1Title = src1Title,
                Source1Val = src1Val,
                Source1Sub = src1Sub,
                Source1Role = src1Role,
                ShowSource2 = showSrc2,
                Source2Title = src2Title,
                Source2Val = src2Val,
                Source2Sub = src2Sub,
                Source2Role = src2Role,
                ShowSource3 = showSrc3,
                Source3Title = src3Title,
                Source3Val = src3Val,
                Source3Sub = src3Sub,
                Source3Role = src3Role,
                LoadTitle = loadTitle,
                LoadVal = loadVal,
                LoadSub = loadSub,
                LoadRole = loadRole,
                FooterTip = footerTip,
                FooterRole = footerRole
            };
        }
    }

    /// <summary>
    /// 单个电网节点图元句柄集
    /// </summary>
    public sealed class NodeBoxHandles
    {
        public GameObject Root;
        public RectTransform Rt;
        public Image AccentBar;
        public RectTransform AccentRt;
        public Text TitleText;
        public RectTransform TitleRt;
        public Text ValText;
        public RectTransform ValRt;
        public Text SubText;
        public RectTransform SubRt;
        public readonly Cached<string> LastTitle = new Cached<string>(string.Empty);
        public readonly Cached<string> LastVal = new Cached<string>(string.Empty);
        public readonly Cached<string> LastSub = new Cached<string>(string.Empty);
        public readonly Cached<TextStyleRole> LastRole = new Cached<TextStyleRole>(TextStyleRole.PrimaryValue);
        public readonly Cached<bool> LastActive = new Cached<bool>(true);

        public void SetSizeAndPos(Vector2 size, Vector2 pos, float s)
        {
            Rt.SetSizeDeltaSafe(size);
            Rt.SetAnchoredPositionSafe(pos);
            AccentRt.SetSizeDeltaSafe(new Vector2(size.x - 6f * s, 1.5f * s));
            AccentRt.SetAnchoredPositionSafe(new Vector2(0f, (size.y * 0.5f) - 1.5f * s));
            TitleRt.SetSizeDeltaSafe(new Vector2(size.x - 4f * s, 12f * s));
            TitleRt.SetAnchoredPositionSafe(new Vector2(0f, (size.y * 0.5f) - 7f * s));
            ValRt.SetSizeDeltaSafe(new Vector2(size.x - 4f * s, 14f * s));
            ValRt.SetAnchoredPositionSafe(new Vector2(0f, (size.y * 0.5f) - 18f * s));
            SubRt.SetSizeDeltaSafe(new Vector2(size.x - 4f * s, 11f * s));
            SubRt.SetAnchoredPositionSafe(new Vector2(0f, -(size.y * 0.5f) + 6f * s));
        }

        public void ResetCache()
        {
            LastTitle.Reset(string.Empty);
            LastVal.Reset(string.Empty);
            LastSub.Reset(string.Empty);
            LastRole.Reset(TextStyleRole.PrimaryValue);
            LastActive.Reset(true);
        }
    }

    /// <summary>
    /// 原生 UGUI 航电电气系统监控面板 (ELEC Power Distribution)
    /// 纯真实数据探针驱动，全自动弹性布局适应飞船实时构型
    /// </summary>
    [FlightWidget("electrical", "elec", "power_grid", Category = WidgetCategory.Systems, DisplayName = "ELEC 电力分配与电网系统", Description = "蓄电池组拓扑、DC ESS 总线负荷、真实发电源与即时净充放电率 (EC/s)。自动弹性排版。", DefaultWidgetId = "custom.electrical", DefaultX = -440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "custom.electrical", "custom.elec", "core.electrical" })]
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
        private RectTransform _subTitleRt;
        private RectTransform _div1Rt;

        // 电网节点 UI 元素
        private NodeBoxHandles _nodeBat1;
        private NodeBoxHandles _nodeBat2;
        private NodeBoxHandles _nodeDcBus;
        private NodeBoxHandles _nodeSource1;
        private NodeBoxHandles _nodeLoad;
        private NodeBoxHandles _nodeSource2;
        private NodeBoxHandles _nodeSource3;

        private Text _footerTipText;
        private RectTransform _footerTipRt;

        // 智能私有缓存 (SPEC-009)
        private readonly CachedFloat _lastHeight = new CachedFloat(155f);
        private readonly Cached<ElecGridMode> _lastGridMode = new Cached<ElecGridMode>(ElecGridMode.Compact);
        private readonly Cached<ElecBatteryLayout> _lastBatLayout = new Cached<ElecBatteryLayout>(ElecBatteryLayout.DualBank);
        private readonly Cached<string> _lastSubTitle = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBadgeText = new Cached<string>(string.Empty);
        private readonly Cached<TextStyleRole> _lastBadgeRole = new Cached<TextStyleRole>(TextStyleRole.Label);
        private readonly Cached<string> _lastFooterTip = new Cached<string>(string.Empty);
        private readonly Cached<TextStyleRole> _lastFooterRole = new Cached<TextStyleRole>(TextStyleRole.Label);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = BaseSize * s;

            // 1. 顶部 Header (Title & StatusBadge 已由基类微控件 DSL 自动构建)
            _subTitleText = UIFactory.CreateText(transform, "SubTitle", I18n.Tr("WIDGET_ELEC_POWER_DIST", "配电"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            _subTitleRt = _subTitleText.GetComponent<RectTransform>();
            _subTitleRt.sizeDelta = new Vector2(120f * s, 16f * s);
            _subTitleRt.anchoredPosition = new Vector2(-15f * s, 62f * s);

            // 分割横线
            GameObject div1Go = UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 50f * s), theme.FrameBorderColor);
            _div1Rt = div1Go.GetComponent<RectTransform>();

            // 2. 第一行：BAT 1 & BAT 2 蓄电池节点与中央母线
            _nodeBat1 = CreateNodeBox(transform, "Node_BAT1", new Vector2(75f * s, 36f * s), new Vector2(-88f * s, 26f * s), I18n.Tr("WIDGET_ELEC_BATT_1", "电池 1"), theme.WarningColor);
            _nodeBat2 = CreateNodeBox(transform, "Node_BAT2", new Vector2(75f * s, 36f * s), new Vector2(88f * s, 26f * s), I18n.Tr("WIDGET_ELEC_BATT_2", "电池 2"), theme.WarningColor);
            _nodeDcBus = CreateNodeBox(transform, "Node_DCBUS", new Vector2(90f * s, 36f * s), new Vector2(0f, 26f * s), I18n.Tr("WIDGET_ELEC_DC_ESS_BUS", "直流应急母线"), theme.AccentPrimary);

            // 3. 第二行：发电源 1 与 电网负载 (LOAD)
            _nodeSource1 = CreateNodeBox(transform, "Node_GEN", new Vector2(122f * s, 40f * s), new Vector2(-65f * s, -22f * s), I18n.Tr("WIDGET_ELEC_PWR_SOURCES", "发电源"), theme.AccentSecondary);
            _nodeLoad = CreateNodeBox(transform, "Node_LOAD", new Vector2(122f * s, 40f * s), new Vector2(65f * s, -22f * s), I18n.Tr("WIDGET_ELEC_GRID_LOAD", "全舰负载"), theme.AccentPrimary);

            // 4. 弹性扩充行：发电源 2 与 发电源 3 (预先装配，待命休眠)
            _nodeSource2 = CreateNodeBox(transform, "Node_SRC2", new Vector2(122f * s, 40f * s), new Vector2(-65f * s, -120f * s), I18n.Tr("WIDGET_ELEC_AUX_SOURCE", "辅助电源"), theme.AccentSecondary);
            _nodeSource3 = CreateNodeBox(transform, "Node_SRC3", new Vector2(122f * s, 40f * s), new Vector2(65f * s, -120f * s), I18n.Tr("WIDGET_ELEC_AUX_SOURCE_2", "辅助电源 2"), theme.AccentSecondary);
            _nodeSource2.Root.SetActiveSafe(false);
            _nodeSource3.Root.SetActiveSafe(false);

            // 5. 底部状态提示微标
            _footerTipText = UIFactory.CreateText(transform, "FooterTip", I18n.Tr("WIDGET_ELEC_BUS_TIP", "28V 直流母线系统  ·  主航电"),
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, theme.TextAccentColor);
            _footerTipRt = _footerTipText.GetComponent<RectTransform>();
            _footerTipRt.sizeDelta = new Vector2(panelSize.x - 20f * s, 12f * s);
            _footerTipRt.anchoredPosition = new Vector2(0f, -64f * s);

            // 注册节点微控件至标准化管理器
            this.Controls.Register(new WidgetReadoutControl("battery_nodes", "蓄电池组", _nodeBat1.ValText != null ? _nodeBat1.ValText.gameObject : null, _nodeBat1.ValText, _nodeBat2.ValText, TextStyleRole.PrimaryValue, "{VOLT}"));
            this.Controls.Register(new WidgetReadoutControl("dc_bus", "直流总线母线", _nodeDcBus.ValText != null ? _nodeDcBus.ValText.gameObject : null, _nodeDcBus.ValText, _nodeDcBus.SubText, TextStyleRole.PrimaryValue, "{EC:PCT}"));
            this.Controls.Register(new WidgetReadoutControl("generation_load", "发电与负载监控", _nodeSource1.ValText != null ? _nodeSource1.ValText.gameObject : null, _nodeSource1.ValText, _nodeLoad.ValText, TextStyleRole.PrimaryValue, "{SOLAR}"));

            _logic.Bat1Template = GetTemplateChannel("BAT1_VAL", "{VOLT}");
            _logic.DcBusValTemplate = GetTemplateChannel("DCBUS_VAL", "{EC:PCT}%");
            _logic.DcBusSubTemplate = GetTemplateChannel("DCBUS_SUB", "{EC}/{EC:MAX} EC");
        }

        private NodeBoxHandles CreateNodeBox(Transform parent, string name, Vector2 size, Vector2 pos, string nodeTitle, Color accentColor)
        {
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            Color borderCol = theme.FrameBorderColor;
            Color cellBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);
            Color faintBorder = WidgetStyleManager.Weighted(borderCol, LineWeight.Faint);
            GameObject box = UIFactory.CreatePanel(parent, name, size, pos, cellBg, faintBorder, 1f * s);
            RectTransform boxRt = box.GetComponent<RectTransform>();

            // 顶部极细状态标示条 (Micro Accent Line)
            GameObject accBarGo = UIFactory.CreatePanel(box.transform, "AccentBar", new Vector2(size.x - 6f * s, 1.5f * s),
                new Vector2(0f, (size.y * 0.5f) - 1.5f * s), accentColor);
            Image accentImg = accBarGo.GetComponent<Image>();
            RectTransform accentRt = accBarGo.GetComponent<RectTransform>();

            Text title = UIFactory.CreateText(box.transform, "Title", nodeTitle, Mathf.RoundToInt(7.5f * s),
                TextAnchor.UpperCenter, theme != null ? (Color)theme.TextAccentColor : accentColor);
            RectTransform titRt = title.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(size.x - 4f * s, 12f * s);
            titRt.anchoredPosition = new Vector2(0f, (size.y * 0.5f) - 7f * s);

            Text valText = UIFactory.CreateText(box.transform, "Value", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            RectTransform valRt = valText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(size.x - 4f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(0f, (size.y * 0.5f) - 18f * s);

            Text subText = UIFactory.CreateText(box.transform, "Sub", "---", Mathf.RoundToInt(7f * s),
                TextAnchor.LowerCenter, accentColor);
            RectTransform subRt = subText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(size.x - 4f * s, 11f * s);
            subRt.anchoredPosition = new Vector2(0f, -(size.y * 0.5f) + 6f * s);

            return new NodeBoxHandles
            {
                Root = box,
                Rt = boxRt,
                AccentBar = accentImg,
                AccentRt = accentRt,
                TitleText = title,
                TitleRt = titRt,
                ValText = valText,
                ValRt = valRt,
                SubText = subText,
                SubRt = subRt
            };
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        private void UpdateElasticLayout(in ElectricalSystemState state)
        {
            float s = CurrentDpiScale;
            float totalH = state.TargetHeight;
            float halfH = totalH * 0.5f;

            // 1. 调整小组件卡片整体尺寸
            RectTransform.SetSizeDeltaSafe(new Vector2(BaseSize.x * s, totalH * s));

            // 2. 顶部 Header 与分隔线自适应
            float headerY = halfH - 15.5f;
            float div1Y = halfH - 27.5f;
            _subTitleRt.SetAnchoredPositionSafe(new Vector2(-15f * s, headerY * s));
            _div1Rt.SetAnchoredPositionSafe(new Vector2(0f, div1Y * s));

            // 3. 电池行 (Row 1)
            float row1Y = halfH - 51.5f;
            if (state.BatteryLayout == ElecBatteryLayout.SingleBank)
            {
                _nodeBat1.SetSizeAndPos(new Vector2(122f * s, 36f * s), new Vector2(-65f * s, row1Y * s), s);
                _nodeDcBus.SetSizeAndPos(new Vector2(122f * s, 36f * s), new Vector2(65f * s, row1Y * s), s);
                if (_nodeBat2.LastActive.Update(false)) { _nodeBat2.Root.SetActiveSafe(false); }
            }
            else
            {
                _nodeBat1.SetSizeAndPos(new Vector2(75f * s, 36f * s), new Vector2(-88f * s, row1Y * s), s);
                _nodeDcBus.SetSizeAndPos(new Vector2(90f * s, 36f * s), new Vector2(0f, row1Y * s), s);
                _nodeBat2.SetSizeAndPos(new Vector2(75f * s, 36f * s), new Vector2(88f * s, row1Y * s), s);
                if (_nodeBat2.LastActive.Update(true)) { _nodeBat2.Root.SetActiveSafe(true); }
            }

            // 4. 动力源与电网负载 (Row 2, Row 3)
            if (state.GridMode == ElecGridMode.Compact)
            {
                float row2Y = halfH - 99.5f;
                _nodeSource1.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(-65f * s, row2Y * s), s);
                _nodeLoad.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(65f * s, row2Y * s), s);

                if (_nodeSource2.LastActive.Update(false)) { _nodeSource2.Root.SetActiveSafe(false); }
                if (_nodeSource3.LastActive.Update(false)) { _nodeSource3.Root.SetActiveSafe(false); }
            }
            else if (state.GridMode == ElecGridMode.ExpandedDual)
            {
                float row2Y = halfH - 99.5f;
                float row3Y = halfH - 143.5f;
                _nodeSource1.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(-65f * s, row2Y * s), s);
                _nodeSource2.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(65f * s, row2Y * s), s);
                _nodeLoad.SetSizeAndPos(new Vector2(252f * s, 38f * s), new Vector2(0f, row3Y * s), s);

                if (_nodeSource2.LastActive.Update(true)) { _nodeSource2.Root.SetActiveSafe(true); }
                if (_nodeSource3.LastActive.Update(false)) { _nodeSource3.Root.SetActiveSafe(false); }
            }
            else // ExpandedMulti
            {
                float row2Y = halfH - 99.5f;
                float row3Y = halfH - 143.5f;
                _nodeSource1.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(-65f * s, row2Y * s), s);
                _nodeSource2.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(65f * s, row2Y * s), s);
                _nodeSource3.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(-65f * s, row3Y * s), s);
                _nodeLoad.SetSizeAndPos(new Vector2(122f * s, 40f * s), new Vector2(65f * s, row3Y * s), s);

                if (_nodeSource2.LastActive.Update(true)) { _nodeSource2.Root.SetActiveSafe(true); }
                if (_nodeSource3.LastActive.Update(true)) { _nodeSource3.Root.SetActiveSafe(true); }
            }

            // 5. 底部系统状态说明 (Footer)
            float footerY = -halfH + 13.5f;
            _footerTipRt.SetAnchoredPositionSafe(new Vector2(0f, footerY * s));
        }

        private void RenderNodeBox(NodeBoxHandles box, string title, string val, string sub, TextStyleRole role, ThemeConfig theme)
        {
            if (box == null) return;
            if (box.LastTitle.Update(title)) { box.TitleText.SetTextSafe(title); }
            if (box.LastVal.Update(val)) { box.ValText.SetTextSafe(val); }
            if (box.LastSub.Update(sub)) { box.SubText.SetTextSafe(sub); }
            if (box.LastRole.Update(role)) { ApplyText(box.SubText, role, theme); }
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            if (!state.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;

            // 1. 弹性几何拓扑刷新 (自适应高度与节点重排)
            bool layoutDirty = _lastHeight.Update(state.TargetHeight) |
                               _lastGridMode.Update(state.GridMode) |
                               _lastBatLayout.Update(state.BatteryLayout);

            if (layoutDirty)
            {
                UpdateElasticLayout(in state);
            }

            // 2. 节点数据渲染
            RenderNodeBox(_nodeBat1, state.Bat1Title, state.Bat1Val, state.Bat1Sub, state.Bat1SubRole, theme);

            if (state.ShowBat2)
            {
                RenderNodeBox(_nodeBat2, state.Bat2Title, state.Bat2Val, state.Bat2Sub, state.Bat2SubRole, theme);
            }

            RenderNodeBox(_nodeDcBus, I18n.Tr("WIDGET_ELEC_DC_ESS_BUS", "直流应急母线"), state.DcVal, state.DcSub, state.DcRole, theme);
            RenderNodeBox(_nodeSource1, state.Source1Title, state.Source1Val, state.Source1Sub, state.Source1Role, theme);
            RenderNodeBox(_nodeLoad, state.LoadTitle, state.LoadVal, state.LoadSub, state.LoadRole, theme);

            if (state.ShowSource2)
            {
                RenderNodeBox(_nodeSource2, state.Source2Title, state.Source2Val, state.Source2Sub, state.Source2Role, theme);
            }

            if (state.ShowSource3)
            {
                RenderNodeBox(_nodeSource3, state.Source3Title, state.Source3Val, state.Source3Sub, state.Source3Role, theme);
            }

            // 3. 顶栏与底栏摘要
            if (_lastSubTitle.Update(state.SubTitle))
            {
                _subTitleText.SetTextSafe(state.SubTitle);
            }

            if (_lastBadgeText.Update(state.StatusBadge))
            {
                StatusBadge.TextComponent.SetTextSafe(state.StatusBadge);
            }
            if (_lastBadgeRole.Update(state.StatusBadgeRole))
            {
                ApplyText(StatusBadge.TextComponent, state.StatusBadgeRole, theme);
            }

            if (_lastFooterTip.Update(state.FooterTip))
            {
                _footerTipText.SetTextSafe(state.FooterTip);
            }
            if (_lastFooterRole.Update(state.FooterRole))
            {
                ApplyText(_footerTipText, state.FooterRole, theme);
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastHeight.Reset(155f);
            _lastGridMode.Reset(ElecGridMode.Compact);
            _lastBatLayout.Reset(ElecBatteryLayout.DualBank);
            _lastSubTitle.Reset(string.Empty);
            _lastBadgeText.Reset(string.Empty);
            _lastBadgeRole.Reset(TextStyleRole.Label);
            _lastFooterTip.Reset(string.Empty);
            _lastFooterRole.Reset(TextStyleRole.Label);
            _nodeBat1?.ResetCache();
            _nodeBat2?.ResetCache();
            _nodeDcBus?.ResetCache();
            _nodeSource1?.ResetCache();
            _nodeLoad?.ResetCache();
            _nodeSource2?.ResetCache();
            _nodeSource3?.ResetCache();
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
                ApplyText(_subTitleText, TextStyleRole.Label, theme);
            }

            ApplyBoxTheme(_nodeBat1, theme);
            ApplyBoxTheme(_nodeBat2, theme);
            ApplyBoxTheme(_nodeDcBus, theme);
            ApplyBoxTheme(_nodeSource1, theme);
            ApplyBoxTheme(_nodeLoad, theme);
            ApplyBoxTheme(_nodeSource2, theme);
            ApplyBoxTheme(_nodeSource3, theme);

            if (_footerTipText != null)
            {
                ApplyText(_footerTipText, TextStyleRole.Label, theme);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        private void ApplyBoxTheme(NodeBoxHandles box, ThemeConfig theme)
        {
            if (box == null || theme == null) return;
            if (box.TitleText != null) ApplyText(box.TitleText, TextStyleRole.Label, theme);
            if (box.ValText != null) ApplyText(box.ValText, TextStyleRole.PrimaryValue, theme);
            if (box.SubText != null) ApplyText(box.SubText, TextStyleRole.Label, theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
