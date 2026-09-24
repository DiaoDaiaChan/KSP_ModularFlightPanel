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
        public double AltitudeASL { get; private set; } = 74.0;
        public double AltitudeAGL { get; private set; } = 0.0;
        public double DisplayAltitude => AltitudeASL;
        public double VerticalSpeed { get; private set; } = 0.0;
        public float NormalizedVSI { get; private set; } = 0.5f;
        public bool IsTouchdownAlert => (AltitudeAGL < 300.0 && VerticalSpeed < -1.5);
        public double DynamicPressure { get; private set; } = 0.0;
        public double AtmosphericPressure { get; private set; } = 1.0;
        public double GForce { get; private set; } = 1.0;

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

        // 机动节点
        public bool HasManeuverNode { get; private set; } = false;
        public double ManeuverDeltaV { get; private set; } = 0.0;
        public double ManeuverTotalDeltaV { get; private set; } = 0.0;
        public double ManeuverTimeToNode { get; private set; } = 0.0;
        public double ManeuverBurnTime { get; private set; } = 0.0;
        public double ManeuverTimeToBurn { get; private set; } = 0.0;

        // 电气系统
        public double ElectricCharge { get; private set; } = 400.0;
        public double MaxElectricCharge { get; private set; } = 400.0;
        public double EcPercent => MaxElectricCharge > 0.001 ? (ElectricCharge / MaxElectricCharge * 100.0) : 100.0;
        public double NetEcRate { get; private set; } = 0.0;
        public float BusVoltage { get; private set; } = 28.0f;
        public double SolarPower { get; private set; } = 0.0;

        private static readonly CommLinkInfo[] DefaultSimulationLinks = new CommLinkInfo[]
        {
            new CommLinkInfo("未命名飞船3", 63000.0, 1.0f, false),
            new CommLinkInfo("未命名飞船3 探测器", 126000.0, 1.0f, false),
            new CommLinkInfo("未命名飞船3", 504000.0, 1.0f, false),
            new CommLinkInfo("未命名飞船3 探测器", 504000.0, 1.0f, false),
            new CommLinkInfo("未命名飞船3 探测器", 252000.0, 1.0f, false),
            new CommLinkInfo("KSAT - Singapore", 15800.0, 1.0f, true)
        };

        // 通信网络
        public double CommSignal { get; private set; } = 0.90;
        public bool IsConnected { get; private set; } = true;
        public string ControlLevelStr { get; private set; } = "FULL CONTROL";
        public int AntennaCount { get; private set; } = 2;
        public double SignalTx { get; private set; } = 0.90;
        public double SignalRx { get; private set; } = 0.90;
        public double DataRateBps { get; private set; } = 15800.0;
        public string DirectLinkTarget { get; private set; } = "KSAT - Singapore";
        public IReadOnlyList<CommLinkInfo> ActiveCommLinks { get; private set; } = DefaultSimulationLinks;

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

        public void ApplyScenario(FlightScenario scenario)
        {
            CurrentScenario = scenario;
            HasManeuverNode = false;
            ManeuverDeltaV = 0.0;
            ManeuverTotalDeltaV = 0.0;
            ManeuverTimeToNode = 0.0;
            ManeuverBurnTime = 0.0;
            ManeuverTimeToBurn = 0.0;

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
                    CurrentStage = 3;
                    StageDeltaV = 2350.0;
                    TotalDeltaV = 4850.0;
                    StageBurnTime = 52.0;
                    TotalBurnTime = 196.0;
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(3, 2350.0, 52.0, 1.65, 310.0, true),
                        new StageDeltaVInfo(2, 1820.0, 84.0, 1.40, 345.0, false),
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, false)
                    };
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(3, 1680.0, 36.0, 2.15, 312.0, true),
                        new StageDeltaVInfo(2, 1820.0, 84.0, 1.40, 345.0, false),
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, false)
                    };
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(3, 1050.0, 22.0, 2.85, 315.0, true),
                        new StageDeltaVInfo(2, 1820.0, 84.0, 1.40, 345.0, false),
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, false)
                    };
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(2, 1820.0, 74.0, 1.65, 348.0, true),
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, false)
                    };
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, true)
                    };
                    ActiveEngines = 1;
                    Apoapsis = 124000.0;
                    Periapsis = 118500.0;
                    TimeToAp = 1450.0;
                    TimeToPe = 320.0;
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
                    ManeuverTotalDeltaV = 320.0;
                    ManeuverDeltaV = 320.0;
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(1, 680.0, 60.0, 0.95, 380.0, true)
                    };
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
                    StageDeltaVList = new List<StageDeltaVInfo>
                    {
                        new StageDeltaVInfo(0, 0.0, 0.0, 0.0, 0.0, true)
                    };
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
                        double burnRate = ManeuverBurnTime > 0.1 ? (ManeuverTotalDeltaV / ManeuverBurnTime) : 10.0;
                        ManeuverDeltaV = Math.Max(0.0, ManeuverDeltaV - burnRate * dt);
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
            ManeuverDeltaV = 0.0;
            ManeuverTotalDeltaV = 0.0;
            ManeuverTimeToNode = 0.0;
            ManeuverBurnTime = 0.0;
            ManeuverTimeToBurn = 0.0;
        }
    }
}
