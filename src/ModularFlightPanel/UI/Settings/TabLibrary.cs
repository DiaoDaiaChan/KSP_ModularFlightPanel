using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新航电组件库与预设发现中枢 (Avionics Component Library & Palette Hub)
    /// 核心升级：
    /// 1. 结构化分类与瞬时搜索检索 (Structured Categories & Instant Search)。
    /// 2. 智能放置与自动聚焦装配 (Smart Placement & Auto-Focus Workflow)：告别随机散落，添加后瞬间跳转至装配台聚焦调校！
    /// 3. 全量接入 MFPGuiSkin 现代黑晶航电卡片设计系统 2.0。
    /// </summary>
    public static class TabLibrary
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _searchQuery = "";
        private static int _categoryIndex = 0;
        private static readonly string[] Categories = new string[]
        {
            "全部组件",
            "通用仪表 (Gauges)",
            "态势导航 (Navigation)",
            "动力系统 (Systems)",
            "SpaceX 套件 (SpaceX)",
            "控制中枢 (Controls)"
        };

        private static string _toastMsg = "";
        private static float _toastTimer = 0f;
        private static int _spawnCounter = 0;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical(GUILayout.Height(SettingsGUI.ContentHeight));

            // 1. 顶部搜索栏与分类选项卡
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("📦 航电组件预设库 (AVIONICS COMPONENT LIBRARY)", "点击「+ 添加」即可智能生成并自动聚焦装配台");
            MFPGuiSkin.DrawSearchBar(ref _searchQuery, "搜索组件名称、标识或功能说明...");

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Categories.Length; i++)
            {
                bool isSel = (_categoryIndex == i);
                GUIStyle catStyle = isSel ? MFPGuiSkin.TabActiveStyle : MFPGuiSkin.TabInactiveStyle;
                if (GUILayout.Button(Categories[i], catStyle, GUILayout.Height(24f)))
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

            // 2. 通用仪表与标尺 (Gauges & Tapes)
            if (_categoryIndex == 0 || _categoryIndex == 1)
            {
                DrawSectionTitle("▼ 通用飞行仪表与标尺套件 (Generic Avionics Gauges)");

                DrawDialPresetItem("ECAM 圆弧通用仪表", "{SPD}", 0, 100, 70, 90, true, "m/s",
                    "🛠️ 270° 马蹄形高对比度圆弧表盘，支持动态指针、数显与软上限爆表模式。可在装配台绑定任意遥测通配符。");

                DrawTapePresetItem("PFD 垂直动态标尺带 (左侧/速度)", "{SPD}", true, 10f, "m/s",
                    "🛠️ PFD 风格平滑滚动动态标尺带（左侧布局），支持任意物理数据与步长。");

                DrawTapePresetItem("PFD 垂直动态标尺带 (右侧/高度)", "{ALT:AGL}", false, 50f, "m",
                    "🛠️ PFD 风格平滑滚动动态标尺带（右侧布局），支持真高/海高自由标定。");

                DrawCardPresetItem("多通道遥测卡片", "SPD: {SPD} | ALT: {ALT:ASL}",
                    "📝 多参数高对比度技术卡片，可在装配台内自由编写任意遥测通配符模板（如 {Q:F2}、{MACH}、{TWR} 等）。");

                GUILayout.Space(8f);
            }

            // 3. 态势导航 (Navigation & Flight Dynamics)
            if (_categoryIndex == 0 || _categoryIndex == 2)
            {
                DrawSectionTitle("▼ 态势感知与飞行导航 (Attitude & Navigation)");

                DrawCorePresetItem("3D 姿态球 (Modular Navball)", "core.navball",
                    "🌐 现代超清矢量/贴图 3D 姿态球核心，支持无极缩放、姿态导引十字与全量机动矢量。");

                DrawCorePresetItem("3D 飞船球形姿态仪 (Vessel 3D Navball)", "nav.vessel_navball",
                    "🚀 全新球形姿态仪：以真实 3D 飞船为中心，外层环绕 3D 姿态球体、人工地平标尺、SAS 目标飞行指引仪与全量导航矢量。");

                DrawCorePresetItem("PFD 航向指示标尺弧", "core.heading_arc",
                    "🧭 主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。");

                DrawSubsystemPresetItem("AERO ND 综合水平态势导航屏", "custom.nd_navigation", "nd_navigation", -440f, 25f,
                    "🧭 飞机航电综合水平态势显示器 (ND)，包含罗盘弧、测距环、飞机微标与航点航路。");

                DrawSubsystemPresetItem("MANEUVER 轨道机动节点指示器", "core.maneuver", "maneuver", 440f, 160f,
                    "🎯 实时机动节点指示器：剩余 Delta-V 进度条、节点倒计时、燃烧时长与一键推演。");

                DrawSubsystemPresetItem("MANEUVER 轨道机动时序与三轴矢量轴", "custom.maneuver_timeline", "maneuver_timeline", 0f, 260f,
                    "⏱️ 横排时间轴形式机动节点指示器：点火窗口时序轨、T0 节点与 Prograde/Normal/Radial 三轴矢量分解。");

                DrawCorePresetItem("ORBITAL 轨道动力学面板", "core.orbital_info",
                    "🌐 轨道力学四项精简读数面板：远地点 (AP)、近地点 (PE)、到达时间与轨道偏心率。");

                GUILayout.Space(8f);
            }

            // 4. 动力系统与机组告警 (Propulsion & Systems)
            if (_categoryIndex == 0 || _categoryIndex == 3)
            {
                DrawSectionTitle("▼ 动力推进与航电子系统 (Propulsion & Systems)");

                DrawSubsystemPresetItem("B747 EICAS 主发动机与机组告警显示", "custom.b747_eicas", "b747_eicas", -440f, 160f,
                    "✈️ 经典波音 747 四发主发动机 CRT：EPR/N1/EGT 四发柱状表、数字框显、TAT/推力模式与起落架状态。");

                DrawSubsystemPresetItem("B747 下部辅助发动机 EICAS", "custom.b747_lower_eicas", "b747_lower_eicas", -440f, -120f,
                    "✈️ 经典波音 747 四发下部系统 CRT：N2/N3 转速表条、燃油流量 FF、滑油压力/温度双轴游标表与震动监控。");

                DrawSubsystemPresetItem("STAGE 垂直分级时序序列仪", "custom.staging_sequence", "staging_sequence", -440f, 0f,
                    "🚀 垂直火箭分级序列仪：逐级剩余 ΔV、燃烧时间、推重比与单级推进剂微量程，重构原版左侧分级。");

                DrawSubsystemPresetItem("ROCKET 2D 垂直推进栈姿态卡", "custom.rocket", "rocket2d", 440f, 160f,
                    "🚀 多级火箭垂直推进栈、推进剂实时耗尽进度条、发动机工况与本级 dV。");

                DrawSubsystemPresetItem("ELEC 电力分配与电网系统", "custom.electrical", "electrical", -440f, 160f,
                    "⚡ 蓄电池电压、DC ESS 总线负荷、太阳能帆板与即时净充放电率 (EC/s)。");

                DrawSubsystemPresetItem("LIFE SUPPORT 维生消耗品监控", "custom.life", "life_support", -440f, -40f,
                    "🌱 乘员居住舱压环境、氧气/电力/RCS/维生消耗品 2x2 进度仪表。");

                DrawSubsystemPresetItem("COMMNET 天线通信网络", "custom.signal", "signal", 440f, -40f,
                    "📡 原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱。");

                DrawSubsystemPresetItem("SYS PERF 航电性能探针监控屏", "custom.perf_monitor", "performance_monitor", 440f, -40f,
                    "⚡ 实时监控 MFP 遥测、外部探针、组件渲染耗时与帧率 FPS，支持一键主干旁路。");

                DrawSubsystemPresetItem("中央主告警光字牌 (Master Warning)", "core.master_warning", "master_warning", 0f, -66f,
                    "🚨 双等级航电警告光字牌：黄色注意 (Caution) 与红色危急 (Warning) 双通道轮播，支持拉起、失速、低油、低电、缺氧全量监测，点击可消警。");

                GUILayout.Space(8f);
            }

            // 5. SpaceX 载人龙飞船与星舰套件 (SpaceX Suite)
            if (_categoryIndex == 0 || _categoryIndex == 4)
            {
                DrawSectionTitle("▼ SpaceX 载人龙飞船与星舰 HUD 套件 (SpaceX Crew Dragon Suite)");

                DrawSubsystemPresetItem("SPACEX 任务遥测顶栏 (Header)", "spacex.header", "spacex_header", 0f, 420f,
                    "🐉 SpaceX 顶部贯通式航电状态栏：主动飞行阶段胶囊徽章、倒计时与 5 组高对比度轨道数显列。");

                DrawSubsystemPresetItem("SPACEX 空间站对接与姿态准星 (Docking Reticle)", "spacex.docking", "spacex_docking", 0f, 170f,
                    "🎯 SpaceX ISS 空间站对接瞄准器：同心双环准星、3 轴姿态偏差角与角速度、测距接近率与 RCS 点亮。");

                DrawSubsystemPresetItem("SPACEX 综合工况与 ECLSS 面板 (Overview)", "spacex.overview", "spacex_overview", -460f, 120f,
                    "🌱 飞船综合工况与维生监控：客舱压力、氧分压、客舱温度、电网功率与气闸/推进剂/热控状态。");

                DrawSubsystemPresetItem("SPACEX 底部控制与链路药丸栏 (Bottom Bar)", "spacex.bottom", "spacex_bottom", 0f, -150f,
                    "🎮 SpaceX 底部药丸触控条：RCS/SAS/参考系/精细控制开关、指向模式与通信链路矩阵。");

                DrawSubsystemPresetItem("SPACEX 飞行关键时序甘特轴 (Timeline)", "spacex.timeline", "spacex_timeline", 0f, 320f,
                    "⏱️ 横排甘特式任务阶段进度标尺：MECO、分级、入轨、对接窗口各节点动态光标推进。");

                GUILayout.Space(8f);
            }

            // 6. 控制中枢与操纵扩展 (Controls & Dock)
            if (_categoryIndex == 0 || _categoryIndex == 5)
            {
                DrawSectionTitle("▼ 航电控制中枢与操纵扩展 (Controls & Dock Suites)");

                DrawCorePresetItem("UI 航电控制中枢 (UI Manager Widget)", "core.ui_widget",
                    "❖ 原生挂载在飞行屏幕上的 UGUI 高度集成管理仪表：实时组件列表、快速分类、一键显隐与自由拖拽联动。");

                DrawCorePresetItem("环形 SAS 模式选择罗盘", "core.sas_dial",
                    "🧭 10 向全功能快速 SAS 模式选择罗盘，带飞船实时滚转与级间剪影。");

                DrawCorePresetItem("AVIONICS 现代折叠工具栏收纳坞", "core.toolbar",
                    "📦 接管原版 20+ MOD 图标的超现代黑晶抽屉坞，彻底消灭屏幕长龙。");

                DrawCorePresetItem("操纵量指示与分级锁控制台", "core.stage_control",
                    "🎮 Pitch/Roll/Yaw 实时舵量标尺与分级安全锁定 (Alt+L) 防误触操作台。");

                DrawCorePresetItem("平滑时间加速控制器 (Time Warp)", "core.timewarp",
                    "⏩ 物理/轨道时间加速等级指示器与一键平滑倍率切换条。");

                DrawCorePresetItem("底部快捷操纵条 (Bottom Controls)", "core.bottom_controls",
                    "⚙ RCS/SAS/刹车/起落架/车灯综合药丸式状态切换条。");

                GUILayout.Space(8f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void DrawSectionTitle(string title)
        {
            GUILayout.Space(4f);
            GUILayout.Label($"<b><color=#00E5FF>{title}</color></b>");
        }

        private static bool FilterMatch(string title, string desc)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            return (title != null && title.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                || (desc != null && desc.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawDialPresetItem(string title, string token, double min, double max, double caution, double warning, bool isSoft, string unit, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00FF88><b>{title}</b></color> <color=#88AACC>[{token}]</color>", GUILayout.ExpandWidth(true));
            string limitTag = isSoft ? "软上限爆表" : "硬限幅";
            MFPGuiSkin.DrawBadge(limitTag, isSoft ? MFPGuiSkin.AccentCyan : MFPGuiSkin.AccentAmber, new Color(0.04f, 0.12f, 0.20f, 0.9f));

            if (GUILayout.Button("+ 添加到面板", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = GetSmartSpawnPosition();
                string newId = WidgetLayoutManager.Instance.AddEcamDialWidget(title, token, min, max, caution, warning, isSoft, unit, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc} (量程: {min:F0}~{max:F0}{unit})</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawTapePresetItem(string title, string token, bool isLeft, float step, string unit, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#88AACC>[{token}]</color>", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge(isLeft ? "左侧标尺" : "右侧标尺", Color.white, new Color(0.00f, 0.35f, 0.50f, 0.9f));

            if (GUILayout.Button("+ 添加到面板", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
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

        private static void DrawCardPresetItem(string title, string template, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#FFAA00><b>{title}</b></color>", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge("动态卡片", Color.white, new Color(0.45f, 0.30f, 0.05f, 0.9f));

            if (GUILayout.Button("+ 添加到面板", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                Vector2 pos = GetSmartSpawnPosition();
                string newId = WidgetLayoutManager.Instance.AddCustomWidget(title, template, pos);
                FlightHUDManager.Instance?.RebuildHUD();
                OnWidgetAdded(newId, title);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc}</size></color>");
            GUILayout.Label($"<color=#00FF88><size=10>模板: {template}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawCorePresetItem(string title, string widgetId, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            var cfg = WidgetLayoutManager.Instance.GetConfig(widgetId);
            bool isAdded = (cfg != null && cfg.IsEnabled);

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#FF88FF><b>{title}</b></color>", GUILayout.ExpandWidth(true));

            if (isAdded)
            {
                if (GUILayout.Button("● 运行中 (点击隐藏)", MFPGuiSkin.WarningButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    cfg.IsEnabled = false;
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast($"已隐藏「{title}」！");
                }
            }
            else
            {
                if (GUILayout.Button("+ 开启此核心组件", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    if (cfg != null)
                    {
                        cfg.IsEnabled = true;
                    }
                    else
                    {
                        cfg = new WidgetConfig(widgetId, title, 0f, 0f) { IsEnabled = true };
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                    }
                    FlightHUDManager.Instance?.RebuildHUD();
                    OnWidgetAdded(widgetId, title);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc}</size></color>");
            MFPGuiSkin.EndCard();
        }

        private static void DrawSubsystemPresetItem(string title, string widgetId, string widgetType, float defaultX, float defaultY, string desc)
        {
            if (!FilterMatch(title, desc)) return;

            var cfg = WidgetLayoutManager.Instance.GetConfig(widgetId);
            bool isAdded = (cfg != null && cfg.IsEnabled);

            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#7088A8>[{widgetType}]</color>", GUILayout.ExpandWidth(true));

            if (isAdded)
            {
                if (GUILayout.Button("● 运行中 (点击隐藏)", MFPGuiSkin.WarningButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    cfg.IsEnabled = false;
                    FlightHUDManager.Instance?.RebuildHUD();
                    ShowToast($"已隐藏「{title}」！");
                }
            }
            else
            {
                if (GUILayout.Button("+ 开启此子系统", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    if (cfg != null)
                    {
                        cfg.IsEnabled = true;
                        cfg.WidgetType = widgetType;
                    }
                    else
                    {
                        cfg = new WidgetConfig(widgetId, title, defaultX, defaultY)
                        {
                            IsEnabled = true,
                            WidgetType = widgetType
                        };
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                    }
                    FlightHUDManager.Instance?.RebuildHUD();
                    OnWidgetAdded(widgetId, title);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#8899AA><size=11>{desc}</size></color>");
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
            ShowToast($"✔ 已生成并聚焦「{title}」！");
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
