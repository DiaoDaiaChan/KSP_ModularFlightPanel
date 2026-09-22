using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 3D 姿态球主组件 (直接 Hook 官方 NavBall 实现，Mod 不做任何多余数学运算)
    /// </summary>
    public class NavballSphereWidget : BaseFlightWidget
    {
        private RenderTexture _renderTexture;
        private Camera _ballCamera;
        private GameObject _sphereObject;
        private Material _sphereMaterial;
        private RawImage _displayImage;

        private Text _headingText;
        private GameObject _headingBox;
        private GameObject _crosshair;

        private readonly System.Collections.Generic.Dictionary<string, GameObject> _markerClones 
            = new System.Collections.Generic.Dictionary<string, GameObject>();

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float ballDiameter = 260f * CurrentDpiScale;
            RectTransform.sizeDelta = new Vector2(ballDiameter, ballDiameter);

            // 1. 动态自适应高分辨率 RenderTexture (单采样避免 DX11 边缘 Alpha 污染)
            int rtResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(ballDiameter * 1.5f)), 512, 2048);
            _renderTexture = new RenderTexture(rtResolution, rtResolution, 24, RenderTextureFormat.ARGB32);
            _renderTexture.antiAliasing = 1;
            _renderTexture.useMipMap = false;
            _renderTexture.autoGenerateMips = false;
            _renderTexture.filterMode = FilterMode.Bilinear;
            _renderTexture.Create();

            // 2. 独立离屏摄像机
            GameObject camObj = new GameObject("Navball_Offscreen_Cam", typeof(Camera));
            camObj.transform.SetParent(transform, false);
            camObj.transform.localPosition = new Vector3(0f, 0f, -2.5f);

            _ballCamera = camObj.GetComponent<Camera>();
            _ballCamera.clearFlags = CameraClearFlags.SolidColor;
            _ballCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _ballCamera.targetTexture = _renderTexture;
            _ballCamera.orthographic = true;
            _ballCamera.orthographicSize = 1.05f;
            _ballCamera.nearClipPlane = 0.1f;
            _ballCamera.farClipPlane = 10f;
            _ballCamera.cullingMask = 1 << 31;

            // 3. 3D 球体 (直接使用原版导航球贴图，并经由高保真增强 Shader 渲染)
            _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereObject.name = "Navball_3D_Sphere";
            _sphereObject.transform.SetParent(transform, false);
            _sphereObject.transform.localPosition = Vector3.zero;
            _sphereObject.transform.localScale = Vector3.one * 2.0f;
            _sphereObject.layer = 31;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            MeshRenderer mr = _sphereObject.GetComponent<MeshRenderer>();
            Shader targetShader = AssetLoader.EnhancedShader ?? AssetLoader.ModernShader;
            _sphereMaterial = new Material(targetShader);

            // 读取官方/Principia/TextureReplacer 正在使用的贴图 (完全保留原版数字、经纬线与刻度)
            Texture stockTex = StockNavBallHook.GetTexture();
            if (stockTex != null)
            {
                _sphereMaterial.SetTexture("_MainTex", stockTex);
            }
            mr.material = _sphereMaterial;

            // 4. RawImage 画布映射
            GameObject rawImgObj = new GameObject("Sphere_RawImage", typeof(RectTransform), typeof(RawImage));
            rawImgObj.transform.SetParent(transform, false);
            RectTransform rawRt = rawImgObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(ballDiameter, ballDiameter);
            rawRt.anchoredPosition = Vector2.zero;

            _displayImage = rawImgObj.GetComponent<RawImage>();
            _displayImage.texture = _renderTexture;

            // 5. 瞄准标与航向标卡
            CreateCrosshair(transform, CurrentDpiScale, theme);
            CreateHeadingBox(transform, CurrentDpiScale, theme);

            ApplyTheme(theme);
        }

        private void CreateCrosshair(Transform parent, float dpiScale, ThemeConfig theme)
        {
            _crosshair = new GameObject("Crosshair_Center", typeof(RectTransform));
            _crosshair.transform.SetParent(parent, false);

            float wingW = 32f * dpiScale;
            float wingH = 3.5f * dpiScale;
            float offset = 26f * dpiScale;

            UIFactory.CreatePanel(_crosshair.transform, "H_Wing_L", new Vector2(wingW, wingH), new Vector2(-offset, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_crosshair.transform, "H_Wing_R", new Vector2(wingW, wingH), new Vector2(offset, 0f), theme.AccentPrimary);
            UIFactory.CreatePanel(_crosshair.transform, "V_Center", new Vector2(wingH, 18f * dpiScale), Vector2.zero, theme.WarningColor);
            UIFactory.CreatePanel(_crosshair.transform, "Center_Dot", new Vector2(6f * dpiScale, 6f * dpiScale), Vector2.zero, theme.WarningColor);
        }

        private void CreateHeadingBox(Transform parent, float dpiScale, ThemeConfig theme)
        {
            Vector2 boxSize = new Vector2(72f * dpiScale, 30f * dpiScale);
            Vector2 anchoredPos = new Vector2(0f, 145f * dpiScale);

            _headingBox = UIFactory.CreatePanel(parent, "Heading_Box", boxSize, anchoredPos, theme.FrameBgColor);

            GameObject border = UIFactory.CreatePanel(_headingBox.transform, "Heading_Border", boxSize, Vector2.zero, Color.clear);
            Outline outline = border.AddComponent<Outline>();
            outline.effectColor = theme.FrameBorderColor;
            outline.effectDistance = new Vector2(1.5f * dpiScale, 1.5f * dpiScale);

            int fontSize = Mathf.RoundToInt(15f * dpiScale);
            _headingText = UIFactory.CreateText(_headingBox.transform, "Heading_Text", "000°", fontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.sizeDelta = boxSize;
            textRt.anchoredPosition = Vector2.zero;
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
        {
            // 核心要点：旋转 100% 同步官方/Principia 结算结果，Mod 不做任何计算
            if (_sphereObject != null)
            {
                _sphereObject.transform.localRotation = StockNavBallHook.GetRotation();
            }

            // 贴图 100% 自动同步第三方贴图模组 (Principia / TextureReplacer)
            Texture stockTex = StockNavBallHook.GetTexture();
            if (stockTex != null && _sphereMaterial != null && _sphereMaterial.GetTexture("_MainTex") != stockTex)
            {
                _sphereMaterial.SetTexture("_MainTex", stockTex);
            }

            // 航向读数同步官方
            if (_headingText != null)
            {
                _headingText.text = StockNavBallHook.GetHeadingText();
            }

            // 同步官方矢量标线 (Prograde, Retrograde, Normal, Target等)
            SyncMarkers();
        }

        private void SyncMarkers()
        {
            if (!StockNavBallHook.HasStockNavBall) return;
            var stock = StockNavBallHook.StockInstance;

            SyncSingleMarker("prograde", stock.progradeVector);
            SyncSingleMarker("retrograde", stock.retrogradeVector);
            SyncSingleMarker("normal", stock.normalVector);
            SyncSingleMarker("antinormal", stock.antiNormalVector);
            SyncSingleMarker("radialIn", stock.radialInVector);
            SyncSingleMarker("radialOut", stock.radialOutVector);
            SyncSingleMarker("target", stock.target);
        }

        private void SyncSingleMarker(string key, Transform stockMarker)
        {
            if (stockMarker == null || _sphereObject == null) return;

            if (!_markerClones.TryGetValue(key, out GameObject clone) || clone == null)
            {
                clone = Instantiate(stockMarker.gameObject);
                clone.name = "Cloned_" + key;
                clone.transform.SetParent(_sphereObject.transform, false);

                // 强制分配到 Layer 31 供离屏摄像机渲染
                Transform[] allTr = clone.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allTr.Length; i++)
                {
                    allTr[i].gameObject.layer = 31;
                }

                Renderer[] rends = clone.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    rends[i].enabled = true;
                }

                _markerClones[key] = clone;
            }

            bool active = stockMarker.gameObject.activeInHierarchy;
            clone.SetActive(active);
            if (active)
            {
                clone.transform.localPosition = stockMarker.localPosition;
                clone.transform.localRotation = stockMarker.localRotation;
                clone.transform.localScale = stockMarker.localScale;
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
                    // 1. 贴图模式：使用原版/TextureReplacer素材，并叠加高动态航电增强
                    Texture stockTex = StockNavBallHook.GetTexture();
                    if (stockTex != null)
                    {
                        _sphereMaterial.SetTexture("_MainTex", stockTex);
                    }
                    _sphereMaterial.SetColor("_RimColor", theme.RimGlowColor);
                    _sphereMaterial.SetFloat("_RimPower", 3.2f);
                    _sphereMaterial.SetFloat("_RimIntensity", 0.45f);
                    _sphereMaterial.SetFloat("_LimbPower", 1.4f);
                    _sphereMaterial.SetFloat("_LimbIntensity", 0.35f);
                    _sphereMaterial.SetFloat("_Contrast", 1.08f);
                    _sphereMaterial.SetFloat("_Brightness", 1.05f);
                    _sphereMaterial.SetFloat("_Saturation", 1.10f);
                }
                else
                {
                    // 2. 程序化矢量模式：纯数学完美超清解算，支持4K/8K无极抗锯齿
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
                    _sphereMaterial.SetFloat("_RimIntensity", 0.45f);
                    _sphereMaterial.SetFloat("_LimbPower", 1.35f);
                    _sphereMaterial.SetFloat("_LimbIntensity", 0.32f);
                }
            }

            if (_headingText != null)
            {
                _headingText.color = theme.TextPrimaryColor;
            }
        }

        private void OnDestroy()
        {
            foreach (var kvp in _markerClones)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _markerClones.Clear();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            if (_sphereMaterial != null)
            {
                Destroy(_sphereMaterial);
            }
        }
    }
}
