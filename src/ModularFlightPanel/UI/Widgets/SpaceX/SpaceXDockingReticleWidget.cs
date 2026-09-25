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
    [FlightWidget("spacex_docking", "dragon_docking", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 空间站对接与姿态准星", Description = "SpaceX ISS 空间站对接瞄准器：同心双环准星、3 轴姿态偏差角与角速度、测距接近率与 RCS 点亮。", DefaultWidgetId = "spacex.docking", DefaultX = 0f, DefaultY = 170f, IsSingleton = true, ExactIds = new[] { "spacex.docking" })]
    public class SpaceXDockingReticleWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        private Image _bgImage;
        private Outline _outline;

        // 同心准星环
        private readonly List<Image> _outerRingSegments = new List<Image>();
        private readonly List<Image> _innerRingSegments = new List<Image>();

        // 十字准星与中心标
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

        private void ParseCustomTemplate(string tpl)
        {
            _rollLabelText = "ROLL";
            _pitchLabelText = "PITCH";
            _yawLabelText = "YAW";
            _rangeLabelText = "RANGE";
            _rateLabelText = "RATE";
            _rangeToken = "{ALT:AGL:DIST}";
            _rateToken = null;
            _xyzTemplate = "X {0:F1}m\nY {1:F1}m\nZ {2:F1}m";

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
                    case "ROLL_LABEL": _rollLabelText = v; break;
                    case "PITCH_LABEL": _pitchLabelText = v; break;
                    case "YAW_LABEL": _yawLabelText = v; break;
                    case "RANGE_LABEL": _rangeLabelText = v; break;
                    case "RATE_LABEL": _rateLabelText = v; break;
                    case "RANGE_TOKEN": _rangeToken = v; break;
                    case "RATE_TOKEN": _rateToken = v; break;
                    case "XYZ_TEMPLATE": _xyzTemplate = v.Replace("\\n", "\n"); break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            Vector2 panelSize = new Vector2(220f * s, 220f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _outline = gameObject.AddComponent<Outline>();
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
            GameObject ringsRoot = new GameObject("ReticleRings", typeof(RectTransform));
            ringsRoot.transform.SetParent(transform, false);

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
            GameObject crossRoot = new GameObject("CenterCrosshair", typeof(RectTransform));
            crossRoot.transform.SetParent(transform, false);

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
            _xyzOffsets = UIFactory.CreateText(transform, "XyzOffsets", "X  0.0m\nY  0.0m\nZ  0.0m", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Muted, theme));
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

            // 1. 三轴姿态读数
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

            // 2. 距离与闭合速度
            string rangeStr = TelemetryTokenEngine.Evaluate(_rangeToken, telemetry);
            if (rangeStr != _lastRange && _rangeValue != null)
            {
                _lastRange = rangeStr;
                _rangeValue.text = rangeStr;
            }

            string rateStr;
            if (!string.IsNullOrEmpty(_rateToken))
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

            // 3. XYZ 相对偏移 (支持模板)
            float xOff = telemetry.Pitch * 0.2f;
            float yOff = telemetry.Roll * 0.2f;
            float zOff = (float)(telemetry.AltitudeAGL % 100.0);
            string xyz = string.Format(_xyzTemplate, xOff, yOff, zOff);
            if (xyz != _lastXyz && _xyzOffsets != null)
            {
                _lastXyz = xyz;
                _xyzOffsets.text = xyz;
            }

            // 4. RCS 喷管脉冲指示器
            int tState = 0;
            if (telemetry.IsRCSEnabled)
            {
                if (telemetry.PitchInput > 0.05f) tState |= 1;
                if (telemetry.PitchInput < -0.05f) tState |= 2;
                if (telemetry.YawInput < -0.05f || telemetry.RollInput < -0.05f) tState |= 4;
                if (telemetry.YawInput > 0.05f || telemetry.RollInput > 0.05f) tState |= 8;
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
