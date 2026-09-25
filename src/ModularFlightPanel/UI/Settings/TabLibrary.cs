using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新航电组件库与预设发现中枢 (Avionics Component Library & Palette Hub)
    /// 核心架构升级：
    /// 1. 结构化分类与瞬时搜索检索 (Structured Categories & Instant Search)。
    /// 2. 彻底解耦硬编码，基于 WidgetRegistry 全自动反射发现 (Auto-Discovery) 动态呈现全量组件。
    /// 3. 保留通用仪表 6 大参数化生成器 (ECAM Dial, Speed/Alt Tapes, Curved HUD Tapes, Custom Cards)。
    /// 4. 智能放置与自动聚焦装配 (Smart Placement & Auto-Focus Workflow)：添加后瞬间跳转至装配台聚焦调校！
    /// 5. 全量接入 MFPGuiSkin 现代黑晶航电卡片设计系统 2.0。
    /// </summary>
    public static class TabLibrary
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _searchQuery = "";
        private static int _categoryIndex = 0;

        private static string GetCategoryName(int index)
        {
            switch (index)
            {
                case 0: return I18n.Tr("LIB_CAT_ALL", "全部组件");
                case 1: return I18n.Tr("LIB_CAT_GAUGES", "通用仪表");
                case 2: return I18n.Tr("LIB_CAT_NAV", "态势导航");
                case 3: return I18n.Tr("LIB_CAT_SYSTEMS", "动力系统");
                case 4: return I18n.Tr("LIB_CAT_SPACEX", "SpaceX 套件");
                case 5: return I18n.Tr("LIB_CAT_CONTROLS", "控制中枢");
                default: return "All";
            }
        }

        private static string _toastMsg = "";
        private static float _toastTimer = 0f;
        private static int _spawnCounter = 0;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical(GUILayout.Height(SettingsGUI.ContentHeight));

            // 1. 顶部搜索栏与分类选项卡
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("LIB_HEADER", "📦 航电组件预设库"),
                I18n.Tr("LIB_SUBHEADER", "点击「+ 添加」即可智能生成并自动聚焦装配台"));
            MFPGuiSkin.DrawSearchBar(ref _searchQuery, I18n.Tr("LIB_SEARCH_PLACEHOLDER", "搜索组件名称、标识或功能说明..."));

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < 6; i++)
            {
                bool isSel = (_categoryIndex == i);
                GUIStyle catStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(GetCategoryName(i), catStyle, GUILayout.Height(24f)))
                {
                    _categoryIndex = i;
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(4f);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight - 120f));

            // Toast 提示 (置于滚动视图内部，杜绝浮动撑大固定外框)
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            // 2. 通用仪表生成器与标尺套件 (Gauges & Tapes Generators)
            if (_categoryIndex == 0 || _categoryIndex == 1)
            {
                DrawSectionTitle(I18n.Tr("LIB_SECTION_GAUGE_GENERATORS", "▼ 参数化仪表生成器 (可定制量程与数据源)"));

                DrawDialPresetItem(I18n.Tr("LIB_ITEM_ECAM_SPD", "ECAM 圆弧通用仪表"), "{SPD}", 0, 100, 70, 90, true, "m/s",
                    I18n.Tr("LIB_DESC_ECAM_SPD", "🛠️ 270° 马蹄形高对比度圆弧表盘，支持动态指针、数显与软上限爆表模式。可在装配台绑定任意遥测通配符。"));

                DrawTapePresetItem(I18n.Tr("LIB_ITEM_TAPE_SPD", "PFD 垂直动态标尺带 (左侧/速度)"), "{SPD}", true, 10f, "m/s",
                    I18n.Tr("LIB_DESC_TAPE_SPD", "🛠️ PFD 风格平滑滚动动态标尺带（左侧布局），支持任意物理数据与步长。"));

                DrawTapePresetItem(I18n.Tr("LIB_ITEM_TAPE_ALT", "PFD 垂直动态标尺带 (右侧/高度)"), "{ALT:AGL}", false, 50f, "m",
                    I18n.Tr("LIB_DESC_TAPE_ALT", "🛠️ PFD 风格平滑滚动动态标尺带（右侧布局），支持真高/海高自由标定。"));

                DrawArcTapePresetItem(I18n.Tr("LIB_ITEM_ARC_SPEED", "HUD 弧形速度带 (可调曲率)"), "{SPD}", 0.5f, 200f, 80f, true,
                    I18n.Tr("LIB_DESC_ARC_SPEED", "✈️ 次世代战斗机 HUD / 环抱式玻璃座舱弧形速度带，支持通过参数自由调整弯曲半径与弧度，带动态量纲与过载变化率。"));

                DrawArcTapePresetItem(I18n.Tr("LIB_ITEM_ARC_ALT", "HUD 弧形高度带 (可调曲率)"), "{ALT}", 0.5f, 200f, 80f, false,
                    I18n.Tr("LIB_DESC_ARC_ALT", "🏔️ 次世代战斗机 HUD / 环抱式玻璃座舱弧形高度带，支持通过参数自由调整弯曲半径与弧度，带垂直升降率 (VSI) 与贴地雷达防撞警示。"));

                DrawCardPresetItem(I18n.Tr("LIB_ITEM_CARD_MULTI", "多通道遥测卡片"), "SPD: {SPD} | ALT: {ALT:ASL}",
                    I18n.Tr("LIB_DESC_CARD_MULTI", "📝 多参数高对比度技术卡片，可在装配台内自由编写任意遥测通配符模板（如 {Q:F2}、{MACH}、{TWR} 等）。"));

                GUILayout.Space(8f);

                DrawCategoryWidgets(WidgetCategory.Gauges, I18n.Tr("LIB_SECTION_GAUGES", "▼ 通用飞行仪表与标尺套件"));
            }

            // 3. 态势导航 (Navigation & Flight Dynamics)
            if (_categoryIndex == 0 || _categoryIndex == 2)
            {
                DrawCategoryWidgets(WidgetCategory.Navigation, I18n.Tr("LIB_SECTION_NAV", "▼ 态势感知与飞行导航"));
            }

            // 4. 动力系统与机组告警 (Propulsion & Systems)
            if (_categoryIndex == 0 || _categoryIndex == 3)
            {
                DrawCategoryWidgets(WidgetCategory.Systems, I18n.Tr("LIB_SECTION_SYSTEMS", "▼ 动力推进与航电子系统"));
            }

            // 5. SpaceX 载人龙飞船与星舰套件 (SpaceX Suite)
            if (_categoryIndex == 0 || _categoryIndex == 4)
            {
                DrawCategoryWidgets(WidgetCategory.SpaceX, I18n.Tr("LIB_SECTION_SPACEX", "▼ SpaceX 载人龙飞船与星舰 HUD 套件"));
            }

            // 6. 控制中枢与操纵扩展 (Controls & Dock)
            if (_categoryIndex == 0 || _categoryIndex == 5)
            {
                DrawCategoryWidgets(WidgetCategory.Controls, I18n.Tr("LIB_SECTION_CONTROLS", "▼ 航电控制中枢与操纵扩展"));
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void DrawSectionTitle(string title)
        {
            GUILayout.Space(4f);
            GUILayout.Label($"<b><color=#00E5FF>{title}</color></b>");
        }

        private static bool FilterMatch(string title, string desc, string typeName = null, string widgetId = null)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            if (title != null && title.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (desc != null && desc.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (typeName != null && typeName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (widgetId != null && widgetId.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void DrawCategoryWidgets(WidgetCategory category, string sectionTitle)
        {
            var descriptors = WidgetRegistry.AllDescriptors;
            bool titleDrawn = false;

            for (int i = 0; i < descriptors.Count; i++)
            {
                var desc = descriptors[i];
                if (desc.Category != category) continue;

                string title = desc.GetLocalizedDisplayName();
                string descText = desc.GetLocalizedDescription();

                if (!FilterMatch(title, descText, desc.TypeName, desc.DefaultWidgetId)) continue;

                if (!titleDrawn)
                {
                    DrawSectionTitle(sectionTitle);
                    titleDrawn = true;
                }

                DrawDescriptorItem(desc, title, descText);
            }

            if (titleDrawn)
            {
                GUILayout.Space(8f);
            }
        }

        private static void DrawDescriptorItem(WidgetDescriptor desc, string title, string descText)
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();

            string colorHex = GetCategoryColorHex(desc.Category);
            GUILayout.Label($"<color={colorHex}><b>{title}</b></color> <color=#7088A8>[{desc.TypeName}]</color>", GUILayout.ExpandWidth(true));

            // 检查当前布局中是否已激活该组件
            var layout = WidgetLayoutManager.Instance?.CurrentLayout;
            WidgetConfig activeCfg = null;
            if (layout != null && layout.Widgets != null)
            {
                // 1. 优先按 DefaultWidgetId 查找
                if (!string.IsNullOrEmpty(desc.DefaultWidgetId))
                {
                    activeCfg = layout.Widgets.Find(w => string.Equals(w.WidgetId, desc.DefaultWidgetId, StringComparison.OrdinalIgnoreCase));
                }
                // 2. 若未找到且是单例，按 WidgetType 查找
                if (activeCfg == null && desc.IsSingleton && !string.IsNullOrEmpty(desc.TypeName))
                {
                    activeCfg = layout.Widgets.Find(w => string.Equals(w.WidgetType, desc.TypeName, StringComparison.OrdinalIgnoreCase));
                }
            }

            bool isAdded = (activeCfg != null && activeCfg.IsEnabled);

            if (isAdded)
            {
                if (GUILayout.Button(I18n.Tr("LIB_RUNNING_HIDE", "● 运行中 (点击隐藏)"), MFPGuiSkin.WarningButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    activeCfg.IsEnabled = false;
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast(I18n.TrFormat("LIB_TOAST_HIDDEN", "已隐藏「{0}」！", title));
                }
            }
            else
            {
                string btnText = desc.IsSingleton
                    ? I18n.Tr("LIB_ENABLE_CORE", "+ 开启此核心组件")
                    : I18n.Tr("LIB_ADD_TO_PANEL", "+ 添加到面板");

                GUIStyle btnStyle = desc.IsSingleton ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SuccessButtonStyle;

                if (GUILayout.Button(btnText, btnStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    if (activeCfg != null)
                    {
                        activeCfg.IsEnabled = true;
                    }
                    else
                    {
                        // 若不是单例且已存在同 ID，生成递增 ID
                        string newId = desc.DefaultWidgetId;
                        if (!desc.IsSingleton && layout != null && layout.Widgets != null)
                        {
                            int count = 1;
                            while (layout.Widgets.Exists(w => string.Equals(w.WidgetId, newId, StringComparison.OrdinalIgnoreCase)))
                            {
                                newId = $"{desc.TypeName}_{count++}";
                            }
                        }

                        activeCfg = desc.CreateConfig(newId);
                        if (!desc.IsSingleton)
                        {
                            activeCfg.PositionX = GetSmartSpawnPosition().x;
                            activeCfg.PositionY = GetSmartSpawnPosition().y;
                        }
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(activeCfg);
                    }

                    FlightHUDManager.Instance?.RebuildHUD();
                    OnWidgetAdded(activeCfg.WidgetId, title);
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(descText))
            {
                GUILayout.Label($"<color=#8899AA><size=11>{descText}</size></color>");
            }
            MFPGuiSkin.EndCard();
        }

        private static string GetCategoryColorHex(WidgetCategory cat)
        {
            switch (cat)
            {
                case WidgetCategory.Gauges: return "#00E5FF";
                case WidgetCategory.Navigation: return "#FF88FF";
                case WidgetCategory.Systems: return "#00FF88";
                case WidgetCategory.SpaceX: return "#FFAA00";
                case WidgetCategory.Controls: return "#66CCFF";
                default: return "#FFFFFF";
            }
        }

        private static void DrawDialPresetItem(string title, string token, double min, double max, double caution, double warning, bool isSoft, string unit, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00FF88><b>{title}</b></color> <color=#88AACC>[{token}]</color>", GUILayout.ExpandWidth(true));
            string limitTag = isSoft ? I18n.Tr("LIB_SOFT_LIMIT", "软上限爆表") : I18n.Tr("LIB_HARD_LIMIT", "硬限幅");
            MFPGuiSkin.DrawBadge(limitTag, isSoft ? MFPGuiSkin.AccentCyan : MFPGuiSkin.AccentAmber, new Color(0.04f, 0.12f, 0.20f, 0.9f));

            if (GUILayout.Button(I18n.Tr("LIB_ADD_TO_PANEL", "+ 添加到面板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = GetSmartSpawnPosition();
                string newId = WidgetLayoutManager.Instance.AddEcamDialWidget(title, token, min, max, caution, warning, isSoft, unit, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            string rangeStr = I18n.TrFormat("LIB_RANGE_FMT", " (量程: {0:F0}~{1:F0}{2})", min, max, unit);
            GUILayout.Label($"<color=#8899AA><size=11>{desc}{rangeStr}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawTapePresetItem(string title, string token, bool isLeft, float step, string unit, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#88AACC>[{token}]</color>", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge(isLeft ? I18n.Tr("LIB_LEFT_TAPE", "左侧标尺") : I18n.Tr("LIB_RIGHT_TAPE", "右侧标尺"), Color.white, new Color(0.00f, 0.35f, 0.50f, 0.9f));

            if (GUILayout.Button(I18n.Tr("LIB_ADD_TO_PANEL", "+ 添加到面板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = isLeft ? new Vector2(-235f, 0f) : new Vector2(235f, 0f);
                string newId = WidgetLayoutManager.Instance.AddTapeWidget(title, token, isLeft, step, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawArcTapePresetItem(string title, string token, float curvature, float radius, float span, bool isLeft, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#88AACC>[{token}]</color>", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge(I18n.Tr("LIB_ARC_TAPE", "弧形标尺"), Color.white, new Color(0.00f, 0.40f, 0.60f, 0.9f));

            if (GUILayout.Button(I18n.Tr("LIB_ADD_TO_PANEL", "+ 添加到面板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = isLeft ? new Vector2(-235f, 0f) : new Vector2(235f, 0f);
                string newId = isLeft
                    ? WidgetLayoutManager.Instance.AddArcSpeedTapeWidget(title, token, curvature, radius, span, isLeft, pos)
                    : WidgetLayoutManager.Instance.AddArcAltitudeTapeWidget(title, token, curvature, radius, span, isLeft, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawCardPresetItem(string title, string template, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#FFAA00><b>{title}</b></color>", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge(I18n.Tr("LIB_DYNAMIC_CARD", "动态卡片"), Color.white, new Color(0.45f, 0.30f, 0.05f, 0.9f));

            if (GUILayout.Button(I18n.Tr("LIB_ADD_TO_PANEL", "+ 添加到面板"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = GetSmartSpawnPosition();
                string newId = WidgetLayoutManager.Instance.AddCustomWidget(title, template, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            string tplStr = I18n.TrFormat("LIB_TEMPLATE_LABEL", "模板: {0}", template);
            GUILayout.Label($"<color=#00FF88><size=10>{tplStr}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static Vector2 GetSmartSpawnPosition()
        {
            // 基于级进偏移计算智能生成位置，绝不再使用混乱的随机数
            _spawnCounter++;
            float offsetX = ((_spawnCounter % 5) - 2) * 45f;
            float offsetY = ((_spawnCounter % 3) - 1) * 35f;
            return new Vector2(offsetX, 60f + offsetY);
        }

        private static void OnWidgetAdded(string widgetId, string title)
        {
            WidgetLayoutManager.Instance.SaveLayout();
            TabAssembler.SetSelectedWidget(widgetId);
            SettingsGUI.Instance?.SwitchTab(1); // 自动无缝切换到装配台，开启极速调校心流！
            ShowToast(I18n.TrFormat("LIB_TOAST_SPAWNED", "✔ 已生成并聚焦「{0}」！", title));
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
