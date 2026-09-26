using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电等宽防抖数显窗 (Avionics Tabular Value Display Box)
    /// 统一治理高频跳变遥测数值的排版布局：
    /// 1. 自动数学负号对齐与等宽规范化 (FormatTabular)，彻底消除文字左右微颤抖动；
    /// 2. 主数值大字与右上/右侧小单位合璧，杜绝文字重叠；
    /// 3. 支持语义色阶跃（正常/高亮/警告/警报）。
    /// </summary>
    public class AvionicsValueBox : MonoBehaviour
    {
        public Image BackgroundImage;
        public Outline BorderOutline;
        public Text TitleLabel;
        public Text ValueText;
        public Text UnitLabel;

        private TextStyleRole _currentRole = TextStyleRole.PrimaryValue;
        public TextStyleRole VisualRole => _currentRole;

        private string _lastRawValue = null;
        private string _lastUnit = null;

        public void Initialize(string title, string initialValue, string unit, Vector2 size, ThemeConfig theme = null)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = UIFactory.GetScreenDpiScale();

            // 1. 嵌入式暗晶卡片底槽
            BackgroundImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            BorderOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            Color borderCol = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);
            BorderOutline.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Ghost);
            BorderOutline.effectDistance = new Vector2(1f * s, 1f * s);

            RectTransform rt = GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = size;

            // 2. 标题微标 (左上)
            if (!string.IsNullOrEmpty(title))
            {
                int titleFontSize = Mathf.Max(6, Mathf.RoundToInt(size.y * 0.28f));
                TitleLabel = UIFactory.CreateText(transform, "Title", title, titleFontSize, TextAnchor.UpperLeft,
                    WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                RectTransform titleRt = TitleLabel.GetComponent<RectTransform>();
                titleRt.anchorMin = new Vector2(0f, 0.5f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.offsetMin = new Vector2(4f * s, 0f);
                titleRt.offsetMax = new Vector2(-4f * s, -2f * s);
            }

            // 3. 中央主数值 (大字等宽)
            int valFontSize = Mathf.Max(8, Mathf.RoundToInt(size.y * 0.48f));
            ValueText = UIFactory.CreateText(transform, "Value", UIFactory.FormatTabular(initialValue ?? "--"), valFontSize,
                TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            ValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = ValueText.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0f, 0f);
            valRt.anchorMax = new Vector2(0.78f, TitleLabel != null ? 0.65f : 1f);
            valRt.offsetMin = new Vector2(4f * s, 2f * s);
            valRt.offsetMax = new Vector2(0f, 0f);

            // 4. 单位角标 (右下)
            if (!string.IsNullOrEmpty(unit))
            {
                int unitFontSize = Mathf.Max(6, Mathf.RoundToInt(size.y * 0.26f));
                UnitLabel = UIFactory.CreateText(transform, "Unit", unit, unitFontSize, TextAnchor.LowerRight,
                    theme.AccentSecondary);
                RectTransform unitRt = UnitLabel.GetComponent<RectTransform>();
                unitRt.anchorMin = new Vector2(0.72f, 0f);
                unitRt.anchorMax = new Vector2(1f, 0.65f);
                unitRt.offsetMin = new Vector2(0f, 2f * s);
                unitRt.offsetMax = new Vector2(-4f * s, 0f);
            }

            _lastRawValue = initialValue;
            _lastUnit = unit;

            ApplyTheme(theme);
        }

        public void SetValue(string valueText, string unit = null)
        {
            if (valueText != _lastRawValue && ValueText != null)
            {
                _lastRawValue = valueText;
                BaseFlightWidget.SetTextIfChanged(ValueText, UIFactory.FormatTabular(valueText));
            }

            if (unit != null && unit != _lastUnit && UnitLabel != null)
            {
                _lastUnit = unit;
                BaseFlightWidget.SetTextIfChanged(UnitLabel, unit);
            }
        }

        public void SetValue(double val, string format = "N1", string unit = null)
        {
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                SetValue("--", unit);
            }
            else
            {
                string str = CacheManager.Instance.FastDouble(gameObject.name + "_val", val, format, 0.05);
                SetValue(str, unit);
            }
        }

        public void SetRole(TextStyleRole role)
        {
            if (_currentRole != role)
            {
                _currentRole = role;
                ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
                if (ValueText != null && theme != null)
                {
                    ValueText.color = WidgetStyleManager.Instance.GetTextColor(role, theme);
                }
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            if (BackgroundImage != null)
            {
                BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }

            if (BorderOutline != null)
            {
                BorderOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            if (TitleLabel != null)
            {
                TitleLabel.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
            }

            if (ValueText != null)
            {
                ValueText.color = WidgetStyleManager.Instance.GetTextColor(_currentRole, theme);
            }

            if (UnitLabel != null)
            {
                UnitLabel.color = theme.AccentSecondary;
            }
        }
    }
}
