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
        private Image _topStripe;

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
            Vector2 panelSize = new Vector2(236f * s, 46f * s);
            RectTransform.sizeDelta = panelSize;

            Color bgCol = theme.FrameBgColor;
            Color borderCol = theme.FrameBorderColor;
            Color primaryAccent = theme.AccentPrimary;
            Color secondaryAccent = theme.AccentSecondary;
            Color textPrimary = theme.TextPrimaryColor;

            // 1. 主背板与微光轮廓
            GameObject panel = UIFactory.CreatePanel(transform, "TimeWarpPanel", panelSize, Vector2.zero, bgCol, borderCol, 1.2f * s);
            _panelBg = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();

            // 2. 顶部微光装饰线条 (Top Accent Stripe)
            _topStripe = UIFactory.CreatePanel(panel.transform, "TopStripe", new Vector2(panelSize.x, 2f * s),
                new Vector2(0f, panelSize.y * 0.5f - 1f * s), primaryAccent).GetComponent<Image>();

            // ==========================================
            // 3. 顶部时钟与状态行 (Y ≈ +10f)
            // ==========================================
            float topY = 10f * s;

            // 时钟模式切换键 (MET / UT)
            Vector2 modeBtnSize = new Vector2(34f * s, 16f * s);
            _modeBtn = UIFactory.CreateButton(panel.transform, "Btn_Mode", modeBtnSize,
                new Vector2(-panelSize.x * 0.5f + 23f * s, topY), OnToggleMode);
            _modeBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);
            var modeOut = _modeBtn.gameObject.AddComponent<Outline>();
            modeOut.effectColor = WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Strong);
            modeOut.effectDistance = new Vector2(1f * s, 1f * s);
            _modeBtnText = UIFactory.CreateText(_modeBtn.transform, "Text", "MET",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _modeBtnText.GetComponent<RectTransform>().sizeDelta = modeBtnSize;

            // 高对比度数字时钟读数 (T+ 0y, 0d, 02:44:16)
            _clockText = UIFactory.CreateText(panel.transform, "ClockText", "T+ 0y, 0d, 00:00:00",
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleLeft, textPrimary);
            RectTransform clockRt = _clockText.GetComponent<RectTransform>();
            clockRt.sizeDelta = new Vector2(116f * s, 18f * s);
            clockRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 102f * s, topY);

            // 暂停/继续控制键 (PAUSE / RUN)
            Vector2 pauseBtnSize = new Vector2(36f * s, 16f * s);
            _pauseBtn = UIFactory.CreateButton(panel.transform, "Btn_Pause", pauseBtnSize,
                new Vector2(panelSize.x * 0.5f - 49f * s, topY), OnTogglePause);
            _pauseBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);
            var pauseOut = _pauseBtn.gameObject.AddComponent<Outline>();
            pauseOut.effectColor = WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Strong);
            pauseOut.effectDistance = new Vector2(1f * s, 1f * s);
            _pauseBtnText = UIFactory.CreateText(_pauseBtn.transform, "Text", "PAUSE",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _pauseBtnText.GetComponent<RectTransform>().sizeDelta = pauseBtnSize;

            // 原版时间栏显隐开关 (STOCK)
            Vector2 stockBtnSize = new Vector2(26f * s, 16f * s);
            _stockBtn = UIFactory.CreateButton(panel.transform, "Btn_Stock", stockBtnSize,
                new Vector2(panelSize.x * 0.5f - 16f * s, topY), OnToggleStock);
            _stockBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);
            var stockOut = _stockBtn.gameObject.AddComponent<Outline>();
            stockOut.effectColor = WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Strong);
            stockOut.effectDistance = new Vector2(1f * s, 1f * s);
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.SecondaryValue));
            _stockBtnText.GetComponent<RectTransform>().sizeDelta = stockBtnSize;

            // ==========================================
            // 4. 底部加速控制行 (Y ≈ -11f)
            // ==========================================
            float botY = -11f * s;

            // 加速模式标志 (WARP / PHYS)
            _warpModeText = UIFactory.CreateText(panel.transform, "WarpMode", "WARP",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft, secondaryAccent);
            RectTransform wmRt = _warpModeText.GetComponent<RectTransform>();
            wmRt.sizeDelta = new Vector2(30f * s, 16f * s);
            wmRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 21f * s, botY);

            // 当前速率读数 (1x / 10,000x)
            _warpRateText = UIFactory.CreateText(panel.transform, "WarpRate", "1x",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft, primaryAccent);
            RectTransform wrRt = _warpRateText.GetComponent<RectTransform>();
            wrRt.sizeDelta = new Vector2(40f * s, 16f * s);
            wrRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 56f * s, botY);

            // 步退减速键 (◀)
            Vector2 stepBtnSize = new Vector2(14f * s, 16f * s);
            _downBtn = UIFactory.CreateButton(panel.transform, "Btn_Down", stepBtnSize,
                new Vector2(-panelSize.x * 0.5f + 85f * s, botY), OnStepWarpDown);
            _downBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);
            var downOut = _downBtn.gameObject.AddComponent<Outline>();
            downOut.effectColor = WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Normal);
            downOut.effectDistance = new Vector2(1f * s, 1f * s);
            _downBtnText = UIFactory.CreateText(_downBtn.transform, "Text", "◀",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _downBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 8 个多级加速指示光段 (Chevrons)
            float chevronStartX = -panelSize.x * 0.5f + 100f * s;
            float chevronW = 8.5f * s;
            float chevronH = 13f * s;
            float chevronGap = 2.5f * s;

            for (int i = 0; i < MaxChevronCount; i++)
            {
                int index = i;
                float cx = chevronStartX + i * (chevronW + chevronGap);
                GameObject chGo = UIFactory.CreatePanel(panel.transform, $"Chevron_{i}",
                    new Vector2(chevronW, chevronH), new Vector2(cx, botY),
                    WidgetStyleManager.Weighted(primaryAccent, LineWeight.Faint),
                    WidgetStyleManager.Weighted(borderCol, LineWeight.Strong), 1f * s);

                Button btn = chGo.AddComponent<Button>();
                btn.onClick.AddListener(() => OnSetWarpIndex(index));

                _chevronImgs[i] = chGo.GetComponent<Image>();
                _chevronBtns[i] = btn;
            }

            // 步进加速键 (▶)
            float upX = chevronStartX + MaxChevronCount * (chevronW + chevronGap) + 4f * s;
            _upBtn = UIFactory.CreateButton(panel.transform, "Btn_Up", stepBtnSize,
                new Vector2(upX, botY), OnStepWarpUp);
            _upBtn.GetComponent<Image>().color = WidgetStyleManager.Surface(SurfaceStyleRole.Control);
            var upOut = _upBtn.gameObject.AddComponent<Outline>();
            upOut.effectColor = WidgetStyleManager.Weighted(secondaryAccent, LineWeight.Normal);
            upOut.effectDistance = new Vector2(1f * s, 1f * s);
            _upBtnText = UIFactory.CreateText(_upBtn.transform, "Text", "▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _upBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 瞬时归一键 (1X Kill-Warp)
            Vector2 cancelBtnSize = new Vector2(26f * s, 16f * s);
            _cancelBtn = UIFactory.CreateButton(panel.transform, "Btn_Cancel", cancelBtnSize,
                new Vector2(panelSize.x * 0.5f - 16f * s, botY), OnCancelWarp);
            _cancelBtn.GetComponent<Image>().color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution);
            var cancelOut = _cancelBtn.gameObject.AddComponent<Outline>();
            cancelOut.effectColor = WidgetStyleManager.Tinted(TextStyleRole.Warning, LineWeight.Bold);
            cancelOut.effectDistance = new Vector2(1f * s, 1f * s);
            _cancelBtnText = UIFactory.CreateText(_cancelBtn.transform, "Text", "1X",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Text(TextStyleRole.Warning));
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
            if (telemetry == null) return;
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

            // 2. 暂停状态提示 (ApplyButton 语义化)
            bool isPaused = telemetry.IsGamePaused;
            if (!_hasInitState || isPaused != _lastPausedState)
            {
                _lastPausedState = isPaused;
                if (_pauseBtnText != null && _pauseBtn != null)
                {
                    _pauseBtnText.text = isPaused ? "RESUME" : "PAUSE";
                    ApplyButton(_pauseBtn, _pauseBtn.GetComponent<Image>(), _pauseBtnText, isPaused ? ButtonVisualRole.Danger : ButtonVisualRole.Normal, isPaused, theme);
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
                Color dimColor = WidgetStyleManager.Weighted(litColor, LineWeight.Faint);

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
                    ApplyButton(_cancelBtn, _cancelBtn.GetComponent<Image>(), _cancelBtnText, canCancel ? ButtonVisualRole.Warning : ButtonVisualRole.Normal, canCancel, theme);
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

        public override void ApplyTheme(ThemeConfig theme)
        {
            _currentTheme = theme;
            if (theme == null) return;

            ApplyCard(_panelBg, _panelOutline, CardStyleRole.Normal, theme);
            if (_topStripe != null) _topStripe.color = (Color)theme.AccentPrimary;

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
