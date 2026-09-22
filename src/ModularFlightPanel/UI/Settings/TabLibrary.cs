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
        private static readonly string[] SubCategories = new string[] { "全部套件", "ECAM 圆弧仪表", "PFD 标尺带", "遥测监控卡片", "核心大字盒" };
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

            // 2. ECAM 圆弧仪表套件
            if (_subCategory == 0 || _subCategory == 1)
            {
                GUILayout.Label("<b>▼ ECAM 圆弧仪表套件 (ECAM Dial Gauges)</b>");

                DrawDialPreset("ECAM 15G 过载表", "{GFORCE}", 0, 15, 8, 12, true, "G",
                    "💥 15G 满格爆表模式，超过 15G 指针停驻满格告警，数字如实累加");

                DrawDialPreset("ECAM 动压 Q 监控表", "{Q}", 0, 40, 25, 35, true, "kPa",
                    "💨 大气动压上升段监测，Max-Q 极限压力告警");

                DrawDialPreset("ECAM 引擎推力表", "{THROTTLE}", 0, 100, 85, 100, false, "%",
                    "🔥 发动机输出总推力百分比 (0~100% 严格限幅截断)");

                DrawDialPreset("ECAM 推重比 TWR", "{TWR}", 0, 5, 3.5, 4.5, true, "",
                    "⚖️ 动力起降 TWR 实时指针，软上限 5.0 模式");

                DrawDialPreset("ECAM 推进剂存量表", "{PROP}", 0, 100, 20, 10, false, "%",
                    "⛽ 当前级推进剂剩余百分比，带 20% 琥珀告警与 10% 红色急危告警");

                DrawDialPreset("ECAM FAR 气动迎角表", "{FAR:AOA}", -15, 25, 15, 20, true, "°",
                    "🛩️ FAR 空气动力学即时迎角指针，带失速临界阈值告警");

                DrawDialPreset("ECAM KER 分级 dV 表", "{KER:DV}", 0, 3500, 2500, 3200, true, "m/s",
                    "🎯 Kerbal Engineer 实时解算当前级真空剩余 Delta-V");

                DrawDialPreset("ECAM 空白自定义表盘", "{SPD}", 0, 100, 70, 90, true, "",
                    "🛠️ 空白表盘底板，添加后可随意绑定任意遥测通配符");

                GUILayout.Space(10f);
            }

            // 3. PFD 标尺带套件
            if (_subCategory == 0 || _subCategory == 2)
            {
                GUILayout.Label("<b>▼ PFD 飞行姿态标尺带套件 (PFD Tape Gauges)</b>");

                DrawTapePreset("PFD 速度标尺带", "{SPD}", true, 10f, "m/s",
                    "🛫 左侧向右读数，步长 10m/s，实时呈现平滑滚动速度刻度");

                DrawTapePreset("PFD 海拔高度标尺带", "{ALT}", false, 100f, "m",
                    "🏔️ 右侧向左读数，步长 100m，实时呈现海平面海拔刻度带");

                DrawTapePreset("PFD 雷达真高标尺带", "{ALT:AGL}", false, 50f, "m",
                    "🌲 右侧向左读数，步长 50m，近地着陆探地真高精准刻度带");

                DrawTapePreset("PFD 垂直速度爬升带", "{VSI}", false, 5f, "m/s",
                    "📈 右侧向左读数，步长 5m/s，呈现垂直爬升与下沉速率");

                DrawTapePreset("PFD FAR 指示空速带", "{FAR:IAS}", true, 20f, "m/s",
                    "🛩️ 左侧向右读数，FAR 空气动力学指示空速 (IAS)");

                DrawTapePreset("PFD 空白标尺带", "{SPD}", true, 10f, "",
                    "🛠️ 自定义滚动标尺，可在装配台中自由指定驱动参数与刻度步长");

                GUILayout.Space(10f);
            }

            // 4. 遥测监控卡片套件
            if (_subCategory == 0 || _subCategory == 3)
            {
                GUILayout.Label("<b>▼ 遥测监控卡片套件 (Telemetry Cards)</b>");

                DrawCardPreset("综合巡航监控卡", "动压: {Q:F2} | 马赫: {MACH} | G力: {GFORCE}",
                    "✈️ 大气层与超音速巡航核心指标");

                DrawCardPreset("动力与推进监控卡", "TWR: {TWR:F2} | 燃料: {PROP} | 油门: {THROTTLE}",
                    "🚀 发动机工况、推重比与推进剂余量");

                DrawCardPreset("轨道机动监控卡", "远地点: {AP:DIST} | 近地点: {PE:DIST} | 到AP: {TAP}",
                    "🌐 远近地点与变轨倒计时卡片");

                DrawCardPreset("航电设备监控卡", "SAS: {SAS} | RCS: {RCS} | 参考系: {FRAME}",
                    "🧭 自动稳定仪、姿控喷气与当前导航参考系");

                DrawCardPreset("FAR 气动参数卡", "IAS: {FAR:IAS} | AoA: {FAR:AOA} | 动压: {FAR:Q} | 失速: {FAR:STALL}",
                    "📡 FAR 权威迎角、动压与失速百分比");

                DrawCardPreset("KER 深度分级卡", "本级 dV: {KER:DV} | 总 dV: {KER:TOTALDV} | TWR: {KER:TWR}",
                    "🎯 Kerbal Engineer 专业级分级推进动力学数据");

                DrawCardPreset("MechJeb 飞行卡", "本级 dV: {MJ:DV} | 终端速度: {MJ:TERMINALVEL} | TWR: {MJ:TWR}",
                    "🤖 MechJeb 动力状态机解算结果");

                DrawCardPreset("空白自定义卡片", "参数1: {SPD} | 参数2: {ALT:ASL}",
                    "📝 空白多行卡片，支持自由通配符装配");

                GUILayout.Space(10f);
            }

            // 5. 核心大字数显盒
            if (_subCategory == 0 || _subCategory == 4)
            {
                GUILayout.Label("<b>▼ 核心大字数显盒 (Digital Readout Boxes)</b>");

                DrawCoreBoxPreset("速度大字读数盒", "core.speed_box", "⚡ 原生高保真三模速度读数盒 (地表/轨道/目标)");
                DrawCoreBoxPreset("高度大字读数盒", "core.alt_box", "🏔️ 原生高保真双模高度读数盒 (海拔/真高)");
                DrawCoreBoxPreset("轨道数据面板", "core.orbital_info", "🌐 原生紧凑型轨道力学四项读数面板");
                DrawCoreBoxPreset("环形 SAS 罗盘", "core.sas_dial", "🧭 10向全功能快速 SAS 模式选择罗盘");
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

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
