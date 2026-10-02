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

        public static Color ColorCardBg
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, ColorBorder, 0.08f);
                c.a = 0.88f;
                return c;
            }
        }

        public static Color ColorCardBgHover
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, ColorAccentPrimary, 0.16f);
                c.a = 0.94f;
                return c;
            }
        }

        public static Color ColorBtnPrimaryBg
        {
            get
            {
                Color c = Color.Lerp(ColorAccentPrimary, ColorWindowBg, 0.45f);
                c.a = 0.90f;
                return c;
            }
        }

        public static Color ColorBtnSecondaryBg
        {
            get
            {
                Color c = Color.Lerp(ColorWindowBg, ColorBorder, 0.18f);
                c.a = 0.82f;
                return c;
            }
        }

        public static Color ColorBtnHoverBg
        {
            get
            {
                Color c = Color.Lerp(ColorBtnSecondaryBg, ColorAccentPrimary, 0.35f);
                c.a = 0.95f;
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
        /// 获取暗晶工作台主窗体底盘材质
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
            bg.a = Mathf.Clamp(bg.a * 1.05f, 0.90f, 0.98f);
            Color border = ColorBorder;
            border.a = 0.85f;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.005f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.025f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.08f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0.02f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", 0f);

            _matCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// 获取工作台内部卡片与面板材质
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

            Color cardBg = Color.Lerp(ColorWindowBg, ColorBorder, 0.12f);
            cardBg.a = isHover ? 0.96f : 0.88f;

            Color borderColor = isEmphasized ? ColorAccentPrimary : (isHover ? Color.Lerp(ColorBorder, ColorAccentPrimary, 0.6f) : ColorBorder);
            borderColor.a = isHover || isEmphasized ? 0.95f : 0.70f;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", cardBg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", borderColor);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", isEmphasized ? 0.012f : 0.008f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.045f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", isHover ? 0.16f : 0.10f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0.015f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isHover ? 0.45f : (isEmphasized ? 0.25f : 0f));

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

            Color bg = isPrimary
                ? Color.Lerp(ColorAccentPrimary, ColorWindowBg, 0.35f)
                : Color.Lerp(ColorWindowBg, ColorBorder, 0.22f);
            bg.a = isHover ? 0.95f : (isPrimary ? 0.88f : 0.82f);

            Color border = isPrimary ? ColorAccentPrimary : ColorBorder;
            border.a = isHover ? 1f : 0.75f;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", isPrimary ? ColorAccentPrimary : ColorAccentSecondary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.010f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.06f);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", isHover ? 0.20f : 0.12f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isHover ? 0.60f : 0f);

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
                ? Color.Lerp(ColorAccentPrimary, ColorWindowBg, 0.25f)
                : Color.Lerp(ColorWindowBg, Color.black, 0.30f);
            bg.a = isAccent ? 0.90f : 0.94f;

            Color border = isAccent ? ColorAccentPrimary : Color.Lerp(ColorBorder, ColorAccentPrimary, 0.40f);
            border.a = 0.90f;

            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", bg);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", border);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", ColorAccentPrimary);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", 0.015f);
            if (mat.HasProperty("_CornerRadius")) mat.SetFloat("_CornerRadius", 0.18f); // 极高圆角形成胶囊药丸形态
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", 0.15f);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", 0.02f);
            if (mat.HasProperty("_HoverGlow")) mat.SetFloat("_HoverGlow", isAccent ? 0.40f : 0.10f);

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
