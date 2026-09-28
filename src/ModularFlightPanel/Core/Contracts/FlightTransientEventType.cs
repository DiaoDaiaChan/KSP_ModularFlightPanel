namespace ModularFlightPanel.Core
{
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
}
