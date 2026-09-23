using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 自定义通配符文本飞行卡片组件
    /// 玩家可以在配置中写入任意参数模板 (例如 "{SPD:SURF:F1} m/s | Q: {Q:F2} | TWR: {TWR:F2}")
    /// 系统实时通过 TelemetryTokenEngine 计算并呈现
    /// </summary>
    public class CustomTokenTextWidget : BaseFlightWidget
    {
        private Text _titleText;
        private Text _contentValueText;
        private Image _cardBgImage;
        private Outline _cardOutline;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Vector2 cardSize = new Vector2(210f * CurrentDpiScale, 56f * CurrentDpiScale);
            RectTransform.sizeDelta = cardSize;

            _cardBgImage = gameObject.AddComponent<Image>();
            _cardBgImage.color = theme.FrameBgColor;

            _cardOutline = gameObject.AddComponent<Outline>();
            _cardOutline.effectColor = theme.FrameBorderColor;
            _cardOutline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);

            // 标题文本
            int titleSize = Mathf.RoundToInt(10f * CurrentDpiScale);
            _titleText = UIFactory.CreateText(transform, "Card_Title", config.DisplayName, titleSize, TextAnchor.UpperLeft, theme.AccentSecondary);
            RectTransform trt = _titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(cardSize.x - 12f * CurrentDpiScale, 16f * CurrentDpiScale);
            trt.anchoredPosition = new Vector2(6f * CurrentDpiScale, (cardSize.y * 0.5f) - 10f * CurrentDpiScale);

            // 内容文本 (大号或等宽点阵)
            int contentSize = Mathf.RoundToInt(13f * CurrentDpiScale);
            _contentValueText = UIFactory.CreateText(transform, "Card_Content", "---", contentSize, TextAnchor.LowerLeft, theme.TextPrimaryColor);
            RectTransform crt = _contentValueText.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(cardSize.x - 12f * CurrentDpiScale, 30f * CurrentDpiScale);
            crt.anchoredPosition = new Vector2(6f * CurrentDpiScale, -(cardSize.y * 0.5f) + 16f * CurrentDpiScale);
        }

        private string _lastEvaluatedText = "";

        public override float DefaultUpdateInterval => 0.1f; // 10Hz 通配符卡片求值刷新，消除静止文本的无效每帧重绘

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (string.IsNullOrEmpty(Config?.CustomTemplate))
            {
                if (_lastEvaluatedText != "空模板")
                {
                    _lastEvaluatedText = "空模板";
                    _contentValueText.text = "空模板 (请设置 CustomTemplate)";
                }
                return;
            }

            // 通过通配符引擎实时求值
            string eval = TelemetryTokenEngine.Evaluate(Config.CustomTemplate, telemetry);
            if (eval != _lastEvaluatedText)
            {
                _lastEvaluatedText = eval;
                _contentValueText.text = eval;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_cardBgImage != null) _cardBgImage.color = theme.FrameBgColor;
            if (_cardOutline != null) _cardOutline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.AccentSecondary;
            if (_contentValueText != null) _contentValueText.color = theme.TextPrimaryColor;
        }
    }
}
