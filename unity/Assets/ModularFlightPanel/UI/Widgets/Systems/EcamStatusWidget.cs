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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // ── 头部集中声明微控件对象 ──
        public TextWidget Title = TextWidget.Title("FLIGHT MEMO");
        public TextWidget PhaseBadge = TextWidget.Badge("PRE-LAUNCH");

        public TextWidget MainState = new TextWidget(TextStyleRole.Accent, -114f, -4f, 160f, 18f, 12f, TextAnchor.MiddleLeft, "SYSTEMS NOMINAL");
        public TextWidget NavContext = new TextWidget(TextStyleRole.SecondaryValue, 50f, -4f, 70f, 18f, 9.5f, TextAnchor.MiddleRight, "SURFACE");

        public LinearBarWidget StatusAccentBar = LinearBarWidget.BottomBar(MeterStyleRole.Primary, height: 2.5f);

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                MainState.Text = "NO VESSEL SIGNAL";
                MainState.SetRole(TextStyleRole.Muted);
                PhaseBadge.Text = "STANDBY";
                PhaseBadge.SetRole(TextStyleRole.Muted);
                NavContext.Text = "---";
                StatusAccentBar.FillAmount = 0f;
                return;
            }

            // 1. 紧急触地/近地防撞拉起告警 (最高优先级)
            if (telemetry.IsTouchdownAlert)
            {
                MainState.Text = "TERRAIN  /  PULL UP";
                MainState.SetRole(TextStyleRole.Danger);
                PhaseBadge.Text = "CRITICAL";
                PhaseBadge.SetRole(TextStyleRole.Danger);
                StatusAccentBar.FillAmount = 1.0f;
                return;
            }

            // 2. 飞控操纵律与主状态摘要
            bool isSas = telemetry.IsSASEnabled;
            bool isRcs = telemetry.IsRCSEnabled;
            string sasMode = telemetry.CurrentSASMode.ToString().ToUpperInvariant();

            if (isSas)
            {
                MainState.Text = $"SAS: {sasMode}";
                MainState.SetRole(TextStyleRole.Accent);
                StatusAccentBar.FillAmount = 0.8f;
            }
            else
            {
                MainState.Text = isRcs ? "MANUAL FLIGHT (RCS)" : "MANUAL FLIGHT";
                MainState.SetRole(TextStyleRole.PrimaryValue);
                StatusAccentBar.FillAmount = 0.4f;
            }

            // 3. 参考系模式
            string frameStr = !string.IsNullOrEmpty(telemetry.SpeedModeName) ? telemetry.SpeedModeName.ToUpperInvariant() : "SURFACE";
            NavContext.Text = frameStr;

            // 4. 真实物理时序飞行阶段推演
            UpdateFlightPhase(telemetry);
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
                PhaseBadge.Text = "PRE-LAUNCH";
                PhaseBadge.SetRole(TextStyleRole.SecondaryValue);
            }
            else if (hasAtm && ralt < atmDepth && vsi > 5.0 && pe < 0.0)
            {
                PhaseBadge.Text = "ASCENT 爬升";
                PhaseBadge.SetRole(TextStyleRole.PrimaryValue);
            }
            else if (hasAtm && ralt < atmDepth && vsi < -10.0 && spd > 1200.0)
            {
                PhaseBadge.Text = "RE-ENTRY 再入";
                PhaseBadge.SetRole(TextStyleRole.Warning);
            }
            else if (ecc >= 1.0)
            {
                PhaseBadge.Text = "ESCAPE 逃逸";
                PhaseBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe > (hasAtm ? atmDepth : 0.0))
            {
                PhaseBadge.Text = "ORBIT 轨道巡航";
                PhaseBadge.SetRole(TextStyleRole.Accent);
            }
            else if (vsi < -2.0 && ralt < 2000.0)
            {
                PhaseBadge.Text = "LANDING 进近着陆";
                PhaseBadge.SetRole(TextStyleRole.Warning);
            }
            else
            {
                PhaseBadge.Text = "SUBORBITAL 亚轨道";
                PhaseBadge.SetRole(TextStyleRole.SecondaryValue);
            }
        }
    }
}
