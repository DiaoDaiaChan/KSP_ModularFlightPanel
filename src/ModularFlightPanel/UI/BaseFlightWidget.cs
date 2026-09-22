using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 所有模块化飞行小组件必须继承的统一基类
    /// 自动提供：自由拖拽句柄绑定、分辨率响应、主题变更响应、生命周期管理
    /// </summary>
    public abstract class BaseFlightWidget : MonoBehaviour
    {
        public WidgetConfig Config { get; set; }
        public string WidgetId => Config?.WidgetId ?? "unknown";
        public string DisplayName => Config?.DisplayName ?? "组件";

        public RectTransform RectTransform { get; private set; }
        public WidgetDragHandler DragHandler { get; private set; }

        protected Canvas RootCanvas { get; private set; }
        protected float CurrentDpiScale { get; private set; } = 1.0f;

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

            // 应用保存的绝对/相对坐标
            RectTransform.anchoredPosition = new Vector2(config.PositionX, config.PositionY);

            // 挂载通用自由拖拽交互器
            DragHandler = gameObject.AddComponent<WidgetDragHandler>();
            DragHandler.Initialize(this, canvas);

            // 调用派生类专用初始化
            OnInitialize(config, theme);
            ApplyTheme(theme);
        }

        protected abstract void OnInitialize(WidgetConfig config, ThemeConfig theme);

        public abstract void ApplyTheme(ThemeConfig theme);

        public abstract void OnUpdateTelemetry(TelemetryHub telemetry);

        protected virtual void Update()
        {
            if (TelemetryHub.Instance != null && TelemetryHub.Instance.HasVessel)
            {
                OnUpdateTelemetry(TelemetryHub.Instance);
            }
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (Config != null) Config.IsEnabled = visible;
        }
    }
}
