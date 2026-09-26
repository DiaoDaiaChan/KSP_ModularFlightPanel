using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Widgets.Gauges;
using ModularFlightPanel.UI.Widgets.Navigation;
using ModularFlightPanel.UI.Widgets.SpaceX;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 组件所属功能大类 (Widget Category)
    /// </summary>
    public enum WidgetCategory
    {
        All = 0,
        Gauges = 1,
        Navigation = 2,
        Systems = 3,
        SpaceX = 4,
        Controls = 5
    }

    /// <summary>
    /// 飞行仪表组件声明式元数据特性 (Declarative Flight Widget Metadata)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FlightWidgetAttribute : Attribute
    {
        public string TypeName { get; }
        public WidgetCategory Category { get; set; } = WidgetCategory.Gauges;
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string DefaultWidgetId { get; set; }
        public float DefaultX { get; set; }
        public float DefaultY { get; set; }
        public float DefaultWidth { get; set; } = 160f;
        public float DefaultHeight { get; set; } = 100f;
        public float DefaultScale { get; set; } = 1.0f;
        public string[] Aliases { get; set; }
        public string[] ExactIds { get; set; }
        public bool IsSingleton { get; set; } = false;

        public FlightWidgetAttribute(string typeName, params string[] aliases)
        {
            TypeName = typeName;
            Aliases = aliases ?? new string[0];
            ExactIds = new string[0];
        }
    }

    /// <summary>
    /// 动态组件元数据描述符 (Widget Metadata Descriptor)
    /// 用于游戏内组件预设库 (TabLibrary) 动态呈现、分类过滤、搜索与智能装配。
    /// </summary>
    public class WidgetDescriptor
    {
        public Type WidgetType { get; set; }
        public string TypeName { get; set; }
        public WidgetCategory Category { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string DefaultWidgetId { get; set; }
        public float DefaultX { get; set; }
        public float DefaultY { get; set; }
        public float DefaultWidth { get; set; } = 160f;
        public float DefaultHeight { get; set; } = 100f;
        public float DefaultScale { get; set; } = 1.0f;
        public string[] Aliases { get; set; } = new string[0];
        public string[] ExactIds { get; set; } = new string[0];
        public bool IsSingleton { get; set; }

        public WidgetConfig CreateConfig(string idOverride = null)
        {
            string id = !string.IsNullOrEmpty(idOverride)
                ? idOverride
                : (!string.IsNullOrEmpty(DefaultWidgetId) ? DefaultWidgetId : TypeName);

            return new WidgetConfig(id, DisplayName ?? TypeName, DefaultX, DefaultY)
            {
                WidgetType = TypeName,
                Scale = DefaultScale > 0.05f ? DefaultScale : 1.0f,
                IsEnabled = true
            };
        }

        public string GetLocalizedDisplayName()
        {
            if (string.IsNullOrEmpty(DisplayName)) return TypeName;
            return I18n.Tr("LIB_ITEM_" + TypeName.ToUpperInvariant(), DisplayName);
        }

        public string GetLocalizedDescription()
        {
            if (string.IsNullOrEmpty(Description)) return string.Empty;
            return I18n.Tr("LIB_DESC_" + TypeName.ToUpperInvariant(), Description);
        }
    }

    /// <summary>
    /// 全局飞行仪表组件注册表、泛型工厂与自动发现中枢 (Widget Registry & Generic Factory Hub)
    /// 核心职责：
    /// 1. 程序集反射自动扫描 [FlightWidget] 特性，彻底杜绝手工硬编码胶水注册；
    /// 2. 约定优于配置 (Convention over Configuration)：遗漏特性时自动智能推导注册，确保零遗漏；
    /// 3. 为游戏内预设库 (TabLibrary) 与无头测试脚本提供全量动态元数据目录 (AllDescriptors)；
    /// 4. 彻底消除 FlightHUDManager 中重复 SpawnXxx 方法，落实开闭原则 (OCP)。
    /// </summary>
    public static class WidgetRegistry
    {
        private static readonly Dictionary<string, Type> _registry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Type> _exactIdRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WidgetDescriptor> _descriptors = new List<WidgetDescriptor>();
        private static readonly Dictionary<string, WidgetDescriptor> _descriptorMap = new Dictionary<string, WidgetDescriptor>(StringComparer.OrdinalIgnoreCase);
        private static bool _isInitialized = false;

        public static IReadOnlyList<WidgetDescriptor> AllDescriptors
        {
            get
            {
                EnsureInitialized();
                return _descriptors;
            }
        }

        static WidgetRegistry()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            AutoDiscoverAndRegisterWidgets();
        }

        /// <summary>
        /// 全自动扫描程序集中的所有飞行仪表组件并自动装配至注册表
        /// </summary>
        public static void AutoDiscoverAndRegisterWidgets()
        {
            _registry.Clear();
            _exactIdRegistry.Clear();
            _descriptors.Clear();
            _descriptorMap.Clear();

            // 1. 扫描包含 BaseFlightWidget 的主程序集及已加载的扩展程序集
            HashSet<Assembly> scannedAssemblies = new HashSet<Assembly>();
            Assembly mfpAsm = typeof(BaseFlightWidget).Assembly;
            ScanAssembly(mfpAsm);
            scannedAssemblies.Add(mfpAsm);

            try
            {
                Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < loadedAssemblies.Length; i++)
                {
                    Assembly asm = loadedAssemblies[i];
                    if (asm == null || scannedAssemblies.Contains(asm)) continue;

                    string name = asm.GetName().Name;
                    // 仅扫描 MFP 自身或明确引用了 MFP 的外部扩展模组
                    if (name.StartsWith("System.") || name.StartsWith("UnityEngine") || name.StartsWith("mscorlib"))
                    {
                        continue;
                    }

                    ScanAssembly(asm);
                    scannedAssemblies.Add(asm);
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn("WidgetRegistry", $"Error scanning AppDomain assemblies: {ex.Message}");
            }

            // 2. 补全历史兼容确切 ID 映射，确保向后兼容性 100%
            RegisterHistoricalFallbackExactIds();

            MFPLogger.Info("WidgetRegistry", $"Auto-discovery completed: {_descriptors.Count} flight widgets discovered and registered.");
        }

        /// <summary>
        /// 扫描指定程序集中的所有组件
        /// </summary>
        public static void ScanAssembly(Assembly asm)
        {
            if (asm == null) return;

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch (Exception ex)
            {
                MFPLogger.Warn("WidgetRegistry", $"Failed to load types from assembly {asm.FullName}: {ex.Message}");
                return;
            }

            if (types == null) return;

            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || t.IsAbstract || !typeof(BaseFlightWidget).IsAssignableFrom(t)) continue;
                if (t.Name == "StandardFlightWidgetTemplate") continue;

                var attrs = (FlightWidgetAttribute[])t.GetCustomAttributes(typeof(FlightWidgetAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    RegisterDescriptor(t, attrs[0]);
                }
                else
                {
                    RegisterByConvention(t);
                }
            }
        }

        /// <summary>
        /// 基于元数据特性注册组件
        /// </summary>
        public static void RegisterDescriptor(Type widgetType, FlightWidgetAttribute attr)
        {
            if (widgetType == null || attr == null || string.IsNullOrEmpty(attr.TypeName)) return;

            var desc = new WidgetDescriptor
            {
                WidgetType = widgetType,
                TypeName = attr.TypeName,
                Category = attr.Category,
                DisplayName = attr.DisplayName ?? widgetType.Name,
                Description = attr.Description ?? string.Empty,
                DefaultWidgetId = attr.DefaultWidgetId ?? attr.TypeName,
                DefaultX = attr.DefaultX,
                DefaultY = attr.DefaultY,
                DefaultWidth = attr.DefaultWidth > 0f ? attr.DefaultWidth : 160f,
                DefaultHeight = attr.DefaultHeight > 0f ? attr.DefaultHeight : 100f,
                DefaultScale = attr.DefaultScale > 0.05f ? attr.DefaultScale : 1.0f,
                Aliases = attr.Aliases ?? new string[0],
                ExactIds = attr.ExactIds ?? new string[0],
                IsSingleton = attr.IsSingleton
            };

            // 存入目录与快速检索字典
            if (!_descriptorMap.ContainsKey(desc.TypeName))
            {
                _descriptors.Add(desc);
                _descriptorMap[desc.TypeName] = desc;
            }

            // 注册到基础工厂字典
            _registry[attr.TypeName] = widgetType;
            if (attr.Aliases != null)
            {
                for (int i = 0; i < attr.Aliases.Length; i++)
                {
                    if (!string.IsNullOrEmpty(attr.Aliases[i]))
                    {
                        _registry[attr.Aliases[i]] = widgetType;
                        if (!_descriptorMap.ContainsKey(attr.Aliases[i]))
                        {
                            _descriptorMap[attr.Aliases[i]] = desc;
                        }
                    }
                }
            }

            // 注册 ExactIds
            if (attr.ExactIds != null)
            {
                for (int i = 0; i < attr.ExactIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(attr.ExactIds[i]))
                    {
                        _exactIdRegistry[attr.ExactIds[i]] = widgetType;
                        if (!_descriptorMap.ContainsKey(attr.ExactIds[i]))
                        {
                            _descriptorMap[attr.ExactIds[i]] = desc;
                        }
                    }
                }
            }

            // 注册 DefaultWidgetId 为 ExactId
            if (!string.IsNullOrEmpty(desc.DefaultWidgetId))
            {
                _exactIdRegistry[desc.DefaultWidgetId] = widgetType;
                if (!_descriptorMap.ContainsKey(desc.DefaultWidgetId))
                {
                    _descriptorMap[desc.DefaultWidgetId] = desc;
                }
            }
        }

        /// <summary>
        /// 约定优于配置自动兜底注册 (Convention Fallback)
        /// 若开发者创建组件时遗漏了 [FlightWidget] 特性，本机制自动提取类名与命名空间推导元数据，保证绝不遗漏。
        /// </summary>
        public static void RegisterByConvention(Type widgetType)
        {
            if (widgetType == null) return;

            string name = widgetType.Name;
            if (name.EndsWith("Widget", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 6);
            }

            // 类名 CamelCase 转 snake_case
            string typeName = ToSnakeCase(name);

            // 根据命名空间推导大类
            WidgetCategory cat = WidgetCategory.Gauges;
            string ns = widgetType.Namespace ?? string.Empty;
            if (ns.IndexOf("Navigation", StringComparison.OrdinalIgnoreCase) >= 0) cat = WidgetCategory.Navigation;
            else if (ns.IndexOf("SpaceX", StringComparison.OrdinalIgnoreCase) >= 0) cat = WidgetCategory.SpaceX;
            else if (ns.IndexOf("Controls", StringComparison.OrdinalIgnoreCase) >= 0) cat = WidgetCategory.Controls;
            else if (ns.IndexOf("Systems", StringComparison.OrdinalIgnoreCase) >= 0) cat = WidgetCategory.Systems;

            var attr = new FlightWidgetAttribute(typeName)
            {
                Category = cat,
                DisplayName = widgetType.Name,
                Description = $"动态发现组件: {widgetType.FullName}",
                DefaultWidgetId = typeName,
                DefaultX = 0f,
                DefaultY = 0f
            };

            RegisterDescriptor(widgetType, attr);
        }

        private static string ToSnakeCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && !char.IsUpper(str[i - 1]))
                    {
                        sb.Append('_');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 手动注册扩展方法（保留向后兼容）
        /// </summary>
        public static void Register<T>(string typeName, params string[] aliases) where T : BaseFlightWidget
        {
            Register(typeName, typeof(T), aliases);
        }

        public static void Register(string typeName, Type widgetType, params string[] aliases)
        {
            if (string.IsNullOrEmpty(typeName) || widgetType == null) return;
            if (!typeof(BaseFlightWidget).IsAssignableFrom(widgetType))
            {
                throw new ArgumentException($"Type {widgetType.FullName} must inherit from BaseFlightWidget.");
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

        public static void RegisterExactId(string widgetId, Type widgetType)
        {
            if (!string.IsNullOrEmpty(widgetId) && widgetType != null)
            {
                _exactIdRegistry[widgetId] = widgetType;
            }
        }

        /// <summary>
        /// 获取组件元数据描述符
        /// </summary>
        public static WidgetDescriptor GetDescriptor(string typeNameOrId)
        {
            if (string.IsNullOrEmpty(typeNameOrId)) return null;
            EnsureInitialized();
            _descriptorMap.TryGetValue(typeNameOrId, out var desc);
            return desc;
        }

        /// <summary>
        /// 注册历史配置文件遗留的精准 WidgetId 映射，确保向后兼容无缝衔接
        /// </summary>
        private static void RegisterHistoricalFallbackExactIds()
        {
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
            RegisterExactId<ReferenceFrameWidget>("nav.reference_frame");
            RegisterExactId<ReferenceFrameWidget>("nav.ref_frame");
            RegisterExactId<ReferenceFrameWidget>("core.reference_frame");

            RegisterExactId<ArcMeterWidget>("core.throttle");
            RegisterExactId<ArcMeterWidget>("core.vsi");
            RegisterExactId<ArcMeterWidget>("core.propellant");

            RegisterExactId<ArcTapeWidget>("custom.arc_speed_tape");
            RegisterExactId<ArcTapeWidget>("core.arc_speed_tape");
            RegisterExactId<ArcTapeWidget>("custom.arc_altitude_tape");
            RegisterExactId<ArcTapeWidget>("core.arc_altitude_tape");

            RegisterExactId<BottomControlsWidget>("core.bottom_controls");
            RegisterExactId<BottomControlsWidget>("core.ref_rcs_sas");
            RegisterExactId<BottomControlsWidget>("core.rcs_ref_sas");
            RegisterExactId<StageControlWidget>("core.stage_control");
            RegisterExactId<EcamStatusWidget>("core.ecam_status");
            RegisterExactId<EcamAlertLogWidget>("core.ecam_alert_log");
            RegisterExactId<EcamAlertLogWidget>("ecam.alert_log");
            RegisterExactId<EcamAlertLogWidget>("custom.ecam_alert_log");
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
            RegisterExactId<FavoriteToolbarWidget>("core.dock_favorites");
            RegisterExactId<FavoriteToolbarWidget>("toolbar.favorites");
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
            EnsureInitialized();

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
            if (id.StartsWith("arc_tape.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("curved_tape.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("arc_alt.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("arc_speed.", StringComparison.OrdinalIgnoreCase)) return typeof(ArcTapeWidget);
            if (id.StartsWith("tape.", StringComparison.OrdinalIgnoreCase)) return typeof(TapeGaugeWidget);
            if (id.StartsWith("ecam.", StringComparison.OrdinalIgnoreCase)) return typeof(EcamDialGaugeWidget);
            if (id.StartsWith("gauge.", StringComparison.OrdinalIgnoreCase)) return typeof(AvionicsBarGaugeWidget);
            if (id.StartsWith("maneuver.", StringComparison.OrdinalIgnoreCase)) return typeof(ManeuverNodeWidget);
            if (id.StartsWith("toolbar.favorites", StringComparison.OrdinalIgnoreCase) || id.StartsWith("dock_favorites", StringComparison.OrdinalIgnoreCase)) return typeof(FavoriteToolbarWidget);
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
