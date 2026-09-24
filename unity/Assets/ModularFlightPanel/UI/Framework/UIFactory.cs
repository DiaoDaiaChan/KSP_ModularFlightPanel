using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace ModularFlightPanel.UI
{
    public static class UIFactory
    {
        public static Font DefaultFont { get; private set; }

        static UIFactory()
        {
            try
            {
                // 优先加载高分辨率现代无衬线航电字体 (如 Windows 原生 Segoe UI 或 Calibri)，显著提升小字号排版清晰度
                DefaultFont = Font.CreateDynamicFontFromOSFont(new string[] { "Segoe UI Semibold", "Segoe UI", "Calibri", "Arial" }, 16);
            }
            catch { }

            if (DefaultFont == null)
            {
                DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            if (DefaultFont == null)
            {
                Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                if (fonts != null && fonts.Length > 0)
                {
                    DefaultFont = fonts[0];
                }
            }
        }

        /// <summary>
        /// 获取当前屏幕相对于 1080p 基准的动态分辨率缩放比率 (响应式适配 2K / 4K / 宽屏)
        /// </summary>
        public static float GetScreenDpiScale()
        {
            float baseHeight = 1080f;
            float currentHeight = Mathf.Max(Screen.height, 720f);
            return currentHeight / baseHeight;
        }

        public static Func<float> CustomNavballUiScaleProvider = null;

        /// <summary>
        /// 读取 KSP 原生 UI_SCALE_NAVBALL 与 UI_SCALE 设定，并结合屏幕 DPI 自动计算权威姿态球界面尺寸比例
        /// </summary>
        public static float GetKspNavballUiScale()
        {
            if (CustomNavballUiScaleProvider != null)
            {
                try { return Mathf.Clamp(CustomNavballUiScaleProvider(), 0.5f, 2.5f); } catch { }
            }

            try
            {
                Type gameSettingsType = Type.GetType("GameSettings, Assembly-CSharp");
                if (gameSettingsType != null)
                {
                    var navField = gameSettingsType.GetField("UI_SCALE_NAVBALL", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var uiField = gameSettingsType.GetField("UI_SCALE", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (navField != null && uiField != null)
                    {
                        float nav = Convert.ToSingle(navField.GetValue(null));
                        float ui = Convert.ToSingle(uiField.GetValue(null));
                        float scale = nav * ui;
                        if (scale > 0.05f) return Mathf.Clamp(scale, 0.5f, 2.5f);
                    }
                }
            }
            catch { }

            return 1.0f;
        }

        public static GameObject CreatePanel(Transform parent, string name, Vector2 size, Vector2 anchoredPos, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            Image img = go.GetComponent<Image>();
            img.color = color;

            return go;
        }

        public static GameObject CreatePanel(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Color color, Color borderColor, float borderWidth = 1f)
        {
            GameObject go = CreatePanel(parent, name, size, anchoredPos, color);
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(borderWidth, borderWidth);
            return go;
        }

        public static GameObject CreatePanel(Transform parent, string name, Vector2 size, Vector2 anchoredPos, Color color, Material customMaterial)
        {
            GameObject go = CreatePanel(parent, name, size, anchoredPos, color);
            if (customMaterial != null)
            {
                Image img = go.GetComponent<Image>();
                if (img != null) img.material = customMaterial;
            }
            return go;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color, Material customMaterial = null, bool addShadow = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text txt = go.GetComponent<Text>();
            txt.font = DefaultFont;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            if (customMaterial != null)
            {
                txt.material = customMaterial;
            }

            if (addShadow)
            {
                // 抗炫光高对比微投影：无论直视恒星太阳还是明亮大气背景，均确保 100% 锐利可读
                Shadow shadow = go.AddComponent<Shadow>();
                Color shadowCol = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
                shadowCol.a = 0.78f;
                shadow.effectColor = shadowCol;
                shadow.effectDistance = new Vector2(1f, -1f);
                shadow.useGraphicAlpha = true;
            }

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160f, 40f);

            return txt;
        }

        /// <summary>
        /// 航电等宽数字对齐工具：确保高频跳变的数字在显示时具有稳定的字符占位，消除视觉左右微颤
        /// </summary>
        public static string FormatTabular(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // 规范化减号为数学负号 (U+2212)，在航电排版中与等宽阿拉伯数字宽度严格一致
            return text.Replace('-', '−');
        }

        public static Button CreateButton(Transform parent, string name, Vector2 size, Vector2 anchoredPos, UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            Image img = go.GetComponent<Image>();
            img.color = WidgetStyleManager.Surface(SurfaceStyleRole.Panel);

            Button btn = go.GetComponent<Button>();
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            return btn;
        }

        public static Button CreateCockpitButton(Transform parent, string name, string label, Vector2 size,
            Vector2 anchoredPos, Color background, Color border, Color textColor, UnityAction onClick)
        {
            GameObject go = CreatePanel(parent, name, size, anchoredPos, background, border, 1f);
            Button btn = go.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, WidgetStyleManager.NeutralOpaque, 0.16f);
            colors.pressedColor = Color.Lerp(background, WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep), 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.05f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            Text text = CreateText(go.transform, name + "_Label", label, Mathf.RoundToInt(size.y * 0.34f),
                TextAnchor.MiddleCenter, textColor);
            RectTransform textRt = text.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(2f, 0f);
            textRt.offsetMax = new Vector2(-2f, 0f);
            return btn;
        }

        public static void ApplyCockpitChrome(GameObject go, Color fill, Color border, float scale)
        {
            Image image = go.GetComponent<Image>();
            if (image != null) image.color = fill;

            Outline outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
            outline.effectColor = border;
            outline.effectDistance = new Vector2(1f * scale, 1f * scale);

            Shadow shadow = go.GetComponent<Shadow>() ?? go.AddComponent<Shadow>();
            Color shadowCol = border;
            shadowCol.a = 0.25f;
            shadow.effectColor = shadowCol;
            shadow.effectDistance = new Vector2(0f, -2f * scale);
            shadow.useGraphicAlpha = true;
        }
    }
}
