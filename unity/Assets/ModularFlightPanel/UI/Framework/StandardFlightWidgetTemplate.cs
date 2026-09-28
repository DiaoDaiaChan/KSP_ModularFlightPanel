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
    // 【现代声明式 UI 对象范式 (Object-DSL)】在类头部集中声明高阶微控件对象，直接继承 BaseAvionicsWidget 大父类
    // ====================================================================================================
    //
    // 【规范概述 (Standard Overview)】:
    // 本文件是 Modular Flight Panel 整个航电系统的【活的代码规范与标准示范标杆】。
    // 所有官方原生组件与第三方扩展组件【推荐优先继承 BaseAvionicsWidget 大父类】，
    // 享受标准物理尺寸、自动卡片底板、刷新阶梯调度与默认遥测驱动，零样板代码快速构建！
    // 本文件自身在无头审计门禁（MFP-SPEC-001 ~ MFP-SPEC-008）中保持 0 错误、0 警告、0 颜色字面量。
    //
    // 【八大核心铁律 (Eight Non-Negotiable Rules)】:
    // 1. 【MFP-SPEC-001: 统一继承契约】:
    //    - 继承 BaseAvionicsWidget（或底层 BaseFlightWidget），接入 Sub-Canvas 独立隔离、自由拖拽、缩放、层级治理。
    // 2. 【MFP-SPEC-002: 阶梯刷新契约】:
    //    - 声明 RefreshTier（姿态/导引 Critical 60Hz 满帧直通；表盘/滚带 Standard 30Hz；电力/控制 Relaxed 10Hz）。
    //    - 亦支持通过 override float CustomHz => 20f 自定义任意精准频率，优先级高于阶梯。
    // 3. 【MFP-SPEC-003: 语义主题管道】:
    //    - 统一样式管道：禁止私自 new Material 或引用底层着色器，必须通过 WidgetStyleManager 语义接口。
    //    - 基类默认实现已全自动将新主题下发给所有注册的微控件 (Controls.ApplyThemeToControls)。
    // 4. 【MFP-SPEC-004: 遥测契约与空船守卫】:
    //    - 基类主入口 MasterUpdateTelemetry 已预先执行空值与空船安全拦截 (HasVessel Guard)。
    // 5. 【MFP-SPEC-005: 安全生命周期】:
    //    - 基类已自动处理 I18n 解绑、RenderManager 注销与微控件回收。若显式 override OnDestroy，必须调用 base.OnDestroy()。
    // 6. 【MFP-SPEC-006: 零颜色字面量】:
    //    - 严禁出现 new Color(...) / Color.red 等字面量（占位透明 Color.clear 除外）。
    //    - 颜色必须 100% 来自 WidgetStyleManager 语义接口或 ThemeConfig。
    // 7. 【MFP-SPEC-007: 禁止直接场景查询】:
    //    - 严禁调用 FindObjectOfType、GameObject.Find、Camera.main，统一经由 FlightTelemetryContext 或 ProbeManager。
    // 8. 【MFP-SPEC-008: 声明式元数据注册】:
    //    - 必须通过 [FlightWidget("type_id", ...)] 标注，自动打通 WidgetRegistry 反射装配与仪表库目录。
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
        /// 声明刷新率阶梯 (Critical 随游戏FPS | Standard 60Hz | Slow 30Hz | Relaxed 10Hz | UltraLow 2Hz | Custom 自定义)
        /// BaseFlightWidget 默认即为 Standard 60Hz，此处显式声明示范
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        /// <summary>
        /// 声明 1.0x DPI 下的基础设计物理参考尺寸 (宽 160px, 高 100px)。
        /// 基类在 BaseInitialize 阶段会自动将其乘以 CurrentDpiScale 并赋予 RectTransform.sizeDelta。
        /// </summary>
        public override Vector2 BaseSize => new Vector2(160f, 100f);

        /// <summary>
        /// 开启基类全自动卡片底板与微光边框
        /// </summary>
        protected override bool AutoCreateCardFrame => true;

        // ------------------------------------------------------------------------------------
        // [Part 2: 声明式微控件对象声明 (Object-DSL 语义泊靠范式)]
        // ------------------------------------------------------------------------------------
        // 头部集中通过语义工厂方法 (Title / Badge / Value / Unit / BottomBar) 声明微控件，
        // 基类自动完成泊靠布局、物理 DPI 乘算、UGUI 创建与 Controls 纳管，0 绝对坐标计算！

        /// <summary>顶部左侧卡片标题 (自动泊靠 TopLeft)</summary>
        public TextWidget HeaderTitle = TextWidget.Title(I18n.Tr("WIDGET_FW_TELEMETRY", "遥测"));

        /// <summary>顶部右侧状态徽标 (自动泊靠 TopRight)</summary>
        public TextWidget StatusBadge = TextWidget.Badge("NORM");

        /// <summary>核心主读数显示 (自动居中泊靠 Center)</summary>
        public TextWidget PrimaryValue = TextWidget.Value("---");

        /// <summary>工程单位角标 (自动泊靠 BottomRight)</summary>
        public TextWidget UnitLabel = TextWidget.Unit("m/s");

        /// <summary>底部水平计量条 (自动通栏泊靠 BottomBar，含 Track 槽与 Fill 条)</summary>
        public LinearBarWidget MeterBar = LinearBarWidget.BottomBar(MeterStyleRole.Primary, height: 4f);

        // 运行时遥测变动缓存 (防止高频 GC 分配与无意义的 Canvas 脏标记)
        private double _lastCachedValue = double.NaN;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 数据心跳与 UI 绘制双轨解耦缓存
        private bool _dataHasValue = false;
        private double _dataVal = double.NaN;
        private string _dataText = "---";
        private float _dataFraction = 0f;
        private CardStyleRole _dataTargetRole = CardStyleRole.Normal;

        // ------------------------------------------------------------------------------------
        // [Part 3: 组件视图排版装配 (OnInitialize)]
        // ------------------------------------------------------------------------------------
        // 微控件已由基类全自动构建，OnInitialize 仅在需要设置动态文本或特异化布局时选填重写
        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            string titleStr = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName.ToUpperInvariant() : I18n.Tr("WIDGET_FW_TELEMETRY", "遥测");
            HeaderTitle.Text = titleStr;
            StatusBadge.Text = GetBadgeText(CardStyleRole.Normal);
            UnitLabel.Text = config?.UnitLabel ?? "";
        }

        // ------------------------------------------------------------------------------------
        // [Part 4: 视觉主题与着色器动态应用 (ApplyTheme)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 当玩家切换视觉预设时统一触发。
        /// 基类默认实现已全自动将主题广播给所有微控件；若需对卡片底板应用特殊告警色则重写拓展。
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            if (CardBackground != null && CardOutline != null)
            {
                ApplyCard(CardBackground, CardOutline, _currentCardRole, theme);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 5: 数据心跳与 UI 绘制双轨生命周期 (OnDataHeartBeat & OnUIDrawLoop)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 【核心数据心跳】按 EffectiveHeartBeatTier 节律调用，专用于执行物理推算与遥测解算 (0 UI 绘制)。
        /// </summary>
        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || Config == null)
            {
                _dataHasValue = false;
                _dataVal = double.NaN;
                _dataText = "---";
                _dataFraction = 0f;
                _dataTargetRole = CardStyleRole.Normal;
                return;
            }

            // 1. 通过父类内置 EvalNumeric 求取数值 (严格面向契约，绝不直触 KSP 核心内部类)
            string token = !string.IsNullOrEmpty(Config.NumericToken) ? Config.NumericToken : "{SPD:SURF:F1}";
            double val = EvalNumeric(token, telemetry, double.NaN);

            if (double.IsNaN(val))
            {
                _dataHasValue = false;
                _dataVal = double.NaN;
                _dataText = "---";
                _dataFraction = 0f;
                _dataTargetRole = CardStyleRole.Normal;
                return;
            }

            _dataHasValue = true;
            _dataVal = val;
            _dataText = EvalToken(token, telemetry, "---");
            _dataFraction = NormalizeValue(val, Config.MinValue, Config.MaxValue);
            _dataTargetRole = ResolveCardRole(val);
        }

        /// <summary>
        /// 【核心 UI 绘制循环】按 RefreshTier 满帧驱动，专用于 UGUI 读数更新、2D UI 着色器材质管线与视觉渲染 (0 遥测物理采样)。
        /// </summary>
        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (!_dataHasValue)
            {
                ShowUnavailable();
                return;
            }

            // 脏标记检查：阈值来自 Config.ValueDeltaThreshold，仅在显著变化时才驱动 UI 重绘
            double delta = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.0;
            if (!double.IsNaN(_lastCachedValue) && Math.Abs(_dataVal - _lastCachedValue) <= delta)
            {
                return;
            }
            _lastCachedValue = _dataVal;

            // 更新微控件读数与填充
            PrimaryValue.Text = _dataText;
            MeterBar.SetFillAmount(_dataFraction, MeterBar.MeterRole);

            // 限幅模式 + 阈值告警状态机 (Normal -> Caution/Warning -> Danger)
            if (_currentCardRole != _dataTargetRole)
            {
                _currentCardRole = _dataTargetRole;
                ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance.CurrentTheme;
                if (CardBackground != null && CardOutline != null)
                {
                    ApplyCard(CardBackground, CardOutline, _dataTargetRole, theme);
                }
                PrimaryValue.SetRole(GetValueTextRole(_dataTargetRole));
                StatusBadge.SetRole(GetValueTextRole(_dataTargetRole));
                StatusBadge.Text = GetBadgeText(_dataTargetRole);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 5b: 量程 / 限幅 / 徽标 语义解析 (全部读 WidgetConfig，禁止写死)]
        // ------------------------------------------------------------------------------------

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

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
