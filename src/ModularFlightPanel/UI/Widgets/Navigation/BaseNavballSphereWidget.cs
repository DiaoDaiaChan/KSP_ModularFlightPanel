using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// 3D 姿态球/导航球标准抽象基类 (Base Navball Sphere Widget)
    /// 统一抽取并封装：
    /// 1. 离屏正交摄像机 (Offscreen Camera) 与渲染层级 (Layer 31) 隔离；
    /// 2. 动态自适应分辨率 RenderTexture 生命周期管理；
    /// 3. 3D 球体网格创建与材质回收；
    /// 4. WidgetRenderManager 分辨率自适应与设置联动事件注册/注销；
    /// 5. 严格杜绝内存泄漏与多余数学计算。
    /// </summary>
    public abstract class BaseNavballSphereWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        public override void ApplyTheme(ThemeConfig theme)
        {
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
        }

        protected RenderTexture _renderTexture;
        protected Camera _ballCamera;
        protected GameObject _sphereObject;
        protected Material _sphereMaterial;

        protected const int NavballOffscreenLayer = 31;

        protected virtual void CreateOffscreenPipeline(float ballDiameter, string cameraName = "Navball_Offscreen_Cam", float sphereScale = 1.88f)
        {
            int rtResolution = 512;
            if (WidgetRenderManager.Instance != null)
            {
                rtResolution = WidgetRenderManager.Instance.CalculateOptimalResolution(
                    new Vector2(ballDiameter, ballDiameter),
                    Config != null ? Config.Scale : 1.0f,
                    Config != null ? Config.RenderScale : 1.0f);
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

            // 离屏正交摄像机
            GameObject camObj = new GameObject(cameraName, typeof(Camera));
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
            _ballCamera.cullingMask = 1 << NavballOffscreenLayer;
            _ballCamera.enabled = false;

            // 3D 单位球体
            _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereObject.name = "Navball_Sphere_Mesh";
            _sphereObject.transform.SetParent(transform, false);
            _sphereObject.transform.localPosition = Vector3.zero;
            _sphereObject.layer = NavballOffscreenLayer;

            Collider col = _sphereObject.GetComponent<Collider>();
            if (col != null) Destroy(col);

            UpdateSphereScale();

            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnRenderResolutionChanged += HandleResolutionChanged;
                WidgetRenderManager.Instance.OnRenderSettingChanged += HandleRenderSettingChanged;
            }
        }

        /// <summary>
        /// 核心反向补偿缩放算法 (Counter-Scaling Engine)
        /// 使得 _sphereObject 在 Unity 3D 世界空间中的绝对物理尺寸恒定为 baseScale (默认直径 1.88f)，
        /// 彻底消除因 UGUI 根节点 localScale 放大导致正交摄像机视口内球体被放大裁切 (Zoom Bug) 与三轴非等比拉伸畸变！
        /// </summary>
        public virtual void UpdateSphereScale()
        {
            if (_sphereObject == null) return;
            float sx = transform.localScale.x;
            float sy = transform.localScale.y;
            float sz = transform.localScale.z;
            if (Mathf.Abs(sx) < 0.0001f) sx = 1f;
            if (Mathf.Abs(sy) < 0.0001f) sy = 1f;
            if (Mathf.Abs(sz) < 0.0001f) sz = 1f;

            float targetRadius = 0.94f;
            float baseScale = 1.88f;

            MeshFilter mf = _sphereObject.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds bounds = mf.sharedMesh.bounds;
                float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                if (maxExtent > 0.0001f)
                {
                    baseScale = targetRadius / maxExtent;
                }
            }

            _sphereObject.transform.localScale = new Vector3(baseScale / sx, baseScale / sy, baseScale / sz);
        }

        protected override void OnScaleChanged(float targetScale, float relativeRatio)
        {
            base.OnScaleChanged(targetScale, relativeRatio);
            UpdateSphereScale();
            HandleRenderSettingChanged();
        }

        protected virtual void LateUpdate()
        {
            if (_sphereObject != null && transform.hasChanged)
            {
                transform.hasChanged = false;
                UpdateSphereScale();
            }
        }

        protected virtual void HandleResolutionChanged(int newRes)
        {
            if (_renderTexture == null || !_renderTexture.IsCreated()) return;
            if (_renderTexture.width == newRes) return;

            _renderTexture.Release();
            Destroy(_renderTexture);

            _renderTexture = new RenderTexture(newRes, newRes, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
                anisoLevel = 4,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();

            if (_ballCamera != null)
            {
                _ballCamera.targetTexture = _renderTexture;
            }
            OnRenderTextureRecreated(_renderTexture);
        }

        protected virtual void HandleRenderSettingChanged()
        {
        }

        protected virtual void OnRenderTextureRecreated(RenderTexture newRt)
        {
        }

        protected override void OnDestroy()
        {
            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnRenderResolutionChanged -= HandleResolutionChanged;
                WidgetRenderManager.Instance.OnRenderSettingChanged -= HandleRenderSettingChanged;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
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
