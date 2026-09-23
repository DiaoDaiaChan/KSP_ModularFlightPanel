using System;

namespace ModularFlightPanel.Core
{
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

        public static void SetProvider(IFlightTelemetry provider)
        {
            _provider = provider;
        }

        public static void Reset()
        {
            _provider = null;
        }
    }
}
