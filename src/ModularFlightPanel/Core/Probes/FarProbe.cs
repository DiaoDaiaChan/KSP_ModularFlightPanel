using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Ferram Aerospace Research (FAR / FARC) 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 FARAPI 及 VesselFlightInfo 全部公开 API 内容，零遗漏。
    /// </summary>
    public static class FarProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("FAR");

        private static MethodInfo _vesselFlightInfoMethod;
        private static PropertyInfo _infoParametersProp;
        private static MethodInfo _farAtmosphereGetPressureMethod;
        private static MethodInfo _farAtmosphereGetTemperatureMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Type farApiType = Type.GetType("FerramAerospaceResearch.FARAPI, FerramAerospaceResearch");
                Assembly farAssembly = null;

                if (farApiType == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name.StartsWith("FerramAerospaceResearch"))
                        {
                            farAssembly = asm;
                            farApiType = asm.GetType("FerramAerospaceResearch.FARAPI");
                            if (farApiType != null) break;
                        }
                    }
                }
                else
                {
                    farAssembly = farApiType.Assembly;
                }

                if (farApiType != null)
                {
                    // 1. 完全遍历 FARAPI 所有公开静态方法、属性与字段
                    int apiCount = Traverser.TraverseStatic(farApiType, "FAR 官方接口 (FARAPI)");

                    // 2. 遍历 VesselFlightInfo 结构体中的所有公开遥测数据字段
                    _vesselFlightInfoMethod = farApiType.GetMethod("VesselFlightInfo", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vessel) }, null);
                    
                    Type flightGuiType = farAssembly?.GetType("FerramAerospaceResearch.FARGUI.FARFlightGUI.FlightGUI");
                    Type vesselFlightInfoType = farAssembly?.GetType("FerramAerospaceResearch.FARGUI.FARFlightGUI.VesselFlightInfo");

                    if (flightGuiType != null && vesselFlightInfoType != null)
                    {
                        _infoParametersProp = flightGuiType.GetProperty("InfoParameters", BindingFlags.Public | BindingFlags.Instance);
                        if (_infoParametersProp != null)
                        {
                            Traverser.TraverseInstance(vesselFlightInfoType, GetActiveVesselFlightInfo, "FAR 气动解算 (VesselFlightInfo)");
                        }
                    }

                    // 3. 挂接 FAR 大气模型类 (FARAtmosphere)，直读环境气压与温度
                    Type farAtmosphereType = farAssembly?.GetType("FerramAerospaceResearch.FARAtmosphere");
                    if (farAtmosphereType == null)
                    {
                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            if (asm.GetName().Name.StartsWith("FerramAerospaceResearch"))
                            {
                                farAtmosphereType = asm.GetType("FerramAerospaceResearch.FARAtmosphere");
                                if (farAtmosphereType != null) break;
                            }
                        }
                    }

                    if (farAtmosphereType != null)
                    {
                        _farAtmosphereGetPressureMethod = farAtmosphereType.GetMethod("GetPressure", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vessel) }, null);
                        _farAtmosphereGetTemperatureMethod = farAtmosphereType.GetMethod("GetTemperature", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vessel) }, null);
                    }

                    // 4. 注册自定义便捷计算属性 (L/D 升阻比、大气气压 atm/kPa/Pa、环境温度)
                    Traverser.RegisterCustom("LiftToDragRatio", typeof(double), () =>
                    {
                        double drag = Traverser.ResolveNumeric("ActiveVesselDragCoeff");
                        double lift = Traverser.ResolveNumeric("ActiveVesselLiftCoeff");
                        if (double.IsNaN(drag) || Math.Abs(drag) < 1e-6) return double.NaN;
                        return lift / drag;
                    }, "FAR 气动解算 (VesselFlightInfo)", "即时气动升阻效率比 (L/D)", new[] { "LD", "LIFTTODRAG" });

                    Traverser.RegisterCustom("AtmosphericPressureAtm", typeof(double), () =>
                    {
                        Vessel v = FlightGlobals.ActiveVessel;
                        return v != null ? GetAtmosphericPressureAtm(v) : double.NaN;
                    }, "FAR 大气物理 (FARAtmosphere)", "当前环境大气压强 (atm)", new[] { "ATM", "PRESSURE", "BARO", "ATMPRESSURE" });

                    Traverser.RegisterCustom("AtmosphericPressurekPa", typeof(double), () =>
                    {
                        Vessel v = FlightGlobals.ActiveVessel;
                        if (v == null) return double.NaN;
                        double pa = GetAtmosphericPressurePa(v);
                        return double.IsNaN(pa) ? double.NaN : (pa / 1000.0);
                    }, "FAR 大气物理 (FARAtmosphere)", "当前环境静压 (kPa)", new[] { "STATICPRESSURE", "KPA", "STATPRES" });

                    Traverser.RegisterCustom("AtmosphericPressurePa", typeof(double), () =>
                    {
                        Vessel v = FlightGlobals.ActiveVessel;
                        return v != null ? GetAtmosphericPressurePa(v) : double.NaN;
                    }, "FAR 大气物理 (FARAtmosphere)", "当前环境静压 (Pa)", new[] { "PA", "PRES_PA" });

                    Traverser.RegisterCustom("AtmosphericTemperature", typeof(double), () =>
                    {
                        Vessel v = FlightGlobals.ActiveVessel;
                        return v != null ? GetAtmosphericTemperature(v) : double.NaN;
                    }, "FAR 大气物理 (FARAtmosphere)", "当前环境外部大气温度 (K)", new[] { "ATMTEMP", "AMB_TEMP" });

                    // 5. 注册标准高频别名，保持 100% 向后兼容
                    Traverser.RegisterAlias("IAS", "ActiveVesselIAS");
                    Traverser.RegisterAlias("EAS", "ActiveVesselEAS");
                    Traverser.RegisterAlias("Q", "ActiveVesselDynPres");
                    Traverser.RegisterAlias("DYNAERO", "ActiveVesselDynPres");
                    Traverser.RegisterAlias("AOA", "ActiveVesselAoA");
                    Traverser.RegisterAlias("SIDESLIP", "ActiveVesselSideslip");
                    Traverser.RegisterAlias("LIFT", "ActiveVesselLiftCoeff");
                    Traverser.RegisterAlias("LIFTCOEFF", "ActiveVesselLiftCoeff");
                    Traverser.RegisterAlias("DRAG", "ActiveVesselDragCoeff");
                    Traverser.RegisterAlias("DRAGCOEFF", "ActiveVesselDragCoeff");
                    Traverser.RegisterAlias("STALL", "ActiveVesselStallFrac");
                    Traverser.RegisterAlias("BALLISTIC", "ActiveVesselBallisticCoeff");
                    Traverser.RegisterAlias("TSFC", "ActiveVesselTSFC");
                    Traverser.RegisterAlias("REFAREA", "ActiveVesselRefArea");
                    Traverser.RegisterAlias("TERMVEL", "ActiveVesselTermVelEst");
                    Traverser.RegisterAlias("AEROFORCE", "ActiveVesselAerodynamicForce");
                    Traverser.RegisterAlias("AEROTORQUE", "ActiveVesselAerodynamicTorque");

                    _isAvailable = true;
                    SafeLog($"[ModularFlightPanel] FarProbe successfully hooked FAR! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] FarProbe initialization warning: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static void SafeLog(string msg)
        {
            try { Debug.Log(msg); }
            catch { Console.WriteLine(msg); }
        }

        private static void SafeLogWarning(string msg)
        {
            try { Debug.LogWarning(msg); }
            catch { Console.WriteLine("[WARN] " + msg); }
        }

        private static object GetActiveVesselFlightInfo()
        {
            if (FlightGlobals.ActiveVessel == null || _vesselFlightInfoMethod == null || _infoParametersProp == null) return null;
            try
            {
                object gui = _vesselFlightInfoMethod.Invoke(null, new object[] { FlightGlobals.ActiveVessel });
                if (gui != null)
                {
                    return _infoParametersProp.GetValue(gui, null);
                }
            }
            catch { }
            return null;
        }

        public static double ResolveNumeric(string subTag, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveNumeric(subTag, modifier) : double.NaN;
        }

        public static string ResolveString(string subTag, string format = null, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveString(subTag, format, modifier) : "---";
        }

        /// <summary>
        /// 获取由 FAR 大气模型解算的当前载具环境静压 (单位: Pa)
        /// </summary>
        public static double GetAtmosphericPressurePa(Vessel vessel)
        {
            if (vessel == null || _farAtmosphereGetPressureMethod == null) return double.NaN;
            try
            {
                object res = _farAtmosphereGetPressureMethod.Invoke(null, new object[] { vessel });
                if (res is double d) return d;
            }
            catch { }
            return double.NaN;
        }

        /// <summary>
        /// 获取由 FAR 大气模型解算的当前载具环境气压 (单位: atm，标准大气压 1 atm = 101325 Pa)
        /// </summary>
        public static double GetAtmosphericPressureAtm(Vessel vessel)
        {
            double pa = GetAtmosphericPressurePa(vessel);
            if (double.IsNaN(pa) || pa < 0.0) return double.NaN;
            return pa / 101325.0;
        }

        /// <summary>
        /// 获取由 FAR 大气模型解算的当前载具环境外部大气温度 (单位: K)
        /// </summary>
        public static double GetAtmosphericTemperature(Vessel vessel)
        {
            if (vessel == null || _farAtmosphereGetTemperatureMethod == null) return double.NaN;
            try
            {
                object res = _farAtmosphereGetTemperatureMethod.Invoke(null, new object[] { vessel });
                if (res is double d) return d;
            }
            catch { }
            return double.NaN;
        }

        // 向后兼容强类型静态属性
        public static double IAS => ResolveNumeric("IAS");
        public static double EAS => ResolveNumeric("EAS");
        public static double DynamicPressure => ResolveNumeric("Q");
        public static double AngleOfAttack => ResolveNumeric("AOA");
        public static double Sideslip => ResolveNumeric("SIDESLIP");
        public static double LiftCoeff => ResolveNumeric("LIFT");
        public static double DragCoeff => ResolveNumeric("DRAG");
        public static double StallFraction => ResolveNumeric("STALL");
        public static double BallisticCoeff => ResolveNumeric("BALLISTIC");
        public static double LiftToDragRatio => ResolveNumeric("LD");
        public static double AtmosphericPressureAtm => FlightGlobals.ActiveVessel != null ? GetAtmosphericPressureAtm(FlightGlobals.ActiveVessel) : double.NaN;
        public static double StaticPressurekPa => FlightGlobals.ActiveVessel != null ? (GetAtmosphericPressurePa(FlightGlobals.ActiveVessel) / 1000.0) : double.NaN;
        public static double AtmosphericTemperature => FlightGlobals.ActiveVessel != null ? GetAtmosphericTemperature(FlightGlobals.ActiveVessel) : double.NaN;
    }
}
