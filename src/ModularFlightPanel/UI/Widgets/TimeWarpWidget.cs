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
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(236f * s, 46f * s);
            RectTransform.sizeDelta = panelSize;

            Color bgCol = (theme != null) ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.92f);
            Color borderCol = (theme != null) ? (Color)theme.FrameBorderColor : Color.cyan;
            Color primaryAccent = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color secondaryAccent = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color textPrimary = (theme != null) ? (Color)theme.TextPrimaryColor : Color.white;

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
            _modeBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var modeOut = _modeBtn.gameObject.AddComponent<Outline>();
            modeOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.45f);
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
            _pauseBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var pauseOut = _pauseBtn.gameObject.AddComponent<Outline>();
            pauseOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.45f);
            pauseOut.effectDistance = new Vector2(1f * s, 1f * s);
            _pauseBtnText = UIFactory.CreateText(_pauseBtn.transform, "Text", "PAUSE",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, primaryAccent);
            _pauseBtnText.GetComponent<RectTransform>().sizeDelta = pauseBtnSize;

            // 原版时间栏显隐开关 (STOCK)
            Vector2 stockBtnSize = new Vector2(26f * s, 16f * s);
            _stockBtn = UIFactory.CreateButton(panel.transform, "Btn_Stock", stockBtnSize,
                new Vector2(panelSize.x * 0.5f - 16f * s, topY), OnToggleStock);
            _stockBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var stockOut = _stockBtn.gameObject.AddComponent<Outline>();
            stockOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.45f);
            stockOut.effectDistance = new Vector2(1f * s, 1f * s);
            _stockBtnText = UIFactory.CreateText(_stockBtn.transform, "Text", "KSP",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
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
            _downBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var downOut = _downBtn.gameObject.AddComponent<Outline>();
            downOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
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
                    new Color(primaryAccent.r, primaryAccent.g, primaryAccent.b, 0.2f),
                    new Color(borderCol.r, borderCol.g, borderCol.b, 0.4f), 1f * s);

                Button btn = chGo.AddComponent<Button>();
                btn.onClick.AddListener(() => OnSetWarpIndex(index));

                _chevronImgs[i] = chGo.GetComponent<Image>();
                _chevronBtns[i] = btn;
            }

            // 步进加速键 (▶)
            float upX = chevronStartX + MaxChevronCount * (chevronW + chevronGap) + 4f * s;
            _upBtn = UIFactory.CreateButton(panel.transform, "Btn_Up", stepBtnSize,
                new Vector2(upX, botY), OnStepWarpUp);
            _upBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
            var upOut = _upBtn.gameObject.AddComponent<Outline>();
            upOut.effectColor = new Color(secondaryAccent.r, secondaryAccent.g, secondaryAccent.b, 0.35f);
            upOut.effectDistance = new Vector2(1f * s, 1f * s);
            _upBtnText = UIFactory.CreateText(_upBtn.transform, "Text", "▶",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, secondaryAccent);
            _upBtnText.GetComponent<RectTransform>().sizeDelta = stepBtnSize;

            // 瞬时归一键 (1X Kill-Warp)
            Vector2 cancelBtnSize = new Vector2(26f * s, 16f * s);
            _cancelBtn = UIFactory.CreateButton(panel.transform, "Btn_Cancel", cancelBtnSize,
                new Vector2(panelSize.x * 0.5f - 16f * s, botY), OnCancelWarp);
            _cancelBtn.GetComponent<Image>().color = new Color(0.16f, 0.10f, 0.05f, 0.95f);
            var cancelOut = _cancelBtn.gameObject.AddComponent<Outline>();
            cancelOut.effectColor = new Color(0.95f, 0.65f, 0.10f, 0.65f);
            cancelOut.effectDistance = new Vector2(1f * s, 1f * s);
            _cancelBtnText = UIFactory.CreateText(_cancelBtn.transform, "Text", "1X",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, new Color(1.0f, 0.75f, 0.20f));
            _cancelBtnText.GetComponent<RectTransform>().sizeDelta = cancelBtnSize;

            // 初始化根据主题配置执行原版时间栏静默隐藏
            _stockHidden = ThemeManager.IsStockTimeWarpHidden;
            NavBallHookService.HideStockTimeWarpAction?.Invoke(_stockHidden);

            ApplyTheme(theme);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
            _lastTelemetry = telemetry;

            // 1. 更新时钟读数
            if (_clockText != null)
            {
                if (_showUniversalTime)
                {
                    _clockText.text = TelemetryTokenEngine.Evaluate("{UT}", telemetry);
                }
                else
                {
                    _clockText.text = TelemetryTokenEngine.Evaluate("{MET}", telemetry);
                }
            }

            // 2. 暂停状态提示
            if (_pauseBtnText != null && _pauseBtn != null)
            {
                if (telemetry.IsGamePaused)
                {
                    _pauseBtnText.text = "RESUME";
                    _pauseBtnText.color = new Color(1.0f, 0.35f, 0.25f);
                    _pauseBtn.GetComponent<Image>().color = new Color(0.25f, 0.06f, 0.06f, 0.95f);
                }
                else
                {
                    _pauseBtnText.text = "PAUSE";
                    _pauseBtnText.color = _currentTheme != null ? (Color)_currentTheme.AccentPrimary : Color.green;
                    _pauseBtn.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 0.95f);
                }
            }

            // 3. 加速模式与倍率
            bool isPhys = telemetry.IsPhysicsWarp;
            Color accent = isPhys
                ? (_currentTheme != null ? (Color)_currentTheme.WarningColor : new Color(0.95f, 0.65f, 0.10f))
                : (_currentTheme != null ? (Color)_currentTheme.AccentPrimary : Color.green);

            if (_warpModeText != null)
            {
                _warpModeText.text = isPhys ? "PHYS" : "WARP";
                _warpModeText.color = isPhys ? accent : (_currentTheme != null ? (Color)_currentTheme.AccentSecondary : Color.cyan);
            }

            if (_warpRateText != null)
            {
                double rate = telemetry.TimeWarpRate;
                if (rate >= 1000.0)
                    _warpRateText.text = $"{rate:N0}x";
                else if (rate > 1.0)
                    _warpRateText.text = $"{rate:0.#}x";
                else
                    _warpRateText.text = "1x";
                _warpRateText.color = (rate > 1.0) ? accent : (_currentTheme != null ? (Color)_currentTheme.TextPrimaryColor : Color.white);
            }

            // 4. 加速光段状态机
            int activeIndex = telemetry.TimeWarpRateIndex;
            int maxIndex = Mathf.Clamp(telemetry.MaxTimeWarpRateIndex, 1, MaxChevronCount - 1);

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
                    if (i <= activeIndex)
                    {
                        // 处于激活加速段内，高亮呈现
                        _chevronImgs[i].color = accent;
                    }
                    else
                    {
                        // 未激活但可用，微光暗色呈现
                        _chevronImgs[i].color = new Color(accent.r, accent.g, accent.b, 0.18f);
                    }
                }
            }

            // 5. 瞬时归一按钮高亮状态
            if (_cancelBtnText != null && _cancelBtn != null)
            {
                if (activeIndex > 0)
                {
                    _cancelBtnText.color = new Color(1.0f, 0.75f, 0.20f);
                    _cancelBtn.GetComponent<Image>().color = new Color(0.24f, 0.14f, 0.05f, 0.95f);
                }
                else
                {
                    _cancelBtnText.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    _cancelBtn.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.14f, 0.85f);
                }
            }

            // 6. 原版时间栏显隐按键文本与颜色
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
            if (_clockText != null) _clockText.color = theme.TextPrimaryColor;
            if (_modeBtnText != null) _modeBtnText.color = theme.AccentSecondary;
            if (_downBtnText != null) _downBtnText.color = theme.AccentSecondary;
            if (_upBtnText != null) _upBtnText.color = theme.AccentSecondary;
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
    }
}
