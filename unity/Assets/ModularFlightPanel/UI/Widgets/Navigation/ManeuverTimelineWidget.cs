using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 横排时间轴机动节点指示器 (Maneuver Timeline Widget)
    /// ====================================================================================
    /// 核心功能特性：
    /// 1. 轨道时序时间轴 (Timeline Ribbon)：
    ///    直观呈现进场巡航 (Approach)、点火起点 (Ignition)、节点交点 (T0 Node) 与点火结束 (Burnout) 全时序；
    ///    动态推进光标与点火进度条实时映射当前飞船时间位置与消耗进度。
    /// 2. 三轴机动速度矢量分解 (3-Axis Velocity Vector Breakdown)：
    ///    宽幅横排排布，分别展示切向 (Prograde / Retrograde)、法向 (Normal / Antinormal)、
    ///    径向 (Radial Out / Radial In) 独立速度增量与双向微型游标标尺。
    /// 3. 多源遥测契约驱动与优雅降级：
    ///    优先挂钩 Principia 高精度 N 体飞行计划 (Principia Hook)，优雅兜底原版 PatchedConicSolver
    ///    开普勒两体机动节点与 MechJeb，100% 经由纯 C# IFlightTelemetry 与 TelemetryTokenEngine 驱动。
    /// 4. 严格遵守 MFP 规范：
    ///    0 颜色字面量 (MFP-SPEC-006 零容忍)、0 场景查询 (MFP-SPEC-007)、DPI 缩放与脏检查零 GC。
    /// </summary>
    public class ManeuverTimelineWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // UI 根与卡片
        private Image _bgImage;
        private Outline _bgOutline;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 顶部 Header 控件
        private Text _titleText;
        private Text _sourceBadgeText;
        private Text _deltaVText;
        private Text _unitText;
        private Text _statusBadgeText;

        // 交互按钮
        private Button _btnWarp;
        private Image _btnWarpImg;
        private Text _btnWarpText;
        private Button _btnDismiss;
        private Image _btnDismissImg;
        private Text _btnDismissText;

        // 中间时间轴控件
        private RectTransform _timelineTrackRt;
        private Image _timelineTrackImg;
        private RectTransform _burnZoneRt;
        private Image _burnZoneImg;
        private RectTransform _burnFillRt;
        private Image _burnFillImg;
        private RectTransform _cursorPipRt;
        private Image _cursorPipImg;

        // 时间轴标尺文字
        private Text _lblApproach;
        private Text _lblIgnition;
        private Text _lblNode;
        private Text _lblBurnout;
        private Text _timelineInfoText;

        // 底部三轴矢量卡片
        // 1. Prograde
        private Image _proBg;
        private Outline _proOutline;
        private Text _proLabel;
        private Text _proValueText;
        private RectTransform _proBarFillRt;
        private Image _proBarFillImg;

        // 2. Normal
        private Image _normBg;
        private Outline _normOutline;
        private Text _normLabel;
        private Text _normValueText;
        private RectTransform _normBarFillRt;
        private Image _normBarFillImg;

        // 3. Radial
        private Image _radBg;
        private Outline _radOutline;
        private Text _radLabel;
        private Text _radValueText;
        private RectTransform _radBarFillRt;
        private Image _radBarFillImg;

        // 通配符通道与可覆盖模板
        private string _titleTemplate = "MANEUVER TIMELINE";
        private string _deltaVToken = "{MN:DV}";
        private string _totalDvToken = "{MN:TOTAL_DV}";
        private string _tNodeToken = "{MN:TNODE}";
        private string _burnTimeToken = "{MN:BURN}";
        private string _timeToBurnToken = "{MN:BURNTIME}";
        private string _proToken = "{MN:PRO}";
        private string _normToken = "{MN:NORM}";
        private string _radToken = "{MN:RAD}";
        private string _sourceToken = "{MN:SOURCE}";
        private string _statusToken = "{MN:STATUS}";

        // 脏检查缓存
        private bool _lastHasNode = false;
        private double _lastDeltaV = double.NaN;
        private double _lastTotalDv = double.NaN;
        private double _lastTimeToNode = double.NaN;
        private double _lastBurnTime = double.NaN;
        private double _lastTimeToBurn = double.NaN;
        private double _lastPrograde = double.NaN;
        private double _lastNormal = double.NaN;
        private double _lastRadial = double.NaN;
        private string _lastSourceStr = string.Empty;
        private string _lastStatusStr = string.Empty;
        private string _lastTimelineInfoStr = string.Empty;
        private float _lastPipNormalized = -1f;

        // 时间轴归一化几何分区常量
        private const float TrackWidthLogical = 492f;
        private const float ZoneIgnitionNorm = 0.44f;
        private const float ZoneNodeNorm = 0.64f;
        private const float ZoneBurnoutNorm = 0.84f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 卡片包围盒 (基准 520 x 115 逻辑像素)
            Vector2 cardSize = new Vector2(520f * s, 115f * s);
            RectTransform.sizeDelta = cardSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            ParseCustomTemplate(config);

            // 2. 顶部 Header (标题 + 来源徽标 + ΔV 读数 + 状态徽标 + 交互按钮)
            BuildHeader(s, style, theme);

            // 3. 中间时间轴通道 (Timeline Ribbon)
            BuildTimeline(s, style, theme);

            // 4. 底部三轴矢量分解卡片 (3-Axis Vector Breakdown)
            BuildVectorBreakdown(s, style, theme);

            ApplyTheme(theme);
        }

        private void BuildHeader(float s, WidgetStyleManager style, ThemeConfig theme)
        {
            // 标题 (左侧)
            _titleText = UIFactory.CreateText(transform, "Header_Title", _titleTemplate, Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform titleRt = _titleText.rectTransform;
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(110f * s, 18f * s);
            titleRt.anchoredPosition = new Vector2(-246f * s, 42f * s);

            // 来源徽标 (紧邻标题右侧, [PRINCIPIA] / [STOCK])
            _sourceBadgeText = UIFactory.CreateText(transform, "Source_Badge", "[STANDBY]", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform srcRt = _sourceBadgeText.rectTransform;
            srcRt.pivot = new Vector2(0f, 0.5f);
            srcRt.anchorMin = srcRt.anchorMax = new Vector2(0.5f, 0.5f);
            srcRt.sizeDelta = new Vector2(70f * s, 18f * s);
            srcRt.anchoredPosition = new Vector2(-134f * s, 42f * s);

            // 中央核心主读数：剩余 ΔV 与单位
            _deltaVText = UIFactory.CreateText(transform, "DeltaV_Value", "---", Mathf.RoundToInt(18f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _deltaVText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = _deltaVText.rectTransform;
            dvRt.pivot = new Vector2(0.5f, 0.5f);
            dvRt.anchorMin = dvRt.anchorMax = new Vector2(0.5f, 0.5f);
            dvRt.sizeDelta = new Vector2(130f * s, 22f * s);
            dvRt.anchoredPosition = new Vector2(0f, 42f * s);

            _unitText = UIFactory.CreateText(transform, "DeltaV_Unit", "m/s", Mathf.RoundToInt(9f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform uRt = _unitText.rectTransform;
            uRt.pivot = new Vector2(0f, 0.5f);
            uRt.anchorMin = uRt.anchorMax = new Vector2(0.5f, 0.5f);
            uRt.sizeDelta = new Vector2(30f * s, 18f * s);
            uRt.anchoredPosition = new Vector2(58f * s, 42f * s);

            // 状态徽标 (ARMED, BURNING, COMPLETE, STANDBY)
            _statusBadgeText = UIFactory.CreateText(transform, "Status_Badge", "STANDBY", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform stRt = _statusBadgeText.rectTransform;
            stRt.pivot = new Vector2(1f, 0.5f);
            stRt.anchorMin = stRt.anchorMax = new Vector2(0.5f, 0.5f);
            stRt.sizeDelta = new Vector2(70f * s, 18f * s);
            stRt.anchoredPosition = new Vector2(174f * s, 42f * s);

            // WARP 按键
            Vector2 warpSize = new Vector2(32f * s, 16f * s);
            _btnWarp = UIFactory.CreateButton(transform, "Btn_Warp", warpSize, new Vector2(195f * s, 42f * s), OnWarpClicked);
            _btnWarpImg = _btnWarp.GetComponent<Image>();
            _btnWarpText = UIFactory.CreateText(_btnWarp.transform, "Text", "WARP", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnWarpText.rectTransform.sizeDelta = warpSize;
            _btnWarpText.rectTransform.anchoredPosition = Vector2.zero;

            // DEL 按键
            Vector2 delSize = new Vector2(28f * s, 16f * s);
            _btnDismiss = UIFactory.CreateButton(transform, "Btn_Del", delSize, new Vector2(230f * s, 42f * s), OnDismissClicked);
            _btnDismissImg = _btnDismiss.GetComponent<Image>();
            _btnDismissText = UIFactory.CreateText(_btnDismiss.transform, "Text", "DEL", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnDismissText.rectTransform.sizeDelta = delSize;
            _btnDismissText.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void BuildTimeline(float s, WidgetStyleManager style, ThemeConfig theme)
        {
            float trackW = TrackWidthLogical * s;
            float trackH = 8f * s;
            float timelineCenterY = 10f * s;

            // 1. 时间轴槽底 (Track)
            GameObject trackGo = UIFactory.CreatePanel(transform, "Timeline_Track", new Vector2(trackW, trackH),
                new Vector2(0f, timelineCenterY), style.GetMeterColor(MeterStyleRole.Track, theme));
            _timelineTrackRt = trackGo.GetComponent<RectTransform>();
            _timelineTrackImg = trackGo.GetComponent<Image>();

            // 2. 点火窗口区间底板 (Burn Zone Window: 0.44 .. 0.84)
            float burnZoneW = trackW * (ZoneBurnoutNorm - ZoneIgnitionNorm);
            float burnZoneX = -trackW * 0.5f + (ZoneIgnitionNorm + (ZoneBurnoutNorm - ZoneIgnitionNorm) * 0.5f) * trackW;
            Color zoneCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Heavy);
            GameObject zoneGo = UIFactory.CreatePanel(trackGo.transform, "Burn_Zone", new Vector2(burnZoneW, trackH),
                new Vector2(burnZoneX, 0f), zoneCol);
            _burnZoneRt = zoneGo.GetComponent<RectTransform>();
            _burnZoneImg = zoneGo.GetComponent<Image>();

            // 3. 点火消耗进度填充 (Burn Fill Bar)
            Color fillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);
            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Burn_Fill", new Vector2(0f, trackH),
                Vector2.zero, fillCol);
            _burnFillRt = fillGo.GetComponent<RectTransform>();
            _burnFillRt.pivot = new Vector2(0f, 0.5f);
            _burnFillRt.anchorMin = _burnFillRt.anchorMax = new Vector2(0f, 0.5f);
            _burnFillRt.anchoredPosition = new Vector2(ZoneIgnitionNorm * trackW, 0f);
            _burnFillImg = fillGo.GetComponent<Image>();

            // 4. T0 节点中心标记线 (T0 Node Mark)
            Color markCol = style.GetTextColor(TextStyleRole.Cardinal, theme);
            GameObject markGo = UIFactory.CreatePanel(trackGo.transform, "T0_Mark", new Vector2(2f * s, trackH + 6f * s),
                new Vector2(-trackW * 0.5f + ZoneNodeNorm * trackW, 0f), markCol);

            // 5. 动态飞船当前位置游标 (Vessel Position Pip)
            Color pipCol = theme.AccentPrimary;
            GameObject pipGo = UIFactory.CreatePanel(trackGo.transform, "Vessel_Pip", new Vector2(4f * s, trackH + 8f * s),
                new Vector2(-trackW * 0.5f, 0f), pipCol);
            _cursorPipRt = pipGo.GetComponent<RectTransform>();
            _cursorPipImg = pipGo.GetComponent<Image>();

            // 6. 时间轴里程碑文字标签 (置于时间轴上方, 彻底与下方信息解耦)
            Color lblCol = style.GetTextColor(TextStyleRole.Label, theme);
            int subFont = Mathf.RoundToInt(6.5f * s);
            float labelTopY = timelineCenterY + 11f * s;

            _lblApproach = CreateMilestoneLabel("APPROACH", -trackW * 0.5f + 0.15f * trackW, labelTopY, subFont, lblCol, s);
            _lblIgnition = CreateMilestoneLabel("IGNITION", -trackW * 0.5f + ZoneIgnitionNorm * trackW, labelTopY, subFont, lblCol, s);
            _lblNode = CreateMilestoneLabel("T0 NODE", -trackW * 0.5f + ZoneNodeNorm * trackW, labelTopY, subFont, style.GetTextColor(TextStyleRole.Cardinal, theme), s);
            _lblBurnout = CreateMilestoneLabel("BURNOUT", -trackW * 0.5f + ZoneBurnoutNorm * trackW, labelTopY, subFont, lblCol, s);

            // 7. 时间轴下方信息读数 (置于时间轴下方, 彻底消除文字与里程碑冲突)
            float labelBottomY = timelineCenterY - 11f * s;
            _timelineInfoText = UIFactory.CreateText(transform, "Timeline_Info", "T-NODE --:-- | BURN --s", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform infoRt = _timelineInfoText.rectTransform;
            infoRt.pivot = new Vector2(1f, 0.5f);
            infoRt.anchorMin = infoRt.anchorMax = new Vector2(0.5f, 0.5f);
            infoRt.sizeDelta = new Vector2(220f * s, 14f * s);
            infoRt.anchoredPosition = new Vector2(246f * s, labelBottomY);
        }

        private Text CreateMilestoneLabel(string text, float x, float y, int fontSize, Color color, float s)
        {
            Text txt = UIFactory.CreateText(transform, $"Lbl_{text}", text, fontSize, TextAnchor.MiddleCenter, color);
            txt.fontStyle = FontStyle.Bold;
            RectTransform rt = txt.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f * s, 12f * s);
            rt.anchoredPosition = new Vector2(x, y);
            return txt;
        }

        private void BuildVectorBreakdown(float s, WidgetStyleManager style, ThemeConfig theme)
        {
            float colW = 158f * s;
            float colH = 34f * s;
            float colY = -34f * s;
            float spacing = 8f * s;

            // Column 1: Prograde / Retrograde
            float x1 = -colW - spacing;
            BuildVectorColumn(x1, colY, colW, colH, "PROGRADE", s, style, theme,
                out _proBg, out _proOutline, out _proLabel, out _proValueText, out _proBarFillRt, out _proBarFillImg);

            // Column 2: Normal / Antinormal
            float x2 = 0f;
            BuildVectorColumn(x2, colY, colW, colH, "NORMAL", s, style, theme,
                out _normBg, out _normOutline, out _normLabel, out _normValueText, out _normBarFillRt, out _normBarFillImg);

            // Column 3: Radial Out / Radial In
            float x3 = colW + spacing;
            BuildVectorColumn(x3, colY, colW, colH, "RADIAL", s, style, theme,
                out _radBg, out _radOutline, out _radLabel, out _radValueText, out _radBarFillRt, out _radBarFillImg);
        }

        private void BuildVectorColumn(float posX, float posY, float w, float h, string title, float s,
            WidgetStyleManager style, ThemeConfig theme,
            out Image bg, out Outline outline, out Text label, out Text valText,
            out RectTransform barFillRt, out Image barFillImg)
        {
            // 底板
            GameObject cardGo = UIFactory.CreatePanel(transform, $"Col_{title}", new Vector2(w, h),
                new Vector2(posX, posY), Color.clear);
            bg = cardGo.GetComponent<Image>();
            outline = cardGo.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(bg, outline, CardStyleRole.SubtleSlot, theme);

            // 轴向标签 (左上)
            label = UIFactory.CreateText(cardGo.transform, "Label", title, Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            label.fontStyle = FontStyle.Bold;
            RectTransform lblRt = label.rectTransform;
            lblRt.pivot = new Vector2(0f, 0.5f);
            lblRt.anchorMin = lblRt.anchorMax = new Vector2(0f, 1f);
            lblRt.sizeDelta = new Vector2(75f * s, 14f * s);
            lblRt.anchoredPosition = new Vector2(6f * s, -8f * s);

            // 数值读数 (右上)
            valText = UIFactory.CreateText(cardGo.transform, "Value", "+0.0 m/s", Mathf.RoundToInt(10.5f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            valText.fontStyle = FontStyle.Bold;
            RectTransform valRt = valText.rectTransform;
            valRt.pivot = new Vector2(1f, 0.5f);
            valRt.anchorMin = valRt.anchorMax = new Vector2(1f, 1f);
            valRt.sizeDelta = new Vector2(75f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(-6f * s, -8f * s);

            // 底部双向标尺微槽 (Center-Zero Meter)
            float barW = w - 12f * s;
            float barH = 3.5f * s;
            GameObject trackGo = UIFactory.CreatePanel(cardGo.transform, "Meter_Track", new Vector2(barW, barH),
                new Vector2(0f, -h * 0.5f + 7f * s), style.GetMeterColor(MeterStyleRole.Track, theme));

            // 中央基准零刻度微线
            UIFactory.CreatePanel(trackGo.transform, "Center_Zero", new Vector2(1.5f * s, barH + 2f * s),
                Vector2.zero, style.GetTextColor(TextStyleRole.Label, theme));

            // 双向填充条 (从中心向左右扩展)
            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Meter_Fill", new Vector2(0f, barH),
                Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            barFillRt = fillGo.GetComponent<RectTransform>();
            barFillRt.pivot = new Vector2(0.5f, 0.5f);
            barFillImg = fillGo.GetComponent<Image>();
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.DisplayName) &&
                    !config.DisplayName.Contains("机动") &&
                    config.DisplayName != "TIMELINE")
                {
                    _titleTemplate = config.DisplayName.ToUpperInvariant();
                }
                else
                {
                    _titleTemplate = "MANEUVER TIMELINE";
                }
            }

            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "TITLE": _titleTemplate = v; break;
                    case "DV": _deltaVToken = v; break;
                    case "TOTAL_DV": _totalDvToken = v; break;
                    case "TNODE": _tNodeToken = v; break;
                    case "BURN": _burnTimeToken = v; break;
                    case "TIMETOBURN": _timeToBurnToken = v; break;
                    case "PRO": _proToken = v; break;
                    case "NORM": _normToken = v; break;
                    case "RAD": _radToken = v; break;
                    case "SOURCE": _sourceToken = v; break;
                    case "STATUS": _statusToken = v; break;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);

            // Header 文字
            ApplyText(_titleText, TextStyleRole.Label, theme);
            ApplyText(_sourceBadgeText, TextStyleRole.Cardinal, theme);
            ApplyText(_deltaVText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);
            ApplyText(_statusBadgeText, _currentCardRole == CardStyleRole.Emphasized ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

            // 按键
            if (_btnWarp != null) ApplyButton(_btnWarp, _btnWarpImg, _btnWarpText, ButtonVisualRole.Normal, false, theme);
            if (_btnDismiss != null) ApplyButton(_btnDismiss, _btnDismissImg, _btnDismissText, ButtonVisualRole.Normal, false, theme);

            // 时间轴
            if (_timelineTrackImg != null) _timelineTrackImg.color = style.GetMeterColor(MeterStyleRole.Track, theme);
            if (_burnZoneImg != null) _burnZoneImg.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Heavy);
            if (_burnFillImg != null) _burnFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
            if (_cursorPipImg != null) _cursorPipImg.color = theme.AccentPrimary;

            // 时间轴标尺文字
            ApplyText(_lblApproach, TextStyleRole.Label, theme);
            ApplyText(_lblIgnition, TextStyleRole.Label, theme);
            ApplyText(_lblNode, TextStyleRole.Cardinal, theme);
            ApplyText(_lblBurnout, TextStyleRole.Label, theme);
            ApplyText(_timelineInfoText, TextStyleRole.SecondaryValue, theme);

            // 三轴卡片
            ApplyCard(_proBg, _proOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_proLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_proValueText, TextStyleRole.PrimaryValue, theme);
            if (_proBarFillImg != null) _proBarFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyCard(_normBg, _normOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_normLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_normValueText, TextStyleRole.PrimaryValue, theme);
            if (_normBarFillImg != null) _normBarFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyCard(_radBg, _radOutline, CardStyleRole.SubtleSlot, theme);
            ApplyText(_radLabel, TextStyleRole.Cardinal, theme);
            ApplyText(_radValueText, TextStyleRole.PrimaryValue, theme);
            if (_radBarFillImg != null) _radBarFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || Config == null) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            float s = CurrentDpiScale;

            // 动态标题求值
            string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            SetTextIfChanged(_titleText, evalTitle);

            // 1. 无机动节点时优雅待机降级
            if (!telemetry.HasManeuverNode)
            {
                ShowStandby(theme, s);
                return;
            }

            _lastHasNode = true;

            // 2. 遥测数值双精度求值 (面向纯 C# 契约与通配符引擎)
            double dv = TelemetryTokenEngine.EvaluateNumeric(_deltaVToken, telemetry);
            if (double.IsNaN(dv)) dv = telemetry.ManeuverDeltaV;

            double totalDv = TelemetryTokenEngine.EvaluateNumeric(_totalDvToken, telemetry);
            if (double.IsNaN(totalDv) || totalDv < 0.01) totalDv = telemetry.ManeuverTotalDeltaV;
            if (totalDv < dv) totalDv = dv;

            double timeToNode = TelemetryTokenEngine.EvaluateNumeric(_tNodeToken, telemetry);
            if (double.IsNaN(timeToNode)) timeToNode = telemetry.ManeuverTimeToNode;

            double burnTime = TelemetryTokenEngine.EvaluateNumeric(_burnTimeToken, telemetry);
            if (double.IsNaN(burnTime)) burnTime = telemetry.ManeuverBurnTime;

            double timeToBurn = TelemetryTokenEngine.EvaluateNumeric(_timeToBurnToken, telemetry);
            if (double.IsNaN(timeToBurn)) timeToBurn = telemetry.ManeuverTimeToBurn;

            // 三轴机动速度矢量求值
            double proDv = TelemetryTokenEngine.EvaluateNumeric(_proToken, telemetry);
            if (double.IsNaN(proDv)) proDv = telemetry.ManeuverDeltaVPrograde;

            double normDv = TelemetryTokenEngine.EvaluateNumeric(_normToken, telemetry);
            if (double.IsNaN(normDv)) normDv = telemetry.ManeuverDeltaVNormal;

            double radDv = TelemetryTokenEngine.EvaluateNumeric(_radToken, telemetry);
            if (double.IsNaN(radDv)) radDv = telemetry.ManeuverDeltaVRadial;

            // 来源与状态标识
            string srcStr = TelemetryTokenEngine.Evaluate(_sourceToken, telemetry);
            if (string.IsNullOrEmpty(srcStr) || srcStr.StartsWith("{")) srcStr = telemetry.ManeuverSource ?? "MANEUVER";
            string formattedSrc = $"[{srcStr}]";
            if (formattedSrc != _lastSourceStr)
            {
                _lastSourceStr = formattedSrc;
                SetTextIfChanged(_sourceBadgeText, formattedSrc);
            }

            // 3. 状态研判与主题高亮模式
            CardStyleRole targetRole = CardStyleRole.Normal;
            string statusStr = "ARMED";

            if (timeToBurn <= 0.0 && dv > 0.1)
            {
                targetRole = CardStyleRole.Emphasized;
                statusStr = "BURNING";
            }
            else if (dv <= 0.1)
            {
                targetRole = CardStyleRole.Normal;
                statusStr = "COMPLETE";
            }
            else if (timeToBurn <= 15.0)
            {
                statusStr = "COUNTDOWN";
            }

            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                ApplyCard(_bgImage, _bgOutline, targetRole, theme);
                ApplyText(_deltaVText, TextStyleRole.PrimaryValue, theme);
                ApplyText(_statusBadgeText, targetRole == CardStyleRole.Emphasized ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);
            }

            if (statusStr != _lastStatusStr)
            {
                _lastStatusStr = statusStr;
                SetTextIfChanged(_statusBadgeText, statusStr);
            }

            // 4. 主读数 ΔV 脏检查
            double deltaThreshold = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (double.IsNaN(_lastDeltaV) || Math.Abs(dv - _lastDeltaV) > deltaThreshold)
            {
                _lastDeltaV = dv;
                SetTextIfChanged(_deltaVText, $"{dv:F1}");
            }

            // 5. 时间轴推进光标与填充条计算
            UpdateTimelineProgress(timeToBurn, timeToNode, burnTime, dv, totalDv, s);

            // 6. 三轴分量卡片更新
            UpdateVectorColumns(proDv, normDv, radDv, totalDv, deltaThreshold, s);

            // 7. 交互按键状态
            if (_btnWarp != null && !_btnWarp.interactable) _btnWarp.interactable = true;
            if (_btnDismiss != null && !_btnDismiss.interactable) _btnDismiss.interactable = true;
        }

        private void UpdateTimelineProgress(double timeToBurn, double timeToNode, double burnTime, double dv, double totalDv, float s)
        {
            float trackW = TrackWidthLogical * s;

            // 归一化光标计算：
            // - 当 timeToBurn > 0: 进场阶段，从 0.05 渐进至 ZoneIgnitionNorm (0.44)
            // - 当 timeToBurn <= 0 && dv > 0.1: 点火阶段，从 ZoneIgnitionNorm (0.44) 推进至 ZoneBurnoutNorm (0.84)
            // - 当 dv <= 0.1: 完成阶段，停留在 ZoneBurnoutNorm 之后 (0.92)
            float pipNorm;
            if (timeToBurn > 0.0)
            {
                // 进场倒计时：以 120 秒为进场视窗
                float approachRatio = Mathf.Clamp01(1.0f - (float)(timeToBurn / Math.Max(timeToBurn + 30.0, 120.0)));
                pipNorm = Mathf.Lerp(0.04f, ZoneIgnitionNorm, approachRatio);
            }
            else if (dv > 0.1)
            {
                // 点火中：依据剩余 dV 进度推进 (0.44 -> 0.84)
                float burnProgress = totalDv > 0.01 ? Mathf.Clamp01(1.0f - (float)(dv / totalDv)) : 0.5f;
                pipNorm = Mathf.Lerp(ZoneIgnitionNorm, ZoneBurnoutNorm, burnProgress);
            }
            else
            {
                // 变轨圆满完成
                pipNorm = 0.92f;
            }

            if (Mathf.Abs(pipNorm - _lastPipNormalized) > 0.002f)
            {
                _lastPipNormalized = pipNorm;
                if (_cursorPipRt != null)
                {
                    _cursorPipRt.anchoredPosition = new Vector2(-trackW * 0.5f + pipNorm * trackW, 0f);
                }

                // 点火消耗填充条 (从 ZoneIgnitionNorm 向右延伸)
                if (_burnFillRt != null)
                {
                    if (pipNorm > ZoneIgnitionNorm)
                    {
                        float fillW = Mathf.Min(pipNorm, ZoneBurnoutNorm) - ZoneIgnitionNorm;
                        _burnFillRt.sizeDelta = new Vector2(fillW * trackW, _burnFillRt.sizeDelta.y);
                    }
                    else
                    {
                        _burnFillRt.sizeDelta = new Vector2(0f, _burnFillRt.sizeDelta.y);
                    }
                }
            }

            // 更新时间轴右侧信息文案
            string infoStr;
            if (timeToBurn <= 0.0 && dv > 0.1)
            {
                infoStr = $"BURNING | REM {dv:F1} m/s";
            }
            else if (dv <= 0.1)
            {
                infoStr = "BURNOUT NOMINAL";
            }
            else
            {
                string tStr = timeToNode < 0 ? "T+" : "T-";
                infoStr = $"{tStr}{FormatDuration(Math.Abs(timeToNode))} | BURN {burnTime:F0}s";
            }

            if (infoStr != _lastTimelineInfoStr)
            {
                _lastTimelineInfoStr = infoStr;
                SetTextIfChanged(_timelineInfoText, infoStr);
            }
        }

        private void UpdateVectorColumns(double pro, double norm, double rad, double totalDv, double deltaThreshold, float s)
        {
            double refScale = Math.Max(totalDv, 50.0);

            // 1. Prograde / Retrograde
            if (double.IsNaN(_lastPrograde) || Math.Abs(pro - _lastPrograde) > deltaThreshold)
            {
                _lastPrograde = pro;
                string sign = pro >= 0.0 ? "+" : "";
                SetTextIfChanged(_proValueText, $"{sign}{pro:F1} m/s");
                SetTextIfChanged(_proLabel, pro >= 0.0 ? "PROGRADE" : "RETROGRADE");
                UpdateBilateralMeter(_proBarFillRt, (float)(pro / refScale), 146f * s);
            }

            // 2. Normal / Antinormal
            if (double.IsNaN(_lastNormal) || Math.Abs(norm - _lastNormal) > deltaThreshold)
            {
                _lastNormal = norm;
                string sign = norm >= 0.0 ? "+" : "";
                SetTextIfChanged(_normValueText, $"{sign}{norm:F1} m/s");
                SetTextIfChanged(_normLabel, norm >= 0.0 ? "NORMAL" : "ANTINORMAL");
                UpdateBilateralMeter(_normBarFillRt, (float)(norm / refScale), 146f * s);
            }

            // 3. Radial Out / Radial In
            if (double.IsNaN(_lastRadial) || Math.Abs(rad - _lastRadial) > deltaThreshold)
            {
                _lastRadial = rad;
                string sign = rad >= 0.0 ? "+" : "";
                SetTextIfChanged(_radValueText, $"{sign}{rad:F1} m/s");
                SetTextIfChanged(_radLabel, rad >= 0.0 ? "RAD OUT" : "RAD IN");
                UpdateBilateralMeter(_radBarFillRt, (float)(rad / refScale), 146f * s);
            }
        }

        private static void UpdateBilateralMeter(RectTransform fillRt, float normalizedValue, float maxBarW)
        {
            if (fillRt == null) return;
            float clamped = Mathf.Clamp(normalizedValue, -1f, 1f);
            float halfW = maxBarW * 0.5f;
            float fillLen = Mathf.Abs(clamped) * halfW;
            float fillCenter = clamped * halfW * 0.5f;

            fillRt.sizeDelta = new Vector2(fillLen, fillRt.sizeDelta.y);
            fillRt.anchoredPosition = new Vector2(fillCenter, 0f);
        }

        private void ShowStandby(ThemeConfig theme, float s)
        {
            if (!_lastHasNode && _lastDeltaV == 0.0) return;

            _lastHasNode = false;
            _lastDeltaV = 0.0;
            _lastTotalDv = 0.0;
            _lastTimeToNode = double.NaN;
            _lastBurnTime = double.NaN;
            _lastTimeToBurn = double.NaN;
            _lastPrograde = double.NaN;
            _lastNormal = double.NaN;
            _lastRadial = double.NaN;
            _lastPipNormalized = -1f;

            SetTextIfChanged(_deltaVText, "---");
            SetTextIfChanged(_sourceBadgeText, "[STANDBY]");
            SetTextIfChanged(_statusBadgeText, "STANDBY");
            SetTextIfChanged(_timelineInfoText, "NO ACTIVE NODE");

            SetTextIfChanged(_proValueText, "---");
            SetTextIfChanged(_normValueText, "---");
            SetTextIfChanged(_radValueText, "---");

            float trackW = TrackWidthLogical * s;
            if (_cursorPipRt != null) _cursorPipRt.anchoredPosition = new Vector2(-trackW * 0.5f, 0f);
            if (_burnFillRt != null) _burnFillRt.sizeDelta = new Vector2(0f, _burnFillRt.sizeDelta.y);

            UpdateBilateralMeter(_proBarFillRt, 0f, 146f * s);
            UpdateBilateralMeter(_normBarFillRt, 0f, 146f * s);
            UpdateBilateralMeter(_radBarFillRt, 0f, 146f * s);

            if (_currentCardRole != CardStyleRole.Normal)
            {
                _currentCardRole = CardStyleRole.Normal;
                ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
                ApplyText(_deltaVText, TextStyleRole.PrimaryValue, theme);
                ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            }

            if (_btnWarp != null && _btnWarp.interactable) _btnWarp.interactable = false;
            if (_btnDismiss != null && _btnDismiss.interactable) _btnDismiss.interactable = false;
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "00:00";
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1.0)
            {
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void OnWarpClicked()
        {
            FlightTelemetryContext.Current?.WarpToManeuverNode();
        }

        private void OnDismissClicked()
        {
            FlightTelemetryContext.Current?.DeleteManeuverNode();
        }

        protected override void OnDestroy()
        {
            if (_btnWarp != null)
            {
                _btnWarp.onClick.RemoveListener(OnWarpClicked);
                _btnWarp = null;
            }
            if (_btnDismiss != null)
            {
                _btnDismiss.onClick.RemoveListener(OnDismissClicked);
                _btnDismiss = null;
            }
            base.OnDestroy();
        }
    }
}
