using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 已挂载组件清单管理器 (Widget Manager)
    /// </summary>
    public static class TabWidgetManager
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void Draw(Action<string> onJumpToAssembler)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout.Widgets;

            GUILayout.BeginVertical();

            // 顶部横幅提示
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toastMsg))
            {
                _toastTimer -= Time.deltaTime;
                GUI.color = Color.green;
                GUILayout.Label($"<b>✔ {_toastMsg}</b>");
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label($"<b>已挂载组件列表 (共 {widgets.Count} 个组件)</b>");
                GUILayout.Label("<color=#AAAAAA><size=11>可在此处快速勾选显隐、修改名称、微调屏幕像素坐标、复制副本或删除扩展组件。</size></color>");
            }

            GUILayout.Space(6f);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];

                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();

                // 1. 显隐开关
                bool prevEnabled = w.IsEnabled;
                w.IsEnabled = GUILayout.Toggle(w.IsEnabled, "", GUILayout.Width(22f));
                if (prevEnabled != w.IsEnabled)
                {
                    NavballHUD.Instance.RebuildHUD();
                }

                // 2. 类型标签徽章
                string typeBadge = w.WidgetType == "tape" ? "<color=#00E5FF>[标尺带]</color>" :
                                   (w.WidgetType == "ecam_dial" ? "<color=#00FF88>[ECAM表盘]</color>" :
                                   (w.WidgetId.StartsWith("spacex.") || w.WidgetType.StartsWith("spacex_") ? "<color=#00F0FF>[SpaceX]</color>" :
                                   (w.WidgetId.StartsWith("custom.") ? "<color=#FFAA00>[卡片]</color>" : "<color=#FF88FF>[核心]</color>")));
                GUILayout.Label(typeBadge, GUILayout.Width(75f));

                // 3. 名称编辑
                w.DisplayName = GUILayout.TextField(w.DisplayName, GUILayout.Width(160f));

                // 4. 坐标读数与微调
                GUILayout.Label($"X:{w.PositionX:F0} Y:{w.PositionY:F0}", GUILayout.Width(100f));
                if (GUILayout.Button("◀", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    w.PositionX -= 10f;
                    ApplyWidgetTransform(w);
                }
                if (GUILayout.Button("▶", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    w.PositionX += 10f;
                    ApplyWidgetTransform(w);
                }
                if (GUILayout.Button("▲", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    w.PositionY += 10f;
                    ApplyWidgetTransform(w);
                }
                if (GUILayout.Button("▼", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    w.PositionY -= 10f;
                    ApplyWidgetTransform(w);
                }

                GUILayout.Space(10f);

                // 5. 跳转装配
                GUI.color = Color.cyan;
                if (GUILayout.Button("🔍 装配调校", GUILayout.Width(80f), GUILayout.Height(20f)))
                {
                    onJumpToAssembler?.Invoke(w.WidgetId);
                }
                GUI.color = Color.white;

                // 6. 复制副本
                if (w.WidgetType == "ecam_dial" || w.WidgetType == "tape" || w.WidgetId.StartsWith("custom.") || w.WidgetId.StartsWith("spacex."))
                {
                    if (GUILayout.Button("➕ 复制", GUILayout.Width(50f), GUILayout.Height(20f)))
                    {
                        WidgetLayoutManager.Instance.DuplicateWidget(w.WidgetId);
                        NavballHUD.Instance.RebuildHUD();
                        ShowToast($"已复制「{w.DisplayName}」副本！");
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }

                    // 7. 删除
                    GUI.color = new Color(1f, 0.4f, 0.4f, 1f);
                    if (GUILayout.Button("🗑️", GUILayout.Width(28f), GUILayout.Height(20f)))
                    {
                        WidgetLayoutManager.Instance.RemoveWidget(w.WidgetId);
                        NavballHUD.Instance.RebuildHUD();
                        ShowToast($"已移除「{w.DisplayName}」！");
                        GUI.color = Color.white;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUI.color = Color.white;
                }

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();

            GUILayout.Space(8f);

            // 底部全局动作
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复出厂默认布局", GUILayout.Height(25f)))
            {
                WidgetLayoutManager.Instance.ResetToDefaultLayout();
                NavballHUD.Instance.RebuildHUD();
                ShowToast("已恢复出厂默认布局！");
            }
            if (GUILayout.Button("清空所有扩展组件", GUILayout.Height(25f)))
            {
                WidgetLayoutManager.Instance.ClearAllCustomWidgets();
                NavballHUD.Instance.RebuildHUD();
                ShowToast("已清空全部自定义扩展组件！");
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void ApplyWidgetTransform(WidgetConfig w)
        {
            WidgetLayoutManager.Instance.SaveLayout();
            if (NavballHUD.Instance != null && NavballHUD.Instance.ModularWidgets != null)
            {
                var target = NavballHUD.Instance.ModularWidgets.Find(x => x.WidgetId == w.WidgetId);
                if (target != null)
                {
                    target.UpdateTransform(w.PositionX, w.PositionY, w.Scale, w.Rotation);
                }
            }
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
