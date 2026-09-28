using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// 3D 飞船球形姿态仪 / 导航球仪表 (Vessel Attitude Sphere / 3D Navball Widget)
    /// 核心特性：
    /// 1. 球形姿态球体表达 (Spherical Navball Representation)：
    ///    - 正交 3D 单位球体配合多参考系天顶/地平渐变、赤道地平线与经纬度俯仰阶梯标尺。
    ///    - 完整对接 NavBallHookService 原生与 Principia 多参考系姿态球纹理或高保真程序化着色器。
    ///    - 2D/3D 导航标记层（Prograde, Retrograde, Normal, Maneuver 等）球面正交平滑投影与边缘淡出。
    /// 2. 中央 3D 飞船实体 (3D Spacecraft at Center)：
    ///    - 取代原版扁平准星，在球体正中心呈现立体航天器。
    ///    - 优先挂载 Vessel3DService.Provider?.Texture3D（来自 Vessel3DBaker 的实时 3D 动态网格捕获）。
    ///    - 未烘焙时平滑回退至高保真 Half-Lambert 光照着色 3D 航天器模型（座舱反光、三角翼、编队灯与引擎辉光）。
    ///    - 具备俯仰透视收缩 cos(Pitch * 0.5) 与滚转/操纵量动态响应。
    /// 3. 飞行指引仪 (Flight Director Target Chevron)：
    ///    - 机头前向反 V 形高亮指引光标，追踪当前 SAS 模式机动目标向量，并在 <= 1.5° 时吸附锁定。
    /// 4. 航电读数与参考系标牌：
    ///    - 顶部弧形标牌：参考系（SURF / ORBIT / TGT）与航向角（HDG {HDG:F0}°）。
    ///    - 底部状态标牌：俯仰/滚转角（P {PITCH:+0;-0;0}° R {ROLL:+0;-0;0}°）与 SAS 锁定状态。
    /// 5. 交互控制：
    ///    - 左键点击底部标牌切换 SAS / STAB 稳定。
    ///    - 右键点击中央飞船切换观察视角（追尾 3D / 俯视 3D）。
    /// 6. 严格落实 MFP-SPEC-001..007 铁律（0 颜色字面量、0 场景查询、分频阶梯 Critical 60Hz、零 GC 缓存守卫）。
    /// </summary>
    [AlwaysFullPower]
    [FlightWidget("vessel_navball", "vessel_attitude_sphere", "attitude_sphere", Category = WidgetCategory.Navigation, DisplayName = "3D 飞船球形姿态仪", Description = "全新球形姿态仪：以真实 3D 飞船为中心，外层环绕 3D 姿态球体、人工地平标尺、SAS 目标飞行指引仪与全量导航矢量。", DefaultWidgetId = "nav.vessel_navball", DefaultX = 0f, DefaultY = 0f, IsSingleton = true, HighFrequency = true, AlwaysFullPower = true, ExactIds = new[] { "nav.vessel_navball", "nav.vessel_attitude_sphere", "core.vessel_navball", "core.vessel_attitude_sphere", "nav.attitude_sphere_3d" })]
    public class VesselAttitudeSphereWidget : BaseNavballSphereWidget, IPointerClickHandler
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;
        private RawImage _sphereDisplayImage;

        // 中央 3D 飞船机构
        private RectTransform _centerShipRoot;
        private RawImage _shipRawImage;
        private RectTransform _flightDirectorRoot;
        private RawImage _flightDirectorRawImage;
        private RectTransform _reticleWingsRoot;

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

        // 静态共享程序化纹理 (避免重复分配)
        private static Texture2D _shared3DSpacecraftTexture;
        private static Texture2D _sharedFlightDirectorTexture;
        private static Texture2D _sharedBezelTexture;

        // 姿态缓存与脏标记
        private double _lastPitch = double.NaN;
        private double _lastRoll = double.NaN;
        private double _lastHeading = double.NaN;
        private string _lastTopText = string.Empty;
        private string _lastBottomText = string.Empty;
        private bool _isDirectorLocked = false;
        private bool _isChasePerspective = true;

        // 多参考系调色板过渡
        private string _lastFrameCategory = "";
        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;
        private bool _isPaletteLerping = false;

        // 动态绘制与亚像素脏标记判定 (Zero Visual Quality Loss)
        private const float RotationDirtyThreshold = 0.025f;
        private const float HeartbeatInterval = 0.25f;
        private Quaternion _lastRenderedRotation = Quaternion.identity;
        private float _lastRenderedTime = -10f;
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
            EnsureSharedTextures();

            // 1. 初始化 3D 姿态球离屏渲染管线 (RenderTexture + Offscreen Camera + Sphere Mesh)
            InitializeOffscreenSphere(ballDiameter, theme);

            // 2. 外部航电金属刻度圆环 (Bezel Ring)
            CreateBezelRing(ballDiameter, s, theme);

            // 3. 球面 2D/3D 导航矢量标记层 (Marker Layer)
            CreateMarkerOverlayLayer(transform, s);

            // 4. 中央 3D 飞船机构 (Center 3D Spacecraft & Flight Director)
            CreateCenter3DSpacecraft(ballDiameter, s, theme);

            // 5. 顶部与底部航电信息微标栏 (Header & Footer Badges)
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


        private void InitializeOffscreenSphere(float ballDiameter, ThemeConfig theme)
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

            // 离屏正交摄像机 (层级 31，正交视口 1.0f)
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

            // 3D 单位球体 (半径 0.94f，预留边缘抗锯齿与发光空间)
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

            // 极简纯净 3D 姿态球 (100% 程序化，0 贴图素材依赖，不使用传统导航球素材)
            var hook = NavBallHookService.Provider;
            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = AssetLoader.MinimalistAttitudeShader ?? AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);
            _sphereMaterial.mainTexture = null;

            // 初始化极简调色板并挂载至材质
            _currentPalette = GetPaletteForCategory(hook?.ReferenceFrameCategory ?? "SURFACE", theme);
            _targetPalette = _currentPalette;
            _paletteInitialized = true;
            ApplyPaletteToSphereMaterial(_currentPalette);

            mr.material = _sphereMaterial;

            // RawImage 球体主贴图视口
            _sphereDisplayImage = CreateChild<RawImage>("Sphere_Viewport", transform,
                new Vector2(ballDiameter, ballDiameter), Vector2.zero);
            _sphereDisplayImage.texture = _renderTexture;
            _sphereDisplayImage.raycastTarget = false;
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

        private void CreateCenter3DSpacecraft(float ballDiameter, float s, ThemeConfig theme)
        {
            // 中央机构根节点
            _centerShipRoot = CreateContainer("Center_3D_Ship_Root", transform,
                new Vector2(46f * s, 46f * s), Vector2.zero);

            // 1. 水平基准定位指示标翼 (Aerospace Horizon Reticle Index Wings)
            _reticleWingsRoot = CreateContainer("Reticle_Wings", _centerShipRoot,
                new Vector2(74f * s, 10f * s), Vector2.zero);

            float wingW = 14f * s;
            float wingH = 2.4f * s;
            float wingOffX = 26f * s;
            UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Wing_L", new Vector2(wingW, wingH), new Vector2(-wingOffX, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Wing_R", new Vector2(wingW, wingH), new Vector2(wingOffX, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Pip_L", new Vector2(2.4f * s, 6f * s), new Vector2(-wingOffX + wingW * 0.5f, -2f * s), theme.AccentPrimary);
            UIFactory.CreatePanel(_reticleWingsRoot, "Reticle_Pip_R", new Vector2(2.4f * s, 6f * s), new Vector2(wingOffX - wingW * 0.5f, -2f * s), theme.AccentPrimary);

            // 2. 3D 飞船主体 (Spacecraft Visual Image)
            _shipRawImage = CreateChild<RawImage>("Spacecraft_Visual", _centerShipRoot,
                new Vector2(44f * s, 44f * s), Vector2.zero);
            _shipRawImage.texture = _shared3DSpacecraftTexture;
            _shipRawImage.color = WidgetStyleManager.NeutralOpaque;
            _shipRawImage.raycastTarget = false;

            // 3. 飞行指引仪 Target Flight Director Chevron
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
            _lastFrameCategory = null;

            if (_bezelRingRawImage != null)
            {
                _bezelRingRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.85f);
            }

            if (_topBadgeText != null)
            {
                ApplyText(_topBadgeText, TextStyleRole.Cardinal, theme);
            }

            if (_bottomBadgeText != null)
            {
                _bottomBadgeText.color = _isDirectorLocked
                    ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                    : style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }

            if (_flightDirectorRawImage != null)
            {
                _flightDirectorRawImage.color = _isDirectorLocked
                    ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                    : WidgetStyleManager.WithAlpha(theme.AccentWarning, 0.90f);
            }

            // 更新 3D 球体材质着色器与极简调色板
            if (_sphereMaterial != null)
            {
                Shader targetShader = AssetLoader.MinimalistAttitudeShader ?? AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
                if (targetShader != null && _sphereMaterial.shader != targetShader)
                {
                    _sphereMaterial.shader = targetShader;
                }
                ApplyPaletteToSphereMaterial(_currentPalette);
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 优先挂载 Vessel3DService 实时烘焙的 3D 飞船贴图
            if (_shipRawImage != null)
            {
                Texture tex3D = Vessel3DService.Provider?.Texture3D;
                if (tex3D != null && _shipRawImage.texture != tex3D)
                {
                    _shipRawImage.texture = tex3D;
                }
                else if (tex3D == null && _shipRawImage.texture != _shared3DSpacecraftTexture)
                {
                    _shipRawImage.texture = _shared3DSpacecraftTexture;
                }
            }

            // 2. 中央 3D 飞船姿态与俯仰收缩 (3D Gimbal Dynamics: 全帧平滑响应，杜绝步进卡顿)
            if (_centerShipRoot != null)
            {
                if (_isChasePerspective)
                {
                    // 追尾视角：飞船主体微幅俯仰/滚转，产生逼真空间悬浮纵深
                    float pitchRad = (float)telemetry.Pitch * Mathf.Deg2Rad;
                    float foreshortenY = Mathf.Clamp(Mathf.Cos(pitchRad * 0.5f), 0.72f, 1.0f);
                    _centerShipRoot.localScale = new Vector3(1.0f, foreshortenY, 1.0f);
                    _centerShipRoot.localRotation = Quaternion.Euler(0f, 0f, (float)-telemetry.Roll * 0.25f);
                }
                else
                {
                    // 俯视机动视角：1:1 纯滚转
                    _centerShipRoot.localScale = Vector3.one;
                    _centerShipRoot.localRotation = Quaternion.Euler(0f, 0f, (float)-telemetry.Roll);
                }
            }

            // 3. 飞行指引仪 Target Flight Director Chevron 解算
            UpdateFlightDirector(telemetry, theme);

            // 4. 顶部航向与参考系标牌更新
            string hdgStr = TelemetryTokenEngine.Evaluate(_headingToken, telemetry);
            if (hdgStr.EndsWith("°")) hdgStr = hdgStr.Substring(0, hdgStr.Length - 1).Trim();
            var hook = NavBallHookService.Provider;
            string frameCat = hook?.ReferenceFrameCategory ?? "SURFACE";
            string topFormatted = $"HDG {hdgStr}° | {frameCat}";
            if (topFormatted != _lastTopText)
            {
                _lastTopText = topFormatted;
                SetTextIfChanged(_topBadgeText, topFormatted);
            }

            // 5. 底部俯仰/滚转与 SAS 状态更新
            string pStr = TelemetryTokenEngine.Evaluate(_pitchToken, telemetry);
            if (pStr.EndsWith("°")) pStr = pStr.Substring(0, pStr.Length - 1).Trim();
            string rStr = TelemetryTokenEngine.Evaluate(_rollToken, telemetry);
            if (rStr.EndsWith("°")) rStr = rStr.Substring(0, rStr.Length - 1).Trim();
            string sasMode = TelemetryTokenEngine.Evaluate(_sasToken, telemetry);
            string lockTag = _isDirectorLocked ? " [LOCK]" : "";
            string btmFormatted = $"P {pStr}° R {rStr}° | {sasMode}{lockTag}";
            if (btmFormatted != _lastBottomText)
            {
                _lastBottomText = btmFormatted;
                SetTextIfChanged(_bottomBadgeText, btmFormatted);
            }

            _lastPitch = telemetry.Pitch;
            _lastRoll = telemetry.Roll;
            _lastHeading = telemetry.Heading;
        }

        private void UpdateFlightDirector(IFlightTelemetry telemetry, ThemeConfig theme)
        {
            if (_flightDirectorRoot == null) return;
            bool sasActive = FlightTelemetryContext.Current?.IsSASEnabled ?? false;
            if (!sasActive)
            {
                if (_flightDirectorRoot.gameObject.activeSelf) _flightDirectorRoot.gameObject.SetActive(false);
                _isDirectorLocked = false;
                return;
            }

            var hook = NavBallHookService.Provider;
            string sasModeKey = (FlightTelemetryContext.Current?.CurrentSASMode ?? FlightSASMode.StabilityAssist).ToString().ToLowerInvariant();
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
                if (_flightDirectorRoot.gameObject.activeSelf) _flightDirectorRoot.gameObject.SetActive(false);
                _isDirectorLocked = false;
                return;
            }

            if (!_flightDirectorRoot.gameObject.activeSelf) _flightDirectorRoot.gameObject.SetActive(true);

            Vector3 localDir = targetDir;
            float deflX = Mathf.Clamp(localDir.x, -1f, 1f);
            float deflY = Mathf.Clamp(localDir.y, -1f, 1f);
            float angError = Mathf.Atan2(Mathf.Sqrt(localDir.x * localDir.x + localDir.y * localDir.y), Mathf.Max(0.001f, localDir.z)) * Mathf.Rad2Deg;

            float s = CurrentDpiScale;
            float maxDeflection = 22f * s;
            bool targetLocked = _isDirectorLocked ? (angError <= 1.8f) : (angError <= 1.3f);
            _isDirectorLocked = targetLocked;

            Vector2 targetPos = targetLocked
                ? new Vector2(0f, 14f * s)
                : new Vector2(deflX * maxDeflection, 14f * s + deflY * maxDeflection);

            float dt = Time.deltaTime;
            float lerpT = (!Application.isPlaying || dt <= 0.0001f) ? 1.0f : Mathf.Clamp01(dt * 14.0f);
            _flightDirectorRoot.anchoredPosition = Vector2.Lerp(_flightDirectorRoot.anchoredPosition, targetPos, lerpT);

            if (_flightDirectorRawImage != null)
            {
                Color targetCol = targetLocked
                    ? WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.95f)
                    : WidgetStyleManager.WithAlpha(theme.AccentWarning, 0.90f);
                if (_flightDirectorRawImage.color != targetCol)
                    _flightDirectorRawImage.color = targetCol;
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!gameObject.activeInHierarchy) return;
            if (_sphereDisplayImage == null || !_sphereDisplayImage.enabled || !_sphereDisplayImage.gameObject.activeInHierarchy) return;

            SyncAttitudeAndVisuals();
            SyncMarkers();

            // 动态绘制与亚像素脏标记判定 (4Hz 保活心跳 + 0.025° 亚像素死区)
            if (_ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                bool rotDirty = !_hasEverRendered || (_sphereObject != null && Quaternion.Angle(_sphereObject.transform.localRotation, _lastRenderedRotation) > RotationDirtyThreshold);
                bool heartbeatDirty = (Time.unscaledTime - _lastRenderedTime) >= HeartbeatInterval;

                if (rotDirty || _isPaletteLerping || _isMaterialDirty || heartbeatDirty || _isRenderDirty)
                {
                    _ballCamera.Render();
                    _hasEverRendered = true;
                    if (_sphereObject != null)
                    {
                        _lastRenderedRotation = _sphereObject.transform.localRotation;
                    }
                    _lastRenderedTime = Time.unscaledTime;
                    _isMaterialDirty = false;
                    _isRenderDirty = false;
                }
            }
        }

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            if (_sphereObject != null)
            {
                bool isProcedural = ThemeManager.Instance.GlobalRenderMode != NavballRenderMode.StockTexture;
                if (hasHook)
                {
                    Quaternion camRot = hook.CameraRotation;
                    Quaternion rawRot = Quaternion.Inverse(camRot) * hook.BallRotation;
                    _sphereObject.transform.localRotation = rawRot;
                }
                else
                {
                    IFlightTelemetry telem = FlightTelemetryContext.Current;
                    Quaternion rawRot = (telem != null) ? telem.AttitudeRotation : Quaternion.identity;
                    _sphereObject.transform.localRotation = rawRot;
                }
            }

            // 多参考系调色板过渡
            string category = hook?.ReferenceFrameCategory ?? "SURFACE";
            if (category != _lastFrameCategory || !_paletteInitialized)
            {
                _targetPalette = GetPaletteForCategory(category, ThemeManager.Instance.CurrentTheme);
                if (!_paletteInitialized)
                {
                    _currentPalette = _targetPalette;
                    _paletteInitialized = true;
                    ApplyPaletteToSphereMaterial(_currentPalette);
                    _isMaterialDirty = true;
                }
                _lastFrameCategory = category;
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
                    img.rectTransform.anchoredPosition = targetPos;

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
                // 右键切换观察视角
                _isChasePerspective = !_isChasePerspective;
                _lastPitch = double.NaN; // 触发刷新
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                // 左键切换 SAS 稳定状态
                FlightTelemetryContext.Current?.ToggleSAS();
            }
        }

        #region Procedural Textures & Asset Generation (Zero External IO)

        private static void EnsureSharedTextures()
        {
            if (_shared3DSpacecraftTexture == null)
            {
                _shared3DSpacecraftTexture = CreateProcedural3DSpacecraftTexture();
            }
            if (_sharedFlightDirectorTexture == null)
            {
                _sharedFlightDirectorTexture = CreateFlightDirectorChevronTexture();
            }
            if (_sharedBezelTexture == null)
            {
                _sharedBezelTexture = CreateBezelRingTexture();
            }
        }

        /// <summary>
        /// 程序化生成 256x256 高精度 3D 航天器纹理 (Half-Lambert 漫反射 + 边缘高光 + 驾驶舱 + 编队灯)
        /// </summary>
        private static Texture2D CreateProcedural3DSpacecraftTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 1.8f * invHalf;

            // 虚拟光源方向 (从左上方照向机身)
            Vector3 lightDir = new Vector3(-0.45f, 0.45f, 0.77f).normalized;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);

                    float alpha = 0f;
                    Vector3 normal = Vector3.forward;
                    float albedoR = 0.85f, albedoG = 0.88f, albedoB = 0.95f;

                    // 1. 中央机身轮廓 (Fuselage: 机头 ny ~ 0.82 至机尾 ny ~ -0.55)
                    float bodyHalfW = 0f;
                    if (ny >= 0.20f && ny <= 0.82f)
                    {
                        float t = (0.82f - ny) / 0.62f; // 0 .. 1
                        bodyHalfW = Mathf.Lerp(0.02f, 0.18f, Mathf.Sqrt(t));
                    }
                    else if (ny >= -0.55f && ny < 0.20f)
                    {
                        bodyHalfW = Mathf.Lerp(0.18f, 0.22f, (0.20f - ny) / 0.75f);
                    }

                    if (bodyHalfW > 0.001f && absX <= bodyHalfW + feather)
                    {
                        float dist = absX - bodyHalfW;
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > alpha)
                        {
                            alpha = a;
                            // 弧形机身法线估计
                            float u = Mathf.Clamp(nx / Mathf.Max(0.01f, bodyHalfW), -1f, 1f);
                            normal = new Vector3(u * 0.85f, 0.15f, Mathf.Sqrt(Mathf.Max(0f, 1f - u * u * 0.72f))).normalized;
                            albedoR = 0.78f; albedoG = 0.84f; albedoB = 0.92f;
                        }
                    }

                    // 2. 三角主翼 (Swept Delta Wings)
                    if (ny >= -0.48f && ny <= 0.35f)
                    {
                        float wingT = (0.35f - ny) / 0.83f; // 0 .. 1
                        float wingSpan = Mathf.Lerp(0.12f, 0.78f, Mathf.Pow(wingT, 1.25f));
                        float wingInner = Mathf.Max(0f, bodyHalfW - 0.02f);
                        if (absX >= wingInner && absX <= wingSpan + feather)
                        {
                            float dist = absX - wingSpan;
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > alpha)
                            {
                                alpha = a;
                                normal = new Vector3(Mathf.Sign(nx) * 0.25f, -0.1f, 0.96f).normalized;
                                albedoR = 0.68f; albedoG = 0.76f; albedoB = 0.86f;
                            }
                        }
                    }

                    // 3. 翼尖航行灯 (Wingtip Formation Lights)
                    if (ny >= -0.45f && ny <= -0.38f && absX >= 0.72f && absX <= 0.79f)
                    {
                        alpha = 1.0f;
                        if (nx < 0f) { albedoR = 1.0f; albedoG = 0.2f; albedoB = 0.2f; }
                        else { albedoR = 0.2f; albedoG = 1.0f; albedoB = 0.4f; }
                    }

                    // 4. 水滴形座舱盖 (Cockpit Canopy)
                    if (ny >= 0.24f && ny <= 0.58f && absX <= 0.085f)
                    {
                        float cT = (0.58f - ny) / 0.34f;
                        float cW = Mathf.Sin(cT * Mathf.PI) * 0.08f;
                        if (absX <= cW + feather)
                        {
                            float dist = absX - cW;
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > 0.01f)
                            {
                                alpha = Mathf.Max(alpha, a);
                                normal = new Vector3(nx * 8f, 0.3f, 0.9f).normalized;
                                albedoR = 0.10f; albedoG = 0.45f; albedoB = 0.65f;
                            }
                        }
                    }

                    // 5. 双发尾喷管 (Twin Engine Exhaust Nozzles)
                    if (ny >= -0.66f && ny <= -0.52f && (Mathf.Abs(absX - 0.11f) <= 0.045f))
                    {
                        alpha = 1.0f;
                        albedoR = 0.20f; albedoG = 0.75f; albedoB = 1.0f; // 离子推进微光
                    }

                    if (alpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        // 3D 光照解算 (Half-Lambert + 边缘高光)
                        float diff = Mathf.Max(0f, Vector3.Dot(normal, lightDir)) * 0.5f + 0.5f;
                        Vector3 viewDir = Vector3.forward;
                        Vector3 halfVec = (lightDir + viewDir).normalized;
                        float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, halfVec)), 16f) * 0.35f;

                        Color shaded = WidgetStyleManager.NeutralOpaque;
                        shaded.r = Mathf.Clamp01(albedoR * diff + spec);
                        shaded.g = Mathf.Clamp01(albedoG * diff + spec);
                        shaded.b = Mathf.Clamp01(albedoB * diff + spec);
                        shaded.a = Mathf.Clamp01(alpha);
                        cols[y * size + x] = shaded;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D CreateFlightDirectorChevronTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf;
                    float absX = Mathf.Abs(nx);

                    float targetY = 0.55f - absX * 0.95f;
                    float distY = Mathf.Abs(ny - targetY);
                    float distX = Mathf.Max(0f, absX - 0.72f);
                    float dist = Mathf.Max(distY - 0.12f, distX);

                    float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                    if (a <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = a;
                        cols[y * size + x] = c;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D CreateBezelRingTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    float alpha = 0f;
                    // 极简纤细刻度环 (r: 0.94 .. 0.985)
                    if (r >= 0.93f && r <= 0.995f)
                    {
                        float dist = Mathf.Max(0.95f - r, r - 0.98f);
                        alpha = (dist <= 0f) ? 0.90f : Mathf.Clamp01(1.0f - dist / feather) * 0.90f;
                    }

                    // 12 点钟航向主标三角形 (Top Notch Triangle)
                    if (ny >= 0.86f && Mathf.Abs(nx) <= (0.98f - ny) * 0.70f)
                    {
                        alpha = 1.0f;
                    }

                    // 滚转角指示标尺 (Bank Angle Ticks at ±30°, ±60°, ±90°)
                    float angDeg = Mathf.Atan2(nx, ny) * Mathf.Rad2Deg; // -180 .. 180
                    float absAng = Mathf.Abs(angDeg);
                    bool isTick = (Mathf.Abs(absAng - 30f) < 0.9f || Mathf.Abs(absAng - 60f) < 0.9f || Mathf.Abs(absAng - 90f) < 0.9f);
                    if (isTick && r >= 0.88f && r <= 0.96f)
                    {
                        alpha = 0.88f;
                    }

                    if (alpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = alpha;
                        cols[y * size + x] = c;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        #endregion

        protected override void OnRenderTextureRecreated(RenderTexture newRt)
        {
            base.OnRenderTextureRecreated(newRt);
            if (_ballCamera != null) _ballCamera.targetTexture = newRt;
            if (_sphereDisplayImage != null) _sphereDisplayImage.texture = newRt;
        }

        protected override void HandleRenderSettingChanged()
        {
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
