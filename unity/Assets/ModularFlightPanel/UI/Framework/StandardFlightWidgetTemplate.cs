using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本 [2/3]：标准航电组件范式标杆 (Standard Flight Widget Reference)
    // ====================================================================================================
    //
    // 【规范概述 (Standard Overview)】:
    // 本文件是 Modular Flight Panel 整个航电系统的【活的代码规范与标准示范标杆】。
    // 所有官方原生组件与第三方扩展组件【必须 100% 遵照本模板结构与规范书写】，禁止任何天马行空的各行其是！
    // 本文件自身在无头审计门禁（MFP-SPEC-001 ~ MFP-SPEC-008）中保持 0 错误、0 警告、0 颜色字面量。
    //
    // 【八大核心铁律 (Eight Non-Negotiable Rules)】:
    // 1. 【MFP-SPEC-001: 继承契约 (Unified Inheritance)】:
    //    - 必须继承 BaseFlightWidget 统一基类，接入 Sub-Canvas 独立隔离、自由拖拽、缩放、层级治理。
    // 2. 【MFP-SPEC-002: 阶梯刷新契约 (Refresh Tier Contract)】:
    //    - 必须显式声明 RefreshTier（姿态/导引 Critical 60Hz；表盘/滚带 Standard 30Hz；电力/维生/控制 Relaxed 10Hz）。
    //    - 全局由 WidgetRenderManager.MasterUpdate 统一单点分频调度，杜绝多个独立 Update 帧散乱开销。
    // 3. 【MFP-SPEC-003: 语义主题管道 (Semantic Theming Pipeline)】:
    //    - 统一样式管道：禁止私自 new Material 或引用底层着色器，必须通过 WidgetStyleManager 语义接口。
    //    - 基类默认实现已全自动将新主题下发给所有注册的微控件 (Controls.ApplyThemeToControls)。
    // 4. 【MFP-SPEC-004: 遥测契约与空船守卫 (Telemetry Contract & Safe Guard)】:
    //    - 必须重写 OnUpdateTelemetry(IFlightTelemetry)。基类主入口 MasterUpdateTelemetry 已预先执行
    //      空值与空船安全拦截 (HasVessel Guard)，派生组件专注业务求值。
    // 5. 【MFP-SPEC-005: 安全生命周期 (Safe Lifecycle)】:
    //    - 基类已自动处理 I18n 解绑、RenderManager 注销与微控件回收。若显式 override OnDestroy，必须调用 base.OnDestroy()。
    // 6. 【MFP-SPEC-006: 零颜色字面量 (Zero Hardcoded Colors)】:
    //    - 严禁出现 new Color(...) / Color.red 等字面量（占位透明 Color.clear 除外）。
    //    - 颜色必须 100% 来自 WidgetStyleManager 语义接口（ApplyCard / ApplyText / ApplyButton / ApplyMeter / GetTextColor）。
    // 7. 【MFP-SPEC-007: 禁止直接场景查询 (No Scene Queries)】:
    //    - 严禁调用 FindObjectOfType、GameObject.Find、Camera.main，统一经由 FlightTelemetryContext 或 ProbeManager。
    // 8. 【MFP-SPEC-008: 声明式元数据注册 (Declarative Registration & Metadata)】:
    //    - 必须通过 [FlightWidget("type_id", ...)] 标注，自动打通 WidgetRegistry 反射装配与仪表库目录。
    //
    // 【现代化上级派发特性 (Modern Upstream Dispatch Features)】:
    // - BaseSize: 声明基准尺寸，基类自动在 BaseInitialize 完成物理 DPI 乘算赋值。
    // - AutoCreateCardFrame: 开启后基类自动生成高对比度航电卡片背景板与边框，0 样板代码。
    // - [WidgetControl]: 字段特性自省注册，自动纳管至 Controls 并自动响应主题切换与配置覆盖。
    // ====================================================================================================

    /// <summary>
    /// 标准航电组件范式模板 (Standard Flight Widget Reference Template)
    /// </summary>
    [FlightWidget("standard_template",
        Category = WidgetCategory.Gauges,
        DisplayName = "Standard Flight Widget Template",
        Description = "Standard reference template for flight instruments.")]
    public class StandardFlightWidgetTemplate : BaseFlightWidget
    {
        // ------------------------------------------------------------------------------------
        // [Part 1: 刷新层级契约与尺寸配置]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 声明刷新率阶梯 (Critical 60Hz | Standard 30Hz | Relaxed 10Hz | UltraLow 2Hz)
        /// 支持用户在设置界面开启垂直同步跟随游戏刷新率，或自由填写任意浮点数 (如 11.2Hz)
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        /// <summary>
        /// 声明 1.0x DPI 下的基础设计物理参考尺寸 (宽 160px, 高 100px)。
        /// 基类在 BaseInitialize 阶段会自动将其乘以 CurrentDpiScale 并赋予 RectTransform.sizeDelta。
        /// </summary>
        public override Vector2 BaseSize => new Vector2(160f, 100f);

        /// <summary>
        /// 开启上级自动卡片底板构建：基类将自动创建 CardBackground 与 CardOutline，
        /// 并在主题变更时自动调用 ApplyCard 保持风格一致。
        /// </summary>
        protected override bool AutoCreateCardFrame => true;

        // ------------------------------------------------------------------------------------
        // [Part 2: 声明式微控件对象声明 (Object-DSL Paradigm)]
        // ------------------------------------------------------------------------------------
        // 头部集中通过 new 声明高阶微控件对象，基类 BaseInitialize 在 OnInitialize 执行前
        // 全自动完成物理 DPI 乘算、UGUI 创建与 Controls 纳管，派生类 0 行创建样板代码！

        /// <summary>顶部左侧卡片标题</summary>
        public TextWidget HeaderTitle = new(TextStyleRole.Label, x: -36f, y: 38f, w: 72f, h: 18f, font: 10f, align: TextAnchor.MiddleLeft);

        /// <summary>顶部右侧状态徽标</summary>
        public TextWidget StatusBadge = new(TextStyleRole.SecondaryValue, x: 42f, y: 38f, w: 60f, h: 18f, font: 8f, align: TextAnchor.MiddleRight);

        /// <summary>核心主读数显示</summary>
        public TextWidget PrimaryValue = new(TextStyleRole.PrimaryValue, x: -24f, y: 8f, w: 96f, h: 32f, font: 22f, align: TextAnchor.MiddleLeft);

        /// <summary>工程单位角标</summary>
        public TextWidget UnitLabel = new(TextStyleRole.Unit, x: 42f, y: 0f, w: 40f, h: 18f, font: 10f, align: TextAnchor.LowerLeft);

        /// <summary>底部水平计量条 (含 Track 底槽与 Fill 填充条)</summary>
        public LinearBarWidget MeterBar = new(MeterStyleRole.Primary, x: 0f, y: -38f, w: 144f, h: 4f, isVertical: false);

        // 运行时遥测变动缓存 (防止高频 GC 分配与无意义的 Canvas 脏标记)
        private double _lastCachedValue = double.NaN;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // ------------------------------------------------------------------------------------
        // [Part 3: 组件视图排版装配 (OnInitialize)]
        // ------------------------------------------------------------------------------------
        // 注意：微控件已由基类全自动构建，OnInitialize 仅在需要设置动态文本或特异化布局时选填重写
        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            string titleStr = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName.ToUpperInvariant() : "TELEMETRY";
            HeaderTitle.Text = titleStr;
            StatusBadge.Text = GetBadgeText(CardStyleRole.Normal);
            UnitLabel.Text = config?.UnitLabel ?? "";
        }

        // ------------------------------------------------------------------------------------
        // [Part 4: 视觉主题与着色器动态应用 (ApplyTheme)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 当玩家切换视觉预设（如现代玻璃、阿波罗点阵、F-16全息冰蓝、琥珀CRT）时统一触发。
        /// 【严禁在本方法内出现任何颜色字面量！必须 100% 走 WidgetStyleManager】
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            // 1. 基类默认分发：将主题下发给所有注册的微控件
            base.ApplyTheme(theme);

            // 2. 根据当前卡片语义角色重新刷新底板背景与边框
            if (CardBackground != null && CardOutline != null)
            {
                ApplyCard(CardBackground, CardOutline, _currentCardRole, theme);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 5: 遥测数据求值与动态呈现 (OnUpdateTelemetry)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 由 WidgetRenderManager 在对应 RefreshTier 刷新时刻调用。
        /// 上级已在 MasterUpdateTelemetry 执行过 HasVessel 空船守卫。
        /// </summary>
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || Config == null) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. 通过通配符引擎求取数值与格式化文本 (严格面向契约，绝不直触 KSP 核心内部类)
            string token = !string.IsNullOrEmpty(Config.NumericToken) ? Config.NumericToken : "{SPD:SURF:F1}";
            double val = TelemetryTokenEngine.EvaluateNumeric(token, telemetry);

            if (double.IsNaN(val))
            {
                ShowUnavailable();
                return;
            }

            // 2. 脏标记检查：阈值来自 Config.ValueDeltaThreshold，仅在显著变化时才驱动 UI 重绘
            double delta = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.0;
            if (!double.IsNaN(_lastCachedValue) && Math.Abs(val - _lastCachedValue) <= delta)
            {
                return;
            }
            _lastCachedValue = val;

            // P1: 使用 DSL 属性自动进行脏检查与防重绘
            PrimaryValue.Text = TelemetryTokenEngine.Evaluate(token, telemetry);

            // 3. 驱动计量条归一化填充 (量程来自 Config，几何永远钳制在 0~1 以免溢出卡片)
            float fraction = NormalizeToRange(val);
            MeterBar.SetFillAmount(fraction, MeterBar.MeterRole);

            // 4. 限幅模式 + 阈值告警状态机 (Normal -> Caution/Warning -> Danger)
            CardStyleRole targetRole = ResolveCardRole(val);

            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                if (CardBackground != null && CardOutline != null)
                {
                    ApplyCard(CardBackground, CardOutline, targetRole, theme);
                }
                PrimaryValue.SetRole(GetValueTextRole(targetRole));
                StatusBadge.SetRole(GetValueTextRole(targetRole));
                StatusBadge.Text = GetBadgeText(targetRole);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 5b: 量程 / 限幅 / 徽标 语义解析 (全部读 WidgetConfig，禁止写死)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 归一化到 0~1 量程。Config.MinValue/MaxValue 决定刻度，几何永远钳制在 [0,1]。
        /// </summary>
        private float NormalizeToRange(double val)
        {
            double range = Config.MaxValue - Config.MinValue;
            if (range <= 0.0001) return 0f;
            return Mathf.Clamp01((float)((val - Config.MinValue) / range));
        }

        /// <summary>
        /// 解析有效限幅模式：旧字段 IsSoftLimit=true 等同于 LimitMode="soft"
        /// hard = 阈值线越界判 Danger (硬上限告警)；soft = 越过 MaxValue 仅判 Warning (爆表软提示)；none = 纯读数不着色
        /// </summary>
        private string EffectiveLimitMode()
        {
            if (Config.IsSoftLimit) return "soft";
            return string.IsNullOrEmpty(Config.LimitMode) ? "hard" : Config.LimitMode.ToLowerInvariant();
        }

        private CardStyleRole ResolveCardRole(double val)
        {
            string mode = EffectiveLimitMode();
            if (mode == "none") return CardStyleRole.Normal;

            bool hasRange = Config.MaxValue > Config.MinValue;
            bool overMax = hasRange && val > Config.MaxValue;

            if (mode == "soft")
            {
                return overMax ? CardStyleRole.Warning : CardStyleRole.Normal;
            }

            // hard: 告警线优先，其次越界硬告警
            if (Config.WarningThreshold > 0 && val >= Config.WarningThreshold) return CardStyleRole.Danger;
            if (Config.CautionThreshold > 0 && val >= Config.CautionThreshold) return CardStyleRole.Warning;
            if (overMax) return CardStyleRole.Danger;
            return CardStyleRole.Normal;
        }

        private static TextStyleRole GetValueTextRole(CardStyleRole cardRole)
        {
            switch (cardRole)
            {
                case CardStyleRole.Danger: return TextStyleRole.Danger;
                case CardStyleRole.Warning: return TextStyleRole.Warning;
                default: return TextStyleRole.PrimaryValue;
            }
        }

        /// <summary>状态徽标文案来自 Config.BadgeXxx（可逐组件定制语言与机型术语）</summary>
        private string GetBadgeText(CardStyleRole cardRole)
        {
            switch (cardRole)
            {
                case CardStyleRole.Danger:
                    return !string.IsNullOrEmpty(Config?.BadgeWarning) ? Config.BadgeWarning : "WARN";
                case CardStyleRole.Warning:
                    return !string.IsNullOrEmpty(Config?.BadgeCaution) ? Config.BadgeCaution : "CAUT";
                default:
                    return !string.IsNullOrEmpty(Config?.BadgeNormal) ? Config.BadgeNormal : "NORM";
            }
        }

        /// <summary>无有效遥测时的统一降级显示 (无数据、不参与量程与告警着色)</summary>
        private void ShowUnavailable()
        {
            if (PrimaryValue.Text == "---") return;

            _lastCachedValue = double.NaN;
            PrimaryValue.Text = "---";
            MeterBar.SetFillAmount(0f, MeterBar.MeterRole);

            if (_currentCardRole != CardStyleRole.Normal)
            {
                ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
                _currentCardRole = CardStyleRole.Normal;
                if (CardBackground != null && CardOutline != null)
                {
                    ApplyCard(CardBackground, CardOutline, CardStyleRole.Normal, theme);
                }
                PrimaryValue.SetRole(TextStyleRole.PrimaryValue);
                StatusBadge.SetRole(TextStyleRole.SecondaryValue);
                StatusBadge.Text = GetBadgeText(CardStyleRole.Normal);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 6: 安全注销与生命周期清理 (OnDestroy)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 销毁清理：必须调用 base.OnDestroy() 以确保 Controls 微控件池、I18n 监听器与 RenderManager 正确解绑
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
