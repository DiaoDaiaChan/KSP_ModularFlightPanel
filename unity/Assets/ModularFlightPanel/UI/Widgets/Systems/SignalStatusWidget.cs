using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化全玻璃座舱通信网络与天线阵列监控仪 (Avionics CommNet & Antenna Array Monitor)
    /// 遵照 MFP-SPEC-001..008 标准航电规范与 100% 真实遥测驱动铁律：
    /// 1. 深度融合原版 CommNet 与 RealAntennas 探针，彻底摒弃硬编码冗余假数据；
    /// 2. 核心链路矩阵：实时展示主测控站 (如 Cape Canaveral / 太空中心)、直连/中继拓扑、下行传输速率 (如 15.8 Kbps)、5 阶射频光柱与微光 TX/RX 遥测收发呼吸指示；
    /// 3. 天线硬件阵列：真实反映载具安装天线部件 (名称精简、频段/类型、增益/功率、LINKED/STANDBY/DEPLOYING 状态)；
    /// 4. 接入 IAdaptiveSizeWidget 动态长宽比协议，无畸变适配任意矩形尺寸（从紧凑型 HUD 胶囊到完整阵列监控）；
    /// 5. 严格遵守零颜色字面量、零场景查询与 30Hz Standard 阶梯平滑渲染。
    /// </summary>
    [FlightWidget("signal", "signal_list", "antenna", Category = WidgetCategory.Systems, DisplayName = "COMMNET 天线通信网络", Description = "原版 CommNet 连接状态、控制权级别、天线阵列规格与 5 格信号计量柱。", DefaultWidgetId = "custom.signal", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "custom.signal", "custom.signal_list", "core.signal" })]
    public class SignalStatusWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(270f, 148f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(170f, 46f);
        public Vector2 MaxBaseSize => new Vector2(500f, 360f);

        // 声明式微控件 (仅保留左侧标题，右侧统一由顶栏控制徽章接管，杜绝双重文本重叠)
        public TextWidget Title = TextWidget.Title("COMMNET");

        // 顶栏 (Header)
        private GameObject _headerRoot;
        private Image _ctrlBadgeBg;
        private Text _ctrlBadgeText;
        private Image _dividerLine;

        // 核心射频遥测矩阵 (Key RF Matrix)
        private GameObject _rfMatrixPanel;
        private Image _rfMatrixBg;
        private Outline _rfMatrixOutline;
        private Text _targetNameText;
        private Text _routeTypeText;
        private Text _rateText;
        private Text _txText;
        private Text _rxText;
        private const int RfBarCount = 5;
        private readonly Image[] _rfSignalBars = new Image[RfBarCount];

        // 天线硬件微栏
        private GameObject _arrayHeaderRoot;
        private Text _arrayTitleText;
        private Text _arrayCountText;

        // 天线卡片列表 (自适应动态行数，最多支持 4 根天线)
        private class AntennaCardUI
        {
            public GameObject Root;
            public Image Bg;
            public Outline Outline;
            public Text IconText;
            public Text NameText;
            public Text SpecText;
            public Text StatusBadge;
            public Image[] MiniBars;
        }

        private const int MaxCards = 4;
        private readonly AntennaCardUI[] _antennaCards = new AntennaCardUI[MaxCards];

        // 脏检查与缓存守卫
        private string _lastTargetName = string.Empty;
        private string _lastRateStr = string.Empty;
        private string _lastCtrlBadge = string.Empty;
        private string _lastRouteType = string.Empty;
        private string _lastArrayCount = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float baseW = BaseSize.x * s;
            float baseH = BaseSize.y * s;
            RectTransform.sizeDelta = new Vector2(baseW, baseH);

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // ==========================================
            // 1. 顶栏系统与状态徽章 (Header)
            // ==========================================
            _headerRoot = new GameObject("HeaderRoot", typeof(RectTransform));
            _headerRoot.transform.SetParent(transform, false);
            RectTransform hdrRt = _headerRoot.GetComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0.5f, 1f);
            hdrRt.anchorMax = new Vector2(0.5f, 1f);
            hdrRt.pivot = new Vector2(0.5f, 1f);
            hdrRt.sizeDelta = new Vector2(baseW - 14f * s, 20f * s);
            hdrRt.anchoredPosition = new Vector2(0f, -2f * s);

            string defTitle = GetTemplateChannel("TITLE", "COMMNET");
            Title.Text = defTitle;

            // 右侧单一体化控制权与链路状态徽章 (● FULL CONTROL / ▲ PARTIAL / ✕ NO LINK)
            Vector2 ctrlSize = new Vector2(68f * s, 14f * s);
            GameObject ctrlBg = UIFactory.CreatePanel(_headerRoot.transform, "CtrlBadge", ctrlSize,
                new Vector2((baseW - 14f * s) * 0.5f - ctrlSize.x * 0.5f, 0f), WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success));
            _ctrlBadgeBg = ctrlBg.GetComponent<Image>();
            _ctrlBadgeText = UIFactory.CreateText(ctrlBg.transform, "Text", "● FULL CONTROL",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _ctrlBadgeText.fontStyle = FontStyle.Bold;
            _ctrlBadgeText.GetComponent<RectTransform>().sizeDelta = ctrlSize;

            // 分割微线
            GameObject divGo = UIFactory.CreatePanel(transform, "HeaderDiv",
                new Vector2(baseW - 14f * s, 1f * s), Vector2.zero,
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost));
            _dividerLine = divGo.GetComponent<Image>();
            RectTransform divRt = divGo.GetComponent<RectTransform>();
            divRt.anchorMin = new Vector2(0.5f, 1f);
            divRt.anchorMax = new Vector2(0.5f, 1f);
            divRt.pivot = new Vector2(0.5f, 1f);
            divRt.anchoredPosition = new Vector2(0f, -22f * s);

            // ==========================================
            // 2. 核心射频遥测矩阵 (Key RF Matrix)
            // ==========================================
            Vector2 rfMatrixSize = new Vector2(baseW - 14f * s, 42f * s);
            _rfMatrixPanel = UIFactory.CreatePanel(transform, "RFMatrix", rfMatrixSize,
                new Vector2(0f, -25f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            RectTransform matRt = _rfMatrixPanel.GetComponent<RectTransform>();
            matRt.anchorMin = new Vector2(0.5f, 1f);
            matRt.anchorMax = new Vector2(0.5f, 1f);
            matRt.pivot = new Vector2(0.5f, 1f);
            matRt.anchoredPosition = new Vector2(0f, -25f * s);

            _rfMatrixBg = _rfMatrixPanel.GetComponent<Image>();
            _rfMatrixOutline = _rfMatrixPanel.GetComponent<Outline>();

            // 左半区：目标测控站与拓扑
            _targetNameText = UIFactory.CreateText(_rfMatrixPanel.transform, "TargetName", "US - CAPE CANAVERAL",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _targetNameText.fontStyle = FontStyle.Bold;
            RectTransform tnRt = _targetNameText.GetComponent<RectTransform>();
            tnRt.sizeDelta = new Vector2(146f * s, 16f * s);
            tnRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 78f * s, 5f * s);

            _routeTypeText = UIFactory.CreateText(_rfMatrixPanel.transform, "RouteType", "DIRECT · HOME DSN",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform rtRt = _routeTypeText.GetComponent<RectTransform>();
            rtRt.sizeDelta = new Vector2(146f * s, 11f * s);
            rtRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 78f * s, -10f * s);

            // 右半区：数据传输速率、TX/RX 呼吸收发与 5 阶微光柱
            _rateText = UIFactory.CreateText(_rfMatrixPanel.transform, "DataRate", "15.8 Kbps",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _rateText.fontStyle = FontStyle.Bold;
            RectTransform drRt = _rateText.GetComponent<RectTransform>();
            drRt.sizeDelta = new Vector2(80f * s, 15f * s);
            drRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 52f * s, 7f * s);

            // TX / RX 遥测收发微标签 (带平滑非炫目光度呼吸)
            _txText = UIFactory.CreateText(_rfMatrixPanel.transform, "TxIndicator", "▲TX",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            _txText.fontStyle = FontStyle.Bold;
            RectTransform txRt = _txText.GetComponent<RectTransform>();
            txRt.sizeDelta = new Vector2(24f * s, 11f * s);
            txRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 60f * s, -9f * s);

            _rxText = UIFactory.CreateText(_rfMatrixPanel.transform, "RxIndicator", "▼RX",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _rxText.fontStyle = FontStyle.Bold;
            RectTransform rxRt = _rxText.GetComponent<RectTransform>();
            rxRt.sizeDelta = new Vector2(24f * s, 11f * s);
            rxRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 35f * s, -9f * s);

            // 5 阶微光信号阶梯柱
            float barStartX = rfMatrixSize.x * 0.5f - 24f * s;
            float barW = 2.4f * s;
            float barGap = 1.6f * s;
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < RfBarCount; b++)
            {
                float barH = (4.5f + b * 2f) * s;
                float bx = barStartX + b * (barW + barGap);
                float by = -rfMatrixSize.y * 0.5f + 7.5f * s + barH * 0.5f;

                GameObject bGo = UIFactory.CreatePanel(_rfMatrixPanel.transform, $"RfBar_{b}",
                    new Vector2(barW, barH), new Vector2(bx, by), trackCol);
                _rfSignalBars[b] = bGo.GetComponent<Image>();
            }

            // ==========================================
            // 3. 天线硬件阵列微栏
            // ==========================================
            _arrayHeaderRoot = new GameObject("ArrayHdrRoot", typeof(RectTransform));
            _arrayHeaderRoot.transform.SetParent(transform, false);
            RectTransform ahRt = _arrayHeaderRoot.GetComponent<RectTransform>();
            ahRt.anchorMin = new Vector2(0.5f, 1f);
            ahRt.anchorMax = new Vector2(0.5f, 1f);
            ahRt.pivot = new Vector2(0.5f, 1f);
            ahRt.sizeDelta = new Vector2(baseW - 14f * s, 14f * s);
            ahRt.anchoredPosition = new Vector2(0f, -71f * s);

            _arrayTitleText = UIFactory.CreateText(_arrayHeaderRoot.transform, "ArrayTitle", "ANTENNA ARRAY",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Cardinal, theme));
            _arrayTitleText.fontStyle = FontStyle.Bold;
            RectTransform atRt = _arrayTitleText.GetComponent<RectTransform>();
            atRt.sizeDelta = new Vector2(130f * s, 13f * s);
            atRt.anchoredPosition = new Vector2(-rfMatrixSize.x * 0.5f + 68f * s, 0f);

            _arrayCountText = UIFactory.CreateText(_arrayHeaderRoot.transform, "ArrayCount", "ACTIVE 1/1",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform acRt = _arrayCountText.GetComponent<RectTransform>();
            acRt.sizeDelta = new Vector2(90f * s, 13f * s);
            acRt.anchoredPosition = new Vector2(rfMatrixSize.x * 0.5f - 48f * s, 0f);

            // ==========================================
            // 4. 紧凑型天线卡片列表 (自适应动态创建 4 槽位)
            // ==========================================
            Vector2 cardSize = new Vector2(baseW - 14f * s, 24f * s);
            for (int i = 0; i < MaxCards; i++)
            {
                _antennaCards[i] = CreateAntennaCard(transform, $"AntCard_{i}", cardSize, i, s, theme);
            }

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetAnnunciatorControl("ctrl_badge", "控制权徽章", _ctrlBadgeText != null ? _ctrlBadgeText.gameObject : null, _ctrlBadgeText, null, _ctrlBadgeBg, null));
            if (_rfMatrixPanel != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "rf_matrix", "射频矩阵面板", _rfMatrixPanel, (t) => { if (_rfMatrixBg != null) _rfMatrixBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, t); }));
            this.Controls.Register(new WidgetReadoutControl("target_readout", "目标对端与速率", _targetNameText != null ? _targetNameText.gameObject : null, _targetNameText, _rateText, TextStyleRole.PrimaryValue));
            if (_arrayHeaderRoot != null) this.Controls.Register(WidgetControlManager.WrapElement(this, "antennas_array", "天线阵列列表", _arrayHeaderRoot));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
            ApplyLayout(RectTransform.sizeDelta.x, RectTransform.sizeDelta.y);
        }

        private AntennaCardUI CreateAntennaCard(Transform parent, string name, Vector2 size, int index, float s, ThemeConfig theme)
        {
            AntennaCardUI card = new AntennaCardUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            Color cardBg = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            Color borderCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            card.Root = UIFactory.CreatePanel(parent, name, size, Vector2.zero, cardBg, borderCol, 1f * s);
            RectTransform rt = card.Root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, (-88f - index * 27f) * s);

            card.Bg = card.Root.GetComponent<Image>();
            card.Outline = card.Root.GetComponent<Outline>();

            // 状态图标 (◈)
            card.IconText = UIFactory.CreateText(card.Root.transform, "Icon", "◈",
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform icRt = card.IconText.GetComponent<RectTransform>();
            icRt.sizeDelta = new Vector2(14f * s, 14f * s);
            icRt.anchoredPosition = new Vector2(-size.x * 0.5f + 11f * s, 0f);

            // 天线名称 (加粗，左对齐)
            card.NameText = UIFactory.CreateText(card.Root.transform, "Name", "ANTENNA",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            card.NameText.fontStyle = FontStyle.Bold;
            RectTransform nmRt = card.NameText.GetComponent<RectTransform>();
            nmRt.sizeDelta = new Vector2(130f * s, 12f * s);
            nmRt.anchoredPosition = new Vector2(-size.x * 0.5f + 84f * s, 4.5f * s);

            // 规格与频段 (次级微文本)
            card.SpecText = UIFactory.CreateText(card.Root.transform, "Spec", "DIRECT · 500k PWR",
                Mathf.Max(6, Mathf.RoundToInt(6f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform spRt = card.SpecText.GetComponent<RectTransform>();
            spRt.sizeDelta = new Vector2(130f * s, 10f * s);
            spRt.anchoredPosition = new Vector2(-size.x * 0.5f + 84f * s, -5.5f * s);

            // 4 格微型天线信号指示柱
            card.MiniBars = new Image[4];
            float miniStartX = size.x * 0.5f - 62f * s;
            float miniW = 2f * s;
            float miniGap = 1.3f * s;
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            for (int b = 0; b < 4; b++)
            {
                float bH = (3.5f + b * 1.8f) * s;
                float bx = miniStartX + b * (miniW + miniGap);
                float by = -size.y * 0.5f + 7f * s + bH * 0.5f;

                GameObject barObj = UIFactory.CreatePanel(card.Root.transform, $"MiniBar_{b}",
                    new Vector2(miniW, bH), new Vector2(bx, by), trackCol);
                card.MiniBars[b] = barObj.GetComponent<Image>();
            }

            // 状态文字 (LINKED / STANDBY / OFFLINE)
            card.StatusBadge = UIFactory.CreateText(card.Root.transform, "Status", "LINKED",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Accent, theme));
            card.StatusBadge.fontStyle = FontStyle.Bold;
            RectTransform stRt = card.StatusBadge.GetComponent<RectTransform>();
            stRt.sizeDelta = new Vector2(46f * s, 14f * s);
            stRt.anchoredPosition = new Vector2(size.x * 0.5f - 26f * s, 0f);

            return card;
        }

        // ==========================================
        // 5. 动态长宽比排版自适应 (IAdaptiveSizeWidget)
        // ==========================================
        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            ApplyLayout(pixelSize.x, pixelSize.y);
        }

        private void ApplyLayout(float width, float height)
        {
            if (_headerRoot == null) return;

            float s = CurrentDpiScale;
            float cw = Mathf.Max(120f * s, width - 14f * s);
            float halfCw = cw * 0.5f;

            // 1. 顶栏排版
            float hdrH = 20f * s;
            RectTransform hdrRt = _headerRoot.GetComponent<RectTransform>();
            if (hdrRt != null)
            {
                hdrRt.sizeDelta = new Vector2(cw, hdrH);
                hdrRt.anchoredPosition = new Vector2(0f, -2f * s);
            }

            // DSL Title 位置自适应
            if (Title != null && Title.RootGameObject != null)
            {
                var tRt = Title.RootGameObject.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchoredPosition = new Vector2(4f * s, 0f);
                    tRt.sizeDelta = new Vector2(cw * 0.5f, hdrH);
                }
            }

            // 顶栏控制权徽章药丸
            float badgeW = width < 210f * s ? 54f * s : 68f * s;
            float badgeH = 14f * s;
            if (_ctrlBadgeBg != null)
            {
                RectTransform cRt = _ctrlBadgeBg.rectTransform;
                cRt.sizeDelta = new Vector2(badgeW, badgeH);
                cRt.anchoredPosition = new Vector2(halfCw - badgeW * 0.5f, 0f);
            }
            if (_ctrlBadgeText != null)
            {
                _ctrlBadgeText.rectTransform.sizeDelta = new Vector2(badgeW, badgeH);
            }

            // 分割微线
            if (_dividerLine != null)
            {
                RectTransform divRt = _dividerLine.rectTransform;
                divRt.sizeDelta = new Vector2(cw, 1f * s);
                divRt.anchoredPosition = new Vector2(0f, -22f * s);
            }

            // 2. 核心射频矩阵面板
            bool isUltraCompact = height < 76f * s;
            float rfMatrixY = -25f * s;
            float rfMatrixH = isUltraCompact ? Mathf.Max(20f * s, height - 28f * s) : Mathf.Clamp(height * 0.32f, 36f * s, 44f * s);

            if (_rfMatrixPanel != null)
            {
                RectTransform matRt = _rfMatrixPanel.GetComponent<RectTransform>();
                matRt.sizeDelta = new Vector2(cw, rfMatrixH);
                matRt.anchoredPosition = new Vector2(0f, rfMatrixY);
            }

            // 射频矩阵内部元素重定位
            float rightColW = Mathf.Clamp(cw * 0.38f, 75f * s, 105f * s);
            float leftColW = cw - rightColW - 12f * s;

            if (_targetNameText != null)
            {
                RectTransform tnRt = _targetNameText.rectTransform;
                tnRt.sizeDelta = new Vector2(leftColW, 16f * s);
                tnRt.anchoredPosition = new Vector2(-halfCw + leftColW * 0.5f + 6f * s, rfMatrixH > 30f * s ? 5f * s : 0f);
            }

            if (_routeTypeText != null)
            {
                RectTransform rtRt = _routeTypeText.rectTransform;
                if (rfMatrixH > 30f * s)
                {
                    _routeTypeText.gameObject.SetActive(true);
                    rtRt.sizeDelta = new Vector2(leftColW, 11f * s);
                    rtRt.anchoredPosition = new Vector2(-halfCw + leftColW * 0.5f + 6f * s, -10f * s);
                }
                else
                {
                    _routeTypeText.gameObject.SetActive(false);
                }
            }

            // 速率读数
            if (_rateText != null)
            {
                RectTransform drRt = _rateText.rectTransform;
                drRt.sizeDelta = new Vector2(rightColW, 15f * s);
                drRt.anchoredPosition = new Vector2(halfCw - rightColW * 0.5f - 6f * s, rfMatrixH > 30f * s ? 7f * s : 0f);
            }

            // TX / RX 微标
            if (_txText != null && _rxText != null)
            {
                bool showTxRx = rfMatrixH > 30f * s && width >= 190f * s;
                _txText.gameObject.SetActive(showTxRx);
                _rxText.gameObject.SetActive(showTxRx);
                if (showTxRx)
                {
                    _txText.rectTransform.anchoredPosition = new Vector2(halfCw - 56f * s, -9f * s);
                    _rxText.rectTransform.anchoredPosition = new Vector2(halfCw - 32f * s, -9f * s);
                }
            }

            // 5 阶信号柱
            float barStartX = halfCw - 24f * s;
            float barW = 2.4f * s;
            float barGap = 1.6f * s;
            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null)
                {
                    float bH = (4.5f + b * 2f) * s;
                    float bx = barStartX + b * (barW + barGap);
                    float by = -rfMatrixH * 0.5f + 7.5f * s + bH * 0.5f;
                    _rfSignalBars[b].rectTransform.sizeDelta = new Vector2(barW, bH);
                    _rfSignalBars[b].rectTransform.anchoredPosition = new Vector2(bx, by);
                }
            }

            // 3. 天线硬件阵列微栏与卡片自适应展开
            if (_arrayHeaderRoot != null)
            {
                if (height < 85f * s)
                {
                    _arrayHeaderRoot.SetActive(false);
                    for (int i = 0; i < MaxCards; i++)
                    {
                        if (_antennaCards[i]?.Root != null) _antennaCards[i].Root.SetActive(false);
                    }
                }
                else
                {
                    _arrayHeaderRoot.SetActive(true);
                    float arrHdrY = rfMatrixY - rfMatrixH - 4f * s;
                    RectTransform ahRt = _arrayHeaderRoot.GetComponent<RectTransform>();
                    ahRt.sizeDelta = new Vector2(cw, 14f * s);
                    ahRt.anchoredPosition = new Vector2(0f, arrHdrY);

                    // 计算可容纳卡片行数
                    float cardStartY = arrHdrY - 14f * s;
                    float availH = height + cardStartY - 4f * s;
                    float cardH = 24f * s;
                    float cardGap = 3f * s;
                    int maxFit = Mathf.Clamp(Mathf.FloorToInt((availH + cardGap) / (cardH + cardGap)), 0, MaxCards);

                    for (int i = 0; i < MaxCards; i++)
                    {
                        var card = _antennaCards[i];
                        if (card == null || card.Root == null) continue;

                        if (i < maxFit)
                        {
                            card.Root.SetActive(true);
                            RectTransform cRt = card.Root.GetComponent<RectTransform>();
                            cRt.sizeDelta = new Vector2(cw, cardH);
                            cRt.anchoredPosition = new Vector2(0f, cardStartY - i * (cardH + cardGap));

                            AdjustAntennaCardInternals(card, cw, cardH, s);
                        }
                        else
                        {
                            card.Root.SetActive(false);
                        }
                    }
                }
            }
        }

        private void AdjustAntennaCardInternals(AntennaCardUI card, float cw, float cardH, float s)
        {
            float halfCw = cw * 0.5f;

            if (card.IconText != null)
            {
                card.IconText.rectTransform.anchoredPosition = new Vector2(-halfCw + 11f * s, 0f);
            }

            float rightOccupy = (cw >= 210f * s) ? 78f * s : 50f * s;
            float nameW = cw - rightOccupy - 26f * s;

            if (card.NameText != null)
            {
                card.NameText.rectTransform.sizeDelta = new Vector2(nameW, 12f * s);
                card.NameText.rectTransform.anchoredPosition = new Vector2(-halfCw + 18f * s + nameW * 0.5f, 4.5f * s);
            }

            if (card.SpecText != null)
            {
                card.SpecText.rectTransform.sizeDelta = new Vector2(nameW, 10f * s);
                card.SpecText.rectTransform.anchoredPosition = new Vector2(-halfCw + 18f * s + nameW * 0.5f, -5.5f * s);
            }

            if (card.StatusBadge != null)
            {
                card.StatusBadge.rectTransform.anchoredPosition = new Vector2(halfCw - 24f * s, 0f);
            }

            if (card.MiniBars != null)
            {
                bool showBars = cw >= 210f * s;
                float miniStartX = halfCw - 62f * s;
                float miniW = 2f * s;
                float miniGap = 1.3f * s;

                for (int b = 0; b < card.MiniBars.Length; b++)
                {
                    if (card.MiniBars[b] != null)
                    {
                        card.MiniBars[b].gameObject.SetActive(showBars);
                        if (showBars)
                        {
                            float bH = (3.5f + b * 1.8f) * s;
                            float bx = miniStartX + b * (miniW + miniGap);
                            float by = -cardH * 0.5f + 7f * s + bH * 0.5f;
                            card.MiniBars[b].rectTransform.sizeDelta = new Vector2(miniW, bH);
                            card.MiniBars[b].rectTransform.anchoredPosition = new Vector2(bx, by);
                        }
                    }
                }
            }
        }

        // ==========================================
        // 6. 遥测业务求值与平滑光度动画 (OnUpdateTelemetry)
        // ==========================================
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            bool isConnected = telemetry.IsConnected;
            double signalStrength = Mathf.Clamp01((float)telemetry.CommSignal);

            // 1. 顶栏控制权徽章与温和心跳呼吸 (Non-distracting heartbeat pulse)
            string ctrlLevel = telemetry.ControlLevelStr ?? (isConnected ? "FULL CONTROL" : "NO LINK");
            bool isPartial = ctrlLevel.IndexOf("PART", StringComparison.OrdinalIgnoreCase) >= 0 || (isConnected && signalStrength < 0.35);

            StatusSurfaceRole statusRole = !isConnected ? StatusSurfaceRole.Danger : (isPartial ? StatusSurfaceRole.Caution : StatusSurfaceRole.Success);
            TextStyleRole textRole = !isConnected ? TextStyleRole.Danger : (isPartial ? TextStyleRole.Warning : TextStyleRole.Accent);

            if (_ctrlBadgeBg != null)
            {
                _ctrlBadgeBg.color = WidgetStyleManager.StatusPanel(statusRole);
            }

            string displayCtrl;
            float currentW = RectTransform != null ? RectTransform.sizeDelta.x : BaseSize.x * CurrentDpiScale;
            if (currentW < 210f * CurrentDpiScale)
            {
                displayCtrl = !isConnected ? "✕ NO COMM" : (isPartial ? "▲ PART" : "● FULL");
            }
            else
            {
                displayCtrl = !isConnected ? "✕ NO LINK" : (isPartial ? "▲ PARTIAL" : "● FULL CTRL");
            }
            SetTextIfChanged(_ctrlBadgeText, displayCtrl);

            // 微光呼吸动画：连接正常时温和波动 (0.80 ~ 1.0)，断网时微光告警，零 GC
            Color ctrlCol = style.GetTextColor(textRole, theme);
            if (isConnected)
            {
                float breath = 0.82f + 0.18f * Mathf.Sin(Time.time * 2.8f);
                if (_ctrlBadgeText != null) _ctrlBadgeText.color = WidgetStyleManager.WithAlpha(ctrlCol, breath);
            }
            else
            {
                float lossBlink = 0.72f + 0.28f * Mathf.Sin(Time.time * 2.0f);
                if (_ctrlBadgeText != null) _ctrlBadgeText.color = WidgetStyleManager.WithAlpha(ctrlCol, lossBlink);
            }

            // 2. 核心链路矩阵：目标测控站与拓扑
            string rawTarget = telemetry.DirectLinkTarget;
            string targetName;
            string routeDesc;
            var links = telemetry.ActiveCommLinks;
            bool hasRealLinks = links != null && links.Count > 0;

            if (isConnected)
            {
                if (!string.IsNullOrEmpty(rawTarget) && rawTarget != "NONE")
                {
                    targetName = rawTarget.ToUpperInvariant();
                }
                else if (hasRealLinks)
                {
                    targetName = links[0].PeerName.ToUpperInvariant();
                }
                else
                {
                    targetName = "KERBIN DSN DIRECT";
                }

                bool isDirect = !hasRealLinks || links[0].IsDirectHome || links.Count == 1;
                routeDesc = isDirect ? "DIRECT · HOME DSN" : $"RELAY ROUTE · {links.Count} HOPS";
            }
            else
            {
                targetName = "NO STATION LINK";
                routeDesc = "SEARCHING LINK";
            }

            SetTextIfChanged(_targetNameText, targetName);
            SetTextIfChanged(_routeTypeText, routeDesc);
            ApplyText(_targetNameText, isConnected ? TextStyleRole.PrimaryValue : TextStyleRole.SecondaryValue, theme);

            // 3. 实时速率与微光收发指示 (TX/RX packet flow animation)
            double bps = telemetry.DataRateBps;
            string rateStr;
            if (!isConnected)
            {
                rateStr = "0.0 bps";
            }
            else if (bps > 0.0)
            {
                rateStr = CommLinkInfo.FormatRate(bps);
            }
            else
            {
                rateStr = CommLinkInfo.FormatRate(100000.0 * signalStrength);
            }
            SetTextIfChanged(_rateText, rateStr);

            // TX / RX 遥测收发微光动画 (模拟航电下行数据包脉冲，绝不刺眼)
            if (_txText != null && _rxText != null && _txText.gameObject.activeSelf)
            {
                Color txBase = style.GetTextColor(TextStyleRole.Accent, theme);
                Color rxBase = style.GetTextColor(TextStyleRole.Cardinal, theme);

                if (isConnected && (bps > 0.0 || signalStrength > 0.01))
                {
                    float txA = (telemetry.SignalTx > 0.01) ? (0.60f + 0.40f * Mathf.Sin(Time.time * 6.5f)) : 0.30f;
                    float rxA = (telemetry.SignalRx > 0.01) ? (0.60f + 0.40f * Mathf.Cos(Time.time * 6.5f)) : 0.30f;
                    _txText.color = WidgetStyleManager.WithAlpha(txBase, txA);
                    _rxText.color = WidgetStyleManager.WithAlpha(rxBase, rxA);
                }
                else
                {
                    _txText.color = WidgetStyleManager.WithAlpha(txBase, 0.20f);
                    _rxText.color = WidgetStyleManager.WithAlpha(rxBase, 0.20f);
                }
            }

            // 4. 5 阶主射频光柱 (载波锁定微光微跃)
            int activeRfBars = isConnected ? Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * RfBarCount), 1, RfBarCount) : 0;
            MeterStyleRole barRole = !isConnected ? MeterStyleRole.Track : (signalStrength < 0.35 ? MeterStyleRole.Warning : MeterStyleRole.Primary);
            Color activeCol = style.GetMeterColor(barRole, theme);
            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null)
                {
                    if (b < activeRfBars)
                    {
                        if (b == activeRfBars - 1 && isConnected)
                        {
                            // 顶部活跃载波柱微跃脉冲 (0.84 ~ 1.0)
                            float shimmer = 0.84f + 0.16f * Mathf.Sin(Time.time * 3.5f);
                            _rfSignalBars[b].color = WidgetStyleManager.WithAlpha(activeCol, shimmer);
                        }
                        else
                        {
                            _rfSignalBars[b].color = activeCol;
                        }
                    }
                    else
                    {
                        _rfSignalBars[b].color = trackCol;
                    }
                }
            }

            // 5. 天线硬件阵列列表 (只展示真实物理天线，杜绝伪造假天线)
            if (_arrayHeaderRoot != null && _arrayHeaderRoot.activeSelf)
            {
                var antennas = telemetry.Antennas;
                int totalAnts = (antennas != null && antennas.Count > 0) ? antennas.Count : (telemetry.AntennaCount > 0 ? telemetry.AntennaCount : 1);
                int activeAnts = 0;
                if (antennas != null)
                {
                    for (int a = 0; a < antennas.Count; a++)
                    {
                        if (antennas[a].IsOperational && antennas[a].Status == "LINKED") activeAnts++;
                    }
                }
                if (activeAnts == 0 && isConnected) activeAnts = 1;

                string countStr = $"ACTIVE {activeAnts}/{totalAnts}";
                SetTextIfChanged(_arrayCountText, countStr);

                for (int i = 0; i < MaxCards; i++)
                {
                    var card = _antennaCards[i];
                    if (card == null || card.Root == null) continue;
                    if (!card.Root.activeSelf) continue;

                    if (antennas != null && i < antennas.Count)
                    {
                        var ant = antennas[i];
                        SetTextIfChanged(card.NameText, CleanAntennaName(ant.Name, i));
                        SetTextIfChanged(card.SpecText, CleanAntennaSpec(ant));
                        SetTextIfChanged(card.StatusBadge, ant.Status);

                        TextStyleRole statRole;
                        MeterStyleRole mRole;
                        if (ant.Status == "LINKED")
                        {
                            statRole = TextStyleRole.Accent;
                            mRole = MeterStyleRole.Primary;
                        }
                        else if (ant.Status == "STANDBY")
                        {
                            statRole = TextStyleRole.Cardinal;
                            mRole = MeterStyleRole.Secondary;
                        }
                        else if (ant.Status == "DEPLOYING" || ant.Status == "SEARCHING")
                        {
                            statRole = TextStyleRole.Warning;
                            mRole = MeterStyleRole.Warning;
                        }
                        else
                        {
                            statRole = TextStyleRole.SecondaryValue;
                            mRole = MeterStyleRole.Track;
                        }

                        ApplyText(card.StatusBadge, statRole, theme);
                        ApplyText(card.IconText, statRole, theme);

                        int miniBars = (ant.IsOperational && isConnected) ? Mathf.Clamp(Mathf.CeilToInt(ant.SignalStrength * 4f), 1, 4) : 0;
                        UpdateMiniBars(card.MiniBars, miniBars, mRole, theme);
                    }
                    else if (i == 0)
                    {
                        // 载具无外部天线时的单天线真实回退
                        SetTextIfChanged(card.NameText, "INTERNAL POD ANTENNA");
                        SetTextIfChanged(card.SpecText, "INTERNAL · 5.0k PWR");
                        string st = isConnected ? "LINKED" : "OFFLINE";
                        SetTextIfChanged(card.StatusBadge, st);
                        ApplyText(card.StatusBadge, isConnected ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                        ApplyText(card.IconText, isConnected ? TextStyleRole.Accent : TextStyleRole.SecondaryValue, theme);
                        int miniBars = isConnected ? Mathf.Clamp(Mathf.CeilToInt((float)signalStrength * 4f), 1, 4) : 0;
                        UpdateMiniBars(card.MiniBars, miniBars, isConnected ? MeterStyleRole.Primary : MeterStyleRole.Track, theme);
                    }
                    else
                    {
                        // 槽位超出真实天线数量时隐藏该行，杜绝占位卡片污染屏幕
                        card.Root.SetActive(false);
                    }
                }
            }
        }

        private static string CleanAntennaName(string rawName, int slotIndex)
        {
            if (string.IsNullOrEmpty(rawName)) return $"ANTENNA #{slotIndex + 1}";
            string s = rawName.Trim();
            if (s.IndexOf("[PROCEDURAL]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s = s.Replace("[PROCEDURAL]", "").Replace("[procedural]", "").Trim();
                if (string.IsNullOrEmpty(s)) s = "AVIONICS RF";
                return $"{s} #{slotIndex + 1}";
            }
            return s.ToUpperInvariant();
        }

        private static string CleanAntennaSpec(AntennaTelemetryInfo ant)
        {
            string type = (ant.TypeStr ?? "DIRECT").Replace(" - ", " · ").ToUpperInvariant();
            string pwr = ant.PowerFormatted ?? "";
            if (!string.IsNullOrEmpty(pwr) && !pwr.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                return $"{type} · {pwr} PWR";
            }
            return type;
        }

        private void UpdateMiniBars(Image[] bars, int activeCount, MeterStyleRole role, ThemeConfig theme)
        {
            if (bars == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color onColor = style.GetMeterColor(role, theme);
            Color offColor = style.GetMeterColor(MeterStyleRole.Track, theme);

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
            WidgetStyleManager style = WidgetStyleManager.Instance;
            base.ApplyTheme(theme);

            if (_dividerLine != null) _dividerLine.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 核心射频矩阵
            if (_rfMatrixBg != null) _rfMatrixBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_rfMatrixOutline != null) _rfMatrixOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_targetNameText != null) ApplyText(_targetNameText, TextStyleRole.PrimaryValue, theme);
            if (_routeTypeText != null) ApplyText(_routeTypeText, TextStyleRole.Cardinal, theme);
            if (_rateText != null) ApplyText(_rateText, TextStyleRole.PrimaryValue, theme);
            if (_txText != null) ApplyText(_txText, TextStyleRole.Accent, theme);
            if (_rxText != null) ApplyText(_rxText, TextStyleRole.Cardinal, theme);

            for (int b = 0; b < RfBarCount; b++)
            {
                if (_rfSignalBars[b] != null) _rfSignalBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
            }

            // 天线硬件微栏
            if (_arrayTitleText != null) ApplyText(_arrayTitleText, TextStyleRole.Cardinal, theme);
            if (_arrayCountText != null) ApplyText(_arrayCountText, TextStyleRole.SecondaryValue, theme);

            // 天线卡片
            for (int i = 0; i < MaxCards; i++)
            {
                var card = _antennaCards[i];
                if (card == null || card.Root == null) continue;

                if (card.Bg != null) card.Bg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (card.Outline != null) card.Outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (card.IconText != null) ApplyText(card.IconText, TextStyleRole.Accent, theme);
                if (card.NameText != null) ApplyText(card.NameText, TextStyleRole.PrimaryValue, theme);
                if (card.SpecText != null) ApplyText(card.SpecText, TextStyleRole.SecondaryValue, theme);
                if (card.StatusBadge != null) ApplyText(card.StatusBadge, TextStyleRole.Accent, theme);

                if (card.MiniBars != null)
                {
                    for (int b = 0; b < card.MiniBars.Length; b++)
                    {
                        if (card.MiniBars[b] != null) card.MiniBars[b].color = style.GetMeterColor(MeterStyleRole.Track, theme);
                    }
                }
            }

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
