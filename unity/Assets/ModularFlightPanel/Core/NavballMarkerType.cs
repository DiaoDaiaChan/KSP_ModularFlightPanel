using System;
using System.Collections.Generic;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 导航球标线类型枚举（消除热循环字符串分配）
    /// </summary>
    public enum NavballMarkerType
    {
        Unknown = 0,
        Prograde,
        Retrograde,
        VelocityVector,
        AntiVelocityVector,
        Normal,
        AntiNormal,
        RadialIn,
        RadialOut,
        Target,
        AntiTarget,
        Maneuver
    }

    /// <summary>
    /// 导航标线类型辅助解析器（解耦 Unity 无头预览与 KSP 运行时）
    /// </summary>
    public static class NavballMarkerHelper
    {
        private static readonly Dictionary<string, NavballMarkerType> _markerKeyToType = new Dictionary<string, NavballMarkerType>(StringComparer.OrdinalIgnoreCase)
        {
            { "prograde", NavballMarkerType.Prograde },
            { "retrograde", NavballMarkerType.Retrograde },
            { "velocity_vector", NavballMarkerType.VelocityVector },
            { "anti_velocity_vector", NavballMarkerType.AntiVelocityVector },
            { "normal", NavballMarkerType.Normal },
            { "antinormal", NavballMarkerType.AntiNormal },
            { "radialin", NavballMarkerType.RadialIn },
            { "radialout", NavballMarkerType.RadialOut },
            { "target", NavballMarkerType.Target },
            { "antitarget", NavballMarkerType.AntiTarget },
            { "maneuver", NavballMarkerType.Maneuver }
        };

        public static NavballMarkerType GetMarkerType(string markerKey)
        {
            if (!string.IsNullOrEmpty(markerKey) && _markerKeyToType.TryGetValue(markerKey, out var type))
            {
                return type;
            }
            return NavballMarkerType.Unknown;
        }
    }
}
