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
    /// 遵循 MFP-SPEC-001..007 航电规范与现代全玻璃座舱 HUD 交互设计标准：
    /// 1. 顶栏安全联动：专业分级安全锁 (SAFE / ARMED) 状态药丸与防误触分级点火触发总成 (FIRE STAGE ▶)；
    /// 2. 分级遥测中枢：高反差深晶数显分级窗 (STAGE 03) 结合单级 Δv、燃烧时序 (⏱ 00:36)、推重比 (TWR) 与发动机计数；
    /// 3. 三轴姿态仪表：PITCH / ROLL / YAW 动态双向微光导轨、零位基准中轴线、四分度刻线与机械配平游标 (Trim Pip)；
    /// 4. 推进剂监测槽：动态识别推进剂名称标签、余量百分比与低燃量三段式脉冲预警；
    /// 5. 模式快速切换：NORM/PREC 微调操纵模式、STG/DCK 飞行/对接口模式与 KSP HUD 原生面板无缝显隐切换；
    /// 6. 动态长宽比自适应 (IAdaptiveSizeWidget)：支持非等比缩放，自动重排并自适应拉伸三轴标尺与遥测卡片；
    /// 7. 严格落实 0 颜色字面量 (MFP-SPEC-006)、零场景查询 (MFP-SPEC-007) 与 Critical (60Hz) 阶梯高保真刷新。
    /// </summary>
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
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "级控制"), null, 9.5f);

        // 顶栏安全联动总成
        private GameObject _statusBadgePill;
        private Image _statusBadgePillBg;
        private Outline _statusBadgePillOutline;
        private Text _statusBadgeText;

        private Button _lockBtn;
        private Image _lockBtnBg;
        private Outline _lockBtnOutline;
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

        private Text _stageDvText;
        private Text _stageTwrEngText;

        // 三轴操纵量标尺 (Pitch, Roll, Yaw)
        private class AxisMeterUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public GameObject LabelBg;
            public Image LabelBgImg;
            public Outline LabelOutline;
            public Text Label;
            public GameObject Track;
            public RectTransform TrackRt;
            public Image TrackBg;
            public Outline TrackOutline;
            public RectTransform FillRt;
            public Image FillImg;
            public RectTransform TrimRt;
            public Image TrimImg;
            public RectTransform CenterTickRt;
            public RectTransform TickNegRt;
            public RectTransform TickPosRt;
            public RectTransform SubTickNegRt;
            public RectTransform SubTickPosRt;
            public Text ValText;
            public RectTransform ValRt;
            public readonly CachedFloat LastTrim = new CachedFloat(-999f, tolerance: 0.005f);
        }

        private AxisMeterUI _pitchMeter;
        private AxisMeterUI _rollMeter;
        private AxisMeterUI _yawMeter;
        private readonly CachedFloat _cachedTrackWidth = new CachedFloat(104f, tolerance: 0.5f);

        // 分级推进剂计量槽
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
        private RectTransform _propTick25Rt;
        private RectTransform _propTick50Rt;
        private RectTransform _propTick75Rt;

        // 底部快捷切换按键组
        private Button _precBtn;
        private Image _precImg;
        private Outline _precOutline;
        private Text _precText;

        private Button _modeBtn;
        private Image _modeImg;
        private Outline _modeOutline;
        private Text _modeText;

        private Button _stockToggleBtn;
        private Image _stockToggleImg;
        private Outline _stockToggleOutline;
        private Text _stockToggleText;
        private readonly Cached<bool> _stockHidden = new Cached<bool>(true);

        // 动效与交互计时器 (全托管生命周期)
        private readonly CachedFloat _fireBtnRecoilTimer = new CachedFloat(0f, tolerance: 0.001f);
        private readonly CachedFloat _currentPropFrac = new CachedFloat(1f, tolerance: 0.001f);

        // 统一私有遥测快照 (零 GC 结构体，解耦遥测心跳与 UI 渲染)
        private struct StageSnapshot
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
        }

        private StageSnapshot _snap;

        // 统一全自动纳管私有状态 (切船/重置时 BaseFlightWidget 全自动复位，无需手写 OnResetPrivateCache！)
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
            // 1. 顶栏安全联动总成 (Header)
            // ==========================================
            if (Title != null)
            {
                Title.Text = I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL");
                Title.SetRole(TextStyleRole.Cardinal);
                if (Title.TextComponent != null)
                {
                    Title.TextComponent.fontSize = Mathf.RoundToInt(9.5f * s);
                    Title.TextComponent.fontStyle = FontStyle.Bold;
                    Title.TextComponent.alignment = TextAnchor.MiddleLeft;
                }
            }

            // 安全状态指示药丸 (ARMED / SAFE)
            _statusBadgePill = UIFactory.CreatePanel(transform, "StatusBadgePill",
                new Vector2(56f * s, 11f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _statusBadgePillBg = _statusBadgePill.GetComponent<Image>();
            _statusBadgePillOutline = _statusBadgePill.GetComponent<Outline>();

            _statusBadgeText = UIFactory.CreateText(_statusBadgePill.transform, "Text", "● " + I18n.Tr("WIDGET_ALERT_ARMED", "待发"),
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _statusBadgeText.fontStyle = FontStyle.Bold;
            _statusBadgeText.rectTransform.sizeDelta = new Vector2(56f * s, 11f * s);

            // 安全锁按键 (ARMED / SAFE)
            Vector2 lockBtnSize = new Vector2(42f * s, 18f * s);
            _lockBtn = UIFactory.CreateButton(transform, "Btn_Lock", lockBtnSize, Vector2.zero, OnToggleLock);
            _lockBtnBg = _lockBtn.GetComponent<Image>();
            _lockBtnBg.color = WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
            _lockBtnOutline = _lockBtn.gameObject.AddComponent<Outline>();
            _lockBtnOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _lockBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _lockBtnText = UIFactory.CreateText(_lockBtn.transform, "Text", I18n.Tr("WIDGET_ALERT_ARMED", "待发"),
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _lockBtnText.fontStyle = FontStyle.Bold;
            _lockBtnText.rectTransform.sizeDelta = lockBtnSize;
            _lockBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_LOCK_TITLE", "分级安全锁 (Alt+L)"),
                I18n.Tr("TOOLTIP_STAGE_LOCK_DESC", "切换火箭分级保险回路。处于锁定状态时阻断一切误触发操作。"), "Alt+L");

            // 分级触发键 (STAGE ▶)
            Vector2 fireBtnSize = new Vector2(46f * s, 18f * s);
            _fireBtn = UIFactory.CreateButton(transform, "Btn_Fire", fireBtnSize, Vector2.zero, OnFireStage);
            _fireBtnBg = _fireBtn.GetComponent<Image>();
            _fireBtnBg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            _fireBtnOutline = _fireBtn.gameObject.AddComponent<Outline>();
            _fireBtnOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _fireBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _fireBtnText = UIFactory.CreateText(_fireBtn.transform, "Text", I18n.Tr("WIDGET_STAGE_STAGE", "级 ▶"),
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _fireBtnText.fontStyle = FontStyle.Bold;
            _fireBtnText.rectTransform.sizeDelta = fireBtnSize;
            _fireBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_FIRE_TITLE", "分级触发 (Space)"),
                I18n.Tr("TOOLTIP_STAGE_FIRE_DESC", "手动执行下一分级点火分离序列。"), "Space");

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
                new Vector2(panelSize.x - 16f * s, 34f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _stageBayBg = _stageBayPanel.GetComponent<Image>();
            _stageBayOutline = _stageBayPanel.GetComponent<Outline>();

            // 左部：级数数显微窗 (内嵌黑底 + 霓虹指示条)
            Vector2 numBoxSize = new Vector2(32f * s, 28f * s);
            _stageNumBox = UIFactory.CreatePanel(_stageBayPanel.transform, "NumBox", numBoxSize,
                Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);
            _stageNumBoxBg = _stageNumBox.GetComponent<Image>();

            // 左边缘垂直霓虹条 (2.5px)
            GameObject barGo = UIFactory.CreatePanel(_stageNumBox.transform, "AccentBar",
                new Vector2(2.5f * s, 28f * s), new Vector2(-16f * s + 1.25f * s, 0f), theme.AccentPrimary);
            _stageAccentBar = barGo.GetComponent<Image>();

            _stageLabelText = UIFactory.CreateText(_stageNumBox.transform, "Label", I18n.Tr("WIDGET_CTRL_STAGE_LABEL", "级"),
                Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform slRt = _stageLabelText.GetComponent<RectTransform>();
            slRt.sizeDelta = new Vector2(numBoxSize.x - 4f * s, 8f * s);
            slRt.anchoredPosition = new Vector2(1f * s, 7.5f * s);

            _stageNumText = UIFactory.CreateText(_stageNumBox.transform, "StageNum", "07",
                Mathf.Max(8, Mathf.RoundToInt(12f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageNumText.fontStyle = FontStyle.Bold;
            RectTransform snRt = _stageNumText.GetComponent<RectTransform>();
            snRt.sizeDelta = new Vector2(numBoxSize.x - 4f * s, 15f * s);
            snRt.anchoredPosition = new Vector2(1f * s, -4f * s);

            // 右部：单级性能 (上行 Δv，下行燃烧时间与 TWR)
            _stageDvText = UIFactory.CreateText(_stageBayPanel.transform, "StageDv", "2,350 m/s",
                Mathf.Max(8, Mathf.RoundToInt(11f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageDvText.fontStyle = FontStyle.Bold;

            _stageTwrEngText = UIFactory.CreateText(_stageBayPanel.transform, "StageTwrEng", "⏱ 01:24 · 1.45 TWR · 4 ENG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));

            // ==========================================
            // 3. 三轴姿态操纵量仪表区 (Pitch, Roll, Yaw)
            // ==========================================
            _pitchMeter = BuildAxisMeter(transform, "Pitch", I18n.Tr("WIDGET_AXIS_PITCH", "PITCH"), s, theme);
            _rollMeter = BuildAxisMeter(transform, "Roll", I18n.Tr("WIDGET_AXIS_ROLL", "ROLL"), s, theme);
            _yawMeter = BuildAxisMeter(transform, "Yaw", I18n.Tr("WIDGET_AXIS_YAW", "YAW"), s, theme);

            // ==========================================
            // 4. 分级推进剂监测槽 (Propellant Tank)
            // ==========================================
            _propTagBg = UIFactory.CreatePanel(transform, "PropTagBg",
                new Vector2(76f * s, 11f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _propTagBgImg = _propTagBg.GetComponent<Image>();
            _propTagOutline = _propTagBg.GetComponent<Outline>();

            _propNameText = UIFactory.CreateText(_propTagBg.transform, "PropName", I18n.Tr("WIDGET_PROP_PROPELLANT", "PROPELLANT"),
                Mathf.Max(5, Mathf.RoundToInt(6f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _propNameText.rectTransform.sizeDelta = new Vector2(76f * s, 11f * s);

            _propPctText = UIFactory.CreateText(transform, "PropPct", "100.0%",
                Mathf.Max(6, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _propPctText.fontStyle = FontStyle.Bold;

            // 推进剂槽轨道 (带刻度标尺与发光条)
            _propTrackPanel = UIFactory.CreatePanel(transform, "PropTrack",
                new Vector2(panelSize.x - 16f * s, 4f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _propTrackBg = _propTrackPanel.GetComponent<Image>();
            _propTrackOutline = _propTrackPanel.GetComponent<Outline>();

            // 刻度微线 (25%, 50%, 75%)
            Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            GameObject t25 = UIFactory.CreatePanel(_propTrackPanel.transform, "Tick25", new Vector2(1f * s, 4f * s), Vector2.zero, tickCol);
            _propTick25Rt = t25.GetComponent<RectTransform>();
            GameObject t50 = UIFactory.CreatePanel(_propTrackPanel.transform, "Tick50", new Vector2(1f * s, 4f * s), Vector2.zero, tickCol);
            _propTick50Rt = t50.GetComponent<RectTransform>();
            GameObject t75 = UIFactory.CreatePanel(_propTrackPanel.transform, "Tick75", new Vector2(1f * s, 4f * s), Vector2.zero, tickCol);
            _propTick75Rt = t75.GetComponent<RectTransform>();

            // 推进剂填充条
            GameObject propFill = UIFactory.CreatePanel(_propTrackPanel.transform, "Fill",
                new Vector2(panelSize.x - 16f * s, 4f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            _propFillRt = propFill.GetComponent<RectTransform>();
            _propFillRt.anchorMin = new Vector2(0f, 0.5f);
            _propFillRt.anchorMax = new Vector2(0f, 0.5f);
            _propFillRt.pivot = new Vector2(0f, 0.5f);
            _propFillRt.anchoredPosition = Vector2.zero;
            _propFillImg = propFill.GetComponent<Image>();

            // ==========================================
            // 5. 底部快捷模式切换按键组
            // ==========================================
            Vector2 miniBtnSize = new Vector2(58f * s, 18f * s);

            _precBtn = UIFactory.CreateButton(transform, "Btn_Prec", miniBtnSize, Vector2.zero, OnTogglePrecision);
            _precImg = _precBtn.GetComponent<Image>();
            _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _precOutline = _precBtn.gameObject.AddComponent<Outline>();
            _precOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _precOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _precText = UIFactory.CreateText(_precBtn.transform, "Text", "NORM",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _precText.fontStyle = FontStyle.Bold;
            _precText.rectTransform.sizeDelta = miniBtnSize;
            _precBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_PREC_TITLE", "操纵微调 (Caps Lock)"),
                I18n.Tr("TOOLTIP_STAGE_PREC_DESC", "切换常规舵面操纵与精密微调操纵模式。"), "Caps Lock");

            _modeBtn = UIFactory.CreateButton(transform, "Btn_Mode", miniBtnSize, Vector2.zero, OnToggleMode);
            _modeImg = _modeBtn.GetComponent<Image>();
            _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeOutline = _modeBtn.gameObject.AddComponent<Outline>();
            _modeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _modeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _modeText = UIFactory.CreateText(_modeBtn.transform, "Text", "STG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _modeText.fontStyle = FontStyle.Bold;
            _modeText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;
            _modeBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_MODE_TITLE", "控制模式切换"),
                I18n.Tr("TOOLTIP_STAGE_MODE_DESC", "在常规分级飞行姿态模式与对接口平移操纵模式间切换。"), "Flight Mode");

            _stockToggleBtn = UIFactory.CreateButton(transform, "Btn_Stock", miniBtnSize, Vector2.zero, OnToggleStockVisibility);
            _stockToggleImg = _stockToggleBtn.GetComponent<Image>();
            _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stockToggleOutline = _stockToggleBtn.gameObject.AddComponent<Outline>();
            _stockToggleOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _stockToggleOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            _stockToggleText = UIFactory.CreateText(_stockToggleBtn.transform, "Text", "KSP HUD",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stockToggleText.fontStyle = FontStyle.Bold;
            _stockToggleText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;
            _stockToggleBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_STOCK_TITLE", "原生界面显隐"),
                I18n.Tr("TOOLTIP_STAGE_STOCK_DESC", "隐藏或还原游戏左下角原版分级控制面板。"), "HUD Toggle");

            // 初始化时默认执行静默隐藏原版左下角
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden.Value);

            // 标准化组件内部控件注册至管理器
            if (_fireBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_fire_btn", "分级点火触发键", _fireBtn.gameObject, _fireBtn, _fireBtnBg, null, _fireBtnText, null, "STAGE", OnFireStage, false));
            }
            if (_lockBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_lock_btn", "分级安全锁按键", _lockBtn.gameObject, _lockBtn, _lockBtnBg, null, _lockBtnText, null, "ARMED", OnToggleLock, true));
            }
            if (_stageBayPanel != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "stage_telemetry_bay", "分级数据凹槽窗", _stageBayPanel);
            }
            if (_propTrackBg != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "propellant_meter", "推进剂余量槽", _propTrackBg.gameObject);
            }

            ApplyLayout(panelSize.x, panelSize.y);
            ApplyTheme(theme);
        }

        private AxisMeterUI BuildAxisMeter(Transform parent, string name, string label, float s, ThemeConfig theme)
        {
            AxisMeterUI meter = new AxisMeterUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            meter.RootRt = CreateContainer("Axis_" + name, parent, new Vector2(DefaultPanelWidth * s, 15f * s));
            meter.Root = meter.RootRt.gameObject;
            GameObject rootGo = meter.Root;

            // 1. 轴标签胶囊 (PITCH / ROLL / YAW)
            meter.LabelBg = UIFactory.CreatePanel(rootGo.transform, "LabelBg",
                new Vector2(34f * s, 13f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            meter.LabelBgImg = meter.LabelBg.GetComponent<Image>();
            meter.LabelOutline = meter.LabelBg.GetComponent<Outline>();

            meter.Label = UIFactory.CreateText(meter.LabelBg.transform, "Text", label,
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            meter.Label.fontStyle = FontStyle.Bold;
            meter.Label.rectTransform.sizeDelta = new Vector2(34f * s, 13f * s);

            // 2. 标尺轨道背景 (宽动态自适应, 高 4px)
            meter.Track = UIFactory.CreatePanel(rootGo.transform, "Track",
                new Vector2(_cachedTrackWidth * s, 4f * s), Vector2.zero,
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            meter.TrackRt = meter.Track.GetComponent<RectTransform>();
            meter.TrackBg = meter.Track.GetComponent<Image>();
            meter.TrackOutline = meter.Track.GetComponent<Outline>();

            // 两端极限量程刻线 (-100% / +100%)
            Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            GameObject tNeg = UIFactory.CreatePanel(meter.Track.transform, "TickNeg",
                new Vector2(1f * s, 6f * s), Vector2.zero, tickCol);
            meter.TickNegRt = tNeg.GetComponent<RectTransform>();

            GameObject tPos = UIFactory.CreatePanel(meter.Track.transform, "TickPos",
                new Vector2(1f * s, 6f * s), Vector2.zero, tickCol);
            meter.TickPosRt = tPos.GetComponent<RectTransform>();

            // 四分度刻线 (-50% / +50%)
            GameObject stNeg = UIFactory.CreatePanel(meter.Track.transform, "SubTickNeg",
                new Vector2(1f * s, 4f * s), Vector2.zero, tickCol);
            meter.SubTickNegRt = stNeg.GetComponent<RectTransform>();

            GameObject stPos = UIFactory.CreatePanel(meter.Track.transform, "SubTickPos",
                new Vector2(1f * s, 4f * s), Vector2.zero, tickCol);
            meter.SubTickPosRt = stPos.GetComponent<RectTransform>();

            // 零位中心基准中轴线 (高亮垂直小长条)
            GameObject cTick = UIFactory.CreatePanel(meter.Track.transform, "CenterTick",
                new Vector2(2f * s, 8f * s), Vector2.zero, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            meter.CenterTickRt = cTick.GetComponent<RectTransform>();

            // 双向动态偏转填充条 (HUD Beam)
            GameObject fill = UIFactory.CreatePanel(meter.Track.transform, "Fill",
                new Vector2(0f, 4f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            meter.FillRt = fill.GetComponent<RectTransform>();
            meter.FillRt.anchorMin = new Vector2(0.5f, 0.5f);
            meter.FillRt.anchorMax = new Vector2(0.5f, 0.5f);
            meter.FillRt.pivot = new Vector2(0.5f, 0.5f);
            meter.FillImg = fill.GetComponent<Image>();

            // 配平指示游标 (Trim Pip)
            GameObject trim = UIFactory.CreatePanel(meter.Track.transform, "TrimPip",
                new Vector2(2.5f * s, 8f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Warning, theme));
            meter.TrimRt = trim.GetComponent<RectTransform>();
            meter.TrimImg = trim.GetComponent<Image>();

            // 偏转读数百分比 (+24%)
            meter.ValText = UIFactory.CreateText(rootGo.transform, "Val", "0%",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            meter.ValText.fontStyle = FontStyle.Bold;
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

            // 1. 顶栏安全联动总成排版
            float topRowY = halfH - 14f * s;
            float titleW = Mathf.Clamp(width * 0.38f, 55f * s, 110f * s);
            if (Title != null && Title.RootGameObject != null)
            {
                RectTransform tRt = Title.RootGameObject.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchorMin = new Vector2(0.5f, 0.5f);
                    tRt.anchorMax = new Vector2(0.5f, 0.5f);
                    tRt.pivot = new Vector2(0.5f, 0.5f);
                    tRt.sizeDelta = new Vector2(titleW, 14f * s);
                    tRt.anchoredPosition = new Vector2(-halfW + margin + titleW * 0.5f, topRowY + 5f * s);
                }
            }

            if (_statusBadgePill != null)
            {
                RectTransform sbRt = _statusBadgePill.GetComponent<RectTransform>();
                sbRt.anchoredPosition = new Vector2(-halfW + margin + 28f * s, topRowY - 6.5f * s);
            }

            float fireW = 46f * s;
            float lockW = 42f * s;
            if (_fireBtn != null)
            {
                RectTransform fRt = _fireBtn.GetComponent<RectTransform>();
                fRt.sizeDelta = new Vector2(fireW, 18f * s);
                fRt.anchoredPosition = new Vector2(halfW - margin - fireW * 0.5f, topRowY);
            }

            if (_lockBtn != null)
            {
                RectTransform lRt = _lockBtn.GetComponent<RectTransform>();
                lRt.sizeDelta = new Vector2(lockW, 18f * s);
                lRt.anchoredPosition = new Vector2(halfW - margin - fireW - 4f * s - lockW * 0.5f, topRowY);
            }

            if (_topDivider != null)
            {
                RectTransform topDivRt = _topDivider.rectTransform;
                topDivRt.sizeDelta = new Vector2(contentW, 1f * s);
                topDivRt.anchoredPosition = new Vector2(0f, topRowY - 12f * s);
            }

            // 2. 分级遥测中枢凹槽窗排版
            float bayH = 32f * s;
            float bayY = topRowY - 29f * s;
            if (_stageBayPanel != null)
            {
                RectTransform bayRt = _stageBayPanel.GetComponent<RectTransform>();
                bayRt.sizeDelta = new Vector2(contentW, bayH);
                bayRt.anchoredPosition = new Vector2(0f, bayY);
            }

            if (_stageNumBox != null)
            {
                RectTransform nbRt = _stageNumBox.GetComponent<RectTransform>();
                nbRt.anchoredPosition = new Vector2(-contentW * 0.5f + 20f * s, 0f);
            }

            float telemStartX = -contentW * 0.5f + 42f * s;
            float telemW = contentW - 46f * s;
            if (_stageDvText != null)
            {
                RectTransform dvRt = _stageDvText.rectTransform;
                dvRt.sizeDelta = new Vector2(telemW, 15f * s);
                dvRt.anchoredPosition = new Vector2(telemStartX + telemW * 0.5f, 6.5f * s);
            }

            if (_stageTwrEngText != null)
            {
                RectTransform twrRt = _stageTwrEngText.rectTransform;
                twrRt.sizeDelta = new Vector2(telemW, 12f * s);
                twrRt.anchoredPosition = new Vector2(telemStartX + telemW * 0.5f, -6.5f * s);
            }

            // 3. 动态纵向空间与三轴姿态操纵量仪表区排版
            float bottomY = -halfH + 13f * s;
            float bottomDivY = bottomY + 12f * s;
            float propBarY = bottomDivY + 13f * s;
            float propHeaderY = propBarY + 10f * s;

            float axisBayTopY = bayY - bayH * 0.5f - 11f * s;
            float axisBayBottomY = propHeaderY + 10f * s;
            float axisSpacing = Mathf.Clamp((axisBayTopY - axisBayBottomY) * 0.5f, 15f * s, 22f * s);

            float labelW = 34f * s;
            float valW = 34f * s;
            float trackW = Mathf.Max(60f * s, contentW - labelW - valW - 14f * s);
            _cachedTrackWidth.Update(trackW / (s > 0f ? s : 1f));

            LayoutAxisMeter(_pitchMeter, axisBayTopY, halfW, margin, labelW, trackW, valW, s);
            LayoutAxisMeter(_rollMeter, axisBayTopY - axisSpacing, halfW, margin, labelW, trackW, valW, s);
            LayoutAxisMeter(_yawMeter, axisBayTopY - axisSpacing * 2f, halfW, margin, labelW, trackW, valW, s);

            // 4. 分级推进剂计量槽排版
            float tagW = Mathf.Clamp(contentW * 0.38f, 56f * s, 86f * s);
            if (_propTagBg != null)
            {
                RectTransform tagRt = _propTagBg.GetComponent<RectTransform>();
                tagRt.sizeDelta = new Vector2(tagW, 12f * s);
                tagRt.anchoredPosition = new Vector2(-halfW + margin + tagW * 0.5f, propHeaderY);
                if (_propNameText != null) _propNameText.rectTransform.sizeDelta = new Vector2(tagW, 12f * s);
            }

            if (_propPctText != null)
            {
                RectTransform pctRt = _propPctText.rectTransform;
                pctRt.sizeDelta = new Vector2(50f * s, 12f * s);
                pctRt.anchoredPosition = new Vector2(halfW - margin - 25f * s, propHeaderY);
            }

            if (_propTrackPanel != null)
            {
                RectTransform trkRt = _propTrackPanel.GetComponent<RectTransform>();
                trkRt.sizeDelta = new Vector2(contentW, 4f * s);
                trkRt.anchoredPosition = new Vector2(0f, propBarY);

                float halfCw = contentW * 0.5f;
                if (_propTick25Rt != null) _propTick25Rt.anchoredPosition = new Vector2(-halfCw * 0.5f, 0f);
                if (_propTick50Rt != null) _propTick50Rt.anchoredPosition = new Vector2(0f, 0f);
                if (_propTick75Rt != null) _propTick75Rt.anchoredPosition = new Vector2(halfCw * 0.5f, 0f);
            }

            if (_bottomDivider != null)
            {
                RectTransform botDivRt = _bottomDivider.rectTransform;
                botDivRt.sizeDelta = new Vector2(contentW, 1f * s);
                botDivRt.anchoredPosition = new Vector2(0f, bottomDivY);
            }

            // 5. 底部快捷模式切换按键组排版
            float btnW = (contentW - 8f * s) / 3f;
            Vector2 miniBtnSize = new Vector2(btnW, 18f * s);

            if (_precBtn != null)
            {
                RectTransform precRt = _precBtn.GetComponent<RectTransform>();
                precRt.sizeDelta = miniBtnSize;
                precRt.anchoredPosition = new Vector2(-halfW + margin + btnW * 0.5f, bottomY);
                if (_precText != null) _precText.rectTransform.sizeDelta = miniBtnSize;
            }

            if (_modeBtn != null)
            {
                RectTransform modeRt = _modeBtn.GetComponent<RectTransform>();
                modeRt.sizeDelta = miniBtnSize;
                modeRt.anchoredPosition = new Vector2(0f, bottomY);
                if (_modeText != null) _modeText.rectTransform.sizeDelta = miniBtnSize;
            }

            if (_stockToggleBtn != null)
            {
                RectTransform stRt = _stockToggleBtn.GetComponent<RectTransform>();
                stRt.sizeDelta = miniBtnSize;
                stRt.anchoredPosition = new Vector2(halfW - margin - btnW * 0.5f, bottomY);
                if (_stockToggleText != null) _stockToggleText.rectTransform.sizeDelta = miniBtnSize;
            }
        }

        private void LayoutAxisMeter(AxisMeterUI meter, float yPos, float halfW, float margin, float labelW, float trackW, float valW, float s)
        {
            if (meter == null || meter.RootRt == null) return;
            meter.RootRt.anchoredPosition = new Vector2(0f, yPos);

            if (meter.LabelBg != null)
            {
                RectTransform lRt = meter.LabelBg.GetComponent<RectTransform>();
                lRt.sizeDelta = new Vector2(labelW, 13f * s);
                lRt.anchoredPosition = new Vector2(-halfW + margin + labelW * 0.5f, 0f);
                if (meter.Label != null) meter.Label.rectTransform.sizeDelta = lRt.sizeDelta;
            }

            if (meter.TrackRt != null)
            {
                meter.TrackRt.sizeDelta = new Vector2(trackW, 4f * s);
                float trackCenterX = -halfW + margin + labelW + 7f * s + trackW * 0.5f;
                meter.TrackRt.anchoredPosition = new Vector2(trackCenterX, 0f);

                float halfTrack = trackW * 0.5f;
                if (meter.TickNegRt != null) meter.TickNegRt.anchoredPosition = new Vector2(-halfTrack, 0f);
                if (meter.TickPosRt != null) meter.TickPosRt.anchoredPosition = new Vector2(halfTrack, 0f);
                if (meter.SubTickNegRt != null) meter.SubTickNegRt.anchoredPosition = new Vector2(-halfTrack * 0.5f, 0f);
                if (meter.SubTickPosRt != null) meter.SubTickPosRt.anchoredPosition = new Vector2(halfTrack * 0.5f, 0f);
                if (meter.CenterTickRt != null) meter.CenterTickRt.anchoredPosition = Vector2.zero;
            }

            if (meter.ValRt != null)
            {
                meter.ValRt.sizeDelta = new Vector2(valW, 14f * s);
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
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            if (context.Telemetry == null || !context.Telemetry.HasVessel)
            {
                _snap.HasVessel = false;
                return;
            }

            _snap.HasVessel = true;
            IFlightTelemetry telem = context.Telemetry;

            _snap.IsLocked = telem.IsStageLocked || StockStageActionService.IsStagingLocked;
            _snap.CurrentStage = telem.CurrentStage;
            _snap.StageDeltaV = telem.StageDeltaV;
            _snap.StageBurnTime = telem.StageBurnTime;
            _snap.Twr = (float)telem.TWR;
            _snap.ActiveEngines = telem.ActiveEngines;
            _snap.PitchInput = telem.PitchInput;
            _snap.PitchTrim = telem.PitchTrim;
            _snap.RollInput = telem.RollInput;
            _snap.RollTrim = telem.RollTrim;
            _snap.YawInput = telem.YawInput;
            _snap.YawTrim = telem.YawTrim;
            _snap.StagePropellantFraction = Mathf.Clamp01((float)telem.StagePropellantFraction);

            string rawName = telem.StagePropellantName;
            if (string.IsNullOrEmpty(rawName)) rawName = "PROPELLANT";
            if (rawName.StartsWith("PROP:", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(5).Trim();
            else if (rawName.StartsWith("PROP", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(4).Trim();
            if (string.IsNullOrEmpty(rawName)) rawName = "PROPELLANT";
            _snap.PropName = rawName.ToUpperInvariant();

            _snap.IsPrecisionControl = telem.IsPrecisionControl;
            _snap.IsDockingMode = telem.IsDockingMode;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_snap.HasVessel) return;

            float s = CurrentDpiScale;
            float dt = context.DeltaTime;

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.ResolveTheme(_cachedTheme);
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
            if (_dirtyLocked.Update(_snap.IsLocked))
            {
                bool isLocked = _dirtyLocked.Value;
                string lockLabel = isLocked
                    ? I18n.Tr("WIDGET_STAGE_LOCKED", "锁定")
                    : I18n.Tr("WIDGET_ALERT_ARMED", "待发");
                SetTextIfChanged(_lockBtnText, lockLabel);

                if (isLocked)
                {
                    _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    ApplyText(_lockBtnText, TextStyleRole.Danger, theme);
                    _fireBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    ApplyText(_fireBtnText, TextStyleRole.SecondaryValue, theme);

                    SetTextIfChanged(_statusBadgeText, "▲ " + I18n.Tr("WIDGET_STAGE_LOCKED", "锁定"));
                    ApplyText(_statusBadgeText, TextStyleRole.Danger, theme);
                    if (_statusBadgePillBg != null) _statusBadgePillBg.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.15f);
                    if (_statusBadgePillOutline != null) _statusBadgePillOutline.effectColor = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Ghost);
                }
                else
                {
                    _lockBtnBg.color = WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
                    ApplyText(_lockBtnText, TextStyleRole.Accent, theme);
                    _fireBtnBg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
                    ApplyText(_fireBtnText, TextStyleRole.PrimaryValue, theme);

                    SetTextIfChanged(_statusBadgeText, "● " + I18n.Tr("WIDGET_ALERT_ARMED", "待发"));
                    ApplyText(_statusBadgeText, TextStyleRole.Accent, theme);
                    if (_statusBadgePillBg != null) _statusBadgePillBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.15f);
                    if (_statusBadgePillOutline != null) _statusBadgePillOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
                }
            }

            // 按键微回弹动效
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

            // 3. 分级数字读数与性能参数 (DirtyField 守卫)
            if (_dirtyStageNum.Update(_snap.CurrentStage))
            {
                SetTextIfChanged(_stageNumText, _snap.CurrentStage.ToString("D2"));
            }

            if (_dirtyStageDv.Update(_snap.StageDeltaV))
            {
                double dv = _dirtyStageDv.Value;
                string dvStr = dv > 0.1 ? $"{dv:N0} m/s" : "0 m/s";
                SetTextIfChanged(_stageDvText, dvStr);
            }

            int burnSec = Mathf.Max(0, (int)_snap.StageBurnTime);
            bool burnDirty = _dirtyBurnSec.Update(burnSec);
            bool twrDirty = _dirtyTwr.Update(_snap.Twr);
            bool engDirty = _dirtyActiveEngines.Update(_snap.ActiveEngines);
            if (burnDirty || twrDirty || engDirty)
            {
                int m = burnSec / 60;
                int sec = burnSec % 60;
                float twr = _dirtyTwr.Value;
                int eng = _dirtyActiveEngines.Value;
                string twrStr = twr > 0.01f 
                    ? $"⏱ {m:00}:{sec:00} · {twr:F2} TWR · ⚙ {eng} ENG" 
                    : $"⏱ {m:00}:{sec:00} · ⚙ {eng} ENG";
                SetTextIfChanged(_stageTwrEngText, twrStr);

                if (_stageAccentBar != null)
                {
                    _stageAccentBar.color = eng > 0 ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }

            // 4. 三轴舵面偏转与配平
            float curTrackW = _cachedTrackWidth.Value * s;
            UpdateAxisVisuals(_pitchMeter, _snap.PitchInput, _snap.PitchTrim, curTrackW, s, theme, _lastPitchPct);
            UpdateAxisVisuals(_rollMeter, _snap.RollInput, _snap.RollTrim, curTrackW, s, theme, _lastRollPct);
            UpdateAxisVisuals(_yawMeter, _snap.YawInput, _snap.YawTrim, curTrackW, s, theme, _lastYawPct);

            // 5. 分级推进剂指示条 (100% 语义驱动)
            float targetPropFrac = _snap.StagePropellantFraction;
            float nextProp = Mathf.Lerp(_currentPropFrac.Value < 0f ? targetPropFrac : _currentPropFrac.Value, targetPropFrac, 0.25f);
            _currentPropFrac.Update(nextProp);

            if (_dirtyPropName.Update(_snap.PropName))
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
            if (_dirtyPrec.Update(_snap.IsPrecisionControl))
            {
                bool isPrec = _dirtyPrec.Value;
                string pStr = isPrec ? "● PREC" : "NORM";
                SetTextIfChanged(_precText, pStr);
                ApplyText(_precText, isPrec ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
                if (_precImg != null) _precImg.color = isPrec ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_precOutline != null) _precOutline.effectColor = isPrec ? WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            if (_dirtyDock.Update(_snap.IsDockingMode))
            {
                bool isDock = _dirtyDock.Value;
                string mStr = isDock ? "● DCK" : "STG";
                SetTextIfChanged(_modeText, mStr);
                ApplyText(_modeText, isDock ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                if (_modeImg != null) _modeImg.color = isDock ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_modeOutline != null) _modeOutline.effectColor = isDock ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
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
                if (meter.FillRt != null)
                {
                    float fillWidth = Mathf.Abs(clampedInput) * halfWidth;
                    float fillCenterOffset = (clampedInput >= 0f) ? (fillWidth * 0.5f) : (-fillWidth * 0.5f);
                    meter.FillRt.sizeDelta = new Vector2(fillWidth, 4f * s);
                    meter.FillRt.anchoredPosition = new Vector2(fillCenterOffset, 0f);
                }

                if (meter.ValText != null)
                {
                    string str = (pct > 0) ? "+" + CacheManager.FastPercent(pct) : (pct == 0 ? "0%" : CacheManager.FastInt(pct) + "%");
                    SetTextIfChanged(meter.ValText, str);
                    TextStyleRole role = Mathf.Abs(pct) > 3 ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue;
                    ApplyText(meter.ValText, role, theme);
                }
            }

            if (meter.TrimRt != null && meter.LastTrim.Update(trim))
            {
                float clampedTrim = Mathf.Clamp(trim, -1f, 1f);
                meter.TrimRt.anchoredPosition = new Vector2(clampedTrim * halfWidth, 0f);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            _cachedTheme = theme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 顶栏
            if (Title != null)
            {
                Title.Text = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL"));
                Title.SetRole(TextStyleRole.Cardinal);
            }

            bool locked = _dirtyLocked.Value;
            if (_statusBadgePillBg != null) _statusBadgePillBg.color = locked ? WidgetStyleManager.WithAlpha(theme.DangerColor, 0.15f) : WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.15f);
            if (_statusBadgePillOutline != null) _statusBadgePillOutline.effectColor = locked ? WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            if (_statusBadgeText != null) ApplyText(_statusBadgeText, locked ? TextStyleRole.Danger : TextStyleRole.Accent, theme);

            if (_lockBtnBg != null) _lockBtnBg.color = locked ? WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme) : WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
            if (_lockBtnOutline != null) _lockBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_lockBtnText != null) ApplyText(_lockBtnText, locked ? TextStyleRole.Danger : TextStyleRole.Accent, theme);

            if (_fireBtnBg != null) _fireBtnBg.color = locked ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : style.GetMeterColor(MeterStyleRole.Warning, theme);
            if (_fireBtnOutline != null) _fireBtnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_fireBtnText != null) ApplyText(_fireBtnText, TextStyleRole.PrimaryValue, theme);

            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 分级中枢凹槽窗
            if (_stageBayBg != null) _stageBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageBayOutline != null) _stageBayOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_stageNumBoxBg != null) _stageNumBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_stageLabelText != null) ApplyText(_stageLabelText, TextStyleRole.SecondaryValue, theme);
            if (_stageNumText != null) ApplyText(_stageNumText, TextStyleRole.PrimaryValue, theme);
            if (_stageDvText != null) ApplyText(_stageDvText, TextStyleRole.PrimaryValue, theme);
            if (_stageTwrEngText != null) ApplyText(_stageTwrEngText, TextStyleRole.SecondaryValue, theme);

            // 三轴仪表着色
            ApplyThemeToAxisMeter(_pitchMeter, theme);
            ApplyThemeToAxisMeter(_rollMeter, theme);
            ApplyThemeToAxisMeter(_yawMeter, theme);

            // 推进剂槽
            if (_propTagBgImg != null) _propTagBgImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_propTagOutline != null) _propTagOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_propNameText != null) ApplyText(_propNameText, TextStyleRole.SecondaryValue, theme);
            if (_propPctText != null) ApplyText(_propPctText, TextStyleRole.PrimaryValue, theme);
            if (_propTrackBg != null) _propTrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_propTrackOutline != null) _propTrackOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_propFillImg != null) _propFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            // 底部按键
            bool prec = _dirtyPrec.Value;
            if (_precImg != null) _precImg.color = prec ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_precOutline != null) _precOutline.effectColor = prec ? WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_precText != null) ApplyText(_precText, prec ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);

            bool dock = _dirtyDock.Value;
            if (_modeImg != null) _modeImg.color = dock ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_modeOutline != null) _modeOutline.effectColor = dock ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost) : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_modeText != null) ApplyText(_modeText, dock ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);

            if (_stockToggleImg != null) _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockToggleOutline != null) _stockToggleOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            UpdateStockToggleButtonState();
        }

        private void ApplyThemeToAxisMeter(AxisMeterUI meter, ThemeConfig theme)
        {
            if (meter == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            if (meter.LabelBgImg != null) meter.LabelBgImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (meter.LabelOutline != null) meter.LabelOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (meter.Label != null) ApplyText(meter.Label, TextStyleRole.SecondaryValue, theme);

            if (meter.TrackBg != null) meter.TrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (meter.TrackOutline != null) meter.TrackOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (meter.FillImg != null) meter.FillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
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
