using UnityEngine;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 声明式自适应物理尺寸契约接口 (Adaptive Physical Sizing Contract)
    /// 用于解决小组件在非等比拉伸 (ScaleX / ScaleY) 时单纯使用 localScale 仿射拉伸导致文字与矢量畸变模糊的问题。
    /// 实现本接口的组件由基类直接将 ScaleX/ScaleY 映射为物理 sizeDelta 像素尺寸，保持 localScale 为 1:1 原生无畸变，
    /// 并触发 OnAdaptiveResize 回调执行视口、刻度池与排版布局的原生点对点重绘。
    /// </summary>
    public interface IAdaptiveSizeWidget
    {
        /// <summary>
        /// 是否允许非等比拉伸 (若返回 false 则 Gizmo 自动隐藏 4 边中点拉伸手柄，仅允许 4 角等比缩放，防止姿态球等被拉成椭圆)
        /// </summary>
        bool AllowNonUniformScale { get; }

        /// <summary>
        /// 最小物理基准尺寸 (防止拉伸过小发生布局崩溃)
        /// </summary>
        Vector2 MinBaseSize { get; }

        /// <summary>
        /// 最大物理基准尺寸 (防止拉伸超出安全视口)
        /// </summary>
        Vector2 MaxBaseSize { get; }

        /// <summary>
        /// 当组件物理像素尺寸发生改变时触发，通知组件自适应调整视口、导轨与子控件
        /// </summary>
        /// <param name="pixelSize">经过 DPI 与非等比系数换算后的实际 sizeDelta 物理像素尺寸</param>
        void OnAdaptiveResize(Vector2 pixelSize);
    }
}
