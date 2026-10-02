using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ModularFlightPanel.Config;

#if KSP_RUNTIME
using HarmonyLib;
using KSP.UI;
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
            public Assembly OwnerAssembly;
        }

#if KSP_RUNTIME
        private static readonly Dictionary<ApplicationLauncherButton, TrackedButtonEntry> _trackedButtons 
            = new Dictionary<ApplicationLauncherButton, TrackedButtonEntry>();

        private static readonly Dictionary<Assembly, List<ApplicationLauncherButton>> _assemblyToButtons
            = new Dictionary<Assembly, List<ApplicationLauncherButton>>();

        // 交互上下文（最近点击 / 悬停）
        private static ApplicationLauncherButton _lastInteractedButton;
        public static ApplicationLauncherButton LastInteractedButton => _lastInteractedButton;
        private static Assembly _lastInteractedAssembly;
        private static float _lastInteractionTime = -1f;

        // 窗口重定向与防抖记录
        private static readonly HashSet<int> _relocatedWindowIds = new HashSet<int>();
        private static readonly HashSet<int> _userDraggedWindowIds = new HashSet<int>();
        private static readonly Dictionary<int, ApplicationLauncherButton> _windowToButton
            = new Dictionary<int, ApplicationLauncherButton>();
        private static readonly Dictionary<ApplicationLauncherButton, Rect> _buttonToLastWindowRect
            = new Dictionary<ApplicationLauncherButton, Rect>();

        public static void RegisterButton(ApplicationLauncherButton btn, RectTransform targetRt, MonoBehaviour host, bool isFavorite)
        {
            if (btn == null || targetRt == null) return;

            Assembly ownerAsm = GetButtonOwnerAssembly(btn);

            if (_trackedButtons.TryGetValue(btn, out var existing))
            {
                existing.TargetRt = targetRt;
                existing.HostWidget = host;
                existing.IsFavoriteDock = isFavorite;
                existing.OwnerAssembly = ownerAsm;
            }
            else
            {
                _trackedButtons[btn] = new TrackedButtonEntry
                {
                    KspButton = btn,
                    TargetRt = targetRt,
                    HostWidget = host,
                    IsFavoriteDock = isFavorite,
                    LastSyncTime = 0f,
                    OwnerAssembly = ownerAsm
                };
            }

            if (ownerAsm != null)
            {
                if (!_assemblyToButtons.TryGetValue(ownerAsm, out var list))
                {
                    list = new List<ApplicationLauncherButton>();
                    _assemblyToButtons[ownerAsm] = list;
                }
                if (!list.Contains(btn))
                {
                    list.Add(btn);
                }
            }

            SyncButtonNow(btn);
        }

        public static void UnregisterButton(ApplicationLauncherButton btn)
        {
            if (btn == null) return;
            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.OwnerAssembly != null)
            {
                if (_assemblyToButtons.TryGetValue(entry.OwnerAssembly, out var list))
                {
                    list.Remove(btn);
                    if (list.Count == 0) _assemblyToButtons.Remove(entry.OwnerAssembly);
                }
            }
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
                UnregisterButton(toRemove[i]);
            }
        }

        public static void ClearAll()
        {
            _trackedButtons.Clear();
            _assemblyToButtons.Clear();
            _relocatedWindowIds.Clear();
            _userDraggedWindowIds.Clear();
            _windowToButton.Clear();
            _buttonToLastWindowRect.Clear();
            _lastInteractedButton = null;
            _lastInteractedAssembly = null;
        }

        public static void NotifyInteraction(ApplicationLauncherButton btn)
        {
            if (btn == null) return;
            _lastInteractedButton = btn;
            _lastInteractionTime = Time.realtimeSinceStartup;
            _lastInteractedAssembly = GetButtonOwnerAssembly(btn);

            // 若该按钮之前绑定的窗口曾被标记为用户手动拖拽，在玩家重新点击/悬停 Dock 按钮时解除标记，允许重新自动停靠
            var toUnmark = new List<int>();
            foreach (var kvp in _windowToButton)
            {
                if (kvp.Value == btn)
                {
                    toUnmark.Add(kvp.Key);
                }
            }
            for (int i = 0; i < toUnmark.Count; i++)
            {
                _userDraggedWindowIds.Remove(toUnmark[i]);
            }
        }

        public static Assembly GetButtonOwnerAssembly(ApplicationLauncherButton btn)
        {
            if (btn == null) return null;
            Callback[] callbacks = { btn.onTrue, btn.onFalse, btn.onHover, btn.onHoverOut, btn.onEnable, btn.onDisable, btn.onLeftClick, btn.onRightClick };
            for (int i = 0; i < callbacks.Length; i++)
            {
                var cb = callbacks[i];
                if (cb != null && cb.Method != null && cb.Method.DeclaringType != null)
                {
                    var asm = cb.Method.DeclaringType.Assembly;
                    if (asm != typeof(object).Assembly && asm != typeof(ApplicationLauncherButton).Assembly)
                    {
                        return asm;
                    }
                }
            }

            var components = btn.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c != null)
                {
                    var asm = c.GetType().Assembly;
                    if (asm != typeof(object).Assembly && asm != typeof(ApplicationLauncherButton).Assembly)
                    {
                        return asm;
                    }
                }
            }

            return null;
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

            RectTransform mainCanvasRt = MainCanvasUtil.MainCanvasRect;
            Camera cam = mainCanvas.worldCamera != null ? mainCanvas.worldCamera : Camera.main;

            if (mainCanvasRt != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(mainCanvasRt, screenPos, cam, out Vector3 worldPos))
            {
                return worldPos;
            }

            float planeDist = mainCanvas.planeDistance > 0f ? mainCanvas.planeDistance : 100f;
            if (cam != null)
            {
                return cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, planeDist));
            }

            return new Vector3(screenPos.x, screenPos.y, 0f);
        }

        private static Camera GetUICamera()
        {
            Canvas mainCanvas = MainCanvasUtil.MainCanvas;
            if (mainCanvas != null && mainCanvas.worldCamera != null) return mainCanvas.worldCamera;
            return Camera.main;
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

            // 保持激活的原生 GenericAppFrame (如 ResourceDisplay、Delta-V 等) 停靠对齐
            try
            {
                if (ApplicationLauncher.Instance != null && ApplicationLauncher.Instance.appSpace != null)
                {
                    var frames = ApplicationLauncher.Instance.appSpace.GetComponentsInChildren<GenericAppFrame>(false);
                    for (int i = 0; i < frames.Length; i++)
                    {
                        var frame = frames[i];
                        if (frame != null && frame.gameObject.activeInHierarchy)
                        {
                            RepositionGenericAppFrame(frame);
                        }
                    }
                }
            }
            catch { }
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
        /// 遵循原版 ApplicationLauncherButton 规范：直接返回画布世界空间坐标
        /// </summary>
        public static bool TryGetAnchorUL(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (btn == null) return false;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return false;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.TargetRt != null && entry.TargetRt.gameObject.activeInHierarchy)
            {
                if (GetRectTransformBounds(entry.TargetRt, out Rect screenBounds, out _))
                {
                    anchor = ScreenToMainCanvasWorld(new Vector2(screenBounds.xMin, screenBounds.yMax));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取 Upper-Right 锚点
        /// </summary>
        public static bool TryGetAnchorUR(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            // 故意返回 false：允许 KSP 原生 GetAnchorUR() 运行。
            // KSP 原生 GenericAppFrame.UpdateDraggingBoundsAtBottom() 会读取 GetAnchorUR().y 并加上 minHeight 像素偏移，
            // 若在此处返回 Canvas 世界坐标，相加后会将窗口直接抛射至摄像机视锥体上方 100 米之外导致窗口完全隐形！
            // 原生 GenericAppFrame 的吸附由 Patch_GenericAppFrame_Reposition 统一接管。
            return false;
        }

        public static bool TryGetAnchorTopRight(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            // 故意返回 false：允许 KSP 原生 ApplicationLauncherButton.GetAnchorTopRight() 运行并返回 (-41, 0, 0)，
            // 确保原生 GenericAppFrame (如 ResourceDisplay、Delta-V 计算器等) 正确锚定在 appSpace 内，避免被误写世界坐标而飞出屏幕
            return false;
        }

        /// <summary>
        /// 获取 Local 锚点
        /// </summary>
        public static bool TryGetAnchorLocal(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            // 故意返回 false：允许原版 GetAnchorLocal() 运行，防止 GenericAppFrame 被写错相对坐标
            return false;
        }

        /// <summary>
        /// 核心智能弹窗生成锚点：供各类 MOD 生成飞出菜单 (Flyout Menu)
        /// 基于真实屏幕像素坐标运算，自动侦测屏幕半区与贴边，杜绝边缘出界与 Dock 遮挡
        /// </summary>
        public static bool TryGetAnchor(ApplicationLauncherButton btn, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (btn == null) return false;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return false;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.TargetRt != null && entry.TargetRt.gameObject.activeInHierarchy)
            {
                if (!GetRectTransformBounds(entry.TargetRt, out Rect screenBounds, out _))
                    return false;

                float btnLeft = screenBounds.xMin;
                float btnRight = screenBounds.xMax;
                float btnBottom = screenBounds.yMin;
                float btnTop = screenBounds.yMax;
                Vector2 screenCenter = screenBounds.center;

                float targetScreenX;
                float targetScreenY;

                // 判断是否靠顶或靠底水平排布 (Screen 物理像素空间: y=0 为屏幕底端, y=Screen.height 为顶端)
                if (screenCenter.y > Screen.height * 0.65f)
                {
                    // 靠顶布局：弹窗向下展开
                    targetScreenX = screenCenter.x;
                    targetScreenY = btnBottom - 8f;
                }
                else if (screenCenter.y < Screen.height * 0.35f)
                {
                    // 靠底布局：弹窗向上展开
                    targetScreenX = screenCenter.x;
                    targetScreenY = btnTop + 8f;
                }
                else if (screenCenter.x < Screen.width * 0.5f)
                {
                    // 靠屏幕左侧半区：弹窗向右展开（贴齐 Dock 按钮右侧）
                    targetScreenX = btnRight + 8f;
                    targetScreenY = screenCenter.y;
                }
                else
                {
                    // 靠屏幕右侧半区：弹窗向左展开（贴齐 Dock 按钮左侧）
                    targetScreenX = btnLeft - 8f;
                    targetScreenY = screenCenter.y;
                }

                // 安全边界约束
                targetScreenX = Mathf.Clamp(targetScreenX, 20f, Screen.width - 20f);
                targetScreenY = Mathf.Clamp(targetScreenY, 20f, Screen.height - 20f);

                anchor = ScreenToMainCanvasWorld(new Vector2(targetScreenX, targetScreenY));
                return true;
            }
            return false;
        }

        /// <summary>
        /// 精确解析任意 RectTransform 在真实屏幕像素与 IMGUI（左上角为原点）空间的绝对边界。
        /// 关键防御：ScreenSpaceOverlay 画布下的 GetWorldCorners 即为真实物理像素坐标，绝对禁止使用摄像机二次投影！
        /// </summary>
        public static bool GetRectTransformBounds(RectTransform rt, out Rect screenBounds, out Rect imguiBounds)
        {
            screenBounds = default;
            imguiBounds = default;
            if (rt == null) return false;

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                float xMin = Mathf.Min(corners[0].x, corners[2].x);
                float xMax = Mathf.Max(corners[0].x, corners[2].x);
                float yMin = Mathf.Min(corners[0].y, corners[2].y);
                float yMax = Mathf.Max(corners[0].y, corners[2].y);

                screenBounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                imguiBounds = new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
                return true;
            }
            else
            {
                Camera cam = (canvas != null && canvas.worldCamera != null) ? canvas.worldCamera : GetUICamera();
                Vector2 s0 = cam != null ? (Vector2)cam.WorldToScreenPoint(corners[0]) : (Vector2)corners[0];
                Vector2 s2 = cam != null ? (Vector2)cam.WorldToScreenPoint(corners[2]) : (Vector2)corners[2];

                float xMin = Mathf.Min(s0.x, s2.x);
                float xMax = Mathf.Max(s0.x, s2.x);
                float yMin = Mathf.Min(s0.y, s2.y);
                float yMax = Mathf.Max(s0.y, s2.y);

                screenBounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                imguiBounds = new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
                return true;
            }
        }

        /// <summary>
        /// 获取按钮在屏幕空间和 IMGUI 空间（左上角原点）下的边界矩形
        /// </summary>
        public static bool TryGetButtonScreenBounds(ApplicationLauncherButton btn, out Rect screenBounds, out Rect imguiBounds)
        {
            screenBounds = default;
            imguiBounds = default;
            if (btn == null) return false;
            if (!_trackedButtons.TryGetValue(btn, out var entry) || entry.TargetRt == null || !entry.TargetRt.gameObject.activeInHierarchy)
                return false;

            return GetRectTransformBounds(entry.TargetRt, out screenBounds, out imguiBounds);
        }

        /// <summary>
        /// 获取指定按钮所属 Dock 的整体边界矩形与按钮自身边界矩形。
        /// 关键防御：在多列或多行 Dock 中，若仅对齐按钮自身，弹窗在向外展开时
        /// 必然会被 Dock 的其他列或行遮挡。本方法合并 Dock 整体外包围盒作为排斥障碍物，
        /// 并在次要轴上保留按钮自身的物理中心，实现“既紧贴 Dock 外部、又对准所点按钮”的精密几何对齐。
        /// </summary>
        public static bool TryGetAnchorObstacleBounds(
            ApplicationLauncherButton btn,
            out Rect obstacleScreenBounds,
            out Rect obstacleImguiBounds,
            out Rect btnScreenBounds,
            out Rect btnImguiBounds)
        {
            obstacleScreenBounds = default;
            obstacleImguiBounds = default;
            btnScreenBounds = default;
            btnImguiBounds = default;

            if (btn == null) return false;
            if (!TryGetButtonScreenBounds(btn, out btnScreenBounds, out btnImguiBounds))
                return false;

            obstacleScreenBounds = btnScreenBounds;
            obstacleImguiBounds = btnImguiBounds;

            if (_trackedButtons.TryGetValue(btn, out var entry) && entry.HostWidget != null)
            {
                RectTransform hostRt = entry.HostWidget.GetComponent<RectTransform>();
                if (hostRt != null && hostRt.gameObject.activeInHierarchy)
                {
                    if (GetRectTransformBounds(hostRt, out Rect dockScreen, out Rect dockImgui))
                    {
                        float sMinX = Mathf.Min(dockScreen.xMin, btnScreenBounds.xMin);
                        float sMaxX = Mathf.Max(dockScreen.xMax, btnScreenBounds.xMax);
                        float sMinY = Mathf.Min(dockScreen.yMin, btnScreenBounds.yMin);
                        float sMaxY = Mathf.Max(dockScreen.yMax, btnScreenBounds.yMax);
                        obstacleScreenBounds = Rect.MinMaxRect(sMinX, sMinY, sMaxX, sMaxY);

                        float iMinX = Mathf.Min(dockImgui.xMin, btnImguiBounds.xMin);
                        float iMaxX = Mathf.Max(dockImgui.xMax, btnImguiBounds.xMax);
                        float iMinY = Mathf.Min(dockImgui.yMin, btnImguiBounds.yMin);
                        float iMaxY = Mathf.Max(dockImgui.yMax, btnImguiBounds.yMax);
                        obstacleImguiBounds = Rect.MinMaxRect(iMinX, iMinY, iMaxX, iMaxY);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 检测指定 IMGUI 矩形是否处于原版工具栏区域（或默认吸附于原版工具栏）。
        /// IMGUI 坐标系：(0, 0) 为屏幕左上角，(Screen.width, Screen.height) 为右下角。
        /// </summary>
        public static bool IsInStockToolbarZone(Rect rect)
        {
            // 原版 KSP 飞行中工具栏位于屏幕右侧边界：
            // 绝大多数模组的初始窗口或弹出菜单均布局在屏幕右侧近边缘区域 (xMax >= Screen.width - 480f 或 x >= Screen.width - 400f)
            bool nearRight = rect.xMax >= Screen.width - 480f || rect.x >= Screen.width - 400f;
            return nearRight;
        }

        /// <summary>
        /// 检测指定 Screen 空间矩形是否处于原版工具栏区域。
        /// Screen 坐标系：(0, 0) 为屏幕左下角，(Screen.width, Screen.height) 为右上角。
        /// </summary>
        public static bool IsInStockToolbarZoneScreen(Rect screenBounds)
        {
            bool nearRight = screenBounds.xMax >= Screen.width - 480f || screenBounds.xMin >= Screen.width - 400f;
            return nearRight;
        }

        /// <summary>
        /// 全局通用：计算窗口相对 Dock 宿主与按钮位置的期望投影坐标。
        /// 基于 Dock 整体外包围盒障碍物向屏幕内侧展开，彻底避免多列多行遮挡，
        /// 同时垂直/水平轴对准按钮中心。
        /// </summary>
        public static Rect CalculateRelocatedWindowRect(Rect originalRect, Rect obstacleBounds, Rect btnBounds)
        {
            float w = Mathf.Max(originalRect.width, 60f);
            float h = Mathf.Max(originalRect.height, 40f);

            float targetX;
            float targetY;

            // IMGUI 坐标系：(0, 0) 为屏幕左上角
            float distRight = Screen.width - obstacleBounds.xMax;
            float distLeft = obstacleBounds.xMin;
            float distBottom = Screen.height - obstacleBounds.yMax;
            float distTop = obstacleBounds.yMin;

            if (distRight < 120f && distRight <= Mathf.Min(distLeft, distBottom, distTop))
            {
                // Dock 贴靠右边缘：向左展开，完全避开整个 Dock，垂直对齐按钮中心
                targetX = obstacleBounds.xMin - w - 8f;
                targetY = btnBounds.center.y - h * 0.5f;
            }
            else if (distLeft < 120f && distLeft <= Mathf.Min(distRight, distBottom, distTop))
            {
                // Dock 贴靠左边缘：向右展开，完全避开整个 Dock，垂直对齐按钮中心
                targetX = obstacleBounds.xMax + 8f;
                targetY = btnBounds.center.y - h * 0.5f;
            }
            else if (distBottom < 120f && distBottom <= Mathf.Min(distLeft, distRight, distTop))
            {
                // Dock 贴靠底边缘：向上展开，完全避开整个 Dock，水平对齐按钮中心
                targetX = btnBounds.center.x - w * 0.5f;
                targetY = obstacleBounds.yMin - h - 8f;
            }
            else if (distTop < 120f && distTop <= Mathf.Min(distLeft, distRight, distBottom))
            {
                // Dock 贴靠顶边缘：向下展开，完全避开整个 Dock，水平对齐按钮中心
                targetX = btnBounds.center.x - w * 0.5f;
                targetY = obstacleBounds.yMax + 8f;
            }
            else if (obstacleBounds.center.x >= Screen.width * 0.5f)
            {
                targetX = obstacleBounds.xMin - w - 8f;
                targetY = btnBounds.center.y - h * 0.5f;
            }
            else
            {
                targetX = obstacleBounds.xMax + 8f;
                targetY = btnBounds.center.y - h * 0.5f;
            }

            // 屏幕安全边界约束
            targetX = Mathf.Clamp(targetX, 6f, Screen.width - w - 6f);
            targetY = Mathf.Clamp(targetY, 25f, Screen.height - h - 6f);

            return new Rect(targetX, targetY, w, h);
        }

        public static Rect CalculateRelocatedWindowRect(Rect originalRect, Rect dockBtnBounds)
        {
            return CalculateRelocatedWindowRect(originalRect, dockBtnBounds, dockBtnBounds);
        }

        private static string GetAssemblyBaseName(Assembly asm)
        {
            if (asm == null) return string.Empty;
            string name = asm.GetName().Name;
            int dotIdx = name.IndexOf('.');
            return dotIdx > 0 ? name.Substring(0, dotIdx) : name;
        }

        /// <summary>
        /// 严格且准确判定玩家是否正在手动拖拽指定的 IMGUI 窗口。
        /// 彻底杜绝因按住鼠标点击 Dock 按钮引发的初始排版位移被误判为“手动拖拽”的恶性假阳性缺陷！
        /// </summary>
        private static bool IsUserDraggingWindow(int id, Rect currentRect, Rect lastRect)
        {
            // 1. 玩家必须按住鼠标主键
            if (!Input.GetMouseButton(0)) return false;

            // 2. 交互防抖：点击 Dock 按钮后的 0.6 秒内，按键属于点击触发，绝不是拖拽窗口！
            // 此时窗口在初始排版 (Layout) 阶段发生的尺寸与位置自适应微调绝对禁止判定为玩家拖拽！
            if (Time.realtimeSinceStartup - _lastInteractionTime < 0.6f) return false;

            // 3. 必须处于非排版绘制阶段（Layout 与 Repaint 阶段发生的坐标变化属于 Unity 内部重排）
            if (Event.current != null && (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint))
            {
                return false;
            }

            // 4. 鼠标指针必须真实落在该窗口矩形范围之内！
            // 彻底杜绝鼠标在屏幕底端 Dock 处点击却误判为拖拽屏幕中上方窗口的致命假阳性！
            if (Event.current != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                if (!currentRect.Contains(mousePos))
                {
                    return false;
                }
            }

            // 5. 窗口移动位移必须具有足够的物理距离阈值
            float sqrDist = (currentRect.position - lastRect.position).sqrMagnitude;
            if (sqrDist < 16.0f) return false;

            // 6. 如果有 Event.current，优先依据 MouseDrag 事件
            if (Event.current != null && Event.current.type == EventType.MouseDrag)
            {
                return true;
            }

            return true;
        }

        /// <summary>
        /// 全局通用 IMGUI 窗口重定向拦截：
        /// 挂钩 UnityEngine.GUI.DoWindow，自动将原版工具栏区域的任意第三方模组 IMGUI 窗口吸附至对应 Dock 按钮。
        /// 若用户使用鼠标手动拖拽了该窗口，立即标记放行，100% 尊重玩家自定义布局自由。
        /// </summary>
        public static void TryRelocateImguiWindow(int id, ref Rect clientRect, GUI.WindowFunction func)
        {
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;
            if (func == null || func.Method == null || func.Method.DeclaringType == null) return;

            // 1. 如果该窗口已被玩家手动拖拽，彻底放行，100% 尊重玩家自定义布局自由
            if (_userDraggedWindowIds.Contains(id))
            {
                return;
            }

            Assembly funcAsm = func.Method.DeclaringType.Assembly;
            string asmName = funcAsm.GetName().Name;

            // 过滤 Unity 引擎与系统底层程序集
            if (asmName == "UnityEngine" || asmName == "UnityEngine.CoreModule" || asmName == "UnityEngine.IMGUIModule" || asmName == "mscorlib")
            {
                return;
            }

            Rect obstacleBounds;
            Rect btnBounds;

            // 2. 检测用户是否正在用鼠标手动拖拽已吸附的窗口
            if (_windowToButton.TryGetValue(id, out var trackedBtn))
            {
                if (_buttonToLastWindowRect.TryGetValue(trackedBtn, out var lastRect))
                {
                    if (IsUserDraggingWindow(id, clientRect, lastRect))
                    {
                        _userDraggedWindowIds.Add(id);
                        return;
                    }
                }

                // 窗口未被拖动且 Dock 按钮位置正常：保持相对吸附
                if (TryGetAnchorObstacleBounds(trackedBtn, out _, out obstacleBounds, out _, out btnBounds))
                {
                    Rect newRect = CalculateRelocatedWindowRect(clientRect, obstacleBounds, btnBounds);
                    clientRect = newRect;
                    _buttonToLastWindowRect[trackedBtn] = newRect;
                }
                return;
            }

            // 3. 初次判定：仅对出现在原版工具栏区域的窗口进行空间校正
            if (!IsInStockToolbarZone(clientRect))
            {
                return;
            }

            // 4. 全局智能匹配所属的 Dock 按钮（不再死板依赖单一 Assembly 强相等）
            ApplicationLauncherButton targetBtn = null;

            // 策略 A (最高优先级)：玩家最近 3.0 秒内在 Dock 中交互（点击或悬停）的按钮
            // 无论是多 DLL 复合模组 (如 AtmosphereAutopilot + AtmosphereAutopilot.UI) 还是基于 ToolbarControl 的通用模组，
            // 玩家点击 Dock 按钮后几帧内弹出的窗口毫无疑问归属于该按钮！
            if (_lastInteractedButton != null && (Time.realtimeSinceStartup - _lastInteractionTime < 3.0f))
            {
                targetBtn = _lastInteractedButton;
            }

            // 策略 B：若时间超过 3 秒，匹配具有相同基程序集名 (如 AtmosphereAutopilot) 且当前为激活状态 (True) 的按钮
            if (targetBtn == null)
            {
                string funcBaseName = GetAssemblyBaseName(funcAsm);
                foreach (var kvp in _trackedButtons)
                {
                    var b = kvp.Key;
                    if (b != null && b.toggleButton != null && b.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True)
                    {
                        var entry = kvp.Value;
                        if (entry != null && entry.OwnerAssembly != null && GetAssemblyBaseName(entry.OwnerAssembly) == funcBaseName)
                        {
                            targetBtn = b;
                            break;
                        }
                    }
                }
            }

            // 策略 C：匹配任意当前处于 True (激活) 状态的 Dock 按钮
            if (targetBtn == null)
            {
                foreach (var kvp in _trackedButtons)
                {
                    var b = kvp.Key;
                    if (b != null && b.toggleButton != null && b.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True)
                    {
                        targetBtn = b;
                        break;
                    }
                }
            }

            // 策略 D：备用取最近交互按钮
            if (targetBtn == null && _lastInteractedButton != null)
            {
                targetBtn = _lastInteractedButton;
            }

            if (targetBtn == null)
            {
                return;
            }

            // 5. 计算并施加空间几何修正
            if (TryGetAnchorObstacleBounds(targetBtn, out _, out obstacleBounds, out _, out btnBounds))
            {
                Rect newRect = CalculateRelocatedWindowRect(clientRect, obstacleBounds, btnBounds);
                clientRect = newRect;

                _relocatedWindowIds.Add(id);
                _windowToButton[id] = targetBtn;
                _buttonToLastWindowRect[targetBtn] = newRect;
            }
        }

        /// <summary>
        /// DoWindow 执行完成后的后置处理：检测玩家鼠标拖拽并更新记录
        /// </summary>
        public static void OnPostDoWindow(int id, Rect resultRect)
        {
            if (_windowToButton.TryGetValue(id, out var btn))
            {
                if (_buttonToLastWindowRect.TryGetValue(btn, out var lastRect))
                {
                    if (IsUserDraggingWindow(id, resultRect, lastRect))
                    {
                        _userDraggedWindowIds.Add(id);
                    }
                    else
                    {
                        _buttonToLastWindowRect[btn] = resultRect;
                    }
                }
            }
        }

        /// <summary>
        /// 原生 KSP 应用窗口 (GenericAppFrame, 如 ResourceDisplay、Delta-V、热力等) 停靠对齐中枢
        private static readonly FieldInfo _appFrameBtnField = AccessTools.Field(typeof(GenericAppFrame), "appLauncherButton");
        private static readonly FieldInfo _resourceDisplayFrameField = AccessTools.Field(typeof(ResourceDisplay), "appFrame");

        public static ApplicationLauncherButton GetFrameButton(GenericAppFrame appFrame)
        {
            if (appFrame == null || _appFrameBtnField == null) return null;
            return _appFrameBtnField.GetValue(appFrame) as ApplicationLauncherButton;
        }

        public static GenericAppFrame GetResourceDisplayAppFrame()
        {
            if (ResourceDisplay.Instance == null || _resourceDisplayFrameField == null) return null;
            return _resourceDisplayFrameField.GetValue(ResourceDisplay.Instance) as GenericAppFrame;
        }

        /// <summary>
        /// 原生 KSP 应用窗口 (GenericAppFrame, 如 ResourceDisplay、Delta-V、热力等) 停靠对齐中枢
        /// </summary>
        public static void RepositionGenericAppFrame(GenericAppFrame appFrame)
        {
            if (appFrame == null) return;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            ApplicationLauncherButton btn = GetFrameButton(appFrame);
            if (btn == null)
            {
                if (ResourceDisplay.Instance != null && GetResourceDisplayAppFrame() == appFrame)
                {
                    btn = ResourceDisplay.Instance.appLauncherButton;
                }
            }
            if (btn == null && _lastInteractedButton != null && _trackedButtons.ContainsKey(_lastInteractedButton))
            {
                btn = _lastInteractedButton;
            }
            if (btn == null) return;

            if (!TryGetAnchorObstacleBounds(btn, out Rect obstacleBounds, out _, out Rect btnBounds, out _))
                return;

            RectTransform frameRt = appFrame.GetComponent<RectTransform>();
            if (frameRt == null) return;

            Canvas canvas = frameRt.GetComponentInParent<Canvas>();
            float canvasScale = (canvas != null && canvas.scaleFactor > 0.01f) ? canvas.scaleFactor : 1f;
            float localScaleX = Mathf.Abs(frameRt.localScale.x) > 0.01f ? Mathf.Abs(frameRt.localScale.x) : 1f;
            float localScaleY = Mathf.Abs(frameRt.localScale.y) > 0.01f ? Mathf.Abs(frameRt.localScale.y) : 1f;
            float effScaleX = canvasScale * localScaleX;
            float effScaleY = canvasScale * localScaleY;

            // 获取 frameRt 在屏幕物理像素空间的精确尺寸（防止因 Canvas 缩放/UI Scale 导致尺寸低估而被 Dock 遮挡）
            float frameW;
            float frameH;
            if (GetRectTransformBounds(frameRt, out Rect currentFrameBounds, out _) && currentFrameBounds.width > 50f && currentFrameBounds.height > 50f)
            {
                frameW = currentFrameBounds.width;
                frameH = currentFrameBounds.height;
            }
            else
            {
                frameW = Mathf.Max(frameRt.rect.width * effScaleX, 280f * effScaleX);
                frameH = Mathf.Max(frameRt.rect.height * effScaleY, 180f * effScaleY);
            }

            float targetLeft;
            float targetBottom;

            // Screen 物理像素空间：(0, 0) 为屏幕左下角
            // 边缘亲和度判定：根据 Dock 距离屏幕各边界的物理距离自适应展开，避让整个 Dock 外包围盒
            float distRight = Screen.width - obstacleBounds.xMax;
            float distLeft = obstacleBounds.xMin;
            float distBottom = obstacleBounds.yMin;
            float distTop = Screen.height - obstacleBounds.yMax;

            if (distRight < 120f && distRight <= Mathf.Min(distLeft, distBottom, distTop))
            {
                // Dock 贴靠右边缘：弹窗向左展开，完全避开整个 Dock，垂直对齐按钮中心
                targetLeft = obstacleBounds.xMin - frameW - 8f;
                targetBottom = btnBounds.center.y - frameH * 0.5f;
            }
            else if (distLeft < 120f && distLeft <= Mathf.Min(distRight, distBottom, distTop))
            {
                // Dock 贴靠左边缘：弹窗向右展开，完全避开整个 Dock，垂直对齐按钮中心
                targetLeft = obstacleBounds.xMax + 8f;
                targetBottom = btnBounds.center.y - frameH * 0.5f;
            }
            else if (distBottom < 120f && distBottom <= Mathf.Min(distLeft, distRight, distTop))
            {
                // Dock 贴靠底边缘：弹窗向上展开，完全避开整个 Dock，水平对齐按钮中心
                targetLeft = btnBounds.center.x - frameW * 0.5f;
                targetBottom = obstacleBounds.yMax + 8f;
            }
            else if (distTop < 120f && distTop <= Mathf.Min(distLeft, distRight, distBottom))
            {
                // Dock 贴靠顶边缘：弹窗向下展开，完全避开整个 Dock，水平对齐按钮中心
                targetLeft = btnBounds.center.x - frameW * 0.5f;
                targetBottom = obstacleBounds.yMin - frameH - 8f;
            }
            else if (obstacleBounds.center.x >= Screen.width * 0.5f)
            {
                // Dock 居于右半屏：向左展开
                targetLeft = obstacleBounds.xMin - frameW - 8f;
                targetBottom = btnBounds.center.y - frameH * 0.5f;
            }
            else
            {
                // Dock 居于左半屏：向右展开
                targetLeft = obstacleBounds.xMax + 8f;
                targetBottom = btnBounds.center.y - frameH * 0.5f;
            }

            // 安全边界约束
            targetLeft = Mathf.Clamp(targetLeft, 10f, Screen.width - frameW - 10f);
            targetBottom = Mathf.Clamp(targetBottom, 10f, Screen.height - frameH - 10f);

            Vector2 pivot = frameRt.pivot;
            float pivotScreenX = targetLeft + pivot.x * frameW;
            float pivotScreenY = targetBottom + pivot.y * frameH;

            RectTransform parentRt = frameRt.parent as RectTransform;
            Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            if (parentRt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, new Vector2(pivotScreenX, pivotScreenY), uiCam, out Vector2 localPoint))
            {
                frameRt.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            }
            else
            {
                frameRt.position = ScreenToMainCanvasWorld(new Vector2(pivotScreenX, pivotScreenY));
            }

            // 确保窗口内部渲染与交互正常
            var cg = appFrame.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        public static void RepositionUguiMenu(RectTransform menuRt, Rect obstacleBounds, Rect btnBounds)
        {
            if (menuRt == null) return;

            Canvas canvas = menuRt.GetComponentInParent<Canvas>();
            float canvasScale = (canvas != null && canvas.scaleFactor > 0.01f) ? canvas.scaleFactor : 1f;
            float localScaleX = Mathf.Abs(menuRt.localScale.x) > 0.01f ? Mathf.Abs(menuRt.localScale.x) : 1f;
            float localScaleY = Mathf.Abs(menuRt.localScale.y) > 0.01f ? Mathf.Abs(menuRt.localScale.y) : 1f;
            float effScaleX = canvasScale * localScaleX;
            float effScaleY = canvasScale * localScaleY;

            float w;
            float h;
            if (GetRectTransformBounds(menuRt, out Rect currentMenuBounds, out _) && currentMenuBounds.width > 30f && currentMenuBounds.height > 30f)
            {
                w = currentMenuBounds.width;
                h = currentMenuBounds.height;
            }
            else
            {
                w = Mathf.Max(menuRt.rect.width * effScaleX, 180f * effScaleX);
                h = Mathf.Max(menuRt.rect.height * effScaleY, 120f * effScaleY);
            }

            float targetLeft;
            float targetBottom;

            float distRight = Screen.width - obstacleBounds.xMax;
            float distLeft = obstacleBounds.xMin;
            float distBottom = obstacleBounds.yMin;
            float distTop = Screen.height - obstacleBounds.yMax;

            if (distRight < 120f && distRight <= Mathf.Min(distLeft, distBottom, distTop))
            {
                targetLeft = obstacleBounds.xMin - w - 8f;
                targetBottom = btnBounds.center.y - h * 0.5f;
            }
            else if (distLeft < 120f && distLeft <= Mathf.Min(distRight, distBottom, distTop))
            {
                targetLeft = obstacleBounds.xMax + 8f;
                targetBottom = btnBounds.center.y - h * 0.5f;
            }
            else if (distBottom < 120f && distBottom <= Mathf.Min(distLeft, distRight, distTop))
            {
                targetLeft = btnBounds.center.x - w * 0.5f;
                targetBottom = obstacleBounds.yMax + 8f;
            }
            else if (distTop < 120f && distTop <= Mathf.Min(distLeft, distRight, distBottom))
            {
                targetLeft = btnBounds.center.x - w * 0.5f;
                targetBottom = obstacleBounds.yMin - h - 8f;
            }
            else if (obstacleBounds.center.x >= Screen.width * 0.5f)
            {
                targetLeft = obstacleBounds.xMin - w - 8f;
                targetBottom = btnBounds.center.y - h * 0.5f;
            }
            else
            {
                targetLeft = obstacleBounds.xMax + 8f;
                targetBottom = btnBounds.center.y - h * 0.5f;
            }

            targetLeft = Mathf.Clamp(targetLeft, 10f, Screen.width - w - 10f);
            targetBottom = Mathf.Clamp(targetBottom, 10f, Screen.height - h - 10f);

            Vector2 pivot = menuRt.pivot;
            float pivotScreenX = targetLeft + pivot.x * w;
            float pivotScreenY = targetBottom + pivot.y * h;

            RectTransform parentRt = menuRt.parent as RectTransform;
            Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            if (parentRt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, new Vector2(pivotScreenX, pivotScreenY), uiCam, out Vector2 localPoint))
            {
                menuRt.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            }
        }

        public static void RepositionUguiMenu(RectTransform menuRt, Rect dockBtnScreenBounds)
        {
            RepositionUguiMenu(menuRt, dockBtnScreenBounds, dockBtnScreenBounds);
        }

        /// <summary>
        /// 点击后安全对齐：若点击了原生应用按钮 (如 ResourceDisplay)，精密吸附其 GenericAppFrame 窗口
        /// </summary>
        public static void OnPostButtonClick(ApplicationLauncherButton btn)
        {
            if (btn == null) return;
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2) return;

            // 1. 如果该按钮对应原生 KSP ResourceDisplay，立即刷新其 GenericAppFrame 停靠对齐
            try
            {
                var resFrame = GetResourceDisplayAppFrame();
                if (resFrame != null && (ResourceDisplay.Instance.appLauncherButton == btn || GetFrameButton(resFrame) == btn))
                {
                    RepositionGenericAppFrame(resFrame);
                }
            }
            catch { }

            // 2. 检查 ApplicationLauncher.Instance.appSpace 下的所有 GenericAppFrame
            try
            {
                if (ApplicationLauncher.Instance != null && ApplicationLauncher.Instance.appSpace != null)
                {
                    var frames = ApplicationLauncher.Instance.appSpace.GetComponentsInChildren<GenericAppFrame>(true);
                    for (int i = 0; i < frames.Length; i++)
                    {
                        var frame = frames[i];
                        if (frame != null && (GetFrameButton(frame) == btn || frames.Length == 1))
                        {
                            RepositionGenericAppFrame(frame);
                        }
                    }
                }
            }
            catch { }

            // 3. 扫描 MainCanvas 下的所有第三方 UGUI 弹出菜单 (如 AtmosphereAutopilot MainMenuGUI 等)
            try
            {
                Canvas mainCanvas = MainCanvasUtil.MainCanvas;
                if (mainCanvas != null && TryGetAnchorObstacleBounds(btn, out Rect obstacleBounds, out _, out Rect btnBounds, out _))
                {
                    var rects = mainCanvas.GetComponentsInChildren<RectTransform>(false);
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var rt = rects[i];
                        if (rt == null || rt == mainCanvas.transform) continue;
                        string n = rt.gameObject.name;
                        if (n.Contains("AtmosphereAutopilot") || n.Contains("MainMenu") || n.Contains("ToolbarMenu") || n.Contains("Flyout"))
                        {
                            RepositionUguiMenu(rt, obstacleBounds, btnBounds);
                        }
                    }
                }
            }
            catch { }
        }

        public static void ApplyDynamicHarmonyPatches(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                // 1. 核心通用底层挂钩：UnityEngine.GUI.DoWindow
                MethodInfo doWinMethod = typeof(UnityEngine.GUI).GetMethod("DoWindow", BindingFlags.Static | BindingFlags.NonPublic);
                if (doWinMethod != null)
                {
                    var prefix = new HarmonyMethod(typeof(Patch_GUI_DoWindow), nameof(Patch_GUI_DoWindow.Prefix));
                    var postfix = new HarmonyMethod(typeof(Patch_GUI_DoWindow), nameof(Patch_GUI_DoWindow.Postfix));
                    harmony.Patch(doWinMethod, prefix: prefix, postfix: postfix);
                    Debug.Log("[ModularFlightPanel] Successfully applied universal Harmony patch on UnityEngine.GUI.DoWindow.");
                }
                else
                {
                    Debug.LogWarning("[ModularFlightPanel] UnityEngine.GUI.DoWindow method not found.");
                }

                // 2. 针对 AtmosphereAutopilot 等具有微小间隙自动关闭特性的模组进行动态悬停安全防护（非强制，容错）
                try
                {
                    var aaWinType = AccessTools.TypeByName("AtmosphereAutopilot.AppLauncherWindow");
                    if (aaWinType != null)
                    {
                        var onGuiCustom = AccessTools.Method(aaWinType, "OnGUICustom");
                        if (onGuiCustom != null)
                        {
                            var prefix = new HarmonyMethod(typeof(Patch_AA_HoverSafety), nameof(Patch_AA_HoverSafety.Prefix));
                            harmony.Patch(onGuiCustom, prefix: prefix);
                            Debug.Log("[ModularFlightPanel] Dynamic hover bridge patch applied for AtmosphereAutopilot.");
                        }
                    }
                }
                catch { }

                // 3. 核心通用挂钩：KSP 原生应用窗口 GenericAppFrame.Reposition (ResourceDisplay / Delta-V / Thermal 等)
                try
                {
                    MethodInfo repoMethod = typeof(GenericAppFrame).GetMethod("Reposition", BindingFlags.Instance | BindingFlags.Public);
                    if (repoMethod != null)
                    {
                        var postfix = new HarmonyMethod(typeof(Patch_GenericAppFrame_Reposition), nameof(Patch_GenericAppFrame_Reposition.Postfix));
                        harmony.Patch(repoMethod, postfix: postfix);
                        Debug.Log("[ModularFlightPanel] Successfully applied universal Harmony patch on GenericAppFrame.Reposition.");
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] Dynamic Harmony patch setup error: {ex.Message}");
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
            // 始终放行原版 GetAnchorUR() 运行，防止 GenericAppFrame.UpdateDraggingBoundsAtBottom 被错误的世界坐标抛射出屏幕！
            return true;
        }
    }

    /// <summary>
    /// KSP 原生应用窗口 GenericAppFrame 重定位挂钩补丁：
    /// 确保 ResourceDisplay、Delta-V、热力等原生窗口在 Dock 模式下精密对齐至对应 Dock 按钮，绝不飞出屏幕
    /// </summary>
    [HarmonyPatch(typeof(GenericAppFrame), "Reposition")]
    public static class Patch_GenericAppFrame_Reposition
    {
        [HarmonyPostfix]
        public static void Postfix(GenericAppFrame __instance)
        {
            DockAnchorTracker.RepositionGenericAppFrame(__instance);
        }
    }

    /// <summary>
    /// 全局底层 IMGUI 窗口拦截补丁：挂钩 UnityEngine.GUI.DoWindow
    /// </summary>
    public static class Patch_GUI_DoWindow
    {
        public static void Prefix(int id, ref Rect clientRect, GUI.WindowFunction func)
        {
            DockAnchorTracker.TryRelocateImguiWindow(id, ref clientRect, func);
        }

        public static void Postfix(int id, Rect __result)
        {
            DockAnchorTracker.OnPostDoWindow(id, __result);
        }
    }

    /// <summary>
    /// AtmosphereAutopilot 悬停移动安全补丁：防止鼠标跨越按钮与弹窗之间微小间隙时被 AA 错误判定为移出而意外关闭
    /// </summary>
    public static class Patch_AA_HoverSafety
    {
        public static bool Prefix(object __instance)
        {
            if (ThemeManager.Instance == null || ThemeManager.Instance.ToolbarStyleMode != 2)
                return true;

            try
            {
                var showHoverField = AccessTools.Field(__instance.GetType(), "show_while_hover");
                if (showHoverField == null) return true;

                bool showWhileHover = (bool)showHoverField.GetValue(__instance);
                if (!showWhileHover) return true;

                var winField = AccessTools.Field(__instance.GetType(), "window");
                if (winField == null) return true;
                Rect win = (Rect)winField.GetValue(__instance);

                Vector2 mouse = Mouse.screenPos;
                if (win.Contains(mouse)) return true;

                if (DockAnchorTracker.LastInteractedButton != null &&
                    DockAnchorTracker.TryGetAnchorObstacleBounds(DockAnchorTracker.LastInteractedButton, out _, out Rect imguiBounds, out _, out _))
                {
                    float minX = Mathf.Min(win.xMin, imguiBounds.xMin) - 15f;
                    float maxX = Mathf.Max(win.xMax, imguiBounds.xMax) + 15f;
                    float minY = Mathf.Min(win.yMin, imguiBounds.yMin) - 15f;
                    float maxY = Mathf.Max(win.yMax, imguiBounds.yMax) + 15f;
                    Rect bridgeRect = Rect.MinMaxRect(minX, minY, maxX, maxY);

                    if (bridgeRect.Contains(mouse))
                    {
                        return false;
                    }
                }
            }
            catch { }

            return true;
        }
    }
#endif
}

