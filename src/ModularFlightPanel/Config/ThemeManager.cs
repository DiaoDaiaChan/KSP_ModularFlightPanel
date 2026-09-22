using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ModularFlightPanel.Config
{
    public class ThemeManager
    {
        private static ThemeManager _instance;
        public static ThemeManager Instance => _instance ?? (_instance = new ThemeManager());

        public List<ThemeConfig> AvailableThemes { get; private set; } = new List<ThemeConfig>();
        public ThemeConfig CurrentTheme { get; private set; }
        public NavballRenderMode GlobalRenderMode { get; set; } = NavballRenderMode.Texture;

        public event Action<ThemeConfig> OnThemeChanged;

        public void NotifyThemeChanged()
        {
            OnThemeChanged?.Invoke(CurrentTheme);
        }

        private string ThemesDirectory => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ModularFlightPanel/Themes");

        public void Initialize()
        {
            EnsureDirectoryAndDefaultThemes();
            LoadAllThemes();

            if (AvailableThemes.Count > 0)
            {
                CurrentTheme = AvailableThemes[0];
            }
            else
            {
                CurrentTheme = ThemeConfig.CreateCyberNeon();
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
                OnThemeChanged?.Invoke(CurrentTheme);
                Debug.Log($"[ModularFlightPanel] Switched theme to: {CurrentTheme.DisplayName}");
            }
        }

        public void SetTheme(ThemeConfig theme)
        {
            if (theme != null)
            {
                CurrentTheme = theme;
                OnThemeChanged?.Invoke(CurrentTheme);
            }
        }
    }
}
