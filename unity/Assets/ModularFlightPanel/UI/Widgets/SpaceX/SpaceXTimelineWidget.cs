using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 飞行时序与时钟只读状态快照 (零 GC 纯值类型，MFP-SPEC-012)
    /// </summary>
    public struct SpaceXTimelineState : IEquatable<SpaceXTimelineState>
    {
        public bool HasVessel;
        public double MissionTime;
        public float Progress;
        public string ClockText;
        public string PhaseText;

        public bool Equals(SpaceXTimelineState other)
        {
            return HasVessel == other.HasVessel
                && Math.Abs(MissionTime - other.MissionTime) < 0.2
                && Math.Abs(Progress - other.Progress) < 0.002f
                && string.Equals(ClockText, other.ClockText, StringComparison.Ordinal)
                && string.Equals(PhaseText, other.PhaseText, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is SpaceXTimelineState other && Equals(other);
        public override int GetHashCode() => HasVessel.GetHashCode();
    }

    /// <summary>
    /// SpaceX 飞行时序与任务时钟纯 C# 业务解耦逻辑 (MFP-SPEC-012)
    /// </summary>
    public class SpaceXTimelineLogic : WidgetLogic<SpaceXTimelineState>
    {
        private string _clockToken = "{MET:COMPACT}";
        private string _subtitleCustom;
        private string _displayName;

        public void Configure(string clockToken, string subtitleCustom, string displayName)
        {
            _clockToken = !string.IsNullOrEmpty(clockToken) ? clockToken : "{MET:COMPACT}";
            _subtitleCustom = subtitleCustom;
            _displayName = displayName;
        }

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            double missionTime = telemetry.MissionTime;
            string clockStr = TelemetryTokenEngine.Evaluate(_clockToken, telemetry);
            float progress = CalculateMissionProgress(telemetry);
            string phaseStr = ResolveMissionPhase(telemetry);

            CurrentState = new SpaceXTimelineState
            {
                HasVessel = true,
                MissionTime = missionTime,
                Progress = progress,
                ClockText = clockStr ?? "T+ 00:00:00",
                PhaseText = phaseStr ?? "STARSHIP FLIGHT TEST"
            };
        }

        public float CalculateMissionProgress(IFlightTelemetry telem)
        {
            if (telem == null) return 0f;
            if (telem.FlightSituation == "PRELAUNCH")
            {
                return 0.08f;
            }
            if (telem.FlightSituation == "LANDED" || telem.FlightSituation == "SPLASHED")
            {
                return 0.95f;
            }

            double atmDepth = telem.HasAtmosphere ? telem.AtmosphereDepth : 10000.0;
            double alt = telem.AltitudeASL;

            // 1. 已入轨 (近地点脱离大气且非逃逸)：使用真近点角 (True Anomaly) 精准指示环绕轨位
            if (telem.Periapsis > atmDepth && telem.Eccentricity < 1.0)
            {
                float trueAnomalyFrac = Mathf.Repeat((float)(telem.TrueAnomaly / 360.0), 1f);
                return Mathf.Lerp(0.76f, 0.96f, trueAnomalyFrac);
            }

            // 2. 爬升与入轨加速阶段
            if (alt < atmDepth * 0.25)
            {
                // 地面起飞至 Max-Q (0.08 .. 0.28)
                float t = Mathf.Clamp01((float)(alt / (atmDepth * 0.25)));
                return Mathf.Lerp(0.08f, 0.28f, t);
            }

            if (alt < atmDepth * 0.85 && telem.CurrentStage >= 2)
            {
                // 一级飞行至分级点 (0.28 .. 0.44)
                float t = Mathf.Clamp01((float)((alt - atmDepth * 0.25) / (atmDepth * 0.60)));
                return Mathf.Lerp(0.28f, 0.44f, t);
            }

            // 二级飞向入轨点 (0.44 .. 0.76)
            double targetOrbitalSpeed = Math.Max(telem.OrbitalSpeed, 1200.0);
            float speedFrac = Mathf.Clamp01((float)(telem.CurrentSpeed / targetOrbitalSpeed));
            return Mathf.Lerp(0.44f, 0.76f, speedFrac);
        }

        public string ResolveMissionPhase(IFlightTelemetry telem)
        {
            if (!string.IsNullOrEmpty(_subtitleCustom))
            {
                return _subtitleCustom;
            }

            if (!string.IsNullOrEmpty(_displayName) && _displayName != "TIMELINE")
            {
                return _displayName.ToUpperInvariant();
            }

            if (telem == null) return I18n.Tr("WIDGET_SPX_STARSHIP_FLIGHT_TEST", "星舰飞行试验");

            string sit = telem.FlightSituation;
            if (sit == "PRELAUNCH") return I18n.Tr("WIDGET_SPX_MSG_PAD_STANDBY", "发射台待命 / 倒计时");
            if (sit == "LANDED" || sit == "SPLASHED") return I18n.Tr("WIDGET_SPX_MSG_TOUCHDOWN_NOMINAL", "触地正常");

            if (telem.IsStageSeparating) return I18n.Tr("WIDGET_SPX_MSG_STAGE_SEPARATION_OCCURRED", "级间分离完成");
            if (telem.IsEngineIgniting) return I18n.Tr("WIDGET_SPX_MSG_IGNITION_SEQUENCE_START", "点火程序启动");

            double atmDepth = telem.HasAtmosphere ? telem.AtmosphereDepth : 0.0;

            if (telem.HasAtmosphere && telem.AltitudeASL < atmDepth)
            {
                if (telem.VerticalSpeed < -50.0) return I18n.Tr("WIDGET_SPX_MSG_ATMOSPHERIC_ENTRY", "大气再入段");
                if (telem.DynamicPressure > 25.0) return I18n.Tr("WIDGET_SPX_MSG_MAXQ", "最大动压");
                if (telem.VerticalSpeed > 10.0 && telem.CurrentStage >= 2) return I18n.Tr("WIDGET_SPX_MSG_SUPER_HEAVY_ASCENT", "超重助推动力爬升");
            }

            if (telem.CurrentStage <= 1 && telem.Throttle > 0.05f)
            {
                return I18n.Tr("WIDGET_SPX_MSG_STARSHIP_SECOND_BURN", "星舰二级点火");
            }

            if (telem.Periapsis > atmDepth && telem.Eccentricity < 1.0)
            {
                return telem.Throttle <= 0.01f ? I18n.Tr("WIDGET_SPX_MSG_ORBITAL_INSERTION_COAST", "入轨 / 滑行") : I18n.Tr("WIDGET_SPX_MSG_ORBITAL_MANEUVER_BURN", "轨道机动点火");
            }

            return I18n.Tr("WIDGET_SPX_STARSHIP_FLIGHT_TEST", "星舰飞行试验");
        }
    }

    /// <summary>
    /// SpaceX 星舰任务序列时序与时钟组件 (SpaceX Webcast Timeline & Mission Clock)
    /// 包含三大核心要素：
    ///   1. 任务序列 (Mission Sequence)：贯穿顶部的抛物线轨迹弧、关键任务节点 (LIFTOFF, MAX-Q, STAGE SEP, LANDING, SECO) 与动态飞行光标
    ///   2. 任务时间 (Mission Time)：中央超大现代等宽数字时钟 (T+ 00:08:03 / T- 00:00:10)
    ///   3. 任务阶段 (Mission Phase)：时钟下方副标题标牌 (STARSHIP FLIGHT TEST / 动态分级飞行情景)
    /// 严格遵循 MFP 架构规范：零 CPU 软件光栅化 (MFP-SPEC-002)、零硬编码颜色 (MFP-SPEC-006)、业务解耦大脑 (MFP-SPEC-012)。
    /// </summary>
    [FlightWidget("spacex_timeline", "dragon_timeline", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 飞行关键时序甘特轴", Description = "横排甘特式任务阶段进度标尺：MECO、分级、入轨、对接窗口各节点动态光标推进。", DefaultWidgetId = "spacex.timeline", DefaultX = 0f, DefaultY = 320f, IsSingleton = true, ExactIds = new[] { "spacex.timeline" })]
    public class SpaceXTimelineWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(520f, 88f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        private const string MilestoneLiftoff = "LIFTOFF";
        private const string MilestoneMaxQ = "MAX-Q";
        private const string MilestoneStageSep = "STAGE SEP";
        private const string MilestoneLanding = "SUPER HEAVY LANDING";
        private const string MilestoneSeco = "SECO";
        private const string MilestoneOrbit = "ORBIT";

        // 声明式微控件
        public TextWidget Clock = TextWidget.Value(I18n.Tr("WIDGET_SPX_CLOCK_PLACEHOLDER", "T+ 00:00:00"));
        public TextWidget Phase = TextWidget.Unit(I18n.Tr("WIDGET_SPX_STARSHIP_FLIGHT_TEST", "星舰飞行试验"));

        // 业务解耦大脑 (MFP-SPEC-012)
        private readonly SpaceXTimelineLogic _logic = new SpaceXTimelineLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        // GPU 矢量抛物弧与节点
        private ProceduralTimelineArcImage _arcImage;
        private RectTransform _progressPipRt;
        private Image _progressPipImage;

        private struct MilestoneUI
        {
            public Text Label;
            public Image Dot;
            public float NormalizedX; // 0..1
        }
        private MilestoneUI[] _milestones;

        // 任务时钟与阶段
        private Text _missionClockText;
        private Text _missionPhaseText;

        // 缓存与脏标记 (MFP-SPEC-009)
        private readonly CachedDouble _lastCachedMissionTime = new CachedDouble(double.NaN);
        private readonly CachedFloat _lastCachedPipProgress = new CachedFloat(-1f);
        private readonly Cached<string> _lastPhaseStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastClockStr = new Cached<string>(string.Empty);
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // CustomTemplate 自定义通道
        private string _clockToken = "{MET:COMPACT}";
        private string _subtitleCustom;

        // 几何参数 (基准像素)
        private const float ArcWidth = 460f;
        private const float ArcHeight = 24f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _clockToken = GetTemplateChannel("CLOCK_TOKEN", "{MET:COMPACT}");
            _subtitleCustom = GetTemplateChannel("SUBTITLE", null);

            _logic.Configure(_clockToken, _subtitleCustom, config?.DisplayName);

            // 1. 组件包围盒 (基准 520x88 逻辑像素)
            Vector2 size = new Vector2(520f * s, 88f * s);
            RectTransform.sizeDelta = size;

            // 2. 半透卡片底板 (全息透明 HUD 风格)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 构建顶部抛物线轨迹弧 (GPU 程序化矢量网格图元，0 CPU 软件光栅化，MFP-SPEC-002)
            _arcImage = CreateChild<ProceduralTimelineArcImage>("Trajectory_Arc", transform, new Vector2(ArcWidth * s, ArcHeight * s), new Vector2(0f, 26f * s));
            _arcImage.raycastTarget = false;
            _arcImage.LineThickness = 2.5f * s;
            _arcImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);

            // 4. 构建任务序列关键节点 (Milestones)
            BuildMilestones(s, theme);

            // 5. 动态飞行光标 (Progress Pip)
            _progressPipImage = CreateChild<Image>("Progress_Pip", transform, new Vector2(7f * s, 7f * s), new Vector2(-ArcWidth * 0.5f * s, 26f * s));
            _progressPipRt = _progressPipImage.rectTransform;
            _progressPipImage.raycastTarget = false;
            _progressPipImage.color = theme.AccentPrimary;

            // 6. 中央任务时钟 (Mission Time: T+ 00:08:03)
            _missionClockText = UIFactory.CreateText(transform, "Mission_Clock", I18n.Tr("WIDGET_SPX_CLOCK_PLACEHOLDER", "T+ 00:00:00"), Mathf.RoundToInt(24f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _missionClockText.fontStyle = FontStyle.Bold;
            RectTransform clockRt = _missionClockText.rectTransform;
            clockRt.sizeDelta = new Vector2(280f * s, 30f * s);
            clockRt.anchoredPosition = new Vector2(0f, -4f * s);

            // 7. 任务阶段副标题 (Mission Phase / Subtitle)
            string defaultPhase = !string.IsNullOrEmpty(_subtitleCustom)
                ? _subtitleCustom
                : (!string.IsNullOrEmpty(config?.DisplayName) && config.DisplayName != "TIMELINE"
                    ? config.DisplayName.ToUpperInvariant()
                    : "STARSHIP FLIGHT TEST");
            _missionPhaseText = UIFactory.CreateText(transform, "Mission_Phase", defaultPhase, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _missionPhaseText.fontStyle = FontStyle.Bold;
            RectTransform phaseRt = _missionPhaseText.rectTransform;
            phaseRt.sizeDelta = new Vector2(360f * s, 16f * s);
            phaseRt.anchoredPosition = new Vector2(0f, -26f * s);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Timeline Panel Background", _bgImage.gameObject, "SpaceX任务时序抛物线面板底板", t => ApplyCard(_bgImage, _bgOutline, _currentCardRole, t)));
            if (_arcImage != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "arc_mesh", "Trajectory Arc Mesh", _arcImage.gameObject, "抛物线任务上升轨迹GPU矢量网格", t => { if (_arcImage != null) _arcImage.color = WidgetStyleManager.Weighted(t.AccentSecondary, LineWeight.Medium); }));
            }
            if (_progressPipImage != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "progress_pip", "Progress Pip", _progressPipImage.gameObject, "当前飞行进度光标"));
            }
            this.Controls.Register(new WidgetReadoutControl("mission_clock", "任务时钟读数", _missionClockText != null ? _missionClockText.gameObject : null, _missionClockText, null, TextStyleRole.PrimaryValue, _clockToken));
            this.Controls.Register(new WidgetReadoutControl("mission_phase", "任务阶段副标题", _missionPhaseText != null ? _missionPhaseText.gameObject : null, _missionPhaseText, null, TextStyleRole.SecondaryValue, "{MET}"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildMilestones(float s, ThemeConfig theme)
        {
            var defs = new (string text, float normX, bool isAbove)[]
            {
                (MilestoneLiftoff, 0.10f, false),
                (MilestoneMaxQ, 0.28f, false),
                (MilestoneStageSep, 0.44f, false),
                (MilestoneLanding, 0.60f, true),
                (MilestoneSeco, 0.76f, true),
                (MilestoneOrbit, 0.92f, false)
            };

            _milestones = new MilestoneUI[defs.Length];
            Color dotColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Bold);
            Color textColor = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Label, theme);

            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float xPos = (def.normX - 0.5f) * ArcWidth * s;
                float yCurve = ComputeArcY(def.normX) * s + 26f * s;

                // 节点小圆点
                Image dotImg = CreateChild<Image>($"Milestone_Dot_{i}", transform, new Vector2(4f * s, 4f * s), new Vector2(xPos, yCurve));
                dotImg.color = dotColor;
                dotImg.raycastTarget = false;

                // 节点文字标签
                float textY = def.isAbove ? yCurve + 8f * s : yCurve - 8f * s;
                Text label = UIFactory.CreateText(transform, $"Milestone_Lbl_{i}", def.text, Mathf.RoundToInt(6.5f * s),
                    def.isAbove ? TextAnchor.LowerCenter : TextAnchor.UpperCenter, textColor);
                label.fontStyle = FontStyle.Bold;
                RectTransform lblRt = label.rectTransform;
                lblRt.sizeDelta = new Vector2(100f * s, 12f * s);
                lblRt.anchoredPosition = new Vector2(xPos, textY);

                _milestones[i] = new MilestoneUI
                {
                    Dot = dotImg,
                    Label = label,
                    NormalizedX = def.normX
                };
            }
        }

        public static float ComputeArcY(float normX)
        {
            // 抛物弧函数：顶点在 normX = 0.55，Y 范围 0 .. ArcHeight
            float dx = (normX - 0.55f) / 0.50f;
            float factor = Mathf.Clamp01(1f - dx * dx);
            return (factor * ArcHeight) - (ArcHeight * 0.5f);
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _bgOutline, _currentCardRole, theme);

            // 轨迹弧着色
            if (_arcImage != null)
            {
                _arcImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
            }

            // 光标着色
            if (_progressPipImage != null)
            {
                _progressPipImage.color = theme.AccentPrimary;
            }

            // 节点着色
            if (_milestones != null)
            {
                Color dotColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Heavy);
                for (int i = 0; i < _milestones.Length; i++)
                {
                    if (_milestones[i].Dot != null) _milestones[i].Dot.color = dotColor;
                    if (_milestones[i].Label != null) ApplyText(_milestones[i].Label, TextStyleRole.Label, theme);
                }
            }

            // 时钟与阶段副标题
            ApplyText(_missionClockText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_missionPhaseText, TextStyleRole.SecondaryValue, theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            _logic.Evaluate(context.Telemetry, context.DeltaTime);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;
            OnRenderState(_logic.CurrentState, theme);
        }

        private void OnRenderState(in SpaceXTimelineState state, ThemeConfig theme)
        {
            if (!state.HasVessel) return;

            // 1. 任务时钟更新 (T+ 00:08:03 格式)
            if (double.IsNaN(_lastCachedMissionTime.Value) || Math.Abs(state.MissionTime - _lastCachedMissionTime.Value) >= 0.5)
            {
                _lastCachedMissionTime.Update(state.MissionTime);
                if (_lastClockStr.Update(state.ClockText) && _missionClockText != null)
                {
                    _missionClockText.text = state.ClockText;
                }
            }

            // 2. 动态计算时序光标进度 (0.0 .. 1.0)
            if (Mathf.Abs(state.Progress - _lastCachedPipProgress.Value) > 0.002f)
            {
                _lastCachedPipProgress.Update(state.Progress);
                float s = CurrentDpiScale;
                float pipX = (state.Progress - 0.5f) * ArcWidth * s;
                float pipY = ComputeArcY(state.Progress) * s + 26f * s;
                if (_progressPipRt != null)
                {
                    _progressPipRt.SetAnchoredPositionSafe(new Vector2(pipX, pipY));
                }

                // 动态高亮已达成里程碑节点
                UpdateMilestones(state.Progress);
            }

            // 3. 动态任务阶段文本推导
            if (_lastPhaseStr.Update(state.PhaseText))
            {
                if (_missionPhaseText != null)
                {
                    _missionPhaseText.text = state.PhaseText;
                }
            }
        }

        private void UpdateMilestones(float currentProgress)
        {
            if (_milestones == null) return;
            ThemeConfig th = WidgetStyleManager.Instance.CurrentTheme;
            Color activeDot = th.AccentPrimary;
            Color futureDot = WidgetStyleManager.Weighted(th.AccentSecondary, LineWeight.Ghost);

            for (int i = 0; i < _milestones.Length; i++)
            {
                bool isReached = currentProgress >= (_milestones[i].NormalizedX - 0.015f);
                if (_milestones[i].Dot != null)
                {
                    _milestones[i].Dot.color = isReached ? activeDot : futureDot;
                }
                if (_milestones[i].Label != null)
                {
                    ApplyText(_milestones[i].Label, isReached ? TextStyleRole.Accent : TextStyleRole.Label, th);
                }
            }
        }

        protected override void OnDestroy()
        {
            _milestones = null;
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// GPU 程序化抛物线时序轨迹网格 (零 CPU 软件光栅化，纯 GPU 几何网格，MFP-SPEC-002)
    /// </summary>
    public class ProceduralTimelineArcImage : MaskableGraphic
    {
        public float LineThickness = 2.5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0.01f || r.height <= 0.01f) return;

            Color c = color;
            const int segments = 48;
            float halfThick = LineThickness * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float normX = (float)i / segments;
                float x = r.xMin + normX * r.width;

                float dx = (normX - 0.55f) / 0.50f;
                float factor = Mathf.Clamp01(1f - dx * dx);
                // r 中心在 (0, 0)，y 范围在 r.yMin .. r.yMax
                float y = r.yMin + factor * r.height;

                // 计算一阶导数以获得精确平滑的线宽法线
                float dFactor = (Mathf.Abs(dx) < 1f) ? (-2f * dx / 0.50f) : 0f;
                float slope = dFactor * (r.height / r.width);
                Vector2 tangent = new Vector2(1f, slope).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x) * halfThick;

                Vector2 center = new Vector2(x, y);
                vh.AddVert(center - normal, c, Vector2.zero);
                vh.AddVert(center + normal, c, Vector2.zero);

                if (i > 0)
                {
                    int baseIdx = (i - 1) * 2;
                    vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                    vh.AddTriangle(baseIdx + 3, baseIdx + 2, baseIdx);
                }
            }
        }
    }
}
