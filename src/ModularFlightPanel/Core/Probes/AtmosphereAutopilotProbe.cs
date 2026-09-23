using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Atmosphere Autopilot (AA) 软依赖遥测反射探针
    /// 自动反射遍历电传飞控状态、迎角与过载限制器、空气动力学短周期动态模型与巡航控制，零硬编码。
    /// </summary>
    public static class AtmosphereAutopilotProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("AA");

        // 反射缓存
        private static Type _aaAddonType;
        private static PropertyInfo _instanceProp;
        private static MethodInfo _getVesselModulesMethod;
        private static Type _flightModelType;
        private static Type _topManagerType;
        private static Type _pitchControllerType;
        private static Type _cruiseControllerType;
        private static Type _fbwControllerType;

        private const double Rad2Deg = 180.0 / Math.PI;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly aaAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("AtmosphereAutopilot", StringComparison.OrdinalIgnoreCase))
                    {
                        aaAssembly = asm;
                        break;
                    }
                }

                if (aaAssembly != null)
                {
                    _aaAddonType = aaAssembly.GetType("AtmosphereAutopilot.AtmosphereAutopilot");
                    _flightModelType = aaAssembly.GetType("AtmosphereAutopilot.FlightModel");
                    _topManagerType = aaAssembly.GetType("AtmosphereAutopilot.TopModuleManager");
                    _pitchControllerType = aaAssembly.GetType("AtmosphereAutopilot.PitchAngularVelocityController");
                    _cruiseControllerType = aaAssembly.GetType("AtmosphereAutopilot.CruiseController");
                    _fbwControllerType = aaAssembly.GetType("AtmosphereAutopilot.StandardFlyByWire");

                    if (_aaAddonType != null)
                    {
                        _instanceProp = _aaAddonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        _getVesselModulesMethod = _aaAddonType.GetMethod("getVesselModules", BindingFlags.Public | BindingFlags.Instance);

                        // 1. 遍历全局 Addon 单例公共成员
                        Traverser.TraverseInstance(_aaAddonType, GetAAInstance, "AA 全局插件单例 (AtmosphereAutopilot)");

                        // 2. 遍历飞行空气动力学动力模型成员 (FlightModel)
                        if (_flightModelType != null)
                        {
                            Traverser.TraverseInstance(_flightModelType, GetActiveFlightModel, "AA 飞行气动动力学模型 (FlightModel)");
                        }

                        // 3. 遍历顶层飞控管理器 (TopModuleManager)
                        if (_topManagerType != null)
                        {
                            Traverser.TraverseInstance(_topManagerType, GetActiveTopManager, "AA 顶层电传飞控管理器 (TopModuleManager)");
                        }

                        // 4. 遍历俯仰控制与过载限制器 (PitchAngularVelocityController)
                        if (_pitchControllerType != null)
                        {
                            Traverser.TraverseInstance(_pitchControllerType, GetActivePitchController, "AA 迎角与过载保护器 (PitchAngularVelocityController)");
                        }

                        // 5. 遍历巡航控制器 (CruiseController)
                        if (_cruiseControllerType != null)
                        {
                            Traverser.TraverseInstance(_cruiseControllerType, GetActiveCruiseController, "AA 巡航高度与航向保持器 (CruiseController)");
                        }

                        // 6. 注册高频通用计算项与别名
                        RegisterDynamicAAMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] AtmosphereAutopilotProbe successfully hooked AA! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"[ModularFlightPanel] Failed to initialize AtmosphereAutopilotProbe: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static object GetAAInstance()
        {
            if (_instanceProp == null) return null;
            try
            {
                return _instanceProp.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static IDictionary GetActiveVesselModules()
        {
            object instance = GetAAInstance();
            if (instance == null || _getVesselModulesMethod == null) return null;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return null;

            try
            {
                return _getVesselModulesMethod.Invoke(instance, new object[] { v }) as IDictionary;
            }
            catch
            {
                return null;
            }
        }

        private static object GetActiveModule(Type moduleType)
        {
            if (moduleType == null) return null;
            IDictionary dict = GetActiveVesselModules();
            if (dict == null) return null;

            try
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is Type k && k == moduleType)
                    {
                        return entry.Value;
                    }
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private static object GetActiveFlightModel() => GetActiveModule(_flightModelType);
        private static object GetActiveTopManager() => GetActiveModule(_topManagerType);
        private static object GetActivePitchController() => GetActiveModule(_pitchControllerType);
        private static object GetActiveCruiseController() => GetActiveModule(_cruiseControllerType);
        private static object GetActiveStandardFBW() => GetActiveModule(_fbwControllerType);

        private static void RegisterDynamicAAMembers()
        {
            // AA 主开关与状态
            Traverser.RegisterManualMember("MASTER_SWITCH", () =>
            {
                object mgr = GetActiveTopManager();
                if (mgr == null) return 0.0;
                PropertyInfo activeProp = mgr.GetType().GetProperty("Active");
                if (activeProp != null)
                {
                    bool active = (bool)activeProp.GetValue(mgr, null);
                    return active ? 1.0 : 0.0;
                }
                return 0.0;
            }, "AA 电传飞控总开关 (1=开, 0=关)", "AA:MASTER_SWITCH / AA:ACTIVE");

            Traverser.RegisterManualMember("ACTIVE", () =>
            {
                double val;
                return Traverser.TryResolveNumeric("MASTER_SWITCH", out val) ? val : 0.0;
            }, "AA 电传飞控总开关别名", "AA:ACTIVE");

            Traverser.RegisterManualStringMember("STATUS_TEXT", () =>
            {
                object mgr = GetActiveTopManager();
                if (mgr == null) return "N/A";
                PropertyInfo activeProp = mgr.GetType().GetProperty("Active");
                bool active = false;
                if (activeProp != null) active = (bool)activeProp.GetValue(mgr, null);
                if (!active) return "OFF";

                FieldInfo activeCtrlField = mgr.GetType().GetField("active_controller", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (activeCtrlField != null)
                {
                    object ctrl = activeCtrlField.GetValue(mgr);
                    if (ctrl != null)
                    {
                        PropertyInfo nameProp = ctrl.GetType().GetProperty("ModuleName");
                        if (nameProp != null) return nameProp.GetValue(ctrl, null)?.ToString() ?? "ON";
                    }
                }
                return "ON";
            }, "AA 当前飞控工作模式文本", "AA:STATUS_TEXT / AA:MODE");

            Traverser.RegisterManualStringMember("MODE", () =>
            {
                string text;
                return Traverser.TryResolveString("STATUS_TEXT", out text) ? text : "OFF";
            }, "AA 当前飞控工作模式文本别名", "AA:MODE");

            // 气动动力学高频项
            Traverser.RegisterManualMember("LIFT_ACC", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                FieldInfo f = _flightModelType.GetField("lift_acc");
                if (f != null) return Convert.ToDouble(f.GetValue(fm));
                return 0.0;
            }, "AA 升力加速度 (m/s²)", "AA:LIFT_ACC");

            Traverser.RegisterManualMember("SLIDE_ACC", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                FieldInfo f = _flightModelType.GetField("slide_acc");
                if (f != null) return Convert.ToDouble(f.GetValue(fm));
                return 0.0;
            }, "AA 侧滑加速度 (m/s²)", "AA:SLIDE_ACC");

            Traverser.RegisterManualMember("DYN_PRESSURE", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                FieldInfo f = _flightModelType.GetField("dyn_pressure");
                if (f != null) return Convert.ToDouble(f.GetValue(fm));
                return 0.0;
            }, "AA 动压 (Pa)", "AA:DYN_PRESSURE");

            // 迎角与侧滑角 (度)
            Traverser.RegisterManualMember("PITCH_AOA", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                MethodInfo m = _flightModelType.GetMethod("AoA", new[] { typeof(int) });
                if (m != null)
                {
                    float aoaRad = (float)m.Invoke(fm, new object[] { 0 });
                    return (double)(aoaRad * (float)Rad2Deg);
                }
                return 0.0;
            }, "AA 俯仰迎角 (deg)", "AA:PITCH_AOA / AA:AOA");

            Traverser.RegisterManualMember("AOA", () =>
            {
                double val;
                return Traverser.TryResolveNumeric("PITCH_AOA", out val) ? val : 0.0;
            }, "AA 迎角别名 (deg)", "AA:AOA");

            Traverser.RegisterManualMember("SIDESLIP", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                MethodInfo m = _flightModelType.GetMethod("AoA", new[] { typeof(int) });
                if (m != null)
                {
                    float aoaRad = (float)m.Invoke(fm, new object[] { 2 });
                    return (double)(aoaRad * (float)Rad2Deg);
                }
                return 0.0;
            }, "AA 侧滑角 (deg)", "AA:SIDESLIP / AA:YAW_AOA");

            // 角速度 (deg/s)
            Traverser.RegisterManualMember("PITCH_RATE", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                MethodInfo m = _flightModelType.GetMethod("AngularVel", new[] { typeof(int) });
                if (m != null)
                {
                    float velRad = (float)m.Invoke(fm, new object[] { 0 });
                    return (double)(velRad * (float)Rad2Deg);
                }
                return 0.0;
            }, "AA 俯仰角速度 (deg/s)", "AA:PITCH_RATE");

            Traverser.RegisterManualMember("ROLL_RATE", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                MethodInfo m = _flightModelType.GetMethod("AngularVel", new[] { typeof(int) });
                if (m != null)
                {
                    float velRad = (float)m.Invoke(fm, new object[] { 1 });
                    return (double)(velRad * (float)Rad2Deg);
                }
                return 0.0;
            }, "AA 滚转角速度 (deg/s)", "AA:ROLL_RATE");

            Traverser.RegisterManualMember("YAW_RATE", () =>
            {
                object fm = GetActiveFlightModel();
                if (fm == null) return 0.0;
                MethodInfo m = _flightModelType.GetMethod("AngularVel", new[] { typeof(int) });
                if (m != null)
                {
                    float velRad = (float)m.Invoke(fm, new object[] { 2 });
                    return (double)(velRad * (float)Rad2Deg);
                }
                return 0.0;
            }, "AA 偏航角速度 (deg/s)", "AA:YAW_RATE");

            // 控制器保护与限制器参数
            Traverser.RegisterManualMember("MODERATE_AOA", () =>
            {
                object pc = GetActivePitchController();
                if (pc == null) return 0.0;
                FieldInfo f = _pitchControllerType.GetField("moderate_aoa");
                if (f != null) return (bool)f.GetValue(pc) ? 1.0 : 0.0;
                return 0.0;
            }, "AA 迎角保护限制开关 (1=开, 0=关)", "AA:MODERATE_AOA");

            Traverser.RegisterManualMember("MODERATE_G", () =>
            {
                object pc = GetActivePitchController();
                if (pc == null) return 0.0;
                FieldInfo f = _pitchControllerType.GetField("moderate_g");
                if (f != null) return (bool)f.GetValue(pc) ? 1.0 : 0.0;
                return 0.0;
            }, "AA 过载保护限制开关 (1=开, 0=关)", "AA:MODERATE_G");

            Traverser.RegisterManualMember("MAX_AOA", () =>
            {
                object pc = GetActivePitchController();
                if (pc == null) return 0.0;
                FieldInfo f = _pitchControllerType.GetField("max_aoa");
                if (f != null) return Convert.ToDouble(f.GetValue(pc));
                return 0.0;
            }, "AA 最大允许迎角设定值 (deg)", "AA:MAX_AOA");

            Traverser.RegisterManualMember("MAX_G", () =>
            {
                object pc = GetActivePitchController();
                if (pc == null) return 0.0;
                FieldInfo f = _pitchControllerType.GetField("max_g_force");
                if (f != null) return Convert.ToDouble(f.GetValue(pc));
                return 0.0;
            }, "AA 最大允许过载设定值 (g)", "AA:MAX_G");

            // 模式状态
            Traverser.RegisterManualMember("ROCKET_MODE", () =>
            {
                object fbw = GetActiveStandardFBW();
                if (fbw == null) return 0.0;
                PropertyInfo p = _fbwControllerType.GetProperty("RocketMode");
                if (p != null) return (bool)p.GetValue(fbw, null) ? 1.0 : 0.0;
                return 0.0;
            }, "AA 火箭飞控模式 (1=开启, 0=飞机模式)", "AA:ROCKET_MODE");

            Traverser.RegisterManualMember("CRUISE_ACTIVE", () =>
            {
                object cc = GetActiveCruiseController();
                if (cc == null) return 0.0;
                PropertyInfo p = _cruiseControllerType.GetProperty("Active");
                if (p != null) return (bool)p.GetValue(cc, null) ? 1.0 : 0.0;
                return 0.0;
            }, "AA 巡航飞控开启状态 (1=开启, 0=关闭)", "AA:CRUISE_ACTIVE");
        }

        private static void SafeLog(string msg)
        {
            try { Debug.Log(msg); } catch { }
        }
    }
}
