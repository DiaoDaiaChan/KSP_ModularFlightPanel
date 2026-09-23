using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// NavyFish Docking Port Alignment Indicator (DPAI) 软依赖遥测反射探针
    /// 遍历对准仪的距离、闭合速率、CDI 横纵平移偏移与滚转角对齐参数，零硬编码。
    /// </summary>
    public static class DockingAlignmentProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("DOCK");

        private static Type _dpaiType;
        private static FieldInfo _distField;
        private static FieldInfo _closureVField;
        private static FieldInfo _closureDField;
        private static FieldInfo _transDevField;
        private static FieldInfo _orientDevField;
        private static FieldInfo _rollOffsetField;
        private static FieldInfo _currentTargetField;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly dpaiAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("DockingPortAlignmentIndicator", StringComparison.OrdinalIgnoreCase))
                    {
                        dpaiAssembly = asm;
                        break;
                    }
                }

                if (dpaiAssembly != null)
                {
                    _dpaiType = dpaiAssembly.GetType("NavyFish.DPAI.DockingPortAlignmentIndicator");
                    if (_dpaiType != null)
                    {
                        // 1. 实例反射遍历全部成员
                        Traverser.TraverseInstance(_dpaiType, GetDpaiInstance, "DPAI 对接引导器 (Indicator)");

                        _distField = _dpaiType.GetField("distanceToTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _closureVField = _dpaiType.GetField("closureV", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _closureDField = _dpaiType.GetField("closureD", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _transDevField = _dpaiType.GetField("translationDeviation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _orientDevField = _dpaiType.GetField("orientationDeviation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _rollOffsetField = _dpaiType.GetField("rollOffset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _currentTargetField = _dpaiType.GetField("currentTarget", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        // 2. 注册通用高频别名
                        RegisterDynamicDPAIMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] DockingAlignmentProbe successfully hooked DPAI! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] DPAI probe initialization warning: {ex.Message}");
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

        private static object _cachedDpaiInstance = null;
        private static float _lastFindTime = 0f;

        public static object GetDpaiInstance()
        {
            if (_dpaiType == null) return null;
            if (_cachedDpaiInstance != null && ((UnityEngine.Object)_cachedDpaiInstance) != null)
            {
                return _cachedDpaiInstance;
            }

            if (Time.realtimeSinceStartup - _lastFindTime > 2.0f)
            {
                _lastFindTime = Time.realtimeSinceStartup;
                try
                {
                    _cachedDpaiInstance = UnityEngine.Object.FindObjectOfType(_dpaiType);
                }
                catch { }
            }

            return _cachedDpaiInstance;
        }

        private static void RegisterDynamicDPAIMembers()
        {
            // 对接净距离 (Distance)
            Traverser.RegisterCustom("Distance", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _distField != null)
                {
                    try { return (float)_distField.GetValue(inst); } catch { }
                }
                return float.NaN;
            }, "DPAI 对接引导", "与目标对接端口的轴向对齐净距离 (m)", new[] { "DISTANCE", "DIST" });

            // 闭合相对速度 (ClosureRate)
            Traverser.RegisterCustom("ClosureRate", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _closureVField != null)
                {
                    try { return (float)_closureVField.GetValue(inst); } catch { }
                }
                return float.NaN;
            }, "DPAI 对接引导", "沿对齐轴向靠近目标的相对接近速率 (m/s)", new[] { "CLOSURERATE", "CLOSUREV", "RATE" });

            // 水平平移对准偏差 (DevX)
            Traverser.RegisterCustom("DevX", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _transDevField != null)
                {
                    try
                    {
                        Vector2 v = (Vector2)_transDevField.GetValue(inst);
                        return v.x;
                    }
                    catch { }
                }
                return 0f;
            }, "DPAI 对接引导", "目标对接轴心的水平 CDI 偏航平移偏差", new[] { "DEVX", "CDIX", "TRANSLATIONX" });

            // 垂直平移对准偏差 (DevY)
            Traverser.RegisterCustom("DevY", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _transDevField != null)
                {
                    try
                    {
                        Vector2 v = (Vector2)_transDevField.GetValue(inst);
                        return v.y;
                    }
                    catch { }
                }
                return 0f;
            }, "DPAI 对接引导", "目标对接轴心的垂直 CDI 偏航平移偏差", new[] { "DEVY", "CDIY", "TRANSLATIONY" });

            // 滚转角对齐偏差 (RollOffset)
            Traverser.RegisterCustom("RollOffset", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _rollOffsetField != null)
                {
                    try { return (float)_rollOffsetField.GetValue(inst); } catch { }
                }
                return 0f;
            }, "DPAI 对接引导", "两端口对准所需的滚转角度差 (deg)", new[] { "ROLLOFFSET", "ROLL" });

            // 俯仰角偏差 (PitchDev)
            Traverser.RegisterCustom("PitchDev", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _orientDevField != null)
                {
                    try
                    {
                        Vector3 v = (Vector3)_orientDevField.GetValue(inst);
                        return v.x;
                    }
                    catch { }
                }
                return 0f;
            }, "DPAI 对接引导", "姿态角俯仰方向偏差 (deg)", new[] { "PITCHDEV" });

            // 偏航角偏差 (YawDev)
            Traverser.RegisterCustom("YawDev", typeof(float), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _orientDevField != null)
                {
                    try
                    {
                        Vector3 v = (Vector3)_orientDevField.GetValue(inst);
                        return v.y;
                    }
                    catch { }
                }
                return 0f;
            }, "DPAI 对接引导", "姿态角偏航方向偏差 (deg)", new[] { "YAWDEV" });

            // 目标端口名称 (TargetName)
            Traverser.RegisterCustom("TargetName", typeof(string), () =>
            {
                object inst = GetDpaiInstance();
                if (inst != null && _currentTargetField != null)
                {
                    try
                    {
                        ITargetable tgt = _currentTargetField.GetValue(inst) as ITargetable;
                        if (tgt != null) return tgt.GetDisplayName();
                    }
                    catch { }
                }
                return "NONE";
            }, "DPAI 对接引导", "当前锁定的目标对接端口或对端载具名称", new[] { "TARGETNAME", "TARGET" });
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
