using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 专业图形编辑软件级自由拖拽、多选批量操作与磁吸微调中枢 (Figma/Photoshop-Grade Interaction Engine)
    /// 核心交互特性：
    /// 1. 毫秒级智能磁吸导引线 (Smart Guides) 与屏幕对称轴 (X=0) 贴合联动。
    /// 2. 像素级方向键微调 (Arrow Keys Nudge: 1px/5px/10px) 与图层层级控制 ([ / ])。
    /// 3. 全局多级撤销/重做 (Ctrl+Z / Ctrl+Y) 与快捷复制 (Ctrl+D) / 删除 (Delete)。
    /// 4. Shift 轴向锁定 (Axis-Lock) 平移与双击快速唤起检视工作台。
    /// 5. 8 点包围盒几何变换手柄与旋转操纵器联动。
    /// </summary>
    public class WidgetDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private static bool _isEditModeActive = false;
        public static event Action<bool> OnEditModeChanged;
        public static bool IsEditModeActive
        {
            get => _isEditModeActive;
            set
            {
                if (_isEditModeActive != value)
                {
                    _isEditModeActive = value;
                    OnEditModeChanged?.Invoke(_isEditModeActive);
                    if (!_isEditModeActive)
                    {
                        WidgetSmartGuides.Instance?.HideAllGuides();
                        WidgetTransformGizmo.Instance?.HideGizmo();
                    }
                    else
                    {
                        WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
                    }
                }
            }
        }
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
        private bool _isDragging = false;
        private Vector2 _dragTotalDelta = Vector2.zero;

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
            _editOverlay = new GameObject("EditOverlay", typeof(RectTransform), typeof(Image));
            _editOverlay.transform.SetParent(transform, false);

            RectTransform rt = _editOverlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            _overlayImage = _editOverlay.GetComponent<Image>();
            _overlayImage.color = new Color(0f, 0.8f, 1f, 0.04f);
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
                _editOutline.effectColor = new Color(0f, 1f, 0.8f, 0.5f);
                _editOutline.effectDistance = new Vector2(1.2f, 1.2f);
            }

            _editTitleText = UIFactory.CreateText(_editOverlay.transform, "Title", $"[拖拽] {_ownerWidget.DisplayName}", 10, TextAnchor.MiddleCenter, Color.white);
            RectTransform trt = _editTitleText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(220f, 20f);
            trt.anchoredPosition = new Vector2(0f, 4f);
        }

        private void Update()
        {
            UpdateEditVisuals();

            if (!IsEditModeActive) return;

            // 全局快捷键与手柄联动（仅当选中当前组件或鼠标悬停时触发）
            bool isSelected = WidgetSelectionManager.IsSelected(_ownerWidget);
            if (_isHovered || isSelected)
            {
                HandleShortcuts(isSelected);
            }
        }

        private void HandleShortcuts(bool isSelected)
        {
            // 0. 全局 Undo / Redo 快捷键 (Ctrl+Z / Ctrl+Y)
            WidgetEditHistory.HandleHotkeys();

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 1. 全选 (Ctrl + A)
            if (ctrl && Input.GetKeyDown(KeyCode.A))
            {
                if (NavballHUD.Instance != null && NavballHUD.Instance.ModularWidgets != null)
                {
                    WidgetSelectionManager.SelectAll(NavballHUD.Instance.ModularWidgets);
                    MFPToastBridge.Show("已全选所有小组件");
                }
                return;
            }

            // 2. 切换蓝图辅助网格 (G 键)
            if (Input.GetKeyDown(KeyCode.G) && !ctrl)
            {
                WidgetCanvasGrid.ToggleGrid();
                return;
            }

            if (!isSelected) return;

            // 3. 像素级方向键微调 (Arrow Keys Nudge)
            float nudge = shift ? 10f : (ctrl ? 5f : 1f);
            if (Input.GetKeyDown(KeyCode.UpArrow)) WidgetSelectionManager.Nudge(new Vector2(0f, nudge));
            else if (Input.GetKeyDown(KeyCode.DownArrow)) WidgetSelectionManager.Nudge(new Vector2(0f, -nudge));
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) WidgetSelectionManager.Nudge(new Vector2(-nudge, 0f));
            else if (Input.GetKeyDown(KeyCode.RightArrow)) WidgetSelectionManager.Nudge(new Vector2(nudge, 0f));

            // 4. 图层层级移动 ([ 键置底，] 键置顶)
            if (Input.GetKeyDown(KeyCode.RightBracket)) WidgetSelectionManager.BringToFront();
            else if (Input.GetKeyDown(KeyCode.LeftBracket)) WidgetSelectionManager.SendToBack();

            // 5. 快速隐藏/删除选中组件 (Delete / Backspace)
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                WidgetSelectionManager.DeleteSelected();
                return;
            }

            // 6. 快捷复位 (R 复位旋转，0 复位缩放)
            if (Input.GetKeyDown(KeyCode.R) && !ctrl)
            {
                WidgetSelectionManager.ResetRotation();
                UpdateSelectionAppearance();
            }
            else if ((Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) && !ctrl)
            {
                WidgetSelectionManager.ResetScale();
                UpdateSelectionAppearance();
            }

            // 7. 滚轮辅助缩放与旋转
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (ctrl && !shift && Math.Abs(scroll) > 0.001f)
            {
                float deltaScale = scroll > 0f ? 0.05f : -0.05f;
                WidgetEditHistory.BeginAction();
                WidgetSelectionManager.BatchScale(deltaScale);
                WidgetEditHistory.CommitAction("滚轮缩放");
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateSelectionAppearance();
            }
            else if (shift && Math.Abs(scroll) > 0.001f)
            {
                float step = ctrl ? 15f : 5f;
                float deltaAngle = scroll > 0f ? step : -step;
                WidgetEditHistory.BeginAction();
                WidgetSelectionManager.BatchRotate(deltaAngle);
                WidgetEditHistory.CommitAction("滚轮旋转");
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateSelectionAppearance();
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
                // 选中高亮: 边框柔和青蓝底衬，主手柄由 WidgetTransformGizmo 接管
                Color goldColor = new Color(1f, 0.85f, 0.15f, 1f);
                if (_editOutline != null)
                {
                    _editOutline.effectColor = goldColor;
                    _editOutline.effectDistance = new Vector2(2f, 2f);
                }
                if (_ringImg != null)
                {
                    _ringImg.color = goldColor;
                }
                if (_overlayImage != null)
                {
                    _overlayImage.color = new Color(1f, 0.85f, 0.15f, 0.08f);
                }
                if (_editTitleText != null)
                {
                    _editTitleText.text = $"<b>★ {_ownerWidget.DisplayName}</b>";
                    _editTitleText.color = goldColor;
                }
            }
            else
            {
                // 普通未选中编辑态
                Color cyanColor = new Color(0f, 0.85f, 1f, 0.5f);
                if (_editOutline != null)
                {
                    _editOutline.effectColor = cyanColor;
                    _editOutline.effectDistance = new Vector2(1.2f, 1.2f);
                }
                if (_ringImg != null)
                {
                    _ringImg.color = cyanColor;
                }
                if (_overlayImage != null)
                {
                    _overlayImage.color = new Color(0f, 0.85f, 1f, 0.04f);
                }
                if (_editTitleText != null)
                {
                    _editTitleText.text = $"{_ownerWidget.DisplayName}";
                    _editTitleText.color = new Color(0.85f, 0.95f, 1f, 0.8f);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar) return;

            // 双击快速唤起装配台聚焦检视 (像 Figma 双击图层一样丝滑)
            if (eventData.clickCount == 2)
            {
                UIWidget.OnRequestOpenWorkbench?.Invoke();
                MFPToastBridge.Show($"🛠️ 正在装配台检视: {_ownerWidget.DisplayName}");
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

            _isDragging = false;
            _dragTotalDelta = Vector2.zero;

            WidgetEditHistory.BeginAction();
            UpdateSelectionAppearance();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive || NavballHUD.IsMouseOverFloatingToolbar || _rectTransform == null || _canvas == null) return;

            _isDragging = true;
            Vector2 delta = eventData.delta / _canvas.scaleFactor;
            _dragTotalDelta += delta;

            // Shift 轴向锁定 (Axis-Lock): 约束为纯水平或纯垂直平移
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift)
            {
                if (Mathf.Abs(_dragTotalDelta.x) > Mathf.Abs(_dragTotalDelta.y))
                {
                    delta.y = 0f;
                }
                else
                {
                    delta.x = 0f;
                }
            }

            bool isMulti = WidgetSelectionManager.IsSelected(_ownerWidget) && WidgetSelectionManager.Count > 1;

            if (isMulti)
            {
                // 多选批量拖拽
                WidgetSelectionManager.BatchMove(delta);

                // 智能磁吸参考线计算
                if (EnableMagneticSnap && WidgetSmartGuides.Instance != null)
                {
                    Vector2 curPos = _rectTransform.anchoredPosition;
                    var snap = WidgetSmartGuides.Instance.EvaluateAndShowGuides(_ownerWidget, curPos, 8f);
                    Vector2 snapDelta = snap.SnappedPosition - curPos;
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

                if (EnableMagneticSnap && WidgetSmartGuides.Instance != null)
                {
                    var snap = WidgetSmartGuides.Instance.EvaluateAndShowGuides(_ownerWidget, _rectTransform.anchoredPosition, 8f);
                    _rectTransform.anchoredPosition = snap.SnappedPosition;
                }
                else
                {
                    // 基础 5px 网格吸附
                    float snapX = Mathf.Round(_rectTransform.anchoredPosition.x / 5f) * 5f;
                    float snapY = Mathf.Round(_rectTransform.anchoredPosition.y / 5f) * 5f;
                    _rectTransform.anchoredPosition = new Vector2(snapX, snapY);
                }
            }

            WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive) return;

            WidgetSmartGuides.Instance?.HideAllGuides();

            // 批量持久化坐标到配置对象
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

            if (_isDragging)
            {
                WidgetEditHistory.CommitAction($"移动 {_ownerWidget.DisplayName}");
                _isDragging = false;
            }

            WidgetLayoutManager.Instance.SaveLayout();
            WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
            UpdateSelectionAppearance();
        }
    }
}
