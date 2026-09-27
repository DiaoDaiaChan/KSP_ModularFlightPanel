using System;
using UnityEngine;
using UnityEngine.UI;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 航电智能 UI 零开销脏检查扩展套件 (Avionics Smart UI Extensions)
    /// 
    /// 架构宗旨（成功陷阱 Pit of Success）：
    /// 彻底消除业务组件层手写重复静态判断与私有 _lastX 字段的认知负担。
    /// 开发者以最符合 Unity 习惯的链式扩展（如 image.SetColor(c)、text.SetTextSafe(s)）书写代码，
    /// 底层自动完成值比对与浮点容差过滤，阻断无意义的 Unity UGUI Canvas 顶点网格重建与布局重排。
    /// </summary>
    public static class SmartUIExtensions
    {
        #region Graphic & Image Extensions

        /// <summary>
        /// 智能颜色赋值：仅在颜色真正发生变化时写入 Graphic.color，避免 Canvas 网格重建
        /// </summary>
        public static bool SetColor(this Graphic graphic, Color targetColor)
        {
            if (graphic == null) return false;
            if (graphic.color == targetColor) return false;
            graphic.color = targetColor;
            return true;
        }

        /// <summary>
        /// 智能透明度赋值：仅在 Alpha 变化超过容差时写入
        /// </summary>
        public static bool SetAlpha(this Graphic graphic, float alpha, float tolerance = 0.002f)
        {
            if (graphic == null) return false;
            Color cur = graphic.color;
            if (Mathf.Abs(cur.a - alpha) <= tolerance) return false;
            cur.a = alpha;
            graphic.color = cur;
            return true;
        }

        /// <summary>
        /// 智能描边颜色赋值：仅在描边颜色变动时写入 Outline.effectColor，杜绝顶点流重建
        /// </summary>
        public static bool SetColor(this Outline outline, Color targetColor)
        {
            if (outline == null) return false;
            if (outline.effectColor == targetColor) return false;
            outline.effectColor = targetColor;
            return true;
        }

        /// <summary>
        /// 智能填充比率赋值：仅当 fillAmount 变化超过容差时写入
        /// </summary>
        public static bool SetFillAmountSafe(this Image image, float fillAmount, float epsilon = 0.001f)
        {
            if (image == null) return false;
            if (Mathf.Abs(image.fillAmount - fillAmount) <= epsilon) return false;
            image.fillAmount = fillAmount;
            return true;
        }

        #endregion

        #region Text Extensions

        /// <summary>
        /// 智能文本写入：字符串内容相同或引用相同时直接拦截，零 GC 阻断 UGUI Text 顶点重建
        /// </summary>
        public static bool SetTextSafe(this Text textComponent, string newText)
        {
            if (textComponent == null || newText == null) return false;
            string cur = textComponent.text;
            if (object.ReferenceEquals(cur, newText)) return false;
            if (cur != null && cur.Length == newText.Length && string.Equals(cur, newText, StringComparison.Ordinal)) return false;
            textComponent.text = newText;
            return true;
        }

        #endregion

        #region Transform & RectTransform Extensions

        /// <summary>
        /// 智能锚点坐标赋值：仅到位移超出容差 (默认 0.05 像素) 时写入，彻底阻断微小浮点抖动
        /// </summary>
        public static bool SetAnchoredPositionSafe(this RectTransform target, Vector2 newPos, float tolerance = 0.05f)
        {
            if (target == null) return false;
            Vector2 cur = target.anchoredPosition;
            if (Mathf.Abs(cur.x - newPos.x) <= tolerance && Mathf.Abs(cur.y - newPos.y) <= tolerance)
            {
                return false;
            }
            target.anchoredPosition = newPos;
            return true;
        }

        /// <summary>
        /// 智能尺寸赋值：仅当尺寸变化超出容差时写入 sizeDelta
        /// </summary>
        public static bool SetSizeDeltaSafe(this RectTransform target, Vector2 newSize, float tolerance = 0.05f)
        {
            if (target == null) return false;
            Vector2 cur = target.sizeDelta;
            if (Mathf.Abs(cur.x - newSize.x) <= tolerance && Mathf.Abs(cur.y - newSize.y) <= tolerance)
            {
                return false;
            }
            target.sizeDelta = newSize;
            return true;
        }

        /// <summary>
        /// 智能局部四元数旋转赋值：角度变化在容差内不写入
        /// </summary>
        public static bool SetLocalRotationSafe(this Transform target, Quaternion newRotation, float angleTolerance = 0.05f)
        {
            if (target == null) return false;
            if (Quaternion.Angle(target.localRotation, newRotation) <= angleTolerance) return false;
            target.localRotation = newRotation;
            return true;
        }

        /// <summary>
        /// 智能局部欧拉角旋转赋值：欧拉角变化在容差内不写入
        /// </summary>
        public static bool SetLocalEulerAnglesSafe(this Transform target, Vector3 newEuler, float angleTolerance = 0.05f)
        {
            if (target == null) return false;
            Vector3 cur = target.localEulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(cur.x, newEuler.x)) <= angleTolerance &&
                Mathf.Abs(Mathf.DeltaAngle(cur.y, newEuler.y)) <= angleTolerance &&
                Mathf.Abs(Mathf.DeltaAngle(cur.z, newEuler.z)) <= angleTolerance)
            {
                return false;
            }
            target.localEulerAngles = newEuler;
            return true;
        }

        /// <summary>
        /// 智能局部缩放赋值：变化超出容差时才写入 localScale
        /// </summary>
        public static bool SetLocalScaleSafe(this Transform target, Vector3 newScale, float tolerance = 0.001f)
        {
            if (target == null) return false;
            Vector3 cur = target.localScale;
            if (Mathf.Abs(cur.x - newScale.x) <= tolerance &&
                Mathf.Abs(cur.y - newScale.y) <= tolerance &&
                Mathf.Abs(cur.z - newScale.z) <= tolerance)
            {
                return false;
            }
            target.localScale = newScale;
            return true;
        }

        #endregion

        #region GameObject & Component Active State Extensions

        /// <summary>
        /// 智能显隐状态控制：状态未改变时不重复调用 SetActive，避免重复触发 OnEnable/OnDisable
        /// </summary>
        public static bool SetActiveSafe(this GameObject go, bool active)
        {
            if (go == null) return false;
            if (go.activeSelf == active) return false;
            go.SetActive(active);
            return true;
        }

        /// <summary>
        /// 智能组件宿主显隐状态控制
        /// </summary>
        public static bool SetActiveSafe(this Component comp, bool active)
        {
            if (comp == null || comp.gameObject == null) return false;
            if (comp.gameObject.activeSelf == active) return false;
            comp.gameObject.SetActive(active);
            return true;
        }

        #endregion
    }
}
