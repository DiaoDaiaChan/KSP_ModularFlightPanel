using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化航电通信网络与真实天线探针组件 (Avionics CommNet & RealAntennas Capsule Suite)
    /// 
    /// 完美替代 KSP 原版左上角通信信号指示器：
    /// 1. 现代化微光胶囊状态条 (5 阶信号阶梯光柱、控制权徽章、主通信站标识、Tx/Rx 与实时速率)
    /// 2. 深度集成 RealAntennas 探针 (查询真实射频链路、双向吞吐速率、动态多信道对端列表)
    /// 3. 抽屉式折叠扩展信道矩阵 (对端站点名称、链路带宽、相对信号强度、中继/直连标识)
    /// 4. 原版 CommNet 信号栏非破坏性安全隐显切换 (STOCK TOGGLE)
    /// </summary>
    /// <summary>
    /// 外部通信对端信道快照 (0 GC 纯值结构体)
    /// </summary>
    public struct PeerRowSnapshot : IEquatable<PeerRowSnapshot>
    {
        public string PeerName;
        public bool IsDirectHome;
        public string FormattedDataRate;
        public int PeerBars;

        public bool Equals(PeerRowSnapshot other)
        {
            return PeerName == other.PeerName &&
                   IsDirectHome == other.IsDirectHome &&
                   FormattedDataRate == other.FormattedDataRate &&
                   PeerBars == other.PeerBars;
        }

        public override bool Equals(object obj) => obj is PeerRowSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                if (PeerName != null) hash = (hash * 397) ^ PeerName.GetHashCode();
                hash = (hash * 397) ^ IsDirectHome.GetHashCode();
                if (FormattedDataRate != null) hash = (hash * 397) ^ FormattedDataRate.GetHashCode();
                hash = (hash * 397) ^ PeerBars.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电通信网络与天线状态快照 (0 GC 纯值结构体)
    /// </summary>
    public struct CommSignalCapsuleState : IEquatable<CommSignalCapsuleState>
    {
        public bool HasVessel;
        public double Sig;
        public int LitBars;
        public int CtrlState;
        public string TargetName;
        public string RateSummary;
        public string MatrixSummary;
        public string MatrixFooter;
        public int LinkCount;
        public PeerRowSnapshot Peer0;
        public PeerRowSnapshot Peer1;
        public PeerRowSnapshot Peer2;
        public PeerRowSnapshot Peer3;
        public PeerRowSnapshot Peer4;

        public PeerRowSnapshot GetPeer(int index)
        {
            switch (index)
            {
                case 0: return Peer0;
                case 1: return Peer1;
                case 2: return Peer2;
                case 3: return Peer3;
                case 4: return Peer4;
                default: return default;
            }
        }

        public bool Equals(CommSignalCapsuleState other)
        {
            return HasVessel == other.HasVessel &&
                   Math.Abs(Sig - other.Sig) < 0.001 &&
                   LitBars == other.LitBars &&
                   CtrlState == other.CtrlState &&
                   TargetName == other.TargetName &&
                   RateSummary == other.RateSummary &&
                   MatrixSummary == other.MatrixSummary &&
                   MatrixFooter == other.MatrixFooter &&
                   LinkCount == other.LinkCount &&
                   Peer0.Equals(other.Peer0) &&
                   Peer1.Equals(other.Peer1) &&
                   Peer2.Equals(other.Peer2) &&
                   Peer3.Equals(other.Peer3) &&
                   Peer4.Equals(other.Peer4);
        }

        public override bool Equals(object obj) => obj is CommSignalCapsuleState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ LitBars.GetHashCode();
                hash = (hash * 397) ^ CtrlState.GetHashCode();
                if (TargetName != null) hash = (hash * 397) ^ TargetName.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电通信网络与天线探针纯 C# 业务解耦大脑 (Headless Widget Logic)
    /// </summary>
    public class CommSignalCapsuleLogic : WidgetLogic<CommSignalCapsuleState>
    {
        public bool IsDropdownOpen { get; set; }

        private float _rateUpdateTimer = 999f;
        private float _matrixUpdateTimer = 999f;
        private int _cachedLitBars = -1;
        private string _rateTemplate;
        private string _summaryTemplate;
        private string _targetFallback;
        private string _cachedRateSummary;
        private string _cachedMatrixSummary;
        private string _cachedMatrixFooter;
        private int _cachedLinkCount;
        private readonly PeerRowSnapshot[] _cachedPeerSnapshots = new PeerRowSnapshot[CommSignalWidget.MaxPeerRows];

        public override void Reset()
        {
            _rateUpdateTimer = 999f;
            _matrixUpdateTimer = 999f;
            _cachedLitBars = -1;
            _cachedRateSummary = null;
            _cachedMatrixSummary = null;
            _cachedMatrixFooter = null;
            _cachedLinkCount = 0;
            for (int i = 0; i < CommSignalWidget.MaxPeerRows; i++) _cachedPeerSnapshots[i] = default;
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            double sig = Mathf.Clamp01((float)telemetry.CommSignal);
            int litBars = Mathf.RoundToInt((float)sig * CommSignalWidget.MainBarCount);
            bool sigStateChanged = litBars != _cachedLitBars;
            _cachedLitBars = litBars;

            int ctrlState = (!telemetry.IsConnected && sig <= 0.001) ? 0 : ((sig < 0.2) ? 1 : 2);

            string tgt = telemetry.DirectLinkTarget;
            string targetName = string.IsNullOrEmpty(tgt)
                ? (_targetFallback ?? (_targetFallback = I18n.Tr("WIDGET_SIG_COMMNET", "通信网络")))
                : tgt;

            _rateUpdateTimer += deltaTime;
            if (_rateUpdateTimer >= 0.25f || sigStateChanged || _cachedRateSummary == null)
            {
                _rateUpdateTimer = 0f;
                if (_rateTemplate == null) _rateTemplate = "{COMM} | {COMM:RATE}";
                _cachedRateSummary = TelemetryTokenEngine.Evaluate(_rateTemplate, telemetry);
            }

            if (IsDropdownOpen)
            {
                _matrixUpdateTimer += deltaTime;
                if (_matrixUpdateTimer >= 0.25f || sigStateChanged || _cachedMatrixSummary == null)
                {
                    _matrixUpdateTimer = 0f;
                    if (_summaryTemplate == null) _summaryTemplate = I18n.Tr("WIDGET_SIG_SUMMARY_TEMPLATE", "发射/接收: {COMM:TX}/{COMM:RX}  {COMM:RATE}");
                    _cachedMatrixSummary = TelemetryTokenEngine.Evaluate(_summaryTemplate, telemetry);

                    var links = telemetry.ActiveCommLinks;
                    int linkCount = (links != null) ? links.Count : 0;
                    _cachedLinkCount = linkCount;

                    for (int r = 0; r < CommSignalWidget.MaxPeerRows; r++)
                    {
                        if (r < linkCount)
                        {
                            var info = links[r];
                            _cachedPeerSnapshots[r] = new PeerRowSnapshot
                            {
                                PeerName = info.PeerName,
                                IsDirectHome = info.IsDirectHome,
                                FormattedDataRate = info.FormattedDataRate,
                                PeerBars = Mathf.RoundToInt((float)info.SignalStrength * 5f)
                            };
                        }
                        else
                        {
                            _cachedPeerSnapshots[r] = default;
                        }
                    }

                    _cachedMatrixFooter = I18n.TrFormat("WIDGET_SIGNAL_FOOTER", linkCount);
                }
            }

            CurrentState = new CommSignalCapsuleState
            {
                HasVessel = true,
                Sig = sig,
                LitBars = litBars,
                CtrlState = ctrlState,
                TargetName = targetName,
                RateSummary = _cachedRateSummary,
                MatrixSummary = _cachedMatrixSummary,
                MatrixFooter = _cachedMatrixFooter,
                LinkCount = _cachedLinkCount,
                Peer0 = _cachedPeerSnapshots[0],
                Peer1 = _cachedPeerSnapshots[1],
                Peer2 = _cachedPeerSnapshots[2],
                Peer3 = _cachedPeerSnapshots[3],
                Peer4 = _cachedPeerSnapshots[4]
            };
        }
    }

    [FlightWidget("comm_signal", "commsignal", Category = WidgetCategory.Systems, DisplayName = "COMM 天线通信信号条", Description = "紧凑型通信天线连接质量与中继跳数状态条。", DefaultWidgetId = "core.comm_signal", DefaultX = 300f, DefaultY = 200f, IsSingleton = true, ExactIds = new[] { "core.comm_signal", "core.commsignal" })]
    public class CommSignalWidget : BaseFlightWidget
    {
        public const int MainBarCount = 5;
        public const int MaxPeerRows = 5;

        public override Vector2 BaseSize => new Vector2(264f, 26f);
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private readonly CommSignalCapsuleLogic _logic = new CommSignalCapsuleLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private Image _panelBg;
        private Outline _panelOutline;

        // 胶囊顶栏 UI 元素
        private GameObject _capsuleBar;
        private readonly Image[] _mainSignalBars = new Image[MainBarCount];
        private Text _ctrlBadgeText;
        private Image _ctrlBadgeBg;
        private Text _targetNameText;
        private Text _rateSummaryText;
        private Button _expandBtn;
        private Text _expandBtnText;
        private Button _stockBtn;
        private Text _stockBtnText;

        // 抽屉式扩展链路列表
        private GameObject _dropdownPanel;
        private Text _matrixTitleText;
        private Text _matrixSummaryText;

        private struct PeerRowUI
        {
            public GameObject RowObj;
            public Text NameText;
            public Text TagText;
            public Text RateText;
            public Image[] MiniBars;
        }
        private readonly PeerRowUI[] _peerRows = new PeerRowUI[MaxPeerRows];
        private Text _matrixFooterText;

        private readonly Cached<bool> _isExpanded = new Cached<bool>(false);
        private readonly Cached<bool> _stockHidden = new Cached<bool>(true);
        private ThemeConfig _currentTheme;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 baseSize = new Vector2(264f * s, 26f * s);
            RectTransform.sizeDelta = baseSize;

            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;
            Color bgCol = theme.FrameBgColor;
            Color borderCol = theme.FrameBorderColor;

            // 1. 胶囊底座 (现代化暗晶毛玻璃背板 0.75 Alpha)
            _capsuleBar = UIFactory.CreatePanel(transform, "CapsuleBar", baseSize, Vector2.zero,
                WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);
            _panelBg = _capsuleBar.GetComponent<Image>();
            _panelOutline = _capsuleBar.GetComponent<Outline>();
            _panelOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // ==========================================
            // 2. 胶囊顶栏控件布局
            // ==========================================
            // 5 阶信号柱 (Heights: 4, 6, 8, 10, 12)
            float barStartX = -baseSize.x * 0.5f + 12f * s;
            float barW = 2.5f * s;
            float barGap = 1.8f * s;
            for (int i = 0; i < MainBarCount; i++)
            {
                float barH = (4f + i * 2f) * s;
                float bx = barStartX + i * (barW + barGap);
                float by = -baseSize.y * 0.5f + 7f * s + barH * 0.5f;

                GameObject bGo = UIFactory.CreatePanel(_capsuleBar.transform, $"SigBar_{i}",
                    new Vector2(barW, barH), new Vector2(bx, by), primaryAccent);
                _mainSignalBars[i] = bGo.GetComponent<Image>();
            }

            // 控制权状态徽章 (FULL / PART / NONE) - 极简药丸
            Vector2 ctrlSize = new Vector2(32f * s, 14f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(_capsuleBar.transform, "CtrlBadge", ctrlSize,
                new Vector2(-baseSize.x * 0.5f + 46f * s, 0f), WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(ctrlBg.transform, "Text", I18n.Tr("WIDGET_SIGNAL_FULL", "满格"),
                Mathf.Max(7, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 主站点/对端名称
            _targetNameText = UIFactory.CreateText(_capsuleBar.transform, "TargetName", I18n.Tr("WIDGET_SIG_KSAT_SINGAPORE", "新加坡 KSAT 站"),
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft, textPrimary);
            _targetNameText.fontStyle = FontStyle.Bold;
            RectTransform tgtRt = _targetNameText.GetComponent<RectTransform>();
            tgtRt.sizeDelta = new Vector2(80f * s, 18f * s);
            tgtRt.anchoredPosition = new Vector2(-baseSize.x * 0.5f + 108f * s, 0f);

            // 综合速率与信号 (100% · 15.8K)
            _rateSummaryText = UIFactory.CreateText(_capsuleBar.transform, "RateSummary", "100% · 15.8K",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, secondaryAccent);
            RectTransform rateRt = _rateSummaryText.GetComponent<RectTransform>();
            rateRt.sizeDelta = new Vector2(72f * s, 18f * s);
            rateRt.anchoredPosition = new Vector2(baseSize.x * 0.5f - 65f * s, 0f);

            // 折叠/展开按键 (▾ / ▴)
            Vector2 expBtnSize = new Vector2(14f * s, 16f * s);
            _expandBtn = UIFactory.CreateButton(_capsuleBar.transform, "Btn_Expand", expBtnSize,
                new Vector2(baseSize.x * 0.5f - 24f * s, 0f), OnToggleExpand);
            _expandBtn.GetComponent<Image>().color = Color.clear;
            _expandBtnText = UIFactory.CreateText(_expandBtn.transform, "Text", "▾",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _expandBtnText.GetComponent<RectTransform>().sizeDelta = expBtnSize;

            // 原版信号栏显隐按键 (KSP)
            Vector2 stockBtnSize = new Vector2(18f * s, 16f * s);
            _stockBtn = UIFactory.CreateButton(_capsuleBar.transform, "Btn_Stock", stockBtnSize,
                new Vector2(baseSize.x * 0.5f - 8f * s, 0f), OnToggleStock);
            _stockBtn.GetComponent<Image>().color = Color.clear;
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));
            _stockBtnText.GetComponent<RectTransform>().sizeDelta = stockBtnSize;

            // ==========================================
            // 3. 抽屉式链路矩阵面板 (Dropdown Matrix)
            // ==========================================
            float dropH = 142f * s;
            Vector2 dropSize = new Vector2(baseSize.x, dropH);
            _dropdownPanel = UIFactory.CreatePanel(transform, "DropdownMatrix", dropSize,
                new Vector2(0f, -baseSize.y * 0.5f - dropH * 0.5f - 2f * s),
                WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(bgCol, 0.20f), 0.96f), borderCol, 1f * s);

            // 标题
            _matrixTitleText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixTitle", I18n.Tr("WIDGET_SIGNAL_TITLE", "REALANTENNAS / COMMNET"),
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform mTitRt = _matrixTitleText.GetComponent<RectTransform>();
            mTitRt.sizeDelta = new Vector2(120f * s, 14f * s);
            mTitRt.anchoredPosition = new Vector2(-dropSize.x * 0.5f + 66f * s, dropSize.y * 0.5f - 10f * s);

            // 双向信号与速率摘要
            _matrixSummaryText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixSummary", I18n.Tr("WIDGET_SIG_TXRX_PLACEHOLDER", "发射/接收: 90%/90%  15.8 Kbps"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, textPrimary);
            RectTransform mSumRt = _matrixSummaryText.GetComponent<RectTransform>();
            mSumRt.sizeDelta = new Vector2(110f * s, 14f * s);
            mSumRt.anchoredPosition = new Vector2(dropSize.x * 0.5f - 58f * s, dropSize.y * 0.5f - 10f * s);

            // 分割线
            UIFactory.CreatePanel(_dropdownPanel.transform, "DivLine", new Vector2(dropSize.x - 12f * s, 1f * s),
                new Vector2(0f, dropSize.y * 0.5f - 19f * s), WidgetStyleManager.Weighted(borderCol, LineWeight.Normal));

            // 对端信道行 (构建 5 行)
            float rowStartY = dropSize.y * 0.5f - 31f * s;
            float rowSpacing = 20f * s;
            for (int r = 0; r < MaxPeerRows; r++)
            {
                _peerRows[r] = BuildPeerRow(_dropdownPanel.transform, $"PeerRow_{r}",
                    new Vector2(dropSize.x - 12f * s, 18f * s),
                    new Vector2(0f, rowStartY - r * rowSpacing), s, secondaryAccent, primaryAccent);
            }

            // 底部脚注
            _matrixFooterText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixFooter",
                I18n.Tr("WIDGET_SIG_MATRIX_FOOTER_INIT", "● 5 活动链路  |  RA 矩阵遥测"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Bold));
            RectTransform footRt = _matrixFooterText.GetComponent<RectTransform>();
            footRt.sizeDelta = new Vector2(dropSize.x, 12f * s);
            footRt.anchoredPosition = new Vector2(0f, -dropSize.y * 0.5f + 8f * s);

            // 初始化根据主题配置执行原版通信指示器静默隐藏
            _stockHidden.Value = ThemeManager.IsStockCommNetHidden;
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden.Value);

            UpdateExpansionLayout();

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "capsule_bar", "通信胶囊条", _capsuleBar, (t) => { if (_panelBg != null) _panelBg.color = WidgetStyleManager.WithAlpha(t.FrameBgColor, 0.75f); }));
            this.Controls.Register(new WidgetAnnunciatorControl("ctrl_badge", "控制权徽章", _ctrlBadgeText != null ? _ctrlBadgeText.gameObject : null, _ctrlBadgeText, null, _ctrlBadgeBg, null));
            this.Controls.Register(new WidgetReadoutControl("target_readout", "通信对端名称", _targetNameText != null ? _targetNameText.gameObject : null, _targetNameText, _rateSummaryText, TextStyleRole.PrimaryValue, "{COMM}"));
            this.Controls.Register(new WidgetActionButtonControl("expand_btn", "折叠展开按键", _expandBtn != null ? _expandBtn.gameObject : null, _expandBtn, _expandBtnText, null, ButtonVisualRole.Ghost));
            this.Controls.Register(new WidgetActionButtonControl("stock_toggle_btn", "原版显隐按键", _stockBtn != null ? _stockBtn.gameObject : null, _stockBtn, _stockBtnText, null, ButtonVisualRole.Ghost));
            if (_dropdownPanel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "dropdown_matrix", "链路信道矩阵", _dropdownPanel));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private PeerRowUI BuildPeerRow(Transform parent, string name, Vector2 size, Vector2 pos, float s, Color labelCol, Color barCol)
        {
            PeerRowUI ui = new PeerRowUI();
            ui.RowObj = UIFactory.CreatePanel(parent, name, size, pos, WidgetStyleManager.Surface(SurfaceStyleRole.Slot));

            // 站点名称 (e.g. KSAT - Singapore)
            ui.NameText = UIFactory.CreateText(ui.RowObj.transform, "Name", "---",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            RectTransform nRt = ui.NameText.GetComponent<RectTransform>();
            nRt.sizeDelta = new Vector2(102f * s, 16f * s);
            nRt.anchoredPosition = new Vector2(-size.x * 0.5f + 54f * s, 0f);

            // 标签 (DSN / RELAY / VESSEL)
            ui.TagText = UIFactory.CreateText(ui.RowObj.transform, "Tag", "DSN",
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, labelCol);
            RectTransform tRt = ui.TagText.GetComponent<RectTransform>();
            tRt.sizeDelta = new Vector2(30f * s, 14f * s);
            tRt.anchoredPosition = new Vector2(6f * s, 0f);

            // 数据速率 (63.0 Kbps)
            ui.RateText = UIFactory.CreateText(ui.RowObj.transform, "Rate", "0.0 bps",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, labelCol);
            RectTransform rRt = ui.RateText.GetComponent<RectTransform>();
            rRt.sizeDelta = new Vector2(50f * s, 16f * s);
            rRt.anchoredPosition = new Vector2(size.x * 0.5f - 40f * s, 0f);

            // 5 格 mini 信号柱
            ui.MiniBars = new Image[5];
            float miniStartX = size.x * 0.5f - 14f * s;
            for (int b = 0; b < 5; b++)
            {
                float mh = (3f + b * 1.5f) * s;
                float mx = miniStartX + b * (1.8f * s + 1f * s);
                GameObject mbGo = UIFactory.CreatePanel(ui.RowObj.transform, $"MiniBar_{b}",
                    new Vector2(1.8f * s, mh), new Vector2(mx, -size.y * 0.5f + 5f * s + mh * 0.5f), barCol);
                ui.MiniBars[b] = mbGo.GetComponent<Image>();
            }

            return ui;
        }

        private readonly Cached<string> _lastTargetName = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastRateSummary = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastMatrixSummary = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastMatrixFooter = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastStockBtnText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastLitBars = new Cached<int>(-1);
        private readonly Cached<int> _lastCtrlState = new Cached<int>(-1);



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

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_currentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 主信号条点亮与色彩驱动
            int litBars = state.LitBars;
            double sig = state.Sig;
            MeterStyleRole barRole = (sig > 0.4)
                ? MeterStyleRole.Primary
                : ((sig > 0.1) ? MeterStyleRole.Warning : MeterStyleRole.Danger);
            Color sigColor = WidgetStyleManager.Meter(barRole, theme);

            if (_lastLitBars.Update(litBars))
            {
                for (int i = 0; i < MainBarCount; i++)
                {
                    if (_mainSignalBars[i] == null) continue;
                    if (i < litBars)
                    {
                        _mainSignalBars[i].color = sigColor;
                    }
                    else
                    {
                        _mainSignalBars[i].color = WidgetStyleManager.WithAlpha(sigColor, style.GetLineAlpha(LineWeight.Ghost, theme));
                    }
                }
            }

            // 2. 控制权徽章 (FULL / PART / NONE)
            int ctrlState = state.CtrlState;
            if (_lastCtrlState.Update(ctrlState) && _ctrlBadgeText != null && _ctrlBadgeBg != null)
            {
                if (ctrlState == 0)
                {
                    _ctrlBadgeText.text = I18n.Tr("WIDGET_SIGNAL_NONE", "NONE");
                    ApplyText(_ctrlBadgeText, TextStyleRole.Danger, theme);
                    _ctrlBadgeBg.color = style.GetStatusSurfaceColor(StatusSurfaceRole.Danger, theme);
                }
                else if (ctrlState == 1)
                {
                    _ctrlBadgeText.text = I18n.Tr("WIDGET_SIGNAL_PART", "PART");
                    ApplyText(_ctrlBadgeText, TextStyleRole.Warning, theme);
                    _ctrlBadgeBg.color = style.GetStatusSurfaceColor(StatusSurfaceRole.Caution, theme);
                }
                else
                {
                    _ctrlBadgeText.text = I18n.Tr("WIDGET_SIGNAL_FULL", "FULL");
                    ApplyText(_ctrlBadgeText, TextStyleRole.Accent, theme);
                    _ctrlBadgeBg.color = style.GetStatusSurfaceColor(StatusSurfaceRole.Success, theme);
                }
            }

            // 3. 主站点名称与综合速率
            if (_targetNameText != null && _lastTargetName.Update(state.TargetName))
            {
                _targetNameText.text = state.TargetName;
            }

            if (_rateSummaryText != null && state.RateSummary != null && _lastRateSummary.Update(state.RateSummary))
            {
                _rateSummaryText.text = state.RateSummary;
            }

            // 4. 抽屉矩阵更新
            if (_dropdownPanel != null && _dropdownPanel.activeSelf)
            {
                if (_matrixSummaryText != null && state.MatrixSummary != null && _lastMatrixSummary.Update(state.MatrixSummary))
                {
                    _matrixSummaryText.text = state.MatrixSummary;
                }

                int linkCount = state.LinkCount;

                for (int r = 0; r < MaxPeerRows; r++)
                {
                    if (r < linkCount)
                    {
                        var info = state.GetPeer(r);
                        _peerRows[r].RowObj.SetActive(true);
                        _peerRows[r].NameText.text = info.PeerName;
                        _peerRows[r].TagText.text = info.IsDirectHome ? I18n.Tr("WIDGET_SIGNAL_DSN", "DSN") : I18n.Tr("WIDGET_SIGNAL_RELAY", "RELAY");
                        _peerRows[r].RateText.text = info.FormattedDataRate;

                        int peerBars = info.PeerBars;
                        for (int b = 0; b < 5; b++)
                        {
                            if (_peerRows[r].MiniBars[b] != null)
                            {
                                _peerRows[r].MiniBars[b].color = (b < peerBars)
                                    ? sigColor
                                    : WidgetStyleManager.WithAlpha(sigColor, style.GetLineAlpha(LineWeight.Ghost, theme));
                            }
                        }
                    }
                    else
                    {
                        _peerRows[r].RowObj.SetActive(false);
                    }
                }

                if (_matrixFooterText != null && state.MatrixFooter != null && _lastMatrixFooter.Update(state.MatrixFooter))
                {
                    _matrixFooterText.text = state.MatrixFooter;
                }
            }

            // 5. 原版通信栏开关文本
            if (_stockBtnText != null)
            {
                string newStockText = _stockHidden.Value ? "KSP" : "MFP";
                if (_lastStockBtnText.Update(newStockText))
                {
                    _stockBtnText.text = newStockText;
                    ApplyText(_stockBtnText, _stockHidden.Value ? TextStyleRole.SecondaryValue : TextStyleRole.Accent, theme);
                }
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastTargetName.Reset(string.Empty);
            _lastRateSummary.Reset(string.Empty);
            _lastMatrixSummary.Reset(string.Empty);
            _lastMatrixFooter.Reset(string.Empty);
            _lastStockBtnText.Reset(string.Empty);
            _lastLitBars.Reset(-1);
            _lastCtrlState.Reset(-1);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            _currentTheme = theme;
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_panelBg != null) _panelBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
            if (_panelOutline != null)
            {
                _panelOutline.enabled = true;
                _panelOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }
            if (_targetNameText != null) ApplyText(_targetNameText, TextStyleRole.PrimaryValue, theme);
            if (_rateSummaryText != null) ApplyText(_rateSummaryText, TextStyleRole.SecondaryValue, theme);
            if (_matrixTitleText != null)
            {
                string title = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SIGNAL_TITLE", "REALANTENNAS / COMMNET"));
                _matrixTitleText.text = title;
                ApplyText(_matrixTitleText, TextStyleRole.Label, theme);
            }
            if (_expandBtn != null) _expandBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockBtn != null) _stockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_expandBtnText != null) ApplyText(_expandBtnText, TextStyleRole.SecondaryValue, theme);
            if (_stockBtnText != null) ApplyText(_stockBtnText, TextStyleRole.SecondaryValue, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            _lastCtrlState.Reset(-1);
            _lastMatrixFooter.Reset(string.Empty);
            if (_matrixTitleText != null)
            {
                _matrixTitleText.text = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SIGNAL_TITLE", "REALANTENNAS / COMMNET"));
            }
        }

        private void OnToggleExpand()
        {
            _isExpanded.Value = !_isExpanded.Value;
            _logic.IsDropdownOpen = _isExpanded.Value;
            UpdateExpansionLayout();
        }

        private void UpdateExpansionLayout()
        {
            float s = CurrentDpiScale;
            float dropH = 142f * s;
            float capH = 26f * s;
            float totalH = _isExpanded.Value ? (capH + dropH + 2f * s) : capH;

            RectTransform.sizeDelta = new Vector2(236f * s, totalH);

            if (_capsuleBar != null)
            {
                RectTransform capRt = _capsuleBar.GetComponent<RectTransform>();
                float capY = _isExpanded.Value ? (totalH * 0.5f - capH * 0.5f) : 0f;
                capRt.anchoredPosition = new Vector2(0f, capY);
            }

            if (_dropdownPanel != null)
            {
                _dropdownPanel.SetActive(_isExpanded.Value);
                if (_isExpanded.Value)
                {
                    RectTransform dropRt = _dropdownPanel.GetComponent<RectTransform>();
                    float dropY = -totalH * 0.5f + dropH * 0.5f;
                    dropRt.anchoredPosition = new Vector2(0f, dropY);
                }
            }

            if (_expandBtnText != null)
            {
                _expandBtnText.text = _isExpanded ? "▴" : "▾";
            }
        }

        private void OnToggleStock()
        {
            _stockHidden.Value = !_stockHidden.Value;
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden.Value);
            ThemeManager.IsStockCommNetHidden = _stockHidden.Value;
            ThemeManager.Instance.SaveSettings();
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_expandBtn != null) _expandBtn.onClick.RemoveAllListeners();
            if (_stockBtn != null) _stockBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
