using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 通信信号纯状态快照 (0 GC 纯值结构体)
    /// </summary>
    public struct CommSignalState
    {
        public bool HasVessel;
        public double RawSignal;
        public int LitBars;
        public int ControlState; // 0=None, 1=Partial, 2=Full
        public string TargetName;
        public string FormattedPercent;
        public string ControlBadgeText;
        public StatusSurfaceRole ControlBadgeRole;
    }

    /// <summary>
    /// 通信信号纯解算大脑 (Headless Pure C# Logic Engine)
    /// 完全脱离 UnityEngine，支持无头仿真、离线单元测试与零 GC 解算。
    /// </summary>
    public class CommSignalLogic : WidgetLogic<CommSignalState>
    {
        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                Reset();
                return;
            }

            double rawSig = Math.Max(0.0, Math.Min(1.0, telemetry.CommSignal));
            int litBars = (int)Math.Round(rawSig * 5.0);
            int ctrlState = (!telemetry.IsConnected && rawSig <= 0.001) ? 0 : ((rawSig < 0.2) ? 1 : 2);

            string tgt = telemetry.DirectLinkTarget;
            string targetName = string.IsNullOrEmpty(tgt)
                ? I18n.Tr("WIDGET_SIG_COMMNET", "通信网络")
                : tgt;

            string percentStr = $"{(int)(rawSig * 100.0)}%";

            string badgeText;
            StatusSurfaceRole badgeRole;
            switch (ctrlState)
            {
                case 2:
                    badgeText = I18n.Tr("WIDGET_SIGNAL_FULL", "满格");
                    badgeRole = StatusSurfaceRole.Success;
                    break;
                case 1:
                    badgeText = I18n.Tr("WIDGET_SIG_CTRL_PARTIAL", "部分控制");
                    badgeRole = StatusSurfaceRole.Caution;
                    break;
                case 0:
                default:
                    badgeText = I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路");
                    badgeRole = StatusSurfaceRole.Danger;
                    break;
            }

            CurrentState = new CommSignalState
            {
                HasVessel = true,
                RawSignal = rawSig,
                LitBars = litBars,
                ControlState = ctrlState,
                TargetName = targetName,
                FormattedPercent = percentStr,
                ControlBadgeText = badgeText,
                ControlBadgeRole = badgeRole
            };
        }
    }

    /// <summary>
    /// 第二代航电通信网络信号指示组件 (Comm Signal Widget v2)
    /// 严格遵循 WidgetLogic 大脑与 BaseFlightWidget 视图渲染解耦规范。
    /// </summary>
    [FlightWidget("comm_signal_v2", "commsignal_v2", Category = WidgetCategory.Systems, DisplayName = "COMM v2 天线通信信号条", Description = "第二代通信天线信号条：纯 C# WidgetLogic 解耦大脑与胶囊式紧凑状态条。", DefaultWidgetId = "core.comm_signal_v2", DefaultX = 300f, DefaultY = 200f, IsSingleton = true, ExactIds = new[] { "core.comm_signal_v2" })]
    public class CommSignalWidgetv2 : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(264f, 26f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private readonly CommSignalLogic _logic = new CommSignalLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private const int MainBarCount = 5;
        private GameObject _capsuleBar;
        private readonly Image[] _mainSignalBars = new Image[MainBarCount];
        private Text _ctrlBadgeText;
        private Image _ctrlBadgeBg;
        private Text _targetNameText;
        private Text _rateSummaryText;

        // 智能私有缓存 (SPEC-009)
        private readonly Cached<bool> _lastHasVessel = new Cached<bool>(false);
        private readonly Cached<int> _lastLitBars = new Cached<int>(-1);
        private readonly Cached<int> _lastCtrlState = new Cached<int>(-1);
        private readonly Cached<string> _lastTargetName = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPercent = new Cached<string>(string.Empty);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 baseSize = new Vector2(264f * s, 26f * s);
            RectTransform.sizeDelta = baseSize;

            Color primaryAccent = theme.AccentPrimary;
            Color textPrimary = theme.TextPrimaryColor;

            // 1. 胶囊底座
            _capsuleBar = UIFactory.CreatePanel(
                transform,
                "CapsuleBar",
                baseSize,
                Vector2.zero,
                WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost),
                1f * s);

            // 2. 5 阶信号柱
            float barStartX = -baseSize.x * 0.5f + 12f * s;
            float barW = 2.5f * s;
            float barGap = 1.8f * s;
            for (int i = 0; i < MainBarCount; i++)
            {
                float barH = (4f + i * 2f) * s;
                float bx = barStartX + i * (barW + barGap);
                float by = -baseSize.y * 0.5f + 7f * s + barH * 0.5f;

                GameObject bGo = UIFactory.CreatePanel(
                    _capsuleBar.transform,
                    $"SigBar_{i}",
                    new Vector2(barW, barH),
                    new Vector2(bx, by),
                    primaryAccent);
                _mainSignalBars[i] = bGo.GetComponent<Image>();
            }

            // 3. 控制权状态徽章
            Vector2 ctrlSize = new Vector2(36f * s, 14f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(
                _capsuleBar.transform,
                "CtrlBadge",
                ctrlSize,
                new Vector2(-baseSize.x * 0.5f + 48f * s, 0f),
                WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(
                ctrlBg.transform,
                "Text",
                I18n.Tr("WIDGET_SIGNAL_FULL", "满格"),
                Mathf.Max(7, Mathf.RoundToInt(7f * s)),
                TextAnchor.MiddleCenter,
                primaryAccent);
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 4. 主站点/对端名称
            _targetNameText = UIFactory.CreateText(
                _capsuleBar.transform,
                "TargetName",
                I18n.Tr("WIDGET_SIG_COMMNET", "通信网络"),
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)),
                TextAnchor.MiddleLeft,
                textPrimary);
            _targetNameText.fontStyle = FontStyle.Bold;
            RectTransform tgtRt = _targetNameText.GetComponent<RectTransform>();
            tgtRt.sizeDelta = new Vector2(90f * s, 18f * s);
            tgtRt.anchoredPosition = new Vector2(-baseSize.x * 0.5f + 115f * s, 0f);

            // 5. 信号百分比
            _rateSummaryText = UIFactory.CreateText(
                _capsuleBar.transform,
                "RateSummary",
                "100%",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)),
                TextAnchor.MiddleRight,
                theme.AccentSecondary);
            RectTransform rateRt = _rateSummaryText.GetComponent<RectTransform>();
            rateRt.sizeDelta = new Vector2(50f * s, 18f * s);
            rateRt.anchoredPosition = new Vector2(baseSize.x * 0.5f - 30f * s, 0f);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            ApplyText(_targetNameText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_rateSummaryText, TextStyleRole.SecondaryValue, theme);

            var state = _logic.CurrentState;
            UpdateBarsVisual(state.LitBars, theme);
            if (_ctrlBadgeBg != null)
            {
                _ctrlBadgeBg.color = WidgetStyleManager.StatusPanel(state.ControlBadgeRole);
            }
        }

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
            var theme = WidgetStyleManager.Instance?.CurrentTheme;

            if (_lastHasVessel.Update(state.HasVessel))
            {
                if (!state.HasVessel)
                {
                    _targetNameText.text = I18n.Tr("WIDGET_SIG_COMMNET", "通信网络");
                    _rateSummaryText.text = "--";
                    _ctrlBadgeText.text = I18n.Tr("WIDGET_SIG_CTRL_NO_LINK", "无链路");
                    if (_ctrlBadgeBg != null) _ctrlBadgeBg.color = WidgetStyleManager.StatusPanel(StatusSurfaceRole.Danger);
                    UpdateBarsVisual(0, theme);
                    return;
                }
            }

            if (!state.HasVessel) return;

            if (_lastLitBars.Update(state.LitBars))
            {
                UpdateBarsVisual(state.LitBars, theme);
            }

            if (_lastCtrlState.Update(state.ControlState))
            {
                _ctrlBadgeText.text = state.ControlBadgeText;
                if (_ctrlBadgeBg != null)
                {
                    _ctrlBadgeBg.color = WidgetStyleManager.StatusPanel(state.ControlBadgeRole);
                }
            }

            if (_lastTargetName.Update(state.TargetName))
            {
                _targetNameText.text = state.TargetName;
            }

            if (_lastPercent.Update(state.FormattedPercent))
            {
                _rateSummaryText.text = state.FormattedPercent;
            }
        }

        private void UpdateBarsVisual(int litBars, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            Color activeColor = theme.AccentPrimary;
            Color inactiveColor = WidgetStyleManager.WithAlpha(theme.TextPrimaryColor, 0.25f);

            for (int i = 0; i < MainBarCount; i++)
            {
                if (_mainSignalBars[i] != null)
                {
                    _mainSignalBars[i].color = (i < litBars) ? activeColor : inactiveColor;
                }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
