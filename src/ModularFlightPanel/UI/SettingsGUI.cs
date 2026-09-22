using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    public class SettingsGUI : MonoBehaviour
    {
        private static SettingsGUI _instance;
        public static SettingsGUI Instance => _instance;

        private bool _isOpen = false;
        private Rect _windowRect = new Rect(100f, 100f, 430f, 540f);
        private int _windowId = 849204;
        private Vector2 _scrollPos;

        // 添加新组件临时输入缓存
        private string _newWidgetTitle = "巡航监控卡";
        private string _newWidgetTemplate = "动压: {Q:F2} | 马赫: {MACH} | TWR: {TWR:F2}";

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            // Alt + N 快捷键
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
                WidgetLayoutManager.Instance.SaveLayout();
            }
        }

        private void OnGUI()
        {
            if (!_isOpen) return;

            GUI.skin = HighLogic.Skin;
            _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindowContent, "Modular Flight Panel - 模块化飞行面板", GUILayout.Width(430f));
        }

        private void DrawWindowContent(int id)
        {
            GUILayout.BeginVertical();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(460f));

            // 1. 拖拽编辑模式核心开关
            GUILayout.Label("<b>1. 布局自由拖拽 (Interactive Layout)</b>");
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            GUI.color = isEdit ? Color.green : Color.white;
            string editBtnText = isEdit ? "▶ [正在编辑] 鼠标直接在屏幕上拖拽移动任意组件" : "▶ [开启自由拖拽模式] (点击进入编辑)";
            if (GUILayout.Button(editBtnText, GUILayout.Height(32f)))
            {
                WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                if (!WidgetDragHandler.IsEditModeActive)
                {
                    WidgetLayoutManager.Instance.SaveLayout();
                }
            }
            GUI.color = Color.white;

            GUILayout.Space(10f);

            // 2. 姿态球渲染模式切换 (贴图 / 程序化)
            GUILayout.Label("<b>2. 姿态球生成模式 (Navball Render Mode)</b>");
            GUILayout.BeginHorizontal();
            bool isTex = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Texture;
            GUI.color = isTex ? Color.cyan : Color.gray;
            if (GUILayout.Button(isTex ? "● 贴图模式 (原版素材高动态增强)" : "○ 贴图模式 (原版素材高动态增强)", GUILayout.Height(28f)))
            {
                if (!isTex)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Texture;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            bool isProc = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            GUI.color = isProc ? Color.cyan : Color.gray;
            if (GUILayout.Button(isProc ? "● 程序化模式 (现代超清矢量)" : "○ 程序化模式 (现代超清矢量)", GUILayout.Height(28f)))
            {
                if (!isProc)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 3. 主题切换
            GUILayout.Label("<b>3. 视觉主题预设 (Themes & Styles)</b>");
            var themes = ThemeManager.Instance.AvailableThemes;
            for (int i = 0; i < themes.Count; i++)
            {
                var t = themes[i];
                bool isCurrent = ThemeManager.Instance.CurrentTheme?.ThemeId == t.ThemeId;

                string label = isCurrent ? $"✔ <b>{t.DisplayName}</b> (生效中)" : $"   {t.DisplayName}";
                if (GUILayout.Button(label, GUILayout.Height(24f)))
                {
                    ThemeManager.Instance.SetTheme(t.ThemeId);
                }
            }

            GUILayout.Space(10f);

            // 4. 现代化大屏缩放
            GUILayout.Label("<b>4. 大屏缩放比例 (Display Scale)</b>");
            if (NavballHUD.Instance != null)
            {
                float currentScale = NavballHUD.Instance.CustomScale;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"尺寸比例: {currentScale:F2}x", GUILayout.Width(110f));
                float newScale = GUILayout.HorizontalSlider(currentScale, 0.7f, 1.8f);
                GUILayout.EndHorizontal();

                if (Mathf.Abs(newScale - currentScale) > 0.01f)
                {
                    NavballHUD.Instance.CustomScale = newScale;
                    NavballHUD.Instance.RebuildHUD();
                }
            }

            GUILayout.Space(10f);

            // 4. 自定义通配符组件添加器
            GUILayout.Label("<b>4. 添加自定义通配符小组件 (Add Custom Widget)</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("组件标题:", GUILayout.Width(70f));
            _newWidgetTitle = GUILayout.TextField(_newWidgetTitle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("参数模板:", GUILayout.Width(70f));
            _newWidgetTemplate = GUILayout.TextField(_newWidgetTemplate);
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=#AAAAAA><size=10>支持通配符: {SPD}, {ALT:AGL}, {VSI}, {Q}, {MACH}, {TWR}, {AP}, {PE}, {TAP}, {GFORCE}, {PROP}</size></color>");

            if (GUILayout.Button("+ 创建并在屏幕上生成新小组件", GUILayout.Height(28f)))
            {
                if (!string.IsNullOrEmpty(_newWidgetTitle))
                {
                    NavballHUD.Instance.AddNewCustomWidget(_newWidgetTitle, _newWidgetTemplate);
                }
            }

            GUILayout.Space(10f);

            // 5. 当前小组件管理清单
            GUILayout.Label("<b>5. 已挂载组件列表 (Widget Manager)</b>");
            var widgets = WidgetLayoutManager.Instance.CurrentLayout.Widgets;
            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];
                GUILayout.BeginHorizontal();
                bool prevEnabled = w.IsEnabled;
                w.IsEnabled = GUILayout.Toggle(w.IsEnabled, $" {w.DisplayName}", GUILayout.Width(190f));
                if (prevEnabled != w.IsEnabled)
                {
                    NavballHUD.Instance.RebuildHUD();
                }

                if (w.WidgetId.StartsWith("custom."))
                {
                    if (GUILayout.Button("删除", GUILayout.Width(45f), GUILayout.Height(20f)))
                    {
                        WidgetLayoutManager.Instance.RemoveWidget(w.WidgetId);
                        NavballHUD.Instance.RebuildHUD();
                        break;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);

            // 6. 系统原生兼容
            GUILayout.Label("<b>6. 系统兼容 (Compatibility)</b>");
            bool hideStock = GUILayout.Toggle(HarmonyPatches.IsStockNavballHidden, " 隐藏 KSP1 原版 Navball 视觉模型");
            if (hideStock != HarmonyPatches.IsStockNavballHidden)
            {
                HarmonyPatches.IsStockNavballHidden = hideStock;
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存并关闭 (Alt+N)", GUILayout.Height(30f)))
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
