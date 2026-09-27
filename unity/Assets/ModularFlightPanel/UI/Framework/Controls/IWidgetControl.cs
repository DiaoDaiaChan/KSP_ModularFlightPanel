using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 组件内部控件大类 (Widget Control Categories)
    /// </summary>
    public enum WidgetControlCategory
    {
        Header,         // 标题栏、副标题、顶部分割线与状态徽标
        Readout,        // 读数框、等宽数值窗、单位角标
        LinearGauge,    // 线性柱条、进度条、标尺轨道
        ArcGauge,       // 极坐标弧形条、ECAM/SpaceX 度量环
        NeedlePointer,  // 动态旋转指针、导引针
        ActionButton,   // 交互开关、多态药丸按钮、控制键
        Annunciator,    // 光字牌、告警警示灯珠
        Viewport,       // 2D剪影、雷达对准光环、三维球视口
        DataStack,      // 垂直推进栈行、表格行、分级列表
        ModeCapsule,    // 模式切换胶囊
        TrendBar,       // 动力学趋势指示条
        GenericElement, // 通用命名子元素
        Misc            // 其他自定义零件
    }

    /// <summary>
    /// 标准状态光字牌状态枚举
    /// </summary>
    public enum AnnunciatorState
    {
        Off,
        Normal,
        Caution,
        Warning
    }

    /// <summary>
    /// 声明式微控件在航电卡片内的语义泊靠槽位 (Semantic Docking Slots)
    /// </summary>
    public enum WidgetDock
    {
        Custom,      // 显式指定坐标与长宽 (X, Y, Width, Height)
        TopLeft,     // 顶部左侧 (标准卡片标题 / 标签)
        TopRight,    // 顶部右侧 (状态徽标 / 辅助角标)
        Center,      // 居中大显示区 (核心数值读数)
        Bottom,      // 底部水平通栏 (柱状计量槽 / 进度条)
        BottomLeft,  // 底部左侧 (次要数值 / 状态文本)
        BottomRight, // 底部右侧 (工程单位 / 辅助标签)
        Fill         // 全面积视口填充
    }

    /// <summary>
    /// 标准化组件控件契约 (Standardized Widget Control Interface)
    /// 暴露独立的 Id、RootGameObject 与 RectTransform。
    /// 
    /// TODO: [DetailedEditMode] 自由编辑模式（详细下钻拖拽与独立排版）：
    /// 未来版本可在此扩展支持深入组件内部的局部自由拖拽排版 (Component Isolation Mode)。
    /// 架构设计要点：
    /// 1. 交互下钻：双击选中小组件进入微控件编辑层，射线检测由 BaseFlightWidget 切换为下属各 IWidgetControl；
    /// 2. 局部变换操纵：复用 WidgetDragHandler 的位移计算，直接更新 IWidgetControl.RectTransform.anchoredPosition；
    /// 3. 位置持久化：通过 WidgetConfig.SubElementTransforms (如 "id:x,y;id2:x,y") 或 CustomTemplate 的 "<ID>_POS=x,y" 存储。
    /// </summary>
    public interface IWidgetControl
    {
        string Id { get; }
        string DisplayName { get; }
        WidgetControlCategory Category { get; }
        bool IsVisible { get; set; }
        GameObject RootGameObject { get; }
        RectTransform RectTransform { get; }
        Vector2 DefaultAnchoredPosition { get; }
        Vector2 CurrentOffset { get; }
        void ApplyOffset(Vector2 offset);
        void ResetOffset();
        void ApplyTheme(ThemeConfig theme);
        void UpdateTelemetry(IFlightTelemetry telemetry);
        void BindConfig(WidgetConfig config);
    }

    /// <summary>
    /// 标准化声明式 DSL 控件契约 (Standardized Declarative DSL Control Interface)
    /// 允许在派生小组件的类头部直接通过 new 声明实例字段，
    /// 由基类 BaseInitialize 通过反射自省自动感知、构建 UGUI 渲染节点并纳管至 Controls。
    /// 彻底消除 CS0649 警告、冗长特性参数与空的 OnInitialize 样板代码。
    /// </summary>
    public interface IWidgetDslControl : IWidgetControl
    {
        void Build(BaseFlightWidget parent, string fieldName, float dpiScale, ThemeConfig theme);
    }

    /// <summary>
    /// 标准控件抽象基类
    /// </summary>
    public abstract class BaseWidgetControl : IWidgetControl
    {
        public string Id { get; protected set; }
        public string DisplayName { get; protected set; }
        public WidgetControlCategory Category { get; protected set; }
        public GameObject RootGameObject { get; protected set; }
        public RectTransform RectTransform => RootGameObject != null ? RootGameObject.GetComponent<RectTransform>() : null;

        protected Vector2 _defaultAnchoredPosition = Vector2.zero;
        protected bool _defaultPosCaptured = false;
        public Vector2 DefaultAnchoredPosition
        {
            get
            {
                CaptureDefaultPosition();
                return _defaultAnchoredPosition;
            }
        }

        protected Vector2 _currentOffset = Vector2.zero;
        public Vector2 CurrentOffset => _currentOffset;

        protected void CaptureDefaultPosition()
        {
            if (!_defaultPosCaptured && RectTransform != null)
            {
                _defaultAnchoredPosition = RectTransform.anchoredPosition;
                _defaultPosCaptured = true;
            }
        }

        public virtual void ApplyOffset(Vector2 offset)
        {
            CaptureDefaultPosition();
            _currentOffset = offset;
            if (RectTransform != null)
            {
                RectTransform.anchoredPosition = _defaultAnchoredPosition + offset;
            }
        }

        public virtual void ResetOffset()
        {
            ApplyOffset(Vector2.zero);
        }

        private bool _isVisible = true;
        public virtual bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                if (RootGameObject != null && RootGameObject.activeSelf != value)
                {
                    RootGameObject.SetActive(value);
                }
            }
        }

        public BaseFlightWidget ParentWidget { get; protected set; }

        protected BaseWidgetControl(BaseFlightWidget parent, string id, string displayName, WidgetControlCategory category, GameObject rootGo)
        {
            ParentWidget = parent;
            Id = id ?? "control";
            DisplayName = displayName ?? Id;
            Category = category;
            RootGameObject = rootGo;
            CaptureDefaultPosition();
        }

        protected BaseWidgetControl(string id, string displayName, WidgetControlCategory category, GameObject rootGo)
            : this(null, id, displayName, category, rootGo)
        {
        }

        public abstract void ApplyTheme(ThemeConfig theme);
        public abstract void UpdateTelemetry(IFlightTelemetry telemetry);

        public virtual void BindConfig(WidgetConfig config)
        {
            CaptureDefaultPosition();
            if (config != null)
            {
                if (config.IsSubElementDisabled(Id))
                {
                    IsVisible = false;
                }
                else
                {
                    IsVisible = true;
                }
                Vector2 offset = config.GetSubElementOffset(Id);
                ApplyOffset(offset);
            }
        }
    }
}
