using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Rendering;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// 权威导航参考系与坐标系高反差航电小组件 (Navigation Reference Frame Indicator Widget - Streamlined)
    /// 极简纯粹航电卡片设计：仅保留专属矢量图标与权威参考系全称，去除繁杂药丸与底行信息。
    /// 支持单击循环切换参考系与右键唤起 Principia 原生参考系窗口。
    /// 100% 遵照 MFP 标准：0 硬编码、0 颜色字面量、通配符双驱动。
    /// </summary>
    [FlightWidget("reference_frame", "ref_frame", "frame_indicator", "nav_frame", Category = WidgetCategory.Navigation, DisplayName = "REF FRAME 导航参考系指示卡", Description = "极简权威导航参考系指示卡：矢量图标与权威参考系名称。支持单击循环切换与右键打开 Principia 窗口。", DefaultWidgetId = "nav.reference_frame", DefaultX = -300f, DefaultY = 200f, IsSingleton = true, ExactIds = new[] { "nav.reference_frame", "nav.ref_frame", "core.reference_frame" })]
    public class ReferenceFrameWidget : BaseFlightWidget, IPointerClickHandler
    {
        public override Vector2 BaseSize => new Vector2(100f, 32f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 声明式微控件
        public TextWidget FrameTitle = TextWidget.Title(I18n.Tr("WIDGET_NAV_FRAME_SURFACE", "表面"));

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        // 左侧参考系矢量微标徽章
        private GameObject _iconBox;
        private Image _iconBoxBg;
        private Outline _iconBoxOutline;
        private RawImage _iconRawImage;

        // 权威参考系主名称
        private Text _frameTitleText;

        // 通配符通道与模板配置
        private string _frameToken = "{FRAME}";
        private string _typeToken = "{FRAME:TYPE}";

        // 尺寸与排版参数 (支持按文字宽度全自动适应)
        private float _cardHeight = 32f;
        private float _padLeft = 5f;
        private float _padRight = 7f;
        private float _spacing = 6f;
        private float _iconBoxSize = 26f;
        private RectTransform _titleRt;

        // 脏检查与平滑缓存
        private string _lastCategory = string.Empty;
        private string _lastTitle = string.Empty;

        public static Action OnCycleReferenceFrameAction;
        public static Action OnToggleReferenceFrameWindowAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 初始化排版缩放参数
            _cardHeight = 32f * s;
            _padLeft = 5f * s;
            _padRight = 7f * s;
            _spacing = 6f * s;
            _iconBoxSize = 26f * s;

            // 2. 底板卡片与边框 (由基类 AutoCreateCardFrame 统一托管)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            _frameToken = GetTemplateChannel(new[] { "FRAME", "NAME", "TITLE" }, _frameToken);
            _typeToken = GetTemplateChannel(new[] { "TYPE", "CATEGORY" }, _typeToken);

            // 3. 左侧图标插槽徽章 (左对齐，垂直居中)
            _iconBox = UIFactory.CreatePanel(transform, "Frame_Icon_Box",
                new Vector2(_iconBoxSize, _iconBoxSize), Vector2.zero, Color.clear);
            RectTransform iconBoxRt = _iconBox.GetComponent<RectTransform>();
            iconBoxRt.anchorMin = new Vector2(0f, 0.5f);
            iconBoxRt.anchorMax = new Vector2(0f, 0.5f);
            iconBoxRt.pivot = new Vector2(0f, 0.5f);
            iconBoxRt.anchoredPosition = new Vector2(_padLeft, 0f);

            _iconBoxBg = _iconBox.GetComponent<Image>();
            _iconBoxOutline = _iconBox.AddComponent<Outline>();
            _iconBoxOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_iconBoxBg, _iconBoxOutline, CardStyleRole.SubtleSlot, theme);

            // 矢量图集 RawImage (20 x 20 px)
            _iconRawImage = CreateChild<RawImage>("Frame_Vector_Icon", _iconBox.transform, new Vector2(24f * s, 24f * s), Vector2.zero);
            _iconRawImage.texture = ReferenceFrameIconAtlasGenerator.GetAtlas();
            _iconRawImage.uvRect = ReferenceFrameIconAtlasGenerator.GetIconUv(ReferenceFrameIconAtlasGenerator.INDEX_INERTIAL);
            _iconRawImage.color = style.GetTextColor(TextStyleRole.Cardinal, theme);

            // 4. 右侧权威参考系名称 (左对齐，紧随图标，自适应宽度)
            if (FrameTitle != null && FrameTitle.TextComponent != null)
            {
                FrameTitle.Text = I18n.Tr("WIDGET_NAV_FRAME_SURFACE", "表面");
                FrameTitle.SetRole(TextStyleRole.PrimaryValue);
                _frameTitleText = FrameTitle.TextComponent;
                _frameTitleText.fontStyle = FontStyle.Bold;
                _frameTitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _frameTitleText.verticalOverflow = VerticalWrapMode.Truncate;
                _titleRt = _frameTitleText.GetComponent<RectTransform>();
                _titleRt.anchorMin = new Vector2(0f, 0.5f);
                _titleRt.anchorMax = new Vector2(0f, 0.5f);
                _titleRt.pivot = new Vector2(0f, 0.5f);
                _titleRt.anchoredPosition = new Vector2(_padLeft + _iconBoxSize + _spacing, 0f);
            }
            else
            {
                _frameTitleText = UIFactory.CreateText(transform, "Frame_Title", I18n.Tr("WIDGET_NAV_FRAME_SURFACE", "表面"), Mathf.RoundToInt(11f * s),
                    TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                _frameTitleText.fontStyle = FontStyle.Bold;
                _frameTitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _frameTitleText.verticalOverflow = VerticalWrapMode.Truncate;

                _titleRt = _frameTitleText.GetComponent<RectTransform>();
                _titleRt.anchorMin = new Vector2(0f, 0.5f);
                _titleRt.anchorMax = new Vector2(0f, 0.5f);
                _titleRt.pivot = new Vector2(0f, 0.5f);
                _titleRt.anchoredPosition = new Vector2(_padLeft + _iconBoxSize + _spacing, 0f);
            }

            // 立即计算初始自适应宽度，消除右侧空白
            AdjustCardWidth("SURFACE");

            // 5. 挂载整卡交互监听与提示
            Button wholeCardBtn = gameObject.AddComponent<Button>();
            wholeCardBtn.transition = Selectable.Transition.None;
            wholeCardBtn.onClick.AddListener(OnCycleClicked);

            gameObject.SetTooltip(
                I18n.Tr("TOOLTIP_REF_FRAME_TITLE", "导航参考系 (Reference Frame)"),
                I18n.Tr("TOOLTIP_REF_FRAME_DESC", "显示当前绘图与速度解算参考系。左键循环切换参考系，右键打开/关闭 Principia 参考系选择器窗口。"),
                I18n.Tr("TOOLTIP_REF_FRAME_HOTKEY", "[L-Click] 切换 [R-Click] 窗口")
            );

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, t)));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "icon_box", "参考系图标徽章", _iconBox, (t) => ApplyCard(_iconBoxBg, _iconBoxOutline, CardStyleRole.SubtleSlot, t)));
            this.Controls.Register(new WidgetReadoutControl("frame_title", "权威参考系全称", _frameTitleText != null ? _frameTitleText.gameObject : null, _frameTitleText, null, TextStyleRole.PrimaryValue));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                if (OnToggleReferenceFrameWindowAction != null)
                {
                    OnToggleReferenceFrameWindowAction.Invoke();
                }
                else
                {
                    OnCycleClicked();
                }
            }
            else
            {
                OnCycleClicked();
            }
        }

        private void OnCycleClicked()
        {
            if (OnCycleReferenceFrameAction != null)
            {
                OnCycleReferenceFrameAction.Invoke();
            }
            else
            {
                FlightTelemetryContext.Current?.CycleSpeedMode();
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);

            ApplyCard(_iconBoxBg, _iconBoxOutline, CardStyleRole.SubtleSlot, theme);
            if (FrameTitle != null) FrameTitle.SetRole(TextStyleRole.PrimaryValue);
            else ApplyText(_frameTitleText, TextStyleRole.PrimaryValue, theme);

            UpdateCategoryVisuals(_lastCategory, theme);
            AdjustCardWidth(_lastTitle);

            this.Controls.ApplyThemeToControls(theme);
        }

        private void AdjustCardWidth(string title)
        {
            if (_titleRt == null || _frameTitleText == null || RectTransform == null) return;
            float s = CurrentDpiScale;
            float textW = _frameTitleText.preferredWidth;
            float targetW = _padLeft + _iconBoxSize + _spacing + textW + _padRight;

            // 限幅保护：最小 76px，最大 260px，消除右侧大片空白
            float minW = 76f * s;
            float maxW = 260f * s;
            if (targetW < minW) targetW = minW;
            if (targetW > maxW)
            {
                targetW = maxW;
                textW = targetW - (_padLeft + _iconBoxSize + _spacing + _padRight);
            }

            _titleRt.sizeDelta = new Vector2(textW + 4f * s, _cardHeight - 4f * s);
            RectTransform.sizeDelta = new Vector2(targetW, _cardHeight);
        }

        private void UpdateCategoryVisuals(string category, ThemeConfig theme)
        {
            if (theme == null) theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            int iconIndex = ReferenceFrameIconAtlasGenerator.GetIconIndex(category);

            TextStyleRole textRole;
            switch (iconIndex)
            {
                case ReferenceFrameIconAtlasGenerator.INDEX_SURFACE:
                case ReferenceFrameIconAtlasGenerator.INDEX_BODY_FIXED:
                    textRole = TextStyleRole.Accent;
                    break;

                case ReferenceFrameIconAtlasGenerator.INDEX_ORBITAL:
                    textRole = TextStyleRole.Cardinal;
                    break;

                case ReferenceFrameIconAtlasGenerator.INDEX_LAGRANGE:
                    textRole = TextStyleRole.Accent;
                    break;

                case ReferenceFrameIconAtlasGenerator.INDEX_TARGET:
                    textRole = TextStyleRole.Warning;
                    break;

                case ReferenceFrameIconAtlasGenerator.INDEX_INERTIAL:
                default:
                    textRole = TextStyleRole.Cardinal;
                    break;
            }

            if (_iconRawImage != null)
            {
                _iconRawImage.uvRect = ReferenceFrameIconAtlasGenerator.GetIconUv(iconIndex);
                _iconRawImage.color = style.GetTextColor(textRole, theme);
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 动态评估当前参考系类型类别 (INERTIAL / SURFACE / ORBIT / LAGRANGE / TARGET)
            string category = TelemetryTokenEngine.Evaluate(_typeToken, telemetry);
            if (string.IsNullOrEmpty(category) || category == "---")
            {
                if (NavBallHookService.Provider != null && !string.IsNullOrEmpty(NavBallHookService.Provider.ReferenceFrameCategory))
                {
                    category = NavBallHookService.Provider.ReferenceFrameCategory;
                }
                else
                {
                    category = !string.IsNullOrEmpty(telemetry.SpeedModeName) ? telemetry.SpeedModeName.ToUpperInvariant() : "ORBIT";
                }
            }

            if (category != _lastCategory)
            {
                _lastCategory = category;
                UpdateCategoryVisuals(category, ThemeManager.Instance?.CurrentTheme);
            }

            // 2. 动态评估参考系全称标题 (如 "HELIOCENTRIC INERTIAL", "KERBIN SURFACE", "ORBIT")
            string title = TelemetryTokenEngine.Evaluate(_frameToken, telemetry);
            if (string.IsNullOrEmpty(title) || title == "---")
            {
                if (NavBallHookService.Provider != null && !string.IsNullOrEmpty(NavBallHookService.Provider.FrameName))
                {
                    title = NavBallHookService.Provider.FrameName;
                }
                else
                {
                    title = telemetry.SpeedModeName ?? category;
                }
            }

            if (title != _lastTitle)
            {
                _lastTitle = title;
                SetTextIfChanged(_frameTitleText, title);
                AdjustCardWidth(title);
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
