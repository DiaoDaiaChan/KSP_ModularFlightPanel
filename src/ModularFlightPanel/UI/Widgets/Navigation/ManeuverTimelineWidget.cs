using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.Navigation
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 极简直线时序机动指示器 (SpaceX Webcast Maneuver Timeline)
    /// ====================================================================================
    /// 核心设计语言 (SpaceX 直播直线时序 HUD 风格)：
    /// 1. 顶部极简水平时间轴 (Straight Horizontal Timeline & Milestones)：
    ///    直观呈现进场 (APPROACH)、点火 (IGNITION)、节点 (T0 NODE) 与关机 (BURNOUT) 4 大关键节点；
    ///    采用极简直线轨道与动态燃烧窗口标线，飞行光标沿直线平滑推进，经过节点自动点亮激活。
    /// 2. 中央大字号核心时钟/ΔV (Hero Digital Center)：
    ///    进场时展示倒计时与总 ΔV (如 "T- 05:20    320.0 m/s")；点火时高亮当前燃烧状态与实时剩余量。
    /// 3. 底部单行三向矢量遥测标牌 (Inline 3-Axis Vector Telemetry Subtitle)：
    ///    以单行极简 HUD 形式展示 Principia / 原版三向速度增量 (PRO / NRM / RAD) 与遥测源标识。
    /// 4. 极致精简与通透悬浮：
    ///    去除非必要的多层方框、按钮与冗余图元，默认采用淡微光悬浮风格 (FRAME=FAINT)。
    /// 5. 严格遵守 MFP 规范：
    ///    0 颜色字面量 (MFP-SPEC-006)、0 场景查询 (MFP-SPEC-007)、纯 C# IFlightTelemetry 解耦。
    /// </summary>
    [FlightWidget("maneuver_timeline", "burn_timeline", Category = WidgetCategory.Navigation, DisplayName = "MANEUVER 轨道机动时序与三轴矢量轴", Description = "横排时间轴形式机动节点指示器：点火窗口时序轨、T0 节点与 Prograde/Normal/Radial 三轴矢量分解。", DefaultWidgetId = "custom.maneuver_timeline", DefaultX = 0f, DefaultY = 260f, IsSingleton = true, ExactIds = new[] { "custom.maneuver_timeline", "core.maneuver_timeline" })]
    public class ManeuverTimelineWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(520f, 100f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 注意: 本组件不使用声明式 DSL 微控件 (TextWidget.Value/Unit)，
        // 时间轴 Hero 与 Subtitle 由 OnInitialize 手动构建以支持多段异构布局。

        // UI 背景与卡片
        private Image _bgImage;
        private Outline _bgOutline;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // 直线时间轴轨道与节点
        private Image _trackLineImage;
        private Image _burnZoneImage;
        private RectTransform _progressPipRt;
        private Image _progressPipImage;

        private struct MilestoneUI
        {
            public Text Label;
            public Image Dot;
            public float NormalizedX;
        }
        private MilestoneUI[] _milestones;

        // 中央核心读数 — 分体式倒计时与 ΔV 双栏
        private Text _countdownLabel;       // 左栏小标: COUNTDOWN
        private Text _countdownText;        // 左栏: T- 05:20
        private Text _deltaVLabel;          // 右栏小标: Δv REMAINING
        private Text _deltaVText;           // 右栏: 320.0 m/s
        private Image _heroDivider;         // 中央竖向分隔线

        // 底部单行三轴矢量副标牌 ([PRINCIPIA] PRO +310.0 · NRM +75.0 · RAD -25.0 m/s)
        private Text _vectorSubtitleText;

        // 缓存与脏检查标记
        private ThemeConfig _cachedTheme;
        private bool _lastHasNode = false;
        private double _lastDeltaV = double.NaN;
        private double _lastPrograde = double.NaN;
        private double _lastNormal = double.NaN;
        private double _lastRadial = double.NaN;
        private float _lastCachedPipProgress = -1f;
        private string _lastHeroStr = string.Empty;
        private string _lastSubtitleStr = string.Empty;

        // 几何参数 (基准像素)
        private const float TrackWidth = 460f;
        private const float TrackCenterY = 32f;
        private const float ZoneIgnitionNorm = 0.38f;
        private const float ZoneBurnoutNorm = 0.88f;

        // 风格配置 (FAINT: 极细淡边框[默认], NONE: 完全无框, NORMAL: 传统卡片)
        private string _frameMode = "FAINT";

        // 通配符通道与可覆盖模板
        private string _deltaVToken = "{MN:DV}";
        private string _totalDvToken = "{MN:TOTAL_DV}";
        private string _tNodeToken = "{MN:T_NODE}";
        private string _burnTimeToken = "{MN:BURN_TIME}";
        private string _timeToBurnToken = "{MN:T_BURN}";
        private string _proToken = "{MN:PRO}";
        private string _normToken = "{MN:NORM}";
        private string _radToken = "{MN:RAD}";
        private string _sourceToken = "{MN:SOURCE}";
        private string _statusToken = "{MN:STATUS}";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            string frame = GetTemplateChannel("FRAME", null);
            _frameMode = !string.IsNullOrEmpty(frame) ? frame.ToUpperInvariant() : "FAINT";
            _deltaVToken = GetTemplateChannel(new[] { "DV_TOKEN", "DELTA_V_TOKEN" }, "{MN:DV}");
            _totalDvToken = GetTemplateChannel("TOTAL_DV_TOKEN", "{MN:TOTAL_DV}");
            _tNodeToken = GetTemplateChannel("T_NODE_TOKEN", "{MN:T_NODE}");
            _burnTimeToken = GetTemplateChannel("BURN_TIME_TOKEN", "{MN:BURN_TIME}");
            _timeToBurnToken = GetTemplateChannel("T_BURN_TOKEN", "{MN:T_BURN}");
            _proToken = GetTemplateChannel("PRO_TOKEN", "{MN:PRO}");
            _normToken = GetTemplateChannel("NORM_TOKEN", "{MN:NORM}");
            _radToken = GetTemplateChannel("RAD_TOKEN", "{MN:RAD}");
            _sourceToken = GetTemplateChannel("SOURCE_TOKEN", "{MN:SOURCE}");
            _statusToken = GetTemplateChannel("STATUS_TOKEN", "{MN:STATUS}");

            // 1. 组件包围盒 (基准 520×100 逻辑像素，三级分层布局)
            Vector2 size = BaseSize * s;
            RectTransform.sizeDelta = size;

            // 2. 底板卡片 (由基类 AutoCreateCardFrame 托管)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null) _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // ═══════════════════════════════════════════════════════
            // TIER 1 (上层): 水平直线时间轴轨道
            // ═══════════════════════════════════════════════════════

            _trackLineImage = CreateChild<Image>("Timeline_Track", transform,
                new Vector2(TrackWidth * s, 2f * s), new Vector2(0f, TrackCenterY * s));
            _trackLineImage.raycastTarget = false;

            // 燃烧窗口高亮段 (IGNITION -> BURNOUT)
            float burnZoneW = (ZoneBurnoutNorm - ZoneIgnitionNorm) * TrackWidth * s;
            float burnZoneCenterX = ((ZoneIgnitionNorm + ZoneBurnoutNorm) * 0.5f - 0.5f) * TrackWidth * s;
            _burnZoneImage = CreateChild<Image>("Burn_Zone_Track", transform,
                new Vector2(burnZoneW, 3f * s), new Vector2(burnZoneCenterX, TrackCenterY * s));
            _burnZoneImage.raycastTarget = false;

            // 关键任务时序节点 (APPROACH, IGNITION, T0 NODE, BURNOUT)
            BuildMilestones(s, theme);

            // 动态飞行光标 (Progress Pip)
            _progressPipImage = CreateChild<Image>("Progress_Pip", transform,
                new Vector2(6f * s, 8f * s), new Vector2(-TrackWidth * 0.5f * s, TrackCenterY * s));
            _progressPipImage.raycastTarget = false;
            _progressPipRt = _progressPipImage.rectTransform;

            // ═══════════════════════════════════════════════════════
            // TIER 2 (中层): 分体式双栏核心读数
            //   ┌─────────────┬──────────────┐
            //   │  COUNTDOWN  │ Δv REMAINING │  ← 小标签 (y=+14)
            //   │  T- 05:20   │  320.0 m/s   │  ← 大字读数 (y=-2)
            //   └─────────────┴──────────────┘
            // ═══════════════════════════════════════════════════════
            float labelY = 14f * s;
            float heroY = -2f * s;
            float colW = (TrackWidth * 0.5f - 20f) * s; // 每栏宽度 (留中央间距)
            float leftCX = -colW * 0.5f - 10f * s;      // 左栏中心 X
            float rightCX = colW * 0.5f + 10f * s;      // 右栏中心 X

            // 左栏小标签: COUNTDOWN
            _countdownLabel = UIFactory.CreateText(transform, "Countdown_Label", I18n.Tr("WIDGET_NAV_COUNTDOWN", "倒计时"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.Label, theme));
            _countdownLabel.fontStyle = FontStyle.Bold;
            RectTransform clRt = _countdownLabel.rectTransform;
            clRt.sizeDelta = new Vector2(colW, 12f * s);
            clRt.anchoredPosition = new Vector2(leftCX, labelY);

            // 左栏大字读数: T- 05:20
            _countdownText = UIFactory.CreateText(transform, "Countdown_Readout", I18n.Tr("WIDGET_NAV_T_COUNTDOWN_PLACEHOLDER", "T- --:--"), Mathf.RoundToInt(20f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _countdownText.fontStyle = FontStyle.Bold;
            RectTransform cntRt = _countdownText.rectTransform;
            cntRt.sizeDelta = new Vector2(colW, 26f * s);
            cntRt.anchoredPosition = new Vector2(leftCX, heroY);

            // 中央竖向分隔线 (2px 宽, 高度贯穿标签与读数两行)
            _heroDivider = CreateChild<Image>("Hero_Divider", transform,
                new Vector2(1.5f * s, 28f * s), new Vector2(0f, 6f * s));
            _heroDivider.raycastTarget = false;

            // 右栏小标签: Δv REMAINING
            _deltaVLabel = UIFactory.CreateText(transform, "DeltaV_Label", I18n.Tr("WIDGET_NAV_DV_REMAINING", "剩余 \u0394v"), Mathf.RoundToInt(7f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            _deltaVLabel.fontStyle = FontStyle.Bold;
            RectTransform dlRt = _deltaVLabel.rectTransform;
            dlRt.sizeDelta = new Vector2(colW, 12f * s);
            dlRt.anchoredPosition = new Vector2(rightCX, labelY);

            // 右栏大字读数: 320.0 m/s
            _deltaVText = UIFactory.CreateText(transform, "DeltaV_Readout", "--- m/s", Mathf.RoundToInt(20f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _deltaVText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = _deltaVText.rectTransform;
            dvRt.sizeDelta = new Vector2(colW, 26f * s);
            dvRt.anchoredPosition = new Vector2(rightCX, heroY);

            // ═══════════════════════════════════════════════════════
            // TIER 3 (下层): 单行三轴矢量遥测标牌 (y = -26)
            // ═══════════════════════════════════════════════════════
            _vectorSubtitleText = UIFactory.CreateText(transform, "Vector_Subtitle", I18n.Tr("PHASE_STANDBY", "待机"), Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _vectorSubtitleText.fontStyle = FontStyle.Bold;
            RectTransform subRt = _vectorSubtitleText.rectTransform;
            subRt.sizeDelta = new Vector2(480f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(0f, -26f * s);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => {
                if (_frameMode == "NONE")
                {
                    if (_bgImage != null) _bgImage.color = Color.clear;
                    if (_bgOutline != null) _bgOutline.enabled = false;
                }
                else if (_frameMode == "FAINT")
                {
                    if (_bgImage != null) _bgImage.color = Color.clear;
                    if (_bgOutline != null)
                    {
                        _bgOutline.enabled = true;
                        _bgOutline.effectColor = WidgetStyleManager.Weighted(t.FrameBorderColor, LineWeight.Hairline);
                    }
                }
                else
                {
                    ApplyCard(_bgImage, _bgOutline, _currentCardRole, t);
                }
            }));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "timeline_track", "时序轨道", _trackLineImage != null ? _trackLineImage.gameObject : null, (t) => {
                if (_trackLineImage != null) _trackLineImage.color = WidgetStyleManager.Weighted(t.AccentSecondary, LineWeight.Medium);
                if (_burnZoneImage != null) _burnZoneImage.color = WidgetStyleManager.Weighted(t.AccentPrimary, LineWeight.Light);
            }));
            this.Controls.Register(new WidgetReadoutControl("countdown_readout", "倒计时读数", _countdownText != null ? _countdownText.gameObject : null, _countdownText, null, TextStyleRole.PrimaryValue, _tNodeToken));
            this.Controls.Register(new WidgetReadoutControl("deltav_readout", "ΔV 读数", _deltaVText != null ? _deltaVText.gameObject : null, _deltaVText, null, TextStyleRole.PrimaryValue, _deltaVToken));
            this.Controls.Register(new WidgetReadoutControl("vector_subtitle", "三轴矢量副标牌", _vectorSubtitleText != null ? _vectorSubtitleText.gameObject : null, _vectorSubtitleText, null, TextStyleRole.SecondaryValue, _proToken));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildMilestones(float s, ThemeConfig theme)
        {
            var defs = new (string text, float normX)[]
            {
                ("APPROACH", 0.10f),
                ("IGNITION", ZoneIgnitionNorm),
                ("T0 NODE", 0.64f),
                ("BURNOUT", ZoneBurnoutNorm)
            };

            _milestones = new MilestoneUI[defs.Length];
            Color dotColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Bold);
            Color textColor = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, theme);

            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float xPos = (def.normX - 0.5f) * TrackWidth * s;
                float yPos = TrackCenterY * s;

                // 节点标记竖向微刻度 (Tick)
                Image dotImg = CreateChild<Image>($"Milestone_Tick_{i}", transform,
                    new Vector2(2f * s, 6f * s), new Vector2(xPos, yPos));
                dotImg.color = dotColor;
                dotImg.raycastTarget = false;

                // 节点文字标签 (统一置于直线轨道上方，整齐划一)
                float textY = yPos + 9f * s;
                Text label = UIFactory.CreateText(transform, $"Milestone_Lbl_{i}", def.text, Mathf.RoundToInt(6.5f * s),
                    TextAnchor.LowerCenter, textColor);
                label.fontStyle = FontStyle.Bold;
                RectTransform lblRt = label.rectTransform;
                lblRt.sizeDelta = new Vector2(90f * s, 12f * s);
                lblRt.anchoredPosition = new Vector2(xPos, textY);

                _milestones[i] = new MilestoneUI
                {
                    Dot = dotImg,
                    Label = label,
                    NormalizedX = def.normX
                };
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 外框模式着色
            if (_frameMode == "NONE")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null) _bgOutline.enabled = false;
            }
            else if (_frameMode == "FAINT")
            {
                if (_bgImage != null) _bgImage.color = Color.clear;
                if (_bgOutline != null)
                {
                    _bgOutline.enabled = true;
                    _bgOutline.effectColor = _currentCardRole == CardStyleRole.Emphasized
                        ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Medium)
                        : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
            }
            else
            {
                ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);
            }

            // 直线时间轴轨道着色
            if (_trackLineImage != null)
            {
                _trackLineImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
            }
            if (_burnZoneImage != null)
            {
                _burnZoneImage.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Light);
            }

            // 飞行光标着色
            if (_progressPipImage != null)
            {
                _progressPipImage.color = theme.AccentPrimary;
            }

            // 节点着色
            if (_milestones != null)
            {
                Color dotColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Bold);
                for (int i = 0; i < _milestones.Length; i++)
                {
                    if (_milestones[i].Dot != null) _milestones[i].Dot.color = dotColor;
                    if (_milestones[i].Label != null) ApplyText(_milestones[i].Label, TextStyleRole.Label, theme);
                }
            }

            // 核心读数双栏标签、数值与分隔线
            ApplyText(_countdownLabel, TextStyleRole.Label, theme);
            ApplyText(_countdownText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_deltaVLabel, TextStyleRole.Label, theme);
            ApplyText(_deltaVText, TextStyleRole.PrimaryValue, theme);
            if (_heroDivider != null)
            {
                _heroDivider.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
            }
            ApplyText(_vectorSubtitleText, TextStyleRole.SecondaryValue, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);
            float s = CurrentDpiScale;

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

            // 3. 状态研判与高亮模式切换
            CardStyleRole targetRole = CardStyleRole.Normal;
            if (timeToBurn <= 0.0 && dv > 0.1)
            {
                targetRole = CardStyleRole.Emphasized;
            }

            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                if (_frameMode == "FAINT" && _bgOutline != null)
                {
                    _bgOutline.effectColor = targetRole == CardStyleRole.Emphasized
                        ? WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Medium)
                        : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                }
                else if (_frameMode == "NORMAL")
                {
                    ApplyCard(_bgImage, _bgOutline, targetRole, theme);
                }
            }

            // 4. 计算归一化飞行光标位置 (pipProgress)
            float pipProgress;
            if (timeToBurn > 0.0)
            {
                // 进场阶段：依据倒计时从 0.08 推进至 IGNITION 点 (0.38)
                float approachRatio = Mathf.Clamp01(1.0f - (float)(timeToBurn / Math.Max(timeToBurn + 30.0, 120.0)));
                pipProgress = Mathf.Lerp(0.08f, ZoneIgnitionNorm, approachRatio);
            }
            else if (dv > 0.1)
            {
                // 点火阶段：从 IGNITION (0.38) 推进至 BURNOUT (0.88)
                float burnProgress = totalDv > 0.01 ? Mathf.Clamp01(1.0f - (float)(dv / totalDv)) : 0.5f;
                pipProgress = Mathf.Lerp(ZoneIgnitionNorm, ZoneBurnoutNorm, burnProgress);
            }
            else
            {
                // 关机完成阶段
                pipProgress = 0.90f;
            }

            // 更新光标水平几何位置与节点经过高亮
            if (Mathf.Abs(pipProgress - _lastCachedPipProgress) > 0.002f)
            {
                _lastCachedPipProgress = pipProgress;
                float pipX = (pipProgress - 0.5f) * TrackWidth * s;
                if (_progressPipRt != null)
                {
                    _progressPipRt.anchoredPosition = new Vector2(pipX, TrackCenterY * s);
                }

                // 更新里程碑点亮状态 (已通过节点高亮)
                if (_milestones != null)
                {
                    Color activeDot = theme.AccentPrimary;
                    Color inactiveDot = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Bold);

                    for (int i = 0; i < _milestones.Length; i++)
                    {
                        bool passed = pipProgress >= _milestones[i].NormalizedX - 0.01f;
                        if (_milestones[i].Dot != null)
                        {
                            _milestones[i].Dot.color = passed ? activeDot : inactiveDot;
                        }
                        if (_milestones[i].Label != null)
                        {
                            ApplyText(_milestones[i].Label, passed ? TextStyleRole.PrimaryValue : TextStyleRole.Label, theme);
                        }
                    }
                }
            }

            // 5. 分体式双栏核心读数 — 左: 倒计时 / 右: ΔV
            string labelStr;
            string countdownStr;
            string dvStr;

            if (timeToBurn > 0.0)
            {
                // 进场模式
                labelStr = "COUNTDOWN";
                int totalSec = Mathf.Abs((int)timeToBurn);
                countdownStr = $"T- {totalSec / 60:00}:{totalSec % 60:00}";
                dvStr = $"{dv:F1} m/s";
            }
            else if (dv > 0.1)
            {
                // 正在燃烧
                labelStr = "BURN ELAPSED";
                int elapsed = Mathf.Abs((int)timeToBurn);
                countdownStr = $"T+ {elapsed / 60:00}:{elapsed % 60:00}";
                dvStr = $"{dv:F1} m/s";
            }
            else
            {
                // 关机完成
                labelStr = "STATUS";
                countdownStr = "COMPLETE";
                dvStr = "0.0 m/s";
            }

            if (countdownStr != _lastHeroStr)
            {
                _lastHeroStr = countdownStr;
                SetTextIfChanged(_countdownText, countdownStr);
                SetTextIfChanged(_countdownLabel, labelStr);
            }
            if (dvStr != _lastSubtitleStr || _lastDeltaV != dv)
            {
                SetTextIfChanged(_deltaVText, dvStr);
            }

            // 6. 底部单行三向矢量遥测标牌更新 ([PRINCIPIA] PRO +310.0 · NRM +75.0 · RAD -25.0 m/s)
            double deltaThreshold = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (double.IsNaN(_lastPrograde) || Math.Abs(proDv - _lastPrograde) > deltaThreshold ||
                Math.Abs(normDv - _lastNormal) > deltaThreshold || Math.Abs(radDv - _lastRadial) > deltaThreshold)
            {
                _lastPrograde = proDv;
                _lastNormal = normDv;
                _lastRadial = radDv;

                string proSign = proDv >= 0 ? "+" : "";
                string normSign = normDv >= 0 ? "+" : "";
                string radSign = radDv >= 0 ? "+" : "";

                string subStr = $"[{srcStr}]  PRO {proSign}{proDv:F1}  ·  NRM {normSign}{normDv:F1}  ·  RAD {radSign}{radDv:F1} m/s";
                if (dv <= 0.1)
                {
                    subStr = $"[{srcStr}]  NOMINAL BURNOUT  ·  ALL NODES EXECUTED";
                }

                if (subStr != _lastSubtitleStr)
                {
                    _lastSubtitleStr = subStr;
                    SetTextIfChanged(_vectorSubtitleText, subStr);
                }
            }
        }

        private void ShowStandby(ThemeConfig theme, float s)
        {
            if (!_lastHasNode && _lastDeltaV == 0.0) return;

            _lastHasNode = false;
            _lastDeltaV = 0.0;
            _lastPrograde = double.NaN;
            _lastNormal = double.NaN;
            _lastRadial = double.NaN;
            _lastCachedPipProgress = -1f;

            SetTextIfChanged(_countdownText, I18n.Tr("WIDGET_NAV_NO_NODE", "无节点"));
            SetTextIfChanged(_deltaVText, "--- m/s");
            SetTextIfChanged(_vectorSubtitleText, I18n.Tr("WIDGET_NAV_AWAITING_PLAN", "等待机动飞行计划"));

            if (_progressPipRt != null)
            {
                _progressPipRt.anchoredPosition = new Vector2(-TrackWidth * 0.5f * s, TrackCenterY * s);
            }

            if (_milestones != null)
            {
                Color inactiveDot = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Bold);
                for (int i = 0; i < _milestones.Length; i++)
                {
                    if (_milestones[i].Dot != null) _milestones[i].Dot.color = inactiveDot;
                    if (_milestones[i].Label != null) ApplyText(_milestones[i].Label, TextStyleRole.Label, theme);
                }
            }
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            _milestones = null;
            base.OnDestroy();
        }
    }
}
