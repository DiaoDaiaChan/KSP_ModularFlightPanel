using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 载人龙飞船底部触控控制与深空网链路状态栏 (SpaceX Bottom Control & Comm Matrix)
    /// 包含：药丸触控开关 (RCS / SAS / SPEED MODE / PREC)、
    /// 当前航向指向状态 (POINTING MODE)、以及 SPX 地面站 / TDRS / ISS 通信链路矩阵。
    /// 严格遵循 MFP 规范，0 颜色字面量，10Hz Relaxed 阶梯刷新。
    /// </summary>
    [FlightWidget("spacex_bottom", "dragon_bottom", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 底部控制与链路操作栏", Description = "SpaceX 底部药丸触控条：RCS/SAS/参考系/精细控制开关、指向模式与通信链路矩阵。", DefaultWidgetId = "spacex.bottom", DefaultX = 0f, DefaultY = -150f, IsSingleton = true, ExactIds = new[] { "spacex.bottom" })]
    public class SpaceXBottomBarWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(420f, 38f);
        protected override bool AutoCreateCardFrame => true;
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
        private readonly Cached<bool> _lastRcs = new Cached<bool>(false);
        private readonly Cached<bool> _lastSas = new Cached<bool>(false);
        private readonly Cached<bool> _lastPrec = new Cached<bool>(false);
        private readonly Cached<string> _lastModeStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastPointing = new Cached<string>(string.Empty);
        private readonly Cached<bool> _lastSpxConn = new Cached<bool>(false);
        private readonly Cached<bool> _lastTdrsConn = new Cached<bool>(false);
        private readonly Cached<bool> _lastIssConn = new Cached<bool>(false);

        // CustomTemplate 自定义通道
        private string _rcsLabel = "RCS";
        private string _sasLabel = "SAS";
        private string _precLabel = "FINE";
        private string _pointingTitle = "POINTING MODE";
        private string _comm1 = "SPX";
        private string _comm2 = "TDRS";
        private string _comm3 = "ISS";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _rcsLabel = GetTemplateChannel("RCS_LABEL", "RCS");
            _sasLabel = GetTemplateChannel("SAS_LABEL", "SAS");
            _precLabel = GetTemplateChannel("PREC_LABEL", I18n.Tr("WIDGET_SPX_FINE", "精细"));
            _pointingTitle = GetTemplateChannel("POINTING_TITLE", I18n.Tr("WIDGET_SPX_POINTING_MODE", "指向模式"));
            _comm1 = GetTemplateChannel("COMM1", "SPX");
            _comm2 = GetTemplateChannel("COMM2", "TDRS");
            _comm3 = GetTemplateChannel("COMM3", "ISS");

            Vector2 panelSize = new Vector2(420f * s, 38f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = CardBackground;
            _outline = CardOutline;
            if (_outline != null)
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

            _pointingValue = UIFactory.CreateText(transform, "PointingValue", I18n.Tr("WIDGET_SPX_EARTH_POINTING", "对地指向"), Mathf.RoundToInt(9.5f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            _pointingValue.rectTransform.anchoredPosition = new Vector2(startX + 45f * s, -6f * s);
            _pointingValue.rectTransform.sizeDelta = new Vector2(90f * s, 14f * s);

            // 3. 极右侧深空与空间站链路微标 (SPX / TDRS / ISS)
            float commX = panelSize.x * 0.5f - 18f * s;
            _commIss = CreateCommTag("Comm_ISS", commX, 0f, s, theme, _comm3);
            commX -= 36f * s;
            _commTdrs = CreateCommTag("Comm_TDRS", commX, 0f, s, theme, _comm2);
            commX -= 38f * s;
            _commSpx = CreateCommTag("Comm_SPX", commX, 0f, s, theme, _comm1);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Bottom Bar Background", _bgImage.gameObject, "SpaceX底栏主卡片底板", t => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            this.Controls.Register(new WidgetActionButtonControl(_rcsBtn, _rcsText, null, ButtonVisualRole.ActiveToggle, "RCS Toggle", "RCS姿控动力开关药丸按钮"));
            this.Controls.Register(new WidgetActionButtonControl(_sasBtn, _sasText, null, ButtonVisualRole.ActiveToggle, "SAS Toggle", "SAS增稳系统开关药丸按钮"));
            this.Controls.Register(new WidgetActionButtonControl(_modeBtn, _modeText, null, ButtonVisualRole.Normal, "Speed Mode", "速度基准循环切换药丸按钮"));
            this.Controls.Register(new WidgetActionButtonControl(_precBtn, _precText, null, ButtonVisualRole.ActiveToggle, "Fine Controls", "精细控制微调模式药丸按钮"));
            this.Controls.Register(new WidgetReadoutControl("pointing_mode", "姿态指向保持模式读数", _pointingValue != null ? _pointingValue.gameObject : null, _pointingValue, _pointingLabel, TextStyleRole.Accent, "{CTRL_MODE}"));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "comm_spx", "Comm SpaceX", _commSpx != null ? _commSpx.gameObject : null));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "comm_tdrs", "Comm TDRS", _commTdrs != null ? _commTdrs.gameObject : null));
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "comm_iss", "Comm ISS", _commIss != null ? _commIss.gameObject : null));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
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

        private bool _dataRcs;
        private bool _dataSas;
        private string _dataModeName = "ORBIT";
        private bool _dataPrec;
        private string _dataPointing = string.Empty;
        private bool _dataSpxConn;
        private bool _dataTdrsConn;
        private bool _dataIssConn;
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

            // 1. RCS 按钮状态
            _dataRcs = telemetry.IsRCSEnabled;

            // 2. SAS 按钮状态
            _dataSas = telemetry.IsSASEnabled;

            // 3. 速度参考系模式 (SURF / ORBIT / TARGET)
            _dataModeName = telemetry.SpeedModeName?.ToUpperInvariant() ?? "ORBIT";

            // 4. 精细控制
            _dataPrec = telemetry.IsPrecisionControl;

            // 5. 当前指向模式
            _dataPointing = GetPointingModeDescription(telemetry);

            // 6. 通信链路独立状态 (SPX地面站、TDRS中继、ISS空间站/目标近距遥测)
            _dataSpxConn = telemetry.IsConnected;
            _dataTdrsConn = telemetry.IsConnected && (telemetry.ActiveCommLinks != null && telemetry.ActiveCommLinks.Count > 1 || telemetry.AntennaCount > 1 || telemetry.SignalRx > 0.4);
            _dataIssConn = telemetry.HasTarget && telemetry.TargetDistance < 80000.0;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_dataHasVessel) return;

            ThemeConfig th = context.Theme ?? ThemeManager.Instance?.CurrentTheme;

            // 1. RCS 按钮状态
            if (_lastRcs.Update(_dataRcs))
            {
                UpdatePillAppearance(_rcsImg, _rcsOutline, _rcsText, _lastRcs.Value, th);
            }

            // 2. SAS 按钮状态
            if (_lastSas.Update(_dataSas))
            {
                UpdatePillAppearance(_sasImg, _sasOutline, _sasText, _lastSas.Value, th);
            }

            // 3. 速度参考系模式
            if (_lastModeStr.Update(_dataModeName) && _modeText != null)
            {
                _modeText.text = _dataModeName;
            }

            // 4. 精细控制
            if (_lastPrec.Update(_dataPrec))
            {
                UpdatePillAppearance(_precImg, _precOutline, _precText, _lastPrec.Value, th);
            }

            // 5. 当前指向模式
            if (_lastPointing.Update(_dataPointing) && _pointingValue != null)
            {
                _pointingValue.text = _dataPointing;
            }

            // 6. 通信链路
            if (_lastSpxConn.Update(_dataSpxConn))
            {
                ApplyText(_commSpx, _dataSpxConn ? TextStyleRole.Accent : TextStyleRole.Muted, th);
            }

            if (_lastTdrsConn.Update(_dataTdrsConn))
            {
                ApplyText(_commTdrs, _dataTdrsConn ? TextStyleRole.Accent : TextStyleRole.Muted, th);
            }

            if (_lastIssConn.Update(_dataIssConn))
            {
                ApplyText(_commIss, _dataIssConn ? TextStyleRole.Accent : TextStyleRole.Muted, th);
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
            if (!t.IsSASEnabled) return I18n.Tr("WIDGET_SPX_POINT_FREE_MANUAL", "自由手动");
            switch (t.CurrentSASMode)
            {
                case FlightSASMode.Prograde: return I18n.Tr("SAS_MODE_PROGRADE", "顺行");
                case FlightSASMode.Retrograde: return I18n.Tr("SAS_MODE_RETROGRADE", "逆行");
                case FlightSASMode.Normal: return I18n.Tr("SAS_MODE_NORMAL", "法向");
                case FlightSASMode.Antinormal: return I18n.Tr("SAS_MODE_ANTINORMAL", "反法向");
                case FlightSASMode.RadialIn: return I18n.Tr("SAS_MODE_RADIAL_IN", "径向内");
                case FlightSASMode.RadialOut: return I18n.Tr("SAS_MODE_RADIAL_OUT", "径向外");
                case FlightSASMode.Target: return I18n.Tr("WIDGET_SPX_POINT_TARGET_LOCK", "目标锁定");
                case FlightSASMode.AntiTarget: return I18n.Tr("WIDGET_SPX_POINT_ANTI_TARGET", "反目标");
                case FlightSASMode.Maneuver: return I18n.Tr("WIDGET_NAV_MANEUVER_NODE", "机动节点");
                default: return I18n.Tr("WIDGET_SPX_POINT_STABILITY", "稳定保持");
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);

            UpdatePillAppearance(_rcsImg, _rcsOutline, _rcsText, _lastRcs.Value, theme);
            UpdatePillAppearance(_sasImg, _sasOutline, _sasText, _lastSas.Value, theme);
            UpdatePillAppearance(_precImg, _precOutline, _precText, _lastPrec.Value, theme);

            if (_modeImg != null) _modeImg.color = style.GetSurfaceColor(SurfaceStyleRole.Control, theme);
            if (_modeOutline != null) _modeOutline.effectColor = style.GetLineColor(LineWeight.Subtle, theme);
            if (_modeText != null) _modeText.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

            if (_pointingLabel != null) ApplyText(_pointingLabel, TextStyleRole.Label, theme);
            if (_pointingValue != null) ApplyText(_pointingValue, TextStyleRole.Accent, theme);

            ApplyText(_commSpx, _lastSpxConn.Value ? TextStyleRole.Accent : TextStyleRole.Muted, theme);
            ApplyText(_commTdrs, _lastTdrsConn.Value ? TextStyleRole.Accent : TextStyleRole.Muted, theme);
            ApplyText(_commIss, _lastIssConn.Value ? TextStyleRole.Accent : TextStyleRole.Muted, theme);
        }

        protected override void OnDestroy()
        {
            _rcsBtn?.onClick.RemoveAllListeners();
            _sasBtn?.onClick.RemoveAllListeners();
            _modeBtn?.onClick.RemoveAllListeners();
            _precBtn?.onClick.RemoveAllListeners();
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
