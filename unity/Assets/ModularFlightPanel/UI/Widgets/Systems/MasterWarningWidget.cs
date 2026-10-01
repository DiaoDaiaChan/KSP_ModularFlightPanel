using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public enum BannerDisplayState
    {
        Normal,         // 标准工作/待命 (Caution & Warning)
        MergingIn,      // 双室向中央滑动合并 (2模块模式, 约 0.10s) / 状态条滑入 (3模块模式)
        MergedHolding,  // 一体横幅高亮呈现当前事件 (单事件 1.25s ~ 2.0s / 链式连击事件 0.95s)
        SwitchingEvent, // 队列中存在后续事件，就地微闪平滑切换至下一事件 (约 0.08s)
        FlashingBack    // 队列全部消费完毕，高频频闪复位回到常态 (约 0.14s)
    }

    public enum EventColorRole
    {
        AccentPrimary,
        AccentSecondary,
        WarningColor,
        DangerColor,
        Success
    }

    // 告警单元结构体 (支持零 GC 判等与分桶脏检查)
    public struct AlertItem : IEquatable<AlertItem>
    {
        public string MainTitle;
        public string TelemetryAffix;
        public bool IsWarning;

        public AlertItem(string title, string affix, bool isWarning)
        {
            MainTitle = title;
            TelemetryAffix = affix;
            IsWarning = isWarning;
        }

        public bool Equals(AlertItem other)
        {
            return IsWarning == other.IsWarning &&
                   string.Equals(MainTitle, other.MainTitle, StringComparison.Ordinal) &&
                   string.Equals(TelemetryAffix, other.TelemetryAffix, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is AlertItem other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (MainTitle != null ? StringComparer.Ordinal.GetHashCode(MainTitle) : 0);
                hash = (hash * 397) ^ (TelemetryAffix != null ? StringComparer.Ordinal.GetHashCode(TelemetryAffix) : 0);
                hash = (hash * 397) ^ IsWarning.GetHashCode();
                return hash;
            }
        }
    }

    // 横幅事件定义模型 (轻量结构体，0 GC)
    public struct BannerEventItem
    {
        public MasterWarningWidget.BannerEventType EventType;
        public string Title;
        public string Sub;
        public string LeftIcon;
        public string RightIcon;
        public float Duration;
        public int Priority; // 调度优先级 (数值越大越优先展示)
        public EventColorRole ColorRole;

        public BannerEventItem(MasterWarningWidget.BannerEventType type, string title, string sub, string leftIcon, string rightIcon, float duration, int priority, EventColorRole colorRole)
        {
            EventType = type;
            Title = title;
            Sub = sub;
            LeftIcon = leftIcon;
            RightIcon = rightIcon;
            Duration = duration;
            Priority = priority;
            ColorRole = colorRole;
        }
    }

    /// <summary>
    /// 中央主告警纯状态快照 (0 GC 纯值结构体，SPEC-012)
    /// </summary>
    public struct MasterWarningState : IEquatable<MasterWarningState>
    {
        public bool HasVessel;
        public int ModulesCount;

        // Caution 舱位
        public bool HasCautAlert;
        public bool CautBlink;
        public string CautTitle;
        public string CautSub;
        public string CautIcon;
        public int CautCount;
        public bool CautAcknowledged;

        // Warning 舱位
        public bool HasWarnAlert;
        public bool WarnBlink;
        public string WarnTitle;
        public string WarnSub;
        public string WarnIcon;
        public int WarnCount;
        public bool WarnAcknowledged;

        // 横幅状态机
        public BannerDisplayState BannerState;
        public float BannerSlideX;
        public float BannerTimer;
        public float BannerDuration;
        public float BannerPulseAlpha;
        public string EventTitle;
        public string EventSub;
        public string EventLeftIcon;
        public string EventRightIcon;
        public EventColorRole EventColor;

        // 巡航工况
        public string NominalTitle;
        public string NominalSub;
        public string NominalIcon;
        public EventColorRole NominalRole;

        public bool Equals(MasterWarningState other)
        {
            return HasVessel == other.HasVessel &&
                   ModulesCount == other.ModulesCount &&
                   HasCautAlert == other.HasCautAlert &&
                   CautBlink == other.CautBlink &&
                   CautCount == other.CautCount &&
                   CautAcknowledged == other.CautAcknowledged &&
                   HasWarnAlert == other.HasWarnAlert &&
                   WarnBlink == other.WarnBlink &&
                   WarnCount == other.WarnCount &&
                   WarnAcknowledged == other.WarnAcknowledged &&
                   BannerState == other.BannerState &&
                   Mathf.Approximately(BannerSlideX, other.BannerSlideX) &&
                   Mathf.Approximately(BannerPulseAlpha, other.BannerPulseAlpha) &&
                   EventColor == other.EventColor &&
                   NominalRole == other.NominalRole &&
                   string.Equals(CautTitle, other.CautTitle, StringComparison.Ordinal) &&
                   string.Equals(CautSub, other.CautSub, StringComparison.Ordinal) &&
                   string.Equals(CautIcon, other.CautIcon, StringComparison.Ordinal) &&
                   string.Equals(WarnTitle, other.WarnTitle, StringComparison.Ordinal) &&
                   string.Equals(WarnSub, other.WarnSub, StringComparison.Ordinal) &&
                   string.Equals(WarnIcon, other.WarnIcon, StringComparison.Ordinal) &&
                   string.Equals(EventTitle, other.EventTitle, StringComparison.Ordinal) &&
                   string.Equals(EventSub, other.EventSub, StringComparison.Ordinal) &&
                   string.Equals(EventLeftIcon, other.EventLeftIcon, StringComparison.Ordinal) &&
                   string.Equals(EventRightIcon, other.EventRightIcon, StringComparison.Ordinal) &&
                   string.Equals(NominalTitle, other.NominalTitle, StringComparison.Ordinal) &&
                   string.Equals(NominalSub, other.NominalSub, StringComparison.Ordinal) &&
                   string.Equals(NominalIcon, other.NominalIcon, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is MasterWarningState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ ModulesCount;
                hash = (hash * 397) ^ (int)BannerState;
                hash = (hash * 397) ^ CautCount;
                hash = (hash * 397) ^ WarnCount;
                return hash;
            }
        }
    }

    /// <summary>
    /// 中央主告警纯业务解耦大脑 (0 GC / 100% 游戏引擎解耦，SPEC-012)
    /// </summary>
    public class MasterWarningLogic : WidgetLogic<MasterWarningState>
    {
        // 模块数量：2 (经典聚拢) 或 3 (金字塔型)
        private int _modulesCount = 3;
        public int ModulesCount => _modulesCount;

        // 告警队列与分频分桶
        private readonly List<AlertItem> _cautAlerts = new List<AlertItem>(8);
        private readonly List<AlertItem> _warnAlerts = new List<AlertItem>(8);
        private readonly List<AlertItem> _urgentCautAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _urgentWarnAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _subsysCautAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _subsysWarnAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _apprCautAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _apprWarnAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _slowCautAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _slowWarnAlerts = new List<AlertItem>(4);
        private readonly List<AlertItem> _tempCaut = new List<AlertItem>(4);
        private readonly List<AlertItem> _tempWarn = new List<AlertItem>(4);
        private uint _heartbeatTick = 0;
        private bool _alertsDirty = false;

        private float _alertEvalTimer = 0f;
        private const float ALERT_EVAL_INTERVAL = 0.10f;
        private bool _forceImmediateAlertEval = true;

        // 瞬态事件横幅状态机与优先级队列
        private BannerDisplayState _bannerState = BannerDisplayState.Normal;
        private BannerEventItem _currentEvent;
        private readonly List<BannerEventItem> _bannerQueue = new List<BannerEventItem>(8);
        private float _bannerTimer = 0f;
        private float _bannerDuration = 1.25f;
        private string _sepTitleTemplate = null;
        private string _engTitleTemplate = null;
        private bool _customSepExplicit = false;
        private bool _customEngExplicit = false;

        private readonly Dictionary<MasterWarningWidget.BannerEventType, float> _eventLastTriggerTimes = new Dictionary<MasterWarningWidget.BannerEventType, float>(16);
        private const float EVENT_COOLDOWN = 1.6f;

        public float WarningThreshold { get; set; } = 0.05f;
        public float CautionThreshold { get; set; } = 0.15f;
        private float _customWarnThresh = -1f;
        private float _customCautThresh = -1f;
        private float _lowFuelPersistentTimer = 0f;
        private const float LOW_FUEL_PERSISTENCE = 0.35f;

        private double _cachedAtmoCutoff = 70000.0;
        private double _cachedEffectivePe = 0.0;
        private double _cachedEffectiveAp = 0.0;

        // 巡航工况
        private readonly CachedDouble _lastRenderedAp = new CachedDouble(-9999999.0, 500.0);
        private readonly CachedDouble _lastRenderedPe = new CachedDouble(-9999999.0, 500.0);
        private readonly CachedDouble _lastRenderedDv = new CachedDouble(-999.0, 1.0);
        private readonly CachedFloat _lastRenderedMach = new CachedFloat(-1f, 0.05f);
        private readonly CachedFloat _lastRenderedVsi = new CachedFloat(-9999f, 5f);
        private string _cachedApSub;
        private string _cachedPeSub;
        private string _cachedDvSub;
        private string _cachedMachSub;
        private string _cachedVsiSub;
        private string _dataNominalTitle = string.Empty;
        private string _dataNominalSub = string.Empty;
        private string _dataNominalIcon = string.Empty;
        private EventColorRole _dataNominalRole = EventColorRole.AccentSecondary;
        private bool _isQuiescentFlightState = false;

        // 轮播与闪烁
        private float _clock = 0f;
        private bool _blink1Hz = false;
        private bool _blink2Hz = false;
        private int _lastCautCount = -1;
        private int _lastWarnCount = -1;

        // 遥测数据死区量化格式缓存
        private string _cachedAglStr = "0m";
        private int _fmtAglMeters = -9999;
        private string _cachedVsiStr = "0m/s";
        private int _fmtVsiVal = -9999;
        private string _cachedGForceStr = "1.0G";
        private double _fmtGForceVal = -999.0;
        private string _cachedTempStr = "0°C";
        private int _fmtTempInt = -9999;
        private string _cachedTtiStr = "0s";
        private int _fmtTtiSec = -9999;
        private string _cachedClosureRateStr = "0.0m/s";
        private double _fmtClosureRateVal = -999.0;
        private string _cachedVMassStr = "0.0t";
        private double _fmtVMassVal = -999.0;
        private string _cachedRadStr = "0.00r/h";
        private double _fmtRadVal = -999.0;
        private string _cachedPressStr = "0.00a";
        private double _fmtPressVal = -999.0;
        private string _cachedDbsSecStr = "0s";
        private int _fmtDbsSec = -9999;
        private string _cachedDbsMinStr = "0m";
        private int _fmtDbsMin = -9999;

        // I18n 缓存字符串
        private string _cachedStrCaution;
        private string _cachedStrWarning;
        private string _cachedStrNorm;
        private string _cachedStrArmed;
        private string _cachedStrNodeArmed;
        private string _cachedStrReady;
        private string _cachedStrEscape;
        private string _cachedStrBallistic;
        private string _cachedStrDeorbit;
        private string _cachedStrOrbitCruise;
        private string _cachedStrAscent;
        private string _cachedStrApproach;
        private string _cachedStrSuborbital;
        private string _cachedNominalReadySub;
        private string _cachedNominalSuborbSub;

        // 告警单元主文案与副标高速缓存 (Zero-GC Alert String Cache)
        private string _cachedAlertMinFuel;
        private string _cachedAlertLowFuel;
        private string _cachedAlertPullUp;
        private string _cachedAlertSinkRate;
        private string _cachedAlertExcessG;
        private string _cachedAlertHighG;
        private string _cachedAlertStall;
        private string _cachedAlertStallWarn;
        private string _cachedAlertUllage;
        private string _cachedAlertUnstable;
        private string _cachedAlertEcCrit;
        private string _cachedAlertLowEc;
        private string _cachedAlertO2Crit;
        private string _cachedAlertLowO2;
        private string _cachedAlertOverheat;
        private string _cachedAlertHighTemp;
        private string _cachedAlertNoComm;
        private string _cachedAlertCommOff;
        private string _cachedAlertLastIgn;
        private string _cachedAlert1Left;
        private string _cachedAlertNoIgn;
        private string _cachedAlert0Left;
        private string _cachedAlertWeakSig;
        private string _cachedAlertEngFail;
        private string _cachedAlertFail;
        private string _cachedAlertImpact;
        private string _cachedAlertTerrClose;
        private string _cachedAlertGearUp;
        private string _cachedAlertRateHigh;
        private string _cachedAlertAoaLimit;
        private string _cachedAlertGLimit;
        private string _cachedAlertAvionOver;
        private string _cachedAlertInterplanLck;
        private string _cachedAlertDeep;
        private string _cachedAlertAvionLock;
        private string _cachedAlertLost;
        private string _cachedAlertAvionDead;
        private string _cachedAlertLoopOverheat;
        private string _cachedAlertLoopTempHi;
        private string _cachedAlertBattDrain;
        private string _cachedAlertDischarging;
        private string _cachedAlertSolarStorm;
        private string _cachedAlertCme;
        private string _cachedAlertRadDanger;
        private string _cachedAlertHighRad;
        private string _cachedAlertCo2Crit;
        private string _cachedAlertHighCo2;
        private string _cachedAlertCabinPress;

        public MasterWarningLogic()
        {
            UpdateI18n();
            _currentEvent = BuildEventItem(MasterWarningWidget.BannerEventType.Separation);
        }

        public void UpdateI18n()
        {
            InitCachedI18n();
            if (!_customSepExplicit) _sepTitleTemplate = I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离");
            if (!_customEngExplicit) _engTitleTemplate = I18n.Tr("WIDGET_ALERT_ENGINE_START", "引擎启动");
        }

        public void SetModulesCount(int count)
        {
            _modulesCount = Mathf.Clamp(count, 2, 3);
        }

        public void ConfigureSwitchInterval(float iv)
        {
            if (iv > 0.5f) _switchInterval = iv;
        }

        public void ConfigureSepTitleTemplate(string sep)
        {
            if (!string.IsNullOrEmpty(sep))
            {
                _sepTitleTemplate = sep;
                _customSepExplicit = true;
            }
        }

        public void ConfigureEngTitleTemplate(string eng)
        {
            if (!string.IsNullOrEmpty(eng))
            {
                _engTitleTemplate = eng;
                _customEngExplicit = true;
            }
        }

        public void ConfigureBannerDuration(float dt)
        {
            if (dt > 0.4f) _bannerDuration = dt;
        }

        public void ConfigureCustomThresholds(float fw, float fc)
        {
            if (fw > 0.01f && fw <= 35f) _customWarnThresh = fw / 100f;
            if (fc > 0.01f && fc <= 50f) _customCautThresh = fc / 100f;
        }

        public void AcknowledgeCaution()
        {
            _cautAcknowledged = true;
        }

        public void AcknowledgeWarning()
        {
            _warnAcknowledged = true;
        }

        public void TriggerBanner(FlightTransientEventType eventType, bool immediateHolding = false)
        {
            if (eventType == FlightTransientEventType.None) return;
            TriggerBanner((MasterWarningWidget.BannerEventType)eventType, immediateHolding);
        }

        public void TriggerBanner(MasterWarningWidget.BannerEventType eventType, bool immediateHolding = false)
        {
            float now = Time.unscaledTime;
            if (_eventLastTriggerTimes.TryGetValue(eventType, out float lastTime))
            {
                if (now - lastTime < EVENT_COOLDOWN && !immediateHolding)
                {
                    return;
                }
            }
            _eventLastTriggerTimes[eventType] = now;

            BannerEventItem newItem = BuildEventItem(eventType);

            if (immediateHolding)
            {
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergedHolding;
                _bannerTimer = 0f;
                _bannerQueue.Clear();
                return;
            }

            if (_bannerState == BannerDisplayState.Normal)
            {
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergingIn;
                _bannerTimer = 0f;
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergedHolding;
                _bannerTimer = 0f;
            }
            else
            {
                if (_bannerState == BannerDisplayState.MergingIn && newItem.Priority > _currentEvent.Priority)
                {
                    EnqueueByPriority(_currentEvent);
                    _currentEvent = newItem;
                    _bannerTimer = 0f;
                }
                else
                {
                    EnqueueByPriority(newItem);
                }
            }
        }

        private void EnqueueByPriority(BannerEventItem item)
        {
            int insertIndex = _bannerQueue.Count;
            for (int i = 0; i < _bannerQueue.Count; i++)
            {
                if (item.Priority > _bannerQueue[i].Priority)
                {
                    insertIndex = i;
                    break;
                }
            }
            _bannerQueue.Insert(insertIndex, item);
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (_cautAlerts.Count > 0) _cautAlerts.Clear();
                if (_warnAlerts.Count > 0) _warnAlerts.Clear();
                CurrentState = default;
                return;
            }

            float dt = deltaTime;
            _heartbeatTick++;

            float now = Time.unscaledTime;
            int frame = Time.frameCount;
            var snapshot = FlightTransientEventDetector.Instance.DetectEvents(telemetry, now, frame);
            if (snapshot.TriggeredEvent != FlightTransientEventType.None)
            {
                TriggerBanner(snapshot.TriggeredEvent);
            }

            _cachedAtmoCutoff = snapshot.AtmosphereCutoff;
            _cachedEffectivePe = snapshot.EffectivePeriapsis;
            _cachedEffectiveAp = snapshot.EffectiveApoapsis;

            _alertEvalTimer += dt;
            bool forceAll = _forceImmediateAlertEval || snapshot.TriggeredEvent != FlightTransientEventType.None;
            if (_alertEvalTimer >= ALERT_EVAL_INTERVAL || forceAll)
            {
                _alertEvalTimer = 0f;
                _forceImmediateAlertEval = false;
                EvaluateTelemetryAlertsStaggered(telemetry, dt, forceAll);
            }

            if ((_heartbeatTick % 2 == 0) || forceAll || string.IsNullOrEmpty(_dataNominalTitle))
            {
                ComputeNominalFlightPhaseData(telemetry);
            }

            SyncCurrentState();
        }

        public void AdvanceFrame(float dt)
        {
            bool hasAlerts = _cautAlerts.Count > 0 || _warnAlerts.Count > 0;
            if (hasAlerts)
            {
                _clock += dt;
                _blink1Hz = ((int)(_clock * 2f) % 2) == 0;
                _blink2Hz = ((int)(_clock * 4f) % 2) == 0;

                _rotateTimer += dt;
                if (_rotateTimer >= _switchInterval)
                {
                    _rotateTimer = 0f;
                    if (_cautAlerts.Count > 1) _cautIndex = (_cautIndex + 1) % _cautAlerts.Count;
                    if (_warnAlerts.Count > 1) _warnIndex = (_warnIndex + 1) % _warnAlerts.Count;
                }
            }

            AdvanceBannerAnimation(dt);
            SyncCurrentState();
        }

        private void AdvanceBannerAnimation(float dt)
        {
            if (_bannerState == BannerDisplayState.MergingIn)
            {
                _bannerTimer += dt;
                float progress = Mathf.Clamp01(_bannerTimer / 0.10f);
                if (progress >= 1.0f)
                {
                    _bannerState = BannerDisplayState.MergedHolding;
                    _bannerTimer = 0f;
                }
            }
            else if (_bannerState == BannerDisplayState.MergedHolding)
            {
                _bannerTimer += dt;
                float targetDuration = (_bannerQueue.Count > 0) ? Mathf.Min(_currentEvent.Duration, 0.95f) : _currentEvent.Duration;
                if (_bannerTimer >= targetDuration)
                {
                    if (_bannerQueue.Count > 0)
                    {
                        _currentEvent = _bannerQueue[0];
                        _bannerQueue.RemoveAt(0);
                        _bannerState = BannerDisplayState.SwitchingEvent;
                        _bannerTimer = 0f;
                    }
                    else
                    {
                        _bannerState = BannerDisplayState.FlashingBack;
                        _bannerTimer = 0f;
                    }
                }
            }
            else if (_bannerState == BannerDisplayState.SwitchingEvent)
            {
                _bannerTimer += dt;
                if (_bannerTimer >= 0.08f)
                {
                    _bannerState = BannerDisplayState.MergedHolding;
                    _bannerTimer = 0f;
                }
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                _bannerTimer += dt;
                float duration = (_modulesCount == 2) ? 0.14f : 0.12f;
                if (_bannerTimer >= duration)
                {
                    _bannerState = BannerDisplayState.Normal;
                    _bannerTimer = 0f;
                    _currentEvent = default;
                }
            }
        }

        private void SyncCurrentState()
        {
            UpdateAlertIndicesAndAcknowledge();

            MasterWarningState s = new MasterWarningState();
            s.HasVessel = true;
            s.ModulesCount = _modulesCount;

            // Caution
            s.HasCautAlert = _cautAlerts.Count > 0;
            if (s.HasCautAlert)
            {
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
                AlertItem item = _cautAlerts[_cautIndex];
                s.CautTitle = item.MainTitle;
                s.CautSub = _cautAlerts.Count > 1 ? $"{_cautIndex + 1}/{_cautAlerts.Count}" : item.TelemetryAffix;
                s.CautIcon = "▲";
                s.CautBlink = _cautAcknowledged || _blink1Hz;
                s.CautCount = _cautAlerts.Count;
                s.CautAcknowledged = _cautAcknowledged;
            }

            // Warning
            s.HasWarnAlert = _warnAlerts.Count > 0;
            if (s.HasWarnAlert)
            {
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
                AlertItem item = _warnAlerts[_warnIndex];
                s.WarnTitle = item.MainTitle;
                s.WarnSub = _warnAlerts.Count > 1 ? $"{_warnIndex + 1}/{_warnAlerts.Count}" : item.TelemetryAffix;
                s.WarnIcon = "▲";
                s.WarnBlink = _warnAcknowledged || _blink2Hz;
                s.WarnCount = _warnAlerts.Count;
                s.WarnAcknowledged = _warnAcknowledged;
            }

            // Banner
            s.BannerState = _bannerState;
            s.BannerTimer = _bannerTimer;
            s.BannerDuration = _bannerDuration;
            if (_bannerState == BannerDisplayState.MergingIn)
            {
                float progress = Mathf.Clamp01(_bannerTimer / 0.10f);
                s.BannerSlideX = Mathf.Lerp(46f, 0f, progress);
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                float progress = Mathf.Clamp01(_bannerTimer / 0.14f);
                s.BannerSlideX = Mathf.Lerp(0f, 46f, progress);
            }
            else
            {
                s.BannerSlideX = 0f;
            }
            s.BannerPulseAlpha = 0.82f + 0.18f * Mathf.Sin(_bannerTimer * 12f);
            if (_bannerState != BannerDisplayState.Normal)
            {
                s.EventTitle = _currentEvent.Title;
                s.EventSub = _currentEvent.Sub;
                s.EventLeftIcon = _currentEvent.LeftIcon;
                s.EventRightIcon = _currentEvent.RightIcon;
                s.EventColor = _currentEvent.ColorRole;
            }
            else
            {
                s.EventTitle = null;
                s.EventSub = null;
                s.EventLeftIcon = null;
                s.EventRightIcon = null;
                s.EventColor = default;
            }

            // Nominal
            s.NominalTitle = _dataNominalTitle;
            s.NominalSub = _dataNominalSub;
            s.NominalIcon = _dataNominalIcon;
            s.NominalRole = _dataNominalRole;

            CurrentState = s;
        }

        private void UpdateAlertIndicesAndAcknowledge()
        {
            if (_cautAlerts.Count > _lastCautCount) _cautAcknowledged = false;
            _lastCautCount = _cautAlerts.Count;
            if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;

            if (_warnAlerts.Count > _lastWarnCount) _warnAcknowledged = false;
            _lastWarnCount = _warnAlerts.Count;
            if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
        }

        private string FormatAglM(int agl)
        {
            if (Math.Abs(agl - _fmtAglMeters) >= 5)
            {
                _fmtAglMeters = agl;
                _cachedAglStr = CacheManager.FastInt(agl) + "m";
            }
            return _cachedAglStr;
        }

        private string FormatVsiMps(int vsi)
        {
            if (Math.Abs(vsi - _fmtVsiVal) >= 2)
            {
                _fmtVsiVal = vsi;
                _cachedVsiStr = CacheManager.FastInt(vsi) + "m/s";
            }
            return _cachedVsiStr;
        }

        private string FormatGForce(double g)
        {
            if (Math.Abs(g - _fmtGForceVal) >= 0.1)
            {
                _fmtGForceVal = g;
                _cachedGForceStr = $"{g:F1}G";
            }
            return _cachedGForceStr;
        }

        private string FormatTemp(int temp)
        {
            if (Math.Abs(temp - _fmtTempInt) >= 2)
            {
                _fmtTempInt = temp;
                _cachedTempStr = CacheManager.FastInt(temp) + "°C";
            }
            return _cachedTempStr;
        }

        private string FormatTtiSec(int tti)
        {
            if (Math.Abs(tti - _fmtTtiSec) >= 1)
            {
                _fmtTtiSec = tti;
                _cachedTtiStr = CacheManager.FastInt(tti) + "s";
            }
            return _cachedTtiStr;
        }

        private string FormatClosureRate(double rate)
        {
            if (Math.Abs(rate - _fmtClosureRateVal) >= 0.1)
            {
                _fmtClosureRateVal = rate;
                _cachedClosureRateStr = $"{rate:F1}m/s";
            }
            return _cachedClosureRateStr;
        }

        private string FormatVMass(double mass)
        {
            if (Math.Abs(mass - _fmtVMassVal) >= 0.2)
            {
                _fmtVMassVal = mass;
                _cachedVMassStr = $"{mass:F1}t";
            }
            return _cachedVMassStr;
        }

        private string FormatRadiation(double rad)
        {
            if (Math.Abs(rad - _fmtRadVal) >= 0.02)
            {
                _fmtRadVal = rad;
                _cachedRadStr = $"{rad:F2}r/h";
            }
            return _cachedRadStr;
        }

        private string FormatPressure(double press)
        {
            if (Math.Abs(press - _fmtPressVal) >= 0.02)
            {
                _fmtPressVal = press;
                _cachedPressStr = $"{press:F2}a";
            }
            return _cachedPressStr;
        }

        private string FormatDbsSec(int sec)
        {
            if (Math.Abs(sec - _fmtDbsSec) >= 2)
            {
                _fmtDbsSec = sec;
                _cachedDbsSecStr = CacheManager.FastInt(sec) + "s";
            }
            return _cachedDbsSecStr;
        }

        private string FormatDbsMin(int min)
        {
            if (Math.Abs(min - _fmtDbsMin) >= 1)
            {
                _fmtDbsMin = min;
                _cachedDbsMinStr = CacheManager.FastInt(min) + "m";
            }
            return _cachedDbsMinStr;
        }

        // 轮播计时器
        private float _rotateTimer = 0f;
        private float _switchInterval = 1.8f;
        private int _cautIndex = 0;
        private int _warnIndex = 0;

        // 消警状态 (Acknowledge)
        private bool _cautAcknowledged = false;
        private bool _warnAcknowledged = false;


        private static string FormatKm(double meters)
        {
            if (double.IsNaN(meters)) return "--";
            double km = meters / 1000.0;
            if (Math.Abs(km) >= 1000.0) return $"{km / 1000.0:F1}M";
            return $"{km:F0}k";
        }

        private void InitCachedI18n()
        {
            _cachedStrCaution = I18n.Tr("WIDGET_ALERT_CAUTION", "注意");
            _cachedStrWarning = I18n.Tr("WIDGET_ALERT_WARNING", "危急");
            _cachedStrNorm = I18n.Tr("WIDGET_ALERT_NORM", "正常");
            _cachedStrArmed = I18n.Tr("WIDGET_ALERT_ARMED", "待命");
            _cachedStrNodeArmed = I18n.Tr("WIDGET_STATUS_NODE_ARMED", "节点待命");
            _cachedStrReady = I18n.Tr("WIDGET_STATUS_READY", "发射就绪");
            _cachedStrEscape = I18n.Tr("WIDGET_STATUS_ESCAPE", "深空逃逸");
            _cachedStrBallistic = I18n.Tr("WIDGET_STATUS_BALLISTIC", "弹道再入撞击");
            _cachedStrDeorbit = I18n.Tr("WIDGET_STATUS_DEORBIT", "离轨再入走廊");
            _cachedStrOrbitCruise = I18n.Tr("WIDGET_STATUS_ORBIT_CRUISE", "轨道巡航");
            _cachedStrAscent = I18n.Tr("WIDGET_STATUS_ASCENT", "大气爬升");
            _cachedStrApproach = I18n.Tr("WIDGET_STATUS_APPROACH", "降落进近");
            _cachedStrSuborbital = I18n.Tr("WIDGET_STATUS_SUBORBITAL", "亚轨道飞行");
            _cachedNominalReadySub = I18n.Tr("WIDGET_ALERT_READY", "就绪");
            _cachedNominalSuborbSub = I18n.Tr("WIDGET_STATUS_SUBORBITAL_SUB", "亚轨道");

            _cachedAlertMinFuel = I18n.Tr("WIDGET_ALERT_MIN_FUEL", "燃料危急!");
            _cachedAlertLowFuel = I18n.Tr("WIDGET_ALERT_LOW_FUEL", "低燃料");
            _cachedAlertPullUp = I18n.Tr("WIDGET_ALERT_PULL_UP", "拉起飞船!");
            _cachedAlertSinkRate = I18n.Tr("WIDGET_ALERT_SINK_RATE", "下沉速率大");
            _cachedAlertExcessG = I18n.Tr("WIDGET_ALERT_HIGH_G", "过载超限!");
            _cachedAlertHighG = I18n.Tr("WIDGET_ALERT_HIGH_G_CAUT", "高过载");
            _cachedAlertStall = I18n.Tr("WIDGET_ALERT_STALL", "气动失速!");
            _cachedAlertStallWarn = I18n.Tr("WIDGET_ALERT_STALL_WARN", "失速预警");
            _cachedAlertUllage = I18n.Tr("WIDGET_ALERT_ULLAGE", "沉底不稳");
            _cachedAlertUnstable = I18n.Tr("WIDGET_ALERT_UNSTABLE", "不稳定");
            _cachedAlertEcCrit = I18n.Tr("WIDGET_ALERT_EC_CRIT", "电力告急!");
            _cachedAlertLowEc = I18n.Tr("WIDGET_ALERT_LOW_EC", "电力不足");
            _cachedAlertO2Crit = I18n.Tr("WIDGET_ALERT_O2_CRIT", "氧气告急!");
            _cachedAlertLowO2 = I18n.Tr("WIDGET_ALERT_LOW_O2", "氧气不足");
            _cachedAlertOverheat = I18n.Tr("WIDGET_ALERT_CRIT_TEMP", "极限超温!");
            _cachedAlertHighTemp = I18n.Tr("WIDGET_ALERT_OVERHEAT", "超温注意");
            _cachedAlertNoComm = I18n.Tr("WIDGET_SIG_CTRL_NO_COMM", "无通信");
            _cachedAlertCommOff = I18n.Tr("WIDGET_ALERT_AFFIX_OFF", "断开");
            _cachedAlertLastIgn = I18n.Tr("WIDGET_ALERT_LAST_IGN", "最后点火");
            _cachedAlert1Left = I18n.Tr("WIDGET_ALERT_1_LEFT", "剩 1 次");
            _cachedAlertNoIgn = I18n.Tr("WIDGET_ALERT_NO_IGN", "无点火次数");
            _cachedAlert0Left = I18n.Tr("WIDGET_ALERT_0_LEFT", "剩 0 次");
            _cachedAlertWeakSig = I18n.Tr("WIDGET_ALERT_WEAK_SIGNAL", "信号微弱");
            _cachedAlertEngFail = I18n.Tr("WIDGET_ALERT_ENGINE_FAIL", "发动机故障失效");
            _cachedAlertFail = I18n.Tr("WIDGET_ALERT_FAIL_AFFIX", "故障");
            _cachedAlertImpact = I18n.Tr("WIDGET_ALERT_TERRAIN_IMPACT", "地表撞击告警");
            _cachedAlertTerrClose = I18n.Tr("WIDGET_ALERT_TERR_CLOSE", "地形接近");
            _cachedAlertGearUp = I18n.Tr("WIDGET_ALERT_GEAR_UP", "收起落架!");
            _cachedAlertRateHigh = I18n.Tr("WIDGET_ALERT_RATE_HIGH", "进近过速");
            _cachedAlertAoaLimit = I18n.Tr("WIDGET_ALERT_AOA_LIMIT", "攻角超限");
            _cachedAlertGLimit = I18n.Tr("WIDGET_ALERT_G_LIMIT", "过载限制");
            _cachedAlertAvionOver = I18n.Tr("WIDGET_ALERT_AVION_OVER", "航电超负荷!");
            _cachedAlertInterplanLck = I18n.Tr("WIDGET_ALERT_INTERPLAN_LCK", "深空锁定");
            _cachedAlertDeep = I18n.Tr("WIDGET_ALERT_DEEP", "深空");
            _cachedAlertAvionLock = I18n.Tr("WIDGET_ALERT_AVIONICS_LOCK", "航电失控锁定");
            _cachedAlertLost = I18n.Tr("WIDGET_ALERT_LOST", "丢失");
            _cachedAlertAvionDead = I18n.Tr("WIDGET_ALERT_AVION_DEAD", "航电失效");
            _cachedAlertLoopOverheat = I18n.Tr("WIDGET_ALERT_THERMAL_OVERHEAT", "热回路超温");
            _cachedAlertLoopTempHi = I18n.Tr("WIDGET_ALERT_LOOP_TEMP_HI", "回路偏热");
            _cachedAlertBattDrain = I18n.Tr("WIDGET_ALERT_BATT_DRAIN", "电池快速放电!");
            _cachedAlertDischarging = I18n.Tr("WIDGET_ALERT_DISCHARGING", "持续放电");
            _cachedAlertSolarStorm = I18n.Tr("WIDGET_ALERT_SOLAR_STORM", "太阳风暴冲击");
            _cachedAlertCme = I18n.Tr("WIDGET_ALERT_CME", "日冕抛射");
            _cachedAlertRadDanger = I18n.Tr("WIDGET_ALERT_RAD_DANGER", "辐射危险!");
            _cachedAlertHighRad = I18n.Tr("WIDGET_ALERT_HIGH_RAD", "高辐射");
            _cachedAlertCo2Crit = I18n.Tr("WIDGET_ALERT_CO2_CRIT", "CO2 极高!");
            _cachedAlertHighCo2 = I18n.Tr("WIDGET_ALERT_HIGH_CO2", "CO2 偏高");
            _cachedAlertCabinPress = I18n.Tr("WIDGET_ALERT_CABIN_PRESS", "座舱失压!");
        }



        private BannerEventItem BuildEventItem(MasterWarningWidget.BannerEventType type)
        {
            switch (type)
            {
                case MasterWarningWidget.BannerEventType.Separation:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.Separation,
                        !string.IsNullOrEmpty(_sepTitleTemplate) ? _sepTitleTemplate : I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离"),
                        "STG",
                        "◀",
                        "▶",
                        _bannerDuration,
                        30,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.EngineStart:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.EngineStart,
                        !string.IsNullOrEmpty(_engTitleTemplate) ? _engTitleTemplate : I18n.Tr("WIDGET_ALERT_ENGINE_START", "引擎启动"),
                        "IGN",
                        "▲",
                        "▲",
                        _bannerDuration,
                        25,
                        EventColorRole.WarningColor
                    );

                case MasterWarningWidget.BannerEventType.MECO:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.MECO,
                        I18n.Tr("WIDGET_ALERT_MECO", "主发关机"),
                        "MECO",
                        "■",
                        "■",
                        _bannerDuration,
                        20,
                        EventColorRole.WarningColor
                    );

                case MasterWarningWidget.BannerEventType.ManeuverApproach:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.ManeuverApproach,
                        I18n.Tr("WIDGET_ALERT_MANEUVER_APPROACH", "接近机动节点"),
                        "T-60s",
                        "◆",
                        "◆",
                        Mathf.Max(1.6f, _bannerDuration),
                        22,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.ManeuverBurn:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.ManeuverBurn,
                        I18n.Tr("WIDGET_ALERT_MANEUVER_BURN", "机动点火执行"),
                        I18n.Tr("WIDGET_ALERT_SUB_BURN", "点火"),
                        "▶",
                        "▶",
                        Mathf.Max(1.5f, _bannerDuration),
                        24,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.OrbitAchieved:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.OrbitAchieved,
                        I18n.Tr("WIDGET_ALERT_ORBIT_ACHIEVED", "入轨圆化完成"),
                        "ORBIT",
                        "★",
                        "★",
                        Mathf.Max(1.8f, _bannerDuration),
                        28,
                        EventColorRole.Success
                    );

                case MasterWarningWidget.BannerEventType.AtmosphereEntry:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.AtmosphereEntry,
                        I18n.Tr("WIDGET_ALERT_ATMOSPHERE_ENTRY", "进入大气层"),
                        I18n.Tr("WIDGET_ALERT_SUB_ENTRY", "再入"),
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        26,
                        EventColorRole.WarningColor
                    );

                case MasterWarningWidget.BannerEventType.ApoapsisPass:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.ApoapsisPass,
                        I18n.Tr("WIDGET_ALERT_AP_PASS", "通过远拱点"),
                        "AP",
                        "▲",
                        "▲",
                        Mathf.Max(1.2f, _bannerDuration),
                        14,
                        EventColorRole.AccentSecondary
                    );

                case MasterWarningWidget.BannerEventType.PeriapsisPass:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.PeriapsisPass,
                        I18n.Tr("WIDGET_ALERT_PE_PASS", "通过近拱点"),
                        "PE",
                        "▼",
                        "▼",
                        Mathf.Max(1.2f, _bannerDuration),
                        14,
                        EventColorRole.AccentSecondary
                    );

                case MasterWarningWidget.BannerEventType.DockingMode:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.DockingMode,
                        I18n.Tr("WIDGET_ALERT_DOCKING_MODE", "进入对接模式"),
                        "DOCK",
                        "⊞",
                        "⊞",
                        Mathf.Max(1.5f, _bannerDuration),
                        16,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.Deorbit:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.Deorbit,
                        I18n.Tr("WIDGET_ALERT_DEORBIT", "飞船离轨"),
                        I18n.Tr("WIDGET_ALERT_SUB_DEORB", "离轨"),
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        27,
                        EventColorRole.WarningColor
                    );

                case MasterWarningWidget.BannerEventType.Escape:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.Escape,
                        I18n.Tr("WIDGET_ALERT_ESCAPE", "逃逸轨道建立"),
                        I18n.Tr("WIDGET_ALERT_SUB_ESC", "逃逸"),
                        "▲",
                        "▲",
                        Mathf.Max(1.8f, _bannerDuration),
                        28,
                        EventColorRole.Success
                    );

                case MasterWarningWidget.BannerEventType.SoiTransition:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.SoiTransition,
                        I18n.Tr("WIDGET_ALERT_SOI_TRANSITION", "进入引力范围"),
                        "SOI",
                        "◆",
                        "◆",
                        Mathf.Max(1.8f, _bannerDuration),
                        29,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.SuicideBurn:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.SuicideBurn,
                        I18n.Tr("WIDGET_ALERT_SUICIDE_BURN", "动力减速着陆"),
                        I18n.Tr("WIDGET_ALERT_SUB_BURN", "点火"),
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        29,
                        EventColorRole.WarningColor
                    );

                case MasterWarningWidget.BannerEventType.Blackout:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.Blackout,
                        I18n.Tr("WIDGET_ALERT_BLACKOUT", "再入黑障"),
                        I18n.Tr("WIDGET_ALERT_SUB_BLKOUT", "黑障"),
                        "⚡",
                        "⚡",
                        Mathf.Max(2.0f, _bannerDuration),
                        26,
                        EventColorRole.DangerColor
                    );

                case MasterWarningWidget.BannerEventType.Touchdown:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.Touchdown,
                        I18n.Tr("WIDGET_ALERT_TOUCHDOWN", "着陆接地成功"),
                        I18n.Tr("WIDGET_ALERT_SUB_TOUCH", "接地"),
                        "⚓",
                        "⚓",
                        Mathf.Max(2.0f, _bannerDuration),
                        30,
                        EventColorRole.Success
                    );

                case MasterWarningWidget.BannerEventType.MaxQ:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.MaxQ,
                        I18n.Tr("WIDGET_ALERT_MAX_Q", "突破最大动压"),
                        I18n.Tr("WIDGET_ALERT_SUB_MAX_Q", "极值动压"),
                        "⚡",
                        "⚡",
                        Mathf.Max(1.8f, _bannerDuration),
                        23,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.V1Rotate:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.V1Rotate,
                        I18n.Tr("WIDGET_ALERT_V1_ROTATE", "起飞决断速度"),
                        I18n.Tr("WIDGET_ALERT_SUB_ROTATE", "抬轮"),
                        "▲",
                        "▲",
                        Mathf.Max(1.5f, _bannerDuration),
                        21,
                        EventColorRole.AccentPrimary
                    );

                case MasterWarningWidget.BannerEventType.SolarStorm:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.SolarStorm,
                        I18n.Tr("WIDGET_ALERT_SOLAR_STORM", "太阳风暴冲击"),
                        I18n.Tr("WIDGET_ALERT_SUB_CME", "耀斑"),
                        "☢",
                        "☢",
                        Mathf.Max(2.2f, _bannerDuration),
                        32,
                        EventColorRole.DangerColor
                    );

                case MasterWarningWidget.BannerEventType.AvionicsLock:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.AvionicsLock,
                        I18n.Tr("WIDGET_ALERT_AVIONICS_LOCK", "航电失控锁定"),
                        I18n.Tr("WIDGET_ALERT_SUB_LOCK", "锁定"),
                        "⚠",
                        "⚠",
                        Mathf.Max(2.5f, _bannerDuration),
                        35,
                        EventColorRole.DangerColor
                    );

                case MasterWarningWidget.BannerEventType.TerrainImpact:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.TerrainImpact,
                        I18n.Tr("WIDGET_ALERT_TERRAIN_IMPACT", "地表撞击告警"),
                        I18n.Tr("WIDGET_ALERT_SUB_IMPACT", "撞击"),
                        "▼",
                        "▼",
                        Mathf.Max(2.0f, _bannerDuration),
                        33,
                        EventColorRole.DangerColor
                    );

                case MasterWarningWidget.BannerEventType.DockingCapture:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.DockingCapture,
                        I18n.Tr("WIDGET_ALERT_DOCKING_CAPTURE", "对接锁扣捕获"),
                        I18n.Tr("WIDGET_ALERT_SUB_LATCH", "锁合"),
                        "⚓",
                        "⚓",
                        Mathf.Max(2.0f, _bannerDuration),
                        28,
                        EventColorRole.Success
                    );

                case MasterWarningWidget.BannerEventType.EngineFailure:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.EngineFailure,
                        I18n.Tr("WIDGET_ALERT_ENGINE_FAIL", "发动机故障失效"),
                        I18n.Tr("WIDGET_ALERT_SUB_FAIL", "失效"),
                        "✕",
                        "✕",
                        Mathf.Max(2.5f, _bannerDuration),
                        34,
                        EventColorRole.DangerColor
                    );

                case MasterWarningWidget.BannerEventType.ThermalOverheat:
                    return new BannerEventItem(
                        MasterWarningWidget.BannerEventType.ThermalOverheat,
                        I18n.Tr("WIDGET_ALERT_THERMAL_OVERHEAT", "热回路超温"),
                        I18n.Tr("WIDGET_ALERT_SUB_SCRAM", "紧急停堆"),
                        "♨",
                        "♨",
                        Mathf.Max(2.0f, _bannerDuration),
                        31,
                        EventColorRole.DangerColor
                    );

                default:
                    return BuildEventItem(MasterWarningWidget.BannerEventType.Separation);
            }
        }



        private void EvaluateTelemetryAlertsStaggered(IFlightTelemetry telem, float dt, bool forceAll)
        {
            // ── Tier 1: 极度危急与生存安全 (10Hz 判定，每拍必测) ──
            _tempCaut.Clear();
            _tempWarn.Clear();
            EvaluateUrgentSafetyAlerts(telem, dt, _tempCaut, _tempWarn);
            if (UpdateAlertBucket(_urgentCautAlerts, _tempCaut)) _alertsDirty = true;
            if (UpdateAlertBucket(_urgentWarnAlerts, _tempWarn)) _alertsDirty = true;

            // 稳态静默工况智能降频 (Quiescent Steady-State Alert Throttling)：
            // 当飞船处于稳定巡航轨道或发射台静止待命且无发动机点火/告警时，外围子系统与进近探测降频至 2Hz/1Hz，节约高达 75% 的诊断探测开销
            bool isQuiescent = _isQuiescentFlightState && _cautAlerts.Count == 0 && _warnAlerts.Count == 0;

            // ── Tier 2: 飞船资源与子系统状态 (常规 5Hz / 稳态静默 2Hz) ──
            bool runTier2 = forceAll || (isQuiescent ? (_heartbeatTick % 5 == 0) : (_heartbeatTick % 2 == 0));
            if (runTier2)
            {
                _tempCaut.Clear();
                _tempWarn.Clear();
                EvaluateSubsystemAlerts(telem, _tempCaut, _tempWarn);
                if (UpdateAlertBucket(_subsysCautAlerts, _tempCaut)) _alertsDirty = true;
                if (UpdateAlertBucket(_subsysWarnAlerts, _tempWarn)) _alertsDirty = true;
            }

            // ── Tier 3: 进近、气动与姿轨导航安全 (常规 5Hz / 稳态静默 1Hz) ──
            bool runTier3 = forceAll || (isQuiescent ? (_heartbeatTick % 10 == 1) : (_heartbeatTick % 2 == 1));
            if (runTier3)
            {
                _tempCaut.Clear();
                _tempWarn.Clear();
                EvaluateApproachAlerts(telem, _tempCaut, _tempWarn);
                if (UpdateAlertBucket(_apprCautAlerts, _tempCaut)) _alertsDirty = true;
                if (UpdateAlertBucket(_apprWarnAlerts, _tempWarn)) _alertsDirty = true;
            }

            // ── Tier 4: 慢速外围环境与深度诊断探针 (常规 2Hz / 稳态静默 1Hz) ──
            bool runTier4 = forceAll || (isQuiescent ? (_heartbeatTick % 10 == 0) : (_heartbeatTick % 5 == 0));
            if (runTier4)
            {
                _tempCaut.Clear();
                _tempWarn.Clear();
                EvaluateSlowDiagnosticAlerts(telem, _tempCaut, _tempWarn);
                if (UpdateAlertBucket(_slowCautAlerts, _tempCaut)) _alertsDirty = true;
                if (UpdateAlertBucket(_slowWarnAlerts, _tempWarn)) _alertsDirty = true;
            }

            // 若任何子桶发生状态改变，原子重建合并列表并重置消警
            if (_alertsDirty)
            {
                RebuildMergedAlerts();
            }
        }

        private static bool AlertListsEqual(List<AlertItem> a, List<AlertItem> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }
            return true;
        }

        private static bool UpdateAlertBucket(List<AlertItem> target, List<AlertItem> temp)
        {
            if (AlertListsEqual(target, temp))
            {
                return false;
            }
            target.Clear();
            target.AddRange(temp);
            return true;
        }

        private void RebuildMergedAlerts()
        {
            _cautAlerts.Clear();
            _cautAlerts.AddRange(_urgentCautAlerts);
            _cautAlerts.AddRange(_subsysCautAlerts);
            _cautAlerts.AddRange(_apprCautAlerts);
            _cautAlerts.AddRange(_slowCautAlerts);

            _warnAlerts.Clear();
            _warnAlerts.AddRange(_urgentWarnAlerts);
            _warnAlerts.AddRange(_subsysWarnAlerts);
            _warnAlerts.AddRange(_apprWarnAlerts);
            _warnAlerts.AddRange(_slowWarnAlerts);

            _alertsDirty = false;
            UpdateAlertIndicesAndAcknowledge();
        }

        private void EvaluateUrgentSafetyAlerts(IFlightTelemetry telem, float dt, List<AlertItem> cautOut, List<AlertItem> warnOut)
        {
            // 1. 低油量持续防抖安全判定
            float prop = telem.StagePropellantFraction;
            bool engineArmed = telem.ActiveEngines > 0 || (telem.TotalStageEngines > 0 && telem.Throttle > 0.001f);

            float warnThresh = WarningThreshold;
            float cautThresh = CautionThreshold;
            if (_customWarnThresh > 0.001f) warnThresh = _customWarnThresh;
            if (_customCautThresh > 0.001f) cautThresh = _customCautThresh;

            if (prop >= 0.40f || !engineArmed)
            {
                _lowFuelPersistentTimer = 0f;
            }
            else if (prop >= 0f && prop <= cautThresh)
            {
                _lowFuelPersistentTimer += dt;
                if (_lowFuelPersistentTimer >= LOW_FUEL_PERSISTENCE)
                {
                    int propPct = Mathf.Clamp(Mathf.RoundToInt(prop * 100f), 0, 100);
                    string affix = CacheManager.FastPercent(propPct);
                    if (prop <= warnThresh) warnOut.Add(new AlertItem(_cachedAlertMinFuel, affix, true));
                    else cautOut.Add(new AlertItem(_cachedAlertLowFuel, affix, false));
                }
            }
            else
            {
                _lowFuelPersistentTimer = 0f;
            }

            // 2. 近地危险下沉与拉起
            if (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 2000.0)
            {
                bool severePullUp = (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 600.0 && telem.AltitudeAGL > 3.0) ||
                                    (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 150.0 && telem.AltitudeAGL > 3.0);
                if (severePullUp)
                {
                    int aglInt = Mathf.RoundToInt((float)telem.AltitudeAGL);
                    warnOut.Add(new AlertItem(_cachedAlertPullUp, FormatAglM(aglInt), true));
                }
                else if (telem.AltitudeAGL > 10.0)
                {
                    int vsiInt = Mathf.RoundToInt((float)telem.VerticalSpeed);
                    cautOut.Add(new AlertItem(_cachedAlertSinkRate, FormatVsiMps(vsiInt), false));
                }
            }

            // 3. 过载极限 (G-FORCE)
            double g = telem.GForce;
            if (g > 6.0)
            {
                string gStr = FormatGForce(g);
                if (g > 9.0) warnOut.Add(new AlertItem(_cachedAlertExcessG, gStr, true));
                else cautOut.Add(new AlertItem(_cachedAlertHighG, gStr, false));
            }

            // 4. 气动失速 (STALL / FAR)
            if (ExternalProbeRegistry.HasFar && telem.AtmosphericPressure > 0.001 && telem.DynamicPressure > 0.5)
            {
                double farStall = ExternalProbeRegistry.ResolveNumeric("FAR", "STALL");
                if (!double.IsNaN(farStall))
                {
                    int stallPct = Mathf.Clamp(Mathf.RoundToInt((float)(farStall * 100.0)), 0, 100);
                    string stallStr = CacheManager.FastPercent(stallPct);
                    if (farStall > 0.70) warnOut.Add(new AlertItem(_cachedAlertStall, stallStr, true));
                    else if (farStall > 0.30) cautOut.Add(new AlertItem(_cachedAlertStallWarn, stallStr, false));
                }
            }
        }

        private void EvaluateSubsystemAlerts(IFlightTelemetry telem, List<AlertItem> cautOut, List<AlertItem> warnOut)
        {
            bool engineArmed = telem.ActiveEngines > 0 || (telem.TotalStageEngines > 0 && telem.Throttle > 0.001f);

            // RealFuels 探针沉底状态
            if (ExternalProbeRegistry.HasRealFuels && engineArmed && telem.Throttle > 0.001f)
            {
                string rfUllage = ExternalProbeRegistry.ResolveString("RF", "ULLAGE", "");
                if (!string.IsNullOrEmpty(rfUllage) &&
                    (rfUllage.IndexOf("Unstable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rfUllage.IndexOf("Very", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    cautOut.Add(new AlertItem(_cachedAlertUllage, _cachedAlertUnstable, false));
                }
            }

            // 电气能量平衡 (ELECTRIC CHARGE)
            double ecPct = telem.EcPercent;
            if (ecPct >= 0.0)
            {
                int ecInt = Mathf.Clamp(Mathf.RoundToInt((float)ecPct), 0, 100);
                string ecStr = CacheManager.FastPercent(ecInt);
                if (ecPct <= 5.0) warnOut.Add(new AlertItem(_cachedAlertEcCrit, ecStr, true));
                else if (ecPct <= 20.0) cautOut.Add(new AlertItem(_cachedAlertLowEc, ecStr, false));
            }

            // 维生系统与氧气 (O2 / KERBALISM)
            if (telem.CrewCapacity > 0)
            {
                float o2 = telem.OxygenPercent;
                if (o2 >= 0f)
                {
                    int o2Int = Mathf.Clamp(Mathf.RoundToInt(o2), 0, 100);
                    string o2Str = CacheManager.FastPercent(o2Int);
                    if (o2 <= 5.0f) warnOut.Add(new AlertItem(_cachedAlertO2Crit, o2Str, true));
                    else if (o2 <= 20.0f) cautOut.Add(new AlertItem(_cachedAlertLowO2, o2Str, false));
                }
            }

            // 舱温与超温 (OVERHEAT)
            double cabinTemp = telem.CabinTemp;
            if (cabinTemp > 80.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                string tempStr = FormatTemp(tempInt);
                if (cabinTemp > 120.0) warnOut.Add(new AlertItem(_cachedAlertOverheat, tempStr, true));
                else cautOut.Add(new AlertItem(_cachedAlertHighTemp, tempStr, false));
            }

            // 通信网络断开 (NO COMM)
            if (!telem.IsConnected && (telem.CrewCount == 0 || telem.CrewCapacity == 0))
            {
                cautOut.Add(new AlertItem(_cachedAlertNoComm, _cachedAlertCommOff, false));
            }

            // RealFuels 剩余点火次数 (RF)
            if (ExternalProbeRegistry.HasRealFuels && engineArmed)
            {
                double ignitions = ExternalProbeRegistry.ResolveNumeric("RF", "IGNITIONS");
                if (ignitions == 1.0)
                {
                    cautOut.Add(new AlertItem(_cachedAlertLastIgn, _cachedAlert1Left, false));
                }
                else if (ignitions == 0.0 && telem.Throttle <= 0.001f)
                {
                    warnOut.Add(new AlertItem(_cachedAlertNoIgn, _cachedAlert0Left, true));
                }
            }

            // RealAntennas 链路裕度不足 (RA)
            if (ExternalProbeRegistry.HasRealAntennas && (telem.CrewCapacity == 0 || telem.CrewCount == 0))
            {
                double sig = ExternalProbeRegistry.ResolveNumeric("RA", "SIGNALSTRENGTH");
                if (!double.IsNaN(sig) && sig > 0.0001 && sig < 0.15)
                {
                    int sigPct = Mathf.Clamp(Mathf.RoundToInt((float)sig * 100f), 0, 100);
                    cautOut.Add(new AlertItem(_cachedAlertWeakSig, CacheManager.FastPercent(sigPct), false));
                }
            }
        }

        private void EvaluateApproachAlerts(IFlightTelemetry telem, List<AlertItem> cautOut, List<AlertItem> warnOut)
        {
            // 发动机故障 (TESTFLIGHT)
            if (ExternalProbeRegistry.HasTestFlight && telem.ActiveEngines > 0)
            {
                double tfFailed = ExternalProbeRegistry.ResolveNumeric("TF", "FAILED");
                if (tfFailed > 0.5)
                {
                    warnOut.Add(new AlertItem(_cachedAlertEngFail, _cachedAlertFail, true));
                }
            }

            // Trajectories 预测地形撞击 (TRAJ)
            if (ExternalProbeRegistry.HasTrajectories && telem.VerticalSpeed < -5.0 && telem.AltitudeAGL < 20000.0)
            {
                double tti = ExternalProbeRegistry.ResolveNumeric("TRAJ", "TIMETOIMPACT");
                if (!double.IsNaN(tti) && tti > 0.0 && tti <= 60.0)
                {
                    int ttiSec = Mathf.RoundToInt((float)tti);
                    string ttiStr = FormatTtiSec(ttiSec);
                    if (tti <= 30.0) warnOut.Add(new AlertItem(_cachedAlertImpact, ttiStr, true));
                    else cautOut.Add(new AlertItem(_cachedAlertTerrClose, ttiStr, false));
                }
            }

            // GPWS 进近未放起落架告警 (GPWS)
            if (ExternalProbeRegistry.HasGPWS && telem.VerticalSpeed < -2.0 && telem.AltitudeAGL > 5.0 && telem.AltitudeAGL < 250.0 && telem.FlightSituation != "LANDED")
            {
                double gearDown = ExternalProbeRegistry.ResolveNumeric("GPWS", "GEARDOWN");
                if (gearDown < 0.5)
                {
                    int aglInt = Mathf.RoundToInt((float)telem.AltitudeAGL);
                    warnOut.Add(new AlertItem(_cachedAlertGearUp, FormatAglM(aglInt), true));
                }
            }

            // DPAI 进近过速告警 (DPAI)
            if (ExternalProbeRegistry.HasDocking && (telem.IsDockingMode || telem.TargetDistance < 100.0))
            {
                double dockDist = ExternalProbeRegistry.ResolveNumeric("DOCK", "DISTANCE");
                if (!double.IsNaN(dockDist) && dockDist > 0.5 && dockDist < 50.0)
                {
                    double closureRate = ExternalProbeRegistry.ResolveNumeric("DOCK", "CLOSURERATE");
                    if (!double.IsNaN(closureRate) && closureRate > 2.0)
                    {
                        cautOut.Add(new AlertItem(_cachedAlertRateHigh, FormatClosureRate(closureRate), false));
                    }
                }
            }

            // AtmosphereAutopilot 限制器保护 (AA)
            if (ExternalProbeRegistry.HasAtmosphereAutopilot && telem.AtmosphericPressure > 0.001 && telem.DynamicPressure > 0.5)
            {
                double modAoA = ExternalProbeRegistry.ResolveNumeric("AA", "MODERATE_AOA");
                if (modAoA > 0.5)
                {
                    double maxAoA = ExternalProbeRegistry.ResolveNumeric("AA", "MAX_AOA");
                    double curAoA = ExternalProbeRegistry.ResolveNumeric("AA", "AOA");
                    if (maxAoA > 1.0 && Math.Abs(curAoA) >= maxAoA * 0.92)
                    {
                        cautOut.Add(new AlertItem(_cachedAlertAoaLimit, CacheManager.FastDegree(Mathf.RoundToInt((float)Math.Abs(curAoA))), false));
                    }
                }

                double modG = ExternalProbeRegistry.ResolveNumeric("AA", "MODERATE_G");
                if (modG > 0.5)
                {
                    double maxG = ExternalProbeRegistry.ResolveNumeric("AA", "MAX_G");
                    if (maxG > 1.0 && telem.GForce >= maxG * 0.90)
                    {
                        cautOut.Add(new AlertItem(_cachedAlertGLimit, FormatGForce(telem.GForce), false));
                    }
                }
            }
        }

        private void EvaluateSlowDiagnosticAlerts(IFlightTelemetry telem, List<AlertItem> cautOut, List<AlertItem> warnOut)
        {
            // RP-1 航电负荷
            if (ExternalProbeRegistry.HasRP1)
            {
                double lockLevel = ExternalProbeRegistry.ResolveNumeric("RP1", "LOCK_LEVEL");
                if (lockLevel == 0.0)
                {
                    double massMargin = ExternalProbeRegistry.ResolveNumeric("RP1", "MASS_MARGIN");
                    if (massMargin < -0.01)
                    {
                        double vMass = ExternalProbeRegistry.ResolveNumeric("RP1", "VESSEL_MASS");
                        warnOut.Add(new AlertItem(_cachedAlertAvionOver, FormatVMass(vMass), true));
                    }
                    else
                    {
                        double ipLock = ExternalProbeRegistry.ResolveNumeric("RP1", "INTERPLANETARY_LOCKED");
                        if (ipLock > 0.5) warnOut.Add(new AlertItem(_cachedAlertInterplanLck, _cachedAlertDeep, true));
                        else warnOut.Add(new AlertItem(_cachedAlertAvionLock, _cachedAlertLost, true));
                    }
                }
                double deadAvionics = ExternalProbeRegistry.ResolveNumeric("RP1", "DEAD_COUNT");
                if (deadAvionics > 0.5)
                {
                    cautOut.Add(new AlertItem(_cachedAlertAvionDead, CacheManager.FastInt(Mathf.RoundToInt((float)deadAvionics)), false));
                }
            }

            // SystemHeat 热回路过热
            if (ExternalProbeRegistry.HasSystemHeat)
            {
                double overheatRatio = ExternalProbeRegistry.ResolveNumeric("SH", "OVERHEATRATIO");
                if (!double.IsNaN(overheatRatio) && overheatRatio > 50.0)
                {
                    int ohInt = Mathf.Clamp(Mathf.RoundToInt((float)overheatRatio), 0, 100);
                    string ohStr = CacheManager.FastPercent(ohInt);
                    if (overheatRatio >= 100.0) warnOut.Add(new AlertItem(_cachedAlertLoopOverheat, ohStr, true));
                    else if (overheatRatio >= 85.0) cautOut.Add(new AlertItem(_cachedAlertLoopTempHi, ohStr, false));
                }
            }

            // DynamicBatteryStorage 快速放电 (仅在低电量或放电工况下关注)
            if (ExternalProbeRegistry.HasDynamicBatteryStorage && telem.EcPercent <= 35.0)
            {
                double isDepleting = ExternalProbeRegistry.ResolveNumeric("DBS", "ISDEPLETING");
                if (isDepleting > 0.5)
                {
                    double timeSec = ExternalProbeRegistry.ResolveNumeric("DBS", "DEPLETIONSECONDS");
                    if (!double.IsNaN(timeSec) && timeSec > 0.0)
                    {
                        if (timeSec <= 120.0) warnOut.Add(new AlertItem(_cachedAlertBattDrain, FormatDbsSec(Mathf.RoundToInt((float)timeSec)), true));
                        else if (timeSec <= 300.0) cautOut.Add(new AlertItem(_cachedAlertDischarging, FormatDbsMin(Mathf.RoundToInt((float)(timeSec / 60.0))), false));
                    }
                }
            }

            // Kerbalism 空间天气与舱压/CO2
            if (ExternalProbeRegistry.HasKerbalism)
            {
                double inStorm = ExternalProbeRegistry.ResolveNumeric("KLSM", "INSTORM");
                if (inStorm > 0.5) warnOut.Add(new AlertItem(_cachedAlertSolarStorm, _cachedAlertCme, true));

                double habRad = ExternalProbeRegistry.ResolveNumeric("KLSM", "HABITATRADIATION");
                if (!double.IsNaN(habRad) && habRad > 0.05)
                {
                    string radStr = FormatRadiation(habRad);
                    if (habRad > 0.20) warnOut.Add(new AlertItem(_cachedAlertRadDanger, radStr, true));
                    else cautOut.Add(new AlertItem(_cachedAlertHighRad, radStr, false));
                }

                if (telem.CrewCapacity > 0 && telem.CrewCount > 0)
                {
                    double poisoning = ExternalProbeRegistry.ResolveNumeric("KLSM", "POISONING");
                    if (!double.IsNaN(poisoning) && poisoning > 0.30)
                    {
                        int co2Pct = Mathf.Clamp(Mathf.RoundToInt((float)(poisoning * 100.0)), 0, 100);
                        string co2Str = CacheManager.FastPercent(co2Pct);
                        if (poisoning > 0.70) warnOut.Add(new AlertItem(_cachedAlertCo2Crit, co2Str, true));
                        else cautOut.Add(new AlertItem(_cachedAlertHighCo2, co2Str, false));
                    }

                    double habPress = ExternalProbeRegistry.ResolveNumeric("KLSM", "PRESSURE");
                    if (!double.IsNaN(habPress) && habPress > 0.001 && habPress < 0.40)
                    {
                        warnOut.Add(new AlertItem(_cachedAlertCabinPress, FormatPressure(habPress), true));
                    }
                }
            }
        }



        private void ComputeNominalFlightPhaseData(IFlightTelemetry telem)
        {
            if (telem == null) return;

            string curSit = telem.FlightSituation;
            bool hasNode = telem.HasManeuverNode;
            float throttle = telem.Throttle;

            // 稳态静默期识别 (Quiescent Steady-State Detection)
            bool isSteadyOrbit = curSit == "ORBITING" && !hasNode && telem.ActiveEngines == 0 && throttle <= 0.001f;
            bool isSteadyPad = (curSit == "LANDED" || curSit == "PRELAUNCH") && telem.SurfaceSpeed < 0.5 && telem.ActiveEngines == 0 && throttle <= 0.001f;
            _isQuiescentFlightState = isSteadyOrbit || isSteadyPad;

            double atmoCutoff = _cachedAtmoCutoff;
            double effectivePe = _cachedEffectivePe;
            double effectiveAp = _cachedEffectiveAp;

            // 若在稳态静默巡航轨道中且工况文本未失效，直接复用，免除全部后续分流与格式化逻辑
            if (isSteadyOrbit && _dataNominalTitle == _cachedStrOrbitCruise && Math.Abs(effectiveAp - _lastRenderedAp.Value) <= 500.0)
            {
                return;
            }
            if (isSteadyPad && _dataNominalTitle == _cachedStrReady)
            {
                return;
            }

            string title;
            string sub;
            string icon;
            EventColorRole role;

            if (hasNode)
            {
                title = _cachedStrNodeArmed ?? (_cachedStrNodeArmed = I18n.Tr("WIDGET_STATUS_NODE_ARMED", "节点待命"));
                double dv = telem.ManeuverDeltaV;
                if (_lastRenderedDv.Update(dv))
                {
                    _cachedDvSub = $"Δv {dv:F0}";
                }
                sub = _cachedDvSub ?? ($"Δv {dv:F0}");
                icon = "◆";
                role = EventColorRole.AccentPrimary;
            }
            else if (telem.FlightSituation == "LANDED" || telem.FlightSituation == "PRELAUNCH" || telem.FlightSituation == "SPLASHED")
            {
                title = _cachedStrReady ?? (_cachedStrReady = I18n.Tr("WIDGET_STATUS_READY", "发射就绪"));
                sub = _cachedNominalReadySub ?? (_cachedNominalReadySub = I18n.Tr("WIDGET_ALERT_READY", "就绪"));
                icon = "●";
                role = EventColorRole.Success;
            }
            else if (telem.FlightSituation == "ESCAPING" || (effectiveAp < 0 && effectiveAp > -9000000.0))
            {
                title = _cachedStrEscape ?? (_cachedStrEscape = I18n.Tr("WIDGET_STATUS_ESCAPE", "深空逃逸"));
                if (_lastRenderedPe.Update(effectivePe))
                {
                    _cachedPeSub = $"Pe {FormatKm(effectivePe)}";
                }
                sub = _cachedPeSub ?? ($"Pe {FormatKm(effectivePe)}");
                icon = "▲";
                role = EventColorRole.Success;
            }
            else if (effectivePe < atmoCutoff && telem.AltitudeASL >= atmoCutoff && effectivePe > -9000000.0)
            {
                // 航天器处于太空高度，但近拱点已降至大气层内或地表之下 (执行了离轨制动或处于再入走廊)
                if (_lastRenderedPe.Update(effectivePe))
                {
                    _cachedPeSub = $"Pe {FormatKm(effectivePe)}";
                }
                sub = _cachedPeSub ?? ($"Pe {FormatKm(effectivePe)}");
                if (effectivePe < 0)
                {
                    title = _cachedStrBallistic ?? (_cachedStrBallistic = I18n.Tr("WIDGET_STATUS_BALLISTIC", "弹道再入撞击"));
                    icon = "▼";
                    role = EventColorRole.WarningColor;
                }
                else
                {
                    title = _cachedStrDeorbit ?? (_cachedStrDeorbit = I18n.Tr("WIDGET_STATUS_DEORBIT", "离轨再入走廊"));
                    icon = "▼";
                    role = EventColorRole.WarningColor;
                }
            }
            else if (telem.FlightSituation == "ORBITING" || (effectivePe >= atmoCutoff && telem.AltitudeASL >= atmoCutoff))
            {
                title = _cachedStrOrbitCruise ?? (_cachedStrOrbitCruise = I18n.Tr("WIDGET_STATUS_ORBIT_CRUISE", "轨道巡航"));
                if (_lastRenderedAp.Update(effectiveAp))
                {
                    _cachedApSub = $"Ap {FormatKm(effectiveAp)}";
                }
                sub = _cachedApSub ?? ($"Ap {FormatKm(effectiveAp)}");
                icon = "●";
                role = EventColorRole.AccentSecondary;
            }
            else if (atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed > 10.0)
            {
                title = _cachedStrAscent ?? (_cachedStrAscent = I18n.Tr("WIDGET_STATUS_ASCENT", "大气爬升"));
                float mach = (float)telem.Mach;
                if (_lastRenderedMach.Update(mach))
                {
                    _cachedMachSub = $"M {mach:F1}";
                }
                sub = _cachedMachSub ?? ($"M {mach:F1}");
                icon = "▲";
                role = EventColorRole.WarningColor;
            }
            else if (telem.VerticalSpeed < -10.0 && (atmoCutoff > 0.0 ? telem.AltitudeASL < atmoCutoff * 0.5 : telem.AltitudeAGL < 3000.0))
            {
                title = _cachedStrApproach ?? (_cachedStrApproach = I18n.Tr("WIDGET_STATUS_APPROACH", "降落进近"));
                float vsi = (float)telem.VerticalSpeed;
                if (_lastRenderedVsi.Update(vsi))
                {
                    _cachedVsiSub = $"VSI {Mathf.RoundToInt(vsi)}";
                }
                sub = _cachedVsiSub ?? ($"VSI {Mathf.RoundToInt(vsi)}");
                icon = "▼";
                role = EventColorRole.WarningColor;
            }
            else
            {
                title = _cachedStrSuborbital ?? (_cachedStrSuborbital = I18n.Tr("WIDGET_STATUS_SUBORBITAL", "亚轨道飞行"));
                sub = _cachedNominalSuborbSub ?? (_cachedNominalSuborbSub = I18n.Tr("WIDGET_STATUS_SUBORBITAL_SUB", "亚轨道"));
                icon = "◈";
                role = EventColorRole.AccentPrimary;
            }

            if (!object.ReferenceEquals(_dataNominalTitle, title) ||
                !object.ReferenceEquals(_dataNominalSub, sub) ||
                !object.ReferenceEquals(_dataNominalIcon, icon) ||
                _dataNominalRole != role)
            {
                _dataNominalTitle = title;
                _dataNominalSub = sub;
                _dataNominalIcon = icon;
                _dataNominalRole = role;
                // nominal dirty
            }
        }



        public override void Reset()
        {
            _bannerQueue.Clear();
            _eventLastTriggerTimes.Clear();
            _currentEvent = default;
            _bannerState = BannerDisplayState.Normal;
            _bannerTimer = 0f;
            _cautAlerts.Clear();
            _warnAlerts.Clear();
            _urgentCautAlerts.Clear();
            _urgentWarnAlerts.Clear();
            _subsysCautAlerts.Clear();
            _subsysWarnAlerts.Clear();
            _apprCautAlerts.Clear();
            _apprWarnAlerts.Clear();
            _slowCautAlerts.Clear();
            _slowWarnAlerts.Clear();
            _tempCaut.Clear();
            _tempWarn.Clear();
            _heartbeatTick = 0;
            _alertsDirty = true;
            _cautAcknowledged = false;
            _warnAcknowledged = false;
            _lastCautCount = -1;
            _lastWarnCount = -1;
            _cautIndex = 0;
            _warnIndex = 0;
            _rotateTimer = 0f;
            _lastRenderedAp.ResetToDefault();
            _lastRenderedPe.ResetToDefault();
            _lastRenderedDv.ResetToDefault();
            _lastRenderedMach.ResetToDefault();
            _lastRenderedVsi.ResetToDefault();
            _cachedApSub = null;
            _cachedPeSub = null;
            _cachedDvSub = null;
            _cachedMachSub = null;
            _cachedVsiSub = null;
            _dataNominalTitle = string.Empty;
            _dataNominalSub = string.Empty;
            _dataNominalIcon = string.Empty;
            _dataNominalRole = EventColorRole.AccentSecondary;
            _fmtAglMeters = -9999;
            _fmtVsiVal = -9999;
            _fmtGForceVal = -999.0;
            _fmtTempInt = -9999;
            _fmtTtiSec = -9999;
            _fmtClosureRateVal = -999.0;
            _fmtVMassVal = -999.0;
            _fmtRadVal = -999.0;
            _fmtPressVal = -999.0;
            _fmtDbsSec = -9999;
            _fmtDbsMin = -9999;
            _isQuiescentFlightState = false;
            CurrentState = default;
        }
    }

    [FlightWidget("master_warning", "warning_annunciator", "annunciator", "cws", Category = WidgetCategory.Systems, DisplayName = "中央主告警光字牌", Description = "双等级航电警告光字牌：黄色注意与红色危急双通道轮播，支持拉起、失速、低油、低电、缺氧全量监测，点击可消警。", DefaultWidgetId = "core.master_warning", DefaultX = 0f, DefaultY = -66f, IsSingleton = true, ExactIds = new[] { "core.master_warning" })]
    public class MasterWarningWidget : BaseFlightWidget
    {
        private readonly MasterWarningLogic _logic = new MasterWarningLogic();
        protected override IWidgetLogic LogicCore => _logic;
        public override Vector2 BaseSize => new Vector2(184f, 42f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

        public int ModulesCount => _logic.ModulesCount;

        public enum BannerEventType
        {
            None = FlightTransientEventType.None,
            Separation = FlightTransientEventType.Separation,         // 分级分离 / 脱扣
            EngineStart = FlightTransientEventType.EngineStart,       // 引擎启动 / 点火
            MECO = FlightTransientEventType.MECO,                     // 主发关机 / 熄火
            ManeuverApproach = FlightTransientEventType.ManeuverApproach, // 接近机动节点 (T-60s)
            ManeuverBurn = FlightTransientEventType.ManeuverBurn,     // 机动点火执行
            OrbitAchieved = FlightTransientEventType.OrbitAchieved,   // 入轨圆化完成 (Stable Orbit)
            Deorbit = FlightTransientEventType.Deorbit,               // 飞船离轨制动 / 进入再入走廊
            AtmosphereEntry = FlightTransientEventType.AtmosphereEntry, // 再入/进入大气层 (Entry Interface)
            Blackout = FlightTransientEventType.Blackout,             // 再入等离子体黑障
            Escape = FlightTransientEventType.Escape,                 // 逃逸轨道建立 (双曲线逃逸)
            SoiTransition = FlightTransientEventType.SoiTransition,   // 穿越引力范围 (SOI 切换)
            SuicideBurn = FlightTransientEventType.SuicideBurn,       // 动力减速着陆点火
            ApoapsisPass = FlightTransientEventType.ApoapsisPass,     // 通过远拱点
            PeriapsisPass = FlightTransientEventType.PeriapsisPass,   // 通过近拱点
            DockingMode = FlightTransientEventType.DockingMode,       // 进入对接模式
            Touchdown = FlightTransientEventType.Touchdown,           // 着陆接地成功
            MaxQ = FlightTransientEventType.MaxQ,                     // 突破最大动压
            V1Rotate = FlightTransientEventType.V1Rotate,             // 起飞决断/抬轮速度
            SolarStorm = FlightTransientEventType.SolarStorm,         // 太阳风暴冲击
            AvionicsLock = FlightTransientEventType.AvionicsLock,     // 航电失控锁定
            TerrainImpact = FlightTransientEventType.TerrainImpact,   // 地表撞击告警
            DockingCapture = FlightTransientEventType.DockingCapture, // 对接锁扣捕获
            EngineFailure = FlightTransientEventType.EngineFailure,   // 发动机故障失效
            ThermalOverheat = FlightTransientEventType.ThermalOverheat // 热回路过热告警
        }

        private void OnAcknowledgeCaution() => AcknowledgeCaution();
        private void OnAcknowledgeWarning() => AcknowledgeWarning();

        // 外部底板与装饰构件
        private Image _outerBezel;
        private Outline _outerOutline;
        private Image _centerDivider;
        private Button _centerDividerBtn;
        private Image _horizDivider;
        private Button _horizDividerBtn;

        // 左舱：Caution 视图组件
        private GameObject _cautCell;
        private RectTransform _cautRect;
        private Image _cautBg;
        private Outline _cautOutline;
        private Image _cautPipBar;
        private Text _cautIcon;
        private Text _cautTitle;
        private Text _cautSub;
        private Button _cautBtn;

        // 右舱：Warning 视图组件
        private GameObject _warnCell;
        private RectTransform _warnRect;
        private Image _warnBg;
        private Outline _warnOutline;
        private Image _warnPipBar;
        private Text _warnIcon;
        private Text _warnTitle;
        private Text _warnSub;
        private Button _warnBtn;

        // 状态提醒 / 一体横幅 UI 节点
        private GameObject _bannerCell;
        private RectTransform _bannerRect;
        private Image _bannerBg;
        private Outline _bannerOutline;
        private Image _bannerPipBar;
        private Text _bannerLeftIcon;
        private Text _bannerTitle;
        private Text _bannerRightIcon;
        private Text _bannerSub;

        // 缓存与防抖 (SPEC-009)
        private readonly CachedFloat _bannerSlidePos = new CachedFloat(-999f, tolerance: 0.5f);
        private readonly Cached<BannerDisplayState> _lastBannerDisplayState = new Cached<BannerDisplayState>(BannerDisplayState.Normal);
        private readonly Cached<int> _lastCautCount = new Cached<int>(-1);
        private readonly Cached<int> _lastWarnCount = new Cached<int>(-1);
        private readonly Cached<bool> _lastCautBlink = new Cached<bool>(false);
        private readonly Cached<bool> _lastWarnBlink = new Cached<bool>(false);
        private readonly Cached<Color> _lastNominalPhaseColor = new Cached<Color>(Color.clear);
        private readonly Cached<string> _lastRenderedNominalTitle = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedNominalSub = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedNominalIcon = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedEventTitle = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedEventSub = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedEventLeftIcon = new Cached<string>(null);
        private readonly Cached<string> _lastRenderedEventRightIcon = new Cached<string>(null);

        private bool _cautWasDeadFront = false;
        private bool _warnWasDeadFront = false;
        private bool _cellsStyleNeedsUpdate = true;
        private bool _nominalStyleNeedsUpdate = true;

        private string _cachedStrCaution;
        private string _cachedStrWarning;
        private string _cachedStrNorm;
        private string _cachedStrArmed;

        private void InitCachedI18n()
        {
            _cachedStrCaution = I18n.Tr("WIDGET_ALERT_CAUTION", "注意");
            _cachedStrWarning = I18n.Tr("WIDGET_ALERT_WARNING", "危急");
            _cachedStrNorm = I18n.Tr("WIDGET_ALERT_NORM", "正常");
            _cachedStrArmed = I18n.Tr("WIDGET_ALERT_ARMED", "待命");
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            InitCachedI18n();
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            // 1. 解析自定义模板配置 (包括模块数量、自定义文本与门限)
            int mc = GetTemplateChannelInt(new[] { "MODULES", "MODE", "COUNT" }, _logic.ModulesCount);
            if (mc >= 2 && mc <= 3) _logic.SetModulesCount(mc);
            if (GetTemplateChannelBool("PYRAMID", false)) _logic.SetModulesCount(3);
            float iv = GetTemplateChannelFloat(new[] { "INTERVAL", "ROTATION" }, 2.5f);
            if (iv > 0.5f) _logic.ConfigureSwitchInterval(iv);
            string sep = GetTemplateChannel(new[] { "SEP", "SEP_TEXT", "SEPARATION" }, null);
            if (!string.IsNullOrEmpty(sep)) _logic.ConfigureSepTitleTemplate(sep);
            string eng = GetTemplateChannel(new[] { "ENG", "ENG_TEXT", "IGNITION" }, null);
            if (!string.IsNullOrEmpty(eng)) _logic.ConfigureEngTitleTemplate(eng);
            float dt = GetTemplateChannelFloat(new[] { "TIME", "BANNER_TIME", "DURATION" }, 1.25f);
            if (dt > 0.4f) _logic.ConfigureBannerDuration(dt);
            float fw = GetTemplateChannelFloat(new[] { "FUEL_WARN", "MIN_FUEL" }, -1f);
            float fc = GetTemplateChannelFloat(new[] { "FUEL_CAUT", "LOW_FUEL" }, -1f);
            _logic.ConfigureCustomThresholds(fw, fc);
            if (config != null)
            {
                if (config.WarningThreshold > 0.01 && config.WarningThreshold <= 25.0)
                    _logic.WarningThreshold = (float)config.WarningThreshold / 100f;
                if (config.CautionThreshold > 0.01 && config.CautionThreshold <= 40.0)
                    _logic.CautionThreshold = (float)config.CautionThreshold / 100f;
            }

            // 2. 航空外框底盘 (Outer Bezel 由基类托管)
            Vector2 initialSize = (_logic.ModulesCount == 3) ? new Vector2(184f * s, 42f * s) : new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = initialSize;

            _outerBezel = CardBackground;
            if (_outerBezel != null) _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            _outerOutline = CardOutline;
            if (_outerOutline != null)
            {
                _outerOutline.effectDistance = new Vector2(1f * s, 1f * s);
                _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            // 3. 中央硬派机械隔离筋条 (Mechanical Divider Rib)
            GameObject divObj = UIFactory.CreatePanel(transform, "Divider", new Vector2(2f * s, 18f * s),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _centerDivider = divObj.GetComponent<Image>();
            _centerDividerBtn = divObj.AddComponent<Button>();
            _centerDividerBtn.transition = Selectable.Transition.None;
            _centerDividerBtn.onClick.AddListener(ToggleModulesMode);

            // 4. 水平机械分隔横梁 (Horizontal Divider Rib, 用于 3 模块金字塔形态)
            GameObject horizObj = UIFactory.CreatePanel(transform, "HorizDivider", new Vector2(180f * s, 2f * s),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _horizDivider = horizObj.GetComponent<Image>();
            _horizDividerBtn = horizObj.AddComponent<Button>();
            _horizDividerBtn.transition = Selectable.Transition.None;
            _horizDividerBtn.onClick.AddListener(ToggleModulesMode);

            // 5. 构建左舱：CAUTION 光字牌 (宽 89px, 高 18px)
            Vector2 cellSize = new Vector2(89f * s, 18f * s);
            BuildCautionCell(s, cellSize, theme);

            // 6. 构建右舱：WARNING 光字牌 (宽 89px, 高 18px)
            Vector2 cellSizeWarn = new Vector2(89f * s, 18f * s);
            BuildWarningCell(s, cellSizeWarn, theme);

            // 7. 构建状态提醒窗 / 一体横幅 (宽 180px, 高 18px)
            BuildBannerCell(s, new Vector2(180f * s, 18f * s), theme);

            // 8. 依据当前模式应用几何布局与定位
            ApplyLayoutMode();

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Outer Bezel", _outerBezel.gameObject, "底盘外框与机械外边框", t =>
            {
                if (_outerBezel != null) _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, t);
                if (_outerOutline != null) _outerOutline.effectColor = WidgetStyleManager.Weighted(t.AccentSecondary, LineWeight.Ghost);
            }));
            this.Controls.Register(new WidgetAnnunciatorControl("caution_annunciator", "CAUTION Annunciator", _cautCell, _cautTitle, _cautSub, _cautBg, _cautOutline));
            this.Controls.Register(new WidgetAnnunciatorControl("warning_annunciator", "WARNING Annunciator", _warnCell, _warnTitle, _warnSub, _warnBg, _warnOutline));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "banner_cell", "Banner Cell", _bannerCell, "瞬态事件一体横幅光字牌"));
            if (_centerDivider != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "center_divider", "Center Divider", _centerDivider.gameObject, "垂直硬派隔离筋条", t =>
                {
                    if (_centerDivider != null) _centerDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, t);
                }));
            }
            if (_horizDivider != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "horiz_divider", "Horizontal Divider", _horizDivider.gameObject, "水平机械分隔横梁", t =>
                {
                    if (_horizDivider != null) _horizDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, t);
                }));
            }

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);
            ApplyTheme(theme);
        }

        private void BuildCautionCell(float s, Vector2 size, ThemeConfig theme)
        {
            _cautCell = UIFactory.CreatePanel(transform, "CautionCell", size, new Vector2(-46f * s, 0f), Color.clear);
            _cautRect = _cautCell.GetComponent<RectTransform>();
            _cautBg = _cautCell.GetComponent<Image>();
            _cautOutline = _cautCell.AddComponent<Outline>();
            _cautOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _cautBtn = _cautCell.AddComponent<Button>();
            _cautBtn.transition = Selectable.Transition.None;
            _cautBtn.onClick.AddListener(OnAcknowledgeCaution);

            // 顶部高光 Pip 指示条
            GameObject pipObj = UIFactory.CreatePanel(_cautCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _cautPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标
            _cautIcon = UIFactory.CreateText(_cautCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.WarningColor);
            _cautIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _cautIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _cautTitle = UIFactory.CreateText(_cautCell.transform, "Title", I18n.Tr("WIDGET_ALERT_CAUTION", "注意"), Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.WarningColor);
            _cautTitle.fontStyle = FontStyle.Bold;
            _cautTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _cautTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注
            _cautSub = UIFactory.CreateText(_cautCell.transform, "Sub", "NORM", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.WarningColor);
            _cautSub.fontStyle = FontStyle.Normal;
            _cautSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _cautSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void BuildWarningCell(float s, Vector2 size, ThemeConfig theme)
        {
            _warnCell = UIFactory.CreatePanel(transform, "WarningCell", size, new Vector2(46f * s, 0f), Color.clear);
            _warnRect = _warnCell.GetComponent<RectTransform>();
            _warnBg = _warnCell.GetComponent<Image>();
            _warnOutline = _warnCell.AddComponent<Outline>();
            _warnOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _warnBtn = _warnCell.AddComponent<Button>();
            _warnBtn.transition = Selectable.Transition.None;
            _warnBtn.onClick.AddListener(OnAcknowledgeWarning);

            // 顶部高光 Pip 指示条
            GameObject pipObj = UIFactory.CreatePanel(_warnCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _warnPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标
            _warnIcon = UIFactory.CreateText(_warnCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.DangerColor);
            _warnIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _warnIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _warnTitle = UIFactory.CreateText(_warnCell.transform, "Title", I18n.Tr("WIDGET_ALERT_WARNING", "警告"), Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.DangerColor);
            _warnTitle.fontStyle = FontStyle.Bold;
            _warnTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _warnTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注
            _warnSub = UIFactory.CreateText(_warnCell.transform, "Sub", I18n.Tr("WIDGET_ALERT_ARMED", "待发"), Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.DangerColor);
            _warnSub.fontStyle = FontStyle.Normal;
            _warnSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _warnSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void BuildBannerCell(float s, Vector2 size, ThemeConfig theme)
        {
            _bannerCell = UIFactory.CreatePanel(transform, "BannerCell", size, Vector2.zero, Color.clear);
            _bannerRect = _bannerCell.GetComponent<RectTransform>();
            _bannerBg = _bannerCell.GetComponent<Image>();
            _bannerOutline = _bannerCell.AddComponent<Outline>();
            _bannerOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 顶部高光 Pip 指示条
            GameObject pipObj = UIFactory.CreatePanel(_bannerCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _bannerPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标 ([-88px .. -76px])
            _bannerLeftIcon = UIFactory.CreateText(_bannerCell.transform, "LeftIcon", "◀", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _bannerLeftIcon.rectTransform.sizeDelta = new Vector2(12f * s, size.y);
            _bannerLeftIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 8f * s, 0f);

            // 主标题 ([-48px .. +36px], 居中偏左 6px)
            _bannerTitle = UIFactory.CreateText(_bannerCell.transform, "Title", I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离"), Mathf.Max(8, Mathf.RoundToInt(8.5f * s)),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _bannerTitle.fontStyle = FontStyle.Bold;
            _bannerTitle.rectTransform.sizeDelta = new Vector2(84f * s, size.y);
            _bannerTitle.rectTransform.anchoredPosition = new Vector2(-6f * s, 0f);

            // 右侧微型附注 ([+39px .. +73px])
            _bannerSub = UIFactory.CreateText(_bannerCell.transform, "Sub", "STG", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.AccentPrimary);
            _bannerSub.fontStyle = FontStyle.Normal;
            _bannerSub.rectTransform.sizeDelta = new Vector2(34f * s, size.y);
            _bannerSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 34f * s, 0f);

            // 右侧状态微标 ([+76px .. +88px])
            _bannerRightIcon = UIFactory.CreateText(_bannerCell.transform, "RightIcon", "▶", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _bannerRightIcon.rectTransform.sizeDelta = new Vector2(12f * s, size.y);
            _bannerRightIcon.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 8f * s, 0f);

            _bannerCell.SetActive(false);
        }


        public void ToggleModulesMode()
        {
            SetModulesCount(_logic.ModulesCount == 3 ? 2 : 3);
        }

        public void SetModulesCount(int count)
        {
            int clamped = Mathf.Clamp(count, 2, 3);
            if (_logic.ModulesCount == clamped) return;

            _logic.SetModulesCount(clamped);

            // 回写配置模板，实现无感热插拔与持久化保存
            if (Config != null)
            {
                string t = Config.CustomTemplate ?? string.Empty;
                if (t.IndexOf("MODULES=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"MODULES=\d+;?", $"MODULES={_logic.ModulesCount};", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                else
                {
                    t = $"MODULES={_logic.ModulesCount};" + t;
                }
                Config.CustomTemplate = t;
            }

            ApplyLayoutMode();
            _cellsStyleNeedsUpdate = true;
            _nominalStyleNeedsUpdate = true;
            _lastBannerDisplayState.Reset(BannerDisplayState.Normal);
            _lastRenderedNominalTitle.Reset(null);
            _lastRenderedNominalSub.Reset(null);
            _lastRenderedNominalIcon.Reset(null);
            _lastNominalPhaseColor.Reset(Color.clear);
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            ApplyTheme(theme);
        }

        public void ApplyLayoutMode()
        {
            float s = CurrentDpiScale;

            if (_logic.ModulesCount == 3)
            {
                Vector2 widgetSize = new Vector2(184f * s, 42f * s);
                RectTransform.sizeDelta = widgetSize;
                if (_outerBezel != null) _outerBezel.rectTransform.sizeDelta = widgetSize;

                if (_cautRect != null)
                {
                    _cautRect.sizeDelta = new Vector2(89f * s, 18f * s);
                    _cautRect.anchoredPosition = new Vector2(-46f * s, 10f * s);
                    _cautCell.SetActive(true);
                }

                if (_warnRect != null)
                {
                    _warnRect.sizeDelta = new Vector2(89f * s, 18f * s);
                    _warnRect.anchoredPosition = new Vector2(46f * s, 10f * s);
                    _warnCell.SetActive(true);
                }

                if (_centerDivider != null)
                {
                    _centerDivider.rectTransform.sizeDelta = new Vector2(2f * s, 18f * s);
                    _centerDivider.rectTransform.anchoredPosition = new Vector2(0f, 10f * s);
                    _centerDivider.gameObject.SetActive(true);
                }

                if (_horizDivider != null)
                {
                    _horizDivider.rectTransform.sizeDelta = new Vector2(180f * s, 2f * s);
                    _horizDivider.rectTransform.anchoredPosition = Vector2.zero;
                    _horizDivider.gameObject.SetActive(true);
                }

                if (_bannerRect != null)
                {
                    _bannerRect.sizeDelta = new Vector2(180f * s, 18f * s);
                    _bannerRect.anchoredPosition = new Vector2(0f, -10f * s);
                    _bannerCell.SetActive(true);
                }
            }
            else
            {
                Vector2 widgetSize = new Vector2(184f * s, 22f * s);
                RectTransform.sizeDelta = widgetSize;
                if (_outerBezel != null) _outerBezel.rectTransform.sizeDelta = widgetSize;

                if (_horizDivider != null)
                {
                    _horizDivider.gameObject.SetActive(false);
                }

                if (_logic.CurrentState.BannerState == BannerDisplayState.Normal)
                {
                    if (_cautRect != null)
                    {
                        _cautRect.sizeDelta = new Vector2(89f * s, 18f * s);
                        _cautRect.anchoredPosition = new Vector2(-46f * s, 0f);
                        _cautCell.SetActive(true);
                    }

                    if (_warnRect != null)
                    {
                        _warnRect.sizeDelta = new Vector2(89f * s, 18f * s);
                        _warnRect.anchoredPosition = new Vector2(46f * s, 0f);
                        _warnCell.SetActive(true);
                    }

                    if (_centerDivider != null)
                    {
                        _centerDivider.rectTransform.sizeDelta = new Vector2(2f * s, 18f * s);
                        _centerDivider.rectTransform.anchoredPosition = Vector2.zero;
                        _centerDivider.gameObject.SetActive(true);
                    }

                    if (_bannerRect != null)
                    {
                        _bannerRect.sizeDelta = new Vector2(180f * s, 18f * s);
                        _bannerRect.anchoredPosition = Vector2.zero;
                        _bannerCell.SetActive(false);
                    }
                }
                else
                {
                    if (_bannerRect != null)
                    {
                        _bannerRect.sizeDelta = new Vector2(180f * s, 18f * s);
                        _bannerRect.anchoredPosition = Vector2.zero;
                    }
                }
            }
        }

        private Color ResolveEventColor(EventColorRole role, ThemeConfig theme)
        {
            switch (role)
            {
                case EventColorRole.AccentPrimary: return theme.AccentPrimary;
                case EventColorRole.AccentSecondary: return theme.AccentSecondary;
                case EventColorRole.WarningColor: return theme.WarningColor;
                case EventColorRole.DangerColor: return theme.DangerColor;
                case EventColorRole.Success: return theme.AccentPositive;
                default: return theme.AccentPrimary;
            }
        }

        public void TriggerBanner(FlightTransientEventType eventType, bool immediateHolding = false)
        {
            _logic.TriggerBanner(eventType, immediateHolding);
        }

        public void TriggerBanner(BannerEventType eventType, bool immediateHolding = false)
        {
            _logic.TriggerBanner(eventType, immediateHolding);
        }

        public void AcknowledgeCaution() => _logic.AcknowledgeCaution();
        public void AcknowledgeWarning() => _logic.AcknowledgeWarning();

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            _logic.AdvanceFrame(context.DeltaTime);
            MasterWarningState state = _logic.CurrentState;
            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            OnRenderState(in state, theme);
        }

        private void OnRenderState(in MasterWarningState state, ThemeConfig theme)
        {
            if (!state.HasVessel) return;

            // 检测横幅状态机阶跃 (SPEC-009 防抖状态机路由)
            bool bannerStateChanged = _lastBannerDisplayState.Update(state.BannerState);
            if (bannerStateChanged)
            {
                if (state.BannerState == BannerDisplayState.Normal)
                {
                    // 从横幅事件回归常态：强制重置所有巡航工况文本与样式缓存，确保光字牌文字与颜色瞬时复位熄灭
                    _lastRenderedNominalTitle.Reset(null);
                    _lastRenderedNominalSub.Reset(null);
                    _lastRenderedNominalIcon.Reset(null);
                    _lastNominalPhaseColor.Reset(Color.clear);
                    _nominalStyleNeedsUpdate = true;
                }
                else
                {
                    // 进入新横幅事件：重置事件文本缓存，确保新事件文案无死区即时写入
                    _lastRenderedEventTitle.Reset(null);
                    _lastRenderedEventSub.Reset(null);
                    _lastRenderedEventLeftIcon.Reset(null);
                    _lastRenderedEventRightIcon.Reset(null);
                }
            }

            if (state.ModulesCount == 3)
            {
                RenderVisualCells(in state, theme);
                if (state.BannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(in state, theme);
                }
                else
                {
                    RenderNominalFlightPhaseUI(in state, theme);
                }
            }
            else
            {
                if (state.BannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(in state, theme);
                    return;
                }

                // 2模块模式回归常态：确保关闭横幅光字牌，复位中央筋条与左右舱位置
                float s = CurrentDpiScale;
                if (_bannerCell != null && _bannerCell.activeSelf) _bannerCell.SetActive(false);
                if (_cautCell != null && !_cautCell.activeSelf) _cautCell.SetActive(true);
                if (_warnCell != null && !_warnCell.activeSelf) _warnCell.SetActive(true);
                if (_centerDivider != null && !_centerDivider.gameObject.activeSelf) _centerDivider.gameObject.SetActive(true);
                if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(-46f * s, 0f);
                if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(46f * s, 0f);

                RenderVisualCells(in state, theme);
            }
        }

        private void UpdateBannerAnimation(in MasterWarningState state, ThemeConfig theme)
        {
            if (theme == null) theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float s = CurrentDpiScale;
            Color eventColor = ResolveEventColor(state.EventColor, theme);

            if (state.BannerState == BannerDisplayState.MergingIn)
            {
                if (state.ModulesCount == 2)
                {
                    float slideX = state.BannerSlideX * s;
                    if (_bannerSlidePos.Update(slideX))
                    {
                        if (_centerDivider != null) _centerDivider.gameObject.SetActive(false);
                        if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(-slideX, 0f);
                        if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(slideX, 0f);
                    }
                }
            }
            else if (state.BannerState == BannerDisplayState.MergedHolding || state.BannerState == BannerDisplayState.SwitchingEvent)
            {
                if (state.ModulesCount == 2)
                {
                    if (_centerDivider != null && _centerDivider.gameObject.activeSelf) _centerDivider.gameObject.SetActive(false);
                    if (_cautCell != null && _cautCell.activeSelf) _cautCell.SetActive(false);
                    if (_warnCell != null && _warnCell.activeSelf) _warnCell.SetActive(false);
                    if (_bannerCell != null && !_bannerCell.activeSelf) _bannerCell.SetActive(true);
                }
                else
                {
                    if (_cautCell != null && !_cautCell.activeSelf) _cautCell.SetActive(true);
                    if (_warnCell != null && !_warnCell.activeSelf) _warnCell.SetActive(true);
                    if (_bannerCell != null && !_bannerCell.activeSelf) _bannerCell.SetActive(true);
                }

                if (_lastRenderedEventTitle.Update(state.EventTitle))
                {
                    if (_bannerTitle != null) _bannerTitle.SetTextSafe(state.EventTitle);
                }
                if (_lastRenderedEventSub.Update(state.EventSub))
                {
                    if (_bannerSub != null) _bannerSub.SetTextSafe(state.EventSub);
                }
                if (_lastRenderedEventLeftIcon.Update(state.EventLeftIcon))
                {
                    if (_bannerLeftIcon != null) _bannerLeftIcon.SetTextSafe(state.EventLeftIcon);
                }
                if (_lastRenderedEventRightIcon.Update(state.EventRightIcon))
                {
                    if (_bannerRightIcon != null) _bannerRightIcon.SetTextSafe(state.EventRightIcon);
                }

                Color activeCol = WidgetStyleManager.WithAlpha(eventColor, state.BannerPulseAlpha);

                if (_bannerBg != null) SetColorIfChanged(_bannerBg, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme));
                if (_bannerOutline != null) SetOutlineColorIfChanged(_bannerOutline, eventColor);
                if (_bannerPipBar != null) SetColorIfChanged(_bannerPipBar, activeCol);
                if (_bannerTitle != null) SetColorIfChanged(_bannerTitle, eventColor);
                if (_bannerSub != null) SetColorIfChanged(_bannerSub, WidgetStyleManager.WithAlpha(eventColor, 0.75f));
                if (_bannerLeftIcon != null) SetColorIfChanged(_bannerLeftIcon, eventColor);
                if (_bannerRightIcon != null) SetColorIfChanged(_bannerRightIcon, eventColor);
            }
            else if (state.BannerState == BannerDisplayState.FlashingBack)
            {
                if (state.ModulesCount == 2)
                {
                    float slideX = state.BannerSlideX * s;
                    if (_bannerSlidePos.Update(slideX))
                    {
                        if (_bannerCell != null && _bannerCell.activeSelf) _bannerCell.SetActive(false);
                        if (_cautCell != null && !_cautCell.activeSelf) _cautCell.SetActive(true);
                        if (_warnCell != null && !_warnCell.activeSelf) _warnCell.SetActive(true);
                        if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(-slideX, 0f);
                        if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(slideX, 0f);
                    }
                }
                else
                {
                    // 3模块模式回闪收尾：快速淡出高光条
                    if (_bannerPipBar != null)
                    {
                        Color fadeCol = WidgetStyleManager.WithAlpha(eventColor, state.BannerPulseAlpha * 0.5f);
                        SetColorIfChanged(_bannerPipBar, fadeCol);
                    }
                }
            }
        }

        private void RenderNominalFlightPhaseUI(in MasterWarningState state, ThemeConfig theme)
        {
            if (_bannerCell == null || !_bannerCell.activeSelf) return;

            Color phaseColor = ResolveEventColor(state.NominalRole, theme);

            if (_lastRenderedNominalTitle.Update(state.NominalTitle))
            {
                if (_bannerTitle != null) _bannerTitle.SetTextSafe(state.NominalTitle);
            }
            if (_lastRenderedNominalSub.Update(state.NominalSub))
            {
                if (_bannerSub != null) _bannerSub.SetTextSafe(state.NominalSub);
            }
            if (_lastRenderedNominalIcon.Update(state.NominalIcon))
            {
                if (_bannerLeftIcon != null) _bannerLeftIcon.SetTextSafe(state.NominalIcon);
                if (_bannerRightIcon != null) _bannerRightIcon.SetTextSafe(state.NominalIcon);
            }

            if (_lastNominalPhaseColor.Update(phaseColor) || _nominalStyleNeedsUpdate)
            {
                _nominalStyleNeedsUpdate = false;
                if (_bannerBg != null) SetColorIfChanged(_bannerBg, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                if (_bannerOutline != null) SetOutlineColorIfChanged(_bannerOutline, WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
                if (_bannerPipBar != null) SetColorIfChanged(_bannerPipBar, phaseColor);
                if (_bannerTitle != null) SetColorIfChanged(_bannerTitle, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme));
                if (_bannerSub != null) SetColorIfChanged(_bannerSub, phaseColor);
                if (_bannerLeftIcon != null) SetColorIfChanged(_bannerLeftIcon, phaseColor);
                if (_bannerRightIcon != null) SetColorIfChanged(_bannerRightIcon, phaseColor);
            }
        }

        private void RenderVisualCells(in MasterWarningState state, ThemeConfig theme)
        {
            if (theme == null) theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            if (theme == null) return;

            // ── A. 渲染左舱：CAUTION ──
            if (state.HasCautAlert)
            {
                _cautWasDeadFront = false;
                _cautTitle.SetTextSafe(state.CautTitle);
                _cautSub.SetTextSafe(state.CautSub);
                _cautIcon.SetTextSafe(state.CautIcon);

                if (_lastCautBlink.Update(state.CautBlink) || _cellsStyleNeedsUpdate)
                {
                    if (state.CautBlink)
                    {
                        _cautBg.SetColor(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme));
                        _cautOutline.SetColor(theme.WarningColor);
                        _cautPipBar.SetColor(theme.WarningColor);
                        _cautTitle.SetColor(theme.WarningColor);
                        _cautSub.SetColor(theme.WarningColor);
                        _cautIcon.SetColor(theme.WarningColor);
                    }
                    else
                    {
                        _cautBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme));
                        _cautOutline.SetColor(WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Faint));
                        _cautPipBar.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.30f));
                        _cautTitle.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f));
                        _cautSub.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f));
                        _cautIcon.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f));
                    }
                }
            }
            else
            {
                if (!_cautWasDeadFront || _cellsStyleNeedsUpdate)
                {
                    _cautWasDeadFront = true;
                    // 暗态待命 (Dead-Front Nominal)
                    _cautTitle.SetTextSafe(_cachedStrCaution ?? (_cachedStrCaution = I18n.Tr("WIDGET_ALERT_CAUTION", "注意")));
                    _cautSub.SetTextSafe(_cachedStrNorm ?? (_cachedStrNorm = I18n.Tr("WIDGET_ALERT_NORM", "正常")));
                    _cautIcon.SetTextSafe("●");

                    _cautBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                    _cautOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
                    _cautPipBar.SetColor(Color.clear);
                    _cautTitle.SetColor(WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f));
                    _cautSub.SetColor(WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    _cautIcon.SetColor(WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost));
                }
            }

            // ── B. 渲染右舱：WARNING ──
            if (state.HasWarnAlert)
            {
                _warnWasDeadFront = false;
                _warnTitle.SetTextSafe(state.WarnTitle);
                _warnSub.SetTextSafe(state.WarnSub);
                _warnIcon.SetTextSafe(state.WarnIcon);

                if (_lastWarnBlink.Update(state.WarnBlink) || _cellsStyleNeedsUpdate)
                {
                    if (state.WarnBlink)
                    {
                        _warnBg.SetColor(WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger, theme));
                        _warnOutline.SetColor(theme.DangerColor);
                        _warnPipBar.SetColor(theme.DangerColor);
                        _warnTitle.SetColor(theme.DangerColor);
                        _warnSub.SetColor(theme.DangerColor);
                        _warnIcon.SetColor(theme.DangerColor);
                    }
                    else
                    {
                        _warnBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme));
                        _warnOutline.SetColor(WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Faint));
                        _warnPipBar.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.30f));
                        _warnTitle.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f));
                        _warnSub.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f));
                        _warnIcon.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f));
                    }
                }
            }
            else
            {
                if (!_warnWasDeadFront || _cellsStyleNeedsUpdate)
                {
                    _warnWasDeadFront = true;
                    // 暗态待命 (Dead-Front Nominal)
                    _warnTitle.SetTextSafe(_cachedStrWarning ?? (_cachedStrWarning = I18n.Tr("WIDGET_ALERT_WARNING", "危急")));
                    _warnSub.SetTextSafe(_cachedStrArmed ?? (_cachedStrArmed = I18n.Tr("WIDGET_ALERT_ARMED", "待命")));
                    _warnIcon.SetTextSafe("●");

                    _warnBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
                    _warnOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
                    _warnPipBar.SetColor(Color.clear);
                    _warnTitle.SetColor(WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f));
                    _warnSub.SetColor(WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    _warnIcon.SetColor(WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost));
                }
            }

            _cellsStyleNeedsUpdate = false;
        }

        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            InitCachedI18n();
            _logic.UpdateI18n();
            _cautWasDeadFront = false;
            _warnWasDeadFront = false;
            _cellsStyleNeedsUpdate = true;
            _nominalStyleNeedsUpdate = true;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            InitCachedI18n();

            _outerBezel?.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme));
            _outerOutline?.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _centerDivider?.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _horizDivider?.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));

            this.Controls.ApplyThemeToControls(theme);

            _cellsStyleNeedsUpdate = true;
            _nominalStyleNeedsUpdate = true;
        }

        protected override void OnResetPrivateCache()
        {
            _bannerSlidePos.Reset(-999f);
            _lastBannerDisplayState.Reset(BannerDisplayState.Normal);
            _lastCautCount.Reset(-1);
            _lastWarnCount.Reset(-1);
            _lastCautBlink.Reset(false);
            _lastWarnBlink.Reset(false);
            _cellsStyleNeedsUpdate = true;
            _nominalStyleNeedsUpdate = true;
            _lastNominalPhaseColor.Reset(Color.clear);
            _lastRenderedNominalTitle.Reset(null);
            _lastRenderedNominalSub.Reset(null);
            _lastRenderedNominalIcon.Reset(null);
            _lastRenderedEventTitle.Reset(null);
            _lastRenderedEventSub.Reset(null);
            _lastRenderedEventLeftIcon.Reset(null);
            _lastRenderedEventRightIcon.Reset(null);
            _cautWasDeadFront = false;
            _warnWasDeadFront = false;
            _logic.Reset();
        }

        protected override void OnDestroy()
        {
            OnResetPrivateCache();
            if (_cautBtn != null) _cautBtn.onClick.RemoveAllListeners();
            if (_warnBtn != null) _warnBtn.onClick.RemoveAllListeners();
            if (_centerDividerBtn != null) _centerDividerBtn.onClick.RemoveAllListeners();
            if (_horizDividerBtn != null) _horizDividerBtn.onClick.RemoveAllListeners();
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
