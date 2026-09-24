using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Config
{
    [Serializable]
    public class DockButtonRule
    {
        public string Key = "";
        public string DefaultName = "";
        public string CustomLabel = "";
        public bool IsVisible = true;
    }

    [Serializable]
    public class ThemeSettingsData
    {
        public string SelectedThemeId = "modern_aero";
        public int RenderMode = 0; // 0 = Texture, 1 = Procedural
        public bool HideStockNavball = true;
        public bool HideStockAltimeter = false;
        public bool HideStockBottomLeft = false;
        public bool HideStockTimeWarp = false;
        public bool HideStockCommNet = false;
        public bool HideStockToolbar = false;
        public int ToolbarStyleMode = 1; // 0 = Stock, 1 = Reskin, 2 = ModernWidget
        public bool MasterBypass = false;
        public bool ShowPerformanceBadge = false;

        // 自适应渲染分辨率与超采样倍率设置 (Smart Resolution & Supersampling)
        public bool AutoAdaptResolution = true;
        public float GlobalRenderScaleMultiplier = 1.0f;

        // 收纳坞按钮自定义过滤与别名配置
        public List<DockButtonRule> DockRules = new List<DockButtonRule>();
        public bool DockShowHiddenDrawer = false;
        public int DockOrientation = 0; // 0 = 纵向双列, 1 = 横向双行, 2 = 横向单行
    }

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
        public List<DockButtonRule> DockRules { get; set; } = new List<DockButtonRule>();
        public bool DockShowHiddenDrawer { get; set; } = false;
        public int DockOrientation { get; set; } = 0;

        public DockButtonRule GetOrCreateDockRule(string key, string defaultName)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (DockRules == null) DockRules = new List<DockButtonRule>();
            var rule = DockRules.Find(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                rule = new DockButtonRule
                {
                    Key = key,
                    DefaultName = defaultName ?? key,
                    CustomLabel = "",
                    IsVisible = true
                };
                DockRules.Add(rule);
            }
            else if (string.IsNullOrEmpty(rule.DefaultName) && !string.IsNullOrEmpty(defaultName))
            {
                rule.DefaultName = defaultName;
            }
            return rule;
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

        private NavballRenderMode _globalRenderMode = NavballRenderMode.Texture;
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
                    RenderMode = (int)_globalRenderMode,
                    HideStockNavball = IsStockNavballHidden,
                    HideStockAltimeter = IsStockAltimeterHidden,
                    HideStockBottomLeft = IsStockBottomLeftHidden,
                    HideStockTimeWarp = IsStockTimeWarpHidden,
                    HideStockCommNet = IsStockCommNetHidden,
                    HideStockToolbar = IsStockToolbarHidden,
                    ToolbarStyleMode = ToolbarStyleMode,
                    MasterBypass = MasterBypass,
                    ShowPerformanceBadge = ShowPerformanceBadge,
                    AutoAdaptResolution = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.AutoAdaptResolution : true,
                    GlobalRenderScaleMultiplier = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.GlobalRenderScaleMultiplier : 1.0f,
                    DockRules = DockRules != null ? new List<DockButtonRule>(DockRules) : new List<DockButtonRule>(),
                    DockShowHiddenDrawer = DockShowHiddenDrawer,
                    DockOrientation = DockOrientation
                };
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to save theme settings: {ex.Message}");
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
                var data = JsonUtility.FromJson<ThemeSettingsData>(json);
                if (data != null)
                {
                    if (!string.IsNullOrEmpty(data.SelectedThemeId))
                    {
                        var found = AvailableThemes.Find(t => t.ThemeId == data.SelectedThemeId);
                        if (found != null) CurrentTheme = found;
                    }
                    if (Enum.IsDefined(typeof(NavballRenderMode), data.RenderMode))
                    {
                        _globalRenderMode = (NavballRenderMode)data.RenderMode;
                    }
                    IsStockNavballHidden = data.HideStockNavball;
                    IsStockAltimeterHidden = data.HideStockAltimeter;
                    IsStockBottomLeftHidden = data.HideStockBottomLeft;
                    IsStockTimeWarpHidden = data.HideStockTimeWarp;
                    IsStockCommNetHidden = data.HideStockCommNet;
                    IsStockToolbarHidden = data.HideStockToolbar;
                    ToolbarStyleMode = data.ToolbarStyleMode;
                    MasterBypass = data.MasterBypass;
                    ShowPerformanceBadge = data.ShowPerformanceBadge;
                    if (data.DockRules != null) DockRules = data.DockRules;
                    DockShowHiddenDrawer = data.DockShowHiddenDrawer;
                    DockOrientation = data.DockOrientation;

                    if (WidgetRenderManager.Instance != null)
                    {
                        WidgetRenderManager.Instance.AutoAdaptResolution = data.AutoAdaptResolution;
                        WidgetRenderManager.Instance.GlobalRenderScaleMultiplier = data.GlobalRenderScaleMultiplier > 0.05f ? data.GlobalRenderScaleMultiplier : 1.0f;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to load theme settings: {ex.Message}");
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
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ModularFlightPanel] Failed to parse custom theme file '{file}': {ex.Message}");
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
                Debug.Log($"[ModularFlightPanel] Switched theme to: {CurrentTheme.DisplayName}");
            }
            else
            {
                Debug.LogWarning($"[ModularFlightPanel] Theme not found for query: '{themeId}'");
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
                Debug.Log($"[ModularFlightPanel] Custom theme saved & activated: {theme.DisplayName} ({path})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to save custom theme: {ex.Message}");
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
                Debug.Log($"[ModularFlightPanel] Custom theme deleted: {themeId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to delete custom theme: {ex.Message}");
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
                    s = isText ? null : AssetLoader.GlassCockpitShader;
                    break;
            }

            if (s == null) return null;

            mat = new Material(s);
            if (mat.HasProperty("_DotSpacing")) mat.SetFloat("_DotSpacing", theme.UiDotSpacing);
            if (mat.HasProperty("_GlowStrength")) mat.SetFloat("_GlowStrength", theme.UiGlowStrength);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", theme.UiScanlineStrength);
            if (mat.HasProperty("_UnlitDotColor")) mat.SetColor("_UnlitDotColor", theme.UiGhostColor);
            if (mat.HasProperty("_PhosphorColor")) mat.SetColor("_PhosphorColor", theme.AccentPrimary);
            if (mat.HasProperty("_LitDotColor")) mat.SetColor("_LitDotColor", theme.AccentPrimary);
            if (mat.HasProperty("_SegmentLitColor")) mat.SetColor("_SegmentLitColor", theme.AccentPrimary);

            _cachedUiMaterials[key] = mat;
            return mat;
        }
    }
}
