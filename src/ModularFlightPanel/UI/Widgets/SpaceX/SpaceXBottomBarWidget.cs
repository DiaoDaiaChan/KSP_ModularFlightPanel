using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 载人龙飞船底部触控控制与深空网链路状态栏 (SpaceX Bottom Control & Comm Matrix)
    /// 包含：药丸触控开关 (RCS / SAS / SPEED MODE / PREC)、
    /// 当前航向指向状态 (POINTING MODE)、以及 SPX 地面站 / TDRS / ISS 通信链路矩阵。
    /// 严格遵循 MFP 规范，0 颜色字面量，10Hz Relaxed 阶梯刷新。
    /// </summary>
    public class SpaceXBottomBarWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Image _bgImage;
        private Outline _outline;

        // 药丸触控开关 (RCS, SAS, MODE, PREC)
        private Button _rcsBtn;
        private Image _rcsImg;
        private Outline _rcsOutline;
        private Text _rcsText;

        private Button _sasBtn;
        private Image _sasImg;
        private Outline _sasOutline;
        private Text _sasText;

        private Button _modeBtn;
        private Image _modeImg;
        private Outline _modeOutline;
        private Text _modeText;

        private Button _precBtn;
        private Image _precImg;
        private Outline _precOutline;
        private Text _precText;

        // 右侧指向模式与通信矩阵
        private Text _pointingLabel;
        private Text _pointingValue;

        private Text _commSpx;
        private Text _commTdrs;
        private Text _commIss;

        // 变动缓存
        private bool _lastRcs = false;
        private bool _lastSas = false;
        private bool _lastPrec = false;
        private string _lastModeStr = string.Empty;
        private string _lastPointing = string.Empty;
        private bool _lastCommConnected = false;

        // CustomTemplate 自定义通道
        private string _rcsLabel = "RCS";
        private string _sasLabel = "SAS";
        private string _precLabel = "FINE";
        private string _pointingTitle = "POINTING MODE";
        private string _comm1 = "SPX";
        private string _comm2 = "TDRS";
        private string _comm3 = "ISS";

        private void ParseCustomTemplate(string tpl)
        {
            _rcsLabel = "RCS";
            _sasLabel = "SAS";
            _precLabel = "FINE";
            _pointingTitle = "POINTING MODE";
            _comm1 = "SPX";
            _comm2 = "TDRS";
            _comm3 = "ISS";
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
                    case "RCS_LABEL": _rcsLabel = v; break;
                    case "SAS_LABEL": _sasLabel = v; break;
                    case "PREC_LABEL": _precLabel = v; break;
                    case "POINTING_TITLE": _pointingTitle = v; break;
                    case "COMM1": _comm1 = v; break;
                    case "COMM2": _comm2 = v; break;
                    case "COMM3": _comm3 = v; break;
                }
            }
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ParseCustomTemplate(config?.CustomTemplate);

            Vector2 panelSize = new Vector2(420f * s, 38f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 1. 左侧药丸按钮群 (RCS, SAS, MODE, PREC)
            float startX = -panelSize.x * 0.5f + 14f * s;
            float btnW = 44f * s;
            float btnH = 22f * s;
            float gap = 6f * s;

            // RCS
            _rcsBtn = CreatePillButton("Btn_RCS", startX + btnW * 0.5f, 0f, btnW, btnH, s, theme, _rcsLabel, OnToggleRCS, out _rcsImg, out _rcsOutline, out _rcsText);

            // SAS
            startX += btnW + gap;
            _sasBtn = CreatePillButton("Btn_SAS", startX + btnW * 0.5f, 0f, btnW, btnH, s, theme, _sasLabel, OnToggleSAS, out _sasImg, out _sasOutline, out _sasText);

            // SPEED MODE (SURF / OBT / TGT)
            startX += btnW + gap;
            float modeW = 54f * s;
            _modeBtn = CreatePillButton("Btn_MODE", startX + modeW * 0.5f, 0f, modeW, btnH, s, theme, "ORBIT", OnCycleMode, out _modeImg, out _modeOutline, out _modeText);

            // PRECISION
            startX += modeW + gap;
            float precW = 46f * s;
            _precBtn = CreatePillButton("Btn_PREC", startX + precW * 0.5f, 0f, precW, btnH, s, theme, _precLabel, OnTogglePrec, out _precImg, out _precOutline, out _precText);

            // 垂直分割线
            startX += precW + 12f * s;
            UIFactory.CreatePanel(transform, "MidSep", new Vector2(1f * s, panelSize.y * 0.55f), new Vector2(startX, 0f), style.GetLineColor(LineWeight.Faint, theme));

            // 2. 右侧当前指向模式 (POINTING MODE)
            startX += 12f * s;
            _pointingLabel = UIFactory.CreateText(transform, "PointingLabel", _pointingTitle, Mathf.RoundToInt(7f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
            _pointingLabel.rectTransform.anchoredPosition = new Vector2(startX + 45f * s, 7f * s);
            _pointingLabel.rectTransform.sizeDelta = new Vector2(90f * s, 10f * s);

            _pointingValue = UIFactory.CreateText(transform, "PointingValue", "EARTH POINTING", Mathf.RoundToInt(9.5f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            _pointingValue.rectTransform.anchoredPosition = new Vector2(startX + 45f * s, -6f * s);
            _pointingValue.rectTransform.sizeDelta = new Vector2(90f * s, 14f * s);

            // 3. 极右侧深空与空间站链路微标 (SPX / TDRS / ISS)
            float commX = panelSize.x * 0.5f - 18f * s;
            _commIss = CreateCommTag("Comm_ISS", commX, 0f, s, theme, _comm3);
            commX -= 36f * s;
            _commTdrs = CreateCommTag("Comm_TDRS", commX, 0f, s, theme, _comm2);
            commX -= 38f * s;
            _commSpx = CreateCommTag("Comm_SPX", commX, 0f, s, theme, _comm1);
        }

        private Button CreatePillButton(string name, float x, float y, float width, float height, float s, ThemeConfig theme, string label,
            UnityEngine.Events.UnityAction onClick, out Image bg, out Outline outline, out Text text)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Vector2 size = new Vector2(width, height);

            Button btn = UIFactory.CreateButton(transform, name, size, new Vector2(x, y), onClick);
            bg = btn.GetComponent<Image>();
            bg.color = style.GetSurfaceColor(SurfaceStyleRole.Control, theme);

            outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = style.GetLineColor(LineWeight.Subtle, theme);
            outline.effectDistance = new Vector2(1f * s, 1f * s);

            text = UIFactory.CreateText(btn.transform, "Label", label, Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            text.rectTransform.sizeDelta = size;
            text.rectTransform.anchoredPosition = Vector2.zero;

            return btn;
        }

        private Text CreateCommTag(string name, float x, float y, float s, ThemeConfig theme, string text)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Text t = UIFactory.CreateText(transform, name, text, Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Muted, theme));
            t.rectTransform.anchoredPosition = new Vector2(x, y);
            t.rectTransform.sizeDelta = new Vector2(34f * s, 16f * s);
            return t;
        }

        private void OnToggleRCS()
        {
            FlightTelemetryContext.Current?.ToggleRCS();
        }

        private void OnToggleSAS()
        {
            FlightTelemetryContext.Current?.ToggleSAS();
        }

        private void OnCycleMode()
        {
            FlightTelemetryContext.Current?.CycleSpeedMode();
        }

        private void OnTogglePrec()
        {
            FlightTelemetryContext.Current?.TogglePrecisionMode();
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig th = ThemeManager.Instance.CurrentTheme;
            WidgetStyleManager st = WidgetStyleManager.Instance;

            // 1. RCS 按钮状态
            if (telemetry.IsRCSEnabled != _lastRcs)
            {
                _lastRcs = telemetry.IsRCSEnabled;
                UpdatePillAppearance(_rcsImg, _rcsOutline, _rcsText, _lastRcs, th);
            }

            // 2. SAS 按钮状态
            if (telemetry.IsSASEnabled != _lastSas)
            {
                _lastSas = telemetry.IsSASEnabled;
                UpdatePillAppearance(_sasImg, _sasOutline, _sasText, _lastSas, th);
            }

            // 3. 速度参考系模式 (SURF / ORBIT / TARGET)
            string modeName = telemetry.SpeedModeName?.ToUpperInvariant() ?? "ORBIT";
            if (modeName != _lastModeStr && _modeText != null)
            {
                _lastModeStr = modeName;
                _modeText.text = modeName;
            }

            // 4. 精细控制
            if (telemetry.IsPrecisionControl != _lastPrec)
            {
                _lastPrec = telemetry.IsPrecisionControl;
                UpdatePillAppearance(_precImg, _precOutline, _precText, _lastPrec, th);
            }

            // 5. 当前指向模式
            string pointing = GetPointingModeDescription(telemetry);
            if (pointing != _lastPointing && _pointingValue != null)
            {
                _lastPointing = pointing;
                _pointingValue.text = pointing;
            }

            // 6. 通信链路状态
            bool conn = telemetry.IsConnected;
            if (conn != _lastCommConnected)
            {
                _lastCommConnected = conn;
                TextStyleRole role = conn ? TextStyleRole.Accent : TextStyleRole.Muted;
                ApplyText(_commSpx, role, th);
                ApplyText(_commTdrs, role, th);
                ApplyText(_commIss, role, th);
            }
        }

        private void UpdatePillAppearance(Image bg, Outline border, Text label, bool isActive, ThemeConfig theme)
        {
            if (bg == null || label == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            ApplyButton(null, bg, label, isActive ? ButtonVisualRole.ActiveToggle : ButtonVisualRole.Normal, isActive, theme);
            if (border != null)
            {
                border.effectColor = isActive ? theme.AccentPrimary : style.GetLineColor(LineWeight.Subtle, theme);
            }
        }

        private static string GetPointingModeDescription(IFlightTelemetry t)
        {
            if (!t.IsSASEnabled) return "FREE MANUAL";
            switch (t.CurrentSASMode)
            {
                case FlightSASMode.Prograde: return "PROGRADE";
                case FlightSASMode.Retrograde: return "RETROGRADE";
                case FlightSASMode.Normal: return "NORMAL";
                case FlightSASMode.Antinormal: return "ANTINORMAL";
                case FlightSASMode.RadialIn: return "RADIAL IN";
                case FlightSASMode.RadialOut: return "RADIAL OUT";
                case FlightSASMode.Target: return "TARGET LOCK";
                case FlightSASMode.AntiTarget: return "ANTI-TARGET";
                case FlightSASMode.Maneuver: return "MANEUVER NODE";
                default: return "STABILITY HOLD";
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);

            UpdatePillAppearance(_rcsImg, _rcsOutline, _rcsText, _lastRcs, theme);
            UpdatePillAppearance(_sasImg, _sasOutline, _sasText, _lastSas, theme);
            UpdatePillAppearance(_precImg, _precOutline, _precText, _lastPrec, theme);

            if (_modeImg != null) _modeImg.color = style.GetSurfaceColor(SurfaceStyleRole.Control, theme);
            if (_modeOutline != null) _modeOutline.effectColor = style.GetLineColor(LineWeight.Subtle, theme);
            if (_modeText != null) _modeText.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

            if (_pointingLabel != null) ApplyText(_pointingLabel, TextStyleRole.Label, theme);
            if (_pointingValue != null) ApplyText(_pointingValue, TextStyleRole.Accent, theme);

            TextStyleRole commRole = _lastCommConnected ? TextStyleRole.Accent : TextStyleRole.Muted;
            ApplyText(_commSpx, commRole, theme);
            ApplyText(_commTdrs, commRole, theme);
            ApplyText(_commIss, commRole, theme);
        }

        protected override void OnDestroy()
        {
            _rcsBtn?.onClick.RemoveAllListeners();
            _sasBtn?.onClick.RemoveAllListeners();
            _modeBtn?.onClick.RemoveAllListeners();
            _precBtn?.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
