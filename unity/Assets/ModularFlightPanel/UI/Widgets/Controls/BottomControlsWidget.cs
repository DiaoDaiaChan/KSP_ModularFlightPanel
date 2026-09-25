using System;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本 [3/3]：交互控制型航电组件 (Interactive Flight Controls Blueprint)
    // 【现代声明式 UI 对象范式 (Object-DSL)】在类头部集中声明高阶交互控件，基类自动构建 UGUI、事件与主题管道
    // ====================================================================================================

    /// <summary>
    /// 一体化机载快速控制栏 (Avionics Flight Control Bar)
    /// 严丝合缝嵌合于姿态球正下方 (宽度 184px, 高度 22px, 完美容纳于双带之间)。
    /// 整合：
    /// 1. RCS 动力开关 (状态自保持与高亮)
    /// 2. SAS 主动力开关 (状态自保持与高亮)
    /// 3. REF FRAME 权威参考系一键切换胶囊 (左键循环 / 右键 Principia 权威参考系设置)
    /// 严格继承 BaseFlightWidget，零硬编码。
    /// </summary>
    [FlightWidget("bottom_controls", "bottom_bar_controls",
        Category = WidgetCategory.Controls,
        DisplayName = "底部快捷操纵条",
        Description = "RCS/SAS/刹车/起落架/车灯综合药丸式状态切换条。",
        DefaultWidgetId = "core.bottom_controls",
        DefaultX = 0f,
        DefaultY = -210f,
        IsSingleton = true,
        ExactIds = new[] { "core.bottom_controls" })]
    public class BottomControlsWidget : BaseAvionicsWidget
    {
        // ── 头部集中声明区：尺寸、刷新率与全部交互微控件 (一屏之内尽收眼底) ──
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override Vector2 BaseSize => new Vector2(184f, 22f);
        protected override bool AutoCreateCardFrame => false; // 紧凑型浮动药丸按键栏，无独立底板

        public ToggleButtonWidget Rcs = new ToggleButtonWidget("RCS", x: -68f, y: 0f, w: 38f, h: 18f, font: 8f)
        {
            OnClick = () => FlightTelemetryContext.Current?.ToggleRCS()
        };

        public ToggleButtonWidget Sas = new ToggleButtonWidget("SAS", x: -26f, y: 0f, w: 38f, h: 18f, font: 8f)
        {
            OnClick = () => FlightTelemetryContext.Current?.ToggleSAS()
        };

        public ActionButtonWidget Frame = new ActionButtonWidget("REF: SURFACE ▾", x: 43f, y: 0f, w: 90f, h: 18f, font: 7.5f)
        {
            OnClick = () => FlightTelemetryContext.Current?.CycleSpeedMode(),
            OnRightClick = () =>
            {
                if (OnTogglePrincipiaWindowAction != null)
                {
                    OnTogglePrincipiaWindowAction.Invoke();
                }
                else
                {
                    FlightTelemetryContext.Current?.CycleSpeedMode();
                }
            }
        };

        public static Action OnTogglePrincipiaWindowAction;

        // ── 视图初始化钩子：仅需绑定多语言悬浮提示 ──
        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            ApplyTooltips();
        }

        // ── 遥测数据动态刷新：自包装属性写入自动触发内置脏检查 ──
        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;

            // 1. RCS / SAS 状态同步 (内置脏检查，仅物理状态改变时触发视觉重绘)
            Rcs.IsActive = telem.IsRCSEnabled;
            Sas.IsActive = telem.IsSASEnabled;

            // 2. 参考系模式状态同步 (支持 Principia 权威参考系与原生 KSP 模式)
            string frameName = telem.SpeedModeName ?? "SURFACE";
            if (string.IsNullOrEmpty(frameName)) frameName = "SURFACE";
            string prefix = GetTemplateChannel("FRAME_PREFIX", "REF: ");
            string newFrameText = $"{prefix}{frameName} ▾";

            if (Frame.Text != newFrameText)
            {
                Frame.Text = newFrameText;

                bool isSpecial = frameName.StartsWith("ORB", StringComparison.OrdinalIgnoreCase) ||
                                 frameName.StartsWith("BARY", StringComparison.OrdinalIgnoreCase) ||
                                 frameName.StartsWith("INER", StringComparison.OrdinalIgnoreCase);
                bool isTarget = frameName.StartsWith("TGT", StringComparison.OrdinalIgnoreCase) ||
                                frameName.StartsWith("TAR", StringComparison.OrdinalIgnoreCase);

                TextStyleRole frameRole = isTarget ? TextStyleRole.Warning : (isSpecial ? TextStyleRole.Accent : TextStyleRole.SecondaryValue);
                Frame.TextRole = frameRole;
            }
        }

        // ── 视觉主题与通道文本应用 ──
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            Rcs.Text = GetTemplateChannel("RCS_LABEL", "RCS");
            Sas.Text = GetTemplateChannel("SAS_LABEL", "SAS");
        }

        // ── 国际化与悬浮提示系统 ──
        private void ApplyTooltips()
        {
            Rcs.SetTooltip(I18n.Tr("WIDGET_BOTTOM_RCS_TITLE", "RCS 姿态推力系统"), I18n.Tr("WIDGET_BOTTOM_RCS_DESC", "开启/关闭反作用姿控喷气动力 (RCS)"), "R");
            Sas.SetTooltip(I18n.Tr("WIDGET_BOTTOM_SAS_TITLE", "SAS 稳定性增益系统"), I18n.Tr("WIDGET_BOTTOM_SAS_DESC", "开启/关闭姿态稳定增益系统 (SAS)"), "T");
            Frame.SetTooltip(I18n.Tr("WIDGET_BOTTOM_FRAME_TITLE", "速度参考系模式"), I18n.Tr("WIDGET_BOTTOM_FRAME_DESC", "左键点击轮换 SURFACE / ORBIT / TARGET 参考系；右键呼出 Principia 权威参考系设置窗口。"));
        }

        protected override void OnLanguageChanged()
        {
            ApplyTooltips();
        }

        // ── 安全生命周期销毁清理 ──
        protected override void OnDestroy()
        {
            if (Rcs?.ButtonComponent != null) Rcs.ButtonComponent.onClick.RemoveAllListeners();
            if (Sas?.ButtonComponent != null) Sas.ButtonComponent.onClick.RemoveAllListeners();
            if (Frame?.ButtonComponent != null) Frame.ButtonComponent.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
