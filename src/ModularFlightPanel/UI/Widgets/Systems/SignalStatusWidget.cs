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
    /// 航电天线硬件槽位状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct AntennaRowSnapshot : IEquatable<AntennaRowSnapshot>
    {
        public string Name;
        public string Status;
        public bool IsActive;

        public bool Equals(AntennaRowSnapshot other)
        {
            return Name == other.Name &&
                   Status == other.Status &&
                   IsActive == other.IsActive;
        }

        public override bool Equals(object obj) => obj is AntennaRowSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (IsActive ? 1 : 0);
                hash = (hash * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hash = (hash * 397) ^ (Status != null ? Status.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电通信网络与链路状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct SignalStatusState : IEquatable<SignalStatusState>
    {
        public bool HasVessel;
        public bool IsConnected;
        public double SignalStrength;
        public bool IsPartial;
        public string TargetName;
        public string RouteDesc;
        public string RateStr;
        public bool HasTx;
        public bool HasRx;
        public int ActiveRfBars;
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

        public bool Equals(SignalStatusState other)
        {
            return HasVessel == other.HasVessel &&
                   IsConnected == other.IsConnected &&
                   Math.Abs(SignalStrength - other.SignalStrength) < 0.005 &&
                   IsPartial == other.IsPartial &&
                   TargetName == other.TargetName &&
                   RouteDesc == other.RouteDesc &&
                   RateStr == other.RateStr &&
                   HasTx == other.HasTx &&
                   HasRx == other.HasRx &&
                   ActiveRfBars == other.ActiveRfBars &&
                   HwSummary == other.HwSummary &&
                   AntennaCount == other.AntennaCount &&
                   Ant0.Equals(other.Ant0) &&
                   Ant1.Equals(other.Ant1) &&
                   Ant2.Equals(other.Ant2) &&
                   Ant3.Equals(other.Ant3);
        }

        public override bool Equals(object obj) => obj is SignalStatusState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ (IsConnected ? 1 : 0);
                hash = (hash * 397) ^ ActiveRfBars;
                hash = (hash * 397) ^ (TargetName != null ? TargetName.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电通信网络与链路纯业务逻辑大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class SignalStatusLogic : WidgetLogic<SignalStatusState>
    {
        public const int RfBarCount = 5;
        public const int MaxExpandedRows = 4;

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

            bool isConnected = telemetry.IsConnected;
            double signalStrength = Mathf.Clamp01((float)telemetry.CommSignal);

            string ctrlLevel = telemetry.ControlLevelStr
                ?? (isConnected ? I18n.Tr("WIDGET_SIG_CTRL_FULL", "全权控制") : I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路"));
            bool isPartial = ctrlLevel.IndexOf("PART", StringComparison.OrdinalIgnoreCase) >= 0 || (isConnected && signalStrength < 0.35);

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

            bool hasTx = telemetry.SignalTx > 0.01;
            bool hasRx = telemetry.SignalRx > 0.01;
            int activeRfBars = isConnected ? Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * RfBarCount), 1, RfBarCount) : 0;

            var antennas = telemetry.Antennas;
            int totalAnts = (antennas != null && antennas.Count > 0) ? antennas.Count : (telemetry.AntennaCount > 0 ? telemetry.AntennaCount : 1);
            int activeAnts = 0;
            string primaryAntName = I18n.Tr("WIDGET_SIG_INTERNAL_ANTENNA", "内置天线");

            if (antennas != null && antennas.Count > 0)
            {
                for (int a = 0; a < antennas.Count; a++)
                {
                    if (antennas[a].IsOperational && antennas[a].Status == "LINKED")
                    {
                        activeAnts++;
                        if (primaryAntName == I18n.Tr("WIDGET_SIG_INTERNAL_ANTENNA", "内置天线"))
                        {
                            primaryAntName = CleanAntennaName(antennas[a].Name, a);
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

            for (int i = 0; i < MaxExpandedRows; i++)
            {
                AntennaRowSnapshot snap;
                if (antennas != null && i < antCount)
                {
                    var ant = antennas[i];
                    snap = new AntennaRowSnapshot
                    {
                        Name = CleanAntennaName(ant.Name, i),
                        Status = ant.Status,
                        IsActive = true
                    };
                }
                else if (i == 0)
                {
                    snap = new AntennaRowSnapshot
                    {
                        Name = I18n.Tr("WIDGET_SIG_INTERNAL_POD_ANTENNA", "内置舱段天线"),
                        Status = isConnected ? "LINKED" : "OFFLINE",
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

            CurrentState = new SignalStatusState
            {
                HasVessel = true,
                IsConnected = isConnected,
                SignalStrength = signalStrength,
                IsPartial = isPartial,
                TargetName = targetName,
                RouteDesc = routeDesc,
                RateStr = rateStr,
                HasTx = hasTx,
                HasRx = hasRx,
                ActiveRfBars = activeRfBars,
                HwSummary = hwSummary,
                AntennaCount = antCount,
                Ant0 = ant0,
                Ant1 = ant1,
                Ant2 = ant2,
                Ant3 = ant3
            };
        }

        public static string CleanAntennaName(string rawName, int slotIndex)
        {
            if (string.IsNullOrEmpty(rawName)) return I18n.TrFormat("WIDGET_SIG_ANTENNA_SLOT", slotIndex + 1);
            string s = rawName.Trim();
            if (s.IndexOf("[PROCEDURAL]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s = s.Replace("[PROCEDURAL]", "").Replace("[procedural]", "").Trim();
                if (string.IsNullOrEmpty(s)) s = "AVIONICS RF";
                return $"{s} #{slotIndex + 1}";
            }
            return s.ToUpperInvariant();
        }
    }

    /// <summary>
    /// 现代化全玻璃座舱通信网络极简航电组件 (Avionics CommNet Streamlined Hub)
    /// 专为游戏内高频飞行视角优化：
    /// 1. 彻底摒弃冗余的嵌套边框、嵌套卡片与重复信号计量柱，呈现极简一体化玻璃座舱质感；
    /// 2. 核心遥测一眼洞悉：主测控站、链路拓扑、传输带宽、5 阶射频光柱与微光 TX/RX 收发指示；
    /// 3. 紧凑型硬件遥测：默认呈现单行天线工况摘要，纵向拉伸时自适应平滑展开无边框天线清单；
    /// 4. 接入 IAdaptiveSizeWidget 动态长宽比协议，无畸变适配任意矩形尺寸；
    /// 5. 严格遵守零颜色字面量、零场景查询与 30Hz Standard 阶梯平滑渲染。
    /// </summary>
    [FlightWidget("signal", "signal_list", "antenna", Category = WidgetCategory.Systems, DisplayName = "COMMNET 天线通信网络", Description = "原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱。", DefaultWidgetId = "custom.signal", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "custom.signal", "custom.signal_list", "core.signal" })]
    public class SignalStatusWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(250f, 76f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private readonly SignalStatusLogic _logic = new SignalStatusLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(160f, 44f);
        public Vector2 MaxBaseSize => new Vector2(460f, 260f);

        // 声明式微控件 (顶栏语义标题)
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_SIG_COMMNET", "通信网络"));

        // 顶栏 (Header)
        private GameObject _headerRoot;
        private Image _ctrlBadgeBg;
        private Text _ctrlBadgeText;
        private Image _dividerLine;

        // 核心射频遥测 (Key RF Matrix)
        private Text _targetNameText;
        private Text _routeTypeText;
        private Text _rateText;
        private Text _txText;
        private Text _rxText;
        private const int RfBarCount = 5;
        private readonly Image[] _rfSignalBars = new Image[RfBarCount];

        // 紧凑模式：单行硬件摘要 (Clean Hardware Summary Line)
        private Text _hardwareSummaryText;

        // 展开模式：无边框优雅天线清单 (Borderless Expanded Antenna Rows)
        private class AntennaRowUI
        {
            public GameObject Root;
            public Text DotText;
            public Text NameText;
            public Text StatusText;
        }

        private const int MaxExpandedRows = 4;
        private readonly AntennaRowUI[] _expandedRows = new AntennaRowUI[MaxExpandedRows];

        // 统一全自动纳管私有 UI 脏检查缓存 (切船/重置时 BaseFlightWidget 自动复位，0 样板代码)
        private readonly Cached<string> _lastTargetName = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastRouteType = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastRateStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastCtrlBadge = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastHwSummary = new Cached<string>(string.Empty);
        private readonly Cached<bool> _lastConnectedState = new Cached<bool>(false);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float baseW = BaseSize.x * s;
            float baseH = BaseSize.y * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // ==========================================
            // 1. 顶栏系统与状态徽章 (Header)
            // ==========================================
            RectTransform hdrRt = CreateContainer("HeaderRoot", transform,
                new Vector2(baseW - 14f * s, 18f * s), new Vector2(0f, -2f * s));
            _headerRoot = hdrRt.gameObject;
            hdrRt.anchorMin = new Vector2(0.5f, 1f);
            hdrRt.anchorMax = new Vector2(0.5f, 1f);
            hdrRt.pivot = new Vector2(0.5f, 1f);

            string defTitle = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SIG_COMMNET", "通信网络"));
            Title.Text = defTitle;

            // 右侧一体化控制权徽章药丸
            Vector2 ctrlSize = new Vector2(64f * s, 13f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(_headerRoot.transform, "CtrlBadge", ctrlSize,
                new Vector2((baseW - 14f * s) * 0.5f - ctrlSize.x * 0.5f, 0f), WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(ctrlBg.transform, "Text", "● " + I18n.Tr("WIDGET_SIG_FULL_CONTROL", "满格控制"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 分割微线
            GameObject divGo = UIFactory.CreatePanel(transform, "HeaderDiv",
                new Vector2(baseW - 14f * s, 1f * s), Vector2.zero,
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _dividerLine = divGo.GetComponent<Image>();
            RectTransform divRt = divGo.GetComponent<RectTransform>();
            divRt.anchorMin = new Vector2(0.5f, 1f);
            divRt.anchorMax = new Vector2(0.5f, 1f);
            divRt.pivot = new Vector2(0.5f, 1f);
            divRt.anchoredPosition = new Vector2(0f, -20f * s);

            // ==========================================
            // 2. 核心遥测读数 (测控站、速率、射频柱)
            // ==========================================
            _targetNameText = UIFactory.CreateText(transform, "TargetName", I18n.Tr("WIDGET_SIG_CAPE_CANAVERAL", "美国 · 卡纳维拉尔角"),
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _targetNameText.fontStyle = FontStyle.Bold;
            RectTransform tnRt = _targetNameText.GetComponent<RectTransform>();
            tnRt.anchorMin = new Vector2(0.5f, 1f);
            tnRt.anchorMax = new Vector2(0.5f, 1f);
            tnRt.pivot = new Vector2(0f, 1f);

            _routeTypeText = UIFactory.CreateText(transform, "RouteType", I18n.Tr("WIDGET_SIG_DIRECT_HOME_DSN", "直连 · 深空网主站"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform rtRt = _routeTypeText.GetComponent<RectTransform>();
            rtRt.anchorMin = new Vector2(0.5f, 1f);
            rtRt.anchorMax = new Vector2(0.5f, 1f);
            rtRt.pivot = new Vector2(0f, 1f);

            _rateText = UIFactory.CreateText(transform, "DataRate", "15.8 Kbps",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _rateText.fontStyle = FontStyle.Bold;
            RectTransform drRt = _rateText.GetComponent<RectTransform>();
            drRt.anchorMin = new Vector2(0.5f, 1f);
            drRt.anchorMax = new Vector2(0.5f, 1f);
            drRt.pivot = new Vector2(1f, 1f);

            _txText = UIFactory.CreateText(transform, "TxIndicator", "▲" + I18n.Tr("WIDGET_SIG_TX", "发射"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _txText.fontStyle = FontStyle.Bold;
            RectTransform txRt = _txText.GetComponent<RectTransform>();
            txRt.anchorMin = new Vector2(0.5f, 1f);
            txRt.anchorMax = new Vector2(0.5f, 1f);
            txRt.pivot = new Vector2(0.5f, 0.5f);

            _rxText = UIFactory.CreateText(transform, "RxIndicator", "▼" + I18n.Tr("WIDGET_SIG_RX", "接收"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _rxText.fontStyle = FontStyle.Bold;
            RectTransform rxRt = _rxText.GetComponent<RectTransform>();
            rxRt.anchorMin = new Vector2(0.5f, 1f);
            rxRt.anchorMax = new Vector2(0.5f, 1f);
            rxRt.pivot = new Vector2(0.5f, 0.5f);

            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < RfBarCount; b++)
            {
                GameObject bGo = UIFactory.CreatePanel(transform, $"RfBar_{b}",
                    Vector2.one, Vector2.zero, trackCol);
                _rfSignalBars[b] = bGo.GetComponent<Image>();
                RectTransform bRt = bGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.5f, 1f);
                bRt.anchorMax = new Vector2(0.5f, 1f);
                bRt.pivot = new Vector2(0.5f, 0f);
            }

            // ==========================================
            // 3. 硬件摘要 (单行极简条) 与 展开清单
            // ==========================================
            _hardwareSummaryText = UIFactory.CreateText(transform, "HwSummary", "● " + I18n.Tr("WIDGET_SIG_COMMUNOTRON", "通信模块 16 (1/3 活动)"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform hwRt = _hardwareSummaryText.GetComponent<RectTransform>();
            hwRt.anchorMin = new Vector2(0.5f, 1f);
            hwRt.anchorMax = new Vector2(0.5f, 1f);
            hwRt.pivot = new Vector2(0f, 1f);

            for (int i = 0; i < MaxExpandedRows; i++)
            {
                _expandedRows[i] = CreateAntennaRow(transform, $"AntRow_{i}", s, theme);
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetAnnunciatorControl("ctrl_badge", "控制权徽章", _ctrlBadgeText != null ? _ctrlBadgeText.gameObject : null, _ctrlBadgeText, null, _ctrlBadgeBg, null));
            this.Controls.Register(new WidgetReadoutControl("target_readout", "目标对端与速率", _targetNameText != null ? _targetNameText.gameObject : null, _targetNameText, _rateText, TextStyleRole.PrimaryValue, "{SIGNAL}"));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
            ApplyLayout(RectTransform.sizeDelta.x, RectTransform.sizeDelta.y);
        }

        private AntennaRowUI CreateAntennaRow(Transform parent, string name, float s, ThemeConfig theme)
        {
            AntennaRowUI row = new AntennaRowUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            RectTransform rt = CreateContainer(name, parent);
            row.Root = rt.gameObject;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            row.DotText = UIFactory.CreateText(row.Root.transform, "Dot", "●",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform dRt = row.DotText.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0f, 0.5f);
            dRt.anchorMax = new Vector2(0f, 0.5f);
            dRt.pivot = new Vector2(0.5f, 0.5f);
            dRt.sizeDelta = new Vector2(10f * s, 14f * s);
            dRt.anchoredPosition = new Vector2(6f * s, 0f);

            row.NameText = UIFactory.CreateText(row.Root.transform, "Name", I18n.Tr("WIDGET_SIG_ANTENNA", "天线"),
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform nRt = row.NameText.GetComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0f, 0.5f);
            nRt.anchorMax = new Vector2(0f, 0.5f);
            nRt.pivot = new Vector2(0f, 0.5f);
            nRt.anchoredPosition = new Vector2(14f * s, 0f);

            row.StatusText = UIFactory.CreateText(row.Root.transform, "Status", I18n.Tr("WIDGET_SIG_LINKED", "已链接"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            row.StatusText.fontStyle = FontStyle.Bold;
            RectTransform sRt = row.StatusText.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(1f, 0.5f);
            sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.pivot = new Vector2(1f, 0.5f);
            sRt.anchoredPosition = new Vector2(-4f * s, 0f);

            row.Root.SetActive(false);
            return row;
        }

        // ==========================================
        // 4. 动态长宽比排版自适应 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayout(pixelSize.x, pixelSize.y);
        }

        private void ApplyLayout(float width, float height)
        {
            if (_headerRoot == null) return;

            float s = CurrentDpiScale;
            float cw = Mathf.Max(120f * s, width - 14f * s);
            float halfCw = cw * 0.5f;

            // 1. 顶栏排版
            float hdrH = 18f * s;
            RectTransform hdrRt = _headerRoot.GetComponent<RectTransform>();
            if (hdrRt != null)
            {
                hdrRt.sizeDelta = new Vector2(cw, hdrH);
                hdrRt.anchoredPosition = new Vector2(0f, -2f * s);
            }

            // DSL Title 位置自适应
            if (Title != null && Title.RootGameObject != null)
            {
                var tRt = Title.RootGameObject.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchoredPosition = new Vector2(4f * s, 0f);
                    tRt.sizeDelta = new Vector2(cw * 0.5f, hdrH);
                }
            }

            // 顶栏控制权徽章药丸
            float badgeW = width < 200f * s ? 52f * s : 64f * s;
            float badgeH = 13f * s;
            if (_ctrlBadgeBg != null)
            {
                RectTransform cRt = _ctrlBadgeBg.rectTransform;
                cRt.sizeDelta = new Vector2(badgeW, badgeH);
                cRt.anchoredPosition = new Vector2(halfCw - badgeW * 0.5f, 0f);
            }
            if (_ctrlBadgeText != null)
            {
                _ctrlBadgeText.rectTransform.sizeDelta = new Vector2(badgeW, badgeH);
            }

            // 分割微线
            if (_dividerLine != null)
            {
                RectTransform divRt = _dividerLine.rectTransform;
                divRt.sizeDelta = new Vector2(cw, 1f * s);
                divRt.anchoredPosition = new Vector2(0f, -20f * s);
            }

            // 2. 核心遥测排版 (左右布局，无内嵌方框)
            float rightColW = Mathf.Clamp(cw * 0.38f, 75f * s, 100f * s);
            float leftColW = cw - rightColW - 10f * s;

            if (_targetNameText != null)
            {
                RectTransform tnRt = _targetNameText.rectTransform;
                tnRt.sizeDelta = new Vector2(leftColW, 14f * s);
                tnRt.anchoredPosition = new Vector2(-halfCw + 4f * s, -23f * s);
            }

            if (_routeTypeText != null)
            {
                RectTransform rtRt = _routeTypeText.rectTransform;
                rtRt.sizeDelta = new Vector2(leftColW, 11f * s);
                rtRt.anchoredPosition = new Vector2(-halfCw + 4f * s, -38f * s);
            }

            // 右侧速率
            if (_rateText != null)
            {
                RectTransform drRt = _rateText.rectTransform;
                drRt.sizeDelta = new Vector2(rightColW, 14f * s);
                drRt.anchoredPosition = new Vector2(halfCw - 4f * s, -23f * s);
            }

            // 右侧 5 阶微光柱与 TX/RX
            float barW = 2.4f * s;
            float barGap = 1.4f * s;
            float totalBarsW = RfBarCount * barW + (RfBarCount - 1) * barGap;
            float barStartX = halfCw - 4f * s - totalBarsW;
            float barsBaseY = -48f * s;

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null)
                {
                    float bH = (4.0f + b * 1.8f) * s;
                    float bx = barStartX + b * (barW + barGap);
                    _rfSignalBars[b].rectTransform.sizeDelta = new Vector2(barW, bH);
                    _rfSignalBars[b].rectTransform.anchoredPosition = new Vector2(bx, barsBaseY);
                }
            }

            if (_txText != null && _rxText != null)
            {
                bool showTxRx = width >= 180f * s;
                _txText.gameObject.SetActive(showTxRx);
                _rxText.gameObject.SetActive(showTxRx);
                if (showTxRx)
                {
                    _txText.rectTransform.sizeDelta = new Vector2(18f * s, 10f * s);
                    _txText.rectTransform.anchoredPosition = new Vector2(barStartX - 28f * s, barsBaseY + 5f * s);

                    _rxText.rectTransform.sizeDelta = new Vector2(18f * s, 10f * s);
                    _rxText.rectTransform.anchoredPosition = new Vector2(barStartX - 10f * s, barsBaseY + 5f * s);
                }
            }

            // 3. 硬件摘要与展开清单自适应切换
            bool isExpandedHeight = height >= 86f * s;

            if (_hardwareSummaryText != null)
            {
                _hardwareSummaryText.gameObject.SetActive(!isExpandedHeight);
                if (!isExpandedHeight)
                {
                    RectTransform hwRt = _hardwareSummaryText.rectTransform;
                    hwRt.sizeDelta = new Vector2(cw - 8f * s, 13f * s);
                    hwRt.anchoredPosition = new Vector2(-halfCw + 4f * s, -54f * s);
                }
            }

            // 纵向扩展模式：展示精炼清单
            if (isExpandedHeight)
            {
                float rowStartY = -54f * s;
                float availH = height - 56f * s - 4f * s;
                float rowH = 15f * s;
                float rowGap = 2f * s;
                int maxFit = Mathf.Clamp(Mathf.FloorToInt((availH + rowGap) / (rowH + rowGap)), 1, MaxExpandedRows);

                for (int i = 0; i < MaxExpandedRows; i++)
                {
                    var row = _expandedRows[i];
                    if (row == null || row.Root == null) continue;

                    if (i < maxFit)
                    {
                        row.Root.SetActive(true);
                        RectTransform rRt = row.Root.GetComponent<RectTransform>();
                        rRt.sizeDelta = new Vector2(cw, rowH);
                        rRt.anchoredPosition = new Vector2(0f, rowStartY - i * (rowH + rowGap));

                        if (row.NameText != null)
                        {
                            row.NameText.rectTransform.sizeDelta = new Vector2(cw - 65f * s, rowH);
                        }
                    }
                    else
                    {
                        row.Root.SetActive(false);
                    }
                }
            }
            else
            {
                for (int i = 0; i < MaxExpandedRows; i++)
                {
                    if (_expandedRows[i]?.Root != null) _expandedRows[i].Root.SetActive(false);
                }
            }
        }

        // ==========================================
        // 5. 遥测业务求值与平滑光度动画 (OnRenderState)
        // ==========================================
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
            WidgetStyleManager style = WidgetStyleManager.Instance;

            bool isConnected = state.IsConnected;
            double signalStrength = state.SignalStrength;
            bool isPartial = state.IsPartial;

            // 1. 顶栏控制权徽章与温和心跳呼吸
            StatusSurfaceRole statusRole = !isConnected ? StatusSurfaceRole.Danger : (isPartial ? StatusSurfaceRole.Caution : StatusSurfaceRole.Success);
            TextStyleRole textRole = !isConnected ? TextStyleRole.Danger : (isPartial ? TextStyleRole.Warning : TextStyleRole.Accent);

            _ctrlBadgeBg.SetColor(WidgetStyleManager.StatusPanel(statusRole));

            string displayCtrl;
            float currentW = RectTransform != null ? RectTransform.sizeDelta.x : BaseSize.x * CurrentDpiScale;
            if (currentW < 200f * CurrentDpiScale)
            {
                displayCtrl = !isConnected
                    ? "✕ " + I18n.Tr("WIDGET_SIG_CTRL_NO_COMM", "无通信")
                    : (isPartial ? "▲ " + I18n.Tr("WIDGET_SIG_CTRL_PARTIAL", "部分控制") : "● " + I18n.Tr("WIDGET_SIG_CTRL_FULL", "全权控制"));
            }
            else
            {
                displayCtrl = !isConnected
                    ? "✕ " + I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路")
                    : (isPartial ? "▲ " + I18n.Tr("WIDGET_SIG_CTRL_PARTIAL", "部分控制") : "● " + I18n.Tr("WIDGET_SIG_CTRL_FULL", "全权控制"));
            }
            if (_lastCtrlBadge.Update(displayCtrl))
            {
                _ctrlBadgeText.SetTextSafe(displayCtrl);
            }

            // 心跳微光动画 (0.82 ~ 1.0)
            Color ctrlCol = style.GetTextColor(textRole, theme);
            float animAlpha = isConnected ? (0.82f + 0.18f * Mathf.Sin(Time.time * 2.8f)) : (0.72f + 0.28f * Mathf.Sin(Time.time * 2.0f));
            _ctrlBadgeText.SetColor(WidgetStyleManager.WithAlpha(ctrlCol, animAlpha));

            // 2. 目标测控站与拓扑
            if (_lastTargetName.Update(state.TargetName))
            {
                _targetNameText.SetTextSafe(state.TargetName);
            }
            if (_lastRouteType.Update(state.RouteDesc))
            {
                _routeTypeText.SetTextSafe(state.RouteDesc);
            }
            if (_lastConnectedState.Update(isConnected))
            {
                ApplyText(_targetNameText, isConnected ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);
            }

            // 3. 速率与 TX/RX 遥测收发微光动画
            if (_lastRateStr.Update(state.RateStr))
            {
                _rateText.SetTextSafe(state.RateStr);
            }

            if (_txText != null && _rxText != null && _txText.gameObject.activeSelf)
            {
                Color txBase = style.GetTextColor(TextStyleRole.Accent, theme);
                Color rxBase = style.GetTextColor(TextStyleRole.Cardinal, theme);

                if (isConnected && (state.RateStr != "0.0 bps" || signalStrength > 0.01))
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

            // 4. 5 阶主射频光柱
            int activeRfBars = state.ActiveRfBars;
            MeterStyleRole barRole = !isConnected ? MeterStyleRole.Track : (signalStrength < 0.35 ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color activeCol = style.GetMeterColor(barRole, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null)
                {
                    if (b < activeRfBars)
                    {
                        if (b == activeRfBars - 1 && isConnected)
                        {
                            float shimmer = 0.84f + 0.16f * Mathf.Sin(Time.time * 3.5f);
                            _rfSignalBars[b].SetColor(WidgetStyleManager.WithAlpha(activeCol, shimmer));
                        }
                        else
                        {
                            _rfSignalBars[b].SetColor(activeCol);
                        }
                    }
                    else
                    {
                        _rfSignalBars[b].SetColor(trackCol);
                    }
                }
            }

            // 5. 硬件信息
            if (_hardwareSummaryText != null && _hardwareSummaryText.gameObject.activeSelf)
            {
                if (_lastHwSummary.Update(state.HwSummary))
                {
                    _hardwareSummaryText.SetTextSafe(state.HwSummary);
                }
            }

            // 展开模式：清单
            for (int i = 0; i < MaxExpandedRows; i++)
            {
                var row = _expandedRows[i];
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

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastTargetName.Reset(string.Empty);
            _lastRouteType.Reset(string.Empty);
            _lastRateStr.Reset(string.Empty);
            _lastCtrlBadge.Reset(string.Empty);
            _lastHwSummary.Reset(string.Empty);
            _lastConnectedState.Reset(false);
        }

        /// <summary>
        /// 天线状态"显示期"本地化：遥测层保留英文语义值（LINKED / STANDBY … 仍被判定逻辑比较），
        /// 只在写入 UI 文本时翻译，避免汉化破坏活动天线计数与文本配色判定。
        /// </summary>
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
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            base.ApplyTheme(theme);

            if (_dividerLine != null) _dividerLine.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_targetNameText != null) ApplyText(_targetNameText, TextStyleRole.PrimaryValue, theme);
            if (_routeTypeText != null) ApplyText(_routeTypeText, TextStyleRole.Cardinal, theme);
            if (_rateText != null) ApplyText(_rateText, TextStyleRole.PrimaryValue, theme);
            if (_txText != null) ApplyText(_txText, TextStyleRole.Accent, theme);
            if (_rxText != null) ApplyText(_rxText, TextStyleRole.Cardinal, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null) _rfSignalBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }

            if (_hardwareSummaryText != null) ApplyText(_hardwareSummaryText, TextStyleRole.SecondaryValue, theme);

            for (int i = 0; i < MaxExpandedRows; i++)
            {
                var row = _expandedRows[i];
                if (row == null || row.Root == null) continue;
                if (row.DotText != null) ApplyText(row.DotText, TextStyleRole.Accent, theme);
                if (row.NameText != null) ApplyText(row.NameText, TextStyleRole.PrimaryValue, theme);
                if (row.StatusText != null) ApplyText(row.StatusText, TextStyleRole.Accent, theme);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
