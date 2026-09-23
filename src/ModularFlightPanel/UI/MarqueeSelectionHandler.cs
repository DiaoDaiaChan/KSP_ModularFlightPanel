using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 编辑模式全屏框选捕获器 (Marquee Box Selection Handler)
    /// 位于画布底层，点击空白区域拖拽拉出高亮半透明矩形选框，批量框选飞行小组件
    /// </summary>
    public class MarqueeSelectionHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _parentRt;
        private Image _bgRaycastCatcher;
        private GameObject _selectionBoxObj;
        private RectTransform _selectionBoxRt;
        private Image _selectionBoxImage;
        private Outline _selectionBoxOutline;

        private Vector2 _startLocalPos;
        private bool _isDraggingBox = false;

        public void Initialize(RectTransform parentRt)
        {
            _parentRt = parentRt;

            // 1. 创建全屏空白射线拾取底板 (位于底层，不遮挡组件自身点击)
            _bgRaycastCatcher = gameObject.GetComponent<Image>();
            if (_bgRaycastCatcher == null)
            {
                _bgRaycastCatcher = gameObject.AddComponent<Image>();
            }
            _bgRaycastCatcher.color = Color.clear;
            _bgRaycastCatcher.raycastTarget = true;

            // 设为第一子项 (位于最底层)
            transform.SetAsFirstSibling();

            RectTransform rt = GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // 2. 创建视觉框选矩形
            _selectionBoxObj = new GameObject("MarqueeSelectionBox", typeof(RectTransform), typeof(Image), typeof(Outline));
            _selectionBoxObj.transform.SetParent(_parentRt, false);
            _selectionBoxRt = _selectionBoxObj.GetComponent<RectTransform>();
            _selectionBoxRt.anchorMin = Vector2.zero;
            _selectionBoxRt.anchorMax = Vector2.zero;
            _selectionBoxRt.pivot = Vector2.zero;

            _selectionBoxImage = _selectionBoxObj.GetComponent<Image>();
            _selectionBoxImage.color = new Color(0f, 0.85f, 1f, 0.15f);
            _selectionBoxImage.raycastTarget = false;

            _selectionBoxOutline = _selectionBoxObj.GetComponent<Outline>();
            _selectionBoxOutline.effectColor = new Color(0f, 1f, 0.8f, 0.9f);
            _selectionBoxOutline.effectDistance = new Vector2(1.5f, 1.5f);

            _selectionBoxObj.SetActive(false);
        }

        private void Update()
        {
            // 仅在编辑模式激活且显示 HUD 时启用空白拾取
            bool editActive = WidgetDragHandler.IsEditModeActive && (NavballHUD.Instance == null || NavballHUD.Instance.IsUIVisible);
            if (_bgRaycastCatcher != null && _bgRaycastCatcher.raycastTarget != editActive)
            {
                _bgRaycastCatcher.raycastTarget = editActive;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!WidgetDragHandler.IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar) return;

            bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                              Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (!isAdditive)
            {
                WidgetSelectionManager.ClearSelection();
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRt, eventData.position, eventData.pressEventCamera, out _startLocalPos);

            _isDraggingBox = true;
            _selectionBoxRt.anchoredPosition = _startLocalPos;
            _selectionBoxRt.sizeDelta = Vector2.zero;
            _selectionBoxObj.SetActive(true);
            _selectionBoxObj.transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDraggingBox || !WidgetDragHandler.IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRt, eventData.position, eventData.pressEventCamera, out Vector2 currentPos);

            float minX = Mathf.Min(_startLocalPos.x, currentPos.x);
            float maxX = Mathf.Max(_startLocalPos.x, currentPos.x);
            float minY = Mathf.Min(_startLocalPos.y, currentPos.y);
            float maxY = Mathf.Max(_startLocalPos.y, currentPos.y);

            float width = maxX - minX;
            float height = maxY - minY;

            _selectionBoxRt.anchoredPosition = new Vector2(minX, minY);
            _selectionBoxRt.sizeDelta = new Vector2(width, height);

            Rect marqueeRect = new Rect(minX, minY, width, height);

            // 碰撞检测：检查当前哪些小组件相交
            if (NavballHUD.Instance != null && NavballHUD.Instance.ModularWidgets != null)
            {
                bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                                  Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                foreach (var w in NavballHUD.Instance.ModularWidgets)
                {
                    if (w == null || !w.gameObject.activeInHierarchy) continue;

                    Rect widgetBounds = WidgetSelectionManager.GetWidgetBounds(w);
                    if (marqueeRect.Overlaps(widgetBounds))
                    {
                        WidgetSelectionManager.Select(w, true);
                    }
                    else if (!isAdditive)
                    {
                        WidgetSelectionManager.Deselect(w);
                    }
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDraggingBox) return;
            _isDraggingBox = false;
            if (_selectionBoxObj != null)
            {
                _selectionBoxObj.SetActive(false);
            }
            WidgetSelectionManager.NotifySelectionChanged();
        }
    }
}
