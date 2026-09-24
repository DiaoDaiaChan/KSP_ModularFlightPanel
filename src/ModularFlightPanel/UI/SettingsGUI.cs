using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 全新模块化航电暗晶工作台 (Modular Avionics Cyber-Dark Workbench - Alt+N)
    /// 全面升级现代暗晶航电 UI 视觉体系，集成高保真状态显示栏、防抖动自适应调度与流畅拖拽交互。
    /// </summary>
    public class SettingsGUI : MonoBehaviour
    {
        private static SettingsGUI _instance;
        public static SettingsGUI Instance => _instance;

        private bool _isOpen = false;
        private Rect _windowRect = new Rect(100f, 60f, 980f, 670f);
        private int _windowId = 849204;

        private int _currentTab = 1; // 默认打开遥测装配台，方便直接调参
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
        }

        public void ToggleWindow()
        {
            _isOpen = !_isOpen;
            if (!_isOpen)
            {
                // 关闭窗口时退出拖拽编辑模式并提交暂存
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                TabAssembler.CommitPendingSaves();
                WidgetLayoutManager.Instance.SaveLayout();
            }
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

            _windowRect = GUILayout.Window(
                _windowId,
                _windowRect,
                DrawWindowContent,
                "",
                MFPGuiSkin.WindowStyle,
                GUILayout.Width(980f),
                GUILayout.Height(670f)
            );
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
            GUILayout.Label("<color=#00E5FF><b>MODULAR FLIGHT PANEL</b></color> <color=#66CCFF><size=11>| 航电工程工作台</size></color>", GUILayout.Width(260f));

            GUILayout.FlexibleSpace();

            // 载具状态摘要
            string vesselName = FlightTelemetryContext.Current?.VesselName ?? "---";
            string frameName = TelemetryTokenEngine.Evaluate("{FRAME}", FlightTelemetryContext.Current);
            double mfpMs = MFPProfiler.AvgTotalMs;
            float fps = MFPProfiler.CurrentFPS;

            string statusText = $"<color=#8898AA>载具: <color=#FFFFFF>{vesselName}</color> | 参考系: <color=#00E5FF>{frameName}</color> | MFP: <color=#00FF88>{mfpMs:F2}ms</color> | <color=#FFB800>{fps:F0} FPS</color></color>";
            GUILayout.Label(statusText);

            GUILayout.FlexibleSpace();

            // 自由拖拽编辑模式开关
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            GUIStyle dragBtnStyle = isEdit ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.StepperButtonStyle;
            string dragBtn = isEdit ? "🎯 [拖拽模式中] 点击锁定" : "🎯 [开启自由拖拽]";
            if (GUILayout.Button(dragBtn, dragBtnStyle, GUILayout.Height(24f), GUILayout.Width(140f)))
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
                NavballHUD.Instance?.RebuildHUD();
            }

            GUILayout.Space(6f);

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
                    if (_currentTab == 1) TabAssembler.CommitPendingSaves();
                    _currentTab = i;
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
                        TabAssembler.CommitPendingSaves();
                        _currentTab = 1;
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

            GUILayout.Label("<color=#6688AA><size=11>快捷键提示: <b>Alt+N</b> 唤出面板 | <b>F2</b> 隐藏全UI | <b>F10</b> 性能HUD | <b>F11</b> 纯净旁路</size></color>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("✔ 保存配置并关闭 (Alt+N)", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(200f)))
            {
                WidgetDragHandler.IsEditModeActive = false;
                TabAssembler.CommitPendingSaves();
                WidgetLayoutManager.Instance.SaveLayout();
                _isOpen = false;
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndVertical();

            // 限制拖拽响应区域为顶栏，防止吞噬窗口内部按钮点击
            GUI.DragWindow(new Rect(0f, 0f, 980f, 40f));
        }
    }
}
