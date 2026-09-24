using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 第三方遥测探针解耦注册表 (FAR / KerbalEngineer / MechJeb)
    /// 允许外围探针以委托形式动态注入数据，使 TelemetryTokenEngine 彻底与第三方 DLL 解耦
    /// </summary>
    public static class ExternalProbeRegistry
    {
        public static Func<string, string, double> NumericResolver;
        public static Func<string, string, string, string> StringResolver;

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
