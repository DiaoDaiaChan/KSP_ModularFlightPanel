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

    /// <summary>
    /// 标准化机载遥测数据接口 (Pure Unity / C# 契约)
    /// 彻底剥离对 KSP 游戏内部类 (Vessel, FlightGlobals, Part, ModuleEngines) 的依赖。
    /// 无论是在真实游戏飞行中、仿真测试模式下，还是在独立的 Unity 编辑器 / 无头渲染环境中，
    /// 所有航电 UI 组件均严格面向此接口工作。
    /// </summary>
    public interface IFlightTelemetry
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
        double CurrentSpeed { get; }
        double SurfaceSpeed { get; }
        double OrbitalSpeed { get; }
        double TargetSpeed { get; }
        double Mach { get; }

        // 高度与大气动力学
        double AltitudeASL { get; }
        double AltitudeAGL { get; }
        double DisplayAltitude { get; }
        double VerticalSpeed { get; }
        float NormalizedVSI { get; }
        double DynamicPressure { get; }
        double AtmosphericPressure { get; }
        double GForce { get; }
        bool IsTouchdownAlert { get; }

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

        // 轨道力学
        double Apoapsis { get; }
        double Periapsis { get; }
        double TimeToAp { get; }
        double TimeToPe { get; }

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
        string SpeedModeName { get; }

        // 三轴姿态操纵量与配平 (-1.0 ~ +1.0)
        float PitchInput { get; }
        float RollInput { get; }
        float YawInput { get; }
        float PitchTrim { get; }
        float RollTrim { get; }
        float YawTrim { get; }

        // 分级安全锁、模式与推进剂
        bool IsStageLocked { get; }
        bool IsPrecisionControl { get; }
        bool IsDockingMode { get; }
        string StagePropellantName { get; }

        // 控制指令回调 (支持解耦双向控制)
        void SetSASMode(FlightSASMode mode);
        void ToggleSAS();
        void ToggleRCS();
        void CycleSpeedMode();
        void ActivateNextStage();
        void ToggleStageLock();
        void TogglePrecisionMode();
        void ToggleFlightMode();

        // 时间加速控制
        void IncreaseTimeWarp();
        void DecreaseTimeWarp();
        void CancelTimeWarp();
        void TogglePause();
        void SetTimeWarpRateIndex(int index);
    }
}
