using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Rendering;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// 3D 飞船球形姿态仪 / 导航球仪表 (Vessel Attitude Sphere / 3D Navball Widget)
    /// 核心特性：
    /// 1. 球形姿态表达 (Spherical Attitude Representation)：
    ///    - 优先采用现代单 Quad 屏幕空间数学解析光线投射 (Screen-Space Analytic Raymarching) 管线，
    ///      完美规避场景阳光过曝洗白与 3D 粗糙多边形棱角缺陷，输出视网膜级超清画质；
    ///    - 兼容极简 3D 离屏着色器网格管线 (MinimalistAttitudeSphere)，支持全系多参考系天顶/地平渐变与抗锯齿俯仰阶梯；
    ///    - 完整对接 NavBallHookService 原生与 Principia 参考系，以及球面正交平滑投影导航标记。
    /// 2. 中央 3D 飞船实体 (3D Spacecraft at Center)：
    ///    - 取代原版扁平准星，在球体正中心呈现立体航天器。
    ///    - 默认采用高保真 Half-Lambert 光照着色 3D 航天器模型（座舱反光、三角主翼、编队灯与离子羽流）；
    ///    - 支持切换至 Vessel3DService.Provider?.Texture3D 机尾正视追随视角 (TailChase)，或 2D 权威俯视剪影；
    ///    - 具备俯仰透视收缩 cos(Pitch * 0.5) 动态响应。
    /// 3. 水平基准定位翼 (Aerospace Horizon Reticle Index Wings)：
    ///    - 权威仪表水线基准标记，始终保持水平正交（0 异常倾角），为飞行员提供可靠的地平线参考。
    /// 4. 飞行指引仪 (Flight Director Target Chevron)：
    ///    - 机头前向反 V 形高亮指引光标，追踪当前 SAS 模式机动目标向量，并在 <= 1.5° 时吸附锁定变绿。
    /// 5. 航电读数与参考系标牌：
    ///    - 顶部弧形标牌：参考系与航向角（HDG {HDG:F0}° | {FRAME}）。
    ///    - 底部状态标牌：俯仰/滚转角（P {PITCH:+0;-0;0}° R {ROLL:+0;-0;0}° | {SAS}）。
    /// 6. 交互控制：
    ///    - 左键点击底部标牌切换 SAS / STAB 稳定。
    ///    - 左键点击中央飞船切换显示模型（程序化 3D 穿梭机 / 载具 3D 模型 / 2D 剪影）。
    ///    - 右键点击中央飞船切换观察视角（追尾 3D / 俯视 3D）。
    /// 7. 严格落实 MFP-SPEC-001..011 铁律（0 颜色字面量、0 场景查询、分频阶梯 Critical 60Hz、2D UI Shader 材质管线接入）。
    /// </summary>
    [AlwaysFullPower]
    [FlightWidget("vessel_navball", "vessel_attitude_sphere", "attitude_sphere", Category = WidgetCategory.Navigation, DisplayName = "3D 飞船球形姿态仪", Description = "全新球形姿态仪：以真实 3D 飞船为中心，外层环绕 3D 姿态球体、人工地平标尺、SAS 目标飞行指引仪与全量导航矢量。", DefaultWidgetId = "nav.vessel_navball", DefaultX = 0f, DefaultY = 0f, IsSingleton = true, HighFrequency = true, AlwaysFullPower = true, ExactIds = new[] { "nav.vessel_navball", "nav.vessel_attitude_sphere", "core.vessel_navball", "core.vessel_attitude_sphere", "nav.attitude_sphere_3d" })]
    public class VesselAttitudeSphereWidget : BaseNavballSphereWidget, IPointerClickHandler
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        public enum CenterShipVisualMode
        {
            Procedural3D = 0,
            RealVessel3D = 1,
            TopDownSilhouette = 2
        }

        private bool _cachedHasVessel;
        private float _cachedPitch;
        private float _cachedRoll;
        private float _cachedHeading;
        private Texture _cachedTex3D;
        private string _cachedTopFormatted;
        private string _cachedBtmFormatted;
        private bool _cachedDirectorActive;
        private bool _cachedDirectorLocked;
        private float _cachedDeflX;
        private float _cachedDeflY;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            if (context.Telemetry == null || !context.Telemetry.HasVessel)
            {
                _cachedHasVessel = false;
                return;
            }

            _cachedHasVessel = true;
            _cachedPitch = (float)context.Telemetry.Pitch;
            _cachedRoll = (float)context.Telemetry.Roll;
            _cachedHeading = (float)context.Telemetry.Heading;

            // 当选择 RealVessel3D 时，自动将烘焙器视图配置为 TailChase
            if (_shipVisualMode == CenterShipVisualMode.RealVessel3D && Vessel3DService.Provider != null)
            {
                if (Vessel3DService.Provider.ViewMode != Vessel3DViewMode.TailChase)
                {
                    Vessel3DService.Provider.ViewMode = Vessel3DViewMode.TailChase;
                }
                _cachedTex3D = Vessel3DService.Provider.Texture3D;
            }
            else
            {
                _cachedTex3D = null;
            }

            int hInt = Mathf.RoundToInt(_cachedHeading) % 360;
            if (hInt < 0) hInt += 360;
            var hook = NavBallHookService.Provider;
            string frameCat = hook?.ReferenceFrameCategory ?? "SURFACE";
            bool hdgDirty = _lastHdgInt.Update(hInt);
            bool catDirty = _lastTopFrameCat.Update(frameCat);
            if (hdgDirty || catDirty)
            {
                string hdgPart = (_headingToken == "{HDG}") ? CacheManager.FastHdg(hInt) : $"HDG {TelemetryTokenEngine.Evaluate(_headingToken, context.Telemetry)}";
                _cachedTopFormatted = $"{hdgPart} | {frameCat}";
            }

            FlightSASMode curSASMode = context.Telemetry.CurrentSASMode;
            bool sasActive = context.Telemetry.IsSASEnabled;
            if (!sasActive)
            {
                _cachedDirectorActive = false;
                _isDirectorLocked = false;
            }
            else
            {
                string sasModeKey = GetSASModeKey(curSASMode);
                Vector3 targetDir = Vector3.forward;
                bool isVis = false;
                bool hasDir = false;

                if (hook != null)
                {
                    hasDir = hook.GetMarkerDirection(sasModeKey, out targetDir, out isVis);
                }
                if (!hasDir && NavBallHookService.MarkerDirectionFallback != null)
                {
                    hasDir = NavBallHookService.MarkerDirectionFallback(sasModeKey, out targetDir, out isVis);
                }

                if (!hasDir || !isVis)
                {
                    _cachedDirectorActive = false;
                    _isDirectorLocked = false;
                }
                else
                {
                    _cachedDirectorActive = true;
                    float deflX = Mathf.Clamp(targetDir.x, -1f, 1f);
                    float deflY = Mathf.Clamp(targetDir.y, -1f, 1f);
                    float angError = Mathf.Atan2(Mathf.Sqrt(targetDir.x * targetDir.x + targetDir.y * targetDir.y), Mathf.Max(0.001f, targetDir.z)) * Mathf.Rad2Deg;

                    bool targetLocked = _isDirectorLocked ? (angError <= 1.8f) : (angError <= 1.3f);
                    _isDirectorLocked = targetLocked;
                    _cachedDirectorLocked = targetLocked;
                    _cachedDeflX = deflX;
                    _cachedDeflY = deflY;
                }
            }

            int pInt = Mathf.RoundToInt(_cachedPitch);
            int rInt = Mathf.RoundToInt(_cachedRoll);
            bool pDirty = _lastPitchInt.Update(pInt);
            bool rDirty = _lastRollInt.Update(rInt);
            bool sasDirty = _lastSASMode.Update(curSASMode);
            bool dirDirty = _lastDirectorLocked.Update(_isDirectorLocked);
            if (pDirty || rDirty || sasDirty || dirDirty)
            {
                string pStr = (_pitchToken == "{PITCH}") ? CacheManager.FastInt(pInt) : TelemetryTokenEngine.Evaluate(_pitchToken, context.Telemetry);
                if (pStr.EndsWith("°")) pStr = pStr.Substring(0, pStr.Length - 1).Trim();

                string rStr = (_rollToken == "{ROLL}") ? CacheManager.FastInt(rInt) : TelemetryTokenEngine.Evaluate(_rollToken, context.Telemetry);
                if (rStr.EndsWith("°")) rStr = rStr.Substring(0, rStr.Length - 1).Trim();

                string sasMode = (_sasToken == "{SAS:MODE}") ? GetSASModeDisplayText(curSASMode) : TelemetryTokenEngine.Evaluate(_sasToken, context.Telemetry);

                string lockTag = _isDirectorLocked ? " [LOCK]" : "";
                _cachedBtmFormatted = $"P {pStr}° R {rStr}° | {sasMode}{lockTag}";
            }

            _lastPitch.Update(context.Telemetry.Pitch);
            _lastRoll.Update(context.Telemetry.Roll);
            _lastHeading.Update(context.Telemetry.Heading);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            // 驱动 3D 姿态仪离屏相机渲染与脏标记复位 (仅在网格渲染管线下工作)
            if (!_isUsingRaymarch && _ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                bool rotDirty = !_hasEverRendered || (_sphereObject != null && Quaternion.Angle(_sphereObject.transform.localRotation, _lastRenderedRotation.Value) > RotationDirtyThreshold);
                bool heartbeatDirty = (Time.unscaledTime - _lastRenderedTime.Value) >= HeartbeatInterval;

                if (rotDirty || _isPaletteLerping || _isMaterialDirty || heartbeatDirty || _isRenderDirty)
                {
                    context.Navball.RenderCamera();
                    _hasEverRendered = true;
                    if (_sphereObject != null)
                    {
                        _lastRenderedRotation.Update(_sphereObject.transform.localRotation);
                    }
                    _lastRenderedTime.Update(Time.unscaledTime);
                    _isMaterialDirty = false;
                    _isRenderDirty = false;
                }
            }

            if (!_cachedHasVessel) return;

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance.CurrentTheme;

            // 1. 中央飞船贴图与视图选择
            if (_shipRawImage != null)
            {
                Texture targetTex = _shared3DSpacecraftTexture;
                if (_shipVisualMode == CenterShipVisualMode.RealVessel3D && _cachedTex3D != null)
                {
                    targetTex = _cachedTex3D;
                }
                else if (_shipVisualMode == CenterShipVisualMode.TopDownSilhouette)
                {
                    Texture silTex = VesselSilhouetteService.Provider?.SilhouetteTexture;
                    if (silTex != null) targetTex = silTex;
                }

                if (_shipRawImage.texture != targetTex)
                {
                    _shipRawImage.texture = targetTex;
                }
            }

            // 2. 中央 3D 飞船姿态与俯仰收缩
            if (_centerShipRoot != null)
            {
                if (_isChasePerspective)
                {
                    float pitchRad = _cachedPitch * Mathf.Deg2Rad;
                    float foreshortenY = Mathf.Clamp(Mathf.Cos(pitchRad * 0.5f), 0.72f, 1.0f);
                    if (_lastShipScaleY.Update(foreshortenY))
                    {
                        _centerShipRoot.localScale = new Vector3(1.0f, foreshortenY, 1.0f);
                    }
                    _centerShipRoot.localRotation = Quaternion.identity;
                }
                else
                {
                    if (_lastShipScaleY.Update(1.0f))
                    {
                        _centerShipRoot.localScale = Vector3.one;
                    }
                    _centerShipRoot.localRotation = Quaternion.Euler(0f, 0f, -_cachedRoll);
                }
            }

            // 3. 飞行指引仪 Target Flight Director
            if (_flightDirectorRoot != null)
            {
                if (!_cachedDirectorActive)
                {
                    if (_flightDirectorRoot.gameObject.activeSelf) _flightDirectorRoot.gameObject.SetActive(false);
                }
                else
                {
                    if (!_flightDirectorRoot.gameObject.activeSelf) _flightDirectorRoot.gameObject.SetActive(true);

                    float s = CurrentDpiScale;
                    float maxDeflection = 22f * s;
                    Vector2 targetPos = _cachedDirectorLocked
                        ? new Vector2(0f, 14f * s)
                        : new Vector2(_cachedDeflX * maxDeflection, 14f * s + _cachedDeflY * maxDeflection);

                    float dt = context.DeltaTime;
                    float lerpT = (!Application.isPlaying || dt <= 0.0001f) ? 1.0f : Mathf.Clamp01(dt * 14.0f);
                    _flightDirectorRoot.anchoredPosition = Vector2.Lerp(_flightDirectorRoot.anchoredPosition, targetPos, lerpT);

                    if (_flightDirectorRawImage != null)
                    {
                        Color targetCol = _cachedDirectorLocked
                            ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                            : WidgetStyleManager.WithAlpha(theme.AccentWarning, 0.90f);
                        if (_flightDirectorRawImage.color != targetCol)
                            _flightDirectorRawImage.color = targetCol;
                    }
                }
            }

            // 4. 顶部航向与参考系标牌更新
            if (_lastTopText.Update(_cachedTopFormatted))
            {
                SetTextIfChanged(_topBadgeText, _cachedTopFormatted);
            }

            // 5. 底部俯仰/滚转与 SAS 状态更新
            if (_lastBottomText.Update(_cachedBtmFormatted))
            {
                SetTextIfChanged(_bottomBadgeText, _cachedBtmFormatted);
            }
        }

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;
        private RawImage _sphereDisplayImage;

        // 水平基准定位翼 (保持水平正交基准)
        private RectTransform _reticleWingsRoot;
        private GameObject _wingL;
        private GameObject _wingR;
        private GameObject _pipL;
        private GameObject _pipR;

        // 中央 3D 飞船机构
        private RectTransform _centerShipRoot;
        private RawImage _shipRawImage;
        private RectTransform _flightDirectorRoot;
        private RawImage _flightDirectorRawImage;

        // 外部圆环包边与装饰刻度
        private GameObject _bezelRingObj;
        private RawImage _bezelRingRawImage;

        // 航电读数标牌
        private GameObject _topBadgeRoot;
        private Text _topBadgeText;
        private GameObject _bottomBadgeRoot;
        private Text _bottomBadgeText;

        // 球面导航标线集合 (Prograde, Retrograde, Normal, Maneuver 等)
        private readonly Dictionary<string, Image> _markerImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private float _visualRadius = 70f;

        // 渲染与视口模式
        private bool _isUsingRaymarch = false;
        private CenterShipVisualMode _shipVisualMode = CenterShipVisualMode.Procedural3D;
        private bool _isDirectorLocked = false;
        private bool _isChasePerspective = true;

        // 静态共享程序化纹理 (避免重复分配)
        private static Texture2D _shared3DSpacecraftTexture;
        private static Texture2D _sharedFlightDirectorTexture;
        private static Texture2D _sharedBezelTexture;

        // 姿态缓存与脏标记
        private readonly CachedDouble _lastPitch = new CachedDouble(double.NaN, 0.05);
        private readonly CachedDouble _lastRoll = new CachedDouble(double.NaN, 0.05);
        private readonly CachedDouble _lastHeading = new CachedDouble(double.NaN, 0.05);
        private readonly Cached<int> _lastHdgInt = new Cached<int>(-1);
        private readonly Cached<string> _lastTopFrameCat = new Cached<string>(null);
        private readonly Cached<int> _lastPitchInt = new Cached<int>(-9999);
        private readonly Cached<int> _lastRollInt = new Cached<int>(-9999);
        private readonly Cached<FlightSASMode> _lastSASMode = new Cached<FlightSASMode>((FlightSASMode)(-1));
        private readonly Cached<bool> _lastDirectorLocked = new Cached<bool>(false);
        private readonly Cached<string> _lastTopText = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBottomText = new Cached<string>(string.Empty);

        // 多参考系调色板过渡
        private readonly Cached<string> _lastFrameCategory = new Cached<string>("");
        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;
        private bool _isPaletteLerping = false;

        // 动态绘制与亚像素脏标记判定 (Zero Visual Quality Loss)
        private const float RotationDirtyThreshold = 0.025f;
        private const float HeartbeatInterval = 0.25f;
        private readonly Cached<Quaternion> _lastRenderedRotation = new Cached<Quaternion>(Quaternion.identity);
        private readonly CachedFloat _lastRenderedTime = new CachedFloat(-10f, 0.001f);
        private readonly CachedFloat _lastShipScaleY = new CachedFloat(-1f, 0.002f);
        private bool _isMaterialDirty = true;
        private bool _hasEverRendered = false;

        private static readonly int _PropSkyColor = Shader.PropertyToID("_SkyColor");
        private static readonly int _PropGroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int _PropEquatorColor = Shader.PropertyToID("_EquatorColor");
        private static readonly int _PropPitchLadderColor = Shader.PropertyToID("_PitchLadderColor");
        private static readonly int _PropMeridianColor = Shader.PropertyToID("_MeridianColor");
        private static readonly int _PropRimColor = Shader.PropertyToID("_RimColor");
        private static readonly int _PropSkyZenithColor = Shader.PropertyToID("_SkyZenithColor");
        private static readonly int _PropSkyHorizonColor = Shader.PropertyToID("_SkyHorizonColor");
        private static readonly int _PropGroundHorizonColor = Shader.PropertyToID("_GroundHorizonColor");
        private static readonly int _PropGroundNadirColor = Shader.PropertyToID("_GroundNadirColor");
        private static readonly int _PropHeadingLineColor = Shader.PropertyToID("_HeadingLineColor");
        private static readonly int _PropSphereInvRotation = Shader.PropertyToID("_SphereInvRotation");
        private static readonly int _PropRenderMode = Shader.PropertyToID("_RenderMode");

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

        // CustomTemplate 通道
        private string _headingToken = "{HDG}";
        private string _pitchToken = "{PITCH}";
        private string _rollToken = "{ROLL}";
        private string _sasToken = "{SAS:MODE}";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float ballDiameter = 150f * s;
            float totalHeight = ballDiameter + 28f * s;
            RectTransform.sizeDelta = new Vector2(ballDiameter, totalHeight);
            _visualRadius = ballDiameter * 0.47f;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _bgOutline.effectColor = Color.clear;

            _headingToken = GetTemplateChannel("HDG", _headingToken);
            _pitchToken = GetTemplateChannel("PITCH", _pitchToken);
            _rollToken = GetTemplateChannel("ROLL", _rollToken);
            _sasToken = GetTemplateChannel("SAS", _sasToken);

            string viewVal = GetTemplateChannel("VIEW", null);
            if (!string.IsNullOrEmpty(viewVal))
            {
                _isChasePerspective = !viewVal.Equals("TOP", StringComparison.OrdinalIgnoreCase);
            }

            string shipModeStr = GetTemplateChannel("SHIP", null);
            if (!string.IsNullOrEmpty(shipModeStr))
            {
                if ("VESSEL".Equals(shipModeStr, StringComparison.OrdinalIgnoreCase) || "REAL".Equals(shipModeStr, StringComparison.OrdinalIgnoreCase))
                {
                    _shipVisualMode = CenterShipVisualMode.RealVessel3D;
                }
                else if ("SILHOUETTE".Equals(shipModeStr, StringComparison.OrdinalIgnoreCase))
                {
                    _shipVisualMode = CenterShipVisualMode.TopDownSilhouette;
                }
                else
                {
                    _shipVisualMode = CenterShipVisualMode.Procedural3D;
                }
            }

            EnsureSharedTextures();

            // 1. 初始化姿态球渲染管线 (优先单 Quad 纯矢量光线投射，规避场景阳光洗白)
            InitializeAttitudeSphere(ballDiameter, theme);

            // 2. 外部航电金属刻度圆环 (Bezel Ring)
            CreateBezelRing(ballDiameter, s, theme);

            // 3. 球面 2D/3D 导航矢量标记层 (Marker Layer)
            CreateMarkerOverlayLayer(transform, s);

            // 4. 水平基准指示翼 (Aerospace Horizon Reticle Index Wings)
            CreateReticleWings(s, theme);

            // 5. 中央 3D 飞船机构 (Center 3D Spacecraft & Flight Director)
            CreateCenter3DSpacecraft(ballDiameter, s, theme);

            // 6. 顶部与底部航电信息微标栏 (Header & Footer Badges)
            CreateAvionicsBadges(ballDiameter, totalHeight, s, theme);

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetGraphicViewportControl("attitude_viewport", "3D姿态球视口", _sphereDisplayImage != null ? _sphereDisplayImage.gameObject : gameObject, _sphereDisplayImage));
            if (_centerShipRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "center_ship", "中央3D飞船", _centerShipRoot.gameObject));
            if (_flightDirectorRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "flight_director", "飞行指引仪", _flightDirectorRoot.gameObject));
            if (_bezelRingObj != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel_ring", "航电圆环外圈", _bezelRingObj, (t) => { if (_bezelRingRawImage != null) _bezelRingRawImage.color = WidgetStyleManager.WithAlpha(t.AccentSecondary, 0.85f); }));
            if (_topBadgeRoot != null) this.Controls.Register(new WidgetReadoutControl("top_badge", "顶部航向参考系标牌", _topBadgeRoot, _topBadgeText, null, TextStyleRole.Cardinal, _headingToken));
            if (_bottomBadgeRoot != null) this.Controls.Register(new WidgetReadoutControl("bottom_badge", "底部俯仰滚转标牌", _bottomBadgeRoot, _bottomBadgeText, null, TextStyleRole.PrimaryValue, _pitchToken));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void InitializeAttitudeSphere(float ballDiameter, ThemeConfig theme)
        {
            string renderModeStr = GetTemplateChannel("RENDER", null);
            bool forceMesh = "MESH".Equals(renderModeStr, StringComparison.OrdinalIgnoreCase) ||
                             "MINIMAL".Equals(renderModeStr, StringComparison.OrdinalIgnoreCase);

            Shader raymarchShader = AssetLoader.RaymarchShader ?? Shader.Find("ModularFlightPanel/NavballRaymarch");
            Shader minimalShader = AssetLoader.MinimalistAttitudeShader ?? Shader.Find("ModularFlightPanel/MinimalistAttitudeSphere");

            _isUsingRaymarch = !forceMesh && raymarchShader != null;
            Shader targetShader = _isUsingRaymarch ? raymarchShader : (minimalShader ?? raymarchShader ?? AssetLoader.ProceduralShader ?? AssetLoader.ModernShader);

            _sphereMaterial = new Material(targetShader);

            _sphereDisplayImage = CreateChild<RawImage>("Sphere_Viewport", transform,
                new Vector2(ballDiameter, ballDiameter), Vector2.zero);
            _sphereDisplayImage.raycastTarget = false;

            if (_isUsingRaymarch)
            {
                _sphereDisplayImage.material = _sphereMaterial;
                _sphereDisplayImage.texture = Texture2D.whiteTexture;
            }
            else
            {
                int rtRes = 512;
                if (WidgetRenderManager.Instance != null)
                {
                    rtRes = WidgetRenderManager.Instance.CalculateOptimalResolution(
                        new Vector2(ballDiameter, ballDiameter),
                        Config != null ? Config.Scale : 1.0f,
                        Config != null ? Config.RenderScale : 1.0f);
                }

                _renderTexture = new RenderTexture(rtRes, rtRes, 16, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    anisoLevel = 4,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear
                };
                _renderTexture.Create();

                _ballCamera = CreateChild<Camera>("Navball_Vessel_Cam", transform);
                GameObject camObj = _ballCamera.gameObject;
                camObj.transform.localPosition = new Vector3(0f, 0f, -2.5f);
                _ballCamera.clearFlags = CameraClearFlags.SolidColor;
                _ballCamera.backgroundColor = WidgetStyleManager.NeutralTransparent;
                _ballCamera.targetTexture = _renderTexture;
                _ballCamera.orthographic = true;
                _ballCamera.orthographicSize = 1.0f;
                _ballCamera.nearClipPlane = 0.1f;
                _ballCamera.farClipPlane = 10f;
                _ballCamera.cullingMask = 1 << 31;
                _ballCamera.enabled = false;
                _ballCamera.useOcclusionCulling = false;
                _ballCamera.allowHDR = false;
                _ballCamera.allowMSAA = false;
                _ballCamera.depthTextureMode = DepthTextureMode.None;

                _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereObject.name = "Navball_Vessel_Sphere";
                _sphereObject.transform.SetParent(transform, false);
                _sphereObject.transform.localPosition = Vector3.zero;
                _sphereObject.layer = 31;
                UpdateSphereScale();

                Collider col = _sphereObject.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
                mr.material = _sphereMaterial;
                _sphereDisplayImage.texture = _renderTexture;
            }

            var hook = NavBallHookService.Provider;
            _currentPalette = GetPaletteForCategory(hook?.ReferenceFrameCategory ?? "SURFACE", theme);
            _targetPalette = _currentPalette;
            _paletteInitialized = true;
            ApplyPaletteToSphereMaterial(_currentPalette);
        }

        private void CreateBezelRing(float ballDiameter, float s, ThemeConfig theme)
        {
            _bezelRingRawImage = CreateChild<RawImage>("Bezel_Ring", transform,
                new Vector2(ballDiameter + 6f * s, ballDiameter + 6f * s), Vector2.zero);
            _bezelRingObj = _bezelRingRawImage.gameObject;
            _bezelRingRawImage.texture = _sharedBezelTexture;
            _bezelRingRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.85f);
            _bezelRingRawImage.raycastTarget = false;
        }

        private void CreateMarkerOverlayLayer(Transform parent, float s)
        {
            RectTransform mlRt = CreateContainer("Markers_Layer", parent,
                new Vector2(_visualRadius * 2f, _visualRadius * 2f), Vector2.zero);
            GameObject markerLayer = mlRt.gameObject;

            string[] markerKeys = new string[]
            {
                "prograde", "retrograde", "velocity_vector", "anti_velocity_vector",
                "normal", "antinormal",
                "radialin", "radialout", "target", "antitarget", "maneuver"
            };

            float markerSize = 24f * s;
            for (int i = 0; i < markerKeys.Length; i++)
            {
                string key = markerKeys[i];
                Image img = CreateChild<Image>("Marker_" + key, markerLayer.transform,
                    new Vector2(markerSize, markerSize), Vector2.zero);
                GameObject mObj = img.gameObject;

                img.sprite = NavballMarkerFactory.GetMarkerSprite(key);
                img.color = WidgetStyleManager.NeutralOpaque;
                img.raycastTarget = false;
                mObj.SetActive(false);

                _markerImages[key] = img;
            }
        }

        private void CreateReticleWings(float s, ThemeConfig theme)
        {
            // 水平水线基准标翼：挂载于 transform，保持 0 角度正交，绝不随 Roll 翻滚
            _reticleWingsRoot = CreateContainer("Reticle_Wings", transform,
                new Vector2(74f * s, 10f * s), Vector2.zero);
            _reticleWingsRoot.localRotation = Quaternion.identity;

            float wingW = 14f * s;
            float wingH = 2.4f * s;
            float wingOffX = 26f * s;
            _wingL = UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Wing_L", new Vector2(wingW, wingH), new Vector2(-wingOffX, 0f), theme.AccentPrimary);
            _wingR = UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Wing_R", new Vector2(wingW, wingH), new Vector2(wingOffX, 0f), theme.AccentPrimary);
            _pipL = UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Pip_L", new Vector2(2.4f * s, 6f * s), new Vector2(-wingOffX + wingW * 0.5f, -2f * s), theme.AccentPrimary);
            _pipR = UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Pip_R", new Vector2(2.4f * s, 6f * s), new Vector2(wingOffX - wingW * 0.5f, -2f * s), theme.AccentPrimary);
        }

        private void CreateCenter3DSpacecraft(float ballDiameter, float s, ThemeConfig theme)
        {
            // 中央机构根节点
            _centerShipRoot = CreateContainer("Center_3D_Ship_Root", transform,
                new Vector2(46f * s, 46f * s), Vector2.zero);

            // 1. 3D 飞船主体 (Spacecraft Visual Image)
            _shipRawImage = CreateChild<RawImage>("Spacecraft_Visual", _centerShipRoot,
                new Vector2(44f * s, 44f * s), Vector2.zero);
            _shipRawImage.texture = _shared3DSpacecraftTexture;
            _shipRawImage.color = WidgetStyleManager.NeutralOpaque;
            _shipRawImage.raycastTarget = false;

            // 2. 飞行指引仪 Target Flight Director Chevron
            _flightDirectorRawImage = CreateChild<RawImage>("Flight_Director_Chevron", _centerShipRoot,
                new Vector2(24f * s, 24f * s), new Vector2(0f, 18f * s));
            _flightDirectorRoot = _flightDirectorRawImage.rectTransform;
            _flightDirectorRawImage.texture = _sharedFlightDirectorTexture;
            _flightDirectorRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f);
            _flightDirectorRawImage.raycastTarget = false;
            _flightDirectorRoot.gameObject.SetActive(false);
        }

        private void CreateAvionicsBadges(float ballDiameter, float totalHeight, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 顶部航向/参考系标牌 (Top Header Badge)
            float badgeW = ballDiameter * 0.92f;
            float badgeH = 18f * s;
            float topY = ballDiameter * 0.5f + 4f * s;

            _topBadgeRoot = UIFactory.CreatePanel(transform, "Top_Header_Badge", new Vector2(badgeW, badgeH),
                new Vector2(0f, topY), WidgetStyleManager.Surface(SurfaceStyleRole.Slot));
            _topBadgeRoot.GetComponent<Image>().raycastTarget = false;
            Outline topOutline = _topBadgeRoot.AddComponent<Outline>();
            topOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Light);
            topOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);

            _topBadgeText = UIFactory.CreateText(_topBadgeRoot.transform, "Top_Text", I18n.Tr("WIDGET_NAV_HDG_SURFACE_PLACEHOLDER", "航向 ---° | 表面"),
                Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform ttRt = _topBadgeText.GetComponent<RectTransform>();
            ttRt.anchorMin = Vector2.zero;
            ttRt.anchorMax = Vector2.one;
            ttRt.sizeDelta = Vector2.zero;
            ttRt.anchoredPosition = Vector2.zero;

            // 底部姿态/SAS标牌 (Bottom Footer Badge)
            float bottomY = -ballDiameter * 0.5f - 4f * s;
            _bottomBadgeRoot = UIFactory.CreatePanel(transform, "Bottom_Footer_Badge", new Vector2(badgeW, badgeH),
                new Vector2(0f, bottomY), WidgetStyleManager.Surface(SurfaceStyleRole.Slot));
            Outline btmOutline = _bottomBadgeRoot.AddComponent<Outline>();
            btmOutline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Light);
            btmOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);

            _bottomBadgeText = UIFactory.CreateText(_bottomBadgeRoot.transform, "Bottom_Text", I18n.Tr("WIDGET_NAV_ATTITUDE_PLACEHOLDER", "俯仰 +0° 滚转 +0° | SAS"),
                Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform btRt = _bottomBadgeText.GetComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.sizeDelta = Vector2.zero;
            btRt.anchoredPosition = Vector2.zero;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _paletteInitialized = false;
            _lastFrameCategory.Reset(string.Empty);

            Material uiMat = style?.GetUiMaterial(isText: false);
            Material txtMat = style?.GetUiMaterial(isText: true);

            if (_bezelRingRawImage != null)
            {
                _bezelRingRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.85f);
            }

            if (_wingL != null) { var img = _wingL.GetComponent<Image>(); if (img != null) { img.color = theme.AccentPrimary; if (uiMat != null) img.material = uiMat; } }
            if (_wingR != null) { var img = _wingR.GetComponent<Image>(); if (img != null) { img.color = theme.AccentPrimary; if (uiMat != null) img.material = uiMat; } }
            if (_pipL != null) { var img = _pipL.GetComponent<Image>(); if (img != null) { img.color = theme.AccentPrimary; if (uiMat != null) img.material = uiMat; } }
            if (_pipR != null) { var img = _pipR.GetComponent<Image>(); if (img != null) { img.color = theme.AccentPrimary; if (uiMat != null) img.material = uiMat; } }

            if (_topBadgeRoot != null && uiMat != null)
            {
                Image bg = _topBadgeRoot.GetComponent<Image>();
                if (bg != null) bg.material = uiMat;
            }
            if (_bottomBadgeRoot != null && uiMat != null)
            {
                Image bg = _bottomBadgeRoot.GetComponent<Image>();
                if (bg != null) bg.material = uiMat;
            }

            if (_topBadgeText != null)
            {
                ApplyText(_topBadgeText, TextStyleRole.Cardinal, theme);
                if (txtMat != null) _topBadgeText.material = txtMat;
            }

            if (_bottomBadgeText != null)
            {
                _bottomBadgeText.color = _isDirectorLocked
                    ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                    : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                if (txtMat != null) _bottomBadgeText.material = txtMat;
            }

            if (_flightDirectorRawImage != null)
            {
                _flightDirectorRawImage.color = _isDirectorLocked
                    ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                    : WidgetStyleManager.WithAlpha(theme.AccentWarning, 0.90f);
            }

            // 更新姿态球材质着色器与调色板
            if (_sphereMaterial != null)
            {
                ApplyPaletteToSphereMaterial(_currentPalette);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!gameObject.activeInHierarchy) return;
            if (_sphereDisplayImage == null || !_sphereDisplayImage.enabled || !_sphereDisplayImage.gameObject.activeInHierarchy) return;

            SyncAttitudeAndVisuals();
            SyncMarkers();

            // 网格渲染模式动态绘制与亚像素脏标记判定 (4Hz 保活心跳 + 0.025° 亚像素死区)
            if (!_isUsingRaymarch && _ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                bool rotDirty = !_hasEverRendered || (_sphereObject != null && Quaternion.Angle(_sphereObject.transform.localRotation, _lastRenderedRotation.Value) > RotationDirtyThreshold);
                bool heartbeatDirty = (Time.unscaledTime - _lastRenderedTime.Value) >= HeartbeatInterval;

                if (rotDirty || _isPaletteLerping || _isMaterialDirty || heartbeatDirty || _isRenderDirty)
                {
                    _ballCamera.Render();
                    _hasEverRendered = true;
                    if (_sphereObject != null)
                    {
                        _lastRenderedRotation.Update(_sphereObject.transform.localRotation);
                    }
                    _lastRenderedTime.Update(Time.unscaledTime);
                    _isMaterialDirty = false;
                    _isRenderDirty = false;
                }
            }
        }

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            Quaternion rawRot;
            if (hasHook)
            {
                Quaternion camRot = hook.CameraRotation;
                rawRot = Quaternion.Inverse(camRot) * hook.BallRotation;
            }
            else
            {
                IFlightTelemetry telem = FlightTelemetryContext.Current;
                rawRot = (telem != null) ? telem.AttitudeRotation : Quaternion.identity;
            }

            if (_isUsingRaymarch)
            {
                Quaternion invRot = Quaternion.Inverse(rawRot);
                Vector4 invRotVec = new Vector4(invRot.x, invRot.y, invRot.z, invRot.w);
                if (_sphereMaterial != null)
                {
                    _sphereMaterial.SetVector(_PropSphereInvRotation, invRotVec);
                    _sphereMaterial.SetFloat(_PropRenderMode, 1.0f);
                }
            }
            else if (_sphereObject != null)
            {
                _sphereObject.transform.localRotation = rawRot;
            }

            // 多参考系调色板过渡
            string category = hook?.ReferenceFrameCategory ?? "SURFACE";
            if (_lastFrameCategory.Update(category) || !_paletteInitialized)
            {
                _targetPalette = GetPaletteForCategory(category, ThemeManager.Instance.CurrentTheme);
                if (!_paletteInitialized)
                {
                    _currentPalette = _targetPalette;
                    _paletteInitialized = true;
                    ApplyPaletteToSphereMaterial(_currentPalette);
                    _isMaterialDirty = true;
                }
                _isPaletteLerping = true;
            }

            if (_sphereMaterial != null && _isPaletteLerping)
            {
                float dt = Time.deltaTime;
                float lerpFactor = (!Application.isPlaying || dt <= 0.0001f) ? 1.0f : Mathf.Clamp01(dt * 8.0f);
                _currentPalette = WidgetStyleManager.LerpFramePalette(_currentPalette, _targetPalette, lerpFactor);
                ApplyPaletteToSphereMaterial(_currentPalette);
                _isMaterialDirty = true;

                if (IsPaletteEqual(ref _currentPalette, ref _targetPalette))
                {
                    _currentPalette = _targetPalette;
                    _isPaletteLerping = false;
                }
            }
        }

        private void ApplyPaletteToSphereMaterial(NavballFramePalette p)
        {
            if (_sphereMaterial == null) return;
            if (_sphereMaterial.HasProperty(_PropSkyColor))
            {
                _sphereMaterial.SetColor(_PropSkyColor, p.SkyHorizon);
                _sphereMaterial.SetColor(_PropGroundColor, p.GroundHorizon);
                _sphereMaterial.SetColor(_PropEquatorColor, p.Equator);
                _sphereMaterial.SetColor(_PropPitchLadderColor, p.PitchLadder);
                _sphereMaterial.SetColor(_PropMeridianColor, p.HeadingLine);
                _sphereMaterial.SetColor(_PropRimColor, p.Rim);
            }
            if (_sphereMaterial.HasProperty(_PropSkyZenithColor))
            {
                _sphereMaterial.SetColor(_PropSkyZenithColor, p.SkyZenith);
                _sphereMaterial.SetColor(_PropSkyHorizonColor, p.SkyHorizon);
                _sphereMaterial.SetColor(_PropGroundHorizonColor, p.GroundHorizon);
                _sphereMaterial.SetColor(_PropGroundNadirColor, p.GroundNadir);
                _sphereMaterial.SetColor(_PropEquatorColor, p.Equator);
                _sphereMaterial.SetColor(_PropPitchLadderColor, p.PitchLadder);
                _sphereMaterial.SetColor(_PropHeadingLineColor, p.HeadingLine);
                _sphereMaterial.SetColor(_PropRimColor, p.Rim);
            }
        }

        private NavballFramePalette GetPaletteForCategory(string category, ThemeConfig theme)
        {
            switch (category?.ToUpperInvariant())
            {
                case "INERTIAL":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.AccentSecondary, theme);
                case "BARYCENTRIC":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.AccentMagenta, theme);
                case "TARGET":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.DangerColor, theme);
                case "ORBIT":
                case "ORBITAL":
                case "BODY_DIRECTION":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.WarningColor, theme);
                case "BODY_SURFACE":
                case "BODY_FIXED":
                {
                    // Principia 地固参考系 (Body-Centred Body-Fixed / ECEF):
                    // 严格与 Principia 官方 navball_surface 保持一致：北半球 (+lat / +Y) 对应大地棕色，南半球 (-lat / -Y) 对应海洋与天蓝色
                    Color bGndH = WidgetStyleManager.WithAlpha(theme.GroundColor, 0.55f);
                    Color bGndN = WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(theme.GroundColor, 0.55f), 0.40f);
                    Color bSkyH = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.55f);
                    Color bSkyZ = WidgetStyleManager.WithAlpha(theme.SkyColor, 0.40f);
                    return new NavballFramePalette
                    {
                        SkyZenith = bGndN,
                        SkyHorizon = bGndH,
                        GroundHorizon = bSkyH,
                        GroundNadir = bSkyZ,
                        Equator = WidgetStyleManager.WithAlpha(theme.HorizonLineColor, 0.85f),
                        PitchLadder = WidgetStyleManager.WithAlpha(theme.GridColor, 0.65f),
                        HeadingLine = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.45f),
                        Rim = WidgetStyleManager.WithAlpha(theme.RimGlowColor, 0.70f)
                    };
                }
                case "SURFACE":
                default:
                    Color skyZ = WidgetStyleManager.WithAlpha(theme.SkyColor, 0.40f);
                    Color skyH = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.55f);
                    Color gndH = WidgetStyleManager.WithAlpha(theme.GroundColor, 0.55f);
                    Color gndN = WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(gndH, 0.55f), 0.40f);
                    Color eq = theme.HorizonLineColor;
                    Color pitch = WidgetStyleManager.WithAlpha(theme.GridColor, 0.65f);
                    Color hdg = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.45f);
                    Color rim = WidgetStyleManager.WithAlpha(theme.RimGlowColor, 0.70f);
                    return new NavballFramePalette
                    {
                        SkyZenith = skyZ,
                        SkyHorizon = skyH,
                        GroundHorizon = gndH,
                        GroundNadir = gndN,
                        Equator = eq,
                        PitchLadder = pitch,
                        HeadingLine = hdg,
                        Rim = rim
                    };
            }
        }

        private void SyncMarkers()
        {
            var hook = NavBallHookService.Provider;
            foreach (var kvp in _markerImages)
            {
                string key = kvp.Key;
                Image img = kvp.Value;
                if (img == null) continue;

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

                // 视界边缘平滑渐隐 [-0.05, -0.22]，杜绝硬边界闪烁
                if (hasDir && isVisible && dir.z > -0.22f)
                {
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                    Vector2 targetPos = new Vector2(dir.x, dir.y) * _visualRadius;
                    if (Mathf.Abs(img.rectTransform.anchoredPosition.x - targetPos.x) > 0.05f || Mathf.Abs(img.rectTransform.anchoredPosition.y - targetPos.y) > 0.05f)
                    {
                        img.rectTransform.anchoredPosition = targetPos;
                    }

                    float alpha = Mathf.Clamp01((dir.z + 0.22f) / 0.32f);
                    if (Mathf.Abs(img.color.a - alpha) > 0.015f)
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = alpha;
                        img.color = c;
                    }
                }
                else
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                }
            }

            // 顺向/逆向与地表航迹标防御性防粘去重 (Anti-overlap De-cluttering)
            Image progImg;
            Image velImg;
            if (_markerImages.TryGetValue("prograde", out progImg) &&
                _markerImages.TryGetValue("velocity_vector", out velImg) &&
                progImg != null && velImg != null &&
                progImg.gameObject.activeSelf && velImg.gameObject.activeSelf)
            {
                float dist = Vector2.Distance(progImg.rectTransform.anchoredPosition, velImg.rectTransform.anchoredPosition);
                if (dist < 18f * CurrentDpiScale)
                {
                    velImg.gameObject.SetActive(false);
                }
            }

            Image retroImg;
            Image antiVelImg;
            if (_markerImages.TryGetValue("retrograde", out retroImg) &&
                _markerImages.TryGetValue("anti_velocity_vector", out antiVelImg) &&
                retroImg != null && antiVelImg != null &&
                retroImg.gameObject.activeSelf && antiVelImg.gameObject.activeSelf)
            {
                float dist = Vector2.Distance(retroImg.rectTransform.anchoredPosition, antiVelImg.rectTransform.anchoredPosition);
                if (dist < 18f * CurrentDpiScale)
                {
                    antiVelImg.gameObject.SetActive(false);
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                // 右键切换观察视角 (Chase 3D <-> Top-Down 3D)
                _isChasePerspective = !_isChasePerspective;
                _lastPitch.Reset(double.NaN); // 触发刷新
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                // 检查是否点击在中央飞船区域
                if (_centerShipRoot != null && RectTransformUtility.RectangleContainsScreenPoint(_centerShipRoot, eventData.position, eventData.pressEventCamera))
                {
                    // 点击飞船轮播视觉样式: Procedural3D -> RealVessel3D -> TopDownSilhouette
                    _shipVisualMode = (CenterShipVisualMode)(((int)_shipVisualMode + 1) % 3);
                }
                else
                {
                    // 左键切换 SAS 稳定状态
                    FlightTelemetryContext.Current?.ToggleSAS();
                }
            }
        }

        #region Procedural Textures & Asset Generation (Zero External IO)

        private static void EnsureSharedTextures()
        {
            if (_shared3DSpacecraftTexture == null)
            {
                _shared3DSpacecraftTexture = SpacecraftAttitudeVisualGenerator.GetOrCreateSpacecraftTexture();
            }
            if (_sharedFlightDirectorTexture == null)
            {
                _sharedFlightDirectorTexture = SpacecraftAttitudeVisualGenerator.GetOrCreateFlightDirectorTexture();
            }
            if (_sharedBezelTexture == null)
            {
                _sharedBezelTexture = SpacecraftAttitudeVisualGenerator.GetOrCreateBezelTexture();
            }
        }

        #endregion

        protected override void OnRenderTextureRecreated(RenderTexture newRt)
        {
            base.OnRenderTextureRecreated(newRt);
            if (_ballCamera != null) _ballCamera.targetTexture = newRt;
            if (_sphereDisplayImage != null && !_isUsingRaymarch) _sphereDisplayImage.texture = newRt;
        }

        protected override void HandleRenderSettingChanged()
        {
            if (_isUsingRaymarch) return;
            if (WidgetRenderManager.Instance == null) return;
            float ballDiameter = _visualRadius * 2.0f;
            int optimalRes = WidgetRenderManager.Instance.CalculateOptimalResolution(
                new Vector2(ballDiameter, ballDiameter),
                Config != null ? Config.Scale : 1.0f,
                Config != null ? Config.RenderScale : 1.0f);
            if (_renderTexture == null || _renderTexture.width != optimalRes)
            {
                HandleResolutionChanged(optimalRes);
            }
        }

        private static string GetSASModeKey(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return "stabilityassist";
                case FlightSASMode.Prograde: return "prograde";
                case FlightSASMode.Retrograde: return "retrograde";
                case FlightSASMode.Normal: return "normal";
                case FlightSASMode.Antinormal: return "antinormal";
                case FlightSASMode.RadialIn: return "radialin";
                case FlightSASMode.RadialOut: return "radialout";
                case FlightSASMode.Target: return "target";
                case FlightSASMode.AntiTarget: return "antitarget";
                case FlightSASMode.Maneuver: return "maneuver";
                default: return "stabilityassist";
            }
        }

        private static string GetSASModeDisplayText(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return "STAB";
                case FlightSASMode.Prograde: return "PROGRADE";
                case FlightSASMode.Retrograde: return "RETROGRADE";
                case FlightSASMode.Normal: return "NORMAL";
                case FlightSASMode.Antinormal: return "ANTINORMAL";
                case FlightSASMode.RadialIn: return "RADIAL IN";
                case FlightSASMode.RadialOut: return "RADIAL OUT";
                case FlightSASMode.Target: return "TARGET";
                case FlightSASMode.AntiTarget: return "ANTI-TARGET";
                case FlightSASMode.Maneuver: return "MANEUVER";
                default: return "SAS";
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            if (_sphereObject != null)
            {
                Destroy(_sphereObject);
                _sphereObject = null;
            }

            if (_ballCamera != null)
            {
                Destroy(_ballCamera.gameObject);
                _ballCamera = null;
            }

            base.OnDestroy();
        }
    }

    /// <summary>
    /// 向后兼容类型别名 (与文件名 VesselAttitudeSphereWidget.cs 对齐)
    /// </summary>
    [Obsolete("Use VesselAttitudeSphereWidget instead.")]
    public class VesselNavballWidget : VesselAttitudeSphereWidget
    {
    }
}
