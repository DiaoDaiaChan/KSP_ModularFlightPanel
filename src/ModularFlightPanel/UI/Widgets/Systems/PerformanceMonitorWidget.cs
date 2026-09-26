using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 原生 UGUI 航电性能探针与诊断监控屏 (Avionics Performance Monitor & Diagnostic Detector)
    /// 实时监控 MFP 遥测、外部探针、组件渲染、飞船剪影耗时、物理帧率与宿主内存分配，
    /// 提供一键主干旁路 (Master Bypass) 控制，100% 遵照 BaseFlightWidget 与 TelemetryTokenEngine 规范。
    /// </summary>
    [FlightWidget("performance_monitor", "perf_monitor", "profiler", Category = WidgetCategory.Systems, DisplayName = "SYS PERF 航电性能探针监控屏", Description = "实时监控 MFP 遥测、外部探针、组件渲染耗时与帧率 FPS，支持一键主干旁路。", DefaultWidgetId = "custom.perf_monitor", DefaultX = 440f, DefaultY = -40f, IsSingleton = true, ExactIds = new[] { "core.performance_monitor", "custom.perf_monitor" })]
    public class PerformanceMonitorWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(240f, 195f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // 声明式微控件头部与状态徽标
        public TextWidget Title = TextWidget.Title("SYS PERF MONITOR");
        public TextWidget StatusBadge = TextWidget.Badge("● LIVE");

        private Image _headerLine;
        private Image _midLine;

        // 核心性能数值 (大字 FPS 与整帧耗时)
        private Text _fpsValText;
        private Text _fpsLabel;
        private Text _mfpMsText;
        private Text _budgetPctText;

        // 子系统耗时计量条与读数 (Widgets, Probes, Telemetry, Core/Hooks)
        private Text _widgetsLabel;
        private Image _widgetsTrack;
        private Image _widgetsFill;
        private Text _widgetsValText;

        private Text _probesLabel;
        private Image _probesTrack;
        private Image _probesFill;
        private Text _probesValText;

        private Text _telemLabel;
        private Image _telemTrack;
        private Image _telemFill;
        private Text _telemValText;

        private Text _coreLabel;
        private Image _coreTrack;
        private Image _coreFill;
        private Text _coreValText;

        // 内存与系统稳定性指标
        private Text _memValText;
        private Text _healthValText;

        // 底部旁路交互控制按钮
        private Button _bypassBtn;
        private Image _bypassBtnBg;
        private Outline _bypassBtnOutline;
        private Text _bypassBtnLabel;

        // 通配符通道模板缓存
        private string _titleTemplate = "SYS PERF MONITOR";
        private string _fpsToken = "{PERF:FPS}";
        private string _totalMsToken = "{PERF:MS}";
        private string _budgetToken = "{PERF:BUDGET}";
        private string _memToken = "{PERF:MEM}";
        private string _widgetsToken = "{PERF:WIDGETS}";
        private string _probesToken = "{PERF:PROBES}";
        private string _telemToken = "{PERF:TELEM}";
        private string _coreToken = "{PERF:HOOKS}";

        // 脏检查缓存 (抑制无效重绘)
        private double _lastFps = double.NaN;
        private double _lastTotalMs = double.NaN;
        private double _lastBudget = double.NaN;
        private double _lastMem = double.NaN;
        private double _lastWidgetsMs = double.NaN;
        private double _lastProbesMs = double.NaN;
        private double _lastTelemMs = double.NaN;
        private double _lastCoreMs = double.NaN;
        private bool _lastBypassState = false;
        private string _lastFormattedFps = string.Empty;
        private string _lastFormattedTotalMs = string.Empty;
        private string _lastFormattedBudget = string.Empty;
        private string _lastFormattedMem = string.Empty;
        private string _lastFormattedHealth = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ParseTemplateChannels(config?.CustomTemplate);
            Title.Text = _titleTemplate;

            // 分割横线 1
            GameObject hlObj = UIFactory.CreatePanel(transform, "HeaderLine", new Vector2(224f * s, 1f * s), new Vector2(0f, 66f * s), theme.FrameBorderColor);
            _headerLine = hlObj.GetComponent<Image>();

            // 2. 核心大字指标 (FPS + 整帧耗时)
            _fpsValText = UIFactory.CreateText(transform, "FpsValue", "60", Mathf.RoundToInt(18f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform fpsValRt = _fpsValText.GetComponent<RectTransform>();
            fpsValRt.sizeDelta = new Vector2(52f * s, 26f * s);
            fpsValRt.anchoredPosition = new Vector2(-60f * s, 44f * s);

            _fpsLabel = UIFactory.CreateText(transform, "FpsLabel", "FPS", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform fpsLblRt = _fpsLabel.GetComponent<RectTransform>();
            fpsLblRt.sizeDelta = new Vector2(28f * s, 16f * s);
            fpsLblRt.anchoredPosition = new Vector2(-18f * s, 41f * s);

            _mfpMsText = UIFactory.CreateText(transform, "MfpMs", "0.00 ms", Mathf.RoundToInt(13f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform msRt = _mfpMsText.GetComponent<RectTransform>();
            msRt.sizeDelta = new Vector2(90f * s, 18f * s);
            msRt.anchoredPosition = new Vector2(60f * s, 48f * s);

            _budgetPctText = UIFactory.CreateText(transform, "BudgetPct", "0.0% BUDGET", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform budRt = _budgetPctText.GetComponent<RectTransform>();
            budRt.sizeDelta = new Vector2(90f * s, 14f * s);
            budRt.anchoredPosition = new Vector2(60f * s, 34f * s);

            // 3. 子系统耗时计量条 (Widgets, Probes, Telemetry, Core/Hooks)
            CreateSubsystemRow(transform, "Row_Widgets", "WIDGETS", 14f * s, s, theme,
                out _widgetsLabel, out _widgetsTrack, out _widgetsFill, out _widgetsValText);

            CreateSubsystemRow(transform, "Row_Probes", "PROBES", -1f * s, s, theme,
                out _probesLabel, out _probesTrack, out _probesFill, out _probesValText);

            CreateSubsystemRow(transform, "Row_Telem", "TELEM", -16f * s, s, theme,
                out _telemLabel, out _telemTrack, out _telemFill, out _telemValText);

            CreateSubsystemRow(transform, "Row_Core", "CORE/HOOK", -31f * s, s, theme,
                out _coreLabel, out _coreTrack, out _coreFill, out _coreValText);

            // 分割横线 2
            GameObject mlObj = UIFactory.CreatePanel(transform, "MidLine", new Vector2(224f * s, 1f * s), new Vector2(0f, -43f * s), theme.FrameBorderColor);
            _midLine = mlObj.GetComponent<Image>();

            // 4. 内存分配与稳定性
            _memValText = UIFactory.CreateText(transform, "MemVal", "HEAP: --.- M", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform memRt = _memValText.GetComponent<RectTransform>();
            memRt.sizeDelta = new Vector2(105f * s, 14f * s);
            memRt.anchoredPosition = new Vector2(-52f * s, -54f * s);

            _healthValText = UIFactory.CreateText(transform, "HealthVal", "GC0: 0 · SPIKE: 0", Mathf.RoundToInt(8f * s),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform hltRt = _healthValText.GetComponent<RectTransform>();
            hltRt.sizeDelta = new Vector2(105f * s, 14f * s);
            hltRt.anchoredPosition = new Vector2(52f * s, -54f * s);

            // 5. 底部 Master Bypass 交互按钮
            GameObject btnObj = UIFactory.CreatePanel(transform, "BypassBtn", new Vector2(216f * s, 22f * s),
                new Vector2(0f, -74f * s), style.GetSurfaceColor(SurfaceStyleRole.Control, theme),
                theme.FrameBorderColor, 1f);
            _bypassBtnBg = btnObj.GetComponent<Image>();
            _bypassBtnOutline = btnObj.GetComponent<Outline>();
            _bypassBtn = btnObj.AddComponent<Button>();
            _bypassBtn.onClick.AddListener(OnBypassClicked);

            _bypassBtnLabel = UIFactory.CreateText(btnObj.transform, "BypassLabel", "⏸ BYPASS MFP (ZERO OVERHEAD)",
                Mathf.RoundToInt(8f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform bLblRt = _bypassBtnLabel.GetComponent<RectTransform>();
            bLblRt.anchorMin = Vector2.zero;
            bLblRt.anchorMax = Vector2.one;
            bLblRt.offsetMin = Vector2.zero;
            bLblRt.offsetMax = Vector2.zero;

            // 注册微控件至标准化管理器
            this.Controls.Register(new WidgetReadoutControl(_fpsValText, _fpsLabel, TextStyleRole.Cardinal, "FPS Readout", "帧率主读数"));
            this.Controls.Register(new WidgetReadoutControl(_mfpMsText, _budgetPctText, TextStyleRole.PrimaryValue, "MFP Overhead", "MFP整帧耗时与预算占比"));
            this.Controls.Register(new WidgetLinearBarControl(_widgetsFill, _widgetsTrack, MeterStyleRole.Primary, false, "Widgets Meter", "组件渲染耗时条"));
            this.Controls.Register(new WidgetLinearBarControl(_probesFill, _probesTrack, MeterStyleRole.Primary, false, "Probes Meter", "探针采样耗时条"));
            this.Controls.Register(new WidgetLinearBarControl(_telemFill, _telemTrack, MeterStyleRole.Primary, false, "Telem Meter", "遥测缓存更新耗时条"));
            this.Controls.Register(new WidgetLinearBarControl(_coreFill, _coreTrack, MeterStyleRole.Primary, false, "Core Meter", "核心管线耗时条"));
            this.Controls.Register(new WidgetReadoutControl(_memValText, _healthValText, TextStyleRole.Label, "Memory Stats", "托管堆分配与GC监控"));
            this.Controls.Register(new WidgetActionButtonControl(_bypassBtn, _bypassBtnLabel, null, ButtonVisualRole.Normal, "Bypass Button", "MFP全管线旁路挂起按钮"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);
        }

        private void CreateSubsystemRow(Transform parent, string name, string label, float yPos, float scale,
            ThemeConfig theme, out Text labelText, out Image trackImg, out Image fillImg, out Text valText)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            labelText = UIFactory.CreateText(parent, name + "_Lbl", label, Mathf.RoundToInt(8f * scale),
                TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform lblRt = labelText.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(65f * scale, 14f * scale);
            lblRt.anchoredPosition = new Vector2(-75f * scale, yPos);

            Color trackCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            Color fillCol = style.GetMeterColor(MeterStyleRole.Primary, theme);

            GameObject trackObj = UIFactory.CreatePanel(parent, name + "_Track", new Vector2(88f * scale, 7f * scale),
                new Vector2(6f * scale, yPos), trackCol);
            trackImg = trackObj.GetComponent<Image>();

            GameObject fillObj = UIFactory.CreatePanel(trackObj.transform, name + "_Fill", new Vector2(0f, 7f * scale),
                Vector2.zero, fillCol);
            fillImg = fillObj.GetComponent<Image>();
            RectTransform fillRt = fillImg.rectTransform;
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;

            valText = UIFactory.CreateText(parent, name + "_Val", "0.00ms", Mathf.RoundToInt(8f * scale),
                TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform valRt = valText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(50f * scale, 14f * scale);
            valRt.anchoredPosition = new Vector2(82f * scale, yPos);
        }

        private void ParseTemplateChannels(string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            string[] pairs = template.Split(';');
            foreach (string p in pairs)
            {
                string[] kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "TITLE": _titleTemplate = v; break;
                    case "FPS": _fpsToken = v; break;
                    case "MS":
                    case "TOTAL": _totalMsToken = v; break;
                    case "BUDGET": _budgetToken = v; break;
                    case "MEM": _memToken = v; break;
                    case "WIDGETS": _widgetsToken = v; break;
                    case "PROBES": _probesToken = v; break;
                    case "TELEM": _telemToken = v; break;
                    case "CORE":
                    case "HOOKS": _coreToken = v; break;
                }
            }
        }

        private void OnBypassClicked()
        {
            MFPProfiler.ToggleMasterBypass();
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);

            if (_headerLine != null) _headerLine.color = theme.FrameBorderColor;
            if (_midLine != null) _midLine.color = theme.FrameBorderColor;

            ApplyText(_fpsValText, TextStyleRole.Cardinal, theme);
            ApplyText(_fpsLabel, TextStyleRole.Unit, theme);
            ApplyText(_mfpMsText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_budgetPctText, TextStyleRole.SecondaryValue, theme);

            ApplyMeter(_widgetsTrack, _widgetsFill, null, MeterStyleRole.Primary, theme);
            ApplyText(_widgetsLabel, TextStyleRole.Label, theme);
            ApplyText(_widgetsValText, TextStyleRole.SecondaryValue, theme);

            ApplyMeter(_probesTrack, _probesFill, null, MeterStyleRole.Secondary, theme);
            ApplyText(_probesLabel, TextStyleRole.Label, theme);
            ApplyText(_probesValText, TextStyleRole.SecondaryValue, theme);

            ApplyMeter(_telemTrack, _telemFill, null, MeterStyleRole.Primary, theme);
            ApplyText(_telemLabel, TextStyleRole.Label, theme);
            ApplyText(_telemValText, TextStyleRole.SecondaryValue, theme);

            ApplyMeter(_coreTrack, _coreFill, null, MeterStyleRole.Secondary, theme);
            ApplyText(_coreLabel, TextStyleRole.Label, theme);
            ApplyText(_coreValText, TextStyleRole.SecondaryValue, theme);

            ApplyText(_memValText, TextStyleRole.Label, theme);
            ApplyText(_healthValText, TextStyleRole.SecondaryValue, theme);

            if (_bypassBtn != null)
            {
                ButtonVisualRole btnRole = MFPProfiler.IsMasterBypassed ? ButtonVisualRole.Warning : ButtonVisualRole.Normal;
                ApplyButton(_bypassBtn, _bypassBtnBg, _bypassBtnLabel, btnRole, false, theme);
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            float s = CurrentDpiScale;
            double deltaThreshold = Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.02;

            // 1. 旁路状态变动侦测
            bool bypassed = MFPProfiler.IsMasterBypassed;
            if (bypassed != _lastBypassState)
            {
                _lastBypassState = bypassed;
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
                StatusBadge.Text = bypassed ? "● BYPASS" : "● LIVE";
                StatusBadge.SetRole(bypassed ? TextStyleRole.Warning : TextStyleRole.Accent);
                _bypassBtnLabel.text = bypassed ? "▶ RESUME MFP HUD" : "⏸ BYPASS MFP (ZERO OVERHEAD)";
                ApplyButton(_bypassBtn, _bypassBtnBg, _bypassBtnLabel, bypassed ? ButtonVisualRole.Warning : ButtonVisualRole.Normal, false, theme);
            }

            // 2. FPS 读数与语义告警
            double fps = TelemetryTokenEngine.EvaluateNumeric(_fpsToken, telemetry);
            if (double.IsNaN(fps) || fps <= 0.0) fps = MFPProfiler.CurrentFPS;

            if (double.IsNaN(_lastFps) || Math.Abs(fps - _lastFps) > 0.8)
            {
                _lastFps = fps;
                string fpsStr = Math.Round(fps).ToString();
                if (fpsStr != _lastFormattedFps)
                {
                    _lastFormattedFps = fpsStr;
                    _fpsValText.text = fpsStr;

                    ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
                    if (fps < 25.0) ApplyText(_fpsValText, TextStyleRole.Danger, theme);
                    else if (fps < 45.0) ApplyText(_fpsValText, TextStyleRole.Warning, theme);
                    else ApplyText(_fpsValText, TextStyleRole.Cardinal, theme);
                }
            }

            // 3. MFP 帧耗时与预算占比
            double totalMs = TelemetryTokenEngine.EvaluateNumeric(_totalMsToken, telemetry);
            if (double.IsNaN(totalMs)) totalMs = MFPProfiler.AvgTotalMs;

            if (double.IsNaN(_lastTotalMs) || Math.Abs(totalMs - _lastTotalMs) > deltaThreshold)
            {
                _lastTotalMs = totalMs;
                string msStr = $"{totalMs:F2} ms";
                if (msStr != _lastFormattedTotalMs)
                {
                    _lastFormattedTotalMs = msStr;
                    _mfpMsText.text = msStr;
                }
            }

            double budget = TelemetryTokenEngine.EvaluateNumeric(_budgetToken, telemetry);
            if (double.IsNaN(budget)) budget = MFPProfiler.FrameBudgetPercent;

            if (double.IsNaN(_lastBudget) || Math.Abs(budget - _lastBudget) > 0.1)
            {
                _lastBudget = budget;
                string budStr = $"{budget:F1}% BUDGET";
                if (budStr != _lastFormattedBudget)
                {
                    _lastFormattedBudget = budStr;
                    _budgetPctText.text = budStr;
                }
            }

            // 4. 子系统耗时柱条更新 (基准上限 2.0ms 满幅)
            const double maxSubsystemMs = 2.0;
            float maxBarWidth = 88f * s;

            // Widgets
            double wMs = TelemetryTokenEngine.EvaluateNumeric(_widgetsToken, telemetry);
            if (double.IsNaN(wMs)) wMs = MFPProfiler.AvgWidgetsMs;
            if (double.IsNaN(_lastWidgetsMs) || Math.Abs(wMs - _lastWidgetsMs) > 0.01)
            {
                _lastWidgetsMs = wMs;
                float frac = Mathf.Clamp01((float)(wMs / maxSubsystemMs));
                _widgetsFill.rectTransform.sizeDelta = new Vector2(maxBarWidth * frac, 7f * s);
                _widgetsValText.text = $"{wMs:F2}ms";
            }

            // Probes
            double pMs = TelemetryTokenEngine.EvaluateNumeric(_probesToken, telemetry);
            if (double.IsNaN(pMs)) pMs = MFPProfiler.AvgProbesMs;
            if (double.IsNaN(_lastProbesMs) || Math.Abs(pMs - _lastProbesMs) > 0.01)
            {
                _lastProbesMs = pMs;
                float frac = Mathf.Clamp01((float)(pMs / maxSubsystemMs));
                _probesFill.rectTransform.sizeDelta = new Vector2(maxBarWidth * frac, 7f * s);
                _probesValText.text = $"{pMs:F2}ms";
            }

            // Telem
            double tMs = TelemetryTokenEngine.EvaluateNumeric(_telemToken, telemetry);
            if (double.IsNaN(tMs)) tMs = MFPProfiler.AvgTelemetryMs;
            if (double.IsNaN(_lastTelemMs) || Math.Abs(tMs - _lastTelemMs) > 0.01)
            {
                _lastTelemMs = tMs;
                float frac = Mathf.Clamp01((float)(tMs / maxSubsystemMs));
                _telemFill.rectTransform.sizeDelta = new Vector2(maxBarWidth * frac, 7f * s);
                _telemValText.text = $"{tMs:F2}ms";
            }

            // Core / Hooks
            double cMs = TelemetryTokenEngine.EvaluateNumeric(_coreToken, telemetry);
            if (double.IsNaN(cMs)) cMs = MFPProfiler.AvgHooksMs + MFPProfiler.AvgSilhouetteMs;
            if (double.IsNaN(_lastCoreMs) || Math.Abs(cMs - _lastCoreMs) > 0.01)
            {
                _lastCoreMs = cMs;
                float frac = Mathf.Clamp01((float)(cMs / maxSubsystemMs));
                _coreFill.rectTransform.sizeDelta = new Vector2(maxBarWidth * frac, 7f * s);
                _coreValText.text = $"{cMs:F2}ms";
            }

            // 5. 内存分配与稳定性
            double mem = TelemetryTokenEngine.EvaluateNumeric(_memToken, telemetry);
            if (double.IsNaN(mem)) mem = MFPProfiler.TotalMemoryMB;
            if (double.IsNaN(_lastMem) || Math.Abs(mem - _lastMem) > 0.5)
            {
                _lastMem = mem;
                string memStr = $"HEAP: {mem:F1} M";
                if (memStr != _lastFormattedMem)
                {
                    _lastFormattedMem = memStr;
                    _memValText.text = memStr;
                }
            }

            string healthStr = $"GC0: {MFPProfiler.Gc0Collections} · SPIKE: {MFPProfiler.SpikeCount}";
            if (healthStr != _lastFormattedHealth)
            {
                _lastFormattedHealth = healthStr;
                _healthValText.text = healthStr;
            }
        }

        protected override void OnDestroy()
        {
            if (_bypassBtn != null)
            {
                _bypassBtn.onClick.RemoveListener(OnBypassClicked);
            }
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
