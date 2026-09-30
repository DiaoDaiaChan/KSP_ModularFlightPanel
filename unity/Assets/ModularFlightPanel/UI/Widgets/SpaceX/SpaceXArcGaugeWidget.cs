using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 环形遥测仪表纯逻辑状态快照 (0-GC 纯值类型，SPEC-012)
    /// </summary>
    public struct SpaceXArcGaugeState : IEquatable<SpaceXArcGaugeState>
    {
        public bool HasValue;
        public double Value;
        public float Fraction;
        public string FormattedText;
        public CardStyleRole TargetCardRole;
        public TextStyleRole ValueTextRole;
        public MeterStyleRole MeterRole;

        public bool Equals(SpaceXArcGaugeState other)
        {
            return HasValue == other.HasValue &&
                   Math.Abs(Value - other.Value) < 0.001 &&
                   Math.Abs(Fraction - other.Fraction) < 0.001f &&
                   FormattedText == other.FormattedText &&
                   TargetCardRole == other.TargetCardRole &&
                   ValueTextRole == other.ValueTextRole &&
                   MeterRole == other.MeterRole;
        }
    }

    /// <summary>
    /// SpaceX 环形仪表纯业务大脑 (100% 游戏与引擎解耦，SPEC-012)
    /// </summary>
    public class SpaceXArcGaugeLogic : WidgetLogic<SpaceXArcGaugeState>
    {
        public string TokenKey { get; set; }
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;
        public double WarningThreshold { get; set; } = 0.0;
        public double CautionThreshold { get; set; } = 0.0;
        public string LimitMode { get; set; }

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            string token = !string.IsNullOrEmpty(TokenKey) ? TokenKey : "{SPD:SURF:KMH}";
            double val = TelemetryTokenEngine.EvaluateNumeric(token, telemetry);

            if (double.IsNaN(val))
            {
                CurrentState = default;
                return;
            }

            string formattedText = TelemetryTokenEngine.Evaluate(token, telemetry);

            double range = MaxValue - MinValue;
            float frac = range > 0.0001 ? Math.Max(0f, Math.Min(1f, (float)((val - MinValue) / range))) : 0f;

            string mode = LimitMode?.ToLowerInvariant();
            CardStyleRole role = CardStyleRole.Normal;
            bool hasRange = MaxValue > MinValue;
            bool overMax = hasRange && val > MaxValue;

            if (mode == "soft")
            {
                role = overMax ? CardStyleRole.Warning : CardStyleRole.Normal;
            }
            else if (mode != "none")
            {
                if (WarningThreshold > 0 && val >= WarningThreshold) role = CardStyleRole.Danger;
                else if (CautionThreshold > 0 && val >= CautionThreshold) role = CardStyleRole.Warning;
                else if (overMax) role = CardStyleRole.Danger;
            }

            TextStyleRole textRole;
            MeterStyleRole meterRole;
            switch (role)
            {
                case CardStyleRole.Danger:
                    textRole = TextStyleRole.Danger;
                    meterRole = MeterStyleRole.Danger;
                    break;
                case CardStyleRole.Warning:
                    textRole = TextStyleRole.Warning;
                    meterRole = MeterStyleRole.Warning;
                    break;
                default:
                    textRole = TextStyleRole.PrimaryValue;
                    meterRole = MeterStyleRole.Primary;
                    break;
            }

            CurrentState = new SpaceXArcGaugeState
            {
                HasValue = true,
                Value = val,
                Fraction = frac,
                FormattedText = formattedText,
                TargetCardRole = role,
                ValueTextRole = textRole,
                MeterRole = meterRole
            };
        }
    }

    /// <summary>
    /// SpaceX 星舰发射广播风格马蹄形弧线表盘 (SpaceX Webcast Arc Gauge)
    /// 遵循 MFP-SPEC-012 (架构分层与 WidgetLogic 解耦)、MFP-SPEC-009 (托管缓存与脏检查)、
    /// MFP-SPEC-002 (纯 GPU 矢量网格，零 CPU 软件光栅化) 规范。
    /// </summary>
    [FlightWidget("spacex_arc", "spacex_gauge", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 环形遥测仪表", Description = "SpaceX 龙飞船高精度同心圆弧表盘，带发光步进游标与动态数字标定。", DefaultWidgetId = "spacex.speed", DefaultX = -240f, DefaultY = 0f, ExactIds = new[] { "spacex.speed", "spacex.altitude" })]
    public class SpaceXArcGaugeWidget : BaseFlightWidget
    {
        private readonly SpaceXArcGaugeLogic _logic = new SpaceXArcGaugeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        public override Vector2 BaseSize => new Vector2(110f, 110f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        private ProceduralArcImage _arcTrackImage;
        private ProceduralArcImage _arcFillImage;

        private Text _topLabelText;
        private Text _primaryValueText;
        private Text _unitLabelText;

        // 遥测缓存与脏标记 (SPEC-009)
        private readonly CachedDouble _lastCachedValue = new CachedDouble(double.NaN);
        private readonly Cached<string> _lastFormattedText = new Cached<string>(string.Empty);
        private readonly CachedFloat _lastFillAmount = new CachedFloat(-1f, 0.002f);
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // CustomTemplate 自定义通道
        private string _tokenKey;
        private string _titleTemplate;
        private string _unitTemplate;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _tokenKey = GetTemplateChannel("TOKEN", null);
            _titleTemplate = GetTemplateChannel("TITLE", null);
            _unitTemplate = GetTemplateChannel("UNIT", null);

            // 配置业务逻辑大脑
            _logic.TokenKey = !string.IsNullOrEmpty(_tokenKey) ? _tokenKey : config?.NumericToken;
            if (config != null)
            {
                _logic.MinValue = config.MinValue;
                _logic.MaxValue = config.MaxValue > config.MinValue ? config.MaxValue : 100.0;
                _logic.WarningThreshold = config.WarningThreshold;
                _logic.CautionThreshold = config.CautionThreshold;
                _logic.LimitMode = config.LimitMode;
            }

            // 1. 组件包围盒 (基准 110x110 逻辑像素)
            Vector2 size = new Vector2(110f * s, 110f * s);
            RectTransform.sizeDelta = size;

            // 2. 外部半透卡片底衬 (轻质深色玻璃，符合 SpaceX 航电风格)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 构建马蹄弧形轨道 (Track) 与填充条 (Fill) (纯 GPU 矢量网格，SPEC-002)
            float arcDiameter = 98f * s;

            // 底槽轨道
            _arcTrackImage = CreateChild<ProceduralArcImage>("Arc_Track", transform, new Vector2(arcDiameter, arcDiameter), new Vector2(0f, 2f * s));
            _arcTrackImage.InnerRadiusRatio = 0.84f;
            _arcTrackImage.OuterRadiusRatio = 0.94f;
            _arcTrackImage.StartAngle = 225f;
            _arcTrackImage.SweepAngle = 270f;
            _arcTrackImage.fillAmount = 1.0f;
            _arcTrackImage.raycastTarget = false;

            // 动态进度填充弧
            _arcFillImage = CreateChild<ProceduralArcImage>("Arc_Fill", transform, new Vector2(arcDiameter, arcDiameter), new Vector2(0f, 2f * s));
            _arcFillImage.InnerRadiusRatio = 0.84f;
            _arcFillImage.OuterRadiusRatio = 0.94f;
            _arcFillImage.StartAngle = 225f;
            _arcFillImage.SweepAngle = 270f;
            _arcFillImage.fillAmount = 0f;
            _arcFillImage.raycastTarget = false;

            // 4. 顶部标题："SPEED" / "ALTITUDE"
            string titleStr = !string.IsNullOrEmpty(_titleTemplate)
                ? _titleTemplate
                : (!string.IsNullOrEmpty(config?.DisplayName) ? config.DisplayName.ToUpperInvariant() : "TELEMETRY");
            _topLabelText = UIFactory.CreateText(transform, "Top_Label", titleStr, Mathf.RoundToInt(8.5f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            _topLabelText.fontStyle = FontStyle.Bold;
            RectTransform topRt = _topLabelText.rectTransform;
            topRt.sizeDelta = new Vector2(90f * s, 16f * s);
            topRt.anchoredPosition = new Vector2(0f, 28f * s);

            // 5. 中央主读数：如 "18497" / "157" (SpaceX 超大号现代无衬线粗体)
            _primaryValueText = UIFactory.CreateText(transform, "Primary_Value", "---", Mathf.RoundToInt(22f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _primaryValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = _primaryValueText.rectTransform;
            valRt.sizeDelta = new Vector2(96f * s, 32f * s);
            valRt.anchoredPosition = new Vector2(0f, 4f * s);

            // 6. 底部单位标签："KM/H" / "KM"
            string unitStr = !string.IsNullOrEmpty(_unitTemplate)
                ? _unitTemplate
                : (!string.IsNullOrEmpty(config?.UnitLabel) ? config.UnitLabel.ToUpperInvariant() : "");
            _unitLabelText = UIFactory.CreateText(transform, "Unit_Label", unitStr, Mathf.RoundToInt(8f * s), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.Unit, theme));
            _unitLabelText.fontStyle = FontStyle.Bold;
            RectTransform unitRt = _unitLabelText.rectTransform;
            unitRt.sizeDelta = new Vector2(80f * s, 14f * s);
            unitRt.anchoredPosition = new Vector2(0f, -24f * s);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Card Background", _bgImage.gameObject, "圆弧表盘底衬", t => ApplyCard(_bgImage, _bgOutline, _currentCardRole, t)));
            this.Controls.Register(new WidgetArcMeterControl(_arcFillImage, _arcTrackImage, MeterStyleRole.Primary, "Arc Meter", "马蹄弧形量规轨道与指示弧"));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "top_label", "Top Label", _topLabelText != null ? _topLabelText.gameObject : null));
            this.Controls.Register(new WidgetReadoutControl("primary_value", "中央超大号读数", _primaryValueText != null ? _primaryValueText.gameObject : null, _primaryValueText, null, TextStyleRole.PrimaryValue, (!string.IsNullOrEmpty(_tokenKey) ? _tokenKey : (Config != null && !string.IsNullOrEmpty(Config.NumericToken) ? Config.NumericToken : "{SPD:SURF:KMH}"))));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "unit_label", "Unit Label", _unitLabelText != null ? _unitLabelText.gameObject : null));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            // 卡片三态样式
            ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);

            // 文字排版
            ApplyText(_topLabelText, TextStyleRole.Label, theme);
            ApplyText(_primaryValueText, GetValueTextRole(_currentCardRole), theme);
            ApplyText(_unitLabelText, TextStyleRole.Unit, theme);

            // 马蹄弧形轨道与填充着色
            if (_arcTrackImage != null)
            {
                _arcTrackImage.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }
            if (_arcFillImage != null)
            {
                _arcFillImage.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            SpaceXArcGaugeState state = _logic.CurrentState;
            if (!state.HasValue)
            {
                ShowUnavailable();
                return;
            }

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 脏标记检查：数值或文本未变时不触发 UGUI 重绘
            double delta = (Config != null && Config.ValueDeltaThreshold > 0.0) ? Config.ValueDeltaThreshold : 0.05;
            if (!double.IsNaN(_lastCachedValue.Value) && Math.Abs(state.Value - _lastCachedValue.Value) <= delta)
            {
                return;
            }
            _lastCachedValue.Update(state.Value);

            // 2. 更新数字文本
            if (_lastFormattedText.Update(state.FormattedText))
            {
                _primaryValueText.SetTextSafe(state.FormattedText);
            }

            // 3. 计算圆弧填充百分比
            if (_arcFillImage != null && _lastFillAmount.Update(state.Fraction))
            {
                _arcFillImage.fillAmount = state.Fraction;
            }

            // 4. 状态机告警着色
            if (_currentCardRole != state.TargetCardRole)
            {
                _currentCardRole = state.TargetCardRole;
                ApplyCard(_bgImage, _bgOutline, state.TargetCardRole, theme);
                ApplyText(_primaryValueText, state.ValueTextRole, theme);
                if (_arcFillImage != null)
                {
                    _arcFillImage.color = style.GetMeterColor(state.MeterRole, theme);
                }
            }
        }

        private static TextStyleRole GetValueTextRole(CardStyleRole role)
        {
            switch (role)
            {
                case CardStyleRole.Danger: return TextStyleRole.Danger;
                case CardStyleRole.Warning: return TextStyleRole.Warning;
                default: return TextStyleRole.PrimaryValue;
            }
        }

        private void ShowUnavailable()
        {
            if (_lastFormattedText.Value == "---") return;
            _lastCachedValue.Reset(double.NaN);
            _lastFormattedText.Update("---");
            _primaryValueText.SetTextSafe("---");
            if (_arcFillImage != null) _arcFillImage.fillAmount = 0f;
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// GPU 程序化马蹄弧形图元 (零 CPU 软件光栅化，纯代码 GPU 弧线几何网格，SPEC-002)
    /// </summary>
    public class ProceduralArcImage : Image
    {
        public float InnerRadiusRatio = 0.84f;
        public float OuterRadiusRatio = 0.94f;
        public float StartAngle = 225f;
        public float SweepAngle = 270f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float fill = Mathf.Clamp01(fillAmount);
            if (fill <= 0.0001f) return;

            Rect r = GetPixelAdjustedRect();
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            float rOuter = radius * OuterRadiusRatio;
            float rInner = radius * InnerRadiusRatio;
            Vector2 center = r.center;

            Color c = color;
            float totalDeg = SweepAngle * fill;
            int segments = Mathf.Max(6, Mathf.RoundToInt(totalDeg / 6f));
            float angleStep = totalDeg / segments;

            for (int i = 0; i <= segments; i++)
            {
                float deg = StartAngle - i * angleStep;
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                Vector2 vIn = center + new Vector2(cos * rInner, sin * rInner);
                Vector2 vOut = center + new Vector2(cos * rOuter, sin * rOuter);

                vh.AddVert(vIn, c, Vector2.zero);
                vh.AddVert(vOut, c, Vector2.zero);

                if (i > 0)
                {
                    int baseIdx = (i - 1) * 2;
                    vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                    vh.AddTriangle(baseIdx + 3, baseIdx + 2, baseIdx);
                }
            }
        }
    }
}
