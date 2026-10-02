using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;
using ModularFlightPanel.UI.Settings;
using ModularFlightPanel.UI.Workbench.Tabs;

namespace ModularFlightPanel.UI.Workbench
{
    /// <summary>
    /// 全新现代航电暗晶工作台 3.0 主视图控制器 (WorkbenchCanvasView)
    /// 核心职责：
    /// 1. 托管独立高刷 UGUI Canvas 与 GPU 程序化暗晶着色器；
    /// 2. 构建现代 IDE 级 Activity Bar、Top App Bar、自适应工作台视口与底部极简悬浮药丸 Dock；
    /// 3. 彻底取代 Unity 遗留 IMGUI，实现 0 GC、0 掉帧与全硬件加速渲染。
    /// </summary>
    public class WorkbenchCanvasView : MonoBehaviour
    {
        private static WorkbenchCanvasView _instance;
        public static WorkbenchCanvasView Instance => _instance;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private CanvasGroup _windowCanvasGroup;
        private RectTransform _windowRt;
        private Image _windowBgImg;

        // 顶栏实时遥测胶囊引用
        private Text _vesselText;
        private Text _frameText;
        private Text _fpsText;
        private float _telemetryTimer = 0f;

        // 左侧 Activity Bar 与标签页系统
        private int _currentTabIndex = 0;
        private IWorkbenchTabView[] _tabViews;
        private GameObject[] _activityButtons;
        private RectTransform _tabContentContainer;

        // 极简悬浮药丸 Dock (画布排版模式)
        private GameObject _floatingDockObj;
        private Text _dockSelInfoText;

        // 窗体拉伸与拖拽几何
        private float _windowWidth = 1060f;
        private float _windowHeight = 670f;
        private Vector2 _windowPos = Vector2.zero;

        // 全屏射线穿透拦截底板
        private GameObject _blockerObj;

        private void Awake()
        {
            _instance = this;
            BuildWorkbenchUI();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public void EnsureBuilt()
        {
            if (_canvas == null)
            {
                BuildWorkbenchUI();
            }
        }

        private void BuildWorkbenchUI()
        {
            WorkbenchStyleEngine.EnsureInitialized();

            // 1. 独立 Overlay 画布
            gameObject.name = "MFP_ModernWorkbench_Root";
            _canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 8500;
            _canvas.pixelPerfect = false;

            _scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 2. 全屏射线拦截器 (阻止点击穿透到 3D 飞船零件)
            _blockerObj = new GameObject("RaycastBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _blockerObj.transform.SetParent(transform, false);
            RectTransform bRt = _blockerObj.GetComponent<RectTransform>();
            bRt.anchorMin = Vector2.zero;
            bRt.anchorMax = Vector2.one;
            bRt.sizeDelta = Vector2.zero;
            Image bImg = _blockerObj.GetComponent<Image>();
            bImg.color = new Color(0f, 0f, 0f, 0.005f); // 极弱透明阻断
            bImg.raycastTarget = true;

            // 3. 工作台主窗体
            GameObject winObj = new GameObject("MainWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            winObj.transform.SetParent(transform, false);

            _windowRt = winObj.GetComponent<RectTransform>();
            _windowRt.sizeDelta = new Vector2(_windowWidth, _windowHeight);
            _windowRt.anchoredPosition = Vector2.zero;

            _windowBgImg = winObj.GetComponent<Image>();
            _windowBgImg.material = WorkbenchStyleEngine.GetWindowGlassMaterial();
            _windowBgImg.color = WorkbenchStyleEngine.ColorWindowBg;

            _windowCanvasGroup = winObj.GetComponent<CanvasGroup>();

            // 窗体内部垂直主布局
            VerticalLayoutGroup winVlg = winObj.AddComponent<VerticalLayoutGroup>();
            winVlg.childForceExpandWidth = true;
            winVlg.childForceExpandHeight = false;
            winVlg.childControlWidth = true;
            winVlg.childControlHeight = true;
            winVlg.spacing = 0f;
            winVlg.padding = new RectOffset(4, 4, 4, 4);

            BuildTopHeader(winObj.transform);
            BuildBodyArea(winObj.transform);
            BuildFooterBar(winObj.transform);

            // 4. 画布排版模式极简悬浮药丸 Dock
            BuildFloatingCanvasDock();

            // 5. 初始化 5 大标签页视图
            _tabViews = new IWorkbenchTabView[]
            {
                new WorkbenchTabStudio(),
                new WorkbenchTabThemes(),
                new WorkbenchTabProfiles(),
                new WorkbenchTabDiagnostics(),
                new WorkbenchTabPreferences()
            };

            SwitchTab(0);
            SetVisible(false, false);
        }

        #region Top Header Bar

        private void BuildTopHeader(Transform parent)
        {
            GameObject header = WorkbenchControls.CreateCard(parent, "TopHeader", new Vector2(0f, 44f));
            var hLe = header.GetComponent<LayoutElement>() ?? header.AddComponent<LayoutElement>();
            hLe.preferredHeight = 44f;
            hLe.minHeight = 44f;
            hLe.flexibleHeight = 0f;

            var hHlg = header.AddComponent<HorizontalLayoutGroup>();
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = true;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.spacing = 10f;
            hHlg.padding = new RectOffset(12, 12, 6, 6);

            // 允许拖拽移动整个窗口
            var dragHandler = header.AddComponent<WorkbenchWindowDragHandler>();
            dragHandler.TargetWindow = _windowRt;

            // 品牌标题与版本徽标
            GameObject titleObj = new GameObject("BrandTitle", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(header.transform, false);
            var tLe = titleObj.AddComponent<LayoutElement>();
            tLe.preferredWidth = 230f;
            tLe.minWidth = 200f;
            tLe.flexibleWidth = 0f;

            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = WorkbenchControls.MainFont;
            titleTxt.fontSize = 13;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            titleTxt.text = "MODULAR FLIGHT PANEL 3.0";
            titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

            WorkbenchControls.CreatePill(header.transform, "AeroPill", "CYBER WORKBENCH", WorkbenchStyleEngine.ColorAccentSecondary, 10);

            // 弹性空白
            GameObject flex = new GameObject("FlexSpace", typeof(RectTransform));
            flex.transform.SetParent(header.transform, false);
            var le = flex.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            // 顶栏实时遥测状态胶囊
            _vesselText = CreateHeaderChip(header.transform, "🚀 载具: ---");
            _frameText = CreateHeaderChip(header.transform, "🌐 参考系: ---");
            _fpsText = CreateHeaderChip(header.transform, "⚡ 60 FPS | 0.1ms");

            // 画布自由排版模式快捷入口
            WorkbenchControls.CreateButton(header.transform, "CanvasModeBtn", "📐 画布自由排版", new Vector2(130f, 28f), () =>
            {
                SettingsGUI.Instance?.EnterCanvasLayoutMode();
            }, true, 11);

            // 顶栏关闭按钮
            WorkbenchControls.CreateButton(header.transform, "CloseBtn", "✕", new Vector2(28f, 28f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, false, 12);
        }

        private Text CreateHeaderChip(Transform parent, string initialText)
        {
            GameObject chip = WorkbenchControls.CreateCard(parent, "Chip", new Vector2(140f, 26f));
            var cLe = chip.GetComponent<LayoutElement>() ?? chip.AddComponent<LayoutElement>();
            cLe.preferredWidth = 140f;
            cLe.minWidth = 120f;
            cLe.preferredHeight = 26f;
            cLe.flexibleWidth = 0f;

            RectTransform cRt = chip.GetComponent<RectTransform>();
            cRt.sizeDelta = new Vector2(140f, 26f);

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(chip.transform, false);
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            txtRt.offsetMin = new Vector2(6f, 0f);
            txtRt.offsetMax = new Vector2(-6f, 0f);

            Text txt = txtObj.GetComponent<Text>();
            txt.font = WorkbenchControls.MainFont;
            txt.fontSize = 11;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = WorkbenchStyleEngine.ColorTextPrimary;
            txt.text = initialText;
            txt.raycastTarget = false;

            return txt;
        }

        #endregion

        #region Body Area (Activity Bar + Content)

        private void BuildBodyArea(Transform parent)
        {
            GameObject body = new GameObject("BodyArea", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            body.transform.SetParent(parent, false);

            RectTransform bodyRt = body.GetComponent<RectTransform>();
            bodyRt.sizeDelta = new Vector2(0f, _windowHeight - 92f);
            var le = body.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.flexibleWidth = 1f;

            HorizontalLayoutGroup bHlg = body.GetComponent<HorizontalLayoutGroup>();
            bHlg.childForceExpandWidth = false;
            bHlg.childForceExpandHeight = true;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.spacing = 6f;
            bHlg.padding = new RectOffset(6, 6, 6, 6);

            // =========================================================================
            // 左侧 Activity Bar 图钉导航栏 (56px 定宽)
            // =========================================================================
            GameObject activityBar = WorkbenchControls.CreateCard(body.transform, "ActivityBar", new Vector2(56f, 0f));
            var abLe = activityBar.GetComponent<LayoutElement>() ?? activityBar.AddComponent<LayoutElement>();
            abLe.preferredWidth = 56f;
            abLe.minWidth = 56f;
            abLe.flexibleWidth = 0f;
            abLe.flexibleHeight = 1f;

            VerticalLayoutGroup aVlg = activityBar.AddComponent<VerticalLayoutGroup>();
            aVlg.childForceExpandWidth = true;
            aVlg.childForceExpandHeight = false;
            aVlg.childControlWidth = true;
            aVlg.childControlHeight = true;
            aVlg.spacing = 8f;
            aVlg.padding = new RectOffset(4, 4, 8, 8);

            string[] tabIcons = new string[] { "🛠️", "🎨", "💾", "🚀", "⚙️" };
            string[] tabNames = new string[] { "工坊", "主题", "档案", "遥测", "偏好" };
            _activityButtons = new GameObject[tabIcons.Length];

            for (int i = 0; i < tabIcons.Length; i++)
            {
                int index = i;
                _activityButtons[i] = WorkbenchControls.CreateButton(activityBar.transform, "TabBtn_" + i, $"{tabIcons[i]}\n<size=9>{tabNames[i]}</size>", new Vector2(46f, 46f), () =>
                {
                    SwitchTab(index);
                }, i == 0, 14);

                var btnLe = _activityButtons[i].GetComponent<LayoutElement>() ?? _activityButtons[i].AddComponent<LayoutElement>();
                btnLe.preferredHeight = 46f;
                btnLe.minHeight = 44f;
                btnLe.flexibleWidth = 1f;
                btnLe.flexibleHeight = 0f;
            }

            // =========================================================================
            // 右侧标签页视口容器
            // =========================================================================
            GameObject contentContainer = new GameObject("ContentContainer", typeof(RectTransform));
            contentContainer.transform.SetParent(body.transform, false);
            _tabContentContainer = contentContainer.GetComponent<RectTransform>();
            var cLe = contentContainer.AddComponent<LayoutElement>();
            cLe.flexibleWidth = 1f;
            cLe.flexibleHeight = 1f;
        }

        #endregion

        #region Footer Bar

        private void BuildFooterBar(Transform parent)
        {
            GameObject footer = WorkbenchControls.CreateCard(parent, "FooterBar", new Vector2(0f, 36f));
            var fLe = footer.GetComponent<LayoutElement>() ?? footer.AddComponent<LayoutElement>();
            fLe.preferredHeight = 36f;
            fLe.minHeight = 36f;
            fLe.flexibleHeight = 0f;

            var fHlg = footer.AddComponent<HorizontalLayoutGroup>();
            fHlg.childForceExpandWidth = false;
            fHlg.childForceExpandHeight = true;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.spacing = 10f;
            fHlg.padding = new RectOffset(12, 12, 4, 4);

            GameObject statusText = new GameObject("StatusText", typeof(RectTransform), typeof(Text));
            statusText.transform.SetParent(footer.transform, false);
            var stLe = statusText.AddComponent<LayoutElement>();
            stLe.flexibleWidth = 1f;

            Text st = statusText.GetComponent<Text>();
            st.font = WorkbenchControls.MainFont;
            st.fontSize = 11;
            st.alignment = TextAnchor.MiddleLeft;
            st.color = WorkbenchStyleEngine.ColorTextMuted;
            st.text = "快捷键: Alt+N / ESC 随时唤出与关闭 | F2 隐藏全屏 UI | 0 GC 高性能硬件加速模式";
            st.horizontalOverflow = HorizontalWrapMode.Overflow;

            GameObject flex = new GameObject("FlexSpace", typeof(RectTransform));
            flex.transform.SetParent(footer.transform, false);
            var le = flex.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            WorkbenchControls.CreateButton(footer.transform, "SaveCloseBtn", "✔ 保存配置并关闭 (Alt+N)", new Vector2(200f, 26f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, true, 11);

            // 右下角拉伸手柄
            GameObject resizeGrip = new GameObject("ResizeGrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            resizeGrip.transform.SetParent(footer.transform, false);
            resizeGrip.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            var rgLe = resizeGrip.AddComponent<LayoutElement>();
            rgLe.preferredWidth = 24f;
            rgLe.minWidth = 24f;
            rgLe.preferredHeight = 24f;
            rgLe.minHeight = 24f;

            Text gt = resizeGrip.GetComponent<Text>();
            gt.font = WorkbenchControls.MainFont;
            gt.fontSize = 14;
            gt.alignment = TextAnchor.MiddleCenter;
            gt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            gt.text = "◢";

            var resizeHandler = resizeGrip.AddComponent<WorkbenchWindowResizeHandler>();
            resizeHandler.TargetWindow = _windowRt;
            resizeHandler.OnResized = (w, h) =>
            {
                _windowWidth = w;
                _windowHeight = h;
            };
        }

        #endregion

        #region Floating Canvas Dock (画布排版模式)

        private void BuildFloatingCanvasDock()
        {
            _floatingDockObj = new GameObject("FloatingCanvasDock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _floatingDockObj.transform.SetParent(transform, false);

            RectTransform dockRt = _floatingDockObj.GetComponent<RectTransform>();
            dockRt.anchorMin = new Vector2(0.5f, 0f);
            dockRt.anchorMax = new Vector2(0.5f, 0f);
            dockRt.pivot = new Vector2(0.5f, 0f);
            dockRt.anchoredPosition = new Vector2(0f, 16f); // 牢牢吸附屏幕底端
            dockRt.sizeDelta = new Vector2(540f, 44f);

            Image dockImg = _floatingDockObj.GetComponent<Image>();
            dockImg.material = WorkbenchStyleEngine.GetPillDockMaterial(false);
            dockImg.color = WorkbenchStyleEngine.ColorPillDarkBg;

            var dHlg = _floatingDockObj.AddComponent<HorizontalLayoutGroup>();
            dHlg.childForceExpandWidth = false;
            dHlg.childForceExpandHeight = true;
            dHlg.spacing = 10f;
            dHlg.padding = new RectOffset(14, 14, 6, 6);

            // 标题
            GameObject dTitle = new GameObject("DockTitle", typeof(RectTransform), typeof(Text));
            dTitle.transform.SetParent(_floatingDockObj.transform, false);
            Text dt = dTitle.GetComponent<Text>();
            dt.font = WorkbenchControls.MainFont;
            dt.fontSize = 12;
            dt.fontStyle = FontStyle.Bold;
            dt.alignment = TextAnchor.MiddleLeft;
            dt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            dt.text = "📐 画布自由排版";

            // 已选计数
            GameObject selInfo = new GameObject("SelInfo", typeof(RectTransform), typeof(Text));
            selInfo.transform.SetParent(_floatingDockObj.transform, false);
            selInfo.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 32f);
            _dockSelInfoText = selInfo.GetComponent<Text>();
            _dockSelInfoText.font = WorkbenchControls.MainFont;
            _dockSelInfoText.fontSize = 11;
            _dockSelInfoText.alignment = TextAnchor.MiddleLeft;
            _dockSelInfoText.color = WorkbenchStyleEngine.ColorWarning;
            _dockSelInfoText.text = "自由拖拽/对齐中";

            // 快捷画板
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "NewArtboardBtn", "🎨 +画板", new Vector2(80f, 28f), () =>
            {
                TabStudio.CreateNewArtboard(false);
            }, false, 11);

            // 返回工坊
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "ReturnBtn", "✔ 返回工坊 (Alt+N)", new Vector2(150f, 28f), () =>
            {
                SettingsGUI.Instance?.ExitCanvasLayoutMode();
            }, true, 11);

            // 关闭
            WorkbenchControls.CreateButton(_floatingDockObj.transform, "CloseBtn", "✕", new Vector2(28f, 28f), () =>
            {
                SettingsGUI.Instance?.ToggleWindow();
            }, false, 12);
        }

        #endregion

        #region Public Control & State Machine

        public void SetVisible(bool isOpen, bool isCanvasLayoutMode)
        {
            EnsureBuilt();

            if (!isOpen)
            {
                _blockerObj?.SetActive(false);
                _windowRt?.gameObject.SetActive(false);
                _floatingDockObj?.SetActive(false);
                return;
            }

            if (isCanvasLayoutMode)
            {
                _blockerObj?.SetActive(false); // 画布排版模式不阻断飞船操作
                _windowRt?.gameObject.SetActive(false);
                _floatingDockObj?.SetActive(true);
            }
            else
            {
                _blockerObj?.SetActive(true);
                _windowRt?.gameObject.SetActive(true);
                _floatingDockObj?.SetActive(false);
                RefreshActiveTab();
            }
        }

        public void SwitchTab(int index)
        {
            if (_tabViews == null || index < 0 || index >= _tabViews.Length) return;

            _currentTabIndex = index;

            // 更新 Activity Bar 按钮状态
            if (_activityButtons != null)
            {
                for (int i = 0; i < _activityButtons.Length; i++)
                {
                    if (_activityButtons[i] != null)
                    {
                        var effect = _activityButtons[i].GetComponent<ModernButtonEffect>();
                        if (effect != null)
                        {
                            effect.IsPrimary = (i == _currentTabIndex);
                            if (effect.ButtonImage != null)
                            {
                                effect.ButtonImage.material = WorkbenchStyleEngine.GetButtonMaterial(i == _currentTabIndex, false);
                                effect.ButtonImage.color = (i == _currentTabIndex) ? WorkbenchStyleEngine.ColorBtnPrimaryBg : WorkbenchStyleEngine.ColorBtnSecondaryBg;
                            }
                        }
                    }
                }
            }

            if (_tabContentContainer != null)
            {
                _tabViews[_currentTabIndex].Build(_tabContentContainer);
            }
        }

        public void RefreshActiveTab()
        {
            if (_tabViews != null && _currentTabIndex >= 0 && _currentTabIndex < _tabViews.Length)
            {
                _tabViews[_currentTabIndex]?.Refresh();
            }
        }

        private void Update()
        {
            if (_canvas == null || !_canvas.gameObject.activeInHierarchy) return;

            // 顶栏遥测胶囊 4Hz 节流平滑刷新
            _telemetryTimer += Time.unscaledDeltaTime;
            if (_telemetryTimer >= 0.25f)
            {
                _telemetryTimer = 0f;

                var ctx = FlightTelemetryContext.Current;
                if (_vesselText != null)
                {
                    _vesselText.text = $"🚀 载具: {ctx?.VesselName ?? "---"}";
                }
                if (_frameText != null)
                {
                    string f = TelemetryTokenEngine.Evaluate("{FRAME}", ctx);
                    _frameText.text = $"🌐 参考系: {(string.IsNullOrEmpty(f) ? "ORBIT" : f)}";
                }
                if (_fpsText != null)
                {
                    _fpsText.text = $"⚡ {MFPProfiler.CurrentFPS:F0} FPS | {MFPProfiler.AvgTotalMs:F2}ms";
                }

                if (_floatingDockObj != null && _floatingDockObj.activeSelf && _dockSelInfoText != null)
                {
                    int selCount = WidgetSelectionManager.Count;
                    _dockSelInfoText.text = selCount > 0 ? $"已选 {selCount} 项" : "自由调整中";
                }
            }

            // 激活标签页生命周期帧驱动
            if (_tabViews != null && _currentTabIndex >= 0 && _currentTabIndex < _tabViews.Length)
            {
                _tabViews[_currentTabIndex]?.OnUpdate();
            }
        }

        #endregion
    }

    #region Window Drag & Resize Handlers

    public class WorkbenchWindowDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform TargetWindow;
        private Vector2 _dragOffset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                _dragOffset = TargetWindow.anchoredPosition - eventData.position;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                TargetWindow.anchoredPosition = eventData.position + _dragOffset;
            }
        }
    }

    public class WorkbenchWindowResizeHandler : MonoBehaviour, IDragHandler
    {
        public RectTransform TargetWindow;
        public Action<float, float> OnResized;

        public void OnDrag(PointerEventData eventData)
        {
            if (TargetWindow != null)
            {
                float newW = Mathf.Clamp(TargetWindow.sizeDelta.x + eventData.delta.x, 860f, Screen.width - 40f);
                float newH = Mathf.Clamp(TargetWindow.sizeDelta.y - eventData.delta.y, 480f, Screen.height - 40f);
                TargetWindow.sizeDelta = new Vector2(newW, newH);
                OnResized?.Invoke(newW, newH);
            }
        }
    }

    #endregion
}
