using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 遥测数据通配符表达式求值引擎
    /// 支持语法例如：
    ///   "{SPD}" -> 当前模式速度
    ///   "{SPD:SURF:F1}" -> 地表速度保留1位小数
    ///   "{ALT:AGL:N0}" -> 雷达真高整数
    ///   "{ALT:ASL:DIST}" -> 海拔高度智能单位 (km / m)
    ///   "{ORBIT:AP:DIST}" -> 远地点智能单位
    ///   "{ORBIT:TAP:TIME}" -> 远地点时间 (T-00:00:00)
    ///   "{HDG:D3}" -> 航向角3位整数 (如 089°)
    ///   "{PITCH:F1}", "{ROLL:F1}" -> 俯仰与滚转
    ///   "{VSI:F1}" -> 垂直爬升速率
    ///   "{TWR:F2}" -> 推重比
    ///   "{GFORCE:F1}" -> 重力加速度
    ///   "{Q:F2}" -> 大气动压 (kPa)
    ///   "{STAGE:PROP:P0}" -> 当前级推进剂百分比
    /// </summary>
    public static class TelemetryTokenEngine
    {
        private static readonly Regex TokenRegex = new Regex(@"\{([A-Za-z0-9_]+)(?::([A-Za-z0-9_]+))?(?::([A-Za-z0-9_]+))?\}", RegexOptions.Compiled);

        public static string Evaluate(string template, TelemetryHub telemetry)
        {
            if (string.IsNullOrEmpty(template) || telemetry == null) return template ?? string.Empty;

            return TokenRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value.ToUpperInvariant();
                string subTag = match.Groups[2].Success ? match.Groups[2].Value.ToUpperInvariant() : string.Empty;
                string format = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

                return ResolveToken(tag, subTag, format, telemetry);
            });
        }

        private static string ResolveToken(string tag, string subTag, string format, TelemetryHub telem)
        {
            Vessel v = telem.ActiveVessel;
            if (v == null) return "---";

            switch (tag)
            {
                case "SPD":
                case "SPEED":
                    double spd = telem.CurrentSpeed;
                    if (subTag == "SURF") spd = telem.SurfaceSpeed;
                    else if (subTag == "OBT" || subTag == "ORBIT") spd = telem.OrbitalSpeed;
                    else if (subTag == "TGT" || subTag == "TARGET") spd = telem.TargetSpeed;
                    return FormatNumber(spd, format, "F1");

                case "ALT":
                case "ALTITUDE":
                    double alt = (subTag == "ASL") ? telem.AltitudeASL : telem.AltitudeAGL;
                    if (format == "DIST") return FormatDistance(alt);
                    return FormatNumber(alt, format, "N0");

                case "VSI":
                case "VERTSPD":
                    return FormatNumber(telem.VerticalSpeed, format, "F1");

                case "HDG":
                case "HEADING":
                    return $"{Mathf.RoundToInt(telem.Heading) % 360:D3}°";

                case "PITCH":
                    return FormatNumber(telem.Pitch, format, "F1");

                case "ROLL":
                    return FormatNumber(telem.Roll, format, "F1");

                case "THROTTLE":
                case "THR":
                    return FormatNumber(telem.Throttle * 100f, format, "F0") + "%";

                case "AP":
                case "APOAPSIS":
                    if (format == "DIST") return FormatDistance(telem.Apoapsis);
                    return FormatNumber(telem.Apoapsis, format, "N0");

                case "PE":
                case "PERIAPSIS":
                    if (format == "DIST") return FormatDistance(telem.Periapsis);
                    return FormatNumber(telem.Periapsis, format, "N0");

                case "TAP":
                    return FormatTime(Math.Max(0.0, telem.TimeToAp));

                case "TPE":
                    return FormatTime(Math.Max(0.0, telem.TimeToPe));

                case "TWR":
                    double thrust = 0.0;
                    var engines = v.FindPartModulesImplementing<ModuleEngines>();
                    if (engines != null)
                    {
                        for (int i = 0; i < engines.Count; i++)
                        {
                            if (engines[i] != null && engines[i].isOperational)
                            {
                                thrust += engines[i].finalThrust;
                            }
                        }
                    }
                    double weight = v.totalMass * (v.mainBody != null ? v.mainBody.GeeASL * 9.80665 : 9.80665);
                    double twr = weight > 0.001 ? thrust / weight : 0.0;
                    return FormatNumber(twr, format, "F2");

                case "GFORCE":
                case "G":
                    return FormatNumber(v.geeForce, format, "F1") + " G";

                case "Q":
                case "DYNAERO":
                    double q = v.dynamicPressurekPa;
                    return FormatNumber(q, format, "F2") + " kPa";

                case "MACH":
                    return FormatNumber(v.mach, format, "F2") + " M";

                case "PROP":
                case "STAGEPROP":
                    return $"{Mathf.RoundToInt(telem.StagePropellantFraction * 100f)}%";

                case "SAS":
                    return telem.IsSASEnabled ? telem.CurrentSASMode.ToString().ToUpper() : "OFF";

                case "RCS":
                    return telem.IsRCSEnabled ? "ON" : "OFF";

                case "BODY":
                    return v.mainBody != null ? v.mainBody.displayName.LocalizeRemoveGender() : "UNKNOWN";

                case "SITUATION":
                    return v.situation.ToString().ToUpper();

                default:
                    return $"{{{tag}}}";
            }
        }

        private static string FormatNumber(double val, string format, string defaultFmt)
        {
            string fmt = string.IsNullOrEmpty(format) ? defaultFmt : format;
            try
            {
                return val.ToString(fmt);
            }
            catch
            {
                return val.ToString(defaultFmt);
            }
        }

        private static string FormatDistance(double meters)
        {
            if (Math.Abs(meters) >= 1000000000.0)
                return $"{meters / 1000000000.0:F2}G m";
            if (Math.Abs(meters) >= 1000000.0)
                return $"{meters / 1000000.0:F2}M m";
            if (Math.Abs(meters) >= 10000.0)
                return $"{meters / 1000.0:F1}k m";
            return $"{meters:N0} m";
        }

        private static string FormatTime(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1.0)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}
