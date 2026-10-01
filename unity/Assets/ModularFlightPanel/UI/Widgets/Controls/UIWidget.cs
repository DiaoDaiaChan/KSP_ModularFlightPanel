using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Controls
{
    /// <summary>
    /// 全局 UI 组件管理与航电作动中枢 (Modular Avionics Flight & UI Control Hub - core.ui_widget)
    /// 遵循 MFP-SPEC-001..012 航电规范标准，三位一体全功能航电中枢：
    /// 1. HUD 界面管家：实时组件编排、快速检索/分类、单项锁定/定位闪烁/出厂复位、预设与量纲制式全局联动
    /// 2. 飞控作动总控：RCS/SAS、起落架、照明、刹车、分级锁、精密操控、紧急逃逸、SAS 9 向对齐罗盘、AG 1~10 动作组
    /// 3. 全机工况监控：电力总线、推进与燃烧工况、通信链路质量与轨道要素实时监视
    /// 4. 0 颜色字面量、0 场景查询、纯 C# 业务解耦大脑 (UIWidgetLogic) 与零 GC 不可变遥测快照
    /// </summary>
    public struct UIWidgetState : IEquatable<UIWidgetState>
    {
        public bool HasStats;
        public int TotalWidgets;
        public int ActiveWidgets;
        public int LockedWidgets;

        // 飞行控制与状态机
        public bool HasVessel;
        public bool IsRcsOn;
        public bool IsSasOn;
        public bool IsGearOn;
        public bool IsLightOn;
        public bool IsBrakesOn;
        public bool IsStageLocked;
        public bool IsPrecisionMode;
        public FlightSASMode CurrentSASMode;
        public SpeedDisplayMode CurrentSpeedMode;
        public int TimeWarpRateIndex;
        public bool IsPaused;
        public uint ActionGroupMask;

        // 综合工况遥测
        public double ElectricCharge;
        public double MaxElectricCharge;
        public float EcPercent;
        public double NetEcRate;
        public double SolarPower;
        public int CurrentStage;
        public double StageDeltaV;
        public double TotalDeltaV;
        public double Twr;
        public float Throttle;
        public double StageBurnTime;
        public float CommSignal;
        public bool IsConnected;
        public double Apoapsis;
        public double Periapsis;
        public double TimeToAp;
        public double TimeToPe;
        public double Inclination;
        public double Eccentricity;

        public bool Equals(UIWidgetState other)
        {
            return HasStats == other.HasStats &&
                   TotalWidgets == other.TotalWidgets &&
                   ActiveWidgets == other.ActiveWidgets &&
                   LockedWidgets == other.LockedWidgets &&
                   HasVessel == other.HasVessel &&
                   IsRcsOn == other.IsRcsOn &&
                   IsSasOn == other.IsSasOn &&
                   IsGearOn == other.IsGearOn &&
                   IsLightOn == other.IsLightOn &&
                   IsBrakesOn == other.IsBrakesOn &&
                   IsStageLocked == other.IsStageLocked &&
                   IsPrecisionMode == other.IsPrecisionMode &&
                   CurrentSASMode == other.CurrentSASMode &&
                   CurrentSpeedMode == other.CurrentSpeedMode &&
                   TimeWarpRateIndex == other.TimeWarpRateIndex &&
                   IsPaused == other.IsPaused &&
                   ActionGroupMask == other.ActionGroupMask &&
                   Math.Abs(EcPercent - other.EcPercent) < 0.001f &&
                   Math.Abs(Throttle - other.Throttle) < 0.001f &&
                   Math.Abs(CommSignal - other.CommSignal) < 0.001f &&
                   IsConnected == other.IsConnected;
        }

        public override bool Equals(object obj) => obj is UIWidgetState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasStats.GetHashCode();
                hash = (hash * 397) ^ TotalWidgets;
                hash = (hash * 397) ^ ActiveWidgets;
                hash = (hash * 397) ^ LockedWidgets;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ IsRcsOn.GetHashCode();
                hash = (hash * 397) ^ IsSasOn.GetHashCode();
                hash = (hash * 397) ^ ((int)CurrentSASMode * 397);
                hash = (hash * 397) ^ (int)ActionGroupMask;
                return hash;
            }
        }
    }

    /// <summary>
    /// UI 航电控制中枢纯 C# 业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class UIWidgetLogic : WidgetLogic<UIWidgetState>
    {
        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            var widgets = WidgetLayoutManager.Instance?.CurrentLayout?.Widgets;
            int total = widgets != null ? widgets.Count : 0;
            int active = 0;
            int locked = 0;
            if (widgets != null)
            {
                for (int i = 0; i < total; i++)
                {
                    if (widgets[i].IsEnabled) active++;
                    if (widgets[i].IsLocked) locked++;
                }
            }

            bool hasVessel = telemetry != null && telemetry.HasVessel;
            bool rcs = hasVessel && telemetry.IsRCSEnabled;
            bool sas = hasVessel && telemetry.IsSASEnabled;
            FlightSASMode sasMode = hasVessel ? telemetry.CurrentSASMode : FlightSASMode.StabilityAssist;
            SpeedDisplayMode spdMode = hasVessel ? telemetry.CurrentSpeedMode : SpeedDisplayMode.Orbit;
            bool stageLocked = hasVessel && telemetry.IsStageLocked;
            bool precision = hasVessel && telemetry.IsPrecisionControl;
            int warpIdx = hasVessel ? telemetry.TimeWarpRateIndex : 0;
            bool paused = hasVessel && telemetry.IsGamePaused;

            bool gear = false;
            bool light = false;
            bool brakes = false;
            uint agMask = 0;

#if !HEADLESS && !UNITY_EDITOR
            if (hasVessel && FlightGlobals.ready && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.ActionGroups != null)
            {
                var ag = FlightGlobals.ActiveVessel.ActionGroups;
                gear = ag[KSPActionGroup.Gear];
                light = ag[KSPActionGroup.Light];
                brakes = ag[KSPActionGroup.Brakes];
                if (ag[KSPActionGroup.Custom01]) agMask |= (1 << 0);
                if (ag[KSPActionGroup.Custom02]) agMask |= (1 << 1);
                if (ag[KSPActionGroup.Custom03]) agMask |= (1 << 2);
                if (ag[KSPActionGroup.Custom04]) agMask |= (1 << 3);
                if (ag[KSPActionGroup.Custom05]) agMask |= (1 << 4);
                if (ag[KSPActionGroup.Custom06]) agMask |= (1 << 5);
                if (ag[KSPActionGroup.Custom07]) agMask |= (1 << 6);
                if (ag[KSPActionGroup.Custom08]) agMask |= (1 << 7);
                if (ag[KSPActionGroup.Custom09]) agMask |= (1 << 8);
                if (ag[KSPActionGroup.Custom10]) agMask |= (1 << 9);
            }
#endif

            CurrentState = new UIWidgetState
            {
                HasStats = true,
                TotalWidgets = total,
                ActiveWidgets = active,
                LockedWidgets = locked,
                HasVessel = hasVessel,
                IsRcsOn = rcs,
                IsSasOn = sas,
                IsGearOn = gear,
                IsLightOn = light,
                IsBrakesOn = brakes,
                IsStageLocked = stageLocked,
                IsPrecisionMode = precision,
                CurrentSASMode = sasMode,
                CurrentSpeedMode = spdMode,
                TimeWarpRateIndex = warpIdx,
                IsPaused = paused,
                ActionGroupMask = agMask,

                ElectricCharge = hasVessel ? telemetry.ElectricCharge : 0.0,
                MaxElectricCharge = hasVessel ? telemetry.MaxElectricCharge : 0.0,
                EcPercent = hasVessel ? (float)telemetry.EcPercent : 0f,
                NetEcRate = hasVessel ? telemetry.NetEcRate : 0.0,
                SolarPower = hasVessel ? telemetry.SolarPower : 0.0,
                CurrentStage = hasVessel ? telemetry.CurrentStage : 0,
                StageDeltaV = hasVessel ? telemetry.StageDeltaV : 0.0,
                TotalDeltaV = hasVessel ? telemetry.TotalDeltaV : 0.0,
                Twr = hasVessel ? telemetry.TWR : 0.0,
                Throttle = hasVessel ? telemetry.Throttle : 0f,
                StageBurnTime = hasVessel ? telemetry.StageBurnTime : 0.0,
                CommSignal = hasVessel ? (float)telemetry.CommSignal : 0f,
                IsConnected = hasVessel && telemetry.IsConnected,
                Apoapsis = hasVessel ? telemetry.Apoapsis : 0.0,
                Periapsis = hasVessel ? telemetry.Periapsis : 0.0,
                TimeToAp = hasVessel ? telemetry.TimeToAp : 0.0,
                TimeToPe = hasVessel ? telemetry.TimeToPe : 0.0,
                Inclination = hasVessel ? telemetry.Inclination : 0.0,
                Eccentricity = hasVessel ? telemetry.Eccentricity : 0.0
            };
        }
    }

    [FlightWidget("ui_widget", "ui_manager", "dock_manager", Category = WidgetCategory.Controls,
        DisplayName = "UI 航电控制中枢",
        Description = "机载多模态航电中枢：HUD组件编排/锁定/定位、全功能飞行作动总控、动作组触控与综合遥测工况大盘。",
        DefaultWidgetId = "core.ui_widget", DefaultX = 380f, DefaultY = 0f, IsSingleton = true,
        ExactIds = new[] { "core.ui_widget", "custom.ui_widget" })]
    public class UIWidget : BaseFlightWidget
    {
        public static UIWidget Instance { get; private set; }

        private readonly UIWidgetLogic _logic = new UIWidgetLogic();
        protected override IWidgetLogic LogicCore => _logic;

        public override Vector2 BaseSize => new Vector2(300f, 380f);
        protected override bool AutoCreateCardFrame => true;

        public static Action OnRequestOpenWorkbench;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;
        public override bool IsInteractive => true;

        // UI 视图容器
        private Image _bgImage;
        private Outline _bgOutline;

        private GameObject _headerRoot;
        private Text _titleText;
        private Text _statusBadge;
        private Button _dragModeBtn;
        private Image _dragModeBtnImg;
        private Text _dragModeBtnText;
        private Button _workbenchBtn;
        private Text _workbenchBtnText;
        private Button _collapseBtn;
        private Text _collapseBtnText;

        // 标签导航栏
        private GameObject _tabBarRoot;
        private Button _tabBtnHud;
        private Image _tabBtnHudImg;
        private Text _tabBtnHudText;
        private Button _tabBtnFlight;
        private Image _tabBtnFlightImg;
        private Text _tabBtnFlightText;
        private Button _tabBtnTelemetry;
        private Image _tabBtnTelemetryImg;
        private Text _tabBtnTelemetryText;
        private int _activeTab = 0; // 0=HUD, 1=Flight, 2=Telemetry

        // Tab 0: HUD 界面管家面板
        private GameObject _tabHudRoot;
        private Button _presetQuickBtn;
        private Text _presetQuickText;
        private Button _unitQuickBtn;
        private Text _unitQuickText;
        private Button _themeQuickBtn;
        private Text _themeQuickText;

        private readonly List<Button> _categoryBtns = new List<Button>();
        private readonly List<Text> _categoryBtnTexts = new List<Text>();
        private int _selectedCategory = 0;
        private readonly string[] CategoryNames = new string[]
        {
            I18n.Tr("UIW_CAT_ALL", "全部"),
            I18n.Tr("UIW_CAT_GAUGES", "仪表"),
            I18n.Tr("UIW_CAT_SYSTEMS", "系统"),
            I18n.Tr("UIW_CAT_CONTROLS", "控制"),
            I18n.Tr("UIW_CAT_ACTIVE", "已开"),
            I18n.Tr("UIW_CAT_HIDDEN", "已隐")
        };

        private ScrollRect _scrollRect;
        private RectTransform _contentRt;
        private Image _scrollBg;

        private class WidgetRowView
        {
            public GameObject Root;
            public Image RowBg;
            public Button ToggleBtn;
            public Image ToggleLed;
            public Text TypeBadgeText;
            public Text NameText;
            public Button LockBtn;
            public Text LockBtnText;
            public Button LocateBtn;
            public Text LocateBtnText;
            public Button ResetBtn;
            public Text ResetBtnText;
            public string WidgetId;
            public bool IsEnabled;
            public bool IsLocked;
        }

        private readonly List<WidgetRowView> _rowViews = new List<WidgetRowView>();

        // Tab 1: 飞控作动面板
        private GameObject _tabFlightRoot;
        private Button _rcsBtn;
        private Image _rcsBtnImg;
        private Text _rcsBtnText;
        private Button _sasBtn;
        private Image _sasBtnImg;
        private Text _sasBtnText;
        private Button _gearBtn;
        private Image _gearBtnImg;
        private Text _gearBtnText;
        private Button _lightBtn;
        private Image _lightBtnImg;
        private Text _lightBtnText;
        private Button _brakesBtn;
        private Image _brakesBtnImg;
        private Text _brakesBtnText;
        private Button _stageLockBtn;
        private Image _stageLockBtnImg;
        private Text _stageLockBtnText;
        private Button _precisionBtn;
        private Image _precisionBtnImg;
        private Text _precisionBtnText;
        private Button _abortBtn;
        private Image _abortBtnImg;
        private Text _abortBtnText;

        // SAS 9 向对齐罗盘按钮
        private readonly Button[] _sasModeBtns = new Button[9];
        private readonly Image[] _sasModeBtnImgs = new Image[9];
        private readonly Text[] _sasModeBtnTexts = new Text[9];
        private static readonly FlightSASMode[] SasModes = new[]
        {
            FlightSASMode.Prograde, FlightSASMode.Retrograde, FlightSASMode.Normal,
            FlightSASMode.Antinormal, FlightSASMode.RadialOut, FlightSASMode.RadialIn,
            FlightSASMode.Target, FlightSASMode.AntiTarget, FlightSASMode.Maneuver
        };
        private static readonly string[] SasModeLabels = new[]
        {
            "PRO", "RET", "NORM", "ANT", "RAD+", "RAD-", "TGT", "A-TGT", "NODE"
        };

        // 动作组 1~10 按钮
        private readonly Button[] _agBtns = new Button[10];
        private readonly Image[] _agBtnImgs = new Image[10];
        private readonly Text[] _agBtnTexts = new Text[10];

        // 导航参考系与时间加速栏
        private Button _refFrameBtn;
        private Text _refFrameBtnText;
        private Button _warpDownBtn;
        private Button _pauseBtn;
        private Text _pauseBtnText;
        private Button _warpZeroBtn;
        private Button _warpUpBtn;

        // Tab 2: 综合工况面板
        private GameObject _tabTelemetryRoot;
        private Image _ecBarFill;
        private Text _ecText;
        private Text _netPowerText;
        private Image _throttleBarFill;
        private Text _stageDvText;
        private Text _twrBurnText;
        private Image _commBarFill;
        private Text _commStatusText;
        private Text _apPeText;
        private Text _timeToApPeText;
        private Text _incEccText;

        private bool _isCollapsed = false;
        private ThemeConfig _cachedTheme;

        // 私有托管死区缓存槽位 (MFP-SPEC-009)
        private readonly Cached<int> _lastTotalCount = new Cached<int>(-1);
        private readonly Cached<int> _lastActiveCount = new Cached<int>(-1);
        private readonly Cached<int> _lastLockedCount = new Cached<int>(-1);
        private readonly Cached<bool> _lastHasVessel = new Cached<bool>(false);
        private readonly Cached<bool> _lastRcs = new Cached<bool>(false);
        private readonly Cached<bool> _lastSas = new Cached<bool>(false);
        private readonly Cached<bool> _lastGear = new Cached<bool>(false);
        private readonly Cached<bool> _lastLight = new Cached<bool>(false);
        private readonly Cached<bool> _lastBrakes = new Cached<bool>(false);
        private readonly Cached<bool> _lastStageLocked = new Cached<bool>(false);
        private readonly Cached<bool> _lastPrecision = new Cached<bool>(false);
        private readonly Cached<FlightSASMode> _lastSasMode = new Cached<FlightSASMode>((FlightSASMode)(-1));
        private readonly Cached<string> _lastSpeedMode = new Cached<string>(string.Empty);
        private readonly Cached<int> _lastWarpRate = new Cached<int>(-1);
        private readonly Cached<bool> _lastPaused = new Cached<bool>(false);
        private readonly Cached<uint> _lastAgMask = new Cached<uint>(0);
        private readonly CachedFloat _lastEcPct = new CachedFloat(-1f);
        private readonly Cached<string> _lastEcVal = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastNetPower = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastStageDv = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastTwr = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastThrottle = new CachedFloat(-1f);
        private readonly CachedFloat _lastCommSignal = new CachedFloat(-1f);
        private readonly Cached<string> _lastApPe = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastApPeTime = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastOrbitElements = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastUnitModeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastThemeStr = new Cached<string>(string.Empty);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Instance = this;
            _cachedTheme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 包围盒与尺寸
            float baseW = 300f * s;
            float baseH = 380f * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            // 2. 底板与边框 (0 颜色字面量)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, _cachedTheme);

            // 3. 顶部标题栏 (Header, 34px)
            RectTransform headerRt = CreateContainer("Header", transform,
                new Vector2(0f, 34f * s), Vector2.zero);
            _headerRoot = headerRt.gameObject;
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);

            _titleText = UIFactory.CreateText(_headerRoot.transform, "Title", I18n.Tr("UIW_HEADER_TITLE", "❖ 航电控制中枢"), Mathf.RoundToInt(11f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.sizeDelta = new Vector2(120f * s, 26f * s);
            titleRt.anchoredPosition = new Vector2(10f * s, 0f);

            // 折叠/展开按钮
            _collapseBtn = CreateButtonElement(_headerRoot.transform, "CollapseBtn", "▼", new Vector2(20f * s, 20f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f * s, 0f), ToggleCollapse);
            _collapseBtnText = _collapseBtn.GetComponentInChildren<Text>();

            // 工作台按钮
            _workbenchBtn = CreateButtonElement(_headerRoot.transform, "WorkbenchBtn", I18n.Tr("UIW_BTN_SET", "工程"), new Vector2(26f * s, 20f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f * s, 0f), () => OnRequestOpenWorkbench?.Invoke());
            _workbenchBtnText = _workbenchBtn.GetComponentInChildren<Text>();

            // 自由拖拽编辑模式按钮
            _dragModeBtn = CreateButtonElement(_headerRoot.transform, "DragBtn", I18n.Tr("UIW_BTN_EDIT", "编辑"), new Vector2(30f * s, 20f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f * s, 0f), () =>
                {
                    WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                    UpdateDragButtonVisual();
                });
            _dragModeBtnImg = _dragModeBtn.GetComponent<Image>();
            _dragModeBtnText = _dragModeBtn.GetComponentInChildren<Text>();

            // 状态徽标 (0/0 ON)
            _statusBadge = UIFactory.CreateText(_headerRoot.transform, "StatusBadge", "0/0 ON", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme));
            RectTransform badgeRt = _statusBadge.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.sizeDelta = new Vector2(65f * s, 24f * s);
            badgeRt.anchoredPosition = new Vector2(-94f * s, 0f);

            // 4. 标签导航栏 (TabBar, 24px)
            RectTransform tabBarRt = CreateContainer("TabBar", transform,
                new Vector2(0f, 24f * s), new Vector2(0f, -34f * s));
            _tabBarRoot = tabBarRt.gameObject;
            tabBarRt.anchorMin = new Vector2(0f, 1f);
            tabBarRt.anchorMax = new Vector2(1f, 1f);
            tabBarRt.pivot = new Vector2(0.5f, 1f);

            float tabW = (baseW - 16f * s) / 3f;
            _tabBtnHud = CreateButtonElement(_tabBarRoot.transform, "Tab_Hud", I18n.Tr("UIW_TAB_HUD", "HUD 界面"),
                new Vector2(tabW - 2f * s, 22f * s), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s, 0f), () => SwitchTab(0));
            _tabBtnHudImg = _tabBtnHud.GetComponent<Image>();
            _tabBtnHudText = _tabBtnHud.GetComponentInChildren<Text>();

            _tabBtnFlight = CreateButtonElement(_tabBarRoot.transform, "Tab_Flight", I18n.Tr("UIW_TAB_FLIGHT", "飞控 作动"),
                new Vector2(tabW - 2f * s, 22f * s), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + tabW, 0f), () => SwitchTab(1));
            _tabBtnFlightImg = _tabBtnFlight.GetComponent<Image>();
            _tabBtnFlightText = _tabBtnFlight.GetComponentInChildren<Text>();

            _tabBtnTelemetry = CreateButtonElement(_tabBarRoot.transform, "Tab_Telem", I18n.Tr("UIW_TAB_TELEMETRY", "遥测 工况"),
                new Vector2(tabW - 2f * s, 22f * s), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + tabW * 2f, 0f), () => SwitchTab(2));
            _tabBtnTelemetryImg = _tabBtnTelemetry.GetComponent<Image>();
            _tabBtnTelemetryText = _tabBtnTelemetry.GetComponentInChildren<Text>();

            // 5. 主体内容面板根
            BuildHudTabPanel(baseW, s);
            BuildFlightTabPanel(baseW, s);
            BuildTelemetryTabPanel(baseW, s);

            SwitchTab(0);
            RefreshWidgetRows();
            UpdateCategoryButtonVisuals();
            UpdateDragButtonVisual();
            UpdateQuickBarVisuals();

            // 注册微控件句柄
            if (_headerRoot != null)
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "header", "控制中枢顶栏", _headerRoot);
            if (_dragModeBtn != null)
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "drag_mode_btn", "编辑模式切换键", _dragModeBtn.gameObject, _dragModeBtn, _dragModeBtnImg, null, _dragModeBtnText, null, "EDIT", () =>
                    {
                        WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                        UpdateDragButtonVisual();
                    }, true));
            if (_workbenchBtn != null)
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "workbench_btn", "工作台呼出键", _workbenchBtn.gameObject, _workbenchBtn, _workbenchBtn.GetComponent<Image>(), null, _workbenchBtnText, null, "SET", () => OnRequestOpenWorkbench?.Invoke(), false));
            if (_collapseBtn != null)
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "collapse_btn", "折叠按键", _collapseBtn.gameObject, _collapseBtn, _collapseBtn.GetComponent<Image>(), null, _collapseBtnText, null, "—", ToggleCollapse, true));
        }

        #region UI Construction Sub-Panels

        private void BuildHudTabPanel(float baseW, float s)
        {
            RectTransform hudRt = CreateContainer("TabHudPanel", transform,
                new Vector2(0f, -60f * s), new Vector2(0f, -30f * s));
            _tabHudRoot = hudRt.gameObject;
            hudRt.anchorMin = Vector2.zero;
            hudRt.anchorMax = Vector2.one;

            // 1. 全局配置条 (预设 / 制式 / 主题, 22px)
            RectTransform quickRt = CreateContainer("QuickBar", _tabHudRoot.transform,
                new Vector2(0f, 22f * s), new Vector2(0f, -2f * s));
            quickRt.anchorMin = new Vector2(0f, 1f);
            quickRt.anchorMax = new Vector2(1f, 1f);
            quickRt.pivot = new Vector2(0.5f, 1f);

            float qBtnW = (baseW - 16f * s) / 3f;
            _presetQuickBtn = CreateButtonElement(quickRt, "PresetBtn", I18n.Tr("UIW_OPT_PRESET", "预设"), new Vector2(qBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s, 0f), CyclePreset);
            _presetQuickText = _presetQuickBtn.GetComponentInChildren<Text>();

            _unitQuickBtn = CreateButtonElement(quickRt, "UnitBtn", I18n.Tr("UIW_OPT_UNIT", "制式"), new Vector2(qBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + qBtnW, 0f), CycleUnitSystem);
            _unitQuickText = _unitQuickBtn.GetComponentInChildren<Text>();

            _themeQuickBtn = CreateButtonElement(quickRt, "ThemeBtn", I18n.Tr("UIW_OPT_THEME", "主题"), new Vector2(qBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + qBtnW * 2f, 0f), CycleTheme);
            _themeQuickText = _themeQuickBtn.GetComponentInChildren<Text>();

            // 2. 分类过滤胶囊行 (20px)
            RectTransform catRt = CreateContainer("CategoryBar", _tabHudRoot.transform,
                new Vector2(0f, 22f * s), new Vector2(0f, -26f * s));
            catRt.anchorMin = new Vector2(0f, 1f);
            catRt.anchorMax = new Vector2(1f, 1f);
            catRt.pivot = new Vector2(0.5f, 1f);

            float catBtnW = (baseW - 16f * s) / CategoryNames.Length;
            for (int i = 0; i < CategoryNames.Length; i++)
            {
                int catIdx = i;
                float posX = 8f * s + catBtnW * i;
                Button btn = CreateButtonElement(catRt, $"Cat_{i}", CategoryNames[i], new Vector2(catBtnW - 2f * s, 18f * s),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(posX, 0f), () =>
                    {
                        _selectedCategory = catIdx;
                        RefreshWidgetRows();
                        UpdateCategoryButtonVisuals();
                    });
                _categoryBtns.Add(btn);
                _categoryBtnTexts.Add(btn.GetComponentInChildren<Text>());
            }

            // 3. 滚动列表容器
            ScrollRect sRect = CreateChild<ScrollRect>("ScrollArea", _tabHudRoot.transform,
                new Vector2(-12f * s, -80f * s), new Vector2(0f, 2f * s));
            GameObject scrollObj = sRect.gameObject;
            RectTransform srt = sRect.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);

            _scrollBg = scrollObj.AddComponent<Image>();
            _scrollBg.color = Color.clear;
            ApplyCard(_scrollBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            _scrollRect = sRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.scrollSensitivity = 20f;

            RectTransform viewRt = CreateViewport("Viewport", scrollObj.transform,
                new Vector2(-4f * s, -4f * s), Vector2.zero);
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;

            _contentRt = CreateContainer("Content", viewRt,
                Vector2.zero, Vector2.zero);
            _contentRt.anchorMin = new Vector2(0f, 1f);
            _contentRt.anchorMax = new Vector2(1f, 1f);
            _contentRt.pivot = new Vector2(0f, 1f);

            _scrollRect.viewport = viewRt;
            _scrollRect.content = _contentRt;

            // 4. 底部批量控制条
            RectTransform footRt = CreateContainer("Footer", _tabHudRoot.transform,
                new Vector2(0f, 24f * s), new Vector2(0f, 3f * s));
            footRt.anchorMin = new Vector2(0f, 0f);
            footRt.anchorMax = new Vector2(1f, 0f);
            footRt.pivot = new Vector2(0.5f, 0f);

            float fBtnW = (baseW - 16f * s) / 5f;
            CreateButtonElement(footRt, "ShowAll", I18n.Tr("UIW_BTN_SHOW_ALL", "全显"), new Vector2(fBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s, 0f), () => SetAllWidgetsActive(true));

            CreateButtonElement(footRt, "HideAll", I18n.Tr("UIW_BTN_HIDE_ALL", "全隐"), new Vector2(fBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW, 0f), () => SetAllWidgetsActive(false));

            CreateButtonElement(footRt, "LockAll", I18n.Tr("UIW_BTN_LOCK_ALL", "全锁"), new Vector2(fBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW * 2f, 0f), ToggleAllWidgetsLock);

            CreateButtonElement(footRt, "SnapAll", I18n.Tr("UIW_BTN_SNAP_GRID", "网格"), new Vector2(fBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW * 3f, 0f), () => SnapAllToGrid(10f));

            CreateButtonElement(footRt, "ResetAll", I18n.Tr("UIW_BTN_RESET_DEFAULT", "默认"), new Vector2(fBtnW - 2f * s, 20f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW * 4f, 0f), () =>
                {
                    WidgetLayoutManager.Instance.ResetToDefaultLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                    RefreshWidgetRows();
                });
        }

        private void BuildFlightTabPanel(float baseW, float s)
        {
            RectTransform fltRt = CreateContainer("TabFlightPanel", transform,
                new Vector2(0f, -60f * s), new Vector2(0f, -6f * s));
            _tabFlightRoot = fltRt.gameObject;
            fltRt.anchorMin = Vector2.zero;
            fltRt.anchorMax = Vector2.one;

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 核心作动开关网格 (2 行 4 列, 每格宽 ~68px, 高 24px)
            Text sec1 = UIFactory.CreateText(_tabFlightRoot.transform, "Sec1", I18n.Tr("UIW_FLT_ACTUATORS", "核心作动总控"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform sec1Rt = sec1.GetComponent<RectTransform>();
            sec1Rt.anchorMin = new Vector2(0f, 1f);
            sec1Rt.anchorMax = new Vector2(1f, 1f);
            sec1Rt.pivot = new Vector2(0f, 1f);
            sec1Rt.sizeDelta = new Vector2(-16f * s, 16f * s);
            sec1Rt.anchoredPosition = new Vector2(10f * s, -4f * s);

            float gW = (baseW - 24f * s) / 4f;
            float gH = 24f * s;
            float topY = -24f * s;

            _rcsBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_RCS", "RCS", new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s, topY), () => FlightTelemetryContext.Control?.ToggleRCS());
            _rcsBtnImg = _rcsBtn.GetComponent<Image>();
            _rcsBtnText = _rcsBtn.GetComponentInChildren<Text>();

            _sasBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_SAS", "SAS", new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW, topY), () => FlightTelemetryContext.Control?.ToggleSAS());
            _sasBtnImg = _sasBtn.GetComponent<Image>();
            _sasBtnText = _sasBtn.GetComponentInChildren<Text>();

            _gearBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_GEAR", I18n.Tr("UIW_ACT_GEAR", "起落"), new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW * 2f, topY), () => ToggleKspActionGroup("Gear"));
            _gearBtnImg = _gearBtn.GetComponent<Image>();
            _gearBtnText = _gearBtn.GetComponentInChildren<Text>();

            _lightBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_LIGHT", I18n.Tr("UIW_ACT_LIGHT", "照明"), new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW * 3f, topY), () => ToggleKspActionGroup("Light"));
            _lightBtnImg = _lightBtn.GetComponent<Image>();
            _lightBtnText = _lightBtn.GetComponentInChildren<Text>();

            float topY2 = topY - gH - 3f * s;
            _brakesBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_BRAKE", I18n.Tr("UIW_ACT_BRAKE", "刹车"), new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s, topY2), () => ToggleKspActionGroup("Brakes"));
            _brakesBtnImg = _brakesBtn.GetComponent<Image>();
            _brakesBtnText = _brakesBtn.GetComponentInChildren<Text>();

            _stageLockBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_LOCK", I18n.Tr("UIW_ACT_STAGE_LOCK", "级锁"), new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW, topY2), () => FlightTelemetryContext.Control?.ToggleStageLock());
            _stageLockBtnImg = _stageLockBtn.GetComponent<Image>();
            _stageLockBtnText = _stageLockBtn.GetComponentInChildren<Text>();

            _precisionBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_PREC", "PREC", new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW * 2f, topY2), () => FlightTelemetryContext.Control?.TogglePrecisionMode());
            _precisionBtnImg = _precisionBtn.GetComponent<Image>();
            _precisionBtnText = _precisionBtn.GetComponentInChildren<Text>();

            _abortBtn = CreateButtonElement(_tabFlightRoot.transform, "Btn_ABORT", I18n.Tr("UIW_ACT_ABORT", "逃逸"), new Vector2(gW - 2f * s, gH),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s + gW * 3f, topY2), () => ToggleKspActionGroup("Abort"));
            _abortBtnImg = _abortBtn.GetComponent<Image>();
            _abortBtnText = _abortBtn.GetComponentInChildren<Text>();

            // 2. SAS 姿态对齐九宫罗盘 (3 行 3 列)
            float sec2Top = topY2 - gH - 8f * s;
            Text sec2 = UIFactory.CreateText(_tabFlightRoot.transform, "Sec2", I18n.Tr("UIW_FLT_SAS_MODES", "SAS 姿态对齐"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform sec2Rt = sec2.GetComponent<RectTransform>();
            sec2Rt.anchorMin = new Vector2(0f, 1f);
            sec2Rt.anchorMax = new Vector2(1f, 1f);
            sec2Rt.pivot = new Vector2(0f, 1f);
            sec2Rt.sizeDelta = new Vector2(-16f * s, 16f * s);
            sec2Rt.anchoredPosition = new Vector2(10f * s, sec2Top);

            float sasW = (baseW - 22f * s) / 3f;
            float sasH = 22f * s;
            float sasGridTop = sec2Top - 18f * s;

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int idx = r * 3 + c;
                    FlightSASMode mode = SasModes[idx];
                    float x = 8f * s + sasW * c;
                    float y = sasGridTop - (sasH + 2f * s) * r;

                    Button btn = CreateButtonElement(_tabFlightRoot.transform, $"SAS_{idx}", SasModeLabels[idx],
                        new Vector2(sasW - 2f * s, sasH), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y),
                        () => FlightTelemetryContext.Control?.SetSASMode(mode));

                    _sasModeBtns[idx] = btn;
                    _sasModeBtnImgs[idx] = btn.GetComponent<Image>();
                    _sasModeBtnTexts[idx] = btn.GetComponentInChildren<Text>();
                }
            }

            // 3. 通用动作组 (AG 1 ~ AG 10, 2 行 5 列)
            float sec3Top = sasGridTop - (sasH + 2f * s) * 3 - 6f * s;
            Text sec3 = UIFactory.CreateText(_tabFlightRoot.transform, "Sec3", I18n.Tr("UIW_FLT_ACTION_GROUPS", "通用动作组"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform sec3Rt = sec3.GetComponent<RectTransform>();
            sec3Rt.anchorMin = new Vector2(0f, 1f);
            sec3Rt.anchorMax = new Vector2(1f, 1f);
            sec3Rt.pivot = new Vector2(0f, 1f);
            sec3Rt.sizeDelta = new Vector2(-16f * s, 16f * s);
            sec3Rt.anchoredPosition = new Vector2(10f * s, sec3Top);

            float agW = (baseW - 26f * s) / 5f;
            float agH = 20f * s;
            float agGridTop = sec3Top - 18f * s;

            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    int idx = r * 5 + c;
                    int agNum = idx + 1;
                    float x = 8f * s + agW * c;
                    float y = agGridTop - (agH + 2f * s) * r;

                    Button btn = CreateButtonElement(_tabFlightRoot.transform, $"AG_{agNum}", $"G{agNum}",
                        new Vector2(agW - 2f * s, agH), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y),
                        () => ToggleCustomActionGroup(agNum));

                    _agBtns[idx] = btn;
                    _agBtnImgs[idx] = btn.GetComponent<Image>();
                    _agBtnTexts[idx] = btn.GetComponentInChildren<Text>();
                }
            }

            // 4. 底部导航参考与时间加速 (1 行)
            float sec4Top = agGridTop - (agH + 2f * s) * 2 - 6f * s;
            float navW = baseW - 16f * s;
            float subW = navW * 0.38f;
            float warpBtnW = (navW - subW - 6f * s) / 4f;

            _refFrameBtn = CreateButtonElement(_tabFlightRoot.transform, "RefFrameBtn", I18n.TrFormat("UIW_NAV_REF_FMT", I18n.Tr("UIW_NAV_REF_ORBIT", "轨道")),
                new Vector2(subW, 22f * s), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f * s, sec4Top),
                () => BottomControlsWidget.CycleReferenceFrame());
            _refFrameBtnText = _refFrameBtn.GetComponentInChildren<Text>();

            float warpStartX = 8f * s + subW + 4f * s;
            _warpDownBtn = CreateButtonElement(_tabFlightRoot.transform, "WarpDown", "◀",
                new Vector2(warpBtnW - 2f * s, 22f * s), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(warpStartX, sec4Top),
                () => FlightTelemetryContext.Control?.DecreaseTimeWarp());

            _pauseBtn = CreateButtonElement(_tabFlightRoot.transform, "PauseBtn", "⏸",
                new Vector2(warpBtnW - 2f * s, 22f * s), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(warpStartX + warpBtnW, sec4Top),
                () => FlightTelemetryContext.Control?.TogglePause());
            _pauseBtnText = _pauseBtn.GetComponentInChildren<Text>();

            _warpZeroBtn = CreateButtonElement(_tabFlightRoot.transform, "WarpZero", "1x",
                new Vector2(warpBtnW - 2f * s, 22f * s), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(warpStartX + warpBtnW * 2f, sec4Top),
                () => FlightTelemetryContext.Control?.CancelTimeWarp());

            _warpUpBtn = CreateButtonElement(_tabFlightRoot.transform, "WarpUp", "▶",
                new Vector2(warpBtnW - 2f * s, 22f * s), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(warpStartX + warpBtnW * 3f, sec4Top),
                () => FlightTelemetryContext.Control?.IncreaseTimeWarp());
        }

        private void BuildTelemetryTabPanel(float baseW, float s)
        {
            RectTransform telRt = CreateContainer("TabTelemPanel", transform,
                new Vector2(0f, -60f * s), new Vector2(0f, -6f * s));
            _tabTelemetryRoot = telRt.gameObject;
            telRt.anchorMin = Vector2.zero;
            telRt.anchorMax = Vector2.one;

            WidgetStyleManager style = WidgetStyleManager.Instance;
            float cardW = baseW - 16f * s;
            float cardH = 68f * s;
            float startY = -4f * s;
            float spacing = 8f * s;

            // 1. 电力总线卡片
            RectTransform pwrCard = CreateContainer("PwrCard", _tabTelemetryRoot.transform,
                new Vector2(cardW, cardH), new Vector2(8f * s, startY));
            pwrCard.anchorMin = new Vector2(0f, 1f);
            pwrCard.anchorMax = new Vector2(0f, 1f);
            pwrCard.pivot = new Vector2(0f, 1f);
            Image pwrBg = pwrCard.gameObject.AddComponent<Image>();
            pwrBg.color = Color.clear;
            ApplyCard(pwrBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            Text pwrTitle = UIFactory.CreateText(pwrCard, "Title", I18n.Tr("UIW_TEL_POWER", "全机电网总线"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform ptRt = pwrTitle.GetComponent<RectTransform>();
            ptRt.anchorMin = new Vector2(0f, 1f);
            ptRt.anchorMax = new Vector2(1f, 1f);
            ptRt.pivot = new Vector2(0f, 1f);
            ptRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            ptRt.anchoredPosition = new Vector2(8f * s, -4f * s);

            Image ecTrack = CreateChild<Image>("Track", pwrCard, new Vector2(cardW - 16f * s, 6f * s), new Vector2(8f * s, -24f * s));
            ecTrack.rectTransform.anchorMin = new Vector2(0f, 1f);
            ecTrack.rectTransform.anchorMax = new Vector2(0f, 1f);
            ecTrack.rectTransform.pivot = new Vector2(0f, 1f);
            ecTrack.color = Color.clear;
            ApplyCard(ecTrack, null, CardStyleRole.SubtleSlot, _cachedTheme);

            _ecBarFill = CreateChild<Image>("Fill", ecTrack.transform, new Vector2(cardW - 16f * s, 6f * s), Vector2.zero);
            _ecBarFill.rectTransform.anchorMin = Vector2.zero;
            _ecBarFill.rectTransform.anchorMax = Vector2.one;
            _ecBarFill.rectTransform.sizeDelta = Vector2.zero;
            _ecBarFill.type = Image.Type.Filled;
            _ecBarFill.fillMethod = Image.FillMethod.Horizontal;
            _ecBarFill.fillAmount = 0.5f;
            _ecBarFill.color = style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme);

            _ecText = UIFactory.CreateText(pwrCard, "EcText", "EC: --- / --- (0.0%)", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme));
            RectTransform ecTxtRt = _ecText.GetComponent<RectTransform>();
            ecTxtRt.anchorMin = new Vector2(0f, 1f);
            ecTxtRt.anchorMax = new Vector2(1f, 1f);
            ecTxtRt.pivot = new Vector2(0f, 1f);
            ecTxtRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            ecTxtRt.anchoredPosition = new Vector2(8f * s, -33f * s);

            _netPowerText = UIFactory.CreateText(pwrCard, "NetText", I18n.TrFormat("UIW_TEL_POWER_FMT", "0.0", "0.0"), Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform netRt = _netPowerText.GetComponent<RectTransform>();
            netRt.anchorMin = new Vector2(0f, 1f);
            netRt.anchorMax = new Vector2(1f, 1f);
            netRt.pivot = new Vector2(0f, 1f);
            netRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            netRt.anchoredPosition = new Vector2(8f * s, -49f * s);

            // 2. 推进动力卡片
            float startY2 = startY - cardH - spacing;
            RectTransform propCard = CreateContainer("PropCard", _tabTelemetryRoot.transform,
                new Vector2(cardW, cardH), new Vector2(8f * s, startY2));
            propCard.anchorMin = new Vector2(0f, 1f);
            propCard.anchorMax = new Vector2(0f, 1f);
            propCard.pivot = new Vector2(0f, 1f);
            Image propBg = propCard.gameObject.AddComponent<Image>();
            propBg.color = Color.clear;
            ApplyCard(propBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            Text propTitle = UIFactory.CreateText(propCard, "Title", I18n.Tr("UIW_TEL_PROPULSION", "推进工况总线"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform proptRt = propTitle.GetComponent<RectTransform>();
            proptRt.anchorMin = new Vector2(0f, 1f);
            proptRt.anchorMax = new Vector2(1f, 1f);
            proptRt.pivot = new Vector2(0f, 1f);
            proptRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            proptRt.anchoredPosition = new Vector2(8f * s, -4f * s);

            Image thrTrack = CreateChild<Image>("ThrTrack", propCard, new Vector2(cardW - 16f * s, 6f * s), new Vector2(8f * s, -24f * s));
            thrTrack.rectTransform.anchorMin = new Vector2(0f, 1f);
            thrTrack.rectTransform.anchorMax = new Vector2(0f, 1f);
            thrTrack.rectTransform.pivot = new Vector2(0f, 1f);
            thrTrack.color = Color.clear;
            ApplyCard(thrTrack, null, CardStyleRole.SubtleSlot, _cachedTheme);

            _throttleBarFill = CreateChild<Image>("Fill", thrTrack.transform, new Vector2(cardW - 16f * s, 6f * s), Vector2.zero);
            _throttleBarFill.rectTransform.anchorMin = Vector2.zero;
            _throttleBarFill.rectTransform.anchorMax = Vector2.one;
            _throttleBarFill.rectTransform.sizeDelta = Vector2.zero;
            _throttleBarFill.type = Image.Type.Filled;
            _throttleBarFill.fillMethod = Image.FillMethod.Horizontal;
            _throttleBarFill.fillAmount = 0f;
            _throttleBarFill.color = style.GetTextColor(TextStyleRole.Accent, _cachedTheme);

            _stageDvText = UIFactory.CreateText(propCard, "DvText", I18n.TrFormat("UIW_TEL_DV_FMT", "0", "0"), Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme));
            RectTransform sdvRt = _stageDvText.GetComponent<RectTransform>();
            sdvRt.anchorMin = new Vector2(0f, 1f);
            sdvRt.anchorMax = new Vector2(1f, 1f);
            sdvRt.pivot = new Vector2(0f, 1f);
            sdvRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            sdvRt.anchoredPosition = new Vector2(8f * s, -33f * s);

            _twrBurnText = UIFactory.CreateText(propCard, "TwrText", I18n.TrFormat("UIW_TEL_PROP_FMT", "0.00", "--", "--", "0"), Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform twrRt = _twrBurnText.GetComponent<RectTransform>();
            twrRt.anchorMin = new Vector2(0f, 1f);
            twrRt.anchorMax = new Vector2(1f, 1f);
            twrRt.pivot = new Vector2(0f, 1f);
            twrRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            twrRt.anchoredPosition = new Vector2(8f * s, -49f * s);

            // 3. 通信控制卡片 (48px)
            float startY3 = startY2 - cardH - spacing;
            float c3H = 50f * s;
            RectTransform commCard = CreateContainer("CommCard", _tabTelemetryRoot.transform,
                new Vector2(cardW, c3H), new Vector2(8f * s, startY3));
            commCard.anchorMin = new Vector2(0f, 1f);
            commCard.anchorMax = new Vector2(0f, 1f);
            commCard.pivot = new Vector2(0f, 1f);
            Image commBg = commCard.gameObject.AddComponent<Image>();
            commBg.color = Color.clear;
            ApplyCard(commBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            Text commTitle = UIFactory.CreateText(commCard, "Title", I18n.Tr("UIW_TEL_COMM", "通信控制链路"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform commtRt = commTitle.GetComponent<RectTransform>();
            commtRt.anchorMin = new Vector2(0f, 1f);
            commtRt.anchorMax = new Vector2(1f, 1f);
            commtRt.pivot = new Vector2(0f, 1f);
            commtRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            commtRt.anchoredPosition = new Vector2(8f * s, -4f * s);

            Image commTrack = CreateChild<Image>("CommTrack", commCard, new Vector2(cardW - 16f * s, 5f * s), new Vector2(8f * s, -22f * s));
            commTrack.rectTransform.anchorMin = new Vector2(0f, 1f);
            commTrack.rectTransform.anchorMax = new Vector2(0f, 1f);
            commTrack.rectTransform.pivot = new Vector2(0f, 1f);
            commTrack.color = Color.clear;
            ApplyCard(commTrack, null, CardStyleRole.SubtleSlot, _cachedTheme);

            _commBarFill = CreateChild<Image>("Fill", commTrack.transform, new Vector2(cardW - 16f * s, 5f * s), Vector2.zero);
            _commBarFill.rectTransform.anchorMin = Vector2.zero;
            _commBarFill.rectTransform.anchorMax = Vector2.one;
            _commBarFill.rectTransform.sizeDelta = Vector2.zero;
            _commBarFill.type = Image.Type.Filled;
            _commBarFill.fillMethod = Image.FillMethod.Horizontal;
            _commBarFill.fillAmount = 1f;
            _commBarFill.color = style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme);

            _commStatusText = UIFactory.CreateText(commCard, "CommTxt", I18n.TrFormat("UIW_TEL_SIGNAL_FMT", "100", I18n.Tr("UIW_TEL_COMM_CONNECTED", "已联通")), Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform comstRt = _commStatusText.GetComponent<RectTransform>();
            comstRt.anchorMin = new Vector2(0f, 1f);
            comstRt.anchorMax = new Vector2(1f, 1f);
            comstRt.pivot = new Vector2(0f, 1f);
            comstRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            comstRt.anchoredPosition = new Vector2(8f * s, -30f * s);

            // 4. 轨道要素卡片 (66px)
            float startY4 = startY3 - c3H - spacing;
            float c4H = 68f * s;
            RectTransform orbCard = CreateContainer("OrbCard", _tabTelemetryRoot.transform,
                new Vector2(cardW, c4H), new Vector2(8f * s, startY4));
            orbCard.anchorMin = new Vector2(0f, 1f);
            orbCard.anchorMax = new Vector2(0f, 1f);
            orbCard.pivot = new Vector2(0f, 1f);
            Image orbBg = orbCard.gameObject.AddComponent<Image>();
            orbBg.color = Color.clear;
            ApplyCard(orbBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            Text orbTitle = UIFactory.CreateText(orbCard, "Title", I18n.Tr("UIW_TEL_ORBIT", "轨道要素概览"),
                Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform orbtRt = orbTitle.GetComponent<RectTransform>();
            orbtRt.anchorMin = new Vector2(0f, 1f);
            orbtRt.anchorMax = new Vector2(1f, 1f);
            orbtRt.pivot = new Vector2(0f, 1f);
            orbtRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            orbtRt.anchoredPosition = new Vector2(8f * s, -4f * s);

            _apPeText = UIFactory.CreateText(orbCard, "ApPe", "Ap: --- km | Pe: --- km", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme));
            RectTransform appeRt = _apPeText.GetComponent<RectTransform>();
            appeRt.anchorMin = new Vector2(0f, 1f);
            appeRt.anchorMax = new Vector2(1f, 1f);
            appeRt.pivot = new Vector2(0f, 1f);
            appeRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            appeRt.anchoredPosition = new Vector2(8f * s, -22f * s);

            _timeToApPeText = UIFactory.CreateText(orbCard, "TimeTo", "T-Ap: --:-- | T-Pe: --:--", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform ttRt = _timeToApPeText.GetComponent<RectTransform>();
            ttRt.anchorMin = new Vector2(0f, 1f);
            ttRt.anchorMax = new Vector2(1f, 1f);
            ttRt.pivot = new Vector2(0f, 1f);
            ttRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            ttRt.anchoredPosition = new Vector2(8f * s, -38f * s);

            _incEccText = UIFactory.CreateText(orbCard, "IncEcc", "Inc: 0.00° | Ecc: 0.0000", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, _cachedTheme));
            RectTransform ieRt = _incEccText.GetComponent<RectTransform>();
            ieRt.anchorMin = new Vector2(0f, 1f);
            ieRt.anchorMax = new Vector2(1f, 1f);
            ieRt.pivot = new Vector2(0f, 1f);
            ieRt.sizeDelta = new Vector2(-16f * s, 16f * s);
            ieRt.anchoredPosition = new Vector2(8f * s, -52f * s);
        }

        #endregion

        #region Interaction & Tabs

        private void SwitchTab(int tabIndex)
        {
            _activeTab = tabIndex;
            if (_tabHudRoot != null) _tabHudRoot.SetActive(_activeTab == 0);
            if (_tabFlightRoot != null) _tabFlightRoot.SetActive(_activeTab == 1);
            if (_tabTelemetryRoot != null) _tabTelemetryRoot.SetActive(_activeTab == 2);
            UpdateTabButtonVisuals();
        }

        private void UpdateTabButtonVisuals()
        {
            if (_tabBtnHudImg != null)
            {
                ApplyCard(_tabBtnHudImg, null, _activeTab == 0 ? CardStyleRole.Emphasized : CardStyleRole.SubtleSlot, _cachedTheme);
                ApplyText(_tabBtnHudText, _activeTab == 0 ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
            if (_tabBtnFlightImg != null)
            {
                ApplyCard(_tabBtnFlightImg, null, _activeTab == 1 ? CardStyleRole.Emphasized : CardStyleRole.SubtleSlot, _cachedTheme);
                ApplyText(_tabBtnFlightText, _activeTab == 1 ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
            if (_tabBtnTelemetryImg != null)
            {
                ApplyCard(_tabBtnTelemetryImg, null, _activeTab == 2 ? CardStyleRole.Emphasized : CardStyleRole.SubtleSlot, _cachedTheme);
                ApplyText(_tabBtnTelemetryText, _activeTab == 2 ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            float s = CurrentDpiScale;
            float baseW = 300f * s;
            float targetH = _isCollapsed ? 36f * s : 380f * s;
            RectTransform.sizeDelta = new Vector2(baseW, targetH);

            if (_tabBarRoot != null) _tabBarRoot.SetActive(!_isCollapsed);
            if (_tabHudRoot != null) _tabHudRoot.SetActive(!_isCollapsed && _activeTab == 0);
            if (_tabFlightRoot != null) _tabFlightRoot.SetActive(!_isCollapsed && _activeTab == 1);
            if (_tabTelemetryRoot != null) _tabTelemetryRoot.SetActive(!_isCollapsed && _activeTab == 2);

            if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "▲" : "▼";
        }

        private void UpdateDragButtonVisual()
        {
            if (_dragModeBtnImg == null) return;
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            ApplyCard(_dragModeBtnImg, null, isEdit ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
            if (_dragModeBtnText != null)
            {
                ApplyText(_dragModeBtnText, isEdit ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
        }

        private void UpdateCategoryButtonVisuals()
        {
            for (int i = 0; i < _categoryBtns.Count; i++)
            {
                bool isSel = (_selectedCategory == i);
                Image img = _categoryBtns[i].GetComponent<Image>();
                ApplyCard(img, null, isSel ? CardStyleRole.Emphasized : CardStyleRole.SubtleSlot, _cachedTheme);
                ApplyText(_categoryBtnTexts[i], isSel ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
        }

        private void UpdateQuickBarVisuals()
        {
            if (_presetQuickText != null)
            {
                string presetPrefix = I18n.Tr("UIW_OPT_PRESET", "预设");
                SetTextIfChanged(_presetQuickText, $"{presetPrefix} ▾");
            }
            if (_unitQuickText != null)
            {
                string unitModeName = AvionicsUnitSystem.GlobalMode.ToString();
                if (unitModeName.Length > 8) unitModeName = unitModeName.Substring(0, 8);
                SetTextIfChanged(_unitQuickText, $"{unitModeName} ▾");
            }
            if (_themeQuickText != null)
            {
                string themeName = ThemeManager.Instance?.CurrentTheme?.DisplayName ?? I18n.Tr("UIW_THEME_DEFAULT", "默认");
                if (themeName.Length > 7) themeName = themeName.Substring(0, 7);
                SetTextIfChanged(_themeQuickText, $"{themeName} ▾");
            }
        }

        private void CyclePreset()
        {
            var presets = LayoutShareHub.GetAvailablePresets();
            if (presets == null || presets.Count == 0) return;

            var p = presets[0];
            var data = LayoutShareHub.LoadPreset(p);
            if (data != null)
            {
                WidgetLayoutManager.Instance.ApplyLayout(data);
                FlightHUDManager.Instance?.RebuildHUD();
                RefreshWidgetRows();
            }
        }

        private void CycleUnitSystem()
        {
            UnitSystemMode cur = AvionicsUnitSystem.GlobalMode;
            UnitSystemMode next = (UnitSystemMode)(((int)cur + 1) % 4);
            AvionicsUnitSystem.GlobalMode = next;
            UpdateQuickBarVisuals();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        private void CycleTheme()
        {
            var tm = ThemeManager.Instance;
            if (tm == null || tm.AvailableThemes == null || tm.AvailableThemes.Count == 0) return;
            int idx = tm.AvailableThemes.FindIndex(t => t.ThemeId == tm.CurrentTheme?.ThemeId);
            int nextIdx = (idx + 1) % tm.AvailableThemes.Count;
            tm.SetTheme(tm.AvailableThemes[nextIdx].ThemeId);
            UpdateQuickBarVisuals();
        }

        private static void ToggleKspActionGroup(string groupName)
        {
#if !HEADLESS && !UNITY_EDITOR
            if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.ActionGroups != null)
            {
                if (groupName == "Gear") FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Gear);
                else if (groupName == "Light") FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Light);
                else if (groupName == "Brakes") FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Brakes);
                else if (groupName == "Abort") FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.Abort);
            }
#endif
        }

        private static void ToggleCustomActionGroup(int groupNumber)
        {
#if !HEADLESS && !UNITY_EDITOR
            if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.ActionGroups != null)
            {
                KSPActionGroup ag = (KSPActionGroup)((int)KSPActionGroup.Custom01 + (groupNumber - 1));
                FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup(ag);
            }
#endif
        }

        private static void LocateAndHighlightWidget(string widgetId)
        {
            var hudWidgets = FlightHUDManager.Instance?.ModularWidgets;
            if (hudWidgets == null) return;
            for (int i = 0; i < hudWidgets.Count; i++)
            {
                var w = hudWidgets[i];
                if (w != null && w.WidgetId == widgetId)
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetSelectionManager.Select(w);
                    break;
                }
            }
        }

        private void ResetWidgetToDefault(WidgetConfig w)
        {
            if (w == null) return;
            var desc = WidgetRegistry.GetDescriptor(w.WidgetId);
            if (desc != null)
            {
                w.PositionX = desc.DefaultX;
                w.PositionY = desc.DefaultY;
                w.Scale = 1.0f;
                w.ScaleX = 1.0f;
                w.ScaleY = 1.0f;
                w.Rotation = 0f;
                w.Opacity = 1.0f;
            }
            else
            {
                w.PositionX = 0f;
                w.PositionY = 0f;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        private void ToggleAllWidgetsLock()
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null || widgets.Count == 0) return;

            bool anyUnlocked = false;
            for (int i = 0; i < widgets.Count; i++)
            {
                if (!widgets[i].IsLocked) { anyUnlocked = true; break; }
            }
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].IsLocked = anyUnlocked;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        #endregion

        #region Widget List Rows

        private void RefreshWidgetRows()
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;

            float s = CurrentDpiScale;
            float rowH = 26f * s;
            float spacing = 2f * s;
            float curY = 0f;

            for (int i = 0; i < _rowViews.Count; i++)
            {
                _rowViews[i].Root.SetActive(false);
            }

            int viewIdx = 0;
            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];
                if (!MatchesCategory(w, _selectedCategory)) continue;

                WidgetRowView row;
                if (viewIdx < _rowViews.Count)
                {
                    row = _rowViews[viewIdx];
                    row.Root.SetActive(true);
                }
                else
                {
                    row = CreateRowView();
                    _rowViews.Add(row);
                }

                BindRow(row, w, curY, rowH);
                curY += rowH + spacing;
                viewIdx++;
            }

            if (_contentRt != null)
                _contentRt.sizeDelta = new Vector2(0f, curY + 6f * s);
        }

        private bool MatchesCategory(WidgetConfig w, int category)
        {
            if (category == 0) return true;
            if (category == 4) return w.IsEnabled;
            if (category == 5) return !w.IsEnabled;

            string id = w.WidgetId.ToLowerInvariant();
            string type = (w.WidgetType ?? "").ToLowerInvariant();

            if (category == 1) // 仪表
                return type == "tape" || type == "arc_meter" || id.StartsWith("gauge.") || id.Contains("gauge") || id.Contains("navball") || id.Contains("attitude");
            if (category == 2) // 系统
                return id.Contains("eicas") || id.Contains("electrical") || id.Contains("life") || id.Contains("perf") || id.Contains("signal") || id.Contains("comm");
            if (category == 3) // 控制
                return id.Contains("toolbar") || id.Contains("control") || id.Contains("sas") || id.Contains("timewarp") || id.Contains("staging") || id.Contains("bottom") || id.Contains("ui_widget");

            return true;
        }

        private WidgetRowView CreateRowView()
        {
            float s = CurrentDpiScale;
            Image rowBg = CreateChild<Image>("Row", _contentRt);
            GameObject rowObj = rowBg.gameObject;
            rowBg.color = Color.clear;
            ApplyCard(rowBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            RectTransform rrt = rowBg.rectTransform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0f, 1f);

            // 开关按钮
            Button tglBtn = CreateChild<Button>("Toggle", rowObj.transform,
                new Vector2(18f * s, 18f * s), new Vector2(6f * s, 0f));
            RectTransform trt = tglBtn.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(0f, 0.5f);
            trt.pivot = new Vector2(0f, 0.5f);

            Image ledImg = CreateChild<Image>("LED", tglBtn.transform,
                new Vector2(10f * s, 10f * s), Vector2.zero);
            ledImg.color = Color.clear;
            ledImg.sprite = Core.NavballMarkerFactory.GetCircleMaskSprite();

            // 类型徽标
            Text typeText = UIFactory.CreateText(rowObj.transform, "Type", "[W]", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, _cachedTheme));
            RectTransform yrt = typeText.GetComponent<RectTransform>();
            yrt.anchorMin = new Vector2(0f, 0.5f);
            yrt.anchorMax = new Vector2(0f, 0.5f);
            yrt.pivot = new Vector2(0f, 0.5f);
            yrt.sizeDelta = new Vector2(36f * s, 20f * s);
            yrt.anchoredPosition = new Vector2(28f * s, 0f);

            // 名称
            Text nameText = UIFactory.CreateText(rowObj.transform, "Name", I18n.Tr("WIDGET_FALLBACK_NAME", "组件"), Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform nrt = nameText.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0f, 0.5f);
            nrt.anchorMax = new Vector2(1f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.sizeDelta = new Vector2(-155f * s, 18f * s);
            nrt.anchoredPosition = new Vector2(66f * s, 0f);
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;

            // 锁定按钮
            Button lockBtn = CreateButtonElement(rowObj.transform, "LockBtn", I18n.Tr("UIW_ROW_LOCK", "锁"),
                new Vector2(20f * s, 18f * s), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-48f * s, 0f), null);
            Text lockText = lockBtn.GetComponentInChildren<Text>();

            // 定位按钮
            Button locateBtn = CreateButtonElement(rowObj.transform, "LocateBtn", I18n.Tr("UIW_ROW_LOCATE", "位"),
                new Vector2(20f * s, 18f * s), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-26f * s, 0f), null);
            Text locateText = locateBtn.GetComponentInChildren<Text>();

            // 复位按钮
            Button resetBtn = CreateButtonElement(rowObj.transform, "ResetBtn", I18n.Tr("UIW_ROW_RESET", "重"),
                new Vector2(20f * s, 18f * s), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f * s, 0f), null);
            Text resetText = resetBtn.GetComponentInChildren<Text>();

            return new WidgetRowView
            {
                Root = rowObj,
                RowBg = rowBg,
                ToggleBtn = tglBtn,
                ToggleLed = ledImg,
                TypeBadgeText = typeText,
                NameText = nameText,
                LockBtn = lockBtn,
                LockBtnText = lockText,
                LocateBtn = locateBtn,
                LocateBtnText = locateText,
                ResetBtn = resetBtn,
                ResetBtnText = resetText
            };
        }

        private void BindRow(WidgetRowView row, WidgetConfig w, float topOffset, float height)
        {
            float s = CurrentDpiScale;
            RectTransform rrt = row.Root.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(-6f * s, height);
            rrt.anchoredPosition = new Vector2(3f * s, -topOffset);

            row.WidgetId = w.WidgetId;
            row.IsEnabled = w.IsEnabled;
            row.IsLocked = w.IsLocked;

            SetTextIfChanged(row.NameText, w.DisplayName);

            string badge = w.WidgetType == "tape" ? I18n.Tr("UIW_BADGE_PFD", "仪表") :
                          (w.WidgetId.StartsWith("spacex.") ? "SPX" :
                          (w.WidgetId.StartsWith("custom.") ? I18n.Tr("UIW_BADGE_CARD", "卡片") : I18n.Tr("UIW_BADGE_CORE", "核心")));
            SetTextIfChanged(row.TypeBadgeText, badge);

            WidgetStyleManager style = WidgetStyleManager.Instance;
            row.ToggleLed.color = w.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                              : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);

            // 锁定状态显示
            SetTextIfChanged(row.LockBtnText, w.IsLocked ? I18n.Tr("UIW_ROW_LOCKED", "锁定") : I18n.Tr("UIW_ROW_FREE", "自由"));
            ApplyText(row.LockBtnText, w.IsLocked ? TextStyleRole.Warning : TextStyleRole.Muted, _cachedTheme);

            row.ToggleBtn.onClick.RemoveAllListeners();
            row.ToggleBtn.onClick.AddListener(() =>
            {
                w.IsEnabled = !w.IsEnabled;
                row.IsEnabled = w.IsEnabled;
                row.ToggleLed.color = w.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                                  : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            });

            row.LockBtn.onClick.RemoveAllListeners();
            row.LockBtn.onClick.AddListener(() =>
            {
                w.IsLocked = !w.IsLocked;
                row.IsLocked = w.IsLocked;
                SetTextIfChanged(row.LockBtnText, w.IsLocked ? I18n.Tr("UIW_ROW_LOCKED", "锁定") : I18n.Tr("UIW_ROW_FREE", "自由"));
                ApplyText(row.LockBtnText, w.IsLocked ? TextStyleRole.Warning : TextStyleRole.Muted, _cachedTheme);
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            });

            row.LocateBtn.onClick.RemoveAllListeners();
            row.LocateBtn.onClick.AddListener(() => LocateAndHighlightWidget(w.WidgetId));

            row.ResetBtn.onClick.RemoveAllListeners();
            row.ResetBtn.onClick.AddListener(() => ResetWidgetToDefault(w));
        }

        private void SetAllWidgetsActive(bool active)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].IsEnabled = active;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        private void SnapAllToGrid(float grid)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].PositionX = Mathf.Round(widgets[i].PositionX / grid) * grid;
                widgets[i].PositionY = Mathf.Round(widgets[i].PositionY / grid) * grid;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        #endregion

        #region Helpers & Themings

        private Button CreateButtonElement(Transform parent, string name, string text, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Action onClick)
        {
            Button btn = CreateButton(name, parent, out RectTransform rt, out Image img, size, pos);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;

            img.color = Color.clear;
            ApplyCard(img, null, CardStyleRole.InteractiveButton, _cachedTheme);

            if (onClick != null)
                btn.onClick.AddListener(() => onClick.Invoke());

            Text txt = UIFactory.CreateText(btn.transform, "Label", text, Mathf.RoundToInt(9.5f * CurrentDpiScale),
                TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            return btn;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            _cachedTheme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, _cachedTheme);
            ApplyText(_titleText, TextStyleRole.Cardinal, _cachedTheme);
            ApplyText(_statusBadge, TextStyleRole.PrimaryValue, _cachedTheme);

            if (_scrollBg != null) ApplyCard(_scrollBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            UpdateTabButtonVisuals();
            UpdateDragButtonVisual();
            UpdateCategoryButtonVisuals();
            UpdateQuickBarVisuals();

            for (int i = 0; i < _rowViews.Count; i++)
            {
                var r = _rowViews[i];
                if (r.Root != null && r.Root.activeSelf)
                {
                    ApplyCard(r.RowBg, null, CardStyleRole.SubtleSlot, _cachedTheme);
                    ApplyText(r.TypeBadgeText, TextStyleRole.Unit, _cachedTheme);
                    ApplyText(r.NameText, TextStyleRole.Label, _cachedTheme);
                    r.ToggleLed.color = r.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                                    : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);
                    ApplyText(r.LockBtnText, r.IsLocked ? TextStyleRole.Warning : TextStyleRole.Muted, _cachedTheme);
                }
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context) => base.OnUIDrawLoop(ref context);

        protected override void OnRenderState()
        {
            UIWidgetState snap = _logic.CurrentState;
            if (!snap.HasStats) return;

            // 1. 顶栏状态徽标
            int total = snap.TotalWidgets;
            int active = snap.ActiveWidgets;
            if (total != _lastTotalCount.Value || active != _lastActiveCount.Value)
            {
                _lastTotalCount.Update(total);
                _lastActiveCount.Update(active);
                SetTextIfChanged(_statusBadge, $"{active}/{total} ON");
            }

            // 2. 根据激活 Tab 渲染高频组件
            if (_activeTab == 1)
            {
                RenderFlightControlsTab(ref snap);
            }
            else if (_activeTab == 2)
            {
                RenderTelemetryTab(ref snap);
            }
        }

        private void RenderFlightControlsTab(ref UIWidgetState snap)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_lastRcs.Update(snap.IsRcsOn) && _rcsBtnImg != null)
            {
                ApplyCard(_rcsBtnImg, null, snap.IsRcsOn ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_rcsBtnText, snap.IsRcsOn ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastSas.Update(snap.IsSasOn) && _sasBtnImg != null)
            {
                ApplyCard(_sasBtnImg, null, snap.IsSasOn ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_sasBtnText, snap.IsSasOn ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastGear.Update(snap.IsGearOn) && _gearBtnImg != null)
            {
                ApplyCard(_gearBtnImg, null, snap.IsGearOn ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_gearBtnText, snap.IsGearOn ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastLight.Update(snap.IsLightOn) && _lightBtnImg != null)
            {
                ApplyCard(_lightBtnImg, null, snap.IsLightOn ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_lightBtnText, snap.IsLightOn ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastBrakes.Update(snap.IsBrakesOn) && _brakesBtnImg != null)
            {
                ApplyCard(_brakesBtnImg, null, snap.IsBrakesOn ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_brakesBtnText, snap.IsBrakesOn ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastStageLocked.Update(snap.IsStageLocked) && _stageLockBtnImg != null)
            {
                ApplyCard(_stageLockBtnImg, null, snap.IsStageLocked ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_stageLockBtnText, snap.IsStageLocked ? TextStyleRole.Warning : TextStyleRole.Label, _cachedTheme);
            }
            if (_lastPrecision.Update(snap.IsPrecisionMode) && _precisionBtnImg != null)
            {
                ApplyCard(_precisionBtnImg, null, snap.IsPrecisionMode ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                ApplyText(_precisionBtnText, snap.IsPrecisionMode ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }

            // SAS 模式高亮
            if (_lastSasMode.Update(snap.CurrentSASMode))
            {
                for (int i = 0; i < 9; i++)
                {
                    bool isCurrent = snap.IsSasOn && (snap.CurrentSASMode == SasModes[i]);
                    if (_sasModeBtnImgs[i] != null)
                    {
                        ApplyCard(_sasModeBtnImgs[i], null, isCurrent ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                        ApplyText(_sasModeBtnTexts[i], isCurrent ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
                    }
                }
            }

            // 动作组高亮
            if (_lastAgMask.Update(snap.ActionGroupMask))
            {
                for (int i = 0; i < 10; i++)
                {
                    bool isAgActive = (snap.ActionGroupMask & (1u << i)) != 0;
                    if (_agBtnImgs[i] != null)
                    {
                        ApplyCard(_agBtnImgs[i], null, isAgActive ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
                        ApplyText(_agBtnTexts[i], isAgActive ? TextStyleRole.PrimaryValue : TextStyleRole.Label, _cachedTheme);
                    }
                }
            }

            // 参考系与加速状态
            if (_refFrameBtnText != null)
            {
                string spdMode = snap.CurrentSpeedMode.ToString().ToUpperInvariant();
                if (_lastSpeedMode.Update(spdMode))
                {
                    string refMode = snap.CurrentSpeedMode == SpeedDisplayMode.Surface ? I18n.Tr("UIW_NAV_REF_SURFACE", "地表") : (snap.CurrentSpeedMode == SpeedDisplayMode.Target ? I18n.Tr("UIW_NAV_REF_TARGET", "目标") : I18n.Tr("UIW_NAV_REF_ORBIT", "轨道"));
                    SetTextIfChanged(_refFrameBtnText, I18n.TrFormat("UIW_NAV_REF_FMT", refMode));
                }
            }
            if (_pauseBtnText != null && _lastPaused.Update(snap.IsPaused))
            {
                SetTextIfChanged(_pauseBtnText, snap.IsPaused ? "▶" : "⏸");
            }
        }

        private void RenderTelemetryTab(ref UIWidgetState snap)
        {
            if (!snap.HasVessel) return;

            // 电力
            if (_lastEcPct.Update(snap.EcPercent) && _ecBarFill != null)
            {
                _ecBarFill.fillAmount = Mathf.Clamp01(snap.EcPercent);
            }
            if (_ecText != null)
            {
                string ecStr = $"EC: {snap.ElectricCharge:F0} / {snap.MaxElectricCharge:F0} ({snap.EcPercent * 100f:F1}%)";
                if (_lastEcVal.Update(ecStr)) SetTextIfChanged(_ecText, ecStr);
            }
            if (_netPowerText != null)
            {
                string pwrStr = I18n.TrFormat("UIW_TEL_POWER_FMT", snap.NetEcRate.ToString("+0.0;-0.0;0.0"), snap.SolarPower.ToString("F1"));
                if (_lastNetPower.Update(pwrStr)) SetTextIfChanged(_netPowerText, pwrStr);
            }

            // 推进
            if (_throttleBarFill != null && _lastThrottle.Update(snap.Throttle))
            {
                _throttleBarFill.fillAmount = Mathf.Clamp01(snap.Throttle);
            }
            if (_stageDvText != null)
            {
                string dvStr = I18n.TrFormat("UIW_TEL_DV_FMT", ((int)snap.StageDeltaV).ToString(), ((int)snap.TotalDeltaV).ToString());
                if (_lastStageDv.Update(dvStr)) SetTextIfChanged(_stageDvText, dvStr);
            }
            if (_twrBurnText != null)
            {
                int burnM = (int)(snap.StageBurnTime / 60.0);
                int burnS = (int)(snap.StageBurnTime % 60.0);
                string twrStr = I18n.TrFormat("UIW_TEL_PROP_FMT", snap.Twr.ToString("F2"), burnM.ToString("D2"), burnS.ToString("D2"), ((int)(snap.Throttle * 100f)).ToString());
                if (_lastTwr.Update(twrStr)) SetTextIfChanged(_twrBurnText, twrStr);
            }

            // 通信
            if (_commBarFill != null && _lastCommSignal.Update(snap.CommSignal))
            {
                _commBarFill.fillAmount = Mathf.Clamp01(snap.CommSignal);
            }
            if (_commStatusText != null)
            {
                string commStr = snap.IsConnected
                    ? I18n.TrFormat("UIW_TEL_SIGNAL_FMT", ((int)(snap.CommSignal * 100f)).ToString(), I18n.Tr("UIW_TEL_COMM_CONNECTED", "已联通"))
                    : I18n.Tr("UIW_TEL_COMM_NO_LINK", "无遥测通信链路");
                SetTextIfChanged(_commStatusText, commStr);
            }

            // 轨道
            if (_apPeText != null)
            {
                string apStr = AvionicsUnitSystem.FormatAdaptive(snap.Apoapsis, UnitDimension.Length, AvionicsUnitSystem.GlobalMode);
                string peStr = AvionicsUnitSystem.FormatAdaptive(snap.Periapsis, UnitDimension.Length, AvionicsUnitSystem.GlobalMode);
                string appeStr = $"Ap: {apStr} | Pe: {peStr}";
                if (_lastApPe.Update(appeStr)) SetTextIfChanged(_apPeText, appeStr);
            }
            if (_timeToApPeText != null)
            {
                string ttStr = $"T-Ap: {FormatDuration(snap.TimeToAp)} | T-Pe: {FormatDuration(snap.TimeToPe)}";
                if (_lastApPeTime.Update(ttStr)) SetTextIfChanged(_timeToApPeText, ttStr);
            }
            if (_incEccText != null)
            {
                string ieStr = $"Inc: {snap.Inclination:F2}° | Ecc: {snap.Eccentricity:F4}";
                if (_lastOrbitElements.Update(ieStr)) SetTextIfChanged(_incEccText, ieStr);
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
