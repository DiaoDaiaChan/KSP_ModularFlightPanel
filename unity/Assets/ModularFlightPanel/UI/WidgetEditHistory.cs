using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 单个小组件在特定历史帧的几何与状态快照
    /// </summary>
    public struct WidgetSnapshotState
    {
        public string WidgetId;
        public float PositionX;
        public float PositionY;
        public float Scale;
        public float Rotation;
        public int SiblingIndex;
        public bool IsEnabled;
    }

    /// <summary>
    /// 单步撤销/重做操作实体
    /// </summary>
    public class EditHistoryStep
    {
        public string Description;
        public List<WidgetSnapshotState> BeforeStates = new List<WidgetSnapshotState>();
        public List<WidgetSnapshotState> AfterStates = new List<WidgetSnapshotState>();
    }

    /// <summary>
    /// 专业图形软件级撤销/重做历史记录栈 (Figma/Photoshop-style Undo/Redo Engine)
    /// 涵盖组件拖拽位移、手柄缩放、手柄旋转、对齐分布、分层调节与删除/显隐全动作。
    /// 支持快捷键：
    /// - Ctrl + Z: 撤销 (Undo)
    /// - Ctrl + Y / Ctrl + Shift + Z: 重做 (Redo)
    /// </summary>
    public static class WidgetEditHistory
    {
        private const int MaxHistoryCapacity = 40;
        private static readonly List<EditHistoryStep> _undoStack = new List<EditHistoryStep>();
        private static readonly List<EditHistoryStep> _redoStack = new List<EditHistoryStep>();

        private static List<WidgetSnapshotState> _pendingBeforeState = null;

        public static bool CanUndo => _undoStack.Count > 0;
        public static bool CanRedo => _redoStack.Count > 0;

        public static event Action OnHistoryChanged;

        /// <summary>
        /// 在一次操作开始前（如按下鼠标拖拽、开始缩放、开始对齐），捕获初始状态
        /// </summary>
        public static void BeginAction()
        {
            _pendingBeforeState = CaptureCurrentState();
        }

        /// <summary>
        /// 操作完成后（如松开鼠标拖拽、对齐算法执行完毕），提交动作生成历史步
        /// </summary>
        public static void CommitAction(string description)
        {
            if (_pendingBeforeState == null) return;

            var afterState = CaptureCurrentState();

            // 检查是否有实质变化，无实质变化则不浪费历史槽位
            if (!HasStateChanged(_pendingBeforeState, afterState))
            {
                _pendingBeforeState = null;
                return;
            }

            var step = new EditHistoryStep
            {
                Description = description,
                BeforeStates = _pendingBeforeState,
                AfterStates = afterState
            };

            _undoStack.Add(step);
            if (_undoStack.Count > MaxHistoryCapacity)
            {
                _undoStack.RemoveAt(0);
            }

            _redoStack.Clear();
            _pendingBeforeState = null;

            OnHistoryChanged?.Invoke();
        }

        /// <summary>
        /// 直接记录一次瞬时动作（如点击对齐、等距分布、复位等无需持续拖拽的操作）
        /// </summary>
        public static void RecordInstantAction(string description, Action action)
        {
            var before = CaptureCurrentState();
            action?.Invoke();
            var after = CaptureCurrentState();

            if (HasStateChanged(before, after))
            {
                var step = new EditHistoryStep
                {
                    Description = description,
                    BeforeStates = before,
                    AfterStates = after
                };

                _undoStack.Add(step);
                if (_undoStack.Count > MaxHistoryCapacity)
                {
                    _undoStack.RemoveAt(0);
                }

                _redoStack.Clear();
                OnHistoryChanged?.Invoke();
            }
        }

        /// <summary>
        /// 执行撤销 (Ctrl+Z)
        /// </summary>
        public static bool Undo()
        {
            if (!CanUndo) return false;

            int lastIdx = _undoStack.Count - 1;
            var step = _undoStack[lastIdx];
            _undoStack.RemoveAt(lastIdx);

            ApplyState(step.BeforeStates);
            _redoStack.Add(step);

            MFPToastBridge.Show($"↶ 撤销: {step.Description}");
            OnHistoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 执行重做 (Ctrl+Y / Ctrl+Shift+Z)
        /// </summary>
        public static bool Redo()
        {
            if (!CanRedo) return false;

            int lastIdx = _redoStack.Count - 1;
            var step = _redoStack[lastIdx];
            _redoStack.RemoveAt(lastIdx);

            ApplyState(step.AfterStates);
            _undoStack.Add(step);

            MFPToastBridge.Show($"↷ 重做: {step.Description}");
            OnHistoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 清空历史记录
        /// </summary>
        public static void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _pendingBeforeState = null;
            OnHistoryChanged?.Invoke();
        }

        /// <summary>
        /// 检测并响应 Undo / Redo 全局热键
        /// </summary>
        public static void HandleHotkeys()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrl && !shift && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
            }
            else if (ctrl && ((shift && Input.GetKeyDown(KeyCode.Z)) || Input.GetKeyDown(KeyCode.Y)))
            {
                Redo();
            }
        }

        private static List<WidgetSnapshotState> CaptureCurrentState()
        {
            var list = new List<WidgetSnapshotState>();
            if (NavballHUD.Instance == null || NavballHUD.Instance.ModularWidgets == null) return list;

            foreach (var w in NavballHUD.Instance.ModularWidgets)
            {
                if (w == null || w.Config == null || w.RectTransform == null) continue;

                list.Add(new WidgetSnapshotState
                {
                    WidgetId = w.WidgetId,
                    PositionX = w.RectTransform.anchoredPosition.x,
                    PositionY = w.RectTransform.anchoredPosition.y,
                    Scale = w.Config.Scale,
                    Rotation = w.Config.Rotation,
                    SiblingIndex = w.transform.GetSiblingIndex(),
                    IsEnabled = w.Config.IsEnabled
                });
            }

            return list;
        }

        private static void ApplyState(List<WidgetSnapshotState> states)
        {
            if (states == null || NavballHUD.Instance == null || NavballHUD.Instance.ModularWidgets == null) return;

            var lookup = new Dictionary<string, BaseFlightWidget>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in NavballHUD.Instance.ModularWidgets)
            {
                if (w != null && !string.IsNullOrEmpty(w.WidgetId))
                {
                    lookup[w.WidgetId] = w;
                }
            }

            foreach (var state in states)
            {
                if (lookup.TryGetValue(state.WidgetId, out var widget) && widget != null)
                {
                    widget.UpdateTransform(state.PositionX, state.PositionY, state.Scale, state.Rotation);
                    widget.transform.SetSiblingIndex(state.SiblingIndex);

                    if (widget.Config != null)
                    {
                        widget.Config.IsEnabled = state.IsEnabled;
                    }
                    widget.gameObject.SetActive(state.IsEnabled);
                }
            }

            WidgetLayoutManager.Instance.SaveLayout();
            WidgetSelectionManager.NotifySelectionChanged();
        }

        private static bool HasStateChanged(List<WidgetSnapshotState> a, List<WidgetSnapshotState> b)
        {
            if (a.Count != b.Count) return true;

            var mapB = new Dictionary<string, WidgetSnapshotState>(b.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < b.Count; i++) mapB[b[i].WidgetId] = b[i];

            for (int i = 0; i < a.Count; i++)
            {
                if (!mapB.TryGetValue(a[i].WidgetId, out var stateB)) return true;

                if (Mathf.Abs(a[i].PositionX - stateB.PositionX) > 0.01f ||
                    Mathf.Abs(a[i].PositionY - stateB.PositionY) > 0.01f ||
                    Mathf.Abs(a[i].Scale - stateB.Scale) > 0.005f ||
                    Mathf.Abs(a[i].Rotation - stateB.Rotation) > 0.05f ||
                    a[i].SiblingIndex != stateB.SiblingIndex ||
                    a[i].IsEnabled != stateB.IsEnabled)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
