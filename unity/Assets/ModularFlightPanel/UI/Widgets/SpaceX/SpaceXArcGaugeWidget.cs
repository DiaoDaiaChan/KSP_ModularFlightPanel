using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 星舰发射广播风格马蹄形弧线表盘 (SpaceX Webcast Arc Gauge)
    /// 适用于：
    ///   - 速度 (Speed)：大号读数 + KM/H 单位 + 动态弧形进度
    ///   - 高度 (Altitude)：大号读数 + KM 单位 + 动态弧形进度
    ///   - 任何机载遥测数值（油门、垂直速度、动压等皆可经由 WidgetConfig 配置）
    /// 严格遵循 MFP 架构规范：
    ///   - 零硬编码与零颜色字面量 (MFP-SPEC-006)
    ///   - 阶梯刷新与脏标记保护 (MFP-SPEC-002, 004)
    ///   - 纯 UGUI 原生填充与抗锯齿矢量光栅化
    /// </summary>
    [FlightWidget("spacex_arc", "spacex_gauge", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 环形遥测仪表", Description = "SpaceX 龙飞船高精度同心圆弧表盘，带发光步进游标与动态数字标定。", DefaultWidgetId = "spacex.speed", DefaultX = -240f, DefaultY = 0f, ExactIds = new[] { "spacex.speed", "spacex.altitude" })]
    public class SpaceXArcGaugeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(110f, 110f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_SPX_SPEED", "速度"));
        public TextWidget Value = TextWidget.Value("{SPD}");

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        private Image _arcTrackImage;
        private Image _arcFillImage;

        private Text _topLabelText;
        private Text _primaryValueText;
        private Text _unitLabelText;

        // 静态共享抗锯齿环状图元 (避免每个实例重复烘焙 Texture)
        private static Sprite _sharedRingSprite;
        private static Texture2D _sharedRingTexture;

        // 遥测缓存与脏标记
        private double _lastCachedValue = double.NaN;
        private string _lastFormattedText = string.Empty;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // CustomTemplate 自定义通道
        private string _tokenKey;
        private string _titleTemplate;
        private string _unitTemplate;

        // 弧度几何常数：270° 穹顶弧，底部 90° 开口容纳单位标签
        private const float ArcTotalFraction = 0.75f; // 270° / 360°

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _tokenKey = GetTemplateChannel("TOKEN", null);
            _titleTemplate = GetTemplateChannel("TITLE", null);
            _unitTemplate = GetTemplateChannel("UNIT", null);

            // 1. 组件包围盒 (基准 110x110 逻辑像素)
            Vector2 size = new Vector2(110f * s, 110f * s);
            RectTransform.sizeDelta = size;

            // 2. 外部半透卡片底衬 (轻质深色玻璃，符合 SpaceX 航电风格)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            EnsureSharedRingSprite();

            // 3. 构建马蹄弧形轨道 (Track) 与填充条 (Fill)
            // 弧度起点：左下角 225°，顺时针至右下角 315° (-45°)
            // UGUI Radial360 Origin.Bottom = 270°，逆时针旋转 45° (z = -45) 使其起点对准 225°
            float arcDiameter = 98f * s;

            // 底槽轨道
            GameObject trackGo = new GameObject("Arc_Track", typeof(RectTransform), typeof(Image));
            trackGo.transform.SetParent(transform, false);
            RectTransform trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.sizeDelta = new Vector2(arcDiameter, arcDiameter);
            trackRt.anchoredPosition = new Vector2(0f, 2f * s);
            trackRt.localEulerAngles = new Vector3(0f, 0f, -45f);

            _arcTrackImage = trackGo.GetComponent<Image>();
            _arcTrackImage.sprite = _sharedRingSprite;
            _arcTrackImage.type = Image.Type.Filled;
            _arcTrackImage.fillMethod = Image.FillMethod.Radial360;
            _arcTrackImage.fillOrigin = (int)Image.Origin360.Bottom;
            _arcTrackImage.fillClockwise = true;
            _arcTrackImage.fillAmount = ArcTotalFraction;
            _arcTrackImage.raycastTarget = false;

            // 动态进度填充弧
            GameObject fillGo = new GameObject("Arc_Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = new Vector2(arcDiameter, arcDiameter);
            fillRt.anchoredPosition = new Vector2(0f, 2f * s);
            fillRt.localEulerAngles = new Vector3(0f, 0f, -45f);

            _arcFillImage = fillGo.GetComponent<Image>();
            _arcFillImage.sprite = _sharedRingSprite;
            _arcFillImage.type = Image.Type.Filled;
            _arcFillImage.fillMethod = Image.FillMethod.Radial360;
            _arcFillImage.fillOrigin = (int)Image.Origin360.Bottom;
            _arcFillImage.fillClockwise = true;
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
            this.Controls.Register(new WidgetReadoutControl(_topLabelText, null, TextStyleRole.Label, "Top Label", "顶部测量参数名"));
            this.Controls.Register(new WidgetReadoutControl(_primaryValueText, null, TextStyleRole.PrimaryValue, "Primary Value", "中央超大号读数"));
            this.Controls.Register(new WidgetReadoutControl(_unitLabelText, null, TextStyleRole.Unit, "Unit Label", "底部工程单位标签"));
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
            ApplyMeter(_arcTrackImage, _arcFillImage, null, MeterStyleRole.Primary, theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel || Config == null) return;
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. 通过通配符引擎求值 (优先使用 _tokenKey 或 Config.NumericToken，例如 {SPD:SURF:KMH} 或 {ALT:ASL:KM})
            string token = !string.IsNullOrEmpty(_tokenKey)
                ? _tokenKey
                : (!string.IsNullOrEmpty(Config.NumericToken) ? Config.NumericToken : "{SPD:SURF:KMH}");
            double val = TelemetryTokenEngine.EvaluateNumeric(token, telemetry);

            if (double.IsNaN(val))
            {
                ShowUnavailable();
                return;
            }

            // 2. 脏标记检查：变化小于阈值时不触发 UGUI 文本重排
            double delta = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (!double.IsNaN(_lastCachedValue) && Math.Abs(val - _lastCachedValue) <= delta)
            {
                return;
            }
            _lastCachedValue = val;

            // 3. 更新数字文本
            string newStr = TelemetryTokenEngine.Evaluate(token, telemetry);
            if (newStr != _lastFormattedText)
            {
                _lastFormattedText = newStr;
                _primaryValueText.text = newStr;
            }

            // 4. 计算圆弧填充百分比 (归一化量程 [MinValue, MaxValue]，最大占 270°)
            float frac = NormalizeToRange(val);
            if (_arcFillImage != null)
            {
                _arcFillImage.fillAmount = frac * ArcTotalFraction;
            }

            // 5. 状态机告警着色
            CardStyleRole targetRole = ResolveCardRole(val);
            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                ApplyCard(_bgImage, _bgOutline, targetRole, theme);
                ApplyText(_primaryValueText, GetValueTextRole(targetRole), theme);
                MeterStyleRole meterRole = targetRole == CardStyleRole.Danger
                    ? MeterStyleRole.Danger
                    : (targetRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                ApplyMeter(null, _arcFillImage, null, meterRole, theme);
            }
        }

        private float NormalizeToRange(double val)
        {
            double range = Config.MaxValue - Config.MinValue;
            if (range <= 0.0001) return 0f;
            return Mathf.Clamp01((float)((val - Config.MinValue) / range));
        }

        private CardStyleRole ResolveCardRole(double val)
        {
            string mode = string.IsNullOrEmpty(Config.LimitMode) ? "hard" : Config.LimitMode.ToLowerInvariant();
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
            if (_lastFormattedText == "---") return;
            _lastCachedValue = double.NaN;
            _lastFormattedText = "---";
            _primaryValueText.text = "---";
            if (_arcFillImage != null) _arcFillImage.fillAmount = 0f;
        }

        /// <summary>
        /// 程序化烘焙高精 256x256 矢量抗锯齿细圆环 Sprite (零颜色字面量，全走 NeutralOpaque)
        /// </summary>
        private static void EnsureSharedRingSprite()
        {
            if (_sharedRingSprite != null) return;

            const int size = 256;
            _sharedRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _sharedRingTexture.filterMode = FilterMode.Bilinear;
            _sharedRingTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] cols = new Color[size * size];
            float half = size * 0.5f;
            float rOuter = 0.94f;
            float rInner = 0.84f;
            float feather = 3f / half; // 3px 次像素平滑

            Color opaque = WidgetStyleManager.NeutralOpaque;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    if (r > rOuter + feather || r < rInner - feather)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    float outerAlpha = Mathf.Clamp01((rOuter - r) / feather + 0.5f);
                    float innerAlpha = Mathf.Clamp01((r - rInner) / feather + 0.5f);
                    float alpha = Mathf.Min(outerAlpha, innerAlpha);

                    Color c = opaque;
                    c.a = alpha;
                    cols[y * size + x] = c;
                }
            }

            _sharedRingTexture.SetPixels(cols);
            _sharedRingTexture.Apply(false, true);

            _sharedRingSprite = Sprite.Create(_sharedRingTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
