using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    public partial class TelemetryHub
    {
        // -------------------------------------------------------------
        // 电气系统 (Electrical System) - 按需惰性解算与 10Hz 节流
        // -------------------------------------------------------------
        private double _electricCharge = 0.0;
        private double _maxElectricCharge = 0.0;
        private double _netEcRate = 0.0;
        private float _busVoltage = 28.0f;
        private double _solarPower = 0.0;

        public double ElectricCharge
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ElectricCharge;
                EnsureSubsystemsUpdated();
                return _electricCharge;
            }
        }

        public double MaxElectricCharge
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.MaxElectricCharge;
                EnsureSubsystemsUpdated();
                return _maxElectricCharge;
            }
        }

        public double EcPercent => MaxElectricCharge > 0.001 ? (ElectricCharge / MaxElectricCharge * 100.0) : 100.0;

        public double NetEcRate
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.NetEcRate;
                EnsureSubsystemsUpdated();
                return _netEcRate;
            }
        }

        public float BusVoltage
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.BusVoltage;
                EnsureSubsystemsUpdated();
                return _busVoltage;
            }
        }

        public double SolarPower
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.SolarPower;
                EnsureSubsystemsUpdated();
                return _solarPower;
            }
        }

        // 差分计算电量速率与太阳能缓存
        private double _lastEc = 0.0;
        private float _lastEcTime = 0f;
        private float _lastSubsystemTime = -1f;
        private List<ModuleDeployableSolarPanel> _cachedSolarPanels = new List<ModuleDeployableSolarPanel>();
        private float _lastPanelScanTime = -1f;
        private int _lastSubsystemPartCount = -1;

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

        // -------------------------------------------------------------
        // 通信网络与 RealAntennas (Comms & RealAntennas) - 按需惰性解算与 5Hz 节流
        // -------------------------------------------------------------
        private double _commSignal = 1.0;
        private bool _isConnected = true;
        private string _controlLevelStr = "FULL CONTROL";
        private int _antennaCount = 0;
        private double _signalTx = 1.0;
        private double _signalRx = 1.0;
        private double _dataRateBps = 0.0;
        private string _directLinkTarget = "NONE";
        private IReadOnlyList<CommLinkInfo> _activeCommLinks = Array.Empty<CommLinkInfo>();
        private IReadOnlyList<AntennaTelemetryInfo> _antennas = Array.Empty<AntennaTelemetryInfo>();

        public double CommSignal
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CommSignal;
                EnsureSubsystemsUpdated();
                return _commSignal;
            }
        }

        public bool IsConnected
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsConnected;
                EnsureSubsystemsUpdated();
                return _isConnected;
            }
        }

        public string ControlLevelStr
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ControlLevelStr;
                EnsureSubsystemsUpdated();
                return _controlLevelStr;
            }
        }

        public int AntennaCount
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.AntennaCount;
                EnsureSubsystemsUpdated();
                return _antennaCount;
            }
        }

        public double SignalTx
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.SignalTx;
                EnsureSubsystemsUpdated();
                return _signalTx;
            }
        }

        public double SignalRx
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.SignalRx;
                EnsureSubsystemsUpdated();
                return _signalRx;
            }
        }

        public double DataRateBps
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.DataRateBps;
                EnsureSubsystemsUpdated();
                return _dataRateBps;
            }
        }

        public string DirectLinkTarget
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.DirectLinkTarget;
                EnsureSubsystemsUpdated();
                return _directLinkTarget;
            }
        }

        public IReadOnlyList<CommLinkInfo> ActiveCommLinks
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ActiveCommLinks;
                EnsureSubsystemsUpdated();
                return _activeCommLinks;
            }
        }

        public IReadOnlyList<AntennaTelemetryInfo> Antennas
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.Antennas;
                EnsureSubsystemsUpdated();
                return _antennas;
            }
        }

        private int _cachedAntennaCount = 0;
        private float _lastAntennaScanTime = -1f;
        private float _lastCommScanTime = -1f;
        private readonly List<CommLinkInfo> _cachedStockCommLinks = new List<CommLinkInfo>(8);
        private readonly List<AntennaTelemetryInfo> _cachedAntennasList = new List<AntennaTelemetryInfo>(8);

        // -------------------------------------------------------------
        // 居住环境与乘员 (Cabin & Life Support)
        // -------------------------------------------------------------
        private int _crewCount = 0;
        private int _crewCapacity = 0;
        private double _cabinPressure = 101.3;
        private double _cabinTemp = 21.0;
        private float _oxygenPercent = 100.0f;
        private float _monoPercent = 100.0f;
        private float _waterPercent = 100.0f;

        public int CrewCount
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CrewCount;
                EnsureSubsystemsUpdated();
                return _crewCount;
            }
        }

        public int CrewCapacity
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CrewCapacity;
                EnsureSubsystemsUpdated();
                return _crewCapacity;
            }
        }

        public double CabinPressure
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CabinPressure;
                EnsureSubsystemsUpdated();
                return _cabinPressure;
            }
        }

        public double CabinTemp
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CabinTemp;
                EnsureSubsystemsUpdated();
                return _cabinTemp;
            }
        }

        public float OxygenPercent
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.OxygenPercent;
                EnsureSubsystemsUpdated();
                return _oxygenPercent;
            }
        }

        public float MonoPercent
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.MonoPercent;
                EnsureSubsystemsUpdated();
                return _monoPercent;
            }
        }

        public float WaterPercent
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.WaterPercent;
                EnsureSubsystemsUpdated();
                return _waterPercent;
            }
        }

        private void EnsureSubsystemsUpdated()
        {
            float now = Time.unscaledTime;
            // 10 Hz 遥测子系统节流与同帧保护
            if (_subsystemsFrame == Time.frameCount || (now - _lastSubsystemTime < 0.1f)) return;
            _subsystemsFrame = Time.frameCount;
            _lastSubsystemTime = now;
            if (!HasVessel) return;
            UpdateSubsystemTelemetry();
        }

        private int _subsystemsFrame = -1;

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
                _electricCharge = curEc;
                _maxElectricCharge = maxEc;

                float dt = now - _lastEcTime;
                if (dt >= 0.15f)
                {
                    double instantRate = (curEc - _lastEc) / dt;
                    _netEcRate = Mathf.Lerp((float)_netEcRate, (float)instantRate, 0.4f);
                    _lastEc = curEc;
                    _lastEcTime = now;
                }
                float ecPct = maxEc > 0.001 ? (float)(curEc / maxEc) : 1.0f;
                _busVoltage = 22.0f + 6.2f * Mathf.Clamp01(ecPct);

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
                _solarPower = sol;

                // 2. 通信网络与链路 (RealAntennas / Stock CommNet - 节流 5 Hz)
                if (partsChanged || (now - _lastCommScanTime) >= 0.2f)
                {
                    _lastCommScanTime = now;
                    if (RealAntennasProbe.IsAvailable)
                    {
                        var raLinks = RealAntennasProbe.GetActiveCommLinks();
                        _activeCommLinks = raLinks;

                        if (partsChanged || (now - _lastAntennaScanTime) >= 1.0f)
                        {
                            _lastAntennaScanTime = now;
                            var raAnts = RealAntennasProbe.GetAntennasList();
                            if (raAnts != null && raAnts.Count > 0)
                            {
                                _cachedAntennasList.Clear();
                                _cachedAntennasList.AddRange(raAnts);
                                _antennas = _cachedAntennasList;
                                _cachedAntennaCount = raAnts.Count;
                            }
                            else
                            {
                                RefreshAntennasList(v);
                            }
                        }
                        _antennaCount = _cachedAntennaCount;

                        _isConnected = RealAntennasProbe.ResolveNumeric("IsConnectedHome") > 0.5;
                        _commSignal = RealAntennasProbe.ResolveNumeric("SignalStrength");
                        if (double.IsNaN(_commSignal)) _commSignal = v.Connection != null ? v.Connection.SignalStrength : 1.0;
                        _signalTx = _commSignal;
                        _signalRx = _commSignal;
                        _dataRateBps = RealAntennasProbe.ResolveNumeric("ActiveDataRate");
                        if (_dataRateBps <= 0.0 && raLinks != null && raLinks.Count > 0)
                        {
                            _dataRateBps = raLinks[0].DataRateBps;
                        }
                        _directLinkTarget = RealAntennasProbe.ResolveString("TargetName");
                        if ((string.IsNullOrEmpty(_directLinkTarget) || _directLinkTarget == "NONE") && raLinks != null && raLinks.Count > 0)
                        {
                            _directLinkTarget = raLinks[0].PeerName;
                        }
                        _controlLevelStr = !_isConnected ? "NO LINK" : (_commSignal < 0.35 ? "WEAK LINK" : "FULL CONTROL");
                    }
                    else
                    {
                        if (v.Connection != null)
                        {
                            _isConnected = v.Connection.IsConnected;
                            _commSignal = v.Connection.SignalStrength;
                            _signalTx = _commSignal;
                            _signalRx = _commSignal;

                            if (!_isConnected) _controlLevelStr = "NO LINK";
                            else if (v.CurrentControlLevel == Vessel.ControlLevel.NONE) _controlLevelStr = "NO CONTROL";
                            else if (v.CurrentControlLevel == Vessel.ControlLevel.PARTIAL_UNMANNED || v.CurrentControlLevel == Vessel.ControlLevel.PARTIAL_MANNED) _controlLevelStr = "PARTIAL";
                            else _controlLevelStr = _commSignal < 0.25 ? "WEAK LINK" : "FULL CONTROL";

                            _cachedStockCommLinks.Clear();
                            if (v.Connection.ControlPath != null && v.Connection.ControlPath.Count > 0)
                            {
                                foreach (var link in v.Connection.ControlPath)
                                {
                                    if (link != null && link.end != null)
                                    {
                                        string pName = link.end.displayName ?? link.end.name;
                                        float qual = Mathf.Clamp01((float)link.strengthAR);
                                        double rate = 100000.0 * qual;
                                        _cachedStockCommLinks.Add(new CommLinkInfo(pName, rate, qual, link.end.isHome));
                                    }
                                }
                            }
                            _activeCommLinks = _cachedStockCommLinks;
                            if (_cachedStockCommLinks.Count > 0)
                            {
                                _directLinkTarget = _cachedStockCommLinks[0].PeerName;
                                _dataRateBps = _cachedStockCommLinks[0].DataRateBps;
                            }
                            else
                            {
                                _directLinkTarget = _isConnected ? "KERBIN DSN" : "NONE";
                                _dataRateBps = _isConnected ? 100000.0 * _commSignal : 0.0;
                            }
                        }
                        if (partsChanged || (now - _lastAntennaScanTime) >= 1.0f)
                        {
                            _lastAntennaScanTime = now;
                            var transmitters = v.FindPartModulesImplementing<ModuleDataTransmitter>();
                            _cachedAntennaCount = transmitters != null ? transmitters.Count : 0;
                            RefreshAntennasList(v);
                        }
                        _antennaCount = _cachedAntennaCount;
                    }
                }

                // 3. 乘员与居住舱
                _crewCount = v.GetCrewCount();
                _crewCapacity = v.GetCrewCapacity();
                _cabinPressure = v.staticPressurekPa > 0.01 ? v.staticPressurekPa : 101.3;
                double rawTempK = (v.rootPart != null && v.rootPart.temperature > 0.1) ? v.rootPart.temperature : v.externalTemperature;
                _cabinTemp = rawTempK > 100.0 ? (rawTempK - 273.15) : rawTempK;
            }
            catch (Exception) { }
        }

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
                        string status = _isConnected ? (i == 0 ? "LINKED" : "STANDBY") : "SEARCHING";
                        float sig = (float)_commSignal;

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
                string status = _isConnected ? "LINKED" : "NO LINK";
                _cachedAntennasList.Add(new AntennaTelemetryInfo("INTERNAL ANTENNA", "INTERNAL", 5000.0, "5.0k", (float)_commSignal, status, true));
            }
            _antennas = _cachedAntennasList;
        }
    }
}
