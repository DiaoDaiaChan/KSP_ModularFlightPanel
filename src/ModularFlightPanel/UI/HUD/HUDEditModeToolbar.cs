using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets.Controls;
#if KSP_RUNTIME
using ModularFlightPanel.UI.Settings;
#endif

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 图形化编辑模式悬浮工具栏 (HUD Edit Mode Toolbar & Action Controller)
    /// 核心职责：
    /// 1. 宿主编辑模式顶部悬浮卡片 (撤销/重做、左中右顶底对齐、水平/垂直等距、网格/图层开关)；
    /// 2. 宿主直接吸附在选中组件右侧/上方的即时悬浮缩放、旋转、图层快速操作盒；
    /// 3. 与主 HUD 编排调度彻底解耦。仅在处于编辑模式时启用 (enabled = true)，
    ///    非编辑状态下彻底休眠 (enabled = false)，100% 消除 Unity IMGUI 的 OnGUI / DoGUI 每帧轮询与内存垃圾！
    /// </summary>
    public class HUDEditModeToolbar : MonoBehaviour
    {
        private FlightHUDManager _hudManager;

        public void Initialize(FlightHUDManager hudManager)
        {
            _hudManager = hudManager;
            enabled = false; // 默认严格休眠，杜绝常驻 IMGUI 开销
        }

#if KSP_RUNTIME && !UNITY_EDITOR
        private void OnGUI()
        {
            if (!WidgetDragHandler.IsEditModeActive || MFPProfiler.IsMasterBypassed) return;

            MFPGuiSkin.EnsureInitialized();

            FlightHUDManager.IsMouseOverFloatingToolbar = false;
            WidgetControlHighlighter.HighlightedControl = null;

            float toolbarW = 1180f;
            float toolbarH = 78f;
            float x = (Screen.width - toolbarW) * 0.5f;
            float y = 12f;

            Rect topToolbarRect = new Rect(x, y, toolbarW, toolbarH);
            if (topToolbarRect.Contains(Event.current.mousePosition))
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(topToolbarRect, MFPGuiSkin.CardStyle);

            // 第一行：标题 + 撤销/重做 + 全套对齐工具 + 快速分享/导入
            GUILayout.BeginHorizontal();
            int selCount = WidgetSelectionManager.Count;
            string selInfo = selCount > 0 ? $"<color=#FFE000><b>已选 {selCount} 项</b></color>" : "<color=#AAAAAA>未选中 (拉框多选)</color>";
            GUILayout.Label($"🛠️ <b>MFP 设计工坊</b> | {selInfo}", GUILayout.Width(170f));

            GUI.enabled = WidgetEditHistory.CanUndo;
            if (GUILayout.Button("↶ 撤销", GUILayout.Width(50f), GUILayout.Height(24f))) WidgetEditHistory.Undo();
            GUI.enabled = WidgetEditHistory.CanRedo;
            if (GUILayout.Button("↷ 重做", GUILayout.Width(50f), GUILayout.Height(24f))) WidgetEditHistory.Redo();
            GUI.enabled = true;

            GUILayout.Space(6f);
            GUI.enabled = selCount >= 2;
            if (GUILayout.Button("⬅ 左对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignLeft();
            if (GUILayout.Button("⏸ 居中X", GUILayout.Width(54f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterX();
            if (GUILayout.Button("➡ 右对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignRight();
            if (GUILayout.Button("⬆ 顶对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignTop();
            if (GUILayout.Button("⏵ 居中Y", GUILayout.Width(54f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterY();
            if (GUILayout.Button("⬇ 底对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignBottom();
            GUI.enabled = selCount >= 3;
            if (GUILayout.Button("⇹ 水平等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeHorizontally();
            if (GUILayout.Button("⇳ 垂直等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeVertically();
            GUI.enabled = selCount >= 1;
            if (GUILayout.Button("⌖ X=0中轴", GUILayout.Width(64f), GUILayout.Height(24f))) WidgetSelectionManager.CenterToScreenX();
            GUI.enabled = true;

            GUILayout.Space(6f);
            if (GUILayout.Button("📋 分享码", GUILayout.Width(68f), GUILayout.Height(24f)))
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    MFPToastBridge.Show(I18n.Tr("PRF_TOAST_SHARE_COPIED", "✔ 已成功复制分享码至剪贴板！"));
                }
            }
            if (GUILayout.Button("📥 导入", GUILayout.Width(52f), GUILayout.Height(24f)))
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip) && LayoutShareHub.TryImportShareCode(clip, out var imported, out string err))
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.ApplyLayout(imported);
                    FlightHUDManager.Instance?.RebuildHUD();
                    MFPToastBridge.Show(I18n.TrFormat("PRF_TOAST_IMPORT_OK", imported.Widgets.Count));
                }
                else
                {
                    UIWidget.OnRequestOpenWorkbench?.Invoke();
                }
            }
            GUILayout.EndHorizontal();

            // 第二行：磁吸/网格开关 + 图层/删除/微调 + 快捷退出
            GUILayout.BeginHorizontal();
            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            Color accentCol = theme != null ? (Color)theme.AccentSecondary : GUI.contentColor;
            Color textCol = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
            bool snap = WidgetDragHandler.EnableMagneticSnap;
            GUI.color = snap ? accentCol : textCol;
            if (GUILayout.Button(snap ? "🧲 磁吸: [开]" : "🧲 磁吸: [关]", GUILayout.Width(84f), GUILayout.Height(22f)))
            {
                WidgetDragHandler.EnableMagneticSnap = !WidgetDragHandler.EnableMagneticSnap;
            }

            bool grid = WidgetCanvasGrid.IsGridVisible;
            GUI.color = grid ? accentCol : textCol;
            if (GUILayout.Button(grid ? "▦ 网格: [开]" : "▦ 网格: [关]", GUILayout.Width(84f), GUILayout.Height(22f)))
            {
                WidgetCanvasGrid.ToggleGrid();
            }

            bool layerOpen = WidgetLayerManager.IsLayerPanelOpen;
            GUI.color = layerOpen ? accentCol : textCol;
            if (GUILayout.Button(layerOpen ? "📑 图层: [开]" : "📑 图层: [关]", GUILayout.Width(84f), GUILayout.Height(22f)))
            {
                WidgetLayerManager.ToggleLayerPanel();
            }
            GUI.color = textCol;

            if (selCount > 0)
            {
                // 图层层级
                if (GUILayout.Button("⤒", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BringToFront();
                if (GUILayout.Button("▲", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BringForward();
                if (GUILayout.Button("▼", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.SendBackward();
                if (GUILayout.Button("⤓", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.SendToBack();

                // 快速删除/隐藏
                if (GUILayout.Button("🗑 隐藏", GUILayout.Width(46f), GUILayout.Height(22f))) WidgetSelectionManager.DeleteSelected();

                // 缩放
                GUILayout.Space(4f);
                if (GUILayout.Button("－", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(-0.1f);
                if (GUILayout.Button("＋", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(+0.1f);
                if (GUILayout.Button("1.0x", GUILayout.Width(36f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(1.0f);

                // 旋转
                if (GUILayout.Button("↺ 15°", GUILayout.Width(42f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(-15f);
                if (GUILayout.Button("0°", GUILayout.Width(26f), GUILayout.Height(22f))) WidgetSelectionManager.ResetRotation();
                if (GUILayout.Button("↻ 15°", GUILayout.Width(42f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(+15f);

                GUILayout.Space(4f);
                if (GUILayout.Button("取消选择", GUILayout.Width(62f), GUILayout.Height(22f))) WidgetSelectionManager.ClearSelection();
            }
            else
            {
                if (GUILayout.Button("全选 (Ctrl+A)", GUILayout.Width(88f), GUILayout.Height(22f)))
                {
                    if (_hudManager != null && _hudManager.ModularWidgets != null)
                    {
                        WidgetSelectionManager.SelectAll(_hudManager.ModularWidgets);
                    }
                }
                GUILayout.Label("<color=#94A3B8><size=10>快捷键: 方向微调(Shift+10px) | 拖拽缩放/旋转 | Shift轴向 | Ctrl+Z撤销 | G网格 | L图层 | []层级</size></color>");
            }

            if (GUILayout.Button("✔ 完成退出", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(84f), GUILayout.Height(24f)))
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // 绘制直接吸附在组件旁边的即时悬浮缩放/旋转操作盒 (点击一下即可!)
            DrawOnWidgetFloatingToolbar(selCount);

            // 绘制 Photoshop 级悬浮图层管理器抽屉 (按 L 键或点击工具栏切换)
            WidgetLayerManager.DrawLayerPanel(selCount);

            // 绘制微控件实时悬停高亮边框
            if (TelemetryParamDrawer.IsOpen)
            {
                float modalW = Mathf.Min(840f, Screen.width - 40f);
                float modalH = Mathf.Min(540f, Screen.height - 40f);
                float modalX = (Screen.width - modalW) * 0.5f;
                float modalY = (Screen.height - modalH) * 0.5f;
                Rect modalRect = new Rect(modalX, modalY, modalW, modalH);

                if (modalRect.Contains(Event.current.mousePosition))
                {
                    FlightHUDManager.IsMouseOverFloatingToolbar = true;
                }

                GUILayout.BeginArea(modalRect, MFPGuiSkin.CardStyle);
                TelemetryParamDrawer.Draw(modalH - 120f);
                GUILayout.EndArea();
            }

            WidgetControlHighlighter.DrawGizmo(_hudManager?.Canvas);

            MFPInputLock.SetWindowHoverLock(FlightHUDManager.IsMouseOverFloatingToolbar);
        }

        private void DrawOnWidgetFloatingToolbar(int selCount)
        {
            if (selCount <= 0 || _hudManager == null || _hudManager.Canvas == null) return;

            BaseFlightWidget primary = null;
            foreach (var w in WidgetSelectionManager.SelectedWidgets)
            {
                if (w != null && w.RectTransform != null)
                {
                    primary = w;
                    break;
                }
            }

            if (primary == null || primary.RectTransform == null) return;

            Vector3[] corners = new Vector3[4];
            primary.RectTransform.GetWorldCorners(corners);

            Canvas canvas = _hudManager.Canvas;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 p1 = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            Vector2 p3 = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float minY_screen = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            float maxY_screen = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            // 屏幕坐标 (左下原点) 转换为 IMGUI 坐标 (左上原点)
            float guiMinY = Screen.height - maxY_screen;
            float guiMaxY = Screen.height - minY_screen;

            float badgeW = 345f;
            float badgeH = selCount == 1 ? 134f : 108f;

            // 优先置于组件右侧，留出 10px 空隙
            float bx = maxX + 10f;
            float by = guiMinY;

            // 若右侧超出屏幕边缘，则自适应翻转至组件左侧
            if (bx + badgeW > Screen.width - 10f)
            {
                bx = minX - badgeW - 10f;
            }
            // 若左右两侧都超出，则置于组件正上方
            if (bx < 10f)
            {
                bx = Mathf.Clamp(minX, 10f, Screen.width - badgeW - 10f);
                by = guiMinY - badgeH - 10f;
            }

            // 屏幕安全边界截断约束
            bx = Mathf.Clamp(bx, 10f, Screen.width - badgeW - 10f);
            by = Mathf.Clamp(by, 10f, Screen.height - badgeH - 10f);

            Rect badgeRect = new Rect(bx, by, badgeW, badgeH);

            if (badgeRect.Contains(Event.current.mousePosition))
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(badgeRect, MFPGuiSkin.CardStyle);

            // 1. 标题行 (显示当前组件名与实时缩放比、旋转角)
            GUILayout.BeginHorizontal();
            float curScale = primary.Config?.Scale ?? 1.0f;
            float curRot = primary.Config?.Rotation ?? 0f;
            string titlePrefix = selCount > 1 ? $"<color=#FFE000><b>[已选 {selCount} 项]</b></color> " : "";
            GUILayout.Label($"{titlePrefix}<b>{primary.DisplayName}</b>", GUILayout.ExpandWidth(true));
            GUILayout.Label($"<color=#00E5FF><b>{curScale:F2}x</b></color> | <color=#FFE000><b>{curRot:F0}°</b></color>", GUILayout.Width(95f));
            GUILayout.EndHorizontal();

            // 2. 缩放控制行 (点击一下即可!)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#00E5FF><b>缩放:</b></color>", GUILayout.Width(35f));
            if (GUILayout.Button("－", GUILayout.Width(25f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchScale(-0.1f);
            }
            if (GUILayout.Button("＋", GUILayout.Width(25f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchScale(+0.1f);
            }
            if (GUILayout.Button("0.8x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchSetScale(0.8f);
            }
            if (GUILayout.Button("1.0x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchSetScale(1.0f);
            }
            if (GUILayout.Button("1.2x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchSetScale(1.2f);
            }
            if (GUILayout.Button("1.5x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                WidgetSelectionManager.BatchSetScale(1.5f);
            }
            GUILayout.EndHorizontal();

            // 3. 旋转控制行 (点击一下即可!)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#FFE000><b>旋转:</b></color>", GUILayout.Width(35f));
            if (GUILayout.Button("↺ 15°", GUILayout.Width(46f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchRotate(-15f);
                else { primary.UpdateTransform(rotation: (curRot - 15f + 360f) % 360f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("0°", GUILayout.Width(30f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.ResetRotation();
                else { primary.UpdateTransform(rotation: 0f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("↻ 15°", GUILayout.Width(46f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchRotate(+15f);
                else { primary.UpdateTransform(rotation: (curRot + 15f) % 360f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("90°", GUILayout.Width(32f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetRotation(90f);
                else { primary.UpdateTransform(rotation: 90f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("180°", GUILayout.Width(38f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetRotation(180f);
                else { primary.UpdateTransform(rotation: 180f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("⤢ 居中X", GUILayout.Width(52f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.CenterToScreenX();
                else { primary.UpdateTransform(x: 0f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            GUILayout.EndHorizontal();

            // 4. 图层层级控制行 (Layer & Drawing Order)
            GUILayout.BeginHorizontal();
            int curLayer = WidgetLayerManager.GetLayerNumber(primary);
            int totalLayers = WidgetLayerManager.TotalLayers;
            GUILayout.Label($"<color=#38BDF8><b>图层: #{curLayer}/{totalLayers}</b></color>", GUILayout.Width(92f));
            if (GUILayout.Button("⤒", GUILayout.Width(24f), GUILayout.Height(20f))) WidgetLayerManager.BringToFront(WidgetSelectionManager.SelectedWidgets);
            if (GUILayout.Button("▲", GUILayout.Width(24f), GUILayout.Height(20f))) WidgetLayerManager.BringForward(WidgetSelectionManager.SelectedWidgets);
            if (GUILayout.Button("▼", GUILayout.Width(24f), GUILayout.Height(20f))) WidgetLayerManager.SendBackward(WidgetSelectionManager.SelectedWidgets);
            if (GUILayout.Button("⤓", GUILayout.Width(24f), GUILayout.Height(20f))) WidgetLayerManager.SendToBack(WidgetSelectionManager.SelectedWidgets);
            GUILayout.Space(6f);
            bool isLocked = primary.Config?.IsLocked == true;
            string lockBtn = isLocked ? "<color=#FFB703>🔒 锁定</color>" : "<color=#94A3B8>🔓 解锁</color>";
            if (GUILayout.Button(lockBtn, GUILayout.Width(58f), GUILayout.Height(20f)))
            {
                WidgetLayerManager.ToggleLock(primary);
            }
            GUILayout.EndHorizontal();

            // 5. 子控件微调与显隐入口 + 遥测装配入口
            if (selCount == 1)
            {
                var ctrlList = primary.Controls.All;
                int totalCtrls = ctrlList.Count;
                int visCtrls = 0;
                for (int i = 0; i < totalCtrls; i++) if (ctrlList[i].IsVisible) visCtrls++;

                GUILayout.Space(2f);
                GUILayout.BeginHorizontal();

                // 控件定制入口
                if (totalCtrls > 0)
                {
                    bool isMicroActive = _showInspectorDrawer && _activeInspectorTab == InspectorTab.MicroControls;
                    GUI.color = isMicroActive ? new Color(0f, 0.9f, 1f) : Color.white;
                    string btnTxt = isMicroActive ? $"⚙️ 控件 ({visCtrls}/{totalCtrls}) [开]" : $"⚙️ 控件 ({visCtrls}/{totalCtrls})";
                    if (GUILayout.Button(btnTxt, GUILayout.Height(20f)))
                    {
                        if (_showInspectorDrawer && _activeInspectorTab == InspectorTab.MicroControls)
                        {
                            _showInspectorDrawer = false;
                        }
                        else
                        {
                            _showInspectorDrawer = true;
                            _activeInspectorTab = InspectorTab.MicroControls;
                        }
                    }
                }

                // 遥测装配入口
                bool isTelemActive = _showInspectorDrawer && _activeInspectorTab == InspectorTab.Telemetry;
                GUI.color = isTelemActive ? new Color(0f, 0.9f, 1f) : Color.white;
                string telemBtn = isTelemActive ? "📊 遥测 [开]" : "📊 遥测装配";
                if (GUILayout.Button(telemBtn, GUILayout.Height(20f)))
                {
                    if (_showInspectorDrawer && _activeInspectorTab == InspectorTab.Telemetry)
                    {
                        _showInspectorDrawer = false;
                    }
                    else
                    {
                        _showInspectorDrawer = true;
                        _activeInspectorTab = InspectorTab.Telemetry;
                    }
                }

                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();

            if (_showInspectorDrawer && selCount == 1)
            {
                DrawWidgetInspectorDrawer(primary, badgeRect);
            }
        }

        public enum InspectorTab
        {
            MicroControls,
            Telemetry,
            Channels
        }

        public static bool IsSubControlCustomizerOpen => _showInspectorDrawer && _activeInspectorTab == InspectorTab.MicroControls;
        private static bool _showInspectorDrawer = false;
        private static InspectorTab _activeInspectorTab = InspectorTab.MicroControls;
        private static Vector2 _subControlScrollPos = Vector2.zero;
        private static Vector2 _telemScrollPos = Vector2.zero;
        private static Vector2 _channelScrollPos = Vector2.zero;
        private static bool _showGlobalTelemetryFoldout = false;

        private static float _lastEvalTime = 0f;
        private static readonly Dictionary<string, string> _channelEvalCache = new Dictionary<string, string>();

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
            _lastEvalTime = Time.unscaledTime;
            return val;
        }

        private static readonly string[] DefaultChannelKeys = new string[] { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6" };
        private static readonly string[] DefaultChannelNames = new string[] { "主速度", "推重比", "雷达高", "动压", "垂直速", "过载" };
        private static readonly string[] DefaultChannelTokens = new string[] { "{SPD}", "{TWR}", "{ALT:AGL}", "{Q}", "{VSI}", "{GFORCE}" };

        private static Dictionary<string, string> ParseChannels(string template)
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

        private static void SetChannel(WidgetConfig w, string channelKey, string token)
        {
            var dict = ParseChannels(w.CustomTemplate);
            dict[channelKey] = token;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            w.CustomTemplate = sb.ToString();
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        private static string GetCategoryShortTag(WidgetControlCategory cat)
        {
            switch (cat)
            {
                case WidgetControlCategory.Header: return "标题";
                case WidgetControlCategory.Readout: return "数显";
                case WidgetControlCategory.LinearGauge: return "柱条";
                case WidgetControlCategory.ArcGauge: return "弧表";
                case WidgetControlCategory.NeedlePointer: return "指针";
                case WidgetControlCategory.ActionButton: return "按键";
                case WidgetControlCategory.Annunciator: return "灯珠";
                case WidgetControlCategory.Viewport: return "视口";
                case WidgetControlCategory.DataStack: return "列表";
                case WidgetControlCategory.ModeCapsule: return "胶囊";
                case WidgetControlCategory.TrendBar: return "趋势";
                default: return "图元";
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

        private void DrawWidgetInspectorDrawer(BaseFlightWidget primary, Rect badgeRect)
        {
            if (primary == null) return;
            var ctrlList = primary.Controls.All;
            WidgetConfig w = primary.Config;

            float subW = _activeInspectorTab == InspectorTab.MicroControls ? 360f : 430f;
            float subH = _activeInspectorTab == InspectorTab.MicroControls 
                ? Mathf.Clamp(80f + ctrlList.Count * 28f, 160f, 320f)
                : (_activeInspectorTab == InspectorTab.Telemetry ? Mathf.Clamp(120f + ctrlList.Count * 36f, 300f, 420f) : 340f);

            float subX = badgeRect.x;
            float subY = badgeRect.y + badgeRect.height + 6f;
            if (subY + subH > Screen.height - 10f)
            {
                subY = badgeRect.y - subH - 6f;
            }
            subX = Mathf.Clamp(subX, 10f, Screen.width - subW - 10f);
            subY = Mathf.Clamp(subY, 10f, Screen.height - subH - 10f);

            Rect subRect = new Rect(subX, subY, subW, subH);
            if (subRect.Contains(Event.current.mousePosition))
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(subRect, MFPGuiSkin.CardStyle);

            // 标题栏与标签切换
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🛠️ <b>{primary.DisplayName}</b>", GUILayout.Width(110f));

            if (ctrlList.Count > 0)
            {
                GUI.color = _activeInspectorTab == InspectorTab.MicroControls ? new Color(0f, 0.9f, 1f) : Color.white;
                if (GUILayout.Button("⚙️ 控件", GUILayout.Width(58f), GUILayout.Height(20f))) _activeInspectorTab = InspectorTab.MicroControls;
            }

            GUI.color = _activeInspectorTab == InspectorTab.Telemetry ? new Color(0f, 0.9f, 1f) : Color.white;
            if (GUILayout.Button("📊 遥测", GUILayout.Width(58f), GUILayout.Height(20f))) _activeInspectorTab = InspectorTab.Telemetry;

            bool hasTemplate = !string.IsNullOrEmpty(w?.CustomTemplate);
            if (hasTemplate)
            {
                GUI.color = _activeInspectorTab == InspectorTab.Channels ? new Color(0f, 0.9f, 1f) : Color.white;
                if (GUILayout.Button("📋 通道", GUILayout.Width(58f), GUILayout.Height(20f))) _activeInspectorTab = InspectorTab.Channels;
            }
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(22f), GUILayout.Height(18f)))
            {
                _showInspectorDrawer = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            if (_activeInspectorTab == InspectorTab.MicroControls)
            {
                DrawMicroControlsTab(primary, ctrlList, subH);
            }
            else if (_activeInspectorTab == InspectorTab.Telemetry)
            {
                DrawTelemetryTab(primary, w, subH);
            }
            else if (_activeInspectorTab == InspectorTab.Channels)
            {
                DrawChannelsTab(primary, w, subH);
            }

            GUILayout.EndArea();
        }

        private void DrawMicroControlsTab(BaseFlightWidget primary, IReadOnlyList<IWidgetControl> ctrlList, float totalH)
        {
            // 提示与批处理栏
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><size=9>💡 提示: 可直接在屏幕上鼠标拖拽子控件，继承 5px 网格与原点磁吸</size></color>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✔ 全显", GUILayout.Width(46f), GUILayout.Height(19f)))
            {
                for (int i = 0; i < ctrlList.Count; i++) primary.Controls.SetControlVisibility(ctrlList[i].Id, true);
                WidgetLayoutManager.Instance.SaveLayout();
            }
            if (GUILayout.Button("○ 全隐", GUILayout.Width(46f), GUILayout.Height(19f)))
            {
                for (int i = 0; i < ctrlList.Count; i++) primary.Controls.SetControlVisibility(ctrlList[i].Id, false);
                WidgetLayoutManager.Instance.SaveLayout();
            }
            if (GUILayout.Button("↺ 复位", GUILayout.Width(46f), GUILayout.Height(19f)))
            {
                primary.Controls.ResetAllOffsets();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>Shift: 10px 快速步进</size></color>");
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            _subControlScrollPos = GUILayout.BeginScrollView(_subControlScrollPos, GUILayout.Height(totalH - 72f));
            for (int i = 0; i < ctrlList.Count; i++)
            {
                var ctrl = ctrlList[i];
                if (ctrl == null) continue;

                GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(22f));

                // 1. 显隐开关
                string led = ctrl.IsVisible ? "<color=#00FF88>●</color>" : "<color=#7088A8>○</color>";
                if (GUILayout.Button(led, GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    primary.Controls.SetControlVisibility(ctrl.Id, !ctrl.IsVisible);
                    WidgetLayoutManager.Instance.SaveLayout();
                }

                // 2. 类别徽章
                string catTag = GetCategoryShortTag(ctrl.Category);
                MFPGuiSkin.DrawBadge(catTag, Color.white, GetCategoryColor(ctrl.Category), 38f);

                // 3. 控件名称
                GUILayout.Label($"<b>{ctrl.DisplayName}</b>", GUILayout.Width(92f));

                // 4. 步进位移
                float step = Event.current.shift ? 10f : 2f;
                Vector2 curOff = ctrl.CurrentOffset;

                if (GUILayout.Button("◀", GUILayout.Width(18f), GUILayout.Height(18f)))
                {
                    primary.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(-step, 0f));
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▶", GUILayout.Width(18f), GUILayout.Height(18f)))
                {
                    primary.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(step, 0f));
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▲", GUILayout.Width(18f), GUILayout.Height(18f)))
                {
                    primary.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(0f, step));
                    WidgetLayoutManager.Instance.SaveLayout();
                }
                if (GUILayout.Button("▼", GUILayout.Width(18f), GUILayout.Height(18f)))
                {
                    primary.Controls.SetControlOffset(ctrl.Id, curOff + new Vector2(0f, -step));
                    WidgetLayoutManager.Instance.SaveLayout();
                }

                // 5. 偏移量读数与单项复位
                bool hasOff = Mathf.Abs(curOff.x) > 0.01f || Mathf.Abs(curOff.y) > 0.01f;
                if (hasOff)
                {
                    GUILayout.Label($"<color=#00E5FF><size=9>{curOff.x:+0;-0;0},{curOff.y:+0;-0;0}</size></color>", GUILayout.Width(38f));
                    if (GUILayout.Button("↺", GUILayout.Width(18f), GUILayout.Height(18f)))
                    {
                        primary.Controls.SetControlOffset(ctrl.Id, Vector2.zero);
                        WidgetLayoutManager.Instance.SaveLayout();
                    }
                }
                else
                {
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>0,0</size></color>", GUILayout.Width(38f));
                    GUILayout.Space(22f);
                }

                GUILayout.EndHorizontal();

                // 实时悬停高亮检测
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
        }

        private void DrawTelemetryTab(BaseFlightWidget primary, WidgetConfig w, float totalH)
        {
            if (w == null) return;
            _telemScrollPos = GUILayout.BeginScrollView(_telemScrollPos, GUILayout.Height(totalH - 46f));

            var allControls = primary.Controls?.All;
            var bindableControls = new List<ITelemetryBindableControl>();
            if (allControls != null)
            {
                for (int i = 0; i < allControls.Count; i++)
                {
                    if (allControls[i] is ITelemetryBindableControl bindable && bindable.HasTelemetryBinding)
                    {
                        bindableControls.Add(bindable);
                    }
                }
            }

            // 1. 微控件多参数独立装配区
            if (bindableControls.Count > 0)
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentCyan}><size=9>💡 发现 {bindableControls.Count} 处微控件数据源，支持逐项装配与实时取样:</size></color>");
                for (int i = 0; i < bindableControls.Count; i++)
                {
                    var ctrl = bindableControls[i];
                    MFPGuiSkin.BeginInset();
                    GUILayout.BeginHorizontal();

                    // 1. 显隐状态点
                    string led = ctrl.IsVisible ? "<color=#00FF88>●</color>" : "<color=#7088A8>○</color>";
                    if (GUILayout.Button(led, GUILayout.Width(20f), GUILayout.Height(18f)))
                    {
                        primary.Controls.SetControlVisibility(ctrl.Id, !ctrl.IsVisible);
                        WidgetLayoutManager.Instance.SaveLayout();
                    }

                    // 2. 类别徽章
                    string catTag = GetCategoryShortTag(ctrl.Category);
                    MFPGuiSkin.DrawBadge(catTag, Color.white, GetCategoryColor(ctrl.Category), 34f);

                    // 3. 微控件显示名称
                    GUILayout.Label($"<b>{ctrl.DisplayName}</b>", GUILayout.Width(86f));

                    // 4. Token 输入框
                    string curTok = ctrl.TelemetryToken ?? "";
                    string editedTok = GUILayout.TextField(curTok, GUILayout.Width(100f));
                    if (editedTok != curTok)
                    {
                        ctrl.TelemetryToken = editedTok;
                        SetChannel(w, $"{ctrl.Id.ToUpperInvariant()}_TOKEN", editedTok);
                    }

                    // 5. 🔍 选参数 按钮
                    if (GUILayout.Button("🔍", GUILayout.Width(24f), GUILayout.Height(20f)))
                    {
                        var targetCtrl = ctrl;
                        TelemetryParamDrawer.Open($"{primary.DisplayName} - {targetCtrl.DisplayName}", chosenToken =>
                        {
                            targetCtrl.TelemetryToken = chosenToken;
                            SetChannel(w, $"{targetCtrl.Id.ToUpperInvariant()}_TOKEN", chosenToken);
                            var meta = TelemetryCatalog.FindByToken(chosenToken);
                            if (meta != null && targetCtrl.SupportsRange)
                            {
                                targetCtrl.MinValue = meta.DefaultMin;
                                targetCtrl.MaxValue = meta.DefaultMax;
                                SetChannel(w, $"{targetCtrl.Id.ToUpperInvariant()}_MIN", meta.DefaultMin.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                SetChannel(w, $"{targetCtrl.Id.ToUpperInvariant()}_MAX", meta.DefaultMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            }
                        });
                    }

                    // 6. 实时采样值预览
                    string sampleVal = GetSampledTokenValue(ctrl.TelemetryToken);
                    GUILayout.Space(2f);
                    string unitSuffix = !string.IsNullOrEmpty(ctrl.TelemetryUnit) ? $" <size=9>{ctrl.TelemetryUnit}</size>" : "";
                    GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{sampleVal}</b></color>{unitSuffix}", GUILayout.Width(62f));

                    // 7. 解绑/重置
                    if (!string.IsNullOrEmpty(ctrl.TelemetryToken))
                    {
                        if (GUILayout.Button("×", GUILayout.Width(18f), GUILayout.Height(18f)))
                        {
                            ctrl.TelemetryToken = "";
                            SetChannel(w, $"{ctrl.Id.ToUpperInvariant()}_TOKEN", "");
                        }
                    }
                    else
                    {
                        GUILayout.Space(20f);
                    }

                    GUILayout.EndHorizontal();

                    // 针对支持量程的微控件 (如微型线性柱条/弧表)，提供独立量程配置行
                    if (ctrl.SupportsRange)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(20f);
                        GUILayout.Label("<size=9>量程:</size>", GUILayout.Width(35f));
                        string minStr = GUILayout.TextField(ctrl.MinValue.ToString("G"), GUILayout.Width(45f));
                        if (double.TryParse(minStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pMin) && Math.Abs(pMin - ctrl.MinValue) > 0.001)
                        {
                            ctrl.MinValue = pMin;
                            SetChannel(w, $"{ctrl.Id.ToUpperInvariant()}_MIN", pMin.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        GUILayout.Label("<size=9>~</size>", GUILayout.Width(10f));
                        string maxStr = GUILayout.TextField(ctrl.MaxValue.ToString("G"), GUILayout.Width(45f));
                        if (double.TryParse(maxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pMax) && Math.Abs(pMax - ctrl.MaxValue) > 0.001)
                        {
                            ctrl.MaxValue = pMax;
                            SetChannel(w, $"{ctrl.Id.ToUpperInvariant()}_MAX", pMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        GUILayout.EndHorizontal();
                    }

                    MFPGuiSkin.EndInset();

                    // 悬停高亮检测
                    if (Event.current.type == EventType.Repaint)
                    {
                        Rect rowRect = GUILayoutUtility.GetLastRect();
                        if (rowRect.Contains(Event.current.mousePosition))
                        {
                            WidgetControlHighlighter.HighlightedControl = ctrl;
                        }
                    }

                    GUILayout.Space(1f);
                }
            }

            // 2. 全局主数据源与量程折叠区 (若无微控件则直接展示，否则提供折叠开关)
            bool showGlobal = bindableControls.Count == 0 || _showGlobalTelemetryFoldout;
            if (bindableControls.Count > 0)
            {
                GUILayout.Space(4f);
                string foldoutTitle = _showGlobalTelemetryFoldout ? "▼ 折叠全局主数据源与量程" : "▶ 展开全局主数据源与量程";
                if (GUILayout.Button(foldoutTitle, GUILayout.Height(18f)))
                {
                    _showGlobalTelemetryFoldout = !_showGlobalTelemetryFoldout;
                }
            }

            if (showGlobal)
            {
                DrawGlobalTelemetrySettings(primary, w);
            }

            GUILayout.EndScrollView();
        }

        private void DrawGlobalTelemetrySettings(BaseFlightWidget primary, WidgetConfig w)
        {
            // 1. 主遥测驱动数据源
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("主数据源:", GUILayout.Width(62f));
            string editToken = GUILayout.TextField(w.NumericToken ?? "", GUILayout.Width(125f));
            if (editToken != w.NumericToken)
            {
                w.NumericToken = editToken;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            if (GUILayout.Button("🔍 选参数", GUILayout.Width(72f), GUILayout.Height(20f)))
            {
                TelemetryParamDrawer.Open(primary.DisplayName + " 主驱动源", chosenToken =>
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
                    WidgetLayoutManager.Instance.SaveLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                });
            }

            string sampleVal = GetSampledTokenValue(w.NumericToken);
            GUILayout.Space(4f);
            GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{sampleVal}</b></color> <size=9>{w.UnitLabel}</size>", GUILayout.Width(75f));

            if (!string.IsNullOrEmpty(w.NumericToken))
            {
                if (GUILayout.Button("解绑", GUILayout.Width(38f), GUILayout.Height(20f)))
                {
                    w.NumericToken = "";
                    WidgetLayoutManager.Instance.SaveLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
            }
            GUILayout.EndHorizontal();

            var paramMeta = TelemetryCatalog.FindByToken(w.NumericToken);
            if (paramMeta != null)
            {
                GUILayout.Label($"<color=#{MFPGuiSkin.HexTextSecondary}><size=9>• {paramMeta.DisplayName}: {paramMeta.Description}</size></color>");
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 量程下限与上限
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("量程下限:", GUILayout.Width(62f));
            string minStr = GUILayout.TextField(w.MinValue.ToString("G"), GUILayout.Width(55f));
            if (float.TryParse(minStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedMin) && Math.Abs(parsedMin - w.MinValue) > 0.001f)
            {
                w.MinValue = parsedMin;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.Space(8f);
            GUILayout.Label("量程上限:", GUILayout.Width(62f));
            string maxStr = GUILayout.TextField(w.MaxValue.ToString("G"), GUILayout.Width(55f));
            if (float.TryParse(maxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedMax) && Math.Abs(parsedMax - w.MaxValue) > 0.001f)
            {
                w.MaxValue = parsedMax;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            if (GUILayout.Button("↺ 推荐", GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                if (paramMeta != null)
                {
                    w.MinValue = (float)paramMeta.DefaultMin;
                    w.MaxValue = (float)paramMeta.DefaultMax;
                    w.CautionThreshold = (float)meta.DefaultCaution;
                    w.WarningThreshold = (float)meta.DefaultWarning;
                    w.UnitLabel = paramMeta.DefaultUnit;
                    if (w.WidgetType == "tape") w.StepInterval = paramMeta.DefaultStep;
                    WidgetLayoutManager.Instance.SaveLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 3. 黄色警戒与红色告警阈值
            GUILayout.BeginHorizontal();
            GUILayout.Label("警戒(黄):", GUILayout.Width(62f));
            string cautStr = GUILayout.TextField(w.CautionThreshold.ToString("G"), GUILayout.Width(55f));
            if (float.TryParse(cautStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCaut) && Math.Abs(parsedCaut - w.CautionThreshold) > 0.001f)
            {
                w.CautionThreshold = parsedCaut;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.Space(8f);
            GUILayout.Label("告警(红):", GUILayout.Width(62f));
            string warnStr = GUILayout.TextField(w.WarningThreshold.ToString("G"), GUILayout.Width(55f));
            if (float.TryParse(warnStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedWarn) && Math.Abs(parsedWarn - w.WarningThreshold) > 0.001f)
            {
                w.WarningThreshold = parsedWarn;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 4. 单位与步进
            GUILayout.BeginHorizontal();
            GUILayout.Label("单位角标:", GUILayout.Width(62f));
            string unitStr = GUILayout.TextField(w.UnitLabel ?? "", GUILayout.Width(55f));
            if (unitStr != w.UnitLabel)
            {
                w.UnitLabel = unitStr;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            GUILayout.Space(8f);
            GUILayout.Label("标尺步进:", GUILayout.Width(62f));
            string stepStr = GUILayout.TextField(w.StepInterval.ToString("G"), GUILayout.Width(55f));
            if (float.TryParse(stepStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStep) && Math.Abs(parsedStep - w.StepInterval) > 0.001f)
            {
                w.StepInterval = parsedStep;
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();
        }

        private void DrawChannelsTab(BaseFlightWidget primary, WidgetConfig w, float totalH)
        {
            if (w == null) return;
            _channelScrollPos = GUILayout.BeginScrollView(_channelScrollPos, GUILayout.Height(totalH - 46f));

            var channelDict = ParseChannels(w.CustomTemplate);
            for (int i = 0; i < 6; i++)
            {
                string chKey = DefaultChannelKeys[i];
                string chName = DefaultChannelNames[i];
                string defaultToken = DefaultChannelTokens[i];

                string currentVal;
                bool isCustom = channelDict.TryGetValue(chKey, out currentVal);
                if (!isCustom) currentVal = defaultToken;

                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{chKey}</b> <size=9><color=#{MFPGuiSkin.HexTextSecondary}>({chName})</color></size>", GUILayout.Width(100f));
                string editedToken = GUILayout.TextField(currentVal ?? "", GUILayout.Width(110f));
                if (editedToken != currentVal)
                {
                    SetChannel(w, chKey, editedToken);
                }

                if (GUILayout.Button("🔍 选", GUILayout.Width(45f), GUILayout.Height(20f)))
                {
                    string targetKey = chKey;
                    TelemetryParamDrawer.Open($"{primary.DisplayName} {targetKey}", token =>
                    {
                        SetChannel(w, targetKey, token);
                    });
                }

                string sampleVal = GetSampledTokenValue(currentVal);
                GUILayout.Space(4f);
                GUILayout.Label($"<color=#{MFPGuiSkin.HexAccentGreen}><b>{sampleVal}</b></color>", GUILayout.Width(65f));

                if (GUILayout.Button("↺", GUILayout.Width(22f), GUILayout.Height(20f)))
                {
                    SetChannel(w, chKey, defaultToken);
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
                GUILayout.Space(1f);
            }

            GUILayout.EndScrollView();
        }
#else
        public static bool IsSubControlCustomizerOpen => false;
#endif
    }
}

