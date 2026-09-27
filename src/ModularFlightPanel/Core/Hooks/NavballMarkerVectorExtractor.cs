using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 导航球标线类型枚举（消除热循环字符串分配）
    /// </summary>
    public enum NavballMarkerType
    {
        Unknown = 0,
        Prograde,
        Retrograde,
        VelocityVector,
        AntiVelocityVector,
        Normal,
        AntiNormal,
        RadialIn,
        RadialOut,
        Target,
        AntiTarget,
        Maneuver
    }

    /// <summary>
    /// 原版与开普勒轨道标线矢量解算器 (NavBall Marker Vector Extractor)
    /// 专责提取 Prograde, Retrograde, Normal, Radial, Maneuver, Target 及对偶矢量的三维视口投影。
    /// 深度集成 CacheManager 同帧轨道三联基快照与姿态缓存，实现 0 B/frame GC 与高频热循环零重复求解。
    /// </summary>
    public static class NavballMarkerVectorExtractor
    {
        private static NavBallBurnVector _cachedBurnVector;
        private static Transform _cachedManeuverTransform;
        private static float _lastBurnVectorSearchTime = -10f;

        // 同帧姿态总变换缓存 (消除同一帧 12~14 个标线重复提取 Camera 与逆变换计算)
        private static Quaternion _cachedTotalMarkerRot = Quaternion.identity;
        private static int _cachedTotalMarkerRotFrame = -1;

        // 静态类型映射表（零 GC 分配）
        private static readonly Dictionary<string, NavballMarkerType> _markerKeyToType = new Dictionary<string, NavballMarkerType>(StringComparer.OrdinalIgnoreCase)
        {
            { "prograde", NavballMarkerType.Prograde },
            { "retrograde", NavballMarkerType.Retrograde },
            { "velocity_vector", NavballMarkerType.VelocityVector },
            { "anti_velocity_vector", NavballMarkerType.AntiVelocityVector },
            { "normal", NavballMarkerType.Normal },
            { "antinormal", NavballMarkerType.AntiNormal },
            { "radialin", NavballMarkerType.RadialIn },
            { "radialout", NavballMarkerType.RadialOut },
            { "target", NavballMarkerType.Target },
            { "antitarget", NavballMarkerType.AntiTarget },
            { "maneuver", NavballMarkerType.Maneuver }
        };

        public static NavballMarkerType GetMarkerType(string markerKey)
        {
            if (!string.IsNullOrEmpty(markerKey) && _markerKeyToType.TryGetValue(markerKey, out var type))
            {
                return type;
            }
            return NavballMarkerType.Unknown;
        }

        public static void InvalidateCaches()
        {
            _cachedBurnVector = null;
            _cachedManeuverTransform = null;
            _cachedTotalMarkerRotFrame = -1;
        }

        public static bool GetMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;
            if (string.IsNullOrEmpty(markerKey)) return false;

            NavballMarkerType markerType = GetMarkerType(markerKey);
            StockNavBallHook.PulseAttitudeConsumerHeartbeat();
            if (markerType == NavballMarkerType.Maneuver)
            {
                StockNavBallHook.PulseManeuverConsumerHeartbeat();
            }

            if (markerType == NavballMarkerType.VelocityVector || markerType == NavballMarkerType.AntiVelocityVector)
            {
                if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface)
                {
                    return false;
                }
            }

            // 1. 直接采用开普勒/轨道/Principia 高精度数学权威解算（0 帧延迟、0 依赖原生 Transform 竞态、与着色器姿态四元数 100% 同源）
            if (CalculateMarkerDirectionMath(markerType, markerKey, out dir, out isVisible))
            {
                return true;
            }

            // 2. 仅对未收录的自定义外置标线尝试从原生 Transform 提取兜底
            if (StockNavBallHook.HasStockNavBall && StockNavBallHook.StockInstance != null)
            {
                Transform marker = GetMarkerTransformByType(markerType);
                if (marker != null && marker.gameObject.activeSelf)
                {
                    Vector3 localPos = marker.localPosition;
                    if (localPos.sqrMagnitude > 0.0001f)
                    {
                        dir = localPos.normalized;
                        isVisible = true;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsMarkerLogicallyActive(string markerKey, Transform marker)
        {
            if (marker != null && marker.gameObject.activeSelf) return true;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return false;

            // 官方权威逻辑：EVA 状态且着陆或贴近地表时，隐藏所有速度与机动标线
            if (v.isEVA && (v.LandedOrSplashed || (v.heightFromTerrain >= 0f && v.heightFromTerrain <= 1f)))
            {
                return false;
            }

            NavballMarkerType keyType = GetMarkerType(markerKey);
            switch (keyType)
            {
                case NavballMarkerType.Prograde:
                case NavballMarkerType.Retrograde:
                    return FlightGlobals.ship_srfVelocity.sqrMagnitude >= 0.01 || FlightGlobals.ship_obtVelocity.sqrMagnitude >= 0.01;

                case NavballMarkerType.VelocityVector:
                case NavballMarkerType.AntiVelocityVector:
                    if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface) return false;
                    return FlightGlobals.ship_srfVelocity.sqrMagnitude >= 4.0;

                case NavballMarkerType.Normal:
                case NavballMarkerType.AntiNormal:
                case NavballMarkerType.RadialIn:
                case NavballMarkerType.RadialOut:
                    // 官方权威逻辑：仅在 Orbit 轨道模式（或 Principia 处于有效轨道参考系）时激活轨道法向与径向指示器
                    if (!PrincipiaProbe.IsAvailable && FlightGlobals.speedDisplayMode != FlightGlobals.SpeedDisplayModes.Orbit)
                    {
                        return false;
                    }
                    return v.orbit != null && v.orbit.vel.sqrMagnitude > 0.01;

                case NavballMarkerType.Target:
                case NavballMarkerType.AntiTarget:
                    return FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null;

                case NavballMarkerType.Maneuver:
                    return (v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                        || (_cachedBurnVector != null && _cachedBurnVector.gameObject.activeSelf);

                default:
                    return marker != null && marker.gameObject.activeSelf;
            }
        }

        public static Transform GetOppositeMarkerTransformByKey(string markerKey)
        {
            if (!StockNavBallHook.HasStockNavBall) return null;
            var inst = StockNavBallHook.StockInstance;
            if (inst == null) return null;
            switch (GetMarkerType(markerKey))
            {
                case NavballMarkerType.Prograde: return inst.retrogradeVector;
                case NavballMarkerType.Retrograde: return inst.progradeVector;
                case NavballMarkerType.Normal: return inst.antiNormalVector;
                case NavballMarkerType.AntiNormal: return inst.normalVector;
                case NavballMarkerType.RadialIn: return inst.radialOutVector;
                case NavballMarkerType.RadialOut: return inst.radialInVector;
                case NavballMarkerType.Target: return inst.retrogradeWaypoint;
                case NavballMarkerType.AntiTarget: return inst.progradeWaypoint;
                default: return null;
            }
        }

        public static Transform GetMarkerTransformByType(NavballMarkerType markerType)
        {
            if (!StockNavBallHook.HasStockNavBall) return null;
            var inst = StockNavBallHook.StockInstance;
            if (inst == null) return null;
            switch (markerType)
            {
                case NavballMarkerType.Prograde: return inst.progradeVector;
                case NavballMarkerType.Retrograde: return inst.retrogradeVector;
                case NavballMarkerType.Normal: return inst.normalVector;
                case NavballMarkerType.AntiNormal: return inst.antiNormalVector;
                case NavballMarkerType.RadialIn: return inst.radialInVector;
                case NavballMarkerType.RadialOut: return inst.radialOutVector;
                case NavballMarkerType.Target: return inst.progradeWaypoint;
                case NavballMarkerType.AntiTarget: return inst.retrogradeWaypoint;
                case NavballMarkerType.Maneuver: return GetManeuverTransform();
                default: return null;
            }
        }

        public static Transform GetMarkerTransformByKey(string markerKey)
        {
            return GetMarkerTransformByType(GetMarkerType(markerKey));
        }

        private static Quaternion GetTotalMarkerRotation(Vessel vessel, Transform refTransform)
        {
            int currentFrame = Time.frameCount;
            if (_cachedTotalMarkerRotFrame == currentFrame && currentFrame != 0)
            {
                return _cachedTotalMarkerRot;
            }

            Quaternion attitudeGymbal;
            if (vessel.isEVA && !MapView.MapIsEnabled && FlightCamera.fetch != null)
            {
                attitudeGymbal = Quaternion.Inverse(FlightCamera.fetch.getReferenceFrame() * Quaternion.AngleAxis(FlightCamera.fetch.camHdg * 57.29578f, Vector3.up) * Quaternion.AngleAxis(FlightCamera.fetch.camPitch * 57.29578f, Vector3.right));
            }
            else
            {
                attitudeGymbal = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Inverse(refTransform.rotation);
            }

            Camera uiCam = StockNavBallHook.GetNavBallCamera();
            Quaternion camRot = (uiCam != null) ? uiCam.transform.rotation : Quaternion.identity;
            _cachedTotalMarkerRot = (camRot != Quaternion.identity)
                ? (Quaternion.Inverse(camRot) * attitudeGymbal)
                : attitudeGymbal;
            _cachedTotalMarkerRotFrame = currentFrame;
            return _cachedTotalMarkerRot;
        }

        private static void ComputeAndCacheOrbitalTriad(Vessel vessel, int currentFrame, out CacheManager.NavballFrameSnapshot snapshot)
        {
            snapshot = new CacheManager.NavballFrameSnapshot
            {
                Frame = currentFrame
            };

            if (vessel.orbit != null && vessel.mainBody != null)
            {
                Vector3 obtVel = (Vector3)vessel.orbit.GetVel();
                if (obtVel.sqrMagnitude > 0.0001f)
                {
                    // 核心优化：同一物理/渲染帧内仅调用一次 vessel.CurrentCoM，避免对全舰零件重复进行质心加权求和
                    Vector3 wCoM = vessel.CurrentCoM;
                    Vector3 cbPos = vessel.mainBody.position;
                    Vector3 rad = Vector3.ProjectOnPlane((wCoM - cbPos).normalized, obtVel).normalized;
                    if (rad.sqrMagnitude > 0.0001f)
                    {
                        Vector3 n = Vector3.Cross(rad, obtVel.normalized).normalized;
                        if (n.sqrMagnitude > 0.0001f)
                        {
                            snapshot.HasOrbit = true;
                            snapshot.Normal = -n;
                            snapshot.AntiNormal = n;
                            snapshot.RadialOut = rad;
                            snapshot.RadialIn = -rad;
                        }
                    }
                }
            }

            if (CacheManager.Instance != null)
            {
                CacheManager.Instance.SetCachedNavballSnapshot(ref snapshot);
            }
        }

        public static bool CalculateMarkerDirectionMath(string markerKey, out Vector3 dir, out bool isVisible)
        {
            return CalculateMarkerDirectionMath(GetMarkerType(markerKey), markerKey, out dir, out isVisible);
        }

        public static bool CalculateMarkerDirectionMath(NavballMarkerType markerType, string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return CalculateSimulatedMarkerDirection(markerType, out dir, out isVisible);
            }

            // 官方权威逻辑：EVA 状态且着陆或贴近地表时，隐藏所有速度与机动标线
            if (vessel.isEVA && (vessel.LandedOrSplashed || (vessel.heightFromTerrain >= 0f && vessel.heightFromTerrain <= 1f)))
            {
                return false;
            }

            Transform refTransform = vessel.ReferenceTransform ?? vessel.transform;
            if (refTransform == null) return false;

            Vector3 worldVec = Vector3.zero;
            bool hasValidVector = false;

            int currentFrame = Time.frameCount;
            CacheManager.NavballFrameSnapshot snapshot = default;
            bool hasSnapshot = CacheManager.Instance != null && CacheManager.Instance.TryGetCachedNavballSnapshot(currentFrame, out snapshot);

            switch (markerType)
            {
                case NavballMarkerType.Prograde:
                case NavballMarkerType.Retrograde:
                {
                    Vector3d vel = Vector3d.zero;
                    // 若 Principia 处于活动态且有有效 Frenet 切向矢量，优先采信 Principia 绘制参考系
                    if (PrincipiaProbe.IsAvailable && PrincipiaProbe.GetVesselFrenetTrihedron(out var tang, out _, out _))
                    {
                        if (tang.sqrMagnitude > 0.001)
                        {
                            vel = tang;
                        }
                    }

                    if (vel.sqrMagnitude <= 0.001)
                    {
                        // 100% 对齐原版 NavBall.Update 的 displayVelocity 物理速度源
                        switch (FlightGlobals.speedDisplayMode)
                        {
                            case FlightGlobals.SpeedDisplayModes.Surface:
                                vel = FlightGlobals.ship_srfVelocity;
                                break;
                            case FlightGlobals.SpeedDisplayModes.Orbit:
                                vel = FlightGlobals.ship_obtVelocity;
                                break;
                            case FlightGlobals.SpeedDisplayModes.Target:
                                vel = FlightGlobals.ship_tgtVelocity;
                                break;
                        }
                    }

                    if (vel.sqrMagnitude > 0.01)
                    {
                        worldVec = (markerType == NavballMarkerType.Prograde) ? (Vector3)vel.normalized : -(Vector3)vel.normalized;
                        hasValidVector = true;
                    }
                    break;
                }

                case NavballMarkerType.VelocityVector:
                case NavballMarkerType.AntiVelocityVector:
                {
                    if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface)
                    {
                        return false;
                    }

                    Vector3d srfVel = FlightGlobals.ship_srfVelocity;
                    if (srfVel.sqrMagnitude < 4.0)
                    {
                        return false;
                    }

                    Vector3d refVel = Vector3d.zero;
                    switch (FlightGlobals.speedDisplayMode)
                    {
                        case FlightGlobals.SpeedDisplayModes.Orbit:
                            refVel = FlightGlobals.ship_obtVelocity;
                            break;
                        case FlightGlobals.SpeedDisplayModes.Target:
                            refVel = FlightGlobals.ship_tgtVelocity;
                            break;
                    }

                    if (refVel.sqrMagnitude > 0.01)
                    {
                        double dot = Vector3d.Dot(srfVel.normalized, refVel.normalized);
                        if (dot >= 0.9990)
                        {
                            return false;
                        }
                    }

                    worldVec = (markerType == NavballMarkerType.VelocityVector) ? (Vector3)srfVel.normalized : -(Vector3)srfVel.normalized;
                    hasValidVector = true;
                    break;
                }

                case NavballMarkerType.Normal:
                case NavballMarkerType.AntiNormal:
                {
                    // 官方权威逻辑：仅在 Orbit 模式或 Principia 存在时显示轨道法向标线
                    if (!PrincipiaProbe.IsAvailable && FlightGlobals.speedDisplayMode != FlightGlobals.SpeedDisplayModes.Orbit)
                    {
                        return false;
                    }

                    if (PrincipiaProbe.IsAvailable && PrincipiaProbe.GetVesselFrenetTrihedron(out _, out _, out var pBinorm))
                    {
                        if (pBinorm.sqrMagnitude > 0.001)
                        {
                            worldVec = (markerType == NavballMarkerType.Normal) ? (Vector3)pBinorm.normalized : -(Vector3)pBinorm.normalized;
                            hasValidVector = true;
                        }
                    }

                    if (!hasValidVector)
                    {
                        if (!hasSnapshot)
                        {
                            ComputeAndCacheOrbitalTriad(vessel, currentFrame, out snapshot);
                            hasSnapshot = true;
                        }
                        if (snapshot.HasOrbit)
                        {
                            worldVec = (markerType == NavballMarkerType.Normal) ? snapshot.Normal : snapshot.AntiNormal;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case NavballMarkerType.RadialIn:
                case NavballMarkerType.RadialOut:
                {
                    // 官方权威逻辑：仅在 Orbit 模式或 Principia 存在时显示轨道径向标线
                    if (!PrincipiaProbe.IsAvailable && FlightGlobals.speedDisplayMode != FlightGlobals.SpeedDisplayModes.Orbit)
                    {
                        return false;
                    }

                    if (PrincipiaProbe.IsAvailable && PrincipiaProbe.GetVesselFrenetTrihedron(out _, out var pNorm, out _))
                    {
                        if (pNorm.sqrMagnitude > 0.001)
                        {
                            // Principia Normal 指向曲率中心即径向内 (Radial In)
                            worldVec = (markerType == NavballMarkerType.RadialIn) ? (Vector3)pNorm.normalized : -(Vector3)pNorm.normalized;
                            hasValidVector = true;
                        }
                    }

                    if (!hasValidVector)
                    {
                        if (!hasSnapshot)
                        {
                            ComputeAndCacheOrbitalTriad(vessel, currentFrame, out snapshot);
                            hasSnapshot = true;
                        }
                        if (snapshot.HasOrbit)
                        {
                            worldVec = (markerType == NavballMarkerType.RadialOut) ? snapshot.RadialOut : snapshot.RadialIn;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case NavballMarkerType.Target:
                case NavballMarkerType.AntiTarget:
                {
                    if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                    {
                        Vector3 targetDir = FlightGlobals.fetch.vesselTargetDirection;
                        if (targetDir.sqrMagnitude < 0.0001f)
                        {
                            Transform tgtTrans = FlightGlobals.fetch.vesselTargetTransform ?? FlightGlobals.fetch.VesselTarget.GetTransform();
                            if (tgtTrans != null)
                            {
                                targetDir = (tgtTrans.position - vessel.transform.position).normalized;
                            }
                        }
                        if (targetDir.sqrMagnitude > 0.0001f)
                        {
                            worldVec = (markerType == NavballMarkerType.Target) ? targetDir : -targetDir;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case NavballMarkerType.Maneuver:
                {
                    if (vessel.patchedConicSolver != null && vessel.patchedConicSolver.maneuverNodes != null && vessel.patchedConicSolver.maneuverNodes.Count > 0)
                    {
                        var node = vessel.patchedConicSolver.maneuverNodes[0];
                        if (node != null)
                        {
                            Vector3 burnVec = (Vector3)node.GetBurnVector(node.patch ?? vessel.orbit);
                            if (burnVec.sqrMagnitude > 0.001f)
                            {
                                worldVec = burnVec.normalized;
                                hasValidVector = true;
                            }
                        }
                    }
                    else if (PrincipiaProbe.IsAvailable && PrincipiaProbe.TryGetManeuverVector(out double p, out double n, out double r))
                    {
                        if (p * p + n * n + r * r > 0.001)
                        {
                            if (PrincipiaProbe.GetVesselFrenetTrihedron(out var tang, out var norm, out var binorm))
                            {
                                Vector3d totalVec = tang * p + binorm * n + norm * r;
                                if (totalVec.sqrMagnitude > 0.001)
                                {
                                    worldVec = (Vector3)totalVec.normalized;
                                    hasValidVector = true;
                                }
                            }
                            else if (vessel.orbit != null)
                            {
                                Vector3d pos = vessel.orbit.pos;
                                Vector3d vel = vessel.orbit.vel;
                                Vector3d normV = Vector3d.Cross(pos, vel);
                                if (vel.sqrMagnitude > 0.0001 && normV.sqrMagnitude > 0.0001)
                                {
                                    Vector3d rad = Vector3d.Cross(vel, normV);
                                    Vector3d totalVec = vel.normalized * p + normV.normalized * n + rad.normalized * r;
                                    worldVec = (Vector3)totalVec.normalized;
                                    hasValidVector = true;
                                }
                            }
                        }
                    }
                    break;
                }
            }

            if (!hasValidVector) return false;

            Quaternion totalRot = GetTotalMarkerRotation(vessel, refTransform);
            Vector3 screenVec = totalRot * worldVec;

            dir = screenVec.normalized;
            isVisible = true;
            return true;
        }

        public static bool CalculateSimulatedMarkerDirection(NavballMarkerType markerType, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            switch (markerType)
            {
                case NavballMarkerType.Prograde:
                    dir = new Vector3(0.04f, 0.18f, 0.98f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.Retrograde:
                    dir = new Vector3(-0.04f, -0.18f, -0.98f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.Normal:
                    dir = new Vector3(0f, 0.94f, 0.34f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.AntiNormal:
                    dir = new Vector3(0f, -0.94f, -0.34f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.RadialIn:
                    dir = new Vector3(-0.92f, 0f, 0.38f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.RadialOut:
                    dir = new Vector3(0.92f, 0f, 0.38f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.Target:
                    dir = new Vector3(0.28f, 0.32f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.AntiTarget:
                    dir = new Vector3(-0.28f, -0.32f, -0.90f).normalized;
                    isVisible = true;
                    return true;
                case NavballMarkerType.Maneuver:
                    dir = new Vector3(-0.24f, 0.36f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                default:
                    return false;
            }
        }

        public static bool CalculateSimulatedMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            return CalculateSimulatedMarkerDirection(GetMarkerType(markerKey), out dir, out isVisible);
        }

        public static Transform GetManeuverTransform()
        {
            if (_cachedManeuverTransform != null) return _cachedManeuverTransform;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.patchedConicSolver == null || v.patchedConicSolver.maneuverNodes == null || v.patchedConicSolver.maneuverNodes.Count == 0)
            {
                return null;
            }

            if (_cachedBurnVector == null && StockNavBallHook.StockInstance != null)
            {
                var parent = StockNavBallHook.StockInstance.transform.parent;
                if (parent != null)
                {
                    _cachedBurnVector = parent.GetComponentInChildren<NavBallBurnVector>(true);
                }
            }

            if (_cachedBurnVector == null)
            {
                float now = Time.unscaledTime;
                if (now - _lastBurnVectorSearchTime > 5.0f)
                {
                    _lastBurnVectorSearchTime = now;
                    _cachedBurnVector = UnityEngine.Object.FindObjectOfType<NavBallBurnVector>();
                }
            }

            if (_cachedBurnVector != null)
            {
                _cachedManeuverTransform = _cachedBurnVector.vectorProgr;
            }
            return _cachedManeuverTransform;
        }
    }
}
