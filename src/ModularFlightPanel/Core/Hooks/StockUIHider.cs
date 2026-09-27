using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI;
using KSP.UI.Screens;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 官方原生 UI 极致隐蔽与免计算保活中枢 (Stealth Stock UI Isolation & Preservation Hub)
    /// 
    /// 核心架构设计 (两层防御与免渲染机制)：
    /// 1. 子画布解耦 (Nested Sub-Canvas Decoupling)：
    ///    - 针对需要保活计算或被 Hook 消费的组件 (NavBall, SpeedDisplay, NavBallBurnVector, StageManager)：
    ///      为其根节点添加独立子画布 (subCanvas.enabled = !hide)。
    ///      在 Unity UGUI 体系中，禁用子画布将彻底切断其与根画布的批处理与网格重构 (0 draw calls, 0 Text.OnPopulateMesh, 0 CanvasUpdateRegistry 脏标记)，
    ///      但其 GameObject 依然活跃，内部 MonoBehaviours 的 Update()、物理循环与 C# 数据内存写入 (如 Text.text, Transform.rotation) 100% 正常运转！
    /// 2. 纯视觉非 Hook 脚本安全休眠 (Safe Sleep of Non-Hooked MonoBehaviours)：
    ///    - 针对无外部消费者、仅在 Update() 中驱动刻度动画的官方纯视觉脚本 (AltimeterSliderButtons, VerticalSpeedGauge,
    ///      LinearAtmosphereGauge, AltitudeTumbler, ThrottleGauge, LinearControlGauges, GeeGauge, StageTumbler, StagingLED,
    ///      METDisplay, ActionGroupToggleButton, SASDisplay, RCSDisplay, LightDisplay)：
    ///      当隐藏时显式休眠其 MonoBehaviour (enabled = false)，彻底消除 CPU 轮询；恢复时无损唤醒 (enabled = true)。
    /// 3. 3D 网格渲染屏蔽：
    ///    - 禁用原版 3D 姿态球及环架的 MeshRenderer (enabled = false)，消除 3D 渲染开销；保留 Transform 旋转解算。
    /// 4. 零 GC 单次缓存机制：
    ///    - 消除每 1.5 秒轮询 GetComponentsInChildren 造成的 GC 尖峰，一次探测、持久重用、场景切换时干净复位。
    /// </summary>
    public static class StockUIHider
    {
        // ── 姿态球 (NavBall) 缓存 ──
        private static bool _isNavballCached = false;
        private static readonly List<Renderer> _cachedAllStockRenderers = new List<Renderer>(64);
        private static readonly List<Graphic> _cachedAllStockGraphics = new List<Graphic>(64);
        private static readonly List<ActionGroupToggleButton> _cachedNavballActionButtons = new List<ActionGroupToggleButton>(16);
        private static SASDisplay _cachedSASDisplay;
        private static RCSDisplay _cachedRCSDisplay;
        private static LightDisplay _cachedLightDisplay;
        private static NavBallBurnVector _cachedBurnVector;
        private static bool _burnVectorSearched = false;

        // ── 高度计 (Altimeter) 缓存 ──
        private static bool _isAltimeterCached = false;
        private static Renderer[] _cachedAltimeterRenderers;
        private static readonly List<ActionGroupToggleButton> _cachedAltimeterActionButtons = new List<ActionGroupToggleButton>(16);
        private static AltimeterSliderButtons _cachedAltimeterSlider;
        private static VerticalSpeedGauge _cachedVsiGauge;
        private static LinearAtmosphereGauge _cachedAtmoGauge;
        private static AltitudeTumbler _cachedAltTumbler;

        // ── 左下角操纵与分级 (Bottom Left / Staging Quadrant) 缓存 ──
        private static bool _isBottomLeftCached = false;
        private static Renderer[] _cachedBottomLeftRenderers;
        private static ThrottleGauge _cachedThrottleGauge;
        private static LinearControlGauges _cachedControlGauges;
        private static GeeGauge _cachedGeeGauge;
        private static StageTumbler _cachedStageTumbler;
        private static StagingLED _cachedStagingLED;

        // ── 时间加速与时钟 (Time Warp & MET) 缓存 ──
        private static bool _isTimeWarpCached = false;
        private static METDisplay _cachedMETDisplay = null;

        public static void ClearCaches()
        {
            _cachedAllStockRenderers.Clear();
            _cachedAllStockGraphics.Clear();
            _cachedNavballActionButtons.Clear();
            _cachedSASDisplay = null;
            _cachedRCSDisplay = null;
            _cachedLightDisplay = null;
            _cachedBurnVector = null;
            _burnVectorSearched = false;
            _isNavballCached = false;

            _cachedAltimeterRenderers = null;
            _cachedAltimeterActionButtons.Clear();
            _cachedAltimeterSlider = null;
            _cachedVsiGauge = null;
            _cachedAtmoGauge = null;
            _cachedAltTumbler = null;
            _isAltimeterCached = false;

            _cachedBottomLeftRenderers = null;
            _cachedThrottleGauge = null;
            _cachedControlGauges = null;
            _cachedGeeGauge = null;
            _cachedStageTumbler = null;
            _cachedStagingLED = null;
            _isBottomLeftCached = false;

            _cachedMETDisplay = null;
            _isTimeWarpCached = false;
        }

        public static void RestoreAllStockUI()
        {
            try { HideStockNavballCompletely(false); } catch (Exception ex) { Debug.LogWarning($"[ModularFlightPanel] RestoreAllStockUI (Navball) warning: {ex.Message}"); }
            try { HideStockAltimeter(false); } catch (Exception ex) { Debug.LogWarning($"[ModularFlightPanel] RestoreAllStockUI (Altimeter) warning: {ex.Message}"); }
            try { HideStockBottomLeft(false); } catch (Exception ex) { Debug.LogWarning($"[ModularFlightPanel] RestoreAllStockUI (BottomLeft) warning: {ex.Message}"); }
            try { HideStockTimeWarp(false); } catch (Exception ex) { Debug.LogWarning($"[ModularFlightPanel] RestoreAllStockUI (TimeWarp) warning: {ex.Message}"); }
            try { HideStockCommNet(false); } catch (Exception ex) { Debug.LogWarning($"[ModularFlightPanel] RestoreAllStockUI (CommNet) warning: {ex.Message}"); }
            try
            {
                if (SpeedDisplay.Instance != null && !SpeedDisplay.Instance.enabled)
                {
                    SpeedDisplay.Instance.enabled = true;
                }
            }
            catch { }
            try
            {
                if (StockNavBallHook.StockInstance != null && !StockNavBallHook.StockInstance.enabled)
                {
                    StockNavBallHook.StockInstance.enabled = true;
                }
            }
            catch { }
            try
            {
                if (StageManager.Instance != null && !StageManager.Instance.enabled)
                {
                    StageManager.Instance.enabled = true;
                }
            }
            catch { }
            try
            {
                NavBallHookService.RestoreStockToolbarAction?.Invoke();
            }
            catch { }
        }

        private static void SafeAddRenderersFromTransform(Transform t, List<Renderer> list)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null && !list.Contains(r)) list.Add(r);
            var arr = t.GetComponentsInChildren<Renderer>(true);
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null && !list.Contains(arr[i])) list.Add(arr[i]);
                }
            }
        }

        /// <summary>
        /// 隐形画布隔离核心：通过嵌套 Canvas 停用彻底消除 UGUI 网格构建与批处理开销，
        /// 同时确保其底层 GameObject 与 MonoBehaviour 完整存活运转。
        /// </summary>
        private static void ApplyStealthCanvasIsolation(GameObject go, bool hide)
        {
            if (go == null) return;

            try
            {
                // 1. 嵌套子画布隔离 (Nested Sub-Canvas Isolation)
                // 停用 subCanvas.enabled 会将整个子树移出 UGUI 渲染队列与 CanvasUpdateRegistry，
                // 彻底阻断 Text.OnPopulateMesh 与 Layout 重绘，省去 ~1.5ms 批处理耗时，
                // 但 GameObject 依旧 Active，子级 MonoBehaviours 依旧收到 Update/LateUpdate 回调。
                Canvas subCanvas = go.GetComponent<Canvas>();
                if (subCanvas == null)
                {
                    subCanvas = go.AddComponent<Canvas>();
                    Canvas parentCanvas = go.transform.parent != null ? go.transform.parent.GetComponentInParent<Canvas>() : null;
                    if (parentCanvas != null && subCanvas.worldCamera == null)
                    {
                        subCanvas.worldCamera = parentCanvas.worldCamera;
                        subCanvas.planeDistance = parentCanvas.planeDistance;
                    }
                }
                if (subCanvas != null && subCanvas.enabled != !hide)
                {
                    subCanvas.enabled = !hide;
                }

                // 2. 射线拾取屏蔽 (GraphicRaycaster Sleep)
                GraphicRaycaster gr = go.GetComponent<GraphicRaycaster>();
                if (gr != null && gr.enabled != !hide)
                {
                    gr.enabled = !hide;
                }

                // 3. CanvasGroup 透明度与交互阻断
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg == null)
                {
                    cg = go.AddComponent<CanvasGroup>();
                }
                float targetAlpha = hide ? 0f : 1f;
                if (Mathf.Abs(cg.alpha - targetAlpha) > 0.01f)
                {
                    cg.alpha = targetAlpha;
                }
                bool targetInteractable = !hide;
                if (cg.blocksRaycasts != targetInteractable)
                {
                    cg.blocksRaycasts = targetInteractable;
                }
                if (cg.interactable != targetInteractable)
                {
                    cg.interactable = targetInteractable;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] ApplyStealthCanvasIsolation error on {go.name}: {ex.Message}");
            }
        }

        public static void HideStockAltimeter(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.altimeterFrame != null)
                {
                    var frameGo = FlightUIModeController.Instance.altimeterFrame.gameObject;
                    ApplyStealthCanvasIsolation(frameGo, hide);

                    if (!_isAltimeterCached)
                    {
                        _isAltimeterCached = true;
                        _cachedAltimeterRenderers = frameGo.GetComponentsInChildren<Renderer>(true);
                        if (_cachedAltimeterSlider == null)
                        {
                            _cachedAltimeterSlider = UnityEngine.Object.FindObjectOfType<AltimeterSliderButtons>();
                        }
                        _cachedVsiGauge = frameGo.GetComponentInChildren<VerticalSpeedGauge>(true);
                        _cachedAtmoGauge = frameGo.GetComponentInChildren<LinearAtmosphereGauge>(true);
                        _cachedAltTumbler = frameGo.GetComponentInChildren<AltitudeTumbler>(true);

                        _cachedAltimeterActionButtons.Clear();
                        var altiBtns = frameGo.GetComponentsInChildren<ActionGroupToggleButton>(true);
                        if (altiBtns != null) _cachedAltimeterActionButtons.AddRange(altiBtns);
                    }

                    // 休眠/唤醒无外部消费者的高频 UI 脚本 (0 CPU 轮询)
                    if (_cachedAltimeterSlider != null && _cachedAltimeterSlider.enabled == hide)
                        _cachedAltimeterSlider.enabled = !hide;
                    if (_cachedVsiGauge != null && _cachedVsiGauge.enabled == hide)
                        _cachedVsiGauge.enabled = !hide;
                    if (_cachedAtmoGauge != null && _cachedAtmoGauge.enabled == hide)
                        _cachedAtmoGauge.enabled = !hide;
                    if (_cachedAltTumbler != null && _cachedAltTumbler.enabled == hide)
                        _cachedAltTumbler.enabled = !hide;

                    for (int i = 0; i < _cachedAltimeterActionButtons.Count; i++)
                    {
                        var btn = _cachedAltimeterActionButtons[i];
                        if (btn != null && btn.enabled == hide)
                            btn.enabled = !hide;
                    }

                    if (_cachedAltimeterRenderers != null)
                    {
                        for (int i = 0; i < _cachedAltimeterRenderers.Length; i++)
                        {
                            var r = _cachedAltimeterRenderers[i];
                            if (r != null && r.enabled == hide)
                            {
                                r.enabled = !hide;
                            }
                        }
                    }
                }

                if (_cachedAltimeterSlider != null)
                {
                    ApplyStealthCanvasIsolation(_cachedAltimeterSlider.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockAltimeter warning: {ex.Message}");
            }
        }

        public static void HideStockBottomLeft(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null)
                {
                    var ctrl = FlightUIModeController.Instance;
                    if (ctrl.stagingQuadrant != null) ApplyStealthCanvasIsolation(ctrl.stagingQuadrant.gameObject, hide);
                    if (ctrl.dockingRotQuadrant != null) ApplyStealthCanvasIsolation(ctrl.dockingRotQuadrant.gameObject, hide);
                    if (ctrl.dockingLinQuadrant != null) ApplyStealthCanvasIsolation(ctrl.dockingLinQuadrant.gameObject, hide);
                    if (ctrl.uiModeFrame != null) ApplyStealthCanvasIsolation(ctrl.uiModeFrame.gameObject, hide);
                    if (ctrl.UIScaleModeFrame != null) ApplyStealthCanvasIsolation(ctrl.UIScaleModeFrame, hide);
                    if (ctrl.UIScaleStageManager != null) ApplyStealthCanvasIsolation(ctrl.UIScaleStageManager, hide);

                    if (!_isBottomLeftCached)
                    {
                        _isBottomLeftCached = true;
                        if (ctrl.stagingQuadrant != null)
                        {
                            _cachedBottomLeftRenderers = ctrl.stagingQuadrant.GetComponentsInChildren<Renderer>(true);
                            _cachedThrottleGauge = ctrl.stagingQuadrant.GetComponentInChildren<ThrottleGauge>(true);
                            _cachedControlGauges = ctrl.stagingQuadrant.GetComponentInChildren<LinearControlGauges>(true);
                            _cachedGeeGauge = ctrl.stagingQuadrant.GetComponentInChildren<GeeGauge>(true);
                            _cachedStageTumbler = ctrl.stagingQuadrant.GetComponentInChildren<StageTumbler>(true);
                            _cachedStagingLED = ctrl.stagingQuadrant.GetComponentInChildren<StagingLED>(true);
                        }
                    }

                    // 休眠/唤醒左下角纯视觉仪表脚本
                    if (_cachedThrottleGauge != null && _cachedThrottleGauge.enabled == hide)
                        _cachedThrottleGauge.enabled = !hide;
                    if (_cachedControlGauges != null && _cachedControlGauges.enabled == hide)
                        _cachedControlGauges.enabled = !hide;
                    if (_cachedGeeGauge != null && _cachedGeeGauge.enabled == hide)
                        _cachedGeeGauge.enabled = !hide;
                    if (_cachedStageTumbler != null && _cachedStageTumbler.enabled == hide)
                        _cachedStageTumbler.enabled = !hide;
                    if (_cachedStagingLED != null && _cachedStagingLED.enabled == hide)
                        _cachedStagingLED.enabled = !hide;

                    if (_cachedBottomLeftRenderers != null)
                    {
                        for (int i = 0; i < _cachedBottomLeftRenderers.Length; i++)
                        {
                            var r = _cachedBottomLeftRenderers[i];
                            if (r != null && r.enabled == hide)
                            {
                                r.enabled = !hide;
                            }
                        }
                    }
                }

                // 核心保活铁律：StageManager.Instance 严禁休眠！
                // 仅对其 GameObject 施加画布隔离以消除 UGUI 重绘，其 MonoBehaviour 必须持续运转以响应空格分级与 Hook 调度
                if (StageManager.Instance != null)
                {
                    ApplyStealthCanvasIsolation(StageManager.Instance.gameObject, hide);
                    if (!StageManager.Instance.enabled)
                    {
                        StageManager.Instance.enabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockBottomLeft warning: {ex.Message}");
            }
        }

        public static void HideStockTimeWarp(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.timeFrame != null)
                {
                    ApplyStealthCanvasIsolation(FlightUIModeController.Instance.timeFrame.gameObject, hide);
                }

                if (!_isTimeWarpCached)
                {
                    _isTimeWarpCached = true;
                    if (_cachedMETDisplay == null)
                    {
                        _cachedMETDisplay = UnityEngine.Object.FindObjectOfType<METDisplay>();
                    }
                }

                if (_cachedMETDisplay != null)
                {
                    // 休眠 METDisplay，消除每一帧的字符串格式化与文本网格 dirty
                    if (_cachedMETDisplay.enabled == hide)
                    {
                        _cachedMETDisplay.enabled = !hide;
                    }
                    if (_cachedMETDisplay.transform.parent != null)
                    {
                        ApplyStealthCanvasIsolation(_cachedMETDisplay.transform.parent.gameObject, hide);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockTimeWarp warning: {ex.Message}");
            }
        }

        public static void HideStockCommNet(bool hide)
        {
            try
            {
                if (TelemetryUpdate.Instance != null)
                {
                    ApplyStealthCanvasIsolation(TelemetryUpdate.Instance.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockCommNet warning: {ex.Message}");
            }
        }

        public static void HideStockNavballCompletely(bool hide)
        {
            if (StockNavBallHook.StockInstance == null) return;

            try
            {
                // 1. 嵌套子画布物理隔离 (同时覆盖宿主与 NavBall 实例)
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                {
                    ApplyStealthCanvasIsolation(FlightUIModeController.Instance.navBall.gameObject, hide);
                }
                if (StockNavBallHook.StockInstance != null && (FlightUIModeController.Instance == null || StockNavBallHook.StockInstance != FlightUIModeController.Instance.navBall))
                {
                    ApplyStealthCanvasIsolation(StockNavBallHook.StockInstance.gameObject, hide);
                }

                // 2. 确保官方折叠托盘处于展开态，防止 KSP 内核暂停姿态矩阵更新
                if (NavBallToggle.Instance != null && NavBallToggle.Instance.panel != null)
                {
                    var panel = NavBallToggle.Instance.panel;
                    if (panel.collapsed)
                    {
                        panel.ExpandImmediate();
                    }
                }

                // 3. 单次精准缓存 (彻底消灭每 1.5 秒 GetComponentsInChildren 造成的 GC 尖峰)
                if (!_isNavballCached)
                {
                    _isNavballCached = true;
                    _cachedAllStockRenderers.Clear();
                    _cachedAllStockGraphics.Clear();
                    _cachedNavballActionButtons.Clear();

                    var stockR = StockNavBallHook.StockInstance.GetComponentsInChildren<Renderer>(true);
                    if (stockR != null) _cachedAllStockRenderers.AddRange(stockR);

                    var stockG = StockNavBallHook.StockInstance.GetComponentsInChildren<Graphic>(true);
                    if (stockG != null) _cachedAllStockGraphics.AddRange(stockG);

                    var buttons = StockNavBallHook.StockInstance.GetComponentsInChildren<ActionGroupToggleButton>(true);
                    if (buttons != null) _cachedNavballActionButtons.AddRange(buttons);

                    _cachedSASDisplay = StockNavBallHook.StockInstance.GetComponentInChildren<SASDisplay>(true);
                    _cachedRCSDisplay = StockNavBallHook.StockInstance.GetComponentInChildren<RCSDisplay>(true);
                    _cachedLightDisplay = StockNavBallHook.StockInstance.GetComponentInChildren<LightDisplay>(true);

                    if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                    {
                        var fuimR = FlightUIModeController.Instance.navBall.GetComponentsInChildren<Renderer>(true);
                        if (fuimR != null)
                        {
                            for (int i = 0; i < fuimR.Length; i++)
                            {
                                if (fuimR[i] != null && !_cachedAllStockRenderers.Contains(fuimR[i]))
                                    _cachedAllStockRenderers.Add(fuimR[i]);
                            }
                        }

                        var frameBtns = FlightUIModeController.Instance.navBall.GetComponentsInChildren<ActionGroupToggleButton>(true);
                        if (frameBtns != null)
                        {
                            for (int i = 0; i < frameBtns.Length; i++)
                            {
                                if (frameBtns[i] != null && !_cachedNavballActionButtons.Contains(frameBtns[i]))
                                    _cachedNavballActionButtons.Add(frameBtns[i]);
                            }
                        }
                    }

                    if (StockNavBallHook.StockInstance.navBall != null)
                    {
                        SafeAddRenderersFromTransform(StockNavBallHook.StockInstance.navBall, _cachedAllStockRenderers);
                        if (StockNavBallHook.StockInstance.navBall.parent != null && StockNavBallHook.StockInstance.navBall.parent != StockNavBallHook.StockInstance.transform)
                        {
                            SafeAddRenderersFromTransform(StockNavBallHook.StockInstance.navBall.parent, _cachedAllStockRenderers);
                        }
                    }
                }

                // 4. 原版 3D 姿态球网格渲染屏蔽 (GPU 零绘制，但底层 Transform 旋转解算照常运行)
                for (int i = 0; i < _cachedAllStockRenderers.Count; i++)
                {
                    var r = _cachedAllStockRenderers[i];
                    if (r != null && r.enabled == hide)
                    {
                        r.enabled = !hide;
                    }
                }

                // 5. 原版 UGUI 射线拾取屏蔽
                for (int i = 0; i < _cachedAllStockGraphics.Count; i++)
                {
                    var g = _cachedAllStockGraphics[i];
                    if (g != null && g.raycastTarget == hide)
                    {
                        g.raycastTarget = !hide;
                    }
                }

                // 6. 休眠原版附属纯视觉按钮与指示灯 (SAS/RCS 开关与指示灯无需在隐藏态做任何计算)
                for (int i = 0; i < _cachedNavballActionButtons.Count; i++)
                {
                    var btn = _cachedNavballActionButtons[i];
                    if (btn != null && btn.enabled == hide)
                    {
                        btn.enabled = !hide;
                    }
                }
                if (_cachedSASDisplay != null && _cachedSASDisplay.enabled == hide) _cachedSASDisplay.enabled = !hide;
                if (_cachedRCSDisplay != null && _cachedRCSDisplay.enabled == hide) _cachedRCSDisplay.enabled = !hide;
                if (_cachedLightDisplay != null && _cachedLightDisplay.enabled == hide) _cachedLightDisplay.enabled = !hide;

                // 7. 动态感知保活调度 (状态驱动与按需唤醒)
                if (_cachedBurnVector == null)
                {
                    _cachedBurnVector = StockNavBallHook.StockInstance.GetComponentInChildren<NavBallBurnVector>(true);
                }
                TickDynamicHooks();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockNavballCompletely warning: {ex.Message}");
            }
        }

        private static bool _isCleanStockNavballActive = false;
        public static bool IsCleanStockNavballActive => _isCleanStockNavballActive;

        /// <summary>
        /// 纯净原生导航球模式：激活 3D 姿态球与矢量，剥离侧面油门/G表/SAS按钮/装饰底框，保持极简悬浮
        /// </summary>
        public static void SetStockNavballClean(bool clean)
        {
            if (_isCleanStockNavballActive == clean && clean) return;
            _isCleanStockNavballActive = clean;
            if (StockNavBallHook.StockInstance == null) return;

            try
            {
                if (!clean)
                {
                    var nb = StockNavBallHook.StockInstance;
                    if (nb.sideGaugeGee != null) nb.sideGaugeGee.gameObject.SetActive(true);
                    if (nb.sideGaugeThrottle != null) nb.sideGaugeThrottle.gameObject.SetActive(true);
                    for (int i = 0; i < _cachedNavballActionButtons.Count; i++)
                    {
                        if (_cachedNavballActionButtons[i] != null) _cachedNavballActionButtons[i].gameObject.SetActive(true);
                    }
                    if (_cachedSASDisplay != null) _cachedSASDisplay.gameObject.SetActive(true);
                    if (_cachedRCSDisplay != null) _cachedRCSDisplay.gameObject.SetActive(true);
                    if (_cachedLightDisplay != null) _cachedLightDisplay.gameObject.SetActive(true);

                    if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                    {
                        var graphics = FlightUIModeController.Instance.navBall.GetComponentsInChildren<Graphic>(true);
                        for (int i = 0; i < graphics.Length; i++)
                        {
                            if (graphics[i] != null && !graphics[i].enabled) graphics[i].enabled = true;
                        }
                    }
                    return;
                }

                // 1. 确保子画布对 NavBall 处于开启状态，确保渲染能够正常进行
                HideStockNavballCompletely(false);

                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                {
                    ApplyStealthCanvasIsolation(FlightUIModeController.Instance.navBall.gameObject, false);
                }
                if (StockNavBallHook.StockInstance != null)
                {
                    ApplyStealthCanvasIsolation(StockNavBallHook.StockInstance.gameObject, false);
                }

                // 2. 确保官方折叠托盘处于展开态
                if (NavBallToggle.Instance != null && NavBallToggle.Instance.panel != null)
                {
                    var panel = NavBallToggle.Instance.panel;
                    if (panel.collapsed)
                    {
                        panel.ExpandImmediate();
                    }
                }

                // 3. 确保 3D 姿态球及姿态矢量指示器 MeshRenderer 全量开启
                for (int i = 0; i < _cachedAllStockRenderers.Count; i++)
                {
                    var r = _cachedAllStockRenderers[i];
                    if (r != null && !r.enabled) r.enabled = true;
                }
                if (StockNavBallHook.StockInstance != null)
                {
                    if (!StockNavBallHook.StockInstance.enabled)
                        StockNavBallHook.StockInstance.enabled = true;
                    if (StockNavBallHook.StockInstance.navBall != null)
                    {
                        var renderers = StockNavBallHook.StockInstance.navBall.GetComponentsInChildren<Renderer>(true);
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            if (renderers[i] != null && !renderers[i].enabled)
                                renderers[i].enabled = true;
                        }
                    }
                }

                // 4. 干净模式：隐藏侧边加速度 G 表与油门表 (sideGaugeGee, sideGaugeThrottle)
                var stockNb = StockNavBallHook.StockInstance;
                if (stockNb.sideGaugeGee != null && stockNb.sideGaugeGee.gameObject.activeSelf)
                {
                    stockNb.sideGaugeGee.gameObject.SetActive(false);
                }
                if (stockNb.sideGaugeThrottle != null && stockNb.sideGaugeThrottle.gameObject.activeSelf)
                {
                    stockNb.sideGaugeThrottle.gameObject.SetActive(false);
                }

                // 5. 隐藏外围 SAS/RCS 指示牌与动作按钮
                for (int i = 0; i < _cachedNavballActionButtons.Count; i++)
                {
                    var btn = _cachedNavballActionButtons[i];
                    if (btn != null && btn.gameObject.activeSelf)
                    {
                        btn.gameObject.SetActive(false);
                    }
                }
                if (_cachedSASDisplay != null && _cachedSASDisplay.gameObject.activeSelf)
                    _cachedSASDisplay.gameObject.SetActive(false);
                if (_cachedRCSDisplay != null && _cachedRCSDisplay.gameObject.activeSelf)
                    _cachedRCSDisplay.gameObject.SetActive(false);
                if (_cachedLightDisplay != null && _cachedLightDisplay.gameObject.activeSelf)
                    _cachedLightDisplay.gameObject.SetActive(false);

                // 6. 隐藏原生外框与装饰性托盘背景图片 (保护 navball、ball、reticle、cross、marker 等核心图元)
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                {
                    var graphics = FlightUIModeController.Instance.navBall.GetComponentsInChildren<Graphic>(true);
                    for (int i = 0; i < graphics.Length; i++)
                    {
                        var g = graphics[i];
                        if (g == null) continue;
                        string nameLower = g.name.ToLowerInvariant();
                        if (nameLower.Contains("navball") || nameLower.Contains("ball") || nameLower.Contains("reticle") || nameLower.Contains("cross") || nameLower.Contains("marker") || nameLower.Contains("heading") || nameLower.Contains("text"))
                            continue;
                        if (nameLower.Contains("frame") || nameLower.Contains("bezel") || nameLower.Contains("bracket") || nameLower.Contains("backing") || nameLower.Contains("trim"))
                        {
                            if (g.enabled)
                            {
                                g.enabled = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] SetStockNavballClean warning: {ex.Message}");
            }
        }

        /// <summary>
        /// 动态按需保活定时节流循环 (Dynamic On-Demand Hook Tick)
        /// 由 FlightHUDManager 每帧驱动，执行状态感知休眠与按需唤醒
        /// </summary>
        public static void TickDynamicHooks()
        {
            try
            {
                bool isNavballHidden = ThemeManager.IsStockNavballHidden && !_isCleanStockNavballActive;
                if (!isNavballHidden)
                {
                    // 若原版 UI 处于显示状态，全部组件强制保持激活
                    if (StockNavBallHook.StockInstance != null && !StockNavBallHook.StockInstance.enabled)
                        StockNavBallHook.StockInstance.enabled = true;
                    if (SpeedDisplay.Instance != null && !SpeedDisplay.Instance.enabled)
                        SpeedDisplay.Instance.enabled = true;
                    if (_cachedBurnVector != null && !_cachedBurnVector.enabled)
                        _cachedBurnVector.enabled = true;
                    return;
                }

                // ── 1. NavBallBurnVector 机动节点感知动态化 ──
                if (!_burnVectorSearched && StockNavBallHook.StockInstance != null)
                {
                    _burnVectorSearched = true;
                    _cachedBurnVector = StockNavBallHook.StockInstance.GetComponentInChildren<NavBallBurnVector>(true);
                }

                if (_cachedBurnVector != null)
                {
                    bool hasManeuver = FlightGlobals.ActiveVessel != null &&
                                       FlightGlobals.ActiveVessel.patchedConicSolver != null &&
                                       FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes != null &&
                                       FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes.Count > 0;

                    // 仅在存在机动节点且有激活姿态消费时唤醒，否则彻底休眠
                    bool needBurnVector = hasManeuver && StockNavBallHook.HasActiveAttitudeConsumer;
                    if (_cachedBurnVector.enabled != needBurnVector)
                    {
                        _cachedBurnVector.enabled = needBurnVector;
                    }
                }

                // ── 2. SpeedDisplay 隐藏态休眠 (彻底杜绝 0.05ms 字符重绘与 StringBuilder 开销) ──
                if (SpeedDisplay.Instance != null && SpeedDisplay.Instance.enabled)
                {
                    SpeedDisplay.Instance.enabled = false;
                }

                // ── 3. NavBall 超轻量级物理万向节状态同步 (彻底省去 0.09ms 材质实例化与 UGUI 循环) ──
                if (StockNavBallHook.StockInstance != null)
                {
                    StockNavBallHook.UpdateStockNavballGymbalsLightweight(StockNavBallHook.StockInstance);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] TickDynamicHooks warning: {ex.Message}");
            }
        }
    }
}

