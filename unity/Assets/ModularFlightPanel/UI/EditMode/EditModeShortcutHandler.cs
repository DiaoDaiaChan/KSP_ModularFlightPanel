using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 编辑模式全局快捷键调度器 (Edit Mode Global Shortcut & Input Dispatcher)
    /// 集中处理键盘输入、鼠标滚轮缩放旋转、方向键微调与层级切换，解耦选择管理器的几何运算与输入轮询。
    /// </summary>
    public static class EditModeShortcutHandler
    {
        public static void HandleGlobalShortcuts()
        {
            if (!WidgetDragHandler.IsEditModeActive) return;

            // 0. 全局撤销/重做 (Ctrl+Z / Ctrl+Y)
            WidgetEditHistory.HandleHotkeys();

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 1. 全选 (Ctrl + A)
            if (ctrl && Input.GetKeyDown(KeyCode.A))
            {
                if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null)
                {
                    WidgetSelectionManager.SelectAll(FlightHUDManager.Instance.ModularWidgets);
                    MFPToastBridge.Show(I18n.Tr("TOAST_SELECT_ALL", "已全选所有小组件"));
                }
                return;
            }

            // 1.1 复制 / 粘贴 / 克隆 (Ctrl + C / Ctrl + V / Ctrl + D)
            if (ctrl && Input.GetKeyDown(KeyCode.C))
            {
                WidgetClipboardManager.CopySelected();
                return;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.V))
            {
                WidgetClipboardManager.Paste();
                return;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.D))
            {
                WidgetClipboardManager.DuplicateSelected();
                return;
            }

            // 2. 切换蓝图辅助网格 (G 键)
            if (Input.GetKeyDown(KeyCode.G) && !ctrl)
            {
                WidgetCanvasGrid.ToggleGrid();
                return;
            }

            // 3. 切换图层管理面板 (L 键)
            if (Input.GetKeyDown(KeyCode.L) && !ctrl)
            {
                WidgetLayerManager.ToggleLayerPanel();
                return;
            }

            if (WidgetSelectionManager.Count == 0) return;

            // 4. 像素级方向键微调 (Arrow Keys Nudge)
            float nudge = shift ? 10f : (ctrl ? 5f : 1f);
            if (Input.GetKeyDown(KeyCode.UpArrow)) WidgetSelectionManager.Nudge(new Vector2(0f, nudge));
            else if (Input.GetKeyDown(KeyCode.DownArrow)) WidgetSelectionManager.Nudge(new Vector2(0f, -nudge));
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) WidgetSelectionManager.Nudge(new Vector2(-nudge, 0f));
            else if (Input.GetKeyDown(KeyCode.RightArrow)) WidgetSelectionManager.Nudge(new Vector2(nudge, 0f));

            // 5. 图层层级移动：
            // ] 上移一层，Shift+] 或 Ctrl+] 置于顶层
            // [ 下移一层，Shift+[ 或 Ctrl+[ 置于底层
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                if (shift || ctrl) WidgetSelectionManager.BringToFront();
                else WidgetSelectionManager.BringForward();
            }
            else if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                if (shift || ctrl) WidgetSelectionManager.SendToBack();
                else WidgetSelectionManager.SendBackward();
            }

            // 6. 快速隐藏/删除选中组件 (Delete / Backspace)
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                WidgetSelectionManager.DeleteSelected();
                return;
            }

            // 7. 快捷复位 (R 复位旋转，0 复位缩放)
            if (Input.GetKeyDown(KeyCode.R) && !ctrl)
            {
                WidgetSelectionManager.ResetRotation();
            }
            else if ((Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) && !ctrl)
            {
                WidgetSelectionManager.ResetScale();
            }

            // 8. 滚轮辅助缩放与旋转
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (ctrl && !shift && Math.Abs(scroll) > 0.001f)
            {
                float deltaScale = scroll > 0f ? 0.05f : -0.05f;
                WidgetEditHistory.BeginAction();
                WidgetSelectionManager.BatchScale(deltaScale);
                WidgetEditHistory.CommitAction(I18n.Tr("HIST_SCROLL_SCALE", "滚轮缩放"));
                WidgetLayoutManager.Instance.SaveLayout();
            }
            else if (shift && Math.Abs(scroll) > 0.001f)
            {
                float step = ctrl ? 15f : 5f;
                float deltaAngle = scroll > 0f ? step : -step;
                WidgetEditHistory.BeginAction();
                WidgetSelectionManager.BatchRotate(deltaAngle);
                WidgetEditHistory.CommitAction(I18n.Tr("HIST_SCROLL_ROT", "滚轮旋转"));
                WidgetLayoutManager.Instance.SaveLayout();
            }
        }
    }
}
