using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新双栏主从式遥测装配台 (Master-Detail Avionics Assembler & Widget Inspector)
    /// 核心革新：
    /// 1. 左栏组件全局秒选器 (Widget Master Navigator, 270px)：支持实时关键字搜索、分类过滤与行内显隐，点击即切，告别盲目翻页。
    /// 2. 缓冲式防重绘数值标定 (Buffered Numeric Calibration)：彻底根绝输入小数点与负号时被重刷回弹的经典问题。
    /// 3. 可视化几何快速对齐工具集 (Visual Alignment & Snap Tools)：居中、贴边、吸附网格一键生效。
    /// 4. 4Hz 定频解算真实遥测实时预览与参数字典一键绑定。
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

        // 遥测词典检索与缓存
        private static string _catalogSearchQuery = "";
        private static string _lastCatalogSearch = null;
        private static int _catalogCatIndex = 0;
        private static int _lastCatalogCatIndex = -1;
        private const int CatalogPageSize = 6;
        private static int _catalogPage = 0;
        private static readonly List<TelemetryParam> _filteredParams = new List<TelemetryParam>();

        // 遥测卡片求值预览缓存
        private static string _cachedTemplate = null;
        private static string _cachedEvaluation = "---";
        private static float _lastEvalTime = 0f;

        // 脏数据与防抖提交
        private static bool _isDirty = false;
        private static float _dirtyTimer = 0f;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void SetSelectedWidget(string widgetId)
        {
            _selectedWidgetId = widgetId;
        }

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null || widgets.Count == 0)
            {
                GUILayout.Label($"<color=#FFAA00><b>{I18n.Tr("ASM_NO_WIDGETS", "当前没有任何组件，请在「航电库」中先添加组件。")}</b></color>");
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
            // 左栏：主从组件选择与过滤器 (Master Widget Navigator, 270px)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(270f), GUILayout.Height(SettingsGUI.ContentHeight));
            DrawWidgetMasterList(widgets, curWidget);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // =========================================================================
            // 右栏：组件参数标定与遥测词典 (Detail Inspector & Slot Calibration)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(SettingsGUI.ContentHeight));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.Height(SettingsGUI.ContentHeight));

            DrawWidgetDetailInspector(curWidget);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        #region Master List (Left Column)

        private static void DrawWidgetMasterList(List<WidgetConfig> widgets, WidgetConfig curWidget)
        {
            MFPGuiSkin.BeginCard();

            // 1. 标题与搜索框
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_HEADER_NAV", "组件导航 (WIDGETS)"), I18n.TrFormat("ASM_TOTAL_COUNT", widgets.Count));
            MFPGuiSkin.DrawSearchBar(ref _widgetSearchQuery, I18n.Tr("ASM_SEARCH_WIDGET", "筛选组件名称/ID..."));

            GUILayout.Space(4f);

            // 2. 分类筛选按钮
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

            GUILayout.Space(6f);

            // 3. 滚动组件卡片列表
            _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.Height(Mathf.Max(120f, SettingsGUI.ContentHeight - 128f)));

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

                // 点击条目选中
                string badge = w.WidgetType == "tape" ? "PFD" :
                              (w.WidgetType == "ecam_dial" ? "ECAM" :
                              (w.WidgetId.StartsWith("spacex.") ? "SPX" :
                              (w.WidgetId.StartsWith("custom.") ? "CARD" : "CORE")));

                string rowText = $"<b>{w.DisplayName}</b>\n<size=9><color=#88AACC>{badge}</color> | <color=#AAAAAA>({w.PositionX:F0}, {w.PositionY:F0})</color></size>";
                if (GUILayout.Button(rowText, "label", GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
                {
                    CommitPendingSaves();
                    _selectedWidgetId = w.WidgetId;
                }

                GUILayout.EndHorizontal();
            }

            if (matchCount == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label($"<color=#8899AA><size=11>{I18n.Tr("ASM_NO_MATCH", "未搜索到匹配项")}</size></color>");
            }

            GUILayout.EndScrollView();
            MFPGuiSkin.EndCard();
        }

        private static bool MatchesCategory(WidgetConfig w, int category)
        {
            if (category == 0) return true;
            string id = w.WidgetId.ToLowerInvariant();
            string type = (w.WidgetType ?? "").ToLowerInvariant();

            if (category == 1) return type == "ecam_dial" || type == "tape" || type == "bar_gauge" || id.Contains("gauge");
            if (category == 2) return id.Contains("eicas") || id.Contains("elec") || id.Contains("life") || id.Contains("perf") || id.Contains("signal") || id.Contains("rocket");
            if (category == 3) return id.StartsWith("spacex.") || type.StartsWith("spacex_");
            if (category == 4) return id.Contains("toolbar") || id.Contains("control") || id.Contains("sas") || id.Contains("timewarp") || id.Contains("staging") || id.Contains("ui_widget");
            return true;
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
            if (w.WidgetType == "ecam_dial" || w.WidgetType == "tape" || w.WidgetType == "bar_gauge")
            {
                DrawDialOrTapeCard(w);
            }
            else if (w.WidgetId.StartsWith("custom.") || w.WidgetType == "custom")
            {
                DrawCardTemplateCard(w);
            }
            else
            {
                DrawCoreInfoCard(w);
            }

            GUILayout.Space(4f);

            // 4. 嵌入式全球遥测词典库
            DrawEmbeddedCatalogCard(w);

            GUILayout.Space(4f);

            // 5. 性能与离屏调优
            DrawPerformanceCard(w);
        }

        private static void DrawBasicInfoCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            string typeTag = w.WidgetType == "tape" ? I18n.Tr("ASM_TAG_TAPE", "PFD 滚动标尺带") :
                            (w.WidgetType == "ecam_dial" ? I18n.Tr("ASM_TAG_ECAM", "ECAM 圆弧仪表") :
                            (w.WidgetId.StartsWith("spacex.") ? I18n.Tr("ASM_TAG_SPACEX", "SpaceX 航电组件") :
                            (w.WidgetId.StartsWith("custom.") ? I18n.Tr("ASM_TAG_CARD", "遥测卡片") : I18n.Tr("ASM_TAG_CORE", "原生核心组件"))));

            MFPGuiSkin.DrawBadge(typeTag, Color.white, new Color(0.00f, 0.45f, 0.65f, 0.95f), 120f);

            GUILayout.Label(I18n.Tr("ASM_PROP_NAME", "组件显示名称:"), GUILayout.Width(85f));
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
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_ALIGN", "📐 快速几何对齐工具"));

            // X / Y 坐标
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_POS_X", "坐标 X (px):"), GUILayout.Width(75f));
            float newX = MFPGuiSkin.DrawBufferedFloatField($"posX_{w.WidgetId}", w.PositionX, 65f);
            if (Math.Abs(newX - w.PositionX) > 0.01f) { w.PositionX = newX; ApplyTransformRuntime(w); }

            if (GUILayout.Button("-50", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX -= 50f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(32f))) { w.PositionX -= 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f))) { w.PositionX -= 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f))) { w.PositionX += 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(32f))) { w.PositionX += 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+50", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionX += 50f; ApplyTransformRuntime(w); }

            GUILayout.Space(12f);

            GUILayout.Label(I18n.Tr("ASM_POS_Y", "坐标 Y (px):"), GUILayout.Width(75f));
            float newY = MFPGuiSkin.DrawBufferedFloatField($"posY_{w.WidgetId}", w.PositionY, 65f);
            if (Math.Abs(newY - w.PositionY) > 0.01f) { w.PositionY = newY; ApplyTransformRuntime(w); }

            if (GUILayout.Button("-50", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY -= 50f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(32f))) { w.PositionY -= 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("-1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f))) { w.PositionY -= 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+1", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(26f))) { w.PositionY += 1f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+10", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(32f))) { w.PositionY += 10f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("+50", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.PositionY += 50f; ApplyTransformRuntime(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 一键对齐工具集
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_QUICK_ALIGN", "快速对齐:"), GUILayout.Width(75f));
            if (GUILayout.Button(I18n.Tr("ASM_ALIGN_CENTER_H", "水平居中 0"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(80f))) { w.PositionX = 0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_ALIGN_CENTER_V", "垂直居中 0"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(80f))) { w.PositionY = 0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_ALIGN_LEFT", "贴左 -440"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f))) { w.PositionX = -440f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_ALIGN_RIGHT", "贴右 +440"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f))) { w.PositionX = 440f; ApplyTransformRuntime(w); }
            if (GUILayout.Button(I18n.Tr("ASM_SNAP_GRID", "🧲 吸附 10px 网格"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(115f)))
            {
                w.PositionX = Mathf.Round(w.PositionX / 10f) * 10f;
                w.PositionY = Mathf.Round(w.PositionY / 10f) * 10f;
                ApplyTransformRuntime(w);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 缩放比例
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.TrFormat("ASM_SCALE_LABEL", w.Scale), GUILayout.Width(95f));
            float sVal = GUILayout.HorizontalSlider(w.Scale, 0.3f, 3.0f, GUILayout.Width(140f));
            if (Math.Abs(sVal - w.Scale) > 0.01f) { w.Scale = Mathf.Round(sVal * 20f) / 20f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("0.8x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 0.8f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.0x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.2x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.2f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.5x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Scale = 1.5f; ApplyTransformRuntime(w); }

            GUILayout.Space(12f);

            // 旋转角
            GUILayout.Label(I18n.TrFormat("ASM_ROTATION_LABEL", w.Rotation), GUILayout.Width(80f));
            float rVal = GUILayout.HorizontalSlider(w.Rotation, 0f, 360f, GUILayout.Width(120f));
            if (Math.Abs(rVal - w.Rotation) > 0.5f) { w.Rotation = Mathf.Round(rVal / 5f) * 5f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("0°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(30f))) { w.Rotation = 0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("90°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(34f))) { w.Rotation = 90f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("180°", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(40f))) { w.Rotation = 180f; ApplyTransformRuntime(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 非对称纵横比拉伸 (细长 / 矮胖调节)
            GUILayout.BeginHorizontal();
            float effX = w.EffectiveScaleX;
            GUILayout.Label(I18n.TrFormat("ASM_SCALE_X_LABEL", effX), GUILayout.Width(95f));
            float sxVal = GUILayout.HorizontalSlider(effX, 0.3f, 3.0f, GUILayout.Width(140f));
            if (Math.Abs(sxVal - effX) > 0.01f) { w.ScaleX = Mathf.Round(sxVal * 20f) / 20f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("0.7x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 0.7f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.0x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 1.0f; ApplyTransformRuntime(w); }
            if (GUILayout.Button("1.4x", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(36f))) { w.ScaleX = 1.4f; ApplyTransformRuntime(w); }

            GUILayout.Space(12f);

            float effY = w.EffectiveScaleY;
            GUILayout.Label(I18n.TrFormat("ASM_SCALE_Y_LABEL", effY), GUILayout.Width(80f));
            float syVal = GUILayout.HorizontalSlider(effY, 0.3f, 3.0f, GUILayout.Width(120f));
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
            GUILayout.Label(I18n.TrFormat("ASM_LAYER_LABEL", curLayer, totalLayers), GUILayout.Width(130f));
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_BOTTOM", "⤓ 置底"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f))) { WidgetLayerManager.SendToBack(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_DOWN", "▼ 降层"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f))) { WidgetLayerManager.SendBackward(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_UP", "▲ 升层"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f))) { WidgetLayerManager.BringForward(w); }
            if (GUILayout.Button(I18n.Tr("LAYER_BTN_TOP", "⤒ 置顶"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f))) { WidgetLayerManager.BringToFront(w); }

            GUILayout.Space(12f);
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

            // 数据源绑定
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_TOKEN_NUMERIC", "当前驱动数据源:"), GUILayout.Width(110f));
            string tokenText = string.IsNullOrEmpty(w.NumericToken) ? I18n.Tr("ASM_TOKEN_UNBOUND", "<未绑定>") : w.NumericToken;
            MFPGuiSkin.DrawBadge(tokenText, Color.white, new Color(0.00f, 0.40f, 0.60f, 0.95f));

            if (GUILayout.Button(I18n.Tr("ASM_TOKEN_UNBIND", "解绑数据源"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(80f)))
            {
                w.NumericToken = "";
                MarkDirty();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            var paramMeta = TelemetryCatalog.FindByToken(w.NumericToken);
            if (paramMeta != null)
            {
                GUILayout.Label($"<color=#9FAFFF><size=11>• {paramMeta.DisplayName}: {paramMeta.Description}</size></color>");
            }

            GUILayout.Space(6f);

            // Min & Max
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_MIN", "量程下限:"), GUILayout.Width(105f));
            double newMin = MFPGuiSkin.DrawBufferedDoubleField($"min_{w.WidgetId}", w.MinValue, 75f);
            if (Math.Abs(newMin - w.MinValue) > 0.0001) { w.MinValue = (float)newMin; MarkDirty(); }

            GUILayout.Space(16f);

            GUILayout.Label(I18n.Tr("ASM_RANGE_MAX", "量程上限:"), GUILayout.Width(105f));
            double newMax = MFPGuiSkin.DrawBufferedDoubleField($"max_{w.WidgetId}", w.MaxValue, 75f);
            if (Math.Abs(newMax - w.MaxValue) > 0.0001) { w.MaxValue = (float)newMax; MarkDirty(); }
            GUILayout.EndHorizontal();

            // Caution & Warning
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_CAUT", "<color=#FFB800>黄色注意门限:</color>"), GUILayout.Width(125f));
            double newCaution = MFPGuiSkin.DrawBufferedDoubleField($"caut_{w.WidgetId}", w.CautionThreshold, 75f);
            if (Math.Abs(newCaution - w.CautionThreshold) > 0.0001) { w.CautionThreshold = (float)newCaution; MarkDirty(); }

            GUILayout.Space(16f);

            GUILayout.Label(I18n.Tr("ASM_RANGE_WARN", "<color=#FF4D4D>红色危急门限:</color>"), GUILayout.Width(125f));
            double newWarn = MFPGuiSkin.DrawBufferedDoubleField($"warn_{w.WidgetId}", w.WarningThreshold, 75f);
            if (Math.Abs(newWarn - w.WarningThreshold) > 0.0001) { w.WarningThreshold = (float)newWarn; MarkDirty(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // Limit Mode
            string curLimit = string.IsNullOrEmpty(w.LimitMode) ? (w.IsSoftLimit ? "soft" : "hard") : w.LimitMode.ToLowerInvariant();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_LIMIT_MODE", "量程模式:"), GUILayout.Width(75f));
            if (GUILayout.Toggle(curLimit == "hard", I18n.Tr("ASM_LIMIT_HARD", "硬限幅"), "Button", GUILayout.Height(22f))) curLimit = "hard";
            if (GUILayout.Toggle(curLimit == "soft", I18n.Tr("ASM_LIMIT_SOFT", "软限幅"), "Button", GUILayout.Height(22f))) curLimit = "soft";
            if (GUILayout.Toggle(curLimit == "none", I18n.Tr("ASM_LIMIT_NONE", "无限制"), "Button", GUILayout.Height(22f))) curLimit = "none";
            GUILayout.EndHorizontal();

            if (w.LimitMode != curLimit)
            {
                w.LimitMode = curLimit;
                w.IsSoftLimit = (curLimit == "soft");
                MarkDirty();
            }

            // 单位
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("ASM_RANGE_UNIT", "单位标注:"), GUILayout.Width(105f));
            string newUnit = GUILayout.TextField(w.UnitLabel ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(80f));
            if (newUnit != w.UnitLabel) { w.UnitLabel = newUnit; MarkDirty(); }

            if (w.WidgetType == "tape")
            {
                GUILayout.Label(I18n.Tr("ASM_TAPE_STEP", "标尺步长:"), GUILayout.Width(65f));
                float newStep = MFPGuiSkin.DrawBufferedFloatField($"step_{w.WidgetId}", w.StepInterval, 60f);
                if (Math.Abs(newStep - w.StepInterval) > 0.01f) { w.StepInterval = newStep; MarkDirty(); }
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void DrawCardTemplateCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_TEMPLATE", "📝 遥测监控卡片模板"));

            string newTemplate = GUILayout.TextArea(w.CustomTemplate ?? "", GUILayout.Height(55f));
            if (newTemplate != w.CustomTemplate)
            {
                w.CustomTemplate = newTemplate;
                MarkDirty();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("ASM_TPL_DELIM", "+ ' | ' 分隔符"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                w.CustomTemplate = (w.CustomTemplate ?? "") + " | ";
                MarkDirty();
            }
            if (GUILayout.Button(I18n.Tr("ASM_TPL_NEWLINE", "+ 换行 \\n"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                w.CustomTemplate = (w.CustomTemplate ?? "") + "\n";
                MarkDirty();
            }
            if (GUILayout.Button(I18n.Tr("ASM_TPL_CLEAR", "清空模板"), MFPGuiSkin.StepperButtonStyle, GUILayout.Height(22f)))
            {
                w.CustomTemplate = "";
                MarkDirty();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 实时 4Hz 遥测解算预览
            if (Time.unscaledTime - _lastEvalTime > 0.25f || _cachedTemplate != w.CustomTemplate)
            {
                _lastEvalTime = Time.unscaledTime;
                _cachedTemplate = w.CustomTemplate;
                try
                {
                    _cachedEvaluation = TelemetryTokenEngine.Evaluate(w.CustomTemplate, TelemetryHub.Instance);
                    if (string.IsNullOrEmpty(_cachedEvaluation)) _cachedEvaluation = I18n.Tr("ASM_TPL_EMPTY", "<空模板>");
                }
                catch (Exception ex)
                {
                    _cachedEvaluation = I18n.TrFormat("ASM_TPL_SYNTAX_ERR", ex.Message);
                }
            }

            GUILayout.Label(I18n.Tr("ASM_LIVE_PREVIEW", "<b>🌟 航电真实遥测实时解算预览:</b>"));
            MFPGuiSkin.DrawBadge(_cachedEvaluation, MFPGuiSkin.AccentGreen, new Color(0.04f, 0.08f, 0.12f, 0.98f));

            MFPGuiSkin.EndCard();
        }

        private static void DrawCoreInfoCard(WidgetConfig w)
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CARD_CORE_TITLE", "⚙️ 核心内建飞行仪表组件"));
            GUILayout.Label($"{I18n.Tr("ASM_CORE_WIDGET_ID", "• 组件标识: ")}<color=#00E5FF>{w.WidgetId}</color>");
            GUILayout.Label($"{I18n.Tr("ASM_CORE_STATUS", "• 运行状态: ")}{(w.IsEnabled ? $"<color=#00FF88>{I18n.Tr("ASM_CORE_RUNNING", "● 正在运行")}</color>" : $"<color=#888888>{I18n.Tr("ASM_CORE_SUSPENDED", "○ 已挂起隐藏")}</color>")}");
            GUILayout.Label($"<color=#88AACC><size=11>{I18n.Tr("ASM_CORE_DESC", "核心组件包含底层管线逻辑 (如 3D 姿态球、滑动罗盘、SAS底座、工具栏坞)，位置与缩放可在上方直接调整或在屏幕拖拽。")}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawEmbeddedCatalogCard(WidgetConfig curWidget)
        {
            UpdateCatalogFilter();

            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("ASM_CATALOG_TITLE", "📖 遥测字典速查"), I18n.TrFormat("ASM_CATALOG_MATCHED", _filteredParams.Count));

            // 搜索框
            MFPGuiSkin.DrawSearchBar(ref _catalogSearchQuery, I18n.Tr("ASM_CATALOG_SEARCH", "搜索遥测参数 / 通配符..."));

            GUILayout.Space(4f);

            // 分类胶囊
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TelemetryCatalog.Categories.Length; i++)
            {
                bool isCat = (_catalogCatIndex == i);
                GUIStyle catStyle = isCat ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                string catShort = TelemetryCatalog.Categories[i].Split(' ')[0];
                if (GUILayout.Button(catShort, catStyle, GUILayout.Height(20f)))
                {
                    _catalogCatIndex = i;
                    _catalogPage = 0;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 分页参数列表
            int total = _filteredParams.Count;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)total / CatalogPageSize));
            int start = _catalogPage * CatalogPageSize;
            int end = Mathf.Min(start + CatalogPageSize, total);

            for (int i = start; i < end; i++)
            {
                var p = _filteredParams[i];
                DrawCatalogParamRow(p, curWidget);
            }

            if (total == 0)
            {
                GUILayout.Label($"<color=#778899><size=11>{I18n.Tr("ASM_CATALOG_NO_PARAM", "未找到匹配的参数")}</size></color>");
            }

            // 分页栏
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _catalogPage > 0;
            if (GUILayout.Button(I18n.Tr("ASM_PAGE_PREV", "◀ 上页"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(70f), GUILayout.Height(22f))) _catalogPage--;
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label(I18n.TrFormat("ASM_PAGE_INDICATOR", _catalogPage + 1, totalPages));
            GUILayout.FlexibleSpace();

            GUI.enabled = _catalogPage < totalPages - 1;
            if (GUILayout.Button(I18n.Tr("ASM_PAGE_NEXT", "下页 ▶"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(70f), GUILayout.Height(22f))) _catalogPage++;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void UpdateCatalogFilter()
        {
            if (_lastCatalogSearch == _catalogSearchQuery && _lastCatalogCatIndex == _catalogCatIndex) return;

            _lastCatalogSearch = _catalogSearchQuery;
            _lastCatalogCatIndex = _catalogCatIndex;
            _filteredParams.Clear();

            string cat = TelemetryCatalog.Categories[_catalogCatIndex];
            bool hasSearch = !string.IsNullOrEmpty(_catalogSearchQuery);

            for (int i = 0; i < TelemetryCatalog.Parameters.Count; i++)
            {
                var p = TelemetryCatalog.Parameters[i];
                if (_catalogCatIndex != 0 && p.Category != cat) continue;

                if (hasSearch)
                {
                    bool m = p.DisplayName.IndexOf(_catalogSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                          || p.Token.IndexOf(_catalogSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                          || p.Description.IndexOf(_catalogSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!m) continue;
                }
                _filteredParams.Add(p);
            }

            int maxPages = Mathf.Max(1, Mathf.CeilToInt((float)_filteredParams.Count / CatalogPageSize));
            if (_catalogPage >= maxPages) _catalogPage = 0;
        }

        private static void DrawCatalogParamRow(TelemetryParam p, WidgetConfig curWidget)
        {
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();

            GUILayout.Label($"<b>{p.DisplayName}</b>", GUILayout.Width(130f));
            MFPGuiSkin.DrawBadge(p.Token, MFPGuiSkin.AccentCyan, new Color(0.00f, 0.28f, 0.45f, 0.9f));

            GUILayout.FlexibleSpace();

            // 快捷填槽与绑定
            if (curWidget.WidgetType == "ecam_dial" || curWidget.WidgetType == "tape" || curWidget.WidgetType == "bar_gauge")
            {
                bool isBound = (curWidget.NumericToken == p.Token);
                string btnTxt = isBound ? I18n.Tr("ASM_BTN_BOUND", "✔ 已绑定") : I18n.Tr("ASM_BTN_BIND", "⚡ 绑定至此表盘");
                GUIStyle bStyle = isBound ? MFPGuiSkin.StepperButtonStyle : MFPGuiSkin.SuccessButtonStyle;

                if (GUILayout.Button(btnTxt, bStyle, GUILayout.Width(125f), GUILayout.Height(22f)))
                {
                    curWidget.NumericToken = p.Token;
                    curWidget.MinValue = (float)p.DefaultMin;
                    curWidget.MaxValue = (float)p.DefaultMax;
                    curWidget.CautionThreshold = (float)p.DefaultCaution;
                    curWidget.WarningThreshold = (float)p.DefaultWarning;
                    curWidget.IsSoftLimit = p.DefaultIsSoftLimit;
                    curWidget.LimitMode = p.DefaultIsSoftLimit ? "soft" : "hard";
                    curWidget.UnitLabel = p.DefaultUnit;
                    if (curWidget.WidgetType == "tape") curWidget.StepInterval = p.DefaultStep;

                    CommitPendingSaves();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.TrFormat("ASM_TOAST_BOUND", p.DisplayName));
                }
            }
            else if (curWidget.WidgetId.StartsWith("custom.") || curWidget.WidgetType == "custom")
            {
                if (GUILayout.Button(I18n.Tr("ASM_BTN_INSERT_TPL", "+ 插入模板"), MFPGuiSkin.WarningButtonStyle, GUILayout.Width(95f), GUILayout.Height(22f)))
                {
                    string prefix = string.IsNullOrEmpty(curWidget.CustomTemplate) ? "" : (curWidget.CustomTemplate.EndsWith(" ") ? "" : " | ");
                    curWidget.CustomTemplate = (curWidget.CustomTemplate ?? "") + prefix + $"{p.DisplayName}: {p.Token}";
                    CommitPendingSaves();
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.TrFormat("ASM_TOAST_INSERTED", p.DisplayName));
                }
            }
            else
            {
                if (GUILayout.Button(I18n.Tr("ASM_BTN_COPY", "📋 复制"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(60f), GUILayout.Height(22f)))
                {
                    GUIUtility.systemCopyBuffer = p.Token;
                    ShowToast(I18n.TrFormat("ASM_TOAST_COPIED", p.Token));
                }
            }

            GUILayout.EndHorizontal();

            // 实时求值与说明
            string liveVal = TelemetryTokenEngine.Evaluate(p.Token, TelemetryHub.Instance);
            GUILayout.Label($"<color=#7088A8><size=10>{p.Description} | {I18n.Tr("ASM_LIVE_READOUT", "当前实时读数: ")}<color=#00FF88>{liveVal}</color></size></color>");

            MFPGuiSkin.EndInset();
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
