using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 标准化机载飞行控制指令接口 (Pure Unity / C# 契约)
    /// 将双向操控指令与只读遥测数据解耦，落实接口隔离原则 (ISP)。
    /// 操控型仪表 (SAS罗盘、分级控制台、时钟加速等) 面向此接口工作。
    /// </summary>
    public interface IFlightControl
    {
        // 飞行姿态与 SAS / RCS 控制
        void SetSASMode(FlightSASMode mode);
        void ToggleSAS();
        void ToggleRCS();
        void CycleSpeedMode();

        // 分级与飞行模式
        void ActivateNextStage();
        void ToggleStageLock();
        void TogglePrecisionMode();
        void ToggleFlightMode();

        // 机动节点
        void WarpToManeuverNode();
        void DeleteManeuverNode();

        // 时间加速与暂停控制
        void IncreaseTimeWarp();
        void DecreaseTimeWarp();
        void CancelTimeWarp();
        void TogglePause();
        void SetTimeWarpRateIndex(int index);
    }
}
