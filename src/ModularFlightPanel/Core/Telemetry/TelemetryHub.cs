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
    [DefaultExecutionOrder(-500)]
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
        public int TotalStageEngines { get; private set; } = 0;

        // 轨道动力学与机动节点
        public double Apoapsis { get; private set; } = 0.0;
        public double Periapsis { get; private set; } = 0.0;
        public double TimeToAp { get; private set; } = 0.0;
        public double TimeToPe { get; private set; } = 0.0;

        public bool HasManeuverNode { get; private set; } = false;
        public double ManeuverDeltaV { get; private set; } = 0.0;
        public double ManeuverTotalDeltaV { get; private set; } = 0.0;
        public double ManeuverTimeToNode { get; private set; } = 0.0;
        public double ManeuverBurnTime { get; private set; } = 0.0;
        public double ManeuverTimeToBurn { get; private set; } = 0.0;
        public double ManeuverDeltaVPrograde { get; private set; } = 0.0;
        public double ManeuverDeltaVNormal { get; private set; } = 0.0;
        public double ManeuverDeltaVRadial { get; private set; } = 0.0;
        public string ManeuverSource { get; private set; } = "STANDBY";

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
        public IReadOnlyList<AntennaTelemetryInfo> Antennas { get; private set; } = Array.Empty<AntennaTelemetryInfo>();

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
                    double targetUT = node.UT - (ManeuverBurnTime * 0.5) - 15.0;
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
        private readonly List<StageDeltaVInfo> _cachedStockStages = new List<StageDeltaVInfo>(16);
        private readonly List<CommLinkInfo> _cachedStockCommLinks = new List<CommLinkInfo>(8);
        private readonly List<AntennaTelemetryInfo> _cachedAntennasList = new List<AntennaTelemetryInfo>(8);
        private static readonly Comparison<StageDeltaVInfo> CompareStageDescending = (a, b) => b.Stage.CompareTo(a.Stage);
        private readonly Dictionary<int, List<StagePartIconData>> _cachedStagePartIcons = new Dictionary<int, List<StagePartIconData>>();
        private float _lastStageIconScanTime = -10f;
        private static System.Reflection.FieldInfo _stageIconImageField;

        private void RefreshAntennasList(Vessel v)
        {
            _cachedAntennasList.Clear();
            if (v == null) return;
            try
            {
                var transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();
                if (transmitters != null && transmitters.Count > 0)
                {
                    for (int i = 0; i < transmitters.Count; i++)
                    {
                        var t = transmitters[i];
                        if (t == null) continue;

                        string antName = (t.part != null && t.part.partInfo != null) ? t.part.partInfo.title : (t.part != null ? t.part.name : "Antenna");
                        string typeStr = t.antennaType.ToString().ToUpperInvariant();
                        double pwr = t.antennaPower;
                        string pwrStr;
                        if (pwr >= 1000000000.0) pwrStr = $"{(pwr / 1000000000.0):F1}G";
                        else if (pwr >= 1000000.0) pwrStr = $"{(pwr / 1000000.0):F1}M";
                        else if (pwr >= 1000.0) pwrStr = $"{(pwr / 1000.0):F1}k";
                        else pwrStr = $"{pwr:F0}";

                        bool isOperational = true;
                        string status = IsConnected ? "LINKED" : "SEARCHING";
                        float sig = (float)CommSignal;

                        if (t.part != null)
                        {
                            var dep = t.part.FindModuleImplementing<ModuleDeployableAntenna>();
                            if (dep != null)
                            {
                                if (dep.deployState == ModuleDeployablePart.DeployState.RETRACTED)
                                {
                                    status = "RETRACTED";
                                    sig = 0f;
                                    isOperational = false;
                                }
                                else if (dep.deployState == ModuleDeployablePart.DeployState.EXTENDING ||
                                         dep.deployState == ModuleDeployablePart.DeployState.RETRACTING)
                                {
                                    status = "DEPLOYING";
                                    sig = 0f;
                                }
                                else if (dep.deployState == ModuleDeployablePart.DeployState.BROKEN)
                                {
                                    status = "BROKEN";
                                    sig = 0f;
                                    isOperational = false;
                                }
                            }
                        }

                        _cachedAntennasList.Add(new AntennaTelemetryInfo(antName, typeStr, pwr, pwrStr, sig, status, isOperational));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RefreshAntennasList warning: {ex.Message}");
            }

            if (_cachedAntennasList.Count == 0)
            {
                string status = IsConnected ? "LINKED" : "NO LINK";
                _cachedAntennasList.Add(new AntennaTelemetryInfo("INTERNAL ANTENNA", "INTERNAL", 5000.0, "5.0k", (float)CommSignal, status, true));
            }
            Antennas = _cachedAntennasList;
        }

        private IReadOnlyList<StagePartIconData> GetStagePartIcons(int stageNum)
        {
            float now = Time.unscaledTime;
            if (now - _lastStageIconScanTime > 1.0f)
            {
                _lastStageIconScanTime = now;
                RefreshStagePartIcons();
            }
            if (_cachedStagePartIcons.TryGetValue(stageNum, out var list))
            {
                return list;
            }
            return Array.Empty<StagePartIconData>();
        }

        private void RefreshStagePartIcons()
        {
            _cachedStagePartIcons.Clear();
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null && KSP.UI.Screens.StageManager.Instance.Stages != null)
                {
                    var mgrStages = KSP.UI.Screens.StageManager.Instance.Stages;
                    for (int i = 0; i < mgrStages.Count; i++)
                    {
                        var grp = mgrStages[i];
                        if (grp == null) continue;
                        int stg = grp.defaultStage;
                        var grpIcons = grp.Icons;
                        if (grpIcons == null || grpIcons.Count == 0) continue;

                        if (!_cachedStagePartIcons.TryGetValue(stg, out var iconList))
                        {
                            iconList = new List<StagePartIconData>();
                            _cachedStagePartIcons[stg] = iconList;
                        }

                        for (int j = 0; j < grpIcons.Count; j++)
                        {
                            var icon = grpIcons[j];
                            if (icon == null) continue;

                            string typeStr = icon.iconType.ToString();
                            int typeIdx = (int)icon.iconType;
                            int count = 1;
                            if (icon.groupedIcons != null && icon.groupedIcons.Count > 0)
                            {
                                count += icon.groupedIcons.Count;
                            }

                            string partTitle = string.Empty;
                            string propName = null;
                            float propFrac = -1f;

                            if (icon.Part != null)
                            {
                                partTitle = icon.Part.partInfo != null ? icon.Part.partInfo.title : icon.Part.name;
                                if (icon.Part.Resources != null)
                                {
                                    var res = icon.Part.Resources;
                                    for (int r = 0; r < res.Count; r++)
                                    {
                                        var resItem = res[r];
                                        if (resItem != null && resItem.maxAmount > 0)
                                        {
                                            string rName = resItem.resourceName;
                                            if (rName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                propName = "Solid Fuel";
                                                propFrac = (float)(resItem.amount / resItem.maxAmount);
                                                break;
                                            }
                                            else if (rName.IndexOf("Liquid", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                propName = "Liquid Fuel";
                                                propFrac = (float)(resItem.amount / resItem.maxAmount);
                                                break;
                                            }
                                            else if (rName.IndexOf("Propellant", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                propName = "Mono";
                                                propFrac = (float)(resItem.amount / resItem.maxAmount);
                                            }
                                        }
                                    }
                                }
                            }

                            Rect uv = default;
                            bool hasUv = false;
                            if (_stageIconImageField == null)
                            {
                                _stageIconImageField = typeof(KSP.UI.Screens.StageIcon).GetField("iconImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                            }
                            var rawImg = _stageIconImageField?.GetValue(icon) as UnityEngine.UI.RawImage;
                            if (rawImg != null)
                            {
                                Rect rUv = rawImg.uvRect;
                                if (rUv.width > 0.01f && rUv.width < 0.5f && rUv.height > 0.01f && rUv.height < 0.5f)
                                {
                                    uv = rUv;
                                    hasUv = true;
                                }
                            }

                            int existingIdx = iconList.FindIndex(p => p.IconType == typeStr);
                            if (existingIdx >= 0)
                            {
                                var exist = iconList[existingIdx];
                                exist.Count += count;
                                if (propFrac >= 0 && exist.PropellantFraction < 0)
                                {
                                    exist.PropellantName = propName;
                                    exist.PropellantFraction = propFrac;
                                }
                                iconList[existingIdx] = exist;
                            }
                            else
                            {
                                iconList.Add(new StagePartIconData(typeStr, typeIdx, count, partTitle, propName, propFrac, uv, hasUv));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RefreshStagePartIcons warning: {ex.Message}");
            }
        }

        private static int _cachedEcDefId = -1;
        private static bool _lookedUpEcDefId = false;

        private static int GetEcDefinitionId()
        {
            if (!_lookedUpEcDefId)
            {
                _lookedUpEcDefId = true;
                if (PartResourceLibrary.Instance != null)
                {
                    var def = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
                    if (def != null) _cachedEcDefId = def.id;
                }
            }
            return _cachedEcDefId;
        }

        #region P2 & P3: Event-Driven Topology Invalidation & Target Kinematics

        private bool _vesselTopologyDirty = true;
        private float _lastLoggedThrottle = -1f;

        private void OnVesselModified(Vessel v)
        {
            if (v == ActiveVessel)
            {
                _vesselTopologyDirty = true;
            }
        }

        private void OnStageActivated(int stage)
        {
            _vesselTopologyDirty = true;
        }

        private void OnVesselChanged(Vessel v)
        {
            _vesselTopologyDirty = true;
            _cachedEngines = null;
            _cachedSolarPanels = null;
            _cachedAntennaCount = 0;
            CacheManager.Instance.ClearTransient();
        }

        /// <summary>
        /// P3: 获取或外推目标交会相对几何状态 (15Hz 物理采样 + 帧间平滑一阶外推)
        /// </summary>
        public CacheManager.TargetKinematicState GetTargetKinematics()
        {
            if (ActiveVessel == null || FlightGlobals.fetch == null || FlightGlobals.fetch.VesselTarget == null)
            {
                return CacheManager.Instance.GetOrExtrapolateTargetState(false, Vector3.zero, Vector3.zero, Quaternion.identity, Time.time);
            }

            var target = FlightGlobals.fetch.VesselTarget;
            Transform targetT = target.GetTransform();
            if (targetT == null)
            {
                return CacheManager.Instance.GetOrExtrapolateTargetState(false, Vector3.zero, Vector3.zero, Quaternion.identity, Time.time);
            }

            Vector3 relPos = targetT.position - ActiveVessel.transform.position;
            Vector3 relVel = (ActiveVessel.obt_velocity - target.GetObtVelocity()).xzy;
            Quaternion relRot = Quaternion.Inverse(ActiveVessel.transform.rotation) * targetT.rotation;

            return CacheManager.Instance.GetOrExtrapolateTargetState(true, relPos, relVel, relRot, Time.time);
        }

        #endregion

        private void Awake()
        {
            _instance = this;
            FlightTelemetryContext.FallbackProvider = () => Instance;

            // 监听载具拓扑与分级事件，驱动 P2 引擎与推进剂缓存按需重算
            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onStageActivate.Add(OnStageActivated);
            GameEvents.onVesselChange.Add(OnVesselChanged);

            // 初始化统一探针中枢与场景搜索排队调度器
            ProbeManager.Instance.InitializeAll();

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
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onStageActivate.Remove(OnStageActivated);
            GameEvents.onVesselChange.Remove(OnVesselChanged);

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
                UpdateManeuverParameters();
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
            TotalStageEngines = sim.TotalStageEngines;

            Apoapsis = sim.Apoapsis;
            Periapsis = sim.Periapsis;
            TimeToAp = sim.TimeToAp;
            TimeToPe = sim.TimeToPe;

            HasManeuverNode = sim.HasManeuverNode;
            ManeuverDeltaV = sim.ManeuverDeltaV;
            ManeuverTotalDeltaV = sim.ManeuverTotalDeltaV;
            ManeuverTimeToNode = sim.ManeuverTimeToNode;
            ManeuverBurnTime = sim.ManeuverBurnTime;
            ManeuverTimeToBurn = sim.ManeuverTimeToBurn;

            ElectricCharge = sim.ElectricCharge;
            MaxElectricCharge = sim.MaxElectricCharge;
            NetEcRate = sim.NetEcRate;
            BusVoltage = sim.BusVoltage;
            SolarPower = sim.SolarPower;

            CommSignal = sim.CommSignal;
            IsConnected = sim.IsConnected;
            ControlLevelStr = sim.ControlLevelStr;
            AntennaCount = sim.AntennaCount;
            ActiveCommLinks = sim.ActiveCommLinks;
            Antennas = sim.Antennas;

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

            // 动压 (Q)：优先读 ActiveVessel.dynamicPressurekPa，若在 FAR 下为 0 则尝试从 FarProbe 获取
            double q = ActiveVessel.dynamicPressurekPa;
            if (q <= 0.00001 && FarProbe.IsAvailable)
            {
                double farQ = FarProbe.DynamicPressure;
                if (!double.IsNaN(farQ) && farQ > 0.0) q = farQ;
            }
            DynamicPressure = q;

            // 环境大气压强解算：优先从 FARC (Ferram Aerospace Research) 探针解算，次选原生 staticPressurekPa，最后回退至天体大气模型 GetPressure
            double atm = double.NaN;
            if (FarProbe.IsAvailable)
            {
                atm = FarProbe.GetAtmosphericPressureAtm(ActiveVessel);
            }

            // Fallback 原版 KSP：若 FAR 未安装、解算返回 NaN 或处于大气层内但取值为 0，触发原版多级保底
            bool inAtmosphere = ActiveVessel.mainBody != null && ActiveVessel.mainBody.atmosphere && ActiveVessel.altitude < ActiveVessel.mainBody.atmosphereDepth;
            if (double.IsNaN(atm) || (atm <= 0.0 && inAtmosphere))
            {
                if (ActiveVessel.staticPressurekPa > 0.0)
                {
                    atm = ActiveVessel.staticPressurekPa / 101.325;
                }
                else if (inAtmosphere)
                {
                    // 应对 launchpad / pre-launch / rails / physics unready 等 staticPressurekPa 尚未刷新的场景，直读天体大气物理模型
                    double staticKpa = ActiveVessel.mainBody.GetPressure(ActiveVessel.altitude);
                    atm = (staticKpa > 0.0) ? (staticKpa / 101.325) : 0.0;
                }
                else
                {
                    atm = 0.0;
                }
            }

            AtmosphericPressure = !double.IsNaN(atm) ? Math.Max(0.0, atm) : 0.0;
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
                bool throttleChanged = Mathf.Abs(Throttle - _lastLoggedThrottle) > 0.005f;
                bool partsChanged = _lastEngineScanPartCount != currentParts;
                bool stageChanged = _lastEngineScanStage != CurrentStage;

                // P2: 事件驱动与稳态滑行解算缓存
                // 仅在分级激活、部件拓扑改变、节流阀变动，或推力产生时的定时刷新周期重算
                float allowedInterval = (Throttle > 0.001f) ? 0.35f : 2.0f;
                bool needEngineScan = _vesselTopologyDirty || _cachedEngines == null || partsChanged || stageChanged ||
                                      throttleChanged || (now - _lastEngineScanTime) >= allowedInterval;

                if (needEngineScan)
                {
                    _lastEngineScanTime = now;
                    _lastEngineScanPartCount = currentParts;
                    _lastEngineScanStage = CurrentStage;
                    _lastLoggedThrottle = Throttle;

                    // 稳态 0 油门滑行快速路径：推力直接置 0，且拓扑未变时保留发动机与推进剂比例，跳过全船部件遍历
                    if (!_vesselTopologyDirty && !partsChanged && !stageChanged && Throttle <= 0.001f && _cachedEngines != null && _cachedEngines.Count > 0)
                    {
                        _cachedTotalThrust = 0.0;
                    }
                    else
                    {

                    double currentResource = 0.0;
                    double maxResource = 0.0;
                    double thrust = 0.0;
                    int engineCount = 0;
                    string detectedProp = "PROP";

                        if (_vesselTopologyDirty || _cachedEngines == null || _cachedEngines.Count == 0 || _lastEngineScanPartCount != currentParts)
                        {
                            _cachedEngines = ActiveVessel.FindPartModulesImplementing<ModuleEngines>();
                            _vesselTopologyDirty = false;
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

                    int stageTotalEngines = 0;
                    if (_cachedEngines != null)
                    {
                        for (int i = 0; i < _cachedEngines.Count; i++)
                        {
                            ModuleEngines eng = _cachedEngines[i];
                            if (eng != null)
                            {
                                if (eng.part != null && (eng.part.inverseStage == CurrentStage || eng.isOperational))
                                    stageTotalEngines++;
                                else if (eng.isOperational)
                                    stageTotalEngines++;
                            }
                        }
                        if (stageTotalEngines == 0) stageTotalEngines = _cachedEngines.Count;
                    }

                    ActiveEngines = engineCount;
                    TotalStageEngines = stageTotalEngines > 0 ? stageTotalEngines : (engineCount > 0 ? engineCount : 1);
                    StagePropellantFraction = maxResource > 0.001 ? (float)(currentResource / maxResource) : 1.0f;
                    StagePropellantName = detectedProp;
                    _cachedTotalThrust = thrust;
                    }

                    // 多级 ΔV 与烧燃时序遥测 (Tier 1: MechJeb -> Tier 2: Stock VesselDeltaV -> Tier 3: KER/单级)
                    bool dvFound = false;
                    MFPProfiler.BeginSample(ProfilerSection.Probes);
                    try
                    {
                        if (MechJebProbe.TryGetStageStats(out List<StageDeltaVInfo> mjStages, out double mjTotDv, out double mjTotTime))
                        {
                            for (int m = 0; m < mjStages.Count; m++)
                            {
                                var s = mjStages[m];
                                if (s.PartIcons == null || s.PartIcons.Count == 0)
                                {
                                    var icons = GetStagePartIcons(s.Stage);
                                    if (icons != null && icons.Count > 0)
                                    {
                                        mjStages[m] = new StageDeltaVInfo(s.Stage, s.DeltaV, s.BurnTime, s.TWR, s.Isp, s.IsActive, icons);
                                    }
                                }
                            }
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
                        _cachedStockStages.Clear();
                        if (vdv.OperatingStageInfo != null)
                        {
                            int curStg = ActiveVessel.currentStage;
                            for (int i = 0; i < vdv.OperatingStageInfo.Count; i++)
                            {
                                var si = vdv.OperatingStageInfo[i];
                                if (si != null)
                                {
                                    var partIcons = GetStagePartIcons(si.stage);
                                    _cachedStockStages.Add(new StageDeltaVInfo(
                                        si.stage,
                                        si.deltaVActual,
                                        si.stageBurnTime,
                                        si.TWRActual,
                                        si.ispActual,
                                        si.stage == curStg,
                                        partIcons
                                    ));
                                }
                            }
                        }
                        if (_cachedStockStages.Count > 0)
                        {
                            _cachedStockStages.Sort(CompareStageDescending);
                            StageDeltaVList = _cachedStockStages;
                            TotalDeltaV = vdv.TotalDeltaVActual;
                            TotalBurnTime = vdv.TotalBurnTime;
                            DeltaVSource = "STOCK";

                            StageDeltaVInfo active = _cachedStockStages.Find(s => s.IsActive);
                            if (active.Stage >= 0)
                            {
                                StageDeltaV = active.DeltaV;
                                StageBurnTime = active.BurnTime;
                            }
                            else
                            {
                                StageDeltaV = _cachedStockStages[0].DeltaV;
                                StageBurnTime = _cachedStockStages[0].BurnTime;
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

                // 1. Principia 飞行计划优先 (高保真 N 体数值积分机动)
                if (PrincipiaProbe.IsAvailable && PrincipiaProbe.HasActiveFlightPlan)
                {
                    double pDv = PrincipiaProbe.ManeuverDeltaV;
                    if (!double.IsNaN(pDv) && pDv > 0.001)
                    {
                        HasManeuverNode = true;
                        ManeuverSource = "PRINCIPIA";
                        ManeuverDeltaV = pDv;
                        ManeuverTotalDeltaV = pDv;
                        double pDur = PrincipiaProbe.ManeuverDuration;
                        ManeuverBurnTime = (double.IsNaN(pDur) || pDur < 0.0) ? 0.0 : pDur;
                        double pTime = PrincipiaProbe.TimeToManeuver;
                        ManeuverTimeToNode = (double.IsNaN(pTime) || pTime < 0.0) ? 0.0 : pTime;
                        ManeuverTimeToBurn = ManeuverTimeToNode;

                        if (PrincipiaProbe.TryGetManeuverVector(out double pro, out double norm, out double rad))
                        {
                            ManeuverDeltaVPrograde = pro;
                            ManeuverDeltaVNormal = norm;
                            ManeuverDeltaVRadial = rad;
                        }
                        else
                        {
                            ManeuverDeltaVPrograde = pDv;
                            ManeuverDeltaVNormal = 0.0;
                            ManeuverDeltaVRadial = 0.0;
                        }
                        return;
                    }
                }

                // 2. 原版 PatchedConicSolver 机动节点 (开普勒两体圆锥拼接)
                if (v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    var node = v.patchedConicSolver.maneuverNodes[0];
                    if (node != null)
                    {
                        Vector3d burnVec = node.GetBurnVector(node.patch ?? v.orbit);
                        HasManeuverNode = true;
                        ManeuverSource = "STOCK";
                        ManeuverDeltaV = double.IsNaN(burnVec.magnitude) ? 0.0 : burnVec.magnitude;
                        ManeuverTotalDeltaV = node.DeltaV != null ? node.DeltaV.magnitude : ManeuverDeltaV;
                        if (double.IsNaN(ManeuverTotalDeltaV)) ManeuverTotalDeltaV = ManeuverDeltaV;

                        double ut = Planetarium.GetUniversalTime();
                        ManeuverTimeToNode = node.UT - ut;

                        // 燃烧时长解算：F = m * a -> a = F/m -> t = dV / a
                        double burnDur = 0.0;
                        double thrust = _cachedTotalThrust;
                        double mass = v.totalMass;
                        if (thrust > 0.1 && mass > 0.01)
                        {
                            double accel = thrust / mass;
                            burnDur = (ManeuverDeltaV > 0.01 ? ManeuverDeltaV : ManeuverTotalDeltaV) / accel;
                        }
                        ManeuverBurnTime = (double.IsNaN(burnDur) || burnDur < 0.0) ? 0.0 : burnDur;
                        ManeuverTimeToBurn = ManeuverTimeToNode - (ManeuverBurnTime * 0.5);

                        // 三轴矢量解算 (Prograde, Normal, Radial)
                        // KSP node.DeltaV: x=Radial, y=Normal, z=Prograde
                        if (node.patch != null && !double.IsNaN(burnVec.magnitude) && burnVec.magnitude > 0.01)
                        {
                            Vector3d proDir = node.patch.getOrbitalVelocityAtUT(node.UT).normalized;
                            Vector3d nrmDir = node.patch.GetOrbitNormal().normalized;
                            Vector3d radDir = Vector3d.Cross(nrmDir, proDir).normalized;

                            ManeuverDeltaVPrograde = Vector3d.Dot(burnVec, proDir);
                            ManeuverDeltaVNormal = Vector3d.Dot(burnVec, nrmDir);
                            ManeuverDeltaVRadial = Vector3d.Dot(burnVec, radDir);
                        }
                        else if (node.DeltaV != null)
                        {
                            ManeuverDeltaVRadial = node.DeltaV.x;
                            ManeuverDeltaVNormal = node.DeltaV.y;
                            ManeuverDeltaVPrograde = node.DeltaV.z;
                        }
                        else
                        {
                            ManeuverDeltaVPrograde = ManeuverDeltaV;
                            ManeuverDeltaVNormal = 0.0;
                            ManeuverDeltaVRadial = 0.0;
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
                        HasManeuverNode = true;
                        ManeuverSource = "MECHJEB";
                        ManeuverDeltaV = mjDv;
                        ManeuverTotalDeltaV = mjDv;
                        double mjDur = MechJebProbe.ResolveNumeric("NODEBURNTIME");
                        ManeuverBurnTime = (double.IsNaN(mjDur) || mjDur < 0.0) ? 0.0 : mjDur;
                        double mjTime = MechJebProbe.ResolveNumeric("TIMETONODE");
                        ManeuverTimeToNode = (double.IsNaN(mjTime) || mjTime < 0.0) ? 0.0 : mjTime;
                        ManeuverTimeToBurn = ManeuverTimeToNode - (ManeuverBurnTime * 0.5);

                        ManeuverDeltaVPrograde = mjDv;
                        ManeuverDeltaVNormal = 0.0;
                        ManeuverDeltaVRadial = 0.0;
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
            HasManeuverNode = false;
            ManeuverSource = "STANDBY";
            ManeuverDeltaV = 0.0;
            ManeuverTotalDeltaV = 0.0;
            ManeuverTimeToNode = 0.0;
            ManeuverBurnTime = 0.0;
            ManeuverTimeToBurn = 0.0;
            ManeuverDeltaVPrograde = 0.0;
            ManeuverDeltaVNormal = 0.0;
            ManeuverDeltaVRadial = 0.0;
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

                // 统一探针中枢调度更新 (包含 Principia, GPWS, FAR, Trajectories, DPAI, MJ 等 15 大探针)
                ProbeManager.Instance.UpdateAllProbes(v, this);

                int partCount = v.parts.Count;
                float now = Time.unscaledTime;
                bool partsChanged = (partCount != _lastSubsystemPartCount);
                if (partsChanged) _lastSubsystemPartCount = partCount;

                // 1. 电气系统 (优先通过 KSP 原生内部资源总线直取，0 堆分配与 0 循环开销)
                double curEc = 0.0, maxEc = 0.0;
                int ecId = GetEcDefinitionId();
                bool gotTotals = false;

                if (ecId >= 0)
                {
                    try
                    {
                        v.GetConnectedResourceTotals(ecId, out curEc, out maxEc);
                        gotTotals = (maxEc > 0.0001);
                    }
                    catch { gotTotals = false; }
                }

                if (!gotTotals)
                {
                    // 回退方案：仅在 Native API 未返回时执行部件级扫描，使用整数 ID 比对杜绝字符串值比较
                    for (int i = 0; i < v.parts.Count; i++)
                    {
                        Part p = v.parts[i];
                        if (p != null && p.Resources != null)
                        {
                            for (int r = 0; r < p.Resources.Count; r++)
                            {
                                PartResource res = p.Resources[r];
                                if (res != null && (ecId >= 0 ? (res.info != null && res.info.id == ecId) : (res.resourceName == "ElectricCharge")))
                                {
                                    curEc += res.amount;
                                    maxEc += res.maxAmount;
                                }
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
                                    RefreshAntennasList(v);
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

                                _cachedStockCommLinks.Clear();
                                if (v.Connection.ControlPath != null && v.Connection.ControlPath.Count > 0)
                                {
                                    foreach (var link in v.Connection.ControlPath)
                                    {
                                        if (link != null && link.end != null)
                                        {
                                            string pName = link.end.displayName ?? link.end.name;
                                            float qual = Mathf.Clamp01((float)link.strengthAR);
                                            _cachedStockCommLinks.Add(new CommLinkInfo(pName, 100000.0 * qual, qual, link.end.isHome));
                                        }
                                    }
                                }
                                ActiveCommLinks = _cachedStockCommLinks;
                                if (_cachedStockCommLinks.Count > 0) DirectLinkTarget = _cachedStockCommLinks[0].PeerName;
                                else DirectLinkTarget = IsConnected ? "KERBIN DSN" : "NONE";
                            }
                            if (partsChanged || (now - _lastAntennaScanTime) >= 1.0f)
                            {
                                _lastAntennaScanTime = now;
                                var transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();
                                _cachedAntennaCount = transmitters != null ? transmitters.Count : 0;
                                RefreshAntennasList(v);
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
