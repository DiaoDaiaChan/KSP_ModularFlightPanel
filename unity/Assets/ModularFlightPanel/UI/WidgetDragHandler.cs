using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 挂载在任意组件根物体上的通用自由拖拽、多选批量移动、磁吸对齐与快捷缩放/旋转交互器
    /// </summary>
    public class WidgetDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public static bool IsEditModeActive { get; set; } = false;
        public static bool EnableMagneticSnap { get; set; } = true;

        private RectTransform _rectTransform;
        private Canvas _canvas;
        private BaseFlightWidget _ownerWidget;

        private GameObject _editOverlay;
        private Image _overlayImage;
        private Outline _editOutline;
        private Image _ringImg;
        private Text _editTitleText;

        private bool _isHovered = false;

        public void Initialize(BaseFlightWidget owner, Canvas canvas)
        {
            _ownerWidget = owner;
            _canvas = canvas;
            _rectTransform = GetComponent<RectTransform>();

            CreateEditVisuals();
            UpdateEditVisuals();

            WidgetSelectionManager.OnSelectionChanged += UpdateSelectionAppearance;
        }

        private void OnDestroy()
        {
            WidgetSelectionManager.OnSelectionChanged -= UpdateSelectionAppearance;
        }

        private void CreateEditVisuals()
        {
            // 创建编辑模式专用的高亮提示边框与标题把手
            _editOverlay = new GameObject("EditOverlay", typeof(RectTransform), typeof(Image));
            _editOverlay.transform.SetParent(transform, false);

            RectTransform rt = _editOverlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            _overlayImage = _editOverlay.GetComponent<Image>();
            _overlayImage.color = new Color(0f, 0.8f, 1f, 0.08f);
            _overlayImage.raycastTarget = true;

            bool isNavball = _ownerWidget is Widgets.NavballSphereWidget;
            if (isNavball)
            {
                _overlayImage.sprite = Core.NavballMarkerFactory.GetCircleMaskSprite();

                GameObject ringObj = new GameObject("CircleEditRing", typeof(RectTransform), typeof(Image));
                ringObj.transform.SetParent(_editOverlay.transform, false);
                RectTransform ringRt = ringObj.GetComponent<RectTransform>();
                ringRt.anchorMin = Vector2.zero;
                ringRt.anchorMax = Vector2.one;
                ringRt.sizeDelta = Vector2.zero;
                ringRt.anchoredPosition = Vector2.zero;

                _ringImg = ringObj.GetComponent<Image>();
                _ringImg.sprite = Core.NavballMarkerFactory.GetCircleRingSprite();
                _ringImg.color = new Color(0f, 1f, 0.8f, 0.9f);
                _ringImg.raycastTarget = false;
            }
            else
            {
                _editOutline = _editOverlay.AddComponent<Outline>();
                _editOutline.effectColor = new Color(0f, 1f, 0.8f, 0.6f);
                _editOutline.effectDistance = new Vector2(1.5f, 1.5f);
            }

            _editTitleText = UIFactory.CreateText(_editOverlay.transform, "Title", $"[拖拽] {_ownerWidget.DisplayName}", 11, TextAnchor.MiddleCenter, Color.white);
            RectTransform trt = _editTitleText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(220f, 22f);
            trt.anchoredPosition = new Vector2(0f, 4f);
        }

        private void Update()
        {
            UpdateEditVisuals();

            if (!IsEditModeActive) return;

            // 快捷键响应 (仅当鼠标悬停或该组件已被多选时)
            bool isSelected = WidgetSelectionManager.IsSelected(_ownerWidget);
            if (_isHovered || isSelected)
            {
                HandleShortcuts(isSelected);
            }
        }

        private void HandleShortcuts(bool isSelected)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");

            // 1. Ctrl + 滚轮 或 [ / ] 键: 平滑无级缩放
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrl && !shift && Math.Abs(scroll) > 0.001f)
            {
                float deltaScale = scroll > 0f ? 0.05f : -0.05f;
                if (isSelected) WidgetSelectionManager.BatchScale(deltaScale);
                else _ownerWidget.UpdateTransform(scale: (_ownerWidget.Config?.Scale ?? 1f) + deltaScale);
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateSelectionAppearance();
            }

            // 2. Shift + 滚轮 或 < / > 键: 平滑旋转 (按住 Ctrl 加速为 15° 吸附)
            if (shift && Math.Abs(scroll) > 0.001f)
            {
                float step = ctrl ? 15f : 5f;
                float deltaAngle = scroll > 0f ? step : -step;
                if (isSelected) WidgetSelectionManager.BatchRotate(deltaAngle);
                else _ownerWidget.UpdateTransform(rotation: (_ownerWidget.Config?.Rotation ?? 0f) + deltaAngle);
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateSelectionAppearance();
            }

            // 3. 键盘按键快捷控制
            if (isSelected)
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    WidgetSelectionManager.ResetRotation();
                    UpdateSelectionAppearance();
                }
                else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
                {
                    WidgetSelectionManager.ResetScale();
                    UpdateSelectionAppearance();
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
        }

        private void UpdateEditVisuals()
        {
            if (_editOverlay != null)
            {
                if (_editOverlay.activeSelf != IsEditModeActive)
                {
                    _editOverlay.SetActive(IsEditModeActive);
                    UpdateSelectionAppearance();
                }
            }
        }

        public void UpdateSelectionAppearance()
        {
            if (_ownerWidget == null || _editOverlay == null || !IsEditModeActive) return;

            bool isSelected = WidgetSelectionManager.IsSelected(_ownerWidget);
            float scale = _ownerWidget.Config?.Scale ?? 1.0f;
            float rot = _ownerWidget.Config?.Rotation ?? 0f;

            if (isSelected)
            {
                // 选中高亮: 金黄霓虹光环
                Color goldColor = new Color(1f, 0.85f, 0.15f, 1f);
                if (_editOutline != null)
                {
                    _editOutline.effectColor = goldColor;
                    _editOutline.effectDistance = new Vector2(3f, 3f);
                }
                if (_ringImg != null)
                {
                    _ringImg.color = goldColor;
                }
                if (_overlayImage != null)
                {
                    _overlayImage.color = new Color(1f, 0.85f, 0.15f, 0.15f);
                }
                if (_editTitleText != null)
                {
                    _editTitleText.text = $"<b>★ [已选中] {_ownerWidget.DisplayName} ({scale:F2}x, {rot:F0}°)</b>";
                    _editTitleText.color = goldColor;
                }
            }
            else
            {
                // 普通未选中编辑态: 柔和青蓝
                Color cyanColor = new Color(0f, 0.85f, 1f, 0.6f);
                if (_editOutline != null)
                {
                    _editOutline.effectColor = cyanColor;
                    _editOutline.effectDistance = new Vector2(1.5f, 1.5f);
                }
                if (_ringImg != null)
                {
                    _ringImg.color = cyanColor;
                }
                if (_overlayImage != null)
                {
                    _overlayImage.color = new Color(0f, 0.85f, 1f, 0.06f);
                }
                if (_editTitleText != null)
                {
                    _editTitleText.text = $"[可拖拽] {_ownerWidget.DisplayName}";
                    _editTitleText.color = Color.white;
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar) return;

            // 双击快捷循环切换缩放 (1.0x -> 1.2x -> 1.5x -> 0.8x -> 1.0x)
            if (eventData.clickCount == 2)
            {
                float currentScale = _ownerWidget.Config?.Scale ?? 1.0f;
                float nextScale = currentScale < 1.15f ? 1.2f : (currentScale < 1.45f ? 1.5f : (currentScale < 1.75f ? 0.8f : 1.0f));
                _ownerWidget.UpdateTransform(scale: nextScale);
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateSelectionAppearance();
                return;
            }

            bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                              Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (isAdditive)
            {
                WidgetSelectionManager.ToggleSelect(_ownerWidget);
            }
            else
            {
                if (!WidgetSelectionManager.IsSelected(_ownerWidget))
                {
                    WidgetSelectionManager.Select(_ownerWidget);
                }
            }

            transform.SetAsLastSibling();
            UpdateSelectionAppearance();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar || _rectTransform == null || _canvas == null) return;

            Vector2 delta = eventData.delta / _canvas.scaleFactor;

            bool isMulti = WidgetSelectionManager.IsSelected(_ownerWidget) && WidgetSelectionManager.Count > 1;

            if (isMulti)
            {
                // 多选批量拖拽
                WidgetSelectionManager.BatchMove(delta);

                // 磁吸吸附校准 (以当前拖拽的 leader 组件为主导校准 group)
                if (EnableMagneticSnap)
                {
                    Vector2 currentPos = _rectTransform.anchoredPosition;
                    Vector2 snappedPos = ApplyMagneticOrGridSnap(currentPos);
                    Vector2 snapDelta = snappedPos - currentPos;
                    if (snapDelta.sqrMagnitude > 0.001f)
                    {
                        WidgetSelectionManager.BatchMove(snapDelta);
                    }
                }
            }
            else
            {
                // 单个组件拖拽
                _rectTransform.anchoredPosition += delta;

                if (EnableMagneticSnap)
                {
                    _rectTransform.anchoredPosition = ApplyMagneticOrGridSnap(_rectTransform.anchoredPosition);
                }
                else
                {
                    // 基础 5px 网格吸附
                    float snapX = Mathf.Round(_rectTransform.anchoredPosition.x / 5f) * 5f;
                    float snapY = Mathf.Round(_rectTransform.anchoredPosition.y / 5f) * 5f;
                    _rectTransform.anchoredPosition = new Vector2(snapX, snapY);
                }
            }
        }

        private Vector2 ApplyMagneticOrGridSnap(Vector2 proposedPos)
        {
            float snapX = Mathf.Round(proposedPos.x / 5f) * 5f;
            float snapY = Mathf.Round(proposedPos.y / 5f) * 5f;

            if (NavballHUD.Instance == null || NavballHUD.Instance.ModularWidgets == null)
            {
                return new Vector2(snapX, snapY);
            }

            float threshold = 8f;
            Rect myBounds = WidgetSelectionManager.GetWidgetBounds(_ownerWidget);
            float halfW = myBounds.width * 0.5f;
            float halfH = myBounds.height * 0.5f;

            float myLeft = proposedPos.x - halfW;
            float myRight = proposedPos.x + halfW;
            float myBottom = proposedPos.y - halfH;
            float myTop = proposedPos.y + halfH;

            // 优先检查与屏幕水平中轴 X=0 对齐
            if (Mathf.Abs(proposedPos.x) < threshold)
            {
                snapX = 0f;
            }

            // 遍历其他未选中的组件进行边缘与中心磁吸
            foreach (var other in NavballHUD.Instance.ModularWidgets)
            {
                if (other == null || other == _ownerWidget || !other.gameObject.activeInHierarchy) continue;
                if (WidgetSelectionManager.IsSelected(other)) continue; // 排除同组选中的组件

                Rect ob = WidgetSelectionManager.GetWidgetBounds(other);

                // 水平吸附 (左-左, 右-右, 中-中, 左-右, 右-左)
                if (Mathf.Abs(myLeft - ob.xMin) < threshold) snapX = ob.xMin + halfW;
                else if (Mathf.Abs(myRight - ob.xMax) < threshold) snapX = ob.xMax - halfW;
                else if (Mathf.Abs(proposedPos.x - ob.center.x) < threshold) snapX = ob.center.x;
                else if (Mathf.Abs(myLeft - ob.xMax) < threshold) snapX = ob.xMax + halfW;
                else if (Mathf.Abs(myRight - ob.xMin) < threshold) snapX = ob.xMin - halfW;

                // 垂直吸附 (顶-顶, 底-底, 中-中, 顶-底, 底-顶)
                if (Mathf.Abs(myTop - ob.yMax) < threshold) snapY = ob.yMax - halfH;
                else if (Mathf.Abs(myBottom - ob.yMin) < threshold) snapY = ob.yMin + halfH;
                else if (Mathf.Abs(proposedPos.y - ob.center.y) < threshold) snapY = ob.center.y;
                else if (Mathf.Abs(myBottom - ob.yMax) < threshold) snapY = ob.yMax + halfH;
                else if (Mathf.Abs(myTop - ob.yMin) < threshold) snapY = ob.yMin - halfH;
            }

            return new Vector2(snapX, snapY);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive) return;

            // 批量保存最新坐标到配置对象
            if (WidgetSelectionManager.IsSelected(_ownerWidget) && WidgetSelectionManager.Count > 1)
            {
                foreach (var w in WidgetSelectionManager.SelectedWidgets)
                {
                    if (w != null && w.Config != null && w.RectTransform != null)
                    {
                        w.Config.PositionX = w.RectTransform.anchoredPosition.x;
                        w.Config.PositionY = w.RectTransform.anchoredPosition.y;
                    }
                }
            }
            else
            {
                if (_ownerWidget?.Config != null && _rectTransform != null)
                {
                    _ownerWidget.Config.PositionX = _rectTransform.anchoredPosition.x;
                    _ownerWidget.Config.PositionY = _rectTransform.anchoredPosition.y;
                }
            }

            WidgetLayoutManager.Instance.SaveLayout();
            UpdateSelectionAppearance();
        }
    }
}
