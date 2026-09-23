using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Trajectories 软依赖遥测反射探针
    /// 遍历 Trajectories.API 提供的所有落点、再入预测与大气减速轨道参数，零硬编码。
    /// </summary>
    public static class TrajectoriesProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("TRAJ");

        private static Type _apiType;
        private static MethodInfo _getTimeTillImpactMethod;
        private static MethodInfo _getImpactPositionMethod;
        private static MethodInfo _getImpactVelocityMethod;
        private static MethodInfo _getSpaceOrbitMethod;
        private static MethodInfo _hasTargetMethod;
        private static MethodInfo _getTargetMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly trajAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("Trajectories", StringComparison.OrdinalIgnoreCase))
                    {
                        trajAssembly = asm;
                        break;
                    }
                }

                if (trajAssembly != null)
                {
                    _apiType = trajAssembly.GetType("Trajectories.API");
                    if (_apiType != null)
                    {
                        // 1. 静态反射遍历 API 类型公开属性与无参方法
                        Traverser.TraverseStatic(_apiType, "Trajectories 核心接口 (API)");

                        _getTimeTillImpactMethod = _apiType.GetMethod("GetTimeTillImpact", BindingFlags.Public | BindingFlags.Static);
                        _getImpactPositionMethod = _apiType.GetMethod("GetImpactPosition", BindingFlags.Public | BindingFlags.Static);
                        _getImpactVelocityMethod = _apiType.GetMethod("GetImpactVelocity", BindingFlags.Public | BindingFlags.Static);
                        _getSpaceOrbitMethod = _apiType.GetMethod("GetSpaceOrbit", BindingFlags.Public | BindingFlags.Static);
                        _hasTargetMethod = _apiType.GetMethod("HasTarget", BindingFlags.Public | BindingFlags.Static);
                        _getTargetMethod = _apiType.GetMethod("GetTarget", BindingFlags.Public | BindingFlags.Static);

                        // 2. 注册高阶通用计算项与别名
                        RegisterDynamicTrajectoriesMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] TrajectoriesProbe successfully hooked Trajectories! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] Trajectories probe initialization warning: {ex.Message}");
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

        private static Vector3? GetImpactPos()
        {
            if (_getImpactPositionMethod == null) return null;
            try
            {
                object res = _getImpactPositionMethod.Invoke(null, null);
                if (res != null) return (Vector3?)res;
            }
            catch { }
            return null;
        }

        private static void RegisterDynamicTrajectoriesMembers()
        {
            // 撞击/接地倒计时 (Time Till Impact)
            Traverser.RegisterCustom("TimeToImpact", typeof(double), () =>
            {
                if (_getTimeTillImpactMethod != null)
                {
                    try
                    {
                        object res = _getTimeTillImpactMethod.Invoke(null, null);
                        if (res != null)
                        {
                            double? val = (double?)res;
                            if (val.HasValue) return val.Value;
                        }
                    }
                    catch { }
                }
                return double.NaN;
            }, "Trajectories 落点预测", "距离地表碰撞或接地的预计倒计时时长 (s)", new[] { "TIMETOIMPACT", "IMPACTTIME", "TTI" });

            // 预测落点纬度 (ImpactLatitude)
            Traverser.RegisterCustom("ImpactLatitude", typeof(double), () =>
            {
                Vector3? pos = GetImpactPos();
                Vessel v = FlightGlobals.ActiveVessel;
                if (pos.HasValue && v != null && v.mainBody != null)
                {
                    v.mainBody.GetLatLonAlt(pos.Value, out double lat, out double lon, out double alt);
                    return lat;
                }
                return double.NaN;
            }, "Trajectories 落点预测", "预计着陆/碰撞点的地表纬度 (deg)", new[] { "IMPACTLAT", "LATITUDE" });

            // 预测落点经度 (ImpactLongitude)
            Traverser.RegisterCustom("ImpactLongitude", typeof(double), () =>
            {
                Vector3? pos = GetImpactPos();
                Vessel v = FlightGlobals.ActiveVessel;
                if (pos.HasValue && v != null && v.mainBody != null)
                {
                    v.mainBody.GetLatLonAlt(pos.Value, out double lat, out double lon, out double alt);
                    return lon;
                }
                return double.NaN;
            }, "Trajectories 落点预测", "预计着陆/碰撞点的地表经度 (deg)", new[] { "IMPACTLON", "LONGITUDE" });

            // 预测落点海拔高度 (ImpactAltitude)
            Traverser.RegisterCustom("ImpactAltitude", typeof(double), () =>
            {
                Vector3? pos = GetImpactPos();
                Vessel v = FlightGlobals.ActiveVessel;
                if (pos.HasValue && v != null && v.mainBody != null)
                {
                    v.mainBody.GetLatLonAlt(pos.Value, out double lat, out double lon, out double alt);
                    return alt;
                }
                return double.NaN;
            }, "Trajectories 落点预测", "预计着陆/碰撞点的地形海拔高度 (m)", new[] { "IMPACTALT", "ALTITUDE" });

            // 目标点距离偏差 (TargetDistance)
            Traverser.RegisterCustom("TargetDistance", typeof(double), () =>
            {
                if (_hasTargetMethod != null && _getTargetMethod != null)
                {
                    try
                    {
                        bool hasTgt = (bool)_hasTargetMethod.Invoke(null, null);
                        if (hasTgt)
                        {
                            object tgtObj = _getTargetMethod.Invoke(null, null);
                            Vector3? impactPos = GetImpactPos();
                            if (tgtObj != null && impactPos.HasValue)
                            {
                                Vector3d? tgtPos = (Vector3d?)tgtObj;
                                if (tgtPos.HasValue)
                                {
                                    return Vector3d.Distance(tgtPos.Value, impactPos.Value);
                                }
                            }
                        }
                    }
                    catch { }
                }
                return double.NaN;
            }, "Trajectories 落点预测", "预计地表落点与目标着陆点的直线偏差距离 (m)", new[] { "TARGETDISTANCE", "TARGETDIST", "DIST" });

            // 接地速度 (ImpactVelocity)
            Traverser.RegisterCustom("ImpactVelocity", typeof(double), () =>
            {
                if (_getImpactVelocityMethod != null)
                {
                    try
                    {
                        object res = _getImpactVelocityMethod.Invoke(null, null);
                        if (res != null)
                        {
                            Vector3? vel = (Vector3?)res;
                            if (vel.HasValue) return (double)vel.Value.magnitude;
                        }
                    }
                    catch { }
                }
                return double.NaN;
            }, "Trajectories 落点预测", "预计撞击或接地瞬间的合速度大小 (m/s)", new[] { "IMPACTVEL", "IMPACTSPEED" });

            // 气阻修正远地点 (AerobrakeAp)
            Traverser.RegisterCustom("AerobrakeAp", typeof(double), () =>
            {
                if (_getSpaceOrbitMethod != null)
                {
                    try
                    {
                        Orbit orb = _getSpaceOrbitMethod.Invoke(null, null) as Orbit;
                        if (orb != null) return orb.ApA;
                    }
                    catch { }
                }
                return double.NaN;
            }, "Trajectories 气动再入", "穿越大气层气动减速出大气层后的修正远地点高度 (m)", new[] { "AEROAP", "AEROBRAKEAP" });

            // 气阻修正近地点 (AerobrakePe)
            Traverser.RegisterCustom("AerobrakePe", typeof(double), () =>
            {
                if (_getSpaceOrbitMethod != null)
                {
                    try
                    {
                        Orbit orb = _getSpaceOrbitMethod.Invoke(null, null) as Orbit;
                        if (orb != null) return orb.PeA;
                    }
                    catch { }
                }
                return double.NaN;
            }, "Trajectories 气动再入", "穿越大气层气动减速出大气层后的修正近地点高度 (m)", new[] { "AEROPE", "AEROBRAKEPE" });

            // 是否已设置目标 (HasTarget)
            Traverser.RegisterCustom("HasTarget", typeof(bool), () =>
            {
                if (_hasTargetMethod != null)
                {
                    try { return (bool)_hasTargetMethod.Invoke(null, null); } catch { }
                }
                return false;
            }, "Trajectories 落点预测", "是否已在地图上标定着陆目标点", new[] { "HASTARGET" });
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
