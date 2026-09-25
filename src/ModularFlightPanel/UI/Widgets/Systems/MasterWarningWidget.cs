using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 中央主告警与警报光字牌 (Master Warning & Caution Annunciator Widget)
    /// 严丝合缝嵌合于主导航球正下方。
    /// 
    /// 工业级航电设计规范 (Industrial Avionics Bezel Architecture)：
    /// 1. 架构形态 (Morphology Options)：
    ///    - 3模块 金字塔型 (3-Module Pyramid Morphology, 默认)：
    ///      * 上层甲板 (Row 1): 双室 Korry 光字牌 [提醒 CAUTION][警告 WARNING]，尺寸 184x18px，中间以机械隔离筋条相隔；
    ///      * 下层底座 (Row 2): 全幅飞行状态提醒窗 [状态提醒]，尺寸 180x18px，以水平金属嵌条相隔；
    ///      * 总尺寸 184x42px，告警光字牌永远在线（不被瞬态事件遮挡），状态提醒窗常态显示巡航工况，瞬态突发时高亮脉冲；
    ///    - 2模块 经典聚拢闪回型 (2-Module Classic Morphing Matrix)：
    ///      * 常态为 184x22px 双室光字牌；
    ///      * 当分级分离/引擎启动/机动节点等事件发生时，双框平滑向中心聚拢合并，显示高反差航电大字横幅，维持后迅速闪回双室。
    /// 2. 交互与配置自由切换 (User Selection)：
    ///    - 支持在 CustomTemplate 中写入 "MODULES=2;" 或 "MODULES=3;" (或 "MODE=2; MODE=3;");
    ///    - 支持在交互界面中直接点击中央隔离筋条在 2 模块与 3 模块之间无缝切换并热重排布局。
    /// 3. 飞行状态事件全景目录 (Flight Status Events Catalog)：
    ///    - 分离 (Separation)、引擎启动 (EngineStart)、主发关机 (MECO)、
    ///    - 接近机动节点 (ManeuverApproach T-60s)、机动点火执行 (ManeuverBurn)、
    ///    - 入轨圆化完成 (OrbitAchieved)、再入大气层 (AtmosphereEntry)、
    ///    - 通过远/近拱点 (Ap/Pe Pass)、对接模式 (DockingMode)、着陆接地成功 (Touchdown)。
    /// 4. 100% 遵照 MFP 六大铁律 (0 颜色字面量、纯 C# 解耦、30Hz 分频、零 GC)。
    /// </summary>
    [FlightWidget("master_warning", "warning_annunciator", "annunciator", "cws", Category = WidgetCategory.Systems, DisplayName = "中央主告警光字牌", Description = "双等级航电警告光字牌：黄色注意与红色危急双通道轮播，支持拉起、失速、低油、低电、缺氧全量监测，点击可消警。", DefaultWidgetId = "core.master_warning", DefaultX = 0f, DefaultY = -66f, IsSingleton = true, ExactIds = new[] { "core.master_warning" })]
    public class MasterWarningWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 模块数量：2 (经典聚拢) 或 3 (金字塔型)
        private int _modulesCount = 3;
        public int ModulesCount => _modulesCount;

        // 外部底板与装饰构件
        private Image _outerBezel;
        private Outline _outerOutline;
        private Image _centerDivider;
        private Button _centerDividerBtn;
        private Image _horizDivider;
        private Button _horizDividerBtn;

        // 告警单元结构体
        private struct AlertItem
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
        }

        // 瞬态机动横幅状态机阶段
        private enum BannerDisplayState
        {
            Normal,         // 标准工作/待命 (Caution & Warning)
            MergingIn,      // 双室向中央滑动合并 (2模块模式, 约 0.10s) / 状态条滑入 (3模块模式)
            MergedHolding,  // 一体横幅高亮呈现当前事件 (单事件 1.25s ~ 2.0s / 链式连击事件 0.95s)
            SwitchingEvent, // 队列中存在后续事件，就地微闪平滑切换至下一事件 (约 0.08s)
            FlashingBack    // 队列全部消费完毕，高频频闪复位回到常态 (约 0.14s)
        }

        public enum BannerEventType
        {
            Separation,         // 分级分离 / 脱扣
            EngineStart,        // 引擎启动 / 点火
            MECO,               // 主发关机 / 熄火
            ManeuverApproach,   // 接近机动节点 (T-60s)
            ManeuverBurn,       // 机动点火执行
            OrbitAchieved,      // 入轨圆化完成 (Stable Orbit)
            Deorbit,            // 飞船离轨制动 / 进入再入走廊
            AtmosphereEntry,    // 再入/进入大气层 (Entry Interface)
            Blackout,           // 再入等离子体黑障
            Escape,             // 逃逸轨道建立 (双曲线逃逸)
            SoiTransition,      // 穿越引力范围 (SOI 切换)
            SuicideBurn,        // 动力减速着陆点火
            ApoapsisPass,       // 通过远拱点
            PeriapsisPass,      // 通过近拱点
            DockingMode,        // 进入对接模式
            Touchdown           // 着陆接地成功
        }

        private enum EventColorRole
        {
            AccentPrimary,
            AccentSecondary,
            WarningColor,
            DangerColor,
            Success
        }

        // 横幅事件定义模型 (轻量结构体，0 GC)
        private struct BannerEventItem
        {
            public BannerEventType EventType;
            public string Title;
            public string Sub;
            public string LeftIcon;
            public string RightIcon;
            public float Duration;
            public int Priority; // 调度优先级 (数值越大越优先展示)
            public EventColorRole ColorRole;

            public BannerEventItem(BannerEventType type, string title, string sub, string leftIcon, string rightIcon, float duration, int priority, EventColorRole colorRole)
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

        // 事件防抖与冷却时间戳 (Debounce & Cooldown: 防止同事件多帧连续触发导致互相打架)
        private readonly Dictionary<BannerEventType, float> _eventLastTriggerTimes = new Dictionary<BannerEventType, float>(16);
        private const float EVENT_COOLDOWN = 1.6f;

        // 低油量安全门限与时域防抖滤波 (防止点火瞬态与误读触发假警报)
        private float _customWarnThresh = -1f;
        private float _customCautThresh = -1f;
        private float _lowFuelPersistentTimer = 0f;
        private const float LOW_FUEL_PERSISTENCE = 0.35f;

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

        // 上一帧遥测缓存 (用于瞬态边缘脉冲触发)
        private int _lastStage = -1;
        private int _lastActiveEngines = -1;
        private float _lastThrottle = -1f;
        private bool _lastIsStageSeparating = false;
        private bool _lastIsEngineIgniting = false;
        private double _lastTimeToNode = -1.0;
        private bool _lastManeuverBurnTriggered = false;
        private double _lastPeriapsis = -9999999.0;
        private double _lastAltitude = -1.0;
        private double _lastTimeToAp = -1.0;
        private double _lastTimeToPe = -1.0;
        private bool _lastIsDockingMode = false;
        private bool _lastIsLanded = true;
        private double _lastAltitudeAGL = 0.0;
        private double _lastEffectivePe = -9999999.0;
        private double _lastEffectiveAp = -9999999.0;
        private string _lastCelestialBody = null;
        private string _lastFlightSituation = null;

        // 左舱：Caution (黄色注意) 视图组件
        private GameObject _cautCell;
        private RectTransform _cautRect;
        private Image _cautBg;
        private Outline _cautOutline;
        private Image _cautPipBar;
        private Text _cautIcon;
        private Text _cautTitle;
        private Text _cautSub;
        private Button _cautBtn;

        // 右舱：Warning (红色危急) 视图组件
        private GameObject _warnCell;
        private RectTransform _warnRect;
        private Image _warnBg;
        private Outline _warnOutline;
        private Image _warnPipBar;
        private Text _warnIcon;
        private Text _warnTitle;
        private Text _warnSub;
        private Button _warnBtn;

        // 告警队列
        private readonly List<AlertItem> _cautAlerts = new List<AlertItem>(8);
        private readonly List<AlertItem> _warnAlerts = new List<AlertItem>(8);

        // 轮播计时器
        private float _rotateTimer = 0f;
        private float _switchInterval = 1.8f;
        private int _cautIndex = 0;
        private int _warnIndex = 0;

        // 消警状态 (Acknowledge)
        private bool _cautAcknowledged = false;
        private bool _warnAcknowledged = false;
        private int _lastCautCount = -1;
        private int _lastWarnCount = -1;

        // 同步时钟与闪烁
        private static float _clock = 0f;
        private static bool _blink1Hz = true;
        private static bool _blink2Hz = true;

        // 脏检查保护 (减少 GC 与 Canvas 重绘)
        private string _lastCautTitleStr = string.Empty;
        private string _lastCautSubStr = string.Empty;
        private string _lastWarnTitleStr = string.Empty;
        private string _lastWarnSubStr = string.Empty;
        private string _lastBannerTitleStr = string.Empty;
        private string _lastBannerSubStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            // 1. 解析自定义模板配置 (包括模块数量、自定义文本与门限)
            ParseCustomTemplate(config?.CustomTemplate);
            if (!_customSepExplicit) _sepTitleTemplate = I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离");
            if (!_customEngExplicit) _engTitleTemplate = I18n.Tr("WIDGET_ALERT_ENGINE_START", "引擎启动");
            _currentEvent = BuildEventItem(BannerEventType.Separation);

            // 2. 航空外框底盘 (Outer Bezel)
            Vector2 initialSize = (_modulesCount == 3) ? new Vector2(184f * s, 42f * s) : new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = initialSize;

            _outerBezel = gameObject.AddComponent<Image>();
            _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            _outerOutline = gameObject.AddComponent<Outline>();
            _outerOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

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
            BuildWarningCell(s, cellSize, theme);

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
            this.Controls.Register(new WidgetAnnunciatorControl(_cautTitle, _cautSub, _cautBg, _cautOutline, "CAUTION Annunciator", "注意告警指示光字牌"));
            this.Controls.Register(new WidgetAnnunciatorControl(_warnTitle, _warnSub, _warnBg, _warnOutline, "WARNING Annunciator", "危急告警指示光字牌"));
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
                this.Controls.Register(WidgetControlManager.WrapElement(this, "horiz_divider", "Horizontal Divider", _horizDivider.gameObject, "水平机械隔离横梁", t =>
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
            _cautTitle = UIFactory.CreateText(_cautCell.transform, "Title", "CAUTION", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
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
            _warnTitle = UIFactory.CreateText(_warnCell.transform, "Title", "WARNING", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.DangerColor);
            _warnTitle.fontStyle = FontStyle.Bold;
            _warnTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _warnTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注
            _warnSub = UIFactory.CreateText(_warnCell.transform, "Sub", "ARMED", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
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
            SetModulesCount(_modulesCount == 3 ? 2 : 3);
        }

        public void SetModulesCount(int count)
        {
            int clamped = Mathf.Clamp(count, 2, 3);
            if (_modulesCount == clamped) return;

            _modulesCount = clamped;

            // 回写配置模板，实现无感热插拔与持久化保存
            if (Config != null)
            {
                string t = Config.CustomTemplate ?? string.Empty;
                if (t.IndexOf("MODULES=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"MODULES=\d+;?", $"MODULES={_modulesCount};", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                else
                {
                    t = $"MODULES={_modulesCount};" + t;
                }
                Config.CustomTemplate = t;
            }

            ApplyLayoutMode();
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            ApplyTheme(theme);
        }

        public void ApplyLayoutMode()
        {
            float s = CurrentDpiScale;

            if (_modulesCount == 3)
            {
                // 3模块 金字塔形态 (Pyramid Morphology)
                // 顶部 Row 1: [提醒][警告] (y = +10px)
                // 中部水平隔离筋条 (y = 0px)
                // 底部 Row 2: [状态提醒] (y = -10px)
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
                // 2模块 经典并列与聚拢闪回形态 (Classic 2-Cell Matrix)
                Vector2 widgetSize = new Vector2(184f * s, 22f * s);
                RectTransform.sizeDelta = widgetSize;
                if (_outerBezel != null) _outerBezel.rectTransform.sizeDelta = widgetSize;

                if (_horizDivider != null)
                {
                    _horizDivider.gameObject.SetActive(false);
                }

                if (_bannerState == BannerDisplayState.Normal)
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

        private void ParseCustomTemplate(string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            string[] pairs = template.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string[] kv = pairs[i].Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                if (k == "MODULES" || k == "MODE" || k == "COUNT")
                {
                    if (int.TryParse(v, out int mc))
                    {
                        _modulesCount = Mathf.Clamp(mc, 2, 3);
                    }
                }
                else if (k == "PYRAMID")
                {
                    if (v == "1" || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) _modulesCount = 3;
                    else if (v == "0" || v.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) _modulesCount = 2;
                }
                else if (k == "INTERVAL" || k == "ROTATION")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float iv) && iv > 0.5f)
                    {
                        _switchInterval = iv;
                    }
                }
                else if (k == "SEP" || k == "SEP_TEXT" || k == "SEPARATION")
                {
                    if (!string.IsNullOrEmpty(v)) { _sepTitleTemplate = v; _customSepExplicit = true; }
                }
                else if (k == "ENG" || k == "ENG_TEXT" || k == "IGNITION")
                {
                    if (!string.IsNullOrEmpty(v)) { _engTitleTemplate = v; _customEngExplicit = true; }
                }
                else if (k == "TIME" || k == "BANNER_TIME" || k == "DURATION")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dt) && dt > 0.4f)
                    {
                        _bannerDuration = dt;
                    }
                }
                else if (k == "FUEL_WARN" || k == "MIN_FUEL")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fw) && fw > 0.01f && fw <= 35f)
                    {
                        _customWarnThresh = fw / 100f;
                    }
                }
                else if (k == "FUEL_CAUT" || k == "LOW_FUEL")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fc) && fc > 0.01f && fc <= 50f)
                    {
                        _customCautThresh = fc / 100f;
                    }
                }
            }
        }

        private BannerEventItem BuildEventItem(BannerEventType type)
        {
            switch (type)
            {
                case BannerEventType.Separation:
                    return new BannerEventItem(
                        BannerEventType.Separation,
                        !string.IsNullOrEmpty(_sepTitleTemplate) ? _sepTitleTemplate : I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离"),
                        "STG",
                        "◀",
                        "▶",
                        _bannerDuration,
                        30,
                        EventColorRole.AccentPrimary
                    );

                case BannerEventType.EngineStart:
                    return new BannerEventItem(
                        BannerEventType.EngineStart,
                        !string.IsNullOrEmpty(_engTitleTemplate) ? _engTitleTemplate : I18n.Tr("WIDGET_ALERT_ENGINE_START", "引擎启动"),
                        "IGN",
                        "▲",
                        "▲",
                        _bannerDuration,
                        25,
                        EventColorRole.WarningColor
                    );

                case BannerEventType.MECO:
                    return new BannerEventItem(
                        BannerEventType.MECO,
                        I18n.Tr("WIDGET_ALERT_MECO", "主发关机"),
                        "MECO",
                        "■",
                        "■",
                        _bannerDuration,
                        20,
                        EventColorRole.WarningColor
                    );

                case BannerEventType.ManeuverApproach:
                    return new BannerEventItem(
                        BannerEventType.ManeuverApproach,
                        I18n.Tr("WIDGET_ALERT_MANEUVER_APPROACH", "接近机动节点"),
                        "T-60s",
                        "◆",
                        "◆",
                        Mathf.Max(1.6f, _bannerDuration),
                        22,
                        EventColorRole.AccentPrimary
                    );

                case BannerEventType.ManeuverBurn:
                    return new BannerEventItem(
                        BannerEventType.ManeuverBurn,
                        I18n.Tr("WIDGET_ALERT_MANEUVER_BURN", "机动点火执行"),
                        "BURN",
                        "▶",
                        "▶",
                        Mathf.Max(1.5f, _bannerDuration),
                        24,
                        EventColorRole.AccentPrimary
                    );

                case BannerEventType.OrbitAchieved:
                    return new BannerEventItem(
                        BannerEventType.OrbitAchieved,
                        I18n.Tr("WIDGET_ALERT_ORBIT_ACHIEVED", "入轨圆化完成"),
                        "ORBIT",
                        "★",
                        "★",
                        Mathf.Max(1.8f, _bannerDuration),
                        28,
                        EventColorRole.Success
                    );

                case BannerEventType.AtmosphereEntry:
                    return new BannerEventItem(
                        BannerEventType.AtmosphereEntry,
                        I18n.Tr("WIDGET_ALERT_ATMOSPHERE_ENTRY", "进入大气层"),
                        "ENTRY",
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        26,
                        EventColorRole.WarningColor
                    );

                case BannerEventType.ApoapsisPass:
                    return new BannerEventItem(
                        BannerEventType.ApoapsisPass,
                        I18n.Tr("WIDGET_ALERT_AP_PASS", "通过远拱点"),
                        "AP",
                        "▲",
                        "▲",
                        Mathf.Max(1.2f, _bannerDuration),
                        14,
                        EventColorRole.AccentSecondary
                    );

                case BannerEventType.PeriapsisPass:
                    return new BannerEventItem(
                        BannerEventType.PeriapsisPass,
                        I18n.Tr("WIDGET_ALERT_PE_PASS", "通过近拱点"),
                        "PE",
                        "▼",
                        "▼",
                        Mathf.Max(1.2f, _bannerDuration),
                        14,
                        EventColorRole.AccentSecondary
                    );

                case BannerEventType.DockingMode:
                    return new BannerEventItem(
                        BannerEventType.DockingMode,
                        I18n.Tr("WIDGET_ALERT_DOCKING_MODE", "进入对接模式"),
                        "DOCK",
                        "⊞",
                        "⊞",
                        Mathf.Max(1.5f, _bannerDuration),
                        16,
                        EventColorRole.AccentPrimary
                    );

                case BannerEventType.Deorbit:
                    return new BannerEventItem(
                        BannerEventType.Deorbit,
                        I18n.Tr("WIDGET_ALERT_DEORBIT", "飞船离轨"),
                        "DEORB",
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        27,
                        EventColorRole.WarningColor
                    );

                case BannerEventType.Escape:
                    return new BannerEventItem(
                        BannerEventType.Escape,
                        I18n.Tr("WIDGET_ALERT_ESCAPE", "逃逸轨道建立"),
                        "ESC",
                        "▲",
                        "▲",
                        Mathf.Max(1.8f, _bannerDuration),
                        28,
                        EventColorRole.Success
                    );

                case BannerEventType.SoiTransition:
                    return new BannerEventItem(
                        BannerEventType.SoiTransition,
                        I18n.Tr("WIDGET_ALERT_SOI_TRANSITION", "进入引力范围"),
                        "SOI",
                        "◆",
                        "◆",
                        Mathf.Max(1.8f, _bannerDuration),
                        29,
                        EventColorRole.AccentPrimary
                    );

                case BannerEventType.SuicideBurn:
                    return new BannerEventItem(
                        BannerEventType.SuicideBurn,
                        I18n.Tr("WIDGET_ALERT_SUICIDE_BURN", "动力减速着陆"),
                        "BURN",
                        "▼",
                        "▼",
                        Mathf.Max(1.8f, _bannerDuration),
                        29,
                        EventColorRole.WarningColor
                    );

                case BannerEventType.Blackout:
                    return new BannerEventItem(
                        BannerEventType.Blackout,
                        I18n.Tr("WIDGET_ALERT_BLACKOUT", "再入黑障"),
                        "BLKOUT",
                        "⚡",
                        "⚡",
                        Mathf.Max(2.0f, _bannerDuration),
                        26,
                        EventColorRole.DangerColor
                    );

                case BannerEventType.Touchdown:
                    return new BannerEventItem(
                        BannerEventType.Touchdown,
                        I18n.Tr("WIDGET_ALERT_TOUCHDOWN", "着陆接地成功"),
                        "TOUCH",
                        "⚓",
                        "⚓",
                        Mathf.Max(2.0f, _bannerDuration),
                        30,
                        EventColorRole.Success
                    );

                default:
                    return BuildEventItem(BannerEventType.Separation);
            }
        }

        private Color ResolveEventColor(EventColorRole role, ThemeConfig theme)
        {
            switch (role)
            {
                case EventColorRole.AccentPrimary:
                    return theme.AccentPrimary;
                case EventColorRole.AccentSecondary:
                    return theme.AccentSecondary;
                case EventColorRole.WarningColor:
                    return theme.WarningColor;
                case EventColorRole.DangerColor:
                    return theme.DangerColor;
                case EventColorRole.Success:
                    return (Color)theme.AccentPositive;
                default:
                    return theme.AccentPrimary;
            }
        }

        private void EnqueueByPriority(BannerEventItem item)
        {
            if (_bannerQueue.Count >= 8) return;

            int insertIdx = _bannerQueue.Count;
            for (int i = 0; i < _bannerQueue.Count; i++)
            {
                if (item.Priority > _bannerQueue[i].Priority)
                {
                    insertIdx = i;
                    break;
                }
            }
            _bannerQueue.Insert(insertIdx, item);
        }

        public void TriggerBanner(BannerEventType eventType, bool immediateHolding = false)
        {
            float now = Time.unscaledTime;

            // 1. 防抖与去重审查：若处于冷却期且非强制立即渲染，则忽略重复脉冲
            if (!immediateHolding)
            {
                if (_eventLastTriggerTimes.TryGetValue(eventType, out float lastTime) && (now - lastTime) < EVENT_COOLDOWN)
                {
                    return;
                }

                // 若当前正在展示同类事件，或队列中已有同类事件，避免重复堆叠
                if (_bannerState != BannerDisplayState.Normal && _bannerState != BannerDisplayState.FlashingBack)
                {
                    if (_currentEvent.EventType == eventType) return;
                    for (int i = 0; i < _bannerQueue.Count; i++)
                    {
                        if (_bannerQueue[i].EventType == eventType) return;
                    }
                }
            }

            _eventLastTriggerTimes[eventType] = now;
            BannerEventItem newItem = BuildEventItem(eventType);

            // 2. 强制即时渲染模式 (供无头渲染器 HeadlessUIRenderer 捕获静态切片)
            if (immediateHolding)
            {
                _bannerQueue.Clear();
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergedHolding;
                _bannerTimer = 0.12f;

                if (_modulesCount == 2)
                {
                    if (_centerDivider != null) _centerDivider.gameObject.SetActive(false);
                    if (_cautCell != null) _cautCell.SetActive(false);
                    if (_warnCell != null) _warnCell.SetActive(false);
                    if (_bannerCell != null) _bannerCell.SetActive(true);
                }
                else
                {
                    if (_cautCell != null) _cautCell.SetActive(true);
                    if (_warnCell != null) _warnCell.SetActive(true);
                    if (_bannerCell != null) _bannerCell.SetActive(true);
                }
                return;
            }

            // 3. 状态机分流与仲裁
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
                _bannerTimer = 0.12f;
                if (_bannerCell != null) _bannerCell.SetActive(true);
                if (_modulesCount == 2)
                {
                    if (_cautCell != null) _cautCell.SetActive(false);
                    if (_warnCell != null) _warnCell.SetActive(false);
                }
            }
            else
            {
                // 当前正有其他事件展示中：
                // 若仍在向中心聚拢/滑入阶段且新到事件优先级更高，置换首位
                if (_bannerState == BannerDisplayState.MergingIn && newItem.Priority > _currentEvent.Priority)
                {
                    BannerEventItem lower = _currentEvent;
                    _currentEvent = newItem;
                    _bannerQueue.Insert(0, lower);
                }
                else
                {
                    EnqueueByPriority(newItem);
                }
            }
        }

        private void OnAcknowledgeCaution()
        {
            _cautAcknowledged = true;
        }

        private void OnAcknowledgeWarning()
        {
            _warnAcknowledged = true;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float dt = Time.unscaledDeltaTime;

            // 1. 侦测分级分离、引擎点火与机动巡航等全景瞬态事件
            DetectTransientEvents(telemetry);

            // 2. 持续评估当前所有活跃警报 (驱动双室光字牌)
            EvaluateTelemetryAlerts(telemetry, dt);

            // 3. 更新座舱全局同步时钟
            _clock += dt;
            _blink1Hz = ((int)(_clock * 2f) % 2) == 0;
            _blink2Hz = ((int)(_clock * 4f) % 2) == 0;

            // 4. 告警数量变动与防抖消警保护
            UpdateAlertIndicesAndAcknowledge();

            // 5. 定时交替轮播推进
            _rotateTimer += dt;
            if (_rotateTimer >= _switchInterval)
            {
                _rotateTimer = 0f;
                if (_cautAlerts.Count > 1) _cautIndex = (_cautIndex + 1) % _cautAlerts.Count;
                if (_warnAlerts.Count > 1) _warnIndex = (_warnIndex + 1) % _warnAlerts.Count;
            }

            // 6. 依据 2 模块或 3 模块架构分流驱动
            if (_modulesCount == 3)
            {
                // 3模块 金字塔形态：
                // 上层甲板：Caution 与 Warning 光字牌永不遮挡，全天候独立工作
                RenderVisualCells();

                // 下层底座：若有瞬态事件由状态机驱动展示，无事件时展示巡航工况
                if (_bannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(dt, theme);
                }
                else
                {
                    RenderNominalFlightPhase(telemetry, theme);
                }
            }
            else
            {
                // 2模块 经典形态：
                // 若处于横幅合并、展示、切换或闪回动画阶段，转由横幅状态机独占驱动
                if (_bannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(dt, theme);
                    return;
                }

                RenderVisualCells();
            }
        }

        private void UpdateAlertIndicesAndAcknowledge()
        {
            if (_cautAlerts.Count != _lastCautCount)
            {
                if (_cautAlerts.Count > _lastCautCount) _cautAcknowledged = false;
                _lastCautCount = _cautAlerts.Count;
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
            }
            if (_warnAlerts.Count != _lastWarnCount)
            {
                if (_warnAlerts.Count > _lastWarnCount) _warnAcknowledged = false;
                _lastWarnCount = _warnAlerts.Count;
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
            }
        }

        private double GetEffectivePeriapsis(IFlightTelemetry telem)
        {
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double pPe = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisPeriapsis");
                if (!double.IsNaN(pPe) && pPe > -9000000.0) return pPe;

                double pPe2 = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "PERIAPSIS");
                if (!double.IsNaN(pPe2) && pPe2 > -9000000.0) return pPe2;
            }
            return telem.Periapsis;
        }

        private double GetEffectiveApoapsis(IFlightTelemetry telem)
        {
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double pAp = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "AnalysisApoapsis");
                if (!double.IsNaN(pAp) && pAp > -9000000.0) return pAp;

                double pAp2 = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "APOAPSIS");
                if (!double.IsNaN(pAp2) && pAp2 > -9000000.0) return pAp2;
            }
            return telem.Apoapsis;
        }

        private double GetAtmosphereCutoff(IFlightTelemetry telem)
        {
            if (telem == null) return 70000.0;

            // 1. 优先从外部探针注册中心检索物理大气边界 (支持 FAR / Principia / 环境物理模组注册)
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double probeDepth = ExternalProbeRegistry.ResolveNumeric("ENV", "AtmosphereDepth");
                if (!double.IsNaN(probeDepth) && probeDepth >= 0.0) return probeDepth;
            }

            // 2. 契约通用化获取当前天体物理真实大气层高度 (0 硬编码，完美适配原版、RSS/RO、Kopernicus、Principia 及任何自定义星球)
            if (telem.AtmosphereDepth > 0.0)
            {
                return telem.AtmosphereDepth;
            }

            // 3. 若当前天体为无大气真空天体 (如月球、水星、各类无气小行星)
            if (!telem.HasAtmosphere)
            {
                return 0.0;
            }

            // 4. 通用物理防御兜底：若存在宏观气压读数则按标准大气厚度兜底
            if (telem.AtmosphericPressure > 0.0001)
            {
                return 70000.0;
            }

            return 0.0;
        }

        private static string FormatKm(double meters)
        {
            if (double.IsNaN(meters)) return "--";
            double km = meters / 1000.0;
            if (Math.Abs(km) >= 1000.0) return $"{km / 1000.0:F1}M";
            return $"{km:F0}k";
        }

        private void DetectTransientEvents(IFlightTelemetry telem)
        {
            // 解析高精度轨道动力学参数 (优先采用 Principia N 体数值摄动分析探针数据，自动 Fallback 至原版通用遥测)
            double atmoCutoff = GetAtmosphereCutoff(telem);
            double effectivePe = GetEffectivePeriapsis(telem);
            double effectiveAp = GetEffectiveApoapsis(telem);

            // ── A. 分级分离判定 ──
            if (_lastStage != -1)
            {
                bool sepSignal = telem.IsStageSeparating && !_lastIsStageSeparating;
                bool stageDropped = telem.CurrentStage < _lastStage;
                if (sepSignal || stageDropped)
                {
                    TriggerBanner(BannerEventType.Separation);
                }
            }

            // ── B. 引擎点火启动判定 ──
            if (_lastActiveEngines != -1)
            {
                bool ignSignal = telem.IsEngineIgniting && !_lastIsEngineIgniting;
                bool engStarted = (_lastActiveEngines == 0 && telem.ActiveEngines > 0 && telem.Throttle > 0.02f) ||
                                  (_lastThrottle <= 0.001f && telem.Throttle > 0.05f && telem.ActiveEngines > 0);
                if (ignSignal || engStarted)
                {
                    TriggerBanner(BannerEventType.EngineStart);
                }

                // ── C. 主发关机 MECO 判定 ──
                bool mecoCutoff = (_lastActiveEngines > 0 && telem.ActiveEngines == 0 &&
                                   (telem.FlightSituation == "FLYING" || telem.FlightSituation == "SUB_ORBITAL" || telem.FlightSituation == "ORBITING"));
                bool throttleCut = (_lastThrottle > 0.25f && telem.Throttle <= 0.001f && telem.ActiveEngines > 0 &&
                                    telem.VerticalSpeed > 10.0 && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH");
                if (mecoCutoff || throttleCut)
                {
                    TriggerBanner(BannerEventType.MECO);
                }
            }

            // ── D. 接近机动节点 (T-60s) 判定 ──
            if (telem.HasManeuverNode && telem.ManeuverTimeToNode > 0.0 && telem.ManeuverTimeToNode <= 60.0)
            {
                if (_lastTimeToNode > 60.0 || _lastTimeToNode < 0.0)
                {
                    TriggerBanner(BannerEventType.ManeuverApproach);
                }
            }

            // ── E. 机动点火执行 BURN 判定 ──
            if (telem.HasManeuverNode && telem.ManeuverTimeToNode <= 2.0 && telem.Throttle > 0.05f)
            {
                if (!_lastManeuverBurnTriggered)
                {
                    _lastManeuverBurnTriggered = true;
                    TriggerBanner(BannerEventType.ManeuverBurn);
                }
            }
            else if (!telem.HasManeuverNode || telem.Throttle <= 0.01f)
            {
                _lastManeuverBurnTriggered = false;
            }

            // ── F. 入轨圆化完成 ORBIT STABLE 判定 ──
            if (_lastEffectivePe > -999999.0 && _lastEffectivePe < atmoCutoff && effectivePe >= atmoCutoff && effectiveAp >= atmoCutoff &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                TriggerBanner(BannerEventType.OrbitAchieved);
            }

            // ── G. 飞船离轨制动 DEORBIT 判定 ──
            // 飞船在闭合轨道 (原先 Pe >= atmoCutoff && Ap >= atmoCutoff)，点火或机动使近拱点降至大气层内 (Pe < atmoCutoff)
            if (_lastEffectivePe >= atmoCutoff && _lastEffectiveAp >= atmoCutoff && effectivePe < atmoCutoff && effectivePe > -9000000.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                TriggerBanner(BannerEventType.Deorbit);
            }

            // ── H. 逃逸轨道建立 ESCAPE 判定 (双曲线脱离) ──
            bool isEscapingNow = (telem.FlightSituation == "ESCAPING") || (effectiveAp < 0 && effectiveAp > -9000000.0);
            bool wasEscapingBefore = (_lastFlightSituation == "ESCAPING") || (_lastEffectiveAp < 0 && _lastEffectiveAp > -9000000.0);
            if (!wasEscapingBefore && isEscapingNow && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                TriggerBanner(BannerEventType.Escape);
            }

            // ── I. 穿越天体引力范围 (SOI Transition) 判定 ──
            if (!string.IsNullOrEmpty(_lastCelestialBody) && !string.IsNullOrEmpty(telem.CelestialBodyName) &&
                !_lastCelestialBody.Equals(telem.CelestialBodyName, StringComparison.OrdinalIgnoreCase))
            {
                TriggerBanner(BannerEventType.SoiTransition);
            }

            // ── J. 再入/进入大气层 ATMOSPHERE ENTRY 判定 ──
            if (atmoCutoff > 0.0 && _lastAltitude >= atmoCutoff && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed < -5.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                TriggerBanner(BannerEventType.AtmosphereEntry);
            }

            // ── K. 再入等离子体黑障 (REENTRY BLACKOUT) 判定 ──
            if (atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.AltitudeASL > atmoCutoff * 0.35 && telem.Mach > 8.0 && telem.DynamicPressure > 12.0)
            {
                TriggerBanner(BannerEventType.Blackout);
            }

            // ── L. 动力减速着陆点火 (SUICIDE / LANDING BURN) 判定 ──
            if (telem.AltitudeAGL < 2000.0 && telem.AltitudeAGL > 15.0 && telem.VerticalSpeed < -15.0 && telem.Throttle > 0.40f && telem.ActiveEngines > 0)
            {
                TriggerBanner(BannerEventType.SuicideBurn);
            }

            // ── M. 拱点穿越判定 (远拱点 Ap / 近拱点 Pe) ──
            if (effectivePe >= atmoCutoff)
            {
                if (_lastTimeToAp > 1.0 && telem.TimeToAp <= 1.0 && telem.TimeToAp >= 0.0)
                {
                    TriggerBanner(BannerEventType.ApoapsisPass);
                }
                if (_lastTimeToPe > 1.0 && telem.TimeToPe <= 1.0 && telem.TimeToPe >= 0.0)
                {
                    TriggerBanner(BannerEventType.PeriapsisPass);
                }
            }

            // ── N. 对接模式进入判定 ──
            if (telem.IsDockingMode && !_lastIsDockingMode)
            {
                TriggerBanner(BannerEventType.DockingMode);
            }

            // ── O. 着陆接地确认 TOUCHDOWN 判定 ──
            bool isLandedNow = (telem.FlightSituation == "LANDED" || telem.FlightSituation == "SPLASHED" || telem.IsTouchdownAlert);
            if (!_lastIsLanded && isLandedNow && _lastAltitudeAGL > 2.0)
            {
                TriggerBanner(BannerEventType.Touchdown);
            }

            // 更新历史遥测缓存
            _lastStage = telem.CurrentStage;
            _lastActiveEngines = telem.ActiveEngines;
            _lastThrottle = telem.Throttle;
            _lastIsStageSeparating = telem.IsStageSeparating;
            _lastIsEngineIgniting = telem.IsEngineIgniting;
            _lastTimeToNode = telem.HasManeuverNode ? telem.ManeuverTimeToNode : -1.0;
            _lastPeriapsis = telem.Periapsis;
            _lastAltitude = telem.AltitudeASL;
            _lastTimeToAp = telem.TimeToAp;
            _lastTimeToPe = telem.TimeToPe;
            _lastIsDockingMode = telem.IsDockingMode;
            _lastIsLanded = isLandedNow;
            _lastAltitudeAGL = telem.AltitudeAGL;
            _lastEffectivePe = effectivePe;
            _lastEffectiveAp = effectiveAp;
            _lastCelestialBody = telem.CelestialBodyName;
            _lastFlightSituation = telem.FlightSituation;
            _lastAltitudeAGL = telem.AltitudeAGL;
        }

        private void UpdateBannerAnimation(float dt, ThemeConfig theme)
        {
            if (theme == null) theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float s = CurrentDpiScale;
            Color eventColor = ResolveEventColor(_currentEvent.ColorRole, theme);

            if (_bannerState == BannerDisplayState.MergingIn)
            {
                _bannerTimer += dt;
                float progress = Mathf.Clamp01(_bannerTimer / 0.10f);

                if (_modulesCount == 2)
                {
                    // 2模块形态：双方框平滑向中央滑动聚拢
                    if (_centerDivider != null) _centerDivider.gameObject.SetActive(false);
                    if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(Mathf.Lerp(-46f * s, 0f, progress), 0f);
                    if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(Mathf.Lerp(46f * s, 0f, progress), 0f);

                    if (progress >= 1.0f)
                    {
                        if (_cautCell != null) _cautCell.SetActive(false);
                        if (_warnCell != null) _warnCell.SetActive(false);
                        if (_bannerCell != null) _bannerCell.SetActive(true);
                        _bannerState = BannerDisplayState.MergedHolding;
                        _bannerTimer = 0f;
                    }
                }
                else
                {
                    // 3模块形态：底座状态窗光脉冲扫入
                    if (progress >= 1.0f)
                    {
                        _bannerState = BannerDisplayState.MergedHolding;
                        _bannerTimer = 0f;
                    }
                }
            }
            else if (_bannerState == BannerDisplayState.MergedHolding)
            {
                _bannerTimer += dt;

                SetTextIfChanged(_bannerTitle, _currentEvent.Title);
                SetTextIfChanged(_bannerSub, _currentEvent.Sub);
                SetTextIfChanged(_bannerLeftIcon, _currentEvent.LeftIcon);
                SetTextIfChanged(_bannerRightIcon, _currentEvent.RightIcon);

                // 航电高光微脉冲 (呼吸感)
                float pulse = 0.82f + 0.18f * Mathf.Sin(_bannerTimer * 12f);
                Color activeCol = WidgetStyleManager.WithAlpha(eventColor, pulse);

                if (_bannerBg != null) _bannerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                if (_bannerOutline != null) _bannerOutline.effectColor = activeCol;
                if (_bannerPipBar != null) _bannerPipBar.color = activeCol;
                if (_bannerTitle != null) _bannerTitle.color = eventColor;
                if (_bannerSub != null) _bannerSub.color = WidgetStyleManager.WithAlpha(eventColor, 0.75f);
                if (_bannerLeftIcon != null) _bannerLeftIcon.color = activeCol;
                if (_bannerRightIcon != null) _bannerRightIcon.color = activeCol;

                // 若有排队连击事件，单事件展示时长适度收紧 (0.95s)，保持紧凑利落的航电节奏感
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

                SetTextIfChanged(_bannerTitle, _currentEvent.Title);
                SetTextIfChanged(_bannerSub, _currentEvent.Sub);
                SetTextIfChanged(_bannerLeftIcon, _currentEvent.LeftIcon);
                SetTextIfChanged(_bannerRightIcon, _currentEvent.RightIcon);

                Color switchColor = ResolveEventColor(_currentEvent.ColorRole, theme);
                if (_bannerOutline != null) _bannerOutline.effectColor = switchColor;
                if (_bannerPipBar != null) _bannerPipBar.color = switchColor;
                if (_bannerTitle != null) _bannerTitle.color = switchColor;
                if (_bannerSub != null) _bannerSub.color = switchColor;

                if (_bannerTimer >= 0.08f)
                {
                    _bannerState = BannerDisplayState.MergedHolding;
                    _bannerTimer = 0f;
                }
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                _bannerTimer += dt;

                if (_modulesCount == 2)
                {
                    // 24Hz 迅速高频频闪
                    bool strobeOn = ((int)(_bannerTimer * 24f) % 2) == 0;
                    if (_bannerCell != null) _bannerCell.SetActive(strobeOn);

                    if (_bannerTimer >= 0.14f)
                    {
                        if (_bannerCell != null) _bannerCell.SetActive(false);
                        if (_cautCell != null) _cautCell.SetActive(true);
                        if (_warnCell != null) _warnCell.SetActive(true);
                        if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(-46f * s, 0f);
                        if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(46f * s, 0f);
                        if (_centerDivider != null) _centerDivider.gameObject.SetActive(true);

                        _bannerState = BannerDisplayState.Normal;
                        _bannerTimer = 0f;
                        RenderVisualCells();
                    }
                }
                else
                {
                    // 3模块形态：平滑淡出并转入常态巡航工况
                    if (_bannerTimer >= 0.12f)
                    {
                        _bannerState = BannerDisplayState.Normal;
                        _bannerTimer = 0f;
                    }
                }
            }
        }

        private void RenderNominalFlightPhase(IFlightTelemetry telem, ThemeConfig theme)
        {
            if (_bannerCell == null || !_bannerCell.activeSelf) return;

            double atmoCutoff = GetAtmosphereCutoff(telem);
            double effectivePe = GetEffectivePeriapsis(telem);
            double effectiveAp = GetEffectiveApoapsis(telem);

            string title;
            string sub;
            string icon;
            Color phaseColor;

            if (telem.HasManeuverNode)
            {
                title = I18n.Tr("WIDGET_STATUS_NODE_ARMED", "节点待命");
                sub = $"Δv {telem.ManeuverDeltaV:F0}";
                icon = "◆";
                phaseColor = theme.AccentPrimary;
            }
            else if (telem.FlightSituation == "LANDED" || telem.FlightSituation == "PRELAUNCH" || telem.FlightSituation == "SPLASHED")
            {
                title = I18n.Tr("WIDGET_STATUS_READY", "发射就绪");
                sub = "READY";
                icon = "●";
                phaseColor = theme.AccentPositive;
            }
            else if (telem.FlightSituation == "ESCAPING" || (effectiveAp < 0 && effectiveAp > -9000000.0))
            {
                title = I18n.Tr("WIDGET_STATUS_ESCAPE", "深空逃逸");
                sub = $"Pe {FormatKm(effectivePe)}";
                icon = "▲";
                phaseColor = theme.AccentPositive;
            }
            else if (effectivePe < atmoCutoff && telem.AltitudeASL >= atmoCutoff && effectivePe > -9000000.0)
            {
                // 航天器处于太空高度，但近拱点已降至大气层内或地表之下 (执行了离轨制动或处于再入走廊)
                if (effectivePe < 0)
                {
                    title = I18n.Tr("WIDGET_STATUS_BALLISTIC", "弹道再入撞击");
                    sub = $"Pe {FormatKm(effectivePe)}";
                    icon = "▼";
                    phaseColor = theme.WarningColor;
                }
                else
                {
                    title = I18n.Tr("WIDGET_STATUS_DEORBIT", "离轨再入走廊");
                    sub = $"Pe {FormatKm(effectivePe)}";
                    icon = "▼";
                    phaseColor = theme.WarningColor;
                }
            }
            else if (telem.FlightSituation == "ORBITING" || (effectivePe >= atmoCutoff && telem.AltitudeASL >= atmoCutoff))
            {
                title = I18n.Tr("WIDGET_STATUS_ORBIT_CRUISE", "轨道巡航");
                sub = $"Ap {FormatKm(effectiveAp)}";
                icon = "●";
                phaseColor = theme.AccentSecondary;
            }
            else if (atmoCutoff > 0.0 && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed > 10.0)
            {
                title = I18n.Tr("WIDGET_STATUS_ASCENT", "大气爬升");
                sub = $"M {telem.Mach:F1}";
                icon = "▲";
                phaseColor = theme.WarningColor;
            }
            else if (telem.VerticalSpeed < -10.0 && (atmoCutoff > 0.0 ? telem.AltitudeASL < atmoCutoff * 0.5 : telem.AltitudeAGL < 3000.0))
            {
                title = I18n.Tr("WIDGET_STATUS_APPROACH", "降落进近");
                sub = $"VSI {Mathf.RoundToInt((float)telem.VerticalSpeed)}";
                icon = "▼";
                phaseColor = theme.WarningColor;
            }
            else
            {
                title = I18n.Tr("WIDGET_STATUS_SUBORBITAL", "亚轨道飞行");
                sub = "SUB-ORB";
                icon = "◈";
                phaseColor = theme.AccentPrimary;
            }

            SetTextIfChanged(_bannerTitle, title);
            SetTextIfChanged(_bannerSub, sub);
            SetTextIfChanged(_bannerLeftIcon, icon);
            SetTextIfChanged(_bannerRightIcon, icon);

            // 暗舱待命微光 (Dead-front subdued glow, 0 颜色字面量)
            Color deadFrontColor = WidgetStyleManager.WithAlpha(phaseColor, 0.45f);
            Color deadFrontSub = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
            Color deadFrontGhost = WidgetStyleManager.Weighted(phaseColor, LineWeight.Ghost);

            if (_bannerBg != null) _bannerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_bannerOutline != null) _bannerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bannerPipBar != null) _bannerPipBar.color = Color.clear;
            if (_bannerTitle != null) _bannerTitle.color = deadFrontColor;
            if (_bannerSub != null) _bannerSub.color = deadFrontSub;
            if (_bannerLeftIcon != null) _bannerLeftIcon.color = deadFrontGhost;
            if (_bannerRightIcon != null) _bannerRightIcon.color = deadFrontGhost;
        }

        private void EvaluateTelemetryAlerts(IFlightTelemetry telem, float dt)
        {
            _cautAlerts.Clear();
            _warnAlerts.Clear();

            // ── 1. 推进剂与沉底 (FUEL / ULLAGE) ──
            float prop = telem.StagePropellantFraction;
            bool engineArmed = telem.ActiveEngines > 0 || (telem.TotalStageEngines > 0 && telem.Throttle > 0.001f);

            // 航电标准低油量安全门限 (Strict Safety Bounds: Warning <= 5%, Caution <= 15%)
            float warnThresh = 0.05f;
            float cautThresh = 0.15f;
            if (_customWarnThresh > 0.001f) warnThresh = _customWarnThresh;
            else if (Config != null && Config.WarningThreshold > 0.01 && Config.WarningThreshold <= 25.0)
                warnThresh = (float)Config.WarningThreshold / 100f;

            if (_customCautThresh > 0.001f) cautThresh = _customCautThresh;
            else if (Config != null && Config.CautionThreshold > 0.01 && Config.CautionThreshold <= 40.0)
                cautThresh = (float)Config.CautionThreshold / 100f;

            // 绝对安全熔断器：若油量在 40% 以上，物理上绝对属于正常或充足，立即清空低油量防抖计数器，杜绝误报
            if (prop >= 0.40f || !engineArmed)
            {
                _lowFuelPersistentTimer = 0f;
            }
            else if (prop >= 0f && prop <= cautThresh)
            {
                _lowFuelPersistentTimer += dt;
                if (_lowFuelPersistentTimer >= LOW_FUEL_PERSISTENCE)
                {
                    int propPct = Mathf.RoundToInt(prop * 100f);
                    if (prop <= warnThresh) _warnAlerts.Add(new AlertItem("MIN FUEL!", $"{propPct}%", true));
                    else _cautAlerts.Add(new AlertItem("LOW FUEL", $"{propPct}%", false));
                }
            }
            else
            {
                _lowFuelPersistentTimer = 0f;
            }

            // RealFuels 探针沉底状态
            if (ExternalProbeRegistry.StringResolver != null)
            {
                string rfUllage = ExternalProbeRegistry.ResolveString("RF", "ULLAGE", "");
                if (!string.IsNullOrEmpty(rfUllage) &&
                    (rfUllage.IndexOf("Unstable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rfUllage.IndexOf("Very", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _cautAlerts.Add(new AlertItem("ULLAGE", "UNSTB", false));
                }
            }

            // ── 2. 电气能量平衡 (ELECTRIC CHARGE / DBS) ──
            double ecPct = telem.EcPercent;
            if (ecPct >= 0.0)
            {
                int ecInt = Mathf.RoundToInt((float)ecPct);
                if (ecPct <= 5.0) _warnAlerts.Add(new AlertItem("EC CRIT!", $"{ecInt}%", true));
                else if (ecPct <= 20.0) _cautAlerts.Add(new AlertItem("LOW EC", $"{ecInt}%", false));
            }

            // ── 3. 近地危险下沉与地形拉起 (PULL UP! / SINK RATE) ──
            bool severePullUp = (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 600.0 && telem.AltitudeAGL > 3.0) ||
                                (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 150.0 && telem.AltitudeAGL > 3.0);
            if (severePullUp)
            {
                int aglInt = Mathf.RoundToInt((float)telem.AltitudeAGL);
                _warnAlerts.Add(new AlertItem("PULL UP!", $"{aglInt}m", true));
            }
            else if (telem.VerticalSpeed < -15.0 && telem.AltitudeAGL < 1500.0 && telem.AltitudeAGL > 10.0)
            {
                int vsiInt = Mathf.RoundToInt((float)telem.VerticalSpeed);
                _cautAlerts.Add(new AlertItem("SINK RATE", $"{vsiInt}m/s", false));
            }

            // ── 4. 气动失速 (STALL / FAR / GPWS) ──
            double farStall = double.NaN;
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                farStall = ExternalProbeRegistry.ResolveNumeric("FAR", "STALL");
            }
            if (!double.IsNaN(farStall))
            {
                int stallPct = Mathf.RoundToInt((float)(farStall * 100.0));
                if (farStall > 0.70) _warnAlerts.Add(new AlertItem("STALL!", $"{stallPct}%", true));
                else if (farStall > 0.30) _cautAlerts.Add(new AlertItem("STALL WARN", $"{stallPct}%", false));
            }

            // ── 5. 维生系统与氧气 (O2 / KERBALISM) ──
            if (telem.CrewCapacity > 0)
            {
                float o2 = telem.OxygenPercent;
                if (o2 >= 0f)
                {
                    int o2Int = Mathf.RoundToInt(o2);
                    if (o2 <= 5.0f) _warnAlerts.Add(new AlertItem("O2 CRIT!", $"{o2Int}%", true));
                    else if (o2 <= 20.0f) _cautAlerts.Add(new AlertItem("LOW O2", $"{o2Int}%", false));
                }
            }

            // ── 6. 舱温与超温 (OVERHEAT / SYSTEMHEAT) ──
            double cabinTemp = telem.CabinTemp;
            if (cabinTemp > 120.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _warnAlerts.Add(new AlertItem("OVERHEAT!", $"{tempInt}°C", true));
            }
            else if (cabinTemp > 80.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _cautAlerts.Add(new AlertItem("HIGH TEMP", $"{tempInt}°C", false));
            }

            // ── 7. 过载极限 (G-FORCE) ──
            double g = telem.GForce;
            if (g > 9.0)
            {
                _warnAlerts.Add(new AlertItem("EXCESS G!", $"{g:F1}G", true));
            }
            else if (g > 6.0)
            {
                _cautAlerts.Add(new AlertItem("HIGH G", $"{g:F1}G", false));
            }

            // ── 8. 发动机故障 (TESTFLIGHT) ──
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double tfFailed = ExternalProbeRegistry.ResolveNumeric("TF", "FAILED");
                if (tfFailed > 0.5)
                {
                    _warnAlerts.Add(new AlertItem("ENG FAIL!", "FAIL", true));
                }
            }

            // ── 9. 通信网络断开 (NO COMM) ──
            if (!telem.IsConnected && (telem.CrewCount == 0 || telem.CrewCapacity == 0))
            {
                _cautAlerts.Add(new AlertItem("NO COMM", "OFF", false));
            }
        }

        private void RenderVisualCells()
        {
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            if (theme == null) return;

            // ── A. 渲染左舱：CAUTION ──
            bool isCautActive = _cautAlerts.Count > 0;
            if (isCautActive)
            {
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
                AlertItem item = _cautAlerts[_cautIndex];

                string pagination = _cautAlerts.Count > 1 ? $"{_cautIndex + 1}/{_cautAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_cautTitle, item.MainTitle);
                SetTextIfChanged(_cautSub, pagination);
                SetTextIfChanged(_cautIcon, "▲");

                bool blink = _cautAcknowledged || _blink1Hz;
                if (blink)
                {
                    _cautBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme);
                    _cautOutline.effectColor = theme.WarningColor;
                    _cautPipBar.color = theme.WarningColor;
                    _cautTitle.color = theme.WarningColor;
                    _cautSub.color = theme.WarningColor;
                    _cautIcon.color = theme.WarningColor;
                }
                else
                {
                    _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Faint);
                    _cautPipBar.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.30f);
                    _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautSub.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautIcon.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_cautTitle, I18n.Tr("WIDGET_ALERT_CAUTION", "CAUTION"));
                SetTextIfChanged(_cautSub, I18n.Tr("WIDGET_ALERT_NORM", "NORM"));
                SetTextIfChanged(_cautIcon, "●");

                _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _cautPipBar.color = Color.clear;
                _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
                _cautSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _cautIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }

            // ── B. 渲染右舱：WARNING ──
            bool isWarnActive = _warnAlerts.Count > 0;
            if (isWarnActive)
            {
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
                AlertItem item = _warnAlerts[_warnIndex];

                string pagination = _warnAlerts.Count > 1 ? $"{_warnIndex + 1}/{_warnAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_warnTitle, item.MainTitle);
                SetTextIfChanged(_warnSub, pagination);
                SetTextIfChanged(_warnIcon, "▲");

                bool blink = _warnAcknowledged || _blink2Hz;
                if (blink)
                {
                    _warnBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger, theme);
                    _warnOutline.effectColor = theme.DangerColor;
                    _warnPipBar.color = theme.DangerColor;
                    _warnTitle.color = theme.DangerColor;
                    _warnSub.color = theme.DangerColor;
                    _warnIcon.color = theme.DangerColor;
                }
                else
                {
                    _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Faint);
                    _warnPipBar.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.30f);
                    _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnSub.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnIcon.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_warnTitle, I18n.Tr("WIDGET_ALERT_WARNING", "WARNING"));
                SetTextIfChanged(_warnSub, I18n.Tr("WIDGET_ALERT_ARMED", "ARMED"));
                SetTextIfChanged(_warnIcon, "●");

                _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _warnPipBar.color = Color.clear;
                _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
                _warnSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _warnIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }
        }

        protected override void OnLanguageChanged()
        {
            if (!_customSepExplicit) _sepTitleTemplate = I18n.Tr("WIDGET_ALERT_SEPARATION", "分  离");
            if (!_customEngExplicit) _engTitleTemplate = I18n.Tr("WIDGET_ALERT_ENGINE_START", "引擎启动");
            RenderVisualCells();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_outerBezel != null) _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_outerOutline != null) _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_centerDivider != null) _centerDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_horizDivider != null) _horizDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            this.Controls.ApplyThemeToControls(theme);

            if (_modulesCount == 3)
            {
                RenderVisualCells();
                if (_bannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(0f, theme);
                }
            }
            else
            {
                if (_bannerState != BannerDisplayState.Normal)
                {
                    UpdateBannerAnimation(0f, theme);
                }
                else
                {
                    RenderVisualCells();
                }
            }
        }

        protected override void OnDestroy()
        {
            _bannerQueue.Clear();
            _eventLastTriggerTimes.Clear();
            if (_cautBtn != null) _cautBtn.onClick.RemoveAllListeners();
            if (_warnBtn != null) _warnBtn.onClick.RemoveAllListeners();
            if (_centerDividerBtn != null) _centerDividerBtn.onClick.RemoveAllListeners();
            if (_horizDividerBtn != null) _horizDividerBtn.onClick.RemoveAllListeners();
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
