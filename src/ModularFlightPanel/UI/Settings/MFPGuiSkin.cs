using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// MFP 全局现代暗晶航电 IMGUI 样式库 (Aero Dark Glass GUI Skin)
    /// 解决 Unity 默认 HighLogic.Skin / IMGUI 简陋、刺眼与性能问题。
    /// 纯内存动态生成纯色贴图，零外部素材依赖，高对比度现代航电座舱质感。
    /// </summary>
    public static class MFPGuiSkin
    {
        private static bool _initialized = false;
        private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

        // 核心调色板 (Cyber Aero Dark Glass Palette)
        public static readonly Color WindowBgColor = new Color(0.06f, 0.08f, 0.12f, 0.98f);
        public static readonly Color HeaderBgColor = new Color(0.10f, 0.14f, 0.20f, 1.0f);
        public static readonly Color CardBgColor = new Color(0.09f, 0.12f, 0.17f, 0.92f);
        public static readonly Color CardBorderColor = new Color(0.18f, 0.25f, 0.35f, 0.70f);
        public static readonly Color CardHoverBgColor = new Color(0.13f, 0.18f, 0.25f, 0.95f);
        
        public static readonly Color AccentCyan = new Color(0.00f, 0.88f, 1.00f, 1.0f);
        public static readonly Color AccentGreen = new Color(0.00f, 0.92f, 0.55f, 1.0f);
        public static readonly Color AccentAmber = new Color(1.00f, 0.72f, 0.10f, 1.0f);
        public static readonly Color AccentRed = new Color(1.00f, 0.30f, 0.30f, 1.0f);
        public static readonly Color AccentMagenta = new Color(0.85f, 0.35f, 1.00f, 1.0f);
        
        public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1.00f, 1.0f);
        public static readonly Color TextSecondary = new Color(0.60f, 0.70f, 0.82f, 1.0f);
        public static readonly Color TextMuted = new Color(0.40f, 0.48f, 0.58f, 1.0f);

        // 核心 UIStyles
        public static GUIStyle WindowStyle { get; private set; }
        public static GUIStyle HeaderStyle { get; private set; }
        public static GUIStyle CardStyle { get; private set; }
        public static GUIStyle CardHoverStyle { get; private set; }
        public static GUIStyle TabActiveStyle { get; private set; }
        public static GUIStyle TabInactiveStyle { get; private set; }
        public static GUIStyle PrimaryButtonStyle { get; private set; }
        public static GUIStyle SuccessButtonStyle { get; private set; }
        public static GUIStyle WarningButtonStyle { get; private set; }
        public static GUIStyle StepperButtonStyle { get; private set; }
        public static GUIStyle SearchFieldStyle { get; private set; }
        public static GUIStyle TokenBadgeStyle { get; private set; }
        public static GUIStyle ValueFieldStyle { get; private set; }
        public static GUIStyle SectionTitleStyle { get; private set; }

        public static Texture2D SolidTex(Color color)
        {
            string key = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
            if (!_textureCache.TryGetValue(key, out Texture2D tex) || tex == null)
            {
                tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                tex.SetPixel(0, 0, color);
                tex.Apply();
                _textureCache[key] = tex;
            }
            return tex;
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;

            // 1. 窗口基础样式
            WindowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(14, 14, 12, 14),
                border = new RectOffset(6, 6, 6, 6)
            };
            WindowStyle.normal.background = SolidTex(WindowBgColor);
            WindowStyle.onNormal.background = SolidTex(WindowBgColor);

            // 2. 卡片与面板样式
            CardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            CardStyle.normal.background = SolidTex(CardBgColor);
            CardStyle.normal.textColor = TextPrimary;

            CardHoverStyle = new GUIStyle(CardStyle);
            CardHoverStyle.normal.background = SolidTex(CardHoverBgColor);

            // 3. 标签栏激活 / 未激活样式
            TabActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(12, 12, 6, 6)
            };
            TabActiveStyle.normal.background = SolidTex(new Color(0.12f, 0.22f, 0.32f, 1.0f));
            TabActiveStyle.normal.textColor = AccentCyan;
            TabActiveStyle.hover.background = SolidTex(new Color(0.15f, 0.28f, 0.40f, 1.0f));
            TabActiveStyle.hover.textColor = Color.white;

            TabInactiveStyle = new GUIStyle(TabActiveStyle)
            {
                fontStyle = FontStyle.Normal
            };
            TabInactiveStyle.normal.background = SolidTex(new Color(0.08f, 0.11f, 0.15f, 0.9f));
            TabInactiveStyle.normal.textColor = TextSecondary;
            TabInactiveStyle.hover.background = SolidTex(new Color(0.11f, 0.15f, 0.22f, 1.0f));
            TabInactiveStyle.hover.textColor = TextPrimary;

            // 4. 操作按钮系列
            PrimaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 4, 4)
            };
            PrimaryButtonStyle.normal.background = SolidTex(new Color(0.00f, 0.45f, 0.65f, 0.9f));
            PrimaryButtonStyle.normal.textColor = Color.white;
            PrimaryButtonStyle.hover.background = SolidTex(new Color(0.00f, 0.60f, 0.85f, 1.0f));
            PrimaryButtonStyle.hover.textColor = Color.white;

            SuccessButtonStyle = new GUIStyle(PrimaryButtonStyle);
            SuccessButtonStyle.normal.background = SolidTex(new Color(0.05f, 0.45f, 0.25f, 0.9f));
            SuccessButtonStyle.normal.textColor = AccentGreen;
            SuccessButtonStyle.hover.background = SolidTex(new Color(0.08f, 0.60f, 0.35f, 1.0f));
            SuccessButtonStyle.hover.textColor = Color.white;

            WarningButtonStyle = new GUIStyle(PrimaryButtonStyle);
            WarningButtonStyle.normal.background = SolidTex(new Color(0.45f, 0.30f, 0.05f, 0.9f));
            WarningButtonStyle.normal.textColor = AccentAmber;
            WarningButtonStyle.hover.background = SolidTex(new Color(0.60f, 0.40f, 0.08f, 1.0f));
            WarningButtonStyle.hover.textColor = Color.white;

            StepperButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 2, 2)
            };
            StepperButtonStyle.normal.background = SolidTex(new Color(0.14f, 0.19f, 0.26f, 0.9f));
            StepperButtonStyle.normal.textColor = TextPrimary;
            StepperButtonStyle.hover.background = SolidTex(new Color(0.20f, 0.28f, 0.38f, 1.0f));
            StepperButtonStyle.hover.textColor = AccentCyan;

            // 5. 搜索框与输入字段
            SearchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
                padding = new RectOffset(6, 6, 4, 4)
            };
            SearchFieldStyle.normal.background = SolidTex(new Color(0.05f, 0.07f, 0.10f, 0.95f));
            SearchFieldStyle.normal.textColor = TextPrimary;
            SearchFieldStyle.focused.background = SolidTex(new Color(0.08f, 0.12f, 0.18f, 1.0f));
            SearchFieldStyle.focused.textColor = Color.white;

            ValueFieldStyle = new GUIStyle(SearchFieldStyle)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };

            // 6. 通配符胶囊徽章样式
            TokenBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2)
            };
            TokenBadgeStyle.normal.background = SolidTex(new Color(0.00f, 0.30f, 0.45f, 0.8f));
            TokenBadgeStyle.normal.textColor = AccentCyan;

            // 7. 标题样式
            SectionTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            SectionTitleStyle.normal.textColor = AccentCyan;

            _initialized = true;
        }

        /// <summary>
        /// 绘制带边框的现代暗晶卡片区域
        /// </summary>
        public static void BeginCard(params GUILayoutOption[] options)
        {
            EnsureInitialized();
            GUILayout.BeginVertical(CardStyle, options);
        }

        public static void EndCard()
        {
            GUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制高对比度微型胶囊徽章
        /// </summary>
        public static void DrawBadge(string text, Color textColor, Color bgColor, float width = 0)
        {
            EnsureInitialized();
            GUIStyle badge = new GUIStyle(TokenBadgeStyle);
            badge.normal.background = SolidTex(bgColor);
            badge.normal.textColor = textColor;
            if (width > 0)
            {
                GUILayout.Label(text, badge, GUILayout.Width(width));
            }
            else
            {
                GUILayout.Label(text, badge);
            }
        }
    }
}
