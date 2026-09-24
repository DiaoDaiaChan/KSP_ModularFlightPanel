using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 专业图形软件级 8 点包围盒变换手柄与旋转操纵器 (Figma/Photoshop-style Transform Gizmo)
    /// 包含：
    /// 1. 4 角缩放手柄 (TopLeft, TopRight, BottomLeft, BottomRight)
    /// 2. 顶部延伸旋转把手 (Top Rotation Pin / Lever)
    /// 3. 实时尺寸与坐标浮动信息牌 (Floating Inspector Badge: X, Y, W, H, Scale, Rot)
    /// 4. 自动适配单选组件与多选群组包围盒
    /// </summary>
    public class WidgetTransformGizmo : MonoBehaviour
    {
        private static WidgetTransformGizmo _instance;
        public static WidgetTransformGizmo Instance => _instance;

        private RectTransform _rootRt;
        private Canvas _canvas;

        private GameObject _gizmoBox;
        private RectTransform _gizmoBoxRt;
        private Outline _boxOutline;
        private Image _boxImage;

        // 旋转手柄与立柱
        private GameObject _rotStemObj;
        private RectTransform _rotStemRt;
        private RectTransform _rotHandle;

        // 信息牌
        private GameObject _infoBadgeObj;
        private RectTransform _infoBadgeRt;
        private Text _infoText;

        // 拖拽手柄交互状态 (彻底杜绝拖动缩放手柄改变大小，仅保留精准旋转)
        public enum DragGizmoMode
        {
            None,
            Rotate
        }
        private DragGizmoMode _currentDragMode = DragGizmoMode.None;
        private Vector2 _dragStartMousePos;
        private float _initialAngle;
        private Rect _initialGroupBounds;
        private Vector2 _initialCenter;

        private Color _cyanCol;
        private Color _goldCol;
        private Color _handleBg;

        public void Initialize(RectTransform parentRt, Canvas canvas)
        {
            _instance = this;
            _rootRt = parentRt;
            _canvas = canvas;

            RectTransform myRt = GetComponent<RectTransform>();
            if (myRt != null)
            {
                myRt.anchorMin = new Vector2(0.5f, 0.5f);
                myRt.anchorMax = new Vector2(0.5f, 0.5f);
                myRt.pivot = new Vector2(0.5f, 0.5f);
                myRt.anchoredPosition = Vector2.zero;
                myRt.sizeDelta = Vector2.zero;
            }

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            _cyanCol = theme.AccentSecondary;
            _goldCol = theme.WarningColor;
            _handleBg = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);

            CreateGizmoVisuals();
            HideGizmo();

            WidgetSelectionManager.OnSelectionChanged += UpdateGizmoPosition;
            WidgetDragHandler.OnEditModeChanged += HandleEditModeChanged;
        }

        private void HandleEditModeChanged(bool active)
        {
            if (!active) HideGizmo();
            else UpdateGizmoPosition();
        }

        private void OnDestroy()
        {
            WidgetSelectionManager.OnSelectionChanged -= UpdateGizmoPosition;
            WidgetDragHandler.OnEditModeChanged -= HandleEditModeChanged;
            if (_gizmoBox != null)
            {
                if (Application.isPlaying) Destroy(_gizmoBox);
                else DestroyImmediate(_gizmoBox);
            }
            if (_instance == this) _instance = null;
        }

        private void CreateGizmoVisuals()
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            _gizmoBox = new GameObject("TransformGizmoBox", typeof(RectTransform), typeof(Image), typeof(Outline));
            _gizmoBox.transform.SetParent(transform, false);
            _gizmoBoxRt = _gizmoBox.GetComponent<RectTransform>();
            _gizmoBoxRt.anchorMin = new Vector2(0.5f, 0.5f);
            _gizmoBoxRt.anchorMax = new Vector2(0.5f, 0.5f);
            _gizmoBoxRt.pivot = new Vector2(0.5f, 0.5f);

            _boxImage = _gizmoBox.GetComponent<Image>();
            _boxImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.03f);
            _boxImage.raycastTarget = false;

            _boxOutline = _gizmoBox.GetComponent<Outline>();
            _boxOutline.effectColor = _cyanCol;
            _boxOutline.effectDistance = new Vector2(1.5f, 1.5f);

            // 1. 创建顶部旋转手柄与延伸立柱 (仅保留姿态微调手柄，彻底禁用改变大小的手柄)
            _rotStemObj = new GameObject("RotStem", typeof(RectTransform), typeof(Image));
            _rotStemObj.transform.SetParent(_gizmoBox.transform, false);
            _rotStemRt = _rotStemObj.GetComponent<RectTransform>();
            _rotStemRt.anchorMin = new Vector2(0.5f, 1f);
            _rotStemRt.anchorMax = new Vector2(0.5f, 1f);
            _rotStemRt.pivot = new Vector2(0.5f, 0f);
            _rotStemRt.sizeDelta = new Vector2(1.5f, 22f);
            _rotStemRt.anchoredPosition = Vector2.zero;
            _rotStemObj.GetComponent<Image>().color = _cyanCol;
            _rotStemObj.GetComponent<Image>().raycastTarget = false;

            _rotHandle = CreateHandle("Handle_Rot", new Vector2(0.5f, 1f), DragGizmoMode.Rotate);
            _rotHandle.anchoredPosition = new Vector2(0f, 22f);

            // 3. 悬浮信息牌 (显示实时坐标与尺寸)
            _infoBadgeObj = new GameObject("GizmoInfoBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
            _infoBadgeObj.transform.SetParent(_gizmoBox.transform, false);
            _infoBadgeRt = _infoBadgeObj.GetComponent<RectTransform>();
            _infoBadgeRt.anchorMin = new Vector2(0.5f, 0f);
            _infoBadgeRt.anchorMax = new Vector2(0.5f, 0f);
            _infoBadgeRt.pivot = new Vector2(0.5f, 1f);
            _infoBadgeRt.sizeDelta = new Vector2(230f, 20f);
            _infoBadgeRt.anchoredPosition = new Vector2(0f, -8f);

            Image bImg = _infoBadgeObj.GetComponent<Image>();
            bImg.color = _handleBg;
            bImg.raycastTarget = false;

            Outline bOut = _infoBadgeObj.GetComponent<Outline>();
            bOut.effectColor = _cyanCol;
            bOut.effectDistance = new Vector2(1f, 1f);

            _infoText = UIFactory.CreateText(_infoBadgeObj.transform, "InfoText", "X: 0 Y: 0 | 1.00x", 9, TextAnchor.MiddleCenter, WidgetStyleManager.NeutralOpaque);
            RectTransform iTrt = _infoText.GetComponent<RectTransform>();
            iTrt.anchorMin = Vector2.zero;
            iTrt.anchorMax = Vector2.one;
            iTrt.sizeDelta = Vector2.zero;
            iTrt.anchoredPosition = Vector2.zero;
        }

        private RectTransform CreateHandle(string name, Vector2 anchor, DragGizmoMode mode)
        {
            GameObject handleObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(GizmoHandleTrigger));
            handleObj.transform.SetParent(_gizmoBox.transform, false);

            RectTransform rt = handleObj.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(10f, 10f);
            rt.anchoredPosition = Vector2.zero;

            Image img = handleObj.GetComponent<Image>();
            img.color = mode == DragGizmoMode.Rotate ? _goldCol : WidgetStyleManager.NeutralOpaque;
            img.raycastTarget = true;

            Outline outline = handleObj.GetComponent<Outline>();
            outline.effectColor = _cyanCol;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var trigger = handleObj.GetComponent<GizmoHandleTrigger>();
            trigger.Mode = mode;
            trigger.Gizmo = this;

            return rt;
        }

        private void Update()
        {
            if (!WidgetDragHandler.IsEditModeActive)
            {
                if (_gizmoBox != null && _gizmoBox.activeSelf) HideGizmo();
                return;
            }

            if (_currentDragMode != DragGizmoMode.None)
            {
                HandleDragProcess();
            }
            else
            {
                UpdateGizmoPosition();
            }
        }

        public void UpdateGizmoPosition()
        {
            if (!WidgetDragHandler.IsEditModeActive || WidgetSelectionManager.Count == 0)
            {
                HideGizmo();
                return;
            }

            var sel = WidgetSelectionManager.SelectedWidgets.ToList();
            if (sel.Count == 0)
            {
                HideGizmo();
                return;
            }

            // 计算整体包围盒
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            foreach (var w in sel)
            {
                Rect b = WidgetSelectionManager.GetWidgetBounds(w);
                if (b.xMin < minX) minX = b.xMin;
                if (b.xMax > maxX) maxX = b.xMax;
                if (b.yMin < minY) minY = b.yMin;
                if (b.yMax > maxY) maxY = b.yMax;
            }

            float w_total = maxX - minX;
            float h_total = maxY - minY;
            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            transform.SetAsLastSibling();
            _gizmoBox.SetActive(true);
            _gizmoBox.transform.SetAsLastSibling();

            _gizmoBoxRt.anchoredPosition = center;
            _gizmoBoxRt.sizeDelta = new Vector2(w_total + 6f, h_total + 6f);

            // 单选时旋转角度跟随该组件，多选时保持 0°
            if (sel.Count == 1)
            {
                float rot = sel[0].Config?.Rotation ?? 0f;
                _gizmoBoxRt.localEulerAngles = new Vector3(0f, 0f, rot);
                float sc = sel[0].Config?.Scale ?? 1f;
                _infoText.text = $"<b>{sel[0].DisplayName}</b> | X:{center.x:F0} Y:{center.y:F0} | {sc:F2}x {rot:F0}°";
            }
            else
            {
                _gizmoBoxRt.localEulerAngles = Vector3.zero;
                _infoText.text = $"<b>多选群组 ({sel.Count} 项)</b> | X:{center.x:F0} Y:{center.y:F0} | W:{w_total:F0} H:{h_total:F0}";
            }
        }

        public void HideGizmo()
        {
            if (_gizmoBox != null) _gizmoBox.SetActive(false);
        }

        // ==========================================
        // 手柄交互事件响应
        // ==========================================
        public void StartGizmoDrag(DragGizmoMode mode, PointerEventData eventData)
        {
            if (mode != DragGizmoMode.Rotate) return;
            _currentDragMode = mode;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, eventData.position, eventData.pressEventCamera, out _dragStartMousePos);

            WidgetEditHistory.BeginAction();

            var sel = WidgetSelectionManager.SelectedWidgets.ToList();
            if (sel.Count > 0)
            {
                _initialAngle = sel[0].Config?.Rotation ?? 0f;

                float minX = sel.Min(w => WidgetSelectionManager.GetWidgetBounds(w).xMin);
                float maxX = sel.Max(w => WidgetSelectionManager.GetWidgetBounds(w).xMax);
                float minY = sel.Min(w => WidgetSelectionManager.GetWidgetBounds(w).yMin);
                float maxY = sel.Max(w => WidgetSelectionManager.GetWidgetBounds(w).yMax);

                _initialGroupBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                _initialCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            }
        }

        private void HandleDragProcess()
        {
            if (_currentDragMode != DragGizmoMode.Rotate) return;

            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, Input.mousePosition, cam, out Vector2 curMousePos);

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 旋转手柄拖拽解算：根据鼠标相对中心点的向量计算角度
            Vector2 dirInitial = _dragStartMousePos - _initialCenter;
            Vector2 dirCurrent = curMousePos - _initialCenter;

            float angleDelta = Vector2.SignedAngle(dirInitial, dirCurrent);
            float targetAngle = (_initialAngle + angleDelta) % 360f;
            if (targetAngle < 0f) targetAngle += 360f;

            // Shift 键吸附至 15° 步进
            if (shift)
            {
                targetAngle = Mathf.Round(targetAngle / 15f) * 15f;
            }

            WidgetSelectionManager.BatchSetRotation(targetAngle);
            UpdateGizmoPosition();
        }

        public void EndGizmoDrag(PointerEventData eventData)
        {
            if (_currentDragMode != DragGizmoMode.None)
            {
                _currentDragMode = DragGizmoMode.None;
                WidgetEditHistory.CommitAction("手柄旋转");
                WidgetLayoutManager.Instance.SaveLayout();
                UpdateGizmoPosition();
            }
        }

        // ==========================================
        // 内部手柄点击触发器
        // ==========================================
        private class GizmoHandleTrigger : MonoBehaviour, IPointerDownHandler, IEndDragHandler, IDragHandler
        {
            public DragGizmoMode Mode;
            public WidgetTransformGizmo Gizmo;

            public void OnPointerDown(PointerEventData eventData)
            {
                Gizmo?.StartGizmoDrag(Mode, eventData);
            }

            public void OnDrag(PointerEventData eventData) { }

            public void OnEndDrag(PointerEventData eventData)
            {
                Gizmo?.EndGizmoDrag(eventData);
            }
        }
    }
}
