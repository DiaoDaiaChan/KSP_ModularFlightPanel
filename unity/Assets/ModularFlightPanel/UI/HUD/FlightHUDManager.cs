using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Navigation;
using ModularFlightPanel.UI.Widgets.Controls;
using ModularFlightPanel.UI.Widgets.SpaceX;
#if KSP_RUNTIME
using ModularFlightPanel.UI.Settings;
#endif

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电面板主画布生命周期与编排调度总管 (Flight HUD Master Manager)
    /// 核心职能：
    /// 1. 宿主 UGUI 主 Canvas、CanvasScaler、GraphicRaycaster 并自适应屏幕物理分辨率
    /// 2. 读取 WidgetLayoutManager 布局数据，通过 WidgetRegistry 泛型工厂动态装配全量仪表
    /// 3. 单点驱动分频调度中枢 WidgetRenderManager.MasterUpdate 与全局热键调度
    /// 4. 纳管图形化编辑模式工具栏 (Edit Mode Toolbar)、智能对齐参考线、网格标尺与框选交互
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FlightHUDManager : MonoBehaviour
    {
        private static FlightHUDManager _instance;
        public static FlightHUDManager Instance => _instance;

        private GameObject _canvasObj;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private GameObject _hudRoot;

        // 统一小组件集合 (全部继承 BaseFlightWidget)
        private List<BaseFlightWidget> _modularWidgets = new List<BaseFlightWidget>();
        public List<BaseFlightWidget> ModularWidgets => _modularWidgets;

        public float CustomScale
        {
            get => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale;
            set => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = value;
        }

        public static bool IsMouseOverFloatingToolbar { get; private set; } = false;

        private void Awake()
        {
            _instance = this;
        }

        public Canvas Canvas => _canvas;

        public void SetRenderCamera(Camera cam)
        {
            if (_canvas != null && cam != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = cam;
                _canvas.planeDistance = 100f;
            }
        }

        public void Initialize(Camera renderCam = null)
        {
            WidgetLayoutManager.Instance.Initialize();
            ThemeManager.Instance.OnThemeChanged += OnThemeChanged;
            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.OnGlobalRenderScaleChanged += HandleGlobalRenderScaleChanged;
            }

#if KSP_RUNTIME
            // 注册 KSP 原生 UI 显隐事件与 DPI 缩放事件 (支持 F2 一键隐藏 UI)
            try
            {
                GameEvents.onHideUI.Add(OnHideUI);
                GameEvents.onShowUI.Add(OnShowUI);
                GameEvents.onUIScaleChange.Add(OnUIScaleChange);
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

        private void Update()
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
                if (_canvasObj != null && _canvasObj.activeSelf)
                {
                    _canvasObj.SetActive(false);
                    if (_canvas != null) _canvas.enabled = false;
                }
                return;
            }
            else
            {
                if (_canvasObj != null && !_canvasObj.activeSelf && _isUIVisible)
                {
                    _canvasObj.SetActive(true);
                    if (_canvas != null) _canvas.enabled = true;
                }
            }

            MFPProfiler.BeginFrame();

            if (!bypassed)
            {
                WidgetRenderManager.Instance.MasterUpdate(Time.unscaledTime);
                WidgetSelectionManager.HandleGlobalShortcuts();
            }

#if KSP_RUNTIME
            // 实时侦测 KSP 主控制台 UI 状态 (双重安全保障：捕获 F2 快捷键与第三方 Mod 的显隐切换)
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
#endif
        }

        private void LateUpdate()
        {
            if (!MFPProfiler.IsMasterBypassed)
            {
                WidgetRenderManager.Instance.MasterLateUpdate();
            }
            MFPProfiler.EndFrame();
        }

        private void BuildCanvas()
        {
            _canvasObj = new GameObject("ModularFlightPanel_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(_canvasObj);
            }

            _canvas = _canvasObj.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            _scaler = _canvasObj.GetComponent<CanvasScaler>();
            // 固定物理像素模式 (ConstantPixelSize)：1:1 绝对物理像素点对点光栅化，不随屏幕分辨率自动缩放/拉伸模糊
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _scaler.scaleFactor = 1.0f;

            // 航电级超采样动态像素密度 (Avionics High-DPI Dynamic Supersampling):
            // 将默认字体与动态矢量光栅化清晰度与全局渲染倍率挂钩 (2.5x * GlobalRenderScaleMultiplier)
            float renderScale = WidgetRenderManager.Instance != null ? WidgetRenderManager.Instance.GlobalRenderScaleMultiplier : 1.0f;
            _scaler.dynamicPixelsPerUnit = 2.5f * Mathf.Clamp(renderScale, 0.5f, 2.5f);
            _scaler.referencePixelsPerUnit = 100f;

            _raycaster = _canvasObj.GetComponent<GraphicRaycaster>();
        }

        public void BuildHUD()
        {
            if (_hudRoot != null)
            {
                if (Application.isPlaying) Destroy(_hudRoot);
                else DestroyImmediate(_hudRoot);
            }
            WidgetRenderManager.Instance.ClearAll();
            _modularWidgets.Clear();

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;

            // 根锚点
            _hudRoot = new GameObject("HUD_Anchor_Root", typeof(RectTransform));
            _hudRoot.transform.SetParent(_canvasObj.transform, false);

            RectTransform rootRt = _hudRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0f);
            rootRt.anchorMax = new Vector2(0.5f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = new Vector2(0f, 215f * CustomScale);
            if (!_isUIVisible)
            {
                _hudRoot.SetActive(false);
            }

            // 实例化全屏蓝图辅助网格与对称轴 (位于底层)
            GameObject gridObj = new GameObject("CanvasBlueprintGrid", typeof(RectTransform));
            gridObj.transform.SetParent(_hudRoot.transform, false);
            gridObj.transform.SetAsFirstSibling();
            var canvasGrid = gridObj.AddComponent<WidgetCanvasGrid>();
            canvasGrid.Initialize(rootRt, _canvas);

            // 实例化全屏框选捕获器 (位于底层，空白拖拽框选)
            GameObject marqueeObj = new GameObject("MarqueeSelectionCatcher", typeof(RectTransform));
            marqueeObj.transform.SetParent(_hudRoot.transform, false);
            var marquee = marqueeObj.AddComponent<MarqueeSelectionHandler>();
            marquee.Initialize(rootRt);            // 动态加载所有模块化小组件 (统一由 WidgetRegistry 泛型工厂驱动，消除 300+ 行重复分支与胶水代码)
            var widgetConfigs = WidgetLayoutManager.Instance.CurrentLayout.Widgets;
            foreach (var cfg in widgetConfigs)
            {
                if (!cfg.IsEnabled) continue;

                // 工具栏折叠坞检查：仅在模式 2 (折叠收纳坞) 下挂载进主画布
                if ((cfg.WidgetType == "toolbar" || cfg.WidgetId == "core.toolbar" || cfg.WidgetId.StartsWith("toolbar.")) &&
                    ThemeManager.Instance.ToolbarStyleMode != 2)
                {
                    continue;
                }

                BaseFlightWidget widget = WidgetRegistry.Spawn(cfg, theme, _hudRoot.transform, _canvas, CustomScale);
                if (widget != null)
                {
                    _modularWidgets.Add(widget);
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
                        new WidgetConfig("core.toolbar", "AVIONICS 现代折叠工具栏", 890f, 0f, 1.0f)
                        {
                            WidgetType = "toolbar",
                            IsEnabled = true
                        };
                    BaseFlightWidget dockWidget = WidgetRegistry.Spawn(dockCfg, theme, _hudRoot.transform, _canvas, CustomScale);
                    if (dockWidget != null) _modularWidgets.Add(dockWidget);
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
            smartGuides.Initialize(rootRt, _canvas);

            // 实例化 8 点包围盒变换手柄与旋转操纵器 (位于最顶层，挂载进 _hudRoot 保证坐标系统一)
            GameObject gizmoObj = new GameObject("TransformGizmo", typeof(RectTransform));
            gizmoObj.transform.SetParent(_hudRoot.transform, false);
            gizmoObj.transform.SetAsLastSibling();
            var gizmo = gizmoObj.AddComponent<WidgetTransformGizmo>();
            gizmo.Initialize(rootRt, _canvas);
        }

        public T SpawnWidget<T>(WidgetConfig cfg, ThemeConfig theme) where T : BaseFlightWidget
        {
            var w = WidgetRegistry.Spawn<T>(cfg, theme, _hudRoot.transform, _canvas, CustomScale);
            if (w != null) _modularWidgets.Add(w);
            return w;
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
            BuildHUD();
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

        public void SetVisible(bool visible)
        {
            SetUIVisible(visible);
        }

        public void SetUIVisible(bool visible)
        {
            if (_isUIVisible == visible && _canvasObj != null && _canvasObj.activeSelf == visible) return;
            _isUIVisible = visible;

            if (_canvas != null)
            {
                _canvas.enabled = visible;
            }

            if (_canvasObj != null)
            {
                _canvasObj.SetActive(visible);
            }

            if (_raycaster != null)
            {
                _raycaster.enabled = visible;
            }
        }

#if KSP_RUNTIME && !UNITY_EDITOR
        private void OnGUI()
        {
            // 实时绘制高精度性能分析探针 HUD 徽章 (可按 F10 开启/关闭)
            MFPProfiler.DrawGUI();

            if (!WidgetDragHandler.IsEditModeActive || !_isUIVisible || MFPProfiler.IsMasterBypassed) return;

            MFPGuiSkin.EnsureInitialized();

            IsMouseOverFloatingToolbar = false;

            float toolbarW = 980f;
            float toolbarH = 78f;
            float x = (Screen.width - toolbarW) * 0.5f;
            float y = 12f;

            Rect topToolbarRect = new Rect(x, y, toolbarW, toolbarH);
            if (topToolbarRect.Contains(Event.current.mousePosition))
            {
                IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(topToolbarRect, MFPGuiSkin.CardStyle);

            // 第一行：标题 + 撤销/重做 + 全套对齐工具
            GUILayout.BeginHorizontal();
            int selCount = WidgetSelectionManager.Count;
            string selInfo = selCount > 0 ? $"<color=#FFE000><b>已选 {selCount} 项</b></color>" : "<color=#AAAAAA>未选中 (拉框多选)</color>";
            GUILayout.Label($"🛠️ <b>MFP 设计工坊</b> | {selInfo}", GUILayout.Width(170f));

            GUI.enabled = WidgetEditHistory.CanUndo;
            if (GUILayout.Button("↶ 撤销", GUILayout.Width(50f), GUILayout.Height(24f))) WidgetEditHistory.Undo();
            GUI.enabled = WidgetEditHistory.CanRedo;
            if (GUILayout.Button("↷ 重做", GUILayout.Width(50f), GUILayout.Height(24f))) WidgetEditHistory.Redo();
            GUI.enabled = true;

            GUILayout.Space(6f);
            GUI.enabled = selCount >= 2;
            if (GUILayout.Button("⬅ 左对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignLeft();
            if (GUILayout.Button("⏸ 居中X", GUILayout.Width(54f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterX();
            if (GUILayout.Button("➡ 右对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignRight();
            if (GUILayout.Button("⬆ 顶对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignTop();
            if (GUILayout.Button("⏵ 居中Y", GUILayout.Width(54f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterY();
            if (GUILayout.Button("⬇ 底对齐", GUILayout.Width(56f), GUILayout.Height(24f))) WidgetSelectionManager.AlignBottom();
            GUI.enabled = selCount >= 3;
            if (GUILayout.Button("⇹ 水平等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeHorizontally();
            if (GUILayout.Button("⇳ 垂直等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeVertically();
            GUI.enabled = selCount >= 1;
            if (GUILayout.Button("⌖ X=0中轴", GUILayout.Width(64f), GUILayout.Height(24f))) WidgetSelectionManager.CenterToScreenX();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 第二行：磁吸/网格开关 + 图层/删除/微调 + 快捷退出
            GUILayout.BeginHorizontal();
            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            Color accentCol = theme != null ? (Color)theme.AccentSecondary : GUI.contentColor;
            Color textCol = theme != null ? (Color)theme.TextPrimaryColor : GUI.contentColor;
            bool snap = WidgetDragHandler.EnableMagneticSnap;
            GUI.color = snap ? accentCol : textCol;
            if (GUILayout.Button(snap ? "🧲 磁吸: [开]" : "🧲 磁吸: [关]", GUILayout.Width(84f), GUILayout.Height(22f)))
            {
                WidgetDragHandler.EnableMagneticSnap = !WidgetDragHandler.EnableMagneticSnap;
            }

            bool grid = WidgetCanvasGrid.IsGridVisible;
            GUI.color = grid ? accentCol : textCol;
            if (GUILayout.Button(grid ? "▦ 网格: [开]" : "▦ 网格: [关]", GUILayout.Width(84f), GUILayout.Height(22f)))
            {
                WidgetCanvasGrid.ToggleGrid();
            }
            GUI.color = textCol;

            if (selCount > 0)
            {
                // 图层层级
                if (GUILayout.Button("⤒ 置顶", GUILayout.Width(46f), GUILayout.Height(22f))) WidgetSelectionManager.BringToFront();
                if (GUILayout.Button("⤓ 置底", GUILayout.Width(46f), GUILayout.Height(22f))) WidgetSelectionManager.SendToBack();

                // 快速删除/隐藏
                if (GUILayout.Button("🗑 隐藏", GUILayout.Width(46f), GUILayout.Height(22f))) WidgetSelectionManager.DeleteSelected();

                // 缩放
                GUILayout.Space(4f);
                if (GUILayout.Button("－", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(-0.1f);
                if (GUILayout.Button("＋", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(+0.1f);
                if (GUILayout.Button("1.0x", GUILayout.Width(36f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(1.0f);

                // 旋转
                if (GUILayout.Button("↺ 15°", GUILayout.Width(42f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(-15f);
                if (GUILayout.Button("0°", GUILayout.Width(26f), GUILayout.Height(22f))) WidgetSelectionManager.ResetRotation();
                if (GUILayout.Button("↻ 15°", GUILayout.Width(42f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(+15f);

                GUILayout.Space(4f);
                if (GUILayout.Button("取消选择", GUILayout.Width(62f), GUILayout.Height(22f))) WidgetSelectionManager.ClearSelection();
            }
            else
            {
                if (GUILayout.Button("全选 (Ctrl+A)", GUILayout.Width(88f), GUILayout.Height(22f))) WidgetSelectionManager.SelectAll(_modularWidgets);
                GUILayout.Label("<color=#94A3B8><size=10>快捷键: 方向键微调(Shift+10px) | 拖拽手柄缩放/旋转 | Shift锁定轴向 | Ctrl+Z撤销 | G网格 | []图层</size></color>");
            }

            if (GUILayout.Button("✔ 完成退出", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(84f), GUILayout.Height(24f)))
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // 绘制直接吸附在组件旁边的即时悬浮缩放/旋转操作盒 (点击一下即可!)
            DrawOnWidgetFloatingToolbar(selCount);

            MFPInputLock.SetWindowHoverLock(IsMouseOverFloatingToolbar);
        }

        private void DrawOnWidgetFloatingToolbar(int selCount)
        {
            if (selCount <= 0 || _canvas == null) return;

            BaseFlightWidget primary = null;
            foreach (var w in WidgetSelectionManager.SelectedWidgets)
            {
                if (w != null && w.RectTransform != null)
                {
                    primary = w;
                    break;
                }
            }

            if (primary == null || primary.RectTransform == null) return;

            Vector3[] corners = new Vector3[4];
            primary.RectTransform.GetWorldCorners(corners);

            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 p1 = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            Vector2 p3 = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float minY_screen = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            float maxY_screen = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));

            // 屏幕坐标 (左下原点) 转换为 IMGUI 坐标 (左上原点)
            float guiMinY = Screen.height - maxY_screen;
            float guiMaxY = Screen.height - minY_screen;

            float badgeW = 325f;
            float badgeH = 82f;

            // 优先置于组件右侧，留出 10px 空隙
            float bx = maxX + 10f;
            float by = guiMinY;

            // 若右侧超出屏幕边缘，则自适应翻转至组件左侧
            if (bx + badgeW > Screen.width - 10f)
            {
                bx = minX - badgeW - 10f;
            }
            // 若左右两侧都超出，则置于组件正上方
            if (bx < 10f)
            {
                bx = Mathf.Clamp(minX, 10f, Screen.width - badgeW - 10f);
                by = guiMinY - badgeH - 10f;
            }

            // 屏幕安全边界截断约束
            bx = Mathf.Clamp(bx, 10f, Screen.width - badgeW - 10f);
            by = Mathf.Clamp(by, 10f, Screen.height - badgeH - 10f);

            Rect badgeRect = new Rect(bx, by, badgeW, badgeH);

            if (badgeRect.Contains(Event.current.mousePosition))
            {
                IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(badgeRect, MFPGuiSkin.CardStyle);

            // 1. 标题行 (显示当前组件名与实时缩放比、旋转角)
            GUILayout.BeginHorizontal();
            float curScale = primary.Config?.Scale ?? 1.0f;
            float curRot = primary.Config?.Rotation ?? 0f;
            string titlePrefix = selCount > 1 ? $"<color=#FFE000><b>[已选 {selCount} 项]</b></color> " : "";
            GUILayout.Label($"{titlePrefix}<b>{primary.DisplayName}</b>", GUILayout.ExpandWidth(true));
            GUILayout.Label($"<color=#00E5FF><b>{curScale:F2}x</b></color> | <color=#FFE000><b>{curRot:F0}°</b></color>", GUILayout.Width(95f));
            GUILayout.EndHorizontal();

            // 2. 缩放控制行 (点击一下即可!)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#00E5FF><b>缩放:</b></color>", GUILayout.Width(35f));
            if (GUILayout.Button("－", GUILayout.Width(25f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchScale(-0.1f);
                else { primary.UpdateTransform(scale: Mathf.Clamp(curScale - 0.1f, 0.2f, 4.0f)); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("＋", GUILayout.Width(25f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchScale(+0.1f);
                else { primary.UpdateTransform(scale: Mathf.Clamp(curScale + 0.1f, 0.2f, 4.0f)); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("0.8x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetScale(0.8f);
                else { primary.UpdateTransform(scale: 0.8f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("1.0x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetScale(1.0f);
                else { primary.UpdateTransform(scale: 1.0f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("1.2x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetScale(1.2f);
                else { primary.UpdateTransform(scale: 1.2f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("1.5x", GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetScale(1.5f);
                else { primary.UpdateTransform(scale: 1.5f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            GUILayout.EndHorizontal();

            // 3. 旋转控制行 (点击一下即可!)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#FFE000><b>旋转:</b></color>", GUILayout.Width(35f));
            if (GUILayout.Button("↺ 15°", GUILayout.Width(46f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchRotate(-15f);
                else { primary.UpdateTransform(rotation: (curRot - 15f + 360f) % 360f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("0°", GUILayout.Width(30f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.ResetRotation();
                else { primary.UpdateTransform(rotation: 0f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("↻ 15°", GUILayout.Width(46f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchRotate(+15f);
                else { primary.UpdateTransform(rotation: (curRot + 15f) % 360f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("90°", GUILayout.Width(32f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetRotation(90f);
                else { primary.UpdateTransform(rotation: 90f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("180°", GUILayout.Width(38f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.BatchSetRotation(180f);
                else { primary.UpdateTransform(rotation: 180f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            if (GUILayout.Button("⤢ 居中X", GUILayout.Width(52f), GUILayout.Height(20f)))
            {
                if (selCount > 1) WidgetSelectionManager.CenterToScreenX();
                else { primary.UpdateTransform(x: 0f); WidgetLayoutManager.Instance.SaveLayout(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
#endif

        private void HandleGlobalRenderScaleChanged(float newScale)
        {
            if (_scaler != null)
            {
                _scaler.dynamicPixelsPerUnit = 2.5f * Mathf.Clamp(newScale, 0.5f, 2.5f);
            }
        }

        private void OnDestroy()
        {
            ThemeManager.Instance.OnThemeChanged -= OnThemeChanged;
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
            }
            catch { }
#endif

            if (_canvasObj != null)
            {
                if (Application.isPlaying) Destroy(_canvasObj);
                else DestroyImmediate(_canvasObj);
            }

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
