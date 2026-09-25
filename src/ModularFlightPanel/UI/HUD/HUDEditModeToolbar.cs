using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
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

            float toolbarW = 1040f;
            float toolbarH = 78f;
            float x = (Screen.width - toolbarW) * 0.5f;
            float y = 12f;

            Rect topToolbarRect = new Rect(x, y, toolbarW, toolbarH);
            if (topToolbarRect.Contains(Event.current.mousePosition))
            {
                FlightHUDManager.IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(topToolbarRect, MFPGuiSkin.CardStyle);

            // 第一行：标题 + 撤销/重做 + 全套对齐工具
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
            float badgeH = 108f;

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

            GUILayout.EndArea();
        }
#endif
    }
}
