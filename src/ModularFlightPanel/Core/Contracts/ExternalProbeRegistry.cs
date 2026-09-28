using System;

namespace ModularFlightPanel.Core
{
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
