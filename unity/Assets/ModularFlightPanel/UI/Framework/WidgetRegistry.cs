using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Widgets.Navigation;
using ModularFlightPanel.UI.Widgets.SpaceX;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 飞行仪表组件声明式元数据特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class FlightWidgetAttribute : Attribute
    {
        public string TypeName { get; }
        public string[] Aliases { get; }

        public FlightWidgetAttribute(string typeName, params string[] aliases)
        {
            TypeName = typeName;
            Aliases = aliases ?? new string[0];
        }
    }

    /// <summary>
    /// 全局飞行仪表组件注册表与泛型工厂中枢 (Widget Registry & Generic Factory)
    /// 彻底消除 FlightHUDManager 中 30+ 重复 SpawnXxx 方法与巨型 if-else/switch 胶水代码，落实开闭原则 (OCP)。
    /// </summary>
    public static class WidgetRegistry
    {
        private static readonly Dictionary<string, Type> _registry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Type> _exactIdRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        static WidgetRegistry()
        {
            RegisterDefaults();
        }

        public static void Register<T>(string typeName, params string[] aliases) where T : BaseFlightWidget
        {
            Register(typeName, typeof(T), aliases);
        }

        public static void Register(string typeName, Type widgetType, params string[] aliases)
        {
            if (string.IsNullOrEmpty(typeName) || widgetType == null) return;
            if (!typeof(BaseFlightWidget).IsAssignableFrom(widgetType))
            {
                throw modernArgumentException($"Type {widgetType.FullName} must inherit from BaseFlightWidget.");
            }

            _registry[typeName] = widgetType;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    if (!string.IsNullOrEmpty(aliases[i]))
                    {
                        _registry[aliases[i]] = widgetType;
                    }
                }
            }
        }

        public static void RegisterExactId<T>(string widgetId) where T : BaseFlightWidget
        {
            if (!string.IsNullOrEmpty(widgetId))
            {
                _exactIdRegistry[widgetId] = typeof(T);
            }
        }

        private static Exception modernArgumentException(string message)
        {
            return new ArgumentException(message);
        }

        private static void RegisterDefaults()
        {
            // Gauges & Dials
            Register<TapeGaugeWidget>("tape", "tape_gauge", "speed_tape", "altitude_tape");
            Register<EcamDialGaugeWidget>("ecam_dial", "ecam_gauge", "dial");
            Register<AvionicsBarGaugeWidget>("bar_gauge", "bar", "avionics_bar");
            Register<ArcMeterWidget>("arc_meter", "meter_arc");
            Register<CustomTokenTextWidget>("custom_token", "custom_text", "custom");

            // Navigation
            Register<NavballSphereWidget>("navball", "navball_sphere");
            Register<VesselAttitudeSphereWidget>("vessel_navball", "vessel_attitude_sphere", "attitude_sphere");
            Register<HeadingArcWidget>("heading_arc", "heading", "compass_arc");
            Register<NDNavigationWidget>("nd_navigation", "nd", "navigation_display");
            Register<OrbitalInfoWidget>("orbital_info", "orbit", "orbital");
            Register<ManeuverNodeWidget>("maneuver", "maneuver_node");
            Register<ManeuverTimelineWidget>("maneuver_timeline", "burn_timeline");

            // Controls
            Register<SASDialWidget>("sas_dial", "sas_compass", "sas");
            Register<StageControlWidget>("stage_control", "staging_ctrl");
            Register<StagingSequenceWidget>("staging_sequence", "stage_sequence");
            Register<TimeWarpWidget>("time_warp", "timewarp", "warp_control");
            Register<BottomControlsWidget>("bottom_controls", "bottom_bar_controls");
            Register<ModernToolbarWidget>("toolbar", "modern_toolbar", "dock");

            // Systems
            Register<MasterWarningWidget>("master_warning", "warning_annunciator", "annunciator", "cws");
            Register<EcamStatusWidget>("ecam_status", "status_memo");
            Register<B747EicasWidget>("b747_eicas", "boeing_eicas", "eicas");
            Register<B747LowerEicasWidget>("b747_lower_eicas", "eicas_lower");
            Register<ElectricalSystemWidget>("electrical", "elec", "power_grid");
            Register<Rocket2DWidget>("rocket2d", "rocket", "staging_diagram");
            Register<LifeSupportWidget>("life_support", "life", "ecls");
            Register<SignalStatusWidget>("signal", "signal_list", "antenna");
            Register<StageDeltaVWidget>("stage_dv", "deltav", "stage_delta_v");
            Register<CommSignalWidget>("comm_signal", "commsignal");
            Register<PerformanceMonitorWidget>("performance_monitor", "perf_monitor", "profiler");
            Register<UIWidget>("ui_widget", "ui_manager", "dock_manager");

            // SpaceX Cockpit
            Register<SpaceXHeaderWidget>("spacex_header", "dragon_header");
            Register<SpaceXDockingReticleWidget>("spacex_docking", "dragon_docking");
            Register<SpaceXOverviewWidget>("spacex_overview", "dragon_overview");
            Register<SpaceXBottomBarWidget>("spacex_bottom", "dragon_bottom");
            Register<SpaceXArcGaugeWidget>("spacex_arc", "spacex_gauge");
            Register<SpaceXTimelineWidget>("spacex_timeline", "dragon_timeline");
            Register<SpaceXAttitudeWidget>("spacex_attitude", "dragon_attitude");
            Register<SpaceXEngineWidget>("spacex_engines", "dragon_engines");

            // 精准 WidgetId 映射 (针对原版/预设中的固定标识)
            RegisterExactId<MasterWarningWidget>("core.master_warning");
            RegisterExactId<NavballSphereWidget>("core.navball");
            RegisterExactId<VesselAttitudeSphereWidget>("nav.vessel_navball");
            RegisterExactId<VesselAttitudeSphereWidget>("nav.vessel_attitude_sphere");
            RegisterExactId<VesselAttitudeSphereWidget>("core.vessel_navball");
            RegisterExactId<VesselAttitudeSphereWidget>("core.vessel_attitude_sphere");
            RegisterExactId<VesselAttitudeSphereWidget>("nav.attitude_sphere_3d");

            RegisterExactId<HeadingArcWidget>("core.heading_arc");
            RegisterExactId<NDNavigationWidget>("custom.nd_navigation");
            RegisterExactId<NDNavigationWidget>("core.nd_arc");
            RegisterExactId<NDNavigationWidget>("core.nd_navigation");

            RegisterExactId<ArcMeterWidget>("core.throttle");
            RegisterExactId<ArcMeterWidget>("core.vsi");
            RegisterExactId<ArcMeterWidget>("core.propellant");

            RegisterExactId<BottomControlsWidget>("core.bottom_controls");
            RegisterExactId<StageControlWidget>("core.stage_control");
            RegisterExactId<EcamStatusWidget>("core.ecam_status");
            RegisterExactId<OrbitalInfoWidget>("core.orbital_info");
            RegisterExactId<SASDialWidget>("core.sas_dial");

            RegisterExactId<B747EicasWidget>("custom.b747_eicas");
            RegisterExactId<B747EicasWidget>("core.b747_eicas");
            RegisterExactId<B747LowerEicasWidget>("custom.b747_lower_eicas");
            RegisterExactId<B747LowerEicasWidget>("core.b747_lower_eicas");

            RegisterExactId<ElectricalSystemWidget>("custom.electrical");
            RegisterExactId<ElectricalSystemWidget>("custom.elec");
            RegisterExactId<ElectricalSystemWidget>("core.electrical");

            RegisterExactId<Rocket2DWidget>("custom.rocket");
            RegisterExactId<Rocket2DWidget>("custom.stage");
            RegisterExactId<Rocket2DWidget>("custom.staging");
            RegisterExactId<Rocket2DWidget>("core.rocket2d");

            RegisterExactId<LifeSupportWidget>("custom.life");
            RegisterExactId<LifeSupportWidget>("custom.life_support");
            RegisterExactId<LifeSupportWidget>("core.life_support");

            RegisterExactId<SignalStatusWidget>("custom.signal");
            RegisterExactId<SignalStatusWidget>("custom.signal_list");
            RegisterExactId<SignalStatusWidget>("core.signal");

            RegisterExactId<TimeWarpWidget>("core.time_warp");
            RegisterExactId<TimeWarpWidget>("core.timewarp");

            RegisterExactId<CommSignalWidget>("core.comm_signal");
            RegisterExactId<CommSignalWidget>("core.commsignal");

            RegisterExactId<StageDeltaVWidget>("gauge.stage_dv");
            RegisterExactId<StageDeltaVWidget>("custom.stage_dv");
            RegisterExactId<StageDeltaVWidget>("core.stage_dv");

            RegisterExactId<StagingSequenceWidget>("custom.staging_sequence");
            RegisterExactId<StagingSequenceWidget>("custom.stage_sequence");
            RegisterExactId<StagingSequenceWidget>("core.staging_sequence");

            RegisterExactId<ManeuverTimelineWidget>("custom.maneuver_timeline");
            RegisterExactId<ManeuverTimelineWidget>("core.maneuver_timeline");

            RegisterExactId<ManeuverNodeWidget>("core.maneuver");
            RegisterExactId<ModernToolbarWidget>("core.toolbar");
            RegisterExactId<PerformanceMonitorWidget>("core.performance_monitor");
            RegisterExactId<PerformanceMonitorWidget>("custom.perf_monitor");
            RegisterExactId<UIWidget>("core.ui_widget");
            RegisterExactId<UIWidget>("custom.ui_widget");

            RegisterExactId<SpaceXHeaderWidget>("spacex.header");
            RegisterExactId<SpaceXDockingReticleWidget>("spacex.docking");
            RegisterExactId<SpaceXOverviewWidget>("spacex.overview");
            RegisterExactId<SpaceXBottomBarWidget>("spacex.bottom");
            RegisterExactId<SpaceXTimelineWidget>("spacex.timeline");
            RegisterExactId<SpaceXAttitudeWidget>("spacex.attitude");
            RegisterExactId<SpaceXEngineWidget>("spacex.engines");
        }

        /// <summary>
        /// 根据 WidgetConfig 智能推导对应的组件实现类型
        /// </summary>
        public static Type ResolveWidgetType(WidgetConfig cfg)
        {
            if (cfg == null) return null;

            // 1. 显式 WidgetType 查表
            if (!string.IsNullOrEmpty(cfg.WidgetType) && _registry.TryGetValue(cfg.WidgetType, out var typeFromType))
            {
                return typeFromType;
            }

            // 2. 精确 WidgetId 查表
            if (!string.IsNullOrEmpty(cfg.WidgetId) && _exactIdRegistry.TryGetValue(cfg.WidgetId, out var typeFromExactId))
            {
                return typeFromExactId;
            }

            // 3. 通配前缀规则推导 (Prefix matching)
            string id = cfg.WidgetId ?? string.Empty;
            if (id.StartsWith("tape.", StringComparison.OrdinalIgnoreCase)) return typeof(TapeGaugeWidget);
            if (id.StartsWith("ecam.", StringComparison.OrdinalIgnoreCase)) return typeof(EcamDialGaugeWidget);
            if (id.StartsWith("gauge.", StringComparison.OrdinalIgnoreCase)) return typeof(AvionicsBarGaugeWidget);
            if (id.StartsWith("maneuver.", StringComparison.OrdinalIgnoreCase)) return typeof(ManeuverNodeWidget);
            if (id.StartsWith("toolbar.", StringComparison.OrdinalIgnoreCase)) return typeof(ModernToolbarWidget);

            if (id.StartsWith("spacex.arc", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("spacex.speed", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("spacex.altitude", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(SpaceXArcGaugeWidget);
            }
            if (id.StartsWith("spacex.header", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXHeaderWidget);
            if (id.StartsWith("spacex.docking", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXDockingReticleWidget);
            if (id.StartsWith("spacex.overview", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXOverviewWidget);
            if (id.StartsWith("spacex.bottom", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXBottomBarWidget);
            if (id.StartsWith("spacex.time", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXTimelineWidget);
            if (id.StartsWith("spacex.attitude", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXAttitudeWidget);
            if (id.StartsWith("spacex.engine", StringComparison.OrdinalIgnoreCase)) return typeof(SpaceXEngineWidget);

            // 4. 自定义通配符组件 (CustomTokenTextWidget)
            if (id.StartsWith("custom.", StringComparison.OrdinalIgnoreCase)) return typeof(CustomTokenTextWidget);

            // 5. 缺省返回通用通配符文本组件
            return typeof(CustomTokenTextWidget);
        }

        /// <summary>
        /// 泛型组件实例化工厂 (单点装配)
        /// </summary>
        public static BaseFlightWidget Spawn(WidgetConfig cfg, ThemeConfig theme, Transform parent, Canvas canvas, float scale)
        {
            if (cfg == null) return null;

            Type widgetType = ResolveWidgetType(cfg);
            if (widgetType == null)
            {
                MFPLogger.Warn("WidgetRegistry", $"Unable to resolve widget type for id: {cfg.WidgetId}, type: {cfg.WidgetType}");
                return null;
            }

            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var widget = (BaseFlightWidget)go.AddComponent(widgetType);
            widget.BaseInitialize(parent, canvas, cfg, theme, scale);
            return widget;
        }

        /// <summary>
        /// 强类型泛型组件实例化
        /// </summary>
        public static T Spawn<T>(WidgetConfig cfg, ThemeConfig theme, Transform parent, Canvas canvas, float scale) where T : BaseFlightWidget
        {
            if (cfg == null) return null;

            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var widget = go.AddComponent<T>();
            widget.BaseInitialize(parent, canvas, cfg, theme, scale);
            return widget;
        }
    }
}
