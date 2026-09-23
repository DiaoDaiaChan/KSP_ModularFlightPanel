using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

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

    /// <summary>
    /// 统一机载遥测中枢 (支持真实飞行遥测与高保真物理仿真双模式)
    /// 彻底解耦，无论在飞行场景还是在主菜单/航天中心测试沙盒，均可向所有组件稳定喂送数据流
    /// </summary>
    public class TelemetryHub : MonoBehaviour, IFlightTelemetry
    {
        private static TelemetryHub _instance;
        public static TelemetryHub Instance => _instance;

        public Vessel ActiveVessel => FlightGlobals.ActiveVessel;
        public string VesselName => ActiveVessel != null ? ActiveVessel.vesselName : (IsSimulationMode ? SimulationEngine.VesselName : "NO VESSEL");
        public string CelestialBodyName => ActiveVessel != null && ActiveVessel.mainBody != null ? ActiveVessel.mainBody.displayName.LocalizeRemoveGender() : (IsSimulationMode ? "KERBIN" : "UNKNOWN");
        public string FlightSituation => ActiveVessel != null ? ActiveVessel.situation.ToString().ToUpper() : (IsSimulationMode ? "SUB_ORBITAL" : "LANDED");

        // 仿真测试模式开关与引擎
        public bool IsSimulationMode { get; set; } = false;
        public TelemetrySimulationEngine SimulationEngine { get; } = new TelemetrySimulationEngine();

        public bool HasVessel => IsSimulationMode || (ActiveVessel != null && ActiveVessel.loaded && ActiveVessel.state != Vessel.State.DEAD);

        // 姿态与航向
        public Quaternion AttitudeRotation { get; private set; } = Quaternion.identity;
        public float Heading { get; private set; } = 0f;
        public float Pitch { get; private set; } = 0f;
        public float Roll { get; private set; } = 0f;

        // 速度
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
        public double CurrentSpeed { get; private set; } = 0.0;
        public double SurfaceSpeed { get; private set; } = 0.0;
        public double OrbitalSpeed { get; private set; } = 0.0;
        public double TargetSpeed { get; private set; } = 0.0;
        public double Mach { get; private set; } = 0.0;

        // 高度与动力学
        public AltitudeDisplayMode CurrentAltMode { get; set; } = AltitudeDisplayMode.Ground;
        public double AltitudeASL { get; private set; } = 0.0;
        public double AltitudeAGL { get; private set; } = 0.0;
        public double DisplayAltitude => CurrentAltMode == AltitudeDisplayMode.Ground ? AltitudeAGL : AltitudeASL;
        public bool IsTouchdownAlert { get; private set; } = false;
        public double DynamicPressure { get; private set; } = 0.0;
        public double AtmosphericPressure { get; private set; } = 1.0;
        public double GForce { get; private set; } = 1.0;

        // 垂直爬升速度与弧形归一化
        public double VerticalSpeed { get; private set; } = 0.0;
        public float NormalizedVSI { get; private set; } = 0.5f;

        // 油门、推力与推进剂
        public float Throttle { get; private set; } = 0f;
        public float StagePropellantFraction { get; private set; } = 1.0f;
        public double TWR { get; private set; } = 0.0;
        public int CurrentStage { get; private set; } = 0;
        public double StageDeltaV { get; private set; } = 0.0;
        public double TotalDeltaV { get; private set; } = 0.0;
        public double StageBurnTime { get; private set; } = 0.0;
        public double TotalBurnTime { get; private set; } = 0.0;
        public IReadOnlyList<StageDeltaVInfo> StageDeltaVList { get; private set; } = Array.Empty<StageDeltaVInfo>();
        public string DeltaVSource { get; private set; } = "NONE";
        public int ActiveEngines { get; private set; } = 0;

        // 轨道动力学
        public double Apoapsis { get; private set; } = 0.0;
        public double Periapsis { get; private set; } = 0.0;
        public double TimeToAp { get; private set; } = 0.0;
        public double TimeToPe { get; private set; } = 0.0;

        // 电气系统 (通用解耦读取)
        public double ElectricCharge { get; private set; } = 0.0;
        public double MaxElectricCharge { get; private set; } = 0.0;
        public double EcPercent => MaxElectricCharge > 0.001 ? (ElectricCharge / MaxElectricCharge * 100.0) : 100.0;
        public double NetEcRate { get; private set; } = 0.0;
        public float BusVoltage { get; private set; } = 28.0f;
        public double SolarPower { get; private set; } = 0.0;

        // 通信网络 (通用解耦读取)
        public double CommSignal { get; private set; } = 1.0;
        public bool IsConnected { get; private set; } = true;
        public string ControlLevelStr { get; private set; } = "FULL CONTROL";
        public int AntennaCount { get; private set; } = 0;
        public double SignalTx { get; private set; } = 1.0;
        public double SignalRx { get; private set; } = 1.0;
        public double DataRateBps { get; private set; } = 0.0;
        public string DirectLinkTarget { get; private set; } = "NONE";
        public IReadOnlyList<CommLinkInfo> ActiveCommLinks { get; private set; } = Array.Empty<CommLinkInfo>();

        // 时间加速与时钟遥测
        public double MissionTime => IsSimulationMode ? SimulationEngine.MissionTime : (ActiveVessel != null ? ActiveVessel.missionTime : 0.0);
        public double UniversalTime => IsSimulationMode ? SimulationEngine.UniversalTime : Planetarium.GetUniversalTime();
        public float TimeWarpRate => IsSimulationMode ? SimulationEngine.TimeWarpRate : (TimeWarp.fetch != null ? TimeWarp.CurrentRate : 1.0f);
        public int TimeWarpRateIndex => IsSimulationMode ? SimulationEngine.TimeWarpRateIndex : (TimeWarp.fetch != null ? TimeWarp.CurrentRateIndex : 0);
        public int MaxTimeWarpRateIndex => IsSimulationMode ? SimulationEngine.MaxTimeWarpRateIndex : (TimeWarp.fetch != null ? (TimeWarp.WarpMode == TimeWarp.Modes.LOW ? TimeWarp.fetch.physicsWarpRates.Length - 1 : TimeWarp.fetch.warpRates.Length - 1) : 7);
        public bool IsPhysicsWarp => IsSimulationMode ? SimulationEngine.IsPhysicsWarp : (TimeWarp.WarpMode == TimeWarp.Modes.LOW);
        public bool IsGamePaused => IsSimulationMode ? SimulationEngine.IsGamePaused : (FlightGlobals.ready && (Time.timeScale == 0f || PauseMenu.isOpen));

        // 居住环境 (通用解耦读取)
        public int CrewCount { get; private set; } = 0;
        public int CrewCapacity { get; private set; } = 0;
        public double CabinPressure { get; private set; } = 101.3;
        public double CabinTemp { get; private set; } = 21.0;
        public float OxygenPercent { get; private set; } = 100.0f;
        public float MonoPercent { get; private set; } = 100.0f;
        public float WaterPercent { get; private set; } = 100.0f;

        // 飞控开关与 SAS
        public bool IsRCSEnabled { get; private set; } = false;
        public bool IsSASEnabled { get; private set; } = false;
        public FlightSASMode CurrentSASMode { get; private set; } = FlightSASMode.StabilityAssist;
        public VesselAutopilot.AutopilotMode KspSASMode => (VesselAutopilot.AutopilotMode)(int)CurrentSASMode;

        public void SetSASMode(FlightSASMode mode)
        {
            CurrentSASMode = mode;
            if (IsSimulationMode)
            {
                SimulationEngine.SetSASMode(mode);
                return;
            }
            if (ActiveVessel != null && ActiveVessel.Autopilot != null)
            {
                ActiveVessel.Autopilot.SetMode((VesselAutopilot.AutopilotMode)(int)mode);
            }
        }

        public void ToggleSAS()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleSAS();
                IsSASEnabled = SimulationEngine.IsSASEnabled;
                return;
            }
            if (ActiveVessel != null && ActiveVessel.ActionGroups != null)
            {
                ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.SAS);
                IsSASEnabled = ActiveVessel.ActionGroups[KSPActionGroup.SAS];
            }
        }

        public void ToggleRCS()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleRCS();
                IsRCSEnabled = SimulationEngine.IsRCSEnabled;
                return;
            }
            if (ActiveVessel != null && ActiveVessel.ActionGroups != null)
            {
                ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.RCS);
                IsRCSEnabled = ActiveVessel.ActionGroups[KSPActionGroup.RCS];
            }
        }

        // 三轴姿态操纵量与配平 (-1.0 ~ +1.0)
        public float PitchInput { get; private set; } = 0f;
        public float RollInput { get; private set; } = 0f;
        public float YawInput { get; private set; } = 0f;
        public float PitchTrim { get; private set; } = 0f;
        public float RollTrim { get; private set; } = 0f;
        public float YawTrim { get; private set; } = 0f;

        // 分级安全锁、模式与推进剂
        public bool IsStageLocked { get; private set; } = false;
        public bool IsPrecisionControl { get; private set; } = false;
        public bool IsDockingMode { get; private set; } = false;
        public string StagePropellantName { get; private set; } = "PROP";

        public void ActivateNextStage()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ActivateNextStage();
                return;
            }
            if (IsStageLocked) return;
            if (KSP.UI.Screens.StageManager.Instance != null)
            {
                KSP.UI.Screens.StageManager.ActivateNextStage();
            }
        }

        public void ToggleStageLock()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleStageLock();
                IsStageLocked = SimulationEngine.IsStageLocked;
                return;
            }
            if (FlightInputHandler.fetch != null)
            {
                FlightInputHandler.fetch.stageLock = !FlightInputHandler.fetch.stageLock;
                IsStageLocked = FlightInputHandler.fetch.stageLock;
            }
        }

        public void TogglePrecisionMode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.TogglePrecisionMode();
                IsPrecisionControl = SimulationEngine.IsPrecisionControl;
                return;
            }
            if (FlightInputHandler.fetch != null)
            {
                FlightInputHandler.fetch.precisionMode = !FlightInputHandler.fetch.precisionMode;
                IsPrecisionControl = FlightInputHandler.fetch.precisionMode;
            }
        }

        public void ToggleFlightMode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleFlightMode();
                IsDockingMode = SimulationEngine.IsDockingMode;
                return;
            }
            if (FlightUIModeController.Instance != null)
            {
                FlightUIMode current = FlightUIModeController.Instance.Mode;
                FlightUIMode target = (current == FlightUIMode.DOCKING) ? FlightUIMode.STAGING : FlightUIMode.DOCKING;
                FlightUIModeController.Instance.SetMode(target);
                IsDockingMode = (target == FlightUIMode.DOCKING);
            }
        }

        // 差分计算电量速率
        private double _lastEc = 0.0;
        private float _lastEcTime = 0f;
        private float _lastSubsystemTime = -1f;
        private float _lastEngineScanTime = -1f;
        private int _lastEngineScanPartCount = -1;
        private int _lastEngineScanStage = -1;
        private double _cachedTotalThrust = 0.0;
        private List<ModuleEngines> _cachedEngines = new List<ModuleEngines>();
        private List<ModuleDeployableSolarPanel> _cachedSolarPanels = new List<ModuleDeployableSolarPanel>();
        private int _cachedAntennaCount = 0;
        private float _lastAntennaScanTime = -1f;
        private float _lastPanelScanTime = -1f;
        private int _lastSubsystemPartCount = -1;
        private float _lastCommScanTime = -1f;

        private void Awake()
        {
            _instance = this;
            FlightTelemetryContext.FallbackProvider = () => Instance;
            ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleSpeedModeAction = () => Instance?.CycleSpeedMode();
            ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleAltitudeModeAction = () => Instance?.CycleAltitudeMode();
            ModularFlightPanel.UI.Widgets.BottomControlsWidget.OnTogglePrincipiaWindowAction = () =>
            {
                if (PrincipiaProbe.IsAvailable)
                    PrincipiaProbe.ToggleReferenceFrameWindow();
                else
                    Instance?.CycleSpeedMode();
            };
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                FlightTelemetryContext.FallbackProvider = null;
                ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleSpeedModeAction = null;
                ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleAltitudeModeAction = null;
                ModularFlightPanel.UI.Widgets.BottomControlsWidget.OnTogglePrincipiaWindowAction = null;
            }
        }

        private void Update()
        {
            if (MFPProfiler.IsMasterBypassed) return;

            MFPProfiler.BeginSample(ProfilerSection.Telemetry);
            try
            {
                if (IsSimulationMode)
                {
                    SimulationEngine.Update(Time.deltaTime);
                    UpdateFromSimulation(SimulationEngine);
                    return;
                }

                if (!HasVessel) return;

                UpdateAttitudeAndHeading();
                UpdateSpeeds();
                UpdateAltitudes();
                UpdateVerticalSpeed();
                UpdateThrottleAndPropellant();
                UpdateOrbitalParameters();
                UpdateFlightControls();

                float now = Time.unscaledTime;
                if (now - _lastSubsystemTime >= 0.1f) // 10 Hz 遥测子系统节流
                {
                    _lastSubsystemTime = now;
                    UpdateSubsystemTelemetry();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] TelemetryHub.Update error: {ex.Message}");
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Telemetry);
            }
        }

        private void UpdateFromSimulation(TelemetrySimulationEngine sim)
        {
            AttitudeRotation = sim.AttitudeRotation;
            Heading = sim.Heading;
            Pitch = sim.Pitch;
            Roll = sim.Roll;

            SurfaceSpeed = sim.SurfaceSpeed;
            OrbitalSpeed = sim.OrbitalSpeed;
            CurrentSpeed = (CurrentSpeedMode == SpeedDisplayMode.Orbit) ? OrbitalSpeed : SurfaceSpeed;
            Mach = sim.Mach;

            AltitudeASL = sim.AltitudeASL;
            AltitudeAGL = sim.AltitudeAGL;
            VerticalSpeed = sim.VerticalSpeed;
            NormalizedVSI = sim.NormalizedVSI;
            DynamicPressure = sim.DynamicPressure;
            AtmosphericPressure = sim.AtmosphericPressure;
            GForce = sim.GForce;

            Throttle = sim.Throttle;
            StagePropellantFraction = sim.StagePropellantFraction;
            TWR = sim.TWR;
            CurrentStage = sim.CurrentStage;
            StageDeltaV = sim.StageDeltaV;
            TotalDeltaV = sim.TotalDeltaV;
            StageBurnTime = sim.StageBurnTime;
            TotalBurnTime = sim.TotalBurnTime;
            StageDeltaVList = sim.StageDeltaVList;
            DeltaVSource = sim.DeltaVSource;
            ActiveEngines = sim.ActiveEngines;

            Apoapsis = sim.Apoapsis;
            Periapsis = sim.Periapsis;
            TimeToAp = sim.TimeToAp;
            TimeToPe = sim.TimeToPe;

            ElectricCharge = sim.ElectricCharge;
            MaxElectricCharge = sim.MaxElectricCharge;
            NetEcRate = sim.NetEcRate;
            BusVoltage = sim.BusVoltage;
            SolarPower = sim.SolarPower;

            CommSignal = sim.CommSignal;
            IsConnected = sim.IsConnected;
            ControlLevelStr = sim.ControlLevelStr;
            AntennaCount = sim.AntennaCount;

            CrewCount = sim.CrewCount;
            CrewCapacity = sim.CrewCapacity;
            CabinPressure = sim.CabinPressure;
            CabinTemp = sim.CabinTemp;
            OxygenPercent = sim.OxygenPercent;
            MonoPercent = sim.MonoPercent;
            WaterPercent = sim.WaterPercent;

            PitchInput = sim.PitchInput;
            RollInput = sim.RollInput;
            YawInput = sim.YawInput;
            PitchTrim = sim.PitchTrim;
            RollTrim = sim.RollTrim;
            YawTrim = sim.YawTrim;
            IsStageLocked = sim.IsStageLocked;
            IsPrecisionControl = sim.IsPrecisionControl;
            IsDockingMode = sim.IsDockingMode;
            StagePropellantName = sim.StagePropellantName;

            IsTouchdownAlert = (AltitudeAGL < 300.0 && VerticalSpeed < -1.5);
        }

        private void UpdateAttitudeAndHeading()
        {
            Transform refTransform = ActiveVessel.ReferenceTransform;
            if (refTransform == null) return;

            if (StockNavBallHook.HasStockNavBall)
            {
                AttitudeRotation = StockNavBallHook.GetRotation();
            }
            else
            {
                AttitudeRotation = Quaternion.Inverse(refTransform.rotation);
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
                Pitch = (float)pitchVal;
            }

            // 2. 权威 Heading (0° ~ 360°) - 优先从原版 NavBall 相对万向节提取高精度连续航向，杜绝整数截断跳变
            if (StockNavBallHook.GetContinuousHeading(out float continuousHdg))
            {
                Heading = continuousHdg;
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
                        Heading = (float)headingAngle;
                    }
                }
                else if (StockNavBallHook.HasStockNavBall && StockNavBallHook.StockInstance.headingText != null &&
                    float.TryParse(StockNavBallHook.StockInstance.headingText.text.Replace("°", "").Trim(), out float stockHdg))
                {
                    Heading = stockHdg;
                }
            }

            // 3. 权威 Roll (-180° ~ +180°) - 严格遵循标准航电水平基准系 (NED Topocentric frame)
            // 在垂直起飞/大俯仰角 (Pitch > 89.85° 或 < -89.85°) 天顶/天底奇异死区，保持上一帧平滑值，彻底杜绝万向节翻滚与浮点噪声抖动
            if (Math.Abs(Pitch) <= 89.85f)
            {
                Vector3d refTop = refTransform.forward; // KSP ReferenceTransform 顶部背侧向量
                Vector3d vecY = Vector3d.Cross(up, refTransform.up).normalized;
                double trigX = Vector3d.Dot(refTop, up);
                double trigY = Vector3d.Dot(refTop, vecY);
                double rollVal = Math.Atan2(trigY, trigX) * (180.0 / Math.PI);
                if (!double.IsNaN(rollVal))
                {
                    Roll = (float)rollVal;
                }
            }
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

            SurfaceSpeed = ActiveVessel.srfSpeed;
            OrbitalSpeed = ActiveVessel.obt_speed;
            TargetSpeed = ActiveVessel.targetObject != null ? FlightGlobals.ship_tgtVelocity.magnitude : 0.0;
            Mach = ActiveVessel.mach;

            if (double.IsNaN(SurfaceSpeed)) SurfaceSpeed = 0.0;
            if (double.IsNaN(OrbitalSpeed)) OrbitalSpeed = 0.0;
            if (double.IsNaN(TargetSpeed)) TargetSpeed = 0.0;
            if (double.IsNaN(Mach)) Mach = 0.0;

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
            DynamicPressure = ActiveVessel.dynamicPressurekPa;
            AtmosphericPressure = (ActiveVessel.staticPressurekPa > 0.0) ? (ActiveVessel.staticPressurekPa / 101.325) : 0.0;
            GForce = ActiveVessel.geeForce;

            if (double.IsNaN(AltitudeASL)) AltitudeASL = 0.0;
            if (double.IsNaN(AltitudeAGL)) AltitudeAGL = 0.0;
            if (double.IsNaN(DynamicPressure)) DynamicPressure = 0.0;
            if (double.IsNaN(AtmosphericPressure)) AtmosphericPressure = 0.0;
            if (double.IsNaN(GForce)) GForce = 1.0;

            IsTouchdownAlert = (AltitudeAGL < 300.0 && VerticalSpeed < -1.5);
        }

        private void UpdateVerticalSpeed()
        {
            VerticalSpeed = ActiveVessel.verticalSpeed;
            if (double.IsNaN(VerticalSpeed)) VerticalSpeed = 0.0;

            float sign = Mathf.Sign((float)VerticalSpeed);
            float mag = Mathf.Abs((float)VerticalSpeed);
            float scaled = Mathf.Log10(Mathf.Clamp(mag, 0f, 100f) + 1f) / Mathf.Log10(101f);
            NormalizedVSI = Mathf.Clamp01(0.5f + sign * scaled * 0.5f);
        }

        private void UpdateThrottleAndPropellant()
        {
            try
            {
                Throttle = FlightInputHandler.state != null ? FlightInputHandler.state.mainThrottle : 0f;
                CurrentStage = ActiveVessel.currentStage;

                float now = Time.unscaledTime;
                int currentParts = ActiveVessel.parts != null ? ActiveVessel.parts.Count : 0;
                bool needEngineScan = (_cachedEngines == null || _lastEngineScanPartCount != currentParts || _lastEngineScanStage != CurrentStage || (now - _lastEngineScanTime) >= 0.15f);

                if (needEngineScan)
                {
                    _lastEngineScanTime = now;
                    _lastEngineScanPartCount = currentParts;
                    _lastEngineScanStage = CurrentStage;

                    double currentResource = 0.0;
                    double maxResource = 0.0;
                    double thrust = 0.0;
                    int engineCount = 0;
                    string detectedProp = "PROP";

                    if (_cachedEngines == null || _cachedEngines.Count == 0 || _lastEngineScanPartCount != currentParts)
                    {
                        _cachedEngines = ActiveVessel.FindPartModulesImplementing<ModuleEngines>();
                    }

                    if (_cachedEngines != null)
                    {
                        for (int i = 0; i < _cachedEngines.Count; i++)
                        {
                            ModuleEngines eng = _cachedEngines[i];
                            if (eng != null && eng.isOperational)
                            {
                                thrust += eng.finalThrust;
                                engineCount++;
                                Part p = eng.part;
                                if (p != null && p.Resources != null)
                                {
                                    for (int r = 0; r < p.Resources.Count; r++)
                                    {
                                        PartResource res = p.Resources[r];
                                        if (res != null && res.info != null)
                                        {
                                            string rName = res.info.name;
                                            if (rName == "LiquidFuel" || rName == "SolidFuel" || rName == "Propellant" ||
                                                rName.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                rName.IndexOf("Methane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                rName == "Oxidizer" || rName == "XenonGas")
                                            {
                                                currentResource += res.amount;
                                                maxResource += res.maxAmount;
                                                if (!string.IsNullOrEmpty(res.info.displayName))
                                                    detectedProp = res.info.displayName;
                                                else
                                                    detectedProp = rName;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    ActiveEngines = engineCount;
                    StagePropellantFraction = maxResource > 0.001 ? (float)(currentResource / maxResource) : 1.0f;
                    StagePropellantName = detectedProp;
                    _cachedTotalThrust = thrust;

                    // 多级 ΔV 与烧燃时序遥测 (Tier 1: MechJeb -> Tier 2: Stock VesselDeltaV -> Tier 3: KER/单级)
                    bool dvFound = false;
                    MFPProfiler.BeginSample(ProfilerSection.Probes);
                    try
                    {
                        if (MechJebProbe.TryGetStageStats(out List<StageDeltaVInfo> mjStages, out double mjTotDv, out double mjTotTime))
                        {
                            StageDeltaVList = mjStages;
                            TotalDeltaV = mjTotDv;
                            TotalBurnTime = mjTotTime;
                            DeltaVSource = "MJ";

                            StageDeltaVInfo active = mjStages.Find(s => s.IsActive);
                            if (active.Stage >= 0)
                            {
                                StageDeltaV = active.DeltaV;
                                StageBurnTime = active.BurnTime;
                            }
                            else if (mjStages.Count > 0)
                            {
                                StageDeltaV = mjStages[0].DeltaV;
                                StageBurnTime = mjStages[0].BurnTime;
                            }
                            dvFound = true;
                        }
                    }
                    finally
                    {
                        MFPProfiler.EndSample(ProfilerSection.Probes);
                    }

                    if (!dvFound && ActiveVessel != null && ActiveVessel.VesselDeltaV != null)
                {
                    var vdv = ActiveVessel.VesselDeltaV;
                    var stockStages = new List<StageDeltaVInfo>();
                    if (vdv.OperatingStageInfo != null)
                    {
                        int curStg = ActiveVessel.currentStage;
                        for (int i = 0; i < vdv.OperatingStageInfo.Count; i++)
                        {
                            var si = vdv.OperatingStageInfo[i];
                            if (si != null)
                            {
                                stockStages.Add(new StageDeltaVInfo(
                                    si.stage,
                                    si.deltaVActual,
                                    si.stageBurnTime,
                                    si.TWRActual,
                                    si.ispActual,
                                    si.stage == curStg
                                ));
                            }
                        }
                    }
                    if (stockStages.Count > 0)
                    {
                        stockStages.Sort((a, b) => b.Stage.CompareTo(a.Stage));
                        StageDeltaVList = stockStages;
                        TotalDeltaV = vdv.TotalDeltaVActual;
                        TotalBurnTime = vdv.TotalBurnTime;
                        DeltaVSource = "STOCK";

                        StageDeltaVInfo active = stockStages.Find(s => s.IsActive);
                        if (active.Stage >= 0)
                        {
                            StageDeltaV = active.DeltaV;
                            StageBurnTime = active.BurnTime;
                        }
                        else
                        {
                            StageDeltaV = stockStages[0].DeltaV;
                            StageBurnTime = stockStages[0].BurnTime;
                        }
                        dvFound = true;
                    }
                }

                if (!dvFound)
                {
                    if (!double.IsNaN(KerbalEngineerProbe.StageDeltaV) && KerbalEngineerProbe.StageDeltaV > 0)
                    {
                        StageDeltaV = KerbalEngineerProbe.StageDeltaV;
                        DeltaVSource = "KER";
                    }
                    else if (!double.IsNaN(MechJebProbe.StageDeltaV) && MechJebProbe.StageDeltaV > 0)
                    {
                        StageDeltaV = MechJebProbe.StageDeltaV;
                        DeltaVSource = "MJ";
                    }
                }
                } // end if (needEngineScan)

                double gee = ActiveVessel.mainBody != null ? ActiveVessel.mainBody.GeeASL * 9.80665 : 9.80665;
                double weight = ActiveVessel.totalMass * gee;
                TWR = weight > 0.001 ? _cachedTotalThrust / weight : 0.0;
            }
            catch (Exception) { }
        }

        private void UpdateOrbitalParameters()
        {
            Orbit orbit = ActiveVessel.orbit;
            if (orbit != null)
            {
                Apoapsis = double.IsNaN(orbit.ApA) ? 0.0 : orbit.ApA;
                Periapsis = double.IsNaN(orbit.PeA) ? 0.0 : orbit.PeA;
                TimeToAp = double.IsNaN(orbit.timeToAp) ? 0.0 : orbit.timeToAp;
                TimeToPe = double.IsNaN(orbit.timeToPe) ? 0.0 : orbit.timeToPe;
            }
        }

        private void UpdateFlightControls()
        {
            if (ActiveVessel != null)
            {
                if (ActiveVessel.ActionGroups != null)
                {
                    IsRCSEnabled = ActiveVessel.ActionGroups[KSPActionGroup.RCS];
                    IsSASEnabled = ActiveVessel.ActionGroups[KSPActionGroup.SAS];
                }

                if (ActiveVessel.Autopilot != null)
                {
                    CurrentSASMode = (FlightSASMode)(int)ActiveVessel.Autopilot.Mode;
                }

                FlightCtrlState ctrl = ActiveVessel.ctrlState;
                PitchInput = Mathf.Clamp(ctrl.pitch, -1f, 1f);
                RollInput = Mathf.Clamp(ctrl.roll, -1f, 1f);
                YawInput = Mathf.Clamp(ctrl.yaw, -1f, 1f);
                PitchTrim = Mathf.Clamp(ctrl.pitchTrim, -1f, 1f);
                RollTrim = Mathf.Clamp(ctrl.rollTrim, -1f, 1f);
                YawTrim = Mathf.Clamp(ctrl.yawTrim, -1f, 1f);
            }

            if (FlightInputHandler.fetch != null)
            {
                IsStageLocked = FlightInputHandler.fetch.stageLock;
                IsPrecisionControl = FlightInputHandler.fetch.precisionMode;
            }

            if (FlightUIModeController.Instance != null)
            {
                IsDockingMode = (FlightUIModeController.Instance.Mode == FlightUIMode.DOCKING);
            }
        }

        private void UpdateSubsystemTelemetry()
        {
            try
            {
                Vessel v = ActiveVessel;
                if (v == null || v.parts == null) return;

                int partCount = v.parts.Count;
                float now = Time.unscaledTime;
                bool partsChanged = (partCount != _lastSubsystemPartCount);
                if (partsChanged) _lastSubsystemPartCount = partCount;

                // 1. 电气系统
                double curEc = 0.0, maxEc = 0.0;
                for (int i = 0; i < v.parts.Count; i++)
                {
                    Part p = v.parts[i];
                    if (p != null && p.Resources != null)
                    {
                        for (int r = 0; r < p.Resources.Count; r++)
                        {
                            PartResource res = p.Resources[r];
                            if (res != null && res.resourceName == "ElectricCharge")
                            {
                                curEc += res.amount;
                                maxEc += res.maxAmount;
                            }
                        }
                    }
                }
                ElectricCharge = curEc;
                MaxElectricCharge = maxEc;

                float dt = now - _lastEcTime;
                if (dt >= 0.15f)
                {
                    double instantRate = (curEc - _lastEc) / dt;
                    NetEcRate = Mathf.Lerp((float)NetEcRate, (float)instantRate, 0.4f);
                    _lastEc = curEc;
                    _lastEcTime = now;
                }
                float ecPct = maxEc > 0.001 ? (float)(curEc / maxEc) : 1.0f;
                BusVoltage = 22.0f + 6.2f * Mathf.Clamp01(ecPct);

                // 太阳能 (节流缓存)
                if (partsChanged || _cachedSolarPanels == null || (now - _lastPanelScanTime) >= 1.0f)
                {
                    _lastPanelScanTime = now;
                    _cachedSolarPanels = v.FindPartModulesImplementing<ModuleDeployableSolarPanel>();
                }
                double sol = 0.0;
                if (_cachedSolarPanels != null)
                {
                    for (int i = 0; i < _cachedSolarPanels.Count; i++)
                    {
                        if (_cachedSolarPanels[i] != null && _cachedSolarPanels[i].flowRate > 0.0001)
                            sol += _cachedSolarPanels[i].flowRate;
                    }
                }
                SolarPower = sol;

                // 2. 通信网络与链路 (RealAntennas / Stock CommNet - 节流 5 Hz)
                if (partsChanged || (now - _lastCommScanTime) >= 0.2f)
                {
                    _lastCommScanTime = now;
                    MFPProfiler.BeginSample(ProfilerSection.Probes);
                    try
                    {
                        if (RealAntennasProbe.IsAvailable)
                        {
                            var raLinks = RealAntennasProbe.GetActiveCommLinks();
                            ActiveCommLinks = raLinks;
                            AntennaCount = (int)RealAntennasProbe.ResolveNumeric("AntennaCount");
                            if (AntennaCount <= 0)
                            {
                                if (partsChanged || (now - _lastAntennaScanTime) >= 1.0f)
                                {
                                    _lastAntennaScanTime = now;
                                    var transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();
                                    _cachedAntennaCount = transmitters != null ? transmitters.Count : 0;
                                }
                                AntennaCount = _cachedAntennaCount;
                            }
                            IsConnected = RealAntennasProbe.ResolveNumeric("IsConnectedHome") > 0.5;
                            CommSignal = RealAntennasProbe.ResolveNumeric("SignalStrength");
                            if (double.IsNaN(CommSignal)) CommSignal = v.Connection != null ? v.Connection.SignalStrength : 1.0;
                            SignalTx = CommSignal;
                            SignalRx = CommSignal;
                            DataRateBps = RealAntennasProbe.ResolveNumeric("ActiveDataRate");
                            DirectLinkTarget = RealAntennasProbe.ResolveString("TargetName");
                            ControlLevelStr = !IsConnected ? "NO LINK" : (CommSignal < 0.35 ? "WEAK LINK" : "FULL CONTROL");
                        }
                        else
                        {
                            if (v.Connection != null)
                            {
                                IsConnected = v.Connection.IsConnected;
                                CommSignal = v.Connection.SignalStrength;
                                SignalTx = CommSignal;
                                SignalRx = CommSignal;
                                ControlLevelStr = !IsConnected ? "NO LINK" : (CommSignal < 0.35 ? "WEAK LINK" : "FULL CONTROL");

                                var links = new List<CommLinkInfo>();
                                if (v.Connection.ControlPath != null && v.Connection.ControlPath.Count > 0)
                                {
                                    foreach (var link in v.Connection.ControlPath)
                                    {
                                        if (link != null && link.end != null)
                                        {
                                            string pName = link.end.displayName ?? link.end.name;
                                            float qual = Mathf.Clamp01((float)link.strengthAR);
                                            links.Add(new CommLinkInfo(pName, 100000.0 * qual, qual, link.end.isHome));
                                        }
                                    }
                                }
                                ActiveCommLinks = links;
                                if (links.Count > 0) DirectLinkTarget = links[0].PeerName;
                                else DirectLinkTarget = IsConnected ? "KERBIN DSN" : "NONE";
                            }
                            if (partsChanged || (now - _lastAntennaScanTime) >= 1.0f)
                            {
                                _lastAntennaScanTime = now;
                                var transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();
                                _cachedAntennaCount = transmitters != null ? transmitters.Count : 0;
                            }
                            AntennaCount = _cachedAntennaCount;
                        }
                    }
                    finally
                    {
                        MFPProfiler.EndSample(ProfilerSection.Probes);
                    }
                }

                // 3. 乘员与居住舱
                CrewCount = v.GetCrewCount();
                CrewCapacity = v.GetCrewCapacity();
                CabinPressure = v.staticPressurekPa > 0.01 ? v.staticPressurekPa : 101.3;
                CabinTemp = v.externalTemperature;
            }
            catch (Exception) { }
        }

        // 时间加速控制回调
        public void IncreaseTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.IncreaseTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(TimeWarp.CurrentRateIndex + 1, false);
            }
        }

        public void DecreaseTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.DecreaseTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(TimeWarp.CurrentRateIndex - 1, false);
            }
        }

        public void CancelTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.CancelTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(0, false);
            }
        }

        public void TogglePause()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.TogglePause();
                return;
            }
            if (FlightGlobals.ready)
            {
                if (PauseMenu.isOpen) PauseMenu.Close();
                else PauseMenu.Display();
            }
        }

        public void SetTimeWarpRateIndex(int index)
        {
            if (IsSimulationMode)
            {
                SimulationEngine.SetTimeWarpRateIndex(index);
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(index, false);
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
        }

        public void CycleAltitudeMode()
        {
            CurrentAltMode = (CurrentAltMode == AltitudeDisplayMode.Ground) ? AltitudeDisplayMode.Sea : AltitudeDisplayMode.Ground;
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
