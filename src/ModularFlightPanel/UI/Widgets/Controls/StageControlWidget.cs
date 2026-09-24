using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化多功能航电分级与飞行姿态操纵台 (Modern Flight Control & Staging Suite)
    /// 
    /// 完美替代 KSP 原版左下角粗糙方框，整合：
    /// 1. 现代化三位数字分级滚轮显示器 (Digital Stage Counter: STAGE 02) 与单级 Δv / TWR 遥测
    /// 2. 分级状态指示 LED (RDY 绿色就绪 / LCK 红色锁定) 与交互式分级触发键 + 安全锁按键
    /// 3. 三轴舵面实时偏转量与机械微调标尺 (Pitch 俯仰, Roll 滚转, Yaw 偏航，双向零位标线与配平游标)
    /// 4. 当前分级活跃推进剂监测条 (动态识别液氢/甲烷/液态燃料/固推名称与剩余百分比，三段式预警变色)
    /// 5. 快速飞行模式与微调切换 (NORM/PREC 微调模式, STG/DCK 分级/对接口模式, 原版左下角隐藏切换)
    /// </summary>
    public class StageControlWidget : BaseFlightWidget
    {
        private Image _panelBg;
        private Outline _panelOutline;

        // 顶栏控件
        private Text _titleText;
        private Button _lockBtn;
        private Text _lockBtnText;
        private Button _fireBtn;
        private Text _fireBtnText;

        // 分级读数区
        private Text _stageNumText;
        private Text _stageDvText;
        private Text _stageTwrEngText;

        // 三轴操纵量标尺 (Pitch, Roll, Yaw)
        private RectTransform _pitchFillRt;
        private Image _pitchFillImg;
        private RectTransform _pitchTrimRt;
        private Text _pitchValText;

        private RectTransform _rollFillRt;
        private Image _rollFillImg;
        private RectTransform _rollTrimRt;
        private Text _rollValText;

        private RectTransform _yawFillRt;
        private Image _yawFillImg;
        private RectTransform _yawTrimRt;
        private Text _yawValText;

        // 分级推进剂指示条
        private Text _propNameText;
        private Text _propPctText;
        private RectTransform _propFillRt;
        private Image _propFillImg;

        // 底部快捷切换按键
        private Button _precBtn;
        private Text _precText;
        private Image _precImg;

        private Button _modeBtn;
        private Text _modeText;
        private Image _modeImg;

        private Button _stockToggleBtn;
        private Text _stockToggleText;
        private bool _stockHidden = true;

        private const float TrackWidth = 104f;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(204f * s, 172f * s);
            RectTransform.sizeDelta = panelSize;

            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            // 1. 主背板与淡框 (全息极简 HUD 风格)
            GameObject panel = UIFactory.CreatePanel(transform, "StageControlPanel", panelSize, Vector2.zero, Color.clear,
                WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Ghost), 1f * s);
            _panelBg = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();
            _panelOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 2. 顶栏：标题、安全锁、分级触发
            float topY = panelSize.y * 0.5f - 14f * s;

            _titleText = UIFactory.CreateText(panel.transform, "Title", "STAGE CONTROL",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(90f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 52f * s, topY);

            // 安全锁按键 (Alt+L) - 极简药丸
            _lockBtn = UIFactory.CreateButton(panel.transform, "Btn_Lock", new Vector2(44f * s, 16f * s),
                new Vector2(panelSize.x * 0.5f - 68f * s, topY), OnToggleLock);
            _lockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _lockBtnText = UIFactory.CreateText(_lockBtn.transform, "Text", "ARMED",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _lockBtnText.fontStyle = FontStyle.Bold;
            _lockBtnText.GetComponent<RectTransform>().sizeDelta = new Vector2(44f * s, 16f * s);

            // 分级触发键 (STAGE ▶) - 醒目药丸
            _fireBtn = UIFactory.CreateButton(panel.transform, "Btn_Fire", new Vector2(42f * s, 16f * s),
                new Vector2(panelSize.x * 0.5f - 23f * s, topY), OnFireStage);
            _fireBtn.GetComponent<Image>().color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution);
            _fireBtnText = UIFactory.CreateText(_fireBtn.transform, "Text", "STAGE ▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.Warning));
            _fireBtnText.fontStyle = FontStyle.Bold;
            _fireBtnText.GetComponent<RectTransform>().sizeDelta = new Vector2(42f * s, 16f * s);

            // 3. 数字分级微章与单级 Δv / TWR
            float stageY = panelSize.y * 0.5f - 38f * s;
            GameObject stageBadge = UIFactory.CreatePanel(panel.transform, "StageBadge",
                new Vector2(28f * s, 18f * s), new Vector2(-panelSize.x * 0.5f + 24f * s, stageY),
                primaryAccent);

            _stageNumText = UIFactory.CreateText(stageBadge.transform, "StageNum", "S02",
                Mathf.Max(9, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.InverseOnAccent, theme));
            _stageNumText.fontStyle = FontStyle.Bold;
            RectTransform stageNumRt = _stageNumText.GetComponent<RectTransform>();
            stageNumRt.sizeDelta = new Vector2(28f * s, 18f * s);
            stageNumRt.anchoredPosition = Vector2.zero;

            // 级间性能遥测 (Δv, TWR, ENGINES)
            _stageDvText = UIFactory.CreateText(panel.transform, "StageDv", "2,350 m/s",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, primaryAccent);
            _stageDvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = _stageDvText.GetComponent<RectTransform>();
            dvRt.sizeDelta = new Vector2(140f * s, 16f * s);
            dvRt.anchoredPosition = new Vector2(20f * s, stageY + 6f * s);

            _stageTwrEngText = UIFactory.CreateText(panel.transform, "StageTwrEng", "01:24 · 1.45 TWR · 4 ENG",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform twrRt = _stageTwrEngText.GetComponent<RectTransform>();
            twrRt.sizeDelta = new Vector2(140f * s, 14f * s);
            twrRt.anchoredPosition = new Vector2(20f * s, stageY - 7f * s);

            // 4. 三轴姿态操纵量仪表 (Pitch, Roll, Yaw)
            float axisStartY = stageY - 24f * s;
            float axisSpacing = 16f * s;

            BuildAxisMeter(panel.transform, "Pitch", "PITCH", axisStartY, s, secondaryAccent,
                out _pitchFillRt, out _pitchFillImg, out _pitchTrimRt, out _pitchValText);

            BuildAxisMeter(panel.transform, "Roll", "ROLL", axisStartY - axisSpacing, s, secondaryAccent,
                out _rollFillRt, out _rollFillImg, out _rollTrimRt, out _rollValText);

            BuildAxisMeter(panel.transform, "Yaw", "YAW", axisStartY - axisSpacing * 2f, s, secondaryAccent,
                out _yawFillRt, out _yawFillImg, out _yawTrimRt, out _yawValText);

            // 5. 分级推进剂指示条
            float propY = axisStartY - axisSpacing * 2f - 20f * s;

            _propNameText = UIFactory.CreateText(panel.transform, "PropName", "PROP: LH2 / OX",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform pNameRt = _propNameText.GetComponent<RectTransform>();
            pNameRt.sizeDelta = new Vector2(120f * s, 12f * s);
            pNameRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 68f * s, propY + 7f * s);

            _propPctText = UIFactory.CreateText(panel.transform, "PropPct", "100.0%",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, primaryAccent);
            RectTransform pPctRt = _propPctText.GetComponent<RectTransform>();
            pPctRt.sizeDelta = new Vector2(60f * s, 12f * s);
            pPctRt.anchoredPosition = new Vector2(panelSize.x * 0.5f - 38f * s, propY + 7f * s);

            // 推进剂槽微线
            GameObject propTrack = UIFactory.CreatePanel(panel.transform, "PropTrack",
                new Vector2(184f * s, 2.5f * s), new Vector2(0f, propY - 2f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset));

            GameObject propFill = UIFactory.CreatePanel(propTrack.transform, "Fill",
                new Vector2(184f * s, 2.5f * s), Vector2.zero, primaryAccent);
            _propFillRt = propFill.GetComponent<RectTransform>();
            _propFillRt.anchorMin = new Vector2(0f, 0.5f);
            _propFillRt.anchorMax = new Vector2(0f, 0.5f);
            _propFillRt.pivot = new Vector2(0f, 0.5f);
            _propFillRt.anchoredPosition = Vector2.zero;
            _propFillImg = propFill.GetComponent<Image>();

            // 6. 底部快捷功能条 (PREC 微调 / MODE 对接 / 隐藏原版) - 极简药丸
            float bottomY = -panelSize.y * 0.5f + 12f * s;
            Vector2 miniBtnSize = new Vector2(58f * s, 15f * s);

            _precBtn = UIFactory.CreateButton(panel.transform, "Btn_Prec", miniBtnSize,
                new Vector2(-60f * s, bottomY), OnTogglePrecision);
            _precImg = _precBtn.GetComponent<Image>();
            _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _precText = UIFactory.CreateText(_precBtn.transform, "Text", "NORM",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _precText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;

            _modeBtn = UIFactory.CreateButton(panel.transform, "Btn_Mode", miniBtnSize,
                new Vector2(0f, bottomY), OnToggleMode);
            _modeImg = _modeBtn.GetComponent<Image>();
            _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeText = UIFactory.CreateText(_modeBtn.transform, "Text", "STG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _modeText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;

            _stockToggleBtn = UIFactory.CreateButton(panel.transform, "Btn_Stock", miniBtnSize,
                new Vector2(60f * s, bottomY), OnToggleStockVisibility);
            _stockToggleBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stockToggleText = UIFactory.CreateText(_stockToggleBtn.transform, "Text", "HIDE STOCK",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));
            _stockToggleText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;

            // 初始化时默认执行静默隐藏原版左下角，保障现代化玻璃仪表无遮挡生效
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden);

            ApplyTheme(theme);
        }

        private void BuildAxisMeter(Transform parent, string name, string label, float yPos, float s, Color accent,
            out RectTransform fillRt, out Image fillImg, out RectTransform trimRt, out Text valText)
        {
            // 轴名称 (PITCH / ROLL / YAW)
            Text lbl = UIFactory.CreateText(parent, name + "_Label", label,
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, accent);
            RectTransform lblRt = lbl.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(36f * s, 14f * s);
            lblRt.anchoredPosition = new Vector2(-76f * s, yPos);

            // 标尺轨道背景 (宽 104px, 高 3px) - 极简细线
            GameObject track = UIFactory.CreatePanel(parent, name + "_Track",
                new Vector2(TrackWidth * s, 3f * s), new Vector2(4f * s, yPos),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset));

            // 零位中心基准刻线
            UIFactory.CreatePanel(track.transform, "CenterTick",
                new Vector2(1f * s, 6f * s), Vector2.zero, WidgetStyleManager.Weighted(accent, LineWeight.Heavy));

            // 双向偏转填充条 (Bidirectional Fill)
            GameObject fill = UIFactory.CreatePanel(track.transform, "Fill",
                new Vector2(0f, 3f * s), Vector2.zero, accent);
            fillRt = fill.GetComponent<RectTransform>();
            fillImg = fill.GetComponent<Image>();

            // 配平指示微标 (Trim Pip)
            GameObject trim = UIFactory.CreatePanel(track.transform, "TrimPip",
                new Vector2(2f * s, 6f * s), Vector2.zero, WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Solid));
            trimRt = trim.GetComponent<RectTransform>();

            // 偏转读数百分比 (+24%)
            valText = UIFactory.CreateText(parent, name + "_Val", "0%",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, WidgetStyleManager.Text(TextStyleRole.PrimaryValue));
            RectTransform valRt = valText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(34f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(80f * s, yPos);
        }

        private void OnToggleLock()
        {
            FlightTelemetryContext.Current?.ToggleStageLock();
        }

        private void OnFireStage()
        {
            FlightTelemetryContext.Current?.ActivateNextStage();
        }

        private void OnTogglePrecision()
        {
            FlightTelemetryContext.Current?.TogglePrecisionMode();
        }

        private void OnToggleMode()
        {
            FlightTelemetryContext.Current?.ToggleFlightMode();
        }

        private void OnToggleStockVisibility()
        {
            _stockHidden = !_stockHidden;
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden);
            if (_stockToggleText != null)
            {
                _stockToggleText.text = _stockHidden ? "HIDE STOCK" : "SHOW STOCK";
            }
        }

        private string _lastStageNumStr;
        private string _lastStageDvStr;
        private string _lastStageTwrStr;
        private string _lastPropNameStr;
        private bool _lastLockedState = false;
        private float _lastPropFrac = -1f;

        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;
            float s = CurrentDpiScale;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_cachedTheme);

            // 1. 分级安全锁与就绪灯
            bool isLocked = telem.IsStageLocked;
            bool hasStages = telem.CurrentStage > 0;


            if (_lockBtnText != null && (_lockBtnText.text == null || isLocked != _lastLockedState))
            {
                _lastLockedState = isLocked;
                string armedLabel = GetTemplateChannel("ARMED_LABEL", "ARMED");
                string lockedLabel = GetTemplateChannel("LOCKED_LABEL", "LOCKED");
                _lockBtnText.text = isLocked ? lockedLabel : armedLabel;
                ApplyText(_lockBtnText, isLocked ? TextStyleRole.Danger : TextStyleRole.Accent, theme);
            }

            // 2. 分级数字读数与性能参数 (Dirty Checking)
            if (_stageNumText != null)
            {
                string sNumStr = $"S{telem.CurrentStage:D2}";
                if (sNumStr != _lastStageNumStr)
                {
                    _lastStageNumStr = sNumStr;
                    _stageNumText.text = sNumStr;
                }
            }

            if (_stageDvText != null)
            {
                double dv = telem.StageDeltaV;
                string dvStr = $"{dv:N0} m/s";
                if (dvStr != _lastStageDvStr)
                {
                    _lastStageDvStr = dvStr;
                    _stageDvText.text = dvStr;
                }
            }

            if (_stageTwrEngText != null)
            {
                int burnSec = Mathf.Max(0, (int)telem.StageBurnTime);
                int m = burnSec / 60;
                int sec = burnSec % 60;
                string twrStr = telem.TWR > 0.01 
                    ? $"{m:00}:{sec:00} · {telem.TWR:F2} TWR · {telem.ActiveEngines} ENG" 
                    : $"{m:00}:{sec:00} · {telem.ActiveEngines} ENG";
                if (twrStr != _lastStageTwrStr)
                {
                    _lastStageTwrStr = twrStr;
                    _stageTwrEngText.text = twrStr;
                }
            }
            // 3. 三轴舵面实时偏转与配平
            UpdateAxisVisuals(telem.PitchInput, telem.PitchTrim, _pitchFillRt, _pitchTrimRt, _pitchValText, s);
            UpdateAxisVisuals(telem.RollInput, telem.RollTrim, _rollFillRt, _rollTrimRt, _rollValText, s);
            UpdateAxisVisuals(telem.YawInput, telem.YawTrim, _yawFillRt, _yawTrimRt, _yawValText, s);

            // 4. 分级推进剂指示条 (100% 语义驱动)
            float propFrac = Mathf.Clamp01(telem.StagePropellantFraction);
            if (_propNameText != null)
            {
                string pNameStr = $"PROP: {telem.StagePropellantName}";
                if (pNameStr != _lastPropNameStr)
                {
                    _lastPropNameStr = pNameStr;
                    _propNameText.text = pNameStr;
                }
            }

            if (Math.Abs(propFrac - _lastPropFrac) > 0.005f)
            {
                _lastPropFrac = propFrac;
                if (_propPctText != null)
                {
                    string pPctStr = $"{propFrac * 100f:F1}%";
                    _propPctText.text = pPctStr;
                    TextStyleRole pRole = (propFrac > 0.25f) ? TextStyleRole.PrimaryValue : ((propFrac > 0.10f) ? TextStyleRole.Warning : TextStyleRole.Danger);
                    ApplyText(_propPctText, pRole, theme);
                }
                if (_propFillRt != null)
                {
                    _propFillRt.sizeDelta = new Vector2(184f * s * propFrac, 2.5f * s);
                }
                if (_propFillImg != null)
                {
                    MeterStyleRole fillRole = (propFrac > 0.25f) ? MeterStyleRole.Primary : ((propFrac > 0.10f) ? MeterStyleRole.Warning : MeterStyleRole.Danger);
                    _propFillImg.color = WidgetStyleManager.Meter(fillRole, theme);
                }
            }

            // 5. 底部微调模式与对接口模式
            if (_precText != null)
            {
                _precText.text = telem.IsPrecisionControl ? "PREC" : "NORM";
                ApplyText(_precText, telem.IsPrecisionControl ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
            }
            if (_modeText != null)
            {
                _modeText.text = telem.IsDockingMode ? "DCK" : "STG";
                ApplyText(_modeText, telem.IsDockingMode ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
            }
        }

        private void UpdateAxisVisuals(float input, float trim, RectTransform fillRt, RectTransform trimRt, Text valText, float s)
        {
            float halfWidth = (TrackWidth * 0.5f) * s;
            float clampedInput = Mathf.Clamp(input, -1f, 1f);
            float fillWidth = Mathf.Abs(clampedInput) * halfWidth;
            float fillCenterOffset = (clampedInput >= 0f) ? (fillWidth * 0.5f) : (-fillWidth * 0.5f);

            if (fillRt != null)
            {
                fillRt.sizeDelta = new Vector2(fillWidth, 3f * s);
                fillRt.anchoredPosition = new Vector2(fillCenterOffset, 0f);
            }

            if (trimRt != null)
            {
                float clampedTrim = Mathf.Clamp(trim, -1f, 1f);
                trimRt.anchoredPosition = new Vector2(clampedTrim * halfWidth, 0f);
            }

            if (valText != null)
            {
                int pct = Mathf.RoundToInt(clampedInput * 100f);
                valText.text = (pct > 0) ? $"+{pct}%" : $"{pct}%";
            }
        }

        private string GetTemplateChannel(string key, string fallback)
        {
            if (string.IsNullOrEmpty(Config?.CustomTemplate)) return fallback;
            string[] pairs = Config.CustomTemplate.Split(';');
            foreach (string pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            return fallback;
        }

        private ThemeConfig _cachedTheme;

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _cachedTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_panelBg != null) _panelBg.color = Color.clear;
            if (_panelOutline != null)
            {
                _panelOutline.enabled = true;
                _panelOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "STAGE CONTROL");
                ApplyText(_titleText, TextStyleRole.Cardinal, theme);
            }
            if (_stageDvText != null) ApplyText(_stageDvText, TextStyleRole.PrimaryValue, theme);
            if (_stageTwrEngText != null) ApplyText(_stageTwrEngText, TextStyleRole.SecondaryValue, theme);
            if (_propNameText != null) ApplyText(_propNameText, TextStyleRole.Muted, theme);
            if (_pitchFillImg != null) _pitchFillImg.color = theme.AccentPrimary;
            if (_rollFillImg != null) _rollFillImg.color = theme.AccentPrimary;
            if (_yawFillImg != null) _yawFillImg.color = theme.AccentPrimary;

            if (_lockBtn != null)
            {
                _lockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_lockBtnText != null) ApplyText(_lockBtnText, _lastLockedState ? TextStyleRole.Danger : TextStyleRole.Accent, theme);
            }
            if (_fireBtn != null)
            {
                _fireBtn.GetComponent<Image>().color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution);
                if (_fireBtnText != null)
                {
                    _fireBtnText.text = GetTemplateChannel("FIRE_LABEL", "STAGE ▶");
                    ApplyText(_fireBtnText, TextStyleRole.Warning, theme);
                }
            }
            if (_precBtn != null) _precBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_modeBtn != null) _modeBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockToggleBtn != null) _stockToggleBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_precText != null) ApplyText(_precText, TextStyleRole.SecondaryValue, theme);
            if (_modeText != null) ApplyText(_modeText, TextStyleRole.SecondaryValue, theme);
            if (_stockToggleText != null) ApplyText(_stockToggleText, TextStyleRole.SecondaryValue, theme);
        }

        protected override void OnDestroy()
        {
            if (_lockBtn != null) _lockBtn.onClick.RemoveAllListeners();
            if (_fireBtn != null) _fireBtn.onClick.RemoveAllListeners();
            if (_precBtn != null) _precBtn.onClick.RemoveAllListeners();
            if (_modeBtn != null) _modeBtn.onClick.RemoveAllListeners();
            if (_stockToggleBtn != null) _stockToggleBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
