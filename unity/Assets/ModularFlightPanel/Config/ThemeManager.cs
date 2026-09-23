using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Config
{
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
            OnThemeChanged?.Invoke(CurrentTheme);
        }

        private string ThemesDirectory => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/Themes");
        private string SettingsFilePath => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/theme_settings.json");

        public void Initialize()
        {
            EnsureDirectoryAndDefaultThemes();
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
                    ShowPerformanceBadge = ShowPerformanceBadge
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
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to load theme settings: {ex.Message}");
            }
        }

        private void EnsureDirectoryAndDefaultThemes()
        {
            if (!Directory.Exists(ThemesDirectory))
            {
                Directory.CreateDirectory(ThemesDirectory);
            }

            SavePresetIfNotExists("cyber_neon.json", ThemeConfig.CreateCyberNeon());
            SavePresetIfNotExists("modern_aero.json", ThemeConfig.CreateModernAero());
            SavePresetIfNotExists("classic_aero.json", ThemeConfig.CreateClassicAero());
            SavePresetIfNotExists("apollo_1969.json", ThemeConfig.CreateApollo1969());
        }

        private void SavePresetIfNotExists(string fileName, ThemeConfig preset)
        {
            string path = Path.Combine(ThemesDirectory, fileName);
            if (!File.Exists(path))
            {
                string json = JsonUtility.ToJson(preset, true);
                File.WriteAllText(path, json);
            }
        }

        public void LoadAllThemes()
        {
            AvailableThemes.Clear();
            if (!Directory.Exists(ThemesDirectory)) return;

            string[] files = Directory.GetFiles(ThemesDirectory, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    ThemeConfig theme = JsonUtility.FromJson<ThemeConfig>(json);
                    if (theme != null && !string.IsNullOrEmpty(theme.ThemeId))
                    {
                        AvailableThemes.Add(theme);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModularFlightPanel] Failed to parse theme file '{file}': {ex.Message}");
                }
            }

            if (AvailableThemes.Count == 0)
            {
                AvailableThemes.Add(ThemeConfig.CreateCyberNeon());
                AvailableThemes.Add(ThemeConfig.CreateModernAero());
            }
        }

        public void SetTheme(string themeId)
        {
            ThemeConfig target = AvailableThemes.Find(t => t.ThemeId == themeId);
            if (target != null)
            {
                CurrentTheme = target;
                SaveSettings();
                OnThemeChanged?.Invoke(CurrentTheme);
                Debug.Log($"[ModularFlightPanel] Switched theme to: {CurrentTheme.DisplayName}");
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
    }
}
