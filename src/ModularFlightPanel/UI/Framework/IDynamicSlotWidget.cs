using System.Collections.Generic;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 动态数据槽位元数据描述符 (Dynamic Data Slot Descriptor)
    /// 统一抽象多列/多行横幅与数据卡片的单元格定义
    /// </summary>
    public class DynamicSlotDescriptor
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Token { get; set; }
        public bool HasSeparator { get; set; }
        public float CustomWidth { get; set; }
        public string SlotType { get; set; }

        public DynamicSlotDescriptor() { }

        public DynamicSlotDescriptor(string id, string title, string token, bool hasSeparator = true, float customWidth = 0f, string slotType = "readout")
        {
            Id = id;
            Title = title;
            Token = token;
            HasSeparator = hasSeparator;
            CustomWidth = customWidth;
            SlotType = slotType;
        }
    }

    /// <summary>
    /// 声明式动态增删行列/槽位组件契约接口 (Dynamic Slot & Column Flow Contract)
    /// 用于支持用户在编辑模式自由增删数据列、调整顺序、开关分隔线与自定义装配 736+ 参数的组件。
    /// 解耦具体组件实现，避免单一组件上帝类化，使顶栏、底栏、侧边仪表柱等均可复用统一的槽位编排协议。
    /// </summary>
    public interface IDynamicSlotWidget
    {
        /// <summary>
        /// 槽位编排器描述标题 (如 "动态槽位横幅模式: 可自由加减数据列、调整顺序与分隔线")
        /// </summary>
        string SlotOrchestratorTitle { get; }

        /// <summary>
        /// 当前激活的动态槽位只读列表
        /// </summary>
        IReadOnlyList<DynamicSlotDescriptor> DynamicSlots { get; }

        /// <summary>
        /// 添加新数据槽位/列
        /// </summary>
        void AddDynamicSlot(string token, string title = null);

        /// <summary>
        /// 移除指定索引的槽位/列
        /// </summary>
        void RemoveDynamicSlot(int index);

        /// <summary>
        /// 调整槽位顺序
        /// </summary>
        void MoveDynamicSlot(int fromIndex, int toIndex);

        /// <summary>
        /// 切换指定槽位的分隔线显隐
        /// </summary>
        void ToggleDynamicSlotSeparator(int index);

        /// <summary>
        /// 更新指定槽位绑定的遥测通配符 Token
        /// </summary>
        void UpdateDynamicSlotToken(int index, string newToken);

        /// <summary>
        /// 更新指定槽位的显示标题
        /// </summary>
        void UpdateDynamicSlotTitle(int index, string newTitle);

        /// <summary>
        /// 恢复出厂默认槽位布局
        /// </summary>
        void ResetToDefaultDynamicSlots();
    }
}
