using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    public partial class TelemetryHub
    {
        // -------------------------------------------------------------
        // 油门、推力、推进剂与分级 ΔV (Propulsion & Staging) - 按需惰性解算与稳态节流
        // -------------------------------------------------------------
        private int _propulsionFrame = -1;
        private float _throttle = 0f;
        private float _stagePropellantFraction = 1.0f;
        private double _twr = 0.0;
        private int _currentStage = 0;
        private double _stageDeltaV = 0.0;
        private double _totalDeltaV = 0.0;
        private double _stageBurnTime = 0.0;
        private double _totalBurnTime = 0.0;
        private IReadOnlyList<StageDeltaVInfo> _stageDeltaVList = Array.Empty<StageDeltaVInfo>();
        private string _deltaVSource = "NONE";
        private int _activeEngines = 0;
        private int _totalStageEngines = 0;
        private string _stagePropellantName = "PROP";

        public float Throttle
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Throttle;
                EnsurePropulsionUpdated();
                return _throttle;
            }
        }

        public float StagePropellantFraction
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.StagePropellantFraction;
                EnsurePropulsionUpdated();
                return _stagePropellantFraction;
            }
        }

        public double TWR
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TWR;
                EnsurePropulsionUpdated();
                return _twr;
            }
        }

        public int CurrentStage
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CurrentStage;
                EnsurePropulsionUpdated();
                return _currentStage;
            }
        }

        public double StageDeltaV
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.StageDeltaV;
                EnsurePropulsionUpdated();
                return _stageDeltaV;
            }
        }

        public double TotalDeltaV
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TotalDeltaV;
                EnsurePropulsionUpdated();
                return _totalDeltaV;
            }
        }

        public double StageBurnTime
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.StageBurnTime;
                EnsurePropulsionUpdated();
                return _stageBurnTime;
            }
        }

        public double TotalBurnTime
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TotalBurnTime;
                EnsurePropulsionUpdated();
                return _totalBurnTime;
            }
        }

        public IReadOnlyList<StageDeltaVInfo> StageDeltaVList
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.StageDeltaVList;
                EnsurePropulsionUpdated();
                return _stageDeltaVList;
            }
        }

        public string DeltaVSource
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.DeltaVSource;
                EnsurePropulsionUpdated();
                return _deltaVSource;
            }
        }

        public int ActiveEngines
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ActiveEngines;
                EnsurePropulsionUpdated();
                return _activeEngines;
            }
        }

        public int TotalStageEngines
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.TotalStageEngines;
                EnsurePropulsionUpdated();
                return _totalStageEngines;
            }
        }

        // 逐台发动机快照 (异构集群支持)；仅在推进域刷新时重建，读取零分配
        private readonly List<EngineTelemetryInfo> _engineInfos = new List<EngineTelemetryInfo>(16);
        private static readonly IReadOnlyList<EngineTelemetryInfo> s_emptyEngines = new EngineTelemetryInfo[0];

        public IReadOnlyList<EngineTelemetryInfo> Engines
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Engines;
                EnsurePropulsionUpdated();
                return _engineInfos.Count > 0 ? _engineInfos : s_emptyEngines;
            }
        }

        /// <summary>
        /// 估算单台发动机的实时推进剂消耗率 (单位/秒)。
        /// 优先使用 KSP 原生推进剂流量比 (ratio)，退化时按 推力/(比冲·g0) 的质量流率换算，
        /// 保证任何引擎类型 (化学/核/电推) 都能给出可读的 FF 读数。
        /// </summary>
        private static float EstimateEngineFuelFlow(ModuleEngines eng)
        {
            if (eng == null || !eng.isOperational || eng.finalThrust <= 0.0001f) return 0f;
            try
            {
                float isp = eng.realIsp > 0.0001f ? eng.realIsp : eng.atmosphereCurve?.Evaluate(0f) ?? 0f;
                float g0 = 9.80665f;
                if (isp > 0.0001f)
                {
                    // 质量流率 (kg/s) = 推力(N) / (Isp · g0)
                    return eng.finalThrust / (isp * g0);
                }
            }
            catch
            {
                // 退化路径：无法获取比冲曲线时按额定推力的线性近似
            }
            return eng.maxThrust > 0.0001f ? eng.finalThrust / eng.maxThrust : 0f;
        }

        public string StagePropellantName
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.StagePropellantName;
                EnsurePropulsionUpdated();
                return _stagePropellantName;
            }
        }

        // 瞬态事件遥测 (分级分离与点火瞬态)
        public bool IsStageSeparating { get; private set; } = false;
        public bool IsEngineIgniting { get; private set; } = false;
        private float _stageSepTimer = 0f;
        private float _engineIgnTimer = 0f;
        private int _lastActiveEngines = -1;

        public void TriggerStageSeparationEvent()
        {
            IsStageSeparating = true;
            _stageSepTimer = 1.6f;
        }

        public void TriggerEngineIgnitionEvent()
        {
            IsEngineIgniting = true;
            _engineIgnTimer = 1.6f;
        }

        // 动力学推力与分级扫描缓存
        private float _lastEngineScanTime = -1f;
        private int _lastEngineScanPartCount = -1;
        private int _lastEngineScanStage = -1;
        private double _cachedTotalThrust = 0.0;
        private List<ModuleEngines> _cachedEngines = new List<ModuleEngines>();
        private readonly List<StageDeltaVInfo> _cachedStockStages = new List<StageDeltaVInfo>(16);
        private static readonly Comparison<StageDeltaVInfo> CompareStageDescending = (a, b) => b.Stage.CompareTo(a.Stage);
        private readonly Dictionary<int, List<StagePartIconData>> _cachedStagePartIcons = new Dictionary<int, List<StagePartIconData>>();
        private float _lastStageIconScanTime = -10f;
        private static System.Reflection.FieldInfo _stageIconImageField;
        private static System.Reflection.FieldInfo _stageIconProtoIconField;
        private static System.Reflection.FieldInfo _protoIconInfoBoxesField;
        private static System.Reflection.FieldInfo _stageGroupStageField;
        private static System.Reflection.FieldInfo _stageGroupUiStageIndexField;
        private static System.Reflection.FieldInfo _moduleEnginesPropellantGaugesField;

        private void EnsurePropulsionUpdated()
        {
            if (_propulsionFrame == Time.frameCount) return;
            _propulsionFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateThrottleAndPropellant();
        }

        private void UpdateThrottleAndPropellant()
        {
            try
            {
                _throttle = FlightInputHandler.state != null ? FlightInputHandler.state.mainThrottle : 0f;
                _currentStage = ActiveVessel.currentStage;

                float now = Time.unscaledTime;
                int currentParts = ActiveVessel.parts != null ? ActiveVessel.parts.Count : 0;
                bool throttleChanged = Mathf.Abs(_throttle - _lastLoggedThrottle) > 0.005f;
                bool partsChanged = _lastEngineScanPartCount != currentParts;
                bool stageChanged = _lastEngineScanStage != _currentStage;

                // 仅在分级激活、部件拓扑改变、节流阀变动，或推力产生时的定时刷新周期重算
                float allowedInterval = (_throttle > 0.001f) ? 0.35f : 2.0f;
                bool needEngineScan = _vesselTopologyDirty || _cachedEngines == null || partsChanged || stageChanged ||
                                      throttleChanged || (now - _lastEngineScanTime) >= allowedInterval;

                if (needEngineScan)
                {
                    _lastEngineScanTime = now;
                    _lastEngineScanPartCount = currentParts;
                    _lastEngineScanStage = _currentStage;
                    _lastLoggedThrottle = _throttle;

                    // 稳态 0 油门滑行快速路径
                    if (!_vesselTopologyDirty && !partsChanged && !stageChanged && _throttle <= 0.001f && _cachedEngines != null && _cachedEngines.Count > 0)
                    {
                        _cachedTotalThrust = 0.0;
                        // 保持逐台快照结构，仅推力/油耗归零，避免 UI 列数闪烁
                        for (int ei = 0; ei < _engineInfos.Count; ei++)
                        {
                            EngineTelemetryInfo z = _engineInfos[ei];
                            z.CurrentThrust = 0f;
                            z.FuelFlow = 0f;
                            z.CommandedThrottle = _throttle;
                            _engineInfos[ei] = z;
                        }
                    }
                    else
                    {
                        double currentResource = 0.0;
                        double maxResource = 0.0;
                        double thrust = 0.0;
                        int engineCount = 0;
                        string detectedProp = "PROP";
                        _engineInfos.Clear();

                        if (_vesselTopologyDirty || _cachedEngines == null || _cachedEngines.Count == 0 || _lastEngineScanPartCount != currentParts)
                        {
                            _cachedEngines = ActiveVessel.FindPartModulesImplementing<ModuleEngines>();
                            _vesselTopologyDirty = false;
                        }

                        float nativeGaugeSum = 0f;
                        int nativeGaugeCount = 0;

                        if (_cachedEngines != null)
                        {
                            for (int i = 0; i < _cachedEngines.Count; i++)
                            {
                                ModuleEngines eng = _cachedEngines[i];
                                if (eng != null && eng.isOperational)
                                {
                                    thrust += eng.finalThrust;
                                    engineCount++;

                                    // 逐台快照：真实推力 / 指令油门 / 油耗，供 EICAS 自适应渲染
                                    float partTempC = 0f;
                                    if (eng.part != null && eng.part.temperature > 0.1)
                                    {
                                        float rawK = (float)eng.part.temperature;
                                        partTempC = rawK > 100f ? (rawK - 273.15f) : rawK;
                                    }
                                    EngineTelemetryInfo info = new EngineTelemetryInfo
                                    {
                                        PartName = eng.part != null ? eng.part.partInfo?.title ?? eng.part.partName : "ENGINE",
                                        PropellantName = eng.propellants != null && eng.propellants.Count > 0
                                            ? (eng.propellants[0].displayName ?? eng.propellants[0].name) : "",
                                        CommandedThrottle = Mathf.Clamp01(eng.currentThrottle > 0.0001f ? eng.currentThrottle : _throttle),
                                        CurrentThrust = eng.finalThrust,
                                        MaxThrust = eng.maxThrust > 0.0001f ? eng.maxThrust : eng.finalThrust,
                                        FuelFlow = EstimateEngineFuelFlow(eng),
                                        IsOperational = true,
                                        PartTemperature = partTempC
                                    };
                                    _engineInfos.Add(info);

                                    // 1. Tier 1: Hook 原版及 Mod 推进剂进度条仪表 (ProtoStageIconInfo / PropellantGauges)
                                    bool hasNativeGauge = false;
                                    if (_moduleEnginesPropellantGaugesField == null)
                                    {
                                        _moduleEnginesPropellantGaugesField = typeof(ModuleEngines).GetField("PropellantGauges", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    }
                                    var gauges = _moduleEnginesPropellantGaugesField?.GetValue(eng) as Dictionary<Propellant, KSP.UI.Screens.ProtoStageIconInfo>;
                                    if (gauges != null && gauges.Count > 0)
                                    {
                                        foreach (var kvp in gauges)
                                        {
                                            var gBox = kvp.Value;
                                            if (gBox != null && gBox.pBarValue >= 0f)
                                            {
                                                nativeGaugeSum += Mathf.Clamp01(gBox.pBarValue);
                                                nativeGaugeCount++;
                                                hasNativeGauge = true;
                                                if (string.IsNullOrEmpty(detectedProp) || detectedProp == "PROP")
                                                {
                                                    detectedProp = !string.IsNullOrEmpty(gBox.pBarCaption) ? gBox.pBarCaption : (kvp.Key?.displayName ?? kvp.Key?.name);
                                                }
                                            }
                                        }
                                    }

                                    if (!hasNativeGauge && eng.part != null && eng.part.stackIcon != null)
                                    {
                                        if (_protoIconInfoBoxesField == null)
                                        {
                                            _protoIconInfoBoxesField = typeof(KSP.UI.Screens.ProtoStageIcon).GetField("infoBoxes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                        }
                                        var boxes = _protoIconInfoBoxesField?.GetValue(eng.part.stackIcon) as List<KSP.UI.Screens.ProtoStageIconInfo>;
                                        if (boxes != null && boxes.Count > 0)
                                        {
                                            for (int b = 0; b < boxes.Count; b++)
                                            {
                                                var box = boxes[b];
                                                if (box != null && box.pBarValue >= 0f)
                                                {
                                                    nativeGaugeSum += Mathf.Clamp01(box.pBarValue);
                                                    nativeGaugeCount++;
                                                    hasNativeGauge = true;
                                                    if (string.IsNullOrEmpty(detectedProp) || detectedProp == "PROP")
                                                    {
                                                        detectedProp = !string.IsNullOrEmpty(box.pBarCaption) ? box.pBarCaption : box.msg;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    // 2. Tier 2: 动态直连储箱拓扑换算 (强制 cache=false 穿透 B9PartSwitch/CryoTanks 动态缓存)
                                    if (eng.propellants != null && eng.propellants.Count > 0)
                                    {
                                        for (int pr = 0; pr < eng.propellants.Count; pr++)
                                        {
                                            var pDef = eng.propellants[pr];
                                            if (pDef == null || pDef.name == "IntakeAir" || pDef.name == "ElectricCharge") continue;

                                            double pAvail = 0, pCap = 0;
                                            if (eng.part != null)
                                            {
                                                eng.part.GetConnectedResourceTotals(pDef.id, pDef.GetFlowMode(), out pAvail, out pCap, false);
                                            }
                                            if (pCap <= 0.001)
                                            {
                                                pAvail = pDef.totalResourceAvailable;
                                                pCap = pDef.totalResourceCapacity;
                                            }

                                            if (pCap > 0.001)
                                            {
                                                currentResource += pAvail;
                                                maxResource += pCap;
                                                if (string.IsNullOrEmpty(detectedProp) || detectedProp == "PROP")
                                                    detectedProp = pDef.displayName ?? pDef.name;
                                            }
                                        }
                                    }
                                    else if (eng.part != null && eng.part.Resources != null)
                                    {
                                        Part p = eng.part;
                                        for (int r = 0; r < p.Resources.Count; r++)
                                        {
                                            PartResource res = p.Resources[r];
                                            if (res != null && res.info != null && res.maxAmount > 0.001)
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

                        // 储箱联通余量核验 (整船兜底)
                        if (nativeGaugeCount == 0 && (maxResource <= 0.001 || (currentResource <= 0.001 && maxResource > 0.001)) && ActiveVessel != null)
                        {
                            double curLf = 0.0, maxLf = 0.0;
                            double curOx = 0.0, maxOx = 0.0;
                            double curSf = 0.0, maxSf = 0.0;
                            double curMp = 0.0, maxMp = 0.0;
                            var lfDef = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition("LiquidFuel") : null;
                            var oxDef = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition("Oxidizer") : null;
                            var sfDef = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition("SolidFuel") : null;
                            var mpDef = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition("MonoPropellant") : null;
                            if (lfDef != null) ActiveVessel.GetConnectedResourceTotals(lfDef.id, out curLf, out maxLf);
                            if (oxDef != null) ActiveVessel.GetConnectedResourceTotals(oxDef.id, out curOx, out maxOx);
                            if (sfDef != null) ActiveVessel.GetConnectedResourceTotals(sfDef.id, out curSf, out maxSf);
                            if (mpDef != null) ActiveVessel.GetConnectedResourceTotals(mpDef.id, out curMp, out maxMp);

                            if (maxLf + maxOx > 0.001 && curLf + curOx > 0.001)
                            {
                                currentResource = curLf + curOx;
                                maxResource = maxLf + maxOx;
                                detectedProp = "LF / OX";
                            }
                            else if (maxSf > 0.001 && curSf > 0.001)
                            {
                                currentResource = curSf;
                                maxResource = maxSf;
                                detectedProp = "SOLID";
                            }
                            else if (maxMp > 0.001 && curMp > 0.001)
                            {
                                currentResource = curMp;
                                maxResource = maxMp;
                                detectedProp = "MONO";
                            }
                        }

                        if (_lastActiveEngines == 0 && engineCount > 0 && _throttle > 0.01f)
                        {
                            TriggerEngineIgnitionEvent();
                        }
                        _lastActiveEngines = engineCount;

                        int stageTotalEngines = 0;
                        if (_cachedEngines != null)
                        {
                            for (int i = 0; i < _cachedEngines.Count; i++)
                            {
                                ModuleEngines eng = _cachedEngines[i];
                                if (eng != null)
                                {
                                    if (eng.part != null && (eng.part.inverseStage == _currentStage || eng.isOperational))
                                        stageTotalEngines++;
                                    else if (eng.isOperational)
                                        stageTotalEngines++;
                                }
                            }
                            if (stageTotalEngines == 0) stageTotalEngines = _cachedEngines.Count;
                        }

                        _activeEngines = engineCount;
                        _totalStageEngines = stageTotalEngines > 0 ? stageTotalEngines : (engineCount > 0 ? engineCount : 1);
                        if (nativeGaugeCount > 0)
                        {
                            _stagePropellantFraction = Mathf.Clamp01(nativeGaugeSum / nativeGaugeCount);
                        }
                        else if (maxResource > 0.0001)
                        {
                            _stagePropellantFraction = Mathf.Clamp01((float)(currentResource / maxResource));
                        }
                        else
                        {
                            _stagePropellantFraction = -1.0f;
                        }
                        _stagePropellantName = detectedProp;
                        _cachedTotalThrust = thrust;
                    }

                    // 多级 ΔV 与完整分级序列遥测
                    bool dvFound = false;
                    try
                    {
                        if (KSP.UI.Screens.StageManager.Instance != null && KSP.UI.Screens.StageManager.Instance.Stages != null)
                        {
                            var mgrStages = KSP.UI.Screens.StageManager.Instance.Stages;
                            int curStg = ActiveVessel != null ? ActiveVessel.currentStage : -1;
                            var vdv = ActiveVessel != null ? ActiveVessel.VesselDeltaV : null;

                            Dictionary<int, StageDeltaVInfo> mjStageMap = null;
                            double mjTotDv = 0.0, mjTotTime = 0.0;
                            if (MechJebProbe.TryGetStageStats(out List<StageDeltaVInfo> mjStages, out mjTotDv, out mjTotTime) && mjStages != null)
                            {
                                mjStageMap = new Dictionary<int, StageDeltaVInfo>();
                                for (int m = 0; m < mjStages.Count; m++)
                                {
                                    mjStageMap[mjStages[m].Stage] = mjStages[m];
                                }
                            }

                            _cachedStockStages.Clear();
                            double sumCalcDv = 0.0;
                            double sumCalcTime = 0.0;

                            for (int i = 0; i < mgrStages.Count; i++)
                            {
                                var grp = mgrStages[i];
                                if (grp == null) continue;

                                int stgNum = -1;
                                if (_stageGroupUiStageIndexField == null)
                                {
                                    _stageGroupUiStageIndexField = typeof(KSP.UI.Screens.StageGroup).GetField("uiStageIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                }
                                var uiTmp = _stageGroupUiStageIndexField?.GetValue(grp) as TMPro.TextMeshProUGUI;
                                if (uiTmp != null && int.TryParse(uiTmp.text, out int parsedNum))
                                {
                                    stgNum = parsedNum;
                                }
                                else
                                {
                                    stgNum = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                                }

                                var partIcons = ExtractIconsFromStageGroup(grp);
                                _cachedStagePartIcons[stgNum] = partIcons;
                                bool hasIcons = partIcons != null && partIcons.Count > 0;

                                double dv = 0.0;
                                double time = 0.0;
                                double twrVal = 0.0;
                                double isp = 0.0;
                                bool hasDv = false;

                                // Tier 1: Check StageGroup.stage (Stock DeltaVStageInfo)
                                if (_stageGroupStageField == null)
                                {
                                    _stageGroupStageField = typeof(KSP.UI.Screens.StageGroup).GetField("stage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                }
                                DeltaVStageInfo stgInfo = _stageGroupStageField?.GetValue(grp) as DeltaVStageInfo;
                                if (stgInfo == null && vdv != null)
                                {
                                    stgInfo = vdv.GetStage(stgNum);
                                }

                                if (stgInfo != null)
                                {
                                    dv = stgInfo.deltaVActual;
                                    time = stgInfo.stageBurnTime;
                                    twrVal = stgInfo.TWRActual;
                                    isp = stgInfo.ispActual;
                                    hasDv = (dv > 0.01 || time > 0.01);
                                }

                                // Tier 2: MechJeb fallback ONLY if stock has no dv AND stage has icons!
                                // Empty stages (hasIcons == false) must NEVER receive MechJeb ΔV!
                                if (!hasDv && hasIcons && mjStageMap != null && mjStageMap.TryGetValue(stgNum, out var mjStat))
                                {
                                    dv = mjStat.DeltaV;
                                    time = mjStat.BurnTime;
                                    twrVal = mjStat.TWR;
                                    isp = mjStat.Isp;
                                    hasDv = (dv > 0.01 || time > 0.01);
                                }

                                bool isActive = (stgNum == curStg);
                                _cachedStockStages.Add(new StageDeltaVInfo(
                                    stgNum,
                                    dv,
                                    time,
                                    twrVal,
                                    isp,
                                    isActive,
                                    partIcons
                                ));

                                sumCalcDv += dv;
                                sumCalcTime += time;
                            }

                            if (_cachedStockStages.Count > 0)
                            {
                                _stageDeltaVList = _cachedStockStages;

                                if (vdv != null && vdv.TotalDeltaVActual > 0.001)
                                {
                                    _totalDeltaV = vdv.TotalDeltaVActual;
                                    _totalBurnTime = vdv.TotalBurnTime;
                                }
                                else if (mjTotDv > 0.001)
                                {
                                    _totalDeltaV = mjTotDv;
                                    _totalBurnTime = mjTotTime;
                                }
                                else
                                {
                                    _totalDeltaV = sumCalcDv;
                                    _totalBurnTime = sumCalcTime;
                                }

                                _deltaVSource = mjStageMap != null ? "MJ+STOCK" : "STOCK";

                                StageDeltaVInfo active = _cachedStockStages.Find(s => s.IsActive);
                                if (active.Stage >= 0)
                                {
                                    _stageDeltaV = active.DeltaV;
                                    _stageBurnTime = active.BurnTime;
                                }
                                else if (_cachedStockStages.Count > 0)
                                {
                                    _stageDeltaV = _cachedStockStages[_cachedStockStages.Count - 1].DeltaV;
                                    _stageBurnTime = _cachedStockStages[_cachedStockStages.Count - 1].BurnTime;
                                }
                                dvFound = true;
                            }
                        }
                    }
                    catch { }

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
                            _stageDeltaVList = _cachedStockStages;
                            _totalDeltaV = vdv.TotalDeltaVActual;
                            _totalBurnTime = vdv.TotalBurnTime;
                            _deltaVSource = "STOCK";

                            StageDeltaVInfo active = _cachedStockStages.Find(s => s.IsActive);
                            if (active.Stage >= 0)
                            {
                                _stageDeltaV = active.DeltaV;
                                _stageBurnTime = active.BurnTime;
                            }
                            else
                            {
                                _stageDeltaV = _cachedStockStages[0].DeltaV;
                                _stageBurnTime = _cachedStockStages[0].BurnTime;
                            }
                            dvFound = true;
                        }
                    }

                    if (!dvFound)
                    {
                        if (!double.IsNaN(KerbalEngineerProbe.StageDeltaV) && KerbalEngineerProbe.StageDeltaV > 0)
                        {
                            _stageDeltaV = KerbalEngineerProbe.StageDeltaV;
                            _deltaVSource = "KER";
                        }
                        else if (!double.IsNaN(MechJebProbe.StageDeltaV) && MechJebProbe.StageDeltaV > 0)
                        {
                            _stageDeltaV = MechJebProbe.StageDeltaV;
                            _deltaVSource = "MJ";
                        }
                    }
                }

                double gee = ActiveVessel.mainBody != null ? ActiveVessel.mainBody.GeeASL * 9.80665 : 9.80665;
                double weight = ActiveVessel.totalMass * gee;
                _twr = weight > 0.001 ? _cachedTotalThrust / weight : 0.0;
            }
            catch (Exception) { }
        }

        public void InvalidateStagePartIcons()
        {
            _lastStageIconScanTime = -999f;
            _cachedStagePartIcons.Clear();
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
                        int stg = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                        _cachedStagePartIcons[stg] = ExtractIconsFromStageGroup(grp);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RefreshStagePartIcons warning: {ex.Message}");
            }
        }

        private List<StagePartIconData> ExtractIconsFromStageGroup(KSP.UI.Screens.StageGroup grp)
        {
            if (grp == null || grp.Icons == null || grp.Icons.Count == 0)
            {
                return new List<StagePartIconData>(0);
            }

            var iconList = new List<StagePartIconData>(grp.Icons.Count);
            for (int j = 0; j < grp.Icons.Count; j++)
            {
                var icon = grp.Icons[j];
                if (icon == null) continue;
                AppendSingleIcon(iconList, icon);
            }
            return iconList;
        }

        private void AppendSingleIcon(List<StagePartIconData> iconList, KSP.UI.Screens.StageIcon icon)
        {
            if (icon == null) return;
            string typeStr = icon.iconType.ToString();
            int typeIdx = (int)icon.iconType;
            int count = (icon.grouped && icon.groupedIcons != null && icon.groupedIcons.Count > 0) ? icon.groupedIcons.Count + 1 : 1;
            if (icon.Part != null && icon.Part.symmetryCounterparts != null && icon.Part.symmetryCounterparts.Count + 1 > count)
            {
                count = icon.Part.symmetryCounterparts.Count + 1;
            }

            string partTitle = string.Empty;
            string propName = null;
            float propFrac = -1f;
            uint flightId = 0;

            if (icon.Part != null)
            {
                flightId = icon.Part.flightID;
                if (flightId == 0) flightId = icon.Part.craftID;
                if (flightId == 0) flightId = (uint)icon.Part.persistentId;
                partTitle = icon.Part.partInfo != null ? icon.Part.partInfo.title : icon.Part.name;
            }

            // 1. Hook ProtoStageIcon / ProtoStageIconInfo (原版及 Mod 逐部件实时仪表)
            if (_stageIconProtoIconField == null)
            {
                _stageIconProtoIconField = typeof(KSP.UI.Screens.StageIcon).GetField("protoIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            }
            KSP.UI.Screens.ProtoStageIcon proto = icon.ProtoIcon ?? (_stageIconProtoIconField?.GetValue(icon) as KSP.UI.Screens.ProtoStageIcon);
            if (proto == null && icon.Part != null)
            {
                proto = icon.Part.stackIcon;
            }

            if (proto != null)
            {
                if (_protoIconInfoBoxesField == null)
                {
                    _protoIconInfoBoxesField = typeof(KSP.UI.Screens.ProtoStageIcon).GetField("infoBoxes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                }
                var boxes = _protoIconInfoBoxesField?.GetValue(proto) as List<KSP.UI.Screens.ProtoStageIconInfo>;
                if (boxes != null && boxes.Count > 0)
                {
                    for (int b = 0; b < boxes.Count; b++)
                    {
                        var box = boxes[b];
                        if (box != null && box.pBarValue >= 0f)
                        {
                            propFrac = Mathf.Clamp01(box.pBarValue);
                            propName = !string.IsNullOrEmpty(box.pBarCaption) ? box.pBarCaption : box.msg;
                            break;
                        }
                    }
                }
            }

            // 2. 检查部件上的 ModuleEngines 与 PropellantGauges
            if (propFrac < 0f && icon.Part != null)
            {
                var engines = icon.Part.FindModulesImplementing<ModuleEngines>();
                if (engines != null && engines.Count > 0)
                {
                    if (_moduleEnginesPropellantGaugesField == null)
                    {
                        _moduleEnginesPropellantGaugesField = typeof(ModuleEngines).GetField("PropellantGauges", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    }
                    for (int e = 0; e < engines.Count && propFrac < 0f; e++)
                    {
                        var eng = engines[e];
                        if (eng == null) continue;
                        var gauges = _moduleEnginesPropellantGaugesField?.GetValue(eng) as Dictionary<Propellant, KSP.UI.Screens.ProtoStageIconInfo>;
                        if (gauges != null && gauges.Count > 0)
                        {
                            foreach (var kvp in gauges)
                            {
                                var gauge = kvp.Value;
                                if (gauge != null && gauge.pBarValue >= 0f)
                                {
                                    propFrac = Mathf.Clamp01(gauge.pBarValue);
                                    propName = !string.IsNullOrEmpty(gauge.pBarCaption) ? gauge.pBarCaption : (kvp.Key?.displayName ?? kvp.Key?.name);
                                    break;
                                }
                            }
                        }

                        // 若仪表未初始化，强制 un-cached (cache=false) 换算联通储箱余量
                        if (propFrac < 0f && eng.propellants != null && eng.propellants.Count > 0)
                        {
                            double eCur = 0, eMax = 0;
                            for (int pIdx = 0; pIdx < eng.propellants.Count; pIdx++)
                            {
                                var pDef = eng.propellants[pIdx];
                                if (pDef == null || pDef.name == "ElectricCharge" || pDef.name == "IntakeAir") continue;
                                double pAvail = 0, pCap = 0;
                                icon.Part.GetConnectedResourceTotals(pDef.id, pDef.GetFlowMode(), out pAvail, out pCap, false);
                                if (pCap > 0.0001)
                                {
                                    eCur += pAvail;
                                    eMax += pCap;
                                    if (string.IsNullOrEmpty(propName)) propName = pDef.displayName ?? pDef.name;
                                }
                            }
                            if (eMax > 0.0001)
                            {
                                propFrac = Mathf.Clamp01((float)(eCur / eMax));
                            }
                        }
                    }
                }
            }

            // 3. 部件直属资源兜底
            if (propFrac < 0f && icon.Part != null && icon.Part.Resources != null)
            {
                var res = icon.Part.Resources;
                for (int r = 0; r < res.Count; r++)
                {
                    var resItem = res[r];
                    if (resItem != null && resItem.maxAmount > 0.0001)
                    {
                        string rName = resItem.resourceName;
                        if (rName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            rName.IndexOf("Liquid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            rName.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            rName.IndexOf("Methane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            rName.IndexOf("Propellant", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            propName = resItem.info?.displayName ?? rName;
                            propFrac = Mathf.Clamp01((float)(resItem.amount / resItem.maxAmount));
                            break;
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

            int existingIdx = iconList.FindIndex(p => p.IconType == typeStr && p.PartTitle == partTitle);
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
                iconList.Add(new StagePartIconData(typeStr, typeIdx, count, partTitle, propName, propFrac, uv, hasUv, flightId));
            }
        }
    }
}
