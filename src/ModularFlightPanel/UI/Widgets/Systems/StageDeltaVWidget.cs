using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化多级火箭 ΔV 与二维剪影时序仪 (Avionics Multi-Stage Delta-V & Vessel Silhouette Tape)
    /// 
    /// 核心特性：
    /// 1. 深度联动 VesselSilhouetteBaker (IVesselSilhouetteProvider)：
    ///    飞行场景下直接呈现真实载具实时烘焙的 2D 正交俯视/侧视轮廓，分级与分离时以 15 FPS 动态展现助推器/整流罩脱落飞离；
    ///    无头测试/原地沙盒模式下自适应切换至高精程序化矢量火箭剪影保底。
    /// 2. 航电多级速度条堆叠 (Speed-Bar Tape Stack)：
    ///    识别 MechJeb (Tier 1) / 原版 VesselDeltaV (Tier 2) / 物理仿真 (Tier 3)，
    ///    展现各级可用 Δv 相对占比、单级读数、精准工作秒数倒计时 (⏱ mm:ss) 与活跃级高亮游标。
    /// 3. 左侧车载剖面瞄准框 (Vehicle Profile Bay)：
    ///    集成 2D 飞船剪影、动态矢量喷流羽流 (Exhaust Plume) 与跨分级水平激光引线。
    /// 4. 严格继承 BaseFlightWidget，所有样式、数据全生命周期数据驱动，零硬编码。
    /// </summary>
    /// <summary>
    /// 航电多级速度条单级槽位状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct StageDeltaVRowSnapshot : IEquatable<StageDeltaVRowSnapshot>
    {
        public int Stage;
        public bool IsActive;
        public double DeltaV;
        public double BurnTime;
        public float Ratio;
        public bool Visible;

        public bool Equals(StageDeltaVRowSnapshot other)
        {
            return Stage == other.Stage &&
                   IsActive == other.IsActive &&
                   Math.Abs(DeltaV - other.DeltaV) < 0.1 &&
                   Math.Abs(BurnTime - other.BurnTime) < 0.1 &&
                   Math.Abs(Ratio - other.Ratio) < 0.005f &&
                   Visible == other.Visible;
        }

        public override bool Equals(object obj) => obj is StageDeltaVRowSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (IsActive ? 1 : 0);
                hash = (hash * 397) ^ (Visible ? 1 : 0);
                hash = (hash * 397) ^ Stage;
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电多级速度条与二维剪影状态快照 (0 GC 纯值类型)
    /// </summary>
    public struct StageDeltaVState : IEquatable<StageDeltaVState>
    {
        public bool HasVessel;
        public string Source;
        public string TotalDvText;
        public string TotalTimeText;
        public bool HasActive;
        public int ActiveStage;
        public double ActiveDeltaV;
        public double ActiveBurnTime;
        public bool IsFiring;
        public float Throttle;
        public string BayFooterStr;
        public string FooterStatusStr;
        public TextStyleRole FooterRole;
        public int StageCount;

        public StageDeltaVRowSnapshot Row0;
        public StageDeltaVRowSnapshot Row1;
        public StageDeltaVRowSnapshot Row2;
        public StageDeltaVRowSnapshot Row3;
        public StageDeltaVRowSnapshot Row4;

        public StageDeltaVRowSnapshot GetRow(int index)
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

        public void SetRow(int index, in StageDeltaVRowSnapshot row)
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

        public bool Equals(StageDeltaVState other)
        {
            return HasVessel == other.HasVessel &&
                   Source == other.Source &&
                   TotalDvText == other.TotalDvText &&
                   TotalTimeText == other.TotalTimeText &&
                   HasActive == other.HasActive &&
                   ActiveStage == other.ActiveStage &&
                   Math.Abs(ActiveDeltaV - other.ActiveDeltaV) < 0.1 &&
                   Math.Abs(ActiveBurnTime - other.ActiveBurnTime) < 0.1 &&
                   IsFiring == other.IsFiring &&
                   Math.Abs(Throttle - other.Throttle) < 0.01f &&
                   BayFooterStr == other.BayFooterStr &&
                   FooterStatusStr == other.FooterStatusStr &&
                   FooterRole == other.FooterRole &&
                   StageCount == other.StageCount &&
                   Row0.Equals(other.Row0) &&
                   Row1.Equals(other.Row1) &&
                   Row2.Equals(other.Row2) &&
                   Row3.Equals(other.Row3) &&
                   Row4.Equals(other.Row4);
        }

        public override bool Equals(object obj) => obj is StageDeltaVState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (HasVessel ? 1 : 0);
                hash = (hash * 397) ^ (HasActive ? 1 : 0);
                hash = (hash * 397) ^ (IsFiring ? 1 : 0);
                hash = (hash * 397) ^ (Source != null ? Source.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 航电多级速度条与火箭分级遥测纯业务逻辑大脑 (0 GC / 100% 游戏引擎解耦)
    /// </summary>
    public class StageDeltaVLogic : WidgetLogic<StageDeltaVState>
    {
        public const int MaxDisplayedStages = 5;
        private readonly List<StageDeltaVInfo> _reusableStageList = new List<StageDeltaVInfo>();

        public string TotalDvTemplate { get; set; } = "{DV:TOTAL}";
        public string TotalTimeTemplate { get; set; } = "Σ {DV:TOTALTIME}";

        public override void Reset()
        {
            CurrentState = default;
            _reusableStageList.Clear();
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = new StageDeltaVState
                {
                    HasVessel = false,
                    FooterStatusStr = I18n.Tr("WIDGET_DV_NO_TELEMETRY", "无遥测链路"),
                    FooterRole = TextStyleRole.Warning
                };
                return;
            }

            string src = string.IsNullOrEmpty(telemetry.DeltaVSource) ? "SIM" : telemetry.DeltaVSource.ToUpperInvariant();

            string totalDvStr = TelemetryTokenEngine.Evaluate(TotalDvTemplate, telemetry);
            string totalTimeStr = TelemetryTokenEngine.Evaluate(TotalTimeTemplate, telemetry);

            IReadOnlyList<StageDeltaVInfo> stages = telemetry.StageDeltaVList;
            _reusableStageList.Clear();
            if (stages == null || stages.Count == 0)
            {
                _reusableStageList.Add(new StageDeltaVInfo(telemetry.CurrentStage, telemetry.StageDeltaV, telemetry.StageBurnTime, telemetry.TWR, 310.0, true));
            }
            else
            {
                _reusableStageList.AddRange(stages);
            }

            int stageCount = _reusableStageList.Count;

            double maxStageDv = 500.0;
            for (int i = 0; i < stageCount; i++)
            {
                if (_reusableStageList[i].DeltaV > maxStageDv) maxStageDv = _reusableStageList[i].DeltaV;
            }

            StageDeltaVInfo activeStageInfo = default;
            bool hasActive = false;

            StageDeltaVState newState = default;
            newState.HasVessel = true;
            newState.Source = src;
            newState.TotalDvText = totalDvStr;
            newState.TotalTimeText = totalTimeStr;
            newState.StageCount = stageCount;

            for (int i = 0; i < MaxDisplayedStages; i++)
            {
                if (i < stageCount)
                {
                    StageDeltaVInfo info = _reusableStageList[i];
                    if (info.IsActive)
                    {
                        activeStageInfo = info;
                        hasActive = true;
                    }

                    float ratio = Mathf.Clamp01((float)(info.DeltaV / maxStageDv));
                    newState.SetRow(i, new StageDeltaVRowSnapshot
                    {
                        Stage = info.Stage,
                        IsActive = info.IsActive,
                        DeltaV = info.DeltaV,
                        BurnTime = info.BurnTime,
                        Ratio = ratio,
                        Visible = true
                    });
                }
                else
                {
                    newState.SetRow(i, new StageDeltaVRowSnapshot { Visible = false });
                }
            }

            newState.HasActive = hasActive;
            if (hasActive)
            {
                newState.ActiveStage = activeStageInfo.Stage;
                newState.ActiveDeltaV = activeStageInfo.DeltaV;
                newState.ActiveBurnTime = activeStageInfo.BurnTime;
            }

            bool isFiring = hasActive && (telemetry.ActiveEngines > 0 || telemetry.Throttle > 0.01f);
            newState.IsFiring = isFiring;
            newState.Throttle = Mathf.Clamp((float)telemetry.Throttle, 0.25f, 1.0f);

            newState.BayFooterStr = hasActive
                ? I18n.TrFormat("WIDGET_DV_STAGE_ACTV_FORMAT", activeStageInfo.Stage)
                : I18n.Tr("WIDGET_DV_STAGING_ARMED", "分级待发");

            if (hasActive)
            {
                newState.FooterStatusStr = I18n.TrFormat("WIDGET_DV_FOOT_ACTIVE", "当前 S{0:D2}: {1:N0} m/s | ⏱ {2}", activeStageInfo.Stage, activeStageInfo.DeltaV, AvionicsFormatting.FormatDuration(activeStageInfo.BurnTime));
                newState.FooterRole = TextStyleRole.Accent;
            }
            else if (stageCount > 0)
            {
                newState.FooterStatusStr = I18n.TrFormat("WIDGET_DV_FOOT_ALL_ARMED", "共 {0} 级就绪 | Σ {1:N0} m/s", stageCount, telemetry.TotalDeltaV);
                newState.FooterRole = TextStyleRole.SecondaryValue;
            }
            else
            {
                newState.FooterStatusStr = I18n.Tr("WIDGET_DV_NO_STAGE_DATA", "无分级数据");
                newState.FooterRole = TextStyleRole.Warning;
            }

            CurrentState = newState;
        }
    }

    [FlightWidget("stage_dv", "deltav", "stage_delta_v", Category = WidgetCategory.Systems, DisplayName = "STAGE ΔV 本级推演仪表", Description = "当前级与总计 Delta-V 动态量程柱状图、燃烧耗尽倒计时与比冲 (Isp)。", DefaultWidgetId = "custom.stage_dv", DefaultX = -440f, DefaultY = 60f, IsSingleton = true, ExactIds = new[] { "gauge.stage_dv", "custom.stage_dv", "core.stage_dv" })]
    public class StageDeltaVWidget : BaseFlightWidget
    {
        protected override IWidgetLogic LogicCore => _logic;
        private readonly StageDeltaVLogic _logic = new StageDeltaVLogic();

        private class StageRowUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image LeaderLine;
            public Image StageBadgeBg;
            public Text StageBadgeText;
            public RectTransform TrackRt;
            public Image TrackBg;
            public RectTransform FillBarRt;
            public Image FillBarImg;
            public RectTransform CaretRt;
            public Image CaretImg;
            public Text StageDvText;
            public Text StageTimeText;
            public readonly Cached<int> LastStage = new Cached<int>(-1);
            public readonly CachedDouble LastDv = new CachedDouble(-1.0, tolerance: 0.5);
            public readonly CachedFloat LastBarW = new CachedFloat(-1f, tolerance: 0.5f);
            public readonly Cached<string> LastTimeText = new Cached<string>(string.Empty);
        }

        private Image _panelBg;
        private Outline _panelOutline;
        private Image _topStripe;

        // 顶栏总览
        private Text _titleText;
        private Image _sourceBadgeBg;
        private Outline _sourceBadgeOutline;
        private Text _sourceBadgeText;
        private Text _totalDvValue;
        private Text _totalTimeValue;

        // 左侧 2D 飞船剪影视窗 (Vessel Silhouette Bay)
        private GameObject _silhouetteBayObj;
        private Image _silhouetteBayBg;
        private Outline _silhouetteBayOutline;
        private Text _silhouetteBayTitle;
        private Text _silhouetteBayFooter;
        private RawImage _silhouetteRawImage;
        private ProceduralRocketSilhouetteGraphic _proceduralSilhouetteGraphic;

        // 动态发动机喷管与喷射羽流
        private GameObject _plumeObj;
        private RectTransform _plumeRt;
        private Image _plumeImg;

        // 右侧动态分级列表行 (预分配 5 级)
        private const int MaxDisplayedStages = 5;
        private readonly List<StageRowUI> _stageRows = new List<StageRowUI>();

        // 底栏摘要与安全状态
        private Image _footerSeparator;
        private Text _footerStatusText;
        private ThemeConfig _currentTheme;

        private readonly CachedFloat _cachedScale = new CachedFloat(1.0f);
        private const float DefaultPanelWidth = 254f;
        private const float DefaultPanelHeight = 186f;
        private const float TrackWidth = 54f;

        public override Vector2 BaseSize => new Vector2(DefaultPanelWidth, DefaultPanelHeight);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            _cachedScale.Value = s;

            Vector2 panelSize = BaseSize * s;
            _panelBg = CardBackground;
            _panelOutline = CardOutline;

            Color primaryAccent = theme.AccentPrimary;

            // 2. 顶部微光警示饰条 (Accent Header Stripe)
            _topStripe = UIFactory.CreatePanel(transform, "TopAccentStripe", new Vector2(panelSize.x, 3f * s),
                new Vector2(0f, panelSize.y * 0.5f - 1.5f * s), primaryAccent).GetComponent<Image>();

            // 3. 顶栏：标题、数据源标识、总 ΔV、总烧燃时序
            BuildHeader(transform, panelSize, s, theme);

            // 4. 左侧 2D 飞船剪影视窗 (Vessel Silhouette Bay)
            BuildSilhouetteBay(transform, panelSize, s, theme);

            // 5. 右侧分级速度条带状栈 (Stage Tape Rows)
            BuildStageRows(transform, panelSize, s, theme);

            // 6. 底栏：活跃级摘要
            BuildFooter(transform, panelSize, s, theme);

            // 注册微控件至标准化管理器
            if (_topStripe != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "top_stripe", "Top Stripe", _topStripe.gameObject, "顶部微光警示饰条", t => { if (_topStripe != null) _topStripe.color = t.AccentPrimary; }));
            }
            this.Controls.Register(new WidgetHeaderControl(_titleText, _sourceBadgeText, "Header", "顶部标题与数据源标牌"));
            this.Controls.Register(new WidgetReadoutControl("total_deltav", "总可用速度增量标牌", _totalDvValue != null ? _totalDvValue.gameObject : null, _totalDvValue, null, TextStyleRole.ValueLarge, "{DV:TOTAL}"));
            this.Controls.Register(new WidgetReadoutControl("total_burn_time", "总工作烧燃时序", _totalTimeValue != null ? _totalTimeValue.gameObject : null, _totalTimeValue, null, TextStyleRole.ValueSmall, "{DV:TOTALTIME}"));
            if (_silhouetteBayObj != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "silhouette_bay", "Silhouette Bay", _silhouetteBayObj, "飞船剪影轮廓视窗"));
            }
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildHeader(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            Color primaryAccent = theme.AccentPrimary;
            Color textPrimary = theme.TextPrimaryColor;

            float halfW = panelSize.x * 0.5f;
            float topY = panelSize.y * 0.5f - 16f * s;

            // 标题 (左对齐)
            _titleText = UIFactory.CreateText(parent, "StageDvTitle", I18n.Tr("WIDGET_DV_TITLE", "级 ΔV"), Mathf.RoundToInt(11f * s),
                TextAnchor.MiddleLeft, textPrimary);
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(-halfW + 10f * s, topY);
            titleRt.sizeDelta = new Vector2(68f * s, 18f * s);

            // 遥测源标识 ([MJ] / [STK] / [SIM])
            GameObject srcObj = UIFactory.CreatePanel(parent, "SourceBadge", new Vector2(25f * s, 14f * s),
                Vector2.zero, WidgetStyleManager.Weighted(primaryAccent, LineWeight.Subtle),
                primaryAccent, 1f * s);
            _sourceBadgeBg = srcObj.GetComponent<Image>();
            _sourceBadgeOutline = srcObj.GetComponent<Outline>();

            RectTransform srcRt = srcObj.GetComponent<RectTransform>();
            srcRt.pivot = new Vector2(0f, 0.5f);
            srcRt.anchoredPosition = new Vector2(-halfW + 10f * s + 70f * s, topY);

            _sourceBadgeText = UIFactory.CreateText(srcObj.transform, "SourceText", I18n.Tr("WIDGET_DV_SIM", "仿真"), Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, primaryAccent);
            _sourceBadgeText.fontStyle = FontStyle.Bold;
            RectTransform srcTextRt = _sourceBadgeText.GetComponent<RectTransform>();
            srcTextRt.anchoredPosition = Vector2.zero;
            srcTextRt.sizeDelta = new Vector2(25f * s, 14f * s);

            // 右侧总 Δv 标牌
            _totalDvValue = UIFactory.CreateText(parent, "TotalDvValue", "4,850 m/s", Mathf.RoundToInt(13f * s),
                TextAnchor.MiddleRight, primaryAccent);
            _totalDvValue.fontStyle = FontStyle.Bold;
            RectTransform totDvRt = _totalDvValue.GetComponent<RectTransform>();
            totDvRt.pivot = new Vector2(1f, 0.5f);
            totDvRt.anchoredPosition = new Vector2(halfW - 10f * s, topY);
            totDvRt.sizeDelta = new Vector2(95f * s, 18f * s);

            // 总烧燃时序 (Σ mm:ss)
            _totalTimeValue = UIFactory.CreateText(parent, "TotalTimeValue", "Σ 03m 16s", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleRight, WidgetStyleManager.Weighted(textPrimary, LineWeight.Heavy));
            RectTransform totTimeRt = _totalTimeValue.GetComponent<RectTransform>();
            totTimeRt.pivot = new Vector2(1f, 0.5f);
            totTimeRt.anchoredPosition = new Vector2(halfW - 10f * s, topY - 14f * s);
            totTimeRt.sizeDelta = new Vector2(85f * s, 14f * s);
        }

        private void BuildSilhouetteBay(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            Color borderCol = borderColOrDefault(theme);
            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            float halfW = panelSize.x * 0.5f;
            float bayW = 54f * s;
            float bayH = 126f * s;
            float bayX = -halfW + 10f * s + bayW * 0.5f; // -127 + 10 + 27 = -90f * s
            float bayY = -3f * s;

            // 视窗玻璃背板
            _silhouetteBayObj = UIFactory.CreatePanel(parent, "SilhouetteBay", new Vector2(bayW, bayH),
                new Vector2(bayX, bayY), WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),
                WidgetStyleManager.Weighted(borderCol, LineWeight.Strong), 1f * s);
            _silhouetteBayBg = _silhouetteBayObj.GetComponent<Image>();
            _silhouetteBayOutline = _silhouetteBayObj.GetComponent<Outline>();

            // 视窗四角航电瞄准框标 (Corner Reticles)
            float retLen = 5f * s;
            float retW = 1.2f * s;
            Color retCol = WidgetStyleManager.Weighted(primaryAccent, LineWeight.Heavy);
            // 左上
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            // 右上
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, bayH * 0.5f - retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetTR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, bayH * 0.5f - retLen * 0.5f), retCol);
            // 左下
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_H", new Vector2(retLen, retW), new Vector2(-bayW * 0.5f + retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBL_V", new Vector2(retW, retLen), new Vector2(-bayW * 0.5f + retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);
            // 右下
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_H", new Vector2(retLen, retW), new Vector2(bayW * 0.5f - retLen * 0.5f, -bayH * 0.5f + retW * 0.5f), retCol);
            UIFactory.CreatePanel(_silhouetteBayObj.transform, "RetBR_V", new Vector2(retW, retLen), new Vector2(bayW * 0.5f - retW * 0.5f, -bayH * 0.5f + retLen * 0.5f), retCol);

            // 视窗顶标
            _silhouetteBayTitle = UIFactory.CreateText(_silhouetteBayObj.transform, "BayTitle", I18n.Tr("WIDGET_DV_VEHICLE_PROFILE", "飞行器剖面"),
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Weighted(textPrimary, LineWeight.Heavy));
            _silhouetteBayTitle.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _silhouetteBayTitle.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(0f, bayH * 0.5f - 8f * s);
            titleRt.sizeDelta = new Vector2(bayW - 4f * s, 12f * s);

            // 2D 飞船剪影图元 (RawImage 显示 VesselSilhouetteBaker 或本地 GPU 矢量保底，保持 1:1 等比防拉伸)
            float silDim = Mathf.Min(bayW - 12f * s, bayH - 32f * s);
            _silhouetteRawImage = CreateChild<RawImage>("VesselSilhouette_RawImage", _silhouetteBayObj.transform,
                new Vector2(silDim, silDim), new Vector2(0f, -2f * s));
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = secondaryAccent;

            _proceduralSilhouetteGraphic = CreateChild<ProceduralRocketSilhouetteGraphic>("ProceduralSilhouette", _silhouetteBayObj.transform,
                new Vector2(silDim, silDim), new Vector2(0f, -2f * s));
            _proceduralSilhouetteGraphic.raycastTarget = false;
            _proceduralSilhouetteGraphic.color = secondaryAccent;
            _proceduralSilhouetteGraphic.RocketAspect = 6.0f;


            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            bool hasBakerTex = tex != null;
            _silhouetteRawImage.gameObject.SetActive(hasBakerTex);
            _proceduralSilhouetteGraphic.gameObject.SetActive(!hasBakerTex);
            if (hasBakerTex) _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 动态点火喷射羽流 (Exhaust Flare Plume)
            _plumeObj = UIFactory.CreatePanel(_silhouetteBayObj.transform, "EnginePlume", new Vector2(10f * s, 10f * s),
                new Vector2(0f, -bayH * 0.5f + 16f * s), primaryAccent);
            _plumeRt = _plumeObj.GetComponent<RectTransform>();
            _plumeImg = _plumeObj.GetComponent<Image>();
            _plumeObj.SetActive(false);

            // 视窗底标
            _silhouetteBayFooter = UIFactory.CreateText(_silhouetteBayObj.transform, "BayFooter", I18n.Tr("WIDGET_DV_STAGE_ACTV_PLACEHOLDER", "级 03 / 活动"),
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleCenter, primaryAccent);
            _silhouetteBayFooter.fontStyle = FontStyle.Bold;
            RectTransform footRt = _silhouetteBayFooter.GetComponent<RectTransform>();
            footRt.anchoredPosition = new Vector2(0f, -bayH * 0.5f + 6.5f * s);
            footRt.sizeDelta = new Vector2(bayW - 4f * s, 10f * s);
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

        private void BuildStageRows(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;
            Color borderCol = borderColOrDefault(theme);

            float halfW = panelSize.x * 0.5f;
            // 剪影视窗右边缘在 -63f * s (视窗宽 54, 居中在 -90f * s -> -90 + 27 = -63f * s)
            float rowStartX = -63f * s;
            float rowWidth = halfW - 8f * s - rowStartX; // 127 - 8 - (-63) = 182f * s

            float startY = panelSize.y * 0.5f - 44f * s;
            float rowHeight = 18f * s;
            float rowSpacing = 3.5f * s;

            for (int i = 0; i < MaxDisplayedStages; i++)
            {
                float rowY = startY - i * (rowHeight + rowSpacing);

                RectTransform rowRt = CreateContainer($"StageRow_{i}", parent,
                    new Vector2(rowWidth, rowHeight), new Vector2(rowStartX, rowY));
                rowRt.pivot = new Vector2(0f, 0.5f);
                GameObject rowObj = rowRt.gameObject;

                // 0. 航电引出线 (Leader line: 连接剪影视窗右侧至分级标牌)
                GameObject leaderObj = UIFactory.CreatePanel(rowObj.transform, "Leader", new Vector2(6f * s, 1.2f * s),
                    Vector2.zero, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
                RectTransform leaderRt = leaderObj.GetComponent<RectTransform>();
                leaderRt.anchorMin = new Vector2(0f, 0.5f);
                leaderRt.anchorMax = new Vector2(0f, 0.5f);
                leaderRt.pivot = new Vector2(0f, 0.5f);
                leaderRt.anchoredPosition = new Vector2(0f, 0f);
                Image leaderImg = leaderObj.GetComponent<Image>();

                // 1. 分级标号卡片 (Badge: S03, S02...)
                GameObject badgeObj = UIFactory.CreatePanel(rowObj.transform, "Badge", new Vector2(23f * s, 15f * s),
                    Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset),
                    WidgetStyleManager.Weighted(borderCol, LineWeight.Strong), 1f * s);
                RectTransform badgeRt = badgeObj.GetComponent<RectTransform>();
                badgeRt.anchorMin = new Vector2(0f, 0.5f);
                badgeRt.anchorMax = new Vector2(0f, 0.5f);
                badgeRt.pivot = new Vector2(0f, 0.5f);
                badgeRt.anchoredPosition = new Vector2(6f * s, 0f);
                Image badgeBg = badgeObj.GetComponent<Image>();

                Text badgeText = UIFactory.CreateText(badgeObj.transform, "BadgeText", $"S{i:D2}", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleCenter, primaryAccent);
                badgeText.fontStyle = FontStyle.Bold;
                RectTransform badgeTextRt = badgeText.GetComponent<RectTransform>();
                badgeTextRt.anchorMin = new Vector2(0.5f, 0.5f);
                badgeTextRt.anchorMax = new Vector2(0.5f, 0.5f);
                badgeTextRt.anchoredPosition = Vector2.zero;
                badgeTextRt.sizeDelta = new Vector2(23f * s, 15f * s);

                // 2. 水平速度条玻璃凹槽轨道 (Track)
                float trackW = TrackWidth * s;
                float trackH = 10f * s;
                float trackX = 31f * s;

                GameObject trackObj = UIFactory.CreatePanel(rowObj.transform, "Track", new Vector2(trackW, trackH),
                    Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),
                    WidgetStyleManager.Weighted(borderCol, LineWeight.Normal), 1f * s);
                RectTransform trackRt = trackObj.GetComponent<RectTransform>();
                trackRt.anchorMin = new Vector2(0f, 0.5f);
                trackRt.anchorMax = new Vector2(0f, 0.5f);
                trackRt.pivot = new Vector2(0f, 0.5f);
                trackRt.anchoredPosition = new Vector2(trackX, 0f);
                Image trackBg = trackObj.GetComponent<Image>();

                // 3. 动态充填条 (Fill Bar)
                Image fillImg = CreateChild<Image>("FillBar", trackObj.transform,
                    new Vector2(trackW * 0.7f, trackH - 2f * s), new Vector2(1f * s, 0f));
                GameObject fillObj = fillImg.gameObject;
                RectTransform fillRt = fillImg.rectTransform;
                fillRt.anchorMin = new Vector2(0f, 0.5f);
                fillRt.anchorMax = new Vector2(0f, 0.5f);
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillImg.color = primaryAccent;

                // 4. 游标高亮指示 (Caret Line)
                Image caretImg = CreateChild<Image>("Caret", fillObj.transform,
                    new Vector2(2.5f * s, trackH + 2f * s), Vector2.zero);
                GameObject caretObj = caretImg.gameObject;
                RectTransform caretRt = caretImg.rectTransform;
                caretRt.anchorMin = new Vector2(1f, 0.5f);
                caretRt.anchorMax = new Vector2(1f, 0.5f);
                caretRt.pivot = new Vector2(1f, 0.5f);
                caretImg.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue);

                // 5. 速度条右侧单级数值 (Stage ΔV text)
                float dvX = trackX + trackW + 4f * s; // 31 + 54 + 4 = 89f * s
                Text dvText = UIFactory.CreateText(rowObj.transform, "DvText", "1,850 m/s", Mathf.RoundToInt(9f * s),
                    TextAnchor.MiddleLeft, textPrimary);
                dvText.fontStyle = FontStyle.Bold;
                RectTransform dvTextRt = dvText.GetComponent<RectTransform>();
                dvTextRt.anchorMin = new Vector2(0f, 0.5f);
                dvTextRt.anchorMax = new Vector2(0f, 0.5f);
                dvTextRt.pivot = new Vector2(0f, 0.5f);
                dvTextRt.anchoredPosition = new Vector2(dvX, 0f);
                dvTextRt.sizeDelta = new Vector2(50f * s, rowHeight);

                // 6. 右侧烧燃倒计时 (Burn Time: 00:36)
                Text timeText = UIFactory.CreateText(rowObj.transform, "TimeText", "00:52", Mathf.RoundToInt(8.5f * s),
                    TextAnchor.MiddleRight, textPrimary);
                RectTransform timeTextRt = timeText.GetComponent<RectTransform>();
                timeTextRt.anchorMin = new Vector2(1f, 0.5f);
                timeTextRt.anchorMax = new Vector2(1f, 0.5f);
                timeTextRt.pivot = new Vector2(1f, 0.5f);
                timeTextRt.anchoredPosition = new Vector2(-2f * s, 0f);
                timeTextRt.sizeDelta = new Vector2(36f * s, rowHeight);

                _stageRows.Add(new StageRowUI
                {
                    Root = rowObj,
                    RootRt = rowRt,
                    LeaderLine = leaderImg,
                    StageBadgeBg = badgeBg,
                    StageBadgeText = badgeText,
                    TrackRt = trackRt,
                    TrackBg = trackBg,
                    FillBarRt = fillRt,
                    FillBarImg = fillImg,
                    CaretRt = caretRt,
                    CaretImg = caretImg,
                    StageDvText = dvText,
                    StageTimeText = timeText
                });
            }
        }

        private void BuildFooter(Transform parent, Vector2 panelSize, float s, ThemeConfig theme)
        {
            Color borderCol = borderColOrDefault(theme);
            Color textPrimary = theme.TextPrimaryColor;

            float footY = -panelSize.y * 0.5f + 14f * s;

            // 分隔线
            _footerSeparator = UIFactory.CreatePanel(parent, "FooterSeparator", new Vector2(panelSize.x - 20f * s, 1f * s),
                new Vector2(0f, footY + 11f * s), WidgetStyleManager.Weighted(borderCol, LineWeight.Strong)).GetComponent<Image>();

            // 状态摘要行
            _footerStatusText = UIFactory.CreateText(parent, "FooterStatus", I18n.Tr("WIDGET_DV_FOOTER_PLACEHOLDER", "活动: S03 | ΔV: 2,350 m/s | ⏱ 00m 52s"),
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, WidgetStyleManager.Weighted(textPrimary, LineWeight.Solid));
            RectTransform footRt = _footerStatusText.GetComponent<RectTransform>();
            footRt.anchoredPosition = new Vector2(0f, footY);
            footRt.sizeDelta = new Vector2(panelSize.x - 20f * s, 16f * s);
        }

        private readonly Cached<string> _dirtyTotalDvText = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyTotalTimeText = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyBayFooterText = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtyFooterStatusText = new Cached<string>(string.Empty);
        private readonly Cached<string> _dirtySourceText = new Cached<string>(string.Empty);
        private readonly CachedFloat _dirtyPlumeThr = new CachedFloat(-1f, tolerance: 0.01f);
        private readonly Cached<bool> _dirtyIsFiring = new Cached<bool>(false);

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            _logic.TotalDvTemplate = GetTemplateChannel("TOTAL_DV_FORMAT", "{DV:TOTAL}");
            _logic.TotalTimeTemplate = GetTemplateChannel("TOTAL_TIME_FORMAT", "Σ {DV:TOTALTIME}");
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_currentTheme);
            Color borderCol = borderColOrDefault(theme);

            if (!state.HasVessel)
            {
                if (_footerStatusText != null && _dirtyFooterStatusText.Update(state.FooterStatusStr))
                {
                    SetTextIfChanged(_footerStatusText, state.FooterStatusStr);
                    ApplyText(_footerStatusText, state.FooterRole, theme);
                }
                if (_plumeObj != null && _dirtyIsFiring.Update(false))
                {
                    _plumeObj.SetActive(false);
                }
                return;
            }

            // 1. 数据源标识
            if (_sourceBadgeText != null && _dirtySourceText.Update(state.Source))
            {
                SetTextIfChanged(_sourceBadgeText, state.Source);
            }

            // 2. 总 Δv 与总烧燃时序
            if (_totalDvValue != null && _dirtyTotalDvText.Update(state.TotalDvText))
            {
                SetTextIfChanged(_totalDvValue, state.TotalDvText);
            }

            if (_totalTimeValue != null && _dirtyTotalTimeText.Update(state.TotalTimeText))
            {
                SetTextIfChanged(_totalTimeValue, state.TotalTimeText);
            }

            // 3. 逐行速度条
            float maxTrackW = TrackWidth * _cachedScale.Value;
            for (int i = 0; i < _stageRows.Count && i < MaxDisplayedStages; i++)
            {
                StageRowUI row = _stageRows[i];
                var snap = state.GetRow(i);

                if (snap.Visible)
                {
                    row.Root.SetActive(true);
                    if (row.LastStage.Update(snap.Stage))
                    {
                        SetTextIfChanged(row.StageBadgeText, $"S{snap.Stage:D2}");
                    }

                    if (snap.IsActive)
                    {
                        row.StageBadgeBg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
                        ApplyText(row.StageBadgeText, TextStyleRole.InverseOnAccent, theme);
                        row.LeaderLine.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
                    }
                    else
                    {
                        row.StageBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);
                        ApplyText(row.StageBadgeText, TextStyleRole.Accent, theme);
                        row.LeaderLine.color = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
                    }

                    float barW = Mathf.Max(3f * _cachedScale.Value, snap.Ratio * maxTrackW);
                    if (row.LastBarW.Update(barW))
                    {
                        row.FillBarRt.sizeDelta = new Vector2(barW, row.TrackRt.sizeDelta.y - 2f * _cachedScale.Value);
                    }

                    if (snap.IsActive)
                    {
                        row.FillBarImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
                        row.CaretImg.enabled = true;
                        row.CaretImg.color = WidgetStyleManager.Text(TextStyleRole.PrimaryValue, theme);
                    }
                    else
                    {
                        row.FillBarImg.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);
                        row.CaretImg.enabled = false;
                    }

                    if (row.LastDv.Update(snap.DeltaV))
                    {
                        SetTextIfChanged(row.StageDvText, $"{snap.DeltaV:N0} m/s");
                    }
                    string timeCompact = AvionicsFormatting.FormatDurationCompact(snap.BurnTime);
                    if (row.LastTimeText.Update(timeCompact))
                    {
                        SetTextIfChanged(row.StageTimeText, timeCompact);
                    }
                }
                else
                {
                    row.Root.SetActive(false);
                }
            }

            // 4. 左侧 2D 剪影视窗底部状态与发动机羽流
            if (_silhouetteBayFooter != null && _dirtyBayFooterText.Update(state.BayFooterStr))
            {
                SetTextIfChanged(_silhouetteBayFooter, state.BayFooterStr);
                ApplyText(_silhouetteBayFooter, state.HasActive ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
            }

            if (_plumeObj != null)
            {
                if (_dirtyIsFiring.Update(state.IsFiring))
                {
                    _plumeObj.SetActive(state.IsFiring);
                }
                if (state.IsFiring && _plumeRt != null)
                {
                    float thr = state.Throttle;
                    if (_dirtyPlumeThr.Update(thr))
                    {
                        _plumeRt.sizeDelta = new Vector2(9f * _cachedScale.Value, (6f + 8f * thr) * _cachedScale.Value);
                    }
                    _plumeImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
                }
            }

            // 5. 底栏摘要
            if (_footerStatusText != null && _dirtyFooterStatusText.Update(state.FooterStatusStr))
            {
                SetTextIfChanged(_footerStatusText, state.FooterStatusStr);
                ApplyText(_footerStatusText, state.FooterRole, theme);
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _dirtyTotalDvText.Reset(string.Empty);
            _dirtyTotalTimeText.Reset(string.Empty);
            _dirtyBayFooterText.Reset(string.Empty);
            _dirtyFooterStatusText.Reset(string.Empty);
            _dirtySourceText.Reset(string.Empty);
            _dirtyPlumeThr.Reset(-1f);
            _dirtyIsFiring.Reset(false);
            for (int i = 0; i < _stageRows.Count; i++)
            {
                _stageRows[i].LastStage.Reset(-1);
                _stageRows[i].LastDv.Reset(-1.0);
                _stageRows[i].LastBarW.Reset(-1f);
                _stageRows[i].LastTimeText.Reset(string.Empty);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;
            base.ApplyTheme(theme);

            Color primaryAccent = (Color)theme.AccentPrimary;
            Color secondaryAccent = (Color)theme.AccentSecondary;
            Color borderCol = borderColOrDefault(theme);

            if (_topStripe != null) _topStripe.color = primaryAccent;
            if (_titleText != null) ApplyText(_titleText, TextStyleRole.Label, theme);

            if (_sourceBadgeBg != null)
            {
                _sourceBadgeBg.color = WidgetStyleManager.GetDerivedColor(primaryAccent, 0.22f);
            }
            if (_sourceBadgeOutline != null) _sourceBadgeOutline.effectColor = primaryAccent;
            if (_sourceBadgeText != null) ApplyText(_sourceBadgeText, TextStyleRole.Muted, theme);

            if (_totalDvValue != null) ApplyText(_totalDvValue, TextStyleRole.PrimaryValue, theme);
            if (_totalTimeValue != null) ApplyText(_totalTimeValue, TextStyleRole.SecondaryValue, theme);
            if (_footerSeparator != null) _footerSeparator.color = WidgetStyleManager.GetDerivedColor(borderCol, 0.40f);

            // 剪影视窗
            if (_silhouetteBayBg != null)
            {
                WidgetStyleManager.Instance.ApplyCardFrame(_silhouetteBayBg, _silhouetteBayOutline, CardStyleRole.TransparentHUD, theme);
            }
            if (_silhouetteRawImage != null) _silhouetteRawImage.color = secondaryAccent;
            if (_proceduralSilhouetteGraphic != null) _proceduralSilhouetteGraphic.color = secondaryAccent;

            foreach (var row in _stageRows)
            {
                if (row.TrackBg != null)
                {
                    WidgetStyleManager.Instance.ApplyCardFrame(row.TrackBg, row.TrackBg.GetComponent<Outline>(), CardStyleRole.SubtleSlot, theme);
                }
                if (row.StageDvText != null) ApplyText(row.StageDvText, TextStyleRole.PrimaryValue, theme);
                if (row.StageTimeText != null) ApplyText(row.StageTimeText, TextStyleRole.SecondaryValue, theme);
            }
        }

        private Color borderColOrDefault(ThemeConfig theme)
        {
            return theme.FrameBorderColor;
        }

        protected override void OnDestroy()
        {
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }

            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
