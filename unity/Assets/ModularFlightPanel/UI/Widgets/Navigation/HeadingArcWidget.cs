using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 姿态球顶部圆弧航向指示带 (Navball Heading Arc Ribbon - Set 2 / 图2)
    /// 紧密环绕姿态球上缘，具备平滑滚动的度数刻度、红南/蓝北罗盘主方位标识、
    /// 顶部气泡框 (Speech-bubble) 权威数显标牌以及翡翠绿反T型基准游标。
    /// 严格继承 BaseFlightWidget，所有视觉样式与数值全生命周期数据驱动。
    /// </summary>
    [FlightWidget("heading_arc", "heading", "compass_arc", Category = WidgetCategory.Navigation, DisplayName = "PFD 航向指示标尺弧", Description = "主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。", DefaultWidgetId = "core.heading_arc", DefaultX = 0f, DefaultY = 120f, IsSingleton = true, ExactIds = new[] { "core.heading_arc" })]
    public class HeadingArcWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

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
            public int CurrentDeg;
            public float LastAlpha;
            public Color BaseColor;
            public Color BaseLineColor;
        }

        private readonly List<HeadingTickUI> _tickPool = new List<HeadingTickUI>();

        // 气泡框与游标
        private GameObject _speechBubbleRoot;
        private RectTransform _speechBubbleRt;
        private Image _bubbleBg;
        private Outline _bubbleOutline;
        private Image _bubblePointerTip;
        private Outline _bubblePointerOutline;
        private Text _headingText;
        private Button _bubbleBtn;

        // 翡翠绿基准游标 (绿反T)
        private GameObject _lubberLineRoot;
        private Image _lubberBar;
        private Image _lubberStem;

        // 底层弧形暗色玻璃底带
        private GameObject _arcBandRoot;
        private readonly List<Image> _bandBgImages = new List<Image>();
        private readonly List<Image> _bandRimImages = new List<Image>();

        // 通配符通道与配置
        private string _valueToken = "{HDG}";

        // 航向平滑阻尼动力学引擎 (EFIS Avionics Damping Filter)
        private float _targetHeading = 0f;
        private float _displayedHeading = 0f;
        private float _headingVelocity = 0f;
        private bool _isHeadingInitialized = false;
        private float _lastRenderedHeading = -999f;
        private int _lastBubbleDeg = -1;

        public static Action OnCycleHeadingModeAction;
        public static Action OnToggleReferenceFrameWindowAction;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            ApplyCanvasIsolation(true);

            float s = CurrentDpiScale;
            float arcRadius = ARC_RADIUS * s;

            // 根尺寸设定，包裹顶部弧度区域
            Vector2 widgetSize = new Vector2(arcRadius * 2.2f, arcRadius * 0.85f);
            RectTransform.sizeDelta = widgetSize;

            ParseCustomTemplate(config);

            // 1. 构建弧形暗色玻璃背景带
            BuildArcBand(arcRadius, s, theme);

            // 2. 初始化刻度对象池
            BuildTickPool(arcRadius, s, theme);

            // 3. 构建顶部气泡框数显标牌
            BuildSpeechBubble(arcRadius, s, theme);

            // 4. 构建翡翠绿反T型基准游标
            BuildLubberMark(arcRadius, s, theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config != null && !string.IsNullOrEmpty(config.NumericToken))
            {
                _valueToken = config.NumericToken;
            }

            if (string.IsNullOrEmpty(config?.CustomTemplate)) return;

            var pairs = config.CustomTemplate.Split(';');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                if (k == "VAL" || k == "VALUE" || k == "TOKEN" || k == "HDG")
                {
                    _valueToken = v;
                }
                else if (k == "MODE" || k == "FRAME")
                {
                    // Reference frame is now handled by ReferenceFrameWidget
                }
            }
        }

        private void BuildArcBand(float radius, float s, ThemeConfig theme)
        {
            _arcBandRoot = new GameObject("Arc_Band_Root", typeof(RectTransform));
            _arcBandRoot.transform.SetParent(transform, false);

            _bandBgImages.Clear();
            _bandRimImages.Clear();

            Color bandCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);

            const int segCount = 24;
            float step = (MAX_ANGULAR_SPAN * 2f) / segCount;
            float arcSegW = ((2f * Mathf.PI * radius * (MAX_ANGULAR_SPAN * 2f / 360f)) / segCount) + 1.5f * s;
            float bandThickness = 22f * s;

            for (int i = 0; i <= segCount; i++)
            {
                float ang = -MAX_ANGULAR_SPAN + (i * step);
                float rad = ang * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                // 1. 半透明暗色玻璃遮光弧板
                Vector2 platePos = new Vector2(sin * (radius - 5f * s), cos * (radius - 5f * s));
                GameObject plate = UIFactory.CreatePanel(_arcBandRoot.transform, $"BandPlate_{i}",
                    new Vector2(arcSegW, bandThickness), platePos, bandCol);
                plate.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandBgImages.Add(plate.GetComponent<Image>());

                // 2. 外缘极细发光轮廓
                Vector2 outerRimPos = new Vector2(sin * (radius + 6f * s), cos * (radius + 6f * s));
                GameObject outerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"OuterRim_{i}",
                    new Vector2(arcSegW, 1.2f * s), outerRimPos, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
                outerRim.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandRimImages.Add(outerRim.GetComponent<Image>());

                // 3. 内缘细弱辅助线
                Vector2 innerRimPos = new Vector2(sin * (radius - 16f * s), cos * (radius - 16f * s));
                GameObject innerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"InnerRim_{i}",
                    new Vector2(arcSegW, 1.0f * s), innerRimPos, WidgetStyleManager.Weighted(borderCol, LineWeight.Subtle));
                innerRim.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                _bandRimImages.Add(innerRim.GetComponent<Image>());
            }
        }

        private void BuildTickPool(float radius, float s, ThemeConfig theme)
        {
            _tickPool.Clear();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int i = 0; i < MAX_VISIBLE_TICKS; i++)
            {
                GameObject root = new GameObject($"TickNode_{i}", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                RectTransform rt = root.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(30f * s, 30f * s);

                // 刻度线
                GameObject lineObj = new GameObject("Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(root.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(1.5f * s, 7f * s);
                lineRt.anchoredPosition = Vector2.zero;
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

                // 刻度数字 / 罗盘主方位
                int fontSize = Mathf.Max(8, Mathf.RoundToInt(9.5f * s));
                Text lbl = UIFactory.CreateText(root.transform, "Label", "", fontSize, TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme), null, addShadow: false);
                lbl.fontStyle = FontStyle.Bold;
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = new Vector2(32f * s, 16f * s);
                lblRt.anchoredPosition = new Vector2(0f, -10f * s);

                _tickPool.Add(new HeadingTickUI
                {
                    Root = root,
                    Rt = rt,
                    Line = lineImg,
                    LineRt = lineRt,
                    Label = lbl,
                    LabelRt = lblRt,
                    CurrentDeg = -999,
                    LastAlpha = -1f,
                    BaseColor = Color.clear,
                    BaseLineColor = style.GetTextColor(TextStyleRole.PrimaryValue, theme)
                });

                root.SetActive(false);
            }
        }

        private void BuildSpeechBubble(float radius, float s, ThemeConfig theme)
        {
            Vector2 boxSize = new Vector2(50f * s, 20f * s);
            Vector2 bubblePos = new Vector2(0f, radius + 15f * s);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _speechBubbleRoot = new GameObject("Heading_SpeechBubble", typeof(RectTransform), typeof(Image), typeof(Button));
            _speechBubbleRoot.transform.SetParent(transform, false);
            _speechBubbleRt = _speechBubbleRoot.GetComponent<RectTransform>();
            _speechBubbleRt.sizeDelta = boxSize;
            _speechBubbleRt.anchoredPosition = bubblePos;

            _bubbleBg = _speechBubbleRoot.GetComponent<Image>();
            _bubbleBg.color = Color.clear;

            _bubbleOutline = _speechBubbleRoot.AddComponent<Outline>();
            _bubbleOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, theme);

            _bubbleBtn = _speechBubbleRoot.GetComponent<Button>();
            if (_bubbleBtn != null)
            {
                _bubbleBtn.transition = Selectable.Transition.None;
            }

            var clickHandler = _speechBubbleRoot.AddComponent<HeadingBubblePointerHandler>();
            clickHandler.OnLeftClick = OnBubbleClicked;
            clickHandler.OnRightClick = () =>
            {
                if (OnToggleReferenceFrameWindowAction != null)
                    OnToggleReferenceFrameWindowAction.Invoke();
                else
                    OnBubbleClicked();
            };

            _speechBubbleRoot.SetTooltip(
                I18n.Tr("WIDGET_NAME_CORE_HEADING_ARC", "PFD 航向指示标尺弧"),
                I18n.Tr("LIB_DESC_HEADING_ARC", "主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。")
            );

            // 气泡框向下尖角指针
            GameObject tipObj = new GameObject("Bubble_Pointer_Tip", typeof(RectTransform), typeof(Image));
            tipObj.transform.SetParent(_speechBubbleRoot.transform, false);
            RectTransform tipRt = tipObj.GetComponent<RectTransform>();
            tipRt.sizeDelta = new Vector2(7f * s, 7f * s);
            tipRt.anchoredPosition = new Vector2(0f, -boxSize.y * 0.5f + 0.5f * s);
            tipRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            _bubblePointerTip = tipObj.GetComponent<Image>();
            _bubblePointerTip.color = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);

            _bubblePointerOutline = tipObj.AddComponent<Outline>();
            _bubblePointerOutline.effectColor = style.GetCardBorderColor(CardStyleRole.Emphasized, theme);
            _bubblePointerOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 气泡框内部数显读数 (189°) - 纯净居中航向角
            int fontSize = Mathf.RoundToInt(12.5f * s);
            _headingText = UIFactory.CreateText(_speechBubbleRoot.transform, "Heading_Value", "000°", fontSize,
                TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _headingText.fontStyle = FontStyle.Bold;
            RectTransform textRt = _headingText.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
        }

        private void BuildLubberMark(float radius, float s, ThemeConfig theme)
        {
            Vector2 lubberPos = new Vector2(0f, radius);

            _lubberLineRoot = new GameObject("Lubber_Line_Root", typeof(RectTransform));
            _lubberLineRoot.transform.SetParent(transform, false);
            RectTransform lubRt = _lubberLineRoot.GetComponent<RectTransform>();
            lubRt.sizeDelta = new Vector2(16f * s, 12f * s);
            lubRt.anchoredPosition = lubberPos;

            Color lubberColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            // 顶部横杠
            GameObject barObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Bar",
                new Vector2(14f * s, 2f * s), new Vector2(0f, 1f * s), lubberColor);
            _lubberBar = barObj.GetComponent<Image>();

            // 竖向立柱
            GameObject stemObj = UIFactory.CreatePanel(_lubberLineRoot.transform, "Lubber_Stem",
                new Vector2(2f * s, 6f * s), new Vector2(0f, -3f * s), lubberColor);
            _lubberStem = stemObj.GetComponent<Image>();
        }

        private void OnBubbleClicked()
        {
            FlightTelemetryContext.Current?.CycleSpeedMode();
            OnCycleHeadingModeAction?.Invoke();
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            double evalHdg = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
            float rawHeading;
            if (!double.IsNaN(evalHdg))
            {
                rawHeading = (float)evalHdg;
            }
            else
            {
                var hook = NavBallHookService.Provider;
                rawHeading = (hook != null && hook.HasStockNavBall) ? hook.HeadingAngle : (float)telemetry.Heading;
            }
            if (float.IsNaN(rawHeading)) rawHeading = 0f;

            _targetHeading = (rawHeading % 360f + 360f) % 360f;

            if (!_isHeadingInitialized)
            {
                _displayedHeading = _targetHeading;
                _isHeadingInitialized = true;
                UpdateRotatingCompassRose(_displayedHeading);
                UpdateBubbleHeadingText(_displayedHeading);
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!_isHeadingInitialized) return;

            float dt = Time.deltaTime;
            if (dt <= 0.0001f)
            {
                _displayedHeading = _targetHeading;
                UpdateRotatingCompassRose(_displayedHeading, true);
                UpdateBubbleHeadingText(_displayedHeading);
                return;
            }

            float angleDiff = Mathf.DeltaAngle(_displayedHeading, _targetHeading);
            if (Mathf.Abs(angleDiff) > 120f)
            {
                _displayedHeading = _targetHeading;
                _headingVelocity = 0f;
            }
            else
            {
                _displayedHeading = Mathf.SmoothDampAngle(_displayedHeading, _targetHeading, ref _headingVelocity, 0.09f, 900f, dt);
                _displayedHeading = (_displayedHeading % 360f + 360f) % 360f;
            }

            UpdateRotatingCompassRose(_displayedHeading);
            UpdateBubbleHeadingText(_displayedHeading);
        }

        private void UpdateBubbleHeadingText(float heading)
        {
            if (_headingText != null)
            {
                int degInt = Mathf.RoundToInt(heading) % 360;
                if (degInt < 0) degInt += 360;
                if (degInt != _lastBubbleDeg)
                {
                    _lastBubbleDeg = degInt;
                    _headingText.text = $"{degInt:D3}°";
                }
            }
        }

        private void UpdateRotatingCompassRose(float currentHeading, bool force = false)
        {
            if (!force && Mathf.Abs(Mathf.DeltaAngle(currentHeading, _lastRenderedHeading)) < 0.02f)
            {
                return;
            }
            _lastRenderedHeading = currentHeading;

            float s = CurrentDpiScale;
            float radius = ARC_RADIUS * s;
            float yCenterOffset = 0f;

            int centerTickDeg = Mathf.RoundToInt(currentHeading / 5f) * 5;
            int tickIdx = 0;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color textCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color subTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

            for (int offsetDeg = -50; offsetDeg <= 50; offsetDeg += 5)
            {
                if (tickIdx >= _tickPool.Count) break;

                int tickDeg = centerTickDeg + offsetDeg;
                int normalizedDeg = (tickDeg % 360 + 360) % 360;

                float deltaAngle = Mathf.DeltaAngle(currentHeading, tickDeg);
                if (Mathf.Abs(deltaAngle) > MAX_ANGULAR_SPAN) continue;

                var item = _tickPool[tickIdx];
                if (!item.Root.activeSelf) item.Root.SetActive(true);

                float rad = deltaAngle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius - yCenterOffset);
                item.Rt.anchoredPosition = pos;
                item.Rt.localEulerAngles = new Vector3(0f, 0f, -deltaAngle);

                float edgeAlpha = Mathf.Clamp01((MAX_ANGULAR_SPAN - Mathf.Abs(deltaAngle)) / 10f);

                if (item.CurrentDeg != normalizedDeg)
                {
                    item.CurrentDeg = normalizedDeg;
                    bool isMajor = (normalizedDeg % 10 == 0);
                    bool isCardinal = (normalizedDeg % 90 == 0);

                    if (isCardinal)
                    {
                        string cardStr = "N";
                        switch (normalizedDeg)
                        {
                            case 0: cardStr = "N"; break;
                            case 90: cardStr = "E"; break;
                            case 180: cardStr = "S"; break;
                            case 270: cardStr = "W"; break;
                        }
                        Color cardCol = style.GetCardinalColor(normalizedDeg, theme);
                        item.BaseLineColor = cardCol;
                        item.BaseColor = cardCol;
                        item.LineRt.sizeDelta = new Vector2(2f * s, 8f * s);
                        item.Label.text = cardStr;
                    }
                    else if (isMajor)
                    {
                        item.BaseLineColor = textCol;
                        item.LineRt.sizeDelta = new Vector2(1.5f * s, 6.5f * s);
                        if (normalizedDeg % 30 == 0)
                        {
                            item.BaseColor = subTickCol;
                            item.Label.text = $"{normalizedDeg:D3}";
                        }
                        else
                        {
                            item.BaseColor = Color.clear;
                            item.Label.text = string.Empty;
                        }
                    }
                    else
                    {
                        item.BaseLineColor = WidgetStyleManager.Weighted(subTickCol, LineWeight.Bold);
                        item.BaseColor = Color.clear;
                        item.LineRt.sizeDelta = new Vector2(1f * s, 4f * s);
                        item.Label.text = string.Empty;
                    }
                }

                if (Mathf.Abs(item.LastAlpha - edgeAlpha) > 0.02f)
                {
                    item.LastAlpha = edgeAlpha;
                    Color finalLineCol = item.BaseLineColor;
                    finalLineCol.a *= edgeAlpha;
                    item.Line.color = finalLineCol;

                    if (!string.IsNullOrEmpty(item.Label.text))
                    {
                        Color finalLblCol = item.BaseColor;
                        finalLblCol.a *= edgeAlpha;
                        item.Label.color = finalLblCol;
                    }
                }

                _tickPool[tickIdx] = item;
                tickIdx++;
            }

            for (int i = tickIdx; i < _tickPool.Count; i++)
            {
                _tickPool[i].Root.SetActive(false);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, theme);
            if (_bubblePointerTip != null)
            {
                _bubblePointerTip.color = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            }
            if (_bubblePointerOutline != null)
            {
                _bubblePointerOutline.effectColor = style.GetCardBorderColor(CardStyleRole.Emphasized, theme);
            }
            ApplyText(_headingText, TextStyleRole.PrimaryValue, theme);

            Color lubberColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            if (_lubberBar != null) _lubberBar.color = lubberColor;
            if (_lubberStem != null) _lubberStem.color = lubberColor;

            Material panelMat = style.GetUiMaterial(isText: false);
            if (_bandBgImages != null && _bandBgImages.Count > 0)
            {
                Color bandCol = style.GetCardBackgroundColor(CardStyleRole.Normal, theme);
                for (int i = 0; i < _bandBgImages.Count; i++)
                {
                    if (_bandBgImages[i] != null)
                    {
                        _bandBgImages[i].material = panelMat;
                        _bandBgImages[i].color = bandCol;
                    }
                }
            }

            if (_bandRimImages != null && _bandRimImages.Count > 0)
            {
                Color borderCol = WidgetStyleManager.GetDerivedColor((Color)theme.FrameBorderColor, 0.35f);
                for (int i = 0; i < _bandRimImages.Count; i++)
                {
                    if (_bandRimImages[i] != null)
                        _bandRimImages[i].color = borderCol;
                }
            }

            Material textMat = style.GetUiMaterial(isText: true);
            for (int i = 0; i < _tickPool.Count; i++)
            {
                var tick = _tickPool[i];
                tick.CurrentDeg = -999;
                tick.LastAlpha = -1f;
                if (tick.Label != null)
                {
                    tick.Label.material = textMat;
                }
                _tickPool[i] = tick;
            }
            if (FlightTelemetryContext.Current != null)
            {
                UpdateRotatingCompassRose((float)FlightTelemetryContext.Current.Heading, force: true);
            }
        }

        protected override void OnDestroy()
        {
            if (_bubbleBtn != null)
            {
                _bubbleBtn.onClick.RemoveListener(OnBubbleClicked);
                _bubbleBtn = null;
            }
            base.OnDestroy();
        }
    }

    public class HeadingBubblePointerHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else
            {
                OnLeftClick?.Invoke();
            }
        }
    }
}

