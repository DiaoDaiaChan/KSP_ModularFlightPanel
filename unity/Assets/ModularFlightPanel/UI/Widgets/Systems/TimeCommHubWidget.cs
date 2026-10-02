using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 时控与通信综合中枢零 GC 纯值遥测快照 (MFP-SPEC-010 / MFP-SPEC-012)
    /// </summary>
    public struct TimeCommHubState : IEquatable<TimeCommHubState>
    {
        // 飞船状态
        public bool HasVessel;

        // 时间加速与时钟遥测
        public string PrimaryClockStr;
        public string SecondaryClockStr;
        public string ModeBtnLabel;
        public bool IsPaused;
        public bool IsPhys;
        public double WarpRate;
        public int ActiveWarpIndex;
        public int MaxWarpIndex;
        public string WarpModeLabel;
        public string WarpTagLabel;

        // 通信网络与天线遥测
        public bool IsConnected;
        public double CommSignal;
        public string SignalPercentStr;
        public string TargetName;
        public string RouteDesc;
        public string RateStr;
        public bool HasTx;
        public bool HasRx;
        public int ActiveRfBars;
        public string ControlBadgeText;
        public StatusSurfaceRole ControlBadgeRole;
        public string HwSummary;
        public int AntennaCount;
        public AntennaRowSnapshot Ant0;
        public AntennaRowSnapshot Ant1;
        public AntennaRowSnapshot Ant2;
        public AntennaRowSnapshot Ant3;

        public AntennaRowSnapshot GetAntenna(int index)
        {
            switch (index)
            {
                case 0: return Ant0;
                case 1: return Ant1;
                case 2: return Ant2;
                case 3: return Ant3;
                default: return default;
            }
        }

        public bool Equals(TimeCommHubState other)
        {
            return HasVessel == other.HasVessel &&
                   string.Equals(PrimaryClockStr, other.PrimaryClockStr, StringComparison.Ordinal) &&
                   string.Equals(SecondaryClockStr, other.SecondaryClockStr, StringComparison.Ordinal) &&
                   string.Equals(ModeBtnLabel, other.ModeBtnLabel, StringComparison.Ordinal) &&
                   IsPaused == other.IsPaused &&
                   IsPhys == other.IsPhys &&
                   Math.Abs(WarpRate - other.WarpRate) < 0.001 &&
                   ActiveWarpIndex == other.ActiveWarpIndex &&
                   MaxWarpIndex == other.MaxWarpIndex &&
                   string.Equals(WarpModeLabel, other.WarpModeLabel, StringComparison.Ordinal) &&
                   string.Equals(WarpTagLabel, other.WarpTagLabel, StringComparison.Ordinal) &&
                   IsConnected == other.IsConnected &&
                   Math.Abs(CommSignal - other.CommSignal) < 0.005 &&
                   string.Equals(SignalPercentStr, other.SignalPercentStr, StringComparison.Ordinal) &&
                   string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
                   string.Equals(RouteDesc, other.RouteDesc, StringComparison.Ordinal) &&
                   string.Equals(RateStr, other.RateStr, StringComparison.Ordinal) &&
                   HasTx == other.HasTx &&
                   HasRx == other.HasRx &&
                   ActiveRfBars == other.ActiveRfBars &&
                   string.Equals(ControlBadgeText, other.ControlBadgeText, StringComparison.Ordinal) &&
                   ControlBadgeRole == other.ControlBadgeRole &&
                   string.Equals(HwSummary, other.HwSummary, StringComparison.Ordinal) &&
                   AntennaCount == other.AntennaCount &&
                   Ant0.Equals(other.Ant0) &&
                   Ant1.Equals(other.Ant1) &&
                   Ant2.Equals(other.Ant2) &&
                   Ant3.Equals(other.Ant3);
        }

        public override bool Equals(object obj) => obj is TimeCommHubState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasVessel ? 1 : 0;
                hash = (hash * 397) ^ (PrimaryClockStr != null ? PrimaryClockStr.GetHashCode() : 0);
                hash = (hash * 397) ^ (IsPaused ? 1 : 0);
                hash = (hash * 397) ^ (IsPhys ? 1 : 0);
                hash = (hash * 397) ^ ActiveWarpIndex;
                hash = (hash * 397) ^ (IsConnected ? 1 : 0);
                hash = (hash * 397) ^ ActiveRfBars;
                hash = (hash * 397) ^ (SignalPercentStr != null ? SignalPercentStr.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 时控与通信综合中枢纯 C# 业务解耦大脑 (MFP-SPEC-004C / MFP-SPEC-012)
    /// 脱离 UnityEngine，完全支持离线单元测试与无头仿真验证。
    /// </summary>
    public class TimeCommHubLogic : WidgetLogic<TimeCommHubState>
    {
        public const int MaxChevronCount = 8;
        public const int RfBarCount = 5;
        public const int MaxExpandedAntennaRows = 4;

        public bool ShowUniversalTime { get; set; } = false;
        public string UtTemplate { get; set; } = "{UT}";
        public string MetTemplate { get; set; } = "{MET}";
        public string PhysLabel { get; set; } = "PHYS";
        public string WarpLabel { get; set; } = "WARP";

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    Reset();
                }
                return;
            }

            // 1. 时间加速与任务/宇宙时钟推演
            string primaryTpl = ShowUniversalTime ? UtTemplate : MetTemplate;
            string secondaryTpl = ShowUniversalTime ? MetTemplate : UtTemplate;
            string primaryClock = TelemetryTokenEngine.Evaluate(primaryTpl, telemetry);
            string secondaryClock = TelemetryTokenEngine.Evaluate(secondaryTpl, telemetry);

            bool isPhys = telemetry.IsPhysicsWarp;
            string warpModeLabel = isPhys ? PhysLabel : WarpLabel;
            string warpTag = isPhys
                ? I18n.Tr("WIDGET_TIME_COMM_MODE_PHYS", "▲ 物理加速")
                : I18n.Tr("WIDGET_TIME_COMM_MODE_ORBIT", "● 轨道加速");

            int maxWarpIdx = Mathf.Clamp(telemetry.MaxTimeWarpRateIndex, 1, MaxChevronCount - 1);
            int activeWarpIdx = telemetry.TimeWarpRateIndex;
            double warpRate = telemetry.TimeWarpRate;
            bool isPaused = telemetry.IsGamePaused;
            string modeBtnLabel = ShowUniversalTime ? "UT" : "MET";

            // 2. 通信网络与链路状态推演
            bool isConnected = telemetry.IsConnected;
            double rawSig = Mathf.Clamp01((float)telemetry.CommSignal);
            string percentStr = $"{(int)(rawSig * 100f + 0.5f)}%";

            string ctrlLevel = telemetry.ControlLevelStr
                ?? (isConnected ? I18n.Tr("WIDGET_SIG_CTRL_FULL", "全权控制") : I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路"));
            bool isPartial = ctrlLevel.IndexOf("PART", StringComparison.OrdinalIgnoreCase) >= 0 || (isConnected && rawSig < 0.35);

            StatusSurfaceRole badgeRole = !isConnected
                ? StatusSurfaceRole.Danger
                : (isPartial ? StatusSurfaceRole.Caution : StatusSurfaceRole.Success);

            string badgeText = !isConnected
                ? "✕ " + I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路")
                : (isPartial ? "▲ " + I18n.Tr("WIDGET_SIG_CTRL_PARTIAL", "部分控制") : "● " + I18n.Tr("WIDGET_SIG_CTRL_FULL", "全权控制"));

            string rawTarget = telemetry.DirectLinkTarget;
            string targetName;
            string routeDesc;
            var links = telemetry.ActiveCommLinks;
            bool hasRealLinks = links != null && links.Count > 0;

            if (isConnected)
            {
                if (!string.IsNullOrEmpty(rawTarget) && rawTarget != "NONE")
                {
                    targetName = rawTarget.ToUpperInvariant();
                }
                else if (hasRealLinks)
                {
                    targetName = links[0].PeerName.ToUpperInvariant();
                }
                else
                {
                    targetName = I18n.Tr("WIDGET_SIG_KERBIN_DSN_DIRECT", "坎星深空网直连");
                }

                bool isDirect = !hasRealLinks || links[0].IsDirectHome || links.Count == 1;
                routeDesc = isDirect
                    ? I18n.Tr("WIDGET_SIG_DIRECT_HOME_DSN", "直连 · 深空网主站")
                    : I18n.TrFormat("WIDGET_SIG_RELAY_HOPS", links.Count);
            }
            else
            {
                targetName = I18n.Tr("WIDGET_SIG_NO_STATION_LINK", "无测控站链路");
                routeDesc = I18n.Tr("WIDGET_SIG_SEARCHING_LINK", "搜索链路中");
            }

            double rawRate = telemetry.DataRateBps;
            string rateStr;
            if (!isConnected || rawRate <= 0.0)
            {
                rateStr = "0.0 bps";
            }
            else if (rawRate < 1000.0)
            {
                rateStr = $"{rawRate:0.#} bps";
            }
            else if (rawRate < 1000000.0)
            {
                rateStr = $"{rawRate / 1000.0:0.0} Kbps";
            }
            else
            {
                rateStr = $"{rawRate / 1000000.0:0.00} Mbps";
            }

            bool hasTx = isConnected && (telemetry.SignalTx > 0.01 || rawRate > 1.0);
            bool hasRx = isConnected && (telemetry.SignalRx > 0.01 || rawSig > 0.05);

            int activeRfBars = 0;
            if (isConnected)
            {
                if (rawSig > 0.85) activeRfBars = 5;
                else if (rawSig > 0.60) activeRfBars = 4;
                else if (rawSig > 0.35) activeRfBars = 3;
                else if (rawSig > 0.15) activeRfBars = 2;
                else activeRfBars = 1;
            }

            // 3. 硬件天线阵列工况推演
            var antennas = telemetry.Antennas;
            string primaryAntName = I18n.Tr("WIDGET_SIG_INTERNAL_POD_ANTENNA", "内置舱段天线");
            int totalAnts = (antennas != null && antennas.Count > 0) ? antennas.Count : (telemetry.AntennaCount > 0 ? telemetry.AntennaCount : 1);
            int activeAnts = 0;

            if (antennas != null && antennas.Count > 0)
            {
                for (int i = 0; i < antennas.Count; i++)
                {
                    if (antennas[i].IsOperational && antennas[i].Status != "BROKEN" && antennas[i].Status != "OFFLINE")
                    {
                        activeAnts++;
                        if (primaryAntName == I18n.Tr("WIDGET_SIG_INTERNAL_POD_ANTENNA", "内置舱段天线"))
                        {
                            primaryAntName = SignalStatusLogic.CleanAntennaName(antennas[i].Name, i);
                        }
                    }
                }
            }
            if (activeAnts == 0 && isConnected) activeAnts = 1;

            string hwSummary = isConnected
                ? I18n.TrFormat("WIDGET_SIG_HW_SUMMARY", primaryAntName, activeAnts, totalAnts)
                : I18n.TrFormat("WIDGET_SIG_HW_NOLINK", totalAnts);

            int antCount = (antennas != null) ? antennas.Count : 0;
            AntennaRowSnapshot ant0 = default, ant1 = default, ant2 = default, ant3 = default;

            for (int i = 0; i < MaxExpandedAntennaRows; i++)
            {
                AntennaRowSnapshot snap;
                if (antennas != null && i < antCount)
                {
                    var ant = antennas[i];
                    snap = new AntennaRowSnapshot
                    {
                        Name = SignalStatusLogic.CleanAntennaName(ant.Name, i),
                        Status = ant.Status,
                        IsActive = true
                    };
                }
                else if (i == 0)
                {
                    snap = new AntennaRowSnapshot
                    {
                        Name = I18n.Tr("WIDGET_SIG_INTERNAL_POD_ANTENNA", "内置舱段天线"),
                        Status = isConnected ? I18n.Tr("WIDGET_SIG_LINKED", "已链接") : I18n.Tr("WIDGET_SIG_OFFLINE", "离线"),
                        IsActive = true
                    };
                }
                else
                {
                    snap = new AntennaRowSnapshot { IsActive = false };
                }

                switch (i)
                {
                    case 0: ant0 = snap; break;
                    case 1: ant1 = snap; break;
                    case 2: ant2 = snap; break;
                    case 3: ant3 = snap; break;
                }
            }

            CurrentState = new TimeCommHubState
            {
                HasVessel = true,
                PrimaryClockStr = primaryClock,
                SecondaryClockStr = secondaryClock,
                ModeBtnLabel = modeBtnLabel,
                IsPaused = isPaused,
                IsPhys = isPhys,
                WarpRate = warpRate,
                ActiveWarpIndex = activeWarpIdx,
                MaxWarpIndex = maxWarpIdx,
                WarpModeLabel = warpModeLabel,
                WarpTagLabel = warpTag,
                IsConnected = isConnected,
                CommSignal = rawSig,
                SignalPercentStr = percentStr,
                TargetName = targetName,
                RouteDesc = routeDesc,
                RateStr = rateStr,
                HasTx = hasTx,
                HasRx = hasRx,
                ActiveRfBars = activeRfBars,
                ControlBadgeText = badgeText,
                ControlBadgeRole = badgeRole,
                HwSummary = hwSummary,
                AntennaCount = antCount,
                Ant0 = ant0,
                Ant1 = ant1,
                Ant2 = ant2,
                Ant3 = ant3
            };
        }
    }

    /// <summary>
    /// 现代化时控与通信综合中枢组件 (Avionics Mission Clock, Time Warp & CommNet Hub)
    /// 
    /// 差异化双舱模块设计：
    /// 1. 左仓（战术时钟与加速控制底盘）：
    ///    - 深色下沉战术面板底盘 (PanelDeep)，航空机械仪器质感
    ///    - 专属高对比度 LCD 任务时钟内嵌表框 (ClockPlate Bezel)，居中大字显示 T+
    ///    - 下沉式 8 级多段加速指示凹槽 (Chevrons Track)，集成步退/步进/瞬时归一 (1X Kill-Warp)
    /// 2. 右仓（航电通信网络 HUD 底盘）：
    ///    - 半透明科技玻璃底盘 (Panel) + 顶置荧光微光饰条 (CommHeaderStripe)，HUD 全息风格
    ///    - 5 阶非线性射频柱状均衡器与高灵敏收发指示 (▲TX / ▼RX)
    ///    - 拓扑路由解析与底部内嵌硬件天线工况槽 (HwSlot)，支持拉伸高度动态展开全阵列
    /// </summary>
    [FlightWidget("time_comm_hub", "timecomm", "mission_comm", "status_hub",
        Category = WidgetCategory.Systems,
        DisplayName = "MISSION & COMM 任务时钟通信综合中枢",
        Description = "时间加速、任务/宇宙时钟、通信网络与天线探针一体化综合中枢。",
        DefaultWidgetId = "core.time_comm_hub",
        DefaultX = 0f, DefaultY = 210f,
        IsSingleton = true,
        ExactIds = new[] { "core.time_comm_hub", "custom.time_comm_hub" })]
    public class TimeCommHubWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(488f, 68f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

        // 声明式自适应物理尺寸契约 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(300f, 42f);
        public Vector2 MaxBaseSize => new Vector2(760f, 220f);

        private readonly TimeCommHubLogic _logic = new TimeCommHubLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private IFlightTelemetry _telemetry;
        private ThemeConfig _currentTheme;
        private bool _stockHidden = true;

        // 中央垂直细分割线
        private GameObject _centerDivider;
        private Image _dividerLine;

        // ==========================================
        // 左仓：时钟与加速控制底盘 (Time & Warp Bay Chassis)
        // ==========================================
        private GameObject _leftBayGo;
        private Image _leftBayBg;
        private Outline _leftBayOutline;
        private GameObject _clockPlateGo;
        private Image _clockPlateBg;
        private Outline _clockPlateOutline;
        private GameObject _chevronsTrackGo;
        private Image _chevronsTrackBg;

        private Button _modeBtn;
        private Text _modeBtnText;
        private Text _clockText;
        private Button _pauseBtn;
        private Text _pauseBtnText;
        private Button _stockBtn;
        private Text _stockBtnText;

        private Text _warpModeText;
        private Text _warpRateText;
        private Button _downBtn;
        private Text _downBtnText;
        private Button _upBtn;
        private Text _upBtnText;
        private Button _cancelBtn;
        private Text _cancelBtnText;

        private const int MaxChevronCount = 8;
        private readonly Image[] _chevronImgs = new Image[MaxChevronCount];
        private readonly Button[] _chevronBtns = new Button[MaxChevronCount];

        private Text _secClockText;
        private Text _warpTagText;

        // ==========================================
        // 右仓：通信网络与天线遥测 HUD (CommNet & Antenna Bay HUD)
        // ==========================================
        private GameObject _rightBayGo;
        private Image _rightBayBg;
        private Outline _rightBayOutline;
        private GameObject _commHeaderStripeGo;
        private Image _commHeaderStripe;
        private GameObject _hwSlotGo;
        private Image _hwSlotBg;

        private Text _commTitleText;
        private Text _commPercentText;
        private GameObject _ctrlBadgeBgGo;
        private Image _ctrlBadgeBg;
        private Text _ctrlBadgeText;

        private Text _routeText;
        private Text _rateText;
        private Text _txText;
        private Text _rxText;

        private const int RfBarCount = 5;
        private readonly Image[] _rfBars = new Image[RfBarCount];

        private Text _hwSummaryText;

        // 展开模式下的独立天线行
        private class AntennaRowUI
        {
            public GameObject Root;
            public Text DotText;
            public Text NameText;
            public Text StatusText;
        }
        private const int MaxExpandedAntennaRows = 4;
        private readonly AntennaRowUI[] _expandedAntennaRows = new AntennaRowUI[MaxExpandedAntennaRows];

        // ==========================================
        // 智能私有防抖脏检缓存 (MFP-SPEC-009)
        // ==========================================
        private readonly Cached<string> _dirtyClockStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtySecClockStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyModeBtnStr = new Cached<string>(string.Empty);
        private readonly Cached<bool> _dirtyPausedState = new Cached<bool>(false);
        private readonly Cached<bool> _dirtyPhysState = new Cached<bool>(false);
        private readonly CachedDouble _dirtyWarpRate = new CachedDouble(-1.0, tolerance: 0.001);
        private readonly Cached<int> _dirtyActiveWarpIdx = new Cached<int>(-1);
        private readonly Cached<int> _dirtyMaxWarpIdx = new Cached<int>(-1);

        private readonly Cached<string> _dirtyPercentStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyCtrlBadgeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyRouteStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyRateStr = new Cached<string>(string.Empty);
        private readonly Cached<int> _dirtyActiveRfBars = new Cached<int>(-1);
        private readonly Cached<string> _dirtyHwSummaryStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyStockBtnStr = new Cached<string>(string.Empty);
        private readonly Cached<bool> _dirtyConnected = new Cached<bool>(false);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float baseW = BaseSize.x * s;
            float baseH = BaseSize.y * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            // 1. 基类已自动挂载底板，此处执行色彩微调
            if (CardBackground != null)
            {
                CardBackground.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
            }
            if (CardOutline != null)
            {
                CardOutline.effectDistance = new Vector2(1f * s, 1f * s);
                CardOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }

            // 2. 双仓模块底盘与中央微缝 (Differentiated Sub-Bay Chassis)
            float bayMarginH = 3.5f * s;
            float bayH = baseH - bayMarginH * 2f;
            float bayGap = 4f * s;
            float leftBayW = Mathf.Round((baseW - 8f * s - bayGap) * 0.495f);
            float rightBayW = baseW - 8f * s - bayGap - leftBayW;

            // 左仓：战术时钟与加速控制底盘 (深色下沉战术面板风格)
            _leftBayGo = UIFactory.CreatePanel(transform, "LeftBay_ChronoConsole",
                new Vector2(leftBayW, bayH), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);
            _leftBayBg = _leftBayGo.GetComponent<Image>();
            _leftBayOutline = _leftBayGo.GetComponent<Outline>();

            // 右仓：航电通信网络 HUD 底盘 (半透科技玻璃风格)
            _rightBayGo = UIFactory.CreatePanel(transform, "RightBay_CommHUD",
                new Vector2(rightBayW, bayH), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Panel, theme),
                WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Faint), 1f * s);
            _rightBayBg = _rightBayGo.GetComponent<Image>();
            _rightBayOutline = _rightBayGo.GetComponent<Outline>();

            // 中央分割缝细线
            _centerDivider = UIFactory.CreatePanel(transform, "CenterDivider",
                new Vector2(1f * s, baseH - 10f * s), Vector2.zero,
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost));
            _dividerLine = _centerDivider.GetComponent<Image>();

            // 右仓顶栏 HUD 科技微光条
            _commHeaderStripeGo = UIFactory.CreatePanel(transform, "CommHeaderStripe",
                new Vector2(rightBayW - 4f * s, 2f * s), Vector2.zero,
                WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.70f));
            _commHeaderStripe = _commHeaderStripeGo.GetComponent<Image>();

            // 数字时钟专用内嵌仪表框 (LCD Clock Plate Bezel)
            _clockPlateGo = UIFactory.CreatePanel(transform, "ClockPlate",
                new Vector2(118f * s, 17f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);
            _clockPlateBg = _clockPlateGo.GetComponent<Image>();
            _clockPlateOutline = _clockPlateGo.GetComponent<Outline>();

            // 8 级多段加速指示凹槽 (Chevrons Track)
            _chevronsTrackGo = UIFactory.CreatePanel(transform, "ChevronsTrack",
                new Vector2(80f * s, 10f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _chevronsTrackBg = _chevronsTrackGo.GetComponent<Image>();

            // 右仓天线硬件遥测微槽 (Hardware Slot)
            _hwSlotGo = UIFactory.CreatePanel(transform, "HwSlot",
                new Vector2(rightBayW - 10f * s, 14f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _hwSlotBg = _hwSlotGo.GetComponent<Image>();

            // ==========================================
            // 3. 构建左仓 UI：时间加速与任务时钟
            // ==========================================
            // 时钟模式切换键 (MET / UT)
            Vector2 modeBtnSize = new Vector2(30f * s, 15f * s);
            _modeBtn = UIFactory.CreateButton(transform, "Btn_ClockMode", modeBtnSize, Vector2.zero, OnToggleMode);
            _modeBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeBtnText = UIFactory.CreateText(_modeBtn.transform, "Text", "MET",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _modeBtnText.fontStyle = FontStyle.Bold;
            _modeBtnText.GetComponent<RectTransform>().sizeDelta = modeBtnSize;

            // 数字任务时钟主读数
            _clockText = UIFactory.CreateText(transform, "ClockText", I18n.Tr("WIDGET_TW_CLOCK_PLACEHOLDER", "T+ 0y, 0d, 00:00:00"),
                Mathf.Max(9, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleCenter, textPrimary);
            _clockText.fontStyle = FontStyle.Bold;

            // 暂停/继续控制键
            Vector2 pauseBtnSize = new Vector2(34f * s, 15f * s);
            _pauseBtn = UIFactory.CreateButton(transform, "Btn_Pause", pauseBtnSize, Vector2.zero, OnTogglePause);
            _pauseBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _pauseBtnText = UIFactory.CreateText(_pauseBtn.transform, "Text", I18n.Tr("WIDGET_TIMEWARP_PAUSE", "暂停"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _pauseBtnText.GetComponent<RectTransform>().sizeDelta = pauseBtnSize;

            // 原版顶部栏显隐联动按键 (KSP)
            Vector2 stockBtnSize = new Vector2(20f * s, 15f * s);
            _stockBtn = UIFactory.CreateButton(transform, "Btn_Stock", stockBtnSize, Vector2.zero, OnToggleStock);
            _stockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stockBtnText.GetComponent<RectTransform>().sizeDelta = stockBtnSize;

            // 加速模式提示 (WARP / PHYS)
            _warpModeText = UIFactory.CreateText(transform, "WarpMode", "WARP",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);

            // 加速倍率读数 (1x / 10,000x)
            _warpRateText = UIFactory.CreateText(transform, "WarpRate", "1x",
                Mathf.Max(7, Mathf.RoundToInt(9f * s)), TextAnchor.MiddleLeft, primaryAccent);
            _warpRateText.fontStyle = FontStyle.Bold;

            // 步退减速键 (◀)
            Vector2 stepBtnSize = new Vector2(14f * s, 14f * s);
            _downBtn = UIFactory.CreateButton(transform, "Btn_Down", stepBtnSize, Vector2.zero, OnStepWarpDown);
            _downBtn.GetComponent<Image>().color = Color.clear;
            _downBtnText = UIFactory.CreateText(_downBtn.transform, "Text", "◀",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _downBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 8 个多级加速指示光段 (Chevrons)
            for (int i = 0; i < MaxChevronCount; i++)
            {
                int index = i;
                GameObject chGo = UIFactory.CreatePanel(transform, $"Chevron_{i}",
                    new Vector2(8.5f * s, 6f * s), Vector2.zero,
                    WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Faint));

                Button btn = chGo.AddComponent<Button>();
                btn.onClick.AddListener(() => OnSetWarpIndex(index));

                _chevronImgs[i] = chGo.GetComponent<Image>();
                _chevronBtns[i] = btn;
            }

            // 步进加速键 (▶)
            _upBtn = UIFactory.CreateButton(transform, "Btn_Up", stepBtnSize, Vector2.zero, OnStepWarpUp);
            _upBtn.GetComponent<Image>().color = Color.clear;
            _upBtnText = UIFactory.CreateText(_upBtn.transform, "Text", "▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _upBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 瞬时归一键 (1X Kill-Warp)
            Vector2 cancelBtnSize = new Vector2(22f * s, 14f * s);
            _cancelBtn = UIFactory.CreateButton(transform, "Btn_Cancel", cancelBtnSize, Vector2.zero, OnCancelWarp);
            _cancelBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _cancelBtnText = UIFactory.CreateText(_cancelBtn.transform, "Text", "1X",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _cancelBtnText.fontStyle = FontStyle.Bold;
            _cancelBtnText.GetComponent<RectTransform>().sizeDelta = cancelBtnSize;

            // 次级时钟读数 (UT / MET 双读数)
            _secClockText = UIFactory.CreateText(transform, "SecClockText", "UT 0y, 0d, 00:00:00",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));

            // 物理/轨道模式微标
            _warpTagText = UIFactory.CreateText(transform, "WarpTagText", I18n.Tr("WIDGET_TIME_COMM_MODE_ORBIT", "● 轨道加速"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Label, theme));

            // ==========================================
            // 4. 构建右仓 UI：通信网络与天线探针
            // ==========================================
            // 标题
            _commTitleText = UIFactory.CreateText(transform, "CommTitle", I18n.Tr("WIDGET_SIG_COMMNET", "通信网络"),
                Mathf.Max(8, Mathf.RoundToInt(9f * s)), TextAnchor.MiddleLeft, textPrimary);
            _commTitleText.fontStyle = FontStyle.Normal;

            // 信号百分比 (97%)
            _commPercentText = UIFactory.CreateText(transform, "CommPercent", "100%",
                Mathf.Max(8, Mathf.RoundToInt(10f * s)), TextAnchor.MiddleRight, primaryAccent);
            _commPercentText.fontStyle = FontStyle.Bold;

            // 控制权状态徽章 (● 全权控制)
            Vector2 ctrlSize = new Vector2(62f * s, 14f * s);
            _ctrlBadgeBgGo = UIFactory.CreatePanel(transform, "CtrlBadge", ctrlSize, Vector2.zero,
                WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = _ctrlBadgeBgGo.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(_ctrlBadgeBgGo.transform, "Text", "● " + I18n.Tr("WIDGET_SIG_FULL_CONTROL", "满格控制"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 链路拓扑与中继跳数
            _routeText = UIFactory.CreateText(transform, "RouteText", I18n.Tr("WIDGET_SIG_DIRECT_HOME_DSN", "直连 · 深空网主站"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));

            // 传输速率
            _rateText = UIFactory.CreateText(transform, "RateText", "252.0 Kbps",
                Mathf.Max(7, Mathf.RoundToInt(9f * s)), TextAnchor.MiddleRight, textPrimary);
            _rateText.fontStyle = FontStyle.Bold;

            // 收发微光标签 (▲发射)
            _txText = UIFactory.CreateText(transform, "TxIndicator", "▲" + I18n.Tr("WIDGET_SIG_TX", "发射"),
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _txText.fontStyle = FontStyle.Bold;

            // 收发微光标签 (▼接收)
            _rxText = UIFactory.CreateText(transform, "RxIndicator", "▼" + I18n.Tr("WIDGET_SIG_RX", "接收"),
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _rxText.fontStyle = FontStyle.Bold;

            // 5 阶射频光柱
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < RfBarCount; b++)
            {
                GameObject bGo = UIFactory.CreatePanel(transform, $"RfBar_{b}", Vector2.one, Vector2.zero, trackCol);
                _rfBars[b] = bGo.GetComponent<Image>();
            }

            // 单行天线硬件遥测摘要
            _hwSummaryText = UIFactory.CreateText(transform, "HwSummary", "● " + I18n.Tr("WIDGET_SIG_COMMUNOTRON", "通信模块 16 (1/3 活动)"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));

            // 展开天线矩阵清单
            for (int i = 0; i < MaxExpandedAntennaRows; i++)
            {
                _expandedAntennaRows[i] = CreateAntennaRow(transform, $"AntRow_{i}", s, theme);
            }

            // 5. 初始化原版时间与通信栏状态
            _stockHidden = ThemeManager.IsStockTimeWarpHidden && ThemeManager.IsStockCommNetHidden;
            NavBallHookService.HideStockTimeWarpAction?.Invoke(_stockHidden);
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden);

            // 6. 执行几何排版与坐标锚定 (必须在微控件注册前完成，确保捕获真实设计默认坐标)
            ApplyLayout(baseW, baseH);

            // 7. 标准化微控件注册
            this.Controls.Register(new WidgetActionButtonControl(this, "clock_mode_btn", "任务时钟模式按键", _modeBtn.gameObject, _modeBtn, _modeBtn.GetComponent<Image>(), null, _modeBtnText, null, "MET", OnToggleMode, true));
            this.Controls.Register(new WidgetReadoutControl("clock_readout", "数字时钟读数", _clockText.gameObject, _clockText, _secClockText, TextStyleRole.PrimaryValue, "{MET}"));
            this.Controls.Register(new WidgetActionButtonControl(this, "pause_btn", "暂停控制按键", _pauseBtn.gameObject, _pauseBtn, _pauseBtn.GetComponent<Image>(), null, _pauseBtnText, null, "PAUSE", OnTogglePause, true));
            this.Controls.Register(new WidgetActionButtonControl(this, "stock_btn", "原版UI切换按键", _stockBtn.gameObject, _stockBtn, _stockBtn.GetComponent<Image>(), null, _stockBtnText, null, "KSP", OnToggleStock, true));
            this.Controls.Register(new WidgetActionButtonControl(this, "cancel_warp_btn", "1X瞬时归一按键", _cancelBtn.gameObject, _cancelBtn, _cancelBtn.GetComponent<Image>(), null, _cancelBtnText, null, "1X", OnCancelWarp, false));
            this.Controls.Register(new WidgetAnnunciatorControl("ctrl_badge", "通信控制权徽章", _ctrlBadgeText != null ? _ctrlBadgeText.gameObject : null, _ctrlBadgeText, null, _ctrlBadgeBg, null));
            this.Controls.Register(new WidgetReadoutControl("comm_percent_readout", "通信信号强度", _commPercentText != null ? _commPercentText.gameObject : null, _commPercentText, null, TextStyleRole.PrimaryValue, "{COMM}"));
        }

        private AntennaRowUI CreateAntennaRow(Transform parent, string name, float s, ThemeConfig theme)
        {
            AntennaRowUI row = new AntennaRowUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            RectTransform rt = CreateContainer(name, parent);
            row.Root = rt.gameObject;

            row.DotText = UIFactory.CreateText(row.Root.transform, "Dot", "●",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));

            row.NameText = UIFactory.CreateText(row.Root.transform, "Name", I18n.Tr("WIDGET_SIG_ANTENNA", "天线"),
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));

            row.StatusText = UIFactory.CreateText(row.Root.transform, "Status", I18n.Tr("WIDGET_SIG_LINKED", "已链接"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            row.StatusText.fontStyle = FontStyle.Bold;

            row.Root.SetActiveSafe(false);
            return row;
        }

        // ==========================================
        // 动态自适应排版核心 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayout(pixelSize.x, pixelSize.y);
        }

        private void ApplyLayout(float width, float height)
        {
            if (_clockText == null || _commTitleText == null) return;

            float s = CurrentDpiScale;
            bool isCompactHeight = height < 54f * s;
            bool isExpandedHeight = height >= 88f * s;

            // 1. 双仓底盘尺寸与中央缝隙布局
            float bayMarginH = 3.5f * s;
            float bayH = height - bayMarginH * 2f;
            float bayGap = 4f * s;
            float leftBayW = Mathf.Round((width - 8f * s - bayGap) * 0.495f);
            float rightBayW = width - 8f * s - bayGap - leftBayW;

            float bayLeftX = -width * 0.5f + 4f * s + leftBayW * 0.5f;
            float bayRightX = width * 0.5f - 4f * s - rightBayW * 0.5f;

            if (_leftBayGo != null)
            {
                RectTransform lbRt = _leftBayGo.GetComponent<RectTransform>();
                lbRt.sizeDelta = new Vector2(leftBayW, bayH);
                lbRt.anchoredPosition = new Vector2(bayLeftX, 0f);
            }

            if (_rightBayGo != null)
            {
                RectTransform rbRt = _rightBayGo.GetComponent<RectTransform>();
                rbRt.sizeDelta = new Vector2(rightBayW, bayH);
                rbRt.anchoredPosition = new Vector2(bayRightX, 0f);
            }

            // 中央分割缝细线
            if (_centerDivider != null)
            {
                RectTransform divRt = _centerDivider.GetComponent<RectTransform>();
                divRt.sizeDelta = new Vector2(1f * s, height - 10f * s);
                divRt.anchoredPosition = new Vector2(bayLeftX + leftBayW * 0.5f + bayGap * 0.5f, 0f);
            }

            // 右仓顶栏 HUD 科技微光条
            if (_commHeaderStripeGo != null)
            {
                RectTransform hsRt = _commHeaderStripeGo.GetComponent<RectTransform>();
                hsRt.sizeDelta = new Vector2(rightBayW - 4f * s, 2f * s);
                hsRt.anchoredPosition = new Vector2(bayRightX, bayH * 0.5f - 1.5f * s);
            }

            // 行高与垂直锚点 (Y 轴中心为 0)
            float y1 = isCompactHeight ? (8f * s) : (height * 0.5f - 14.5f * s);
            float y2 = isCompactHeight ? (-8f * s) : 0f;
            float y3 = -height * 0.5f + 14.5f * s;

            // ------------------------------------------
            // 2. 左仓排版 (Tactical Chrono & Warp Console)
            // ------------------------------------------
            float leftInnerStart = bayLeftX - leftBayW * 0.5f + 5f * s;
            float leftInnerEnd = bayLeftX + leftBayW * 0.5f - 5f * s;

            // Row 1: ModeBtn | ClockPlate & Text | PauseBtn | StockBtn
            if (_modeBtn != null)
            {
                RectTransform mbRt = _modeBtn.GetComponent<RectTransform>();
                mbRt.sizeDelta = new Vector2(28f * s, 15f * s);
                mbRt.anchoredPosition = new Vector2(leftInnerStart + 14f * s, y1);
            }

            float clkPlateW = Mathf.Clamp(leftBayW - 120f * s, 85f * s, 122f * s);
            float centerClockX = leftInnerStart + 31f * s + clkPlateW * 0.5f;

            if (_clockPlateGo != null)
            {
                RectTransform cpRt = _clockPlateGo.GetComponent<RectTransform>();
                cpRt.sizeDelta = new Vector2(clkPlateW, 17f * s);
                cpRt.anchoredPosition = new Vector2(centerClockX, y1);
            }

            if (_clockText != null)
            {
                RectTransform clkRt = _clockText.GetComponent<RectTransform>();
                clkRt.sizeDelta = new Vector2(clkPlateW - 4f * s, 15f * s);
                clkRt.anchoredPosition = new Vector2(centerClockX, y1);
            }

            if (_stockBtn != null)
            {
                RectTransform stkRt = _stockBtn.GetComponent<RectTransform>();
                stkRt.sizeDelta = new Vector2(22f * s, 15f * s);
                stkRt.anchoredPosition = new Vector2(leftInnerEnd - 11f * s, y1);
            }

            if (_pauseBtn != null)
            {
                RectTransform psRt = _pauseBtn.GetComponent<RectTransform>();
                psRt.sizeDelta = new Vector2(34f * s, 15f * s);
                psRt.anchoredPosition = new Vector2(leftInnerEnd - 22f * s - 3f * s - 17f * s, y1);
            }

            // Row 2: WarpLabel | WarpRate | [◀] | Chevrons Track | [▶] | [1X]
            if (_warpModeText != null && _warpRateText != null)
            {
                RectTransform wmRt = _warpModeText.GetComponent<RectTransform>();
                wmRt.sizeDelta = new Vector2(26f * s, 14f * s);
                wmRt.anchoredPosition = new Vector2(leftInnerStart + 13f * s, y2);

                RectTransform wrRt = _warpRateText.GetComponent<RectTransform>();
                wrRt.sizeDelta = new Vector2(26f * s, 14f * s);
                wrRt.anchoredPosition = new Vector2(leftInnerStart + 40f * s, y2);
            }

            if (_downBtn != null)
            {
                RectTransform dnRt = _downBtn.GetComponent<RectTransform>();
                dnRt.sizeDelta = new Vector2(13f * s, 13f * s);
                dnRt.anchoredPosition = new Vector2(leftInnerStart + 64f * s, y2);
            }

            float trackW = 80f * s;
            float trackStartX = leftInnerStart + 76f * s;
            float trackCenterX = trackStartX + trackW * 0.5f;

            if (_chevronsTrackGo != null)
            {
                RectTransform trkRt = _chevronsTrackGo.GetComponent<RectTransform>();
                trkRt.sizeDelta = new Vector2(trackW, 10f * s);
                trkRt.anchoredPosition = new Vector2(trackCenterX, y2);
            }

            float chevronW = 8f * s;
            float chevronH = 6f * s;
            float chevronGap = 2f * s;
            float chevronStartX = trackStartX + 1f * s + chevronW * 0.5f;

            for (int i = 0; i < MaxChevronCount; i++)
            {
                if (_chevronImgs[i] != null)
                {
                    float cx = chevronStartX + i * (chevronW + chevronGap);
                    RectTransform cRt = _chevronImgs[i].GetComponent<RectTransform>();
                    cRt.sizeDelta = new Vector2(chevronW, chevronH);
                    cRt.anchoredPosition = new Vector2(cx, y2);
                }
            }

            if (_upBtn != null)
            {
                RectTransform upRt = _upBtn.GetComponent<RectTransform>();
                upRt.sizeDelta = new Vector2(13f * s, 13f * s);
                upRt.anchoredPosition = new Vector2(trackStartX + trackW + 8f * s, y2);
            }

            if (_cancelBtn != null)
            {
                RectTransform cnRt = _cancelBtn.GetComponent<RectTransform>();
                cnRt.sizeDelta = new Vector2(22f * s, 14f * s);
                cnRt.anchoredPosition = new Vector2(leftInnerEnd - 12f * s, y2);
            }

            // Row 3: Secondary Clock & Warp Tag
            if (_secClockText != null && _warpTagText != null)
            {
                _secClockText.gameObject.SetActiveSafe(!isCompactHeight);
                _warpTagText.gameObject.SetActiveSafe(!isCompactHeight);
                if (!isCompactHeight)
                {
                    float secW = leftBayW - 100f * s;
                    RectTransform scRt = _secClockText.GetComponent<RectTransform>();
                    scRt.sizeDelta = new Vector2(secW, 13f * s);
                    scRt.anchoredPosition = new Vector2(leftInnerStart + secW * 0.5f, y3);

                    RectTransform wtRt = _warpTagText.GetComponent<RectTransform>();
                    wtRt.sizeDelta = new Vector2(80f * s, 13f * s);
                    wtRt.anchoredPosition = new Vector2(leftInnerEnd - 40f * s, y3);
                }
            }

            // ------------------------------------------
            // 3. 右仓排版 (CommNet & Telemetry Glass HUD)
            // ------------------------------------------
            float rightInnerStart = bayRightX - rightBayW * 0.5f + 5f * s;
            float rightInnerEnd = bayRightX + rightBayW * 0.5f - 5f * s;

            // Row 1: CommTitle | CommPercent | CtrlBadge
            if (_commTitleText != null)
            {
                RectTransform ctRt = _commTitleText.GetComponent<RectTransform>();
                ctRt.sizeDelta = new Vector2(68f * s, 16f * s);
                ctRt.anchoredPosition = new Vector2(rightInnerStart + 34f * s, y1);
            }

            if (_ctrlBadgeBgGo != null)
            {
                RectTransform cbRt = _ctrlBadgeBgGo.GetComponent<RectTransform>();
                cbRt.sizeDelta = new Vector2(64f * s, 15f * s);
                cbRt.anchoredPosition = new Vector2(rightInnerEnd - 32f * s, y1);
            }

            if (_commPercentText != null)
            {
                RectTransform cpRt = _commPercentText.GetComponent<RectTransform>();
                cpRt.sizeDelta = new Vector2(42f * s, 16f * s);
                cpRt.anchoredPosition = new Vector2(rightInnerEnd - 64f * s - 4f * s - 21f * s, y1);
            }

            // Row 2: RouteText | RateText | Tx/Rx | 5 RF Bars
            float barW = 2.4f * s;
            float barGap = 1.4f * s;
            float totalBarsW = RfBarCount * barW + (RfBarCount - 1) * barGap;
            float barsStartX = rightInnerEnd - totalBarsW - 2f * s;
            float barsBaseY = y2 - 5f * s;

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfBars[b] != null)
                {
                    float bH = (4.0f + b * 2.0f) * s;
                    float bx = barsStartX + b * (barW + barGap) + barW * 0.5f;
                    RectTransform bRt = _rfBars[b].GetComponent<RectTransform>();
                    bRt.sizeDelta = new Vector2(barW, bH);
                    bRt.anchoredPosition = new Vector2(bx, barsBaseY + bH * 0.5f);
                }
            }

            if (_txText != null && _rxText != null)
            {
                bool showTxRx = rightBayW >= 180f * s;
                _txText.gameObject.SetActiveSafe(showTxRx);
                _rxText.gameObject.SetActiveSafe(showTxRx);
                if (showTxRx)
                {
                    RectTransform rxRt = _rxText.GetComponent<RectTransform>();
                    rxRt.sizeDelta = new Vector2(20f * s, 12f * s);
                    rxRt.anchoredPosition = new Vector2(barsStartX - 12f * s, y2);

                    RectTransform txRt = _txText.GetComponent<RectTransform>();
                    txRt.sizeDelta = new Vector2(20f * s, 12f * s);
                    txRt.anchoredPosition = new Vector2(barsStartX - 34f * s, y2);
                }
            }

            if (_rateText != null)
            {
                float rateW = 46f * s;
                RectTransform drRt = _rateText.GetComponent<RectTransform>();
                drRt.sizeDelta = new Vector2(rateW, 14f * s);
                drRt.anchoredPosition = new Vector2(barsStartX - 48f * s - rateW * 0.5f, y2);
            }

            if (_routeText != null)
            {
                float routeW = Mathf.Clamp(rightBayW - 145f * s, 55f * s, 85f * s);
                RectTransform rtRt = _routeText.GetComponent<RectTransform>();
                rtRt.sizeDelta = new Vector2(routeW, 14f * s);
                rtRt.anchoredPosition = new Vector2(rightInnerStart + routeW * 0.5f, y2);
            }

            // Row 3: Hardware Summary (紧凑/标准) 与 展开清单
            bool showHwSummary = !isCompactHeight && !isExpandedHeight;
            if (_hwSlotGo != null)
            {
                _hwSlotGo.SetActiveSafe(showHwSummary);
                if (showHwSummary)
                {
                    RectTransform hsRt = _hwSlotGo.GetComponent<RectTransform>();
                    hsRt.sizeDelta = new Vector2(rightBayW - 10f * s, 14f * s);
                    hsRt.anchoredPosition = new Vector2(bayRightX, y3);
                }
            }

            if (_hwSummaryText != null)
            {
                _hwSummaryText.gameObject.SetActiveSafe(showHwSummary);
                if (showHwSummary)
                {
                    RectTransform hwRt = _hwSummaryText.GetComponent<RectTransform>();
                    hwRt.sizeDelta = new Vector2(rightBayW - 18f * s, 13f * s);
                    hwRt.anchoredPosition = new Vector2(bayRightX, y3);
                }
            }

            // 纵向扩展模式 (展示展开天线清单)
            if (isExpandedHeight)
            {
                float rowStartY = -32f * s;
                float rowH = 15f * s;
                float rowGap = 2f * s;
                float availH = height - 42f * s;
                int maxFit = Mathf.Clamp(Mathf.FloorToInt(availH / (rowH + rowGap)), 1, MaxExpandedAntennaRows);

                for (int i = 0; i < MaxExpandedAntennaRows; i++)
                {
                    var row = _expandedAntennaRows[i];
                    if (row == null || row.Root == null) continue;

                    if (i < maxFit)
                    {
                        row.Root.SetActiveSafe(true);
                        RectTransform rRt = row.Root.GetComponent<RectTransform>();
                        rRt.sizeDelta = new Vector2(rightBayW - 10f * s, rowH);
                        rRt.anchoredPosition = new Vector2(bayRightX, rowStartY - i * (rowH + rowGap));

                        if (row.DotText != null)
                        {
                            RectTransform dRt = row.DotText.GetComponent<RectTransform>();
                            dRt.sizeDelta = new Vector2(10f * s, rowH);
                            dRt.anchoredPosition = new Vector2(-(rightBayW - 10f * s) * 0.5f + 5f * s, 0f);
                        }
                        if (row.NameText != null)
                        {
                            RectTransform nRt = row.NameText.GetComponent<RectTransform>();
                            nRt.sizeDelta = new Vector2(rightBayW - 75f * s, rowH);
                            nRt.anchoredPosition = new Vector2(-(rightBayW - 10f * s) * 0.5f + 14f * s + (rightBayW - 75f * s) * 0.5f, 0f);
                        }
                        if (row.StatusText != null)
                        {
                            RectTransform sRt = row.StatusText.GetComponent<RectTransform>();
                            sRt.sizeDelta = new Vector2(50f * s, rowH);
                            sRt.anchoredPosition = new Vector2((rightBayW - 10f * s) * 0.5f - 25f * s, 0f);
                        }
                    }
                    else
                    {
                        row.Root.SetActiveSafe(false);
                    }
                }
            }
            else
            {
                for (int i = 0; i < MaxExpandedAntennaRows; i++)
                {
                    if (_expandedAntennaRows[i]?.Root != null) _expandedAntennaRows[i].Root.SetActiveSafe(false);
                }
            }
        }

        // ==========================================
        // 渲染与状态驱动循环 (OnRenderState)
        // ==========================================
        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            _telemetry = context.Telemetry;
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            TimeCommHubState state = _logic.CurrentState;
            if (!state.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_currentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // ------------------------------------------
            // 1. 左仓：时钟与加速控制驱动
            // ------------------------------------------
            if (_clockText != null && _dirtyClockStr.Update(state.PrimaryClockStr))
            {
                _clockText.SetTextSafe(state.PrimaryClockStr);
            }

            if (_secClockText != null && _dirtySecClockStr.Update(state.SecondaryClockStr))
            {
                _secClockText.SetTextSafe(state.SecondaryClockStr);
            }

            if (_modeBtnText != null && _dirtyModeBtnStr.Update(state.ModeBtnLabel))
            {
                _modeBtnText.SetTextSafe(state.ModeBtnLabel);
            }

            // 暂停按钮状态
            bool isPaused = state.IsPaused;
            if (_dirtyPausedState.Update(isPaused))
            {
                if (_pauseBtnText != null && _pauseBtn != null)
                {
                    _pauseBtnText.SetTextSafe(isPaused ? I18n.Tr("WIDGET_TIMEWARP_PAUSED", "已暂停") : I18n.Tr("WIDGET_TIMEWARP_PAUSE", "暂停"));
                    _pauseBtn.GetComponent<Image>().color = isPaused
                        ? WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger)
                        : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    _pauseBtnText.color = isPaused
                        ? style.GetTextColor(TextStyleRole.Danger, theme)
                        : style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    _pauseBtnText.fontStyle = isPaused ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            // 物理/轨道加速模式标签
            bool isPhys = state.IsPhys;
            if (_dirtyPhysState.Update(isPhys))
            {
                if (_warpModeText != null)
                {
                    _warpModeText.SetTextSafe(state.WarpModeLabel);
                    ApplyText(_warpModeText, isPhys ? TextStyleRole.Warning : TextStyleRole.Label, theme);
                }
                if (_warpTagText != null)
                {
                    _warpTagText.SetTextSafe(state.WarpTagLabel);
                    ApplyText(_warpTagText, isPhys ? TextStyleRole.Warning : TextStyleRole.Label, theme);
                }
            }

            // 加速倍率读数
            double rate = state.WarpRate;
            if (_warpRateText != null && _dirtyWarpRate.Update(rate))
            {
                string rStr = (rate >= 1000.0) ? $"{rate:N0}x" : ((rate > 1.0) ? $"{rate:0.#}x" : "1x");
                _warpRateText.SetTextSafe(rStr);
                ApplyText(_warpRateText, (rate > 1.0) ? (isPhys ? TextStyleRole.Warning : TextStyleRole.Accent) : TextStyleRole.PrimaryValue, theme);
            }

            // 8 级加速光柱
            int activeIndex = state.ActiveWarpIndex;
            int maxIndex = state.MaxWarpIndex;
            bool activeDirty = _dirtyActiveWarpIdx.Update(activeIndex);
            bool maxDirty = _dirtyMaxWarpIdx.Update(maxIndex);

            if (activeDirty || maxDirty)
            {
                MeterStyleRole fillRole = isPhys ? MeterStyleRole.Warning : MeterStyleRole.Primary;
                Color litColor = WidgetStyleManager.Meter(fillRole, theme);
                Color dimColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint);

                for (int i = 0; i < MaxChevronCount; i++)
                {
                    if (_chevronImgs[i] == null) continue;
                    if (i > maxIndex)
                    {
                        _chevronImgs[i].gameObject.SetActiveSafe(false);
                    }
                    else
                    {
                        _chevronImgs[i].gameObject.SetActiveSafe(true);
                        _chevronImgs[i].SetColor((i <= activeIndex) ? litColor : dimColor);
                    }
                }

                // 瞬时归一按钮高亮状态
                if (_cancelBtnText != null && _cancelBtn != null)
                {
                    bool canCancel = activeIndex > 0;
                    _cancelBtn.GetComponent<Image>().color = canCancel
                        ? WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution)
                        : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    _cancelBtnText.color = canCancel
                        ? style.GetTextColor(TextStyleRole.Warning, theme)
                        : style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                }
            }

            // 原版切换按钮
            if (_stockBtnText != null)
            {
                string stockStr = _stockHidden ? "KSP" : "MFP";
                if (_dirtyStockBtnStr.Update(stockStr))
                {
                    _stockBtnText.SetTextSafe(stockStr);
                    ApplyText(_stockBtnText, _stockHidden ? TextStyleRole.SecondaryValue : TextStyleRole.Accent, theme);
                }
            }

            // ------------------------------------------
            // 2. 右仓：通信网络与天线探针驱动
            // ------------------------------------------
            bool isConnected = state.IsConnected;
            double commSig = state.CommSignal;

            if (_commPercentText != null && _dirtyPercentStr.Update(state.SignalPercentStr))
            {
                _commPercentText.SetTextSafe(state.SignalPercentStr);
            }

            if (_dirtyCtrlBadgeStr.Update(state.ControlBadgeText))
            {
                if (_ctrlBadgeText != null) _ctrlBadgeText.SetTextSafe(state.ControlBadgeText);
                if (_ctrlBadgeBg != null) _ctrlBadgeBg.SetColor(WidgetStyleManager.StatusPanel(state.ControlBadgeRole));
            }

            // 控制权徽章微光呼吸动画
            if (_ctrlBadgeText != null)
            {
                TextStyleRole textRole = !isConnected ? TextStyleRole.Danger : (state.ControlBadgeRole == StatusSurfaceRole.Caution ? TextStyleRole.Warning : TextStyleRole.Accent);
                Color ctrlCol = style.GetTextColor(textRole, theme);
                float animAlpha = isConnected ? (0.82f + 0.18f * Mathf.Sin(Time.time * 2.8f)) : (0.72f + 0.28f * Mathf.Sin(Time.time * 2.0f));
                _ctrlBadgeText.SetColor(WidgetStyleManager.WithAlpha(ctrlCol, animAlpha));
            }

            if (_routeText != null && _dirtyRouteStr.Update(state.RouteDesc))
            {
                _routeText.SetTextSafe(state.RouteDesc);
            }

            if (_rateText != null && _dirtyRateStr.Update(state.RateStr))
            {
                _rateText.SetTextSafe(state.RateStr);
            }

            // Tx/Rx 遥测收发微光动画
            if (_txText != null && _rxText != null && _txText.gameObject.activeSelf)
            {
                Color txBase = style.GetTextColor(TextStyleRole.Accent, theme);
                Color rxBase = style.GetTextColor(TextStyleRole.Cardinal, theme);

                if (isConnected && (state.RateStr != "0.0 bps" || commSig > 0.01))
                {
                    float txA = state.HasTx ? (0.60f + 0.40f * Mathf.Sin(Time.time * 6.5f)) : 0.30f;
                    float rxA = state.HasRx ? (0.60f + 0.40f * Mathf.Cos(Time.time * 6.5f)) : 0.30f;
                    _txText.SetColor(WidgetStyleManager.WithAlpha(txBase, txA));
                    _rxText.SetColor(WidgetStyleManager.WithAlpha(rxBase, rxA));
                }
                else
                {
                    _txText.SetColor(WidgetStyleManager.WithAlpha(txBase, 0.20f));
                    _rxText.SetColor(WidgetStyleManager.WithAlpha(rxBase, 0.20f));
                }
            }

            // 5 阶 RF Bars 光柱
            int activeRfBars = state.ActiveRfBars;
            if (_dirtyActiveRfBars.Update(activeRfBars) || _dirtyConnected.Update(isConnected))
            {
                MeterStyleRole barRole = !isConnected ? MeterStyleRole.Track : (commSig < 0.35 ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                Color activeCol = style.GetMeterColor(barRole, theme);
                Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);

                for (int b = 0; b < RfBarCount; b++)
                {
                    if (_rfBars[b] != null)
                    {
                        if (b < activeRfBars)
                        {
                            if (b == activeRfBars - 1 && isConnected)
                            {
                                float shimmer = 0.84f + 0.16f * Mathf.Sin(Time.time * 3.5f);
                                _rfBars[b].SetColor(WidgetStyleManager.WithAlpha(activeCol, shimmer));
                            }
                            else
                            {
                                _rfBars[b].SetColor(activeCol);
                            }
                        }
                        else
                        {
                            _rfBars[b].SetColor(trackCol);
                        }
                    }
                }
            }

            // 硬件信息单行摘要
            if (_hwSummaryText != null && _hwSummaryText.gameObject.activeSelf)
            {
                if (_dirtyHwSummaryStr.Update(state.HwSummary))
                {
                    _hwSummaryText.SetTextSafe(state.HwSummary);
                }
            }

            // 展开模式：天线阵列清单
            for (int i = 0; i < MaxExpandedAntennaRows; i++)
            {
                var row = _expandedAntennaRows[i];
                if (row == null || row.Root == null || !row.Root.activeSelf) continue;

                var snapshot = state.GetAntenna(i);
                if (snapshot.IsActive)
                {
                    row.NameText.SetTextSafe(snapshot.Name);
                    row.StatusText.SetTextSafe(LocalizeAntennaStatus(snapshot.Status));

                    TextStyleRole sRole = (snapshot.Status == "LINKED") ? TextStyleRole.Accent :
                        (snapshot.Status == "STANDBY" ? TextStyleRole.Cardinal : TextStyleRole.SecondaryValue);

                    ApplyText(row.StatusText, sRole, theme);
                    ApplyText(row.DotText, sRole, theme);
                }
                else
                {
                    row.Root.SetActiveSafe(false);
                }
            }
        }

        private static string LocalizeAntennaStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return status;
            switch (status.Trim().ToUpperInvariant())
            {
                case "LINKED": return I18n.Tr("WIDGET_SIG_LINKED", "已链接");
                case "STANDBY": return I18n.Tr("WIDGET_SIG_STANDBY", "待机");
                case "SEARCHING": return I18n.Tr("WIDGET_SIG_SEARCHING", "搜索中");
                case "RETRACTED": return I18n.Tr("WIDGET_SIG_RETRACTED", "已收回");
                case "DEPLOYING": return I18n.Tr("WIDGET_SIG_DEPLOYING", "展开中");
                case "BROKEN": return I18n.Tr("WIDGET_SIG_BROKEN", "损坏");
                case "OFFLINE": return I18n.Tr("WIDGET_SIG_OFFLINE", "离线");
                case "NONE": return I18n.Tr("WIDGET_SIGNAL_NONE", "无");
                default: return status;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (CardBackground != null)
            {
                CardBackground.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
            }
            if (CardOutline != null)
            {
                CardOutline.enabled = true;
                CardOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }
            if (_dividerLine != null)
            {
                _dividerLine.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }

            // 双仓外壳与嵌入式底盘色彩
            if (_leftBayBg != null)
            {
                _leftBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            }
            if (_leftBayOutline != null)
            {
                _leftBayOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }
            if (_rightBayBg != null)
            {
                _rightBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Panel, theme);
            }
            if (_rightBayOutline != null)
            {
                _rightBayOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Faint);
            }
            if (_commHeaderStripe != null)
            {
                _commHeaderStripe.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.70f);
            }
            if (_clockPlateBg != null)
            {
                _clockPlateBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (_clockPlateOutline != null)
            {
                _clockPlateOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }
            if (_chevronsTrackBg != null)
            {
                _chevronsTrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (_hwSlotBg != null)
            {
                _hwSlotBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }

            // 左仓色彩
            ApplyText(_clockText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_secClockText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_warpModeText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_warpRateText, TextStyleRole.Accent, theme);
            ApplyText(_warpTagText, TextStyleRole.Label, theme);

            ApplyText(_modeBtnText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_downBtnText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_upBtnText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_cancelBtnText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_stockBtnText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_pauseBtnText, TextStyleRole.SecondaryValue, theme);

            if (_modeBtn != null) _modeBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_pauseBtn != null) _pauseBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockBtn != null) _stockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_cancelBtn != null) _cancelBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            Material btnMat = style.GetUiMaterial(isText: false);
            if (_modeBtn != null) _modeBtn.GetComponent<Image>().material = btnMat;
            if (_pauseBtn != null) _pauseBtn.GetComponent<Image>().material = btnMat;
            if (_stockBtn != null) _stockBtn.GetComponent<Image>().material = btnMat;
            if (_cancelBtn != null) _cancelBtn.GetComponent<Image>().material = btnMat;

            // 右仓色彩
            ApplyText(_commTitleText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_commPercentText, TextStyleRole.Accent, theme);
            ApplyText(_routeText, TextStyleRole.Cardinal, theme);
            ApplyText(_rateText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_txText, TextStyleRole.Accent, theme);
            ApplyText(_rxText, TextStyleRole.Cardinal, theme);
            ApplyText(_hwSummaryText, TextStyleRole.SecondaryValue, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfBars[b] != null) _rfBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }

            for (int i = 0; i < MaxExpandedAntennaRows; i++)
            {
                var row = _expandedAntennaRows[i];
                if (row == null || row.Root == null) continue;
                if (row.DotText != null) ApplyText(row.DotText, TextStyleRole.Accent, theme);
                if (row.NameText != null) ApplyText(row.NameText, TextStyleRole.PrimaryValue, theme);
                if (row.StatusText != null) ApplyText(row.StatusText, TextStyleRole.Accent, theme);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        // ==========================================
        // 用户交互指令触发 (Actions & Handlers)
        // ==========================================
        private void OnToggleMode()
        {
            _logic.ShowUniversalTime = !_logic.ShowUniversalTime;
            if (_modeBtnText != null)
            {
                _modeBtnText.SetTextSafe(_logic.ShowUniversalTime ? "UT" : "MET");
            }
        }

        private void OnTogglePause()
        {
            _telemetry?.TogglePause();
        }

        private void OnToggleStock()
        {
            _stockHidden = !_stockHidden;
            NavBallHookService.HideStockTimeWarpAction?.Invoke(_stockHidden);
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden);
            ThemeManager.IsStockTimeWarpHidden = _stockHidden;
            ThemeManager.IsStockCommNetHidden = _stockHidden;
            ThemeManager.Instance.SaveSettings();
        }

        private void OnStepWarpDown()
        {
            _telemetry?.DecreaseTimeWarp();
        }

        private void OnStepWarpUp()
        {
            _telemetry?.IncreaseTimeWarp();
        }

        private void OnSetWarpIndex(int idx)
        {
            _telemetry?.SetTimeWarpRateIndex(idx);
        }

        private void OnCancelWarp()
        {
            _telemetry?.CancelTimeWarp();
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _dirtyClockStr.Reset(string.Empty);
            _dirtySecClockStr.Reset(string.Empty);
            _dirtyModeBtnStr.Reset(string.Empty);
            _dirtyPausedState.Reset(false);
            _dirtyPhysState.Reset(false);
            _dirtyWarpRate.Reset(-1.0);
            _dirtyActiveWarpIdx.Reset(-1);
            _dirtyMaxWarpIdx.Reset(-1);
            _dirtyPercentStr.Reset(string.Empty);
            _dirtyCtrlBadgeStr.Reset(string.Empty);
            _dirtyRouteStr.Reset(string.Empty);
            _dirtyRateStr.Reset(string.Empty);
            _dirtyActiveRfBars.Reset(-1);
            _dirtyHwSummaryStr.Reset(string.Empty);
            _dirtyStockBtnStr.Reset(string.Empty);
            _dirtyConnected.Reset(false);
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            _logic.PhysLabel = GetTemplateChannel("PHYS_LABEL", "PHYS");
            _logic.WarpLabel = GetTemplateChannel("WARP_LABEL", "WARP");
            OnResetPrivateCache();
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_modeBtn != null) _modeBtn.onClick.RemoveAllListeners();
            if (_pauseBtn != null) _pauseBtn.onClick.RemoveAllListeners();
            if (_stockBtn != null) _stockBtn.onClick.RemoveAllListeners();
            if (_downBtn != null) _downBtn.onClick.RemoveAllListeners();
            if (_upBtn != null) _upBtn.onClick.RemoveAllListeners();
            if (_cancelBtn != null) _cancelBtn.onClick.RemoveAllListeners();
            for (int i = 0; i < MaxChevronCount; i++)
            {
                if (_chevronBtns[i] != null) _chevronBtns[i].onClick.RemoveAllListeners();
            }
            base.OnDestroy();
        }
    }
}
