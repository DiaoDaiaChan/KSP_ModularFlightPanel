using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Dynamic Battery Storage (DBS) 软依赖遥测反射探针
    /// 遍历载具总发电量、总耗电量、净充放电差额与电池电量耗尽倒计时，零硬编码。
    /// </summary>
    public static class DynamicBatteryStorageProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("DBS");

        private static Type _vdmType;
        private static Type _elecDataType;
        private static PropertyInfo _electricalDataProp;
        private static PropertyInfo _currentProdProp;
        private static PropertyInfo _currentConsProp;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly dbsAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("DynamicBatteryStorage", StringComparison.OrdinalIgnoreCase))
                    {
                        dbsAssembly = asm;
                        break;
                    }
                }

                if (dbsAssembly != null)
                {
                    _vdmType = dbsAssembly.GetType("DynamicBatteryStorage.VesselDataManager");
                    _elecDataType = dbsAssembly.GetType("DynamicBatteryStorage.VesselElectricalData");

                    if (_vdmType != null && _elecDataType != null)
                    {
                        _electricalDataProp = _vdmType.GetProperty("ElectricalData", BindingFlags.Public | BindingFlags.Instance);
                        _currentProdProp = _elecDataType.GetProperty("CurrentProduction", BindingFlags.Public | BindingFlags.Instance);
                        _currentConsProp = _elecDataType.GetProperty("CurrentConsumption", BindingFlags.Public | BindingFlags.Instance);

                        // 1. 实例反射遍历电气数据类型
                        Traverser.TraverseInstance(_elecDataType, GetActiveElectricalData, "DBS 电气能量数据 (VesselElectricalData)");

                        // 2. 注册高频通用计算项与别名
                        RegisterDynamicDBSMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] DynamicBatteryStorageProbe successfully hooked DBS! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] DBS probe initialization warning: {ex.Message}");
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

        public static object GetActiveElectricalData()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _vdmType == null || _electricalDataProp == null) return null;

            try
            {
                // 优先从 VesselModules 获取
                for (int i = 0; i < v.vesselModules.Count; i++)
                {
                    VesselModule vm = v.vesselModules[i];
                    if (vm != null && _vdmType.IsInstanceOfType(vm))
                    {
                        return _electricalDataProp.GetValue(vm, null);
                    }
                }

                // 次选从根物体查找
                var comp = v.GetComponent(_vdmType);
                if (comp != null)
                {
                    return _electricalDataProp.GetValue(comp, null);
                }
            }
            catch { }

            return null;
        }

        private static void RegisterDynamicDBSMembers()
        {
            // 即时总发电量 (PowerGeneration)
            Traverser.RegisterCustom("PowerGeneration", typeof(double), () =>
            {
                object ed = GetActiveElectricalData();
                if (ed != null && _currentProdProp != null)
                {
                    try { return (double)_currentProdProp.GetValue(ed, null); } catch { }
                }
                return 0.0;
            }, "DBS 能源系统", "全舰当前即时总电力生产率 (EC/s 或 kW)", new[] { "POWERGENERATION", "PROD", "GEN", "IN" });

            // 即时总耗电量 (PowerConsumption)
            Traverser.RegisterCustom("PowerConsumption", typeof(double), () =>
            {
                object ed = GetActiveElectricalData();
                if (ed != null && _currentConsProp != null)
                {
                    try { return (double)_currentConsProp.GetValue(ed, null); } catch { }
                }
                return 0.0;
            }, "DBS 能源系统", "全舰当前即时总电力负荷消耗率 (EC/s 或 kW)", new[] { "POWERCONSUMPTION", "CONS", "DRAIN", "OUT" });

            // 净充放电差额速率 (NetRate)
            Traverser.RegisterCustom("NetRate", typeof(double), () =>
            {
                object ed = GetActiveElectricalData();
                if (ed != null && _currentProdProp != null && _currentConsProp != null)
                {
                    try
                    {
                        double p = (double)_currentProdProp.GetValue(ed, null);
                        double c = (double)_currentConsProp.GetValue(ed, null);
                        return p - c;
                    }
                    catch { }
                }
                return 0.0;
            }, "DBS 能源系统", "全舰电网净充放电差额速率 (正为蓄电，负为亏电)", new[] { "NETRATE", "NET", "FLOW" });

            // 电池完全耗尽倒计时秒数 (TimeToDepletionSeconds)
            Traverser.RegisterCustom("TimeToDepletionSeconds", typeof(double), () =>
            {
                object ed = GetActiveElectricalData();
                Vessel v = FlightGlobals.ActiveVessel;
                if (ed != null && v != null && _currentProdProp != null && _currentConsProp != null)
                {
                    try
                    {
                        double p = (double)_currentProdProp.GetValue(ed, null);
                        double c = (double)_currentConsProp.GetValue(ed, null);
                        double net = p - c;
                        if (net < -0.001)
                        {
                            v.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double currentEC, out double maxEC);
                            return Math.Max(0.0, currentEC / (-net));
                        }
                    }
                    catch { }
                }
                return double.NaN;
            }, "DBS 能源系统", "当前亏电速度下剩余电池存量彻底耗尽预计所需秒数", new[] { "DEPLETIONSECONDS", "TIMELEFTSEC" });

            // 电池耗尽倒计时格式化文本 (TimeToDepletion)
            Traverser.RegisterCustom("TimeToDepletion", typeof(string), () =>
            {
                object ed = GetActiveElectricalData();
                Vessel v = FlightGlobals.ActiveVessel;
                if (ed != null && v != null && _currentProdProp != null && _currentConsProp != null)
                {
                    try
                    {
                        double p = (double)_currentProdProp.GetValue(ed, null);
                        double c = (double)_currentConsProp.GetValue(ed, null);
                        double net = p - c;
                        if (net >= -0.001)
                        {
                            return "INFINITE";
                        }
                        v.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double currentEC, out double maxEC);
                        double sec = Math.Max(0.0, currentEC / (-net));
                        TimeSpan ts = TimeSpan.FromSeconds(sec);
                        if (ts.TotalHours >= 24)
                            return $"{ts.Days}d {ts.Hours}h";
                        return ts.Hours > 0 ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                    }
                    catch { }
                }
                return "---";
            }, "DBS 能源系统", "电池存量耗尽倒计时格式化文本 (HH:MM:SS)", new[] { "TIMETODEPLETION", "DEPLETION", "DRAINTIME" });

            // 是否处于亏电放电状态 (IsDepleting)
            Traverser.RegisterCustom("IsDepleting", typeof(bool), () =>
            {
                object ed = GetActiveElectricalData();
                if (ed != null && _currentProdProp != null && _currentConsProp != null)
                {
                    try
                    {
                        double p = (double)_currentProdProp.GetValue(ed, null);
                        double c = (double)_currentConsProp.GetValue(ed, null);
                        return c > p + 0.001;
                    }
                    catch { }
                }
                return false;
            }, "DBS 能源系统", "当前全舰总耗电是否大于发电（处于亏电净放电状态）", new[] { "ISDEPLETING", "DEPLETING" });
        }

        public static double ResolveNumeric(string memberName, string componentModifier = null)
        {
            if (!_isAvailable) return double.NaN;
            return Traverser.ResolveNumeric(memberName, componentModifier);
        }

        public static string ResolveString(string memberName, string format = null, string componentModifier = null)
        {
            if (!_isAvailable) return "---";
            return Traverser.ResolveString(memberName, format, componentModifier);
        }
    }
}
