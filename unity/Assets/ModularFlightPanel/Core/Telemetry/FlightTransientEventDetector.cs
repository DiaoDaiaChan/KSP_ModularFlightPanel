using System;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Core.Telemetry
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) - 独立飞行瞬态事件检测器 (Flight Transient Event Detector)
    /// ====================================================================================
    /// 核心职能：
    /// 1. 独立解耦：将航电全景飞行瞬态事件状态机从组件视图中剥离为标准中枢服务；
    /// 2. 接入 CacheManager：全量输出同帧快照 (FlightEventSnapshot) 与探针防重缓存，多组件并发共享 0 重复推演；
    /// 3. 动态物理门控与死区粗筛：平稳巡航阶段阻断 90% 的极端动力学与跨模组探针无谓空转；
    /// 4. 零 GC 零装箱：100% 遵照 MFP 工业级航电开发规范。
    /// </summary>
    public class FlightTransientEventDetector
    {
        private static FlightTransientEventDetector _instance;
        public static FlightTransientEventDetector Instance => _instance ?? (_instance = new FlightTransientEventDetector());

        private const float EVAL_INTERVAL = 0.20f; // 5Hz 稳态巡航节拍器 (状态突变时 0 延迟即时触发)
        private float _lastEvalTime = -10f;

        // 历史遥测状态机缓存 (用于边沿触发判定)
        private int _lastStage = -1;
        private int _lastActiveEngines = -1;
        private float _lastThrottle = -1f;
        private bool _lastIsStageSeparating = false;
        private bool _lastIsEngineIgniting = false;
        private double _lastTimeToNode = -1.0;
        private double _lastPeriapsis = -999999.0;
        private double _lastEffectivePe = -999999.0;
        private double _lastEffectiveAp = -999999.0;
        private double _lastAltitude = 0.0;
        private double _lastTimeToAp = -1.0;
        private double _lastTimeToPe = -1.0;
        private bool _lastIsDockingMode = false;
        private bool _lastIsLanded = false;
        private double _lastAltitudeAGL = 0.0;
        private string _lastCelestialBody = string.Empty;
        private string _lastFlightSituation = string.Empty;
        private bool _lastManeuverBurnTriggered = false;

        // 外部探针与高阶动力学边缘触发历史
        private double _peakDynamicPressure = 0.0;
        private bool _maxQTriggered = false;
        private double _lastSurfaceSpeed = 0.0;
        private bool _lastInStorm = false;
        private int _lastLockLevel = -1;
        private double _lastTti = -1.0;
        private double _lastDockDist = -1.0;
        private bool _lastTfFailed = false;
        private double _lastShOverheat = 0.0;

        // 大气边界缓存
        private string _cachedAtmoBody = null;
        private double _cachedAtmoVal = 70000.0;

        // 当前活跃快照
        private CacheManager.FlightEventSnapshot _currentSnapshot;

        public CacheManager.FlightEventSnapshot CurrentSnapshot => _currentSnapshot;

        /// <summary>
        /// 核心事件检测管线：优先命中 CacheManager 当前帧快照；平飞时物理门控拦截，发生突变或到达 10Hz 节拍时执行快速判定。
        /// </summary>
        public CacheManager.FlightEventSnapshot DetectEvents(IFlightTelemetry telem, float now, int frame)
        {
            // 1. 优先命中 CacheManager 当前帧快照（多组件同帧零重复推演）
            if (CacheManager.Instance.TryGetCachedFlightEventSnapshot(frame, out var cached))
            {
                return cached;
            }

            if (telem == null || !telem.HasVessel)
            {
                _currentSnapshot = default;
                _currentSnapshot.Frame = frame;
                _currentSnapshot.Timestamp = now;
                _currentSnapshot.HasVessel = false;
                _currentSnapshot.TriggeredEvent = FlightTransientEventType.None;
                CacheManager.Instance.SetCachedFlightEventSnapshot(ref _currentSnapshot);
                return _currentSnapshot;
            }

            // 2. 物理粗筛前置守卫：分级、引擎点火、油门跳变或机动 T-60s 边沿判定
            string curBody = telem.CelestialBodyName;
            bool bodyChanged = !string.IsNullOrEmpty(curBody) && !object.ReferenceEquals(curBody, _lastCelestialBody) && !curBody.Equals(_lastCelestialBody, StringComparison.OrdinalIgnoreCase);

            bool stateChanged = telem.CurrentStage != _lastStage ||
                                telem.ActiveEngines != _lastActiveEngines ||
                                telem.IsStageSeparating != _lastIsStageSeparating ||
                                telem.IsEngineIgniting != _lastIsEngineIgniting ||
                                (telem.Throttle > 0.05f != _lastThrottle > 0.05f) ||
                                (telem.HasManeuverNode && telem.ManeuverTimeToNode <= 60.0 && (_lastTimeToNode > 60.0 || _lastTimeToNode < 0.0)) ||
                                (telem.FlightSituation != _lastFlightSituation) ||
                                bodyChanged;

            // 稳态静默旁路 (Quiescent Steady-State Fast-Path)：
            // 在巡航轨道或发射台静止待命且无发动机动力学扰动时，瞬态事件物理不可能发生，直接复用快照（0.001ms 纳秒级直通）
            bool isSteadyOrbit = telem.FlightSituation == "ORBITING" && !telem.HasManeuverNode && telem.ActiveEngines == 0 && telem.Throttle <= 0.001f;
            bool isSteadyPad = (telem.FlightSituation == "LANDED" || telem.FlightSituation == "PRELAUNCH") && telem.SurfaceSpeed < 0.5 && telem.ActiveEngines == 0 && telem.Throttle <= 0.001f;
            bool isQuiescent = (isSteadyOrbit || isSteadyPad) && !stateChanged && _currentSnapshot.HasVessel;

            // 若处于稳态静默期，或未到 10Hz 判定节拍且无状态突变，直接复用快照
            if (isQuiescent || ((now - _lastEvalTime) < EVAL_INTERVAL && !stateChanged && _currentSnapshot.HasVessel))
            {
                _currentSnapshot.Frame = frame;
                _currentSnapshot.Timestamp = now;
                _currentSnapshot.TriggeredEvent = FlightTransientEventType.None;
                CacheManager.Instance.SetCachedFlightEventSnapshot(ref _currentSnapshot);
                return _currentSnapshot;
            }

            _lastEvalTime = now;

            // 3. 计算关键轨道动力学与大气边界参数
            double atmoCutoff = ResolveAtmosphereCutoff(telem, frame);
            double effectivePe = ResolveEffectivePeriapsis(telem, frame);
            double effectiveAp = ResolveEffectiveApoapsis(telem, frame);

            FlightTransientEventType triggeredEvent = FlightTransientEventType.None;

            // ── A. 分级分离判定 ──
            if (_lastStage != -1)
            {
                bool sepSignal = telem.IsStageSeparating && !_lastIsStageSeparating;
                bool stageDropped = telem.CurrentStage < _lastStage;
                if (sepSignal || stageDropped)
                {
                    triggeredEvent = FlightTransientEventType.Separation;
                }
            }

            // ── B. 引擎点火启动判定 ──
            if (triggeredEvent == FlightTransientEventType.None && _lastActiveEngines != -1)
            {
                bool ignSignal = telem.IsEngineIgniting && !_lastIsEngineIgniting;
                bool engStarted = (_lastActiveEngines == 0 && telem.ActiveEngines > 0 && telem.Throttle > 0.02f) ||
                                  (_lastThrottle <= 0.001f && telem.Throttle > 0.05f && telem.ActiveEngines > 0);
                if (ignSignal || engStarted)
                {
                    triggeredEvent = FlightTransientEventType.EngineStart;
                }
                else
                {
                    // ── C. 主发关机 MECO 判定 ──
                    bool mecoCutoff = (_lastActiveEngines > 0 && telem.ActiveEngines == 0 &&
                                       (telem.FlightSituation == "FLYING" || telem.FlightSituation == "SUB_ORBITAL" || telem.FlightSituation == "ORBITING"));
                    bool throttleCut = (_lastThrottle > 0.25f && telem.Throttle <= 0.001f && telem.ActiveEngines > 0 &&
                                        telem.VerticalSpeed > 10.0 && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH");
                    if (mecoCutoff || throttleCut)
                    {
                        triggeredEvent = FlightTransientEventType.MECO;
                    }
                }
            }

            // ── D. 接近机动节点 (T-60s) 判定 ──
            if (triggeredEvent == FlightTransientEventType.None && telem.HasManeuverNode && telem.ManeuverTimeToNode > 0.0 && telem.ManeuverTimeToNode <= 60.0)
            {
                if (_lastTimeToNode > 60.0 || _lastTimeToNode < 0.0)
                {
                    triggeredEvent = FlightTransientEventType.ManeuverApproach;
                }
            }

            // ── E. 机动点火执行 BURN 判定 ──
            if (triggeredEvent == FlightTransientEventType.None && telem.HasManeuverNode && telem.ManeuverTimeToNode <= 2.0 && telem.Throttle > 0.05f)
            {
                if (!_lastManeuverBurnTriggered)
                {
                    _lastManeuverBurnTriggered = true;
                    triggeredEvent = FlightTransientEventType.ManeuverBurn;
                }
            }
            else if (!telem.HasManeuverNode || telem.Throttle <= 0.01f)
            {
                _lastManeuverBurnTriggered = false;
            }

            // ── F. 入轨圆化完成 ORBIT STABLE 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                _lastEffectivePe > -999999.0 && _lastEffectivePe < atmoCutoff && effectivePe >= atmoCutoff && effectiveAp >= atmoCutoff &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                triggeredEvent = FlightTransientEventType.OrbitAchieved;
            }

            // ── G. 飞船离轨制动 DEORBIT 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                _lastEffectivePe >= atmoCutoff && _lastEffectiveAp >= atmoCutoff && effectivePe < atmoCutoff && effectivePe > -9000000.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                triggeredEvent = FlightTransientEventType.Deorbit;
            }

            // ── H. 逃逸轨道建立 ESCAPE 判定 ──
            bool isEscapingNow = (telem.FlightSituation == "ESCAPING") || (effectiveAp < 0 && effectiveAp > -9000000.0);
            bool wasEscapingBefore = (_lastFlightSituation == "ESCAPING") || (_lastEffectiveAp < 0 && _lastEffectiveAp > -9000000.0);
            if (triggeredEvent == FlightTransientEventType.None && !wasEscapingBefore && isEscapingNow && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                triggeredEvent = FlightTransientEventType.Escape;
            }

            // ── I. 穿越天体引力范围 (SOI Transition) 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                !string.IsNullOrEmpty(_lastCelestialBody) && !string.IsNullOrEmpty(telem.CelestialBodyName) &&
                !_lastCelestialBody.Equals(telem.CelestialBodyName, StringComparison.OrdinalIgnoreCase))
            {
                triggeredEvent = FlightTransientEventType.SoiTransition;
            }

            // ── J. 再入/进入大气层 ATMOSPHERE ENTRY 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                atmoCutoff > 0.0 && _lastAltitude >= atmoCutoff && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed < -5.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                triggeredEvent = FlightTransientEventType.AtmosphereEntry;
            }

            // ── K. 再入等离子体黑障 (REENTRY BLACKOUT) 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.AltitudeASL > atmoCutoff * 0.35 && telem.Mach > 8.0 && telem.DynamicPressure > 12.0)
            {
                triggeredEvent = FlightTransientEventType.Blackout;
            }

            // ── L. 动力减速着陆点火 (SUICIDE / LANDING BURN) 判定 ──
            if (triggeredEvent == FlightTransientEventType.None &&
                telem.AltitudeAGL < 2000.0 && telem.AltitudeAGL > 15.0 && telem.VerticalSpeed < -15.0 && telem.Throttle > 0.40f && telem.ActiveEngines > 0)
            {
                triggeredEvent = FlightTransientEventType.SuicideBurn;
            }

            // ── M. 拱点穿越判定 (远拱点 Ap / 近拱点 Pe) ──
            if (triggeredEvent == FlightTransientEventType.None && effectivePe >= atmoCutoff)
            {
                if (_lastTimeToAp > 1.0 && telem.TimeToAp <= 1.0 && telem.TimeToAp >= 0.0)
                {
                    triggeredEvent = FlightTransientEventType.ApoapsisPass;
                }
                else if (_lastTimeToPe > 1.0 && telem.TimeToPe <= 1.0 && telem.TimeToPe >= 0.0)
                {
                    triggeredEvent = FlightTransientEventType.PeriapsisPass;
                }
            }

            // ── N. 对接模式进入判定 ──
            if (triggeredEvent == FlightTransientEventType.None && telem.IsDockingMode && !_lastIsDockingMode)
            {
                triggeredEvent = FlightTransientEventType.DockingMode;
            }

            // ── O. 着陆接地确认 TOUCHDOWN 判定 ──
            bool isLandedNow = (telem.FlightSituation == "LANDED" || telem.FlightSituation == "SPLASHED" || telem.IsTouchdownAlert);
            if (triggeredEvent == FlightTransientEventType.None && !_lastIsLanded && isLandedNow && _lastAltitudeAGL > 2.0)
            {
                triggeredEvent = FlightTransientEventType.Touchdown;
            }

            // ── P. 突破最大动压 MAX Q 判定 ──
            if (atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed > 10.0 &&
                (telem.FlightSituation == "FLYING" || telem.FlightSituation == "SUB_ORBITAL"))
            {
                if (telem.DynamicPressure > _peakDynamicPressure)
                {
                    _peakDynamicPressure = telem.DynamicPressure;
                }
                else if (!_maxQTriggered && _peakDynamicPressure >= 10.0 && telem.DynamicPressure <= (_peakDynamicPressure - 1.2) && telem.DynamicPressure > 2.0)
                {
                    _maxQTriggered = true;
                    if (triggeredEvent == FlightTransientEventType.None)
                    {
                        triggeredEvent = FlightTransientEventType.MaxQ;
                    }
                }
            }
            else if (telem.FlightSituation == "LANDED" || telem.FlightSituation == "PRELAUNCH" || telem.AltitudeASL >= atmoCutoff)
            {
                _maxQTriggered = false;
                _peakDynamicPressure = 0.0;
            }

            // ── Q. 起飞决断速度 V1 / 抬轮 ROTATE 判定 (GPWS) ──
            if (ExternalProbeRegistry.HasGPWS && triggeredEvent == FlightTransientEventType.None &&
                (telem.FlightSituation == "LANDED" || telem.AltitudeAGL < 8.0) && telem.SurfaceSpeed > 10.0 && telem.Throttle > 0.40f)
            {
                double v1 = ExternalProbeRegistry.ResolveNumeric("GPWS", "V1Speed");
                if (!double.IsNaN(v1) && v1 > 10.0 && _lastSurfaceSpeed < v1 && telem.SurfaceSpeed >= v1)
                {
                    triggeredEvent = FlightTransientEventType.V1Rotate;
                }
            }

            // ── R. 发动机故障失效 (TestFlight) ──
            if (ExternalProbeRegistry.HasTestFlight && triggeredEvent == FlightTransientEventType.None && telem.ActiveEngines > 0)
            {
                double tfFailed = ExternalProbeRegistry.ResolveNumeric("TF", "FAILED");
                bool isFailed = tfFailed > 0.5;
                if (isFailed && !_lastTfFailed)
                {
                    triggeredEvent = FlightTransientEventType.EngineFailure;
                }
                _lastTfFailed = isFailed;
            }
            else
            {
                _lastTfFailed = false;
            }

            // ── S. 航电失控锁定 (RP-1 Avionics) ──
            if (ExternalProbeRegistry.HasRP1 && triggeredEvent == FlightTransientEventType.None)
            {
                double lockLevel = ExternalProbeRegistry.ResolveNumeric("RP1", "LOCK_LEVEL");
                if (!double.IsNaN(lockLevel))
                {
                    int lvl = (int)lockLevel;
                    if (lvl == 0 && _lastLockLevel > 0)
                    {
                        triggeredEvent = FlightTransientEventType.AvionicsLock;
                    }
                    _lastLockLevel = lvl;
                }
            }

            // ── T. 预测地表撞击告警 (Trajectories) ──
            if (ExternalProbeRegistry.HasTrajectories && triggeredEvent == FlightTransientEventType.None &&
                telem.VerticalSpeed < -10.0 && telem.AltitudeAGL > 100.0 && telem.AltitudeAGL < 25000.0 && telem.FlightSituation != "LANDED")
            {
                double tti = ExternalProbeRegistry.ResolveNumeric("TRAJ", "TIMETOIMPACT");
                if (!double.IsNaN(tti) && tti > 0.0 && tti <= 30.0 && (_lastTti > 30.0 || _lastTti < 0.0))
                {
                    triggeredEvent = FlightTransientEventType.TerrainImpact;
                }
                _lastTti = tti;
            }
            else
            {
                _lastTti = -1.0;
            }

            // ── U. 太阳风暴/日冕物质抛射冲击 (Kerbalism) ──
            if (ExternalProbeRegistry.HasKerbalism && triggeredEvent == FlightTransientEventType.None &&
                (telem.AltitudeASL >= atmoCutoff || atmoCutoff <= 0.0))
            {
                double inStorm = ExternalProbeRegistry.ResolveNumeric("KLSM", "INSTORM");
                bool isStorm = inStorm > 0.5;
                if (isStorm && !_lastInStorm)
                {
                    triggeredEvent = FlightTransientEventType.SolarStorm;
                }
                _lastInStorm = isStorm;
            }
            else
            {
                _lastInStorm = false;
            }

            // ── V. 端口对接锁扣捕获 (DPAI) ──
            if (ExternalProbeRegistry.HasDocking && triggeredEvent == FlightTransientEventType.None &&
                (telem.IsDockingMode || telem.TargetDistance < 50.0))
            {
                double dockDist = ExternalProbeRegistry.ResolveNumeric("DOCK", "DISTANCE");
                if (!double.IsNaN(dockDist) && dockDist > 0.0 && dockDist <= 0.25 && _lastDockDist > 0.25)
                {
                    triggeredEvent = FlightTransientEventType.DockingCapture;
                }
                _lastDockDist = dockDist;
            }
            else
            {
                _lastDockDist = -1.0;
            }

            // ── W. 热回路超温紧急告警 (SystemHeat) ──
            if (ExternalProbeRegistry.HasSystemHeat && triggeredEvent == FlightTransientEventType.None)
            {
                double shRatio = ExternalProbeRegistry.ResolveNumeric("SH", "OVERHEATRATIO");
                if (!double.IsNaN(shRatio))
                {
                    if (shRatio >= 100.0 && _lastShOverheat < 100.0)
                    {
                        triggeredEvent = FlightTransientEventType.ThermalOverheat;
                    }
                    _lastShOverheat = shRatio;
                }
            }

            // 更新历史遥测缓存
            _lastSurfaceSpeed = telem.SurfaceSpeed;
            _lastStage = telem.CurrentStage;
            _lastActiveEngines = telem.ActiveEngines;
            _lastThrottle = telem.Throttle;
            _lastIsStageSeparating = telem.IsStageSeparating;
            _lastIsEngineIgniting = telem.IsEngineIgniting;
            _lastTimeToNode = telem.HasManeuverNode ? telem.ManeuverTimeToNode : -1.0;
            _lastPeriapsis = telem.Periapsis;
            _lastAltitude = telem.AltitudeASL;
            _lastTimeToAp = telem.TimeToAp;
            _lastTimeToPe = telem.TimeToPe;
            _lastIsDockingMode = telem.IsDockingMode;
            _lastIsLanded = isLandedNow;
            _lastAltitudeAGL = telem.AltitudeAGL;
            _lastEffectivePe = effectivePe;
            _lastEffectiveAp = effectiveAp;
            _lastCelestialBody = telem.CelestialBodyName;
            _lastFlightSituation = telem.FlightSituation;

            // 组装并写入 CacheManager 统一快照
            _currentSnapshot = new CacheManager.FlightEventSnapshot
            {
                Frame = frame,
                Timestamp = now,
                HasVessel = true,
                TriggeredEvent = triggeredEvent,
                EffectivePeriapsis = effectivePe,
                EffectiveApoapsis = effectiveAp,
                AtmosphereCutoff = atmoCutoff,
                CelestialBody = telem.CelestialBodyName,
                FlightSituation = telem.FlightSituation
            };

            CacheManager.Instance.SetCachedFlightEventSnapshot(ref _currentSnapshot);
            return _currentSnapshot;
        }

        private double ResolveEffectivePeriapsis(IFlightTelemetry telem, int frame)
        {
            if (ExternalProbeRegistry.HasPrincipia)
            {
                if (CacheManager.Instance.TryGetCachedProbeNumeric("PRINCIPIA_PE", frame, out double cachedPe))
                {
                    if (!double.IsNaN(cachedPe) && cachedPe > -9000000.0) return cachedPe;
                }
                else
                {
                    double pPe = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisPeriapsis");
                    if (double.IsNaN(pPe) || pPe <= -9000000.0)
                    {
                        pPe = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "PERIAPSIS");
                    }
                    CacheManager.Instance.SetCachedProbeNumeric("PRINCIPIA_PE", frame, pPe);
                    if (!double.IsNaN(pPe) && pPe > -9000000.0) return pPe;
                }
            }
            return telem.Periapsis;
        }

        private double ResolveEffectiveApoapsis(IFlightTelemetry telem, int frame)
        {
            if (ExternalProbeRegistry.HasPrincipia)
            {
                if (CacheManager.Instance.TryGetCachedProbeNumeric("PRINCIPIA_AP", frame, out double cachedAp))
                {
                    if (!double.IsNaN(cachedAp) && cachedAp > -9000000.0) return cachedAp;
                }
                else
                {
                    double pAp = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisApoapsis");
                    if (double.IsNaN(pAp) || pAp <= -9000000.0)
                    {
                        pAp = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "APOAPSIS");
                    }
                    CacheManager.Instance.SetCachedProbeNumeric("PRINCIPIA_AP", frame, pAp);
                    if (!double.IsNaN(pAp) && pAp > -9000000.0) return pAp;
                }
            }
            return telem.Apoapsis;
        }

        private double ResolveAtmosphereCutoff(IFlightTelemetry telem, int frame)
        {
            if (telem == null) return 70000.0;
            string curBody = telem.CelestialBodyName;
            if (curBody != null && curBody == _cachedAtmoBody) return _cachedAtmoVal;

            _cachedAtmoBody = curBody;

            if (telem.AtmosphereDepth > 0.0)
            {
                _cachedAtmoVal = telem.AtmosphereDepth;
                return _cachedAtmoVal;
            }

            if (!telem.HasAtmosphere)
            {
                _cachedAtmoVal = 0.0;
                return 0.0;
            }

            if (telem.AtmosphericPressure > 0.0001)
            {
                _cachedAtmoVal = 70000.0;
                return 70000.0;
            }

            _cachedAtmoVal = 0.0;
            return 0.0;
        }

        /// <summary>
        /// 切换场景或飞船时重置检测器状态机
        /// </summary>
        public void Reset()
        {
            _lastStage = -1;
            _lastActiveEngines = -1;
            _lastThrottle = -1f;
            _lastIsStageSeparating = false;
            _lastIsEngineIgniting = false;
            _lastTimeToNode = -1.0;
            _lastPeriapsis = -999999.0;
            _lastEffectivePe = -999999.0;
            _lastEffectiveAp = -999999.0;
            _lastAltitude = 0.0;
            _lastTimeToAp = -1.0;
            _lastTimeToPe = -1.0;
            _lastIsDockingMode = false;
            _lastIsLanded = false;
            _lastAltitudeAGL = 0.0;
            _lastCelestialBody = string.Empty;
            _lastFlightSituation = string.Empty;
            _lastManeuverBurnTriggered = false;
            _peakDynamicPressure = 0.0;
            _maxQTriggered = false;
            _lastSurfaceSpeed = 0.0;
            _lastInStorm = false;
            _lastLockLevel = -1;
            _lastTti = -1.0;
            _lastDockDist = -1.0;
            _lastTfFailed = false;
            _lastShOverheat = 0.0;
            _cachedAtmoBody = null;
            _currentSnapshot = default;
            _lastEvalTime = -10f;
        }
    }
}
