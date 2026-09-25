using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 自定义通配符文本飞行卡片组件 (Custom Token Text Card Widget)
    /// 玩家可以在配置中写入任意参数模板 (例如 "{SPD:SURF:F1} m/s | Q: {Q:F2} | TWR: {TWR:F2}")，
    /// 标题与内容全量由 TelemetryTokenEngine 实时求值并具备脏检查保护。
    /// </summary>
    [FlightWidget("custom_token", "custom_text", "custom", Category = WidgetCategory.Gauges, DisplayName = "多通道遥测动态卡片", Description = "支持任意遥测通配符模板的高对比度动态数据卡片。", DefaultWidgetId = "custom.telemetry_card", DefaultX = 0f, DefaultY = 0f)]
    public class CustomTokenTextWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private WidgetReadoutControl _titleControl;
        private WidgetReadoutControl _contentControl;
        private Image _cardBgImage;
        private Outline _cardOutline;
        private string _titleTemplate = "TELEMETRY";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Vector2 cardSize = new Vector2(210f * s, 56f * s);
            RectTransform.sizeDelta = cardSize;

            _cardBgImage = gameObject.AddComponent<Image>();
            _cardBgImage.color = Color.clear;

            _cardOutline = gameObject.AddComponent<Outline>();
            _cardOutline.effectDistance = new Vector2(1.5f * s, 1.5f * s);

            _titleTemplate = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName : "TELEMETRY";

            // 标题文本
            int titleSize = Mathf.RoundToInt(10f * s);
            Text titleText = UIFactory.CreateText(transform, "Card_Title", _titleTemplate, titleSize, TextAnchor.UpperLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trt = titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(cardSize.x - 12f * s, 16f * s);
            trt.anchoredPosition = new Vector2(6f * s, (cardSize.y * 0.5f) - 10f * s);

            // 内容文本
            int contentSize = Mathf.RoundToInt(13f * s);
            Text contentValueText = UIFactory.CreateText(transform, "Card_Content", "---", contentSize, TextAnchor.LowerLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform crt = contentValueText.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(cardSize.x - 12f * s, 30f * s);
            crt.anchoredPosition = new Vector2(6f * s, -(cardSize.y * 0.5f) + 16f * s);

            // 注册子控件至标准化管理器并初始化
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_cardBgImage, _cardOutline, CardStyleRole.Normal, t)));
            _titleControl = new WidgetReadoutControl("card_title", "卡片标题", titleText.gameObject, titleText, null, TextStyleRole.Label);
            _contentControl = new WidgetReadoutControl("card_content", "动态内容", contentValueText.gameObject, contentValueText, null, TextStyleRole.PrimaryValue);
            this.Controls.Register(_titleControl);
            this.Controls.Register(_contentControl);

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            _titleControl.SetValue(evalTitle);

            string tpl = Config?.CustomTemplate;
            string eval = !string.IsNullOrEmpty(tpl) ? TelemetryTokenEngine.Evaluate(tpl, telemetry) : "---";
            _contentControl.SetValue(eval);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
