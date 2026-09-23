using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// TestFlight 软依赖遥测反射探针
    /// 遍历发动机可靠性、运行燃时、即时失效率与部件故障状态，零硬编码。
    /// </summary>
    public static class TestFlightProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("TF");

        private static Type _tfCoreType;
        private static MethodInfo _getOperatingTimeMethod;
        private static MethodInfo _getBaseFailureRateMethod;
        private static MethodInfo _getFlightDataMethod;
        private static MethodInfo _getActiveFailuresMethod;
        private static MethodInfo _getPartStatusMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly tfAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("TestFlightCore", StringComparison.OrdinalIgnoreCase) ||
                        asmName.Equals("TestFlight", StringComparison.OrdinalIgnoreCase))
                    {
                        tfAssembly = asm;
                        break;
                    }
                }

                if (tfAssembly != null)
                {
                    _tfCoreType = tfAssembly.GetType("TestFlightCore.TestFlightCore");
                    if (_tfCoreType != null)
                    {
                        Traverser.TraverseInstance(_tfCoreType, GetActiveTFCore, "TestFlight 引擎可靠性核心 (TestFlightCore)");

                        _getOperatingTimeMethod = _tfCoreType.GetMethod("GetOperatingTime", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _getBaseFailureRateMethod = _tfCoreType.GetMethod("GetBaseFailureRate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _getFlightDataMethod = _tfCoreType.GetMethod("GetFlightData", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _getActiveFailuresMethod = _tfCoreType.GetMethod("GetActiveFailures", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _getPartStatusMethod = _tfCoreType.GetMethod("GetPartStatus", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                        // 注册高频通用计算项与别名
                        RegisterDynamicTFMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] TestFlightProbe successfully hooked TestFlight! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] TestFlight probe initialization warning: {ex.Message}");
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

        public static object GetActiveTFCore()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || _tfCoreType == null) return null;

            // 优先查找处于点火运行中引擎的 TestFlightCore
            List<Part> parts = v.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                var tf = FindModule(p, _tfCoreType);
                if (tf != null)
                {
                    var eng = p.FindModuleImplementing<ModuleEngines>();
                    if (eng != null && eng.isOperational && eng.currentThrottle > 0.01f)
                    {
                        return tf;
                    }
                }
            }

            // 次选：载具上首个可用的 TestFlightCore
            for (int i = 0; i < parts.Count; i++)
            {
                var tf = FindModule(parts[i], _tfCoreType);
                if (tf != null) return tf;
            }

            return null;
        }

        private static void RegisterDynamicTFMembers()
        {
            // 引擎实际累计工作燃时 (OperatingTime)
            Traverser.RegisterCustom("OperatingTime", typeof(float), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getOperatingTimeMethod != null)
                {
                    try { return (float)_getOperatingTimeMethod.Invoke(tf, null); } catch { }
                }
                return 0f;
            }, "TestFlight 动力可靠性", "当前引擎本次点火累计实际持续燃烧时长 (s)", new[] { "OPERATINGTIME", "BURNTIME" });

            // 基础瞬时失效率 (BaseFailureRate)
            Traverser.RegisterCustom("FailureRate", typeof(double), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getBaseFailureRateMethod != null)
                {
                    try { return (double)_getBaseFailureRateMethod.Invoke(tf, null); } catch { }
                }
                return 0.0;
            }, "TestFlight 动力可靠性", "当前工况下引擎即时故障失效概率率值", new[] { "FAILURERATE", "FAILCHANCE", "FAILRATE" });

            // 飞行积累测试数据点 (FlightData)
            Traverser.RegisterCustom("FlightData", typeof(float), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getFlightDataMethod != null)
                {
                    try { return (float)_getFlightDataMethod.Invoke(tf, null); } catch { }
                }
                return 0f;
            }, "TestFlight 动力可靠性", "该型号发动机累计飞行验证积累的数据点 (DU)", new[] { "FLIGHTDATA", "DATA", "DU" });

            // 引擎运行健康状态文本 (Status)
            Traverser.RegisterCustom("Status", typeof(string), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getActiveFailuresMethod != null)
                {
                    try
                    {
                        var list = _getActiveFailuresMethod.Invoke(tf, null) as IList;
                        if (list != null && list.Count > 0)
                        {
                            object failure = list[0];
                            if (failure != null)
                            {
                                var titleProp = failure.GetType().GetProperty("failureTitle") ?? (MemberInfo)failure.GetType().GetField("failureTitle");
                                if (titleProp is PropertyInfo pi) return pi.GetValue(failure, null) as string ?? "FAILED";
                                if (titleProp is FieldInfo fi) return fi.GetValue(failure) as string ?? "FAILED";
                                return "FAILED";
                            }
                        }
                    }
                    catch { }
                }
                return "NOMINAL";
            }, "TestFlight 动力可靠性", "发动机当前健康与故障详情 (NOMINAL 或具体故障名称)", new[] { "STATUS", "HEALTH", "FAILURETYPE" });

            // 是否已发生故障 (Failed)
            Traverser.RegisterCustom("Failed", typeof(bool), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getActiveFailuresMethod != null)
                {
                    try
                    {
                        var list = _getActiveFailuresMethod.Invoke(tf, null) as IList;
                        return list != null && list.Count > 0;
                    }
                    catch { }
                }
                return false;
            }, "TestFlight 动力可靠性", "当前引擎是否发生任何异常故障", new[] { "FAILED", "ISFAILED", "HASFAILURE" });

            // 当前活动故障数量 (FailureCount)
            Traverser.RegisterCustom("FailureCount", typeof(int), () =>
            {
                object tf = GetActiveTFCore();
                if (tf != null && _getActiveFailuresMethod != null)
                {
                    try
                    {
                        var list = _getActiveFailuresMethod.Invoke(tf, null) as IList;
                        if (list != null) return list.Count;
                    }
                    catch { }
                }
                return 0;
            }, "TestFlight 动力可靠性", "发动机当前并发活跃故障总数", new[] { "FAILURECOUNT", "FAILCOUNT" });
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
