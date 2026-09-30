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
    [AlwaysFullPower]
    public abstract class BaseNavballSphereWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        public override UIDrawPipelineKind PreferredDrawPipeline => UIDrawPipelineKind.NavballSphere3D;

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
        protected bool _isRenderDirty = true;
        public void MarkRenderDirty() => _isRenderDirty = true;
        public void MarkRenderClean() => _isRenderDirty = false;
        public bool IsRenderDirty => _isRenderDirty;
        public Material SphereMaterial => _sphereMaterial;
        public RenderTexture TargetTexture => _renderTexture;
        public Camera OffscreenCamera => _ballCamera;

        protected int _cachedOptimalResolution = 512;
        protected bool _optimalResDirty = true;

        public override FlightNavballPipeline GetNavballPipeline()
        {
            if (_optimalResDirty && WidgetRenderManager.Instance != null)
            {
                float dim = RectTransform != null ? Mathf.Max(RectTransform.rect.width, RectTransform.rect.height) : 200f;
                if (dim <= 0.1f) dim = 200f;
                _cachedOptimalResolution = WidgetRenderManager.Instance.CalculateOptimalResolution(
                    new Vector2(dim, dim),
                    Config != null ? Config.Scale : 1.0f,
                    Config != null ? Config.RenderScale : 1.0f,
                    minRes: 512);
                _optimalResDirty = false;
            }
            return new FlightNavballPipeline(_sphereMaterial, _renderTexture, _ballCamera, _isRenderDirty, _cachedOptimalResolution, this);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            // 若配置了 3D 离屏相机且检测到重绘脏标记，安全驱动离屏渲染
            if (_ballCamera != null && _isRenderDirty && _renderTexture != null && _renderTexture.IsCreated())
            {
                context.Navball.RenderCamera();
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
            _isRenderDirty = true;
        }

        protected override void OnScaleChanged(float targetScale, float relativeRatio)
        {
            base.OnScaleChanged(targetScale, relativeRatio);
            _optimalResDirty = true;
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
            _optimalResDirty = true;
            if (_renderTexture == null || !_renderTexture.IsCreated()) return;
            if (_renderTexture.width == newRes) return;

            _renderTexture.Release();
            Destroy(_renderTexture);

            _renderTexture = new RenderTexture(newRes, newRes, 0, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                anisoLevel = 8,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();

            if (_ballCamera != null)
            {
                _ballCamera.targetTexture = _renderTexture;
            }
            _isRenderDirty = true;
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
            this.Controls.UnregisterAll();
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
