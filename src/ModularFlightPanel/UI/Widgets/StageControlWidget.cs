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

        // 顶部警示条与标题
        private Image _topStripe;
        private Text _titleText;
        private Image _stageLed;
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

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(204f * s, 186f * s);
            RectTransform.sizeDelta = panelSize;

            Color bgCol = (theme != null) ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.90f);
            Color borderCol = (theme != null) ? (Color)theme.FrameBorderColor : Color.cyan;
            Color primaryAccent = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color secondaryAccent = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color textPrimary = (theme != null) ? (Color)theme.TextPrimaryColor : Color.white;

            // 1. 主背板
            GameObject panel = UIFactory.CreatePanel(transform, "StageControlPanel", panelSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
            _panelBg = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();

            // 2. 顶部赛博风格警示条 (Hazard Stripe)
            _topStripe = UIFactory.CreatePanel(panel.transform, "TopStripe", new Vector2(panelSize.x, 3f * s),
                new Vector2(0f, panelSize.y * 0.5f - 1.5f * s), new Color(0.95f, 0.65f, 0.10f, 0.85f)).GetComponent<Image>();

            // 3. 顶栏：标题、LED 就绪灯、安全锁、分级触发
            float topY = panelSize.y * 0.5f - 16f * s;

            _stageLed = UIFactory.CreatePanel(panel.transform, "StageLED", new Vector2(10f * s, 10f * s),
                new Vector2(-panelSize.x * 0.5f + 14f * s, topY), primaryAccent).GetComponent<Image>();
            var ledOutline = _stageLed.gameObject.AddComponent<Outline>();
            ledOutline.effectColor = new Color(primaryAccent.r, primaryAccent.g, primaryAccent.b, 0.5f);
            ledOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _titleText = UIFactory.CreateText(panel.transform, "Title", "FCS / STAGE",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform titleRt = _titleText.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(76f * s, 16f * s);
            titleRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 62f * s, topY);

            // 安全锁按键 (Alt+L)
            _lockBtn = UIFactory.CreateButton(panel.transform, "Btn_Lock", new Vector2(44f * s, 16f * s),
                new Vector2(panelSize.x * 0.5f - 68f * s, topY), OnToggleLock);
            _lockBtn.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var lockOut = _lockBtn.gameObject.AddComponent<Outline>();
            lockOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.4f);
            lockOut.effectDistance = new Vector2(1f * s, 1f * s);
            _lockBtnText = UIFactory.CreateText(_lockBtn.transform, "Text", "ARMED",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _lockBtnText.GetComponent<RectTransform>().sizeDelta = new Vector2(44f * s, 16f * s);

            // 分级触发键 (STAGE ▶)
            _fireBtn = UIFactory.CreateButton(panel.transform, "Btn_Fire", new Vector2(42f * s, 16f * s),
                new Vector2(panelSize.x * 0.5f - 23f * s, topY), OnFireStage);
            _fireBtn.GetComponent<Image>().color = new Color(0.18f, 0.12f, 0.05f, 0.95f);
            var fireOut = _fireBtn.gameObject.AddComponent<Outline>();
            fireOut.effectColor = new Color(0.95f, 0.65f, 0.10f, 0.65f);
            fireOut.effectDistance = new Vector2(1f * s, 1f * s);
            _fireBtnText = UIFactory.CreateText(_fireBtn.transform, "Text", "STAGE ▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, new Color(1.0f, 0.75f, 0.20f));
            _fireBtnText.GetComponent<RectTransform>().sizeDelta = new Vector2(42f * s, 16f * s);

            // 4. 数字分级读数与单级 Δv / TWR 框 (y ≈ +38)
            float stageY = panelSize.y * 0.5f - 42f * s;
            GameObject stageBox = UIFactory.CreatePanel(panel.transform, "StageCounterBox",
                new Vector2(62f * s, 30f * s), new Vector2(-panelSize.x * 0.5f + 40f * s, stageY),
                new Color(0.02f, 0.04f, 0.08f, 0.95f), secondaryAccent, 1f * s);

            Text stageSub = UIFactory.CreateText(stageBox.transform, "StageSub", "STAGE",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            RectTransform stageSubRt = stageSub.GetComponent<RectTransform>();
            stageSubRt.sizeDelta = new Vector2(60f * s, 10f * s);
            stageSubRt.anchoredPosition = new Vector2(0f, 9f * s);

            _stageNumText = UIFactory.CreateText(stageBox.transform, "StageNum", "02",
                Mathf.Max(12, Mathf.RoundToInt(16f * s)), TextAnchor.MiddleCenter, textPrimary);
            RectTransform stageNumRt = _stageNumText.GetComponent<RectTransform>();
            stageNumRt.sizeDelta = new Vector2(60f * s, 20f * s);
            stageNumRt.anchoredPosition = new Vector2(0f, -4f * s);

            // 级间性能遥测 (Δv, TWR, ENGINES)
            _stageDvText = UIFactory.CreateText(panel.transform, "StageDv", "Δv  2,350 m/s",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft, primaryAccent);
            RectTransform dvRt = _stageDvText.GetComponent<RectTransform>();
            dvRt.sizeDelta = new Vector2(110f * s, 14f * s);
            dvRt.anchoredPosition = new Vector2(24f * s, stageY + 7f * s);

            _stageTwrEngText = UIFactory.CreateText(panel.transform, "StageTwrEng", "TWR 1.45  |  2 ENG",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform twrRt = _stageTwrEngText.GetComponent<RectTransform>();
            twrRt.sizeDelta = new Vector2(110f * s, 14f * s);
            twrRt.anchoredPosition = new Vector2(24f * s, stageY - 7f * s);

            // 5. 三轴姿态操纵量仪表 (Pitch, Roll, Yaw) (y ≈ -2f, -22f, -42f)
            float axisStartY = stageY - 26f * s;
            float axisSpacing = 19f * s;

            BuildAxisMeter(panel.transform, "Pitch", "PITCH", axisStartY, s, secondaryAccent,
                out _pitchFillRt, out _pitchFillImg, out _pitchTrimRt, out _pitchValText);

            BuildAxisMeter(panel.transform, "Roll", "ROLL", axisStartY - axisSpacing, s, secondaryAccent,
                out _rollFillRt, out _rollFillImg, out _rollTrimRt, out _rollValText);

            BuildAxisMeter(panel.transform, "Yaw", "YAW", axisStartY - axisSpacing * 2f, s, secondaryAccent,
                out _yawFillRt, out _yawFillImg, out _yawTrimRt, out _yawValText);

            // 6. 分级推进剂指示条 (y ≈ -42f - 30f)
            float propY = axisStartY - axisSpacing * 2f - 22f * s;

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

            // 推进剂槽进度条
            GameObject propTrack = UIFactory.CreatePanel(panel.transform, "PropTrack",
                new Vector2(184f * s, 5f * s), new Vector2(0f, propY - 3f * s),
                new Color(0.06f, 0.09f, 0.14f, 0.90f), new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f), 1f * s);

            GameObject propFill = UIFactory.CreatePanel(propTrack.transform, "Fill",
                new Vector2(184f * s, 5f * s), Vector2.zero, primaryAccent);
            _propFillRt = propFill.GetComponent<RectTransform>();
            _propFillRt.anchorMin = new Vector2(0f, 0.5f);
            _propFillRt.anchorMax = new Vector2(0f, 0.5f);
            _propFillRt.pivot = new Vector2(0f, 0.5f);
            _propFillRt.anchoredPosition = Vector2.zero;
            _propFillImg = propFill.GetComponent<Image>();

            // 7. 底部快捷功能条 (PREC 微调 / MODE 对接 / 隐藏原版)
            float bottomY = -panelSize.y * 0.5f + 12f * s;
            Vector2 miniBtnSize = new Vector2(58f * s, 15f * s);

            // 微调控制 (NORM / PREC)
            _precBtn = UIFactory.CreateButton(panel.transform, "Btn_Prec", miniBtnSize,
                new Vector2(-60f * s, bottomY), OnTogglePrecision);
            _precImg = _precBtn.GetComponent<Image>();
            _precImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);
            var precOut = _precBtn.gameObject.AddComponent<Outline>();
            precOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            precOut.effectDistance = new Vector2(1f * s, 1f * s);
            _precText = UIFactory.CreateText(_precBtn.transform, "Text", "NORM",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _precText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;

            // 模式切换 (STG / DCK)
            _modeBtn = UIFactory.CreateButton(panel.transform, "Btn_Mode", miniBtnSize,
                new Vector2(0f, bottomY), OnToggleMode);
            _modeImg = _modeBtn.GetComponent<Image>();
            _modeImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);
            var modeOut = _modeBtn.gameObject.AddComponent<Outline>();
            modeOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            modeOut.effectDistance = new Vector2(1f * s, 1f * s);
            _modeText = UIFactory.CreateText(_modeBtn.transform, "Text", "STG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _modeText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;

            // 原版左下角隐藏切换 (STOCK)
            _stockToggleBtn = UIFactory.CreateButton(panel.transform, "Btn_Stock", miniBtnSize,
                new Vector2(60f * s, bottomY), OnToggleStockVisibility);
            _stockToggleBtn.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 0.95f);
            var stockOut = _stockToggleBtn.gameObject.AddComponent<Outline>();
            stockOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            stockOut.effectDistance = new Vector2(1f * s, 1f * s);
            _stockToggleText = UIFactory.CreateText(_stockToggleBtn.transform, "Text", "HIDE STOCK",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
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
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, accent);
            RectTransform lblRt = lbl.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(40f * s, 14f * s);
            lblRt.anchoredPosition = new Vector2(-74f * s, yPos);

            // 标尺轨道背景 (宽 104px, 高 6px)
            GameObject track = UIFactory.CreatePanel(parent, name + "_Track",
                new Vector2(TrackWidth * s, 6f * s), new Vector2(4f * s, yPos),
                new Color(0.05f, 0.08f, 0.13f, 0.95f), new Color(accent.r, accent.g, accent.b, 0.25f), 1f * s);

            // 零位中心基准刻线
            UIFactory.CreatePanel(track.transform, "CenterTick",
                new Vector2(1f * s, 8f * s), Vector2.zero, new Color(accent.r, accent.g, accent.b, 0.75f));

            // 双向偏转填充条 (Bidirectional Fill)
            GameObject fill = UIFactory.CreatePanel(track.transform, "Fill",
                new Vector2(0f, 6f * s), Vector2.zero, accent);
            fillRt = fill.GetComponent<RectTransform>();
            fillImg = fill.GetComponent<Image>();

            // 配平指示微标 (Trim Pip)
            GameObject trim = UIFactory.CreatePanel(track.transform, "TrimPip",
                new Vector2(2f * s, 8f * s), Vector2.zero, new Color(1.0f, 0.75f, 0.20f, 0.95f));
            trimRt = trim.GetComponent<RectTransform>();

            // 偏转读数百分比 (+24%)
            valText = UIFactory.CreateText(parent, name + "_Val", "0%",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, Color.white);
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

        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;
            float s = CurrentDpiScale;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color primaryAccent = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color secondaryAccent = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color cautionCol = new Color(0.95f, 0.75f, 0.15f);
            Color dangerCol = new Color(0.95f, 0.22f, 0.22f);

            // 1. 分级安全锁与就绪灯
            bool isLocked = telem.IsStageLocked;
            bool hasStages = telem.CurrentStage > 0;

            if (_stageLed != null)
            {
                if (isLocked) _stageLed.color = dangerCol;
                else if (hasStages) _stageLed.color = primaryAccent;
                else _stageLed.color = new Color(0.3f, 0.35f, 0.4f, 0.6f);
            }

            if (_lockBtnText != null)
            {
                _lockBtnText.text = isLocked ? "LOCKED" : "ARMED";
                _lockBtnText.color = isLocked ? dangerCol : primaryAccent;
            }

            // 2. 分级数字读数与性能参数
            if (_stageNumText != null)
            {
                _stageNumText.text = $"{telem.CurrentStage:D2}";
            }
            if (_stageDvText != null)
            {
                _stageDvText.text = $"Δv  {Mathf.RoundToInt((float)telem.StageDeltaV):N0} m/s";
            }
            if (_stageTwrEngText != null)
            {
                _stageTwrEngText.text = $"TWR {telem.TWR:F2}  |  {telem.ActiveEngines} ENG";
            }

            // 3. 三轴舵面实时偏转与配平
            UpdateAxisVisuals(telem.PitchInput, telem.PitchTrim, _pitchFillRt, _pitchTrimRt, _pitchValText, s);
            UpdateAxisVisuals(telem.RollInput, telem.RollTrim, _rollFillRt, _rollTrimRt, _rollValText, s);
            UpdateAxisVisuals(telem.YawInput, telem.YawTrim, _yawFillRt, _yawTrimRt, _yawValText, s);

            // 4. 分级推进剂指示条
            float propFrac = Mathf.Clamp01(telem.StagePropellantFraction);
            if (_propNameText != null)
            {
                _propNameText.text = $"PROP: {telem.StagePropellantName}";
            }
            if (_propPctText != null)
            {
                _propPctText.text = $"{propFrac * 100f:F1}%";
                _propPctText.color = (propFrac > 0.25f) ? primaryAccent : ((propFrac > 0.10f) ? cautionCol : dangerCol);
            }
            if (_propFillRt != null)
            {
                _propFillRt.sizeDelta = new Vector2(184f * s * propFrac, 5f * s);
            }
            if (_propFillImg != null)
            {
                _propFillImg.color = (propFrac > 0.25f) ? primaryAccent : ((propFrac > 0.10f) ? cautionCol : dangerCol);
            }

            // 5. 底部微调模式与对接口模式
            if (_precText != null)
            {
                _precText.text = telem.IsPrecisionControl ? "PREC" : "NORM";
                _precText.color = telem.IsPrecisionControl ? cautionCol : secondaryAccent;
            }
            if (_modeText != null)
            {
                _modeText.text = telem.IsDockingMode ? "DCK" : "STG";
                _modeText.color = telem.IsDockingMode ? cautionCol : secondaryAccent;
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
                fillRt.sizeDelta = new Vector2(fillWidth, 6f * s);
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

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            float s = CurrentDpiScale;

            if (_panelBg != null) _panelBg.color = theme.FrameBgColor;
            if (_panelOutline != null)
            {
                _panelOutline.effectColor = theme.FrameBorderColor;
                _panelOutline.effectDistance = new Vector2(1.2f * s, 1.2f * s);
            }
            if (_titleText != null) _titleText.color = theme.AccentSecondary;
            if (_stageDvText != null) _stageDvText.color = theme.AccentPrimary;
            if (_stageTwrEngText != null) _stageTwrEngText.color = theme.AccentSecondary;
            if (_propNameText != null) _propNameText.color = theme.AccentSecondary;
            if (_pitchFillImg != null) _pitchFillImg.color = theme.AccentSecondary;
            if (_rollFillImg != null) _rollFillImg.color = theme.AccentSecondary;
            if (_yawFillImg != null) _yawFillImg.color = theme.AccentSecondary;
        }
    }
}
