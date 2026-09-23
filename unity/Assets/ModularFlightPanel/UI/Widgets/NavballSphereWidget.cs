using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

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
    public class NavballSphereWidget : BaseFlightWidget
    {
        private RenderTexture _renderTexture;
        private Camera _ballCamera;
        private GameObject _sphereObject;
        private Material _sphereMaterial;
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
        private float _visualRadius;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            // 1. 自适应读取 KSP 原生 UI_SCALE_NAVBALL 与尺寸比例 (基准直径优化为 ~150px，完全贴合原生)
            float uiScale = UIFactory.GetKspNavballUiScale();
            float ballDiameter = 150f * uiScale * CurrentDpiScale;
            float shellWidth = ballDiameter + 92f * CurrentDpiScale;
            float shellHeight = ballDiameter + 116f * CurrentDpiScale;

            bool showShell = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0;
            bool showHeadingBox = config != null && !string.IsNullOrEmpty(config.CustomTemplate) && config.CustomTemplate.IndexOf("heading_box", StringComparison.OrdinalIgnoreCase) >= 0;

            RectTransform.sizeDelta = showShell ? new Vector2(shellWidth, shellHeight) : new Vector2(ballDiameter, ballDiameter);
            _visualRadius = ballDiameter * 0.5f;

            // 0. 航电外壳 (当处于模块化 HUD 时默认隐藏矩形外壳，保持纯圆仪表面貌)
            if (showShell)
            {
                CreateNavballShell(shellWidth, shellHeight, CurrentDpiScale, theme);
            }

            // 2. 动态自适应超高分辨率 RenderTexture (开启 8x 硬件 MSAA 抗锯齿与三线性滤波，消除几何与贴图锯齿)
            int rtResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(ballDiameter * 3.0f)), 1024, 2048);
            _renderTexture = new RenderTexture(rtResolution, rtResolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear
            };
            _renderTexture.Create();

            // 3. 独立离屏摄像机 (正交投影视口 1.0f 完美贴合单位球，零拉伸畸变)
            GameObject camObj = new GameObject("Navball_Offscreen_Cam", typeof(Camera));
            camObj.transform.SetParent(transform, false);
            camObj.transform.localPosition = new Vector3(0f, 0f, -2.5f);

            _ballCamera = camObj.GetComponent<Camera>();
            _ballCamera.clearFlags = CameraClearFlags.SolidColor;
            _ballCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
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
            _sphereObject.transform.localScale = Vector3.one * 2.0f;
            _sphereObject.layer = 31;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            // 优先共享官方专属 NavBall Mesh，保障 UV 展开与官方贴图 100% 绝对契合
            var hook = NavBallHookService.Provider;
            if (hook != null && hook.HasStockNavBall)
            {
                if (hook.StockMesh != null)
                {
                    _sphereObject.GetComponent<MeshFilter>().sharedMesh = hook.StockMesh;
                }
            }
            UpdateSphereMeshScale();

            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = AssetLoader.EnhancedShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);

            // 读取官方/Principia/TextureReplacer 正在使用的贴图与 UV 缩放偏置
            Texture stockTex = hook?.BallTexture;
            if (stockTex != null)
            {
                _sphereMaterial.SetTexture("_MainTex", stockTex);
            }
            if (hook != null && hook.HasStockNavBall)
            {
                _sphereMaterial.mainTextureScale = hook.TextureScale;
                _sphereMaterial.mainTextureOffset = hook.TextureOffset;
            }
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
            Color border = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            bezelOutline.effectColor = new Color(border.r, border.g, border.b, 0.45f);
            bezelOutline.effectDistance = new Vector2(1.2f * CurrentDpiScale, 1.2f * CurrentDpiScale);

            if (showHeadingBox)
            {
                CreateHeadingBox(transform, CurrentDpiScale, theme);
            }

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
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            UIFactory.CreatePanel(_shellRoot.transform, "Shell_Top_Div",
                new Vector2(shellWidth - 20f * dpiScale, 1f * dpiScale),
                new Vector2(0f, shellHeight * 0.5f - 22f * dpiScale),
                new Color(borderCol.r, borderCol.g, borderCol.b, 0.25f));

            // 底部状态微标栏
            GameObject bottomBar = UIFactory.CreatePanel(_shellRoot.transform, "Navball_Shell_BottomBar",
                new Vector2(shellWidth - 20f * dpiScale, 16f * dpiScale),
                new Vector2(0f, -shellHeight * 0.5f + 14f * dpiScale),
                new Color(0.06f, 0.09f, 0.14f, 0.45f));
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
            if (_sphereObject == null) return;
            MeshFilter meshFilter = _sphereObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;

            Bounds bounds = meshFilter.sharedMesh.bounds;
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            if (maxExtent > 0.0001f)
            {
                // Keep the visual radius at one world unit for both stock and replacement navball meshes.
                _sphereObject.transform.localScale = Vector3.one * (1f / maxExtent);
            }
        }

        private void CreateMarkerOverlayLayer(Transform parent, float dpiScale)
        {
            GameObject markerLayerObj = new GameObject("Markers_Layer", typeof(RectTransform));
            markerLayerObj.transform.SetParent(parent, false);
            RectTransform mlRt = markerLayerObj.GetComponent<RectTransform>();
            mlRt.sizeDelta = new Vector2(_visualRadius * 2f, _visualRadius * 2f);
            mlRt.anchoredPosition = Vector2.zero;

            string[] markerKeys = new string[]
            {
                "prograde", "retrograde", "normal", "antinormal",
                "radialin", "radialout", "target", "maneuver"
            };

            float markerSize = 25f * dpiScale;

            for (int i = 0; i < markerKeys.Length; i++)
            {
                string key = markerKeys[i];
                GameObject mObj = new GameObject("Marker_" + key, typeof(RectTransform), typeof(Image));
                mObj.transform.SetParent(markerLayerObj.transform, false);

                RectTransform mRt = mObj.GetComponent<RectTransform>();
                mRt.sizeDelta = new Vector2(markerSize, markerSize);
                mRt.anchoredPosition = Vector2.zero;

                Image img = mObj.GetComponent<Image>();
                img.sprite = NavballMarkerFactory.GetMarkerSprite(key);
                img.raycastTarget = false;
                mObj.SetActive(false);

                _markerImages[key] = img;
            }
        }

        private void CreateCrosshair(Transform parent, float dpiScale, ThemeConfig theme)
        {
            _crosshair = new GameObject("Crosshair_Center", typeof(RectTransform));
            _crosshair.transform.SetParent(parent, false);

            float wingW = 28f * dpiScale;
            float wingH = 3.2f * dpiScale;
            float offset = 22f * dpiScale;

            UIFactory.CreatePanel(_crosshair.transform, "H_Wing_L", new Vector2(wingW, wingH), new Vector2(-offset, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_crosshair.transform, "H_Wing_R", new Vector2(wingW, wingH), new Vector2(offset, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_crosshair.transform, "V_Center", new Vector2(wingH, 16f * dpiScale), Vector2.zero, theme.WarningColor);
            UIFactory.CreatePanel(_crosshair.transform, "Center_Dot", new Vector2(5.5f * dpiScale, 5.5f * dpiScale), Vector2.zero, theme.WarningColor);
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

        private void OnEnable()
        {
            Camera.onPreCull += OnCameraPreCull;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
        }

        private void OnCameraPreCull(Camera cam)
        {
            if (cam == _ballCamera)
            {
                SyncAttitudeAndVisuals();
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            // 0. 官方专属网格与材质属性动态挂钩检查 (确保晚期加载时无缝衔接)
            if (hasHook && _sphereObject != null)
            {
                MeshFilter ourMf = _sphereObject.GetComponent<MeshFilter>();
                if (hook.StockMesh != null && ourMf.sharedMesh != hook.StockMesh)
                {
                    ourMf.sharedMesh = hook.StockMesh;
                    UpdateSphereMeshScale();
                }
                else if (_sphereObject.transform.localScale == Vector3.one * 2.0f && ourMf != null && ourMf.sharedMesh != null)
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

            // 若已启用现代航向弧带 core.heading_arc，则自动隐藏传统方盒避免遮挡重叠
            bool hasHeadingArc = WidgetLayoutManager.Instance.GetConfig("core.heading_arc")?.IsEnabled ?? false;
            if (_headingBox != null && _headingBox.activeSelf == hasHeadingArc)
            {
                _headingBox.SetActive(!hasHeadingArc);
            }

            SyncAttitudeAndVisuals();
            SyncMarkers();
        }

        private struct FramePalette
        {
            public Color SkyZenith;
            public Color SkyHorizon;
            public Color GroundHorizon;
            public Color GroundNadir;
            public Color Equator;
            public Color PitchLadder;
            public Color HeadingLine;
            public Color Rim;
        }

        private string _lastFrameCategory = "";
        private FramePalette _currentPalette;
        private FramePalette _targetPalette;
        private bool _paletteInitialized = false;

        private FramePalette GetPaletteForCategory(string category, ThemeConfig theme)
        {
            switch (category?.ToUpperInvariant())
            {
                case "INERTIAL":
                    return new FramePalette
                    {
                        SkyZenith = new Color(0.12f, 0.14f, 0.18f, 1f),
                        SkyHorizon = new Color(0.32f, 0.36f, 0.42f, 1f),
                        GroundHorizon = new Color(0.06f, 0.07f, 0.09f, 1f),
                        GroundNadir = new Color(0.02f, 0.02f, 0.03f, 1f),
                        Equator = new Color(0.68f, 0.84f, 1.0f, 1f),
                        PitchLadder = new Color(0.80f, 0.86f, 0.94f, 0.65f),
                        HeadingLine = new Color(0.50f, 0.65f, 0.85f, 0.40f),
                        Rim = new Color(0.55f, 0.72f, 0.95f, 1f)
                    };
                case "BARYCENTRIC":
                    return new FramePalette
                    {
                        SkyZenith = new Color(0.20f, 0.05f, 0.28f, 1f),
                        SkyHorizon = new Color(0.56f, 0.18f, 0.65f, 1f),
                        GroundHorizon = new Color(0.22f, 0.06f, 0.26f, 1f),
                        GroundNadir = new Color(0.08f, 0.02f, 0.10f, 1f),
                        Equator = new Color(0.94f, 0.86f, 1.0f, 1f),
                        PitchLadder = new Color(0.90f, 0.78f, 0.98f, 0.70f),
                        HeadingLine = new Color(0.75f, 0.40f, 0.95f, 0.45f),
                        Rim = new Color(0.88f, 0.42f, 1.0f, 1f)
                    };
                case "TARGET":
                    return new FramePalette
                    {
                        SkyZenith = new Color(0.28f, 0.06f, 0.08f, 1f),
                        SkyHorizon = new Color(0.68f, 0.22f, 0.26f, 1f),
                        GroundHorizon = new Color(0.22f, 0.05f, 0.07f, 1f),
                        GroundNadir = new Color(0.08f, 0.01f, 0.02f, 1f),
                        Equator = new Color(0.98f, 0.78f, 0.35f, 1f),
                        PitchLadder = new Color(0.98f, 0.80f, 0.82f, 0.70f),
                        HeadingLine = new Color(0.95f, 0.35f, 0.40f, 0.45f),
                        Rim = new Color(1.0f, 0.30f, 0.36f, 1f)
                    };
                case "BODY_DIRECTION":
                    return new FramePalette
                    {
                        SkyZenith = new Color(0.26f, 0.13f, 0.04f, 1f),
                        SkyHorizon = new Color(0.68f, 0.44f, 0.16f, 1f),
                        GroundHorizon = new Color(0.28f, 0.15f, 0.05f, 1f),
                        GroundNadir = new Color(0.10f, 0.05f, 0.01f, 1f),
                        Equator = new Color(1.0f, 0.92f, 0.65f, 1f),
                        PitchLadder = new Color(0.98f, 0.90f, 0.70f, 0.70f),
                        HeadingLine = new Color(0.95f, 0.65f, 0.25f, 0.45f),
                        Rim = new Color(1.0f, 0.72f, 0.25f, 1f)
                    };
                case "SURFACE":
                default:
                    Color skyZ = theme != null ? (Color)theme.SkyColor : new Color(0.04f, 0.16f, 0.38f, 1f);
                    Color skyH = theme != null ? (Color)theme.AccentSecondary : new Color(0.09f, 0.48f, 0.80f, 1f);
                    Color gndH = theme != null ? (Color)theme.GroundColor : new Color(0.36f, 0.22f, 0.10f, 1f);
                    Color gndN = Color.Lerp(gndH, Color.black, 0.55f);
                    Color eq = theme != null ? (Color)theme.HorizonLineColor : Color.white;
                    Color pitch = theme != null ? (Color)theme.GridColor : new Color(0.85f, 0.94f, 1.0f, 0.70f);
                    Color hdg = theme != null ? (Color)theme.AccentSecondary : new Color(0.20f, 0.70f, 0.95f, 0.45f);
                    Color rim = theme != null ? (Color)theme.RimGlowColor : new Color(0.20f, 0.78f, 1.0f, 1f);
                    return new FramePalette
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

        private static FramePalette LerpPalette(FramePalette from, FramePalette to, float t)
        {
            return new FramePalette
            {
                SkyZenith = Color.Lerp(from.SkyZenith, to.SkyZenith, t),
                SkyHorizon = Color.Lerp(from.SkyHorizon, to.SkyHorizon, t),
                GroundHorizon = Color.Lerp(from.GroundHorizon, to.GroundHorizon, t),
                GroundNadir = Color.Lerp(from.GroundNadir, to.GroundNadir, t),
                Equator = Color.Lerp(from.Equator, to.Equator, t),
                PitchLadder = Color.Lerp(from.PitchLadder, to.PitchLadder, t),
                HeadingLine = Color.Lerp(from.HeadingLine, to.HeadingLine, t),
                Rim = Color.Lerp(from.Rim, to.Rim, t)
            };
        }

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            // 1. 姿态旋转：使用 localRotation 配合官方摄像机视口变换，杜绝任何外部画布/物体倾斜畸变
            if (_sphereObject != null)
            {
                if (hasHook)
                {
                    Quaternion camRot = hook.CameraRotation;
                    _sphereObject.transform.localRotation = Quaternion.Inverse(camRot) * hook.BallRotation;
                }
                else
                {
                    IFlightTelemetry telem = FlightTelemetryContext.Current;
                    _sphereObject.transform.localRotation = (telem != null) ? telem.AttitudeRotation : Quaternion.identity;
                }
            }

            // 2. 贴图与程序化多参考系自适应变色 (Principia / Stock 多参考系高保真映射)
            bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
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

            if (isProcedural && _sphereMaterial != null)
            {
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
                }
            }
            else if (!isProcedural && hasHook && hook.BallTexture != null && _sphereMaterial != null)
            {
                if (_sphereMaterial.HasProperty("_MainTex") && _sphereMaterial.GetTexture("_MainTex") != hook.BallTexture)
                {
                    _sphereMaterial.SetTexture("_MainTex", hook.BallTexture);
                }
                if (_sphereMaterial.HasProperty("_RimColor"))
                {
                    _sphereMaterial.SetColor("_RimColor", _currentPalette.Rim);
                }
                if (_sphereMaterial.HasProperty("_AtmosphereGlowColor"))
                {
                    _sphereMaterial.SetColor("_AtmosphereGlowColor", _currentPalette.Rim);
                }
            }

            // 3. 航向读数与参考系模式更新
            if (_headingText != null)
            {
                _headingText.text = hasHook ? hook.HeadingText : $"HDG {Mathf.RoundToInt(FlightTelemetryContext.Current?.Heading ?? 0f) % 360:D3}°";
            }
            if (_frameText != null)
            {
                _frameText.text = hasHook ? hook.FrameName : category;
                _frameText.color = _currentPalette.Rim;
            }
            if (_shellStatus != null)
            {
                _shellStatus.color = _currentPalette.Rim;
            }
        }

        protected virtual void LateUpdate()
        {
            // 在 Principia/官方 LateUpdate 彻底执行完毕后，执行最终高保真姿态与标线同步
            SyncAttitudeAndVisuals();
            SyncMarkers();

            // 显式驱动离屏相机渲染至 RenderTexture，确保采样到最新的 Principia 多参考系姿态
            if (_ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                _ballCamera.Render();
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

                if (hook != null && hook.GetMarkerDirection(key, out Vector3 dir, out bool isVisible))
                {
                    // 在可见前半球 (dir.z > -0.05f)
                    if (isVisible && dir.z > -0.05f)
                    {
                        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                        // 正交平面投影: (x, y) * 半径
                        img.rectTransform.anchoredPosition = new Vector2(dir.x, dir.y) * _visualRadius;

                        // 接近地平线边缘时平滑渐隐淡出
                        float alpha = Mathf.Clamp01((dir.z + 0.05f) / 0.20f);
                        Color c = img.color;
                        c.a = alpha;
                        img.color = c;
                    }
                    else
                    {
                        if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                    }
                }
                else
                {
                    if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _paletteInitialized = false;
            if (_sphereMaterial != null)
            {
                bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
                Shader targetShader = null;

                if (isProcedural)
                {
                    // 程序化模式：纯数学完美超清矢量解算 (彻底脱离贴图，消除低清模糊与锯齿)
                    targetShader = AssetLoader.ProceduralShader ?? AssetLoader.ModernShader;
                }
                else
                {
                    // 贴图模式：真实渲染官方原版/Principia/TextureReplacer 的姿态球贴图素材
                    if (theme != null && !string.IsNullOrEmpty(theme.ShaderName))
                    {
                        if (theme.ShaderName.EndsWith("NavballHalftone")) targetShader = AssetLoader.HalftoneShader;
                        else if (theme.ShaderName.EndsWith("NavballEnhanced")) targetShader = AssetLoader.EnhancedShader;
                    }
                    if (targetShader == null)
                    {
                        targetShader = AssetLoader.EnhancedShader ?? AssetLoader.ModernShader;
                    }
                }

                if (targetShader != null && _sphereMaterial.shader != targetShader)
                {
                    _sphereMaterial.shader = targetShader;
                }

                // 贴图与着色参数设置 (仅在贴图模式下挂载原版/Principia 底图，程序化模式下严禁混入杂质线条)
                if (!isProcedural)
                {
                    Texture stockTex = NavBallHookService.Provider?.BallTexture;
                    if (stockTex != null && _sphereMaterial.HasProperty("_MainTex"))
                    {
                        _sphereMaterial.SetTexture("_MainTex", stockTex);
                    }
                }
                else
                {
                    if (_sphereMaterial.HasProperty("_MainTex"))
                    {
                        _sphereMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    }
                }

                if (!isProcedural)
                {
                    // 贴图模式着色增强
                    if (_sphereMaterial.HasProperty("_SkyColor"))
                    {
                        _sphereMaterial.SetColor("_SkyColor", theme.SkyColor);
                        _sphereMaterial.SetColor("_GroundColor", theme.GroundColor);
                        _sphereMaterial.SetColor("_EquatorColor", theme.HorizonLineColor);
                        _sphereMaterial.SetColor("_GridColor", theme.GridColor);
                        _sphereMaterial.SetColor("_DotColor", theme.DitherDotColor);
                    }
                    if (_sphereMaterial.HasProperty("_SkyZenithColor"))
                    {
                        _sphereMaterial.SetColor("_SkyZenithColor", theme.SkyColor);
                        _sphereMaterial.SetColor("_SkyHorizonColor", theme.AccentSecondary);
                        _sphereMaterial.SetColor("_GroundHorizonColor", theme.GroundColor);
                        _sphereMaterial.SetColor("_GroundNadirColor", Color.Lerp(theme.GroundColor, Color.black, 0.4f));
                        _sphereMaterial.SetColor("_HorizonLineColor", theme.HorizonLineColor);
                        _sphereMaterial.SetColor("_PitchLadderColor", theme.GridColor);
                        _sphereMaterial.SetColor("_AtmosphereGlowColor", theme.RimGlowColor);
                        if (_sphereMaterial.HasProperty("_TextureBlend"))
                        {
                            _sphereMaterial.SetFloat("_TextureBlend", 1.0f); // 贴图模式：完整融合原版贴图
                        }
                    }

                    _sphereMaterial.SetColor("_RimColor", theme.RimGlowColor);
                    _sphereMaterial.SetFloat("_RimPower", 3.2f);
                    _sphereMaterial.SetFloat("_RimIntensity", 0.28f);
                    _sphereMaterial.SetFloat("_LimbPower", 1.4f);
                    _sphereMaterial.SetFloat("_LimbIntensity", 0.32f);
                    _sphereMaterial.SetFloat("_Contrast", 1.02f);
                    _sphereMaterial.SetFloat("_Brightness", 1.0f);
                    _sphereMaterial.SetFloat("_Saturation", 1.05f);
                    _sphereMaterial.SetFloat("_SpecIntensity", 0.16f);
                }
                else
                {
                    // 2. 程序化矢量模式：纯数学完美超清解算
                    _sphereMaterial.SetColor("_SkyZenithColor", theme.SkyColor);
                    _sphereMaterial.SetColor("_SkyHorizonColor", theme.AccentSecondary);
                    _sphereMaterial.SetColor("_GroundHorizonColor", theme.GroundColor);
                    _sphereMaterial.SetColor("_GroundNadirColor", Color.Lerp(theme.GroundColor, Color.black, 0.4f));
                    _sphereMaterial.SetColor("_EquatorColor", theme.HorizonLineColor);
                    _sphereMaterial.SetFloat("_EquatorWidth", 0.005f);
                    _sphereMaterial.SetColor("_PitchLadderColor", theme.GridColor);
                    _sphereMaterial.SetFloat("_PitchLadderWidth", 0.004f);
                    _sphereMaterial.SetColor("_HeadingLineColor", theme.AccentSecondary);
                    _sphereMaterial.SetColor("_RimColor", theme.RimGlowColor);
                    _sphereMaterial.SetFloat("_RimPower", 3.2f);
                    _sphereMaterial.SetFloat("_RimIntensity", 0.35f);
                    _sphereMaterial.SetFloat("_LimbPower", 1.35f);
                    _sphereMaterial.SetFloat("_LimbIntensity", 0.30f);
                    _sphereMaterial.SetFloat("_SpecIntensity", 0.16f);
                    if (_sphereMaterial.HasProperty("_TextureBlend"))
                    {
                        _sphereMaterial.SetFloat("_TextureBlend", 0.0f); // 程序化模式：纯数学解算
                    }
                }
            }

            if (_headingText != null)
            {
                _headingText.color = theme.TextPrimaryColor;
            }
            if (_frameText != null)
            {
                _frameText.color = theme.AccentSecondary;
            }
            if (_shellImage != null)
            {
                _shellImage.color = theme.FrameBgColor;
            }
            if (_shellOutline != null)
            {
                _shellOutline.effectColor = theme.FrameBorderColor;
            }
            if (_shellTitle != null)
            {
                _shellTitle.color = theme.AccentSecondary;
            }
            if (_shellStatus != null)
            {
                _shellStatus.color = theme.AccentPrimary;
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnCameraPreCull;
            _markerImages.Clear();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
            }
            if (_sphereObject != null)
            {
                Destroy(_sphereObject);
            }
        }
    }
}
