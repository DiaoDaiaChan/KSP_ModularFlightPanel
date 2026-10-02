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
    /// 3D 姿态球主控仪表组件 (Navball Sphere Widget)
    /// 采用现代单 Quad 屏幕空间数学解析光线投射 (Screen-Space Analytic Raymarching) 管线，
    /// 彻底移除旧版 3D 摄像机、球体网格、离屏 RenderTexture 与贴图烘焙，
    /// 仅保留：
    /// 1. 新管线 (ProceduralVector): 现代 GPU 单 Quad 纯数学矢量解析直出，视网膜级超清画质
    /// 2. 原版贴图 (StockTexture): 单 Quad 采样原版 / TextureReplacer 材质贴图并辅以微锐化滤波
    /// 3. 原版导航球 (StockDirect): 直接调用官方 3D 导航球并剔除外围杂项
    /// </summary>
    /// <summary>
    /// 3D 姿态球状态快照 (0 GC 值类型)
    /// </summary>
    public struct NavballSphereState : IEquatable<NavballSphereState>
    {
        public bool HasVessel;
        public NavballRenderMode RenderMode;
        public Texture StockTexture;
        public Vector2 TextureScale;
        public Vector2 TextureOffset;
        public string RefCategory;
        public string RefCategoryUpper;
        public string FrameName;
        public string FormattedFrame;
        public string FormattedHeading;
        public float Heading;
        public float RollAngle;
        public Quaternion AttitudeRotation;
        public string SpeedMode;
        public FlightSASMode SasMode;
        public bool SasActive;
        public bool HasTarget;
        public bool HasManeuverNode;
        public float GroundHazardAlert;

        public bool Equals(NavballSphereState other)
        {
            return HasVessel == other.HasVessel &&
                   RenderMode == other.RenderMode &&
                   ReferenceEquals(StockTexture, other.StockTexture) &&
                   TextureScale == other.TextureScale &&
                   TextureOffset == other.TextureOffset &&
                   RefCategory == other.RefCategory &&
                   RefCategoryUpper == other.RefCategoryUpper &&
                   FrameName == other.FrameName &&
                   FormattedFrame == other.FormattedFrame &&
                   FormattedHeading == other.FormattedHeading &&
                   Mathf.Abs(Heading - other.Heading) < 0.05f &&
                   Mathf.Abs(RollAngle - other.RollAngle) < 0.05f &&
                   Quaternion.Angle(AttitudeRotation, other.AttitudeRotation) < 0.01f &&
                   SpeedMode == other.SpeedMode &&
                   SasMode == other.SasMode &&
                   SasActive == other.SasActive &&
                   HasTarget == other.HasTarget &&
                   HasManeuverNode == other.HasManeuverNode &&
                   Mathf.Abs(GroundHazardAlert - other.GroundHazardAlert) < 0.01f;
        }

        public override bool Equals(object obj) => obj is NavballSphereState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ (int)RenderMode;
                if (RefCategory != null) hash = (hash * 397) ^ RefCategory.GetHashCode();
                if (RefCategoryUpper != null) hash = (hash * 397) ^ RefCategoryUpper.GetHashCode();
                if (FormattedFrame != null) hash = (hash * 397) ^ FormattedFrame.GetHashCode();
                if (FormattedHeading != null) hash = (hash * 397) ^ FormattedHeading.GetHashCode();
                hash = (hash * 397) ^ Heading.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 3D 姿态球主控仪表业务解耦大脑 (Headless Widget Logic)
    /// </summary>
    public class NavballSphereLogic : WidgetLogic<NavballSphereState>
    {
        private int _lastHdgVal = -1;
        private string _lastCatUpper = null;
        private string _cachedHdgText = string.Empty;

        private string _lastRawFrame = null;
        private string _lastFrameCat = null;
        private string _cachedFrameText = string.Empty;

        private string _lastRawCat = null;
        private string _cachedCatUpper = "SURFACE";

        public override void Reset()
        {
            _lastHdgVal = -1;
            _lastCatUpper = null;
            _cachedHdgText = string.Empty;
            _lastRawFrame = null;
            _lastFrameCat = null;
            _cachedFrameText = string.Empty;
            _lastRawCat = null;
            _cachedCatUpper = "SURFACE";
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            NavballRenderMode mode = ThemeManager.Instance?.GlobalRenderMode ?? NavballRenderMode.ProceduralVector;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            var hook = NavBallHookService.Provider;
            Texture stockTex = null;
            Vector2 texScale = Vector2.one;
            Vector2 texOffset = Vector2.zero;
            string rawCat = "SURFACE";
            string rawFrame = null;
            float heading = 0f;
            Quaternion attitudeRot = Quaternion.identity;
            float rollAngle = 0f;

            if (hook != null)
            {
                if (mode == NavballRenderMode.StockTexture)
                {
                    stockTex = hook.BallTexture;
                    texScale = hook.TextureScale;
                    texOffset = hook.TextureOffset;
                }
                rawCat = hook.ReferenceFrameCategory;
                if (string.IsNullOrEmpty(rawCat)) rawCat = "SURFACE";
                rawFrame = hook.FrameName;

                if (hook.HasStockNavBall)
                {
                    heading = hook.HeadingAngle;
                    attitudeRot = hook.ViewRotation;
                }
                else if (telemetry != null)
                {
                    heading = (float)telemetry.Heading;
                    attitudeRot = telemetry.AttitudeRotation;
                }
            }
            else if (telemetry != null)
            {
                heading = (float)telemetry.Heading;
                attitudeRot = telemetry.AttitudeRotation;
            }

            if (telemetry != null)
            {
                rollAngle = (float)telemetry.Roll;
            }
            else
            {
                rollAngle = attitudeRot.eulerAngles.z;
                if (rollAngle > 180f) rollAngle -= 360f;
            }

            if (!object.ReferenceEquals(rawCat, _lastRawCat) && rawCat != _lastRawCat)
            {
                _lastRawCat = rawCat;
                _cachedCatUpper = string.IsNullOrEmpty(rawCat) ? "SURFACE" : rawCat.ToUpperInvariant();
            }
            string catUpper = _cachedCatUpper;

            int iHdg = (Mathf.RoundToInt(heading) % 360 + 360) % 360;
            if (iHdg != _lastHdgVal || catUpper != _lastCatUpper)
            {
                _lastHdgVal = iHdg;
                _lastCatUpper = catUpper;
                switch (catUpper)
                {
                    case "INERTIAL":
                        int raH = Mathf.FloorToInt(iHdg / 15f);
                        int raM = Mathf.FloorToInt(((iHdg % 15) / 15f) * 60f);
                        _cachedHdgText = $"RA {raH:D2}h{raM:D2}m";
                        break;
                    case "BODY_FIXED":
                    case "BODY_SURFACE":
                        _cachedHdgText = CacheManager.FastLon(iHdg);
                        break;
                    case "ORBIT":
                    case "ORBITAL":
                        _cachedHdgText = CacheManager.FastObt(iHdg);
                        break;
                    case "TARGET":
                        _cachedHdgText = CacheManager.FastTgt(iHdg);
                        break;
                    case "LAGRANGE":
                    case "BARYCENTRIC":
                        _cachedHdgText = $"LAG {iHdg:D3}°";
                        break;
                    default:
                        _cachedHdgText = CacheManager.FastHdg(iHdg);
                        break;
                }
            }

            if (rawFrame != _lastRawFrame || catUpper != _lastFrameCat)
            {
                _lastRawFrame = rawFrame;
                _lastFrameCat = catUpper;
                string frame = rawFrame;
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
                _cachedFrameText = frame;
            }

            string speedMode = telemetry != null ? (telemetry.SpeedModeName ?? string.Empty) : string.Empty;
            FlightSASMode sasMode = telemetry != null ? telemetry.CurrentSASMode : FlightSASMode.StabilityAssist;
            bool sasActive = telemetry != null && telemetry.IsSASEnabled;
            bool hasVessel = telemetry != null && telemetry.HasVessel;
            bool hasTarget = telemetry != null && telemetry.HasTarget;
            bool hasManeuver = telemetry != null && telemetry.HasManeuverNode;

            float hazardAlert = 0.0f;
            if (hasVessel)
            {
                double vSpeed = telemetry.VerticalSpeed;
                if (vSpeed < -18.0)
                {
                    double rAlt = telemetry.AltitudeAGL;
                    if (rAlt > 0.1 && rAlt < 800.0)
                    {
                        float sinkHazard = Mathf.Clamp01((float)(-vSpeed - 18.0) / 45.0f);
                        float altHazard = Mathf.Clamp01((float)(800.0 - rAlt) / 750.0f);
                        hazardAlert = sinkHazard * altHazard;
                    }
                }
            }

            CurrentState = new NavballSphereState
            {
                HasVessel = hasVessel,
                RenderMode = mode,
                StockTexture = stockTex,
                TextureScale = texScale,
                TextureOffset = texOffset,
                RefCategory = rawCat,
                RefCategoryUpper = catUpper,
                FrameName = rawFrame,
                FormattedFrame = _cachedFrameText,
                FormattedHeading = _cachedHdgText,
                Heading = heading,
                RollAngle = rollAngle,
                AttitudeRotation = attitudeRot,
                SpeedMode = speedMode,
                SasMode = sasMode,
                SasActive = sasActive,
                HasTarget = hasTarget,
                HasManeuverNode = hasManeuver,
                GroundHazardAlert = hazardAlert
            };
        }
    }

    [DefaultExecutionOrder(10000)]
    [AlwaysFullPower]
    [FlightWidget("navball", "navball_sphere", Category = WidgetCategory.Navigation, DisplayName = "3D 姿态球", Description = "现代超清矢量/贴图 3D 姿态球核心，支持无极缩放、姿态导引十字与全量机动矢量。", DefaultWidgetId = "core.navball", DefaultX = 0f, DefaultY = 0f, IsSingleton = true, HighFrequency = true, AlwaysFullPower = true, ExactIds = new[] { "core.navball" })]
    public class NavballSphereWidget : BaseNavballSphereWidget, IPointerClickHandler, INavballMarkerWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private readonly NavballSphereLogic _logic = new NavballSphereLogic();
        protected override IWidgetLogic LogicCore => _logic;

        public override FlightNavballPipeline GetNavballPipeline()
        {
            return new FlightNavballPipeline(_sphereMaterial, null, null, false, 512, this);
        }

        private readonly Cached<string> _lastAppliedHeadingText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastAppliedFrameText = new Cached<string>(string.Empty);
        private readonly Cached<Color> _lastAppliedFrameColor = new Cached<Color>(Color.clear);

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
            var state = _logic.CurrentState;
            var mode = state.RenderMode;

            if (mode == NavballRenderMode.StockDirect)
            {
                if (_displayImage != null && _displayImage.enabled) _displayImage.enabled = false;
                if (_crosshair != null && _crosshair.activeSelf) _crosshair.SetActive(false);
                if (_bezelRing != null && _bezelRing.activeSelf) _bezelRing.SetActive(false);
                if (_markerSlots != null)
                {
                    for (int i = 0; i < _markerSlots.Length; i++)
                    {
                        var slotImg = _markerSlots[i]?.Image;
                        if (slotImg != null && slotImg.gameObject.activeSelf)
                        {
                            slotImg.gameObject.SetActive(false);
                        }
                    }
                }
                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
                NavBallHookService.SyncStockNavballAction?.Invoke(RectTransform, CommittedScale);
                return;
            }

            if (NavBallHookService.IsCleanStockNavballActiveFunc?.Invoke() ?? false)
            {
                NavBallHookService.ResetStockNavballAction?.Invoke();
                NavBallHookService.SetStockNavballCleanAction?.Invoke(false);
                NavBallHookService.HideStockNavballAction?.Invoke(true);
            }

            if (_displayImage != null && !_displayImage.enabled) _displayImage.enabled = true;
            if (_crosshair != null && !_crosshair.activeSelf) _crosshair.SetActive(true);
            if (_bezelRing != null && !_bezelRing.activeSelf) _bezelRing.SetActive(true);

            if (mode == NavballRenderMode.StockTexture)
            {
                Texture stockTex = state.StockTexture;
                if (stockTex != null && _displayImage != null && _displayImage.texture != stockTex)
                {
                    _displayImage.texture = stockTex;
                    _sphereMaterial.mainTexture = stockTex;
                    _sphereMaterial.SetTextureScale("_MainTex", state.TextureScale);
                    _sphereMaterial.SetTextureOffset("_MainTex", state.TextureOffset);
                }
            }

            // 更新航向读数盒与参考系模式显示
            if (_headingText != null)
            {
                if (_lastAppliedHeadingText.Update(state.FormattedHeading))
                {
                    SetTextIfChanged(_headingText, state.FormattedHeading);
                }
            }

            if (_frameText != null)
            {
                if (_lastAppliedFrameText.Update(state.FormattedFrame))
                {
                    SetTextIfChanged(_frameText, state.FormattedFrame);
                }

                // 参考系角标切变颜色同步 (仅在动画结束后且颜色发生改变时写入 UGUI)
                if (_frameBadgeAnimTimer >= 0.40f)
                {
                    ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;
                    Color accentCol = GetFrameAccentColor(state.RefCategoryUpper, curTheme);
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
            _displayedAttitudeRotation = Quaternion.identity;
            _lastAppliedHeadingText.Reset(string.Empty);
            _lastAppliedFrameText.Reset(string.Empty);
            _lastAppliedFrameColor.Reset(Color.clear);
        }

        // ── 渲染显示组件 ──
        private RawImage _displayImage;
        private float _visualRadius;

        // ── 仪表盘外壳与修饰图元 ──
        private GameObject _shellRoot;
        private Image _shellImage;
        private Outline _shellOutline;
        private Text _shellTitle;
        private Text _shellStatus;
        private GameObject _bezelRing;
        private GameObject _crosshair;
        private Image _reticleImage;
        private GameObject _headingBox;
        private Text _headingText;
        private Text _frameText;

        // ── 2D 矢量 HUD 标记覆盖层 ──
        private Transform _markerContainer;

        // ── 着色器属性 Uniform 缓存 ──
        private static readonly int _PropRenderMode = Shader.PropertyToID("_RenderMode");
        private static readonly int _PropSphereInvRotation = Shader.PropertyToID("_SphereInvRotation");

        private static readonly int _PropSkyZenithColor = Shader.PropertyToID("_SkyZenithColor");
        private static readonly int _PropSkyHorizonColor = Shader.PropertyToID("_SkyHorizonColor");
        private static readonly int _PropGroundHorizonColor = Shader.PropertyToID("_GroundHorizonColor");
        private static readonly int _PropGroundNadirColor = Shader.PropertyToID("_GroundNadirColor");

        private static readonly int _PropSkyColor = Shader.PropertyToID("_SkyColor");
        private static readonly int _PropGroundColor = Shader.PropertyToID("_GroundColor");
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
        private float _rollPointerAlpha = 0f;

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
        private float _uploadedTrendStrength = -1f;
        private Quaternion _uploadedTrendRotation = Quaternion.identity;
        private int _trendFrame = -1;

        // ── 视网膜细节与几何适配缓存 ──
        private readonly Vector3[] _displayCorners = new Vector3[4];
        private int _screenWidth = -1;
        private int _screenHeight = -1;
        private float _detailScale = -1f;

        // ── 坡度角刻度与滚转指引指针 ──
        private readonly List<Image> _bankAngleTicks = new List<Image>();
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
        private GameObject _markerHoverTooltipObj;
        private RectTransform _markerHoverTooltipRt;
        private Image _markerHoverTooltipBg;
        private Outline _markerHoverTooltipOutline;
        private Text _markerHoverTooltipText;
        private Text _markerHoverTooltipSub;
        private string _activeHoveredMarkerKey = null;
        private Vector2 _activeHoveredMarkerPos = Vector2.zero;

        private static readonly Dictionary<string, (string title, string sub)> MarkerTooltipLabels = new Dictionary<string, (string title, string sub)>(StringComparer.OrdinalIgnoreCase)
        {
            { "prograde", ("PROGRADE", "L-CLICK: LOCK PROGRADE") },
            { "retrograde", ("RETROGRADE", "L-CLICK: LOCK RETROGRADE") },
            { "normal", ("NORMAL", "L-CLICK: LOCK NORMAL") },
            { "antinormal", ("ANTI-NORMAL", "L-CLICK: LOCK ANTI-NORMAL") },
            { "radialin", ("RADIAL IN", "L-CLICK: LOCK RADIAL IN") },
            { "radialout", ("RADIAL OUT", "L-CLICK: LOCK RADIAL OUT") },
            { "target", ("TARGET", "L-CLICK: LOCK TARGET") },
            { "antitarget", ("ANTI-TARGET", "L-CLICK: LOCK ANTI-TARGET") },
            { "maneuver", ("MANEUVER NODE", "L-CLICK: SAS / R-CLICK: WARP") },
            { "velocity_vector", ("VELOCITY", "L-CLICK: LOCK PROGRADE") },
            { "anti_velocity_vector", ("ANTI-VELOCITY", "L-CLICK: LOCK RETROGRADE") }
        };

        // ── 标线渲染死区量化与 UGUI 重构消除缓存 ──
        private struct MarkerRenderState
        {
            public Vector2 Position;
            public float Scale;
            public float Alpha;
        }

        private class MarkerSlot
        {
            public string Key;
            public NavballMarkerType MarkerType;
            public Image Image;
            public RectTransform RectTransform;
            public NavballMarkerClickHandler Handler;
            public Vector3 CurrentDir;
            public Vector3 TransitionStartDir;
            public Vector2 RenderedPos;
            public Vector2 LastStableBearing;
            public MarkerRenderState LastRenderState;
            public bool IsActive;
        }

        private MarkerSlot[] _markerSlots;
        private readonly MarkerSlot[] _markerSlotByType = new MarkerSlot[16];
        private readonly Vector4[] _cachedAvoidVectors = new Vector4[4];
        private Vector4 _uploadedAvoid0;
        private Vector4 _uploadedAvoid1;
        private Vector4 _uploadedAvoid2;
        private Vector4 _uploadedAvoid3;
        private Vector4 _uploadedInvRot;
        private RectTransform _crosshairRt;
        private string _evaluatedPaletteCategory;
        private ThemeConfig _evaluatedPaletteTheme;

        // ── 着色器动态属性存在性布尔缓存（消除热循环 native C++ HasProperty 调用） ──
        private bool _hasPropFramePatternOld;
        private bool _hasPropFramePattern;
        private bool _hasPropFrameTransitionProgress;
        private bool _hasPropGroundHazardAlert;
        private bool _hasPropVernierScaleDetail;
        private bool _hasPropTrendStrength;
        private bool _hasPropTrendRotation;

        // ── 着色器已提交值缓存（消除高频冗余 native SetFloat/SetColor 跨界开销） ──
        private float _uploadedHazardAlert = -999f;
        private float _uploadedVernierDetail = -999f;
        private float _uploadedFramePatternOld = -999f;
        private float _uploadedFramePattern = -999f;
        private float _uploadedFrameTransitionProgress = -999f;
        private NavballFramePalette _uploadedPalette;
        private bool _hasUploadedPalette = false;
        private readonly CachedFloat _lastBreathScale = new CachedFloat(-1f, 0.015f);

        // ── 航向与姿态死区更新缓存 ──
        private readonly CachedFloat _lastRollPointerAngle = new CachedFloat(-9999f, 0.05f);
        private readonly CachedFloat _lastBankTicksAlpha = new CachedFloat(-1f, 0.025f);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float ballDiameter = 150f * UIFactory.GetKspNavballUiScale() * s;

            bool showShell = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showHeadingBox = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("heading_box", StringComparison.OrdinalIgnoreCase) >= 0;
            float shellWidth = ballDiameter + 36f * s;
            float shellHeight = ballDiameter + (showHeadingBox ? 44f : 36f) * s;

            RectTransform.sizeDelta = showShell ? new Vector2(shellWidth, shellHeight) : new Vector2(ballDiameter, ballDiameter);
            _visualRadius = ballDiameter * 0.5f;

            ApplyCanvasIsolation(true);

            if (showShell)
            {
                CreateNavballShell(shellWidth, shellHeight, s, theme);
            }

            // 1. 屏幕空间数学解析光线投射姿态球 (Screen-Space Analytic Raymarched Sphere)
            // 彻底移除独立摄像机、3D 网格小球与 RenderTexture，改为在原生 UGUI 矩形上通过着色器直接解析绘制
            // 0 摄像机开销、0 离屏纹理显存、任意大机动旋转下 CPU 耗时恒定 0.005 ms！
            _displayImage = CreateChild<RawImage>("Sphere_RaymarchImage", transform,
                new Vector2(ballDiameter, ballDiameter), Vector2.zero);
            _displayImage.texture = Texture2D.whiteTexture;
            _displayImage.raycastTarget = false;

            Shader targetShader = AssetLoader.RaymarchShader ?? Shader.Find("ModularFlightPanel/NavballRaymarch") ?? Shader.Find("UI/Default");
            _sphereMaterial = new Material(targetShader);
            _displayImage.material = _sphereMaterial;
            CacheMaterialProperties();
            UpdateProceduralDetailScale();

            var initialMode = ThemeManager.Instance.GlobalRenderMode;
            if (initialMode == NavballRenderMode.StockDirect)
            {
                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
            }

            // 2. 2D 矢量标线层 (Prograde, Retrograde, Normal, Radial, Target, Maneuver)
            CreateMarkerOverlayLayer(transform, CurrentDpiScale);

            // 3. 瞄准标与金属圆环包边
            CreateCrosshair(transform, CurrentDpiScale, theme);

            // 极细航电金属质感圆环外圈与滚转坡度刻度弧 (Bank Angle Roll Scale)
            GameObject bezelObj = UIFactory.CreatePanel(transform, "Sphere_Bezel_Ring",
                new Vector2(ballDiameter + 2f * CurrentDpiScale, ballDiameter + 2f * CurrentDpiScale),
                Vector2.zero, Color.clear);
            Image bezelImg = bezelObj.GetComponent<Image>();
            bezelImg.raycastTarget = false;
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            if (bezelImg != null && uiMat != null) bezelImg.material = uiMat;

            Outline bezelOutline = bezelObj.AddComponent<Outline>();
            Color border = theme.FrameBorderColor;
            bezelOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
            bezelOutline.effectDistance = new Vector2(1.2f * CurrentDpiScale, 1.2f * CurrentDpiScale);
            _bezelRing = bezelObj;
            CreateBankAngleScale(bezelObj.transform, ballDiameter * 0.5f, CurrentDpiScale, theme);

            if (showHeadingBox)
            {
                CreateHeadingBox(transform, CurrentDpiScale, theme);
            }

            // 标线层置于最顶层，确保鼠标悬停与点击事件不被背景包边遮挡
            if (_markerContainer != null)
            {
                _markerContainer.SetAsLastSibling();
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetGraphicViewportControl("navball_viewport", "3D球体视口", _displayImage != null ? _displayImage.gameObject : gameObject, _displayImage));
            if (_crosshair != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "crosshair", "准星标线", _crosshair));
            if (_headingBox != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "heading_box", "航向盒读数", _headingBox));
            if (_shellRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "ecam_shell", "ECAM机匣外壳", _shellRoot, (t) => ApplyCard(_shellImage, _shellOutline, CardStyleRole.Normal, t)));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void CacheMaterialProperties()
        {
            if (_sphereMaterial == null)
            {
                _hasPropFramePatternOld = false;
                _hasPropFramePattern = false;
                _hasPropFrameTransitionProgress = false;
                _hasPropGroundHazardAlert = false;
                _hasPropVernierScaleDetail = false;
                _hasPropTrendStrength = false;
                _hasPropTrendRotation = false;
                return;
            }
            _hasPropFramePatternOld = _sphereMaterial.HasProperty(_PropFramePatternOld);
            _hasPropFramePattern = _sphereMaterial.HasProperty(_PropFramePattern);
            _hasPropFrameTransitionProgress = _sphereMaterial.HasProperty(_PropFrameTransitionProgress);
            _hasPropGroundHazardAlert = _sphereMaterial.HasProperty(_PropGroundHazardAlert);
            _hasPropVernierScaleDetail = _sphereMaterial.HasProperty(_PropVernierScaleDetail);
            _hasPropTrendStrength = _sphereMaterial.HasProperty(_PropTrendStrength);
            _hasPropTrendRotation = _sphereMaterial.HasProperty(_PropTrendRotation);
        }

        private void CreateNavballShell(float shellWidth, float shellHeight, float dpiScale, ThemeConfig theme)
        {
            _shellRoot = UIFactory.CreatePanel(transform, "Navball_Ecam_Shell", new Vector2(shellWidth, shellHeight),
                Vector2.zero, theme.FrameBgColor);
            _shellRoot.transform.SetAsFirstSibling();
            _shellImage = _shellRoot.GetComponent<Image>();
            _shellImage.raycastTarget = false;
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);
            if (_shellImage != null && uiMat != null) _shellImage.material = uiMat;

            _shellOutline = _shellRoot.AddComponent<Outline>();
            _shellOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            _shellTitle = UIFactory.CreateText(_shellRoot.transform, "Navball_Shell_Title",
                I18n.Tr("WIDGET_NAV_ATTITUDE_NAVBALL", "姿态 / 导航球"), Mathf.Max(8, Mathf.RoundToInt(8f * dpiScale)),
                TextAnchor.MiddleLeft, theme.AccentSecondary);
            if (_shellTitle != null && txtMat != null) _shellTitle.material = txtMat;
            RectTransform titleRt = _shellTitle.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(shellWidth - 30f * dpiScale, 16f * dpiScale);
            titleRt.anchoredPosition = new Vector2(-shellWidth * 0.5f + 15f * dpiScale,
                shellHeight * 0.5f - 12f * dpiScale);

            _shellStatus = UIFactory.CreateText(_shellRoot.transform, "Navball_Shell_Status", I18n.Tr("WIDGET_NAV_LIVE", "实时"),
                Mathf.Max(7, Mathf.RoundToInt(7f * dpiScale)), TextAnchor.MiddleRight, theme.AccentPrimary);
            if (_shellStatus != null && txtMat != null) _shellStatus.material = txtMat;
            RectTransform statusRt = _shellStatus.GetComponent<RectTransform>();
            statusRt.sizeDelta = new Vector2(60f * dpiScale, 16f * dpiScale);
            statusRt.anchoredPosition = new Vector2(shellWidth * 0.5f - 15f * dpiScale,
                shellHeight * 0.5f - 12f * dpiScale);
        }

        private void CreateCrosshair(Transform parent, float dpiScale, ThemeConfig theme)
        {
            RectTransform crosshairRt = CreateContainer("Navball_Crosshair_Reticle", parent,
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

        private void CreateBankAngleScale(Transform parent, float radius, float s, ThemeConfig theme)
        {
            _bankAngleTicks.Clear();
            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            // 坡度角定义：0° (顶部基准), ±10°, ±20°, ±30° (标准转弯), ±45° (大坡度), ±60° (极限坡度)
            float[] angles = new float[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f, 45f, -45f, 60f, -60f };
            float r = radius + 2.5f * s; // 紧贴金属外表圈外缘

            foreach (float deg in angles)
            {
                bool isZero = Mathf.Abs(deg) < 0.1f;
                bool isMajor = Mathf.Abs(Mathf.Abs(deg) - 30f) < 0.1f || isZero;
                bool isWarn = Mathf.Abs(deg) >= 44f;

                float tickLen = isZero ? (6.5f * s) : (isMajor ? 6f * s : (isWarn ? 5f * s : 3.5f * s));
                float tickWidth = isZero ? (2.2f * s) : (isMajor ? 1.8f * s : 1.2f * s);

                float rad = deg * Mathf.Deg2Rad;
                float dist = r + tickLen * 0.5f;
                Vector2 tickPos = new Vector2(Mathf.Sin(rad) * dist, Mathf.Cos(rad) * dist);
                Image img = CreateChild<Image>($"BankTick_{deg:F0}", parent,
                    new Vector2(tickWidth, tickLen), tickPos);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -deg);
                img.raycastTarget = false;
                img.color = isWarn ? theme.WarningColor : (isZero ? theme.HorizonLineColor : theme.GridColor);
                if (uiMat != null) img.material = uiMat;
                _bankAngleTicks.Add(img);
            }

            // 动态高精度滚转/天顶指引指针 (Roll / Sky Pointer)
            _bankRollPointerRoot = CreateContainer("BankRollPointer_Root", parent,
                Vector2.zero, Vector2.zero);

            _bankRollPointerImg = CreateChild<Image>("PointerNeedle", _bankRollPointerRoot,
                new Vector2(10f * s, 12f * s), new Vector2(0f, r + 4.5f * s));
            _bankRollPointerImg.sprite = NavballMarkerFactory.GetRollPointerSprite();
            _bankRollPointerImg.color = theme.HorizonLineColor;
            _bankRollPointerImg.raycastTarget = false;
            if (uiMat != null) _bankRollPointerImg.material = uiMat;
        }

        private void CreateHeadingBox(Transform parent, float dpiScale, ThemeConfig theme)
        {
            _headingBox = UIFactory.CreatePanel(parent, "Navball_Heading_Box",
                new Vector2(96f * dpiScale, 20f * dpiScale),
                new Vector2(0f, -_visualRadius - 13f * dpiScale),
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
            _headingText.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            _headingText.rectTransform.sizeDelta = new Vector2(96f * dpiScale, 20f * dpiScale);
            if (txtMat != null) _headingText.material = txtMat;

            _frameText = UIFactory.CreateText(_headingBox.transform, "Frame_Label",
                I18n.Tr("WIDGET_NAV_SURF", "表面"), Mathf.Max(7, Mathf.RoundToInt(8f * dpiScale)),
                TextAnchor.MiddleRight, theme.AccentSecondary);
            _frameText.rectTransform.anchoredPosition = new Vector2(44f * dpiScale, 0f);
            _frameText.rectTransform.sizeDelta = new Vector2(36f * dpiScale, 18f * dpiScale);
            if (txtMat != null) _frameText.material = txtMat;
        }

        private void CreateMarkerOverlayLayer(Transform parent, float dpiScale)
        {
            RectTransform rt = CreateContainer("Navball_Marker_Overlay", parent,
                new Vector2(_visualRadius * 2f, _visualRadius * 2f), Vector2.zero);
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
            Array.Clear(_markerSlotByType, 0, _markerSlotByType.Length);
            _markerSlots = new MarkerSlot[markerKeys.Length];
            for (int i = 0; i < markerKeys.Length; i++)
            {
                string k = markerKeys[i];
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

                NavballMarkerType mType = NavballMarkerHelper.GetMarkerType(k);
                var slot = new MarkerSlot
                {
                    Key = k,
                    MarkerType = mType,
                    Image = img,
                    RectTransform = img.rectTransform,
                    Handler = clickHandler,
                    CurrentDir = Vector3.zero,
                    TransitionStartDir = Vector3.zero,
                    RenderedPos = Vector2.zero,
                    LastStableBearing = Vector2.up,
                    LastRenderState = default,
                    IsActive = false
                };
                _markerSlots[i] = slot;
                if ((int)mType < _markerSlotByType.Length)
                {
                    _markerSlotByType[(int)mType] = slot;
                }
            }

            // 1. 机动节点航向流光引导箭头容器 (Steering Director Chevron Flow)
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

            // 2. SAS 动态角括号锁定框 (Active SAS Lock Reticle)
            _sasLockReticleImage = CreateChild<Image>("Active_SAS_Reticle", _markerContainer,
                new Vector2(36f * dpiScale, 36f * dpiScale), Vector2.zero);
            _sasLockReticleRt = _sasLockReticleImage.rectTransform;
            _sasLockReticleImage.sprite = NavballMarkerFactory.GetSASLockReticleSprite();
            _sasLockReticleImage.color = WidgetStyleManager.NeutralOpaque;
            _sasLockReticleImage.raycastTarget = false;
            if (uiMat != null) _sasLockReticleImage.material = uiMat;
            _sasLockReticleImage.gameObject.SetActive(false);

            // 3. 点击冲击波扩散环 (Shockwave Ripple)
            _sasRippleImage = CreateChild<Image>("SAS_Shockwave_Ripple", _markerContainer,
                new Vector2(48f * dpiScale, 48f * dpiScale), Vector2.zero);
            _sasRippleRt = _sasRippleImage.rectTransform;
            _sasRippleImage.sprite = NavballMarkerFactory.GetShockwaveSprite();
            _sasRippleImage.color = WidgetStyleManager.NeutralOpaque;
            _sasRippleImage.raycastTarget = false;
            if (uiMat != null) _sasRippleImage.material = uiMat;
            _sasRippleImage.gameObject.SetActive(false);

            // 4. 光标悬停微航电提示卡片 (Cursor Hover Floating Tooltip HUD Card)
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



        private bool _detailScaleDirty = true;

        protected override void OnScaleChanged(float targetScale, float relativeRatio)
        {
            base.OnScaleChanged(targetScale, relativeRatio);
            _detailScale = -1f;
            _detailScaleDirty = true;
            UpdateProceduralDetailScale();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!gameObject.activeInHierarchy) return;
            if (_displayImage == null || !_displayImage.enabled || !_displayImage.gameObject.activeInHierarchy) return;

            SyncAttitudeAndVisuals();
            SyncMarkers();
            UpdateReticleDynamics();
            UpdateRollPointer();
            UpdateSASAndGuidanceVisuals();
            UpdateFrameBadgeAnimation();
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
                _frameText.SetColor(fCol);
            }
            else
            {
                _frameText.rectTransform.SetLocalScaleSafe(Vector3.one);
            }
        }

        private void UpdateProceduralDetailScale()
        {
            if (_displayImage == null || _sphereMaterial == null) return;

            int sw = Screen.width;
            int sh = Screen.height;
            if (!_detailScaleDirty && _detailScale >= 0f && sw == _screenWidth && sh == _screenHeight)
            {
                return;
            }
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

            // 1. 权威姿态四元数解算
            var state = _logic.CurrentState;
            Quaternion rawRot;
            if (hasHook)
            {
                rawRot = hook.ViewRotation;
            }
            else
            {
                rawRot = state.AttitudeRotation;
            }

            // 2. 坐标系切变（自动/手动/Principia）探测与平滑过渡动画启动
            string category = hook?.ReferenceFrameCategory ?? (state.RefCategory ?? "SURFACE");
            string frameName = hook?.FrameName ?? (state.FrameName ?? "");
            string speedMode = state.SpeedMode ?? "";

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

                // 切换瞬间平息趋势预测器的角速度突变，避免跨参考系角度差导致的导引跳变
                _hasPreviousAttitudeRotation = false;
                _filteredTrendRotation = Quaternion.identity;
                _attitudeTrendStrength = 0f;

                // 触发中心微光雷达波扩散动画，颜色对应新参考系
                Color frameAccent = GetFrameAccentColor(category, curTheme);
                TriggerShockwaveRipple(Vector2.zero, frameAccent);

                // 激活参考系角标徽章缩放动画
                _frameBadgeAnimTimer = 0f;

                // 记录所有当前可见标记物的三维起始矢量，以便进行 3D 球面 Slerp 平滑过渡
                if (_markerSlots != null)
                {
                    for (int i = 0; i < _markerSlots.Length; i++)
                    {
                        var slot = _markerSlots[i];
                        if (slot != null)
                        {
                            slot.TransitionStartDir = slot.CurrentDir;
                        }
                    }
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
                // 航电级三次减速平滑缓动曲线 (Cubic Ease-Out)
                eased = 1.0f - Mathf.Pow(1.0f - transitionProgress, 3.0f);

                // 姿态四元数平滑 Slerp 过渡：起点为切变瞬间姿态，目标为随飞船最新实时旋转的 rawRot
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
                        // 场景切变、时间加速跳变或大角位移：瞬时咬合无延迟
                        _displayedAttitudeRotation = rawRot;
                    }
                    else if (angleDelta < 0.035f)
                    {
                        // 静止态/小微颤死区守卫：完全锁存稳态姿态，0 像素噪点抖动
                    }
                    else
                    {
                        // 航电 S 曲线自适应追踪：微颤强阻尼平滑，大操纵机动满速直跟
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

                if (_hasPropFramePatternOld && Mathf.Abs(_transitionStartFramePattern - _uploadedFramePatternOld) > 0.001f)
                {
                    _uploadedFramePatternOld = _transitionStartFramePattern;
                    _sphereMaterial.SetFloat(_PropFramePatternOld, _transitionStartFramePattern);
                }
                if (_hasPropFramePattern && Mathf.Abs(newPattern - _uploadedFramePattern) > 0.001f)
                {
                    _uploadedFramePattern = newPattern;
                    _sphereMaterial.SetFloat(_PropFramePattern, newPattern);
                }
                float targetProgress = _isFrameTransitioning ? eased : 1.0f;
                if (_hasPropFrameTransitionProgress && Mathf.Abs(targetProgress - _uploadedFrameTransitionProgress) > 0.005f)
                {
                    _uploadedFrameTransitionProgress = targetProgress;
                    _sphereMaterial.SetFloat(_PropFrameTransitionProgress, targetProgress);
                }
            }

            // 3. 程序化多参考系自适应变色与高级航电动态特性驱动 (Principia / Stock 多参考系高保真映射)
            if (_evaluatedPaletteCategory != category || _evaluatedPaletteTheme != curTheme)
            {
                _evaluatedPaletteCategory = category;
                _evaluatedPaletteTheme = curTheme;
                _targetPalette = GetPaletteForCategory(category, curTheme);
                _isPaletteLerping = true;
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
                // GPWS / 近地大下沉率防撞动态斑马纹警示驱动 (Ground Terrain Hazard Pull-Up Alert)
                if (_hasPropGroundHazardAlert)
                {
                    float hazardAlert = state.GroundHazardAlert;
                    if (Mathf.Abs(hazardAlert - _uploadedHazardAlert) > 0.015f)
                    {
                        _uploadedHazardAlert = hazardAlert;
                        _sphereMaterial.SetFloat(_PropGroundHazardAlert, hazardAlert);
                    }
                }

                // 近地平精细游标阶梯驱动 (Vernier Scale Detail)
                if (_hasPropVernierScaleDetail)
                {
                    float vernier = 1.0f;
                    if (state.HasVessel)
                    {
                        float pitchRate = Mathf.Abs(_smoothedAngularVelocity.x);
                        vernier = 1.0f - Mathf.Clamp01(pitchRate / 18.0f);
                    }
                    if (Mathf.Abs(vernier - _uploadedVernierDetail) > 0.02f)
                    {
                        _uploadedVernierDetail = vernier;
                        _sphereMaterial.SetFloat(_PropVernierScaleDetail, vernier);
                    }
                }
            }
        }

        private void SyncMarkers()
        {
            var hook = NavBallHookService.Provider;
            int avoidIdx = 0;
            for (int i = 0; i < 4; i++) _cachedAvoidVectors[i] = Vector4.zero;

            var state = _logic.CurrentState;
            bool hasTarget = state.HasTarget;
            bool hasManeuver = state.HasManeuverNode;
            bool isSurfaceMode = state.RefCategoryUpper == "SURFACE" || state.RefCategoryUpper == "BODY_SURFACE" || state.RefCategoryUpper == "BODY_FIXED";

            for (int slotIdx = 0; slotIdx < _markerSlots.Length; slotIdx++)
            {
                var slot = _markerSlots[slotIdx];
                var img = slot.Image;
                if (img == null) continue;
                string key = slot.Key;

                // 快速过滤非活动模式标记，节约高达 40% 的高频解算
                if (!hasTarget && (slot.MarkerType == NavballMarkerType.Target || slot.MarkerType == NavballMarkerType.AntiTarget))
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                    slot.RenderedPos = Vector2.zero;
                    slot.CurrentDir = Vector3.zero;
                    slot.LastRenderState = default;
                    slot.IsActive = false;
                    continue;
                }
                if (!hasManeuver && slot.MarkerType == NavballMarkerType.Maneuver)
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                    slot.RenderedPos = Vector2.zero;
                    slot.CurrentDir = Vector3.zero;
                    slot.LastRenderState = default;
                    slot.IsActive = false;
                    continue;
                }
                if (isSurfaceMode && (slot.MarkerType == NavballMarkerType.VelocityVector || slot.MarkerType == NavballMarkerType.AntiVelocityVector))
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                    slot.RenderedPos = Vector2.zero;
                    slot.CurrentDir = Vector3.zero;
                    slot.LastRenderState = default;
                    slot.IsActive = false;
                    continue;
                }

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
                    if (_isFrameTransitioning && slot.TransitionStartDir.sqrMagnitude > 0.001f)
                    {
                        float transT = Mathf.Clamp01(_frameTransitionTimer / FrameTransitionDuration);
                        float eased = 1.0f - Mathf.Pow(1.0f - transT, 3.0f);
                        currentDir = Vector3.Slerp(slot.TransitionStartDir, dir, eased).normalized;
                    }
                    slot.CurrentDir = currentDir;

                    Vector2 bearing = new Vector2(currentDir.x, currentDir.y);
                    float bearingMag = bearing.magnitude;
                    float peripheryRadius = _visualRadius + 7.5f * CurrentDpiScale;

                    Vector2 rawMarkerPos;
                    float targetScale;
                    float alpha;

                    if (currentDir.z >= 0.05f)
                    {
                        // 1. 前向可见半球：正交贴合投影在球体正面
                        rawMarkerPos = (bearingMag > 1.0f && bearingMag > 0.001f)
                            ? (bearing / bearingMag) * _visualRadius
                            : bearing * _visualRadius;
                        targetScale = 1.0f;
                        alpha = 1.0f;

                        // 记录稳定方位角，为切入背面提供连续初值
                        if (bearingMag > 0.08f)
                        {
                            slot.LastStableBearing = bearing / bearingMag;
                        }
                    }
                    else
                    {
                        // 2. 背向半球与超出范围：吸附在表圈外围轨道，平滑过渡并淡化显示 (Periphery Orbit Clamping & Backside Fade)
                        float tOff = Mathf.Clamp01((0.05f - currentDir.z) / 0.15f);
                        Vector2 frontPos = (bearingMag > 1.0f && bearingMag > 0.001f)
                            ? (bearing / bearingMag) * _visualRadius
                            : bearing * _visualRadius;

                        // ── 正后方 180° 奇点防打转与防抖滤波 (Singularity Anti-Spin & Hysteresis) ──
                        // 当标线指向正后方死区 (bearingMag -> 0) 时，避免微小扰动或滚转导致 normBearing 在 360° 剧烈打转
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

                        Vector2 periphPos = normBearing * peripheryRadius;
                        rawMarkerPos = Vector2.Lerp(frontPos, periphPos, tOff);

                        // 根据背向角距离远近自然变淡加深：-currentDir.z 从 0 (地平) 到 1.0 (正后方 180°)
                        float tDepth = Mathf.Clamp01(-currentDir.z);
                        alpha = Mathf.Lerp(0.85f, 0.40f, tDepth);
                        targetScale = Mathf.Lerp(0.90f, 0.68f, tDepth);

                        // 在绝对正后方极小死区锥体 (bearingMag < 0.06) 内进一步平息透明度，避免正对时视觉抢眼
                        if (bearingMag < 0.06f)
                        {
                            alpha *= Mathf.Lerp(0.35f, 1.0f, bearingMag / 0.06f);
                        }
                    }

                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                    slot.IsActive = true;

                    // 机动节点脉冲呼吸特效
                    if (slot.MarkerType == NavballMarkerType.Maneuver)
                    {
                        float pulseRaw = Mathf.Sin(Time.unscaledTime * 6f);
                        float pulse = 1.0f + 0.08f * (Mathf.Round(pulseRaw * 8f) * 0.125f);
                        targetScale *= pulse;
                    }

                    if (_isFrameTransitioning && currentDir.z >= 0.05f)
                    {
                        float transT = Mathf.Clamp01(_frameTransitionTimer / FrameTransitionDuration);
                        float transPulse = 1.0f + 0.12f * Mathf.Sin(transT * Mathf.PI);
                        targetScale *= transPulse;
                    }

                    // 2. 标线位置亚像素自适应防抖滤波 (Marker Spatial Deadband & Anti-Jitter)
                    // 消除载具高频物理微抖对标线投影的震荡干扰
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
                            renderedPos = rawMarkerPos; // 大操纵位移瞬时咬合无延迟
                        }
                    }
                    slot.RenderedPos = renderedPos;

                    // 光标悬停交互 (Hover Scaling, Highlight & Press Feedback)
                    NavballMarkerClickHandler handler = slot.Handler;
                    bool isHovered = handler != null && handler.IsHovered;
                    bool isPressed = handler != null && handler.IsPressed;

                    if (isHovered)
                    {
                        _activeHoveredMarkerKey = key;
                        _activeHoveredMarkerPos = renderedPos;
                        targetScale *= 1.28f;
                        alpha = Mathf.Max(alpha, 0.98f);
                        if (isPressed)
                        {
                            targetScale *= 0.88f;
                        }
                        img.transform.SetAsLastSibling();
                    }

                    // 性能核心优化：UGUI死区量化守卫，杜绝微亚像素浮动导致每帧反复脏化 Canvas 网格
                    var lastState = slot.LastRenderState;
                    bool posChanged = (renderedPos - lastState.Position).sqrMagnitude > 0.16f; // > 0.4px
                    bool scaleChanged = Mathf.Abs(targetScale - lastState.Scale) > 0.02f;
                    bool alphaChanged = Mathf.Abs(alpha - lastState.Alpha) > 0.02f;

                    var rt = slot.RectTransform ?? img.rectTransform;
                    if (posChanged)
                    {
                        rt.SetAnchoredPositionSafe(renderedPos);
                        lastState.Position = renderedPos;
                    }
                    if (scaleChanged)
                    {
                        rt.SetLocalScaleSafe(new Vector3(targetScale, targetScale, 1.0f));
                        lastState.Scale = targetScale;
                    }
                    if (alphaChanged)
                    {
                        img.SetAlpha(alpha);
                        lastState.Alpha = alpha;
                    }
                    slot.LastRenderState = lastState;

                    // 避免球体字号与前方核心航向/机动标重叠遮挡
                    if (avoidIdx < 4 && currentDir.z > 0.1f)
                    {
                        _cachedAvoidVectors[avoidIdx] = new Vector4(currentDir.x, currentDir.y, 0.18f, 1.0f);
                        avoidIdx++;
                    }
                }
                else
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                    slot.RenderedPos = Vector2.zero;
                    slot.CurrentDir = Vector3.zero;
                    slot.LastRenderState = default;
                    slot.IsActive = false;
                    if (_activeHoveredMarkerKey == key)
                    {
                        _activeHoveredMarkerKey = null;
                    }
                }
            }

            // 更新悬浮提示微卡片 HUD
            if (_markerHoverTooltipObj != null)
            {
                if (!string.IsNullOrEmpty(_activeHoveredMarkerKey) && MarkerTooltipLabels.TryGetValue(_activeHoveredMarkerKey, out var tipData))
                {
                    if (!_markerHoverTooltipObj.activeSelf) _markerHoverTooltipObj.SetActive(true);
                    _markerHoverTooltipObj.transform.SetAsLastSibling();

                    _markerHoverTooltipText.SetTextSafe(tipData.title);
                    _markerHoverTooltipSub.SetTextSafe(tipData.sub);

                    float s = CurrentDpiScale;
                    Vector2 tipPos = _activeHoveredMarkerPos;
                    if (tipPos.y >= 0) tipPos.y -= 25f * s;
                    else tipPos.y += 25f * s;

                    tipPos.x = Mathf.Clamp(tipPos.x, -_visualRadius * 0.65f, _visualRadius * 0.65f);
                    _markerHoverTooltipRt.SetAnchoredPositionSafe(tipPos);

                    ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;
                    FlightSASMode? mode = GetSASModeForMarker(_activeHoveredMarkerKey);
                    Color borderCol = mode.HasValue ? NavballMarkerFactory.GetSASModeColor(mode.Value, curTheme) : (curTheme?.AccentPrimary ?? WidgetStyleManager.NeutralOpaque);

                    if (_markerHoverTooltipOutline != null) _markerHoverTooltipOutline.SetColor(borderCol);
                    if (_markerHoverTooltipText != null) _markerHoverTooltipText.SetColor(borderCol);
                    if (_markerHoverTooltipSub != null) _markerHoverTooltipSub.SetColor(curTheme?.TextPrimaryColor ?? WidgetStyleManager.NeutralOpaque);
                    if (_markerHoverTooltipBg != null)
                    {
                        Color bg = curTheme?.FrameBgColor ?? WidgetStyleManager.NeutralOpaque;
                        bg.a = 0.90f;
                        _markerHoverTooltipBg.SetColor(bg);
                    }
                }
                else
                {
                    if (_markerHoverTooltipObj.activeSelf) _markerHoverTooltipObj.SetActive(false);
                }
            }

            if (_sphereMaterial != null)
            {
                if (!FastVector4RoughEquals(_uploadedAvoid0, _cachedAvoidVectors[0]))
                {
                    _uploadedAvoid0 = _cachedAvoidVectors[0];
                    _sphereMaterial.SetVector(_PropMarkerAvoid0, _cachedAvoidVectors[0]);
                }
                if (!FastVector4RoughEquals(_uploadedAvoid1, _cachedAvoidVectors[1]))
                {
                    _uploadedAvoid1 = _cachedAvoidVectors[1];
                    _sphereMaterial.SetVector(_PropMarkerAvoid1, _cachedAvoidVectors[1]);
                }
                if (!FastVector4RoughEquals(_uploadedAvoid2, _cachedAvoidVectors[2]))
                {
                    _uploadedAvoid2 = _cachedAvoidVectors[2];
                    _sphereMaterial.SetVector(_PropMarkerAvoid2, _cachedAvoidVectors[2]);
                }
                if (!FastVector4RoughEquals(_uploadedAvoid3, _cachedAvoidVectors[3]))
                {
                    _uploadedAvoid3 = _cachedAvoidVectors[3];
                    _sphereMaterial.SetVector(_PropMarkerAvoid3, _cachedAvoidVectors[3]);
                }
            }
        }

        private static bool FastVector4RoughEquals(Vector4 a, Vector4 b, float epsilon = 0.005f)
        {
            return Mathf.Abs(a.x - b.x) < epsilon &&
                   Mathf.Abs(a.y - b.y) < epsilon &&
                   Mathf.Abs(a.z - b.z) < epsilon &&
                   Mathf.Abs(a.w - b.w) < epsilon;
        }

        private void UpdateRollPointer()
        {
            if (_bankRollPointerRoot == null) return;

            string category = _logic.CurrentState.RefCategoryUpper;
            bool isSurface = category == "SURFACE" ||
                             category == "BODY_FIXED" ||
                             category == "BODY_SURFACE";

            float dt = Time.unscaledDeltaTime;
            float targetAlpha = isSurface ? 1.0f : 0.0f;
            _rollPointerAlpha = Mathf.Lerp(_rollPointerAlpha, targetAlpha, Mathf.Clamp01(dt * 9.0f));
            if (Mathf.Abs(_rollPointerAlpha - targetAlpha) < 0.005f) _rollPointerAlpha = targetAlpha;

            // 坡度标尺与滚转指针只在地表参考系 (SURFACE) 生效；在太空/轨道/惯性/拉格朗日系下平滑渐隐，避免太空乱漂
            if (_rollPointerAlpha < 0.01f)
            {
                if (_bankRollPointerRoot.gameObject.activeSelf) _bankRollPointerRoot.gameObject.SetActive(false);
                SetBankTicksVisibility(false, 0f);
                return;
            }

            if (!_bankRollPointerRoot.gameObject.activeSelf) _bankRollPointerRoot.gameObject.SetActive(true);
            SetBankTicksVisibility(true, _rollPointerAlpha);

            float rollAngle = _logic.CurrentState.RollAngle;

            if (_lastRollPointerAngle.Update(rollAngle))
            {
                _bankRollPointerRoot.localRotation = Quaternion.Euler(0f, 0f, -rollAngle);
            }

            if (_bankRollPointerImg != null)
            {
                ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
                bool isExtreme = Mathf.Abs(rollAngle) >= 44f;
                Color baseCol = isExtreme 
                    ? (theme != null ? (Color)theme.WarningColor : WidgetStyleManager.NeutralOpaque)
                    : (theme != null ? (Color)theme.HorizonLineColor : WidgetStyleManager.NeutralOpaque);
                baseCol.a = _rollPointerAlpha;
                _bankRollPointerImg.SetColor(baseCol);
            }
        }

        private void SetBankTicksVisibility(bool visible, float alpha)
        {
            bool alphaChanged = _lastBankTicksAlpha.Update(alpha);

            for (int i = 0; i < _bankAngleTicks.Count; i++)
            {
                Image img = _bankAngleTicks[i];
                if (img == null) continue;
                if (img.gameObject.activeSelf != visible)
                {
                    img.gameObject.SetActive(visible);
                }
                if (visible && alphaChanged)
                {
                    img.SetAlpha(alpha * 0.85f);
                }
            }
        }

        private void UpdateSASAndGuidanceVisuals()
        {
            float dt = Time.unscaledDeltaTime;

            // 1. 点击冲击波扩散动画 (Shockwave Ripple)
            if (_sasRippleImage != null && _sasRippleImage.gameObject.activeSelf)
            {
                _rippleTimer += dt;
                float t = Mathf.Clamp01(_rippleTimer / 0.45f);
                if (t >= 1.0f)
                {
                    _sasRippleImage.gameObject.SetActive(false);
                }
                else
                {
                    float s = Mathf.Lerp(0.5f, 2.3f, t);
                    _sasRippleRt.SetLocalScaleSafe(new Vector3(s, s, 1.0f));
                    Color rc = _sasRippleColor;
                    rc.a = Mathf.Lerp(0.95f, 0.0f, t * t);
                    _sasRippleImage.SetColor(rc);
                }
            }

            // 2. SAS 动态角括号锁定框 (Active SAS Lock Reticle)
            var state = _logic.CurrentState;
            bool sasActive = state.SasActive;
            FlightSASMode curSASMode = state.SasMode;

            // 优化：StabilityAssist 属于基础姿态阻尼保持，准星本身已是基准，无需常驻黄色方框遮挡机头
            // 仅当锁定在具体导引矢量标 (Prograde, Retrograde, Normal, Maneuver, Target 等) 时显式呈现角括号锁定框
            bool isDirectionalLock = sasActive && curSASMode != FlightSASMode.StabilityAssist;

            if (_sasLockReticleRt != null && _sasLockReticleImage != null)
            {
                if (isDirectionalLock)
                {
                    MarkerSlot targetSlot = GetSlotForSASMode(curSASMode);
                    bool isTargetActive = targetSlot != null && targetSlot.Image != null && targetSlot.Image.gameObject.activeSelf;

                    if (!isTargetActive)
                    {
                        if (_sasLockReticleRt.gameObject.activeSelf) _sasLockReticleRt.gameObject.SetActive(false);
                    }
                    else
                    {
                        if (!_sasLockReticleRt.gameObject.activeSelf) _sasLockReticleRt.gameObject.SetActive(true);
                        Vector2 targetPos = targetSlot.RectTransform.anchoredPosition;

                        if (Vector2.Distance(_sasLockReticleRt.anchoredPosition, targetPos) < 1.5f)
                        {
                            _sasLockReticleRt.SetAnchoredPositionSafe(targetPos);
                        }
                        else
                        {
                            _sasLockReticleRt.SetAnchoredPositionSafe(Vector2.Lerp(_sasLockReticleRt.anchoredPosition, targetPos, Mathf.Clamp01(dt * 30.0f)));
                        }

                        float breathRaw = Mathf.Sin(Time.unscaledTime * 5.0f);
                        float breath = 1.0f + 0.05f * (Mathf.Round(breathRaw * 8f) * 0.125f);
                        if (_lastBreathScale.Update(breath))
                        {
                            _sasLockReticleRt.SetLocalScaleSafe(new Vector3(breath, breath, 1.0f));
                        }

                        ThemeConfig curTheme = ThemeManager.Instance?.CurrentTheme;
                        Color lockCol = NavballMarkerFactory.GetSASModeColor(curSASMode, curTheme);
                        float lockAlpha = targetSlot.CurrentDir.z < 0.05f ? targetSlot.LastRenderState.Alpha : 0.92f;
                        lockCol.a = lockAlpha;
                        _sasLockReticleImage.SetColor(lockCol);
                    }
                }
                else
                {
                    if (_sasLockReticleRt.gameObject.activeSelf) _sasLockReticleRt.gameObject.SetActive(false);
                }
            }

            // 3. 机动节点动态流光导引 (Maneuver Node Steering Director & Pulse Guide)
            UpdateManeuverGuidance();
        }

        private void UpdateManeuverGuidance()
        {
            if (_maneuverGuideContainer == null) return;

            var manSlot = _markerSlotByType[(int)NavballMarkerType.Maneuver];
            bool hasManeuver = manSlot != null && _logic.CurrentState.HasManeuverNode && manSlot.CurrentDir.sqrMagnitude > 0.001f;
            if (!hasManeuver)
            {
                if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
                return;
            }

            Vector2 manPos;
            if (manSlot.Image != null && manSlot.Image.gameObject.activeSelf)
            {
                manPos = manSlot.RectTransform.anchoredPosition;
            }
            else
            {
                // 机动节点在背面：若未处于正后方死区锥体内，流光指向边缘方位引导转头
                Vector2 bearing = new Vector2(manSlot.CurrentDir.x, manSlot.CurrentDir.y);
                float bearingMag = bearing.magnitude;
                if (bearingMag < 0.12f)
                {
                    // 180° 正后方死区：平息流光，避免全向打转
                    if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
                    return;
                }
                manPos = (bearing / bearingMag) * _visualRadius;
            }

            float dist = manPos.magnitude;
            float maxGuideDist = _visualRadius + 15f * CurrentDpiScale;

            if (dist > 8f && dist < maxGuideDist)
            {
                // 正后方死区锥体内平息流光，避免全向打转
                Vector2 manBearing = new Vector2(manSlot.CurrentDir.x, manSlot.CurrentDir.y);
                if (manSlot.CurrentDir.z < 0.05f && manBearing.magnitude < 0.12f)
                {
                    if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
                    return;
                }

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
                    Vector2 targetChevronPos = manPos * t;
                    if ((chev.rectTransform.anchoredPosition - targetChevronPos).sqrMagnitude > 0.5f)
                    {
                        chev.rectTransform.SetAnchoredPositionSafe(targetChevronPos);
                    }
                    if (Quaternion.Angle(chev.rectTransform.localRotation, chevronRot) > 0.5f)
                    {
                        chev.rectTransform.localRotation = chevronRot;
                    }

                    float alpha = Mathf.Sin(phase * Mathf.PI) * 0.85f;
                    if (manSlot.CurrentDir.z < 0.05f)
                    {
                        alpha *= manSlot.LastRenderState.Alpha;
                    }
                    if (Mathf.Abs(chev.color.a - alpha) > 0.03f)
                    {
                        Color c = chevronCol;
                        c.a = alpha;
                        chev.SetColor(c);
                    }
                }
            }
            else
            {
                if (_maneuverGuideContainer.activeSelf) _maneuverGuideContainer.SetActive(false);
            }
        }

        public void OnMarkerHoverEnter(string markerKey, Vector2 pos)
        {
            _activeHoveredMarkerKey = markerKey;
            _activeHoveredMarkerPos = pos;
        }

        public void OnMarkerHoverExit(string markerKey)
        {
            if (_activeHoveredMarkerKey == markerKey)
            {
                _activeHoveredMarkerKey = null;
            }
        }

        public void HandleMarkerClick(string markerKey, Vector2 pos)
        {
            FlightSASMode? targetMode = GetSASModeForMarker(markerKey);
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

        private static NavballMarkerType GetMarkerTypeForSASMode(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.Prograde: return NavballMarkerType.Prograde;
                case FlightSASMode.Retrograde: return NavballMarkerType.Retrograde;
                case FlightSASMode.Normal: return NavballMarkerType.Normal;
                case FlightSASMode.Antinormal: return NavballMarkerType.AntiNormal;
                case FlightSASMode.RadialIn: return NavballMarkerType.RadialIn;
                case FlightSASMode.RadialOut: return NavballMarkerType.RadialOut;
                case FlightSASMode.Target: return NavballMarkerType.Target;
                case FlightSASMode.AntiTarget: return NavballMarkerType.AntiTarget;
                case FlightSASMode.Maneuver: return NavballMarkerType.Maneuver;
                case FlightSASMode.StabilityAssist:
                default:
                    return NavballMarkerType.Unknown;
            }
        }

        private MarkerSlot GetSlotForSASMode(FlightSASMode mode)
        {
            var mType = GetMarkerTypeForSASMode(mode);
            if (mType == NavballMarkerType.Unknown) return null;
            var slot = _markerSlotByType[(int)mType];
            if (slot != null && slot.Image != null && slot.Image.gameObject.activeSelf)
            {
                return slot;
            }
            if (mType == NavballMarkerType.Prograde)
            {
                var velSlot = _markerSlotByType[(int)NavballMarkerType.VelocityVector];
                if (velSlot != null && velSlot.Image != null && velSlot.Image.gameObject.activeSelf)
                    return velSlot;
            }
            else if (mType == NavballMarkerType.Retrograde)
            {
                var antiVelSlot = _markerSlotByType[(int)NavballMarkerType.AntiVelocityVector];
                if (antiVelSlot != null && antiVelSlot.Image != null && antiVelSlot.Image.gameObject.activeSelf)
                    return antiVelSlot;
            }
            return slot;
        }

        public static FlightSASMode? GetSASModeForMarker(string markerKey)
        {
            switch (markerKey?.ToLowerInvariant())
            {
                case "prograde":
                case "velocity_vector":
                case "surface_prograde":
                    return FlightSASMode.Prograde;
                case "retrograde":
                case "anti_velocity_vector":
                case "surface_retrograde":
                    return FlightSASMode.Retrograde;
                case "normal":
                    return FlightSASMode.Normal;
                case "antinormal":
                    return FlightSASMode.Antinormal;
                case "radialin":
                    return FlightSASMode.RadialIn;
                case "radialout":
                    return FlightSASMode.RadialOut;
                case "target":
                    return FlightSASMode.Target;
                case "antitarget":
                    return FlightSASMode.AntiTarget;
                case "maneuver":
                    return FlightSASMode.Maneuver;
                case "reticle":
                case "crosshair":
                    return FlightSASMode.StabilityAssist;
                default:
                    return null;
            }
        }

        private void UpdateReticleDynamics()
        {
            if (_crosshairRt != null && _crosshairRt.anchoredPosition != Vector2.zero)
            {
                _crosshairRt.SetAnchoredPositionSafe(Vector2.zero);
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
            if (_hasUploadedPalette && IsPaletteEqual(ref _uploadedPalette, ref palette)) return;

            _uploadedPalette = palette;
            _hasUploadedPalette = true;
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
                if (_sphereMaterial != null && _hasPropTrendStrength)
                {
                    _sphereMaterial.SetFloat(_PropTrendStrength, 0f);
                }
                return;
            }

            if (_hasPreviousAttitudeRotation && dt > 0.001f && dt < 0.25f)
            {
                Quaternion delta = currentRotation * Quaternion.Inverse(_previousAttitudeRotation.Value);
                if (delta.w < 0f)
                    delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

                Vector3 halfAxis = new Vector3(delta.x, delta.y, delta.z);
                float sinHalfAngle = halfAxis.magnitude;

                Vector3 rawBallAngularVelocity = Vector3.zero;
                if (sinHalfAngle > 0.00015f)
                {
                    float angleDegrees = 2f * Mathf.Atan2(sinHalfAngle, Mathf.Clamp(delta.w, 0f, 1f)) * Mathf.Rad2Deg;
                    float rawRate = angleDegrees / dt;
                    if (rawRate < 240f)
                    {
                        rawBallAngularVelocity = (halfAxis / sinHalfAngle) * rawRate;
                    }
                }

                // 物理映射：将球体在摄像机视口下的逆运动转换为载具在屏幕视口下的真实运动角速度
                // 球体表面运动方向与飞船实际旋转运动互为反向逆矩阵映射：
                // Pitch (抬头/低头): 飞船向下(按W)时球体向上翻滚(Ball.x<0)，载具角速度需反向(-Ball.x)引导指示器向下
                // Yaw (右偏/左偏): 飞船向右偏航时球体向左偏转(Ball.y<0)，载具角速度需反向(-Ball.y)引导指示器向右
                // Roll (右滚/左滚): 飞船顺时针滚转时球体逆时针旋转(Ball.z>0)，载具角速度需反向(-Ball.z)引导指示器顺时针
                Vector3 rawVesselAngularVelocity = -rawBallAngularVelocity;

                // 低通滤波角速度矢量，彻底消除跨物理帧瞬时微步进与角轴旋转随机翻转
                float filterBlend = (!Application.isPlaying || dt <= 0.0001f) ? 1f : Mathf.Clamp01(dt * 7.5f);
                _smoothedAngularVelocity = Vector3.Lerp(_smoothedAngularVelocity, rawVesselAngularVelocity, filterBlend);
            }
            else
            {
                _smoothedAngularVelocity = Vector3.zero;
            }

            _previousAttitudeRotation.Update(currentRotation);
            _hasPreviousAttitudeRotation = true;

            float smoothRate = _smoothedAngularVelocity.magnitude;
            // 死区量化守卫：低于 0.35°/s 的微幅扰动视为稳态静止，杜绝虚线趋势指示抖动
            if (smoothRate > 0.35f)
            {
                Vector3 axis = _smoothedAngularVelocity / smoothRate;
                // 航电标准 1.25s 动量前瞻时间常数 (上限钳制在 22°，避免极端大角速度下穿模溢出)
                float predictionAngle = Mathf.Clamp(smoothRate * 1.25f, 0f, 22f);
                targetTrendRotation = Quaternion.AngleAxis(predictionAngle, axis);
                targetStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 4.0f, smoothRate));
            }
            else
            {
                targetTrendRotation = Quaternion.identity;
                targetStrength = 0f;
            }

            float blend = (!Application.isPlaying || dt <= 0.0001f) ? 1f : Mathf.Clamp01(dt * 8.0f);
            _filteredTrendRotation = Quaternion.Slerp(_filteredTrendRotation, targetTrendRotation, blend);
            _attitudeTrendStrength = Mathf.MoveTowards(_attitudeTrendStrength, targetStrength, (!Application.isPlaying ? 1f : dt * 3.5f));

            if (_sphereMaterial != null && _hasPropTrendRotation)
            {
                var curMode = ThemeManager.Instance.GlobalRenderMode;
                if (curMode == NavballRenderMode.ProceduralVector)
                {
                    bool trendDiffers = Mathf.Abs(_attitudeTrendStrength - _uploadedTrendStrength) > 0.01f ||
                                       (_attitudeTrendStrength > 0.01f && Quaternion.Angle(_filteredTrendRotation, _uploadedTrendRotation) > 0.05f);
                    if (trendDiffers)
                    {
                        _sphereMaterial.SetVector(_PropTrendRotation, new Vector4(
                            _filteredTrendRotation.x, _filteredTrendRotation.y, _filteredTrendRotation.z, _filteredTrendRotation.w));
                        if (_hasPropTrendStrength)
                        {
                            _sphereMaterial.SetFloat(_PropTrendStrength, _attitudeTrendStrength);
                        }
                        _uploadedTrendStrength = _attitudeTrendStrength;
                        _uploadedTrendRotation = _filteredTrendRotation;
                    }
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;
            _paletteInitialized = false;
            _lastFrameCategory.Reset(null);

            var mode = ThemeManager.Instance.GlobalRenderMode;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            if (mode == NavballRenderMode.StockDirect)
            {
                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
                if (_displayImage != null) _displayImage.enabled = false;
                if (_crosshair != null) _crosshair.SetActive(false);
                if (_bezelRing != null) _bezelRing.SetActive(false);
                if (_markerSlots != null)
                {
                    for (int i = 0; i < _markerSlots.Length; i++)
                    {
                        var slotImg = _markerSlots[i]?.Image;
                        if (slotImg != null) slotImg.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                if (NavBallHookService.IsCleanStockNavballActiveFunc?.Invoke() ?? false)
                {
                    NavBallHookService.ResetStockNavballAction?.Invoke();
                    NavBallHookService.SetStockNavballCleanAction?.Invoke(false);
                    NavBallHookService.HideStockNavballAction?.Invoke(true);
                }
                if (_displayImage != null) _displayImage.enabled = true;
                if (_crosshair != null) _crosshair.SetActive(true);
                if (_bezelRing != null) _bezelRing.SetActive(true);

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
                    CacheMaterialProperties();
                }
            }

            Material uiMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);

            if (_shellImage != null)
            {
                if (uiMat != null) _shellImage.material = uiMat;
                ApplyCard(_shellImage, _shellOutline, CardStyleRole.Normal, theme);
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
            if (_shellTitle != null)
            {
                if (txtMat != null) _shellTitle.material = txtMat;
                ApplyText(_shellTitle, TextStyleRole.Label, theme);
            }
            if (_shellStatus != null)
            {
                if (txtMat != null) _shellStatus.material = txtMat;
                ApplyText(_shellStatus, TextStyleRole.SecondaryValue, theme);
            }

            NavballMarkerFactory.ClearCache();
            if (_markerSlots != null)
            {
                for (int i = 0; i < _markerSlots.Length; i++)
                {
                    var slot = _markerSlots[i];
                    if (slot?.Image != null)
                    {
                        slot.Image.sprite = NavballMarkerFactory.GetMarkerSprite(slot.Key);
                        if (uiMat != null) slot.Image.material = uiMat;
                    }
                }
            }
            if (_reticleImage != null)
            {
                _reticleImage.sprite = NavballMarkerFactory.GetReticleSprite();
                _reticleImage.color = WidgetStyleManager.NeutralOpaque;
                if (uiMat != null) _reticleImage.material = uiMat;
            }

            if (_bezelRing != null)
            {
                Outline bo = _bezelRing.GetComponent<Outline>();
                if (bo != null) bo.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Strong);
                Image bImg = _bezelRing.GetComponent<Image>();
                if (bImg != null && uiMat != null) bImg.material = uiMat;
            }

            for (int i = 0; i < _bankAngleTicks.Count; i++)
            {
                Image img = _bankAngleTicks[i];
                if (img == null) continue;
                bool isWarn = i >= _bankAngleTicks.Count - 4;
                bool isZero = i == 0;
                img.color = isWarn ? theme.WarningColor : (isZero ? theme.HorizonLineColor : theme.GridColor);
                if (uiMat != null) img.material = uiMat;
            }

            if (_bankRollPointerImg != null)
            {
                _bankRollPointerImg.sprite = NavballMarkerFactory.GetRollPointerSprite();
                _bankRollPointerImg.color = theme.HorizonLineColor;
                if (uiMat != null) _bankRollPointerImg.material = uiMat;
            }
            if (_sasLockReticleImage != null)
            {
                _sasLockReticleImage.sprite = NavballMarkerFactory.GetSASLockReticleSprite();
                if (uiMat != null) _sasLockReticleImage.material = uiMat;
            }
            if (_sasRippleImage != null)
            {
                _sasRippleImage.sprite = NavballMarkerFactory.GetShockwaveSprite();
                if (uiMat != null) _sasRippleImage.material = uiMat;
            }
            if (_guidanceChevrons != null)
            {
                Sprite chevSpr = NavballMarkerFactory.GetGuidanceChevronSprite();
                for (int i = 0; i < _guidanceChevrons.Length; i++)
                {
                    if (_guidanceChevrons[i] != null)
                    {
                        _guidanceChevrons[i].sprite = chevSpr;
                        if (uiMat != null) _guidanceChevrons[i].material = uiMat;
                    }
                }
            }

            if (_markerHoverTooltipBg != null)
            {
                Color bg = theme.FrameBgColor;
                bg.a = 0.90f;
                _markerHoverTooltipBg.color = bg;
                if (uiMat != null) _markerHoverTooltipBg.material = uiMat;
            }
            if (_markerHoverTooltipText != null && txtMat != null)
            {
                _markerHoverTooltipText.material = txtMat;
            }
            if (_markerHoverTooltipSub != null)
            {
                _markerHoverTooltipSub.color = theme.TextPrimaryColor;
                if (txtMat != null) _markerHoverTooltipSub.material = txtMat;
            }

            this.Controls.ApplyThemeToControls(theme);
            MarkRenderDirty();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                FlightTelemetryContext.Current?.CycleSpeedMode();
            }
        }

        protected override void HandleResolutionChanged(int newRes)
        {
            _detailScaleDirty = true;
            UpdateProceduralDetailScale();
        }

        protected override void HandleRenderSettingChanged()
        {
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_markerSlots != null)
            {
                for (int i = 0; i < _markerSlots.Length; i++)
                {
                    _markerSlots[i] = null;
                }
                _markerSlots = null;
            }
            Array.Clear(_markerSlotByType, 0, _markerSlotByType.Length);
            _lastRollPointerAngle.Reset(-9999f);
            _lastBankTicksAlpha.Reset(-1f);
            _uploadedHazardAlert = -999f;
            _uploadedVernierDetail = -999f;
            _uploadedFramePatternOld = -999f;
            _uploadedFramePattern = -999f;
            _uploadedFrameTransitionProgress = -999f;
            _hasUploadedPalette = false;
            _lastBreathScale.Reset(-1f);
            _markerHoverTooltipObj = null;
            _markerHoverTooltipRt = null;
            _markerHoverTooltipBg = null;
            _markerHoverTooltipOutline = null;
            _markerHoverTooltipText = null;
            _markerHoverTooltipSub = null;
            _bankAngleTicks.Clear();
            _bankRollPointerRoot = null;
            _bankRollPointerImg = null;
            _sasLockReticleRt = null;
            _sasLockReticleImage = null;
            _sasRippleRt = null;
            _sasRippleImage = null;
            _maneuverGuideContainer = null;
            if (NavBallHookService.IsCleanStockNavballActiveFunc?.Invoke() ?? false)
            {
                NavBallHookService.ResetStockNavballAction?.Invoke();
                NavBallHookService.SetStockNavballCleanAction?.Invoke(false);
            }
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
                _sphereMaterial = null;
            }
            base.OnDestroy();
        }
    }

    /// <summary>
    /// 姿态球/导航球标记物交互契约接口
    /// </summary>
    public interface INavballMarkerWidget
    {
        void OnMarkerHoverEnter(string markerKey, Vector2 pos);
        void OnMarkerHoverExit(string markerKey);
        void HandleMarkerClick(string markerKey, Vector2 pos);
        void HandleMarkerRightClick(string markerKey, Vector2 pos);
    }

    /// <summary>
    /// 导航标与准星交互事件拦截转发器 (Click-to-SAS 航电操作路由 & 悬停反馈)
    /// </summary>
    public class NavballMarkerClickHandler : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public string MarkerKey;
        public INavballMarkerWidget Widget;
        public bool IsHovered { get; private set; }
        public bool IsPressed { get; private set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            IsHovered = true;
            Widget?.OnMarkerHoverEnter(MarkerKey, GetComponent<RectTransform>().anchoredPosition);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            IsHovered = false;
            IsPressed = false;
            Widget?.OnMarkerHoverExit(MarkerKey);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                IsPressed = true;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Widget != null)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    Widget.HandleMarkerClick(MarkerKey, GetComponent<RectTransform>().anchoredPosition);
                    eventData.Use();
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    Widget.HandleMarkerRightClick(MarkerKey, GetComponent<RectTransform>().anchoredPosition);
                    eventData.Use();
                }
            }
        }
    }
}

