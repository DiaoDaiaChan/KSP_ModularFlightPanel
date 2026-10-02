using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets.Navigation;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代矩形姿态仪 / 导航球主控仪表 (Rectangular Navball / ADI Widget)
    /// 采用现代单 Quad 屏幕空间数学解析光线投射 (Screen-Space Analytic Raymarching) 矩形等角孔径管线，
    /// 完美支持任意长宽比非等比自由调节 (1:1、4:3、16:9、21:9、1:2 等)，保持各向同性等角视场无畸变！
    /// 核心特性：
    /// 1. 矩形孔径纯数学矢量 (ProceduralVector) 与原版贴图采样 (StockTexture) 双模；
    /// 2. 完整集成现有导航球全量 11 种 2D 矢量标线 (Prograde, Retrograde, Normal, Maneuver 等) 及其矩形视口透视投射与边界吸附；
    /// 3. Principia / Stock 6 大参考系动态调色板与平滑过渡动力学 (SURFACE, INERTIAL, LAGRANGE, TARGET, ORBIT, BODY_FIXED)；
    /// 4. 姿态运动角速度 3-DOF 趋势预测引线与翼尖标校；
    /// 5. GPWS 近地大下沉率防撞动态斑马纹与近地平精细游标阶梯；
    /// 6. SAS 目标动态角括号锁定框、水波纹冲击波、机动节点航向流光引导；
    /// 7. 标线悬停航电微卡片与左键锁定 SAS / 右键机动跃迁交互；
    /// 8. 坡度角刻度与滚转/天顶指引指针 (Bank Angle Roll Scale & Sky Pointer)；
    /// 9. 智能自适应物理尺寸契约 (IAdaptiveSizeWidget)，可在编辑模式 GUI 中随意拖拽手柄拉伸长宽比。
    /// </summary>
    /// <summary>
    /// 矩形姿态仪显示模式 (Display Mode)
    /// </summary>
    public enum RectangularNavballMode
    {
        DimensionReducedPFD = 0, // 降维模式：民航PFD 2-DOF 姿态指示器 (俯仰+滚转解耦，航向解耦居中)
        Spherical3D = 1          // 3D模式：三维导航球等角投影在矩形窗口中
    }

    /// <summary>
    /// 矩形姿态仪状态快照 (0 GC 值类型)
    /// </summary>
    public struct RectangularNavballState : IEquatable<RectangularNavballState>
    {
        public bool HasVessel;
        public NavballRenderMode RenderMode;
        public RectangularNavballMode DisplayMode;
        public Texture StockTex;
        public Vector2 TexScale;
        public Vector2 TexOffset;
        public string RefCategory;
        public string RefCategoryUpper;
        public string FrameName;
        public float Heading;

        public bool Equals(RectangularNavballState other)
        {
            return HasVessel == other.HasVessel &&
                   RenderMode == other.RenderMode &&
                   DisplayMode == other.DisplayMode &&
                   ReferenceEquals(StockTex, other.StockTex) &&
                   TexScale == other.TexScale &&
                   TexOffset == other.TexOffset &&
                   RefCategory == other.RefCategory &&
                   RefCategoryUpper == other.RefCategoryUpper &&
                   FrameName == other.FrameName &&
                   Mathf.Abs(Heading - other.Heading) < 0.05f;
        }

        public override bool Equals(object obj) => obj is RectangularNavballState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ (int)RenderMode;
                hash = (hash * 397) ^ (int)DisplayMode;
                hash = (hash * 397) ^ Heading.GetHashCode();
                if (RefCategoryUpper != null) hash = (hash * 397) ^ RefCategoryUpper.GetHashCode();
                if (FrameName != null) hash = (hash * 397) ^ FrameName.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 矩形姿态仪业务解耦大脑 (Headless Widget Logic)
    /// </summary>
    public class RectangularNavballLogic : WidgetLogic<RectangularNavballState>
    {
        public RectangularNavballMode DisplayMode = RectangularNavballMode.DimensionReducedPFD;
        private string _lastRawCat = null;
        private string _cachedCatUpper = null;

        public override void Reset()
        {
            _lastRawCat = null;
            _cachedCatUpper = null;
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            NavballRenderMode renderMode = ThemeManager.Instance?.GlobalRenderMode ?? NavballRenderMode.ProceduralVector;
            if (renderMode == NavballRenderMode.ProceduralBake) renderMode = NavballRenderMode.ProceduralVector;

            Texture stockTex = null;
            Vector2 texScale = Vector2.one;
            Vector2 texOffset = Vector2.zero;
            string rawCat = "SURFACE";
            string frameName = null;

            var hook = NavBallHookService.Provider;
            if (hook != null)
            {
                if (renderMode == NavballRenderMode.StockTexture)
                {
                    stockTex = hook.BallTexture;
                    texScale = hook.TextureScale;
                    texOffset = hook.TextureOffset;
                }

                rawCat = hook.ReferenceFrameCategory;
                if (string.IsNullOrEmpty(rawCat)) rawCat = "SURFACE";
                frameName = hook.FrameName;
            }

            if (!object.ReferenceEquals(rawCat, _lastRawCat) && rawCat != _lastRawCat)
            {
                _lastRawCat = rawCat;
                _cachedCatUpper = rawCat.ToUpperInvariant();
            }

            float heading = (hook != null && hook.HasStockNavBall) ? hook.HeadingAngle : ((telemetry != null) ? (float)telemetry.Heading : 0f);

            CurrentState = new RectangularNavballState
            {
                HasVessel = telemetry != null && telemetry.HasVessel,
                RenderMode = renderMode,
                DisplayMode = DisplayMode,
                StockTex = stockTex,
                TexScale = texScale,
                TexOffset = texOffset,
                RefCategory = rawCat,
                RefCategoryUpper = _cachedCatUpper ?? rawCat,
                FrameName = frameName,
                Heading = heading
            };
        }
    }

    [DefaultExecutionOrder(10000)]
    [AlwaysFullPower]
    [FlightWidget("rect_navball", "rectangular_navball", Category = WidgetCategory.Navigation, DisplayName = "矩形姿态球", Description = "现代矩形姿态仪 / 导航球，集成超清矢量/贴图渲染、全量导航标线，支持在编辑GUI中任意非等比调节长宽比。", DefaultWidgetId = "nav.rect_navball", DefaultX = 0f, DefaultY = 0f, HighFrequency = true, AlwaysFullPower = true, ExactIds = new[] { "nav.rect_navball", "nav.rectangular_navball", "core.rect_navball" })]
    public class RectangularNavballWidget : BaseFlightWidget, IAdaptiveSizeWidget, IPointerClickHandler, INavballMarkerWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Critical;
        public override Vector2 BaseSize => new Vector2(240f, 160f);
        protected override bool AutoCreateCardFrame => false;

        private readonly RectangularNavballLogic _logic = new RectangularNavballLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // ── IAdaptiveSizeWidget 自适应尺寸契约 ──
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(120f, 80f);
        public Vector2 MaxBaseSize => new Vector2(960f, 640f);

        public override FlightNavballPipeline GetNavballPipeline()
        {
            return new FlightNavballPipeline(_sphereMaterial, null, null, false, 512);
        }

        private float _currentWidth = 240f;
        private float _currentHeight = 160f;
        private float _aspectRatio = 1.5f;

        // ── 遥测与参考系缓存 ──
        private readonly Cached<string> _lastRawFrameName = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastAppliedCategoryForFrame = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastAppliedFrameText = new Cached<string>(string.Empty);
        private readonly Cached<Color> _lastAppliedFrameColor = new Cached<Color>(Color.clear);

        // ── 渲染显示与材质 ──
        private RawImage _displayImage;
        private Material _sphereMaterial;
        private GameObject _bezelBox;
        private RectTransform _bezelRt;
        private Outline _bezelOutline;
        private GameObject _crosshair;
        private RectTransform _crosshairRt;
        private Image _reticleImage;
        private GameObject _headingBox;
        private Text _headingText;
        private Text _frameText;

        // ── 2D 矢量 HUD 标记覆盖层 ──
        private readonly Dictionary<string, Image> _markerImages = new Dictionary<string, Image>(12);
        private RectTransform _markerContainer;

        // ── 着色器属性 Uniform 缓存 ──
        private static readonly int _PropRenderMode = Shader.PropertyToID("_RenderMode");
        private static readonly int _PropSphereInvRotation = Shader.PropertyToID("_SphereInvRotation");
        private static readonly int _PropApertureShape = Shader.PropertyToID("_ApertureShape");
        private static readonly int _PropAspectRatio = Shader.PropertyToID("_AspectRatio");
        private static readonly int _PropCornerRadius = Shader.PropertyToID("_CornerRadius");
        private static readonly int _PropFovScale = Shader.PropertyToID("_FovScale");
        private static readonly int _PropPfdMode = Shader.PropertyToID("_PfdMode");

        private static readonly int _PropSkyColor = Shader.PropertyToID("_SkyColor");
        private static readonly int _PropGroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int _PropSkyZenithColor = Shader.PropertyToID("_SkyZenithColor");
        private static readonly int _PropSkyHorizonColor = Shader.PropertyToID("_SkyHorizonColor");
        private static readonly int _PropGroundHorizonColor = Shader.PropertyToID("_GroundHorizonColor");
        private static readonly int _PropGroundNadirColor = Shader.PropertyToID("_GroundNadirColor");

        private static readonly int _PropEquatorColor = Shader.PropertyToID("_EquatorColor");
        private static readonly int _PropHorizonLineColor = Shader.PropertyToID("_HorizonLineColor");
        private static readonly int _PropGridColor = Shader.PropertyToID("_GridColor");
        private static readonly int _PropPitchLadderColor = Shader.PropertyToID("_PitchLadderColor");
        private static readonly int _PropHeadingLineColor = Shader.PropertyToID("_HeadingLineColor");
        private static readonly int _PropLabelColor = Shader.PropertyToID("_LabelColor");
        private static readonly int _PropLabelOutlineColor = Shader.PropertyToID("_LabelOutlineColor");
        private static readonly int _PropDotColor = Shader.PropertyToID("_DotColor");
        private static readonly int _PropDotDensity = Shader.PropertyToID("_DotDensity");
        private static readonly int _PropDotMinRadius = Shader.PropertyToID("_DotMinRadius");
        private static readonly int _PropDotMaxRadius = Shader.PropertyToID("_DotMaxRadius");
        private static readonly int _PropRimColor = Shader.PropertyToID("_RimColor");
        private static readonly int _PropAtmosphereGlowColor = Shader.PropertyToID("_AtmosphereGlowColor");
        private static readonly int _PropEmissionIntensity = Shader.PropertyToID("_EmissionIntensity");
        private static readonly int _PropNumeralRollAngle = Shader.PropertyToID("_NumeralRollAngle");
        private static readonly int _PropNumeralUprightMode = Shader.PropertyToID("_NumeralUprightMode");
        private static readonly int _PropNumeralTangentComp = Shader.PropertyToID("_NumeralTangentComp");

        private static readonly int _PropDetailScale = Shader.PropertyToID("_DetailScale");
        private static readonly int _PropFramePattern = Shader.PropertyToID("_FramePattern");
        private static readonly int _PropFramePatternOld = Shader.PropertyToID("_FramePatternOld");
        private static readonly int _PropFrameTransitionProgress = Shader.PropertyToID("_FrameTransitionProgress");
        private static readonly int _PropTrendRotation = Shader.PropertyToID("_TrendRotation");
        private static readonly int _PropTrendStrength = Shader.PropertyToID("_TrendStrength");

        private static readonly int _PropMarkerAvoid0 = Shader.PropertyToID("_MarkerAvoid0");
        private static readonly int _PropMarkerAvoid1 = Shader.PropertyToID("_MarkerAvoid1");
        private static readonly int _PropMarkerAvoid2 = Shader.PropertyToID("_MarkerAvoid2");
        private static readonly int _PropMarkerAvoid3 = Shader.PropertyToID("_MarkerAvoid3");

        private static readonly int _PropGroundHazardAlert = Shader.PropertyToID("_GroundHazardAlert");
        private static readonly int _PropVernierScaleDetail = Shader.PropertyToID("_VernierScaleDetail");

        private RectangularNavballMode _displayMode = RectangularNavballMode.DimensionReducedPFD;
        private readonly Cached<float> _lastUploadedPfdMode = new Cached<float>(-1f);
        private float _uploadedRenderMode = -1f;

        // ── 坐标系平滑切变过渡动力学与多参考系动态调色板 ──
        private const float FrameTransitionDuration = 0.45f;
        private bool _isFrameTransitioning = false;
        private float _frameTransitionTimer = 999f;
        private Quaternion _transitionStartRot = Quaternion.identity;
        private Quaternion _displayedAttitudeRotation = Quaternion.identity;
        private NavballFramePalette _transitionStartPalette;
        private float _transitionStartFramePattern = 0f;
        private float _currentFramePattern = 0f;
        private readonly Cached<string> _lastFrameName = new Cached<string>(null);
        private readonly Cached<string> _lastSpeedMode = new Cached<string>(null);

        private float _frameBadgeAnimTimer = 999f;
        private readonly Dictionary<string, Vector2> _renderedMarkerPositions = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _currentMarkerDirs = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _transitionStartMarkerDirs = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;
        private bool _isPaletteLerping = false;
        private readonly Cached<string> _lastFrameCategory = new Cached<string>(null);

        // ── 姿态角速度低通滤波与趋势预测 ──
        private readonly Cached<Quaternion> _previousAttitudeRotation = new Cached<Quaternion>(Quaternion.identity);
        private bool _hasPreviousAttitudeRotation = false;
        private Vector3 _smoothedAngularVelocity = Vector3.zero;
        private Quaternion _filteredTrendRotation = Quaternion.identity;
        private float _attitudeTrendStrength = 0f;
        private int _trendFrame = -1;

        // ── 视网膜细节与几何适配缓存 ──
        private readonly Vector3[] _displayCorners = new Vector3[4];
        private int _screenWidth = -1;
        private int _screenHeight = -1;
        private float _detailScale = -1f;

        // ── 坡度角刻度与滚转指引指针 ──
        private readonly List<Image> _bankAngleTicks = new List<Image>();
        private readonly List<float> _bankTickAngles = new List<float>();
        private RectTransform _bankRollPointerRoot;
        private Image _bankRollPointerImg;

        // ── SAS 联动指示与点击反馈 ──
        private RectTransform _sasLockReticleRt;
        private Image _sasLockReticleImage;
        private RectTransform _sasRippleRt;
        private Image _sasRippleImage;
        private float _rippleTimer = 999f;
        private Color _sasRippleColor = WidgetStyleManager.NeutralOpaque;

        // ── 机动节点航向流光引导 ──
        private GameObject _maneuverGuideContainer;
        private readonly Image[] _guidanceChevrons = new Image[4];
        private Quaternion _currentAttitudeRotation = Quaternion.identity;

        // ── 矢量标悬停交互与悬浮提示 ──
        private readonly Dictionary<string, NavballMarkerClickHandler> _markerHandlers = new Dictionary<string, NavballMarkerClickHandler>(StringComparer.OrdinalIgnoreCase);
        private GameObject _markerHoverTooltipObj;
        private RectTransform _markerHoverTooltipRt;
        private Image _markerHoverTooltipBg;
        private Outline _markerHoverTooltipOutline;
        private Text _markerHoverTooltipText;
        private Text _markerHoverTooltipSub;
        private string _activeHoveredMarkerKey = null;
        private Vector2 _activeHoveredMarkerPos = Vector2.zero;

        // ── 标线渲染槽位 ──
        private class MarkerSlot
        {
            public string Key;
            public NavballMarkerType MarkerType;
            public Image Image;
            public RectTransform RectTransform;
            public NavballMarkerClickHandler Handler;
            public Vector3 CurrentDir;
            public Vector2 RenderedPos;
            public Vector2 LastStableBearing;
        }

        private MarkerSlot[] _markerSlots;
        private readonly Vector4[] _cachedAvoidVectors = new Vector4[4];
        private Vector4 _uploadedAvoid0;
        private Vector4 _uploadedAvoid1;
        private Vector4 _uploadedAvoid2;
        private Vector4 _uploadedAvoid3;
        private Vector4 _uploadedInvRot;
        private string _evaluatedPaletteCategory;
        private ThemeConfig _evaluatedPaletteTheme;

        // ── 航向与姿态死区更新缓存 ──
        private readonly Cached<int> _lastHeadingValue = new Cached<int>(-1);
        private readonly Cached<string> _lastHeadingCategory = new Cached<string>(null);
        private readonly CachedFloat _lastRollPointerAngle = new CachedFloat(-9999f, 0.05f);
        private readonly CachedFloat _lastBankTicksAlpha = new CachedFloat(-1f, 0.01f);
        private bool _detailScaleDirty = true;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float baseW = BaseSize.x * s;
            float baseH = BaseSize.y * s;

            float effX = config != null ? config.EffectiveScaleX : 1.0f;
            float effY = config != null ? config.EffectiveScaleY : 1.0f;
            float initW = baseW * effX;
            float initH = baseH * effY;

            _currentWidth = Mathf.Max(MinBaseSize.x * s, initW);
            _currentHeight = Mathf.Max(MinBaseSize.y * s, initH);
            _aspectRatio = _currentWidth / _currentHeight;

            RectTransform.sizeDelta = new Vector2(_currentWidth, _currentHeight);
            ApplyCanvasIsolation(true);

            string modeStr = GetTemplateChannel("MODE", "PFD");
            if ("3d".Equals(modeStr, StringComparison.OrdinalIgnoreCase) || "spherical".Equals(modeStr, StringComparison.OrdinalIgnoreCase))
            {
                _displayMode = RectangularNavballMode.Spherical3D;
            }
            else
            {
                _displayMode = RectangularNavballMode.DimensionReducedPFD;
            }
            _logic.DisplayMode = _displayMode;

            // 1. 屏幕空间数学解析光线投射矩形姿态仪 (Screen-Space Rectangular ADI)
            _displayImage = CreateChild<RawImage>("Rect_RaymarchImage", transform,
                new Vector2(_currentWidth, _currentHeight), Vector2.zero);
            _displayImage.texture = Texture2D.whiteTexture;
            _displayImage.raycastTarget = false;

            Shader targetShader = AssetLoader.RaymarchShader ?? Shader.Find("ModularFlightPanel/NavballRaymarch") ?? Shader.Find("UI/Default");
            _sphereMaterial = new Material(targetShader);
            _displayImage.material = _sphereMaterial;

            // 初始上传矩形孔径与长宽比
            _sphereMaterial.SetFloat(_PropApertureShape, 1.0f);
            _sphereMaterial.SetFloat(_PropAspectRatio, _aspectRatio);
            _sphereMaterial.SetFloat(_PropCornerRadius, 0.04f);
            _sphereMaterial.SetFloat(_PropFovScale, 1.0f);
            float pfdModeVal = (_displayMode == RectangularNavballMode.DimensionReducedPFD) ? 1.0f : 0.0f;
            _sphereMaterial.SetFloat(_PropPfdMode, pfdModeVal);
            _lastUploadedPfdMode.Reset(pfdModeVal);

            // 2. 2D 矢量标线层
            CreateMarkerOverlayLayer(transform, s);

            // 3. 中心准星十字瞄准标
            CreateCrosshair(transform, s, theme);

            // 4. 极细航电金属质感圆角矩形外框
            CreateBezelBox(transform, _currentWidth, _currentHeight, s, theme);

            // 5. 顶部滚转坡度弧与指引指针
            CreateBankAngleScale(transform, _currentWidth, _currentHeight, s, theme);

            // 6. 底部航向读数盒与参考系模式标牌
            CreateHeadingBox(transform, _currentHeight, s, theme);

            // 标线层置于最顶层，确保鼠标悬停与点击事件不被背景包边遮挡
            if (_markerContainer != null)
            {
                _markerContainer.SetAsLastSibling();
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetGraphicViewportControl("rect_navball_viewport", "矩形视口", _displayImage != null ? _displayImage.gameObject : gameObject, _displayImage));
            if (_crosshair != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "crosshair", "准星标线", _crosshair));
            if (_headingBox != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "heading_box", "航向盒读数", _headingBox));
            if (_bezelBox != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel_frame", "外边框", _bezelBox));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
            UpdateProceduralDetailScale();
        }

        protected override void OnScaleChanged(float targetScale, float relativeRatio)
        {
            base.OnScaleChanged(targetScale, relativeRatio);
            _detailScale = -1f;
            _detailScaleDirty = true;
            UpdateProceduralDetailScale();
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            float w = Mathf.Max(MinBaseSize.x * s, pixelSize.x);
            float h = Mathf.Max(MinBaseSize.y * s, pixelSize.y);
            _currentWidth = w;
            _currentHeight = h;
            _aspectRatio = w / h;

            if (_displayImage != null)
            {
                _displayImage.rectTransform.SetSizeDeltaSafe(new Vector2(w, h));
            }
            if (_sphereMaterial != null)
            {
                _sphereMaterial.SetFloat(_PropAspectRatio, _aspectRatio);
                _sphereMaterial.SetFloat(_PropApertureShape, 1.0f);
            }
            if (_markerContainer != null)
            {
                _markerContainer.SetSizeDeltaSafe(new Vector2(w, h));
            }
            if (_bezelBox != null && _bezelRt != null)
            {
                _bezelRt.SetSizeDeltaSafe(new Vector2(w + 2f * s, h + 2f * s));
            }
            if (_headingBox != null)
            {
                _headingBox.GetComponent<RectTransform>().SetAnchoredPositionSafe(new Vector2(0f, -h * 0.5f - 13f * s));
            }
            if (_bankRollPointerRoot != null)
            {
                _bankRollPointerRoot.SetAnchoredPositionSafe(new Vector2(0f, h * 0.5f - 18f * s));
            }
            UpdateBankAngleTicksPositions();
            _detailScaleDirty = true;
            UpdateProceduralDetailScale();
        }

        private void CreateBezelBox(Transform parent, float width, float height, float dpiScale, ThemeConfig theme)
        {
            _bezelBox = UIFactory.CreatePanel(parent, "RectNavball_Bezel",
                new Vector2(width + 2f * dpiScale, height + 2f * dpiScale),
                Vector2.zero, WidgetStyleManager.NeutralTransparent);
            _bezelRt = _bezelBox.GetComponent<RectTransform>();
            Image bezelImg = _bezelBox.GetComponent<Image>();
            bezelImg.raycastTarget = false;
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            if (bezelImg != null && uiMat != null) bezelImg.material = uiMat;

            _bezelOutline = _bezelBox.AddComponent<Outline>();
            Color border = theme.FrameBorderColor;
            _bezelOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
            _bezelOutline.effectDistance = new Vector2(1.2f * dpiScale, 1.2f * dpiScale);
        }

        private void CreateCrosshair(Transform parent, float dpiScale, ThemeConfig theme)
        {
            RectTransform crosshairRt = CreateContainer("RectNavball_Crosshair_Reticle", parent,
                new Vector2(76f * dpiScale, 32f * dpiScale), Vector2.zero);
            _crosshair = crosshairRt.gameObject;
            _crosshairRt = crosshairRt;

            _reticleImage = CreateChild<Image>("Reticle_Image", _crosshair.transform,
                crosshairRt.sizeDelta, Vector2.zero);
            _reticleImage.sprite = NavballMarkerFactory.GetReticleSprite();
            _reticleImage.color = WidgetStyleManager.NeutralOpaque;
            _reticleImage.raycastTarget = false;
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            if (uiMat != null) _reticleImage.material = uiMat;
        }

        private void CreateBankAngleScale(Transform parent, float width, float height, float s, ThemeConfig theme)
        {
            _bankAngleTicks.Clear();
            _bankTickAngles.Clear();
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            float[] angles = new float[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 45f, -45f, 60f, -60f };

            // 动态高精度滚转/天顶指引指针 (Roll / Sky Pointer)
            _bankRollPointerRoot = CreateContainer("BankRollPointer_Root", parent,
                Vector2.zero, new Vector2(0f, height * 0.5f - 18f * s));

            _bankRollPointerImg = CreateChild<Image>("PointerNeedle", _bankRollPointerRoot,
                new Vector2(10f * s, 12f * s), new Vector2(0f, 0f));
            _bankRollPointerImg.sprite = NavballMarkerFactory.GetRollPointerSprite();
            _bankRollPointerImg.color = theme.HorizonLineColor;
            _bankRollPointerImg.raycastTarget = false;
            if (uiMat != null) _bankRollPointerImg.material = uiMat;

            foreach (float deg in angles)
            {
                bool isZero = Mathf.Abs(deg) < 0.1f;
                bool isMajor = Mathf.Abs(Mathf.Abs(deg) - 30f) < 0.1f || isZero;
                bool isWarn = Mathf.Abs(deg) >= 44f;

                float tickLen = isZero ? (6.5f * s) : (isMajor ? 6f * s : (isWarn ? 5f * s : 3.5f * s));
                float tickWidth = isZero ? (2.2f * s) : (isMajor ? 1.8f * s : 1.2f * s);

                Image img = CreateChild<Image>($"BankTick_{deg:F0}", parent,
                    new Vector2(tickWidth, tickLen), Vector2.zero);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -deg);
                img.raycastTarget = false;
                img.color = isWarn ? theme.WarningColor : (isZero ? theme.HorizonLineColor : theme.GridColor);
                if (uiMat != null) img.material = uiMat;

                _bankAngleTicks.Add(img);
                _bankTickAngles.Add(deg);
            }

            UpdateBankAngleTicksPositions();
        }

        private void UpdateBankAngleTicksPositions()
        {
            float s = CurrentDpiScale;
            float topY = _currentHeight * 0.5f - 18f * s;
            float arcRadius = 42f * s;

            for (int i = 0; i < _bankAngleTicks.Count; i++)
            {
                Image img = _bankAngleTicks[i];
                if (img == null) continue;
                float deg = _bankTickAngles[i];
                float rad = deg * Mathf.Deg2Rad;
                Vector2 tickPos = new Vector2(Mathf.Sin(rad) * arcRadius, topY + (Mathf.Cos(rad) - 1.0f) * arcRadius);
                img.rectTransform.SetAnchoredPositionSafe(tickPos);
            }
        }

        private void CreateHeadingBox(Transform parent, float height, float dpiScale, ThemeConfig theme)
        {
            _headingBox = UIFactory.CreatePanel(parent, "RectNavball_Heading_Box",
                new Vector2(114f * dpiScale, 20f * dpiScale),
                new Vector2(0f, -height * 0.5f - 13f * dpiScale),
                theme.FrameBgColor);

            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);
            Image hImg = _headingBox.GetComponent<Image>();
            if (hImg != null && uiMat != null) hImg.material = uiMat;

            Outline hOutline = _headingBox.AddComponent<Outline>();
            hOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Normal);
            hOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _headingText = UIFactory.CreateText(_headingBox.transform, "Heading_Value",
                "HDG 000°", Mathf.Max(10, Mathf.RoundToInt(11f * dpiScale)),
                TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            _headingText.rectTransform.anchoredPosition = new Vector2(-16f * dpiScale, 0f);
            _headingText.rectTransform.sizeDelta = new Vector2(76f * dpiScale, 20f * dpiScale);
            if (txtMat != null) _headingText.material = txtMat;

            _frameText = UIFactory.CreateText(_headingBox.transform, "Frame_Label",
                I18n.Tr("WIDGET_NAV_SURF", "表面"), Mathf.Max(7, Mathf.RoundToInt(8f * dpiScale)),
                TextAnchor.MiddleRight, theme.AccentSecondary);
            _frameText.rectTransform.anchoredPosition = new Vector2(38f * dpiScale, 0f);
            _frameText.rectTransform.sizeDelta = new Vector2(34f * dpiScale, 18f * dpiScale);
            if (txtMat != null) _frameText.material = txtMat;
        }

        private void CreateMarkerOverlayLayer(Transform parent, float dpiScale)
        {
            RectTransform rt = CreateContainer("RectNavball_Marker_Overlay", parent,
                new Vector2(_currentWidth, _currentHeight), Vector2.zero);
            _markerContainer = rt;

            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);

            string[] markerKeys = new string[]
            {
                "prograde", "retrograde", "normal", "antinormal",
                "radialin", "radialout", "target", "antitarget",
                "maneuver", "velocity_vector", "anti_velocity_vector"
            };

            float markerSize = 26f * dpiScale;
            _markerHandlers.Clear();
            _renderedMarkerPositions.Clear();
            _currentMarkerDirs.Clear();
            _transitionStartMarkerDirs.Clear();
            _markerSlots = new MarkerSlot[markerKeys.Length];
            for (int i = 0; i < markerKeys.Length; i++)
            {
                string k = markerKeys[i];
                _renderedMarkerPositions[k] = Vector2.zero;
                _currentMarkerDirs[k] = Vector3.zero;
                Image img = CreateChild<Image>($"Marker_{k}", _markerContainer,
                    new Vector2(markerSize, markerSize), Vector2.zero);
                GameObject mObj = img.gameObject;
                img.sprite = NavballMarkerFactory.GetMarkerSprite(k);
                img.color = WidgetStyleManager.NeutralOpaque;
                img.raycastTarget = true;
                if (uiMat != null) img.material = uiMat;

                var clickHandler = mObj.AddComponent<NavballMarkerClickHandler>();
                clickHandler.MarkerKey = k;
                clickHandler.Widget = this;

                mObj.SetActive(false);
                _markerImages[k] = img;
                _markerHandlers[k] = clickHandler;

                _markerSlots[i] = new MarkerSlot
                {
                    Key = k,
                    MarkerType = NavballMarkerHelper.GetMarkerType(k),
                    Image = img,
                    RectTransform = img.rectTransform,
                    Handler = clickHandler,
                    LastStableBearing = Vector2.up
                };
            }

            // 1. 机动节点航向流光引导箭头容器
            _maneuverGuideContainer = CreateContainer("Maneuver_Guide_Flow", _markerContainer,
                Vector2.zero, Vector2.zero).gameObject;

            Sprite chevSprite = NavballMarkerFactory.GetGuidanceChevronSprite();
            for (int i = 0; i < _guidanceChevrons.Length; i++)
            {
                Image cImg = CreateChild<Image>($"GuideChevron_{i}", _maneuverGuideContainer.transform,
                    new Vector2(14f * dpiScale, 14f * dpiScale), Vector2.zero);
                cImg.sprite = chevSprite;
                cImg.color = WidgetStyleManager.NeutralOpaque;
                cImg.raycastTarget = false;
                if (uiMat != null) cImg.material = uiMat;
                _guidanceChevrons[i] = cImg;
            }
            _maneuverGuideContainer.SetActive(false);

            // 2. SAS 动态角括号锁定框
            _sasLockReticleImage = CreateChild<Image>("Active_SAS_Reticle", _markerContainer,
                new Vector2(36f * dpiScale, 36f * dpiScale), Vector2.zero);
            _sasLockReticleRt = _sasLockReticleImage.rectTransform;
            _sasLockReticleImage.sprite = NavballMarkerFactory.GetSASLockReticleSprite();
            _sasLockReticleImage.color = WidgetStyleManager.NeutralOpaque;
            _sasLockReticleImage.raycastTarget = false;
            if (uiMat != null) _sasLockReticleImage.material = uiMat;
            _sasLockReticleImage.gameObject.SetActive(false);

            // 3. 点击冲击波扩散环
            _sasRippleImage = CreateChild<Image>("SAS_Shockwave_Ripple", _markerContainer,
                new Vector2(48f * dpiScale, 48f * dpiScale), Vector2.zero);
            _sasRippleRt = _sasRippleImage.rectTransform;
            _sasRippleImage.sprite = NavballMarkerFactory.GetShockwaveSprite();
            _sasRippleImage.color = WidgetStyleManager.NeutralOpaque;
            _sasRippleImage.raycastTarget = false;
            if (uiMat != null) _sasRippleImage.material = uiMat;
            _sasRippleImage.gameObject.SetActive(false);

            // 4. 光标悬停微航电提示卡片
            _markerHoverTooltipBg = CreateChild<Image>("Marker_Hover_Tooltip", _markerContainer,
                new Vector2(104f * dpiScale, 26f * dpiScale), Vector2.zero);
            GameObject tipObj = _markerHoverTooltipBg.gameObject;
            _markerHoverTooltipRt = _markerHoverTooltipBg.rectTransform;
            _markerHoverTooltipBg.color = WidgetStyleManager.NeutralOpaque;
            _markerHoverTooltipBg.raycastTarget = false;
            if (uiMat != null) _markerHoverTooltipBg.material = uiMat;

            _markerHoverTooltipOutline = tipObj.AddComponent<Outline>();
            _markerHoverTooltipOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);
            _markerHoverTooltipOutline.effectColor = WidgetStyleManager.NeutralOpaque;

            _markerHoverTooltipText = UIFactory.CreateText(tipObj.transform, "Tooltip_Title", I18n.Tr("SAS_MODE_PROGRADE", "顺行"),
                Mathf.Max(8, Mathf.RoundToInt(8.5f * dpiScale)), TextAnchor.MiddleCenter, WidgetStyleManager.NeutralOpaque);
            _markerHoverTooltipText.rectTransform.anchoredPosition = new Vector2(0f, 4.5f * dpiScale);
            _markerHoverTooltipText.rectTransform.sizeDelta = new Vector2(100f * dpiScale, 12f * dpiScale);
            _markerHoverTooltipText.raycastTarget = false;
            if (txtMat != null) _markerHoverTooltipText.material = txtMat;

            _markerHoverTooltipSub = UIFactory.CreateText(tipObj.transform, "Tooltip_Sub", I18n.Tr("WIDGET_NAV_CLICK_ENGAGE_SAS", "点击：启用 SAS"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * dpiScale)), TextAnchor.MiddleCenter, WidgetStyleManager.NeutralOpaque);
            _markerHoverTooltipSub.rectTransform.anchoredPosition = new Vector2(0f, -6f * dpiScale);
            _markerHoverTooltipSub.rectTransform.sizeDelta = new Vector2(100f * dpiScale, 10f * dpiScale);
            _markerHoverTooltipSub.raycastTarget = false;
            if (txtMat != null) _markerHoverTooltipSub.material = txtMat;

            _markerHoverTooltipObj = tipObj;
            tipObj.SetActive(false);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;

            _paletteInitialized = false;
            _lastFrameCategory.Reset(null);

            var mode = ThemeManager.Instance != null ? ThemeManager.Instance.GlobalRenderMode : NavballRenderMode.ProceduralVector;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            Shader targetShader = AssetLoader.RaymarchShader ?? Shader.Find("ModularFlightPanel/NavballRaymarch") ?? Shader.Find("UI/Default");

            if (_sphereMaterial != null)
            {
                if (_sphereMaterial.shader != targetShader && targetShader != null)
                {
                    _sphereMaterial.shader = targetShader;
                }

                // 传递模式给 Raymarch Shader: 0=Stock, 1=Vector
                float renderModeVal = (mode == NavballRenderMode.StockTexture) ? 0f : 1f;
                _sphereMaterial.SetFloat(_PropRenderMode, renderModeVal);
                _uploadedRenderMode = renderModeVal;
                _sphereMaterial.SetFloat(_PropApertureShape, 1.0f);
                _sphereMaterial.SetFloat(_PropAspectRatio, _aspectRatio);
                _sphereMaterial.SetFloat(_PropCornerRadius, 0.04f);
                _sphereMaterial.SetFloat(_PropFovScale, 1.0f);
                float pfdModeVal = (_displayMode == RectangularNavballMode.DimensionReducedPFD) ? 1.0f : 0.0f;
                _sphereMaterial.SetFloat(_PropPfdMode, pfdModeVal);
                _lastUploadedPfdMode.Reset(pfdModeVal);

                if (mode == NavballRenderMode.StockTexture)
                {
                    var hook = NavBallHookService.Provider;
                    Texture stockTex = hook?.BallTexture;
                    if (stockTex != null)
                    {
                        if (_displayImage != null) _displayImage.texture = stockTex;
                        _sphereMaterial.mainTexture = stockTex;
                        _sphereMaterial.SetTextureScale("_MainTex", hook.TextureScale);
                        _sphereMaterial.SetTextureOffset("_MainTex", hook.TextureOffset);
                    }
                }

                if (_sphereMaterial.HasProperty(_PropNumeralUprightMode)) _sphereMaterial.SetFloat(_PropNumeralUprightMode, 0.0f);
                if (_sphereMaterial.HasProperty(_PropNumeralRollAngle)) _sphereMaterial.SetFloat(_PropNumeralRollAngle, 0.0f);
                if (_sphereMaterial.HasProperty(_PropNumeralTangentComp)) _sphereMaterial.SetFloat(_PropNumeralTangentComp, 1.0f);

                // 天地与网格色彩统一注入
                Color skyZenith = theme.SkyColor;
                Color skyHrz = (Color)theme.AccentSecondary;

                if (_sphereMaterial.HasProperty(_PropSkyColor)) _sphereMaterial.SetColor(_PropSkyColor, skyZenith);
                if (_sphereMaterial.HasProperty(_PropGroundColor)) _sphereMaterial.SetColor(_PropGroundColor, theme.GroundColor);
                if (_sphereMaterial.HasProperty(_PropSkyZenithColor)) _sphereMaterial.SetColor(_PropSkyZenithColor, skyZenith);
                if (_sphereMaterial.HasProperty(_PropSkyHorizonColor)) _sphereMaterial.SetColor(_PropSkyHorizonColor, skyHrz);
                if (_sphereMaterial.HasProperty(_PropGroundHorizonColor)) _sphereMaterial.SetColor(_PropGroundHorizonColor, theme.GroundColor);
                if (_sphereMaterial.HasProperty(_PropGroundNadirColor)) _sphereMaterial.SetColor(_PropGroundNadirColor, WidgetStyleManager.Darken(theme.GroundColor, 0.4f));

                if (_sphereMaterial.HasProperty(_PropEquatorColor)) _sphereMaterial.SetColor(_PropEquatorColor, theme.HorizonLineColor);
                if (_sphereMaterial.HasProperty(_PropHorizonLineColor)) _sphereMaterial.SetColor(_PropHorizonLineColor, theme.HorizonLineColor);

                if (_sphereMaterial.HasProperty(_PropGridColor)) _sphereMaterial.SetColor(_PropGridColor, theme.GridColor);
                if (_sphereMaterial.HasProperty(_PropPitchLadderColor)) _sphereMaterial.SetColor(_PropPitchLadderColor, theme.GridColor);
                if (_sphereMaterial.HasProperty(_PropHeadingLineColor)) _sphereMaterial.SetColor(_PropHeadingLineColor, theme.AccentSecondary);

                if (_sphereMaterial.HasProperty(_PropDotColor)) _sphereMaterial.SetColor(_PropDotColor, theme.DitherDotColor);
                if (_sphereMaterial.HasProperty(_PropDotDensity)) _sphereMaterial.SetFloat(_PropDotDensity, theme.DotDensity > 0 ? theme.DotDensity : 38f);
                if (_sphereMaterial.HasProperty(_PropDotMinRadius)) _sphereMaterial.SetFloat(_PropDotMinRadius, theme.DotMinRadius > 0 ? theme.DotMinRadius : 0.04f);
                if (_sphereMaterial.HasProperty(_PropDotMaxRadius)) _sphereMaterial.SetFloat(_PropDotMaxRadius, theme.DotMaxRadius > 0 ? theme.DotMaxRadius : 0.46f);

                if (_sphereMaterial.HasProperty(_PropRimColor)) _sphereMaterial.SetColor(_PropRimColor, theme.RimGlowColor);
                if (_sphereMaterial.HasProperty(_PropAtmosphereGlowColor)) _sphereMaterial.SetColor(_PropAtmosphereGlowColor, theme.RimGlowColor);
                if (_sphereMaterial.HasProperty(_PropEmissionIntensity)) _sphereMaterial.SetFloat(_PropEmissionIntensity, 1.25f);

                if (_sphereMaterial.HasProperty(_PropLabelColor)) _sphereMaterial.SetColor(_PropLabelColor, theme.TextPrimaryColor);
                if (_sphereMaterial.HasProperty(_PropLabelOutlineColor)) _sphereMaterial.SetColor(_PropLabelOutlineColor, theme.TextInverseColor);
            }

            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);

            if (_bezelBox != null)
            {
                var bImg = _bezelBox.GetComponent<Image>();
                if (bImg != null && uiMat != null) bImg.material = uiMat;
            }
            if (_bezelOutline != null)
            {
                _bezelOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong);
            }
            if (_headingText != null)
            {
                if (txtMat != null) _headingText.material = txtMat;
                ApplyText(_headingText, TextStyleRole.PrimaryValue, theme);
            }
            if (_frameText != null)
            {
                if (txtMat != null) _frameText.material = txtMat;
                ApplyText(_frameText, TextStyleRole.Label, theme);
            }
            if (_bankRollPointerImg != null)
            {
                _bankRollPointerImg.color = theme.HorizonLineColor;
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            if (_displayImage == null || !_displayImage.gameObject.activeInHierarchy) return;
            var state = _logic.CurrentState;

            float targetPfdMode = (state.DisplayMode == RectangularNavballMode.DimensionReducedPFD) ? 1.0f : 0.0f;
            if (_sphereMaterial != null && _lastUploadedPfdMode.Update(targetPfdMode))
            {
                _sphereMaterial.SetFloat(_PropPfdMode, targetPfdMode);
            }

            var mode = state.RenderMode;
            float targetRenderMode = (mode == NavballRenderMode.StockTexture) ? 0f : 1f;
            if (_sphereMaterial != null && _uploadedRenderMode != targetRenderMode)
            {
                _uploadedRenderMode = targetRenderMode;
                _sphereMaterial.SetFloat(_PropRenderMode, targetRenderMode);
            }

            if (mode == NavballRenderMode.StockTexture)
            {
                Texture stockTex = state.StockTex;
                if (stockTex != null && _displayImage != null && _displayImage.texture != stockTex)
                {
                    _displayImage.texture = stockTex;
                    _sphereMaterial.mainTexture = stockTex;
                    _sphereMaterial.SetTextureScale("_MainTex", state.TexScale);
                    _sphereMaterial.SetTextureOffset("_MainTex", state.TexOffset);
                }
            }

            // 更新航向读数盒与参考系模式显示
            string catUpper = state.RefCategoryUpper ?? "SURFACE";
            if (_headingText != null)
            {
                float hdg = state.Heading;
                int iHdg = (Mathf.RoundToInt(hdg) % 360 + 360) % 360;

                bool hdgDirty = _lastHeadingValue.Update(iHdg);
                bool catDirty = _lastHeadingCategory.Update(catUpper);
                if (hdgDirty || catDirty)
                {
                    switch (catUpper)
                    {
                        case "INERTIAL":
                            int raH = Mathf.FloorToInt(iHdg / 15f);
                            int raM = Mathf.FloorToInt(((iHdg % 15) / 15f) * 60f);
                            SetTextIfChanged(_headingText, $"RA {raH:D2}h{raM:D2}m");
                            break;
                        case "BODY_FIXED":
                        case "BODY_SURFACE":
                            SetTextIfChanged(_headingText, CacheManager.FastLon(iHdg));
                            break;
                        case "ORBIT":
                        case "ORBITAL":
                            SetTextIfChanged(_headingText, CacheManager.FastObt(iHdg));
                            break;
                        case "TARGET":
                            SetTextIfChanged(_headingText, CacheManager.FastTgt(iHdg));
                            break;
                        case "LAGRANGE":
                        case "BARYCENTRIC":
                            SetTextIfChanged(_headingText, $"LAG {iHdg:D3}°");
                            break;
                        default:
                            SetTextIfChanged(_headingText, CacheManager.FastHdg(iHdg));
                            break;
                    }
                }
            }

            if (_frameText != null)
            {
                bool rawFrameDirty = _lastRawFrameName.Update(state.FrameName);
                bool appliedCatDirty = _lastAppliedCategoryForFrame.Update(catUpper);
                if (rawFrameDirty || appliedCatDirty)
                {
                    string frame = state.FrameName;
                    if (string.IsNullOrEmpty(frame))
                    {
                        switch (catUpper)
                        {
                            case "INERTIAL": frame = "INERT"; break;
                            case "BODY_FIXED":
                            case "BODY_SURFACE": frame = "FIXED"; break;
                            case "ORBIT":
                            case "ORBITAL": frame = "ORBIT"; break;
                            case "TARGET": frame = "TARGT"; break;
                            case "LAGRANGE":
                            case "BARYCENTRIC": frame = "LAGRN"; break;
                            default: frame = "SURF"; break;
                        }
                    }
                    else if (frame.Length > 5)
                    {
                        frame = frame.Substring(0, 5).ToUpperInvariant();
                    }
                    else
                    {
                        frame = frame.ToUpperInvariant();
                    }

                    if (_lastAppliedFrameText.Update(frame))
                    {
                        SetTextIfChanged(_frameText, frame);
                    }
                }

                if (_frameBadgeAnimTimer >= 0.40f)
                {
                    ThemeConfig curTheme = WidgetStyleManager.Instance?.CurrentTheme ?? ThemeManager.Instance?.CurrentTheme;
                    Color accentCol = GetFrameAccentColor(catUpper, curTheme);
                    if (_lastAppliedFrameColor.Update(accentCol))
                    {
                        _frameText.color = accentCol;
                    }
                }
            }
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastUploadedPfdMode.Reset(-1f);
        }

        protected virtual void LateUpdate()
        {
            if (!gameObject.activeInHierarchy) return;
            if (_displayImage == null || !_displayImage.enabled || !_displayImage.gameObject.activeInHierarchy) return;

            SyncAttitudeAndVisuals();
            SyncMarkers();
            IFlightTelemetry curTelem = FlightTelemetryContext.Current;
            UpdateRollPointer(curTelem, _currentAttitudeRotation);
            UpdateSASAndGuidanceVisuals(curTelem);
            UpdateFrameBadgeAnimation();
        }

        private void UpdateProceduralDetailScale()
        {
            if (_displayImage == null || _sphereMaterial == null) return;
            int sw = Screen.width;
            int sh = Screen.height;
            if (!_detailScaleDirty && _detailScale >= 0f && sw == _screenWidth && sh == _screenHeight) return;

            _detailScaleDirty = false;
            _screenWidth = sw;
            _screenHeight = sh;

            Canvas canvas = _displayImage.canvas;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransform imageRect = _displayImage.rectTransform;
            imageRect.GetWorldCorners(_displayCorners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[0]);
            Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[1]);
            Vector2 bottomRight = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[3]);
            float displayPixels = Mathf.Max(Vector2.Distance(bottomLeft, topLeft), Vector2.Distance(bottomLeft, bottomRight));
            float detailScale = Mathf.InverseLerp(88f, 240f, displayPixels);
            if (Mathf.Abs(detailScale - _detailScale) > 0.01f)
            {
                _detailScale = detailScale;
                _sphereMaterial.SetFloat(_PropDetailScale, detailScale);
            }
        }

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);
            ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;

            Quaternion rawRot;
            if (hasHook)
            {
                rawRot = hook.ViewRotation;
            }
            else
            {
                IFlightTelemetry telem = FlightTelemetryContext.Current;
                rawRot = (telem != null) ? telem.AttitudeRotation : Quaternion.identity;
            }

            string category = hook?.ReferenceFrameCategory ?? "SURFACE";
            string frameName = hook?.FrameName ?? "";
            IFlightTelemetry curTelem = FlightTelemetryContext.Current;
            string speedMode = curTelem?.SpeedModeName ?? "";

            bool isFrameSwitch = false;
            if (_paletteInitialized)
            {
                if (_lastFrameCategory.Value != null && !category.Equals(_lastFrameCategory.Value, StringComparison.OrdinalIgnoreCase))
                {
                    isFrameSwitch = true;
                }
                else if (_lastSpeedMode.Value != null && !speedMode.Equals(_lastSpeedMode.Value, StringComparison.OrdinalIgnoreCase))
                {
                    isFrameSwitch = true;
                }
                else if (_lastFrameName.Value != null && !string.IsNullOrEmpty(frameName) && !frameName.Equals(_lastFrameName.Value, StringComparison.OrdinalIgnoreCase))
                {
                    isFrameSwitch = true;
                }
            }

            if (isFrameSwitch)
            {
                _isFrameTransitioning = true;
                _frameTransitionTimer = 0f;
                _transitionStartRot = _displayedAttitudeRotation;
                _transitionStartPalette = _currentPalette;
                _transitionStartFramePattern = _currentFramePattern;

                _hasPreviousAttitudeRotation = false;
                _filteredTrendRotation = Quaternion.identity;
                _attitudeTrendStrength = 0f;

                Color frameAccent = GetFrameAccentColor(category, curTheme);
                TriggerShockwaveRipple(Vector2.zero, frameAccent);
                _frameBadgeAnimTimer = 0f;

                _transitionStartMarkerDirs.Clear();
                foreach (var kvp in _currentMarkerDirs)
                {
                    _transitionStartMarkerDirs[kvp.Key] = kvp.Value;
                }
            }

            _lastFrameCategory.Update(category);
            _lastFrameName.Update(frameName);
            _lastSpeedMode.Update(speedMode);

            float newPattern = GetFramePatternCode(category);
            float transitionProgress = 1.0f;
            float eased = 1.0f;

            if (_isFrameTransitioning)
            {
                _frameTransitionTimer += Time.unscaledDeltaTime;
                transitionProgress = Mathf.Clamp01(_frameTransitionTimer / FrameTransitionDuration);
                eased = 1.0f - Mathf.Pow(1.0f - transitionProgress, 3.0f);

                _displayedAttitudeRotation = Quaternion.Slerp(_transitionStartRot, rawRot, eased);
                _currentFramePattern = Mathf.Lerp(_transitionStartFramePattern, newPattern, eased);

                if (transitionProgress >= 1.0f)
                {
                    _isFrameTransitioning = false;
                    _displayedAttitudeRotation = rawRot;
                    _currentFramePattern = newPattern;
                }
            }
            else
            {
                // 高保真自适应姿态防抖滤波 (AHRS Adaptive Anti-Jitter Filter)
                // 彻底消除 PhysX 物理步进与 SAS PID 闭环微振颤 (0.01°~0.04°)，稳态如磐石，机动零延迟
                if (_displayedAttitudeRotation == Quaternion.identity)
                {
                    _displayedAttitudeRotation = rawRot;
                }
                else
                {
                    float angleDelta = Quaternion.Angle(_displayedAttitudeRotation, rawRot);
                    float dt = Time.unscaledDeltaTime;

                    if (angleDelta > 30f || !Application.isPlaying || dt <= 0.0001f)
                    {
                        _displayedAttitudeRotation = rawRot;
                    }
                    else if (angleDelta < 0.035f)
                    {
                        // 稳态微颤死区守卫
                    }
                    else
                    {
                        float tRate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.035f, 0.28f, angleDelta));
                        float filterSpeed = Mathf.Lerp(14f, 48f, tRate);
                        float slerpT = Mathf.Clamp01(dt * filterSpeed);
                        _displayedAttitudeRotation = Quaternion.Slerp(_displayedAttitudeRotation, rawRot, slerpT);
                    }
                }
                _currentFramePattern = newPattern;
            }

            _currentAttitudeRotation = _displayedAttitudeRotation;
            UpdateAttitudeTrend(_displayedAttitudeRotation);

            if (_sphereMaterial != null)
            {
                Quaternion invRot = Quaternion.Inverse(_displayedAttitudeRotation);
                Vector4 invRotVec = new Vector4(invRot.x, invRot.y, invRot.z, invRot.w);
                if (invRotVec != _uploadedInvRot)
                {
                    _uploadedInvRot = invRotVec;
                    _sphereMaterial.SetVector(_PropSphereInvRotation, invRotVec);
                }

                _sphereMaterial.SetFloat(_PropApertureShape, 1.0f);
                _sphereMaterial.SetFloat(_PropAspectRatio, _aspectRatio);
                _sphereMaterial.SetFloat(_PropFramePatternOld, _transitionStartFramePattern);
                _sphereMaterial.SetFloat(_PropFramePattern, newPattern);
                _sphereMaterial.SetFloat(_PropFrameTransitionProgress, _isFrameTransitioning ? eased : 1.0f);
            }

            if (_evaluatedPaletteCategory != category || _evaluatedPaletteTheme != curTheme)
            {
                _evaluatedPaletteCategory = category;
                _evaluatedPaletteTheme = curTheme;
                _targetPalette = GetPaletteForCategory(category, curTheme);
            }
            if (!_paletteInitialized)
            {
                _currentPalette = _targetPalette;
                _transitionStartPalette = _targetPalette;
                _paletteInitialized = true;
                UploadPaletteToMaterial(_currentPalette);
            }
            else if (_isFrameTransitioning)
            {
                _currentPalette = LerpPalette(_transitionStartPalette, _targetPalette, eased);
                UploadPaletteToMaterial(_currentPalette);
            }
            else if (_isPaletteLerping)
            {
                float dt = Time.deltaTime;
                float lerpFactor = (!Application.isPlaying || dt <= 0.0001f) ? 1.0f : Mathf.Clamp01(dt * 8.0f);
                _currentPalette = LerpPalette(_currentPalette, _targetPalette, lerpFactor);
                UploadPaletteToMaterial(_currentPalette);

                if (IsPaletteEqual(ref _currentPalette, ref _targetPalette))
                {
                    _currentPalette = _targetPalette;
                    _isPaletteLerping = false;
                }
            }

            if (_sphereMaterial != null)
            {
                // GPWS 警示与精细游标阶梯
                float hazardAlert = 0.0f;
                if (curTelem != null)
                {
                    double rAlt = curTelem.AltitudeAGL;
                    double vSpeed = curTelem.VerticalSpeed;
                    if (rAlt > 0.1 && rAlt < 800.0 && vSpeed < -18.0)
                    {
                        float sinkHazard = Mathf.Clamp01((float)(-vSpeed - 18.0) / 45.0f);
                        float altHazard = Mathf.Clamp01((float)(800.0 - rAlt) / 750.0f);
                        hazardAlert = sinkHazard * altHazard;
                    }
                }
                _sphereMaterial.SetFloat(_PropGroundHazardAlert, hazardAlert);

                float vernier = 1.0f;
                if (curTelem != null)
                {
                    float pitchRate = Mathf.Abs(_smoothedAngularVelocity.x);
                    vernier = 1.0f - Mathf.Clamp01(pitchRate / 18.0f);
                }
                _sphereMaterial.SetFloat(_PropVernierScaleDetail, vernier);
            }
        }

        private void SyncMarkers()
        {
            var hook = NavBallHookService.Provider;
            int avoidIdx = 0;
            for (int i = 0; i < 4; i++) _cachedAvoidVectors[i] = Vector4.zero;

            float halfW = _currentWidth * 0.5f;
            float halfH = _currentHeight * 0.5f;
            float ar = _aspectRatio;
            float s = CurrentDpiScale;
            float marginX = 14f * s;
            float marginY = 14f * s;
            float boundX = halfW - marginX;
            float boundY = halfH - marginY;

            for (int slotIdx = 0; slotIdx < _markerSlots.Length; slotIdx++)
            {
                var slot = _markerSlots[slotIdx];
                var img = slot.Image;
                if (img == null) continue;
                string key = slot.Key;

                Vector3 dir = Vector3.forward;
                bool isVisible = false;
                bool hasDir = false;

                if (hook != null)
                {
                    hasDir = hook.GetMarkerDirection(key, out dir, out isVisible);
                }
                if (!hasDir && NavBallHookService.MarkerDirectionFallback != null)
                {
                    hasDir = NavBallHookService.MarkerDirectionFallback(key, out dir, out isVisible);
                }

                if (hasDir && (isVisible || dir.sqrMagnitude > 0.001f))
                {
                    Vector3 currentDir = dir;
                    if (_isFrameTransitioning && _transitionStartMarkerDirs.TryGetValue(key, out Vector3 startDir) && startDir.sqrMagnitude > 0.001f)
                    {
                        float transT = Mathf.Clamp01(_frameTransitionTimer / FrameTransitionDuration);
                        float eased = 1.0f - Mathf.Pow(1.0f - transT, 3.0f);
                        currentDir = Vector3.Slerp(startDir, dir, eased).normalized;
                    }
                    slot.CurrentDir = currentDir;
                    _currentMarkerDirs[key] = currentDir;

                    float alpha;
                    float targetScale;
                    Vector2 rawMarkerPos;

                    if (currentDir.z >= 0.04f)
                    {
                        // 1. 前向可见半球：正交/透视投影在矩形仪表正面
                        alpha = 1.0f;
                        targetScale = Mathf.Lerp(0.85f, 1.0f, Mathf.Clamp01(currentDir.z + 0.2f));

                        float rawX, rawY;
                        if (_displayMode == RectangularNavballMode.DimensionReducedPFD)
                        {
                            // 现代民航 PFD 严密线性等角投影 (垂直视场角 55°，纵向显示 ±27.5° 黄金跨度)
                            // 与着色器 PFD 度规 100% 严格一致，各向同性像素比例，长宽比自由伸缩无畸变
                            float fovMul = 1.0f;
                            float degPerUnit = 27.5f * fovMul;
                            float pitchAngle = Mathf.Asin(Mathf.Clamp(currentDir.y, -1f, 1f)) * Mathf.Rad2Deg;
                            float yawAngle = Mathf.Atan2(currentDir.x, Mathf.Max(0.001f, currentDir.z)) * Mathf.Rad2Deg;
                            rawY = (pitchAngle / degPerUnit) * halfH;
                            rawX = (yawAngle / degPerUnit) * halfH;
                        }
                        else
                        {
                            // 3D 球面等角正交外接球投影
                            float rSphere = Mathf.Sqrt(ar * ar + 1.0f) * 1.02f;
                            rawX = currentDir.x * (rSphere * halfH);
                            rawY = currentDir.y * (rSphere * halfH);
                        }

                        if (Mathf.Abs(rawX) <= boundX && Mathf.Abs(rawY) <= boundY)
                        {
                            rawMarkerPos = new Vector2(rawX, rawY);
                        }
                        else
                        {
                            float scaleX = Mathf.Abs(rawX) > 0.001f ? boundX / Mathf.Abs(rawX) : 1f;
                            float scaleY = Mathf.Abs(rawY) > 0.001f ? boundY / Mathf.Abs(rawY) : 1f;
                            float edgeScale = Mathf.Min(scaleX, scaleY);
                            rawMarkerPos = new Vector2(rawX * edgeScale, rawY * edgeScale);
                        }

                        Vector2 bearing = new Vector2(currentDir.x, currentDir.y);
                        if (bearing.sqrMagnitude > 0.006f)
                        {
                            slot.LastStableBearing = bearing.normalized;
                        }
                    }
                    else
                    {
                        // 2. 背向半球与超出范围：吸附在仪表边框内缘，平滑过渡并淡化显示 (Backside Rim Clamping & Fade)
                        float tDepth = Mathf.Clamp01(-currentDir.z);
                        alpha = Mathf.Lerp(0.85f, 0.40f, tDepth);
                        targetScale = Mathf.Lerp(0.85f, 0.65f, tDepth);

                        Vector2 bearing = new Vector2(currentDir.x, currentDir.y);
                        float bearingMag = bearing.magnitude;

                        Vector2 normBearing;
                        if (bearingMag >= 0.12f)
                        {
                            slot.LastStableBearing = bearing / bearingMag;
                            normBearing = slot.LastStableBearing;
                        }
                        else
                        {
                            if (slot.LastStableBearing == Vector2.zero)
                            {
                                slot.LastStableBearing = Vector2.up;
                            }
                            if (bearingMag > 0.03f)
                            {
                                Vector2 instantNorm = bearing / bearingMag;
                                slot.LastStableBearing = Vector2.MoveTowards(slot.LastStableBearing, instantNorm, Time.unscaledDeltaTime * 2.5f).normalized;
                            }
                            normBearing = slot.LastStableBearing;
                        }

                        if (bearingMag < 0.06f)
                        {
                            alpha *= Mathf.Lerp(0.35f, 1.0f, bearingMag / 0.06f);
                        }

                        float scaleX = Mathf.Abs(normBearing.x) > 0.001f ? boundX / Mathf.Abs(normBearing.x) : 9999f;
                        float scaleY = Mathf.Abs(normBearing.y) > 0.001f ? boundY / Mathf.Abs(normBearing.y) : 9999f;
                        float edgeDist = Mathf.Min(scaleX, scaleY);
                        rawMarkerPos = normBearing * edgeDist;
                    }

                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                    // 2. 标线位置亚像素自适应防抖滤波 (Marker Spatial Deadband & Anti-Jitter)
                    Vector2 renderedPos;
                    if (slot.RenderedPos == Vector2.zero)
                    {
                        renderedPos = rawMarkerPos;
                    }
                    else
                    {
                        float posDeltaSqr = (rawMarkerPos - slot.RenderedPos).sqrMagnitude;
                        if (posDeltaSqr < 0.10f) // < 0.31px 死区防抖
                        {
                            renderedPos = slot.RenderedPos;
                        }
                        else if (posDeltaSqr < 4.0f) // 0.31px ~ 2.0px 微抖平滑滤波
                        {
                            float filterT = Mathf.Clamp01(Time.unscaledDeltaTime * 28f);
                            renderedPos = Vector2.Lerp(slot.RenderedPos, rawMarkerPos, filterT);
                        }
                        else
                        {
                            renderedPos = rawMarkerPos;
                        }
                    }

                    slot.RenderedPos = renderedPos;
                    _renderedMarkerPositions[key] = renderedPos;

                    slot.RectTransform.SetAnchoredPositionSafe(renderedPos);
                    slot.RectTransform.SetLocalScaleSafe(new Vector3(targetScale, targetScale, 1.0f));

                    Color c = img.color;
                    if (Mathf.Abs(c.a - alpha) > 0.02f)
                    {
                        c.a = alpha;
                        img.color = c;
                    }

                    if (avoidIdx < 4 && currentDir.z > 0.25f && alpha > 0.85f)
                    {
                        float normX = renderedPos.x / halfW;
                        float normY = renderedPos.y / halfH;
                        _cachedAvoidVectors[avoidIdx++] = new Vector4(normX, normY, 0.16f, 1.0f);
                    }
                }
                else
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                }
            }

            if (_sphereMaterial != null)
            {
                if (_cachedAvoidVectors[0] != _uploadedAvoid0) { _uploadedAvoid0 = _cachedAvoidVectors[0]; _sphereMaterial.SetVector(_PropMarkerAvoid0, _uploadedAvoid0); }
                if (_cachedAvoidVectors[1] != _uploadedAvoid1) { _uploadedAvoid1 = _cachedAvoidVectors[1]; _sphereMaterial.SetVector(_PropMarkerAvoid1, _uploadedAvoid1); }
                if (_cachedAvoidVectors[2] != _uploadedAvoid2) { _uploadedAvoid2 = _cachedAvoidVectors[2]; _sphereMaterial.SetVector(_PropMarkerAvoid2, _uploadedAvoid2); }
                if (_cachedAvoidVectors[3] != _uploadedAvoid3) { _uploadedAvoid3 = _cachedAvoidVectors[3]; _sphereMaterial.SetVector(_PropMarkerAvoid3, _uploadedAvoid3); }
            }
        }

        private void UpdateRollPointer(IFlightTelemetry telem, Quaternion rot)
        {
            if (_bankRollPointerRoot == null) return;
            float rollDeg = 0f;
            if (telem != null)
            {
                rollDeg = (float)telem.Roll;
            }
            else
            {
                Vector3 fwd = rot * Vector3.forward;
                Vector3 up = rot * Vector3.up;
                rollDeg = -Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg;
            }

            if (_lastRollPointerAngle.Update(rollDeg))
            {
                _bankRollPointerRoot.localRotation = Quaternion.Euler(0f, 0f, -rollDeg);
            }
        }

        private void UpdateSASAndGuidanceVisuals(IFlightTelemetry telem)
        {
            if (_sasRippleImage != null && _sasRippleImage.gameObject.activeSelf)
            {
                _rippleTimer += Time.unscaledDeltaTime;
                if (_rippleTimer >= 0.45f)
                {
                    _sasRippleImage.gameObject.SetActive(false);
                }
                else
                {
                    float t = _rippleTimer / 0.45f;
                    float scale = Mathf.Lerp(0.5f, 2.2f, t);
                    _sasRippleRt.SetLocalScaleSafe(new Vector3(scale, scale, 1f));
                    Color c = _sasRippleColor;
                    c.a = Mathf.Lerp(0.95f, 0f, t);
                    _sasRippleImage.color = c;
                }
            }

            FlightSASMode sasMode = telem != null ? telem.CurrentSASMode : FlightSASMode.StabilityAssist;
            string targetMarkerKey = GetMarkerKeyForSASMode(sasMode);

            if (!string.IsNullOrEmpty(targetMarkerKey) && _markerImages.TryGetValue(targetMarkerKey, out Image targetImg) && targetImg != null && targetImg.gameObject.activeSelf)
            {
                if (!_sasLockReticleImage.gameObject.activeSelf) _sasLockReticleImage.gameObject.SetActive(true);
                _sasLockReticleRt.SetAnchoredPositionSafe(targetImg.rectTransform.anchoredPosition);
                _sasLockReticleRt.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 45f);
            }
            else
            {
                if (_sasLockReticleImage.gameObject.activeSelf) _sasLockReticleImage.gameObject.SetActive(false);
            }

            UpdateManeuverGuidance();
        }

        private void UpdateManeuverGuidance()
        {
            if (_maneuverGuideContainer == null) return;
            bool hasManeuver = _markerImages.TryGetValue("maneuver", out Image manImg) && manImg != null && manImg.gameObject.activeSelf;
            if (!hasManeuver)
            {
                if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
                return;
            }

            Vector2 manPos = manImg.rectTransform.anchoredPosition;
            float dist = manPos.magnitude;
            float maxDim = Mathf.Max(_currentWidth, _currentHeight) * 0.6f;

            if (dist > 8f && dist < maxDim)
            {
                if (!_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(true);
                float angleDeg = Mathf.Atan2(manPos.y, manPos.x) * Mathf.Rad2Deg - 90f;
                Quaternion chevronRot = Quaternion.Euler(0f, 0f, angleDeg);
                Color chevronCol = NavballMarkerFactory.GetGuidanceFlowColor();

                for (int i = 0; i < _guidanceChevrons.Length; i++)
                {
                    Image chev = _guidanceChevrons[i];
                    if (chev == null) continue;
                    float phase = ((Time.unscaledTime * 1.5f + i * 0.25f) % 1.0f);
                    float t = 0.18f + 0.68f * phase;
                    chev.rectTransform.SetAnchoredPositionSafe(manPos * t);
                    chev.rectTransform.localRotation = chevronRot;
                    float alpha = Mathf.Sin(phase * Mathf.PI) * 0.85f;
                    Color c = chevronCol;
                    c.a = alpha;
                    chev.color = c;
                }
            }
            else
            {
                if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
            }
        }

        public void TriggerShockwaveRipple(Vector2 pos, Color col)
        {
            if (_sasRippleImage == null) return;
            _sasRippleRt.SetAnchoredPositionSafe(pos);
            _sasRippleColor = col;
            _rippleTimer = 0f;
            _sasRippleImage.gameObject.SetActive(true);
            _sasRippleRt.SetLocalScaleSafe(new Vector3(0.5f, 0.5f, 1f));
            Color c = col;
            c.a = 0.95f;
            _sasRippleImage.color = c;
        }

        private void UpdateFrameBadgeAnimation()
        {
            if (_frameText == null) return;
            var hook = NavBallHookService.Provider;
            string category = hook?.ReferenceFrameCategory ?? "SURFACE";
            ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;

            if (_frameBadgeAnimTimer < 0.40f)
            {
                _frameBadgeAnimTimer += Time.unscaledDeltaTime;
                float bt = Mathf.Clamp01(_frameBadgeAnimTimer / 0.35f);
                float bScale = Mathf.Lerp(1.28f, 1.0f, 1.0f - Mathf.Pow(1.0f - bt, 2.0f));
                _frameText.rectTransform.SetLocalScaleSafe(new Vector3(bScale, bScale, 1.0f));
                Color fCol = GetFrameAccentColor(category, curTheme);
                fCol.a = Mathf.Lerp(0.5f, 1.0f, bt);
                _frameText.color = fCol;
            }
            else
            {
                _frameText.rectTransform.SetLocalScaleSafe(Vector3.one);
            }
        }

        private static string GetMarkerKeyForSASMode(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.Prograde: return "prograde";
                case FlightSASMode.Retrograde: return "retrograde";
                case FlightSASMode.Normal: return "normal";
                case FlightSASMode.Antinormal: return "antinormal";
                case FlightSASMode.RadialIn: return "radialin";
                case FlightSASMode.RadialOut: return "radialout";
                case FlightSASMode.Target: return "target";
                case FlightSASMode.AntiTarget: return "antitarget";
                case FlightSASMode.Maneuver: return "maneuver";
                default: return null;
            }
        }

        private static Color GetFrameAccentColor(string category, ThemeConfig theme)
        {
            if (theme == null) return WidgetStyleManager.NeutralOpaque;
            switch (category)
            {
                case "ORBIT":
                case "ORBITAL":
                case "BODY_DIRECTION":
                    return theme.WarningColor;
                case "TARGET":
                    return theme.DangerColor;
                case "LAGRANGE":
                case "BARYCENTRIC":
                    return theme.AccentMagenta;
                case "INERTIAL":
                    return theme.AccentSecondary;
                case "BODY_FIXED":
                case "BODY_SURFACE":
                case "SURFACE":
                    return theme.AccentPrimary;
                default:
                    return theme.AccentPrimary;
            }
        }

        private NavballFramePalette GetPaletteForCategory(string category, ThemeConfig theme)
        {
            switch (category)
            {
                case "INERTIAL":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.AccentSecondary, theme);
                case "LAGRANGE":
                case "BARYCENTRIC":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.AccentMagenta, theme);
                case "TARGET":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.DangerColor, theme);
                case "ORBIT":
                case "ORBITAL":
                case "BODY_DIRECTION":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.WarningColor, theme);
                case "BODY_FIXED":
                case "BODY_SURFACE":
                case "SURFACE":
                default:
                    return WidgetStyleManager.Instance.GetNavballSurfacePalette(theme);
            }
        }

        private static bool FastColorEquals(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f &&
                   Mathf.Abs(a.g - b.g) < 0.002f &&
                   Mathf.Abs(a.b - b.b) < 0.002f &&
                   Mathf.Abs(a.a - b.a) < 0.002f;
        }

        private static bool IsPaletteEqual(ref NavballFramePalette a, ref NavballFramePalette b)
        {
            return FastColorEquals(a.SkyZenith, b.SkyZenith) &&
                   FastColorEquals(a.SkyHorizon, b.SkyHorizon) &&
                   FastColorEquals(a.GroundHorizon, b.GroundHorizon) &&
                   FastColorEquals(a.GroundNadir, b.GroundNadir) &&
                   FastColorEquals(a.Equator, b.Equator) &&
                   FastColorEquals(a.PitchLadder, b.PitchLadder) &&
                   FastColorEquals(a.HeadingLine, b.HeadingLine) &&
                   FastColorEquals(a.Rim, b.Rim);
        }

        private void UploadPaletteToMaterial(NavballFramePalette palette)
        {
            if (_sphereMaterial == null) return;
            _sphereMaterial.SetColor(_PropSkyZenithColor, palette.SkyZenith);
            _sphereMaterial.SetColor(_PropSkyHorizonColor, palette.SkyHorizon);
            _sphereMaterial.SetColor(_PropGroundHorizonColor, palette.GroundHorizon);
            _sphereMaterial.SetColor(_PropGroundNadirColor, palette.GroundNadir);
            _sphereMaterial.SetColor(_PropEquatorColor, palette.Equator);
            _sphereMaterial.SetColor(_PropPitchLadderColor, palette.PitchLadder);
            _sphereMaterial.SetColor(_PropHeadingLineColor, palette.HeadingLine);
            _sphereMaterial.SetColor(_PropRimColor, palette.Rim);
        }

        private static NavballFramePalette LerpPalette(NavballFramePalette a, NavballFramePalette b, float t)
        {
            return new NavballFramePalette
            {
                SkyZenith = Color.Lerp(a.SkyZenith, b.SkyZenith, t),
                SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, t),
                GroundHorizon = Color.Lerp(a.GroundHorizon, b.GroundHorizon, t),
                GroundNadir = Color.Lerp(a.GroundNadir, b.GroundNadir, t),
                Equator = Color.Lerp(a.Equator, b.Equator, t),
                PitchLadder = Color.Lerp(a.PitchLadder, b.PitchLadder, t),
                HeadingLine = Color.Lerp(a.HeadingLine, b.HeadingLine, t),
                Rim = Color.Lerp(a.Rim, b.Rim, t)
            };
        }

        private static float GetFramePatternCode(string category)
        {
            switch (category?.ToUpperInvariant())
            {
                case "INERTIAL": return 1f;
                case "LAGRANGE":
                case "BARYCENTRIC": return 2f;
                case "TARGET": return 3f;
                case "ORBIT":
                case "ORBITAL":
                case "BODY_DIRECTION": return 4f;
                case "BODY_SURFACE":
                case "BODY_FIXED": return 5f;
                default: return 0f;
            }
        }

        private void UpdateAttitudeTrend(Quaternion currentRotation)
        {
            if (_trendFrame == Time.frameCount) return;
            _trendFrame = Time.frameCount;

            float dt = Time.unscaledDeltaTime;
            float targetStrength = 0f;
            Quaternion targetTrendRotation = Quaternion.identity;

            if (_isFrameTransitioning)
            {
                _hasPreviousAttitudeRotation = false;
                _filteredTrendRotation = Quaternion.identity;
                _attitudeTrendStrength = 0f;
                if (_sphereMaterial != null)
                {
                    _sphereMaterial.SetFloat(_PropTrendStrength, 0f);
                }
                return;
            }

            if (_hasPreviousAttitudeRotation && dt > 0.001f && dt < 0.25f)
            {
                Quaternion delta = currentRotation * Quaternion.Inverse(_previousAttitudeRotation.Value);
                if (delta.w < 0f)
                {
                    delta.x = -delta.x;
                    delta.y = -delta.y;
                    delta.z = -delta.z;
                    delta.w = -delta.w;
                }
                delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (angleDeg > 180f) angleDeg -= 360f;

                float speedDegPerSec = Mathf.Abs(angleDeg) / dt;
                Vector3 rawAngVel = (axis.sqrMagnitude > 0.0001f) ? (axis.normalized * (angleDeg * Mathf.Deg2Rad / dt)) : Vector3.zero;

                float smoothT = Mathf.Clamp01(dt * 12.0f);
                _smoothedAngularVelocity = Vector3.Lerp(_smoothedAngularVelocity, rawAngVel, smoothT);

                if (speedDegPerSec > 0.8f)
                {
                    float leadSeconds = 1.0f;
                    float leadAngle = (_smoothedAngularVelocity.magnitude * Mathf.Rad2Deg) * leadSeconds;
                    leadAngle = Mathf.Clamp(leadAngle, 0f, 40f);
                    targetTrendRotation = Quaternion.AngleAxis(leadAngle, _smoothedAngularVelocity.normalized);
                    targetStrength = Mathf.InverseLerp(1.2f, 7.5f, speedDegPerSec);
                }
            }

            _previousAttitudeRotation.Update(currentRotation);
            _hasPreviousAttitudeRotation = true;

            float filterT = Mathf.Clamp01(dt * 8.0f);
            _filteredTrendRotation = Quaternion.Slerp(_filteredTrendRotation, targetTrendRotation, filterT);
            _attitudeTrendStrength = Mathf.Lerp(_attitudeTrendStrength, targetStrength, filterT);

            if (_sphereMaterial != null)
            {
                Vector4 tRotVec = new Vector4(_filteredTrendRotation.x, _filteredTrendRotation.y, _filteredTrendRotation.z, _filteredTrendRotation.w);
                _sphereMaterial.SetVector(_PropTrendRotation, tRotVec);
                _sphereMaterial.SetFloat(_PropTrendStrength, _attitudeTrendStrength);
            }
        }

        public void OnMarkerHoverEnter(string markerKey, Vector2 pos)
        {
            _activeHoveredMarkerKey = markerKey;
            _activeHoveredMarkerPos = pos;
            if (_markerHoverTooltipObj != null && _markerHoverTooltipText != null && _markerHoverTooltipSub != null)
            {
                _markerHoverTooltipObj.SetActive(true);
                _markerHoverTooltipRt.SetAnchoredPositionSafe(new Vector2(pos.x, pos.y + 24f * CurrentDpiScale));
                string title = markerKey.ToUpperInvariant();
                switch (markerKey.ToLowerInvariant())
                {
                    case "prograde": title = I18n.Tr("SAS_MODE_PROGRADE", "顺行"); break;
                    case "retrograde": title = I18n.Tr("SAS_MODE_RETROGRADE", "逆行"); break;
                    case "normal": title = I18n.Tr("SAS_MODE_NORMAL", "法向"); break;
                    case "antinormal": title = I18n.Tr("SAS_MODE_ANTINORMAL", "反法向"); break;
                    case "radialin": title = I18n.Tr("SAS_MODE_RADIAL_IN", "径向向内"); break;
                    case "radialout": title = I18n.Tr("SAS_MODE_RADIAL_OUT", "径向向外"); break;
                    case "target": title = I18n.Tr("SAS_MODE_TARGET", "目标"); break;
                    case "antitarget": title = I18n.Tr("SAS_MODE_ANTITARGET", "反目标"); break;
                    case "maneuver": title = I18n.Tr("SAS_MODE_MANEUVER", "机动节点"); break;
                }
                SetTextIfChanged(_markerHoverTooltipText, title);
                SetTextIfChanged(_markerHoverTooltipSub, I18n.Tr("WIDGET_NAV_CLICK_ENGAGE_SAS", "点击：启用 SAS"));
            }
        }

        public void OnMarkerHoverExit(string markerKey)
        {
            if (_activeHoveredMarkerKey == markerKey)
            {
                _activeHoveredMarkerKey = null;
                if (_markerHoverTooltipObj != null) _markerHoverTooltipObj.SetActive(false);
            }
        }

        public void HandleMarkerClick(string markerKey, Vector2 pos)
        {
            FlightSASMode? targetMode = NavballSphereWidget.GetSASModeForMarker(markerKey);
            if (targetMode.HasValue)
            {
                var telem = FlightTelemetryContext.Current;
                if (telem != null)
                {
                    telem.SetSASMode(targetMode.Value);
                }
                ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;
                TriggerShockwaveRipple(pos, NavballMarkerFactory.GetSASModeColor(targetMode.Value, curTheme));
            }
        }

        public void HandleMarkerRightClick(string markerKey, Vector2 pos)
        {
#if KSP_RUNTIME
            if (markerKey != null && markerKey.Equals("maneuver", StringComparison.OrdinalIgnoreCase))
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null && v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    var node = v.patchedConicSolver.maneuverNodes[0];
                    if (node != null && TimeWarp.fetch != null)
                    {
                        double now = Planetarium.GetUniversalTime();
                        double targetUt = node.UT - 30.0;
                        if (targetUt > now + 5.0)
                        {
                            TimeWarp.fetch.WarpTo(targetUt);
                            ScreenMessages.PostScreenMessage("MFP: Timewarping to Maneuver Node (T-30s)", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            }
#endif
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                FlightTelemetryContext.Current?.CycleSpeedMode();
            }
        }

        public override void PopulateContextMenu(Action<string, Action> registerAction)
        {
            base.PopulateContextMenu(registerAction);
            if (_displayMode == RectangularNavballMode.DimensionReducedPFD)
            {
                registerAction(I18n.Tr("CTX_NAV_MODE_3D", "🌐 切换为: 3D三维导航球"), () => SetDisplayMode(RectangularNavballMode.Spherical3D));
            }
            else
            {
                registerAction(I18n.Tr("CTX_NAV_MODE_PFD", "✈ 切换为: 民航降维PFD"), () => SetDisplayMode(RectangularNavballMode.DimensionReducedPFD));
            }
        }

        public void SetDisplayMode(RectangularNavballMode mode)
        {
            if (_displayMode == mode) return;
            _displayMode = mode;
            _logic.DisplayMode = mode;
            SetCustomTemplateChannel("MODE", mode == RectangularNavballMode.DimensionReducedPFD ? "PFD" : "3D");
            if (_sphereMaterial != null)
            {
                float targetPfd = mode == RectangularNavballMode.DimensionReducedPFD ? 1.0f : 0.0f;
                _lastUploadedPfdMode.Reset(targetPfd);
                _sphereMaterial.SetFloat(_PropPfdMode, targetPfd);
            }
            if (mode == RectangularNavballMode.DimensionReducedPFD)
            {
                MFPToastBridge.Show(I18n.Tr("TIP_NAV_MODE_PFD", "已切换至民航降维PFD模式"));
            }
            else
            {
                MFPToastBridge.Show(I18n.Tr("TIP_NAV_MODE_3D", "已切换至3D三维导航球模式"));
            }
        }

        private void SetCustomTemplateChannel(string key, string value)
        {
            if (Config == null) return;
            string raw = Config.CustomTemplate ?? string.Empty;
            var parts = new List<string>();
            bool updated = false;

            if (!string.IsNullOrEmpty(raw))
            {
                string[] pairs = raw.Split(';');
                foreach (var p in pairs)
                {
                    int eq = p.IndexOf('=');
                    if (eq > 0)
                    {
                        string k = p.Substring(0, eq).Trim();
                        if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        {
                            parts.Add($"{key}={value}");
                            updated = true;
                            continue;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(p)) parts.Add(p);
                }
            }

            if (!updated)
            {
                parts.Add($"{key}={value}");
            }

            Config.CustomTemplate = string.Join(";", parts.ToArray());
            InvalidateTemplateChannels();
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
            }
            base.OnDestroy();
        }
    }
}
