using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
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

            _axisXBadge = UIFactory.CreateText(badgeObj.transform, "Text", "⌖ X = 0 对称轴", 8, TextAnchor.MiddleCenter, _axisCol);
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

            // 垂直向上下展开网格线 (±100, ±200, ±300, ±400)
            for (int y = -400; y <= 400; y += 100)
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
            MFPToastBridge.Show(IsGridVisible ? "▦ 蓝图辅助网格: [开启]" : "▦ 蓝图辅助网格: [关闭]");
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
}
