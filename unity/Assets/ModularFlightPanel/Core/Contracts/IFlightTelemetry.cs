using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    public enum FlightSASMode
    {
        StabilityAssist,
        Prograde,
        Retrograde,
        Normal,
        Antinormal,
        RadialIn,
        RadialOut,
        Target,
        AntiTarget,
        Maneuver
    }

    public enum AltitudeDisplayMode
    {
        Ground, // AGL 雷达真高
        Sea     // ASL 绝对海拔
    }

    /// <summary>
    /// 标准化机载遥测数据接口 (Pure Unity / C# 契约)
    /// 彻底剥离对 KSP 游戏内部类 (Vessel, FlightGlobals, Part, ModuleEngines) 的依赖。
    /// 无论是在真实游戏飞行中、仿真测试模式下，还是在独立的 Unity 编辑器 / 无头渲染环境中，
    /// 所有航电 UI 组件均严格面向此接口工作。
    /// </summary>
    public interface IFlightTelemetry : IFlightControl
    {
        bool HasVessel { get; }
        bool IsSimulationMode { get; }
        string VesselName { get; }
        string CelestialBodyName { get; }
        string FlightSituation { get; }

        // 姿态与航向
        Quaternion AttitudeRotation { get; }
        float Heading { get; }
        float Pitch { get; }
        float Roll { get; }

        // 速度与马赫数
        SpeedDisplayMode CurrentSpeedMode { get; }
        string SpeedModeName { get; }
        double CurrentSpeed { get; }
        double SurfaceSpeed { get; }
        double OrbitalSpeed { get; }
        double TargetSpeed { get; }
        double Mach { get; }

        // 高度与大气动力学 (100% 通用化天体物理参数，0 硬编码任何星球)
        AltitudeDisplayMode CurrentAltMode { get; }
        double AltitudeASL { get; }
        double AltitudeAGL { get; }
        double DisplayAltitude { get; }
        double VerticalSpeed { get; }
        float NormalizedVSI { get; }
        double DynamicPressure { get; }
        double AtmosphericPressure { get; }
        double GForce { get; }
        bool IsTouchdownAlert { get; }
        bool HasAtmosphere { get; }
        double AtmosphereDepth { get; }

        // 动力、推重比与分级推进
        float Throttle { get; }
        double TWR { get; }
        float StagePropellantFraction { get; }
        double StageDeltaV { get; }
        double TotalDeltaV { get; }
        double StageBurnTime { get; }
        double TotalBurnTime { get; }
        IReadOnlyList<StageDeltaVInfo> StageDeltaVList { get; }
        string DeltaVSource { get; }
        int CurrentStage { get; }
        int ActiveEngines { get; }
        int TotalStageEngines { get; }

        // 轨道力学与机动节点
        double Apoapsis { get; }
        double Periapsis { get; }
        double TimeToAp { get; }
        double TimeToPe { get; }
        double SemiMajorAxis { get; }
        double Eccentricity { get; }
        double Inclination { get; }
        double LongitudeOfAscendingNode { get; }
        double ArgumentOfPeriapsis { get; }
        double TrueAnomaly { get; }
        double OrbitalPeriod { get; }
        bool HasManeuverNode { get; }
        double ManeuverDeltaV { get; }
        double ManeuverTotalDeltaV { get; }
        double ManeuverTimeToNode { get; }
        double ManeuverBurnTime { get; }
        double ManeuverTimeToBurn { get; }
        double ManeuverDeltaVPrograde { get; }
        double ManeuverDeltaVNormal { get; }
        double ManeuverDeltaVRadial { get; }
        string ManeuverSource { get; }

        // 电气系统
        double ElectricCharge { get; }
        double MaxElectricCharge { get; }
        double EcPercent { get; }
        double NetEcRate { get; }
        float BusVoltage { get; }
        double SolarPower { get; }

        // 通信网络与 RealAntennas 遥测
        double CommSignal { get; }
        bool IsConnected { get; }
        string ControlLevelStr { get; }
        int AntennaCount { get; }
        double SignalTx { get; }
        double SignalRx { get; }
        double DataRateBps { get; }
        string DirectLinkTarget { get; }
        IReadOnlyList<CommLinkInfo> ActiveCommLinks { get; }
        IReadOnlyList<AntennaTelemetryInfo> Antennas { get; }

        // 时间加速与时钟遥测
        double MissionTime { get; }
        double UniversalTime { get; }
        float TimeWarpRate { get; }
        int TimeWarpRateIndex { get; }
        int MaxTimeWarpRateIndex { get; }
        bool IsPhysicsWarp { get; }
        bool IsGamePaused { get; }

        // 维生环境
        int CrewCount { get; }
        int CrewCapacity { get; }
        double CabinPressure { get; }
        double CabinTemp { get; }
        float OxygenPercent { get; }
        float MonoPercent { get; }
        float WaterPercent { get; }

        // 飞行控制与 SAS
        bool IsRCSEnabled { get; }
        bool IsSASEnabled { get; }
        FlightSASMode CurrentSASMode { get; }

        // 三轴姿态操纵量与配平 (-1.0 ~ +1.0)
        float PitchInput { get; }
        float RollInput { get; }
        float YawInput { get; }
        float PitchTrim { get; }
        float RollTrim { get; }
        float YawTrim { get; }

        // 三轴平移操纵量 (-1.0 ~ +1.0)
        float XInput { get; }
        float YInput { get; }
        float ZInput { get; }

        // 目标交会对接遥测
        bool HasTarget { get; }
        string TargetName { get; }
        double TargetDistance { get; }
        Vector3 TargetRelativePosition { get; }
        Vector3 TargetRelativeVelocity { get; }
        float TargetDeviationX { get; }
        float TargetDeviationY { get; }
        float TargetDeviationZ { get; }
        float TargetClosingSpeed { get; }
        float TargetPitchAlignment { get; }
        float TargetRollAlignment { get; }
        float TargetYawAlignment { get; }

        // 分级安全锁、模式与推进剂
        bool IsStageLocked { get; }
        bool IsPrecisionControl { get; }
        bool IsDockingMode { get; }
        string StagePropellantName { get; }

        // 瞬态事件遥测 (分级分离与点火瞬态)
        bool IsStageSeparating { get; }
        bool IsEngineIgniting { get; }

        // 注：所有双向控制指令方法已拆分解耦至 IFlightControl 接口 (落实 ISP 接口隔离原则)
    }
}
