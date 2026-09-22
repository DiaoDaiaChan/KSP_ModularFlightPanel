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
            return kspScale * GetScreenDpiScale();
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
    }
}
