using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Widgets.Navigation;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// UI 绘制循环管线类型
    /// </summary>
    public enum UIDrawPipelineKind
    {
        /// <summary>标准 2D UI 矢量/着色器管线（覆盖绝大多数飞行仪表）</summary>
        UiShader2D,
        /// <summary>3D / 矢量姿态球屏幕空间光线投射与离屏相机管线</summary>
        NavballSphere3D,
        /// <summary>自定义绘制管线</summary>
        Custom
    }

    /// <summary>
    /// 【核心航电 UI 绘制循环契约】(IUIDrawLoop)
    /// 强制每个飞行仪表组件必须包含统一的 UI 渲染入口。
    /// 由父类 BaseFlightWidget 严格按 RefreshTier 节律与微秒帧预算切片主分发调度。
    /// 内置 2D UI Shader 渲染管线与 3D/矢量姿态球管线，
    /// 彻底实现「数据解算 (DataHeartBeat)」与「视觉渲染 (UIDrawLoop)」正交解耦。
    /// </summary>
    public interface IUIDrawLoop
    {
        /// <summary>
        /// UI 绘制循环入口（由父类在 RefreshTier 渲染帧驱动）
        /// 承载 2D UI Shader 材质管线绑定与 3D 姿态球管线驱动。
        /// </summary>
        void OnUIDrawLoop(ref FlightUIDrawContext context);
    }

    /// <summary>
    /// 2D UI 着色器渲染管线 (FlightUiShader2DPipeline)
    /// 集中管控与向具体 UGUI 控件挂载当前主题与全局配置匹配的 GPU 2D 着色器材质。
    /// </summary>
    public struct FlightUiShader2DPipeline
    {
        public ThemeConfig Theme;
        public WidgetStyleManager Style;
        public UiShaderStyle CurrentStyle => Theme != null ? Theme.UiStyle : UiShaderStyle.Modern_Glass;
        public bool IsGpuAccelerationEnabled => ThemeManager.Instance == null || ThemeManager.Instance.EnableGpu2DUIAcceleration;

        public FlightUiShader2DPipeline(ThemeConfig theme, WidgetStyleManager style)
        {
            Theme = theme;
            Style = style;
        }

        /// <summary>
        /// 获取针对当前主题配置生成的 UI 面板 / 文字专用 Material
        /// </summary>
        public Material GetMaterial(bool isText = false)
        {
            return Style != null ? Style.GetUiMaterial(isText) : null;
        }

        /// <summary>
        /// 便捷挂载 2D UI 着色器材质至 Graphic（自动比对材质避免冗余脏更新）
        /// </summary>
        public void ApplyUiMaterial(Graphic graphic, bool isText = false)
        {
            if (graphic == null) return;
            Material mat = GetMaterial(isText);
            if (graphic.material != mat)
            {
                graphic.material = mat;
            }
        }

        /// <summary>
        /// 便捷挂载高保真文字着色器材质至 Text
        /// </summary>
        public void ApplyTextMaterial(Text text)
        {
            ApplyUiMaterial(text, true);
        }

        /// <summary>
        /// 获取当前主题的标准文字角色颜色
        /// </summary>
        public Color GetTextColor(TextStyleRole role)
        {
            return Style != null ? Style.GetTextColor(role, Theme) : Color.white;
        }

        /// <summary>
        /// 获取当前主题的标准表面角色颜色
        /// </summary>
        public Color GetSurfaceColor(SurfaceStyleRole role)
        {
            return Style != null ? Style.GetSurfaceColor(role, Theme) : Color.clear;
        }

        /// <summary>
        /// 获取当前主题的标准线条角色颜色
        /// </summary>
        public Color GetLineColor(LineWeight weight)
        {
            return Style != null ? Style.GetLineColor(weight, Theme) : Color.white;
        }
    }

    /// <summary>
    /// 3D / 矢量导航球绘制管线 (FlightNavballPipeline)
    /// 统一纳管屏幕空间光线投射 (Screen-Space Raymarching) Uniform 参数注入与 3D 正交相机离屏渲染。
    /// </summary>
    public struct FlightNavballPipeline
    {
        public Material SphereMaterial;
        public RenderTexture TargetTexture;
        public Camera OffscreenCamera;
        public bool IsRenderDirty;
        public int OptimalResolution;
        public BaseNavballSphereWidget NavballWidget;

        public FlightNavballPipeline(Material sphereMat, RenderTexture targetRt, Camera offscreenCam, bool isDirty, int optRes, BaseNavballSphereWidget widget = null)
        {
            SphereMaterial = sphereMat;
            TargetTexture = targetRt;
            OffscreenCamera = offscreenCam;
            IsRenderDirty = isDirty;
            OptimalResolution = optRes;
            NavballWidget = widget;
        }

        /// <summary>
        /// 标记姿态球视口与网格需要重绘
        /// </summary>
        public void MarkDirty()
        {
            NavballWidget?.MarkRenderDirty();
        }

        /// <summary>
        /// 驱动 3D 离屏正交摄像机执行一次渲染并将脏标记复位
        /// </summary>
        public void RenderCamera()
        {
            if (OffscreenCamera != null && TargetTexture != null && TargetTexture.IsCreated())
            {
                OffscreenCamera.Render();
                if (NavballWidget != null) NavballWidget.MarkRenderClean();
            }
        }

        /// <summary>
        /// 向姿态球片元着色器安全注入四元数旋转向量 (Vector4: x, y, z, w)
        /// </summary>
        public void SetShaderRotation(int propId, Quaternion rotation)
        {
            if (SphereMaterial != null && propId != 0)
            {
                SphereMaterial.SetVector(propId, new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
            }
        }

        /// <summary>
        /// 向姿态球片元着色器安全注入四维向量 (Vector4)
        /// </summary>
        public void SetShaderVector(int propId, Vector4 value)
        {
            if (SphereMaterial != null && propId != 0)
            {
                SphereMaterial.SetVector(propId, value);
            }
        }

        /// <summary>
        /// 向姿态球片元着色器安全注入颜色 (Color)
        /// </summary>
        public void SetShaderColor(int propId, Color color)
        {
            if (SphereMaterial != null && propId != 0)
            {
                SphereMaterial.SetColor(propId, color);
            }
        }

        /// <summary>
        /// 向姿态球片元着色器安全注入浮点标量 (float)
        /// </summary>
        public void SetShaderFloat(int propId, float value)
        {
            if (SphereMaterial != null && propId != 0)
            {
                SphereMaterial.SetFloat(propId, value);
            }
        }
    }

    /// <summary>
    /// UI 绘制循环上下文参数包（强类型栈分配结构体，零 GC 托管堆分配）
    /// 专用于由父类按 RefreshTier 阶梯或 AlwaysFullPower 满帧直通调度的视觉更新。
    /// 内置 2D UI Shader 渲染管线 (Shader2D) 与 3D/矢量姿态球管线 (Navball)。
    /// </summary>
    public struct FlightUIDrawContext
    {
        /// <summary>当前帧渲染步长 (未缩放秒数)</summary>
        public float DeltaTime;

        /// <summary>当前未缩放绝对时间戳</summary>
        public float UnscaledTime;

        /// <summary>当前 Unity 渲染帧序号</summary>
        public int FrameCount;

        /// <summary>画布全局 1080p 缩放系数</summary>
        public float CanvasScaleFactor;

        /// <summary>当前正在调度执行的宿主小组件引用</summary>
        public BaseFlightWidget Widget;

        /// <summary>当前组件首选的绘制管线类型</summary>
        public UIDrawPipelineKind PipelineKind;

        /// <summary>当前激活的航电主题配置快照</summary>
        public ThemeConfig Theme;

        /// <summary>全局 UI 控件统一样式管理器</summary>
        public WidgetStyleManager Style;

        /// <summary>2D UI 着色器渲染管线句柄</summary>
        public FlightUiShader2DPipeline Shader2D;

        /// <summary>3D/矢量姿态球绘制管线句柄</summary>
        public FlightNavballPipeline Navball;

        /// <summary>当前 UI 绘制循环是否处于静息节能模式</summary>
        public bool IsResting;

        /// <summary>当前绘制帧是否触发了实质性视觉变动</summary>
        public bool HasVisualChanges;

        public FlightUIDrawContext(float dt, BaseFlightWidget widget, UIDrawPipelineKind pipelineKind, ThemeConfig theme, WidgetStyleManager style, FlightNavballPipeline navball = default, bool isResting = false)
        {
            DeltaTime = dt;
            UnscaledTime = Time.unscaledTime;
            FrameCount = Time.frameCount;
            CanvasScaleFactor = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.GetCanvasScaleFactor() : 1.0f;
            Widget = widget;
            PipelineKind = pipelineKind;
            Theme = theme;
            Style = style;
            Shader2D = new FlightUiShader2DPipeline(theme, style);
            Navball = navball;
            IsResting = isResting;
            HasVisualChanges = false;
        }

        /// <summary>
        /// 标记当前绘制帧产生了视觉改变并请求继续刷新
        /// </summary>
        public void MarkDirty(string reason = null)
        {
            HasVisualChanges = true;
            Widget?.MarkVisualDirty(reason);
        }

        /// <summary>
        /// 立即唤醒当前组件的绘制循环
        /// </summary>
        public void WakeUp(string reason = null)
        {
            HasVisualChanges = true;
            Widget?.Awaken(reason);
        }

        /// <summary>
        /// 便捷挂载 2D UI 着色器材质至 Graphic
        /// </summary>
        public void ApplyUiMaterial(Graphic graphic, bool isText = false)
        {
            Shader2D.ApplyUiMaterial(graphic, isText);
        }

        /// <summary>
        /// 便捷挂载高保真文字着色器材质至 Text
        /// </summary>
        public void ApplyTextMaterial(Text text)
        {
            Shader2D.ApplyTextMaterial(text);
        }

        /// <summary>
        /// 获取针对当前主题配置生成的 UI 面板 / 文字专用 Material
        /// </summary>
        public Material GetUiMaterial(bool isText = false)
        {
            return Shader2D.GetMaterial(isText);
        }

        /// <summary>
        /// 递归遍历指定节点树并挂载合规着色器材质
        /// </summary>
        public void ApplyShaderToHierarchy(GameObject root)
        {
            if (root == null) return;
            var graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null) continue;
                ApplyUiMaterial(g, g is Text);
            }
        }

        /// <summary>
        /// 请求姿态球或组件标记重绘
        /// </summary>
        public void RequestRepaint()
        {
            if (Navball.NavballWidget != null)
            {
                Navball.MarkDirty();
            }
        }
    }
}
