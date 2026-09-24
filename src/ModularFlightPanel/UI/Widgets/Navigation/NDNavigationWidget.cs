using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 民航客机/现代先进战机风格导航显示器 (Navigation Display - ND in ARC Mode - Set 1 / 图1)
    /// 具备上部罗盘圆弧标尺带 (10度双数字数显)、基准三角游标与航向跟踪线 (Green Track Vector)、
    /// 20/40/60 虚线同心测距环 (Range Rings)、中心金黄飞机微标 (Yellow Aircraft Symbol)、
    /// 航路航点引导 (Target / Waypoint Diamond) 以及四角专业航电数据 (GS/TAS, 风向风速, 目标ETA, VOR导引)。
    /// 严格继承 BaseFlightWidget，杜绝硬编码，由 IFlightTelemetry 与 TelemetryTokenEngine 统一驱动。
    /// </summary>
    public class NDNavigationWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        private const float ND_ARC_RADIUS = 108f;
        private const float ND_ARC_FOV = 48f; // 可见罗盘视口半角范围 (±48°)
        private const int TICK_POOL_COUNT = 22;

        private struct NDTickUI
        {
            public GameObject Root;
            public RectTransform Rt;
            public Image Line;
            public RectTransform LineRt;
            public Text Label;
            public RectTransform LabelRt;
        }

        private readonly List<NDTickUI> _tickPool = new List<NDTickUI>();

        // 基础面板
        private Image _bgImage;
        private Outline _outline;

        // 顶部四角航电读数
        private Text _gsTasText;
        private Text _windText;
        private Text _procedureText;
        private Text _topHeadingText;
        private Text _waypointNameDistText;
        private Text _waypointEtaText;

        // 底部导引状态
        private Text _vor1Text;
        private Text _vor2Text;
        private Text _driftText;

        // 几何元素
        private GameObject _airplaneSymbol;
        private GameObject _trackLine;
        private GameObject _targetRouteLine;
        private GameObject _targetWaypointMarker;
        private Text _targetWpName;
        private GameObject _lubberTriangle;
        private GameObject _rangeRingsRoot;

        // 飞机中心挂载点 (相对面板底部偏置)
        private Vector2 _aircraftCenterPos;

        // 通配符通道与模板
        private string _headingToken = "{HDG}";
        private string _gsTasTemplate = "GS {SPD:SURF:F0}  TAS {SPD:OBT:F0}";
        private string _windTemplate = "081 / 17 ↑";
        private string _procedureTemplate = "ILS04-Z";
        private string _waypointNameDistTemplate = "TGT-1 302°\n39 NM";
        private string _waypointEtaTemplate = "12:23";
        private string _vor1Template = "▲ VOR1\n108.20 M\n100 NM";
        private string _vor2Template = "VOR2 ▲\nXSJ\n100 NM";
        private string _driftTemplate = "34.7 L";

        // 脏检查与缓存
        private float _lastRenderedHeading = -999f;
        private string _lastTens = string.Empty;
        private string _lastGsTas = string.Empty;
        private string _lastWind = string.Empty;
        private string _lastProc = string.Empty;
        private string _lastWp = string.Empty;
        private string _lastEta = string.Empty;
        private string _lastVor1 = string.Empty;
        private string _lastVor2 = string.Empty;
        private string _lastDrift = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(280f * s, 260f * s);
            RectTransform.sizeDelta = panelSize;
            _aircraftCenterPos = new Vector2(0f, -panelSize.y * 0.5f + 48f * s);

            ParseCustomTemplate(config);

            // 1. 半透明暗色玻璃卡片底板
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = Color.clear;
            _outline = gameObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            UIFactory.ApplyCockpitChrome(gameObject, _bgImage.color, _outline.effectColor, s);

            // 2. 顶部航电状态角标
            BuildAvionicsHeader(panelSize, s, theme);

            // 3. 构建 20 / 40 / 60 同心虚线测距环
            BuildRangeRings(panelSize, s, theme);

            // 4. 构建顶部罗盘圆弧与刻度池
            BuildCompassArc(panelSize, s, theme);

            // 5. 构建中央飞机微标与基准跟踪线
            BuildAircraftAndTrackLine(panelSize, s, theme);

            // 6. 构建航点与导航引导路径
            BuildWaypointGuidance(panelSize, s, theme);

            // 7. 底部 VOR / NAV 导引栏
            BuildBottomNavAids(panelSize, s, theme);

            ApplyTheme(theme);
        }

        private void ParseCustomTemplate(WidgetConfig config)
        {
            if (config != null && !string.IsNullOrEmpty(config.NumericToken))
            {
                _headingToken = config.NumericToken;
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
                    case "HDG":
                    case "VAL":
                    case "TOKEN": _headingToken = v; break;
                    case "GSTAS":
                    case "SPEED": _gsTasTemplate = v; break;
                    case "WIND": _windTemplate = v; break;
                    case "PROC":
                    case "APPROACH": _procedureTemplate = v; break;
                    case "WP":
                    case "WAYPOINT": _waypointNameDistTemplate = v; break;
                    case "ETA": _waypointEtaTemplate = v; break;
                    case "VOR1": _vor1Template = v; break;
                    case "VOR2": _vor2Template = v; break;
                    case "DRIFT": _driftTemplate = v; break;
                }
            }
        }

        private void BuildAvionicsHeader(Vector2 size, float s, ThemeConfig theme)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 左上角：GS 388 TAS 372
            _gsTasText = UIFactory.CreateText(transform, "GS_TAS_Text", "GS 000  TAS 000",
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform gsRt = _gsTasText.GetComponent<RectTransform>();
            gsRt.sizeDelta = new Vector2(110f * s, 14f * s);
            gsRt.anchoredPosition = new Vector2(-halfW + 62f * s, halfH - 12f * s);

            // 左上角第二行：风向风速 081 / 17 ↑
            _windText = UIFactory.CreateText(transform, "Wind_Text", _windTemplate,
                Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform windRt = _windText.GetComponent<RectTransform>();
            windRt.sizeDelta = new Vector2(90f * s, 14f * s);
            windRt.anchoredPosition = new Vector2(-halfW + 52f * s, halfH - 26f * s);

            // 顶部中央：进近航路模式 (ILS04-Z 或 TGT-NAV)
            _procedureText = UIFactory.CreateText(transform, "Procedure_Text", _procedureTemplate,
                Mathf.Max(8, Mathf.RoundToInt(9.5f * s)), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform procRt = _procedureText.GetComponent<RectTransform>();
            procRt.sizeDelta = new Vector2(90f * s, 16f * s);
            procRt.anchoredPosition = new Vector2(0f, halfH - 12f * s);

            // 顶部中央：当前航向大十位显数 (如 "24")
            _topHeadingText = UIFactory.CreateText(transform, "Top_Heading_Tens", "24",
                Mathf.RoundToInt(14f * s), TextAnchor.MiddleCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform hdgRt = _topHeadingText.GetComponent<RectTransform>();
            hdgRt.sizeDelta = new Vector2(40f * s, 18f * s);
            hdgRt.anchoredPosition = new Vector2(0f, halfH - 30f * s);

            // 右上角：目标航点与方位
            _waypointNameDistText = UIFactory.CreateText(transform, "Waypoint_NameDist", _waypointNameDistTemplate,
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform wpRt = _waypointNameDistText.GetComponent<RectTransform>();
            wpRt.sizeDelta = new Vector2(100f * s, 26f * s);
            wpRt.anchoredPosition = new Vector2(halfW - 56f * s, halfH - 16f * s);

            // 右上角第二行：预估到达时间
            _waypointEtaText = UIFactory.CreateText(transform, "Waypoint_ETA", _waypointEtaTemplate,
                Mathf.Max(7, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform etaRt = _waypointEtaText.GetComponent<RectTransform>();
            etaRt.sizeDelta = new Vector2(60f * s, 14f * s);
            etaRt.anchoredPosition = new Vector2(halfW - 36f * s, halfH - 34f * s);
        }

        private void BuildRangeRings(Vector2 size, float s, ThemeConfig theme)
        {
            _rangeRingsRoot = new GameObject("ND_RangeRings_Root", typeof(RectTransform));
            _rangeRingsRoot.transform.SetParent(transform, false);

            Color borderCol = WidgetStyleManager.Instance.GetCardBorderColor(CardStyleRole.Normal, theme);
            Color ringCol = WidgetStyleManager.WithAlpha(borderCol, 0.28f);
            float[] radii = new float[] { 36f * s, 72f * s, 108f * s };
            string[] ringLabels = new string[] { "", "40", "60" };

            for (int rIdx = 0; rIdx < radii.Length; rIdx++)
            {
                float radius = radii[rIdx];
                string label = ringLabels[rIdx];

                const int segs = 16;
                for (int i = 0; i <= segs; i++)
                {
                    if (i % 2 == 1) continue;
                    float ang = -52f + (i * (104f / segs));
                    float rad = ang * Mathf.Deg2Rad;
                    Vector2 pos = _aircraftCenterPos + new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);

                    GameObject dot = UIFactory.CreatePanel(_rangeRingsRoot.transform, $"Ring_{rIdx}_Seg_{i}",
                        new Vector2(4f * s, 1.2f * s), pos, ringCol);
                    dot.transform.localEulerAngles = new Vector3(0f, 0f, -ang);
                }

                if (!string.IsNullOrEmpty(label))
                {
                    float leftAng = -42f * Mathf.Deg2Rad;
                    Vector2 leftPos = _aircraftCenterPos + new Vector2(Mathf.Sin(leftAng) * radius - 10f * s, Mathf.Cos(leftAng) * radius);
                    Text lTxt = UIFactory.CreateText(_rangeRingsRoot.transform, $"RingLbl_L_{label}", label,
                        Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                        WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    lTxt.GetComponent<RectTransform>().anchoredPosition = leftPos;

                    float rightAng = 42f * Mathf.Deg2Rad;
                    Vector2 rightPos = _aircraftCenterPos + new Vector2(Mathf.Sin(rightAng) * radius + 10f * s, Mathf.Cos(rightAng) * radius);
                    Text rTxt = UIFactory.CreateText(_rangeRingsRoot.transform, $"RingLbl_R_{label}", label,
                        Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                        WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
                    rTxt.GetComponent<RectTransform>().anchoredPosition = rightPos;
                }
            }
        }

        private void BuildCompassArc(Vector2 size, float s, ThemeConfig theme)
        {
            float radius = ND_ARC_RADIUS * s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 顶部正中央倒三角形基准游标
            Vector2 lubberPos = _aircraftCenterPos + new Vector2(0f, radius + 3f * s);
            _lubberTriangle = new GameObject("Lubber_Triangle", typeof(RectTransform), typeof(Image));
            _lubberTriangle.transform.SetParent(transform, false);
            RectTransform lubRt = _lubberTriangle.GetComponent<RectTransform>();
            lubRt.sizeDelta = new Vector2(7f * s, 7f * s);
            lubRt.anchoredPosition = lubberPos;
            lubRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            Image lubImg = _lubberTriangle.GetComponent<Image>();
            lubImg.color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);

            // 2. 刻度对象池
            _tickPool.Clear();
            for (int i = 0; i < TICK_POOL_COUNT; i++)
            {
                GameObject root = new GameObject($"ND_Tick_{i}", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                RectTransform rt = root.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(24f * s, 24f * s);

                GameObject lineObj = new GameObject("Line", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(root.transform, false);
                RectTransform lineRt = lineObj.GetComponent<RectTransform>();
                lineRt.sizeDelta = new Vector2(1.5f * s, 6.5f * s);
                lineRt.anchoredPosition = Vector2.zero;
                Image lineImg = lineObj.GetComponent<Image>();
                lineImg.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(8f * s));
                Text lbl = UIFactory.CreateText(root.transform, "Label", "", fontSize, TextAnchor.MiddleCenter,
                    style.GetTextColor(TextStyleRole.PrimaryValue, theme));
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = new Vector2(24f * s, 14f * s);
                lblRt.anchoredPosition = new Vector2(0f, 8.5f * s);

                _tickPool.Add(new NDTickUI
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

        private void BuildAircraftAndTrackLine(Vector2 size, float s, ThemeConfig theme)
        {
            float radius = ND_ARC_RADIUS * s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 垂直基准航迹线
            _trackLine = UIFactory.CreatePanel(transform, "ND_Track_Line",
                new Vector2(1.5f * s, radius - 10f * s),
                _aircraftCenterPos + new Vector2(0f, (radius - 10f * s) * 0.5f),
                WidgetStyleManager.Meter(MeterStyleRole.Primary, theme));

            // 跟踪双箭头
            GameObject chev = UIFactory.CreatePanel(transform, "ND_Track_Chevron",
                new Vector2(8f * s, 8f * s),
                _aircraftCenterPos + new Vector2(0f, radius * 0.85f),
                Color.clear);
            Outline chevOl = chev.AddComponent<Outline>();
            chevOl.effectColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            chevOl.effectDistance = new Vector2(1f * s, 1f * s);
            chev.transform.localEulerAngles = new Vector3(0f, 0f, 45f);

            // 2. 标志性飞机微标
            Color goldCol = style.GetTextColor(TextStyleRole.Warning, theme);
            _airplaneSymbol = new GameObject("ND_Aircraft_Symbol", typeof(RectTransform));
            _airplaneSymbol.transform.SetParent(transform, false);
            _airplaneSymbol.GetComponent<RectTransform>().anchoredPosition = _aircraftCenterPos;

            // 主机翼横杠
            UIFactory.CreatePanel(_airplaneSymbol.transform, "Wings", new Vector2(24f * s, 2.5f * s), Vector2.zero, goldCol);
            // 垂直机身立柱
            UIFactory.CreatePanel(_airplaneSymbol.transform, "Fuselage", new Vector2(2.5f * s, 16f * s), new Vector2(0f, 2f * s), goldCol);
            // 机尾平尾横杠
            UIFactory.CreatePanel(_airplaneSymbol.transform, "Tail", new Vector2(10f * s, 2f * s), new Vector2(0f, -5f * s), goldCol);
        }

        private void BuildWaypointGuidance(Vector2 size, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 目标航点微标 (绿色菱形 ◇)
            _targetWaypointMarker = new GameObject("ND_Target_Waypoint", typeof(RectTransform), typeof(Image));
            _targetWaypointMarker.transform.SetParent(transform, false);
            RectTransform wpRt = _targetWaypointMarker.GetComponent<RectTransform>();
            wpRt.sizeDelta = new Vector2(8f * s, 8f * s);
            wpRt.anchoredPosition = _aircraftCenterPos + new Vector2(35f * s, 45f * s);
            wpRt.localEulerAngles = new Vector3(0f, 0f, 45f);

            Image wpImg = _targetWaypointMarker.GetComponent<Image>();
            wpImg.color = Color.clear;
            Outline wpOl = _targetWaypointMarker.AddComponent<Outline>();
            wpOl.effectColor = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            wpOl.effectDistance = new Vector2(1.2f * s, 1.2f * s);

            // 航点名称
            _targetWpName = UIFactory.CreateText(_targetWaypointMarker.transform, "Wp_Name", "PP518",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform nameRt = _targetWpName.GetComponent<RectTransform>();
            nameRt.sizeDelta = new Vector2(45f * s, 12f * s);
            nameRt.anchoredPosition = new Vector2(10f * s, 0f);
            nameRt.localEulerAngles = new Vector3(0f, 0f, -45f);

            // 目标航线引导虚线
            Color priCol = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);
            _targetRouteLine = UIFactory.CreatePanel(transform, "ND_Route_Line",
                new Vector2(1.2f * s, 55f * s),
                _aircraftCenterPos + new Vector2(18f * s, 24f * s),
                WidgetStyleManager.WithAlpha(priCol, 0.45f));
            _targetRouteLine.transform.localEulerAngles = new Vector3(0f, 0f, -36f);
        }

        private void BuildBottomNavAids(Vector2 size, float s, ThemeConfig theme)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 左下角：▲ VOR1
            _vor1Text = UIFactory.CreateText(transform, "VOR1_Text", _vor1Template,
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform v1Rt = _vor1Text.GetComponent<RectTransform>();
            v1Rt.sizeDelta = new Vector2(85f * s, 36f * s);
            v1Rt.anchoredPosition = new Vector2(-halfW + 48f * s, -halfH + 24f * s);

            // 右下角：VOR2 ▲
            _vor2Text = UIFactory.CreateText(transform, "VOR2_Text", _vor2Template,
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform v2Rt = _vor2Text.GetComponent<RectTransform>();
            v2Rt.sizeDelta = new Vector2(85f * s, 36f * s);
            v2Rt.anchoredPosition = new Vector2(halfW - 48f * s, -halfH + 24f * s);

            // 侧风漂移角读数
            _driftText = UIFactory.CreateText(transform, "Drift_Text", _driftTemplate,
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleLeft,
                style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform driftRt = _driftText.GetComponent<RectTransform>();
            driftRt.sizeDelta = new Vector2(45f * s, 14f * s);
            driftRt.anchoredPosition = _aircraftCenterPos + new Vector2(-28f * s, 0f);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            double evalHdg = TelemetryTokenEngine.EvaluateNumeric(_headingToken, telemetry);
            float heading = !double.IsNaN(evalHdg) ? (float)evalHdg : (float)telemetry.Heading;
            if (float.IsNaN(heading)) heading = 0f;

            // 1. 更新顶部大十位数显 (如 24 代表 240°)
            int tens = Mathf.RoundToInt(heading / 10f) % 36;
            if (tens < 0) tens += 36;
            string tensStr = $"{tens:D2}";
            if (tensStr != _lastTens)
            {
                _lastTens = tensStr;
                if (_topHeadingText != null) _topHeadingText.text = tensStr;
            }

            // 2. 动态更新四角航电读数 (通配符求值与脏检查)
            string evalGsTas = TelemetryTokenEngine.Evaluate(_gsTasTemplate, telemetry);
            if (evalGsTas != _lastGsTas)
            {
                _lastGsTas = evalGsTas;
                if (_gsTasText != null) _gsTasText.text = evalGsTas;
            }

            string evalWind = TelemetryTokenEngine.Evaluate(_windTemplate, telemetry);
            if (evalWind != _lastWind)
            {
                _lastWind = evalWind;
                if (_windText != null) _windText.text = evalWind;
            }

            string evalProc = TelemetryTokenEngine.Evaluate(_procedureTemplate, telemetry);
            if (evalProc != _lastProc)
            {
                _lastProc = evalProc;
                if (_procedureText != null) _procedureText.text = evalProc;
            }

            string evalWp = TelemetryTokenEngine.Evaluate(_waypointNameDistTemplate, telemetry);
            if (evalWp != _lastWp)
            {
                _lastWp = evalWp;
                if (_waypointNameDistText != null) _waypointNameDistText.text = evalWp;
            }

            string evalEta = TelemetryTokenEngine.Evaluate(_waypointEtaTemplate, telemetry);
            if (evalEta != _lastEta)
            {
                _lastEta = evalEta;
                if (_waypointEtaText != null) _waypointEtaText.text = evalEta;
            }

            string evalVor1 = TelemetryTokenEngine.Evaluate(_vor1Template, telemetry);
            if (evalVor1 != _lastVor1)
            {
                _lastVor1 = evalVor1;
                if (_vor1Text != null) _vor1Text.text = evalVor1;
            }

            string evalVor2 = TelemetryTokenEngine.Evaluate(_vor2Template, telemetry);
            if (evalVor2 != _lastVor2)
            {
                _lastVor2 = evalVor2;
                if (_vor2Text != null) _vor2Text.text = evalVor2;
            }

            string evalDrift = TelemetryTokenEngine.Evaluate(_driftTemplate, telemetry);
            if (evalDrift != _lastDrift)
            {
                _lastDrift = evalDrift;
                if (_driftText != null) _driftText.text = evalDrift;
            }

            // 3. 动态更新罗盘圆弧十度刻度带
            if (Mathf.Abs(Mathf.DeltaAngle(heading, _lastRenderedHeading)) > 0.05f)
            {
                _lastRenderedHeading = heading;
                UpdateArcCompassRose(heading);
            }
        }

        private void UpdateArcCompassRose(float currentHeading)
        {
            float s = CurrentDpiScale;
            float radius = ND_ARC_RADIUS * s;
            int centerTickDeg = Mathf.RoundToInt(currentHeading / 5f) * 5;
            int tickIdx = 0;

            ThemeConfig theme = WidgetStyleManager.ResolveTheme(ThemeManager.Instance?.CurrentTheme);
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color textCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color subTickCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);

            for (int offsetDeg = -45; offsetDeg <= 45; offsetDeg += 5)
            {
                if (tickIdx >= _tickPool.Count) break;

                int tickDeg = centerTickDeg + offsetDeg;
                int normalizedDeg = (tickDeg % 360 + 360) % 360;

                float deltaAngle = Mathf.DeltaAngle(currentHeading, tickDeg);
                if (Mathf.Abs(deltaAngle) > ND_ARC_FOV) continue;

                var item = _tickPool[tickIdx];
                item.Root.SetActive(true);

                float rad = deltaAngle * Mathf.Deg2Rad;
                Vector2 pos = _aircraftCenterPos + new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);
                item.Rt.anchoredPosition = pos;
                item.Rt.localEulerAngles = new Vector3(0f, 0f, -deltaAngle);

                bool isTens = (normalizedDeg % 10 == 0);

                if (isTens)
                {
                    item.LineRt.sizeDelta = new Vector2(1.5f * s, 6.5f * s);
                    item.Line.color = textCol;

                    int displayTens = normalizedDeg / 10;
                    item.Label.text = $"{displayTens:D2}";
                    item.Label.color = textCol;
                    item.Label.fontSize = Mathf.RoundToInt(8.5f * s);
                    item.Label.gameObject.SetActive(true);
                }
                else
                {
                    item.LineRt.sizeDelta = new Vector2(1f * s, 4f * s);
                    item.Line.color = WidgetStyleManager.WithAlpha(subTickCol, 0.6f);
                    item.Label.gameObject.SetActive(false);
                }

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

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            ApplyText(_gsTasText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_topHeadingText, TextStyleRole.PrimaryValue, theme);
            ApplyText(_procedureText, TextStyleRole.Label, theme);
            ApplyText(_windText, TextStyleRole.Accent, theme);
            ApplyText(_waypointNameDistText, TextStyleRole.SecondaryValue, theme);
            ApplyText(_waypointEtaText, TextStyleRole.Accent, theme);
            ApplyText(_vor1Text, TextStyleRole.SecondaryValue, theme);
            ApplyText(_vor2Text, TextStyleRole.PrimaryValue, theme);
            ApplyText(_driftText, TextStyleRole.Accent, theme);
            if (_targetWpName != null) ApplyText(_targetWpName, TextStyleRole.Accent, theme);

            if (_lubberTriangle != null) _lubberTriangle.GetComponent<Image>().color = WidgetStyleManager.Meter(MeterStyleRole.Secondary, theme);
            if (_trackLine != null) _trackLine.GetComponent<Image>().color = WidgetStyleManager.Meter(MeterStyleRole.Primary, theme);

            Material textMat = style.GetUiMaterial(isText: true);
            for (int i = 0; i < _tickPool.Count; i++)
            {
                if (_tickPool[i].Label != null)
                {
                    _tickPool[i].Label.color = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
                    _tickPool[i].Label.material = textMat;
                }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
