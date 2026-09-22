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

        private readonly Dictionary<string, Image> _markerImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private float _visualRadius;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            // 1. 自适应读取 KSP 原生 UI_SCALE_NAVBALL 与尺寸比例 (基准直径优化为 ~150px，完全贴合原生)
            float uiScale = UIFactory.GetKspNavballUiScale();
            float ballDiameter = 150f * uiScale * CurrentDpiScale;
            RectTransform.sizeDelta = new Vector2(ballDiameter, ballDiameter);

            _visualRadius = ballDiameter * 0.5f;

            // 2. 动态自适应高分辨率 RenderTexture (开启 4x 硬件 MSAA 抗锯齿，消除几何锯齿)
            int rtResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(ballDiameter * 1.5f)), 512, 2048);
            _renderTexture = new RenderTexture(rtResolution, rtResolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
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

            // 4. 3D 球体 (直接共享官方 StockNavBall 网格模型与 UV 拓扑，杜绝贴图畸变)
            _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereObject.name = "Navball_3D_Sphere";
            _sphereObject.transform.SetParent(transform, false);
            _sphereObject.transform.localPosition = Vector3.zero;
            _sphereObject.transform.localScale = Vector3.one * 2.0f;
            _sphereObject.layer = 31;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // 优先共享官方专属 NavBall Mesh，保障 UV 展开与官方贴图 100% 绝对契合
            if (StockNavBallHook.HasStockNavBall)
            {
                MeshFilter stockMf = StockNavBallHook.StockInstance.navBall.GetComponent<MeshFilter>();
                if (stockMf != null && stockMf.sharedMesh != null)
                {
                    _sphereObject.GetComponent<MeshFilter>().sharedMesh = stockMf.sharedMesh;
                }
            }
            UpdateSphereMeshScale();

            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = AssetLoader.EnhancedShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);

            // 读取官方/Principia/TextureReplacer 正在使用的贴图与 UV 缩放偏置
            Texture stockTex = StockNavBallHook.GetTexture();
            if (stockTex != null)
            {
                _sphereMaterial.SetTexture("_MainTex", stockTex);
            }
            if (StockNavBallHook.HasStockNavBall)
            {
                Renderer stockR = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>();
                if (stockR != null && stockR.sharedMaterial != null)
                {
                    _sphereMaterial.mainTextureScale = stockR.sharedMaterial.mainTextureScale;
                    _sphereMaterial.mainTextureOffset = stockR.sharedMaterial.mainTextureOffset;
                }
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

            // 7. 瞄准标与航向标卡
            CreateCrosshair(transform, CurrentDpiScale, theme);
            CreateHeadingBox(transform, CurrentDpiScale, theme);

            ApplyTheme(theme);
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

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            // 0. 官方专属网格与材质属性动态挂钩检查 (确保晚期加载时无缝衔接)
            if (StockNavBallHook.HasStockNavBall && _sphereObject != null)
            {
                MeshFilter stockMf = StockNavBallHook.StockInstance.navBall.GetComponent<MeshFilter>();
                MeshFilter ourMf = _sphereObject.GetComponent<MeshFilter>();
                if (stockMf != null && stockMf.sharedMesh != null && ourMf.sharedMesh != stockMf.sharedMesh)
                {
                    ourMf.sharedMesh = stockMf.sharedMesh;
                    UpdateSphereMeshScale();
                }
                else if (_sphereObject.transform.localScale == Vector3.one * 2.0f && ourMf != null && ourMf.sharedMesh != null)
                {
                    UpdateSphereMeshScale();
                }

                Renderer stockR = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>();
                if (stockR != null && stockR.sharedMaterial != null && _sphereMaterial != null)
                {
                    if (_sphereMaterial.mainTextureScale != stockR.sharedMaterial.mainTextureScale)
                        _sphereMaterial.mainTextureScale = stockR.sharedMaterial.mainTextureScale;
                    if (_sphereMaterial.mainTextureOffset != stockR.sharedMaterial.mainTextureOffset)
                        _sphereMaterial.mainTextureOffset = stockR.sharedMaterial.mainTextureOffset;
                }
            }

            // 1. 姿态旋转：同步官方摄像机视口空间下的权威旋转
            if (_sphereObject != null)
            {
                Camera stockCam = StockNavBallHook.GetNavBallCamera();
                Quaternion camRot = (stockCam != null) ? stockCam.transform.rotation : Quaternion.identity;
                _sphereObject.transform.rotation = Quaternion.Inverse(camRot) * StockNavBallHook.GetRotation();
            }

            // 2. 贴图同步：实时同步 Principia 多参考系 (Barycentric/Inertial/Surface) 与 TextureReplacer
            Texture stockTex = StockNavBallHook.GetTexture();
            if (stockTex != null && _sphereMaterial != null && _sphereMaterial.GetTexture("_MainTex") != stockTex)
            {
                _sphereMaterial.SetTexture("_MainTex", stockTex);
            }

            // 3. 航向读数与参考系模式更新
            if (_headingText != null)
            {
                _headingText.text = StockNavBallHook.GetHeadingText();
            }
            if (_frameText != null)
            {
                _frameText.text = StockNavBallHook.GetReferenceFrameName();
            }

            // 4. 2D 亚像素无畸变平滑投影矢量标线同步
            SyncMarkers();
        }

        private void SyncMarkers()
        {
            foreach (var kvp in _markerImages)
            {
                string key = kvp.Key;
                Image img = kvp.Value;
                if (img == null) continue;

                if (StockNavBallHook.GetMarkerDirection(key, out Vector3 dir, out bool isVisible))
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
            if (_sphereMaterial != null)
            {
                bool isProcedural = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
                Shader targetShader = isProcedural 
                    ? (AssetLoader.ProceduralShader ?? AssetLoader.EnhancedShader) 
                    : (AssetLoader.EnhancedShader ?? AssetLoader.ModernShader);

                if (_sphereMaterial.shader != targetShader)
                {
                    _sphereMaterial.shader = targetShader;
                }

                if (!isProcedural)
                {
                    // 1. 贴图模式：使用原版/Principia素材，色彩对比与高光适度调校，杜绝过曝
                    Texture stockTex = StockNavBallHook.GetTexture();
                    if (stockTex != null)
                    {
                        _sphereMaterial.SetTexture("_MainTex", stockTex);
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
                    _sphereMaterial.SetColor("_SkyZenithColor", new Color(0.04f, 0.18f, 0.38f, 1.0f));
                    _sphereMaterial.SetColor("_SkyHorizonColor", theme.AccentSecondary);
                    _sphereMaterial.SetColor("_GroundHorizonColor", new Color(0.24f, 0.18f, 0.15f, 1.0f));
                    _sphereMaterial.SetColor("_GroundNadirColor", new Color(0.08f, 0.07f, 0.06f, 1.0f));
                    _sphereMaterial.SetColor("_EquatorColor", Color.white);
                    _sphereMaterial.SetFloat("_EquatorWidth", 0.005f);
                    _sphereMaterial.SetColor("_PitchLadderColor", Color.white);
                    _sphereMaterial.SetFloat("_PitchLadderWidth", 0.004f);
                    _sphereMaterial.SetColor("_HeadingLineColor", theme.AccentSecondary);
                    _sphereMaterial.SetColor("_RimColor", theme.RimGlowColor);
                    _sphereMaterial.SetFloat("_RimPower", 3.2f);
                    _sphereMaterial.SetFloat("_RimIntensity", 0.35f);
                    _sphereMaterial.SetFloat("_LimbPower", 1.35f);
                    _sphereMaterial.SetFloat("_LimbIntensity", 0.30f);
                    _sphereMaterial.SetFloat("_SpecIntensity", 0.16f);
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
        }

        private void OnDestroy()
        {
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
