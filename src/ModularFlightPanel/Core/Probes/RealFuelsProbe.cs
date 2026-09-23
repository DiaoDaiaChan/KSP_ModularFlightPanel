using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// RealFuels 软依赖遥测反射探针
    /// 遍历 RealFuels 推进剂沉底状态 (Ullage)、可点火次数 (Ignitions) 与发动机配置 (ModuleEngineConfigs)，零硬编码。
    /// </summary>
    public static class RealFuelsProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("RF");

        private static Type _mecType;
        private static Type _ullageModuleType;
        private static PropertyInfo _ignitionsProp;
        private static FieldInfo _configNameField;
        private static FieldInfo _configDisplayField;
        private static FieldInfo _throttleField;

        private static FieldInfo _ullageSetsField;
        private static MethodInfo _getPropellantStatusMethod;
        private static MethodInfo _getPropellantStabilityMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly rfAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("RealFuels", StringComparison.OrdinalIgnoreCase))
                    {
                        rfAssembly = asm;
                        break;
                    }
                }

                if (rfAssembly != null)
                {
                    _mecType = rfAssembly.GetType("RealFuels.ModuleEngineConfigs");
                    _ullageModuleType = rfAssembly.GetType("RealFuels.Ullage.UllageModule");

                    if (_mecType != null)
                    {
                        Traverser.TraverseInstance(_mecType, GetActiveMEC, "RealFuels 发动机配置 (ModuleEngineConfigs)");

                        _ignitionsProp = _mecType.GetProperty("Ignitions", BindingFlags.Public | BindingFlags.Instance);
                        _configNameField = _mecType.GetField("configuration", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _configDisplayField = _mecType.GetField("configurationDisplay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _throttleField = _mecType.GetField("throttle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    if (_ullageModuleType != null)
                    {
                        Traverser.TraverseInstance(_ullageModuleType, GetActiveUllageModule, "RealFuels 沉底物理系统 (UllageModule)");
                        _ullageSetsField = _ullageModuleType.GetField("ullageSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    Type simType = rfAssembly.GetType("RealFuels.Ullage.UllageSimulator");
                    if (simType != null)
                    {
                        _getPropellantStatusMethod = simType.GetMethod("GetPropellantStatus", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _getPropellantStabilityMethod = simType.GetMethod("GetPropellantStability", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    }

                    // 注册高频通用别名
                    RegisterDynamicRFMembers();

                    _isAvailable = true;
                    SafeLog($"[ModularFlightPanel] RealFuelsProbe successfully hooked RealFuels! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] RealFuels probe initialization warning: {ex.Message}");
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

        private static PartModule FindModule(Part p, Type targetType)
        {
            if (p == null || p.Modules == null || targetType == null) return null;
            for (int i = 0; i < p.Modules.Count; i++)
            {
                var m = p.Modules[i];
                if (m != null && targetType.IsAssignableFrom(m.GetType()))
                    return m;
            }
            return null;
        }

        public static object GetActiveMEC()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _mecType == null) return null;

            // 优先查找处于点火状态的引擎 ModuleEngineConfigs
            List<Part> parts = v.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                var mec = FindModule(p, _mecType);
                if (mec != null)
                {
                    var eng = p.FindModuleImplementing<ModuleEngines>();
                    if (eng != null && eng.isOperational && eng.currentThrottle > 0.01f)
                    {
                        return mec;
                    }
                }
            }

            // 次选：任意启用的发动机配置
            for (int i = 0; i < parts.Count; i++)
            {
                var mec = FindModule(parts[i], _mecType);
                if (mec != null) return mec;
            }

            return null;
        }

        public static object GetActiveUllageModule()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _ullageModuleType == null) return null;

            // 载具上首个 UllageModule 组件
            for (int i = 0; i < v.Parts.Count; i++)
            {
                var um = FindModule(v.Parts[i], _ullageModuleType);
                if (um != null) return um;
            }

            return null;
        }

        private static object GetActiveUllageSimulator()
        {
            object um = GetActiveUllageModule();
            if (um != null && _ullageSetsField != null)
            {
                try
                {
                    var sets = _ullageSetsField.GetValue(um) as IDictionary;
                    if (sets != null)
                    {
                        foreach (DictionaryEntry entry in sets)
                        {
                            object uSet = entry.Value;
                            if (uSet != null)
                            {
                                var simField = uSet.GetType().GetField("simulator", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (simField != null) return simField.GetValue(uSet);
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static void RegisterDynamicRFMembers()
        {
            // 剩余点火次数 (Ignitions)
            Traverser.RegisterCustom("Ignitions", typeof(int), () =>
            {
                object mec = GetActiveMEC();
                if (mec != null && _ignitionsProp != null)
                {
                    try { return (int)_ignitionsProp.GetValue(mec, null); } catch { }
                }
                return -1;
            }, "RealFuels 动力推进", "当前活动发动机剩余可点火重启次数 (-1 代表无限)", new[] { "IGNITIONS", "IGNITIONSLEFT", "IGNCOUNT" });

            // 推进剂沉底状态文本 (Ullage)
            Traverser.RegisterCustom("Ullage", typeof(string), () =>
            {
                object sim = GetActiveUllageSimulator();
                if (sim != null && _getPropellantStatusMethod != null)
                {
                    try
                    {
                        object res = _getPropellantStatusMethod.Invoke(sim, null);
                        if (res != null) return res.ToString();
                    }
                    catch { }
                }
                return "NOMINAL";
            }, "RealFuels 沉底物理", "当前推进剂在储箱内的沉底稳定状态 (Stable / Unstable 等)", new[] { "ULLAGE", "ULLAGESTATE", "STATUS" });

            // 沉底稳定度数值 (UllageStability)
            Traverser.RegisterCustom("UllageStability", typeof(double), () =>
            {
                object sim = GetActiveUllageSimulator();
                if (sim != null && _getPropellantStabilityMethod != null)
                {
                    try { return Convert.ToDouble(_getPropellantStabilityMethod.Invoke(sim, null)); } catch { }
                }
                return 1.0;
            }, "RealFuels 沉底物理", "推进剂沉底稳定性量化百分比 (0..1)", new[] { "ULLAGESTABILITY", "STABILITY" });

            // 发动机当前配置型号 (EngineConfig)
            Traverser.RegisterCustom("EngineConfig", typeof(string), () =>
            {
                object mec = GetActiveMEC();
                if (mec != null)
                {
                    if (_configDisplayField != null)
                    {
                        try
                        {
                            string disp = _configDisplayField.GetValue(mec) as string;
                            if (!string.IsNullOrEmpty(disp)) return disp;
                        }
                        catch { }
                    }
                    if (_configNameField != null)
                    {
                        try
                        {
                            string name = _configNameField.GetValue(mec) as string;
                            if (!string.IsNullOrEmpty(name)) return name;
                        }
                        catch { }
                    }
                }
                return "DEFAULT";
            }, "RealFuels 动力推进", "当前正在运行的发动机配置型号名称", new[] { "CONFIG", "ENGINECONFIG", "CONFIGNAME" });

            // 引擎设定推力油门百分比 (Throttle)
            Traverser.RegisterCustom("Throttle", typeof(float), () =>
            {
                object mec = GetActiveMEC();
                if (mec != null && _throttleField != null)
                {
                    try { return (float)_throttleField.GetValue(mec); } catch { }
                }
                return 0f;
            }, "RealFuels 动力推进", "发动机配置当前的有效油门开度比率 (0..1)", new[] { "THROTTLE", "CONFIGTHROTTLE" });
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
