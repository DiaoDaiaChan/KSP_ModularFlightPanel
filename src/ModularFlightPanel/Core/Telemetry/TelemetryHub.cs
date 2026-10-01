using System;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{

    /// <summary>
    /// 统一机载遥测中枢 (Demand-Driven Reactive Telemetry Hub)
    /// 核心架构革新：
    /// 1. 按需惰性解算 (Lazy Pull-Based Evaluation)：未被 UI 组件或通配符引用的遥测数据绝不参与计算，彻底杜绝全量轮询。
    /// 2. 帧内去重与时钟合并 (Frame-Coalesced Quantization)：单帧内若多个组件并发读取同一遥测域，仅执行一次物理提取，其余调用零延迟直接返回缓存。
    /// 3. 分域模块化分流 (Domain Subsystem Separation)：拆分为 Dynamics / Propulsion / OrbitManeuver / Subsystems / Controls 五大专责领域。
    /// 4. 稳态事件驱动 (Event-Driven Topology Cache)：在滑行或稳态下彻底休眠部件网络遍历，由 KSP 原生事件精准唤醒。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public partial class TelemetryHub : MonoBehaviour, IFlightTelemetry
    {
        private static TelemetryHub _instance;
        public static TelemetryHub Instance => _instance;

        public Vessel ActiveVessel => FlightGlobals.ActiveVessel;
        public string VesselName => ActiveVessel != null ? ActiveVessel.vesselName : (IsSimulationMode ? SimulationEngine.VesselName : "NO VESSEL");
        private CelestialBody _lastMainBody;
        private string _cachedCelestialBodyName = "UNKNOWN";

        public string CelestialBodyName
        {
            get
            {
                if (IsSimulationMode) return "KERBIN";
                var vessel = ActiveVessel;
                if (vessel != null && vessel.mainBody != null)
                {
                    if (vessel.mainBody != _lastMainBody)
                    {
                        _lastMainBody = vessel.mainBody;
                        _cachedCelestialBodyName = vessel.mainBody.displayName.LocalizeRemoveGender();
                    }
                    return _cachedCelestialBodyName;
                }
                return "UNKNOWN";
            }
        }

        public string FlightSituation
        {
            get
            {
                if (IsSimulationMode) return "SUB_ORBITAL";
                var vessel = ActiveVessel;
                if (vessel == null) return "LANDED";
                switch (vessel.situation)
                {
                    case Vessel.Situations.LANDED: return "LANDED";
                    case Vessel.Situations.SPLASHED: return "SPLASHED";
                    case Vessel.Situations.PRELAUNCH: return "PRELAUNCH";
                    case Vessel.Situations.FLYING: return "FLYING";
                    case Vessel.Situations.SUB_ORBITAL: return "SUB_ORBITAL";
                    case Vessel.Situations.ORBITING: return "ORBITING";
                    case Vessel.Situations.ESCAPING: return "ESCAPING";
                    case Vessel.Situations.DOCKED: return "DOCKED";
                    default: return "LANDED";
                }
            }
        }

        // 仿真测试模式开关与引擎
        public bool IsSimulationMode { get; set; } = false;
        public TelemetrySimulationEngine SimulationEngine { get; } = new TelemetrySimulationEngine();

        public bool HasVessel => IsSimulationMode || (ActiveVessel != null && ActiveVessel.loaded && ActiveVessel.state != Vessel.State.DEAD);

        // 时间加速与时钟遥测
        public double MissionTime => IsSimulationMode ? SimulationEngine.MissionTime : (ActiveVessel != null ? ActiveVessel.missionTime : 0.0);
        public double UniversalTime => IsSimulationMode ? SimulationEngine.UniversalTime : Planetarium.GetUniversalTime();
        public float TimeWarpRate => IsSimulationMode ? SimulationEngine.TimeWarpRate : (TimeWarp.fetch != null ? TimeWarp.CurrentRate : 1.0f);
        public int TimeWarpRateIndex => IsSimulationMode ? SimulationEngine.TimeWarpRateIndex : (TimeWarp.fetch != null ? TimeWarp.CurrentRateIndex : 0);
        public int MaxTimeWarpRateIndex => IsSimulationMode ? SimulationEngine.MaxTimeWarpRateIndex : (TimeWarp.fetch != null ? (TimeWarp.WarpMode == TimeWarp.Modes.LOW ? TimeWarp.fetch.physicsWarpRates.Length - 1 : TimeWarp.fetch.warpRates.Length - 1) : 7);
        public bool IsPhysicsWarp => IsSimulationMode ? SimulationEngine.IsPhysicsWarp : (TimeWarp.WarpMode == TimeWarp.Modes.LOW);
        public bool IsGamePaused => IsSimulationMode ? SimulationEngine.IsGamePaused : (FlightGlobals.ready && (Time.timeScale == 0f || PauseMenu.isOpen));

        // P2 & P3: Event-Driven Topology Invalidation & Target Kinematics
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
            TriggerStageSeparationEvent();
            if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }

        private void OnPartUndocked(Part p)
        {
            _vesselTopologyDirty = true;
            TriggerStageSeparationEvent();
        }

        private void OnPartDecoupled(Part p)
        {
            _vesselTopologyDirty = true;
            TriggerStageSeparationEvent();
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

            Transform refT = ActiveVessel.ReferenceTransform != null ? ActiveVessel.ReferenceTransform : ActiveVessel.transform;
            Vector3 worldRelPos = targetT.position - refT.position;
            Vector3 localRelPos = refT.InverseTransformDirection(worldRelPos);
            Vector3 worldRelVel = (ActiveVessel.obt_velocity - target.GetObtVelocity()).xzy;
            Vector3 localRelVel = refT.InverseTransformDirection(worldRelVel);
            Quaternion relRot = Quaternion.Inverse(refT.rotation) * targetT.rotation;

            return CacheManager.Instance.GetOrExtrapolateTargetState(true, localRelPos, localRelVel, relRot, Time.time);
        }

        // 目标交会对接遥测 (Target & Docking Telemetry)
        public bool HasTarget => IsSimulationMode ? SimulationEngine.HasTarget : (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null);
        public string TargetName => IsSimulationMode ? SimulationEngine.TargetName : (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null ? FlightGlobals.fetch.VesselTarget.GetName() : "NO TARGET");
        public double TargetDistance => IsSimulationMode ? SimulationEngine.TargetDistance : GetTargetKinematics().Distance;
        public Vector3 TargetRelativePosition => IsSimulationMode ? SimulationEngine.TargetRelativePosition : GetTargetKinematics().RelativePosition;
        public Vector3 TargetRelativeVelocity => IsSimulationMode ? SimulationEngine.TargetRelativeVelocity : GetTargetKinematics().RelativeVelocity;
        public float TargetDeviationX => IsSimulationMode ? SimulationEngine.TargetDeviationX : GetTargetKinematics().DeviationX;
        public float TargetDeviationY => IsSimulationMode ? SimulationEngine.TargetDeviationY : GetTargetKinematics().DeviationY;
        public float TargetDeviationZ => IsSimulationMode ? SimulationEngine.TargetDeviationZ : GetTargetKinematics().DeviationZ;
        public float TargetClosingSpeed => IsSimulationMode ? SimulationEngine.TargetClosingSpeed : GetTargetKinematics().ClosingSpeed;
        public float TargetPitchAlignment => IsSimulationMode ? SimulationEngine.TargetPitchAlignment : GetTargetKinematics().PitchAlignment;
        public float TargetRollAlignment => IsSimulationMode ? SimulationEngine.TargetRollAlignment : GetTargetKinematics().RollAlignment;
        public float TargetYawAlignment => IsSimulationMode ? SimulationEngine.TargetYawAlignment : GetTargetKinematics().YawAlignment;

        private void Awake()
        {
            _instance = this;
            FlightTelemetryContext.FallbackProvider = () => Instance;

            // 监听载具拓扑与分级/分离事件，驱动 P2 引擎与推进剂缓存按需重算
            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onStageActivate.Add(OnStageActivated);
            GameEvents.onVesselChange.Add(OnVesselChanged);
            GameEvents.onPartUndock.Add(OnPartUndocked);
            GameEvents.onPartDeCouple.Add(OnPartDecoupled);
            GameEvents.StageManager.OnGUIStageSequenceModified.Add(OnStageSequenceModified);
            GameEvents.StageManager.OnGUIStageAdded.Add(OnStageAdded);
            GameEvents.StageManager.OnGUIStageRemoved.Add(OnStageRemoved);

            // 初始化统一探针中枢与场景搜索排队调度器
            ProbeManager.Instance.InitializeAll();

            ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleSpeedModeAction = () => Instance?.CycleSpeedMode();
            ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleAltitudeModeAction = () => Instance?.CycleAltitudeMode();
            ModularFlightPanel.UI.Widgets.Gauges.ArcTapeWidget.OnCycleSpeedModeAction = () => Instance?.CycleSpeedMode();
            ModularFlightPanel.UI.Widgets.Gauges.ArcTapeWidget.OnCycleAltitudeModeAction = () => Instance?.CycleAltitudeMode();
            ModularFlightPanel.UI.Widgets.Navigation.ReferenceFrameWidget.OnCycleReferenceFrameAction = () => Instance?.CycleSpeedMode();
            ModularFlightPanel.UI.Widgets.Navigation.ReferenceFrameWidget.OnToggleReferenceFrameWindowAction = () =>
            {
                if (PrincipiaProbe.IsAvailable)
                    PrincipiaProbe.ToggleReferenceFrameWindow();
                else
                    Instance?.CycleSpeedMode();
            };
            ModularFlightPanel.UI.Widgets.HeadingArcWidget.OnCycleHeadingModeAction = () => Instance?.CycleSpeedMode();
            ModularFlightPanel.UI.Widgets.HeadingArcWidget.OnToggleReferenceFrameWindowAction = () =>
            {
                if (PrincipiaProbe.IsAvailable)
                    PrincipiaProbe.ToggleReferenceFrameWindow();
                else
                    Instance?.CycleSpeedMode();
            };
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
            GameEvents.onPartUndock.Remove(OnPartUndocked);
            GameEvents.onPartDeCouple.Remove(OnPartDecoupled);
            GameEvents.StageManager.OnGUIStageSequenceModified.Remove(OnStageSequenceModified);
            GameEvents.StageManager.OnGUIStageAdded.Remove(OnStageAdded);
            GameEvents.StageManager.OnGUIStageRemoved.Remove(OnStageRemoved);

            if (_instance == this)
            {
                _instance = null;
                FlightTelemetryContext.FallbackProvider = null;
                ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleSpeedModeAction = null;
                ModularFlightPanel.UI.Widgets.TapeGaugeWidget.OnCycleAltitudeModeAction = null;
                ModularFlightPanel.UI.Widgets.Gauges.ArcTapeWidget.OnCycleSpeedModeAction = null;
                ModularFlightPanel.UI.Widgets.Gauges.ArcTapeWidget.OnCycleAltitudeModeAction = null;
                ModularFlightPanel.UI.Widgets.BottomControlsWidget.OnTogglePrincipiaWindowAction = null;
            }
        }

        private void OnStageSequenceModified()
        {
            InvalidateStagePartIcons();
            if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
        private void OnStageAdded(int stg)
        {
            InvalidateStagePartIcons();
            if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }
        private void OnStageRemoved(int stg)
        {
            InvalidateStagePartIcons();
            if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
            {
                StockUIHider.HideStockBottomLeft(true);
            }
        }

        /// <summary>
        /// 主生命周期 Tick：仅更新瞬态计时器与仿真时钟。
        /// 彻底移除每帧全量被动物理轮询（物理耗时降至趋近 0.001ms，所有遥测计算 100% 移交按需惰性拉取）。
        /// </summary>
        private void Update()
        {
            if (MFPProfiler.IsMasterBypassed) return;

            // 瞬态动画计时器更新 (< 0.001 ms)
            if (_stageSepTimer > 0f)
            {
                _stageSepTimer -= Time.unscaledDeltaTime;
                if (_stageSepTimer <= 0f) IsStageSeparating = false;
            }
            if (_engineIgnTimer > 0f)
            {
                _engineIgnTimer -= Time.unscaledDeltaTime;
                if (_engineIgnTimer <= 0f) IsEngineIgniting = false;
            }

            if (IsSimulationMode)
            {
                SimulationEngine.Update(Time.deltaTime);
            }
        }
    }
}
