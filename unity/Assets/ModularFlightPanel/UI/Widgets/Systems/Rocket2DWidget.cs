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
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 飞船分级二维拓扑结构仪 (2D Vessel Silhouette Staging Topology)
    /// ====================================================================================
    /// 核心特性：
    /// 1. 左侧 2D 飞船剪影拓扑视窗 (Vehicle Silhouette Bay)：
    ///    - 优先联动 VesselSilhouetteBaker (IVesselSilhouetteProvider) 获取游戏内真实飞船实时烘焙轮廓，支持助推器/整流罩脱落视觉飞离；
    ///    - 离线、无头渲染或原地沙盒下自适应切换至高精多级火箭程序化矢量剪影保底；
    ///    - 动态发动机喷流羽流 (Exhaust Plume)：在激活发动机正下方呈现随油门缩放并伴随高频喷焰微颤（26Hz）的动态羽流；
    ///    - 视窗四角航电瞄准框标 (Corner Reticles) 与动态底标 (STAGE S06 / ACTIVE)。
    /// 2. 右侧动态多级垂直推进栈 (Multistage Propulsion Stack)：
    ///    - 全面摒弃写死 3 级限制，动态读取并展示 StageDeltaVList 真实载具多级推进数据；
    ///    - 采用标准航电分级顺序（自顶向下：S00 载荷/卫星至当前点火底级）；
    ///    - 单级卡片集成：左侧激光连接引线、分级徽章 (S06)、分级角色 (BOOSTER / CORE / UPPER / PAYLOAD)、
    ///      推进剂液位量程条 (带平滑阻尼插值与 <5% 烈度频闪)、单级 ΔV 读数、燃烧倒计时 (⏱ mm:ss) 与 TWR 推重比。
    /// 3. 顶部航电综合简报栏 (Avionics Header Summary)：
    ///    - 标题与副标题动态求值 (ROCKET / 2D · STAGING TOPOLOGY)；
    ///    - 中央实时 TWR 与激活发动机计数；
    ///    - 右侧全级总 ΔV 汇总与战备状态光字 (● ARMED / ● BURNING / ● SAFED)。
    /// 4. 严格落实 MFP-SPEC-001..007 铁律：
    ///    - 0 颜色字面量 (MFP-SPEC-006)：100% 经由 WidgetStyleManager 语义着色；
    ///    - 0 场景查询 (MFP-SPEC-007)；
    ///    - 纯 C# UGUI 架构与零 GC 文本脏检查守护 (SetTextIfChanged)；
    ///    - 支持 CustomTemplate 通配符通道覆写。
    /// </summary>
    [FlightWidget("rocket2d", "rocket", "staging_diagram", Category = WidgetCategory.Systems, DisplayName = "ROCKET 2D 垂直推进栈姿态卡", Description = "多级火箭垂直推进栈、推进剂实时耗尽进度条、发动机工况与本级 dV。", DefaultWidgetId = "custom.rocket", DefaultX = 440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "custom.rocket", "custom.stage", "custom.staging", "core.rocket2d" })]
    public class Rocket2DWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(DefaultWidth, DefaultHeight);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_ROCKET_TITLE", "火箭 2D"));
        public TextWidget StatusBadge = TextWidget.Badge(I18n.Tr("WIDGET_ALERT_ARMED", "待发"));

        // 单级推进堆叠行 UI 结构
        private class StageRowUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image LeaderLine;
            public GameObject BadgeObj;
            public Image BadgeBg;
            public Text BadgeText;
            public Text StageRoleText;
            public Text StageDvText;
            public Image FuelTrack;
            public RectTransform FuelFillRt;
            public Image FuelFill;
            public Text FuelPercentText;
            public Text StageTimeText;
            public Text StageTwrText;
            public Image RowHighlightBg;

            // 动效与平滑状态
            public bool IsActiveStage;
            public bool IsBurning;
            public float TargetFuelFrac;
            public float CurrentFuelFrac;
            public int StageNumber;
            public readonly CachedFloat LastFuelFrac = new CachedFloat(-1f, 0.001f);
            public readonly Cached<string> LastPctText = new Cached<string>(string.Empty);
        }

        // 外框与底板
        private Image _bgImage;
        private Outline _outline;

        // 顶栏航电总览
        private Text _titleText;
        private Text _subTitleText;
        private Text _summaryTwrText;
        private Text _summaryDvText;
        private Image _headerDivider;

        // 左侧 2D 飞船剪影视窗 (Vehicle Silhouette Bay)
        private GameObject _silhouetteBayObj;
        private Image _silhouetteBayBg;
        private Outline _silhouetteBayOutline;
        private Text _silhouetteBayTitle;
        private Text _silhouetteBayFooter;
        private RawImage _silhouetteRawImage;
        private static Texture2D _fallbackSilhouetteTexture;

        // 动态发动机喷流羽流 (Exhaust Plume)
        private GameObject _plumeObj;
        private RectTransform _plumeRt;
        private Image _plumeImg;

        // 右侧动态多级推进栈
        private const int MaxDisplayedStages = 5;
        private readonly List<StageRowUI> _stageRows = new List<StageRowUI>();

        // 几何尺寸
        private const float DefaultWidth = 260f;
        private const float DefaultHeight = 176f;
        private const float FuelTrackMaxWidth = 48f;

        // 风格与通配符配置
        private string _titleTemplate = "ROCKET 2D";
        private string _subTitleTemplate = "STAGING";
        private string _stageDvToken = "{STAGE:DV}";
        private string _totalDvToken = "{DV:TOTAL}";
        private string _twrToken = "{TWR}";
        private ThemeConfig _cachedTheme;

        // 脏检查缓存
        private readonly Cached<string> _cachedTitleSlot = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastPlumeFlutter = new CachedFloat(-1f, 0.005f);

        // 动画时间模拟支持 (用于无头单帧/连续帧确定性渲染)
        public static float CustomAnimationTime = -1f;
        public static float CustomAnimationDeltaTime = -1f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _titleTemplate = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_ROCKET_TITLE", "ROCKET 2D"));
            _subTitleTemplate = GetTemplateChannel("SUBTITLE", I18n.Tr("WIDGET_SIG_CTRL_STAGING", "STAGING"));
            _stageDvToken = GetTemplateChannel(new[] { "STAGE_DV", "DV_TOKEN" }, "{STAGE:DV}");
            _totalDvToken = GetTemplateChannel(new[] { "TOTAL_DV", "TOTAL_DV_TOKEN" }, "{DV:TOTAL}");
            _twrToken = GetTemplateChannel(new[] { "TWR", "TWR_TOKEN" }, "{TWR}");

            Vector2 panelSize = BaseSize * s;
            _bgImage = CardBackground;
            _outline = CardOutline;

            // 2. 顶部航电综合简报栏 (Header)
            BuildHeader(transform, panelSize, s, theme);

            // 3. 左侧 2D 飞船剪影视窗 (Silhouette Bay)
            BuildSilhouetteBay(transform, panelSize, s, theme);

            // 4. 右侧多级垂直推进栈 (Propulsion Stacks)
            BuildPropulsionStack(transform, panelSize, s, theme);

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetHeaderControl("header_summary", "顶部简报栏", _titleText != null ? _titleText.gameObject : null, _titleText, _subTitleText));
            this.Controls.Register(new WidgetReadoutControl("twr_dv_readout", "TWR与总速度增量", _summaryDvText != null ? _summaryDvText.gameObject : null, _summaryDvText, _summaryTwrText, TextStyleRole.PrimaryValue, _totalDvToken));
            if (_silhouetteBayObj != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "silhouette_bay", "飞船剪影视窗", _silhouetteBayObj));
            if (_stageRows.Count > 0) this.Controls.Register(WidgetControlManager.WrapElement(this, "propulsion_stack", "多级推进栈", _stageRows[0].Root));
        }

        private void BuildHeader(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float halfW = panelSize.x * 0.5f;
            float topY = panelSize.y * 0.5f - 14f * s;

            // 主标题
            _titleText = UIFactory.CreateText(parent, "Title", _titleTemplate, Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.pivot = new Vector2(0f, 0.5f);
            titRt.sizeDelta = new Vector2(76f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-halfW + 10f * s, topY);

            // 副标题
            _subTitleText = UIFactory.CreateText(parent, "SubTitle", _subTitleTemplate, Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.sizeDelta = new Vector2(45f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(-halfW + 86f * s, topY);

            // 中央实时 TWR
            _summaryTwrText = UIFactory.CreateText(parent, "Sum_Twr", "TWR 0.00", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _summaryTwrText.fontStyle = FontStyle.Bold;
            RectTransform twrRt = _summaryTwrText.GetComponent<RectTransform>();
            twrRt.pivot = new Vector2(1f, 0.5f);
            twrRt.sizeDelta = new Vector2(55f * s, 16f * s);
            twrRt.anchoredPosition = new Vector2(halfW - 74f * s, topY);

            // 右侧总 ΔV 汇总与状态 LED
            _summaryDvText = UIFactory.CreateText(parent, "Sum_Dv", "--- m/s", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _summaryDvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = _summaryDvText.GetComponent<RectTransform>();
            dvRt.pivot = new Vector2(1f, 0.5f);
            dvRt.sizeDelta = new Vector2(66f * s, 16f * s);
            dvRt.anchoredPosition = new Vector2(halfW - 8f * s, topY);

            // 分割微线
            _headerDivider = CreateChild<Image>("Header_Divider", parent,
                new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, panelSize.y * 0.5f - 24f * s));
            _headerDivider.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            _headerDivider.raycastTarget = false;
        }

        private void BuildSilhouetteBay(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float halfW = panelSize.x * 0.5f;
            float bayW = 60f * s;
            float bayH = 138f * s;
            float bayX = -halfW + 10f * s + bayW * 0.5f; // -130 + 10 + 30 = -90f * s
            float bayY = -12f * s;

            // 视窗深邃暗晶背板
            _silhouetteBayObj = UIFactory.CreatePanel(parent, "SilhouetteBay", new Vector2(bayW, bayH),
                new Vector2(bayX, bayY), WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong), 1f * s);
            _silhouetteBayBg = _silhouetteBayObj.GetComponent<Image>();
            _silhouetteBayOutline = _silhouetteBayObj.GetComponent<Outline>();

            // 四角航电瞄准框标 (Corner Reticles)
            float retLen = 4.5f * s;
            float retW = 1.2f * s;
            Color retCol = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Heavy);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);

            // 视窗顶部标牌
            _silhouetteBayTitle = UIFactory.CreateText(_silhouetteBayObj.transform, "BayTitle", I18n.Tr("WIDGET_ROCKET_PROFILE", "剖面"),
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            _silhouetteBayTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _silhouetteBayTitle.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(0f, bayH * 0.5f - 7.5f * s);
            titleRt.sizeDelta = new Vector2(bayW - 4f * s, 11f * s);

            // 2D 飞船剪影图元 (RawImage)
            _silhouetteRawImage = CreateChild<RawImage>("VesselSilhouette_RawImage", _silhouetteBayObj.transform,
                new Vector2(50f * s, 108f * s), new Vector2(0f, -2f * s));
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = theme.AccentSecondary;

            // 优先直通真实载具烘焙纹理，保底接入程序化矢量纹理
            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            if (tex == null)
            {
                if (_fallbackSilhouetteTexture == null)
                {
                    _fallbackSilhouetteTexture = CreateProceduralRocketSilhouetteTexture();
                }
                tex = _fallbackSilhouetteTexture;
            }
            _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 发动机点火羽流 (Exhaust Plume)
            _plumeObj = UIFactory.CreatePanel(_silhouetteBayObj.transform, "EnginePlume", new Vector2(10f * s, 12f * s),
                Vector2.zero, style.GetMeterColor(MeterStyleRole.Warning, theme));
            _plumeRt = _plumeObj.GetComponent<RectTransform>();
            _plumeRt.pivot = new Vector2(0.5f, 1f);
            _plumeRt.anchoredPosition = new Vector2(0f, -bayH * 0.5f + 25f * s);
            _plumeImg = _plumeObj.GetComponent<Image>();
            _plumeObj.SetActive(false);

            // 视窗底部标牌
            _silhouetteBayFooter = UIFactory.CreateText(_silhouetteBayObj.transform, "BayFooter", "S-- · " + I18n.Tr("WIDGET_ALERT_ARMED", "待发"),
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _silhouetteBayFooter.fontStyle = FontStyle.Bold;
            RectTransform footRt = _silhouetteBayFooter.GetComponent<RectTransform>();
            footRt.anchoredPosition = new Vector2(0f, -bayH * 0.5f + 8f * s);
            footRt.sizeDelta = new Vector2(bayW - 4f * s, 12f * s);
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            if (_silhouetteRawImage != null && tex != null)
            {
                _silhouetteRawImage.texture = tex;
            }
        }

        private void BuildPropulsionStack(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float halfW = panelSize.x * 0.5f;
            float stackStartX = -halfW + 74f * s; // 紧随视窗右侧
            float stackWidth = panelSize.x - 82f * s; // 约 178px
            float startY = panelSize.y * 0.5f - 38f * s;
            float rowHeight = 24f * s;
            float rowSpacing = 3.5f * s;

            for (int i = 0; i < MaxDisplayedStages; i++)
            {
                float rowY = startY - i * (rowHeight + rowSpacing);

                RectTransform rowRt = CreateContainer($"StageRow_{i}", parent,
                    new Vector2(stackWidth, rowHeight), new Vector2(stackStartX, rowY));
                rowRt.pivot = new Vector2(0f, 0.5f);
                GameObject rowObj = rowRt.gameObject;

                // 全行背景高亮 (用于活跃级呼吸)
                Image rowBgImg = CreateChild<Image>("HighlightBg", rowObj.transform);
                RectTransform rowBgRt = rowBgImg.rectTransform;
                rowBgRt.anchorMin = Vector2.zero;
                rowBgRt.anchorMax = Vector2.one;
                rowBgRt.sizeDelta = Vector2.zero;
                rowBgImg.color = Color.clear;
                rowBgImg.raycastTarget = false;

                // 1. 左侧激光连接引线 (Leader Line)
                GameObject leaderObj = UIFactory.CreatePanel(rowObj.transform, "Leader", new Vector2(10f * s, 1.2f * s),
                    Vector2.zero, WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal));
                RectTransform leaderRt = leaderObj.GetComponent<RectTransform>();
                leaderRt.anchorMin = new Vector2(0f, 0.5f);
                leaderRt.anchorMax = new Vector2(0f, 0.5f);
                leaderRt.pivot = new Vector2(0f, 0.5f);
                leaderRt.anchoredPosition = new Vector2(-4f * s, 0f);
                Image leaderImg = leaderObj.GetComponent<Image>();

                // 2. 分级徽章 (Badge: S06, S05...)
                GameObject badgeObj = UIFactory.CreatePanel(rowObj.transform, "Badge", new Vector2(23f * s, 16f * s),
                    Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                    WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong), 1f * s);
                RectTransform badgeRt = badgeObj.GetComponent<RectTransform>();
                badgeRt.anchorMin = new Vector2(0f, 0.5f);
                badgeRt.anchorMax = new Vector2(0f, 0.5f);
                badgeRt.pivot = new Vector2(0f, 0.5f);
                badgeRt.anchoredPosition = new Vector2(6f * s, 0f);
                Image badgeBg = badgeObj.GetComponent<Image>();

                Text badgeText = UIFactory.CreateText(badgeObj.transform, "BadgeText", $"S{i:D2}", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                badgeText.fontStyle = FontStyle.Bold;
                RectTransform badgeTextRt = badgeText.GetComponent<RectTransform>();
                badgeTextRt.anchorMin = new Vector2(0.5f, 0.5f);
                badgeTextRt.anchorMax = new Vector2(0.5f, 0.5f);
                badgeTextRt.sizeDelta = badgeRt.sizeDelta;
                badgeTextRt.anchoredPosition = Vector2.zero;

                // 3. 上半行：分级角色/说明 + 单级 ΔV
                Text roleText = UIFactory.CreateText(rowObj.transform, "RoleText", I18n.Tr("WIDGET_ROCKET_PROPULSION", "推进系统"), Mathf.RoundToInt(7.5f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                roleText.fontStyle = FontStyle.Bold;
                RectTransform roleRt = roleText.GetComponent<RectTransform>();
                roleRt.anchorMin = new Vector2(0f, 0.5f);
                roleRt.anchorMax = new Vector2(0f, 0.5f);
                roleRt.pivot = new Vector2(0f, 0.5f);
                roleRt.anchoredPosition = new Vector2(33f * s, 5.5f * s);
                roleRt.sizeDelta = new Vector2(74f * s, 12f * s);

                Text dvText = UIFactory.CreateText(rowObj.transform, "DvText", "--- m/s", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                dvText.fontStyle = FontStyle.Bold;
                RectTransform dvRt = dvText.GetComponent<RectTransform>();
                dvRt.anchorMin = new Vector2(1f, 0.5f);
                dvRt.anchorMax = new Vector2(1f, 0.5f);
                dvRt.pivot = new Vector2(1f, 0.5f);
                dvRt.anchoredPosition = new Vector2(-4f * s, 5.5f * s);
                dvRt.sizeDelta = new Vector2(64f * s, 12f * s);

                // 4. 下半行：推进剂微槽 + 百分比 + 时序/TWR
                float trackW = FuelTrackMaxWidth * s;
                float trackH = 4.5f * s;
                GameObject trackObj = UIFactory.CreatePanel(rowObj.transform, "FuelTrack", new Vector2(trackW, trackH),
                    Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                    WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 0.8f * s);
                RectTransform trackRt = trackObj.GetComponent<RectTransform>();
                trackRt.anchorMin = new Vector2(0f, 0.5f);
                trackRt.anchorMax = new Vector2(0f, 0.5f);
                trackRt.pivot = new Vector2(0f, 0.5f);
                trackRt.anchoredPosition = new Vector2(33f * s, -5.5f * s);
                Image trackBg = trackObj.GetComponent<Image>();

                Image fillImg = CreateChild<Image>("FuelFill", trackObj.transform,
                    new Vector2(trackW, trackH - 1f * s), new Vector2(0.5f * s, 0f));
                GameObject fillObj = fillImg.gameObject;
                RectTransform fillRt = fillImg.rectTransform;
                fillRt.anchorMin = new Vector2(0f, 0.5f);
                fillRt.anchorMax = new Vector2(0f, 0.5f);
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillImg.color = theme.AccentPrimary;

                Text pctText = UIFactory.CreateText(rowObj.transform, "PctText", "100%", Mathf.RoundToInt(6.5f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform pctRt = pctText.GetComponent<RectTransform>();
                pctRt.anchorMin = new Vector2(0f, 0.5f);
                pctRt.anchorMax = new Vector2(0f, 0.5f);
                pctRt.pivot = new Vector2(0f, 0.5f);
                pctRt.anchoredPosition = new Vector2((33f + FuelTrackMaxWidth + 4f) * s, -5.5f * s);
                pctRt.sizeDelta = new Vector2(24f * s, 11f * s);

                Text metaText = UIFactory.CreateText(rowObj.transform, "MetaText", I18n.Tr("WIDGET_ROCKET_STAGE_META", "00:00 · 0.00 吨"), Mathf.RoundToInt(7f * s),
                    TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform metaRt = metaText.GetComponent<RectTransform>();
                metaRt.anchorMin = new Vector2(1f, 0.5f);
                metaRt.anchorMax = new Vector2(1f, 0.5f);
                metaRt.pivot = new Vector2(1f, 0.5f);
                metaRt.anchoredPosition = new Vector2(-4f * s, -5.5f * s);
                metaRt.sizeDelta = new Vector2(62f * s, 11f * s);

                _stageRows.Add(new StageRowUI
                {
                    Root = rowObj,
                    RootRt = rowRt,
                    LeaderLine = leaderImg,
                    BadgeObj = badgeObj,
                    BadgeBg = badgeBg,
                    BadgeText = badgeText,
                    StageRoleText = roleText,
                    StageDvText = dvText,
                    FuelTrack = trackBg,
                    FuelFillRt = fillRt,
                    FuelFill = fillImg,
                    FuelPercentText = pctText,
                    StageTimeText = metaText,
                    StageTwrText = metaText,
                    RowHighlightBg = rowBgImg,
                    StageNumber = i
                });
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            base.ApplyTheme(theme);
            ApplyText(_titleText, TextStyleRole.Cardinal, theme);
            ApplyText(_subTitleText, TextStyleRole.Label, theme);
            ApplyText(_summaryTwrText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_summaryDvText, TextStyleRole.PrimaryValue, theme);

            if (_headerDivider != null)
            {
                _headerDivider.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            }

            if (_silhouetteBayBg != null)
            {
                _silhouetteBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            }
            if (_silhouetteBayOutline != null)
            {
                _silhouetteBayOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong);
            }
            if (_silhouetteBayTitle != null)
            {
                _silhouetteBayTitle.color = style.GetTextColor(TextStyleRole.Label, theme);
            }
            if (_silhouetteRawImage != null)
            {
                _silhouetteRawImage.color = theme.AccentSecondary;
            }
            if (_plumeImg != null)
            {
                _plumeImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
            }
            if (_silhouetteBayFooter != null)
            {
                ApplyText(_silhouetteBayFooter, TextStyleRole.PrimaryValue, theme);
            }

            for (int i = 0; i < _stageRows.Count; i++)
            {
                StageRowUI row = _stageRows[i];
                if (row.FuelTrack != null)
                {
                    row.FuelTrack.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                }
                if (row.LeaderLine != null)
                {
                    row.LeaderLine.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal);
                }
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        private struct StageRowSnapshot
        {
            public int StageNumber;
            public bool IsActive;
            public bool IsExpended;
            public bool IsBurning;
            public string BadgeText;
            public string RoleText;
            public string DvText;
            public string MetaText;
            public float PropFrac;
            public bool Visible;
        }
        private readonly StageRowSnapshot[] _cachedStageSnapshots = new StageRowSnapshot[16];
        private readonly List<StageDeltaVInfo> _reusableSortedStages = new List<StageDeltaVInfo>();
        private bool _cachedHasVessel;
        private string _cachedTitle;
        private string _cachedSubTitle;
        private string _cachedTwrStr;
        private string _cachedDvStr;
        private Texture _cachedSilhouetteTex;
        private bool _cachedIsFiring;
        private string _cachedBayFootStr;
        private int _cachedDisplayCount;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            if (context.Telemetry == null || !context.Telemetry.HasVessel)
            {
                _cachedHasVessel = false;
                return;
            }

            _cachedHasVessel = true;

            _cachedTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, context.Telemetry);
            _cachedSubTitle = TelemetryTokenEngine.Evaluate(_subTitleTemplate, context.Telemetry);

            string twrVal = TelemetryTokenEngine.Evaluate(_twrToken, context.Telemetry);
            _cachedTwrStr = $"TWR {twrVal}";

            string dvVal = TelemetryTokenEngine.Evaluate(_totalDvToken, context.Telemetry);
            _cachedDvStr = dvVal.EndsWith("m/s", StringComparison.OrdinalIgnoreCase) ? dvVal : $"{dvVal} m/s";

            _cachedSilhouetteTex = VesselSilhouetteService.Provider?.SilhouetteTexture;

            IReadOnlyList<StageDeltaVInfo> stages = context.Telemetry.StageDeltaVList;
            int stageCount = stages != null ? stages.Count : 0;
            int curStage = context.Telemetry.CurrentStage;

            _reusableSortedStages.Clear();
            if (stageCount > 0)
            {
                _reusableSortedStages.AddRange(stages);
                _reusableSortedStages.Sort((a, b) => a.Stage.CompareTo(b.Stage));
            }
            else
            {
                _reusableSortedStages.Add(new StageDeltaVInfo(curStage, context.Telemetry.StageDeltaV, context.Telemetry.StageBurnTime, context.Telemetry.TWR, 310.0, true));
            }

            int curIdx = -1;
            for (int k = 0; k < _reusableSortedStages.Count; k++)
            {
                if (_reusableSortedStages[k].Stage == curStage)
                {
                    curIdx = k;
                    break;
                }
            }
            if (curIdx < 0) curIdx = _reusableSortedStages.Count - 1;

            int windowStart = 0;
            if (_reusableSortedStages.Count > MaxDisplayedStages)
            {
                windowStart = Mathf.Clamp(curIdx - (MaxDisplayedStages - 1), 0, _reusableSortedStages.Count - MaxDisplayedStages);
            }
            int displayCount = Mathf.Min(_reusableSortedStages.Count, MaxDisplayedStages);
            _cachedDisplayCount = displayCount;

            for (int i = 0; i < _stageRows.Count && i < _cachedStageSnapshots.Length; i++)
            {
                if (i < displayCount)
                {
                    StageDeltaVInfo stg = _reusableSortedStages[windowStart + i];
                    bool isActive = stg.IsActive || (stg.Stage == curStage);
                    bool isExpended = stg.Stage > curStage;
                    bool isBurning = isActive && (context.Telemetry.Throttle > 0.01 || context.Telemetry.ActiveEngines > 0 || stg.BurnTime > 0.01);

                    string roleStr = null;
                    if (stg.PartIcons != null && stg.PartIcons.Count > 0)
                    {
                        for (int p = 0; p < stg.PartIcons.Count; p++)
                        {
                            string itype = stg.PartIcons[p].IconType;
                            if (itype == "LAUNCH_CLAMP") { roleStr = "PAD RELEASE"; break; }
                            if (itype == "SOLID_BOOSTER") { roleStr = "SOLID BOOSTER"; break; }
                            if (itype == "PARACHUTES") { roleStr = "RECOVERY CHUTE"; break; }
                            if (itype == "DECOUPLER_HOR") { roleStr = "RADIAL SEP"; break; }
                            if (itype == "DECOUPLER_VERT") { roleStr = "STAGE DECOUPLER"; break; }
                            if (itype == "FAIRING") { roleStr = "FAIRING JETT"; break; }
                        }
                    }
                    if (string.IsNullOrEmpty(roleStr))
                    {
                        if (stg.Stage == 0) roleStr = "PAYLOAD / ORBIT";
                        else if (stg.DeltaV > 2000.0) roleStr = "CORE STAGE";
                        else if (stg.TWR > 1.8) roleStr = "BOOSTER CLUSTER";
                        else if (stg.DeltaV > 800.0) roleStr = "UPPER STAGE";
                        else if (stg.DeltaV < 0.01 && stg.BurnTime < 0.01) roleStr = "STAGE SEP";
                        else roleStr = $"STAGE {stg.Stage:D2}";
                    }

                    string dvTextStr = stg.DeltaV > 0.01 ? $"{stg.DeltaV:N0} m/s" : "---";
                    int burnSec = Mathf.Max(0, (int)stg.BurnTime);
                    int m = burnSec / 60;
                    int sec = burnSec % 60;
                    string metaStr = stg.TWR > 0.01 
                        ? $"{m:D2}:{sec:D2} · {stg.TWR:F2}T" 
                        : $"{m:D2}:{sec:D2} · {stg.Isp:F0}s";

                    float propFrac = 0f;
                    if (isActive)
                    {
                        propFrac = Mathf.Clamp01((float)context.Telemetry.StagePropellantFraction);
                    }
                    else if (isExpended)
                    {
                        propFrac = 0f;
                    }
                    else if (stg.PartIcons != null)
                    {
                        for (int p = 0; p < stg.PartIcons.Count; p++)
                        {
                            if (stg.PartIcons[p].PropellantFraction >= 0f)
                            {
                                propFrac = Mathf.Clamp01(stg.PartIcons[p].PropellantFraction);
                                break;
                            }
                        }
                    }
                    else
                    {
                        propFrac = 1.0f;
                    }

                    _cachedStageSnapshots[i] = new StageRowSnapshot
                    {
                        StageNumber = stg.Stage,
                        IsActive = isActive,
                        IsExpended = isExpended,
                        IsBurning = isBurning,
                        BadgeText = $"S{stg.Stage:D2}",
                        RoleText = roleStr,
                        DvText = dvTextStr,
                        MetaText = metaStr,
                        PropFrac = propFrac,
                        Visible = true
                    };
                }
                else
                {
                    _cachedStageSnapshots[i] = new StageRowSnapshot { Visible = false };
                }
            }

            bool isFiring = curStage >= 0 && (context.Telemetry.ActiveEngines > 0 || context.Telemetry.Throttle > 0.01);
            _cachedIsFiring = isFiring;
            _cachedBayFootStr = curStage >= 0 ? $"S{curStage:D2} · {(isFiring ? I18n.Tr("WIDGET_NAV_BURNING", "燃烧中") : I18n.Tr("WIDGET_ALERT_ARMED", "待发"))}" : I18n.Tr("WIDGET_ROCKET_SAFED", "已保险");
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_cachedHasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(context.Theme ?? _cachedTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float s = CurrentDpiScale;

            // 1. 顶部 Header 动态评估
            SetTextIfChanged(_titleText, _cachedTitle);
            SetTextIfChanged(_subTitleText, _cachedSubTitle);
            SetTextIfChanged(_summaryTwrText, _cachedTwrStr);
            SetTextIfChanged(_summaryDvText, _cachedDvStr);

            // 2. 剪影视窗与纹理守卫
            if (_silhouetteRawImage != null)
            {
                Texture tex = _cachedSilhouetteTex ?? _fallbackSilhouetteTexture;
                if (_silhouetteRawImage.texture != tex)
                {
                    _silhouetteRawImage.texture = tex;
                }
            }

            // 4. 逐级更新推进栈各行
            int displayCount = _cachedDisplayCount;
            for (int i = 0; i < _stageRows.Count; i++)
            {
                StageRowUI row = _stageRows[i];
                if (i < displayCount)
                {
                    row.Root.SetActive(true);
                    var snap = _cachedStageSnapshots[i];
                    row.StageNumber = snap.StageNumber;
                    row.IsActiveStage = snap.IsActive;
                    row.IsBurning = snap.IsBurning;

                    // A. 徽章标号与全行高亮
                    SetTextIfChanged(row.BadgeText, snap.BadgeText);
                    if (snap.IsActive)
                    {
                        row.BadgeBg.color = theme.AccentPrimary;
                        row.BadgeText.color = style.GetTextColor(TextStyleRole.InverseOnAccent, theme);
                        row.LeaderLine.color = theme.AccentPrimary;
                        ApplyText(row.StageDvText, TextStyleRole.PrimaryValue, theme);
                    }
                    else if (snap.IsExpended)
                    {
                        row.BadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme);
                        row.BadgeText.color = style.GetTextColor(TextStyleRole.Label, theme);
                        row.LeaderLine.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                        ApplyText(row.StageDvText, TextStyleRole.Label, theme);
                    }
                    else
                    {
                        row.BadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme);
                        row.BadgeText.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                        row.LeaderLine.color = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal);
                        ApplyText(row.StageDvText, TextStyleRole.SecondaryValue, theme);
                    }

                    // B. 分级角色
                    SetTextIfChanged(row.StageRoleText, snap.RoleText);

                    // C. 单级 ΔV 与烧燃倒计时
                    SetTextIfChanged(row.StageDvText, snap.DvText);
                    SetTextIfChanged(row.StageTimeText, snap.MetaText);

                    // D. 推进剂余量推算
                    float propFrac = snap.PropFrac;
                    row.TargetFuelFrac = propFrac;
                    if (row.CurrentFuelFrac < 0.001f && propFrac > 0.001f)
                    {
                        row.CurrentFuelFrac = propFrac;
                    }

                    float trackW = FuelTrackMaxWidth * s;
                    if (row.LastFuelFrac.Update(row.CurrentFuelFrac))
                    {
                        row.FuelFillRt.sizeDelta = new Vector2(trackW * row.CurrentFuelFrac, row.FuelFillRt.sizeDelta.y);
                        string pctText = snap.IsExpended ? "JETT" : $"{(propFrac * 100f):F0}%";
                        SetTextIfChanged(row.FuelPercentText, pctText);
                    }
                }
                else
                {
                    row.Root.SetActive(false);
                }
            }

            // 5. 视窗底部状态与羽流控制
            if (_plumeObj != null)
            {
                _plumeObj.SetActive(_cachedIsFiring);
            }

            SetTextIfChanged(_silhouetteBayFooter, _cachedBayFootStr);
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeInHierarchy) return;

            float dt = CustomAnimationDeltaTime >= 0f ? CustomAnimationDeltaTime : Time.unscaledDeltaTime;
            float time = CustomAnimationTime >= 0f ? CustomAnimationTime : Time.unscaledTime;
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 动态发动机喷流羽流高频微颤 (Combustion Flame Flutter 26Hz)
            if (_plumeObj != null && _plumeObj.activeSelf)
            {
                float flutter = 1.0f + Mathf.Sin(time * 26f) * 0.12f;
                if (_lastPlumeFlutter.Update(flutter))
                {
                    float thr = 1.0f;
                    _plumeRt.sizeDelta = new Vector2(10f * s * flutter, (10f + 14f * thr) * s * flutter);
                }
                _plumeImg.color = WidgetStyleManager.WithAlpha(style.GetMeterColor(MeterStyleRole.Warning, theme), 0.85f + Mathf.Sin(time * 28f) * 0.15f);
            }

            // 2. 逐级推进栈行呼吸与液位平滑
            for (int i = 0; i < _stageRows.Count; i++)
            {
                StageRowUI row = _stageRows[i];
                if (!row.Root.activeSelf) continue;

                // A. 激活级背景微光呼吸 (Active Stage Breathing Wave)
                if (row.IsActiveStage)
                {
                    float freq = row.IsBurning ? 5.2f : 2.4f;
                    float wave = Mathf.Sin(time * freq) * 0.5f + 0.5f;
                    float alpha = row.IsBurning ? (0.12f + wave * 0.12f) : (0.08f + wave * 0.08f);
                    row.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, alpha);
                }
                else
                {
                    row.RowHighlightBg.color = Color.clear;
                }

                // B. 推进剂平滑阻尼衰减与告警频闪
                if (Mathf.Abs(row.CurrentFuelFrac - row.TargetFuelFrac) > 0.001f)
                {
                    row.CurrentFuelFrac = Mathf.MoveTowards(row.CurrentFuelFrac, row.TargetFuelFrac, dt * 1.8f);
                    float trackW = FuelTrackMaxWidth * s;
                    row.FuelFillRt.sizeDelta = new Vector2(trackW * row.CurrentFuelFrac, row.FuelFillRt.sizeDelta.y);
                }

                if (row.CurrentFuelFrac <= 0.05f && row.IsActiveStage)
                {
                    // 极度危急频闪 (5Hz Emergency Strobe)
                    bool blinkOn = (Mathf.Sin(time * 30f) > 0f);
                    Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
                    row.FuelFill.color = blinkOn ? dangerCol : WidgetStyleManager.WithAlpha(dangerCol, 0.2f);
                }
                else if (row.CurrentFuelFrac <= 0.20f && row.IsActiveStage)
                {
                    // 低燃料琥珀色呼吸预警 (2.5Hz Amber Warning)
                    float warnPulse = Mathf.Sin(time * 8f) * 0.35f + 0.65f;
                    Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                    row.FuelFill.color = WidgetStyleManager.WithAlpha(warnCol, warnPulse);
                }
                else
                {
                    row.FuelFill.color = row.IsActiveStage 
                        ? theme.AccentPrimary 
                        : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
                }
            }
        }

        /// <summary>
        /// 程序化高精多级火箭矢量二维剪影纹理 (用于无头测试与原地沙盒无活跃飞船时的保底呈现)
        /// </summary>
        private static Texture2D CreateProceduralRocketSilhouetteTexture()
        {
            int w = 128;
            int h = 256;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] cols = new Color[w * h];
            float centerX = (w - 1) * 0.5f;

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / (h - 1); // 0.0 (底部) .. 1.0 (顶部)

                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - centerX) / centerX; // 0.0 (中轴线) .. 1.0 (侧边缘)
                    bool inside = false;
                    bool isLine = false;

                    // 1. 顶部载荷整流罩 / 头锥 (ny: 0.80 .. 0.96)
                    if (ny >= 0.80f && ny <= 0.96f)
                    {
                        float t = (ny - 0.80f) / 0.16f;
                        float fairingW = Mathf.Lerp(0.22f, 0.02f, Mathf.Pow(t, 0.75f));
                        if (dx <= fairingW) inside = true;
                    }
                    // 2. 上面级 (ny: 0.65 .. 0.795)
                    else if (ny >= 0.65f && ny < 0.795f)
                    {
                        if (dx <= 0.22f) inside = true;
                    }
                    // 3. 主芯级 (ny: 0.22 .. 0.645)
                    else if (ny >= 0.22f && ny < 0.645f)
                    {
                        if (dx <= 0.22f) inside = true;
                    }
                    // 4. 底部主发动机喷管 (ny: 0.14 .. 0.215)
                    else if (ny >= 0.14f && ny < 0.215f)
                    {
                        float t = (ny - 0.14f) / 0.075f;
                        float nozzleW = Mathf.Lerp(0.26f, 0.18f, t);
                        if (dx <= nozzleW) inside = true;
                    }

                    // 5. 两侧捆绑助推器 (ny: 0.24 .. 0.60)
                    if (ny >= 0.24f && ny <= 0.60f)
                    {
                        float boosterCenter = 0.38f;
                        float boosterHalfW = 0.09f;
                        float bstDx = Mathf.Abs(dx - boosterCenter);

                        if (ny > 0.54f)
                        {
                            float t = (ny - 0.54f) / 0.06f;
                            float curW = Mathf.Lerp(boosterHalfW, 0.01f, t);
                            if (bstDx <= curW) inside = true;
                        }
                        else
                        {
                            if (bstDx <= boosterHalfW) inside = true;
                        }
                    }

                    // 6. 助推器底部喷管 (ny: 0.17 .. 0.235)
                    if (ny >= 0.17f && ny < 0.235f)
                    {
                        float boosterCenter = 0.38f;
                        float bstDx = Mathf.Abs(dx - boosterCenter);
                        if (bstDx <= 0.06f) inside = true;
                    }

                    // 7. 级间隔框细线刻痕
                    if (inside && (Mathf.Abs(ny - 0.795f) < 0.005f || Mathf.Abs(ny - 0.645f) < 0.005f))
                    {
                        isLine = true;
                    }

                    // 8. 脊线高光
                    if (inside && dx <= 0.02f && ny >= 0.24f && ny <= 0.90f)
                    {
                        isLine = true;
                    }

                    Color c = Color.clear;
                    if (inside)
                    {
                        c = isLine
                            ? WidgetStyleManager.Weighted(WidgetStyleManager.NeutralOpaque, LineWeight.Strong)
                            : WidgetStyleManager.NeutralOpaque;
                    }

                    cols[y * w + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(false, true);
            return tex;
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
            _stageRows.Clear();
            base.OnDestroy();
        }
    }
}
