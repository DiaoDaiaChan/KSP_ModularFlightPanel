using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Kerbalism 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 VesselData (全量环境与生命维持)、
    /// ConnectionInfo (通信链路)、Features (特性开关) 以及 API/DB 的全部公开成员，零遗漏。
    /// </summary>
    public static class KerbalismProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("KERBALISM");

        private static Type _vesselDataType;
        private static Type _connectionInfoType;
        private static Type _featuresType;
        private static Type _apiType;
        private static Type _dbType;

        private static MethodInfo _kerbalismDataMethod;
        private static PropertyInfo _connectionProp;
        private static PropertyInfo _comfortsProp;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly kerbalismAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("Kerbalism", StringComparison.OrdinalIgnoreCase) ||
                        asmName.StartsWith("Kerbalism1", StringComparison.OrdinalIgnoreCase))
                    {
                        kerbalismAssembly = asm;
                        break;
                    }
                }

                if (kerbalismAssembly != null)
                {
                    _vesselDataType = kerbalismAssembly.GetType("KERBALISM.VesselData");
                    _connectionInfoType = kerbalismAssembly.GetType("KERBALISM.ConnectionInfo") ?? kerbalismAssembly.GetType("KERBALISM.IConnectionInfo");
                    _featuresType = kerbalismAssembly.GetType("KERBALISM.Features");
                    _apiType = kerbalismAssembly.GetType("KERBALISM.API");
                    _dbType = kerbalismAssembly.GetType("KERBALISM.DB");

                    if (_dbType != null)
                    {
                        _kerbalismDataMethod = _dbType.GetMethod("KerbalismData", new[] { typeof(Vessel) });
                    }

                    if (_vesselDataType != null)
                    {
                        _connectionProp = _vesselDataType.GetProperty("Connection", BindingFlags.Public | BindingFlags.Instance);
                        _comfortsProp = _vesselDataType.GetProperty("Comforts", BindingFlags.Public | BindingFlags.Instance);
                    }

                    // 1. 遍历 Features 静态功能开关
                    if (_featuresType != null)
                    {
                        Traverser.TraverseStatic(_featuresType, "Kerbalism 运行特性 (Features)");
                    }

                    // 2. 遍历 VesselData 全量属性与字段
                    if (_vesselDataType != null)
                    {
                        Traverser.TraverseInstance(_vesselDataType, GetActiveVesselData, "Kerbalism 载具状态与生命维持 (VesselData)");
                    }

                    // 3. 遍历 ConnectionInfo
                    if (_connectionInfoType != null)
                    {
                        Traverser.TraverseInstance(_connectionInfoType, GetActiveConnectionInfo, "Kerbalism 通信链路 (ConnectionInfo)");
                    }

                    // 4. 遍历 API 静态方法
                    if (_apiType != null)
                    {
                        Traverser.TraverseStatic(_apiType, "Kerbalism 公开接口 (API)");
                    }

                    // 5. 注册高阶通用别名与动态计算
                    RegisterDynamicKerbalismMembers();

                    _isAvailable = true;
                    SafeLog($"[ModularFlightPanel] KerbalismProbe successfully hooked Kerbalism! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] Kerbalism probe initialization warning: {ex.Message}");
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

        public static object GetActiveVesselData()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _kerbalismDataMethod == null) return null;

            try
            {
                return _kerbalismDataMethod.Invoke(null, new object[] { v });
            }
            catch
            {
                return null;
            }
        }

        public static object GetActiveConnectionInfo()
        {
            object vd = GetActiveVesselData();
            if (vd == null || _connectionProp == null) return null;

            try
            {
                return _connectionProp.GetValue(vd, null);
            }
            catch
            {
                return null;
            }
        }

        private static void RegisterDynamicKerbalismMembers()
        {
            // 环境辐射 (Radiation)
            Traverser.RegisterCustom("Radiation", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvRadiation");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 空间辐射", "当前位置环境辐射剂量率 (rad/h)", new[] { "RADIATION", "ENVRADIATION" });

            // 乘员舱屏蔽后辐射 (HabitatRadiation)
            Traverser.RegisterCustom("HabitatRadiation", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvHabitatRadiation");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 空间辐射", "乘员舱内经过防护屏蔽后的有效辐射剂量率 (rad/h)", new[] { "HABITATRADIATION", "HABRADIATION", "SHIELDEDRADIATION" });

            // 舱内气压 (Pressure)
            Traverser.RegisterCustom("Pressure", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("Pressure");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 舱内环境", "乘员舱归一化大气压力 (0..1 或 atm)", new[] { "PRESSURE", "HABPRESSURE" });

            // 二氧化碳中毒度 (Poisoning)
            Traverser.RegisterCustom("Poisoning", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("Poisoning");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 舱内环境", "乘员舱二氧化碳废气滞留比例 (0..1)", new[] { "POISONING", "CO2", "CO2POISONING" });

            // 乘员舱辐射屏蔽率 (Shielding)
            Traverser.RegisterCustom("Shielding", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("Shielding");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 舱内环境", "乘员舱防辐射屏蔽吸收比例 (0..1)", new[] { "SHIELDING", "HABSHIELDING" });

            // 生活空间系数 (LivingSpace)
            Traverser.RegisterCustom("LivingSpace", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("LivingSpace");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 舱内环境", "乘员活动生活空间宽裕系数", new[] { "LIVINGSPACE" });

            // 舒适度系数 (Comfort)
            Traverser.RegisterCustom("Comfort", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null && _comfortsProp != null)
                {
                    try
                    {
                        object comforts = _comfortsProp.GetValue(vd, null);
                        if (comforts != null)
                        {
                            var factorField = comforts.GetType().GetField("factor") ?? (MemberInfo)comforts.GetType().GetProperty("factor");
                            if (factorField is FieldInfo fi) return Convert.ToDouble(fi.GetValue(comforts));
                            if (factorField is PropertyInfo pi) return Convert.ToDouble(pi.GetValue(comforts, null));
                        }
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 舱内环境", "综合乘员心理舒适度系数", new[] { "COMFORT", "COMFORTFACTOR" });

            // 环境温度 (Temperature)
            Traverser.RegisterCustom("Temperature", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvTemperature");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 外部空间环境", "载具当前位置空间环境有效黑体辐射/热辐射平衡温度 (K)", new[] { "TEMPERATURE", "ENVTEMP", "TEMPK" });

            // 适宜温差 (TempDiff)
            Traverser.RegisterCustom("TempDiff", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvTempDiff");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 外部空间环境", "外部温度与乘员舱生存温度的差值 (K)", new[] { "TEMPDIFF", "SURVIVALTEMPDIFF" });

            // 总太阳光照通量 (TotalFlux)
            Traverser.RegisterCustom("TotalFlux", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvSolarFluxTotal");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 外部空间环境", "载具位置接收到的恒星总辐射通量 (W/m²)", new[] { "TOTALFLUX", "SOLARFLUX", "ENVFLUX" });

            // 是否处于光照中 (InSunlight)
            Traverser.RegisterCustom("InSunlight", typeof(bool), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvInSunlight");
                        if (prop != null) return (bool)prop.GetValue(vd, null);
                    }
                    catch { }
                }
                return true;
            }, "Kerbalism 外部空间环境", "载具当前是否处于直射阳光照射下", new[] { "INSUNLIGHT", "SUNLIGHT" });

            // 是否处于全本影中 (InShadow)
            Traverser.RegisterCustom("InShadow", typeof(bool), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvInFullShadow");
                        if (prop != null) return (bool)prop.GetValue(vd, null);
                    }
                    catch { }
                }
                return false;
            }, "Kerbalism 外部空间环境", "载具当前是否处于天体本影背光区", new[] { "INSHADOW", "SHADOW" });

            // 太阳风暴爆发状态 (InStorm)
            Traverser.RegisterCustom("InStorm", typeof(bool), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvStorm");
                        if (prop != null) return (bool)prop.GetValue(vd, null);
                    }
                    catch { }
                }
                return false;
            }, "Kerbalism 空间天气", "载具当前是否正遭受恒星日冕物质抛射(CME)太阳风暴袭击", new[] { "STORM", "INSTORM", "CME" });

            // 太阳风暴辐射强度 (StormRadiation)
            Traverser.RegisterCustom("StormRadiation", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("EnvStormRadiation");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 空间天气", "太阳风暴引起的额外高能粒子辐射剂量率 (rad/h)", new[] { "STORMRADIATION" });

            // 部件故障状态 (Malfunction)
            Traverser.RegisterCustom("Malfunction", typeof(bool), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("Malfunction");
                        if (prop != null) return (bool)prop.GetValue(vd, null);
                    }
                    catch { }
                }
                return false;
            }, "Kerbalism 部件可靠性", "载具上是否存在任何部件故障或损坏", new[] { "MALFUNCTION", "FAILURES" });

            // 致命/严重故障状态 (Critical)
            Traverser.RegisterCustom("Critical", typeof(bool), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("Critical");
                        if (prop != null) return (bool)prop.GetValue(vd, null);
                    }
                    catch { }
                }
                return false;
            }, "Kerbalism 部件可靠性", "载具上是否存在无法修复的严重/灾难性失效", new[] { "CRITICAL", "CRITICALFAILURE" });

            // 科学数据存储盘剩余空间 (DrivesFreeSpace)
            Traverser.RegisterCustom("DrivesFreeSpace", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("DrivesFreeSpace");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 科学系统", "公共数据硬盘可用剩余存储容量 (MB)", new[] { "DRIVESFREE", "DISKFREE", "DATAFREE" });

            // 科学数据存储盘总容量 (DrivesCapacity)
            Traverser.RegisterCustom("DrivesCapacity", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("DrivesCapacity");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 科学系统", "公共数据硬盘总存储容量 (MB)", new[] { "DRIVESCAPACITY", "DISKCAP", "DATACAP" });

            // 太阳能电池板平均受光效率 (SolarPanelsAverageExposure)
            Traverser.RegisterCustom("SolarPanelsExposure", typeof(double), () =>
            {
                object vd = GetActiveVesselData();
                if (vd != null)
                {
                    try
                    {
                        var prop = vd.GetType().GetProperty("SolarPanelsAverageExposure");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(vd, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 能源系统", "全舰太阳能帆板综合有效受光与光照利用率 (0..1)", new[] { "SOLAREXPOSURE", "PANELSEXPOSURE" });

            // 通信速率 (DataRate)
            Traverser.RegisterCustom("DataRate", typeof(double), () =>
            {
                object conn = GetActiveConnectionInfo();
                if (conn != null)
                {
                    try
                    {
                        var prop = conn.GetType().GetProperty("DataRate") ?? (MemberInfo)conn.GetType().GetField("rate");
                        if (prop is PropertyInfo pi) return Convert.ToDouble(pi.GetValue(conn, null));
                        if (prop is FieldInfo fi) return Convert.ToDouble(fi.GetValue(conn));
                    }
                    catch { }
                }
                return 0.0;
            }, "Kerbalism 通信系统", "当前科学与遥测下行速率 (MB/s)", new[] { "DATARATE", "COMMRATE" });

            // 通信连通状态 (Linked)
            Traverser.RegisterCustom("Linked", typeof(bool), () =>
            {
                object conn = GetActiveConnectionInfo();
                if (conn != null)
                {
                    try
                    {
                        var prop = conn.GetType().GetProperty("Linked") ?? (MemberInfo)conn.GetType().GetField("linked");
                        if (prop is PropertyInfo pi) return (bool)pi.GetValue(conn, null);
                        if (prop is FieldInfo fi) return (bool)fi.GetValue(conn);
                    }
                    catch { }
                }
                return false;
            }, "Kerbalism 通信系统", "是否成功建立至指挥控制中心或中继站的有效通信", new[] { "LINKED", "CONNECTED" });

            // 目标中继/地面站名称 (TargetName)
            Traverser.RegisterCustom("TargetName", typeof(string), () =>
            {
                object conn = GetActiveConnectionInfo();
                if (conn != null)
                {
                    try
                    {
                        var prop = conn.GetType().GetProperty("target_name") ?? (MemberInfo)conn.GetType().GetField("target_name");
                        if (prop is PropertyInfo pi) return pi.GetValue(conn, null) as string;
                        if (prop is FieldInfo fi) return fi.GetValue(conn) as string;
                    }
                    catch { }
                }
                return "NONE";
            }, "Kerbalism 通信系统", "通信束当前直连的目标地面站或中继星名称", new[] { "TARGETNAME", "COMMTARGET" });
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
