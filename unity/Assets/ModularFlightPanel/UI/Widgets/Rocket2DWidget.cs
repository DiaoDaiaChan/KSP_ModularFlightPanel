using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 火箭姿态与分级动力学卡片 (Rocket 2D / Staging Monitor)
    /// 直观呈现当前级推进剂耗尽进度条、下级预备状态与实时 dV / TWR
    /// </summary>
    public class Rocket2DWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _subTitleText;
        private Text _statusText;

        // 顶部简报
        private Text _summaryStageText;
        private Text _summaryTwrText;
        private Text _summaryDvText;

        // 3 级推进堆叠结构
        private struct StageRowUI
        {
            public GameObject RowObj;
            public Image LeftAccent;
            public Text StageIdText;
            public Text StageStatusText;
            public Text StageDetailText;
            public RectTransform FuelBarFill;
            public Text FuelPercentText;
        }

        private StageRowUI[] _stageRows = new StageRowUI[3];
        private float _barMaxWidth;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(270f * s, 168f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            // 1. 顶部 Header
            _titleText = UIFactory.CreateText(transform, "Title", "ROCKET / 2D", Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(95f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-75f * s, 68f * s);

            _subTitleText = UIFactory.CreateText(transform, "SubTitle", "STAGING TELEMETRY", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(100f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(15f * s, 68f * s);

            _statusText = UIFactory.CreateText(transform, "Status", "● LIVE", Mathf.RoundToInt(9f * s), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statRt = _statusText.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(50f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(100f * s, 68f * s);

            // 分割横线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 56f * s), theme.FrameBorderColor);

            // 2. 简报行 (STAGE, TWR, STAGE dV)
            _summaryStageText = UIFactory.CreateText(transform, "Sum_Stg", "STG --", Mathf.RoundToInt(9f * s), TextAnchor.MiddleLeft, theme.WarningColor);
            RectTransform sStgRt = _summaryStageText.GetComponent<RectTransform>();
            sStgRt.sizeDelta = new Vector2(75f * s, 16f * s);
            sStgRt.anchoredPosition = new Vector2(-85f * s, 44f * s);

            _summaryTwrText = UIFactory.CreateText(transform, "Sum_Twr", "TWR 0.00", Mathf.RoundToInt(9f * s), TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform sTwrRt = _summaryTwrText.GetComponent<RectTransform>();
            sTwrRt.sizeDelta = new Vector2(75f * s, 16f * s);
            sTwrRt.anchoredPosition = new Vector2(0f, 44f * s);

            _summaryDvText = UIFactory.CreateText(transform, "Sum_Dv", "dV ---- m/s", Mathf.RoundToInt(9f * s), TextAnchor.MiddleRight, theme.AccentSecondary);
            RectTransform sDvRt = _summaryDvText.GetComponent<RectTransform>();
            sDvRt.sizeDelta = new Vector2(85f * s, 16f * s);
            sDvRt.anchoredPosition = new Vector2(82f * s, 44f * s);

            // 3. 构建 3 个分级状态条 (当前活跃级、下一级、轨道入轨级)
            _barMaxWidth = 135f * s;
            float[] yOffsets = new float[] { 14f * s, -22f * s, -58f * s };

            for (int i = 0; i < 3; i++)
            {
                _stageRows[i] = CreateStageRow(transform, $"StageRow_{i}", new Vector2(panelSize.x - 16f * s, 32f * s), new Vector2(0f, yOffsets[i]), i == 0, theme);
            }
        }

        private StageRowUI CreateStageRow(Transform parent, string name, Vector2 size, Vector2 pos, bool isActive, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            StageRowUI row = new StageRowUI();

            Color rowBg = isActive ? new Color(0.08f, 0.14f, 0.22f, 0.55f) : new Color(0.06f, 0.09f, 0.14f, 0.45f);
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.gray;
            Color faintBorder = new Color(borderCol.r, borderCol.g, borderCol.b, 0.18f);

            row.RowObj = UIFactory.CreatePanel(parent, name, size, pos, rowBg, faintBorder, 1f * s);

            // 左侧状态竖条
            GameObject accentObj = UIFactory.CreatePanel(row.RowObj.transform, "Accent", new Vector2(3f * s, size.y - 4f * s),
                new Vector2(-(size.x * 0.5f) + 2f * s, 0f), isActive ? theme.AccentPrimary : theme.FrameBorderColor);
            row.LeftAccent = accentObj.GetComponent<Image>();

            // 级数标签
            row.StageIdText = UIFactory.CreateText(row.RowObj.transform, "StgId", "STG 0", Mathf.RoundToInt(9.5f * s),
                TextAnchor.MiddleLeft, isActive ? theme.WarningColor : theme.TextAccentColor);
            RectTransform idRt = row.StageIdText.GetComponent<RectTransform>();
            idRt.sizeDelta = new Vector2(48f * s, 14f * s);
            idRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 30f * s, 7f * s);

            // 状态标签 (ACTIVE / STANDBY)
            row.StageStatusText = UIFactory.CreateText(row.RowObj.transform, "StgStat", isActive ? "ACTIVE" : "STANDBY",
                Mathf.RoundToInt(6.5f * s), TextAnchor.MiddleLeft, isActive ? theme.AccentPrimary : theme.TextAccentColor);
            RectTransform statRt = row.StageStatusText.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(48f * s, 10f * s);
            statRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 30f * s, -7f * s);

            // 推进剂槽道
            GameObject barBg = UIFactory.CreatePanel(row.RowObj.transform, "FuelBar_Bg", new Vector2(_barMaxWidth, 7f * s),
                new Vector2(25f * s, -6f * s), theme.FrameBorderColor);

            // 推进剂填充条
            GameObject barFill = UIFactory.CreatePanel(barBg.transform, "FuelBar_Fill", new Vector2(_barMaxWidth, 7f * s),
                Vector2.zero, isActive ? theme.AccentPrimary : theme.AccentSecondary);
            row.FuelBarFill = barFill.GetComponent<RectTransform>();
            row.FuelBarFill.pivot = new Vector2(0f, 0.5f);
            row.FuelBarFill.anchoredPosition = new Vector2(-(_barMaxWidth * 0.5f), 0f);

            // 详细说明 (如 CORE LFO / SOLID BOOSTER)
            row.StageDetailText = UIFactory.CreateText(row.RowObj.transform, "Detail", "PROPULSION",
                Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform detRt = row.StageDetailText.GetComponent<RectTransform>();
            detRt.sizeDelta = new Vector2(_barMaxWidth, 12f * s);
            detRt.anchoredPosition = new Vector2(25f * s, 7f * s);

            // 百分比读数
            row.FuelPercentText = UIFactory.CreateText(row.RowObj.transform, "Percent", "100%",
                Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, theme.TextPrimaryColor);
            RectTransform pctRt = row.FuelPercentText.GetComponent<RectTransform>();
            pctRt.sizeDelta = new Vector2(40f * s, 14f * s);
            pctRt.anchoredPosition = new Vector2((size.x * 0.5f) - 24f * s, 0f);

            return row;
        }

        public override float DefaultUpdateInterval => 0.1f; // 动力分级监控 10Hz 刷新足以保证平滑体验并降低 CPU

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            int curStage = (int)TelemetryTokenEngine.EvaluateNumeric("{STAGE}", telemetry);
            float propFraction = (float)(TelemetryTokenEngine.EvaluateNumeric("{PROP}", telemetry) / 100.0);
            int activeEngines = (int)TelemetryTokenEngine.EvaluateNumeric("{ENG}", telemetry);
            double stageDv = TelemetryTokenEngine.EvaluateNumeric("{STAGE:DV}", telemetry);

            // 3. 更新 Header 简报
            _summaryStageText.text = TelemetryTokenEngine.Evaluate("{STAGE}", telemetry);
            _summaryTwrText.text = TelemetryTokenEngine.Evaluate("TWR {TWR:F2}", telemetry);
            _summaryDvText.text = stageDv > 0 ? TelemetryTokenEngine.Evaluate("dV {STAGE:DV}", telemetry) : $"{activeEngines} ENG ON";

            // 4. 更新 3 级推进条
            UpdateRowDisplay(0, curStage, "ACTIVE", $"{activeEngines} ENGINES OPERATIONAL", propFraction, true);

            if (curStage > 0)
            {
                _stageRows[1].RowObj.SetActive(true);
                UpdateRowDisplay(1, curStage - 1, "STANDBY", "UPPER STAGE READY", 1.0f, false);
            }
            else
            {
                _stageRows[1].RowObj.SetActive(false);
            }

            if (curStage > 1)
            {
                _stageRows[2].RowObj.SetActive(true);
                UpdateRowDisplay(2, 0, "FINAL", "PAYLOAD / ORBIT INSERTION", 1.0f, false);
            }
            else
            {
                _stageRows[2].RowObj.SetActive(false);
            }
        }

        private void UpdateRowDisplay(int rowIndex, int stageNumber, string statusTag, string detail, float fraction, bool isActive)
        {
            StageRowUI row = _stageRows[rowIndex];
            row.StageIdText.text = $"STG {stageNumber}";
            row.StageStatusText.text = statusTag;
            row.StageDetailText.text = detail;

            float clampedFraction = Mathf.Clamp01(fraction);
            row.FuelBarFill.sizeDelta = new Vector2(_barMaxWidth * clampedFraction, row.FuelBarFill.sizeDelta.y);
            row.FuelPercentText.text = $"{clampedFraction * 100f:F1}%";

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            if (isActive && theme != null)
            {
                Image fillImg = row.FuelBarFill.GetComponent<Image>();
                if (fillImg != null)
                {
                    if (clampedFraction <= 0.1f) fillImg.color = theme.WarningColor;
                    else if (clampedFraction <= 0.25f) fillImg.color = theme.CautionColor;
                    else fillImg.color = theme.AccentPrimary;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null) _bgImage.color = theme.FrameBgColor;
            if (_outline != null) _outline.effectColor = theme.FrameBorderColor;
            if (_titleText != null) _titleText.color = theme.TextPrimaryColor;
            if (_subTitleText != null) _subTitleText.color = theme.AccentSecondary;
            if (_statusText != null) _statusText.color = theme.AccentPrimary;
            if (_summaryStageText != null) _summaryStageText.color = theme.WarningColor;
            if (_summaryTwrText != null) _summaryTwrText.color = theme.TextPrimaryColor;
            if (_summaryDvText != null) _summaryDvText.color = theme.AccentSecondary;
        }
    }
}
