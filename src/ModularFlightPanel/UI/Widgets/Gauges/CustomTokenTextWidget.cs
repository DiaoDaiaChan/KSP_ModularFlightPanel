using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本 [1/3]：纯读数卡片型航电组件 (Standard Readout Card Blueprint)
    // 【头部全集中声明范式】几何尺寸、底板样式、刷新率与全部控件排版【100% 集中在类头部一次性声明】
    // ====================================================================================================

    /// <summary>
    /// 自定义通配符文本飞行卡片组件 (Custom Token Text Card Widget)
    /// 玩家可以在配置中写入任意参数模板 (例如 "{SPD:SURF:F1} m/s | Q: {Q:F2} | TWR: {TWR:F2}")，
    /// 标题与内容全量由 TelemetryTokenEngine 实时求值并具备脏检查保护。
    /// </summary>
    [FlightWidget("custom_token", "custom_text", "custom",
        Category = WidgetCategory.Gauges,
        DisplayName = "多通道遥测动态卡片",
        Description = "支持任意遥测通配符模板的高对比度动态数据卡片。",
        DefaultWidgetId = "custom.telemetry_card",
        DefaultX = 0f,
        DefaultY = 0f)]
    public class CustomTokenTextWidget : BaseFlightWidget
    {
        // ====================================================================================
        // 【头部全集中声明区】基础尺寸、底板样式、刷新率与全部子控件契约 (一屏之内尽收眼底)
        // ====================================================================================
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override Vector2 BaseSize => new Vector2(210f, 56f);
        protected override bool AutoCreateCardFrame => true;

#pragma warning disable CS0649 // 字段由基类根据特性全自动反射实例化并注入
        /// <summary>卡片标题：坐标 (6, 18), 尺寸 198x16, 字号 10, 左上对齐</summary>
        [WidgetControl("card_title", TextStyleRole.Label, "卡片标题",
            X = 6f, Y = 18f, Width = 198f, Height = 16f, FontSize = 10f, Alignment = TextAnchor.UpperLeft)]
        private Text _titleText;

        /// <summary>动态核心内容：坐标 (6, -12), 尺寸 198x30, 字号 13, 左下对齐</summary>
        [WidgetControl("card_content", TextStyleRole.PrimaryValue, "动态内容",
            X = 6f, Y = -12f, Width = 198f, Height = 30f, FontSize = 13f, Alignment = TextAnchor.LowerLeft)]
        private Text _contentValueText;
#pragma warning restore CS0649

        // ====================================================================================
        // 【视图初始化】0 行 UI 创建样板代码！基类已按头部特性全自动实例化并注入字段
        // ====================================================================================
        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            // 特性声明控件已由基类提前自动构建注入；主题切换由基类自动下发，本方法无需多余逻辑
        }

        // ====================================================================================
        // 【遥测数据动态刷新】纯业务求值与防重绘
        // ====================================================================================
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            // 1. 标题通配符求值与防重绘保护
            string titleTpl = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : "TELEMETRY";
            SetTextIfChanged(_titleText, TelemetryTokenEngine.Evaluate(titleTpl, telemetry));

            // 2. 动态内容通配符求值与防重绘保护
            string contentTpl = Config?.CustomTemplate;
            SetTextIfChanged(_contentValueText, !string.IsNullOrEmpty(contentTpl) ? TelemetryTokenEngine.Evaluate(contentTpl, telemetry) : "---");
        }
    }
}
