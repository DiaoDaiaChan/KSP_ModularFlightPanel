using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// MFP 全局现代暗晶航电 IMGUI 设计系统 2.0 (Cyber Dark Glass GUI Skin Suite)
    /// 彻底废除 Unity 原版灰框与拼凑的粗糙感，提供像素级精致的暗晶座舱面板美学。
    /// 内置：
    /// 1. 细线高光边框动态贴图缓存 (Crisp 9-Slice Bordered Textures)
    /// 2. 缓冲式数值安全输入 (Buffered Numeric Field - 彻底消灭打小数点被重置回弹问题)
    /// 3. 现代化胶囊徽章、语义按钮、搜索框与 Toast 通知组件
    /// </summary>
    public static class MFPGuiSkin
    {
        private static bool _initialized = false;
        private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, string> _inputBufferMap = new Dictionary<string, string>();

        // 核心调色板 (Cyber Aero Dark Glass Palette)
        public static readonly Color WindowBgColor = new Color(0.06f, 0.08f, 0.12f, 0.98f);
        public static readonly Color HeaderBgColor = new Color(0.10f, 0.14f, 0.20f, 1.0f);
        public static readonly Color CardBgColor = new Color(0.08f, 0.11f, 0.16f, 0.92f);
        public static readonly Color CardBorderColor = new Color(0.18f, 0.26f, 0.38f, 0.75f);
        public static readonly Color CardHoverBgColor = new Color(0.12f, 0.16f, 0.24f, 0.96f);
        public static readonly Color InsetBgColor = new Color(0.04f, 0.06f, 0.09f, 0.95f);
        public static readonly Color InsetBorderColor = new Color(0.12f, 0.18f, 0.26f, 0.60f);

        public static readonly Color AccentCyan = new Color(0.00f, 0.88f, 1.00f, 1.0f);
        public static readonly Color AccentGreen = new Color(0.00f, 0.92f, 0.55f, 1.0f);
        public static readonly Color AccentAmber = new Color(1.00f, 0.72f, 0.10f, 1.0f);
        public static readonly Color AccentRed = new Color(1.00f, 0.30f, 0.30f, 1.0f);
        public static readonly Color AccentMagenta = new Color(0.85f, 0.35f, 1.00f, 1.0f);
        public static readonly Color AccentBlue = new Color(0.20f, 0.55f, 0.95f, 1.0f);

        public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1.00f, 1.0f);
        public static readonly Color TextSecondary = new Color(0.65f, 0.75f, 0.88f, 1.0f);
        public static readonly Color TextMuted = new Color(0.42f, 0.50f, 0.62f, 1.0f);

        // 核心 UIStyles
        public static GUIStyle WindowStyle { get; private set; }
        public static GUIStyle HeaderStyle { get; private set; }
        public static GUIStyle CardStyle { get; private set; }
        public static GUIStyle CardHoverStyle { get; private set; }
        public static GUIStyle InsetStyle { get; private set; }
        public static GUIStyle TabActiveStyle { get; private set; }
        public static GUIStyle TabInactiveStyle { get; private set; }
        public static GUIStyle PrimaryButtonStyle { get; private set; }
        public static GUIStyle SecondaryButtonStyle { get; private set; }
        public static GUIStyle SuccessButtonStyle { get; private set; }
        public static GUIStyle WarningButtonStyle { get; private set; }
        public static GUIStyle DangerButtonStyle { get; private set; }
        public static GUIStyle StepperButtonStyle { get; private set; }
        public static GUIStyle SearchFieldStyle { get; private set; }
        public static GUIStyle TokenBadgeStyle { get; private set; }
        public static GUIStyle ValueFieldStyle { get; private set; }
        public static GUIStyle SectionTitleStyle { get; private set; }
        public static GUIStyle SubtitleStyle { get; private set; }
        public static GUIStyle RowSelectedStyle { get; private set; }
        public static GUIStyle RowNormalStyle { get; private set; }

        public static Texture2D SolidTex(Color color)
        {
            string key = $"solid_{ColorUtility.ToHtmlStringRGBA(color)}";
            if (!_textureCache.TryGetValue(key, out Texture2D tex) || tex == null)
            {
                tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                tex.SetPixel(0, 0, color);
                tex.Apply();
                _textureCache[key] = tex;
            }
            return tex;
        }

        public static Texture2D BorderedTex(Color bg, Color border, int borderWidth = 1, int size = 16)
        {
            string key = $"bord_{ColorUtility.ToHtmlStringRGBA(bg)}_{ColorUtility.ToHtmlStringRGBA(border)}_{borderWidth}_{size}";
            if (!_textureCache.TryGetValue(key, out Texture2D tex) || tex == null)
            {
                tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool isBorder = (x < borderWidth || x >= size - borderWidth || y < borderWidth || y >= size - borderWidth);
                        tex.SetPixel(x, y, isBorder ? border : bg);
                    }
                }
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
                padding = new RectOffset(16, 16, 14, 16),
                border = new RectOffset(6, 6, 6, 6)
            };
            Texture2D winTex = BorderedTex(WindowBgColor, new Color(0.00f, 0.88f, 1.00f, 0.45f), 1, 16);
            WindowStyle.normal.background = winTex;
            WindowStyle.onNormal.background = winTex;

            // 2. 卡片与内嵌面板样式
            CardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 4, 4)
            };
            CardStyle.normal.background = BorderedTex(CardBgColor, CardBorderColor, 1, 16);
            CardStyle.normal.textColor = TextPrimary;

            CardHoverStyle = new GUIStyle(CardStyle);
            CardHoverStyle.normal.background = BorderedTex(CardHoverBgColor, new Color(0.00f, 0.88f, 1.00f, 0.60f), 1, 16);

            InsetStyle = new GUIStyle(CardStyle)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 2, 2)
            };
            InsetStyle.normal.background = BorderedTex(InsetBgColor, InsetBorderColor, 1, 16);

            // 3. 标签栏激活 / 未激活样式
            TabActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 7, 7)
            };
            TabActiveStyle.normal.background = BorderedTex(new Color(0.12f, 0.22f, 0.34f, 1.0f), AccentCyan, 1, 16);
            TabActiveStyle.normal.textColor = AccentCyan;
            TabActiveStyle.hover.background = BorderedTex(new Color(0.16f, 0.28f, 0.42f, 1.0f), AccentCyan, 1, 16);
            TabActiveStyle.hover.textColor = Color.white;

            TabInactiveStyle = new GUIStyle(TabActiveStyle)
            {
                fontStyle = FontStyle.Normal
            };
            TabInactiveStyle.normal.background = BorderedTex(new Color(0.07f, 0.10f, 0.15f, 0.9f), new Color(0.15f, 0.20f, 0.28f, 0.5f), 1, 16);
            TabInactiveStyle.normal.textColor = TextSecondary;
            TabInactiveStyle.hover.background = BorderedTex(new Color(0.10f, 0.14f, 0.20f, 1.0f), new Color(0.25f, 0.35f, 0.48f, 0.8f), 1, 16);
            TabInactiveStyle.hover.textColor = TextPrimary;

            // 4. 按钮系列
            PrimaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 5, 5)
            };
            PrimaryButtonStyle.normal.background = BorderedTex(new Color(0.00f, 0.45f, 0.65f, 0.95f), new Color(0.00f, 0.88f, 1.00f, 0.8f), 1, 16);
            PrimaryButtonStyle.normal.textColor = Color.white;
            PrimaryButtonStyle.hover.background = BorderedTex(new Color(0.00f, 0.60f, 0.85f, 1.0f), new Color(0.30f, 0.95f, 1.00f, 1.0f), 1, 16);
            PrimaryButtonStyle.hover.textColor = Color.white;

            SecondaryButtonStyle = new GUIStyle(PrimaryButtonStyle);
            SecondaryButtonStyle.normal.background = BorderedTex(new Color(0.12f, 0.17f, 0.24f, 0.95f), new Color(0.25f, 0.35f, 0.48f, 0.8f), 1, 16);
            SecondaryButtonStyle.normal.textColor = TextPrimary;
            SecondaryButtonStyle.hover.background = BorderedTex(new Color(0.18f, 0.25f, 0.35f, 1.0f), AccentCyan, 1, 16);
            SecondaryButtonStyle.hover.textColor = AccentCyan;

            SuccessButtonStyle = new GUIStyle(PrimaryButtonStyle);
            SuccessButtonStyle.normal.background = BorderedTex(new Color(0.05f, 0.45f, 0.25f, 0.95f), new Color(0.00f, 0.92f, 0.55f, 0.8f), 1, 16);
            SuccessButtonStyle.normal.textColor = AccentGreen;
            SuccessButtonStyle.hover.background = BorderedTex(new Color(0.08f, 0.60f, 0.35f, 1.0f), Color.white, 1, 16);
            SuccessButtonStyle.hover.textColor = Color.white;

            WarningButtonStyle = new GUIStyle(PrimaryButtonStyle);
            WarningButtonStyle.normal.background = BorderedTex(new Color(0.48f, 0.32f, 0.05f, 0.95f), new Color(1.00f, 0.72f, 0.10f, 0.8f), 1, 16);
            WarningButtonStyle.normal.textColor = AccentAmber;
            WarningButtonStyle.hover.background = BorderedTex(new Color(0.65f, 0.42f, 0.08f, 1.0f), Color.white, 1, 16);
            WarningButtonStyle.hover.textColor = Color.white;

            DangerButtonStyle = new GUIStyle(PrimaryButtonStyle);
            DangerButtonStyle.normal.background = BorderedTex(new Color(0.48f, 0.12f, 0.15f, 0.95f), new Color(1.00f, 0.30f, 0.30f, 0.8f), 1, 16);
            DangerButtonStyle.normal.textColor = AccentRed;
            DangerButtonStyle.hover.background = BorderedTex(new Color(0.65f, 0.18f, 0.22f, 1.0f), Color.white, 1, 16);
            DangerButtonStyle.hover.textColor = Color.white;

            StepperButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 3, 3)
            };
            StepperButtonStyle.normal.background = BorderedTex(new Color(0.13f, 0.18f, 0.25f, 0.9f), new Color(0.22f, 0.30f, 0.42f, 0.7f), 1, 16);
            StepperButtonStyle.normal.textColor = TextPrimary;
            StepperButtonStyle.hover.background = BorderedTex(new Color(0.18f, 0.26f, 0.36f, 1.0f), AccentCyan, 1, 16);
            StepperButtonStyle.hover.textColor = AccentCyan;

            // 5. 搜索框与输入字段
            SearchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 5, 5)
            };
            SearchFieldStyle.normal.background = BorderedTex(new Color(0.05f, 0.07f, 0.10f, 0.95f), new Color(0.20f, 0.28f, 0.40f, 0.8f), 1, 16);
            SearchFieldStyle.normal.textColor = TextPrimary;
            SearchFieldStyle.focused.background = BorderedTex(new Color(0.08f, 0.12f, 0.18f, 1.0f), AccentCyan, 1, 16);
            SearchFieldStyle.focused.textColor = Color.white;

            ValueFieldStyle = new GUIStyle(SearchFieldStyle)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };

            // 6. 徽章与标签
            TokenBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 3, 3)
            };
            TokenBadgeStyle.normal.background = BorderedTex(new Color(0.00f, 0.28f, 0.42f, 0.85f), new Color(0.00f, 0.88f, 1.00f, 0.5f), 1, 16);
            TokenBadgeStyle.normal.textColor = AccentCyan;

            SectionTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            SectionTitleStyle.normal.textColor = AccentCyan;

            SubtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10
            };
            SubtitleStyle.normal.textColor = TextMuted;

            // 7. 交互行项样式 (用于 Master-Detail 导航列表)
            RowNormalStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 2)
            };
            RowNormalStyle.normal.background = BorderedTex(new Color(0.07f, 0.10f, 0.14f, 0.85f), new Color(0.14f, 0.20f, 0.28f, 0.5f), 1, 16);
            RowNormalStyle.normal.textColor = TextPrimary;

            RowSelectedStyle = new GUIStyle(RowNormalStyle);
            RowSelectedStyle.normal.background = BorderedTex(new Color(0.10f, 0.20f, 0.30f, 0.95f), AccentCyan, 1, 16);
            RowSelectedStyle.normal.textColor = Color.white;

            _initialized = true;
        }

        public static void BeginCard(params GUILayoutOption[] options)
        {
            EnsureInitialized();
            GUILayout.BeginVertical(CardStyle, options);
        }

        public static void EndCard()
        {
            GUILayout.EndVertical();
        }

        public static void BeginInset(params GUILayoutOption[] options)
        {
            EnsureInitialized();
            GUILayout.BeginVertical(InsetStyle, options);
        }

        public static void EndInset()
        {
            GUILayout.EndVertical();
        }

        public static void DrawHeader(string title, string subtitle = null, string badge = null, Color badgeBg = default)
        {
            EnsureInitialized();
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, SectionTitleStyle);
            if (!string.IsNullOrEmpty(badge))
            {
                Color bg = badgeBg == default ? new Color(0.0f, 0.35f, 0.5f, 0.9f) : badgeBg;
                DrawBadge(badge, Color.white, bg);
            }
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(subtitle))
            {
                GUILayout.Label(subtitle, SubtitleStyle);
            }
            GUILayout.EndHorizontal();
        }

        public static void DrawBadge(string text, Color textColor, Color bgColor, float width = 0)
        {
            EnsureInitialized();
            GUIStyle badge = new GUIStyle(TokenBadgeStyle);
            badge.normal.background = BorderedTex(bgColor, new Color(textColor.r, textColor.g, textColor.b, 0.5f), 1, 16);
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

        public static void DrawSearchBar(ref string query, string placeholder = "搜索...", float width = 0)
        {
            EnsureInitialized();
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(20f));

            var options = width > 0 ? new GUILayoutOption[] { GUILayout.Width(width) } : new GUILayoutOption[] { GUILayout.ExpandWidth(true) };
            string newQuery = GUILayout.TextField(query ?? "", SearchFieldStyle, options);
            if (newQuery != query)
            {
                query = newQuery;
            }

            if (!string.IsNullOrEmpty(query))
            {
                if (GUILayout.Button("✕", StepperButtonStyle, GUILayout.Width(24f), GUILayout.Height(22f)))
                {
                    query = "";
                    GUI.FocusControl(null);
                }
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 缓冲式双精度浮点数输入控件：
        /// 在输入与聚焦期间缓存未解析的字符串，仅在回车或失焦时解析并提交，彻底解决输入小数点或负号时被重绘强制刷掉的严重问题。
        /// </summary>
        public static double DrawBufferedDoubleField(string controlId, double value, float width = 70f, string format = "G")
        {
            EnsureInitialized();
            GUI.SetNextControlName(controlId);

            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);
            if (!_inputBufferMap.TryGetValue(controlId, out string text))
            {
                text = value.ToString(format);
                _inputBufferMap[controlId] = text;
            }

            if (!isFocused)
            {
                text = value.ToString(format);
                _inputBufferMap[controlId] = text;
            }

            string newText = GUILayout.TextField(text, ValueFieldStyle, GUILayout.Width(width));
            if (newText != text)
            {
                _inputBufferMap[controlId] = newText;
            }

            if (double.TryParse(newText, out double parsedVal))
            {
                return parsedVal;
            }
            return value;
        }

        /// <summary>
        /// 缓冲式单精度浮点数输入控件
        /// </summary>
        public static float DrawBufferedFloatField(string controlId, float value, float width = 60f, string format = "F0")
        {
            EnsureInitialized();
            GUI.SetNextControlName(controlId);

            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);
            if (!_inputBufferMap.TryGetValue(controlId, out string text))
            {
                text = value.ToString(format);
                _inputBufferMap[controlId] = text;
            }

            if (!isFocused)
            {
                text = value.ToString(format);
                _inputBufferMap[controlId] = text;
            }

            string newText = GUILayout.TextField(text, ValueFieldStyle, GUILayout.Width(width));
            if (newText != text)
            {
                _inputBufferMap[controlId] = newText;
            }

            if (float.TryParse(newText, out float parsedVal))
            {
                return parsedVal;
            }
            return value;
        }

        private static string _globalToastMsg = string.Empty;
        private static float _globalToastTimer = 0f;

        public static void ShowToast(string msg, float duration = 2.0f)
        {
            _globalToastMsg = msg;
            _globalToastTimer = duration;
        }

        public static void DrawGlobalToast()
        {
            DrawToast(ref _globalToastMsg, ref _globalToastTimer);
        }

        /// <summary>
        /// 绘制 Toast 通知胶囊
        /// </summary>
        public static void DrawToast(ref string msg, ref float timer)
        {
            if (timer > 0f && !string.IsNullOrEmpty(msg))
            {
                timer -= Time.unscaledDeltaTime;
                float alpha = Mathf.Clamp01(timer / 0.5f);
                Color c = new Color(0.00f, 0.92f, 0.55f, alpha);
                Color bg = new Color(0.04f, 0.20f, 0.12f, 0.95f * alpha);
                DrawBadge($"✔ {msg}", c, bg);
                GUILayout.Space(4f);
            }
        }
    }
}
