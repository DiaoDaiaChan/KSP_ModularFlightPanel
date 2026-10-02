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
    /// 单元格快照 (0 GC struct)
    /// </summary>
    public struct CustomTokenCellSnapshot
    {
        public string Value;
        public TextStyleRole Role;
    }

    /// <summary>
    /// 多通道遥测综合矩阵卡状态快照 (0 GC struct)
    /// </summary>
    public struct CustomTokenTextState : IEquatable<CustomTokenTextState>
    {
        public bool HasVessel;
        public string Title;
        public string Badge;
        public int Version;

        public bool Equals(CustomTokenTextState other)
        {
            return HasVessel == other.HasVessel &&
                   Version == other.Version &&
                   Title == other.Title &&
                   Badge == other.Badge;
        }

        public override bool Equals(object obj) => obj is CustomTokenTextState other && Equals(other);
        public override int GetHashCode() => (Title, Badge, Version).GetHashCode();
    }

    /// <summary>
    /// 多通道遥测综合矩阵卡业务大脑 (MFP-SPEC-012)
    /// </summary>
    public class CustomTokenTextLogic : WidgetLogic<CustomTokenTextState>
    {
        public TelemetryMatrixData ActiveData;
        public string DisplayTitleTemplate;

        private CustomTokenCellSnapshot[] _cellSnapshots = new CustomTokenCellSnapshot[64];
        private int _version = 0;

        public CustomTokenCellSnapshot GetCell(int r, int c)
        {
            if (ActiveData == null) return default;
            int idx = r * ActiveData.Columns + c;
            if (idx >= 0 && idx < _cellSnapshots.Length)
            {
                return _cellSnapshots[idx];
            }
            return default;
        }

        public override void Reset()
        {
            CurrentState = default;
            _version = 0;
            for (int i = 0; i < _cellSnapshots.Length; i++)
            {
                _cellSnapshots[i] = default;
            }
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (ActiveData == null || telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    CurrentState = new CustomTokenTextState { HasVessel = false };
                }
                return;
            }

            string displayTitle = !string.IsNullOrEmpty(DisplayTitleTemplate) ? DisplayTitleTemplate : "多通道遥测综合矩阵卡";
            string title = BaseFlightWidget.EvalToken(displayTitle, telemetry, displayTitle);

            string badge = (ActiveData.Mode == MatrixDisplayMode.Table) 
                ? $"{ActiveData.Columns}-COL TABLE" 
                : $"{ActiveData.Rows}x{ActiveData.Columns} MJ";

            int totalCells = ActiveData.Rows * ActiveData.Columns;
            if (_cellSnapshots.Length < totalCells)
            {
                _cellSnapshots = new CustomTokenCellSnapshot[Math.Max(totalCells, _cellSnapshots.Length * 2)];
            }

            for (int r = 0; r < ActiveData.Rows; r++)
            {
                for (int c = 0; c < ActiveData.Columns; c++)
                {
                    int idx = r * ActiveData.Columns + c;
                    string token = (r < ActiveData.Grid.Count && c < ActiveData.Grid[r].Count) ? ActiveData.Grid[r][c].Token : null;
                    if (string.IsNullOrEmpty(token))
                    {
                        _cellSnapshots[idx] = new CustomTokenCellSnapshot
                        {
                            Value = "---",
                            Role = TextStyleRole.PrimaryValue
                        };
                        continue;
                    }

                    string evalStr = BaseFlightWidget.EvalToken(token, telemetry, "---");
                    TextStyleRole role = EvaluateSemanticRole(token, telemetry);
                    _cellSnapshots[idx] = new CustomTokenCellSnapshot
                    {
                        Value = evalStr,
                        Role = role
                    };
                }
            }

            _version++;
            CurrentState = new CustomTokenTextState
            {
                HasVessel = true,
                Title = title,
                Badge = badge,
                Version = _version
            };
        }

        public static TextStyleRole EvaluateSemanticRole(string token, IFlightTelemetry telemetry)
        {
            if (telemetry == null) return TextStyleRole.PrimaryValue;
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
    }

    /// <summary>
    /// 多通道遥测综合矩阵卡片 (MechJeb Telemetry Matrix Card)
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

        private readonly CustomTokenTextLogic _logic = new CustomTokenTextLogic();
        protected override IWidgetLogic LogicCore => _logic;

        private readonly Cached<string> _lastTitleStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastBadgeStr = new Cached<string>(string.Empty);

        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(160f, 50f);
        public Vector2 MaxBaseSize => new Vector2(1600f, 1000f);

        private readonly CachedFloat _currentWidth = new CachedFloat(280f, 0.05f);
        private readonly CachedFloat _currentHeight = new CachedFloat(100f, 0.05f);
        private readonly Cached<bool> _isCustomResized = new Cached<bool>(false);

        private GameObject _headerObj;
        private Text _statusDotText;
        private Text _busTagText;
        private Text _titleText;
        private GameObject _badgeContainer;
        private Text _badgeText;
        private Image _badgeBg;
        private Outline _badgeOutline;
        private Image _headerDivider;
        private Image _headerAccentGlow;

        private GameObject _gridContainerObj;
        private RectTransform _gridContainerRt;

        private class CellRuntimeUI
        {
            public int Row;
            public int Col;
            public string Label = "";
            public string Token = "";

            public GameObject CellObj;
            public RectTransform CellRt;
            public Image TileBg;
            public Outline TileOutline;
            public Image StatusPip;
            public Text LabelText;
            public Text ValText;

            public readonly Cached<string> LastVal = new Cached<string>(string.Empty);
            public readonly Cached<Color> LastColor = new Cached<Color>(Color.clear);
            public readonly Cached<Color> LastBgColor = new Cached<Color>(Color.clear);
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
        private readonly Cached<TelemetryMatrixData> _activeData = new Cached<TelemetryMatrixData>(null);
        private string _cachedTemplate = null;
        private readonly Cached<bool> _needsUiRebuild = new Cached<bool>(false);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = ResolveEffectiveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            RectTransform hRt = CreateContainer("HeaderRow", transform);
            _headerObj = hRt.gameObject;
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.SetAnchoredPositionSafe(Vector2.zero);
            hRt.SetSizeDeltaSafe(new Vector2(0f, 28f * s));

            // 1. 状态信标指示灯 (Live Telemetry Bus Indicator)
            _statusDotText = UIFactory.CreateText(_headerObj.transform, "StatusDot", "●", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform dotRt = _statusDotText.rectTransform;
            dotRt.anchorMin = new Vector2(0f, 0.5f);
            dotRt.anchorMax = new Vector2(0f, 0.5f);
            dotRt.pivot = new Vector2(0f, 0.5f);
            dotRt.SetAnchoredPositionSafe(new Vector2(10f * s, 0f));
            dotRt.SetSizeDeltaSafe(new Vector2(12f * s, 18f * s));

            // 2. 总线微标签与主标题 (Bus Micro Tag & Title)
            _busTagText = UIFactory.CreateText(_headerObj.transform, "BusTag", "TLM // MATRIX", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform bTagRt = _busTagText.rectTransform;
            bTagRt.anchorMin = new Vector2(0f, 1f);
            bTagRt.anchorMax = new Vector2(0.6f, 1f);
            bTagRt.pivot = new Vector2(0f, 1f);
            bTagRt.SetAnchoredPositionSafe(new Vector2(24f * s, -4f * s));
            bTagRt.SetSizeDeltaSafe(new Vector2(120f * s, 10f * s));

            _titleText = UIFactory.CreateText(_headerObj.transform, "Title", DisplayName, Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform tRt = _titleText.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(0.65f, 1f);
            tRt.SetOffsetsSafe(new Vector2(24f * s, -4f * s), new Vector2(0f, -4f * s));

            // 3. 右侧胶囊高精徽标 (Pill Badge Capsule)
            GameObject badgePill = UIFactory.CreatePanel(_headerObj.transform, "BadgePill", new Vector2(76f * s, 16f * s), Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            _badgeContainer = badgePill;
            _badgeBg = badgePill.GetComponent<Image>();
            _badgeOutline = badgePill.AddComponent<Outline>();
            _badgeOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint, theme));
            _badgeOutline.effectDistance = new Vector2(1f * s, 1f * s);

            RectTransform bpRt = badgePill.GetComponent<RectTransform>();
            bpRt.anchorMin = new Vector2(1f, 0.5f);
            bpRt.anchorMax = new Vector2(1f, 0.5f);
            bpRt.pivot = new Vector2(1f, 0.5f);
            bpRt.SetAnchoredPositionSafe(new Vector2(-10f * s, 0f));

            _badgeText = UIFactory.CreateText(badgePill.transform, "Badge", I18n.Tr("WIDGET_MJ_MATRIX_BADGE", "3x2 MJ"), Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform bRt = _badgeText.rectTransform;
            bRt.anchorMin = Vector2.zero;
            bRt.anchorMax = Vector2.one;
            bRt.SetOffsetsSafe(new Vector2(2f * s, 0f), new Vector2(-2f * s, 0f));

            // 4. 发丝级分隔线与主强调发光槽 (Crisp Divider & Accent Glow)
            GameObject divObj = UIFactory.CreatePanel(_headerObj.transform, "HeaderDivider", new Vector2(0f, 1f * s), Vector2.zero, style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            _headerDivider = divObj.GetComponent<Image>();
            RectTransform divRt = _headerDivider.rectTransform;
            divRt.anchorMin = new Vector2(0f, 0f);
            divRt.anchorMax = new Vector2(1f, 0f);
            divRt.pivot = new Vector2(0.5f, 0f);
            divRt.SetSizeDeltaSafe(new Vector2(-16f * s, 1f * s));
            divRt.SetAnchoredPositionSafe(new Vector2(0f, 0f));

            GameObject glowObj = UIFactory.CreatePanel(_headerObj.transform, "AccentGlow", new Vector2(36f * s, 1.5f * s), Vector2.zero, theme.AccentPrimary);
            _headerAccentGlow = glowObj.GetComponent<Image>();
            RectTransform glowRt = _headerAccentGlow.rectTransform;
            glowRt.anchorMin = new Vector2(0f, 0f);
            glowRt.anchorMax = new Vector2(0f, 0f);
            glowRt.pivot = new Vector2(0f, 0f);
            glowRt.SetAnchoredPositionSafe(new Vector2(10f * s, -0.5f * s));

            _gridContainerRt = CreateContainer("GridContainer", transform);
            _gridContainerObj = _gridContainerRt.gameObject;
            _gridContainerRt.anchorMin = new Vector2(0f, 0f);
            _gridContainerRt.anchorMax = new Vector2(1f, 1f);
            _gridContainerRt.SetOffsetsSafe(new Vector2(6f * s, 6f * s), new Vector2(-6f * s, -30f * s));

            _cachedTemplate = Config?.CustomTemplate;
            _activeData.Value = TelemetryMatrixData.FromTemplate(_cachedTemplate);
            _logic.ActiveData = _activeData.Value;
            _logic.DisplayTitleTemplate = Config?.DisplayName;

            RebuildUI(theme);
        }

        private Vector2 GetDynamicBaseSize()
        {
            var data = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            if (data.Mode == MatrixDisplayMode.Table)
            {
                float w = Mathf.Max(260f, data.Columns * 68f + 20f);
                float h = 32f + 20f + data.Rows * 20f + 12f;
                return new Vector2(w, h);
            }
            else
            {
                float colW = data.Columns == 1 ? 240f : 148f;
                float w = Mathf.Max(220f, data.Columns * colW + 16f);
                float h = 32f + data.Rows * 25f + 12f;
                return new Vector2(w, h);
            }
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            _currentWidth.Value = pixelSize.x / s;
            _currentHeight.Value = pixelSize.y / s;
            _isCustomResized.Value = true;
            UpdateLayoutGeometry();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            theme = ResolveEffectiveTheme(theme);
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_titleText != null) _titleText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            if (_busTagText != null) _busTagText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            if (_statusDotText != null) _statusDotText.SetColor(style.GetTextColor(TextStyleRole.Accent, theme));
            if (_badgeText != null) _badgeText.SetColor(style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            if (_badgeBg != null) _badgeBg.SetColor(style.GetSurfaceColor(SurfaceStyleRole.Inset, theme));
            if (_badgeOutline != null) _badgeOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint, theme));
            if (_headerDivider != null) _headerDivider.SetColor(style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            if (_headerAccentGlow != null) _headerAccentGlow.SetColor(theme.AccentPrimary);

            Color rowTintEven = style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme);
            Color rowTintOdd = Color.clear;

            if (_tableHeaderUI != null)
            {
                if (_tableHeaderUI.HeaderBg != null) _tableHeaderUI.HeaderBg.SetColor(rowTintEven);
                if (_tableHeaderUI.HeaderDivider != null) _tableHeaderUI.HeaderDivider.SetColor(style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
                for (int i = 0; i < _tableHeaderUI.HeaderTexts.Count; i++)
                {
                    if (_tableHeaderUI.HeaderTexts[i] != null)
                    {
                        _tableHeaderUI.HeaderTexts[i].SetColor(style.GetTextColor(TextStyleRole.Label, theme));
                    }
                }
            }

            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                if (row.RowBg != null)
                {
                    row.RowBg.SetColor((r % 2 == 0) ? rowTintEven : rowTintOdd);
                }
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.TileBg != null) cell.TileBg.SetColor(style.GetSurfaceColor(SurfaceStyleRole.Slot, theme));
                    if (cell.TileOutline != null) cell.TileOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
                    if (cell.StatusPip != null) cell.StatusPip.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Normal, theme));
                    if (cell.LabelText != null) cell.LabelText.SetColor(style.GetTextColor(TextStyleRole.Label, theme));
                    if (cell.ValText != null) cell.ValText.SetColor(style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                    cell.LastColor.Reset(Color.clear);
                    cell.LastBgColor.Reset(Color.clear);
                }
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            string curTpl = Config?.CustomTemplate;
            if (curTpl != _cachedTemplate)
            {
                _cachedTemplate = curTpl;
                _activeData.Value = TelemetryMatrixData.FromTemplate(_cachedTemplate);
                _logic.ActiveData = _activeData.Value;
                _needsUiRebuild.Value = true;
            }

            _logic.DisplayTitleTemplate = !string.IsNullOrEmpty(Config?.DisplayName) ? Config.DisplayName : I18n.Tr("WIDGET_NAME_CUSTOM_TOKEN", "多通道遥测综合矩阵卡");

            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            if (_needsUiRebuild.Value)
            {
                _needsUiRebuild.Value = false;
                RebuildUI(context.Theme);
            }

            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;

            if (_lastTitleStr.Update(state.Title) && _titleText != null)
            {
                _titleText.SetTextSafe(state.Title);
            }

            if (_lastBadgeStr.Update(state.Badge) && _badgeText != null)
            {
                _badgeText.SetTextSafe(state.Badge);
            }

            var theme = ResolveEffectiveTheme(null);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (!state.HasVessel)
            {
                if (_statusDotText != null) _statusDotText.SetColor(style.GetTextColor(TextStyleRole.Muted, theme));
                SetAllCellsFallback("---", TextStyleRole.PrimaryValue);
                return;
            }

            bool hasWarning = false;
            bool hasDanger = false;

            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    var snap = _logic.GetCell(r, c);
                    string valStr = snap.Value ?? "---";

                    if (snap.Role == TextStyleRole.Danger) hasDanger = true;
                    else if (snap.Role == TextStyleRole.Warning) hasWarning = true;

                    if (cell.ValText != null)
                    {
                        if (cell.LastVal.Update(valStr))
                        {
                            cell.ValText.SetTextSafe(valStr);
                        }
                        Color roleCol = style.GetTextColor(snap.Role, theme);
                        if (cell.LastColor.Update(roleCol))
                        {
                            cell.ValText.SetColor(roleCol);
                        }
                    }

                    if (cell.TileBg != null && cell.StatusPip != null)
                    {
                        Color pipCol;
                        Color tileBgCol;
                        Color outlineCol;

                        if (snap.Role == TextStyleRole.Danger)
                        {
                            pipCol = theme.DangerColor;
                            tileBgCol = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.18f);
                            outlineCol = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Subtle, theme);
                        }
                        else if (snap.Role == TextStyleRole.Warning)
                        {
                            pipCol = theme.WarningColor;
                            tileBgCol = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.14f);
                            outlineCol = WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Subtle, theme);
                        }
                        else
                        {
                            pipCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Normal, theme);
                            tileBgCol = style.GetSurfaceColor(SurfaceStyleRole.Slot, theme);
                            outlineCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);
                        }

                        if (cell.LastBgColor.Update(tileBgCol))
                        {
                            cell.TileBg.SetColor(tileBgCol);
                            cell.StatusPip.SetColor(pipCol);
                            if (cell.TileOutline != null) cell.TileOutline.SetColor(outlineCol);
                        }
                    }
                }
            }

            if (_statusDotText != null)
            {
                Color dotCol = hasDanger ? theme.DangerColor : (hasWarning ? theme.WarningColor : style.GetTextColor(TextStyleRole.Accent, theme));
                _statusDotText.SetColor(dotCol);
            }
        }

        private void SetAllCellsFallback(string fallback, TextStyleRole role)
        {
            var theme = ResolveEffectiveTheme(null);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var row = _runtimeRows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.ValText != null)
                    {
                        if (cell.LastVal.Update(fallback)) cell.ValText.SetTextSafe(fallback);
                        Color col = style.GetTextColor(role, theme);
                        if (cell.LastColor.Update(col)) cell.ValText.SetColor(col);
                    }
                    if (cell.TileBg != null)
                    {
                        Color bgCol = style.GetSurfaceColor(SurfaceStyleRole.Slot, theme);
                        if (cell.LastBgColor.Update(bgCol))
                        {
                            cell.TileBg.SetColor(bgCol);
                            if (cell.StatusPip != null) cell.StatusPip.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Normal, theme));
                            if (cell.TileOutline != null) cell.TileOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
                        }
                    }
                }
            }
        }

        private void RebuildUI(ThemeConfig theme)
        {
            theme = theme ?? WidgetStyleManager.ResolveTheme(null);
            if (!_isCustomResized.Value)
            {
                Vector2 dynSize = GetDynamicBaseSize();
                _currentWidth.Value = dynSize.x;
                _currentHeight.Value = dynSize.y;
                RectTransform.SetSizeDeltaSafe(dynSize * CurrentDpiScale);
            }

            BuildGridUI(theme);
            ApplyTheme(theme);
        }

        private void BuildGridUI(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int i = _gridContainerRt.childCount - 1; i >= 0; i--)
            {
                Destroy(_gridContainerRt.GetChild(i).gameObject);
            }
            _runtimeRows.Clear();
            _tableHeaderUI = null;

            if (_activeData.Value == null) return;

            bool isTable = (_activeData.Value.Mode == MatrixDisplayMode.Table);

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
                _tableHeaderUI.HeaderRt.SetAnchoredPositionSafe(Vector2.zero);
                _tableHeaderUI.HeaderRt.SetSizeDeltaSafe(new Vector2(0f, 20f * s));

                GameObject hDivObj = UIFactory.CreatePanel(thGo.transform, "HLine", new Vector2(0f, 1f * s), Vector2.zero, style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
                _tableHeaderUI.HeaderDivider = hDivObj.GetComponent<Image>();
                RectTransform hDivRt = _tableHeaderUI.HeaderDivider.rectTransform;
                hDivRt.anchorMin = new Vector2(0f, 0f);
                hDivRt.anchorMax = new Vector2(1f, 0f);
                hDivRt.pivot = new Vector2(0f, 0f);
                hDivRt.SetSizeDeltaSafe(new Vector2(0f, 1f * s));
                hDivRt.SetAnchoredPositionSafe(Vector2.zero);

                for (int c = 0; c < _activeData.Value.Columns; c++)
                {
                    string hText = (c < _activeData.Value.TableHeaders.Count) ? _activeData.Value.TableHeaders[c] : $"Col {c + 1}";
                    Text txt = UIFactory.CreateText(thGo.transform, $"H_{c}", hText, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Label, theme));
                    _tableHeaderUI.HeaderTexts.Add(txt);
                }
            }

            for (int r = 0; r < _activeData.Value.Rows; r++)
            {
                var rowUi = new RowRuntimeUI { RowIndex = r };
                GameObject rGo = UIFactory.CreatePanel(_gridContainerRt, $"Row_{r}", new Vector2(0f, 24f * s), Vector2.zero, Color.clear);
                rowUi.RowObj = rGo;
                rowUi.RowRt = rGo.GetComponent<RectTransform>();
                rowUi.RowBg = rGo.GetComponent<Image>();

                rowUi.RowRt.anchorMin = new Vector2(0f, 1f);
                rowUi.RowRt.anchorMax = new Vector2(1f, 1f);
                rowUi.RowRt.pivot = new Vector2(0f, 1f);

                for (int c = 0; c < _activeData.Value.Columns; c++)
                {
                    var cellData = (r < _activeData.Value.Grid.Count && c < _activeData.Value.Grid[r].Count) ? _activeData.Value.Grid[r][c] : new MatrixCellData();
                    var cellUi = new CellRuntimeUI
                    {
                        Row = r,
                        Col = c,
                        Label = cellData.Label,
                        Token = cellData.Token
                    };

                    RectTransform cellRt = CreateContainer($"Cell_{r}_{c}", rGo.transform);
                    cellUi.CellObj = cellRt.gameObject;
                    cellUi.CellRt = cellRt;

                    if (isTable)
                    {
                        cellUi.ValText = UIFactory.CreateText(cellUi.CellObj.transform, "Val", "---", Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                        RectTransform vRt = cellUi.ValText.rectTransform;
                        vRt.anchorMin = Vector2.zero;
                        vRt.anchorMax = Vector2.one;
                        vRt.SetOffsetsSafe(new Vector2(2f * s, 0f), new Vector2(-2f * s, 0f));
                    }
                    else
                    {
                        // 1. 独立现代卡片凹槽瓦片 (Glass Slot Tile)
                        GameObject tileGo = UIFactory.CreatePanel(cellRt, "TileBg", Vector2.zero, Vector2.zero, style.GetSurfaceColor(SurfaceStyleRole.Slot, theme));
                        cellUi.TileBg = tileGo.GetComponent<Image>();
                        RectTransform tRt = tileGo.GetComponent<RectTransform>();
                        tRt.anchorMin = Vector2.zero;
                        tRt.anchorMax = Vector2.one;
                        tRt.SetOffsetsSafe(new Vector2(2f * s, 2f * s), new Vector2(-2f * s, -2f * s));

                        cellUi.TileOutline = tileGo.AddComponent<Outline>();
                        cellUi.TileOutline.SetColor(WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme));
                        cellUi.TileOutline.effectDistance = new Vector2(1f * s, 1f * s);

                        // 2. 瓦片左侧状态竖条指示灯 (Status Pip)
                        GameObject pipGo = UIFactory.CreatePanel(tileGo.transform, "StatusPip", new Vector2(2.5f * s, 0f), Vector2.zero, WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Normal, theme));
                        cellUi.StatusPip = pipGo.GetComponent<Image>();
                        RectTransform pipRt = pipGo.GetComponent<RectTransform>();
                        pipRt.anchorMin = new Vector2(0f, 0.18f);
                        pipRt.anchorMax = new Vector2(0f, 0.82f);
                        pipRt.pivot = new Vector2(0f, 0.5f);
                        pipRt.SetSizeDeltaSafe(new Vector2(2.5f * s, 0f));
                        pipRt.SetAnchoredPositionSafe(new Vector2(3f * s, 0f));

                        // 3. 标签文案
                        cellUi.LabelText = UIFactory.CreateText(tileGo.transform, "Lbl", cellUi.Label, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
                        RectTransform lRt = cellUi.LabelText.rectTransform;
                        lRt.anchorMin = new Vector2(0f, 0f);
                        lRt.anchorMax = new Vector2(0.42f, 1f);
                        lRt.SetOffsetsSafe(new Vector2(9f * s, 0f), Vector2.zero);

                        // 4. 读数大字
                        cellUi.ValText = UIFactory.CreateText(tileGo.transform, "Val", "---", Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                        cellUi.ValText.fontStyle = FontStyle.Bold;
                        RectTransform vRt = cellUi.ValText.rectTransform;
                        vRt.anchorMin = new Vector2(0.38f, 0f);
                        vRt.anchorMax = new Vector2(1f, 1f);
                        vRt.SetOffsetsSafe(Vector2.zero, new Vector2(-6f * s, 0f));
                    }

                    rowUi.Cells.Add(cellUi);
                }

                _runtimeRows.Add(rowUi);
            }

            UpdateLayoutGeometry();
        }

        private void UpdateLayoutGeometry()
        {
            if (_activeData.Value == null) return;
            float s = CurrentDpiScale;
            bool isTable = (_activeData.Value.Mode == MatrixDisplayMode.Table);

            int cols = Mathf.Max(1, _activeData.Value.Columns);
            int rows = Mathf.Max(1, _activeData.Value.Rows);

            float headerH = isTable ? 20f * s : 0f;
            float totalContentH = _currentHeight.Value * s - 38f * s;
            float availRowsH = Mathf.Max(20f * s, totalContentH - headerH);
            float rowH = Mathf.Max(18f * s, availRowsH / rows);

            if (_tableHeaderUI != null && isTable)
            {
                _tableHeaderUI.HeaderRt.SetAnchoredPositionSafe(Vector2.zero);
                _tableHeaderUI.HeaderRt.SetSizeDeltaSafe(new Vector2(0f, headerH));
                for (int c = 0; c < _tableHeaderUI.HeaderTexts.Count; c++)
                {
                    Text t = _tableHeaderUI.HeaderTexts[c];
                    if (t != null)
                    {
                        RectTransform tRt = t.rectTransform;
                        tRt.anchorMin = new Vector2((float)c / cols, 0f);
                        tRt.anchorMax = new Vector2((float)(c + 1) / cols, 1f);
                        tRt.SetOffsetsSafe(new Vector2(2f * s, 0f), new Vector2(-2f * s, 0f));
                    }
                }
            }

            float startY = isTable ? -headerH : 0f;
            for (int r = 0; r < _runtimeRows.Count; r++)
            {
                var rowUi = _runtimeRows[r];
                rowUi.RowRt.SetAnchoredPositionSafe(new Vector2(0f, startY - r * rowH));
                rowUi.RowRt.SetSizeDeltaSafe(new Vector2(0f, rowH));

                for (int c = 0; c < rowUi.Cells.Count; c++)
                {
                    var cell = rowUi.Cells[c];
                    cell.CellRt.anchorMin = new Vector2((float)c / cols, 0f);
                    cell.CellRt.anchorMax = new Vector2((float)(c + 1) / cols, 1f);
                    cell.CellRt.SetOffsetsSafe(Vector2.zero, Vector2.zero);
                }
            }
        }

        public string SlotOrchestratorTitle => I18n.Tr("MJ_SLOT_ORCHESTRATOR_TITLE", "MJ 综合遥测矩阵: 自由加减行与列、自定义标签与 736+ 参数");

        private readonly List<DynamicSlotDescriptor> _cachedDescriptors = new List<DynamicSlotDescriptor>();
        public IReadOnlyList<DynamicSlotDescriptor> DynamicSlots
        {
            get
            {
                _cachedDescriptors.Clear();
                if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
                for (int r = 0; r < _activeData.Value.Rows; r++)
                {
                    for (int c = 0; c < _activeData.Value.Columns; c++)
                    {
                        var cell = _activeData.Value.Grid[r][c];
                        string title = string.IsNullOrEmpty(cell.Label) ? $"R{r + 1}C{c + 1}" : cell.Label;
                        _cachedDescriptors.Add(new DynamicSlotDescriptor($"slot_{r}_{c}", title, cell.Token, c < _activeData.Value.Columns - 1));
                    }
                }
                return _cachedDescriptors;
            }
        }

        public void AddDynamicSlot(string token, string title = null)
        {
            if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            _activeData.Value.AddRow();
            int lastRow = _activeData.Value.Rows - 1;
            if (_activeData.Value.Grid[lastRow].Count > 0)
            {
                _activeData.Value.Grid[lastRow][0].Token = token;
                if (!string.IsNullOrEmpty(title)) _activeData.Value.Grid[lastRow][0].Label = title;
            }
            SaveAndApplyTemplate();
        }

        public void RemoveDynamicSlot(int index)
        {
            if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            if (index >= 0 && index < _activeData.Value.Rows)
            {
                _activeData.Value.RemoveRow(index);
                SaveAndApplyTemplate();
            }
        }

        public void MoveDynamicSlot(int fromIndex, int toIndex)
        {
            if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            _activeData.Value.MoveRow(fromIndex, toIndex);
            SaveAndApplyTemplate();
        }

        public void ToggleDynamicSlotSeparator(int index)
        {
        }

        public void UpdateDynamicSlotToken(int index, string newToken)
        {
            if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            int cols = _activeData.Value.Columns;
            int r = index / cols;
            int c = index % cols;
            if (r < _activeData.Value.Rows && c < cols)
            {
                _activeData.Value.Grid[r][c].Token = newToken;
                SaveAndApplyTemplate();
            }
        }

        public void UpdateDynamicSlotTitle(int index, string newTitle)
        {
            if (_activeData.Value == null) _activeData.Value = TelemetryMatrixData.FromTemplate(Config?.CustomTemplate);
            int cols = _activeData.Value.Columns;
            int r = index / cols;
            int c = index % cols;
            if (r < _activeData.Value.Rows && c < cols)
            {
                _activeData.Value.Grid[r][c].Label = newTitle;
                SaveAndApplyTemplate();
            }
        }

        public void ResetToDefaultDynamicSlots()
        {
            _activeData.Value = TelemetryMatrixData.CreateDefaultKeyValue();
            SaveAndApplyTemplate();
        }

        private void SaveAndApplyTemplate()
        {
            if (Config != null)
            {
                Config.CustomTemplate = _activeData.Value.ToTemplate();
            }
            _cachedTemplate = Config?.CustomTemplate;
            _logic.ActiveData = _activeData.Value;
            RebuildUI(ResolveEffectiveTheme(null));
            WidgetLayoutManager.Instance?.SaveLayout();
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _logic.Reset();
            _lastTitleStr.Reset(string.Empty);
            _lastBadgeStr.Reset(string.Empty);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
