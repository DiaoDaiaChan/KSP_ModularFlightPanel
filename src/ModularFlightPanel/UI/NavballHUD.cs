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

        // 统一小组件集合
        private List<BaseFlightWidget> _modularWidgets = new List<BaseFlightWidget>();

        // 核心姿态球与伴生仪表
        private NavballSphereWidget _sphereWidget;
        private ArcMeterWidget _throttleArcWidget;
        private ArcMeterWidget _vsiArcWidget;
        private ArcMeterWidget _propellantArcWidget;
        private BottomControlsWidget _bottomControlsWidget;

        public float CustomScale
        {
            get => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale;
            set => WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = value;
        }

        private void Awake()
        {
            _instance = this;
        }

        public void Initialize()
        {
            WidgetLayoutManager.Instance.Initialize();
            ThemeManager.Instance.OnThemeChanged += OnThemeChanged;
            BuildCanvas();
            BuildHUD();
        }

        private void BuildCanvas()
        {
            _canvasObj = new GameObject("ModularFlightPanel_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(_canvasObj);

            _canvas = _canvasObj.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            _scaler = _canvasObj.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = _canvasObj.GetComponent<GraphicRaycaster>();
        }

        public void BuildHUD()
        {
            if (_hudRoot != null) Destroy(_hudRoot);
            _modularWidgets.Clear();

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            LayoutConfig fallbackLayout = new LayoutConfig();

            // 根锚点
            _hudRoot = new GameObject("HUD_Anchor_Root", typeof(RectTransform));
            _hudRoot.transform.SetParent(_canvasObj.transform, false);

            RectTransform rootRt = _hudRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0f);
            rootRt.anchorMax = new Vector2(0.5f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = new Vector2(0f, 160f * CustomScale);

            // 1. 核心姿态球 (Pluggable Navball Sphere)
            WidgetConfig navballCfg = WidgetLayoutManager.Instance.GetConfig("core.navball") 
                ?? new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f);
            if (navballCfg.IsEnabled)
            {
                GameObject sphereObj = new GameObject("Widget_NavballSphere");
                sphereObj.transform.SetParent(_hudRoot.transform, false);
                _sphereWidget = sphereObj.AddComponent<NavballSphereWidget>();
                _sphereWidget.BaseInitialize(_hudRoot.transform, _canvas, navballCfg, theme, CustomScale);
                _modularWidgets.Add(_sphereWidget);
            }
            else
            {
                _sphereWidget = null;
            }

            // 2. 核心弧形表
            GameObject throtObj = new GameObject("Widget_ThrottleArc");
            throtObj.transform.SetParent(_hudRoot.transform, false);
            _throttleArcWidget = throtObj.AddComponent<ArcMeterWidget>();
            _throttleArcWidget.Initialize(_hudRoot.transform, ArcMeterType.Throttle, fallbackLayout, theme, CustomScale);

            GameObject vsiObj = new GameObject("Widget_VSIArc");
            vsiObj.transform.SetParent(_hudRoot.transform, false);
            _vsiArcWidget = vsiObj.AddComponent<ArcMeterWidget>();
            _vsiArcWidget.Initialize(_hudRoot.transform, ArcMeterType.VerticalSpeed, fallbackLayout, theme, CustomScale);

            GameObject propObj = new GameObject("Widget_PropellantArc");
            propObj.transform.SetParent(_hudRoot.transform, false);
            _propellantArcWidget = propObj.AddComponent<ArcMeterWidget>();
            _propellantArcWidget.Initialize(_hudRoot.transform, ArcMeterType.StagePropellant, fallbackLayout, theme, CustomScale);

            GameObject ctrlObj = new GameObject("Widget_BottomControls");
            ctrlObj.transform.SetParent(_hudRoot.transform, false);
            _bottomControlsWidget = ctrlObj.AddComponent<BottomControlsWidget>();
            _bottomControlsWidget.Initialize(_hudRoot.transform, fallbackLayout, theme, CustomScale);

            // 3. 动态加载所有模块化小组件 (BaseFlightWidget 体系)
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

                switch (cfg.WidgetId)
                {
                    case "core.speed_box":
                        SpawnSpeedBox(cfg, theme);
                        break;
                    case "core.alt_box":
                        SpawnAltBox(cfg, theme);
                        break;
                    case "core.orbital_info":
                        SpawnOrbitalInfo(cfg, theme);
                        break;
                    case "core.sas_dial":
                        SpawnSASDial(cfg, theme);
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

        private void SpawnSpeedBox(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<DigitalBoxWidget>();
            w.SetupType(DigitalBoxType.Speed);
            w.BaseInitialize(_hudRoot.transform, _canvas, cfg, theme, CustomScale);
            _modularWidgets.Add(w);
        }

        private void SpawnAltBox(WidgetConfig cfg, ThemeConfig theme)
        {
            GameObject go = new GameObject($"Widget_{cfg.WidgetId}");
            var w = go.AddComponent<DigitalBoxWidget>();
            w.SetupType(DigitalBoxType.Altitude);
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
            if (_sphereWidget != null) _sphereWidget.ApplyTheme(newTheme);
            if (_throttleArcWidget != null) _throttleArcWidget.ApplyTheme(newTheme);
            if (_vsiArcWidget != null) _vsiArcWidget.ApplyTheme(newTheme);
            if (_propellantArcWidget != null) _propellantArcWidget.ApplyTheme(newTheme);
            if (_bottomControlsWidget != null) _bottomControlsWidget.ApplyTheme(newTheme);

            for (int i = 0; i < _modularWidgets.Count; i++)
            {
                if (_modularWidgets[i] != null)
                {
                    _modularWidgets[i].ApplyTheme(newTheme);
                }
            }
        }

        public void SetVisible(bool visible)
        {
            if (_canvasObj != null)
            {
                _canvasObj.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            ThemeManager.Instance.OnThemeChanged -= OnThemeChanged;
            if (_canvasObj != null)
            {
                Destroy(_canvasObj);
            }
        }
    }
}
