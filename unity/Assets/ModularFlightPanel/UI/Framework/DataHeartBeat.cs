using System;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电数据心跳上下文参数包（强类型只读传递，零 GC 托管堆分配）
    /// 专用于由父类按 EffectiveHeartBeatTier 节流调度的数据推算与物理量解算。
    /// 内置无感多频子节拍算子（Every、EveryNth、EverySeconds、Adaptive）与全机跨组件联动广播（Publish）。
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

        /// <summary>当前组件从诞生起累计的心跳周期递增序号 (0, 1, 2...，用于无分配整数取模分频)</summary>
        public int Tick;

        /// <summary>当前正在调度执行的宿主小组件引用</summary>
        public BaseFlightWidget Widget;

        public FlightHeartbeatContext(IFlightTelemetry telem, float dt, int tick = 0, BaseFlightWidget widget = null)
        {
            Telemetry = telem;
            DeltaTime = dt;
            IsManeuvering = telem != null && (telem.Throttle > 0.01f || telem.DynamicPressure > 0.1 || telem.HasManeuverNode);
            UniversalTime = telem != null ? telem.UniversalTime : 0.0;
            Tick = tick;
            Widget = widget;
        }

        /// <summary>
        /// 每 N 个心跳拍触发一次 (极简整数取模，零 GC，用于子组件内部快速分频)
        /// </summary>
        public bool EveryNth(int nth)
        {
            if (nth <= 1) return true;
            return (Tick % nth) == 0;
        }

        /// <summary>
        /// 目标刷新阶梯分频判定：根据当前组件的心跳频率与目标阶梯自动折算整除比率。
        /// 例如组件主心跳为 30Hz，Every(Relaxed 10Hz) 自动换算为 30/10=3，即每 3 拍触发一次。
        /// 子类无需声明任何计时器变量即可直接实现 10Hz 中频计算！
        /// </summary>
        public bool Every(WidgetRefreshTier targetTier)
        {
            if (Widget == null) return true;
            WidgetRefreshTier currentTier = Widget.EffectiveHeartBeatTier;
            int currentHz = GetTierApproxHz(currentTier);
            int targetHz = GetTierApproxHz(targetTier);
            if (currentHz <= targetHz) return true;
            int ratio = currentHz / targetHz;
            return (Tick % Math.Max(1, ratio)) == 0;
        }

        /// <summary>
        /// 命名时间通道分频判定 (免声明私有计时器变量)：只需传入业务通道名与秒数，父类自动维护时间戳。
        /// 例如 ctx.EverySeconds("orbit", 1.0f) 每 1 秒触发一次。
        /// </summary>
        public bool EverySeconds(string channelKey, float seconds)
        {
            if (Widget == null) return true;
            return Widget.CheckChannelElapsed(channelKey, seconds);
        }

        /// <summary>
        /// 自适应巡航/机动分频：
        /// 当载具点火、机动或处于高动态环境时，以全频心跳刷新；
        /// 在平稳巡航或怠速时，自动降频至 cruiseTier (默认 Relaxed 10Hz 或 UltraLow 2Hz)。
        /// </summary>
        public bool Adaptive(WidgetRefreshTier cruiseTier = WidgetRefreshTier.Relaxed, bool forceHigh = false)
        {
            return IsManeuvering || forceHigh || Every(cruiseTier);
        }

        /// <summary>
        /// 跨仪表发布自定义计算结果与状态通道，供全机舱其他组件无缝联动读取。
        /// </summary>
        public void Publish(string key, object value)
        {
            Widget?.PublishDataChannel(key, value);
        }

        private static int GetTierApproxHz(WidgetRefreshTier tier)
        {
            switch (tier)
            {
                case WidgetRefreshTier.Critical: return 60;
                case WidgetRefreshTier.Standard: return 60;
                case WidgetRefreshTier.Slow:     return 30;
                case WidgetRefreshTier.Relaxed:  return 10;
                case WidgetRefreshTier.UltraLow: return 2;
                default: return 30;
            }
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
