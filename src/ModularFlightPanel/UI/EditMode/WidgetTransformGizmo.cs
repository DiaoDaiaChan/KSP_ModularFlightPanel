using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

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

        // 4 角手柄
        private RectTransform _handleTL;
        private RectTransform _handleTR;
        private RectTransform _handleBL;
        private RectTransform _handleBR;

        // 4 边手柄 (用于独立调整长宽比/细长与矮胖)
        private RectTransform _handleT;
        private RectTransform _handleB;
        private RectTransform _handleL;
        private RectTransform _handleR;

        // 旋转手柄
        private GameObject _rotStemObj;
        private RectTransform _rotStemRt;
        private RectTransform _rotHandle;

        // 信息牌
        private GameObject _infoBadgeObj;
        private RectTransform _infoBadgeRt;
        private Text _infoText;

        // 拖拽手柄交互状态
        public enum DragGizmoMode
        {
            None,
            ScaleTL, ScaleTR, ScaleBL, ScaleBR,
            ScaleT, ScaleB, ScaleL, ScaleR,
            Rotate
        }
        private DragGizmoMode _currentDragMode = DragGizmoMode.None;
        private Vector2 _dragStartMousePos;
        private float _initialScale;
        private float _initialScaleX = 1f;
        private float _initialScaleY = 1f;
        private float _initialAngle;
        private Rect _initialGroupBounds;
        private Vector2 _initialCenter;

        private readonly Dictionary<BaseFlightWidget, float> _initialWidgetScales = new Dictionary<BaseFlightWidget, float>();
        private readonly Dictionary<BaseFlightWidget, Vector2> _initialWidgetScalesXY = new Dictionary<BaseFlightWidget, Vector2>();

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

            // 1. 创建 4 角缩放手柄
            _handleTL = CreateHandle("Handle_TL", new Vector2(0f, 1f), DragGizmoMode.ScaleTL);
            _handleTR = CreateHandle("Handle_TR", new Vector2(1f, 1f), DragGizmoMode.ScaleTR);
            _handleBL = CreateHandle("Handle_BL", new Vector2(0f, 0f), DragGizmoMode.ScaleBL);
            _handleBR = CreateHandle("Handle_BR", new Vector2(1f, 0f), DragGizmoMode.ScaleBR);

            // 1b. 创建 4 边中点拉伸手柄 (独立调整细长与矮胖)
            _handleT = CreateHandle("Handle_T", new Vector2(0.5f, 1f), DragGizmoMode.ScaleT, new Vector2(14f, 6f));
            _handleB = CreateHandle("Handle_B", new Vector2(0.5f, 0f), DragGizmoMode.ScaleB, new Vector2(14f, 6f));
            _handleL = CreateHandle("Handle_L", new Vector2(0f, 0.5f), DragGizmoMode.ScaleL, new Vector2(6f, 14f));
            _handleR = CreateHandle("Handle_R", new Vector2(1f, 0.5f), DragGizmoMode.ScaleR, new Vector2(6f, 14f));

            // 2. 创建顶部旋转手柄与延伸立柱
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
            _infoBadgeRt.sizeDelta = new Vector2(250f, 20f);
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

        private RectTransform CreateHandle(string name, Vector2 anchor, DragGizmoMode mode, Vector2 size = default)
        {
            if (size == default) size = new Vector2(10f, 10f);
            GameObject handleObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(GizmoHandleTrigger));
            handleObj.transform.SetParent(_gizmoBox.transform, false);

            RectTransform rt = handleObj.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
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
                if (!Input.GetMouseButton(0))
                {
                    EndGizmoDrag(null);
                }
                else
                {
                    HandleDragProcess();
                }
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
                var target = sel[0];
                bool isLocked = target.Config?.IsLocked == true;
                float rot = target.Config?.Rotation ?? 0f;
                _gizmoBoxRt.localEulerAngles = new Vector3(0f, 0f, rot);
                float sx = target.Config?.EffectiveScaleX ?? 1f;
                float sy = target.Config?.EffectiveScaleY ?? 1f;
                string scaleStr = Math.Abs(sx - sy) < 0.001f ? $"{sx:F2}x" : $"{sx:F2}x,{sy:F2}y";
                int layerNum = WidgetLayerManager.GetLayerNumber(target);
                int totalLayers = WidgetLayerManager.TotalLayers;
                string lockBadge = isLocked ? " <color=#FFB703>🔒 [已锁定]</color>" : "";
                _infoText.text = $"<b>{target.DisplayName}</b>{lockBadge} | <color=#38BDF8>#{layerNum}/{totalLayers}</color> | X:{center.x:F0} Y:{center.y:F0} | {scaleStr} {rot:F0}°";

                if (_boxOutline != null)
                {
                    _boxOutline.effectColor = isLocked ? new Color(1f, 0.7f, 0.2f, 0.75f) : _cyanCol;
                }
                bool allowEdge = !isLocked;
                if (target is ModularFlightPanel.UI.Framework.IAdaptiveSizeWidget adaptive)
                {
                    allowEdge = !isLocked && adaptive.AllowNonUniformScale;
                }
                else if (target.GetType().Name.Contains("Sphere") || target.GetType().Name.Contains("Navball"))
                {
                    allowEdge = false;
                }
                SetHandlesActive(!isLocked, allowEdge);
            }
            else
            {
                _gizmoBoxRt.localEulerAngles = Vector3.zero;
                _infoText.text = $"<b>{I18n.TrFormat("GIZMO_GROUP_COUNT_FMT", "多选群组 ({0} 项)", sel.Count)}</b> | X:{center.x:F0} Y:{center.y:F0} | W:{w_total:F0} H:{h_total:F0}";
                if (_boxOutline != null) _boxOutline.effectColor = _cyanCol;
                SetHandlesActive(true, true);
            }
        }

        private void SetHandlesActive(bool active, bool allowEdgeHandles = true)
        {
            if (_handleTL != null) _handleTL.gameObject.SetActive(active);
            if (_handleTR != null) _handleTR.gameObject.SetActive(active);
            if (_handleBL != null) _handleBL.gameObject.SetActive(active);
            if (_handleBR != null) _handleBR.gameObject.SetActive(active);
            if (_handleT != null) _handleT.gameObject.SetActive(active && allowEdgeHandles);
            if (_handleB != null) _handleB.gameObject.SetActive(active && allowEdgeHandles);
            if (_handleL != null) _handleL.gameObject.SetActive(active && allowEdgeHandles);
            if (_handleR != null) _handleR.gameObject.SetActive(active && allowEdgeHandles);
            if (_rotStemObj != null) _rotStemObj.SetActive(active);
            if (_rotHandle != null) _rotHandle.gameObject.SetActive(active);
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
            _currentDragMode = mode;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, eventData.position, eventData.pressEventCamera, out _dragStartMousePos);

            WidgetEditHistory.BeginAction();

            var sel = WidgetSelectionManager.SelectedWidgets.ToList();
            if (sel.Count > 0)
            {
                _initialScale = sel[0].Config?.Scale ?? 1f;
                _initialScaleX = sel[0].Config?.EffectiveScaleX ?? 1f;
                _initialScaleY = sel[0].Config?.EffectiveScaleY ?? 1f;
                _initialAngle = sel[0].Config?.Rotation ?? 0f;
                _initialWidgetScales.Clear();
                _initialWidgetScalesXY.Clear();
                foreach (var w in sel)
                {
                    if (w != null && w.Config != null)
                    {
                        _initialWidgetScales[w] = w.Config.Scale;
                        _initialWidgetScalesXY[w] = new Vector2(w.Config.EffectiveScaleX, w.Config.EffectiveScaleY);
                    }
                }

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
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, Input.mousePosition, cam, out Vector2 curMousePos);

            Vector2 mouseDelta = curMousePos - _dragStartMousePos;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (_currentDragMode == DragGizmoMode.Rotate)
            {
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
            else if (_currentDragMode == DragGizmoMode.ScaleT || _currentDragMode == DragGizmoMode.ScaleB)
            {
                // 垂直方向拉伸/压缩 (高度、矮胖与细长调节)
                float angleForProj = (WidgetSelectionManager.Count == 1) ? _initialAngle : 0f;
                Vector2 localDelta = Quaternion.Euler(0, 0, -angleForProj) * mouseDelta;
                float distY = (_currentDragMode == DragGizmoMode.ScaleT) ? localDelta.y : -localDelta.y;
                float baseH = Mathf.Max(20f, _initialGroupBounds.height);
                float factorY = Mathf.Max(0.1f, 1.0f + (distY / (baseH * 0.5f)));

                if (shift)
                {
                    factorY = Mathf.Round(factorY * 20f) / 20f;
                }

                if (_initialWidgetScalesXY.Count > 1)
                {
                    WidgetSelectionManager.BatchScaleRelativeXY(_initialWidgetScalesXY, null, factorY, commit: false);
                }
                else
                {
                    float newScaleY = Mathf.Clamp(_initialScaleY * factorY, 0.2f, 4.0f);
                    WidgetSelectionManager.BatchSetScaleXY(null, newScaleY, commit: false);
                }
                UpdateGizmoPosition();
            }
            else if (_currentDragMode == DragGizmoMode.ScaleL || _currentDragMode == DragGizmoMode.ScaleR)
            {
                // 水平方向拉伸/压缩 (宽度调节)
                float angleForProj = (WidgetSelectionManager.Count == 1) ? _initialAngle : 0f;
                Vector2 localDelta = Quaternion.Euler(0, 0, -angleForProj) * mouseDelta;
                float distX = (_currentDragMode == DragGizmoMode.ScaleR) ? localDelta.x : -localDelta.x;
                float baseW = Mathf.Max(20f, _initialGroupBounds.width);
                float factorX = Mathf.Max(0.1f, 1.0f + (distX / (baseW * 0.5f)));

                if (shift)
                {
                    factorX = Mathf.Round(factorX * 20f) / 20f;
                }

                if (_initialWidgetScalesXY.Count > 1)
                {
                    WidgetSelectionManager.BatchScaleRelativeXY(_initialWidgetScalesXY, factorX, null, commit: false);
                }
                else
                {
                    float newScaleX = Mathf.Clamp(_initialScaleX * factorX, 0.2f, 4.0f);
                    WidgetSelectionManager.BatchSetScaleXY(newScaleX, null, commit: false);
                }
                UpdateGizmoPosition();
            }
            else
            {
                // 角手柄等比例缩放 (按对角线比例缩放，彻底消除多选比例坍塌)
                float angleForProj = (WidgetSelectionManager.Count == 1) ? _initialAngle : 0f;
                Vector2 localDelta = Quaternion.Euler(0, 0, -angleForProj) * mouseDelta;
                float baseDiag = Mathf.Max(30f, Mathf.Sqrt(_initialGroupBounds.width * _initialGroupBounds.width + _initialGroupBounds.height * _initialGroupBounds.height));
                float dist = Vector2.Dot(localDelta.normalized, GetHandleDirection(_currentDragMode)) * localDelta.magnitude;

                float scaleFactor = Mathf.Max(0.1f, 1.0f + (dist / (baseDiag * 0.5f)));

                // Shift 键吸附至 0.05x 步进
                if (shift)
                {
                    scaleFactor = Mathf.Round(scaleFactor * 20f) / 20f;
                }

                if (_initialWidgetScales.Count > 1)
                {
                    WidgetSelectionManager.BatchScaleRelative(_initialWidgetScales, scaleFactor, commit: false);
                }
                else
                {
                    float newScale = Mathf.Clamp(_initialScale * scaleFactor, 0.2f, 4.0f);
                    WidgetSelectionManager.BatchSetScale(newScale, commit: false);
                }
                UpdateGizmoPosition();
            }
        }

        private Vector2 GetHandleDirection(DragGizmoMode mode)
        {
            switch (mode)
            {
                case DragGizmoMode.ScaleTR: return new Vector2(1f, 1f).normalized;
                case DragGizmoMode.ScaleTL: return new Vector2(-1f, 1f).normalized;
                case DragGizmoMode.ScaleBR: return new Vector2(1f, -1f).normalized;
                case DragGizmoMode.ScaleBL: return new Vector2(-1f, -1f).normalized;
                default: return Vector2.up;
            }
        }

        public void EndGizmoDrag(PointerEventData eventData)
        {
            if (_currentDragMode != DragGizmoMode.None)
            {
                string desc = _currentDragMode == DragGizmoMode.Rotate ? I18n.Tr("GIZMO_ACTION_ROTATE", "手柄旋转") : I18n.Tr("GIZMO_ACTION_SCALE", "手柄缩放");
                bool wasScale = _currentDragMode != DragGizmoMode.Rotate;
                _currentDragMode = DragGizmoMode.None;
                WidgetEditHistory.CommitAction(desc);
                WidgetLayoutManager.Instance.SaveLayout();

                if (wasScale)
                {
                    FlightHUDManager.Instance?.RespawnWidgets(WidgetSelectionManager.SelectedWidgets);
                }
                UpdateGizmoPosition();
            }
        }

        // ==========================================
        // 内部手柄点击触发器
        // ==========================================
        private class GizmoHandleTrigger : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
        {
            public DragGizmoMode Mode;
            public WidgetTransformGizmo Gizmo;

            public void OnPointerDown(PointerEventData eventData)
            {
                Gizmo?.StartGizmoDrag(Mode, eventData);
            }

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData) { }

            public void OnEndDrag(PointerEventData eventData)
            {
                Gizmo?.EndGizmoDrag(eventData);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Gizmo?.EndGizmoDrag(eventData);
            }
        }
    }
}
