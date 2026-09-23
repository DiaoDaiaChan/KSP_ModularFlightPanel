using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化航电通信网络与真实天线探针组件 (Avionics CommNet & RealAntennas Capsule Suite)
    /// 
    /// 完美替代 KSP 原版左上角通信信号指示器：
    /// 1. 现代化微光胶囊状态条 (5 阶信号阶梯光柱、控制权徽章、主通信站标识、Tx/Rx 与实时速率)
    /// 2. 深度集成 RealAntennas 探针 (查询真实射频链路、双向吞吐速率、动态多信道对端列表)
    /// 3. 抽屉式折叠扩展信道矩阵 (对端站点名称、链路带宽、相对信号强度、中继/直连标识)
    /// 4. 原版 CommNet 信号栏非破坏性安全隐显切换 (STOCK TOGGLE)
    /// </summary>
    public class CommSignalWidget : BaseFlightWidget
    {
        private Image _panelBg;
        private Outline _panelOutline;
        private Image _topStripe;

        // 胶囊顶栏 UI 元素
        private GameObject _capsuleBar;
        private const int MainBarCount = 5;
        private readonly Image[] _mainSignalBars = new Image[MainBarCount];
        private Text _ctrlBadgeText;
        private Image _ctrlBadgeBg;
        private Text _targetNameText;
        private Text _rateSummaryText;
        private Button _expandBtn;
        private Text _expandBtnText;
        private Button _stockBtn;
        private Text _stockBtnText;

        // 抽屉式扩展链路列表
        private GameObject _dropdownPanel;
        private Text _matrixTitleText;
        private Text _matrixSummaryText;

        private const int MaxPeerRows = 5;
        private struct PeerRowUI
        {
            public GameObject RowObj;
            public Text NameText;
            public Text TagText;
            public Text RateText;
            public Image[] MiniBars;
        }
        private readonly PeerRowUI[] _peerRows = new PeerRowUI[MaxPeerRows];
        private Text _matrixFooterText;

        private bool _isExpanded = true;
        private bool _stockHidden = true;
        private ThemeConfig _currentTheme;
        private IFlightTelemetry _lastTelemetry;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            Vector2 baseSize = new Vector2(236f * s, 32f * s);
            RectTransform.sizeDelta = baseSize;

            Color bgCol = (theme != null) ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.94f);
            Color borderCol = (theme != null) ? (Color)theme.FrameBorderColor : Color.cyan;
            Color primaryAccent = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color secondaryAccent = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color textPrimary = (theme != null) ? (Color)theme.TextPrimaryColor : Color.white;

            // 1. 胶囊底座
            _capsuleBar = UIFactory.CreatePanel(transform, "CapsuleBar", baseSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
            _panelBg = _capsuleBar.GetComponent<Image>();
            _panelOutline = _capsuleBar.GetComponent<Outline>();

            // 顶部微光装饰线条
            _topStripe = UIFactory.CreatePanel(_capsuleBar.transform, "TopStripe", new Vector2(baseSize.x, 2f * s),
                new Vector2(0f, baseSize.y * 0.5f - 1f * s), primaryAccent).GetComponent<Image>();

            // ==========================================
            // 2. 胶囊顶栏控件布局
            // ==========================================
            // 5 阶信号柱 (Heights: 4, 6, 8, 10, 12)
            float barStartX = -baseSize.x * 0.5f + 12f * s;
            float barW = 2.5f * s;
            float barGap = 1.8f * s;
            for (int i = 0; i < MainBarCount; i++)
            {
                float barH = (4f + i * 2f) * s;
                float bx = barStartX + i * (barW + barGap);
                float by = -baseSize.y * 0.5f + 8f * s + barH * 0.5f;

                GameObject bGo = UIFactory.CreatePanel(_capsuleBar.transform, $"SigBar_{i}",
                    new Vector2(barW, barH), new Vector2(bx, by), primaryAccent);
                _mainSignalBars[i] = bGo.GetComponent<Image>();
            }

            // 控制权状态徽章 (FULL / PART / NONE)
            Vector2 ctrlSize = new Vector2(34f * s, 16f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(_capsuleBar.transform, "CtrlBadge", ctrlSize,
                new Vector2(-baseSize.x * 0.5f + 46f * s, 0f), new Color(0.08f, 0.16f, 0.12f, 0.95f),
                new Color(primaryAccent.r, primaryAccent.g, primaryAccent.b, 0.45f), 1f * s);
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(ctrlBg.transform, "Text", "FULL",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 主站点/对端名称
            _targetNameText = UIFactory.CreateText(_capsuleBar.transform, "TargetName", "KSAT - Singapore",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft, textPrimary);
            RectTransform tgtRt = _targetNameText.GetComponent<RectTransform>();
            tgtRt.sizeDelta = new Vector2(70f * s, 18f * s);
            tgtRt.anchoredPosition = new Vector2(-baseSize.x * 0.5f + 102f * s, 0f);

            // 综合速率与信号 (90% | 15.8K)
            _rateSummaryText = UIFactory.CreateText(_capsuleBar.transform, "RateSummary", "90% | 15.8K",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, secondaryAccent);
            RectTransform rateRt = _rateSummaryText.GetComponent<RectTransform>();
            rateRt.sizeDelta = new Vector2(62f * s, 18f * s);
            rateRt.anchoredPosition = new Vector2(baseSize.x * 0.5f - 76f * s, 0f);

            // 折叠/展开按键 (▼ / ▲)
            Vector2 expBtnSize = new Vector2(16f * s, 16f * s);
            _expandBtn = UIFactory.CreateButton(_capsuleBar.transform, "Btn_Expand", expBtnSize,
                new Vector2(baseSize.x * 0.5f - 36f * s, 0f), OnToggleExpand);
            _expandBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var expOut = _expandBtn.gameObject.AddComponent<Outline>();
            expOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            expOut.effectDistance = new Vector2(1f * s, 1f * s);
            _expandBtnText = UIFactory.CreateText(_expandBtn.transform, "Text", "▼",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _expandBtnText.GetComponent<RectTransform>().sizeDelta = expBtnSize;

            // 原版信号栏显隐按键 (KSP)
            Vector2 stockBtnSize = new Vector2(24f * s, 16f * s);
            _stockBtn = UIFactory.CreateButton(_capsuleBar.transform, "Btn_Stock", stockBtnSize,
                new Vector2(baseSize.x * 0.5f - 14f * s, 0f), OnToggleStock);
            _stockBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var stockOut = _stockBtn.gameObject.AddComponent<Outline>();
            stockOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            stockOut.effectDistance = new Vector2(1f * s, 1f * s);
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
            _stockBtnText.GetComponent<RectTransform>().sizeDelta = stockBtnSize;

            // ==========================================
            // 3. 抽屉式链路矩阵面板 (Dropdown Matrix)
            // ==========================================
            float dropH = 142f * s;
            Vector2 dropSize = new Vector2(baseSize.x, dropH);
            _dropdownPanel = UIFactory.CreatePanel(transform, "DropdownMatrix", dropSize,
                new Vector2(0f, -baseSize.y * 0.5f - dropH * 0.5f - 2f * s),
                new Color(bgCol.r * 0.8f, bgCol.g * 0.8f, bgCol.b * 0.8f, 0.96f), borderCol, 1f * s);

            // 标题
            _matrixTitleText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixTitle", "REALANTENNAS / COMMNET",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform mTitRt = _matrixTitleText.GetComponent<RectTransform>();
            mTitRt.sizeDelta = new Vector2(120f * s, 14f * s);
            mTitRt.anchoredPosition = new Vector2(-dropSize.x * 0.5f + 66f * s, dropSize.y * 0.5f - 10f * s);

            // 双向信号与速率摘要
            _matrixSummaryText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixSummary", "Tx/Rx: 90%/90%  15.8 Kbps",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, textPrimary);
            RectTransform mSumRt = _matrixSummaryText.GetComponent<RectTransform>();
            mSumRt.sizeDelta = new Vector2(110f * s, 14f * s);
            mSumRt.anchoredPosition = new Vector2(dropSize.x * 0.5f - 58f * s, dropSize.y * 0.5f - 10f * s);

            // 分割线
            UIFactory.CreatePanel(_dropdownPanel.transform, "DivLine", new Vector2(dropSize.x - 12f * s, 1f * s),
                new Vector2(0f, dropSize.y * 0.5f - 19f * s), new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f));

            // 对端信道行 (构建 5 行)
            float rowStartY = dropSize.y * 0.5f - 31f * s;
            float rowSpacing = 20f * s;
            for (int r = 0; r < MaxPeerRows; r++)
            {
                _peerRows[r] = BuildPeerRow(_dropdownPanel.transform, $"PeerRow_{r}",
                    new Vector2(dropSize.x - 12f * s, 18f * s),
                    new Vector2(0f, rowStartY - r * rowSpacing), s, secondaryAccent, primaryAccent);
            }

            // 底部脚注
            _matrixFooterText = UIFactory.CreateText(_dropdownPanel.transform, "MatrixFooter",
                "● 5 ACTIVE LINKS  |  RA MATRIX TELEMETRY",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.65f));
            RectTransform footRt = _matrixFooterText.GetComponent<RectTransform>();
            footRt.sizeDelta = new Vector2(dropSize.x, 12f * s);
            footRt.anchoredPosition = new Vector2(0f, -dropSize.y * 0.5f + 8f * s);

            // 初始化根据主题配置执行原版通信指示器静默隐藏
            _stockHidden = ThemeManager.IsStockCommNetHidden;
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden);

            UpdateExpansionLayout();
            ApplyTheme(theme);
        }

        private PeerRowUI BuildPeerRow(Transform parent, string name, Vector2 size, Vector2 pos, float s, Color labelCol, Color barCol)
        {
            PeerRowUI ui = new PeerRowUI();
            ui.RowObj = UIFactory.CreatePanel(parent, name, size, pos, new Color(0.06f, 0.10f, 0.16f, 0.65f));

            // 站点名称 (e.g. KSAT - Singapore)
            ui.NameText = UIFactory.CreateText(ui.RowObj.transform, "Name", "---",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, Color.white);
            RectTransform nRt = ui.NameText.GetComponent<RectTransform>();
            nRt.sizeDelta = new Vector2(102f * s, 16f * s);
            nRt.anchoredPosition = new Vector2(-size.x * 0.5f + 54f * s, 0f);

            // 标签 (DSN / RELAY / VESSEL)
            ui.TagText = UIFactory.CreateText(ui.RowObj.transform, "Tag", "DSN",
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, labelCol);
            RectTransform tRt = ui.TagText.GetComponent<RectTransform>();
            tRt.sizeDelta = new Vector2(30f * s, 14f * s);
            tRt.anchoredPosition = new Vector2(6f * s, 0f);

            // 数据速率 (63.0 Kbps)
            ui.RateText = UIFactory.CreateText(ui.RowObj.transform, "Rate", "0.0 bps",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, labelCol);
            RectTransform rRt = ui.RateText.GetComponent<RectTransform>();
            rRt.sizeDelta = new Vector2(50f * s, 16f * s);
            rRt.anchoredPosition = new Vector2(size.x * 0.5f - 40f * s, 0f);

            // 5 格 mini 信号柱
            ui.MiniBars = new Image[5];
            float miniStartX = size.x * 0.5f - 14f * s;
            for (int b = 0; b < 5; b++)
            {
                float mh = (3f + b * 1.5f) * s;
                float mx = miniStartX + b * (1.8f * s + 1f * s);
                GameObject mbGo = UIFactory.CreatePanel(ui.RowObj.transform, $"MiniBar_{b}",
                    new Vector2(1.8f * s, mh), new Vector2(mx, -size.y * 0.5f + 5f * s + mh * 0.5f), barCol);
                ui.MiniBars[b] = mbGo.GetComponent<Image>();
            }

            return ui;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
            _lastTelemetry = telemetry;

            // 1. 主信号条点亮
            double sig = Mathf.Clamp01((float)telemetry.CommSignal);
            int litBars = Mathf.RoundToInt((float)sig * MainBarCount);
            Color sigColor = (sig > 0.4)
                ? (_currentTheme != null ? (Color)_currentTheme.AccentPrimary : Color.green)
                : ((sig > 0.1) ? (_currentTheme != null ? (Color)_currentTheme.WarningColor : Color.yellow) : Color.red);

            for (int i = 0; i < MainBarCount; i++)
            {
                if (_mainSignalBars[i] == null) continue;
                if (i < litBars)
                {
                    _mainSignalBars[i].color = sigColor;
                }
                else
                {
                    _mainSignalBars[i].color = new Color(sigColor.r, sigColor.g, sigColor.b, 0.15f);
                }
            }

            // 2. 控制权徽章 (FULL / PART / NONE)
            if (_ctrlBadgeText != null && _ctrlBadgeBg != null)
            {
                if (!telemetry.IsConnected && sig <= 0.001)
                {
                    _ctrlBadgeText.text = "NONE";
                    _ctrlBadgeText.color = new Color(1.0f, 0.35f, 0.25f);
                    _ctrlBadgeBg.color = new Color(0.25f, 0.06f, 0.06f, 0.95f);
                }
                else if (sig < 0.2)
                {
                    _ctrlBadgeText.text = "PART";
                    _ctrlBadgeText.color = _currentTheme != null ? (Color)_currentTheme.WarningColor : Color.yellow;
                    _ctrlBadgeBg.color = new Color(0.25f, 0.18f, 0.05f, 0.95f);
                }
                else
                {
                    _ctrlBadgeText.text = "FULL";
                    _ctrlBadgeText.color = _currentTheme != null ? (Color)_currentTheme.AccentPrimary : Color.green;
                    _ctrlBadgeBg.color = new Color(0.06f, 0.18f, 0.10f, 0.95f);
                }
            }

            // 3. 主站点名称与综合速率
            if (_targetNameText != null)
            {
                string tgt = telemetry.DirectLinkTarget;
                _targetNameText.text = string.IsNullOrEmpty(tgt) ? "COMMNET" : tgt;
            }

            if (_rateSummaryText != null)
            {
                string rateStr = CommLinkInfo.FormatRate(telemetry.DataRateBps);
                _rateSummaryText.text = $"{Mathf.RoundToInt((float)sig * 100f)}% | {rateStr}";
            }

            // 4. 抽屉矩阵更新
            if (_dropdownPanel != null && _dropdownPanel.activeSelf)
            {
                if (_matrixSummaryText != null)
                {
                    int tx = Mathf.RoundToInt((float)telemetry.SignalTx * 100f);
                    int rx = Mathf.RoundToInt((float)telemetry.SignalRx * 100f);
                    string rateStr = CommLinkInfo.FormatRate(telemetry.DataRateBps);
                    _matrixSummaryText.text = $"Tx/Rx: {tx}%/{rx}%  {rateStr}";
                }

                var links = telemetry.ActiveCommLinks;
                int linkCount = (links != null) ? links.Count : 0;

                for (int r = 0; r < MaxPeerRows; r++)
                {
                    if (r < linkCount)
                    {
                        var info = links[r];
                        _peerRows[r].RowObj.SetActive(true);
                        _peerRows[r].NameText.text = info.PeerName;
                        _peerRows[r].TagText.text = info.IsDirectHome ? "DSN" : "RELAY";
                        _peerRows[r].RateText.text = info.FormattedDataRate;

                        int peerBars = Mathf.RoundToInt((float)info.SignalStrength * 5f);
                        for (int b = 0; b < 5; b++)
                        {
                            if (_peerRows[r].MiniBars[b] != null)
                            {
                                _peerRows[r].MiniBars[b].color = (b < peerBars)
                                    ? sigColor
                                    : new Color(sigColor.r, sigColor.g, sigColor.b, 0.15f);
                            }
                        }
                    }
                    else
                    {
                        _peerRows[r].RowObj.SetActive(false);
                    }
                }

                if (_matrixFooterText != null)
                {
                    _matrixFooterText.text = $"● {linkCount} ACTIVE LINKS  |  REALANTENNAS PROBE";
                }
            }

            // 5. 原版通信栏开关文本
            if (_stockBtnText != null)
            {
                _stockBtnText.text = _stockHidden ? "KSP" : "MFP";
                _stockBtnText.color = _stockHidden
                    ? new Color(0.6f, 0.7f, 0.8f, 0.9f)
                    : (_currentTheme != null ? (Color)_currentTheme.AccentPrimary : Color.green);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;

            if (_panelBg != null) _panelBg.color = theme.FrameBgColor;
            if (_panelOutline != null) _panelOutline.effectColor = theme.FrameBorderColor;
            if (_topStripe != null) _topStripe.color = theme.AccentPrimary;
            if (_targetNameText != null) _targetNameText.color = theme.TextPrimaryColor;
            if (_rateSummaryText != null) _rateSummaryText.color = theme.AccentSecondary;
            if (_matrixTitleText != null) _matrixTitleText.color = theme.AccentSecondary;
            if (_expandBtnText != null) _expandBtnText.color = theme.AccentSecondary;
        }

        private void OnToggleExpand()
        {
            _isExpanded = !_isExpanded;
            UpdateExpansionLayout();
        }

        private void UpdateExpansionLayout()
        {
            float s = CurrentDpiScale;
            float dropH = 142f * s;
            float capH = 32f * s;
            float totalH = _isExpanded ? (capH + dropH + 2f * s) : capH;

            RectTransform.sizeDelta = new Vector2(236f * s, totalH);

            if (_capsuleBar != null)
            {
                RectTransform capRt = _capsuleBar.GetComponent<RectTransform>();
                float capY = _isExpanded ? (totalH * 0.5f - capH * 0.5f) : 0f;
                capRt.anchoredPosition = new Vector2(0f, capY);
            }

            if (_dropdownPanel != null)
            {
                _dropdownPanel.SetActive(_isExpanded);
                if (_isExpanded)
                {
                    RectTransform dropRt = _dropdownPanel.GetComponent<RectTransform>();
                    float dropY = -totalH * 0.5f + dropH * 0.5f;
                    dropRt.anchoredPosition = new Vector2(0f, dropY);
                }
            }

            if (_expandBtnText != null)
            {
                _expandBtnText.text = _isExpanded ? "▲" : "▼";
            }
        }

        private void OnToggleStock()
        {
            _stockHidden = !_stockHidden;
            NavBallHookService.HideStockCommNetAction?.Invoke(_stockHidden);
            ThemeManager.IsStockCommNetHidden = _stockHidden;
            ThemeManager.Instance.SaveSettings();
        }
    }
}
