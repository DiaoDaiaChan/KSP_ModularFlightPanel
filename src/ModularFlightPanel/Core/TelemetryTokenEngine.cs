using System;
using System.Collections.Generic;
using System.Text;
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

        private struct ParsedNumericToken
        {
            public string Tag;
            public string SubTag;
        }

        private static readonly Dictionary<string, ParsedNumericToken> _numericTokenCache = new Dictionary<string, ParsedNumericToken>(StringComparer.OrdinalIgnoreCase);

        private static ParsedNumericToken GetOrParseNumericToken(string token)
        {
            if (_numericTokenCache.TryGetValue(token, out var cached))
            {
                return cached;
            }

            string clean = token.Trim().Trim('{', '}');
            string[] parts = clean.Split(':');
            string tag = parts[0].ToUpperInvariant();
            string subTag = string.Empty;
            if (parts.Length > 2)
                subTag = parts[1].ToUpperInvariant() + ":" + parts[2].ToUpperInvariant();
            else if (parts.Length > 1)
                subTag = parts[1].ToUpperInvariant();

            var parsed = new ParsedNumericToken { Tag = tag, SubTag = subTag };
            if (_numericTokenCache.Count < 512)
            {
                _numericTokenCache[token] = parsed;
            }
            return parsed;
        }

        /// <summary>
        /// 原生双精度数值提取（用于驱动表盘指针、弧线、带状滚动物理计算）
        /// 支持如 "{SPD}", "{ALT:AGL}", "{GFORCE}", "{Q}", "{TWR}", "{THROTTLE}", "{PROP}" 等
        /// 亦可传入无花括号的纯标识如 "GFORCE" 或 "ALT:ASL"
        /// </summary>
        public static double EvaluateNumeric(string token, IFlightTelemetry telemetry)
        {
            if (string.IsNullOrEmpty(token) || telemetry == null) return double.NaN;
            if (!telemetry.HasVessel) return double.NaN;

            var parsed = GetOrParseNumericToken(token);
            string tag = parsed.Tag;
            string subTag = parsed.SubTag;

            switch (tag)
            {
                case "SPD":
                case "SPEED":
                    double spdNum = telemetry.CurrentSpeed;
                    if (subTag == "SURF" || subTag == "SURF:KMH" || subTag == "SURF_KMH") spdNum = telemetry.SurfaceSpeed;
                    else if (subTag == "OBT" || subTag == "ORBIT" || subTag == "OBT:KMH") spdNum = telemetry.OrbitalSpeed;
                    else if (subTag == "TGT" || subTag == "TARGET" || subTag == "TGT:KMH") spdNum = telemetry.TargetSpeed;
                    if (subTag == "KMH" || subTag.EndsWith(":KMH") || subTag.EndsWith("_KMH")) return spdNum * 3.6;
                    return spdNum;

                case "MACH":
                    return telemetry.Mach;

                case "ALT":
                case "ALTITUDE":
                    double altNum = telemetry.DisplayAltitude;
                    if (subTag == "ASL" || subTag == "ASL:KM" || subTag == "ASL_KM") altNum = telemetry.AltitudeASL;
                    else if (subTag == "AGL" || subTag == "RADAR" || subTag == "AGL:KM" || subTag == "AGL_KM") altNum = telemetry.AltitudeAGL;
                    if (subTag == "KM" || subTag.EndsWith(":KM") || subTag.EndsWith("_KM")) return altNum / 1000.0;
                    return altNum;

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

                case "MN":
                case "MANEUVER":
                    if (subTag == "TOTAL" || subTag == "TOTALDV") return telemetry.ManeuverTotalDeltaV;
                    if (subTag == "TIME" || subTag == "TIMETONODE") return telemetry.ManeuverTimeToNode;
                    if (subTag == "BURNTIME" || subTag == "DURATION") return telemetry.ManeuverBurnTime;
                    if (subTag == "TIMETOBURN" || subTag == "STARTBURN") return telemetry.ManeuverTimeToBurn;
                    if (subTag == "PRO" || subTag == "PROGRADE") return telemetry.ManeuverDeltaVPrograde;
                    if (subTag == "NORM" || subTag == "NORMAL") return telemetry.ManeuverDeltaVNormal;
                    if (subTag == "RAD" || subTag == "RADIAL") return telemetry.ManeuverDeltaVRadial;
                    return telemetry.ManeuverDeltaV;

                case "NODEDV":
                    return telemetry.ManeuverDeltaV;

                case "TIMETONODE":
                    return telemetry.ManeuverTimeToNode;

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
                    if (subTag == "TOTAL" || subTag == "STAGE" || subTag == "ALL") return telemetry.TotalStageEngines;
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

                case "PERF":
                case "PROFILER":
                    if (subTag == "FPS") return MFPProfiler.CurrentFPS;
                    if (subTag == "MS" || subTag == "TOTAL" || subTag == "TOTAL_MS" || subTag == "FRAMETIME") return MFPProfiler.AvgTotalMs;
                    if (subTag == "LAST" || subTag == "LAST_MS") return MFPProfiler.LastTotalMs;
                    if (subTag == "MIN" || subTag == "MIN_MS") return MFPProfiler.MinTotalMs;
                    if (subTag == "MAX" || subTag == "MAX_MS") return MFPProfiler.MaxTotalMs;
                    if (subTag == "BUDGET" || subTag == "BUDGET_PCT" || subTag == "PCT") return MFPProfiler.FrameBudgetPercent;
                    if (subTag == "TELEM" || subTag == "TELEMETRY") return MFPProfiler.AvgTelemetryMs;
                    if (subTag == "PROBES" || subTag == "PROBE") return MFPProfiler.AvgProbesMs;
                    if (subTag == "WIDGETS" || subTag == "WIDGET") return MFPProfiler.AvgWidgetsMs;
                    if (subTag == "SILHOUETTE" || subTag == "SILH") return MFPProfiler.AvgSilhouetteMs;
                    if (subTag == "HOOKS" || subTag == "HOOK") return MFPProfiler.AvgHooksMs;
                    if (subTag == "MEM" || subTag == "MEMORY" || subTag == "RAM") return MFPProfiler.TotalMemoryMB;
                    if (subTag == "GC" || subTag == "GC0") return MFPProfiler.Gc0Collections;
                    if (subTag == "GC1") return MFPProfiler.Gc1Collections;
                    if (subTag == "GC2") return MFPProfiler.Gc2Collections;
                    if (subTag == "SPIKES" || subTag == "SPIKE") return MFPProfiler.SpikeCount;
                    if (subTag == "COUNT" || subTag == "ACTIVE" || subTag == "ACTIVE_WIDGETS") return MFPProfiler.ActiveWidgetCount;
                    if (subTag == "TOP_MS" || subTag == "TOP_WIDGET_MS") return MFPProfiler.TopOffenderWidgetMs;
                    return MFPProfiler.AvgTotalMs;

                default:
                    return ExternalProbeRegistry.ResolveNumeric(tag, subTag);
            }
        }

        private struct TemplateSegment
        {
            public bool IsToken;
            public string StaticText;
            public string Tag;
            public string SubTag;
            public string Format;
        }

        private class CompiledTemplate
        {
            public TemplateSegment[] Segments;
        }

        private static readonly Dictionary<string, CompiledTemplate> _compiledTemplateCache =
            new Dictionary<string, CompiledTemplate>(StringComparer.Ordinal);

        [ThreadStatic]
        private static StringBuilder _evalSb;

        private static CompiledTemplate CompileTemplate(string template)
        {
            var matches = TokenRegex.Matches(template);
            if (matches.Count == 0)
            {
                return new CompiledTemplate
                {
                    Segments = new[] { new TemplateSegment { IsToken = false, StaticText = template } }
                };
            }

            var segments = new List<TemplateSegment>(matches.Count * 2 + 1);
            int lastIndex = 0;

            for (int i = 0; i < matches.Count; i++)
            {
                Match m = matches[i];
                if (m.Index > lastIndex)
                {
                    segments.Add(new TemplateSegment
                    {
                        IsToken = false,
                        StaticText = template.Substring(lastIndex, m.Index - lastIndex)
                    });
                }

                string tag = m.Groups[1].Value.ToUpperInvariant();
                string part2 = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;
                string part3 = m.Groups[3].Success ? m.Groups[3].Value : string.Empty;
                string part4 = m.Groups[4].Success ? m.Groups[4].Value : string.Empty;

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

                segments.Add(new TemplateSegment
                {
                    IsToken = true,
                    Tag = tag,
                    SubTag = subTag,
                    Format = format
                });

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < template.Length)
            {
                segments.Add(new TemplateSegment
                {
                    IsToken = false,
                    StaticText = template.Substring(lastIndex)
                });
            }

            return new CompiledTemplate { Segments = segments.ToArray() };
        }

        public static string Evaluate(string template, IFlightTelemetry telemetry)
        {
            if (string.IsNullOrEmpty(template) || telemetry == null) return template ?? string.Empty;

            if (!_compiledTemplateCache.TryGetValue(template, out var compiled))
            {
                compiled = CompileTemplate(template);
                if (_compiledTemplateCache.Count < 512)
                {
                    _compiledTemplateCache[template] = compiled;
                }
            }

            if (compiled.Segments.Length == 1 && !compiled.Segments[0].IsToken)
            {
                return compiled.Segments[0].StaticText;
            }

            if (_evalSb == null) _evalSb = new StringBuilder(256);
            _evalSb.Length = 0;

            for (int i = 0; i < compiled.Segments.Length; i++)
            {
                var seg = compiled.Segments[i];
                if (!seg.IsToken)
                {
                    _evalSb.Append(seg.StaticText);
                }
                else
                {
                    _evalSb.Append(ResolveToken(seg.Tag, seg.SubTag, seg.Format, telemetry));
                }
            }

            return _evalSb.ToString();
        }

        private static string ResolveToken(string tag, string subTag, string format, IFlightTelemetry telem)
        {
            if (!telem.HasVessel) return "---";

            switch (tag)
            {
                case "SPD":
                case "SPEED":
                    double spd = telem.CurrentSpeed;
                    if (subTag == "SURF" || subTag == "SURF:KMH" || subTag == "SURF_KMH") spd = telem.SurfaceSpeed;
                    else if (subTag == "OBT" || subTag == "ORBIT" || subTag == "OBT:KMH") spd = telem.OrbitalSpeed;
                    else if (subTag == "TGT" || subTag == "TARGET" || subTag == "TGT:KMH") spd = telem.TargetSpeed;
                    if (format == "KMH" || subTag == "KMH" || subTag.EndsWith(":KMH") || subTag.EndsWith("_KMH"))
                        return FormatNumber(spd * 3.6, (format == "KMH" || string.IsNullOrEmpty(format)) ? "F0" : format, "F0");
                    return FormatNumber(spd, format, "F1");

                case "ALT":
                case "ALTITUDE":
                    double alt = (subTag.StartsWith("ASL")) ? telem.AltitudeASL : (subTag.StartsWith("AGL") || subTag.StartsWith("RADAR") ? telem.AltitudeAGL : telem.DisplayAltitude);
                    if (format == "KM" || subTag == "KM" || subTag.EndsWith(":KM") || subTag.EndsWith("_KM"))
                        return FormatNumber(alt / 1000.0, (format == "KM" || string.IsNullOrEmpty(format)) ? "F0" : format, "F0");
                    if (format == "DIST") return FormatDistance(alt);
                    return FormatNumber(alt, format, "N0");

                case "VSI":
                case "VERTSPD":
                    return FormatNumber(telem.VerticalSpeed, format, "F1");

                case "HDG":
                case "HEADING":
                    return CacheManager.FastDegree(Mathf.RoundToInt(telem.Heading));

                case "PITCH":
                    return FormatNumber(telem.Pitch, format, "F1", "tok_pitch");

                case "ROLL":
                    return FormatNumber(telem.Roll, format, "F1", "tok_roll");

                case "THROTTLE":
                case "THR":
                    return CacheManager.FastPercent(Mathf.RoundToInt(telem.Throttle * 100f));

                case "AP":
                case "APOAPSIS":
                    if (format == "DIST") return FormatDistance(telem.Apoapsis);
                    return FormatNumber(telem.Apoapsis, format, "N0", "tok_ap");

                case "PE":
                case "PERIAPSIS":
                    if (format == "DIST") return FormatDistance(telem.Periapsis);
                    return FormatNumber(telem.Periapsis, format, "N0", "tok_pe");

                case "TAP":
                    return FormatTime(Math.Max(0.0, telem.TimeToAp));

                case "TPE":
                    return FormatTime(Math.Max(0.0, telem.TimeToPe));

                case "TWR":
                    return FormatNumber(telem.TWR, format, "F2", "tok_twr");

                case "GFORCE":
                case "G":
                    return CacheManager.Instance.FastDoubleWithAffix("tok_gforce", telem.GForce, "", " G", format ?? "F1", 0.05);

                case "Q":
                case "DYNAERO":
                    return CacheManager.Instance.FastDoubleWithAffix("tok_q", telem.DynamicPressure, "", " kPa", format ?? "F2", 0.05);

                case "ATM":
                case "ATMOSPHERE":
                case "BARO":
                    return CacheManager.Instance.FastDoubleWithAffix("tok_atm", telem.AtmosphericPressure, "", " atm", format ?? "F2", 0.05);

                case "MACH":
                    return CacheManager.Instance.FastDoubleWithAffix("tok_mach", telem.Mach, "", " M", format ?? "F2", 0.02);

                case "PROP":
                case "STAGEPROP":
                    return CacheManager.FastPercent(Mathf.RoundToInt(telem.StagePropellantFraction * 100f));

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
                    string fmt = string.IsNullOrEmpty(format) ? subTag : format;
                    return FormatMissionTime(telem.MissionTime, fmt);

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

                case "MN":
                case "MANEUVER":
                    if (!telem.HasManeuverNode) return "---";
                    if (subTag == "SOURCE") return telem.ManeuverSource;
                    if (subTag == "TOTAL" || subTag == "TOTALDV") return FormatNumber(telem.ManeuverTotalDeltaV, format, "F1") + (format == "RAW" ? "" : " m/s");
                    if (subTag == "TIME" || subTag == "TIMETONODE") return (telem.ManeuverTimeToNode < 0 ? "T+" : "T-") + FormatTime(Math.Abs(telem.ManeuverTimeToNode));
                    if (subTag == "BURNTIME" || subTag == "DURATION") return FormatTime(Math.Max(0.0, telem.ManeuverBurnTime));
                    if (subTag == "TIMETOBURN" || subTag == "STARTBURN") return (telem.ManeuverTimeToBurn < 0 ? "T+" : "T-") + FormatTime(Math.Abs(telem.ManeuverTimeToBurn));
                    if (subTag == "STATUS") return telem.ManeuverTimeToBurn <= 0 ? (telem.ManeuverDeltaV <= 0.1 ? "COMPLETE" : "BURNING") : "ARMED";
                    if (subTag == "PRO" || subTag == "PROGRADE")
                    {
                        string sign = telem.ManeuverDeltaVPrograde >= 0.0 ? "+" : "";
                        return sign + FormatNumber(telem.ManeuverDeltaVPrograde, format, "F1") + (format == "RAW" ? "" : " m/s");
                    }
                    if (subTag == "NORM" || subTag == "NORMAL")
                    {
                        string sign = telem.ManeuverDeltaVNormal >= 0.0 ? "+" : "";
                        return sign + FormatNumber(telem.ManeuverDeltaVNormal, format, "F1") + (format == "RAW" ? "" : " m/s");
                    }
                    if (subTag == "RAD" || subTag == "RADIAL")
                    {
                        string sign = telem.ManeuverDeltaVRadial >= 0.0 ? "+" : "";
                        return sign + FormatNumber(telem.ManeuverDeltaVRadial, format, "F1") + (format == "RAW" ? "" : " m/s");
                    }
                    return FormatNumber(telem.ManeuverDeltaV, format, "F1") + (format == "RAW" ? "" : " m/s");

                case "NODEDV":
                    if (!telem.HasManeuverNode) return "---";
                    return FormatNumber(telem.ManeuverDeltaV, format, "F1") + (format == "RAW" ? "" : " m/s");

                case "TIMETONODE":
                    if (!telem.HasManeuverNode) return "---";
                    return (telem.ManeuverTimeToNode < 0 ? "T+" : "T-") + FormatTime(Math.Abs(telem.ManeuverTimeToNode));

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
                    if (subTag == "TOTAL" || subTag == "STAGE" || subTag == "ALL") return telem.TotalStageEngines.ToString();
                    if (subTag == "CLUSTER") return $"{telem.ActiveEngines}/{telem.TotalStageEngines}";
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

                case "PERF":
                case "PROFILER":
                    if (subTag == "STATUS") return MFPProfiler.IsMasterBypassed ? "BYPASS" : "ACTIVE";
                    if (subTag == "FPS") return FormatNumber(MFPProfiler.CurrentFPS, format, "F0");
                    if (subTag == "MS" || subTag == "TOTAL" || subTag == "TOTAL_MS" || subTag == "FRAMETIME") return FormatNumber(MFPProfiler.AvgTotalMs, format, "F2") + (format == "RAW" ? "" : " ms");
                    if (subTag == "LAST" || subTag == "LAST_MS") return FormatNumber(MFPProfiler.LastTotalMs, format, "F2") + (format == "RAW" ? "" : " ms");
                    if (subTag == "MIN" || subTag == "MIN_MS") return FormatNumber(MFPProfiler.MinTotalMs, format, "F2") + (format == "RAW" ? "" : " ms");
                    if (subTag == "MAX" || subTag == "MAX_MS") return FormatNumber(MFPProfiler.MaxTotalMs, format, "F2") + (format == "RAW" ? "" : " ms");
                    if (subTag == "BUDGET" || subTag == "BUDGET_PCT" || subTag == "PCT") return FormatNumber(MFPProfiler.FrameBudgetPercent, format, "F1") + "%";
                    if (subTag == "TELEM" || subTag == "TELEMETRY") return FormatNumber(MFPProfiler.AvgTelemetryMs, format, "F3") + (format == "RAW" ? "" : " ms");
                    if (subTag == "PROBES" || subTag == "PROBE") return FormatNumber(MFPProfiler.AvgProbesMs, format, "F3") + (format == "RAW" ? "" : " ms");
                    if (subTag == "WIDGETS" || subTag == "WIDGET") return FormatNumber(MFPProfiler.AvgWidgetsMs, format, "F3") + (format == "RAW" ? "" : " ms");
                    if (subTag == "SILHOUETTE" || subTag == "SILH") return FormatNumber(MFPProfiler.AvgSilhouetteMs, format, "F3") + (format == "RAW" ? "" : " ms");
                    if (subTag == "HOOKS" || subTag == "HOOK") return FormatNumber(MFPProfiler.AvgHooksMs, format, "F3") + (format == "RAW" ? "" : " ms");
                    if (subTag == "MEM" || subTag == "MEMORY" || subTag == "RAM") return FormatNumber(MFPProfiler.TotalMemoryMB, format, "F1", "perf_mem") + (format == "RAW" ? "" : " MB");
                    if (subTag == "GC" || subTag == "GC0") return CacheManager.FastInt(MFPProfiler.Gc0Collections);
                    if (subTag == "SPIKES" || subTag == "SPIKE") return CacheManager.FastInt(MFPProfiler.SpikeCount);
                    if (subTag == "COUNT" || subTag == "ACTIVE" || subTag == "ACTIVE_WIDGETS") return CacheManager.FastInt(MFPProfiler.ActiveWidgetCount);
                    if (subTag == "TOP" || subTag == "TOP_WIDGET") return MFPProfiler.TopOffenderWidgetId;
                    if (subTag == "TOP_MS" || subTag == "TOP_WIDGET_MS") return FormatNumber(MFPProfiler.TopOffenderWidgetMs, format, "F3", "perf_top_ms") + " ms";
                    if (subTag == "CACHE_HIT" || subTag == "HITRATE") return FormatNumber(CacheManager.Instance.HitRatePercent, format, "F1", "perf_hit") + "%";
                    if (subTag == "CACHE_SAVED") return FormatNumber(CacheManager.Instance.EstimatedBytesSaved / 1024.0, format, "F1", "perf_saved") + " KB";
                    if (subTag == "CACHE_SLOTS") return CacheManager.FastInt(CacheManager.Instance.DeadbandSlotCount);
                    if (subTag == "CACHE_REQS") return CacheManager.FastInt((int)Math.Min(CacheManager.Instance.TotalRequests, 9999));
                    return FormatNumber(MFPProfiler.AvgTotalMs, format, "F2", "perf_total_ms") + (format == "RAW" ? "" : " ms");

                default:
                    string extVal = ExternalProbeRegistry.ResolveString(tag, subTag, format);
                    if (!string.IsNullOrEmpty(extVal) && extVal != "---" && !extVal.StartsWith("{"))
                        return extVal;
                    return $"{{{tag}}}";
            }
        }

        private static string FormatNumber(double val, string format, string defaultFmt, string slotKey = null)
        {
            string fmt = string.IsNullOrEmpty(format) ? defaultFmt : format;
            if (fmt == "F0" || fmt == "N0")
            {
                long rounded = (long)Math.Round(val);
                if (rounded >= -1000 && rounded <= 9999)
                {
                    return CacheManager.FastInt((int)rounded);
                }
            }
            string key = slotKey ?? fmt;
            return CacheManager.Instance.FastDouble(key, val, fmt, 0.05);
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

            string sign = negative ? "T- " : "T+ ";

            if (format == "HMS" || format == "TIME" || format == "CLOCK")
            {
                return $"{sign}{totalHours:D2}:{mins:D2}:{secs:D2}";
            }

            if (format == "RAW_HMS")
            {
                return (negative ? "-" : "") + $"{totalHours:D2}:{mins:D2}:{secs:D2}";
            }

            int hours = totalHours % 24;
            int days = (totalHours / 24) % 365;
            int years = totalHours / (24 * 365);

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
