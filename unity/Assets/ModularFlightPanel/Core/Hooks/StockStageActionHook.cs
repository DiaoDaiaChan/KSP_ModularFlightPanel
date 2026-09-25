using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版 StageManager 分级操作深度挂钩器 (Stock Stage Action Hook)
    /// 1. 深度接管分级的新增 (Add/Insert)、删除 (Delete)、全级调序与部件跨级拖拽移动；
    /// 2. 联动 3D 游戏场景中零件与对称体的高保真轮廓高亮 (SetHighlight) 与视线聚焦；
    /// 3. 提供分级安全锁切换 (Alt+L / StagingLock) 与空格触发分级 (ActivateNextStage)。
    /// </summary>
    public class StockStageActionHook : IStockStageActionProvider
    {
        public bool CanModifyStaging
        {
            get
            {
#if KSP_RUNTIME
                return (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null) ||
                       (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null);
#else
                return true;
#endif
            }
        }

        public bool IsStagingLocked
        {
            get
            {
#if KSP_RUNTIME
                if (FlightInputHandler.fetch != null)
                {
                    return FlightInputHandler.fetch.stageLock;
                }
#endif
                return false;
            }
        }

        public void InsertStage(int stageIndex)
        {
#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    int target = Mathf.Max(0, stageIndex);
                    KSP.UI.Screens.StageManager.Instance.AddStageAt(target);
                    KSP.UI.Screens.StageManager.Instance.SortIcons(false);
                    Debug.Log($"[ModularFlightPanel] Inserted new stage at index {target}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] InsertStage error: {ex.Message}");
            }
#endif
        }

        public void DeleteStage(int stageIndex)
        {
#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    var stages = mgr.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var grp = stages[i];
                            int stgIdx = (grp != null && grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                            if (grp != null && (stgIdx == stageIndex || grp.defaultStage == stageIndex))
                            {
                                mgr.DeleteStage(grp, true);
                                mgr.SortIcons(false);
                                mgr.UpdateStageGroups(false);
                                Debug.Log($"[ModularFlightPanel] Deleted stage S{stageIndex:00}");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] DeleteStage error: {ex.Message}");
            }
#endif
        }

        public void MoveStage(int fromStage, int toStage)
        {
#if KSP_RUNTIME
            try
            {
                if (fromStage == toStage) return;
                var parts = GetCurrentVesselParts();
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var p = parts[i];
                        if (p == null) continue;
                        if (p.inverseStage == fromStage)
                        {
                            p.inverseStage = toStage;
                        }
                        else if (fromStage < toStage && p.inverseStage > fromStage && p.inverseStage <= toStage)
                        {
                            p.inverseStage--;
                        }
                        else if (fromStage > toStage && p.inverseStage < fromStage && p.inverseStage >= toStage)
                        {
                            p.inverseStage++;
                        }
                    }
                }
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    KSP.UI.Screens.StageManager.Instance.SortIcons(false);
                    KSP.UI.Screens.StageManager.Instance.UpdateStageGroups(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] MoveStage error: {ex.Message}");
            }
#endif
        }

        public void MovePartToStage(uint partFlightId, int fromStage, int partIndex, int targetStage)
        {
#if KSP_RUNTIME
            try
            {
                Part targetPart = null;
                if (partFlightId > 0)
                {
                    targetPart = FindPartById(partFlightId);
                }

                // 若未按 flightID 查找到，则按原版 StageManager 顺序查找
                if (targetPart == null && KSP.UI.Screens.StageManager.Instance != null)
                {
                    var stages = KSP.UI.Screens.StageManager.Instance.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var grp = stages[i];
                            int stgIdx = (grp != null && grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                            if (grp != null && (stgIdx == fromStage || grp.defaultStage == fromStage) && grp.Icons != null && partIndex >= 0 && partIndex < grp.Icons.Count)
                            {
                                var icon = grp.Icons[partIndex];
                                if (icon != null && icon.Part != null)
                                {
                                    targetPart = icon.Part;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (targetPart != null)
                {
                    targetPart.inverseStage = targetStage;
                    if (targetPart.symmetryCounterparts != null)
                    {
                        for (int s = 0; s < targetPart.symmetryCounterparts.Count; s++)
                        {
                            var sym = targetPart.symmetryCounterparts[s];
                            if (sym != null) sym.inverseStage = targetStage;
                        }
                    }

                    if (KSP.UI.Screens.StageManager.Instance != null)
                    {
                        KSP.UI.Screens.StageManager.Instance.SortIcons(false);
                        KSP.UI.Screens.StageManager.Instance.UpdateStageGroups(false);
                    }
                    Debug.Log($"[ModularFlightPanel] Moved part '{targetPart.name}' to Stage S{targetStage:00}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] MovePartToStage error: {ex.Message}");
            }
#endif
        }

        public void SetPartHighlight(uint partFlightId, bool highlight, Color? highlightColor = null)
        {
#if KSP_RUNTIME
            try
            {
                if (partFlightId == 0) return;
                Part p = FindPartById(partFlightId);
                if (p != null)
                {
                    Color col = highlightColor ?? Color.cyan;
                    if (highlight)
                    {
                        p.SetHighlightColor(col);
                        p.SetHighlight(true, false);
                    }
                    else
                    {
                        p.SetHighlightDefault();
                        p.SetHighlight(false, false);
                    }

                    if (p.symmetryCounterparts != null)
                    {
                        for (int s = 0; s < p.symmetryCounterparts.Count; s++)
                        {
                            var sym = p.symmetryCounterparts[s];
                            if (sym != null)
                            {
                                if (highlight)
                                {
                                    sym.SetHighlightColor(col);
                                    sym.SetHighlight(true, false);
                                }
                                else
                                {
                                    sym.SetHighlightDefault();
                                    sym.SetHighlight(false, false);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif
        }

        public void ClearAllHighlights()
        {
#if KSP_RUNTIME
            try
            {
                var parts = GetCurrentVesselParts();
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var p = parts[i];
                        if (p != null)
                        {
                            p.SetHighlightDefault();
                            p.SetHighlight(false, false);
                        }
                    }
                }
            }
            catch { }
#endif
        }

        public void ActivateNextStage()
        {
#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    KSP.UI.Screens.StageManager.ActivateNextStage();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ActivateNextStage error: {ex.Message}");
            }
#endif
        }

        public void ToggleStagingLock()
        {
#if KSP_RUNTIME
            try
            {
                if (FlightInputHandler.fetch != null)
                {
                    FlightInputHandler.fetch.stageLock = !FlightInputHandler.fetch.stageLock;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ToggleStagingLock error: {ex.Message}");
            }
#endif
        }

#if KSP_RUNTIME
        private static List<Part> GetCurrentVesselParts()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
            {
                return FlightGlobals.ActiveVessel.parts;
            }
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                return EditorLogic.fetch.ship.parts;
            }
            return null;
        }

        private static Part FindPartById(uint id)
        {
            var parts = GetCurrentVesselParts();
            if (parts == null) return null;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p != null && (p.flightID == id || p.craftID == id || (uint)p.persistentId == id))
                {
                    return p;
                }
            }
            return null;
        }
#endif
    }
}
