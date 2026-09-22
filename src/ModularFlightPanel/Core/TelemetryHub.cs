using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    public enum SpeedDisplayMode
    {
        Surface,
        Orbit,
        Target
    }

    public enum AltitudeDisplayMode
    {
        Ground, // AGL 雷达真高
        Sea     // ASL 绝对海拔
    }

    public class TelemetryHub : MonoBehaviour
    {
        private static TelemetryHub _instance;
        public static TelemetryHub Instance => _instance;

        public Vessel ActiveVessel => FlightGlobals.ActiveVessel;
        public bool HasVessel => ActiveVessel != null && ActiveVessel.loaded && !ActiveVessel.packed;

        // 姿态与航向
        public Quaternion AttitudeRotation { get; private set; } = Quaternion.identity;
        public float Heading { get; private set; } = 0f;
        public float Pitch { get; private set; } = 0f;
        public float Roll { get; private set; } = 0f;

        // 速度
        public SpeedDisplayMode CurrentSpeedMode { get; set; } = SpeedDisplayMode.Surface;
        public double CurrentSpeed { get; private set; } = 0.0;
        public double SurfaceSpeed { get; private set; } = 0.0;
        public double OrbitalSpeed { get; private set; } = 0.0;
        public double TargetSpeed { get; private set; } = 0.0;

        // 高度
        public AltitudeDisplayMode CurrentAltMode { get; set; } = AltitudeDisplayMode.Ground;
        public double AltitudeASL { get; private set; } = 0.0;
        public double AltitudeAGL { get; private set; } = 0.0;
        public double DisplayAltitude => CurrentAltMode == AltitudeDisplayMode.Ground ? AltitudeAGL : AltitudeASL;
        public bool IsTouchdownAlert { get; private set; } = false;

        // 垂直爬升速度与弧形归一化
        public double VerticalSpeed { get; private set; } = 0.0;
        public float NormalizedVSI { get; private set; } = 0.5f; // 0.0 (-100m/s) ~ 0.5 (0m/s) ~ 1.0 (+100m/s)

        // 油门与分级推进剂
        public float Throttle { get; private set; } = 0f;
        public float StagePropellantFraction { get; private set; } = 1.0f;

        // 轨道动力学
        public double Apoapsis { get; private set; } = 0.0;
        public double Periapsis { get; private set; } = 0.0;
        public double TimeToAp { get; private set; } = 0.0;
        public double TimeToPe { get; private set; } = 0.0;

        // 飞控开关与 SAS
        public bool IsRCSEnabled { get; private set; } = false;
        public bool IsSASEnabled { get; private set; } = false;
        public VesselAutopilot.AutopilotMode CurrentSASMode { get; private set; } = VesselAutopilot.AutopilotMode.StabilityAssist;

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            if (!HasVessel) return;

            UpdateAttitudeAndHeading();
            UpdateSpeeds();
            UpdateAltitudes();
            UpdateVerticalSpeed();
            UpdateThrottleAndPropellant();
            UpdateOrbitalParameters();
            UpdateFlightControls();
        }

        private void UpdateAttitudeAndHeading()
        {
            Transform refTransform = ActiveVessel.ReferenceTransform;
            if (refTransform == null) return;

            // 获取飞船基准旋转 (相对于参考坐标系)
            AttitudeRotation = Quaternion.Inverse(refTransform.rotation);

            // 准确计算相对于天体北极的航向角 (0° ~ 360°)
            Vector3d up = ActiveVessel.up;
            Vector3d north = Vector3d.Exclude(up, ActiveVessel.mainBody.transform.up).normalized;
            Vector3d forward = Vector3d.Exclude(up, refTransform.up).normalized;

            double headingAngle = Vector3d.Angle(north, forward);
            if (Vector3d.Dot(Vector3d.Cross(north, forward), up) < 0.0)
            {
                headingAngle = 360.0 - headingAngle;
            }
            Heading = (float)headingAngle;

            // 俯仰角 (-90° ~ +90°)
            Pitch = (float)(90.0 - Vector3d.Angle(refTransform.up, up));

            // 滚转角 (-180° ~ +180°)
            Vector3d right = Vector3d.Cross(up, forward).normalized;
            Roll = (float)Vector3d.Angle(refTransform.right, right);
            if (Vector3d.Dot(refTransform.forward, right) > 0.0)
            {
                Roll = -Roll;
            }
        }

        private void UpdateSpeeds()
        {
            SurfaceSpeed = ActiveVessel.srfSpeed;
            OrbitalSpeed = ActiveVessel.obt_speed;
            TargetSpeed = ActiveVessel.targetObject != null ? FlightGlobals.ship_tgtVelocity.magnitude : 0.0;

            switch (CurrentSpeedMode)
            {
                case SpeedDisplayMode.Surface:
                    CurrentSpeed = SurfaceSpeed;
                    break;
                case SpeedDisplayMode.Orbit:
                    CurrentSpeed = OrbitalSpeed;
                    break;
                case SpeedDisplayMode.Target:
                    CurrentSpeed = TargetSpeed;
                    break;
            }
        }

        private void UpdateAltitudes()
        {
            AltitudeASL = ActiveVessel.altitude;
            AltitudeAGL = ActiveVessel.radarAltitude;

            // 触地预警：当真高小于 300 米且正在下坠时点亮预警
            IsTouchdownAlert = (AltitudeAGL < 300.0 && VerticalSpeed < -1.5);
        }

        private void UpdateVerticalSpeed()
        {
            VerticalSpeed = ActiveVessel.verticalSpeed;

            // 将 -100 ~ +100 m/s 经由对数/分段曲线非线性映射到 0.0 ~ 1.0
            float sign = Mathf.Sign((float)VerticalSpeed);
            float mag = Mathf.Abs((float)VerticalSpeed);
            float scaled = Mathf.Log10(Mathf.Clamp(mag, 0f, 100f) + 1f) / Mathf.Log10(101f);
            NormalizedVSI = Mathf.Clamp01(0.5f + sign * scaled * 0.5f);
        }

        private void UpdateThrottleAndPropellant()
        {
            Throttle = FlightInputHandler.state.mainThrottle;

            double currentResource = 0.0;
            double maxResource = 0.0;
            List<Part> currentStageParts = ActiveVessel.FindPartModulesImplementing<ModuleEngines>()
                .ConvertAll(m => m.part);

            foreach (Part p in currentStageParts)
            {
                for (int i = 0; i < p.Resources.Count; i++)
                {
                    PartResource res = p.Resources[i];
                    if (res.info.name == "LiquidFuel" || res.info.name == "SolidFuel" || res.info.name == "Propellant")
                    {
                        currentResource += res.amount;
                        maxResource += res.maxAmount;
                    }
                }
            }

            StagePropellantFraction = maxResource > 0.001 ? (float)(currentResource / maxResource) : 1.0f;
        }

        private void UpdateOrbitalParameters()
        {
            Orbit orbit = ActiveVessel.orbit;
            if (orbit != null)
            {
                Apoapsis = orbit.ApA;
                Periapsis = orbit.PeA;
                TimeToAp = orbit.timeToAp;
                TimeToPe = orbit.timeToPe;
            }
        }

        private void UpdateFlightControls()
        {
            IsRCSEnabled = ActiveVessel.ActionGroups[KSPActionGroup.RCS];
            IsSASEnabled = ActiveVessel.ActionGroups[KSPActionGroup.SAS];

            if (ActiveVessel.Autopilot != null)
            {
                CurrentSASMode = ActiveVessel.Autopilot.Mode;
            }
        }

        public void CycleSpeedMode()
        {
            CurrentSpeedMode = (SpeedDisplayMode)(((int)CurrentSpeedMode + 1) % 3);
            if (CurrentSpeedMode == SpeedDisplayMode.Target && ActiveVessel.targetObject == null)
            {
                CurrentSpeedMode = SpeedDisplayMode.Surface;
            }
        }

        public void CycleAltitudeMode()
        {
            CurrentAltMode = (CurrentAltMode == AltitudeDisplayMode.Ground) ? AltitudeDisplayMode.Sea : AltitudeDisplayMode.Ground;
        }

        public void ToggleRCS()
        {
            if (ActiveVessel != null)
            {
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, !IsRCSEnabled);
            }
        }

        public void ToggleSAS()
        {
            if (ActiveVessel != null)
            {
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, !IsSASEnabled);
            }
        }

        public void SetSASMode(VesselAutopilot.AutopilotMode mode)
        {
            if (ActiveVessel?.Autopilot != null)
            {
                if (!IsSASEnabled)
                {
                    ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                }
                ActiveVessel.Autopilot.SetMode(mode);
            }
        }
    }
}
