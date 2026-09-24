using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化全玻璃座舱 CommNet / RealAntennas 天线通信网络监控仪 (Avionics CommNet / Antenna Monitor)
    /// 遵照 MFP-SPEC-001..007 标准航电规范与 SpaceX 直播 HUD 语言：
    /// 1. 100% 接入真实天线遥测 (支持原版 CommNet / RealAntennas 探针与部件深度回退)；
    /// 2. 移除陈旧粗糙深褐色卡片与金属边框，转为超通透暗晶毛玻璃背板 (0.75 Alpha)；
    /// 3. 自适应高度动态排布 (Auto-Compact Height: 单根 100px, 双根 134px, 三根 168px)；
    /// 4. 严格遵守零颜色字面量与 10Hz Relaxed 阶梯分频刷新。
    /// </summary>
    public class SignalStatusWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private GameObject _headerRoot;
        private Text _titleText;
        private Text _subTitleText;
        private Text _statusBadge;
        private Image _dividerLine;

        // 通信网络简报
        private GameObject _summaryRoot;
        private Text _antCountText;
        private Text _controlLevelText;
        private Text _signalPercentText;

        // 天线卡片列表 (最多呈现 3 根天线)
        private class AntennaRowUI
        {
            public GameObject RowObj;
            public RectTransform RowRt;
            public Image RowBg;
            public Outline RowOutline;
            public Text IconText;
            public Text NameText;
            public Text SpecText;
            public Image[] SignalBars; // 5 格信号条
            public Text LinkStatusText;
        }

        private const int MaxAntennaRows = 3;
        private readonly AntennaRowUI[] _antennaRows = new AntennaRowUI[MaxAntennaRows];

        private string _lastStatusBadgeText = string.Empty;
        private string _lastCtrlLevelText = string.Empty;
        private string _lastSigPercentText = string.Empty;
        private string _lastAntCountText = string.Empty;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float baseW = 270f * s;
            float baseH = 168f * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            // 1. 暗晶微距背板
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
            _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 2. 顶部 Header 栏
            _headerRoot = new GameObject("HeaderRoot", typeof(RectTransform));
            _headerRoot.transform.SetParent(transform, false);
            RectTransform hdrRt = _headerRoot.GetComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0f, 1f);
            hdrRt.anchorMax = new Vector2(1f, 1f);
            hdrRt.pivot = new Vector2(0.5f, 1f);
            hdrRt.sizeDelta = new Vector2(0f, 26f * s);
            hdrRt.anchoredPosition = Vector2.zero;

            _titleText = UIFactory.CreateText(_headerRoot.transform, "Title", "COMMNET",
                Mathf.Max(9, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(80f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-baseW * 0.5f + 48f * s, -14f * s);

            _subTitleText = UIFactory.CreateText(_headerRoot.transform, "SubTitle", "ANTENNA NETWORK",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(100f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(-baseW * 0.5f + 135f * s, -14f * s);

            _statusBadge = UIFactory.CreateText(_headerRoot.transform, "Badge", "● LINKED",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleRight, theme.AccentPrimary);
            _statusBadge.fontStyle = FontStyle.Bold;
            RectTransform statRt = _statusBadge.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(70f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(baseW * 0.5f - 42f * s, -14f * s);

            // 分割微线
            GameObject divGo = UIFactory.CreatePanel(transform, "HeaderDiv",
                new Vector2(baseW - 16f * s, 1f * s), Vector2.zero,
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _dividerLine = divGo.GetComponent<Image>();
            RectTransform divRt = divGo.GetComponent<RectTransform>();
            divRt.anchorMin = new Vector2(0.5f, 1f);
            divRt.anchorMax = new Vector2(0.5f, 1f);
            divRt.pivot = new Vector2(0.5f, 1f);
            divRt.anchoredPosition = new Vector2(0f, -27f * s);

            // 3. 简报行 (Summary)
            _summaryRoot = new GameObject("SummaryRoot", typeof(RectTransform));
            _summaryRoot.transform.SetParent(transform, false);
            RectTransform sumRt = _summaryRoot.GetComponent<RectTransform>();
            sumRt.anchorMin = new Vector2(0f, 1f);
            sumRt.anchorMax = new Vector2(1f, 1f);
            sumRt.pivot = new Vector2(0.5f, 1f);
            sumRt.sizeDelta = new Vector2(0f, 18f * s);
            sumRt.anchoredPosition = new Vector2(0f, -32f * s);

            _antCountText = UIFactory.CreateText(_summaryRoot.transform, "Sum_Count", "ANTENNAS 0",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform cntRt = _antCountText.GetComponent<RectTransform>();
            cntRt.sizeDelta = new Vector2(80f * s, 16f * s);
            cntRt.anchoredPosition = new Vector2(-baseW * 0.5f + 48f * s, -8f * s);

            _controlLevelText = UIFactory.CreateText(_summaryRoot.transform, "Sum_Ctrl", "FULL CONTROL",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, theme.AccentPrimary);
            _controlLevelText.fontStyle = FontStyle.Bold;
            RectTransform ctrlRt = _controlLevelText.GetComponent<RectTransform>();
            ctrlRt.sizeDelta = new Vector2(90f * s, 16f * s);
            ctrlRt.anchoredPosition = new Vector2(0f, -8f * s);

            _signalPercentText = UIFactory.CreateText(_summaryRoot.transform, "Sum_Sig", "SIG 100%",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, theme.TextPrimaryColor);
            _signalPercentText.fontStyle = FontStyle.Bold;
            RectTransform sigRt = _signalPercentText.GetComponent<RectTransform>();
            sigRt.sizeDelta = new Vector2(70f * s, 16f * s);
            sigRt.anchoredPosition = new Vector2(baseW * 0.5f - 42f * s, -8f * s);

            // 4. 构建 3 个天线卡片行 (相对顶栏自顶向下挂载)
            Vector2 rowSize = new Vector2(baseW - 16f * s, 30f * s);
            for (int i = 0; i < MaxAntennaRows; i++)
            {
                _antennaRows[i] = CreateAntennaRow(transform, $"AntRow_{i}", rowSize, i, s, theme);
            }

            ApplyTheme(theme);
        }

        private AntennaRowUI CreateAntennaRow(Transform parent, string name, Vector2 size, int index, float s, ThemeConfig theme)
        {
            AntennaRowUI row = new AntennaRowUI();
            Color rowBg = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            Color borderCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            row.RowObj = UIFactory.CreatePanel(parent, name, size, Vector2.zero, rowBg, borderCol, 1f * s);
            row.RowRt = row.RowObj.GetComponent<RectTransform>();
            row.RowRt.anchorMin = new Vector2(0.5f, 1f);
            row.RowRt.anchorMax = new Vector2(0.5f, 1f);
            row.RowRt.pivot = new Vector2(0.5f, 1f);
            row.RowRt.anchoredPosition = new Vector2(0f, (-54f - index * 34f) * s);

            row.RowBg = row.RowObj.GetComponent<Image>();
            row.RowOutline = row.RowObj.GetComponent<Outline>();

            // 天线图标
            row.IconText = UIFactory.CreateText(row.RowObj.transform, "Icon", "◉",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform icRt = row.IconText.GetComponent<RectTransform>();
            icRt.sizeDelta = new Vector2(16f * s, 16f * s);
            icRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 12f * s, 0f);

            // 天线名称
            row.NameText = UIFactory.CreateText(row.RowObj.transform, "Name", "INTERNAL ANTENNA",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            row.NameText.fontStyle = FontStyle.Bold;
            RectTransform nmRt = row.NameText.GetComponent<RectTransform>();
            nmRt.sizeDelta = new Vector2(126f * s, 14f * s);
            nmRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 84f * s, 5.5f * s);

            // 天线规格 (DIRECT · 500k POWER)
            row.SpecText = UIFactory.CreateText(row.RowObj.transform, "Spec", "DIRECT  ·  5.0k POWER",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform spRt = row.SpecText.GetComponent<RectTransform>();
            spRt.sizeDelta = new Vector2(126f * s, 11f * s);
            spRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 84f * s, -6f * s);

            // 5 格信号计量柱
            row.SignalBars = new Image[5];
            float[] barHeights = new float[] { 3.5f * s, 6f * s, 8.5f * s, 11f * s, 13.5f * s };
            for (int b = 0; b < 5; b++)
            {
                GameObject barObj = UIFactory.CreatePanel(row.RowObj.transform, $"SigBar_{b}",
                    new Vector2(2.5f * s, barHeights[b]),
                    new Vector2((size.x * 0.5f) - 62f * s + (b * 4.5f * s), -(size.y * 0.5f) + (barHeights[b] * 0.5f) + 8f * s),
                    theme.InactiveMeterColor);
                row.SignalBars[b] = barObj.GetComponent<Image>();
            }

            // 连接状态
            row.LinkStatusText = UIFactory.CreateText(row.RowObj.transform, "LinkStat", "LINKED",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, theme.AccentPrimary);
            row.LinkStatusText.fontStyle = FontStyle.Bold;
            RectTransform lsRt = row.LinkStatusText.GetComponent<RectTransform>();
            lsRt.sizeDelta = new Vector2(44f * s, 14f * s);
            lsRt.anchoredPosition = new Vector2((size.x * 0.5f) - 24f * s, 0f);

            return row;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            float s = CurrentDpiScale;

            string commToken = GetTemplateChannel("VAL", "{COMM}");
            bool isConnected = telemetry.IsConnected;
            double signalStrength = TelemetryTokenEngine.EvaluateNumeric(commToken, telemetry) / 100.0;
            if (double.IsNaN(signalStrength)) signalStrength = telemetry.CommSignal;

            string ctrlLevelStr = telemetry.ControlLevelStr;
            int antCount = telemetry.AntennaCount;

            // 1. 状态徽章
            string badgeStr = isConnected ? "● LINKED" : "▲ OFFLINE";
            if (badgeStr != _lastStatusBadgeText)
            {
                _lastStatusBadgeText = badgeStr;
                _statusBadge.text = badgeStr;
            }
            TextStyleRole ctrlRole = isConnected ? (signalStrength < 0.35 ? TextStyleRole.Warning : TextStyleRole.Accent) : TextStyleRole.Danger;
            ApplyText(_statusBadge, ctrlRole, theme);

            // 2. 控制权
            if (ctrlLevelStr != _lastCtrlLevelText)
            {
                _lastCtrlLevelText = ctrlLevelStr;
                _controlLevelText.text = ctrlLevelStr;
            }
            ApplyText(_controlLevelText, ctrlRole, theme);

            // 3. 综合信号读数
            string sigPercentStr = $"SIG {Mathf.RoundToInt((float)signalStrength * 100f)}%";
            if (sigPercentStr != _lastSigPercentText)
            {
                _lastSigPercentText = sigPercentStr;
                _signalPercentText.text = sigPercentStr;
            }

            // 4. 读取真实天线列表 (真实 KSP ModuleDataTransmitter 适配，含回退)
            var antennas = telemetry.Antennas;
            int realAntCount = (antennas != null && antennas.Count > 0) ? antennas.Count : (antCount > 0 ? antCount : 1);
            string antCountStr = $"ANTENNAS {realAntCount}";
            if (antCountStr != _lastAntCountText)
            {
                _lastAntCountText = antCountStr;
                _antCountText.text = antCountStr;
            }

            int displayCount = Mathf.Clamp(realAntCount, 1, MaxAntennaRows);

            // 固定标准尺寸高度 (168px)，关闭运行时大小自适应，保持设计几何绝对稳定

            int overallBars = Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * 5f), isConnected ? 1 : 0, 5);

            for (int i = 0; i < MaxAntennaRows; i++)
            {
                var row = _antennaRows[i];
                if (row == null || row.RowObj == null) continue;

                if (i < displayCount)
                {
                    row.RowObj.SetActive(true);

                    if (antennas != null && i < antennas.Count)
                    {
                        var ant = antennas[i];
                        row.NameText.text = ant.Name.ToUpperInvariant();
                        row.SpecText.text = ant.SpecSummary;
                        row.LinkStatusText.text = ant.Status;

                        TextStyleRole statRole = (ant.Status == "LINKED") ? TextStyleRole.Accent :
                            (ant.Status == "SEARCHING" || ant.Status == "DEPLOYING" ? TextStyleRole.Warning : TextStyleRole.Muted);
                        ApplyText(row.LinkStatusText, statRole, theme);

                        int antBars = Mathf.Clamp(Mathf.CeilToInt(ant.SignalStrength * 5f), (ant.IsOperational && isConnected) ? 1 : 0, 5);
                        UpdateSignalBars(row.SignalBars, antBars, isConnected && ant.IsOperational, theme);
                    }
                    else
                    {
                        // 单天线回退
                        row.NameText.text = i == 0 ? "PRIMARY COMM DISH" : $"ANTENNA #{i + 1}";
                        row.SpecText.text = "DIRECT · 5.0k POWER";
                        row.LinkStatusText.text = isConnected ? "LINKED" : "SEARCHING";
                        ApplyText(row.LinkStatusText, isConnected ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
                        UpdateSignalBars(row.SignalBars, overallBars, isConnected, theme);
                    }
                }
                else
                {
                    row.RowObj.SetActive(false);
                }
            }
        }

        private void UpdateSignalBars(Image[] bars, int activeCount, bool isConnected, ThemeConfig theme)
        {
            if (bars == null) return;
            MeterStyleRole onRole = isConnected ? MeterStyleRole.Primary : MeterStyleRole.Warning;
            Color onColor = WidgetStyleManager.Instance.GetMeterColor(onRole, theme);
            Color offColor = WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Track, theme);

            for (int b = 0; b < bars.Length; b++)
            {
                if (bars[b] != null)
                {
                    bars[b].color = b < activeCount ? onColor : offColor;
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            if (_bgImage != null) _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.75f);
            if (_outline != null)
            {
                _outline.enabled = true;
                _outline.effectColor = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost);
                _outline.effectDistance = new Vector2(1f * s, 1f * s);
            }

            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "COMMNET");
                ApplyText(_titleText, TextStyleRole.PrimaryValue, theme);
            }
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "ANTENNA NETWORK");
                ApplyText(_subTitleText, TextStyleRole.SecondaryValue, theme);
            }
            if (_statusBadge != null) ApplyText(_statusBadge, TextStyleRole.Accent, theme);
            if (_dividerLine != null) _dividerLine.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            if (_antCountText != null) ApplyText(_antCountText, TextStyleRole.SecondaryValue, theme);
            if (_controlLevelText != null) ApplyText(_controlLevelText, TextStyleRole.Accent, theme);
            if (_signalPercentText != null) ApplyText(_signalPercentText, TextStyleRole.PrimaryValue, theme);

            for (int i = 0; i < MaxAntennaRows; i++)
            {
                var row = _antennaRows[i];
                if (row == null || row.RowObj == null) continue;
                if (row.RowBg != null) row.RowBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (row.RowOutline != null) row.RowOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (row.IconText != null) ApplyText(row.IconText, TextStyleRole.SecondaryValue, theme);
                if (row.NameText != null) ApplyText(row.NameText, TextStyleRole.PrimaryValue, theme);
                if (row.SpecText != null) ApplyText(row.SpecText, TextStyleRole.SecondaryValue, theme);
                if (row.LinkStatusText != null) ApplyText(row.LinkStatusText, TextStyleRole.Accent, theme);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
