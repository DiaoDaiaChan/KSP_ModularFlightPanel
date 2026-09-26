using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新已挂载组件清单管理器与批处理中枢 (Avionics Mounted Widget Manager & Batch Hub)
    /// 核心升级：
    /// 1. 实时搜索与状态过滤 (Instant Filter & State Filter: 全部 / 运行中 / 已挂起)。
    /// 2. 批量全显、全隐与一键全量网格对齐 (Batch Operations)。
    /// 3. 二次确认安全守卫 (Confirmation Guard) 杜绝意外清空整套座舱排版。
    /// 4. 全量接入 MFPGuiSkin 现代黑晶设计系统 2.0。
    /// </summary>
    public static class TabWidgetManager
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _searchQuery = "";
        private static int _statusFilter = 0; // 0=All, 1=Enabled Only, 2=Disabled Only
        private const int StatusFilterCount = 3;

        private static bool _confirmReset = false;
        private static bool _confirmClear = false;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void Draw(Action<string> onJumpToAssembler)
        {
            MFPGuiSkin.EnsureInitialized();
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;

            GUILayout.BeginVertical();

            // 1. 顶部搜索与批量控制卡片
            MFPGuiSkin.BeginCard();
            int activeCount = 0;
            for (int i = 0; i < widgets.Count; i++) if (widgets[i].IsEnabled) activeCount++;
            MFPGuiSkin.DrawHeader(I18n.Tr("MGR_HEADER", "📋 挂载清单总管"),
                string.Format(I18n.Tr("MGR_TOTAL_COUNT", "已挂载组件: {0} 个 (激活 {1} 个)"), widgets.Count, activeCount));

            MFPGuiSkin.DrawSearchBar(ref _searchQuery, I18n.Tr("LIB_SEARCH_PLACEHOLDER", "搜索组件名称或标识..."));

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();

            // 状态过滤胶囊
            for (int i = 0; i < StatusFilterCount; i++)
            {
                bool isSel = (_statusFilter == i);
                GUIStyle btnStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                string filterLabel = i == 0 ? I18n.Tr("ASM_FILTER_ALL", "全部") :
                                     i == 1 ? I18n.Tr("MGR_FILTER_RUNNING", "运行中") : I18n.Tr("MGR_FILTER_SUSPENDED", "已挂起");
                if (GUILayout.Button(filterLabel, btnStyle, GUILayout.Height(22f), GUILayout.Width(75f)))
                {
                    _statusFilter = i;
                }
            }

            GUILayout.FlexibleSpace();

            // 批量操作按钮
            if (GUILayout.Button(I18n.Tr("MGR_BTN_SHOW_ALL", "✔ 全部显示"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f)))
            {
                for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = true;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_SHOW_ALL", "已全部启用显示！"));
            }

            if (GUILayout.Button(I18n.Tr("MGR_BTN_HIDE_ALL", "○ 全部隐藏"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f)))
            {
                for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = false;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_HIDE_ALL", "已全部挂起隐藏！"));
            }

            if (GUILayout.Button(I18n.Tr("MGR_SNAP_GRID", "🧲 全量吸附 10px 网格"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f)))
            {
                for (int i = 0; i < widgets.Count; i++)
                {
                    widgets[i].PositionX = Mathf.Round(widgets[i].PositionX / 10f) * 10f;
                    widgets[i].PositionY = Mathf.Round(widgets[i].PositionY / 10f) * 10f;
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_GRID_SNAP", "已完成全量组件网格对齐！"));
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(4f);

            // 2. 列表内容 (高度严格锁定，扣除顶底固定卡片与间距，合计 550f)
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(Mathf.Max(120f, SettingsGUI.ContentHeight - 140f)));

            // Toast 提示 (置于滚动列表内部，杜绝撑大顶栏高度)
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            bool hasQuery = !string.IsNullOrEmpty(_searchQuery);
            int renderedCount = 0;

            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];

                if (_statusFilter == 1 && !w.IsEnabled) continue;
                if (_statusFilter == 2 && w.IsEnabled) continue;

                if (hasQuery)
                {
                    bool m = (w.DisplayName != null && w.DisplayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (w.WidgetId != null && w.WidgetId.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                          || (w.WidgetType != null && w.WidgetType.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!m) continue;
                }

                renderedCount++;
                DrawWidgetRow(w, onJumpToAssembler);
            }

            if (renderedCount == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#7088A8><size=11>{I18n.Tr("MGR_NO_MATCH", "未找到符合条件的组件条目")}</size></color>");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6f);

            // 3. 底部危险区域与重置操作 (带防误触二次确认保护)
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            if (_confirmReset)
            {
                GUILayout.Label($"<color=#FF4444><b>{I18n.Tr("MGR_CONFIRM_RESET", "⚠ 确定要恢复出厂默认布局吗？当前排版将被覆盖！")}</b></color>", GUILayout.ExpandWidth(true));
                if (GUILayout.Button(I18n.Tr("MGR_BTN_CONFIRM_RESET", "✔ 确认覆盖恢复"), MFPGuiSkin.DangerButtonStyle, GUILayout.Height(24f), GUILayout.Width(130f)))
                {
                    WidgetLayoutManager.Instance.ResetToDefaultLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                    _confirmReset = false;
                    ShowToast(I18n.Tr("MGR_TOAST_RESET_DONE", "已恢复出厂默认布局！"));
                }
                if (GUILayout.Button(I18n.Tr("COMMON_CANCEL", "取消"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(60f)))
                {
                    _confirmReset = false;
                }
            }
            else if (_confirmClear)
            {
                GUILayout.Label($"<color=#FF4444><b>{I18n.Tr("MGR_CONFIRM_CLEAR", "⚠ 确定清空所有自定义/扩展组件吗？")}</b></color>", GUILayout.ExpandWidth(true));
                if (GUILayout.Button(I18n.Tr("MGR_BTN_CONFIRM_CLEAR", "✔ 确认全部清空"), MFPGuiSkin.DangerButtonStyle, GUILayout.Height(24f), GUILayout.Width(130f)))
                {
                    WidgetLayoutManager.Instance.ClearAllCustomWidgets();
                    FlightHUDManager.Instance?.RebuildHUD();
                    _confirmClear = false;
                    ShowToast(I18n.Tr("MGR_TOAST_CLEAR_DONE", "已清空全部自定义扩展组件！"));
                }
                if (GUILayout.Button(I18n.Tr("COMMON_CANCEL", "取消"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(60f)))
                {
                    _confirmClear = false;
                }
            }
            else
            {
                if (GUILayout.Button(I18n.Tr("MGR_BTN_RESET_LAYOUT", "↺ 恢复出厂默认布局"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(160f)))
                {
                    _confirmReset = true;
                }
                if (GUILayout.Button(I18n.Tr("MGR_BTN_CLEAR_EXT", "🗑️ 清空所有扩展组件"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(160f)))
                {
                    _confirmClear = true;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("MGR_RESET_GUARD_HINT", "重置操作设有二次确认安全守卫")}</size></color>");
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndVertical();
        }

        private static void DrawWidgetRow(WidgetConfig w, Action<string> onJumpToAssembler)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            // 1. 显隐开关按钮
            string ledText = w.IsEnabled ? $"<color=#00FF88>{I18n.Tr("MGR_LED_SHOW", "● 显")}</color>" : $"<color=#7088A8>{I18n.Tr("MGR_LED_HIDE", "○ 隐")}</color>";
            if (GUILayout.Button(ledText, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(24f)))
            {
                w.IsEnabled = !w.IsEnabled;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            // 2. 类别徽章
            string typeBadge = w.WidgetType == "tape" ? I18n.Tr("MGR_TYPE_TAPE", "PFD 标尺") :
                              (w.WidgetType == "ecam_dial" ? I18n.Tr("MGR_TYPE_ECAM", "ECAM 表盘") :
                              (w.WidgetId.StartsWith("spacex.") ? I18n.Tr("MGR_TYPE_SPACEX", "SPX 龙船") :
                              (w.WidgetId.StartsWith("custom.") ? I18n.Tr("MGR_TYPE_CARD", "遥测卡片") : I18n.Tr("MGR_TYPE_CORE", "原生核心"))));

            Color badgeCol = w.WidgetType == "tape" ? new Color(0.00f, 0.40f, 0.60f, 0.9f) :
                            (w.WidgetType == "ecam_dial" ? new Color(0.00f, 0.45f, 0.25f, 0.9f) :
                            (w.WidgetId.StartsWith("spacex.") ? new Color(0.10f, 0.30f, 0.60f, 0.9f) :
                            (w.WidgetId.StartsWith("custom.") ? new Color(0.50f, 0.32f, 0.05f, 0.9f) : new Color(0.35f, 0.18f, 0.45f, 0.9f))));

            MFPGuiSkin.DrawBadge(typeBadge, Color.white, badgeCol, 75f);

            // 3. 名称行内编辑
            string newName = GUILayout.TextField(w.DisplayName ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(170f), GUILayout.Height(22f));
            if (newName != w.DisplayName)
            {
                w.DisplayName = newName;
                WidgetLayoutManager.Instance.SaveLayout();
            }

            // 4. 坐标微调
            GUILayout.Label($"X:<b>{w.PositionX:F0}</b> Y:<b>{w.PositionY:F0}</b>", GUILayout.Width(105f));
            if (GUILayout.Button("◀", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(22f)))
            {
                w.PositionX -= 10f;
                ApplyWidgetTransform(w);
            }
            if (GUILayout.Button("▶", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(22f)))
            {
                w.PositionX += 10f;
                ApplyWidgetTransform(w);
            }
            if (GUILayout.Button("▲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(22f)))
            {
                w.PositionY += 10f;
                ApplyWidgetTransform(w);
            }
            if (GUILayout.Button("▼", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(22f)))
            {
                w.PositionY -= 10f;
                ApplyWidgetTransform(w);
            }

            // 4b. 图层与锁定
            int layerNum = w.DrawOrder + 1;
            GUILayout.Label($"<color=#38BDF8><b>#{layerNum}</b></color>", GUILayout.Width(28f));
            if (GUILayout.Button("▲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(22f)))
            {
                WidgetLayerManager.BringForward(w);
            }
            if (GUILayout.Button("▼", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(22f)))
            {
                WidgetLayerManager.SendBackward(w);
            }
            string lockIcon = w.IsLocked ? "<color=#FFB703>🔒</color>" : "<color=#7088A8>🔓</color>";
            if (GUILayout.Button(lockIcon, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(25f), GUILayout.Height(22f)))
            {
                WidgetLayerManager.ToggleLock(w);
            }

            GUILayout.FlexibleSpace();

            // 5. 跳转装配台按钮
            if (GUILayout.Button(I18n.Tr("MGR_BTN_EDIT", "🔍 装配调校"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(90f), GUILayout.Height(22f)))
            {
                onJumpToAssembler?.Invoke(w.WidgetId);
            }

            // 6. 复制副本
            if (w.WidgetType == "ecam_dial" || w.WidgetType == "tape" || w.WidgetId.StartsWith("custom.") || w.WidgetId.StartsWith("spacex."))
            {
                if (GUILayout.Button(I18n.Tr("MGR_BTN_COPY", "➕ 复制"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(55f), GUILayout.Height(22f)))
                {
                    WidgetLayoutManager.Instance.DuplicateWidget(w.WidgetId);
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.TrFormat("MGR_TOAST_COPIED", w.DisplayName));
                }

                // 7. 删除
                if (GUILayout.Button("🗑️", MFPGuiSkin.DangerButtonStyle, GUILayout.Width(28f), GUILayout.Height(22f)))
                {
                    WidgetLayoutManager.Instance.RemoveWidget(w.WidgetId);
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.TrFormat("MGR_TOAST_REMOVED", w.DisplayName));
                }
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();
        }

        private static void ApplyWidgetTransform(WidgetConfig w)
        {
            WidgetLayoutManager.Instance.SaveLayout();
            if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null)
            {
                var target = FlightHUDManager.Instance.ModularWidgets.Find(x => x.WidgetId == w.WidgetId);
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
