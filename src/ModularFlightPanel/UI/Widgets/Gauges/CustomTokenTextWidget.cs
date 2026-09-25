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
    // 直接继承 BaseAvionicsWidget，享受通用航电物理尺寸、卡片底板、刷新阶梯与默认遥测驱动
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
        // ── 头部集中声明区：尺寸、刷新率与全部语义泊靠 DSL 控件 (一屏之内尽收眼底) ──
        public override Vector2 BaseSize => new Vector2(210f, 56f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        public TextWidget Title = TextWidget.Title("TELEMETRY");
        public TextWidget Content = TextWidget.Value("---");

        // ── 遥测数据动态刷新：纯业务求值，赋值自动触发内部脏检查与防重绘 ──
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            string titleTpl = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : "TELEMETRY";
            Title.Text = EvalToken(titleTpl, telemetry, "TELEMETRY");

            string contentTpl = Config?.CustomTemplate;
            Content.Text = !string.IsNullOrEmpty(contentTpl) ? EvalToken(contentTpl, telemetry, "---") : "---";
        }
    }
}
