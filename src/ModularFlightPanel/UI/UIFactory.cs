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
            DefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        /// <summary>
        /// 读取 KSP 原生 UI_SCALE_NAVBALL 与 UI_SCALE 设定，并结合屏幕 DPI 自动计算权威姿态球界面尺寸比例
        /// </summary>
        public static float GetKspNavballUiScale()
        {
            float kspScale = 1.0f;
            try
            {
                kspScale = GameSettings.UI_SCALE_NAVBALL * GameSettings.UI_SCALE;
            }
            catch
            {
                kspScale = 1.0f;
            }

            if (kspScale <= 0.05f) kspScale = 1.0f;
            return Mathf.Clamp(kspScale, 0.5f, 2.5f);
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

        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color)
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

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160f, 40f);

            return txt;
        }

        public static Button CreateButton(Transform parent, string name, Vector2 size, Vector2 anchoredPos, UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.1f, 0.15f, 0.2f, 0.7f);

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
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.16f);
            colors.pressedColor = Color.Lerp(background, Color.black, 0.18f);
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
            shadow.effectColor = new Color(border.r, border.g, border.b, 0.25f);
            shadow.effectDistance = new Vector2(0f, -2f * scale);
            shadow.useGraphicAlpha = true;
        }
    }
}
