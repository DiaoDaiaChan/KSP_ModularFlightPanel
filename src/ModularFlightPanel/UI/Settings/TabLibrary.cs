using System;
using UnityEngine;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 航电组件库与预设模板选择器 (Palette & Templates)
    /// </summary>
    public static class TabLibrary
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static int _subCategory = 0;
        private static readonly string[] SubCategories = new string[] { "全部组件", "通用仪表与卡片", "航电子系统面板", "核心扩展组件" };
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void Draw()
        {
            GUILayout.BeginVertical();

            // 1. 顶部子分类筛选
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>套件类型:</b>", GUILayout.Width(65f));
            for (int i = 0; i < SubCategories.Length; i++)
            {
                bool isSel = _subCategory == i;
                GUI.color = isSel ? Color.cyan : Color.white;
                if (GUILayout.Button(SubCategories[i], GUILayout.Height(24f)))
                {
                    _subCategory = i;
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // Toast 提示
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toastMsg))
            {
                _toastTimer -= Time.deltaTime;
                GUI.color = Color.green;
                GUILayout.Label($"<b>✔ {_toastMsg}</b>");
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label("<color=#AAAAAA><size=11>点击任意组件右侧的「+ 添加到面板」即可直接在屏幕上生成，并可在装配台或拖拽模式下自由摆放与调校。</size></color>");
            }

            GUILayout.Space(6f);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 2. 通用仪表与卡片套件 (Generic Gauges & Cards)
            if (_subCategory == 0 || _subCategory == 1)
            {
                GUILayout.Label("<b>▼ 通用飞行仪表与卡片套件 (Generic Avionics Gauges)</b>");

                DrawDialPreset("ECAM 圆弧通用仪表", "{SPD}", 0, 100, 70, 90, true, "",
                    "🛠️ 270° 马蹄形高对比度圆弧表盘，带动态指针、数显与软上限爆表模式。添加后可在装配台中自由绑定任意遥测通配符并设置量程。");

                DrawTapePreset("PFD 垂直通用标尺带", "{SPD}", true, 10f, "m/s",
                    "🛠️ PFD 风格平滑滚动动态标尺带，添加后可自由指定为左侧/右侧方向、绑定任意遥测数据与刻度步长。");

                DrawCardPreset("遥测通配符卡片", "参数1: {SPD} | 参数2: {ALT:ASL}",
                    "📝 多参数高对比度技术卡片，可在装配台内自由编写任意遥测通配符模板（如 {Q:F2}、{MACH}、{TWR} 等）。");

                GUILayout.Space(10f);
            }

            // 3. 航电子系统面板套件 (Avionics Subsystems)
            if (_subCategory == 0 || _subCategory == 2)
            {
                GUILayout.Label("<b>▼ 航电子系统原生组件 (Avionics Subsystems)</b>");

                DrawSubsystemPreset("B747 EICAS 发动机与机组告警显示", "custom.b747_eicas", "b747_eicas", -440f, 160f,
                    "✈️ 经典波音 747 四发主发动机 CRT：EPR/N1/EGT 四发柱状表、数字框显、TAT/推力模式、起落架与客舱/燃油状态");

                DrawSubsystemPreset("B747 下部辅助发动机 EICAS", "custom.b747_lower_eicas", "b747_lower_eicas", -440f, -120f,
                    "✈️ 经典波音 747 四发下部系统 CRT：N2/N3 转速表条、燃油流量 FF、滑油压力/温度双轴游标表、滑油量与震动监控");

                DrawSubsystemPreset("ELEC 电力分配系统", "custom.electrical", "electrical", -440f, 160f,
                    "⚡ 蓄电池电压、DC ESS 总线负荷、太阳能帆板与即时净充放电率 (EC/s)");

                DrawSubsystemPreset("ROCKET 2D 分级姿态卡", "custom.rocket", "rocket2d", 440f, 160f,
                    "🚀 多级火箭垂直推进栈、推进剂实时耗尽进度条、发动机工况与本级 dV");

                DrawSubsystemPreset("LIFE SUPPORT 维生监控卡", "custom.life", "life_support", -440f, -40f,
                    "🌱 乘员居住舱压环境、氧气/电力/RCS/维生消耗品 2x2 进度仪表");

                DrawSubsystemPreset("COMMNET 天线通信网络", "custom.signal", "signal", 440f, -40f,
                    "📡 原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱");

                DrawSubsystemPreset("AERO ND 综合导航屏", "custom.nd_navigation", "nd_navigation", -440f, 25f,
                    "🧭 飞机航电综合水平态势显示器 (Set 1)，包含罗盘弧、测距环、飞机微标与航点航路");

                DrawSubsystemPreset("MANEUVER 机动节点指示器", "core.maneuver", "maneuver", 440f, 160f,
                    "🎯 实时机动节点指示器：剩余 Delta-V 进度条、节点倒计时、燃烧时长预估、点火倒计时与一键时间加速/删除");

                DrawSubsystemPreset("SPACEX 载人龙飞船任务遥测顶栏", "spacex.header", "spacex_header", 0f, 420f,
                    "🐉 SpaceX Crew Dragon 顶部贯通式航电状态栏：主动飞行阶段胶囊徽章、倒计时与 5 组高对比度轨道数显列");

                DrawSubsystemPreset("SPACEX 对接与姿态准星 HUD", "spacex.docking", "spacex_docking", 0f, 170f,
                    "🎯 SpaceX ISS 空间站对接瞄准器：同心双环准星、3 轴姿态偏差角与角速度、测距接近率与 RCS 喷管脉冲点亮");

                DrawSubsystemPreset("SPACEX 综合工况与 ECLSS 面板", "spacex.overview", "spacex_overview", -460f, 120f,
                    "🌱 飞船综合工况与维生监控：客舱压力、氧分压、客舱温度、电网功率与气闸/推进剂/热控/对接口状态矩阵");

                DrawSubsystemPreset("SPACEX 底部控制与链路栏", "spacex.bottom", "spacex_bottom", 0f, -150f,
                    "🎮 SpaceX 底部药丸触控条：RCS/SAS/参考系/精细控制开关、指向模式与 SPX GND/TDRS/ISS 通信链路矩阵");

                DrawSubsystemPreset("SYS PERF 航电性能探针监控屏", "custom.perf_monitor", "performance_monitor", 440f, -40f,
                    "⚡ 实时监控 MFP 遥测、外部探针、组件渲染、飞船剪影耗时与物理帧率 FPS，支持一键主干旁路 (Master Bypass)");

                GUILayout.Space(10f);
            }

            // 4. 核心扩展组件 (Core Extensions)
            if (_subCategory == 0 || _subCategory == 3)
            {
                GUILayout.Label("<b>▼ 核心扩展组件 (Core Extensions)</b>");

                DrawCoreBoxPreset("ORBITAL 轨道数据面板", "core.orbital_info", "🌐 原生紧凑型轨道力学四项读数面板 (AP, PE, Time to AP/PE)");
                DrawCoreBoxPreset("环形 SAS 罗盘", "core.sas_dial", "🧭 10向全功能快速 SAS 模式选择罗盘，带飞船实时滚转剪影");

                GUILayout.Space(10f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void DrawDialPreset(string title, string token, double min, double max, double caution, double warning, bool isSoft, string unit, string desc)
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00FF88><b>{title}</b></color> <color=#888888>[{token}]</color>", GUILayout.ExpandWidth(true));
            string limitTag = isSoft ? "<color=#00E5FF>[软上限爆表]</color>" : "<color=#FFAA00>[有上限限幅]</color>";
            GUILayout.Label(limitTag, GUILayout.Width(95f));

            if (GUILayout.Button("+ 添加到面板", GUILayout.Width(100f), GUILayout.Height(22f)))
            {
                Vector2 pos = new Vector2(UnityEngine.Random.Range(-320f, 320f), UnityEngine.Random.Range(30f, 160f));
                WidgetLayoutManager.Instance.AddEcamDialWidget(title, token, min, max, caution, warning, isSoft, unit, pos);
                NavballHUD.Instance.RebuildHUD();
                ShowToast($"已生成「{title}」！");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{desc} (量程: {min:F0}~{max:F0}{unit})</size></color>");
            GUILayout.EndVertical();
        }

        private static void DrawTapePreset(string title, string token, bool isLeft, float step, string unit, string desc)
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#888888>[{token}]</color>", GUILayout.ExpandWidth(true));
            string orientTag = isLeft ? "<color=#FFAA00>[左侧带]</color>" : "<color=#00FF66>[右侧带]</color>";
            GUILayout.Label(orientTag, GUILayout.Width(70f));

            if (GUILayout.Button("+ 添加到面板", GUILayout.Width(100f), GUILayout.Height(22f)))
            {
                Vector2 pos = isLeft ? new Vector2(-235f, UnityEngine.Random.Range(-30f, 30f)) : new Vector2(235f, UnityEngine.Random.Range(-30f, 30f));
                WidgetLayoutManager.Instance.AddTapeWidget(title, token, isLeft, step, pos);
                NavballHUD.Instance.RebuildHUD();
                ShowToast($"已生成「{title}」！");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{desc}</size></color>");
            GUILayout.EndVertical();
        }

        private static void DrawCardPreset(string title, string template, string desc)
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#FFAA00><b>{title}</b></color>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("+ 添加到面板", GUILayout.Width(100f), GUILayout.Height(22f)))
            {
                Vector2 pos = new Vector2(UnityEngine.Random.Range(-250f, 250f), UnityEngine.Random.Range(80f, 220f));
                WidgetLayoutManager.Instance.AddCustomWidget(title, template, pos);
                NavballHUD.Instance.RebuildHUD();
                ShowToast($"已生成「{title}」！");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{desc}</size></color>");
            GUILayout.Label($"<color=#66FF88><size=10>模板: {template}</size></color>");
            GUILayout.EndVertical();
        }

        private static void DrawCoreBoxPreset(string title, string widgetId, string desc)
        {
            var cfg = WidgetLayoutManager.Instance.GetConfig(widgetId);
            bool isAdded = cfg != null && cfg.IsEnabled;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#FF88FF><b>{title}</b></color>", GUILayout.ExpandWidth(true));

            if (isAdded)
            {
                GUI.color = Color.yellow;
                if (GUILayout.Button("已在面板上 (点击隐藏)", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    cfg.IsEnabled = false;
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已隐藏「{title}」！");
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("+ 开启此核心组件", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    if (cfg != null)
                    {
                        cfg.IsEnabled = true;
                    }
                    else
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(new WidgetConfig(widgetId, title, 0f, 0f) { IsEnabled = true });
                    }
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已启用「{title}」！");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{desc}</size></color>");
            GUILayout.EndVertical();
        }

        private static void DrawSubsystemPreset(string title, string widgetId, string widgetType, float defaultX, float defaultY, string desc)
        {
            var cfg = WidgetLayoutManager.Instance.GetConfig(widgetId);
            bool isAdded = cfg != null && cfg.IsEnabled;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#00E5FF><b>{title}</b></color> <color=#888888>[{widgetType}]</color>", GUILayout.ExpandWidth(true));

            if (isAdded)
            {
                GUI.color = Color.yellow;
                if (GUILayout.Button("已在面板上 (点击隐藏)", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    cfg.IsEnabled = false;
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已隐藏「{title}」！");
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("+ 开启此子系统", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    if (cfg != null)
                    {
                        cfg.IsEnabled = true;
                        cfg.WidgetType = widgetType;
                    }
                    else
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(new WidgetConfig(widgetId, title, defaultX, defaultY)
                        {
                            IsEnabled = true,
                            WidgetType = widgetType
                        });
                    }
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已启用「{title}」！");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{desc}</size></color>");
            GUILayout.EndVertical();
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
