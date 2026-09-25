using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.Controls
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 现代化多级火箭分级序列仪 (Avionics Staging Sequence Widget)
    /// ====================================================================================
    /// 全面实现原版分级操作与交互中枢能力，彻底替代 KSP 原版分级列表：
    /// 1. 深度交互操作中枢 (Interactive Staging Manipulation)：
    ///    - [+] 增加分级：顶部与各级快捷插入新空分级 (InsertStage)；
    ///    - [-] 删除分级：快速删除冗余/空分级 (DeleteStage)；
    ///    - 拖拽重排分级：拖动分级徽章直接上下交换分级顺序 (MoveStage)；
    ///    - 拖拽部件跨级移动：拖拽部件芯片跨级投放，零件与其对称体立即同步移至目标分级 (MovePartToStage)；
    ///    - 零件场景高亮与悬停提示：鼠标悬停在芯片上，3D 视口内飞船上对应零件及对称体高亮发光 (SetHighlight)，同时弹出悬停信息卡；
    ///    - 分级触发与安全锁：底部直接点击 ARMED/LOCKED 切换安全锁，点击 STAGE 按键触发分级。
    /// 2. 深度挂钩原版分级部件图标 (Stock Stage Icons Deep Hook & Modern Redraw)：
    ///    通过 StockStageIconService 与 StageIconAtlasGenerator 获取图集与 UV，以高反差 HUD 航电微芯片形式重绘；
    ///    完美支持原版 Atlas 与程序化备用 Atlas，显示对称数量角标 (×4, ×6)。
    /// 3. 单级高精动力学遥测 (Stage Dynamics Telemetry)：
    ///    展示单级可用 ΔV (m/s)、发动机工作时间 (⏱ mm:ss)、推重比 (TWR) 及比冲 (Isp)。
    /// 4. 单级推进剂微量程光条 (Hairline Propellant Gauge)：
    ///    三段式推进剂安全预警条 (20% 黄、5% 红)。
    /// 5. 严格遵守 MFP 规范：
    ///    0 颜色字面量 (MFP-SPEC-006)、0 场景查询 (MFP-SPEC-007)、纯 C# 服务解耦。
    /// </summary>
    [FlightWidget("staging_sequence", "stage_sequence", Category = WidgetCategory.Controls, DisplayName = "STAGE 垂直分级时序序列仪", Description = "垂直火箭分级序列仪：逐级剩余 ΔV、燃烧时间、推重比与单级推进剂微量程，重构原版左侧分级。", DefaultWidgetId = "custom.staging_sequence", DefaultX = -440f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "custom.staging_sequence", "custom.stage_sequence", "core.staging_sequence" })]
    public class StagingSequenceWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // UI 根与卡片
        private Image _bgImage;
        private Outline _bgOutline;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 顶栏总览
        private Text _titleText;
        private Button _addStageTopBtn;
        private Image _addStageTopBg;
        private Text _addStageTopText;
        private Text _totalDvText;
        private Image _topDivider;

        // 部件图标微芯片 UI 结构
        internal class StageIconChipUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image ChipBg;
            public Outline ChipOutline;
            public RawImage IconRawImage;
            public Text MultiplierText;
            public StagePartDragHandler DragHandler;
            public uint PartFlightId;
            public int StageNumber;
            public int PartIndex;
            public StagePartIconData PartData;
            public bool IsHovered;
            public float CurrentScale = 1f;
        }

        // 单级行 UI 结构
        internal class StageItemUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image RowHighlightBg;
            public Image BadgeBg;
            public Text BadgeText;
            public StageBadgeDragHandler BadgeDragHandler;
            public Button InsertAboveBtn;
            public Image InsertAboveBg;
            public Text InsertAboveText;
            public Button DeleteStageBtn;
            public Image DeleteStageBg;
            public Text DeleteStageText;
            public Text StageDvText;
            public Text StageMetaText;
            public GameObject IconsContainer;
            public RectTransform IconsContainerRt;
            public readonly List<StageIconChipUI> IconChips = new List<StageIconChipUI>();
            public GameObject PropBarRoot;
            public RectTransform PropBarRootRt;
            public Image PropTrack;
            public Image PropFill;
            public Text PropNameText;
            public Image Separator;
            public Text EmptySlotText;
            public int StageNumber;
            public bool IsActiveStage;
            public bool IsBurning;
            public float TargetPropFrac;
            public float CurrentPropFrac;
            public float TransitionFlashTimer;
        }

        private const int InitialPooledStages = 10;
        private const int MaxDisplayedStages = 32;
        private const int MaxChipsPerStage = 6;
        private readonly List<StageItemUI> _stageItems = new List<StageItemUI>();

        // 可滚动分级视口组件
        private ScrollRect _scrollRect;
        private RectTransform _scrollViewportRt;
        private RectTransform _scrollContentRt;
        private string _stageOrder = "STOCK";

        // 底栏安全与触发指示
        private Image _bottomDivider;
        private Button _statusBadgeBtn;
        private Image _statusBadgeBg;
        private Text _statusBadgeText;
        private Button _stageTriggerBtn;
        private Image _stageTriggerBg;
        private Text _stageTriggerText;

        // 悬浮信息提示框 (Floating HUD Tooltip)
        private GameObject _tooltipRoot;
        private RectTransform _tooltipRt;
        private Image _tooltipBg;
        private Outline _tooltipOutline;
        private Text _tooltipTitle;
        private Text _tooltipSub;

        // 拖拽影子图标 (Drag Ghost Avatar)
        private GameObject _dragGhostRoot;
        private RectTransform _dragGhostRt;
        private Image _dragGhostBg;
        private RawImage _dragGhostIcon;
        private Text _dragGhostMult;

        // 缓存与脏检查标记
        private ThemeConfig _cachedTheme;
        private bool _lastStageLocked = false;
        private string _lastTotalDvStr = string.Empty;
        private int _highestStageNumber = 0;
        private int _lastActiveStage = -1;
        private float _stageTriggerRecoilTimer = 0f;
        private float _stageTriggerFlashTimer = 0f;
        public static float CustomAnimationTime = -1f;
        public static float CustomAnimationDeltaTime = -1f;

        // 几何参数 (基准像素)
        private const float DefaultWidth = 160f;
        private const float DefaultHeight = 260f;

        // 风格配置
        private string _frameMode = "FAINT";
        private string _titleTemplate = "STAGE SEQUENCE";
        private string _totalDvToken = "{DV:TOTAL}";
        private string _stageDvToken = "{DV:STAGE}";

        private void ParseCustomTemplate(string tpl)
        {
            _frameMode = "FAINT";
            _titleTemplate = "STAGE SEQUENCE";
            _totalDvToken = "{DV:TOTAL}";
            _stageDvToken = "{DV:STAGE}";
            _stageOrder = "STOCK";

            if (string.IsNullOrEmpty(tpl)) return;
            string[] pairs = tpl.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i].Trim();
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                string k = p.Substring(0, eq).Trim().ToUpperInvariant();
                string v = p.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "FRAME": _frameMode = v.ToUpperInvariant(); break;
                    case "TITLE": _titleTemplate = v; break;
                    case "TOTAL_DV_TOKEN": _totalDvToken = v; break;
                    case "STAGE_DV_TOKEN": _stageDvToken = v; break;
                    case "ORDER": _stageOrder = v.ToUpperInvariant(); break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            // 1. 组件包围盒 (基准 160x260 逻辑像素)
            Vector2 size = new Vector2(DefaultWidth * s, DefaultHeight * s);
            RectTransform.sizeDelta = size;

            // 2. 底板卡片 (现代化暗晶毛玻璃背板 0.75 Alpha)
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
            _bgImage.raycastTarget = false; // 不遮挡子元素与 EditMode 交互
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _bgOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);

            // 3. 顶部总览行 (标题 + 插入级 [+] + 全级总 ΔV)
            _titleText = UIFactory.CreateText(transform, "Title_Text", _titleTemplate, Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.sizeDelta = new Vector2(76f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-36f * s, (DefaultHeight * 0.5f - 14f) * s);

            // 顶栏快速插入新分级 [+] 按键
            GameObject addTopGo = new GameObject("Btn_Add_Top", typeof(RectTransform), typeof(Image), typeof(Button));
            addTopGo.transform.SetParent(transform, false);
            RectTransform addTopRt = addTopGo.GetComponent<RectTransform>();
            addTopRt.sizeDelta = new Vector2(14f * s, 13f * s);
            addTopRt.anchoredPosition = new Vector2(12f * s, (DefaultHeight * 0.5f - 14f) * s);
            _addStageTopBg = addTopGo.GetComponent<Image>();
            _addStageTopBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _addStageTopBtn = addTopGo.GetComponent<Button>();
            _addStageTopBtn.onClick.AddListener(OnAddStageTopClicked);

            _addStageTopText = UIFactory.CreateText(addTopGo.transform, "Text", "+", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _addStageTopText.fontStyle = FontStyle.Bold;
            _addStageTopText.rectTransform.sizeDelta = addTopRt.sizeDelta;

            _totalDvText = UIFactory.CreateText(transform, "Total_Dv_Text", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _totalDvText.fontStyle = FontStyle.Bold;
            RectTransform totalDvRt = _totalDvText.rectTransform;
            totalDvRt.sizeDelta = new Vector2(50f * s, 16f * s);
            totalDvRt.anchoredPosition = new Vector2(49f * s, (DefaultHeight * 0.5f - 14f) * s);

            // 顶部分割微线
            GameObject topDivGo = new GameObject("Top_Divider", typeof(RectTransform), typeof(Image));
            topDivGo.transform.SetParent(transform, false);
            RectTransform topDivRt = topDivGo.GetComponent<RectTransform>();
            topDivRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, 1f * s);
            topDivRt.anchoredPosition = new Vector2(0f, (DefaultHeight * 0.5f - 24f) * s);
            _topDivider = topDivGo.GetComponent<Image>();
            _topDivider.raycastTarget = false;

            // 4. 创建可滚动分级视口 (Scroll View + RectMask2D)
            GameObject scrollRootGo = new GameObject("Stages_Scroll_View", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(RectMask2D));
            scrollRootGo.transform.SetParent(transform, false);
            _scrollViewportRt = scrollRootGo.GetComponent<RectTransform>();
            _scrollViewportRt.pivot = new Vector2(0.5f, 0.5f);

            Image viewBg = scrollRootGo.GetComponent<Image>();
            viewBg.color = Color.clear;
            viewBg.raycastTarget = true; // 捕获鼠标滚轮事件

            _scrollRect = scrollRootGo.GetComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 25f;

            GameObject contentGo = new GameObject("Scroll_Content", typeof(RectTransform));
            contentGo.transform.SetParent(scrollRootGo.transform, false);
            _scrollContentRt = contentGo.GetComponent<RectTransform>();
            _scrollContentRt.anchorMin = new Vector2(0f, 1f);
            _scrollContentRt.anchorMax = new Vector2(1f, 1f);
            _scrollContentRt.pivot = new Vector2(0.5f, 1f);

            _scrollRect.content = _scrollContentRt;
            _scrollRect.viewport = _scrollViewportRt;

            // 5. 构建分级行对象池 (初始预分配 10 级)
            for (int i = 0; i < InitialPooledStages; i++)
            {
                StageItemUI item = CreateStageItem(i, s, theme);
                _stageItems.Add(item);
            }

            // 5. 底部分割微线与可交互操作栏
            GameObject botDivGo = new GameObject("Bottom_Divider", typeof(RectTransform), typeof(Image));
            botDivGo.transform.SetParent(transform, false);
            RectTransform botDivRt = botDivGo.GetComponent<RectTransform>();
            botDivRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, 1f * s);
            botDivRt.anchoredPosition = new Vector2(0f, (-DefaultHeight * 0.5f + 20f) * s);
            _bottomDivider = botDivGo.GetComponent<Image>();
            _bottomDivider.raycastTarget = false;

            // 分级锁切换按键 [ARMED / LOCKED]
            GameObject statusBtnGo = new GameObject("Btn_Status_Lock", typeof(RectTransform), typeof(Image), typeof(Button));
            statusBtnGo.transform.SetParent(transform, false);
            RectTransform statusRt = statusBtnGo.GetComponent<RectTransform>();
            statusRt.sizeDelta = new Vector2(50f * s, 15f * s);
            statusRt.anchoredPosition = new Vector2(-46f * s, (-DefaultHeight * 0.5f + 10f) * s);
            _statusBadgeBg = statusBtnGo.GetComponent<Image>();
            _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _statusBadgeBtn = statusBtnGo.GetComponent<Button>();
            _statusBadgeBtn.onClick.AddListener(OnStatusLockClicked);

            _statusBadgeText = UIFactory.CreateText(statusBtnGo.transform, "Status_Text", "ARMED", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _statusBadgeText.fontStyle = FontStyle.Bold;
            _statusBadgeText.rectTransform.sizeDelta = statusRt.sizeDelta;

            // 分级触发按键 [SPACE TO STAGE / ▶ STAGE]
            GameObject trigBtnGo = new GameObject("Btn_Stage_Trigger", typeof(RectTransform), typeof(Image), typeof(Button));
            trigBtnGo.transform.SetParent(transform, false);
            RectTransform trigRt = trigBtnGo.GetComponent<RectTransform>();
            trigRt.sizeDelta = new Vector2(85f * s, 15f * s);
            trigRt.anchoredPosition = new Vector2(28f * s, (-DefaultHeight * 0.5f + 10f) * s);
            _stageTriggerBg = trigBtnGo.GetComponent<Image>();
            _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stageTriggerBtn = trigBtnGo.GetComponent<Button>();
            _stageTriggerBtn.onClick.AddListener(OnStageTriggerClicked);

            _stageTriggerText = UIFactory.CreateText(trigBtnGo.transform, "Trigger_Text", "SPACE TO STAGE", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            _stageTriggerText.rectTransform.sizeDelta = trigRt.sizeDelta;

            // 6. 创建悬浮信息提示卡 (Tooltip)
            CreateTooltipPanel(s, theme);

            // 7. 创建拖拽虚拟影子 (Drag Ghost)
            CreateDragGhost(s, theme);

            ApplyTheme(theme);
        }

        private StageItemUI CreateStageItem(int index, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Transform parent = _scrollContentRt != null ? (Transform)_scrollContentRt : transform;
            GameObject root = new GameObject($"Stage_Item_{index}", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2((DefaultWidth - 14f) * s, 40f * s);

            // 拖放悬停发光底板 (Drop Target Glow)
            GameObject rowHlGo = new GameObject("Row_Highlight_Bg", typeof(RectTransform), typeof(Image));
            rowHlGo.transform.SetParent(root.transform, false);
            RectTransform rowHlRt = rowHlGo.GetComponent<RectTransform>();
            rowHlRt.sizeDelta = new Vector2((DefaultWidth - 14f) * s, 38f * s);
            Image rowHlImg = rowHlGo.GetComponent<Image>();
            rowHlImg.color = Color.clear;
            rowHlImg.raycastTarget = false;

            // 分级徽章 (可拖拽调序，如 S03 / S02)
            GameObject badgeGo = new GameObject("Badge_Bg", typeof(RectTransform), typeof(Image), typeof(Button));
            badgeGo.transform.SetParent(root.transform, false);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.sizeDelta = new Vector2(24f * s, 14f * s);
            Image badgeImg = badgeGo.GetComponent<Image>();
            badgeImg.raycastTarget = true;

            StageBadgeDragHandler badgeDrag = badgeGo.AddComponent<StageBadgeDragHandler>();
            badgeDrag.OwnerWidget = this;

            Text badgeText = UIFactory.CreateText(badgeGo.transform, "Badge_Text", $"S{index:00}", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.rectTransform.sizeDelta = badgeRt.sizeDelta;

            // 快速插入 [+] 与删除 [-] 按钮容器
            GameObject actsGo = new GameObject("Row_Actions", typeof(RectTransform));
            actsGo.transform.SetParent(root.transform, false);
            RectTransform actsRt = actsGo.GetComponent<RectTransform>();
            actsRt.sizeDelta = new Vector2(28f * s, 14f * s);

            // [+] 插入级
            GameObject insGo = new GameObject("Btn_Insert", typeof(RectTransform), typeof(Image), typeof(Button));
            insGo.transform.SetParent(actsGo.transform, false);
            RectTransform insRt = insGo.GetComponent<RectTransform>();
            insRt.sizeDelta = new Vector2(12f * s, 12f * s);
            insRt.anchoredPosition = new Vector2(-7f * s, 0f);
            Image insImg = insGo.GetComponent<Image>();
            insImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            Button insBtn = insGo.GetComponent<Button>();

            Text insTxt = UIFactory.CreateText(insGo.transform, "Text", "+", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            insTxt.rectTransform.sizeDelta = insRt.sizeDelta;

            // [-] 删除级
            GameObject delGo = new GameObject("Btn_Delete", typeof(RectTransform), typeof(Image), typeof(Button));
            delGo.transform.SetParent(actsGo.transform, false);
            RectTransform delRt = delGo.GetComponent<RectTransform>();
            delRt.sizeDelta = new Vector2(12f * s, 12f * s);
            delRt.anchoredPosition = new Vector2(7f * s, 0f);
            Image delImg = delGo.GetComponent<Image>();
            delImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            Button delBtn = delGo.GetComponent<Button>();

            Text delTxt = UIFactory.CreateText(delGo.transform, "Text", "−", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            delTxt.rectTransform.sizeDelta = delRt.sizeDelta;

            // 单级 ΔV 数值
            Text dvText = UIFactory.CreateText(root.transform, "Stage_Dv", "--- m/s", Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            dvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = dvText.rectTransform;
            dvRt.sizeDelta = new Vector2(75f * s, 16f * s);

            // 单级元数据副行 (⏱ 00:52 · TWR 1.65)
            Text metaText = UIFactory.CreateText(root.transform, "Stage_Meta", "---", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform metaRt = metaText.rectTransform;
            metaRt.sizeDelta = new Vector2(136f * s, 12f * s);

            // 空分级占位提示文本 [EMPTY STAGE]
            Text emptyTxt = UIFactory.CreateText(root.transform, "Empty_Slot_Text", "[EMPTY STAGE]", Mathf.RoundToInt(7f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform emptyRt = emptyTxt.rectTransform;
            emptyRt.pivot = new Vector2(0f, 0.5f);
            emptyRt.sizeDelta = new Vector2(80f * s, 14f * s);
            emptyRt.anchoredPosition = new Vector2(-10f * s, 0f);
            emptyTxt.gameObject.SetActive(false);

            // 部件图标托盘容器 (Icons Container)
            GameObject iconsContainer = new GameObject("Icons_Container", typeof(RectTransform));
            iconsContainer.transform.SetParent(root.transform, false);
            RectTransform iconsContainerRt = iconsContainer.GetComponent<RectTransform>();
            iconsContainerRt.sizeDelta = new Vector2(140f * s, 22f * s);

            var chips = new List<StageIconChipUI>();
            Texture initialAtlas = StockStageIconService.Provider?.StockAtlas ?? StageIconAtlasGenerator.GetAtlas();

            for (int c = 0; c < MaxChipsPerStage; c++)
            {
                StageIconChipUI chip = CreateIconChip(iconsContainer.transform, c, s, theme, initialAtlas);
                chips.Add(chip);
            }

            // 推进剂监测微条 (Propellant Bar)
            GameObject propRoot = new GameObject("Prop_Bar_Root", typeof(RectTransform));
            propRoot.transform.SetParent(root.transform, false);
            RectTransform propRootRt = propRoot.GetComponent<RectTransform>();
            propRootRt.sizeDelta = new Vector2(140f * s, 10f * s);

            GameObject trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackGo.transform.SetParent(propRoot.transform, false);
            RectTransform trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.sizeDelta = new Vector2(140f * s, 2f * s);
            Image trackImg = trackGo.GetComponent<Image>();
            trackImg.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(propRoot.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.sizeDelta = new Vector2(140f * s, 2.5f * s);
            fillRt.anchoredPosition = new Vector2(-70f * s, 0f);
            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.raycastTarget = false;

            Text propName = UIFactory.CreateText(propRoot.transform, "Prop_Name", "PROPELLANT", Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform propNameRt = propName.rectTransform;
            propNameRt.sizeDelta = new Vector2(100f * s, 10f * s);
            propNameRt.anchoredPosition = new Vector2(20f * s, 6f * s);

            // 分割微线
            GameObject sepGo = new GameObject("Sep", typeof(RectTransform), typeof(Image));
            sepGo.transform.SetParent(root.transform, false);
            RectTransform sepRt = sepGo.GetComponent<RectTransform>();
            sepRt.sizeDelta = new Vector2(140f * s, 1f * s);
            Image sepImg = sepGo.GetComponent<Image>();
            sepImg.raycastTarget = false;

            var item = new StageItemUI
            {
                Root = root,
                RootRt = rootRt,
                RowHighlightBg = rowHlImg,
                BadgeBg = badgeImg,
                BadgeText = badgeText,
                BadgeDragHandler = badgeDrag,
                InsertAboveBtn = insBtn,
                InsertAboveBg = insImg,
                InsertAboveText = insTxt,
                DeleteStageBtn = delBtn,
                DeleteStageBg = delImg,
                DeleteStageText = delTxt,
                StageDvText = dvText,
                StageMetaText = metaText,
                EmptySlotText = emptyTxt,
                IconsContainer = iconsContainer,
                IconsContainerRt = iconsContainerRt,
                PropBarRoot = propRoot,
                PropBarRootRt = propRootRt,
                PropTrack = trackImg,
                PropFill = fillImg,
                PropNameText = propName,
                Separator = sepImg,
                StageNumber = index
            };
            badgeDrag.StageItem = item;
            item.IconChips.AddRange(chips);

            insBtn.onClick.AddListener(() => OnInsertStageClicked(item.StageNumber + 1));
            delBtn.onClick.AddListener(() => OnDeleteStageClicked(item.StageNumber));

            return item;
        }

        private StageIconChipUI CreateIconChip(Transform parent, int chipIndex, float s, ThemeConfig theme, Texture atlas)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject chipGo = new GameObject($"Chip_{chipIndex}", typeof(RectTransform), typeof(Image), typeof(Outline));
            chipGo.transform.SetParent(parent, false);
            RectTransform chipRt = chipGo.GetComponent<RectTransform>();
            chipRt.sizeDelta = new Vector2(20f * s, 20f * s);
            chipRt.anchoredPosition = new Vector2((-58f + chipIndex * 23f) * s, 0f);

            Image chipBg = chipGo.GetComponent<Image>();
            chipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            chipBg.raycastTarget = true; // 响应拖拽与鼠标悬停

            Outline chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectDistance = new Vector2(1f * s, 1f * s);
            chipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 挂载专用零件拖拽交互处理器
            StagePartDragHandler drag = chipGo.AddComponent<StagePartDragHandler>();
            drag.OwnerWidget = this;

            // 图标 RawImage (18x18 居中)
            GameObject rawGo = new GameObject("Icon_Raw", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(chipGo.transform, false);
            RectTransform rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(18f * s, 18f * s);
            rawRt.anchoredPosition = Vector2.zero;

            RawImage rawImg = rawGo.GetComponent<RawImage>();
            rawImg.raycastTarget = false;
            rawImg.texture = atlas;
            rawImg.uvRect = StageIconAtlasGenerator.GetIconUv(2);
            rawImg.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);

            // 数量倍率文本 (如 ×4, ×6)
            Text multText = UIFactory.CreateText(chipGo.transform, "Mult_Text", "×1", Mathf.RoundToInt(6.5f * s),
                TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            multText.fontStyle = FontStyle.Bold;
            RectTransform multRt = multText.rectTransform;
            multRt.sizeDelta = new Vector2(16f * s, 10f * s);
            multRt.anchoredPosition = new Vector2(2f * s, -5f * s);

            chipGo.SetActive(false);

            var chipUI = new StageIconChipUI
            {
                Root = chipGo,
                RootRt = chipRt,
                ChipBg = chipBg,
                ChipOutline = chipOutline,
                IconRawImage = rawImg,
                MultiplierText = multText,
                DragHandler = drag,
                PartIndex = chipIndex
            };
            drag.Chip = chipUI;

            return chipUI;
        }

        private void CreateTooltipPanel(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _tooltipRoot = new GameObject("Floating_Tooltip", typeof(RectTransform), typeof(Image), typeof(Outline));
            _tooltipRoot.transform.SetParent(transform, false);
            _tooltipRt = _tooltipRoot.GetComponent<RectTransform>();
            _tooltipRt.sizeDelta = new Vector2(136f * s, 30f * s);
            _tooltipRt.pivot = new Vector2(0.5f, 0f);

            _tooltipBg = _tooltipRoot.GetComponent<Image>();
            _tooltipBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.90f);
            _tooltipBg.raycastTarget = false;

            _tooltipOutline = _tooltipRoot.GetComponent<Outline>();
            _tooltipOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _tooltipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);

            _tooltipTitle = UIFactory.CreateText(_tooltipRoot.transform, "Title", "PART INFO", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _tooltipTitle.fontStyle = FontStyle.Bold;
            _tooltipTitle.rectTransform.sizeDelta = new Vector2(128f * s, 12f * s);
            _tooltipTitle.rectTransform.anchoredPosition = new Vector2(0f, 6f * s);

            _tooltipSub = UIFactory.CreateText(_tooltipRoot.transform, "Sub", I18n.Tr("STG_TOOLTIP_HINT", "[拖拽跨级 · 悬停高亮]"), Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _tooltipSub.rectTransform.sizeDelta = new Vector2(128f * s, 10f * s);
            _tooltipSub.rectTransform.anchoredPosition = new Vector2(0f, -6f * s);

            _tooltipRoot.SetActive(false);
        }

        private void CreateDragGhost(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _dragGhostRoot = new GameObject("Drag_Ghost_Avatar", typeof(RectTransform), typeof(Image), typeof(Outline));
            _dragGhostRoot.transform.SetParent(transform, false);
            _dragGhostRt = _dragGhostRoot.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(24f * s, 24f * s);

            _dragGhostBg = _dragGhostRoot.GetComponent<Image>();
            _dragGhostBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.40f);
            _dragGhostBg.raycastTarget = false;

            Outline outl = _dragGhostRoot.GetComponent<Outline>();
            outl.effectDistance = new Vector2(1f * s, 1f * s);
            outl.effectColor = theme.AccentPrimary;

            GameObject rawGo = new GameObject("Ghost_Raw", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(_dragGhostRoot.transform, false);
            RectTransform rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(20f * s, 20f * s);
            _dragGhostIcon = rawGo.GetComponent<RawImage>();
            _dragGhostIcon.raycastTarget = false;
            _dragGhostIcon.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);

            _dragGhostMult = UIFactory.CreateText(_dragGhostRoot.transform, "Mult", "", Mathf.RoundToInt(7f * s),
                TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _dragGhostMult.fontStyle = FontStyle.Bold;
            _dragGhostMult.rectTransform.sizeDelta = new Vector2(16f * s, 10f * s);
            _dragGhostMult.rectTransform.anchoredPosition = new Vector2(2f * s, -5f * s);

            _dragGhostRoot.SetActive(false);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 外框模式着色 (现代化暗晶毛玻璃背板 0.75 Alpha)
            if (_frameMode == "NONE")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null) _bgOutline.enabled = false;
            }
            else if (_frameMode == "FAINT")
            {
                if (_bgImage != null) _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
                if (_bgOutline != null)
                {
                    _bgOutline.enabled = true;
                    _bgOutline.effectColor = _currentCardRole == CardStyleRole.Emphasized
                        ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Medium)
                        : WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                }
            }
            else
            {
                ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);
            }

            // 2. 顶栏与底栏
            ApplyText(_titleText, TextStyleRole.Cardinal, theme);
            ApplyText(_totalDvText, TextStyleRole.PrimaryValue, theme);
            if (_addStageTopBg != null) _addStageTopBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_addStageTopText != null) ApplyText(_addStageTopText, TextStyleRole.PrimaryValue, theme);

            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            if (_statusBadgeBg != null) _statusBadgeBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_statusBadgeText != null) ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            if (_stageTriggerBg != null) _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageTriggerText != null) ApplyText(_stageTriggerText, TextStyleRole.Label, theme);

            // 3. 各分级项着色
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (item.Separator != null) item.Separator.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (item.PropTrack != null) item.PropTrack.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
                if (item.PropFill != null) item.PropFill.color = theme.AccentPrimary;
                if (item.PropNameText != null) ApplyText(item.PropNameText, TextStyleRole.Unit, theme);

                if (item.InsertAboveBg != null) item.InsertAboveBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (item.InsertAboveText != null) ApplyText(item.InsertAboveText, TextStyleRole.SecondaryValue, theme);
                if (item.DeleteStageBg != null) item.DeleteStageBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (item.DeleteStageText != null) ApplyText(item.DeleteStageText, TextStyleRole.Label, theme);
                if (item.EmptySlotText != null) ApplyText(item.EmptySlotText, TextStyleRole.Label, theme);

                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    StageIconChipUI chip = item.IconChips[c];
                    if (chip.ChipBg != null) chip.ChipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    if (chip.ChipOutline != null) chip.ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    if (chip.MultiplierText != null) ApplyText(chip.MultiplierText, TextStyleRole.PrimaryValue, theme);
                }
            }

            // 4. Tooltip & Drag Ghost
            if (_tooltipBg != null) _tooltipBg.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.90f);
            if (_tooltipOutline != null) _tooltipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            if (_tooltipTitle != null) ApplyText(_tooltipTitle, TextStyleRole.PrimaryValue, theme);
            if (_tooltipSub != null) ApplyText(_tooltipSub, TextStyleRole.SecondaryValue, theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 动态标题与全级总 ΔV
            string title = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            SetTextIfChanged(_titleText, title);

            double totalDv = TelemetryTokenEngine.EvaluateNumeric(_totalDvToken, telemetry);
            if (double.IsNaN(totalDv)) totalDv = telemetry.TotalDeltaV;
            string totalDvStr = $"{totalDv:N0} m/s";
            if (totalDvStr != _lastTotalDvStr)
            {
                _lastTotalDvStr = totalDvStr;
                SetTextIfChanged(_totalDvText, totalDvStr);
            }

            // 2. 分级安全锁与状态
            bool isLocked = StockStageActionService.IsStagingLocked || telemetry.IsStageLocked;
            if (isLocked != _lastStageLocked)
            {
                _lastStageLocked = isLocked;
                string statusText = isLocked ? "LOCKED" : "ARMED";
                SetTextIfChanged(_statusBadgeText, statusText);
                _statusBadgeText.color = isLocked 
                    ? style.GetTextColor(TextStyleRole.Warning, theme)
                    : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }

            // 3. 读取分级列表
            IReadOnlyList<StageDeltaVInfo> stages = telemetry.StageDeltaVList;
            int stageCount = stages != null ? stages.Count : 0;
            int curStage = telemetry.CurrentStage;

            // 记录当前最高分级编号
            _highestStageNumber = curStage;
            if (stages != null && stages.Count > 0)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].Stage > _highestStageNumber) _highestStageNumber = stages[i].Stage;
                }
            }

            // 获取当前有效图集
            Texture stockAtlas = StockStageIconService.Provider?.StockAtlas;
            bool isUsingStockAtlas = stockAtlas != null;
            Texture currentAtlas = isUsingStockAtlas ? stockAtlas : StageIconAtlasGenerator.GetAtlas();

            // 构建排序后的分级显示列表
            List<StageDeltaVInfo> sortedStages = new List<StageDeltaVInfo>();
            if (stageCount > 0)
            {
                sortedStages.AddRange(stages);
                if (_stageOrder == "REVERSE")
                {
                    sortedStages.Sort((a, b) => b.Stage.CompareTo(a.Stage));
                }
                else
                {
                    sortedStages.Sort((a, b) => a.Stage.CompareTo(b.Stage));
                }
            }
            else
            {
                sortedStages.Add(new StageDeltaVInfo(curStage, telemetry.StageDeltaV, telemetry.StageBurnTime, telemetry.TWR, 310.0, true));
            }

            if (_lastActiveStage >= 0 && _lastActiveStage != curStage)
            {
                // 分级切除与点火：为新激活级触发入场高光闪烁冲击
                for (int m = 0; m < _stageItems.Count; m++)
                {
                    if (_stageItems[m].StageNumber == curStage)
                    {
                        _stageItems[m].TransitionFlashTimer = 0.5f;
                    }
                }
            }
            _lastActiveStage = curStage;

            int displayCount = Mathf.Min(sortedStages.Count, MaxDisplayedStages);

            // 动态扩充对象池
            while (_stageItems.Count < displayCount)
            {
                _stageItems.Add(CreateStageItem(_stageItems.Count, s, theme));
            }

            // 预估单级高度以实现自适应紧凑包围盒 (Auto-Compact)
            float totalItemsHeight = 0f;
            float[] itemHeights = new float[displayCount];
            for (int k = 0; k < displayCount; k++)
            {
                StageDeltaVInfo stgSample = sortedStages[k];
                int partCount = stgSample.PartIcons != null ? stgSample.PartIcons.Count : 0;
                bool hasDv = stgSample.DeltaV > 0.01 || stgSample.BurnTime > 0.01;
                bool isStgActive = stgSample.IsActive || (stgSample.Stage == curStage);
                bool hasProp = isStgActive;
                if (!hasProp && stgSample.PartIcons != null)
                {
                    for (int p = 0; p < stgSample.PartIcons.Count; p++)
                    {
                        if (stgSample.PartIcons[p].PropellantFraction >= 0f) { hasProp = true; break; }
                    }
                }

                float h;
                if (partCount == 0 && !hasDv)
                {
                    h = 22f; // 空分级
                }
                else if (!hasDv)
                {
                    h = 38f; // 纯动作级（降落伞、分离器）
                }
                else
                {
                    h = 42f; // 基础动力级
                    if (partCount > 0) h += 24f; // 图标槽
                    if (hasProp) h += 12f;      // 推进剂微条
                }
                itemHeights[k] = h;
                totalItemsHeight += (h + 3f);
            }

            // 自适应高度限制 (单级约 96px，多级可扩展至 340px，超出部分由 ScrollView 滚动)
            float headerH = 26f;
            float footerH = 24f;
            float maxDynamicH = 430f;
            float dynamicHeight = Mathf.Clamp(headerH + totalItemsHeight + footerH, 96f, maxDynamicH);

            if (Mathf.Abs(RectTransform.sizeDelta.y - dynamicHeight * s) > 1f)
            {
                RectTransform.sizeDelta = new Vector2(DefaultWidth * s, dynamicHeight * s);
            }

            // 动态对齐顶栏与底栏
            _titleText.rectTransform.anchoredPosition = new Vector2(-36f * s, (dynamicHeight * 0.5f - 13f) * s);
            _addStageTopBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(14f * s, (dynamicHeight * 0.5f - 13f) * s);
            _totalDvText.rectTransform.anchoredPosition = new Vector2(49f * s, (dynamicHeight * 0.5f - 13f) * s);
            _topDivider.rectTransform.anchoredPosition = new Vector2(0f, (dynamicHeight * 0.5f - 24f) * s);

            _bottomDivider.rectTransform.anchoredPosition = new Vector2(0f, (-dynamicHeight * 0.5f + 20f) * s);
            _statusBadgeBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(-46f * s, (-dynamicHeight * 0.5f + 10f) * s);
            _stageTriggerBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(28f * s, (-dynamicHeight * 0.5f + 10f) * s);

            // 更新触发按键文案与可用状态
            string trigText = isLocked ? "LOCKED" : (curStage >= 0 ? $"STAGE S{curStage:00}" : "NO STAGE");
            SetTextIfChanged(_stageTriggerText, trigText);
            _stageTriggerBtn.interactable = !isLocked && curStage >= 0;

            // 视口与滚动内容区域适配
            float viewportH = dynamicHeight - headerH - footerH;
            if (_scrollViewportRt != null)
            {
                _scrollViewportRt.sizeDelta = new Vector2((DefaultWidth - 8f) * s, viewportH * s);
                float viewportCenterY = (dynamicHeight * 0.5f - headerH) - (viewportH * 0.5f);
                _scrollViewportRt.anchoredPosition = new Vector2(0f, viewportCenterY * s);
            }

            if (_scrollContentRt != null)
            {
                float contentH = Mathf.Max(viewportH, totalItemsHeight);
                _scrollContentRt.sizeDelta = new Vector2((DefaultWidth - 14f) * s, contentH * s);
                if (totalItemsHeight <= viewportH + 1f)
                {
                    _scrollContentRt.anchoredPosition = Vector2.zero;
                }
            }

            // 布局 Y 锚点起点 (自内容顶端向下排列)
            float currentY = 0f;

            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (i >= displayCount)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                // 提取单级动力学数据
                StageDeltaVInfo stg = sortedStages[i];
                item.StageNumber = stg.Stage;
                bool isActive = stg.IsActive || (stg.Stage == curStage);
                item.IsActiveStage = isActive;
                item.IsBurning = isActive && (telemetry.Throttle > 0.01f || telemetry.VerticalSpeed > 1f || stg.BurnTime > 0.01);

                int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                bool hasIcons = partIconCount > 0;
                bool hasDv = stg.DeltaV > 0.01 || stg.BurnTime > 0.01;

                bool hasProp = false;
                float propFrac = 0f;
                string propName = "PROPELLANT";

                if (isActive)
                {
                    hasProp = true;
                    propFrac = Mathf.Clamp01(telemetry.StagePropellantFraction);
                    propName = !string.IsNullOrEmpty(telemetry.StagePropellantName) ? telemetry.StagePropellantName : "PROPELLANT";
                }
                else if (stg.PartIcons != null)
                {
                    for (int p = 0; p < stg.PartIcons.Count; p++)
                    {
                        if (stg.PartIcons[p].PropellantFraction >= 0f)
                        {
                            hasProp = true;
                            propFrac = Mathf.Clamp01(stg.PartIcons[p].PropellantFraction);
                            propName = !string.IsNullOrEmpty(stg.PartIcons[p].PropellantName) ? stg.PartIcons[p].PropellantName : "PROPELLANT";
                            break;
                        }
                    }
                }

                item.TargetPropFrac = propFrac;
                if (item.CurrentPropFrac < 0.001f && propFrac > 0.001f)
                {
                    item.CurrentPropFrac = propFrac;
                }

                float itemH = itemHeights[i];
                item.Root.SetActive(true);
                item.RootRt.anchorMin = new Vector2(0.5f, 1f);
                item.RootRt.anchorMax = new Vector2(0.5f, 1f);
                item.RootRt.pivot = new Vector2(0.5f, 0.5f);
                item.RootRt.sizeDelta = new Vector2((DefaultWidth - 14f) * s, itemH * s);
                item.RootRt.anchoredPosition = new Vector2(0f, -currentY - (itemH * 0.5f * s));
                currentY += (itemH + 3f) * s;

                // 1. 分级微章 (S05 / S04) 与全行高亮
                SetTextIfChanged(item.BadgeText, $"S{stg.Stage:00}");
                item.RowHighlightBg.rectTransform.sizeDelta = new Vector2((DefaultWidth - 14f) * s, (itemH - 2f) * s);
                item.RowHighlightBg.color = isActive 
                    ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.12f) 
                    : Color.clear;

                if (isActive)
                {
                    item.BadgeBg.color = theme.AccentPrimary;
                    item.BadgeText.color = style.GetTextColor(TextStyleRole.InverseOnAccent, theme);
                    ApplyText(item.StageDvText, TextStyleRole.PrimaryValue, theme);
                }
                else
                {
                    item.BadgeBg.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
                    item.BadgeText.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    ApplyText(item.StageDvText, TextStyleRole.SecondaryValue, theme);
                }

                // 仅非活跃级允许删除
                item.DeleteStageBtn.gameObject.SetActive(!isActive);

                // 2. 根据分级形态布局各元素
                if (!hasIcons && !hasDv)
                {
                    // === Case A: 空分级 (Empty stage) ===
                    item.BadgeBg.rectTransform.anchoredPosition = new Vector2(-58f * s, 0f);
                    item.InsertAboveBtn.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(-28f * s, 0f);

                    if (item.EmptySlotText != null)
                    {
                        item.EmptySlotText.gameObject.SetActive(true);
                        item.EmptySlotText.rectTransform.anchoredPosition = new Vector2(-10f * s, 0f);
                        SetTextIfChanged(item.EmptySlotText, "[EMPTY STAGE]");
                    }
                    item.StageDvText.gameObject.SetActive(false);
                    item.StageMetaText.gameObject.SetActive(false);
                    item.IconsContainer.SetActive(false);
                    item.PropBarRoot.SetActive(false);
                }
                else if (!hasDv)
                {
                    // === Case B: 纯动作功能级 (降落伞、分离器等) ===
                    item.BadgeBg.rectTransform.anchoredPosition = new Vector2(-58f * s, 8f * s);
                    item.InsertAboveBtn.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(-28f * s, 8f * s);

                    if (item.EmptySlotText != null) item.EmptySlotText.gameObject.SetActive(false);

                    item.StageDvText.gameObject.SetActive(true);
                    item.StageDvText.rectTransform.anchoredPosition = new Vector2(30f * s, 8f * s);
                    SetTextIfChanged(item.StageDvText, "---");

                    item.StageMetaText.gameObject.SetActive(false);

                    // 部件图标槽
                    item.IconsContainer.SetActive(true);
                    item.IconsContainerRt.anchoredPosition = new Vector2(0f, -8f * s);
                    item.PropBarRoot.SetActive(false);
                }
                else
                {
                    // === Case C: 动力推进级 (引擎) ===
                    float line1Y = (itemH * 0.5f - 10f) * s;
                    item.BadgeBg.rectTransform.anchoredPosition = new Vector2(-58f * s, line1Y);
                    item.InsertAboveBtn.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(-28f * s, line1Y);

                    if (item.EmptySlotText != null) item.EmptySlotText.gameObject.SetActive(false);

                    item.StageDvText.gameObject.SetActive(true);
                    item.StageDvText.rectTransform.anchoredPosition = new Vector2(30f * s, line1Y);
                    SetTextIfChanged(item.StageDvText, $"{stg.DeltaV:N0} m/s");

                    float line2Y = (itemH * 0.5f - 24f) * s;
                    item.StageMetaText.gameObject.SetActive(true);
                    item.StageMetaText.rectTransform.anchoredPosition = new Vector2(-5f * s, line2Y);

                    int burnSec = Mathf.Max(0, (int)stg.BurnTime);
                    int m = burnSec / 60;
                    int sec = burnSec % 60;
                    string metaStr = stg.TWR > 0.01 
                        ? $"{m:00}:{sec:00} · {stg.TWR:F2} TWR" 
                        : $"{m:00}:{sec:00} · {stg.Isp:F0}s Isp";
                    SetTextIfChanged(item.StageMetaText, metaStr);

                    if (hasIcons)
                    {
                        item.IconsContainer.SetActive(true);
                        item.IconsContainerRt.anchoredPosition = new Vector2(0f, (itemH * 0.5f - 43f) * s);
                    }
                    else
                    {
                        item.IconsContainer.SetActive(false);
                    }

                    if (hasProp)
                    {
                        item.PropBarRoot.SetActive(true);
                        item.PropBarRootRt.anchoredPosition = new Vector2(0f, (-itemH * 0.5f + 8f) * s);

                        float fullW = 140f * s;
                        item.PropFill.rectTransform.sizeDelta = new Vector2(fullW * propFrac, 2.5f * s);

                        if (propFrac <= 0.05f)
                        {
                            item.PropFill.color = style.GetMeterColor(MeterStyleRole.Danger, theme);
                        }
                        else if (propFrac <= 0.20f)
                        {
                            item.PropFill.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
                        }
                        else
                        {
                            item.PropFill.color = theme.AccentPrimary;
                        }

                        SetTextIfChanged(item.PropNameText, $"{propName.ToUpperInvariant()} {(propFrac * 100f):F0}%");
                    }
                    else
                    {
                        item.PropBarRoot.SetActive(false);
                    }
                }

                // 3. 部件图标微芯片排布 (Icons Tray)
                if (hasIcons && item.IconsContainer.activeSelf)
                {
                    int displayedChips = Mathf.Min(partIconCount, MaxChipsPerStage);
                    for (int c = 0; c < item.IconChips.Count; c++)
                    {
                        StageIconChipUI chip = item.IconChips[c];
                        if (c < displayedChips)
                        {
                            chip.Root.SetActive(true);
                            StagePartIconData partData = stg.PartIcons[c];
                            chip.PartData = partData;
                            chip.PartFlightId = partData.PartFlightId;
                            chip.StageNumber = stg.Stage;
                            chip.PartIndex = c;

                            if (chip.IconRawImage.texture != currentAtlas)
                            {
                                chip.IconRawImage.texture = currentAtlas;
                            }

                            Rect uv;
                            int iconIndex = partData.IconTypeIndex > 0 
                                ? partData.IconTypeIndex 
                                : StageIconAtlasGenerator.GetIconIndex(partData.IconType);

                            if (isUsingStockAtlas)
                            {
                                if (partData.HasStockUv && partData.StockUvRect.width > 0.01f && partData.StockUvRect.width < 0.5f)
                                {
                                    uv = partData.StockUvRect;
                                }
                                else if (StockStageIconService.Provider != null)
                                {
                                    uv = StockStageIconService.Provider.GetStockIconUv(iconIndex);
                                }
                                else
                                {
                                    uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                                }
                            }
                            else
                            {
                                uv = StageIconAtlasGenerator.GetIconUv(iconIndex);
                            }
                            chip.IconRawImage.uvRect = uv;

                            // 活跃级高亮主色，待命级保持白字
                            chip.IconRawImage.color = isActive 
                                ? theme.AccentPrimary 
                                : style.GetTextColor(TextStyleRole.PrimaryValue, theme);

                            // 对称数量标签 (仅当 > 1 时显示)
                            if (partData.Count > 1)
                            {
                                chip.MultiplierText.gameObject.SetActive(true);
                                SetTextIfChanged(chip.MultiplierText, $"×{partData.Count}");
                            }
                            else
                            {
                                chip.MultiplierText.gameObject.SetActive(false);
                            }
                        }
                        else
                        {
                            chip.Root.SetActive(false);
                        }
                    }
                }

                // 4. 分割微线
                item.Separator.rectTransform.anchoredPosition = new Vector2(0f, -itemH * 0.5f * s);
            }
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
            Color primaryText = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color inverseText = style.GetTextColor(TextStyleRole.InverseOnAccent, theme);

            // 1. 底栏分级点火触发按键 (Stage Trigger Button) 战备心跳与击发回弹
            if (_stageTriggerBtn != null && _stageTriggerBg != null)
            {
                if (_stageTriggerRecoilTimer > 0f)
                {
                    _stageTriggerRecoilTimer -= dt;
                    float recoilProgress = Mathf.Clamp01(_stageTriggerRecoilTimer / 0.22f);
                    float recoilScale = Mathf.Lerp(1.0f, 0.92f, recoilProgress);
                    _stageTriggerBtn.transform.localScale = new Vector3(recoilScale, recoilScale, 1f);
                }
                else if (_stageTriggerBtn.transform.localScale.x != 1f)
                {
                    _stageTriggerBtn.transform.localScale = Vector3.one;
                }

                if (_stageTriggerFlashTimer > 0f)
                {
                    _stageTriggerFlashTimer -= dt;
                    float flashP = Mathf.Clamp01(_stageTriggerFlashTimer / 0.28f);
                    _stageTriggerBg.color = Color.Lerp(theme.AccentPrimary, inverseText, flashP * 0.85f);
                }
                else if (_stageTriggerBtn.interactable)
                {
                    // ARMED 战备状态：微妙的正弦心跳呼吸律动 (Armed Heartbeat 1.2 Hz)
                    float trigWave = Mathf.Sin(time * 3.2f) * 0.5f + 0.5f;
                    _stageTriggerBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.72f + trigWave * 0.28f);
                }
                else
                {
                    // LOCKED 锁定状态：静默暗板
                    _stageTriggerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                }
            }

            // 2. 遍历各分级行实现高级呼吸与流动微光
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;

                // A. 激活级背景呼吸与切级闪烁 (Active Stage Row Breathing & Separation Flare)
                if (item.IsActiveStage)
                {
                    float freq = item.IsBurning ? 5.2f : 2.4f;
                    float wave = Mathf.Sin(time * freq) * 0.5f + 0.5f;

                    if (item.TransitionFlashTimer > 0f)
                    {
                        item.TransitionFlashTimer -= dt;
                        float flashP = Mathf.Clamp01(item.TransitionFlashTimer / 0.5f);
                        float flashAlpha = Mathf.Lerp(0.12f + wave * 0.10f, 0.48f, flashP);
                        item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, flashAlpha);
                        item.BadgeBg.color = Color.Lerp(theme.AccentPrimary, inverseText, flashP * 0.6f);
                    }
                    else
                    {
                        float baseAlpha = item.IsBurning ? (0.12f + wave * 0.12f) : (0.08f + wave * 0.08f);
                        item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, baseAlpha);
                        item.BadgeBg.color = Color.Lerp(theme.AccentPrimary, WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Heavy), wave * 0.25f);
                    }
                }

                // B. 推进剂液位平滑流动与低油量频闪 (Propellant Liquid Damping & Emergency Strobe)
                if (item.PropBarRoot != null && item.PropBarRoot.activeSelf)
                {
                    if (Mathf.Abs(item.CurrentPropFrac - item.TargetPropFrac) > 0.001f)
                    {
                        item.CurrentPropFrac = Mathf.MoveTowards(item.CurrentPropFrac, item.TargetPropFrac, dt * 1.8f);
                        float fullW = 140f * s;
                        item.PropFill.rectTransform.sizeDelta = new Vector2(fullW * item.CurrentPropFrac, 2.5f * s);
                    }

                    if (item.CurrentPropFrac <= 0.05f)
                    {
                        // 极度危急：5Hz 烈度频闪 (Emergency Strobe Alert)
                        bool blinkOn = (Mathf.Sin(time * 30f) > 0f);
                        Color dangerCol = style.GetMeterColor(MeterStyleRole.Danger, theme);
                        item.PropFill.color = blinkOn 
                            ? dangerCol 
                            : WidgetStyleManager.WithAlpha(dangerCol, 0.2f);
                    }
                    else if (item.CurrentPropFrac <= 0.20f)
                    {
                        // 低燃料：琥珀色呼吸预警 (Amber Warning Pulse)
                        float warnPulse = Mathf.Sin(time * 8f) * 0.35f + 0.65f;
                        Color warnCol = style.GetMeterColor(MeterStyleRole.Warning, theme);
                        item.PropFill.color = WidgetStyleManager.WithAlpha(warnCol, warnPulse);
                    }
                    else
                    {
                        // 正常余量：伴随燃烧细微光泽扫描 (Combustion Specular Shimmer)
                        if (item.IsBurning)
                        {
                            float shimmer = Mathf.Sin(time * 4.5f + i) * 0.15f + 0.85f;
                            item.PropFill.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, shimmer);
                        }
                        else
                        {
                            item.PropFill.color = theme.AccentPrimary;
                        }
                    }
                }

                // C. 部件芯片悬停浮起微动效 (Part Icon Chip Hover Float)
                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    StageIconChipUI chip = item.IconChips[c];
                    if (!chip.Root.activeSelf) continue;

                    float targetScale = chip.IsHovered ? 1.14f : 1.0f;
                    if (Mathf.Abs(chip.CurrentScale - targetScale) > 0.002f)
                    {
                        chip.CurrentScale = Mathf.MoveTowards(chip.CurrentScale, targetScale, dt * 7.5f);
                        chip.RootRt.localScale = new Vector3(chip.CurrentScale, chip.CurrentScale, 1f);
                    }

                    if (chip.IsHovered)
                    {
                        chip.ChipOutline.effectColor = theme.AccentPrimary;
                        chip.ChipBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.30f);
                    }
                    else
                    {
                        chip.ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                        chip.ChipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    }
                }
            }
        }

        #region 交互响应回调 (Interactive Callbacks)

        private void OnAddStageTopClicked()
        {
            StockStageActionService.InsertStage(_highestStageNumber + 1);
        }

        private void OnInsertStageClicked(int stageIndex)
        {
            StockStageActionService.InsertStage(stageIndex);
        }

        private void OnDeleteStageClicked(int stageIndex)
        {
            StockStageActionService.DeleteStage(stageIndex);
        }

        private void OnStatusLockClicked()
        {
            _stageTriggerFlashTimer = 0.16f;
            StockStageActionService.ToggleStagingLock();
        }

        private void OnStageTriggerClicked()
        {
            _stageTriggerRecoilTimer = 0.22f;
            _stageTriggerFlashTimer = 0.28f;
            StockStageActionService.ActivateNextStage();
        }

        internal void OnChipPointerEnter(StageIconChipUI chip)
        {
            if (chip == null) return;
            chip.IsHovered = true;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;

            // 1. 场景 3D 高亮
            if (chip.PartFlightId > 0)
            {
                StockStageActionService.SetPartHighlight(chip.PartFlightId, true, theme.AccentPrimary);
            }

            // 2. 悬浮提示框 (Tooltip)
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(true);
                string title = !string.IsNullOrEmpty(chip.PartData.PartTitle) ? chip.PartData.PartTitle : chip.PartData.IconType;
                if (chip.PartData.Count > 1) title += $" (×{chip.PartData.Count})";
                SetTextIfChanged(_tooltipTitle, title);

                string dragHint = I18n.Tr("STG_DRAG_MOVE_HINT", "[拖拽跨级移动]");
                string sub = chip.PartData.PropellantFraction >= 0f 
                    ? $"{chip.PartData.PropellantName?.ToUpperInvariant()} {(chip.PartData.PropellantFraction * 100f):F0}% · {dragHint}" 
                    : $"STAGE S{chip.StageNumber:00} · {dragHint}";
                SetTextIfChanged(_tooltipSub, sub);

                // 悬停在芯片正上方
                Vector3 chipWorld = chip.RootRt.position;
                _tooltipRt.position = chipWorld + new Vector3(0f, 22f * s, 0f);
            }
        }

        internal void OnChipPointerExit(StageIconChipUI chip)
        {
            if (chip == null) return;
            chip.IsHovered = false;
            if (chip.PartFlightId > 0)
            {
                StockStageActionService.SetPartHighlight(chip.PartFlightId, false);
            }
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(false);
            }
        }

        internal void OnChipBeginDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (chip == null) return;
            if (_tooltipRoot != null) _tooltipRoot.SetActive(false);

            if (_dragGhostRoot != null)
            {
                _dragGhostRoot.SetActive(true);
                _dragGhostIcon.texture = chip.IconRawImage.texture;
                _dragGhostIcon.uvRect = chip.IconRawImage.uvRect;
                if (chip.PartData.Count > 1)
                {
                    _dragGhostMult.gameObject.SetActive(true);
                    SetTextIfChanged(_dragGhostMult, $"×{chip.PartData.Count}");
                }
                else
                {
                    _dragGhostMult.gameObject.SetActive(false);
                }
                _dragGhostRt.position = eventData.position;
            }
        }

        internal void OnChipDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null && _dragGhostRoot.activeSelf)
            {
                _dragGhostRt.position = eventData.position;
            }

            // 悬停高亮目标分级行
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(item.RootRt, eventData.position, eventData.pressEventCamera);
                if (isHovered && item.StageNumber != chip.StageNumber)
                {
                    item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.20f);
                }
                else
                {
                    item.RowHighlightBg.color = Color.clear;
                }
            }
        }

        internal void OnChipEndDrag(StageIconChipUI chip, PointerEventData eventData)
        {
            if (_dragGhostRoot != null) _dragGhostRoot.SetActive(false);

            StageItemUI targetItem = null;
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (!item.Root.activeSelf) continue;
                item.RowHighlightBg.color = Color.clear;
                if (RectTransformUtility.RectangleContainsScreenPoint(item.RootRt, eventData.position, eventData.pressEventCamera))
                {
                    targetItem = item;
                }
            }

            if (targetItem != null && targetItem.StageNumber != chip.StageNumber)
            {
                StockStageActionService.MovePartToStage(chip.PartFlightId, chip.StageNumber, chip.PartIndex, targetItem.StageNumber);
            }

            StockStageActionService.ClearAllHighlights();
        }

        internal void OnStageBadgeBeginDrag(StageItemUI item, PointerEventData eventData)
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            item.RowHighlightBg.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.30f);
        }

        internal void OnStageBadgeDrag(StageItemUI item, PointerEventData eventData)
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI other = _stageItems[i];
                if (!other.Root.activeSelf || other == item) continue;
                bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(other.RootRt, eventData.position, eventData.pressEventCamera);
                other.RowHighlightBg.color = isHovered ? WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.25f) : Color.clear;
            }
        }

        internal void OnStageBadgeEndDrag(StageItemUI item, PointerEventData eventData)
        {
            item.RowHighlightBg.color = Color.clear;
            StageItemUI targetItem = null;
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI other = _stageItems[i];
                if (!other.Root.activeSelf) continue;
                other.RowHighlightBg.color = Color.clear;
                if (other != item && RectTransformUtility.RectangleContainsScreenPoint(other.RootRt, eventData.position, eventData.pressEventCamera))
                {
                    targetItem = other;
                }
            }

            if (targetItem != null && targetItem.StageNumber != item.StageNumber)
            {
                StockStageActionService.MoveStage(item.StageNumber, targetItem.StageNumber);
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            StockStageActionService.ClearAllHighlights();
            _stageItems.Clear();
            base.OnDestroy();
        }
    }

    #region 专用交互中继器 (Dedicated Event Handlers)

    public class StagePartDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageIconChipUI Chip;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OwnerWidget?.OnChipPointerEnter(Chip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OwnerWidget?.OnChipPointerExit(Chip);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipBeginDrag(Chip, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipDrag(Chip, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnChipEndDrag(Chip, eventData);
        }
    }

    public class StageBadgeDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public StagingSequenceWidget OwnerWidget;
        internal StagingSequenceWidget.StageItemUI StageItem;

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnBeginDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeBeginDrag(StageItem, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeDrag(StageItem, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OwnerWidget?.OnStageBadgeEndDrag(StageItem, eventData);
        }
    }

    #endregion
}
