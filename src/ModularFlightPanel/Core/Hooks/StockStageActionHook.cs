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
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.InsertSimulatedStage(stageIndex);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    var stages = mgr.Stages;
                    if (stages == null) return;

                    int target = Mathf.Clamp(stageIndex, 0, stages.Count);

                    mgr.IncrementCurrentStage();
                    mgr.AddStageAt(target);
                    mgr.SetManualStageOffset(target);
                    KSP.UI.Screens.StageManager.SetSeparationIndices();
                    GameEvents.StageManager.OnGUIStageAdded.Fire(target);
                    GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
                    TelemetryHub.Instance?.InvalidateStagePartIcons();
                    Debug.Log($"[ModularFlightPanel] Inserted new stage at index {target} (requested {stageIndex})");
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
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.DeleteSimulatedStage(stageIndex);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    var stages = mgr.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var grp = stages[i];
                            if (grp == null) continue;
                            int stgIdx = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                            if (stgIdx == stageIndex || grp.defaultStage == stageIndex)
                            {
                                mgr.DecrementCurrentStage();
                                mgr.DeleteStage(grp, mgr.Visible);
                                mgr.SetManualStageOffset(stgIdx);
                                KSP.UI.Screens.StageManager.SetSeparationIndices();
                                GameEvents.StageManager.OnGUIStageRemoved.Fire(stgIdx);
                                GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
                                TelemetryHub.Instance?.InvalidateStagePartIcons();
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
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.MoveSimulatedStage(fromStage, toStage);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    var stages = mgr.Stages;
                    if (stages != null)
                    {
                        KSP.UI.Screens.StageGroup srcGroup = null;
                        KSP.UI.Screens.StageGroup dstGroup = null;
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var grp = stages[i];
                            if (grp != null)
                            {
                                int idx = grp.inverseStageIndex >= 0 ? grp.inverseStageIndex : i;
                                if (idx == fromStage || grp.defaultStage == fromStage) srcGroup = grp;
                                if (idx == toStage || grp.defaultStage == toStage) dstGroup = grp;
                            }
                        }

                        if (srcGroup != null && dstGroup != null)
                        {
                            int dstListIndex = stages.IndexOf(dstGroup);
                            int dstSiblingIndex = dstGroup.transform.GetSiblingIndex();
                            mgr.RemoveStage(srcGroup);
                            mgr.InsertStageAt(srcGroup, dstListIndex, dstSiblingIndex);
                            srcGroup.transform.localScale = Vector3.one;
                            srcGroup.SetManualStageOffset();
                            dstGroup.SetManualStageOffset();
                            KSP.UI.Screens.StageManager.SetSeparationIndices();
                            GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
                            TelemetryHub.Instance?.InvalidateStagePartIcons();
                        }
                    }
                }
                Debug.Log($"[ModularFlightPanel] Moved stage S{fromStage:00} -> S{toStage:00}");
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
                if (fromStage == targetStage) return;
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.MoveSimulatedPartToStage(partFlightId, fromStage, partIndex, targetStage);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance == null) return;
                var mgr = KSP.UI.Screens.StageManager.Instance;
                var stages = mgr.Stages;
                if (stages == null) return;

                KSP.UI.Screens.StageGroup srcGroup = null;
                KSP.UI.Screens.StageGroup dstGroup = null;
                for (int i = 0; i < stages.Count; i++)
                {
                    var grp = stages[i];
                    if (grp == null) continue;
                    int idx = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                    if (idx == fromStage || grp.defaultStage == fromStage) srcGroup = grp;
                    if (idx == targetStage || grp.defaultStage == targetStage) dstGroup = grp;
                }

                if (srcGroup == null)
                {
                    Debug.LogWarning($"[ModularFlightPanel] MovePartToStage: source stage S{fromStage:00} not found");
                    return;
                }

                if (dstGroup == null)
                {
                    InsertStage(targetStage);
                    stages = mgr.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var grp = stages[i];
                            if (grp == null) continue;
                            int idx = (grp.inverseStageIndex >= 0) ? grp.inverseStageIndex : i;
                            if (idx == targetStage || grp.defaultStage == targetStage) { dstGroup = grp; break; }
                        }
                        if (dstGroup == null && stages.Count > 0)
                        {
                            int clampIdx = Mathf.Clamp(targetStage, 0, stages.Count - 1);
                            dstGroup = stages[clampIdx];
                        }
                    }
                }

                if (dstGroup == null) return;

                KSP.UI.Screens.StageIcon targetIcon = null;
                KSP.UI.Screens.StageIcon parentLead = null;
                var srcIcons = srcGroup.Icons;

                if (srcIcons != null)
                {
                    if (partFlightId > 0)
                    {
                        for (int j = 0; j < srcIcons.Count; j++)
                        {
                            var icon = srcIcons[j];
                            if (icon == null) continue;
                            if (icon.Part != null && (icon.Part.flightID == partFlightId || icon.Part.craftID == partFlightId || (uint)icon.Part.persistentId == partFlightId))
                            {
                                targetIcon = icon;
                                break;
                            }
                            if (icon.groupedIcons != null)
                            {
                                for (int g = 0; g < icon.groupedIcons.Count; g++)
                                {
                                    var child = icon.groupedIcons[g];
                                    if (child != null && child.Part != null && (child.Part.flightID == partFlightId || child.Part.craftID == partFlightId || (uint)child.Part.persistentId == partFlightId))
                                    {
                                        targetIcon = child;
                                        parentLead = icon;
                                        break;
                                    }
                                }
                                if (targetIcon != null) break;
                            }
                        }
                    }

                    if (targetIcon == null && partIndex >= 0 && partIndex < srcIcons.Count)
                    {
                        targetIcon = srcIcons[partIndex];
                    }
                }

                if (targetIcon == null)
                {
                    Debug.LogWarning($"[ModularFlightPanel] MovePartToStage: could not find icon for part {partFlightId} in S{fromStage:00}");
                    return;
                }

                if (parentLead != null)
                {
                    parentLead.RemoveFromGroup(targetIcon, true);
                    dstGroup.AddIcon(targetIcon, true);
                }
                else
                {
                    srcGroup.RemoveIcon(targetIcon, true);
                    dstGroup.AddIcon(targetIcon, true);

                    if (targetIcon.grouped && targetIcon.groupedIcons != null)
                    {
                        for (int g = 0; g < targetIcon.groupedIcons.Count; g++)
                        {
                            var child = targetIcon.groupedIcons[g];
                            if (child != null)
                            {
                                child.Stage = dstGroup;
                                child.SetInverseSequenceIndex(dstGroup.inverseStageIndex, child.InStageIndex, true);
                            }
                        }
                    }
                }

                srcGroup.UpdateInStageIndexes();
                dstGroup.UpdateInStageIndexes();
                srcGroup.SetPartIndices(true);
                dstGroup.SetPartIndices(true);
                srcGroup.SetManualStageOffset();
                dstGroup.SetManualStageOffset();
                KSP.UI.Screens.StageManager.SetSeparationIndices();
                GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
                TelemetryHub.Instance?.InvalidateStagePartIcons();

                Debug.Log($"[ModularFlightPanel] Moved part '{targetIcon.Part?.name ?? targetIcon.name}' to Stage S{dstGroup.inverseStageIndex:00}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] MovePartToStage error: {ex.Message}");
            }
#endif
        }

        public void InsertStageAndMovePart(uint partFlightId, int fromStage, int partIndex, int insertAtStageIndex)
        {
#if KSP_RUNTIME
            try
            {
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.InsertSimulatedStage(insertAtStageIndex);
                    TelemetryHub.Instance.SimulationEngine.MoveSimulatedPartToStage(partFlightId, fromStage, partIndex, insertAtStageIndex);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance != null && KSP.UI.Screens.StageManager.Instance.Stages != null)
                {
                    int target = Mathf.Clamp(insertAtStageIndex, 0, KSP.UI.Screens.StageManager.Instance.Stages.Count);
                    InsertStage(target);
                    MovePartToStage(partFlightId, fromStage, partIndex, target);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] InsertStageAndMovePart error: {ex.Message}");
            }
#endif
        }

        public void ReorderPartInStage(uint partFlightId, int stage, int fromIndex, int toIndex)
        {
#if KSP_RUNTIME
            try
            {
                if (fromIndex == toIndex) return;
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.ReorderSimulatedPartInStage(partFlightId, stage, fromIndex, toIndex);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance == null) return;
                var mgr = KSP.UI.Screens.StageManager.Instance;
                var stages = mgr.Stages;
                if (stages == null) return;

                KSP.UI.Screens.StageGroup group = null;
                for (int i = 0; i < stages.Count; i++)
                {
                    var grp = stages[i];
                    if (grp != null && (grp.inverseStageIndex == stage || grp.defaultStage == stage))
                    {
                        group = grp;
                        break;
                    }
                }

                if (group != null && group.Icons != null && fromIndex >= 0 && fromIndex < group.Icons.Count)
                {
                    var icon = group.Icons[fromIndex];
                    int safeTo = Mathf.Clamp(toIndex, 0, group.Icons.Count - 1);
                    group.RemoveIcon(icon, true);
                    group.AddIconAt(icon, safeTo, -1, true);
                    group.UpdateInStageIndexes();
                    group.SetPartIndices(true);
                    group.SetManualStageOffset();
                    KSP.UI.Screens.StageManager.SetSeparationIndices();
                    GameEvents.StageManager.OnGUIStageSequenceModified.Fire();
                    TelemetryHub.Instance?.InvalidateStagePartIcons();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ReorderPartInStage error: {ex.Message}");
            }
#endif
        }

        public void ToggleSymmetryExpansion(uint partFlightId, int stage, int partIndex)
        {
#if KSP_RUNTIME
            try
            {
                if (TelemetryHub.Instance != null && TelemetryHub.Instance.IsSimulationMode)
                {
                    TelemetryHub.Instance.SimulationEngine.ToggleSimulatedSymmetry(partFlightId, stage, partIndex);
                    return;
                }

                if (KSP.UI.Screens.StageManager.Instance == null) return;
                var mgr = KSP.UI.Screens.StageManager.Instance;
                var stages = mgr.Stages;
                if (stages == null) return;

                KSP.UI.Screens.StageGroup group = null;
                for (int i = 0; i < stages.Count; i++)
                {
                    var grp = stages[i];
                    if (grp != null && (grp.inverseStageIndex == stage || grp.defaultStage == stage))
                    {
                        group = grp;
                        break;
                    }
                }

                if (group != null && group.Icons != null)
                {
                    KSP.UI.Screens.StageIcon targetIcon = null;
                    if (partFlightId > 0)
                    {
                        for (int j = 0; j < group.Icons.Count; j++)
                        {
                            var ic = group.Icons[j];
                            if (ic != null && ic.Part != null && (ic.Part.flightID == partFlightId || ic.Part.craftID == partFlightId || (uint)ic.Part.persistentId == partFlightId))
                            {
                                targetIcon = ic;
                                break;
                            }
                        }
                    }
                    if (targetIcon == null && partIndex >= 0 && partIndex < group.Icons.Count)
                    {
                        targetIcon = group.Icons[partIndex];
                    }

                    if (targetIcon != null && targetIcon.grouped)
                    {
                        if (targetIcon.expanded)
                        {
                            targetIcon.CollapseGroup();
                        }
                        else
                        {
                            targetIcon.ExpandGroup();
                        }
                        TelemetryHub.Instance?.InvalidateStagePartIcons();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ToggleSymmetryExpansion error: {ex.Message}");
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
                    if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
                    {
                        StockUIHider.HideStockBottomLeft(true);
                    }
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

    /// <summary>
    /// KSP 原版 StageManager 分级图标贴图与 UV 映射深度挂钩器 (Zero-Overhead Stock Stage Hook)
    /// 直接提取原版 defaultIconMap 图集与 StageIcon UV，实现与原版分级图标的无缝对齐
    /// </summary>
    public class StockStageIconHook : IStockStageIconProvider
    {
        private Texture2D _cachedAtlas;
        private float _lastAtlasCheckTime = -10f;

        public Texture StockAtlas
        {
            get
            {
                float now = Time.unscaledTime;
                if (_cachedAtlas == null || (now - _lastAtlasCheckTime > 3.0f))
                {
                    _lastAtlasCheckTime = now;
                    _cachedAtlas = FindStockAtlas();
                }
                return _cachedAtlas;
            }
        }

        public bool HasStockAtlas => StockAtlas != null;

#if KSP_RUNTIME
        private static System.Reflection.FieldInfo _defaultIconMapField;
        private static System.Reflection.FieldInfo _iconImageField;
        private static bool _fieldsResolved = false;

        private static void EnsureFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;
            try
            {
                var type = typeof(KSP.UI.Screens.StageIcon);
                _defaultIconMapField = type.GetField("defaultIconMap", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _iconImageField = type.GetField("iconImage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            catch { }
        }

        private static Texture2D ExtractTextureFromIcon(KSP.UI.Screens.StageIcon icon)
        {
            if (icon == null) return null;
            EnsureFields();
            if (_defaultIconMapField != null)
            {
                try
                {
                    if (_defaultIconMapField.GetValue(icon) is Texture2D tDef) return tDef;
                }
                catch { }
            }
            if (_iconImageField != null)
            {
                try
                {
                    var img = _iconImageField.GetValue(icon) as UnityEngine.UI.RawImage;
                    if (img != null && img.texture is Texture2D tImg) return tImg;
                }
                catch { }
            }
            var raw = icon.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            if (raw != null && raw.texture is Texture2D t1) return t1;
            var uiImg = icon.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (uiImg != null && uiImg.sprite != null && uiImg.sprite.texture != null) return uiImg.sprite.texture;
            return null;
        }

        private Texture2D FindStockAtlas()
        {
            try
            {
                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    var mgr = KSP.UI.Screens.StageManager.Instance;
                    if (mgr.stageIconPrefab != null)
                    {
                        var tex = ExtractTextureFromIcon(mgr.stageIconPrefab);
                        if (tex != null) return tex;
                    }

                    // 从当前激活级或任一已实例化图标获取
                    var stages = mgr.Stages;
                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            var stg = stages[i];
                            if (stg != null && stg.Icons != null)
                            {
                                for (int j = 0; j < stg.Icons.Count; j++)
                                {
                                    var tex = ExtractTextureFromIcon(stg.Icons[j]);
                                    if (tex != null) return tex;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] StockStageIconHook FindStockAtlas warning: {ex.Message}");
            }
            return null;
        }
#else
        private Texture2D FindStockAtlas() => null;
#endif

        public Rect GetStockIconUv(string iconType)
        {
            int index = StageIconAtlasGenerator.GetIconIndex(iconType);
            return GetStockIconUv(index);
        }

        public Rect GetStockIconUv(int iconIndex)
        {
            Texture atlas = StockAtlas;
            if (atlas != null && atlas.width > 0)
            {
                // KSP 原版 StageIcon.SetIcon 权威 UV 布局算法
                int iconSize = 32;
                int cols = atlas.width / iconSize;
                if (cols > 0)
                {
                    int x = iconIndex % cols;
                    int y = iconIndex / cols;
                    float num = (float)atlas.width / (float)iconSize;
                    return new Rect((float)x / num, 1f - (float)(y + 1) / num, 1f / num, 1f / num);
                }
            }
            return StageIconAtlasGenerator.GetIconUv(iconIndex);
        }
    }

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

        public void InsertStageAndMovePart(uint partFlightId, int fromStage, int partIndex, int insertAtStageIndex)
        {
            if (_simEngine != null)
            {
                _simEngine.InsertSimulatedStage(insertAtStageIndex);
                _simEngine.MoveSimulatedPartToStage(partFlightId, fromStage, partIndex, insertAtStageIndex);
            }
            Debug.Log($"[HeadlessStageActionHook] Inserted stage S{insertAtStageIndex:00} and moved simulated part {partFlightId}");
        }

        public void ReorderPartInStage(uint partFlightId, int stage, int fromIndex, int toIndex)
        {
            if (_simEngine != null)
            {
                _simEngine.ReorderSimulatedPartInStage(partFlightId, stage, fromIndex, toIndex);
            }
            Debug.Log($"[HeadlessStageActionHook] Reordered simulated part in S{stage:00} from idx {fromIndex} -> {toIndex}");
        }

        public void ToggleSymmetryExpansion(uint partFlightId, int stage, int partIndex)
        {
            if (_simEngine != null)
            {
                _simEngine.ToggleSimulatedSymmetry(partFlightId, stage, partIndex);
            }
            Debug.Log($"[HeadlessStageActionHook] Toggled symmetry expansion for part {partFlightId} in S{stage:00}");
        }

        public void SetPartHighlight(uint partFlightId, bool highlight, Color? highlightColor = null)
        {
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
