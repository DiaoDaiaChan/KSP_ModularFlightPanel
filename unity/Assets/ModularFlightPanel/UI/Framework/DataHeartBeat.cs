using System;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电数据心跳上下文参数包（强类型只读传递，零 GC 托管堆分配）
    /// 专用于由父类按 EffectiveHeartBeatTier 节流调度的数据推算与物理量解算。
    /// </summary>
    public struct FlightHeartbeatContext
    {
        /// <summary>飞行实时遥测数据源快照</summary>
        public IFlightTelemetry Telemetry;

        /// <summary>距离上一次心跳执行的时间步长 (秒)</summary>
        public float DeltaTime;

        /// <summary>载具是否处于机动或高动态受力状态 (油门>0 / 动压>0.1 / 存在机动节点)</summary>
        public bool IsManeuvering;

        /// <summary>KSP 全局普适时间戳 (UT 秒)</summary>
        public double UniversalTime;

        public FlightHeartbeatContext(IFlightTelemetry telem, float dt)
        {
            Telemetry = telem;
            DeltaTime = dt;
            IsManeuvering = telem != null && (telem.Throttle > 0.01f || telem.DynamicPressure > 0.1 || telem.HasManeuverNode);
            UniversalTime = telem != null ? telem.UniversalTime : 0.0;
        }
    }

    /// <summary>
    /// 【核心航电数据心跳契约接口】
    /// 规定所有模块化飞行组件的数据刷新契约。
    /// 核心约束法则：
    /// 1. 约束规则：HeartBeat 采样频率必须小于等于组件渲染刷新率 (RefreshTier)；
    /// 2. 缺省等价机制：默认 HeartBeatTier 等于 RefreshTier，零学习负担；
    /// 3. 父类绝对收敛：若子类误配倒挂频率，父类底层自动钳位截断至 RefreshTier。
    /// </summary>
    public interface IDataHeartBeat
    {
        /// <summary>
        /// 数据心跳阶梯（物理采样频率）
        /// </summary>
        WidgetRefreshTier HeartBeatTier { get; }

        /// <summary>
        /// 数据心跳刷新入口（由父类 MasterUpdateTelemetry 节律分发）
        /// </summary>
        void OnDataHeartBeat(in FlightHeartbeatContext context);
    }
}
