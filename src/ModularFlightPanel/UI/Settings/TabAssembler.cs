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

            GUILayout.Space(6f);

            // 空间几何与变换控制 (Transform, Scale, Rotation & Alignment)
            DrawTransformInspector(curWidget);

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

            // 通用：绘制单独优化控制区 (Individual Render Optimization)
            DrawOptimizationInspector(curWidget);

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

            // 三态量程语义：硬上限 / 软上限爆表 / 无上限
            GUILayout.Space(4f);
            string currentLimitMode = string.IsNullOrEmpty(w.LimitMode) ? (w.IsSoftLimit ? "soft" : "hard") : w.LimitMode.ToLowerInvariant();
            string previousLimitMode = currentLimitMode;
            GUILayout.Label("量程语义 (Limit Model):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(currentLimitMode == "hard", "硬上限", "Button")) currentLimitMode = "hard";
            if (GUILayout.Toggle(currentLimitMode == "soft", "软上限爆表", "Button")) currentLimitMode = "soft";
            if (GUILayout.Toggle(currentLimitMode == "none", "无上限", "Button")) currentLimitMode = "none";
            GUILayout.EndHorizontal();
            w.LimitMode = currentLimitMode;
            w.IsSoftLimit = currentLimitMode == "soft";
            if (previousLimitMode != currentLimitMode)
            {
                WidgetLayoutManager.Instance.SaveLayout();
            }
            string limitTip = currentLimitMode == "soft"
                ? "<color=#00E5FF><size=10>软上限：指针卡在满量程，数显继续显示真实超限值，并触发红光报警。</size></color>"
                : currentLimitMode == "none"
                    ? "<color=#9FAFFF><size=10>无上限：不截断真实数值，刻度仅作为参考，不制造虚假的最大值。</size></color>"
                    : "<color=#FFAA00><size=10>硬上限：读数、指针与状态在满量程处截断。</size></color>";
            GUILayout.Label(limitTip);

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

        private static void DrawOptimizationInspector(WidgetConfig w)
        {
            GUILayout.Space(8f);
            GUILayout.Label("<color=#00E5FF><b>⚡ 绘制性能单独优化 (Render Optimization):</b></color>");
            GUILayout.BeginVertical("box");

            // 1. 独立画布隔离 (Sub-Canvas)
            bool prevIsolate = w.IsolateCanvas;
            w.IsolateCanvas = GUILayout.Toggle(w.IsolateCanvas, " 启用独立画布隔离 (Isolate Sub-Canvas 避免脏标记污染)");
            if (w.IsolateCanvas != prevIsolate)
            {
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance?.RebuildHUD();
                ShowToast($"已{(w.IsolateCanvas ? "开启" : "关闭")}「{w.DisplayName}」独立画布！");
            }

            // 2. 遥测刷新与绘制分频 (Update Interval / Frequency Throttling)
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("刷新分频模式:", GUILayout.Width(85f));
            string hzLabel = w.UpdateInterval <= 0f ? "原生 60Hz+ (每帧)" :
                            (w.UpdateInterval <= 0.06f ? "中频 20Hz (0.05s)" :
                            (w.UpdateInterval <= 0.15f ? "低频 10Hz (0.1s)" :
                            (w.UpdateInterval <= 0.25f ? "节能 5Hz (0.2s)" : "极简 2Hz (0.5s)")));
            GUILayout.Label($"<b>{hzLabel}</b>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("60Hz", GUILayout.Height(20f))) { w.UpdateInterval = 0f; WidgetLayoutManager.Instance.SaveLayout(); ShowToast("已设为 60Hz 每帧刷新"); }
            if (GUILayout.Button("20Hz", GUILayout.Height(20f))) { w.UpdateInterval = 0.05f; WidgetLayoutManager.Instance.SaveLayout(); ShowToast("已设为 20Hz (0.05s)"); }
            if (GUILayout.Button("10Hz", GUILayout.Height(20f))) { w.UpdateInterval = 0.1f; WidgetLayoutManager.Instance.SaveLayout(); ShowToast("已设为 10Hz (0.1s)"); }
            if (GUILayout.Button("5Hz", GUILayout.Height(20f))) { w.UpdateInterval = 0.2f; WidgetLayoutManager.Instance.SaveLayout(); ShowToast("已设为 5Hz (0.2s)"); }
            if (GUILayout.Button("2Hz", GUILayout.Height(20f))) { w.UpdateInterval = 0.5f; WidgetLayoutManager.Instance.SaveLayout(); ShowToast("已设为 2Hz (0.5s)"); }
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=#888888><size=10>说明：姿态球与标尺带推荐 60Hz；维生/通信/电力面板设为 2~5Hz 可节省 80% CPU 与顶点批处理开销。</size></color>");

            GUILayout.EndVertical();
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
                    curWidget.LimitMode = p.DefaultIsSoftLimit ? "soft" : "hard";
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

        private static void DrawTransformInspector(WidgetConfig w)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>📐 空间几何与变换 (Transform & Alignment)</b>");

            // 1. 坐标位置 (Position X, Y)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"X: <b>{w.PositionX:F0}px</b>", GUILayout.Width(80f));
            if (GUILayout.Button("-10", GUILayout.Width(35f))) { w.PositionX -= 10f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("-1", GUILayout.Width(28f))) { w.PositionX -= 1f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("+1", GUILayout.Width(28f))) { w.PositionX += 1f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("+10", GUILayout.Width(35f))) { w.PositionX += 10f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("居中 0", GUILayout.Width(50f))) { w.PositionX = 0f; ApplyWidgetTransform(w); }

            GUILayout.Space(8f);
            GUILayout.Label($"Y: <b>{w.PositionY:F0}px</b>", GUILayout.Width(80f));
            if (GUILayout.Button("-10", GUILayout.Width(35f))) { w.PositionY -= 10f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("-1", GUILayout.Width(28f))) { w.PositionY -= 1f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("+1", GUILayout.Width(28f))) { w.PositionY += 1f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("+10", GUILayout.Width(35f))) { w.PositionY += 10f; ApplyWidgetTransform(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 2. 缩放比例 (Scale)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"缩放比: <b>{w.Scale:F2}x</b>", GUILayout.Width(85f));
            if (GUILayout.Button("-0.1", GUILayout.Width(35f))) { w.Scale = Mathf.Clamp(Mathf.Round((w.Scale - 0.1f) * 20f) / 20f, 0.2f, 4.0f); ApplyWidgetTransform(w); }
            if (GUILayout.Button("+0.1", GUILayout.Width(35f))) { w.Scale = Mathf.Clamp(Mathf.Round((w.Scale + 0.1f) * 20f) / 20f, 0.2f, 4.0f); ApplyWidgetTransform(w); }
            float newScale = GUILayout.HorizontalSlider(w.Scale, 0.3f, 2.5f, GUILayout.Width(100f));
            if (Math.Abs(newScale - w.Scale) > 0.005f)
            {
                w.Scale = Mathf.Round(newScale * 20f) / 20f;
                ApplyWidgetTransform(w);
            }
            if (GUILayout.Button("0.8x", GUILayout.Width(38f))) { w.Scale = 0.8f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("1.0x", GUILayout.Width(38f))) { w.Scale = 1.0f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("1.2x", GUILayout.Width(38f))) { w.Scale = 1.2f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("1.5x", GUILayout.Width(38f))) { w.Scale = 1.5f; ApplyWidgetTransform(w); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 3. 旋转角度 (Rotation)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"旋转角: <b>{w.Rotation:F0}°</b>", GUILayout.Width(95f));
            float newRot = GUILayout.HorizontalSlider(w.Rotation, 0f, 360f, GUILayout.Width(130f));
            if (Math.Abs(newRot - w.Rotation) > 0.5f)
            {
                w.Rotation = Mathf.Round(newRot / 5f) * 5f;
                ApplyWidgetTransform(w);
            }
            if (GUILayout.Button("0°", GUILayout.Width(35f))) { w.Rotation = 0f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("90°", GUILayout.Width(35f))) { w.Rotation = 90f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("180°", GUILayout.Width(40f))) { w.Rotation = 180f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("270°", GUILayout.Width(40f))) { w.Rotation = 270f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("-15°", GUILayout.Width(38f))) { w.Rotation = (w.Rotation - 15f + 360f) % 360f; ApplyWidgetTransform(w); }
            if (GUILayout.Button("+15°", GUILayout.Width(38f))) { w.Rotation = (w.Rotation + 15f) % 360f; ApplyWidgetTransform(w); }
            GUILayout.EndHorizontal();

            // 4. 多选批量对齐工具
            int selCount = WidgetSelectionManager.Count;
            if (selCount >= 2)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#FFE000><b>多选对齐 ({selCount}项):</b></color>", GUILayout.Width(100f));
                if (GUILayout.Button("左对齐", GUILayout.Width(50f))) WidgetSelectionManager.AlignLeft();
                if (GUILayout.Button("居中X", GUILayout.Width(48f))) WidgetSelectionManager.AlignCenterX();
                if (GUILayout.Button("右对齐", GUILayout.Width(50f))) WidgetSelectionManager.AlignRight();
                if (GUILayout.Button("顶对齐", GUILayout.Width(50f))) WidgetSelectionManager.AlignTop();
                if (GUILayout.Button("居中Y", GUILayout.Width(48f))) WidgetSelectionManager.AlignCenterY();
                if (GUILayout.Button("底对齐", GUILayout.Width(50f))) WidgetSelectionManager.AlignBottom();
                if (selCount >= 3)
                {
                    if (GUILayout.Button("水平等距", GUILayout.Width(58f))) WidgetSelectionManager.DistributeHorizontally();
                    if (GUILayout.Button("垂直等距", GUILayout.Width(58f))) WidgetSelectionManager.DistributeVertically();
                }
                GUILayout.EndHorizontal();
            }

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
