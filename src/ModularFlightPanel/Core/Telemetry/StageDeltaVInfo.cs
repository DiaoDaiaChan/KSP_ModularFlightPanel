using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 单级部件图标与推进剂状态数据模型 (Pure Unity / C# Contract)
    /// 解耦原版 KSP StageIcon、ProtoStageIcon 与 DefaultIcons 枚举
    /// </summary>
    public struct StagePartIconData
    {
        public string IconType;           // 部件图标类型 (如 "SOLID_BOOSTER", "LIQUID_ENGINE", "DECOUPLER_VERT", "DECOUPLER_HOR", "PARACHUTES", "COMMAND_POD")
        public int IconTypeIndex;         // DefaultIcons 索引编号 (如 2: LIQUID_ENGINE, 3: SOLID_BOOSTER, 5: DECOUPLER_VERT 等)
        public int Count;                 // 部件对称/数量倍率 (如 1, 2, 4, 6)
        public string PartTitle;          // 部件显示名称 (如 "BACC 'Thumper' 固体助推器")
        public string PropellantName;     // 推进剂类型名称 (如 "Solid Fuel", "Liquid Fuel", 若无则为 null)
        public float PropellantFraction;  // 推进剂余量比例 (0.0 ~ 1.0, 若非推进部件则为 -1.0)
        public Rect StockUvRect;          // 原版 StageIcon 贴图图集 UV 矩形
        public bool HasStockUv;           // 是否包含原版有效 UV 坐标
        public uint PartFlightId;         // 部件全局唯一 ID (flightID / craftID)，用于场景高亮与跨级移动

        public StagePartIconData(string iconType, int iconTypeIndex, int count, string partTitle = "", string propName = null, float propFrac = -1f, Rect stockUv = default, bool hasStockUv = false, uint partFlightId = 0)
        {
            IconType = iconType;
            IconTypeIndex = iconTypeIndex;
            Count = count > 0 ? count : 1;
            PartTitle = partTitle ?? string.Empty;
            PropellantName = propName;
            PropellantFraction = propFrac;
            StockUvRect = stockUv;
            HasStockUv = hasStockUv;
            PartFlightId = partFlightId;
        }
    }

    /// <summary>
    /// 单级火箭动力与燃烧遥测数据模型 (Pure C# Contract)
    /// 无论来自 MechJeb2 (VacStats/AtmoStats)、原版 1.12+ (VesselDeltaV) 还是离线仿真引擎，
    /// 均映射为此标准化数据结构，彻底解耦。
    /// </summary>
    public struct StageDeltaVInfo
    {
        public int Stage;                                    // 级数编号 (如 0, 1, 2...)
        public double DeltaV;                                // 该级可用 Delta-V (m/s)
        public double BurnTime;                              // 该级发动机全推力工作时间 (秒)
        public double TWR;                                   // 该级起步/平均推重比
        public double Isp;                                   // 该级比冲 (秒)
        public bool IsActive;                                // 是否为当前正在工作的激活级
        public IReadOnlyList<StagePartIconData> PartIcons;   // 该级触发的部件图标列表

        public StageDeltaVInfo(int stage, double dv, double burnTime, double twr = 0.0, double isp = 0.0, bool isActive = false, IReadOnlyList<StagePartIconData> partIcons = null)
        {
            Stage = stage;
            DeltaV = dv;
            BurnTime = burnTime;
            TWR = twr;
            Isp = isp;
            IsActive = isActive;
            PartIcons = partIcons ?? Array.Empty<StagePartIconData>();
        }
    }
}
