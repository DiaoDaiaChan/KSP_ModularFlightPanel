using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 现代化多功能分级与飞行姿态操纵台 (Modern Flight Control Suite)
    /// ====================================================================================
    /// 遵循 MFP-SPEC-001..012 航电规范与现代全玻璃座舱 HUD 交互设计标准：
    /// 1. 机载工业级机壳底板：4角 DZUS 快速拆卸锁紧螺栓总成与极细工程倒角包边；
    /// 2. 顶栏安全联动：集成式 Korry 389 照光式电气联锁电门 (ARMED / SAFE) 与防误触分级点火触发器 (FIRE STG ▶)；
    /// 3. 分级遥测中枢：高反差深晶数显分级窗 (STAGE 03) 结合单级 Δv、燃烧时序 (⏱ 00:36)、推重比 (TWR) 与发动机计数；
    /// 4. 三轴游标标尺：PITCH / ROLL / YAW 动态双向导轨刻度尺、零位基准中轴线、四分度游标与机械配平指示游标 (Trim Pip)；
    /// 5. 推进剂监测槽：智能双语推进剂标签、十分度刻度微线、经典航空量纲标记 (E / ½ / F) 与三段式状态着色；
    /// 6. 底部航电软键：3路 Korry 风格分栏照光式按键 (NORM/PREC 微调、STG/DCK 飞行/对接口与 KSP HUD 显隐)；
    /// 7. 严格落实 0 颜色字面量 (MFP-SPEC-006)、零场景查询 (MFP-SPEC-007) 与 Critical (60Hz) 阶梯高保真刷新。
    /// </summary>
    public struct StageControlState : IEquatable<StageControlState>
    {
        public bool HasVessel;
        public bool IsLocked;
        public int CurrentStage;
        public double StageDeltaV;
        public double StageBurnTime;
        public float Twr;
        public int ActiveEngines;
        public float PitchInput;
        public float PitchTrim;
        public float RollInput;
        public float RollTrim;
        public float YawInput;
        public float YawTrim;
        public float StagePropellantFraction;
        public string PropName;
        public bool IsPrecisionControl;
        public bool IsDockingMode;

        public bool Equals(StageControlState other)
        {
            return HasVessel == other.HasVessel &&
                   IsLocked == other.IsLocked &&
                   CurrentStage == other.CurrentStage &&
                   Math.Abs(StageDeltaV - other.StageDeltaV) < 0.01 &&
                   Math.Abs(StageBurnTime - other.StageBurnTime) < 0.01 &&
                   Math.Abs(Twr - other.Twr) < 0.01f &&
                   ActiveEngines == other.ActiveEngines &&
                   Math.Abs(PitchInput - other.PitchInput) < 0.005f &&
                   Math.Abs(PitchTrim - other.PitchTrim) < 0.005f &&
                   Math.Abs(RollInput - other.RollInput) < 0.005f &&
                   Math.Abs(RollTrim - other.RollTrim) < 0.005f &&
                   Math.Abs(YawInput - other.YawInput) < 0.005f &&
                   Math.Abs(YawTrim - other.YawTrim) < 0.005f &&
                   Math.Abs(StagePropellantFraction - other.StagePropellantFraction) < 0.002f &&
                   string.Equals(PropName, other.PropName, StringComparison.Ordinal) &&
                   IsPrecisionControl == other.IsPrecisionControl &&
                   IsDockingMode == other.IsDockingMode;
        }

        public override bool Equals(object obj) => obj is StageControlState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasVessel.GetHashCode();
                hash = (hash * 397) ^ IsLocked.GetHashCode();
                hash = (hash * 397) ^ CurrentStage;
                hash = (hash * 397) ^ StageDeltaV.GetHashCode();
                hash = (hash * 397) ^ StageBurnTime.GetHashCode();
                hash = (hash * 397) ^ Twr.GetHashCode();
                hash = (hash * 397) ^ ActiveEngines;
                hash = (hash * 397) ^ PitchInput.GetHashCode();
                hash = (hash * 397) ^ RollInput.GetHashCode();
                hash = (hash * 397) ^ YawInput.GetHashCode();
                hash = (hash * 397) ^ StagePropellantFraction.GetHashCode();
                hash = (hash * 397) ^ (PropName != null ? PropName.GetHashCode() : 0);
                hash = (hash * 397) ^ IsPrecisionControl.GetHashCode();
                hash = (hash * 397) ^ IsDockingMode.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 分级与操纵台纯 C# 业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class StageControlLogic : WidgetLogic<StageControlState>
    {
        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    Reset();
                }
                return;
            }

            string rawName = telemetry.StagePropellantName;
            if (string.IsNullOrEmpty(rawName)) rawName = "PROP";
            if (rawName.StartsWith("PROP:", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(5).Trim();
            else if (rawName.StartsWith("PROP", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(4).Trim();
            if (string.IsNullOrEmpty(rawName)) rawName = "PROP";

            CurrentState = new StageControlState
            {
                HasVessel = true,
                IsLocked = telemetry.IsStageLocked || StockStageActionService.IsStagingLocked,
                CurrentStage = telemetry.CurrentStage,
                StageDeltaV = telemetry.StageDeltaV,
                StageBurnTime = telemetry.StageBurnTime,
                Twr = (float)telemetry.TWR,
                ActiveEngines = telemetry.ActiveEngines,
                PitchInput = telemetry.PitchInput,
                PitchTrim = telemetry.PitchTrim,
                RollInput = telemetry.RollInput,
                RollTrim = telemetry.RollTrim,
                YawInput = telemetry.YawInput,
                YawTrim = telemetry.YawTrim,
                StagePropellantFraction = Mathf.Clamp01((float)telemetry.StagePropellantFraction),
                PropName = rawName.ToUpperInvariant(),
                IsPrecisionControl = telemetry.IsPrecisionControl,
                IsDockingMode = telemetry.IsDockingMode
            };
        }
    }

    [FlightWidget("stage_control", "staging_ctrl", Category = WidgetCategory.Controls, DisplayName = "操纵量指示与分级锁控制台", Description = "Pitch/Roll/Yaw 实时舵量标尺与分级安全锁定 (Alt+L) 防误触操作台。", DefaultWidgetId = "core.stage_control", DefaultX = -360f, DefaultY = -180f, IsSingleton = true, HighFrequency = true, ExactIds = new[] { "core.stage_control" })]
    public class StageControlWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(DefaultPanelWidth, DefaultPanelHeight);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Critical;

        private const float DefaultPanelWidth = 204f;
        private const float DefaultPanelHeight = 186f;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(170f, 150f);
        public Vector2 MaxBaseSize => new Vector2(360f, 320f);

        // 声明式微控件 (顶栏主标题)
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL"), null, 8.5f);

        // 4角 DZUS 工业螺栓紧固总成
        private GameObject[] _chassisScrews;
        private GameObject[] _chassisScrewSlots;

        // 顶栏系统就绪指示 LED
        private GameObject _sysStatusLed;
        private Image _sysStatusLedImg;

        // 顶栏安全联动总成 (Korry 389 照光式开关设计)
        private Button _lockBtn;
        private Image _lockBtnBg;
        private Outline _lockBtnOutline;
        private Image _lockBtnLed;
        private Text _lockBtnText;

        private Button _fireBtn;
        private Image _fireBtnBg;
        private Outline _fireBtnOutline;
        private Text _fireBtnText;

        private Image _topDivider;
        private Image _bottomDivider;

        // 分级遥测中枢凹槽窗 (Stage Telemetry Bay)
        private GameObject _stageBayPanel;
        private Image _stageBayBg;
        private Outline _stageBayOutline;

        private GameObject _stageNumBox;
        private Image _stageNumBoxBg;
        private Image _stageAccentBar;
        private Text _stageLabelText;
        private Text _stageNumText;
        private Image _stageBayDivider;

        private Text _stageDvText;
        private Text _stageTwrEngText;

        // 三轴游标刻度操纵量仪表区 (Pitch, Roll, Yaw)
        private class AxisMeterUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Text Label;
            public RectTransform LabelRt;

            public GameObject Track;
            public RectTransform TrackRt;
            public Image TrackBg;

            public Image RailTop;
            public Image RailBottom;

            public RectTransform FillRt;
            public Image FillImg;
            public RectTransform PointerRt;
            public Image PointerImg;

            public RectTransform TrimRt;
            public Image TrimImg;

            public RectTransform CenterTickRt;
            public RectTransform TickNegRt;
            public RectTransform TickPosRt;
            public RectTransform SubTickNegRt;
            public RectTransform SubTickPosRt;
            public RectTransform MicroTickNegRt;
            public RectTransform MicroTickPosRt;
            public RectTransform MicroTickNeg75Rt;
            public RectTransform MicroTickPos75Rt;

            public Text ValText;
            public RectTransform ValRt;
            public readonly CachedFloat LastTrim = new CachedFloat(-999f, tolerance: 0.005f);
        }

        private AxisMeterUI _pitchMeter;
        private AxisMeterUI _rollMeter;
        private AxisMeterUI _yawMeter;
        private readonly CachedFloat _cachedTrackWidth = new CachedFloat(96f, tolerance: 0.5f);

        // 分级推进剂计量槽 (航电油箱指示条)
        private GameObject _propTagBg;
        private Image _propTagBgImg;
        private Outline _propTagOutline;
        private Text _propNameText;
        private Text _propPctText;

        private GameObject _propTrackPanel;
        private Image _propTrackBg;
        private Outline _propTrackOutline;
        private RectTransform _propFillRt;
        private Image _propFillImg;

        // 推进剂刻度线与 E/¼/½/¾/F 标度
        private RectTransform[] _propTicks;
        private Text _propEText;
        private Text _propQuarterText;
        private Text _propHalfText;
        private Text _propThreeQuarterText;
        private Text _propFText;

        // 底部航电软键按键组 (Korry 389 照光式按键)
        private Button _precBtn;
        private Image _precImg;
        private Outline _precOutline;
        private Image _precLed;
        private Text _precText;

        private Button _modeBtn;
        private Image _modeImg;
        private Outline _modeOutline;
        private Image _modeLed;
        private Text _modeText;

        private Button _stockToggleBtn;
        private Image _stockToggleImg;
        private Outline _stockToggleOutline;
        private Image _stockToggleLed;
        private Text _stockToggleText;
        private readonly Cached<bool> _stockHidden = new Cached<bool>(true);

        // 动效与交互计时器 (全托管生命周期)
        private readonly CachedFloat _fireBtnRecoilTimer = new CachedFloat(0f, tolerance: 0.001f);
        private readonly CachedFloat _currentPropFrac = new CachedFloat(1f, tolerance: 0.001f);

        private readonly StageControlLogic _logic = new StageControlLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // 统一私有状态缓存
        private ThemeConfig _cachedTheme;
        private readonly Cached<bool> _dirtyLocked = new Cached<bool>(false);
        private readonly Cached<bool> _dirtyPrec = new Cached<bool>(false);
        private readonly Cached<bool> _dirtyDock = new Cached<bool>(false);
        private readonly Cached<int> _dirtyStageNum = new Cached<int>(-1);
        private readonly CachedDouble _dirtyStageDv = new CachedDouble(-1.0, tolerance: 0.5);
        private readonly Cached<string> _dirtyPropName = new Cached<string>(null);
        private readonly Cached<int> _lastPitchPct = new Cached<int>(-9999);
        private readonly Cached<int> _lastRollPct = new Cached<int>(-9999);
        private readonly Cached<int> _lastYawPct = new Cached<int>(-9999);
        private readonly CachedFloat _lastLayoutW = new CachedFloat(-1f, tolerance: 0.5f);
        private readonly CachedFloat _lastLayoutH = new CachedFloat(-1f, tolerance: 0.5f);
        private readonly Cached<int> _dirtyBurnSec = new Cached<int>(-1);
        private readonly CachedFloat _dirtyTwr = new CachedFloat(-1f, tolerance: 0.05f);
        private readonly Cached<int> _dirtyActiveEngines = new Cached<int>(-1);
        private readonly CachedFloat _dirtyPropFill = new CachedFloat(-1f, tolerance: 0.002f);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = BaseSize * s;
            RectTransform.sizeDelta = panelSize;

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // ==========================================
            // 0. 四角 DZUS 工业螺栓 (Captive Screws)
            // ==========================================
            BuildChassisScrews(panelSize, s, theme);

            // ==========================================
            // 1. 顶栏安全联动总成 (Header)
            // ==========================================
            if (Title != null)
            {
                Title.Text = I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL");
                Title.SetRole(TextStyleRole.Cardinal);
                if (Title.TextComponent != null)
                {
                    Title.TextComponent.fontSize = Mathf.Max(6, Mathf.RoundToInt(7.5f * s));
                    Title.TextComponent.fontStyle = FontStyle.Bold;
                    Title.TextComponent.alignment = TextAnchor.MiddleLeft;
                    Title.TextComponent.resizeTextForBestFit = false;
                    Title.TextComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Title.TextComponent.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }

            // 系统状态微型 LED (带外圈金属倒角)
            _sysStatusLed = UIFactory.CreatePanel(transform, "SysStatusLed",
                new Vector2(4.5f * s, 4.5f * s), Vector2.zero, theme.AccentPrimary);
            _sysStatusLedImg = _sysStatusLed.GetComponent<Image>();
            Outline ledOl = _sysStatusLed.AddComponent<Outline>();
            ledOl.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            ledOl.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);

            // Korry 389 照光式分级安全电门 (ARMED / SAFE)
            Vector2 lockBtnSize = new Vector2(42f * s, 16f * s);
            _lockBtn = UIFactory.CreateButton(transform, "Btn_Lock", lockBtnSize, Vector2.zero, OnToggleLock);
            _lockBtnBg = _lockBtn.GetComponent<Image>();
            _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _lockBtnOutline = _lockBtn.gameObject.AddComponent<Outline>();
            _lockBtnOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _lockBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 电门顶部照光 LED 条 (2px)
            GameObject lockLedGo = UIFactory.CreatePanel(_lockBtn.transform, "LedBar",
                new Vector2(lockBtnSize.x, 2f * s), new Vector2(0f, (lockBtnSize.y - 2f * s) * 0.5f),
                WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _lockBtnLed = lockLedGo.GetComponent<Image>();

            _lockBtnText = UIFactory.CreateText(_lockBtn.transform, "Text", I18n.Tr("WIDGET_ALERT_ARMED", "ARMED"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _lockBtnText.fontStyle = FontStyle.Bold;
            _lockBtnText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _lockBtnText.verticalOverflow = VerticalWrapMode.Overflow;
            _lockBtnText.rectTransform.sizeDelta = new Vector2(lockBtnSize.x, lockBtnSize.y - 2f * s);
            _lockBtnText.rectTransform.anchoredPosition = new Vector2(0f, -1f * s);
            _lockBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_LOCK_TITLE", "Stage Lock (Alt+L)"),
                I18n.Tr("TOOLTIP_STAGE_LOCK_DESC", "Toggle staging safety interlock."), "Alt+L");

            // 防误触分级触发电门 (STAGE ▶)
            Vector2 fireBtnSize = new Vector2(46f * s, 16f * s);
            _fireBtn = UIFactory.CreateButton(transform, "Btn_Fire", fireBtnSize, Vector2.zero, OnFireStage);
            _fireBtnBg = _fireBtn.GetComponent<Image>();
            _fireBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _fireBtnOutline = _fireBtn.gameObject.AddComponent<Outline>();
            _fireBtnOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _fireBtnOutline.effectColor = style.GetMeterColor(MeterStyleRole.Warning, theme);

            _fireBtnText = UIFactory.CreateText(_fireBtn.transform, "Text", I18n.Tr("WIDGET_STAGE_STAGE", "STAGE ▶"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetMeterColor(MeterStyleRole.Warning, theme));
            _fireBtnText.fontStyle = FontStyle.Bold;
            _fireBtnText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _fireBtnText.verticalOverflow = VerticalWrapMode.Overflow;
            _fireBtnText.rectTransform.sizeDelta = fireBtnSize;
            _fireBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_FIRE_TITLE", "Activate Stage (Space)"),
                I18n.Tr("TOOLTIP_STAGE_FIRE_DESC", "Execute next stage ignition sequence."), "Space");

            // 顶部分割细线
            _topDivider = CreateChild<Image>("TopDivider", transform);
            _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 底部分割细线
            _bottomDivider = CreateChild<Image>("BottomDivider", transform);
            _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // ==========================================
            // 2. 分级遥测中枢凹槽窗 (Stage Telemetry Bay)
            // ==========================================
            _stageBayPanel = UIFactory.CreatePanel(transform, "StageBay",
                new Vector2(panelSize.x - 16f * s, 30f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _stageBayBg = _stageBayPanel.GetComponent<Image>();
            _stageBayOutline = _stageBayPanel.GetComponent<Outline>();

            // 左部：级数数显微窗 (内嵌黑底 + 霓虹指示条)
            Vector2 numBoxSize = new Vector2(38f * s, 24f * s);
            _stageNumBox = UIFactory.CreatePanel(_stageBayPanel.transform, "NumBox", numBoxSize,
                Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);
            _stageNumBoxBg = _stageNumBox.GetComponent<Image>();

            // 左边缘垂直霓虹条 (2.5px)
            GameObject barGo = UIFactory.CreatePanel(_stageNumBox.transform, "AccentBar",
                new Vector2(2.5f * s, 24f * s), new Vector2(-numBoxSize.x * 0.5f + 1.25f * s, 0f), theme.AccentPrimary);
            _stageAccentBar = barGo.GetComponent<Image>();

            _stageLabelText = UIFactory.CreateText(_stageNumBox.transform, "Label", I18n.Tr("WIDGET_CTRL_STAGE_LABEL", "STAGE"),
                Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stageLabelText.fontStyle = FontStyle.Bold;
            _stageLabelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stageLabelText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform slRt = _stageLabelText.GetComponent<RectTransform>();
            slRt.sizeDelta = new Vector2(numBoxSize.x - 4f * s, 8f * s);
            slRt.anchoredPosition = new Vector2(1f * s, 5.5f * s);

            _stageNumText = UIFactory.CreateText(_stageNumBox.transform, "StageNum", "07",
                Mathf.Max(8, Mathf.RoundToInt(12f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageNumText.fontStyle = FontStyle.Bold;
            _stageNumText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stageNumText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform snRt = _stageNumText.GetComponent<RectTransform>();
            snRt.sizeDelta = new Vector2(numBoxSize.x - 4f * s, 14f * s);
            snRt.anchoredPosition = new Vector2(1f * s, -4.5f * s);

            // 垂直分割微线 (1px, 结构化加强)
            GameObject bayDivGo = UIFactory.CreatePanel(_stageBayPanel.transform, "BayDivider",
                new Vector2(1f * s, 22f * s), Vector2.zero, WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle));
            _stageBayDivider = bayDivGo.GetComponent<Image>();

            // 右部：单级性能 (上行 Δv，下行燃烧时间与 TWR 与引擎数)
            _stageDvText = UIFactory.CreateText(_stageBayPanel.transform, "StageDv", "Δv  0 m/s",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageDvText.fontStyle = FontStyle.Bold;
            _stageDvText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stageDvText.verticalOverflow = VerticalWrapMode.Overflow;

            _stageTwrEngText = UIFactory.CreateText(_stageBayPanel.transform, "StageTwrEng", "T 00:00 │ 0.00 TWR │ 0 ENG",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stageTwrEngText.fontStyle = FontStyle.Normal;
            _stageTwrEngText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stageTwrEngText.verticalOverflow = VerticalWrapMode.Overflow;

            // ==========================================
            // 3. 三轴姿态操纵量游标仪表区 (Pitch, Roll, Yaw)
            // ==========================================
            _pitchMeter = BuildAxisMeter(transform, "Pitch", "P · " + I18n.Tr("WIDGET_AXIS_PITCH", "PITCH"), s, theme);
            _rollMeter = BuildAxisMeter(transform, "Roll", "R · " + I18n.Tr("WIDGET_AXIS_ROLL", "ROLL"), s, theme);
            _yawMeter = BuildAxisMeter(transform, "Yaw", "Y · " + I18n.Tr("WIDGET_AXIS_YAW", "YAW"), s, theme);

            // ==========================================
            // 4. 分级推进剂监测槽 (Propellant Tank)
            // ==========================================
            _propTagBg = UIFactory.CreatePanel(transform, "PropTagBg",
                new Vector2(96f * s, 12f * s), Vector2.zero, Color.clear);
            _propTagBgImg = _propTagBg.GetComponent<Image>();
            _propTagOutline = _propTagBg.GetComponent<Outline>();
            if (_propTagOutline != null) _propTagOutline.effectColor = Color.clear;

            _propNameText = UIFactory.CreateText(transform, "PropName", I18n.Tr("WIDGET_PROP_PROPELLANT", "PROPELLANT"),
                Mathf.Max(5, Mathf.RoundToInt(6f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propNameText.fontStyle = FontStyle.Bold;
            _propNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propNameText.verticalOverflow = VerticalWrapMode.Overflow;

            _propPctText = UIFactory.CreateText(transform, "PropPct", "100.0%",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _propPctText.fontStyle = FontStyle.Bold;
            _propPctText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propPctText.verticalOverflow = VerticalWrapMode.Overflow;

            // 推进剂槽轨道 (带刻度标尺与发光条)
            _propTrackPanel = UIFactory.CreatePanel(transform, "PropTrack",
                new Vector2(panelSize.x - 16f * s, 6f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _propTrackBg = _propTrackPanel.GetComponent<Image>();
            _propTrackOutline = _propTrackPanel.GetComponent<Outline>();

            // 推进剂填充条 (先建填充层，后建刻度线确保刻度清晰叠在燃料条之上)
            GameObject propFill = UIFactory.CreatePanel(_propTrackPanel.transform, "Fill",
                new Vector2(panelSize.x - 16f * s, 4f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            _propFillRt = propFill.GetComponent<RectTransform>();
            _propFillRt.anchorMin = new Vector2(0f, 0.5f);
            _propFillRt.anchorMax = new Vector2(0f, 0.5f);
            _propFillRt.pivot = new Vector2(0f, 0.5f);
            _propFillRt.anchoredPosition = Vector2.zero;
            _propFillImg = propFill.GetComponent<Image>();

            // 刻度微线 (10% ~ 90% 共 9 条十分度微刻线，20% 低油警告刻线，50% 半程大刻线)
            Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle);
            _propTicks = new RectTransform[9];
            for (int i = 0; i < 9; i++)
            {
                float tH = ((i + 1) == 5) ? 5.5f * s : 3.5f * s;
                Color thisTickCol = ((i + 1) == 2) ? theme.WarningColor : tickCol;
                GameObject tGo = UIFactory.CreatePanel(_propTrackPanel.transform, $"Tick_{(i + 1) * 10}",
                    new Vector2(1f * s, tH), Vector2.zero, thisTickCol);
                _propTicks[i] = tGo.GetComponent<RectTransform>();
            }

            // E / ¼ / ½ / ¾ / F 经典航空量纲标度
            _propEText = UIFactory.CreateText(transform, "PropE", "E",
                Mathf.Max(4, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propEText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propEText.verticalOverflow = VerticalWrapMode.Overflow;

            _propQuarterText = UIFactory.CreateText(transform, "PropQtr", "¼",
                Mathf.Max(4, Mathf.RoundToInt(5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propQuarterText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propQuarterText.verticalOverflow = VerticalWrapMode.Overflow;

            _propHalfText = UIFactory.CreateText(transform, "PropHalf", "½",
                Mathf.Max(4, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propHalfText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propHalfText.verticalOverflow = VerticalWrapMode.Overflow;

            _propThreeQuarterText = UIFactory.CreateText(transform, "Prop3Qtr", "¾",
                Mathf.Max(4, Mathf.RoundToInt(5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propThreeQuarterText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propThreeQuarterText.verticalOverflow = VerticalWrapMode.Overflow;

            _propFText = UIFactory.CreateText(transform, "PropF", "F",
                Mathf.Max(4, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propFText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _propFText.verticalOverflow = VerticalWrapMode.Overflow;

            // ==========================================
            // 5. 底部快捷模式切换按键组 (Korry 389 照光式)
            // ==========================================
            Vector2 miniBtnSize = new Vector2(58f * s, 15f * s);

            _precBtn = UIFactory.CreateButton(transform, "Btn_Prec", miniBtnSize, Vector2.zero, OnTogglePrecision);
            _precImg = _precBtn.GetComponent<Image>();
            _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _precOutline = _precBtn.gameObject.AddComponent<Outline>();
            _precOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _precOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            GameObject precLedGo = UIFactory.CreatePanel(_precBtn.transform, "LedBar",
                new Vector2(miniBtnSize.x, 2f * s), new Vector2(0f, (miniBtnSize.y - 2f * s) * 0.5f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _precLed = precLedGo.GetComponent<Image>();

            _precText = UIFactory.CreateText(_precBtn.transform, "Text", "NORM",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _precText.fontStyle = FontStyle.Bold;
            _precText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _precText.verticalOverflow = VerticalWrapMode.Overflow;
            _precText.rectTransform.sizeDelta = new Vector2(miniBtnSize.x, miniBtnSize.y - 2f * s);
            _precText.rectTransform.anchoredPosition = new Vector2(0f, -1f * s);
            _precBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_PREC_TITLE", "Precision Mode (Caps Lock)"),
                I18n.Tr("TOOLTIP_STAGE_PREC_DESC", "Toggle fine control surface sensitivity."), "Caps Lock");

            _modeBtn = UIFactory.CreateButton(transform, "Btn_Mode", miniBtnSize, Vector2.zero, OnToggleMode);
            _modeImg = _modeBtn.GetComponent<Image>();
            _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeOutline = _modeBtn.gameObject.AddComponent<Outline>();
            _modeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _modeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            GameObject modeLedGo = UIFactory.CreatePanel(_modeBtn.transform, "LedBar",
                new Vector2(miniBtnSize.x, 2f * s), new Vector2(0f, (miniBtnSize.y - 2f * s) * 0.5f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _modeLed = modeLedGo.GetComponent<Image>();

            _modeText = UIFactory.CreateText(_modeBtn.transform, "Text", "STG",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _modeText.fontStyle = FontStyle.Bold;
            _modeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _modeText.verticalOverflow = VerticalWrapMode.Overflow;
            _modeText.rectTransform.sizeDelta = new Vector2(miniBtnSize.x, miniBtnSize.y - 2f * s);
            _modeText.rectTransform.anchoredPosition = new Vector2(0f, -1f * s);
            _modeBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_MODE_TITLE", "Flight Mode"),
                I18n.Tr("TOOLTIP_STAGE_MODE_DESC", "Toggle staging vs docking mode."), "Flight Mode");

            _stockToggleBtn = UIFactory.CreateButton(transform, "Btn_Stock", miniBtnSize, Vector2.zero, OnToggleStockVisibility);
            _stockToggleImg = _stockToggleBtn.GetComponent<Image>();
            _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stockToggleOutline = _stockToggleBtn.gameObject.AddComponent<Outline>();
            _stockToggleOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _stockToggleOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            GameObject stockLedGo = UIFactory.CreatePanel(_stockToggleBtn.transform, "LedBar",
                new Vector2(miniBtnSize.x, 2f * s), new Vector2(0f, (miniBtnSize.y - 2f * s) * 0.5f),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _stockToggleLed = stockLedGo.GetComponent<Image>();

            _stockToggleText = UIFactory.CreateText(_stockToggleBtn.transform, "Text", "KSP HUD",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stockToggleText.fontStyle = FontStyle.Bold;
            _stockToggleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stockToggleText.verticalOverflow = VerticalWrapMode.Overflow;
            _stockToggleText.rectTransform.sizeDelta = new Vector2(miniBtnSize.x, miniBtnSize.y - 2f * s);
            _stockToggleText.rectTransform.anchoredPosition = new Vector2(0f, -1f * s);
            _stockToggleBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_STOCK_TITLE", "Native HUD Toggle"),
                I18n.Tr("TOOLTIP_STAGE_STOCK_DESC", "Toggle visibility of stock staging panel."), "HUD Toggle");

            // 初始化时默认执行静默隐藏原版左下角
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden.Value);

            // 标准化组件内部控件注册至管理器
            if (_fireBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_fire_btn", "STAGE FIRE", _fireBtn.gameObject, _fireBtn, _fireBtnBg, null, _fireBtnText, null, "STAGE", OnFireStage, false));
            }
            if (_lockBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_lock_btn", "STAGE LOCK", _lockBtn.gameObject, _lockBtn, _lockBtnBg, null, _lockBtnText, null, "ARMED", OnToggleLock, true));
            }
            if (_stageBayPanel != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "stage_telemetry_bay", "STAGE TELEMETRY", _stageBayPanel);
            }
            if (_propTrackBg != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "propellant_meter", "PROPELLANT METER", _propTrackBg.gameObject);
            }

            ApplyLayout(panelSize.x, panelSize.y);
            ApplyTheme(theme);
        }

        private void BuildChassisScrews(Vector2 panelSize, float s, ThemeConfig theme)
        {
            _chassisScrews = new GameObject[4];
            _chassisScrewSlots = new GameObject[4];

            Color screwCol = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            Color slotCol = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);

            for (int i = 0; i < 4; i++)
            {
                GameObject sGo = UIFactory.CreatePanel(transform, $"Screw_{i}", new Vector2(4.5f * s, 4.5f * s), Vector2.zero, screwCol);
                _chassisScrews[i] = sGo;

                GameObject slotGo = UIFactory.CreatePanel(sGo.transform, "Slot", new Vector2(2.5f * s, 1f * s), Vector2.zero, slotCol);
                _chassisScrewSlots[i] = slotGo;
            }
        }

        private AxisMeterUI BuildAxisMeter(Transform parent, string name, string label, float s, ThemeConfig theme)
        {
            AxisMeterUI meter = new AxisMeterUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            meter.RootRt = CreateContainer("Axis_" + name, parent, new Vector2(DefaultPanelWidth * s, 14f * s));
            meter.Root = meter.RootRt.gameObject;
            GameObject rootGo = meter.Root;

            // 1. 无框式极简航电文本标签 (PITCH / ROLL / YAW)
            meter.Label = UIFactory.CreateText(rootGo.transform, "Label", label,
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            meter.Label.fontStyle = FontStyle.Bold;
            meter.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            meter.Label.verticalOverflow = VerticalWrapMode.Overflow;
            meter.LabelRt = meter.Label.GetComponent<RectTransform>();

            // 2. 标尺游标轨道 (宽动态自适应, 高 5px, 极简深邃暗晶无卡片框)
            meter.Track = UIFactory.CreatePanel(rootGo.transform, "Track",
                new Vector2(_cachedTrackWidth * s, 5f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme));
            meter.TrackRt = meter.Track.GetComponent<RectTransform>();
            meter.TrackBg = meter.Track.GetComponent<Image>();

            // 上下极细导轨线 (0.8px)
            Color railCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            GameObject railT = UIFactory.CreatePanel(meter.Track.transform, "RailTop", new Vector2(_cachedTrackWidth * s, 0.8f * s), Vector2.zero, railCol);
            meter.RailTop = railT.GetComponent<Image>();
            GameObject railB = UIFactory.CreatePanel(meter.Track.transform, "RailBottom", new Vector2(_cachedTrackWidth * s, 0.8f * s), Vector2.zero, railCol);
            meter.RailBottom = railB.GetComponent<Image>();

            // 两端极限量程刻线 (-100% / +100%, 5px)
            Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            GameObject tNeg = UIFactory.CreatePanel(meter.Track.transform, "TickNeg",
                new Vector2(1f * s, 5f * s), Vector2.zero, tickCol);
            meter.TickNegRt = tNeg.GetComponent<RectTransform>();

            GameObject tPos = UIFactory.CreatePanel(meter.Track.transform, "TickPos",
                new Vector2(1f * s, 5f * s), Vector2.zero, tickCol);
            meter.TickPosRt = tPos.GetComponent<RectTransform>();

            // 半程四分度刻线 (-50% / +50%, 3.5px)
            GameObject stNeg = UIFactory.CreatePanel(meter.Track.transform, "SubTickNeg",
                new Vector2(1f * s, 3.5f * s), Vector2.zero, tickCol);
            meter.SubTickNegRt = stNeg.GetComponent<RectTransform>();

            GameObject stPos = UIFactory.CreatePanel(meter.Track.transform, "SubTickPos",
                new Vector2(1f * s, 3.5f * s), Vector2.zero, tickCol);
            meter.SubTickPosRt = stPos.GetComponent<RectTransform>();

            // 四分之一微刻线 (-25% / +25%, 2.2px)
            GameObject mtNeg = UIFactory.CreatePanel(meter.Track.transform, "MicroTickNeg",
                new Vector2(1f * s, 2.2f * s), Vector2.zero, tickCol);
            meter.MicroTickNegRt = mtNeg.GetComponent<RectTransform>();

            GameObject mtPos = UIFactory.CreatePanel(meter.Track.transform, "MicroTickPos",
                new Vector2(1f * s, 2.2f * s), Vector2.zero, tickCol);
            meter.MicroTickPosRt = mtPos.GetComponent<RectTransform>();

            // 75% 微刻线 (-75% / +75%, 2.2px)
            GameObject mt75Neg = UIFactory.CreatePanel(meter.Track.transform, "MicroTick75Neg",
                new Vector2(1f * s, 2.2f * s), Vector2.zero, tickCol);
            meter.MicroTickNeg75Rt = mt75Neg.GetComponent<RectTransform>();

            GameObject mt75Pos = UIFactory.CreatePanel(meter.Track.transform, "MicroTick75Pos",
                new Vector2(1f * s, 2.2f * s), Vector2.zero, tickCol);
            meter.MicroTickPos75Rt = mt75Pos.GetComponent<RectTransform>();

            // 零位中心基准中轴线 (6.5px 高度，1.5px 宽)
            GameObject cTick = UIFactory.CreatePanel(meter.Track.transform, "CenterTick",
                new Vector2(1.5f * s, 6.5f * s), Vector2.zero, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            meter.CenterTickRt = cTick.GetComponent<RectTransform>();

            // 双向动态偏转填充条 (HUD Beam, 高 3.5px)
            GameObject fill = UIFactory.CreatePanel(meter.Track.transform, "Fill",
                new Vector2(0f, 3.5f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            meter.FillRt = fill.GetComponent<RectTransform>();
            meter.FillRt.anchorMin = new Vector2(0.5f, 0.5f);
            meter.FillRt.anchorMax = new Vector2(0.5f, 0.5f);
            meter.FillRt.pivot = new Vector2(0.5f, 0.5f);
            meter.FillImg = fill.GetComponent<Image>();

            // 射束先端边缘亮头 (Edge Cap, 高 3.5px 与光柱同高, 宽 1.5px)
            GameObject ptr = UIFactory.CreatePanel(meter.Track.transform, "PointerPip",
                new Vector2(1.5f * s, 3.5f * s), Vector2.zero, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            meter.PointerRt = ptr.GetComponent<RectTransform>();
            meter.PointerImg = ptr.GetComponent<Image>();

            // 机械配平游标 (Trim Notch, 独立上置于顶导轨之上，高 3px 宽 2px 警示黄色)
            GameObject trim = UIFactory.CreatePanel(meter.Track.transform, "TrimPip",
                new Vector2(2f * s, 3f * s), new Vector2(0f, 2.5f * s), style.GetMeterColor(MeterStyleRole.Warning, theme));
            meter.TrimRt = trim.GetComponent<RectTransform>();
            meter.TrimImg = trim.GetComponent<Image>();

            // 偏转读数百分比 (+24%)
            meter.ValText = UIFactory.CreateText(rootGo.transform, "Val", "0%",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            meter.ValText.fontStyle = FontStyle.Bold;
            meter.ValText.horizontalOverflow = HorizontalWrapMode.Overflow;
            meter.ValText.verticalOverflow = VerticalWrapMode.Overflow;
            meter.ValRt = meter.ValText.GetComponent<RectTransform>();
            meter.ValRt.sizeDelta = new Vector2(34f * s, 14f * s);

            return meter;
        }

        // ==========================================
        // 动态长宽比排版自适应 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayout(pixelSize.x, pixelSize.y);
        }

        private void ApplyLayout(float width, float height)
        {
            float s = CurrentDpiScale;
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float margin = 8f * s;
            float contentW = width - margin * 2f;

            // 0. 四角 DZUS 螺栓排版
            if (_chassisScrews != null && _chassisScrews.Length == 4)
            {
                float screwInset = 4.5f * s;
                _chassisScrews[0].GetComponent<RectTransform>().anchoredPosition = new Vector2(-halfW + screwInset, halfH - screwInset);
                _chassisScrews[1].GetComponent<RectTransform>().anchoredPosition = new Vector2(halfW - screwInset, halfH - screwInset);
                _chassisScrews[2].GetComponent<RectTransform>().anchoredPosition = new Vector2(-halfW + screwInset, -halfH + screwInset);
                _chassisScrews[3].GetComponent<RectTransform>().anchoredPosition = new Vector2(halfW - screwInset, -halfH + screwInset);
            }

            // 1. 顶栏安全联动总成排版
            float topRowY = halfH - 13f * s;
            float fireW = 46f * s;
            float lockW = 42f * s;
            float titleMaxW = Mathf.Max(50f * s, contentW - fireW - lockW - 16f * s);

            if (Title != null && Title.RootGameObject != null)
            {
                RectTransform tRt = Title.RootGameObject.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchorMin = new Vector2(0.5f, 0.5f);
                    tRt.anchorMax = new Vector2(0.5f, 0.5f);
                    tRt.pivot = new Vector2(0f, 0.5f);
                    tRt.sizeDelta = new Vector2(titleMaxW, 14f * s);
                    tRt.anchoredPosition = new Vector2(-halfW + margin + 11f * s, topRowY);
                }
            }

            if (_sysStatusLed != null)
            {
                RectTransform ledRt = _sysStatusLed.GetComponent<RectTransform>();
                ledRt.anchoredPosition = new Vector2(-halfW + margin + 3f * s, topRowY);
            }

            if (_fireBtn != null)
            {
                RectTransform fRt = _fireBtn.GetComponent<RectTransform>();
                fRt.sizeDelta = new Vector2(fireW, 16f * s);
                fRt.anchoredPosition = new Vector2(halfW - margin - fireW * 0.5f, topRowY);
            }

            if (_lockBtn != null)
            {
                RectTransform lRt = _lockBtn.GetComponent<RectTransform>();
                lRt.sizeDelta = new Vector2(lockW, 16f * s);
                lRt.anchoredPosition = new Vector2(halfW - margin - fireW - 4f * s - lockW * 0.5f, topRowY);
            }

            if (_topDivider != null)
            {
                RectTransform topDivRt = _topDivider.rectTransform;
                topDivRt.sizeDelta = new Vector2(contentW, 1f * s);
                topDivRt.anchoredPosition = new Vector2(0f, topRowY - 10f * s);
            }

            // 2. 分级遥测中枢凹槽窗排版
            float bayH = 30f * s;
            float bayY = topRowY - 26f * s;
            if (_stageBayPanel != null)
            {
                RectTransform bayRt = _stageBayPanel.GetComponent<RectTransform>();
                bayRt.sizeDelta = new Vector2(contentW, bayH);
                bayRt.anchoredPosition = new Vector2(0f, bayY);
            }

            if (_stageNumBox != null)
            {
                RectTransform nbRt = _stageNumBox.GetComponent<RectTransform>();
                nbRt.anchoredPosition = new Vector2(-contentW * 0.5f + 21f * s, 0f);
            }

            if (_stageBayDivider != null)
            {
                RectTransform divRt = _stageBayDivider.rectTransform;
                divRt.anchoredPosition = new Vector2(-contentW * 0.5f + 43f * s, 0f);
            }

            float telemStartX = -contentW * 0.5f + 48f * s;
            float telemW = contentW - 52f * s;
            if (_stageDvText != null)
            {
                RectTransform dvRt = _stageDvText.rectTransform;
                dvRt.sizeDelta = new Vector2(telemW, 14f * s);
                dvRt.anchoredPosition = new Vector2(telemStartX + telemW * 0.5f, 5.5f * s);
            }

            if (_stageTwrEngText != null)
            {
                RectTransform twrRt = _stageTwrEngText.rectTransform;
                twrRt.sizeDelta = new Vector2(telemW, 12f * s);
                twrRt.anchoredPosition = new Vector2(telemStartX + telemW * 0.5f, -5.5f * s);
            }

            // 3. 动态纵向空间与三轴姿态操纵量仪表区排版
            float bottomY = -halfH + 12f * s;
            float bottomDivY = bottomY + 11f * s;
            float propCalibY = bottomDivY + 7f * s;
            float propBarY = propCalibY + 8f * s;
            float propHeaderY = propBarY + 9f * s;

            float axisBayTopY = bayY - bayH * 0.5f - 11f * s;
            float axisBayBottomY = propHeaderY + 9f * s;
            float axisSpacing = Mathf.Clamp((axisBayTopY - axisBayBottomY) * 0.5f, 15f * s, 21f * s);

            float labelW = 38f * s;
            float valW = 34f * s;
            float trackW = Mathf.Max(60f * s, contentW - labelW - valW - 8f * s);
            _cachedTrackWidth.Update(trackW / (s > 0f ? s : 1f));

            LayoutAxisMeter(_pitchMeter, axisBayTopY, halfW, margin, labelW, trackW, valW, s);
            LayoutAxisMeter(_rollMeter, axisBayTopY - axisSpacing, halfW, margin, labelW, trackW, valW, s);
            LayoutAxisMeter(_yawMeter, axisBayTopY - axisSpacing * 2f, halfW, margin, labelW, trackW, valW, s);

            // 4. 分级推进剂计量槽排版
            if (_propNameText != null)
            {
                RectTransform pNameRt = _propNameText.rectTransform;
                pNameRt.sizeDelta = new Vector2(contentW * 0.6f, 11f * s);
                pNameRt.anchoredPosition = new Vector2(-halfW + margin + contentW * 0.3f, propHeaderY);
            }

            if (_propPctText != null)
            {
                RectTransform pctRt = _propPctText.rectTransform;
                pctRt.sizeDelta = new Vector2(contentW * 0.38f, 11f * s);
                pctRt.anchoredPosition = new Vector2(halfW - margin - contentW * 0.19f, propHeaderY);
            }

            if (_propTrackPanel != null)
            {
                RectTransform trkRt = _propTrackPanel.GetComponent<RectTransform>();
                trkRt.sizeDelta = new Vector2(contentW, 6f * s);
                trkRt.anchoredPosition = new Vector2(0f, propBarY);

                float halfCw = contentW * 0.5f;
                if (_propTicks != null)
                {
                    for (int i = 0; i < _propTicks.Length; i++)
                    {
                        if (_propTicks[i] != null)
                        {
                            float fraction = (i + 1) * 0.1f;
                            float tickX = -halfCw + contentW * fraction;
                            _propTicks[i].anchoredPosition = new Vector2(tickX, 0f);
                        }
                    }
                }
            }

            // E / ¼ / ½ / ¾ / F 标度排版
            if (_propEText != null)
            {
                RectTransform eRt = _propEText.rectTransform;
                eRt.sizeDelta = new Vector2(16f * s, 8f * s);
                eRt.anchoredPosition = new Vector2(-halfW + margin + 8f * s, propCalibY);
            }
            if (_propQuarterText != null)
            {
                RectTransform qRt = _propQuarterText.rectTransform;
                qRt.sizeDelta = new Vector2(16f * s, 8f * s);
                qRt.anchoredPosition = new Vector2(-contentW * 0.25f, propCalibY);
            }
            if (_propHalfText != null)
            {
                RectTransform hRt = _propHalfText.rectTransform;
                hRt.sizeDelta = new Vector2(16f * s, 8f * s);
                hRt.anchoredPosition = new Vector2(0f, propCalibY);
            }
            if (_propThreeQuarterText != null)
            {
                RectTransform tqRt = _propThreeQuarterText.rectTransform;
                tqRt.sizeDelta = new Vector2(16f * s, 8f * s);
                tqRt.anchoredPosition = new Vector2(contentW * 0.25f, propCalibY);
            }
            if (_propFText != null)
            {
                RectTransform fRt = _propFText.rectTransform;
                fRt.sizeDelta = new Vector2(16f * s, 8f * s);
                fRt.anchoredPosition = new Vector2(halfW - margin - 8f * s, propCalibY);
            }

            if (_bottomDivider != null)
            {
                RectTransform botDivRt = _bottomDivider.rectTransform;
                botDivRt.sizeDelta = new Vector2(contentW, 1f * s);
                botDivRt.anchoredPosition = new Vector2(0f, bottomDivY);
            }

            // 5. 底部按键三等分排版 (MFD OSB Softkeys)
            float btnSpacing = 3f * s;
            float singleBtnW = (contentW - btnSpacing * 2f) / 3f;
            Vector2 singleBtnSize = new Vector2(singleBtnW, 15f * s);

            if (_precBtn != null)
            {
                RectTransform pRt = _precBtn.GetComponent<RectTransform>();
                pRt.sizeDelta = singleBtnSize;
                pRt.anchoredPosition = new Vector2(-halfW + margin + singleBtnW * 0.5f, bottomY);
                if (_precLed != null) _precLed.rectTransform.sizeDelta = new Vector2(singleBtnW, 2f * s);
                if (_precText != null) _precText.rectTransform.sizeDelta = new Vector2(singleBtnW, singleBtnSize.y - 2f * s);
            }

            if (_modeBtn != null)
            {
                RectTransform mRt = _modeBtn.GetComponent<RectTransform>();
                mRt.sizeDelta = singleBtnSize;
                mRt.anchoredPosition = new Vector2(0f, bottomY);
                if (_modeLed != null) _modeLed.rectTransform.sizeDelta = new Vector2(singleBtnW, 2f * s);
                if (_modeText != null) _modeText.rectTransform.sizeDelta = new Vector2(singleBtnW, singleBtnSize.y - 2f * s);
            }

            if (_stockToggleBtn != null)
            {
                RectTransform sRt = _stockToggleBtn.GetComponent<RectTransform>();
                sRt.sizeDelta = singleBtnSize;
                sRt.anchoredPosition = new Vector2(halfW - margin - singleBtnW * 0.5f, bottomY);
                if (_stockToggleLed != null) _stockToggleLed.rectTransform.sizeDelta = new Vector2(singleBtnW, 2f * s);
                if (_stockToggleText != null) _stockToggleText.rectTransform.sizeDelta = new Vector2(singleBtnW, singleBtnSize.y - 2f * s);
            }
        }

        private void LayoutAxisMeter(AxisMeterUI meter, float yPos, float halfW, float margin, float labelW, float trackW, float valW, float s)
        {
            if (meter == null || meter.RootRt == null) return;
            meter.RootRt.anchoredPosition = new Vector2(0f, yPos);

            if (meter.LabelRt != null)
            {
                meter.LabelRt.sizeDelta = new Vector2(labelW, 12f * s);
                meter.LabelRt.anchoredPosition = new Vector2(-halfW + margin + labelW * 0.5f, 0f);
            }

            if (meter.TrackRt != null)
            {
                meter.TrackRt.sizeDelta = new Vector2(trackW, 5f * s);
                float trackCenterX = -halfW + margin + labelW + 4f * s + trackW * 0.5f;
                meter.TrackRt.anchoredPosition = new Vector2(trackCenterX, 0f);

                if (meter.RailTop != null)
                {
                    meter.RailTop.rectTransform.sizeDelta = new Vector2(trackW, 0.8f * s);
                    meter.RailTop.rectTransform.anchoredPosition = new Vector2(0f, 2.1f * s);
                }
                if (meter.RailBottom != null)
                {
                    meter.RailBottom.rectTransform.sizeDelta = new Vector2(trackW, 0.8f * s);
                    meter.RailBottom.rectTransform.anchoredPosition = new Vector2(0f, -2.1f * s);
                }

                float halfTrack = trackW * 0.5f;
                if (meter.TickNegRt != null) meter.TickNegRt.anchoredPosition = new Vector2(-halfTrack, 0f);
                if (meter.TickPosRt != null) meter.TickPosRt.anchoredPosition = new Vector2(halfTrack, 0f);
                if (meter.SubTickNegRt != null) meter.SubTickNegRt.anchoredPosition = new Vector2(-halfTrack * 0.5f, 0f);
                if (meter.SubTickPosRt != null) meter.SubTickPosRt.anchoredPosition = new Vector2(halfTrack * 0.5f, 0f);
                if (meter.MicroTickNegRt != null) meter.MicroTickNegRt.anchoredPosition = new Vector2(-halfTrack * 0.25f, 0f);
                if (meter.MicroTickPosRt != null) meter.MicroTickPosRt.anchoredPosition = new Vector2(halfTrack * 0.25f, 0f);
                if (meter.MicroTickNeg75Rt != null) meter.MicroTickNeg75Rt.anchoredPosition = new Vector2(-halfTrack * 0.75f, 0f);
                if (meter.MicroTickPos75Rt != null) meter.MicroTickPos75Rt.anchoredPosition = new Vector2(halfTrack * 0.75f, 0f);
                if (meter.CenterTickRt != null) meter.CenterTickRt.anchoredPosition = Vector2.zero;
            }

            if (meter.ValRt != null)
            {
                meter.ValRt.sizeDelta = new Vector2(valW, 12f * s);
                meter.ValRt.anchoredPosition = new Vector2(halfW - margin - valW * 0.5f, 0f);
            }
        }

        private void OnToggleLock()
        {
            StockStageActionService.ToggleStagingLock();
            FlightTelemetryContext.Current?.ToggleStageLock();
        }

        private void OnFireStage()
        {
            _fireBtnRecoilTimer.Value = 0.20f;
            StockStageActionService.ActivateNextStage();
            FlightTelemetryContext.Current?.ActivateNextStage();
        }

        private void OnTogglePrecision()
        {
            FlightTelemetryContext.Current?.TogglePrecisionMode();
        }

        private void OnToggleMode()
        {
            FlightTelemetryContext.Current?.ToggleFlightMode();
        }

        private void OnToggleStockVisibility()
        {
            _stockHidden.Value = !_stockHidden.Value;
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden.Value);
            UpdateStockToggleButtonState();
        }

        private void UpdateStockToggleButtonState()
        {
            if (_stockToggleText != null)
            {
                string text = _stockHidden.Value ? "KSP HUD" : "● KSP HUD";
                SetTextIfChanged(_stockToggleText, text);
            }
            if (_cachedTheme != null)
            {
                ApplyText(_stockToggleText, _stockHidden.Value ? TextStyleRole.SecondaryValue : TextStyleRole.Accent, _cachedTheme);
                if (_stockToggleLed != null)
                {
                    _stockToggleLed.color = _stockHidden.Value 
                        ? WidgetStyleManager.Weighted(_cachedTheme.AccentSecondary, LineWeight.Ghost)
                        : _cachedTheme.AccentPrimary;
                }
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            // 按键微回弹动效
            float dt = context.DeltaTime;
            if (_fireBtnRecoilTimer.Value > 0f)
            {
                _fireBtnRecoilTimer.Value -= dt;
                float recoilScale = 1f - Mathf.Clamp01(_fireBtnRecoilTimer.Value * 0.4f);
                if (_fireBtn != null) _fireBtn.transform.localScale = new Vector3(recoilScale, recoilScale, 1f);
            }
            else if (_fireBtn != null && _fireBtn.transform.localScale.x < 0.999f)
            {
                _fireBtn.transform.localScale = Vector3.one;
            }
        }

        protected override void OnRenderState()
        {
            StageControlState snap = _logic.CurrentState;
            if (!snap.HasVessel) return;

            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 自适应排版重算 (增加尺寸脏检查，杜绝每帧重复 ApplyLayout)
            float currentW = RectTransform.rect.width > 10f ? RectTransform.rect.width : BaseSize.x * s;
            float currentH = RectTransform.rect.height > 10f ? RectTransform.rect.height : BaseSize.y * s;
            bool layoutDirty = _lastLayoutW.Update(currentW) | _lastLayoutH.Update(currentH);
            if (layoutDirty)
            {
                ApplyLayout(currentW, currentH);
            }

            // 2. 分级安全锁与就绪联动 (DirtyField 守卫)
            if (_dirtyLocked.Update(snap.IsLocked))
            {
                bool isLocked = _dirtyLocked.Value;
                string lockLabel = isLocked
                    ? I18n.Tr("WIDGET_STAGE_LOCKED", "SAFE")
                    : I18n.Tr("WIDGET_ALERT_ARMED", "ARMED");
                SetTextIfChanged(_lockBtnText, lockLabel);

                if (isLocked)
                {
                    _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    ApplyText(_lockBtnText, TextStyleRole.Danger, theme);
                    if (_lockBtnLed != null) _lockBtnLed.color = theme.DangerColor;

                    _fireBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    ApplyText(_fireBtnText, TextStyleRole.SecondaryValue, theme);
                    if (_fireBtnOutline != null) _fireBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

                    if (_sysStatusLedImg != null) _sysStatusLedImg.color = theme.DangerColor;
                }
                else
                {
                    _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    ApplyText(_lockBtnText, TextStyleRole.Accent, theme);
                    if (_lockBtnLed != null) _lockBtnLed.color = theme.AccentPrimary;

                    _fireBtnBg.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.12f);
                    ApplyText(_fireBtnText, TextStyleRole.PrimaryValue, theme);
                    if (_fireBtnOutline != null) _fireBtnOutline.effectColor = style.GetMeterColor(MeterStyleRole.Warning, theme);

                    if (_sysStatusLedImg != null) _sysStatusLedImg.color = theme.AccentPrimary;
                }
            }

            // 3. 分级数字读数与性能参数 (DirtyField 守卫)
            if (_dirtyStageNum.Update(snap.CurrentStage))
            {
                SetTextIfChanged(_stageNumText, snap.CurrentStage.ToString("D2"));
            }

            if (_dirtyStageDv.Update(snap.StageDeltaV))
            {
                double dv = _dirtyStageDv.Value;
                string dvStr = dv > 0.1 ? $"Δv  {dv:N0} m/s" : "Δv  0 m/s";
                SetTextIfChanged(_stageDvText, dvStr);
            }

            int burnSec = Mathf.Max(0, (int)snap.StageBurnTime);
            bool burnDirty = _dirtyBurnSec.Update(burnSec);
            bool twrDirty = _dirtyTwr.Update(snap.Twr);
            bool engDirty = _dirtyActiveEngines.Update(snap.ActiveEngines);
            if (burnDirty || twrDirty || engDirty)
            {
                int m = burnSec / 60;
                int sec = burnSec % 60;
                float twr = _dirtyTwr.Value;
                int eng = _dirtyActiveEngines.Value;
                string twrStr = twr > 0.01f 
                    ? $"T {m:00}:{sec:00} │ {twr:F2} TWR │ {eng} ENG" 
                    : $"T {m:00}:{sec:00} │ {eng} ENG";
                SetTextIfChanged(_stageTwrEngText, twrStr);

                if (_stageAccentBar != null)
                {
                    _stageAccentBar.color = eng > 0 ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }

            // 4. 三轴舵面偏转与配平
            float curTrackW = _cachedTrackWidth.Value * s;
            UpdateAxisVisuals(_pitchMeter, snap.PitchInput, snap.PitchTrim, curTrackW, s, theme, _lastPitchPct);
            UpdateAxisVisuals(_rollMeter, snap.RollInput, snap.RollTrim, curTrackW, s, theme, _lastRollPct);
            UpdateAxisVisuals(_yawMeter, snap.YawInput, snap.YawTrim, curTrackW, s, theme, _lastYawPct);

            // 5. 分级推进剂指示条 (100% 语义驱动)
            float targetPropFrac = snap.StagePropellantFraction;
            float nextProp = Mathf.Lerp(_currentPropFrac.Value < 0f ? targetPropFrac : _currentPropFrac.Value, targetPropFrac, 0.25f);
            _currentPropFrac.Update(nextProp);

            if (_dirtyPropName.Update(snap.PropName))
            {
                SetTextIfChanged(_propNameText, _dirtyPropName.Value);
            }

            float contentW = currentW - 16f * s;
            int propPctInt = Mathf.Clamp(Mathf.RoundToInt(_currentPropFrac.Value * 100f), 0, 100);
            SetTextIfChanged(_propPctText, CacheManager.FastPercent(propPctInt));

            TextStyleRole pRole = (_currentPropFrac.Value > 0.25f) ? TextStyleRole.PrimaryValue : ((_currentPropFrac.Value > 0.10f) ? TextStyleRole.Warning : TextStyleRole.Danger);
            ApplyText(_propPctText, pRole, theme);

            if (_dirtyPropFill.Update(_currentPropFrac.Value) && _propFillRt != null)
            {
                _propFillRt.sizeDelta = new Vector2(contentW * _dirtyPropFill.Value, 4f * s);
            }
            if (_propFillImg != null)
            {
                if (_currentPropFrac.Value < 0.10f)
                {
                    // 临界低油量 1.5Hz 柔和脉冲
                    float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 9.4f);
                    _propFillImg.color = WidgetStyleManager.WithAlpha(theme.DangerColor, pulse);
                }
                else
                {
                    MeterStyleRole fillRole = (_currentPropFrac.Value > 0.25f) ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                    _propFillImg.color = style.GetMeterColor(fillRole, theme);
                }
            }

            // 6. 底部模式按键
            if (_dirtyPrec.Update(snap.IsPrecisionControl))
            {
                bool isPrec = _dirtyPrec.Value;
                string pStr = isPrec ? "● PREC" : "NORM";
                SetTextIfChanged(_precText, pStr);
                ApplyText(_precText, isPrec ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
                if (_precLed != null)
                {
                    _precLed.color = isPrec ? theme.WarningColor : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
                if (_precOutline != null)
                {
                    _precOutline.effectColor = isPrec ? WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }

            if (_dirtyDock.Update(snap.IsDockingMode))
            {
                bool isDock = _dirtyDock.Value;
                string mStr = isDock ? "● DCK" : "STG";
                SetTextIfChanged(_modeText, mStr);
                ApplyText(_modeText, isDock ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                if (_modeLed != null)
                {
                    _modeLed.color = isDock ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
                if (_modeOutline != null)
                {
                    _modeOutline.effectColor = isDock ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }
        }

        private void UpdateAxisVisuals(AxisMeterUI meter, float input, float trim, float trackW, float s, ThemeConfig theme, Cached<int> lastPct)
        {
            if (meter == null) return;
            float halfWidth = trackW * 0.5f;
            float clampedInput = Mathf.Clamp(input, -1f, 1f);

            int pct = Mathf.RoundToInt(clampedInput * 100f);
            if (lastPct.Update(pct))
            {
                float absInput = Mathf.Abs(clampedInput);
                float fillWidth = absInput * halfWidth;
                float fillCenterOffset = (clampedInput >= 0f) ? (fillWidth * 0.5f) : (-fillWidth * 0.5f);

                if (meter.FillRt != null)
                {
                    meter.FillRt.sizeDelta = new Vector2(fillWidth, 3.5f * s);
                    meter.FillRt.anchoredPosition = new Vector2(fillCenterOffset, 0f);
                }

                if (meter.FillImg != null)
                {
                    MeterStyleRole fillRole = Mathf.Abs(pct) > 80 ? MeterStyleRole.Warning : MeterStyleRole.Primary;
                    meter.FillImg.color = WidgetStyleManager.Instance.GetMeterColor(fillRole, theme);
                }

                if (meter.PointerRt != null)
                {
                    if (absInput < 0.01f)
                    {
                        meter.PointerRt.sizeDelta = Vector2.zero;
                    }
                    else
                    {
                        meter.PointerRt.sizeDelta = new Vector2(1.5f * s, 3.5f * s);
                        meter.PointerRt.anchoredPosition = new Vector2(clampedInput * halfWidth, 0f);
                    }
                }

                if (meter.ValText != null)
                {
                    string str = (pct > 0) ? "+" + CacheManager.FastPercent(pct) : (pct == 0 ? "0%" : CacheManager.FastInt(pct) + "%");
                    SetTextIfChanged(meter.ValText, str);
                    TextStyleRole role = Mathf.Abs(pct) > 80 ? TextStyleRole.Warning : (Mathf.Abs(pct) > 3 ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue);
                    ApplyText(meter.ValText, role, theme);
                }
            }

            if (meter.TrimRt != null && meter.LastTrim.Update(trim))
            {
                float clampedTrim = Mathf.Clamp(trim, -1f, 1f);
                meter.TrimRt.sizeDelta = new Vector2(2f * s, 3f * s);
                meter.TrimRt.anchoredPosition = new Vector2(clampedTrim * halfWidth, 2.5f * s);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            _cachedTheme = theme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // DZUS 螺栓
            if (_chassisScrews != null)
            {
                Color screwCol = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                Color slotCol = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                for (int i = 0; i < _chassisScrews.Length; i++)
                {
                    if (_chassisScrews[i] != null) _chassisScrews[i].GetComponent<Image>().color = screwCol;
                    if (_chassisScrewSlots != null && _chassisScrewSlots[i] != null) _chassisScrewSlots[i].GetComponent<Image>().color = slotCol;
                }
            }

            // 顶栏
            if (Title != null)
            {
                Title.Text = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL"));
                Title.SetRole(TextStyleRole.Cardinal);
            }

            bool locked = _dirtyLocked.Value;
            if (_sysStatusLedImg != null) _sysStatusLedImg.color = locked ? theme.DangerColor : theme.AccentPrimary;

            if (_lockBtnBg != null) _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_lockBtnOutline != null) _lockBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_lockBtnLed != null) _lockBtnLed.color = locked ? theme.DangerColor : theme.AccentPrimary;
            if (_lockBtnText != null) ApplyText(_lockBtnText, locked ? TextStyleRole.Danger : TextStyleRole.Accent, theme);

            if (_fireBtnBg != null) _fireBtnBg.color = locked ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : WidgetStyleManager.WithAlpha(theme.WarningColor, 0.12f);
            if (_fireBtnOutline != null) _fireBtnOutline.effectColor = locked ? WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost) : style.GetMeterColor(MeterStyleRole.Warning, theme);
            if (_fireBtnText != null) ApplyText(_fireBtnText, locked ? TextStyleRole.SecondaryValue : TextStyleRole.PrimaryValue, theme);

            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 分级中枢凹槽窗
            if (_stageBayBg != null) _stageBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageBayOutline != null) _stageBayOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_stageNumBoxBg != null) _stageNumBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_stageBayDivider != null) _stageBayDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle);
            if (_stageLabelText != null) ApplyText(_stageLabelText, TextStyleRole.SecondaryValue, theme);
            if (_stageNumText != null) ApplyText(_stageNumText, TextStyleRole.PrimaryValue, theme);
            if (_stageDvText != null) ApplyText(_stageDvText, TextStyleRole.PrimaryValue, theme);
            if (_stageTwrEngText != null) ApplyText(_stageTwrEngText, TextStyleRole.SecondaryValue, theme);

            // 三轴仪表着色
            ApplyThemeToAxisMeter(_pitchMeter, theme);
            ApplyThemeToAxisMeter(_rollMeter, theme);
            ApplyThemeToAxisMeter(_yawMeter, theme);

            // 推进剂槽
            if (_propTagBgImg != null) _propTagBgImg.color = Color.clear;
            if (_propTagOutline != null) _propTagOutline.effectColor = Color.clear;
            if (_propNameText != null) ApplyText(_propNameText, TextStyleRole.SecondaryValue, theme);
            if (_propPctText != null) ApplyText(_propPctText, TextStyleRole.PrimaryValue, theme);
            if (_propTrackBg != null) _propTrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_propTrackOutline != null) _propTrackOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_propFillImg != null) _propFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            if (_propTicks != null)
            {
                Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Subtle);
                for (int i = 0; i < _propTicks.Length; i++)
                {
                    if (_propTicks[i] != null)
                    {
                        Color c = (i == 1) ? theme.WarningColor : tickCol;
                        _propTicks[i].GetComponent<Image>().color = c;
                    }
                }
            }

            if (_propEText != null) ApplyText(_propEText, TextStyleRole.SecondaryValue, theme);
            if (_propQuarterText != null) ApplyText(_propQuarterText, TextStyleRole.SecondaryValue, theme);
            if (_propHalfText != null) ApplyText(_propHalfText, TextStyleRole.SecondaryValue, theme);
            if (_propThreeQuarterText != null) ApplyText(_propThreeQuarterText, TextStyleRole.SecondaryValue, theme);
            if (_propFText != null) ApplyText(_propFText, TextStyleRole.SecondaryValue, theme);

            // 底部按键
            bool prec = _dirtyPrec.Value;
            if (_precImg != null) _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_precOutline != null) _precOutline.effectColor = prec ? WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_precLed != null) _precLed.color = prec ? theme.WarningColor : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_precText != null) ApplyText(_precText, prec ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);

            bool dock = _dirtyDock.Value;
            if (_modeImg != null) _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_modeOutline != null) _modeOutline.effectColor = dock ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_modeLed != null) _modeLed.color = dock ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_modeText != null) ApplyText(_modeText, dock ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);

            if (_stockToggleImg != null) _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockToggleOutline != null) _stockToggleOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            UpdateStockToggleButtonState();
        }

        private void ApplyThemeToAxisMeter(AxisMeterUI meter, ThemeConfig theme)
        {
            if (meter == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            if (meter.Label != null) ApplyText(meter.Label, TextStyleRole.SecondaryValue, theme);

            if (meter.TrackBg != null) meter.TrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (meter.RailTop != null) meter.RailTop.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (meter.RailBottom != null) meter.RailBottom.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (meter.FillImg != null) meter.FillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
            if (meter.PointerImg != null) meter.PointerImg.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            if (meter.TrimImg != null) meter.TrimImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            if (meter.ValText != null) ApplyText(meter.ValText, TextStyleRole.PrimaryValue, theme);
        }

        protected override void OnDestroy()
        {
            if (_lockBtn != null) _lockBtn.onClick.RemoveAllListeners();
            if (_fireBtn != null) _fireBtn.onClick.RemoveAllListeners();
            if (_precBtn != null) _precBtn.onClick.RemoveAllListeners();
            if (_modeBtn != null) _modeBtn.onClick.RemoveAllListeners();
            if (_stockToggleBtn != null) _stockToggleBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
