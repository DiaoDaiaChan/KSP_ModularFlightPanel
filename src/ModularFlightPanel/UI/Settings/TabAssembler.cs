using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新现代化航电一体化装配工作台 (Modern Avionics Studio Workbench)
    /// 核心革新：
    /// 1. 左栏组件大纲与快捷新建：实时过滤、行内显隐、快捷创建预设与全量批处理工具条。
    /// 2. 可视化多通道插槽生成器 (Visual Multi-Channel Form)：针对多通道遥测矩阵卡与仪表，提供结构化通道配置与一键填槽。
    /// 3. 全局遥测参数速查抽屉联动 (Telemetry Parameter Drawer Integration)：彻底消灭 123 页翻页噩梦。
    /// 4. 4Hz 定频遥测采样与脏标记防抖提交机制。
    /// </summary>
    public static class TabAssembler
    {
        private static Vector2 _leftScroll = Vector2.zero;
        private static Vector2 _rightScroll = Vector2.zero;
        private static string _widgetSearchQuery = "";
        private static int _widgetCategoryFilter = 0; // 0=All, 1=Gauges, 2=Systems, 3=SpaceX, 4=Controls

        private static string GetCatName(int index)
        {
            switch (index)
            {
                case 0: return I18n.Tr("ASM_FILTER_ALL", "全部");
                case 1: return I18n.Tr("ASM_FILTER_GAUGES", "仪表");
                case 2: return I18n.Tr("ASM_FILTER_SYSTEMS", "系统");
                case 3: return I18n.Tr("ASM_FILTER_SPACEX", "SPX");
                case 4: return I18n.Tr("ASM_FILTER_CONTROLS", "控制");
                default: return "All";
            }
        }

        private static string _selectedWidgetId = null;

        // 快速添加折叠状态
        private static bool _showQuickAdd = false;
        private static int _quickSpawnCounter = 0;

        // 多通道模式切换：false=可视化插槽, true=高级源码
        private static bool _showRawTemplateSource = false;

        // 遥测预览缓存 (4Hz)
        private static string _cachedTemplate = null;
        private static string _cachedEvaluation = "---";
        private static float _lastEvalTime = 0f;
        private static readonly Dictionary<string, string> _channelEvalCache = new Dictionary<string, string>();

        // 脏数据与防抖提交
        private static bool _isDirty = false;
        private static float _dirtyTimer = 0f;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void SetSelectedWidget(string widgetId)
        {
            _selectedWidgetId = widgetId;
            if (TelemetryParamDrawer.IsOpen) TelemetryParamDrawer.Close();
        }

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null || widgets.Count == 0)
            {
                GUILayout.BeginVertical();
                MFPGuiSkin.BeginCard();
                GUILayout.Label($"<color=#FFAA00><b>{I18n.Tr("ASM_NO_WIDGETS", "当前没有任何组件。点击下方按钮即可快速生成开箱即用航电组件：")}</b></color>");
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("ASM_BTN_SPAWN_DIAL", "+ 生成 ECAM 仪表"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(28f)))
                {
                    SpawnQuickWidget("ecam_dial", "ecam_dial", I18n.Tr("ASM_SPAWN_ECAM", "ECAM 仪表"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_BTN_SPAWN_TAPE", "+ 生成 PFD 标尺带"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(28f)))
                {
                    SpawnQuickWidget("tape", "tape", I18n.Tr("ASM_SPAWN_TAPE", "PFD 标尺带"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_BTN_SPAWN_CARD", "+ 生成 6通道遥测矩阵卡"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(28f)))
                {
                    SpawnQuickWidget("custom_token", "custom.telemetry_card", I18n.Tr("ASM_SPAWN_CARD", "遥测矩阵卡"));
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndCard();
                GUILayout.EndVertical();
                return;
            }

            // 保持当前选中有效
            WidgetConfig curWidget = null;
            if (!string.IsNullOrEmpty(_selectedWidgetId))
            {
                curWidget = widgets.Find(x => x.WidgetId == _selectedWidgetId);
            }
            if (curWidget == null)
            {
                curWidget = widgets[0];
                _selectedWidgetId = curWidget.WidgetId;
            }

            // 监听鼠标抬起与防抖保存
            if (Event.current.type == EventType.MouseUp && _isDirty)
            {
                CommitPendingSaves();
            }

            if (_isDirty)
            {
                _dirtyTimer += Time.unscaledDeltaTime;
                if (_dirtyTimer > 1.5f) CommitPendingSaves();
            }

            // Toast 提示
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            GUILayout.BeginHorizontal(GUILayout.Height(SettingsGUI.ContentHeight));

            // =========================================================================
            // 左栏：主从组件选择与大纲导航 (Master Widget Navigator, 280px)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(280f), GUILayout.Height(SettingsGUI.ContentHeight));
            DrawWidgetMasterList(widgets, curWidget);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // =========================================================================
            // 右栏：组件检查器 或 遥测参数速查抽屉 (Inspector or Telemetry Drawer)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(SettingsGUI.ContentHeight));

            if (TelemetryParamDrawer.IsOpen)
            {
                TelemetryParamDrawer.Draw();
            }
            else
            {
                _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.Height(SettingsGUI.ContentHeight));
                DrawWidgetDetailInspector(curWidget);
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        #region Master List (Left Column)

        private static void DrawWidgetMasterList(List<WidgetConfig> widgets, WidgetConfig curWidget)
        {
            MFPGuiSkin.BeginCard();

            // 1. 标题与数量
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_HEADER_NAV", "组件大纲 (WIDGETS)"), I18n.TrFormat("ASM_TOTAL_COUNT", widgets.Count));

            // 2. 快捷新建组件下拉栏
            string quickAddLabel = _showQuickAdd ? I18n.Tr("ASM_BTN_QUICK_ADD_CLOSE", "▲ 收起新建菜单") : I18n.Tr("ASM_BTN_QUICK_ADD_OPEN", "➕ 快速新建组件...");
            GUIStyle quickAddStyle = _showQuickAdd ? MFPGuiSkin.SecondaryButtonStyle : MFPGuiSkin.PrimaryButtonStyle;
            if (GUILayout.Button(quickAddLabel, quickAddStyle, GUILayout.Height(24f)))
            {
                _showQuickAdd = !_showQuickAdd;
            }

            if (_showQuickAdd)
            {
                MFPGuiSkin.BeginInset();
                GUILayout.Label($"<b><size=10><color=#{MFPGuiSkin.HexAccentCyan}>{I18n.Tr("ASM_QUICK_ADD_TITLE", "选择常用航电模板即时生成:")}</color></size></b>");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("ASM_SPAWN_ECAM", "📊 ECAM 仪表"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    SpawnQuickWidget("ecam_dial", "ecam_dial", I18n.Tr("ASM_SPAWN_ECAM", "ECAM 仪表"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_SPAWN_TAPE", "📏 PFD 标尺带"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    SpawnQuickWidget("tape", "tape", I18n.Tr("ASM_SPAWN_TAPE", "PFD 标尺带"));
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("ASM_SPAWN_CARD", "📝 遥测矩阵卡"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    SpawnQuickWidget("custom_token", "custom.telemetry_card", I18n.Tr("ASM_SPAWN_CARD", "遥测矩阵卡"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_SPAWN_ARC", "🎛️ 弧形表"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    SpawnQuickWidget("arc_meter", "arc_meter", I18n.Tr("ASM_TAG_ARC", "弧形指示器"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_SPAWN_BAR", "📶 状态条"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    SpawnQuickWidget("bar_gauge", "bar_gauge", I18n.Tr("ASM_TAG_BAR", "横向条形表"));
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
            }

            GUILayout.Space(4f);

            // 3. 搜索框
            MFPGuiSkin.DrawSearchBar(ref _widgetSearchQuery, I18n.Tr("ASM_SEARCH_WIDGET", "筛选组件名称/ID..."));

            GUILayout.Space(4f);

            // 4. 分类过滤胶囊
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 5; i++)
            {
                bool isCat = (_widgetCategoryFilter == i);
                GUIStyle catStyle = isCat ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(GetCatName(i), catStyle, GUILayout.Height(20f)))
                {
                    _widgetCategoryFilter = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 5. 滚动组件大纲卡片列表 (高度自适应计算，扣除顶底固定区域)
            float scrollHeight = Mathf.Max(120f, SettingsGUI.ContentHeight - (_showQuickAdd ? 220f : 160f));
            _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.Height(scrollHeight));

            bool hasQuery = !string.IsNullOrEmpty(_widgetSearchQuery);
            int matchCount = 0;

            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];

                if (!MatchesCategory(w, _widgetCategoryFilter)) continue;

                if (hasQuery)
                {
                    bool match = (w.DisplayName != null && w.DisplayName.IndexOf(_widgetSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                              || (w.WidgetId != null && w.WidgetId.IndexOf(_widgetSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                              || (w.WidgetType != null && w.WidgetType.IndexOf(_widgetSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                matchCount++;
                bool isSelected = (w.WidgetId == curWidget.WidgetId);
                GUIStyle rowStyle = isSelected ? MFPGuiSkin.RowSelectedStyle : MFPGuiSkin.RowNormalStyle;

                GUILayout.BeginHorizontal(rowStyle);

                // 显隐开关小球
                string ledChar = w.IsEnabled ? "<color=#00FF88>●</color>" : "<color=#667788>○</color>";
                if (GUILayout.Button(ledChar, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(22f)))
                {
                    w.IsEnabled = !w.IsEnabled;
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                }

                // 类型角标
                string badge = w.WidgetType == "tape" ? "PFD" :
                              (w.WidgetType == "ecam_dial" ? "ECAM" :
                              (w.WidgetType == "arc_meter" ? "ARC" :
                              (w.WidgetType == "bar_gauge" ? "BAR" :
                              (w.WidgetId.StartsWith("spacex.") ? "SPX" :
                              (w.WidgetId.StartsWith("custom.") ? I18n.Tr("WIDGET_UIMGR_CARD", "卡片") : I18n.Tr("WIDGET_UIMGR_CORE", "核心"))))));

                string rowText = $"<b>{w.DisplayName}</b>\n<size=9><color=#88AACC>{badge}</color> | <color=#AAAAAA>({w.PositionX:F0}, {w.PositionY:F0})</color></size>";
                if (GUILayout.Button(rowText, "label", GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
                {
                    CommitPendingSaves();
                    _selectedWidgetId = w.WidgetId;
                    if (TelemetryParamDrawer.IsOpen) TelemetryParamDrawer.Close();
                }

                GUILayout.EndHorizontal();
            }

            if (matchCount == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#8899AA><size=11>{I18n.Tr("ASM_NO_MATCH", "未搜索到匹配项")}</size></color>");
            }

            GUILayout.EndScrollView();

            // 6. 底部批量快捷操作工具条 (Batch Toolbar)
            GUILayout.Space(4f);
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("MGR_BTN_SHOW_ALL", "✔ 全显"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
            {
                for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = true;
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_SHOW_ALL", "已全部启用显示！"));
            }
            if (GUILayout.Button(I18n.Tr("MGR_BTN_HIDE_ALL", "○ 全隐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
            {
                for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = false;
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_HIDE_ALL", "已全部挂起隐藏！"));
            }
            if (GUILayout.Button(I18n.Tr("MGR_SNAP_GRID_SHORT", "🧲 对齐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
            {
                for (int i = 0; i < widgets.Count; i++)
                {
                    widgets[i].PositionX = Mathf.Round(widgets[i].PositionX / 10f) * 10f;
                    widgets[i].PositionY = Mathf.Round(widgets[i].PositionY / 10f) * 10f;
                }
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(I18n.Tr("MGR_TOAST_GRID_SNAP", "已完成全量组件网格对齐！"));
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private static bool MatchesCategory(WidgetConfig w, int category)
        {
            if (category == 0) return true;
            string id = w.WidgetId.ToLowerInvariant();
            string type = (w.WidgetType ?? "").ToLowerInvariant();

            if (category == 1) return type == "ecam_dial" || type == "tape" || type == "bar_gauge" || type == "arc_meter" || id.Contains("gauge") || id.Contains("meter");
            if (category == 2) return id.Contains("eicas") || id.Contains("elec") || id.Contains("life") || id.Contains("perf") || id.Contains("signal") || id.Contains("rocket");
            if (category == 3) return id.StartsWith("spacex.") || type.StartsWith("spacex_");
            if (category == 4) return id.Contains("toolbar") || id.Contains("control") || id.Contains("sas") || id.Contains("timewarp") || id.Contains("staging") || id.Contains("ui_widget");
            return true;
        }

        private static void SpawnQuickWidget(string typeName, string defaultId, string defaultName)
        {
            var layout = WidgetLayoutManager.Instance?.CurrentLayout;
            if (layout == null || layout.Widgets == null) return;

            _quickSpawnCounter++;
            int count = 1;
            string newId = defaultId;
            while (layout.Widgets.Exists(w => string.Equals(w.WidgetId, newId, StringComparison.OrdinalIgnoreCase)))
            {
                newId = $"{defaultId}_{count++}";
            }

            float offX = ((_quickSpawnCounter % 5) - 2) * 45f;
            float offY = ((_quickSpawnCounter % 3) - 1) * 35f;

            var newCfg = new WidgetConfig(newId, $"{defaultName} #{count}", offX, offY)
            {
                WidgetType = typeName,
                IsEnabled = true
            };

            // 预设默认通道与数值源
            if (typeName == "tape")
            {
                newCfg.StepInterval = 50f;
                newCfg.MinValue = 0f;
                newCfg.MaxValue = 1000f;
                newCfg.NumericToken = "{SPD:SURF}";
                newCfg.UnitLabel = "m/s";
            }
            else if (typeName == "ecam_dial")
            {
                newCfg.MinValue = 0f;
                newCfg.MaxValue = 100f;
                newCfg.NumericToken = "{THROTTLE}";
                newCfg.UnitLabel = "%";
            }
            else if (typeName == "arc_meter")
            {
                newCfg.MinValue = 0f;
                newCfg.MaxValue = 100f;
                newCfg.NumericToken = "{THROTTLE}";
                newCfg.UnitLabel = "%";
            }
            else if (typeName == "bar_gauge")
            {
                newCfg.MinValue = 0f;
                newCfg.MaxValue = 100f;
                newCfg.NumericToken = "{PROP}";
                newCfg.UnitLabel = "%";
            }
            else if (typeName == "custom_token" || typeName == "custom")
            {
                newCfg.CustomTemplate = TelemetryMatrixData.CreateDefaultKeyValue().ToTemplate();
            }

            layout.Widgets.Add(newCfg);
            _selectedWidgetId = newCfg.WidgetId;
            MarkDirty();
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            ShowToast(string.Format(I18n.Tr("ASM_TOAST_SPAWNED", "✔ 已快速生成: {0}"), newCfg.DisplayName));
        }

        #endregion

        #region Detail Inspector (Right Column)

        private static void DrawWidgetDetailInspector(WidgetConfig w)
        {
            // 1. 顶部基础属性卡
            DrawBasicInfoCard(w);

            GUILayout.Space(4f);

            // 2. 空间几何与快速对齐
            DrawTransformCard(w);

            GUILayout.Space(4f);

            // 3. 遥测插槽驱动与量程标定
            if (w.WidgetType == "ecam_dial" || w.WidgetType == "tape" || w.WidgetType == "bar_gauge" ||
                w.WidgetType == "arc_meter" || w.WidgetId == "core.vsi" || w.WidgetId == "core.throttle" || w.WidgetId == "core.propellant")
            {
                DrawDialOrTapeCard(w);
            }
            else if (w.WidgetId.StartsWith("custom.") || w.WidgetType == "custom" || w.WidgetType == "custom_token" || w.WidgetType == "custom_token_text")
            {
                DrawMultiChannelCard(w);
            }
            else
            {
                DrawCoreInfoCard(w);
            }

            GUILayout.Space(4f);

            // 3b. 组件微控件定制 (Micro-Controls Customizer)
            DrawMicroControlsCard(w);

            GUILayout.Space(4f);

            // 4. 全局遥测速查抽屉快速入口卡片
            DrawTelemetryDrawerLauncherCard(w);

            GUILayout.Space(4f);

            // 5. 性能与离屏调优
            DrawPerformanceCard(w);
        }

        private static void DrawBasicInfoCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            string typeTag = w.WidgetType == "tape" ? I18n.Tr("ASM_TAG_TAPE", "PFD 标尺带") :
                            (w.WidgetType == "ecam_dial" ? I18n.Tr("ASM_TAG_ECAM", "ECAM 仪表") :
                            (w.WidgetType == "arc_meter" ? I18n.Tr("ASM_TAG_ARC", "弧形电平指示器") :
                            (w.WidgetType == "bar_gauge" ? I18n.Tr("ASM_TAG_BAR", "横向条形图") :
                            (w.WidgetId.StartsWith("spacex.") ? I18n.Tr("ASM_TAG_SPACEX", "SpaceX 套件") :
                            (w.WidgetId.StartsWith("custom.") ? I18n.Tr("ASM_TAG_CARD", "遥测卡片") : I18n.Tr("ASM_TAG_CORE", "原生核心组件"))))));

            MFPGuiSkin.DrawBadge(typeTag, Color.white, new Color(0.00f, 0.45f, 0.65f, 0.95f), 130f);

            GUILayout.Label(I18n.Tr("ASM_PROP_NAME", "显示名称:"), GUILayout.Width(65f));
            string newName = GUILayout.TextField(w.DisplayName ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.ExpandWidth(true));
            if (newName != w.DisplayName)
            {
                w.DisplayName = newName;
                MarkDirty();
            }

            GUILayout.Space(8f);

            bool prevEnabled = w.IsEnabled;
            w.IsEnabled = GUILayout.Toggle(w.IsEnabled, w.IsEnabled ? I18n.Tr("ASM_STATUS_RUNNING", "● 运行显示") : I18n.Tr("ASM_STATUS_SUSPENDED", "○ 挂起隐藏"), GUILayout.Width(85f));
            if (prevEnabled != w.IsEnabled)
            {
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();
        }

        private static void DrawTransformCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_TRANSFORM", "📐 空间几何与快速对齐"));

            // 坐标微调
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_POS_X_LABEL", "横坐标 X:"), GUILayout.Width(65f));
            float newX = MFPGuiSkin.DrawBufferedFloatField($"posX_{w.WidgetId}", w.PositionX, 65f);
            if (Math.Abs(newX - w.PositionX) > 0.01f) { w.PositionX = newX; ApplyTransformRuntime(w); }

            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX -= 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f))) { w.PositionX -= 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f))) { w.PositionX += 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX += 10f; ApplyTransformRuntime(w); }

            GUILayout.Space(12f);

            GUILayout.Label(I18n.Tr("ASM_POS_Y_LABEL", "纵坐标 Y:"), GUILayout.Width(65f));
            float newY = MFPGuiSkin.DrawBufferedFloatField($"posY_{w.WidgetId}", w.PositionY, 65f);
            if (Math.Abs(newY - w.PositionY) > 0.01f) { w.PositionY = newY; ApplyTransformRuntime(w); }

            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY -= 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f))) { w.PositionY -= 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(28f))) { w.PositionY += 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY += 10f; ApplyTransformRuntime(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 快速对齐预设按钮
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_ALIGN_LABEL", "对齐吸附:"), GUILayout.Width(65f));
            if (GUILayout.Button(I18n.Tr("ASM_BTN_SNAP10", "🧲 10px 网格"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(85f)))
            {
                w.PositionX = Mathf.Round(w.PositionX / 10f) * 10f;
                w.PositionY = Mathf.Round(w.PositionY / 10f) * 10f;
                ApplyTransformRuntime(w);
            }
            if (GUILayout.Button(I18n.Tr("ASM_BTN_CENTER_X", "↔ 水平居中"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(85f))) { w.PositionX = 0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_BTN_CENTER_Y", "↕ 垂直居中"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(85f))) { w.PositionY = 0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_BTN_ORIGIN", "⌖ 归零 (0,0)"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(85f))) { w.PositionX = 0f; w.PositionY = 0f; ApplyTransformRuntime(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 缩放控制 (ScaleX / ScaleY)
            GUILayout.BeginHorizontal();
            float effX = w.EffectiveScaleX;
            GUILayout.Label(I18n.TrFormat("ASM_SCALE_X_LABEL", effX), GUILayout.Width(95f));
            float sxVal = GUILayout.HorizontalSlider(effX, 0.3f, 3.0f, GUILayout.Width(130f));
            if (Math.Abs(sxVal - effX) > 0.01f) { w.ScaleX = Mathf.Round(sxVal * 20f) / 20f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("0.7x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 0.7f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.0x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 1.0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.4x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 1.4f; ApplyTransformRuntime(w); }

            GUILayout.Space(10f);

            float effY = w.EffectiveScaleY;
            GUILayout.Label(I18n.TrFormat("ASM_SCALE_Y_LABEL", effY), GUILayout.Width(80f));
            float syVal = GUILayout.HorizontalSlider(effY, 0.3f, 3.0f, GUILayout.Width(110f));
            if (Math.Abs(syVal - effY) > 0.01f) { w.ScaleY = Mathf.Round(syVal * 20f) / 20f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("0.7x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleY = 0.7f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.0x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleY = 1.0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.4x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleY = 1.4f; ApplyTransformRuntime(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 图层层级与安全锁定
            GUILayout.BeginHorizontal();
            int curLayer = w.DrawOrder + 1;
            int totalLayers = WidgetLayerManager.TotalLayers;
            GUILayout.Label(I18n.TrFormat("ASM_LAYER_LABEL", curLayer, totalLayers), GUILayout.Width(125f));
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_BOTTOM", "⤓ 置底"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f))) { WidgetLayerManager.SendToBack(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_DOWN", "▼ 降层"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f))) { WidgetLayerManager.SendBackward(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_UP", "▲ 升层"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f))) { WidgetLayerManager.BringForward(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_TOP", "⤒ 置顶"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f))) { WidgetLayerManager.BringToFront(w); }

            GUILayout.Space(10f);
            bool isLocked = GUILayout.Toggle(w.IsLocked, w.IsLocked ? I18n.Tr("LAYER_LOCKED", "🔒 锁定图层") : I18n.Tr("LAYER_UNLOCKED", "🔓 未锁定"));
            if (isLocked != w.IsLocked)
            {
                var widgets = FlightHUDManager.Instance?.ModularWidgets;
                var targetWidget = widgets != null ? widgets.FirstOrDefault(x => x.WidgetId == w.WidgetId) : null;
                if (targetWidget != null)
                {
                    WidgetLayerManager.SetLock(targetWidget, isLocked);
                }
                else
                {
                    w.IsLocked = isLocked;
                    WidgetLayoutManager.Instance.SaveLayout();
                }
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void DrawDialOrTapeCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CALIBRATION", "📊 仪表数据驱动与量程标定"));

            // 1. 主遥测驱动数据源 (带实时采样与速查抽屉触发)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_TOKEN_NUMERIC", "主驱动数据源:"), GUILayout.Width(95f));

            string tokenText = string.IsNullOrEmpty(w.NumericToken) ? I18n.Tr("ASM_TOKEN_UNBOUND", "<未绑定>") : w.NumericToken;
            string editToken = GUILayout.TextField(w.NumericToken ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(160f));
            if (editToken != w.NumericToken)
            {
                w.NumericToken = editToken;
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            // 🔍 选参数按钮 -> 打开速查抽屉
            if (GUILayout.Button(I18n.Tr("ASM_BTN_PICK_PARAM", "🔍 选参数"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                TelemetryParamDrawer.Open(w.DisplayName + I18n.Tr("ASM_PARAM_MAIN_SRC", " 主驱动源"), chosenToken =>
                {
                    w.NumericToken = chosenToken;
                    var meta = TelemetryCatalog.FindByToken(chosenToken);
                    if (meta != null)
                    {
                        w.MinValue = (float)meta.DefaultMin;
                        w.MaxValue = (float)meta.DefaultMax;
                        w.CautionThreshold = (float)meta.DefaultCaution;
                        w.WarningThreshold = (float)meta.DefaultWarning;
                        w.UnitLabel = meta.DefaultUnit;
                        if (w.WidgetType == "tape") w.StepInterval = meta.DefaultStep;
                    }
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                });
            }

            // 实时采样值预览
            string liveSample = GetSampledTokenValue(w.NumericToken);
            GUILayout.Space(6f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{liveSample}</b></color> <size=9>{w.UnitLabel}</size>", GUILayout.Width(90f));

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(w.NumericToken))
            {
                if (GUILayout.Button(I18n.Tr("ASM_TOKEN_UNBIND", "解绑"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f)))
                {
                    w.NumericToken = "";
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
            }
            GUILayout.EndHorizontal();

            var paramMeta = TelemetryCatalog.FindByToken(w.NumericToken);
            if (paramMeta != null)
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>• {paramMeta.DisplayName}: {paramMeta.Description}</size></color>");
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 量程下限与上限
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_MIN", "量程下限:"), GUILayout.Width(95f));
            double newMin = MFPGuiSkin.DrawBufferedDoubleField($"min_{w.WidgetId}", w.MinValue, 75f);
            if (Math.Abs(newMin - w.MinValue) > 0.0001) { w.MinValue = (float)newMin; MarkDirty(); }

            GUILayout.Space(16f);

            GUILayout.Label(I18n.Tr("ASM_RANGE_MAX", "量程上限:"), GUILayout.Width(95f));
            double newMax = MFPGuiSkin.DrawBufferedDoubleField($"max_{w.WidgetId}", w.MaxValue, 75f);
            if (Math.Abs(newMax - w.MaxValue) > 0.0001) { w.MaxValue = (float)newMax; MarkDirty(); }

            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 3. 黄色警戒与红色告警阈值
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_CAUTION", "警戒阈值 (黄):"), GUILayout.Width(95f));
            double newCaution = MFPGuiSkin.DrawBufferedDoubleField($"caut_{w.WidgetId}", w.CautionThreshold, 75f);
            if (Math.Abs(newCaution - w.CautionThreshold) > 0.0001) { w.CautionThreshold = (float)newCaution; MarkDirty(); }

            GUILayout.Space(16f);

            GUILayout.Label(I18n.Tr("ASM_RANGE_WARNING", "告警阈值 (红):"), GUILayout.Width(95f));
            double newWarning = MFPGuiSkin.DrawBufferedDoubleField($"warn_{w.WidgetId}", w.WarningThreshold, 75f);
            if (Math.Abs(newWarning - w.WarningThreshold) > 0.0001) { w.WarningThreshold = (float)newWarning; MarkDirty(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 4. 单位标注与步长
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_UNIT", "单位标注:"), GUILayout.Width(95f));
            string newUnit = GUILayout.TextField(w.UnitLabel ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(80f));
            if (newUnit != w.UnitLabel) { w.UnitLabel = newUnit; MarkDirty(); }

            if (w.WidgetType == "tape")
            {
                GUILayout.Space(16f);
                GUILayout.Label(I18n.Tr("ASM_TAPE_STEP", "标尺步长:"), GUILayout.Width(65f));
                float newStep = MFPGuiSkin.DrawBufferedFloatField($"step_{w.WidgetId}", w.StepInterval, 60f);
                if (Math.Abs(newStep - w.StepInterval) > 0.01f) { w.StepInterval = newStep; MarkDirty(); }
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static string GetDefaultChannelName(int index)
        {
            switch (index)
            {
                case 0: return I18n.Tr("ASM_CH_SPD", "空速 (SPD)");
                case 1: return I18n.Tr("ASM_CH_TWR", "推重比 (TWR)");
                case 2: return I18n.Tr("ASM_CH_RALT", "雷达真高 (RALT)");
                case 3: return I18n.Tr("ASM_CH_Q", "大气动压 (Q)");
                case 4: return I18n.Tr("ASM_CH_VSI", "垂直升降 (VSI)");
                case 5: return I18n.Tr("ASM_CH_G", "重力过载 (G)");
                default: return $"CH{index + 1}";
            }
        }

        private static readonly string[] DefaultChannelKeys = new string[] { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6" };
        private static readonly string[] DefaultChannelTokens = new string[] { "{SPD}", "{TWR}", "{ALT:AGL}", "{Q}", "{VSI}", "{GFORCE}" };

        private static void DrawMultiChannelCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();

            // 标题与源码/可视化模式切换
            GUILayout.BeginHorizontal();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CHANNELS_MJ", "🎛️ MechJeb 综合遥测矩阵配置"));

            string modeBtnLabel = _showRawTemplateSource ? I18n.Tr("ASM_MODE_VISUAL", "🎛️ 可视化矩阵模式") : I18n.Tr("ASM_MODE_RAW", "📝 原始模板源码");
            if (GUILayout.Button(modeBtnLabel, MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(130f), GUILayout.Height(22f)))
            {
                _showRawTemplateSource = !_showRawTemplateSource;
            }
            GUILayout.EndHorizontal();

            if (_showRawTemplateSource)
            {
                // ==========================================
                // 模式 1: 原始模板源码 (Raw Code)
                // ==========================================
                MFPGuiSkin.BeginInset();
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("ASM_RAW_TPL_DESC", "直接编辑结构化通道模板 (格式: MODE=KV/TABLE;COLS=N;ROWS=M; 或 CH1={TOKEN};)：")}</size></color>");
                string newTemplate = GUILayout.TextArea(w.CustomTemplate ?? "", GUILayout.Height(65f));
                if (newTemplate != w.CustomTemplate)
                {
                    w.CustomTemplate = newTemplate;
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("ASM_BTN_ADD_SEMICOLON", "+ 分号 ';'"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
                {
                    w.CustomTemplate = (w.CustomTemplate ?? "") + ";";
                    MarkDirty();
                }
                if (GUILayout.Button(I18n.Tr("ASM_TPL_CLEAR", "清空模板"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
                {
                    w.CustomTemplate = "";
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
            }
            else
            {
                // ==========================================
                // 模式 2: 可视化矩阵编排 (Visual Matrix Form)
                // ==========================================
                var matrixData = TelemetryMatrixData.FromTemplate(w.CustomTemplate);
                bool isTable = (matrixData.Mode == MatrixDisplayMode.Table);

                // 1. 常用 MechJeb 预设快速生成栏
                MFPGuiSkin.BeginInset();
                GUILayout.Label($"<b><size=10><color=#{MFPGuiSkin.HexAccentCyan}>{I18n.Tr("ASM_QUICK_PRESET_TITLE", "MechJeb 航电矩阵常用预设:")}</color></size></b>");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_2COL", "🚀 经典 2 列 (6 通道)"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateDefaultKeyValue().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast("✔ 已应用 MechJeb 经典 2 列遥测矩阵");
                    return;
                }
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_1COL", "📋 单列监控"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateSingleColumnMonitor().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast("✔ 已应用 MechJeb 单列综合监控卡");
                    return;
                }
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_TABLE", "📊 分级 ΔV 表格"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateDefaultTable().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast("✔ 已应用 MechJeb 分级 ΔV 数据表");
                    return;
                }
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_ORBIT", "🛰️ 轨道机动"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateOrbitalMatrix().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast("✔ 已应用轨道与机动参数矩阵");
                    return;
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(4f);

                // 2. 模式与行列维度控制栏
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();

                // 模式切换
                bool newIsTable = GUILayout.Toggle(isTable, isTable ? I18n.Tr("ASM_MODE_TABLE", "📊 数据表格模式") : I18n.Tr("ASM_MODE_KV", "🎛️ 键值网格模式"), GUILayout.Width(130f));
                if (newIsTable != isTable)
                {
                    matrixData.Mode = newIsTable ? MatrixDisplayMode.Table : MatrixDisplayMode.KeyValue;
                    if (newIsTable && matrixData.TableHeaders.Count == 0)
                    {
                        for (int c = 0; c < matrixData.Columns; c++) matrixData.TableHeaders.Add($"Col {c + 1}");
                    }
                    w.CustomTemplate = matrixData.ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    return;
                }

                GUILayout.Space(10f);

                // 列数步进器
                GUILayout.Label(string.Format(I18n.Tr("ASM_COLS_LABEL", "列数: {0}"), matrixData.Columns), GUILayout.Width(65f));
                if (GUILayout.Button("-", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (matrixData.Columns > 1)
                    {
                        matrixData.SetColumns(matrixData.Columns - 1);
                        w.CustomTemplate = matrixData.ToTemplate();
                        MarkDirty();
                        FlightHUDManager.Instance?.RebuildHUD();
                        return;
                    }
                }
                if (GUILayout.Button("+", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    if (matrixData.Columns < 12)
                    {
                        matrixData.SetColumns(matrixData.Columns + 1);
                        w.CustomTemplate = matrixData.ToTemplate();
                        MarkDirty();
                        FlightHUDManager.Instance?.RebuildHUD();
                        return;
                    }
                }

                GUILayout.Space(12f);

                // 行数提示与添加行按钮
                GUILayout.Label(string.Format(I18n.Tr("ASM_ROWS_LABEL", "行数: {0}"), matrixData.Rows), GUILayout.Width(65f));
                if (GUILayout.Button(I18n.Tr("ASM_BTN_ADD_ROW", "+ 添加行"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(80f), GUILayout.Height(22f)))
                {
                    matrixData.AddRow();
                    w.CustomTemplate = matrixData.ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    return;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(4f);

                // 3. 矩阵单元格逐行逐列表单
                if (isTable)
                {
                    // 表格模式：表头设置
                    MFPGuiSkin.BeginInset();
                    GUILayout.Label($"<b><size=10>{I18n.Tr("ASM_TBL_HEADERS_TITLE", "表格各列标题 (表头):")}</size></b>");
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < matrixData.Columns; c++)
                    {
                        string curH = (c < matrixData.TableHeaders.Count) ? matrixData.TableHeaders[c] : $"Col {c + 1}";
                        string newH = GUILayout.TextField(curH ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(75f));
                        if (newH != curH)
                        {
                            while (matrixData.TableHeaders.Count <= c) matrixData.TableHeaders.Add($"Col {matrixData.TableHeaders.Count + 1}");
                            matrixData.TableHeaders[c] = newH;
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                        }
                    }
                    GUILayout.EndHorizontal();
                    MFPGuiSkin.EndInset();
                    GUILayout.Space(4f);

                    // 表格数据行
                    for (int r = 0; r < matrixData.Rows; r++)
                    {
                        int rowIdx = r;
                        MFPGuiSkin.BeginInset();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>{string.Format(I18n.Tr("ASM_ROW_HEADER", "行 #{0}"), r + 1)}</b>", GUILayout.Width(50f));

                        GUI.enabled = (r > 0);
                        if (GUILayout.Button("▲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                        {
                            matrixData.MoveRow(r, r - 1);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = (r < matrixData.Rows - 1);
                        if (GUILayout.Button("▼", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                        {
                            matrixData.MoveRow(r, r + 1);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = (matrixData.Rows > 1);
                        if (GUILayout.Button(I18n.Tr("ASM_BTN_DEL_ROW", "🗑 删除"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
                        {
                            matrixData.RemoveRow(r);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = true;

                        GUILayout.EndHorizontal();

                        // 行内各列 Token 输入
                        GUILayout.BeginHorizontal();
                        for (int c = 0; c < matrixData.Columns; c++)
                        {
                            int colIdx = c;
                            var cell = (rowIdx < matrixData.Grid.Count && colIdx < matrixData.Grid[rowIdx].Count) ? matrixData.Grid[rowIdx][colIdx] : new MatrixCellData();
                            string curTok = cell.Token ?? "";

                            string newTok = GUILayout.TextField(curTok, MFPGuiSkin.SearchFieldStyle, GUILayout.Width(68f));
                            if (newTok != curTok)
                            {
                                cell.Token = newTok;
                                w.CustomTemplate = matrixData.ToTemplate();
                                MarkDirty();
                                FlightHUDManager.Instance?.RebuildHUD();
                            }

                            if (GUILayout.Button("🔍", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                            {
                                string colName = (colIdx < matrixData.TableHeaders.Count) ? matrixData.TableHeaders[colIdx] : $"C{colIdx + 1}";
                                TelemetryParamDrawer.Open($"{colName} R{rowIdx + 1}", chosenToken =>
                                {
                                    cell.Token = chosenToken;
                                    w.CustomTemplate = matrixData.ToTemplate();
                                    MarkDirty();
                                    FlightHUDManager.Instance?.RebuildHUD();
                                });
                            }
                        }
                        GUILayout.EndHorizontal();

                        MFPGuiSkin.EndInset();
                        GUILayout.Space(2f);
                    }
                }
                else
                {
                    // 键值网格模式：每一行展示各列的 [标签] + [Token] + [选参数] + [实时采样]
                    for (int r = 0; r < matrixData.Rows; r++)
                    {
                        int rowIdx = r;
                        MFPGuiSkin.BeginInset();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>{string.Format(I18n.Tr("ASM_ROW_HEADER", "行 #{0}"), r + 1)}</b>", GUILayout.Width(60f));

                        GUI.enabled = (r > 0);
                        if (GUILayout.Button("▲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                        {
                            matrixData.MoveRow(r, r - 1);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = (r < matrixData.Rows - 1);
                        if (GUILayout.Button("▼", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                        {
                            matrixData.MoveRow(r, r + 1);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = (matrixData.Rows > 1);
                        if (GUILayout.Button(I18n.Tr("ASM_BTN_DEL_ROW", "🗑 删除"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
                        {
                            matrixData.RemoveRow(r);
                            w.CustomTemplate = matrixData.ToTemplate();
                            MarkDirty();
                            FlightHUDManager.Instance?.RebuildHUD();
                            return;
                        }
                        GUI.enabled = true;

                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        // 逐列渲染单元格表单
                        for (int c = 0; c < matrixData.Columns; c++)
                        {
                            int colIdx = c;
                            var cell = (rowIdx < matrixData.Grid.Count && colIdx < matrixData.Grid[rowIdx].Count) ? matrixData.Grid[rowIdx][colIdx] : new MatrixCellData();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>C{c + 1}</size></color>", GUILayout.Width(20f));

                            // 标签
                            string curLbl = cell.Label ?? "";
                            string newLbl = GUILayout.TextField(curLbl, MFPGuiSkin.SearchFieldStyle, GUILayout.Width(70f));
                            if (newLbl != curLbl)
                            {
                                cell.Label = newLbl;
                                w.CustomTemplate = matrixData.ToTemplate();
                                MarkDirty();
                                FlightHUDManager.Instance?.RebuildHUD();
                            }

                            // Token
                            string curTok = cell.Token ?? "";
                            string newTok = GUILayout.TextField(curTok, MFPGuiSkin.SearchFieldStyle, GUILayout.Width(130f));
                            if (newTok != curTok)
                            {
                                cell.Token = newTok;
                                w.CustomTemplate = matrixData.ToTemplate();
                                MarkDirty();
                                FlightHUDManager.Instance?.RebuildHUD();
                            }

                            // 🔍 选参数
                            if (GUILayout.Button("🔍", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(26f), GUILayout.Height(20f)))
                            {
                                TelemetryParamDrawer.Open($"{cell.Label} 遥测参数", chosenToken =>
                                {
                                    cell.Token = chosenToken;
                                    var meta = TelemetryCatalog.FindByToken(chosenToken);
                                    if (string.IsNullOrEmpty(cell.Label) || cell.Label.StartsWith("CH") || cell.Label.StartsWith("R"))
                                    {
                                        cell.Label = meta != null ? meta.DisplayName : chosenToken.Trim('{', '}');
                                    }
                                    w.CustomTemplate = matrixData.ToTemplate();
                                    MarkDirty();
                                    FlightHUDManager.Instance?.RebuildHUD();
                                });
                            }

                            // 实时采样值预览
                            string sampleVal = GetSampledTokenValue(curTok);
                            GUILayout.Space(4f);
                            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{sampleVal}</b></color>", GUILayout.Width(75f));

                            GUILayout.EndHorizontal();
                        }

                        MFPGuiSkin.EndInset();
                        GUILayout.Space(2f);
                    }
                }
            }

            GUILayout.Space(6f);

            // 实时综合解算预览
            DrawLiveEvaluationBox(w.CustomTemplate);

            MFPGuiSkin.EndCard();
        }

        private static void DrawCoreInfoCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CORE_TITLE", "⚙️ 核心内建飞行仪表组件"));
            GUILayout.Label($"{I18n.Tr("ASM_CORE_WIDGET_ID", "• 组件标识: ")}<color=#00E5FF>{w.WidgetId}</color>");
            GUILayout.Label($"{I18n.Tr("ASM_CORE_STATUS", "• 运行状态: ")}{(w.IsEnabled ? $"<color=#00FF88>{I18n.Tr("ASM_CORE_RUNNING", "● 正在运行")}</color>" : $"<color=#888888>{I18n.Tr("ASM_CORE_SUSPENDED", "○ 已挂起隐藏")}</color>")}");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("ASM_CORE_DESC", "核心组件包含底层管线逻辑 (如 3D 姿态球、滑动罗盘、SAS底座、工具栏坞)，位置与缩放可在上方直接调整或在屏幕拖拽。")}</size></color>");

            GUILayout.Space(6f);

            // 通用自定义模板与通道配置 (CustomTemplate)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_CORE_TPL_LABEL", "<b>自定义模板:</b>"), GUILayout.Width(110f));
            string newTpl = GUILayout.TextField(w.CustomTemplate ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.ExpandWidth(true));
            if (newTpl != w.CustomTemplate)
            {
                w.CustomTemplate = newTpl;
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            if (GUILayout.Button(I18n.Tr("ASM_BTN_PICK_PARAM", "🔍 选参数"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                TelemetryParamDrawer.Open(w.DisplayName + I18n.Tr("ASM_PARAM_TEMPLATE", " 模板"), chosenToken =>
                {
                    string cur = w.CustomTemplate ?? "";
                    string prefix = string.IsNullOrEmpty(cur) ? "" : (cur.EndsWith(";") ? "" : ";");
                    w.CustomTemplate = cur + prefix + chosenToken + ";";
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                });
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private static Vector2 _asmSubScrollPos = Vector2.zero;

        private static void DrawMicroControlsCard(WidgetConfig w)
        {
            BaseFlightWidget runtime = null;
            if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null)
            {
                runtime = FlightHUDManager.Instance.ModularWidgets.Find(x => x.WidgetId == w.WidgetId);
            }

            var ctrlList = runtime?.Controls?.All;
            if (ctrlList == null || ctrlList.Count == 0) return;

            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("ASM_CARD_MICRO_CONTROLS", "⚙️ 组件子控件配置与显隐排版"),
                string.Format(I18n.Tr("ASM_MICRO_COUNT", "已纳管 {0} 个微控件 (支持独立显隐与局部位移)"), ctrlList.Count)
            );

            // 批处理栏
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("MGR_BTN_SHOW_ALL", "✔ 全部显示"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(20f)))
            {
                for (int i = 0; i < ctrlList.Count; i++) runtime.Controls.SetControlVisibility(ctrlList[i].Id, true);
                MarkDirty();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            if (GUILayout.Button(I18n.Tr("MGR_BTN_HIDE_ALL", "○ 全部隐藏"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(20f)))
            {
                for (int i = 0; i < ctrlList.Count; i++) runtime.Controls.SetControlVisibility(ctrlList[i].Id, false);
                MarkDirty();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            if (GUILayout.Button(I18n.Tr("CTL_RESET_ALL_OFFSETS", "↺ 全部复位"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(20f)))
            {
                runtime.Controls.ResetAllOffsets();
                MarkDirty();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("ASM_SHIFT_STEP_HINT", "Shift: 10px 快速步进")}</size></color>");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            _asmSubScrollPos = GUILayout.BeginScrollView(_asmSubScrollPos, GUILayout.MaxHeight(180f));
            for (int i = 0; i < ctrlList.Count; i++)
            {
                var ctrl = ctrlList[i];
                if (ctrl == null) continue;

                GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(24f));

                // 1. 显隐
                string led = ctrl.IsVisible ? $"<color=#00FF88>{I18n.Tr("ASM_CTRL_VISIBLE", "● 显")}</color>" : $"<color=#7088A8>{I18n.Tr("ASM_CTRL_HIDDEN", "○ 隐")}</color>";
                if (GUILayout.Button(led, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlVisibility(ctrl.Id, !ctrl.IsVisible);
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }

                // 2. 类别
                string catTag = GetCategoryShortTag(ctrl.Category);
                MFPGuiSkin.DrawBadge(catTag, Color.white, GetCategoryColor(ctrl.Category), 42f);

                // 3. 名称
                GUILayout.Label($"<b>{ctrl.DisplayName}</b>", GUILayout.Width(130f));

                // 4. 微调按钮
                float step = Event.current.shift ? 10f : 2f;
                Vector2 curOff = ctrl.CurrentOffset;

                if (GUILayout.Button("◀", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(-step, 0f));
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▶", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(step, 0f));
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(0f, step));
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▼", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(0f, -step));
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }

                // 5. 偏移数值与复位
                bool hasOff = Mathf.Abs(curOff.x) > 0.01f || Mathf.Abs(curOff.y) > 0.01f;
                if (hasOff)
                {
                    GUILayout.Label($"<color=#00E5FF><size=10>{curOff.x:+0.0;-0.0;0}, {curOff.y:+0.0;-0.0;0}</size></color>", GUILayout.Width(68f));
                    if (GUILayout.Button("↺", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                    {
                        runtime.Controls.SetControlOffset(ctrl.Id, Vector2.zero);
                        MarkDirty();
                        WidgetLayoutManager.Instance.SaveLayout();
                    }
                }
                else
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>0.0, 0.0</size></color>", GUILayout.Width(68f));
                    GUILayout.Space(26f);
                }

                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.Repaint)
                {
                    Rect rowRect = GUILayoutUtility.GetLastRect();
                    if (rowRect.Contains(Event.current.mousePosition))
                    {
                        WidgetControlHighlighter.HighlightedControl = ctrl;
                    }
                }
            }
            GUILayout.EndScrollView();

            MFPGuiSkin.EndCard();
        }

        private static string GetCategoryShortTag(WidgetControlCategory cat)
        {
            switch (cat)
            {
                case WidgetControlCategory.Header: return I18n.Tr("ASM_CAT_HEADER", "标题");
                case WidgetControlCategory.Readout: return I18n.Tr("ASM_CAT_READOUT", "数显");
                case WidgetControlCategory.LinearGauge: return I18n.Tr("ASM_CAT_LINEAR_GAUGE", "柱条");
                case WidgetControlCategory.ArcGauge: return I18n.Tr("ASM_CAT_ARC_GAUGE", "弧表");
                case WidgetControlCategory.NeedlePointer: return I18n.Tr("ASM_CAT_NEEDLE", "指针");
                case WidgetControlCategory.ActionButton: return I18n.Tr("ASM_CAT_BTN", "按键");
                case WidgetControlCategory.Annunciator: return I18n.Tr("ASM_CAT_ANNUNCIATOR", "灯珠");
                case WidgetControlCategory.Viewport: return I18n.Tr("ASM_CAT_VIEWPORT", "视口");
                case WidgetControlCategory.DataStack: return I18n.Tr("ASM_CAT_DATA_STACK", "列表");
                case WidgetControlCategory.ModeCapsule: return I18n.Tr("ASM_CAT_CAPSULE", "胶囊");
                case WidgetControlCategory.TrendBar: return I18n.Tr("ASM_CAT_TREND", "趋势");
                default: return I18n.Tr("ASM_CAT_PRIMITIVE", "图元");
            }
        }

        private static Color GetCategoryColor(WidgetControlCategory cat)
        {
            switch (cat)
            {
                case WidgetControlCategory.Readout: return new Color(0.00f, 0.45f, 0.65f, 0.9f);
                case WidgetControlCategory.LinearGauge:
                case WidgetControlCategory.ArcGauge: return new Color(0.00f, 0.50f, 0.30f, 0.9f);
                case WidgetControlCategory.ActionButton: return new Color(0.55f, 0.35f, 0.05f, 0.9f);
                case WidgetControlCategory.Header: return new Color(0.35f, 0.20f, 0.50f, 0.9f);
                default: return new Color(0.25f, 0.30f, 0.38f, 0.9f);
            }
        }

        private static void DrawTelemetryDrawerLauncherCard(WidgetConfig curWidget)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawHeader(
                I18n.Tr("ASM_DRAWER_LAUNCHER_TITLE", "📖 遥测字典速查与智能填槽中枢 (736+ 参数)"),
                I18n.Tr("ASM_DRAWER_LAUNCHER_SUB", "按 13 大航电领域归类检索，支持中文模糊快搜与实机真实数据采样。")
            );
            GUILayout.EndVertical();

            if (GUILayout.Button(I18n.Tr("ASM_BTN_OPEN_DRAWER", "🔍 打开全量遥测抽屉"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(160f), GUILayout.Height(28f)))
            {
                TelemetryParamDrawer.Open(curWidget.DisplayName, token =>
                {
                    if (curWidget.WidgetType == "ecam_dial" || curWidget.WidgetType == "tape" || curWidget.WidgetType == "arc_meter" || curWidget.WidgetType == "bar_gauge")
                    {
                        curWidget.NumericToken = token;
                    }
                    else
                    {
                        string cur = curWidget.CustomTemplate ?? "";
                        string prefix = string.IsNullOrEmpty(cur) ? "" : (cur.EndsWith(";") ? "" : ";");
                        curWidget.CustomTemplate = cur + prefix + token + ";";
                    }
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                });
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();
        }

        private static void DrawPerformanceCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_PERF", "⚡ 绘制性能单独调优"));

            // 画布隔离
            bool prevIsolate = w.IsolateCanvas;
            w.IsolateCanvas = GUILayout.Toggle(w.IsolateCanvas, I18n.Tr("ASM_PROP_ISOLATE_CANVAS", " 启用独立画布"));
            if (w.IsolateCanvas != prevIsolate)
            {
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(w.IsolateCanvas ? I18n.Tr("ASM_CANVAS_ENABLED", "已开启画布隔离") : I18n.Tr("ASM_CANVAS_DISABLED", "已关闭画布隔离"));
            }

            GUILayout.Space(4f);

            // 刷新阶梯
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_PERF_REFRESH_MODE", "刷新分频模式:"), GUILayout.Width(90f));
            string hzLabel = w.UpdateInterval <= 0f ? I18n.Tr("ASM_HZ_60_PLUS", "60Hz+ (每帧)") :
                            (w.UpdateInterval <= 0.06f ? "20Hz (0.05s)" :
                            (w.UpdateInterval <= 0.15f ? "10Hz (0.1s)" :
                            (w.UpdateInterval <= 0.25f ? "5Hz (0.2s)" : "2Hz (0.5s)")));
            GUILayout.Label($"<b><color=#00E5FF>{hzLabel}</color></b>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("ASM_PERF_60HZ", "60Hz 满血"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.UpdateInterval = 0f; MarkDirty(); ShowToast(I18n.Tr("ASM_TOAST_HZ_60", "已设为 60Hz 满帧刷新")); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_20HZ", "20Hz 标称"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.UpdateInterval = 0.05f; MarkDirty(); ShowToast(I18n.Tr("ASM_TOAST_HZ_20", "已设为 20Hz")); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_10HZ", "10Hz 舒缓"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.UpdateInterval = 0.1f; MarkDirty(); ShowToast(I18n.Tr("ASM_TOAST_HZ_10", "已设为 10Hz")); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_2HZ", "2Hz 节能"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.UpdateInterval = 0.5f; MarkDirty(); ShowToast(I18n.Tr("ASM_TOAST_HZ_2", "已设为 2Hz 节能")); }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void DrawLiveEvaluationBox(string template)
        {
            if (Time.unscaledTime - _lastEvalTime > 0.25f || _cachedTemplate != template)
            {
                _lastEvalTime = Time.unscaledTime;
                _cachedTemplate = template;
                try
                {
                    if (!string.IsNullOrEmpty(template) && template.IndexOf('=') >= 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        string[] pairs = template.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < pairs.Length; i++)
                        {
                            string p = pairs[i].Trim();
                            int eq = p.IndexOf('=');
                            if (eq > 0)
                            {
                                string k = p.Substring(0, eq).Trim();
                                string v = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : "";
                                string evaluatedVal = TelemetryTokenEngine.Evaluate(v, TelemetryHub.Instance);
                                sb.AppendLine($"{k} ➔ {evaluatedVal}");
                            }
                            else
                            {
                                sb.AppendLine(TelemetryTokenEngine.Evaluate(p, TelemetryHub.Instance));
                            }
                        }
                        _cachedEvaluation = sb.ToString().TrimEnd();
                    }
                    else
                    {
                        _cachedEvaluation = TelemetryTokenEngine.Evaluate(template, TelemetryHub.Instance);
                    }
                    if (string.IsNullOrEmpty(_cachedEvaluation)) _cachedEvaluation = I18n.Tr("ASM_TPL_EMPTY", "<空模板>");
                }
                catch (Exception ex)
                {
                    _cachedEvaluation = I18n.TrFormat("ASM_TPL_SYNTAX_ERR", ex.Message);
                }
            }

            GUILayout.Label(I18n.Tr("ASM_LIVE_PREVIEW", "<b>🌟 航电真实遥测实时解算预览:</b>"));
            MFPGuiSkin.DrawBadge(_cachedEvaluation, MFPGuiSkin.AccentGreen, new Color(0.04f, 0.08f, 0.12f, 0.98f));
        }

        #endregion

        #region Channel Helpers & Parsing

        private static Dictionary<string, string> ParseChannelsFromTemplate(string template)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return dict;

            string[] parts = template.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string v = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : "";
                    dict[k] = v;
                }
            }
            return dict;
        }

        private static void SetChannelInTemplate(WidgetConfig w, string channelKey, string token)
        {
            var dict = ParseChannelsFromTemplate(w.CustomTemplate);
            dict[channelKey] = token;

            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            w.CustomTemplate = sb.ToString();
            MarkDirty();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        private static string GetSampledTokenValue(string token)
        {
            if (string.IsNullOrEmpty(token)) return "---";
            string val;
            if (_channelEvalCache.TryGetValue(token, out val) && Time.unscaledTime - _lastEvalTime <= 0.25f)
            {
                return val;
            }
            try
            {
                val = TelemetryTokenEngine.Evaluate(token, TelemetryHub.Instance);
                if (string.IsNullOrEmpty(val)) val = "---";
            }
            catch
            {
                val = "ERR";
            }
            _channelEvalCache[token] = val;
            return val;
        }

        #endregion

        #region Helpers

        private static void ApplyTransformRuntime(WidgetConfig w)
        {
            MarkDirty();
            if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null)
            {
                var target = FlightHUDManager.Instance.ModularWidgets.Find(x => x.WidgetId == w.WidgetId);
                if (target != null)
                {
                    target.UpdateTransform(w.PositionX, w.PositionY, w.Scale, w.Rotation, w.ScaleX, w.ScaleY);
                }
            }
        }

        private static void MarkDirty()
        {
            _isDirty = true;
            _dirtyTimer = 0f;
        }

        public static void CommitPendingSaves()
        {
            if (!_isDirty) return;
            _isDirty = false;
            _dirtyTimer = 0f;
            WidgetLayoutManager.Instance.SaveLayout();
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }

        #endregion
    }
}
