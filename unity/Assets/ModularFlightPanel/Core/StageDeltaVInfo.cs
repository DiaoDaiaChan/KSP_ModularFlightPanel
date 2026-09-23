using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 单级火箭动力与燃烧遥测数据模型 (Pure C# Contract)
    /// 无论来自 MechJeb2 (VacStats/AtmoStats)、原版 1.12+ (VesselDeltaV) 还是离线仿真引擎，
    /// 均映射为此标准化数据结构，彻底解耦。
    /// </summary>
    public struct StageDeltaVInfo
    {
        public int Stage;          // 级数编号 (如 0, 1, 2...)
        public double DeltaV;      // 该级可用 Delta-V (m/s)
        public double BurnTime;    // 该级发动机全推力工作时间 (秒)
        public double TWR;         // 该级起步/平均推重比
        public double Isp;         // 该级比冲 (秒)
        public bool IsActive;      // 是否为当前正在工作的激活级

        public StageDeltaVInfo(int stage, double dv, double burnTime, double twr = 0.0, double isp = 0.0, bool isActive = false)
        {
            Stage = stage;
            DeltaV = dv;
            BurnTime = burnTime;
            TWR = twr;
            Isp = isp;
            IsActive = isActive;
        }
    }
}
