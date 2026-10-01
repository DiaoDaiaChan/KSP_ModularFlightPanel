using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;
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
                    OnEditModeChanged.SafeInvoke(_isEditModeActive, "OnEditModeChanged");
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
        public bool IsHovered => _isHovered;
        private bool _isDragging = false;
        private Vector2 _dragTotalDelta = Vector2.zero;
        private Vector2 _dragStartPos = Vector2.zero;

        private IWidgetControl _draggedControl = null;
        private Vector2 _controlStartOffset = Vector2.zero;
        private Vector2 _controlTotalDelta = Vector2.zero;
        private bool _isDraggingControl = false;

        private IWidgetControl FindSubControlAtScreenPoint(Vector2 screenPoint)
        {
            if (_ownerWidget?.Controls?.All == null) return null;
            var list = _ownerWidget.Controls.All;
            Camera cam = (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : _canvas.worldCamera;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var ctrl = list[i];
                if (ctrl == null || !ctrl.IsVisible) continue;
                var rt = ctrl.RectTransform;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, cam))
                {
                    return ctrl;
                }
            }
            return null;
        }

        private void Update()
        {
            if (!IsEditModeActive) return;
            if (_isHovered && !_isDragging && !_isDraggingControl && HUDEditModeToolbar.IsSubControlCustomizerOpen && WidgetSelectionManager.IsSelected(_ownerWidget))
            {
                var hit = FindSubControlAtScreenPoint(Input.mousePosition);
                if (hit != null)
                {
                    WidgetControlHighlighter.HighlightedControl = hit;
                }
            }
        }

        public void Initialize(BaseFlightWidget owner, Canvas canvas)
        {
            _ownerWidget = owner;
            _canvas = canvas;
            _rectTransform = GetComponent<RectTransform>();

            CreateEditVisuals();
            UpdateEditVisuals();

            WidgetSelectionManager.OnSelectionChanged += UpdateSelectionAppearance;
            OnEditModeChanged += HandleEditModeChanged;
            this.enabled = IsEditModeActive;
        }

        public void Teardown()
        {
            WidgetSelectionManager.OnSelectionChanged -= UpdateSelectionAppearance;
            OnEditModeChanged -= HandleEditModeChanged;
            if (_editOverlay != null)
            {
                if (Application.isPlaying) Destroy(_editOverlay);
                else DestroyImmediate(_editOverlay);
                _editOverlay = null;
            }
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void HandleEditModeChanged(bool isEdit)
        {
            if (this == null) return;
            this.enabled = isEdit;
            UpdateEditVisuals();
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

            _editTitleText = UIFactory.CreateText(_editOverlay.transform, "Title", I18n.TrFormat("DRAG_TITLE_FMT", _ownerWidget.DisplayName), 10, TextAnchor.MiddleCenter, Color.white);
            RectTransform trt = _editTitleText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 1f);
            trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(220f, 20f);
            trt.anchoredPosition = new Vector2(0f, 4f);
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
            if (this == null) return;
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
            if (this == null || _ownerWidget == null || _editOverlay == null || !IsEditModeActive) return;

            bool isSelected = WidgetSelectionManager.IsSelected(_ownerWidget);
            bool isLocked = _ownerWidget.Config?.IsLocked == true;
            float scale = _ownerWidget.Config?.Scale ?? 1.0f;
            float rot = _ownerWidget.Config?.Rotation ?? 0f;

            if (_overlayImage != null)
            {
                // 锁定态且未选中时，关闭射线拦截，允许穿透点击底层仪表
                _overlayImage.raycastTarget = !isLocked || isSelected;
            }

            if (isSelected)
            {
                // 选中高亮: 边框柔和青蓝底衬，主手柄由 WidgetTransformGizmo 接管
                Color goldColor = isLocked ? new Color(1f, 0.65f, 0.15f, 0.95f) : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, null);
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
                    Color overlayCol = goldColor;
                    overlayCol.a = 0.08f;
                    _overlayImage.color = overlayCol;
                }
                if (_editTitleText != null)
                {
                    // 选中态由顶层 TransformGizmo 的浮动徽标统一展示，避免文字重叠冲突
                    _editTitleText.gameObject.SetActive(false);
                }
            }
            else if (isLocked)
            {
                // 锁定未选中态：低调灰金框衬与微弱半透明底衬，标题展示锁定图标
                Color lockCol = new Color(0.7f, 0.6f, 0.4f, 0.45f);
                if (_editOutline != null)
                {
                    _editOutline.effectColor = lockCol;
                    _editOutline.effectDistance = new Vector2(1f, 1f);
                }
                if (_ringImg != null)
                {
                    _ringImg.color = lockCol;
                }
                if (_overlayImage != null)
                {
                    _overlayImage.color = new Color(0.5f, 0.45f, 0.35f, 0.03f);
                }
                if (_editTitleText != null)
                {
                    _editTitleText.gameObject.SetActive(true);
                    _editTitleText.text = $"🔒 {_ownerWidget.DisplayName}";
                    _editTitleText.color = new Color(0.9f, 0.75f, 0.4f, 0.85f);
                }
            }
            else
            {
                // 普通未选中编辑态
                Color cyanColor = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, null);
                cyanColor.a = 0.5f;
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
                    Color overlayCol = cyanColor;
                    overlayCol.a = 0.04f;
                    _overlayImage.color = overlayCol;
                }
                if (_editTitleText != null)
                {
                    _editTitleText.gameObject.SetActive(true);
                    _editTitleText.text = $"{_ownerWidget.DisplayName}";
                    _editTitleText.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, null);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (FlightHUDManager.IsMouseOverFloatingToolbar) return;

            // 穿透拦截：如果鼠标光标位于打开的 SettingsGUI 工作台窗口内，拦截画布组件的点击与拖拽
#if !HEADLESS && !UNITY_EDITOR
            if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && !SettingsGUI.Instance.IsCanvasLayoutMode)
            {
                Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (SettingsGUI.Instance.WindowRect.Contains(guiMouse)) return;
            }
#endif

            // 右键唤起通用航电上下文菜单 (锁定/复位/紧凑模式/单位制式/遥测检视)
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                WidgetActionRouter.HandlePointerDown(_ownerWidget, eventData);
                return;
            }

            if (!IsEditModeActive) return;

            // 优先拦截：处于微控件精细定制态且命中下属微控件，或当前选中小组件为自由搭建画板 CustomCompositePanelWidget
            bool isComposite = _ownerWidget is Widgets.Gauges.CustomCompositePanelWidget;
            if ((HUDEditModeToolbar.IsSubControlCustomizerOpen || isComposite) && _ownerWidget != null && _ownerWidget.Controls != null && WidgetSelectionManager.IsSelected(_ownerWidget))
            {
                var hitControl = FindSubControlAtScreenPoint(eventData.position);
                if (hitControl != null)
                {
                    _draggedControl = hitControl;
                    _isDraggingControl = true;
                    _controlStartOffset = hitControl.CurrentOffset;
                    _controlTotalDelta = Vector2.zero;
                    WidgetControlHighlighter.HighlightedControl = hitControl;
                    _isDragging = false;
                    WidgetEditHistory.BeginAction();
                    return;
                }
            }
            _isDraggingControl = false;
            _draggedControl = null;

            // 锁定图层拦截：禁止画布直接拖拽，引导用户按 L 或在图层面板解锁
            if (_ownerWidget?.Config != null && _ownerWidget.Config.IsLocked)
            {
                MFPToastBridge.Show(I18n.TrFormat("TOAST_LOCKED_HINT_FMT", _ownerWidget.DisplayName));
                return;
            }

            // 双击快速唤起装配台聚焦检视 (像 Figma 双击图层一样丝滑)
            if (eventData.clickCount == 2)
            {
                UIWidget.OnRequestOpenWorkbench?.Invoke();
                MFPToastBridge.Show(I18n.TrFormat("DRAG_TOAST_INSPECT_FMT", _ownerWidget.DisplayName));
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
            _dragStartPos = _rectTransform != null ? _rectTransform.anchoredPosition : Vector2.zero;

            WidgetEditHistory.BeginAction();
            UpdateSelectionAppearance();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive || FlightHUDManager.IsMouseOverFloatingToolbar || _rectTransform == null || _canvas == null) return;
#if !HEADLESS && !UNITY_EDITOR
            if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && !SettingsGUI.Instance.IsCanvasLayoutMode)
            {
                Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (SettingsGUI.Instance.WindowRect.Contains(guiMouse)) return;
            }
#endif
            if (_ownerWidget?.Config != null && _ownerWidget.Config.IsLocked) return;

            // 1. 微控件直接平移与磁吸拖拽
            if (_isDraggingControl && _draggedControl != null)
            {
                Vector2 subDelta = eventData.delta / _canvas.scaleFactor;
                _controlTotalDelta += subDelta;

                Vector2 rawOffset = _controlStartOffset + _controlTotalDelta;

                // Shift 轴向锁定 (纯水平或纯垂直)
                bool shiftKey = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shiftKey)
                {
                    if (Mathf.Abs(_controlTotalDelta.x) > Mathf.Abs(_controlTotalDelta.y))
                    {
                        rawOffset.y = _controlStartOffset.y;
                    }
                    else
                    {
                        rawOffset.x = _controlStartOffset.x;
                    }
                }

                // 原点磁吸贴合 (Snap to Origin, 6px 阈值)
                if (Mathf.Abs(rawOffset.x) < 6f) rawOffset.x = 0f;
                if (Mathf.Abs(rawOffset.y) < 6f) rawOffset.y = 0f;

                // 基础 5px 网格吸附 (如果启用了磁吸)
                if (EnableMagneticSnap)
                {
                    rawOffset.x = Mathf.Round(rawOffset.x / 5f) * 5f;
                    rawOffset.y = Mathf.Round(rawOffset.y / 5f) * 5f;
                }

                _draggedControl.ApplyOffset(rawOffset);
                _ownerWidget.Config?.SetSubElementOffset(_draggedControl.Id, rawOffset);
                if (_ownerWidget is Widgets.Gauges.CustomCompositePanelWidget composite)
                {
                    composite.UpdateLayerPositionFromOffset(_draggedControl.Id, rawOffset);
                }
                WidgetControlHighlighter.HighlightedControl = _draggedControl;
                return;
            }

            _isDragging = true;
            Vector2 delta = eventData.delta / _canvas.scaleFactor;
            _dragTotalDelta += delta;

            // Shift 轴向锁定 (Axis-Lock): 约束为纯水平或纯垂直平移
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Vector2 effectiveDelta = _dragTotalDelta;
            if (shift)
            {
                if (Mathf.Abs(_dragTotalDelta.x) > Mathf.Abs(_dragTotalDelta.y))
                {
                    effectiveDelta.y = 0f;
                }
                else
                {
                    effectiveDelta.x = 0f;
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
                // 单个组件拖拽：基于起始基准位置 + 累计无漂移位移量进行计算，彻底杜绝磁吸振荡与闪烁
                Vector2 rawPos = _dragStartPos + effectiveDelta;

                if (EnableMagneticSnap && WidgetSmartGuides.Instance != null)
                {
                    var snap = WidgetSmartGuides.Instance.EvaluateAndShowGuides(_ownerWidget, rawPos, 8f);
                    _rectTransform.anchoredPosition = snap.SnappedPosition;
                }
                else
                {
                    // 基础 5px 网格吸附
                    float snapX = Mathf.Round(rawPos.x / 5f) * 5f;
                    float snapY = Mathf.Round(rawPos.y / 5f) * 5f;
                    _rectTransform.anchoredPosition = new Vector2(snapX, snapY);
                }
            }

            WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsEditModeActive || (_ownerWidget?.Config != null && _ownerWidget.Config.IsLocked)) return;

            if (_isDraggingControl)
            {
                _isDraggingControl = false;
                if (_ownerWidget is Widgets.Gauges.CustomCompositePanelWidget composite)
                {
                    composite.CommitLayerOffsetsToConfig();
                }
                _draggedControl = null;
                WidgetLayoutManager.Instance.SaveLayout();
                WidgetEditHistory.CommitAction(I18n.TrFormat("DRAG_HIST_MOVE_FMT", _ownerWidget?.DisplayName ?? ""));
                return;
            }

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
                WidgetEditHistory.CommitAction(I18n.TrFormat("DRAG_HIST_MOVE_FMT", _ownerWidget.DisplayName));
                _isDragging = false;
            }

            WidgetLayoutManager.Instance.SaveLayout();
            WidgetTransformGizmo.Instance?.UpdateGizmoPosition();
            UpdateSelectionAppearance();
        }
    }
}
