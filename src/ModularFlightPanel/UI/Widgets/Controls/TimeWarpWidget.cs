using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化航电时间加速与任务时钟组件 (Avionics Time Warp & Mission Clock Suite)
    /// 
    /// 完美替代 KSP 原版左上角粗糙方框：
    /// 1. 高对比度数字任务时钟 (MET 任务已过时间 / UT 宇宙世界标准时间双模式一键切换)
    /// 2. 多级交互式光带箭头脉冲推进器 (8 级轨道加速 / 4 级物理加速自适应，支持直按任意层级)
    /// 3. 单步快进/步退微调键 (◀ / ▶) 与紧急一键瞬时归一键 (1X Kill-Warp)
    /// 4. 游戏暂停与物理加速 (PHYSICS) 琥珀色警示状态机
    /// 5. 原版顶部时间栏非破坏性安全隐显切换 (STOCK TOGGLE)
    /// </summary>
    public class TimeWarpWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        private Image _panelBg;
        private Outline _panelOutline;

        // 顶部时钟与状态行
        private Button _modeBtn;
        private Text _modeBtnText;
        private Text _clockText;
        private Button _pauseBtn;
        private Text _pauseBtnText;
        private Button _stockBtn;
        private Text _stockBtnText;

        // 底部加速与操纵行
        private Text _warpModeText;
        private Text _warpRateText;
        private Button _downBtn;
        private Text _downBtnText;
        private Button _upBtn;
        private Text _upBtnText;
        private Button _cancelBtn;
        private Text _cancelBtnText;

        // 交互式 8 级加速光段 (Chevrons)
        private const int MaxChevronCount = 8;
        private readonly Image[] _chevronImgs = new Image[MaxChevronCount];
        private readonly Button[] _chevronBtns = new Button[MaxChevronCount];

        private bool _showUniversalTime = false;
        private bool _stockHidden = true;
        private ThemeConfig _currentTheme;
        private IFlightTelemetry _lastTelemetry;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(236f * s, 42f * s);
            RectTransform.sizeDelta = panelSize;

            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            // 1. 主背板与淡轮廓 (全息极简 HUD 风格)
            GameObject panel = UIFactory.CreatePanel(transform, "TimeWarpPanel", panelSize, Vector2.zero, Color.clear,
                WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Ghost), 1f * s);
            _panelBg = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();
            _panelOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // ==========================================
            // 2. 顶部时钟与状态行 (Y ≈ +10f)
            // ==========================================
            float topY = 10f * s;

            // 时钟模式切换键 (MET / UT) - 极简药丸
            Vector2 modeBtnSize = new Vector2(32f * s, 15f * s);
            _modeBtn = UIFactory.CreateButton(panel.transform, "Btn_Mode", modeBtnSize,
                new Vector2(-panelSize.x * 0.5f + 20f * s, topY), OnToggleMode);
            _modeBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeBtnText = UIFactory.CreateText(_modeBtn.transform, "Text", "MET",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _modeBtnText.fontStyle = FontStyle.Bold;
            _modeBtnText.GetComponent<RectTransform>().sizeDelta = modeBtnSize;

            // 高对比度数字时钟读数 (T+ 0y, 0d, 02:44:16)
            _clockText = UIFactory.CreateText(panel.transform, "ClockText", "T+ 0y, 0d, 00:00:00",
                Mathf.Max(9, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, textPrimary);
            _clockText.fontStyle = FontStyle.Bold;
            RectTransform clockRt = _clockText.GetComponent<RectTransform>();
            clockRt.sizeDelta = new Vector2(116f * s, 18f * s);
            clockRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 98f * s, topY);

            // 暂停/继续控制键 (PAUSE / RUN)
            Vector2 pauseBtnSize = new Vector2(38f * s, 15f * s);
            _pauseBtn = UIFactory.CreateButton(panel.transform, "Btn_Pause", pauseBtnSize,
                new Vector2(panelSize.x * 0.5f - 42f * s, topY), OnTogglePause);
            _pauseBtn.GetComponent<Image>().color = Color.clear;
            _pauseBtnText = UIFactory.CreateText(_pauseBtn.transform, "Text", "PAUSE",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _pauseBtnText.GetComponent<RectTransform>().sizeDelta = pauseBtnSize;

            // 原版时间栏显隐开关 (STOCK)
            Vector2 stockBtnSize = new Vector2(22f * s, 15f * s);
            _stockBtn = UIFactory.CreateButton(panel.transform, "Btn_Stock", stockBtnSize,
                new Vector2(panelSize.x * 0.5f - 11f * s, topY), OnToggleStock);
            _stockBtn.GetComponent<Image>().color = Color.clear;
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stockBtnText.GetComponent<RectTransform>().sizeDelta = stockBtnSize;

            // 细分割线
            UIFactory.CreatePanel(panel.transform, "DivLine", new Vector2(panelSize.x - 16f * s, 1f * s),
                Vector2.zero, WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Ghost));

            // ==========================================
            // 3. 底部加速控制行 (Y ≈ -10f)
            // ==========================================
            float botY = -10f * s;

            // 加速模式标志 (WARP / PHYS)
            _warpModeText = UIFactory.CreateText(panel.transform, "WarpMode", "WARP",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform wmRt = _warpModeText.GetComponent<RectTransform>();
            wmRt.sizeDelta = new Vector2(28f * s, 16f * s);
            wmRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 18f * s, botY);

            // 当前速率读数 (1x / 10,000x)
            _warpRateText = UIFactory.CreateText(panel.transform, "WarpRate", "1x",
                Mathf.Max(7, Mathf.RoundToInt(9f * s)), TextAnchor.MiddleLeft, primaryAccent);
            _warpRateText.fontStyle = FontStyle.Bold;
            RectTransform wrRt = _warpRateText.GetComponent<RectTransform>();
            wrRt.sizeDelta = new Vector2(36f * s, 16f * s);
            wrRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 50f * s, botY);

            // 步退减速键 (◀)
            Vector2 stepBtnSize = new Vector2(14f * s, 14f * s);
            _downBtn = UIFactory.CreateButton(panel.transform, "Btn_Down", stepBtnSize,
                new Vector2(-panelSize.x * 0.5f + 76f * s, botY), OnStepWarpDown);
            _downBtn.GetComponent<Image>().color = Color.clear;
            _downBtnText = UIFactory.CreateText(_downBtn.transform, "Text", "◀",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _downBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 8 个多级加速微型指示光段 (Micro-Pips)
            float chevronStartX = -panelSize.x * 0.5f + 92f * s;
            float chevronW = 8.5f * s;
            float chevronH = 6f * s;
            float chevronGap = 2.5f * s;

            for (int i = 0; i < MaxChevronCount; i++)
            {
                int index = i;
                float cx = chevronStartX + i * (chevronW + chevronGap);
                GameObject chGo = UIFactory.CreatePanel(panel.transform, $"Chevron_{i}",
                    new Vector2(chevronW, chevronH), new Vector2(cx, botY),
                    WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Faint));

                Button btn = chGo.AddComponent<Button>();
                btn.onClick.AddListener(() => OnSetWarpIndex(index));

                _chevronImgs[i] = chGo.GetComponent<Image>();
                _chevronBtns[i] = btn;
            }

            // 步进加速键 (▶)
            float upX = chevronStartX + MaxChevronCount * (chevronW + chevronGap) + 3f * s;
            _upBtn = UIFactory.CreateButton(panel.transform, "Btn_Up", stepBtnSize,
                new Vector2(upX, botY), OnStepWarpUp);
            _upBtn.GetComponent<Image>().color = Color.clear;
            _upBtnText = UIFactory.CreateText(_upBtn.transform, "Text", "▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _upBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 瞬时归一键 (1X Kill-Warp)
            Vector2 cancelBtnSize = new Vector2(22f * s, 14f * s);
            _cancelBtn = UIFactory.CreateButton(panel.transform, "Btn_Cancel", cancelBtnSize,
                new Vector2(panelSize.x * 0.5f - 14f * s, botY), OnCancelWarp);
            _cancelBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _cancelBtnText = UIFactory.CreateText(_cancelBtn.transform, "Text", "1X",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _cancelBtnText.fontStyle = FontStyle.Bold;
            _cancelBtnText.GetComponent<RectTransform>().sizeDelta = cancelBtnSize;

            // 初始化根据主题配置执行原版时间栏静默隐藏
            _stockHidden = ThemeManager.IsStockTimeWarpHidden;
            NavBallHookService.HideStockTimeWarpAction?.Invoke(_stockHidden);

            ApplyTheme(theme);
        }


        private string _lastClockStr;
        private string _lastRateStr;
        private bool _lastPausedState = false;
        private bool _lastPhysState = false;
        private int _lastActiveIndex = -1;
        private int _lastMaxIndex = -1;
        private bool _hasInitState = false;

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;
            _lastTelemetry = telemetry;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(_currentTheme);

            // 1. 更新时钟读数 (CustomTemplate & Dirty Checking)
            if (_clockText != null)
            {
                string utTpl = GetTemplateChannel("UT_FORMAT", "{UT}");
                string metTpl = GetTemplateChannel("MET_FORMAT", "{MET}");
                string clockStr = _showUniversalTime
                    ? TelemetryTokenEngine.Evaluate(utTpl, telemetry)
                    : TelemetryTokenEngine.Evaluate(metTpl, telemetry);

                if (clockStr != _lastClockStr)
                {
                    _lastClockStr = clockStr;
                    _clockText.text = clockStr;
                }
            }

            // 2. 暂停状态提示
            bool isPaused = telemetry.IsGamePaused;
            if (!_hasInitState || isPaused != _lastPausedState)
            {
                _lastPausedState = isPaused;
                if (_pauseBtnText != null && _pauseBtn != null)
                {
                    _pauseBtnText.text = isPaused ? "PAUSED" : "PAUSE";
                    _pauseBtn.GetComponent<Image>().color = isPaused 
                        ? WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger) 
                        : Color.clear;
                    _pauseBtnText.color = isPaused 
                        ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Danger, theme) 
                        : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    _pauseBtnText.fontStyle = isPaused ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            // 3. 加速模式与倍率
            bool isPhys = telemetry.IsPhysicsWarp;
            if (!_hasInitState || isPhys != _lastPhysState)
            {
                _lastPhysState = isPhys;
                if (_warpModeText != null)
                {
                    string physLabel = GetTemplateChannel("PHYS_LABEL", "PHYS");
                    string warpLabel = GetTemplateChannel("WARP_LABEL", "WARP");
                    _warpModeText.text = isPhys ? physLabel : warpLabel;
                    ApplyText(_warpModeText, isPhys ? TextStyleRole.Warning : TextStyleRole.Label, theme);
                }
            }

            if (_warpRateText != null)
            {
                double rate = telemetry.TimeWarpRate;
                string rStr = (rate >= 1000.0) ? $"{rate:N0}x" : ((rate > 1.0) ? $"{rate:0.#}x" : "1x");
                if (rStr != _lastRateStr)
                {
                    _lastRateStr = rStr;
                    _warpRateText.text = rStr;
                    ApplyText(_warpRateText, (rate > 1.0) ? (isPhys ? TextStyleRole.Warning : TextStyleRole.Accent) : TextStyleRole.PrimaryValue, theme);
                }
            }

            // 4. 加速光段状态机
            int activeIndex = telemetry.TimeWarpRateIndex;
            int maxIndex = Mathf.Clamp(telemetry.MaxTimeWarpRateIndex, 1, MaxChevronCount - 1);

            if (!_hasInitState || activeIndex != _lastActiveIndex || maxIndex != _lastMaxIndex)
            {
                _lastActiveIndex = activeIndex;
                _lastMaxIndex = maxIndex;

                MeterStyleRole fillRole = isPhys ? MeterStyleRole.Warning : MeterStyleRole.Primary;
                Color litColor = WidgetStyleManager.Meter(fillRole, theme);
                Color dimColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint);

                for (int i = 0; i < MaxChevronCount; i++)
                {
                    if (_chevronImgs[i] == null) continue;

                    if (i > maxIndex)
                    {
                        _chevronImgs[i].gameObject.SetActive(false);
                    }
                    else
                    {
                        _chevronImgs[i].gameObject.SetActive(true);
                        _chevronImgs[i].color = (i <= activeIndex) ? litColor : dimColor;
                    }
                }

                // 5. 瞬时归一按钮高亮状态
                if (_cancelBtnText != null && _cancelBtn != null)
                {
                    bool canCancel = activeIndex > 0;
                    _cancelBtn.GetComponent<Image>().color = canCancel 
                        ? WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution) 
                        : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    _cancelBtnText.color = canCancel 
                        ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, theme) 
                        : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                }
            }

            // 6. 原版时间栏显隐按键文本与颜色
            if (_stockBtnText != null)
            {
                _stockBtnText.text = _stockHidden ? "KSP" : "MFP";
                ApplyText(_stockBtnText, _stockHidden ? TextStyleRole.SecondaryValue : TextStyleRole.Accent, theme);
            }

            _hasInitState = true;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            _currentTheme = theme;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_panelBg != null) _panelBg.color = Color.clear;
            if (_panelOutline != null)
            {
                _panelOutline.enabled = true;
                _panelOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            ApplyText(_clockText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_warpRateText, TextStyleRole.Accent, theme);
            ApplyText(_warpModeText, TextStyleRole.Label, theme);

            ApplyText(_modeBtnText, TextStyleRole.Label, theme);
            ApplyText(_downBtnText, TextStyleRole.Label, theme);
            ApplyText(_upBtnText, TextStyleRole.Label, theme);
            ApplyText(_cancelBtnText, TextStyleRole.Label, theme);
            ApplyText(_stockBtnText, TextStyleRole.Label, theme);

            Material btnMat = WidgetStyleManager.Instance.GetUiMaterial(isText: false);
            if (_modeBtn != null) _modeBtn.GetComponent<Image>().material = btnMat;
            if (_downBtn != null) _downBtn.GetComponent<Image>().material = btnMat;
            if (_upBtn != null) _upBtn.GetComponent<Image>().material = btnMat;
            if (_cancelBtn != null) _cancelBtn.GetComponent<Image>().material = btnMat;
            if (_stockBtn != null) _stockBtn.GetComponent<Image>().material = btnMat;
            if (_pauseBtn != null) _pauseBtn.GetComponent<Image>().material = btnMat;
        }

        private void OnToggleMode()
        {
            _showUniversalTime = !_showUniversalTime;
            if (_modeBtnText != null)
            {
                _modeBtnText.text = _showUniversalTime ? "UT" : "MET";
            }
        }

        private void OnTogglePause()
        {
            _lastTelemetry?.TogglePause();
        }

        private void OnToggleStock()
        {
            _stockHidden = !_stockHidden;
            NavBallHookService.HideStockTimeWarpAction?.Invoke(_stockHidden);
            ThemeManager.IsStockTimeWarpHidden = _stockHidden;
            ThemeManager.Instance.SaveSettings();
        }

        private void OnStepWarpDown()
        {
            _lastTelemetry?.DecreaseTimeWarp();
        }

        private void OnStepWarpUp()
        {
            _lastTelemetry?.IncreaseTimeWarp();
        }

        private void OnSetWarpIndex(int idx)
        {
            _lastTelemetry?.SetTimeWarpRateIndex(idx);
        }

        private void OnCancelWarp()
        {
            _lastTelemetry?.CancelTimeWarp();
        }

        protected override void OnDestroy()
        {
            if (_modeBtn != null) _modeBtn.onClick.RemoveAllListeners();
            if (_pauseBtn != null) _pauseBtn.onClick.RemoveAllListeners();
            if (_stockBtn != null) _stockBtn.onClick.RemoveAllListeners();
            if (_downBtn != null) _downBtn.onClick.RemoveAllListeners();
            if (_upBtn != null) _upBtn.onClick.RemoveAllListeners();
            if (_cancelBtn != null) _cancelBtn.onClick.RemoveAllListeners();
            for (int i = 0; i < MaxChevronCount; i++)
            {
                if (_chevronBtns[i] != null) _chevronBtns[i].onClick.RemoveAllListeners();
            }
            base.OnDestroy();
        }
    }
}
