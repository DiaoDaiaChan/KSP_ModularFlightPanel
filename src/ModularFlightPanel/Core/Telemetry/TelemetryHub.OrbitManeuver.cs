using System;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    public partial class TelemetryHub
    {
        // -------------------------------------------------------------
        // 轨道动力学 (Orbital Parameters) - 按需惰性解算与同帧去重
        // -------------------------------------------------------------
        private int _orbitFrame = -1;
        private double _apoapsis = 0.0;
        private double _periapsis = 0.0;
        private double _timeToAp = 0.0;
        private double _timeToPe = 0.0;

        public double Apoapsis
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Apoapsis;
                EnsureOrbitUpdated();
                return _apoapsis;
            }
        }

        public double Periapsis
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Periapsis;
                EnsureOrbitUpdated();
                return _periapsis;
            }
        }

        public double TimeToAp
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TimeToAp;
                EnsureOrbitUpdated();
                return _timeToAp;
            }
        }

        public double TimeToPe
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TimeToPe;
                EnsureOrbitUpdated();
                return _timeToPe;
            }
        }

        private void EnsureOrbitUpdated()
        {
            if (_orbitFrame == Time.frameCount) return;
            _orbitFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateOrbitalParameters();
        }

        private void UpdateOrbitalParameters()
        {
            Orbit orbit = ActiveVessel.orbit;
            if (orbit != null)
            {
                _apoapsis = double.IsNaN(orbit.ApA) ? 0.0 : orbit.ApA;
                _periapsis = double.IsNaN(orbit.PeA) ? 0.0 : orbit.PeA;
                _timeToAp = double.IsNaN(orbit.timeToAp) ? 0.0 : orbit.timeToAp;
                _timeToPe = double.IsNaN(orbit.timeToPe) ? 0.0 : orbit.timeToPe;
            }
        }

        // -------------------------------------------------------------
        // 机动节点动力学 (Maneuver Parameters) - 按需惰性解算
        // -------------------------------------------------------------
        private int _maneuverFrame = -1;
        private bool _hasManeuverNode = false;
        private double _maneuverDeltaV = 0.0;
        private double _maneuverTotalDeltaV = 0.0;
        private double _maneuverTimeToNode = 0.0;
        private double _maneuverBurnTime = 0.0;
        private double _maneuverTimeToBurn = 0.0;
        private double _maneuverDeltaVPrograde = 0.0;
        private double _maneuverDeltaVNormal = 0.0;
        private double _maneuverDeltaVRadial = 0.0;
        private string _maneuverSource = "STANDBY";

        public bool HasManeuverNode
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.HasManeuverNode;
                EnsureManeuverUpdated();
                return _hasManeuverNode;
            }
        }

        public double ManeuverDeltaV
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ManeuverDeltaV;
                EnsureManeuverUpdated();
                return _maneuverDeltaV;
            }
        }

        public double ManeuverTotalDeltaV
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ManeuverTotalDeltaV;
                EnsureManeuverUpdated();
                return _maneuverTotalDeltaV;
            }
        }

        public double ManeuverTimeToNode
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ManeuverTimeToNode;
                EnsureManeuverUpdated();
                return _maneuverTimeToNode;
            }
        }

        public double ManeuverBurnTime
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ManeuverBurnTime;
                EnsureManeuverUpdated();
                return _maneuverBurnTime;
            }
        }

        public double ManeuverTimeToBurn
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ManeuverTimeToBurn;
                EnsureManeuverUpdated();
                return _maneuverTimeToBurn;
            }
        }

        public double ManeuverDeltaVPrograde
        {
            get
            {
                if (IsSimulationMode) return 0.0;
                EnsureManeuverUpdated();
                return _maneuverDeltaVPrograde;
            }
        }

        public double ManeuverDeltaVNormal
        {
            get
            {
                if (IsSimulationMode) return 0.0;
                EnsureManeuverUpdated();
                return _maneuverDeltaVNormal;
            }
        }

        public double ManeuverDeltaVRadial
        {
            get
            {
                if (IsSimulationMode) return 0.0;
                EnsureManeuverUpdated();
                return _maneuverDeltaVRadial;
            }
        }

        public string ManeuverSource
        {
            get
            {
                if (IsSimulationMode) return "SIM";
                EnsureManeuverUpdated();
                return _maneuverSource;
            }
        }

        private void EnsureManeuverUpdated()
        {
            if (_maneuverFrame == Time.frameCount) return;
            _maneuverFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateManeuverParameters();
        }

        private void UpdateManeuverParameters()
        {
            try
            {
                Vessel v = ActiveVessel;
                if (v == null)
                {
                    ResetManeuverParameters();
                    return;
                }

                // 1. Principia 飞行计划优先
                if (PrincipiaProbe.IsAvailable && PrincipiaProbe.HasActiveFlightPlan)
                {
                    double pDv = PrincipiaProbe.ManeuverDeltaV;
                    if (!double.IsNaN(pDv) && pDv > 0.001)
                    {
                        _hasManeuverNode = true;
                        _maneuverSource = "PRINCIPIA";
                        _maneuverDeltaV = pDv;
                        _maneuverTotalDeltaV = pDv;
                        double pDur = PrincipiaProbe.ManeuverDuration;
                        _maneuverBurnTime = (double.IsNaN(pDur) || pDur < 0.0) ? 0.0 : pDur;
                        double pTime = PrincipiaProbe.TimeToManeuver;
                        _maneuverTimeToNode = (double.IsNaN(pTime) || pTime < 0.0) ? 0.0 : pTime;
                        _maneuverTimeToBurn = _maneuverTimeToNode;

                        if (PrincipiaProbe.TryGetManeuverVector(out double pro, out double norm, out double rad))
                        {
                            _maneuverDeltaVPrograde = pro;
                            _maneuverDeltaVNormal = norm;
                            _maneuverDeltaVRadial = rad;
                        }
                        else
                        {
                            _maneuverDeltaVPrograde = pDv;
                            _maneuverDeltaVNormal = 0.0;
                            _maneuverDeltaVRadial = 0.0;
                        }
                        return;
                    }
                }

                // 2. 原版 PatchedConicSolver 机动节点
                if (v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    var node = v.patchedConicSolver.maneuverNodes[0];
                    if (node != null)
                    {
                        Vector3d burnVec = node.GetBurnVector(node.patch ?? v.orbit);
                        _hasManeuverNode = true;
                        _maneuverSource = "STOCK";
                        _maneuverDeltaV = double.IsNaN(burnVec.magnitude) ? 0.0 : burnVec.magnitude;
                        _maneuverTotalDeltaV = node.DeltaV != null ? node.DeltaV.magnitude : _maneuverDeltaV;
                        if (double.IsNaN(_maneuverTotalDeltaV)) _maneuverTotalDeltaV = _maneuverDeltaV;

                        double ut = Planetarium.GetUniversalTime();
                        _maneuverTimeToNode = node.UT - ut;

                        double burnDur = 0.0;
                        EnsurePropulsionUpdated();
                        double thrust = _cachedTotalThrust;
                        double mass = v.totalMass;
                        if (thrust > 0.1 && mass > 0.01)
                        {
                            double accel = thrust / mass;
                            burnDur = (_maneuverDeltaV > 0.01 ? _maneuverDeltaV : _maneuverTotalDeltaV) / accel;
                        }
                        _maneuverBurnTime = (double.IsNaN(burnDur) || burnDur < 0.0) ? 0.0 : burnDur;
                        _maneuverTimeToBurn = _maneuverTimeToNode - (_maneuverBurnTime * 0.5);

                        if (node.patch != null && !double.IsNaN(burnVec.magnitude) && burnVec.magnitude > 0.01)
                        {
                            Vector3d proDir = node.patch.getOrbitalVelocityAtUT(node.UT).normalized;
                            Vector3d nrmDir = node.patch.GetOrbitNormal().normalized;
                            Vector3d radDir = Vector3d.Cross(nrmDir, proDir).normalized;

                            _maneuverDeltaVPrograde = Vector3d.Dot(burnVec, proDir);
                            _maneuverDeltaVNormal = Vector3d.Dot(burnVec, nrmDir);
                            _maneuverDeltaVRadial = Vector3d.Dot(burnVec, radDir);
                        }
                        else if (node.DeltaV != null)
                        {
                            _maneuverDeltaVRadial = node.DeltaV.x;
                            _maneuverDeltaVNormal = node.DeltaV.y;
                            _maneuverDeltaVPrograde = node.DeltaV.z;
                        }
                        else
                        {
                            _maneuverDeltaVPrograde = _maneuverDeltaV;
                            _maneuverDeltaVNormal = 0.0;
                            _maneuverDeltaVRadial = 0.0;
                        }
                        return;
                    }
                }

                // 3. MechJeb 兜底
                if (MechJebProbe.IsAvailable)
                {
                    double mjDv = MechJebProbe.ResolveNumeric("NODEDV");
                    if (!double.IsNaN(mjDv) && mjDv > 0.001)
                    {
                        _hasManeuverNode = true;
                        _maneuverSource = "MECHJEB";
                        _maneuverDeltaV = mjDv;
                        _maneuverTotalDeltaV = mjDv;
                        double mjDur = MechJebProbe.ResolveNumeric("NODEBURNTIME");
                        _maneuverBurnTime = (double.IsNaN(mjDur) || mjDur < 0.0) ? 0.0 : mjDur;
                        double mjTime = MechJebProbe.ResolveNumeric("TIMETONODE");
                        _maneuverTimeToNode = (double.IsNaN(mjTime) || mjTime < 0.0) ? 0.0 : mjTime;
                        _maneuverTimeToBurn = _maneuverTimeToNode - (_maneuverBurnTime * 0.5);

                        _maneuverDeltaVPrograde = mjDv;
                        _maneuverDeltaVNormal = 0.0;
                        _maneuverDeltaVRadial = 0.0;
                        return;
                    }
                }

                ResetManeuverParameters();
            }
            catch
            {
                ResetManeuverParameters();
            }
        }

        private void ResetManeuverParameters()
        {
            _hasManeuverNode = false;
            _maneuverSource = "STANDBY";
            _maneuverDeltaV = 0.0;
            _maneuverTotalDeltaV = 0.0;
            _maneuverTimeToNode = 0.0;
            _maneuverBurnTime = 0.0;
            _maneuverTimeToBurn = 0.0;
            _maneuverDeltaVPrograde = 0.0;
            _maneuverDeltaVNormal = 0.0;
            _maneuverDeltaVRadial = 0.0;
        }

        public void WarpToManeuverNode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.WarpToManeuverNode();
                return;
            }

            Vessel v = ActiveVessel;
            if (v != null && v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                var node = v.patchedConicSolver.maneuverNodes[0];
                if (node != null && TimeWarp.fetch != null)
                {
                    EnsureManeuverUpdated();
                    double targetUT = node.UT - (_maneuverBurnTime * 0.5) - 15.0;
                    if (targetUT > Planetarium.GetUniversalTime())
                    {
                        TimeWarp.fetch.WarpTo(targetUT);
                    }
                }
            }
        }

        public void DeleteManeuverNode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.DeleteManeuverNode();
                return;
            }

            Vessel v = ActiveVessel;
            if (v != null && v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                var node = v.patchedConicSolver.maneuverNodes[0];
                if (node != null)
                {
                    node.RemoveSelf();
                    ResetManeuverParameters();
                }
            }
        }
    }
}
