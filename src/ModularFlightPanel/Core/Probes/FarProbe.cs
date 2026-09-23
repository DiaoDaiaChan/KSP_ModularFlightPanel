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

                    // 3. 注册自定义便捷计算属性 (L/D 升阻比)
                    Traverser.RegisterCustom("LiftToDragRatio", typeof(double), () =>
                    {
                        double drag = Traverser.ResolveNumeric("ActiveVesselDragCoeff");
                        double lift = Traverser.ResolveNumeric("ActiveVesselLiftCoeff");
                        if (double.IsNaN(drag) || Math.Abs(drag) < 1e-6) return double.NaN;
                        return lift / drag;
                    }, "FAR 气动解算 (VesselFlightInfo)", "即时气动升阻效率比 (L/D)", new[] { "LD", "LIFTTODRAG" });

                    // 4. 注册标准高频别名，保持 100% 向后兼容
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
    }
}
