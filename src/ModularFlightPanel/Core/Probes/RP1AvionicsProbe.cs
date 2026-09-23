using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// RP-1 / RP-0 航电系统 (Avionics) 软依赖遥测反射探针
    /// 自动反射遍历载具航电控制吨位上限、总质量、失控/轴向控制锁定状态、星际控制阈值与电量消耗，零硬编码。
    /// </summary>
    public static class RP1AvionicsProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("RP1");

        // 反射类型与方法缓存
        private static Type _controlLockerUtilsType;
        private static MethodInfo _shouldLockMethod;
        private static Type _moduleAvionicsType;
        private static Type _moduleProcAvionicsType;

        // 运行时状态缓存 (每个物理帧/调用间隔更新，防止重复计算)
        private static int _lastUpdateFrame = -1;
        private static int _cachedLockLevel = 2; // 2=Unlocked, 1=Axial, 0=Locked
        private static float _cachedMaxMass = 0f;
        private static float _cachedVesselMass = 0f;
        private static bool _cachedInterplanetaryLocked = false;
        private static float _cachedTotalWatts = 0f;
        private static int _cachedAvionicsCount = 0;
        private static int _cachedActiveCount = 0;
        private static int _cachedDeadCount = 0;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly rp0Assembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("RP0", StringComparison.OrdinalIgnoreCase))
                    {
                        rp0Assembly = asm;
                        break;
                    }
                }

                if (rp0Assembly != null)
                {
                    _controlLockerUtilsType = rp0Assembly.GetType("RP0.ControlLockerUtils");
                    _moduleAvionicsType = rp0Assembly.GetType("RP0.ModuleAvionics");
                    _moduleProcAvionicsType = rp0Assembly.GetType("RP0.ProceduralAvionics.ModuleProceduralAvionics");

                    if (_controlLockerUtilsType != null)
                    {
                        // public static LockLevel ShouldLock(List<Part> parts, bool countClamps, out float maxMass, out float vesselMass, out bool isLimitedByNonInterplanetary)
                        _shouldLockMethod = _controlLockerUtilsType.GetMethod("ShouldLock", BindingFlags.Public | BindingFlags.Static);

                        // 1. 遍历 ControlLockerUtils 静态成员
                        Traverser.TraverseType(_controlLockerUtilsType, "RP-1 航电锁控中枢 (ControlLockerUtils)");

                        // 2. 遍历 ModuleAvionics 实例成员 (主航电模块)
                        if (_moduleAvionicsType != null)
                        {
                            Traverser.TraverseInstance(_moduleAvionicsType, GetPrimaryAvionicsModule, "RP-1 航电部件模块 (ModuleAvionics)");
                        }

                        // 3. 遍历 ModuleProceduralAvionics 实例成员
                        if (_moduleProcAvionicsType != null)
                        {
                            Traverser.TraverseInstance(_moduleProcAvionicsType, GetPrimaryProcAvionicsModule, "RP-1 过程航电部件模块 (ModuleProceduralAvionics)");
                        }

                        // 4. 注册动态计算项与别名
                        RegisterDynamicRP1Members();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] RP1AvionicsProbe successfully hooked RP-1! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"[ModularFlightPanel] Failed to initialize RP1AvionicsProbe: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static void UpdateVesselAvionicsCache()
        {
            if (Time.frameCount == _lastUpdateFrame) return;
            _lastUpdateFrame = Time.frameCount;

            _cachedLockLevel = 2;
            _cachedMaxMass = 0f;
            _cachedVesselMass = 0f;
            _cachedInterplanetaryLocked = false;
            _cachedTotalWatts = 0f;
            _cachedAvionicsCount = 0;
            _cachedActiveCount = 0;
            _cachedDeadCount = 0;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.parts == null || v.parts.Count == 0) return;

            if (_shouldLockMethod != null)
            {
                try
                {
                    object[] args = new object[] { v.parts, false, 0f, 0f, false };
                    object result = _shouldLockMethod.Invoke(null, args);
                    if (result != null)
                    {
                        // LockLevel enum: Locked = 0, Axial = 1, Unlocked = 2
                        _cachedLockLevel = Convert.ToInt32(result);
                        _cachedMaxMass = Convert.ToSingle(args[2]);
                        _cachedVesselMass = Convert.ToSingle(args[3]);
                        _cachedInterplanetaryLocked = Convert.ToBoolean(args[4]);
                    }
                }
                catch
                {
                    // 静默捕获
                }
            }

            // 统计航电功耗与模块状态
            if (_moduleAvionicsType != null)
            {
                try
                {
                    FieldInfo wattsField = _moduleAvionicsType.GetField("currentWatts", BindingFlags.Public | BindingFlags.Instance);
                    FieldInfo deadField = _moduleAvionicsType.GetField("dead", BindingFlags.Public | BindingFlags.Instance);
                    FieldInfo enabledField = _moduleAvionicsType.GetField("systemEnabled", BindingFlags.Public | BindingFlags.Instance);

                    for (int i = 0; i < v.parts.Count; i++)
                    {
                        Part p = v.parts[i];
                        for (int m = 0; m < p.Modules.Count; m++)
                        {
                            PartModule mod = p.Modules[m];
                            if (mod != null && _moduleAvionicsType.IsAssignableFrom(mod.GetType()))
                            {
                                _cachedAvionicsCount++;
                                bool isDead = deadField != null && (bool)deadField.GetValue(mod);
                                bool isEnabled = enabledField != null && (bool)enabledField.GetValue(mod);
                                if (isDead)
                                {
                                    _cachedDeadCount++;
                                }
                                else if (isEnabled)
                                {
                                    _cachedActiveCount++;
                                    if (wattsField != null)
                                    {
                                        _cachedTotalWatts += Convert.ToSingle(wattsField.GetValue(mod));
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 容错降级
                }
            }
        }

        private static object GetPrimaryAvionicsModule()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.parts == null || _moduleAvionicsType == null) return null;

            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    PartModule mod = p.Modules[m];
                    if (mod != null && _moduleAvionicsType.IsAssignableFrom(mod.GetType()))
                    {
                        return mod;
                    }
                }
            }
            return null;
        }

        private static object GetPrimaryProcAvionicsModule()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.parts == null || _moduleProcAvionicsType == null) return null;

            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    PartModule mod = p.Modules[m];
                    if (mod != null && _moduleProcAvionicsType.IsAssignableFrom(mod.GetType()))
                    {
                        return mod;
                    }
                }
            }
            return null;
        }

        private static void RegisterDynamicRP1Members()
        {
            // 锁控级别数值 (2=解锁/正常, 1=仅轴向, 0=完全失控锁定)
            Traverser.RegisterManualMember("LOCK_LEVEL", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedLockLevel;
            }, "RP-1 航电锁控状态码 (2=正常控制, 1=仅轴向, 0=失控锁定)", "RP1:LOCK_LEVEL");

            Traverser.RegisterManualMember("IS_LOCKED", () =>
            {
                UpdateVesselAvionicsCache();
                return _cachedLockLevel == 0 ? 1.0 : 0.0;
            }, "RP-1 是否完全失控锁定 (1=是, 0=否)", "RP1:IS_LOCKED");

            Traverser.RegisterManualMember("IS_AXIAL", () =>
            {
                UpdateVesselAvionicsCache();
                return _cachedLockLevel == 1 ? 1.0 : 0.0;
            }, "RP-1 是否处于轴向控制状态 (1=是, 0=否)", "RP1:IS_AXIAL");

            Traverser.RegisterManualMember("IS_UNLOCKED", () =>
            {
                UpdateVesselAvionicsCache();
                return _cachedLockLevel == 2 ? 1.0 : 0.0;
            }, "RP-1 是否处于完全控制状态 (1=是, 0=否)", "RP1:IS_UNLOCKED");

            // 吨位限制与质量 (t)
            Traverser.RegisterManualMember("CONTROLLABLE_MASS", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedMaxMass;
            }, "RP-1 航电最大支持控制吨位 (t)", "RP1:CONTROLLABLE_MASS / RP1:MAX_MASS");

            Traverser.RegisterManualMember("MAX_MASS", () =>
            {
                double val;
                return Traverser.TryResolveNumeric("CONTROLLABLE_MASS", out val) ? val : 0.0;
            }, "RP-1 航电最大控制吨位别名", "RP1:MAX_MASS");

            Traverser.RegisterManualMember("VESSEL_MASS", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedVesselMass;
            }, "RP-1 航电计算载具总质量 (t)", "RP1:VESSEL_MASS");

            Traverser.RegisterManualMember("MASS_MARGIN", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)(_cachedMaxMass - _cachedVesselMass);
            }, "RP-1 航电控制吨位余量 (MaxMass - VesselMass, t)", "RP1:MASS_MARGIN");

            Traverser.RegisterManualMember("MASS_RATIO", () =>
            {
                UpdateVesselAvionicsCache();
                if (_cachedMaxMass <= 0.0001f) return 999.0;
                return (double)(_cachedVesselMass / _cachedMaxMass);
            }, "RP-1 航电质量占比 (VesselMass / MaxMass)", "RP1:MASS_RATIO");

            Traverser.RegisterManualMember("INTERPLANETARY_LOCKED", () =>
            {
                UpdateVesselAvionicsCache();
                return _cachedInterplanetaryLocked ? 1.0 : 0.0;
            }, "RP-1 是否因近地航电进入深空而锁定 (1=是, 0=否)", "RP1:INTERPLANETARY_LOCKED");

            // 航电功耗与模块统计
            Traverser.RegisterManualMember("POWER_DRAW", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedTotalWatts;
            }, "RP-1 航电总耗电功率 (W)", "RP1:POWER_DRAW / RP1:WATTS");

            Traverser.RegisterManualMember("WATTS", () =>
            {
                double val;
                return Traverser.TryResolveNumeric("POWER_DRAW", out val) ? val : 0.0;
            }, "RP-1 航电耗电别名 (W)", "RP1:WATTS");

            Traverser.RegisterManualMember("AVIONICS_COUNT", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedAvionicsCount;
            }, "RP-1 载具航电模块总数", "RP1:AVIONICS_COUNT");

            Traverser.RegisterManualMember("ACTIVE_COUNT", () =>
            {
                UpdateVesselAvionicsCache();
                return (double)_cachedActiveCount;
            }, "RP-1 载具正常工作的航电模块数", "RP1:ACTIVE_COUNT");

            // 状态字符串
            Traverser.RegisterManualStringMember("STATUS_TEXT", () =>
            {
                UpdateVesselAvionicsCache();
                if (_cachedAvionicsCount == 0) return "NO AVIONICS";
                if (_cachedInterplanetaryLocked) return "INTERPLANETARY LOCK";
                switch (_cachedLockLevel)
                {
                    case 0: return "INSUFFICIENT (LOCKED)";
                    case 1: return "AXIAL ONLY";
                    case 2: return "UNLOCKED";
                    default: return "UNKNOWN";
                }
            }, "RP-1 航电控制状态文本", "RP1:STATUS_TEXT / RP1:STATUS");

            Traverser.RegisterManualStringMember("STATUS", () =>
            {
                string text;
                return Traverser.TryResolveString("STATUS_TEXT", out text) ? text : "UNKNOWN";
            }, "RP-1 航电状态别名", "RP1:STATUS");
        }

        private static void SafeLog(string msg)
        {
            try { Debug.Log(msg); } catch { }
        }
    }
}
