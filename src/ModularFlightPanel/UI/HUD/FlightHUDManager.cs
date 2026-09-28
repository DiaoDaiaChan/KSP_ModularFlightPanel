using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Navigation;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Widgets.SpaceX;
using ModularFlightPanel.UI.HUD;
#if KSP_RUNTIME
using ModularFlightPanel.UI.Settings;
#endif

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电面板主画布生命周期与编排调度总管 (Flight HUD Master Manager)
    /// 核心职能：
    /// 1. 托管 UGUI 主画布与光栅化配置 (委托至 HUDCanvasManager)
    /// 2. 读取 WidgetLayoutManager 布局数据，通过 WidgetRegistry 泛型工厂装配全量仪表
    /// 3. 单点驱动分频调度中枢 WidgetRenderManager.MasterUpdate 与全局热键调度
    /// 4. 彻底剥离 IMGUI 悬浮工具栏 (委托至 HUDEditModeToolbar) 与性能探针 (委托至 HUDProfilerOverlay)，
    ///    本类实现 0 个 OnGUI 声明，彻底杜绝 Unity IMGUI 常驻引擎轮询与垃圾分配！
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FlightHUDManager : MonoBehaviour
    {
        private static FlightHUDManager _instance;
        public static FlightHUDManager Instance => _instance;

        private readonly HUDCanvasManager _canvasManager = new HUDCanvasManager();
        private GameObject _hudRoot;
        public GameObject HUDRoot => _hudRoot;

        private HUDEditModeToolbar _editModeToolbar;
        private HUDProfilerOverlay _profilerOverlay;

        // 统一小组件集合 (全部继承 BaseFlightWidget)
        private List<BaseFlightWidget> _modularWidgets = new List<BaseFlightWidget>();
        public List<BaseFlightWidget> ModularWidgets => _modularWidgets;

        public float CustomScale
        {
            get => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale;
            set => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = value;
        }

        public static bool IsMouseOverFloatingToolbar { get; set; } = false;

        private int _consecutiveUpdateExceptions = 0;
        private const int MaxConsecutiveExceptionsBeforeTrip = 3;

        private void Awake()
        {
            _instance = this;
        }

        public Canvas Canvas => _canvasManager.Canvas;

        public void SetRenderCamera(Camera cam)
        {
            _canvasManager.SetRenderCamera(cam);
        }

        public void Initialize(Camera renderCam = null)
        {
            try
            {
                WidgetLayoutManager.Instance.Initialize();
                ThemeManager.Instance.OnThemeChanged += OnThemeChanged;
                I18nManager.OnLanguageChanged += HandleLanguageChanged;
                if (WidgetRenderManager.Instance != null)
                {
                    WidgetRenderManager.Instance.OnGlobalRenderScaleChanged += HandleGlobalRenderScaleChanged;
                }

#if KSP_RUNTIME
                // 注册 KSP 原生 UI 显隐事件、DPI 缩放与活动载具切换事件 (支持 F2 一键隐藏 UI 与切船载具专属配置)
                try
                {
                    GameEvents.onHideUI.Add(OnHideUI);
                    GameEvents.onShowUI.Add(OnShowUI);
                    GameEvents.onUIScaleChange.Add(OnUIScaleChange);
                    GameEvents.onVesselChange.Add(OnVesselChange);
                    if (FlightGlobals.ActiveVessel != null)
                    {
                        WidgetLayoutManager.Instance.OnActiveVesselChanged(FlightGlobals.ActiveVessel.vesselName);
                    }
                }
                catch { }
#endif

                BuildCanvas();
                if (renderCam != null)
                {
                    SetRenderCamera(renderCam);
                }
                BuildHUD();
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);

#if KSP_RUNTIME
                // 若进入场景时 KSP 已经处于 F2 隐藏界面状态，立即同步隐藏
                try
                {
                    if (KSP.UI.UIMasterController.Instance != null && !KSP.UI.UIMasterController.Instance.IsUIShowing)
                    {
                        SetUIVisible(false);
                    }
                }
                catch { }
#endif
            }
            catch (Exception ex)
            {
                MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager Initialize fatal error: {ex}");
                MFPSafetyFallback.TriggerFaultFallback("HUD 初始化发生致命异常 (HUD Initialization Error)", ex);
            }
        }

        private void Update()
        {
            if (MFPSafetyFallback.IsFaulted)
            {
                if (_canvasManager != null && _canvasManager.IsCanvasActive)
                {
                    _canvasManager.SetVisible(false);
                }
                return;
            }

            try
            {
                // 性能探针与主干旁路热键响应
                if (Input.GetKeyDown(KeyCode.F11))
                {
                    MFPProfiler.ToggleMasterBypass();
                }
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                }

                bool bypassed = MFPProfiler.IsMasterBypassed;
                if (bypassed)
                {
                    if (_canvasManager.IsCanvasActive)
                    {
                        _canvasManager.SetVisible(false);
                    }
                    if (_editModeToolbar != null && _editModeToolbar.enabled) _editModeToolbar.enabled = false;
                    if (_profilerOverlay != null && _profilerOverlay.enabled) _profilerOverlay.enabled = false;
                    return;
                }
                else
                {
                    if (!_canvasManager.IsCanvasActive && _isUIVisible)
                    {
                        _canvasManager.SetVisible(true);
                    }
                }

                // 按需同步 IMGUI 挂载组件与根画布射线检测状态
                bool isEditMode = WidgetDragHandler.IsEditModeActive && _isUIVisible;
                if (_editModeToolbar != null && _editModeToolbar.enabled != isEditMode)
                {
                    _editModeToolbar.enabled = isEditMode;
                }

                _canvasManager.SetRaycasterEnabled(isEditMode);

                bool showProfiler = MFPProfiler.ShowOverlay && _isUIVisible;
                if (_profilerOverlay != null && _profilerOverlay.enabled != showProfiler)
                {
                    _profilerOverlay.enabled = showProfiler;
                }

                MFPProfiler.BeginFrame();

                // 视口与界面隐藏态绝对零开销直通 (Zero-Cost Shortcut when UI is hidden or bypassed)
                if (!bypassed && _isUIVisible)
                {
                    MFPProfiler.BeginSample(ProfilerSection.TotalMFP);
                    try
                    {
#if KSP_RUNTIME
                        ModularFlightPanel.Core.StockNavBallHook.TickDynamicHooks();
#endif
#if KSP_RUNTIME
                        // 预热/刷新遥测与探针中枢，彻底脱耦组件渲染，避免组件 update 时把探针耗时算在首个访问组件头上
                        TelemetryHub.Instance?.EnsureSubsystemsUpdated();
#endif

                        WidgetRenderManager.Instance.MasterUpdate(Time.unscaledTime);
                        WidgetSelectionManager.HandleGlobalShortcuts();
                    }
                    finally
                    {
                        MFPProfiler.EndSample(ProfilerSection.TotalMFP);
                    }
                }

#if KSP_RUNTIME
                // 实时侦测 KSP 主控制台 UI 状态 (双重安全保障：捕获 F2 快捷键与第三方 Mod 的显隐切换)
                // 节流为每 15 帧检查一次，避免每帧执行单例查找与反射开销
                if (Time.frameCount % 15 == 0)
                {
                    try
                    {
                        if (KSP.UI.UIMasterController.Instance != null)
                        {
                            bool isShowing = KSP.UI.UIMasterController.Instance.IsUIShowing;
                            if (_isUIVisible != isShowing)
                            {
                                SetUIVisible(isShowing);
                            }
                        }
                    }
                    catch { }
                }
#endif
                _consecutiveUpdateExceptions = 0; // 成功执行，重置异常计数
            }
            catch (Exception ex)
            {
                _consecutiveUpdateExceptions++;
                MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager Update exception ({_consecutiveUpdateExceptions}/{MaxConsecutiveExceptionsBeforeTrip}): {ex.Message}");
                if (_consecutiveUpdateExceptions >= MaxConsecutiveExceptionsBeforeTrip)
                {
                    MFPSafetyFallback.TriggerFaultFallback("连续多帧渲染未捕获异常 (Consecutive Update Exceptions)", ex);
                }
            }
        }

        private void LateUpdate()
        {
            if (MFPSafetyFallback.IsFaulted) return;

            try
            {
                if (!MFPProfiler.IsMasterBypassed && _isUIVisible)
                {
                    WidgetRenderManager.Instance.MasterLateUpdate();
#if KSP_RUNTIME
                    DockAnchorTracker.LateUpdateSync();
#endif
                }
                MFPProfiler.EndFrame();
            }
            catch (Exception ex)
            {
                _consecutiveUpdateExceptions++;
                MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager LateUpdate exception ({_consecutiveUpdateExceptions}/{MaxConsecutiveExceptionsBeforeTrip}): {ex.Message}");
                if (_consecutiveUpdateExceptions >= MaxConsecutiveExceptionsBeforeTrip)
                {
                    MFPSafetyFallback.TriggerFaultFallback("连续多帧 LateUpdate 未捕获异常 (Consecutive LateUpdate Exceptions)", ex);
                }
            }
        }

        private void BuildCanvas()
        {
            _canvasManager.BuildCanvas();

            // 挂载专用编辑模式悬浮工具栏 (默认休眠)
            if (_canvasManager.CanvasObject != null)
            {
                _editModeToolbar = _canvasManager.CanvasObject.GetComponent<HUDEditModeToolbar>() ??
                                   _canvasManager.CanvasObject.AddComponent<HUDEditModeToolbar>();
                _editModeToolbar.Initialize(this);

                _profilerOverlay = _canvasManager.CanvasObject.GetComponent<HUDProfilerOverlay>() ??
                                   _canvasManager.CanvasObject.AddComponent<HUDProfilerOverlay>();
            }
        }

        public void BuildHUD()
        {
            if (_modularWidgets != null && _modularWidgets.Count > 0)
            {
                for (int i = 0; i < _modularWidgets.Count; i++)
                {
                    var w = _modularWidgets[i];
                    if (w != null)
                    {
                        try { w.Teardown(); } catch { }
                    }
                }
            }

            if (_hudRoot != null)
            {
                _hudRoot.SetActive(false);
                if (Application.isPlaying) Destroy(_hudRoot);
                else DestroyImmediate(_hudRoot);
            }
            WidgetRenderManager.Instance.ClearAll();
            _modularWidgets.Clear();
            WidgetSelectionManager.ClearSelection();

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;

            // 根锚点：装配期间保持 SetActive(false)，彻底防止子节点逐个挂载/调整层级时的逐次 UGUI 重绘
            _hudRoot = new GameObject("HUD_Anchor_Root", typeof(RectTransform));
            _hudRoot.SetActive(false);
            _hudRoot.transform.SetParent(_canvasManager.CanvasObject.transform, false);

            RectTransform rootRt = _hudRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0f);
            rootRt.anchorMax = new Vector2(0.5f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = new Vector2(0f, 215f * CustomScale);

            // 实例化全屏蓝图辅助网格与对称轴 (位于底层)
            GameObject gridObj = new GameObject("CanvasBlueprintGrid", typeof(RectTransform));
            gridObj.transform.SetParent(_hudRoot.transform, false);
            gridObj.transform.SetAsFirstSibling();
            var canvasGrid = gridObj.AddComponent<WidgetCanvasGrid>();
            canvasGrid.Initialize(rootRt, _canvasManager.Canvas);

            // 实例化全屏框选捕获器 (位于底层，空白拖拽框选)
            GameObject marqueeObj = new GameObject("MarqueeSelectionCatcher", typeof(RectTransform));
            marqueeObj.transform.SetParent(_hudRoot.transform, false);
            var marquee = marqueeObj.AddComponent<MarqueeSelectionHandler>();
            marquee.Initialize(rootRt);

            // 动态加载所有模块化小组件 (按 DrawOrder 升序排列，统一由 WidgetRegistry 泛型工厂驱动)
            var widgetConfigs = WidgetLayoutManager.Instance.CurrentLayout.Widgets
                .OrderBy(c => c.DrawOrder)
                .ToList();
            foreach (var cfg in widgetConfigs)
            {
                if (!cfg.IsEnabled) continue;

                // 工具栏折叠坞与常用快捷坞检查：仅在模式 2 (折叠收纳坞) 下挂载进主画布
                if ((cfg.WidgetType == "toolbar" || cfg.WidgetId == "core.toolbar" || cfg.WidgetId.StartsWith("toolbar.")) &&
                    ThemeManager.Instance.ToolbarStyleMode != 2)
                {
                    continue;
                }
                if ((cfg.WidgetType == "dock_favorites" || cfg.WidgetId == "core.dock_favorites" || cfg.WidgetId.StartsWith("dock_favorites") || cfg.WidgetId == "toolbar.favorites") &&
                    (ThemeManager.Instance.ToolbarStyleMode != 2 || !ThemeManager.Instance.DockEnableFavoritePanel))
                {
                    continue;
                }

                try
                {
                    BaseFlightWidget widget = WidgetRegistry.Spawn(cfg, theme, _hudRoot.transform, _canvasManager.Canvas, CustomScale);
                    if (widget != null)
                    {
                        _modularWidgets.Add(widget);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModularFlightPanel] Failed to spawn widget '{cfg.WidgetId}': {ex}");
                }
            }

            // 模式 2 (折叠收纳坞) 保障：若当前布局未含 core.toolbar 或被意外关闭，自动确保 Dock 正常实例化
            if (ThemeManager.Instance.ToolbarStyleMode == 2)
            {
                bool hasDock = false;
                for (int i = 0; i < _modularWidgets.Count; i++)
                {
                    if (_modularWidgets[i] is ModernToolbarWidget)
                    {
                        hasDock = true;
                        break;
                    }
                }
                if (!hasDock)
                {
                    var dockCfg = WidgetLayoutManager.Instance.GetConfig("core.toolbar") ??
                        new WidgetConfig("core.toolbar", I18n.GetWidgetName("core.toolbar", "AVIONICS 现代折叠工具栏"), 890f, 0f, 1.0f)
                        {
                            WidgetType = "toolbar",
                            IsEnabled = true
                        };
                    BaseFlightWidget dockWidget = WidgetRegistry.Spawn(dockCfg, theme, _hudRoot.transform, _canvasManager.Canvas, CustomScale);
                    if (dockWidget != null) _modularWidgets.Add(dockWidget);
                }

                // 独立常用快捷坞保障：若已启用常用面板且未在当前布局中，自动确保 FavoriteToolbarWidget 实例化
                if (ThemeManager.Instance.DockEnableFavoritePanel)
                {
                    bool hasFavDock = false;
                    for (int i = 0; i < _modularWidgets.Count; i++)
                    {
                        if (_modularWidgets[i] is FavoriteToolbarWidget)
                        {
                            hasFavDock = true;
                            break;
                        }
                    }
                    if (!hasFavDock)
                    {
                        var favCfg = WidgetLayoutManager.Instance.GetConfig("core.dock_favorites") ??
                            new WidgetConfig("core.dock_favorites", I18n.GetWidgetName("core.dock_favorites", "AVIONICS 常用快捷工具栏"), ThemeManager.Instance.DockFavoritePosX, ThemeManager.Instance.DockFavoritePosY, 1.0f)
                            {
                                WidgetType = "dock_favorites",
                                IsEnabled = true
                            };
                        BaseFlightWidget favWidget = WidgetRegistry.Spawn(favCfg, theme, _hudRoot.transform, _canvasManager.Canvas, CustomScale);
                        if (favWidget != null) _modularWidgets.Add(favWidget);
                    }
                }
            }

            foreach (var w in _modularWidgets)
            {
                WidgetRenderManager.Instance.RegisterWidget(w, w.RefreshTier);
                w.IsManagedByRenderManager = true;
            }

            Debug.Log($"[ModularFlightPanel] Assembled {_modularWidgets.Count} modular flight widgets into WidgetRenderManager.");

            // 实例化 Figma 级智能对齐参考线中枢 (位于顶层，挂载进 _hudRoot 保证坐标系统一)
            GameObject guidesObj = new GameObject("SmartGuides", typeof(RectTransform));
            guidesObj.transform.SetParent(_hudRoot.transform, false);
            guidesObj.transform.SetAsLastSibling();
            var smartGuides = guidesObj.AddComponent<WidgetSmartGuides>();
            smartGuides.Initialize(rootRt, _canvasManager.Canvas);

            // 实例化 8 点包围盒变换手柄与旋转操纵器 (位于最顶层，挂载进 _hudRoot 保证坐标系统一)
            GameObject gizmoObj = new GameObject("TransformGizmo", typeof(RectTransform));
            gizmoObj.transform.SetParent(_hudRoot.transform, false);
            gizmoObj.transform.SetAsLastSibling();
            var gizmo = gizmoObj.AddComponent<WidgetTransformGizmo>();
            gizmo.Initialize(rootRt, _canvasManager.Canvas);

            // 全量统一标准化并同步 UGUI Hierarchy 图层顺序
            WidgetLayerManager.NormalizeAndSyncLayers(recordHistory: false);

            // 全量装配完成，一次性唤醒根节点，将数十次分散的 Canvas 脏标记合并为单次聚合光栅化
            if (_isUIVisible)
            {
                _hudRoot.SetActive(true);
            }
            _canvasManager.SetRaycasterEnabled(WidgetDragHandler.IsEditModeActive && _isUIVisible);
        }

        public T SpawnWidget<T>(WidgetConfig cfg, ThemeConfig theme) where T : BaseFlightWidget
        {
            var w = WidgetRegistry.Spawn<T>(cfg, theme, _hudRoot.transform, _canvasManager.Canvas, CustomScale);
            if (w != null) _modularWidgets.Add(w);
            return w;
        }

        /// <summary>
        /// 权威无缝重构单个小组件以应用全新原生点对点分辨率 (Native Point-to-Point Sharpness)
        /// 彻底解决由于 localScale 放大导致的 2D UGUI 字体与矢量边框模糊、以及姿态球分辨率未同步的问题。
        /// </summary>
        public BaseFlightWidget RespawnWidget(BaseFlightWidget oldWidget)
        {
            if (oldWidget == null || oldWidget.Config == null || _hudRoot == null) return null;

            int index = _modularWidgets.IndexOf(oldWidget);
            bool wasSelected = WidgetSelectionManager.IsSelected(oldWidget);

            WidgetSelectionManager.Deselect(oldWidget);
            WidgetRenderManager.Instance?.UnregisterWidget(oldWidget);
            _modularWidgets.Remove(oldWidget);

            try { oldWidget.Teardown(); } catch { }

            WidgetConfig cfg = oldWidget.Config;
            if (Application.isPlaying) Destroy(oldWidget.gameObject);
            else DestroyImmediate(oldWidget.gameObject);

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            BaseFlightWidget newWidget = WidgetRegistry.Spawn(cfg, theme, _hudRoot.transform, _canvasManager.Canvas, CustomScale);

            if (newWidget != null)
            {
                if (index >= 0 && index <= _modularWidgets.Count)
                {
                    _modularWidgets.Insert(index, newWidget);
                }
                else
                {
                    _modularWidgets.Add(newWidget);
                }

                WidgetRenderManager.Instance?.RegisterWidget(newWidget, newWidget.RefreshTier);
                newWidget.IsManagedByRenderManager = true;

                if (wasSelected)
                {
                    WidgetSelectionManager.Select(newWidget, addToSelection: true);
                }

                WidgetLayerManager.NormalizeAndSyncLayers(recordHistory: false);
            }

            return newWidget;
        }

        /// <summary>
        /// 批量更新组件原生清晰度
        /// </summary>
        public void RespawnWidgets(IEnumerable<BaseFlightWidget> widgets)
        {
            if (widgets == null) return;
            var list = new List<BaseFlightWidget>(widgets);
            if (list.Count == 0) return;

            var selectedIds = new HashSet<string>();
            foreach (var w in WidgetSelectionManager.SelectedWidgets)
            {
                if (w != null && !string.IsNullOrEmpty(w.WidgetId))
                {
                    selectedIds.Add(w.WidgetId);
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                RespawnWidget(list[i]);
            }

            // 恢复选中状态
            foreach (var id in selectedIds)
            {
                for (int i = 0; i < _modularWidgets.Count; i++)
                {
                    if (_modularWidgets[i] != null && _modularWidgets[i].WidgetId == id)
                    {
                        if (!WidgetSelectionManager.IsSelected(_modularWidgets[i]))
                        {
                            WidgetSelectionManager.Select(_modularWidgets[i], addToSelection: true);
                        }
                        break;
                    }
                }
            }

            // 刷新手柄位置与包围盒
            var gizmo = _hudRoot.GetComponentInChildren<WidgetTransformGizmo>();
            if (gizmo != null)
            {
                gizmo.UpdateGizmoPosition();
            }
        }

        public void AddNewCustomWidget(string title, string template)
        {
            Vector2 pos = new Vector2(UnityEngine.Random.Range(-200f, 200f), UnityEngine.Random.Range(50f, 250f));
            WidgetLayoutManager.Instance.AddCustomWidget(title, template, pos);
            BuildHUD();
        }

        public void AddNewTapeWidget(string title, string token, bool isLeft, float step)
        {
            Vector2 pos = isLeft ? new Vector2(-230f, 0f) : new Vector2(230f, 0f);
            WidgetLayoutManager.Instance.AddTapeWidget(title, token, isLeft, step, pos);
            BuildHUD();
        }

        public void AddNewEcamDialWidget(string title, string token, double min, double max, double caution, double warning, bool isSoftLimit, string unit)
        {
            Vector2 pos = new Vector2(UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(20f, 150f));
            WidgetLayoutManager.Instance.AddEcamDialWidget(title, token, min, max, caution, warning, isSoftLimit, unit, pos);
            BuildHUD();
        }

        public void RebuildHUD()
        {
            try
            {
                if (WidgetLayoutManager.Instance != null && WidgetLayoutManager.Instance.CurrentLayout != null)
                {
                    if (TryInPlaceUpdateLayout(WidgetLayoutManager.Instance.CurrentLayout))
                    {
                        return;
                    }
                }
                BuildHUD();
            }
            catch (Exception ex)
            {
                MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager RebuildHUD fatal error: {ex}");
                MFPSafetyFallback.TriggerFaultFallback("HUD 重新装配致命故障 (HUD Rebuild Fatal Error)", ex);
            }
        }

        /// <summary>
        /// 智能拓扑比对就地更新 (In-Place Layout Reconcile)
        /// 当切换载具或重载布局时，若小组件拓扑集合 (WidgetId & WidgetType) 与当前完全一致，
        /// 仅就地同步 RectTransform 坐标、旋角、图层与数据配置，彻底杜绝单帧内销毁与反射重建 30+ 个 GameObject 的 78ms 性能尖峰！
        /// </summary>
        public bool TryInPlaceUpdateLayout(WidgetLayoutData layout)
        {
            if (layout == null || layout.Widgets == null || _hudRoot == null || _modularWidgets == null) return false;

            var activeConfigs = layout.Widgets.Where(c => c != null && c.IsEnabled).ToList();
            if (activeConfigs.Count != _modularWidgets.Count) return false;

            var configMap = new Dictionary<string, WidgetConfig>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < activeConfigs.Count; i++)
            {
                var c = activeConfigs[i];
                if (string.IsNullOrEmpty(c.WidgetId)) return false;
                configMap[c.WidgetId] = c;
            }

            for (int i = 0; i < _modularWidgets.Count; i++)
            {
                var w = _modularWidgets[i];
                if (w == null || string.IsNullOrEmpty(w.WidgetId)) return false;
                if (!configMap.TryGetValue(w.WidgetId, out var cfg)) return false;
                if (!string.Equals(w.Config?.WidgetType, cfg.WidgetType, StringComparison.OrdinalIgnoreCase)) return false;
            }

            // 拓扑 100% 吻合：原地毫秒级同步，0 GameObject 分配，0 Canvas 重建风暴
            for (int i = 0; i < _modularWidgets.Count; i++)
            {
                var w = _modularWidgets[i];
                var cfg = configMap[w.WidgetId];
                w.Config = cfg;

                if (w.RectTransform != null)
                {
                    w.RectTransform.anchoredPosition = new Vector2(cfg.PositionX, cfg.PositionY);
                    w.RectTransform.localEulerAngles = new Vector3(0f, 0f, cfg.Rotation);
                }

                if (w.DragHandler != null)
                {
                    w.DragHandler.UpdateSelectionAppearance();
                }
            }

            RectTransform rootRt = _hudRoot.GetComponent<RectTransform>();
            if (rootRt != null)
            {
                rootRt.anchoredPosition = new Vector2(0f, 215f * CustomScale);
            }

            WidgetLayerManager.NormalizeAndSyncLayers(recordHistory: false);
            return true;
        }

        private void OnThemeChanged(ThemeConfig newTheme)
        {
            if (ThemeManager.Instance.ToolbarStyleMode == 1)
            {
                StockToolbarHook.ReskinStockToolbar();
            }
            for (int i = 0; i < _modularWidgets.Count; i++)
            {
                if (_modularWidgets[i] != null)
                {
                    _modularWidgets[i].ApplyTheme(newTheme);
                }
            }
        }

        private bool _isUIVisible = true;
        public bool IsUIVisible => _isUIVisible;

        private void OnHideUI()
        {
            SetUIVisible(false);
        }

        private void OnShowUI()
        {
            SetUIVisible(true);
        }

        private void OnUIScaleChange()
        {
            RebuildHUD();
        }

#if KSP_RUNTIME
        private void OnVesselChange(Vessel v)
        {
            if (v == null) return;
            try
            {
                bool layoutChanged = WidgetLayoutManager.Instance.OnActiveVesselChanged(v.vesselName);
                if (layoutChanged)
                {
                    if (TryInPlaceUpdateLayout(WidgetLayoutManager.Instance.CurrentLayout))
                    {
                        return;
                    }
                    RebuildHUD();
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Error(MFPLogger.CatUI, $"FlightHUDManager OnVesselChange error: {ex}");
                MFPSafetyFallback.TriggerFaultFallback("载具切换重构时发生致命异常 (OnVesselChange Fatal Error)", ex);
            }
        }
#endif

        public void SetVisible(bool visible)
        {
            SetUIVisible(visible);
        }

        public void SetUIVisible(bool visible)
        {
            if (_isUIVisible == visible && _canvasManager.IsCanvasActive == visible) return;
            _isUIVisible = visible;
            _canvasManager.SetVisible(visible);
            if (_hudRoot != null)
            {
                _hudRoot.SetActive(visible);
            }
        }

        private void HandleGlobalRenderScaleChanged(float newScale)
        {
            _canvasManager.SetDynamicPixelsPerUnit(2.5f * Mathf.Clamp(newScale, 0.5f, 2.5f));
        }

        private void HandleLanguageChanged(string newLang)
        {
            RebuildHUD();
        }

        private void OnDestroy()
        {
            ThemeManager.Instance.OnThemeChanged -= OnThemeChanged;
            I18nManager.OnLanguageChanged -= HandleLanguageChanged;
            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnGlobalRenderScaleChanged -= HandleGlobalRenderScaleChanged;
            }
#if KSP_RUNTIME
            try
            {
                GameEvents.onHideUI.Remove(OnHideUI);
                GameEvents.onShowUI.Remove(OnShowUI);
                GameEvents.onUIScaleChange.Remove(OnUIScaleChange);
                GameEvents.onVesselChange.Remove(OnVesselChange);
            }
            catch { }
#endif

            _canvasManager.Destroy();

            if (_instance == this) _instance = null;
        }
    }

    /// <summary>
    /// [向后兼容别名] 历史命名别名，重定向至 FlightHUDManager
    /// </summary>
    [Obsolete("NavballHUD 已全面重构并更名为 FlightHUDManager，请直接使用 FlightHUDManager")]
    public class NavballHUD : FlightHUDManager
    {
        public new static FlightHUDManager Instance => FlightHUDManager.Instance;
    }
}
