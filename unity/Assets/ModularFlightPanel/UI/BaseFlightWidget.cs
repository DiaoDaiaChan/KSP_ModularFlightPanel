using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 所有模块化飞行小组件必须继承的统一基类
    /// 自动提供：自由拖拽句柄绑定、分辨率响应、主题与着色器样式管道 (WidgetStyleManager)、
    /// 独立画布渲染隔离 (Sub-Canvas Isolation)、生命周期与阶梯 Tick 刷新率管控 (WidgetRenderManager)、
    /// 全自动化交互侦测与 EventSystem 射线按需裁剪 (Raycast Target Pruning)
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
        /// 控件在 WidgetRenderManager 中的刷新率阶梯。
        /// Critical: 60Hz 满帧 (姿态球, 航向指示弧)
        /// Standard: 30Hz (滚带, 表盘, 罗盘, 导航)
        /// Relaxed: 10Hz (电力, 维生, ΔV, 控制栏, 时间加速, 轨道数据)
        /// </summary>
        public virtual WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        /// <summary>
        /// 默认刷新间隔 (秒)。仅在独立无管理器模式或特定配置下作为回退
        /// </summary>
        public virtual float DefaultUpdateInterval => 0f;

        /// <summary>
        /// 是否已受全局 WidgetRenderManager 接管（接管后禁用 MonoBehaviour 独立 Update，改由主分发调度）
        /// </summary>
        public bool IsManagedByRenderManager { get; set; } = false;

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
            // 挂载嵌套 Sub-Canvas，隔离 UGUI 网格脏标记与 Draw Call 重建
            ApplyCanvasIsolation(true);

            // 挂载通用自由拖拽交互器
            DragHandler = gameObject.AddComponent<WidgetDragHandler>();
            DragHandler.Initialize(this, canvas);

            // 注册进全局绘制与生命周期管理器 (WidgetRenderManager)
            WidgetRenderManager.Instance.RegisterWidget(this, RefreshTier);
            IsManagedByRenderManager = true;

            // 调用派生类专用初始化与样式应用
            OnInitialize(config, theme);
            ApplyTheme(theme);

            // 全自动化通用交互侦测与射线优化 (普适全量 33 个组件，彻底告别单组件硬编码与手动重写)
            AutoDetectInteractivityAndPruneRaycasts();
        }

        private bool? _explicitInteractive = null;
        private bool _autoDetectedInteractive = false;

        /// <summary>
        /// 该小组件是否包含需要在正常飞行状态下接收鼠标点击的交互式控件 (如分级按钮、时间加速控制等)。
        /// 全自动普适化驱动：默认在 BaseInitialize 自动扫描层级中的 Selectable (Button/Toggle/Slider等) 与 Pointer 点击处理器。
        /// 纯只读读数仪表自动返回 false，彻底关闭 GraphicRaycaster，杜绝鼠标移动时 EventSystem 全场景射线遍历。
        /// 支持通过属性直接覆盖显式设定。
        /// </summary>
        public virtual bool IsInteractive
        {
            get => _explicitInteractive ?? _autoDetectedInteractive;
            set
            {
                _explicitInteractive = value;
                UpdateRaycasterState();
            }
        }

        public GraphicRaycaster SubRaycaster { get; private set; }

        protected virtual void Awake()
        {
            WidgetDragHandler.OnEditModeChanged += HandleEditModeChanged;
        }

        protected virtual void OnDestroy()
        {
            WidgetDragHandler.OnEditModeChanged -= HandleEditModeChanged;
            WidgetRenderManager.Instance?.UnregisterWidget(this);
        }

        private void HandleEditModeChanged(bool isEdit)
        {
            UpdateRaycasterState();
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
                UpdateRaycasterState();
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
                    SubRaycaster = null;
                }
            }
        }

        /// <summary>
        /// 全自动化通用交互侦测与射线优化：
        /// 1. 深度扫描子节点中的交互控件 (Selectable: Button, Toggle, Slider, Dropdown, InputField 或 IPointerClickHandler)
        /// 2. 自动关闭所有纯视觉元件 (背景板, 刻度线, 静态标签) 的 raycastTarget，将射线遍历目标降至最低
        /// 3. 根据自动判定的交互性配置 SubRaycaster 状态
        /// </summary>
        public void AutoDetectInteractivityAndPruneRaycasts()
        {
            // 1. 自动深度扫描是否具有交互元素
            var selectables = GetComponentsInChildren<Selectable>(true);
            var pointerHandlers = GetComponentsInChildren<IPointerClickHandler>(true);
            var eventTriggers = GetComponentsInChildren<EventTrigger>(true);

            _autoDetectedInteractive = (selectables != null && selectables.Length > 0)
                || (pointerHandlers != null && pointerHandlers.Length > 0)
                || (eventTriggers != null && eventTriggers.Length > 0);

            // 2. 普适化优化：将非交互性纯视觉元件的 raycastTarget 设为 false
            var graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic g = graphics[i];
                if (g == null) continue;

                // EditOverlay 属于通用拖拽交互把手，由 WidgetDragHandler 自行控制
                if (g.gameObject.name == "EditOverlay" || (g.transform.parent != null && g.transform.parent.name == "EditOverlay")) continue;

                bool isTarget = false;
                // 自身挂载了交互组件
                if (g.GetComponent<Selectable>() != null || g.GetComponent<IPointerClickHandler>() != null || g.GetComponent<EventTrigger>() != null)
                {
                    isTarget = true;
                }
                else
                {
                    // 检查是否是其父级 Selectable 的 targetGraphic
                    var parentSelectable = g.GetComponentInParent<Selectable>();
                    if (parentSelectable != null && parentSelectable.targetGraphic == g)
                    {
                        isTarget = true;
                    }
                }

                if (!isTarget)
                {
                    g.raycastTarget = false;
                }
            }

            UpdateRaycasterState();
        }

        /// <summary>
        /// 动态按需激活/休眠当前组件的 GraphicRaycaster
        /// </summary>
        public void UpdateRaycasterState()
        {
            if (SubCanvas == null) return;
            bool needsRaycaster = IsInteractive || WidgetDragHandler.IsEditModeActive;
            if (SubRaycaster == null)
            {
                SubRaycaster = GetComponent<GraphicRaycaster>();
            }

            if (needsRaycaster)
            {
                if (SubRaycaster == null) SubRaycaster = gameObject.AddComponent<GraphicRaycaster>();
                SubRaycaster.enabled = true;
            }
            else if (SubRaycaster != null)
            {
                SubRaycaster.enabled = false;
            }
        }

        protected abstract void OnInitialize(WidgetConfig config, ThemeConfig theme);

        public abstract void ApplyTheme(ThemeConfig theme);

        public abstract void OnUpdateTelemetry(IFlightTelemetry telemetry);

        #region WidgetStyleManager Convenience Helpers

        protected void ApplyCard(Image bg, Outline border, CardStyleRole role = CardStyleRole.Normal, ThemeConfig theme = null)
        {
            WidgetStyleManager.Instance.ApplyCardFrame(bg, border, role, theme);
        }

        protected void ApplyText(Text text, TextStyleRole role = TextStyleRole.PrimaryValue, ThemeConfig theme = null)
        {
            WidgetStyleManager.Instance.ApplyTextStyle(text, role, theme);
        }

        protected void ApplyButton(Button btn, Image bg, Text label, ButtonVisualRole role = ButtonVisualRole.Normal, bool isPressed = false, ThemeConfig theme = null)
        {
            WidgetStyleManager.Instance.ApplyButtonStyle(btn, bg, label, role, isPressed, theme);
        }

        protected void ApplyMeter(Graphic track, Graphic fill, Graphic needle = null, MeterStyleRole role = MeterStyleRole.Primary, ThemeConfig theme = null)
        {
            WidgetStyleManager.Instance.ApplyMeterStyle(track, fill, needle, role, theme);
        }

        #endregion

        #region Universal Performance & Zero-Allocation UI Helpers

        /// <summary>
        /// 通用文本脏标记守卫：仅当文本内容实际发生改变时才写入 UGUI Text，
        /// 避免重复赋值引发 UGUI 顶点缓冲区与网格布局的无效重建
        /// </summary>
        public static bool SetTextIfChanged(Text textComponent, string newText)
        {
            if (textComponent == null || newText == null) return false;
            if (string.Equals(textComponent.text, newText, StringComparison.Ordinal)) return false;
            textComponent.text = newText;
            return true;
        }

        /// <summary>
        /// 通用图片填充脏标记守卫：仅当填充比例变化超过容差时才更新 fillAmount
        /// </summary>
        public static bool SetImageFillIfChanged(Image image, float fillAmount, float epsilon = 0.001f)
        {
            if (image == null) return false;
            if (Mathf.Abs(image.fillAmount - fillAmount) <= epsilon) return false;
            image.fillAmount = fillAmount;
            return true;
        }

        /// <summary>
        /// 通用颜色脏标记守卫：仅当颜色变化时才写入 Graphic.color
        /// </summary>
        public static bool SetColorIfChanged(Graphic graphic, Color targetColor)
        {
            if (graphic == null) return false;
            if (graphic.color == targetColor) return false;
            graphic.color = targetColor;
            return true;
        }

        /// <summary>
        /// P1: 浮点数死区量化格式化（调用 CacheManager 统一中枢，避免每帧分配新 string 产生 GC 垃圾）
        /// </summary>
        public string FastFormat(string paramName, double value, string format = "F1", double tolerance = 0.05)
        {
            string key = (WidgetId ?? "w") + "_" + paramName;
            return CacheManager.Instance.FastDouble(key, value, format, tolerance);
        }

        /// <summary>
        /// P1: 自带前后缀的浮点数死区量化格式化
        /// </summary>
        public string FastFormatWithAffix(string paramName, double value, string prefix, string suffix, string format = "F1", double tolerance = 0.05)
        {
            string key = (WidgetId ?? "w") + "_" + paramName;
            return CacheManager.Instance.FastDoubleWithAffix(key, value, prefix, suffix, format, tolerance);
        }

        /// <summary>
        /// P1: 零 GC 快速整数格式化 (-1000 ~ 9999 静态数组直取)
        /// </summary>
        public static string FastIntString(int value)
        {
            return CacheManager.FastInt(value);
        }

        /// <summary>
        /// P1: 零 GC 快速百分比格式化 (0% ~ 100% 静态数组直取)
        /// </summary>
        public static string FastPercentString(int percent)
        {
            return CacheManager.FastPercent(percent);
        }

        /// <summary>
        /// P1: 零 GC 快速度数格式化 (0° ~ 360° 静态数组直取)
        /// </summary>
        public static string FastDegreeString(int degree)
        {
            return CacheManager.FastDegree(degree);
        }

        #endregion

        protected virtual void Update()
        {
            // 当已被 WidgetRenderManager 接管并由 MasterUpdate 分发时，跳过自身独立 Update，消除开销
            if (IsManagedByRenderManager) return;
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
            WidgetRenderManager.Instance?.SetWidgetActive(this, visible);
        }
    }
}
