using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 航电小组件纯逻辑大脑接口 (Headless Widget Logic Contract)
    /// 将遥测数据处理、物理公式换算、单位体系决策与状态快照构建完全从 UnityEngine.MonoBehaviour 视图解耦。
    /// 纯 C#，无 UnityEngine 依赖，可在无头测试与后台线程中 100% 独立运行。
    /// </summary>
    public interface IWidgetLogic
    {
        /// <summary>
        /// 重置所有内部计算状态（例如切星体、切飞船或重新加载时）
        /// </summary>
        void Reset();

        /// <summary>
        /// 根据最新遥测快照与时间步长解算当前业务状态
        /// </summary>
        /// <param name="telemetry">只读遥测提供者</param>
        /// <param name="deltaTime">自上一帧的心跳时间步长 (秒)</param>
        void Evaluate(IFlightTelemetry telemetry, float deltaTime);
    }

    /// <summary>
    /// 强类型纯逻辑大脑基类
    /// </summary>
    /// <typeparam name="TState">只读值类型状态快照 (必须为 struct 杜绝 GC)</typeparam>
    public abstract class WidgetLogic<TState> : IWidgetLogic where TState : struct
    {
        /// <summary>
        /// 最新解算完成的状态快照，供视图层只读消费
        /// </summary>
        public TState CurrentState { get; protected set; }

        /// <inheritdoc />
        public abstract void Reset();

        /// <inheritdoc />
        public abstract void Evaluate(IFlightTelemetry telemetry, float deltaTime);
    }
}
