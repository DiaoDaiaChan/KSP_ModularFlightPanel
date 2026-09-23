using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Kerbal Engineer Redux (KER) 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 SimManager、Stage 以及全部 8 大 Readout Processors 公开 API，零遗漏。
    /// </summary>
    public static class KerbalEngineerProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("KER");

        private static PropertyInfo _lastStageProp;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly kerAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.Equals("KerbalEngineer", StringComparison.OrdinalIgnoreCase))
                    {
                        kerAssembly = asm;
                        break;
                    }
                }

                if (kerAssembly != null)
                {
                    // 1. 遍历 SimManager (仿真管理器公开静态成员)
                    Type simManagerType = kerAssembly.GetType("KerbalEngineer.VesselSimulator.SimManager");
                    if (simManagerType != null)
                    {
                        Traverser.TraverseStatic(simManagerType, "KER 仿真机 (SimManager)");
                        _lastStageProp = simManagerType.GetProperty("LastStage", BindingFlags.Public | BindingFlags.Static);
                    }

                    // 2. 遍历 Stage (分级解算详细指标，全部分级推力/dV/燃时/RCS等)
                    Type stageType = kerAssembly.GetType("KerbalEngineer.VesselSimulator.Stage");
                    if (stageType != null && _lastStageProp != null)
                    {
                        Traverser.TraverseInstance(stageType, GetLastStage, "KER 分级遥测 (Stage)");
                    }

                    // 3. 遍历全部 8 个 Readout Processors 公开遥测解算器
                    string[] processorNames = new string[]
                    {
                        "KerbalEngineer.Flight.Readouts.Vessel.SimulationProcessor",
                        "KerbalEngineer.Flight.Readouts.Vessel.AttitudeProcessor",
                        "KerbalEngineer.Flight.Readouts.Surface.AtmosphericProcessor",
                        "KerbalEngineer.Flight.Readouts.Surface.ImpactProcessor",
                        "KerbalEngineer.Flight.Readouts.Surface.SurfaceDistanceProcessor",
                        "KerbalEngineer.Flight.Readouts.Thermal.ThermalProcessor",
                        "KerbalEngineer.Flight.Readouts.Orbital.ManoeuvreNode.ManoeuvreProcessor",
                        "KerbalEngineer.Flight.Readouts.Rendezvous.RendezvousProcessor"
                    };

                    for (int i = 0; i < processorNames.Length; i++)
                    {
                        Type procType = kerAssembly.GetType(processorNames[i]);
                        if (procType != null)
                        {
                            string shortName = procType.Name.Replace("Processor", "");
                            Traverser.TraverseStatic(procType, $"KER {shortName} 处理器");
                        }
                    }

                    // 4. 注册标准高频别名，保持 100% 向后兼容
                    Traverser.RegisterAlias("DV", "deltaV");
                    Traverser.RegisterAlias("STAGEDV", "deltaV");
                    Traverser.RegisterAlias("TOTALDV", "totalDeltaV");
                    Traverser.RegisterAlias("TWR", "actualThrustToWeight");
                    Traverser.RegisterAlias("STAGETWR", "actualThrustToWeight");
                    Traverser.RegisterAlias("BURNTIME", "time");
                    Traverser.RegisterAlias("STAGEBURNTIME", "time");
                    Traverser.RegisterAlias("TOTALBURNTIME", "totalTime");
                    Traverser.RegisterAlias("ISP", "isp");
                    Traverser.RegisterAlias("STAGEISP", "isp");
                    Traverser.RegisterAlias("THRUST", "thrust");
                    Traverser.RegisterAlias("ACTUALTHRUST", "actualThrust");

                    // 自杀式点火与着陆撞击别名
                    Traverser.RegisterAlias("SUICIDECD", "SuicideCountdown");
                    Traverser.RegisterAlias("SUICIDEALT", "SuicideAltitude");
                    Traverser.RegisterAlias("SUICIDEDV", "SuicideDeltaV");
                    Traverser.RegisterAlias("SUICIDEDIST", "SuicideDistance");
                    Traverser.RegisterAlias("SUICIDELEN", "SuicideLength");
                    Traverser.RegisterAlias("IMPACTTIME", "Time");
                    Traverser.RegisterAlias("IMPACTALT", "Altitude");

                    // 热力学别名
                    Traverser.RegisterAlias("HOTTESTTEMP", "HottestTemperature");
                    Traverser.RegisterAlias("COOLESTTEMP", "CoolestTemperature");

                    // 机动节点别名
                    Traverser.RegisterAlias("NODEDV", "NodeDeltaV");
                    Traverser.RegisterAlias("MANOEUVREDV", "NodeDeltaV");
                    Traverser.RegisterAlias("TIMETONODE", "TimeToManoeuvre");
                    Traverser.RegisterAlias("NODEBURNTIME", "BurnTime");

                    _isAvailable = true;
                    SafeLog($"[ModularFlightPanel] KerbalEngineerProbe successfully hooked KER! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] KER probe initialization warning: {ex.Message}");
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

        private static object GetLastStage()
        {
            if (!_isAvailable || _lastStageProp == null) return null;
            try
            {
                return _lastStageProp.GetValue(null, null);
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
        public static double StageDeltaV => ResolveNumeric("deltaV");
        public static double TotalDeltaV => ResolveNumeric("totalDeltaV");
        public static double StageTWR => ResolveNumeric("actualThrustToWeight");
        public static double StageBurnTime => ResolveNumeric("time");
        public static double TotalBurnTime => ResolveNumeric("totalTime");
        public static double StageIsp => ResolveNumeric("isp");
    }
}
