using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 对齐类型枚举
    /// </summary>
    public enum SnapAlignmentType
    {
        None,
        LeftToLeft,
        LeftToRight,
        RightToRight,
        RightToLeft,
        CenterXToCenterX,
        CenterXToScreen,
        TopToTop,
        TopToBottom,
        BottomToBottom,
        BottomToTop,
        CenterYToCenterY,
        CenterYToScreen
    }

    /// <summary>
    /// 对齐计算结果
    /// </summary>
    public struct SnapResult
    {
        public Vector2 SnappedPosition;
        public bool HasSnapX;
        public bool HasSnapY;
        public float GuideX;
        public float GuideY;
        public SnapAlignmentType TypeX;
        public SnapAlignmentType TypeY;
    }

    /// <summary>
    /// Figma 级智能对齐参考线中枢 (Smart Dynamic Alignment Guides & Snap Visualizer)
    /// 当用户拖拽小组件时，实时探测相邻组件边缘、中心轴与屏幕对称轴，并在画布上绘制
    /// 霓虹动态导引虚线与实时对齐光标。
    /// </summary>
    public class WidgetSmartGuides : MonoBehaviour
    {
        private static WidgetSmartGuides _instance;
        public static WidgetSmartGuides Instance => _instance;

        private RectTransform _rootRt;
        private Canvas _canvas;

        // 对象池：参考线
        private const int LinePoolSize = 6;
        private readonly List<Image> _vLines = new List<Image>();
        private readonly List<Image> _hLines = new List<Image>();

        // 标签：显示对齐类型或间距
        private Text _guideLabelX;
        private Text _guideLabelY;

        private Color _cyanCol = new Color(0f, 0.9f, 1f, 0.85f);
        private Color _magentaCol = new Color(0.95f, 0.25f, 0.8f, 0.85f);
        private Color _goldCol = new Color(1f, 0.8f, 0.1f, 0.9f);

        public void Initialize(RectTransform parentRt, Canvas canvas)
        {
            _instance = this;
            _rootRt = parentRt;
            _canvas = canvas;

            GameObject guidesContainer = new GameObject("SmartGuidesContainer", typeof(RectTransform));
            guidesContainer.transform.SetParent(_rootRt, false);
            guidesContainer.transform.SetAsLastSibling();

            RectTransform cRt = guidesContainer.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.sizeDelta = Vector2.zero;
            cRt.anchoredPosition = Vector2.zero;

            // 1. 初始化垂直与水平导引线池
            for (int i = 0; i < LinePoolSize; i++)
            {
                _vLines.Add(CreateGuideLine(guidesContainer.transform, $"VLine_{i}", true));
                _hLines.Add(CreateGuideLine(guidesContainer.transform, $"HLine_{i}", false));
            }

            // 2. 初始化对齐文本胶囊
            _guideLabelX = CreateGuideBadge(guidesContainer.transform, "LabelX");
            _guideLabelY = CreateGuideBadge(guidesContainer.transform, "LabelY");

            HideAllGuides();
        }

        private Image CreateGuideLine(Transform parent, string name, bool isVertical)
        {
            GameObject lineObj = new GameObject(name, typeof(RectTransform), typeof(Image));
            lineObj.transform.SetParent(parent, false);

            RectTransform rt = lineObj.GetComponent<RectTransform>();
            Image img = lineObj.GetComponent<Image>();
            img.raycastTarget = false;

            if (isVertical)
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(1.5f, 0f);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(0f, 1.5f);
            }

            lineObj.SetActive(false);
            return img;
        }

        private Text CreateGuideBadge(Transform parent, string name)
        {
            GameObject badgeObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
            badgeObj.transform.SetParent(parent, false);

            RectTransform rt = badgeObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(74f, 18f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image bg = badgeObj.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
            bg.raycastTarget = false;

            Outline outline = badgeObj.GetComponent<Outline>();
            outline.effectColor = _cyanCol;
            outline.effectDistance = new Vector2(1f, 1f);

            Text txt = UIFactory.CreateText(badgeObj.transform, "Text", "0", 9, TextAnchor.MiddleCenter, Color.white);
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            badgeObj.SetActive(false);
            return txt;
        }

        /// <summary>
        /// 核心计算：根据拖拽组件当前提议位置，计算磁吸结果并自动渲染智能导引线
        /// </summary>
        public SnapResult EvaluateAndShowGuides(BaseFlightWidget draggingWidget, Vector2 proposedPos, float threshold = 8f)
        {
            SnapResult res = new SnapResult
            {
                SnappedPosition = proposedPos,
                HasSnapX = false,
                HasSnapY = false,
                TypeX = SnapAlignmentType.None,
                TypeY = SnapAlignmentType.None
            };

            if (draggingWidget == null || !WidgetDragHandler.EnableMagneticSnap)
            {
                HideAllGuides();
                return res;
            }

            Rect myBounds = WidgetSelectionManager.GetWidgetBounds(draggingWidget);
            float halfW = myBounds.width * 0.5f;
            float halfH = myBounds.height * 0.5f;

            float myLeft = proposedPos.x - halfW;
            float myRight = proposedPos.x + halfW;
            float myBottom = proposedPos.y - halfH;
            float myTop = proposedPos.y + halfH;

            float bestDistX = threshold;
            float bestDistY = threshold;

            // 1. 优先检测与屏幕对称中轴 (X=0) 对齐
            if (Mathf.Abs(proposedPos.x) < bestDistX)
            {
                bestDistX = Mathf.Abs(proposedPos.x);
                res.SnappedPosition.x = 0f;
                res.GuideX = 0f;
                res.HasSnapX = true;
                res.TypeX = SnapAlignmentType.CenterXToScreen;
            }

            // 2. 检测与屏幕地平中轴 (Y=0) 对齐
            if (Mathf.Abs(proposedPos.y) < bestDistY)
            {
                bestDistY = Mathf.Abs(proposedPos.y);
                res.SnappedPosition.y = 0f;
                res.GuideY = 0f;
                res.HasSnapY = true;
                res.TypeY = SnapAlignmentType.CenterYToScreen;
            }

            // 3. 遍历其他所有组件进行几何边缘与中心吸附
            if (NavballHUD.Instance != null && NavballHUD.Instance.ModularWidgets != null)
            {
                foreach (var other in NavballHUD.Instance.ModularWidgets)
                {
                    if (other == null || other == draggingWidget || !other.gameObject.activeInHierarchy) continue;
                    if (WidgetSelectionManager.IsSelected(other)) continue;

                    Rect ob = WidgetSelectionManager.GetWidgetBounds(other);

                    // X 轴测试
                    // Center - Center
                    float dCC = Mathf.Abs(proposedPos.x - ob.center.x);
                    if (dCC < bestDistX)
                    {
                        bestDistX = dCC;
                        res.SnappedPosition.x = ob.center.x;
                        res.GuideX = ob.center.x;
                        res.HasSnapX = true;
                        res.TypeX = SnapAlignmentType.CenterXToCenterX;
                    }

                    // Left - Left
                    float dLL = Mathf.Abs(myLeft - ob.xMin);
                    if (dLL < bestDistX)
                    {
                        bestDistX = dLL;
                        res.SnappedPosition.x = ob.xMin + halfW;
                        res.GuideX = ob.xMin;
                        res.HasSnapX = true;
                        res.TypeX = SnapAlignmentType.LeftToLeft;
                    }

                    // Right - Right
                    float dRR = Mathf.Abs(myRight - ob.xMax);
                    if (dRR < bestDistX)
                    {
                        bestDistX = dRR;
                        res.SnappedPosition.x = ob.xMax - halfW;
                        res.GuideX = ob.xMax;
                        res.HasSnapX = true;
                        res.TypeX = SnapAlignmentType.RightToRight;
                    }

                    // Left - Right (间隙贴合)
                    float dLR = Mathf.Abs(myLeft - ob.xMax);
                    if (dLR < bestDistX)
                    {
                        bestDistX = dLR;
                        res.SnappedPosition.x = ob.xMax + halfW;
                        res.GuideX = ob.xMax;
                        res.HasSnapX = true;
                        res.TypeX = SnapAlignmentType.LeftToRight;
                    }

                    // Right - Left (间隙贴合)
                    float dRL = Mathf.Abs(myRight - ob.xMin);
                    if (dRL < bestDistX)
                    {
                        bestDistX = dRL;
                        res.SnappedPosition.x = ob.xMin - halfW;
                        res.GuideX = ob.xMin;
                        res.HasSnapX = true;
                        res.TypeX = SnapAlignmentType.RightToLeft;
                    }

                    // Y 轴测试
                    // Center - Center
                    float dCC_Y = Mathf.Abs(proposedPos.y - ob.center.y);
                    if (dCC_Y < bestDistY)
                    {
                        bestDistY = dCC_Y;
                        res.SnappedPosition.y = ob.center.y;
                        res.GuideY = ob.center.y;
                        res.HasSnapY = true;
                        res.TypeY = SnapAlignmentType.CenterYToCenterY;
                    }

                    // Top - Top
                    float dTT = Mathf.Abs(myTop - ob.yMax);
                    if (dTT < bestDistY)
                    {
                        bestDistY = dTT;
                        res.SnappedPosition.y = ob.yMax - halfH;
                        res.GuideY = ob.yMax;
                        res.HasSnapY = true;
                        res.TypeY = SnapAlignmentType.TopToTop;
                    }

                    // Bottom - Bottom
                    float dBB = Mathf.Abs(myBottom - ob.yMin);
                    if (dBB < bestDistY)
                    {
                        bestDistY = dBB;
                        res.SnappedPosition.y = ob.yMin + halfH;
                        res.GuideY = ob.yMin;
                        res.HasSnapY = true;
                        res.TypeY = SnapAlignmentType.BottomToBottom;
                    }

                    // Top - Bottom (间隙贴合)
                    float dTB = Mathf.Abs(myTop - ob.yMin);
                    if (dTB < bestDistY)
                    {
                        bestDistY = dTB;
                        res.SnappedPosition.y = ob.yMin - halfH;
                        res.GuideY = ob.yMin;
                        res.HasSnapY = true;
                        res.TypeY = SnapAlignmentType.TopToBottom;
                    }

                    // Bottom - Top (间隙贴合)
                    float dBT = Mathf.Abs(myBottom - ob.yMax);
                    if (dBT < bestDistY)
                    {
                        bestDistY = dBT;
                        res.SnappedPosition.y = ob.yMax + halfH;
                        res.GuideY = ob.yMax;
                        res.HasSnapY = true;
                        res.TypeY = SnapAlignmentType.BottomToTop;
                    }
                }
            }

            // 4. 更新参考线视觉
            UpdateGuideVisuals(res);

            return res;
        }

        private void UpdateGuideVisuals(SnapResult snap)
        {
            HideAllGuides();

            // 绘制 X 轴参考线
            if (snap.HasSnapX && _vLines.Count > 0)
            {
                var line = _vLines[0];
                line.gameObject.SetActive(true);
                RectTransform rt = line.rectTransform;
                rt.anchoredPosition = new Vector2(snap.GuideX, 0f);

                Color lineCol = snap.TypeX == SnapAlignmentType.CenterXToScreen ? _goldCol :
                               (snap.TypeX == SnapAlignmentType.CenterXToCenterX ? _cyanCol : _magentaCol);
                line.color = lineCol;

                if (_guideLabelX != null)
                {
                    _guideLabelX.transform.parent.gameObject.SetActive(true);
                    RectTransform lRt = _guideLabelX.transform.parent.GetComponent<RectTransform>();
                    lRt.anchoredPosition = new Vector2(snap.GuideX, snap.SnappedPosition.y + 35f);
                    _guideLabelX.text = $"X: {snap.GuideX:F0}";
                    _guideLabelX.color = lineCol;
                }
            }

            // 绘制 Y 轴参考线
            if (snap.HasSnapY && _hLines.Count > 0)
            {
                var line = _hLines[0];
                line.gameObject.SetActive(true);
                RectTransform rt = line.rectTransform;
                rt.anchoredPosition = new Vector2(0f, snap.GuideY);

                Color lineCol = snap.TypeY == SnapAlignmentType.CenterYToScreen ? _goldCol :
                               (snap.TypeY == SnapAlignmentType.CenterYToCenterY ? _cyanCol : _magentaCol);
                line.color = lineCol;

                if (_guideLabelY != null)
                {
                    _guideLabelY.transform.parent.gameObject.SetActive(true);
                    RectTransform lRt = _guideLabelY.transform.parent.GetComponent<RectTransform>();
                    lRt.anchoredPosition = new Vector2(snap.SnappedPosition.x + 45f, snap.GuideY);
                    _guideLabelY.text = $"Y: {snap.GuideY:F0}";
                    _guideLabelY.color = lineCol;
                }
            }
        }

        public void HideAllGuides()
        {
            for (int i = 0; i < _vLines.Count; i++) _vLines[i].gameObject.SetActive(false);
            for (int i = 0; i < _hLines.Count; i++) _hLines[i].gameObject.SetActive(false);
            if (_guideLabelX != null) _guideLabelX.transform.parent.gameObject.SetActive(false);
            if (_guideLabelY != null) _guideLabelY.transform.parent.gameObject.SetActive(false);
        }
    }
}
