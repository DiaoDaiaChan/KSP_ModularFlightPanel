using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Avionics;
using ModularFlightPanel.UI.Framework;
using AnnunciatorState = ModularFlightPanel.UI.Framework.AnnunciatorState;

namespace ModularFlightPanel.UI.Widgets.Gauges
{
    /// <summary>
    /// 自由航电搭建仪表板 (Custom Composite Freeform Avionics Panel)
    /// 核心特性：
    /// 1. 深度对标 Photoshop (PS) / Figma 级自由画布：8 点形变把手自由拉伸、独立不透明度调节 (0%~100%)、旋转、图层管理与智能磁吸；
    /// 2. 自动遍历与装配现有 44+ 款组件内的任意控件，亦支持官方高频航电构件库；
    /// 3. 独立 Sub-Canvas 隔离重绘，0 GC 业务解耦大脑 (CompositePanelLogic)；
    /// 4. 完美遵从 SPEC-001 ~ SPEC-012 架构红线。
    /// </summary>
    [FlightWidget("composite_panel", "custom_composite_panel", "freeform_panel", "custom_artboard",
        Category = WidgetCategory.Gauges,
        DisplayName = "自由航电搭建仪表板",
        Description = "对标 Photoshop 自由画布与图层系统的全功能自定义航电仪表板：自由拖拽、独立不透明度、8点形变拉伸、旋转、图层管理与全量控件遍历装配。",
        DefaultWidgetId = "custom.composite_panel",
        DefaultWidth = 380f,
        DefaultHeight = 220f)]
    public class CustomCompositePanelWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(_configData != null ? _configData.BaseWidth : 380f, _configData != null ? _configData.BaseHeight : 220f);
        protected override bool AutoCreateCardFrame => false; // 由面板自主根据 BackgroundStyle 与 PanelOpacity 精确绘制

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard; // 60Hz 渲染
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed; // 10Hz 物理心跳

        private readonly CompositePanelLogic _logic = new CompositePanelLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // IAdaptiveSizeWidget 契约实现
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(100f, 60f);
        public Vector2 MaxBaseSize => new Vector2(1920f, 1080f);

        private CompositePanelConfig _configData;
        private readonly Cached<string> _cachedTemplateJson = new Cached<string>(null);
        private readonly Cached<bool> _needsLayerRebuild = new Cached<bool>(false);

        // 视图层图层映射与缓存
        private class RuntimeLayerItem
        {
            public CompositeElementConfig Config;
            public GameObject RootGo;
            public RectTransform Rt;
            public CanvasGroup AlphaGroup;

            // 控件具体引用
            public Text ValueText;
            public Text TitleText;
            public Text UnitText;
            public Text BadgeText;
            public Image BarFillImage;
            public Image LampImage;
            public Image ButtonLed;
            public Button ActionBtn;
            public Material ArcMaterial;
            public IWidgetControl BoundControl;

            // 防抖脏检缓存 (SPEC-009)
            public readonly Cached<string> CachedStr = new Cached<string>(string.Empty);
            public readonly Cached<float> CachedFloat = new Cached<float>(-1f);
            public readonly Cached<float> CachedArcFill = new Cached<float>(-1f);
            public readonly Cached<Color> CachedColor = new Cached<Color>(Color.clear);
            public readonly Cached<bool> CachedActive = new Cached<bool>(false);
        }

        private static readonly FlightSASMode[] MiniSasModes = new FlightSASMode[]
        {
            FlightSASMode.StabilityAssist,
            FlightSASMode.Prograde,
            FlightSASMode.Retrograde,
            FlightSASMode.Normal,
            FlightSASMode.Antinormal,
            FlightSASMode.RadialIn,
            FlightSASMode.RadialOut,
            FlightSASMode.Target
        };

        private static readonly string[] MiniSasKeys = new string[]
        {
            "SAS_STAB",
            "SAS_PRO",
            "SAS_RET",
            "SAS_NRM",
            "SAS_ANT",
            "SAS_RIN",
            "SAS_ROUT",
            "SAS_TGT"
        };

        private static readonly string[] MiniSasFallbacks = new string[]
        {
            "STAB",
            "PRO",
            "RET",
            "NRM",
            "ANT",
            "R-IN",
            "R-OUT",
            "TGT"
        };

        private readonly List<RuntimeLayerItem> _runtimeLayers = new List<RuntimeLayerItem>();

        private GameObject _artboardBgGo;
        private Image _artboardBgImage;
        private Outline _artboardOutline;
        private CanvasGroup _artboardAlphaGroup;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = ResolveEffectiveTheme(theme);
            LoadAndParseConfig();
            BuildArtboardFrame(theme);
            RebuildLayers(theme);
        }

        private void LoadAndParseConfig()
        {
            _cachedTemplateJson.Value = Config?.CustomTemplate;
            _configData = CompositePanelConfig.FromJson(_cachedTemplateJson.Value);
            _logic.Config = _configData;
        }

        private void BuildArtboardFrame(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            var style = WidgetStyleManager.Instance;

            if (_artboardBgGo == null)
            {
                _artboardBgGo = UIFactory.CreatePanel(transform, "ArtboardBackground", RectTransform.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme));
                _artboardBgImage = _artboardBgGo.GetComponent<Image>();
                _artboardOutline = _artboardBgGo.AddComponent<Outline>();
                _artboardAlphaGroup = _artboardBgGo.AddComponent<CanvasGroup>();
            }

            RectTransform bgRt = _artboardBgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            _artboardAlphaGroup.alpha = Mathf.Clamp01(_configData.PanelOpacity);
            _artboardBgImage.SetColor(style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme));
            _artboardOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
            _artboardOutline.effectDistance = new Vector2(1f * s, 1f * s);
        }

        private void RebuildLayers(ThemeConfig theme)
        {
            // 清理旧图层与控件
            for (int i = 0; i < _runtimeLayers.Count; i++)
            {
                var layer = _runtimeLayers[i];
                if (layer != null)
                {
                    if (layer.ArcMaterial != null)
                    {
                        Destroy(layer.ArcMaterial);
                        layer.ArcMaterial = null;
                    }
                    if (layer.RootGo != null)
                    {
                        Destroy(layer.RootGo);
                    }
                }
            }
            _runtimeLayers.Clear();
            this.Controls.UnregisterAll();

            if (_configData == null || _configData.Elements == null) return;

            float s = CurrentDpiScale;
            var style = WidgetStyleManager.Instance;

            // 按照 DrawOrder 排序构建
            var elements = new List<CompositeElementConfig>(_configData.Elements);
            elements.Sort((a, b) => a.DrawOrder.CompareTo(b.DrawOrder));

            for (int i = 0; i < elements.Count; i++)
            {
                var elem = elements[i];
                if (elem == null) continue;

                var item = new RuntimeLayerItem
                {
                    Config = elem
                };

                // 创建图层容器
                RectTransform layerRt = CreateContainer(elem.LayerId, transform);
                GameObject layerGo = layerRt.gameObject;

                layerRt.anchorMin = new Vector2(0.5f, 0.5f);
                layerRt.anchorMax = new Vector2(0.5f, 0.5f);
                layerRt.pivot = new Vector2(0.5f, 0.5f);
                layerRt.SetAnchoredPositionSafe(new Vector2(elem.X * s, elem.Y * s));
                layerRt.SetSizeDeltaSafe(new Vector2(elem.Width * s, elem.Height * s));
                layerRt.localEulerAngles = new Vector3(0f, 0f, elem.Rotation);

                CanvasGroup cg = layerGo.AddComponent<CanvasGroup>();
                cg.alpha = Mathf.Clamp01(elem.Opacity);
                cg.blocksRaycasts = !elem.IsLocked;

                item.RootGo = layerGo;
                item.Rt = layerRt;
                item.AlphaGroup = cg;

                // 实例化图层内部微构件
                BuildElementContent(item, elem, theme, s, style);

                _runtimeLayers.Add(item);
            }
        }

        private void BuildElementContent(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            var cat = elem.ResolveCategory();
            switch (cat)
            {
                case WidgetControlCategory.Header:
                    BuildHeaderElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.Readout:
                    BuildReadoutElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.LinearGauge:
                    BuildLinearGaugeElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.ArcGauge:
                    BuildArcGaugeElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.Annunciator:
                    BuildAnnunciatorElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.ActionButton:
                    BuildActionButtonElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.ModeCapsule:
                    BuildModeCapsuleElement(item, elem, theme, s, style);
                    break;
                case WidgetControlCategory.Misc:
                case WidgetControlCategory.GenericElement:
                default:
                    BuildStructuralElement(item, elem, theme, s, style);
                    break;
            }
        }

        private void BuildHeaderElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            // 背景卡片 (半透明暗晶)
            GameObject cardGo = UIFactory.CreatePanel(item.RootGo.transform, "HeaderCard", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme));
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = Vector2.zero; cardRt.anchorMax = Vector2.one;
            cardRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 左侧青蓝重点指示色条 (Accent Pill)
            GameObject pillGo = UIFactory.CreatePanel(cardGo.transform, "AccentPill", new Vector2(3.5f * s, 0f), Vector2.zero, theme.AccentPrimary);
            RectTransform pillRt = pillGo.GetComponent<RectTransform>();
            pillRt.anchorMin = new Vector2(0f, 0.18f); pillRt.anchorMax = new Vector2(0f, 0.82f);
            pillRt.pivot = new Vector2(0f, 0.5f);
            pillRt.SetSizeDeltaSafe(new Vector2(3.5f * s, 0f));
            pillRt.SetAnchoredPositionSafe(new Vector2(4f * s, 0f));

            // 主标题 (大写粗体)
            string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : (!string.IsNullOrEmpty(elem.Name) ? elem.Name : I18n.Tr("COMP_ARTBOARD_DEFAULT_NAME", "自由航电仪表板"));
            item.TitleText = UIFactory.CreateText(cardGo.transform, "Title", titleStr, Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            item.TitleText.fontStyle = FontStyle.Bold;
            RectTransform tRt = item.TitleText.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(0.72f, 1f);
            tRt.SetOffsetsSafe(new Vector2(12f * s, 2f * s), Vector2.zero);

            // 右侧状态胶囊徽标 (Status Badge)
            string badgeStr = !string.IsNullOrEmpty(elem.Token) && elem.Token != "{SPD}" ? elem.Token : I18n.Tr("WIDGET_ALERT_NORM", "NORM");
            item.BadgeText = UIFactory.CreateText(cardGo.transform, "Badge", badgeStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            item.BadgeText.fontStyle = FontStyle.Bold;
            RectTransform bRt = item.BadgeText.rectTransform;
            bRt.anchorMin = new Vector2(0.7f, 0f); bRt.anchorMax = new Vector2(1f, 1f);
            bRt.SetOffsetsSafe(Vector2.zero, new Vector2(-6f * s, 2f * s));

            // 底部分割发丝线
            GameObject lineGo = UIFactory.CreatePanel(cardGo.transform, "Divider", new Vector2(0f, 1f * s), Vector2.zero, style.GetLineColor(LineWeight.Faint, theme));
            RectTransform lRt = lineGo.GetComponent<RectTransform>();
            lRt.anchorMin = new Vector2(0f, 0f); lRt.anchorMax = new Vector2(1f, 0f);
            lRt.pivot = new Vector2(0.5f, 0f);
            lRt.SetSizeDeltaSafe(new Vector2(0f, 1f * s));
            lRt.SetAnchoredPositionSafe(Vector2.zero);

            var ctrl = new WidgetHeaderControl(this, elem.LayerId, elem.Name, item.RootGo, item.TitleText, null, item.BadgeText, lineGo.GetComponent<Image>(), titleStr, "", badgeStr);
            item.BoundControl = ctrl;
            this.Controls.Register(ctrl);
        }

        private void BuildReadoutElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            // 背景槽 (Inset)
            GameObject bgGo = UIFactory.CreatePanel(item.RootGo.transform, "SlotBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            var outline = bgGo.AddComponent<Outline>();
            outline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 标题标签 (TopLeft)
            string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
            item.TitleText = UIFactory.CreateText(bgGo.transform, "Title", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tRt = item.TitleText.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0.5f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.SetOffsetsSafe(new Vector2(6f * s, 0f), new Vector2(-6f * s, -3f * s));

            // 主读数大字 (BottomLeft/Center)
            item.ValueText = UIFactory.CreateText(bgGo.transform, "Value", "---", Mathf.RoundToInt(15f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            item.ValueText.fontStyle = FontStyle.Bold;
            RectTransform vRt = item.ValueText.rectTransform;
            vRt.anchorMin = new Vector2(0f, 0f); vRt.anchorMax = new Vector2(0.72f, 0.68f);
            vRt.SetOffsetsSafe(new Vector2(6f * s, 3f * s), Vector2.zero);

            // 单位角标 (BottomRight)
            string unitStr = elem.Unit ?? "";
            item.UnitText = UIFactory.CreateText(bgGo.transform, "Unit", unitStr, Mathf.RoundToInt(8f * s), TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform uRt = item.UnitText.rectTransform;
            uRt.anchorMin = new Vector2(0.7f, 0f); uRt.anchorMax = new Vector2(1f, 0.65f);
            uRt.SetOffsetsSafe(Vector2.zero, new Vector2(-6f * s, 4f * s));

            var ctrl = new WidgetReadoutControl(this, elem.LayerId, elem.Name, item.RootGo, bgGo.GetComponent<Image>(), outline, item.TitleText, item.ValueText, item.UnitText, elem.Token, elem.Title, elem.Unit);
            item.BoundControl = ctrl;
            this.Controls.Register(ctrl);
        }

        private void BuildLinearGaugeElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            bool isVertical = elem.Height > elem.Width * 1.2f;

            // 1. 底槽卡片 (Slot)
            GameObject trackGo = UIFactory.CreatePanel(item.RootGo.transform, "Track", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Slot, theme));
            var outline = trackGo.AddComponent<Outline>();
            outline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
            RectTransform tRt = trackGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            if (!isVertical)
            {
                // 水平排版: 上半部标题与百分比，下半部带留白的内嵌滑轨
                float labelHeight = Mathf.Min(16f * s, elem.Height * 0.45f);

                // 标题 (TopLeft)
                string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(trackGo.transform, "Title", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform lblRt = item.TitleText.rectTransform;
                lblRt.anchorMin = new Vector2(0f, 1f); lblRt.anchorMax = new Vector2(0.7f, 1f);
                lblRt.pivot = new Vector2(0f, 1f);
                lblRt.SetSizeDeltaSafe(new Vector2(0f, labelHeight));
                lblRt.SetAnchoredPositionSafe(new Vector2(6f * s, -1f * s));

                // 读数或百分比 (TopRight)
                item.ValueText = UIFactory.CreateText(trackGo.transform, "Value", "---", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                item.ValueText.fontStyle = FontStyle.Bold;
                RectTransform valRt = item.ValueText.rectTransform;
                valRt.anchorMin = new Vector2(0.7f, 1f); valRt.anchorMax = new Vector2(1f, 1f);
                valRt.pivot = new Vector2(1f, 1f);
                valRt.SetSizeDeltaSafe(new Vector2(0f, labelHeight));
                valRt.SetAnchoredPositionSafe(new Vector2(-6f * s, -1f * s));

                // 下部内衬导轨槽 (Rail)
                GameObject railGo = UIFactory.CreatePanel(trackGo.transform, "Rail", Vector2.zero, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                RectTransform railRt = railGo.GetComponent<RectTransform>();
                railRt.anchorMin = new Vector2(0f, 0f); railRt.anchorMax = new Vector2(1f, 0f);
                railRt.pivot = new Vector2(0.5f, 0f);
                float railH = Mathf.Max(6f * s, elem.Height * 0.4f);
                railRt.SetSizeDeltaSafe(new Vector2(-12f * s, railH));
                railRt.SetAnchoredPositionSafe(new Vector2(0f, 4f * s));

                // 填充条 (2px 内嵌于导轨中，绝不顶格撑满)
                GameObject fillGo = UIFactory.CreatePanel(railGo.transform, "Fill", Vector2.zero, Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
                item.BarFillImage = fillGo.GetComponent<Image>();
                item.BarFillImage.type = Image.Type.Filled;
                item.BarFillImage.fillMethod = Image.FillMethod.Horizontal;
                item.BarFillImage.SetFillAmountSafe(0.5f);

                RectTransform fRt = fillGo.GetComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
                fRt.SetOffsetsSafe(new Vector2(1f * s, 1f * s), new Vector2(-1f * s, -1f * s));
            }
            else
            {
                // 垂直柱条排版: 顶部小标，下部纵向导轨
                string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(trackGo.transform, "Title", titleStr, Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform lblRt = item.TitleText.rectTransform;
                lblRt.anchorMin = new Vector2(0f, 1f); lblRt.anchorMax = new Vector2(1f, 1f);
                lblRt.pivot = new Vector2(0.5f, 1f);
                lblRt.SetSizeDeltaSafe(new Vector2(0f, 16f * s));
                lblRt.SetAnchoredPositionSafe(new Vector2(0f, -2f * s));

                // 垂直内衬导轨
                GameObject railGo = UIFactory.CreatePanel(trackGo.transform, "Rail", Vector2.zero, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                RectTransform railRt = railGo.GetComponent<RectTransform>();
                railRt.anchorMin = new Vector2(0.5f, 0f); railRt.anchorMax = new Vector2(0.5f, 1f);
                railRt.pivot = new Vector2(0.5f, 0.5f);
                railRt.SetSizeDeltaSafe(new Vector2(item.Rt.sizeDelta.x - 8f * s, -(22f * s)));
                railRt.SetAnchoredPositionSafe(new Vector2(0f, -8f * s));

                // 填充条
                GameObject fillGo = UIFactory.CreatePanel(railGo.transform, "Fill", Vector2.zero, Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
                item.BarFillImage = fillGo.GetComponent<Image>();
                item.BarFillImage.type = Image.Type.Filled;
                item.BarFillImage.fillMethod = Image.FillMethod.Vertical;
                item.BarFillImage.SetFillAmountSafe(0.5f);

                RectTransform fRt = fillGo.GetComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
                fRt.SetOffsetsSafe(new Vector2(1f * s, 1f * s), new Vector2(-1f * s, -1f * s));
            }

            var ctrl = new WidgetLinearBarControl(this, elem.LayerId, elem.Name, item.RootGo, trackGo.GetComponent<Image>(), item.BarFillImage, elem.Token, elem.MinValue, elem.MaxValue, item.Rt.sizeDelta.x, isVertical)
            {
                CautionThreshold = elem.CautionThreshold,
                WarningThreshold = elem.WarningThreshold,
                MeterRole = MeterStyleRole.Primary
            };
            item.BoundControl = ctrl;
            this.Controls.Register(ctrl);
        }

        private void BuildArcGaugeElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            // 背景卡片 (半透明暗晶圆角槽)
            GameObject bgGo = UIFactory.CreatePanel(item.RootGo.transform, "ArcBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme));
            var outline = bgGo.AddComponent<Outline>();
            outline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 标题标签 (Top)
            string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
            item.TitleText = UIFactory.CreateText(bgGo.transform, "Title", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tRt = item.TitleText.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.SetSizeDeltaSafe(new Vector2(0f, 14f * s));
            tRt.SetAnchoredPositionSafe(new Vector2(0f, -3f * s));

            // 中心大字数值
            item.ValueText = UIFactory.CreateText(bgGo.transform, "Value", "---", Mathf.RoundToInt(14f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            item.ValueText.fontStyle = FontStyle.Bold;
            RectTransform vRt = item.ValueText.rectTransform;
            vRt.anchorMin = new Vector2(0.2f, 0.28f); vRt.anchorMax = new Vector2(0.8f, 0.72f);
            vRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 底部单位 (Bottom)
            string unitStr = elem.Unit ?? "";
            item.UnitText = UIFactory.CreateText(bgGo.transform, "Unit", unitStr, Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform uRt = item.UnitText.rectTransform;
            uRt.anchorMin = new Vector2(0f, 0f); uRt.anchorMax = new Vector2(1f, 0f);
            uRt.pivot = new Vector2(0.5f, 0f);
            uRt.SetSizeDeltaSafe(new Vector2(0f, 12f * s));
            uRt.SetAnchoredPositionSafe(new Vector2(0f, 4f * s));

            // 圆弧度量环 GameObject
            float ringDim = Mathf.Min(item.Rt.sizeDelta.x, item.Rt.sizeDelta.y) * 0.88f;
            GameObject ringGo = UIFactory.CreatePanel(bgGo.transform, "ArcRing", new Vector2(ringDim, ringDim), Vector2.zero, WidgetStyleManager.NeutralTransparent);
            Image ringImg = ringGo.GetComponent<Image>();
            ringImg.raycastTarget = false;

            if (AssetLoader.RadialMeterShader != null)
            {
                var mat = new Material(AssetLoader.RadialMeterShader);
                mat.SetFloat("_Clockwise", 1.0f);
                mat.SetFloat("_StartAngle", 210.0f);
                mat.SetFloat("_EndAngle", 330.0f);
                mat.SetFloat("_InnerRadius", 0.76f);
                mat.SetFloat("_OuterRadius", 0.92f);
                mat.SetFloat("_SegmentCount", 16.0f);
                mat.SetFloat("_SegmentGap", 0.06f);
                mat.SetColor("_ActiveColor", style.GetMeterColor(MeterStyleRole.Primary, theme));
                mat.SetColor("_InactiveColor", style.GetMeterColor(MeterStyleRole.Track, theme));
                mat.SetColor("_BorderColor", style.GetCardBorderColor(CardStyleRole.Normal, theme));
                mat.SetFloat("_FillAmount", 0.5f);

                ringImg.SetMaterialSafe(mat);
                item.ArcMaterial = mat;

                var ctrl = new WidgetArcMeterControl(this, elem.LayerId, elem.Name, item.RootGo, ringImg, mat, elem.Token, elem.MinValue, elem.MaxValue);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            else
            {
                // Shader 不可用时的回退实现
                ringImg.SetColor(style.GetMeterColor(MeterStyleRole.Primary, theme));
                ringImg.type = Image.Type.Filled;
                ringImg.fillMethod = Image.FillMethod.Radial360;
                ringImg.fillOrigin = (int)Image.Origin360.Top;
                ringImg.SetFillAmountSafe(0.5f);
                item.BarFillImage = ringImg;

                var ctrl = new WidgetArcMeterControl(this, elem.LayerId, elem.Name, item.RootGo, ringImg, null, elem.Token, elem.MinValue, elem.MaxValue);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
        }

        private void BuildAnnunciatorElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            // 驾驶舱倒角双层外框
            GameObject lampGo = UIFactory.CreatePanel(item.RootGo.transform, "LampBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            item.LampImage = lampGo.GetComponent<Image>();
            var outline = lampGo.AddComponent<Outline>();
            outline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));

            RectTransform lRt = lampGo.GetComponent<RectTransform>();
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 内衬高反差背光发光小窗
            GameObject innerGo = UIFactory.CreatePanel(lampGo.transform, "InnerBezel", new Vector2(item.Rt.sizeDelta.x - 4f * s, item.Rt.sizeDelta.y - 4f * s), Vector2.zero, WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.12f));
            RectTransform inRt = innerGo.GetComponent<RectTransform>();
            inRt.anchorMin = Vector2.zero; inRt.anchorMax = Vector2.one;
            inRt.SetOffsetsSafe(new Vector2(2f * s, 2f * s), new Vector2(-2f * s, -2f * s));

            // 居中大写告警标牌文字
            string labelStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
            item.TitleText = UIFactory.CreateText(innerGo.transform, "Label", labelStr, Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            item.TitleText.fontStyle = FontStyle.Bold;
            RectTransform txtRt = item.TitleText.rectTransform;
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            var ctrl = new WidgetAnnunciatorControl(this, elem.LayerId, elem.Name, item.RootGo, item.LampImage, outline, item.TitleText, labelStr);
            item.BoundControl = ctrl;
            this.Controls.Register(ctrl);
        }

        private void BuildActionButtonElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            string proto = (elem.PrototypeId ?? "").ToLowerInvariant();

            // A. 微型 SAS 朝向阵列 (Mini SAS Pad)
            if (proto.Contains("mini_sas") || elem.ActionType == "SAS_PAD")
            {
                var padCtrl = new MiniSasPadControl(this, elem.LayerId, elem.Name, item.RootGo);
                int modeCount = MiniSasModes.Length;
                float btnW = item.Rt.sizeDelta.x / modeCount;
                for (int m = 0; m < modeCount; m++)
                {
                    GameObject btnGo = UIFactory.CreatePanel(item.RootGo.transform, "Sas_" + m, new Vector2(btnW - 2f * s, item.Rt.sizeDelta.y - 2f * s), Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                    RectTransform bRt = btnGo.GetComponent<RectTransform>();
                    bRt.anchorMin = new Vector2((float)m / modeCount, 0f);
                    bRt.anchorMax = new Vector2((float)(m + 1) / modeCount, 1f);
                    bRt.SetOffsetsSafe(new Vector2(1f * s, 1f * s), new Vector2(-1f * s, -1f * s));

                    Button btn = btnGo.AddComponent<Button>();
                    Text lbl = UIFactory.CreateText(btnGo.transform, "Text", I18n.Tr(MiniSasKeys[m], MiniSasFallbacks[m]), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    lbl.fontStyle = FontStyle.Bold;
                    RectTransform lRt = lbl.rectTransform;
                    lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
                    lRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

                    padCtrl.AddSasButton(MiniSasModes[m], btn, btnGo.GetComponent<Image>(), lbl);
                }
                item.BoundControl = padCtrl;
                this.Controls.Register(padCtrl);
                return;
            }

            // B. 双重防误触安全分级器 (Arm & Stage)
            if (proto.Contains("safety_stage") || elem.ActionType == "STAGE")
            {
                // 左侧 ARM 保险键 (38% 宽度)
                GameObject armGo = UIFactory.CreatePanel(item.RootGo.transform, "ArmBtn", Vector2.zero, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                RectTransform armRt = armGo.GetComponent<RectTransform>();
                armRt.anchorMin = new Vector2(0f, 0f); armRt.anchorMax = new Vector2(0.38f, 1f);
                armRt.SetOffsetsSafe(Vector2.zero, new Vector2(-2f * s, 0f));
                Button armBtn = armGo.AddComponent<Button>();
                Text armLbl = UIFactory.CreateText(armGo.transform, "ArmText", I18n.Tr("WIDGET_ALERT_ARMED", "ARM"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                armLbl.fontStyle = FontStyle.Bold;
                RectTransform alRt = armLbl.rectTransform;
                alRt.anchorMin = Vector2.zero; alRt.anchorMax = Vector2.one;
                alRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

                // ARM LED 指示灯条
                GameObject armLedGo = UIFactory.CreatePanel(armGo.transform, "ArmLed", new Vector2(0f, 2.5f * s), Vector2.zero, style.GetTextColor(TextStyleRole.Muted, theme));
                RectTransform aledRt = armLedGo.GetComponent<RectTransform>();
                aledRt.anchorMin = new Vector2(0.15f, 0f); aledRt.anchorMax = new Vector2(0.85f, 0f);
                aledRt.pivot = new Vector2(0.5f, 0f);
                aledRt.SetSizeDeltaSafe(new Vector2(0f, 2.5f * s));
                aledRt.SetAnchoredPositionSafe(new Vector2(0f, 2f * s));

                // 右侧 STAGE 分级大键 (60% 宽度)
                GameObject stageGo = UIFactory.CreatePanel(item.RootGo.transform, "StageBtn", Vector2.zero, Vector2.zero, WidgetStyleManager.WithAlpha(theme.DangerColor, 0.25f));
                RectTransform stRt = stageGo.GetComponent<RectTransform>();
                stRt.anchorMin = new Vector2(0.40f, 0f); stRt.anchorMax = new Vector2(1f, 1f);
                stRt.SetOffsetsSafe(new Vector2(2f * s, 0f), Vector2.zero);
                Button stageBtn = stageGo.AddComponent<Button>();
                stageBtn.interactable = false;
                Text stageLbl = UIFactory.CreateText(stageGo.transform, "StageText", I18n.Tr("WIDGET_CTRL_STAGE_LABEL", "STAGE"), Mathf.RoundToInt(10f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                stageLbl.fontStyle = FontStyle.Bold;
                RectTransform slRt = stageLbl.rectTransform;
                slRt.anchorMin = Vector2.zero; slRt.anchorMax = Vector2.one;
                slRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

                var safetyCtrl = new SafetyArmStageControl(this, elem.LayerId, elem.Name, item.RootGo,
                    stageBtn, stageGo.GetComponent<Image>(), stageLbl,
                    armBtn, armGo.GetComponent<Image>(), armLbl, armLedGo.GetComponent<Image>());
                item.BoundControl = safetyCtrl;
                this.Controls.Register(safetyCtrl);
                return;
            }

            // C. 官方高频标准动作按键 (Tactile Button with status LED)
            GameObject defBtnGo = UIFactory.CreatePanel(item.RootGo.transform, "BtnBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            Button actBtn = defBtnGo.AddComponent<Button>();
            item.ActionBtn = actBtn;
            var defOutline = defBtnGo.AddComponent<Outline>();
            defOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));

            RectTransform dbRt = defBtnGo.GetComponent<RectTransform>();
            dbRt.anchorMin = Vector2.zero; dbRt.anchorMax = Vector2.one;
            dbRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 左侧状态 LED 指示小条 (3px wide)
            GameObject ledGo = UIFactory.CreatePanel(defBtnGo.transform, "Led", new Vector2(3f * s, 0f), Vector2.zero, style.GetTextColor(TextStyleRole.Muted, theme));
            item.ButtonLed = ledGo.GetComponent<Image>();
            RectTransform ledRt = ledGo.GetComponent<RectTransform>();
            ledRt.anchorMin = new Vector2(0f, 0.15f); ledRt.anchorMax = new Vector2(0f, 0.85f);
            ledRt.pivot = new Vector2(0f, 0.5f);
            ledRt.SetSizeDeltaSafe(new Vector2(3f * s, 0f));
            ledRt.SetAnchoredPositionSafe(new Vector2(3.5f * s, 0f));

            // 按钮文案
            string labelStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
            item.TitleText = UIFactory.CreateText(defBtnGo.transform, "Label", labelStr, Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            item.TitleText.fontStyle = FontStyle.Bold;
            RectTransform btxtRt = item.TitleText.rectTransform;
            btxtRt.anchorMin = Vector2.zero; btxtRt.anchorMax = Vector2.one;
            btxtRt.SetOffsetsSafe(new Vector2(7f * s, 0f), Vector2.zero);

            var sysCtrl = new AvionicsSystemSwitchControl(this, elem.LayerId, elem.Name, item.RootGo, actBtn, defBtnGo.GetComponent<Image>(), defOutline, item.TitleText, item.ButtonLed, elem.ActionType);
            item.BoundControl = sysCtrl;
            this.Controls.Register(sysCtrl);
        }

        private void BuildModeCapsuleElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            // 圆角胶囊背景
            GameObject capGo = UIFactory.CreatePanel(item.RootGo.transform, "CapsuleBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            var outline = capGo.AddComponent<Outline>();
            outline.SetColor(WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Normal, theme));

            RectTransform cRt = capGo.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero; cRt.anchorMax = Vector2.one;
            cRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

            // 左侧微型小圆点 (Mode Dot)
            GameObject dotGo = UIFactory.CreatePanel(capGo.transform, "Dot", new Vector2(5f * s, 5f * s), Vector2.zero, theme.AccentPrimary);
            RectTransform dotRt = dotGo.GetComponent<RectTransform>();
            dotRt.anchorMin = new Vector2(0f, 0.5f); dotRt.anchorMax = new Vector2(0f, 0.5f);
            dotRt.pivot = new Vector2(0f, 0.5f);
            dotRt.SetAnchoredPositionSafe(new Vector2(5f * s, 0f));

            // 模式文本
            string labelStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : (!string.IsNullOrEmpty(elem.Name) ? elem.Name : I18n.Tr("COMPOSITE_MODE", "MODE"));
            item.TitleText = UIFactory.CreateText(capGo.transform, "ModeText", labelStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            item.TitleText.fontStyle = FontStyle.Bold;
            RectTransform tRt = item.TitleText.rectTransform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.SetOffsetsSafe(new Vector2(10f * s, 0f), Vector2.zero);

            Button btn = capGo.AddComponent<Button>();
            item.ActionBtn = btn;

            var ctrl = new AvionicsSystemSwitchControl(this, elem.LayerId, elem.Name, item.RootGo, btn, capGo.GetComponent<Image>(), outline, item.TitleText, dotGo.GetComponent<Image>(), elem.ActionType ?? "CYCLE_FRAME");
            item.BoundControl = ctrl;
            this.Controls.Register(ctrl);
        }

        private void BuildStructuralElement(RuntimeLayerItem item, CompositeElementConfig elem, ThemeConfig theme, float s, WidgetStyleManager style)
        {
            bool isHairline = elem.Height <= 4f || elem.Width <= 4f;

            if (isHairline)
            {
                // 纯净发丝级分割线 (无边框内凹，避免方框误解)
                GameObject lineGo = UIFactory.CreatePanel(item.RootGo.transform, "DividerLine", item.Rt.sizeDelta, Vector2.zero, style.GetLineColor(LineWeight.Faint, theme));
                RectTransform lRt = lineGo.GetComponent<RectTransform>();
                lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
                lRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

                var ctrl = new StructuralShapeControl(this, elem.LayerId, elem.Name, item.RootGo, lineGo.GetComponent<Image>(), null, null);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            else
            {
                // 半透明暗晶分区衬垫卡片 (Subtle Dark Glass Panel)
                GameObject cardGo = UIFactory.CreatePanel(item.RootGo.transform, "SectionCard", item.Rt.sizeDelta, Vector2.zero, WidgetStyleManager.WithAlpha(style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme), 0.45f));
                var outline = cardGo.AddComponent<Outline>();
                outline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));

                RectTransform cRt = cardGo.GetComponent<RectTransform>();
                cRt.anchorMin = Vector2.zero; cRt.anchorMax = Vector2.one;
                cRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);

                Text txt = null;
                if (!string.IsNullOrEmpty(elem.Title))
                {
                    txt = UIFactory.CreateText(cardGo.transform, "SectionTitle", elem.Title, Mathf.RoundToInt(8.5f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
                    RectTransform txtRt = txt.rectTransform;
                    txtRt.anchorMin = new Vector2(0f, 0.7f); txtRt.anchorMax = new Vector2(1f, 1f);
                    txtRt.SetOffsetsSafe(new Vector2(6f * s, 0f), new Vector2(-6f * s, -4f * s));
                }

                var ctrl = new StructuralShapeControl(this, elem.LayerId, elem.Name, item.RootGo, cardGo.GetComponent<Image>(), outline, txt);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            theme = ResolveEffectiveTheme(theme);
            if (theme == null) return;

            BuildArtboardFrame(theme);
            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            string curTpl = Config?.CustomTemplate;
            if (curTpl != _cachedTemplateJson.Value)
            {
                LoadAndParseConfig();
                _needsLayerRebuild.Value = true;
            }

            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_needsLayerRebuild.Value)
            {
                _needsLayerRebuild.Value = false;
                RebuildLayers(ResolveEffectiveTheme(context.Theme));
            }
        }

        protected override void OnRenderState()
        {
            var panelState = _logic.CurrentState;
            var theme = ResolveEffectiveTheme(null);
            var style = WidgetStyleManager.Instance;

            for (int i = 0; i < _runtimeLayers.Count; i++)
            {
                var layer = _runtimeLayers[i];
                if (layer == null || !layer.Config.IsVisible) continue;

                var elemState = _logic.GetElementState(i);

                // 1. 读数文本更新 (防抖脏检)
                if (layer.ValueText != null)
                {
                    string txt = elemState.HasValue ? elemState.FormattedText : "---";
                    if (layer.CachedStr.Update(txt))
                    {
                        layer.ValueText.SetTextSafe(txt);
                    }
                    Color col = style.GetTextColor(elemState.TextRole, theme);
                    if (layer.CachedColor.Update(col))
                    {
                        layer.ValueText.SetColor(col);
                    }
                }

                // 2. 线性计量槽填充更新
                if (layer.BarFillImage != null)
                {
                    if (layer.CachedFloat.Update(elemState.NormalizedFraction))
                    {
                        layer.BarFillImage.SetFillAmountSafe(elemState.NormalizedFraction);
                    }
                }

                // 3. 弧形度量环着色器更新
                if (layer.ArcMaterial != null)
                {
                    if (layer.CachedArcFill.Update(elemState.NormalizedFraction))
                    {
                        layer.ArcMaterial.SetFloat("_FillAmount", elemState.NormalizedFraction);
                    }
                }

                // 4. 告警光字牌更新
                if (layer.LampImage != null)
                {
                    Color lampCol;
                    if (elemState.LampState == ModularFlightPanel.UI.Framework.AnnunciatorState.Warning) lampCol = theme.DangerColor;
                    else if (elemState.LampState == ModularFlightPanel.UI.Framework.AnnunciatorState.Caution) lampCol = theme.WarningColor;
                    else if (elemState.LampState == ModularFlightPanel.UI.Framework.AnnunciatorState.Normal) lampCol = style.GetTextColor(TextStyleRole.Accent, theme);
                    else lampCol = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);

                    if (layer.CachedColor.Update(lampCol))
                    {
                        layer.LampImage.SetColor(lampCol);
                    }
                }

                // 5. 按钮状态 LED 更新
                if (layer.ButtonLed != null)
                {
                    if (layer.CachedActive.Update(elemState.IsActive))
                    {
                        Color ledCol = elemState.IsActive
                            ? style.GetTextColor(TextStyleRole.Accent, theme)
                            : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint, theme);
                        layer.ButtonLed.SetColor(ledCol);
                    }
                }
            }
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            if (_configData != null)
            {
                _configData.BaseWidth = pixelSize.x / s;
                _configData.BaseHeight = pixelSize.y / s;
                RectTransform.SetSizeDeltaSafe(pixelSize);
            }
        }

        public CompositePanelConfig GetCurrentCompositeConfig() => _configData;

        public void UpdateCompositeConfig(CompositePanelConfig newCfg)
        {
            if (newCfg == null) return;
            _configData = newCfg;
            _logic.Config = _configData;
            if (Config != null)
            {
                Config.CustomTemplate = newCfg.ToJson();
            }
            _cachedTemplateJson.Value = Config?.CustomTemplate;
            RebuildLayers(WidgetStyleManager.ResolveTheme(null));
        }

        public void UpdateLayerPositionFromOffset(string layerId, Vector2 offset)
        {
            if (_configData == null || _configData.Elements == null) return;
            float s = CurrentDpiScale;
            var elem = _configData.Elements.Find(e => e.LayerId == layerId);
            if (elem != null)
            {
                var layer = _runtimeLayers.Find(l => l.Config != null && l.Config.LayerId == layerId);
                if (layer?.Rt != null)
                {
                    layer.Rt.SetAnchoredPositionSafe(new Vector2(elem.X * s, elem.Y * s) + offset);
                }
            }
        }

        public void CommitLayerOffsetsToConfig()
        {
            if (_configData == null || _configData.Elements == null) return;
            float s = CurrentDpiScale;
            bool modified = false;

            for (int i = 0; i < _runtimeLayers.Count; i++)
            {
                var layer = _runtimeLayers[i];
                if (layer?.Config == null || layer.Rt == null) continue;

                Vector2 curPos = layer.Rt.anchoredPosition;
                float newX = Mathf.Round((curPos.x / s) * 10f) / 10f;
                float newY = Mathf.Round((curPos.y / s) * 10f) / 10f;

                if (Mathf.Abs(layer.Config.X - newX) > 0.05f || Mathf.Abs(layer.Config.Y - newY) > 0.05f)
                {
                    layer.Config.X = newX;
                    layer.Config.Y = newY;
                    modified = true;
                }
            }

            if (modified)
            {
                SaveCompositeConfig();
            }
        }

        public void SaveCompositeConfig()
        {
            if (_configData != null && Config != null)
            {
                Config.CustomTemplate = _configData.ToJson();
                _cachedTemplateJson.Value = Config.CustomTemplate;
            }
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < _runtimeLayers.Count; i++)
            {
                if (_runtimeLayers[i]?.ArcMaterial != null)
                {
                    Destroy(_runtimeLayers[i].ArcMaterial);
                    _runtimeLayers[i].ArcMaterial = null;
                }
            }
            base.OnDestroy();
        }
    }
}
