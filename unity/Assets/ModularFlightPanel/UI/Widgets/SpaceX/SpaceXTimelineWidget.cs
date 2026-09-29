using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 星舰任务序列时序与时钟组件 (SpaceX Webcast Timeline & Mission Clock)
    /// 包含三大核心要素：
    ///   1. 任务序列 (Mission Sequence)：贯穿顶部的抛物线轨迹弧、关键任务节点 (LIFTOFF, MAX-Q, STAGE SEP, LANDING, SECO) 与动态飞行光标
    ///   2. 任务时间 (Mission Time)：中央超大现代等宽数字时钟 (T+ 00:08:03 / T- 00:00:10)
    ///   3. 任务阶段 (Mission Phase)：时钟下方副标题标牌 (STARSHIP FLIGHT TEST / 动态分级飞行情景)
    /// 严格遵循 MFP 架构规范：零硬编码与零颜色字面量 (MFP-SPEC-006)。
    /// </summary>
    [FlightWidget("spacex_timeline", "dragon_timeline", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 飞行关键时序甘特轴", Description = "横排甘特式任务阶段进度标尺：MECO、分级、入轨、对接窗口各节点动态光标推进。", DefaultWidgetId = "spacex.timeline", DefaultX = 0f, DefaultY = 320f, IsSingleton = true, ExactIds = new[] { "spacex.timeline" })]
    public class SpaceXTimelineWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(520f, 88f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget Clock = TextWidget.Value(I18n.Tr("WIDGET_SPX_CLOCK_PLACEHOLDER", "T+ 00:00:00"));
        public TextWidget Phase = TextWidget.Unit(I18n.Tr("WIDGET_SPX_STARSHIP_FLIGHT_TEST", "星舰飞行试验"));

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;

        // 轨迹抛物弧与节点
        private RawImage _arcRawImage;
        private static Texture2D _sharedArcTexture;
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

        // 缓存与脏标记
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

            // 1. 组件包围盒 (基准 520x88 逻辑像素)
            Vector2 size = new Vector2(520f * s, 88f * s);
            RectTransform.sizeDelta = size;

            // 2. 半透卡片底板 (全息透明 HUD 风格)
            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 构建顶部抛物线轨迹弧 (Trajectory Arc)
            EnsureSharedArcTexture();

            _arcRawImage = CreateChild<RawImage>("Trajectory_Arc", transform, new Vector2(ArcWidth * s, ArcHeight * s), new Vector2(0f, 26f * s));
            GameObject arcGo = _arcRawImage.gameObject;
            RectTransform arcRt = _arcRawImage.rectTransform;
            _arcRawImage.texture = _sharedArcTexture;
            _arcRawImage.raycastTarget = false;

            // 4. 构建任务序列关键节点 (Milestones)
            BuildMilestones(s, theme);

            // 5. 动态飞行光标 (Progress Pip)
            _progressPipImage = CreateChild<Image>("Progress_Pip", transform, new Vector2(7f * s, 7f * s), new Vector2(-ArcWidth * 0.5f * s, 26f * s));
            GameObject pipGo = _progressPipImage.gameObject;
            _progressPipRt = _progressPipImage.rectTransform;
            _progressPipImage.raycastTarget = false;

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
            if (_arcRawImage != null)
            {
                this.Controls.Register(new WidgetGraphicViewportControl(_arcRawImage, "Trajectory Arc", "抛物线任务上升轨迹弧"));
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
            // 经典 SpaceX 星舰试飞时序关键节点
            var defs = new (string text, float normX, bool isAbove)[]
            {
                ("LIFTOFF", 0.10f, false),
                ("MAX-Q", 0.28f, false),
                ("STAGE SEP", 0.44f, false),
                ("SUPER HEAVY LANDING", 0.60f, true),
                ("SECO", 0.76f, true),
                ("ORBIT", 0.92f, false)
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
                GameObject dotGo = dotImg.gameObject;
                RectTransform dotRt = dotImg.rectTransform;
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

        private static float ComputeArcY(float normX)
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
            if (_arcRawImage != null)
            {
                _arcRawImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium);
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
                Color labelColor = style.GetTextColor(TextStyleRole.Label, theme);
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

        private double _dataMissionTime;
        private string _dataClockStr;
        private float _dataPipProgress;
        private string _dataDynamicPhase;
        private bool _dataHasVessel;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataHasVessel = false;
                return;
            }
            _dataHasVessel = true;

            // 1. 任务时钟更新
            _dataMissionTime = telemetry.MissionTime;
            _dataClockStr = TelemetryTokenEngine.Evaluate(_clockToken, telemetry);

            // 2. 动态计算时序光标进度 (0.0 .. 1.0)
            _dataPipProgress = CalculateMissionProgress(telemetry);

            // 3. 动态任务阶段文本推导
            _dataDynamicPhase = ResolveMissionPhase(telemetry);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_dataHasVessel) return;

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;

            // 1. 任务时钟更新 (T+ 00:08:03 格式)
            if (double.IsNaN(_lastCachedMissionTime.Value) || Math.Abs(_dataMissionTime - _lastCachedMissionTime.Value) >= 0.5)
            {
                _lastCachedMissionTime.Update(_dataMissionTime);
                if (_lastClockStr.Update(_dataClockStr) && _missionClockText != null)
                {
                    _missionClockText.text = _dataClockStr;
                }
            }

            // 2. 动态计算时序光标进度 (0.0 .. 1.0)
            if (Mathf.Abs(_dataPipProgress - _lastCachedPipProgress.Value) > 0.002f)
            {
                _lastCachedPipProgress.Update(_dataPipProgress);
                float s = CurrentDpiScale;
                float pipX = (_dataPipProgress - 0.5f) * ArcWidth * s;
                float pipY = ComputeArcY(_dataPipProgress) * s + 26f * s;
                if (_progressPipRt != null)
                {
                    _progressPipRt.SetAnchoredPositionSafe(new Vector2(pipX, pipY));
                }

                // 动态高亮已达成里程碑节点
                UpdateMilestones(_dataPipProgress);
            }

            // 3. 动态任务阶段文本推导
            if (_lastPhaseStr.Update(_dataDynamicPhase))
            {
                if (_missionPhaseText != null)
                {
                    _missionPhaseText.text = _dataDynamicPhase;
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

        private float CalculateMissionProgress(IFlightTelemetry telem)
        {
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

        private string ResolveMissionPhase(IFlightTelemetry telem)
        {
            if (!string.IsNullOrEmpty(_subtitleCustom))
            {
                return _subtitleCustom;
            }

            if (!string.IsNullOrEmpty(Config?.DisplayName) && Config.DisplayName != "TIMELINE")
            {
                return Config.DisplayName.ToUpperInvariant();
            }

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

        private static void EnsureSharedArcTexture()
        {
            if (_sharedArcTexture != null) return;

            const int w = 512;
            const int h = 64;
            _sharedArcTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _sharedArcTexture.filterMode = FilterMode.Bilinear;
            _sharedArcTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] cols = new Color[w * h];
            Color opaque = WidgetStyleManager.NeutralOpaque;

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    // 抛物弧曲线中心 Y (归一化 0..1)
                    float dx = (nx - 0.55f) / 0.48f;
                    float curveY = Mathf.Clamp01(1f - dx * dx) * 0.70f + 0.15f;

                    float dist = Mathf.Abs(ny - curveY);
                    float thickness = 0.045f;
                    float feather = 0.035f;

                    if (dist > thickness + feather)
                    {
                        cols[y * w + x] = Color.clear;
                        continue;
                    }

                    float alpha = Mathf.Clamp01((thickness + feather - dist) / feather);
                    Color c = opaque;
                    c.a = alpha;
                    cols[y * w + x] = c;
                }
            }

            _sharedArcTexture.SetPixels(cols);
            _sharedArcTexture.Apply(false, true);
        }

        protected override void OnDestroy()
        {
            _milestones = null;
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
