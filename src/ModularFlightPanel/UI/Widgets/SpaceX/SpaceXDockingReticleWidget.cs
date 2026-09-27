using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 载人龙飞船 ISS 空间站对接光标 HUD (SpaceX Docking Reticle & Attitude Alignment HUD)
    /// 包含：极简同心双环准星、十字对准线、3轴姿态角与偏差率 (ROLL / PITCH / YAW)、
    /// 相对距离 (RANGE) 与接近率 (RATE)、以及 4 轴姿控喷管脉冲指示器 (Thruster Arrows)。
    /// 严格遵循 MFP 规范，0 颜色字面量，60Hz Critical 满帧驱动。
    /// </summary>
    [FlightWidget("spacex_docking", "dragon_docking", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 空间站对接与姿态准星", Description = "SpaceX ISS 空间站对接瞄准器：同心双环准星、3 轴姿态偏差角与角速度、测距接近率与 RCS 点亮。", DefaultWidgetId = "spacex.docking", DefaultX = 0f, DefaultY = 170f, IsSingleton = true, HighFrequency = true, ExactIds = new[] { "spacex.docking" })]
    public class SpaceXDockingReticleWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(220f, 220f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // 声明式微控件
        public TextWidget RangeTitle = TextWidget.Title(I18n.Tr("WIDGET_SPX_RANGE", "距离"));
        public TextWidget RateTitle = TextWidget.Title(I18n.Tr("WIDGET_SPX_RATE", "速率"));

        private Image _bgImage;
        private Outline _outline;

        // 同心准星环
        private readonly List<Image> _outerRingSegments = new List<Image>();
        private readonly List<Image> _innerRingSegments = new List<Image>();

        // 十字准星与中心标
        private RectTransform _crossRootRt;
        private Image _centerCrossH;
        private Image _centerCrossV;
        private Image _centerReticleRing;

        // 3轴姿态读数 (Roll, Pitch, Yaw)
        private Text _rollLabel;
        private Text _rollValue;
        private Text _pitchLabel;
        private Text _pitchValue;
        private Text _yawLabel;
        private Text _yawValue;

        // 对接测距与接近率 (Range & Rate)
        private Text _rangeLabel;
        private Text _rangeValue;
        private Text _rateLabel;
        private Text _rateValue;
        private Text _xyzOffsets;

        // 4 向 RCS 喷管脉冲指示器 (Up, Down, Left, Right)
        private Image _thrusterUp;
        private Image _thrusterDown;
        private Image _thrusterLeft;
        private Image _thrusterRight;

        // 脏数据缓存
        private string _lastRoll = string.Empty;
        private string _lastPitch = string.Empty;
        private string _lastYaw = string.Empty;
        private string _lastRange = string.Empty;
        private string _lastRate = string.Empty;
        private string _lastXyz = string.Empty;
        private int _lastThrusterState = -1;

        // CustomTemplate 自定义通道
        private string _rollLabelText = "ROLL";
        private string _pitchLabelText = "PITCH";
        private string _yawLabelText = "YAW";
        private string _rangeLabelText = "RANGE";
        private string _rateLabelText = "RATE";
        private string _rangeToken = "{ALT:AGL:DIST}";
        private string _rateToken;
        private string _xyzTemplate = "X {0:F1}m\nY {1:F1}m\nZ {2:F1}m";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _rollLabelText = GetTemplateChannel("ROLL_LABEL", I18n.Tr("WIDGET_SPX_ROLL", "滚转"));
            _pitchLabelText = GetTemplateChannel("PITCH_LABEL", I18n.Tr("WIDGET_SPX_PITCH", "俯仰"));
            _yawLabelText = GetTemplateChannel("YAW_LABEL", I18n.Tr("WIDGET_SPX_YAW", "偏航"));
            _rangeLabelText = GetTemplateChannel("RANGE_LABEL", I18n.Tr("WIDGET_SPX_RANGE", "距离"));
            _rateLabelText = GetTemplateChannel("RATE_LABEL", I18n.Tr("WIDGET_SPX_RATE", "速率"));
            _rangeToken = GetTemplateChannel("RANGE_TOKEN", "{ALT:AGL:DIST}");
            _rateToken = GetTemplateChannel("RATE_TOKEN", null);
            _xyzTemplate = GetTemplateChannel("XYZ_TEMPLATE", "X {0:F1}m\nY {1:F1}m\nZ {2:F1}m").Replace("\\n", "\n");

            Vector2 panelSize = new Vector2(220f * s, 220f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = CardBackground;
            _outline = CardOutline;
            if (_outline != null)
                _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 1. 构建同心圆环准星 (外环 R=82, 内环 R=44)
            BuildReticleRings(82f * s, 44f * s, s, theme);

            // 2. 构建中心十字准星
            BuildCenterCrosshair(s, theme);

            // 3. 构建 3 轴姿态偏差读数 (Roll, Pitch, Yaw)
            BuildAttitudeLabels(panelSize, s, theme);

            // 4. 构建测距与闭合速率 (Range, Rate, XYZ)
            BuildRangeAndRateLabels(panelSize, s, theme);

            // 5. 构建 4 轴姿控喷管脉冲指示器 (Thruster Arrows)
            BuildThrusterChevrons(panelSize, s, theme);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Docking HUD Background", _bgImage.gameObject, "SpaceX对接平视显示底板", t => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            if (_centerReticleRing != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "crosshair", "Center Crosshair", _centerReticleRing.gameObject, "对接中心环与十字准星"));
            }
            this.Controls.Register(new WidgetReadoutControl(_rollValue, _rollLabel, TextStyleRole.Accent, "Roll Deviation", "滚转对接偏差"));
            this.Controls.Register(new WidgetReadoutControl(_pitchValue, _pitchLabel, TextStyleRole.Accent, "Pitch Deviation", "俯仰对接偏差"));
            this.Controls.Register(new WidgetReadoutControl(_yawValue, _yawLabel, TextStyleRole.Accent, "Yaw Deviation", "偏航对接偏差"));
            this.Controls.Register(new WidgetReadoutControl(_rangeValue, _rangeLabel, TextStyleRole.PrimaryValue, "Range Readout", "对接距离读数"));
            this.Controls.Register(new WidgetReadoutControl(_rateValue, _rateLabel, TextStyleRole.PrimaryValue, "Closing Rate", "临近闭合速率读数"));
            this.Controls.Register(new WidgetReadoutControl(_xyzOffsets, null, TextStyleRole.Muted, "XYZ Offsets", "空间相对偏移量"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildReticleRings(float outerR, float innerR, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            RectTransform ringsRt = CreateContainer("ReticleRings", transform);
            GameObject ringsRoot = ringsRt.gameObject;

            Color outerCol = style.GetLineColor(LineWeight.Subtle, theme);
            Color innerCol = style.GetLineColor(LineWeight.Light, theme);

            // 外环：24 片圆周微段 (带 4 个主方位豁口)
            const int outerCount = 24;
            for (int i = 0; i < outerCount; i++)
            {
                if (i % 6 == 0) continue; // 留出 0/90/180/270 方位读数开口
                float ang = i * (360f / outerCount);
                float rad = ang * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * outerR, Mathf.Cos(rad) * outerR);

                GameObject seg = UIFactory.CreatePanel(ringsRoot.transform, $"Outer_{i}", new Vector2(6f * s, 1.2f * s), pos, outerCol);
                seg.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _outerRingSegments.Add(seg.GetComponent<Image>());
            }

            // 内环：16 片圆周微段
            const int innerCount = 16;
            for (int i = 0; i < innerCount; i++)
            {
                float ang = i * (360f / innerCount);
                float rad = ang * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * innerR, Mathf.Cos(rad) * innerR);

                GameObject seg = UIFactory.CreatePanel(ringsRoot.transform, $"Inner_{i}", new Vector2(4f * s, 1f * s), pos, innerCol);
                seg.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _innerRingSegments.Add(seg.GetComponent<Image>());
            }
        }

        private void BuildCenterCrosshair(float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _crossRootRt = CreateContainer("CenterCrosshair", transform);
            GameObject crossRoot = _crossRootRt.gameObject;

            Color lineCol = style.GetLineColor(LineWeight.Bold, theme);

            // 水平十字线 (带中央开口)
            GameObject h = UIFactory.CreatePanel(crossRoot.transform, "CrossH", new Vector2(28f * s, 1.2f * s), Vector2.zero, lineCol);
            _centerCrossH = h.GetComponent<Image>();

            // 垂直十字线
            GameObject v = UIFactory.CreatePanel(crossRoot.transform, "CrossV", new Vector2(1.2f * s, 28f * s), Vector2.zero, lineCol);
            _centerCrossV = v.GetComponent<Image>();

            // 中心瞄准小微标圆
            // 中心瞄准小微标圆
            GameObject dot = UIFactory.CreatePanel(crossRoot.transform, "CenterDot", new Vector2(6f * s, 6f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            _centerReticleRing = dot.GetComponent<Image>();
        }

        private void BuildAttitudeLabels(Vector2 size, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 顶部 ROLL
            _rollLabel = UIFactory.CreateText(transform, "RollLabel", _rollLabelText, Mathf.RoundToInt(8f * s), TextAnchor.LowerCenter, style.GetTextColor(TextStyleRole.Label, theme));
            _rollLabel.rectTransform.anchoredPosition = new Vector2(0f, 96f * s);
            _rollLabel.rectTransform.sizeDelta = new Vector2(70f * s, 12f * s);

            _rollValue = UIFactory.CreateText(transform, "RollValue", "0.0°", Mathf.RoundToInt(11f * s), TextAnchor.UpperCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _rollValue.rectTransform.anchoredPosition = new Vector2(0f, 84f * s);
            _rollValue.rectTransform.sizeDelta = new Vector2(70f * s, 14f * s);

            // 右侧 PITCH
            _pitchLabel = UIFactory.CreateText(transform, "PitchLabel", _pitchLabelText, Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            _pitchLabel.rectTransform.anchoredPosition = new Vector2(92f * s, 8f * s);
            _pitchLabel.rectTransform.sizeDelta = new Vector2(40f * s, 12f * s);

            _pitchValue = UIFactory.CreateText(transform, "PitchValue", "0.0°", Mathf.RoundToInt(11f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            _pitchValue.rectTransform.anchoredPosition = new Vector2(92f * s, -6f * s);
            _pitchValue.rectTransform.sizeDelta = new Vector2(40f * s, 14f * s);

            // 底部 YAW
            _yawLabel = UIFactory.CreateText(transform, "YawLabel", _yawLabelText, Mathf.RoundToInt(8f * s), TextAnchor.UpperCenter, style.GetTextColor(TextStyleRole.Label, theme));
            _yawLabel.rectTransform.anchoredPosition = new Vector2(0f, -86f * s);
            _yawLabel.rectTransform.sizeDelta = new Vector2(70f * s, 12f * s);

            _yawValue = UIFactory.CreateText(transform, "YawValue", "0.0°", Mathf.RoundToInt(11f * s), TextAnchor.LowerCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _yawValue.rectTransform.anchoredPosition = new Vector2(0f, -98f * s);
            _yawValue.rectTransform.sizeDelta = new Vector2(70f * s, 14f * s);
        }

        private void BuildRangeAndRateLabels(Vector2 size, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 左下侧：RANGE
            _rangeLabel = UIFactory.CreateText(transform, "RangeLabel", _rangeLabelText, Mathf.RoundToInt(7.5f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.Label, theme));
            _rangeLabel.rectTransform.anchoredPosition = new Vector2(-28f * s, -42f * s);
            _rangeLabel.rectTransform.sizeDelta = new Vector2(60f * s, 12f * s);

            _rangeValue = UIFactory.CreateText(transform, "RangeValue", "0 m", Mathf.RoundToInt(10.5f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _rangeValue.rectTransform.anchoredPosition = new Vector2(-28f * s, -54f * s);
            _rangeValue.rectTransform.sizeDelta = new Vector2(60f * s, 14f * s);

            // 右下侧：RATE
            _rateLabel = UIFactory.CreateText(transform, "RateLabel", _rateLabelText, Mathf.RoundToInt(7.5f * s), TextAnchor.LowerRight, style.GetTextColor(TextStyleRole.Label, theme));
            _rateLabel.rectTransform.anchoredPosition = new Vector2(28f * s, -42f * s);
            _rateLabel.rectTransform.sizeDelta = new Vector2(60f * s, 12f * s);

            _rateValue = UIFactory.CreateText(transform, "RateValue", "0.00 m/s", Mathf.RoundToInt(10.5f * s), TextAnchor.UpperRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _rateValue.rectTransform.anchoredPosition = new Vector2(28f * s, -54f * s);
            _rateValue.rectTransform.sizeDelta = new Vector2(60f * s, 14f * s);

            // 左侧上部：XYZ 相对偏移
            _xyzOffsets = UIFactory.CreateText(transform, "XyzOffsets", I18n.Tr("WIDGET_SPX_XYZ_OFFSETS", "X  0.0m\nY  0.0m\nZ  0.0m"), Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Muted, theme));
            _xyzOffsets.rectTransform.anchoredPosition = new Vector2(-75f * s, 35f * s);
            _xyzOffsets.rectTransform.sizeDelta = new Vector2(50f * s, 36f * s);
        }

        private void BuildThrusterChevrons(Vector2 size, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color idleCol = style.GetLineColor(LineWeight.Ghost, theme);

            // 上喷管指示标 (R=65)
            GameObject up = UIFactory.CreatePanel(transform, "ThrusterUp", new Vector2(10f * s, 3f * s), new Vector2(0f, 65f * s), idleCol);
            _thrusterUp = up.GetComponent<Image>();

            // 下喷管指示标
            GameObject down = UIFactory.CreatePanel(transform, "ThrusterDown", new Vector2(10f * s, 3f * s), new Vector2(0f, -65f * s), idleCol);
            _thrusterDown = down.GetComponent<Image>();

            // 左喷管指示标
            GameObject left = UIFactory.CreatePanel(transform, "ThrusterLeft", new Vector2(3f * s, 10f * s), new Vector2(-65f * s, 0f), idleCol);
            _thrusterLeft = left.GetComponent<Image>();

            // 右喷管指示标
            GameObject right = UIFactory.CreatePanel(transform, "ThrusterRight", new Vector2(3f * s, 10f * s), new Vector2(65f * s, 0f), idleCol);
            _thrusterRight = right.GetComponent<Image>();
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            float s = CurrentDpiScale;

            // 1. 三轴姿态读数 (锁定目标时优先指示对齐偏差，未锁定时指示载具姿态)
            if (telemetry.HasTarget)
            {
                string rollStr = $"{telemetry.TargetRollAlignment:+0.0;-0.0;0.0}°";
                if (rollStr != _lastRoll && _rollValue != null)
                {
                    _lastRoll = rollStr;
                    _rollValue.text = rollStr;
                }

                string pitchStr = $"{telemetry.TargetPitchAlignment:+0.0;-0.0;0.0}°";
                if (pitchStr != _lastPitch && _pitchValue != null)
                {
                    _lastPitch = pitchStr;
                    _pitchValue.text = pitchStr;
                }

                string yawStr = $"{telemetry.TargetYawAlignment:+0.0;-0.0;0.0}°";
                if (yawStr != _lastYaw && _yawValue != null)
                {
                    _lastYaw = yawStr;
                    _yawValue.text = yawStr;
                }
            }
            else
            {
                string rollStr = $"{telemetry.Roll:F1}°";
                if (rollStr != _lastRoll && _rollValue != null)
                {
                    _lastRoll = rollStr;
                    _rollValue.text = rollStr;
                }

                string pitchStr = $"{telemetry.Pitch:F1}°";
                if (pitchStr != _lastPitch && _pitchValue != null)
                {
                    _lastPitch = pitchStr;
                    _pitchValue.text = pitchStr;
                }

                string yawStr = $"{telemetry.Heading:F1}°";
                if (yawStr != _lastYaw && _yawValue != null)
                {
                    _lastYaw = yawStr;
                    _yawValue.text = yawStr;
                }
            }

            // 2. 距离与闭合速度 (有目标时取目标真值，无目标时安全回退)
            string rangeStr;
            if (telemetry.HasTarget)
            {
                double dist = telemetry.TargetDistance;
                rangeStr = dist >= 1000.0 ? $"{(dist * 0.001):F2} km" : $"{dist:F1} m";
            }
            else if (!string.IsNullOrEmpty(_rangeToken))
            {
                rangeStr = TelemetryTokenEngine.Evaluate(_rangeToken, telemetry);
            }
            else
            {
                double alt = telemetry.AltitudeAGL;
                rangeStr = alt >= 1000.0 ? $"{(alt * 0.001):F2} km" : $"{alt:F0} m";
            }
            if (rangeStr != _lastRange && _rangeValue != null)
            {
                _lastRange = rangeStr;
                _rangeValue.text = rangeStr;
            }

            string rateStr;
            if (telemetry.HasTarget)
            {
                float rate = telemetry.TargetClosingSpeed;
                string sign = rate > 0.001f ? "+" : "";
                rateStr = $"{sign}{rate:F2} m/s";
            }
            else if (!string.IsNullOrEmpty(_rateToken))
            {
                rateStr = TelemetryTokenEngine.Evaluate(_rateToken, telemetry);
            }
            else
            {
                double vsi = telemetry.VerticalSpeed;
                rateStr = Math.Abs(vsi) >= 100.0 ? $"{(vsi * 0.001):F2} km/s" : $"{vsi:F2} m/s";
            }
            if (rateStr != _lastRate && _rateValue != null)
            {
                _lastRate = rateStr;
                _rateValue.text = rateStr;
            }

            // 3. XYZ 相对空间偏差与准星平移导引
            if (telemetry.HasTarget)
            {
                float xOff = telemetry.TargetDeviationX;
                float yOff = telemetry.TargetDeviationY;
                float zOff = telemetry.TargetDeviationZ;
                string xyz = string.Format(_xyzTemplate, xOff, yOff, zOff);
                if (xyz != _lastXyz && _xyzOffsets != null)
                {
                    _lastXyz = xyz;
                    _xyzOffsets.text = xyz;
                }

                // 物理准星动态指引：十字微标圈随 X / Y 偏差偏移 (最大偏转 38 逻辑像素)
                if (_crossRootRt != null)
                {
                    float maxR = 38f * s;
                    float ox = Mathf.Clamp(xOff * 2.5f * s, -maxR, maxR);
                    float oy = Mathf.Clamp(yOff * 2.5f * s, -maxR, maxR);
                    _crossRootRt.anchoredPosition = new Vector2(ox, oy);
                }
            }
            else
            {
                string noTgt = "NO TARGET\nLOCKED";
                if (noTgt != _lastXyz && _xyzOffsets != null)
                {
                    _lastXyz = noTgt;
                    _xyzOffsets.text = noTgt;
                }

                if (_crossRootRt != null && _crossRootRt.anchoredPosition != Vector2.zero)
                {
                    _crossRootRt.anchoredPosition = Vector2.zero;
                }
            }

            // 4. RCS 喷管脉冲指示器 (融合姿态回转与 X/Y 平移操纵)
            int tState = 0;
            if (telemetry.IsRCSEnabled)
            {
                if (telemetry.PitchInput > 0.05f || telemetry.YInput > 0.05f) tState |= 1;
                if (telemetry.PitchInput < -0.05f || telemetry.YInput < -0.05f) tState |= 2;
                if (telemetry.YawInput < -0.05f || telemetry.RollInput < -0.05f || telemetry.XInput < -0.05f) tState |= 4;
                if (telemetry.YawInput > 0.05f || telemetry.RollInput > 0.05f || telemetry.XInput > 0.05f) tState |= 8;
            }

            if (tState != _lastThrusterState)
            {
                _lastThrusterState = tState;
                ThemeConfig th = WidgetStyleManager.Instance.CurrentTheme;
                WidgetStyleManager st = WidgetStyleManager.Instance;
                Color activeCol = st.GetMeterColor(MeterStyleRole.Primary, th);
                Color idleCol = st.GetLineColor(LineWeight.Ghost, th);

                if (_thrusterUp != null) _thrusterUp.color = (tState & 1) != 0 ? activeCol : idleCol;
                if (_thrusterDown != null) _thrusterDown.color = (tState & 2) != 0 ? activeCol : idleCol;
                if (_thrusterLeft != null) _thrusterLeft.color = (tState & 4) != 0 ? activeCol : idleCol;
                if (_thrusterRight != null) _thrusterRight.color = (tState & 8) != 0 ? activeCol : idleCol;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);

            Color outerCol = style.GetLineColor(LineWeight.Subtle, theme);
            Color innerCol = style.GetLineColor(LineWeight.Light, theme);

            for (int i = 0; i < _outerRingSegments.Count; i++)
                if (_outerRingSegments[i] != null) _outerRingSegments[i].color = outerCol;

            for (int i = 0; i < _innerRingSegments.Count; i++)
                if (_innerRingSegments[i] != null) _innerRingSegments[i].color = innerCol;

            Color crossCol = style.GetLineColor(LineWeight.Bold, theme);
            if (_centerCrossH != null) _centerCrossH.color = crossCol;
            if (_centerCrossV != null) _centerCrossV.color = crossCol;
            if (_centerReticleRing != null) _centerReticleRing.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            if (_rollLabel != null) ApplyText(_rollLabel, TextStyleRole.Label, theme);
            if (_rollValue != null) ApplyText(_rollValue, TextStyleRole.Accent, theme);
            if (_pitchLabel != null) ApplyText(_pitchLabel, TextStyleRole.Label, theme);
            if (_pitchValue != null) ApplyText(_pitchValue, TextStyleRole.Accent, theme);
            if (_yawLabel != null) ApplyText(_yawLabel, TextStyleRole.Label, theme);
            if (_yawValue != null) ApplyText(_yawValue, TextStyleRole.Accent, theme);

            if (_rangeLabel != null) ApplyText(_rangeLabel, TextStyleRole.Label, theme);
            if (_rangeValue != null) ApplyText(_rangeValue, TextStyleRole.PrimaryValue, theme);
            if (_rateLabel != null) ApplyText(_rateLabel, TextStyleRole.Label, theme);
            if (_rateValue != null) ApplyText(_rateValue, TextStyleRole.PrimaryValue, theme);
            if (_xyzOffsets != null) ApplyText(_xyzOffsets, TextStyleRole.Muted, theme);

            Color idleCol = style.GetLineColor(LineWeight.Ghost, theme);
            if (_thrusterUp != null) _thrusterUp.color = idleCol;
            if (_thrusterDown != null) _thrusterDown.color = idleCol;
            if (_thrusterLeft != null) _thrusterLeft.color = idleCol;
            if (_thrusterRight != null) _thrusterRight.color = idleCol;
        }

        protected override void OnDestroy()
        {
            _outerRingSegments.Clear();
            _innerRingSegments.Clear();
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
