using System;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 无头离线仿真与自动化测试专用的分级操作挂钩器 (Headless Stage Action Hook)
    /// 允许在完全无真实 KSP 游戏进程的环境中执行完整的添加、删除、拖拽排序与部件跨级移动模拟。
    /// </summary>
    public class HeadlessStageActionHook : IStockStageActionProvider
    {
        private readonly TelemetrySimulationEngine _simEngine;

        public HeadlessStageActionHook(TelemetrySimulationEngine simEngine = null)
        {
            _simEngine = simEngine;
        }

        public bool CanModifyStaging => true;

        public bool IsStagingLocked => _simEngine != null && _simEngine.IsStageLocked;

        public void InsertStage(int stageIndex)
        {
            if (_simEngine != null)
            {
                _simEngine.InsertSimulatedStage(stageIndex);
            }
            Debug.Log($"[HeadlessStageActionHook] Inserted simulated stage at S{stageIndex:00}");
        }

        public void DeleteStage(int stageIndex)
        {
            if (_simEngine != null)
            {
                _simEngine.DeleteSimulatedStage(stageIndex);
            }
            Debug.Log($"[HeadlessStageActionHook] Deleted simulated stage at S{stageIndex:00}");
        }

        public void MoveStage(int fromStage, int toStage)
        {
            if (_simEngine != null)
            {
                _simEngine.MoveSimulatedStage(fromStage, toStage);
            }
            Debug.Log($"[HeadlessStageActionHook] Reordered simulated stage from S{fromStage:00} to S{toStage:00}");
        }

        public void MovePartToStage(uint partFlightId, int fromStage, int partIndex, int targetStage)
        {
            if (_simEngine != null)
            {
                _simEngine.MoveSimulatedPartToStage(partFlightId, fromStage, partIndex, targetStage);
            }
            Debug.Log($"[HeadlessStageActionHook] Moved simulated part {partFlightId} (from S{fromStage} idx {partIndex}) to S{targetStage:00}");
        }

        public void SetPartHighlight(uint partFlightId, bool highlight, Color? highlightColor = null)
        {
            // 无头模式下模拟记录高亮
        }

        public void ClearAllHighlights()
        {
        }

        public void ActivateNextStage()
        {
            if (_simEngine != null)
            {
                _simEngine.ActivateNextStage();
            }
            Debug.Log("[HeadlessStageActionHook] Activated next simulated stage");
        }

        public void ToggleStagingLock()
        {
            if (_simEngine != null)
            {
                _simEngine.ToggleStageLock();
            }
            Debug.Log($"[HeadlessStageActionHook] Toggled stage lock. Now: {IsStagingLocked}");
        }
    }
}
