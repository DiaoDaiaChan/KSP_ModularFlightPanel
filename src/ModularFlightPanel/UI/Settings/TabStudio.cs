using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.HUD;
using AnnunciatorState = ModularFlightPanel.UI.Framework.AnnunciatorState;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新航电一体化设计工坊 (Modern Avionics Studio Workbench - TabStudio)
    /// 深度融合航电组件库 (Palette)、挂载层级树 (Hierarchy) 与全功能属性检查器 (Inspector)，
    /// 形成现代化 IDE 级左右双栏联动工作流。
    /// 
    /// 核心特性：
    /// 1. 左栏组件中枢 (310px)：双模切片自由切换「📋 已挂载层级」与「📦 航电组件库」，支持即时搜索与批处理。
    /// 2. 右栏属性检查器：所见即所得的变换调整、仪表标定、多通道遥测矩阵与微控件 DSL 样式。
    /// 3. 无缝嵌入遥测字典速查抽屉 (TelemetryParamDrawer)：一键查表 736+ 参数并直填插槽。
    /// 4. 4Hz 定频遥测采样与脏标记防抖提交机制。
    /// </summary>
    public class TabStudio : ISettingsTab
    {
        public static TabStudio Instance { get; private set; }

        public TabStudio()
        {
            Instance = this;
        }

        public string TabId => "studio";
        public string DisplayTitle => I18n.Tr("UI_TAB_STUDIO", "🛠️ 航电工坊");

        // 左栏模式：0 = 已挂载层级 (Hierarchy), 1 = 航电组件库 (Palette), 2 = 常用 MOD 工具栏 (ModToolbar)
        private int _leftPanelMode = 0;

        // MOD 工具栏管理状态
        private Vector2 _modToolbarScroll = Vector2.zero;
        private string _modToolbarSearch = "";
        private int _modToolbarStatusFilter = 0; // 0=All, 1=Fav, 2=Visible, 3=Hidden
        private static string _selectedDockRuleKey = null;
        private Vector2 _modInspectorScroll = Vector2.zero;

        // 挂载层级树状态
        private Vector2 _hierarchyScroll = Vector2.zero;
        private string _hierarchySearch = "";
        private int _hierarchyStatusFilter = 0; // 0=All, 1=Enabled, 2=Disabled

        // 组件库状态
        private Vector2 _paletteScroll = Vector2.zero;
        private string _paletteSearch = "";
        private int _paletteCategoryFilter = 0; // 0=All, 1=Gauges, 2=Nav, 3=Systems, 4=SpaceX, 5=Controls
        private bool _showPreviews = true;

        // 自由搭建工坊专属状态 (Composite Panel Freeform Studio State)
        private string _selectedLayerId = null;
        private int _studioSubTab = 0; // 0 = 图层与精调 (Layers & Inspector), 1 = 构件素材箱 (Toolbox), 2 = 画板与预设 (Canvas & Presets)
        private string _toolboxSearch = "";
        private int _toolboxCategoryFilter = 0;
        private int _toolboxSourceFilter = 0;
        private Vector2 _layersScroll = Vector2.zero;
        private Vector2 _toolboxScroll = Vector2.zero;

        // 右栏属性检查器状态
        private Vector2 _inspectorScroll = Vector2.zero;
        private Vector2 _microControlsScroll = Vector2.zero;
        private static string _selectedWidgetId = null;
        public static string SelectedWidgetId => _selectedWidgetId;

        private bool _showRawTemplate = false;

        // 遥测预览缓存 (4Hz)
        private string _cachedTemplate = null;
        private string _cachedEvaluation = "---";
        private float _lastEvalTime = 0f;

        // 脏数据与防抖提交
        private bool _isDirty = false;
        private float _dirtyTimer = 0f;
        private string _toastMsg = "";
        private float _toastTimer = 0f;

        public static void SetSelectedWidget(string widgetId)
        {
            _selectedWidgetId = widgetId;
            if (TelemetryParamDrawer.IsOpen) TelemetryParamDrawer.Close();

            // 双向联动：同步将 HUD 上的实时实例置为唯一选中，即刻激活 8 点变换手柄
            if (!string.IsNullOrEmpty(widgetId) && FlightHUDManager.Instance?.ModularWidgets != null)
            {
                for (int i = 0; i < FlightHUDManager.Instance.ModularWidgets.Count; i++)
                {
                    var live = FlightHUDManager.Instance.ModularWidgets[i];
                    if (live != null && live.Config?.WidgetId == widgetId)
                    {
                        if (!WidgetSelectionManager.IsSelected(live))
                        {
                            WidgetSelectionManager.Select(live, false);
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 全局创建自由航电画板 (PS 自由搭建工坊)
        /// </summary>
        public static void CreateNewArtboard(bool blank = false)
        {
            var layout = WidgetLayoutManager.Instance.CurrentLayout;
            if (layout == null) return;

            string baseId = "custom.artboard";
            string newId = baseId;
            int counter = 1;
            while (layout.Widgets.Any(x => x.WidgetId == newId))
            {
                newId = $"{baseId}_{counter++}";
            }

            float px = Mathf.Round((Screen.width - 380f) * 0.5f / 10f) * 10f;
            float py = Mathf.Round((Screen.height - 220f) * 0.5f / 10f) * 10f;

            var demoCfg = blank ? CompositePanelConfig.CreateBlankPanel() : CompositePanelConfig.CreateDefaultDemoPanel();

            var w = new WidgetConfig(newId, I18n.Tr("COMP_ARTBOARD_DEFAULT_NAME", "自由航电仪表板"), px, py)
            {
                WidgetType = "composite_panel",
                Scale = 1.0f,
                Rotation = 0f,
                IsEnabled = true,
                CustomTemplate = demoCfg.ToJson()
            };

            layout.Widgets.Add(w);
            SetSelectedWidget(newId);
            if (Instance != null)
            {
                Instance._leftPanelMode = 0;
                Instance.CommitPendingSaves();
                Instance.ShowToast(I18n.Tr("LIB_TOAST_ARTBOARD_ADDED", "已创建自由航电画板！可在右侧工坊开始自由布局"));
            }
            else
            {
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
                MFPGuiSkin.ShowToast(I18n.Tr("LIB_TOAST_ARTBOARD_ADDED", "已创建自由航电画板！可在右侧工坊开始自由布局"));
            }

            // 同步激活编辑模式并选中新画板
            WidgetDragHandler.IsEditModeActive = true;
            if (FlightHUDManager.Instance?.ModularWidgets != null)
            {
                for (int i = 0; i < FlightHUDManager.Instance.ModularWidgets.Count; i++)
                {
                    var live = FlightHUDManager.Instance.ModularWidgets[i];
                    if (live != null && live.Config?.WidgetId == newId)
                    {
                        WidgetSelectionManager.Select(live, false);
                        break;
                    }
                }
            }
        }

        public void SetLeftPanelMode(int mode)
        {
            _leftPanelMode = Mathf.Clamp(mode, 0, 2);
            if (_leftPanelMode == 2)
            {
                EnsureDockRulesPopulated();
            }
        }

        public static void OpenToModToolbar()
        {
            if (SettingsGUI.Instance != null)
            {
                SettingsGUI.Instance.OpenToTab(0);
                if (SettingsGUI.Instance.CurrentTabInstance is TabStudio studio)
                {
                    studio.SetLeftPanelMode(2);
                }
            }
        }

        public static void EnsureDockRulesPopulated()
        {
            var themeMgr = ThemeManager.Instance;
            if (themeMgr == null) return;
            if (themeMgr.DockRules == null) themeMgr.DockRules = new List<DockButtonRule>();

#if KSP_RUNTIME
            try
            {
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                if (launcher != null)
                {
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    var all = new List<KSP.UI.Screens.ApplicationLauncherButton>();
                    if (stockBtns != null) all.AddRange(stockBtns);
                    if (modBtns != null) all.AddRange(modBtns);

                    for (int i = 0; i < all.Count; i++)
                    {
                        var btn = all[i];
                        if (btn == null) continue;
                        ModernToolbarWidget.GetButtonIdentity(btn, i, out string key, out string defName);
                        themeMgr.GetOrCreateDockRule(key, defName);
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("EnsureDockRules_Ksp", $"Error syncing toolbar rules: {ex.Message}");
            }
#endif

            // 若依然为空（例如在 Editor 预览或非 KSP 环境），注入标准模拟常用 MOD
            if (themeMgr.DockRules.Count == 0)
            {
                string[] mockKeys = { "MOCK_MJ", "MOCK_KER", "MOCK_TRAJ", "MOCK_MFP", "MOCK_DPAI", "MOCK_ALARM" };
                string[] mockNames = { "MechJeb", "KER", "Trajectories", "MFP", "DPAI", "AlarmClock" };
                for (int i = 0; i < mockKeys.Length; i++)
                {
                    var rule = themeMgr.GetOrCreateDockRule(mockKeys[i], mockNames[i]);
                    if (i < 4 && rule != null) rule.IsFavorite = true;
                }
            }
        }

        public void OnEnter()
        {
            _hierarchyScroll = Vector2.zero;
            _paletteScroll = Vector2.zero;
            _inspectorScroll = Vector2.zero;
            _microControlsScroll = Vector2.zero;
            // 确保有有效选中
            EnsureSelectedWidget();
        }

        public void OnExit()
        {
            if (_isDirty) CommitPendingSaves();
            if (TelemetryParamDrawer.IsOpen) TelemetryParamDrawer.Close();
        }

        public void Draw(float availableHeight)
        {
            MFPGuiSkin.EnsureInitialized();

            // 监听防抖提交
            if (Event.current.type == EventType.MouseUp && _isDirty)
            {
                CommitPendingSaves();
            }

            if (_isDirty)
            {
                _dirtyTimer += Time.unscaledDeltaTime;
                if (_dirtyTimer > 1.5f) CommitPendingSaves();
            }

            // 局部 Toast 通知
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            EnsureSelectedWidget();

            GUILayout.BeginHorizontal(GUILayout.Height(availableHeight));

            // =========================================================================
            // 左栏：组件中枢 (层级树 / 组件库, 310px 宽)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(310f), GUILayout.Height(availableHeight));
            DrawLeftPanel(widgets, availableHeight);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // =========================================================================
            // 右栏：属性检查器 或 遥测参数速查抽屉
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(availableHeight));
            if (TelemetryParamDrawer.IsOpen)
            {
                TelemetryParamDrawer.Draw(availableHeight - 20f);
            }
            else if (_leftPanelMode == 2)
            {
                DrawModToolbarInspector(availableHeight);
            }
            else
            {
                WidgetConfig curWidget = null;
                if (widgets != null && !string.IsNullOrEmpty(_selectedWidgetId))
                {
                    curWidget = widgets.Find(x => x.WidgetId == _selectedWidgetId);
                }
                DrawRightInspector(curWidget, availableHeight);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        #region Left Panel (Hierarchy & Palette)

        private void DrawLeftPanel(List<WidgetConfig> widgets, float totalHeight)
        {
            MFPGuiSkin.BeginCard();

            // 1. 顶部模式切换分段按钮
            GUILayout.BeginHorizontal();
            bool isHier = _leftPanelMode == 0;
            bool isPal = _leftPanelMode == 1;
            bool isMod = _leftPanelMode == 2;
            int mountedCount = widgets != null ? widgets.Count : 0;
            string hierLabel = string.Format(I18n.Tr("STUDIO_TAB_HIERARCHY", "📋 挂载 ({0})"), mountedCount);
            string palLabel = I18n.Tr("STUDIO_TAB_PALETTE", "📦 库");

            int favCount = 0;
            var rules = ThemeManager.Instance?.DockRules;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++) if (rules[i].IsFavorite) favCount++;
            }
            string modLabel = string.Format(I18n.Tr("STUDIO_TAB_MOD_TOOLBAR", "🧰 MOD ({0})"), favCount);

            GUIStyle hierStyle = isHier ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
            GUIStyle palStyle = isPal ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
            GUIStyle modStyle = isMod ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;

            if (GUILayout.Button(hierLabel, hierStyle, GUILayout.Height(24f)))
            {
                _leftPanelMode = 0;
            }
            if (GUILayout.Button(palLabel, palStyle, GUILayout.Height(24f)))
            {
                _leftPanelMode = 1;
            }
            if (GUILayout.Button(modLabel, modStyle, GUILayout.Height(24f)))
            {
                _leftPanelMode = 2;
                EnsureDockRulesPopulated();
            }
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_QUICK_ARTBOARD", "🎨 +画板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(66f), GUILayout.Height(24f)))
            {
                CreateNewArtboard(false);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 2. 内部内容分发
            float innerContentHeight = Mathf.Max(120f, totalHeight - 65f);
            if (_leftPanelMode == 0)
            {
                DrawHierarchySubPanel(widgets, innerContentHeight);
            }
            else if (_leftPanelMode == 1)
            {
                DrawPaletteSubPanel(innerContentHeight);
            }
            else
            {
                DrawModToolbarSubPanel(innerContentHeight);
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawHierarchySubPanel(List<WidgetConfig> widgets, float availableHeight)
        {
            // 自由画板快捷入口
            GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><b>🎨 {I18n.Tr("STUDIO_HIER_ARTBOARD_TITLE", "自由航电搭建画板")}</b></color>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_NEW_ARTBOARD_SHORT", "✨ 新建"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(58f), GUILayout.Height(20f)))
            {
                CreateNewArtboard(false);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 搜索与过滤
            MFPGuiSkin.DrawSearchBar(ref _hierarchySearch, I18n.Tr("STUDIO_SEARCH_HIERARCHY", "过滤已挂载组件..."));

            GUILayout.Space(3f);

            // 状态过滤胶囊 + 批处理
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 3; i++)
            {
                bool isSel = (_hierarchyStatusFilter == i);
                GUIStyle btnStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                string filterLabel = i == 0 ? I18n.Tr("ASM_FILTER_ALL", "全部") :
                                     i == 1 ? I18n.Tr("MGR_FILTER_RUNNING", "运行中") : I18n.Tr("MGR_FILTER_SUSPENDED", "已挂起");
                if (GUILayout.Button(filterLabel, btnStyle, GUILayout.Height(20f), GUILayout.Width(50f)))
                {
                    _hierarchyStatusFilter = i;
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("✔", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                if (widgets != null)
                {
                    for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = true;
                    CommitPendingSaves();
                    ShowToast(I18n.Tr("MGR_TOAST_SHOW_ALL", "已全部启用显示！"));
                }
            }
            if (GUILayout.Button("○", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                if (widgets != null)
                {
                    for (int i = 0; i < widgets.Count; i++) widgets[i].IsEnabled = false;
                    CommitPendingSaves();
                    ShowToast(I18n.Tr("MGR_TOAST_HIDE_ALL", "已全部挂起隐藏！"));
                }
            }
            if (GUILayout.Button("🧲", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                if (widgets != null)
                {
                    for (int i = 0; i < widgets.Count; i++)
                    {
                        widgets[i].PositionX = Mathf.Round(widgets[i].PositionX / 10f) * 10f;
                        widgets[i].PositionY = Mathf.Round(widgets[i].PositionY / 10f) * 10f;
                    }
                    CommitPendingSaves();
                    ShowToast(I18n.Tr("MGR_TOAST_GRID_SNAP", "已完成全量组件网格对齐！"));
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 滚动列表
            float listHeight = Mathf.Max(80f, availableHeight - 65f);
            _hierarchyScroll = GUILayout.BeginScrollView(_hierarchyScroll, GUILayout.Height(listHeight));

            if (widgets == null || widgets.Count == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("STUDIO_EMPTY_HIERARCHY", "暂无挂载组件\n点击上方「📦 航电库」挑选组件")}</size></color>", GUI.skin.label);
            }
            else
            {
                string searchLower = (_hierarchySearch ?? "").Trim().ToLowerInvariant();
                for (int i = 0; i < widgets.Count; i++)
                {
                    var w = widgets[i];
                    if (w == null) continue;

                    // 状态过滤
                    if (_hierarchyStatusFilter == 1 && !w.IsEnabled) continue;
                    if (_hierarchyStatusFilter == 2 && w.IsEnabled) continue;

                    // 搜索过滤
                    if (!string.IsNullOrEmpty(searchLower))
                    {
                        string nameLower = (w.DisplayName ?? "").ToLowerInvariant();
                        string idLower = (w.WidgetId ?? "").ToLowerInvariant();
                        if (!nameLower.Contains(searchLower) && !idLower.Contains(searchLower)) continue;
                    }

                    bool isSelected = (w.WidgetId == _selectedWidgetId);
                    GUIStyle rowStyle = isSelected ? MFPGuiSkin.RowSelectedStyle : MFPGuiSkin.RowNormalStyle;

                    GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(28f));

                    // 显隐开关指示灯
                    string ledColor = w.IsEnabled ? MFPGuiSkin.HexAccentGreen : MFPGuiSkin.HexTextSecondary;
                    if (GUILayout.Button($"<color=#{ledColor}>●</color>", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                    {
                        w.IsEnabled = !w.IsEnabled;
                        MarkDirty();
                        FlightHUDManager.Instance?.RebuildHUD();
                    }

                    // 组件名称 (点击选中)
                    string dName = string.IsNullOrEmpty(w.DisplayName) ? w.WidgetId : w.DisplayName;
                    if (w.WidgetType == "composite_panel" || w.WidgetType == "custom_composite_panel")
                    {
                        dName = $"🎨 {dName}";
                    }
                    if (GUILayout.Button(dName, GUI.skin.label, GUILayout.ExpandWidth(true), GUILayout.Height(20f)))
                    {
                        SetSelectedWidget(w.WidgetId);
                    }

                    // 缩放读数徽章
                    GUILayout.Label($"<size=10><color=#{MFPGuiSkin.HexAccentCyan}>{w.Scale:F1}x</color></size>", GUILayout.Width(30f));

                    // 行内删除按钮
                    if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                    {
                        widgets.RemoveAt(i);
                        MarkDirty();
                        FlightHUDManager.Instance?.RebuildHUD();
                        EnsureSelectedWidget();
                        break;
                    }

                    GUILayout.EndHorizontal();
                    GUILayout.Space(2f);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawPaletteSubPanel(float availableHeight)
        {
            // ✨ 自由航电工坊创建入口 (对标 Photoshop 自由画布搭建)
            DrawFreeformPanelHeroCard();

            GUILayout.Space(4f);

            // 搜索栏
            MFPGuiSkin.DrawSearchBar(ref _paletteSearch, I18n.Tr("LIB_SEARCH_PLACEHOLDER", "搜索组件名称或标识..."));

            GUILayout.Space(3f);

            // 分类横条
            GUILayout.BeginHorizontal();
            string[] cats = new string[]
            {
                I18n.Tr("ASM_FILTER_ALL", "全"),
                I18n.Tr("ASM_FILTER_GAUGES", "表"),
                I18n.Tr("ASM_FILTER_NAV", "导"),
                I18n.Tr("ASM_FILTER_SYSTEMS", "系"),
                I18n.Tr("ASM_FILTER_SPACEX", "SPX"),
                I18n.Tr("ASM_FILTER_CONTROLS", "控")
            };
            for (int i = 0; i < cats.Length; i++)
            {
                bool isSel = (_paletteCategoryFilter == i);
                GUIStyle catStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(cats[i], catStyle, GUILayout.Height(20f)))
                {
                    _paletteCategoryFilter = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 预览图开关
            GUILayout.BeginHorizontal();
            string prevBtnText = _showPreviews ? I18n.Tr("LIB_TOGGLE_PREVIEW_ON", "🖼️ 预览: 开") : I18n.Tr("LIB_TOGGLE_PREVIEW_OFF", "🖼️ 预览: 关");
            if (GUILayout.Button(prevBtnText, MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
            {
                _showPreviews = !_showPreviews;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 组件卡片列表
            float listHeight = Mathf.Max(80f, availableHeight - 85f);
            _paletteScroll = GUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(listHeight));

            var descriptors = WidgetRegistry.AllDescriptors;
            string searchLower = (_paletteSearch ?? "").Trim().ToLowerInvariant();

            for (int i = 0; i < descriptors.Count; i++)
            {
                var desc = descriptors[i];
                if (desc == null) continue;

                // 常用 MOD 独立快捷坞已废弃并整合至编辑UI，隐藏不向用户展示添加入口
                if (desc.TypeName == "dock_favorites" || desc.DefaultWidgetId == "core.dock_favorites") continue;

                // 分类匹配
                if (!MatchesCategory(desc, _paletteCategoryFilter)) continue;

                // 搜索匹配
                if (!string.IsNullOrEmpty(searchLower))
                {
                    string idLower = (desc.TypeName ?? "").ToLowerInvariant();
                    string nameLower = (desc.DisplayName ?? "").ToLowerInvariant();
                    string descLower = (desc.Description ?? "").ToLowerInvariant();
                    if (!idLower.Contains(searchLower) && !nameLower.Contains(searchLower) && !descLower.Contains(searchLower))
                    {
                        continue;
                    }
                }

                GUILayout.BeginVertical(MFPGuiSkin.InsetStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{desc.DisplayName}</b>", GUILayout.ExpandWidth(true));
                if (GUILayout.Button(I18n.Tr("LIB_BTN_ADD_SHORT", "+ 添加"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(60f), GUILayout.Height(20f)))
                {
                    SpawnWidgetFromDescriptor(desc);
                }
                GUILayout.EndHorizontal();

                // 预览图
                if (_showPreviews)
                {
                    Texture2D tex = WidgetPreviewLoader.GetPreviewTexture(desc);
                    if (tex != null)
                    {
                        Rect previewRect = GUILayoutUtility.GetRect(260f, 65f);
                        GUI.DrawTexture(previewRect, tex, ScaleMode.ScaleToFit);
                    }
                }

                if (!string.IsNullOrEmpty(desc.Description))
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>{desc.Description}</size></color>");
                }

                GUILayout.EndVertical();
                GUILayout.Space(3f);
            }

            GUILayout.EndScrollView();
        }

        private static bool MatchesCategory(WidgetDescriptor desc, int catIndex)
        {
            if (catIndex == 0) return true;
            return (int)desc.Category == catIndex;
        }

        private void SpawnWidgetFromDescriptor(WidgetDescriptor desc)
        {
            var layout = WidgetLayoutManager.Instance.CurrentLayout;
            if (layout == null) return;

            string baseId = !string.IsNullOrEmpty(desc.DefaultWidgetId) ? desc.DefaultWidgetId : desc.TypeName;
            string newId = baseId;
            int counter = 1;
            while (layout.Widgets.Any(x => x.WidgetId == newId))
            {
                newId = $"{baseId}_{counter++}";
            }

            float px = Mathf.Round((Screen.width - desc.DefaultWidth) * 0.5f / 10f) * 10f;
            float py = Mathf.Round((Screen.height - desc.DefaultHeight) * 0.5f / 10f) * 10f;

            var w = new WidgetConfig(newId, desc.DisplayName, px, py)
            {
                WidgetType = desc.TypeName,
                Scale = 1.0f,
                Rotation = 0f,
                IsEnabled = true
            };

            layout.Widgets.Add(w);
            SetSelectedWidget(newId);
            _leftPanelMode = 0; // 自动切回已挂载层级，方便立即查看和调整
            CommitPendingSaves();
            ShowToast(string.Format(I18n.Tr("LIB_TOAST_ADDED", "已添加组件: {0}"), desc.DisplayName));
        }

        private void DrawFreeformPanelHeroCard()
        {
            GUILayout.BeginVertical(MFPGuiSkin.InsetStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🎨 <b>{I18n.Tr("STUDIO_FREEFORM_HERO_TITLE", "自由航电搭建画板")}</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_NEW_ARTBOARD", "✨ 新建画板"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(78f), GUILayout.Height(22f)))
            {
                SpawnCompositePanelWidget();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>{I18n.Tr("STUDIO_FREEFORM_HERO_DESC", "对标 PS 自由画布：图层透明度、8点拉伸、旋转与遍历 44+ 款组件构件")}</size></color>");
            GUILayout.EndVertical();
        }

        private void SpawnCompositePanelWidget()
        {
            CreateNewArtboard(false);
        }

        private void SpawnBlankCompositePanelWidget()
        {
            CreateNewArtboard(true);
        }

        private void DrawModToolbarSubPanel(float availableHeight)
        {
            // 搜索与过滤
            MFPGuiSkin.DrawSearchBar(ref _modToolbarSearch, I18n.Tr("STUDIO_SEARCH_MODS", "过滤模组名称或按键标识..."));

            GUILayout.Space(3f);

            // 状态过滤胶囊 + 批处理
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                bool isSel = (_modToolbarStatusFilter == i);
                GUIStyle btnStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                string filterLabel = i == 0 ? I18n.Tr("ASM_FILTER_ALL", "全部") :
                                     i == 1 ? I18n.Tr("THM_DOCK_ITEM_FAV", "★ 常用") :
                                     i == 2 ? I18n.Tr("MGR_BTN_SHOW", "显示") : I18n.Tr("MGR_BTN_HIDE", "隐藏");
                if (GUILayout.Button(filterLabel, btnStyle, GUILayout.Height(20f), GUILayout.Width(44f)))
                {
                    _modToolbarStatusFilter = i;
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.Tr("THM_DOCK_BTN_RECOMMEND", "★ 推荐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(48f), GUILayout.Height(20f)))
            {
                ThemeManager.Instance.AutoRecommendFavorites();
                CommitPendingSaves();
                ShowToast(I18n.Tr("STUDIO_TOAST_MOD_RECOMMENDED", "已自动推荐常用 MOD 快捷项！"));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 滚动列表
            float listHeight = Mathf.Max(80f, availableHeight - 65f);
            _modToolbarScroll = GUILayout.BeginScrollView(_modToolbarScroll, GUILayout.Height(listHeight));

            var rules = ThemeManager.Instance?.DockRules;
            if (rules == null || rules.Count == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("STUDIO_EMPTY_MODS", "未检测到工具栏模组\n点击上方「★ 推荐」初始化默认列表")}</size></color>", GUI.skin.label);
            }
            else
            {
                string searchLower = (_modToolbarSearch ?? "").Trim().ToLowerInvariant();
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (r == null) continue;

                    // 状态过滤: 0=All, 1=Fav, 2=Visible, 3=Hidden
                    if (_modToolbarStatusFilter == 1 && !r.IsFavorite) continue;
                    if (_modToolbarStatusFilter == 2 && !r.IsVisible) continue;
                    if (_modToolbarStatusFilter == 3 && r.IsVisible) continue;

                    // 搜索匹配
                    if (!string.IsNullOrEmpty(searchLower))
                    {
                        string nameLower = (r.DisplayName ?? "").ToLowerInvariant();
                        string keyLower = (r.Key ?? "").ToLowerInvariant();
                        if (!nameLower.Contains(searchLower) && !keyLower.Contains(searchLower))
                            continue;
                    }

                    bool isSelected = (_selectedDockRuleKey == r.Key);
                    GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle);

                    // 选中状态高亮与点击选择
                    string starPrefix = r.IsFavorite ? "<color=#FFD700>★ </color>" : "";
                    string eyePrefix = r.IsVisible ? "" : "<color=#888888>⊘ </color>";
                    string labelTxt = $"{starPrefix}{eyePrefix}{r.DisplayName}";
                    GUIStyle itemStyle = isSelected ? MFPGuiSkin.RowSelectedStyle : MFPGuiSkin.RowNormalStyle;

                    if (GUILayout.Button(labelTxt, itemStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22f)))
                    {
                        _selectedDockRuleKey = r.Key;
                    }

                    // 快速 ★ 常用切换
                    GUIStyle favStyle = r.IsFavorite ? MFPGuiSkin.WarningButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(r.IsFavorite ? "★" : "☆", favStyle, GUILayout.Width(24f), GUILayout.Height(22f)))
                    {
                        r.IsFavorite = !r.IsFavorite;
                        ThemeManager.Instance.SaveSettings();
                    }

                    // 快速 👁 显隐切换
                    GUIStyle visStyle = r.IsVisible ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
                    if (GUILayout.Button(r.IsVisible ? "👁" : "○", visStyle, GUILayout.Width(24f), GUILayout.Height(22f)))
                    {
                        r.IsVisible = !r.IsVisible;
                        ThemeManager.Instance.SaveSettings();
                    }

                    GUILayout.EndHorizontal();
                    GUILayout.Space(2f);
                }
            }

            GUILayout.EndScrollView();
        }

        #endregion

        #region Right Panel (Inspector)

        private void DrawRightInspector(WidgetConfig w, float availableHeight)
        {
            if (w == null)
            {
                DrawEmptyInspectorState(availableHeight);
                return;
            }

            if (w.WidgetType == "composite_panel" || w.WidgetType == "custom_composite_panel")
            {
                DrawCompositePanelStudio(w, availableHeight);
                return;
            }

            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll, GUILayout.Height(availableHeight));

            // 1. 顶部基础卡片 (名称、启用、实时求值、复制/删除)
            DrawInspectorHeaderCard(w);

            GUILayout.Space(5f);

            // 2. 几何变换与布局卡片 (坐标、对齐、缩放、旋转、图层)
            DrawTransformCard(w);

            GUILayout.Space(5f);

            // 3. 仪表量程与标定专属卡片 (针对 Dial / Tape / Bar)
            if (IsDialOrTapeWidget(w))
            {
                DrawDialOrTapeCard(w);
                GUILayout.Space(5f);
            }

            // 4. 遥测插槽与数据矩阵卡片 (针对自定义多通道、SpaceX等)
            if (IsMultiChannelWidget(w))
            {
                DrawMultiChannelCard(w);
                GUILayout.Space(5f);
            }

            // 5. 内部微控件 DSL 样式卡片
            DrawMicroControlsCard(w);

            GUILayout.Space(5f);

            // 6. 性能与刷新分频卡片
            DrawPerformanceCard(w);

            GUILayout.EndScrollView();
        }

        private void DrawModToolbarInspector(float availableHeight)
        {
            var rules = ThemeManager.Instance?.DockRules;
            if (rules == null || rules.Count == 0)
            {
                EnsureDockRulesPopulated();
                rules = ThemeManager.Instance?.DockRules;
            }

            DockButtonRule curRule = null;
            if (rules != null && rules.Count > 0)
            {
                if (!string.IsNullOrEmpty(_selectedDockRuleKey))
                {
                    curRule = rules.Find(x => x.Key.Equals(_selectedDockRuleKey, StringComparison.OrdinalIgnoreCase));
                }
                if (curRule == null)
                {
                    curRule = rules[0];
                    _selectedDockRuleKey = curRule.Key;
                }
            }

            _modInspectorScroll = GUILayout.BeginScrollView(_modInspectorScroll, GUILayout.Height(availableHeight));

            if (curRule != null)
            {
                // 卡片 1: 选中 MOD 属性与快捷设置
                MFPGuiSkin.BeginCard();
                GUILayout.Label($"🧰 <b>{I18n.Tr("STUDIO_MOD_INSPECT_TITLE", "MOD 快捷项调校")}</b> | <color=#{MFPGuiSkin.HexAccentCyan}>{curRule.DisplayName}</color>");
                GUILayout.Space(6f);

                // 原始标识与按键 ID
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Tr("STUDIO_MOD_KEY", "系统标识 (Key):"), GUILayout.Width(110f));
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}>{curRule.Key}</color>", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Tr("STUDIO_MOD_DEFAULT_NAME", "默认模组名称:"), GUILayout.Width(110f));
                GUILayout.Label($"<b>{curRule.DefaultName}</b>", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(6f);

                // 自定义显示名称
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Tr("STUDIO_MOD_CUSTOM_NAME", "自定义显示名称:"), GUILayout.Width(110f));
                string oldName = curRule.CustomLabel ?? "";
                string newName = GUILayout.TextField(oldName, GUILayout.ExpandWidth(true), GUILayout.Height(22f));
                if (newName != oldName)
                {
                    curRule.CustomLabel = newName;
                    _isDirty = true;
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(6f);

                // 常用开关与显隐开关
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Tr("STUDIO_MOD_FAV_TOGGLE", "★ 设为常用快捷项:"), GUILayout.Width(130f));
                GUIStyle favStyle = curRule.IsFavorite ? MFPGuiSkin.WarningButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
                string favTxt = curRule.IsFavorite ? I18n.Tr("STUDIO_STATUS_FAV_ON", "★ 已收藏 (顶部工具栏快速直达)") : I18n.Tr("STUDIO_STATUS_FAV_OFF", "☆ 普通 (未收藏)");
                if (GUILayout.Button(favTxt, favStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    curRule.IsFavorite = !curRule.IsFavorite;
                    _isDirty = true;
                    CommitPendingSaves();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Tr("STUDIO_MOD_VIS_TOGGLE", "👁 主收纳坞显示:"), GUILayout.Width(130f));
                GUIStyle visStyle = curRule.IsVisible ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
                string visTxt = curRule.IsVisible ? I18n.Tr("THM_STATUS_VISIBLE", "● 正常显示") : I18n.Tr("THM_STATUS_HIDDEN", "○ 已隐藏");
                if (GUILayout.Button(visTxt, visStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    curRule.IsVisible = !curRule.IsVisible;
                    _isDirty = true;
                    CommitPendingSaves();
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(8f);

                // 交互测试与即时启动
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Tr("STUDIO_MOD_BTN_TRIGGER", "▶ 测试启动 / 打开 MOD 窗口 (左键)"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                {
                    TriggerModAction(curRule, false);
                }
                if (GUILayout.Button(I18n.Tr("STUDIO_MOD_BTN_SETTINGS", "⚙️ 打开设置 (右键)"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(130f)))
                {
                    TriggerModAction(curRule, true);
                }
                GUILayout.EndHorizontal();

                MFPGuiSkin.EndCard();
                GUILayout.Space(6f);
            }

            // 卡片 2: 全局工具栏集成与批处理
            MFPGuiSkin.BeginCard();
            GUILayout.Label($"🧰 <b>{I18n.Tr("STUDIO_TOOLBAR_GLOBAL_TITLE", "全局工具栏集成设置")}</b>");
            GUILayout.Space(6f);

            // 模式单行分段
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("THM_TOOLBAR_MODE", "接管模式:"), GUILayout.Width(70f));
            for (int m = 0; m < 3; m++)
            {
                bool isCur = curTbMode == m;
                GUIStyle btnSt = isCur ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                string mTitle = m == 0 ? I18n.Tr("THM_TB_MODE_STOCK", "0 原版") :
                                m == 1 ? I18n.Tr("THM_TB_MODE_RESKIN", "1 黑晶重肤") : I18n.Tr("THM_TB_MODE_DOCK", "2 折叠收纳坞");
                if (GUILayout.Button(mTitle, btnSt, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    ThemeManager.Instance.ToolbarStyleMode = m;
                    ThemeManager.Instance.SaveSettings();
                    StockToolbarHook.ApplyStyleMode(m);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 快捷操作
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("THM_DOCK_BTN_RECOMMEND", "★ 智能推荐常用"), MFPGuiSkin.WarningButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.AutoRecommendFavorites();
                CommitPendingSaves();
                ShowToast(I18n.Tr("STUDIO_TOAST_MOD_RECOMMENDED", "已自动推荐常用 MOD 快捷项！"));
            }
            if (GUILayout.Button(I18n.Tr("THM_BTN_SHOW_ALL", "全显"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(60f)))
            {
                if (rules != null) { for (int i = 0; i < rules.Count; i++) rules[i].IsVisible = true; }
                ThemeManager.Instance.SaveSettings();
            }
            if (GUILayout.Button(I18n.Tr("THM_BTN_HIDE_ALL", "全隐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(24f), GUILayout.Width(60f)))
            {
                if (rules != null) { for (int i = 0; i < rules.Count; i++) rules[i].IsVisible = false; }
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();

            GUILayout.EndScrollView();
        }

        private void TriggerModAction(DockButtonRule rule, bool isRightClick)
        {
            if (rule == null) return;
#if KSP_RUNTIME
            try
            {
                var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                if (launcher != null)
                {
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    var all = new List<KSP.UI.Screens.ApplicationLauncherButton>();
                    if (stockBtns != null) all.AddRange(stockBtns);
                    if (modBtns != null) all.AddRange(modBtns);

                    for (int i = 0; i < all.Count; i++)
                    {
                        var btn = all[i];
                        if (btn == null) continue;
                        ModernToolbarWidget.GetButtonIdentity(btn, i, out string k, out string _);
                        if (k.Equals(rule.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            ModernToolbarWidget.TriggerKspButtonClick(btn, null, isRightClick);
                            ShowToast(string.Format(I18n.Tr("UI_TOAST_MOD_TRIGGERED", "✔ 已触发 [{0}]"), rule.DisplayName));
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("TriggerModAction", $"Failed triggering mod button: {ex.Message}");
            }
#endif
            ShowToast(string.Format(I18n.Tr("UI_TOAST_MOD_TRIGGERED", "✔ 已触发 [{0}] (模拟)"), rule.DisplayName));
        }

        private void DrawEmptyInspectorState(float availableHeight)
        {
            MFPGuiSkin.BeginCard(GUILayout.Height(availableHeight - 10f));
            GUILayout.Space(12f);

            // 🎨 自由航电搭建工坊 Hero Card (置顶醒目引导)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><size=13><b>🎨 {I18n.Tr("STUDIO_HERO_FREEFORM_BANNER", "自由航电搭建工坊 (PS 自由画板)")}</b></size></color>");
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("STUDIO_HERO_FREEFORM_SUB", "自由添加微控件图层、0%~100% 透明度调节、8 点变换手柄缩放旋转，遍历 44+ 款组件构件")}</size></color>");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_NEW_ARTBOARD_HERO", "✨ 立即创建自由画板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(30f), GUILayout.Width(150f)))
            {
                CreateNewArtboard(false);
            }
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_NEW_BLANK_HERO", "📄 空白画板"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(30f), GUILayout.Width(90f)))
            {
                CreateNewArtboard(true);
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(20f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><size=14><b>{I18n.Tr("STUDIO_EMPTY_INSPECTOR_TITLE", "🛠️ 未选中航电组件")}</b></size></color>", GUI.skin.label);
            GUILayout.Space(8f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("STUDIO_EMPTY_INSPECTOR_DESC", "请在左侧列表中点击选择要配置的组件，或点击下方按钮快速生成开箱即用航电模板：")}</size></color>");
            GUILayout.Space(14f);

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
        }

        private void SpawnQuickWidget(string typeName, string defaultId, string defaultName)
        {
            var layout = WidgetLayoutManager.Instance.CurrentLayout;
            if (layout == null) return;

            string id = defaultId;
            int c = 1;
            while (layout.Widgets.Any(x => x.WidgetId == id))
            {
                id = $"{defaultId}_{c++}";
            }

            var w = new WidgetConfig(id, defaultName, Screen.width * 0.5f - 100f, Screen.height * 0.5f - 50f)
            {
                WidgetType = typeName,
                Scale = 1.0f,
                IsEnabled = true
            };
            layout.Widgets.Add(w);
            SetSelectedWidget(id);
            _leftPanelMode = 0;
            CommitPendingSaves();
            ShowToast(string.Format(I18n.Tr("LIB_TOAST_ADDED", "已添加组件: {0}"), defaultName));
        }

        private void DrawInspectorHeaderCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();

            GUILayout.BeginHorizontal();
            // 启用开关
            bool prevEnabled = w.IsEnabled;
            w.IsEnabled = GUILayout.Toggle(w.IsEnabled, "", GUILayout.Width(20f));
            if (w.IsEnabled != prevEnabled)
            {
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            // 名称编辑
            string newName = MFPGuiSkin.DrawBufferedStringField($"txt_name_{w.WidgetId}", w.DisplayName, 200f);
            if (newName != w.DisplayName)
            {
                w.DisplayName = newName;
                MarkDirty();
            }

            // 类型徽章
            MFPGuiSkin.DrawBadge(w.WidgetType ?? "widget", Color.white, new Color(0.12f, 0.22f, 0.35f, 0.9f), 100f);

            GUILayout.FlexibleSpace();

            // 复制组件
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_DUPLICATE", "⎘ 复制"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f), GUILayout.Width(60f)))
            {
                DuplicateWidget(w);
            }

            // 重置变换
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_RESET_XFORM", "⟲ 重置变换"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f), GUILayout.Width(80f)))
            {
                w.Scale = 1.0f;
                w.Rotation = 0f;
                ApplyTransformRuntime(w);
                MarkDirty();
                ShowToast(I18n.Tr("UI_TOAST_RESET_WINDOW", "已重置组件几何变换"));
            }

            // 删除组件
            if (GUILayout.Button(I18n.Tr("STUDIO_BTN_DELETE", "🗑 删除"), MFPGuiSkin.DangerButtonStyle, GUILayout.Height(22f), GUILayout.Width(60f)))
            {
                var list = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
                if (list != null)
                {
                    list.Remove(w);
                    CommitPendingSaves();
                    EnsureSelectedWidget();
                    return;
                }
            }
            GUILayout.EndHorizontal();

            // 实时求值呈现框
            string evalTarget = !string.IsNullOrEmpty(w.CustomTemplate) ? w.CustomTemplate : w.NumericToken;
            if (!string.IsNullOrEmpty(evalTarget))
            {
                GUILayout.Space(4f);
                DrawLiveEvaluationBox(evalTarget);
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawTransformCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_TRANSFORM", "📐 几何变换与屏幕布局"));

            // 坐标 X / Y
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("X:", GUILayout.Width(20f));
            float newX = MFPGuiSkin.DrawBufferedFloatField($"tf_posx_{w.WidgetId}", w.PositionX, 65f);
            if (Math.Abs(newX - w.PositionX) > 0.01f) { w.PositionX = newX; ApplyTransformRuntime(w); MarkDirty(); }

            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX -= 10f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX += 10f; ApplyTransformRuntime(w); MarkDirty(); }

            GUILayout.Space(12f);

            GUILayout.Label("Y:", GUILayout.Width(20f));
            float newY = MFPGuiSkin.DrawBufferedFloatField($"tf_posy_{w.WidgetId}", w.PositionY, 65f);
            if (Math.Abs(newY - w.PositionY) > 0.01f) { w.PositionY = newY; ApplyTransformRuntime(w); MarkDirty(); }

            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY -= 10f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY += 10f; ApplyTransformRuntime(w); MarkDirty(); }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.Tr("ASM_BTN_CENTER_X", "居中X"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(55f), GUILayout.Height(22f)))
            {
                w.PositionX = Mathf.Round((Screen.width - 200f * w.Scale) * 0.5f / 10f) * 10f;
                ApplyTransformRuntime(w); MarkDirty();
            }
            if (GUILayout.Button(I18n.Tr("ASM_BTN_CENTER_Y", "居中Y"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(55f), GUILayout.Height(22f)))
            {
                w.PositionY = Mathf.Round((Screen.height - 120f * w.Scale) * 0.5f / 10f) * 10f;
                ApplyTransformRuntime(w); MarkDirty();
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 缩放 Scale
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_SCALE_LABEL", "缩放:"), GUILayout.Width(45f));
            float newScale = GUILayout.HorizontalSlider(w.Scale, 0.4f, 2.5f, GUILayout.Width(130f));
            if (Math.Abs(newScale - w.Scale) > 0.005f) { w.Scale = Mathf.Round(newScale * 20f) / 20f; ApplyTransformRuntime(w); MarkDirty(); }

            GUILayout.Label($"<b><color=#{MFPGuiSkin.HexAccentCyan}>{w.Scale:F2}x</color></b>", GUILayout.Width(45f));

            if (GUILayout.Button("0.8x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 0.8f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("1.0x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.0f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("1.2x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.2f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("1.5x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.5f; ApplyTransformRuntime(w); MarkDirty(); }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 旋转 Rotation
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_ROT_LABEL", "旋转:"), GUILayout.Width(45f));
            float newRot = GUILayout.HorizontalSlider(w.Rotation, 0f, 360f, GUILayout.Width(130f));
            if (Math.Abs(newRot - w.Rotation) > 0.5f) { w.Rotation = Mathf.Round(newRot / 5f) * 5f; ApplyTransformRuntime(w); MarkDirty(); }

            GUILayout.Label($"<b><color=#{MFPGuiSkin.HexAccentAmber}>{w.Rotation:F0}°</color></b>", GUILayout.Width(45f));

            if (GUILayout.Button("0°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f))) { w.Rotation = 0f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("90°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f))) { w.Rotation = 90f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("180°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(42f))) { w.Rotation = 180f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("↺ 15°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(44f))) { w.Rotation = (w.Rotation - 15f + 360f) % 360f; ApplyTransformRuntime(w); MarkDirty(); }
            if (GUILayout.Button("↻ 15°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(44f))) { w.Rotation = (w.Rotation + 15f) % 360f; ApplyTransformRuntime(w); MarkDirty(); }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 图层层级
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("STUDIO_LAYER_ORDER", "图层深度:"), GUILayout.Width(65f));
            if (GUILayout.Button("⤒ " + I18n.Tr("STUDIO_BRING_FRONT", "置顶"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { MoveWidgetZOrder(w, -2); }
            if (GUILayout.Button("▲ " + I18n.Tr("STUDIO_MOVE_UP", "上移"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { MoveWidgetZOrder(w, -1); }
            if (GUILayout.Button("▼ " + I18n.Tr("STUDIO_MOVE_DOWN", "下移"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { MoveWidgetZOrder(w, 1); }
            if (GUILayout.Button("⤓ " + I18n.Tr("STUDIO_SEND_BACK", "置底"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { MoveWidgetZOrder(w, 2); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private void DrawDialOrTapeCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CALIBRATION", "📊 仪表数据驱动与量程标定"));

            // 主遥测驱动源
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_TOKEN_NUMERIC", "主驱动数据源:"), GUILayout.Width(95f));

            string newTok = MFPGuiSkin.DrawBufferedStringField($"txt_numtok_{w.WidgetId}", w.NumericToken ?? "", 160f);
            if (newTok != w.NumericToken)
            {
                w.NumericToken = newTok;
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            if (GUILayout.Button(I18n.Tr("ASM_BTN_PICK_PARAM", "🔍 选参数"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                TelemetryParamDrawer.Open(w.DisplayName + I18n.Tr("ASM_PARAM_MAIN_SRC", " 主驱动源"), chosen =>
                {
                    w.NumericToken = chosen;
                    var meta = TelemetryCatalog.FindByToken(chosen);
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

            string liveSample = GetSampledTokenValue(w.NumericToken);
            GUILayout.Space(6f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{liveSample}</b></color> <size=9>{w.UnitLabel}</size>", GUILayout.Width(90f));

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 量程参数
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_PROP_MIN", "最小值:"), GUILayout.Width(50f));
            w.MinValue = MFPGuiSkin.DrawBufferedFloatField($"prop_min_{w.WidgetId}", w.MinValue, 60f);

            GUILayout.Space(8f);
            GUILayout.Label(I18n.Tr("ASM_PROP_MAX", "最大值:"), GUILayout.Width(50f));
            w.MaxValue = MFPGuiSkin.DrawBufferedFloatField($"prop_max_{w.WidgetId}", w.MaxValue, 60f);

            GUILayout.Space(8f);
            GUILayout.Label(I18n.Tr("ASM_PROP_CAUTION", "警示阈值:"), GUILayout.Width(60f));
            w.CautionThreshold = MFPGuiSkin.DrawBufferedFloatField($"prop_caut_{w.WidgetId}", w.CautionThreshold, 60f);

            GUILayout.Space(8f);
            GUILayout.Label(I18n.Tr("ASM_PROP_WARNING", "危险阈值:"), GUILayout.Width(60f));
            w.WarningThreshold = MFPGuiSkin.DrawBufferedFloatField($"prop_warn_{w.WidgetId}", w.WarningThreshold, 60f);
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_PROP_STEP", "刻度步长:"), GUILayout.Width(60f));
            w.StepInterval = MFPGuiSkin.DrawBufferedFloatField($"prop_step_{w.WidgetId}", w.StepInterval, 55f);

            GUILayout.Space(8f);
            GUILayout.Label(I18n.Tr("ASM_PROP_UNIT", "单位标签:"), GUILayout.Width(60f));
            w.UnitLabel = MFPGuiSkin.DrawBufferedStringField($"prop_unit_{w.WidgetId}", w.UnitLabel ?? "", 60f);

            GUILayout.Space(8f);
            w.IsSoftLimit = GUILayout.Toggle(w.IsSoftLimit, I18n.Tr("ASM_PROP_SOFT_LIMIT", " 柔性超限"));
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private void DrawMultiChannelCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();

            GUILayout.BeginHorizontal();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CHANNELS_MJ", "🎛️ 遥测多通道插槽配置"));

            string modeBtnLabel = _showRawTemplate ? I18n.Tr("ASM_MODE_VISUAL", "🎛️ 可视化矩阵模式") : I18n.Tr("ASM_MODE_RAW", "📝 原始模板源码");
            if (GUILayout.Button(modeBtnLabel, MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(130f), GUILayout.Height(22f)))
            {
                _showRawTemplate = !_showRawTemplate;
            }
            GUILayout.EndHorizontal();

            if (_showRawTemplate)
            {
                MFPGuiSkin.BeginInset();
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>{I18n.Tr("ASM_RAW_TPL_DESC", "直接编辑结构化通道模板 (格式: CH1={TOKEN};)：")}</size></color>");
                string newTpl = GUILayout.TextArea(w.CustomTemplate ?? "", GUILayout.Height(65f));
                if (newTpl != w.CustomTemplate)
                {
                    w.CustomTemplate = newTpl;
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
                MFPGuiSkin.EndInset();
            }
            else
            {
                var matrixData = TelemetryMatrixData.FromTemplate(w.CustomTemplate);

                // 常用预设快捷按钮
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b><size=10><color=#{MFPGuiSkin.HexAccentCyan}>{I18n.Tr("ASM_QUICK_PRESET_TITLE", "常用预设:")}</color></size></b>", GUILayout.Width(70f));
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_2COL", "🚀 经典 2 列 (6 通道)"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateDefaultKeyValue().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.Tr("ASM_TOAST_APPLIED_2COL", "✔ 已应用 2 列遥测矩阵"));
                }
                if (GUILayout.Button(I18n.Tr("ASM_PRESET_MJ_1COL", "📋 单列监控"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f)))
                {
                    w.CustomTemplate = TelemetryMatrixData.CreateSingleColumnMonitor().ToTemplate();
                    MarkDirty();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.Tr("ASM_TOAST_APPLIED_1COL", "✔ 已应用单列监控卡"));
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();

                GUILayout.Space(3f);

                // 插槽清单
                var channels = ParseChannelsFromTemplate(w.CustomTemplate);
                string[] defaultSlots = new string[] { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6" };

                for (int i = 0; i < defaultSlots.Length; i++)
                {
                    string slotKey = defaultSlots[i];
                    channels.TryGetValue(slotKey, out string currentToken);
                    currentToken = currentToken ?? "";

                    MFPGuiSkin.BeginInset();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{slotKey}:</b>", GUILayout.Width(45f));

                    string editedToken = MFPGuiSkin.DrawBufferedStringField($"slot_{w.WidgetId}_{slotKey}", currentToken, 140f);
                    if (editedToken != currentToken)
                    {
                        SetChannelInTemplate(w, slotKey, editedToken);
                    }

                    if (GUILayout.Button(I18n.Tr("ASM_BTN_PICK_PARAM", "🔍 选参数"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(70f), GUILayout.Height(20f)))
                    {
                        string captureKey = slotKey;
                        TelemetryParamDrawer.Open($"{w.DisplayName} [{captureKey}]", chosen =>
                        {
                            SetChannelInTemplate(w, captureKey, chosen);
                        });
                    }

                    string liveVal = GetSampledTokenValue(currentToken);
                    GUILayout.Space(6f);
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{liveVal}</b></color>", GUILayout.Width(100f));

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(currentToken))
                    {
                        if (GUILayout.Button("✕", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
                        {
                            SetChannelInTemplate(w, slotKey, "");
                        }
                    }
                    GUILayout.EndHorizontal();
                    MFPGuiSkin.EndInset();
                    GUILayout.Space(2f);
                }
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawMicroControlsCard(WidgetConfig w)
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
                I18n.Tr("ASM_CARD_MICRO_CONTROLS", "⚙️ 组件微控件配置与显隐排版"),
                string.Format(I18n.Tr("ASM_MICRO_COUNT", "已纳管 {0} 个微控件"), ctrlList.Count)
            );

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
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            _microControlsScroll = GUILayout.BeginScrollView(_microControlsScroll, GUILayout.Height(140f));
            for (int i = 0; i < ctrlList.Count; i++)
            {
                var ctrl = ctrlList[i];
                if (ctrl == null) continue;

                GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(24f));
                string led = ctrl.IsVisible ? $"<color=#00FF88>{I18n.Tr("ASM_CTRL_VISIBLE", "● 显")}</color>" : $"<color=#7088A8>{I18n.Tr("ASM_CTRL_HIDDEN", "○ 隐")}</color>";
                if (GUILayout.Button(led, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(20f)))
                {
                    runtime.Controls.SetControlVisibility(ctrl.Id, !ctrl.IsVisible);
                    MarkDirty();
                    WidgetLayoutManager.Instance.SaveLayout();
                }

                GUILayout.Label($"<b>{ctrl.DisplayName}</b> <size=9><color=#{MFPGuiSkin.HexTextSecondary}>[{ctrl.Category}]</color></size>", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }
            GUILayout.EndScrollView();

            MFPGuiSkin.EndCard();
        }

        private void DrawPerformanceCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(
                I18n.Tr("ASM_CARD_PERF", "⚡ 绘制性能与双轨刷新率调优"),
                I18n.Tr("ASM_CARD_PERF_SUB", "显示循环(UIDrawLoop)与数据心跳(DataHeartBeat)独立分频")
            );

            bool prevIsolate = w.IsolateCanvas;
            w.IsolateCanvas = GUILayout.Toggle(w.IsolateCanvas, I18n.Tr("ASM_PROP_ISOLATE_CANVAS", " 启用独立画布"));
            if (w.IsolateCanvas != prevIsolate)
            {
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
                ShowToast(w.IsolateCanvas ? I18n.Tr("ASM_CANVAS_ENABLED", "已开启画布隔离") : I18n.Tr("ASM_CANVAS_DISABLED", "已关闭画布隔离"));
            }

            GUILayout.Space(4f);

            // 1. 显示刷新率 (UIDrawLoop)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_PERF_DRAWLOOP_TITLE", "显示刷新率 (UIDrawLoop):"), GUILayout.Width(170f));
            string drawLabel;
            if (w.CustomHz > 0.001f)
            {
                drawLabel = string.Format("{0:F1} Hz", w.CustomHz);
            }
            else if (w.UpdateInterval > 0.0001f)
            {
                float hz = 1.0f / w.UpdateInterval;
                drawLabel = string.Format("{0:F0} Hz ({1:F2}s)", hz, w.UpdateInterval);
            }
            else
            {
                drawLabel = I18n.Tr("ASM_HZ_60_PLUS", "60Hz+ (每帧 / VSync)");
            }
            GUILayout.Label(string.Format("<b><color=#{0}>{1}</color></b>", MFPGuiSkin.HexAccentCyan, drawLabel), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("ASM_PERF_AUTO_DEFAULT", "默认阶梯"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 0f; w.UpdateInterval = 0f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_60HZ", "60Hz 满血"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 60f; w.UpdateInterval = 1f / 60f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_30HZ", "30Hz 标称"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 30f; w.UpdateInterval = 1f / 30f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_20HZ", "20Hz 常用"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 20f; w.UpdateInterval = 0.05f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_10HZ", "10Hz 舒缓"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 10f; w.UpdateInterval = 0.10f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_2HZ", "2Hz 节能"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.CustomHz = 2f; w.UpdateInterval = 0.50f; MarkDirty(); }
            GUILayout.EndHorizontal();

            // 滑块微调显示 Hz (1.0Hz ~ 120.0Hz)
            float curDrawHz = w.CustomHz > 0.001f ? w.CustomHz : (w.UpdateInterval > 0.0001f ? (1f / w.UpdateInterval) : 60f);
            float newDrawHz = GUILayout.HorizontalSlider(curDrawHz, 1.0f, 120.0f);
            if (Math.Abs(newDrawHz - curDrawHz) > 0.4f)
            {
                w.CustomHz = Mathf.Round(newDrawHz * 10f) / 10f;
                w.UpdateInterval = 1f / Mathf.Max(0.1f, w.CustomHz);
                MarkDirty();
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 数据刷新率 (DataHeartBeat)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_PERF_HEARTBEAT_TITLE", "数据心跳率 (DataHeartBeat):"), GUILayout.Width(170f));
            string hbLabel;
            if (w.HeartBeatInterval < 0f && w.HeartBeatHz <= 0.001f)
            {
                hbLabel = I18n.Tr("DIAG_HB_AUTO", "各组件默认");
            }
            else if (w.HeartBeatInterval == 0f || (w.HeartBeatHz <= 0.001f && w.HeartBeatInterval < 0.001f))
            {
                hbLabel = I18n.Tr("ASM_PERF_SYNC_DRAW", "同步显示");
            }
            else if (w.HeartBeatHz > 0.001f)
            {
                hbLabel = string.Format("{0:F1} Hz", w.HeartBeatHz);
            }
            else
            {
                float hz = 1.0f / w.HeartBeatInterval;
                hbLabel = string.Format("{0:F0} Hz ({1:F2}s)", hz, w.HeartBeatInterval);
            }
            GUILayout.Label(string.Format("<b><color=#{0}>{1}</color></b>", MFPGuiSkin.HexAccentAmber, hbLabel), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("ASM_PERF_SYNC_DRAW", "同步显示"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatInterval = 0f; w.HeartBeatHz = 0f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_AUTO_DEFAULT", "默认节拍"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatInterval = -1f; w.HeartBeatHz = 0f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_20HZ", "20Hz 高频"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatHz = 20f; w.HeartBeatInterval = 0.05f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_10HZ", "10Hz 推荐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatHz = 10f; w.HeartBeatInterval = 0.10f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_5HZ", "5Hz 舒缓"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatHz = 5f; w.HeartBeatInterval = 0.20f; MarkDirty(); }
            if (GUILayout.Button(I18n.Tr("ASM_PERF_2HZ", "2Hz 节能"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f))) { w.HeartBeatHz = 2f; w.HeartBeatInterval = 0.50f; MarkDirty(); }
            GUILayout.EndHorizontal();

            // 滑块微调心跳 Hz (1.0Hz ~ 60.0Hz)
            float curHbHz = w.HeartBeatHz > 0.001f ? w.HeartBeatHz : (w.HeartBeatInterval > 0.0001f ? (1f / w.HeartBeatInterval) : 10f);
            float newHbHz = GUILayout.HorizontalSlider(curHbHz, 1.0f, 60.0f);
            if (Math.Abs(newHbHz - curHbHz) > 0.4f)
            {
                w.HeartBeatHz = Mathf.Round(newHbHz * 10f) / 10f;
                w.HeartBeatInterval = 1f / Mathf.Max(0.1f, w.HeartBeatHz);
                MarkDirty();
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 3. 双轨状态比对与防倒挂提示 (Cadence Insight)
            MFPGuiSkin.BeginInset();
            float effDrawHz = w.CustomHz > 0.001f ? w.CustomHz : (w.UpdateInterval > 0.0001f ? (1f / w.UpdateInterval) : 60f);
            float effHbHz;
            if (w.HeartBeatInterval == 0f || (w.HeartBeatHz <= 0.001f && w.HeartBeatInterval < 0.001f))
            {
                effHbHz = effDrawHz;
            }
            else if (w.HeartBeatHz > 0.001f)
            {
                effHbHz = w.HeartBeatHz;
            }
            else if (w.HeartBeatInterval > 0.0001f)
            {
                effHbHz = 1f / w.HeartBeatInterval;
            }
            else
            {
                effHbHz = 10f;
            }

            string ratioDesc;
            if (effHbHz >= effDrawHz - 0.1f)
            {
                ratioDesc = I18n.Tr("ASM_PERF_RATIO_SYNC", "1:1 同频解算");
            }
            else
            {
                int ratio = Mathf.RoundToInt(effDrawHz / Mathf.Max(0.1f, effHbHz));
                ratioDesc = string.Format(I18n.Tr("ASM_PERF_RATIO_INTERP", "{0}:1 分频插值"), ratio);
            }

            GUILayout.Label(string.Format("<color=#{0}><size=11>• {1}</size></color>", MFPGuiSkin.HexTextSecondary, string.Format(I18n.Tr("ASM_PERF_CADENCE_DESC", "物理解算: {0} | 画面渲染: {1} ({2})"), string.Format("{0:F0}Hz", effHbHz), string.Format("{0:F0}Hz", effDrawHz), ratioDesc)));
            if (effHbHz > effDrawHz + 0.1f)
            {
                GUILayout.Label(string.Format("<color=#FFA502><size=10>⚠ {0}: {1}</size></color>", I18n.Tr("ASM_PERF_RATIO_CLAMPED", "SPEC-002B 防倒挂钳位"), I18n.Tr("ASM_PERF_CLAMP_DESC", "数据心跳率高于显示刷新率时将自动钳平至显示刷新率运行。")));
            }
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private void DrawLiveEvaluationBox(string template)
        {
            if (Time.unscaledTime - _lastEvalTime > 0.25f || _cachedTemplate != template)
            {
                _lastEvalTime = Time.unscaledTime;
                _cachedTemplate = template;
                try
                {
                    _cachedEvaluation = TelemetryTokenEngine.Evaluate(template, TelemetryHub.Instance);
                    if (string.IsNullOrEmpty(_cachedEvaluation)) _cachedEvaluation = I18n.Tr("ASM_TPL_EMPTY", "<空模板>");
                }
                catch (Exception ex)
                {
                    _cachedEvaluation = I18n.TrFormat("ASM_TPL_SYNTAX_ERR", ex.Message);
                }
            }

            MFPGuiSkin.BeginInset();
            GUILayout.Label(string.Format(I18n.Tr("ASM_EVAL_PREVIEW", "实时数据流 (4Hz): <color=#{0}><b>{1}</b></color>"), MFPGuiSkin.HexAccentGreen, _cachedEvaluation));
            MFPGuiSkin.EndInset();
        }

        #endregion

        #region Helpers & State Operations

        private void EnsureSelectedWidget()
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null || widgets.Count == 0)
            {
                _selectedWidgetId = null;
                return;
            }

            // 优先遵循 HUD 现场的主动选中
            var firstSel = WidgetSelectionManager.SelectedWidgets.FirstOrDefault();
            if (firstSel?.Config != null)
            {
                string hudSelId = firstSel.Config.WidgetId;
                if (widgets.Any(x => x.WidgetId == hudSelId))
                {
                    _selectedWidgetId = hudSelId;
                    return;
                }
            }

            if (string.IsNullOrEmpty(_selectedWidgetId) || !widgets.Any(x => x.WidgetId == _selectedWidgetId))
            {
                _selectedWidgetId = widgets[0].WidgetId;
            }
        }

        private void DuplicateWidget(WidgetConfig source)
        {
            var layout = WidgetLayoutManager.Instance.CurrentLayout;
            if (layout == null || source == null) return;

            string newId = $"{source.WidgetId}_copy";
            int c = 1;
            while (layout.Widgets.Any(x => x.WidgetId == newId))
            {
                newId = $"{source.WidgetId}_copy{c++}";
            }

            var copy = new WidgetConfig(newId, $"{source.DisplayName} (Copy)", source.PositionX + 20f, source.PositionY + 20f)
            {
                WidgetType = source.WidgetType,
                Scale = source.Scale,
                Rotation = source.Rotation,
                CustomTemplate = source.CustomTemplate,
                NumericToken = source.NumericToken,
                MinValue = source.MinValue,
                MaxValue = source.MaxValue,
                CautionThreshold = source.CautionThreshold,
                WarningThreshold = source.WarningThreshold,
                UnitLabel = source.UnitLabel,
                StepInterval = source.StepInterval,
                IsSoftLimit = source.IsSoftLimit,
                IsEnabled = source.IsEnabled,
                IsolateCanvas = source.IsolateCanvas,
                UpdateInterval = source.UpdateInterval
            };

            layout.Widgets.Add(copy);
            SetSelectedWidget(newId);
            CommitPendingSaves();
            ShowToast(string.Format(I18n.Tr("STUDIO_TOAST_DUPLICATED", "已复制组件: {0}"), copy.DisplayName));
        }

        private void MoveWidgetZOrder(WidgetConfig w, int delta)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;
            int idx = widgets.IndexOf(w);
            if (idx < 0) return;

            widgets.RemoveAt(idx);
            int newIdx = idx;
            if (delta == -2) newIdx = 0; // 置顶
            else if (delta == 2) newIdx = widgets.Count; // 置底
            else newIdx = Mathf.Clamp(idx + delta, 0, widgets.Count);

            widgets.Insert(newIdx, w);
            MarkDirty();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        private static bool IsDialOrTapeWidget(WidgetConfig w)
        {
            if (w == null) return false;
            string t = (w.WidgetType ?? "").ToLowerInvariant();
            return t.Contains("dial") || t.Contains("tape") || t.Contains("arc") || t.Contains("meter") || t.Contains("gauge");
        }

        private static bool IsMultiChannelWidget(WidgetConfig w)
        {
            if (w == null) return false;
            string t = (w.WidgetType ?? "").ToLowerInvariant();
            return t.Contains("custom_token") || t.Contains("matrix") || t.Contains("card") || t.StartsWith("spacex.");
        }

        private static Dictionary<string, string> ParseChannelsFromTemplate(string template)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return dict;

            string[] tokens = template.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                int eq = tok.IndexOf('=');
                if (eq > 0)
                {
                    string key = tok.Substring(0, eq).Trim();
                    string val = tok.Substring(eq + 1).Trim();
                    dict[key] = val;
                }
            }
            return dict;
        }

        private void SetChannelInTemplate(WidgetConfig w, string channelKey, string token)
        {
            var dict = ParseChannelsFromTemplate(w.CustomTemplate);
            if (string.IsNullOrEmpty(token)) dict.Remove(channelKey);
            else dict[channelKey] = token;

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
            try
            {
                return TelemetryTokenEngine.Evaluate(token, TelemetryHub.Instance);
            }
            catch
            {
                return "---";
            }
        }

        private void ApplyTransformRuntime(WidgetConfig w)
        {
            if (FlightHUDManager.Instance == null || FlightHUDManager.Instance.ModularWidgets == null) return;
            var runtime = FlightHUDManager.Instance.ModularWidgets.Find(x => x.WidgetId == w.WidgetId);
            if (runtime != null)
            {
                runtime.UpdateTransform(w.PositionX, w.PositionY, w.Scale, w.Rotation);
            }
        }

        private void MarkDirty()
        {
            _isDirty = true;
            _dirtyTimer = 0f;
        }

        public void CommitPendingSaves()
        {
            _isDirty = false;
            _dirtyTimer = 0f;
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        private void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.0f;
        }

        #region Freeform Avionics Studio (PS-Grade Freeform Panel Workbench)

        private void DrawCompositePanelStudio(WidgetConfig w, float availableHeight)
        {
            var panelCfg = CompositePanelConfig.FromJson(w.CustomTemplate);
            if (panelCfg == null)
            {
                panelCfg = CompositePanelConfig.CreateDefaultDemoPanel();
                SyncCompositeConfig(w, panelCfg);
            }

            // 确保有有效选中图层
            if (panelCfg.Elements != null && panelCfg.Elements.Count > 0)
            {
                if (string.IsNullOrEmpty(_selectedLayerId) || !panelCfg.Elements.Any(e => e.LayerId == _selectedLayerId))
                {
                    _selectedLayerId = panelCfg.Elements[0].LayerId;
                }
            }
            else
            {
                _selectedLayerId = null;
            }

            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll, GUILayout.Height(availableHeight));

            // 1. 顶部工坊状态卡片
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🎨 <b>{I18n.Tr("STUDIO_FREEFORM_TITLE", "自由航电工坊 (PS 自由编辑模式)")}</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(w.IsEnabled ? "● 运行中" : "○ 已挂起", w.IsEnabled ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(20f)))
            {
                w.IsEnabled = !w.IsEnabled;
                MarkDirty();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>画板: <b>{w.DisplayName}</b> (ID: {w.WidgetId}) | 基准分辨率: <b>{panelCfg.BaseWidth:0} × {panelCfg.BaseHeight:0}</b> | 图层: <b>{panelCfg.Elements.Count}</b> 个 | 透明度: <b>{(int)(panelCfg.PanelOpacity * 100)}%</b></size></color>");

            GUILayout.Space(5f);

            // 模式分段切片
            GUILayout.BeginHorizontal();
            string[] subTabs = new string[]
            {
                string.Format(I18n.Tr("STUDIO_SUBTAB_LAYERS", "📑 图层与精调 ({0})"), panelCfg.Elements.Count),
                I18n.Tr("STUDIO_SUBTAB_TOOLBOX", "📦 构件素材箱 (44+款组件)"),
                I18n.Tr("STUDIO_SUBTAB_CANVAS", "⚙️ 画板与预设")
            };
            for (int i = 0; i < subTabs.Length; i++)
            {
                bool isSel = _studioSubTab == i;
                GUIStyle tabStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(subTabs[i], tabStyle, GUILayout.Height(24f)))
                {
                    _studioSubTab = i;
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(5f);

            // 2. 根据 SubTab 绘制主体内容
            if (_studioSubTab == 0)
            {
                DrawCompositeLayersAndInspector(w, panelCfg);
            }
            else if (_studioSubTab == 1)
            {
                DrawCompositeToolbox(w, panelCfg);
            }
            else
            {
                DrawCompositeCanvasAndPresets(w, panelCfg);
            }

            GUILayout.EndScrollView();
        }

        private void DrawCompositeLayersAndInspector(WidgetConfig w, CompositePanelConfig panelCfg)
        {
            // === 1. 图层列表面板 (PS Layers Panel) ===
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"📑 <b>{I18n.Tr("COMP_LAYERS_HEADER", "图层堆叠管理 (从顶层至底层)")}</b>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button(I18n.Tr("COMP_BTN_ADD_CONTROL", "➕ 置入新构件"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(95f), GUILayout.Height(20f)))
            {
                _studioSubTab = 1; // 切换到构件箱
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 针对当前选中图层的快捷操作工具条
            CompositeElementConfig curElem = panelCfg.Elements.Find(e => e.LayerId == _selectedLayerId);

            GUILayout.BeginHorizontal();
            GUI.enabled = curElem != null;

            if (GUILayout.Button("🔝 置顶", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                int maxOrder = panelCfg.Elements.Count > 0 ? panelCfg.Elements.Max(e => e.DrawOrder) : 0;
                curElem.DrawOrder = maxOrder + 1;
                NormalizeDrawOrders(panelCfg);
                SyncCompositeConfig(w, panelCfg);
            }
            if (GUILayout.Button("⬆️ 上移", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                MoveLayerOrder(panelCfg, curElem, 1);
                SyncCompositeConfig(w, panelCfg);
            }
            if (GUILayout.Button("⬇️ 下移", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                MoveLayerOrder(panelCfg, curElem, -1);
                SyncCompositeConfig(w, panelCfg);
            }
            if (GUILayout.Button("🔚 置底", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                int minOrder = panelCfg.Elements.Count > 0 ? panelCfg.Elements.Min(e => e.DrawOrder) : 0;
                curElem.DrawOrder = minOrder - 1;
                NormalizeDrawOrders(panelCfg);
                SyncCompositeConfig(w, panelCfg);
            }

            GUILayout.Space(10f);

            if (GUILayout.Button("📋 复制", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                var clone = curElem.Clone();
                clone.LayerId = "elem_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                clone.Name = curElem.Name + " (副本)";
                clone.X += 15f;
                clone.Y -= 15f;
                clone.DrawOrder = curElem.DrawOrder + 1;
                panelCfg.Elements.Add(clone);
                NormalizeDrawOrders(panelCfg);
                _selectedLayerId = clone.LayerId;
                SyncCompositeConfig(w, panelCfg);
                ShowToast(string.Format("已复制图层: {0}", clone.Name));
            }
            if (GUILayout.Button("🗑️ 删除", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                panelCfg.Elements.Remove(curElem);
                _selectedLayerId = panelCfg.Elements.Count > 0 ? panelCfg.Elements[0].LayerId : null;
                SyncCompositeConfig(w, panelCfg);
                ShowToast("已删除图层");
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 滚动图层列表 (降序显示：越顶层越在上面，符合 Photoshop / Figma 直觉)
            var sortedLayers = new List<CompositeElementConfig>(panelCfg.Elements);
            sortedLayers.Sort((a, b) => b.DrawOrder.CompareTo(a.DrawOrder));

            _layersScroll = GUILayout.BeginScrollView(_layersScroll, GUILayout.Height(Mathf.Min(160f, Mathf.Max(65f, sortedLayers.Count * 28f + 10f))));

            if (sortedLayers.Count == 0)
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>画板尚无任何图层，点击上方「➕ 置入新构件」开始搭建！</size></color>");
            }
            else
            {
                for (int i = 0; i < sortedLayers.Count; i++)
                {
                    var elem = sortedLayers[i];
                    bool isSelected = elem.LayerId == _selectedLayerId;
                    GUIStyle rowStyle = isSelected ? MFPGuiSkin.RowSelectedStyle : MFPGuiSkin.RowNormalStyle;

                    GUILayout.BeginHorizontal(rowStyle);

                    // 显隐眼睛
                    string eyeIcon = elem.IsVisible ? "👁️" : "🕶️";
                    if (GUILayout.Button(eyeIcon, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(24f), GUILayout.Height(20f)))
                    {
                        elem.IsVisible = !elem.IsVisible;
                        SyncCompositeConfig(w, panelCfg);
                    }

                    // 锁定挂锁
                    string lockIcon = elem.IsLocked ? "🔒" : "🔓";
                    if (GUILayout.Button(lockIcon, MFPGuiSkin.StepperButtonStyle, GUILayout.Width(24f), GUILayout.Height(20f)))
                    {
                        elem.IsLocked = !elem.IsLocked;
                        SyncCompositeConfig(w, panelCfg);
                    }

                    // 图层名选择按钮
                    string namePrefix = isSelected ? $"<b><color=#{MFPGuiSkin.HexAccentCyan}>{elem.Name}</color></b>" : elem.Name;
                    if (GUILayout.Button(namePrefix, GUI.skin.label, GUILayout.ExpandWidth(true), GUILayout.Height(20f)))
                    {
                        _selectedLayerId = elem.LayerId;
                    }

                    // 类型标牌
                    string protoTag = GetPrototypeCategoryTag(elem);
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>[{protoTag}]</size></color>", GUILayout.Width(55f));

                    // 不透明度
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>α:{(int)(elem.Opacity * 100)}%</size></color>", GUILayout.Width(45f));

                    GUILayout.EndHorizontal();
                    GUILayout.Space(1f);
                }
            }

            GUILayout.EndScrollView();
            MFPGuiSkin.EndCard();

            GUILayout.Space(5f);

            // === 2. 选中图层精细属性检查器 (Selected Layer Properties Inspector) ===
            DrawSelectedLayerInspectorCard(w, panelCfg, curElem);
        }

        private void DrawSelectedLayerInspectorCard(WidgetConfig w, CompositePanelConfig panelCfg, CompositeElementConfig curElem)
        {
            MFPGuiSkin.BeginCard();

            if (curElem == null)
            {
                GUILayout.Label($"👈 <color=#{MFPGuiSkin.HexTextSecondary}>请在上方图层列表中选择一个图层进行精细调整</color>");
                MFPGuiSkin.EndCard();
                return;
            }

            GUILayout.Label($"🛠️ <b>{I18n.Tr("COMP_LAYER_INSPECT_TITLE", "图层精细调校")}</b> | <color=#{MFPGuiSkin.HexAccentCyan}>{curElem.Name}</color>");
            GUILayout.Space(4f);

            // 1. 图层基本标识与重命名
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("图层名称:", GUILayout.Width(75f));
            string newName = GUILayout.TextField(curElem.Name, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
            if (newName != curElem.Name)
            {
                curElem.Name = newName;
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("原型标识:", GUILayout.Width(75f));
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>{curElem.PrototypeId} (来源: {curElem.SourceWidgetTypeName})</size></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("构件品类:", GUILayout.Width(75f));
            GUILayout.Label($"<b><color=#{MFPGuiSkin.HexAccentCyan}>{GetPrototypeCategoryTag(curElem)}</color></b> <color=#{MFPGuiSkin.HexTextSecondary}><size=9>({curElem.Category})</size></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 几何变换 (Transform: X, Y, W, H, Rotation)
            MFPGuiSkin.BeginInset();
            GUILayout.Label("📐 <b>几何变换与位置尺寸 (Transform)</b>");
            GUILayout.Space(2f);

            // X 轴
            GUILayout.BeginHorizontal();
            GUILayout.Label("水平位置 X:", GUILayout.Width(75f));
            float oldX = curElem.X;
            float newX = DrawStepNumericField(curElem.X, 10f, 1f);
            if (Mathf.Abs(newX - oldX) > 0.01f)
            {
                curElem.X = Mathf.Round(newX * 10f) / 10f;
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            // Y 轴
            GUILayout.BeginHorizontal();
            GUILayout.Label("垂直位置 Y:", GUILayout.Width(75f));
            float oldY = curElem.Y;
            float newY = DrawStepNumericField(curElem.Y, 10f, 1f);
            if (Mathf.Abs(newY - oldY) > 0.01f)
            {
                curElem.Y = Mathf.Round(newY * 10f) / 10f;
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            // 宽度 Width
            GUILayout.BeginHorizontal();
            GUILayout.Label("宽度 Width:", GUILayout.Width(75f));
            float oldW = curElem.Width;
            float newW = Mathf.Max(10f, DrawStepNumericField(curElem.Width, 10f, 1f));
            if (Mathf.Abs(newW - oldW) > 0.01f)
            {
                curElem.Width = Mathf.Round(newW * 10f) / 10f;
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            // 高度 Height
            GUILayout.BeginHorizontal();
            GUILayout.Label("高度 Height:", GUILayout.Width(75f));
            float oldH = curElem.Height;
            float newH = Mathf.Max(10f, DrawStepNumericField(curElem.Height, 10f, 1f));
            if (Mathf.Abs(newH - oldH) > 0.01f)
            {
                curElem.Height = Mathf.Round(newH * 10f) / 10f;
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            // 旋转 Rotation
            GUILayout.BeginHorizontal();
            GUILayout.Label("旋转角度:", GUILayout.Width(75f));
            float oldRot = curElem.Rotation;
            float newRot = GUILayout.HorizontalSlider(curElem.Rotation, 0f, 360f, GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{newRot:0}°</b>", GUILayout.Width(45f));
            if (GUILayout.Button("0°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(30f), GUILayout.Height(18f))) newRot = 0f;
            if (GUILayout.Button("90°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f), GUILayout.Height(18f))) newRot = 90f;
            if (Mathf.Abs(newRot - oldRot) > 0.1f)
            {
                curElem.Rotation = Mathf.Round(newRot);
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 3. 图层独立不透明度 (Layer Opacity - 对标 Photoshop)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("🪟 <b>图层独立不透明度 (Opacity)</b>:", GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b><color=#{MFPGuiSkin.HexAccentCyan}>{(int)(curElem.Opacity * 100)}%</color></b>", GUILayout.Width(45f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            float oldOp = curElem.Opacity;
            float newOp = GUILayout.HorizontalSlider(curElem.Opacity, 0.0f, 1.0f, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("25%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(38f), GUILayout.Height(18f))) newOp = 0.25f;
            if (GUILayout.Button("50%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(38f), GUILayout.Height(18f))) newOp = 0.50f;
            if (GUILayout.Button("75%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(38f), GUILayout.Height(18f))) newOp = 0.75f;
            if (GUILayout.Button("100%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(42f), GUILayout.Height(18f))) newOp = 1.0f;
            if (Mathf.Abs(newOp - oldOp) > 0.005f)
            {
                curElem.Opacity = Mathf.Clamp01(Mathf.Round(newOp * 100f) / 100f);
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 4. 遥测数据源与量纲设置 (Telemetry & Units)
            bool isTelemetryControl = !curElem.PrototypeId.Contains("header") && !curElem.PrototypeId.Contains("separator") && !curElem.PrototypeId.Contains("box");
            if (isTelemetryControl)
            {
                MFPGuiSkin.BeginInset();
                GUILayout.Label("📡 <b>遥测数据源与量纲 (Telemetry & Units)</b>");
                GUILayout.Space(2f);

                // Token 与 查表抽屉按钮
                GUILayout.BeginHorizontal();
                GUILayout.Label("数据 Token:", GUILayout.Width(75f));
                string oldTok = curElem.Token ?? "";
                string newTok = GUILayout.TextField(oldTok, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
                if (newTok != oldTok)
                {
                    curElem.Token = newTok;
                    SyncCompositeConfig(w, panelCfg);
                }
                if (GUILayout.Button(I18n.Tr("COMP_BTN_BROWSE_PARAM", "🔍 查表选择 (736+)"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(130f), GUILayout.Height(20f)))
                {
                    TelemetryParamDrawer.Open($"选择图层 [{curElem.Name}] 遥测参数", chosen =>
                    {
                        curElem.Token = chosen;
                        if (chosen.Contains("SPD") || chosen.Contains("VEL")) curElem.UnitDimension = "Velocity";
                        else if (chosen.Contains("ALT") || chosen.Contains("AP") || chosen.Contains("PE") || chosen.Contains("DIST")) curElem.UnitDimension = "Length";
                        else if (chosen.Contains("ACC") || chosen.Contains("G_FORCE")) curElem.UnitDimension = "Acceleration";
                        else if (chosen.Contains("ATM") || chosen.Contains("PRES") || chosen.Contains("Q")) curElem.UnitDimension = "Pressure";
                        SyncCompositeConfig(w, panelCfg);
                    });
                }
                GUILayout.EndHorizontal();

                // 标题与单位
                GUILayout.BeginHorizontal();
                GUILayout.Label("显示标题:", GUILayout.Width(75f));
                string newTitle = GUILayout.TextField(curElem.Title ?? "", GUILayout.Width(110f), GUILayout.Height(20f));
                if (newTitle != (curElem.Title ?? ""))
                {
                    curElem.Title = newTitle;
                    SyncCompositeConfig(w, panelCfg);
                }

                GUILayout.Space(8f);
                GUILayout.Label("单位角标:", GUILayout.Width(60f));
                string newUnit = GUILayout.TextField(curElem.Unit ?? "", GUILayout.Width(80f), GUILayout.Height(20f));
                if (newUnit != (curElem.Unit ?? ""))
                {
                    curElem.Unit = newUnit;
                    SyncCompositeConfig(w, panelCfg);
                }
                GUILayout.EndHorizontal();

                // 量纲选择
                GUILayout.BeginHorizontal();
                GUILayout.Label("量纲制式:", GUILayout.Width(75f));
                string[] dims = new string[] { "无", "Velocity", "Length", "Acceleration", "Pressure" };
                string[] dimLabels = new string[] { "无换算", "航速", "高度", "加速度", "压强" };
                for (int d = 0; d < dims.Length; d++)
                {
                    bool isDimSel = string.Equals(curElem.UnitDimension, dims[d], StringComparison.OrdinalIgnoreCase) || (d == 0 && string.IsNullOrEmpty(curElem.UnitDimension));
                    GUIStyle bStyle = isDimSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                    if (GUILayout.Button(dimLabels[d], bStyle, GUILayout.Height(18f)))
                    {
                        curElem.UnitDimension = d == 0 ? "" : dims[d];
                        SyncCompositeConfig(w, panelCfg);
                    }
                }
                GUILayout.EndHorizontal();

                // 标定阈值 (适用于 LinearBar 或 Readout)
                if (curElem.PrototypeId.Contains("bar") || curElem.PrototypeId.Contains("gauge") || curElem.PrototypeId.Contains("readout"))
                {
                    GUILayout.Space(2f);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("量程范围:", GUILayout.Width(75f));
                    GUILayout.Label(I18n.Tr("ASM_PROP_MIN", "最小值:"), GUILayout.Width(45f));
                    curElem.MinValue = DrawStepDoubleField(curElem.MinValue, 10.0, 1.0);
                    GUILayout.Label(I18n.Tr("ASM_PROP_MAX", "最大值:"), GUILayout.Width(45f));
                    curElem.MaxValue = DrawStepDoubleField(curElem.MaxValue, 10.0, 1.0);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(80f);
                    GUILayout.Label("注意阈:", GUILayout.Width(45f));
                    curElem.CautionThreshold = DrawStepDoubleField(curElem.CautionThreshold, 5.0, 1.0);
                    GUILayout.Label("告警阈:", GUILayout.Width(45f));
                    curElem.WarningThreshold = DrawStepDoubleField(curElem.WarningThreshold, 5.0, 1.0);
                    GUILayout.EndHorizontal();
                }

                MFPGuiSkin.EndInset();
            }

            // 5. 交互动作配置 (适用于开关/分级按钮)
            bool isActionControl = curElem.PrototypeId.Contains("btn") || curElem.PrototypeId.Contains("button") || curElem.PrototypeId.Contains("switch") || curElem.PrototypeId.Contains("stage");
            if (isActionControl)
            {
                GUILayout.Space(4f);
                MFPGuiSkin.BeginInset();
                GUILayout.Label("⚡ <b>系统动作绑定 (Flight System Action)</b>");
                GUILayout.Space(2f);

                GUILayout.BeginHorizontal();
                GUILayout.Label("动作类型:", GUILayout.Width(75f));
                string[] actions = new string[] { "RCS", "SAS", "Gear", "Brakes", "Lights", "Abort", "Stage" };
                for (int a = 0; a < actions.Length; a++)
                {
                    bool isActSel = string.Equals(curElem.ActionType, actions[a], StringComparison.OrdinalIgnoreCase);
                    GUIStyle aStyle = isActSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                    if (GUILayout.Button(actions[a], aStyle, GUILayout.Height(18f)))
                    {
                        curElem.ActionType = actions[a];
                        SyncCompositeConfig(w, panelCfg);
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("开关模式:", GUILayout.Width(75f));
                string toggleText = curElem.IsToggle ? "🔒 自锁双态开关" : "⚡ 瞬时触发按钮";
                if (GUILayout.Button(toggleText, MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f), GUILayout.Width(130f)))
                {
                    curElem.IsToggle = !curElem.IsToggle;
                    SyncCompositeConfig(w, panelCfg);
                }
                GUILayout.EndHorizontal();

                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawCompositeToolbox(WidgetConfig w, CompositePanelConfig panelCfg)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"📦 <b>{I18n.Tr("COMP_TOOLBOX_TITLE", "航电微构件工具箱 (自动遍历现有 44+ 款组件与原生构件)")}</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("📑 返回图层面板", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(100f), GUILayout.Height(20f)))
            {
                _studioSubTab = 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 搜索栏
            MFPGuiSkin.DrawSearchBar(ref _toolboxSearch, "搜索构件显示名、原型 ID 或 Token...");

            GUILayout.Space(3f);

            // 功能类别标签横条
            GUILayout.BeginHorizontal();
            string[] catLabels = new string[] { "全类别", "读数盒", "线性槽", "弧形表", "光字牌", "动作开关", "姿控排", "结构装饰" };
            for (int i = 0; i < catLabels.Length; i++)
            {
                bool isSel = _toolboxCategoryFilter == i;
                GUIStyle catStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(catLabels[i], catStyle, GUILayout.Height(20f)))
                {
                    _toolboxCategoryFilter = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 来源组件筛选横条
            GUILayout.BeginHorizontal();
            string[] srcLabels = new string[] { "全来源", "官方原生", "姿态球", "底控台", "推力表", "SpaceX", "其他组件" };
            for (int s = 0; s < srcLabels.Length; s++)
            {
                bool isSel = _toolboxSourceFilter == s;
                GUIStyle srcStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(srcLabels[s], srcStyle, GUILayout.Height(18f)))
                {
                    _toolboxSourceFilter = s;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 构件卡片列表
            var allPrototypes = WidgetControlCatalog.AllPrototypes;
            string searchLower = (_toolboxSearch ?? "").Trim().ToLowerInvariant();

            _toolboxScroll = GUILayout.BeginScrollView(_toolboxScroll, GUILayout.Height(380f));

            int displayedCount = 0;

            for (int i = 0; i < allPrototypes.Count; i++)
            {
                var proto = allPrototypes[i];
                if (proto == null) continue;

                // 类别过滤
                if (!MatchesToolboxCategory(proto, _toolboxCategoryFilter)) continue;

                // 来源过滤
                if (!MatchesToolboxSource(proto, _toolboxSourceFilter)) continue;

                // 搜索过滤
                if (!string.IsNullOrEmpty(searchLower))
                {
                    string idLower = (proto.PrototypeId ?? "").ToLowerInvariant();
                    string nameLower = (proto.DisplayName ?? "").ToLowerInvariant();
                    string descLower = (proto.Description ?? "").ToLowerInvariant();
                    string tokLower = (proto.DefaultToken ?? "").ToLowerInvariant();
                    string srcLower = (proto.SourceWidgetDisplayName ?? "").ToLowerInvariant();
                    if (!idLower.Contains(searchLower) && !nameLower.Contains(searchLower) &&
                        !descLower.Contains(searchLower) && !tokLower.Contains(searchLower) &&
                        !srcLower.Contains(searchLower))
                    {
                        continue;
                    }
                }

                displayedCount++;

                GUILayout.BeginVertical(MFPGuiSkin.InsetStyle);
                GUILayout.BeginHorizontal();

                // 构件名称与分类徽章
                string catBadge = HUDEditModeToolbar.GetCategoryShortTag(proto.Category);
                string catColor = MFPGuiSkin.HexTextSecondary;
                switch (proto.Category)
                {
                    case WidgetControlCategory.Header: catColor = "BB86FC"; break;
                    case WidgetControlCategory.Readout: catColor = MFPGuiSkin.HexAccentCyan; break;
                    case WidgetControlCategory.LinearGauge: catColor = MFPGuiSkin.HexAccentGreen; break;
                    case WidgetControlCategory.ArcGauge: catColor = "03DAC6"; break;
                    case WidgetControlCategory.Annunciator: catColor = MFPGuiSkin.HexAccentAmber; break;
                    case WidgetControlCategory.ActionButton: catColor = "FFB74D"; break;
                    case WidgetControlCategory.ModeCapsule: catColor = "BA68C8"; break;
                }

                GUILayout.Label($"<color=#{catColor}><b>[{catBadge}]</b></color> <b>{proto.DisplayName}</b>", GUILayout.ExpandWidth(true));
                GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><size=10>[{proto.DefaultSize.x:0}×{proto.DefaultSize.y:0}]</size></color>", GUILayout.Width(65f));

                if (GUILayout.Button(I18n.Tr("COMP_BTN_PLACE", "+ 置入画板"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(78f), GUILayout.Height(20f)))
                {
                    PlacePrototypeIntoPanel(w, panelCfg, proto);
                }
                GUILayout.EndHorizontal();

                // 来源与说明
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=10>来源: {proto.SourceWidgetDisplayName} | 标识: {proto.PrototypeId}</size></color>", GUILayout.ExpandWidth(true));
                if (!string.IsNullOrEmpty(proto.DefaultToken))
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><size=9>{proto.DefaultToken}</size></color>", GUILayout.Width(110f));
                }
                else if (!string.IsNullOrEmpty(proto.DefaultAction))
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentAmber}><size=9>动作: {proto.DefaultAction}</size></color>", GUILayout.Width(110f));
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(proto.Description))
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>{proto.Description}</size></color>");
                }

                GUILayout.EndVertical();
                GUILayout.Space(2f);
            }

            if (displayedCount == 0)
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=11>未搜索到匹配构件，尝试清空搜索词或切换筛选标签。</size></color>");
            }

            GUILayout.EndScrollView();
            MFPGuiSkin.EndCard();
        }

        private void PlacePrototypeIntoPanel(WidgetConfig w, CompositePanelConfig panelCfg, ControlPrototypeDescriptor proto)
        {
            var newElem = new CompositeElementConfig
            {
                LayerId = "elem_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Name = proto.DisplayName,
                PrototypeId = proto.PrototypeId,
                SourceWidgetTypeName = proto.SourceWidgetTypeName,
                Category = proto.Category,
                Width = proto.DefaultSize.x,
                Height = proto.DefaultSize.y,
                Token = proto.DefaultToken,
                Title = !string.IsNullOrEmpty(proto.DefaultTitle) ? proto.DefaultTitle : WidgetControlCatalog.ExtractCleanTitle(proto.DisplayName),
                Unit = proto.DefaultUnit,
                UnitDimension = proto.DefaultUnitDimension,
                MinValue = proto.DefaultMinValue,
                MaxValue = proto.DefaultMaxValue,
                CautionThreshold = proto.DefaultCaution,
                WarningThreshold = proto.DefaultWarning,
                ActionType = proto.DefaultAction,
                IsToggle = proto.IsToggle,
                Opacity = proto.DefaultOpacity,
                DrawOrder = panelCfg.Elements.Count > 0 ? panelCfg.Elements.Max(e => e.DrawOrder) + 1 : 0
            };

            // 错开位置避免完全重叠
            int count = panelCfg.Elements.Count;
            newElem.X = (count % 4 - 1.5f) * 35f;
            newElem.Y = (count % 3 - 1f) * 20f;

            panelCfg.Elements.Add(newElem);
            NormalizeDrawOrders(panelCfg);
            _selectedLayerId = newElem.LayerId;
            _studioSubTab = 0; // 自动切回图层精调页
            SyncCompositeConfig(w, panelCfg);
            ShowToast(string.Format(I18n.Tr("STUDIO_TOAST_ELEMENT_PLACED", "已置入控件: {0}"), proto.DisplayName));
        }

        private void DrawCompositeCanvasAndPresets(WidgetConfig w, CompositePanelConfig panelCfg)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.Label($"⚙️ <b>{I18n.Tr("COMP_CANVAS_SETTINGS_TITLE", "画板底盘参数与预设 (Artboard & Presets)")}</b>");
            GUILayout.Space(4f);

            // 1. 画板尺寸与几何参数
            MFPGuiSkin.BeginInset();
            GUILayout.Label("📐 <b>画板基准分辨率 (Base Resolution)</b>");
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("画板宽度:", GUILayout.Width(75f));
            float oldW = panelCfg.BaseWidth;
            float newW = DrawStepNumericField(panelCfg.BaseWidth, 20f, 5f);
            if (Mathf.Abs(newW - oldW) > 0.1f)
            {
                panelCfg.BaseWidth = Mathf.Clamp(newW, 100f, 1920f);
                SyncCompositeConfig(w, panelCfg);
            }

            GUILayout.Space(10f);
            GUILayout.Label("画板高度:", GUILayout.Width(75f));
            float oldH = panelCfg.BaseHeight;
            float newH = DrawStepNumericField(panelCfg.BaseHeight, 20f, 5f);
            if (Mathf.Abs(newH - oldH) > 0.1f)
            {
                panelCfg.BaseHeight = Mathf.Clamp(newH, 60f, 1080f);
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            // 常用预设分辨率
            GUILayout.BeginHorizontal();
            GUILayout.Label("常用分辨率:", GUILayout.Width(75f));
            if (GUILayout.Button("380×220 标准", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(18f))) { panelCfg.BaseWidth = 380f; panelCfg.BaseHeight = 220f; SyncCompositeConfig(w, panelCfg); }
            if (GUILayout.Button("440×240 宽屏", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(18f))) { panelCfg.BaseWidth = 440f; panelCfg.BaseHeight = 240f; SyncCompositeConfig(w, panelCfg); }
            if (GUILayout.Button("240×360 竖屏", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(18f))) { panelCfg.BaseWidth = 240f; panelCfg.BaseHeight = 360f; SyncCompositeConfig(w, panelCfg); }
            if (GUILayout.Button("320×320 正方", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(18f))) { panelCfg.BaseWidth = 320f; panelCfg.BaseHeight = 320f; SyncCompositeConfig(w, panelCfg); }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 底盘材质与不透明度
            MFPGuiSkin.BeginInset();
            GUILayout.Label("🪟 <b>画板底衬材质与不透明度 (Backdrop Style & Opacity)</b>");
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("材质风格:", GUILayout.Width(75f));
            string[] styles = new string[] { "DarkGlass", "Clear", "Framed", "MetalOutline" };
            string[] styleLabels = new string[] { "深色航空玻璃", "无框纯透", "工程边框", "金属拉丝" };
            for (int s = 0; s < styles.Length; s++)
            {
                bool isSel = string.Equals(panelCfg.BackgroundStyle, styles[s], StringComparison.OrdinalIgnoreCase);
                GUIStyle bStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(styleLabels[s], bStyle, GUILayout.Height(18f)))
                {
                    panelCfg.BackgroundStyle = styles[s];
                    SyncCompositeConfig(w, panelCfg);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("底衬不透明度:", GUILayout.Width(85f));
            float oldOp = panelCfg.PanelOpacity;
            float newOp = GUILayout.HorizontalSlider(panelCfg.PanelOpacity, 0f, 1f, GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{(int)(newOp * 100)}%</b>", GUILayout.Width(45f));
            if (GUILayout.Button("0% 纯透", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f), GUILayout.Height(18f))) newOp = 0.0f;
            if (GUILayout.Button("30%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f), GUILayout.Height(18f))) newOp = 0.30f;
            if (GUILayout.Button("70%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f), GUILayout.Height(18f))) newOp = 0.70f;
            if (GUILayout.Button("95%", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(35f), GUILayout.Height(18f))) newOp = 0.95f;
            if (Mathf.Abs(newOp - oldOp) > 0.005f)
            {
                panelCfg.PanelOpacity = Mathf.Clamp01(Mathf.Round(newOp * 100f) / 100f);
                SyncCompositeConfig(w, panelCfg);
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 3. 一键载入预设面板
            MFPGuiSkin.BeginInset();
            GUILayout.Label("📋 <b>一键置入预设模板 (Presets)</b>");
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🛩️ 经典综合座舱 PFD", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                panelCfg = CompositePanelConfig.CreateDefaultDemoPanel();
                SyncCompositeConfig(w, panelCfg);
                ShowToast("已载入经典综合座舱 PFD 模板");
            }
            if (GUILayout.Button("🚀 SpaceX 推进遥测台", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                panelCfg = CompositePanelConfig.CreateSpaceXPropulsionPanel();
                SyncCompositeConfig(w, panelCfg);
                ShowToast("已载入 SpaceX 推进遥测台模板");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🪐 轨道机动领航综合板", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                panelCfg = CompositePanelConfig.CreateOrbitalManeuverPanel();
                SyncCompositeConfig(w, panelCfg);
                ShowToast("已载入轨道机动领航综合板模板");
            }
            if (GUILayout.Button("✨ 清空为纯净画板", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                panelCfg = CompositePanelConfig.CreateBlankPanel();
                SyncCompositeConfig(w, panelCfg);
                ShowToast("已清空画板");
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 4. JSON 配置导入与导出
            MFPGuiSkin.BeginInset();
            GUILayout.Label("💾 <b>画板配置 JSON 导入 / 导出</b>");
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 复制配置到剪贴板", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(22f)))
            {
                GUIUtility.systemCopyBuffer = panelCfg.ToJson(true);
                ShowToast("已复制画板完整 JSON 配置到剪贴板！");
            }
            if (GUILayout.Button("📥 从剪贴板粘贴导入", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip) && clip.Contains("Elements"))
                {
                    var imported = CompositePanelConfig.FromJson(clip);
                    if (imported != null && imported.Elements != null)
                    {
                        panelCfg = imported;
                        SyncCompositeConfig(w, panelCfg);
                        ShowToast("成功从剪贴板导入画板配置！");
                    }
                    else
                    {
                        ShowToast("剪贴板内容不是合法的画板配置 JSON");
                    }
                }
                else
                {
                    ShowToast("剪贴板中未发现有效画板 JSON 数据");
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        private static float DrawStepNumericField(float val, float bigStep, float smallStep)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(180f));
            if (GUILayout.Button($"-{bigStep:0}", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f), GUILayout.Height(18f))) val -= bigStep;
            if (GUILayout.Button($"-{smallStep:0}", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(18f))) val -= smallStep;

            string sVal = GUILayout.TextField($"{val:0.0}", GUILayout.Width(50f), GUILayout.Height(18f));
            if (float.TryParse(sVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                val = parsed;
            }

            if (GUILayout.Button($"+{smallStep:0}", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(22f), GUILayout.Height(18f))) val += smallStep;
            if (GUILayout.Button($"+{bigStep:0}", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f), GUILayout.Height(18f))) val += bigStep;
            GUILayout.EndHorizontal();
            return val;
        }

        private static double DrawStepDoubleField(double val, double bigStep, double smallStep)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(130f));
            if (GUILayout.Button("-", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(18f), GUILayout.Height(18f))) val -= smallStep;

            string sVal = GUILayout.TextField($"{val:0.0}", GUILayout.Width(45f), GUILayout.Height(18f));
            if (double.TryParse(sVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                val = parsed;
            }

            if (GUILayout.Button("+", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(18f), GUILayout.Height(18f))) val += smallStep;
            GUILayout.EndHorizontal();
            return val;
        }

        private static void NormalizeDrawOrders(CompositePanelConfig cfg)
        {
            if (cfg?.Elements == null) return;
            var list = new List<CompositeElementConfig>(cfg.Elements);
            list.Sort((a, b) => a.DrawOrder.CompareTo(b.DrawOrder));
            for (int i = 0; i < list.Count; i++)
            {
                list[i].DrawOrder = i;
            }
        }

        private static void MoveLayerOrder(CompositePanelConfig cfg, CompositeElementConfig target, int delta)
        {
            if (cfg?.Elements == null || target == null || delta == 0) return;
            var list = new List<CompositeElementConfig>(cfg.Elements);
            list.Sort((a, b) => a.DrawOrder.CompareTo(b.DrawOrder));
            int idx = list.IndexOf(target);
            if (idx < 0) return;
            int newIdx = Mathf.Clamp(idx + delta, 0, list.Count - 1);
            if (newIdx == idx) return;

            list.RemoveAt(idx);
            list.Insert(newIdx, target);
            for (int i = 0; i < list.Count; i++)
            {
                list[i].DrawOrder = i;
            }
        }

        private static string GetPrototypeCategoryTag(CompositeElementConfig elem)
        {
            if (elem == null) return HUDEditModeToolbar.GetCategoryShortTag(WidgetControlCategory.GenericElement);
            return HUDEditModeToolbar.GetCategoryShortTag(elem.ResolveCategory());
        }

        private static string GetPrototypeCategoryTag(string protoId)
        {
            if (!string.IsNullOrEmpty(protoId))
            {
                var proto = WidgetControlCatalog.FindPrototype(protoId);
                if (proto != null)
                {
                    return HUDEditModeToolbar.GetCategoryShortTag(proto.Category);
                }
            }
            return HUDEditModeToolbar.GetCategoryShortTag(WidgetControlCategory.GenericElement);
        }

        private static bool MatchesToolboxCategory(ControlPrototypeDescriptor proto, int catFilter)
        {
            if (catFilter == 0) return true;
            switch (catFilter)
            {
                case 1: return proto.Category == WidgetControlCategory.Readout;
                case 2: return proto.Category == WidgetControlCategory.LinearGauge;
                case 3: return proto.Category == WidgetControlCategory.ArcGauge;
                case 4: return proto.Category == WidgetControlCategory.Annunciator;
                case 5: return proto.Category == WidgetControlCategory.ActionButton;
                case 6: return proto.PrototypeId.Contains("sas") || proto.Category == WidgetControlCategory.ModeCapsule;
                case 7: return proto.Category == WidgetControlCategory.Header || proto.Category == WidgetControlCategory.GenericElement;
                default: return true;
            }
        }

        private static bool MatchesToolboxSource(ControlPrototypeDescriptor proto, int srcFilter)
        {
            if (srcFilter == 0) return true;
            string src = (proto.SourceWidgetTypeName ?? "").ToLowerInvariant();
            switch (srcFilter)
            {
                case 1: return src == "native";
                case 2: return src.Contains("navball");
                case 3: return src.Contains("bottom_controls");
                case 4: return src.Contains("throttle");
                case 5: return src.Contains("spacex");
                default: return src != "native" && !src.Contains("navball") && !src.Contains("bottom_controls") && !src.Contains("throttle") && !src.Contains("spacex");
            }
        }

        private void SyncCompositeConfig(WidgetConfig w, CompositePanelConfig panelCfg)
        {
            if (w == null || panelCfg == null) return;
            w.CustomTemplate = panelCfg.ToJson();
            MarkDirty();

            // 实时热刷新当前活跃小组件实例
            if (FlightHUDManager.Instance?.ModularWidgets != null)
            {
                for (int i = 0; i < FlightHUDManager.Instance.ModularWidgets.Count; i++)
                {
                    var liveWidget = FlightHUDManager.Instance.ModularWidgets[i];
                    if (liveWidget != null && liveWidget.Config?.WidgetId == w.WidgetId && liveWidget is Widgets.Gauges.CustomCompositePanelWidget composite)
                    {
                        composite.UpdateCompositeConfig(panelCfg);
                        break;
                    }
                }
            }
        }

        #endregion

        #endregion
    }
}
