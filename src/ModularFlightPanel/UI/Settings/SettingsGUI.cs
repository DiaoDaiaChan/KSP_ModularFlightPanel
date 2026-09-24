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

        private Rect _windowRect = new Rect(100f, 60f, 1040f, 740f);
        private int _windowId = 849204;
        private bool _rectInitialized = false;

        private int _currentTab = 1; // 默认打开遥测装配台
        private readonly string[] TabTitles = new string[]
        {
            "📦 航电库 (Library)",
            "🛠️ 遥测装配台 (Assembler)",
            "📋 挂载清单 (Manager)",
            "🎨 视觉风格 (Themes)",
            "🔄 预设与分享 (Share)",
            "🚀 仿真沙盒 (Sandbox)"
        };

        private void Awake()
        {
            _instance = this;
            UIWidget.OnRequestOpenWorkbench = ToggleWindow;
            MFPToastBridge.OnShowToast = (msg) => Settings.MFPGuiSkin.ShowToast(msg);
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

        public void ToggleWindow()
        {
            _isOpen = !_isOpen;
            if (!_isOpen)
            {
                // 关闭窗口时退出拖拽编辑模式、释放输入锁并提交暂存
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                TabAssembler.CommitPendingSaves();
                WidgetLayoutManager.Instance.SaveLayout();
                MFPInputLock.ReleaseAllLocks();
            }
            else
            {
                EnsureWindowRect();
            }
            OnWindowStateChanged?.Invoke(_isOpen);
        }

        private void EnsureWindowRect()
        {
            float targetW = Mathf.Clamp(Screen.width * 0.72f, 960f, 1140f);
            float targetH = Mathf.Clamp(Screen.height * 0.82f, 640f, 820f);

            if (!_rectInitialized)
            {
                float x = (Screen.width - targetW) * 0.5f;
                float y = (Screen.height - targetH) * 0.5f;
                _windowRect = new Rect(x, y, targetW, targetH);
                _rectInitialized = true;
            }
            else
            {
                _windowRect.width = targetW;
                _windowRect.height = targetH;
                _windowRect.x = Mathf.Clamp(_windowRect.x, 10f, Screen.width - targetW - 10f);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 10f, Screen.height - targetH - 10f);
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

            // 输入穿透安全防护
            bool isMouseOver = _windowRect.Contains(Event.current.mousePosition);
            MFPInputLock.SetWindowHoverLock(isMouseOver);

            bool isTextFocused = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
            MFPInputLock.SetKeyboardFocusLock(isTextFocused);

            _windowRect = GUILayout.Window(
                _windowId,
                _windowRect,
                DrawWindowContent,
                "",
                MFPGuiSkin.WindowStyle,
                GUILayout.Width(_windowRect.width),
                GUILayout.Height(_windowRect.height)
            );

            // 保持窗口在屏幕安全可视范围内
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - _windowRect.height);
        }

        private void DrawWindowContent(int id)
        {
            GUILayout.BeginVertical();

            // =========================================================================
            // 1. 顶部控制栏与载具遥测状态条 (Aero Header & Quick Status Strip)
            // =========================================================================
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            // 标题徽章
            GUILayout.Label("<color=#00E5FF><b>MODULAR FLIGHT PANEL</b></color> <color=#88AACC><size=11>| 航电工程工作台</size></color>", GUILayout.Width(270f));

            GUILayout.FlexibleSpace();

            // 载具状态摘要
            string vesselName = FlightTelemetryContext.Current?.VesselName ?? "---";
            string frameName = TelemetryTokenEngine.Evaluate("{FRAME}", FlightTelemetryContext.Current);
            double mfpMs = MFPProfiler.AvgTotalMs;
            float fps = MFPProfiler.CurrentFPS;

            string statusText = $"<color=#7088A8>载具: <color=#FFFFFF>{vesselName}</color> | 参考系: <color=#00E5FF>{frameName}</color> | MFP: <color=#00FF88>{mfpMs:F2}ms</color> | <color=#FFB800>{fps:F0} FPS</color></color>";
            GUILayout.Label(statusText);

            GUILayout.FlexibleSpace();

            // 自由拖拽编辑模式开关
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            GUIStyle dragBtnStyle = isEdit ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string dragBtn = isEdit ? "🎯 [拖拽模式中] 点击锁定" : "🎯 [开启自由拖拽]";
            if (GUILayout.Button(dragBtn, dragBtnStyle, GUILayout.Height(24f), GUILayout.Width(145f)))
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
            string ballBtn = isBallOn ? "🌐 姿态球: 开" : "🌐 姿态球: 关";
            if (GUILayout.Button(ballBtn, ballBtnStyle, GUILayout.Height(24f), GUILayout.Width(95f)))
            {
                if (navCfg != null)
                {
                    navCfg.IsEnabled = !navCfg.IsEnabled;
                }
                else
                {
                    navCfg = new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.Space(6f);

            // 顶栏关闭按钮
            if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f), GUILayout.Height(24f)))
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
            // 3. 标签主体渲染 (Tab Content Area)
            // =========================================================================
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
                    TabSharePresets.Draw();
                    break;
                case 5:
                    TabSimulation.Draw();
                    break;
            }

            // =========================================================================
            // 4. 底栏状态与快捷指令 (Footer Status Bar)
            // =========================================================================
            GUILayout.Space(6f);
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            int totalWidgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets.Count ?? 0;
            GUILayout.Label($"<color=#7088A8><size=11>当前布局: <b>{totalWidgets}</b> 个组件 | 快捷键: <b>Alt+N / ESC</b> 关闭 | <b>F2</b> 隐藏全UI | <b>F10</b> 性能HUD | <b>F11</b> 纯净旁路</size></color>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("✔ 保存配置并关闭 (Alt+N)", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(200f)))
            {
                ToggleWindow();
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndVertical();

            // 限制拖拽响应区域为顶栏，防止吞噬窗口内部按钮点击
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 42f));
        }
    }
}
