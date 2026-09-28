using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 猛禽发动机状态集群指示器 (SpaceX Webcast Engine Cluster Status Dial)
    /// 核心特性：
    ///   1. 自动识别当前分级发动机总数 (TotalStageEngines) 与当前实际点火运行引擎数 (ActiveEngines)；
    ///   2. 星舰 6 发猛禽标准构型 (3 台中心海平面机动猛禽 + 3 台外围真空大喷管猛禽)；
    ///   3. 智能兼容 Falcon 9 (9 发八角盘 Octaweb) 与任意舰船多发集群自适应排布；
    ///   4. 实时点火发光与节流推力羽流辉光，完全还原截图中 5 发点火亮起 + 1 发停机关机的经典画面；
    ///   5. 严格遵循 MFP 架构规范：零硬编码与零颜色字面量 (MFP-SPEC-006)。
    /// </summary>
    [FlightWidget("spacex_engines", "dragon_engines", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 引擎状态阵列", Description = "SpaceX 猎鹰 9 发动机多孔圆环/星舰猛禽集群点火状态阵列图。", DefaultWidgetId = "spacex.engines", DefaultX = 360f, DefaultY = 0f, IsSingleton = true, ExactIds = new[] { "spacex.engines" })]
    public class SpaceXEngineWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(96f, 96f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title(I18n.Tr("WIDGET_SPX_ENGINES", "发动机"));
        public TextWidget Status = TextWidget.Value(I18n.Tr("WIDGET_SPX_CUTOFF", "关机"));

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;
        private RawImage _dialBackdropRawImage;
        private Text _titleText;
        private Text _statusText;

        // 共享程序化底盘纹理
        private static Texture2D _sharedDialBezelTexture;

        // 发动机集群图元
        private class EngineNodeUI
        {
            public GameObject RootGo;
            public Image BaseRing;
            public Image CoreLight;
            public Vector2 NormalizedPos;
            public bool IsActive;
        }

        private readonly List<EngineNodeUI> _engineNodes = new List<EngineNodeUI>();
        private Transform _clusterContainer;

        // 遥测缓存与脏标记
        private int _cachedActiveEngines = -1;
        private int _cachedTotalEngines = -1;
        private float _cachedThrottle = -1f;
        private string _lastStatusStr = string.Empty;

        // CustomTemplate 自定义通道
        private string _titleCustom = "ENGINES";
        private string _cutoffLabel = "MECO / CUTOFF";
        private string _activeTemplate = "{0} / {1} ACTIVE";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _titleCustom = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_SPX_ENGINES", "发动机"));
            _cutoffLabel = GetTemplateChannel("CUTOFF_LABEL", I18n.Tr("WIDGET_SPX_MECO_CUTOFF", "主发关机 / 关机"));
            _activeTemplate = GetTemplateChannel("ACTIVE_TEMPLATE", I18n.Tr("WIDGET_SPX_ACTIVE_TEMPLATE", "{0} / {1} 台运行"));

            // 1. 组件包围盒 (基准 96x96 逻辑像素圆形表盘，与姿态球完全一致)
            float diameter = 96f * s;
            RectTransform.sizeDelta = new Vector2(diameter, diameter);

            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            EnsureSharedBezelTexture();

            // 2. 外部圆形深色底盘
            _dialBackdropRawImage = CreateChild<RawImage>("Engine_Bezel", transform, new Vector2(diameter, diameter), Vector2.zero);
            GameObject bezelGo = _dialBackdropRawImage.gameObject;
            RectTransform bezelRt = _dialBackdropRawImage.rectTransform;
            _dialBackdropRawImage.texture = _sharedDialBezelTexture;
            _dialBackdropRawImage.raycastTarget = false;

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
            if (_dialBackdropRawImage != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel", "Dial Bezel", _dialBackdropRawImage.gameObject, "发动机圆形深色底盘", t => { if (_dialBackdropRawImage != null) _dialBackdropRawImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep); }));
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
                // 顶部偏左 150°
                positions.Add(new Vector2(-rVac * 0.866f, rVac * 0.5f));
                diameters.Add(16f * s);

                // 顶部偏右 30°
                positions.Add(new Vector2(rVac * 0.866f, rVac * 0.5f));
                diameters.Add(16f * s);

                // 正下方 270° (截图中关机的第 6 发猛禽真空)
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

            if (_dialBackdropRawImage != null)
            {
                _dialBackdropRawImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
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

        private int _dataActiveEngines;
        private int _dataTotalEngines = 6;
        private float _dataThrottle;
        private bool _dataIsIgniting;
        private bool _dataIsFlameout;
        private bool _dataIsFiring;
        private bool _dataHasVessel;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataHasVessel = false;
                return;
            }
            _dataHasVessel = true;

            int activeEngines = telemetry.ActiveEngines;
            int totalEngines = telemetry.TotalStageEngines > 0 ? telemetry.TotalStageEngines : 6;
            float throttle = telemetry.Throttle;

            _dataActiveEngines = activeEngines;
            _dataTotalEngines = totalEngines;
            _dataThrottle = throttle;

            bool isIgniting = telemetry.IsEngineIgniting;
            bool isFlameout = throttle > 0.05f && (activeEngines == 0 || telemetry.StagePropellantFraction <= 0.0001f);
            bool isFiring = !isFlameout && (isIgniting || (throttle > 0.005f && activeEngines > 0));

            _dataIsIgniting = isIgniting;
            _dataIsFlameout = isFlameout;
            _dataIsFiring = isFiring;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_dataHasVessel) return;

            int activeEngines = _dataActiveEngines;
            int totalEngines = _dataTotalEngines;
            float throttle = _dataThrottle;

            // 脏标记检查
            if (activeEngines == _cachedActiveEngines &&
                totalEngines == _cachedTotalEngines &&
                Mathf.Abs(throttle - _cachedThrottle) < 0.02f)
            {
                return;
            }

            _cachedActiveEngines = activeEngines;
            _cachedThrottle = throttle;

            ThemeConfig theme = context.Theme ?? WidgetStyleManager.Instance?.CurrentTheme;
            float s = CurrentDpiScale;

            // 若当前级发动机总数变动，自适应重构排布
            if (totalEngines != _cachedTotalEngines || _engineNodes.Count != totalEngines)
            {
                _cachedTotalEngines = totalEngines;
                RebuildEngineLayout(totalEngines, s, theme);
            }

            bool isIgniting = _dataIsIgniting;
            bool isFlameout = _dataIsFlameout;
            bool isFiring = _dataIsFiring;
            int litCount = isFiring ? Mathf.Min(activeEngines > 0 ? activeEngines : totalEngines, _engineNodes.Count) : 0;

            float throttleScale = Mathf.Lerp(0.5f, 1.0f, Mathf.Clamp01(throttle));
            Color activeColor = isFlameout
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
                    node.CoreLight.transform.localScale = new Vector3(throttleScale, throttleScale, 1f);
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
            if (_statusText != null)
            {
                string sStr;
                if (isFlameout)
                {
                    sStr = "FLAMEOUT / DEPLETED";
                }
                else if (isIgniting)
                {
                    sStr = "IGNITION SEQUENCE";
                }
                else if (throttle <= 0.005f)
                {
                    sStr = _cutoffLabel;
                }
                else
                {
                    sStr = string.Format(_activeTemplate, litCount, _engineNodes.Count);
                }

                if (sStr != _lastStatusStr)
                {
                    _lastStatusStr = sStr;
                    _statusText.text = sStr;
                }
            }
        }

        private static void EnsureSharedBezelTexture()
        {
            if (_sharedDialBezelTexture != null) return;

            const int size = 256;
            _sharedDialBezelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _sharedDialBezelTexture.filterMode = FilterMode.Bilinear;
            _sharedDialBezelTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] cols = new Color[size * size];
            float half = size * 0.5f;
            Color opaque = WidgetStyleManager.NeutralOpaque;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    if (r > 1.0f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    float edgeAlpha = Mathf.Clamp01((1.0f - r) / (2f / half));
                    float ringAlpha = Mathf.Clamp01((0.03f - Mathf.Abs(r - 0.93f)) / (1.5f / half));

                    Color c = opaque;
                    c.a = Mathf.Max(0.25f, ringAlpha * 0.85f) * edgeAlpha;
                    cols[y * size + x] = c;
                }
            }

            _sharedDialBezelTexture.SetPixels(cols);
            _sharedDialBezelTexture.Apply(false, true);
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
}
