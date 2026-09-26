using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 机动节点指示器 (Maneuver Node Indicator)
    /// ====================================================================================
    /// 实时呈现轨道机动规划、剩余变轨速度增量 (Δv)、节点倒计时 (T-NODE)、
    /// 预估燃烧时长 (BURN TIME)、提前点火倒计时 (BURN IN)，
    /// 并提供一键时间加速到点火点 (WARP) 与取消机动节点 (DEL) 交互操作。
    ///
    /// 严格遵守 MFP 规范：
    /// 1. 继承 BaseFlightWidget，显式声明 RefreshTier (Standard 30Hz)
    /// 2. 0 颜色字面量，100% 走 WidgetStyleManager 语义角色体系
    /// 3. 面向 IFlightTelemetry 与 TelemetryTokenEngine 纯 C# 契约，支持原版/Principia/MechJeb
    /// 4. 脏标记保护，DPI 动态缩放
    /// </summary>
    [FlightWidget("maneuver", "maneuver_node", Category = WidgetCategory.Navigation, DisplayName = "MANEUVER 轨道机动节点指示器", Description = "实时机动节点指示器：剩余 Delta-V 进度条、节点倒计时、燃烧时长与一键推演。", DefaultWidgetId = "core.maneuver", DefaultX = 440f, DefaultY = 160f, IsSingleton = true, ExactIds = new[] { "core.maneuver" })]
    public class ManeuverNodeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(200f, 105f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 声明式微控件
        public TextWidget HeaderTitle = TextWidget.Title("MANEUVER NODE");
        public TextWidget StatusBadge = TextWidget.Badge("STANDBY");

        // UI 层次节点
        private Image _bgImage;
        private Outline _bgOutline;

        private Text _headerTitleText;
        private Text _statusBadgeText;

        private Text _deltaVValueText;
        private Text _unitText;

        private Image _meterTrack;
        private Image _meterFill;

        private Text _tNodeLabel;
        private Text _tNodeValueText;

        private Text _burnTimeLabel;
        private Text _burnTimeValueText;

        private Text _burnInText;

        private Button _btnWarp;
        private Image _btnWarpImg;
        private Text _btnWarpText;

        private Button _btnDismiss;
        private Image _btnDismissImg;
        private Text _btnDismissText;

        // 通配符通道与模板
        private string _titleTemplate = "MANEUVER NODE";
        private string _deltaVToken = "{MN:DV}";
        private string _totalDvToken = "{MN:TOTAL_DV}";
        private string _tNodeToken = "{MN:TNODE}";
        private string _burnTimeToken = "{MN:BURN}";
        private string _timeToBurnToken = "{MN:BURNTIME}";
        private string _unitTemplate = "m/s";
        private string _warpTextTemplate = "WARP";
        private string _delTextTemplate = "DEL";

        // 运行时状态与脏标记缓存
        private bool _lastHasNode = false;
        private double _lastDeltaV = double.NaN;
        private double _lastTimeToNode = double.NaN;
        private double _lastBurnTime = double.NaN;
        private double _lastTimeToBurn = double.NaN;
        private CardStyleRole _currentCardRole = CardStyleRole.Normal;
        private string _lastFormattedDeltaV = string.Empty;
        private string _lastTitleStr = string.Empty;
        private string _lastBadgeStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 卡片包围盒 (200 x 105 px)
            Vector2 cardSize = BaseSize * s;
            RectTransform.sizeDelta = cardSize;

            _bgImage = CardBackground;
            _bgOutline = CardOutline;

            ParseCustomTemplate(config);

            // 2. 顶部 Header (标题 + 状态徽标已由微控件挂载)
            if (HeaderTitle != null && HeaderTitle.TextComponent != null)
            {
                HeaderTitle.Text = _titleTemplate;
                HeaderTitle.SetRole(TextStyleRole.Label);
                _headerTitleText = HeaderTitle.TextComponent;
                RectTransform titleRt = _headerTitleText.rectTransform;
                titleRt.pivot = new Vector2(0f, 0.5f);
                titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
                titleRt.sizeDelta = new Vector2(120f * s, 16f * s);
                titleRt.anchoredPosition = new Vector2(-92f * s, 42f * s);
            }
            else
            {
                _headerTitleText = UIFactory.CreateText(transform, "Header_Title", _titleTemplate, Mathf.RoundToInt(9.5f * s), TextAnchor.MiddleLeft,
                    style.GetTextColor(TextStyleRole.Label, theme));
                RectTransform titleRt = _headerTitleText.rectTransform;
                titleRt.pivot = new Vector2(0f, 0.5f);
                titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
                titleRt.sizeDelta = new Vector2(120f * s, 16f * s);
                titleRt.anchoredPosition = new Vector2(-92f * s, 42f * s);
            }

            if (StatusBadge != null && StatusBadge.TextComponent != null)
            {
                StatusBadge.Text = "STANDBY";
                StatusBadge.SetRole(TextStyleRole.SecondaryValue);
                _statusBadgeText = StatusBadge.TextComponent;
                RectTransform badgeRt = _statusBadgeText.rectTransform;
                badgeRt.pivot = new Vector2(1f, 0.5f);
                badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0.5f, 0.5f);
                badgeRt.sizeDelta = new Vector2(60f * s, 16f * s);
                badgeRt.anchoredPosition = new Vector2(92f * s, 42f * s);
            }
            else
            {
                _statusBadgeText = UIFactory.CreateText(transform, "Status_Badge", "STANDBY", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight,
                    style.GetTextColor(TextStyleRole.SecondaryValue, theme));
                RectTransform badgeRt = _statusBadgeText.rectTransform;
                badgeRt.pivot = new Vector2(1f, 0.5f);
                badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0.5f, 0.5f);
                badgeRt.sizeDelta = new Vector2(60f * s, 16f * s);
                badgeRt.anchoredPosition = new Vector2(92f * s, 42f * s);
            }

            // 3. 核心主读数 (剩余 Delta-V + 单位)
            _deltaVValueText = UIFactory.CreateText(transform, "DeltaV_Value", "---", Mathf.RoundToInt(22f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valRt = _deltaVValueText.rectTransform;
            valRt.pivot = new Vector2(0f, 0.5f);
            valRt.anchorMin = valRt.anchorMax = new Vector2(0.5f, 0.5f);
            valRt.sizeDelta = new Vector2(130f * s, 26f * s);
            valRt.anchoredPosition = new Vector2(-92f * s, 22f * s);

            _unitText = UIFactory.CreateText(transform, "Unit_Label", _unitTemplate, Mathf.RoundToInt(10f * s), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform unitRt = _unitText.rectTransform;
            unitRt.pivot = new Vector2(1f, 0.5f);
            unitRt.anchorMin = unitRt.anchorMax = new Vector2(0.5f, 0.5f);
            unitRt.sizeDelta = new Vector2(40f * s, 18f * s);
            unitRt.anchoredPosition = new Vector2(92f * s, 20f * s);

            // 4. 水平进度计量槽 (Progress Meter)
            GameObject trackGo = UIFactory.CreatePanel(transform, "Meter_Track",
                new Vector2(cardSize.x - 16f * s, 4f * s), new Vector2(0f, 6f * s),
                style.GetMeterColor(MeterStyleRole.Track, theme));
            _meterTrack = trackGo.GetComponent<Image>();

            GameObject fillGo = UIFactory.CreatePanel(trackGo.transform, "Meter_Fill", new Vector2(0f, 4f * s), Vector2.zero,
                style.GetMeterColor(MeterStyleRole.Primary, theme));
            _meterFill = fillGo.GetComponent<Image>();
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;

            // 5. 双倒计时栏 (T-NODE & BURN TIME)
            float colW = (cardSize.x - 20f * s) * 0.5f;

            // 左列：T-NODE
            _tNodeLabel = UIFactory.CreateText(transform, "TNode_Label", "T-NODE", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform tLblRt = _tNodeLabel.rectTransform;
            tLblRt.pivot = new Vector2(0f, 0.5f);
            tLblRt.anchorMin = tLblRt.anchorMax = new Vector2(0.5f, 0.5f);
            tLblRt.sizeDelta = new Vector2(colW, 12f * s);
            tLblRt.anchoredPosition = new Vector2(-92f * s, -8f * s);

            _tNodeValueText = UIFactory.CreateText(transform, "TNode_Value", "--:--", Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform tValRt = _tNodeValueText.rectTransform;
            tValRt.pivot = new Vector2(0f, 0.5f);
            tValRt.anchorMin = tValRt.anchorMax = new Vector2(0.5f, 0.5f);
            tValRt.sizeDelta = new Vector2(colW, 14f * s);
            tValRt.anchoredPosition = new Vector2(-92f * s, -22f * s);

            // 右列：BURN TIME
            _burnTimeLabel = UIFactory.CreateText(transform, "BurnTime_Label", "BURN TIME", Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform bLblRt = _burnTimeLabel.rectTransform;
            bLblRt.pivot = new Vector2(0f, 0.5f);
            bLblRt.anchorMin = bLblRt.anchorMax = new Vector2(0.5f, 0.5f);
            bLblRt.sizeDelta = new Vector2(colW, 12f * s);
            bLblRt.anchoredPosition = new Vector2(2f * s, -8f * s);

            _burnTimeValueText = UIFactory.CreateText(transform, "BurnTime_Value", "--:--", Mathf.RoundToInt(10.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform bValRt = _burnTimeValueText.rectTransform;
            bValRt.pivot = new Vector2(0f, 0.5f);
            bValRt.anchorMin = bValRt.anchorMax = new Vector2(0.5f, 0.5f);
            bValRt.sizeDelta = new Vector2(colW, 14f * s);
            bValRt.anchoredPosition = new Vector2(2f * s, -22f * s);

            // 6. 底栏 (提前点火倒计时 + WARP / DEL 动作按键)
            _burnInText = UIFactory.CreateText(transform, "BurnIn_Text", "BURN IN --:--", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform biRt = _burnInText.rectTransform;
            biRt.pivot = new Vector2(0f, 0.5f);
            biRt.anchorMin = biRt.anchorMax = new Vector2(0.5f, 0.5f);
            biRt.sizeDelta = new Vector2(95f * s, 18f * s);
            biRt.anchoredPosition = new Vector2(-92f * s, -40f * s);

            // 按键：WARP
            Vector2 warpBtnSize = new Vector2(36f * s, 16f * s);
            _btnWarp = UIFactory.CreateButton(transform, "Btn_Warp", warpBtnSize, new Vector2(34f * s, -40f * s), OnWarpClicked);
            _btnWarpImg = _btnWarp.GetComponent<Image>();
            _btnWarpText = UIFactory.CreateText(_btnWarp.transform, "Text", _warpTextTemplate, Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnWarpText.rectTransform.sizeDelta = warpBtnSize;
            _btnWarpText.rectTransform.anchoredPosition = Vector2.zero;

            // 按键：DEL
            Vector2 delBtnSize = new Vector2(30f * s, 16f * s);
            _btnDismiss = UIFactory.CreateButton(transform, "Btn_Del", delBtnSize, new Vector2(74f * s, -40f * s), OnDismissClicked);
            _btnDismissImg = _btnDismiss.GetComponent<Image>();
            _btnDismissText = UIFactory.CreateText(_btnDismiss.transform, "Text", _delTextTemplate, Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _btnDismissText.rectTransform.sizeDelta = delBtnSize;
            _btnDismissText.rectTransform.anchoredPosition = Vector2.zero;

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "卡片底板", gameObject, (t) => ApplyCard(_bgImage, _bgOutline, _currentCardRole, t)));
            this.Controls.Register(new WidgetHeaderControl("header", "标题栏", _headerTitleText != null ? _headerTitleText.gameObject : null, _headerTitleText, _statusBadgeText));
            this.Controls.Register(new WidgetReadoutControl("deltav_readout", "DeltaV读数", _deltaVValueText != null ? _deltaVValueText.gameObject : null, _deltaVValueText, _unitText, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetLinearBarControl("progress_meter", "变轨进度条", _meterTrack != null ? _meterTrack.gameObject : null, _meterFill, _meterTrack, MeterStyleRole.Primary, false));
            this.Controls.Register(new WidgetReadoutControl("tnode_readout", "节点倒计时", _tNodeValueText != null ? _tNodeValueText.gameObject : null, _tNodeValueText, _tNodeLabel, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetReadoutControl("burntime_readout", "燃烧时长", _burnTimeValueText != null ? _burnTimeValueText.gameObject : null, _burnTimeValueText, _burnTimeLabel, TextStyleRole.PrimaryValue));
            this.Controls.Register(new WidgetReadoutControl("burn_in_text", "点火倒计时", _burnInText != null ? _burnInText.gameObject : null, _burnInText, null, TextStyleRole.SecondaryValue));
            this.Controls.Register(new WidgetActionButtonControl("warp_btn", "推演按键", _btnWarp != null ? _btnWarp.gameObject : null, _btnWarp, _btnWarpText, _btnWarpImg, ButtonVisualRole.Normal));
            this.Controls.Register(new WidgetActionButtonControl("dismiss_btn", "取消按键", _btnDismiss != null ? _btnDismiss.gameObject : null, _btnDismiss, _btnDismissText, _btnDismissImg, ButtonVisualRole.Normal));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.DisplayName)) _titleTemplate = config.DisplayName.ToUpperInvariant();
                if (!string.IsNullOrEmpty(config.UnitLabel)) _unitTemplate = config.UnitLabel;
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
                    case "DV":
                    case "DV_TOKEN": _deltaVToken = v; break;
                    case "TOTAL_DV":
                    case "TOTAL_DV_TOKEN": _totalDvToken = v; break;
                    case "TNODE":
                    case "TNODE_TOKEN": _tNodeToken = v; break;
                    case "BURN":
                    case "BURN_TOKEN": _burnTimeToken = v; break;
                    case "WARP_LABEL": _warpTextTemplate = v; break;
                    case "DEL_LABEL": _delTextTemplate = v; break;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            ApplyText(_headerTitleText, TextStyleRole.Label, theme);
            ApplyText(_statusBadgeText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_deltaVValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_unitText, TextStyleRole.Unit, theme);

            ApplyMeter(_meterTrack, _meterFill, null, MeterStyleRole.Primary, theme);

            ApplyText(_tNodeLabel, TextStyleRole.Label, theme);
            ApplyText(_tNodeValueText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_burnTimeLabel, TextStyleRole.Label, theme);
            ApplyText(_burnTimeValueText, TextStyleRole.PrimaryValue, theme);

            ApplyText(_burnInText, TextStyleRole.SecondaryValue, theme);

            if (_btnWarp != null) ApplyButton(_btnWarp, _btnWarpImg, _btnWarpText, ButtonVisualRole.Normal, false, theme);
            if (_btnDismiss != null) ApplyButton(_btnDismiss, _btnDismissImg, _btnDismissText, ButtonVisualRole.Normal, false, theme);

            this.Controls.ApplyThemeToControls(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel || Config == null) return;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);

            // 标题动态求值
            string evalTitle = TelemetryTokenEngine.Evaluate(_titleTemplate, telemetry);
            if (evalTitle != _lastTitleStr)
            {
                _lastTitleStr = evalTitle;
                if (_headerTitleText != null) _headerTitleText.text = evalTitle;
            }

            // 1. 无机动节点时优雅降级为 STANDBY 待机模式
            if (!telemetry.HasManeuverNode)
            {
                ShowUnavailable(theme);
                return;
            }

            _lastHasNode = true;
            double dv = TelemetryTokenEngine.EvaluateNumeric(_deltaVToken, telemetry);
            if (double.IsNaN(dv)) dv = telemetry.ManeuverDeltaV;

            double totalDv = TelemetryTokenEngine.EvaluateNumeric(_totalDvToken, telemetry);
            if (double.IsNaN(totalDv)) totalDv = telemetry.ManeuverTotalDeltaV;

            double timeToNode = TelemetryTokenEngine.EvaluateNumeric(_tNodeToken, telemetry);
            if (double.IsNaN(timeToNode)) timeToNode = telemetry.ManeuverTimeToNode;

            double burnTime = TelemetryTokenEngine.EvaluateNumeric(_burnTimeToken, telemetry);
            if (double.IsNaN(burnTime)) burnTime = telemetry.ManeuverBurnTime;

            double timeToBurn = TelemetryTokenEngine.EvaluateNumeric(_timeToBurnToken, telemetry);
            if (double.IsNaN(timeToBurn)) timeToBurn = telemetry.ManeuverTimeToBurn;

            // 2. 脏标记检查：阈值对比
            double deltaThreshold = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            bool dvChanged = double.IsNaN(_lastDeltaV) || Math.Abs(dv - _lastDeltaV) > deltaThreshold;
            bool tNodeChanged = double.IsNaN(_lastTimeToNode) || Math.Abs(timeToNode - _lastTimeToNode) >= 0.5;
            bool burnTimeChanged = double.IsNaN(_lastBurnTime) || Math.Abs(burnTime - _lastBurnTime) >= 0.5;
            bool timeToBurnChanged = double.IsNaN(_lastTimeToBurn) || Math.Abs(timeToBurn - _lastTimeToBurn) >= 0.5;

            if (dvChanged)
            {
                _lastDeltaV = dv;
                string newDvStr = $"{dv:F1}";
                if (newDvStr != _lastFormattedDeltaV)
                {
                    _lastFormattedDeltaV = newDvStr;
                    _deltaVValueText.text = newDvStr;
                }

                float fraction = 1.0f;
                if (totalDv > 0.1)
                {
                    fraction = Mathf.Clamp01((float)(dv / totalDv));
                }
                float maxW = _meterTrack.rectTransform.sizeDelta.x;
                _meterFill.rectTransform.sizeDelta = new Vector2(maxW * fraction, _meterFill.rectTransform.sizeDelta.y);
            }

            if (tNodeChanged)
            {
                _lastTimeToNode = timeToNode;
                string prefix = timeToNode < 0 ? "T+ " : "T- ";
                _tNodeValueText.text = prefix + FormatDuration(Math.Abs(timeToNode));
            }

            if (burnTimeChanged)
            {
                _lastBurnTime = burnTime;
                _burnTimeValueText.text = FormatDuration(Math.Max(0.0, burnTime));
            }

            if (timeToBurnChanged)
            {
                _lastTimeToBurn = timeToBurn;
                if (timeToBurn <= 0.0)
                {
                    _burnInText.text = "BURNING!";
                }
                else
                {
                    _burnInText.text = "BURN IN " + FormatDuration(timeToBurn);
                }
            }

            // 3. 状态判定与主题响应
            CardStyleRole targetRole = CardStyleRole.Normal;
            string badgeText = "ARMED";

            if (timeToBurn <= 0.0 && dv > 0.1)
            {
                targetRole = CardStyleRole.Emphasized;
                badgeText = "BURNING";
            }
            else if (dv <= 0.1)
            {
                targetRole = CardStyleRole.Normal;
                badgeText = "COMPLETE";
            }

            if (_currentCardRole != targetRole)
            {
                _currentCardRole = targetRole;
                ApplyCard(_bgImage, _bgOutline, targetRole, theme);
                ApplyText(_deltaVValueText, TextStyleRole.PrimaryValue, theme);
                ApplyText(_statusBadgeText, targetRole == CardStyleRole.Emphasized ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);
            }

            if (badgeText != _lastBadgeStr)
            {
                _lastBadgeStr = badgeText;
                _statusBadgeText.text = badgeText;
            }

            if (_btnWarp != null && !_btnWarp.interactable) _btnWarp.interactable = true;
            if (_btnDismiss != null && !_btnDismiss.interactable) _btnDismiss.interactable = true;
        }

        private void ShowUnavailable(ThemeConfig theme)
        {
            if (!_lastHasNode && _lastFormattedDeltaV == "---") return;

            _lastHasNode = false;
            _lastDeltaV = double.NaN;
            _lastTimeToNode = double.NaN;
            _lastBurnTime = double.NaN;
            _lastTimeToBurn = double.NaN;
            _lastFormattedDeltaV = "---";

            _deltaVValueText.text = "---";
            _meterFill.rectTransform.sizeDelta = new Vector2(0f, _meterFill.rectTransform.sizeDelta.y);
            _tNodeValueText.text = "--:--";
            _burnTimeValueText.text = "--:--";
            _burnInText.text = "NO NODE";
            _statusBadgeText.text = "STANDBY";
            _lastBadgeStr = "STANDBY";

            if (_currentCardRole != CardStyleRole.Normal)
            {
                _currentCardRole = CardStyleRole.Normal;
                ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
                ApplyText(_deltaVValueText, TextStyleRole.PrimaryValue, theme);
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
            this.Controls.UnregisterAll();
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
