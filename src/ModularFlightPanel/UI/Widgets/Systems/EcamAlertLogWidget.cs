using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public enum EcamAlertSeverity
    {
        Warning,   // Level 3 (Red, immediate crew action)
        Caution,   // Level 2 (Amber, crew awareness & checklist)
        Advisory,  // Level 1 (Cyan/Blue, monitoring system)
        Memo       // Level 0 (Green/White, normal flight checklist / milestone)
    }

    public class EcamLogEntry
    {
        public EcamAlertSeverity Severity;
        public string Tag;          // e.g. "WARN", "CAUT", "ADV", "MEMO"
        public string Icon;         // e.g. "⚡", "▼", "◆", "●", "▲"
        public string Title;        // e.g. "飞船离轨制动 DEORBIT"
        public string Detail;       // e.g. "Pe -45k", "IGN 6 ENG"
        public string Timestamp;    // e.g. "+00:14:22"
        public double MetSeconds;
        public bool IsPersistent;   // Active continuous alert (e.g. LOW FUEL)
        public bool IsAcknowledged;
        public float BlinkTimer;

        public EcamLogEntry(EcamAlertSeverity severity, string tag, string icon, string title, string detail, string timestamp, double metSeconds, bool isPersistent = false)
        {
            Severity = severity;
            Tag = tag;
            Icon = icon;
            Title = title;
            Detail = detail;
            Timestamp = timestamp;
            MetSeconds = metSeconds;
            IsPersistent = isPersistent;
            IsAcknowledged = false;
            BlinkTimer = 0f;
        }
    }

    /// <summary>
    /// ECAM 日志单行快照 (0 GC 纯值类型)
    /// </summary>
    public struct EcamRowState : IEquatable<EcamRowState>
    {
        public bool Visible;
        public EcamAlertSeverity Severity;
        public string Tag;
        public string Title;
        public string Detail;
        public string Timestamp;
        public bool IsPersistent;

        public bool Equals(EcamRowState other)
        {
            return Visible == other.Visible &&
                   Severity == other.Severity &&
                   Tag == other.Tag &&
                   Title == other.Title &&
                   Detail == other.Detail &&
                   Timestamp == other.Timestamp &&
                   IsPersistent == other.IsPersistent;
        }

        public override bool Equals(object obj) => obj is EcamRowState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (Visible ? 1 : 0);
                hash = (hash * 397) ^ (int)Severity;
                hash = (hash * 397) ^ (Tag != null ? Tag.GetHashCode() : 0);
                hash = (hash * 397) ^ (Title != null ? Title.GetHashCode() : 0);
                hash = (hash * 397) ^ (Detail != null ? Detail.GetHashCode() : 0);
                hash = (hash * 397) ^ (Timestamp != null ? Timestamp.GetHashCode() : 0);
                hash = (hash * 397) ^ (IsPersistent ? 1 : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// ECAM 系统诊断单行快照 (0 GC 纯值类型)
    /// </summary>
    public struct EcamDiagRowState : IEquatable<EcamDiagRowState>
    {
        public string Tag;
        public string Title;
        public string Val;
        public string Aux;
        public TextStyleRole Role;

        public bool Equals(EcamDiagRowState other)
        {
            return Tag == other.Tag &&
                   Title == other.Title &&
                   Val == other.Val &&
                   Aux == other.Aux &&
                   Role == other.Role;
        }

        public override bool Equals(object obj) => obj is EcamDiagRowState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (Tag != null ? Tag.GetHashCode() : 0);
                hash = (hash * 397) ^ (Title != null ? Title.GetHashCode() : 0);
                hash = (hash * 397) ^ (Val != null ? Val.GetHashCode() : 0);
                hash = (hash * 397) ^ (Aux != null ? Aux.GetHashCode() : 0);
                hash = (hash * 397) ^ (int)Role;
                return hash;
            }
        }
    }

    /// <summary>
    /// ECAM 飞行告警与备忘日志全机状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct EcamAlertLogState : IEquatable<EcamAlertLogState>
    {
        public bool HasVessel;
        public string PhaseText;
        public int WarnCount;
        public int CautCount;
        public int MemoCount;
        public int LogCount;
        public string StatusText;
        public TextStyleRole StatusRole;
        public bool ShowSystemStatusPage;

        public EcamRowState Row0;
        public EcamRowState Row1;
        public EcamRowState Row2;
        public EcamRowState Row3;
        public EcamRowState Row4;

        public EcamDiagRowState Diag0;
        public EcamDiagRowState Diag1;
        public EcamDiagRowState Diag2;
        public EcamDiagRowState Diag3;
        public EcamDiagRowState Diag4;

        public EcamRowState GetRow(int index)
        {
            switch (index)
            {
                case 0: return Row0;
                case 1: return Row1;
                case 2: return Row2;
                case 3: return Row3;
                case 4: return Row4;
                default: return default;
            }
        }

        public void SetRow(int index, in EcamRowState row)
        {
            switch (index)
            {
                case 0: Row0 = row; break;
                case 1: Row1 = row; break;
                case 2: Row2 = row; break;
                case 3: Row3 = row; break;
                case 4: Row4 = row; break;
            }
        }

        public EcamDiagRowState GetDiag(int index)
        {
            switch (index)
            {
                case 0: return Diag0;
                case 1: return Diag1;
                case 2: return Diag2;
                case 3: return Diag3;
                case 4: return Diag4;
                default: return default;
            }
        }

        public void SetDiag(int index, in EcamDiagRowState diag)
        {
            switch (index)
            {
                case 0: Diag0 = diag; break;
                case 1: Diag1 = diag; break;
                case 2: Diag2 = diag; break;
                case 3: Diag3 = diag; break;
                case 4: Diag4 = diag; break;
            }
        }

        public bool Equals(EcamAlertLogState other)
        {
            return HasVessel == other.HasVessel &&
                   PhaseText == other.PhaseText &&
                   WarnCount == other.WarnCount &&
                   CautCount == other.CautCount &&
                   MemoCount == other.MemoCount &&
                   LogCount == other.LogCount &&
                   StatusText == other.StatusText &&
                   StatusRole == other.StatusRole &&
                   ShowSystemStatusPage == other.ShowSystemStatusPage &&
                   Row0.Equals(other.Row0) &&
                   Row1.Equals(other.Row1) &&
                   Row2.Equals(other.Row2) &&
                   Row3.Equals(other.Row3) &&
                   Row4.Equals(other.Row4) &&
                   Diag0.Equals(other.Diag0) &&
                   Diag1.Equals(other.Diag1) &&
                   Diag2.Equals(other.Diag2) &&
                   Diag3.Equals(other.Diag3) &&
                   Diag4.Equals(other.Diag4);
        }

        public override bool Equals(object obj) => obj is EcamAlertLogState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ WarnCount;
                hash = (hash * 397) ^ CautCount;
                hash = (hash * 397) ^ MemoCount;
                hash = (hash * 397) ^ LogCount;
                hash = (hash * 397) ^ (PhaseText != null ? PhaseText.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// ECAM 飞行告警与备忘日志纯业务解耦大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class EcamAlertLogLogic : WidgetLogic<EcamAlertLogState>
    {
        public const int MaxDisplayRows = 5;

        private readonly List<EcamLogEntry> _logBuffer = new List<EcamLogEntry>(40);
        private readonly List<EcamLogEntry> _activePersistentAlerts = new List<EcamLogEntry>(8);

        public bool IsClearedMode { get; private set; }
        public bool ShowSystemStatusPage { get; set; }

        private int _lastStage = -1;
        private int _lastActiveEngines = -1;
        private float _lastThrottle = 0f;
        private bool _lastIsStageSeparating = false;
        private bool _lastIsEngineIgniting = false;
        private double _lastTimeToNode = -1.0;
        private bool _lastManeuverBurnTriggered = false;
        private double _lastAltitude = 0.0;
        private double _lastEffectivePe = -999999.0;
        private double _lastEffectiveAp = -999999.0;
        private string _lastCelestialBody = string.Empty;
        private string _lastFlightSituation = string.Empty;
        private bool _lastIsLanded = false;
        private double _lastAltitudeAGL = 0.0;
        private float _lowFuelTimer = 0f;
        private bool _initialHistorySeeded = false;

        public EcamAlertLogLogic()
        {
            SeedInitialChecklistMemos();
        }

        public void SeedInitialChecklistMemos()
        {
            if (_initialHistorySeeded) return;
            _initialHistorySeeded = true;

            PushLogEntry(new EcamLogEntry(
                EcamAlertSeverity.Memo, "GUID", "●",
                I18n.Tr("WIDGET_ECAM_ALIGN_OK", "惯导对准就绪 ALIGN"),
                "NOMINAL", "+00:00:00", 0.0));

            PushLogEntry(new EcamLogEntry(
                EcamAlertSeverity.Memo, "ELEC", "●",
                I18n.Tr("WIDGET_ECAM_ENTRY_SYS_OK", "航电总线巡检正常"),
                "28V BUS", "+00:00:02", 2.0));

            PushLogEntry(new EcamLogEntry(
                EcamAlertSeverity.Advisory, "PROP", "◆",
                I18n.Tr("WIDGET_ECAM_PROP_ARM", "主推力系统待命预冷"),
                "CHILLED", "+00:00:05", 5.0));

            PushLogEntry(new EcamLogEntry(
                EcamAlertSeverity.Memo, "STG", "◀",
                I18n.Tr("WIDGET_ALERT_SEPARATION", "级间分级分离就绪"),
                "ARMED", "+00:00:08", 8.0));
        }

        public void PushLogEntry(EcamLogEntry entry)
        {
            if (entry == null) return;
            _logBuffer.Insert(0, entry);
            if (_logBuffer.Count > 40)
            {
                _logBuffer.RemoveAt(_logBuffer.Count - 1);
            }
        }

        public void ClearAcknowledgedAlerts()
        {
            IsClearedMode = true;
            for (int i = 0; i < _logBuffer.Count; i++)
            {
                if (_logBuffer[i].Severity != EcamAlertSeverity.Warning)
                {
                    _logBuffer[i].IsAcknowledged = true;
                }
            }
        }

        public void RecallClearedAlerts()
        {
            IsClearedMode = false;
            for (int i = 0; i < _logBuffer.Count; i++)
            {
                _logBuffer[i].IsAcknowledged = false;
            }
        }

        public void ToggleStatusPage()
        {
            ShowSystemStatusPage = !ShowSystemStatusPage;
        }

        public override void Reset()
        {
            _initialHistorySeeded = false;
            _logBuffer.Clear();
            _activePersistentAlerts.Clear();
            IsClearedMode = false;
            ShowSystemStatusPage = false;
            _lastStage = -1;
            _lastActiveEngines = -1;
            _lastThrottle = 0f;
            _lastIsStageSeparating = false;
            _lastIsEngineIgniting = false;
            _lastTimeToNode = -1.0;
            _lastManeuverBurnTriggered = false;
            _lastAltitude = 0.0;
            _lastEffectivePe = -999999.0;
            _lastEffectiveAp = -999999.0;
            _lastCelestialBody = string.Empty;
            _lastFlightSituation = string.Empty;
            _lastIsLanded = false;
            _lastAltitudeAGL = 0.0;
            _lowFuelTimer = 0f;
            SeedInitialChecklistMemos();
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            double atmoCutoff = GetAtmosphereCutoff(telemetry);
            double effectivePe = GetEffectivePeriapsis(telemetry);
            double effectiveAp = GetEffectiveApoapsis(telemetry);

            DetectAndIngestEvents(telemetry, atmoCutoff, effectivePe, effectiveAp);
            EvaluatePersistentAlerts(telemetry, deltaTime);

            EcamAlertLogState newState = default;
            newState.HasVessel = true;
            newState.ShowSystemStatusPage = ShowSystemStatusPage;

            int warnCount = 0;
            int cautCount = 0;
            int memoCount = 0;

            for (int i = 0; i < _activePersistentAlerts.Count; i++)
            {
                if (_activePersistentAlerts[i].Severity == EcamAlertSeverity.Warning) warnCount++;
                else if (_activePersistentAlerts[i].Severity == EcamAlertSeverity.Caution) cautCount++;
            }
            for (int i = 0; i < _logBuffer.Count; i++)
            {
                if (_logBuffer[i].Severity == EcamAlertSeverity.Warning) warnCount++;
                else if (_logBuffer[i].Severity == EcamAlertSeverity.Caution) cautCount++;
                else memoCount++;
            }

            newState.WarnCount = warnCount;
            newState.CautCount = cautCount;
            newState.MemoCount = memoCount;
            newState.LogCount = _logBuffer.Count;
            newState.PhaseText = FormatFlightPhase(telemetry.FlightSituation);

            if (ShowSystemStatusPage)
            {
                newState.StatusText = I18n.Tr("ECAM_SYS_STS", "机载系统工况 STS");
                newState.StatusRole = TextStyleRole.Accent;
            }
            else if (warnCount > 0)
            {
                newState.StatusText = I18n.Tr("WIDGET_ECAM_WARN", "严重危急警报");
                newState.StatusRole = TextStyleRole.Danger;
            }
            else if (cautCount > 0)
            {
                newState.StatusText = I18n.Tr("WIDGET_ECAM_ATTN", "需机组注意处理");
                newState.StatusRole = TextStyleRole.Warning;
            }
            else
            {
                newState.StatusText = I18n.Tr("WIDGET_ECAM_NORMAL", "系统工况受监控");
                newState.StatusRole = TextStyleRole.PrimaryValue;
            }

            int displayIdx = 0;
            for (int i = 0; i < _activePersistentAlerts.Count && displayIdx < MaxDisplayRows; i++)
            {
                var entry = _activePersistentAlerts[i];
                newState.SetRow(displayIdx++, new EcamRowState
                {
                    Visible = true,
                    Severity = entry.Severity,
                    Tag = !string.IsNullOrEmpty(entry.Tag) ? entry.Tag : entry.Icon,
                    Title = entry.Title,
                    Detail = entry.Detail,
                    Timestamp = entry.Timestamp,
                    IsPersistent = entry.IsPersistent
                });
            }
            for (int i = 0; i < _logBuffer.Count && displayIdx < MaxDisplayRows; i++)
            {
                var entry = _logBuffer[i];
                if (IsClearedMode && entry.IsAcknowledged) continue;
                newState.SetRow(displayIdx++, new EcamRowState
                {
                    Visible = true,
                    Severity = entry.Severity,
                    Tag = !string.IsNullOrEmpty(entry.Tag) ? entry.Tag : entry.Icon,
                    Title = entry.Title,
                    Detail = entry.Detail,
                    Timestamp = entry.Timestamp,
                    IsPersistent = entry.IsPersistent
                });
            }
            while (displayIdx < MaxDisplayRows)
            {
                newState.SetRow(displayIdx++, default);
            }

            newState.Diag0 = new EcamDiagRowState
            {
                Tag = "PROP",
                Title = I18n.Tr("ECAM_SYS_PROP", "动力推进系统 PROP"),
                Val = $"{telemetry.ActiveEngines} ENG",
                Aux = $"THR {Mathf.RoundToInt(telemetry.Throttle * 100f)}%",
                Role = TextStyleRole.Accent
            };
            newState.Diag1 = new EcamDiagRowState
            {
                Tag = "ELEC",
                Title = I18n.Tr("ECAM_SYS_ELEC", "机载电网能源 ELEC"),
                Val = $"{Mathf.RoundToInt((float)telemetry.EcPercent)}% EC",
                Aux = "BUS OK",
                Role = telemetry.EcPercent <= 20.0 ? TextStyleRole.Warning : TextStyleRole.PrimaryValue
            };
            newState.Diag2 = new EcamDiagRowState
            {
                Tag = "TRAJ",
                Title = I18n.Tr("ECAM_SYS_TRAJ", "轨道动力参数 TRAJ"),
                Val = $"Pe {FormatKm(effectivePe)}",
                Aux = $"Ap {FormatKm(effectiveAp)}",
                Role = TextStyleRole.Accent
            };
            newState.Diag3 = new EcamDiagRowState
            {
                Tag = "ATMO",
                Title = I18n.Tr("ECAM_SYS_ATMO", "飞行走廊环境 ATMO"),
                Val = $"M {telemetry.Mach:F1}",
                Aux = $"Q {telemetry.DynamicPressure:F1}k",
                Role = TextStyleRole.Label
            };
            newState.Diag4 = new EcamDiagRowState
            {
                Tag = "GUID",
                Title = I18n.Tr("ECAM_SYS_GUID", "姿态惯导工况 GUID"),
                Val = $"{telemetry.GForce:F1}G",
                Aux = $"STG {telemetry.CurrentStage}",
                Role = TextStyleRole.PrimaryValue
            };

            CurrentState = newState;
        }

        private static double GetAtmosphereCutoff(IFlightTelemetry telem)
        {
            if (telem == null) return 70000.0;
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double probeDepth = ExternalProbeRegistry.ResolveNumeric("ENV", "AtmosphereDepth");
                if (!double.IsNaN(probeDepth) && probeDepth >= 0.0) return probeDepth;
            }
            if (telem.AtmosphereDepth > 0.0) return telem.AtmosphereDepth;
            if (!telem.HasAtmosphere) return 0.0;
            if (telem.AtmosphericPressure > 0.0001) return 70000.0;
            return 0.0;
        }

        private static double GetEffectivePeriapsis(IFlightTelemetry telem)
        {
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double pPe = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisPeriapsis");
                if (double.IsNaN(pPe)) pPe = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "PERIAPSIS");
                if (!double.IsNaN(pPe)) return pPe;
            }
            return telem.Periapsis;
        }

        private static double GetEffectiveApoapsis(IFlightTelemetry telem)
        {
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double pAp = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisApoapsis");
                if (double.IsNaN(pAp)) pAp = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "APOAPSIS");
                if (!double.IsNaN(pAp)) return pAp;
            }
            return telem.Apoapsis;
        }

        private static string FormatKm(double meters)
        {
            if (double.IsNaN(meters)) return "--";
            double km = meters / 1000.0;
            if (Math.Abs(km) >= 1000.0) return $"{km / 1000.0:F1}M";
            return $"{km:F0}k";
        }

        private static string FormatMet(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int totalSec = Mathf.FloorToInt((float)seconds);
            int hrs = totalSec / 3600;
            int mins = (totalSec % 3600) / 60;
            int secs = totalSec % 60;
            if (hrs > 0) return $"+{hrs:D2}:{mins:D2}:{secs:D2}";
            return $"+{mins:D2}:{secs:D2}";
        }

        private void DetectAndIngestEvents(IFlightTelemetry telem, double atmoCutoff, double effectivePe, double effectiveAp)
        {
            string curMet = FormatMet(telem.MissionTime);

            if (_lastStage != -1)
            {
                bool sepSignal = telem.IsStageSeparating && !_lastIsStageSeparating;
                bool stageDropped = telem.CurrentStage < _lastStage;
                if (sepSignal || stageDropped)
                {
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Memo, "STG", "◀",
                        I18n.Tr("WIDGET_ALERT_SEPARATION", "级间分级分离"),
                        $"STG {telem.CurrentStage}", curMet, telem.MissionTime));
                }
            }

            if (_lastActiveEngines != -1)
            {
                bool ignSignal = telem.IsEngineIgniting && !_lastIsEngineIgniting;
                bool engStarted = (_lastActiveEngines == 0 && telem.ActiveEngines > 0 && telem.Throttle > 0.02f) ||
                                  (_lastThrottle <= 0.001f && telem.Throttle > 0.05f && telem.ActiveEngines > 0);
                if (ignSignal || engStarted)
                {
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Caution, "IGN", "▲",
                        I18n.Tr("WIDGET_ALERT_ENGINE_START", "主发动机点火启动"),
                        $"{telem.ActiveEngines} ENG", curMet, telem.MissionTime));
                }

                bool mecoCutoff = (_lastActiveEngines > 0 && telem.ActiveEngines == 0 &&
                                   (telem.FlightSituation == "FLYING" || telem.FlightSituation == "SUB_ORBITAL" || telem.FlightSituation == "ORBITING"));
                bool throttleCut = (_lastThrottle > 0.25f && telem.Throttle <= 0.001f && telem.ActiveEngines > 0 &&
                                    telem.VerticalSpeed > 10.0 && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH");
                if (mecoCutoff || throttleCut)
                {
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Caution, "MECO", "■",
                        I18n.Tr("WIDGET_ALERT_MECO", "主发关机 MECO 确认"),
                        "CUTOFF", curMet, telem.MissionTime));
                }
            }

            if (telem.HasManeuverNode && telem.ManeuverTimeToNode > 0.0 && telem.ManeuverTimeToNode <= 60.0)
            {
                if (_lastTimeToNode > 60.0 || _lastTimeToNode < 0.0)
                {
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Advisory, "NODE", "◆",
                        I18n.Tr("WIDGET_ALERT_MANEUVER_APPROACH", "接近机动节点 T-60s"),
                        $"Δv {telem.ManeuverDeltaV:F0}", curMet, telem.MissionTime));
                }
            }

            if (telem.HasManeuverNode && telem.ManeuverTimeToNode <= 2.0 && telem.Throttle > 0.05f)
            {
                if (!_lastManeuverBurnTriggered)
                {
                    _lastManeuverBurnTriggered = true;
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Advisory, "BURN", "▶",
                        I18n.Tr("WIDGET_ALERT_MANEUVER_BURN", "机动点火执行中"),
                        $"Δv {telem.ManeuverDeltaV:F0}", curMet, telem.MissionTime));
                }
            }
            else if (!telem.HasManeuverNode || telem.Throttle <= 0.01f)
            {
                _lastManeuverBurnTriggered = false;
            }

            if (_lastEffectivePe > -999999.0 && _lastEffectivePe < atmoCutoff && effectivePe >= atmoCutoff && effectiveAp >= atmoCutoff &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "ORB", "★",
                    I18n.Tr("WIDGET_ALERT_ORBIT_ACHIEVED", "入轨圆化建立完成"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            if (_lastEffectivePe >= atmoCutoff && _lastEffectiveAp >= atmoCutoff && effectivePe < atmoCutoff && effectivePe > -9000000.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "DEORB", "▼",
                    I18n.Tr("WIDGET_ALERT_DEORBIT", "飞船离轨制动进入走廊"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            bool isEscNow = (telem.FlightSituation == "ESCAPING") || (effectiveAp < 0 && effectiveAp > -9000000.0);
            bool wasEscBefore = (_lastFlightSituation == "ESCAPING") || (_lastEffectiveAp < 0 && _lastEffectiveAp > -9000000.0);
            if (!wasEscBefore && isEscNow && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "ESC", "▲",
                    I18n.Tr("WIDGET_ALERT_ESCAPE", "逃逸轨道建立 ESCAPE"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            if (!string.IsNullOrEmpty(_lastCelestialBody) && !string.IsNullOrEmpty(telem.CelestialBodyName) &&
                !string.Equals(_lastCelestialBody, telem.CelestialBodyName, StringComparison.OrdinalIgnoreCase))
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Advisory, "SOI", "◆",
                    I18n.Tr("WIDGET_ALERT_SOI_TRANSITION", "进入天体引力影响圈"),
                    telem.CelestialBodyName, curMet, telem.MissionTime));
            }

            if (atmoCutoff > 0.0 && _lastAltitude >= atmoCutoff && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed < -5.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "ENTRY", "▼",
                    I18n.Tr("WIDGET_ALERT_ATMOSPHERE_ENTRY", "穿越边界进入大气层"),
                    $"M {telem.Mach:F1}", curMet, telem.MissionTime));
            }

            if (atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.AltitudeASL > atmoCutoff * 0.35 && telem.Mach > 8.0 && telem.DynamicPressure > 12.0)
            {
                if (Math.Abs(telem.VerticalSpeed) > 10.0)
                {
                    PushLogEntry(new EcamLogEntry(
                        EcamAlertSeverity.Warning, "BLKOUT", "⚡",
                        I18n.Tr("WIDGET_ALERT_BLACKOUT", "等离子体再入黑障通信中断"),
                        $"Q {telem.DynamicPressure:F0}kPa", curMet, telem.MissionTime));
                }
            }

            if (telem.AltitudeAGL < 2000.0 && telem.AltitudeAGL > 15.0 && telem.VerticalSpeed < -15.0 && telem.Throttle > 0.40f && telem.ActiveEngines > 0)
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "BURN", "▼",
                    I18n.Tr("WIDGET_ALERT_SUICIDE_BURN", "动力减速着陆推力建立"),
                    $"AGL {telem.AltitudeAGL:F0}m", curMet, telem.MissionTime));
            }

            bool isLandedNow = (telem.FlightSituation == "LANDED" || telem.FlightSituation == "SPLASHED" || telem.IsTouchdownAlert);
            if (!_lastIsLanded && isLandedNow && _lastAltitudeAGL > 2.0)
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "TOUCH", "⚓",
                    I18n.Tr("WIDGET_ALERT_TOUCHDOWN", "着陆接地成功 TOUCHDOWN"),
                    "LANDED", curMet, telem.MissionTime));
            }

            _lastStage = telem.CurrentStage;
            _lastActiveEngines = telem.ActiveEngines;
            _lastThrottle = telem.Throttle;
            _lastIsStageSeparating = telem.IsStageSeparating;
            _lastIsEngineIgniting = telem.IsEngineIgniting;
            _lastTimeToNode = telem.HasManeuverNode ? telem.ManeuverTimeToNode : -1.0;
            _lastAltitude = telem.AltitudeASL;
            _lastAltitudeAGL = telem.AltitudeAGL;
            _lastEffectivePe = effectivePe;
            _lastEffectiveAp = effectiveAp;
            _lastCelestialBody = telem.CelestialBodyName;
            _lastFlightSituation = telem.FlightSituation;
            _lastIsLanded = isLandedNow;
        }

        private void EvaluatePersistentAlerts(IFlightTelemetry telem, float dt)
        {
            _activePersistentAlerts.Clear();
            string curMet = FormatMet(telem.MissionTime);

            float prop = telem.StagePropellantFraction;
            bool engineArmed = telem.ActiveEngines > 0 || (telem.TotalStageEngines > 0 && telem.Throttle > 0.001f);
            if (prop >= 0.40f || !engineArmed)
            {
                _lowFuelTimer = 0f;
            }
            else if (prop >= 0f && prop <= 0.15f)
            {
                _lowFuelTimer += dt;
                if (_lowFuelTimer >= 0.8f)
                {
                    int pct = Mathf.RoundToInt(prop * 100f);
                    if (prop <= 0.05f)
                    {
                        _activePersistentAlerts.Add(new EcamLogEntry(
                            EcamAlertSeverity.Warning, "FUEL", "!",
                            I18n.Tr("WIDGET_ALERT_MIN_FUEL_EMERGENCY", "最低燃油告警"), $"{pct}%", curMet, telem.MissionTime, true));
                    }
                    else
                    {
                        _activePersistentAlerts.Add(new EcamLogEntry(
                            EcamAlertSeverity.Caution, "FUEL", "▲",
                            I18n.Tr("WIDGET_ALERT_LOW_FUEL_ADVISORY", "低燃油提醒"), $"{pct}%", curMet, telem.MissionTime, true));
                    }
                }
            }

            bool severePullUp = (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 600.0 && telem.AltitudeAGL > 3.0) ||
                                (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 150.0 && telem.AltitudeAGL > 3.0);
            if (severePullUp)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "GPWS", "!",
                    I18n.Tr("WIDGET_ALERT_TERRAIN_PULL_UP", "近地拉起警告!"), $"{telem.AltitudeAGL:F0}m", curMet, telem.MissionTime, true));
            }
            else if (telem.VerticalSpeed < -15.0 && telem.AltitudeAGL < 1500.0 && telem.AltitudeAGL > 10.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "GPWS", "▲",
                    I18n.Tr("WIDGET_ALERT_EXCESS_SINK_RATE", "下沉率过大"), $"{telem.VerticalSpeed:F0}m/s", curMet, telem.MissionTime, true));
            }

            if (telem.EcPercent >= 0.0)
            {
                if (telem.EcPercent <= 5.0)
                {
                    _activePersistentAlerts.Add(new EcamLogEntry(
                        EcamAlertSeverity.Warning, "ELEC", "!",
                        I18n.Tr("WIDGET_ALERT_BATTERY_CRITICAL", "电池严重告急!"), $"{Mathf.RoundToInt((float)telem.EcPercent)}%", curMet, telem.MissionTime, true));
                }
                else if (telem.EcPercent <= 20.0)
                {
                    _activePersistentAlerts.Add(new EcamLogEntry(
                        EcamAlertSeverity.Caution, "ELEC", "▲",
                        I18n.Tr("WIDGET_ALERT_LOW_BATTERY_EC", "电池电量偏低"), $"{Mathf.RoundToInt((float)telem.EcPercent)}%", curMet, telem.MissionTime, true));
                }
            }

            if (telem.CabinTemp > 120.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "TEMP", "!",
                    I18n.Tr("WIDGET_ALERT_CABIN_OVERHEAT", "座舱过热告警!"), $"{Mathf.RoundToInt((float)telem.CabinTemp)}°C", curMet, telem.MissionTime, true));
            }
            if (telem.GForce > 9.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "G-LOAD", "!",
                    I18n.Tr("WIDGET_ALERT_EXCESS_GLOAD", "过载严重超限!"), $"{telem.GForce:F1}G", curMet, telem.MissionTime, true));
            }
        }

        private static string FormatFlightPhase(string sit)
        {
            if (string.IsNullOrEmpty(sit)) return I18n.Tr("WIDGET_EICAS_PHASE_CRUISE", "巡航");
            string upper = sit.ToUpperInvariant();
            if (upper.Contains("ORBIT")) return I18n.Tr("WIDGET_EICAS_PHASE_ORBIT", "轨道");
            if (upper.Contains("CRUISE")) return I18n.Tr("WIDGET_EICAS_PHASE_CRUISE", "巡航");
            if (upper.Contains("LAUNCH") || upper.Contains("PRE")) return I18n.Tr("WIDGET_EICAS_PHASE_PRELCH", "发射前");
            if (upper.Contains("FLY")) return I18n.Tr("WIDGET_EICAS_PHASE_FLIGHT", "飞行");
            if (upper.Contains("ESC")) return I18n.Tr("WIDGET_EICAS_PHASE_ESCAPE", "逃逸");
            if (upper.Contains("LAND")) return I18n.Tr("WIDGET_EICAS_PHASE_LANDED", "已着陆");
            if (upper.Contains("SPLASH")) return I18n.Tr("WIDGET_EICAS_PHASE_SPLASH", "溅落");
            if (upper.Contains("SUB")) return I18n.Tr("WIDGET_EICAS_PHASE_SUBORB", "亚轨道");
            if (upper.Contains("DOCK")) return I18n.Tr("WIDGET_EICAS_PHASE_DOCKED", "已对接");
            return upper.Length > 6 ? upper.Substring(0, 6) : upper;
        }
    }

    [FlightWidget("ecam_alert_log", "alert_log", "eicas_messages", "warning_log", Category = WidgetCategory.Systems, DisplayName = "ECAM 飞行告警与备忘日志", Description = "仿空客 ECAM / 波音 EICAS 集中式电子飞行告警屏：实时推演离轨、逃逸、黑障、分级与遥测异常全时序事件日志。", DefaultWidgetId = "custom.ecam_alert_log", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "core.ecam_alert_log", "ecam.alert_log", "custom.ecam_alert_log" })]
    public class EcamAlertLogWidget : BaseFlightWidget
    {
        private readonly EcamAlertLogLogic _logic = new EcamAlertLogLogic();
        protected override IWidgetLogic LogicCore => _logic;
        public override Vector2 BaseSize => new Vector2(BASE_WIDTH, BASE_HEIGHT);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;


        // 几何布局常量 (乘 CurrentDpiScale)
        private const float BASE_WIDTH = 300f;
        private const float BASE_HEIGHT = 178f;
        private const float HEADER_HEIGHT = 28f;
        private const float FOOTER_HEIGHT = 24f;
        private const float ROW_HEIGHT = 22f;
        private const float ROW_SPACING = 2f;
        private const int MAX_DISPLAY_ROWS = 5;

        // 视觉容器节点
        private Image _bgImage;
        private Outline _bgOutline;
        private Image _topAccentPip;

        // 顶栏节点 (Display Unit Header)
        private Image _headerBg;
        private Outline _headerDivider;
        private Image _modeBadgeBg;
        private Outline _modeBadgeOutline;
        private Text _modeBadgeText;
        private Text _headerTitle;
        private Image _phaseBadgeBg;
        private Outline _phaseBadgeOutline;
        private Text _phaseBadgeText;

        // 顶栏三色告警光字牌 (Korry Annunciator Tiles)
        private Image _warnBoxBg;
        private Outline _warnBoxOutline;
        private Image _warnTopPip;
        private Text _badgeWarnText;

        private Image _cautBoxBg;
        private Outline _cautBoxOutline;
        private Image _cautTopPip;
        private Text _badgeCautText;

        private Image _memoBoxBg;
        private Outline _memoBoxOutline;
        private Image _memoTopPip;
        private Text _badgeMemoText;

        // 日志行 UI 槽位
        private struct RowSlot
        {
            public GameObject Root;
            public Image RowBg;
            public Outline RowOutline;
            public Image LeftPipBar;
            public GameObject BadgeChip;
            public Image BadgeBg;
            public Outline BadgeOutline;
            public Text IconText;
            public Text TitleText;
            public GameObject DetailChip;
            public Image DetailBg;
            public Outline DetailOutline;
            public Text DetailText;
            public Text TimeText;
        }
        private readonly RowSlot[] _rowSlots = new RowSlot[MAX_DISPLAY_ROWS];

        // 底栏节点 (Cockpit ECP Controls)
        private Image _footerBg;
        private Outline _footerTopDivider;
        private Image _statusLedHalo;
        private Image _statusLedDot;
        private Text _statusText;
        private Text _bufferCountText;

        // 实体按键
        private Button _btnClr;
        private Image _btnClrBg;
        private Outline _btnClrOutline;
        private Text _btnClrText;
        private AvionicsButtonFeedback _btnClrFb;

        private Button _btnRcl;
        private Image _btnRclBg;
        private Outline _btnRclOutline;
        private Text _btnRclText;
        private AvionicsButtonFeedback _btnRclFb;

        private Button _btnSts;
        private Image _btnStsBg;
        private Outline _btnStsOutline;
        private Image _btnStsActiveBar;
        private Text _btnStsText;
        private AvionicsButtonFeedback _btnStsFb;

        private readonly CachedFloat _globalBlinkTimer = new CachedFloat(0f, tolerance: 0.001f);

        // 视觉缓存防抖
        private readonly Cached<string> _lastPhaseText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastWarnCount = new Cached<int>(-1);
        private readonly Cached<string> _lastWarnText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastCautCount = new Cached<int>(-1);
        private readonly Cached<string> _lastCautText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastMemoCount = new Cached<int>(-1);
        private readonly Cached<string> _lastMemoText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastStatusText = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastLogCount = new Cached<int>(-1);
        private readonly Cached<string> _lastBufferCountText = new Cached<string>(string.Empty);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 基底包围盒
            RectTransform.sizeDelta = new Vector2(BASE_WIDTH * s, BASE_HEIGHT * s);

            // 2. 卡片底板与边框 (0 颜色字面量)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1.2f * s, 1.2f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Emphasized, theme);

            // 顶端外壳荧光科技条 (Top Accent Pip)
            GameObject topPipObj = UIFactory.CreatePanel(transform, "TopAccentPip", new Vector2(48f * s, 2f * s),
                new Vector2(0f, (BASE_HEIGHT * 0.5f - 1f) * s), theme.AccentPrimary);
            _topAccentPip = topPipObj.GetComponent<Image>();

            // 3. 构建顶栏 Header
            BuildHeader(s, theme);

            // 4. 构建 5 槽位日志行
            BuildLogRows(s, theme);

            // 5. 构建底栏 Footer (含 CLR / RCL / STS 航电交互按键)
            BuildFooter(s, theme);

            // 6. 初始飞行清单备忘已由 LogicCore 预置

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Emphasized, t)));
            this.Controls.Register(new WidgetHeaderControl("header", "ECAM顶栏", _headerTitle != null ? _headerTitle.gameObject : null, _headerTitle, _modeBadgeText));
            if (_warnBoxBg != null) this.Controls.Register(new WidgetAnnunciatorControl("annunciators", "三色告警光字牌", _warnBoxBg.gameObject, _badgeWarnText, _badgeCautText, _warnBoxBg, _warnBoxOutline));
            if (_rowSlots[0].Root != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "rows_viewport", "日志行视口", _rowSlots[0].Root));
            if (_footerBg != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "footer", "ECP底栏", _footerBg.gameObject));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildHeader(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject headerObj = UIFactory.CreatePanel(transform, "Header", new Vector2(BASE_WIDTH * s, HEADER_HEIGHT * s),
                new Vector2(0f, (BASE_HEIGHT * 0.5f - HEADER_HEIGHT * 0.5f) * s), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _headerBg = headerObj.GetComponent<Image>();

            _headerDivider = headerObj.AddComponent<Outline>();
            _headerDivider.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _headerDivider.effectDistance = new Vector2(0f, -1f * s);

            // 1. ECAM 模式芯片 [ ● ECAM ]
            GameObject modeObj = UIFactory.CreatePanel(headerObj.transform, "ModeChip", new Vector2(40f * s, 16f * s),
                new Vector2((-BASE_WIDTH * 0.5f + 25f) * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme));
            _modeBadgeBg = modeObj.GetComponent<Image>();
            _modeBadgeOutline = modeObj.AddComponent<Outline>();
            _modeBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Subtle);
            _modeBadgeOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);

            _modeBadgeText = UIFactory.CreateText(modeObj.transform, "ModeText", "● ECAM", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _modeBadgeText.fontStyle = FontStyle.Bold;
            _modeBadgeText.rectTransform.sizeDelta = new Vector2(40f * s, 16f * s);

            // 2. 标题 (飞行告警与备忘)
            string titleStr = I18n.Tr("WIDGET_ECAM_LOG_TITLE", "飞行告警与备忘");
            _headerTitle = UIFactory.CreateText(headerObj.transform, "HeaderTitle", titleStr, Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _headerTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _headerTitle.rectTransform;
            titleRt.sizeDelta = new Vector2(78f * s, HEADER_HEIGHT * s);
            titleRt.anchoredPosition = new Vector2(-60f * s, 0f);

            // 3. 飞行阶段芯片 (Flight Phase Chip)
            GameObject phaseObj = UIFactory.CreatePanel(headerObj.transform, "PhaseChip", new Vector2(44f * s, 16f * s),
                new Vector2(6f * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _phaseBadgeBg = phaseObj.GetComponent<Image>();
            _phaseBadgeOutline = phaseObj.AddComponent<Outline>();
            _phaseBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _phaseBadgeOutline.effectDistance = new Vector2(0.6f * s, 0.6f * s);

            _phaseBadgeText = UIFactory.CreateText(phaseObj.transform, "PhaseText", I18n.Tr("PHASE_ORBIT", "轨道巡航"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Unit, theme));
            _phaseBadgeText.fontStyle = FontStyle.Bold;
            _phaseBadgeText.rectTransform.sizeDelta = new Vector2(44f * s, 16f * s);

            // 4. 三大航电告警光字牌 (Master Annunciator Tiles)
            // Warning [! 0]
            GameObject warnObj = UIFactory.CreatePanel(headerObj.transform, "TileWarn", new Vector2(28f * s, 16f * s),
                new Vector2(60f * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _warnBoxBg = warnObj.GetComponent<Image>();
            _warnBoxOutline = warnObj.AddComponent<Outline>();
            _warnBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.20f);
            _warnBoxOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            GameObject warnPipObj = UIFactory.CreatePanel(warnObj.transform, "TopPip", new Vector2(28f * s, 2f * s),
                new Vector2(0f, 7f * s), WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f));
            _warnTopPip = warnPipObj.GetComponent<Image>();
            _badgeWarnText = UIFactory.CreateText(warnObj.transform, "Text", "! 0", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.40f));
            _badgeWarnText.fontStyle = FontStyle.Bold;
            _badgeWarnText.rectTransform.sizeDelta = new Vector2(28f * s, 16f * s);

            // Caution [▲ 0]
            GameObject cautObj = UIFactory.CreatePanel(headerObj.transform, "TileCaut", new Vector2(28f * s, 16f * s),
                new Vector2(92f * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _cautBoxBg = cautObj.GetComponent<Image>();
            _cautBoxOutline = cautObj.AddComponent<Outline>();
            _cautBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.20f);
            _cautBoxOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            GameObject cautPipObj = UIFactory.CreatePanel(cautObj.transform, "TopPip", new Vector2(28f * s, 2f * s),
                new Vector2(0f, 7f * s), WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f));
            _cautTopPip = cautPipObj.GetComponent<Image>();
            _badgeCautText = UIFactory.CreateText(cautObj.transform, "Text", "▲ 0", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.40f));
            _badgeCautText.fontStyle = FontStyle.Bold;
            _badgeCautText.rectTransform.sizeDelta = new Vector2(28f * s, 16f * s);

            // Memo [● 0]
            GameObject memoObj = UIFactory.CreatePanel(headerObj.transform, "TileMemo", new Vector2(28f * s, 16f * s),
                new Vector2(124f * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _memoBoxBg = memoObj.GetComponent<Image>();
            _memoBoxOutline = memoObj.AddComponent<Outline>();
            _memoBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.20f);
            _memoBoxOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            GameObject memoPipObj = UIFactory.CreatePanel(memoObj.transform, "TopPip", new Vector2(28f * s, 2f * s),
                new Vector2(0f, 7f * s), WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f));
            _memoTopPip = memoPipObj.GetComponent<Image>();
            _badgeMemoText = UIFactory.CreateText(memoObj.transform, "Text", "● 0", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleCenter, WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.40f));
            _badgeMemoText.fontStyle = FontStyle.Bold;
            _badgeMemoText.rectTransform.sizeDelta = new Vector2(28f * s, 16f * s);
        }

        private void BuildLogRows(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float startY = (BASE_HEIGHT * 0.5f - HEADER_HEIGHT - 2f - ROW_HEIGHT * 0.5f) * s;
            float stepY = (ROW_HEIGHT + ROW_SPACING) * s;

            for (int i = 0; i < MAX_DISPLAY_ROWS; i++)
            {
                float rowY = startY - (i * stepY);
                GameObject rowObj = UIFactory.CreatePanel(transform, $"Row_{i}", new Vector2((BASE_WIDTH - 8f) * s, ROW_HEIGHT * s),
                    new Vector2(0f, rowY), WidgetStyleManager.WithAlpha(theme.AccentSecondary, i % 2 == 0 ? 0.04f : 0.07f));
                Image rowBg = rowObj.GetComponent<Image>();

                Outline rowLine = rowObj.AddComponent<Outline>();
                rowLine.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f);
                rowLine.effectDistance = new Vector2(0.6f * s, 0.6f * s);

                // 1. 左侧竖条色彩指示器 (3.5px 宽 PipBar)
                GameObject pipObj = UIFactory.CreatePanel(rowObj.transform, "PipBar", new Vector2(3.5f * s, (ROW_HEIGHT - 4f) * s),
                    new Vector2((-BASE_WIDTH * 0.5f + 7.5f) * s, 0f), theme.AccentPositive);
                Image pipBar = pipObj.GetComponent<Image>();

                // 2. 等级与系统标签芯片 (Badge Chip)
                GameObject badgeObj = UIFactory.CreatePanel(rowObj.transform, "BadgeChip", new Vector2(28f * s, 16f * s),
                    new Vector2((-BASE_WIDTH * 0.5f + 25.5f) * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                Image badgeBg = badgeObj.GetComponent<Image>();
                Outline badgeOutline = badgeObj.AddComponent<Outline>();
                badgeOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.5f);
                badgeOutline.effectDistance = new Vector2(0.6f * s, 0.6f * s);

                Text iconText = UIFactory.CreateText(badgeObj.transform, "Icon", I18n.Tr("WIDGET_EICAS_MEMO", "备忘"), Mathf.RoundToInt(7.5f * s),
                    TextAnchor.MiddleCenter, theme.AccentPositive);
                iconText.fontStyle = FontStyle.Bold;
                iconText.rectTransform.sizeDelta = new Vector2(28f * s, 16f * s);

                // 3. 主标题 (Title)
                Text titleText = UIFactory.CreateText(rowObj.transform, "Title", I18n.Tr("WIDGET_EICAS_SYSTEM_NOMINAL", "系统正常"), Mathf.RoundToInt(9.5f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                titleText.fontStyle = FontStyle.Bold;
                titleText.rectTransform.sizeDelta = new Vector2(116f * s, ROW_HEIGHT * s);
                titleText.rectTransform.anchoredPosition = new Vector2(-36f * s, 0f);

                // 4. 遥测读数芯片 (Detail Chip)
                GameObject detailObj = UIFactory.CreatePanel(rowObj.transform, "DetailChip", new Vector2(52f * s, 16f * s),
                    new Vector2(53f * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                Image detailBg = detailObj.GetComponent<Image>();
                Outline detailOutline = detailObj.AddComponent<Outline>();
                detailOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.22f);
                detailOutline.effectDistance = new Vector2(0.6f * s, 0.6f * s);

                Text detailText = UIFactory.CreateText(detailObj.transform, "DetailText", "---", Mathf.RoundToInt(8f * s),
                    TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                detailText.fontStyle = FontStyle.Bold;
                detailText.rectTransform.sizeDelta = new Vector2(52f * s, 16f * s);

                // 5. 任务时钟 MET (Chrono Timestamp)
                Text timeText = UIFactory.CreateText(rowObj.transform, "Time", "+00:00:00", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Unit, theme));
                timeText.rectTransform.sizeDelta = new Vector2(46f * s, ROW_HEIGHT * s);
                timeText.rectTransform.anchoredPosition = new Vector2((BASE_WIDTH * 0.5f - 28f) * s, 0f);

                _rowSlots[i] = new RowSlot
                {
                    Root = rowObj,
                    RowBg = rowBg,
                    RowOutline = rowLine,
                    LeftPipBar = pipBar,
                    BadgeChip = badgeObj,
                    BadgeBg = badgeBg,
                    BadgeOutline = badgeOutline,
                    IconText = iconText,
                    TitleText = titleText,
                    DetailChip = detailObj,
                    DetailBg = detailBg,
                    DetailOutline = detailOutline,
                    DetailText = detailText,
                    TimeText = timeText
                };
            }
        }

        private void BuildFooter(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject footerObj = UIFactory.CreatePanel(transform, "Footer", new Vector2(BASE_WIDTH * s, FOOTER_HEIGHT * s),
                new Vector2(0f, (-BASE_HEIGHT * 0.5f + FOOTER_HEIGHT * 0.5f) * s), WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _footerBg = footerObj.GetComponent<Image>();

            _footerTopDivider = footerObj.AddComponent<Outline>();
            _footerTopDivider.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _footerTopDivider.effectDistance = new Vector2(0f, 1f * s);

            // 1. 左侧状态同心双环 LED 指示灯 (Dual Concentric LED Halo & Dot)
            GameObject haloObj = UIFactory.CreatePanel(footerObj.transform, "StatusLedHalo", new Vector2(9f * s, 9f * s),
                new Vector2((-BASE_WIDTH * 0.5f + 12f) * s, 0f), WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.25f));
            _statusLedHalo = haloObj.GetComponent<Image>();

            GameObject ledObj = UIFactory.CreatePanel(haloObj.transform, "StatusLedDot", new Vector2(4.5f * s, 4.5f * s),
                Vector2.zero, theme.AccentPositive);
            _statusLedDot = ledObj.GetComponent<Image>();

            // 2. 状态文本
            string normalStr = I18n.Tr("WIDGET_ECAM_NORMAL", "系统工况受监控");
            _statusText = UIFactory.CreateText(footerObj.transform, "Status", normalStr, Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, theme.AccentPositive);
            _statusText.fontStyle = FontStyle.Bold;
            _statusText.rectTransform.sizeDelta = new Vector2(98f * s, FOOTER_HEIGHT * s);
            _statusText.rectTransform.anchoredPosition = new Vector2((-BASE_WIDTH * 0.5f + 68f) * s, 0f);

            // 3. 缓冲日志计数标签 (Buffer Count Tag)
            _bufferCountText = UIFactory.CreateText(footerObj.transform, "BufCount", I18n.Tr("WIDGET_EICAS_LOG_COUNT", "日志 5/40"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Unit, theme));
            _bufferCountText.fontStyle = FontStyle.Bold;
            _bufferCountText.rectTransform.sizeDelta = new Vector2(42f * s, FOOTER_HEIGHT * s);
            _bufferCountText.rectTransform.anchoredPosition = new Vector2(-2f * s, 0f);

            // 4. 右侧 ECAM 实体物理按键 (CLR / RCL / STS)
            // CLR 按键
            GameObject btnClrObj = UIFactory.CreatePanel(footerObj.transform, "BtnCLR", new Vector2(30f * s, 16f * s),
                new Vector2((BASE_WIDTH * 0.5f - 88f) * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme));
            _btnClrBg = btnClrObj.GetComponent<Image>();
            _btnClrOutline = btnClrObj.AddComponent<Outline>();
            _btnClrOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle);
            _btnClrOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            _btnClrText = UIFactory.CreateText(btnClrObj.transform, "Text", I18n.Tr("WIDGET_ECAM_CLR", "消警"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _btnClrText.fontStyle = FontStyle.Bold;
            _btnClrText.rectTransform.sizeDelta = new Vector2(30f * s, 16f * s);
            _btnClr = btnClrObj.AddComponent<Button>();
            _btnClrFb = btnClrObj.AddComponent<AvionicsButtonFeedback>();
            _btnClrFb.Initialize(_btnClr, _btnClrBg, _btnClrOutline, _btnClrText, theme);
            _btnClr.onClick.AddListener(ClearAcknowledgedAlerts);

            // RCL 按键
            GameObject btnRclObj = UIFactory.CreatePanel(footerObj.transform, "BtnRCL", new Vector2(30f * s, 16f * s),
                new Vector2((BASE_WIDTH * 0.5f - 54f) * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme));
            _btnRclBg = btnRclObj.GetComponent<Image>();
            _btnRclOutline = btnRclObj.AddComponent<Outline>();
            _btnRclOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle);
            _btnRclOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            _btnRclText = UIFactory.CreateText(btnRclObj.transform, "Text", I18n.Tr("WIDGET_ECAM_RCL", "召回"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _btnRclText.fontStyle = FontStyle.Bold;
            _btnRclText.rectTransform.sizeDelta = new Vector2(30f * s, 16f * s);
            _btnRcl = btnRclObj.AddComponent<Button>();
            _btnRclFb = btnRclObj.AddComponent<AvionicsButtonFeedback>();
            _btnRclFb.Initialize(_btnRcl, _btnRclBg, _btnRclOutline, _btnRclText, theme);
            _btnRcl.onClick.AddListener(RecallClearedAlerts);

            // STS 按键
            GameObject btnStsObj = UIFactory.CreatePanel(footerObj.transform, "BtnSTS", new Vector2(32f * s, 16f * s),
                new Vector2((BASE_WIDTH * 0.5f - 20f) * s, 0f), WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme));
            _btnStsBg = btnStsObj.GetComponent<Image>();
            _btnStsOutline = btnStsObj.AddComponent<Outline>();
            _btnStsOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Subtle);
            _btnStsOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);

            GameObject stsBarObj = UIFactory.CreatePanel(btnStsObj.transform, "ActiveBar", new Vector2(32f * s, 2f * s),
                new Vector2(0f, 7f * s), Color.clear);
            _btnStsActiveBar = stsBarObj.GetComponent<Image>();

            _btnStsText = UIFactory.CreateText(btnStsObj.transform, "Text", I18n.Tr("WIDGET_ECAM_STS", "状态"), Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _btnStsText.fontStyle = FontStyle.Bold;
            _btnStsText.rectTransform.sizeDelta = new Vector2(32f * s, 16f * s);
            _btnSts = btnStsObj.AddComponent<Button>();
            _btnStsFb = btnStsObj.AddComponent<AvionicsButtonFeedback>();
            _btnStsFb.Initialize(_btnSts, _btnStsBg, _btnStsOutline, _btnStsText, theme);
            _btnSts.onClick.AddListener(ToggleStatusPage);
        }

        public void PushLogEntry(EcamLogEntry entry) => _logic.PushLogEntry(entry);
        public void ClearAcknowledgedAlerts() => _logic.ClearAcknowledgedAlerts();
        public void RecallClearedAlerts() => _logic.RecallClearedAlerts();
        public void ToggleStatusPage() => _logic.ToggleStatusPage();

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Emphasized, theme);
            if (_topAccentPip != null) _topAccentPip.color = theme.AccentPrimary;

            if (_headerBg != null) _headerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_headerDivider != null) _headerDivider.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            if (_modeBadgeBg != null) _modeBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme);
            if (_modeBadgeOutline != null) _modeBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Subtle);
            if (_modeBadgeText != null) _modeBadgeText.color = theme.AccentPrimary;

            ApplyText(_headerTitle, TextStyleRole.Cardinal, theme);

            if (_phaseBadgeBg != null) _phaseBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_phaseBadgeOutline != null) _phaseBadgeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_phaseBadgeText, TextStyleRole.Unit, theme);

            if (_warnBoxBg != null) _warnBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_warnBoxOutline != null) _warnBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
            if (_warnTopPip != null) _warnTopPip.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
            if (_badgeWarnText != null) _badgeWarnText.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);

            if (_cautBoxBg != null) _cautBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_cautBoxOutline != null) _cautBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
            if (_cautTopPip != null) _cautTopPip.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
            if (_badgeCautText != null) _badgeCautText.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);

            if (_memoBoxBg != null) _memoBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_memoBoxOutline != null) _memoBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f);
            if (_memoTopPip != null) _memoTopPip.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f);
            if (_badgeMemoText != null) _badgeMemoText.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.40f);

            if (_footerBg != null) _footerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_footerTopDivider != null) _footerTopDivider.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_statusLedHalo != null) _statusLedHalo.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.25f);
            if (_statusLedDot != null) _statusLedDot.color = theme.AccentPositive;
            ApplyText(_statusText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_bufferCountText, TextStyleRole.Unit, theme);

            _btnClrFb?.ApplyTheme(theme);
            _btnRclFb?.ApplyTheme(theme);
            _btnStsFb?.ApplyTheme(theme);
            if (_btnStsActiveBar != null) _btnStsActiveBar.color = _logic.ShowSystemStatusPage ? theme.AccentPrimary : Color.clear;

            for (int i = 0; i < MAX_DISPLAY_ROWS; i++)
            {
                RowSlot slot = _rowSlots[i];
                if (slot.RowBg != null) slot.RowBg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, i % 2 == 0 ? 0.04f : 0.07f);
                if (slot.RowOutline != null) slot.RowOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f);
                if (slot.BadgeBg != null) slot.BadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (slot.DetailBg != null) slot.DetailBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (slot.DetailOutline != null) slot.DetailOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.22f);
                ApplyText(slot.DetailText, TextStyleRole.SecondaryValue, theme);
                ApplyText(slot.TimeText, TextStyleRole.Unit, theme);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            _logic.Evaluate(context.Telemetry, context.DeltaTime);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            _globalBlinkTimer.Value += context.DeltaTime;
            if (_globalBlinkTimer.Value >= 1000f) _globalBlinkTimer.Value = 0f;

            EcamAlertLogState state = _logic.CurrentState;
            OnRenderState(in state, context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme);
        }

        protected override void OnResetPrivateCache()
        {
            _lastPhaseText.Reset();
            _lastWarnCount.Reset();
            _lastWarnText.Reset();
            _lastCautCount.Reset();
            _lastCautText.Reset();
            _lastMemoCount.Reset();
            _lastMemoText.Reset();
            _lastStatusText.Reset();
            _lastLogCount.Reset();
            _lastBufferCountText.Reset();
            _logic.Reset();
        }

        private void OnRenderState(in EcamAlertLogState state, ThemeConfig theme)
        {
            if (!state.HasVessel) return;

            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            bool blinkOn = ((int)(_globalBlinkTimer.Value * 2f)) % 2 == 0;

            // 1. 顶栏飞行阶段胶囊更新
            if (_lastPhaseText.Update(state.PhaseText))
            {
                SetTextIfChanged(_phaseBadgeText, state.PhaseText);
            }

            // 2. 顶栏光字牌更新 (Master Annunciator Tiles)
            if (_lastWarnCount.Update(state.WarnCount))
            {
                string warnStr = $"! {state.WarnCount}";
                _lastWarnText.Update(warnStr);
                SetTextIfChanged(_badgeWarnText, warnStr);
            }
            if (state.WarnCount > 0)
            {
                Color warnCol = blinkOn ? theme.DangerColor : WidgetStyleManager.WithAlpha(theme.DangerColor, 0.60f);
                if (_warnBoxBg != null) _warnBoxBg.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, blinkOn ? 0.35f : 0.18f));
                if (_warnBoxOutline != null) _warnBoxOutline.SetColor(warnCol);
                if (_warnTopPip != null) _warnTopPip.SetColor(warnCol);
                if (_badgeWarnText != null) _badgeWarnText.SetColor(warnCol);
            }
            else
            {
                if (_warnBoxBg != null) _warnBoxBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                if (_warnBoxOutline != null) _warnBoxOutline.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f));
                if (_warnTopPip != null) _warnTopPip.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f));
                if (_badgeWarnText != null) _badgeWarnText.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f));
            }

            if (_lastCautCount.Update(state.CautCount))
            {
                string cautStr = $"▲ {state.CautCount}";
                _lastCautText.Update(cautStr);
                SetTextIfChanged(_badgeCautText, cautStr);
            }
            if (state.CautCount > 0)
            {
                if (_cautBoxBg != null) _cautBoxBg.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.25f));
                if (_cautBoxOutline != null) _cautBoxOutline.SetColor(theme.WarningColor);
                if (_cautTopPip != null) _cautTopPip.SetColor(theme.WarningColor);
                if (_badgeCautText != null) _badgeCautText.SetColor(theme.WarningColor);
            }
            else
            {
                if (_cautBoxBg != null) _cautBoxBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                if (_cautBoxOutline != null) _cautBoxOutline.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f));
                if (_cautTopPip != null) _cautTopPip.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f));
                if (_badgeCautText != null) _badgeCautText.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f));
            }

            if (_lastMemoCount.Update(state.MemoCount))
            {
                string memoStr = $"● {state.MemoCount}";
                _lastMemoText.Update(memoStr);
                SetTextIfChanged(_badgeMemoText, memoStr);
            }
            if (state.MemoCount > 0)
            {
                if (_memoBoxBg != null) _memoBoxBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.18f));
                if (_memoBoxOutline != null) _memoBoxOutline.SetColor(theme.AccentPositive);
                if (_memoTopPip != null) _memoTopPip.SetColor(theme.AccentPositive);
                if (_badgeMemoText != null) _badgeMemoText.SetColor(theme.AccentPositive);
            }
            else
            {
                if (_memoBoxBg != null) _memoBoxBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                if (_memoBoxOutline != null) _memoBoxOutline.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f));
                if (_memoTopPip != null) _memoTopPip.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f));
                if (_badgeMemoText != null) _badgeMemoText.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.40f));
            }

            // 3. 底栏总体工况显示
            if (state.ShowSystemStatusPage)
            {
                RenderSystemStatusPage(in state, theme);
                return;
            }

            if (_btnStsActiveBar != null) _btnStsActiveBar.SetColor(Color.clear);
            if (_btnStsBg != null) _btnStsBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme));
            if (_btnStsOutline != null) _btnStsOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Subtle));

            if (_lastStatusText.Update(state.StatusText)) SetTextIfChanged(_statusText, state.StatusText);
            Color statusCol = style.GetTextColor(state.StatusRole, theme);
            if (_statusText != null) _statusText.SetColor(statusCol);

            if (state.WarnCount > 0)
            {
                Color dotCol = blinkOn ? theme.DangerColor : WidgetStyleManager.WithAlpha(theme.DangerColor, 0.3f);
                if (_statusLedDot != null) _statusLedDot.SetColor(dotCol);
                if (_statusLedHalo != null) _statusLedHalo.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, blinkOn ? 0.40f : 0.15f));
            }
            else if (state.CautCount > 0)
            {
                if (_statusLedDot != null) _statusLedDot.SetColor(theme.WarningColor);
                if (_statusLedHalo != null) _statusLedHalo.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.25f));
            }
            else
            {
                if (_statusLedDot != null) _statusLedDot.SetColor(theme.AccentPositive);
                if (_statusLedHalo != null) _statusLedHalo.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.25f));
            }

            // 4. 缓冲日志计数
            if (_lastLogCount.Update(state.LogCount))
            {
                string bufStr = $"LOG {state.LogCount}/40";
                _lastBufferCountText.Update(bufStr);
                SetTextIfChanged(_bufferCountText, bufStr);
            }

            // 5. 渲染 5 槽位
            for (int slotIdx = 0; slotIdx < MAX_DISPLAY_ROWS; slotIdx++)
            {
                RowSlot slot = _rowSlots[slotIdx];
                EcamRowState row = state.GetRow(slotIdx);
                if (row.Visible)
                {
                    if (!slot.Root.activeSelf) slot.Root.SetActive(true);

                    Color itemColor;
                    switch (row.Severity)
                    {
                        case EcamAlertSeverity.Warning: itemColor = theme.DangerColor; break;
                        case EcamAlertSeverity.Caution: itemColor = theme.WarningColor; break;
                        case EcamAlertSeverity.Advisory: itemColor = theme.AccentPrimary; break;
                        case EcamAlertSeverity.Memo: default: itemColor = theme.AccentPositive; break;
                    }

                    if (row.Severity == EcamAlertSeverity.Warning && !blinkOn)
                    {
                        itemColor = WidgetStyleManager.WithAlpha(itemColor, 0.45f);
                    }

                    // 1. 左侧竖条指示色彩
                    if (slot.LeftPipBar != null) slot.LeftPipBar.SetColor(itemColor);

                    // 2. 等级图标与子系统芯片 (Badge Chip)
                    if (slot.BadgeBg != null) slot.BadgeBg.SetColor(WidgetStyleManager.WithAlpha(itemColor, 0.16f));
                    if (slot.BadgeOutline != null) slot.BadgeOutline.SetColor(WidgetStyleManager.WithAlpha(itemColor, 0.65f));
                    SetTextIfChanged(slot.IconText, row.Tag);
                    if (slot.IconText != null) slot.IconText.SetColor(itemColor);

                    // 3. 标题
                    SetTextIfChanged(slot.TitleText, row.Title);
                    if (slot.TitleText != null)
                    {
                        slot.TitleText.SetColor((row.Severity == EcamAlertSeverity.Warning || row.Severity == EcamAlertSeverity.Caution)
                            ? itemColor
                            : style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                    }

                    // 4. 遥测读数芯片
                    SetTextIfChanged(slot.DetailText, row.Detail);
                    if (slot.DetailBg != null) slot.DetailBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                    if (slot.DetailOutline != null) slot.DetailOutline.SetColor(WidgetStyleManager.WithAlpha(itemColor, 0.35f));
                    if (slot.DetailText != null) slot.DetailText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));

                    // 5. 任务时钟
                    SetTextIfChanged(slot.TimeText, row.Timestamp);
                    if (slot.TimeText != null) slot.TimeText.SetColor(style.GetTextColor(TextStyleRole.Unit, theme));

                    // 6. 卡片底色与微光边框
                    if (slot.RowBg != null)
                    {
                        slot.RowBg.SetColor(row.IsPersistent
                            ? WidgetStyleManager.WithAlpha(itemColor, 0.15f)
                            : WidgetStyleManager.WithAlpha(theme.AccentSecondary, slotIdx % 2 == 0 ? 0.04f : 0.07f));
                    }
                    if (slot.RowOutline != null)
                    {
                        slot.RowOutline.SetColor(row.IsPersistent
                            ? WidgetStyleManager.WithAlpha(itemColor, 0.50f)
                            : WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f));
                    }
                }
                else
                {
                    if (slot.Root.activeSelf) slot.Root.SetActive(false);
                }
            }
        }

        private void RenderSystemStatusPage(in EcamAlertLogState state, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            if (_lastStatusText.Update(state.StatusText)) SetTextIfChanged(_statusText, state.StatusText);
            if (_statusText != null) _statusText.SetColor(theme.AccentPrimary);
            if (_statusLedDot != null) _statusLedDot.SetColor(theme.AccentPrimary);
            if (_statusLedHalo != null) _statusLedHalo.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.25f));
            SetTextIfChanged(_bufferCountText, I18n.Tr("WIDGET_EICAS_PAGE_COUNT", "页 1/1"));

            if (_btnStsBg != null) _btnStsBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.35f));
            if (_btnStsOutline != null) _btnStsOutline.SetColor(theme.AccentPrimary);
            if (_btnStsActiveBar != null) _btnStsActiveBar.SetColor(theme.AccentPrimary);

            for (int i = 0; i < MAX_DISPLAY_ROWS; i++)
            {
                RowSlot slot = _rowSlots[i];
                if (!slot.Root.activeSelf) slot.Root.SetActive(true);
                EcamDiagRowState d = state.GetDiag(i);
                Color col = style.GetTextColor(d.Role, theme);

                if (slot.LeftPipBar != null) slot.LeftPipBar.SetColor(col);
                if (slot.BadgeBg != null) slot.BadgeBg.SetColor(WidgetStyleManager.WithAlpha(col, 0.16f));
                if (slot.BadgeOutline != null) slot.BadgeOutline.SetColor(WidgetStyleManager.WithAlpha(col, 0.65f));
                SetTextIfChanged(slot.IconText, d.Tag);
                if (slot.IconText != null) slot.IconText.SetColor(col);

                SetTextIfChanged(slot.TitleText, d.Title);
                if (slot.TitleText != null) slot.TitleText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));

                SetTextIfChanged(slot.DetailText, d.Val);
                if (slot.DetailBg != null) slot.DetailBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                if (slot.DetailOutline != null) slot.DetailOutline.SetColor(WidgetStyleManager.WithAlpha(col, 0.35f));
                if (slot.DetailText != null) slot.DetailText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));

                SetTextIfChanged(slot.TimeText, d.Aux);
                if (slot.TimeText != null) slot.TimeText.SetColor(style.GetTextColor(TextStyleRole.Unit, theme));

                if (slot.RowBg != null) slot.RowBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentSecondary, i % 2 == 0 ? 0.04f : 0.07f));
                if (slot.RowOutline != null) slot.RowOutline.SetColor(WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f));
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
