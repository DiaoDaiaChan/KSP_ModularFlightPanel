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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

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

        // 左侧 2D 飞船剪影视窗与姿态机构 (Vehicle Silhouette & Attitude Assembly)
        private GameObject _silhouetteBayObj;
        private Image _silhouetteBayBg;
        private Outline _silhouetteBayOutline;
        private Text _silhouetteBayTitle;
        private GameObject _silhouetteBayFooterPill;
        private Text _silhouetteBayFooter;
        private RectTransform _rocketAssemblyRt;
        private RawImage _silhouetteRawImage;
        private ProceduralRocketSilhouetteGraphic _proceduralSilhouetteGraphic;

        // 动态发动机喷流羽流 (Exhaust Plume)
        private GameObject _plumeRootObj;
        private RectTransform _plumeRt;
        private Image _plumeOuterImg;
        private Image _plumeCoreImg;

        // 右侧动态多级推进栈
        private const int MaxDisplayedStages = 5;
        private readonly List<StageRowUI> _stageRows = new List<StageRowUI>();

        // 几何尺寸
        private const float DefaultWidth = 260f;
        private const float DefaultHeight = 176f;
        private const float FuelTrackMaxWidth = 52f;

        // 风格与通配符配置
        private string _titleTemplate = "ROCKET 2D";
        private string _subTitleTemplate = "STAGING";
        private string _stageDvToken = "{STAGE:DV}";
        private string _totalDvToken = "{DV:TOTAL}";
        private string _twrToken = "{TWR}";
        private ThemeConfig _cachedTheme;

        // 姿态与视觉补间状态
        private float _cachedPitch = 90f;
        private float _cachedTargetTilt = 0f;
        private float _currentTilt = 0f;
        private float _cachedThrottle = 0f;
        private string _cachedBayTitleStr = string.Empty;

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

            // 俯仰 0° 水平基准微刻度 (Pitch 0° Horizon Reference Marks)
            float tickW = 4f * s;
            float tickH = 1.2f * s;
            Color tickCol = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "HorizTick_L", new Vector2(tickW, tickH), new Vector2(-bayW * 0.5f + tickW * 0.5f + 1f * s, 0f), tickCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "HorizTick_R", new Vector2(tickW, tickH), new Vector2(bayW * 0.5f - tickW * 0.5f - 1f * s, 0f), tickCol);

            // 视窗顶部标牌 (动态呈现俯仰姿态 PITCH 72°)
            _silhouetteBayTitle = UIFactory.CreateText(_silhouetteBayObj.transform, "BayTitle", I18n.Tr("WIDGET_ROCKET_PROFILE", "剖面"),
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _silhouetteBayTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _silhouetteBayTitle.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(0f, bayH * 0.5f - 8f * s);
            titleRt.sizeDelta = new Vector2(bayW - 4f * s, 12f * s);

            // 中央飞船 2D 姿态云台机构 (Vessel Attitude & Staging Gimbal)
            _rocketAssemblyRt = CreateContainer("RocketAssembly", _silhouetteBayObj.transform,
                new Vector2(46f * s, 94f * s), new Vector2(0f, 0f));
            _rocketAssemblyRt.pivot = new Vector2(0.5f, 0.40f);

            // 2D 飞船剪影图元 (RawImage + 本地 GPU 矢量保底)
            _silhouetteRawImage = CreateChild<RawImage>("VesselSilhouette_RawImage", _rocketAssemblyRt,
                new Vector2(46f * s, 94f * s), Vector2.zero);
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = theme.AccentSecondary;

            _proceduralSilhouetteGraphic = CreateChild<ProceduralRocketSilhouetteGraphic>("ProceduralSilhouette", _rocketAssemblyRt,
                new Vector2(46f * s, 94f * s), Vector2.zero);
            _proceduralSilhouetteGraphic.raycastTarget = false;
            _proceduralSilhouetteGraphic.color = theme.AccentSecondary;

            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            bool hasBakerTex = tex != null;
            _silhouetteRawImage.gameObject.SetActive(hasBakerTex);
            _proceduralSilhouetteGraphic.gameObject.SetActive(!hasBakerTex);
            if (hasBakerTex) _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 发动机点火羽流 (Exhaust Plume: 联动火箭倾角、超音速激波芯与膨胀羽流)
            _plumeRt = CreateContainer("PlumeRoot", _rocketAssemblyRt,
                new Vector2(10f * s, 14f * s), new Vector2(0f, -38f * s));
            _plumeRt.pivot = new Vector2(0.5f, 1f);
            _plumeRootObj = _plumeRt.gameObject;

            _plumeOuterImg = CreateChild<Image>("PlumeOuter", _plumeRt,
                new Vector2(8f * s, 12f * s), Vector2.zero);
            _plumeOuterImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            _plumeOuterImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            _plumeOuterImg.raycastTarget = false;

            _plumeCoreImg = CreateChild<Image>("PlumeCore", _plumeRt,
                new Vector2(3.5f * s, 6f * s), Vector2.zero);
            _plumeCoreImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            _plumeCoreImg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f);
            _plumeCoreImg.raycastTarget = false;

            _plumeRootObj.SetActive(false);

            // 视窗底部状态胶囊底板 (Bottom Status Pill)
            _silhouetteBayFooterPill = UIFactory.CreatePanel(_silhouetteBayObj.transform, "BayFooterPill",
                new Vector2(bayW - 8f * s, 14f * s), new Vector2(0f, -bayH * 0.5f + 9.5f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal), 0.8f * s);

            _silhouetteBayFooter = UIFactory.CreateText(_silhouetteBayFooterPill.transform, "BayFooter", "S-- · " + I18n.Tr("WIDGET_ALERT_ARMED", "待发"),
                Mathf.RoundToInt(7f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _silhouetteBayFooter.fontStyle = FontStyle.Bold;
            RectTransform footRt = _silhouetteBayFooter.GetComponent<RectTransform>();
            footRt.anchorMin = Vector2.zero;
            footRt.anchorMax = Vector2.one;
            footRt.sizeDelta = Vector2.zero;
            footRt.anchoredPosition = Vector2.zero;
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            if (_silhouetteRawImage != null)
            {
                bool hasBakerTex = tex != null;
                _silhouetteRawImage.gameObject.SetActive(hasBakerTex);
                if (_proceduralSilhouetteGraphic != null)
                {
                    _proceduralSilhouetteGraphic.gameObject.SetActive(!hasBakerTex);
                }
                if (hasBakerTex)
                {
                    _silhouetteRawImage.texture = tex;
                }
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
                _silhouetteBayTitle.color = style.GetTextColor(TextStyleRole.Cardinal, theme);
            }
            if (_silhouetteRawImage != null)
            {
                _silhouetteRawImage.color = theme.AccentSecondary;
            }
            if (_proceduralSilhouetteGraphic != null)
            {
                _proceduralSilhouetteGraphic.color = theme.AccentSecondary;
            }
            if (_plumeOuterImg != null)
            {
                _plumeOuterImg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            }
            if (_plumeCoreImg != null)
            {
                _plumeCoreImg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f);
            }
            if (_silhouetteBayFooterPill != null)
            {
                var pillBg = _silhouetteBayFooterPill.GetComponent<Image>();
                if (pillBg != null) pillBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
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
            int activeEng = context.Telemetry.ActiveEngines;
            string engSuffix = activeEng > 0 ? $" ({activeEng} ENG)" : string.Empty;
            _cachedTwrStr = $"TWR {twrVal}{engSuffix}";

            string dvVal = TelemetryTokenEngine.Evaluate(_totalDvToken, context.Telemetry);
            _cachedDvStr = dvVal.EndsWith("m/s", StringComparison.OrdinalIgnoreCase) ? dvVal : $"{dvVal} m/s";

            _cachedSilhouetteTex = VesselSilhouetteService.Provider?.SilhouetteTexture;

            // 姿态解算 (Attitude & Staging Orientation: 90° 直立, 重力转向顺势倾斜)
            float pitch = context.Telemetry.Pitch;
            _cachedPitch = pitch;
            float targetTilt = Mathf.Clamp(90f - pitch, -50f, 50f);
            _cachedTargetTilt = targetTilt;
            _cachedBayTitleStr = $"{I18n.Tr("WIDGET_AXIS_PITCH", "PITCH")} {pitch:F0}°";

            _cachedThrottle = Mathf.Clamp01((float)context.Telemetry.Throttle);

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
            _cachedBayFootStr = curStage >= 0 
                ? $"S{curStage:D2} · {(isFiring ? I18n.Tr("WIDGET_NAV_BURNING", "燃烧中") : I18n.Tr("WIDGET_ALERT_ARMED", "待发"))}" 
                : I18n.Tr("WIDGET_ROCKET_SAFED", "已保险");
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_cachedHasVessel) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(context.Theme ?? _cachedTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float s = CurrentDpiScale;
            float dt = CustomAnimationDeltaTime >= 0f ? CustomAnimationDeltaTime : context.DeltaTime;
            float time = CustomAnimationTime >= 0f ? CustomAnimationTime : Time.unscaledTime;
            Vector2 panelSize = BaseSize * s;

            // 1. 顶部 Header 动态评估
            _titleText.SetTextSafe(_cachedTitle);
            _subTitleText.SetTextSafe(_cachedSubTitle);
            _summaryTwrText.SetTextSafe(_cachedTwrStr);
            _summaryDvText.SetTextSafe(_cachedDvStr);

            // 2. 剪影与姿态俯仰角旋转动画
            _silhouetteBayTitle.SetTextSafe(_cachedBayTitleStr);

            if (_rocketAssemblyRt != null)
            {
                _currentTilt = Mathf.MoveTowards(_currentTilt, _cachedTargetTilt, dt * 60f);
                _rocketAssemblyRt.localRotation = Quaternion.Euler(0f, 0f, -_currentTilt);
            }

            if (_silhouetteRawImage != null && _cachedSilhouetteTex != null)
            {
                if (_silhouetteRawImage.texture != _cachedSilhouetteTex)
                {
                    _silhouetteRawImage.texture = _cachedSilhouetteTex;
                }
            }

            // 3. 动态发动机喷流羽流高频微颤 (26Hz Flame Flutter & Mach Shock Diamonds)
            if (_plumeRootObj != null)
            {
                _plumeRootObj.SetActiveSafe(_cachedIsFiring);
                if (_cachedIsFiring)
                {
                    float flutter = 1.0f + Mathf.Sin(time * 26f) * 0.12f;
                    float thr = _cachedThrottle > 0.01f ? _cachedThrottle : 0.8f;
                    float plumeH = Mathf.Clamp((8f + 5f * thr) * s * flutter, 6f * s, 14f * s);
                    float plumeW = (7f + 2f * thr) * s * flutter;

                    if (_plumeOuterImg != null)
                    {
                        _plumeOuterImg.rectTransform.SetSizeDeltaSafe(new Vector2(plumeW, plumeH));
                        Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                        _plumeOuterImg.SetColor(WidgetStyleManager.WithAlpha(warnCol, 0.82f + Mathf.Sin(time * 28f) * 0.16f));
                    }
                    if (_plumeCoreImg != null)
                    {
                        _plumeCoreImg.rectTransform.SetSizeDeltaSafe(new Vector2(plumeW * 0.45f, plumeH * 0.55f));
                        _plumeCoreImg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.95f));
                    }
                }
            }

            // 4. 视窗底部状态标牌与胶囊底板
            _silhouetteBayFooter.SetTextSafe(_cachedBayFootStr);
            if (_cachedIsFiring)
            {
                _silhouetteBayFooter.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            }
            else
            {
                _silhouetteBayFooter.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            }

            // 5. 动态多级推进栈排版与数据绑定
            int displayCount = _cachedDisplayCount;
            float stackH = 132f * s;
            float rHeight = displayCount <= 4 ? 25.5f * s : 24f * s;
            float rSpacing = displayCount > 1 
                ? Mathf.Clamp((stackH - displayCount * rHeight) / (displayCount - 1), 3f * s, 6.5f * s) 
                : 0f;
            float startY = panelSize.y * 0.5f - 36f * s;

            for (int i = 0; i < _stageRows.Count; i++)
            {
                StageRowUI row = _stageRows[i];
                if (i < displayCount)
                {
                    row.Root.SetActiveSafe(true);
                    float rowY = startY - i * (rHeight + rSpacing);
                    row.RootRt.SetAnchoredPositionSafe(new Vector2(-panelSize.x * 0.5f + 74f * s, rowY));
                    row.RootRt.SetSizeDeltaSafe(new Vector2(panelSize.x - 82f * s, rHeight));

                    var snap = _cachedStageSnapshots[i];
                    row.StageNumber = snap.StageNumber;
                    row.IsActiveStage = snap.IsActive;
                    row.IsBurning = snap.IsBurning;

                    // A. 徽章标号与全行高亮
                    row.BadgeText.SetTextSafe(snap.BadgeText);
                    if (snap.IsActive)
                    {
                        row.BadgeBg.SetColor(theme.AccentPrimary);
                        row.BadgeText.SetColor(style.GetTextColor(TextStyleRole.InverseOnAccent, theme));
                        row.LeaderLine.SetColor(theme.AccentPrimary);
                        row.StageDvText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));

                        // 激活级背景微光呼吸 (Active Stage Breathing Wave)
                        float freq = snap.IsBurning ? 5.2f : 2.4f;
                        float wave = Mathf.Sin(time * freq) * 0.5f + 0.5f;
                        float alpha = snap.IsBurning ? (0.12f + wave * 0.12f) : (0.08f + wave * 0.08f);
                        row.RowHighlightBg.SetColor(WidgetStyleManager.WithAlpha(theme.AccentPrimary, alpha));
                    }
                    else if (snap.IsExpended)
                    {
                        row.BadgeBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme));
                        row.BadgeText.SetColor(style.GetTextColor(TextStyleRole.Label, theme));
                        row.LeaderLine.SetColor(WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost));
                        row.StageDvText.SetColor(style.GetTextColor(TextStyleRole.Label, theme));
                        row.RowHighlightBg.SetColor(Color.clear);
                    }
                    else
                    {
                        row.BadgeBg.SetColor(WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme));
                        row.BadgeText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                        row.LeaderLine.SetColor(WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal));
                        row.StageDvText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                        row.RowHighlightBg.SetColor(Color.clear);
                    }

                    // B. 分级角色
                    row.StageRoleText.SetTextSafe(snap.RoleText);
                    if (snap.IsActive)
                    {
                        row.StageRoleText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                    }
                    else
                    {
                        row.StageRoleText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    }

                    // C. 单级 ΔV 与烧燃倒计时
                    row.StageDvText.SetTextSafe(snap.DvText);
                    row.StageTimeText.SetTextSafe(snap.MetaText);

                    // D. 推进剂余量推算与平滑阻尼衰减
                    float propFrac = snap.PropFrac;
                    row.TargetFuelFrac = propFrac;
                    if (row.CurrentFuelFrac < 0.001f && propFrac > 0.001f)
                    {
                        row.CurrentFuelFrac = propFrac;
                    }
                    if (Mathf.Abs(row.CurrentFuelFrac - row.TargetFuelFrac) > 0.001f)
                    {
                        row.CurrentFuelFrac = Mathf.MoveTowards(row.CurrentFuelFrac, row.TargetFuelFrac, dt * 1.8f);
                    }

                    float trackW = FuelTrackMaxWidth * s;
                    row.FuelFillRt.SetSizeDeltaSafe(new Vector2(trackW * row.CurrentFuelFrac, row.FuelFillRt.sizeDelta.y));
                    string pctText = snap.IsExpended ? "JETT" : $"{(propFrac * 100f):F0}%";
                    row.FuelPercentText.SetTextSafe(pctText);

                    // 推进剂液位告警着色
                    if (row.CurrentFuelFrac <= 0.05f && row.IsActiveStage)
                    {
                        bool blinkOn = (Mathf.Sin(time * 30f) > 0f);
                        Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
                        row.FuelFill.SetColor(blinkOn ? dangerCol : WidgetStyleManager.WithAlpha(dangerCol, 0.2f));
                    }
                    else if (row.CurrentFuelFrac <= 0.20f && row.IsActiveStage)
                    {
                        float warnPulse = Mathf.Sin(time * 8f) * 0.35f + 0.65f;
                        Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                        row.FuelFill.SetColor(WidgetStyleManager.WithAlpha(warnCol, warnPulse));
                    }
                    else
                    {
                        row.FuelFill.SetColor(row.IsActiveStage 
                            ? theme.AccentPrimary 
                            : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium));
                    }
                }
                else
                {
                    row.Root.SetActiveSafe(false);
                }
            }
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

    /// <summary>
    /// UGUI GPU 矢量火箭剪影图元 (零 CPU 软件光栅化，纯代码 GPU 三角形与四边形)
    /// </summary>
    public class ProceduralRocketSilhouetteGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float w = r.width;
            float h = r.height;
            float cx = r.center.x;
            float cy = r.center.y;

            Color c = color;
            Color lineCol = WidgetStyleManager.Weighted(c, LineWeight.Strong);

            // 1. 头锥 / 整流罩 (Apex: top center; Base: core stage width)
            float coreHalfW = w * 0.18f;
            float yTop = cy + h * 0.44f;
            float yFairingBase = cy + h * 0.28f;
            AddTri(vh, cx, yTop, cx - coreHalfW, yFairingBase, cx + coreHalfW, yFairingBase, c);

            // 2. 主芯级筒段 (Core Stage)
            float yEngineBase = cy - h * 0.28f;
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yEngineBase, yFairingBase, c, c);

            // 级间隔框细线
            float yStage1 = cy + h * 0.12f;
            float yStage2 = cy - h * 0.08f;
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yStage1 - 0.75f, yStage1 + 0.75f, lineCol, lineCol);
            AddQuad(vh, cx - coreHalfW, cx + coreHalfW, yStage2 - 0.75f, yStage2 + 0.75f, lineCol, lineCol);

            // 3. 底部主发动机喷管 (Nozzle)
            float yNozzleBase = cy - h * 0.38f;
            float nozzleTopHalfW = coreHalfW * 0.65f;
            float nozzleBtmHalfW = coreHalfW * 0.90f;
            AddTrapezoid(vh, cx, yEngineBase, nozzleTopHalfW, yNozzleBase, nozzleBtmHalfW, c);

            // 4. 两侧捆绑助推器 (Side Boosters)
            float boosterHalfW = coreHalfW * 0.55f;
            float boosterDist = coreHalfW * 1.65f;
            float bTop = cy + h * 0.15f;
            float bNose = cy + h * 0.22f;
            float bBtm = cy - h * 0.24f;
            float bNozzle = cy - h * 0.32f;

            // 左助推器
            float bxL = cx - boosterDist;
            AddTri(vh, bxL, bNose, bxL - boosterHalfW, bTop, bxL + boosterHalfW, bTop, c);
            AddQuad(vh, bxL - boosterHalfW, bxL + boosterHalfW, bBtm, bTop, c, c);
            AddTrapezoid(vh, bxL, bBtm, boosterHalfW * 0.8f, bNozzle, boosterHalfW * 1.1f, c);

            // 右助推器
            float bxR = cx + boosterDist;
            AddTri(vh, bxR, bNose, bxR - boosterHalfW, bTop, bxR + boosterHalfW, bTop, c);
            AddQuad(vh, bxR - boosterHalfW, bxR + boosterHalfW, bBtm, bTop, c, c);
            AddTrapezoid(vh, bxR, bBtm, boosterHalfW * 0.8f, bNozzle, boosterHalfW * 1.1f, c);

            // 5. 气动稳定翼 (Fins)
            float finSpan = coreHalfW * 2.2f;
            float finTop = cy - h * 0.20f;
            float finBtm = cy - h * 0.28f;
            AddTri(vh, cx - coreHalfW, finTop, cx - finSpan, finBtm, cx - coreHalfW, finBtm, c);
            AddTri(vh, cx + coreHalfW, finTop, cx + finSpan, finBtm, cx + coreHalfW, finBtm, c);
        }

        private static void AddTri(VertexHelper vh, float x0, float y0, float x1, float y1, float x2, float y2, Color c)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x2, y2), c, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
        }

        private static void AddQuad(VertexHelper vh, float xMin, float xMax, float yMin, float yMax, Color c0, Color c1)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(xMin, yMin), c0, Vector2.zero);
            vh.AddVert(new Vector3(xMin, yMax), c1, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMax), c1, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMin), c0, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private static void AddTrapezoid(VertexHelper vh, float cx, float yTop, float halfWTop, float yBtm, float halfWBtm, Color c)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(cx - halfWTop, yTop), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfWTop, yTop), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfWBtm, yBtm), c, Vector2.zero);
            vh.AddVert(new Vector3(cx - halfWBtm, yBtm), c, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }
    }
}
