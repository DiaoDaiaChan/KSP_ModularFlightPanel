using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// PFD 姿态球顶部圆弧航向指示带 (Navball Heading Arc Ribbon - Set 2 / 图2)
    /// 紧密环绕姿态球上缘，具备平滑滚动的度数刻度、红南/蓝北罗盘主方位标识、
    /// 顶部气泡框 (Speech-bubble) 权威数显标牌以及翡翠绿反T型基准游标。
    /// 严格继承 BaseFlightWidget，所有视觉样式与数值全生命周期数据驱动。
    /// </summary>
    [FlightWidget("heading_arc", "heading", "compass_arc", Category = WidgetCategory.Navigation, DisplayName = "PFD 航向指示标尺弧", Description = "主飞行仪表（PFD）顶部平滑滚动机体罗盘弧，带航向数显与度数刻度。", DefaultWidgetId = "core.heading_arc", DefaultX = 0f, DefaultY = 76f, IsSingleton = true, HighFrequency = true, ExactIds = new[] { "core.heading_arc" })]
    public class HeadingArcWidget : BaseFlightWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(202f, 82f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget - 允许编辑模式自由拉动长宽比与曲率)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(140f, 40f);
        public Vector2 MaxBaseSize => new Vector2(400f, 200f);

        private const int MAX_VISIBLE_TICKS = 24;
        private const float ARC_RADIUS = 92f;
        private const float ARC_Y_CENTER_OFFSET = 76f;
        private const float MAX_ANGULAR_SPAN = 55f; // 可见视口半角范围 (±55°)

        // 当前动态自适应尺寸与椭圆几何参数 (Adaptive Radius & Curvature)
        private float _currentRadiusX = 0f;
        private float _currentRadiusY = 0f;
        private float _currentYCenterOffset = 0f;

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
        private readonly List<RectTransform> _bandPlateRts = new List<RectTransform>();
        private readonly List<RectTransform> _outerRimRts = new List<RectTransform>();
        private readonly List<RectTransform> _innerRimRts = new List<RectTransform>();

        // 通配符通道与配置
        private string _valueToken = "{HDG}";

        // 航向平滑阻尼动力学引擎 (EFIS Avionics Damping Filter)
        private float _targetHeading = 0f;
        private float _displayedHeading = 0f;
        private float _headingVelocity = 0f;
        private bool _isHeadingInitialized = false;
        private float _lastRenderedHeading = -999f;
        private int _lastBubbleDeg = -1;
        private Color _cachedTextCol;
        private Color _cachedSubTickCol;
        private Color _cachedNorthCol;
        private Color _cachedEastCol;
        private Color _cachedSouthCol;
        private Color _cachedWestCol;
        private bool _hasCachedArcColors = false;

        private Color GetCachedCardinalColor(int deg)
        {
            switch (deg)
            {
                case 0: return _cachedNorthCol;
                case 90: return _cachedEastCol;
                case 180: return _cachedSouthCol;
                case 270: return _cachedWestCol;
                default: return _cachedTextCol;
            }
        }

        public static Action OnCycleHeadingModeAction;
        public static Action OnToggleReferenceFrameWindowAction;

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            float s = CurrentDpiScale;
            if (s <= 0.001f) s = 1.0f;

            float scaleX = (BaseSize.x > 0f) ? (pixelSize.x / (BaseSize.x * s)) : 1.0f;
            float scaleY = (BaseSize.y > 0f) ? (pixelSize.y / (BaseSize.y * s)) : 1.0f;

            scaleX = Mathf.Clamp(scaleX, 0.4f, 3.0f);
            scaleY = Mathf.Clamp(scaleY, 0.4f, 3.0f);

            _currentRadiusX = ARC_RADIUS * s * scaleX;
            _currentRadiusY = ARC_RADIUS * s * scaleY;
            _currentYCenterOffset = ARC_Y_CENTER_OFFSET * s * scaleY;

            if (_arcBandRoot == null) return;

            UpdateArcBandLayout(_currentRadiusX, _currentRadiusY, _currentYCenterOffset, s);

            if (_speechBubbleRt != null)
            {
                _speechBubbleRt.anchoredPosition = new Vector2(0f, _currentRadiusY + 15f * s - _currentYCenterOffset);
            }

            if (_lubberLineRoot != null)
            {
                var lubRt = _lubberLineRoot.GetComponent<RectTransform>();
                if (lubRt != null) lubRt.anchoredPosition = new Vector2(0f, _currentRadiusY - _currentYCenterOffset);
            }

            UpdateRotatingCompassRose(_displayedHeading, force: true);
        }

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            ApplyCanvasIsolation(true);

            float s = CurrentDpiScale;
            if (_currentRadiusX <= 0.001f || _currentRadiusY <= 0.001f)
            {
                float effW = (RectTransform != null && RectTransform.sizeDelta.x > 10f) ? RectTransform.sizeDelta.x : (BaseSize.x * s);
                float effH = (RectTransform != null && RectTransform.sizeDelta.y > 10f) ? RectTransform.sizeDelta.y : (BaseSize.y * s);
                float scaleX = Mathf.Clamp(effW / (BaseSize.x * s), 0.4f, 3.0f);
                float scaleY = Mathf.Clamp(effH / (BaseSize.y * s), 0.4f, 3.0f);

                _currentRadiusX = ARC_RADIUS * s * scaleX;
                _currentRadiusY = ARC_RADIUS * s * scaleY;
                _currentYCenterOffset = ARC_Y_CENTER_OFFSET * s * scaleY;
            }

            if (config != null && !string.IsNullOrEmpty(config.NumericToken))
            {
                _valueToken = config.NumericToken;
            }
            _valueToken = GetTemplateChannel(new[] { "VAL", "VALUE", "TOKEN", "HDG" }, _valueToken);

            // 1. 构建弧形暗色玻璃背景带
            BuildArcBand(_currentRadiusX, _currentRadiusY, _currentYCenterOffset, s, theme);

            // 2. 初始化刻度对象池
            BuildTickPool(s, theme);

            // 3. 构建顶部气泡框数显标牌
            BuildSpeechBubble(_currentRadiusY, _currentYCenterOffset, s, theme);

            // 4. 构建翡翠绿反T型基准游标
            BuildLubberMark(_currentRadiusY, _currentYCenterOffset, s, theme);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "arc_band", "罗盘弧底带", _arcBandRoot, (t) => {
                WidgetStyleManager st = WidgetStyleManager.Instance;
                Material pm = st.GetUiMaterial(isText: false);
                Color bandCol = st.GetCardBackgroundColor(CardStyleRole.Normal, t);
                for (int i = 0; i < _bandBgImages.Count; i++)
                {
                    if (_bandBgImages[i] != null)
                    {
                        _bandBgImages[i].material = pm;
                        _bandBgImages[i].color = bandCol;
                    }
                }
            }));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "speech_bubble", "气泡航向标牌", _speechBubbleRoot, (t) => {
                ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, t);
                ApplyText(_headingText, TextStyleRole.PrimaryValue, t);
            }));
            this.Controls.Register(WidgetControlManager.WrapElement(this, "lubber_mark", "翡翠绿基准游标", _lubberLineRoot, (t) => {
                Color lc = WidgetStyleManager.Meter(MeterStyleRole.Primary, t);
                if (_lubberBar != null) _lubberBar.color = lc;
                if (_lubberStem != null) _lubberStem.color = lc;
            }));

            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void BuildArcBand(float rx, float ry, float yCenterOffset, float s, ThemeConfig theme)
        {
            _arcBandRoot = CreateContainer("Arc_Band_Root", transform).gameObject;

            _bandBgImages.Clear();
            _bandRimImages.Clear();
            _bandPlateRts.Clear();
            _outerRimRts.Clear();
            _innerRimRts.Clear();

            Color bandCol = WidgetStyleManager.Instance.GetCardBackgroundColor(CardStyleRole.Normal, theme);
            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);

            const int segCount = 24;
            float step = (MAX_ANGULAR_SPAN * 2f) / segCount;
            float rAvg = (rx + ry) * 0.5f;
            float arcSegW = ((2f * Mathf.PI * rAvg * (MAX_ANGULAR_SPAN * 2f / 360f)) / segCount) + 1.5f * s;
            float bandThickness = 22f * s;

            for (int i = 0; i <= segCount; i++)
            {
                float ang = -MAX_ANGULAR_SPAN + (i * step);
                float rad = ang * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                float nx = ry * sin;
                float ny = rx * cos;
                float normLen = Mathf.Sqrt(nx * nx + ny * ny);
                Vector2 normDir = normLen > 0.0001f ? new Vector2(nx / normLen, ny / normLen) : Vector2.up;
                float normalAngle = Mathf.Atan2(nx, ny) * Mathf.Rad2Deg;

                Vector2 basePt = new Vector2(sin * rx, cos * ry - yCenterOffset);

                // 1. 半透明暗色玻璃遮光弧板
                Vector2 platePos = basePt + normDir * (-5f * s);
                GameObject plate = UIFactory.CreatePanel(_arcBandRoot.transform, $"BandPlate_{i}",
                    new Vector2(arcSegW, bandThickness), platePos, bandCol);
                plate.transform.localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                _bandBgImages.Add(plate.GetComponent<Image>());
                _bandPlateRts.Add(plate.GetComponent<RectTransform>());

                // 2. 外缘极细发光轮廓
                Vector2 outerRimPos = basePt + normDir * (6f * s);
                GameObject outerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"OuterRim_{i}",
                    new Vector2(arcSegW, 1.2f * s), outerRimPos, WidgetStyleManager.Weighted(borderCol, LineWeight.Strong));
                outerRim.transform.localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                _bandRimImages.Add(outerRim.GetComponent<Image>());
                _outerRimRts.Add(outerRim.GetComponent<RectTransform>());

                // 3. 内缘细弱辅助线
                Vector2 innerRimPos = basePt + normDir * (-16f * s);
                GameObject innerRim = UIFactory.CreatePanel(_arcBandRoot.transform, $"InnerRim_{i}",
                    new Vector2(arcSegW, 1.0f * s), innerRimPos, WidgetStyleManager.Weighted(borderCol, LineWeight.Subtle));
                innerRim.transform.localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                _bandRimImages.Add(innerRim.GetComponent<Image>());
                _innerRimRts.Add(innerRim.GetComponent<RectTransform>());
            }
        }

        private void UpdateArcBandLayout(float rx, float ry, float yCenterOffset, float s)
        {
            if (_bandPlateRts.Count == 0) return;

            const int segCount = 24;
            float step = (MAX_ANGULAR_SPAN * 2f) / segCount;
            float rAvg = (rx + ry) * 0.5f;
            float arcSegW = ((2f * Mathf.PI * rAvg * (MAX_ANGULAR_SPAN * 2f / 360f)) / segCount) + 1.5f * s;
            float bandThickness = 22f * s;

            for (int i = 0; i <= segCount && i < _bandPlateRts.Count; i++)
            {
                float ang = -MAX_ANGULAR_SPAN + (i * step);
                float rad = ang * Mathf.Deg2Rad;
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                float nx = ry * sin;
                float ny = rx * cos;
                float normLen = Mathf.Sqrt(nx * nx + ny * ny);
                Vector2 normDir = normLen > 0.0001f ? new Vector2(nx / normLen, ny / normLen) : Vector2.up;
                float normalAngle = Mathf.Atan2(nx, ny) * Mathf.Rad2Deg;

                Vector2 basePt = new Vector2(sin * rx, cos * ry - yCenterOffset);

                if (_bandPlateRts[i] != null)
                {
                    _bandPlateRts[i].anchoredPosition = basePt + normDir * (-5f * s);
                    _bandPlateRts[i].localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                    _bandPlateRts[i].sizeDelta = new Vector2(arcSegW, bandThickness);
                }
                if (i < _outerRimRts.Count && _outerRimRts[i] != null)
                {
                    _outerRimRts[i].anchoredPosition = basePt + normDir * (6f * s);
                    _outerRimRts[i].localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                    _outerRimRts[i].sizeDelta = new Vector2(arcSegW, 1.2f * s);
                }
                if (i < _innerRimRts.Count && _innerRimRts[i] != null)
                {
                    _innerRimRts[i].anchoredPosition = basePt + normDir * (-16f * s);
                    _innerRimRts[i].localEulerAngles = new Vector3(0f, 0f, -normalAngle);
                    _innerRimRts[i].sizeDelta = new Vector2(arcSegW, 1.0f * s);
                }
            }
        }

        private void BuildTickPool(float s, ThemeConfig theme)
        {
            _tickPool.Clear();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            for (int i = 0; i < MAX_VISIBLE_TICKS; i++)
            {
                RectTransform rt = CreateContainer($"TickNode_{i}", transform,
                    new Vector2(30f * s, 30f * s), Vector2.zero);
                GameObject root = rt.gameObject;

                // 刻度线
                Image lineImg = CreateChild<Image>("Line", root.transform,
                    new Vector2(1.5f * s, 7f * s), Vector2.zero);
                GameObject lineObj = lineImg.gameObject;
                RectTransform lineRt = lineImg.rectTransform;
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

        private void BuildSpeechBubble(float ry, float yCenterOffset, float s, ThemeConfig theme)
        {
            Vector2 boxSize = new Vector2(50f * s, 20f * s);
            Vector2 bubblePos = new Vector2(0f, ry + 15f * s - yCenterOffset);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _bubbleBtn = CreateButton("Heading_SpeechBubble", transform, out _speechBubbleRt, out _bubbleBg,
                boxSize, bubblePos);
            _speechBubbleRoot = _speechBubbleRt.gameObject;
            _bubbleBg.color = Color.clear;

            _bubbleOutline = _speechBubbleRoot.AddComponent<Outline>();
            _bubbleOutline.effectDistance = new Vector2(1f * s, 1f * s);
            ApplyCard(_bubbleBg, _bubbleOutline, CardStyleRole.Emphasized, theme);

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
            _bubblePointerTip = CreateChild<Image>("Bubble_Pointer_Tip", _speechBubbleRoot.transform,
                new Vector2(7f * s, 7f * s), new Vector2(0f, -boxSize.y * 0.5f + 0.5f * s));
            GameObject tipObj = _bubblePointerTip.gameObject;
            RectTransform tipRt = _bubblePointerTip.rectTransform;
            tipRt.localEulerAngles = new Vector3(0f, 0f, 45f);
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

        private void BuildLubberMark(float ry, float yCenterOffset, float s, ThemeConfig theme)
        {
            Vector2 lubberPos = new Vector2(0f, ry - yCenterOffset);

            RectTransform lubRt = CreateContainer("Lubber_Line_Root", transform,
                new Vector2(16f * s, 12f * s), lubberPos);
            _lubberLineRoot = lubRt.gameObject;

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

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel) return;

            float rawHeading;
            var hook = NavBallHookService.Provider;
            if (hook != null && hook.HasStockNavBall)
            {
                rawHeading = hook.HeadingAngle;
            }
            else
            {
                double evalHdg = TelemetryTokenEngine.EvaluateNumeric(_valueToken, telemetry);
                rawHeading = !double.IsNaN(evalHdg) ? (float)evalHdg : (float)telemetry.Heading;
            }
            if (float.IsNaN(rawHeading)) rawHeading = 0f;

            _targetHeading = (rawHeading % 360f + 360f) % 360f;

            if (!_isHeadingInitialized)
            {
                _displayedHeading = _targetHeading;
                _isHeadingInitialized = true;
            }

            float dt = context.DeltaTime;
            if (dt <= 0.0001f)
            {
                _displayedHeading = _targetHeading;
            }
            else
            {
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
            }
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_isHeadingInitialized) return;

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
            float rx = _currentRadiusX > 0.001f ? _currentRadiusX : (ARC_RADIUS * s);
            float ry = _currentRadiusY > 0.001f ? _currentRadiusY : (ARC_RADIUS * s);
            float yCenterOffset = _currentYCenterOffset > 0.001f ? _currentYCenterOffset : (ARC_Y_CENTER_OFFSET * s);

            int centerTickDeg = Mathf.RoundToInt(currentHeading / 5f) * 5;
            int tickIdx = 0;

            if (!_hasCachedArcColors)
            {
                ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
                WidgetStyleManager style = WidgetStyleManager.Instance;
                _cachedTextCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                _cachedSubTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _cachedNorthCol = style.GetCardinalColor(0, theme);
                _cachedEastCol = style.GetCardinalColor(90, theme);
                _cachedSouthCol = style.GetCardinalColor(180, theme);
                _cachedWestCol = style.GetCardinalColor(270, theme);
                _hasCachedArcColors = true;
            }
            Color textCol = _cachedTextCol;
            Color subTickCol = _cachedSubTickCol;

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
                float sin = Mathf.Sin(rad);
                float cos = Mathf.Cos(rad);

                Vector2 pos = new Vector2(sin * rx, cos * ry - yCenterOffset);
                item.Rt.anchoredPosition = pos;

                float normalAngle = Mathf.Atan2(ry * sin, rx * cos) * Mathf.Rad2Deg;
                item.Rt.localEulerAngles = new Vector3(0f, 0f, -normalAngle);

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
                        Color cardCol = GetCachedCardinalColor(normalizedDeg);
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
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            _cachedTextCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            _cachedSubTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            _cachedNorthCol = style.GetCardinalColor(0, theme);
            _cachedEastCol = style.GetCardinalColor(90, theme);
            _cachedSouthCol = style.GetCardinalColor(180, theme);
            _cachedWestCol = style.GetCardinalColor(270, theme);
            _hasCachedArcColors = true;

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

            this.Controls.ApplyThemeToControls(theme);
        }

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
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

