using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 编辑模式小组件多选管理器与对齐变换引擎 (Multi-Selection & Alignment Engine)
    /// </summary>
    public static class WidgetSelectionManager
    {
        private static readonly HashSet<BaseFlightWidget> _selectedWidgets = new HashSet<BaseFlightWidget>();

        public static event Action OnSelectionChanged;

        public static int Count => CleanSelection();

        public static IReadOnlyCollection<BaseFlightWidget> SelectedWidgets
        {
            get
            {
                CleanSelection();
                return _selectedWidgets;
            }
        }

        public static bool IsSelected(BaseFlightWidget widget)
        {
            if (widget == null) return false;
            return _selectedWidgets.Contains(widget);
        }

        public static void Select(BaseFlightWidget widget, bool addToSelection = false)
        {
            if (widget == null) return;
            if (!addToSelection)
            {
                _selectedWidgets.Clear();
            }
            _selectedWidgets.Add(widget);
            NotifySelectionChanged();
        }

        public static void ToggleSelect(BaseFlightWidget widget)
        {
            if (widget == null) return;
            if (_selectedWidgets.Contains(widget))
            {
                _selectedWidgets.Remove(widget);
            }
            else
            {
                _selectedWidgets.Add(widget);
            }
            NotifySelectionChanged();
        }

        public static void Deselect(BaseFlightWidget widget)
        {
            if (widget == null) return;
            if (_selectedWidgets.Remove(widget))
            {
                NotifySelectionChanged();
            }
        }

        public static void ClearSelection()
        {
            if (_selectedWidgets.Count > 0)
            {
                _selectedWidgets.Clear();
                NotifySelectionChanged();
            }
        }

        public static void SelectAll(IEnumerable<BaseFlightWidget> widgets)
        {
            _selectedWidgets.Clear();
            if (widgets != null)
            {
                foreach (var w in widgets)
                {
                    if (w != null && w.gameObject.activeInHierarchy)
                    {
                        _selectedWidgets.Add(w);
                    }
                }
            }
            NotifySelectionChanged();
        }

        public static bool SetSelection(IEnumerable<BaseFlightWidget> newSelection)
        {
            CleanSelection();
            var incoming = new HashSet<BaseFlightWidget>();
            if (newSelection != null)
            {
                foreach (var w in newSelection)
                {
                    if (w != null && w.gameObject != null && w.gameObject.activeInHierarchy)
                    {
                        incoming.Add(w);
                    }
                }
            }

            if (_selectedWidgets.SetEquals(incoming))
            {
                return false;
            }

            _selectedWidgets.Clear();
            foreach (var w in incoming)
            {
                _selectedWidgets.Add(w);
            }
            NotifySelectionChanged();
            return true;
        }

        public static void NotifySelectionChanged()
        {
            CleanSelection();
            OnSelectionChanged.SafeInvoke("OnSelectionChanged");
        }

        private static int CleanSelection()
        {
            _selectedWidgets.RemoveWhere(w => w == null || w.gameObject == null);
            return _selectedWidgets.Count;
        }

        // ==========================================
        // 几何包围盒计算 (Geometry & Bounding Box)
        // ==========================================
        public static Rect GetWidgetBounds(BaseFlightWidget w)
        {
            if (w == null || w.RectTransform == null) return Rect.zero;

            Rect r = w.RectTransform.rect;
            float scaleX = Mathf.Abs(w.RectTransform.localScale.x);
            float scaleY = Mathf.Abs(w.RectTransform.localScale.y);
            float w_px = (r.width > 10f ? r.width : 100f) * scaleX;
            float h_px = (r.height > 10f ? r.height : 80f) * scaleY;
            Vector2 pos = w.RectTransform.anchoredPosition;
            Vector2 pivot = w.RectTransform.pivot;

            float centerX = pos.x + (0.5f - pivot.x) * w_px;
            float centerY = pos.y + (0.5f - pivot.y) * h_px;

            return new Rect(centerX - w_px * 0.5f, centerY - h_px * 0.5f, w_px, h_px);
        }

        // ==========================================
        // 批量对齐算法 (Alignment Algorithms)
        // ==========================================
        public static void AlignLeft()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_LEFT", "左对齐"), () =>
            {
                float minX = list.Min(w => GetWidgetBounds(w).xMin);
                foreach (var w in list)
                {
                    var b = GetWidgetBounds(w);
                    float newX = minX + b.width * 0.5f;
                    w.UpdateTransform(x: newX);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void AlignCenterX()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_HCENTER", "水平居中"), () =>
            {
                float avgX = list.Average(w => w.RectTransform.anchoredPosition.x);
                foreach (var w in list)
                {
                    w.UpdateTransform(x: avgX);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void AlignRight()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_RIGHT", "右对齐"), () =>
            {
                float maxX = list.Max(w => GetWidgetBounds(w).xMax);
                foreach (var w in list)
                {
                    var b = GetWidgetBounds(w);
                    float newX = maxX - b.width * 0.5f;
                    w.UpdateTransform(x: newX);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void AlignTop()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_TOP", "顶对齐"), () =>
            {
                float maxY = list.Max(w => GetWidgetBounds(w).yMax);
                foreach (var w in list)
                {
                    var b = GetWidgetBounds(w);
                    float newY = maxY - b.height * 0.5f;
                    w.UpdateTransform(y: newY);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void AlignCenterY()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_VCENTER", "垂直居中"), () =>
            {
                float avgY = list.Average(w => w.RectTransform.anchoredPosition.y);
                foreach (var w in list)
                {
                    w.UpdateTransform(y: avgY);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void AlignBottom()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_BOTTOM", "底对齐"), () =>
            {
                float minY = list.Min(w => GetWidgetBounds(w).yMin);
                foreach (var w in list)
                {
                    var b = GetWidgetBounds(w);
                    float newY = minY + b.height * 0.5f;
                    w.UpdateTransform(y: newY);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void DistributeHorizontally()
        {
            var list = SelectedWidgets.OrderBy(w => w.RectTransform.anchoredPosition.x).ToList();
            if (list.Count < 3) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_DISTRIBUTE_H", "水平等距分布"), () =>
            {
                float minX = list.First().RectTransform.anchoredPosition.x;
                float maxX = list.Last().RectTransform.anchoredPosition.x;
                float step = (maxX - minX) / (list.Count - 1);

                for (int i = 1; i < list.Count - 1; i++)
                {
                    float targetX = minX + i * step;
                    list[i].UpdateTransform(x: targetX);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void DistributeVertically()
        {
            var list = SelectedWidgets.OrderBy(w => w.RectTransform.anchoredPosition.y).ToList();
            if (list.Count < 3) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_DISTRIBUTE_V", "垂直等距分布"), () =>
            {
                float minY = list.First().RectTransform.anchoredPosition.y;
                float maxY = list.Last().RectTransform.anchoredPosition.y;
                float step = (maxY - minY) / (list.Count - 1);

                for (int i = 1; i < list.Count - 1; i++)
                {
                    float targetY = minY + i * step;
                    list[i].UpdateTransform(y: targetY);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void CenterToScreenX()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count == 0) return;

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_ALIGN_AXIS_X0", "对齐至机体对称轴(X=0)"), () =>
            {
                if (list.Count == 1)
                {
                    list[0].UpdateTransform(x: 0f);
                }
                else
                {
                    float minX = list.Min(w => GetWidgetBounds(w).xMin);
                    float maxX = list.Max(w => GetWidgetBounds(w).xMax);
                    float groupCenterX = (minX + maxX) * 0.5f;
                    float offset = -groupCenterX;

                    foreach (var w in list)
                    {
                        w.UpdateTransform(x: w.RectTransform.anchoredPosition.x + offset);
                    }
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void Nudge(Vector2 delta)
        {
            if (Count == 0) return;
            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_KEY_NUDGE", "键盘微调位移"), () =>
            {
                BatchMove(delta);
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void BringToFront()
        {
            WidgetLayerManager.BringToFront(SelectedWidgets);
        }

        public static void SendToBack()
        {
            WidgetLayerManager.SendToBack(SelectedWidgets);
        }

        public static void BringForward()
        {
            WidgetLayerManager.BringForward(SelectedWidgets);
        }

        public static void SendBackward()
        {
            WidgetLayerManager.SendBackward(SelectedWidgets);
        }

        public static void DeleteSelected()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count == 0) return;
            WidgetEditHistory.RecordInstantAction(I18n.TrFormat("HIST_HIDE_WIDGETS_FMT", list.Count), () =>
            {
                foreach (var w in list)
                {
                    if (w != null)
                    {
                        if (w.Config != null) w.Config.IsEnabled = false;
                        w.gameObject.SetActive(false);
                    }
                }
                ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
            });
            MFPToastBridge.Show(I18n.Tr("TOAST_HIDDEN_CTRLZ", "已隐藏选中的组件 (Ctrl+Z 可撤销)"));
        }

        // ==========================================
        // 批量平移、缩放与旋转 (Batch Transformations)
        // ==========================================
        public static void BatchMove(Vector2 delta)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.RectTransform != null && (w.Config == null || !w.Config.IsLocked))
                {
                    Vector2 newPos = w.RectTransform.anchoredPosition + delta;
                    w.UpdateTransform(x: newPos.x, y: newPos.y);
                }
            }
        }

        public static void BatchScale(float deltaScale, bool commit = true)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    float currentScale = (w.Config.Scale > 0.01f) ? w.Config.Scale : 1.0f;
                    float newScale = Mathf.Clamp(currentScale + deltaScale, 0.2f, 4.0f);
                    newScale = Mathf.Round(newScale * 20f) / 20f;
                    w.UpdateTransform(scale: newScale);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchSetScale(float targetScale, bool commit = true)
        {
            float clamped = Mathf.Clamp(targetScale, 0.2f, 4.0f);
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    w.UpdateTransform(scale: clamped);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchSetScaleXY(float? targetScaleX, float? targetScaleY, bool commit = true)
        {
            float? clampedX = targetScaleX.HasValue ? (float?)Mathf.Clamp(targetScaleX.Value, 0.2f, 4.0f) : null;
            float? clampedY = targetScaleY.HasValue ? (float?)Mathf.Clamp(targetScaleY.Value, 0.2f, 4.0f) : null;
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    w.UpdateTransform(scaleX: clampedX, scaleY: clampedY);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchAdjustScaleXY(float deltaX, float deltaY, bool commit = true)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    float currentX = w.Config.EffectiveScaleX;
                    float currentY = w.Config.EffectiveScaleY;
                    float newX = Mathf.Clamp(currentX + deltaX, 0.2f, 4.0f);
                    float newY = Mathf.Clamp(currentY + deltaY, 0.2f, 4.0f);
                    newX = Mathf.Round(newX * 20f) / 20f;
                    newY = Mathf.Round(newY * 20f) / 20f;
                    w.UpdateTransform(scaleX: newX, scaleY: newY);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchSetAspectRatio(float targetAspectRatio, bool commit = true)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    w.SetAspectRatio(targetAspectRatio, save: false);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchResetAspectRatio(bool commit = true)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    w.ResetAspectRatio(save: false);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchSetOpacity(float opacity)
        {
            float clamped = Mathf.Clamp(opacity, 0.05f, 1.0f);
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
                {
                    w.SetOpacity(clamped, save: false);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchAdjustOpacity(float delta)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
                {
                    float cur = w.Opacity;
                    float next = Mathf.Clamp(cur + delta, 0.05f, 1.0f);
                    next = Mathf.Round(next * 20f) / 20f;
                    w.SetOpacity(next, save: false);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchSetThemeOverride(string themeId)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
                {
                    w.SetThemeOverride(themeId, save: false);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchRotate(float deltaAngle)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    float newAngle = (w.Config.Rotation + deltaAngle) % 360f;
                    if (newAngle < 0f) newAngle += 360f;
                    w.UpdateTransform(rotation: newAngle);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchSetRotation(float targetRotation)
        {
            float clamped = (targetRotation % 360f + 360f) % 360f;
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null && !w.Config.IsLocked)
                {
                    w.UpdateTransform(rotation: clamped);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void ResetRotation()
        {
            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_RESET_ROT", "复位旋转至0°"), () =>
            {
                foreach (var w in SelectedWidgets)
                {
                    if (w != null) w.UpdateTransform(rotation: 0f);
                }
                WidgetLayoutManager.Instance.SaveLayout();
            });
        }

        public static void ResetScale()
        {
            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_RESET_SCALE", "复位缩放至1.0x"), () =>
            {
                foreach (var w in SelectedWidgets)
                {
                    if (w != null) w.UpdateTransform(scale: 1.0f);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            });
        }

        public static void BatchScaleRelative(Dictionary<BaseFlightWidget, float> initialScales, float scaleFactor, bool commit = false)
        {
            if (initialScales == null || initialScales.Count == 0) return;
            foreach (var kvp in initialScales)
            {
                var w = kvp.Key;
                if (w != null && w.Config != null)
                {
                    float targetScale = Mathf.Clamp(kvp.Value * scaleFactor, 0.2f, 4.0f);
                    w.UpdateTransform(scale: targetScale);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        public static void BatchScaleRelativeXY(Dictionary<BaseFlightWidget, Vector2> initialScalesXY, float? factorX, float? factorY, bool commit = false)
        {
            if (initialScalesXY == null || initialScalesXY.Count == 0) return;
            foreach (var kvp in initialScalesXY)
            {
                var w = kvp.Key;
                if (w != null && w.Config != null)
                {
                    float? targetX = factorX.HasValue ? (float?)Mathf.Clamp(kvp.Value.x * factorX.Value, 0.2f, 4.0f) : null;
                    float? targetY = factorY.HasValue ? (float?)Mathf.Clamp(kvp.Value.y * factorY.Value, 0.2f, 4.0f) : null;
                    w.UpdateTransform(scaleX: targetX, scaleY: targetY);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
            if (commit)
            {
                FlightHUDManager.Instance?.RespawnWidgets(SelectedWidgets);
            }
        }

        /// <summary>
        /// 全局编辑模式热键单例轮询（已解耦并委托至 EditModeShortcutHandler）
        /// </summary>
        public static void HandleGlobalShortcuts()
        {
            EditModeShortcutHandler.HandleGlobalShortcuts();
        }
    }

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

    /// <summary>
    /// 编辑模式小组件剪贴板与复用中枢 (Widget Clipboard & Cloning Engine)
    /// 支持单选/多选组件的复制 (Ctrl+C)、粘贴 (Ctrl+V) 与快捷克隆 (Ctrl+D)。
    /// 自动分配全局唯一 WidgetId、智能递增偏移排版，并记录撤销历史。
    /// </summary>
    public static class WidgetClipboardManager
    {
        private static readonly List<WidgetConfig> _clipboard = new List<WidgetConfig>();
        private static int _consecutivePasteCount = 0;

        public static int ClipboardCount => _clipboard.Count;
        public static bool HasData => _clipboard.Count > 0;

        /// <summary>
        /// 复制指定或当前选中的小组件到剪贴板
        /// </summary>
        public static void CopySelected(IEnumerable<BaseFlightWidget> targets = null)
        {
            var list = (targets ?? WidgetSelectionManager.SelectedWidgets).Where(w => w != null && w.Config != null).ToList();
            if (list.Count == 0)
            {
                MFPToastBridge.Show(I18n.Tr("TOAST_CLIPBOARD_NO_SELECTION", "未选择可复制的小组件"));
                return;
            }

            _clipboard.Clear();
            foreach (var w in list)
            {
                _clipboard.Add(w.Config.Clone());
            }

            _consecutivePasteCount = 0;
            MFPToastBridge.Show(I18n.TrFormat("TOAST_CLIPBOARD_COPIED", _clipboard.Count));
        }

        /// <summary>
        /// 从剪贴板粘贴小组件实例
        /// </summary>
        public static void Paste()
        {
            if (_clipboard.Count == 0)
            {
                MFPToastBridge.Show(I18n.Tr("TOAST_CLIPBOARD_EMPTY", "剪贴板为空，请先按 Ctrl+C 复制组件"));
                return;
            }

            var layout = WidgetLayoutManager.Instance?.CurrentLayout;
            if (layout == null || layout.Widgets == null) return;

            _consecutivePasteCount++;
            Vector2 stepOffset = new Vector2(24f * _consecutivePasteCount, -24f * _consecutivePasteCount);

            var createdIds = new List<string>();

            WidgetEditHistory.RecordInstantAction(I18n.Tr("HIST_PASTE_WIDGETS", "粘贴小组件"), () =>
            {
                foreach (var srcCfg in _clipboard)
                {
                    string uniqueId = GenerateUniqueWidgetId(srcCfg.WidgetId, srcCfg.WidgetType, layout);
                    var newCfg = srcCfg.Clone(uniqueId, stepOffset.x, stepOffset.y);
                    newCfg.IsEnabled = true;

                    // 确保 WidgetType 绝不为空，方便泛型工厂精准实例化
                    if (string.IsNullOrEmpty(newCfg.WidgetType))
                    {
                        newCfg.WidgetType = srcCfg.WidgetType;
                    }

                    layout.Widgets.Add(newCfg);
                    createdIds.Add(uniqueId);
                }

                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();

                // 重建后自动框选新粘贴出的所有组件
                if (FlightHUDManager.Instance?.ModularWidgets != null)
                {
                    var newWidgets = FlightHUDManager.Instance.ModularWidgets
                        .Where(w => w != null && createdIds.Contains(w.WidgetId, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    WidgetSelectionManager.SetSelection(newWidgets);
                }
            });

            MFPToastBridge.Show(I18n.TrFormat("TOAST_CLIPBOARD_PASTED", createdIds.Count));
        }

        /// <summary>
        /// 快捷克隆选中的组件 (Ctrl+D)
        /// </summary>
        public static void DuplicateSelected(IEnumerable<BaseFlightWidget> targets = null)
        {
            var list = (targets ?? WidgetSelectionManager.SelectedWidgets).Where(w => w != null && w.Config != null).ToList();
            if (list.Count == 0) return;

            CopySelected(list);
            Paste();
        }

        /// <summary>
        /// 为克隆或新建组件生成全局唯一的 WidgetId
        /// </summary>
        public static string GenerateUniqueWidgetId(string baseId, string typeName, WidgetLayoutData layout)
        {
            string prefix = baseId;
            if (string.IsNullOrEmpty(prefix))
            {
                prefix = !string.IsNullOrEmpty(typeName) ? typeName : "widget";
            }

            // 清理末尾现有的 _copy 或 _copyX
            int copyIdx = prefix.IndexOf("_copy", StringComparison.OrdinalIgnoreCase);
            if (copyIdx > 0)
            {
                prefix = prefix.Substring(0, copyIdx);
            }

            string candidate = $"{prefix}_copy";
            int counter = 1;
            while (layout != null && layout.Widgets != null && layout.Widgets.Exists(w => string.Equals(w.WidgetId, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{prefix}_copy{counter++}";
            }
            return candidate;
        }
    }
}
