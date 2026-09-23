using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 所有模块化飞行小组件必须继承的统一基类
    /// 自动提供：自由拖拽句柄绑定、分辨率响应、主题变更响应、生命周期管理、
    /// 独立画布渲染隔离 (Sub-Canvas Isolation) 以及按需刷新分频优化 (Update Throttling)
    /// </summary>
    public abstract class BaseFlightWidget : MonoBehaviour
    {
        public WidgetConfig Config { get; set; }
        public string WidgetId => Config?.WidgetId ?? "unknown";
        public string DisplayName => Config?.DisplayName ?? "组件";

        public RectTransform RectTransform { get; private set; }
        public WidgetDragHandler DragHandler { get; private set; }
        public Canvas SubCanvas { get; private set; }

        protected Canvas RootCanvas { get; private set; }
        protected float CurrentDpiScale { get; private set; } = 1.0f;
        protected float LastUpdateTime { get; private set; } = -1f;

        /// <summary>
        /// 默认刷新间隔 (秒)。高频组件重写为 0f (每帧)，低频组件可重写为 0.1f~0.5f
        /// </summary>
        public virtual float DefaultUpdateInterval => 0f;

        public virtual void BaseInitialize(Transform parent, Canvas canvas, WidgetConfig config, ThemeConfig theme, float scale)
        {
            Config = config;
            RootCanvas = canvas;
            CurrentDpiScale = scale;

            transform.SetParent(parent, false);
            RectTransform = GetComponent<RectTransform>();
            if (RectTransform == null)
            {
                RectTransform = gameObject.AddComponent<RectTransform>();
            }

            // 应用保存的绝对/相对坐标、缩放与旋转
            RectTransform.anchoredPosition = new Vector2(config.PositionX, config.PositionY);
            float s = config.Scale > 0.01f ? config.Scale : 1.0f;
            RectTransform.localScale = new Vector3(s, s, 1.0f);
            RectTransform.localEulerAngles = new Vector3(0f, 0f, config.Rotation);

            // 独立画布绘制优化 (Sub-Canvas Isolation)：
            // 当启用时，挂载嵌套 Sub-Canvas，隔离 UGUI 网格脏标记与 Draw Call 重建
            // 避免单个小组件重绘引发整屏 UI 顶点缓冲区全量刷新
            if (config != null && config.IsolateCanvas)
            {
                ApplyCanvasIsolation(true);
            }

            // 挂载通用自由拖拽交互器
            DragHandler = gameObject.AddComponent<WidgetDragHandler>();
            DragHandler.Initialize(this, canvas);

            // 调用派生类专用初始化
            OnInitialize(config, theme);
            ApplyTheme(theme);
        }

        /// <summary>
        /// 切换或应用独立画布渲染隔离
        /// </summary>
        public void ApplyCanvasIsolation(bool isolate)
        {
            SubCanvas = GetComponent<Canvas>();
            if (isolate)
            {
                if (SubCanvas == null)
                {
                    SubCanvas = gameObject.AddComponent<Canvas>();
                }
                SubCanvas.overrideSorting = false;
                if (GetComponent<GraphicRaycaster>() == null)
                {
                    gameObject.AddComponent<GraphicRaycaster>();
                }
            }
            else
            {
                if (SubCanvas != null)
                {
                    var raycaster = GetComponent<GraphicRaycaster>();
                    if (raycaster != null)
                    {
                        if (Application.isPlaying) Destroy(raycaster);
                        else DestroyImmediate(raycaster);
                    }
                    if (Application.isPlaying) Destroy(SubCanvas);
                    else DestroyImmediate(SubCanvas);
                    SubCanvas = null;
                }
            }
        }

        protected abstract void OnInitialize(WidgetConfig config, ThemeConfig theme);

        public abstract void ApplyTheme(ThemeConfig theme);

        public abstract void OnUpdateTelemetry(IFlightTelemetry telemetry);

        protected virtual void Update()
        {
            if (MFPProfiler.IsMasterBypassed) return;

            try
            {
                float interval = Config != null && Config.UpdateInterval > 0f ? Config.UpdateInterval : DefaultUpdateInterval;
                if (interval > 0f)
                {
                    if (Time.unscaledTime - LastUpdateTime < interval)
                    {
                        return;
                    }
                }
                LastUpdateTime = Time.unscaledTime;

                IFlightTelemetry telem = FlightTelemetryContext.Current;
                if (telem != null && telem.HasVessel)
                {
                    MFPProfiler.BeginSample(ProfilerSection.Widgets);
                    try
                    {
                        OnUpdateTelemetry(telem);
                    }
                    finally
                    {
                        MFPProfiler.EndSample(ProfilerSection.Widgets);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] Error updating widget {WidgetId}: {ex.Message}");
            }
        }

        /// <summary>
        /// 动态更新小组件的几何变换 (坐标、缩放与旋转) 并保持配置同步
        /// </summary>
        public void UpdateTransform(float? x = null, float? y = null, float? scale = null, float? rotation = null)
        {
            if (Config != null)
            {
                if (x.HasValue) Config.PositionX = x.Value;
                if (y.HasValue) Config.PositionY = y.Value;
                if (scale.HasValue) Config.Scale = Mathf.Clamp(scale.Value, 0.2f, 4.0f);
                if (rotation.HasValue) Config.Rotation = (rotation.Value % 360f + 360f) % 360f;
            }

            if (RectTransform != null)
            {
                if (x.HasValue || y.HasValue)
                {
                    RectTransform.anchoredPosition = new Vector2(Config?.PositionX ?? RectTransform.anchoredPosition.x, Config?.PositionY ?? RectTransform.anchoredPosition.y);
                }
                if (scale.HasValue)
                {
                    float s = (Config != null && Config.Scale > 0.01f) ? Config.Scale : 1.0f;
                    RectTransform.localScale = new Vector3(s, s, 1.0f);
                }
                if (rotation.HasValue)
                {
                    float r = Config != null ? Config.Rotation : 0f;
                    RectTransform.localEulerAngles = new Vector3(0f, 0f, r);
                }
            }

            if (DragHandler != null)
            {
                DragHandler.UpdateSelectionAppearance();
            }
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (Config != null) Config.IsEnabled = visible;
        }
    }
}
