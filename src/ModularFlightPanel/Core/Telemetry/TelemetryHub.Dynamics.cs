using System;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    public partial class TelemetryHub
    {
        // -------------------------------------------------------------
        // 姿态与航向 (Attitude & Heading) - 按需惰性解算与同帧去重
        // -------------------------------------------------------------
        private int _attitudeFrame = -1;
        private Quaternion _attitudeRotation = Quaternion.identity;
        private float _heading = 0f;
        private float _pitch = 0f;
        private float _roll = 0f;

        public Quaternion AttitudeRotation
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.AttitudeRotation;
                EnsureAttitudeUpdated();
                return _attitudeRotation;
            }
        }

        public float Heading
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Heading;
                EnsureAttitudeUpdated();
                return _heading;
            }
        }

        public float Pitch
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Pitch;
                EnsureAttitudeUpdated();
                return _pitch;
            }
        }

        public float Roll
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Roll;
                EnsureAttitudeUpdated();
                return _roll;
            }
        }

        private void EnsureAttitudeUpdated()
        {
            if (_attitudeFrame == Time.frameCount) return;
            _attitudeFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateAttitudeAndHeading();
        }

        private void UpdateAttitudeAndHeading()
        {
            Transform refTransform = ActiveVessel.ReferenceTransform;
            if (refTransform == null) return;

            if (StockNavBallHook.HasStockNavBall)
            {
                _attitudeRotation = StockNavBallHook.GetRotation();
            }
            else
            {
                _attitudeRotation = Quaternion.Inverse(refTransform.rotation);
            }

            Vector3d up = ActiveVessel.up;
            Vector3d bodyUp = ActiveVessel.mainBody != null ? ActiveVessel.mainBody.transform.up : Vector3d.up;
            Vector3d north = Vector3d.Exclude(up, bodyUp).normalized;
            if (north.sqrMagnitude < 0.001) north = Vector3d.forward;
            Vector3d east = Vector3d.Cross(up, north).normalized;

            // 1. 权威 Pitch (-90° ~ +90°)
            double pitchVal = 90.0 - Vector3d.Angle(refTransform.up, up);
            if (!double.IsNaN(pitchVal))
            {
                _pitch = (float)pitchVal;
            }

            // 2. 权威 Heading (0° ~ 360°) - 优先从原版 NavBall 相对万向节提取高精度连续航向，杜绝整数截断跳变
            if (StockNavBallHook.GetContinuousHeading(out float continuousHdg))
            {
                _heading = continuousHdg;
            }
            else
            {
                Vector3d forwardHoriz = Vector3d.Exclude(up, refTransform.up);
                if (forwardHoriz.sqrMagnitude > 0.0001)
                {
                    forwardHoriz.Normalize();
                    double headingAngle = Vector3d.Angle(north, forwardHoriz);
                    if (Vector3d.Dot(east, forwardHoriz) < 0.0)
                    {
                        headingAngle = 360.0 - headingAngle;
                    }
                    if (!double.IsNaN(headingAngle))
                    {
                        _heading = (float)headingAngle;
                    }
                }
                else if (StockNavBallHook.HasStockNavBall && StockNavBallHook.StockInstance.headingText != null &&
                    float.TryParse(StockNavBallHook.StockInstance.headingText.text.Replace("°", "").Trim(), out float stockHdg))
                {
                    _heading = stockHdg;
                }
            }

            // 3. 权威 Roll (-180° ~ +180°) - 严格遵循标准航电水平基准系 (NED Topocentric frame)
            if (Math.Abs(_pitch) <= 89.85f)
            {
                Vector3d refTop = refTransform.forward;
                Vector3d vecY = Vector3d.Cross(up, refTransform.up).normalized;
                double trigX = Vector3d.Dot(refTop, up);
                double trigY = Vector3d.Dot(refTop, vecY);
                double rollVal = Math.Atan2(trigY, trigX) * (180.0 / Math.PI);
                if (!double.IsNaN(rollVal))
                {
                    _roll = (float)rollVal;
                }
            }
        }

        // -------------------------------------------------------------
        // 速度与马赫数 (Speeds & Mach) - 按需惰性解算与同帧去重
        // -------------------------------------------------------------
        private int _speedFrame = -1;
        private double _currentSpeed = 0.0;
        private double _surfaceSpeed = 0.0;
        private double _orbitalSpeed = 0.0;
        private double _targetSpeed = 0.0;
        private double _mach = 0.0;

        public SpeedDisplayMode CurrentSpeedMode { get; set; } = SpeedDisplayMode.Surface;

        public string SpeedModeName
        {
            get
            {
                if (IsSimulationMode) return CurrentSpeedMode.ToString().ToUpperInvariant();

                if (PrincipiaProbe.IsAvailable)
                {
                    string pNav = PrincipiaProbe.NavballFrameName;
                    if (!string.IsNullOrEmpty(pNav)) return pNav.ToUpperInvariant();
                    string pFrame = PrincipiaProbe.FrameName;
                    if (!string.IsNullOrEmpty(pFrame)) return pFrame.ToUpperInvariant();
                }
                string stock = StockNavBallHook.GetReferenceFrameName();
                if (!string.IsNullOrEmpty(stock)) return stock.ToUpperInvariant();
                return CurrentSpeedMode.ToString().ToUpperInvariant();
            }
        }

        public double CurrentSpeed
        {
            get
            {
                if (IsSimulationMode) return (CurrentSpeedMode == SpeedDisplayMode.Orbit) ? SimulationEngine.OrbitalSpeed : SimulationEngine.SurfaceSpeed;
                EnsureSpeedsUpdated();
                return _currentSpeed;
            }
        }

        public double SurfaceSpeed
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.SurfaceSpeed;
                EnsureSpeedsUpdated();
                return _surfaceSpeed;
            }
        }

        public double OrbitalSpeed
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.OrbitalSpeed;
                EnsureSpeedsUpdated();
                return _orbitalSpeed;
            }
        }

        public double TargetSpeed
        {
            get
            {
                if (IsSimulationMode) return 0.0;
                EnsureSpeedsUpdated();
                return _targetSpeed;
            }
        }

        public double Mach
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Mach;
                EnsureSpeedsUpdated();
                return _mach;
            }
        }

        private void EnsureSpeedsUpdated()
        {
            if (_speedFrame == Time.frameCount) return;
            _speedFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateSpeeds();
        }

        private void UpdateSpeeds()
        {
            if (FlightGlobals.fetch != null)
            {
                switch (FlightGlobals.speedDisplayMode)
                {
                    case FlightGlobals.SpeedDisplayModes.Surface:
                        CurrentSpeedMode = SpeedDisplayMode.Surface;
                        break;
                    case FlightGlobals.SpeedDisplayModes.Orbit:
                        CurrentSpeedMode = SpeedDisplayMode.Orbit;
                        break;
                    case FlightGlobals.SpeedDisplayModes.Target:
                        CurrentSpeedMode = SpeedDisplayMode.Target;
                        break;
                }
            }

            _surfaceSpeed = ActiveVessel.srfSpeed;
            _orbitalSpeed = ActiveVessel.obt_speed;
            _targetSpeed = ActiveVessel.targetObject != null ? FlightGlobals.ship_tgtVelocity.magnitude : 0.0;
            _mach = ActiveVessel.mach;

            if (double.IsNaN(_surfaceSpeed)) _surfaceSpeed = 0.0;
            if (double.IsNaN(_orbitalSpeed)) _orbitalSpeed = 0.0;
            if (double.IsNaN(_targetSpeed)) _targetSpeed = 0.0;
            if (double.IsNaN(_mach)) _mach = 0.0;

            switch (CurrentSpeedMode)
            {
                case SpeedDisplayMode.Surface:
                    _currentSpeed = _surfaceSpeed;
                    break;
                case SpeedDisplayMode.Orbit:
                    _currentSpeed = _orbitalSpeed;
                    break;
                case SpeedDisplayMode.Target:
                    _currentSpeed = _targetSpeed;
                    break;
            }

            // Principia 权威参考系接管
            if (PrincipiaProbe.IsAvailable)
            {
                if (PrincipiaProbe.IsTargetFrameSelected)
                {
                    CurrentSpeedMode = SpeedDisplayMode.Target;
                    if (PrincipiaProbe.GetActiveVesselSpeed(out double pTgtSpeed))
                    {
                        _targetSpeed = pTgtSpeed;
                        _currentSpeed = pTgtSpeed;
                    }
                    else if (StockNavBallHook.TryGetNavballSpeed(out double nbSpeed))
                    {
                        _targetSpeed = nbSpeed;
                        _currentSpeed = nbSpeed;
                    }
                }
                else if (FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Target)
                {
                    CurrentSpeedMode = SpeedDisplayMode.Target;
                    _currentSpeed = _targetSpeed;
                }
                else
                {
                    bool isSurface = PrincipiaProbe.IsSurfaceFrameSelected;
                    CurrentSpeedMode = isSurface ? SpeedDisplayMode.Surface : SpeedDisplayMode.Orbit;

                    if (PrincipiaProbe.GetActiveVesselSpeed(out double pSpeed))
                    {
                        _currentSpeed = pSpeed;
                        if (isSurface) _surfaceSpeed = pSpeed;
                        else _orbitalSpeed = pSpeed;
                    }
                    else if (StockNavBallHook.TryGetNavballSpeed(out double nbSpeed))
                    {
                        _currentSpeed = nbSpeed;
                        if (isSurface) _surfaceSpeed = nbSpeed;
                        else _orbitalSpeed = nbSpeed;
                    }
                }
            }
            else
            {
                string refName = StockNavBallHook.GetReferenceFrameName();
                if (!string.IsNullOrEmpty(refName) &&
                    refName != "ORBIT" && refName != "SURFACE" && refName != "TARGET" &&
                    StockNavBallHook.TryGetNavballSpeed(out double extSpeed))
                {
                    _currentSpeed = extSpeed;
                    if (CurrentSpeedMode == SpeedDisplayMode.Orbit) _orbitalSpeed = extSpeed;
                    else if (CurrentSpeedMode == SpeedDisplayMode.Surface) _surfaceSpeed = extSpeed;
                }
            }
        }

        public void CycleSpeedMode()
        {
            if (IsSimulationMode)
            {
                CurrentSpeedMode = (SpeedDisplayMode)(((int)CurrentSpeedMode + 1) % 3);
                return;
            }

            if (PrincipiaProbe.IsAvailable)
            {
                PrincipiaProbe.CycleReferenceFrame();
            }
            else if (FlightGlobals.fetch != null)
            {
                FlightGlobals.CycleSpeedModes();
            }

            if (FlightGlobals.fetch != null)
            {
                switch (FlightGlobals.speedDisplayMode)
                {
                    case FlightGlobals.SpeedDisplayModes.Surface:
                        CurrentSpeedMode = SpeedDisplayMode.Surface;
                        break;
                    case FlightGlobals.SpeedDisplayModes.Orbit:
                        CurrentSpeedMode = SpeedDisplayMode.Orbit;
                        break;
                    case FlightGlobals.SpeedDisplayModes.Target:
                        CurrentSpeedMode = SpeedDisplayMode.Target;
                        break;
                }
            }
            _speedFrame = -1; // 强制下一读数即刻更新
        }

        // -------------------------------------------------------------
        // 高度与动力学 (Altitudes & Dynamics) - 按需惰性解算与同帧去重
        // -------------------------------------------------------------
        private int _altitudeFrame = -1;
        private double _altitudeASL = 0.0;
        private double _altitudeAGL = 0.0;
        private double _dynamicPressure = 0.0;
        private double _atmosphericPressure = 1.0;
        private double _gForce = 1.0;
        private bool _isTouchdownAlert = false;

        public AltitudeDisplayMode CurrentAltMode { get; set; } = AltitudeDisplayMode.Ground;

        public double AltitudeASL
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.AltitudeASL;
                EnsureAltitudesUpdated();
                return _altitudeASL;
            }
        }

        public double AltitudeAGL
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.AltitudeAGL;
                EnsureAltitudesUpdated();
                return _altitudeAGL;
            }
        }

        public double DisplayAltitude => CurrentAltMode == AltitudeDisplayMode.Ground ? AltitudeAGL : AltitudeASL;

        public double DynamicPressure
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.DynamicPressure;
                EnsureAltitudesUpdated();
                return _dynamicPressure;
            }
        }

        public double AtmosphericPressure
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.AtmosphericPressure;
                EnsureAltitudesUpdated();
                return _atmosphericPressure;
            }
        }

        public double GForce
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.GForce;
                EnsureAltitudesUpdated();
                return _gForce;
            }
        }

        public bool IsTouchdownAlert
        {
            get
            {
                if (IsSimulationMode) return (SimulationEngine.AltitudeAGL < 300.0 && SimulationEngine.VerticalSpeed < -1.5);
                EnsureAltitudesUpdated();
                EnsureVerticalSpeedUpdated();
                return _isTouchdownAlert;
            }
        }

        public bool HasAtmosphere => ActiveVessel != null && ActiveVessel.mainBody != null && ActiveVessel.mainBody.atmosphere;
        public double AtmosphereDepth => (ActiveVessel != null && ActiveVessel.mainBody != null && ActiveVessel.mainBody.atmosphere) ? ActiveVessel.mainBody.atmosphereDepth : 0.0;

        private void EnsureAltitudesUpdated()
        {
            if (_altitudeFrame == Time.frameCount) return;
            _altitudeFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateAltitudes();
        }

        private void UpdateAltitudes()
        {
            _altitudeASL = ActiveVessel.altitude;
            _altitudeAGL = ActiveVessel.radarAltitude;

            double q = ActiveVessel.dynamicPressurekPa;
            if (q <= 0.00001 && FarProbe.IsAvailable)
            {
                double farQ = FarProbe.DynamicPressure;
                if (!double.IsNaN(farQ) && farQ > 0.0) q = farQ;
            }
            _dynamicPressure = q;

            double atm = double.NaN;
            if (FarProbe.IsAvailable)
            {
                atm = FarProbe.GetAtmosphericPressureAtm(ActiveVessel);
            }

            bool inAtmosphere = ActiveVessel.mainBody != null && ActiveVessel.mainBody.atmosphere && ActiveVessel.altitude < ActiveVessel.mainBody.atmosphereDepth;
            if (double.IsNaN(atm) || (atm <= 0.0 && inAtmosphere))
            {
                if (ActiveVessel.staticPressurekPa > 0.0)
                {
                    atm = ActiveVessel.staticPressurekPa / 101.325;
                }
                else if (inAtmosphere)
                {
                    double staticKpa = ActiveVessel.mainBody.GetPressure(ActiveVessel.altitude);
                    atm = (staticKpa > 0.0) ? (staticKpa / 101.325) : 0.0;
                }
                else
                {
                    atm = 0.0;
                }
            }

            _atmosphericPressure = !double.IsNaN(atm) ? Math.Max(0.0, atm) : 0.0;
            _gForce = ActiveVessel.geeForce;

            if (double.IsNaN(_altitudeASL)) _altitudeASL = 0.0;
            if (double.IsNaN(_altitudeAGL)) _altitudeAGL = 0.0;
            if (double.IsNaN(_dynamicPressure)) _dynamicPressure = 0.0;
            if (double.IsNaN(_atmosphericPressure)) _atmosphericPressure = 0.0;
            if (double.IsNaN(_gForce)) _gForce = 1.0;

            _isTouchdownAlert = (_altitudeAGL < 300.0 && _verticalSpeed < -1.5);
        }

        public void CycleAltitudeMode()
        {
            CurrentAltMode = (CurrentAltMode == AltitudeDisplayMode.Ground) ? AltitudeDisplayMode.Sea : AltitudeDisplayMode.Ground;
        }

        // -------------------------------------------------------------
        // 垂直爬升速度与 VSI (Vertical Speed & Normalized VSI)
        // -------------------------------------------------------------
        private int _verticalSpeedFrame = -1;
        private double _verticalSpeed = 0.0;
        private float _normalizedVSI = 0.5f;

        public double VerticalSpeed
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.VerticalSpeed;
                EnsureVerticalSpeedUpdated();
                return _verticalSpeed;
            }
        }

        public float NormalizedVSI
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.NormalizedVSI;
                EnsureVerticalSpeedUpdated();
                return _normalizedVSI;
            }
        }

        private void EnsureVerticalSpeedUpdated()
        {
            if (_verticalSpeedFrame == Time.frameCount) return;
            _verticalSpeedFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateVerticalSpeed();
        }

        private void UpdateVerticalSpeed()
        {
            _verticalSpeed = ActiveVessel.verticalSpeed;
            if (double.IsNaN(_verticalSpeed)) _verticalSpeed = 0.0;

            float sign = Mathf.Sign((float)_verticalSpeed);
            float mag = Mathf.Abs((float)_verticalSpeed);
            float scaled = Mathf.Log10(Mathf.Clamp(mag, 0f, 100f) + 1f) / Mathf.Log10(101f);
            _normalizedVSI = Mathf.Clamp01(0.5f + sign * scaled * 0.5f);
        }
    }
}
