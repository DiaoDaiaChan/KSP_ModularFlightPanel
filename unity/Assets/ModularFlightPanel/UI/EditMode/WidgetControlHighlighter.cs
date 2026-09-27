using System;
using UnityEngine;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 控件级所见即所得高亮器 (Micro-Control Live Highlight Gizmo)
    /// 当在定制面板悬停/聚焦某个子控件时，在游戏屏幕空间实时框选该微控件的包围盒与标签
    /// </summary>
    public static class WidgetControlHighlighter
    {
        public static IWidgetControl HighlightedControl { get; set; }
        private static Texture2D _borderTex;

        private static Texture2D GetBorderTex()
        {
            if (_borderTex == null)
            {
                _borderTex = new Texture2D(1, 1);
                _borderTex.SetPixel(0, 0, Color.white);
                _borderTex.Apply();
            }
            return _borderTex;
        }

        public static void DrawGizmo(Canvas canvas)
        {
            if (HighlightedControl == null || HighlightedControl.RectTransform == null) return;
            RectTransform rt = HighlightedControl.RectTransform;
            if (!rt.gameObject.activeInHierarchy) return;

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Camera cam = (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;
            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 p1 = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            Vector2 p3 = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            float guiX = minX - 2f;
            float guiY = (Screen.height - maxY) - 2f;
            float guiW = (maxX - minX) + 4f;
            float guiH = (maxY - minY) + 4f;

            if (guiW < 4f || guiH < 4f) return;

            Color prevCol = GUI.color;
            Color cyanHighlight = new Color(0f, 0.9f, 1f, 0.85f);
            Color fillCol = new Color(0f, 0.9f, 1f, 0.12f);
            Texture2D tex = GetBorderTex();

            // 1. 半透明填充背板
            GUI.color = fillCol;
            GUI.DrawTexture(new Rect(guiX, guiY, guiW, guiH), tex);

            // 2. 边框 (Top, Bottom, Left, Right 2px)
            GUI.color = cyanHighlight;
            GUI.DrawTexture(new Rect(guiX, guiY, guiW, 2f), tex);
            GUI.DrawTexture(new Rect(guiX, guiY + guiH - 2f, guiW, 2f), tex);
            GUI.DrawTexture(new Rect(guiX, guiY, 2f, guiH), tex);
            GUI.DrawTexture(new Rect(guiX + guiW - 2f, guiY, 2f, guiH), tex);

            // 3. 悬浮微标牌
            string label = $" ⌖ {HighlightedControl.DisplayName} ";
            Vector2 tagSize = GUI.skin.label.CalcSize(new GUIContent(label));
            float tagY = guiY - tagSize.y - 2f;
            if (tagY < 5f) tagY = guiY + guiH + 2f;

            GUI.color = new Color(0.05f, 0.15f, 0.25f, 0.92f);
            GUI.DrawTexture(new Rect(guiX, tagY, tagSize.x + 8f, tagSize.y), tex);
            GUI.color = cyanHighlight;
            GUI.Label(new Rect(guiX + 2f, tagY, tagSize.x + 4f, tagSize.y), $"<b>{label}</b>");

            GUI.color = prevCol;
        }
    }
}
