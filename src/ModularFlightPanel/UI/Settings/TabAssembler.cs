using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 可视化遥测装配工作台 (Visual Token Assembler & Widget Inspector)
    /// 用户无需手动输入代码或文本，在右侧参数菜单中一键点击即可绑定到表盘槽位或装配到卡片模板，并实时预览航电求值效果
    /// </summary>
    public static class TabAssembler
    {
        private static Vector2 _leftScroll = Vector2.zero;
        private static Vector2 _rightScroll = Vector2.zero;
        private static string _searchQuery = "";
        private static int _selectedCategoryIndex = 0;
        private static int _selectedWidgetIndex = 0;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void SetSelectedWidget(string widgetId)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout.Widgets;
            for (int i = 0; i < widgets.Count; i++)
            {
                if (widgets[i].WidgetId == widgetId)
                {
                    _selectedWidgetIndex = i;
                    break;
                }
            }
        }

        public static void Draw()
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout.Widgets;
            if (widgets == null || widgets.Count == 0)
            {
                GUILayout.Label("当前没有任何组件，请在「航电组件库」中先添加组件。");
                return;
            }

            if (_selectedWidgetIndex >= widgets.Count) _selectedWidgetIndex = 0;
            var curWidget = widgets[_selectedWidgetIndex];

            // 顶部横幅提示
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toastMsg))
            {
                _toastTimer -= Time.deltaTime;
                GUI.color = Color.green;
                GUILayout.Label($"<b>✔ {_toastMsg}</b>");
                GUI.color = Color.white;
            }

            GUILayout.BeginHorizontal();

            // ----------------------------------------------------
            // 左侧：组件装配槽位与实时预览 (Left Panel: Slot & Inspector)
            // ----------------------------------------------------
            GUILayout.BeginVertical("box", GUILayout.Width(460f), GUILayout.ExpandHeight(true));
            _leftScroll = GUILayout.BeginScrollView(_leftScroll);

            GUILayout.Label("<b>🛠️ 当前选中装配组件:</b>");

            // 组件选择器下拉栏 (简易切换)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ 上一个", GUILayout.Width(75f)))
            {
                _selectedWidgetIndex = (_selectedWidgetIndex - 1 + widgets.Count) % widgets.Count;
                curWidget = widgets[_selectedWidgetIndex];
            }

            string badge = curWidget.WidgetType == "ecam_dial" ? "<color=#00FF88>[ECAM表盘]</color>" :
                          (curWidget.WidgetType == "tape" ? "<color=#00E5FF>[PFD标尺带]</color>" :
                          (curWidget.WidgetId.StartsWith("custom.") ? "<color=#FFAA00>[遥测卡片]</color>" : "<color=#888888>[核心]</color>"));

            GUILayout.Label($"<b>{curWidget.DisplayName}</b> {badge}", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("下一个 ▶", GUILayout.Width(75f)))
            {
                _selectedWidgetIndex = (_selectedWidgetIndex + 1) % widgets.Count;
                curWidget = widgets[_selectedWidgetIndex];
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 基础信息编辑
            GUILayout.BeginHorizontal();
            GUILayout.Label("组件名称:", GUILayout.Width(70f));
            curWidget.DisplayName = GUILayout.TextField(curWidget.DisplayName);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            // 分支 A: ECAM 表盘或 PFD 标尺带装配
            if (curWidget.WidgetType == "ecam_dial" || curWidget.WidgetType == "tape")
            {
                DrawDialOrTapeInspector(curWidget);
            }
            // 分支 B: 遥测监控卡片装配
            else if (curWidget.WidgetId.StartsWith("custom.") || curWidget.WidgetType == "custom")
            {
                DrawCardInspector(curWidget);
            }
            // 分支 C: 核心预设组件
            else
            {
                DrawCoreInspector(curWidget);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // ----------------------------------------------------
            // 右侧：遥测参数菜单库 (Right Panel: Telemetry Menu)
            // ----------------------------------------------------
            GUILayout.BeginVertical("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.Label("<b>📋 遥测参数菜单库 (点击一键绑定/插入)</b>");

            // 分类筛选与搜索栏
            GUILayout.BeginHorizontal();
            _searchQuery = GUILayout.TextField(_searchQuery, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", GUILayout.Width(45f)))
            {
                _searchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int i = 0; i < TelemetryCatalog.Categories.Length; i++)
            {
                bool isCat = _selectedCategoryIndex == i;
                GUI.color = isCat ? Color.cyan : Color.white;
                if (GUILayout.Button(TelemetryCatalog.Categories[i].Split(' ')[0], GUILayout.Height(20f)))
                {
                    _selectedCategoryIndex = i;
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            _rightScroll = GUILayout.BeginScrollView(_rightScroll);

            string currentCategoryFilter = TelemetryCatalog.Categories[_selectedCategoryIndex];

            for (int i = 0; i < TelemetryCatalog.Parameters.Count; i++)
            {
                var p = TelemetryCatalog.Parameters[i];

                // 类别与搜索过滤
                if (_selectedCategoryIndex != 0 && p.Category != currentCategoryFilter) continue;
                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    bool match = p.DisplayName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                              || p.Token.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                              || p.Description.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }

                DrawParameterMenuCard(p, curWidget);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private static void DrawDialOrTapeInspector(WidgetConfig w)
        {
            GUILayout.Label("<color=#00E5FF><b>▼ 驱动参数绑定槽位 (Data Source Slot):</b></color>");

            // 突出显示的绑定槽
            GUI.color = new Color(0.2f, 1f, 0.6f, 1f);
            GUILayout.BeginVertical("box");
            GUILayout.Label($"<b>当前驱动通配符:</b> <size=14>{w.NumericToken}</size>");
            var paramMeta = TelemetryCatalog.FindByToken(w.NumericToken);
            if (paramMeta != null)
            {
                GUILayout.Label($"<color=#CCCCCC><size=11>{paramMeta.DisplayName} - {paramMeta.Description}</size></color>");
            }
            GUILayout.EndVertical();
            GUI.color = Color.white;

            GUILayout.Label("<color=#FFAA00><size=10>👉 点击右侧菜单中任意参数，立即一键替换此表盘的驱动数据源！</size></color>");

            GUILayout.Space(8f);
            GUILayout.Label("<b>▼ 量程与标尺参数细调 (Gauge Calibration):</b>");

            // 上限与下限
            GUILayout.BeginHorizontal();
            GUILayout.Label("量程下限 (Min):", GUILayout.Width(110f));
            w.MinValue = DrawDoubleField(w.MinValue);
            GUILayout.Label("满量程上限 (Max):", GUILayout.Width(110f));
            w.MaxValue = DrawDoubleField(w.MaxValue);
            GUILayout.EndHorizontal();

            // 警示与告警阈值
            GUILayout.BeginHorizontal();
            GUILayout.Label("黄色警示线 (Caution):", GUILayout.Width(110f));
            w.CautionThreshold = DrawDoubleField(w.CautionThreshold);
            GUILayout.Label("红色告警线 (Warn):", GUILayout.Width(110f));
            w.WarningThreshold = DrawDoubleField(w.WarningThreshold);
            GUILayout.EndHorizontal();

            // 软上限爆表模式开关
            GUILayout.Space(4f);
            bool prevSoft = w.IsSoftLimit;
            w.IsSoftLimit = GUILayout.Toggle(w.IsSoftLimit, " <b>软上限爆表模式 (Soft Limit Overflow)</b>");
            if (prevSoft != w.IsSoftLimit)
            {
                WidgetLayoutManager.Instance.SaveLayout();
            }
            string softTip = w.IsSoftLimit
                ? "<color=#00E5FF><size=10>已开启软上限：超过满量程时指针卡在上限并报警，数字绝不截断继续如实累加 (如15G过载表)。</size></color>"
                : "<color=#FFAA00><size=10>已设为硬上限：读数与指针在满量程截断限幅 (如引擎油门 0~100%)。</size></color>";
            GUILayout.Label(softTip);

            // 单位标签
            GUILayout.BeginHorizontal();
            GUILayout.Label("单位标注 (Unit):", GUILayout.Width(110f));
            w.UnitLabel = GUILayout.TextField(w.UnitLabel ?? "", GUILayout.Width(80f));
            if (w.WidgetType == "tape")
            {
                GUILayout.Label("标尺间隔步长:", GUILayout.Width(90f));
                w.StepInterval = DrawFloatField(w.StepInterval);
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("保存此仪表参数并刷新 HUD", GUILayout.Height(24f)))
            {
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance.RebuildHUD();
                ShowToast($"已保存「{w.DisplayName}」配置！");
            }
        }

        private static void DrawCardInspector(WidgetConfig w)
        {
            GUILayout.Label("<color=#FFAA00><b>▼ 遥测卡片文本模板 (Template):</b></color>");

            w.CustomTemplate = GUILayout.TextArea(w.CustomTemplate ?? "", GUILayout.Height(65f));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 分隔符 ' | '", GUILayout.Height(20f)))
            {
                w.CustomTemplate = (w.CustomTemplate ?? "") + " | ";
            }
            if (GUILayout.Button("+ 换行 \\n", GUILayout.Height(20f)))
            {
                w.CustomTemplate = (w.CustomTemplate ?? "") + "\n";
            }
            if (GUILayout.Button("清空模板", GUILayout.Height(20f)))
            {
                w.CustomTemplate = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=#FFAA00><size=10>👉 点击右侧菜单中任意参数，直接将该通配符追加到光标末尾！无需打字</size></color>");

            GUILayout.Space(8f);

            // 实时航电求值预览 (Live Evaluation Preview)
            GUILayout.Label("<b>🌟 航电真实遥测求值实时预览 (Live HUD Preview):</b>");
            GUI.color = new Color(0.1f, 0.15f, 0.2f, 1f);
            GUILayout.BeginVertical("box");
            GUI.color = Color.white;

            string evaluated = "---";
            try
            {
                evaluated = TelemetryTokenEngine.Evaluate(w.CustomTemplate, TelemetryHub.Instance);
                if (string.IsNullOrEmpty(evaluated)) evaluated = "<空模板>";
            }
            catch (Exception ex)
            {
                evaluated = $"[求值异常]: {ex.Message}";
            }

            GUI.color = new Color(0.2f, 1f, 0.6f, 1f);
            GUILayout.Label($"<size=13><b>{evaluated}</b></size>");
            GUI.color = Color.white;

            GUILayout.EndVertical();

            GUILayout.Space(6f);
            if (GUILayout.Button("保存此卡片并刷新 HUD", GUILayout.Height(24f)))
            {
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance.RebuildHUD();
                ShowToast($"已保存「{w.DisplayName}」模板！");
            }
        }

        private static void DrawCoreInspector(WidgetConfig w)
        {
            GUILayout.Label("<color=#FF88FF><b>▼ 核心内建飞行组件:</b></color>");
            GUILayout.Label($"组件标识: {w.WidgetId}");
            GUILayout.Label($"当前启用: {(w.IsEnabled ? "✔ 显示中" : "✖ 已隐藏")}");
            GUILayout.Label("<color=#AAAAAA><size=11>核心组件提供内建专属渲染逻辑 (如高保真 3D 姿态球、滑动带、SAS 罗盘等)，可在拖拽模式下自由摆放位置。</size></color>");
        }

        private static void DrawParameterMenuCard(TelemetryParam p, WidgetConfig curWidget)
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();

            GUILayout.Label($"<b>{p.DisplayName}</b> <color=#00E5FF>[{p.Token}]</color>", GUILayout.ExpandWidth(true));

            // 一键执行按钮
            if (curWidget.WidgetType == "ecam_dial" || curWidget.WidgetType == "tape")
            {
                GUI.color = Color.green;
                if (GUILayout.Button("✔ 绑定到表盘", GUILayout.Width(95f), GUILayout.Height(20f)))
                {
                    curWidget.NumericToken = p.Token;
                    // 智能量程与单位推荐
                    curWidget.MinValue = p.DefaultMin;
                    curWidget.MaxValue = p.DefaultMax;
                    curWidget.CautionThreshold = p.DefaultCaution;
                    curWidget.WarningThreshold = p.DefaultWarning;
                    curWidget.IsSoftLimit = p.DefaultIsSoftLimit;
                    curWidget.UnitLabel = p.DefaultUnit;
                    if (curWidget.WidgetType == "tape") curWidget.StepInterval = p.DefaultStep;

                    WidgetLayoutManager.Instance.SaveLayout();
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已将「{p.DisplayName}」绑定到「{curWidget.DisplayName}」！");
                }
                GUI.color = Color.white;
            }
            else if (curWidget.WidgetId.StartsWith("custom.") || curWidget.WidgetType == "custom")
            {
                GUI.color = Color.yellow;
                if (GUILayout.Button("+ 插入到卡片", GUILayout.Width(95f), GUILayout.Height(20f)))
                {
                    string prefix = string.IsNullOrEmpty(curWidget.CustomTemplate) ? "" : (curWidget.CustomTemplate.EndsWith(" ") ? "" : " | ");
                    curWidget.CustomTemplate = (curWidget.CustomTemplate ?? "") + prefix + $"{p.DisplayName}: {p.Token}";
                    WidgetLayoutManager.Instance.SaveLayout();
                    NavballHUD.Instance.RebuildHUD();
                    ShowToast($"已将「{p.DisplayName}」插入模板！");
                }
                GUI.color = Color.white;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#CCCCCC><size=10>{p.Description} (推荐上限: {p.DefaultMax}{p.DefaultUnit})</size></color>");
            GUILayout.EndVertical();
        }

        private static double DrawDoubleField(double val)
        {
            string txt = val.ToString("G");
            string newTxt = GUILayout.TextField(txt, GUILayout.Width(65f));
            if (double.TryParse(newTxt, out double parsed)) return parsed;
            return val;
        }

        private static float DrawFloatField(float val)
        {
            string txt = val.ToString("F0");
            string newTxt = GUILayout.TextField(txt, GUILayout.Width(65f));
            if (float.TryParse(newTxt, out float parsed)) return parsed;
            return val;
        }

        private static void ShowToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 2.5f;
        }
    }
}
