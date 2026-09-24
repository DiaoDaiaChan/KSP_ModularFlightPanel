using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 航电通信网络与天线遥测卡片 (CommNet / Antenna Signal Monitor)
    /// 实时查询原版 CommNet / RealAntennas 连接状态、控制权等级与天线工况
    /// </summary>
    public class SignalStatusWidget : BaseFlightWidget
    {
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _subTitleText;
        private Text _statusBadge;

        // 通信网络简报
        private Text _antCountText;
        private Text _controlLevelText;
        private Text _signalPercentText;

        // 天线卡片列表 (最多呈现 3 根天线)
        private struct AntennaRowUI
        {
            public GameObject RowObj;
            public Text NameText;
            public Text SpecText;
            public Image[] SignalBars; // 5 格信号条
            public Text LinkStatusText;
        }

        private AntennaRowUI[] _antennaRows = new AntennaRowUI[3];

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(270f * s, 155f * s);
            RectTransform.sizeDelta = panelSize;

            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _outline = gameObject.AddComponent<Outline>();
            _outline.effectColor = theme.FrameBorderColor;
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            // 1. 顶部 Header
            _titleText = UIFactory.CreateText(transform, "Title", "COMMNET", Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform titRt = _titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(90f * s, 18f * s);
            titRt.anchoredPosition = new Vector2(-75f * s, 62f * s);

            _subTitleText = UIFactory.CreateText(transform, "SubTitle", "ANTENNA NETWORK", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.AccentSecondary);
            RectTransform subRt = _subTitleText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(100f * s, 16f * s);
            subRt.anchoredPosition = new Vector2(20f * s, 62f * s);

            _statusBadge = UIFactory.CreateText(transform, "Badge", "● LINKED", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform statRt = _statusBadge.GetComponent<RectTransform>();
            statRt.sizeDelta = new Vector2(65f * s, 16f * s);
            statRt.anchoredPosition = new Vector2(95f * s, 62f * s);

            // 分割横线
            UIFactory.CreatePanel(transform, "Div1", new Vector2(panelSize.x - 16f * s, 1f * s), new Vector2(0f, 50f * s), theme.FrameBorderColor);

            // 2. 简报行
            _antCountText = UIFactory.CreateText(transform, "Sum_Count", "ANTENNAS 0", Mathf.RoundToInt(8f * s), TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform cntRt = _antCountText.GetComponent<RectTransform>();
            cntRt.sizeDelta = new Vector2(80f * s, 14f * s);
            cntRt.anchoredPosition = new Vector2(-82f * s, 38f * s);

            _controlLevelText = UIFactory.CreateText(transform, "Sum_Ctrl", "FULL CONTROL", Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, theme.AccentPrimary);
            RectTransform ctrlRt = _controlLevelText.GetComponent<RectTransform>();
            ctrlRt.sizeDelta = new Vector2(85f * s, 14f * s);
            ctrlRt.anchoredPosition = new Vector2(0f, 38f * s);

            _signalPercentText = UIFactory.CreateText(transform, "Sum_Sig", "SIG 100%", Mathf.RoundToInt(8.5f * s), TextAnchor.MiddleRight, theme.AccentSecondary);
            RectTransform sigRt = _signalPercentText.GetComponent<RectTransform>();
            sigRt.sizeDelta = new Vector2(70f * s, 14f * s);
            sigRt.anchoredPosition = new Vector2(95f * s, 38f * s);

            // 3. 构建 3 个天线卡片行
            float[] yOffsets = new float[] { 11f * s, -23f * s, -57f * s };
            for (int i = 0; i < 3; i++)
            {
                _antennaRows[i] = CreateAntennaRow(transform, $"AntRow_{i}", new Vector2(panelSize.x - 16f * s, 30f * s), new Vector2(0f, yOffsets[i]), theme);
            }
        }

        private AntennaRowUI CreateAntennaRow(Transform parent, string name, Vector2 size, Vector2 pos, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            AntennaRowUI row = new AntennaRowUI();

            Color rowBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot);
            Color borderCol = theme.FrameBorderColor;
            Color faintBorder = WidgetStyleManager.Weighted(borderCol, LineWeight.Faint);
            row.RowObj = UIFactory.CreatePanel(parent, name, size, pos, rowBg, faintBorder, 1f * s);

            // 天线图标
            Text icon = UIFactory.CreateText(row.RowObj.transform, "Icon", "◉", Mathf.RoundToInt(11f * s),
                TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform icRt = icon.GetComponent<RectTransform>();
            icRt.sizeDelta = new Vector2(16f * s, 16f * s);
            icRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 12f * s, 0f);

            // 天线名称
            row.NameText = UIFactory.CreateText(row.RowObj.transform, "Name", "INTERNAL ANTENNA", Mathf.RoundToInt(8.5f * s),
                TextAnchor.MiddleLeft, theme.TextPrimaryColor);
            RectTransform nmRt = row.NameText.GetComponent<RectTransform>();
            nmRt.sizeDelta = new Vector2(120f * s, 14f * s);
            nmRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 82f * s, 6f * s);

            // 天线规格 (DIRECT / RELAY / POWER)
            row.SpecText = UIFactory.CreateText(row.RowObj.transform, "Spec", "DIRECT  ·  5.0k POWER", Mathf.RoundToInt(6.5f * s),
                TextAnchor.MiddleLeft, theme.TextAccentColor);
            RectTransform spRt = row.SpecText.GetComponent<RectTransform>();
            spRt.sizeDelta = new Vector2(120f * s, 10f * s);
            spRt.anchoredPosition = new Vector2(-(size.x * 0.5f) + 82f * s, -6f * s);

            // 5 格信号计量柱
            row.SignalBars = new Image[5];
            float[] barHeights = new float[] { 4f * s, 7f * s, 10f * s, 13f * s, 16f * s };
            for (int b = 0; b < 5; b++)
            {
                GameObject barObj = UIFactory.CreatePanel(row.RowObj.transform, $"SigBar_{b}", new Vector2(3f * s, barHeights[b]),
                    new Vector2((size.x * 0.5f) - 65f * s + (b * 5f * s), -(size.y * 0.5f) + (barHeights[b] * 0.5f) + 7f * s),
                    theme.FrameBorderColor);
                row.SignalBars[b] = barObj.GetComponent<Image>();
            }

            // 连接状态
            row.LinkStatusText = UIFactory.CreateText(row.RowObj.transform, "LinkStat", "LINKED", Mathf.RoundToInt(7.5f * s),
                TextAnchor.MiddleRight, theme.AccentPrimary);
            RectTransform lsRt = row.LinkStatusText.GetComponent<RectTransform>();
            lsRt.sizeDelta = new Vector2(40f * s, 14f * s);
            lsRt.anchoredPosition = new Vector2((size.x * 0.5f) - 20f * s, 0f);

            return row;
        }

        private string _lastStatusBadgeText;
        private string _lastCtrlLevelText;
        private string _lastSigPercentText;
        private string _lastAntCountText;
        private int _lastActiveBars = -1;
        private bool _lastConnected = false;

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            string commToken = GetTemplateChannel("VAL", "{COMM}");
            bool isConnected = telemetry.IsConnected;
            double signalStrength = TelemetryTokenEngine.EvaluateNumeric(commToken, telemetry) / 100.0;
            if (double.IsNaN(signalStrength)) signalStrength = telemetry.CommSignal;

            string ctrlLevelStr = telemetry.ControlLevelStr;
            int antCount = telemetry.AntennaCount;

            string badgeStr = isConnected ? "● LINKED" : "▲ OFFLINE";
            if (badgeStr != _lastStatusBadgeText)
            {
                _lastStatusBadgeText = badgeStr;
                _statusBadge.text = badgeStr;
            }

            TextStyleRole ctrlRole = isConnected ? (signalStrength < 0.35 ? TextStyleRole.Warning : TextStyleRole.Accent) : TextStyleRole.Danger;
            ApplyText(_statusBadge, ctrlRole, theme);

            if (ctrlLevelStr != _lastCtrlLevelText)
            {
                _lastCtrlLevelText = ctrlLevelStr;
                _controlLevelText.text = ctrlLevelStr;
            }
            ApplyText(_controlLevelText, ctrlRole, theme);

            string sigPercentStr = TelemetryTokenEngine.Evaluate("SIG " + commToken, telemetry);
            if (sigPercentStr != _lastSigPercentText)
            {
                _lastSigPercentText = sigPercentStr;
                _signalPercentText.text = sigPercentStr;
            }

            string antCountStr = $"ANTENNAS {antCount}";
            if (antCountStr != _lastAntCountText)
            {
                _lastAntCountText = antCountStr;
                _antCountText.text = antCountStr;
            }

            int activeBars = Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * 5f), isConnected ? 1 : 0, 5);
            bool barsDirty = (activeBars != _lastActiveBars || isConnected != _lastConnected);
            if (barsDirty)
            {
                _lastActiveBars = activeBars;
                _lastConnected = isConnected;
            }

            for (int i = 0; i < 3; i++)
            {
                if (i < antCount)
                {
                    _antennaRows[i].RowObj.SetActive(true);
                    _antennaRows[i].NameText.text = i == 0 ? "PRIMARY COMM DISH" : $"OMNI ANTENNA #{i}";
                    _antennaRows[i].SpecText.text = "DIRECT · 5.0k POWER";
                    _antennaRows[i].LinkStatusText.text = isConnected ? "LINKED" : "SEARCHING";
                    ApplyText(_antennaRows[i].LinkStatusText, isConnected ? TextStyleRole.Accent : TextStyleRole.Warning, theme);

                    if (barsDirty) UpdateSignalBars(_antennaRows[i].SignalBars, activeBars, isConnected, theme);
                }
                else if (i == 0 && antCount == 0)
                {
                    _antennaRows[i].RowObj.SetActive(true);
                    _antennaRows[i].NameText.text = "INTERNAL PROBE/POD";
                    _antennaRows[i].SpecText.text = "DIRECT · 5.0k RATING";
                    _antennaRows[i].LinkStatusText.text = isConnected ? "LINKED" : "NO SIGNAL";
                    ApplyText(_antennaRows[i].LinkStatusText, isConnected ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
                    if (barsDirty) UpdateSignalBars(_antennaRows[i].SignalBars, activeBars, isConnected, theme);
                }
                else
                {
                    _antennaRows[i].RowObj.SetActive(false);
                }
            }
        }

        private void UpdateSignalBars(Image[] bars, int activeCount, bool isConnected, ThemeConfig theme)
        {
            MeterStyleRole onRole = isConnected ? MeterStyleRole.Primary : MeterStyleRole.Warning;
            Color onColor = WidgetStyleManager.Meter(onRole, theme);
            Color offColor = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);

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
            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_titleText != null)
            {
                _titleText.text = GetTemplateChannel("TITLE", "COMMNET");
                ApplyText(_titleText, TextStyleRole.Label, theme);
            }
            if (_subTitleText != null)
            {
                _subTitleText.text = GetTemplateChannel("SUBTITLE", "ANTENNA NETWORK");
                ApplyText(_subTitleText, TextStyleRole.SecondaryValue, theme);
            }
            if (_statusBadge != null) ApplyText(_statusBadge, TextStyleRole.Accent, theme);
            if (_antCountText != null) ApplyText(_antCountText, TextStyleRole.Muted, theme);
            if (_controlLevelText != null) ApplyText(_controlLevelText, TextStyleRole.PrimaryValue, theme);
            if (_signalPercentText != null) ApplyText(_signalPercentText, TextStyleRole.SecondaryValue, theme);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
