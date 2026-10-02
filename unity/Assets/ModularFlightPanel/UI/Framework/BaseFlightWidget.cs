using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 所有模块化飞行小组件必须继承的统一基类
    /// 自动提供：自由拖拽句柄绑定、分辨率响应、主题与着色器样式管道 (WidgetStyleManager)、
    /// 独立画布渲染隔离 (Sub-Canvas Isolation)、生命周期与阶梯 Tick 刷新率管控 (WidgetRenderManager)、
    /// 全自动化交互侦测与 EventSystem 射线按需裁剪 (Raycast Target Pruning)、
    /// 集中式数据心跳驱动与物理步长解算 (IDataHeartBeat)
    /// </summary>
    public abstract class BaseFlightWidget : MonoBehaviour, IDataHeartBeat, IUIDrawLoop, CacheManager.IWidgetPrivateCache
    {
        public WidgetConfig Config { get; set; }
        public string WidgetId => Config?.WidgetId ?? "unknown";
        public string DisplayName => Config?.DisplayName ?? I18n.Tr("WIDGET_FALLBACK_NAME", "组件");

        public RectTransform RectTransform { get; private set; }
        public WidgetDragHandler DragHandler { get; private set; }
        public Canvas SubCanvas { get; private set; }
        public CanvasGroup CanvasGroup { get; private set; }

        /// <summary>
        /// 当前组件不透明度 (0.05f ~ 1.0f)
        /// </summary>
        public float Opacity
        {
            get => Config != null && Config.Opacity > 0.01f ? Config.Opacity : 1.0f;
            set => SetOpacity(value);
        }

        /// <summary>
        /// 设置当前组件整体不透明度并实时应用至 CanvasGroup
        /// </summary>
        public void SetOpacity(float opacity, bool save = true)
        {
            float clamped = Mathf.Clamp(opacity, 0.05f, 1.0f);
            if (Config != null) Config.Opacity = clamped;
            if (CanvasGroup != null) CanvasGroup.alpha = clamped;
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 当前组件独立配色主题覆盖 ID (若为空则跟随全局主题)
        /// </summary>
        public string ThemeOverride
        {
            get => Config?.ThemeOverride ?? "";
            set => SetThemeOverride(value);
        }

        /// <summary>
        /// 当前组件实际生效的主题配置
        /// </summary>
        public ThemeConfig WidgetTheme => ResolveEffectiveTheme(null);

        /// <summary>
        /// 解析该组件当前生效的主题配置 (优先使用组件专属 ThemeOverride，否则回退至全局主题)
        /// </summary>
        public ThemeConfig ResolveEffectiveTheme(ThemeConfig globalTheme = null)
        {
            string ovr = Config?.ThemeOverride;
            if (!string.IsNullOrEmpty(ovr))
            {
                var builtins = ThemeConfig.GetAllBuiltinThemes();
                for (int i = 0; i < builtins.Count; i++)
                {
                    if (string.Equals(builtins[i].ThemeId, ovr, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(builtins[i].ThemeId.Replace("_", ""), ovr.Replace("_", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        return builtins[i];
                    }
                }
                if (ThemeManager.Instance?.AvailableThemes != null)
                {
                    var av = ThemeManager.Instance.AvailableThemes;
                    for (int i = 0; i < av.Count; i++)
                    {
                        if (string.Equals(av[i].ThemeId, ovr, StringComparison.OrdinalIgnoreCase))
                        {
                            return av[i];
                        }
                    }
                }
            }
            return WidgetStyleManager.ResolveTheme(globalTheme);
        }

        /// <summary>
        /// 设置组件独立配色主题并实时重绘微控件与底板
        /// </summary>
        public void SetThemeOverride(string themeId, bool save = true)
        {
            if (Config != null) Config.ThemeOverride = themeId ?? "";
            ThemeConfig effective = ResolveEffectiveTheme(null);
            ApplyTheme(effective);
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 调整组件长宽比 (目标宽高比 ratio = ScaleX / ScaleY)
        /// </summary>
        public void SetAspectRatio(float targetAspectRatio, bool save = true)
        {
            if (Config == null) return;
            float currentArea = Config.EffectiveScaleX * Config.EffectiveScaleY;
            if (currentArea <= 0.01f) currentArea = 1.0f;
            float newScaleX = Mathf.Clamp(Mathf.Sqrt(currentArea * targetAspectRatio), 0.2f, 4.0f);
            float newScaleY = Mathf.Clamp(Mathf.Sqrt(currentArea / targetAspectRatio), 0.2f, 4.0f);
            UpdateTransform(scaleX: newScaleX, scaleY: newScaleY);
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 调整水平比例 (细长/紧凑)
        /// </summary>
        public void AdjustScaleX(float delta, bool save = true)
        {
            if (Config == null) return;
            float newX = Mathf.Clamp(Config.EffectiveScaleX + delta, 0.2f, 4.0f);
            newX = Mathf.Round(newX * 20f) / 20f;
            UpdateTransform(scaleX: newX);
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 调整垂直比例 (矮胖/拉长)
        /// </summary>
        public void AdjustScaleY(float delta, bool save = true)
        {
            if (Config == null) return;
            float newY = Mathf.Clamp(Config.EffectiveScaleY + delta, 0.2f, 4.0f);
            newY = Mathf.Round(newY * 20f) / 20f;
            UpdateTransform(scaleY: newY);
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 复位长宽比为 1:1 等比例状态
        /// </summary>
        public void ResetAspectRatio(bool save = true)
        {
            if (Config == null) return;
            float s = Config.Scale > 0.01f ? Config.Scale : 1.0f;
            UpdateTransform(scaleX: s, scaleY: s);
            if (save) WidgetLayoutManager.Instance?.SaveLayout();
        }

        /// <summary>
        /// 组件标准化微控件管理器容器 (Standardized Control Container)
        /// </summary>
        public WidgetControlContainer Controls { get; }

        protected BaseFlightWidget()
        {
            Controls = new WidgetControlContainer(this);
        }

        protected Canvas RootCanvas { get; private set; }
        public float CurrentDpiScale { get; protected set; } = 1.0f;
        protected float LastUpdateTime { get; private set; } = -1f;

        private bool? _cachedAlwaysFullPower;

        /// <summary>
        /// 声明该组件是否强制满血极速运行 (Always Full Power / Unthrottled)。
        /// 支持通过在类上标注 [AlwaysFullPower] 特性、[FlightWidget(..., AlwaysFullPower = true)] 或 override 此属性返回 true。
        /// 当为 true 时：
        /// 1. RefreshTier 自动为 Critical (随游戏实时 FPS 满帧直通，永不降频)；
        /// 2. HeartBeatTier 自动为 Critical (每一渲染帧同步更新遥测，零延迟)；
        /// 3. 彻底豁免全局微秒帧预算切片 (Budget Slicing Exempt) 与省电节能模式 (Eco Saver Exempt)；
        /// 专为 3D 姿态球 (NavballSphereWidget) 等核心航电生命线量身定制！
        /// </summary>
        public virtual bool AlwaysFullPower
        {
            get
            {
                if (!_cachedAlwaysFullPower.HasValue)
                {
                    Type t = GetType();
                    bool hasAttr = Attribute.IsDefined(t, typeof(AlwaysFullPowerAttribute), true);
                    if (!hasAttr)
                    {
                        var fw = (FlightWidgetAttribute)Attribute.GetCustomAttribute(t, typeof(FlightWidgetAttribute), true);
                        hasAttr = fw != null && fw.AlwaysFullPower;
                    }
                    _cachedAlwaysFullPower = hasAttr;
                }
                return _cachedAlwaysFullPower.Value;
            }
        }

        /// <summary>
        /// 控件在 WidgetRenderManager 中的刷新率阶梯。
        /// Critical: 随游戏 FPS (满帧直通游戏实时渲染帧率，如 3D 姿态球)
        /// Standard: 60Hz (标准高帧率 UI，如速度/高度动态标尺带)
        /// Slow: 30Hz (经典仪表，如 ND 导航, SAS 罗盘)
        /// Relaxed: 10Hz (低频监控，如电力, 维生, ΔV, 控制栏, 时间加速, 轨道数据)
        /// UltraLow: 2Hz (后台低频监视，深空巡航)
        /// Custom: 自定义 (由 CustomHz / DefaultUpdateInterval 决定)
        /// 若声明了 AlwaysFullPower，自动强制返回 Critical。
        /// </summary>
        public virtual WidgetRefreshTier RefreshTier => AlwaysFullPower ? WidgetRefreshTier.Critical : WidgetRefreshTier.Standard;

        /// <summary>
        /// 控件数据心跳节拍阶梯 (Data Heartbeat Tier)。
        /// 约束契约：HeartBeat 频率必须小于等于 RefreshTier 组件画面刷新率。
        /// 缺省等价机制：默认与当前组件的 RefreshTier 保持相等，零额外心智负担。
        /// 若声明了 AlwaysFullPower，自动强制返回 Critical 每一帧同步求解。
        /// </summary>
        public virtual WidgetRefreshTier HeartBeatTier => AlwaysFullPower ? WidgetRefreshTier.Critical : RefreshTier;

        /// <summary>
        /// 根据阶梯与自定义参数换算周期秒数
        /// </summary>
        public static float GetTierInterval(WidgetRefreshTier tier, float customInterval)
        {
            switch (tier)
            {
                case WidgetRefreshTier.Critical: return 0f;
                case WidgetRefreshTier.Standard: return 1.0f / 60f;
                case WidgetRefreshTier.Slow:     return 1.0f / 30f;
                case WidgetRefreshTier.Relaxed:  return 1.0f / 10f;
                case WidgetRefreshTier.UltraLow: return 1.0f / 2f;
                case WidgetRefreshTier.Custom:   return customInterval > 0f ? customInterval : (1.0f / 60f);
                default: return 1.0f / 60f;
            }
        }

        /// <summary>
        /// 当前组件画面显示刷新周期 (秒, UIDrawLoop)
        /// 优先级：AlwaysFullPower(0s) > Config.CustomHz / UpdateInterval > 组件源码 CustomHz / DefaultUpdateInterval > RefreshTier 阶梯周期
        /// </summary>
        public float RefreshInterval
        {
            get
            {
                if (AlwaysFullPower) return 0f;
                if (Config != null)
                {
                    if (Config.CustomHz > 0.001f) return 1.0f / Config.CustomHz;
                    if (Config.UpdateInterval > 0.0001f) return Config.UpdateInterval;
                }
                if (CustomHz > 0.001f) return 1.0f / CustomHz;
                if (DefaultUpdateInterval > 0f) return DefaultUpdateInterval;
                return GetTierInterval(RefreshTier, 0f);
            }
        }

        /// <summary>
        /// 当前组件数据心跳节拍周期 (秒, DataHeartbeat)
        /// 优先级：AlwaysFullPower(0s) > Config.HeartBeatHz / HeartBeatInterval > 全局心跳基准 GlobalDataHeartbeatHz > HeartBeatTier 阶梯周期
        /// </summary>
        public float HeartBeatInterval
        {
            get
            {
                if (AlwaysFullPower) return 0f;
                if (Config != null)
                {
                    if (Config.HeartBeatHz > 0.001f) return 1.0f / Config.HeartBeatHz;
                    if (Config.HeartBeatInterval >= 0f) return Config.HeartBeatInterval;
                }
                if (WidgetRenderManager.Instance != null && WidgetRenderManager.Instance.GlobalDataHeartbeatHz > 0.001f)
                {
                    return 1.0f / WidgetRenderManager.Instance.GlobalDataHeartbeatHz;
                }
                return GetTierInterval(HeartBeatTier, DefaultUpdateInterval);
            }
        }

        /// <summary>
        /// 经父类安全拦截收敛后的实际生效数据心跳阶梯（物理保证 HeartBeat 频率 <= RefreshTier 刷新率）。
        /// 若 HeartBeatTier 误设为更小数值 (更高频)，父类自动强制截断钳位为 RefreshTier。
        /// </summary>
        public WidgetRefreshTier EffectiveHeartBeatTier
        {
            get
            {
                if (AlwaysFullPower) return WidgetRefreshTier.Critical;
                WidgetRefreshTier hb = HeartBeatTier;
                WidgetRefreshTier rf = RefreshTier;
                if (hb == WidgetRefreshTier.Custom || rf == WidgetRefreshTier.Custom)
                {
                    return HeartBeatInterval < RefreshInterval ? rf : hb;
                }
                return hb < rf ? rf : hb;
            }
        }

        /// <summary>
        /// 经父类收敛计算后的实际生效心跳周期秒数（物理保证 SPEC-002B：HeartBeat 频率 <= RefreshTier 刷新率，即心跳周期 >= 刷新周期）
        /// </summary>
        public float EffectiveHeartBeatInterval
        {
            get
            {
                if (AlwaysFullPower) return 0f;
                float rfInterval = RefreshInterval;
                float hbInterval = HeartBeatInterval;
                // 心跳频率必须 <= 刷新率，即心跳周期必须 >= 刷新周期
                return hbInterval < rfInterval ? rfInterval : hbInterval;
            }
        }

        /// <summary>
        /// 组件源码级自定义目标刷新率 (Hz)。
        /// 当派生类 override 此属性并返回大于 0 的数值时 (例如 20f, 45f)，
        /// 将直接以该精确频率执行节流更新，拥有高于 RefreshTier 阶梯的生效优先级。
        /// </summary>
        public virtual float CustomHz => 0f;

        /// <summary>
        /// 默认刷新间隔 (秒)。优先由 CustomHz 换算推导，亦可直接重写。
        /// </summary>
        public virtual float DefaultUpdateInterval => CustomHz > 0.001f ? (1.0f / CustomHz) : 0f;

        /// <summary>
        /// 组件首选的 UI 绘制管线类型 (默认标准 2D UI 矢量/着色器管线，姿态球类可覆盖返回 NavballSphere3D)
        /// </summary>
        public virtual UIDrawPipelineKind PreferredDrawPipeline => UIDrawPipelineKind.UiShader2D;

        /// <summary>
        /// 姿态球专属绘制管线构造器（默认返回空，由 BaseNavballSphereWidget 派生实现）
        /// </summary>
        public virtual FlightNavballPipeline GetNavballPipeline() => default;

        /// <summary>
        /// 是否已受全局 WidgetRenderManager 接管（接管后禁用 MonoBehaviour 独立 Update，改由主分发调度）
        /// </summary>
        public bool IsManagedByRenderManager { get; set; } = false;

        /// <summary>
        /// 当前组件在初始化时实际固化的设计缩放系数 (Committed Widget Scale)
        /// </summary>
        public float CommittedScale { get; private set; } = 1.0f;

        // ==========================================================================================
        // 小分辨率锐利度契约 (Crisp Geometry Contract)
        //
        // 问题：UGUI 中的 Image 若被指定小于 1 个物理像素的边宽（例如 1.5 UI 单位 × 0.6 的 DPI 缩放
        //       = 0.9 物理像素），光栅化时要么被完全丢弃（虚线抖断），要么以 < 1 的覆盖率渲染成
        //       半透明灰线（糊边）。分辨率越低越严重 —— 这正是小尺寸组件"不够锐利"的根因。
        //
        // 对策：任何"细几何体"（圆弧分段、标尺、指针、刻度线、基准线）的线宽都必须经过
        //       CrispLength 钳制到 >= MinCrispPhysicalPixels 个物理像素，并做半像素栅格对齐。
        //
        // 注意：本契约只用于**图元几何尺寸**，不适用于字号。字号在低分辨率下应"变大"（相对提升），
        //       而非被钳制到某个最小物理像素。
        // ==========================================================================================

        /// <summary>细几何体的最小物理像素宽度。低于此值的 Image 会在低分辨率下丢线或糊边。</summary>
        public const float MinCrispPhysicalPixels = 1.5f;

        /// <summary>
        /// 计算当前组件在屏幕上的物理像素 / UI 单位比值 (等价于 Canvas.scaleFactor × 全局缩放)。
        /// 该值 <![CDATA[<]]> 1 表示组件被缩小显示，细几何体面临亚像素风险。
        /// </summary>
        public float CurrentPixelRatio
        {
            get
            {
                if (CurrentDpiScale > 0.0001f) return CurrentDpiScale;
                var rm = WidgetRenderManager.Instance;
                return rm != null ? rm.GetCanvasScaleFactor() : 1.0f;
            }
        }

        /// <summary>
        /// 将图元线宽钳制到至少 <see cref="MinCrispPhysicalPixels"/> 个物理像素，并做半像素栅格对齐。
        /// 入参与返回均为 UI 单位；调用方应直接将其用于 sizeDelta / effectDistance。
        /// </summary>
        public float CrispLength(float uiLength, float minPhysicalPixels = MinCrispPhysicalPixels)
        {
            float ratio = CurrentPixelRatio;
            if (ratio <= 0.0001f) ratio = 1.0f;

            float uiLen = Mathf.Max(uiLength, minPhysicalPixels / ratio);

            // 在当前物理像素栅格上量化到半像素边界，使边缘恰好落在像素中心，消除双线性糊边
            float physical = uiLen * ratio;
            physical = Mathf.Floor(physical) + 0.5f;
            return physical / ratio;
        }

        /// <summary>
        /// 在给定父级横向上居中放置一条宽度受钳制的水平细线，消除半像素偏移造成的糊边。
        /// </summary>
        public static RectTransform CrispHorizontalRule(RectTransform parent, float centerX, float centerY,
            float uiWidth, float uiHeight)
        {
            GameObject go = UIFactory.CreatePanel(parent, "CrispRule", new Vector2(uiWidth, uiHeight),
                new Vector2(centerX, centerY), Color.clear);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(centerX, centerY);
            rt.sizeDelta = new Vector2(uiWidth, uiHeight);
            return rt;
        }

        public virtual void BaseInitialize(Transform parent, Canvas canvas, WidgetConfig config, ThemeConfig theme, float scale)
        {
            Config = config;
            theme = ResolveEffectiveTheme(theme);
            RootCanvas = canvas;
            float widgetScale = (config != null && config.Scale > 0.01f) ? config.Scale : 1.0f;
            CommittedScale = widgetScale;
            // 核心分辨率铁律：将组件配置的自身缩放 (Widget Scale) 与屏幕物理 DPI 缩放融为一体，
            // 确保派生类在 OnInitialize 中创建的一切文本、线宽与 RenderTexture 均以物理 1:1 原生分辨率栅格化，
            // 彻底告别 GPU localScale 双线性模糊拉伸。
            CurrentDpiScale = scale * widgetScale;

            transform.SetParent(parent, false);
            RectTransform = GetComponent<RectTransform>();
            if (RectTransform == null)
            {
                RectTransform = gameObject.AddComponent<RectTransform>();
            }

            CanvasGroup = GetComponent<CanvasGroup>();
            if (CanvasGroup == null)
            {
                CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            float targetOpacity = (config != null && config.Opacity > 0.01f) ? config.Opacity : 1.0f;
            CanvasGroup.alpha = Mathf.Clamp(targetOpacity, 0.05f, 1.0f);

            // 统一锚点与轴心至中心 (0.5, 0.5)，彻底杜绝编辑模式包围盒与组件像素错位
            RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            RectTransform.pivot = new Vector2(0.5f, 0.5f);

            // 应用保存的绝对/相对坐标、缩放与旋转 (以原生 1:1 坐标系建立)
            float posX = config.PositionX;
            float posY = config.PositionY;
            float effScale = Mathf.Max(0.1f, scale);
            float halfScreenW = (Screen.width / effScale) * 0.5f;
            float canvasH = Screen.height / effScale;
            float hudRootY = 215f * scale;
            float minSafeY = -hudRootY - 200f;
            float maxSafeY = (canvasH - hudRootY) + 200f;
            if (halfScreenW > 100f && (Mathf.Abs(posX) > halfScreenW + 200f || posY < minSafeY || posY > maxSafeY))
            {
                posX = Mathf.Clamp(posX, -halfScreenW + 100f, halfScreenW - 100f);
                posY = Mathf.Clamp(posY, -hudRootY + 50f, (canvasH - hudRootY) - 50f);
                config.PositionX = posX;
                config.PositionY = posY;
            }
            RectTransform.anchoredPosition = new Vector2(posX, posY);
            float effX = config.EffectiveScaleX;
            float effY = config.EffectiveScaleY;

            bool isAdaptive = (this is IAdaptiveSizeWidget adaptiveWidget) && adaptiveWidget.AllowNonUniformScale;
            if (isAdaptive)
            {
                // 自适应组件：彻底保持 localScale 1:1，杜绝字体/边框仿射拉伸畸变
                RectTransform.localScale = Vector3.one;
            }
            else
            {
                float initRatioX = CommittedScale > 0.001f ? (effX / CommittedScale) : 1.0f;
                float initRatioY = CommittedScale > 0.001f ? (effY / CommittedScale) : 1.0f;
                RectTransform.localScale = new Vector3(initRatioX, initRatioY, 1.0f);
            }
            RectTransform.localEulerAngles = new Vector3(0f, 0f, config.Rotation);

            // 上级自动尺寸派发：当派生类声明了 BaseSize 时，自动计算物理像素尺寸
            if (BaseSize.x > 0f && BaseSize.y > 0f)
            {
                if (isAdaptive)
                {
                    float factorX = CommittedScale > 0.001f ? (effX / CommittedScale) : 1.0f;
                    float factorY = CommittedScale > 0.001f ? (effY / CommittedScale) : 1.0f;
                    Vector2 newSize = new Vector2(BaseSize.x * CurrentDpiScale * factorX, BaseSize.y * CurrentDpiScale * factorY);
                    RectTransform.sizeDelta = newSize;
                    ((IAdaptiveSizeWidget)this).OnAdaptiveResize(newSize);
                }
                else
                {
                    RectTransform.sizeDelta = BaseSize * CurrentDpiScale;
                }
            }

            // 上级自动卡片底板派发：当启用 AutoCreateCardFrame 时，基类自动在根节点创建背景板与 Outline，
            // 并自动接入微控件主题与样式生命周期，派生类无需手写一行底板代码
            if (AutoCreateCardFrame)
            {
                CardBackground = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
                CardBackground.material = WidgetStyleManager.Instance.GetUiMaterial(isText: false);
                CardOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
                CardOutline.effectDistance = new Vector2(1f * CurrentDpiScale, 1f * CurrentDpiScale);
                WidgetStyleManager.Instance.ApplyCardFrame(CardBackground, CardOutline, CardRole, theme);

                this.Controls.Wrap("card_frame", I18n.Tr("CTL_CARD_FRAME", "卡片底板"), gameObject, t =>
                {
                    WidgetStyleManager.Instance.ApplyCardFrame(CardBackground, CardOutline, CardRole, t);
                });
            }

            // 独立画布绘制优化 (Sub-Canvas Isolation)：
            // 挂载嵌套 Sub-Canvas，隔离 UGUI 网格脏标记与 Draw Call 重建
            ApplyCanvasIsolation(true);

            // 挂载通用自由拖拽交互器
            DragHandler = gameObject.AddComponent<WidgetDragHandler>();
            DragHandler.Initialize(this, canvas);

            // 注册进全局绘制与生命周期管理器 (WidgetRenderManager)
            WidgetRenderManager.Instance.RegisterWidget(this, RefreshTier);
            IsManagedByRenderManager = true;

            // 检查派生类是否显式重写了 Update() / LateUpdate() / FixedUpdate()。若均未重写，则关闭自身 MonoBehaviour.enabled，
            // 避免 Unity 引擎每帧对无帧循环的组件产生无谓调度；同时保障重写了 LateUpdate() 的核心航电组件 (如姿态球) 正常触发
            var updateMethod = GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var lateUpdateMethod = GetType().GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var fixedUpdateMethod = GetType().GetMethod("FixedUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            bool hasUnityLifecycle = (updateMethod != null && updateMethod.DeclaringType != typeof(BaseFlightWidget)) ||
                                     (lateUpdateMethod != null && lateUpdateMethod.DeclaringType != typeof(BaseFlightWidget)) ||
                                     (fixedUpdateMethod != null && fixedUpdateMethod.DeclaringType != typeof(BaseFlightWidget));

            if (!hasUnityLifecycle)
            {
                this.enabled = false;
            }

            // 监听全局语言切换通知
            I18nManager.OnLanguageChanged += HandleLanguageChanged;

            // 自动扫描与构建声明式 DSL 控件 (Object-DSL 范式，头部集中声明即可全自动构建)
            AutoBuildDslControls(theme);

            // 自动扫描与提前注入特性微控件 (若派生类在头部特性中声明了布局，在此全自动构建 UGUI 并注入字段)
            AutoRegisterAnnotatedControls();

            // 调用派生类专用初始化与样式应用 (派生类可直接使用已自动注入的字段，或执行特异化排版)
            OnInitialize(config, theme);

            // 二次扫描补全 (针对在 OnInitialize 中手动赋值的字段)
            AutoRegisterAnnotatedControls();

            // 全自动微控件治理：自动绑定配置与下发主题，派生组件彻底无需手写绑定与生效调用
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);

            // 全自动化通用交互侦测与射线优化 (普适全量 33 个组件，彻底告别单组件硬编码与手动重写)
            AutoDetectInteractivityAndPruneRaycasts();
        }

        /// <summary>
        /// 组件设计参考原生尺寸 (Design Reference Size，在 1.0x DPI 下的基础像素尺寸)。
        /// 派生组件若 override 此属性并返回大于 Vector2.zero 的值，
        /// 基类 BaseInitialize 会在 OnInitialize 之前自动将其乘以 CurrentDpiScale 并赋予 RectTransform.sizeDelta。
        /// 彻底省去派生类重复书写 `RectTransform.sizeDelta = new Vector2(...) * CurrentDpiScale` 的样板代码。
        /// </summary>
        public virtual Vector2 BaseSize => Vector2.zero;

        /// <summary>
        /// 是否由基类自动构建标准卡片底板与边框 (Auto Create Standard Card Frame)。
        /// 若派生类返回 true，BaseInitialize 会在调用 OnInitialize 之前，
        /// 自动在当前 GameObject 上挂载 Image 与 Outline，赋予 SurfaceStyleRole.CardBg 与 Ghost 细边框，
        /// 并将其作为 "card_frame" 自动纳入 Controls 管理体系。派生类无需手动创建底板与管理边框。
        /// </summary>
        protected virtual bool AutoCreateCardFrame => false;

        /// <summary>
        /// 当 AutoCreateCardFrame 为 true 时采用的卡片语义角色 (Normal / Warning / Danger / Accent)。默认为 Normal。
        /// </summary>
        protected virtual CardStyleRole CardRole => CardStyleRole.Normal;

        /// <summary>
        /// 当 AutoCreateCardFrame 为 true 时由基类自动生成的卡片背景 Image 组件
        /// </summary>
        public Image CardBackground { get; private set; }

        /// <summary>
        /// 当 AutoCreateCardFrame 为 true 时由基类自动生成的卡片边缘 Outline 组件
        /// </summary>
        public Outline CardOutline { get; private set; }

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

        private List<IResetableCache> _autoDiscoveredCaches;

        protected virtual void Awake()
        {
            WidgetDragHandler.OnEditModeChanged += HandleEditModeChanged;
            CacheManager.Instance.RegisterWidgetCache(this);
            DiscoverResetableCaches();
        }

        private void DiscoverResetableCaches()
        {
            if (_autoDiscoveredCaches != null) return;
            var fields = GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (typeof(IResetableCache).IsAssignableFrom(f.FieldType))
                {
                    var cacheObj = f.GetValue(this) as IResetableCache;
                    if (cacheObj != null)
                    {
                        if (_autoDiscoveredCaches == null) _autoDiscoveredCaches = new List<IResetableCache>(8);
                        _autoDiscoveredCaches.Add(cacheObj);
                    }
                }
            }
        }

        public virtual void Teardown()
        {
            CacheManager.Instance.UnregisterWidgetCache(this);
            _autoDiscoveredCaches?.Clear();
            this.Controls.UnregisterAll();
            WidgetDragHandler.OnEditModeChanged -= HandleEditModeChanged;
            if (DragHandler != null)
            {
                DragHandler.Teardown();
            }
            WidgetRenderManager.Instance?.UnregisterWidget(this);
            I18nManager.OnLanguageChanged -= HandleLanguageChanged;
        }

        /// <summary>
        /// 响应 CacheManager 全局瞬态缓存清空广播 (换船/切场景/重置)，触发派生组件私有状态复位
        /// </summary>
        void CacheManager.IWidgetPrivateCache.ResetPrivateCache()
        {
            // 1. 全自动执行所有声明为 Cached / CachedFloat / CachedDouble 等字段的复位
            if (_autoDiscoveredCaches != null)
            {
                for (int i = 0; i < _autoDiscoveredCaches.Count; i++)
                {
                    _autoDiscoveredCaches[i].ResetToDefault();
                }
            }

            // 2. 触发派生类额外自定义复位逻辑
            OnResetPrivateCache();
        }

        /// <summary>
        /// 当换船 (Switch Vessel)、场景卸载、UI 重置或 CacheManager.ClearTransient() 被调用时触发。
        /// 若组件字段已采用 Cached / CachedFloat / CachedDouble 声明，父类已全自动复位，
        /// 派生组件通常完全无需重写此方法（仅在有特异化队列需清空时重写）。
        /// </summary>
        protected virtual void OnResetPrivateCache() { }

        protected virtual void OnDestroy()
        {
            Teardown();
        }

        private void HandleLanguageChanged(string newLang)
        {
            OnLanguageChanged();
        }

        /// <summary>
        /// 当系统界面语言切换时自动触发，派生组件可重写以即刻更新标签、工具提示及备忘文本
        /// </summary>
        protected virtual void OnLanguageChanged() { }

        private void HandleEditModeChanged(bool isEdit)
        {
            if (this == null) return;
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
                SubCanvas.pixelPerfect = true;
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
            if (this == null || gameObject == null || SubCanvas == null) return;
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

        /// <summary>
        /// 派生组件初始化钩子。
        /// 当组件完全由头部声明式 DSL 控件 (TextWidget, ToggleButtonWidget, ActionButtonWidget, LinearBarWidget)
        /// 构成时，基类已自动完成构建与注册，派生类可完全省略重写此方法！
        /// </summary>
        protected virtual void OnInitialize(WidgetConfig config, ThemeConfig theme) { }

        /// <summary>
        /// 当玩家切换视觉主题时统一触发。
        /// 默认实现已全自动将新主题下发给所有注册的微控件 (Controls.ApplyThemeToControls)。
        /// 若组件仅由标准化微控件组成，派生类可完全无需 override 此方法！
        /// </summary>
        public virtual void ApplyTheme(ThemeConfig theme)
        {
            theme = ResolveEffectiveTheme(theme);
            if (theme == null) return;
            this.Controls.ApplyThemeToControls(theme);

            if (CardBackground != null)
            {
                WidgetStyleManager.Instance?.ApplyCardFrame(CardBackground, CardOutline, CardRole, theme);
            }

            // 递归保障：自动扫描组件根节点下所有原生 Text，确保字体与材质 100% 同步
            Text[] texts = GetComponentsInChildren<Text>(true);
            if (texts != null)
            {
                Font activeFont = UIFactory.GetActiveFont(theme);
                Material activeTextMat = WidgetStyleManager.Instance?.GetUiMaterial(isText: true);
                for (int i = 0; i < texts.Length; i++)
                {
                    Text t = texts[i];
                    if (t == null) continue;
                    bool fontChanged = (activeFont != null && t.font != activeFont);
                    if (fontChanged)
                    {
                        t.font = activeFont;
                    }
                    bool matChanged = (t.material != activeTextMat);
                    if (matChanged)
                    {
                        t.material = activeTextMat;
                    }
                    if (fontChanged || matChanged)
                    {
                        t.SetAllDirty();
                    }
                }
            }
        }

        private void AutoBuildDslControls(ThemeConfig theme)
        {
            var fields = GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            float s = CurrentDpiScale;

            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (typeof(IWidgetDslControl).IsAssignableFrom(f.FieldType))
                {
                    var dslCtrl = f.GetValue(this) as IWidgetDslControl;
                    if (dslCtrl != null)
                    {
                        if (dslCtrl.RootGameObject == null)
                        {
                            dslCtrl.Build(this, f.Name, s, theme);
                        }
                        if (this.Controls.Get<IWidgetControl>(dslCtrl.Id) == null)
                        {
                            this.Controls.Register(dslCtrl);
                        }
                    }
                }
            }
        }

        private void AutoRegisterAnnotatedControls()
        {
            var fields = GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                var attrs = f.GetCustomAttributes(typeof(WidgetControlAttribute), true);
                if (attrs != null && attrs.Length > 0)
                {
                    var attr = (WidgetControlAttribute)attrs[0];
                    object val = f.GetValue(this);

                    // 1. 若字段未赋值且显式指定了布局信息：由基类全自动通过 UIFactory 构建并回填字段
                    bool hasLayout = attr.Width > 0f || attr.Height > 0f || Math.Abs(attr.X) > 0.001f || Math.Abs(attr.Y) > 0.001f || attr.DefaultText != null;
                    if (val == null && attr.AutoInstantiate && hasLayout)
                    {
                        if (f.FieldType == typeof(Text))
                        {
                            int sz = Mathf.Max(6, Mathf.RoundToInt(attr.FontSize * s));
                            string initialText = attr.DefaultText ?? attr.Token ?? "---";
                            Text newText = UIFactory.CreateText(transform, attr.Id, initialText, sz, attr.Alignment, style.GetTextColor(attr.TextRole, theme));
                            RectTransform rt = newText.rectTransform;
                            Vector2 cardSz = RectTransform.sizeDelta;
                            float w = attr.Width > 0f ? attr.Width * s : (cardSz.x > 0f ? cardSz.x - 12f * s : 100f * s);
                            float h = attr.Height > 0f ? attr.Height * s : (sz + 6f * s);
                            rt.sizeDelta = new Vector2(w, h);
                            rt.anchoredPosition = new Vector2(attr.X * s, attr.Y * s);
                            f.SetValue(this, newText);
                            val = newText;
                        }
                        else if (f.FieldType == typeof(Image))
                        {
                            Vector2 sz = new Vector2((attr.Width > 0f ? attr.Width : 100f) * s, (attr.Height > 0f ? attr.Height : 6f) * s);
                            Vector2 pos = new Vector2(attr.X * s, attr.Y * s);
                            GameObject imgGo = UIFactory.CreatePanel(transform, attr.Id, sz, pos, style.GetMeterColor(attr.MeterRole, theme));
                            Image newImg = imgGo.GetComponent<Image>();
                            f.SetValue(this, newImg);
                            val = newImg;
                        }
                    }

                    // 2. 注册入 Controls（若尚未注册）
                    if (this.Controls.Get<IWidgetControl>(attr.Id) == null)
                    {
                        if (val is Text text && text != null)
                        {
                            this.Controls.Register(new WidgetReadoutControl(attr.Id, attr.DisplayName, text.gameObject, text, null, attr.TextRole, attr.Token));
                        }
                        else if (val is Image img && img != null)
                        {
                            this.Controls.Register(new WidgetLinearBarControl(attr.Id, attr.DisplayName, img.gameObject, img, null, attr.MeterRole));
                        }
                        else if (val is Button btn && btn != null)
                        {
                            this.Controls.Register(new WidgetActionButtonControl(attr.Id, attr.DisplayName, btn.gameObject, btn, null, null, attr.ButtonRole));
                        }
                        else if (val is GameObject go && go != null)
                        {
                            this.Controls.Register(WidgetControlManager.WrapElement(this, attr.Id, attr.DisplayName, go));
                        }
                    }
                }
            }
        }

        private float _lastHeartBeatTime = -10f;

        private int _heartBeatTick = 0;
        private Dictionary<string, float> _subCadenceTimers;
        private static readonly System.Collections.Generic.Dictionary<string, object> _globalPublishedChannels = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 全局主遥测更新派发调度入口 (Master Telemetry Update Dispatcher)。
        /// 由 WidgetRenderManager 单点阶梯分发，自动执行：
        /// 1. 遥测上下文空值与空船安全拦截 (HasVessel Guard)
        /// 2. 数据心跳节拍判定与专属物理计算派发 (OnDataHeartBeat, 受 EffectiveHeartBeatTier 节流)
        /// 3. 所有已注册微控件的自动化遥测更新 (Controls.UpdateControls)
        /// 4. 纯 UI 渲染/补间帧更新 (OnUpdateRender)
        /// </summary>
        public void MasterUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            float now = Time.unscaledTime;
            float interval = EffectiveHeartBeatInterval;
            float rfInterval = RefreshInterval;
            bool isHeartbeatSameAsRefresh = (interval <= 0.001f && rfInterval <= 0.001f) ||
                                            (Math.Abs(interval - rfInterval) < 0.001f);
            bool shouldHeartBeat = isHeartbeatSameAsRefresh ||
                                   (_lastHeartBeatTime < 0f) ||
                                   (interval <= 0.001f) ||
                                   (now - _lastHeartBeatTime >= interval - 0.0005f);

            if (shouldHeartBeat)
            {
                float dt = _lastHeartBeatTime > 0f ? (now - _lastHeartBeatTime) : Time.unscaledDeltaTime;
                _lastHeartBeatTime = now;
                int tick = _heartBeatTick++;

                var ctx = new FlightHeartbeatContext(telemetry, dt, tick, this);
                OnDataHeartBeat(in ctx);
            }

            // 1. 微控件全自动化遥测更新 (包含通配符 Token 计算与脏检查)
            this.Controls.UpdateControls(telemetry);

            // 2. UI 绘制循环调度器 (UIDrawLoop，含 2D UI Shader 材质管线与姿态球管线)
            ExecuteUIDrawLoop(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 父类统一纳管的 UI 绘制循环主调度器（内嵌帧调度、异常沙箱隔离与管线装配）
        /// </summary>
        public void ExecuteUIDrawLoop(float deltaTime)
        {
            if (!gameObject.activeSelf) return;

            var theme = WidgetStyleManager.Instance?.CurrentTheme;
            var style = WidgetStyleManager.Instance;
            var navball = GetNavballPipeline();
            var drawCtx = new FlightUIDrawContext(deltaTime, this, PreferredDrawPipeline, theme, style, navball);

            try
            {
                OnUIDrawLoop(ref drawCtx);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ModularFlightPanel] Error in {WidgetId}.OnUIDrawLoop: {ex.Message}");
            }
        }

        /// <summary>
        /// 【核心航电 UI 绘制循环契约】由父类严格按 RefreshTier 节律或 AlwaysFullPower 满帧调度的 UI 视觉绘制入口。
        /// 承载 2D UI Shader 材质管线绑定与 3D/矢量姿态球管线驱动。
        /// 默认实现自动回退调用 OnUpdateRender(context.DeltaTime)，确保全库 46 个存量组件 100% 向后兼容。
        /// </summary>
        public virtual void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            if (LogicCore != null)
            {
                OnRenderState();
            }
            OnUpdateRender(context.DeltaTime);
        }

        /// <summary>
        /// 纯 C# 业务解算大脑核心（可选）。
        /// 若子类提供 LogicCore，则数据心跳自动优先执行 LogicCore.Evaluate，UI绘制循环自动执行 OnRenderState() 将纯状态映射至视觉；
        /// 若未提供，则自动回退调用传统的 OnUpdateTelemetry(context.Telemetry)。
        /// </summary>
        protected virtual IWidgetLogic LogicCore => null;

        /// <summary>
        /// 当组件接入 WidgetLogic 时在 OnUIDrawLoop 中触发的状态渲染回调。
        /// 专用于从 LogicCore 的纯结构体状态映射到 UI 控件，零遥测查询，零 GC 分配。
        /// </summary>
        protected virtual void OnRenderState()
        {
        }

        /// <summary>
        /// 允许派生组件在通用右键上下文菜单中注入专属交互行为 (如分级仪简洁/完整模式切换、罗盘模式等)。
        /// </summary>
        public virtual void PopulateContextMenu(Action<string, Action> registerAction)
        {
        }

        /// <summary>
        /// 【核心航电数据心跳契约】由父类严格按 EffectiveHeartBeatTier 节拍调度的专属数据刷新方法。
        /// 专用于执行遥测参数计算、物理量解算、状态机轮询与微控件赋值。
        /// 默认实现自动回退调用 OnUpdateTelemetry(context.Telemetry)，确保全库 46 个存量组件 100% 向后兼容。
        /// </summary>
        public virtual void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            if (LogicCore != null)
            {
                LogicCore.Evaluate(context.Telemetry, context.DeltaTime);
            }
            else
            {
                OnUpdateTelemetry(context.Telemetry);
            }
        }

        /// <summary>
        /// 纯 UI 渲染/平滑补间帧更新钩子（随组件 RefreshTier 满帧触发，不受 HeartBeatTier 节流）。
        /// 常用于补间滚带平滑移动、罗盘平滑旋转等不需要每帧重新计算物理遥测的纯视觉动画。
        /// </summary>
        public virtual void OnUpdateRender(float deltaTime)
        {
        }

        /// <summary>
        /// 派生组件特异化遥测更新钩子。
        /// 默认实现为空。若组件完全由声明式微控件 (Controls / DSL) 构成，派生类可直接省略重写此方法。
        /// </summary>
        public virtual void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
        }

        /// <summary>
        /// 判定指定命名的心跳子通道自上次触发后是否已经过指定秒数 (免子类声明 private float 计时器字段)
        /// </summary>
        public bool CheckChannelElapsed(string channelKey, float intervalSeconds)
        {
            if (string.IsNullOrEmpty(channelKey)) return true;
            if (_subCadenceTimers == null) _subCadenceTimers = new Dictionary<string, float>(4, StringComparer.Ordinal);
            float now = Time.unscaledTime;
            if (!_subCadenceTimers.TryGetValue(channelKey, out float lastTime) || (now - lastTime >= intervalSeconds - 0.0005f))
            {
                _subCadenceTimers[channelKey] = now;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 跨仪表发布自定义计算结果与状态通道，供全机舱其他组件无缝联动读取
        /// </summary>
        public void PublishDataChannel(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_globalPublishedChannels)
            {
                _globalPublishedChannels[key] = value;
            }
        }

        /// <summary>
        /// 尝试读取全机舱由其他组件发布的自定义数据通道
        /// </summary>
        public static bool TryGetPublishedChannel<T>(string key, out T value)
        {
            lock (_globalPublishedChannels)
            {
                if (_globalPublishedChannels.TryGetValue(key, out object obj) && obj is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            value = default;
            return false;
        }

        #region Avionics Evaluation & Computation Helpers

        /// <summary>
        /// <summary>
        /// 便捷通配符字符串求值工具（自动空值回退与安全保护）
        /// </summary>
        public static string EvalToken(string token, IFlightTelemetry telemetry, string fallback = "---")
        {
            if (telemetry == null || string.IsNullOrEmpty(token)) return fallback;
            string res = TelemetryTokenEngine.Evaluate(token, telemetry);
            return string.IsNullOrEmpty(res) ? fallback : res;
        }

        /// <summary>
        /// 便捷数值型通配符求值工具（自动 NaN 保护与安全回退）
        /// </summary>
        public static double EvalNumeric(string token, IFlightTelemetry telemetry, double fallback = 0.0)
        {
            if (telemetry == null || string.IsNullOrEmpty(token)) return fallback;
            double res = TelemetryTokenEngine.EvaluateNumeric(token, telemetry);
            return double.IsNaN(res) ? fallback : res;
        }

        /// <summary>
        /// 便捷数值范围归一化工具 (0.0 ~ 1.0)
        /// </summary>
        public static float NormalizeValue(double val, double min, double max)
        {
            if (double.IsNaN(val)) return 0f;
            double range = max - min;
            if (range <= 0.00001) return 0f;
            return Mathf.Clamp01((float)((val - min) / range));
        }

        #region 航电平滑插值中枢 (Avionics Smoothing & Interpolation Engine)

        /// <summary>
        /// 帧率无关的一阶指数平滑插值 (Framerate-Independent Exponential Smoothing)
        /// 专用于低频遥测心跳 (如 10Hz Standard) 与高频 UI 绘制循环 (60Hz Critical) 之间的平滑衔接，
        /// 彻底消除低频遥测带来的视觉顿挫感。
        /// </summary>
        /// <param name="current">当前平滑数值</param>
        /// <param name="target">遥测目标数值</param>
        /// <param name="speed">收敛速度系数 (推荐 12f~25f，约 0.05~0.1 秒内平滑收敛)</param>
        /// <param name="deltaTime">帧间隔时间 (Time.unscaledDeltaTime)</param>
        /// <param name="deadband">吸附死区阈值，若差距小于该值直接吸附至 target</param>
        public static float Interpolate(float current, float target, float speed = 15f, float deltaTime = 0.02f, float deadband = 0.0001f)
        {
            if (float.IsNaN(current)) return target;
            if (float.IsNaN(target)) return current;
            float diff = target - current;
            if (Mathf.Abs(diff) <= deadband) return target;
            float dt = deltaTime > 0f ? (deltaTime > 0.2f ? 0.2f : deltaTime) : 0.02f;
            float factor = 1.0f - Mathf.Exp(-speed * dt);
            float result = current + diff * factor;
            return Mathf.Abs(target - result) <= deadband ? target : result;
        }

        /// <summary>
        /// 双精度帧率无关一阶指数平滑插值 (Double Precision Exponential Smoothing)
        /// 专用于速度/高度/距离等大动态范围物理遥测数据在 10Hz 与 60Hz 之间的无抖动平滑桥接。
        /// </summary>
        public static double Interpolate(double current, double target, double speed = 15.0, float deltaTime = 0.02f, double deadband = 0.0001)
        {
            if (double.IsNaN(current)) return target;
            if (double.IsNaN(target)) return current;
            double diff = target - current;
            if (Math.Abs(diff) <= deadband) return target;
            float dt = deltaTime > 0f ? (deltaTime > 0.2f ? 0.2f : deltaTime) : 0.02f;
            double factor = 1.0 - Math.Exp(-speed * dt);
            double result = current + diff * factor;
            return Math.Abs(target - result) <= deadband ? target : result;
        }

        /// <summary>
        /// 恒定速率线性平滑插值 (Constant Rate MoveTowards Smoothing)
        /// </summary>
        public static float InterpolateTowards(float current, float target, float maxDeltaPerSec, float deltaTime = 0.02f)
        {
            if (float.IsNaN(current)) return target;
            if (float.IsNaN(target)) return current;
            float dt = deltaTime > 0f ? (deltaTime > 0.2f ? 0.2f : deltaTime) : 0.02f;
            return Mathf.MoveTowards(current, target, maxDeltaPerSec * dt);
        }

        /// <summary>
        /// 双精度恒定速率线性平滑插值 (Double Precision MoveTowards Smoothing)
        /// </summary>
        public static double InterpolateTowards(double current, double target, double maxDeltaPerSec, float deltaTime = 0.02f)
        {
            if (double.IsNaN(current)) return target;
            if (double.IsNaN(target)) return current;
            float dt = deltaTime > 0f ? (deltaTime > 0.2f ? 0.2f : deltaTime) : 0.02f;
            double step = maxDeltaPerSec * dt;
            if (Math.Abs(target - current) <= step) return target;
            return current + Math.Sign(target - current) * step;
        }

        /// <summary>
        /// 二维坐标向量帧率无关平滑插值 (Vector2 Smoothing)
        /// </summary>
        public static Vector2 Interpolate(Vector2 current, Vector2 target, float speed = 15f, float deltaTime = 0.02f)
        {
            return new Vector2(
                Interpolate(current.x, target.x, speed, deltaTime),
                Interpolate(current.y, target.y, speed, deltaTime)
            );
        }

        /// <summary>
        /// 颜色通道平滑插值 (Color Smoothing)
        /// </summary>
        public static Color Interpolate(Color current, Color target, float speed = 15f, float deltaTime = 0.02f)
        {
            float dt = deltaTime > 0f ? (deltaTime > 0.2f ? 0.2f : deltaTime) : 0.02f;
            float t = Mathf.Clamp01(1.0f - Mathf.Exp(-speed * dt));
            return Color.Lerp(current, target, t);
        }

        #endregion

        /// <summary>
        /// 高效文本防抖写入（内容未改变时不触发 UGUI 网格与顶点重建）
        /// </summary>
        protected bool SetText(Text target, string text)
        {
            return SetTextIfChanged(target, text);
        }

        /// <summary>
        /// 高效柱条/进度填充写入（比例未改变时不触发重绘）
        /// </summary>
        protected bool SetBarFill(Image fillImage, float ratio)
        {
            return SetImageFillIfChanged(fillImage, ratio);
        }

        #endregion

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

        #region Universal Semantic Node Builders (Zero Raw GameObject)

        /// <summary>
        /// 创建标准 UGUI 矩形容器节点 (自动注入 RectTransform、归一化 Pivot/Anchor、安全绑定父级)
        /// </summary>
        protected RectTransform CreateContainer(string name, Transform parent = null, Vector2? size = null, Vector2? anchoredPos = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            RectTransform rt = (RectTransform)go.transform;
            if (size.HasValue) rt.sizeDelta = size.Value;
            if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
            return rt;
        }

        /// <summary>
        /// 创建视口裁剪容器 (带 RectMask2D 与 RectTransform)
        /// </summary>
        protected RectTransform CreateViewport(string name, Transform parent = null, Vector2? size = null, Vector2? anchoredPos = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(parent != null ? parent : transform, false);
            RectTransform rt = (RectTransform)go.transform;
            if (size.HasValue) rt.sizeDelta = size.Value;
            if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
            return rt;
        }

        /// <summary>
        /// 创建交互按钮节点 (集成 RectTransform, Image 背景与 Button 交互组件)
        /// </summary>
        protected Button CreateButton(string name, Transform parent, out RectTransform rt, out Image bg, Vector2? size = null, Vector2? anchoredPos = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent != null ? parent : transform, false);
            rt = (RectTransform)go.transform;
            bg = go.GetComponent<Image>();
            if (size.HasValue) rt.sizeDelta = size.Value;
            if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
            return go.GetComponent<Button>();
        }

        /// <summary>
        /// 创建强类型子节点组件 (自动注入 RectTransform 与目标组件类型)
        /// </summary>
        protected TComponent CreateChild<TComponent>(string name, Transform parent = null, Vector2? size = null, Vector2? anchoredPos = null) where TComponent : Component
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TComponent));
            go.transform.SetParent(parent != null ? parent : transform, false);
            RectTransform rt = (RectTransform)go.transform;
            if (size.HasValue) rt.sizeDelta = size.Value;
            if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
            return go.GetComponent<TComponent>();
        }

        /// <summary>
        /// 创建带有附加组件的复合节点
        /// </summary>
        protected GameObject CreateNode(string name, Transform parent = null, params Type[] components)
        {
            Type[] allComponents;
            if (components == null || components.Length == 0)
            {
                allComponents = new[] { typeof(RectTransform) };
            }
            else
            {
                bool hasRt = false;
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == typeof(RectTransform)) { hasRt = true; break; }
                }
                if (!hasRt)
                {
                    allComponents = new Type[components.Length + 1];
                    allComponents[0] = typeof(RectTransform);
                    Array.Copy(components, 0, allComponents, 1, components.Length);
                }
                else
                {
                    allComponents = components;
                }
            }

            GameObject go = new GameObject(name, allComponents);
            go.transform.SetParent(parent != null ? parent : transform, false);
            return go;
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
            string cur = textComponent.text;
            if (object.ReferenceEquals(cur, newText)) return false;
            if (cur != null && cur.Length == newText.Length && string.Equals(cur, newText, StringComparison.Ordinal)) return false;
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
        /// 通用描边颜色脏标记守卫：仅当描边颜色发生实际改变时才写入 Outline.effectColor，
        /// 彻底阻断 UGUI Outline 每帧重复赋值导致的整图元顶点流 (Vertex Stream) 重建与重绘
        /// </summary>
        public static bool SetOutlineColorIfChanged(Outline outline, Color targetColor)
        {
            if (outline == null) return false;
            if (outline.effectColor == targetColor) return false;
            outline.effectColor = targetColor;
            return true;
        }

        /// <summary>
        /// 通用局部四元数旋转脏标记守卫：仅当旋转角度变化大于容差 (默认 0.05°) 时才写入 localRotation，
        /// 彻底阻断微小浮点抖动引发的整颗 UGUI 树矩阵脏标记与 Canvas 重新布局
        /// </summary>
        public static bool SetLocalRotationIfChanged(Transform target, Quaternion newRotation, float angleTolerance = 0.05f)
        {
            if (target == null) return false;
            if (Quaternion.Angle(target.localRotation, newRotation) <= angleTolerance) return false;
            target.localRotation = newRotation;
            return true;
        }

        /// <summary>
        /// 通用局部欧拉角旋转脏标记守卫：仅当欧拉角变化大于容差时才写入 localEulerAngles
        /// </summary>
        public static bool SetLocalEulerAnglesIfChanged(Transform target, Vector3 newEuler, float angleTolerance = 0.05f)
        {
            if (target == null) return false;
            Vector3 cur = target.localEulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(cur.x, newEuler.x)) <= angleTolerance &&
                Mathf.Abs(Mathf.DeltaAngle(cur.y, newEuler.y)) <= angleTolerance &&
                Mathf.Abs(Mathf.DeltaAngle(cur.z, newEuler.z)) <= angleTolerance)
            {
                return false;
            }
            target.localEulerAngles = newEuler;
            return true;
        }

        /// <summary>
        /// 通用 RectTransform 锚点坐标脏标记守卫：仅当位移变化大于容差 (默认 0.05 像素) 时才写入 anchoredPosition
        /// </summary>
        public static bool SetAnchoredPositionIfChanged(RectTransform target, Vector2 newPos, float distanceTolerance = 0.05f)
        {
            if (target == null) return false;
            Vector2 cur = target.anchoredPosition;
            if (Mathf.Abs(cur.x - newPos.x) <= distanceTolerance && Mathf.Abs(cur.y - newPos.y) <= distanceTolerance)
            {
                return false;
            }
            target.anchoredPosition = newPos;
            return true;
        }

        /// <summary>
        /// 通用 RectTransform 尺寸脏标记守卫：仅当尺寸变化大于容差 (默认 0.05 像素) 时才写入 sizeDelta
        /// </summary>
        public static bool SetSizeDeltaIfChanged(RectTransform target, Vector2 newSize, float tolerance = 0.05f)
        {
            if (target == null) return false;
            Vector2 cur = target.sizeDelta;
            if (Mathf.Abs(cur.x - newSize.x) <= tolerance && Mathf.Abs(cur.y - newSize.y) <= tolerance)
            {
                return false;
            }
            target.sizeDelta = newSize;
            return true;
        }

        /// <summary>
        /// 通用 GameObject 显隐脏标记守卫：仅当 activeSelf 与 targetActive 不一致时才调用 SetActive
        /// </summary>
        public static bool SetActiveIfChanged(GameObject target, bool targetActive)
        {
            if (target == null) return false;
            if (target.activeSelf == targetActive) return false;
            target.SetActive(targetActive);
            return true;
        }

        /// <summary>
        /// 通用局部缩放脏标记守卫：仅当缩放变化大于容差时才写入 localScale
        /// </summary>
        public static bool SetScaleIfChanged(Transform target, Vector3 newScale, float tolerance = 0.001f)
        {
            if (target == null) return false;
            Vector3 cur = target.localScale;
            if (Mathf.Abs(cur.x - newScale.x) <= tolerance &&
                Mathf.Abs(cur.y - newScale.y) <= tolerance &&
                Mathf.Abs(cur.z - newScale.z) <= tolerance)
            {
                return false;
            }
            target.localScale = newScale;
            return true;
        }

        #region Hardware Overlay & Static-Dynamic Decoupling Helpers

        /// <summary>
        /// 创建标准硬件覆盖层容器（用于动态指针、航天器图元、轨迹线等动态图元）。
        /// 隔离底层静态几何网格，所有动态移动纯靠硬件 Transform 偏移驱动，0 CPU 网格重建与 Canvas 脏化。
        /// </summary>
        public GameObject CreateHardwareOverlayLayer(string name = "HardwareOverlay")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        /// <summary>
        /// 创建标准硬件动态标记图元 (Image + RectTransform)
        /// </summary>
        public Image CreateHardwareMarker(Transform parent, string name, Vector2 size, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.material = WidgetStyleManager.Instance?.GetUiMaterial(isText: false);
            img.color = color;
            if (sprite != null) img.sprite = sprite;
            return img;
        }

        #endregion

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

        #region Standardized Avionics Formatting & Threshold Evaluators

        /// <summary>
        /// 标准秒数持续时间格式化 ("hh:mm:ss" 或 "mm:ss")
        /// </summary>
        public static string FormatDuration(double seconds) => AvionicsFormatting.FormatDuration(seconds);

        /// <summary>
        /// 紧凑型飞行时序格式化（例如 "12d", "04:15:30", "08:42"）
        /// </summary>
        public static string FormatDurationCompact(double seconds) => AvionicsFormatting.FormatDurationCompact(seconds);

        /// <summary>
        /// 标准机动/任务倒计时格式化（支持正负时序，如 "T-01:23", "T+00:45"）
        /// </summary>
        public static string FormatCountdown(double seconds, string prefix = "T-") => AvionicsFormatting.FormatCountdown(seconds, prefix);

        /// <summary>
        /// 国际单位制 (SI) 距离自适应量程格式化 (Gm / Mm / km / m)
        /// </summary>
        public static string FormatMetricDistance(double meters, string format = "F1") => AvionicsFormatting.FormatMetricDistance(meters, format);

        /// <summary>
        /// 国际单位制 (SI) 速度自适应量程格式化 (km/s / m/s)
        /// </summary>
        public static string FormatMetricSpeed(double mps, string format = "F1") => AvionicsFormatting.FormatMetricSpeed(mps, format);

        /// <summary>
        /// 标准告警阈值求值器：将数值与注意/警告阈值比对，返回标准语义文本样式角色 (Normal / Warning / Danger)
        /// </summary>
        public static TextStyleRole EvaluateThresholdRole(double val, double cautionThresh, double warningThresh, bool lowerIsWorse = false)
        {
            if (double.IsNaN(val)) return TextStyleRole.PrimaryValue;
            if (lowerIsWorse)
            {
                if (val <= warningThresh) return TextStyleRole.Danger;
                if (val <= cautionThresh) return TextStyleRole.Warning;
                return TextStyleRole.PrimaryValue;
            }
            else
            {
                if (val >= warningThresh) return TextStyleRole.Danger;
                if (val >= cautionThresh) return TextStyleRole.Warning;
                return TextStyleRole.PrimaryValue;
            }
        }

        /// <summary>
        /// 标准卡片告警阈值求值器：将数值与注意/警告阈值比对，返回标准卡片样式角色 (Normal / Warning / Danger)
        /// </summary>
        public static CardStyleRole EvaluateCardRole(double val, double cautionThresh, double warningThresh, bool lowerIsWorse = false)
        {
            if (double.IsNaN(val)) return CardStyleRole.Normal;
            if (lowerIsWorse)
            {
                if (val <= warningThresh) return CardStyleRole.Danger;
                if (val <= cautionThresh) return CardStyleRole.Warning;
                return CardStyleRole.Normal;
            }
            else
            {
                if (val >= warningThresh) return CardStyleRole.Danger;
                if (val >= cautionThresh) return CardStyleRole.Warning;
                return CardStyleRole.Normal;
            }
        }

        /// <summary>
        /// 标准度量条告警阈值求值器：将数值与注意/警告阈值比对，返回度量条样式角色 (Primary / Secondary / Warning)
        /// </summary>
        public static MeterStyleRole EvaluateMeterRole(double val, double cautionThresh, double warningThresh, bool lowerIsWorse = false)
        {
            if (double.IsNaN(val)) return MeterStyleRole.Primary;
            if (lowerIsWorse)
            {
                if (val <= warningThresh) return MeterStyleRole.Warning;
                if (val <= cautionThresh) return MeterStyleRole.Warning;
                return MeterStyleRole.Primary;
            }
            else
            {
                if (val >= warningThresh) return MeterStyleRole.Warning;
                if (val >= cautionThresh) return MeterStyleRole.Warning;
                return MeterStyleRole.Primary;
            }
        }

        #endregion

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
                    MFPProfiler.BeginWidgetSample(WidgetId, DisplayName);
                    try
                    {
                        MasterUpdateTelemetry(telem);
                    }
                    finally
                    {
                        MFPProfiler.EndWidgetSample(WidgetId);
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
        public void UpdateTransform(float? x = null, float? y = null, float? scale = null, float? rotation = null, float? scaleX = null, float? scaleY = null)
        {
            if (Config != null)
            {
                if (x.HasValue) Config.PositionX = x.Value;
                if (y.HasValue) Config.PositionY = y.Value;
                if (scale.HasValue)
                {
                    Config.Scale = Mathf.Clamp(scale.Value, 0.2f, 4.0f);
                    Config.ScaleX = Config.Scale;
                    Config.ScaleY = Config.Scale;
                }
                if (scaleX.HasValue) Config.ScaleX = Mathf.Clamp(scaleX.Value, 0.2f, 4.0f);
                if (scaleY.HasValue) Config.ScaleY = Mathf.Clamp(scaleY.Value, 0.2f, 4.0f);
                if (rotation.HasValue) Config.Rotation = (rotation.Value % 360f + 360f) % 360f;
            }

            if (RectTransform != null)
            {
                if (x.HasValue || y.HasValue)
                {
                    float curX = x ?? Config?.PositionX ?? RectTransform.anchoredPosition.x;
                    float curY = y ?? Config?.PositionY ?? RectTransform.anchoredPosition.y;

                    var rm = WidgetRenderManager.Instance;
                    float canvasScale = rm != null ? rm.GetCanvasScaleFactor() : 1.0f;
                    if (canvasScale < 0.01f) canvasScale = 1.0f;
                    float halfScreenW = (Screen.width / canvasScale) * 0.5f;
                    float canvasH = Screen.height / canvasScale;
                    float hudRootY = 215f * (FlightHUDManager.Instance?.CustomScale ?? 1.0f);
                    float clampRangeX = Mathf.Max(400f, halfScreenW + 100f);
                    float minClampY = -hudRootY - 100f;
                    float maxClampY = (canvasH - hudRootY) + 100f;
                    curX = Mathf.Clamp(curX, -clampRangeX, clampRangeX);
                    curY = Mathf.Clamp(curY, minClampY, maxClampY);

                    if (Config != null)
                    {
                        Config.PositionX = curX;
                        Config.PositionY = curY;
                    }
                    SetAnchoredPositionIfChanged(RectTransform, new Vector2(curX, curY));
                }
                if (scale.HasValue || scaleX.HasValue || scaleY.HasValue)
                {
                    float effX = Config != null ? Config.EffectiveScaleX : 1.0f;
                    float effY = Config != null ? Config.EffectiveScaleY : 1.0f;

                    if ((this is IAdaptiveSizeWidget adaptive) && adaptive.AllowNonUniformScale)
                    {
                        RectTransform.localScale = Vector3.one;
                        if (BaseSize.x > 0f && BaseSize.y > 0f)
                        {
                            float factorX = CommittedScale > 0.001f ? (effX / CommittedScale) : 1.0f;
                            float factorY = CommittedScale > 0.001f ? (effY / CommittedScale) : 1.0f;
                            Vector2 newSize = new Vector2(BaseSize.x * CurrentDpiScale * factorX, BaseSize.y * CurrentDpiScale * factorY);
                            float minW = adaptive.MinBaseSize.x * CurrentDpiScale;
                            float minH = adaptive.MinBaseSize.y * CurrentDpiScale;
                            float maxW = Mathf.Max(minW, adaptive.MaxBaseSize.x * CurrentDpiScale * 4f);
                            float maxH = Mathf.Max(minH, adaptive.MaxBaseSize.y * CurrentDpiScale * 4f);
                            newSize.x = Mathf.Clamp(newSize.x, minW, maxW);
                            newSize.y = Mathf.Clamp(newSize.y, minH, maxH);
                            RectTransform.sizeDelta = newSize;
                            adaptive.OnAdaptiveResize(newSize);
                        }
                    }
                    else
                    {
                        float ratioX = CommittedScale > 0.001f ? (effX / CommittedScale) : effX;
                        float ratioY = CommittedScale > 0.001f ? (effY / CommittedScale) : effY;
                        RectTransform.localScale = new Vector3(ratioX, ratioY, 1.0f);
                        OnScaleChanged((effX + effY) * 0.5f, (ratioX + ratioY) * 0.5f);
                    }
                }
                if (rotation.HasValue)
                {
                    float r = Config != null ? Config.Rotation : 0f;
                    SetLocalEulerAnglesIfChanged(RectTransform, new Vector3(0f, 0f, r));
                }
            }

            if (DragHandler != null)
            {
                DragHandler.UpdateSelectionAppearance();
            }
        }

        /// <summary>
        /// 当组件缩放动态变动时触发 (例如编辑模式实时手柄拖拽)
        /// </summary>
        /// <param name="targetScale">目标总缩放倍率 (例如 1.5x)</param>
        /// <param name="relativeRatio">相对初始化固化尺寸的比例 (例如 1.5 / 1.0 = 1.5)</param>
        protected virtual void OnScaleChanged(float targetScale, float relativeRatio)
        {
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (Config != null) Config.IsEnabled = visible;
            WidgetRenderManager.Instance?.SetWidgetActive(this, visible);
        }

        #region Zero-Allocation Template Channel Caching

        private readonly System.Collections.Generic.Dictionary<string, string> _templateChannelCache =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _cachedCustomTemplateRaw = null;

        /// <summary>
        /// 零 GC 结构化通配符通道提取器：一次性将 CustomTemplate（例如 EPR={THR};N1={ENG:N1}）
        /// 解析并缓存在组件内部，供 OnUpdateTelemetry 纳秒级高频查表，杜绝每秒数百次 Split(';') 托管堆垃圾分配。
        /// </summary>
        public string GetTemplateChannel(string key, string fallback = "")
        {
            EnsureTemplateChannelsParsed();
            if (_templateChannelCache.TryGetValue(key, out string val))
            {
                return val;
            }
            return fallback;
        }

        /// <summary>
        /// 支持多个别名键名的结构化通配符通道提取器（按顺序首个匹配即返回）
        /// </summary>
        public string GetTemplateChannel(string[] aliases, string fallback = "")
        {
            if (aliases == null || aliases.Length == 0) return fallback;
            EnsureTemplateChannelsParsed();
            for (int i = 0; i < aliases.Length; i++)
            {
                if (_templateChannelCache.TryGetValue(aliases[i], out string val))
                {
                    return val;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 零 GC 结构化浮点数通道提取器 (带 InvariantCulture 解析保护)
        /// </summary>
        public double GetTemplateChannelDouble(string key, double fallback = 0.0)
        {
            string val = GetTemplateChannel(key, null);
            if (!string.IsNullOrEmpty(val) && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return fallback;
        }

        public double GetTemplateChannelDouble(string[] aliases, double fallback = 0.0)
        {
            string val = GetTemplateChannel(aliases, null);
            if (!string.IsNullOrEmpty(val) && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return fallback;
        }

        /// <summary>
        /// 零 GC 结构化单精度浮点数通道提取器
        /// </summary>
        public float GetTemplateChannelFloat(string key, float fallback = 0.0f)
        {
            string val = GetTemplateChannel(key, null);
            if (!string.IsNullOrEmpty(val) && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            return fallback;
        }

        public float GetTemplateChannelFloat(string[] aliases, float fallback = 0.0f)
        {
            string val = GetTemplateChannel(aliases, null);
            if (!string.IsNullOrEmpty(val) && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            return fallback;
        }

        /// <summary>
        /// 零 GC 结构化整数通道提取器
        /// </summary>
        public int GetTemplateChannelInt(string key, int fallback = 0)
        {
            string val = GetTemplateChannel(key, null);
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            return fallback;
        }

        public int GetTemplateChannelInt(string[] aliases, int fallback = 0)
        {
            string val = GetTemplateChannel(aliases, null);
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            return fallback;
        }

        /// <summary>
        /// 零 GC 结构化布尔通道提取器 (支持 true/false, 1/0, yes/no, on/off)
        /// </summary>
        public bool GetTemplateChannelBool(string key, bool fallback = false)
        {
            string val = GetTemplateChannel(key, null);
            if (!string.IsNullOrEmpty(val))
            {
                if (bool.TryParse(val, out bool b)) return b;
                if (val == "1" || val.Equals("yes", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
                if (val == "0" || val.Equals("no", StringComparison.OrdinalIgnoreCase) || val.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return fallback;
        }

        public bool GetTemplateChannelBool(string[] aliases, bool fallback = false)
        {
            string val = GetTemplateChannel(aliases, null);
            if (!string.IsNullOrEmpty(val))
            {
                if (bool.TryParse(val, out bool b)) return b;
                if (val == "1" || val.Equals("yes", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
                if (val == "0" || val.Equals("no", StringComparison.OrdinalIgnoreCase) || val.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return fallback;
        }

        public void InvalidateTemplateChannels()
        {
            _cachedCustomTemplateRaw = null;
            _templateChannelCache.Clear();
        }

        private void EnsureTemplateChannelsParsed()
        {
            string raw = Config?.CustomTemplate;
            if (raw == _cachedCustomTemplateRaw) return;
            _cachedCustomTemplateRaw = raw;
            _templateChannelCache.Clear();
            if (string.IsNullOrEmpty(raw)) return;

            string[] pairs = raw.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string v = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : string.Empty;
                    if (k.Length > 0)
                    {
                        _templateChannelCache[k] = v;
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// 标准微控件声明特性 (Widget Control Declarative Annotation)
    /// 在组件的 UGUI 字段/属性（Text, Image, Button, GameObject）上标记此特性后，
    /// 基类 BaseInitialize 会在 OnInitialize 执行完毕后自动通过反射完成微控件注册，
    /// 派生组件彻底无需手写一行 Register、BindConfig 或 ApplyTheme！
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class WidgetControlAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }
        public TextStyleRole TextRole { get; set; } = TextStyleRole.PrimaryValue;
        public MeterStyleRole MeterRole { get; set; } = MeterStyleRole.Primary;
        public ButtonVisualRole ButtonRole { get; set; } = ButtonVisualRole.Normal;
        public string Token { get; set; }

        // 头部集中式布局属性 (全自动由基类完成 DPI 缩放与实例化注入)
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float Width { get; set; } = 0f;
        public float Height { get; set; } = 0f;
        public float FontSize { get; set; } = 10f;
        public TextAnchor Alignment { get; set; } = TextAnchor.MiddleLeft;
        public string DefaultText { get; set; } = null;
        public bool AutoInstantiate { get; set; } = true;

        public WidgetControlAttribute(string id, string displayName = null)
        {
            Id = id;
            DisplayName = displayName ?? id;
        }

        public WidgetControlAttribute(string id, TextStyleRole textRole, string displayName = null)
        {
            Id = id;
            TextRole = textRole;
            DisplayName = displayName ?? id;
        }

        public WidgetControlAttribute(string id, MeterStyleRole meterRole, string displayName = null)
        {
            Id = id;
            MeterRole = meterRole;
            DisplayName = displayName ?? id;
        }

        public WidgetControlAttribute(string id, ButtonVisualRole buttonRole, string displayName = null)
        {
            Id = id;
            ButtonRole = buttonRole;
            DisplayName = displayName ?? id;
        }
    }

    /// <summary>
    /// 通用航电卡片组件兼容基类 (向后兼容保留，新组件建议直接继承 BaseFlightWidget)
    /// </summary>
    [Obsolete("BaseFlightWidget 已全面内置航电算子与默认遥测更新，新组件推荐直接继承 BaseFlightWidget。")]
    public abstract class BaseAvionicsWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(160f, 50f);
        protected override bool AutoCreateCardFrame => true;
    }

    #region Zero-GC Component Private State Helpers

    /// <summary>
    /// 可自动复位的组件私有缓存契约接口
    /// </summary>
    public interface IResetableCache
    {
        void ResetToDefault();
    }

    /// <summary>
    /// 全自动纳管私有缓存包装器 (通用类型：bool, int, enum, string 等)。
    /// 声明为组件私有字段后，BaseFlightWidget 会在切船/重置时全自动调用复位，组件无需手写任何重置代码！
    /// </summary>
    public class Cached<T> : IResetableCache
    {
        private T _value;
        private readonly T _defaultValue;

        public Cached(T initial = default)
        {
            _value = initial;
            _defaultValue = initial;
        }

        public T Value
        {
            get => _value;
            set => _value = value;
        }

        public bool Update(T next)
        {
            if (EqualityComparer<T>.Default.Equals(_value, next))
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(T value = default)
        {
            _value = value;
        }

        public void ResetToDefault()
        {
            _value = _defaultValue;
        }

        public static implicit operator T(Cached<T> c) => c != null ? c._value : default;
        public override string ToString() => _value != null ? _value.ToString() : string.Empty;
    }

    /// <summary>
    /// 全自动纳管私有浮点死区缓存包装器 (带容差死区过滤)。
    /// 声明为组件私有字段后，BaseFlightWidget 会在切船/重置时全自动调用复位，组件无需手写任何重置代码！
    /// </summary>
    public class CachedFloat : IResetableCache
    {
        private float _value;
        private readonly float _defaultValue;
        private readonly float _tolerance;

        public CachedFloat(float initial = 0f, float tolerance = 0.001f)
        {
            _value = initial;
            _defaultValue = initial;
            _tolerance = tolerance;
        }

        public float Value
        {
            get => _value;
            set => _value = value;
        }

        public bool Update(float next)
        {
            if (Math.Abs(next - _value) <= _tolerance)
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(float value)
        {
            _value = value;
        }

        public void ResetToDefault()
        {
            _value = _defaultValue;
        }

        public static implicit operator float(CachedFloat c) => c != null ? c._value : 0f;
        public override string ToString() => _value.ToString();
    }

    /// <summary>
    /// 全自动纳管私有双精度浮点死区缓存包装器 (带容差死区过滤)。
    /// 声明为组件私有字段后，BaseFlightWidget 会在切船/重置时全自动调用复位，组件无需手写任何重置代码！
    /// </summary>
    public class CachedDouble : IResetableCache
    {
        private double _value;
        private readonly double _defaultValue;
        private readonly double _tolerance;

        public CachedDouble(double initial = 0.0, double tolerance = 0.001)
        {
            _value = initial;
            _defaultValue = initial;
            _tolerance = tolerance;
        }

        public double Value
        {
            get => _value;
            set => _value = value;
        }

        public bool Update(double next)
        {
            if (Math.Abs(next - _value) <= _tolerance)
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(double value)
        {
            _value = value;
        }

        public void ResetToDefault()
        {
            _value = _defaultValue;
        }

        public static implicit operator double(CachedDouble c) => c != null ? c._value : 0.0;
        public override string ToString() => _value.ToString();
    }

    /// <summary>
    /// 零 GC 通用值脏检查结构体槽位 (适合高性能局部值比较)
    /// </summary>
    public struct DirtyField<T> : IEquatable<DirtyField<T>>
    {
        private T _value;

        public DirtyField(T initial)
        {
            _value = initial;
        }

        public T Value => _value;

        public bool Update(T next)
        {
            if (EqualityComparer<T>.Default.Equals(_value, next))
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(T value = default)
        {
            _value = value;
        }

        public bool Equals(DirtyField<T> other) => EqualityComparer<T>.Default.Equals(_value, other._value);
        public override bool Equals(object obj) => obj is DirtyField<T> other && Equals(other);
        public override int GetHashCode() => _value != null ? EqualityComparer<T>.Default.GetHashCode(_value) : 0;
        public static implicit operator T(DirtyField<T> field) => field._value;
    }

    /// <summary>
    /// 零 GC 浮点数死区脏检查结构体槽位 (带绝对容差过滤，阻断高频微动重绘)
    /// </summary>
    public struct DirtyFloat : IEquatable<DirtyFloat>
    {
        private float _value;
        private readonly float _tolerance;

        public DirtyFloat(float initial, float tolerance = 0.001f)
        {
            _value = initial;
            _tolerance = tolerance;
        }

        public float Value => _value;

        public bool Update(float next)
        {
            if (Math.Abs(next - _value) <= _tolerance)
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(float value = 0f)
        {
            _value = value;
        }

        public bool Equals(DirtyFloat other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is DirtyFloat other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public static implicit operator float(DirtyFloat field) => field._value;
    }

    /// <summary>
    /// 零 GC 双精度死区脏检查结构体槽位 (带绝对容差过滤，阻断高频微动重绘)
    /// </summary>
    public struct DirtyDouble : IEquatable<DirtyDouble>
    {
        private double _value;
        private readonly double _tolerance;

        public DirtyDouble(double initial, double tolerance = 0.001)
        {
            _value = initial;
            _tolerance = tolerance;
        }

        public double Value => _value;

        public bool Update(double next)
        {
            if (Math.Abs(next - _value) <= _tolerance)
            {
                return false;
            }
            _value = next;
            return true;
        }

        public void Reset(double value = 0.0)
        {
            _value = value;
        }

        public bool Equals(DirtyDouble other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is DirtyDouble other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public static implicit operator double(DirtyDouble field) => field._value;
    }

    #endregion
}

