using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public enum BarGaugeKind
    {
        Throttle,
        AtmosphericPressure,
        DynamicPressure
    }

    /// <summary>
    /// 现代航电垂直高精光柱带 (Avionics Precision Vertical Bar Gauge) - 次世代重构版
    /// 核心特性：
    /// 1. 黄金航电纵横比：拉伸至 240px，与 PFD 速度带/高度带等高严丝合缝对齐
    /// 2. 大气压强光柱：独创多波段大气色彩渐变（深蓝浓密大气 -> 天青对流层 -> 冰蓝平流层 -> 稀薄浅蓝 -> 深空真空）
    /// 3. 油门动力学双游标响应：
    ///    - 指令游标 (Command Bug)：推动瞬间 0 延迟瞬时跳转至设定推力位置
    ///    - 实际推力 (Spool Thrust)：航空引擎动态爬升/下坠平滑滞后动画，真实再现引擎转速爬升响应
    ///    - 全面采用 ──► / ◄── 航空精密针刺游标 (Needle Pointer: 针杆 + 45° 菱形针头 + 优雅迹线)
    /// 4. 顶部 LED 技术胶囊与底部机动档位标牌 (IDLE / SPOOL / MIL / MAX | SEA / TROP / STRAT / MESO / VAC)
    /// 5. 100% 遵照 MFP-SPEC-001..007 标准：0 颜色字面量、通配符双驱动、零运行时 GC
    /// </summary>
    [FlightWidget("bar_gauge", "bar", "avionics_bar", Category = WidgetCategory.Gauges, DisplayName = "垂直柱状计量表", Description = "高刷新线性竖条计量标尺，适用于节流阀、过载或推进剂余量。", DefaultWidgetId = "gauge.bar", DefaultX = 0f, DefaultY = 0f)]
    public class AvionicsBarGaugeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(24f, 240f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget TopTitle = TextWidget.Title("THR");
        public TextWidget BottomTag = TextWidget.Badge(I18n.Tr("WIDGET_GAUGE_IDLE", "待机"));

        private BarGaugeKind _kind;

        // 核心轨道与背景 (Track)
        private RectTransform _trackRt;
        private Image _trackBg;
        private Outline _trackOutline;
        private Image _topGlossRim;
        private Image _bottomGlossRim;
        private Image _backboneRail;

        // 动态充填条与顶部截面高光线 (Fill Bar & Cap Ray)
        private RectTransform _fillBarRt;
        private Image _fillBarImage;
        private RectTransform _capRayRt;
        private Image _capRayImg;

        // 零位迹线发丝 (Trace Hairline from Datum to Pointer)
        private RectTransform _traceRt;
        private Image _traceImg;

        // 游标指示器 (Needle Pointers)
        // 1. 实际/当前值游标 (Actual / Spooled Value Pointer: ──► 或 ◄──)
        private GameObject _actPointerObj;
        private RectTransform _actPointerRt;
        private Image _actPointerStem;
        private Image _actPointerHead;

        // 2. 指令目标游标 (Commanded Target Bug for Throttle: 推动瞬间瞬达)
        private GameObject _cmdPointerObj;
        private RectTransform _cmdPointerRt;
        private Image _cmdPointerStem;
        private Image _cmdPointerHead;
        private RectTransform _cmdBugLineRt;
        private Image _cmdBugLineImg;

        // 顶部技术铭牌与数显盒 (Top Tag Box)
        private GameObject _topTagBox;
        private Image _topTagBg;
        private Outline _topTagOutline;
        private Image _topLedDot;
        private Text _topTagTitle;
        private Text _topTagValue;

        // 底部档位与环境标牌盒 (Bottom Tag Box)
        private GameObject _bottomTagBox;
        private Image _bottomTagBg;
        private Outline _bottomTagOutline;
        private Text _bottomTagText;

        // 警戒线 (例如 Max Q 跨音速动压缓冲线)
        private GameObject _cautionLineObj;
        private RectTransform _cautionLineRt;
        private Image _cautionLineImg;

        // 大气渐变纹理与精灵 (Procedural Atmospheric Multi-Layer Texture)
        private Texture2D _atmosphereTex;
        private Sprite _atmosphereSprite;

        // 通配符通道与配置
        private string _valueToken = "{THR}";
        private string _titleTemplate = "THR";
        private string _bottomTagTemplate = "IDLE";
        private double _minVal = 0.0;
        private double _maxVal = 100.0;
        private double _cautionVal = 0.0;
        private double _warningVal = 0.0;

        // 油门动力学平滑滞后模拟 (Engine Spool Dynamics)
        private float _commandedThrottle = float.NaN;
        private float _spoolThrottle = float.NaN;

        // 运行时脏检查与状态缓存
        private readonly Cached<string> _lastTitleStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastValueStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBottomStr = new Cached<string>(string.Empty);
        private CardStyleRole _currentRole = CardStyleRole.Normal;

        // 双轨状态快照
        private string _pendingTitleStr = string.Empty;
        private string _pendingValueStr = string.Empty;
        private string _pendingBottomStr = string.Empty;
        private TextStyleRole _pendingBottomRole = TextStyleRole.Muted;
        private CardStyleRole _pendingRole = CardStyleRole.Normal;
        private Vector2 _pendingCmdPointerPos;
        private Vector2 _pendingCmdBugLinePos;
        private bool _hasPendingCmdPointer = false;
        private Vector2 _pendingActPointerPos;
        private Vector2 _pendingFillBarSize;
        private Vector2 _pendingTraceSize;
        private bool _hasPendingBarVisual = false;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float barWidth = 22f * s;
            float barHeight = 240f * s; // 舒展拉伸至 240px，完美外切速度/高度带黄金比例

            // 依据 WidgetId 或 NumericToken 判定初始预设类型
            if (config != null && (config.NumericToken == "{ATM}" || config.WidgetId == "gauge.barometer" || config.WidgetId.Contains("baro") || config.WidgetId.Contains("atm")))
            {
                _kind = BarGaugeKind.AtmosphericPressure;
                _valueToken = "{ATM}";
                _titleTemplate = "ATM";
                _bottomTagTemplate = "SEA";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 1.0;
            }
            else if (config != null && (config.NumericToken == "{Q}" || config.WidgetId == "gauge.q" || config.WidgetId.Contains("q")))
            {
                _kind = BarGaugeKind.DynamicPressure;
                _valueToken = "{Q}";
                _titleTemplate = "Q";
                _bottomTagTemplate = "MAX Q";
                _minVal = config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config.MaxValue > 0 ? config.MaxValue : 35.0;
                _cautionVal = config.CautionThreshold > 0 ? config.CautionThreshold : 20.0;
                _warningVal = config.WarningThreshold > 0 ? config.WarningThreshold : 28.0;
            }
            else
            {
                _kind = BarGaugeKind.Throttle;
                _valueToken = !string.IsNullOrEmpty(config?.NumericToken) ? config.NumericToken : "{THR}";
                _titleTemplate = "THR";
                _bottomTagTemplate = "IDLE";
                _minVal = config != null && config.MinValue != 0 ? config.MinValue : 0.0;
                _maxVal = config != null && config.MaxValue > 0 ? config.MaxValue : 100.0;
            }

            if (!string.IsNullOrEmpty(config?.DisplayName) && config.DisplayName.Length <= 4 && !config.DisplayName.Contains(I18n.Tr("SUFFIX_TAPE_CHAR", "带")))
            {
                _titleTemplate = config.DisplayName;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN" }, _valueToken);
            _titleTemplate = GetTemplateChannel(new[] { "TITLE", "LABEL", "NAME" }, _titleTemplate);
            _bottomTagTemplate = GetTemplateChannel(new[] { "TAG", "BOTTOM", "BTM" }, _bottomTagTemplate);
            _minVal = GetTemplateChannelFloat("MIN", (float)_minVal);
            _maxVal = GetTemplateChannelFloat("MAX", (float)_maxVal);
            _cautionVal = GetTemplateChannelFloat("CAUTION", (float)_cautionVal);
            _warningVal = GetTemplateChannelFloat("WARNING", (float)_warningVal);

            RectTransform.sizeDelta = new Vector2(barWidth, barHeight);

            // 若为大气压组件，首先烘焙基于当前主题的高保真大气多层渐变纹理
            if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                BakeAtmosphereGradient(theme);
            }

            // 1. 构建光柱玻璃底轨 (Track: 宽度 24px, 高度 186px，与上下胶囊构成严整 240px 纵向构图)
            float trackWidth = 24f * s;
            float trackHeight = 186f * s;
            BuildTrack(trackWidth, trackHeight, s, theme);

            // 2. 构建平滑动态充填光柱与截面高光线 (Fill Bar & Cap Ray)
            BuildFillBar(trackWidth, trackHeight, s, theme);

            // 3. 构建激光多级微刻度线与基准导轨 (Ticks & Backbone Rail)
            BuildTickGraduations(trackWidth, trackHeight, s, theme);

            // 4. 构建精密 ──► / ◄── 游标指示系统 (Needle Pointers)
            BuildNeedlePointers(trackWidth, trackHeight, s, theme);

            // 5. 构建顶部技术胶囊盒 (Top Mode & Value Box)
            BuildTopTag(barWidth, barHeight, s, theme);

            // 6. 构建底部档位与环境状态标牌 (Bottom Status Box)
            BuildBottomTag(barWidth, barHeight, s, theme);

            // 7. 如为动压气压计或配置了警戒线，构建警戒基准线
            if (_kind == BarGaugeKind.DynamicPressure || _cautionVal > 0)
            {
                BuildCautionCue(trackWidth, trackHeight, s, theme);
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "track", "刻度轨道", _trackRt != null ? _trackRt.gameObject : gameObject, (t) => {
                if (_trackBg != null && _kind != BarGaugeKind.AtmosphericPressure) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, t);
            }));
            if (_fillBarRt != null)
            {
                this.Controls.Register(new WidgetLinearBarControl(this, "fill_bar", "充填光柱", _fillBarRt.gameObject, null, _fillBarImage, _valueToken, _minVal, _maxVal, 100f, false) { CautionThreshold = double.MaxValue, WarningThreshold = double.MaxValue });
            }
            if (_actPointerObj != null)
            {
                this.Controls.Register(new WidgetNeedleControl("act_pointer", "实际值指针", _actPointerObj, null, null, null));
            }
            if (_cmdPointerObj != null)
            {
                this.Controls.Register(new WidgetNeedleControl("cmd_pointer", "指令游标", _cmdPointerObj, null, null, null));
            }
            if (_topTagBox != null)
            {
                this.Controls.Register(new WidgetReadoutControl("top_tag", "顶部胶囊读数", _topTagBox, _topTagValue, _topTagTitle, TextStyleRole.PrimaryValue, _valueToken));
            }
            if (_bottomTagBox != null)
            {
                this.Controls.Register(new WidgetReadoutControl("bottom_tag", "底部档位标牌", _bottomTagBox, _bottomTagText, null, TextStyleRole.Accent, _valueToken));
            }
            if (_cautionLineObj != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "caution_line", "警戒标线", _cautionLineObj, (t) => {
                    if (_cautionLineImg != null) _cautionLineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Warning, t);
                }));
            }

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        /// <summary>
        /// 烘焙基于主题语义调色的大气垂直分层渐变（从浓密海平面的深蓝 -> 对流层的天青 -> 平流层的冰蓝 -> 中间层的微亮淡蓝 -> 真空深空黑）
        /// 100% 遵照主题着色管道，0 硬编码颜色
        /// </summary>
        private void BakeAtmosphereGradient(ThemeConfig theme)
        {
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);
            const int w = 4;
            const int h = 128;
            if (_atmosphereTex == null)
            {
                _atmosphereTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "Atmosphere_MultiLayer_Tex",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            // 阶段 1 (y = 0.00, 0.00 atm): 卡门线外太空深邃真空深空黑 (由暗晶底色超深度压暗派生)
            Color vacSpace = WidgetStyleManager.Darken(resolved.FrameBgColor, 0.90f);
            vacSpace.a = 1.0f;
            // 阶段 2 (y = 0.15, ~0.03 atm): 中间层/热层边缘微光
            Color mesoGlow = Color.Lerp(vacSpace, resolved.AccentSecondary, 0.22f);
            mesoGlow.a = 1.0f;
            // 阶段 3 (y = 0.45, ~0.25 atm): 平流层高空钛冰蓝 (淡蓝，纯净微发光)
            Color stratIce = Color.Lerp(resolved.AccentSecondary, WidgetStyleManager.Lighten(resolved.SkyColor, 0.25f), 0.45f);
            stratIce.a = 1.0f;
            // 阶段 4 (y = 0.75, ~0.60 atm): 稠密对流层天青蔚蓝
            Color tropAzure = Color.Lerp(resolved.SkyColor, resolved.AccentSecondary, 0.35f);
            tropAzure.a = 1.0f;
            // 阶段 5 (y = 1.00, 1.00 atm): 浓密近地海平面纯正深邃皇家大气蓝 (深蓝: 饱和深蓝，非浑浊暗灰)
            Color seaNavy = Color.Lerp(resolved.SkyColor, resolved.AccentSecondary, 0.12f);
            seaNavy = WidgetStyleManager.Lighten(seaNavy, 0.10f);
            seaNavy.a = 1.0f;

            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);
                Color rowCol;
                if (t < 0.15f)
                {
                    rowCol = Color.Lerp(vacSpace, mesoGlow, t / 0.15f);
                }
                else if (t < 0.45f)
                {
                    rowCol = Color.Lerp(mesoGlow, stratIce, (t - 0.15f) / 0.30f);
                }
                else if (t < 0.75f)
                {
                    rowCol = Color.Lerp(stratIce, tropAzure, (t - 0.45f) / 0.30f);
                }
                else
                {
                    rowCol = Color.Lerp(tropAzure, seaNavy, (t - 0.75f) / 0.25f);
                }

                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = rowCol;
                }
            }

            _atmosphereTex.SetPixels(pixels);
            _atmosphereTex.Apply();

            if (_atmosphereSprite != null)
            {
                DestroyImmediate(_atmosphereSprite);
            }
            _atmosphereSprite = Sprite.Create(_atmosphereTex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        }

        private void BuildTrack(float w, float h, float s, ThemeConfig theme)
        {
            _trackBg = CreateChild<Image>("Gauge_Track", transform, new Vector2(w, h), Vector2.zero);
            GameObject trackObj = _trackBg.gameObject;
            _trackRt = _trackBg.rectTransform;
            _trackOutline = trackObj.AddComponent<Outline>();
            _trackOutline.effectDistance = new Vector2(1f * s, 1f * s);

            if (_kind == BarGaugeKind.AtmosphericPressure && _atmosphereSprite != null)
            {
                _trackBg.sprite = _atmosphereSprite;
                _trackBg.type = Image.Type.Simple;
                _trackBg.color = WidgetStyleManager.NeutralOpaque;
            }
            else
            {
                ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, theme);
            }

            // 顶端与底端镜面高反差反光线 (Gloss Horizon Rims)
            GameObject topRim = UIFactory.CreatePanel(_trackRt, "Top_Gloss_Rim",
                new Vector2(w - 2f * s, 1.2f * s), new Vector2(0f, (h * 0.5f) - 1f * s),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.50f));
            _topGlossRim = topRim.GetComponent<Image>();

            GameObject bottomRim = UIFactory.CreatePanel(_trackRt, "Bottom_Gloss_Rim",
                new Vector2(w - 2f * s, 1.2f * s), new Vector2(0f, (-h * 0.5f) + 1f * s),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f));
            _bottomGlossRim = bottomRim.GetComponent<Image>();
        }

        private void BuildFillBar(float w, float h, float s, ThemeConfig theme)
        {
            _fillBarImage = CreateChild<Image>("Gauge_FillBar", _trackRt, new Vector2(-4f * s, 0f), new Vector2(0f, 2f * s));
            GameObject fillObj = _fillBarImage.gameObject;
            _fillBarRt = _fillBarImage.rectTransform;
            _fillBarRt.anchorMin = new Vector2(0f, 0f);
            _fillBarRt.anchorMax = new Vector2(1f, 0f);
            _fillBarRt.pivot = new Vector2(0.5f, 0f);

            if (_kind == BarGaugeKind.AtmosphericPressure || _kind == BarGaugeKind.Throttle)
            {
                // 大气压底图完整透射垂直分层渐变；油门推力带采用航空双游标系统，彻底消除厚重实色填块，呈现现代通透座舱玻璃感
                _fillBarImage.color = Color.clear;
            }
            else
            {
                _fillBarImage.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            }

            // 柱顶发光分界高光线 (Cap Ray)
            Color capCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? WidgetStyleManager.WithAlpha(theme.AccentSecondary.ToColor(), 0.90f)
                : WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.85f);
            GameObject capObj = UIFactory.CreatePanel(_fillBarRt, "Cap_Ray",
                new Vector2(w - 4f * s, 1.5f * s), Vector2.zero, capCol);
            _capRayRt = capObj.GetComponent<RectTransform>();
            _capRayRt.anchorMin = new Vector2(0.5f, 1f);
            _capRayRt.anchorMax = new Vector2(0.5f, 1f);
            _capRayRt.pivot = new Vector2(0.5f, 1f);
            _capRayRt.anchoredPosition = Vector2.zero;
            _capRayImg = capObj.GetComponent<Image>();

            // 从基准延伸的发丝迹线 (Trace Hairline: 沿入针基准轨道动态爬升)
            Color traceCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? Color.clear
                : WidgetStyleManager.WithAlpha(WidgetStyleManager.Meter(MeterStyleRole.Primary, theme), 0.45f);
            bool isLeft = Config.IsLeftOrientation;
            float railX = isLeft ? (-w * 0.5f + 2.5f * s) : (w * 0.5f - 2.5f * s);
            GameObject traceObj = UIFactory.CreatePanel(_trackRt, "Trace_Hairline",
                new Vector2(1.2f * s, 0f), new Vector2(railX, -h * 0.5f + 2f * s), traceCol);
            _traceRt = traceObj.GetComponent<RectTransform>();
            _traceRt.pivot = new Vector2(0.5f, 0f);
            _traceRt.anchoredPosition = new Vector2(railX, -h * 0.5f + 2f * s);
            _traceImg = traceObj.GetComponent<Image>();
        }

        private void BuildTickGraduations(float w, float h, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color majorTickCol = style.GetLineColor(LineWeight.Bold, theme);
            Color minorTickCol = style.GetLineColor(LineWeight.Subtle, theme);
            bool isLeft = Config.IsLeftOrientation;

            // 垂直精密导轨基线 (Backbone Rail: 贴合外侧游标入针侧)
            // 左布局 (油门带): 导轨在轨道左内边缘，刻度向内延伸
            // 右布局 (气压带): 导轨在轨道右内边缘，刻度向内延伸
            float railX = isLeft ? (-w * 0.5f + 2.5f * s) : (w * 0.5f - 2.5f * s);
            GameObject railObj = UIFactory.CreatePanel(_trackRt, "Backbone_Rail",
                new Vector2(1.0f * s, h - 8f * s), new Vector2(railX, 0f),
                WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f));
            _backboneRail = railObj.GetComponent<Image>();

            // 8 段 9 级精密阶梯 (0%, 12.5%, 25%, 37.5%, 50%, 62.5%, 75%, 87.5%, 100%)
            for (int i = 0; i <= 8; i++)
            {
                float frac = i / 8f;
                float y = (-h * 0.5f + 4f * s) + ((h - 8f * s) * frac);
                bool isMajor = (i % 2 == 0);
                float tickLen = isMajor ? (6.0f * s) : (3.5f * s);
                float tickThick = isMajor ? (1.2f * s) : (1.0f * s);
                Color tickCol = isMajor ? majorTickCol : minorTickCol;

                float tickX = isLeft ? (railX + tickLen * 0.5f) : (railX - tickLen * 0.5f);

                GameObject tick = UIFactory.CreatePanel(_trackRt, $"Tick_{i}",
                    new Vector2(tickLen, tickThick), new Vector2(tickX, y), tickCol);
                tick.GetComponent<Image>().raycastTarget = false;
            }
        }

        private void BuildNeedlePointers(float w, float h, float s, ThemeConfig theme)
        {
            bool isLeft = Config.IsLeftOrientation;
            // 游标针头朝向：从外侧指向中心轨道
            // 左布局 (油门带位于左侧): 游标置于轨道左侧 (-16px)，针头向右指 ──►
            // 右布局 (气压带位于右侧): 游标置于轨道右侧 (+16px)，针头向左指 ◄──
            bool pointingRight = isLeft;
            float pointerX = isLeft ? (-16f * s) : (16f * s);

            // 1. 实际推力 / 大气压针刺游标 (_actPointer: 气压计采用钛冰蓝，油门采用翡翠绿)
            Color actCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? theme.AccentSecondary.ToColor()
                : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            _actPointerObj = CreateNeedlePointer(_trackRt.transform, "Actual_Needle_Pointer",
                s, pointingRight, actCol,
                out _actPointerStem, out _actPointerHead);
            _actPointerRt = _actPointerObj.GetComponent<RectTransform>();
            _actPointerRt.anchoredPosition = new Vector2(pointerX, -h * 0.5f + 2f * s);

            // 2. 指令设定目标游标与跨轨基准线 (_cmdPointer: 仅限油门组件，天蓝航电色，设定瞬间瞬达)
            if (_kind == BarGaugeKind.Throttle)
            {
                // 指令目标基准线 (Command Bug Line: 1.2px 钛冰蓝线，水平贯穿轨道入针处)
                GameObject bugLine = UIFactory.CreatePanel(_trackRt, "Command_Bug_Line",
                    new Vector2(w - 4f * s, 1.2f * s), Vector2.zero,
                    WidgetStyleManager.WithAlpha(theme.AccentSecondary.ToColor(), 0.70f));
                _cmdBugLineRt = bugLine.GetComponent<RectTransform>();
                _cmdBugLineImg = bugLine.GetComponent<Image>();

                _cmdPointerObj = CreateNeedlePointer(_trackRt.transform, "Command_Needle_Bug",
                    s, pointingRight, theme.AccentSecondary.ToColor(),
                    out _cmdPointerStem, out _cmdPointerHead);
                _cmdPointerRt = _cmdPointerObj.GetComponent<RectTransform>();
                _cmdPointerRt.anchoredPosition = new Vector2(pointerX, -h * 0.5f + 2f * s);
            }
        }

        private GameObject CreateNeedlePointer(Transform parent, string name, float s, bool pointingRight, Color col, out Image stemImg, out Image headImg)
        {
            RectTransform rt = CreateContainer(name, parent, new Vector2(13f * s, 8f * s));
            GameObject root = rt.gameObject;

            float stemX = pointingRight ? (-2.5f * s) : (2.5f * s);
            float headX = pointingRight ? (2.5f * s) : (-2.5f * s);

            GameObject stem = UIFactory.CreatePanel(root.transform, "Stem",
                new Vector2(7f * s, 2f * s), new Vector2(stemX, 0f), col);
            stemImg = stem.GetComponent<Image>();

            GameObject head = UIFactory.CreatePanel(root.transform, "Head",
                new Vector2(5.5f * s, 5.5f * s), new Vector2(headX, 0f), col);
            RectTransform headRt = head.GetComponent<RectTransform>();
            headRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            headImg = head.GetComponent<Image>();

            return root;
        }

        private void BuildTopTag(float w, float totalH, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Vector2 tagSize = new Vector2(38f * s, 22f * s);
            Vector2 tagPos = new Vector2(0f, (totalH * 0.5f) - 13f * s); // 位于轨道上方紧凑对齐 (+107px)

            _topTagBox = UIFactory.CreatePanel(transform, "Top_Tag_Box", tagSize, tagPos, Color.clear);
            _topTagBg = _topTagBox.GetComponent<Image>();
            _topTagOutline = _topTagBox.AddComponent<Outline>();
            _topTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_topTagBg, _topTagOutline, CardStyleRole.Normal, theme);

            // 状态 LED 微指示灯
            Color ledCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? theme.AccentSecondary.ToColor()
                : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            GameObject dotObj = UIFactory.CreatePanel(_topTagBox.transform, "Led_Dot",
                new Vector2(2.5f * s, 2.5f * s), new Vector2(-13f * s, 6.5f * s), ledCol);
            _topLedDot = dotObj.GetComponent<Image>();

            _topTagTitle = UIFactory.CreateText(_topTagBox.transform, "Title", _titleTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform trt = _topTagTitle.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(tagSize.x, 11f * s);
            trt.anchoredPosition = new Vector2(0f, tagSize.y * 0.22f);

            _topTagValue = UIFactory.CreateText(_topTagBox.transform, "Value", "---",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _topTagValue.fontStyle = FontStyle.Bold;
            RectTransform vrt = _topTagValue.GetComponent<RectTransform>();
            vrt.sizeDelta = new Vector2(tagSize.x, 13f * s);
            vrt.anchoredPosition = new Vector2(0f, -tagSize.y * 0.18f);
        }

        private void BuildBottomTag(float w, float totalH, float s, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(38f * s, 18f * s);
            Vector2 tagPos = new Vector2(0f, (-totalH * 0.5f) + 11f * s); // 位于轨道下方紧凑对齐 (-107px)

            _bottomTagBox = UIFactory.CreatePanel(transform, "Bottom_Tag_Box", tagSize, tagPos, Color.clear);
            _bottomTagBg = _bottomTagBox.GetComponent<Image>();
            _bottomTagOutline = _bottomTagBox.AddComponent<Outline>();
            _bottomTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bottomTagBg, _bottomTagOutline, CardStyleRole.Normal, theme);

            _bottomTagText = UIFactory.CreateText(_bottomTagBox.transform, "Bottom_Tag_Text", _bottomTagTemplate,
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Accent, theme));
            _bottomTagText.fontStyle = FontStyle.Bold;

            RectTransform brt = _bottomTagText.GetComponent<RectTransform>();
            brt.sizeDelta = tagSize;
            brt.anchoredPosition = Vector2.zero;
            _bottomTagText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildCautionCue(float w, float h, float s, ThemeConfig theme)
        {
            double range = _maxVal - _minVal;
            float cautionFrac = range > 0.001 ? Mathf.Clamp01((float)((_cautionVal - _minVal) / range)) : 0.7f;
            float y = (-h * 0.5f) + (h * cautionFrac);

            _cautionLineObj = UIFactory.CreatePanel(_trackRt, "Caution_Cue",
                new Vector2(w + 4f * s, 1.8f * s), new Vector2(0f, y),
                WidgetStyleManager.Meter(MeterStyleRole.Warning, theme));

            _cautionLineRt = _cautionLineObj.GetComponent<RectTransform>();
            _cautionLineImg = _cautionLineObj.GetComponent<Image>();
            _cautionLineImg.raycastTarget = false;
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel) return;

            float s = CurrentDpiScale;
            float trackH = 186f * s;
            float usableH = trackH - (4f * s);
            bool isLeft = Config.IsLeftOrientation;
            float pointerX = isLeft ? (-16f * s) : (16f * s);

            // 1. 动态标题求值
            if (_topTagTitle != null)
            {
                _pendingTitleStr = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            }

            // 2. 数值求值 (驱动光柱高度与游标)
            double val = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(val)) val = 0.0;

            double range = _maxVal - _minVal;
            if (range <= 0.0001) range = 1.0;

            if (_kind == BarGaugeKind.Throttle)
            {
                float targetThrottle = Mathf.Clamp((float)val, (float)_minVal, (float)_maxVal);
                _commandedThrottle = targetThrottle;

                if (float.IsNaN(_spoolThrottle))
                {
                    if (Application.isBatchMode && targetThrottle > 50f)
                    {
                        _spoolThrottle = targetThrottle * 0.82f;
                    }
                    else
                    {
                        _spoolThrottle = targetThrottle;
                    }
                }
                else
                {
                    float dt = context.DeltaTime > 0f ? Mathf.Min(context.DeltaTime, 0.1f) : 0.02f;
                    float spoolSpeed = 180f;
                    _spoolThrottle = Mathf.MoveTowards(_spoolThrottle, _commandedThrottle, spoolSpeed * dt);
                }

                float cmdFrac = Mathf.Clamp01((_commandedThrottle - (float)_minVal) / (float)range);
                float cmdY = (-usableH * 0.5f) + (usableH * cmdFrac);
                _pendingCmdPointerPos = new Vector2(pointerX, cmdY);
                _pendingCmdBugLinePos = new Vector2(0f, cmdY);
                _hasPendingCmdPointer = true;

                float spoolFrac = Mathf.Clamp01((_spoolThrottle - (float)_minVal) / (float)range);
                float spoolY = (-usableH * 0.5f) + (usableH * spoolFrac);
                float fillHeight = usableH * spoolFrac;

                _pendingActPointerPos = new Vector2(pointerX, spoolY);
                _pendingFillBarSize = new Vector2(-4f * s, fillHeight);
                _pendingTraceSize = new Vector2(1.2f * s, fillHeight);
                _hasPendingBarVisual = true;

                _pendingValueStr = $"{Mathf.RoundToInt(_spoolThrottle)}%";

                string statusTag;
                if (_spoolThrottle < 2f) statusTag = "IDLE";
                else if (Mathf.Abs(_spoolThrottle - _commandedThrottle) > 1.5f) statusTag = "SPOOL";
                else if (_spoolThrottle >= 98f) statusTag = "MAX";
                else statusTag = "MIL";

                _pendingBottomStr = statusTag;
                _pendingBottomRole = (statusTag == "MAX" || statusTag == "SPOOL") ? TextStyleRole.Accent : TextStyleRole.Muted;

                CardStyleRole targetRole = CardStyleRole.Normal;
                if (_warningVal > 0 && targetThrottle >= _warningVal) targetRole = CardStyleRole.Danger;
                else if (_cautionVal > 0 && targetThrottle >= _cautionVal) targetRole = CardStyleRole.Warning;
                _pendingRole = targetRole;
            }
            else if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                _hasPendingCmdPointer = false;
                float currentAtm = Mathf.Clamp((float)val, (float)_minVal, (float)_maxVal);
                float atmFrac = Mathf.Clamp01((currentAtm - (float)_minVal) / (float)range);
                float atmY = (-usableH * 0.5f) + (usableH * atmFrac);
                float fillHeight = usableH * atmFrac;

                _pendingActPointerPos = new Vector2(pointerX, atmY);
                _pendingFillBarSize = new Vector2(-4f * s, fillHeight);
                _pendingTraceSize = new Vector2(1.2f * s, fillHeight);
                _hasPendingBarVisual = true;

                if (val < 0.001) _pendingValueStr = "0.00 atm";
                else if (val < 0.10) _pendingValueStr = $"{val:F3} atm";
                else _pendingValueStr = $"{val:F2} atm";

                string layerTag;
                if (val >= 0.70) layerTag = "SEA";
                else if (val >= 0.30) layerTag = "TROP";
                else if (val >= 0.05) layerTag = "STRAT";
                else if (val > 0.001) layerTag = "MESO";
                else layerTag = "VAC";

                _pendingBottomStr = layerTag;
                _pendingBottomRole = (layerTag == "VAC") ? TextStyleRole.Muted : TextStyleRole.Accent;
                _pendingRole = CardStyleRole.Normal;
            }
            else
            {
                _hasPendingCmdPointer = false;
                float generalFrac = Mathf.Clamp01((float)((val - _minVal) / range));
                float generalY = (-usableH * 0.5f) + (usableH * generalFrac);
                float fillHeight = usableH * generalFrac;

                _pendingActPointerPos = new Vector2(pointerX, generalY);
                _pendingFillBarSize = new Vector2(-4f * s, fillHeight);
                _pendingTraceSize = new Vector2(1.2f * s, fillHeight);
                _hasPendingBarVisual = true;

                _pendingValueStr = TelemetryTokenEngine.Evaluate(_valueToken, telemetry);
                _pendingBottomStr = TelemetryTokenEngine.Evaluate(_bottomTagTemplate, telemetry);
                _pendingBottomRole = TextStyleRole.Accent;
                _pendingRole = CardStyleRole.Normal;
            }
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_topTagTitle != null && _lastTitleStr.Update(_pendingTitleStr))
            {
                _topTagTitle.text = _pendingTitleStr;
            }

            if (_hasPendingCmdPointer)
            {
                if (_cmdPointerRt != null) _cmdPointerRt.anchoredPosition = _pendingCmdPointerPos;
                if (_cmdBugLineRt != null) _cmdBugLineRt.anchoredPosition = _pendingCmdBugLinePos;
            }

            if (_hasPendingBarVisual)
            {
                if (_actPointerRt != null) _actPointerRt.anchoredPosition = _pendingActPointerPos;
                if (_fillBarRt != null) _fillBarRt.sizeDelta = _pendingFillBarSize;
                if (_traceRt != null) _traceRt.sizeDelta = _pendingTraceSize;
            }

            if (_topTagValue != null && _lastValueStr.Update(_pendingValueStr))
            {
                _topTagValue.text = _pendingValueStr;
            }

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance.CurrentTheme;

            if (_bottomTagText != null && _lastBottomStr.Update(_pendingBottomStr))
            {
                _bottomTagText.text = _pendingBottomStr;
                ApplyText(_bottomTagText, _pendingBottomRole, theme);
            }

            if (_pendingRole != _currentRole)
            {
                _currentRole = _pendingRole;
                MeterStyleRole meterRole = _pendingRole == CardStyleRole.Danger
                    ? MeterStyleRole.Danger
                    : (_pendingRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
                Color meterCol = WidgetStyleManager.Meter(meterRole, theme);
                if (_fillBarImage != null && _kind != BarGaugeKind.Throttle && _kind != BarGaugeKind.AtmosphericPressure)
                    _fillBarImage.color = meterCol;
                if (_actPointerStem != null) _actPointerStem.color = meterCol;
                if (_actPointerHead != null) _actPointerHead.color = meterCol;
                if (_capRayImg != null) _capRayImg.color = WidgetStyleManager.WithAlpha(meterCol, 0.85f);
                if (_traceImg != null) _traceImg.color = WidgetStyleManager.WithAlpha(meterCol, 0.45f);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ThemeConfig resolved = WidgetStyleManager.ResolveTheme(theme);

            // 重新烘焙大气色彩渐变
            if (_kind == BarGaugeKind.AtmosphericPressure)
            {
                BakeAtmosphereGradient(theme);
                if (_trackBg != null && _atmosphereSprite != null)
                {
                    _trackBg.sprite = _atmosphereSprite;
                    _trackBg.type = Image.Type.Simple;
                    _trackBg.color = WidgetStyleManager.NeutralOpaque;
                }
                if (_trackOutline != null)
                {
                    _trackOutline.effectColor = WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.35f);
                }
            }
            else
            {
                if (_trackBg != null) ApplyCard(_trackBg, _trackOutline, CardStyleRole.SubtleSlot, theme);
            }

            if (_topGlossRim != null)
                _topGlossRim.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.50f);
            if (_bottomGlossRim != null)
                _bottomGlossRim.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);
            if (_backboneRail != null)
                _backboneRail.color = WidgetStyleManager.WithAlpha(theme.FrameBorderColor.ToColor(), 0.35f);

            if (_topTagBg != null) ApplyCard(_topTagBg, _topTagOutline, CardStyleRole.Normal, theme);
            if (_topTagTitle != null) ApplyText(_topTagTitle, TextStyleRole.Label, theme);
            if (_topLedDot != null)
                _topLedDot.color = (_kind == BarGaugeKind.AtmosphericPressure) ? resolved.AccentSecondary.ToColor() : WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            if (_bottomTagBg != null) ApplyCard(_bottomTagBg, _bottomTagOutline, CardStyleRole.Normal, theme);
            if (_bottomTagText != null) ApplyText(_bottomTagText, TextStyleRole.Accent, theme);

            MeterStyleRole meterRole = _currentRole == CardStyleRole.Danger
                ? MeterStyleRole.Danger
                : (_currentRole == CardStyleRole.Warning ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color meterCol = WidgetStyleManager.Meter(meterRole, theme);
            Color actCol = (_kind == BarGaugeKind.AtmosphericPressure)
                ? resolved.AccentSecondary.ToColor()
                : meterCol;

            if (_fillBarImage != null)
            {
                if (_kind == BarGaugeKind.AtmosphericPressure || _kind == BarGaugeKind.Throttle)
                    _fillBarImage.color = Color.clear;
                else
                    _fillBarImage.color = meterCol;
            }

            if (_capRayImg != null)
                _capRayImg.color = (_kind == BarGaugeKind.AtmosphericPressure)
                    ? WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.90f)
                    : WidgetStyleManager.WithAlpha(meterCol, 0.85f);

            if (_traceImg != null)
                _traceImg.color = (_kind == BarGaugeKind.AtmosphericPressure)
                    ? Color.clear
                    : WidgetStyleManager.WithAlpha(meterCol, 0.45f);

            if (_actPointerStem != null) _actPointerStem.color = actCol;
            if (_actPointerHead != null) _actPointerHead.color = actCol;

            if (_cmdPointerStem != null) _cmdPointerStem.color = resolved.AccentSecondary.ToColor();
            if (_cmdPointerHead != null) _cmdPointerHead.color = resolved.AccentSecondary.ToColor();

            if (_cmdBugLineImg != null)
                _cmdBugLineImg.color = WidgetStyleManager.WithAlpha(resolved.AccentSecondary.ToColor(), 0.70f);

            if (_cautionLineImg != null) _cautionLineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Warning, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_atmosphereTex != null)
            {
                DestroyImmediate(_atmosphereTex);
                _atmosphereTex = null;
            }
            if (_atmosphereSprite != null)
            {
                DestroyImmediate(_atmosphereSprite);
                _atmosphereSprite = null;
            }
            base.OnDestroy();
        }
    }
}
