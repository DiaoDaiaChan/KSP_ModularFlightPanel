using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Workbench
{
    /// <summary>
    /// 航电暗晶工作台 3.0 统一样式与 GPU 材质引擎 (WorkbenchStyleEngine)
    /// 核心特性：
    /// 1. 深度联动 ThemeConfig 航电主题语义配色体系，杜绝私有颜色硬编码；
    /// 2. 统一管理 ModernWorkbenchGlass 着色器材质实例与变体；
    /// 3. 0 GC 材质缓存池与主题热切换实时广播刷新；
    /// 4. 完美兼容 1080p / 1440p / 4K 亚像素 SDF 矢量圆角与程序化暗晶玻璃质感。
    /// </summary>
    public static class WorkbenchStyleEngine
    {
        private static readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>(StringComparer.Ordinal);
        private static bool _initialized = false;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (ThemeManager.Instance != null)
            {
                ThemeManager.Instance.OnThemeChanged += HandleThemeChanged;
            }
        }

        private static void HandleThemeChanged(ThemeConfig newTheme)
        {
            ClearCache();
        }

        public static void ClearCache()
        {
            foreach (var kvp in _matCache)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            _matCache.Clear();
        }

        public static ThemeConfig ActiveTheme => ThemeManager.Instance?.CurrentTheme ?? new ThemeConfig();

        #region Semantic Theme Colors

        public static Color ColorWindowBg
        {
            get
            {
                Color c = (Color)ActiveTheme.FrameBgColor;
                if (c.r > 0.4f && c.g > 0.4f && c.b > 0.4f)
                    c = new Color(0.02f, 0.025f, 0.035f, 0.94f);
                else
                    c.a = Mathf.Clamp(c.a * 1.05f, 0.90f, 0.96f);
                return c;
            }
        }

        public static Color ColorPanelBg
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, Color.black, 0.18f);
                c.a = 0.82f;
                return c;
            }
        }

        public static Color ColorCardBg
        {
            get
            {
                Color c = new Color(ColorWindowBg.r * 0.9f + 0.035f, ColorWindowBg.g * 0.9f + 0.040f, ColorWindowBg.b * 0.9f + 0.055f, 0.75f);
                return c;
            }
        }

        public static Color ColorCardBgHover
        {
            get
            {
                Color c = new Color(ColorWindowBg.r * 0.85f + 0.065f, ColorWindowBg.g * 0.85f + 0.075f, ColorWindowBg.b * 0.85f + 0.105f, 0.90f);
                return c;
            }
        }

        public static Color ColorBtnPrimaryBg
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, ColorAccentPrimary, 0.38f);
                c.a = 0.90f;
                return c;
            }
        }

        public static Color ColorBtnSecondaryBg
        {
            get
            {
                Color c = new Color(0.08f, 0.11f, 0.16f, 0.72f);
                return c;
            }
        }

        public static Color ColorBtnHoverBg
        {
            get
            {
                Color c = Color.Lerp(ColorBtnSecondaryBg, ColorAccentPrimary, 0.22f);
                c.a = 0.92f;
                return c;
            }
        }

        public static Color ColorPillAccentBg
        {
            get
            {
                Color c = Color.Lerp(ColorAccentPrimary, ColorWindowBg, 0.55f);
                c.a = 0.90f;
                return c;
            }
        }

        public static Color ColorPillDarkBg
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, ColorBorder, 0.25f);
                c.a = 0.85f;
                return c;
            }
        }

        public static Color ColorBorder => (Color)ActiveTheme.FrameBorderColor;
        public static Color ColorStructureBorder => new Color(1f, 1f, 1f, 0.08f);
        public static Color ColorStructureBorderSubtle => new Color(1f, 1f, 1f, 0.04f);
        public static Color ColorAccentPrimary => (Color)ActiveTheme.AccentPrimary;
        public static Color ColorAccentSecondary => (Color)ActiveTheme.AccentSecondary;
        public static Color ColorWarning => (Color)ActiveTheme.WarningColor;
        public static Color ColorDanger => (Color)ActiveTheme.DangerColor;
        public static Color ColorSuccess => new Color(0.2f, 0.85f, 0.45f, 1f);
        public static Color ColorTextPrimary => (Color)ActiveTheme.TextPrimaryColor;
        public static Color ColorTextAccent => (Color)ActiveTheme.TextAccentColor;
        public static Color ColorTextMuted => new Color(ColorTextAccent.r, ColorTextAccent.g, ColorTextAccent.b, 0.55f);

        #endregion

        #region Material Generation & Retrieval

        /// <summary>
        /// 获取暗晶工作台主窗体底盘材质 (深邃纯净底盘，1px 发丝边，无多余彩光)
        /// </summary>
        public static Material GetWindowGlassMaterial()
        {
            EnsureInitialized();
            string key = "window_glass_" + ActiveTheme.ThemeId;
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_WindowGlass";

            Color bg = ColorWindowBg;
            bg.a = Mathf.Clamp(bg.a * 1.05f, 0.92f, 0.98f);
            Color border = new Color(ColorBorder.r, ColorBorder.g, ColorBorder.b, 0.32f);

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0030f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.022f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.05f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取结构性面板容器材质 (顶栏、底栏、导航轨、左右分栏，纯净深色无彩边)
        /// </summary>
        public static Material GetContainerPanelMaterial()
        {
            EnsureInitialized();
            string key = "panel_container_" + ActiveTheme.ThemeId;
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_ContainerPanel";

            Color bg = ColorPanelBg;
            Color border = ColorStructureBorder;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0025f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.020f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.03f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取工作台内部浮动卡片材质 (轻微发丝边，悬停升温，选中带左侧强调色)
        /// </summary>
        public static Material GetCardMaterial(bool isHover = false, bool isEmphasized = false)
        {
            EnsureInitialized();
            string key = $"card_{ActiveTheme.ThemeId}_{(isHover ? "hov" : "norm")}_{(isEmphasized ? "emp" : "norm")}";
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_CardGlass";

            Color cardBg = isHover ? ColorCardBgHover : ColorCardBg;

            Color borderColor;
            if (isEmphasized)
            {
                borderColor = ColorAccentPrimary;
                borderColor.a = 0.70f;
            }
            else if (isHover)
            {
                borderColor = Color.Lerp(ColorStructureBorder, ColorAccentPrimary, 0.40f);
                borderColor.a = 0.45f;
            }
            else
            {
                borderColor = new Color(ColorBorder.r, ColorBorder.g, ColorBorder.b, 0.16f);
            }

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", cardBg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", borderColor);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", isEmphasized ? 0.0045f : 0.0030f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.035f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", isHover ? 0.08f : 0.04f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isHover ? 0.30f : (isEmphasized ? 0.15f : 0f));
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", isEmphasized ? 0.30f : 0f);

            _matCache[key] = mat;
            return mat;
        }

        public static Material GetCardGlassMaterial(bool isEmphasized = false) => GetCardMaterial(false, isEmphasized);

        /// <summary>
        /// 获取交互按钮材质
        /// </summary>
        public static Material GetButtonMaterial(bool isPrimary = false, bool isHover = false)
        {
            EnsureInitialized();
            string key = $"btn_{ActiveTheme.ThemeId}_{(isPrimary ? "pri" : "sec")}_{(isHover ? "hov" : "norm")}";
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_BtnGlass";

            Color bg;
            Color border;
            float topAccent = 0f;

            if (isPrimary)
            {
                bg = isHover
                    ? Color.Lerp(ColorWindowBg, ColorAccentPrimary, 0.46f)
                    : Color.Lerp(ColorWindowBg, ColorAccentPrimary, 0.36f);
                bg.a = isHover ? 0.95f : 0.88f;

                border = ColorAccentPrimary;
                border.a = isHover ? 0.85f : 0.50f;
                topAccent = 0.30f;
            }
            else
            {
                bg = isHover
                    ? Color.Lerp(ColorBtnSecondaryBg, ColorAccentPrimary, 0.16f)
                    : ColorBtnSecondaryBg;
                bg.a = isHover ? 0.88f : 0.70f;

                border = new Color(1f, 1f, 1f, isHover ? 0.22f : 0.09f);
            }

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0035f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.045f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", isHover ? 0.12f : 0.06f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isHover ? (isPrimary ? 0.40f : 0.15f) : 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", topAccent);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取轻量幽灵按钮材质 (平时透明，悬停显现)
        /// </summary>
        public static Material GetGhostButtonMaterial(bool isHover = false)
        {
            EnsureInitialized();
            string key = $"btn_ghost_{ActiveTheme.ThemeId}_{(isHover ? "hov" : "norm")}";
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_GhostBtnGlass";

            Color bg = new Color(1f, 1f, 1f, isHover ? 0.09f : 0.001f);
            Color border = new Color(1f, 1f, 1f, isHover ? 0.18f : 0f);

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0030f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.040f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取分段模式控制器底轨跑道材质
        /// </summary>
        public static Material GetSegmentTrackMaterial()
        {
            EnsureInitialized();
            string key = "seg_track_" + ActiveTheme.ThemeId;
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_SegTrack";

            Color bg = new Color(0.03f, 0.045f, 0.07f, 0.88f);
            Color border = new Color(1f, 1f, 1f, 0.07f);

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0030f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.080f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取分段模式控制器浮动滑块材质
        /// </summary>
        public static Material GetSegmentThumbMaterial()
        {
            EnsureInitialized();
            string key = "seg_thumb_" + ActiveTheme.ThemeId;
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_SegThumb";

            Color bg = Color.Lerp(ColorWindowBg, ColorBorder, 0.28f);
            bg.a = 0.95f;
            Color border = new Color(1f, 1f, 1f, 0.18f);

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0035f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.070f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.10f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取胶囊徽标与药丸 Dock 材质
        /// </summary>
        public static Material GetPillDockMaterial(bool isAccent = false)
        {
            EnsureInitialized();
            string key = $"pill_dock_{ActiveTheme.ThemeId}_{(isAccent ? "acc" : "dark")}";
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.ModernWorkbenchShader ?? AssetLoader.GlassCockpitShader;
            if (s == null) return null;

            mat = new Material(s);
            mat.name = "MFP_Workbench_PillDockGlass";

            Color bg = isAccent
                ? Color.Lerp(ColorAccentPrimary, ColorWindowBg, 0.22f)
                : Color.Lerp(ColorWindowBg, Color.black, 0.30f);
            bg.a = isAccent ? 0.88f : 0.92f;

            Color border = isAccent ? ColorAccentPrimary : new Color(1f, 1f, 1f, 0.12f);
            border.a = isAccent ? 0.75f : 0.12f;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.0035f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.18f); // 极高圆角形成胶囊药丸形态
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.08f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isAccent ? 0.25f : 0f);
            if (mat.HasProperty("_TopAccentStrength")) mat.SetFloat("_TopAccentStrength", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取高清无锯齿锐利文本材质
        /// </summary>
        public static Material GetCrispTextMaterial()
        {
            EnsureInitialized();
            string key = "crisp_text_" + ActiveTheme.ThemeId;
            if (_matCache.TryGetValue(key, out Material mat) && mat != null) return mat;

            Shader s = AssetLoader.CrispAvionicsTextShader;
            if (s != null)
            {
                mat = new Material(s);
                mat.name = "MFP_Workbench_CrispText";
            }
            _matCache[key] = mat;
            return mat;
        }

        #endregion
    }
}
