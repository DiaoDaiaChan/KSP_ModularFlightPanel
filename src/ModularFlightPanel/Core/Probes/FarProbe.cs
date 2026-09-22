using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Ferram Aerospace Research (FAR / FARC) 软依赖遥测反射探针
    /// 零硬编码依赖，当玩家安装 FAR 时自动捕获高阶空气动力学遥测指标
    /// </summary>
    public static class FarProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        private static Func<double> _getIAS;
        private static Func<double> _getEAS;
        private static Func<double> _getDynPres;
        private static Func<double> _getAoA;
        private static Func<double> _getSideslip;
        private static Func<double> _getLiftCoeff;
        private static Func<double> _getDragCoeff;
        private static Func<double> _getStallFrac;
        private static Func<double> _getBallisticCoeff;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Type farApiType = Type.GetType("FerramAerospaceResearch.FARAPI, FerramAerospaceResearch");
                if (farApiType == null)
                {
                    // 在已加载的程序集中搜索
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name.StartsWith("FerramAerospaceResearch"))
                        {
                            farApiType = asm.GetType("FerramAerospaceResearch.FARAPI");
                            if (farApiType != null) break;
                        }
                    }
                }

                if (farApiType != null)
                {
                    _getIAS = CreateDoubleDelegate(farApiType, "ActiveVesselIAS");
                    _getEAS = CreateDoubleDelegate(farApiType, "ActiveVesselEAS");
                    _getDynPres = CreateDoubleDelegate(farApiType, "ActiveVesselDynPres");
                    _getAoA = CreateDoubleDelegate(farApiType, "ActiveVesselAoA");
                    _getSideslip = CreateDoubleDelegate(farApiType, "ActiveVesselSideslip");
                    _getLiftCoeff = CreateDoubleDelegate(farApiType, "ActiveVesselLiftCoeff");
                    _getDragCoeff = CreateDoubleDelegate(farApiType, "ActiveVesselDragCoeff");
                    _getStallFrac = CreateDoubleDelegate(farApiType, "ActiveVesselStallFrac");
                    _getBallisticCoeff = CreateDoubleDelegate(farApiType, "ActiveVesselBallisticCoeff");

                    _isAvailable = true;
                    Debug.Log("[ModularFlightPanel] Successfully hooked Ferram Aerospace Research (FAR) API!");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] FAR probe initialization notice: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static Func<double> CreateDoubleDelegate(Type type, string methodName)
        {
            MethodInfo mi = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (mi != null)
            {
                try
                {
                    return (Func<double>)Delegate.CreateDelegate(typeof(Func<double>), mi);
                }
                catch
                {
                    return () => { try { return Convert.ToDouble(mi.Invoke(null, null)); } catch { return 0.0; } };
                }
            }
            return null;
        }

        public static double IAS => _isAvailable && _getIAS != null ? _getIAS() : double.NaN;
        public static double EAS => _isAvailable && _getEAS != null ? _getEAS() : double.NaN;
        public static double DynamicPressure => _isAvailable && _getDynPres != null ? _getDynPres() : double.NaN;
        public static double AngleOfAttack => _isAvailable && _getAoA != null ? _getAoA() : double.NaN;
        public static double Sideslip => _isAvailable && _getSideslip != null ? _getSideslip() : double.NaN;
        public static double LiftCoeff => _isAvailable && _getLiftCoeff != null ? _getLiftCoeff() : double.NaN;
        public static double DragCoeff => _isAvailable && _getDragCoeff != null ? _getDragCoeff() : double.NaN;
        public static double StallFraction => _isAvailable && _getStallFrac != null ? _getStallFrac() : double.NaN;
        public static double BallisticCoeff => _isAvailable && _getBallisticCoeff != null ? _getBallisticCoeff() : double.NaN;
        public static double LiftToDragRatio
        {
            get
            {
                double d = DragCoeff;
                return (Math.Abs(d) > 0.0001) ? LiftCoeff / d : 0.0;
            }
        }
    }
}
