using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Settings;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Workbench;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 模块化航电现代暗晶工程工作台控制器 (Modular Avionics Cyber-Dark Workbench Controller - Alt+N)
    /// 核心特性：
    /// 1. 托管快捷键路由 (Alt+N / ESC) 与窗体全生命周期；
    /// 2. 状态机驱动现代 UGUI 工作台 (WorkbenchCanvasView)；
    /// 3. 画布排版模式解耦与极简悬浮药丸 Dock；
    /// 4. 彻底剔除历史 IMGUI 渲染器与 Draw 循环，实现 0 GC 极速渲染。
    /// </summary>
    public class SettingsGUI : MonoBehaviour
    {
        private static SettingsGUI _instance;
        public static SettingsGUI Instance => _instance;
        public static Action<bool> OnWindowStateChanged;

        private bool _isOpen = false;
        public bool IsOpen => _isOpen;

        // 画布自由排版模式解耦状态机
        private bool _isCanvasLayoutMode = false;
        public bool IsCanvasLayoutMode => _isCanvasLayoutMode;

        public const float DefaultWindowWidth = 1060f;
        public const float DefaultWindowHeight = 670f;
        public const float MinWindowWidth = 860f;
        public const float MinWindowHeight = 460f;
        public const float WindowWidth = DefaultWindowWidth;
        public const float WindowHeight = DefaultWindowHeight;

        public static float ContentHeight => Instance != null ? Instance.CurrentContentHeight : 480f;
        public float CurrentContentHeight
        {
            get
            {
                float usedH = 190f + (MFPSafetyFallback.IsFaulted ? 48f : 0f);
                return Mathf.Max(240f, _windowRect.height - usedH);
            }
        }

        private Rect _windowRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);
        public Rect WindowRect => _windowRect;
        public bool IsMouseOverWindow => _isOpen && !_isCanvasLayoutMode && _windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        private bool _rectInitialized = false;

        private bool _isMaximized = false;
        private Rect _preMaximizeRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);

        private int _currentTabIndex = 0;
        private WorkbenchCanvasView _modernView;

        private void Awake()
        {
            _instance = this;
            _modernView = gameObject.GetComponent<WorkbenchCanvasView>() ?? gameObject.AddComponent<WorkbenchCanvasView>();

            UIWidget.OnRequestOpenWorkbench = ToggleWindow;
            MFPToastBridge.OnShowToast = (msg) => Settings.MFPGuiSkin.ShowToast(msg);
            I18nManager.OnLanguageChanged += HandleLanguageChanged;
        }

        private void HandleLanguageChanged(string newLang)
        {
            _modernView?.RefreshActiveTab();
        }

        private void Update()
        {
            // Alt + N 快捷键呼出/关闭
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.N))
            {
                if (KSP.UI.UIMasterController.Instance == null || KSP.UI.UIMasterController.Instance.IsUIShowing)
                {
                    if (_isCanvasLayoutMode)
                    {
                        ExitCanvasLayoutMode();
                    }
                    else
                    {
                        ToggleWindow();
                    }
                }
            }
            else if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                if (_isCanvasLayoutMode)
                {
                    ExitCanvasLayoutMode();
                }
                else
                {
                    ToggleWindow();
                }
            }
        }

        public void SwitchTab(int tabIndex)
        {
            _currentTabIndex = Mathf.Clamp(tabIndex, 0, 4);
            _modernView?.SwitchTab(_currentTabIndex);
        }

        /// <summary>
        /// 兼容旧版调用并自动重定向到对应的新标签页 (0:工坊, 1:主题, 2:档案, 3:遥测, 4:偏好)
        /// </summary>
        public void OpenToTab(int tabIndex)
        {
            _isOpen = true;
            _isCanvasLayoutMode = false;
            WidgetDragHandler.IsEditModeActive = true;

            EnsureWindowRect();

            // 向下兼容映射:
            // 0 (Library), 1 (Assembler), 2 (Manager) => 0 (Studio)
            // 3 (Themes) => 1 (Themes)
            // 4 (Profiles) => 2 (Profiles)
            // 5 (Simulation) => 3 (Diagnostics)
            int mapped = tabIndex;
            if (tabIndex <= 2) mapped = 0;
            else if (tabIndex == 3) mapped = 1;
            else if (tabIndex == 4) mapped = 2;
            else if (tabIndex >= 5) mapped = 3;

            SwitchTab(mapped);
            _modernView?.SetVisible(_isOpen, _isCanvasLayoutMode);
            OnWindowStateChanged?.Invoke(_isOpen);
        }

        public void ToggleWindow()
        {
            try
            {
                _isOpen = !_isOpen;

                if (!_isOpen)
                {
                    _isCanvasLayoutMode = false;
                    try
                    {
                        WidgetDragHandler.IsEditModeActive = false;
                        WidgetSelectionManager.ClearSelection();
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Warn(MFPLogger.CatUI, $"Error deactivating edit mode: {ex.Message}");
                    }

                    try
                    {
                        WidgetLayoutManager.Instance.SaveLayout();
                        SaveWindowSettings();
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Warn(MFPLogger.CatUI, $"Error committing layout saves: {ex.Message}");
                    }
                }
                else
                {
                    _isCanvasLayoutMode = false;
                    WidgetDragHandler.IsEditModeActive = true;
                    EnsureWindowRect();
                }

                _modernView?.SetVisible(_isOpen, _isCanvasLayoutMode);

                try
                {
                    OnWindowStateChanged?.Invoke(_isOpen);
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn(MFPLogger.CatUI, $"Error invoking OnWindowStateChanged: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Error(MFPLogger.CatUI, $"Error in ToggleWindow: {ex.Message}");
            }
            finally
            {
                if (!_isOpen)
                {
                    MFPInputLock.ReleaseAllLocks();
                }
            }
        }

        #region Canvas Layout Mode (解耦画布自由排版)

        public void EnterCanvasLayoutMode()
        {
            _isCanvasLayoutMode = true;
            WidgetDragHandler.IsEditModeActive = true;
            _modernView?.SetVisible(_isOpen, _isCanvasLayoutMode);
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_ENTER_CANVAS", "📐 已进入画布自由排版模式 (点击药丸栏返回工作台)"));
        }

        public void ExitCanvasLayoutMode()
        {
            _isCanvasLayoutMode = false;
            WidgetDragHandler.IsEditModeActive = true;
            WidgetSelectionManager.ClearSelection();
            WidgetLayoutManager.Instance.SaveLayout();
            _modernView?.SetVisible(_isOpen, _isCanvasLayoutMode);
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_EXIT_CANVAS", "✔ 已返回航电工程工作台"));
        }

        #endregion

        #region Window Geometry & State

        private void EnsureWindowRect()
        {
            if (!_rectInitialized)
            {
                var tm = ThemeManager.Instance;
                float savedW = tm != null && tm.SettingsWindowWidth > 0f ? tm.SettingsWindowWidth : DefaultWindowWidth;
                float savedH = tm != null && tm.SettingsWindowHeight > 0f ? tm.SettingsWindowHeight : DefaultWindowHeight;
                bool savedMax = tm != null && tm.SettingsWindowMaximized;

                if (!savedMax && (savedH >= Screen.height - 30f || savedH < MinWindowHeight))
                {
                    savedH = Mathf.Min(DefaultWindowHeight, Mathf.Max(MinWindowHeight, Screen.height - 80f));
                }
                if (savedW >= Screen.width - 10f || savedW < MinWindowWidth)
                {
                    savedW = Mathf.Min(DefaultWindowWidth, Mathf.Max(MinWindowWidth, Screen.width - 40f));
                }

                float savedX = tm != null && tm.SettingsWindowX >= 0f ? tm.SettingsWindowX : Mathf.Max(15f, (Screen.width - savedW) * 0.5f);
                float savedY = tm != null && tm.SettingsWindowY >= 0f ? tm.SettingsWindowY : Mathf.Max(15f, (Screen.height - savedH) * 0.5f);

                if (savedX > Screen.width - 80f || savedY > Screen.height - 80f || savedY < 5f)
                {
                    savedX = Mathf.Max(15f, (Screen.width - savedW) * 0.5f);
                    savedY = Mathf.Max(15f, (Screen.height - savedH) * 0.5f);
                }

                _windowRect = new Rect(savedX, savedY, savedW, savedH);
                _isMaximized = savedMax;
                _preMaximizeRect = new Rect(savedX, savedY, savedW, savedH);
                _rectInitialized = true;
            }
            ClampWindowToScreen();
        }

        private void ClampWindowToScreen()
        {
            float maxAllowedW = Mathf.Max(MinWindowWidth, Screen.width - 20f);
            float maxAllowedH = Mathf.Max(MinWindowHeight, Screen.height - 20f);
            _windowRect.width = Mathf.Round(Mathf.Clamp(_windowRect.width, MinWindowWidth, maxAllowedW));
            _windowRect.height = Mathf.Round(Mathf.Clamp(_windowRect.height, MinWindowHeight, maxAllowedH));

            float maxX = Mathf.Max(0f, Screen.width - _windowRect.width);
            float maxY = Mathf.Max(0f, Screen.height - _windowRect.height);
            _windowRect.x = Mathf.Round(Mathf.Clamp(_windowRect.x, 0f, maxX));
            _windowRect.y = Mathf.Round(Mathf.Clamp(_windowRect.y, 0f, maxY));
        }

        private void SaveWindowSettings()
        {
            if (ThemeManager.Instance == null) return;
            ThemeManager.Instance.SettingsWindowX = _windowRect.x;
            ThemeManager.Instance.SettingsWindowY = _windowRect.y;
            ThemeManager.Instance.SettingsWindowWidth = _windowRect.width;
            ThemeManager.Instance.SettingsWindowHeight = _windowRect.height;
            ThemeManager.Instance.SettingsWindowMaximized = _isMaximized;
            ThemeManager.Instance.SaveSettings();
        }

        public void ResetToDefault()
        {
            _isMaximized = false;
            float defaultH = Mathf.Min(DefaultWindowHeight, Mathf.Max(MinWindowHeight, Screen.height - 80f));
            float defaultW = Mathf.Min(DefaultWindowWidth, Mathf.Max(MinWindowWidth, Screen.width - 40f));
            _windowRect.width = defaultW;
            _windowRect.height = defaultH;
            _windowRect.x = Mathf.Max(15f, (Screen.width - defaultW) * 0.5f);
            _windowRect.y = Mathf.Max(15f, (Screen.height - defaultH) * 0.5f);
            ClampWindowToScreen();
            SaveWindowSettings();
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_RESET_WINDOW", "✔ 已还原标准窗口尺寸 (1060×670)"));
        }

        public void ToggleMaximize()
        {
            if (!_isMaximized)
            {
                _preMaximizeRect = _windowRect;
                float maxW = Mathf.Max(MinWindowWidth, Screen.width * 0.95f);
                float maxH = Mathf.Max(MinWindowHeight, Screen.height * 0.92f);
                float posX = Mathf.Max(0f, (Screen.width - maxW) * 0.5f);
                float posY = Mathf.Max(0f, (Screen.height - maxH) * 0.5f);
                _windowRect = new Rect(posX, posY, maxW, maxH);
                _isMaximized = true;
                SaveWindowSettings();
                MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_MAXIMIZE_WINDOW", "⛶ 已最大化窗口"));
            }
            else
            {
                _isMaximized = false;
                _windowRect = _preMaximizeRect;
                ClampWindowToScreen();
                SaveWindowSettings();
                MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_RESTORE_WINDOW", "⧉ 已还原窗口尺寸"));
            }
        }

        private void OnDisable()
        {
            MFPInputLock.ReleaseAllLocks();
        }

        private void OnDestroy()
        {
            if (UIWidget.OnRequestOpenWorkbench == ToggleWindow)
            {
                UIWidget.OnRequestOpenWorkbench = null;
            }
            I18nManager.OnLanguageChanged -= HandleLanguageChanged;
            MFPInputLock.ReleaseAllLocks();
        }

        #endregion
    }
}
