using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.Controls
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 现代化多级火箭分级序列仪 (Avionics Staging Sequence Widget)
    /// ====================================================================================
    /// 核心设计语言 (极简垂直分级栈 HUD 风格，重构 KSP 原版左侧厚重分级列表)：
    /// 1. 深度挂钩原版分级部件图标 (Stock Stage Icons Deep Hook & Modern Redraw)：
    ///    通过 StockStageIconService 与 StageIconAtlasGenerator 深度获取原版部件图集与 UV 贴图，
    ///    以现代高反差 HUD 航电微芯片形式重绘展示 (发动机 🚀、固体助推器 ⚙、分离器 ☲、降落伞 🪂、指令舱 ⯌)；
    ///    支持部件对称/数量角标 (×4, ×6) 并在无头模式下提供 100% 矢量的程序化图集后备。
    /// 2. 垂直堆叠分级序列栈 (Vertical Staging Stack)：
    ///    自顶向下呈现当前激活级与后续待命分级 (S5 -> S4 -> S3 -> S2 -> S1 -> S0)；
    ///    激活级高亮发光显示，已抛弃级自动移除，待命级保持清爽次级色。
    /// 3. 单级高精动力学遥测 (Stage Dynamics Telemetry)：
    ///    展示单级可用 ΔV (m/s)、发动机全推力工作时间 (⏱ mm:ss)、推重比 (TWR) 及比冲 (Isp)。
    /// 4. 单级推进剂微量程光条 (Hairline Propellant Gauge)：
    ///    为当前激活级及含燃料分级提供高集成度极细推进剂监控条 (LF+OX / SOLID / MONO)，
    ///    具备 20% 黄、5% 红三段式安全预警。
    /// 5. 载具总余量汇总与安全锁状态 (Summary & Safety Status)：
    ///    顶部展示载具全级可用总 ΔV，底部指示分级安全锁 (ARMED / LOCKED) 与就绪指示。
    /// 6. 严格遵守 MFP 规范：
    ///    0 颜色字面量 (MFP-SPEC-006)、0 场景查询 (MFP-SPEC-007)、纯 C# IFlightTelemetry 契约驱动。
    /// </summary>
    public class StagingSequenceWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // UI 根与卡片
        private Image _bgImage;
        private Outline _bgOutline;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 顶栏总览
        private Text _titleText;
        private Text _totalDvText;
        private Image _topDivider;

        // 部件图标微芯片 UI 结构
        private class StageIconChipUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image ChipBg;
            public Outline ChipOutline;
            public RawImage IconRawImage;
            public Text MultiplierText;
        }

        // 单级行 UI 结构
        private class StageItemUI
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image BadgeBg;
            public Text BadgeText;
            public Text StageDvText;
            public Text StageMetaText;       // ⏱ 01:14 · 1.65 TWR
            public GameObject IconsContainer;
            public RectTransform IconsContainerRt;
            public readonly List<StageIconChipUI> IconChips = new List<StageIconChipUI>();
            public GameObject PropBarRoot;
            public RectTransform PropBarRootRt;
            public Image PropTrack;
            public Image PropFill;
            public Text PropNameText;
            public Image Separator;
        }

        private const int MaxDisplayedStages = 6;
        private const int MaxChipsPerStage = 4;
        private readonly List<StageItemUI> _stageItems = new List<StageItemUI>();

        // 底栏安全与触发指示
        private Image _bottomDivider;
        private Text _statusBadgeText;
        private Text _stageTriggerText;

        // 缓存与脏检查标记
        private ThemeConfig _cachedTheme;
        private bool _lastStageLocked = false;
        private string _lastTitleStr = string.Empty;
        private string _lastTotalDvStr = string.Empty;
        private string _lastStatusStr = string.Empty;

        // 几何参数 (基准像素)
        private const float DefaultWidth = 160f;
        private const float DefaultHeight = 260f;

        // 风格配置 (FAINT: 极细淡边框[默认], NONE: 完全无框, NORMAL: 传统卡片)
        private string _frameMode = "FAINT";

        // 通配符通道与可覆盖模板
        private string _titleTemplate = "STAGE SEQUENCE";
        private string _totalDvToken = "{DV:TOTAL}";
        private string _stageDvToken = "{DV:STAGE}";

        private void ParseCustomTemplate(string tpl)
        {
            _frameMode = "FAINT";
            _titleTemplate = "STAGE SEQUENCE";
            _totalDvToken = "{DV:TOTAL}";
            _stageDvToken = "{DV:STAGE}";

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

            // 2. 底板卡片 (全息透明 HUD / 极简淡框风格)
            _bgImage = gameObject.AddComponent<Image>();
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 顶部总览行 (标题 + 全级总 ΔV)
            _titleText = UIFactory.CreateText(transform, "Title_Text", _titleTemplate, Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.sizeDelta = new Vector2(90f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-28f * s, (DefaultHeight * 0.5f - 14f) * s);

            _totalDvText = UIFactory.CreateText(transform, "Total_Dv_Text", "---", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _totalDvText.fontStyle = FontStyle.Bold;
            RectTransform totalDvRt = _totalDvText.rectTransform;
            totalDvRt.sizeDelta = new Vector2(60f * s, 16f * s);
            totalDvRt.anchoredPosition = new Vector2(44f * s, (DefaultHeight * 0.5f - 14f) * s);

            // 顶部分割微线
            GameObject topDivGo = new GameObject("Top_Divider", typeof(RectTransform), typeof(Image));
            topDivGo.transform.SetParent(transform, false);
            RectTransform topDivRt = topDivGo.GetComponent<RectTransform>();
            topDivRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, 1f * s);
            topDivRt.anchoredPosition = new Vector2(0f, (DefaultHeight * 0.5f - 24f) * s);
            _topDivider = topDivGo.GetComponent<Image>();
            _topDivider.raycastTarget = false;

            // 4. 构建预分配分级行对象池 (最多展示 6 级)
            for (int i = 0; i < MaxDisplayedStages; i++)
            {
                StageItemUI item = CreateStageItem(i, s, theme);
                _stageItems.Add(item);
            }

            // 5. 底部分割微线与状态栏
            GameObject botDivGo = new GameObject("Bottom_Divider", typeof(RectTransform), typeof(Image));
            botDivGo.transform.SetParent(transform, false);
            RectTransform botDivRt = botDivGo.GetComponent<RectTransform>();
            botDivRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, 1f * s);
            botDivRt.anchoredPosition = new Vector2(0f, (-DefaultHeight * 0.5f + 20f) * s);
            _bottomDivider = botDivGo.GetComponent<Image>();
            _bottomDivider.raycastTarget = false;

            _statusBadgeText = UIFactory.CreateText(transform, "Status_Badge", "ARMED", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _statusBadgeText.fontStyle = FontStyle.Bold;
            RectTransform statusRt = _statusBadgeText.rectTransform;
            statusRt.sizeDelta = new Vector2(70f * s, 14f * s);
            statusRt.anchoredPosition = new Vector2(-36f * s, (-DefaultHeight * 0.5f + 10f) * s);

            _stageTriggerText = UIFactory.CreateText(transform, "Stage_Trigger_Hint", "SPACE TO STAGE", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trigRt = _stageTriggerText.rectTransform;
            trigRt.sizeDelta = new Vector2(70f * s, 14f * s);
            trigRt.anchoredPosition = new Vector2(36f * s, (-DefaultHeight * 0.5f + 10f) * s);

            ApplyTheme(theme);
        }

        private StageItemUI CreateStageItem(int index, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject root = new GameObject($"Stage_Item_{index}", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, 40f * s);

            // 分级微章 (S05 / S04)
            GameObject badgeGo = new GameObject("Badge_Bg", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(root.transform, false);
            RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.sizeDelta = new Vector2(24f * s, 14f * s);
            Image badgeImg = badgeGo.GetComponent<Image>();
            badgeImg.raycastTarget = false;

            Text badgeText = UIFactory.CreateText(badgeGo.transform, "Badge_Text", $"S{index:00}", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.rectTransform.sizeDelta = badgeRt.sizeDelta;

            // 单级 ΔV 数值
            Text dvText = UIFactory.CreateText(root.transform, "Stage_Dv", "--- m/s", Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            dvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = dvText.rectTransform;
            dvRt.sizeDelta = new Vector2(85f * s, 16f * s);

            // 单级元数据副行 (⏱ 00:52 · TWR 1.65)
            Text metaText = UIFactory.CreateText(root.transform, "Stage_Meta", "---", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform metaRt = metaText.rectTransform;
            metaRt.sizeDelta = new Vector2(136f * s, 12f * s);

            // 部件图标托盘容器 (Icons Container)
            GameObject iconsContainer = new GameObject("Icons_Container", typeof(RectTransform));
            iconsContainer.transform.SetParent(root.transform, false);
            RectTransform iconsContainerRt = iconsContainer.GetComponent<RectTransform>();
            iconsContainerRt.sizeDelta = new Vector2(140f * s, 22f * s);

            // 预分配最多 4 个部件微芯片
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
            trackRt.anchoredPosition = new Vector2(0f, 0f);
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
                BadgeBg = badgeImg,
                BadgeText = badgeText,
                StageDvText = dvText,
                StageMetaText = metaText,
                IconsContainer = iconsContainer,
                IconsContainerRt = iconsContainerRt,
                PropBarRoot = propRoot,
                PropBarRootRt = propRootRt,
                PropTrack = trackImg,
                PropFill = fillImg,
                PropNameText = propName,
                Separator = sepImg
            };
            item.IconChips.AddRange(chips);
            return item;
        }

        private StageIconChipUI CreateIconChip(Transform parent, int chipIndex, float s, ThemeConfig theme, Texture atlas)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject chipGo = new GameObject($"Chip_{chipIndex}", typeof(RectTransform), typeof(Image), typeof(Outline));
            chipGo.transform.SetParent(parent, false);
            RectTransform chipRt = chipGo.GetComponent<RectTransform>();
            chipRt.sizeDelta = new Vector2(22f * s, 22f * s);
            // 依次水平紧凑排布
            chipRt.anchoredPosition = new Vector2((-57f + chipIndex * 26f) * s, 0f);

            Image chipBg = chipGo.GetComponent<Image>();
            chipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            chipBg.raycastTarget = false;

            Outline chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectDistance = new Vector2(1f * s, 1f * s);
            chipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

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

            // 数量倍率文本 (如 ×4, ×6，居于芯片右下角)
            Text multText = UIFactory.CreateText(chipGo.transform, "Mult_Text", "×1", Mathf.RoundToInt(6.5f * s),
                TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            multText.fontStyle = FontStyle.Bold;
            RectTransform multRt = multText.rectTransform;
            multRt.sizeDelta = new Vector2(16f * s, 10f * s);
            multRt.anchoredPosition = new Vector2(2f * s, -5f * s);

            chipGo.SetActive(false);

            return new StageIconChipUI
            {
                Root = chipGo,
                RootRt = chipRt,
                ChipBg = chipBg,
                ChipOutline = chipOutline,
                IconRawImage = rawImg,
                MultiplierText = multText
            };
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 外框模式着色
            if (_frameMode == "NONE")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null) _bgOutline.enabled = false;
            }
            else if (_frameMode == "FAINT")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null)
                {
                    _bgOutline.enabled = true;
                    _bgOutline.effectColor = _currentCardRole == CardStyleRole.Emphasized
                        ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Medium)
                        : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }
            else
            {
                ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);
            }

            // 2. 顶栏与底栏
            ApplyText(_titleText, TextStyleRole.Cardinal, theme);
            ApplyText(_totalDvText, TextStyleRole.PrimaryValue, theme);
            if (_topDivider != null) _topDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_bottomDivider != null) _bottomDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_stageTriggerText, TextStyleRole.Label, theme);

            // 3. 各分级项着色
            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (item.Separator != null) item.Separator.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (item.PropTrack != null) item.PropTrack.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
                if (item.PropFill != null) item.PropFill.color = theme.AccentPrimary;
                if (item.PropNameText != null) ApplyText(item.PropNameText, TextStyleRole.Unit, theme);

                for (int c = 0; c < item.IconChips.Count; c++)
                {
                    StageIconChipUI chip = item.IconChips[c];
                    if (chip.ChipBg != null) chip.ChipBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    if (chip.ChipOutline != null) chip.ChipOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    if (chip.MultiplierText != null) ApplyText(chip.MultiplierText, TextStyleRole.PrimaryValue, theme);
                }
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
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
            bool isLocked = telemetry.IsStageLocked;
            if (isLocked != _lastStageLocked)
            {
                _lastStageLocked = isLocked;
                string statusText = isLocked ? "LOCKED" : "ARMED";
                SetTextIfChanged(_statusBadgeText, statusText);
                _statusBadgeText.color = isLocked 
                    ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, theme)
                    : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }

            // 3. 读取分级列表 (从 StageDeltaVList 或当前单级构建)
            IReadOnlyList<StageDeltaVInfo> stages = telemetry.StageDeltaVList;
            int stageCount = stages != null ? stages.Count : 0;
            int curStage = telemetry.CurrentStage;

            // 获取当前有效图集
            Texture currentAtlas = StockStageIconService.Provider?.StockAtlas ?? StageIconAtlasGenerator.GetAtlas();

            // 布局 Y 锚点起点 (自顶向下排列)
            float currentY = (DefaultHeight * 0.5f - 28f) * s;
            float bottomLimitY = (-DefaultHeight * 0.5f + 24f) * s;

            for (int i = 0; i < _stageItems.Count; i++)
            {
                StageItemUI item = _stageItems[i];
                if (i >= stageCount && (i > 0 || stageCount > 0))
                {
                    item.Root.SetActive(false);
                    continue;
                }

                // 提取单级动力学数据
                StageDeltaVInfo stg;
                if (stageCount > 0)
                {
                    stg = stages[i];
                }
                else
                {
                    stg = new StageDeltaVInfo(curStage, telemetry.StageDeltaV, telemetry.StageBurnTime, telemetry.TWR, 310.0, true);
                }

                bool isActive = stg.IsActive || (stg.Stage == curStage);

                // 判断是否展示部件图标
                int partIconCount = stg.PartIcons != null ? stg.PartIcons.Count : 0;
                bool hasIcons = partIconCount > 0;

                // 判断是否展示推进剂进度条
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

                // 计算当前分级行高
                float itemH = 28f;
                if (hasIcons) itemH += 28f;
                if (hasProp) itemH += 14f;

                // 视口底部溢出保护
                if (currentY - itemH * s < bottomLimitY)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                item.Root.SetActive(true);
                item.RootRt.sizeDelta = new Vector2((DefaultWidth - 16f) * s, itemH * s);
                item.RootRt.anchoredPosition = new Vector2(0f, currentY - itemH * 0.5f * s);
                currentY -= (itemH + 3f) * s;

                // 1. 分级微章 (S05 / S04)
                SetTextIfChanged(item.BadgeText, $"S{stg.Stage:00}");
                item.BadgeBg.rectTransform.anchoredPosition = new Vector2(-58f * s, (itemH * 0.5f - 9f) * s);
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

                // 2. 单级 ΔV 数值
                SetTextIfChanged(item.StageDvText, $"{stg.DeltaV:N0} m/s");
                item.StageDvText.rectTransform.anchoredPosition = new Vector2(28f * s, (itemH * 0.5f - 9f) * s);

                // 3. 单级元数据副行 (00:52 · 1.65 TWR)
                int burnSec = Mathf.Max(0, (int)stg.BurnTime);
                int m = burnSec / 60;
                int sec = burnSec % 60;
                string metaStr = stg.TWR > 0.01 
                    ? $"{m:00}:{sec:00} · {stg.TWR:F2} TWR" 
                    : $"{m:00}:{sec:00} · {stg.Isp:F0}s Isp";
                SetTextIfChanged(item.StageMetaText, metaStr);
                item.StageMetaText.rectTransform.anchoredPosition = new Vector2(-5f * s, (itemH * 0.5f - 23f) * s);

                // 4. 部件图标微芯片排布 (Icons Tray)
                if (hasIcons)
                {
                    item.IconsContainer.SetActive(true);
                    item.IconsContainerRt.anchoredPosition = new Vector2(0f, (itemH * 0.5f - 43f) * s);

                    int displayedChips = Mathf.Min(partIconCount, MaxChipsPerStage);
                    for (int c = 0; c < item.IconChips.Count; c++)
                    {
                        StageIconChipUI chip = item.IconChips[c];
                        if (c < displayedChips)
                        {
                            chip.Root.SetActive(true);
                            StagePartIconData partData = stg.PartIcons[c];

                            if (chip.IconRawImage.texture != currentAtlas)
                            {
                                chip.IconRawImage.texture = currentAtlas;
                            }

                            Rect uv = partData.HasStockUv 
                                ? partData.StockUvRect 
                                : (StockStageIconService.Provider != null 
                                    ? StockStageIconService.Provider.GetStockIconUv(partData.IconTypeIndex) 
                                    : StageIconAtlasGenerator.GetIconUv(partData.IconTypeIndex));
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
                else
                {
                    item.IconsContainer.SetActive(false);
                }

                // 5. 推进剂监控微条
                if (hasProp)
                {
                    item.PropBarRoot.SetActive(true);
                    item.PropBarRootRt.anchoredPosition = new Vector2(0f, (-itemH * 0.5f + 9f) * s);

                    float fullW = 140f * s;
                    item.PropFill.rectTransform.sizeDelta = new Vector2(fullW * propFrac, 2.5f * s);

                    // 推进剂三段式预警变色
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

                // 6. 分割微线
                item.Separator.rectTransform.anchoredPosition = new Vector2(0f, -itemH * 0.5f * s);
            }
        }

        protected override void OnDestroy()
        {
            _stageItems.Clear();
            base.OnDestroy();
        }
    }
}
