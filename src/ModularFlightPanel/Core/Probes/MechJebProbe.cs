using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// MechJeb 2 (MJ) 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 VesselState、MechJebCore 以及 MechJebModuleInfoItems 全部公开 API，零遗漏。
    /// </summary>
    public static class MechJebProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("MJ");

        private static Type _mechJebCoreType;
        private static FieldInfo _vesselStateField;
        private static MethodInfo _getComputerModuleMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly mjAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("MechJeb2"))
                    {
                        mjAssembly = asm;
                        break;
                    }
                }

                if (mjAssembly != null)
                {
                    _mechJebCoreType = mjAssembly.GetType("MuMech.MechJebCore");
                    Type vsType = mjAssembly.GetType("MuMech.VesselState");
                    Type infoItemsType = mjAssembly.GetType("MuMech.MechJebModuleInfoItems");

                    if (_mechJebCoreType != null && vsType != null)
                    {
                        _vesselStateField = _mechJebCoreType.GetField("vesselState", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) 
                                           ?? _mechJebCoreType.GetField("VesselState", BindingFlags.Public | BindingFlags.Instance);
                        _getComputerModuleMethod = _mechJebCoreType.GetMethod("GetComputerModule", new[] { typeof(string) });

                        // 1. 彻底遍历 VesselState 中所有 125+ 公开遥测字段与属性
                        Traverser.TraverseInstance(vsType, GetVesselState, "MechJeb 飞行状态机 (VesselState)");

                        // 2. 遍历 MechJebCore 公开实例字段与属性
                        Traverser.TraverseInstance(_mechJebCoreType, GetActiveMechJebCore, "MechJeb 核心组件 (MechJebCore)");

                        // 3. 遍历 MechJebModuleInfoItems 各项综合遥测算法 (TWR、变轨时长、大气压等)
                        if (infoItemsType != null)
                        {
                            Traverser.TraverseInstance(infoItemsType, GetInfoItemsModule, "MechJeb 综合信息项 (InfoItems)");
                        }

                        // 4. 注册标准高频别名，保持 100% 向后兼容
                        Traverser.RegisterAlias("DV", "deltaVStage");
                        Traverser.RegisterAlias("STAGEDV", "deltaVStage");
                        Traverser.RegisterAlias("TOTALDV", "deltaVTotal");
                        Traverser.RegisterAlias("TWR", "twr");
                        Traverser.RegisterAlias("TERMINALVEL", "terminalVelocity");
                        Traverser.RegisterAlias("CURRENTTHRUST", "thrustCurrent");
                        Traverser.RegisterAlias("THRUST", "thrustAvailable");

                        // InfoItems 别名
                        Traverser.RegisterAlias("SURFACETWR", "SurfaceTWR");
                        Traverser.RegisterAlias("LOCALTWR", "LocalTWR");
                        Traverser.RegisterAlias("THROTTLETWR", "ThrottleTWR");
                        Traverser.RegisterAlias("NODEDV", "NextManeuverNodeDeltaV");
                        Traverser.RegisterAlias("TIMETONODE", "TimeToManeuverNode");
                        Traverser.RegisterAlias("NODEBURNTIME", "NextManeuverNodeBurnTime");
                        Traverser.RegisterAlias("COORDINATES", "GetCoordinateString");
                        Traverser.RegisterAlias("ORBITSUMMARY", "CurrentOrbitSummary");
                        Traverser.RegisterAlias("TARGETORBITSUMMARY", "TargetOrbitSummary");

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] MechJebProbe successfully hooked MechJeb 2! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] MechJeb probe initialization warning: {ex.Message}");
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

        private static object GetActiveMechJebCore()
        {
            if (FlightGlobals.ActiveVessel == null || _mechJebCoreType == null) return null;
            try
            {
                var modules = FlightGlobals.ActiveVessel.FindPartModulesImplementing<PartModule>();
                for (int i = 0; i < modules.Count; i++)
                {
                    if (_mechJebCoreType.IsAssignableFrom(modules[i].GetType()))
                    {
                        return modules[i];
                    }
                }
            }
            catch { }
            return null;
        }

        private static object GetVesselState()
        {
            object core = GetActiveMechJebCore();
            if (core == null || _vesselStateField == null) return null;
            try
            {
                return _vesselStateField.GetValue(core);
            }
            catch
            {
                return null;
            }
        }

        private static object GetInfoItemsModule()
        {
            object core = GetActiveMechJebCore();
            if (core == null || _getComputerModuleMethod == null) return null;
            try
            {
                return _getComputerModuleMethod.Invoke(core, new object[] { "MechJebModuleInfoItems" });
            }
            catch
            {
                return null;
            }
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
        public static double StageDeltaV => ResolveNumeric("deltaVStage");
        public static double TotalDeltaV => ResolveNumeric("deltaVTotal");
        public static double TerminalVelocity => ResolveNumeric("terminalVelocity");
        public static double CurrentThrust => ResolveNumeric("thrustCurrent");
        public static double CurrentTWR => ResolveNumeric("twr");

        /// <summary>
        /// 权威提取 MechJeb2 完整分级动力与燃烧统计 (VacStats / AtmoStats)
        /// 提取包括各级 KSPStage、DeltaV、DeltaTime(燃烧时间)、TWR 与比冲，并计算全舰总和。
        /// </summary>
        public static bool TryGetStageStats(out List<StageDeltaVInfo> stageList, out double totalDeltaV, out double totalBurnTime)
        {
            stageList = null;
            totalDeltaV = 0.0;
            totalBurnTime = 0.0;

            if (!_isAvailable) return false;

            try
            {
                object core = GetActiveMechJebCore();
                if (core == null || _getComputerModuleMethod == null) return false;

                object stageStatsMod = _getComputerModuleMethod.Invoke(core, new object[] { "MechJebModuleStageStats" });
                if (stageStatsMod == null) return false;

                Type modType = stageStatsMod.GetType();

                // 触发 RequestUpdate() 唤醒后台燃耗解算
                MethodInfo reqMethod = modType.GetMethod("RequestUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (reqMethod != null)
                {
                    try { reqMethod.Invoke(stageStatsMod, null); } catch { }
                }

                // 优先读取 VacStats (属性或字段)，次选 AtmoStats / stats
                object statsObj = null;
                PropertyInfo pi = modType.GetProperty("VacStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? modType.GetProperty("vacStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? modType.GetProperty("AtmoStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? modType.GetProperty("Stats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null)
                {
                    statsObj = pi.GetValue(stageStatsMod, null);
                }
                else
                {
                    FieldInfo fi = modType.GetField("VacStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? modType.GetField("vacStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? modType.GetField("AtmoStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? modType.GetField("stats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null) statsObj = fi.GetValue(stageStatsMod);
                }

                if (statsObj is System.Collections.IEnumerable enumerable)
                {
                    var list = new List<StageDeltaVInfo>();
                    int curStage = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.currentStage : -1;

                    foreach (object item in enumerable)
                    {
                        if (item == null) continue;
                        Type itemType = item.GetType();

                        int stageNum = ReadInt(item, itemType, "KSPStage", "stage", "stageNumber", "Stage");
                        double dv = ReadDouble(item, itemType, "DeltaV", "deltaV", "dv");
                        double time = ReadDouble(item, itemType, "DeltaTime", "deltaTime", "time", "burnTime", "Time", "BurnTime");
                        double thrust = ReadDouble(item, itemType, "Thrust", "thrust", "thrustAvailable");
                        double startMass = ReadDouble(item, itemType, "StartMass", "startMass");
                        double isp = ReadDouble(item, itemType, "Isp", "isp", "ISP");

                        double twr = 0.0;
                        if (startMass > 0.001 && thrust > 0.001)
                        {
                            twr = thrust / (startMass * 9.80665);
                        }
                        else
                        {
                            twr = ReadDouble(item, itemType, "twr", "TWR", "startTWR", "maxTWR");
                        }

                        if (dv > 0.01 || time > 0.01)
                        {
                            bool isActive = (stageNum == curStage);
                            list.Add(new StageDeltaVInfo(stageNum, dv, time, twr, isp, isActive));
                            totalDeltaV += dv;
                            totalBurnTime += time;
                        }
                    }

                    if (list.Count > 0)
                    {
                        list.Sort((a, b) => b.Stage.CompareTo(a.Stage));
                        stageList = list;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] Error reading MechJeb stage stats: {ex.Message}");
            }

            return false;
        }

        private static int ReadInt(object obj, Type type, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo p = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    object v = p.GetValue(obj, null);
                    if (v != null) return Convert.ToInt32(v);
                }
                FieldInfo f = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object v = f.GetValue(obj);
                    if (v != null) return Convert.ToInt32(v);
                }
            }
            return 0;
        }

        private static double ReadDouble(object obj, Type type, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo p = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    object v = p.GetValue(obj, null);
                    if (v != null) return Convert.ToDouble(v);
                }
                FieldInfo f = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object v = f.GetValue(obj);
                    if (v != null) return Convert.ToDouble(v);
                }
            }
            return 0.0;
        }
    }
}
