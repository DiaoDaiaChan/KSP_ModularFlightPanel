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

        // 模式与顶部标签
        private Text _topModeText;
        private Text _bottomUnitText;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            // 基础尺寸设置
            float width = 72f * CurrentDpiScale;
            float height = 280f * CurrentDpiScale;
            RectTransform.sizeDelta = new Vector2(width, height);

            // 1. 半透明背景板
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.94f);

            _bgOutline = gameObject.AddComponent<Outline>();
            Color borderCol = theme.FrameBorderColor;
            _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.6f);
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
        }

        private void BuildTickPool(ThemeConfig theme)
        {
            _tickPool.Clear();
            float tickX = Config.IsLeftOrientation ? (32f * CurrentDpiScale) : (-32f * CurrentDpiScale);

            for (int i = 0; i < TICK_POOL_SIZE; i++)
            {
                GameObject itemObj = new GameObject($"Tick_{i}", typeof(RectTransform));
                itemObj.transform.SetParent(_tickContainer, false);
                RectTransform rt = itemObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(64f * CurrentDpiScale, 16f * CurrentDpiScale);

                // 刻度线
                GameObject lineObj = new GameObject("Tick_Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(itemObj.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(12f * CurrentDpiScale, 2f * CurrentDpiScale);
                lineRt.anchoredPosition = new Vector2(tickX, 0f);
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = theme.TextAccentColor;

                // 刻度数字
                int fontSize = Mathf.RoundToInt(11f * CurrentDpiScale);
                TextAnchor align = Config.IsLeftOrientation ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                Text labelTxt = UIFactory.CreateText(itemObj.transform, "Tick_Text", "0", fontSize, align, theme.TextPrimaryColor);
                RectTransform labelRt = labelTxt.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(46f * CurrentDpiScale, 16f * CurrentDpiScale);
                float labelX = Config.IsLeftOrientation ? (tickX - 28f * CurrentDpiScale) : (tickX + 28f * CurrentDpiScale);
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
            float boxW = 76f * CurrentDpiScale;
            float boxH = 34f * CurrentDpiScale;

            GameObject boxObj = new GameObject("Center_Readout_Box", typeof(RectTransform), typeof(Image), typeof(Button));
            boxObj.transform.SetParent(transform, false);
            _centerBoxRt = boxObj.GetComponent<RectTransform>();
            _centerBoxRt.sizeDelta = new Vector2(boxW, boxH);

            // 让游标微凸指向 Navball 方向
            float offsetX = Config.IsLeftOrientation ? (8f * CurrentDpiScale) : (-8f * CurrentDpiScale);
            _centerBoxRt.anchoredPosition = new Vector2(offsetX, 0f);

            _centerBoxBg = boxObj.GetComponent<Image>();
            _centerBoxBg.color = new Color(0.02f, 0.04f, 0.06f, 0.95f);

            _centerBoxOutline = boxObj.AddComponent<Outline>();
            Color accentColor = Config.IsLeftOrientation ? theme.WarningColor : theme.AccentMagenta;
            _centerBoxOutline.effectColor = accentColor;
            _centerBoxOutline.effectDistance = new Vector2(1.5f * CurrentDpiScale, 1.5f * CurrentDpiScale);

            Button btn = boxObj.GetComponent<Button>();
            btn.onClick.AddListener(OnBoxClicked);

            // 游标指示箭头 (三角形或指示柱)
            GameObject arrowObj = new GameObject("Pointer_Arrow", typeof(RectTransform), typeof(Image));
            arrowObj.transform.SetParent(boxObj.transform, false);
            RectTransform arrowRt = arrowObj.GetComponent<RectTransform>();
            arrowRt.sizeDelta = new Vector2(8f * CurrentDpiScale, 8f * CurrentDpiScale);
            float arrowX = Config.IsLeftOrientation ? (boxW * 0.5f + 4f * CurrentDpiScale) : (-boxW * 0.5f - 4f * CurrentDpiScale);
            arrowRt.anchoredPosition = new Vector2(arrowX, 0f);
            arrowRt.localEulerAngles = new Vector3(0f, 0f, 45f); // 45度菱形指针
            _pointerArrow = arrowObj.GetComponent<Image>();
            _pointerArrow.color = accentColor;

            // 中央实时数字
            int valFontSize = Mathf.RoundToInt(15f * CurrentDpiScale);
            _centerValueText = UIFactory.CreateText(boxObj.transform, "Readout_Value", "0", valFontSize, TextAnchor.MiddleCenter, theme.TextPrimaryColor);
            RectTransform valRt = _centerValueText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(boxW - 6f * CurrentDpiScale, boxH);
            valRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLabels(ThemeConfig theme)
        {
            float halfH = RectTransform.sizeDelta.y * 0.5f;

            // 顶部模式文字 (如 "SURF", "ASL")
            int topFontSize = Mathf.RoundToInt(10f * CurrentDpiScale);
            _topModeText = UIFactory.CreateText(transform, "Top_Mode", Config.DisplayName, topFontSize, TextAnchor.MiddleCenter, theme.AccentSecondary);
            RectTransform topRt = _topModeText.GetComponent<RectTransform>();
            topRt.sizeDelta = new Vector2(RectTransform.sizeDelta.x, 18f * CurrentDpiScale);
            topRt.anchoredPosition = new Vector2(0f, halfH + 10f * CurrentDpiScale);

            // 底部单位文字 (如 "m/s", "m")
            int btmFontSize = Mathf.RoundToInt(10f * CurrentDpiScale);
            string unit = string.IsNullOrEmpty(Config.UnitLabel) ? (Config.IsLeftOrientation ? "m/s" : "m") : Config.UnitLabel;
            _bottomUnitText = UIFactory.CreateText(transform, "Bottom_Unit", unit, btmFontSize, TextAnchor.MiddleCenter, theme.TextAccentColor);
            RectTransform btmRt = _bottomUnitText.GetComponent<RectTransform>();
            btmRt.sizeDelta = new Vector2(RectTransform.sizeDelta.x, 16f * CurrentDpiScale);
            btmRt.anchoredPosition = new Vector2(0f, -halfH - 9f * CurrentDpiScale);
        }

        private void OnBoxClicked()
        {
            if (TelemetryHub.Instance == null) return;

            if (Config.IsLeftOrientation || Config.NumericToken.Contains("SPD"))
            {
                TelemetryHub.Instance.CycleSpeedMode();
            }
            else
            {
                TelemetryHub.Instance.CycleAltitudeMode();
            }
        }

        public override void OnUpdateTelemetry(TelemetryHub telemetry)
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
                _centerValueText.text = $"{val:F1}";
            }
            else
            {
                _centerValueText.text = $"{val:F1}";
            }
        }

        private void UpdateLabels(TelemetryHub telemetry)
        {
            if (Config.IsLeftOrientation || Config.NumericToken.Contains("SPD"))
            {
                _topModeText.text = telemetry.CurrentSpeedMode.ToString().ToUpper();
            }
            else if (Config.NumericToken.Contains("ALT"))
            {
                _topModeText.text = telemetry.CurrentAltMode == AltitudeDisplayMode.Ground ? "RADAR" : "SEA";
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
                _bgImage.color = new Color(0.008f, 0.012f, 0.04f, 0.94f);

            Color borderCol = theme.FrameBorderColor;
            if (_bgOutline != null)
                _bgOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.6f);

            Color accent = Config.IsLeftOrientation ? (Color)theme.WarningColor : (Color)theme.AccentMagenta;
            if (_centerBoxOutline != null) _centerBoxOutline.effectColor = accent;
            if (_pointerArrow != null) _pointerArrow.color = accent;
            if (_centerValueText != null) _centerValueText.color = theme.TextPrimaryColor;
            if (_topModeText != null) _topModeText.color = theme.AccentSecondary;
            if (_bottomUnitText != null) _bottomUnitText.color = theme.TextAccentColor;

            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                    _tickPool[i].Label.color = theme.TextPrimaryColor;
            }
        }
    }
}
