using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// SystemHeat 软依赖遥测反射探针
    /// 遍历飞船热力循环回路 (HeatLoop)、回路温度、过热比例、排热负荷与废热通量，零硬编码。
    /// </summary>
    public static class SystemHeatProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("SH");

        private static Type _shvType;
        private static Type _simType;
        private static Type _loopType;

        private static PropertyInfo _simulatorProp;
        private static PropertyInfo _heatLoopsProp;
        private static PropertyInfo _totalHeatGenProp;
        private static PropertyInfo _totalHeatRejProp;

        private static PropertyInfo _tempProp;
        private static PropertyInfo _nomTempProp;
        private static PropertyInfo _netFluxProp;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly shAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("SystemHeat", StringComparison.OrdinalIgnoreCase))
                    {
                        shAssembly = asm;
                        break;
                    }
                }

                if (shAssembly != null)
                {
                    _shvType = shAssembly.GetType("SystemHeat.SystemHeatVessel");
                    _simType = shAssembly.GetType("SystemHeat.SystemHeatSimulator");
                    _loopType = shAssembly.GetType("SystemHeat.HeatLoop");

                    if (_shvType != null && _simType != null && _loopType != null)
                    {
                        _simulatorProp = _shvType.GetProperty("Simulator", BindingFlags.Public | BindingFlags.Instance);
                        _heatLoopsProp = _simType.GetProperty("HeatLoops", BindingFlags.Public | BindingFlags.Instance);
                        _totalHeatGenProp = _simType.GetProperty("TotalHeatGeneration", BindingFlags.Public | BindingFlags.Instance);
                        _totalHeatRejProp = _simType.GetProperty("TotalHeatRejection", BindingFlags.Public | BindingFlags.Instance);

                        _tempProp = _loopType.GetProperty("Temperature", BindingFlags.Public | BindingFlags.Instance);
                        _nomTempProp = _loopType.GetProperty("NominalTemperature", BindingFlags.Public | BindingFlags.Instance);
                        _netFluxProp = _loopType.GetProperty("NetFlux", BindingFlags.Public | BindingFlags.Instance);

                        // 1. 实例反射遍历 Simulator
                        Traverser.TraverseInstance(_simType, GetActiveSimulator, "SystemHeat 热力仿真中枢 (Simulator)");

                        // 2. 实例反射遍历主热回路
                        Traverser.TraverseInstance(_loopType, GetPrimaryLoop, "SystemHeat 主热力回路 (HeatLoop)");

                        // 3. 注册通用高频别名
                        RegisterDynamicSHMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] SystemHeatProbe successfully hooked SystemHeat! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] SystemHeat probe initialization warning: {ex.Message}");
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

        public static object GetActiveSimulator()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _shvType == null || _simulatorProp == null) return null;

            try
            {
                for (int i = 0; i < v.vesselModules.Count; i++)
                {
                    VesselModule vm = v.vesselModules[i];
                    if (vm != null && _shvType.IsInstanceOfType(vm))
                    {
                        return _simulatorProp.GetValue(vm, null);
                    }
                }

                var comp = v.GetComponent(_shvType);
                if (comp != null)
                {
                    return _simulatorProp.GetValue(comp, null);
                }
            }
            catch { }

            return null;
        }

        public static object GetPrimaryLoop()
        {
            object sim = GetActiveSimulator();
            if (sim == null || _heatLoopsProp == null) return null;

            try
            {
                var list = _heatLoopsProp.GetValue(sim, null) as IList;
                if (list != null && list.Count > 0)
                {
                    // 挑选温度最高的回路作为首要警报监控回路
                    object hottest = list[0];
                    float maxT = 0f;
                    if (_tempProp != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            object l = list[i];
                            if (l != null)
                            {
                                float t = (float)_tempProp.GetValue(l, null);
                                if (t > maxT)
                                {
                                    maxT = t;
                                    hottest = l;
                                }
                            }
                        }
                    }
                    return hottest;
                }
            }
            catch { }

            return null;
        }

        private static void RegisterDynamicSHMembers()
        {
            // 主回路最高工作温度 (LoopTemp)
            Traverser.RegisterCustom("LoopTemp", typeof(float), () =>
            {
                object loop = GetPrimaryLoop();
                if (loop != null && _tempProp != null)
                {
                    try { return (float)_tempProp.GetValue(loop, null); } catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "当前温度最高的主热力回路运行温度 (K)", new[] { "LOOPTEMP", "TEMP", "HOTTESTTEMP" });

            // 主回路额定设计温度 (NominalTemp)
            Traverser.RegisterCustom("NominalTemp", typeof(float), () =>
            {
                object loop = GetPrimaryLoop();
                if (loop != null && _nomTempProp != null)
                {
                    try { return (float)_nomTempProp.GetValue(loop, null); } catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "主热力回路标称/额定安全工作温度上限 (K)", new[] { "NOMINALTEMP", "NOMTEMP" });

            // 回路过热超标百分比 (OverheatRatio)
            Traverser.RegisterCustom("OverheatRatio", typeof(float), () =>
            {
                object loop = GetPrimaryLoop();
                if (loop != null && _tempProp != null && _nomTempProp != null)
                {
                    try
                    {
                        float t = (float)_tempProp.GetValue(loop, null);
                        float nom = (float)_nomTempProp.GetValue(loop, null);
                        if (nom > 1f)
                        {
                            return (t / nom) * 100f;
                        }
                    }
                    catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "当前主回路温度相对标称安全温度的过热百分比 (100% 为临界)", new[] { "OVERHEATRATIO", "OVERHEAT", "OVERHEATPERCENT" });

            // 全舰总产热废热通量 (TotalHeatGeneration)
            Traverser.RegisterCustom("TotalHeatGen", typeof(float), () =>
            {
                object sim = GetActiveSimulator();
                if (sim != null && _totalHeatGenProp != null)
                {
                    try { return (float)_totalHeatGenProp.GetValue(sim, null); } catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "全舰所有热源部件即时产生的废热总通量 (kW)", new[] { "TOTALHEATGEN", "HEATGEN" });

            // 全舰总辐射排热能力 (TotalHeatRejection)
            Traverser.RegisterCustom("TotalHeatRej", typeof(float), () =>
            {
                object sim = GetActiveSimulator();
                if (sim != null && _totalHeatRejProp != null)
                {
                    try { return (float)_totalHeatRejProp.GetValue(sim, null); } catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "全舰散热片向外界空间辐射排出的总热通量 (kW)", new[] { "TOTALHEATREJ", "HEATREJ", "RADIATORCAP" });

            // 净热量增减通量 (NetFlux)
            Traverser.RegisterCustom("NetHeatFlux", typeof(float), () =>
            {
                object loop = GetPrimaryLoop();
                if (loop != null && _netFluxProp != null)
                {
                    try { return (float)_netFluxProp.GetValue(loop, null); } catch { }
                }
                object sim = GetActiveSimulator();
                if (sim != null && _totalHeatGenProp != null && _totalHeatRejProp != null)
                {
                    try
                    {
                        float gen = (float)_totalHeatGenProp.GetValue(sim, null);
                        float rej = (float)_totalHeatRejProp.GetValue(sim, null);
                        return gen - rej;
                    }
                    catch { }
                }
                return 0f;
            }, "SystemHeat 热力系统", "热力系统即时净热通量积聚速率 (正为升温积热，负为降温排热, kW)", new[] { "NETHEATFLUX", "NETFLUX" });

            // 活跃热力回路总数 (LoopCount)
            Traverser.RegisterCustom("LoopCount", typeof(int), () =>
            {
                object sim = GetActiveSimulator();
                if (sim != null && _heatLoopsProp != null)
                {
                    try
                    {
                        var list = _heatLoopsProp.GetValue(sim, null) as IList;
                        if (list != null) return list.Count;
                    }
                    catch { }
                }
                return 0;
            }, "SystemHeat 热力系统", "载具上构建运行的物理独立热力循环回路总数", new[] { "LOOPCOUNT", "LOOPS" });

            // 是否处于过热危险状态 (IsOverheating)
            Traverser.RegisterCustom("IsOverheating", typeof(bool), () =>
            {
                object loop = GetPrimaryLoop();
                if (loop != null && _tempProp != null && _nomTempProp != null)
                {
                    try
                    {
                        float t = (float)_tempProp.GetValue(loop, null);
                        float nom = (float)_nomTempProp.GetValue(loop, null);
                        return nom > 1f && t > nom;
                    }
                    catch { }
                }
                return false;
            }, "SystemHeat 热力系统", "是否存在任何回路超过额定标称温度（过热报警）", new[] { "ISOVERHEATING", "OVERHEATING" });
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
