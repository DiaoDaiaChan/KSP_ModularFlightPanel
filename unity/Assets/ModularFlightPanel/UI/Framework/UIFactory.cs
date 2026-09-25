using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    public static class UIFactory
    {
        public static Font DefaultFont { get; private set; }
        public static Font PixelFont { get; private set; }

        static UIFactory()
        {
            try
            {
                // 优先加载清晰锐利、全字重且完美支持中英双语的 Windows 标杆显示字体 (Segoe UI / 微软雅黑 / Arial)，
                // 彻底杜绝可变字体在旧版 Unity 引擎中笔画发虚发灰、小字号粘连与 CJK 缺失模糊的问题
                DefaultFont = Font.CreateDynamicFontFromOSFont(new string[] { "Segoe UI", "Microsoft YaHei", "Arial", "Calibri", "Bahnschrift" }, 16);
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

            try
            {
                // 优先加载等宽硬朗、具备浓厚物理数码管与点阵终端质感的硬件等宽字体 (Consolas / Lucida Console / MS Gothic / Courier New)
                PixelFont = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Lucida Console", "MS Gothic", "Courier New", "SimSun" }, 15);
            }
            catch { }

            if (PixelFont == null)
            {
                PixelFont = DefaultFont;
            }
        }

        /// <summary>
        /// 根据当前或指定主题获取应当生效的字体资产 (平滑矢量 vs 复古点阵像素)
        /// </summary>
        public static Font GetActiveFont(ThemeConfig theme = null)
        {
            theme = theme ?? ThemeManager.Instance?.CurrentTheme;
            if (theme != null && theme.FontStyle == AvionicsFontStyle.RetroPixel && PixelFont != null)
            {
                return PixelFont;
            }
            return DefaultFont ?? PixelFont;
        }

        /// <summary>
        /// 获取动态分辨率缩放比率 (固定返回 1.0f，保证 1:1 绝对物理像素点对点光栅化，避免全屏拉伸与形变)
        /// </summary>
        public static float GetScreenDpiScale()
        {
            return 1.0f;
        }

        public static Func<float> CustomNavballUiScaleProvider = null;

        /// <summary>
        /// 读取 KSP 姿态球比例 (固定返回 1.0f，保持高保真设计尺寸，避免随游戏原生全局缩放拉伸形变)
        /// </summary>
        public static float GetKspNavballUiScale()
        {
            if (CustomNavballUiScaleProvider != null)
            {
                try { return Mathf.Clamp(CustomNavballUiScaleProvider(), 0.5f, 2.5f); } catch { }
            }
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

        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color, Material customMaterial = null, bool addShadow = false, bool alignByGeometry = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text txt = go.GetComponent<Text>();
            txt.font = GetActiveFont();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.alignByGeometry = alignByGeometry;

            if (customMaterial != null)
            {
                txt.material = customMaterial;
            }
            else
            {
                // 默认接入航电抗蚀刻高清文本材质 (若已初始化)
                Material textMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);
                if (textMat != null) txt.material = textMat;
            }

            if (addShadow)
            {
                // 仅在明确指定时开启柔和环境阴影，禁止粗暴的 (1, -1) 偏移造成小字号粘连与糊字
                Shadow shadow = go.AddComponent<Shadow>();
                Color shadowCol = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
                shadowCol.a = 0.50f;
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

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
            Color borderColor = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);

            Image img = go.GetComponent<Image>();
            img.color = WidgetStyleManager.Surface(SurfaceStyleRole.Panel);

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = WidgetStyleManager.Weighted(borderColor, LineWeight.Ghost);
            outline.effectDistance = new Vector2(1f, 1f);

            Button btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            var feedback = go.AddComponent<AvionicsButtonFeedback>();
            feedback.Initialize(btn, img, outline, null, theme);

            return btn;
        }

        public static Button CreateCockpitButton(Transform parent, string name, string label, Vector2 size,
            Vector2 anchoredPos, Color background, Color border, Color textColor, UnityAction onClick, ButtonVisualRole role = ButtonVisualRole.Normal)
        {
            GameObject go = CreatePanel(parent, name, size, anchoredPos, background, border, 1f);
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(onClick);

            Text text = CreateText(go.transform, name + "_Label", label, Mathf.RoundToInt(size.y * 0.34f),
                TextAnchor.MiddleCenter, textColor);
            RectTransform textRt = text.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(2f, 0f);
            textRt.offsetMax = new Vector2(-2f, 0f);

            Image img = go.GetComponent<Image>();
            Outline outline = go.GetComponent<Outline>();

            var feedback = go.AddComponent<AvionicsButtonFeedback>();
            feedback.VisualRole = role;
            feedback.Initialize(btn, img, outline, text, WidgetStyleManager.Instance?.CurrentTheme);

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

        public static AvionicsSegmentedControl CreateSegmentedControl(Transform parent, string name, Vector2 size,
            Vector2 anchoredPos, string[] options, int defaultIndex, Action<int, string> onSelect)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var segmented = go.AddComponent<AvionicsSegmentedControl>();
            segmented.Initialize(options, defaultIndex, size, onSelect, WidgetStyleManager.Instance?.CurrentTheme);
            return segmented;
        }

        public static AvionicsValueBox CreateValueBox(Transform parent, string name, Vector2 size,
            Vector2 anchoredPos, string title, string initialValue, string unit = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var valBox = go.AddComponent<AvionicsValueBox>();
            valBox.Initialize(title, initialValue, unit, size, WidgetStyleManager.Instance?.CurrentTheme);
            return valBox;
        }

        public static AvionicsAnnunciator CreateAnnunciator(Transform parent, string name, Vector2 size,
            Vector2 anchoredPos, string label, AnnunciatorState initialState = AnnunciatorState.Off,
            bool blinkOnAlert = true, UnityAction onClick = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var ann = go.AddComponent<AvionicsAnnunciator>();
            ann.Initialize(label, size, initialState, blinkOnAlert, onClick, WidgetStyleManager.Instance?.CurrentTheme);
            return ann;
        }
    }
}
