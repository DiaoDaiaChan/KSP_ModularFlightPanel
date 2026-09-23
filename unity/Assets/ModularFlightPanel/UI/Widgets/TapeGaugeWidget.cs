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
    /// 5. 完全基于通配符与元数据驱动
    /// </summary>
    public class TapeGaugeWidget : BaseFlightWidget
    {
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

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            // 基础尺寸设置：精简为窄体现代航电 PFD 标尺宽度与紧凑比例高度 (46x210px)
            float width = 46f * CurrentDpiScale;
            float height = 210f * CurrentDpiScale;
            RectTransform.sizeDelta = new Vector2(width, height);

            // 1. 半透明背景板
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = theme.FrameBgColor;

            _bgOutline = gameObject.AddComponent<Outline>();
            Color borderCol = theme.FrameBorderColor;
            _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            _bgOutline.effectDistance = new Vector2(1f * CurrentDpiScale, 1f * CurrentDpiScale);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _bgOutline.effectColor, CurrentDpiScale);

            // 2. 标尺视口 (裁剪超出范围的刻度)
            GameObject viewportObj = new GameObject("Tape_Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObj.transform.SetParent(transform, false);
            _viewportRt = viewportObj.GetComponent<RectTransform>();
            _viewportRt.sizeDelta = new Vector2(width, height - 12f * CurrentDpiScale);
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

        private void BuildTickPool(ThemeConfig theme)
        {
            _tickPool.Clear();
            float tickX = Config.IsLeftOrientation ? (18f * CurrentDpiScale) : (-18f * CurrentDpiScale);

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                GameObject itemObj = new GameObject($"Tick_{i}", typeof(RectTransform));
                itemObj.transform.SetParent(_tickContainer, false);
                RectTransform rt = itemObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(40f * CurrentDpiScale, 16f * CurrentDpiScale);

                // 刻度线
                GameObject lineObj = new GameObject("Tick_Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(itemObj.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(7f * CurrentDpiScale, 1.5f * CurrentDpiScale);
                lineRt.anchoredPosition = new Vector2(tickX, 0f);
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = theme.TextAccentColor;

                // 刻度数字
                int fontSize = Mathf.RoundToInt(9f * CurrentDpiScale);
                TextAnchor align = Config.IsLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                Text labelTxt = UIFactory.CreateText(itemObj.transform, "Tick_Text", "0", fontSize, align, theme.TextPrimaryColor);
                RectTransform labelRt = labelTxt.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(25f * CurrentDpiScale, 14f * CurrentDpiScale);
                float labelX = Config.IsLeftOrientation ? (tickX - 15f * CurrentDpiScale) : (tickX + 15f * CurrentDpiScale);
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
            float boxW = 50f * CurrentDpiScale;
            float boxH = 24f * CurrentDpiScale;

            GameObject boxObj = new GameObject("Center_Readout_Box", typeof(RectTransform), typeof(Image), typeof(Button));
            boxObj.transform.SetParent(transform, false);
            _centerBoxRt = boxObj.GetComponent<RectTransform>();
            _centerBoxRt.sizeDelta = new Vector2(boxW, boxH);

            // 让游标微凸指向 Navball 方向
            float offsetX = Config.IsLeftOrientation ? (3f * CurrentDpiScale) : (-3f * CurrentDpiScale);
            _centerBoxRt.anchoredPosition = new Vector2(offsetX, 0f);

            _centerBoxBg = boxObj.GetComponent<Image>();
            _centerBoxBg.color = new Color(0.02f, 0.04f, 0.06f, 0.95f);

            _centerBoxOutline = boxObj.AddComponent<Outline>();
            Color accentColor = theme.AccentSecondary;
            _centerBoxOutline.effectColor = accentColor;
            _centerBoxOutline.effectDistance = new Vector2(1f * CurrentDpiScale, 1f * CurrentDpiScale);

            Button btn = boxObj.GetComponent<Button>();
            btn.onClick.AddListener(OnBoxClicked);

            // 游标指示箭头 (三角形或指示柱)
            GameObject arrowObj = new GameObject("Pointer_Arrow", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(boxObj.transform, false);
            RectTransform arrowRt = arrowObj.GetComponent<RectTransform>();
            arrowRt.sizeDelta = new Vector2(6f * CurrentDpiScale, 6f * CurrentDpiScale);
            float arrowX = Config.IsLeftOrientation ? (boxW * 0.5f + 3f * CurrentDpiScale) : (-boxW * 0.5f - 3f * CurrentDpiScale);
            arrowRt.anchoredPosition = new Vector2(arrowX, 0f);
            arrowRt.localEulerAngles = new Vector3(0f, 0f, 45f); // 45度菱形指针
            _pointerArrow = arrowObj.GetComponent<Image>();
            _pointerArrow.color = accentColor;

            // 中央实时数字
            int valFontSize = Mathf.RoundToInt(12f * CurrentDpiScale);
            _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxW - 6f * CurrentDpiScale, boxH);
            valRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLabels(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float halfH = RectTransform.sizeDelta.y * 0.5f;
            float w = RectTransform.sizeDelta.x;
            Color bgCol = theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.06f, 0.09f, 0.90f);
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;

            // 1. 顶部模式文字标牌盒 (如 "SURF", "ASL", "ORBIT")
            Vector2 topBoxSize = new Vector2(w, 18f * s);
            Vector2 topBoxPos = new Vector2(0f, halfH + 11f * s);
            _topModeBox = UIFactory.CreatePanel(transform, "Top_Mode_Box", topBoxSize, topBoxPos, bgCol);
            _topModeBg = _topModeBox.GetComponent<Image>();
            _topModeOutline = _topModeBox.AddComponent<Outline>();
            _topModeOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            _topModeOutline.effectDistance = new Vector2(1f * s, 1f * s);

            int topFontSize = Mathf.RoundToInt(9f * s);
            _topModeText = UIFactory.CreateText(_topModeBox.transform, "Top_Mode", Config.DisplayName, topFontSize, TextAnchor.MiddleCenter, theme != null ? (Color)theme.AccentSecondary : Color.cyan);
            RectTransform topRt = _topModeText.GetComponent<RectTransform>();
            topRt.sizeDelta = topBoxSize;
            topRt.anchoredPosition = Vector2.zero;
            _topModeText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 2. 底部单位文字标牌盒 (如 "m/s", "m")
            Vector2 btmBoxSize = new Vector2(w, 16f * s);
            Vector2 btmBoxPos = new Vector2(0f, -halfH - 10f * s);
            _bottomUnitBox = UIFactory.CreatePanel(transform, "Bottom_Unit_Box", btmBoxSize, btmBoxPos, bgCol);
            _bottomUnitBg = _bottomUnitBox.GetComponent<Image>();
            _bottomUnitOutline = _bottomUnitBox.AddComponent<Outline>();
            _bottomUnitOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            _bottomUnitOutline.effectDistance = new Vector2(1f * s, 1f * s);

            int btmFontSize = Mathf.RoundToInt(8.5f * s);
            string unit = string.IsNullOrEmpty(Config.UnitLabel) ? (Config.IsLeftOrientation ? "m/s" : "m") : Config.UnitLabel;
            _bottomUnitText = UIFactory.CreateText(_bottomUnitBox.transform, "Bottom_Unit", unit, btmFontSize, TextAnchor.MiddleCenter, theme != null ? (Color)theme.TextAccentColor : Color.gray);
            RectTransform btmRt = _bottomUnitText.GetComponent<RectTransform>();
            btmRt.sizeDelta = btmBoxSize;
            btmRt.anchoredPosition = Vector2.zero;
            _bottomUnitText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildDynamicTrendIndicator(ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float width = RectTransform.sizeDelta.x;
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            Color bgCol = theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.06f, 0.09f, 0.90f);

            // 侧边指示条挂载点 (速度带在左侧外缘，高度带在右侧外缘)
            float trendX = Config.IsLeftOrientation ? -(width * 0.5f + 7f * s) : (width * 0.5f + 7f * s);

            GameObject root = new GameObject("Trend_Indicator_Root", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _trendRoot = root.GetComponent<RectTransform>();
            _trendRoot.sizeDelta = new Vector2(24f * s, 210f * s);
            _trendRoot.anchoredPosition = new Vector2(trendX, 0f);

            // 1. 顶部固定三层胶囊标牌盒 (Top Tag Box: Title + Dynamic Value + Unit)
            // 采用航电紧凑型三段式数字窗口，物理隔离动态箭头，杜绝数字与箭头、标签溢出重叠
            Vector2 boxSize = new Vector2(24f * s, 32f * s);
            Vector2 boxPos = new Vector2(0f, 85f * s);

            _trendTagBox = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Tag_Box", boxSize, boxPos, bgCol);
            _trendTagBg = _trendTagBox.GetComponent<Image>();

            _trendTagOutline = _trendTagBox.AddComponent<Outline>();
            _trendTagOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            _trendTagOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 1.1 标牌标题 (ACC 或 V/S)
            string tag = Config.IsLeftOrientation ? "ACC" : "V/S";
            _trendTagText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Tag_Title", tag,
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.AccentSecondary : Color.cyan);
            RectTransform tagRt = _trendTagText.GetComponent<RectTransform>();
            tagRt.sizeDelta = new Vector2(boxSize.x, 9f * s);
            tagRt.anchoredPosition = new Vector2(0f, 9.5f * s);
            _trendTagText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 1.2 动态读数数值 (+5.0 / +921 / -150 / 0.0)
            _trendRateText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Rate_Val", "0.0",
                Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.AccentPrimary : Color.green);
            _trendRateText.fontStyle = FontStyle.Bold;
            RectTransform rateRt = _trendRateText.GetComponent<RectTransform>();
            rateRt.sizeDelta = new Vector2(boxSize.x, 10f * s);
            rateRt.anchoredPosition = new Vector2(0f, 0f);
            _trendRateText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 1.3 物理单位 (G 或 m/s)
            string unit = Config.IsLeftOrientation ? "G" : "m/s";
            _trendUnitText = UIFactory.CreateText(_trendTagBox.transform, "Trend_Unit", unit,
                Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.TextAccentColor : Color.gray);
            RectTransform unitRt = _trendUnitText.GetComponent<RectTransform>();
            unitRt.sizeDelta = new Vector2(boxSize.x, 8f * s);
            unitRt.anchoredPosition = new Vector2(0f, -9.5f * s);
            _trendUnitText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 2. 垂直基准轨道暗色遮光背景槽 (Smoked Glass Track Slot)
            // 完整覆盖滑轨与刻度区域，彻底杜绝刻度线与动态黄色光标裸露悬浮于游戏画面中
            Vector2 trackBgSize = new Vector2(20f * s, 134f * s);
            Vector2 trackBgPos = new Vector2(0f, -6f * s);
            _trendTrackBgObj = UIFactory.CreatePanel(_trendRoot.transform, "Trend_Track_Bg", trackBgSize, trackBgPos, bgCol);
            _trendTrackBg = _trendTrackBgObj.GetComponent<Image>();
            _trendTrackOutline = _trendTrackBgObj.AddComponent<Outline>();
            _trendTrackOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f);
            _trendTrackOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 2.1 垂直基准轨道线
            GameObject trackObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_Track",
                new Vector2(1.5f * s, 118f * s), Vector2.zero,
                new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f));
            _trendTrack = trackObj.GetComponent<Image>();

            // 2.2 中央 0 刻度基准线
            GameObject zeroObj = UIFactory.CreatePanel(_trendTrackBgObj.transform, "Trend_ZeroTick",
                new Vector2(8f * s, 1.5f * s), Vector2.zero,
                new Color(borderCol.r, borderCol.g, borderCol.b, 0.75f));
            _trendZeroTick = zeroObj.GetComponent<Image>();

            // 2.3 辅助刻度线 (±25, ±50)
            float[] tickOffsets = new float[] { 25f * s, 50f * s, -25f * s, -50f * s };
            foreach (float yOff in tickOffsets)
            {
                UIFactory.CreatePanel(_trendTrackBgObj.transform, $"Trend_Tick_{yOff:F0}",
                    new Vector2(5f * s, 1f * s), new Vector2(0f, yOff),
                    new Color(borderCol.r, borderCol.g, borderCol.b, 0.40f));
            }

            // 2.4 动态伸缩指示条 (从 0 刻度向上/下伸展)
            GameObject barObj = new GameObject("Trend_Bar", typeof(RectTransform), typeof(Image));
            barObj.transform.SetParent(_trendTrackBgObj.transform, false);
            _trendBarRt = barObj.GetComponent<RectTransform>();
            _trendBarRt.sizeDelta = new Vector2(3f * s, 0f);
            _trendBarRt.pivot = new Vector2(0.5f, 0f);
            _trendBarRt.anchoredPosition = Vector2.zero;
            _trendBarImg = barObj.GetComponent<Image>();
            _trendBarImg.color = theme != null ? (Color)theme.AccentPrimary : Color.green;

            // 2.5 动态箭头微标 (位于条状末端，45度菱形尖角)
            GameObject arrowObj = new GameObject("Trend_Arrow", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(_trendTrackBgObj.transform, false);
            _trendArrowRt = arrowObj.GetComponent<RectTransform>();
            _trendArrowRt.sizeDelta = new Vector2(6f * s, 6f * s);
            _trendArrowRt.anchoredPosition = Vector2.zero;
            _trendArrowRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _trendArrowImg = arrowObj.GetComponent<Image>();
            _trendArrowImg.color = _trendBarImg.color;
        }

        public static Action OnCycleSpeedModeAction;
        public static Action OnCycleAltitudeModeAction;

        private void OnBoxClicked()
        {
            if (Config.IsLeftOrientation || Config.NumericToken.Contains("SPD"))
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
            // 采样主驱动数值
            double currentVal = TelemetryTokenEngine.EvaluateNumeric(Config.NumericToken, telemetry);
            if (double.IsNaN(currentVal)) currentVal = 0.0;

            // 1. 更新中央数显框
            UpdateCenterReadout(currentVal);

            // 2. 更新模式与单位标签
            UpdateLabels(telemetry);

            // 3. 滚动刻度池物理计算
            UpdateRollingTape(currentVal);

            // 4. 真实 PFD 动态升降率 / 加速度动态指示条与箭头
            UpdateDynamicTrendIndicator(telemetry);
        }

        private void UpdateDynamicTrendIndicator(IFlightTelemetry telemetry)
        {
            if (_trendBarRt == null || telemetry == null) return;

            float s = CurrentDpiScale;
            float maxBarLength = 50f * s; // 轨道最大行程 50px，箭头最大 tip 53px，与上方标牌盒底沿 (69px) 保持 16px 绝对安全间隔
            float rateFraction = 0f;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color primaryCol = theme != null ? (Color)theme.AccentPrimary : Color.green;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.yellow;
            Color normalCol = theme != null ? (Color)theme.TextPrimaryColor : Color.white;

            if (Config.IsLeftOrientation || Config.NumericToken.Contains("SPD"))
            {
                // 速度带：加速度趋势向量 (Speed Trend Vector / ACC)
                float displayAccelG;
                if (telemetry.Throttle > 0.05f)
                {
                    // 动力推进行程：基于有效 TWR 计算真实加速度 G
                    displayAccelG = (float)(telemetry.TWR * telemetry.Throttle);
                    rateFraction = Mathf.Clamp(displayAccelG / 4.0f, -1f, 1f);
                }
                else
                {
                    // 滑行或再入大气阻力减速行程
                    displayAccelG = (float)(telemetry.GForce - 1.0);
                    rateFraction = Mathf.Clamp(displayAccelG * 0.35f, -1f, 1f);
                }

                if (_trendRateText != null)
                {
                    if (Mathf.Abs(displayAccelG) < 0.05f)
                    {
                        _trendRateText.text = "0.0";
                        _trendRateText.color = normalCol;
                    }
                    else if (displayAccelG > 0f)
                    {
                        _trendRateText.text = $"+{Mathf.Min(displayAccelG, 99.9f):F1}";
                        _trendRateText.color = primaryCol;
                    }
                    else
                    {
                        _trendRateText.text = $"-{Mathf.Min(Mathf.Abs(displayAccelG), 99.9f):F1}";
                        _trendRateText.color = warnCol;
                    }
                }

                if (_trendUnitText != null)
                {
                    _trendUnitText.text = "G";
                }
            }
            else
            {
                // 高度带：垂直升降率 (Vertical Speed Trend / V/S)
                double vs = telemetry.VerticalSpeed;
                if (double.IsNaN(vs)) vs = 0.0;

                if (Math.Abs(vs) > 0.01)
                {
                    rateFraction = Mathf.Clamp((float)(vs / 120.0), -1f, 1f);
                }
                else
                {
                    rateFraction = (telemetry.NormalizedVSI - 0.5f) * 2.0f;
                }

                if (_trendRateText != null)
                {
                    if (Math.Abs(vs) < 0.1)
                    {
                        _trendRateText.text = "0";
                        _trendRateText.color = normalCol;
                    }
                    else if (vs > 0.0)
                    {
                        if (vs >= 1000.0)
                            _trendRateText.text = $"+{vs / 1000.0:F1}k";
                        else
                            _trendRateText.text = $"+{vs:F0}";

                        _trendRateText.color = primaryCol;
                    }
                    else
                    {
                        double absVs = Math.Abs(vs);
                        if (absVs >= 1000.0)
                            _trendRateText.text = $"-{absVs / 1000.0:F1}k";
                        else
                            _trendRateText.text = $"-{absVs:F0}";

                        _trendRateText.color = warnCol;
                    }
                }

                if (_trendUnitText != null)
                {
                    _trendUnitText.text = "m/s";
                }
            }

            float dynamicLen = Mathf.Abs(rateFraction) * maxBarLength;
            bool isPositive = rateFraction >= 0f;
            Color activeColor = isPositive ? primaryCol : warnCol;

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

            if (_trendBarImg != null) _trendBarImg.color = activeColor;
            if (_trendArrowImg != null)
            {
                _trendArrowImg.color = activeColor;
                _trendArrowImg.gameObject.SetActive(dynamicLen > 2f * s);
            }
        }

        private void UpdateCenterReadout(double val)
        {
            if (Math.Abs(val) >= 100000.0)
            {
                _centerValueText.text = $"{val / 1000.0:F1}k";
            }
            else if (Math.Abs(val) >= 10000.0)
            {
                _centerValueText.text = $"{Math.Round(val):N0}";
            }
            else if (Math.Abs(val) >= 100.0)
            {
                _centerValueText.text = $"{Math.Round(val, 1):F1}";
            }
            else
            {
                _centerValueText.text = $"{val:F1}";
            }
        }

        private void UpdateLabels(IFlightTelemetry telemetry)
        {
            if (Config.IsLeftOrientation || Config.NumericToken.Contains("SPD"))
            {
                _topModeText.text = Config.NumericToken.Contains("SURF") ? "SURF" : (Config.NumericToken.Contains("OBT") ? "ORBIT" : "SPD");
            }
            else if (Config.NumericToken.Contains("ALT"))
            {
                _topModeText.text = Config.NumericToken.Contains("AGL") || Config.NumericToken.Contains("RADAR") ? "RADAR" : "ASL";
            }
        }

        private void UpdateRollingTape(double currentVal)
        {
            float step = Config.StepInterval > 0f ? Config.StepInterval : (Config.IsLeftOrientation ? 10f : 100f);
            float pixelsPerUnit = (28f * CurrentDpiScale) / step;
            float visibleHalfSpan = (_viewportRt.sizeDelta.y * 0.5f) / pixelsPerUnit;

            // 计算视口起始刻度 (向下对齐整刻度)
            double startTick = Math.Floor((currentVal - visibleHalfSpan) / step) * step;

            float lineBaseX = Config.IsLeftOrientation ? (32f * CurrentDpiScale) : (-32f * CurrentDpiScale);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                TickItem item = _tickPool[i];
                double tickVal = startTick + i * step;

                // 速度带通常不显示负刻度
                if (tickVal < 0.0 && Config.IsLeftOrientation)
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

                // 是否主刻度 (每隔一个或整5/整10)
                long tickIndex = (long)Math.Round(tickVal / step);
                bool isMajor = (tickIndex % 2 == 0);

                RectTransform lineRt = item.Line.GetComponent<RectTransform>();
                if (isMajor)
                {
                    lineRt.sizeDelta = new Vector2(15f * CurrentDpiScale, 2f * CurrentDpiScale);
                    item.Line.color = Color.white;
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
                    item.Line.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
                    item.Label.gameObject.SetActive(false);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (_bgImage != null)
                _bgImage.color = theme.FrameBgColor;

            Color borderCol = theme.FrameBorderColor;
            if (_bgOutline != null)
                _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.6f);

            Color accent = (Color)theme.AccentSecondary;
            if (_centerBoxOutline != null) _centerBoxOutline.effectColor = accent;
            if (_pointerArrow != null) _pointerArrow.color = accent;
            if (_centerValueText != null) _centerValueText.color = theme.TextPrimaryColor;
            if (_topModeBg != null) _topModeBg.color = theme.FrameBgColor;
            if (_topModeOutline != null)
                _topModeOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            if (_topModeText != null) _topModeText.color = theme.AccentSecondary;

            if (_bottomUnitBg != null) _bottomUnitBg.color = theme.FrameBgColor;
            if (_bottomUnitOutline != null)
                _bottomUnitOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            if (_bottomUnitText != null) _bottomUnitText.color = theme.TextAccentColor;

            if (_trendTagBg != null) _trendTagBg.color = theme.FrameBgColor;
            if (_trendTagOutline != null)
                _trendTagOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.45f);
            if (_trendTagText != null) _trendTagText.color = theme.AccentSecondary;
            if (_trendUnitText != null) _trendUnitText.color = theme.TextAccentColor;

            if (_trendTrackBg != null) _trendTrackBg.color = theme.FrameBgColor;
            if (_trendTrackOutline != null)
                _trendTrackOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f);
            if (_trendTrack != null) _trendTrack.color = new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f);
            if (_trendZeroTick != null) _trendZeroTick.color = new Color(borderCol.r, borderCol.g, borderCol.b, 0.75f);

            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                    _tickPool[i].Label.color = theme.TextPrimaryColor;
            }
        }
    }
}
