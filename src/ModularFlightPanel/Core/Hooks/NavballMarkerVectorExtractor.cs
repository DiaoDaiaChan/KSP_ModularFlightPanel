using System;
using UnityEngine;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 原版与开普勒轨道标线矢量解算器 (NavBall Marker Vector Extractor)
    /// 专责提取 Prograde, Retrograde, Normal, Radial, Maneuver, Target 及对偶矢量的三维视口投影。
    /// </summary>
    public static class NavballMarkerVectorExtractor
    {
        private static NavBallBurnVector _cachedBurnVector;
        private static Transform _cachedManeuverTransform;
        private static float _lastBurnVectorSearchTime = -10f;

        public static void InvalidateCaches()
        {
            _cachedBurnVector = null;
            _cachedManeuverTransform = null;
        }

        public static bool GetMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;
            if (string.IsNullOrEmpty(markerKey)) return false;

            string lowerKey = markerKey.ToLowerInvariant();
            StockNavBallHook.PulseAttitudeConsumerHeartbeat();
            if (lowerKey.IndexOf("maneuver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                StockNavBallHook.PulseManeuverConsumerHeartbeat();
            }

            if (lowerKey == "velocity_vector" || lowerKey == "anti_velocity_vector")
            {
                if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface)
                {
                    return false;
                }
            }

            // 1. 直接采用开普勒/轨道/Principia 高精度数学权威解算（0 帧延迟、0 依赖原生 Transform 竞态、与着色器姿态四元数 100% 同源）
            if (CalculateMarkerDirectionMath(markerKey, out dir, out isVisible))
            {
                return true;
            }

            // 2. 仅对未收录的自定义外置标线尝试从原生 Transform 提取兜底
            if (StockNavBallHook.HasStockNavBall && StockNavBallHook.StockInstance != null)
            {
                Transform marker = GetMarkerTransformByKey(markerKey);
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

            string key = markerKey.ToLowerInvariant();
            switch (key)
            {
                case "prograde":
                case "retrograde":
                    return v.srf_velocity.sqrMagnitude >= 0.01 || v.obt_velocity.sqrMagnitude >= 0.01;

                case "velocity_vector":
                case "anti_velocity_vector":
                    if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface) return false;
                    return v.srf_velocity.sqrMagnitude >= 4.0;

                case "normal":
                case "antinormal":
                case "radialin":
                case "radialout":
                    return v.orbit != null && v.orbit.vel.sqrMagnitude > 0.01;

                case "target":
                case "antitarget":
                    return FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null;

                case "maneuver":
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
            switch (markerKey.ToLowerInvariant())
            {
                case "prograde": return inst.retrogradeVector;
                case "retrograde": return inst.progradeVector;
                case "normal": return inst.antiNormalVector;
                case "antinormal": return inst.normalVector;
                case "radialin": return inst.radialOutVector;
                case "radialout": return inst.radialInVector;
                case "target": return inst.retrogradeWaypoint;
                case "antitarget": return inst.progradeWaypoint;
                default: return null;
            }
        }

        public static Transform GetMarkerTransformByKey(string markerKey)
        {
            if (!StockNavBallHook.HasStockNavBall) return null;
            var inst = StockNavBallHook.StockInstance;
            switch (markerKey.ToLowerInvariant())
            {
                case "prograde": return inst.progradeVector;
                case "retrograde": return inst.retrogradeVector;
                case "normal": return inst.normalVector;
                case "antinormal": return inst.antiNormalVector;
                case "radialin": return inst.radialInVector;
                case "radialout": return inst.radialOutVector;
                case "target": return inst.progradeWaypoint;
                case "antitarget": return inst.retrogradeWaypoint;
                case "maneuver": return GetManeuverTransform();
                default: return null;
            }
        }

        public static bool CalculateMarkerDirectionMath(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return CalculateSimulatedMarkerDirection(markerKey, out dir, out isVisible);
            }

            Transform refTransform = vessel.ReferenceTransform ?? vessel.transform;
            if (refTransform == null) return false;

            Vector3 worldVec = Vector3.zero;
            bool hasValidVector = false;

            string key = markerKey.ToLowerInvariant();
            switch (key)
            {
                case "prograde":
                case "retrograde":
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
                        switch (FlightGlobals.speedDisplayMode)
                        {
                            case FlightGlobals.SpeedDisplayModes.Surface:
                                vel = vessel.srf_velocity;
                                break;
                            case FlightGlobals.SpeedDisplayModes.Orbit:
                                vel = vessel.obt_velocity;
                                break;
                            case FlightGlobals.SpeedDisplayModes.Target:
                                if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                                {
                                    vel = vessel.obt_velocity - FlightGlobals.fetch.VesselTarget.GetObtVelocity();
                                }
                                else
                                {
                                    vel = vessel.srf_velocity;
                                }
                                break;
                        }
                    }

                    if (vel.sqrMagnitude > 0.01)
                    {
                        worldVec = (key == "prograde") ? (Vector3)vel.normalized : -(Vector3)vel.normalized;
                        hasValidVector = true;
                    }
                    break;
                }

                case "velocity_vector":
                case "anti_velocity_vector":
                {
                    if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Surface)
                    {
                        return false;
                    }

                    Vector3d srfVel = vessel.srf_velocity;
                    if (srfVel.sqrMagnitude < 4.0)
                    {
                        return false;
                    }

                    Vector3d refVel = Vector3d.zero;
                    switch (FlightGlobals.speedDisplayMode)
                    {
                        case FlightGlobals.SpeedDisplayModes.Orbit:
                            refVel = vessel.obt_velocity;
                            break;
                        case FlightGlobals.SpeedDisplayModes.Target:
                            if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                                refVel = vessel.obt_velocity - FlightGlobals.fetch.VesselTarget.GetObtVelocity();
                            else
                                refVel = vessel.srf_velocity;
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

                    worldVec = (key == "velocity_vector") ? (Vector3)srfVel.normalized : -(Vector3)srfVel.normalized;
                    hasValidVector = true;
                    break;
                }

                case "normal":
                case "antinormal":
                {
                    if (PrincipiaProbe.IsAvailable && PrincipiaProbe.GetVesselFrenetTrihedron(out _, out _, out var pBinorm))
                    {
                        if (pBinorm.sqrMagnitude > 0.001)
                        {
                            worldVec = (key == "normal") ? (Vector3)pBinorm.normalized : -(Vector3)pBinorm.normalized;
                            hasValidVector = true;
                        }
                    }

                    if (!hasValidVector && vessel.orbit != null && vessel.mainBody != null)
                    {
                        Vector3 wCoM = vessel.CurrentCoM;
                        Vector3 cbPos = vessel.mainBody.position;
                        Vector3 obtVel = (Vector3)vessel.orbit.GetVel();
                        if (obtVel.sqrMagnitude > 0.0001f)
                        {
                            Vector3 rad = Vector3.ProjectOnPlane((wCoM - cbPos).normalized, obtVel).normalized;
                            Vector3 n = Vector3.Cross(rad, obtVel.normalized).normalized;
                            if (n.sqrMagnitude > 0.0001f)
                            {
                                worldVec = (key == "normal") ? -n : n;
                                hasValidVector = true;
                            }
                        }
                    }
                    break;
                }

                case "radialin":
                case "radialout":
                {
                    if (PrincipiaProbe.IsAvailable && PrincipiaProbe.GetVesselFrenetTrihedron(out _, out var pNorm, out _))
                    {
                        if (pNorm.sqrMagnitude > 0.001)
                        {
                            // Principia Normal 指向曲率中心即径向内 (Radial In)
                            worldVec = (key == "radialin") ? (Vector3)pNorm.normalized : -(Vector3)pNorm.normalized;
                            hasValidVector = true;
                        }
                    }

                    if (!hasValidVector && vessel.orbit != null && vessel.mainBody != null)
                    {
                        Vector3 wCoM = vessel.CurrentCoM;
                        Vector3 cbPos = vessel.mainBody.position;
                        Vector3 obtVel = (Vector3)vessel.orbit.GetVel();
                        if (obtVel.sqrMagnitude > 0.0001f)
                        {
                            Vector3 rad = Vector3.ProjectOnPlane((wCoM - cbPos).normalized, obtVel).normalized;
                            if (rad.sqrMagnitude > 0.0001f)
                            {
                                worldVec = (key == "radialout") ? rad : -rad;
                                hasValidVector = true;
                            }
                        }
                    }
                    break;
                }

                case "target":
                case "antitarget":
                {
                    if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                    {
                        Vector3 targetDir = FlightGlobals.fetch.vesselTargetDirection;
                        if (targetDir.sqrMagnitude < 0.001f)
                        {
                            targetDir = (FlightGlobals.fetch.VesselTarget.GetTransform().position - vessel.transform.position).normalized;
                        }
                        if (targetDir.sqrMagnitude > 0.001f)
                        {
                            worldVec = (key == "target") ? targetDir : -targetDir;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case "maneuver":
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
            Vector3 screenVec = (camRot != Quaternion.identity)
                ? (Quaternion.Inverse(camRot) * (attitudeGymbal * worldVec))
                : (attitudeGymbal * worldVec);

            dir = screenVec.normalized;
            isVisible = true;
            return true;
        }

        public static bool CalculateSimulatedMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            switch (markerKey.ToLowerInvariant())
            {
                case "prograde":
                    dir = new Vector3(0.04f, 0.18f, 0.98f).normalized;
                    isVisible = true;
                    return true;
                case "retrograde":
                    dir = new Vector3(-0.04f, -0.18f, -0.98f).normalized;
                    isVisible = true;
                    return true;
                case "normal":
                    dir = new Vector3(0f, 0.94f, 0.34f).normalized;
                    isVisible = true;
                    return true;
                case "antinormal":
                    dir = new Vector3(0f, -0.94f, -0.34f).normalized;
                    isVisible = true;
                    return true;
                case "radialin":
                    dir = new Vector3(-0.92f, 0f, 0.38f).normalized;
                    isVisible = true;
                    return true;
                case "radialout":
                    dir = new Vector3(0.92f, 0f, 0.38f).normalized;
                    isVisible = true;
                    return true;
                case "target":
                    dir = new Vector3(0.28f, 0.32f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                case "antitarget":
                    dir = new Vector3(-0.28f, -0.32f, -0.90f).normalized;
                    isVisible = true;
                    return true;
                case "maneuver":
                    dir = new Vector3(-0.24f, 0.36f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                default:
                    return false;
            }
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
