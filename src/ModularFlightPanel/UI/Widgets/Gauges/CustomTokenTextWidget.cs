using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 多通道遥测综合矩阵卡片 (MechJeb Telemetry Matrix Card)
    /// 标准化 MechJeb 风格航电矩阵：
    /// 1. 深度对标 MechJeb 经典监控窗口与 Delta-V 状态表格；
    /// 2. 支持任意增删行数与列数（1~12 列，1~20 行）；
    /// 3. 支持键值监控网格 (Key-Value) 与数据表格 (Table) 双模式切换；
    /// 4. 彻底杜绝文字重叠：标签居左、读数居右、动态弹性分列与微光分隔线；
    /// 5. 接入 IAdaptiveSizeWidget 动态尺寸自适应协议与 IDynamicSlotWidget 槽位编排协议；
    /// 6. 100% 遵照 SPEC-001..008 核心架构规范与零颜色字面量铁律。
    /// </summary>
    [FlightWidget("custom_token", "custom_text", "custom", "telemetry_matrix", "mj_matrix",
        Category = WidgetCategory.Gauges,
        DisplayName = "多通道遥测综合矩阵卡",
        Description = "MechJeb 风格高集成度遥测监控矩阵卡：支持任意自定义增减行与列、自由定义标签与通配符、提供键值监控与分级数据表格双模式，智能越限告警变色。",
        DefaultWidgetId = "custom.telemetry_card",
        DefaultX = 0f,
        DefaultY = 0f)]
    public class CustomTokenTextWidget : BaseFlightWidget, IAdaptiveSizeWidget, IDynamicSlotWidget
    {
        public override Vector2 BaseSize => GetDynamicBaseSize();
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Slow;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(160f, 50f);
        public Vector2 MaxBaseSize => new Vector2(1600f, 1000f);

        private float _currentWidth = 280f;
        private float _currentHeight = 100f;
        private bool _isCustomResized = false;

        // 顶部标题栏
        private GameObject _headerObj;
        private Text _titleText;
        private Text _badgeText;
        private Image _headerDivider;

        // 矩阵内容容器
        private GameObject _gridContainerObj;
        private RectTransform _gridContainerRt;

        // 运行期网格单元 UI 模型
        private class CellRuntimeUI
        {
            public int Row;
            public int Col;
            public string Label = "";
            public string Token = "";

            public GameObject CellObj;
            public RectTransform CellRt;
            public Text LabelText;
            public Text ValText;
            public Image ColDivider;

            public string PendingValue = "---";
            public TextStyleRole PendingRole = TextStyleRole.PrimaryValue;
            public string LastRenderedValue = null;
            public TextStyleRole LastRenderedRole = (TextStyleRole)(-1);
        }

        private class RowRuntimeUI
        {
            public int RowIndex;
            public GameObject RowObj;
            public RectTransform RowRt;
            public Image RowBg;
            public readonly List<CellRuntimeUI> Cells = new List<CellRuntimeUI>();
        }

        private class TableHeaderRuntimeUI
        {
            public GameObject HeaderObj;
            public RectTransform HeaderRt;
            public Image HeaderBg;
            public Image HeaderDivider;
            public readonly List<Text> HeaderTexts = new List<Text>();
        }

        private readonly List<RowRuntimeUI> _runtimeRows = new List<RowRuntimeUI>();
        private TableHeaderRuntimeUI _tableHeaderUI = null;
        private TelemetryMatrixData _activeData = null;
        private string _cachedTemplate = null;

        private string _pendingTitle = "多通道遥测综合矩阵卡";
        private string _pendingBadge = "MJ MATRIX";
        private string _lastRenderedTitle = null;
        private string _lastRenderedBadge = null;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            _headerObj = new GameObject("HeaderRow", typeof(RectTransform));
            _headerObj.transform.SetParent(transform, false);
            RectTransform hRt = _headerObj.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = Vector2.zero;
            hRt.sizeDelta = new Vector2(0f, 26f * s);

            WidgetStyleManager style = WidgetStyleManager.Instance;
            _titleText = UIFactory.CreateText(_headerObj.transform, "Title", DisplayName, Mathf.RoundToInt(10f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tRt = _titleText.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(0.7f, 1f);
            tRt.offsetMin = new Vector2(10f * s, 0f);
            tRt.offsetMax = Vector2.zero;

            _badgeText = UIFactory.CreateText(_headerObj.transform, "Badge", I18n.Tr("WIDGET_MJ_MATRIX_BADGE", "MJ 矩阵"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform bRt = _badgeText.rectTransform;
            bRt.anchorMin = new Vector2(0.65f, 0f);
            bRt.anchorMax = new Vector2(1f, 1f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = new Vector2(-10f * s, 0f);

            GameObject divObj = UIFactory.CreatePanel(_headerObj.transform, "HeaderDivider", new Vector2(0f, 1f * s), Vector2.zero, style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            _headerDivider = divObj.GetComponent<Image>();
            RectTransform divRt = _headerDivider.rectTransform;
            divRt.anchorMin = new Vector2(0f, 0f);
            divRt.anchorMax = new Vector2(1f, 0f);
            divRt.pivot = new Vector2(0.5f, 0f);
            divRt.sizeDelta = new Vector2(-16f * s, 1f * s);
            divRt.anchoredPosition = new Vector2(0f, 1f * s);

            _gridContainerObj = new GameObject("GridContainer", typeof(RectTransform));
            _gridContainerObj.transform.SetParent(transform, false);
            _gridContainerRt = _gridContainerObj.GetComponent<RectTransform>();
            _gridContainerRt.anchorMin = new Vector2(0f, 0f);
            _gridContainerRt.anchorMax = new Vector2(1f, 1f);
            _gridContainerRt.offsetMin = new Vector2(6f * s, 6f * s);
            _gridContainerRt.offsetMax = new Vector2(-6f * s, -28f * s);

            _cachedTemplate = Config?.CustomTemplate;
            _activeData = TelemetryMatrixData.FromTemplate(_cachedTemplate);
            RebuildUI(theme);
        }

        private Vector2 GetDynamicBaseSize()
        {
            var data = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            if (data.Mode == MatrixDisplayMode.Table)
            {
                float w = Mathf.Max(260f, data.Columns * 64f + 20f);
                float h = 28f + 20f + data.Rows * 18f + 10f;
                return new Vector2(w, h);
            }
            else
            {
                float colW = data.Columns == 1 ? 240f : 140f;
                float w = Mathf.Max(200f, data.Columns * colW + 16f);
                float h = 28f + data.Rows * 20f + 10f;
                return new Vector2(w, h);
            }
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            _currentWidth = pixelSize.x / s;
            _currentHeight = pixelSize.y / s;
            _isCustomResized = true;
            UpdateLayoutGeometry();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_titleText != null) _titleText.color = style.GetTextColor(TextStyleRole.Label, theme);
            if (_badgeText != null) _badgeText.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            if (_headerDivider != null) _headerDivider.color = style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme);

            Color rowTintEven = style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme);
            rowTintEven.a = 0.08f;
            Color rowTintOdd = Color.clear;

            if (_tableHeaderUI != null)
            {
                if (_tableHeaderUI.HeaderBg != null) _tableHeaderUI.HeaderBg.color = rowTintEven;
                if (_tableHeaderUI.HeaderDivider != null) _tableHeaderUI.HeaderDivider.color = style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme);
                for (int i = 0; i < _tableHeaderUI.HeaderTexts.Count; i++)
                {
                    if (_tableHeaderUI.HeaderTexts[i] != null)
                    {
                        _tableHeaderUI.HeaderTexts[i].color = style.GetTextColor(TextStyleRole.Label, theme);
                    }
                }
            }

            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                if (row.RowBg != null)
                {
                    row.RowBg.color = (r % 2 == 0) ? rowTintEven : rowTintOdd;
                }
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.LabelText != null) cell.LabelText.color = style.GetTextColor(TextStyleRole.Label, theme);
                    if (cell.ValText != null) cell.ValText.color = style.GetTextColor(cell.PendingRole, theme);
                    if (cell.ColDivider != null) cell.ColDivider.color = style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme);
                }
            }
        }

        private bool _needsUiRebuild = false;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;

            string curTpl = Config?.CustomTemplate;
            if (curTpl != _cachedTemplate)
            {
                _cachedTemplate = curTpl;
                _activeData = TelemetryMatrixData.FromTemplate(_cachedTemplate);
                _needsUiRebuild = true;
            }

            string displayTitle = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : I18n.Tr("WIDGET_NAME_CUSTOM_TOKEN", "多通道遥测综合矩阵卡");
            _pendingTitle = EvalToken(displayTitle, telemetry, displayTitle);

            if (_activeData != null)
            {
                _pendingBadge = (_activeData.Mode == MatrixDisplayMode.Table) 
                    ? $"{_activeData.Columns}-COL TABLE" 
                    : $"{_activeData.Rows}x{_activeData.Columns} MJ";
            }

            if (_activeData == null || telemetry == null || !telemetry.HasVessel)
            {
                SetAllCellsFallback("---", TextStyleRole.PrimaryValue);
                return;
            }

            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (string.IsNullOrEmpty(cell.Token))
                    {
                        cell.PendingValue = "---";
                        cell.PendingRole = TextStyleRole.PrimaryValue;
                        continue;
                    }

                    string evalStr = EvalToken(cell.Token, telemetry, "---");
                    cell.PendingValue = evalStr;

                    // 航电语义越限告警判定 (Q 动压、过载 G、推重比 TWR、升降率 VSI 智能求值)
                    cell.PendingRole = EvaluateSemanticRole(cell.Token, telemetry);
                }
            }
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            if (_needsUiRebuild)
            {
                _needsUiRebuild = false;
                RebuildUI(context.Theme);
            }

            if (_titleText != null && _pendingTitle != _lastRenderedTitle)
            {
                _lastRenderedTitle = _pendingTitle;
                _titleText.SetTextSafe(_pendingTitle);
            }

            if (_badgeText != null && _badgeBadgeChanged())
            {
                _lastRenderedBadge = _pendingBadge;
                _badgeText.SetTextSafe(_pendingBadge);
            }

            var theme = context.Theme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.ValText != null)
                    {
                        if (cell.PendingValue != cell.LastRenderedValue)
                        {
                            cell.LastRenderedValue = cell.PendingValue;
                            cell.ValText.SetTextSafe(cell.PendingValue);
                        }

                        if (cell.PendingRole != cell.LastRenderedRole)
                        {
                            cell.LastRenderedRole = cell.PendingRole;
                            cell.ValText.color = style.GetTextColor(cell.PendingRole, theme);
                        }
                    }
                }
            }
        }

        private bool _badgeBadgeChanged()
        {
            return _pendingBadge != _lastRenderedBadge;
        }

        private TextStyleRole EvaluateSemanticRole(string token, IFlightTelemetry telemetry)
        {
            string u = token.ToUpperInvariant();
            if (u.Contains("Q") && !u.Contains("STATUS") && !u.Contains("EQUAT"))
            {
                double q = telemetry.DynamicPressure;
                if (q > 35.0) return TextStyleRole.Danger;
                if (q > 25.0) return TextStyleRole.Warning;
            }
            else if (u.Contains("GFORCE") || u == "{G}")
            {
                double g = telemetry.GForce;
                if (g > 6.0) return TextStyleRole.Danger;
                if (g > 4.0) return TextStyleRole.Warning;
            }
            else if (u.Contains("TWR"))
            {
                double twr = telemetry.TWR;
                if (twr > 0.05 && twr < 1.0 && telemetry.AltitudeAGL < 1000.0) return TextStyleRole.Warning;
            }
            else if (u.Contains("VSI"))
            {
                double vsi = telemetry.VerticalSpeed;
                if (vsi < -50.0 && telemetry.AltitudeAGL < 3000.0) return TextStyleRole.Danger;
            }
            return TextStyleRole.PrimaryValue;
        }

        private void SetAllCellsFallback(string fallback, TextStyleRole role)
        {
            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    row.Cells[c].PendingValue = fallback;
                    row.Cells[c].PendingRole = role;
                }
            }
        }

        private void RebuildUI(ThemeConfig theme)
        {
            theme = theme ?? WidgetStyleManager.ResolveTheme(null);
            if (!_isCustomResized)
            {
                Vector2 dynSize = GetDynamicBaseSize();
                _currentWidth = dynSize.x;
                _currentHeight = dynSize.y;
                RectTransform.sizeDelta = dynSize * CurrentDpiScale;
            }

            BuildGridUI(theme);
            ApplyTheme(theme);
        }

        private void BuildGridUI(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 清理既有网格节点
            for (int i = _gridContainerRt.childCount - 1; i >= 0; i--)
            {
                Destroy(_gridContainerRt.GetChild(i).gameObject);
            }
            _runtimeRows.Clear();
            _tableHeaderUI = null;

            if (_activeData == null) return;

            bool isTable = (_activeData.Mode == MatrixDisplayMode.Table);

            // 1. 若为表格模式，构建顶部列标题行
            if (isTable)
            {
                _tableHeaderUI = new TableHeaderRuntimeUI();
                GameObject thGo = UIFactory.CreatePanel(_gridContainerRt, "TableHeaderRow", new Vector2(0f, 20f * s), Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme));
                _tableHeaderUI.HeaderObj = thGo;
                _tableHeaderUI.HeaderRt = thGo.GetComponent<RectTransform>();
                _tableHeaderUI.HeaderBg = thGo.GetComponent<Image>();

                _tableHeaderUI.HeaderRt.anchorMin = new Vector2(0f, 1f);
                _tableHeaderUI.HeaderRt.anchorMax = new Vector2(1f, 1f);
                _tableHeaderUI.HeaderRt.pivot = new Vector2(0f, 1f);
                _tableHeaderUI.HeaderRt.anchoredPosition = Vector2.zero;
                _tableHeaderUI.HeaderRt.sizeDelta = new Vector2(0f, 20f * s);

                // 表头下横线
                GameObject hDivObj = UIFactory.CreatePanel(thGo.transform, "HLine", new Vector2(0f, 1f * s), Vector2.zero, style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
                _tableHeaderUI.HeaderDivider = hDivObj.GetComponent<Image>();
                RectTransform hDivRt = _tableHeaderUI.HeaderDivider.rectTransform;
                hDivRt.anchorMin = new Vector2(0f, 0f);
                hDivRt.anchorMax = new Vector2(1f, 0f);
                hDivRt.pivot = new Vector2(0f, 0f);
                hDivRt.sizeDelta = new Vector2(0f, 1f * s);
                hDivRt.anchoredPosition = Vector2.zero;

                for (int c = 0; c < _activeData.Columns; c++)
                {
                    string hText = (c < _activeData.TableHeaders.Count) ? _activeData.TableHeaders[c] : $"Col {c + 1}";
                    Text txt = UIFactory.CreateText(thGo.transform, $"H_{c}", hText, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
                    _tableHeaderUI.HeaderTexts.Add(txt);
                }
            }

            // 2. 构建数据行与单元格
            for (int r = 0; r < _activeData.Rows; r++)
            {
                var rowUi = new RowRuntimeUI { RowIndex = r };
                GameObject rGo = UIFactory.CreatePanel(_gridContainerRt, $"Row_{r}", new Vector2(0f, 20f * s), Vector2.zero, Color.clear);
                rowUi.RowObj = rGo;
                rowUi.RowRt = rGo.GetComponent<RectTransform>();
                rowUi.RowBg = rGo.GetComponent<Image>();

                rowUi.RowRt.anchorMin = new Vector2(0f, 1f);
                rowUi.RowRt.anchorMax = new Vector2(1f, 1f);
                rowUi.RowRt.pivot = new Vector2(0f, 1f);

                for (int c = 0; c < _activeData.Columns; c++)
                {
                    var cellData = (r < _activeData.Grid.Count && c < _activeData.Grid[r].Count) ? _activeData.Grid[r][c] : new MatrixCellData();
                    var cellUi = new CellRuntimeUI
                    {
                        Row = r,
                        Col = c,
                        Label = cellData.Label,
                        Token = cellData.Token
                    };

                    GameObject cGo = new GameObject($"Cell_{r}_{c}", typeof(RectTransform));
                    cGo.transform.SetParent(rGo.transform, false);
                    cellUi.CellObj = cGo;
                    cellUi.CellRt = cGo.GetComponent<RectTransform>();

                    if (isTable)
                    {
                        // 表格数据单元格：单数值居中或靠右
                        cellUi.ValText = UIFactory.CreateText(cGo.transform, "Val", "---", Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                        RectTransform vRt = cellUi.ValText.rectTransform;
                        vRt.anchorMin = Vector2.zero;
                        vRt.anchorMax = Vector2.one;
                        vRt.offsetMin = new Vector2(2f * s, 0f);
                        vRt.offsetMax = new Vector2(-2f * s, 0f);
                    }
                    else
                    {
                        // 键值网格单元格：左侧标签，右侧数值，绝不重叠
                        cellUi.LabelText = UIFactory.CreateText(cGo.transform, "Lbl", cellUi.Label, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                        RectTransform lRt = cellUi.LabelText.rectTransform;
                        lRt.anchorMin = new Vector2(0f, 0f);
                        lRt.anchorMax = new Vector2(0.48f, 1f);
                        lRt.offsetMin = new Vector2(6f * s, 0f);
                        lRt.offsetMax = Vector2.zero;

                        cellUi.ValText = UIFactory.CreateText(cGo.transform, "Val", "---", Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                        RectTransform vRt = cellUi.ValText.rectTransform;
                        vRt.anchorMin = new Vector2(0.48f, 0f);
                        vRt.anchorMax = new Vector2(1f, 1f);
                        vRt.offsetMin = Vector2.zero;
                        vRt.offsetMax = new Vector2(-6f * s, 0f);

                        // 列间微光垂直分割线
                        if (c > 0)
                        {
                            GameObject sep = UIFactory.CreatePanel(cGo.transform, "VSep", new Vector2(1f * s, 14f * s), Vector2.zero, style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
                            cellUi.ColDivider = sep.GetComponent<Image>();
                            RectTransform sepRt = cellUi.ColDivider.rectTransform;
                            sepRt.anchorMin = new Vector2(0f, 0.5f);
                            sepRt.anchorMax = new Vector2(0f, 0.5f);
                            sepRt.pivot = new Vector2(0.5f, 0.5f);
                            sepRt.anchoredPosition = Vector2.zero;
                            sepRt.sizeDelta = new Vector2(1f * s, 14f * s);
                        }
                    }

                    rowUi.Cells.Add(cellUi);
                }

                _runtimeRows.Add(rowUi);
            }

            UpdateLayoutGeometry();
        }

        private void UpdateLayoutGeometry()
        {
            if (_activeData == null) return;
            float s = CurrentDpiScale;
            bool isTable = (_activeData.Mode == MatrixDisplayMode.Table);

            int cols = Mathf.Max(1, _activeData.Columns);
            int rows = Mathf.Max(1, _activeData.Rows);

            float headerH = isTable ? 20f * s : 0f;
            float totalContentH = _currentHeight * s - 36f * s;
            float availRowsH = Mathf.Max(20f * s, totalContentH - headerH);
            float rowH = Mathf.Max(16f * s, availRowsH / rows);

            if (_tableHeaderUI != null && isTable)
            {
                _tableHeaderUI.HeaderRt.anchoredPosition = Vector2.zero;
                _tableHeaderUI.HeaderRt.sizeDelta = new Vector2(0f, headerH);
                for (int c = 0; c < _tableHeaderUI.HeaderTexts.Count; c++)
                {
                    Text t = _tableHeaderUI.HeaderTexts[c];
                    if (t != null)
                    {
                        RectTransform tRt = t.rectTransform;
                        tRt.anchorMin = new Vector2((float)c / cols, 0f);
                        tRt.anchorMax = new Vector2((float)(c + 1) / cols, 1f);
                        tRt.offsetMin = new Vector2(2f * s, 0f);
                        tRt.offsetMax = new Vector2(-2f * s, 0f);
                    }
                }
            }

            float startY = isTable ? -headerH : 0f;
            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var rowUi = _runtimeRows[r];
                rowUi.RowRt.anchoredPosition = new Vector2(0f, startY - r * rowH);
                rowUi.RowRt.sizeDelta = new Vector2(0f, rowH);

                for (int c = 0; c < rowUi.Cells.Count; c++)
                {
                    var cell = rowUi.Cells[c];
                    cell.CellRt.anchorMin = new Vector2((float)c / cols, 0f);
                    cell.CellRt.anchorMax = new Vector2((float)(c + 1) / cols, 1f);
                    cell.CellRt.offsetMin = Vector2.zero;
                    cell.CellRt.offsetMax = Vector2.zero;
                }
            }
        }

        // ==========================================
        // IDynamicSlotWidget 契约接口显式实现
        // ==========================================
        public string SlotOrchestratorTitle => I18n.Tr("MJ_SLOT_ORCHESTRATOR_TITLE", "MJ 综合遥测矩阵: 自由加减行与列、自定义标签与 736+ 参数");

        private readonly List<DynamicSlotDescriptor> _cachedDescriptors = new List<DynamicSlotDescriptor>();
        public IReadOnlyList<DynamicSlotDescriptor> DynamicSlots
        {
            get
            {
                _cachedDescriptors.Clear();
                if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
                for (int r = 0; r < _activeData.Rows; r++)
                {
                    for (int c = 0; c < _activeData.Columns; c++)
                    {
                        var cell = _activeData.Grid[r][c];
                        string title = string.IsNullOrEmpty(cell.Label) ? $"R{r + 1}C{c + 1}" : cell.Label;
                        _cachedDescriptors.Add(new DynamicSlotDescriptor($"slot_{r}_{c}", title, cell.Token, c < _activeData.Columns - 1));
                    }
                }
                return _cachedDescriptors;
            }
        }

        public void AddDynamicSlot(string token, string title = null)
        {
            if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            _activeData.AddRow();
            int lastRow = _activeData.Rows - 1;
            if (_activeData.Grid[lastRow].Count > 0)
            {
                _activeData.Grid[lastRow][0].Token = token;
                if (!string.IsNullOrEmpty(title)) _activeData.Grid[lastRow][0].Label = title;
            }
            SaveAndApplyTemplate();
        }

        public void RemoveDynamicSlot(int index)
        {
            if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            if (index >= 0 && index < _activeData.Rows)
            {
                _activeData.RemoveRow(index);
                SaveAndApplyTemplate();
            }
        }

        public void MoveDynamicSlot(int fromIndex, int toIndex)
        {
            if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            _activeData.MoveRow(fromIndex, toIndex);
            SaveAndApplyTemplate();
        }

        public void ToggleDynamicSlotSeparator(int index)
        {
            // 分割线随列数自动维护
        }

        public void UpdateDynamicSlotToken(int index, string newToken)
        {
            if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            int cols = _activeData.Columns;
            int r = index / cols;
            int c = index % cols;
            if (r < _activeData.Rows && c < cols)
            {
                _activeData.Grid[r][c].Token = newToken;
                SaveAndApplyTemplate();
            }
        }

        public void UpdateDynamicSlotTitle(int index, string newTitle)
        {
            if (_activeData == null) _activeData = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            int cols = _activeData.Columns;
            int r = index / cols;
            int c = index % cols;
            if (r < _activeData.Rows && c < cols)
            {
                _activeData.Grid[r][c].Label = newTitle;
                SaveAndApplyTemplate();
            }
        }

        public void ResetToDefaultDynamicSlots()
        {
            _activeData = TelemetryMatrixData.CreateDefaultKeyValue();
            SaveAndApplyTemplate();
        }

        private void SaveAndApplyTemplate()
        {
            if (Config != null)
            {
                Config.CustomTemplate = _activeData.ToTemplate();
            }
            _cachedTemplate = Config?.CustomTemplate;
            _needsUiRebuild = true;
            WidgetLayoutManager.Instance?.SaveLayout();
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
