using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 轨道力学综合态势面板 (Orbital Dynamics & Kinematics Card)
    /// 现代航电双列 6 参量轨道力学监控卡片：
    /// 左列：远地点 (AP) 与近地点 (PE) 绝对高度、到达倒计时 (T-AP / T-PE)；
    /// 右列：轨道偏心率 (Ecc)、轨道倾角 (Inc) 及顺/逆行极向标、轨道周期 (Period)；
    /// 顶部状态微标：根据轨道能量状态机动态推演 SUBORBITAL / CIRCULAR / ELLIPTIC / ESCAPE 并变色。
    /// 100% 遵照 SPEC-001..008 核心架构规范。
    /// </summary>
    [FlightWidget("orbital_info", "orbit", "orbital", "orbital_dynamics",
        Category = WidgetCategory.Navigation,
        DisplayName = "ORBITAL 轨道动力学面板",
        Description = "轨道力学六根数综合面板：远地点/近地点、拱点倒计时、偏心率、倾角、周期与轨道动力学状态胶囊。",
        DefaultWidgetId = "core.orbital_info",
        DefaultX = 0f,
        DefaultY = 180f,
        IsSingleton = true,
        ExactIds = new[] { "core.orbital_info" })]
    public class OrbitalInfoWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(260f, 96f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        /// <summary>
        /// 语义主题管道 (MFP-SPEC-003)：显式接入 WidgetStyleManager 单向主题下发。
        /// 视觉全部来自语义角色微控件，因此由基类把主题推送给全部已注册微控件即可。
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
        }

        // ── 顶部系统标题与轨道动力学能量胶囊 ──
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_NAV_ORBITAL_DYNAMICS", "轨道动力学"));
        public TextWidget OrbitBadge = TextWidget.Badge(I18n.Tr("WIDGET_STATUS_SUBORBITAL", "亚轨道"));

        // ── 左列：拱点几何与倒计时 (X = -60f) ──
        public TextWidget ApLabel = new TextWidget(TextStyleRole.Label, -120f, 16f, 26f, 16f, 8.5f, TextAnchor.MiddleLeft, "AP");
        public TextWidget ApVal = new TextWidget(TextStyleRole.PrimaryValue, -94f, 16f, 80f, 16f, 11f, TextAnchor.MiddleRight, "---");
        public TextWidget ApUnit = new TextWidget(TextStyleRole.Unit, -14f, 16f, 0f, 16f, 8f, TextAnchor.MiddleLeft, "");

        public TextWidget PeLabel = new TextWidget(TextStyleRole.Label, -120f, -6f, 26f, 16f, 8.5f, TextAnchor.MiddleLeft, "PE");
        public TextWidget PeVal = new TextWidget(TextStyleRole.PrimaryValue, -94f, -6f, 80f, 16f, 11f, TextAnchor.MiddleRight, "---");
        public TextWidget PeUnit = new TextWidget(TextStyleRole.Unit, -14f, -6f, 0f, 16f, 8f, TextAnchor.MiddleLeft, "");

        public TextWidget TimeReadout = new TextWidget(TextStyleRole.SecondaryValue, -120f, -28f, 112f, 16f, 8f, TextAnchor.MiddleLeft, "T-AP: --:--  PE: --:--");

        // ── 右列：轨道形态六根数 (X = 60f) ──
        public TextWidget EccLabel = new TextWidget(TextStyleRole.Label, 12f, 16f, 28f, 16f, 8.5f, TextAnchor.MiddleLeft, "ECC");
        public TextWidget EccVal = new TextWidget(TextStyleRole.PrimaryValue, 42f, 16f, 68f, 16f, 11f, TextAnchor.MiddleRight, "0.000");

        public TextWidget IncLabel = new TextWidget(TextStyleRole.Label, 12f, -6f, 28f, 16f, 8.5f, TextAnchor.MiddleLeft, "INC");
        public TextWidget IncVal = new TextWidget(TextStyleRole.PrimaryValue, 42f, -6f, 50f, 16f, 11f, TextAnchor.MiddleRight, "0.0°");
        public TextWidget IncDir = new TextWidget(TextStyleRole.Unit, 94f, -6f, 22f, 16f, 8f, TextAnchor.MiddleLeft, "PRO");

        public TextWidget PeriodLabel = new TextWidget(TextStyleRole.Label, 12f, -28f, 28f, 16f, 8.5f, TextAnchor.MiddleLeft, "PER");
        public TextWidget PeriodVal = new TextWidget(TextStyleRole.SecondaryValue, 42f, -28f, 74f, 16f, 8.5f, TextAnchor.MiddleRight, "--m --s");

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                ApVal.Text = "---";
                PeVal.Text = "---";
                TimeReadout.Text = I18n.Tr("ORBIT_TIME_PLACEHOLDER", "T-AP: --:--  PE: --:--");
                EccVal.Text = "---";
                IncVal.Text = "---";
                PeriodVal.Text = "---";
                OrbitBadge.Text = I18n.Tr("ORBIT_NO_VESSEL", "NO VESSEL");
                OrbitBadge.SetRole(TextStyleRole.Muted);
                return;
            }

            // 1. 远拱点 AP 与近拱点 PE
            double ap = telemetry.Apoapsis;
            double pe = telemetry.Periapsis;
            ApVal.Text = FormatMetricDistance(ap);
            PeVal.Text = pe < -100000.0 ? I18n.Tr("ORBIT_VAL_IMPACT", "IMPACT") : FormatMetricDistance(pe);

            // 2. 拱点时间倒计时
            double tAp = telemetry.TimeToAp;
            double tPe = telemetry.TimeToPe;
            string tApStr = FormatDurationCompact(tAp);
            string tPeStr = FormatDurationCompact(tPe);
            TimeReadout.Text = $"T-AP {tApStr}  PE {tPeStr}";

            // 3. 轨道偏心率 Ecc (优先检索探针，无缝开普勒几何推算 Fallback)
            double ecc = ExternalProbeRegistry.ResolveNumeric("ORBIT", "ECC");
            if (double.IsNaN(ecc) || ecc < 0.0)
            {
                double rA = Math.Max(10000.0, 600000.0 + ap);
                double rP = 600000.0 + pe;
                ecc = rP <= 0.0 ? 1.05 : Math.Max(0.0, (rA - rP) / (rA + rP));
            }
            EccVal.Text = ecc.ToString("F3");

            // 4. 轨道倾角 Inc (探针优先)
            double inc = ExternalProbeRegistry.ResolveNumeric("ORBIT", "INC");
            if (double.IsNaN(inc)) inc = 0.0;
            IncVal.Text = $"{inc:F1}°";
            if (inc > 90.0)
            {
                IncDir.Text = I18n.Tr("ORBIT_DIR_RET", "RET"); // 逆行
                IncDir.SetRole(TextStyleRole.Warning);
            }
            else
            {
                IncDir.Text = I18n.Tr("ORBIT_DIR_PRO", "PRO"); // 顺行
                IncDir.SetRole(TextStyleRole.Unit);
            }

            // 5. 轨道周期 Period (探针优先或基于拱点时钟推算)
            double period = ExternalProbeRegistry.ResolveNumeric("ORBIT", "PERIOD");
            if (double.IsNaN(period) || period <= 0.0)
            {
                period = ecc < 1.0 ? Math.Abs(tAp - tPe) * 2.0 : 0.0;
            }
            PeriodVal.Text = FormatPeriod(period);

            // 6. 轨道动力学能量状态胶囊
            UpdateOrbitState(telemetry, ap, pe, ecc);
        }

        private void UpdateOrbitState(IFlightTelemetry telemetry, double ap, double pe, double ecc)
        {
            double atmDepth = telemetry.AtmosphereDepth;
            bool hasAtm = telemetry.HasAtmosphere;
            double safeAlt = hasAtm ? atmDepth : 0.0;

            if (ecc >= 1.0)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_ESCAPE", "ESCAPE 逃逸");
                OrbitBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe < safeAlt)
            {
                if (pe < 0.0)
                {
                    OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_BALLISTIC", "BALLISTIC 撞击");
                    OrbitBadge.SetRole(TextStyleRole.Danger);
                }
                else
                {
                    OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_SUBORBIT", "SUBORBITAL 亚轨道");
                    OrbitBadge.SetRole(TextStyleRole.Warning);
                }
            }
            else if (ecc < 0.015)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_CIRCULAR", "CIRCULAR 圆轨道");
                OrbitBadge.SetRole(TextStyleRole.Accent);
            }
            else
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_ELLIPTIC", "ELLIPTIC 椭圆轨");
                OrbitBadge.SetRole(TextStyleRole.PrimaryValue);
            }
        }

        private static string FormatPeriod(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0) return "---";
            if (seconds >= 86400.0) return $"{seconds / 86400.0:F1}d";
            int sec = (int)seconds;
            int h = sec / 3600;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            if (h > 0) return $"{h}h {m}m";
            return $"{m}m {s}s";
        }
    }
}
