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

    public enum SpeedDisplayMode
    {
        Surface,
        Orbit,
        Target
    }

    /// <summary>
    /// 单台发动机的实时遥测快照 (纯 C# 值类型，无 KSP 依赖)。
    /// 用于 EICAS 类多发表组件逐台渲染，天然支持异构发动机集群
    /// (不同推力/不同燃料类型的发动机各自持有独立读数)。
    /// </summary>
    public struct EngineTelemetryInfo
    {
        /// <summary>该发动机所在部件名称 (异构集群下用于区分不同型号)。</summary>
        public string PartName;

        /// <summary>该发动机使用的推进剂显示名 (如 "Liquid Fuel/Oxidizer")。</summary>
        public string PropellantName;

        /// <summary>指令推力设定 (0~1)：即玩家油门/发动机限制百分比，对应 EICAS 的 N1 读数。</summary>
        public float CommandedThrottle;

        /// <summary>实时推力 (kN)：该发动机当前实际输出的推力，对应 EICAS 的 N2 读数。</summary>
        public float CurrentThrust;

        /// <summary>该发动机满推力额定值 (kN)，用于推力百分比归一化。</summary>
        public float MaxThrust;

        /// <summary>实时燃料消耗率 (kg/s 或单位/s)：用于 FF 读数。</summary>
        public float FuelFlow;

        /// <summary>是否处于工作状态 (已点火且未熄火)。</summary>
        public bool IsOperational;

        /// <summary>实时推力占额定推力的比例 (0~1)，安全归一化。</summary>
        public float ThrustFraction => MaxThrust > 0.001f ? Mathf.Clamp01(CurrentThrust / MaxThrust) : 0f;

        /// <summary>
        /// 该发动机部件的实时温度 (°C)。取自 KSP 原生 part.temperature，
        /// 引擎工作时会真实升温，作为 EGT (排气温度) 的可用替代量（非真实排气温度）。
        /// </summary>
        public float PartTemperature;
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

        /// <summary>
        /// 当前分级所有发动机的逐台遥测快照。顺序稳定 (与部件挂载顺序一致)，
        /// 供 EICAS 多发组件自适应渲染；无发动机时返回空列表而非 null。
        /// </summary>
        IReadOnlyList<EngineTelemetryInfo> Engines { get; }

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
        int SolarPanelsTotal { get; }
        int SolarPanelsActive { get; }
        double RtgPower { get; }
        int RtgCount { get; }
        double FuelCellPower { get; }
        int FuelCellCount { get; }
        int FuelCellActiveCount { get; }
        double AlternatorPower { get; }
        int AlternatorCount { get; }
        double TotalPowerGeneration { get; }
        double TotalPowerConsumption { get; }
        double TimeToDepletionSeconds { get; }
        double TimeToFullSeconds { get; }
        IReadOnlyList<BatteryTelemetryInfo> Batteries { get; }

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

    /// <summary>
    /// 标准化机载飞行控制指令接口 (Pure Unity / C# 契约)
    /// 将双向操控指令与只读遥测数据解耦，落实接口隔离原则 (ISP)。
    /// 操控型仪表 (SAS罗盘、分级控制台、时钟加速等) 面向此接口工作。
    /// </summary>
    public interface IFlightControl
    {
        // 飞行姿态与 SAS / RCS 控制
        void SetSASMode(FlightSASMode mode);
        void ToggleSAS();
        void ToggleRCS();
        void CycleSpeedMode();

        // 分级与飞行模式
        void ActivateNextStage();
        void ToggleStageLock();
        void TogglePrecisionMode();
        void ToggleFlightMode();

        // 机动节点
        void WarpToManeuverNode();
        void DeleteManeuverNode();

        // 时间加速与暂停控制
        void IncreaseTimeWarp();
        void DecreaseTimeWarp();
        void CancelTimeWarp();
        void TogglePause();
        void SetTimeWarpRateIndex(int index);
    }

    /// <summary>
    /// 飞行瞬态动力学与状态机事件类型 (Flight Transient Event Type)
    /// 用于主告警光字牌、时序甘特轴、ECAM状态条与机动指示卡等航电组件。
    /// </summary>
    public enum FlightTransientEventType
    {
        None = 0,
        Separation,         // 分级分离 / 脱扣
        EngineStart,        // 引擎启动 / 点火
        MECO,               // 主发关机 / 熄火
        ManeuverApproach,   // 接近机动节点 (T-60s)
        ManeuverBurn,       // 机动点火执行
        OrbitAchieved,      // 入轨圆化完成 (Stable Orbit)
        Deorbit,            // 飞船离轨制动 / 进入再入走廊
        AtmosphereEntry,    // 再入/进入大气层 (Entry Interface)
        Blackout,           // 再入等离子体黑障
        Escape,             // 逃逸轨道建立 (双曲线逃逸)
        SoiTransition,      // 穿越引力范围 (SOI 切换)
        SuicideBurn,        // 动力减速着陆点火
        ApoapsisPass,       // 通过远拱点
        PeriapsisPass,      // 通过近拱点
        DockingMode,        // 进入对接模式
        Touchdown,          // 着陆接地成功
        MaxQ,               // 突破最大动压 (Max Q Passed)
        V1Rotate,           // GPWS 起飞决断/抬轮速度 (V1 Decision / Rotate)
        SolarStorm,         // Kerbalism 太阳风暴冲击 (CME / Solar Storm)
        AvionicsLock,       // RP-1 航电失控锁定 (Avionics Locked)
        TerrainImpact,      // Trajectories 预测地表撞击告警 (Ground Impact Imminent)
        DockingCapture,     // DPAI 端口对接锁扣捕获 (Docking Captured)
        EngineFailure,      // TestFlight 发动机故障失效 (Engine Failure)
        ThermalOverheat     // SystemHeat 热回路过热紧急告警 (Thermal Loop Overheat)
    }

    /// <summary>
    /// 全局机载遥测数据上下文访问点
    /// 允许任何 UI 组件与无头渲染器直接接入标准 IFlightTelemetry 提供者
    /// 实现了 UI 表现层与游戏核心引擎的彻底解耦
    /// </summary>
    public static class FlightTelemetryContext
    {
        private static IFlightTelemetry _provider;
        public static Func<IFlightTelemetry> FallbackProvider { get; set; }

        public static IFlightTelemetry Current
        {
            get => _provider ?? (FallbackProvider != null ? FallbackProvider() : null);
            set => _provider = value;
        }

        /// <summary>
        /// 全局机载控制指令访问点 (ISP 隔离接口)
        /// </summary>
        public static IFlightControl Control => Current;

        public static void SetProvider(IFlightTelemetry provider)
        {
            _provider = provider;
        }

        public static void Reset()
        {
            _provider = null;
        }
    }

    /// <summary>
    /// 标准化单根天线工况与遥测快照 (CommNet / RealAntennas Antenna Telemetry Snapshot)
    /// </summary>
    public struct AntennaTelemetryInfo
    {
        public string Name;
        public string TypeStr; // "DIRECT", "RELAY", "INTERNAL"
        public double Power;
        public string PowerFormatted; // "5.0k", "2.0M", "100G"
        public float SignalStrength; // 0.0f .. 1.0f
        public string Status; // "LINKED", "SEARCHING", "RETRACTED", "OFFLINE"
        public bool IsOperational;

        public AntennaTelemetryInfo(string name, string typeStr, double power, string powerFormatted, float signalStrength, string status, bool isOperational)
        {
            Name = name ?? "ANTENNA";
            TypeStr = typeStr ?? "DIRECT";
            Power = power;
            PowerFormatted = powerFormatted ?? "---";
            SignalStrength = signalStrength;
            Status = status ?? "OFFLINE";
            IsOperational = isOperational;
        }

        public string SpecSummary => $"{TypeStr}  ·  {PowerFormatted} POWER";
    }

    /// <summary>
    /// 标准化通信网络对端链路遥测快照 (CommNet / RealAntennas Active Link Info)
    /// </summary>
    public struct CommLinkInfo
    {
        public string PeerName;
        public double DataRateBps;
        public float SignalStrength; // 0.0f .. 1.0f
        public bool IsDirectHome;

        public CommLinkInfo(string peerName, double dataRateBps, float signalStrength, bool isDirectHome = false)
        {
            PeerName = peerName;
            DataRateBps = dataRateBps;
            SignalStrength = signalStrength;
            IsDirectHome = isDirectHome;
        }

        public string FormattedDataRate => FormatRate(DataRateBps);

        public static string FormatRate(double bps)
        {
            if (bps <= 0.0) return "0.0 bps";
            if (bps >= 1000000.0)
                return $"{(bps / 1000000.0):F1} Mbps";
            if (bps >= 1000.0)
                return $"{(bps / 1000.0):F1} Kbps";
            return $"{bps:F0} bps";
        }
    }

    /// <summary>
    /// 单个机载蓄电池组遥测快照 (Pure C# Contract)
    /// </summary>
    public struct BatteryTelemetryInfo
    {
        public string PartTitle;
        public double Amount;
        public double MaxAmount;
        public bool IsFlowEnabled;
        public bool IsDedicated;

        public BatteryTelemetryInfo(string partTitle, double amount, double maxAmount, bool isFlowEnabled, bool isDedicated)
        {
            PartTitle = partTitle ?? string.Empty;
            Amount = amount;
            MaxAmount = maxAmount;
            IsFlowEnabled = isFlowEnabled;
            IsDedicated = isDedicated;
        }

        public double Percent => MaxAmount > 0.001 ? (Amount / MaxAmount * 100.0) : 100.0;
    }

    /// <summary>
    /// 单级部件图标与推进剂状态数据模型 (Pure Unity / C# Contract)
    /// 解耦原版 KSP StageIcon、ProtoStageIcon 与 DefaultIcons 枚举
    /// </summary>
    public struct StagePartIconData
    {
        public string IconType;           // 部件图标类型
        public int IconTypeIndex;         // DefaultIcons 索引编号
        public int Count;                 // 部件对称/数量倍率
        public string PartTitle;          // 部件显示名称
        public string PropellantName;     // 推进剂类型名称
        public float PropellantFraction;  // 推进剂余量比例 (0.0 ~ 1.0, 若非推进部件则为 -1.0)
        public Rect StockUvRect;          // 原版 StageIcon 贴图图集 UV 矩形
        public bool HasStockUv;           // 是否包含原版有效 UV 坐标
        public uint PartFlightId;         // 部件全局唯一 ID (flightID / craftID)，用于场景高亮与跨级移动
        public bool IsGroupLeader;        // 是否为对称组领头图标
        public bool IsExpanded;           // 是否处于展开状态

        public StagePartIconData(string iconType, int iconTypeIndex, int count, string partTitle = "", string propName = null, float propFrac = -1f, Rect stockUv = default, bool hasStockUv = false, uint partFlightId = 0, bool isGroupLeader = false, bool isExpanded = false)
        {
            IconType = iconType;
            IconTypeIndex = iconTypeIndex;
            Count = count > 0 ? count : 1;
            PartTitle = partTitle ?? string.Empty;
            PropellantName = propName;
            PropellantFraction = propFrac;
            StockUvRect = stockUv;
            HasStockUv = hasStockUv;
            PartFlightId = partFlightId;
            IsGroupLeader = isGroupLeader;
            IsExpanded = isExpanded;
        }
    }

    /// <summary>
    /// 单级火箭动力与燃烧遥测数据模型 (Pure C# Contract)
    /// </summary>
    public struct StageDeltaVInfo
    {
        public int Stage;                                    // 级数编号 (如 0, 1, 2...)
        public double DeltaV;                                // 该级可用 Delta-V (m/s)
        public double BurnTime;                              // 该级发动机全推力工作时间 (秒)
        public double TWR;                                   // 该级起步/平均推重比
        public double Isp;                                   // 该级比冲 (秒)
        public bool IsActive;                                // 是否为当前正在工作的激活级
        public IReadOnlyList<StagePartIconData> PartIcons;   // 该级触发的部件图标列表

        public StageDeltaVInfo(int stage, double dv, double burnTime, double twr = 0.0, double isp = 0.0, bool isActive = false, IReadOnlyList<StagePartIconData> partIcons = null)
        {
            Stage = stage;
            DeltaV = dv;
            BurnTime = burnTime;
            TWR = twr;
            Isp = isp;
            IsActive = isActive;
            PartIcons = partIcons ?? Array.Empty<StagePartIconData>();
        }
    }

    /// <summary>
    /// 外部探针位图标志（零开销位掩码，消除字符串字典查询）
    /// </summary>
    [Flags]
    public enum ProbeTagFlags : uint
    {
        None = 0,
        FAR = 1 << 0,
        KER = 1 << 1,
        MJ = 1 << 2,
        Principia = 1 << 3,
        RealAntennas = 1 << 4,
        Kerbalism = 1 << 5,
        Trajectories = 1 << 6,
        Docking = 1 << 7,
        GPWS = 1 << 8,
        RealFuels = 1 << 9,
        TestFlight = 1 << 10,
        DynamicBatteryStorage = 1 << 11,
        SystemHeat = 1 << 12,
        AtmosphereAutopilot = 1 << 13,
        RP1 = 1 << 14,
    }

    /// <summary>
    /// 第三方遥测探针解耦注册表 (FAR / KerbalEngineer / MechJeb / Principia 等 15 大模组)
    /// 允许外围探针以委托形式动态注入数据，使 TelemetryTokenEngine 与小组件彻底与第三方 DLL 解耦
    /// </summary>
    public static class ExternalProbeRegistry
    {
        public static Func<string, string, double> NumericResolver;
        public static Func<string, string, string, string> StringResolver;
        public static Func<string, bool> TagAvailabilityResolver;

        public static ProbeTagFlags AvailableFlags;

        // 零开销纳秒级硬件位掩码查询属性
        public static bool HasFar => (AvailableFlags & ProbeTagFlags.FAR) != 0;
        public static bool HasKer => (AvailableFlags & ProbeTagFlags.KER) != 0;
        public static bool HasMj => (AvailableFlags & ProbeTagFlags.MJ) != 0;
        public static bool HasPrincipia => (AvailableFlags & ProbeTagFlags.Principia) != 0;
        public static bool HasRealAntennas => (AvailableFlags & ProbeTagFlags.RealAntennas) != 0;
        public static bool HasKerbalism => (AvailableFlags & ProbeTagFlags.Kerbalism) != 0;
        public static bool HasTrajectories => (AvailableFlags & ProbeTagFlags.Trajectories) != 0;
        public static bool HasDocking => (AvailableFlags & ProbeTagFlags.Docking) != 0;
        public static bool HasGPWS => (AvailableFlags & ProbeTagFlags.GPWS) != 0;
        public static bool HasRealFuels => (AvailableFlags & ProbeTagFlags.RealFuels) != 0;
        public static bool HasTestFlight => (AvailableFlags & ProbeTagFlags.TestFlight) != 0;
        public static bool HasDynamicBatteryStorage => (AvailableFlags & ProbeTagFlags.DynamicBatteryStorage) != 0;
        public static bool HasSystemHeat => (AvailableFlags & ProbeTagFlags.SystemHeat) != 0;
        public static bool HasAtmosphereAutopilot => (AvailableFlags & ProbeTagFlags.AtmosphereAutopilot) != 0;
        public static bool HasRP1 => (AvailableFlags & ProbeTagFlags.RP1) != 0;

        public static bool IsTagAvailable(string tag)
        {
            if (AvailableFlags == ProbeTagFlags.None) return false;
            return TagAvailabilityResolver != null && TagAvailabilityResolver(tag);
        }

        public static double ResolveNumeric(string tag, string subTag)
        {
            if (NumericResolver != null)
            {
                try { return NumericResolver(tag, subTag); }
                catch (Exception ex)
                {
                    MFPLogger.WarnThrottled("Probe_Numeric_" + tag, $"External probe numeric error for {tag}:{subTag} - {ex.Message}");
                }
            }
            return double.NaN;
        }

        public static string ResolveString(string tag, string subTag, string format)
        {
            if (StringResolver != null)
            {
                try { return StringResolver(tag, subTag, format); }
                catch (Exception ex)
                {
                    MFPLogger.WarnThrottled("Probe_String_" + tag, $"External probe string error for {tag}:{subTag} - {ex.Message}");
                }
            }
            return "---";
        }
    }
}
