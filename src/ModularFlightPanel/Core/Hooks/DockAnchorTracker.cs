using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;

#if KSP_RUNTIME
using HarmonyLib;
using KSP.UI.Screens;
#endif

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// KSP 原版与第三方 MOD 工具栏按钮坐标实时同步与视口锚点追踪中枢 (Dock Anchor Tracker)
    /// 
    /// 解决痛点：
    /// 当 MFP 处于折叠收纳坞 (Dock) 模式时，原版 ApplicationLauncher 虽被 CanvasGroup 隐藏，
    /// 但第三方 MOD (如 AtmosphereAutopilot、ToolbarControl 关联的数十个 MOD 等)
    /// 在被点击时仍会通过 ApplicationLauncherButton.GetAnchor() / GetAnchorUL() 或 Transform 
    /// 寻址原版工具栏在屏幕边缘的停靠坐标，导致子菜单、弹窗或飞出窗（Flyout）出现在屏幕右下角原位置。
    /// 
    /// 解决方案（双管齐下）：
    /// 1. 物理位置热同步：将原版 ApplicationLauncherButton 及其容器 Transform 瞬时对齐到 MFP Dock 按钮在 MainCanvas 下的世界坐标。
    /// 2. Harmony 动态拦截：对 ApplicationLauncherButton.GetAnchor()、GetAnchorUL()、GetAnchorUR()、
    ///    GetAnchorTopRight()、GetAnchorLocal() 挂钩，根据 Dock 屏幕相对朝向（左侧/右侧/顶部/底部）
    ///    实时智能计算防遮挡弹出锚点。
    /// </summary>
    public static class DockAnchorTracker
    {
        public class TrackedButtonEntry
        {
#if KSP_RUNTIME
            public ApplicationLauncherButton KspButton;
#endif
            public RectTransform TargetRt;
            public MonoBehaviour HostWidget;
            public bool IsFavoriteDock;
            public Vector3[] CachedWorldCorners = new Vector3[4];
            public float LastSyncTime;
        }

#if KSP_RUNTIME
        private static readonly Dictionary<ApplicationLauncherButton, TrackedButtonEntry> _trackedButtons 
            = new Dictionary<ApplicationLauncherButton, TrackedButtonEntry>();

        public static void RegisterButton(ApplicationLauncherButton btn, RectTransform targetRt, MonoBehaviour host, bool isFavorite)
        {
            if (btn == null || targetRt == null) return;

            if (_trackedButtons.TryGetValue(btn, out var existing))
            {
                existing.TargetRt = targetRt;
                existing.HostWidget = host;
                existing.IsFavoriteDock = isFavorite;
            }
            else
            {
                _trackedButtons[btn] = new TrackedButtonEntry
                {
                    KspButton = btn,
                    TargetRt = targetRt,
                    HostWidget = host,
                    IsFavoriteDock = isFavorite,
                    LastSyncTime = 0f
                };
            }

            SyncButtonNow(btn);
        }

        public static void UnregisterButton(ApplicationLauncherButton btn)
        {
            if (btn == null) return;
            _trackedButtons.Remove(btn);
        }

        public static void UnregisterWidget(MonoBehaviour host)
        {
            if (host == null) return;
            var toRemove = new List<ApplicationLauncherButton>();
            foreach (var kvp in _trackedButtons)
            {
                if (kvp.Value.HostWidget == host)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                _trackedButtons.Remove(toRemove[i]);
            }
        }

        public static void ClearAll()
        {
            _trackedButtons.Clear();
        }

        /// <summary>
        /// 将屏幕物理像素坐标转换到 KSP 主画布 (MainCanvas) 的世界坐标系
        /// </summary>
        public static Vector3 ScreenToMainCanvasWorld(Vector2 screenPos)
        {
            Canvas mainCanvas = MainCanvasUtil.MainCanvas;
            if (mainCanvas == null)
            {
                return new Vector3(screenPos.x, screenPos.y, 0f);
            }

            if (mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return new Vector3(screenPos.x, screenPos.y, 0f);
            }

            Camera cam = mainCanvas.worldCamera != null ? mainCanvas.worldCamera : Camera.main;

            float planeDist = mainCanvas.planeDistance > 0f ? mainCanvas.planeDistance : 100f;
            if (cam != null)
            {
                return cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, planeDist));
            }

            return new Vector3(screenPos.x, screenPos.y, 0f);
        }

        /// <summary>
        /// 瞬时对齐指定按钮的 Transform 与容器物理坐标
        /// </summary>
        public static void SyncButtonNow(ApplicationLauncherButton kspBtn)
        {
            if (kspBtn == null) return;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            if (_trackedButtons.TryGetValue(kspBtn, out var entry))
            {
                SyncEntryTransform(entry);
            }
        }

        private static float _lastCentralSyncTime = -1f;

        /// <summary>
        /// 由 FlightHUDManager.LateUpdate() 统一调用的全局集中式锚点同步中枢。
        /// 将停靠追踪彻底从各个 Toolbar 组件的 Update 业务代码中剥离，集中在帧末批量平滑对齐。
        /// </summary>
        public static void LateUpdateSync()
        {
#if KSP_RUNTIME
            if (_trackedButtons.Count == 0) return;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            float now = Time.unscaledTime;
            // 10Hz 节流，兼顾平滑性与零 CPU 开销
            if (now - _lastCentralSyncTime < 0.1f) return;
            _lastCentralSyncTime = now;

            SyncAll();
#endif
        }

        /// <summary>
        /// 轮询或在拖拽时同步全部已跟踪按钮的物理 Transform (内部集中调度，禁止组件私自调用)
        /// </summary>
        internal static void SyncAll()
        {
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            foreach (var kvp in _trackedButtons)
            {
                SyncEntryTransform(kvp.Value);
            }
        }

        private static void SyncEntryTransform(TrackedButtonEntry entry)
        {
            if (entry == null || entry.TargetRt == null) return;
            if (!entry.TargetRt.gameObject.activeInHierarchy) return;

            // 仅对齐并缓存 Dock 界面按钮世界边界 (供 Harmony GetAnchor 系列补丁提供计算基准)
            // 绝不直接暴力修改 KSPButton 及其 container 的 Transform.position，
            // 彻底根除原版 SimpleLayout 布局器每帧与位置同步抢占冲突导致的剧烈高频晃动与闪烁！
            entry.TargetRt.GetWorldCorners(entry.CachedWorldCorners);
            entry.LastSyncTime = Time.unscaledTime;
        }

        /// <summary>
        /// 获取 Upper-Left 锚点（供 ToolbarControl 及 IMGUI 标定屏幕 Rect）
        /// </summary>
        public static bool TryGetAnchorUL(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (btn == null) return false;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return false;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.TargetRt != null && entry.TargetRt.gameObject.activeInHierarchy)
            {
                entry.TargetRt.GetWorldCorners(entry.CachedWorldCorners);
                // corners[1] = Upper-Left (minX, maxY)
                anchor = ScreenToMainCanvasWorld(entry.CachedWorldCorners[1]);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取 Upper-Right 锚点
        /// </summary>
        public static bool TryGetAnchorUR(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (btn == null) return false;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return false;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.TargetRt != null && entry.TargetRt.gameObject.activeInHierarchy)
            {
                entry.TargetRt.GetWorldCorners(entry.CachedWorldCorners);
                // corners[2] = Upper-Right (maxX, maxY)
                anchor = ScreenToMainCanvasWorld(entry.CachedWorldCorners[2]);
                return true;
            }
            return false;
        }

        public static bool TryGetAnchorTopRight(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            return TryGetAnchorUR(btn, out anchor);
        }

        /// <summary>
        /// 获取 Local 锚点
        /// </summary>
        public static bool TryGetAnchorLocal(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            if (TryGetAnchor(btn, out anchor))
            {
                if (btn.container != null && btn.container.transform.parent != null)
                {
                    anchor = btn.container.transform.parent.InverseTransformPoint(anchor);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 核心智能弹窗生成锚点：供 AtmosphereAutopilot、KAC 等 MOD 生成飞出菜单 (Flyout Menu)
        /// 自动侦测屏幕半区与贴边，杜绝边缘出界与 Dock 遮挡
        /// </summary>
        public static bool TryGetAnchor(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (btn == null) return false;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return false;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.TargetRt != null && entry.TargetRt.gameObject.activeInHierarchy)
            {
                entry.TargetRt.GetWorldCorners(entry.CachedWorldCorners);
                Vector3[] c = entry.CachedWorldCorners;
                Vector2 screenCenter = (c[0] + c[2]) * 0.5f;

                float targetScreenX;
                float targetScreenY;

                // 判断是否靠顶或靠底水平排布
                if (screenCenter.y > Screen.height * 0.85f)
                {
                    // 靠顶布局：弹窗向下展开
                    targetScreenX = screenCenter.x + 95f;
                    targetScreenY = c[0].y - 8f;
                }
                else if (screenCenter.y < Screen.height * 0.15f)
                {
                    // 靠底布局：弹窗向上展开
                    targetScreenX = screenCenter.x + 95f;
                    targetScreenY = c[1].y + 8f + 70f;
                }
                else if (screenCenter.x < Screen.width * 0.5f)
                {
                    // 靠屏幕左侧半区：弹窗向右展开（贴齐 Dock 按钮右侧）
                    // 预留标准 195px 弹窗宽度使其 Upper-Right 锚点恰好位于右侧
                    targetScreenX = c[2].x + 8f + 195f;
                    targetScreenY = screenCenter.y + 12f;
                }
                else
                {
                    // 靠屏幕右侧半区：弹窗向左展开（贴齐 Dock 按钮左侧）
                    targetScreenX = c[1].x - 8f;
                    targetScreenY = screenCenter.y + 12f;
                }

                // 安全边界约束
                targetScreenX = Mathf.Clamp(targetScreenX, 205f, Screen.width - 15f);
                targetScreenY = Mathf.Clamp(targetScreenY, 80f, Screen.height - 25f);

                anchor = ScreenToMainCanvasWorld(new Vector2(targetScreenX, targetScreenY));
                return true;
            }
            return false;
        }

        /// <summary>
        /// 点击后安全补丁：若第三方模组（如 AtmosphereAutopilot）在 MainCanvas 下动态生成了子菜单，
        /// 确保其位置与当前 Dock 按钮精密吸附对齐
        /// </summary>
        public static void OnPostButtonClick(ApplicationLauncherButton btn)
        {
            if (btn == null) return;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            if (!_trackedButtons.TryGetValue(btn, out var entry) || entry.TargetRt == null) return;

            try
            {
                Canvas mainCanvas = MainCanvasUtil.MainCanvas;
                if (mainCanvas == null) return;

                entry.TargetRt.GetWorldCorners(entry.CachedWorldCorners);
                Vector3[] c = entry.CachedWorldCorners;
                Vector2 screenCenter = (c[0] + c[2]) * 0.5f;

                // 检查 MainCanvas 下是否有名字包含 "ToolbarMenu" 或挂载了 MainMenuGUI 的新弹窗对象
                for (int i = 0; i < mainCanvas.transform.childCount; i++)
                {
                    Transform child = mainCanvas.transform.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;

                    string name = child.name;
                    if (name.IndexOf("ToolbarMenu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        RectTransform childRt = child.GetComponent<RectTransform>();
                        if (childRt != null)
                        {
                            // 调整弹窗位置紧贴 Dock 项
                            if (screenCenter.x < Screen.width * 0.5f)
                            {
                                Vector2 targetPos = new Vector2(c[2].x + 8f + childRt.rect.width, screenCenter.y + 12f);
                                targetPos.x = Mathf.Clamp(targetPos.x, childRt.rect.width + 10f, Screen.width - 10f);
                                targetPos.y = Mathf.Clamp(targetPos.y, childRt.rect.height + 10f, Screen.height - 10f);
                                childRt.position = ScreenToMainCanvasWorld(targetPos);
                            }
                            else
                            {
                                Vector2 targetPos = new Vector2(c[1].x - 8f, screenCenter.y + 12f);
                                targetPos.x = Mathf.Clamp(targetPos.x, 10f, Screen.width - 10f);
                                targetPos.y = Mathf.Clamp(targetPos.y, childRt.rect.height + 10f, Screen.height - 10f);
                                childRt.position = ScreenToMainCanvasWorld(targetPos);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.WarnThrottled("DockAnchor_PostClick", $"Post-click alignment error: {ex.Message}");
            }
        }
#endif
    }

#if KSP_RUNTIME
    // =========================================================================
    // Harmony 运行时动态补丁：挂钩 ApplicationLauncherButton 全套锚点计算接口
    // =========================================================================

    [HarmonyPatch(typeof(ApplicationLauncherButton), "GetAnchor")]
    public static class Patch_AppLauncherButton_GetAnchor
    {
        [HarmonyPrefix]
        public static bool Prefix(ApplicationLauncherButton __instance, ref Vector3 __result)
        {
            if (DockAnchorTracker.TryGetAnchor(__instance, out Vector3 anchor))
            {
                __result = anchor;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ApplicationLauncherButton), "GetAnchorUL")]
    public static class Patch_AppLauncherButton_GetAnchorUL
    {
        [HarmonyPrefix]
        public static bool Prefix(ApplicationLauncherButton __instance, ref Vector3 __result)
        {
            if (DockAnchorTracker.TryGetAnchorUL(__instance, out Vector3 anchor))
            {
                __result = anchor;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ApplicationLauncherButton), "GetAnchorUR")]
    public static class Patch_AppLauncherButton_GetAnchorUR
    {
        [HarmonyPrefix]
        public static bool Prefix(ApplicationLauncherButton __instance, ref Vector3 __result)
        {
            if (DockAnchorTracker.TryGetAnchorUR(__instance, out Vector3 anchor))
            {
                __result = anchor;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ApplicationLauncherButton), "GetAnchorTopRight")]
    public static class Patch_AppLauncherButton_GetAnchorTopRight
    {
        [HarmonyPrefix]
        public static bool Prefix(ApplicationLauncherButton __instance, ref Vector3 __result)
        {
            if (DockAnchorTracker.TryGetAnchorTopRight(__instance, out Vector3 anchor))
            {
                __result = anchor;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ApplicationLauncherButton), "GetAnchorLocal")]
    public static class Patch_AppLauncherButton_GetAnchorLocal
    {
        [HarmonyPrefix]
        public static bool Prefix(ApplicationLauncherButton __instance, ref Vector3 __result)
        {
            if (DockAnchorTracker.TryGetAnchorLocal(__instance, out Vector3 anchor))
            {
                __result = anchor;
                return false;
            }
            return true;
        }
    }
#endif
}
