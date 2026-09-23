using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Config;

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

        public static void NotifySelectionChanged()
        {
            CleanSelection();
            OnSelectionChanged?.Invoke();
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

            float scale = (w.Config != null && w.Config.Scale > 0.01f) ? w.Config.Scale : 1f;
            Rect r = w.RectTransform.rect;
            float w_px = (r.width > 10f ? r.width : 100f) * scale;
            float h_px = (r.height > 10f ? r.height : 80f) * scale;
            Vector2 pos = w.RectTransform.anchoredPosition;

            return new Rect(pos.x - w_px * 0.5f, pos.y - h_px * 0.5f, w_px, h_px);
        }

        // ==========================================
        // 批量对齐算法 (Alignment Algorithms)
        // ==========================================
        public static void AlignLeft()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float minX = list.Min(w => GetWidgetBounds(w).xMin);
            foreach (var w in list)
            {
                var b = GetWidgetBounds(w);
                float newX = minX + b.width * 0.5f;
                w.UpdateTransform(x: newX);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void AlignCenterX()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float avgX = list.Average(w => w.RectTransform.anchoredPosition.x);
            foreach (var w in list)
            {
                w.UpdateTransform(x: avgX);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void AlignRight()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float maxX = list.Max(w => GetWidgetBounds(w).xMax);
            foreach (var w in list)
            {
                var b = GetWidgetBounds(w);
                float newX = maxX - b.width * 0.5f;
                w.UpdateTransform(x: newX);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void AlignTop()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float maxY = list.Max(w => GetWidgetBounds(w).yMax);
            foreach (var w in list)
            {
                var b = GetWidgetBounds(w);
                float newY = maxY - b.height * 0.5f;
                w.UpdateTransform(y: newY);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void AlignCenterY()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float avgY = list.Average(w => w.RectTransform.anchoredPosition.y);
            foreach (var w in list)
            {
                w.UpdateTransform(y: avgY);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void AlignBottom()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count < 2) return;

            float minY = list.Min(w => GetWidgetBounds(w).yMin);
            foreach (var w in list)
            {
                var b = GetWidgetBounds(w);
                float newY = minY + b.height * 0.5f;
                w.UpdateTransform(y: newY);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void DistributeHorizontally()
        {
            var list = SelectedWidgets.OrderBy(w => w.RectTransform.anchoredPosition.x).ToList();
            if (list.Count < 3) return;

            float minX = list.First().RectTransform.anchoredPosition.x;
            float maxX = list.Last().RectTransform.anchoredPosition.x;
            float step = (maxX - minX) / (list.Count - 1);

            for (int i = 1; i < list.Count - 1; i++)
            {
                float targetX = minX + i * step;
                list[i].UpdateTransform(x: targetX);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void DistributeVertically()
        {
            var list = SelectedWidgets.OrderBy(w => w.RectTransform.anchoredPosition.y).ToList();
            if (list.Count < 3) return;

            float minY = list.First().RectTransform.anchoredPosition.y;
            float maxY = list.Last().RectTransform.anchoredPosition.y;
            float step = (maxY - minY) / (list.Count - 1);

            for (int i = 1; i < list.Count - 1; i++)
            {
                float targetY = minY + i * step;
                list[i].UpdateTransform(y: targetY);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void CenterToScreenX()
        {
            var list = SelectedWidgets.ToList();
            if (list.Count == 0) return;

            if (list.Count == 1)
            {
                list[0].UpdateTransform(x: 0f);
            }
            else
            {
                // 将多选群组的整体包围盒中心对准 X = 0
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
        }

        // ==========================================
        // 批量平移、缩放与旋转 (Batch Transformations)
        // ==========================================
        public static void BatchMove(Vector2 delta)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.RectTransform != null)
                {
                    Vector2 newPos = w.RectTransform.anchoredPosition + delta;
                    w.UpdateTransform(x: newPos.x, y: newPos.y);
                }
            }
        }

        public static void BatchScale(float deltaScale)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
                {
                    float currentScale = (w.Config.Scale > 0.01f) ? w.Config.Scale : 1.0f;
                    float newScale = Mathf.Clamp(currentScale + deltaScale, 0.2f, 4.0f);
                    newScale = Mathf.Round(newScale * 20f) / 20f;
                    w.UpdateTransform(scale: newScale);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchSetScale(float targetScale)
        {
            float clamped = Mathf.Clamp(targetScale, 0.2f, 4.0f);
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
                {
                    w.UpdateTransform(scale: clamped);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void BatchRotate(float deltaAngle)
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null && w.Config != null)
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
                if (w != null && w.Config != null)
                {
                    w.UpdateTransform(rotation: clamped);
                }
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void ResetRotation()
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null) w.UpdateTransform(rotation: 0f);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }

        public static void ResetScale()
        {
            foreach (var w in SelectedWidgets)
            {
                if (w != null) w.UpdateTransform(scale: 1.0f);
            }
            WidgetLayoutManager.Instance.SaveLayout();
        }
    }
}
