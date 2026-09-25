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
    /// 3D 姿态球主组件 (直接 Hook 官方 NavBall 实现，Mod 不做任何多余数学运算)
    /// 完整支持：
    /// 1. 原版与 Principia 动态多参考系 (Barycentric/Inertial/Surface/Target) 世界旋转与贴图
    /// 2. 2D 亚像素无畸变平滑投影矢量标线 (Prograde, Retrograde, Normal, Maneuver 等)
    /// 3. 圆形 Stencil 硬件遮罩，完全规避方形边缘杂色与 Alpha 污染
    /// 4. 自适应 KSP 原生 UI_SCALE_NAVBALL 与屏幕物理 DPI 缩放
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

        private static Mesh _primitiveSphereMesh;
        private Image _reticleImage;
        private float _currentHazardAlert = 0.0f;
        private float _currentVernierDetail = 0.0f;
        private float _lastNavballProbeSampleTime = -1f;
        private double _cachedGpwsRadarAltitude = double.NaN;
        private double _cachedGpwsSinkRate = double.NaN;
        private double _cachedTrajImpactTime = double.NaN;

        private static void EnsureDefaultSphereMesh()
        {
            if (_primitiveSphereMesh == null)
            {
                GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _primitiveSphereMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Destroy(tempSphere);
                else DestroyImmediate(tempSphere);
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            // 1. 标准姿态球基准直径为 150px * s (结合物理 DPI 与组件自身缩放统一构建原生点对点尺寸)
            float ballDiameter = 150f * s;
            _ballDiameter = ballDiameter;
            float shellWidth = ballDiameter + 92f * s;
            float shellHeight = ballDiameter + 116f * s;

            bool showShell = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showHeadingBox = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("heading_box", StringComparison.OrdinalIgnoreCase) >= 0;

            RectTransform.sizeDelta = showShell ? new Vector2(shellWidth, shellHeight) : new Vector2(ballDiameter, ballDiameter);
            _visualRadius = ballDiameter * 0.5f * 0.94f;

            // 0. 航电外壳 (当处于模块化 HUD 时默认隐藏矩形外壳，保持纯圆仪表面貌)
            ApplyCanvasIsolation(true);

            if (showShell)
            {
                CreateNavballShell(shellWidth, shellHeight, s, theme);
            }

            // 2. 动态自适应刚刚好高效 RenderTexture (依据 WidgetRenderManager 结合物理占用与倍率自适应)
            int rtResolution = 512;
            if (WidgetRenderManager.Instance != null)
            {
                rtResolution = WidgetRenderManager.Instance.CalculateOptimalResolution(
                    new Vector2(150f, 150f),
                    config != null ? config.Scale : 1.0f,
                    config != null ? config.RenderScale : 1.0f);
            }
            _renderTexture = new RenderTexture(rtResolution, rtResolution, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
                anisoLevel = 4,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();

            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnRenderResolutionChanged += HandleResolutionChanged;
                WidgetRenderManager.Instance.OnRenderSettingChanged += HandleRenderSettingChanged;
            }

            // 3. 独立离屏摄像机 (正交投影视口 1.0f 完美贴合单位球，零拉伸畸变)
            GameObject camObj = new GameObject("Navball_Offscreen_Cam", typeof(Camera));
            camObj.transform.SetParent(transform, false);
            camObj.transform.localPosition = new Vector3(0f, 0f, -2.5f);

            _ballCamera = camObj.GetComponent<Camera>();
            _ballCamera.clearFlags = CameraClearFlags.SolidColor;
            _ballCamera.backgroundColor = WidgetStyleManager.NeutralTransparent;
            _ballCamera.targetTexture = _renderTexture;
            _ballCamera.orthographic = true;
            _ballCamera.orthographicSize = 1.0f;
            _ballCamera.nearClipPlane = 0.1f;
            _ballCamera.farClipPlane = 10f;
            _ballCamera.cullingMask = 1 << 31;
            _ballCamera.enabled = false; // 严禁每帧盲目自动渲染，改由 LateUpdate 在 Principia 姿态结算完毕后权威触发

            // 4. 3D 球体 (直接共享官方 StockNavBall 网格模型与 UV 拓扑，杜绝贴图畸变)
            _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereObject.name = "Navball_3D_Sphere";
            _sphereObject.transform.SetParent(transform, false);
            _sphereObject.transform.localPosition = Vector3.zero;
            _sphereObject.layer = 31;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            // 程序化模式使用标准数学单位球 (PrimitiveType.Sphere，北极 +Y, 南极 -Y, 零极点畸变)
            UpdateSphereScale();

            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);
            mr.material = _sphereMaterial;

            // 5. RawImage 画布映射 (直接作为子物体渲染，彻底摒弃 1-bit Stencil UGUI Mask 硬锯齿)
            GameObject rawImgObj = new GameObject("Sphere_RawImage", typeof(RectTransform), typeof(RawImage));
            rawImgObj.transform.SetParent(transform, false);
            RectTransform rawRt = rawImgObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(ballDiameter, ballDiameter);
            rawRt.anchoredPosition = Vector2.zero;

            _displayImage = rawImgObj.GetComponent<RawImage>();
            _displayImage.texture = _renderTexture;
            _displayImage.raycastTarget = false;

            // 6. 2D 矢量标线层 (Prograde, Retrograde, Normal, Radial, Target, Maneuver)
            CreateMarkerOverlayLayer(transform, CurrentDpiScale);

            // 7. 瞄准标与金属圆环包边
            CreateCrosshair(transform, CurrentDpiScale, theme);

            // 极细航电金属质感圆环外圈 (消除粗糙边界与几何空隙)
            GameObject bezelObj = UIFactory.CreatePanel(transform, "Sphere_Bezel_Ring",
                new Vector2(ballDiameter + 2f * CurrentDpiScale, ballDiameter + 2f * CurrentDpiScale),
                Vector2.zero, Color.clear);
            Outline bezelOutline = bezelObj.AddComponent<Outline>();
            Color border = theme.FrameBorderColor;
            bezelOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
            bezelOutline.effectDistance = new Vector2(1.2f * CurrentDpiScale, 1.2f * CurrentDpiScale);

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

        private void UpdateSphereMeshScale()
        {
            UpdateSphereScale();
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

            // 1. 航向角读数
            int fontSize = Mathf.RoundToInt(14f * dpiScale);
            _headingText = UIFactory.CreateText(_headingBox.transform, "Heading_Text", "000°", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0f, 0.35f);
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;

            // 2. 参考系模式读数 (Principia / Stock: BARYCENTRIC, SURFACE, ORBIT, TARGET)
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

            // 0. 官方专属网格与材质属性动态挂钩检查 (贴图与程序化自适应模式)
            bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            _visualRadius = _ballDiameter * 0.5f * (isProcedural ? 0.94f : 1.0f);
            if (_sphereObject != null)
            {
                MeshFilter ourMf = _sphereObject.GetComponent<MeshFilter>();
                if (isProcedural)
                {
                    EnsureDefaultSphereMesh();
                    if (ourMf != null && _primitiveSphereMesh != null && ourMf.sharedMesh != _primitiveSphereMesh)
                    {
                        ourMf.sharedMesh = _primitiveSphereMesh;
                        UpdateSphereMeshScale();
                    }
                }
                else if (hasHook && ourMf != null)
                {
                    if (hook.StockMesh != null && ourMf.sharedMesh != hook.StockMesh)
                    {
                        ourMf.sharedMesh = hook.StockMesh;
                        UpdateSphereMeshScale();
                    }
                    else if (_sphereObject.transform.localScale == Vector3.one * 2.0f && ourMf.sharedMesh != null)
                    {
                        UpdateSphereMeshScale();
                    }

                    if (_sphereMaterial != null && _sphereMaterial.HasProperty("_MainTex"))
                    {
                        if (_sphereMaterial.mainTextureScale != hook.TextureScale)
                            _sphereMaterial.mainTextureScale = hook.TextureScale;
                        if (_sphereMaterial.mainTextureOffset != hook.TextureOffset)
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

            // 姿态与矢量标线由 LateUpdate 在 Principia 姿态结算后权威驱动，避免每帧重复计算 10+ 标线方位
        }

        private string _lastFrameCategory = "";
        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;
        private Quaternion _previousAttitudeRotation = Quaternion.identity;
        private Quaternion _filteredTrendRotation = Quaternion.identity;
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
                    // Principia 地固参考系 (Body-Centred Body-Fixed / ECEF):
                    // 严格与 Principia 官方 navball_surface 保持一致：北半球 (+lat / +Y) 对应大地棕色，南半球 (-lat / -Y) 对应海洋/天蓝色
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
                if (sinHalfAngle > 0.00001f)
                {
                    float angleDegrees = 2f * Mathf.Atan2(sinHalfAngle, Mathf.Clamp(delta.w, 0f, 1f)) * Mathf.Rad2Deg;
                    float angularRate = angleDegrees / dt;
                    Vector3 axis = halfAxis / sinHalfAngle;
                    // Longer prediction window keeps low-rate attitude changes visible.
                    float predictionAngle = Mathf.Min(angularRate * 0.75f, 24f);
                    Quaternion parentPrediction = Quaternion.AngleAxis(predictionAngle, axis);
                    targetTrendRotation = Quaternion.Inverse(currentRotation) * parentPrediction * currentRotation;
                    targetStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 5.0f, angularRate));
                }
            }

            _previousAttitudeRotation = currentRotation;
            _hasPreviousAttitudeRotation = true;
            float blend = (!Application.isPlaying || dt <= 0.0001f) ? 1f : Mathf.Clamp01(dt * 9f);
            _filteredTrendRotation = Quaternion.Slerp(_filteredTrendRotation, targetTrendRotation, blend);
            _attitudeTrendStrength = Mathf.MoveTowards(_attitudeTrendStrength, targetStrength, (!Application.isPlaying ? 1f : dt * 5f));

            if (_sphereMaterial != null && _sphereMaterial.HasProperty("_TrendRotation"))
            {
                _sphereMaterial.SetVector("_TrendRotation", new Vector4(
                    _filteredTrendRotation.x, _filteredTrendRotation.y, _filteredTrendRotation.z, _filteredTrendRotation.w));
                _sphereMaterial.SetFloat("_TrendStrength", _attitudeTrendStrength);
            }
        }

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            // 1. 姿态旋转：使用 localRotation 配合官方摄像机视口变换，杜绝任何外部画布/物体倾斜畸变
            if (_sphereObject != null)
            {
                bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
                if (hasHook)
                {
                    Quaternion camRot = hook.CameraRotation;
                    Quaternion rawRot = Quaternion.Inverse(camRot) * hook.BallRotation;
                    _sphereObject.transform.localRotation = isProcedural
                        ? new Quaternion(-rawRot.x, -rawRot.y, rawRot.z, rawRot.w)
                        : rawRot;
                }
                else
                {
                    IFlightTelemetry telem = FlightTelemetryContext.Current;
                    Quaternion rawRot = (telem != null) ? telem.AttitudeRotation : Quaternion.identity;
                    _sphereObject.transform.localRotation = isProcedural
                        ? new Quaternion(-rawRot.x, -rawRot.y, rawRot.z, rawRot.w)
                        : rawRot;
                }

                UpdateAttitudeTrend(_sphereObject.transform.localRotation);

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
                }
                _lastFrameCategory = category;
            }

            if (_sphereMaterial != null)
            {
                float framePattern = GetFramePatternCode(category);
                if (Mathf.Abs(framePattern - _lastFramePattern) > 0.01f && _sphereMaterial.HasProperty("_FramePattern"))
                {
                    _sphereMaterial.SetFloat("_FramePattern", framePattern);
                    _lastFramePattern = framePattern;
                }

                float dt = Time.deltaTime;
                float lerpFactor = (!Application.isPlaying || dt <= 0.0001f) ? 1.0f : Mathf.Clamp01(dt * 8.0f);
                _currentPalette = LerpPalette(_currentPalette, _targetPalette, lerpFactor);

                if (_sphereMaterial.HasProperty("_SkyZenithColor"))
                {
                    _sphereMaterial.SetColor("_SkyZenithColor", _currentPalette.SkyZenith);
                    _sphereMaterial.SetColor("_SkyHorizonColor", _currentPalette.SkyHorizon);
                    _sphereMaterial.SetColor("_GroundHorizonColor", _currentPalette.GroundHorizon);
                    _sphereMaterial.SetColor("_GroundNadirColor", _currentPalette.GroundNadir);
                    _sphereMaterial.SetColor("_EquatorColor", _currentPalette.Equator);
                    _sphereMaterial.SetColor("_PitchLadderColor", _currentPalette.PitchLadder);
                    _sphereMaterial.SetColor("_HeadingLineColor", _currentPalette.HeadingLine);
                    _sphereMaterial.SetColor("_RimColor", _currentPalette.Rim);
                    var curTheme = ThemeManager.Instance.CurrentTheme;
                    if (curTheme != null)
                    {
                        _sphereMaterial.SetColor("_LabelColor", curTheme.TextPrimaryColor);
                        _sphereMaterial.SetColor("_LabelOutlineColor", curTheme.TextInverseColor);
                    }
                }

                // GPWS / 近地大下沉率防撞动态斑马纹警示驱动 (Ground Terrain Hazard Pull-Up Alert)
                IFlightTelemetry curTelem = FlightTelemetryContext.Current;
                bool isHazard = false;
                if (curTelem != null && curTelem.HasVessel)
                {
                    UpdateNavballProbeSnapshot(curTelem);
                    // 优先取 GPWS 雷达真高；Mod 未安装或参数不可用时，回退到核心 AGL。
                    double radarAltitude = _cachedGpwsRadarAltitude;
                    if (double.IsNaN(radarAltitude) || double.IsInfinity(radarAltitude) || radarAltitude < 0.0)
                        radarAltitude = curTelem.AltitudeAGL;

                    // GPWS 下沉率用于加强趋势判断，但只在核心 VSI 确认正在下降时采纳，避免不同 Mod 的正负号约定误报。
                    double gpwsSinkRate = _cachedGpwsSinkRate;
                    double descentRate = Math.Max(0.0, -curTelem.VerticalSpeed);
                    if (curTelem.VerticalSpeed < 0.0 && !double.IsNaN(gpwsSinkRate) && !double.IsInfinity(gpwsSinkRate))
                        descentRate = Math.Max(descentRate, Math.Abs(gpwsSinkRate));

                    // Trajectories 的预测撞击倒计时可提前提示高速再入/落地风险，不依赖是否已进入低空。
                    double impactTime = _cachedTrajImpactTime;
                    bool predictedImpact = !double.IsNaN(impactTime) && !double.IsInfinity(impactTime) && impactTime > 0.0 && impactTime < 15.0;
                    bool lowAltitudeDescent = radarAltitude > 0.0 && radarAltitude < 300.0 && curTelem.VerticalSpeed < 0.0 && descentRate > 8.0;
                    isHazard = lowAltitudeDescent || predictedImpact || curTelem.IsTouchdownAlert;
                }
                float targetHazard = isHazard ? 1.0f : 0.0f;
                _currentHazardAlert = Mathf.MoveTowards(_currentHazardAlert, targetHazard, (!Application.isPlaying ? 1.0f : dt * 6.0f));
                if (_sphereMaterial.HasProperty("_GroundHazardAlert"))
                {
                    _sphereMaterial.SetFloat("_GroundHazardAlert", _currentHazardAlert);
                }

                // 近地平精密 2.5° 游标微调刻度 (Vernier Scale Detail)
                float pitchVal = (curTelem != null) ? Mathf.Abs(curTelem.Pitch) : 0f;
                float targetVernier = (pitchVal < 6.0f) ? Mathf.Clamp01((6.0f - pitchVal) / 3.0f) : 0f;
                _currentVernierDetail = Mathf.MoveTowards(_currentVernierDetail, targetVernier, (!Application.isPlaying ? 1.0f : dt * 4.0f));
                if (_sphereMaterial.HasProperty("_VernierScaleDetail"))
                {
                    _sphereMaterial.SetFloat("_VernierScaleDetail", _currentVernierDetail);
                }
            }

            // 3. 航向读数与参考系模式更新 (增加脏标记比对，杜绝每帧触发 UGUI 字体几何顶点网格重建)
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

            // 在 Principia/官方 LateUpdate 彻底执行完毕后，执行最终高保真姿态与标线同步
            SyncAttitudeAndVisuals();
            SyncMarkers();
            UpdateReticleDynamics();
            UpdateProceduralDetailScale();

            // 强制锁定离屏相机 FPS 跟随游戏每一帧满频同步渲染，彻底杜绝帧率不一致导致的标线与球体相对漂移
            if (_ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                _ballCamera.Render();
            }
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

                if (hasDir && isVisible && dir.z > -0.15f)
                {
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                    img.rectTransform.localScale = Vector3.one * GetMarkerPulseScale(key);

                    // 正交平面投影: (x, y) * 半径，每一帧直接贴合目标坐标，0 滞后、0 阈值量化步进
                    img.rectTransform.anchoredPosition = new Vector2(dir.x, dir.y) * _visualRadius;

                    // 接近地平线边缘时平滑渐隐淡出，前向半球始终满不透明度保持高可见度
                    float alpha = Mathf.Clamp01((dir.z + 0.15f) / 0.25f);
                    if (Mathf.Abs(img.color.a - alpha) > 0.02f)
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = alpha;
                        img.color = c;
                    }
                }
                else if (hasDir && dir.z <= -0.15f)
                {
                    // 背面标记投影到球缘，保留方位感；正后方没有可靠的左右方向，因此不强行猜测。
                    Vector2 bearing = new Vector2(dir.x, dir.y);
                    if (bearing.sqrMagnitude > 0.025f)
                    {
                        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                        bearing.Normalize();
                        img.rectTransform.anchoredPosition = bearing * (_visualRadius * 0.82f);
                        img.rectTransform.localScale = Vector3.one * (0.62f * GetMarkerPulseScale(key));
                        Color ghost = WidgetStyleManager.NeutralOpaque;
                        ghost.a = Mathf.Lerp(0.40f, 0.18f, Mathf.Clamp01(-dir.z));
                        if (img.color != ghost) img.color = ghost;
                    }
                    else if (img.gameObject.activeSelf)
                    {
                        img.gameObject.SetActive(false);
                    }
                }
                else
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
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

        private float GetMarkerPulseScale(string key)
        {
            if (!Application.isPlaying) return 1f;
            float amplitude;
            float frequency;
            float phase;
            switch (key)
            {
                case "maneuver": amplitude = 0.22f; frequency = 1.55f; phase = 0f; break;
                case "target": amplitude = 0.17f; frequency = 1.05f; phase = 0.8f; break;
                case "antitarget": amplitude = 0.17f; frequency = 1.05f; phase = 2.1f; break;
                case "prograde": amplitude = 0.12f; frequency = 0.75f; phase = 1.4f; break;
                case "retrograde": amplitude = 0.12f; frequency = 0.75f; phase = 2.7f; break;
                default: return 1f;
            }
            return 1f + amplitude * Mathf.Sin((Time.unscaledTime * frequency + phase) * Mathf.PI * 2f);
        }

        private void UpdateReticleDynamics()
        {
            if (_reticleImage == null) return;
            float movement = _attitudeTrendStrength;
            float phase = Application.isPlaying
                ? Time.unscaledTime * (1.6f + movement * 3.8f) * Mathf.PI * 2f
                : 0f;
            float wave = Application.isPlaying ? Mathf.Sin(phase) : 0f;
            float scale = 1f + wave * Mathf.Lerp(0.01f, 0.04f, movement);
            if (Mathf.Abs(_reticleImage.rectTransform.localScale.x - scale) > 0.002f)
                _reticleImage.rectTransform.localScale = Vector3.one * scale;

            Color reticleColor = _reticleImage.color;
            reticleColor.a = Mathf.Lerp(0.52f, 0.42f, movement) + wave * Mathf.Lerp(0.02f, 0.055f, movement);
            if (_reticleImage.color != reticleColor) _reticleImage.color = reticleColor;
        }

        private void UpdateMarkerAvoidanceMasks()
        {
            if (_sphereMaterial == null || !_sphereMaterial.HasProperty(MarkerAvoidancePropertyIds[0])) return;
            float halfDiameter = Mathf.Max(1f, _ballDiameter * 0.5f);
            for (int i = 0; i < MarkerAvoidanceKeys.Length; i++)
            {
                Vector4 avoidance = Vector4.zero;
                if (_markerImages.TryGetValue(MarkerAvoidanceKeys[i], out Image marker) && marker != null && marker.gameObject.activeSelf)
                {
                    Vector2 point = marker.rectTransform.anchoredPosition / halfDiameter;
                    float markerScale = Mathf.Abs(marker.rectTransform.localScale.x);
                    float radius = Mathf.Clamp(0.23f * markerScale, 0.08f, 0.28f);
                    avoidance = new Vector4(point.x, point.y, radius, Mathf.Clamp01(marker.color.a));
                }
                _markerAvoidanceValues[i] = avoidance;
                _sphereMaterial.SetVector(MarkerAvoidancePropertyIds[i], avoidance);
            }
        }

        private void UpdateProceduralDetailScale()
        {
            if (_sphereMaterial == null || !_sphereMaterial.HasProperty("_DetailScale") || _displayImage == null) return;

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
                _sphereMaterial.SetFloat("_DetailScale", detailScale);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _paletteInitialized = false;
            _lastFrameCategory = null;
            _lastFramePattern = -1f;

            if (_sphereMaterial != null)
            {
                Shader targetShader = AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
                if (_sphereMaterial.shader != targetShader && targetShader != null)
                {
                    _sphereMaterial.shader = targetShader;
                }

                if (_sphereMaterial.HasProperty("_NumeralUprightMode")) _sphereMaterial.SetFloat("_NumeralUprightMode", 0.0f);
                if (_sphereMaterial.HasProperty("_NumeralRollAngle")) _sphereMaterial.SetFloat("_NumeralRollAngle", 0.0f);
                if (_sphereMaterial.HasProperty("_NumeralTangentComp")) _sphereMaterial.SetFloat("_NumeralTangentComp", 1.0f);

                // 2. 天地与网格色彩统一注入：无论何种 Shader，属性存在即注入，杜绝硬编码与色彩脱节
                bool isModern = (targetShader == AssetLoader.ModernShader);
                Color skyZenith = theme.SkyColor;
                Color skyHrz = isModern ? WidgetStyleManager.Lighten(skyZenith, 0.16f) : (Color)theme.AccentSecondary;

                if (_sphereMaterial.HasProperty("_SkyColor")) _sphereMaterial.SetColor("_SkyColor", skyZenith);
                if (_sphereMaterial.HasProperty("_GroundColor")) _sphereMaterial.SetColor("_GroundColor", theme.GroundColor);
                if (_sphereMaterial.HasProperty("_SkyZenithColor")) _sphereMaterial.SetColor("_SkyZenithColor", skyZenith);
                if (_sphereMaterial.HasProperty("_SkyHorizonColor")) _sphereMaterial.SetColor("_SkyHorizonColor", skyHrz);
                if (_sphereMaterial.HasProperty("_GroundHorizonColor")) _sphereMaterial.SetColor("_GroundHorizonColor", theme.GroundColor);
                if (_sphereMaterial.HasProperty("_GroundNadirColor")) _sphereMaterial.SetColor("_GroundNadirColor", WidgetStyleManager.Darken(theme.GroundColor, 0.4f));

                if (_sphereMaterial.HasProperty("_EquatorColor")) _sphereMaterial.SetColor("_EquatorColor", theme.HorizonLineColor);
                if (_sphereMaterial.HasProperty("_HorizonLineColor")) _sphereMaterial.SetColor("_HorizonLineColor", theme.HorizonLineColor);

                if (_sphereMaterial.HasProperty("_GridColor")) _sphereMaterial.SetColor("_GridColor", theme.GridColor);
                if (_sphereMaterial.HasProperty("_PitchLadderColor")) _sphereMaterial.SetColor("_PitchLadderColor", theme.GridColor);
                if (_sphereMaterial.HasProperty("_HeadingLineColor")) _sphereMaterial.SetColor("_HeadingLineColor", theme.AccentSecondary);

                if (_sphereMaterial.HasProperty("_DotColor")) _sphereMaterial.SetColor("_DotColor", theme.DitherDotColor);
                if (_sphereMaterial.HasProperty("_DotDensity")) _sphereMaterial.SetFloat("_DotDensity", theme.DotDensity > 0 ? theme.DotDensity : 38f);
                if (_sphereMaterial.HasProperty("_DotMinRadius")) _sphereMaterial.SetFloat("_DotMinRadius", theme.DotMinRadius > 0 ? theme.DotMinRadius : 0.04f);
                if (_sphereMaterial.HasProperty("_DotMaxRadius")) _sphereMaterial.SetFloat("_DotMaxRadius", theme.DotMaxRadius > 0 ? theme.DotMaxRadius : 0.46f);

                if (_sphereMaterial.HasProperty("_RimColor")) _sphereMaterial.SetColor("_RimColor", theme.RimGlowColor);
                if (_sphereMaterial.HasProperty("_AtmosphereGlowColor")) _sphereMaterial.SetColor("_AtmosphereGlowColor", theme.RimGlowColor);
                if (_sphereMaterial.HasProperty("_EmissionIntensity")) _sphereMaterial.SetFloat("_EmissionIntensity", 1.25f);

                if (_sphereMaterial.HasProperty("_LabelColor")) _sphereMaterial.SetColor("_LabelColor", theme.TextPrimaryColor);
                if (_sphereMaterial.HasProperty("_LabelOutlineColor")) _sphereMaterial.SetColor("_LabelOutlineColor", theme.TextInverseColor);
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
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            _renderTexture = new RenderTexture(newRes, newRes, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                anisoLevel = 4,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();
            if (_ballCamera != null) _ballCamera.targetTexture = _renderTexture;
            if (_displayImage != null) _displayImage.texture = _renderTexture;
        }

        protected override void HandleRenderSettingChanged()
        {
            if (WidgetRenderManager.Instance == null) return;
            int optimalRes = WidgetRenderManager.Instance.CalculateOptimalResolution(
                new Vector2(150f, 150f),
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
            _markerImages.Clear();
            if (_sphereObject != null)
            {
                Destroy(_sphereObject);
            }
            base.OnDestroy();
        }
    }
}
