using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 姿态球顶部圆弧航向指示带 (Navball Heading Arc Ribbon - Set 2 / 图2)
    /// 紧密环绕姿态球上缘，具备平滑滚动的度数刻度、红南/蓝北罗盘主方位标识、
    /// 顶部气泡框 (Speech-bubble) 权威数显标牌以及翡翠绿反T型基准游标。
    /// 严格继承 BaseFlightWidget，所有视觉样式与数值全生命周期数据驱动。
    /// </summary>
    public class HeadingArcWidget : BaseFlightWidget
    {
        private const int MAX_VISIBLE_TICKS = 24;
        private const float ARC_RADIUS = 92f;
        private const float MAX_ANGULAR_SPAN = 55f; // 可见视口半角范围 (±55°)

        private struct HeadingTickUI
        {
            public GameObject Root;
            public RectTransform Rt;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
        }

        private readonly List<HeadingTickUI> _tickPool = new List<HeadingTickUI>();

        // 气泡框与游标
        private GameObject _speechBubbleRoot;
        private RectTransform _speechBubbleRt;
        private Image _bubbleBg;
        private Outline _bubbleOutline;
        private Image _bubblePointerTip;
        private Text _headingText;
        private Text _frameModeText;

        // 翡翠绿基准游标 (绿反T)
        private GameObject _lubberLineRoot;
        private Image _lubberBar;
        private Image _lubberStem;

        // 底层弧形暗色玻璃底带
        private GameObject _arcBandRoot;
        private readonly List<Image> _bandBgImages = new List<Image>();
        private readonly List<Image> _bandRimImages = new List<Image>();

        // 航向平滑阻尼动力学引擎 (EFIS Avionics Damping Filter)
        private float _targetHeading = 0f;
        private float _displayedHeading = 0f;
        private float _headingVelocity = 0f;
        private bool _isHeadingInitialized = false;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float arcRadius = ARC_RADIUS * s;

            // 根尺寸设定，包裹顶部弧度区域
            Vector2 widgetSize = new Vector2(arcRadius * 2.2f, arcRadius * 0.85f);
            RectTransform.sizeDelta = widgetSize;

            // 1. 构建弧形暗色玻璃背景带 (由微型弧片点阵构成平滑导轨)
            BuildArcBand(arcRadius, s, theme);

            // 2. 初始化刻度对象池
            BuildTickPool(arcRadius, s, theme);

            // 3. 构建顶部气泡框数显标牌 (Speech-Bubble Badge)
            BuildSpeechBubble(arcRadius, s, theme);

            // 4. 构建翡翠绿反T型基准游标 (Lubber Mark)
            BuildLubberMark(arcRadius, s, theme);

            ApplyTheme(theme);
        }

        private void BuildArcBand(float radius, float s, ThemeConfig theme)
        {
            _arcBandRoot = new GameObject("Arc_Band_Root", typeof(RectTransform));
            _arcBandRoot.transform.SetParent(transform, false);

            _bandBgImages.Clear();
            _bandRimImages.Clear();

            Color bandCol = theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.06f, 0.09f, 0.85f);
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : new Color(0.35f, 0.65f, 0.95f, 0.40f);

            // 沿着 ±55° 弧度构建平滑的暗色玻璃遮光底板与内/外边缘轮廓
            const int segCount = 24;
            float step = (MAX_ANGULAR_SPAN * 2f) / segCount;
            // 弧长步进并略微加宽，消除浮点旋转缝隙
            float arcSegW = ((2f * Mathf.PI * radius * (MAX_ANGULAR_SPAN * 2f / 360f)) / segCount) + 1.5f * s;
            float bandThickness = 22f * s; // 覆盖刻度线与数字显示区域

            for (int i = 0; i <= segCount; i++)
            {
                float ang = -MAX_ANGULAR_SPAN + (i * step);
                float rad = ang * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                // 1. 半透明暗色玻璃遮光弧板 (背衬刻度线与读数，保证强光背景下的高辨识度)
                Vector2 platePos = new Vector2(sin * (radius - 5f * s), cos * (radius - 5f * s));
                GameObject plate = UIFactory.CreatePanel(_arcBandRoot.transform, $"BandPlate_{i}",
                    new Vector2(arcSegW, bandThickness), platePos, bandCol);
                plate.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandBgImages.Add(plate.GetComponent<Image>());

                // 2. 外缘极细发光轮廓 (Outer Rim Line)
                Vector2 outerRimPos = new Vector2(sin * (radius + 6f * s), cos * (radius + 6f * s));
                GameObject outerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"OuterRim_{i}",
                    new Vector2(arcSegW, 1.2f * s), outerRimPos, new Color(borderCol.r, borderCol.g, borderCol.b, 0.40f));
                outerRim.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandRimImages.Add(outerRim.GetComponent<Image>());

                // 3. 内缘细弱辅助线 (Inner Rim Line)
                Vector2 innerRimPos = new Vector2(sin * (radius - 16f * s), cos * (radius - 16f * s));
                GameObject innerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"InnerRim_{i}",
                    new Vector2(arcSegW, 1.0f * s), innerRimPos, new Color(borderCol.r, borderCol.g, borderCol.b, 0.22f));
                innerRim.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandRimImages.Add(innerRim.GetComponent<Image>());
            }
        }

        private void BuildTickPool(float radius, float s, ThemeConfig theme)
        {
            _tickPool.Clear();
            Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;

            for (int i = 0; i < MAX_VISIBLE_TICKS; i++)
            {
                GameObject root = new GameObject($"TickNode_{i}", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                RectTransform rt = root.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(30f * s, 30f * s);

                // 刻度线 (沿法线辐射指向圆心)
                GameObject lineObj = new GameObject("Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(root.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(1.5f * s, 7f * s);
                lineRt.anchoredPosition = Vector2.zero;
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = theme != null ? (Color)theme.TextAccentColor : Color.white;

                // 刻度数字 / 罗盘主方位
                int fontSize = Mathf.Max(7, Mathf.RoundToInt(8.5f * s));
                Text lbl = UIFactory.CreateText(root.transform, "Label", "", fontSize, TextAnchor.MiddleCenter,
                    theme != null ? (Color)theme.TextPrimaryColor : Color.white);
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = new Vector2(28f * s, 14f * s);
                lblRt.anchoredPosition = new Vector2(0f, -10f * s);

                _tickPool.Add(new HeadingTickUI
                {
                    Root = root,
                    Rt = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = lbl,
                    LabelRt = lblRt
                });

                root.SetActive(false);
            }
        }

        private void BuildSpeechBubble(float radius, float s, ThemeConfig theme)
        {
            // 气泡框主体：位于弧形正顶点上方 (radius + 15px)
            Vector2 boxSize = new Vector2(56f * s, 20f * s);
            Vector2 bubblePos = new Vector2(0f, radius + 15f * s);

            _speechBubbleRoot = new GameObject("Heading_SpeechBubble", typeof(RectTransform), typeof(Image), typeof(Button));
            _speechBubbleRoot.transform.SetParent(transform, false);
            _speechBubbleRt = _speechBubbleRoot.GetComponent<RectTransform>();
            _speechBubbleRt.sizeDelta = boxSize;
            _speechBubbleRt.anchoredPosition = bubblePos;

            _bubbleBg = _speechBubbleRoot.GetComponent<Image>();
            _bubbleBg.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);

            _bubbleOutline = _speechBubbleRoot.AddComponent<Outline>();
            Color accentSecondary = theme != null ? (Color)theme.AccentSecondary : new Color(0.35f, 0.65f, 0.95f, 1f);
            _bubbleOutline.effectColor = accentSecondary;
            _bubbleOutline.effectDistance = new Vector2(1f * s, 1f * s);

            Button bubbleBtn = _speechBubbleRoot.GetComponent<Button>();
            bubbleBtn.onClick.AddListener(OnBubbleClicked);

            // 气泡框向下尖角指针 (Speech Bubble Pointer Stem)
            GameObject tipObj = new GameObject("Bubble_PointerTip", typeof(RectTransform), typeof(Image));
            tipObj.transform.SetParent(_speechBubbleRoot.transform, false);
            RectTransform tipRt = tipObj.GetComponent<RectTransform>();
            tipRt.sizeDelta = new Vector2(6f * s, 6f * s);
            tipRt.anchoredPosition = new Vector2(0f, -boxSize.y * 0.5f + 0.5f * s);
            tipRt.localEulerAngles = new Vector3(0f, 0f, 45f); // 45度菱形形成向下尖角
            _bubblePointerTip = tipObj.GetComponent<Image>();
            _bubblePointerTip.color = _bubbleBg.color;

            Outline tipOutline = tipObj.AddComponent<Outline>();
            tipOutline.effectColor = _bubbleOutline.effectColor;
            tipOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 气泡框内部数显读数 (189°) 居左中排版
            int fontSize = Mathf.RoundToInt(13f * s);
            _headingText = UIFactory.CreateText(_speechBubbleRoot.transform, "Heading_Value", "000°", fontSize,
                TextAnchor.MiddleRight, theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.02f, 0f);
            textRt.anchorMax = new Vector2(0.66f, 1f);
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;

            // 模式角标 (如 SRF / OBT) 居右中排版
            _frameModeText = UIFactory.CreateText(_speechBubbleRoot.transform, "Mode_Tag", "SRF",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft,
                theme != null ? (Color)theme.TextAccentColor : Color.gray);
            _frameModeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform modeRt = _frameModeText.GetComponent<RectTransform>();
            modeRt.anchorMin = new Vector2(0.68f, 0f);
            modeRt.anchorMax = new Vector2(0.98f, 1f);
            modeRt.sizeDelta = Vector2.zero;
            modeRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLubberMark(float radius, float s, ThemeConfig theme)
        {
            // 翡翠绿基准游标 (反 T 型)：横杠贴在弧线上，立柱朝下指示姿态球中心
            Vector2 lubberPos = new Vector2(0f, radius);

            _lubberLineRoot = new GameObject("Lubber_Line_Root", typeof(RectTransform));
            _lubberLineRoot.transform.SetParent(transform, false);
            RectTransform lubRt = _lubberLineRoot.GetComponent<RectTransform>();
            lubRt.sizeDelta = new Vector2(16f * s, 12f * s);
            lubRt.anchoredPosition = lubberPos;

            Color lubberColor = theme != null ? (Color)theme.AccentPrimary : Color.green;

            // 顶部横杠 (Green Bar)
            GameObject barObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Bar",
                new Vector2(14f * s, 2f * s), new Vector2(0f, 1f * s), lubberColor);
            _lubberBar = barObj.GetComponent<Image>();

            // 竖向立柱 (Green Stem)
            GameObject stemObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Stem",
                new Vector2(2f * s, 6f * s), new Vector2(0f, -3f * s), lubberColor);
            _lubberStem = stemObj.GetComponent<Image>();
        }

        public static Action OnCycleHeadingModeAction;

        private void OnBubbleClicked()
        {
            FlightTelemetryContext.Current?.CycleSpeedMode();
            OnCycleHeadingModeAction?.Invoke();
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            var hook = NavBallHookService.Provider;
            float rawHeading = (hook != null && hook.HasStockNavBall) ? hook.HeadingAngle : (float)telemetry.Heading;
            if (float.IsNaN(rawHeading)) rawHeading = 0f;

            _targetHeading = (rawHeading % 360f + 360f) % 360f;

            if (!_isHeadingInitialized)
            {
                _displayedHeading = _targetHeading;
                _isHeadingInitialized = true;
                UpdateRotatingCompassRose(_displayedHeading);
                UpdateBubbleHeadingText(_displayedHeading);
            }

            // 更新参考系模式角标 (SRF / OBT / TGT)
            if (_frameModeText != null)
            {
                string frame = telemetry.SpeedModeName ?? hook?.FrameName ?? "SRF";
                if (string.IsNullOrEmpty(frame)) frame = "SRF";
                string frameTag = "SRF";
                if (frame.StartsWith("ORB", StringComparison.OrdinalIgnoreCase)) frameTag = "OBT";
                else if (frame.StartsWith("TG", StringComparison.OrdinalIgnoreCase) || frame.StartsWith("TAR", StringComparison.OrdinalIgnoreCase)) frameTag = "TGT";
                _frameModeText.text = frameTag;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!_isHeadingInitialized) return;

            float dt = Time.deltaTime;
            if (dt <= 0.0001f)
            {
                // 离线渲染 / 暂停模式下即时同步
                _displayedHeading = _targetHeading;
                UpdateRotatingCompassRose(_displayedHeading);
                UpdateBubbleHeadingText(_displayedHeading);
                return;
            }

            // 超大角度突变时（如载具切换或模式重置 > 120°），直接瞬移，防止 360 度失真绕转
            float angleDiff = Mathf.DeltaAngle(_displayedHeading, _targetHeading);
            if (Mathf.Abs(angleDiff) > 120f)
            {
                _displayedHeading = _targetHeading;
                _headingVelocity = 0f;
            }
            else
            {
                // 采用航空级 SmoothDampAngle 阻尼平滑，0.09s 响应时间，兼具极速跟随与高档滑动质感
                _displayedHeading = Mathf.SmoothDampAngle(_displayedHeading, _targetHeading, ref _headingVelocity, 0.09f, 900f, dt);
                _displayedHeading = (_displayedHeading % 360f + 360f) % 360f;
            }

            // 每帧驱动刻度弧带平滑滚动与气泡读数刷新
            UpdateRotatingCompassRose(_displayedHeading);
            UpdateBubbleHeadingText(_displayedHeading);
        }

        private void UpdateBubbleHeadingText(float heading)
        {
            if (_headingText != null)
            {
                int degInt = Mathf.RoundToInt(heading) % 360;
                if (degInt < 0) degInt += 360;
                _headingText.text = $"{degInt:D3}°";
            }
        }

        private void UpdateRotatingCompassRose(float currentHeading)
        {
            float s = CurrentDpiScale;
            float radius = ARC_RADIUS * s;
            float yCenterOffset = 0f;

            // 查找视口内的刻度步进 (每 5° 一个刻度)
            int centerTickDeg = Mathf.RoundToInt(currentHeading / 5f) * 5;
            int tickIdx = 0;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color textCol = theme != null ? (Color)theme.TextPrimaryColor : Color.white;
            Color accentSec = theme != null ? (Color)theme.AccentSecondary : Color.cyan;
            Color warnCol = theme != null ? (Color)theme.WarningColor : Color.red;
            Color subTickCol = theme != null ? (Color)theme.TextAccentColor : Color.gray;

            for (int offsetDeg = -50; offsetDeg <= 50; offsetDeg += 5)
            {
                if (tickIdx >= _tickPool.Count) break;

                int tickDeg = centerTickDeg + offsetDeg;
                int normalizedDeg = (tickDeg % 360 + 360) % 360;

                // 计算相对于航向顶点的连续角位移
                float deltaAngle = Mathf.DeltaAngle(currentHeading, tickDeg);
                if (Mathf.Abs(deltaAngle) > MAX_ANGULAR_SPAN) continue;

                var item = _tickPool[tickIdx];
                item.Root.SetActive(true);

                // 极坐标变换到圆弧切线 (连续浮点亚像素坐标)
                float rad = deltaAngle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius - yCenterOffset);
                item.Rt.anchoredPosition = pos;
                item.Rt.localEulerAngles = new Vector3(0f, 0f, -deltaAngle);

                // 边缘平滑渐隐 (在 45°~55° 区间渐变透明，消除刻度突然出现/消失的生硬感)
                float edgeAlpha = Mathf.Clamp01((MAX_ANGULAR_SPAN - Mathf.Abs(deltaAngle)) / 10f);

                bool isMajor = (normalizedDeg % 10 == 0);
                bool isCardinal = (normalizedDeg % 90 == 0);

                if (isCardinal)
                {
                    // 主方位标识：S 标红 (图2特征), N 标青/蓝, E/W 标白
                    string cardStr = "N";
                    Color cardCol = accentSec;

                    switch (normalizedDeg)
                    {
                        case 0:
                            cardStr = "N";
                            cardCol = accentSec; // 北极亮蓝
                            break;
                        case 90:
                            cardStr = "E";
                            cardCol = textCol;
                            break;
                        case 180:
                            cardStr = "S";
                            cardCol = new Color(1f, 0.28f, 0.28f, 1f); // 南向醒目红标 (图2标志性元素)
                            break;
                        case 270:
                            cardStr = "W";
                            cardCol = textCol;
                            break;
                    }

                    Color finalCardCol = cardCol;
                    finalCardCol.a *= edgeAlpha;
                    item.LineRt.sizeDelta = new Vector2(2f * s, 8f * s);
                    item.Line.color = finalCardCol;

                    item.Label.text = cardStr;
                    item.Label.color = finalCardCol;
                    item.Label.fontSize = Mathf.RoundToInt(10.5f * s);
                    item.Label.gameObject.SetActive(true);
                }
                else if (isMajor)
                {
                    // 10 度大刻度线与 30 度角数显
                    Color finalLineCol = textCol;
                    finalLineCol.a *= edgeAlpha;
                    item.LineRt.sizeDelta = new Vector2(1.5f * s, 6.5f * s);
                    item.Line.color = finalLineCol;

                    if (normalizedDeg % 30 == 0)
                    {
                        Color finalLblCol = subTickCol;
                        finalLblCol.a *= edgeAlpha;
                        item.Label.text = $"{normalizedDeg:D3}";
                        item.Label.color = finalLblCol;
                        item.Label.fontSize = Mathf.RoundToInt(7.5f * s);
                        item.Label.gameObject.SetActive(true);
                    }
                    else
                    {
                        item.Label.gameObject.SetActive(false);
                    }
                }
                else
                {
                    // 5 度小刻度线
                    Color finalLineCol = new Color(subTickCol.r, subTickCol.g, subTickCol.b, 0.55f * edgeAlpha);
                    item.LineRt.sizeDelta = new Vector2(1f * s, 4f * s);
                    item.Line.color = finalLineCol;
                    item.Label.gameObject.SetActive(false);
                }

                tickIdx++;
            }

            // 隐藏剩余未使用的刻度对象
            for (int i = tickIdx; i < _tickPool.Count; i++)
            {
                _tickPool[i].Root.SetActive(false);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            if (_bubbleBg != null) _bubbleBg.color = theme.FrameBgColor;
            if (_bubbleOutline != null) _bubbleOutline.effectColor = theme.AccentSecondary;
            if (_bubblePointerTip != null) _bubblePointerTip.color = theme.FrameBgColor;
            if (_headingText != null) _headingText.color = theme.TextPrimaryColor;
            if (_frameModeText != null) _frameModeText.color = theme.TextAccentColor;

            if (_lubberBar != null) _lubberBar.color = theme.AccentPrimary;
            if (_lubberStem != null) _lubberStem.color = theme.AccentPrimary;

            if (_bandBgImages != null && _bandBgImages.Count > 0)
            {
                Color bandCol = theme.FrameBgColor;
                for (int i = 0; i < _bandBgImages.Count; i++)
                {
                    if (_bandBgImages[i] != null) _bandBgImages[i].color = bandCol;
                }
            }

            if (_bandRimImages != null && _bandRimImages.Count > 0)
            {
                Color borderCol = theme.FrameBorderColor;
                for (int i = 0; i < _bandRimImages.Count; i++)
                {
                    if (_bandRimImages[i] != null)
                        _bandRimImages[i].color = new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f);
                }
            }
        }
    }
}
