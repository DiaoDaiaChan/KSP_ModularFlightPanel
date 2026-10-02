using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    #region WidgetCanvasGrid
    /// <summary>
    /// 全屏航电设计蓝图辅助网格与对称轴中枢 (Aerospace Canvas Blueprint Grid & Symmetry Crosshairs)
    /// 在编辑模式下呈现专业 Figma/CAD 级细微网格与机体对称中轴，辅助完美居中对称与间距排布。
    /// </summary>
    public class WidgetCanvasGrid : MonoBehaviour
    {
        private static WidgetCanvasGrid _instance;
        public static WidgetCanvasGrid Instance => _instance;

        public static bool IsGridVisible { get; set; } = true;
        public static event Action<bool> OnGridVisibilityChanged;

        private GameObject _gridRoot;
        private RectTransform _rootRt;
        private Canvas _canvas;

        private Image _axisX;
        private Image _axisY;
        private Text _axisXBadge;

        private readonly List<Image> _gridLines = new List<Image>();

        private Color _axisCol;
        private Color _subAxisCol;
        private Color _gridCol;

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
            _axisCol = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.55f);
            _subAxisCol = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.25f);
            _gridCol = WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.08f);

            _gridRoot = new GameObject("CanvasBlueprintGridRoot", typeof(RectTransform));
            _gridRoot.transform.SetParent(transform, false);
            _gridRoot.transform.SetAsFirstSibling(); // 置于最底层

            RectTransform rt = _gridRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // 1. 创建 X = 0 机体垂直对称轴
            _axisX = CreateLine(_gridRoot.transform, "Axis_X_Centerline", true, _axisCol, 1.5f);
            RectTransform xRt = _axisX.rectTransform;
            xRt.anchoredPosition = new Vector2(0f, 0f);

            // 顶部机体中心指示徽标 (X = 0 CENTERLINE)
            GameObject badgeObj = new GameObject("CenterlineBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
            badgeObj.transform.SetParent(_gridRoot.transform, false);
            RectTransform bRt = badgeObj.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.5f, 0.5f);
            bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.sizeDelta = new Vector2(86f, 16f);
            bRt.anchoredPosition = new Vector2(0f, 320f);

            Image bImg = badgeObj.GetComponent<Image>();
            bImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            bImg.raycastTarget = false;

            Outline bOut = badgeObj.GetComponent<Outline>();
            bOut.effectColor = _axisCol;
            bOut.effectDistance = new Vector2(1f, 1f);

            _axisXBadge = UIFactory.CreateText(badgeObj.transform, "Text", I18n.Tr("GRID_AXIS_X0", "⌖ X = 0 对称轴"), 8, TextAnchor.MiddleCenter, _axisCol);
            RectTransform btRt = _axisXBadge.GetComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.sizeDelta = Vector2.zero;
            btRt.anchoredPosition = Vector2.zero;

            // 2. 创建 Y = 0 地平基准线
            _axisY = CreateLine(_gridRoot.transform, "Axis_Y_Horizon", false, _subAxisCol, 1.2f);
            RectTransform yRt = _axisY.rectTransform;
            yRt.anchoredPosition = new Vector2(0f, 0f);

            // 3. 创建 100px 疏网格与 50px 辅助网格
            BuildGridMesh();

            UpdateVisibility();

            WidgetDragHandler.OnEditModeChanged += HandleEditModeChanged;
        }

        private void HandleEditModeChanged(bool active)
        {
            if (this == null) return;
            UpdateVisibility();
        }

        private void OnDestroy()
        {
            WidgetDragHandler.OnEditModeChanged -= HandleEditModeChanged;
            if (_instance == this) _instance = null;
        }

        private void BuildGridMesh()
        {
            // 水平向两侧展开网格线 (±100, ±200, ±300, ±400, ±500, ±600, ±700, ±800)
            for (int x = -800; x <= 800; x += 100)
            {
                if (x == 0) continue;
                var line = CreateLine(_gridRoot.transform, $"Grid_V_{x}", true, _gridCol, 1.0f);
                line.rectTransform.anchoredPosition = new Vector2(x, 0f);
                _gridLines.Add(line);
            }

            // 垂直向上下展开网格线 (覆盖地平线下方至屏幕顶端)
            for (int y = -300; y <= 1200; y += 100)
            {
                if (y == 0) continue;
                var line = CreateLine(_gridRoot.transform, $"Grid_H_{y}", false, _gridCol, 1.0f);
                line.rectTransform.anchoredPosition = new Vector2(0f, y);
                _gridLines.Add(line);
            }
        }

        private Image CreateLine(Transform parent, string name, bool isVertical, Color color, float thickness)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            Image img = obj.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            if (isVertical)
            {
                rt.sizeDelta = new Vector2(thickness, 4000f);
            }
            else
            {
                rt.sizeDelta = new Vector2(4000f, thickness);
            }

            return img;
        }

        public static void ToggleGrid()
        {
            IsGridVisible = !IsGridVisible;
            _instance?.UpdateVisibility();
            OnGridVisibilityChanged?.Invoke(IsGridVisible);
            MFPToastBridge.Show(IsGridVisible ? I18n.Tr("GRID_BLUEPRINT_ON", "▦ 蓝图辅助网格: [开启]") : I18n.Tr("GRID_BLUEPRINT_OFF", "▦ 蓝图辅助网格: [关闭]"));
        }

        public void UpdateVisibility()
        {
            bool show = WidgetDragHandler.IsEditModeActive && IsGridVisible;
            if (_gridRoot != null)
            {
                _gridRoot.SetActive(show);
            }
        }
    }
    #endregion

    #region MarqueeSelectionHandler
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
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 4000f);
            rt.anchoredPosition = Vector2.zero;

            // 2. 创建视觉框选矩形
            _selectionBoxObj = new GameObject("MarqueeSelectionBox", typeof(RectTransform), typeof(Image), typeof(Outline));
            _selectionBoxObj.transform.SetParent(_parentRt, false);
            _selectionBoxRt = _selectionBoxObj.GetComponent<RectTransform>();
            _selectionBoxRt.anchorMin = new Vector2(0.5f, 0.5f);
            _selectionBoxRt.anchorMax = new Vector2(0.5f, 0.5f);
            _selectionBoxRt.pivot = Vector2.zero;

            _selectionBoxImage = _selectionBoxObj.GetComponent<Image>();
            Color accent = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, null);
            Color fillCol = accent;
            fillCol.a = 0.15f;
            _selectionBoxImage.color = fillCol;
            _selectionBoxImage.raycastTarget = false;

            _selectionBoxOutline = _selectionBoxObj.GetComponent<Outline>();
            Color outlineCol = accent;
            outlineCol.a = 0.9f;
            _selectionBoxOutline.effectColor = outlineCol;
            _selectionBoxOutline.effectDistance = new Vector2(1.5f, 1.5f);

            _selectionBoxObj.SetActive(false);
            WidgetDragHandler.OnEditModeChanged += HandleEditModeChanged;
            UpdateRaycastState();
        }

        private void OnDestroy()
        {
            WidgetDragHandler.OnEditModeChanged -= HandleEditModeChanged;
            if (_selectionBoxObj != null)
            {
                if (Application.isPlaying) Destroy(_selectionBoxObj);
                else DestroyImmediate(_selectionBoxObj);
            }
        }

        private void HandleEditModeChanged(bool isEdit)
        {
            if (this == null) return;
            UpdateRaycastState();
        }

        public void UpdateRaycastState()
        {
            if (this == null) return;
            bool editActive = WidgetDragHandler.IsEditModeActive && (FlightHUDManager.Instance == null || FlightHUDManager.Instance.IsUIVisible);
            if (_bgRaycastCatcher != null && _bgRaycastCatcher.raycastTarget != editActive)
            {
                _bgRaycastCatcher.raycastTarget = editActive;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!WidgetDragHandler.IsEditModeActive || FlightHUDManager.IsMouseOverFloatingToolbar) return;

            // 穿透拦截：如果鼠标光标位于打开的 SettingsGUI 工作台窗口内，拦截框选操作
#if !HEADLESS && !UNITY_EDITOR
            if (SettingsGUI.Instance != null && SettingsGUI.Instance.IsOpen && !SettingsGUI.Instance.IsCanvasLayoutMode)
            {
                Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (SettingsGUI.Instance.WindowRect.Contains(guiMouse)) return;
            }
#endif

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
            if (!_isDraggingBox || !WidgetDragHandler.IsEditModeActive) return;

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

            // 碰撞检测：检查当前哪些小组件相交 (批量处理，阻断每帧事件风暴)
            if (FlightHUDManager.Instance != null && FlightHUDManager.Instance.ModularWidgets != null)
            {
                bool isAdditive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                                  Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                var candidates = new HashSet<BaseFlightWidget>();
                if (isAdditive)
                {
                    foreach (var sel in WidgetSelectionManager.SelectedWidgets)
                    {
                        if (sel != null) candidates.Add(sel);
                    }
                }

                foreach (var w in FlightHUDManager.Instance.ModularWidgets)
                {
                    if (w == null || !w.gameObject.activeInHierarchy) continue;

                    Rect widgetBounds = WidgetSelectionManager.GetWidgetBounds(w);
                    if (marqueeRect.Overlaps(widgetBounds))
                    {
                        candidates.Add(w);
                    }
                }

                WidgetSelectionManager.SetSelection(candidates);
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
    #endregion
}
