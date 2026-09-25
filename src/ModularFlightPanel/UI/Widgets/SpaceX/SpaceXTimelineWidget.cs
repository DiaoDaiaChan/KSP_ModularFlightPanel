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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

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
        private double _lastCachedMissionTime = double.NaN;
        private float _lastCachedPipProgress = -1f;
        private string _lastPhaseStr = string.Empty;
        private string _lastClockStr = string.Empty;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;

        // CustomTemplate 自定义通道
        private string _clockToken = "{MET:COMPACT}";
        private string _subtitleCustom;

        // 几何参数 (基准像素)
        private const float ArcWidth = 460f;
        private const float ArcHeight = 24f;

        private void ParseCustomTemplate(string tpl)
        {
            _clockToken = "{MET:COMPACT}";
            _subtitleCustom = null;
            if (string.IsNullOrEmpty(tpl)) return;
            string[] pairs = tpl.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i].Trim();
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                string k = p.Substring(0, eq).Trim().ToUpperInvariant();
                string v = p.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "CLOCK_TOKEN": _clockToken = v; break;
                    case "SUBTITLE": _subtitleCustom = v; break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            // 1. 组件包围盒 (基准 520x88 逻辑像素)
            Vector2 size = new Vector2(520f * s, 88f * s);
            RectTransform.sizeDelta = size;

            // 2. 半透卡片底板 (全息透明 HUD 风格)
            _bgImage = gameObject.AddComponent<Image>();
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 3. 构建顶部抛物线轨迹弧 (Trajectory Arc)
            EnsureSharedArcTexture();

            GameObject arcGo = new GameObject("Trajectory_Arc", typeof(RectTransform), typeof(RawImage));
            arcGo.transform.SetParent(transform, false);
            RectTransform arcRt = arcGo.GetComponent<RectTransform>();
            arcRt.sizeDelta = new Vector2(ArcWidth * s, ArcHeight * s);
            arcRt.anchoredPosition = new Vector2(0f, 26f * s);

            _arcRawImage = arcGo.GetComponent<RawImage>();
            _arcRawImage.texture = _sharedArcTexture;
            _arcRawImage.raycastTarget = false;

            // 4. 构建任务序列关键节点 (Milestones)
            BuildMilestones(s, theme);

            // 5. 动态飞行光标 (Progress Pip)
            GameObject pipGo = new GameObject("Progress_Pip", typeof(RectTransform), typeof(Image));
            pipGo.transform.SetParent(transform, false);
            _progressPipRt = pipGo.GetComponent<RectTransform>();
            _progressPipRt.sizeDelta = new Vector2(7f * s, 7f * s);
            _progressPipRt.anchoredPosition = new Vector2(-ArcWidth * 0.5f * s, 26f * s);

            _progressPipImage = pipGo.GetComponent<Image>();
            _progressPipImage.raycastTarget = false;

            // 6. 中央任务时钟 (Mission Time: T+ 00:08:03)
            _missionClockText = UIFactory.CreateText(transform, "Mission_Clock", "T+ 00:00:00", Mathf.RoundToInt(24f * s), TextAnchor.MiddleCenter,
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
            this.Controls.Register(new WidgetReadoutControl(_missionClockText, null, TextStyleRole.PrimaryValue, "Mission Clock", "任务时钟读数"));
            this.Controls.Register(new WidgetReadoutControl(_missionPhaseText, null, TextStyleRole.SecondaryValue, "Mission Phase", "任务阶段副标题"));
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
                GameObject dotGo = new GameObject($"Milestone_Dot_{i}", typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(transform, false);
                RectTransform dotRt = dotGo.GetComponent<RectTransform>();
                dotRt.sizeDelta = new Vector2(4f * s, 4f * s);
                dotRt.anchoredPosition = new Vector2(xPos, yCurve);

                Image dotImg = dotGo.GetComponent<Image>();
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

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 任务时钟更新 (T+ 00:08:03 格式)
            double mTime = telemetry.MissionTime;
            if (double.IsNaN(_lastCachedMissionTime) || Math.Abs(mTime - _lastCachedMissionTime) >= 0.5)
            {
                _lastCachedMissionTime = mTime;
                string clockStr = TelemetryTokenEngine.Evaluate(_clockToken, telemetry);
                if (clockStr != _lastClockStr && _missionClockText != null)
                {
                    _lastClockStr = clockStr;
                    _missionClockText.text = clockStr;
                }
            }

            // 2. 动态计算时序光标进度 (0.0 .. 1.0)
            float pipProgress = CalculateMissionProgress(telemetry);
            if (Mathf.Abs(pipProgress - _lastCachedPipProgress) > 0.002f)
            {
                _lastCachedPipProgress = pipProgress;
                float s = CurrentDpiScale;
                float pipX = (pipProgress - 0.5f) * ArcWidth * s;
                float pipY = ComputeArcY(pipProgress) * s + 26f * s;
                if (_progressPipRt != null)
                {
                    _progressPipRt.anchoredPosition = new Vector2(pipX, pipY);
                }
            }

            // 3. 动态任务阶段文本推导
            string dynamicPhase = ResolveMissionPhase(telemetry);
            if (dynamicPhase != _lastPhaseStr)
            {
                _lastPhaseStr = dynamicPhase;
                if (_missionPhaseText != null)
                {
                    _missionPhaseText.text = dynamicPhase;
                }
            }
        }

        private float CalculateMissionProgress(IFlightTelemetry telem)
        {
            if (telem.FlightSituation == "PRELAUNCH" || telem.FlightSituation == "LANDED")
            {
                return 0.08f;
            }

            // 根据入轨速度与高度合成飞行进度
            double speed = telem.CurrentSpeed;
            double alt = telem.AltitudeASL;

            if (alt < 15000.0)
            {
                // 地面至穿音障 (0.08 .. 0.28)
                return Mathf.Lerp(0.08f, 0.28f, (float)(alt / 15000.0));
            }
            if (alt < 65000.0 && telem.CurrentStage >= 2)
            {
                // 一级爬升与分级 (0.28 .. 0.44)
                return Mathf.Lerp(0.28f, 0.44f, (float)((alt - 15000.0) / 50000.0));
            }
            if (speed < 6500.0 && alt < 160000.0)
            {
                // 二级加速飞向入轨点 (0.44 .. 0.76)
                float t = Mathf.Clamp01((float)(speed / 6500.0));
                return Mathf.Lerp(0.44f, 0.76f, t);
            }

            // 轨道巡航与任务完成 (0.76 .. 0.95)
            return Mathf.Clamp(0.76f + (float)(telem.MissionTime % 600.0 / 600.0) * 0.19f, 0.76f, 0.95f);
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
            if (sit == "PRELAUNCH") return "PAD STANDBY / COUNTDOWN";
            if (sit == "LANDED" || sit == "SPLASHED") return "TOUCHDOWN NOMINAL";

            if (telem.AltitudeASL < 15000.0 && telem.VerticalSpeed > 10.0)
            {
                return telem.DynamicPressure > 25.0 ? "MAX-Q DYNAMIC PRESSURE" : "SUPER HEAVY POWERED ASCENT";
            }
            if (telem.CurrentStage <= 1 && telem.Throttle > 0.05f)
            {
                return "STARSHIP SECOND STAGE BURN";
            }
            if (telem.OrbitalSpeed > 2000.0 && telem.VerticalSpeed < 50.0 && telem.VerticalSpeed > -50.0)
            {
                return "ORBITAL INSERTION / COAST";
            }
            if (telem.VerticalSpeed < -50.0 && telem.AltitudeASL < 70000.0)
            {
                return "ATMOSPHERIC ENTRY PHASE";
            }

            return "STARSHIP FLIGHT TEST";
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
