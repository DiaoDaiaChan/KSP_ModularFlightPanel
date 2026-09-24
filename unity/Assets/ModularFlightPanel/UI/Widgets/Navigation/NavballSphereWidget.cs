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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

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
        private Quaternion _lastRenderedAttitude = Quaternion.identity;
        private float _lastCameraRenderTime = -1f;
        private float _lastProfileCheckTime = -1f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
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
            ApplyCanvasIsolation(true);

            if (showShell)
            {
                CreateNavballShell(shellWidth, shellHeight, CurrentDpiScale, theme);
            }

            // 2. 动态自适应刚刚好高效 RenderTexture (依据 WidgetRenderManager 结合物理占用与倍率自适应)
            int rtResolution = 512;
            if (WidgetRenderManager.Instance != null)
            {
                rtResolution = WidgetRenderManager.Instance.CalculateOptimalResolution(
                    new Vector2(ballDiameter, ballDiameter),
                    config != null ? config.Scale : 1.0f,
                    config != null ? config.RenderScale : 1.0f);
            }
            _renderTexture = new RenderTexture(rtResolution, rtResolution, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
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
            _sphereObject.transform.localScale = Vector3.one * 2.0f;
            _sphereObject.layer = 31;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            // 优先共享官方专属 NavBall Mesh，保障 UV 展开与官方贴图 100% 绝对契合 (仅贴图模式下使用)
            bool isProceduralInit = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            var hook = NavBallHookService.Provider;
            if (!isProceduralInit && hook != null && hook.HasStockNavBall && hook.StockMesh != null)
            {
                _sphereObject.GetComponent<MeshFilter>().sharedMesh = hook.StockMesh;
            }
            // 程序化模式使用标准数学单位球 (PrimitiveType.Sphere，北极 +Y, 南极 -Y, 零极点畸变)
            UpdateSphereMeshScale();

            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = isProceduralInit
                ? (AssetLoader.ProceduralShader ?? AssetLoader.ModernShader)
                : (AssetLoader.EnhancedShader ?? AssetLoader.ModernShader);
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
            Color border = theme.FrameBorderColor;
            bezelOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
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
            markerLayerObj.layer = parent.gameObject.layer;
            markerLayerObj.transform.SetParent(parent, false);
            RectTransform mlRt = markerLayerObj.GetComponent<RectTransform>();
            mlRt.sizeDelta = new Vector2(_visualRadius * 2f, _visualRadius * 2f);
            mlRt.anchoredPosition = Vector2.zero;

            string[] markerKeys = new string[]
            {
                "prograde", "retrograde", "normal", "antinormal",
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


        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            // 0. 官方专属网格与材质属性动态挂钩检查 (仅贴图模式下使用)
            bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            if (!isProcedural && hasHook && _sphereObject != null)
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

            // 姿态与矢量标线由 LateUpdate 在 Principia 姿态结算后权威驱动，避免每帧重复计算 10+ 标线方位
        }

        private string _lastFrameCategory = "";
        private NavballFramePalette _currentPalette;
        private NavballFramePalette _targetPalette;
        private bool _paletteInitialized = false;

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

        private void SyncAttitudeAndVisuals()
        {
            var hook = NavBallHookService.Provider;
            bool hasHook = (hook != null && hook.HasStockNavBall);

            // 1. 姿态旋转：使用 localRotation 配合官方摄像机视口变换，杜绝任何外部画布/物体倾斜畸变
            bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
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
                    var curTheme = ThemeManager.Instance.CurrentTheme;
                    if (curTheme != null)
                    {
                        _sphereMaterial.SetColor("_LabelColor", curTheme.TextPrimaryColor);
                        _sphereMaterial.SetColor("_LabelOutlineColor", curTheme.TextInverseColor);
                    }
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

        protected virtual void LateUpdate()
        {
            if (!gameObject.activeInHierarchy) return;

            // 在 Principia/官方 LateUpdate 彻底执行完毕后，执行最终高保真姿态与标线同步
            SyncAttitudeAndVisuals();
            SyncMarkers();

            // 显式驱动离屏相机渲染至 RenderTexture，确保采样到最新的 Principia 多参考系姿态
            if (_ballCamera != null && _renderTexture != null && _renderTexture.IsCreated())
            {
                float unscaledTime = Time.unscaledTime;

                // 1. 全局阶梯节流对齐：严格跟随全局 RefreshProfile (Eco 模式 30Hz / 降频模式 / 自定义 Hz)
                if (!WidgetRenderManager.Instance.ShouldUpdateTier(WidgetRefreshTier.Critical, unscaledTime, ref _lastProfileCheckTime))
                {
                    return;
                }

                // 2. 姿态静止缓存与 10Hz 保底：当飞船处于滑行、停靠或暂停时，避免每帧重复渲染
                Quaternion currentAtt = _sphereObject != null ? _sphereObject.transform.localRotation : Quaternion.identity;
                bool attitudeChanged = Quaternion.Angle(currentAtt, _lastRenderedAttitude) > 0.05f;

                if (attitudeChanged || (unscaledTime - _lastCameraRenderTime) >= 0.1f)
                {
                    _lastRenderedAttitude = currentAtt;
                    _lastCameraRenderTime = unscaledTime;
                    _ballCamera.Render();
                }
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

                if (hasDir && isVisible && dir.z > -0.05f)
                {
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                    // 正交平面投影: (x, y) * 半径，仅当位移超过微小阈值时才修改 RectTransform
                    Vector2 targetPos = new Vector2(dir.x, dir.y) * _visualRadius;
                    if ((img.rectTransform.anchoredPosition - targetPos).sqrMagnitude > 0.04f)
                    {
                        img.rectTransform.anchoredPosition = targetPos;
                    }

                    // 接近地平线边缘时平滑渐隐淡出
                    float alpha = Mathf.Clamp01((dir.z + 0.05f) / 0.20f);
                    if (Mathf.Abs(img.color.a - alpha) > 0.02f)
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
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _paletteInitialized = false;
            _lastFrameCategory = null;

            if (_sphereMaterial != null)
            {
                bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
                Shader targetShader = null;

                if (!string.IsNullOrEmpty(theme.ShaderName))
                {
                    if (theme.ShaderName.EndsWith("NavballHalftone"))
                        targetShader = AssetLoader.HalftoneShader;
                    else if (theme.ShaderName.EndsWith("NavballModern"))
                        targetShader = isProcedural ? (AssetLoader.ProceduralShader ?? AssetLoader.ModernShader) : (AssetLoader.EnhancedShader ?? AssetLoader.ModernShader);
                    else if (theme.ShaderName.EndsWith("NavballEnhanced"))
                        targetShader = AssetLoader.EnhancedShader;
                    else if (theme.ShaderName.EndsWith("NavballProcedural"))
                        targetShader = AssetLoader.ProceduralShader;
                }

                if (targetShader == null)
                {
                    targetShader = isProcedural
                        ? (AssetLoader.ProceduralShader ?? AssetLoader.ModernShader)
                        : (AssetLoader.EnhancedShader ?? AssetLoader.ModernShader);
                }

                if (_sphereMaterial.shader != targetShader && targetShader != null)
                {
                    _sphereMaterial.shader = targetShader;
                }

                // 1. 贴图与融合因子统一 (以 JSON / Theme 设置为准)
                Texture stockTex = NavBallHookService.Provider?.BallTexture;
                if (!isProcedural && stockTex != null)
                {
                    if (_sphereMaterial.HasProperty("_MainTex")) _sphereMaterial.SetTexture("_MainTex", stockTex);
                    if (_sphereMaterial.HasProperty("_TextureBlend")) _sphereMaterial.SetFloat("_TextureBlend", 1.0f);
                }
                else
                {
                    if (_sphereMaterial.HasProperty("_MainTex")) _sphereMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (_sphereMaterial.HasProperty("_TextureBlend")) _sphereMaterial.SetFloat("_TextureBlend", 0.0f);
                }

                // 2. 天地与网格色彩统一注入：无论何种 Shader，属性存在即注入，杜绝硬编码与色彩脱节
                if (_sphereMaterial.HasProperty("_SkyColor")) _sphereMaterial.SetColor("_SkyColor", theme.SkyColor);
                if (_sphereMaterial.HasProperty("_GroundColor")) _sphereMaterial.SetColor("_GroundColor", theme.GroundColor);
                if (_sphereMaterial.HasProperty("_SkyZenithColor")) _sphereMaterial.SetColor("_SkyZenithColor", theme.SkyColor);
                if (_sphereMaterial.HasProperty("_SkyHorizonColor")) _sphereMaterial.SetColor("_SkyHorizonColor", theme.AccentSecondary);
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
        }

        private void HandleResolutionChanged(int newRes)
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

        private void HandleRenderSettingChanged()
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
            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnRenderResolutionChanged -= HandleResolutionChanged;
                WidgetRenderManager.Instance.OnRenderSettingChanged -= HandleRenderSettingChanged;
            }
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
            base.OnDestroy();
        }
    }
}
