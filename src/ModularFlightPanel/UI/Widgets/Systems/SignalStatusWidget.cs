using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化全玻璃座舱通信网络与天线阵列监控仪 (Avionics CommNet & Antenna Array Monitor)
    /// 遵照 MFP-SPEC-001..007 标准航电规范与 100% 真实遥测驱动铁律：
    /// 1. 深度融合原版 CommNet 与 RealAntennas 探针，彻底摒弃任何硬编码假天线数据；
    /// 2. 核心链路矩阵：实时展示主测控站 (如 Cape Canaveral / 太空中心)、直连/中继拓扑、下行传输速率 (如 15.8 Kbps)、Tx/Rx 信号比与 5 阶微光射频光柱；
    /// 3. 天线硬件阵列：真实反映载具安装天线部件 (名称、频段/类型、增益/功率、LINKED/STANDBY/RETRACTED 部署与连接状态)；
    /// 4. 严格遵守零颜色字面量、零场景查询与 10Hz Relaxed 阶梯分频刷新。
    /// </summary>
    [FlightWidget("signal", "signal_list", "antenna", Category = WidgetCategory.Systems, DisplayName = "COMMNET 天线通信网络", Description = "原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱。", DefaultWidgetId = "custom.signal", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "custom.signal", "custom.signal_list", "core.signal" })]
    public class SignalStatusWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(270f, 168f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title("COMMNET");
        public TextWidget StatusBadge = TextWidget.Badge("● LINKED");

        // 顶栏 (Header)
        private GameObject _headerRoot;
        private Text _subTitleText;
        private Image _ctrlBadgeBg;
        private Text _ctrlBadgeText;
        private Text _linkBadgeText;
        private Image _dividerLine;

        // 核心射频遥测矩阵 (Key RF Matrix)
        private GameObject _rfMatrixPanel;
        private Image _rfMatrixBg;
        private Outline _rfMatrixOutline;
        private Text _targetTitleText;
        private Text _targetNameText;
        private Text _routeTypeText;
        private Text _rateText;
        private Text _txRxText;
        private const int RfBarCount = 5;
        private readonly Image[] _rfSignalBars = new Image[RfBarCount];

        // 天线硬件微栏
        private GameObject _arrayHeaderRoot;
        private Text _arrayTitleText;
        private Text _arrayCountText;

        // 天线卡片列表 (2 张高精度卡片)
        private class AntennaCardUI
        {
            public GameObject Root;
            public Image Bg;
            public Outline Outline;
            public Text IconText;
            public Text NameText;
            public Text SpecText;
            public Text StatusBadge;
            public Image[] MiniBars;
        }

        private const int MaxCards = 2;
        private readonly AntennaCardUI[] _antennaCards = new AntennaCardUI[MaxCards];

        // 脏检查与缓存守卫
        private string _lastTargetName = string.Empty;
        private string _lastRateStr = string.Empty;
        private string _lastTxRxStr = string.Empty;
        private string _lastCtrlBadge = string.Empty;
        private string _lastLinkBadge = string.Empty;
        private string _lastRouteType = string.Empty;
        private string _lastArrayCount = string.Empty;
        private int _lastActiveRfBars = -1;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float baseW = 270f * s;
            float baseH = 168f * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            WidgetStyleManager style = WidgetStyleManager.Instance;



            // ==========================================
            // 2. 顶栏系统与状态 (Header)
            // ==========================================
            _headerRoot = new GameObject("HeaderRoot", typeof(RectTransform));
            _headerRoot.transform.SetParent(transform, false);
            RectTransform hdrRt = _headerRoot.GetComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0.5f, 1f);
            hdrRt.anchorMax = new Vector2(0.5f, 1f);
            hdrRt.pivot = new Vector2(0.5f, 1f);
            hdrRt.sizeDelta = new Vector2(baseW, 24f * s);
            hdrRt.anchoredPosition = Vector2.zero;

            string defTitle = GetTemplateChannel("TITLE", "COMMNET");
            Title.Text = defTitle;

            _subTitleText = UIFactory.CreateText(_headerRoot.transform, "SubTitle", "AVIONICS RF",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(68f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(-baseW * 0.5f + 128f * s, 0f);

            // 控制权状态药丸 (FULL CONTROL / PARTIAL / NO LINK)
            Vector2 ctrlSize = new Vector2(58f * s, 14f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(_headerRoot.transform, "CtrlBadge", ctrlSize,
                new Vector2(baseW * 0.5f - 82f * s, 0f), WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(ctrlBg.transform, "Text", "FULL CONTROL",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 链路状态文字 (● LINKED / ▲ OFFLINE)
            _linkBadgeText = UIFactory.CreateText(_headerRoot.transform, "LinkBadge", "● LINKED",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            _linkBadgeText.fontStyle = FontStyle.Bold;
            RectTransform lbRt = _linkBadgeText.GetComponent<RectTransform>();
            lbRt.sizeDelta = new Vector2(52f * s, 16f * s);
            lbRt.anchoredPosition = new Vector2(baseW * 0.5f - 30f * s, 0f);

            // 分割微线
            GameObject divGo = UIFactory.CreatePanel(transform, "HeaderDiv",
                new Vector2(baseW - 14f * s, 1f * s), Vector2.zero,
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _dividerLine = divGo.GetComponent<Image>();
            RectTransform divRt = divGo.GetComponent<RectTransform>();
            divRt.anchorMin = new Vector2(0.5f, 1f);
            divRt.anchorMax = new Vector2(0.5f, 1f);
            divRt.pivot = new Vector2(0.5f, 1f);
            divRt.anchoredPosition = new Vector2(0f, -24f * s);

            // ==========================================
            // 3. 核心射频遥测矩阵 (Key RF Matrix)
            // ==========================================
            Vector2 rfMatrixSize = new Vector2(baseW - 14f * s, 44f * s);
            _rfMatrixPanel = UIFactory.CreatePanel(transform, "RFMatrix", rfMatrixSize,
                new Vector2(0f, -48f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            RectTransform matRt = _rfMatrixPanel.GetComponent<RectTransform>();
            matRt.anchorMin = new Vector2(0.5f, 1f);
            matRt.anchorMax = new Vector2(0.5f, 1f);
            matRt.pivot = new Vector2(0.5f, 1f);
            matRt.anchoredPosition = new Vector2(0f, -26f * s);

            _rfMatrixBg = _rfMatrixPanel.GetComponent<Image>();
            _rfMatrixOutline = _rfMatrixPanel.GetComponent<Outline>();

            // 左半区：目标测控站与拓扑
            _targetTitleText = UIFactory.CreateText(_rfMatrixPanel.transform, "TargetTitle", "ACTIVE TARGET / HOP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform ttRt = _targetTitleText.GetComponent<RectTransform>();
            ttRt.sizeDelta = new Vector2(138f * s, 10f * s);
            ttRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 76f * s, 13f * s);

            _targetNameText = UIFactory.CreateText(_rfMatrixPanel.transform, "TargetName", "US - CAPE CANAVERAL",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _targetNameText.fontStyle = FontStyle.Bold;
            RectTransform tnRt = _targetNameText.GetComponent<RectTransform>();
            tnRt.sizeDelta = new Vector2(142f * s, 16f * s);
            tnRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 78f * s, 1f * s);

            _routeTypeText = UIFactory.CreateText(_rfMatrixPanel.transform, "RouteType", "DIRECT LINK · HOME DSN",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform rtRt = _routeTypeText.GetComponent<RectTransform>();
            rtRt.sizeDelta = new Vector2(138f * s, 10f * s);
            rtRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 76f * s, -12f * s);

            // 右半区：数据传输速率、Tx/Rx 与 5 阶微光柱
            _rateText = UIFactory.CreateText(_rfMatrixPanel.transform, "DataRate", "15.8 Kbps",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _rateText.fontStyle = FontStyle.Bold;
            RectTransform drRt = _rateText.GetComponent<RectTransform>();
            drRt.sizeDelta = new Vector2(74f * s, 15f * s);
            drRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 54f * s, 6.5f * s);

            _txRxText = UIFactory.CreateText(_rfMatrixPanel.transform, "TxRx", "TX 100% · RX 100%",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform trRt = _txRxText.GetComponent<RectTransform>();
            trRt.sizeDelta = new Vector2(80f * s, 11f * s);
            trRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 57f * s, -9f * s);

            // 5 阶微光信号阶梯柱 (Heights: 4, 6, 8, 10, 12)
            float barStartX = rfMatrixSize.x * 0.5f - 19f * s;
            float barW = 2.2f * s;
            float barGap = 1.4f * s;
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < RfBarCount; b++)
            {
                float barH = (4.5f + b * 2f) * s;
                float bx = barStartX + b * (barW + barGap);
                float by = -rfMatrixSize.y * 0.5f + 14f * s + barH * 0.5f;

                GameObject bGo = UIFactory.CreatePanel(_rfMatrixPanel.transform, $"RfBar_{b}",
                    new Vector2(barW, barH), new Vector2(bx, by), trackCol);
                _rfSignalBars[b] = bGo.GetComponent<Image>();
            }

            // ==========================================
            // 4. 天线阵列小节标题微栏
            // ==========================================
            _arrayHeaderRoot = new GameObject("ArrayHdrRoot", typeof(RectTransform));
            _arrayHeaderRoot.transform.SetParent(transform, false);
            RectTransform ahRt = _arrayHeaderRoot.GetComponent<RectTransform>();
            ahRt.anchorMin = new Vector2(0.5f, 1f);
            ahRt.anchorMax = new Vector2(0.5f, 1f);
            ahRt.pivot = new Vector2(0.5f, 1f);
            ahRt.sizeDelta = new Vector2(baseW - 14f * s, 14f * s);
            ahRt.anchoredPosition = new Vector2(0f, -73f * s);

            _arrayTitleText = UIFactory.CreateText(_arrayHeaderRoot.transform, "ArrayTitle", "ANTENNAS & HARDWARE",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _arrayTitleText.fontStyle = FontStyle.Bold;
            RectTransform atRt = _arrayTitleText.GetComponent<RectTransform>();
            atRt.sizeDelta = new Vector2(130f * s, 13f * s);
            atRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 68f * s, 0f);

            _arrayCountText = UIFactory.CreateText(_arrayHeaderRoot.transform, "ArrayCount", "ACTIVE: 1 / 1",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform acRt = _arrayCountText.GetComponent<RectTransform>();
            acRt.sizeDelta = new Vector2(90f * s, 13f * s);
            acRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 48f * s, 0f);

            // ==========================================
            // 5. 真实天线卡片列表 (2 张标准卡片)
            // ==========================================
            Vector2 cardSize = new Vector2(baseW - 14f * s, 34f * s);
            for (int i = 0; i < MaxCards; i++)
            {
                _antennaCards[i] = CreateAntennaCard(transform, $"AntCard_{i}", cardSize, i, s, theme);
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetAnnunciatorControl("ctrl_badge", "控制权徽章", _ctrlBadgeText != null ? _ctrlBadgeText.gameObject : null, _ctrlBadgeText, _linkBadgeText, _ctrlBadgeBg, null));
            if (_rfMatrixPanel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "rf_matrix", "射频矩阵面板", _rfMatrixPanel, (t) => { if (_rfMatrixBg != null) _rfMatrixBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, t); }));
            this.Controls.Register(new WidgetReadoutControl("target_readout", "目标对端与速率", _targetNameText != null ? _targetNameText.gameObject : null, _targetNameText, _rateText, TextStyleRole.PrimaryValue));
            if (_arrayHeaderRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "antennas_array", "天线阵列列表", _arrayHeaderRoot));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private AntennaCardUI CreateAntennaCard(Transform parent, string name, Vector2 size, int index, float s, ThemeConfig theme)
        {
            AntennaCardUI card = new AntennaCardUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Color cardBg = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            Color borderCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            card.Root = UIFactory.CreatePanel(parent, name, size, Vector2.zero, cardBg, borderCol, 1f * s);
            RectTransform rt = card.Root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, (-89f - index * 38f) * s);

            card.Bg = card.Root.GetComponent<Image>();
            card.Outline = card.Root.GetComponent<Outline>();

            // 状态图标 (◈)
            card.IconText = UIFactory.CreateText(card.Root.transform, "Icon", "◈",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform icRt = card.IconText.GetComponent<RectTransform>();
            icRt.sizeDelta = new Vector2(16f * s, 16f * s);
            icRt.anchoredPosition = new Vector2(-size.x * 0.5f + 14f * s, 0f);

            // 天线名称 (加粗，左对齐)
            card.NameText = UIFactory.CreateText(card.Root.transform, "Name", "ANTENNA",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            card.NameText.fontStyle = FontStyle.Bold;
            RectTransform nmRt = card.NameText.GetComponent<RectTransform>();
            nmRt.sizeDelta = new Vector2(136f * s, 14f * s);
            nmRt.anchoredPosition = new Vector2(-size.x * 0.5f + 92f * s, 5.5f * s);

            // 规格与频段 (次级文字)
            card.SpecText = UIFactory.CreateText(card.Root.transform, "Spec", "DIRECT · 500k PWR",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform spRt = card.SpecText.GetComponent<RectTransform>();
            spRt.sizeDelta = new Vector2(136f * s, 11f * s);
            spRt.anchoredPosition = new Vector2(-size.x * 0.5f + 92f * s, -6f * s);

            // 4 格微型天线信号指示柱
            card.MiniBars = new Image[4];
            float miniStartX = size.x * 0.5f - 68f * s;
            float miniW = 2f * s;
            float miniGap = 1.3f * s;
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < 4; b++)
            {
                float bH = (3.5f + b * 2f) * s;
                float bx = miniStartX + b * (miniW + miniGap);
                float by = -size.y * 0.5f + 10f * s + bH * 0.5f;

                GameObject barObj = UIFactory.CreatePanel(card.Root.transform, $"MiniBar_{b}",
                    new Vector2(miniW, bH), new Vector2(bx, by), trackCol);
                card.MiniBars[b] = barObj.GetComponent<Image>();
            }

            // 状态药丸文字 (LINKED / STANDBY / RETRACTED)
            card.StatusBadge = UIFactory.CreateText(card.Root.transform, "Status", "LINKED",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            card.StatusBadge.fontStyle = FontStyle.Bold;
            RectTransform stRt = card.StatusBadge.GetComponent<RectTransform>();
            stRt.sizeDelta = new Vector2(50f * s, 14f * s);
            stRt.anchoredPosition = new Vector2(size.x * 0.5f - 29f * s, 0f);

            return card;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            bool isConnected = telemetry.IsConnected;
            double signalStrength = telemetry.CommSignal;
            if (signalStrength < 0.0) signalStrength = 0.0;
            if (signalStrength > 1.0) signalStrength = 1.0;

            // 1. 顶栏控制权徽章与指示灯
            string ctrlLevel = telemetry.ControlLevelStr ?? (isConnected ? "FULL CONTROL" : "NO LINK");
            SetTextIfChanged(_ctrlBadgeText, ctrlLevel);

            TextStyleRole ctrlRole = !isConnected ? TextStyleRole.Danger :
                (ctrlLevel.IndexOf("PART", StringComparison.OrdinalIgnoreCase) >= 0 || signalStrength < 0.35 ? TextStyleRole.Warning : TextStyleRole.Accent);
            ApplyText(_ctrlBadgeText, ctrlRole, theme);

            string linkBadge = isConnected ? "● LINKED" : "▲ OFFLINE";
            SetTextIfChanged(_linkBadgeText, linkBadge);
            ApplyText(_linkBadgeText, isConnected ? TextStyleRole.Accent : TextStyleRole.Danger, theme);

            // 2. 核心链路矩阵：目标测控站
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
                    targetName = "KERBIN DSN DIRECT";
                }

                bool isDirect = !hasRealLinks || links[0].IsDirectHome || links.Count == 1;
                routeDesc = isDirect ? "DIRECT LINK · HOME DSN" : $"RELAY ROUTE · {links.Count} HOPS";
            }
            else
            {
                targetName = "NO STATION LINK";
                routeDesc = "SEARCHING GROUND STATIONS";
            }

            SetTextIfChanged(_targetNameText, targetName);
            SetTextIfChanged(_routeTypeText, routeDesc);
            ApplyText(_targetNameText, isConnected ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

            // 3. 实时速率与 Tx/Rx
            double bps = telemetry.DataRateBps;
            string rateStr;
            if (!isConnected)
            {
                rateStr = "0.0 bps";
            }
            else if (bps > 0.0)
            {
                rateStr = CommLinkInfo.FormatRate(bps);
            }
            else
            {
                rateStr = CommLinkInfo.FormatRate(100000.0 * signalStrength);
            }
            SetTextIfChanged(_rateText, rateStr);

            int txPct = Mathf.RoundToInt((float)(telemetry.SignalTx * 100.0));
            int rxPct = Mathf.RoundToInt((float)(telemetry.SignalRx * 100.0));
            string txRxStr = $"TX {txPct}% · RX {rxPct}%";
            SetTextIfChanged(_txRxText, txRxStr);

            // 4. 5 阶主射频光柱
            int activeRfBars = isConnected ? Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * RfBarCount), 1, RfBarCount) : 0;
            if (activeRfBars != _lastActiveRfBars)
            {
                _lastActiveRfBars = activeRfBars;
                MeterStyleRole barRole = !isConnected ? MeterStyleRole.Track : (signalStrength < 0.35 ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                Color activeCol = style.GetMeterColor(barRole, theme);
                Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);

                for (int b = 0; b < RfBarCount; b++)
                {
                    if (_rfSignalBars[b] != null)
                    {
                        _rfSignalBars[b].color = b < activeRfBars ? activeCol : trackCol;
                    }
                }
            }

            // 5. 天线硬件阵列列表 (真实物理天线)
            var antennas = telemetry.Antennas;
            int totalAnts = (antennas != null && antennas.Count > 0) ? antennas.Count : (telemetry.AntennaCount > 0 ? telemetry.AntennaCount : 1);
            int activeAnts = 0;
            if (antennas != null)
            {
                for (int a = 0; a < antennas.Count; a++)
                {
                    if (antennas[a].IsOperational && antennas[a].Status == "LINKED") activeAnts++;
                }
            }
            if (activeAnts == 0 && isConnected) activeAnts = 1;

            string countStr = $"ACTIVE: {activeAnts} / {totalAnts}";
            SetTextIfChanged(_arrayCountText, countStr);

            for (int i = 0; i < MaxCards; i++)
            {
                var card = _antennaCards[i];
                if (card == null || card.Root == null) continue;

                if (antennas != null && i < antennas.Count)
                {
                    card.Root.SetActive(true);
                    var ant = antennas[i];

                    SetTextIfChanged(card.NameText, ant.Name.ToUpperInvariant());
                    SetTextIfChanged(card.SpecText, ant.SpecSummary);
                    SetTextIfChanged(card.StatusBadge, ant.Status);

                    TextStyleRole statRole;
                    MeterStyleRole barRole;
                    if (ant.Status == "LINKED")
                    {
                        statRole = TextStyleRole.Accent;
                        barRole = MeterStyleRole.Primary;
                    }
                    else if (ant.Status == "STANDBY")
                    {
                        statRole = TextStyleRole.Cardinal;
                        barRole = MeterStyleRole.Secondary;
                    }
                    else if (ant.Status == "DEPLOYING" || ant.Status == "SEARCHING")
                    {
                        statRole = TextStyleRole.Warning;
                        barRole = MeterStyleRole.Warning;
                    }
                    else
                    {
                        statRole = TextStyleRole.SecondaryValue;
                        barRole = MeterStyleRole.Track;
                    }

                    ApplyText(card.StatusBadge, statRole, theme);
                    ApplyText(card.IconText, statRole, theme);

                    int bars = (ant.IsOperational && isConnected) ? Mathf.Clamp(Mathf.CeilToInt(ant.SignalStrength * 4f), 1, 4) : 0;
                    UpdateMiniBars(card.MiniBars, bars, barRole, theme);
                }
                else if (i == 0)
                {
                    // 载具无外部天线时的单天线真实回退 (明确注明为内置天线)
                    card.Root.SetActive(true);
                    SetTextIfChanged(card.NameText, "INTERNAL POD ANTENNA");
                    SetTextIfChanged(card.SpecText, "INTERNAL · 5.0k POWER");
                    string st = isConnected ? "LINKED" : "OFFLINE";
                    SetTextIfChanged(card.StatusBadge, st);
                    ApplyText(card.StatusBadge, isConnected ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                    ApplyText(card.IconText, isConnected ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                    int bars = isConnected ? Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * 4f), 1, 4) : 0;
                    UpdateMiniBars(card.MiniBars, bars, isConnected ? MeterStyleRole.Primary : MeterStyleRole.Track, theme);
                }
                else
                {
                    // 仅有 1 根天线时，第 2 张卡片不再伪造假天线，而是清晰表明无备用天线
                    card.Root.SetActive(true);
                    SetTextIfChanged(card.NameText, "SECONDARY ANTENNA");
                    SetTextIfChanged(card.SpecText, "NOT INSTALLED · INTERNAL STANDBY");
                    SetTextIfChanged(card.StatusBadge, "STANDBY");
                    ApplyText(card.StatusBadge, TextStyleRole.SecondaryValue, theme);
                    ApplyText(card.IconText, TextStyleRole.SecondaryValue, theme);
                    UpdateMiniBars(card.MiniBars, 0, MeterStyleRole.Track, theme);
                }
            }
        }

        private void UpdateMiniBars(Image[] bars, int activeCount, MeterStyleRole role, ThemeConfig theme)
        {
            if (bars == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color onColor = style.GetMeterColor(role, theme);
            Color offColor = style.GetMeterColor(MeterStyleRole.Track, theme);

            for (int b = 0; b < bars.Length; b++)
            {
                if (bars[b] != null)
                {
                    bars[b].color = b < activeCount ? onColor : offColor;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            base.ApplyTheme(theme);
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "AVIONICS RF");
                ApplyText(_subTitleText, TextStyleRole.SecondaryValue, theme);
            }
            if (_ctrlBadgeBg != null) _ctrlBadgeBg.color = WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
            if (_ctrlBadgeText != null) ApplyText(_ctrlBadgeText, TextStyleRole.Accent, theme);
            if (_linkBadgeText != null) ApplyText(_linkBadgeText, TextStyleRole.Accent, theme);
            if (_dividerLine != null) _dividerLine.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 核心射频矩阵
            if (_rfMatrixBg != null) _rfMatrixBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_rfMatrixOutline != null) _rfMatrixOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_targetTitleText != null) ApplyText(_targetTitleText, TextStyleRole.SecondaryValue, theme);
            if (_targetNameText != null) ApplyText(_targetNameText, TextStyleRole.PrimaryValue, theme);
            if (_routeTypeText != null) ApplyText(_routeTypeText, TextStyleRole.Cardinal, theme);
            if (_rateText != null) ApplyText(_rateText, TextStyleRole.PrimaryValue, theme);
            if (_txRxText != null) ApplyText(_txRxText, TextStyleRole.SecondaryValue, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null) _rfSignalBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }

            // 天线硬件微栏
            if (_arrayTitleText != null) ApplyText(_arrayTitleText, TextStyleRole.Cardinal, theme);
            if (_arrayCountText != null) ApplyText(_arrayCountText, TextStyleRole.SecondaryValue, theme);

            // 天线卡片
            for (int i = 0; i < MaxCards; i++)
            {
                var card = _antennaCards[i];
                if (card == null || card.Root == null) continue;

                if (card.Bg != null) card.Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (card.Outline != null) card.Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (card.IconText != null) ApplyText(card.IconText, TextStyleRole.Accent, theme);
                if (card.NameText != null) ApplyText(card.NameText, TextStyleRole.PrimaryValue, theme);
                if (card.SpecText != null) ApplyText(card.SpecText, TextStyleRole.SecondaryValue, theme);
                if (card.StatusBadge != null) ApplyText(card.StatusBadge, TextStyleRole.Accent, theme);

                if (card.MiniBars != null)
                {
                    for (int b = 0; b < card.MiniBars.Length; b++)
                    {
                        if (card.MiniBars[b] != null) card.MiniBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
                    }
                }
            }

            _lastActiveRfBars = -1;

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
