using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Settings;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 全新模块化航电暗晶工程工作台 (Modular Avionics Cyber-Dark Workbench - Alt+N)
    /// 核心特性：
    /// 1. 双重输入穿透防护 (MFPInputLock)：悬浮阻断背景 3D 相机旋转与误点零件，输入时独占键盘阻断热键泄露。
    /// 2. 屏幕自适应居中与拖拽边界吸附 (Adaptive Resolution & Bounds Clamp)。
    /// 3. ESC / Alt+N 优雅关闭、暂存防抖提交与即时落盘。
    /// 4. 完美联动现代暗晶航电设计系统 2.0 (MFPGuiSkin)。
    /// </summary>
    public class SettingsGUI : MonoBehaviour
    {
        private static SettingsGUI _instance;
        public static SettingsGUI Instance => _instance;
        public static Action<bool> OnWindowStateChanged;

        private bool _isOpen = false;
        public bool IsOpen => _isOpen;

        public const float DefaultWindowWidth = 1040f;
        public const float DefaultWindowHeight = 650f;
        public const float MinWindowWidth = 840f;
        public const float MinWindowHeight = 440f;
        public const float WindowWidth = DefaultWindowWidth;
        public const float WindowHeight = DefaultWindowHeight;

        public static float ContentHeight => Instance != null ? Instance.CurrentContentHeight : 480f;
        public float CurrentContentHeight => Mathf.Max(240f, _windowRect.height - 170f);

        private Rect _windowRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);
        private int _windowId = 849204;
        private bool _rectInitialized = false;

        // 自由拉伸与全自适应视口状态机
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Vector2 _resizeStartWindowSize;
        private bool _isMaximized = false;
        private Rect _preMaximizeRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);

        private int _currentTab = 1; // 默认打开遥测装配台
        private readonly string[] TabTitles = new string[]
        {
            I18n.Tr("UI_TAB_LIBRARY", "📦 航电库"),
            I18n.Tr("UI_TAB_ASSEMBLER", "🛠️ 遥测装配台"),
            I18n.Tr("UI_TAB_MANAGER", "📋 挂载清单"),
            I18n.Tr("UI_TAB_THEMES", "🎨 视觉风格"),
            I18n.Tr("UI_TAB_PROFILES", "💾 档案与配置"),
            I18n.Tr("UI_TAB_SANDBOX", "🚀 仿真沙盒")
        };

        private void Awake()
        {
            _instance = this;
            UIWidget.OnRequestOpenWorkbench = ToggleWindow;
            MFPToastBridge.OnShowToast = (msg) => Settings.MFPGuiSkin.ShowToast(msg);
            I18nManager.OnLanguageChanged += HandleLanguageChanged;
            UpdateTabTitles();
        }

        private void UpdateTabTitles()
        {
            TabTitles[0] = I18n.Tr("UI_TAB_LIBRARY", "📦 航电库");
            TabTitles[1] = I18n.Tr("UI_TAB_ASSEMBLER", "🛠️ 遥测装配台");
            TabTitles[2] = I18n.Tr("UI_TAB_MANAGER", "📋 挂载清单");
            TabTitles[3] = I18n.Tr("UI_TAB_THEMES", "🎨 视觉风格");
            TabTitles[4] = I18n.Tr("UI_TAB_PROFILES", "💾 档案与配置");
            TabTitles[5] = I18n.Tr("UI_TAB_SANDBOX", "🚀 仿真沙盒");
        }

        private void HandleLanguageChanged(string newLang)
        {
            UpdateTabTitles();
        }

        private void Update()
        {
            // F2 隐藏界面时不响应 Alt+N
            if (KSP.UI.UIMasterController.Instance != null && !KSP.UI.UIMasterController.Instance.IsUIShowing)
            {
                return;
            }

            // Alt + N 快捷键呼出/关闭
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.N))
            {
                ToggleWindow();
            }

            // 打开状态下按下 ESC 退出窗口
            if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                ToggleWindow();
            }
        }

        public void SwitchTab(int tabIndex)
        {
            if (_currentTab == 1) TabAssembler.CommitPendingSaves();
            _currentTab = Mathf.Clamp(tabIndex, 0, TabTitles.Length - 1);
        }

        public void OpenToTab(int tabIndex)
        {
            _isOpen = true;
            SwitchTab(tabIndex);
            OnWindowStateChanged?.Invoke(_isOpen);
        }

        public void ToggleWindow()
        {
            _isOpen = !_isOpen;
            if (!_isOpen)
            {
                // 关闭窗口时退出拖拽编辑模式、释放输入锁、提交暂存并持久化几何布局
                _isResizing = false;
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                TabAssembler.CommitPendingSaves();
                WidgetLayoutManager.Instance.SaveLayout();
                SaveWindowSettings();
                MFPInputLock.ReleaseAllLocks();
            }
            else
            {
                EnsureWindowRect();
                // 进入工作台时默认开启自由拖拽编辑模式
                WidgetDragHandler.IsEditModeActive = true;
            }
            OnWindowStateChanged?.Invoke(_isOpen);
        }

        private void EnsureWindowRect()
        {
            if (!_rectInitialized)
            {
                var tm = ThemeManager.Instance;
                float savedW = tm != null && tm.SettingsWindowWidth > 0f ? tm.SettingsWindowWidth : DefaultWindowWidth;
                float savedH = tm != null && tm.SettingsWindowHeight > 0f ? tm.SettingsWindowHeight : DefaultWindowHeight;
                bool savedMax = tm != null && tm.SettingsWindowMaximized;

                // 防御性校验：如果持久化的高度几乎占满屏幕且并非最大化，自动重置为标准默认高度
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

                // 防御越界：如果窗口跑出屏幕可视区，拉回中央
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
            _windowRect.width = Mathf.Clamp(_windowRect.width, MinWindowWidth, maxAllowedW);
            _windowRect.height = Mathf.Clamp(_windowRect.height, MinWindowHeight, maxAllowedH);

            float maxX = Mathf.Max(0f, Screen.width - _windowRect.width);
            float maxY = Mathf.Max(0f, Screen.height - _windowRect.height);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxX);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);
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
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_RESET_WINDOW", "✔ 已还原标准窗口尺寸 (1040x650)"));
        }

        public void ToggleMaximize()
        {
            if (!_isMaximized)
            {
                _preMaximizeRect = _windowRect;
                float maxW = Mathf.Max(MinWindowWidth, Screen.width * 0.94f);
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

        private void OnGUI()
        {
            if (!_isOpen) return;

            // F2 隐藏界面时不绘制设置面板
            if (KSP.UI.UIMasterController.Instance != null && !KSP.UI.UIMasterController.Instance.IsUIShowing)
            {
                return;
            }

            MFPGuiSkin.EnsureInitialized();
            GUI.skin = HighLogic.Skin;

            // 保持窗口在屏幕安全可视范围内
            ClampWindowToScreen();

            // 自由拉伸交互状态机 (安全兜底：如果外部抬起鼠标，确保释放拉伸状态)
            if (_isResizing && (Event.current.rawType == EventType.MouseUp || Event.current.type == EventType.MouseUp))
            {
                GUIUtility.hotControl = 0;
                _isResizing = false;
                SaveWindowSettings();
            }

            // 输入穿透安全防护
            bool isMouseOver = _windowRect.Contains(Event.current.mousePosition);
            MFPInputLock.SetWindowHoverLock(isMouseOver);

            bool isTextFocused = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
            MFPInputLock.SetKeyboardFocusLock(isTextFocused);

            // 严格保护用户指定或拖拽的窗口尺寸，严禁 GUILayout 内部弹性内容在帧间滚雪球无限撑大
            float targetW = _windowRect.width;
            float targetH = _windowRect.height;

            _windowRect = GUILayout.Window(
                _windowId,
                _windowRect,
                DrawWindowContent,
                "",
                MFPGuiSkin.WindowStyle,
                GUILayout.Width(targetW),
                GUILayout.Height(targetH)
            );

            // 恢复物理尺寸锁定，消解 GUILayout 内部弹性内容导致的尺寸漂移
            _windowRect.width = targetW;
            _windowRect.height = targetH;

            // 绘制后再次约束在安全屏幕视口内
            ClampWindowToScreen();
        }

        private void DrawWindowContent(int id)
        {
            GUILayout.BeginVertical();

            // =========================================================================
            // 1. 顶部控制栏与载具遥测状态条 (Aero Header & Quick Status Strip)
            // =========================================================================
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            // 标题徽章 (色标联动主题配色)
            string subTitle = I18n.Tr("UI_WORKBENCH_SUBTITLE", "航电工程工作台");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><b>MODULAR FLIGHT PANEL</b></color> <color=#{MFPGuiSkin.HexTextSecondary}><size=11>| {subTitle}</size></color>", GUILayout.Width(270f));

            GUILayout.FlexibleSpace();

            // 载具状态摘要 (状态色动态派生自主题)
            string vesselName = FlightTelemetryContext.Current?.VesselName ?? "---";
            string frameName = TelemetryTokenEngine.Evaluate("{FRAME}", FlightTelemetryContext.Current);
            double mfpMs = MFPProfiler.AvgTotalMs;
            float fps = MFPProfiler.CurrentFPS;

            string vesselLabel = I18n.Tr("UI_VESSEL", "载具");
            string frameLabel = I18n.Tr("UI_REF_FRAME", "参考系");
            string statusText = $"<color=#{MFPGuiSkin.HexTextSecondary}>{vesselLabel}: <color=#FFFFFF>{vesselName}</color> | {frameLabel}: <color=#{MFPGuiSkin.HexAccentCyan}>{frameName}</color> | MFP: <color=#{MFPGuiSkin.HexAccentGreen}>{mfpMs:F2}ms</color> | <color=#{MFPGuiSkin.HexAccentAmber}>{fps:F0} FPS</color></color>";
            GUILayout.Label(statusText);

            GUILayout.FlexibleSpace();

            // 自由拖拽编辑模式开关
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            GUIStyle dragBtnStyle = isEdit ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string dragBtn = isEdit ? I18n.Tr("UI_DRAG_MODE_ACTIVE", "🎯 [拖拽模式中] 点击锁定") : I18n.Tr("UI_DRAG_MODE_IDLE", "🎯 [开启自由拖拽]");
            if (GUILayout.Button(dragBtn, dragBtnStyle, GUILayout.Height(24f), GUILayout.Width(150f)))
            {
                WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                if (!WidgetDragHandler.IsEditModeActive)
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.SaveLayout();
                }
            }

            GUILayout.Space(6f);

            // 姿态球快速显隐开关
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isBallOn = navCfg == null || navCfg.IsEnabled;
            GUIStyle ballBtnStyle = isBallOn ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.WarningButtonStyle;
            string ballBtn = isBallOn ? I18n.Tr("UI_NAVBALL_ON", "🌐 姿态球: 开") : I18n.Tr("UI_NAVBALL_OFF", "🌐 姿态球: 关");
            if (GUILayout.Button(ballBtn, ballBtnStyle, GUILayout.Height(24f), GUILayout.Width(105f)))
            {
                if (navCfg != null)
                {
                    navCfg.IsEnabled = !navCfg.IsEnabled;
                }
                else
                {
                    navCfg = new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "3D 姿态球"), 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.Space(6f);

            // 全局语言快速切换按钮 (顶栏常驻)
            string curLang = I18nManager.Instance.CurrentLanguage;
            bool isZh = curLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
            string langBtnLabel = isZh ? I18n.Tr("UI_LANG_ZH", "🇨🇳 中文") : I18n.Tr("UI_LANG_EN", "🇺🇸 EN");
            string langTooltip = isZh ? I18n.Tr("UI_LANG_TIP_TO_EN", "点击切换至英文") : I18n.Tr("UI_LANG_TIP_TO_ZH", "点击切换至中文");
            if (GUILayout.Button(new GUIContent(langBtnLabel, langTooltip), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.Width(76f)))
            {
                string nextLang = isZh ? "en-US" : "zh-CN";
                I18nManager.Instance.SetLanguage(nextLang);
                ThemeManager.Instance.SaveSettings();
                MFPGuiSkin.ShowToast(isZh ? I18n.Tr("UI_TOAST_LANG_EN", "已切换为英文") : I18n.Tr("UI_TOAST_LANG_ZH", "已切换为简体中文"));
            }

            GUILayout.Space(6f);

            // 一键重置基线尺寸 (1040x650)
            if (GUILayout.Button(new GUIContent("⟲", I18n.Tr("UI_WINDOW_RESET", "重置窗口尺寸 (1040×650)")), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(26f), GUILayout.Height(24f)))
            {
                ResetToDefault();
            }

            GUILayout.Space(4f);

            // 一键最大化 / 还原尺寸
            string maxIcon = _isMaximized ? "⧉" : "⛶";
            string maxTip = _isMaximized ? I18n.Tr("UI_WINDOW_RESTORE", "还原窗口大小") : I18n.Tr("UI_WINDOW_MAXIMIZE", "最大化窗口 (适应屏幕)");
            if (GUILayout.Button(new GUIContent(maxIcon, maxTip), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(26f), GUILayout.Height(24f)))
            {
                ToggleMaximize();
            }

            GUILayout.Space(4f);

            // 顶栏关闭按钮
            if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f), GUILayout.Height(24f)))
            {
                ToggleWindow();
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(4f);

            // =========================================================================
            // 2. 标签导航栏 (Aero Tab Bar)
            // =========================================================================
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabTitles.Length; i++)
            {
                bool isSel = _currentTab == i;
                GUIStyle tabStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(TabTitles[i], tabStyle, GUILayout.Height(28f)))
                {
                    SwitchTab(i);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // =========================================================================
            // 3. 标签主体渲染 (Tab Content Area - 动态响应式 CurrentContentHeight 自适应铺满)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Height(CurrentContentHeight), GUILayout.MaxHeight(CurrentContentHeight));
            switch (_currentTab)
            {
                case 0:
                    TabLibrary.Draw();
                    break;
                case 1:
                    TabAssembler.Draw();
                    break;
                case 2:
                    TabWidgetManager.Draw(jumpId =>
                    {
                        SwitchTab(1);
                        TabAssembler.SetSelectedWidget(jumpId);
                    });
                    break;
                case 3:
                    TabThemeSettings.Draw();
                    break;
                case 4:
                    TabProfilesConfig.Draw();
                    break;
                case 5:
                    TabSimulation.Draw();
                    break;
            }
            GUILayout.EndVertical();

            // 弹性填充空间，确保底栏始终牢固锚定在窗口最底部，零像素跳变
            GUILayout.FlexibleSpace();

            // =========================================================================
            // 4. 底栏状态与快捷指令 (Footer Status Bar - 绝对恒定锚定)
            // =========================================================================
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            int totalWidgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets.Count ?? 0;
            string footerFmt = I18n.Tr("UI_FOOTER_STATUS", "当前布局: <b>{0}</b> 个组件 | 快捷键: <b>Alt+N / ESC</b> 关闭 | <b>F2</b> 隐藏全UI | <b>F10</b> 性能HUD | <b>F11</b> 纯净旁路");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{string.Format(footerFmt, totalWidgets)}</size></color>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button(I18n.Tr("UI_SAVE_AND_CLOSE", "✔ 保存配置并关闭 (Alt+N)"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(220f)))
            {
                ToggleWindow();
            }

            GUILayout.Space(18f); // 预留给右下角拉伸手柄的空隙

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndVertical();

            // 右下角折角拉伸放大交互手柄 (独立 ControlID，获得独占 HotControl，支持平滑缩放)
            int resizeControlId = GUIUtility.GetControlID("MFPSettingsResizeHandle".GetHashCode(), FocusType.Passive);
            Rect gripRect = new Rect(_windowRect.width - 24f, _windowRect.height - 24f, 24f, 24f);
            GUI.Label(gripRect, new GUIContent("◢", I18n.Tr("UI_RESIZE_GRIP_TIP", "按住并拖拽以自由调整窗口大小")), MFPGuiSkin.ResizeGripStyle);

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (gripRect.Contains(e.mousePosition) && e.button == 0)
                    {
                        GUIUtility.hotControl = resizeControlId;
                        _isResizing = true;
                        _resizeStartMousePos = e.mousePosition;
                        _resizeStartWindowSize = new Vector2(_windowRect.width, _windowRect.height);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == resizeControlId && _isResizing)
                    {
                        Vector2 delta = e.mousePosition - _resizeStartMousePos;
                        float maxAllowedW = Screen.width - _windowRect.x - 10f;
                        float maxAllowedH = Screen.height - _windowRect.y - 10f;
                        _windowRect.width = Mathf.Clamp(_resizeStartWindowSize.x + delta.x, MinWindowWidth, maxAllowedW);
                        _windowRect.height = Mathf.Clamp(_resizeStartWindowSize.y + delta.y, MinWindowHeight, maxAllowedH);
                        _isMaximized = false;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == resizeControlId)
                    {
                        GUIUtility.hotControl = 0;
                        _isResizing = false;
                        SaveWindowSettings();
                        e.Use();
                    }
                    break;
            }

            // 双区域平滑自由拖拽 (仅在未处于拉伸调整状态时响应)：
            if (GUIUtility.hotControl != resizeControlId && !_isResizing)
            {
                // 1. 顶栏拖动区域 (避开右侧控制按钮群约 470px)
                GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(80f, _windowRect.width - 470f), 44f));
                // 2. 底栏拖动区域 (避开右侧保存按钮与拉伸手柄约 260px)
                GUI.DragWindow(new Rect(0f, _windowRect.height - 36f, Mathf.Max(80f, _windowRect.width - 260f), 36f));
            }
        }
    }
}
