using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Settings;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 全新模块化航电暗晶工程工作台 2.0 (Modular Avionics Cyber-Dark Workbench - Alt+N)
    /// 核心特性：
    /// 1. 4 大核心功能中枢架构：[🛠️ 航电设计工坊]、[🎨 视觉风格与主题]、[💾 档案与预设中枢]、[🚀 诊断与遥测沙盒]。
    /// 2. 解耦画布排版模式 (Canvas Layout Mode)：一键收拢为极简悬浮药丸 Dock，彻底消除视口遮挡与编辑手柄互相打架。
    /// 3. 精确动态响应式视口系统：彻底废除硬编码高度魔法减法，支持自由拉伸与多分辨率完美自适应。
    /// 4. 双重输入穿透防护 (MFPInputLock)：悬浮阻断背景 3D 相机旋转与误点零件，输入时独占键盘阻断热键泄露。
    /// 5. ESC / Alt+N 优雅呼出与关闭、脏标记防抖提交与即时落盘。
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
                // 窗口内边距 (30f) + 顶部卡片 (46f) + 标签栏 (36f) + 底部栏 (48f) + 间距 (30f) = 190f
                float usedH = 190f + (MFPSafetyFallback.IsFaulted ? 48f : 0f);
                return Mathf.Max(240f, _windowRect.height - usedH);
            }
        }

        private Rect _windowRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);
        public Rect WindowRect => _windowRect;
        public bool IsMouseOverWindow => _isOpen && !_isCanvasLayoutMode && _windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        private int _windowId = 849204;
        private bool _rectInitialized = false;

        // 自由拉伸交互状态机
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Vector2 _resizeStartWindowSize;
        private bool _isMaximized = false;
        private Rect _preMaximizeRect = new Rect(100f, 60f, DefaultWindowWidth, DefaultWindowHeight);

        private ISettingsTab[] _tabs;
        private int _currentTab = 0; // 默认打开 0: 航电设计工坊 (TabStudio)
        public ISettingsTab CurrentTabInstance => (_tabs != null && _currentTab >= 0 && _currentTab < _tabs.Length) ? _tabs[_currentTab] : null;

        private SettingsGUIDrawer _drawer;

        private void Awake()
        {
            _instance = this;
            _drawer = gameObject.GetComponent<SettingsGUIDrawer>() ?? gameObject.AddComponent<SettingsGUIDrawer>();
            _drawer.Owner = this;
            _drawer.enabled = false;

            // 实例化 4 大功能中枢
            _tabs = new ISettingsTab[]
            {
                new TabStudio(),
                new TabThemeSettings(),
                new TabProfilesConfig(),
                new TabDiagnostics()
            };

            UIWidget.OnRequestOpenWorkbench = ToggleWindow;
            MFPToastBridge.OnShowToast = (msg) => Settings.MFPGuiSkin.ShowToast(msg);
            I18nManager.OnLanguageChanged += HandleLanguageChanged;
        }

        private void HandleLanguageChanged(string newLang)
        {
            // 语言变更时自动触发重绘与文本刷新
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
            if (_tabs == null || _tabs.Length == 0) return;
            int target = Mathf.Clamp(tabIndex, 0, _tabs.Length - 1);
            if (_currentTab != target)
            {
                _tabs[_currentTab]?.OnExit();
                _currentTab = target;
                _tabs[_currentTab]?.OnEnter();
            }
        }

        /// <summary>
        /// 兼容旧版调用并自动重定向到对应的新 4 级标签页
        /// </summary>
        public void OpenToTab(int tabIndex)
        {
            _isOpen = true;
            _isCanvasLayoutMode = false;
            WidgetDragHandler.IsEditModeActive = true;

            if (_drawer != null && !_drawer.enabled)
            {
                _drawer.enabled = true;
            }
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
            OnWindowStateChanged?.Invoke(_isOpen);
        }

        public void ToggleWindow()
        {
            try
            {
                _isOpen = !_isOpen;
                if (_drawer != null && _drawer.enabled != _isOpen)
                {
                    _drawer.enabled = _isOpen;
                }

                if (!_isOpen)
                {
                    // 关闭工作台时彻底退出排版模式、释放输入锁并提交保存
                    _isCanvasLayoutMode = false;
                    _isResizing = false;
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
                        if (_tabs != null && _currentTab >= 0 && _currentTab < _tabs.Length)
                        {
                            _tabs[_currentTab]?.OnExit();
                        }
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
                    if (_tabs != null && _currentTab >= 0 && _currentTab < _tabs.Length)
                    {
                        _tabs[_currentTab]?.OnEnter();
                    }
                }

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
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_ENTER_CANVAS", "📐 已进入画布自由排版模式 (点击药丸栏返回工作台)"));
        }

        public void ExitCanvasLayoutMode()
        {
            _isCanvasLayoutMode = false;
            WidgetDragHandler.IsEditModeActive = true;
            WidgetSelectionManager.ClearSelection();
            WidgetLayoutManager.Instance.SaveLayout();
            MFPGuiSkin.ShowToast(I18n.Tr("UI_TOAST_EXIT_CANVAS", "✔ 已返回航电工程工作台"));
        }

        private void DrawCanvasModeFloatingDock()
        {
            float dockW = 540f;
            float dockH = 40f;
            float dockX = (Screen.width - dockW) * 0.5f;
            float dockY = Screen.height - 54f; // 牢牢锚定在屏幕最底端，零遮挡
            Rect dockRect = new Rect(dockX, dockY, dockW, dockH);

            bool isMouseOver = dockRect.Contains(Event.current.mousePosition);
            if (isMouseOver) FlightHUDManager.IsMouseOverFloatingToolbar = true;
            MFPInputLock.SetWindowHoverLock(isMouseOver);

            GUILayout.BeginArea(dockRect, MFPGuiSkin.CardStyle);
            GUILayout.BeginHorizontal();

            GUILayout.Label($"📐 <color=#{MFPGuiSkin.HexAccentCyan}><b>{I18n.Tr("UI_CANVAS_DOCK_TITLE", "画布自由排版")}</b></color>", GUILayout.Width(115f));

            int selCount = WidgetSelectionManager.Count;
            string selInfo = selCount > 0 ? string.Format(I18n.Tr("UI_CANVAS_SEL_COUNT", "已选 {0} 项"), selCount) : I18n.Tr("UI_CANVAS_DRAG_HINT", "拖拽/旋转调整中");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentAmber}><size=11>{selInfo}</size></color>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button(I18n.Tr("UI_CANVAS_BTN_ARTBOARD", "🎨 +画板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(76f), GUILayout.Height(24f)))
            {
                TabStudio.CreateNewArtboard(false);
            }

            if (GUILayout.Button(I18n.Tr("UI_CANVAS_RETURN_WORKBENCH", "✔ 返回工坊 (Alt+N)"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
            {
                ExitCanvasLayoutMode();
            }

            if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f), GUILayout.Height(24f)))
            {
                ToggleWindow();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        #endregion

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

        public void RenderGUI()
        {
            if (!_isOpen) return;

            // F2 隐藏界面时不绘制设置面板
            if (KSP.UI.UIMasterController.Instance != null && !KSP.UI.UIMasterController.Instance.IsUIShowing)
            {
                return;
            }

            MFPGuiSkin.EnsureInitialized();
            GUI.skin = HighLogic.Skin;

            // 若处于画布自由排版模式，只绘制极简浮动 Dock
            if (_isCanvasLayoutMode)
            {
                DrawCanvasModeFloatingDock();
                return;
            }

            ClampWindowToScreen();

            // 自由拉伸交互状态机安全兜底
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

            _windowRect.width = targetW;
            _windowRect.height = targetH;
            ClampWindowToScreen();
        }

        private void DrawWindowContent(int id)
        {
            SafeGUIGateway.ExecuteWindowContent(id, () => DrawWindowContentInternal(id), () => SwitchTab(0), "SettingsWindow");
        }

        private void DrawWindowContentInternal(int id)
        {
            GUILayout.BeginVertical();

            // =========================================================================
            // 1. 顶部控制栏与载具遥测状态条 (Aero Header & Quick Status Strip)
            // =========================================================================
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            // 标题徽章
            string subTitle = I18n.Tr("UI_WORKBENCH_SUBTITLE", "航电工程工作台");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><b>MODULAR FLIGHT PANEL</b></color> <color=#{MFPGuiSkin.HexTextSecondary}><size=11>| {subTitle}</size></color>", GUILayout.Width(260f));

            GUILayout.FlexibleSpace();

            // 载具状态摘要
            string vesselName = FlightTelemetryContext.Current?.VesselName ?? "---";
            string frameName = TelemetryTokenEngine.Evaluate("{FRAME}", FlightTelemetryContext.Current);
            double mfpMs = MFPProfiler.AvgTotalMs;
            float fps = MFPProfiler.CurrentFPS;

            string vesselLabel = I18n.Tr("UI_VESSEL", "载具");
            string frameLabel = I18n.Tr("UI_REF_FRAME", "参考系");
            string statusText = $"<color=#{MFPGuiSkin.HexTextSecondary}>{vesselLabel}: <color=#FFFFFF>{vesselName}</color> | {frameLabel}: <color=#{MFPGuiSkin.HexAccentCyan}>{frameName}</color> | MFP: <color=#{MFPGuiSkin.HexAccentGreen}>{mfpMs:F2}ms</color> | <color=#{MFPGuiSkin.HexAccentAmber}>{fps:F0} FPS</color></color>";
            GUILayout.Label(statusText);

            GUILayout.FlexibleSpace();

            // 🎨 自由航电画板快捷创建入口 (一键创建 PS 自由搭建面板)
            if (GUILayout.Button(I18n.Tr("UI_BTN_NEW_ARTBOARD", "🎨 新建自由画板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(24f), GUILayout.Width(126f)))
            {
                SwitchTab(0);
                TabStudio.CreateNewArtboard(false);
            }

            GUILayout.Space(6f);

            // 画布自由排版模式入口 (点击后折叠工作台为屏幕底部药丸栏，留出全屏无遮挡自由拖拽与排版)
            if (GUILayout.Button(I18n.Tr("UI_BTN_CANVAS_MODE", "📐 画布自由排版"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(24f), GUILayout.Width(130f)))
            {
                EnterCanvasLayoutMode();
            }

            GUILayout.Space(6f);

            // 语言切换按钮
            string curLang = I18nManager.Instance.CurrentLanguage;
            bool isZh = curLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
            string langBtnLabel = isZh ? I18n.Tr("UI_LANG_ZH", "🇨🇳 中文") : I18n.Tr("UI_LANG_EN", "🇺🇸 EN");
            if (GUILayout.Button(langBtnLabel, MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.Width(72f)))
            {
                string nextLang = isZh ? "en-US" : "zh-CN";
                I18nManager.Instance.SetLanguage(nextLang);
                ThemeManager.Instance.SaveSettings();
                MFPGuiSkin.ShowToast(isZh ? I18n.Tr("UI_TOAST_LANG_EN", "已切换为英文") : I18n.Tr("UI_TOAST_LANG_ZH", "已切换为简体中文"));
            }

            GUILayout.Space(6f);

            // 顶栏关闭按钮
            if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f), GUILayout.Height(24f)))
            {
                ToggleWindow();
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            // =========================================================================
            // 1.5 故障熔断警告栏 (Fault Fallback Banner)
            // =========================================================================
            if (MFPSafetyFallback.IsFaulted)
            {
                GUILayout.Space(3f);
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(I18n.Tr("UI_FAULT_TITLE", "<color=#FF4444><b>⚠ 航电安全熔断已激活 (Avionics Circuit Breaker Active)</b></color>"), GUI.skin.label);
                GUILayout.Label(string.Format(I18n.Tr("UI_FAULT_DESC", "原因: {0}\n当前已切断 MFP 渲染并 100% 恢复原生原版界面。"), MFPSafetyFallback.FaultReason), GUI.skin.label);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.Tr("UI_FAULT_BTN_RETRY", "🔄 尝试恢复 MFP"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(30f), GUILayout.Width(130f)))
                {
                    bool ok = MFPSafetyFallback.TryRecoverFromFault();
                    MFPGuiSkin.ShowToast(ok ? I18n.Tr("UI_TOAST_FAULT_RECOVERED", "✔ 航电系统已尝试恢复装配") : I18n.Tr("UI_TOAST_FAULT_RETRY_FAIL", "✖ 恢复失败"));
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
            }

            GUILayout.Space(4f);

            // =========================================================================
            // 2. 4 大核心功能标签导航栏 (Aero Tab Bar)
            // =========================================================================
            GUILayout.BeginHorizontal();
            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.Length; i++)
                {
                    bool isSel = _currentTab == i;
                    GUIStyle tabStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                    if (GUILayout.Button(_tabs[i].DisplayTitle, tabStyle, GUILayout.Height(28f)))
                    {
                        SwitchTab(i);
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            // =========================================================================
            // 3. 动态响应式标签主体渲染 (精确计算的视口净高度)
            // =========================================================================
            float availH = CurrentContentHeight;
            GUILayout.BeginVertical(GUILayout.Height(availH), GUILayout.MaxHeight(availH));
            if (_tabs != null && _currentTab >= 0 && _currentTab < _tabs.Length)
            {
                _tabs[_currentTab].Draw(availH);
            }
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // =========================================================================
            // 4. 底栏状态与快捷指令 (Footer Status Bar - 恒定底边锚定)
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

            GUILayout.Space(18f); // 预留给右下角拉伸手柄
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndVertical();

            // =========================================================================
            // 5. 右下角折角拉伸放大手柄
            // =========================================================================
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

            // 自由拖拽窗口移动 (仅顶栏单次调用，杜绝双 DragWindow 引起的事件冲突与画面闪烁)
            if (GUIUtility.hotControl != resizeControlId && !_isResizing)
            {
                GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(80f, _windowRect.width - 260f), 42f));
            }
        }

        private class SettingsGUIDrawer : MonoBehaviour
        {
            public SettingsGUI Owner;

            private void Awake()
            {
                enabled = false;
            }

            private void OnGUI()
            {
                if (Owner != null && Owner.IsOpen)
                {
                    SafeGUIGateway.ExecuteRoot(() => Owner.RenderGUI(), "SettingsGUI");
                }
            }
        }
    }
}
