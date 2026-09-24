using System;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版分级操作与交互中枢契约 (Stock Staging Action & Interaction Contract)
    /// 彻底解耦原版 StageManager、StageGroup、StageIcon 以及 Part 对象的场景依赖，
    /// 允许在游戏运行时 (Flight / Editor) 与离线无头仿真环境中统一执行分级增删、拖拽调序、部件跨级移动与高亮。
    /// </summary>
    public interface IStockStageActionProvider
    {
        /// <summary>
        /// 当前是否允许修改分级序列 (在飞行中或航天制造厂中有效)
        /// </summary>
        bool CanModifyStaging { get; }

        /// <summary>
        /// 当前分级安全锁是否已锁定 (Alt+L / StagingLock)
        /// </summary>
        bool IsStagingLocked { get; }

        /// <summary>
        /// 在指定索引处插入新分级 (例如在 S02 上方插入新级)
        /// </summary>
        /// <param name="stageIndex">目标分级编号</param>
        void InsertStage(int stageIndex);

        /// <summary>
        /// 删除指定分级 (特别是空分级或用户指定移除的分级)
        /// </summary>
        /// <param name="stageIndex">要删除的分级编号</param>
        void DeleteStage(int stageIndex);

        /// <summary>
        /// 调整分级顺序 (将整级重排)
        /// </summary>
        /// <param name="fromStage">源分级编号</param>
        /// <param name="toStage">目标分级编号</param>
        void MoveStage(int fromStage, int toStage);

        /// <summary>
        /// 将指定部件及其对称体跨级移动至新分级
        /// </summary>
        /// <param name="partFlightId">部件的全局唯一 flightID / craftID</param>
        /// <param name="fromStage">源分级编号</param>
        /// <param name="partIndex">源分级中的部件列表索引</param>
        /// <param name="targetStage">目标分级编号</param>
        void MovePartToStage(uint partFlightId, int fromStage, int partIndex, int targetStage);

        /// <summary>
        /// 在 3D 游戏场景中高亮/取消高亮该部件及其对称体
        /// </summary>
        /// <param name="partFlightId">部件的全局唯一 flightID / craftID</param>
        /// <param name="highlight">是否高亮</param>
        /// <param name="highlightColor">高亮颜色 (默认青色/高亮绿)</param>
        void SetPartHighlight(uint partFlightId, bool highlight, Color? highlightColor = null);

        /// <summary>
        /// 清除载具上所有部件的高亮状态
        /// </summary>
        void ClearAllHighlights();

        /// <summary>
        /// 触发执行下一级分级操作 (Activate Next Stage)
        /// </summary>
        void ActivateNextStage();

        /// <summary>
        /// 切换分级安全锁状态 (Toggle Staging Lock)
        /// </summary>
        void ToggleStagingLock();
    }

    /// <summary>
    /// 原版分级操作与交互中枢服务总线 (Stock Staging Action Service Bus)
    /// </summary>
    public static class StockStageActionService
    {
        public static IStockStageActionProvider Provider { get; set; }

        public static bool CanModifyStaging => Provider?.CanModifyStaging ?? true;
        public static bool IsStagingLocked => Provider?.IsStagingLocked ?? false;

        public static void InsertStage(int stageIndex) => Provider?.InsertStage(stageIndex);
        public static void DeleteStage(int stageIndex) => Provider?.DeleteStage(stageIndex);
        public static void MoveStage(int fromStage, int toStage) => Provider?.MoveStage(fromStage, toStage);
        public static void MovePartToStage(uint partFlightId, int fromStage, int partIndex, int targetStage)
            => Provider?.MovePartToStage(partFlightId, fromStage, partIndex, targetStage);
        public static void SetPartHighlight(uint partFlightId, bool highlight, Color? highlightColor = null)
            => Provider?.SetPartHighlight(partFlightId, highlight, highlightColor);
        public static void ClearAllHighlights() => Provider?.ClearAllHighlights();
        public static void ActivateNextStage() => Provider?.ActivateNextStage();
        public static void ToggleStagingLock() => Provider?.ToggleStagingLock();
    }
}
