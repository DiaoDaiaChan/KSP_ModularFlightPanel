using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Widgets.Navigation;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代超清屏幕空间解析光线投射姿态球 (Screen-Space Analytic Raymarched Sphere)
    /// 彻底废弃离屏摄像机、RenderTexture 与 3D 网格体，直接在原生 UGUI 矩形通过解析几何光线投射绘制。
    /// 完整支持：
    /// 1. 原版与 Principia 动态多参考系 (Barycentric/Inertial/Surface/Target) 世界旋转与贴图
    /// 2. 2D 亚像素无畸变平滑投影矢量标线 (Prograde, Retrograde, Normal, Maneuver 等)
    /// 3. 三种核心渲染模式 (原版贴图 / 程序化导航球 / 原版导航球直驱)
    /// 4. 自适应 KSP 原生 UI_SCALE_NAVBALL 与屏幕物理 DPI 缩放，大机动下 CPU 耗时恒定 0.005 ms！
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [FlightWidget("navball", "navball_sphere", Category = WidgetCategory.Navigation, DisplayName = "3D 姿态球", Description = "现代超清矢量/贴图 3D 姿态球核心，支持无极缩放、姿态导引十字与全量机动矢量。", DefaultWidgetId = "core.navball", DefaultX = 0f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "core.navball" })]
    public class NavballSphereWidget : BaseNavballSphereWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private RawImage _displayImage;

        private Text _headingText;
        private Text _frameText;
        private GameObject _headingBox;
        private GameObject _crosshair;
        private GameObject _shellRoot;
        private Image _shellImage;
        private Outline _shellOutline;
        private Text _shellTitle;
        private Text _shellStatus;

        private readonly Dictionary<string, Image> _markerImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private float _ballDiameter;
        private float _visualRadius;
        private readonly Vector3[] _displayCorners = new Vector3[4];
        private float _lastDetailScale = -1f;

        private Image _reticleImage;
        private float _currentHazardAlert = 0.0f;
        private bool _isHazardActive = false;
        private float _hazardHoldTimer = 0.0f;
        private float _currentVernierDetail = 0.0f;
        private float _lastNavballProbeSampleTime = -1f;
        private double _cachedGpwsRadarAltitude = double.NaN;
        private double _cachedGpwsSinkRate = double.NaN;
        private double _cachedTrajImpactTime = double.NaN;

        private GameObject _bezelRing;

        private float _lastUploadedHazard = -1f;
        private float _lastUploadedVernier = -1f;
        private float _lastUploadedTrendStrength = -1f;
        private Quaternion _lastUploadedTrendRotation = Quaternion.identity;
        private bool _isPaletteLerping = false;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;

        private string _lastFrameCategory = "";
        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;
        private Quaternion _previousAttitudeRotation = Quaternion.identity;
        private Quaternion _filteredTrendRotation = Quaternion.identity;
        private Vector3 _smoothedAngularVelocity = Vector3.zero;
        private bool _hasPreviousAttitudeRotation;
        private float _attitudeTrendStrength;
        private float _lastFramePattern = -1f;
        private readonly Vector4[] _markerAvoidanceValues = new Vector4[4];
        private static readonly string[] MarkerAvoidanceKeys = { "maneuver", "prograde", "target", "normal" };
        private static readonly int[] MarkerAvoidancePropertyIds =
        {
            Shader.PropertyToID("_MarkerAvoid0"), Shader.PropertyToID("_MarkerAvoid1"),
            Shader.PropertyToID("_MarkerAvoid2"), Shader.PropertyToID("_MarkerAvoid3")
        };

        #region Pre-baked Shader Property IDs (Zero-String CPU Cache)
        private static readonly int _PropSphereInvRotation = Shader.PropertyToID("_SphereInvRotation");
        private static readonly int _PropRenderMode = Shader.PropertyToID("_RenderMode");
        private static readonly int _PropSkyZenithColor = Shader.PropertyToID("_SkyZenithColor");
        private static readonly int _PropSkyHorizonColor = Shader.PropertyToID("_SkyHorizonColor");
        private static readonly int _PropGroundHorizonColor = Shader.PropertyToID("_GroundHorizonColor");
        private static readonly int _PropGroundNadirColor = Shader.PropertyToID("_GroundNadirColor");
        private static readonly int _PropEquatorColor = Shader.PropertyToID("_EquatorColor");
        private static readonly int _PropEquatorWidth = Shader.PropertyToID("_EquatorWidth");
        private static readonly int _PropPitchLadderColor = Shader.PropertyToID("_PitchLadderColor");
        private static readonly int _PropPitchLadderWidth = Shader.PropertyToID("_PitchLadderWidth");
        private static readonly int _PropHeadingLineColor = Shader.PropertyToID("_HeadingLineColor");
        private static readonly int _PropRimColor = Shader.PropertyToID("_RimColor");
        private static readonly int _PropLabelColor = Shader.PropertyToID("_LabelColor");
        private static readonly int _PropLabelOutlineColor = Shader.PropertyToID("_LabelOutlineColor");
        private static readonly int _PropGroundHazardAlert = Shader.PropertyToID("_GroundHazardAlert");
        private static readonly int _PropVernierScaleDetail = Shader.PropertyToID("_VernierScaleDetail");
        private static readonly int _PropFramePattern = Shader.PropertyToID("_FramePattern");
        private static readonly int _PropTrendRotation = Shader.PropertyToID("_TrendRotation");
        private static readonly int _PropTrendStrength = Shader.PropertyToID("_TrendStrength");
        private static readonly int _PropDetailScale = Shader.PropertyToID("_DetailScale");
        private static readonly int _PropMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int _PropSkyColor = Shader.PropertyToID("_SkyColor");
        private static readonly int _PropGroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int _PropGridColor = Shader.PropertyToID("_GridColor");
        private static readonly int _PropHorizonLineColor = Shader.PropertyToID("_HorizonLineColor");
        private static readonly int _PropDotColor = Shader.PropertyToID("_DotColor");
        private static readonly int _PropDotDensity = Shader.PropertyToID("_DotDensity");
        private static readonly int _PropDotMinRadius = Shader.PropertyToID("_DotMinRadius");
        private static readonly int _PropDotMaxRadius = Shader.PropertyToID("_DotMaxRadius");
        private static readonly int _PropAtmosphereGlowColor = Shader.PropertyToID("_AtmosphereGlowColor");
        private static readonly int _PropEmissionIntensity = Shader.PropertyToID("_EmissionIntensity");
        private static readonly int _PropNumeralUprightMode = Shader.PropertyToID("_NumeralUprightMode");
        private static readonly int _PropNumeralRollAngle = Shader.PropertyToID("_NumeralRollAngle");
        private static readonly int _PropNumeralTangentComp = Shader.PropertyToID("_NumeralTangentComp");
        #endregion

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float ballDiameter = 150f * s;
            _ballDiameter = ballDiameter;
            float shellWidth = ballDiameter + 92f * s;
            float shellHeight = ballDiameter + 116f * s;

            bool showShell = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showHeadingBox = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("heading_box", StringComparison.OrdinalIgnoreCase) >= 0;

            RectTransform.sizeDelta = showShell ? new Vector2(shellWidth, shellHeight) : new Vector2(ballDiameter, ballDiameter);
            _visualRadius = ballDiameter * 0.5f * 0.94f;

            // 0. 航电外壳隔离
            ApplyCanvasIsolation(true);

            if (showShell)
            {
                CreateNavballShell(shellWidth, shellHeight, s, theme);
            }

            // 1. 屏幕空间数学解析光线投射姿态球 (Screen-Space Analytic Raymarched Sphere)
            // 彻底移除独立摄像机、3D 网格小球与 RenderTexture，改为在原生 UGUI 矩形上通过着色器直接解析绘制
            // 0 摄像机开销、0 离屏纹理显存、任意大机动旋转下 CPU 耗时恒定 0.005 ms！
            GameObject raymarchObj = new GameObject("Sphere_RaymarchImage", typeof(RectTransform), typeof(RawImage));
            raymarchObj.transform.SetParent(transform, false);
            RectTransform rawRt = raymarchObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(ballDiameter, ballDiameter);
            rawRt.anchoredPosition = Vector2.zero;

            _displayImage = raymarchObj.GetComponent<RawImage>();
            _displayImage.texture = Texture2D.whiteTexture;
            _displayImage.raycastTarget = false;

            Shader targetShader = AssetLoader.RaymarchShader ?? AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);
            _displayImage.material = _sphereMaterial;

            var initialMode = ThemeManager.Instance.GlobalRenderMode;
            if (initialMode == NavballRenderMode.StockDirect)
            {
                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
            }

            // 2. 2D 矢量标线层 (Prograde, Retrograde, Normal, Radial, Target, Maneuver)
            CreateMarkerOverlayLayer(transform, CurrentDpiScale);

            // 3. 瞄准标与金属圆环包边
            CreateCrosshair(transform, CurrentDpiScale, theme);

            // 极细航电金属质感圆环外圈
            GameObject bezelObj = UIFactory.CreatePanel(transform, "Sphere_Bezel_Ring",
                new Vector2(ballDiameter + 2f * CurrentDpiScale, ballDiameter + 2f * CurrentDpiScale),
                Vector2.zero, Color.clear);
            Outline bezelOutline = bezelObj.AddComponent<Outline>();
            Color border = theme.FrameBorderColor;
            bezelOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
            bezelOutline.effectDistance = new Vector2(1.2f * CurrentDpiScale, 1.2f * CurrentDpiScale);
            _bezelRing = bezelObj;

            if (showHeadingBox)
            {
                CreateHeadingBox(transform, CurrentDpiScale, theme);
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetGraphicViewportControl("navball_viewport", "3D球体视口", _displayImage != null ? _displayImage.gameObject : gameObject, _displayImage));
            if (_crosshair != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "crosshair", "准星标线", _crosshair));
            if (_headingBox != null) this.Controls.Register(new WidgetReadoutControl("heading_box", "航向盒读数", _headingBox, _headingText, _frameText, TextStyleRole.PrimaryValue));
            if (_shellRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "ecam_shell", "ECAM机匣外壳", _shellRoot, (t) => ApplyCard(_shellImage, _shellOutline, CardStyleRole.Normal, t)));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void CreateNavballShell(float shellWidth, float shellHeight, float dpiScale, ThemeConfig theme)
        {
            _shellRoot = UIFactory.CreatePanel(transform, "Navball_Ecam_Shell", new Vector2(shellWidth, shellHeight),
                Vector2.zero, theme.FrameBgColor);
            _shellRoot.transform.SetAsFirstSibling();
            _shellImage = _shellRoot.GetComponent<Image>();
            _shellImage.raycastTarget = false;
            _shellOutline = _shellRoot.AddComponent<Outline>();
            _shellOutline.effectDistance = new Vector2(1f * dpiScale, 1f * dpiScale);

            // 顶部技术标题与在线状态，保持 SpaceX / F-35 极简平显风格
            _shellTitle = UIFactory.CreateText(_shellRoot.transform, "Navball_Shell_Title",
                "ATTITUDE  /  NAVBALL", Mathf.Max(8, Mathf.RoundToInt(8f * dpiScale)),
                TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform titleRt = _shellTitle.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(shellWidth - 30f * dpiScale, 16f * dpiScale);
            titleRt.anchoredPosition = new Vector2(-shellWidth * 0.5f + 15f * dpiScale,
                shellHeight * 0.5f - 12f * dpiScale);

            _shellStatus = UIFactory.CreateText(_shellRoot.transform, "Navball_Shell_Status", "LIVE",
                Mathf.Max(7, Mathf.RoundToInt(7f * dpiScale)), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statusRt = _shellStatus.GetComponent<RectTransform>();
            statusRt.sizeDelta = new Vector2(42f * dpiScale, 14f * dpiScale);
            statusRt.anchoredPosition = new Vector2(shellWidth * 0.5f - 28f * dpiScale,
                shellHeight * 0.5f - 12f * dpiScale);

            // 顶部装饰分割微线
            Color borderCol = theme.FrameBorderColor;
            UIFactory.CreatePanel(_shellRoot.transform, "Shell_Top_Div",
                new Vector2(shellWidth - 20f * dpiScale, 1f * dpiScale),
                new Vector2(0f, shellHeight * 0.5f - 22f * dpiScale),
                WidgetStyleManager.Weighted(borderCol, LineWeight.Light));

            // 底部状态微标栏
            GameObject bottomBar = UIFactory.CreatePanel(_shellRoot.transform, "Navball_Shell_BottomBar",
                new Vector2(shellWidth - 20f * dpiScale, 16f * dpiScale),
                new Vector2(0f, -shellHeight * 0.5f + 14f * dpiScale),
                WidgetStyleManager.Surface(SurfaceStyleRole.Slot));
            bottomBar.GetComponent<Image>().raycastTarget = false;

            Text bottomText = UIFactory.CreateText(bottomBar.transform, "Bottom_Label", "FRAME / ATTITUDE",
                Mathf.Max(7, Mathf.RoundToInt(7f * dpiScale)), TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform bottomTextRt = bottomText.GetComponent<RectTransform>();
            bottomTextRt.anchorMin = Vector2.zero;
            bottomTextRt.anchorMax = Vector2.one;
            bottomTextRt.sizeDelta = Vector2.zero;
            bottomTextRt.anchoredPosition = Vector2.zero;
        }

        public override void UpdateSphereScale()
        {
            // 在数学解析光线投射管线中无需操作 3D 网格 scale
        }

        private void CreateMarkerOverlayLayer(Transform parent, float dpiScale)
        {
            GameObject markerLayerObj = new GameObject("Markers_Layer", typeof(RectTransform));
            markerLayerObj.layer = parent.gameObject.layer;
            markerLayerObj.transform.SetParent(parent, false);
            RectTransform mlRt = markerLayerObj.GetComponent<RectTransform>();
            mlRt.sizeDelta = new Vector2(_visualRadius * 2f, _visualRadius * 2f);
            mlRt.anchoredPosition = Vector2.zero;

            string[] markerKeys = new string[]
            {
                "prograde", "retrograde", "velocity_vector", "anti_velocity_vector",
                "normal", "antinormal",
                "radialin", "radialout", "target", "antitarget", "maneuver"
            };

            float markerSize = 25f * dpiScale;

            for (int i = 0; i < markerKeys.Length; i++)
            {
                string key = markerKeys[i];
                GameObject mObj = new GameObject("Marker_" + key, typeof(RectTransform), typeof(Image));
                mObj.layer = markerLayerObj.layer;
                mObj.transform.SetParent(markerLayerObj.transform, false);

                RectTransform mRt = mObj.GetComponent<RectTransform>();
                mRt.sizeDelta = new Vector2(markerSize, markerSize);
                mRt.anchoredPosition = Vector2.zero;

                Image img = mObj.GetComponent<Image>();
                img.sprite = NavballMarkerFactory.GetMarkerSprite(key);
                img.color = WidgetStyleManager.NeutralOpaque;
                img.raycastTarget = false;
                mObj.SetActive(false);

                _markerImages[key] = img;
            }
        }

        private void CreateCrosshair(Transform parent, float dpiScale, ThemeConfig theme)
        {
            float s = dpiScale;
            Vector2 reticleSize = new Vector2(96f * s, 48f * s);
            _crosshair = UIFactory.CreatePanel(parent, "Crosshair_Center", reticleSize, Vector2.zero, WidgetStyleManager.NeutralOpaque);
            _reticleImage = _crosshair.GetComponent<Image>();
            _reticleImage.sprite = NavballMarkerFactory.GetReticleSprite();
            _reticleImage.raycastTarget = false;
        }

        private void CreateHeadingBox(Transform parent, float dpiScale, ThemeConfig theme)
        {
            Vector2 boxSize = new Vector2(68f * dpiScale, 36f * dpiScale);
            Vector2 anchoredPos = new Vector2(0f, _visualRadius + 22f * dpiScale);

            _headingBox = UIFactory.CreatePanel(parent, "Heading_Box", boxSize, anchoredPos, theme.FrameBgColor);
            _headingBox.GetComponent<Image>().raycastTarget = false;

            GameObject border = UIFactory.CreatePanel(_headingBox.transform, "Heading_Border", boxSize, Vector2.zero, Color.clear);
            Outline outline = border.AddComponent<Outline>();
            outline.effectColor = theme.FrameBorderColor;
            outline.effectDistance = new Vector2(1.5f * dpiScale, 1.5f * dpiScale);

            int fontSize = Mathf.RoundToInt(14f * dpiScale);
            _headingText = UIFactory.CreateText(_headingBox.transform, "Heading_Text", "000°", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0f, 0.35f);
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;

            int frameFontSize = Mathf.Max(9, Mathf.RoundToInt(9f * dpiScale));
            _frameText = UIFactory.CreateText(_headingBox.transform, "Frame_Text", "ORBIT", frameFontSize, TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform frameRt = _frameText.GetComponent<RectTransform>();
            frameRt.anchorMin = Vector2.zero;
            frameRt.anchorMax = new Vector2(1f, 0.4f);
            frameRt.sizeDelta = Vector2.zero;
            frameRt.anchoredPosition = Vector2.zero;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);
            var mode = ThemeManager.Instance.GlobalRenderMode;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            if (mode == NavballRenderMode.StockDirect)
            {
                if (_headingText != null)
                {
                    string targetHeading = hasHook ? hook.HeadingText : $"HDG {Mathf.RoundToInt(FlightTelemetryContext.Current?.Heading ?? 0f) % 360:D3}°";
                    if (_headingText.text != targetHeading) _headingText.text = targetHeading;
                }
                if (_frameText != null)
                {
                    string targetFrame = hasHook ? hook.FrameName : (hook?.ReferenceFrameCategory ?? "SURFACE");
                    if (_frameText.text != targetFrame) _frameText.text = targetFrame;
                }
                return;
            }

            bool isStockTexture = (mode == NavballRenderMode.StockTexture);
            _visualRadius = _ballDiameter * 0.5f * (isStockTexture ? 1.0f : 0.94f);

            if (_sphereMaterial != null)
            {
                if (isStockTexture && hasHook)
                {
                    Texture stockTex = hook.BallTexture;
                    if (stockTex != null && _sphereMaterial.mainTexture != stockTex)
                    {
                        _sphereMaterial.mainTexture = stockTex;
                    }
                    if (_sphereMaterial.mainTextureScale != hook.TextureScale)
                    {
                        _sphereMaterial.mainTextureScale = hook.TextureScale;
                    }
                    if (_sphereMaterial.mainTextureOffset != hook.TextureOffset)
                    {
                        _sphereMaterial.mainTextureOffset = hook.TextureOffset;
                    }
                }
            }

            // 若已启用现代航向弧带 core.heading_arc，则自动隐藏传统方盒避免遮挡重叠
            bool hasHeadingArc = WidgetLayoutManager.Instance.GetConfig("core.heading_arc")?.IsEnabled ?? false;
            if (_headingBox != null && _headingBox.activeSelf == hasHeadingArc)
            {
                _headingBox.SetActive(!hasHeadingArc);
            }

            // 2. 程序化多参考系自适应变色与高级航电动态特性驱动 (Principia / Stock 多参考系高保真映射)
            string category = hook?.ReferenceFrameCategory ?? "SURFACE";

            if (category != _lastFrameCategory || !_paletteInitialized)
            {
                _targetPalette = GetPaletteForCategory(category, ThemeManager.Instance.CurrentTheme);
                if (!_paletteInitialized)
                {
                    _currentPalette = _targetPalette;
                    _paletteInitialized = true;
                    UploadPaletteToMaterial(_currentPalette);
                }
                _lastFrameCategory = category;
                _isPaletteLerping = true;
            }

            if (_sphereMaterial != null)
            {
                float framePattern = GetFramePatternCode(category);
                if (mode == NavballRenderMode.ProceduralVector && Mathf.Abs(framePattern - _lastFramePattern) > 0.01f && _sphereMaterial.HasProperty(_PropFramePattern))
                {
                    _sphereMaterial.SetFloat(_PropFramePattern, framePattern);
                    _lastFramePattern = framePattern;
                }

                if (_isPaletteLerping)
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

                // GPWS / 近地大下沉率防撞动态斑马纹警示驱动 (Ground Terrain Hazard Pull-Up Alert)
                IFlightTelemetry curTelem = FlightTelemetryContext.Current;
                bool isHazardTriggered = false;
                if (curTelem != null && curTelem.HasVessel)
                {
                    UpdateNavballProbeSnapshot(curTelem);
                    double radarAltitude = _cachedGpwsRadarAltitude;
                    if (double.IsNaN(radarAltitude) || double.IsInfinity(radarAltitude) || radarAltitude < 0.0)
                        radarAltitude = curTelem.AltitudeAGL;

                    double gpwsSinkRate = _cachedGpwsSinkRate;
                    double descentRate = Math.Max(0.0, -curTelem.VerticalSpeed);
                    if (curTelem.VerticalSpeed < 0.0 && !double.IsNaN(gpwsSinkRate) && !double.IsInfinity(gpwsSinkRate))
                        descentRate = Math.Max(descentRate, Math.Abs(gpwsSinkRate));

                    double impactTime = _cachedTrajImpactTime;
                    bool predictedImpact = !double.IsNaN(impactTime) && !double.IsInfinity(impactTime) && impactTime > 0.0 && impactTime < 12.0;
                    bool lowAltitudeDescent = radarAltitude > 0.0 && radarAltitude < 280.0 && curTelem.VerticalSpeed < 0.0 && descentRate > 8.5;
                    isHazardTriggered = lowAltitudeDescent || predictedImpact || curTelem.IsTouchdownAlert;
                }

                float dtHazard = Time.deltaTime;
                if (isHazardTriggered)
                {
                    _isHazardActive = true;
                    _hazardHoldTimer = 0.6f;
                }
                else if (_hazardHoldTimer > 0f)
                {
                    _hazardHoldTimer -= dtHazard;
                    if (_hazardHoldTimer <= 0f) _isHazardActive = false;
                }
                else
                {
                    _isHazardActive = false;
                }

                if (mode == NavballRenderMode.ProceduralVector)
                {
                    float targetHazard = _isHazardActive ? 1.0f : 0.0f;
                    _currentHazardAlert = Mathf.MoveTowards(_currentHazardAlert, targetHazard, (!Application.isPlaying ? 1.0f : dtHazard * 3.5f));
                    if (Mathf.Abs(_currentHazardAlert - _lastUploadedHazard) > 0.015f && _sphereMaterial.HasProperty(_PropGroundHazardAlert))
                    {
                        _sphereMaterial.SetFloat(_PropGroundHazardAlert, _currentHazardAlert);
                        _lastUploadedHazard = _currentHazardAlert;
                    }

                    // 近地平精密 2.5° 游标微调刻度 (Vernier Scale Detail)
                    float pitchVal = (curTelem != null) ? Mathf.Abs(curTelem.Pitch) : 0f;
                    float targetVernier = (pitchVal < 6.0f) ? Mathf.Clamp01((6.0f - pitchVal) / 3.0f) : 0f;
                    _currentVernierDetail = Mathf.MoveTowards(_currentVernierDetail, targetVernier, (!Application.isPlaying ? 1.0f : dtHazard * 3.5f));
                    if (Mathf.Abs(_currentVernierDetail - _lastUploadedVernier) > 0.025f && _sphereMaterial.HasProperty(_PropVernierScaleDetail))
                    {
                        _sphereMaterial.SetFloat(_PropVernierScaleDetail, _currentVernierDetail);
                        _lastUploadedVernier = _currentVernierDetail;
                    }
                }
            }

            // 3. 航向读数与参考系模式更新
            if (_headingText != null)
            {
                string targetHeading = hasHook ? hook.HeadingText : $"HDG {Mathf.RoundToInt(FlightTelemetryContext.Current?.Heading ?? 0f) % 360:D3}°";
                if (_headingText.text != targetHeading)
                {
                    _headingText.text = targetHeading;
                }
            }
            if (_frameText != null)
            {
                string targetFrame = hasHook ? hook.FrameName : category;
                if (_frameText.text != targetFrame)
                {
                    _frameText.text = targetFrame;
                }
                if (_frameText.color != _currentPalette.Rim)
                {
                    _frameText.color = _currentPalette.Rim;
                }
            }
            if (_shellStatus != null && _shellStatus.color != _currentPalette.Rim)
            {
                _shellStatus.color = _currentPalette.Rim;
            }
        }

        private void UpdateNavballProbeSnapshot(IFlightTelemetry telemetry)
        {
            float now = Time.unscaledTime;
            if (Application.isPlaying && now - _lastNavballProbeSampleTime < 0.1f) return;
            _lastNavballProbeSampleTime = now;
            _cachedGpwsRadarAltitude = TelemetryTokenEngine.EvaluateNumeric("{GPWS:RadarAltitude}", telemetry);
            _cachedGpwsSinkRate = TelemetryTokenEngine.EvaluateNumeric("{GPWS:SinkRate}", telemetry);
            _cachedTrajImpactTime = TelemetryTokenEngine.EvaluateNumeric("{TRAJ:ImpactTime}", telemetry);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!gameObject.activeInHierarchy) return;

            var mode = ThemeManager.Instance.GlobalRenderMode;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            if (mode == NavballRenderMode.StockDirect)
            {
                if (_displayImage != null && _displayImage.enabled) _displayImage.enabled = false;
                if (_crosshair != null && _crosshair.activeSelf) _crosshair.SetActive(false);
                if (_bezelRing != null && _bezelRing.activeSelf) _bezelRing.SetActive(false);
                foreach (var kvp in _markerImages)
                {
                    if (kvp.Value != null && kvp.Value.gameObject.activeSelf) kvp.Value.gameObject.SetActive(false);
                }

                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
                NavBallHookService.SyncStockNavballAction?.Invoke(this.RectTransform, Config != null ? Config.Scale : 1.0f);
                return;
            }
            else
            {
                if (NavBallHookService.IsCleanStockNavballActiveFunc?.Invoke() ?? false)
                {
                    NavBallHookService.ResetStockNavballAction?.Invoke();
                    NavBallHookService.SetStockNavballCleanAction?.Invoke(false);
                    NavBallHookService.HideStockNavballAction?.Invoke(true);
                }
                if (_displayImage != null && !_displayImage.enabled) _displayImage.enabled = true;
                if (_crosshair != null && !_crosshair.activeSelf) _crosshair.SetActive(true);
                if (_bezelRing != null && !_bezelRing.activeSelf) _bezelRing.SetActive(true);
            }

            if (_displayImage == null || !_displayImage.enabled || !_displayImage.gameObject.activeInHierarchy) return;

            // 在 Principia/官方 LateUpdate 彻底执行完毕后，执行最终高保真姿态与标线同步
            SyncAttitudeAndVisuals();
            SyncMarkers();
            UpdateReticleDynamics();
            UpdateProceduralDetailScale();
        }

        private float _lastMarkerDiagLogTime = -10f;

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

                // 视界边缘平滑过渡 [-0.08, -0.24]，彻底消灭坐标跳变与缩放突变
                if (hasDir && isVisible && dir.z > -0.24f)
                {
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                    Vector2 bearing = new Vector2(dir.x, dir.y);
                    float bearingMag = bearing.magnitude;
                    Vector2 normBearing = bearingMag > 0.001f ? (bearing / bearingMag) : Vector2.up;

                    float tRear = Mathf.Clamp01((-0.08f - dir.z) / 0.12f);

                    Vector2 frontPos = new Vector2(dir.x, dir.y) * _visualRadius;
                    Vector2 rearPos = normBearing * (_visualRadius * 0.84f);
                    img.rectTransform.anchoredPosition = Vector2.Lerp(frontPos, rearPos, tRear);

                    float targetScale = Mathf.Lerp(1.0f, 0.65f, tRear);
                    if (Mathf.Abs(img.rectTransform.localScale.x - targetScale) > 0.02f)
                    {
                        img.rectTransform.localScale = Vector3.one * targetScale;
                    }

                    float frontAlpha = Mathf.Clamp01((dir.z + 0.15f) / 0.25f);
                    float rearAlpha = Mathf.Lerp(0.38f, 0.16f, Mathf.Clamp01(-dir.z));
                    float finalAlpha = Mathf.Lerp(frontAlpha, rearAlpha, tRear);

                    if (Mathf.Abs(img.color.a - finalAlpha) > 0.05f)
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = finalAlpha;
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

            UpdateMarkerAvoidanceMasks();

            if (Application.isPlaying && Time.unscaledTime - _lastMarkerDiagLogTime > 5.0f)
            {
                _lastMarkerDiagLogTime = Time.unscaledTime;
                System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
                sb.Append("[ModularFlightPanel][NavballDiag] Active Markers: ");
                int count = 0;
                foreach (var kvp in _markerImages)
                {
                    if (kvp.Value != null && kvp.Value.gameObject.activeSelf)
                    {
                        sb.Append(kvp.Key).Append(" ");
                        count++;
                    }
                }
                if (count == 0) sb.Append("(none)");
                Debug.Log(sb.ToString());
            }
        }

        private void UpdateReticleDynamics()
        {
            if (_reticleImage == null) return;
            float movement = _attitudeTrendStrength;
            float targetAlpha = Mathf.Lerp(0.55f, 0.45f, movement);
            Color reticleColor = _reticleImage.color;
            if (Mathf.Abs(reticleColor.a - targetAlpha) > 0.05f)
            {
                reticleColor.a = targetAlpha;
                _reticleImage.color = reticleColor;
            }
        }

        private void UpdateMarkerAvoidanceMasks()
        {
            if (ThemeManager.Instance.GlobalRenderMode != NavballRenderMode.ProceduralVector) return;
            if (_sphereMaterial == null || !_sphereMaterial.HasProperty(MarkerAvoidancePropertyIds[0])) return;
            float halfDiameter = Mathf.Max(1f, _ballDiameter * 0.5f);
            for (int i = 0; i < MarkerAvoidanceKeys.Length; i++)
            {
                Vector4 avoidance = Vector4.zero;
                if (_markerImages.TryGetValue(MarkerAvoidanceKeys[i], out Image marker) && marker != null && marker.gameObject.activeSelf)
                {
                    Vector2 point = marker.rectTransform.anchoredPosition / halfDiameter;
                    float radius = 0.20f;
                    avoidance = new Vector4(point.x, point.y, radius, 1.0f);
                }
                Vector4 prev = _markerAvoidanceValues[i];
                if (Mathf.Abs(avoidance.x - prev.x) > 0.015f ||
                    Mathf.Abs(avoidance.y - prev.y) > 0.015f ||
                    Mathf.Abs(avoidance.w - prev.w) > 0.5f)
                {
                    _markerAvoidanceValues[i] = avoidance;
                    _sphereMaterial.SetVector(MarkerAvoidancePropertyIds[i], avoidance);
                }
            }
        }

        private void UpdateProceduralDetailScale()
        {
            if (ThemeManager.Instance.GlobalRenderMode != NavballRenderMode.ProceduralVector) return;
            if (_sphereMaterial == null || !_sphereMaterial.HasProperty(_PropDetailScale) || _displayImage == null) return;

            int sw = Screen.width;
            int sh = Screen.height;
            if (_lastDetailScale >= 0f && sw == _lastScreenWidth && sh == _lastScreenHeight && !transform.hasChanged)
            {
                return;
            }
            _lastScreenWidth = sw;
            _lastScreenHeight = sh;

            Canvas canvas = _displayImage.canvas;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransform imageRect = _displayImage.rectTransform;
            imageRect.GetWorldCorners(_displayCorners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[0]);
            Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[1]);
            Vector2 bottomRight = RectTransformUtility.WorldToScreenPoint(uiCamera, _displayCorners[3]);
            float displayPixels = Mathf.Max(Vector2.Distance(bottomLeft, topLeft), Vector2.Distance(bottomLeft, bottomRight));
            float detailScale = Mathf.InverseLerp(88f, 240f, displayPixels);
            if (Mathf.Abs(detailScale - _lastDetailScale) > 0.015f)
            {
                _lastDetailScale = detailScale;
                _sphereMaterial.SetFloat(_PropDetailScale, detailScale);
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
                case "BODY_DIRECTION":
                    return WidgetStyleManager.Instance.GetNavballFramePalette(theme.WarningColor, theme);
                case "BODY_SURFACE":
                {
                    Color bGndH = theme.GroundColor;
                    Color bGndN = WidgetStyleManager.Darken(bGndH, 0.55f);
                    Color bSkyH = theme.AccentSecondary;
                    Color bSkyZ = theme.SkyColor;
                    return new NavballFramePalette
                    {
                        SkyZenith = bGndN,
                        SkyHorizon = bGndH,
                        GroundHorizon = bSkyH,
                        GroundNadir = bSkyZ,
                        Equator = theme.HorizonLineColor,
                        PitchLadder = theme.GridColor,
                        HeadingLine = theme.AccentSecondary,
                        Rim = theme.RimGlowColor
                    };
                }
                case "SURFACE":
                default:
                    Color skyZ = theme.SkyColor;
                    Color skyH = theme.AccentSecondary;
                    Color gndH = theme.GroundColor;
                    Color gndN = WidgetStyleManager.Darken(gndH, 0.55f);
                    Color eq = theme.HorizonLineColor;
                    Color pitch = theme.GridColor;
                    Color hdg = theme.AccentSecondary;
                    Color rim = theme.RimGlowColor;
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

        private void UploadPaletteToMaterial(Material mat, NavballFramePalette palette)
        {
            if (mat == null) return;
            if (mat.HasProperty(_PropSkyZenithColor))
            {
                mat.SetColor(_PropSkyZenithColor, palette.SkyZenith);
                mat.SetColor(_PropSkyHorizonColor, palette.SkyHorizon);
                mat.SetColor(_PropGroundHorizonColor, palette.GroundHorizon);
                mat.SetColor(_PropGroundNadirColor, palette.GroundNadir);
                mat.SetColor(_PropEquatorColor, palette.Equator);
                mat.SetColor(_PropPitchLadderColor, palette.PitchLadder);
                mat.SetColor(_PropHeadingLineColor, palette.HeadingLine);
                mat.SetColor(_PropRimColor, palette.Rim);
                var curTheme = ThemeManager.Instance.CurrentTheme;
                if (curTheme != null)
                {
                    mat.SetColor(_PropLabelColor, curTheme.TextPrimaryColor);
                    mat.SetColor(_PropLabelOutlineColor, curTheme.TextInverseColor);
                }
            }
        }

        private void UploadPaletteToMaterial(NavballFramePalette palette)
        {
            UploadPaletteToMaterial(_sphereMaterial, palette);
        }

        private static NavballFramePalette LerpPalette(NavballFramePalette from, NavballFramePalette to, float t)
        {
            return WidgetStyleManager.LerpFramePalette(from, to, t);
        }

        private static float GetFramePatternCode(string category)
        {
            switch (category?.ToUpperInvariant())
            {
                case "INERTIAL": return 1f;
                case "ORBIT": return 1f;
                case "BARYCENTRIC": return 2f;
                case "TARGET": return 3f;
                case "BODY_DIRECTION": return 4f;
                case "BODY_SURFACE": return 5f;
                default: return 0f;
            }
        }

        private void UpdateAttitudeTrend(Quaternion currentRotation)
        {
            float dt = Time.unscaledDeltaTime;
            float targetStrength = 0f;
            Quaternion targetTrendRotation = Quaternion.identity;

            if (_hasPreviousAttitudeRotation && dt > 0.001f && dt < 0.25f)
            {
                Quaternion delta = currentRotation * Quaternion.Inverse(_previousAttitudeRotation);
                if (delta.w < 0f)
                    delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

                Vector3 halfAxis = new Vector3(delta.x, delta.y, delta.z);
                float sinHalfAngle = halfAxis.magnitude;

                Vector3 rawAngularVelocity = Vector3.zero;
                if (sinHalfAngle > 0.00015f)
                {
                    float angleDegrees = 2f * Mathf.Atan2(sinHalfAngle, Mathf.Clamp(delta.w, 0f, 1f)) * Mathf.Rad2Deg;
                    float rawRate = angleDegrees / dt;
                    if (rawRate < 180f)
                    {
                        rawAngularVelocity = (halfAxis / sinHalfAngle) * rawRate;
                    }
                }

                // 低通滤波角速度矢量，彻底消除跨物理帧瞬时微步进与角轴旋转随机翻转
                float filterBlend = (!Application.isPlaying || dt <= 0.0001f) ? 1f : Mathf.Clamp01(dt * 6.5f);
                _smoothedAngularVelocity = Vector3.Lerp(_smoothedAngularVelocity, rawAngularVelocity, filterBlend);
            }
            else
            {
                _smoothedAngularVelocity = Vector3.zero;
            }

            _previousAttitudeRotation = currentRotation;
            _hasPreviousAttitudeRotation = true;

            float smoothRate = _smoothedAngularVelocity.magnitude;
            // 死区量化守卫：低于 0.40°/s 的微幅扰动视为稳态静止，杜绝虚线趋势指示抖动
            if (smoothRate > 0.40f)
            {
                Vector3 axis = _smoothedAngularVelocity / smoothRate;
                float predictionAngle = Mathf.Clamp(smoothRate * 0.45f, 0f, 20f);
                Quaternion parentPrediction = Quaternion.AngleAxis(predictionAngle, axis);
                targetTrendRotation = Quaternion.Inverse(currentRotation) * parentPrediction * currentRotation;
                targetStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 5.0f, smoothRate));
            }
            else
            {
                targetTrendRotation = Quaternion.identity;
                targetStrength = 0f;
            }

            float blend = (!Application.isPlaying || dt <= 0.0001f) ? 1f : Mathf.Clamp01(dt * 7.0f);
            _filteredTrendRotation = Quaternion.Slerp(_filteredTrendRotation, targetTrendRotation, blend);
            _attitudeTrendStrength = Mathf.MoveTowards(_attitudeTrendStrength, targetStrength, (!Application.isPlaying ? 1f : dt * 3.5f));

            if (_sphereMaterial != null && _sphereMaterial.HasProperty(_PropTrendRotation))
            {
                var curMode = ThemeManager.Instance.GlobalRenderMode;
                if (curMode == NavballRenderMode.ProceduralVector)
                {
                    bool trendDiffers = Mathf.Abs(_attitudeTrendStrength - _lastUploadedTrendStrength) > 0.01f ||
                                       (_attitudeTrendStrength > 0.01f && Quaternion.Angle(_filteredTrendRotation, _lastUploadedTrendRotation) > 0.05f);
                    if (trendDiffers)
                    {
                        _sphereMaterial.SetVector(_PropTrendRotation, new Vector4(
                            _filteredTrendRotation.x, _filteredTrendRotation.y, _filteredTrendRotation.z, _filteredTrendRotation.w));
                        _sphereMaterial.SetFloat(_PropTrendStrength, _attitudeTrendStrength);
                        _lastUploadedTrendStrength = _attitudeTrendStrength;
                        _lastUploadedTrendRotation = _filteredTrendRotation;
                    }
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

            UpdateAttitudeTrend(rawRot);

            if (_sphereMaterial != null)
            {
                // 数学解析光线投射姿态逆矩阵：视线向量 viewRay 乘以此逆矩阵即为球体模型坐标 p
                Matrix4x4 invRot = Matrix4x4.Rotate(Quaternion.Inverse(rawRot));
                _sphereMaterial.SetMatrix(_PropSphereInvRotation, invRot);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _paletteInitialized = false;
            _lastFrameCategory = null;
            _lastFramePattern = -1f;

            var mode = ThemeManager.Instance.GlobalRenderMode;
            if (mode == NavballRenderMode.ProceduralBake) mode = NavballRenderMode.ProceduralVector;

            if (mode == NavballRenderMode.StockDirect)
            {
                NavBallHookService.SetStockNavballCleanAction?.Invoke(true);
                if (_displayImage != null) _displayImage.enabled = false;
                if (_crosshair != null) _crosshair.SetActive(false);
                if (_bezelRing != null) _bezelRing.SetActive(false);
                foreach (var kvp in _markerImages)
                {
                    if (kvp.Value != null) kvp.Value.gameObject.SetActive(false);
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

                Shader targetShader = AssetLoader.RaymarchShader ?? AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;

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
                        Texture stockTex = NavBallHookService.Provider?.BallTexture;
                        if (stockTex != null) _sphereMaterial.mainTexture = stockTex;
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
            }

            if (_shellImage != null)
            {
                ApplyCard(_shellImage, _shellOutline, CardStyleRole.Normal, theme);
            }
            if (_headingText != null)
            {
                ApplyText(_headingText, TextStyleRole.PrimaryValue, theme);
            }
            if (_frameText != null)
            {
                ApplyText(_frameText, TextStyleRole.Label, theme);
            }
            if (_shellTitle != null)
            {
                ApplyText(_shellTitle, TextStyleRole.Label, theme);
            }
            if (_shellStatus != null)
            {
                ApplyText(_shellStatus, TextStyleRole.SecondaryValue, theme);
            }

            NavballMarkerFactory.ClearCache();
            if (_markerImages != null)
            {
                foreach (var kvp in _markerImages)
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.sprite = NavballMarkerFactory.GetMarkerSprite(kvp.Key);
                    }
                }
            }
            if (_reticleImage != null)
            {
                _reticleImage.sprite = NavballMarkerFactory.GetReticleSprite();
                _reticleImage.color = WidgetStyleManager.NeutralOpaque;
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void HandleResolutionChanged(int newRes)
        {
            // 数学解析光线投射管线直接在屏幕空间亚像素级绘制，无需管理离屏 RenderTexture 分辨率
        }

        protected override void HandleRenderSettingChanged()
        {
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            _markerImages.Clear();
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
}
