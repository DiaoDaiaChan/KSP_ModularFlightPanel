using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 风格滚动标尺带套件 (Speed Tape / Altitude Tape Kit)
    /// 具备：
    /// 1. 物理垂直滚动的刻度梯级 (平滑浮点运算，零GC，抗大数值抖动)
    /// 2. 主副刻度齿交替与数字标牌
    /// 3. 中央高对比度读数窗口与指向姿态球的游标箭头
    /// 4. 模式切换快捷响应 (点击切换地表/轨道/目标速度，或海拔/真实高度)
    /// 5. 完全基于通配符与 CustomTemplate 双驱动，零硬编码，统一样式管道
    /// </summary>
    public class TapeGaugeWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        private const int TICK_POOL_SIZE = 28;

        private Image _bgImage;
        private Outline _bgOutline;
        private RectTransform _viewportRt;
        private RectTransform _tickContainer;

        // 刻度池项
        private struct TickItem
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
            public Text Label;
        }
        private readonly List<TickItem> _tickPool = new List<TickItem>(TICK_POOL_SIZE);

        // 中央高对比度读数窗口
        private RectTransform _centerBoxRt;
        private Image _centerBoxBg;
        private Outline _centerBoxOutline;
        private Text _centerValueText;
        private Image _pointerArrow;
        private Button _centerBoxBtn;

        // 模式与顶部/底部标签外框
        private GameObject _topModeBox;
        private Image _topModeBg;
        private Outline _topModeOutline;
        private Text _topModeText;

        private GameObject _bottomUnitBox;
        private Image _bottomUnitBg;
        private Outline _bottomUnitOutline;
        private Text _bottomUnitText;

        // 真实 PFD 动态升降率 / 加速度趋势指示条与指示箭头 (Rate / Trend Vector)
        private RectTransform _trendRoot;
        private GameObject _trendTagBox;
        private Outline _trendTagOutline;
        private Image _trendTagBg;
        private Text _trendTagText;
        private Text _trendRateText;
        private Text _trendUnitText;

        private GameObject _trendTrackBgObj;
        private Image _trendTrackBg;
        private Outline _trendTrackOutline;
        private Image _trendTrack;
        private Image _trendZeroTick;
        private RectTransform _trendBarRt;
        private Image _trendBarImg;
        private RectTransform _trendArrowRt;
        private Image _trendArrowImg;

        // 通配符通道与模板
        private string _valueToken = "{SPD}";
        private string _topModeTemplate = "SPD";
        private string _bottomUnitTemplate = "m/s";
        private string _trendToken = "{GFORCE}";
        private string _trendTagTemplate = "ACC";
        private string _trendUnitTemplate = "G";
        private float _trendMaxScale = 4.0f;

        // 脏检查与缓存
        private double _lastValue = double.NaN;
        private double _lastTrendVal = double.NaN;
        private string _lastCenterText = string.Empty;
        private string _lastTopText = string.Empty;
        private string _lastBottomText = string.Empty;
        private string _lastTrendTagText = string.Empty;
        private string _lastTrendRateText = string.Empty;
        private string _lastTrendUnitText = string.Empty;
        private bool _lastTrendPositive = true;

        public static Action OnCycleSpeedModeAction;
        public static Action OnCycleAltitudeModeAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float width = 46f * s;
            float height = 210f * s;
            RectTransform.sizeDelta = new Vector2(width, height);

            ParseCustomTemplate(config);

            // 1. 半透明背景板
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _bgOutline = gameObject.AddComponent<Outline>();
            _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _bgOutline.effectColor, s);

            // 2. 标尺视口 (裁剪超出范围的刻度)
            GameObject viewportObj = new GameObject("Tape_Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(transform, false);
            _viewportRt = viewportObj.GetComponent<RectTransform>();
            _viewportRt.sizeDelta = new Vector2(width, height - 12f * s);
            _viewportRt.anchoredPosition = Vector2.zero;

            // 刻度容器
            GameObject containerObj = new GameObject("Tick_Container", typeof(RectTransform));
            containerObj.transform.SetParent(_viewportRt, false);
            _tickContainer = containerObj.GetComponent<RectTransform>();
            _tickContainer.sizeDelta = _viewportRt.sizeDelta;
            _tickContainer.anchoredPosition = Vector2.zero;

            // 初始化刻度对象池
            BuildTickPool(theme);

            // 3. 中央高对比度读数窗口 (Center Readout Box)
            BuildCenterReadoutBox(theme);

            // 4. 顶部与底部模式标牌
            BuildLabels(theme);

            // 5. 真实的 PFD 动态指示条与指示箭头 (V/S 升降率 / 加速度趋势)
            BuildDynamicTrendIndicator(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            bool isLeft = config != null && config.IsLeftOrientation;
            string numToken = config?.NumericToken ?? "";

            if (isLeft || numToken.Contains("SPD"))
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{SPD}";
                _topModeTemplate = "SPD";
                _bottomUnitTemplate = !string.IsNullOrEmpty(config?.UnitLabel) ? config.UnitLabel : "m/s";
                _trendToken = "{GFORCE}";
                _trendTagTemplate = "ACC";
                _trendUnitTemplate = "G";
                _trendMaxScale = 4.0f;
            }
            else
            {
                _valueToken = !string.IsNullOrEmpty(numToken) ? numToken : "{ALT}";
                _topModeTemplate = "ALT";
                _bottomUnitTemplate = !string.IsNullOrEmpty(config?.UnitLabel) ? config.UnitLabel : "m";
                _trendToken = "{VS}";
                _trendTagTemplate = "V/S";
                _trendUnitTemplate = "m/s";
                _trendMaxScale = 100.0f;
            }

            if (!string.IsNullOrEmpty(config?.DisplayName))
            {
                _topModeTemplate = config.DisplayName;
            }

            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "VAL":
                    case "VALUE":
                    case "TOKEN":
                        _valueToken = v;
                        break;
                    case "TOP":
                    case "MODE":
                    case "TOP_LABEL":
                        _topModeTemplate = v;
                        break;
                    case "UNIT":
                    case "BOTTOM":
                    case "BOTTOM_LABEL":
                        _bottomUnitTemplate = v;
                        break;
                    case "TREND_VAL":
                    case "TREND_TOKEN":
                        _trendToken = v;
                        break;
                    case "TREND_TAG":
                    case "TREND_LABEL":
                        _trendTagTemplate = v;
                        break;
                    case "TREND_UNIT":
                        _trendUnitTemplate = v;
                        break;
                    case "TREND_MAX":
                        if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maxS))
                            _trendMaxScale = maxS;
                        break;
                }
            }
        }

        private void BuildTickPool(ThemeConfig theme)
        {
            _tickPool.Clear();
            float s = CurrentDpiScale;
            float tickX = Config.IsLeftOrientation ? (18f * s) : (-18f * s);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                GameObject itemObj = new GameObject($"Tick_{i}", typeof(RectTransform));
                itemObj.transform.SetParent(_tickContainer, false);
                RectTransform rt = itemObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(40f * s, 16f * s);

                // 刻度线
                GameObject lineObj = new GameObject("Tick_Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(itemObj.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(7f * s, 1.5f * s);
                lineRt.anchoredPosition = new Vector2(tickX, 0f);
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);

                // 刻度数字
                int fontSize = Mathf.RoundToInt(9f * s);
                TextAnchor align = Config.IsLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                Text labelTxt = UIFactory.CreateText(itemObj.transform, "Tick_Text", "0", fontSize, align,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                RectTransform labelRt = labelTxt.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(25f * s, 14f * s);
                float labelX = Config.IsLeftOrientation ? (tickX - 15f * s) : (tickX + 15f * s);
                labelRt.anchoredPosition = new Vector2(labelX, 0f);

                _tickPool.Add(new TickItem
                {
                    Root = itemObj,
                    Rect = rt,
                    Line = lineImg,
                    Label = labelTxt
                });
            }
        }

        private void BuildCenterReadoutBox(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float boxW = 50f * s;
            float boxH = 24f * s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            GameObject boxObj = new GameObject("Center_Readout_Box", typeof(RectTransform), typeof(Image), typeof(Button));
            boxObj.transform.SetParent(transform, false);
            _centerBoxRt = boxObj.GetComponent<RectTransform>();
            _centerBoxRt.sizeDelta = new Vector2(boxW, boxH);

            float offsetX = Config.IsLeftOrientation ? (3f * s) : (-3f * s);
            _centerBoxRt.anchoredPosition = new Vector2(offsetX, 0f);

            _centerBoxBg = boxObj.GetComponent<Image>();
            _centerBoxBg.color = Color.clear;

            _centerBoxOutline = boxObj.AddComponent<Outline>();
            _centerBoxOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.SubtleSlot, theme);

            _centerBoxBtn = boxObj.GetComponent<Button>();
            if (_centerBoxBtn != null)
            {
                _centerBoxBtn.onClick.AddListener(OnBoxClicked);
            }

            // 游标指示箭头 (45度菱形指针)
            GameObject arrowObj = new GameObject("Pointer_Arrow", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(boxObj.transform, false);
            RectTransform arrowRt = arrowObj.GetComponent<RectTransform>();
            arrowRt.sizeDelta = new Vector2(6f * s, 6f * s);
            float arrowX = Config.IsLeftOrientation ? (boxW * 0.5f + 3f * s) : (-boxW * 0.5f - 3f * s);
            arrowRt.anchoredPosition = new Vector2(arrowX, 0f);
            arrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _pointerArrow = arrowObj.GetComponent<Image>();
            _pointerArrow.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);

            // 中央实时数字
            int valFontSize = Mathf.RoundToInt(12f * s);
            _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxW - 6f * s, boxH);
            valRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLabels(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float halfH = RectTransform.sizeDelta.y * 0.5f;
            float w = RectTransform.sizeDelta.x;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 顶部模式文字标牌盒
            Vector2 topBoxSize = new Vector2(w, 18f * s);
            Vector2 topBoxPos = new Vector2(0f, halfH + 11f * s);
            _topModeBox = UIFactory.CreatePanel(transform, "Top_Mode_Box", topBoxSize, topBoxPos, Color.clear);
            _topModeBg = _topModeBox.GetComponent<Image>();
            _topModeOutline = _topModeBox.AddComponent<Outline>();
            _topModeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_topModeBg, _topModeOutline, CardStyleRole.Normal, theme);

            int topFontSize = Mathf.RoundToInt(9f * s);
            _topModeText = UIFactory.CreateText(_topModeBox.transform, "Top_Mode", _topModeTemplate, topFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform topRt = _topModeText.GetComponent<RectTransform>();
            topRt.sizeDelta = topBoxSize;
            topRt.anchoredPosition = Vector2.zero;
            _topModeText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 2. 底部单位文字标牌盒
            Vector2 btmBoxSize = new Vector2(w, 16f * s);
            Vector2 btmBoxPos = new Vector2(0f, -halfH - 10f * s);
            _bottomUnitBox = UIFactory.CreatePanel(transform, "Bottom_Unit_Box", btmBoxSize, btmBoxPos, Color.clear);
            _bottomUnitBg = _bottomUnitBox.GetComponent<Image>();
            _bottomUnitOutline = _bottomUnitBox.AddComponent<Outline>();
            _bottomUnitOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bottomUnitBg, _bottomUnitOutline, CardStyleRole.Normal, theme);

            int btmFontSize = Mathf.RoundToInt(8.5f * s);
            _bottomUnitText = UIFactory.CreateText(_bottomUnitBox.transform, "Bottom_Unit", _bottomUnitTemplate, btmFontSize, TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform btmRt = _bottomUnitText.GetComponent<RectTransform>();
            btmRt.sizeDelta = btmBoxSize;
            btmRt.anchoredPosition = Vector2.zero;
            _bottomUnitText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildDynamicTrendIndicator(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 侧边指示条挂载点
            float trendX = Config.IsLeftOrientation ? -(width * 0.5f + 7f * s) : (width * 0.5f + 7f * s);

            GameObject root = new GameObject("Trend_Indicator_Root", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _trendRoot = root.GetComponent<RectTransform>();
            _trendRoot.sizeDelta = new Vector2(24f * s, 210f * s);
            _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

            // 1. 顶部固定三层胶囊标牌盒
            Vector2 boxSize = new Vector2(24f * s, 32f * s);
            Vector2 boxPos = new Vector2(0f, 85f * s);

            _trendTagBox = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Tag_Box", boxSize, boxPos, Color.clear);
            _trendTagBg = _trendTagBox.GetComponent<Image>();
            _trendTagOutline = _trendTagBox.AddComponent<Outline>();
            _trendTagOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_trendTagBg, _trendTagOutline, CardStyleRole.Normal, theme);

            // 1.1 标牌标题
            _trendTagText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Tag_Title", _trendTagTemplate,
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            RectTransform tagRt = _trendTagText.GetComponent<RectTransform>();
            tagRt.sizeDelta = new Vector2(boxSize.x, 9f * s);
            tagRt.anchoredPosition = new Vector2(0f, 9.5f * s);
            _trendTagText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 1.2 动态读数数值
            _trendRateText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Rate_Val", "0.0",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _trendRateText.fontStyle = FontStyle.Bold;
            RectTransform rateRt = _trendRateText.GetComponent<RectTransform>();
            rateRt.sizeDelta = new Vector2(boxSize.x, 10f * s);
            rateRt.anchoredPosition = new Vector2(0f, 0f);
            _trendRateText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 1.3 物理单位
            _trendUnitText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Unit", _trendUnitTemplate,
                Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Unit, theme));
            RectTransform unitRt = _trendUnitText.GetComponent<RectTransform>();
            unitRt.sizeDelta = new Vector2(boxSize.x, 8f * s);
            unitRt.anchoredPosition = new Vector2(0f, -9.5f * s);
            _trendUnitText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 2. 垂直基准轨道暗色遮光背景槽
            Vector2 trackBgSize = new Vector2(20f * s, 134f * s);
            Vector2 trackBgPos = new Vector2(0f, -6f * s);
            _trendTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Track_Bg", trackBgSize, trackBgPos, Color.clear);
            _trendTrackBg = _trendTrackBgObj.GetComponent<Image>();
            _trendTrackOutline = _trendTrackBgObj.AddComponent<Outline>();
            _trendTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_trendTrackBg, _trendTrackOutline, CardStyleRole.SubtleSlot, theme);

            // 2.1 垂直基准轨道线
            GameObject trackObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_Track",
                new Vector2(1.5f * s, 118f * s), Vector2.zero,
                style.GetMeterColor(MeterStyleRole.Track, theme));
            _trendTrack = trackObj.GetComponent<Image>();

            // 2.2 中央 0 刻度基准线
            GameObject zeroObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_ZeroTick",
                new Vector2(8f * s, 1.5f * s), Vector2.zero,
                style.GetMeterColor(MeterStyleRole.Track, theme));
            _trendZeroTick = zeroObj.GetComponent<Image>();

            // 2.3 辅助刻度线 (±25, ±50)
            float[] tickOffsets = new float[] { 25f * s, 50f * s, -25f * s, -50f * s };
            Color trackTickCol = style.GetMeterColor(MeterStyleRole.Track, theme);
            foreach (float yOff in tickOffsets)
            {
                UIFactory.CreatePanel(_trendTrackBgObj.transform, $"Trend_Tick_{yOff:F0}",
                    new Vector2(5f * s, 1f * s), new Vector2(0f, yOff), trackTickCol);
            }

            // 2.4 动态伸缩指示条
            GameObject barObj = new GameObject("Trend_Bar", typeof(RectTransform), typeof(Image));
            barObj.transform.SetParent(_trendTrackBgObj.transform, false);
            _trendBarRt = barObj.GetComponent<RectTransform>();
            _trendBarRt.sizeDelta = new Vector2(3f * s, 0f);
            _trendBarRt.pivot = new Vector2(0.5f, 0f);
            _trendBarRt.anchoredPosition = Vector2.zero;
            _trendBarImg = barObj.GetComponent<Image>();
            _trendBarImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 2.5 动态箭头微标
            GameObject arrowObj = new GameObject("Trend_Arrow", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(_trendTrackBgObj.transform, false);
            _trendArrowRt = arrowObj.GetComponent<RectTransform>();
            _trendArrowRt.sizeDelta = new Vector2(6f * s, 6f * s);
            _trendArrowRt.anchoredPosition = Vector2.zero;
            _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _trendArrowImg = arrowObj.GetComponent<Image>();
            _trendArrowImg.color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
        }

        private void OnBoxClicked()
        {
            if (Config.IsLeftOrientation || _valueToken.Contains("SPD"))
            {
                OnCycleSpeedModeAction?.Invoke();
            }
            else
            {
                OnCycleAltitudeModeAction?.Invoke();
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            // 采样主驱动数值
            double currentVal = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            if (double.IsNaN(currentVal)) currentVal = 0.0;

            // 1. 脏标记检查并更新主刻度与读数
            double deltaThreshold = Config != null && Config.ValueDeltaThreshold > 0.0 ? Config.ValueDeltaThreshold : 0.05;
            if (double.IsNaN(_lastValue) || Math.Abs(currentVal - _lastValue) > deltaThreshold)
            {
                _lastValue = currentVal;
                UpdateCenterReadout(currentVal);
                UpdateRollingTape(currentVal);
            }

            // 2. 动态求值并更新模式与单位标签
            UpdateLabels(telemetry);

            // 3. 动态趋势指示条与箭头
            UpdateDynamicTrendIndicator(telemetry);
        }

        private void UpdateDynamicTrendIndicator(IFlightTelemetry telemetry)
        {
            if (_trendBarRt == null || telemetry == null) return;

            float s = CurrentDpiScale;
            float maxBarLength = 50f * s;

            // 统一由 Token 求值获取趋势值 (如 {GFORCE} 或 {VS})
            double trendVal = TelemetryTokenEngine.EvaluateNumeric(_trendToken, telemetry);
            if (double.IsNaN(trendVal)) trendVal = 0.0;

            float maxScale = _trendMaxScale > 0.001f ? _trendMaxScale : 4.0f;
            float rateFraction = Mathf.Clamp((float)(trendVal / maxScale), -1f, 1f);

            bool isPositive = rateFraction >= 0f;
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);

            // 脏检查与状态更新
            if (double.IsNaN(_lastTrendVal) || Math.Abs(trendVal - _lastTrendVal) > 0.02 || isPositive != _lastTrendPositive)
            {
                _lastTrendVal = trendVal;
                _lastTrendPositive = isPositive;

                MeterStyleRole meterRole = isPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
                if (_trendBarImg != null) _trendBarImg.color = WidgetStyleManager.Meter(meterRole, theme);
                if (_trendArrowImg != null) _trendArrowImg.color = WidgetStyleManager.Meter(meterRole, theme);

                TextStyleRole textRole = isPositive ? TextStyleRole.Accent : TextStyleRole.Warning;
                ApplyText(_trendRateText, textRole, theme);

                string formattedRate;
                if (Math.Abs(trendVal) < 0.05)
                {
                    formattedRate = "0.0";
                }
                else if (trendVal > 0.0)
                {
                    formattedRate = trendVal >= 1000.0 ? $"+{trendVal / 1000.0:F1}k" : $"+{trendVal:F1}";
                }
                else
                {
                    double absVal = Math.Abs(trendVal);
                    formattedRate = absVal >= 1000.0 ? $"-{absVal / 1000.0:F1}k" : $"-{absVal:F1}";
                }

                if (formattedRate != _lastTrendRateText)
                {
                    _lastTrendRateText = formattedRate;
                    if (_trendRateText != null) _trendRateText.text = formattedRate;
                }
            }

            float dynamicLen = Mathf.Abs(rateFraction) * maxBarLength;
            if (isPositive)
            {
                _trendBarRt.pivot = new Vector2(0.5f, 0f);
                _trendBarRt.anchoredPosition = Vector2.zero;
                _trendBarRt.sizeDelta = new Vector2(3f * s, dynamicLen);

                _trendArrowRt.anchoredPosition = new Vector2(0f, dynamicLen + 3f * s);
                _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            }
            else
            {
                _trendBarRt.pivot = new Vector2(0.5f, 1f);
                _trendBarRt.anchoredPosition = Vector2.zero;
                _trendBarRt.sizeDelta = new Vector2(3f * s, dynamicLen);

                _trendArrowRt.anchoredPosition = new Vector2(0f, -dynamicLen - 3f * s);
                _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            }

            if (_trendArrowImg != null)
            {
                _trendArrowImg.gameObject.SetActive(dynamicLen > 2f * s);
            }
        }

        private void UpdateCenterReadout(double val)
        {
            string formatted;
            if (Math.Abs(val) >= 100000.0)
            {
                formatted = $"{val / 1000.0:F1}k";
            }
            else if (Math.Abs(val) >= 10000.0)
            {
                formatted = $"{Math.Round(val):N0}";
            }
            else if (Math.Abs(val) >= 100.0)
            {
                formatted = $"{Math.Round(val, 1):F1}";
            }
            else
            {
                formatted = $"{val:F1}";
            }

            if (formatted != _lastCenterText)
            {
                _lastCenterText = formatted;
                if (_centerValueText != null) _centerValueText.text = formatted;
            }
        }

        private void UpdateLabels(IFlightTelemetry telemetry)
        {
            string evalTop = TelemetryTokenEngine.Evaluate(_topModeTemplate, telemetry);
            if (evalTop != _lastTopText)
            {
                _lastTopText = evalTop;
                if (_topModeText != null) _topModeText.text = evalTop;
            }

            string evalBtm = TelemetryTokenEngine.Evaluate(_bottomUnitTemplate, telemetry);
            if (evalBtm != _lastBottomText)
            {
                _lastBottomText = evalBtm;
                if (_bottomUnitText != null) _bottomUnitText.text = evalBtm;
            }

            string evalTag = TelemetryTokenEngine.Evaluate(_trendTagTemplate, telemetry);
            if (evalTag != _lastTrendTagText)
            {
                _lastTrendTagText = evalTag;
                if (_trendTagText != null) _trendTagText.text = evalTag;
            }

            string evalUnit = TelemetryTokenEngine.Evaluate(_trendUnitTemplate, telemetry);
            if (evalUnit != _lastTrendUnitText)
            {
                _lastTrendUnitText = evalUnit;
                if (_trendUnitText != null) _trendUnitText.text = evalUnit;
            }
        }

        private void UpdateRollingTape(double currentVal)
        {
            float step = Config != null && Config.StepInterval > 0f ? Config.StepInterval : (Config != null && Config.IsLeftOrientation ? 10f : 100f);
            float pixelsPerUnit = (28f * CurrentDpiScale) / step;
            float visibleHalfSpan = (_viewportRt.sizeDelta.y * 0.5f) / pixelsPerUnit;

            // 计算视口起始刻度 (向下对齐整刻度)
            double startTick = Math.Floor((currentVal - visibleHalfSpan) / step) * step;

            for (int i = 0; i < _tickPool.Count; i++)
            {
                TickItem item = _tickPool[i];
                double tickVal = startTick + i * step;

                // 速度带通常不显示负刻度
                if (tickVal < 0.0 && Config != null && Config.IsLeftOrientation)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                float y = (float)(tickVal - currentVal) * pixelsPerUnit;
                if (Math.Abs(y) > (_viewportRt.sizeDelta.y * 0.5f) + 12f * CurrentDpiScale)
                {
                    item.Root.SetActive(false);
                    continue;
                }

                item.Root.SetActive(true);
                item.Rect.anchoredPosition = new Vector2(0f, y);

                long tickIndex = (long)Math.Round(tickVal / step);
                bool isMajor = (tickIndex % 2 == 0);

                RectTransform lineRt = item.Line.GetComponent<RectTransform>();
                if (isMajor)
                {
                    lineRt.sizeDelta = new Vector2(15f * CurrentDpiScale, 2f * CurrentDpiScale);
                    item.Label.gameObject.SetActive(true);

                    if (Math.Abs(tickVal) >= 100000.0)
                        item.Label.text = $"{tickVal / 1000.0:F0}k";
                    else if (Math.Abs(tickVal) >= 10000.0)
                        item.Label.text = $"{Math.Round(tickVal):N0}";
                    else
                        item.Label.text = $"{tickVal:F0}";
                }
                else
                {
                    lineRt.sizeDelta = new Vector2(8f * CurrentDpiScale, 1.5f * CurrentDpiScale);
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);
            ApplyCard(_centerBoxBg, _centerBoxOutline, CardStyleRole.SubtleSlot, theme);
            if (_pointerArrow != null) _pointerArrow.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);
            ApplyText(_centerValueText, TextStyleRole.PrimaryValue, theme);

            ApplyCard(_topModeBg, _topModeOutline, CardStyleRole.Normal, theme);
            ApplyText(_topModeText, TextStyleRole.Cardinal, theme);

            ApplyCard(_bottomUnitBg, _bottomUnitOutline, CardStyleRole.Normal, theme);
            ApplyText(_bottomUnitText, TextStyleRole.Unit, theme);

            ApplyCard(_trendTagBg, _trendTagOutline, CardStyleRole.Normal, theme);
            ApplyText(_trendTagText, TextStyleRole.Cardinal, theme);
            ApplyText(_trendRateText, _lastTrendPositive ? TextStyleRole.Accent : TextStyleRole.Warning, theme);
            ApplyText(_trendUnitText, TextStyleRole.Unit, theme);

            ApplyCard(_trendTrackBg, _trendTrackOutline, CardStyleRole.SubtleSlot, theme);
            if (_trendTrack != null) _trendTrack.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
            if (_trendZeroTick != null) _trendZeroTick.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);

            MeterStyleRole trendRole = _lastTrendPositive ? MeterStyleRole.Primary : MeterStyleRole.Warning;
            if (_trendBarImg != null) _trendBarImg.color = WidgetStyleManager.Meter(trendRole, theme);
            if (_trendArrowImg != null) _trendArrowImg.color = WidgetStyleManager.Meter(trendRole, theme);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                {
                    ApplyText(_tickPool[i].Label, TextStyleRole.PrimaryValue, theme);
                }
                if (_tickPool[i].Line != null)
                {
                    _tickPool[i].Line.color = WidgetStyleManager.Meter(MeterStyleRole.Track, theme);
                }
            }
        }

        protected override void OnDestroy()
        {
            if (_centerBoxBtn != null)
            {
                _centerBoxBtn.onClick.RemoveListener(OnBoxClicked);
                _centerBoxBtn = null;
            }
            base.OnDestroy();
        }
    }
}
