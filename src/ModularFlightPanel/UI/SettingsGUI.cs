using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 全新模块化航电配置工作台 (Modular Avionics Configuration Workbench)
    /// </summary>
    public class SettingsGUI : MonoBehaviour
    {
        private static SettingsGUI _instance;
        public static SettingsGUI Instance => _instance;

        private bool _isOpen = false;
        private Rect _windowRect = new Rect(120f, 90f, 860f, 620f);
        private int _windowId = 849204;

        private int _currentTab = 0;
        private readonly string[] TabTitles = new string[]
        {
            "📦 航电组件库 (Library)",
            "🛠️ 遥测装配台 (Assembler)",
            "📋 挂载管理 (Manager)",
            "🎨 视觉风格 (Themes)",
            "🔄 预设与分享码 (Presets & Share)",
            "🚀 遥测仿真沙盒 (Simulation)"
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
                // 关闭窗口时自动退出拖拽编辑模式并保存
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
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

            GUI.skin = HighLogic.Skin;
            _windowRect = GUILayout.Window(
                _windowId,
                _windowRect,
                DrawWindowContent,
                "Modular Flight Panel | 模块化飞行面板航电工作台 (Alt+N)",
                GUILayout.Width(860f),
                GUILayout.Height(620f)
            );
        }

        private void DrawWindowContent(int id)
        {
            GUILayout.BeginVertical();

            // 1. 顶部控制栏 (Header)
            GUILayout.BeginHorizontal("box");

            // 自由拖拽开关
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            GUI.color = isEdit ? Color.green : Color.white;
            string dragBtn = isEdit ? "▶ [正在自由拖拽] 屏幕上拖拽组件" : "▶ [开启自由拖拽模式]";
            if (GUILayout.Button(dragBtn, GUILayout.Height(28f), GUILayout.Width(240f)))
            {
                WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                if (!WidgetDragHandler.IsEditModeActive)
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.SaveLayout();
                }
            }
            GUI.color = Color.white;

            GUILayout.Space(8f);

            // 重点需求：开启/关闭自定义姿态球快捷按钮
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isBallOn = navCfg == null || navCfg.IsEnabled;
            GUI.color = isBallOn ? new Color(0.2f, 1f, 0.8f, 1f) : new Color(1f, 0.6f, 0.2f, 1f);
            string ballBtn = isBallOn ? "🌐 姿态球: [显示中 (点击隐藏)]" : "🌐 姿态球: [已隐藏 (点击显示)]";
            if (GUILayout.Button(ballBtn, GUILayout.Height(28f), GUILayout.Width(200f)))
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
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();

            // 载具与当前参考系信息
            string vesselName = FlightTelemetryContext.Current?.VesselName ?? "---";
            string frameName = TelemetryTokenEngine.Evaluate("{FRAME}", FlightTelemetryContext.Current);
            GUILayout.Label($"<color=#AAAAAA><size=11>载具: {vesselName} | 参考系: <color=#00E5FF>{frameName}</color></size></color>", GUILayout.Height(28f));

            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 2. 标签导航栏 (Tab Bar)
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabTitles.Length; i++)
            {
                bool isSel = _currentTab == i;
                GUI.color = isSel ? Color.cyan : Color.white;
                if (GUILayout.Button($"<b>{TabTitles[i]}</b>", GUILayout.Height(30f)))
                {
                    _currentTab = i;
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 3. 标签主体渲染 (Tab Content)
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

            // 4. 底栏 (Footer)
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存配置并关闭窗口 (Alt+N)", GUILayout.Height(28f)))
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetLayoutManager.Instance.SaveLayout();
                _isOpen = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
