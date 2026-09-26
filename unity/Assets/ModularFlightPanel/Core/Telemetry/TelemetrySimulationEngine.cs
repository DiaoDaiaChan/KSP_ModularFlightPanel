using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    public enum FlightScenario
    {
        PadHold,          // 发射台预备 (0m/s, 1G, 满电满燃料)
        AscentTransonic,  // 音障爬升 (速度接近 340m/s, 动压抬升)
        MaxQ,             // 极限动压 (Q=34.2 kPa 极值, 3.5G, 强震动)
        MECOAndStaging,   // 关机分级 (推力切断, 抛弃级, 下一级点火)
        OrbitalCruise,    // 入轨微重力 (120km, 2280m/s, 0G, 帆板展开净充电)
        PowerCrisis,      // 暗面断电 (太阳能归零, 电池耗尽, 电压跌破告警)
        ReentryBlackout   // 黑障再入 (大角度俯冲 -420m/s, 信号中断离线, 开伞)
    }

    /// <summary>
    /// 高保真机载遥测物理仿真引擎 (纯 C# 解耦核心)
    /// 脱离 KSP ActiveVessel 依赖，为无头测试与原地沙盒提供全套动态飞行物理数据
    /// </summary>
    public class TelemetrySimulationEngine : IFlightTelemetry
    {
        public bool HasVessel => true;
        public bool IsSimulationMode => true;
        public string VesselName => "SIM-VESSEL-1";
        public string CelestialBodyName => "Kerbin";
        public string FlightSituation => CurrentScenario.ToString().ToUpper();

        public bool IsPlaying { get; set; } = true;
        public float PlaybackSpeed { get; set; } = 1.0f;
        public float TimelineTime { get; set; } = 0f;
        public float MaxTimelineTime { get; set; } = 120f; // 120 秒标准升空入轨时序

        public FlightScenario CurrentScenario { get; private set; } = FlightScenario.PadHold;

        // 姿态与航向
        public Quaternion AttitudeRotation { get; private set; } = Quaternion.identity;
        public float Heading { get; private set; } = 90f;
        public float Pitch { get; private set; } = 90f;
        public float Roll { get; private set; } = 0f;

        // 速度
        public double SurfaceSpeed { get; private set; } = 0.0;
        public double OrbitalSpeed { get; private set; } = 175.0;
        public double TargetSpeed { get; set; } = 0.0;
        public double CurrentSpeed => SurfaceSpeed;
        public double Mach { get; private set; } = 0.0;

        // 高度与动力学
        public AltitudeDisplayMode CurrentAltMode { get; set; } = AltitudeDisplayMode.Sea;
        public double AltitudeASL { get; private set; } = 74.0;
        public double AltitudeAGL { get; private set; } = 0.0;
        public double DisplayAltitude => AltitudeASL;
        public double VerticalSpeed { get; private set; } = 0.0;
        public float NormalizedVSI { get; private set; } = 0.5f;
        public bool IsTouchdownAlert => (AltitudeAGL < 300.0 && VerticalSpeed < -1.5);
        public double DynamicPressure { get; private set; } = 0.0;
        public double AtmosphericPressure { get; private set; } = 1.0;
        public double GForce { get; private set; } = 1.0;
        public bool HasAtmosphere { get; private set; } = true;
        public double AtmosphereDepth { get; private set; } = 70000.0;

        // 推进系统
        public float Throttle { get; private set; } = 0.0f;
        public float StagePropellantFraction { get; private set; } = 1.0f;
        public double TWR { get; private set; } = 0.0;
        public int CurrentStage { get; private set; } = 3;
        public double StageDeltaV { get; private set; } = 2350.0;
        public double TotalDeltaV { get; private set; } = 4850.0;
        public double StageBurnTime { get; private set; } = 52.0;
        public double TotalBurnTime { get; private set; } = 196.0;
        public IReadOnlyList<StageDeltaVInfo> StageDeltaVList { get; private set; } = Array.Empty<StageDeltaVInfo>();
        public string DeltaVSource { get; private set; } = "SIM";
        public int ActiveEngines { get; private set; } = 0;
        public int TotalStageEngines { get; private set; } = 6;

        // 轨道数据
        public double Apoapsis { get; private set; } = 74.0;
        public double Periapsis { get; private set; } = -600000.0;
        public double TimeToAp { get; private set; } = 0.0;
        public double TimeToPe { get; private set; } = 0.0;
        public double SemiMajorAxis { get; private set; } = 600074.0;
        public double Eccentricity { get; private set; } = 0.0;
        public double Inclination { get; private set; } = 0.0;
        public double LongitudeOfAscendingNode { get; private set; } = 0.0;
        public double ArgumentOfPeriapsis { get; private set; } = 0.0;
        public double TrueAnomaly { get; private set; } = 0.0;
        public double OrbitalPeriod { get; private set; } = 0.0;

        // 机动节点
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

        // 电气系统
        public double ElectricCharge { get; private set; } = 400.0;
        public double MaxElectricCharge { get; private set; } = 400.0;
        public double EcPercent => MaxElectricCharge > 0.001 ? (ElectricCharge / MaxElectricCharge * 100.0) : 100.0;
        public double NetEcRate { get; private set; } = 0.0;
        public float BusVoltage { get; private set; } = 28.0f;
        public double SolarPower { get; private set; } = 0.0;

        private static readonly CommLinkInfo[] DefaultSimulationLinks = new CommLinkInfo[]
        {
            new CommLinkInfo("US - Cape Canaveral", 15800.0, 1.0f, true),
            new CommLinkInfo("Tracking Station Madrid", 63000.0, 0.92f, false),
            new CommLinkInfo("Kerbin Relay Alpha", 504000.0, 0.98f, false)
        };

        private static readonly AntennaTelemetryInfo[] DefaultSimulationAntennas = new AntennaTelemetryInfo[]
        {
            new AntennaTelemetryInfo("Communotron 16", "DIRECT", 500000.0, "500k", 0.95f, "LINKED", true),
            new AntennaTelemetryInfo("RA-2 Relay Antenna", "RELAY", 2000000000.0, "2.0G", 1.0f, "STANDBY", true),
            new AntennaTelemetryInfo("Internal Pod Antenna", "INTERNAL", 5000.0, "5.0k", 0.90f, "STANDBY", true)
        };

        // 通信网络
        public double CommSignal { get; private set; } = 1.0;
        public bool IsConnected { get; private set; } = true;
        public string ControlLevelStr { get; private set; } = "FULL CONTROL";
        public int AntennaCount { get; private set; } = 3;
        public double SignalTx { get; private set; } = 1.0;
        public double SignalRx { get; private set; } = 1.0;
        public double DataRateBps { get; private set; } = 15800.0;
        public string DirectLinkTarget { get; private set; } = "US - Cape Canaveral";
        public IReadOnlyList<CommLinkInfo> ActiveCommLinks { get; private set; } = DefaultSimulationLinks;
        public IReadOnlyList<AntennaTelemetryInfo> Antennas { get; private set; } = DefaultSimulationAntennas;

        // 时间加速与时钟遥测
        public double MissionTime { get; set; } = 9856.0; // 02:44:16
        public double UniversalTime { get; set; } = 15284000.0;
        public float TimeWarpRate { get; set; } = 1.0f;
        public int TimeWarpRateIndex { get; set; } = 0;
        public int MaxTimeWarpRateIndex => WarpRates.Length - 1;
        public bool IsPhysicsWarp { get; set; } = false;
        public bool IsGamePaused { get; set; } = false;

        private static readonly float[] WarpRates = new float[] { 1f, 5f, 10f, 50f, 100f, 1000f, 10000f, 100000f };

        // 居住环境
        public int CrewCount { get; private set; } = 3;
        public int CrewCapacity { get; private set; } = 3;
        public double CabinPressure { get; private set; } = 101.3;
        public double CabinTemp { get; private set; } = 21.5;
        public float OxygenPercent { get; private set; } = 98.4f;
        public float MonoPercent { get; private set; } = 85.0f;
        public float WaterPercent { get; private set; } = 92.0f;

        // 飞控开关与 SAS
        public bool IsRCSEnabled { get; set; } = true;
        public bool IsSASEnabled { get; set; } = true;
        public FlightSASMode CurrentSASMode { get; set; } = FlightSASMode.StabilityAssist;
        public string SpeedModeName { get; set; } = "SURFACE";

        // 三轴姿态操纵量与配平 (-1.0 ~ +1.0)
        public float PitchInput { get; set; } = 0.24f;
        public float RollInput { get; set; } = 0.0f;
        public float YawInput { get; set; } = -0.15f;
        public float PitchTrim { get; set; } = 0.05f;
        public float RollTrim { get; set; } = 0.0f;
        public float YawTrim { get; set; } = -0.02f;

        // 分级安全锁、模式与推进剂
        public bool IsStageLocked { get; set; } = false;
        public bool IsPrecisionControl { get; set; } = false;
        public bool IsDockingMode { get; set; } = false;
        public string StagePropellantName { get; set; } = "LH2 / OX";

        // 瞬态事件遥测
        public bool IsStageSeparating { get; set; } = false;
        public bool IsEngineIgniting { get; set; } = false;

        public void SetSASMode(FlightSASMode mode) { CurrentSASMode = mode; }
        public void ToggleSAS() { IsSASEnabled = !IsSASEnabled; }
        public void ToggleRCS() { IsRCSEnabled = !IsRCSEnabled; }
        public void CycleSpeedMode()
        {
            if (SpeedModeName == "SURFACE") SpeedModeName = "ORBIT";
            else if (SpeedModeName == "ORBIT") SpeedModeName = "TARGET";
            else SpeedModeName = "SURFACE";
        }
        public void ActivateNextStage()
        {
            if (CurrentStage > 0 && !IsStageLocked)
            {
                CurrentStage--;
                StagePropellantFraction = 1.0f;
                IsStageSeparating = true;
            }
        }
        public void ToggleStageLock() { IsStageLocked = !IsStageLocked; }

        public void SetFlightParameters(double surfaceSpeed, double altitudeASL, float pitch, float heading, float throttle, int activeEngines, int totalStageEngines, double missionTime = 504.0)
        {
            SurfaceSpeed = surfaceSpeed;
            AltitudeASL = altitudeASL;
            AltitudeAGL = altitudeASL;
            Pitch = pitch;
            Heading = heading;
            Throttle = throttle;
            ActiveEngines = activeEngines;
            TotalStageEngines = totalStageEngines;
            MissionTime = missionTime;
        }
        public void TogglePrecisionMode() { IsPrecisionControl = !IsPrecisionControl; }
        public void ToggleFlightMode() { IsDockingMode = !IsDockingMode; }

        public void IncreaseTimeWarp()
        {
            if (TimeWarpRateIndex < MaxTimeWarpRateIndex)
            {
                TimeWarpRateIndex++;
                TimeWarpRate = WarpRates[TimeWarpRateIndex];
            }
        }

        public void DecreaseTimeWarp()
        {
            if (TimeWarpRateIndex > 0)
            {
                TimeWarpRateIndex--;
                TimeWarpRate = WarpRates[TimeWarpRateIndex];
            }
        }

        public void CancelTimeWarp()
        {
            TimeWarpRateIndex = 0;
            TimeWarpRate = 1.0f;
        }

        public void TogglePause()
        {
            IsGamePaused = !IsGamePaused;
        }

        public void SetTimeWarpRateIndex(int index)
        {
            TimeWarpRateIndex = Mathf.Clamp(index, 0, MaxTimeWarpRateIndex);
            TimeWarpRate = WarpRates[TimeWarpRateIndex];
        }

        public TelemetrySimulationEngine()
        {
            ApplyScenario(FlightScenario.PadHold);
        }

        private static List<StageDeltaVInfo> CreateSimulatedStages(int currentStage, float stageFuel, double activeDv, double activeBurnTime, double activeTwr)
        {
            var list = new List<StageDeltaVInfo>();
            if (currentStage >= 6)
            {
                // S00: 载荷与伞降 (Parachute) - 纯功能级
                list.Add(new StageDeltaVInfo(0, 0.0, 0.0, 0.0, 0.0, currentStage == 0, new List<StagePartIconData>
                {
                    new StagePartIconData("PARACHUTES", 8, 1, "Mk16 Parachute", null, -1f, default, false, 1007)
                }));

                // S01: 空分级 (Empty Stage)
                list.Add(new StageDeltaVInfo(1, 0.0, 0.0, 0.0, 0.0, currentStage == 1, new List<StagePartIconData>()));

                // S02: 芯二级动力 (LV-T45 'Swivel' + TD-12 Decoupler)
                double s2Dv = currentStage == 2 ? activeDv : 2530.0;
                double s2Time = currentStage == 2 ? activeBurnTime : 75.0;
                double s2Twr = currentStage == 2 ? activeTwr : 1.65;
                list.Add(new StageDeltaVInfo(2, s2Dv, s2Time, s2Twr, 320.0, currentStage == 2, new List<StagePartIconData>
                {
                    new StagePartIconData("LIQUID_ENGINE", 2, 1, "LV-T45 'Swivel' Liquid Fuel Engine", "Liquid Fuel", currentStage == 2 ? stageFuel : 1.0f, default, false, 1002),
                    new StagePartIconData("DECOUPLER_VERT", 5, 1, "TD-12 Decoupler", null, -1f, default, false, 1004)
                }));

                // S03: 空分级 (Empty Stage)
                list.Add(new StageDeltaVInfo(3, 0.0, 0.0, 0.0, 0.0, currentStage == 3, new List<StagePartIconData>()));

                // S04: 径向推进级 (24-77 'Twitch' Liquid Engine x2)
                double s4Dv = currentStage == 4 ? activeDv : 1024.0;
                double s4Time = currentStage == 4 ? activeBurnTime : 48.0;
                double s4Twr = currentStage == 4 ? activeTwr : 2.10;
                list.Add(new StageDeltaVInfo(4, s4Dv, s4Time, s4Twr, 310.0, currentStage == 4, new List<StagePartIconData>
                {
                    new StagePartIconData("LIQUID_ENGINE", 2, 2, "24-77 'Twitch' Liquid Engine", "Liquid Fuel", currentStage == 4 ? stageFuel : 1.0f, default, false, 1005)
                }));

                // S05: 芯一级级间脱离器 (TD-12 Decoupler) - 纯功能级
                list.Add(new StageDeltaVInfo(5, 0.0, 0.0, 0.0, 0.0, currentStage == 5, new List<StagePartIconData>
                {
                    new StagePartIconData("DECOUPLER_VERT", 5, 1, "TD-12 Decoupler", null, -1f, default, false, 1006)
                }));

                // S06: 径向分离器 (TT-38K Radial Decoupler x2) - 纯功能级 / 发射台首发触发级
                list.Add(new StageDeltaVInfo(6, 0.0, 0.0, 0.0, 0.0, currentStage == 6, new List<StagePartIconData>
                {
                    new StagePartIconData("DECOUPLER_HOR", 6, 2, "TT-38K Radial Decoupler", null, -1f, default, false, 1003)
                }));
                return list;
            }

            if (currentStage >= 3)
            {
                list.Add(new StageDeltaVInfo(3, activeDv, activeBurnTime, activeTwr, 312.0, currentStage == 3, new List<StagePartIconData>
                {
                    new StagePartIconData("SOLID_BOOSTER", 3, 6, "BACC Solid Fuel Booster", "Solid Fuel", stageFuel, default, false, 1001),
                    new StagePartIconData("LIQUID_ENGINE", 2, 1, "RE-M3 'Mainsail' Liquid Engine", "Liquid Fuel", Mathf.Clamp01(stageFuel + 0.15f), default, false, 1002)
                }));
            }
            if (currentStage >= 2)
            {
                double s2Dv = currentStage == 2 ? activeDv : 1820.0;
                double s2Time = currentStage == 2 ? activeBurnTime : 84.0;
                double s2Twr = currentStage == 2 ? activeTwr : 1.40;
                list.Add(new StageDeltaVInfo(2, s2Dv, s2Time, s2Twr, 345.0, currentStage == 2, new List<StagePartIconData>
                {
                    new StagePartIconData("DECOUPLER_HOR", 6, 4, "TT-70 Radial Decoupler", null, -1f, default, false, 1003),
                    new StagePartIconData("DECOUPLER_VERT", 5, 1, "TD-25 Decoupler", null, -1f, default, false, 1004)
                }));
            }
            if (currentStage >= 1)
            {
                double s1Dv = currentStage == 1 ? activeDv : 680.0;
                double s1Time = currentStage == 1 ? activeBurnTime : 60.0;
                double s1Twr = currentStage == 1 ? activeTwr : 0.95;
                list.Add(new StageDeltaVInfo(1, s1Dv, s1Time, s1Twr, 380.0, currentStage == 1, new List<StagePartIconData>
                {
                    new StagePartIconData("LIQUID_ENGINE", 2, 1, "RE-L10 'Poodle' Liquid Fuel Engine", "Liquid Fuel", currentStage == 1 ? stageFuel : 1.0f, default, false, 1005),
                    new StagePartIconData("DECOUPLER_VERT", 5, 1, "TD-12 Decoupler", null, -1f, default, false, 1006)
                }));
            }
            list.Add(new StageDeltaVInfo(0, 0.0, 0.0, 0.0, 0.0, currentStage == 0, new List<StagePartIconData>
            {
                new StagePartIconData("PARACHUTES", 8, 2, "Mk16-XL Parachute", null, -1f, default, false, 1007),
                new StagePartIconData("COMMAND_POD", 4, 1, "Mk1-3 Command Pod", null, -1f, default, false, 1008)
            }));
            return list;
        }

        public void InsertSimulatedStage(int stageIndex)
        {
            var list = new List<StageDeltaVInfo>(StageDeltaVList);
            int insertPos = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Stage <= stageIndex)
                {
                    insertPos = i;
                    break;
                }
            }
            list.Insert(insertPos, new StageDeltaVInfo(stageIndex, 0.0, 0.0, 0.0, 0.0, false, new List<StagePartIconData>()));
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                list[i] = new StageDeltaVInfo(list.Count - 1 - i, s.DeltaV, s.BurnTime, s.TWR, s.Isp, s.IsActive, s.PartIcons);
            }
            StageDeltaVList = list;
        }

        public void DeleteSimulatedStage(int stageIndex)
        {
            var list = new List<StageDeltaVInfo>(StageDeltaVList);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Stage == stageIndex)
                {
                    list.RemoveAt(i);
                    break;
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                list[i] = new StageDeltaVInfo(list.Count - 1 - i, s.DeltaV, s.BurnTime, s.TWR, s.Isp, s.IsActive, s.PartIcons);
            }
            StageDeltaVList = list;
        }

        public void MoveSimulatedPartToStage(uint partFlightId, int fromStage, int partIndex, int targetStage)
        {
            var list = new List<StageDeltaVInfo>(StageDeltaVList);
            StagePartIconData foundPart = default;
            bool found = false;
            int fromStageIdx = -1;
            int toStageIdx = -1;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Stage == fromStage) fromStageIdx = i;
                if (list[i].Stage == targetStage) toStageIdx = i;
            }

            if (fromStageIdx >= 0 && toStageIdx >= 0)
            {
                var fromParts = new List<StagePartIconData>(list[fromStageIdx].PartIcons);
                for (int p = 0; p < fromParts.Count; p++)
                {
                    if ((partFlightId > 0 && fromParts[p].PartFlightId == partFlightId) || (partIndex == p))
                    {
                        foundPart = fromParts[p];
                        fromParts.RemoveAt(p);
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    var fromStageInfo = list[fromStageIdx];
                    list[fromStageIdx] = new StageDeltaVInfo(fromStageInfo.Stage, fromStageInfo.DeltaV, fromStageInfo.BurnTime, fromStageInfo.TWR, fromStageInfo.Isp, fromStageInfo.IsActive, fromParts);

                    var toParts = new List<StagePartIconData>(list[toStageIdx].PartIcons);
                    toParts.Add(foundPart);
                    var toStageInfo = list[toStageIdx];
                    list[toStageIdx] = new StageDeltaVInfo(toStageInfo.Stage, toStageInfo.DeltaV, toStageInfo.BurnTime, toStageInfo.TWR, toStageInfo.Isp, toStageInfo.IsActive, toParts);

                    StageDeltaVList = list;
                }
            }
        }

        public void ApplyScenario(FlightScenario scenario)
        {
            CurrentScenario = scenario;
            HasManeuverNode = false;
            ManeuverDeltaV = 0.0;
            ManeuverTotalDeltaV = 0.0;
            ManeuverTimeToNode = 0.0;
            ManeuverBurnTime = 0.0;
            ManeuverTimeToBurn = 0.0;
            ManeuverDeltaVPrograde = 0.0;
            ManeuverDeltaVNormal = 0.0;
            ManeuverDeltaVRadial = 0.0;
            ManeuverSource = "STANDBY";

            switch (scenario)
            {
                case FlightScenario.PadHold:
                    TimelineTime = 0f;
                    SurfaceSpeed = 0.0;
                    OrbitalSpeed = 175.0;
                    AltitudeASL = 74.0;
                    AltitudeAGL = 0.0;
                    VerticalSpeed = 0.0;
                    NormalizedVSI = 0.5f;
                    DynamicPressure = 0.0;
                    AtmosphericPressure = 1.0;
                    GForce = 1.0;
                    Throttle = 0.0f;
                    StagePropellantFraction = 1.0f;
                    TWR = 0.0;
                    CurrentStage = 6;
                    StageDeltaV = 0.0;
                    TotalDeltaV = 3554.0;
                    StageBurnTime = 0.0;
                    TotalBurnTime = 123.0;
                    StageDeltaVList = CreateSimulatedStages(6, 1.0f, 0.0, 0.0, 0.0);
                    ActiveEngines = 0;
                    Pitch = 90f;
                    Heading = 90f;
                    Roll = 0f;
                    ElectricCharge = 400.0;
                    MaxElectricCharge = 400.0;
                    NetEcRate = 0.0;
                    BusVoltage = 28.2f;
                    SolarPower = 0.0;
                    CommSignal = 1.0;
                    IsConnected = true;
                    ControlLevelStr = "FULL CONTROL";
                    CabinPressure = 101.3;
                    CabinTemp = 21.0;
                    OxygenPercent = 100.0f;
                    MonoPercent = 100.0f;
                    WaterPercent = 100.0f;
                    break;

                case FlightScenario.AscentTransonic:
                    TimelineTime = 25f;
                    SurfaceSpeed = 335.0;
                    OrbitalSpeed = 480.0;
                    AltitudeASL = 8200.0;
                    AltitudeAGL = 8126.0;
                    VerticalSpeed = 310.0;
                    NormalizedVSI = 0.88f;
                    DynamicPressure = 24.5;
                    AtmosphericPressure = 0.65;
                    GForce = 2.4;
                    Throttle = 1.0f;
                    StagePropellantFraction = 0.72f;
                    TWR = 2.15;
                    CurrentStage = 3;
                    StageDeltaV = 1680.0;
                    TotalDeltaV = 4180.0;
                    StageBurnTime = 36.0;
                    TotalBurnTime = 180.0;
                    StageDeltaVList = CreateSimulatedStages(3, 0.72f, 1680.0, 36.0, 2.15);
                    ActiveEngines = 4;
                    Pitch = 72f;
                    Heading = 90f;
                    ElectricCharge = 385.0;
                    NetEcRate = -0.85;
                    BusVoltage = 27.6f;
                    break;

                case FlightScenario.MaxQ:
                    TimelineTime = 42f;
                    SurfaceSpeed = 540.0;
                    OrbitalSpeed = 710.0;
                    AltitudeASL = 11500.0;
                    AltitudeAGL = 11426.0;
                    VerticalSpeed = 460.0;
                    NormalizedVSI = 0.94f;
                    DynamicPressure = 34.2; // 极值压力
                    AtmosphericPressure = 0.28;
                    GForce = 3.6;           // 极大过载
                    Throttle = 0.85f;       // 减推力穿过最大动压区
                    StagePropellantFraction = 0.45f;
                    TWR = 2.85;
                    CurrentStage = 3;
                    StageDeltaV = 1050.0;
                    TotalDeltaV = 3550.0;
                    StageBurnTime = 22.0;
                    TotalBurnTime = 166.0;
                    StageDeltaVList = CreateSimulatedStages(3, 0.45f, 1050.0, 22.0, 2.85);
                    ActiveEngines = 4;
                    Pitch = 58f;
                    Heading = 90f;
                    ElectricCharge = 370.0;
                    NetEcRate = -1.2;
                    BusVoltage = 27.2f;
                    break;

                case FlightScenario.MECOAndStaging:
                    TimelineTime = 65f;
                    SurfaceSpeed = 1250.0;
                    OrbitalSpeed = 1480.0;
                    AltitudeASL = 48000.0;
                    AltitudeAGL = 48000.0;
                    VerticalSpeed = 520.0;
                    NormalizedVSI = 0.95f;
                    DynamicPressure = 0.8;
                    AtmosphericPressure = 0.01;
                    GForce = 0.2; // 级间微重力
                    Throttle = 0.0f;
                    StagePropellantFraction = 0.98f; // 下一级充满
                    TWR = 1.65;
                    CurrentStage = 2;               // 分级完成
                    StageDeltaV = 1820.0;
                    TotalDeltaV = 2500.0;
                    StageBurnTime = 74.0;
                    TotalBurnTime = 134.0;
                    StageDeltaVList = CreateSimulatedStages(2, 0.98f, 1820.0, 74.0, 1.65);
                    ActiveEngines = 1;
                    Pitch = 32f;
                    Heading = 90f;
                    ElectricCharge = 355.0;
                    NetEcRate = -0.4;
                    BusVoltage = 26.9f;
                    break;

                case FlightScenario.OrbitalCruise:
                    TimelineTime = 95f;
                    SurfaceSpeed = 2150.0;
                    OrbitalSpeed = 2285.0;
                    AltitudeASL = 120500.0;
                    AltitudeAGL = 120500.0;
                    VerticalSpeed = 0.0;
                    NormalizedVSI = 0.5f;
                    DynamicPressure = 0.0;
                    AtmosphericPressure = 0.0;
                    GForce = 0.0; // 零重力
                    Throttle = 0.0f;
                    StagePropellantFraction = 0.65f;
                    TWR = 0.0;
                    CurrentStage = 1;
                    StageDeltaV = 680.0;
                    TotalDeltaV = 680.0;
                    StageBurnTime = 60.0;
                    TotalBurnTime = 60.0;
                    StageDeltaVList = CreateSimulatedStages(1, 0.65f, 680.0, 60.0, 0.95);
                    ActiveEngines = 1;
                    Apoapsis = 124000.0;
                    Periapsis = 118500.0;
                    TimeToAp = 1450.0;
                    TimeToPe = 320.0;
                    SemiMajorAxis = 721250.0;
                    Eccentricity = 0.0038;
                    Inclination = 28.5;
                    LongitudeOfAscendingNode = 45.2;
                    ArgumentOfPeriapsis = 120.0;
                    TrueAnomaly = 65.0;
                    OrbitalPeriod = 3280.0;
                    Pitch = 0f;
                    Heading = 90f;
                    ElectricCharge = 398.0;
                    NetEcRate = 12.4;  // 太阳能全开充电
                    SolarPower = 14.2;
                    BusVoltage = 28.1f;
                    CommSignal = 1.0;
                    IsConnected = true;
                    ControlLevelStr = "FULL CONTROL";
                    HasManeuverNode = true;
                    ManeuverSource = "SIMULATION";
                    ManeuverTotalDeltaV = 320.0;
                    ManeuverDeltaV = 320.0;
                    ManeuverDeltaVPrograde = 310.0;
                    ManeuverDeltaVNormal = 75.0;
                    ManeuverDeltaVRadial = -25.0;
                    ManeuverTimeToNode = 180.0;
                    ManeuverBurnTime = 24.0;
                    ManeuverTimeToBurn = 168.0;
                    break;

                case FlightScenario.PowerCrisis:
                    TimelineTime = 110f;
                    SurfaceSpeed = 2150.0;
                    OrbitalSpeed = 2285.0;
                    AltitudeASL = 120500.0;
                    AltitudeAGL = 120500.0;
                    VerticalSpeed = 0.0;
                    DynamicPressure = 0.0;
                    AtmosphericPressure = 0.0;
                    GForce = 0.0;
                    CurrentStage = 1;
                    StageDeltaV = 680.0;
                    TotalDeltaV = 680.0;
                    StageBurnTime = 60.0;
                    TotalBurnTime = 60.0;
                    StageDeltaVList = CreateSimulatedStages(1, 0.15f, 680.0, 60.0, 0.95);
                    ElectricCharge = 35.0;  // 仅余 < 10%
                    NetEcRate = -4.5;       // 净放电急剧流失
                    SolarPower = 0.0;       // 天体阴影无光照
                    BusVoltage = 22.4f;     // 低电压严重告警
                    break;

                case FlightScenario.ReentryBlackout:
                    TimelineTime = 118f;
                    SurfaceSpeed = 1680.0;
                    OrbitalSpeed = 1820.0;
                    AltitudeASL = 28000.0;
                    AltitudeAGL = 27926.0;
                    VerticalSpeed = -420.0; // 垂直大角度俯冲
                    NormalizedVSI = 0.08f;
                    DynamicPressure = 28.5; // 再入动压极值
                    AtmosphericPressure = 0.15;
                    GForce = 4.2;           // 高减速过载
                    Throttle = 0.0f;
                    StagePropellantFraction = 0.12f;
                    CurrentStage = 0;
                    StageDeltaV = 0.0;
                    TotalDeltaV = 0.0;
                    StageBurnTime = 0.0;
                    TotalBurnTime = 0.0;
                    StageDeltaVList = CreateSimulatedStages(0, 0.0f, 0.0, 0.0, 0.0);
                    Pitch = -28f;
                    CommSignal = 0.0;       // 等离子体黑障断网
                    IsConnected = false;
                    ControlLevelStr = "NO LINK";
                    ElectricCharge = 180.0;
                    NetEcRate = -2.1;
                    BusVoltage = 25.2f;
                    break;
            }
        }

        public void Update(float dt)
        {
            if (!IsPlaying) return;

            if (!IsGamePaused)
            {
                MissionTime += dt * TimeWarpRate;
                UniversalTime += dt * TimeWarpRate;
            }

            TimelineTime += dt * PlaybackSpeed;
            if (TimelineTime > MaxTimelineTime)
            {
                TimelineTime = 0f;
            }

            // 平滑连续物理动态推演 (平滑插值，避免阶跃)
            float t = TimelineTime;
            if (t < 35f)
            {
                // 发射上升段
                float progress = t / 35f;
                SurfaceSpeed = Mathf.Lerp(0f, 450f, progress * progress);
                AltitudeASL = 74f + progress * progress * 9500f;
                AltitudeAGL = AltitudeASL - 74f;
                VerticalSpeed = Mathf.Lerp(0f, 380f, progress);
                DynamicPressure = Mathf.Lerp(0f, 32f, Mathf.Sin(progress * Mathf.PI * 0.9f));
                AtmosphericPressure = Mathf.Clamp01(Mathf.Lerp(1.0f, 0.35f, progress));
                GForce = Mathf.Lerp(1.0f, 3.2f, progress);
                Throttle = 1.0f;
                StagePropellantFraction = Mathf.Clamp01(1.0f - (progress * 0.45f));
                Pitch = Mathf.Lerp(90f, 65f, progress);
            }
            else if (t < 75f)
            {
                // MaxQ 到分级段
                float progress = (t - 35f) / 40f;
                SurfaceSpeed = Mathf.Lerp(450f, 1600f, progress);
                AltitudeASL = Mathf.Lerp(9500f, 65000f, progress);
                AltitudeAGL = AltitudeASL;
                VerticalSpeed = Mathf.Lerp(380f, 600f, progress);
                DynamicPressure = Mathf.Lerp(32f, 0.2f, progress);
                AtmosphericPressure = Mathf.Clamp01(Mathf.Lerp(0.35f, 0.0f, progress));
                GForce = Mathf.Lerp(3.2f, 1.2f, progress);
                Pitch = Mathf.Lerp(65f, 25f, progress);
                StagePropellantFraction = Mathf.Clamp01(0.55f - (progress * 0.50f));
            }
            else
            {
                // 入轨巡航
                float progress = (t - 75f) / 45f;
                SurfaceSpeed = Mathf.Lerp(1600f, 2280f, progress);
                AltitudeASL = Mathf.Lerp(65000f, 120000f, progress);
                AltitudeAGL = AltitudeASL;
                VerticalSpeed = Mathf.Lerp(600f, 0f, progress);
                DynamicPressure = 0.0;
                AtmosphericPressure = 0.0;
                GForce = Mathf.Lerp(1.2f, 0.0f, progress);
                Pitch = Mathf.Lerp(25f, 0f, progress);
                Throttle = 0.0f;
            }

            // 垂直速度归一化 (-100 ~ +100 m/s 映射到 0~1)
            float sign = Mathf.Sign((float)VerticalSpeed);
            float mag = Mathf.Abs((float)VerticalSpeed);
            float scaled = Mathf.Log10(Mathf.Clamp(mag, 0f, 100f) + 1f) / Mathf.Log10(101f);
            NormalizedVSI = Mathf.Clamp01(0.5f + sign * scaled * 0.5f);
            Mach = SurfaceSpeed / 340.0;

            // 飞行操纵舵面仿真动量
            PitchInput = Mathf.Clamp(Mathf.Sin(t * 1.5f) * 0.35f + PitchTrim, -1f, 1f);
            RollInput = Mathf.Clamp(Mathf.Cos(t * 1.2f) * 0.20f + RollTrim, -1f, 1f);
            YawInput = Mathf.Clamp(Mathf.Sin(t * 0.8f) * 0.15f + YawTrim, -1f, 1f);

            // 机动节点动力学演化
            if (HasManeuverNode)
            {
                if (ManeuverTimeToNode > -30.0)
                {
                    ManeuverTimeToNode -= dt;
                    ManeuverTimeToBurn = ManeuverTimeToNode - (ManeuverBurnTime * 0.5);
                    if (ManeuverTimeToBurn <= 0.0 && ManeuverDeltaV > 0.0)
                    {
                        double prevDv = ManeuverDeltaV;
                        double burnRate = ManeuverBurnTime > 0.1 ? (ManeuverTotalDeltaV / ManeuverBurnTime) : 10.0;
                        ManeuverDeltaV = Math.Max(0.0, ManeuverDeltaV - burnRate * dt);
                        double fraction = prevDv > 0.001 ? (ManeuverDeltaV / prevDv) : 0.0;
                        ManeuverDeltaVPrograde *= fraction;
                        ManeuverDeltaVNormal *= fraction;
                        ManeuverDeltaVRadial *= fraction;
                    }
                }
            }
        }

        public void WarpToManeuverNode()
        {
            if (HasManeuverNode && ManeuverTimeToBurn > 15.0)
            {
                ManeuverTimeToNode = 15.0 + (ManeuverBurnTime * 0.5);
                ManeuverTimeToBurn = 15.0;
            }
        }

        public void DeleteManeuverNode()
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
    }
}
