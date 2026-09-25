using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 载人龙飞船全景任务状态与遥测顶栏 (SpaceX Panoramic Header Banner)
    /// 包含：飞行阶段徽章 (Active Phase)、倒计时/任务时钟、惯性速度、高度、远地点、近地点与倾角/航向读数。
    /// 纯 UGUI 高对比度排版，遵循 MFP 规范，0 颜色字面量，分阶梯低开销刷新。
    /// </summary>
    [FlightWidget("spacex_header", "dragon_header", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 任务遥测顶栏", Description = "SpaceX 顶部贯通式航电状态栏：主动飞行阶段胶囊徽章、倒计时与 5 组高对比度轨道数显列。", DefaultWidgetId = "spacex.header", DefaultX = 0f, DefaultY = 420f, IsSingleton = true, ExactIds = new[] { "spacex.header" })]
    public class SpaceXHeaderWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // UI 视图节点
        private Image _bgImage;
        private Outline _outline;
        private Image _bottomAccentLine;

        // 飞行阶段胶囊徽章
        private Image _phaseBadgeBg;
        private Outline _phaseBadgeOutline;
        private Text _phaseLabel;
        private Text _phaseValue;

        // 任务倒计时
        private Text _timerLabel;
        private Text _timerValue;

        // 五列遥测参数 (Vel, Alt, Ap, Pe, Inc)
        private Text _velLabel;
        private Text _velValue;
        private Text _altLabel;
        private Text _altValue;
        private Text _apLabel;
        private Text _apValue;
        private Text _peLabel;
        private Text _peValue;
        private Text _incLabel;
        private Text _incValue;

        // 垂直分割线
        private Image _sep1;
        private Image _sep2;
        private Image _sep3;
        private Image _sep4;
        private Image _sep5;
        private Image _sep6;

        // 脏数据缓存
        private string _lastPhase = string.Empty;
        private string _lastTimer = string.Empty;
        private string _lastTimerLabel = string.Empty;
        private string _lastVel = string.Empty;
        private string _lastAlt = string.Empty;
        private string _lastAp = string.Empty;
        private string _lastPe = string.Empty;
        private string _lastInc = string.Empty;

        // CustomTemplate 自定义通道
        private string _phaseLabelStr = "ACTIVE PHASE";
        private string _timerLabelStr = "SPLASHDOWN / MET";
        private string _velLabelStr = "INERTIAL VELOCITY";
        private string _altLabelStr = "ALTITUDE";
        private string _apLabelStr = "APOGEE";
        private string _peLabelStr = "PERIGEE";
        private string _incLabelStr = "INCLINATION";
        private string _velToken;
        private string _altToken = "{ALT:ASL:DIST}";
        private string _apToken = "{AP:DIST}";
        private string _peToken = "{PE:DIST}";
        private string _incToken;

        private void ParseCustomTemplate(string tpl)
        {
            _phaseLabelStr = "ACTIVE PHASE";
            _timerLabelStr = "SPLASHDOWN / MET";
            _velLabelStr = "INERTIAL VELOCITY";
            _altLabelStr = "ALTITUDE";
            _apLabelStr = "APOGEE";
            _peLabelStr = "PERIGEE";
            _incLabelStr = "INCLINATION";
            _velToken = null;
            _altToken = "{ALT:ASL:DIST}";
            _apToken = "{AP:DIST}";
            _peToken = "{PE:DIST}";
            _incToken = null;

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
                    case "PHASE_LABEL": _phaseLabelStr = v; break;
                    case "TIMER_LABEL": _timerLabelStr = v; break;
                    case "VEL_LABEL": _velLabelStr = v; break;
                    case "ALT_LABEL": _altLabelStr = v; break;
                    case "AP_LABEL": _apLabelStr = v; break;
                    case "PE_LABEL": _peLabelStr = v; break;
                    case "INC_LABEL": _incLabelStr = v; break;
                    case "VEL_TOKEN": _velToken = v; break;
                    case "ALT_TOKEN": _altToken = v; break;
                    case "AP_TOKEN": _apToken = v; break;
                    case "PE_TOKEN": _peToken = v; break;
                    case "INC_TOKEN": _incToken = v; break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            // 1. 顶栏包围盒 (960px x 42px)
            Vector2 panelSize = new Vector2(960f * s, 42f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 底部电光青色细强调线 (1.5px)
            GameObject lineObj = UIFactory.CreatePanel(transform, "BottomAccentLine", new Vector2(panelSize.x, 1.5f * s), new Vector2(0f, -panelSize.y * 0.5f + 0.75f * s), theme.AccentPrimary);
            _bottomAccentLine = lineObj.GetComponent<Image>();

            // 2. 左侧飞行阶段徽章 (Active Phase Badge)
            float startX = -panelSize.x * 0.5f + 16f * s;
            GameObject badgeGo = UIFactory.CreatePanel(transform, "PhaseBadgeBg", new Vector2(170f * s, 30f * s), new Vector2(startX + 85f * s, 0f), style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme));
            _phaseBadgeBg = badgeGo.GetComponent<Image>();
            _phaseBadgeOutline = badgeGo.AddComponent<Outline>();
            _phaseBadgeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _phaseBadgeOutline.effectColor = style.GetLineColor(theme.AccentSecondary, LineWeight.Subtle, theme);

            _phaseLabel = UIFactory.CreateText(badgeGo.transform, "PhaseLabel", _phaseLabelStr, Mathf.RoundToInt(8f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform plRt = _phaseLabel.rectTransform;
            plRt.anchorMin = new Vector2(0f, 0.5f);
            plRt.anchorMax = new Vector2(1f, 1f);
            plRt.anchoredPosition = new Vector2(8f * s, -2f * s);
            plRt.sizeDelta = new Vector2(-16f * s, 0f);

            _phaseValue = UIFactory.CreateText(badgeGo.transform, "PhaseValue", "ORBITAL COAST", Mathf.RoundToInt(11f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform pvRt = _phaseValue.rectTransform;
            pvRt.anchorMin = new Vector2(0f, 0f);
            pvRt.anchorMax = new Vector2(1f, 0.65f);
            pvRt.anchoredPosition = new Vector2(8f * s, 3f * s);
            pvRt.sizeDelta = new Vector2(-16f * s, 0f);

            // 分割线 1
            float curX = startX + 180f * s;
            _sep1 = CreateSeparator(curX, panelSize.y, s, theme);

            // 3. 倒计时 / 任务时钟 (Mission / Splashdown Time)
            curX += 12f * s;
            CreateMetricColumn("Timer", curX, 120f * s, s, theme, _timerLabelStr, "T-00:00:00", out _timerLabel, out _timerValue);

            curX += 130f * s;
            _sep2 = CreateSeparator(curX, panelSize.y, s, theme);

            // 4. 五组核心遥测列
            // 速度 (Inertial Velocity)
            curX += 12f * s;
            CreateMetricColumn("Vel", curX, 110f * s, s, theme, _velLabelStr, "0.00 km/s", out _velLabel, out _velValue);

            curX += 120f * s;
            _sep3 = CreateSeparator(curX, panelSize.y, s, theme);

            // 高度 (Altitude)
            curX += 12f * s;
            CreateMetricColumn("Alt", curX, 100f * s, s, theme, _altLabelStr, "0 m", out _altLabel, out _altValue);

            curX += 110f * s;
            _sep4 = CreateSeparator(curX, panelSize.y, s, theme);

            // 远地点 (Apogee)
            curX += 12f * s;
            CreateMetricColumn("Ap", curX, 100f * s, s, theme, _apLabelStr, "0 m", out _apLabel, out _apValue);

            curX += 110f * s;
            _sep5 = CreateSeparator(curX, panelSize.y, s, theme);

            // 近地点 (Perigee)
            curX += 12f * s;
            CreateMetricColumn("Pe", curX, 100f * s, s, theme, _peLabelStr, "0 m", out _peLabel, out _peValue);

            curX += 110f * s;
            _sep6 = CreateSeparator(curX, panelSize.y, s, theme);

            // 倾角 (Inclination)
            curX += 12f * s;
            CreateMetricColumn("Inc", curX, 90f * s, s, theme, _incLabelStr, "0.00°", out _incLabel, out _incValue);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Header Bar Background", _bgImage.gameObject, "SpaceX顶栏全景面板底板", t => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            if (_bottomAccentLine != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "accent_line", "Bottom Accent Line", _bottomAccentLine.gameObject, "底部电光青色细强调线", t => { if (_bottomAccentLine != null) _bottomAccentLine.color = t.AccentPrimary; }));
            }
            this.Controls.Register(new WidgetReadoutControl(_phaseValue, _phaseLabel, TextStyleRole.Accent, "Active Phase", "当前任务飞行阶段徽章"));
            this.Controls.Register(new WidgetReadoutControl(_timerValue, _timerLabel, TextStyleRole.PrimaryValue, "Mission Timer", "任务时钟与溅落倒计时"));
            this.Controls.Register(new WidgetReadoutControl(_velValue, _velLabel, TextStyleRole.PrimaryValue, "Inertial Velocity", "惯性速度列"));
            this.Controls.Register(new WidgetReadoutControl(_altValue, _altLabel, TextStyleRole.PrimaryValue, "Altitude", "海拔高度列"));
            this.Controls.Register(new WidgetReadoutControl(_apValue, _apLabel, TextStyleRole.PrimaryValue, "Apogee", "远地点高度列"));
            this.Controls.Register(new WidgetReadoutControl(_peValue, _peLabel, TextStyleRole.PrimaryValue, "Perigee", "近地点高度列"));
            this.Controls.Register(new WidgetReadoutControl(_incValue, _incLabel, TextStyleRole.PrimaryValue, "Inclination", "轨道倾角列"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private Image CreateSeparator(float posX, float height, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject sepGo = UIFactory.CreatePanel(transform, "Separator", new Vector2(1f * s, height * 0.55f), new Vector2(posX, 0f), style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            return sepGo.GetComponent<Image>();
        }

        private void CreateMetricColumn(string name, float posX, float width, float s, ThemeConfig theme, string labelStr, string defaultVal, out Text labelText, out Text valueText)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            float centerX = posX + width * 0.5f;

            labelText = UIFactory.CreateText(transform, $"{name}_Label", labelStr, Mathf.RoundToInt(8f * s), TextAnchor.UpperCenter, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform lRt = labelText.rectTransform;
            lRt.sizeDelta = new Vector2(width, 14f * s);
            lRt.anchoredPosition = new Vector2(centerX, 8f * s);

            valueText = UIFactory.CreateText(transform, $"{name}_Value", defaultVal, Mathf.RoundToInt(12f * s), TextAnchor.LowerCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform vRt = valueText.rectTransform;
            vRt.sizeDelta = new Vector2(width, 18f * s);
            vRt.anchoredPosition = new Vector2(centerX, -6f * s);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 飞行阶段推断
            string phase = InferFlightPhase(telemetry);
            if (phase != _lastPhase && _phaseValue != null)
            {
                _lastPhase = phase;
                _phaseValue.text = phase;
            }

            // 2. 任务时钟
            string timer;
            string tLbl;
            if (telemetry.HasManeuverNode && telemetry.ManeuverTimeToNode > 0)
            {
                timer = "T-" + FormatSeconds((float)telemetry.ManeuverTimeToNode);
                tLbl = "TIME TO NODE";
            }
            else
            {
                timer = "MET " + FormatSeconds((float)telemetry.MissionTime);
                tLbl = _timerLabelStr;
            }

            if (tLbl != _lastTimerLabel && _timerLabel != null)
            {
                _lastTimerLabel = tLbl;
                _timerLabel.text = tLbl;
            }

            if (timer != _lastTimer && _timerValue != null)
            {
                _lastTimer = timer;
                _timerValue.text = timer;
            }

            // 3. 惯性速度
            string velStr;
            if (!string.IsNullOrEmpty(_velToken))
            {
                velStr = TelemetryTokenEngine.Evaluate(_velToken, telemetry);
            }
            else
            {
                double spd = telemetry.OrbitalSpeed > 10.0 ? telemetry.OrbitalSpeed : telemetry.CurrentSpeed;
                velStr = spd >= 1000.0 ? $"{(spd * 0.001):F2} km/s" : $"{spd:F1} m/s";
            }
            if (velStr != _lastVel && _velValue != null)
            {
                _lastVel = velStr;
                _velValue.text = velStr;
            }

            // 4. 高度
            string altStr = TelemetryTokenEngine.Evaluate(_altToken, telemetry);
            if (altStr != _lastAlt && _altValue != null)
            {
                _lastAlt = altStr;
                _altValue.text = altStr;
            }

            // 5. 远地点与近地点
            string apStr = TelemetryTokenEngine.Evaluate(_apToken, telemetry);
            if (apStr != _lastAp && _apValue != null)
            {
                _lastAp = apStr;
                _apValue.text = apStr;
            }

            string peStr = TelemetryTokenEngine.Evaluate(_peToken, telemetry);
            if (peStr != _lastPe && _peValue != null)
            {
                _lastPe = peStr;
                _peValue.text = peStr;
            }

            // 6. 倾角 / 航向
            string incStr;
            if (!string.IsNullOrEmpty(_incToken))
            {
                incStr = TelemetryTokenEngine.Evaluate(_incToken, telemetry);
            }
            else
            {
                incStr = $"{telemetry.Heading:F1}°";
            }
            if (incStr != _lastInc && _incValue != null)
            {
                _lastInc = incStr;
                _incValue.text = incStr;
            }
        }

        private static string InferFlightPhase(IFlightTelemetry t)
        {
            if (t.CurrentSpeed < 1.0 && t.AltitudeASL < 150.0) return "PAD HOLD";
            if (t.AtmosphericPressure > 0.001)
            {
                if (t.VerticalSpeed < -50.0) return "REENTRY ENTRY";
                if (t.Mach >= 0.8 && t.Mach <= 1.3) return "TRANSONIC PASS";
                if (t.VerticalSpeed > 10.0) return "ASCENT POWERED";
                if (t.AltitudeAGL < 200.0 && t.VerticalSpeed < -2.0) return "CHUTE DESCENT";
            }
            if (t.Apoapsis > 70000.0 && t.Periapsis > 65000.0) return "ORBITAL COAST";
            if (t.HasManeuverNode) return "APPROACH / BURN";
            return "FREE FLIGHT";
        }

        private static string FormatSeconds(float sec)
        {
            if (sec < 0) sec = 0;
            int s = (int)sec;
            int h = s / 3600;
            int m = (s % 3600) / 60;
            int remainS = s % 60;
            return $"{h:D2}:{m:D2}:{remainS:D2}";
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_bottomAccentLine != null) _bottomAccentLine.color = theme.AccentPrimary;

            if (_phaseBadgeBg != null) _phaseBadgeBg.color = style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme);
            if (_phaseBadgeOutline != null) _phaseBadgeOutline.effectColor = style.GetLineColor(theme.AccentSecondary, LineWeight.Subtle, theme);
            if (_phaseLabel != null) ApplyText(_phaseLabel, TextStyleRole.Label, theme);
            if (_phaseValue != null) ApplyText(_phaseValue, TextStyleRole.Accent, theme);

            if (_timerLabel != null) ApplyText(_timerLabel, TextStyleRole.Label, theme);
            if (_timerValue != null) ApplyText(_timerValue, TextStyleRole.PrimaryValue, theme);

            if (_velLabel != null) ApplyText(_velLabel, TextStyleRole.Label, theme);
            if (_velValue != null) ApplyText(_velValue, TextStyleRole.PrimaryValue, theme);
            if (_altLabel != null) ApplyText(_altLabel, TextStyleRole.Label, theme);
            if (_altValue != null) ApplyText(_altValue, TextStyleRole.PrimaryValue, theme);
            if (_apLabel != null) ApplyText(_apLabel, TextStyleRole.Label, theme);
            if (_apValue != null) ApplyText(_apValue, TextStyleRole.PrimaryValue, theme);
            if (_peLabel != null) ApplyText(_peLabel, TextStyleRole.Label, theme);
            if (_peValue != null) ApplyText(_peValue, TextStyleRole.PrimaryValue, theme);
            if (_incLabel != null) ApplyText(_incLabel, TextStyleRole.Label, theme);
            if (_incValue != null) ApplyText(_incValue, TextStyleRole.PrimaryValue, theme);

            Color sepCol = style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme);
            if (_sep1 != null) _sep1.color = sepCol;
            if (_sep2 != null) _sep2.color = sepCol;
            if (_sep3 != null) _sep3.color = sepCol;
            if (_sep4 != null) _sep4.color = sepCol;
            if (_sep5 != null) _sep5.color = sepCol;
            if (_sep6 != null) _sep6.color = sepCol;
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
