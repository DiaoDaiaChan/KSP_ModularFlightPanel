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
    /// ECAM / EICAS 集中式电子飞行告警与备忘日志组件 (Airbus ECAM / Boeing EICAS Warning and Alert Log).
    /// 严谨落实 MFP 全面标准化铁律：
    /// 1. 0 颜色字面量：100% 由 WidgetStyleManager 语义角色与主题管道赋色；
    /// 2. 0 星球硬编码：依赖 IFlightTelemetry.AtmosphereDepth 与 HasAtmosphere 真实物理天体参数；
    /// 3. Principia 探针优先：自动检索 N 体分析近拱点/远拱点，并无缝 Fallback 原版开普勒参数；
    /// 4. 实时串联分级分离、引擎启动、主发关机、机动点火、入轨圆化、飞船离轨、逃逸轨道、引力穿越、动力减速与再入黑障全航程飞行里程碑。
    /// </summary>
    [FlightWidget("ecam_alert_log", "alert_log", "eicas_messages", "warning_log", Category = WidgetCategory.Systems, DisplayName = "ECAM 飞行告警与备忘日志", Description = "仿空客 ECAM / 波音 EICAS 集中式电子飞行告警屏：实时推演离轨、逃逸、黑障、分级与遥测异常全时序事件日志。", DefaultWidgetId = "custom.ecam_alert_log", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "core.ecam_alert_log", "ecam.alert_log", "custom.ecam_alert_log" })]
    public class EcamAlertLogWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(BASE_WIDTH, BASE_HEIGHT);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 声明式微控件
        public TextWidget HeaderTitle = TextWidget.Title("ECAM / EICAS");

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

        // 日志缓冲队列 (FIFO，上限 40 条)
        private readonly List<EcamLogEntry> _logBuffer = new List<EcamLogEntry>(40);
        private readonly List<EcamLogEntry> _activePersistentAlerts = new List<EcamLogEntry>(8);

        // 交互与显示模式
        private bool _isClearedMode = false;
        private bool _showSystemStatusPage = false;
        private float _globalBlinkTimer = 0f;

        // 历史遥测防抖缓存
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

            // 6. 预置初始飞行清单备忘 (消除冷启动黑屏)
            SeedInitialChecklistMemos();

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

            _phaseBadgeText = UIFactory.CreateText(phaseObj.transform, "PhaseText", "ORBIT", Mathf.RoundToInt(7.5f * s),
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

                Text iconText = UIFactory.CreateText(badgeObj.transform, "Icon", "MEMO", Mathf.RoundToInt(7.5f * s),
                    TextAnchor.MiddleCenter, theme.AccentPositive);
                iconText.fontStyle = FontStyle.Bold;
                iconText.rectTransform.sizeDelta = new Vector2(28f * s, 16f * s);

                // 3. 主标题 (Title)
                Text titleText = UIFactory.CreateText(rowObj.transform, "Title", "SYSTEM NOMINAL", Mathf.RoundToInt(9.5f * s),
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
            _bufferCountText = UIFactory.CreateText(footerObj.transform, "BufCount", "LOG 5/40", Mathf.RoundToInt(7.5f * s),
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

        private void AddButtonListener(GameObject go, Action onClick)
        {
            Button btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private void SeedInitialChecklistMemos()
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
            // 插入至最前面 (最新事件在最顶行)
            _logBuffer.Insert(0, entry);
            if (_logBuffer.Count > 40)
            {
                _logBuffer.RemoveAt(_logBuffer.Count - 1);
            }
        }

        public void ClearAcknowledgedAlerts()
        {
            _isClearedMode = true;
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
            _isClearedMode = false;
            for (int i = 0; i < _logBuffer.Count; i++)
            {
                _logBuffer[i].IsAcknowledged = false;
            }
        }

        public void ToggleStatusPage()
        {
            _showSystemStatusPage = !_showSystemStatusPage;
        }

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
            if (_btnStsActiveBar != null) _btnStsActiveBar.color = _showSystemStatusPage ? theme.AccentPrimary : Color.clear;

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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float dt = Time.deltaTime;
            _globalBlinkTimer += dt;
            if (_globalBlinkTimer >= 1000f) _globalBlinkTimer = 0f;

            // 1. 评估高精度轨道动力学与物理大气边界 (0 硬编码任何星球，Principia 探针优先)
            double atmoCutoff = GetAtmosphereCutoff(telemetry);
            double effectivePe = GetEffectivePeriapsis(telemetry);
            double effectiveAp = GetEffectiveApoapsis(telemetry);

            // 2. 检测机载瞬态事件并自动注入日志队列
            DetectAndIngestEvents(telemetry, atmoCutoff, effectivePe, effectiveAp);

            // 3. 评估持续性告警 (低油量、拉起、过温、失速、过载、沉底等)
            EvaluatePersistentAlerts(telemetry, dt);

            // 4. 汇总当前活跃消息并刷新 5 槽位视图
            RenderEcamLogView(theme, telemetry, effectivePe, effectiveAp);
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

            // A. 分级分离事件 (Staging Separation)
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

            // B. 引擎点火启动 (Engine Ignition)
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

                // C. 主发关机 MECO (Main Engine Cutoff)
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

            // D. 接近机动节点 T-60s
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

            // E. 机动点火执行 BURN
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

            // F. 入轨圆化完成 ORBIT
            if (_lastEffectivePe > -999999.0 && _lastEffectivePe < atmoCutoff && effectivePe >= atmoCutoff && effectiveAp >= atmoCutoff &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "ORB", "★",
                    I18n.Tr("WIDGET_ALERT_ORBIT_ACHIEVED", "入轨圆化建立完成"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            // G. 飞船离轨制动 DEORBIT (核心新增特性)
            if (_lastEffectivePe >= atmoCutoff && _lastEffectiveAp >= atmoCutoff && effectivePe < atmoCutoff && effectivePe > -9000000.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "DEORB", "▼",
                    I18n.Tr("WIDGET_ALERT_DEORBIT", "飞船离轨制动进入走廊"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            // H. 逃逸轨道建立 ESCAPE (双曲线)
            bool isEscNow = (telem.FlightSituation == "ESCAPING") || (effectiveAp < 0 && effectiveAp > -9000000.0);
            bool wasEscBefore = (_lastFlightSituation == "ESCAPING") || (_lastEffectiveAp < 0 && _lastEffectiveAp > -9000000.0);
            if (!wasEscBefore && isEscNow && telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "ESC", "▲",
                    I18n.Tr("WIDGET_ALERT_ESCAPE", "逃逸轨道建立 ESCAPE"),
                    $"Pe {FormatKm(effectivePe)}", curMet, telem.MissionTime));
            }

            // I. 天体引力范围穿越 SOI TRANSITION
            if (!string.IsNullOrEmpty(_lastCelestialBody) && !string.IsNullOrEmpty(telem.CelestialBodyName) &&
                !_lastCelestialBody.Equals(telem.CelestialBodyName, StringComparison.OrdinalIgnoreCase))
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Advisory, "SOI", "◆",
                    I18n.Tr("WIDGET_ALERT_SOI_TRANSITION", "进入天体引力影响圈"),
                    telem.CelestialBodyName, curMet, telem.MissionTime));
            }

            // J. 进入大气层 ATMOSPHERE ENTRY
            if (atmoCutoff > 0.0 && _lastAltitude >= atmoCutoff && telem.AltitudeASL < atmoCutoff && telem.VerticalSpeed < -5.0 &&
                telem.FlightSituation != "LANDED" && telem.FlightSituation != "PRELAUNCH")
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "ENTRY", "▼",
                    I18n.Tr("WIDGET_ALERT_ATMOSPHERE_ENTRY", "穿越边界进入大气层"),
                    $"M {telem.Mach:F1}", curMet, telem.MissionTime));
            }

            // K. 再入黑障 BLACKOUT
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

            // L. 动力减速着陆 SUICIDE BURN
            if (telem.AltitudeAGL < 2000.0 && telem.AltitudeAGL > 15.0 && telem.VerticalSpeed < -15.0 && telem.Throttle > 0.40f && telem.ActiveEngines > 0)
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "BURN", "▼",
                    I18n.Tr("WIDGET_ALERT_SUICIDE_BURN", "动力减速着陆推力建立"),
                    $"AGL {telem.AltitudeAGL:F0}m", curMet, telem.MissionTime));
            }

            // M. 着陆接地确认 TOUCHDOWN
            bool isLandedNow = (telem.FlightSituation == "LANDED" || telem.FlightSituation == "SPLASHED" || telem.IsTouchdownAlert);
            if (!_lastIsLanded && isLandedNow && _lastAltitudeAGL > 2.0)
            {
                PushLogEntry(new EcamLogEntry(
                    EcamAlertSeverity.Memo, "TOUCH", "⚓",
                    I18n.Tr("WIDGET_ALERT_TOUCHDOWN", "着陆接地成功 TOUCHDOWN"),
                    "LANDED", curMet, telem.MissionTime));
            }

            // 缓存本帧遥测
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

            // 1. 推进剂余量警告 (Strict Fuel Boundaries)
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
                            "MIN FUEL EMERGENCY", $"{pct}%", curMet, telem.MissionTime, true));
                    }
                    else
                    {
                        _activePersistentAlerts.Add(new EcamLogEntry(
                            EcamAlertSeverity.Caution, "FUEL", "▲",
                            "LOW FUEL ADVISORY", $"{pct}%", curMet, telem.MissionTime, true));
                    }
                }
            }

            // 2. 近地拉起 PULL UP! / SINK RATE
            bool severePullUp = (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 600.0 && telem.AltitudeAGL > 3.0) ||
                                (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 150.0 && telem.AltitudeAGL > 3.0);
            if (severePullUp)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "GPWS", "!",
                    "TERRAIN PULL UP!", $"{telem.AltitudeAGL:F0}m", curMet, telem.MissionTime, true));
            }
            else if (telem.VerticalSpeed < -15.0 && telem.AltitudeAGL < 1500.0 && telem.AltitudeAGL > 10.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Caution, "GPWS", "▲",
                    "EXCESS SINK RATE", $"{telem.VerticalSpeed:F0}m/s", curMet, telem.MissionTime, true));
            }

            // 3. 电网余量 CRITICAL EC
            if (telem.EcPercent >= 0.0)
            {
                if (telem.EcPercent <= 5.0)
                {
                    _activePersistentAlerts.Add(new EcamLogEntry(
                        EcamAlertSeverity.Warning, "ELEC", "!",
                        "BATTERY CRITICAL!", $"{Mathf.RoundToInt((float)telem.EcPercent)}%", curMet, telem.MissionTime, true));
                }
                else if (telem.EcPercent <= 20.0)
                {
                    _activePersistentAlerts.Add(new EcamLogEntry(
                        EcamAlertSeverity.Caution, "ELEC", "▲",
                        "LOW BATTERY EC", $"{Mathf.RoundToInt((float)telem.EcPercent)}%", curMet, telem.MissionTime, true));
                }
            }

            // 4. 超温与超载 OVERHEAT / EXCESS G
            if (telem.CabinTemp > 120.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "TEMP", "!",
                    "CABIN OVERHEAT!", $"{Mathf.RoundToInt((float)telem.CabinTemp)}°C", curMet, telem.MissionTime, true));
            }
            if (telem.GForce > 9.0)
            {
                _activePersistentAlerts.Add(new EcamLogEntry(
                    EcamAlertSeverity.Warning, "G-LOAD", "!",
                    "EXCESS G-LOAD!", $"{telem.GForce:F1}G", curMet, telem.MissionTime, true));
            }
        }

        private static string FormatFlightPhase(string sit)
        {
            if (string.IsNullOrEmpty(sit)) return "CRUISE";
            string upper = sit.ToUpperInvariant();
            if (upper.Contains("ORBIT")) return "ORBIT";
            if (upper.Contains("CRUISE")) return "CRUISE";
            if (upper.Contains("LAUNCH") || upper.Contains("PRE")) return "PRE-LCH";
            if (upper.Contains("FLY")) return "FLIGHT";
            if (upper.Contains("ESC")) return "ESCAPE";
            if (upper.Contains("LAND")) return "LANDED";
            if (upper.Contains("SPLASH")) return "SPLASH";
            if (upper.Contains("SUB")) return "SUB-ORB";
            if (upper.Contains("DOCK")) return "DOCKED";
            return upper.Length > 6 ? upper.Substring(0, 6) : upper;
        }

        private void RenderEcamLogView(ThemeConfig theme, IFlightTelemetry telem, double effectivePe, double effectiveAp)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 统计总计数 (Statistics)
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

            // 闪烁时钟 (Level 3 Warning 2Hz 闪烁)
            bool blinkOn = ((int)(_globalBlinkTimer * 2f)) % 2 == 0;

            // 1. 顶栏飞行阶段胶囊更新
            string phaseName = FormatFlightPhase(telem?.FlightSituation);
            SetTextIfChanged(_phaseBadgeText, phaseName);

            // 2. 顶栏光字牌更新 (Master Annunciator Tiles)
            SetTextIfChanged(_badgeWarnText, $"! {warnCount}");
            if (warnCount > 0)
            {
                Color warnCol = blinkOn ? theme.DangerColor : WidgetStyleManager.WithAlpha(theme.DangerColor, 0.60f);
                if (_warnBoxBg != null) _warnBoxBg.color = WidgetStyleManager.WithAlpha(theme.DangerColor, blinkOn ? 0.35f : 0.18f);
                if (_warnBoxOutline != null) _warnBoxOutline.effectColor = warnCol;
                if (_warnTopPip != null) _warnTopPip.color = warnCol;
                if (_badgeWarnText != null) _badgeWarnText.color = warnCol;
            }
            else
            {
                if (_warnBoxBg != null) _warnBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_warnBoxOutline != null) _warnBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
                if (_warnTopPip != null) _warnTopPip.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
                if (_badgeWarnText != null) _badgeWarnText.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
            }

            SetTextIfChanged(_badgeCautText, $"▲ {cautCount}");
            if (cautCount > 0)
            {
                if (_cautBoxBg != null) _cautBoxBg.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.25f);
                if (_cautBoxOutline != null) _cautBoxOutline.effectColor = theme.WarningColor;
                if (_cautTopPip != null) _cautTopPip.color = theme.WarningColor;
                if (_badgeCautText != null) _badgeCautText.color = theme.WarningColor;
            }
            else
            {
                if (_cautBoxBg != null) _cautBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_cautBoxOutline != null) _cautBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
                if (_cautTopPip != null) _cautTopPip.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
                if (_badgeCautText != null) _badgeCautText.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
            }

            SetTextIfChanged(_badgeMemoText, $"● {memoCount}");
            if (memoCount > 0)
            {
                if (_memoBoxBg != null) _memoBoxBg.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.18f);
                if (_memoBoxOutline != null) _memoBoxOutline.effectColor = theme.AccentPositive;
                if (_memoTopPip != null) _memoTopPip.color = theme.AccentPositive;
                if (_badgeMemoText != null) _badgeMemoText.color = theme.AccentPositive;
            }
            else
            {
                if (_memoBoxBg != null) _memoBoxBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_memoBoxOutline != null) _memoBoxOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f);
                if (_memoTopPip != null) _memoTopPip.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.22f);
                if (_badgeMemoText != null) _badgeMemoText.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.40f);
            }

            // 3. 底栏总体工况显示
            if (_showSystemStatusPage)
            {
                RenderSystemStatusPage(theme, telem, effectivePe, effectiveAp);
                return;
            }

            if (_btnStsActiveBar != null) _btnStsActiveBar.color = Color.clear;
            if (_btnStsBg != null) _btnStsBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Control, theme);
            if (_btnStsOutline != null) _btnStsOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Subtle);

            if (warnCount > 0)
            {
                SetTextIfChanged(_statusText, I18n.Tr("WIDGET_ECAM_WARN", "严重危急警报"));
                if (_statusText != null) _statusText.color = theme.DangerColor;
                Color dotCol = blinkOn ? theme.DangerColor : WidgetStyleManager.WithAlpha(theme.DangerColor, 0.3f);
                if (_statusLedDot != null) _statusLedDot.color = dotCol;
                if (_statusLedHalo != null) _statusLedHalo.color = WidgetStyleManager.WithAlpha(theme.DangerColor, blinkOn ? 0.40f : 0.15f);
            }
            else if (cautCount > 0)
            {
                SetTextIfChanged(_statusText, I18n.Tr("WIDGET_ECAM_ATTN", "需机组注意处理"));
                if (_statusText != null) _statusText.color = theme.WarningColor;
                if (_statusLedDot != null) _statusLedDot.color = theme.WarningColor;
                if (_statusLedHalo != null) _statusLedHalo.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.25f);
            }
            else
            {
                SetTextIfChanged(_statusText, I18n.Tr("WIDGET_ECAM_NORMAL", "系统工况受监控"));
                if (_statusText != null) _statusText.color = theme.AccentPositive;
                if (_statusLedDot != null) _statusLedDot.color = theme.AccentPositive;
                if (_statusLedHalo != null) _statusLedHalo.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.25f);
            }

            // 4. 缓冲日志计数
            SetTextIfChanged(_bufferCountText, $"LOG {_logBuffer.Count}/40");

            // 5. 合成展示队列：优先顶显未消警的持续性警告，随后展示时序日志条目
            List<EcamLogEntry> displayList = new List<EcamLogEntry>(MAX_DISPLAY_ROWS);
            for (int i = 0; i < _activePersistentAlerts.Count && displayList.Count < MAX_DISPLAY_ROWS; i++)
            {
                displayList.Add(_activePersistentAlerts[i]);
            }
            for (int i = 0; i < _logBuffer.Count && displayList.Count < MAX_DISPLAY_ROWS; i++)
            {
                if (_isClearedMode && _logBuffer[i].IsAcknowledged) continue;
                displayList.Add(_logBuffer[i]);
            }

            for (int slotIdx = 0; slotIdx < MAX_DISPLAY_ROWS; slotIdx++)
            {
                RowSlot slot = _rowSlots[slotIdx];
                if (slotIdx < displayList.Count)
                {
                    EcamLogEntry entry = displayList[slotIdx];
                    if (!slot.Root.activeSelf) slot.Root.SetActive(true);

                    Color itemColor;
                    switch (entry.Severity)
                    {
                        case EcamAlertSeverity.Warning: itemColor = theme.DangerColor; break;
                        case EcamAlertSeverity.Caution: itemColor = theme.WarningColor; break;
                        case EcamAlertSeverity.Advisory: itemColor = theme.AccentPrimary; break;
                        case EcamAlertSeverity.Memo: default: itemColor = theme.AccentPositive; break;
                    }

                    if (entry.Severity == EcamAlertSeverity.Warning && !blinkOn)
                    {
                        itemColor = WidgetStyleManager.WithAlpha(itemColor, 0.45f);
                    }

                    // 1. 左侧竖条指示色彩
                    if (slot.LeftPipBar != null) slot.LeftPipBar.color = itemColor;

                    // 2. 等级图标与子系统芯片 (Badge Chip)
                    if (slot.BadgeBg != null) slot.BadgeBg.color = WidgetStyleManager.WithAlpha(itemColor, 0.16f);
                    if (slot.BadgeOutline != null) slot.BadgeOutline.effectColor = WidgetStyleManager.WithAlpha(itemColor, 0.65f);
                    SetTextIfChanged(slot.IconText, !string.IsNullOrEmpty(entry.Tag) ? entry.Tag : entry.Icon);
                    if (slot.IconText != null) slot.IconText.color = itemColor;

                    // 3. 标题
                    SetTextIfChanged(slot.TitleText, entry.Title);
                    if (slot.TitleText != null)
                    {
                        slot.TitleText.color = (entry.Severity == EcamAlertSeverity.Warning || entry.Severity == EcamAlertSeverity.Caution)
                            ? itemColor
                            : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    }

                    // 4. 遥测读数芯片
                    SetTextIfChanged(slot.DetailText, entry.Detail);
                    if (slot.DetailBg != null) slot.DetailBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    if (slot.DetailOutline != null) slot.DetailOutline.effectColor = WidgetStyleManager.WithAlpha(itemColor, 0.35f);
                    if (slot.DetailText != null) slot.DetailText.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

                    // 5. 任务时钟
                    SetTextIfChanged(slot.TimeText, entry.Timestamp);
                    if (slot.TimeText != null) slot.TimeText.color = style.GetTextColor(TextStyleRole.Unit, theme);

                    // 6. 卡片底色与微光边框
                    if (slot.RowBg != null)
                    {
                        slot.RowBg.color = entry.IsPersistent
                            ? WidgetStyleManager.WithAlpha(itemColor, 0.15f)
                            : WidgetStyleManager.WithAlpha(theme.AccentSecondary, slotIdx % 2 == 0 ? 0.04f : 0.07f);
                    }
                    if (slot.RowOutline != null)
                    {
                        slot.RowOutline.effectColor = entry.IsPersistent
                            ? WidgetStyleManager.WithAlpha(itemColor, 0.50f)
                            : WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f);
                    }
                }
                else
                {
                    // 空余行槽位隐藏
                    if (slot.Root.activeSelf) slot.Root.SetActive(false);
                }
            }
        }

        private struct SysDiagEntry
        {
            public string Tag;
            public string Title;
            public string Val;
            public string Aux;
            public Color Col;
        }

        private void RenderSystemStatusPage(ThemeConfig theme, IFlightTelemetry telem, double effectivePe, double effectiveAp)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            SetTextIfChanged(_statusText, I18n.Tr("ECAM_SYS_STS", "机载系统工况 STS"));
            if (_statusText != null) _statusText.color = theme.AccentPrimary;
            if (_statusLedDot != null) _statusLedDot.color = theme.AccentPrimary;
            if (_statusLedHalo != null) _statusLedHalo.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.25f);
            SetTextIfChanged(_bufferCountText, "PAGE 1/1");

            if (_btnStsBg != null) _btnStsBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.35f);
            if (_btnStsOutline != null) _btnStsOutline.effectColor = theme.AccentPrimary;
            if (_btnStsActiveBar != null) _btnStsActiveBar.color = theme.AccentPrimary;

            SysDiagEntry[] diag = new SysDiagEntry[MAX_DISPLAY_ROWS]
            {
                new SysDiagEntry { Tag = "PROP", Title = I18n.Tr("ECAM_SYS_PROP", "动力推进系统 PROP"), Val = $"{telem.ActiveEngines} ENG", Aux = $"THR {Mathf.RoundToInt(telem.Throttle * 100f)}%", Col = theme.AccentPrimary },
                new SysDiagEntry { Tag = "ELEC", Title = I18n.Tr("ECAM_SYS_ELEC", "机载电网能源 ELEC"), Val = $"{Mathf.RoundToInt((float)telem.EcPercent)}% EC", Aux = "BUS OK", Col = telem.EcPercent <= 20.0 ? theme.WarningColor : theme.AccentPositive },
                new SysDiagEntry { Tag = "TRAJ", Title = I18n.Tr("ECAM_SYS_TRAJ", "轨道动力参数 TRAJ"), Val = $"Pe {FormatKm(effectivePe)}", Aux = $"Ap {FormatKm(effectiveAp)}", Col = theme.AccentPrimary },
                new SysDiagEntry { Tag = "ATMO", Title = I18n.Tr("ECAM_SYS_ATMO", "飞行走廊环境 ATMO"), Val = $"M {telem.Mach:F1}", Aux = $"Q {telem.DynamicPressure:F1}k", Col = theme.AccentSecondary },
                new SysDiagEntry { Tag = "GUID", Title = I18n.Tr("ECAM_SYS_GUID", "姿态惯导工况 GUID"), Val = $"{telem.GForce:F1}G", Aux = $"STG {telem.CurrentStage}", Col = theme.AccentPositive }
            };

            for (int i = 0; i < MAX_DISPLAY_ROWS; i++)
            {
                RowSlot slot = _rowSlots[i];
                if (!slot.Root.activeSelf) slot.Root.SetActive(true);
                SysDiagEntry d = diag[i];

                if (slot.LeftPipBar != null) slot.LeftPipBar.color = d.Col;
                if (slot.BadgeBg != null) slot.BadgeBg.color = WidgetStyleManager.WithAlpha(d.Col, 0.16f);
                if (slot.BadgeOutline != null) slot.BadgeOutline.effectColor = WidgetStyleManager.WithAlpha(d.Col, 0.65f);
                SetTextIfChanged(slot.IconText, d.Tag);
                if (slot.IconText != null) slot.IconText.color = d.Col;

                SetTextIfChanged(slot.TitleText, d.Title);
                if (slot.TitleText != null) slot.TitleText.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);

                SetTextIfChanged(slot.DetailText, d.Val);
                if (slot.DetailBg != null) slot.DetailBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (slot.DetailOutline != null) slot.DetailOutline.effectColor = WidgetStyleManager.WithAlpha(d.Col, 0.35f);
                if (slot.DetailText != null) slot.DetailText.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

                SetTextIfChanged(slot.TimeText, d.Aux);
                if (slot.TimeText != null) slot.TimeText.color = style.GetTextColor(TextStyleRole.Unit, theme);

                if (slot.RowBg != null) slot.RowBg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, i % 2 == 0 ? 0.04f : 0.07f);
                if (slot.RowOutline != null) slot.RowOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.15f);
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
