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
        private string _cachedTemplateJson = null;

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
            public Image BarFillImage;
            public Image LampImage;
            public Image ButtonLed;
            public Button ActionBtn;
            public IWidgetControl BoundControl;

            // 防抖脏检缓存 (SPEC-009)
            public readonly Cached<string> CachedStr = new Cached<string>(string.Empty);
            public readonly Cached<float> CachedFloat = new Cached<float>(-1f);
            public readonly Cached<Color> CachedColor = new Cached<Color>(Color.clear);
            public readonly Cached<bool> CachedActive = new Cached<bool>(false);
        }

        private readonly List<RuntimeLayerItem> _runtimeLayers = new List<RuntimeLayerItem>();

        private GameObject _artboardBgGo;
        private Image _artboardBgImage;
        private Outline _artboardOutline;
        private CanvasGroup _artboardAlphaGroup;
        private bool _needsLayerRebuild = false;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = ResolveEffectiveTheme(theme);
            LoadAndParseConfig();
            BuildArtboardFrame(theme);
            RebuildLayers(theme);
        }

        private void LoadAndParseConfig()
        {
            _cachedTemplateJson = Config?.CustomTemplate;
            _configData = CompositePanelConfig.FromJson(_cachedTemplateJson);
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
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            _artboardAlphaGroup.alpha = Mathf.Clamp01(_configData.PanelOpacity);
            _artboardBgImage.color = style.GetSurfaceColor(SurfaceStyleRole.PanelDeep, theme);
            _artboardOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);
            _artboardOutline.effectDistance = new Vector2(1f * s, 1f * s);
        }

        private void RebuildLayers(ThemeConfig theme)
        {
            // 清理旧图层与控件
            for (int i = 0; i < _runtimeLayers.Count; i++)
            {
                if (_runtimeLayers[i]?.RootGo != null)
                {
                    Destroy(_runtimeLayers[i].RootGo);
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
                GameObject layerGo = new GameObject(elem.LayerId, typeof(RectTransform));
                layerGo.transform.SetParent(transform, false);
                RectTransform layerRt = layerGo.GetComponent<RectTransform>();

                layerRt.anchorMin = new Vector2(0.5f, 0.5f);
                layerRt.anchorMax = new Vector2(0.5f, 0.5f);
                layerRt.pivot = new Vector2(0.5f, 0.5f);
                layerRt.anchoredPosition = new Vector2(elem.X * s, elem.Y * s);
                layerRt.sizeDelta = new Vector2(elem.Width * s, elem.Height * s);
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
            string proto = (elem.PrototypeId ?? "").ToLowerInvariant();

            // 1. 读数盒 (Readout)
            if (proto.Contains("readout") || proto.Contains("speed") || proto.Contains("alt") || proto.Contains("digit"))
            {
                // 背景槽
                GameObject bgGo = UIFactory.CreatePanel(item.RootGo.transform, "SlotBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                var outline = bgGo.AddComponent<Outline>();
                outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);
                RectTransform bgRt = bgGo.GetComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

                // 标题标签 (TopLeft)
                string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(bgGo.transform, "Title", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform tRt = item.TitleText.rectTransform;
                tRt.anchorMin = new Vector2(0f, 0.5f); tRt.anchorMax = new Vector2(1f, 1f);
                tRt.offsetMin = new Vector2(6f * s, 0f); tRt.offsetMax = new Vector2(-6f * s, -4f * s);

                // 主读数大字 (BottomLeft/Center)
                item.ValueText = UIFactory.CreateText(bgGo.transform, "Value", "---", Mathf.RoundToInt(15f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                item.ValueText.fontStyle = FontStyle.Bold;
                RectTransform vRt = item.ValueText.rectTransform;
                vRt.anchorMin = new Vector2(0f, 0f); vRt.anchorMax = new Vector2(0.7f, 0.65f);
                vRt.offsetMin = new Vector2(6f * s, 4f * s); vRt.offsetMax = Vector2.zero;

                // 单位角标 (BottomRight)
                string unitStr = elem.Unit ?? "";
                item.UnitText = UIFactory.CreateText(bgGo.transform, "Unit", unitStr, Mathf.RoundToInt(8f * s), TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.Unit, theme));
                RectTransform uRt = item.UnitText.rectTransform;
                uRt.anchorMin = new Vector2(0.7f, 0f); uRt.anchorMax = new Vector2(1f, 0.65f);
                uRt.offsetMin = Vector2.zero; uRt.offsetMax = new Vector2(-6f * s, 4f * s);

                var ctrl = new WidgetReadoutControl(this, elem.LayerId, elem.Name, item.RootGo, bgGo.GetComponent<Image>(), outline, item.TitleText, item.ValueText, item.UnitText, elem.Token, elem.Title, elem.Unit);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            // 2. 线性计量槽 (Linear Bar)
            else if (proto.Contains("bar") || proto.Contains("linear") || proto.Contains("gauge") || proto.Contains("thr"))
            {
                // 底槽
                GameObject trackGo = UIFactory.CreatePanel(item.RootGo.transform, "Track", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Slot, theme));
                var outline = trackGo.AddComponent<Outline>();
                outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);
                RectTransform tRt = trackGo.GetComponent<RectTransform>();
                tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
                tRt.offsetMin = Vector2.zero; tRt.offsetMax = Vector2.zero;

                // 填充条
                GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Fill", Vector2.zero, Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
                item.BarFillImage = fillGo.GetComponent<Image>();
                item.BarFillImage.type = Image.Type.Filled;
                item.BarFillImage.fillMethod = Image.FillMethod.Horizontal;
                item.BarFillImage.fillAmount = 0.5f;

                RectTransform fRt = fillGo.GetComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
                fRt.offsetMin = Vector2.zero; fRt.offsetMax = Vector2.zero;

                // 标题标签
                string titleStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(trackGo.transform, "Title", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                RectTransform lblRt = item.TitleText.rectTransform;
                lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
                lblRt.offsetMin = new Vector2(6f * s, 0f); lblRt.offsetMax = new Vector2(-6f * s, 0f);

                var ctrl = new WidgetLinearBarControl(this, elem.LayerId, elem.Name, item.RootGo, trackGo.GetComponent<Image>(), fillGo.GetComponent<Image>(), elem.Token, elem.MinValue, elem.MaxValue, item.Rt.sizeDelta.x, false)
                {
                    CautionThreshold = elem.CautionThreshold,
                    WarningThreshold = elem.WarningThreshold,
                    MeterRole = MeterStyleRole.Primary
                };
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            // 3. 状态光字牌 (Annunciator)
            else if (proto.Contains("annunciator") || proto.Contains("lamp") || proto.Contains("gear") || proto.Contains("brake"))
            {
                GameObject lampGo = UIFactory.CreatePanel(item.RootGo.transform, "LampBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                item.LampImage = lampGo.GetComponent<Image>();
                var outline = lampGo.AddComponent<Outline>();
                outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);

                RectTransform lRt = lampGo.GetComponent<RectTransform>();
                lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
                lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;

                string labelStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(lampGo.transform, "Label", labelStr, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                item.TitleText.fontStyle = FontStyle.Bold;
                RectTransform txtRt = item.TitleText.rectTransform;
                txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;

                var ctrl = new WidgetAnnunciatorControl(this, elem.LayerId, elem.Name, item.RootGo, item.LampImage, outline, item.TitleText, labelStr);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            // 4. 机载系统动作开关 (System Switch / Action Button)
            else if (proto.Contains("action") || proto.Contains("switch") || proto.Contains("btn") || proto.Contains("rcs") || proto.Contains("sas"))
            {
                GameObject btnGo = UIFactory.CreatePanel(item.RootGo.transform, "BtnBg", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                Button btn = btnGo.AddComponent<Button>();
                item.ActionBtn = btn;
                var outline = btnGo.AddComponent<Outline>();
                outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);

                RectTransform bRt = btnGo.GetComponent<RectTransform>();
                bRt.anchorMin = Vector2.zero; bRt.anchorMax = Vector2.one;
                bRt.offsetMin = Vector2.zero; bRt.offsetMax = Vector2.zero;

                // 左侧状态 LED 指示小条
                GameObject ledGo = UIFactory.CreatePanel(btnGo.transform, "Led", new Vector2(3f * s, 0f), Vector2.zero, style.GetTextColor(TextStyleRole.Muted, theme));
                item.ButtonLed = ledGo.GetComponent<Image>();
                RectTransform ledRt = ledGo.GetComponent<RectTransform>();
                ledRt.anchorMin = new Vector2(0f, 0.15f); ledRt.anchorMax = new Vector2(0f, 0.85f);
                ledRt.pivot = new Vector2(0f, 0.5f);
                ledRt.sizeDelta = new Vector2(3f * s, 0f);
                ledRt.anchoredPosition = new Vector2(3f * s, 0f);

                // 按钮文案
                string labelStr = !string.IsNullOrEmpty(elem.Title) ? elem.Title : elem.Name;
                item.TitleText = UIFactory.CreateText(btnGo.transform, "Label", labelStr, Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                item.TitleText.fontStyle = FontStyle.Bold;
                RectTransform txtRt = item.TitleText.rectTransform;
                txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(6f * s, 0f); txtRt.offsetMax = Vector2.zero;

                var ctrl = new AvionicsSystemSwitchControl(this, elem.LayerId, elem.Name, item.RootGo, btn, btnGo.GetComponent<Image>(), outline, item.TitleText, item.ButtonLed, elem.ActionType);
                item.BoundControl = ctrl;
                this.Controls.Register(ctrl);
            }
            // 5. 结构修饰件与通用容器
            else
            {
                GameObject shapeGo = UIFactory.CreatePanel(item.RootGo.transform, "Shape", item.Rt.sizeDelta, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
                var outline = shapeGo.AddComponent<Outline>();
                outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);

                RectTransform sRt = shapeGo.GetComponent<RectTransform>();
                sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
                sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;

                Text txt = null;
                if (!string.IsNullOrEmpty(elem.Title))
                {
                    txt = UIFactory.CreateText(shapeGo.transform, "StaticText", elem.Title, Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
                    RectTransform txtRt = txt.rectTransform;
                    txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
                    txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
                }

                var ctrl = new StructuralShapeControl(this, elem.LayerId, elem.Name, item.RootGo, shapeGo.GetComponent<Image>(), outline, txt);
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
            if (curTpl != _cachedTemplateJson)
            {
                LoadAndParseConfig();
                _needsLayerRebuild = true;
            }

            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_needsLayerRebuild)
            {
                _needsLayerRebuild = false;
                RebuildLayers(WidgetStyleManager.ResolveTheme(null));
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

                // 2. 计量槽填充更新
                if (layer.BarFillImage != null)
                {
                    if (layer.CachedFloat.Update(elemState.NormalizedFraction))
                    {
                        layer.BarFillImage.SetFillAmountSafe(elemState.NormalizedFraction);
                    }
                }

                // 3. 告警光字牌更新
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

                // 4. 按钮状态 LED 更新
                if (layer.ButtonLed != null)
                {
                    if (layer.CachedActive.Update(elemState.IsActive))
                    {
                        Color ledCol = elemState.IsActive
                            ? style.GetTextColor(TextStyleRole.Accent, theme)
                            : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint);
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
                RectTransform.sizeDelta = pixelSize;
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
            _cachedTemplateJson = Config?.CustomTemplate;
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
                    layer.Rt.anchoredPosition = new Vector2(elem.X * s, elem.Y * s) + offset;
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
                _cachedTemplateJson = Config.CustomTemplate;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
