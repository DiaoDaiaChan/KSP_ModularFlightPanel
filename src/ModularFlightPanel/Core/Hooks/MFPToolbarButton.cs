using System;
using System.IO;
using UnityEngine;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版应用启动栏 (ApplicationLauncher / Toolbar) 按钮接入中枢
    /// 为 ModularFlightPanel 注册专属暗晶航电工具栏图标，支持点击切换设置菜单 (SettingsGUI)
    /// 并与 Alt+N / ESC 状态双向实时同步。
    /// </summary>
    public class MFPToolbarButton : MonoBehaviour
    {
        private static MFPToolbarButton _instance;
        public static MFPToolbarButton Instance => _instance;

#if KSP_RUNTIME
        private KSP.UI.Screens.ApplicationLauncherButton _btn;
#endif
        private Texture2D _iconTexture;

        private void Awake()
        {
            _instance = this;
            LoadIconTexture();
        }

        private void Start()
        {
#if KSP_RUNTIME
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);

            if (KSP.UI.Screens.ApplicationLauncher.Ready && _btn == null)
            {
                OnAppLauncherReady();
            }

            SettingsGUI.OnWindowStateChanged += HandleWindowStateChanged;
#endif
        }

        private void OnDestroy()
        {
#if KSP_RUNTIME
            SettingsGUI.OnWindowStateChanged -= HandleWindowStateChanged;
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            RemoveButton();
#endif
            if (_instance == this) _instance = null;
        }

#if KSP_RUNTIME
        private void OnAppLauncherReady()
        {
            if (_btn != null) return;
            if (KSP.UI.Screens.ApplicationLauncher.Instance == null) return;

            try
            {
                if (_iconTexture == null)
                {
                    LoadIconTexture();
                }

                _btn = KSP.UI.Screens.ApplicationLauncher.Instance.AddModApplication(
                    onTrue: OnButtonToggled,
                    onFalse: OnButtonToggled,
                    onHover: null,
                    onHoverOut: null,
                    onEnable: null,
                    onDisable: null,
                    visibleInScenes: KSP.UI.Screens.ApplicationLauncher.AppScenes.FLIGHT | KSP.UI.Screens.ApplicationLauncher.AppScenes.MAPVIEW,
                    texture: _iconTexture
                );

                if (_btn != null && SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen)
                {
                    _btn.SetTrue(false);
                }

                MFPLogger.Info(MFPLogger.CatUI, "ApplicationLauncher toolbar button registered successfully.");
            }
            catch (Exception ex)
            {
                MFPLogger.Warn(MFPLogger.CatUI, $"Failed to register ApplicationLauncher button: {ex.Message}");
            }
        }

        private void OnAppLauncherDestroyed()
        {
            RemoveButton();
        }

        private void RemoveButton()
        {
            if (_btn != null)
            {
                try
                {
                    if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                    {
                        KSP.UI.Screens.ApplicationLauncher.Instance.RemoveModApplication(_btn);
                    }
                }
                catch { }
                _btn = null;
            }
        }

        private void OnButtonToggled()
        {
            if (SettingsGUI.Instance != null)
            {
                SettingsGUI.Instance.ToggleWindow();
            }
        }

        private void HandleWindowStateChanged(bool isOpen)
        {
            if (_btn == null) return;
            try
            {
                if (isOpen)
                {
                    _btn.SetTrue(false);
                }
                else
                {
                    _btn.SetFalse(false);
                }
            }
            catch { }
        }
#endif

        private void LoadIconTexture()
        {
            // 1. 优先尝试从 GameDatabase 加载
#if KSP_RUNTIME
            try
            {
                if (GameDatabase.Instance != null)
                {
                    var tex = GameDatabase.Instance.GetTexture("ModularFlightPanel/Textures/toolbar_icon", false);
                    if (tex != null)
                    {
                        _iconTexture = tex;
                        return;
                    }
                }
            }
            catch { }
#endif

            // 2. 尝试从磁盘绝对路径加载 PNG
            try
            {
                string path = Path.Combine(AppPathHelper.RootPath, "GameData", "ModularFlightPanel", "Textures", "toolbar_icon.png");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(38, 38, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        _iconTexture = tex;
                        return;
                    }
                }

                // 2.5 备用路径 PluginData
                string altPath = Path.Combine(AppPathHelper.RootPath, "GameData", "ModularFlightPanel", "PluginData", "toolbar_icon.png");
                if (File.Exists(altPath))
                {
                    byte[] bytes = File.ReadAllBytes(altPath);
                    Texture2D tex = new Texture2D(38, 38, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        _iconTexture = tex;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn(MFPLogger.CatUI, $"Failed to load toolbar icon from disk: {ex.Message}");
            }

            // 3. 保底：程序化生成一个 38x38 科技蓝姿态球图标，杜绝粉红错位
            _iconTexture = CreateFallbackIcon();
        }

        private Texture2D CreateFallbackIcon()
        {
            Texture2D tex = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            Color cyan = new Color(0f, 0.9f, 1f, 1f);
            Color dark = new Color(0.06f, 0.12f, 0.2f, 0.9f);
            Color clear = Color.clear;

            for (int y = 0; y < 38; y++)
            {
                for (int x = 0; x < 38; x++)
                {
                    float dx = x - 18.5f;
                    float dy = y - 18.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > 16.5f)
                    {
                        tex.SetPixel(x, y, clear);
                    }
                    else if (dist >= 14.5f)
                    {
                        tex.SetPixel(x, y, cyan);
                    }
                    else
                    {
                        tex.SetPixel(x, y, dark);
                    }
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
