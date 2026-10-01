using System;
using UnityEngine;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 设置工作台标签页生命周期与绘制契约 (Settings Workbench Tab Contract)
    /// 规范航电工程工作台各个功能中枢的生命周期、响应式绘制与数据暂存提交。
    /// </summary>
    public interface ISettingsTab
    {
        /// <summary>
        /// 标签页唯一标识符 (例如 "studio", "themes", "profiles", "diagnostics")
        /// </summary>
        string TabId { get; }

        /// <summary>
        /// 标签页显示名称（已本地化字符串）
        /// </summary>
        string DisplayTitle { get; }

        /// <summary>
        /// 当切换进入当前标签页时调用
        /// </summary>
        void OnEnter();

        /// <summary>
        /// 当切换离开当前标签页时调用（提交暂存、重置局部搜索等）
        /// </summary>
        void OnExit();

        /// <summary>
        /// 绘制标签页内部主体内容
        /// </summary>
        /// <param name="availableHeight">工作台自适应视口测算出的可用净高度 (像素)</param>
        void Draw(float availableHeight);
    }
}
