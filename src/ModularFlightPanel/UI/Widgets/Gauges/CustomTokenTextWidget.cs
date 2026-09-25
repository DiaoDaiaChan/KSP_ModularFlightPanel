using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本 [1/3]：纯读数卡片型航电组件 (Standard Readout Card Blueprint)
    // 【现代声明式 UI 对象范式 (Object-DSL)】在类头部集中声明高阶控件对象，基类全自动构建 UGUI 并纳管生命周期
    // ====================================================================================================

    /// <summary>
    /// 自定义通配符文本飞行卡片组件 (Custom Token Text Card Widget)
    /// 玩家可以在配置中写入任意参数模板 (例如 "{SPD:SURF:F1} m/s | Q: {Q:F2} | TWR: {TWR:F2}")，
    /// 标题与内容全量由 TelemetryTokenEngine 实时求值并具备内部脏检查保护。
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
        // ── 头部集中声明区：尺寸、底板样式、刷新率与全部声明式控件 (一屏之内尽收眼底) ──
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override Vector2 BaseSize => new Vector2(210f, 56f);
        protected override bool AutoCreateCardFrame => true;

        public TextWidget Title = new(TextStyleRole.Label, x: 6f, y: 18f, w: 198f, h: 16f, font: 10f, align: TextAnchor.UpperLeft);
        public TextWidget Content = new(TextStyleRole.PrimaryValue, x: 6f, y: -12f, w: 198f, h: 30f, font: 13f, align: TextAnchor.LowerLeft);

        // ── 遥测数据动态刷新：纯业务求值，赋值自动触发内部脏检查与防重绘 ──
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            string titleTpl = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : "TELEMETRY";
            Title.Text = TelemetryTokenEngine.Evaluate(titleTpl, telemetry);

            string contentTpl = Config?.CustomTemplate;
            Content.Text = !string.IsNullOrEmpty(contentTpl) ? TelemetryTokenEngine.Evaluate(contentTpl, telemetry) : "---";
        }
    }
}
