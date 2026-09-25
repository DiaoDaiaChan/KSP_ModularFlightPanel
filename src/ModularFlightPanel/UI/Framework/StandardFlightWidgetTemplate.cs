using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 标准航电组件范式模板 (Standard Flight Widget Template)
    /// ====================================================================================
    /// 
    /// 所有官方原生组件与自定义派生组件【必须 100% 遵照本模板结构与规范书写】，禁止任何天马行空的各行其是！
    /// 本文件是规范审计（MFP-SPEC-001..008）的标杆：它自身必须是 0 违规（含 0 颜色字面量）。
    /// 
    /// 【七大核心铁律 (Non-Negotiable Rules)】:
    /// 1. 【严禁任何硬编码 (No Hardcoding)】:
    ///    - 严禁出现 new Color(...) / Color.xxx 字面量（占位色同样禁止，唯一例外是容器透明用的 Color.clear）。
    ///      所有颜色必须来自 WidgetStyleManager 的语义接口（ApplyCard / ApplyText / ApplyButton / ApplyMeter）
    ///      或 WidgetStyleManager.GetXxxColor(role, theme)。
    ///    - 严禁写死固定尺寸，所有位置、外边距、字号均需乘以 CurrentDpiScale。
    ///    - 严禁写死视图策略：脏标记阈值、徽标文案、量程与限幅模式一律读 WidgetConfig。
    /// 2. 【着色器统一样式管道 (Unified Style & Shader Pipeline)】:
    ///    - 禁止组件私自创建 Material 或引用底层 Shader！必须经由语义接口，
    ///      确保当前主题选择的点阵、CRT、全息或玻璃着色器 100% 作用于本组件。
    /// 3. 【生命周期与分频刷新管控 (Render & Lifecycle Management)】:
    ///    - 必须显式声明 RefreshTier（姿态/航向标 Critical 60Hz；表盘/滚带/导航 Standard 30Hz；电力/维生/ΔV Relaxed 10Hz）。
    ///    - 当已被 WidgetRenderManager 接管时，由 MasterUpdate 按步长统一驱动，杜绝每帧混乱计算。
    /// 4. 【按需重绘与脏标记保护 (Dirty Protection & Change Detection)】:
    ///    - 在 OnUpdateTelemetry 中按 Config.ValueDeltaThreshold 做数值变动对比，未变化时严禁重复向 Text.text 赋值
    ///      或调用 SetActive，彻底根除 UGUI 顶点缓冲区全量重构。
    /// 5. 【量程与限幅契约 (Range & Limit Contract)】:
    ///    - 量程来自 Config.MinValue/MaxValue，告警线来自 Config.CautionThreshold/WarningThreshold，
    ///      限幅语义来自 Config.LimitMode（hard=越界硬告警 / soft=爆表软提示 / none=纯读数），兼容旧字段 IsSoftLimit。
    /// 6. 【统一缓存中枢与零 GC 契约 (Unified Caching & Zero-GC Contract)】:
    ///    - 高频遥测数字与标签优先使用 FastFormat / FastIntString / FastPercentString / FastDegreeString，
    ///      配合 SetTextIfChanged 阻断 70%~85% 的无意义 UGUI 顶点重绘与垃圾回收微卡顿。
    /// 7. 【声明式全自动装配契约 (Declarative Auto-Registration & Metadata Contract, MFP-SPEC-008)】:
    ///    - 必须通过 [FlightWidget("type_name", Category = WidgetCategory.Xxx, ...)] 进行声明式元数据标注，
    ///      自动打通 WidgetRegistry 反射装配中枢与 TabLibrary 仪表库动态目录，彻底杜绝手工修改工厂分支。
    /// </summary>
    [FlightWidget("standard_template",
        Category = WidgetCategory.Gauges,
        DisplayName = "Standard Flight Widget Template",
        Description = "Standard reference template for flight instruments.")]
    public class StandardFlightWidgetTemplate : BaseFlightWidget
    {
        // ------------------------------------------------------------------------------------
        // [Part 1: 刷新层级契约与字段声明]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 声明刷新率阶梯 (Critical 60Hz | Standard 30Hz | Relaxed 10Hz | UltraLow 2Hz)
        /// 支持用户在设置界面开启垂直同步跟随游戏刷新率，或自由填写任意浮点数 (如 11.2Hz)
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // UI 视图节点引用 (统一按层次分组：背景框体、顶部标题、核心数值、次级辅助与状态标签)
        private Image _bgImage;
        private Outline _bgOutline;

        private Text _headerTitleText;
        private Text _statusBadgeText;

        private Text _primaryValueText;
        private Text _unitText;

        private Image _meterTrack;
        private Image _meterFill;

        // 运行时遥测变动缓存 (防止高频 GC 分配与无意义的 Canvas 脏标记)
        private double _lastCachedValue = double.NaN;
        private string _lastFormattedText = string.Empty;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // ------------------------------------------------------------------------------------
        // [Part 2: 组件视图构建与装配 (OnInitialize)]
        // ------------------------------------------------------------------------------------

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 计算组件几何包围盒 (基准逻辑像素 x DPI 缩放)
            Vector2 cardSize = new Vector2(160f * s, 100f * s);
            RectTransform.sizeDelta = cardSize;

            // 2. 装配标准卡片底板与边框 (统一挂载 Sub-Canvas 隔离顶点)
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;   // 占位透明，真实底色由 ApplyCard 按语义角色注入
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 构建标准顶部标题栏 (Header)
            string titleStr = !string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName.ToUpperInvariant() : "TELEMETRY";
            _headerTitleText = UIFactory.CreateText(transform, "Header_Title", titleStr, Mathf.RoundToInt(10f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform titleRt = _headerTitleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.sizeDelta = new Vector2(-16f * s, 18f * s);
            titleRt.anchoredPosition = new Vector2(8f * s, -6f * s);

            // 状态徽标 (右上角，文案来自 Config.BadgeXxx，例如 NOMINAL / ALERT)
            _statusBadgeText = UIFactory.CreateText(transform, "Status_Badge", GetBadgeText(CardStyleRole.Normal), Mathf.RoundToInt(8f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform badgeRt = _statusBadgeText.rectTransform;
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(1f, 1f);
            badgeRt.sizeDelta = new Vector2(60f * s, 18f * s);
            badgeRt.anchoredPosition = new Vector2(-8f * s, -6f * s);

            // 4. 构建核心主读数与单位
            _primaryValueText = UIFactory.CreateText(transform, "Primary_Value", "---", Mathf.RoundToInt(22f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valRt = _primaryValueText.rectTransform;
            valRt.anchorMin = new Vector2(0f, 0.4f);
            valRt.anchorMax = new Vector2(0.7f, 0.85f);
            valRt.anchoredPosition = new Vector2(8f * s, 0f);

            _unitText = UIFactory.CreateText(transform, "Unit_Label", config?.UnitLabel ?? "", Mathf.RoundToInt(10f * s), TextAnchor.LowerLeft,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform unitRt = _unitText.rectTransform;
            unitRt.anchorMin = new Vector2(0.72f, 0.45f);
            unitRt.anchorMax = new Vector2(1f, 0.75f);
            unitRt.anchoredPosition = Vector2.zero;

            // 5. 构建底部水平计量槽 (Meter)
            GameObject trackGo = UIFactory.CreatePanel(transform, "Meter_Track",
                new Vector2(cardSize.x - 16f * s, 4f * s), new Vector2(0f, -cardSize.y * 0.5f + 12f * s),
                style.GetMeterColor(MeterStyleRole.Track, theme));
            _meterTrack = trackGo.GetComponent<Image>();

            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Meter_Fill", new Vector2(0f, 4f * s), Vector2.zero,
                style.GetMeterColor(MeterStyleRole.Primary, theme));
            _meterFill = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------------------------
        // [Part 3: 视觉主题与着色器动态应用 (ApplyTheme)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 当玩家切换视觉预设（如现代玻璃、阿波罗点阵、F-16全息冰蓝、琥珀CRT）时统一触发
        /// 【严禁在本方法内出现任何颜色字面量！必须 100% 走 WidgetStyleManager】
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            // 1. 卡片外框应用主题与专属 Shader (点阵/全息/CRT/玻璃)
            ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);

            // 2. 文字排版应用专属 Shader 与语义色彩
            ApplyText(_headerTitleText, TextStyleRole.Label, theme);
            ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_primaryValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);

            // 3. 计量槽应用样式
            ApplyMeter(_meterTrack, _meterFill, null, MeterStyleRole.Primary, theme);
        }

        // ------------------------------------------------------------------------------------
        // [Part 4: 遥测数据求值与动态呈现 (OnUpdateTelemetry)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 由 WidgetRenderManager 在对应 RefreshTier 刷新时刻调用
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

            // P1: 使用 SetTextIfChanged 阻断相同字符串引发的 UGUI 顶点重建
            // 若自定义数值格式化，推荐使用 FastFormat("pri_val", val, "F1", 0.05) 进行死区量化
            string newStr = TelemetryTokenEngine.Evaluate(token, telemetry);
            SetTextIfChanged(_primaryValueText, newStr);

            // 3. 驱动计量条归一化填充 (量程来自 Config，几何永远钳制在 0~1 以免溢出卡片)
            float fraction = NormalizeToRange(val);
            float maxWidth = _meterTrack.rectTransform.sizeDelta.x;
            _meterFill.rectTransform.sizeDelta = new Vector2(maxWidth * fraction, _meterFill.rectTransform.sizeDelta.y);

            // 4. 限幅模式 + 阈值告警状态机 (Normal -> Caution/Warning -> Danger)
            CardStyleRole targetRole = ResolveCardRole(val);

            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                ApplyCard(_bgImage, _bgOutline, targetRole, theme);
                ApplyText(_primaryValueText, GetValueTextRole(targetRole), theme);
                ApplyText(_statusBadgeText, GetValueTextRole(targetRole), theme);
                SetTextIfChanged(_statusBadgeText, GetBadgeText(targetRole));
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 4b: 量程 / 限幅 / 徽标 语义解析 (全部读 WidgetConfig，禁止写死)]
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
            if (_lastFormattedText == "---") return;

            _lastCachedValue = double.NaN;
            _lastFormattedText = "---";
            _primaryValueText.text = "---";
            _meterFill.rectTransform.sizeDelta = new Vector2(0f, _meterFill.rectTransform.sizeDelta.y);

            if (_currentCardRole != CardStyleRole.Normal)
            {
                ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
                _currentCardRole = CardStyleRole.Normal;
                ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
                ApplyText(_primaryValueText, TextStyleRole.PrimaryValue, theme);
                ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
                _statusBadgeText.text = GetBadgeText(CardStyleRole.Normal);
            }
        }

        // ------------------------------------------------------------------------------------
        // [Part 5: 销毁与注销 (OnDestroy)]
        // ------------------------------------------------------------------------------------

        protected override void OnDestroy()
        {
            // 必须调用基类注销方法，通知 WidgetRenderManager 安全移除注册项
            base.OnDestroy();
        }
    }
}
