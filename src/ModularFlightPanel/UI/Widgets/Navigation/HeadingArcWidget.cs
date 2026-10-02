using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    public readonly struct HeadingArcState : IEquatable<HeadingArcState>
    {
        public readonly bool HasVessel;
        public readonly float DisplayedHeading;
        public readonly int BubbleDeg;

        public HeadingArcState(bool hasVessel, float displayedHeading, int bubbleDeg)
        {
            HasVessel = hasVessel;
            DisplayedHeading = displayedHeading;
            BubbleDeg = bubbleDeg;
        }

        public bool Equals(HeadingArcState other)
        {
            return HasVessel == other.HasVessel &&
                   Mathf.Abs(DisplayedHeading - other.DisplayedHeading) < 0.02f &&
                   BubbleDeg == other.BubbleDeg;
        }

        public override bool Equals(object obj) => obj is HeadingArcState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ DisplayedHeading.GetHashCode();
                hash = (hash * 397) ^ BubbleDeg.GetHashCode();
                return hash;
            }
        }
    }

    public class HeadingArcLogic : WidgetLogic<HeadingArcState>
    {
        public string ValueToken { get; set; } = "{HDG}";

        private float _targetHeading = 0f;
        private float _displayedHeading = 0f;
        private float _headingVelocity = 0f;
        private bool _isHeadingInitialized = false;

        public override void Reset()
        {
            _targetHeading = 0f;
            _displayedHeading = 0f;
            _headingVelocity = 0f;
            _isHeadingInitialized = false;
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = new HeadingArcState(false, 0f, 0);
                return;
            }

            float rawHeading;
            var hook = NavBallHookService.Provider;
            if (hook != null && hook.HasStockNavBall)
            {
                rawHeading = hook.HeadingAngle;
            }
            else
            {
                double evalHdg = TelemetryTokenEngine.EvaluateNumeric(ValueToken, telemetry);
                rawHeading = !double.IsNaN(evalHdg) ? (float)evalHdg : (float)telemetry.Heading;
            }
            if (float.IsNaN(rawHeading)) rawHeading = 0f;

            _targetHeading = (rawHeading % 360f + 360f) % 360f;

            if (!_isHeadingInitialized)
            {
                _displayedHeading = _targetHeading;
                _isHeadingInitialized = true;
            }

            if (deltaTime <= 0.0001f)
            {
                _displayedHeading = _targetHeading;
            }
            else
            {
                float angleDiff = Mathf.DeltaAngle(_displayedHeading, _targetHeading);
                if (Mathf.Abs(angleDiff) > 120f)
                {
                    _displayedHeading = _targetHeading;
                    _headingVelocity = 0f;
                }
                else
                {
                    _displayedHeading = Mathf.SmoothDampAngle(_displayedHeading, _targetHeading, ref _headingVelocity, 0.09f, 900f, deltaTime);
                    _displayedHeading = (_displayedHeading % 360f + 360f) % 360f;
                }
            }

            int degInt = Mathf.RoundToInt(_displayedHeading) % 360;
            if (degInt < 0) degInt += 360;

            CurrentState = new HeadingArcState(true, _displayedHeading, degInt);
        }
    }

    /// <summary>
    /// GPU 程序化罗盘标尺圆弧底板与内外轮廓线 (单 Graphic 矢量几何，零额外 GameObject 堆叠，SPEC-002)
    /// </summary>
    public class ProceduralCompassArcGraphic : MaskableGraphic
    {
        public float RadiusX = 92f;
        public float RadiusY = 92f;
        public float YCenterOffset = 76f;
        public float MaxAngularSpan = 55f;
        public float DpiScale = 1f;
        public float AdaptiveScale = 1f;

        public Color PlateColor = Color.clear;
        public Color OuterRimColor = Color.clear;
        public Color InnerRimColor = Color.clear;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (RadiusX <= 1f || RadiusY <= 1f || MaxAngularSpan <= 1f) return;

            float s = DpiScale * AdaptiveScale;
            float outerOffset = 4.5f * s;
            float outerRimThick = Mathf.Max(1.2f, 1.4f * s);
            float innerOffset = -22.5f * s;
            float innerRimThick = Mathf.Max(1.0f, 1.2f * s);

            const int segCount = 28;
            float step = (MaxAngularSpan * 2f) / segCount;

            for (int i = 0; i <= segCount; i++)
            {
                float ang = -MaxAngularSpan + (i * step);
                float rad = ang * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                float nx = RadiusY * sin;
                float ny = RadiusX * cos;
                float normLen = Mathf.Sqrt(nx * nx + ny * ny);
                Vector2 normDir = normLen > 0.0001f ? new Vector2(nx / normLen, ny / normLen) : Vector2.up;

                Vector2 basePt = new Vector2(sin * RadiusX, cos * RadiusY - YCenterOffset);

                // 两侧平滑渐隐 (边缘羽化)
                float edgeFade = Mathf.Clamp01((MaxAngularSpan - Mathf.Abs(ang)) / 8f);

                Color cOuter = OuterRimColor;
                cOuter.a *= edgeFade;

                Color cPlate = PlateColor;
                cPlate.a *= edgeFade;

                Color cInner = InnerRimColor;
                cInner.a *= edgeFade;

                // 6 顶构造 3 个独立四边形，避免颜色混合污染
                // Quad 0: 外轮廓发光线 (outerOffset -> outerOffset + outerRimThick)
                Vector2 p0 = basePt + normDir * (outerOffset + outerRimThick);
                Vector2 p1 = basePt + normDir * outerOffset;

                // Quad 1: 暗色玻璃底板 (innerOffset -> outerOffset)
                Vector2 p2 = basePt + normDir * outerOffset;
                Vector2 p3 = basePt + normDir * innerOffset;

                // Quad 2: 内轮廓辅助线 (innerOffset - innerRimThick -> innerOffset)
                Vector2 p4 = basePt + normDir * innerOffset;
                Vector2 p5 = basePt + normDir * (innerOffset - innerRimThick);

                vh.AddVert(p0, cOuter, Vector2.zero);
                vh.AddVert(p1, cOuter, Vector2.zero);
                vh.AddVert(p2, cPlate, Vector2.zero);
                vh.AddVert(p3, cPlate, Vector2.zero);
                vh.AddVert(p4, cInner, Vector2.zero);
                vh.AddVert(p5, cInner, Vector2.zero);

                if (i > 0)
                {
                    int prev = (i - 1) * 6;
                    int curr = i * 6;

                    // Quad 0: 外轮廓
                    vh.AddTriangle(prev + 0, curr + 0, curr + 1);
                    vh.AddTriangle(curr + 1, prev + 1, prev + 0);

                    // Quad 1: 底板
                    vh.AddTriangle(prev + 2, curr + 2, curr + 3);
                    vh.AddTriangle(curr + 3, prev + 3, prev + 2);

                    // Quad 2: 内轮廓
                    vh.AddTriangle(prev + 4, curr + 4, curr + 5);
                    vh.AddTriangle(curr + 5, prev + 5, prev + 4);
                }
            }
        }
    }

    /// <summary>
    /// PFD 姿态球顶部圆弧航向指示带 (Navball Heading Arc Ribbon - Set 2 / 图2)
    /// 紧密环绕姿态球上缘，具备平滑滚动的度数刻度、红南/蓝北罗盘主方位标识、
    /// 顶部气泡框 (Speech-bubble) 权威数显标牌以及翡翠绿反T型基准游标。
    /// 支持智能密度 LOD 与小分辨率自适应布局，消除摩尔纹与几何遮挡，彻底实现 0 GC 与极致性能。
    /// </summary>
    [FlightWidget("heading_arc", "heading", "compass_arc", Category = WidgetCategory.Navigation, DisplayName = "PFD 航向指示标尺弧", Description = "主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。", DefaultWidgetId = "core.heading_arc", DefaultX = 0f, DefaultY = 76f, IsSingleton = true, HighFrequency = true, ExactIds = new[] { "core.heading_arc" })]
    public class HeadingArcWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(202f, 82f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget - 允许编辑模式自由拉动长宽比与曲率)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(140f, 40f);
        public Vector2 MaxBaseSize => new Vector2(400f, 200f);

        private const int MAX_VISIBLE_TICKS = 24;
        private const float ARC_RADIUS = 92f;
        private const float ARC_Y_CENTER_OFFSET = 76f;
        private const float MAX_ANGULAR_SPAN = 55f; // 可见视口半角范围 (±55°)

        private static readonly string[] _hdg3DigitStrings = GenerateHdg3DigitStrings();
        private static string[] GenerateHdg3DigitStrings()
        {
            var arr = new string[360];
            for (int i = 0; i < 360; i++)
            {
                arr[i] = i.ToString("D3") + "°";
            }
            return arr;
        }

        private static readonly string[] _tickDegStrings = GenerateTickDegStrings();
        private static string[] GenerateTickDegStrings()
        {
            var arr = new string[360];
            for (int i = 0; i < 360; i++)
            {
                arr[i] = i.ToString("D3");
            }
            return arr;
        }

        // 当前动态自适应尺寸与椭圆几何参数 (Adaptive Radius & Curvature)
        private readonly CachedFloat _currentRadiusX = new CachedFloat(0f, 0.05f);
        private readonly CachedFloat _currentRadiusY = new CachedFloat(0f, 0.05f);
        private readonly CachedFloat _currentYCenterOffset = new CachedFloat(0f, 0.05f);
        private readonly CachedFloat _currentAdaptiveScale = new CachedFloat(1.0f, 0.005f);

        private struct HeadingTickUI
        {
            public GameObject Root;
            public RectTransform Rt;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
            public int CurrentDeg;
            public float LastAlpha;
            public Color BaseColor;
            public Color BaseLineColor;
        }

        private readonly List<HeadingTickUI> _tickPool = new List<HeadingTickUI>(MAX_VISIBLE_TICKS);

        // 气泡框与游标
        private GameObject _speechBubbleRoot;
        private RectTransform _speechBubbleRt;
        private Image _bubbleBg;
        private Outline _bubbleOutline;
        private Image _bubblePointerTip;
        private Outline _bubblePointerOutline;
        private Text _headingText;
        private Button _bubbleBtn;

        // 翡翠绿基准游标 (绿反T)
        private GameObject _lubberLineRoot;
        private RectTransform _lubberRt;
        private Image _lubberBar;
        private Image _lubberStem;

        // 单 Graphic 高性能程序化圆弧网格
        private ProceduralCompassArcGraphic _arcBandGraphic;

        // 通配符通道与配置
        private string _valueToken = "{HDG}";

        // 业务解算大脑核心
        private readonly HeadingArcLogic _logic = new HeadingArcLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private readonly CachedFloat _lastRenderedHeading = new CachedFloat(-999f, tolerance: 0.02f);
        private readonly Cached<int> _lastBubbleDeg = new Cached<int>(-1);
        private Color _cachedTextCol;
        private Color _cachedSubTickCol;
        private Color _cachedNorthCol;
        private Color _cachedEastCol;
        private Color _cachedSouthCol;
        private Color _cachedWestCol;
        private bool _hasCachedArcColors = false;

        private Color GetCachedCardinalColor(int deg)
        {
            switch (deg)
            {
                case 0: return _cachedNorthCol;
                case 90: return _cachedEastCol;
                case 180: return _cachedSouthCol;
                case 270: return _cachedWestCol;
                default: return _cachedTextCol;
            }
        }

        public static Action OnCycleHeadingModeAction;
        public static Action OnToggleReferenceFrameWindowAction;

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            if (s <= 0.001f) s = 1.0f;

            float scaleX = (BaseSize.x > 0f) ? (pixelSize.x / (BaseSize.x * s)) : 1.0f;
            float scaleY = (BaseSize.y > 0f) ? (pixelSize.y / (BaseSize.y * s)) : 1.0f;

            scaleX = Mathf.Clamp(scaleX, 0.4f, 3.0f);
            scaleY = Mathf.Clamp(scaleY, 0.4f, 3.0f);

            _currentRadiusX.Value = ARC_RADIUS * s * scaleX;
            _currentRadiusY.Value = ARC_RADIUS * s * scaleY;
            _currentYCenterOffset.Value = ARC_Y_CENTER_OFFSET * s * scaleY;
            _currentAdaptiveScale.Value = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.55f, 2.0f);

            if (_arcBandGraphic != null)
            {
                _arcBandGraphic.RadiusX = _currentRadiusX.Value;
                _arcBandGraphic.RadiusY = _currentRadiusY.Value;
                _arcBandGraphic.YCenterOffset = _currentYCenterOffset.Value;
                _arcBandGraphic.DpiScale = s;
                _arcBandGraphic.AdaptiveScale = _currentAdaptiveScale.Value;
                _arcBandGraphic.SetVerticesDirty();
            }

            ApplyAdaptiveElementScaling(s, _currentAdaptiveScale.Value);
            UpdateRotatingCompassRose(_logic.CurrentState.DisplayedHeading, force: true);
        }

        private void ApplyAdaptiveElementScaling(float s, float adaptiveScale)
        {
            float rY = _currentRadiusY.Value > 0.001f ? _currentRadiusY.Value : (ARC_RADIUS * s);
            float yOff = _currentYCenterOffset.Value > 0.001f ? _currentYCenterOffset.Value : (ARC_Y_CENTER_OFFSET * s);
            float effScale = s * adaptiveScale;

            // 1. 基准游标 (翡翠绿反T): 贴紧圆弧外缘 (+4.5f * effScale)
            float barW = Mathf.Max(10f, 12f * effScale);
            float barH = Mathf.Max(2f, 2.2f * effScale);
            float stemW = Mathf.Max(2f, 2.2f * effScale);
            float stemH = Mathf.Max(4.5f, 5.0f * effScale);
            Vector2 lubberPos = new Vector2(0f, rY - yOff + (4.5f * effScale));

            if (_lubberLineRoot != null && _lubberRt != null)
            {
                _lubberRt.anchoredPosition = lubberPos;
                _lubberRt.sizeDelta = new Vector2(barW + 2f, barH + stemH);

                if (_lubberBar != null)
                {
                    _lubberBar.rectTransform.sizeDelta = new Vector2(barW, barH);
                    _lubberBar.rectTransform.anchoredPosition = new Vector2(0f, barH * 0.5f);
                }
                if (_lubberStem != null)
                {
                    _lubberStem.rectTransform.sizeDelta = new Vector2(stemW, stemH);
                    _lubberStem.rectTransform.anchoredPosition = new Vector2(0f, -stemH * 0.5f);
                }
            }

            // 2. 顶部气泡框数显标牌: 坐落于基准游标正上方，指针尖端与基准横杠保留 1.5px 清爽间隔
            float bubbleW = Mathf.Max(40f, 46f * effScale);
            float bubbleH = Mathf.Max(16f, 18f * effScale);
            Vector2 boxSize = new Vector2(bubbleW, bubbleH);

            float tipSize = Mathf.Max(4.5f, 5.5f * effScale);
            float tipVisualH = tipSize * 0.707f;

            if (_speechBubbleRt != null)
            {
                _speechBubbleRt.sizeDelta = boxSize;

                if (_bubblePointerTip != null)
                {
                    _bubblePointerTip.rectTransform.sizeDelta = new Vector2(tipSize, tipSize);
                    _bubblePointerTip.rectTransform.anchoredPosition = new Vector2(0f, -boxSize.y * 0.5f + 0.5f * effScale);
                }

                // 标牌垂直位置: 基准横杠顶部 + 1.5px 间隔 + 尖角下延高 + 标牌半高
                float bubblePosY = (lubberPos.y + barH) + 1.5f + (boxSize.y * 0.5f + tipVisualH - 0.5f * effScale);
                _speechBubbleRt.anchoredPosition = new Vector2(0f, bubblePosY);

                if (_headingText != null)
                {
                    _headingText.fontSize = Mathf.Clamp(Mathf.RoundToInt(11.5f * effScale), 10, 20);
                }
            }

            // 3. 刻度文字与尺寸自适应 (高质量超采样)
            const int BASE_LABEL_FONT_SIZE = 18;
            float targetVisualFontSize = Mathf.Clamp(9.2f * effScale, 8.5f, 14f);
            float fontScale = targetVisualFontSize / (float)BASE_LABEL_FONT_SIZE;
            Vector2 labelSize = new Vector2(36f, 18f);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                var item = _tickPool[i];
                if (item.Label != null)
                {
                    item.Label.fontSize = BASE_LABEL_FONT_SIZE;
                    item.LabelRt.sizeDelta = labelSize;
                    item.LabelRt.localScale = new Vector3(fontScale, fontScale, 1f);
                    item.LabelRt.anchoredPosition = new Vector2(0f, -14.0f * effScale);
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            ApplyCanvasIsolation(true);
            if (SubCanvas != null)
            {
                SubCanvas.pixelPerfect = false;
            }

            float s = CurrentDpiScale;
            if (_currentRadiusX.Value <= 0.001f || _currentRadiusY.Value <= 0.001f)
            {
                float effW = (RectTransform != null && RectTransform.sizeDelta.x > 10f) ? RectTransform.sizeDelta.x : (BaseSize.x * s);
                float effH = (RectTransform != null && RectTransform.sizeDelta.y > 10f) ? RectTransform.sizeDelta.y : (BaseSize.y * s);
                float scaleX = Mathf.Clamp(effW / (BaseSize.x * s), 0.4f, 3.0f);
                float scaleY = Mathf.Clamp(effH / (BaseSize.y * s), 0.4f, 3.0f);

                _currentRadiusX.Value = ARC_RADIUS * s * scaleX;
                _currentRadiusY.Value = ARC_RADIUS * s * scaleY;
                _currentYCenterOffset.Value = ARC_Y_CENTER_OFFSET * s * scaleY;
                _currentAdaptiveScale.Value = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.55f, 2.0f);
            }

            if (config != null && !string.IsNullOrEmpty(config.NumericToken))
            {
                _valueToken = config.NumericToken;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN", "HDG" }, _valueToken);
            _logic.ValueToken = _valueToken;

            // 1. 构建 GPU 矢量程序化罗盘弧
            BuildArcBandGraphic(s, theme);

            // 2. 初始化刻度对象池
            BuildTickPool(s, theme);

            // 3. 构建顶部气泡框数显标牌
            BuildSpeechBubble(s, theme);

            // 4. 构建翡翠绿反T型基准游标
            BuildLubberMark(s, theme);

            // 5. 应用尺寸自适应
            ApplyAdaptiveElementScaling(s, _currentAdaptiveScale.Value);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "arc_band", "罗盘弧底带", _arcBandGraphic.gameObject, (t) => {
                WidgetStyleManager st = WidgetStyleManager.Instance;
                Color borderCol = WidgetStyleManager.GetDerivedColor((Color)t.FrameBorderColor, 0.50f);
                _arcBandGraphic.PlateColor = st.GetCardBackgroundColor(CardStyleRole.Normal, t);
                _arcBandGraphic.OuterRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Heavy);
                _arcBandGraphic.InnerRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
                _arcBandGraphic.material = st.GetUiMaterial(isText: false);
                _arcBandGraphic.SetVerticesDirty();
            }));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "speech_bubble", "气泡航向标牌", _speechBubbleRoot, (t) => {
                ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, t);
                ApplyText(_headingText, TextStyleRole.PrimaryValue, t);
            }));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "lubber_mark", "翡翠绿基准游标", _lubberLineRoot, (t) => {
                Color lc = WidgetStyleManager.Meter(MeterStyleRole.Primary, t);
                if (_lubberBar != null) _lubberBar.color = lc;
                if (_lubberStem != null) _lubberStem.color = lc;
            }));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildArcBandGraphic(float s, ThemeConfig theme)
        {
            RectTransform bandRt = CreateContainer("Arc_Band_Root", transform, Vector2.zero, Vector2.zero);
            _arcBandGraphic = bandRt.gameObject.AddComponent<ProceduralCompassArcGraphic>();
            _arcBandGraphic.raycastTarget = false;
            _arcBandGraphic.RadiusX = _currentRadiusX.Value;
            _arcBandGraphic.RadiusY = _currentRadiusY.Value;
            _arcBandGraphic.YCenterOffset = _currentYCenterOffset.Value;
            _arcBandGraphic.MaxAngularSpan = MAX_ANGULAR_SPAN;
            _arcBandGraphic.DpiScale = s;
            _arcBandGraphic.AdaptiveScale = _currentAdaptiveScale.Value;

            WidgetStyleManager st = WidgetStyleManager.Instance;
            Color borderCol = WidgetStyleManager.GetDerivedColor((Color)theme.FrameBorderColor, 0.50f);
            _arcBandGraphic.PlateColor = st.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            _arcBandGraphic.OuterRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Heavy);
            _arcBandGraphic.InnerRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
            _arcBandGraphic.material = st.GetUiMaterial(isText: false);
        }

        private void BuildTickPool(float s, ThemeConfig theme)
        {
            _tickPool.Clear();
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Material textMat = style.GetUiMaterial(isText: true);

            for (int i = 0; i < MAX_VISIBLE_TICKS; i++)
            {
                RectTransform rt = CreateContainer($"TickNode_{i}", transform,
                    new Vector2(30f * s, 30f * s), Vector2.zero);
                GameObject root = rt.gameObject;

                // 刻度线 (高清晰度纯净矢量线元)
                Image lineImg = CreateChild<Image>("Line", root.transform,
                    new Vector2(1.5f * s, 6.5f * s), Vector2.zero);
                lineImg.sprite = null;
                lineImg.raycastTarget = false;
                RectTransform lineRt = lineImg.rectTransform;
                lineRt.pivot = new Vector2(0.5f, 0.5f);
                lineImg.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

                // 刻度数字 / 罗盘主方位 (高质量 18px 超采样字模)
                const int baseFontSize = 18;
                Text lbl = UIFactory.CreateText(root.transform, "Label", "", baseFontSize, TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme), null, addShadow: false);
                lbl.fontStyle = FontStyle.Bold;
                lbl.raycastTarget = false;
                if (textMat != null) lbl.material = textMat;
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = new Vector2(36f, 18f);
                lblRt.anchoredPosition = new Vector2(0f, -14f * s);

                _tickPool.Add(new HeadingTickUI
                {
                    Root = root,
                    Rt = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = lbl,
                    LabelRt = lblRt,
                    CurrentDeg = -999,
                    LastAlpha = -1f,
                    BaseColor = Color.clear,
                    BaseLineColor = style.GetTextColor(TextStyleRole.PrimaryValue, theme)
                });

                root.SetActive(false);
            }
        }

        private void BuildSpeechBubble(float s, ThemeConfig theme)
        {
            float adaptiveScale = _currentAdaptiveScale.Value;
            float effScale = s * adaptiveScale;
            float bubbleW = Mathf.Max(40f, 46f * effScale);
            float bubbleH = Mathf.Max(16f, 18f * effScale);
            Vector2 boxSize = new Vector2(bubbleW, bubbleH);

            float barH = Mathf.Max(2f, 2.2f * effScale);
            float tipSize = Mathf.Max(4.5f, 5.5f * effScale);
            float tipVisualH = tipSize * 0.707f;
            float lubberPosY = _currentRadiusY.Value - _currentYCenterOffset.Value + (4.5f * effScale);
            float bubblePosY = (lubberPosY + barH) + 1.5f + (boxSize.y * 0.5f + tipVisualH - 0.5f * effScale);
            Vector2 bubblePos = new Vector2(0f, bubblePosY);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _bubbleBtn = CreateButton("Heading_SpeechBubble", transform, out _speechBubbleRt, out _bubbleBg,
                boxSize, bubblePos);
            _speechBubbleRoot = _speechBubbleRt.gameObject;
            _bubbleBg.color = Color.clear;

            _bubbleOutline = _speechBubbleRoot.AddComponent<Outline>();
            _bubbleOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, theme);

            if (_bubbleBtn != null)
            {
                _bubbleBtn.transition = Selectable.Transition.None;
            }

            var clickHandler = _speechBubbleRoot.AddComponent<HeadingBubblePointerHandler>();
            clickHandler.OnLeftClick = OnBubbleClicked;
            clickHandler.OnRightClick = () =>
            {
                if (OnToggleReferenceFrameWindowAction != null)
                    OnToggleReferenceFrameWindowAction.Invoke();
                else
                    OnBubbleClicked();
            };

            _speechBubbleRoot.SetTooltip(
                I18n.Tr("WIDGET_NAME_CORE_HEADING_ARC", "PFD 航向指示标尺弧"),
                I18n.Tr("LIB_DESC_HEADING_ARC", "主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。")
            );

            // 气泡框向下尖角指针
            _bubblePointerTip = CreateChild<Image>("Bubble_Pointer_Tip", _speechBubbleRoot.transform,
                new Vector2(tipSize, tipSize), new Vector2(0f, -boxSize.y * 0.5f + 0.5f * effScale));
            _bubblePointerTip.raycastTarget = false;
            GameObject tipObj = _bubblePointerTip.gameObject;
            RectTransform tipRt = _bubblePointerTip.rectTransform;
            tipRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _bubblePointerTip.color = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);

            _bubblePointerOutline = tipObj.AddComponent<Outline>();
            _bubblePointerOutline.effectColor = style.GetCardBorderColor(CardStyleRole.Emphasized, theme);
            _bubblePointerOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 气泡框内部数显读数 (090°) - 等宽纯净居中航向角
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(11.5f * effScale), 10, 20);
            _headingText = UIFactory.CreateText(_speechBubbleRoot.transform, "Heading_Value", "000°", fontSize,
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _headingText.fontStyle = FontStyle.Bold;
            _headingText.raycastTarget = false;
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLubberMark(float s, ThemeConfig theme)
        {
            float adaptiveScale = _currentAdaptiveScale.Value;
            float effScale = s * adaptiveScale;
            float barW = Mathf.Max(10f, 12f * effScale);
            float barH = Mathf.Max(2f, 2.2f * effScale);
            float stemW = Mathf.Max(2f, 2.2f * effScale);
            float stemH = Mathf.Max(4.5f, 5.0f * effScale);
            Vector2 lubberPos = new Vector2(0f, _currentRadiusY.Value - _currentYCenterOffset.Value + (4.5f * effScale));

            _lubberRt = CreateContainer("Lubber_Line_Root", transform,
                new Vector2(barW + 2f, barH + stemH), lubberPos);
            _lubberLineRoot = _lubberRt.gameObject;

            Color lubberColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 顶部横杠 (纯净矢量线元)
            GameObject barObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Bar",
                new Vector2(barW, barH), new Vector2(0f, barH * 0.5f), lubberColor);
            _lubberBar = barObj.GetComponent<Image>();
            _lubberBar.raycastTarget = false;

            // 竖向立柱 (纯净矢量线元)
            GameObject stemObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Stem",
                new Vector2(stemW, stemH), new Vector2(0f, -stemH * 0.5f), lubberColor);
            _lubberStem = stemObj.GetComponent<Image>();
            _lubberStem.raycastTarget = false;
        }

        private void OnBubbleClicked()
        {
            FlightTelemetryContext.Current?.CycleSpeedMode();
            OnCycleHeadingModeAction?.Invoke();
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context) => base.OnUIDrawLoop(ref context);

        protected override void OnRenderState()
        {
            HeadingArcState state = _logic.CurrentState;
            if (!state.HasVessel) return;

            UpdateRotatingCompassRose(state.DisplayedHeading);
            UpdateBubbleHeadingText(state.BubbleDeg);
        }

        private void UpdateBubbleHeadingText(int degInt)
        {
            if (_headingText != null)
            {
                if (_lastBubbleDeg.Update(degInt))
                {
                    _headingText.SetTextSafe(_hdg3DigitStrings[degInt]);
                }
            }
        }

        private void UpdateRotatingCompassRose(float currentHeading, bool force = false)
        {
            if (!force && !_lastRenderedHeading.Update(currentHeading))
            {
                return;
            }
            if (force)
            {
                _lastRenderedHeading.Reset(currentHeading);
            }

            float s = CurrentDpiScale;
            float rx = _currentRadiusX.Value > 0.001f ? _currentRadiusX.Value : (ARC_RADIUS * s);
            float ry = _currentRadiusY.Value > 0.001f ? _currentRadiusY.Value : (ARC_RADIUS * s);
            float yCenterOffset = _currentYCenterOffset.Value > 0.001f ? _currentYCenterOffset.Value : (ARC_Y_CENTER_OFFSET * s);
            float adaptiveScale = _currentAdaptiveScale.Value;

            // 智能密度 LOD: 小分辨率/紧凑尺寸下动态调降刻度密度，根除摩尔纹与重叠拥挤
            float arcPitch5Deg = rx * (5f * Mathf.Deg2Rad);
            bool isCompact = arcPitch5Deg < 7.5f;
            bool isUltraCompact = arcPitch5Deg < 4.5f;
            int tickStep = isCompact ? 10 : 5;

            int centerTickDeg = Mathf.RoundToInt(currentHeading / (float)tickStep) * tickStep;
            int tickIdx = 0;

            if (!_hasCachedArcColors)
            {
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                WidgetStyleManager style = WidgetStyleManager.Instance;
                _cachedTextCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                _cachedSubTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _cachedNorthCol = style.GetCardinalColor(0, theme);
                _cachedEastCol = style.GetCardinalColor(90, theme);
                _cachedSouthCol = style.GetCardinalColor(180, theme);
                _cachedWestCol = style.GetCardinalColor(270, theme);
                _hasCachedArcColors = true;
            }
            Color textCol = _cachedTextCol;
            Color subTickCol = _cachedSubTickCol;

            for (int offsetDeg = -50; offsetDeg <= 50; offsetDeg += tickStep)
            {
                if (tickIdx >= _tickPool.Count) break;

                int tickDeg = centerTickDeg + offsetDeg;
                int normalizedDeg = (tickDeg % 360 + 360) % 360;

                float deltaAngle = Mathf.DeltaAngle(currentHeading, tickDeg);
                if (Mathf.Abs(deltaAngle) > MAX_ANGULAR_SPAN) continue;

                var item = _tickPool[tickIdx];
                item.Root.SetActiveSafe(true);

                float rad = deltaAngle * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                float nx = ry * sin;
                float ny = rx * cos;
                float normLen = Mathf.Sqrt(nx * nx + ny * ny);
                Vector2 normDir = normLen > 0.0001f ? new Vector2(nx / normLen, ny / normLen) : Vector2.up;

                // 刻度挂接于圆弧外缘略微向内 (0.5px)
                Vector2 basePt = new Vector2(sin * rx, cos * ry - yCenterOffset);
                item.Rt.anchoredPosition = basePt + normDir * (4.0f * s * adaptiveScale);

                float normalAngle = Mathf.Atan2(nx, ny) * Mathf.Rad2Deg;
                item.Rt.localEulerAngles = new Vector3(0f, 0f, -normalAngle);

                float edgeAlpha = Mathf.Clamp01((MAX_ANGULAR_SPAN - Mathf.Abs(deltaAngle)) / 8f);

                if (item.CurrentDeg != normalizedDeg)
                {
                    item.CurrentDeg = normalizedDeg;
                    bool isMajor = (normalizedDeg % 10 == 0);
                    bool isCardinal = (normalizedDeg % 90 == 0);

                    if (isCardinal)
                    {
                        string cardStr;
                        switch (normalizedDeg)
                        {
                            case 0: cardStr = "N"; break;
                            case 90: cardStr = "E"; break;
                            case 180: cardStr = "S"; break;
                            default: cardStr = "W"; break;
                        }
                        Color cardCol = GetCachedCardinalColor(normalizedDeg);
                        item.BaseLineColor = cardCol;
                        item.BaseColor = cardCol;

                        float cW = Mathf.Max(1.8f, 2.0f * s * adaptiveScale);
                        float cH = Mathf.Max(6.5f, 7.5f * s * adaptiveScale);
                        item.LineRt.sizeDelta = new Vector2(cW, cH);
                        item.LineRt.anchoredPosition = new Vector2(0f, -cH * 0.5f);
                        item.LabelRt.anchoredPosition = new Vector2(0f, -14.0f * s * adaptiveScale);
                        item.Label.SetTextSafe(cardStr);
                    }
                    else if (isMajor)
                    {
                        item.BaseLineColor = textCol;
                        float mW = Mathf.Max(1.4f, 1.5f * s * adaptiveScale);
                        float mH = Mathf.Max(4.5f, 5.5f * s * adaptiveScale);
                        item.LineRt.sizeDelta = new Vector2(mW, mH);
                        item.LineRt.anchoredPosition = new Vector2(0f, -mH * 0.5f);
                        item.LabelRt.anchoredPosition = new Vector2(0f, -13.5f * s * adaptiveScale);

                        // 30度主数字标注 (超紧凑模式下仅每60度)
                        if (normalizedDeg % 30 == 0 && (!isUltraCompact || normalizedDeg % 60 == 0))
                        {
                            item.BaseColor = textCol;
                            item.Label.SetTextSafe(_tickDegStrings[normalizedDeg]);
                        }
                        else
                        {
                            item.BaseColor = Color.clear;
                            item.Label.SetTextSafe(string.Empty);
                        }
                    }
                    else
                    {
                        item.BaseLineColor = WidgetStyleManager.Weighted(subTickCol, LineWeight.Heavy);
                        item.BaseColor = Color.clear;
                        float subW = Mathf.Max(1.0f, 1.1f * s * adaptiveScale);
                        float subH = Mathf.Max(3.0f, 3.5f * s * adaptiveScale);
                        item.LineRt.sizeDelta = new Vector2(subW, subH);
                        item.LineRt.anchoredPosition = new Vector2(0f, -subH * 0.5f);
                        item.Label.SetTextSafe(string.Empty);
                    }
                }

                if (Mathf.Abs(item.LastAlpha - edgeAlpha) > 0.02f)
                {
                    item.LastAlpha = edgeAlpha;
                    Color finalLineCol = item.BaseLineColor;
                    finalLineCol.a *= edgeAlpha;
                    item.Line.color = finalLineCol;

                    if (!string.IsNullOrEmpty(item.Label.text))
                    {
                        Color finalLblCol = item.BaseColor;
                        finalLblCol.a *= edgeAlpha;
                        item.Label.color = finalLblCol;
                    }
                }

                _tickPool[tickIdx] = item;
                tickIdx++;
            }

            for (int i = tickIdx; i < _tickPool.Count; i++)
            {
                _tickPool[i].Root.SetActiveSafe(false);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _cachedTextCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            _cachedSubTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            _cachedNorthCol = style.GetCardinalColor(0, theme);
            _cachedEastCol = style.GetCardinalColor(90, theme);
            _cachedSouthCol = style.GetCardinalColor(180, theme);
            _cachedWestCol = style.GetCardinalColor(270, theme);
            _hasCachedArcColors = true;

            ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, theme);
            if (_bubblePointerTip != null)
            {
                _bubblePointerTip.color = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            }
            if (_bubblePointerOutline != null)
            {
                _bubblePointerOutline.effectColor = style.GetCardBorderColor(CardStyleRole.Emphasized, theme);
            }
            ApplyText(_headingText, TextStyleRole.PrimaryValue, theme);

            Color lubberColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            if (_lubberBar != null) _lubberBar.color = lubberColor;
            if (_lubberStem != null) _lubberStem.color = lubberColor;

            if (_arcBandGraphic != null)
            {
                Color borderCol = WidgetStyleManager.GetDerivedColor((Color)theme.FrameBorderColor, 0.50f);
                _arcBandGraphic.PlateColor = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);
                _arcBandGraphic.OuterRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Heavy);
                _arcBandGraphic.InnerRimColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
                _arcBandGraphic.material = style.GetUiMaterial(isText: false);
                _arcBandGraphic.SetVerticesDirty();
            }

            Material textMat = style.GetUiMaterial(isText: true);
            for (int i = 0; i < _tickPool.Count; i++)
            {
                var tick = _tickPool[i];
                tick.CurrentDeg = -999;
                tick.LastAlpha = -1f;
                if (tick.Label != null)
                {
                    tick.Label.material = textMat;
                }
                _tickPool[i] = tick;
            }

            if (FlightTelemetryContext.Current != null)
            {
                UpdateRotatingCompassRose((float)FlightTelemetryContext.Current.Heading, force: true);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_bubbleBtn != null)
            {
                _bubbleBtn.onClick.RemoveListener(OnBubbleClicked);
                _bubbleBtn = null;
            }
            base.OnDestroy();
        }
    }

    public class HeadingBubblePointerHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else
            {
                OnLeftClick?.Invoke();
            }
        }
    }
}
