using System;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版分级图标深度挂钩提供者契约 (Pure Unity Contract)
    /// 允许组件安全访问原版 StageIcon 贴图图集与 UV 贴图坐标，完全与 KSP 场景对象解耦。
    /// </summary>
    public interface IStockStageIconProvider
    {
        Texture StockAtlas { get; }
        bool HasStockAtlas { get; }
        Rect GetStockIconUv(string iconType);
        Rect GetStockIconUv(int iconIndex);
    }

    /// <summary>
    /// 原版分级图标挂钩服务总线
    /// </summary>
    public static class StockStageIconService
    {
        private static IStockStageIconProvider _provider;
        public static IStockStageIconProvider Provider
        {
            get => _provider;
            set => _provider = value;
        }
    }
}
