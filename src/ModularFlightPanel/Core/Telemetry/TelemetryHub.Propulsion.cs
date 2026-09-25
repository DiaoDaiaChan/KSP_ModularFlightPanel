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

                                    if (eng.propellants != null && eng.propellants.Count > 0)
                                    {
                                        for (int pr = 0; pr < eng.propellants.Count; pr++)
                                        {
                                            var pDef = eng.propellants[pr];
                                            if (pDef != null && pDef.totalResourceCapacity > 0.001)
                                            {
                                                string pName = pDef.name;
                                                if (pName == "IntakeAir" || pName == "ElectricCharge") continue;

                                                currentResource += pDef.totalResourceAvailable;
                                                maxResource += pDef.totalResourceCapacity;
                                                if (string.IsNullOrEmpty(detectedProp) || detectedProp == "PROP")
                                                    detectedProp = pDef.displayName ?? pDef.name;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Part p = eng.part;
                                        if (p != null && p.Resources != null)
                                        {
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
                        }

                        // 储箱联通余量核验
                        if ((maxResource <= 0.001 || (currentResource <= 0.001 && maxResource > 0.001)) && ActiveVessel != null)
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
                            else if (maxResource <= 0.001 && maxLf + maxOx > 0.001)
                            {
                                currentResource = curLf + curOx;
                                maxResource = maxLf + maxOx;
                                detectedProp = "LF / OX";
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
                        _stagePropellantFraction = maxResource > 0.001 ? Mathf.Clamp01((float)(currentResource / maxResource)) : 1.0f;
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
                                int stgNum = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                                var partIcons = GetStagePartIcons(stgNum);

                                double dv = 0.0;
                                double time = 0.0;
                                double twrVal = 0.0;
                                double isp = 0.0;
                                bool hasDv = false;

                                if (mjStageMap != null && mjStageMap.TryGetValue(stgNum, out var mjStat))
                                {
                                    dv = mjStat.DeltaV;
                                    time = mjStat.BurnTime;
                                    twrVal = mjStat.TWR;
                                    isp = mjStat.Isp;
                                    hasDv = (dv > 0.01 || time > 0.01);
                                }

                                if (!hasDv)
                                {
                                    DeltaVStageInfo stgInfo = vdv != null ? vdv.GetStage(stgNum) : null;
                                    if (stgInfo != null)
                                    {
                                        dv = stgInfo.deltaVActual;
                                        time = stgInfo.stageBurnTime;
                                        twrVal = stgInfo.TWRActual;
                                        isp = stgInfo.ispActual;
                                        hasDv = (dv > 0.01 || time > 0.01);
                                    }
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
                        if (!_cachedStagePartIcons.TryGetValue(stg, out var iconList))
                        {
                            iconList = new List<StagePartIconData>();
                            _cachedStagePartIcons[stg] = iconList;
                        }

                        var grpIcons = grp.Icons;
                        if (grpIcons == null || grpIcons.Count == 0) continue;

                        for (int j = 0; j < grpIcons.Count; j++)
                        {
                            var icon = grpIcons[j];
                            if (icon == null) continue;

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
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] RefreshStagePartIcons warning: {ex.Message}");
            }
        }
    }
}
