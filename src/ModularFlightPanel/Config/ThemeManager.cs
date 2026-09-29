using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Config
{
    public class ThemeManager
    {
        private static ThemeManager _instance;
        public static ThemeManager Instance => _instance ?? (_instance = new ThemeManager());

        public static bool IsStockNavballHidden { get; set; } = true;
        public static bool IsStockAltimeterHidden { get; set; } = false;
        public static bool IsStockBottomLeftHidden { get; set; } = false;
        public static bool IsStockTimeWarpHidden { get; set; } = false;
        public static bool IsStockCommNetHidden { get; set; } = false;
        public static bool IsStockToolbarHidden { get; set; } = false;
        public int ToolbarStyleMode { get; set; } = 1;
        public int NonFlightToolbarMode { get; set; } = 1;
        public bool EnableGpu2DUIAcceleration { get; set; } = true;
        public List<DockButtonRule> DockRules { get; set; } = new List<DockButtonRule>();
        public bool DockShowHiddenDrawer { get; set; } = false;
        public int DockOrientation { get; set; } = 0;

        public bool DockEnableFavoritePanel { get; set; } = true;
        public int DockFavoriteOrientation { get; set; } = 1;
        public bool DockKeepFavoritesInMain { get; set; } = false;
        public float DockFavoritePosX { get; set; } = 0f;
        public float DockFavoritePosY { get; set; } = -380f;

        public float SettingsWindowX { get; set; } = -1f;
        public float SettingsWindowY { get; set; } = -1f;
        public float SettingsWindowWidth { get; set; } = 1040f;
        public float SettingsWindowHeight { get; set; } = 650f;
        public bool SettingsWindowMaximized { get; set; } = false;

        public DockButtonRule GetOrCreateDockRule(string key, string defaultName)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (DockRules == null) DockRules = new List<DockButtonRule>();
            var rule = DockRules.Find(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                bool isDefaultFav = false;
                string lower = (key + " " + (defaultName ?? "")).ToLowerInvariant();
                if (lower.Contains("mechjeb") || lower.Contains("engineer") || lower.Contains("trajector") || lower.Contains("docking") || lower.Contains("dpai") || lower.Contains("mfp") || lower.Contains("alarm"))
                {
                    isDefaultFav = true;
                }

                rule = new DockButtonRule
                {
                    Key = key,
                    DefaultName = defaultName ?? key,
                    CustomLabel = "",
                    IsVisible = true,
                    IsFavorite = isDefaultFav
                };
                DockRules.Add(rule);
            }
            else if (string.IsNullOrEmpty(rule.DefaultName) && !string.IsNullOrEmpty(defaultName))
            {
                rule.DefaultName = defaultName;
            }
            return rule;
        }

        public void AutoRecommendFavorites()
        {
            if (DockRules == null || DockRules.Count == 0) return;
            string[] favKeywords = new[] { "mechjeb", "mj", "ker", "engineer", "trajector", "dpai", "docking", "alarm", "mfp", "naviball", "kac", "transfer" };
            foreach (var rule in DockRules)
            {
                string combined = (rule.Key + " " + rule.DefaultName + " " + rule.CustomLabel).ToLowerInvariant();
                foreach (var kw in favKeywords)
                {
                    if (combined.Contains(kw))
                    {
                        rule.IsFavorite = true;
                        rule.IsVisible = true;
                        break;
                    }
                }
            }
            SaveSettings();
        }
        public bool MasterBypass
        {
            get => Core.MFPProfiler.IsMasterBypassed;
            set => Core.MFPProfiler.IsMasterBypassed = value;
        }
        public bool ShowPerformanceBadge
        {
            get => Core.MFPProfiler.ShowOverlay;
            set => Core.MFPProfiler.ShowOverlay = value;
        }

        public List<ThemeConfig> AvailableThemes { get; private set; } = new List<ThemeConfig>();
        public ThemeConfig CurrentTheme { get; private set; }

        private NavballRenderMode _globalRenderMode = NavballRenderMode.ProceduralVector;
        public NavballRenderMode GlobalRenderMode
        {
            get => _globalRenderMode;
            set
            {
                if (_globalRenderMode != value)
                {
                    _globalRenderMode = value;
                    SaveSettings();
                    NotifyThemeChanged();
                }
            }
        }

        public event Action<ThemeConfig> OnThemeChanged;

        public void NotifyThemeChanged()
        {
            if (ToolbarStyleMode == 1)
            {
                StockToolbarHook.ReskinStockToolbar();
            }
            OnThemeChanged?.Invoke(CurrentTheme);
        }

        private string ThemesDirectory => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/Themes");
        private string SettingsFilePath => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/theme_settings.json");

        public void Initialize()
        {
            LoadAllThemes();

            ThemeConfig defaultTheme = AvailableThemes.Find(t => t.ThemeId == "modern_aero");
            CurrentTheme = defaultTheme ?? (AvailableThemes.Count > 0 ? AvailableThemes[0] : ThemeConfig.CreateModernAero());

            LoadSettings();
        }

        public void SaveSettings()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var data = new ThemeSettingsData
                {
                    SelectedThemeId = CurrentTheme != null ? CurrentTheme.ThemeId : "modern_aero",
                    SelectedLanguage = I18nManager.Instance.CurrentLanguage,
                    RenderMode = (int)_globalRenderMode,
                    HideStockNavball = IsStockNavballHidden,
                    HideStockAltimeter = IsStockAltimeterHidden,
                    HideStockBottomLeft = IsStockBottomLeftHidden,
                    HideStockTimeWarp = IsStockTimeWarpHidden,
                    HideStockCommNet = IsStockCommNetHidden,
                    HideStockToolbar = IsStockToolbarHidden,
                    ToolbarStyleMode = ToolbarStyleMode,
                    NonFlightToolbarMode = NonFlightToolbarMode,
                    MasterBypass = MasterBypass,
                    ShowPerformanceBadge = ShowPerformanceBadge,
                    EnableGpu2DUIAcceleration = EnableGpu2DUIAcceleration,
                    AutoAdaptResolution = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.AutoAdaptResolution : true,
                    GlobalRenderScaleMultiplier = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.GlobalRenderScaleMultiplier : 1.0f,
                    DockRules = DockRules != null ? new List<DockButtonRule>(DockRules) : new List<DockButtonRule>(),
                    DockShowHiddenDrawer = DockShowHiddenDrawer,
                    DockOrientation = DockOrientation,
                    DockEnableFavoritePanel = DockEnableFavoritePanel,
                    DockFavoriteOrientation = DockFavoriteOrientation,
                    DockKeepFavoritesInMain = DockKeepFavoritesInMain,
                    DockFavoritePosX = DockFavoritePosX,
                    DockFavoritePosY = DockFavoritePosY,
                    SettingsWindowX = SettingsWindowX,
                    SettingsWindowY = SettingsWindowY,
                    SettingsWindowWidth = SettingsWindowWidth,
                    SettingsWindowHeight = SettingsWindowHeight,
                    SettingsWindowMaximized = SettingsWindowMaximized
                };
                string json = AvionicsConfigParser.SerializeThemeSettings(data, true);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatTheme, ex, "Failed to save theme settings");
            }
        }

        public void LoadSettings()
        {
            if (!File.Exists(SettingsFilePath))
            {
                SaveSettings();
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                var data = AvionicsConfigParser.ParseThemeSettings(json, out string err);
                if (data != null)
                {
                    if (!string.IsNullOrEmpty(data.SelectedThemeId))
                    {
                        var found = AvailableThemes.Find(t => t.ThemeId == data.SelectedThemeId);
                        if (found != null) CurrentTheme = found;
                    }
                    if (!string.IsNullOrEmpty(data.SelectedLanguage) && data.SelectedLanguage != "auto")
                    {
                        I18nManager.Instance.SetLanguage(data.SelectedLanguage, false);
                    }
                    _globalRenderMode = (NavballRenderMode)Mathf.Clamp(data.RenderMode, 0, 3);
                    if (_globalRenderMode == NavballRenderMode.ProceduralBake) _globalRenderMode = NavballRenderMode.ProceduralVector;
                    IsStockNavballHidden = data.HideStockNavball;
                    IsStockAltimeterHidden = data.HideStockAltimeter;
                    IsStockBottomLeftHidden = data.HideStockBottomLeft;
                    IsStockTimeWarpHidden = data.HideStockTimeWarp;
                    IsStockCommNetHidden = data.HideStockCommNet;
                    IsStockToolbarHidden = data.HideStockToolbar;
                    ToolbarStyleMode = data.ToolbarStyleMode;
                    NonFlightToolbarMode = data.NonFlightToolbarMode;
                    MasterBypass = data.MasterBypass;
                    ShowPerformanceBadge = data.ShowPerformanceBadge;
                    EnableGpu2DUIAcceleration = data.EnableGpu2DUIAcceleration;
                    if (data.DockRules != null) DockRules = data.DockRules;
                    DockShowHiddenDrawer = data.DockShowHiddenDrawer;
                    DockOrientation = data.DockOrientation;
                    DockEnableFavoritePanel = data.DockEnableFavoritePanel;
                    DockFavoriteOrientation = data.DockFavoriteOrientation;
                    DockKeepFavoritesInMain = data.DockKeepFavoritesInMain;
                    DockFavoritePosX = data.DockFavoritePosX;
                    DockFavoritePosY = data.DockFavoritePosY;
                    SettingsWindowX = data.SettingsWindowX;
                    SettingsWindowY = data.SettingsWindowY;
                    SettingsWindowWidth = data.SettingsWindowWidth > 0f ? data.SettingsWindowWidth : 1040f;
                    SettingsWindowHeight = data.SettingsWindowHeight > 0f ? data.SettingsWindowHeight : 650f;
                    SettingsWindowMaximized = data.SettingsWindowMaximized;

                    if (WidgetRenderManager.Instance != null)
                    {
                        WidgetRenderManager.Instance.AutoAdaptResolution = data.AutoAdaptResolution;
                        WidgetRenderManager.Instance.GlobalRenderScaleMultiplier = data.GlobalRenderScaleMultiplier > 0.05f ? data.GlobalRenderScaleMultiplier : 1.0f;
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatTheme, ex, "Failed to load theme settings");
            }
        }

        public void LoadAllThemes()
        {
            AvailableThemes.Clear();

            // 1. 全部以 Shader + ThemeConfig 内置规范驱动的 9 大高品质主题为主体 (单一权威来源，杜绝外部残缺 JSON 干扰覆盖)
            AvailableThemes.AddRange(ThemeConfig.GetAllBuiltinThemes());

            // 2. 如果用户在 Themes 目录下放置了自定义扩展主题 (非内置 9 款 ID)，可选注入
            if (Directory.Exists(ThemesDirectory))
            {
                string[] files = Directory.GetFiles(ThemesDirectory, "*.json");
                foreach (string file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        ThemeConfig theme = JsonUtility.FromJson<ThemeConfig>(json);
                        if (theme != null && !string.IsNullOrEmpty(theme.ThemeId))
                        {
                            int existingIdx = AvailableThemes.FindIndex(t => t.ThemeId == theme.ThemeId);
                            if (existingIdx < 0)
                            {
                                AvailableThemes.Add(theme);
                            }
                            else
                            {
                                AvailableThemes[existingIdx] = theme;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Exception(MFPLogger.CatTheme, ex, $"Failed to parse custom theme file '{file}'");
                    }
                }
            }
        }

        public void SetTheme(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) return;
            string cleanId = themeId.Trim();
            ThemeConfig target = AvailableThemes.Find(t =>
                string.Equals(t.ThemeId, cleanId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.ThemeId.Replace("_", ""), cleanId.Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                t.DisplayName.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0);

            if (target != null)
            {
                CurrentTheme = target;
                SaveSettings();
                OnThemeChanged?.Invoke(CurrentTheme);
                MFPLogger.Info(MFPLogger.CatTheme, $"Switched theme to: {CurrentTheme.DisplayName}");
            }
            else
            {
                MFPLogger.Warn(MFPLogger.CatTheme, $"Theme not found for query: '{themeId}'");
            }
        }

        public void SetTheme(ThemeConfig theme)
        {
            if (theme != null)
            {
                CurrentTheme = theme;
                SaveSettings();
                OnThemeChanged?.Invoke(CurrentTheme);
            }
        }

        public bool IsBuiltinTheme(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) return false;
            var builtins = ThemeConfig.GetAllBuiltinThemes();
            return builtins.Exists(b => b.ThemeId.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        }

        public void SaveCustomTheme(ThemeConfig theme)
        {
            if (theme == null || string.IsNullOrEmpty(theme.ThemeId)) return;
            try
            {
                if (!Directory.Exists(ThemesDirectory)) Directory.CreateDirectory(ThemesDirectory);
                string path = Path.Combine(ThemesDirectory, $"{theme.ThemeId}.json");
                string json = JsonUtility.ToJson(theme, true);
                File.WriteAllText(path, json);

                int idx = AvailableThemes.FindIndex(t => t.ThemeId == theme.ThemeId);
                if (idx >= 0) AvailableThemes[idx] = theme;
                else AvailableThemes.Add(theme);

                SetTheme(theme);
                MFPLogger.Info(MFPLogger.CatTheme, $"Custom theme saved & activated: {theme.DisplayName} ({path})");
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatTheme, ex, "Failed to save custom theme");
            }
        }

        public bool DeleteCustomTheme(string themeId)
        {
            if (IsBuiltinTheme(themeId)) return false;
            try
            {
                string path = Path.Combine(ThemesDirectory, $"{themeId}.json");
                if (File.Exists(path)) File.Delete(path);
                AvailableThemes.RemoveAll(t => t.ThemeId.Equals(themeId, StringComparison.OrdinalIgnoreCase));
                if (CurrentTheme?.ThemeId == themeId)
                {
                    SetTheme(AvailableThemes.Count > 0 ? AvailableThemes[0].ThemeId : "modern_aero");
                }
                MFPLogger.Info(MFPLogger.CatTheme, $"Custom theme deleted: {themeId}");
                return true;
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatTheme, ex, "Failed to delete custom theme");
                return false;
            }
        }

        public ThemeConfig CloneTheme(ThemeConfig source, string newThemeId, string newDisplayName)
        {
            if (source == null) return null;
            string json = JsonUtility.ToJson(source);
            ThemeConfig clone = JsonUtility.FromJson<ThemeConfig>(json);
            clone.ThemeId = newThemeId;
            clone.DisplayName = newDisplayName;
            return clone;
        }

        private readonly Dictionary<string, Material> _cachedUiMaterials = new Dictionary<string, Material>();

        /// <summary>
        /// 获取针对当前主题配置生成的 UI 面板 / 文字专用 Material
        /// 带有自动缓存复用，避免产生 GC 垃圾与重复 Draw Call
        /// </summary>
        public Material GetUiMaterial(ThemeConfig theme, bool isText = false)
        {
            if (theme == null) return null;
            string key = $"{theme.ThemeId}_{(int)theme.UiStyle}_{(isText ? "txt" : "panel")}";
            if (_cachedUiMaterials.TryGetValue(key, out Material mat) && mat != null)
            {
                return mat;
            }

            Shader s = null;
            switch (theme.UiStyle)
            {
                case UiShaderStyle.Dot_Matrix:
                    s = isText ? AssetLoader.DotMatrixShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Phosphor_HUD:
                    s = isText ? AssetLoader.PhosphorHoloShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Digital_Segment:
                    s = isText ? AssetLoader.DigitalSegmentShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Cyber_Neon:
                    s = isText ? AssetLoader.NeonGlowShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Modern_Glass:
                default:
                    s = isText ? AssetLoader.CrispAvionicsTextShader : AssetLoader.GlassCockpitShader;
                    break;
            }

            if (s == null) return null;

            mat = new Material(s);
            if (mat.HasProperty("_DotSpacing")) mat.SetFloat("_DotSpacing", theme.UiDotSpacing);
            if (mat.HasProperty("_GlowStrength")) mat.SetFloat("_GlowStrength", theme.UiGlowStrength);
            if (mat.HasProperty("_BloomStrength")) mat.SetFloat("_BloomStrength", theme.UiGlowStrength);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", theme.UiScanlineStrength);
            if (mat.HasProperty("_ScanlineDepth")) mat.SetFloat("_ScanlineDepth", theme.UiScanlineStrength);
            if (mat.HasProperty("_UnlitDotColor")) mat.SetColor("_UnlitDotColor", theme.UiGhostColor);
            if (mat.HasProperty("_PhosphorColor")) mat.SetColor("_PhosphorColor", theme.AccentPrimary);
            if (mat.HasProperty("_LitDotColor")) mat.SetColor("_LitDotColor", Color.white);
            if (mat.HasProperty("_SegmentLitColor")) mat.SetColor("_SegmentLitColor", theme.AccentPrimary);
            if (mat.HasProperty("_NeonGlowColor")) mat.SetColor("_NeonGlowColor", theme.AccentSecondary);

            _cachedUiMaterials[key] = mat;
            return mat;
        }
    }
}
