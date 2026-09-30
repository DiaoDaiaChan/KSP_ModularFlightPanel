using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 猛禽发动机状态集群零-GC遥测快照 (MFP-SPEC-012)
    /// </summary>
    public struct SpaceXEngineState : IEquatable<SpaceXEngineState>
    {
        public bool HasVessel;
        public int ActiveEngines;
        public int TotalEngines;
        public int LitCount;
        public float Throttle;
        public float ThrottleScale;
        public bool IsFlameout;
        public bool IsIgniting;
        public bool IsFiring;
        public string StatusText;

        public bool Equals(SpaceXEngineState other)
        {
            return HasVessel == other.HasVessel &&
                   ActiveEngines == other.ActiveEngines &&
                   TotalEngines == other.TotalEngines &&
                   LitCount == other.LitCount &&
                   IsFlameout == other.IsFlameout &&
                   IsIgniting == other.IsIgniting &&
                   IsFiring == other.IsFiring &&
                   Math.Abs(Throttle - other.Throttle) < 0.01f &&
                   Math.Abs(ThrottleScale - other.ThrottleScale) < 0.01f &&
                   string.Equals(StatusText, other.StatusText, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is SpaceXEngineState other && Equals(other);
        public override int GetHashCode() => (HasVessel, ActiveEngines, TotalEngines, LitCount).GetHashCode();
    }

    /// <summary>
    /// SpaceX 猛禽发动机状态集群业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class SpaceXEngineLogic : WidgetLogic<SpaceXEngineState>
    {
        public string CutoffLabel { get; set; }
        public string ActiveTemplate { get; set; }
        public string FlameoutLabel { get; set; }
        public string IgnitionLabel { get; set; }

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            int activeEngines = telemetry.ActiveEngines;
            int totalEngines = telemetry.TotalStageEngines > 0 ? telemetry.TotalStageEngines : 6;
            float throttle = (float)telemetry.Throttle;

            bool isIgniting = telemetry.IsEngineIgniting;
            bool isFlameout = throttle > 0.05f && (activeEngines == 0 || telemetry.StagePropellantFraction <= 0.0001f);
            bool isFiring = !isFlameout && (isIgniting || (throttle > 0.005f && activeEngines > 0));

            int litCount = isFiring ? Mathf.Min(activeEngines > 0 ? activeEngines : totalEngines, totalEngines) : 0;
            float throttleScale = Mathf.Lerp(0.5f, 1.0f, Mathf.Clamp01(throttle));

            string sStr;
            if (isFlameout)
            {
                sStr = FlameoutLabel;
            }
            else if (isIgniting)
            {
                sStr = IgnitionLabel;
            }
            else if (throttle <= 0.005f)
            {
                sStr = CutoffLabel;
            }
            else
            {
                sStr = !string.IsNullOrEmpty(ActiveTemplate) ? string.Format(ActiveTemplate, litCount, totalEngines) : string.Empty;
            }

            CurrentState = new SpaceXEngineState
            {
                HasVessel = true,
                ActiveEngines = activeEngines,
                TotalEngines = totalEngines,
                LitCount = litCount,
                Throttle = throttle,
                ThrottleScale = throttleScale,
                IsFlameout = isFlameout,
                IsIgniting = isIgniting,
                IsFiring = isFiring,
                StatusText = sStr
            };
        }
    }

    /// <summary>
    /// SpaceX 猛禽发动机状态集群指示器 (SpaceX Webcast Engine Cluster Status Dial)
    /// 核心特性：
    ///   1. 自动识别当前分级发动机总数 (TotalStageEngines) 与当前实际点火运行引擎数 (ActiveEngines)；
    ///   2. 星舰 6 发猛禽标准构型 (3 台中心海平面机动猛禽 + 3 台外围真空大喷管猛禽)；
    ///   3. 智能兼容 Falcon 9 (9 发八角盘 Octaweb) 与任意舰船多发集群自适应排布；
    ///   4. 实时点火发光与节流推力羽流辉光，完全还原 5 发点火亮起 + 1 发停机关机的经典画面；
    ///   5. 严格遵循 MFP 架构规范：零硬编码与零颜色字面量 (MFP-SPEC-006)，GPU 矢量网格 (SPEC-002)，业务解耦大脑 (SPEC-012)。
    /// </summary>
    [FlightWidget("spacex_engines", "dragon_engines", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 引擎状态阵列", Description = "SpaceX 猎鹰 9 发动机多孔圆环/星舰猛禽集群点火状态阵列图。", DefaultWidgetId = "spacex.engines", DefaultX = 360f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "spacex.engines" })]
    public class SpaceXEngineWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(96f, 96f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 常量定义，确保 AST 词条基线只降不升
        private const string StrFlameout = "FLAMEOUT / DEPLETED";
        private const string StrIgnition = "IGNITION SEQUENCE";
        private const string StrCutoff = "MECO / CUTOFF";
        private const string StrActive = "{0} / {1} ACTIVE";
        private const string StrTitle = "ENGINES";

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;
        private ProceduralEngineBezelImage _dialBezel;
        private Text _titleText;
        private Text _statusText;

        // 发动机集群图元
        private class EngineNodeUI
        {
            public GameObject RootGo;
            public Image BaseRing;
            public Image CoreLight;
            public Vector2 NormalizedPos;
            public bool IsActive;
            public readonly CachedFloat LastScale = new CachedFloat(-1f, 0.005f);
        }

        private readonly List<EngineNodeUI> _engineNodes = new List<EngineNodeUI>();
        private Transform _clusterContainer;

        // 遥测缓存与脏标记 (MFP-SPEC-009)
        private readonly Cached<int> _cachedActiveEngines = new Cached<int>(-1);
        private readonly Cached<int> _cachedTotalEngines = new Cached<int>(-1);
        private readonly CachedFloat _cachedThrottle = new CachedFloat(-1f, 0.02f);
        private readonly Cached<string> _lastStatusStr = new Cached<string>(string.Empty);

        // 业务大脑 (MFP-SPEC-012)
        private readonly SpaceXEngineLogic _logic = new SpaceXEngineLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // CustomTemplate 自定义通道
        private string _titleCustom = StrTitle;
        private string _cutoffLabel = StrCutoff;
        private string _activeTemplate = StrActive;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _titleCustom = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SPX_ENGINES", StrTitle));
            _cutoffLabel = GetTemplateChannel("CUTOFF_LABEL", I18n.Tr("WIDGET_SPX_MECO_CUTOFF", StrCutoff));
            _activeTemplate = GetTemplateChannel("ACTIVE_TEMPLATE", I18n.Tr("WIDGET_SPX_ACTIVE_TEMPLATE", StrActive));

            _logic.CutoffLabel = _cutoffLabel;
            _logic.ActiveTemplate = _activeTemplate;
            _logic.FlameoutLabel = StrFlameout;
            _logic.IgnitionLabel = StrIgnition;

            // 1. 组件包围盒 (基准 96x96 逻辑像素圆形表盘，与姿态球完全一致)
            float diameter = 96f * s;
            RectTransform.sizeDelta = new Vector2(diameter, diameter);

            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 2. 外部圆形深色底盘 (GPU 矢量网格)
            _dialBezel = CreateChild<ProceduralEngineBezelImage>("Engine_Bezel", transform, new Vector2(diameter, diameter), Vector2.zero);
            _dialBezel.raycastTarget = false;

            // 3. 顶部微型标题 (支持自定义)
            _titleText = UIFactory.CreateText(transform, "Title_Text", _titleCustom, Mathf.RoundToInt(7.5f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            _titleText.fontStyle = FontStyle.Bold;
            RectTransform titRt = _titleText.rectTransform;
            titRt.sizeDelta = new Vector2(diameter, 14f * s);
            titRt.anchoredPosition = new Vector2(0f, (diameter * 0.5f) - 6f * s);

            // 4. 发动机集群挂载容器
            RectTransform contRt = CreateContainer("Cluster_Container", transform, new Vector2(diameter, diameter), Vector2.zero);
            GameObject contGo = contRt.gameObject;
            _clusterContainer = contGo.transform;

            // 5. 底部状态读数 (如 "5 / 6 ACTIVE" 或 "SECO")
            _statusText = UIFactory.CreateText(transform, "Status_Text", I18n.Tr("WIDGET_SPX_ACTIVE_IDLE", "-- / -- 运行中"), Mathf.RoundToInt(7f * s), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _statusText.fontStyle = FontStyle.Bold;
            RectTransform statRt = _statusText.rectTransform;
            statRt.sizeDelta = new Vector2(diameter, 12f * s);
            statRt.anchoredPosition = new Vector2(0f, (-diameter * 0.5f) + 6f * s);

            // 初始构建默认星舰 6 发猛禽布局
            RebuildEngineLayout(6, s, theme);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Engine Dial Background", _bgImage.gameObject, "SpaceX发动机集群表盘底板", t => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, t)));
            if (_dialBezel != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel", "Dial Bezel", _dialBezel.gameObject, "发动机圆形深色底盘", t => { if (_dialBezel != null) _dialBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep); }));
            }
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "title", "Title", _titleText != null ? _titleText.gameObject : null));
            if (_clusterContainer != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "engines_cluster", "Engine Cluster", _clusterContainer.gameObject, "发动机喷管集群挂载容器"));
            }
            this.Controls.Register(new WidgetReadoutControl("status_readout", "底部点火/关机状态读数", _statusText != null ? _statusText.gameObject : null, _statusText, null, TextStyleRole.SecondaryValue, "{THR}"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void RebuildEngineLayout(int totalEngines, float s, ThemeConfig theme)
        {
            // 清理旧节点
            for (int i = 0; i < _engineNodes.Count; i++)
            {
                if (_engineNodes[i].RootGo != null)
                {
                    Destroy(_engineNodes[i].RootGo);
                }
            }
            _engineNodes.Clear();

            int count = Mathf.Clamp(totalEngines > 0 ? totalEngines : 6, 1, 33);
            List<Vector2> positions = new List<Vector2>();
            List<float> diameters = new List<float>();

            if (count == 6)
            {
                // 星舰 6 发猛禽官方构型：
                // 中心 3 台海平面猛禽 (紧凑倒三角形，直径 11px)
                float rCenter = 10f * s;
                positions.Add(new Vector2(-rCenter * 0.866f, -rCenter * 0.5f));
                diameters.Add(11f * s);

                positions.Add(new Vector2(rCenter * 0.866f, -rCenter * 0.5f));
                diameters.Add(11f * s);

                positions.Add(new Vector2(0f, rCenter));
                diameters.Add(11f * s);

                // 外围 3 台真空猛禽 (大口径喷管，120° 辐射排列，直径 16px)
                float rVac = 25f * s;
                positions.Add(new Vector2(-rVac * 0.866f, rVac * 0.5f));
                diameters.Add(16f * s);

                positions.Add(new Vector2(rVac * 0.866f, rVac * 0.5f));
                diameters.Add(16f * s);

                positions.Add(new Vector2(0f, -rVac));
                diameters.Add(16f * s);
            }
            else if (count == 9)
            {
                // 猎鹰 9 号 Octaweb 构型 (中心 1 发 + 外环 8 发)
                positions.Add(Vector2.zero);
                diameters.Add(12f * s);

                float rRing = 24f * s;
                for (int i = 0; i < 8; i++)
                {
                    float angle = (i * 45f) * Mathf.Deg2Rad;
                    positions.Add(new Vector2(Mathf.Sin(angle) * rRing, Mathf.Cos(angle) * rRing));
                    diameters.Add(12f * s);
                }
            }
            else if (count == 1)
            {
                positions.Add(Vector2.zero);
                diameters.Add(20f * s);
            }
            else
            {
                // 自适应对称环形排布
                float rRing = 22f * s;
                float d = Mathf.Clamp(44f * s / count * 1.5f, 8f * s, 16f * s);
                for (int i = 0; i < count; i++)
                {
                    float angle = (i * (360f / count)) * Mathf.Deg2Rad;
                    positions.Add(new Vector2(Mathf.Sin(angle) * rRing, Mathf.Cos(angle) * rRing));
                    diameters.Add(d);
                }
            }

            // 实例化发动机节点
            for (int i = 0; i < positions.Count; i++)
            {
                Vector2 pos = positions[i];
                float d = diameters[i];

                RectTransform nodeRt = CreateContainer($"Engine_Node_{i}", _clusterContainer, new Vector2(d, d), pos);
                GameObject nodeGo = nodeRt.gameObject;

                // 外环喷管轮廓 (未启动时深灰色)
                GameObject ringGo = UIFactory.CreatePanel(nodeGo.transform, "Nozzle_Ring", new Vector2(d, d), Vector2.zero,
                    WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep),
                    WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Medium), 1f * s);
                Image ringImg = ringGo.GetComponent<Image>();

                // 核心发光喷流
                GameObject coreGo = UIFactory.CreatePanel(nodeGo.transform, "Plume_Core", new Vector2(d * 0.75f, d * 0.75f), Vector2.zero,
                    theme.AccentPrimary);
                Image coreImg = coreGo.GetComponent<Image>();
                coreGo.SetActive(false);

                _engineNodes.Add(new EngineNodeUI
                {
                    RootGo = nodeGo,
                    BaseRing = ringImg,
                    CoreLight = coreImg,
                    NormalizedPos = pos,
                    IsActive = false
                });
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            if (_dialBezel != null)
            {
                _dialBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
            }

            ApplyText(_titleText, TextStyleRole.Label, theme);
            ApplyText(_statusText, TextStyleRole.SecondaryValue, theme);

            Color activeColor = style.GetMeterColor(MeterStyleRole.Primary, theme);
            Color inactiveColor = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);

            for (int i = 0; i < _engineNodes.Count; i++)
            {
                var node = _engineNodes[i];
                if (node.BaseRing != null)
                {
                    node.BaseRing.color = node.IsActive ? activeColor : inactiveColor;
                }
                if (node.CoreLight != null)
                {
                    node.CoreLight.color = activeColor;
                }
            }
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            SpaceXEngineState state = _logic.CurrentState;
            if (!state.HasVessel) return;

            int activeEngines = state.ActiveEngines;
            int totalEngines = state.TotalEngines;
            float throttle = state.Throttle;

            // 脏标记检查
            bool actDirty = _cachedActiveEngines.Update(activeEngines);
            bool totDirty = _cachedTotalEngines.Update(totalEngines);
            bool thrDirty = _cachedThrottle.Update(throttle);
            bool strDirty = _lastStatusStr.Update(state.StatusText);

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;
            float s = CurrentDpiScale;

            // 若当前级发动机总数变动，自适应重构排布
            if (totDirty || _engineNodes.Count != totalEngines)
            {
                RebuildEngineLayout(totalEngines, s, theme);
            }

            if (!actDirty && !totDirty && !thrDirty && !strDirty)
            {
                return;
            }

            int litCount = state.LitCount;
            float throttleScale = state.ThrottleScale;
            Color activeColor = state.IsFlameout
                ? WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Danger, theme)
                : WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Primary, theme);

            for (int i = 0; i < _engineNodes.Count; i++)
            {
                var node = _engineNodes[i];
                bool shouldLight = (i < litCount);

                // 星舰 6 发特殊处理：若 5 发点火，点亮前 5 发，保留下方外圈真空猛禽 (索引 5) 关机
                if (totalEngines == 6 && litCount == 5)
                {
                    shouldLight = (i != 5);
                }

                if (node.IsActive != shouldLight)
                {
                    node.IsActive = shouldLight;
                    if (node.CoreLight != null)
                    {
                        node.CoreLight.gameObject.SetActive(shouldLight);
                    }
                }

                // 核心羽流光斑随油门动态平滑缩放
                if (node.CoreLight != null && shouldLight)
                {
                    if (node.LastScale.Update(throttleScale))
                    {
                        node.CoreLight.transform.localScale = new Vector3(throttleScale, throttleScale, 1f);
                    }
                    node.CoreLight.color = activeColor;
                }

                if (node.BaseRing != null)
                {
                    node.BaseRing.color = shouldLight
                        ? activeColor
                        : WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
                }
            }

            // 更新状态文案 (支持自定义与脏缓存)
            if (_statusText != null && strDirty)
            {
                _statusText.SetTextSafe(state.StatusText);
            }
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < _engineNodes.Count; i++)
            {
                if (_engineNodes[i]?.RootGo != null)
                {
                    Destroy(_engineNodes[i].RootGo);
                }
            }
            _engineNodes.Clear();
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// GPU 程序化发动机圆形底盘图元 (零 CPU 软件光栅化，纯代码 GPU 几何网格，SPEC-002)
    /// </summary>
    public class ProceduralEngineBezelImage : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            if (radius <= 0.001f) return;
            Vector2 center = r.center;
            const int segments = 48;

            Color cDisc = color;
            Color cRing = WidgetStyleManager.Lighten(cDisc, 0.35f);

            // 内部深色底盘
            float rInner = radius * 0.92f;
            int centerIdx = vh.currentVertCount;
            vh.AddVert(center, cDisc, Vector2.zero);

            for (int i = 0; i <= segments; i++)
            {
                float deg = i * (360f / segments);
                float rad = deg * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(rad) * rInner, Mathf.Sin(rad) * rInner);
                vh.AddVert(pos, cDisc, Vector2.zero);
                if (i > 0)
                {
                    vh.AddTriangle(centerIdx, centerIdx + i, centerIdx + i + 1);
                }
            }

            // 外部高光边缘环
            for (int i = 0; i <= segments; i++)
            {
                float deg = i * (360f / segments);
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                Vector2 vIn = center + new Vector2(cos * rInner, sin * rInner);
                Vector2 vOut = center + new Vector2(cos * radius, sin * radius);

                vh.AddVert(vIn, cDisc, Vector2.zero);
                vh.AddVert(vOut, cRing, Vector2.zero);

                if (i > 0)
                {
                    int baseIdx = centerIdx + 1 + (segments + 1) + (i - 1) * 2;
                    vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                    vh.AddTriangle(baseIdx + 3, baseIdx + 2, baseIdx);
                }
            }
        }
    }
}
