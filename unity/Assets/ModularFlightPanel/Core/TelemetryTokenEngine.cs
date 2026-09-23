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
        private static readonly Regex TokenRegex = new Regex(@"\{([A-Za-z0-9_]+)(?::([A-Za-z0-9_]+))?(?::([A-Za-z0-9_]+))?(?::([A-Za-z0-9_]+))?\}", RegexOptions.Compiled);

        /// <summary>
        /// 原生双精度数值提取（用于驱动表盘指针、弧线、带状滚动物理计算）
        /// 支持如 "{SPD}", "{ALT:AGL}", "{GFORCE}", "{Q}", "{TWR}", "{THROTTLE}", "{PROP}" 等
        /// 亦可传入无花括号的纯标识如 "GFORCE" 或 "ALT:ASL"
        /// </summary>
        public static double EvaluateNumeric(string token, IFlightTelemetry telemetry)
        {
            if (string.IsNullOrEmpty(token) || telemetry == null) return double.NaN;
            if (!telemetry.HasVessel) return double.NaN;

            string clean = token.Trim().Trim('{', '}');
            string[] parts = clean.Split(':');
            string tag = parts[0].ToUpperInvariant();
            string subTag = string.Empty;
            if (parts.Length > 2)
                subTag = parts[1].ToUpperInvariant() + ":" + parts[2].ToUpperInvariant();
            else if (parts.Length > 1)
                subTag = parts[1].ToUpperInvariant();

            switch (tag)
            {
                case "SPD":
                case "SPEED":
                    if (subTag == "SURF") return telemetry.SurfaceSpeed;
                    if (subTag == "OBT" || subTag == "ORBIT") return telemetry.OrbitalSpeed;
                    if (subTag == "TGT" || subTag == "TARGET") return telemetry.TargetSpeed;
                    return telemetry.CurrentSpeed;

                case "MACH":
                    return telemetry.Mach;

                case "ALT":
                case "ALTITUDE":
                    if (subTag == "ASL") return telemetry.AltitudeASL;
                    if (subTag == "AGL" || subTag == "RADAR") return telemetry.AltitudeAGL;
                    return telemetry.DisplayAltitude;

                case "VSI":
                case "VERTSPD":
                    return telemetry.VerticalSpeed;

                case "HDG":
                case "HEADING":
                    return (telemetry.Heading % 360.0 + 360.0) % 360.0;

                case "PITCH":
                    return telemetry.Pitch;

                case "ROLL":
                    return telemetry.Roll;

                case "THROTTLE":
                case "THR":
                    return telemetry.Throttle * 100.0;

                case "AP":
                case "APOAPSIS":
                    return telemetry.Apoapsis;

                case "PE":
                case "PERIAPSIS":
                    return telemetry.Periapsis;

                case "TAP":
                    return Math.Max(0.0, telemetry.TimeToAp);

                case "TPE":
                    return Math.Max(0.0, telemetry.TimeToPe);

                case "TWR":
                    return telemetry.TWR;

                case "GFORCE":
                case "G":
                    return telemetry.GForce;

                case "Q":
                case "DYNAERO":
                    return telemetry.DynamicPressure;

                case "ATM":
                case "ATMOSPHERE":
                case "BARO":
                    return telemetry.AtmosphericPressure;

                case "PROP":
                case "STAGEPROP":
                    return telemetry.StagePropellantFraction * 100.0;

                case "EC":
                case "ELEC":
                    if (subTag == "MAX") return telemetry.MaxElectricCharge;
                    if (subTag == "RATE") return telemetry.NetEcRate;
                    if (subTag == "VOLT" || subTag == "VOLTAGE") return telemetry.BusVoltage;
                    if (subTag == "PCT" || subTag == "PERCENT") return telemetry.EcPercent;
                    return telemetry.ElectricCharge;

                case "SIGNAL":
                case "COMM":
                    if (subTag == "TX") return telemetry.SignalTx * 100.0;
                    if (subTag == "RX") return telemetry.SignalRx * 100.0;
                    if (subTag == "RATE") return telemetry.DataRateBps;
                    return telemetry.CommSignal * 100.0;

                case "WARP":
                case "TIMEWARP":
                    if (subTag == "RATE") return telemetry.TimeWarpRate;
                    if (subTag == "INDEX" || subTag == "RATEINDEX") return telemetry.TimeWarpRateIndex;
                    if (subTag == "MAX") return telemetry.MaxTimeWarpRateIndex;
                    return telemetry.TimeWarpRate;

                case "MET":
                case "MISSIONTIME":
                    return telemetry.MissionTime;

                case "UT":
                case "UNIVERSALTIME":
                    return telemetry.UniversalTime;

                case "DV":
                case "DELTAV":
                    if (subTag == "TOTAL") return telemetry.TotalDeltaV;
                    if (subTag == "TIME" || subTag == "BURNTIME" || subTag == "STAGEBURNTIME") return telemetry.StageBurnTime;
                    if (subTag == "TOTALTIME" || subTag == "TOTALBURNTIME") return telemetry.TotalBurnTime;
                    return telemetry.StageDeltaV;

                case "BURNTIME":
                    if (subTag == "TOTAL") return telemetry.TotalBurnTime;
                    return telemetry.StageBurnTime;

                case "STAGE":
                case "STG":
                    if (subTag == "DV") return telemetry.StageDeltaV;
                    if (subTag == "TOTALDV") return telemetry.TotalDeltaV;
                    if (subTag == "TIME" || subTag == "BURNTIME") return telemetry.StageBurnTime;
                    if (subTag == "TOTALTIME") return telemetry.TotalBurnTime;
                    return telemetry.CurrentStage;

                case "CTRL_PITCH":
                    return telemetry.PitchInput * 100.0;
                case "CTRL_ROLL":
                    return telemetry.RollInput * 100.0;
                case "CTRL_YAW":
                    return telemetry.YawInput * 100.0;
                case "TRIM_PITCH":
                    return telemetry.PitchTrim * 100.0;
                case "TRIM_ROLL":
                    return telemetry.RollTrim * 100.0;
                case "TRIM_YAW":
                    return telemetry.YawTrim * 100.0;

                case "CREW":
                    if (subTag == "CAP" || subTag == "CAPACITY") return telemetry.CrewCapacity;
                    if (subTag == "PCT" || subTag == "PERCENT")
                        return telemetry.CrewCapacity > 0 ? ((double)telemetry.CrewCount / telemetry.CrewCapacity * 100.0) : 0.0;
                    return telemetry.CrewCount;

                case "PRESSURE":
                case "CABINPRESSURE":
                    return telemetry.CabinPressure;

                case "TEMP":
                case "CABINTEMP":
                    return telemetry.CabinTemp;

                case "SOLAR":
                    if (subTag == "ACTIVE" || subTag == "COUNT")
                        return telemetry.SolarPower > 0.01 ? 2.0 : 0.0;
                    return telemetry.SolarPower;

                case "MONO":
                case "MONOPROP":
                case "RCS_FUEL":
                    return telemetry.MonoPercent;

                case "O2":
                case "OXYGEN":
                    return telemetry.OxygenPercent;

                case "WATER":
                case "H2O":
                    return telemetry.WaterPercent;

                case "VOLT":
                case "VOLTAGE":
                    return telemetry.BusVoltage;

                case "ENG":
                case "ENGINES":
                    return telemetry.ActiveEngines;

                case "FRAME":
                    return 0.0;

                case "FAR":
                case "FARC":
                case "KER":
                case "ENGINEER":
                case "MJ":
                case "MECHJEB":
                case "PRINCIPIA":
                case "PRINCIA":
                case "PRIN":
                case "RA":
                case "REALANTENNAS":
                case "REALANTENNA":
                case "KERBALISM":
                case "KLSM":
                case "TRAJ":
                case "TRAJECTORIES":
                case "DOCK":
                case "DPAI":
                case "NAVYFISH":
                case "GPWS":
                case "TAWS":
                case "RF":
                case "REALFUELS":
                case "REALFUEL":
                case "TF":
                case "TESTFLIGHT":
                case "DBS":
                case "DYNAMICBATTERYSTORAGE":
                case "SH":
                case "SYSTEMHEAT":
                case "AA":
                case "ATMOSPHEREAUTOPILOT":
                case "RP1":
                case "RP0":
                case "AVIONICS":
                    return ExternalProbeRegistry.ResolveNumeric(tag, subTag);

                default:
                    return ExternalProbeRegistry.ResolveNumeric(tag, subTag);
            }
        }

        public static string Evaluate(string template, IFlightTelemetry telemetry)
        {
            if (string.IsNullOrEmpty(template) || telemetry == null) return template ?? string.Empty;

            return TokenRegex.Replace(template, match =>
            {
                string tag = match.Groups[1].Value.ToUpperInvariant();
                string part2 = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
                string part3 = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
                string part4 = match.Groups[4].Success ? match.Groups[4].Value : string.Empty;

                string subTag = part2.ToUpperInvariant();
                string format = string.Empty;

                if (!string.IsNullOrEmpty(part4))
                {
                    subTag = part2.ToUpperInvariant() + ":" + part3.ToUpperInvariant();
                    format = part4;
                }
                else if (!string.IsNullOrEmpty(part3))
                {
                    string p3Upper = part3.ToUpperInvariant();
                    if (p3Upper == "X" || p3Upper == "Y" || p3Upper == "Z" || p3Upper == "MAG" || p3Upper == "MAGNITUDE")
                    {
                        subTag = part2.ToUpperInvariant() + ":" + p3Upper;
                        format = string.Empty;
                    }
                    else
                    {
                        format = part3;
                    }
                }

                return ResolveToken(tag, subTag, format, telemetry);
            });
        }

        private static string ResolveToken(string tag, string subTag, string format, IFlightTelemetry telem)
        {
            if (!telem.HasVessel) return "---";

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
                    return FormatNumber(telem.TWR, format, "F2");

                case "GFORCE":
                case "G":
                    return FormatNumber(telem.GForce, format, "F1") + " G";

                case "Q":
                case "DYNAERO":
                    return FormatNumber(telem.DynamicPressure, format, "F2") + " kPa";

                case "ATM":
                case "ATMOSPHERE":
                case "BARO":
                    return FormatNumber(telem.AtmosphericPressure, format, "F2") + " atm";

                case "MACH":
                    return FormatNumber(telem.Mach, format, "F2") + " M";

                case "PROP":
                case "STAGEPROP":
                    return $"{Mathf.RoundToInt(telem.StagePropellantFraction * 100f)}%";

                case "EC":
                case "ELEC":
                    if (subTag == "MAX") return FormatNumber(telem.MaxElectricCharge, format, "F0");
                    if (subTag == "RATE") return FormatNumber(telem.NetEcRate, format, "F1") + " e/s";
                    if (subTag == "VOLT" || subTag == "VOLTAGE") return FormatNumber(telem.BusVoltage, format, "F1") + " V";
                    if (subTag == "PCT" || subTag == "PERCENT") return FormatNumber(telem.EcPercent, format, "F0") + "%";
                    return FormatNumber(telem.ElectricCharge, format, "F0");

                case "SIGNAL":
                case "COMM":
                    if (subTag == "TX") return FormatNumber(telem.SignalTx * 100.0, format, "F0") + "%";
                    if (subTag == "RX") return FormatNumber(telem.SignalRx * 100.0, format, "F0") + "%";
                    if (subTag == "RATE") return CommLinkInfo.FormatRate(telem.DataRateBps);
                    if (subTag == "TARGET" || subTag == "PEER") return telem.DirectLinkTarget;
                    if (subTag == "STATUS") return telem.IsConnected ? "CONNECTED" : "NO SIGNAL";
                    return FormatNumber(telem.CommSignal * 100.0, format, "F0") + "%";

                case "WARP":
                case "TIMEWARP":
                    if (subTag == "MODE") return telem.IsPhysicsWarp ? "PHYSICS" : "REGULAR";
                    if (subTag == "RATE") return $"{telem.TimeWarpRate:0.#}x";
                    if (subTag == "INDEX") return telem.TimeWarpRateIndex.ToString();
                    if (subTag == "PAUSE" || subTag == "PAUSED") return telem.IsGamePaused ? "PAUSED" : "RUNNING";
                    return $"{telem.TimeWarpRate:0.#}x";

                case "MET":
                case "MISSIONTIME":
                    return FormatMissionTime(telem.MissionTime, format);

                case "UT":
                case "UNIVERSALTIME":
                    return FormatUniversalTime(telem.UniversalTime, format);

                case "DV":
                case "DELTAV":
                    if (subTag == "TOTAL") return FormatNumber(telem.TotalDeltaV, format, "N0") + (format == "RAW" ? "" : " m/s");
                    if (subTag == "TIME" || subTag == "BURNTIME" || subTag == "STAGEBURNTIME") return FormatTime(telem.StageBurnTime);
                    if (subTag == "TOTALTIME" || subTag == "TOTALBURNTIME") return FormatTime(telem.TotalBurnTime);
                    if (subTag == "SOURCE") return telem.DeltaVSource;
                    return FormatNumber(telem.StageDeltaV, format, "N0") + (format == "RAW" ? "" : " m/s");

                case "BURNTIME":
                    if (subTag == "TOTAL") return FormatTime(telem.TotalBurnTime);
                    return FormatTime(telem.StageBurnTime);

                case "STAGE":
                case "STG":
                    if (subTag == "DV") return FormatNumber(telem.StageDeltaV, format, "N0") + " m/s";
                    if (subTag == "TOTALDV") return FormatNumber(telem.TotalDeltaV, format, "N0") + " m/s";
                    if (subTag == "TIME" || subTag == "BURNTIME") return FormatTime(telem.StageBurnTime);
                    if (subTag == "TOTALTIME") return FormatTime(telem.TotalBurnTime);
                    return $"STAGE {telem.CurrentStage}";

                case "SAS":
                    return telem.IsSASEnabled ? telem.CurrentSASMode.ToString().ToUpper() : "OFF";

                case "RCS":
                    return telem.IsRCSEnabled ? "ON" : "OFF";

                case "BODY":
                    return telem.CelestialBodyName;

                case "SITUATION":
                    return telem.FlightSituation;

                case "CREW":
                    if (subTag == "CAP" || subTag == "CAPACITY") return FormatNumber(telem.CrewCapacity, format, "D0");
                    if (subTag == "PCT" || subTag == "PERCENT")
                    {
                        double pct = telem.CrewCapacity > 0 ? ((double)telem.CrewCount / telem.CrewCapacity * 100.0) : 0.0;
                        return FormatNumber(pct, format, "F0") + "%";
                    }
                    if (format == "FULL" || string.IsNullOrEmpty(format)) return $"CREW {telem.CrewCount}/{telem.CrewCapacity}";
                    return FormatNumber(telem.CrewCount, format, "D0");

                case "PRESSURE":
                case "CABINPRESSURE":
                    return FormatNumber(telem.CabinPressure, format, "F1") + " kPa";

                case "TEMP":
                case "CABINTEMP":
                    return FormatNumber(telem.CabinTemp, format, "F1") + " °C";

                case "SOLAR":
                    if (subTag == "ACTIVE" || subTag == "COUNT")
                        return (telem.SolarPower > 0.01 ? 2 : 0).ToString();
                    return FormatNumber(telem.SolarPower, format, "F2") + " e/s";

                case "MONO":
                case "MONOPROP":
                case "RCS_FUEL":
                    return FormatNumber(telem.MonoPercent, format, "F1") + "%";

                case "O2":
                case "OXYGEN":
                    return FormatNumber(telem.OxygenPercent, format, "F1") + "%";

                case "WATER":
                case "H2O":
                    return FormatNumber(telem.WaterPercent, format, "F1") + "%";

                case "VOLT":
                case "VOLTAGE":
                    return FormatNumber(telem.BusVoltage, format, "F1") + " V";

                case "ENG":
                case "ENGINES":
                    return telem.ActiveEngines.ToString();

                case "CTRL_PITCH":
                    return FormatNumber(telem.PitchInput * 100.0, format, "+0;-0;0") + "%";
                case "CTRL_ROLL":
                    return FormatNumber(telem.RollInput * 100.0, format, "+0;-0;0") + "%";
                case "CTRL_YAW":
                    return FormatNumber(telem.YawInput * 100.0, format, "+0;-0;0") + "%";
                case "TRIM_PITCH":
                    return FormatNumber(telem.PitchTrim * 100.0, format, "+0;-0;0") + "%";
                case "TRIM_ROLL":
                    return FormatNumber(telem.RollTrim * 100.0, format, "+0;-0;0") + "%";
                case "TRIM_YAW":
                    return FormatNumber(telem.YawTrim * 100.0, format, "+0;-0;0") + "%";
                case "STAGE_LOCK":
                    return telem.IsStageLocked ? "LOCKED" : "ARMED";
                case "CTRL_MODE":
                    return telem.IsDockingMode ? "DOCKING" : "STAGING";
                case "CTRL_PREC":
                    return telem.IsPrecisionControl ? "PREC" : "NORM";
                case "STAGE_PROP_NAME":
                    return telem.StagePropellantName;

                case "FRAME":
                    if (NavBallHookService.Provider != null && !string.IsNullOrEmpty(NavBallHookService.Provider.FrameName))
                        return NavBallHookService.Provider.FrameName;
                    string principiaFrame = ExternalProbeRegistry.ResolveString("PRINCIPIA", "FRAME", "");
                    if (!string.IsNullOrEmpty(principiaFrame))
                        return principiaFrame;
                    return "SURFACE";

                case "FAR":
                case "FARC":
                case "KER":
                case "ENGINEER":
                case "MJ":
                case "MECHJEB":
                case "PRINCIPIA":
                case "PRINCIA":
                case "PRIN":
                case "RA":
                case "REALANTENNAS":
                case "REALANTENNA":
                case "KERBALISM":
                case "KLSM":
                case "TRAJ":
                case "TRAJECTORIES":
                case "DOCK":
                case "DPAI":
                case "NAVYFISH":
                case "GPWS":
                case "TAWS":
                case "RF":
                case "REALFUELS":
                case "REALFUEL":
                case "TF":
                case "TESTFLIGHT":
                case "DBS":
                case "DYNAMICBATTERYSTORAGE":
                case "SH":
                case "SYSTEMHEAT":
                case "AA":
                case "ATMOSPHEREAUTOPILOT":
                case "RP1":
                case "RP0":
                case "AVIONICS":
                    return ExternalProbeRegistry.ResolveString(tag, subTag, format);

                default:
                    string extVal = ExternalProbeRegistry.ResolveString(tag, subTag, format);
                    if (!string.IsNullOrEmpty(extVal) && extVal != "---" && !extVal.StartsWith("{"))
                        return extVal;
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

        private static string FormatMissionTime(double seconds, string format)
        {
            bool negative = seconds < 0;
            double absSec = Math.Abs(seconds);
            int secs = (int)(absSec % 60);
            int mins = (int)((absSec / 60) % 60);
            int totalHours = (int)(absSec / 3600);

            if (format == "HMS" || format == "TIME")
            {
                return (negative ? "-" : "") + $"{totalHours:D2}:{mins:D2}:{secs:D2}";
            }

            int hours = totalHours % 24;
            int days = (totalHours / 24) % 365;
            int years = totalHours / (24 * 365);

            string sign = negative ? "T- " : "T+ ";
            if (format == "COMPACT")
            {
                if (years > 0) return $"{sign}{years}y {days}d {hours:D2}:{mins:D2}:{secs:D2}";
                if (days > 0) return $"{sign}{days}d {hours:D2}:{mins:D2}:{secs:D2}";
                return $"{sign}{hours:D2}:{mins:D2}:{secs:D2}";
            }

            return $"{sign}{years}y, {days}d, {hours:D2}:{mins:D2}:{secs:D2}";
        }

        private static string FormatUniversalTime(double ut, string format)
        {
            double absSec = Math.Max(0.0, ut);
            int secs = (int)(absSec % 60);
            int mins = (int)((absSec / 60) % 60);
            int totalHours = (int)(absSec / 3600);
            int hours = totalHours % 24;
            int days = (totalHours / 24) % 365 + 1;
            int years = totalHours / (24 * 365) + 1;

            if (format == "HMS" || format == "TIME")
                return $"{totalHours:D2}:{mins:D2}:{secs:D2}";

            return $"Y{years} D{days:D3} {hours:D2}:{mins:D2}:{secs:D2}";
        }
    }
}
