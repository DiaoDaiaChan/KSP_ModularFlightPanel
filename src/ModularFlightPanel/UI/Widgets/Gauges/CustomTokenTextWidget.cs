using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 自定义通配符文本飞行卡片组件 (Custom Token Text Card Widget)
    /// 玩家可以在配置中写入任意参数模板 (例如 "{SPD:SURF:F1} m/s | Q: {Q:F2} | TWR: {TWR:F2}")，
    /// 标题与内容全量由 TelemetryTokenEngine 实时求值并具备脏检查保护。
    /// </summary>
    public class CustomTokenTextWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Text _titleText;
        private Text _contentValueText;
        private Image _cardBgImage;
        private Outline _cardOutline;

        private string _titleTemplate = "TELEMETRY";
        private string _lastTitleText = string.Empty;
        private string _lastEvaluatedText = string.Empty;

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
            _titleText = UIFactory.CreateText(transform, "Card_Title", _titleTemplate, titleSize, TextAnchor.UpperLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trt = _titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(cardSize.x - 12f * s, 16f * s);
            trt.anchoredPosition = new Vector2(6f * s, (cardSize.y * 0.5f) - 10f * s);

            // 内容文本
            int contentSize = Mathf.RoundToInt(13f * s);
            _contentValueText = UIFactory.CreateText(transform, "Card_Content", "---", contentSize, TextAnchor.LowerLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform crt = _contentValueText.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(cardSize.x - 12f * s, 30f * s);
            crt.anchoredPosition = new Vector2(6f * s, -(cardSize.y * 0.5f) + 16f * s);

            ApplyTheme(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            // 1. 标题通配符求值与脏检查
            if (_titleText != null)
            {
                string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
                if (evalTitle != _lastTitleText)
                {
                    _lastTitleText = evalTitle;
                    _titleText.text = evalTitle;
                }
            }

            // 2. 内容通配符模板求值与脏检查
            if (string.IsNullOrEmpty(Config?.CustomTemplate))
            {
                if (_lastEvaluatedText != "---")
                {
                    _lastEvaluatedText = "---";
                    _contentValueText.text = "---";
                }
                return;
            }

            string eval = TelemetryTokenEngine.Evaluate(Config.CustomTemplate, telemetry);
            if (eval != _lastEvaluatedText)
            {
                _lastEvaluatedText = eval;
                _contentValueText.text = eval;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            ApplyCard(_cardBgImage, _cardOutline, CardStyleRole.Normal, theme);
            if (_titleText != null) ApplyText(_titleText, TextStyleRole.Label, theme);
            if (_contentValueText != null) ApplyText(_contentValueText, TextStyleRole.PrimaryValue, theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
