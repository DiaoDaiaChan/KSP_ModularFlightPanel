using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Widgets.Controls
{
    /// <summary>
    /// 全局 UI 组件管理与交互中枢小部件 (Modular Avionics UI Manager Widget - core.ui_widget)
    /// 遵照 MFP-SPEC-001..007 标准航电规范，原位挂载在飞行屏幕上的 UGUI 高度集成管理仪表。
    /// 具备：
    /// 1. 0 穿透阻断（原生 UGUI GraphicRaycaster 完美阻断底层 3D 点击）
    /// 2. 原位极简折叠胶囊 (38px 顶栏) 与全功能控制中心 (340px 展开态)
    /// 3. 全局小部件实时列表、快速分类过滤与一键显隐开关
    /// 4. 自由拖拽编辑模式联动、磁吸网格对齐与快速打开 Alt+N 航电工程工作台
    /// 5. 100% 遵从 0 颜色字面量与阶梯分频刷新规范 (Relaxed 10Hz)
    /// </summary>
    public class UIWidget : BaseFlightWidget
    {
        public static UIWidget Instance { get; private set; }

        /// <summary>
        /// 外部委托：请求打开航电设计工作台 (Alt+N)
        /// 解耦 IMGUI 与 UGUI，纯 Unity / 无头测试模式下安全静默
        /// </summary>
        public static Action OnRequestOpenWorkbench;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override bool IsInteractive => true;

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        private GameObject _headerRoot;
        private Text _titleText;
        private Text _statusBadge;
        private Button _dragModeBtn;
        private Image _dragModeBtnImg;
        private Text _dragModeBtnText;
        private Button _workbenchBtn;
        private Text _workbenchBtnText;
        private Button _collapseBtn;
        private Text _collapseBtnText;

        private GameObject _bodyRoot;
        private ScrollRect _scrollRect;
        private RectTransform _contentRt;
        private Image _scrollBg;

        // 分类过滤按钮组
        private readonly List<Button> _categoryBtns = new List<Button>();
        private readonly List<Text> _categoryBtnTexts = new List<Text>();
        private int _selectedCategory = 0; // 0=All, 1=Gauges, 2=Systems, 3=SpaceX, 4=Controls
        private readonly string[] CategoryNames = new string[] { "全部", "仪表", "系统", "SPX", "控制" };

        // 挂载行视图项池
        private class WidgetRowView
        {
            public GameObject Root;
            public Image RowBg;
            public Button ToggleBtn;
            public Image ToggleLed;
            public Text NameText;
            public Text TypeBadgeText;
            public string WidgetId;
            public bool IsEnabled;
        }

        private readonly List<WidgetRowView> _rowViews = new List<WidgetRowView>();
        private bool _isCollapsed = false;
        private int _lastWidgetCount = -1;
        private int _lastActiveCount = -1;
        private ThemeConfig _cachedTheme;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            Instance = this;
            _cachedTheme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 包围盒
            float baseW = 290f * s;
            float baseH = 340f * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            // 2. 底板与边框 (0 颜色字面量)
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, _cachedTheme);

            // 3. 顶部标题栏 (Header, 36px)
            _headerRoot = new GameObject("Header", typeof(RectTransform));
            _headerRoot.transform.SetParent(transform, false);
            RectTransform headerRt = _headerRoot.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(0f, 36f * s);
            headerRt.anchoredPosition = Vector2.zero;

            _titleText = UIFactory.CreateText(_headerRoot.transform, "Title", "❖ UI MANAGER", Mathf.RoundToInt(11f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, _cachedTheme));
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.sizeDelta = new Vector2(110f * s, 26f * s);
            titleRt.anchoredPosition = new Vector2(10f * s, 0f);

            // 折叠/展开按钮 (靠最右)
            _collapseBtn = CreateButton(_headerRoot.transform, "CollapseBtn", "▼", new Vector2(22f * s, 22f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f * s, 0f), ToggleCollapse);
            _collapseBtnText = _collapseBtn.GetComponentInChildren<Text>();

            // 快捷打开工作台按钮 (Alt+N)
            _workbenchBtn = CreateButton(_headerRoot.transform, "WorkbenchBtn", "⚙", new Vector2(22f * s, 22f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-32f * s, 0f), () =>
                {
                    OnRequestOpenWorkbench?.Invoke();
                });
            _workbenchBtnText = _workbenchBtn.GetComponentInChildren<Text>();

            // 快捷自由拖拽编辑按钮
            _dragModeBtn = CreateButton(_headerRoot.transform, "DragBtn", "🎯", new Vector2(22f * s, 22f * s),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-58f * s, 0f), () =>
                {
                    WidgetDragHandler.IsEditModeActive = !WidgetDragHandler.IsEditModeActive;
                    UpdateDragButtonVisual();
                });
            _dragModeBtnImg = _dragModeBtn.GetComponent<Image>();
            _dragModeBtnText = _dragModeBtn.GetComponentInChildren<Text>();

            // 状态徽标 (数量)
            _statusBadge = UIFactory.CreateText(_headerRoot.transform, "StatusBadge", "0/0", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme));
            RectTransform badgeRt = _statusBadge.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 0.5f);
            badgeRt.anchorMax = new Vector2(1f, 0.5f);
            badgeRt.pivot = new Vector2(1f, 0.5f);
            badgeRt.sizeDelta = new Vector2(65f * s, 24f * s);
            badgeRt.anchoredPosition = new Vector2(-84f * s, 0f);

            // 4. 下部主体区域 (Body)
            _bodyRoot = new GameObject("Body", typeof(RectTransform));
            _bodyRoot.transform.SetParent(transform, false);
            RectTransform bodyRt = _bodyRoot.GetComponent<RectTransform>();
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.sizeDelta = new Vector2(0f, -38f * s);
            bodyRt.anchoredPosition = new Vector2(0f, -19f * s);

            // 分类胶囊行
            GameObject catBar = new GameObject("CategoryBar", typeof(RectTransform));
            catBar.transform.SetParent(_bodyRoot.transform, false);
            RectTransform catRt = catBar.GetComponent<RectTransform>();
            catRt.anchorMin = new Vector2(0f, 1f);
            catRt.anchorMax = new Vector2(1f, 1f);
            catRt.pivot = new Vector2(0.5f, 1f);
            catRt.sizeDelta = new Vector2(0f, 24f * s);
            catRt.anchoredPosition = new Vector2(0f, -2f * s);

            float catBtnW = (baseW - 16f * s) / CategoryNames.Length;
            for (int i = 0; i < CategoryNames.Length; i++)
            {
                int catIdx = i;
                float posX = 8f * s + catBtnW * i;
                Button btn = CreateButton(catBar.transform, $"Cat_{i}", CategoryNames[i], new Vector2(catBtnW - 2f * s, 20f * s),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(posX, 0f), () =>
                    {
                        _selectedCategory = catIdx;
                        RefreshWidgetRows();
                        UpdateCategoryButtonVisuals();
                    });
                _categoryBtns.Add(btn);
                _categoryBtnTexts.Add(btn.GetComponentInChildren<Text>());
            }

            // 滚动列表容器
            GameObject scrollObj = new GameObject("ScrollArea", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollObj.transform.SetParent(_bodyRoot.transform, false);
            RectTransform srt = scrollObj.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.sizeDelta = new Vector2(-12f * s, -60f * s);
            srt.anchoredPosition = new Vector2(0f, 2f * s);

            _scrollBg = scrollObj.GetComponent<Image>();
            _scrollBg.color = Color.clear;
            ApplyCard(_scrollBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            _scrollRect = scrollObj.GetComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.scrollSensitivity = 18f;

            // Viewport 使用 RectMask2D (无需 Stencil 缓冲，100% 稳定裁剪且零 GPU 开销)
            GameObject viewObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewObj.transform.SetParent(scrollObj.transform, false);
            RectTransform viewRt = viewObj.GetComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.sizeDelta = new Vector2(-4f * s, -4f * s);
            viewRt.anchoredPosition = Vector2.zero;

            // Content
            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewObj.transform, false);
            _contentRt = contentObj.GetComponent<RectTransform>();
            _contentRt.anchorMin = new Vector2(0f, 1f);
            _contentRt.anchorMax = new Vector2(1f, 1f);
            _contentRt.pivot = new Vector2(0f, 1f);
            _contentRt.sizeDelta = new Vector2(0f, 0f);
            _contentRt.anchoredPosition = Vector2.zero;

            _scrollRect.viewport = viewRt;
            _scrollRect.content = _contentRt;

            // 底部批量控制条
            GameObject footerObj = new GameObject("Footer", typeof(RectTransform));
            footerObj.transform.SetParent(_bodyRoot.transform, false);
            RectTransform footRt = footerObj.GetComponent<RectTransform>();
            footRt.anchorMin = new Vector2(0f, 0f);
            footRt.anchorMax = new Vector2(1f, 0f);
            footRt.pivot = new Vector2(0.5f, 0f);
            footRt.sizeDelta = new Vector2(0f, 26f * s);
            footRt.anchoredPosition = new Vector2(0f, 4f * s);

            float fBtnW = (baseW - 16f * s) / 4f;
            CreateButton(footerObj.transform, "ShowAll", "👁 全显", new Vector2(fBtnW - 2f * s, 22f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s, 0f), () =>
                {
                    SetAllWidgetsActive(true);
                });

            CreateButton(footerObj.transform, "HideAll", "🚫 全隐", new Vector2(fBtnW - 2f * s, 22f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW, 0f), () =>
                {
                    SetAllWidgetsActive(false);
                });

            CreateButton(footerObj.transform, "SnapAll", "🧲 网格", new Vector2(fBtnW - 2f * s, 22f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW * 2f, 0f), () =>
                {
                    SnapAllToGrid(10f);
                });

            CreateButton(footerObj.transform, "ResetAll", "↺ 默认", new Vector2(fBtnW - 2f * s, 22f * s),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f * s + fBtnW * 3f, 0f), () =>
                {
                    WidgetLayoutManager.Instance.ResetToDefaultLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                    RefreshWidgetRows();
                });

            RefreshWidgetRows();
            UpdateCategoryButtonVisuals();
            UpdateDragButtonVisual();
        }

        private Button CreateButton(Transform parent, string name, string text, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Action onClick)
        {
            GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            Image img = btnObj.GetComponent<Image>();
            img.color = Color.clear;
            ApplyCard(img, null, CardStyleRole.InteractiveButton, _cachedTheme);

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            Text txt = UIFactory.CreateText(btnObj.transform, "Label", text, Mathf.RoundToInt(10f * CurrentDpiScale),
                TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            return btn;
        }

        private void ToggleCollapse()
        {
            _isCollapsed = !_isCollapsed;
            float s = CurrentDpiScale;
            float baseW = 290f * s;
            float targetH = _isCollapsed ? 38f * s : 340f * s;
            RectTransform.sizeDelta = new Vector2(baseW, targetH);
            _bodyRoot.SetActive(!_isCollapsed);
            if (_collapseBtnText != null) _collapseBtnText.text = _isCollapsed ? "▲" : "▼";
        }

        private void UpdateDragButtonVisual()
        {
            if (_dragModeBtnImg == null) return;
            bool isEdit = WidgetDragHandler.IsEditModeActive;
            ApplyCard(_dragModeBtnImg, null, isEdit ? CardStyleRole.Emphasized : CardStyleRole.InteractiveButton, _cachedTheme);
            if (_dragModeBtnText != null)
            {
                ApplyText(_dragModeBtnText, isEdit ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
        }

        private void UpdateCategoryButtonVisuals()
        {
            for (int i = 0; i < _categoryBtns.Count; i++)
            {
                bool isSel = (_selectedCategory == i);
                Image img = _categoryBtns[i].GetComponent<Image>();
                ApplyCard(img, null, isSel ? CardStyleRole.Emphasized : CardStyleRole.SubtleSlot, _cachedTheme);
                ApplyText(_categoryBtnTexts[i], isSel ? TextStyleRole.Accent : TextStyleRole.Label, _cachedTheme);
            }
        }

        private void RefreshWidgetRows()
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;

            float s = CurrentDpiScale;
            float rowH = 26f * s;
            float spacing = 2f * s;
            float curY = 0f;

            // 隐藏现有项
            for (int i = 0; i < _rowViews.Count; i++)
            {
                _rowViews[i].Root.SetActive(false);
            }

            int viewIdx = 0;
            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i];
                if (!MatchesCategory(w, _selectedCategory)) continue;

                WidgetRowView row;
                if (viewIdx < _rowViews.Count)
                {
                    row = _rowViews[viewIdx];
                    row.Root.SetActive(true);
                }
                else
                {
                    row = CreateRowView();
                    _rowViews.Add(row);
                }

                BindRow(row, w, curY, rowH);
                curY += rowH + spacing;
                viewIdx++;
            }

            _contentRt.sizeDelta = new Vector2(0f, curY + 6f * s);
        }

        private bool MatchesCategory(WidgetConfig w, int category)
        {
            if (category == 0) return true;
            string id = w.WidgetId.ToLowerInvariant();
            string type = (w.WidgetType ?? "").ToLowerInvariant();

            if (category == 1) // 仪表
            {
                return type == "ecam_dial" || type == "tape" || type == "arc_meter" || id.StartsWith("gauge.") || id.Contains("gauge");
            }
            if (category == 2) // 系统
            {
                return id.Contains("eicas") || id.Contains("electrical") || id.Contains("life") || id.Contains("perf") || id.Contains("signal") || id.Contains("rocket");
            }
            if (category == 3) // SpaceX
            {
                return id.StartsWith("spacex.") || type.StartsWith("spacex_");
            }
            if (category == 4) // 控制
            {
                return id.Contains("toolbar") || id.Contains("control") || id.Contains("sas") || id.Contains("timewarp") || id.Contains("staging") || id.Contains("bottom") || id.Contains("ui_widget");
            }
            return true;
        }

        private WidgetRowView CreateRowView()
        {
            float s = CurrentDpiScale;
            GameObject rowObj = new GameObject("Row", typeof(RectTransform), typeof(Image));
            rowObj.transform.SetParent(_contentRt, false);

            Image rowBg = rowObj.GetComponent<Image>();
            rowBg.color = Color.clear;
            ApplyCard(rowBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            RectTransform rrt = rowObj.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0f, 1f);

            // 开关按钮
            GameObject tglObj = new GameObject("Toggle", typeof(RectTransform), typeof(Button));
            tglObj.transform.SetParent(rowObj.transform, false);
            RectTransform trt = tglObj.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(0f, 0.5f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.sizeDelta = new Vector2(18f * s, 18f * s);
            trt.anchoredPosition = new Vector2(6f * s, 0f);
            Button tglBtn = tglObj.GetComponent<Button>();

            GameObject ledObj = new GameObject("LED", typeof(RectTransform), typeof(Image));
            ledObj.transform.SetParent(tglObj.transform, false);
            RectTransform lrt = ledObj.GetComponent<RectTransform>();
            lrt.sizeDelta = new Vector2(10f * s, 10f * s);
            lrt.anchoredPosition = new Vector2(9f * s, 9f * s);
            Image ledImg = ledObj.GetComponent<Image>();
            ledImg.color = Color.clear;
            ledImg.sprite = Core.NavballMarkerFactory.GetCircleMaskSprite();

            // 类型徽标
            Text typeText = UIFactory.CreateText(rowObj.transform, "Type", "[W]", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, _cachedTheme));
            RectTransform yrt = typeText.GetComponent<RectTransform>();
            yrt.anchorMin = new Vector2(0f, 0.5f);
            yrt.anchorMax = new Vector2(0f, 0.5f);
            yrt.pivot = new Vector2(0f, 0.5f);
            yrt.sizeDelta = new Vector2(45f * s, 20f * s);
            yrt.anchoredPosition = new Vector2(30f * s, 0f);

            // 名称
            Text nameText = UIFactory.CreateText(rowObj.transform, "Name", "Widget", Mathf.RoundToInt(10f * s),
                TextAnchor.MiddleLeft, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, _cachedTheme));
            RectTransform nrt = nameText.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0f, 0.5f);
            nrt.anchorMax = new Vector2(1f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.sizeDelta = new Vector2(-85f * s, 20f * s);
            nrt.anchoredPosition = new Vector2(78f * s, 0f);

            return new WidgetRowView
            {
                Root = rowObj,
                RowBg = rowBg,
                ToggleBtn = tglBtn,
                ToggleLed = ledImg,
                TypeBadgeText = typeText,
                NameText = nameText
            };
        }

        private void BindRow(WidgetRowView row, WidgetConfig w, float topOffset, float height)
        {
            float s = CurrentDpiScale;
            RectTransform rrt = row.Root.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(-6f * s, height);
            rrt.anchoredPosition = new Vector2(3f * s, -topOffset);

            row.WidgetId = w.WidgetId;
            row.IsEnabled = w.IsEnabled;

            SetTextIfChanged(row.NameText, w.DisplayName);

            string badge = w.WidgetType == "tape" ? "PFD" :
                          (w.WidgetType == "ecam_dial" ? "ECAM" :
                          (w.WidgetId.StartsWith("spacex.") ? "SPX" :
                          (w.WidgetId.StartsWith("custom.") ? "CARD" : "CORE")));
            SetTextIfChanged(row.TypeBadgeText, badge);

            // 更新 LED 颜色
            WidgetStyleManager style = WidgetStyleManager.Instance;
            row.ToggleLed.color = w.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                              : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);

            row.ToggleBtn.onClick.RemoveAllListeners();
            row.ToggleBtn.onClick.AddListener(() =>
            {
                w.IsEnabled = !w.IsEnabled;
                row.IsEnabled = w.IsEnabled;
                row.ToggleLed.color = w.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                                  : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            });
        }

        private void SetAllWidgetsActive(bool active)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].IsEnabled = active;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        private void SnapAllToGrid(float grid)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].PositionX = Mathf.Round(widgets[i].PositionX / grid) * grid;
                widgets[i].PositionY = Mathf.Round(widgets[i].PositionY / grid) * grid;
            }
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD();
            RefreshWidgetRows();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _cachedTheme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, _cachedTheme);
            ApplyText(_titleText, TextStyleRole.Cardinal, _cachedTheme);
            ApplyText(_statusBadge, TextStyleRole.PrimaryValue, _cachedTheme);

            if (_scrollBg != null) ApplyCard(_scrollBg, null, CardStyleRole.SubtleSlot, _cachedTheme);

            UpdateDragButtonVisual();
            UpdateCategoryButtonVisuals();

            for (int i = 0; i < _rowViews.Count; i++)
            {
                var r = _rowViews[i];
                if (r.Root != null && r.Root.activeSelf)
                {
                    ApplyCard(r.RowBg, null, CardStyleRole.SubtleSlot, _cachedTheme);
                    ApplyText(r.TypeBadgeText, TextStyleRole.Unit, _cachedTheme);
                    ApplyText(r.NameText, TextStyleRole.Label, _cachedTheme);
                    r.ToggleLed.color = r.IsEnabled ? style.GetTextColor(TextStyleRole.PrimaryValue, _cachedTheme)
                                                    : style.GetTextColor(TextStyleRole.Muted, _cachedTheme);
                }
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            var widgets = WidgetLayoutManager.Instance.CurrentLayout?.Widgets;
            if (widgets == null) return;

            int total = widgets.Count;
            int active = 0;
            for (int i = 0; i < total; i++)
            {
                if (widgets[i].IsEnabled) active++;
            }

            if (total != _lastWidgetCount || active != _lastActiveCount)
            {
                _lastWidgetCount = total;
                _lastActiveCount = active;
                SetTextIfChanged(_statusBadge, $"{active}/{total} ON");
            }
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
