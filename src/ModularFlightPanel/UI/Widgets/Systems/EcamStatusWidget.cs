using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// HUD 飞行阶段与系统备忘横条 (HUD Flight Phase & Systems Memo Bar)
    /// 紧凑型航电抬头状态横条：
    /// 1. 左侧：高对比度动态状态指示柱 (State Bar) 与当前飞行阶段胶囊徽标 (8 级真实物理时序推演)；
    /// 2. 中央：飞控律模式与主告警摘要 (SAS 模式 / 手动操作 / TERRAIN PULL UP 紧急拉起)；
    /// 3. 右侧：当前速度参考系与导航引导态势。
    /// 100% 遵照 SPEC-001..008 核心架构规范，完美兼容既有 core.ecam_status 预设锚点。
    /// </summary>
    [FlightWidget("ecam_status", "status_memo", "flight_memo", "hud_memo",
        Category = WidgetCategory.Systems,
        DisplayName = "HUD 飞行阶段与备忘横条",
        Description = "紧凑型航电抬头备忘横条：飞行阶段时序推演、飞控律模式、关键地形防撞拉起与导航参考系速览。",
        DefaultWidgetId = "core.ecam_status",
        DefaultX = 0f,
        DefaultY = 80f,
        IsSingleton = true,
        ExactIds = new[] { "core.ecam_status" })]
    public class EcamStatusWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(260f, 48f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        /// <summary>
        /// 语义主题管道 (MFP-SPEC-003)：显式接入 WidgetStyleManager 单向主题下发。
        /// 视觉全部来自语义角色微控件，因此由基类把主题推送给全部已注册微控件即可。
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
        }

        // ── 头部集中声明微控件对象 ──
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_EICAS_FLIGHT_MEMO", "飞行告警日志"));
        public TextWidget PhaseBadge = TextWidget.Badge(I18n.Tr("PHASE_PRELAUNCH", "发射前"));

        public TextWidget MainState = new TextWidget(TextStyleRole.Accent, -114f, -4f, 160f, 18f, 12f, TextAnchor.MiddleLeft, "SYSTEMS NOMINAL");
        public TextWidget NavContext = new TextWidget(TextStyleRole.SecondaryValue, 50f, -4f, 70f, 18f, 9.5f, TextAnchor.MiddleRight, "SURFACE");

        public LinearBarWidget StatusAccentBar = LinearBarWidget.BottomBar(MeterStyleRole.Primary, height: 2.5f);

        private string _dataMainStateText = "SYSTEMS NOMINAL";
        private TextStyleRole _dataMainStateRole = TextStyleRole.Accent;
        private string _dataPhaseBadgeText = "STANDBY";
        private TextStyleRole _dataPhaseBadgeRole = TextStyleRole.SecondaryValue;
        private string _dataNavContextText = "SURFACE";
        private float _dataFillAmount = 0.8f;

        private readonly Cached<string> _lastMainStateText = new Cached<string>(string.Empty);
        private readonly Cached<TextStyleRole> _lastMainStateRole = new Cached<TextStyleRole>(TextStyleRole.Accent);
        private readonly Cached<string> _lastPhaseBadgeText = new Cached<string>(string.Empty);
        private readonly Cached<TextStyleRole> _lastPhaseBadgeRole = new Cached<TextStyleRole>(TextStyleRole.SecondaryValue);
        private readonly Cached<string> _lastNavContextText = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastFillAmount = new CachedFloat(-1f);

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataMainStateText = I18n.Tr("STATE_NO_VESSEL_SIGNAL", "NO VESSEL SIGNAL");
                _dataMainStateRole = TextStyleRole.Muted;
                _dataPhaseBadgeText = I18n.Tr("PHASE_STANDBY", "STANDBY");
                _dataPhaseBadgeRole = TextStyleRole.Muted;
                _dataNavContextText = "---";
                _dataFillAmount = 0f;
                return;
            }

            // 1. 紧急触地/近地防撞拉起告警 (最高优先级)
            if (telemetry.IsTouchdownAlert)
            {
                _dataMainStateText = I18n.Tr("ALERT_TERRAIN_PULLUP", "TERRAIN  /  PULL UP");
                _dataMainStateRole = TextStyleRole.Danger;
                _dataPhaseBadgeText = I18n.Tr("PHASE_CRITICAL", "CRITICAL");
                _dataPhaseBadgeRole = TextStyleRole.Danger;
                _dataNavContextText = !string.IsNullOrEmpty(telemetry.SpeedModeName) ? telemetry.SpeedModeName.ToUpperInvariant() : "SURFACE";
                _dataFillAmount = 1.0f;
                return;
            }

            // 2. 飞控操纵律与主状态摘要
            bool isSas = telemetry.IsSASEnabled;
            bool isRcs = telemetry.IsRCSEnabled;
            string sasMode = telemetry.CurrentSASMode.ToString().ToUpperInvariant();

            if (isSas)
            {
                _dataMainStateText = $"SAS: {sasMode}";
                _dataMainStateRole = TextStyleRole.Accent;
                _dataFillAmount = 0.8f;
            }
            else
            {
                _dataMainStateText = isRcs ? I18n.Tr("STATE_MANUAL_RCS", "MANUAL FLIGHT (RCS)") : I18n.Tr("STATE_MANUAL", "MANUAL FLIGHT");
                _dataMainStateRole = TextStyleRole.PrimaryValue;
                _dataFillAmount = 0.4f;
            }

            // 3. 参考系模式
            string frameStr = !string.IsNullOrEmpty(telemetry.SpeedModeName) ? telemetry.SpeedModeName.ToUpperInvariant() : "SURFACE";
            _dataNavContextText = frameStr;

            // 4. 真实物理时序飞行阶段推演
            UpdateFlightPhase(telemetry);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_lastMainStateText.Update(_dataMainStateText)) MainState.Text = _dataMainStateText;
            if (_lastMainStateRole.Update(_dataMainStateRole)) MainState.SetRole(_dataMainStateRole);
            if (_lastPhaseBadgeText.Update(_dataPhaseBadgeText)) PhaseBadge.Text = _dataPhaseBadgeText;
            if (_lastPhaseBadgeRole.Update(_dataPhaseBadgeRole)) PhaseBadge.SetRole(_dataPhaseBadgeRole);
            if (_lastNavContextText.Update(_dataNavContextText)) NavContext.Text = _dataNavContextText;
            if (_lastFillAmount.Update(_dataFillAmount)) StatusAccentBar.FillAmount = _dataFillAmount;
        }

        private void UpdateFlightPhase(IFlightTelemetry telemetry)
        {
            double ralt = telemetry.AltitudeAGL;
            double vsi = telemetry.VerticalSpeed;
            double ap = telemetry.Apoapsis;
            double pe = telemetry.Periapsis;
            double atmDepth = telemetry.AtmosphereDepth;
            bool hasAtm = telemetry.HasAtmosphere;
            double ecc = ExternalProbeRegistry.ResolveNumeric("ORBIT", "ECC");
            if (double.IsNaN(ecc) || ecc < 0.0)
            {
                double rA = Math.Max(10000.0, 600000.0 + ap);
                double rP = 600000.0 + pe;
                ecc = rP <= 0.0 ? 1.05 : Math.Max(0.0, (rA - rP) / (rA + rP));
            }
            double spd = telemetry.SurfaceSpeed;

            // 状态机推演
            if (ralt < 15.0 && spd < 1.0)
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_PRELAUNCH", "PRE-LAUNCH");
                _dataPhaseBadgeRole = TextStyleRole.SecondaryValue;
            }
            else if (hasAtm && ralt < atmDepth && vsi > 5.0 && pe < 0.0)
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_ASCENT", "ASCENT 爬升");
                _dataPhaseBadgeRole = TextStyleRole.PrimaryValue;
            }
            else if (hasAtm && ralt < atmDepth && vsi < -10.0 && spd > 1200.0)
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_REENTRY", "RE-ENTRY 再入");
                _dataPhaseBadgeRole = TextStyleRole.Warning;
            }
            else if (ecc >= 1.0)
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_ESCAPE", "ESCAPE 逃逸");
                _dataPhaseBadgeRole = TextStyleRole.Danger;
            }
            else if (pe > (hasAtm ? atmDepth : 0.0))
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_ORBIT", "ORBIT 轨道巡航");
                _dataPhaseBadgeRole = TextStyleRole.Accent;
            }
            else if (vsi < -2.0 && ralt < 2000.0)
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_LANDING", "LANDING 进近着陆");
                _dataPhaseBadgeRole = TextStyleRole.Warning;
            }
            else
            {
                _dataPhaseBadgeText = I18n.Tr("PHASE_SUBORBIT", "SUBORBITAL 亚轨道");
                _dataPhaseBadgeRole = TextStyleRole.SecondaryValue;
            }
        }
    }
}
