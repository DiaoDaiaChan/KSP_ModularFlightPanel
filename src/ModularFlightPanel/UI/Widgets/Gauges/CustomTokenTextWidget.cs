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
    // ====================================================================================================
    //
    // 【架构蓝本说明 (Architecture Blueprint Purpose)】:
    // 本组件是全模组“纯数据/通配符读数卡片型仪表”的官方标准蓝本，展示了 MFP 现代化极简航电开发范式：
    // 1. 【上级自动派发尺寸 (BaseSize Auto-Scaling)】:
    //    - 声明 `BaseSize => new Vector2(210f, 56f)`，基类在 BaseInitialize 自动完成 DPI 缩放并赋予 RectTransform.sizeDelta。
    //    - 彻底省去子类重复计算和赋值尺寸的代码。
    // 2. 【上级自动底板与边框 (AutoCreateCardFrame Auto-Generation)】:
    //    - 声明 `AutoCreateCardFrame => true`，基类全自动构建航电背景板 (Image) 与高对比度边框 (Outline)，
    //      并自动包装为 "card_frame" 微控件纳入样式调度。派生类 0 行底板样板代码！
    // 3. 【声明式微控件特性注解 ([WidgetControl] Declarative Binding)】:
    //    - 字段直接使用 `[WidgetControl("card_title", TextStyleRole.Label)]` 标记，基类在 OnInitialize 后
    //      自动通过反射扫描装配、绑定配置与下发主题，组件开发者无需手写任何 Controls.Register。
    // 4. 【零样板生命周期 (Zero-Boilerplate Lifecycle)】:
    //    - 主题切换 (ApplyTheme) 与销毁清理 (OnDestroy) 100% 由基类托管完成，无需手写空壳 override。
    // 5. 【防御式遥测安全 (Upstream Telemetry Guard & Zero-GC Dirty Protection)】:
    //    - 基类 MasterUpdateTelemetry 统一执行空船守卫 (HasVessel Guard) 并先行动画化微控件。
    //    - 业务层使用 SetTextIfChanged 阻断 95% 无意义的 UGUI 顶点缓冲区全量重建，达成零 GC 运行。
    //
    // 【适用类型】: 各种状态卡片、通配符读数卡、引擎参数卡、轨道数显框、诊断文字板等。
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
        // ------------------------------------------------------------------------------------
        // [Part 1: 刷新阶梯与尺寸配置 (Refresh Tier & Geometry Contract)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 读数卡片采用 Relaxed 阶梯 (标称 10Hz)，在大尺度飞行与复杂多通道下极大降低 CPU 负担
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        /// <summary>
        /// 声明 1.0x DPI 下的原生设计基准尺寸 (宽 210px, 高 56px)
        /// 基类在 BaseInitialize 阶段会自动将其乘以 CurrentDpiScale 并赋予 RectTransform.sizeDelta
        /// </summary>
        public override Vector2 BaseSize => new Vector2(210f, 56f);

        /// <summary>
        /// 开启上级自动卡片底板构建：基类将自动构建背景板、细边框，并自动纳入微控件体系响应主题切换
        /// </summary>
        protected override bool AutoCreateCardFrame => true;

        // ------------------------------------------------------------------------------------
        // [Part 2: 声明式微控件字段 ([WidgetControl] Declarative Fields)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 卡片标题标签：标记为微控件后，基类自动按 TextStyleRole.Label 赋予主题颜色并纳入配置管理
        /// </summary>
        [WidgetControl("card_title", TextStyleRole.Label, "卡片标题")]
        private Text _titleText;

        /// <summary>
        /// 动态核心内容读数：标记为微控件后，基类自动按 TextStyleRole.PrimaryValue 赋予主题强调色
        /// </summary>
        [WidgetControl("card_content", TextStyleRole.PrimaryValue, "动态内容")]
        private Text _contentValueText;

        // 运行时缓存与通配符模板
        private string _titleTemplate = "TELEMETRY";

        // ------------------------------------------------------------------------------------
        // [Part 3: 组件视图排版 (OnInitialize)]
        // ------------------------------------------------------------------------------------

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Vector2 cardSize = RectTransform.sizeDelta;

            _titleTemplate = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName : "TELEMETRY";

            // 1. 构建顶部标题文本 (字号 10px * DPI)
            int titleSize = Mathf.RoundToInt(10f * s);
            _titleText = UIFactory.CreateText(transform, "Card_Title", _titleTemplate, titleSize, TextAnchor.UpperLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trt = _titleText.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(cardSize.x - 12f * s, 16f * s);
            trt.anchoredPosition = new Vector2(6f * s, (cardSize.y * 0.5f) - 10f * s);

            // 2. 构建底部主内容文本 (字号 13px * DPI)
            int contentSize = Mathf.RoundToInt(13f * s);
            _contentValueText = UIFactory.CreateText(transform, "Card_Content", "---", contentSize, TextAnchor.LowerLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform crt = _contentValueText.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(cardSize.x - 12f * s, 30f * s);
            crt.anchoredPosition = new Vector2(6f * s, -(cardSize.y * 0.5f) + 16f * s);

            // 【注】无需手动调用 Controls.Register 或 ApplyCard：
            // 基类 BaseInitialize 会在 OnInitialize 结束后自动通过反射扫描带 [WidgetControl] 特性的字段完成注册，
            // 并在玩家切换主题时自动分发 ApplyTheme！
        }

        // ------------------------------------------------------------------------------------
        // [Part 4: 遥测数据动态刷新 (OnUpdateTelemetry)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 遥测更新主逻辑：
        /// 上级已在 MasterUpdateTelemetry 执行了空值与空船守卫 (HasVessel Guard)，此处专注纯粹的业务求值
        /// </summary>
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            // 1. 标题通配符求值与防重绘保护 (例如支持在标题中插入 "{VESSEL:NAME}")
            string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            SetTextIfChanged(_titleText, evalTitle);

            // 2. 动态内容通配符求值与防重绘保护 (CustomTemplate 为空时回退显示 "---")
            string tpl = Config?.CustomTemplate;
            string eval = !string.IsNullOrEmpty(tpl) ? TelemetryTokenEngine.Evaluate(tpl, telemetry) : "---";
            SetTextIfChanged(_contentValueText, eval);
        }
    }
}
