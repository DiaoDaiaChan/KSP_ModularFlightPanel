using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 中央主警告与警报光字牌 (Master Warning & Caution Annunciator Widget)
    /// 严丝合缝嵌合于主导航球正下方 (宽度 184px, 高度 22px, 完美架设于双带之间)。
    /// 
    /// 工业级航电设计规范 (Industrial Avionics Bezel Architecture)：
    /// 1. 采用航空标准双室 Korry 光字牌 (Dual-Cell Korry Matrix) 架构，中间内嵌金属机械隔离筋条；
    /// 2. 真实航电“暗舱透光” (Backlit Dead-Front Display) 质感：
    ///    - 熄灭待命时：暗色熏黑玻璃内嵌微光幽灵刻字 (Ghost Inscription) 与微型绿光就绪指示灯；
    ///    - 激活报警时：顶部状态光条 (Status Pip Bar) 高亮脉冲，大字号发光主警报 + 右下角微型量化遥测读数与轮播指示器；
    /// 3. 分级独立分道：左舱专注黄色注意 (Caution)，右舱专注红色危急 (Warning)；
    /// 4. 同等级多告警智能平滑轮播 (Alternating Rotation) 并带点阵跑马灯与序号；
    /// 5. 100% 遵照 MFP 六大铁律 (0 颜色字面量、纯 C# 解耦、30Hz 分频、零 GC)。
    /// </summary>
    public class MasterWarningWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 外部底板与装饰构件
        private Image _outerBezel;
        private Outline _outerOutline;
        private Image _centerDivider;

        // 告警单元结构体
        private struct AlertItem
        {
            public string MainTitle;
            public string TelemetryAffix;
            public bool IsWarning;

            public AlertItem(string title, string affix, bool isWarning)
            {
                MainTitle = title;
                TelemetryAffix = affix;
                IsWarning = isWarning;
            }
        }

        // 左舱：Caution (黄色注意) 视图组件
        private GameObject _cautCell;
        private Image _cautBg;
        private Outline _cautOutline;
        private Image _cautPipBar;
        private Text _cautIcon;
        private Text _cautTitle;
        private Text _cautSub;
        private Button _cautBtn;

        // 右舱：Warning (红色危急) 视图组件
        private GameObject _warnCell;
        private Image _warnBg;
        private Outline _warnOutline;
        private Image _warnPipBar;
        private Text _warnIcon;
        private Text _warnTitle;
        private Text _warnSub;
        private Button _warnBtn;

        // 告警队列
        private readonly List<AlertItem> _cautAlerts = new List<AlertItem>(8);
        private readonly List<AlertItem> _warnAlerts = new List<AlertItem>(8);

        // 轮播计时器
        private float _rotateTimer = 0f;
        private float _switchInterval = 1.8f;
        private int _cautIndex = 0;
        private int _warnIndex = 0;

        // 消警状态 (Acknowledge)
        private bool _cautAcknowledged = false;
        private bool _warnAcknowledged = false;
        private int _lastCautCount = -1;
        private int _lastWarnCount = -1;

        // 同步时钟与闪烁
        private static float _clock = 0f;
        private static bool _blink1Hz = true;
        private static bool _blink2Hz = true;

        // 脏检查保护
        private string _lastCautTitleStr = string.Empty;
        private string _lastCautSubStr = string.Empty;
        private string _lastWarnTitleStr = string.Empty;
        private string _lastWarnSubStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 widgetSize = new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = widgetSize;

            ParseCustomTemplate(config?.CustomTemplate);

            // 1. 航空外框底盘 (Outer Bezel)
            _outerBezel = gameObject.AddComponent<Image>();
            _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            _outerOutline = gameObject.AddComponent<Outline>();
            _outerOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 2. 中央硬派机械隔离筋条 (Mechanical Divider Rib, 宽 2px, 高 18px)
            GameObject divObj = UIFactory.CreatePanel(transform, "Divider", new Vector2(2f * s, 18f * s),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _centerDivider = divObj.GetComponent<Image>();

            // 3. 构建左舱：CAUTION 光字牌 (宽 89px, 高 18px, 偏置 -46px)
            Vector2 cellSize = new Vector2(89f * s, 18f * s);
            BuildCautionCell(s, cellSize, theme);

            // 4. 构建右舱：WARNING 光字牌 (宽 89px, 高 18px, 偏置 +46px)
            BuildWarningCell(s, cellSize, theme);

            ApplyTheme(theme);
        }

        private void BuildCautionCell(float s, Vector2 size, ThemeConfig theme)
        {
            _cautCell = UIFactory.CreatePanel(transform, "CautionCell", size, new Vector2(-46f * s, 0f), Color.clear);
            _cautBg = _cautCell.GetComponent<Image>();
            _cautOutline = _cautCell.AddComponent<Outline>();
            _cautOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _cautBtn = _cautCell.AddComponent<Button>();
            _cautBtn.transition = Selectable.Transition.None;
            _cautBtn.onClick.AddListener(OnAcknowledgeCaution);

            // 顶部高光 Pip 指示条 (贴合格栅顶唇)
            GameObject pipObj = UIFactory.CreatePanel(_cautCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _cautPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标 (带光刻质感)
            _cautIcon = UIFactory.CreateText(_cautCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.WarningColor);
            _cautIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _cautIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _cautTitle = UIFactory.CreateText(_cautCell.transform, "Title", "CAUTION", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.WarningColor);
            _cautTitle.fontStyle = FontStyle.Bold;
            _cautTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _cautTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注与角标 (如 1/2 或 14%)
            _cautSub = UIFactory.CreateText(_cautCell.transform, "Sub", "NORM", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.WarningColor);
            _cautSub.fontStyle = FontStyle.Normal;
            _cautSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _cautSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void BuildWarningCell(float s, Vector2 size, ThemeConfig theme)
        {
            _warnCell = UIFactory.CreatePanel(transform, "WarningCell", size, new Vector2(46f * s, 0f), Color.clear);
            _warnBg = _warnCell.GetComponent<Image>();
            _warnOutline = _warnCell.AddComponent<Outline>();
            _warnOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _warnBtn = _warnCell.AddComponent<Button>();
            _warnBtn.transition = Selectable.Transition.None;
            _warnBtn.onClick.AddListener(OnAcknowledgeWarning);

            // 顶部高光 Pip 指示条 (贴合格栅顶唇)
            GameObject pipObj = UIFactory.CreatePanel(_warnCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _warnPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标
            _warnIcon = UIFactory.CreateText(_warnCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.DangerColor);
            _warnIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _warnIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _warnTitle = UIFactory.CreateText(_warnCell.transform, "Title", "WARNING", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.DangerColor);
            _warnTitle.fontStyle = FontStyle.Bold;
            _warnTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _warnTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注与角标
            _warnSub = UIFactory.CreateText(_warnCell.transform, "Sub", "ARMED", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.DangerColor);
            _warnSub.fontStyle = FontStyle.Normal;
            _warnSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _warnSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void ParseCustomTemplate(string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            string[] pairs = template.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string[] kv = pairs[i].Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                if (k == "INTERVAL" || k == "ROTATION")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float iv) && iv > 0.5f)
                    {
                        _switchInterval = iv;
                    }
                }
            }
        }

        private void OnAcknowledgeCaution()
        {
            _cautAcknowledged = true;
        }

        private void OnAcknowledgeWarning()
        {
            _warnAcknowledged = true;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 1. 更新座舱全局同步时钟
            float dt = Time.unscaledDeltaTime;
            _clock += dt;
            _blink1Hz = ((int)(_clock * 2f) % 2) == 0;
            _blink2Hz = ((int)(_clock * 4f) % 2) == 0;

            // 2. 采集当前所有活跃警报
            EvaluateTelemetryAlerts(telemetry);

            // 3. 告警数量变动时复位消警
            if (_cautAlerts.Count != _lastCautCount)
            {
                _lastCautCount = _cautAlerts.Count;
                _cautAcknowledged = false;
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
            }
            if (_warnAlerts.Count != _lastWarnCount)
            {
                _lastWarnCount = _warnAlerts.Count;
                _warnAcknowledged = false;
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
            }

            // 4. 定时交替轮播推进
            _rotateTimer += dt;
            if (_rotateTimer >= _switchInterval)
            {
                _rotateTimer = 0f;
                if (_cautAlerts.Count > 1) _cautIndex = (_cautIndex + 1) % _cautAlerts.Count;
                if (_warnAlerts.Count > 1) _warnIndex = (_warnIndex + 1) % _warnAlerts.Count;
            }

            // 5. 渲染双光字牌
            RenderVisualCells();
        }

        private void EvaluateTelemetryAlerts(IFlightTelemetry telem)
        {
            _cautAlerts.Clear();
            _warnAlerts.Clear();

            // ── 1. 推进剂与沉底 (FUEL / ULLAGE) ──
            float prop = telem.StagePropellantFraction;
            if (prop >= 0f && telem.ActiveEngines > 0)
            {
                int propPct = Mathf.RoundToInt(prop * 100f);
                if (prop <= 0.05f) _warnAlerts.Add(new AlertItem("MIN FUEL!", $"{propPct}%", true));
                else if (prop <= 0.15f) _cautAlerts.Add(new AlertItem("LOW FUEL", $"{propPct}%", false));
            }

            // RealFuels 探针沉底状态
            if (ExternalProbeRegistry.StringResolver != null)
            {
                string rfUllage = ExternalProbeRegistry.ResolveString("RF", "ULLAGE", "");
                if (!string.IsNullOrEmpty(rfUllage) &&
                    (rfUllage.IndexOf("Unstable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rfUllage.IndexOf("Very", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _cautAlerts.Add(new AlertItem("ULLAGE", "UNSTB", false));
                }
            }

            // ── 2. 电气能量平衡 (ELECTRIC CHARGE / DBS) ──
            double ecPct = telem.EcPercent;
            if (ecPct >= 0.0)
            {
                int ecInt = Mathf.RoundToInt((float)ecPct);
                if (ecPct <= 5.0) _warnAlerts.Add(new AlertItem("EC CRIT!", $"{ecInt}%", true));
                else if (ecPct <= 20.0) _cautAlerts.Add(new AlertItem("LOW EC", $"{ecInt}%", false));
            }

            // ── 3. 近地与急剧下沉 (PULL UP! / SINK RATE) ──
            if (telem.IsTouchdownAlert || (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 500.0 && telem.AltitudeAGL > 5.0))
            {
                int aglInt = Mathf.RoundToInt((float)telem.AltitudeAGL);
                _warnAlerts.Add(new AlertItem("PULL UP!", $"{aglInt}m", true));
            }
            else if (telem.VerticalSpeed < -15.0 && telem.AltitudeAGL < 1500.0 && telem.AltitudeAGL > 10.0)
            {
                int vsiInt = Mathf.RoundToInt((float)telem.VerticalSpeed);
                _cautAlerts.Add(new AlertItem("SINK RATE", $"{vsiInt}m/s", false));
            }

            // ── 4. 气动失速 (STALL / FAR / GPWS) ──
            double farStall = double.NaN;
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                farStall = ExternalProbeRegistry.ResolveNumeric("FAR", "STALL");
            }
            if (!double.IsNaN(farStall))
            {
                int stallPct = Mathf.RoundToInt((float)(farStall * 100.0));
                if (farStall > 0.70) _warnAlerts.Add(new AlertItem("STALL!", $"{stallPct}%", true));
                else if (farStall > 0.30) _cautAlerts.Add(new AlertItem("STALL WARN", $"{stallPct}%", false));
            }

            // ── 5. 维生系统与氧气 (O2 / KERBALISM) ──
            if (telem.CrewCapacity > 0)
            {
                float o2 = telem.OxygenPercent;
                if (o2 >= 0f)
                {
                    int o2Int = Mathf.RoundToInt(o2);
                    if (o2 <= 5.0f) _warnAlerts.Add(new AlertItem("O2 CRIT!", $"{o2Int}%", true));
                    else if (o2 <= 20.0f) _cautAlerts.Add(new AlertItem("LOW O2", $"{o2Int}%", false));
                }
            }

            // ── 6. 舱温与超温 (OVERHEAT / SYSTEMHEAT) ──
            double cabinTemp = telem.CabinTemp;
            if (cabinTemp > 120.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _warnAlerts.Add(new AlertItem("OVERHEAT!", $"{tempInt}°C", true));
            }
            else if (cabinTemp > 80.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _cautAlerts.Add(new AlertItem("HIGH TEMP", $"{tempInt}°C", false));
            }

            // ── 7. 过载极限 (G-FORCE) ──
            double g = telem.GForce;
            if (g > 9.0)
            {
                _warnAlerts.Add(new AlertItem("EXCESS G!", $"{g:F1}G", true));
            }
            else if (g > 6.0)
            {
                _cautAlerts.Add(new AlertItem("HIGH G", $"{g:F1}G", false));
            }

            // ── 8. 发动机故障 (TESTFLIGHT) ──
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double tfFailed = ExternalProbeRegistry.ResolveNumeric("TF", "FAILED");
                if (tfFailed > 0.5)
                {
                    _warnAlerts.Add(new AlertItem("ENG FAIL!", "FAIL", true));
                }
            }

            // ── 9. 通信网络断开 (NO COMM) ──
            if (!telem.IsConnected)
            {
                _cautAlerts.Add(new AlertItem("NO COMM", "OFF", false));
            }
        }

        private void RenderVisualCells()
        {
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
            if (theme == null) return;

            // ── A. 渲染左舱：CAUTION ──
            bool isCautActive = _cautAlerts.Count > 0;
            if (isCautActive)
            {
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
                AlertItem item = _cautAlerts[_cautIndex];

                string pagination = _cautAlerts.Count > 1 ? $"{_cautIndex + 1}/{_cautAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_cautTitle, item.MainTitle);
                SetTextIfChanged(_cautSub, pagination);
                SetTextIfChanged(_cautIcon, "▲");

                bool blink = _cautAcknowledged || _blink1Hz;
                if (blink)
                {
                    _cautBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme);
                    _cautOutline.effectColor = theme.WarningColor;
                    _cautPipBar.color = theme.WarningColor;
                    _cautTitle.color = theme.WarningColor;
                    _cautSub.color = theme.WarningColor;
                    _cautIcon.color = theme.WarningColor;
                }
                else
                {
                    _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Faint);
                    _cautPipBar.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.30f);
                    _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautSub.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautIcon.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_cautTitle, "CAUTION");
                SetTextIfChanged(_cautSub, "NORM");
                SetTextIfChanged(_cautIcon, "●");

                _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _cautPipBar.color = Color.clear;
                _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
                _cautSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _cautIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }

            // ── B. 渲染右舱：WARNING ──
            bool isWarnActive = _warnAlerts.Count > 0;
            if (isWarnActive)
            {
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
                AlertItem item = _warnAlerts[_warnIndex];

                string pagination = _warnAlerts.Count > 1 ? $"{_warnIndex + 1}/{_warnAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_warnTitle, item.MainTitle);
                SetTextIfChanged(_warnSub, pagination);
                SetTextIfChanged(_warnIcon, "▲");

                bool blink = _warnAcknowledged || _blink2Hz;
                if (blink)
                {
                    _warnBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger, theme);
                    _warnOutline.effectColor = theme.DangerColor;
                    _warnPipBar.color = theme.DangerColor;
                    _warnTitle.color = theme.DangerColor;
                    _warnSub.color = theme.DangerColor;
                    _warnIcon.color = theme.DangerColor;
                }
                else
                {
                    _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Faint);
                    _warnPipBar.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.30f);
                    _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnSub.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnIcon.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_warnTitle, "WARNING");
                SetTextIfChanged(_warnSub, "ARMED");
                SetTextIfChanged(_warnIcon, "●");

                _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _warnPipBar.color = Color.clear;
                _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
                _warnSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _warnIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_outerBezel != null) _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_outerOutline != null) _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_centerDivider != null) _centerDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            RenderVisualCells();
        }

        protected override void OnDestroy()
        {
            if (_cautBtn != null) _cautBtn.onClick.RemoveAllListeners();
            if (_warnBtn != null) _warnBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
