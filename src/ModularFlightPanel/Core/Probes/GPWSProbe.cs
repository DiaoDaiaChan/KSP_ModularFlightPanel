using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// KSP GPWS (Ground Proximity Warning System) 软依赖遥测反射探针
    /// 遍历近地警告系统的雷达真高、下沉率、V1/起飞决断速度、失速迎角与起落架状态，零硬编码。
    /// </summary>
    public static class GPWSProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("GPWS");

        private static Type _gpwsType;
        private static PropertyInfo _radarAltProp;
        private static PropertyInfo _verSpeedProp;
        private static PropertyInfo _horSpeedProp;
        private static FieldInfo _planeField;

        private static PropertyInfo _v1SpeedProp;
        private static PropertyInfo _takeoffSpeedProp;
        private static PropertyInfo _landingSpeedProp;
        private static PropertyInfo _stallAoaProp;
        private static FieldInfo _isGearDownField;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly gpwsAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("GPWS", StringComparison.OrdinalIgnoreCase))
                    {
                        gpwsAssembly = asm;
                        break;
                    }
                }

                if (gpwsAssembly != null)
                {
                    _gpwsType = gpwsAssembly.GetType("KSP_GPWS.Gpws");
                    if (_gpwsType != null)
                    {
                        Traverser.TraverseInstance(_gpwsType, GetGpwsInstance, "GPWS 近地告警核心 (Gpws)");

                        _radarAltProp = _gpwsType.GetProperty("RadarAltitude", BindingFlags.Public | BindingFlags.Instance);
                        _verSpeedProp = _gpwsType.GetProperty("VerSpeed", BindingFlags.Public | BindingFlags.Instance);
                        _horSpeedProp = _gpwsType.GetProperty("HorSpeed", BindingFlags.Public | BindingFlags.Instance);
                        _planeField = _gpwsType.GetField("plane", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (_planeField != null)
                        {
                            Type planeType = _planeField.FieldType;
                            _v1SpeedProp = planeType.GetProperty("V1Speed", BindingFlags.Public | BindingFlags.Instance);
                            _takeoffSpeedProp = planeType.GetProperty("TakeOffSpeed", BindingFlags.Public | BindingFlags.Instance);
                            _landingSpeedProp = planeType.GetProperty("LandingSpeed", BindingFlags.Public | BindingFlags.Instance);
                            _stallAoaProp = planeType.GetProperty("StallAoa", BindingFlags.Public | BindingFlags.Instance);
                            _isGearDownField = planeType.GetField("isGearDown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        }

                        // 注册通用高频别名
                        RegisterDynamicGPWSMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] GPWSProbe successfully hooked GPWS! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] GPWS probe initialization warning: {ex.Message}");
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

        private static object _cachedGpwsInstance = null;
        private static float _lastFindTime = 0f;

        public static object GetGpwsInstance()
        {
            if (_gpwsType == null) return null;
            if (_cachedGpwsInstance != null && ((UnityEngine.Object)_cachedGpwsInstance) != null)
            {
                return _cachedGpwsInstance;
            }

            if (Time.realtimeSinceStartup - _lastFindTime > 2.0f)
            {
                _lastFindTime = Time.realtimeSinceStartup;
                try
                {
                    _cachedGpwsInstance = UnityEngine.Object.FindObjectOfType(_gpwsType);
                }
                catch { }
            }

            return _cachedGpwsInstance;
        }

        public static object GetGpwsPlaneInstance()
        {
            object inst = GetGpwsInstance();
            if (inst == null || _planeField == null) return null;
            try { return _planeField.GetValue(inst); } catch { return null; }
        }

        private static void RegisterDynamicGPWSMembers()
        {
            // 雷达高度 (RadarAltitude)
            Traverser.RegisterCustom("RadarAltitude", typeof(double), () =>
            {
                object inst = GetGpwsInstance();
                if (inst != null && _radarAltProp != null)
                {
                    try { return (double)_radarAltProp.GetValue(inst, null); } catch { }
                }
                return double.NaN;
            }, "GPWS 近地告警", "无线电测距雷达真实地表净高度 (m)", new[] { "RADARALT", "RADAR", "RADIOALT" });

            // 垂直下沉速率 (SinkRate / VerSpeed)
            Traverser.RegisterCustom("SinkRate", typeof(double), () =>
            {
                object inst = GetGpwsInstance();
                if (inst != null && _verSpeedProp != null)
                {
                    try { return (double)_verSpeedProp.GetValue(inst, null); } catch { }
                }
                return double.NaN;
            }, "GPWS 近地告警", "即时垂直升降率或下沉速度 (m/s)", new[] { "SINKRATE", "VERSPEED", "VERTICALSPEED" });

            // 地速/水平速度 (HorSpeed)
            Traverser.RegisterCustom("HorSpeed", typeof(double), () =>
            {
                object inst = GetGpwsInstance();
                if (inst != null && _horSpeedProp != null)
                {
                    try { return (double)_horSpeedProp.GetValue(inst, null); } catch { }
                }
                return double.NaN;
            }, "GPWS 近地告警", "对地水平移动速度分量 (m/s)", new[] { "HORSPEED", "GROUNDSPEED" });

            // V1 决断速度 (V1Speed)
            Traverser.RegisterCustom("V1Speed", typeof(float), () =>
            {
                object plane = GetGpwsPlaneInstance();
                if (plane != null && _v1SpeedProp != null)
                {
                    try { return (float)_v1SpeedProp.GetValue(plane, null); } catch { }
                }
                return 0f;
            }, "GPWS 飞行决断", "起飞中断与决断基准表速 V1 (m/s)", new[] { "V1", "V1SPEED" });

            // 抬轮速度 Vr (TakeOffSpeed)
            Traverser.RegisterCustom("TakeOffSpeed", typeof(float), () =>
            {
                object plane = GetGpwsPlaneInstance();
                if (plane != null && _takeoffSpeedProp != null)
                {
                    try { return (float)_takeoffSpeedProp.GetValue(plane, null); } catch { }
                }
                return 0f;
            }, "GPWS 飞行决断", "起飞抬前轮表速 Vr (m/s)", new[] { "VR", "TAKEOFFSPEED" });

            // 进近进场速度 Vref (LandingSpeed)
            Traverser.RegisterCustom("LandingSpeed", typeof(float), () =>
            {
                object plane = GetGpwsPlaneInstance();
                if (plane != null && _landingSpeedProp != null)
                {
                    try { return (float)_landingSpeedProp.GetValue(plane, null); } catch { }
                }
                return 0f;
            }, "GPWS 飞行决断", "最终进近着陆参考基准表速 Vref (m/s)", new[] { "VREF", "LANDINGSPEED" });

            // 失速临界迎角 (StallAoa)
            Traverser.RegisterCustom("StallAoa", typeof(float), () =>
            {
                object plane = GetGpwsPlaneInstance();
                if (plane != null && _stallAoaProp != null)
                {
                    try { return (float)_stallAoaProp.GetValue(plane, null); } catch { }
                }
                return 0f;
            }, "GPWS 飞行安全", "气动失速临界攻角门限 (deg)", new[] { "STALLAOA" });

            // 起落架放下状态 (IsGearDown)
            Traverser.RegisterCustom("IsGearDown", typeof(bool), () =>
            {
                object plane = GetGpwsPlaneInstance();
                if (plane != null && _isGearDownField != null)
                {
                    try { return (bool)_isGearDownField.GetValue(plane); } catch { }
                }
                return false;
            }, "GPWS 飞行安全", "起落架是否已可靠锁定放下", new[] { "GEARDOWN", "GEAR" });
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
