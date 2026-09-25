using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Framework
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 通用航电卡片大父类 (Base Avionics Widget)
    // 【核心设计哲学】为所有常规机载航电仪表提供最底层、最通用的管线与物理缺省值：
    //   1. 尺寸与排版：缺省 BaseSize = (160, 50)，缺省自动构建标准卡片底板与边框 (AutoCreateCardFrame = true)
    //   2. 刷新阶梯：缺省接入 30Hz 标准航电更新 (RefreshTier = Standard)
    //   3. 零样板遥测：缺省实现空 OnUpdateTelemetry，微控件声明即可全自动由基类调度求值
    //   4. 零样板生命周期：基类完整继承与托管 ApplyTheme 与 OnDestroy 解绑链
    //   5. 实用航电算子：内置 Token 计算、数值归一化与高效脏标记设值辅助函数
    // 派生航电组件仅需在头部声明极简 DSL 控件 (TextWidget, LinearBarWidget, ToggleButtonWidget) 即可开箱即用！
    // ====================================================================================================

    /// <summary>
    /// 标准机载航电卡片组件大基类。
    /// 预设了所有通用的航电物理尺寸、卡片底板、刷新率与更新缺省值，派生类如有特殊需求可按需 override。
    /// </summary>
    public abstract class BaseAvionicsWidget : BaseFlightWidget
    {
        /// <summary>
        /// 标准航电缺省刷新率：30Hz 标准航电阶梯 (Standard)。
        /// 如为 60Hz 姿态球/动态陀螺可 override 为 Critical；如为电力/轨道参数可 override 为 Relaxed。
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        /// <summary>
        /// 源码级自定义精确刷新频率 (Hz)。缺省为 0 (采用 RefreshTier 阶梯)。
        /// 若填写大于 0 的数值 (例如 20f, 14.5f)，系统将直接以此精确频率驱动组件更新，拥有高于 RefreshTier 的生效优先级。
        /// </summary>
        public override float CustomHz => 0f;

        /// <summary>
        /// 标准航电缺省物理卡片尺寸：160x50 原生参考像素。
        /// 派生组件如有特殊规格（如 210x56、80x80、240x120）可直接重写。
        /// </summary>
        public override Vector2 BaseSize => new Vector2(160f, 50f);

        /// <summary>
        /// 标准航电缺省开启自动卡片底板与发光边框。
        /// 若开发纯透明 HUD 浮层或全屏覆盖物可 override 为 false。
        /// </summary>
        protected override bool AutoCreateCardFrame => true;

        /// <summary>
        /// 标准航电卡片外观角色：Normal。
        /// </summary>
        protected override CardStyleRole CardRole => CardStyleRole.Normal;

        /// <summary>
        /// 默认遥测驱动钩子。
        /// 当组件完全由声明式 DSL 控件构成时，基类 MasterUpdateTelemetry 会自动执行微控件更新与 Token 计算，
        /// 派生类可直接省略重写此方法。若有额外特异化计算可自由 override。
        /// </summary>
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
        }

        /// <summary>
        /// 主题下发钩子。默认全自动将主题广播给所有纳管微控件。
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
        }

        /// <summary>
        /// 安全销毁钩子。默认完整调用基类解绑链与控件释放。
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        #region Avionics Evaluation & Computation Helpers

        /// <summary>
        /// 便捷通配符字符串求值工具（自动空值回退与安全保护）
        /// </summary>
        protected string EvalToken(string token, IFlightTelemetry telemetry, string fallback = "---")
        {
            if (telemetry == null || string.IsNullOrEmpty(token)) return fallback;
            string res = TelemetryTokenEngine.Evaluate(token, telemetry);
            return string.IsNullOrEmpty(res) ? fallback : res;
        }

        /// <summary>
        /// 便捷数值型通配符求值工具（自动 NaN 保护与安全回退）
        /// </summary>
        protected double EvalNumeric(string token, IFlightTelemetry telemetry, double fallback = 0.0)
        {
            if (telemetry == null || string.IsNullOrEmpty(token)) return fallback;
            double res = TelemetryTokenEngine.EvaluateNumeric(token, telemetry);
            return double.IsNaN(res) ? fallback : res;
        }

        /// <summary>
        /// 便捷数值范围归一化工具 (0.0 ~ 1.0)
        /// </summary>
        protected float NormalizeValue(double val, double min, double max)
        {
            if (double.IsNaN(val)) return 0f;
            double range = max - min;
            if (range <= 0.00001) return 0f;
            return Mathf.Clamp01((float)((val - min) / range));
        }

        /// <summary>
        /// 高效文本防抖写入（内容未改变时不触发 UGUI 网格与顶点重建）
        /// </summary>
        protected bool SetText(Text target, string text)
        {
            return SetTextIfChanged(target, text);
        }

        /// <summary>
        /// 高效柱条/进度填充写入（比例未改变时不触发重绘）
        /// </summary>
        protected bool SetBarFill(Image fillImage, float ratio)
        {
            return SetImageFillIfChanged(fillImage, ratio);
        }

        #endregion
    }
}
