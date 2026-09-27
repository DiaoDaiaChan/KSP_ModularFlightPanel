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

            // 5. 子控件微调与显隐入口 (仅在单选且拥有注册微控件时展示)
            if (selCount == 1)
            {
                var ctrlList = primary.Controls.All;
                int totalCtrls = ctrlList.Count;
                if (totalCtrls > 0)
                {
                    int visCtrls = 0;
                    for (int i = 0; i < totalCtrls; i++) if (ctrlList[i].IsVisible) visCtrls++;
                    GUILayout.Space(2f);
                    GUILayout.BeginHorizontal();
                    string btnTxt = _showSubControlInspector ? $"⚙️ 控件定制: [已展开] ({visCtrls}/{totalCtrls})" : $"⚙️ 控件定制 ({visCtrls}/{totalCtrls})";
                    GUI.color = _showSubControlInspector ? new Color(0f, 0.9f, 1f) : Color.white;
                    if (GUILayout.Button(btnTxt, GUILayout.Height(20f)))
                    {
                        _showSubControlInspector = !_showSubControlInspector;
                    }
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndArea();

            if (_showSubControlInspector && selCount == 1 && primary.Controls.All.Count > 0)
            {
                DrawSubControlInspector(primary, badgeRect);
            }
        }

        private static bool _showSubControlInspector = false;
        private static Vector2 _subControlScrollPos = Vector2.zero;

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

        private void DrawSubControlInspector(BaseFlightWidget primary, Rect badgeRect)
        {
            if (primary == null) return;
            var ctrlList = primary.Controls.All;
            if (ctrlList.Count == 0) return;

            float subW = 345f;
            float subH = Mathf.Min(260f, 65f + ctrlList.Count * 28f);

            // 优先置于 badgeRect 下方
            float subX = badgeRect.x;
            float subY = badgeRect.y + badgeRect.height + 6f;

            // 若下方超出屏幕边缘，则自适应翻转至上方
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

            // 标题栏
            GUILayout.BeginHorizontal();
            GUILayout.Label($"⚙️ <b>{primary.DisplayName}</b> - 子控件定制", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("×", GUILayout.Width(22f), GUILayout.Height(18f)))
            {
                _showSubControlInspector = false;
            }
            GUILayout.EndHorizontal();

            // 批处理工具栏
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
            GUILayout.Label("<color=#7088A8><size=9>Shift:10px步进</size></color>");
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 列表
            _subControlScrollPos = GUILayout.BeginScrollView(_subControlScrollPos, GUILayout.Height(subH - 52f));
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
                    GUILayout.Label("<color=#506070><size=9>0,0</size></color>", GUILayout.Width(38f));
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

            GUILayout.EndArea();
        }
#endif
    }
}
