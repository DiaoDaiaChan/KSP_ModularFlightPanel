using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Widgets;

namespace ModularFlightPanel.UI
{
    public class NavballHUD : MonoBehaviour
    {
        private static NavballHUD _instance;
        public static NavballHUD Instance => _instance;

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

#if !UNITY_EDITOR
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

#if !UNITY_EDITOR
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

#if !UNITY_EDITOR
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
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1.0f;

            // 航电级超采样动态像素密度 (Avionics High-DPI Dynamic Supersampling):
            // 将默认字体与动态矢量光栅化清晰度提升至 2.5x，根治整体模糊、发虚、锯齿感
            _scaler.dynamicPixelsPerUnit = 2.5f;
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

            // 实例化全屏框选捕获器 (位于底层，空白拖拽框选)
            GameObject marqueeObj = new GameObject("MarqueeSelectionCatcher", typeof(RectTransform));
            marqueeObj.transform.SetParent(_hudRoot.transform, false);
            var marquee = marqueeObj.AddComponent<MarqueeSelectionHandler>();
            marquee.Initialize(rootRt);

            // 动态加载所有模块化小组件 (统一 BaseFlightWidget 体系)
            var widgetConfigs = WidgetLayoutManager.Instance.CurrentLayout.Widgets;
            foreach (var cfg in widgetConfigs)
            {
                if (!cfg.IsEnabled) continue;

                if (cfg.WidgetType == "tape" || cfg.WidgetId.StartsWith("tape."))
                {
                    SpawnTapeWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "ecam_dial" || cfg.WidgetId.StartsWith("ecam."))
                {
                    SpawnEcamDialWidget(cfg, theme);
                    continue;
                }

                // 专属原生子系统面板 (优先通过 WidgetType 或 WidgetId 匹配)
                if (cfg.WidgetType == "electrical" || cfg.WidgetId == "custom.electrical" || cfg.WidgetId == "custom.elec")
                {
                    SpawnElectricalWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "rocket2d" || cfg.WidgetType == "rocket" || cfg.WidgetId == "custom.rocket" || cfg.WidgetId == "custom.stage" || cfg.WidgetId == "custom.staging")
                {
                    SpawnRocketWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "life_support" || cfg.WidgetType == "life" || cfg.WidgetId == "custom.life" || cfg.WidgetId == "custom.life_support")
                {
                    SpawnLifeSupportWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "signal" || cfg.WidgetType == "signal_list" || cfg.WidgetId == "custom.signal" || cfg.WidgetId == "custom.signal_list")
                {
                    SpawnSignalWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "heading_arc" || cfg.WidgetId == "core.heading_arc")
                {
                    SpawnHeadingArcWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "time_warp" || cfg.WidgetType == "timewarp" || cfg.WidgetId == "core.time_warp" || cfg.WidgetId == "core.timewarp")
                {
                    SpawnTimeWarpWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "comm_signal" || cfg.WidgetType == "commsignal" || cfg.WidgetId == "core.comm_signal" || cfg.WidgetId == "core.commsignal")
                {
                    SpawnCommSignalWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "nd_navigation" || cfg.WidgetType == "nd" || cfg.WidgetId == "custom.nd_navigation" || cfg.WidgetId == "core.nd_arc")
                {
                    SpawnNDNavigationWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "stage_dv" || cfg.WidgetId == "gauge.stage_dv" || cfg.WidgetId == "custom.stage_dv" || cfg.WidgetId == "core.stage_dv")
                {
                    SpawnStageDeltaVWidget(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "bar_gauge" || cfg.WidgetId.StartsWith("gauge."))
                {
                    SpawnAvionicsBarGauge(cfg, theme);
                    continue;
                }

                if (cfg.WidgetType == "toolbar" || cfg.WidgetId == "core.toolbar" || cfg.WidgetId.StartsWith("toolbar."))
                {
                    SpawnModernToolbar(cfg, theme);
                    continue;
                }

                switch (cfg.WidgetId)
                {
                    case "core.navball":
                        SpawnNavballSphere(cfg, theme);
                        break;
                    case "core.heading_arc":
                        SpawnHeadingArcWidget(cfg, theme);
                        break;
                    case "custom.nd_navigation":
                    case "core.nd_arc":
                        SpawnNDNavigationWidget(cfg, theme);
                        break;
                    case "core.throttle":
                    case "core.vsi":
                    case "core.propellant":
                        SpawnArcMeter(cfg, theme);
                        break;
                    case "core.bottom_controls":
                        SpawnBottomControls(cfg, theme);
                        break;
                    case "core.stage_control":
                        SpawnStageControl(cfg, theme);
                        break;
                    case "core.ecam_status":
                        SpawnEcamStatus(cfg, theme);
                        break;
                    case "core.orbital_info":
                        SpawnOrbitalInfo(cfg, theme);
                        break;
                    case "core.sas_dial":
                        SpawnSASDial(cfg, theme);
                        break;
                    case "core.time_warp":
                    case "core.timewarp":
                        SpawnTimeWarpWidget(cfg, theme);
                        break;
                    case "core.comm_signal":
                    case "core.commsignal":
                        SpawnCommSignalWidget(cfg, theme);
                        break;
                    case "core.toolbar":
                        SpawnModernToolbar(cfg, theme);
                        break;
                    default:
                        // 自定义通配符组件 (CustomTokenTextWidget)
                        if (cfg.WidgetId.StartsWith("custom."))
                        {
                            SpawnCustomTokenWidget(cfg, theme);
                        }
                        break;
                }
            }

            Debug.Log($"[ModularFlightPanel] Assembled {_modularWidgets.Count} modular flight widgets.");
        }

        private void SpawnNavballSphere(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<NavballSphereWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnArcMeter(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<ArcMeterWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnBottomControls(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<BottomControlsWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnStageControl(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<StageControlWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnEcamStatus(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<EcamStatusWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnTapeWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<TapeGaugeWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnEcamDialWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<EcamDialGaugeWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnOrbitalInfo(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<OrbitalInfoWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnSASDial(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<SASDialWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnElectricalWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<ElectricalSystemWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnRocketWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<Rocket2DWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnLifeSupportWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<LifeSupportWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnSignalWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<SignalStatusWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnHeadingArcWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<HeadingArcWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnNDNavigationWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<NDNavigationWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnAvionicsBarGauge(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<AvionicsBarGaugeWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnStageDeltaVWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<StageDeltaVWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnTimeWarpWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<TimeWarpWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnCommSignalWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<CommSignalWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnModernToolbar(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<ModernToolbarWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnCustomTokenWidget(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<CustomTokenTextWidget>();
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
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

        private void OnGUI()
        {
            // 实时绘制高精度性能分析探针 HUD 徽章 (可按 F10 开启/关闭)
            MFPProfiler.DrawGUI();

            if (!WidgetDragHandler.IsEditModeActive || !_isUIVisible || MFPProfiler.IsMasterBypassed) return;

#if !UNITY_EDITOR
            GUI.skin = HighLogic.Skin;
#endif

            IsMouseOverFloatingToolbar = false;

            float toolbarW = 890f;
            float toolbarH = 74f;
            float x = (Screen.width - toolbarW) * 0.5f;
            float y = 12f;

            Rect topToolbarRect = new Rect(x, y, toolbarW, toolbarH);
            if (topToolbarRect.Contains(Event.current.mousePosition))
            {
                IsMouseOverFloatingToolbar = true;
            }

            GUILayout.BeginArea(topToolbarRect, GUI.skin.box);

            GUILayout.BeginHorizontal();
            int selCount = WidgetSelectionManager.Count;
            string selInfo = selCount > 0 ? $"<color=#FFE000><b>已选 {selCount} 项</b></color>" : "<color=#AAAAAA>未选中 (拉框多选)</color>";
            GUILayout.Label($"🛠️ <b>MFP 布局编辑</b> | {selInfo}", GUILayout.Width(170f));

            GUI.enabled = selCount >= 2;
            if (GUILayout.Button("⬅ 左对齐", GUILayout.Width(58f), GUILayout.Height(24f))) WidgetSelectionManager.AlignLeft();
            if (GUILayout.Button("⏸ 水平居中", GUILayout.Width(66f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterX();
            if (GUILayout.Button("➡ 右对齐", GUILayout.Width(58f), GUILayout.Height(24f))) WidgetSelectionManager.AlignRight();
            if (GUILayout.Button("⬆ 顶对齐", GUILayout.Width(58f), GUILayout.Height(24f))) WidgetSelectionManager.AlignTop();
            if (GUILayout.Button("⏵ 垂直居中", GUILayout.Width(66f), GUILayout.Height(24f))) WidgetSelectionManager.AlignCenterY();
            if (GUILayout.Button("⬇ 底对齐", GUILayout.Width(58f), GUILayout.Height(24f))) WidgetSelectionManager.AlignBottom();
            GUI.enabled = selCount >= 3;
            if (GUILayout.Button("⇹ 水平等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeHorizontally();
            if (GUILayout.Button("⇳ 垂直等距", GUILayout.Width(68f), GUILayout.Height(24f))) WidgetSelectionManager.DistributeVertically();
            GUI.enabled = selCount >= 1;
            if (GUILayout.Button("⤢ 居中X", GUILayout.Width(54f), GUILayout.Height(24f))) WidgetSelectionManager.CenterToScreenX();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool snap = WidgetDragHandler.EnableMagneticSnap;
            GUI.color = snap ? Color.cyan : Color.white;
            if (GUILayout.Button(snap ? "🧲 磁吸: [开]" : "🧲 磁吸: [关]", GUILayout.Width(88f), GUILayout.Height(22f)))
            {
                WidgetDragHandler.EnableMagneticSnap = !WidgetDragHandler.EnableMagneticSnap;
            }
            GUI.color = Color.white;

            if (selCount > 0)
            {
                // 缩放直接生效按钮
                GUILayout.Label("<color=#00E5FF><b>缩放:</b></color>", GUILayout.Width(35f));
                if (GUILayout.Button("－", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(-0.1f);
                if (GUILayout.Button("＋", GUILayout.Width(22f), GUILayout.Height(22f))) WidgetSelectionManager.BatchScale(+0.1f);
                if (GUILayout.Button("0.8x", GUILayout.Width(38f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(0.8f);
                if (GUILayout.Button("1.0x", GUILayout.Width(38f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(1.0f);
                if (GUILayout.Button("1.2x", GUILayout.Width(38f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(1.2f);
                if (GUILayout.Button("1.5x", GUILayout.Width(38f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetScale(1.5f);

                // 旋转直接生效按钮
                GUILayout.Space(6f);
                GUILayout.Label("<color=#FFE000><b>旋转:</b></color>", GUILayout.Width(35f));
                if (GUILayout.Button("↺ 15°", GUILayout.Width(44f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(-15f);
                if (GUILayout.Button("0°", GUILayout.Width(26f), GUILayout.Height(22f))) WidgetSelectionManager.ResetRotation();
                if (GUILayout.Button("↻ 15°", GUILayout.Width(44f), GUILayout.Height(22f))) WidgetSelectionManager.BatchRotate(+15f);
                if (GUILayout.Button("90°", GUILayout.Width(32f), GUILayout.Height(22f))) WidgetSelectionManager.BatchSetRotation(90f);

                GUILayout.Space(6f);
                if (GUILayout.Button("取消选择", GUILayout.Width(62f), GUILayout.Height(22f))) WidgetSelectionManager.ClearSelection();
            }
            else
            {
                if (GUILayout.Button("全选全部组件", GUILayout.Width(90f), GUILayout.Height(22f))) WidgetSelectionManager.SelectAll(_modularWidgets);
                GUILayout.Label("<color=#CCCCCC><size=10>快捷键: 空白拉框 | Ctrl+滚轮缩放 | Shift+滚轮旋转 | R复位旋转 | 0复位缩放</size></color>");
            }

            GUI.color = Color.green;
            if (GUILayout.Button("✔ 保存退出", GUILayout.Width(80f), GUILayout.Height(22f)))
            {
                WidgetDragHandler.IsEditModeActive = false;
                WidgetSelectionManager.ClearSelection();
                WidgetLayoutManager.Instance.SaveLayout();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // 绘制直接吸附在组件旁边的即时悬浮缩放/旋转操作盒 (点击一下即可!)
            DrawOnWidgetFloatingToolbar(selCount);
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

            GUI.color = new Color(0.06f, 0.14f, 0.22f, 0.95f);
            GUILayout.BeginArea(badgeRect, GUI.skin.box);
            GUI.color = Color.white;

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

        private void OnDestroy()
        {
            ThemeManager.Instance.OnThemeChanged -= OnThemeChanged;
#if !UNITY_EDITOR
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
}
