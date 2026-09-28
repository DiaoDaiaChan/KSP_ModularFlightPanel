using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 轨道六根数全息态势与动力学面板 (Holographic Orbital Elements & Dynamics Widget)
    /// 双模态现代航电仪表：
    /// ● 精简模式 (COMPACT)：模块化深色插槽卡片，一览开普勒 6 根数 + 拱点倒计时与能量胶囊。
    /// ● 完整模式 (FULL)：高精度全息伪 3D 空间轨道图 (Holographic 3D Orbit Diagram)：
    ///   - 正交轴测相机 (camYaw / camPitch) 投影，按**相机空间深度**做前后分层：
    ///     近侧半周为高亮发光实线，远侧半周为纤细暗线，中央实体行星球体严格遮挡远侧轨道段。
    ///   - 赤道基准参考平面盘 (Equatorial Reference Disk) 与惯性参考轴 X / Y / Z。
    ///   - 空间开普勒圆锥曲线 (极坐标方程 + 倾角/升交点/近拱点三轴正交基底旋转)。
    ///   - 闭合椭圆按远拱点归一化；双曲线/抛物线按 ν∞ 开区间采样并冲出画幅，绝不虚假闭合。
    ///   - 严格在数学定义上精准标记：
    ///     1. a (半长轴) 与 e (离心率) 形状
    ///     2. i (轨道倾角，轨道面与赤道面的空间夹角，含升交点处真实二面角弧)
    ///     3. Ω / LAN (升交点赤经，基准轴至升交点方向的赤道面夹角弧)
    ///     4. ω / AOP (近拱点辐角，升交点至近拱点的轨道面夹角弧)
    ///     5. ν / TA (真近点角，近拱点至飞船矢径的夹角弧)
    ///     6. PE (近拱点)、AP (远拱点)、AN (升交点)、DN (降交点) 与航天器矢量微标
    ///   - 六根数语义色谱 (参数读数与其对应划线强制同色，密集图元可一眼对应)：
    ///     a 翡翠绿 (轨道曲线/拱线/AP·PE) · e 琥珀金 (偏心率矢量) · i 冰蓝 (倾角弧 ×2 + 角动量 h)
    ///     Ω 品红 (Ω 弧/升交线/AN·DN) · ω 青碧 (ω 弧) · ν 紫罗兰 (ν 弧 + 位置矢径 r)
    ///     速度矢量 v 取中性近白；航天器本体沿用正向状态色；赤道盘与 XYZ 轴为中性基准。
    /// 100% 遵照 SPEC-001..008 核心架构规范，0 颜色字面量，0 场景查询。
    /// </summary>
    [FlightWidget("orbital_elements", "orbit_elements", "orbital_3d",
        Category = WidgetCategory.Navigation,
        DisplayName = "ORBITAL ELEMENTS 轨道六根数面板",
        Description = "轨道六根数全息面板：支持精简航电卡槽与完整伪3D轨道球双模态切换，精准渲染开普勒力学全量六根数与空间交点。",
        DefaultWidgetId = "nav.orbital_elements",
        DefaultX = 270f,
        DefaultY = 180f,
        IsSingleton = true,
        ExactIds = new[] { "nav.orbital_elements" })]
    public class OrbitalElementsWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;
        protected override bool AutoCreateCardFrame => true;

        // ── 数据心跳与 UI 绘制解耦状态缓存 ──
        private bool _hasVessel = false;
        private double _dataAp = double.NaN;
        private double _dataPe = double.NaN;
        private double _dataTAp = double.NaN;
        private double _dataTPe = double.NaN;
        private double _dataSma = double.NaN;
        private double _dataEcc = double.NaN;
        private double _dataInc = double.NaN;
        private double _dataLan = double.NaN;
        private double _dataAop = double.NaN;
        private double _dataTra = double.NaN;
        private double _dataPeriod = double.NaN;
        private string _dataBadgeText = "---";
        private TextStyleRole _dataBadgeRole = TextStyleRole.Muted;
        private string _dataTitleText = "ORBIT ELEMENTS";

        // ── 性能节流与状态缓存 ──
        private float _lastMeshRebuildTime = -1f;
        private float _cachedDpiScale = 1f; // DPI 缩放系数缓存 (供 PopulateOrbitMesh 使用，避免在 OnPopulateMesh 回调中查询 Unity API)

        // ── 尺寸规格：精简模式 290×116，完整模式 400×440 ──
        private static readonly Vector2 CompactSize = new Vector2(290f, 116f);
        private static readonly Vector2 FullSize = new Vector2(400f, 440f);
        public override Vector2 BaseSize => _isFullMode ? FullSize : CompactSize;

        // ── DSL 声明式微控件 (基类全自动构建与主题纳管) ──
        public TextWidget Title = TextWidget.Title(I18n.Tr("ORBIT_TITLE", "轨道要素"));
        public TextWidget OrbitBadge = TextWidget.Badge("---");

        // ── 模式状态 ──
        private bool _isFullMode = false;

        // ── 根节点容器 ──
        private GameObject _compactRoot;
        private GameObject _fullRoot;

        // ── 模式切换胶囊按钮 ──
        private Button _modeButton;
        private Image _modeBtnBg;
        private Outline _modeBtnOutline;
        private Text _modeBtnLabel;

        // ── 精简模式 UI 元件 ──
        private Text _apVal, _peVal, _tApPeReadout;
        private Text _smaVal, _eccVal, _incVal, _incDirVal;
        private Text _lanVal, _aopVal, _taVal, _perVal;

        // ── 完整模式 3D 矢量视口 (GPU 矢量网格渲染，0 CPU 像素光栅化) ──
        private GameObject _globeRoot;
        private OrbitalDiagramGraphic _diagramGraphic;

        // ── 动静分离：动态航天器图元硬件覆盖层 (0 CPU 网格重建) ──
        private GameObject _scMarker;
        private RectTransform _scMarkerRt;
        private Image _scMarkerImg;
        private Image _scMarkerDotImg;
        private Outline _scOutline;
        private GameObject _scRadiusLine;
        private RectTransform _scRadiusLineRt;
        private Image _scRadiusLineImg;
        private GameObject _scVelocityArrow;
        private RectTransform _scVelocityArrowRt;
        private Image _scVelocityArrowImg;
        private GameObject _scVelocityHead;
        private RectTransform _scVelocityHeadRt;
        private Image _scVelocityHeadImg;

        // ── 3D 开普勒权威拓扑 UGUI 矢量标注层 (0 模糊，硬件 Outline 描边防背景重叠) ──
        private GameObject _labelsRoot;
        private Text _lblAxisX;
        private Text _lblAxisY;
        private Text _lblAxisZ;
        private Text _lblEquator;
        private Text _lblOrigin;
        private Text _lblNodeAn;
        private Text _lblNodeDn;
        private Text _lblArcOmega;
        private Text _lblArcInc;
        private Text _lblVectorH;
        private Text _lblVectorE;
        private Text _lblArcAop;
        private Text _lblApoapsis;
        private Text _lblPeriapsis;
        private Text _lblArcTa;
        private Text _lblVectorR;
        private Text _lblVectorV;
        private Text _lblSpacecraft;

        // ── 读数防抖与字符串缓存 (避免逐帧 GC 与 Text 网格脏化) ──
        private double _lastCompactAp = double.NaN;
        private double _lastCompactPe = double.NaN;
        private int _lastCompactSec = -1;
        private double _lastCompactSma = double.NaN;
        private double _lastCompactEcc = double.NaN;
        private double _lastCompactInc = double.NaN;
        private double _lastCompactLan = double.NaN;
        private double _lastCompactAop = double.NaN;
        private double _lastCompactTa = double.NaN;
        private double _lastCompactPer = double.NaN;

        private double _lastFullAp = double.NaN;
        private double _lastFullPe = double.NaN;
        private int _lastFullSec = -1;
        private double _lastFullSma = double.NaN;
        private double _lastFullEcc = double.NaN;
        private double _lastFullLan = double.NaN;
        private double _lastFullAop = double.NaN;
        private double _lastFullInc = double.NaN;
        private double _lastFullTa = double.NaN;
        private double _lastFullPer = double.NaN;

        // ── 每帧复用的采样缓冲 (几何图元不再逐帧分配数组) ──
        private readonly Vector2[] _orbitPts = new Vector2[385];
        private readonly bool[] _orbitFront = new bool[385];
        private readonly Vector2[] _diskPts = new Vector2[129];

        private double _lastDrawnAp = double.NaN;
        private double _lastDrawnPe = double.NaN;

        // ── 完整模式四角微卡槽读数 ──
        private Text _fApVal, _fPeVal, _fTimeVal;
        private Text _fSmaVal, _fEccVal, _fPeriodVal;
        private Text _fLanVal, _fAopVal;
        private Text _fIncVal, _fTaVal;

        // ── 主题语义颜色缓存 (SPEC-006: 零颜色字面量) ──
        // 六根数"参数读数 ↔ 对应划线"强制同色，密集图元下可一眼对应 (色谱见 ResolveElementColors)：
        //   a 翡翠绿 / e 琥珀金 / i 冰蓝 / Ω 品红 / ω 青碧 / ν 紫罗兰
        private Color32 _cClear;
        private Color32 _cPlanetSun;
        private Color32 _cPlanetShadow;
        private Color32 _cPlanetGrid;
        private Color32 _cEquatorPlane;
        private Color32 _cOrbitFront;
        private Color32 _cOrbitBack;
        private Color32 _cOrbitGlow;
        private Color32 _cVessel;
        private Color32 _cVesselGlow;
        private Color32 _cApPe;
        private Color32 _cNode;
        private Color32 _cElemI;
        private Color32 _cElemLan;
        private Color32 _cElemAop;
        private Color32 _cElemTa;
        private Color32 _cAxis;
        private Color32 _cVectorH;
        private Color32 _cVectorE;
        private Color32 _cVectorR;
        private Color32 _cVectorV;
        private Color32 _cLabelText;

        // ── 渲染脏标记防抖 (防止无意义像素重绘) ──
        private double _lastDrawnSma = double.NaN;
        private double _lastDrawnEcc = double.NaN;
        private double _lastDrawnInc = double.NaN;
        private double _lastDrawnLan = double.NaN;
        private double _lastDrawnAop = double.NaN;
        private double _lastDrawnTra = double.NaN;

        private const double DefaultKerbinRadius = 600000.0;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            _cachedDpiScale = s;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 尽早烘焙语义色缓存 (确保后续微标与硬件图元初始化时获得有效色彩)
            CacheThemeColors(theme);

            RectTransform.sizeDelta = CompactSize * s;

            // ═════════════════════════════════════════════════════════════════
            // 1. 模式切换按钮 (顶部右侧高亮小药丸按键)
            // ═════════════════════════════════════════════════════════════════
            float btnW = 54f * s;
            float btnH = 16f * s;
            float btnX = 18f * s;
            float btnY = (CompactSize.y * 0.5f - 14f) * s;

            GameObject btnGo = UIFactory.CreatePanel(transform, "Mode_Toggle_Btn", new Vector2(btnW, btnH),
                new Vector2(btnX, btnY), WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme));
            _modeButton = btnGo.AddComponent<Button>();
            _modeButton.onClick.AddListener(OnModeToggle);
            _modeBtnBg = btnGo.GetComponent<Image>();
            _modeBtnBg.material = style.GetUiMaterial(isText: false);

            _modeBtnOutline = btnGo.AddComponent<Outline>();
            _modeBtnOutline.effectDistance = new Vector2(0.8f * s, 0.8f * s);
            _modeBtnOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.6f);

            _modeBtnLabel = UIFactory.CreateText(btnGo.transform, "Label", I18n.Tr("ORBIT_BTN_3D", "3D 空间球"),
                Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform btnLblRt = _modeBtnLabel.rectTransform;
            btnLblRt.anchorMin = Vector2.zero; btnLblRt.anchorMax = Vector2.one;
            btnLblRt.sizeDelta = Vector2.zero; btnLblRt.anchoredPosition = Vector2.zero;

            // ═════════════════════════════════════════════════════════════════
            // 2. 精简模式容器 (Compact Mode: 双嵌合卡槽 + 底部角度通栏)
            // ═════════════════════════════════════════════════════════════════
            RectTransform cRt = CreateContainer("Compact_Root", transform, Vector2.zero, Vector2.zero);
            _compactRoot = cRt.gameObject;
            cRt.anchorMin = Vector2.zero; cRt.anchorMax = Vector2.one;

            Color slotBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme);
            Color slotBorder = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost, theme);
            Color labelCol = style.GetTextColor(TextStyleRole.Label, theme);
            Color primCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color secCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            Color unitCol = style.GetTextColor(TextStyleRole.Unit, theme);

            int fsLbl = Mathf.Max(6, Mathf.RoundToInt(8f * s));
            int fsVal = Mathf.Max(8, Mathf.RoundToInt(10.5f * s));
            int fsSmall = Mathf.Max(6, Mathf.RoundToInt(7.5f * s));

            // 左卡槽：拱点几何与倒计时 (w: 132, h: 54, pos: -69, 10)
            GameObject leftSlot = CreateAvionicsSlot(_compactRoot.transform, "Left_Apsides_Slot",
                new Vector2(132f * s, 54f * s), new Vector2(-69f * s, 9f * s), slotBg, slotBorder, s);

            CreateLabelValPair(leftSlot.transform, "AP", -42f * s, 14f * s, 32f * s, 14f * s,
                22f * s, 14f * s, 76f * s, 14f * s, I18n.Tr("ORBIT_LABEL_AP", "AP"), "---", fsLbl, fsVal, labelCol, primCol, out _, out _apVal);
            CreateLabelValPair(leftSlot.transform, "PE", -42f * s, -1f * s, 32f * s, 14f * s,
                22f * s, -1f * s, 76f * s, 14f * s, I18n.Tr("ORBIT_LABEL_PE", "PE"), "---", fsLbl, fsVal, labelCol, primCol, out _, out _peVal);

            _tApPeReadout = UIFactory.CreateText(leftSlot.transform, "T_ApPe", I18n.Tr("WIDGET_NAV_T_AP_PE_PLACEHOLDER", "T-AP --:--  PE --:--"),
                fsSmall, TextAnchor.MiddleCenter, secCol);
            SetRect(_tApPeReadout.rectTransform, 0f, -17f * s, 122f * s, 13f * s);

            // 右卡槽：轨道形态几何 (w: 132, h: 54, pos: 69, 10)
            GameObject rightSlot = CreateAvionicsSlot(_compactRoot.transform, "Right_Shape_Slot",
                new Vector2(132f * s, 54f * s), new Vector2(69f * s, 9f * s), slotBg, slotBorder, s);

            CreateLabelValPair(rightSlot.transform, "SMA", -42f * s, 14f * s, 32f * s, 14f * s,
                22f * s, 14f * s, 76f * s, 14f * s, I18n.Tr("ORBIT_LABEL_SMA", "SMA"), "---", fsLbl, fsVal, labelCol, primCol, out _, out _smaVal);
            CreateLabelValPair(rightSlot.transform, "ECC", -42f * s, -1f * s, 32f * s, 14f * s,
                22f * s, -1f * s, 76f * s, 14f * s, I18n.Tr("ORBIT_LABEL_ECC", "ECC"), "0.000", fsLbl, fsVal, labelCol, primCol, out _, out _eccVal);
            CreateLabelValPair(rightSlot.transform, "INC", -42f * s, -17f * s, 32f * s, 14f * s,
                10f * s, -17f * s, 50f * s, 14f * s, I18n.Tr("ORBIT_LABEL_INC", "INC"), "0.0°", fsLbl, fsVal, labelCol, primCol, out _, out _incVal);

            _incDirVal = UIFactory.CreateText(rightSlot.transform, "INC_DIR", I18n.Tr("ORBIT_DIR_PRO", "顺行"), fsSmall, TextAnchor.MiddleRight, unitCol);
            SetRect(_incDirVal.rectTransform, 48f * s, -17f * s, 24f * s, 13f * s);

            // 底部横槽：开普勒空间三姿态角 + 周期 (w: 270, h: 22, pos: 0, -38)
            GameObject bottomSlot = CreateAvionicsSlot(_compactRoot.transform, "Bottom_Angles_Slot",
                new Vector2(270f * s, 22f * s), new Vector2(0f, -38f * s), slotBg, slotBorder, s);

            CreateLabelValPair(bottomSlot.transform, "LAN", -120f * s, 0f, 16f * s, 14f * s,
                -92f * s, 0f, 40f * s, 14f * s, I18n.Tr("ORBIT_ELEM_LAN", "Ω"), "---°", fsSmall, fsSmall, labelCol, secCol, out _, out _lanVal);
            CreateLabelValPair(bottomSlot.transform, "AOP", -53f * s, 0f, 16f * s, 14f * s,
                -25f * s, 0f, 40f * s, 14f * s, I18n.Tr("ORBIT_ELEM_AOP", "ω"), "---°", fsSmall, fsSmall, labelCol, secCol, out _, out _aopVal);
            CreateLabelValPair(bottomSlot.transform, "TA", 15f * s, 0f, 16f * s, 14f * s,
                43f * s, 0f, 40f * s, 14f * s, I18n.Tr("ORBIT_ELEM_TA", "ν"), "---°", fsSmall, fsSmall, labelCol, primCol, out _, out _taVal);
            CreateLabelValPair(bottomSlot.transform, "PER", 80f * s, 0f, 22f * s, 14f * s,
                110f * s, 0f, 38f * s, 14f * s, I18n.Tr("ORBIT_LABEL_PER", "PER"), "--:--", fsSmall, fsSmall, labelCol, unitCol, out _, out _perVal);

            // ═════════════════════════════════════════════════════════════════
            // 3. 完整模式容器 (Full Mode: 全息 3D 轨道球 + 四角悬浮卡槽)
            // ═════════════════════════════════════════════════════════════════
            RectTransform fRt = CreateContainer("Full_Root", transform, Vector2.zero, Vector2.zero);
            _fullRoot = fRt.gameObject;
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
            _fullRoot.SetActive(false);

            // 中央全息球视口 (UGUI 矢量网格渲染，大幅拓宽中央视口占比)
            float globeBoxW = 388f * s;
            float globeBoxH = 376f * s;
            GameObject globeBox = CreateAvionicsSlot(_fullRoot.transform, "Globe_Viewport_Frame",
                new Vector2(globeBoxW, globeBoxH), new Vector2(0f, -14f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme), slotBorder, s);

            _diagramGraphic = CreateChild<OrbitalDiagramGraphic>("Globe_Graphic", globeBox.transform,
                new Vector2(globeBoxW - 4f * s, globeBoxH - 4f * s), Vector2.zero);
            _globeRoot = _diagramGraphic.gameObject;
            _diagramGraphic.Widget = this;
            _diagramGraphic.raycastTarget = false;

            // 3D 权威开普勒拓扑 UGUI 矢量标注层 (挂载在网格上方，带背景描边防重叠)
            _labelsRoot = new GameObject("Diagram_Labels", typeof(RectTransform));
            _labelsRoot.transform.SetParent(globeBox.transform, false);
            var lblRt = _labelsRoot.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.sizeDelta = Vector2.zero;
            lblRt.anchoredPosition = Vector2.zero;

            _lblAxisX = CreateDiagramLabel(_labelsRoot.transform, "Lbl_AxisX", I18n.Tr("ORBIT_DIAG_X", "X (春分点)"), _cAxis, s, 9);
            _lblAxisY = CreateDiagramLabel(_labelsRoot.transform, "Lbl_AxisY", I18n.Tr("ORBIT_DIAG_Y", "Y"), _cAxis, s, 9);
            _lblAxisZ = CreateDiagramLabel(_labelsRoot.transform, "Lbl_AxisZ", I18n.Tr("ORBIT_DIAG_Z", "Z"), _cAxis, s, 9);
            _lblEquator = CreateDiagramLabel(_labelsRoot.transform, "Lbl_Equator", I18n.Tr("ORBIT_DIAG_EQ", "赤道面"), _cAxis, s, 8);
            _lblOrigin = CreateDiagramLabel(_labelsRoot.transform, "Lbl_Origin", I18n.Tr("ORBIT_DIAG_ORIGIN", "O"), _cAxis, s, 9);
            _lblNodeAn = CreateDiagramLabel(_labelsRoot.transform, "Lbl_NodeAN", I18n.Tr("ORBIT_DIAG_AN", "AN (升交点)"), _cNode, s, 9);
            _lblNodeDn = CreateDiagramLabel(_labelsRoot.transform, "Lbl_NodeDN", I18n.Tr("ORBIT_DIAG_DN", "DN (降交点)"), _cNode, s, 8);
            _lblArcOmega = CreateDiagramLabel(_labelsRoot.transform, "Lbl_ArcOmega", I18n.Tr("ORBIT_ELEM_LAN", "Ω"), _cElemLan, s, 11);
            _lblArcInc = CreateDiagramLabel(_labelsRoot.transform, "Lbl_ArcInc", I18n.Tr("ORBIT_ELEM_INC", "i"), _cElemI, s, 11);
            _lblVectorH = CreateDiagramLabel(_labelsRoot.transform, "Lbl_VectorH", "h", _cVectorH, s, 9);
            _lblVectorE = CreateDiagramLabel(_labelsRoot.transform, "Lbl_VectorE", "e", _cVectorE, s, 9);
            _lblArcAop = CreateDiagramLabel(_labelsRoot.transform, "Lbl_ArcAop", I18n.Tr("ORBIT_ELEM_AOP", "ω"), _cElemAop, s, 11);
            _lblPeriapsis = CreateDiagramLabel(_labelsRoot.transform, "Lbl_PE", I18n.Tr("ORBIT_DIAG_PE", "PE (近拱点)"), _cApPe, s, 9);
            _lblApoapsis = CreateDiagramLabel(_labelsRoot.transform, "Lbl_AP", I18n.Tr("ORBIT_DIAG_AP", "AP (远拱点)"), _cApPe, s, 9);
            _lblArcTa = CreateDiagramLabel(_labelsRoot.transform, "Lbl_ArcTa", I18n.Tr("ORBIT_ELEM_TA", "ν"), _cElemTa, s, 11);
            _lblVectorR = CreateDiagramLabel(_labelsRoot.transform, "Lbl_VectorR", "r", _cVectorR, s, 9);
            _lblVectorV = CreateDiagramLabel(_labelsRoot.transform, "Lbl_VectorV", "v", _cVectorV, s, 9);
            _lblSpacecraft = CreateDiagramLabel(_labelsRoot.transform, "Lbl_Spacecraft", I18n.Tr("ORBIT_DIAG_SC", "SC (航天器)"), _cVessel, s, 9);

            // 动静分离：使用 BaseFlightWidget 标准硬件覆盖层图元 (0 CPU 网格重建，纯 Transform 偏移)
            _scRadiusLineImg = CreateHardwareMarker(globeBox.transform, "SC_Radius_Line", new Vector2(1f, 1.5f * s), _cVectorR);
            _scRadiusLine = _scRadiusLineImg.gameObject;
            _scRadiusLineRt = _scRadiusLineImg.rectTransform;
            _scRadiusLineRt.pivot = new Vector2(0f, 0.5f);
            _scRadiusLineImg.raycastTarget = false;
            _scRadiusLine.SetActive(false);

            _scVelocityArrowImg = CreateHardwareMarker(globeBox.transform, "SC_Velocity_Arrow", new Vector2(1f, 2.0f * s), _cVectorV);
            _scVelocityArrow = _scVelocityArrowImg.gameObject;
            _scVelocityArrowRt = _scVelocityArrowImg.rectTransform;
            _scVelocityArrowRt.pivot = new Vector2(0f, 0.5f);
            _scVelocityArrowImg.raycastTarget = false;
            _scVelocityArrow.SetActive(false);

            _scVelocityHead = new GameObject("SC_VelocityHead", typeof(RectTransform), typeof(Image));
            _scVelocityHead.transform.SetParent(_scVelocityArrow.transform, false);
            _scVelocityHeadRt = _scVelocityHead.GetComponent<RectTransform>();
            _scVelocityHeadRt.sizeDelta = new Vector2(6f * s, 6f * s);
            _scVelocityHeadRt.pivot = new Vector2(0.5f, 0.5f);
            _scVelocityHeadImg = _scVelocityHead.GetComponent<Image>();
            _scVelocityHeadImg.material = style.GetUiMaterial(isText: false);
            _scVelocityHeadImg.color = _cVectorV;
            _scVelocityHeadImg.raycastTarget = false;

            _scMarkerImg = CreateHardwareMarker(globeBox.transform, "SC_Marker", new Vector2(10f * s, 10f * s), _cVessel);
            _scMarker = _scMarkerImg.gameObject;
            _scMarkerRt = _scMarkerImg.rectTransform;
            _scMarkerRt.pivot = new Vector2(0.5f, 0.5f);
            _scMarkerImg.raycastTarget = false;
            _scOutline = _scMarker.AddComponent<Outline>();
            _scOutline.effectDistance = new Vector2(1.5f * s, 1.5f * s);
            _scOutline.effectColor = _cVesselGlow;

            var scDotGo = new GameObject("SC_Dot", typeof(RectTransform), typeof(Image));
            scDotGo.transform.SetParent(_scMarker.transform, false);
            var scDotRt = scDotGo.GetComponent<RectTransform>();
            scDotRt.sizeDelta = new Vector2(4f * s, 4f * s);
            scDotRt.anchoredPosition = Vector2.zero;
            _scMarkerDotImg = scDotGo.GetComponent<Image>();
            _scMarkerDotImg.material = style.GetUiMaterial(isText: false);
            _scMarkerDotImg.color = _cVessel;
            _scMarkerDotImg.raycastTarget = false;
            _scMarker.SetActive(false);

            // 四角 HUD 读数卡槽 (增大卡槽尺寸与字号，让六根数一目了然)
            float badgeW = 100f * s;
            float badgeHTop = 42f * s;
            float badgeHBot = 30f * s;
            float cornerX = 142f * s;
            float cornerTopY = 152f * s;
            float cornerBottomY = -178f * s;
            int fsBadge = Mathf.Max(7, Mathf.RoundToInt(9f * s));
            int fsBadgeSub = Mathf.Max(6, Mathf.RoundToInt(7.5f * s));

            // [左上角] 拱点与时钟
            GameObject bTopL = CreateAvionicsSlot(_fullRoot.transform, "Badge_TopLeft",
                new Vector2(badgeW, badgeHTop), new Vector2(-cornerX, cornerTopY), slotBg, slotBorder, s);
            _fApVal = UIFactory.CreateText(bTopL.transform, "F_AP", $"{I18n.Tr("ORBIT_LABEL_AP", "AP")} ---", fsBadge, TextAnchor.MiddleLeft, primCol);
            SetRect(_fApVal.rectTransform, 0f, 9f * s, badgeW - 12f * s, 14f * s);
            _fPeVal = UIFactory.CreateText(bTopL.transform, "F_PE", $"{I18n.Tr("ORBIT_LABEL_PE", "PE")} ---", fsBadge, TextAnchor.MiddleLeft, primCol);
            SetRect(_fPeVal.rectTransform, 0f, -3f * s, badgeW - 12f * s, 14f * s);
            _fTimeVal = UIFactory.CreateText(bTopL.transform, "F_TIME", $"{I18n.Tr("ORBIT_FMT_T_AP", "至远")} --:--", fsBadgeSub, TextAnchor.MiddleLeft, secCol);
            SetRect(_fTimeVal.rectTransform, 0f, -16f * s, badgeW - 12f * s, 12f * s);

            // [右上角] 轨道尺度与周期
            GameObject bTopR = CreateAvionicsSlot(_fullRoot.transform, "Badge_TopRight",
                new Vector2(badgeW, badgeHTop), new Vector2(cornerX, cornerTopY), slotBg, slotBorder, s);
            _fSmaVal = UIFactory.CreateText(bTopR.transform, "F_SMA", $"{I18n.Tr("ORBIT_ELEM_SMA", "a")} ---", fsBadge, TextAnchor.MiddleRight, secCol);
            SetRect(_fSmaVal.rectTransform, 0f, 9f * s, badgeW - 12f * s, 14f * s);
            _fEccVal = UIFactory.CreateText(bTopR.transform, "F_ECC", $"{I18n.Tr("ORBIT_ELEM_ECC", "e")} 0.0000", fsBadge, TextAnchor.MiddleRight, secCol);
            SetRect(_fEccVal.rectTransform, 0f, -3f * s, badgeW - 12f * s, 14f * s);
            _fPeriodVal = UIFactory.CreateText(bTopR.transform, "F_PER", $"{I18n.Tr("ORBIT_LABEL_PERIOD", "周")} --:--", fsBadgeSub, TextAnchor.MiddleRight, unitCol);
            SetRect(_fPeriodVal.rectTransform, 0f, -16f * s, badgeW - 12f * s, 12f * s);

            // [左下角] 赤道参考面要素
            GameObject bBotL = CreateAvionicsSlot(_fullRoot.transform, "Badge_BotLeft",
                new Vector2(badgeW, badgeHBot), new Vector2(-cornerX, cornerBottomY), slotBg, slotBorder, s);
            _fLanVal = UIFactory.CreateText(bBotL.transform, "F_LAN", $"{I18n.Tr("ORBIT_ELEM_LAN", "Ω")} 0.0°", fsBadge, TextAnchor.MiddleLeft, unitCol);
            SetRect(_fLanVal.rectTransform, 0f, 5f * s, badgeW - 12f * s, 14f * s);
            _fAopVal = UIFactory.CreateText(bBotL.transform, "F_AOP", $"{I18n.Tr("ORBIT_ELEM_AOP", "ω")} 0.0°", fsBadge, TextAnchor.MiddleLeft, unitCol);
            SetRect(_fAopVal.rectTransform, 0f, -7f * s, badgeW - 12f * s, 14f * s);

            // [右下角] 空间倾角与当前真近点角
            GameObject bBotR = CreateAvionicsSlot(_fullRoot.transform, "Badge_BotRight",
                new Vector2(badgeW, badgeHBot), new Vector2(cornerX, cornerBottomY), slotBg, slotBorder, s);
            _fIncVal = UIFactory.CreateText(bBotR.transform, "F_INC", $"{I18n.Tr("ORBIT_ELEM_INC", "i")} 0.0°", fsBadge, TextAnchor.MiddleRight, secCol);
            SetRect(_fIncVal.rectTransform, 0f, 5f * s, badgeW - 12f * s, 14f * s);
            _fTaVal = UIFactory.CreateText(bBotR.transform, "F_TA", $"{I18n.Tr("ORBIT_ELEM_TA", "ν")} 0.0°", fsBadge, TextAnchor.MiddleRight, primCol);
            SetRect(_fTaVal.rectTransform, 0f, -7f * s, badgeW - 12f * s, 14f * s);

            // 纳管至基类标准管理器
            this.Controls.Wrap("compact_view", I18n.Tr("ORBIT_VIEW_COMPACT", "精简模式视图"), _compactRoot, null);
            this.Controls.Wrap("full_view", I18n.Tr("ORBIT_VIEW_FULL", "完整全息模式视图"), _fullRoot, null);
            if (_globeRoot != null)
                this.Controls.Wrap("orbit_globe", I18n.Tr("ORBIT_VIEW_GLOBE", "3D轨道球视口"), _globeRoot, null);

            ApplyReadoutColors(theme);   // 读数先着色 (与划线同色)
            CacheThemeColors(theme);
            _cachedDpiScale = s;

            if (config != null && (config.CustomTemplate == "full" || config.WidgetType == "orbital_3d"))
            {
                OnModeToggle();
            }
        }

        private static GameObject CreateAvionicsSlot(Transform parent, string name, Vector2 size, Vector2 pos, Color fill, Color border, float scale)
        {
            GameObject slot = UIFactory.CreatePanel(parent, name, size, pos, fill);
            Outline ol = slot.AddComponent<Outline>();
            ol.effectDistance = new Vector2(0.8f * scale, 0.8f * scale);
            ol.effectColor = border;
            return slot;
        }

        private static Text CreateDiagramLabel(Transform parent, string name, string text, Color color, float scale, int fontSize = 9)
        {
            Text txt = UIFactory.CreateText(parent, name, text, Mathf.Max(8, Mathf.RoundToInt(fontSize * scale)), TextAnchor.MiddleCenter, color);
            txt.rectTransform.sizeDelta = new Vector2(80f * scale, 18f * scale);
            txt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            txt.raycastTarget = false;
            var ol = txt.gameObject.AddComponent<Outline>();
            ol.effectColor = WidgetStyleManager.Instance.GetSurfaceColor(SurfaceStyleRole.PanelDeep);
            ol.effectDistance = new Vector2(1f * scale, -1f * scale);
            return txt;
        }

        private static void CreateLabelValPair(Transform parent, string prefix,
            float lx, float ly, float lw, float lh,
            float vx, float vy, float vw, float vh,
            string labelStr, string defaultVal, int fsL, int fsV, Color cL, Color cV,
            out Text lbl, out Text val)
        {
            lbl = UIFactory.CreateText(parent, prefix + "_Label", labelStr, fsL, TextAnchor.MiddleLeft, cL);
            SetRect(lbl.rectTransform, lx, ly, lw, lh);
            val = UIFactory.CreateText(parent, prefix + "_Val", defaultVal, fsV, TextAnchor.MiddleRight, cV);
            SetRect(val.rectTransform, vx, vy, vw, vh);
        }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            if (rt == null) return;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // ═════════════════════════════════════════════════════════════════
        // 模式切换
        // ═════════════════════════════════════════════════════════════════
        private void OnModeToggle()
        {
            _isFullMode = !_isFullMode;

            _compactRoot.SetActive(!_isFullMode);
            _fullRoot.SetActive(_isFullMode);

            float s = CurrentDpiScale;
            _cachedDpiScale = s;
            Vector2 targetSize = _isFullMode ? FullSize : CompactSize;
            RectTransform.sizeDelta = targetSize * s;

            if (_modeBtnLabel != null)
            {
                _modeBtnLabel.text = _isFullMode ? I18n.Tr("ORBIT_BTN_COMPACT", "TEXT") : I18n.Tr("ORBIT_BTN_3D", "3D SPHERE");
            }

            // 更新按钮坐标 (始终停靠在右上角)
            float btnW = 54f * s;
            float btnH = 16f * s;
            float btnX = 18f * s;
            float btnY = (targetSize.y * 0.5f - 14f) * s;
            _modeButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(btnX, btnY);

            if (_isFullMode)
            {
                _lastDrawnSma = double.NaN; // 强制首次绘制
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-003: 主题样式管道
        // ═════════════════════════════════════════════════════════════════
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            WidgetStyleManager style = WidgetStyleManager.Instance;

            if (_modeBtnOutline != null)
                _modeBtnOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.6f);
            if (_modeBtnLabel != null)
                _modeBtnLabel.color = style.GetTextColor(TextStyleRole.Accent, theme);

            ApplyReadoutColors(theme);      // 先着色读数
            CacheThemeColors(theme);        // 再烘焙语义色缓存 (两者共用同一色谱)
            if (_isFullMode) _lastDrawnSma = double.NaN;
        }

        /// <summary>
        /// 六根数语义色谱 —— "参数读数"与"对应划线"必须取同一色，否则密集图元无法对应。
        /// 派生色一律经 WidgetStyleManager.Tint 由主题色合成 (SPEC-006: 零颜色字面量)，随换肤自动联动：
        ///   a  半长轴  翡翠绿 AccentPrimary      → 轨道曲线 + 拱线(PE–AP) + AP/PE 标记
        ///   e  离心率  琥珀金 WarningColor       → 偏心率矢量 e
        ///   i  轨道倾角 冰蓝   AccentSecondary    → 倾角弧 ×2 (Z–h 弧 / 升交点二面角弧) + 角动量矢量 h
        ///   Ω  升交点赤经 品红 AccentMagenta      → Ω 弧 + 升交线/降交虚线 + AN/DN 交点
        ///   ω  近拱点辐角 青碧 Tint(绿→蓝 55%)    → ω 弧
        ///   ν  真近点角  紫罗兰 Tint(蓝→品红 50%) → ν 弧 + 位置矢径 r
        /// 另：速度矢量 v 取近白中性色；航天器本体沿用 AccentPositive 状态色；赤道盘与 XYZ 轴为中性基准。
        /// </summary>
        private static void ResolveElementColors(ThemeConfig theme,
            out Color a, out Color e, out Color i, out Color lan, out Color aop, out Color ta)
        {
            a = theme.AccentPrimary;                                                            // 翡翠绿
            e = theme.WarningColor;                                                             // 琥珀金
            i = theme.AccentSecondary;                                                          // 冰蓝
            lan = theme.AccentMagenta;                                                          // 品红
            aop = WidgetStyleManager.Tint(theme.AccentPrimary, theme.AccentSecondary, 0.55f);    // 青碧
            ta = WidgetStyleManager.Tint(theme.AccentSecondary, theme.AccentMagenta, 0.5f);      // 紫罗兰
        }

        /// <summary>把六根数语义色应用到全部读数文本 (精简模式卡槽 + 完整模式四角)，与划线颜色严格一致</summary>
        private void ApplyReadoutColors(ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            Color secCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            Color unitCol = style.GetTextColor(TextStyleRole.Unit, theme);

            Color aCol, eCol, iCol, lanCol, aopCol, taCol;
            ResolveElementColors(theme, out aCol, out eCol, out iCol, out lanCol, out aopCol, out taCol);

            // 精简模式卡槽 (AP/PE 属 a 的半长轴端点；T-AP 与周期为中性时间量)
            if (_apVal != null) _apVal.color = aCol;
            if (_peVal != null) _peVal.color = aCol;
            if (_smaVal != null) _smaVal.color = aCol;
            if (_eccVal != null) _eccVal.color = eCol;
            if (_incVal != null) _incVal.color = iCol;
            if (_lanVal != null) _lanVal.color = lanCol;
            if (_aopVal != null) _aopVal.color = aopCol;
            if (_taVal != null) _taVal.color = taCol;
            if (_tApPeReadout != null) _tApPeReadout.color = secCol;
            if (_incDirVal != null) _incDirVal.color = unitCol;
            if (_perVal != null) _perVal.color = unitCol;

            // 完整模式四角读数
            if (_fApVal != null) _fApVal.color = aCol;
            if (_fPeVal != null) _fPeVal.color = aCol;
            if (_fSmaVal != null) _fSmaVal.color = aCol;
            if (_fEccVal != null) _fEccVal.color = eCol;
            if (_fIncVal != null) _fIncVal.color = iCol;
            if (_fLanVal != null) _fLanVal.color = lanCol;
            if (_fAopVal != null) _fAopVal.color = aopCol;
            if (_fTaVal != null) _fTaVal.color = taCol;
            if (_fTimeVal != null) _fTimeVal.color = secCol;
            if (_fPeriodVal != null) _fPeriodVal.color = unitCol;
        }

        private void CacheThemeColors(ThemeConfig theme)
        {
            _cClear = (Color32)Color.clear;

            Color aCol, eCol, iCol, lanCol, aopCol, taCol;
            ResolveElementColors(theme, out aCol, out eCol, out iCol, out lanCol, out aopCol, out taCol);

            // 行星昼夜半球 (中性天体)
            Color pBase = WidgetStyleManager.Darken(theme.AccentSecondary, 0.40f);
            _cPlanetSun = (Color32)WidgetStyleManager.WithAlpha(pBase, 0.95f);
            _cPlanetShadow = (Color32)WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(pBase, 0.65f), 0.90f);
            _cPlanetGrid = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.45f);

            // 赤道参考盘 (中性基准面，不属于任何单一根数)
            _cEquatorPlane = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.22f);

            // a：轨道曲线 (远/近侧深度分层) + 拱线 + AP/PE 拱点
            _cOrbitFront = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.95f);
            _cOrbitGlow = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.30f);
            _cOrbitBack = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.28f);
            _cApPe = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.95f);

            // e：偏心率矢量
            _cVectorE = (Color32)WidgetStyleManager.WithAlpha(eCol, 1.0f);

            // i：倾角弧 ×2 + 角动量矢量 h (i 即 Z 轴与 h 的夹角，同色同族)
            _cElemI = (Color32)WidgetStyleManager.WithAlpha(iCol, 0.90f);
            _cVectorH = (Color32)WidgetStyleManager.WithAlpha(iCol, 1.0f);

            // Ω：Ω 弧 + 升交线/降交虚线 + AN/DN 交点
            _cElemLan = (Color32)WidgetStyleManager.WithAlpha(lanCol, 0.90f);
            _cNode = (Color32)WidgetStyleManager.WithAlpha(lanCol, 0.95f);

            // ω：ω 弧 (轨道面内 n̂ → ê)
            _cElemAop = (Color32)WidgetStyleManager.WithAlpha(aopCol, 0.90f);

            // ν：ν 弧 (轨道面内 ê → r̂) + 位置矢径 r
            _cElemTa = (Color32)WidgetStyleManager.WithAlpha(taCol, 0.90f);
            _cVectorR = (Color32)WidgetStyleManager.WithAlpha(taCol, 0.95f);

            // 航天器本体状态色 与 速度矢量 (中性高对比近白)
            _cVessel = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPositive, 1.0f);
            _cVesselGlow = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.40f);
            _cVectorV = (Color32)WidgetStyleManager.WithAlpha(theme.HorizonLineColor, 1.0f);

            // 中性参考系 (坐标轴 / 标签)
            _cAxis = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.85f);
            _cLabelText = (Color32)WidgetStyleManager.WithAlpha(WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme), 1.0f);

            // 动态航天器覆盖层硬件图元着色 (根治图元透明隐形缺陷)
            if (_scRadiusLineImg != null) _scRadiusLineImg.color = _cVectorR;
            if (_scVelocityArrowImg != null) _scVelocityArrowImg.color = _cVectorV;
            if (_scVelocityHeadImg != null) _scVelocityHeadImg.color = _cVectorV;
            if (_scMarkerImg != null) _scMarkerImg.color = _cVessel;
            if (_scMarkerDotImg != null) _scMarkerDotImg.color = _cVessel;
            if (_scOutline != null) _scOutline.effectColor = _cVesselGlow;

            // 3D 权威开普勒拓扑 UGUI 矢量标注着色 (与几何要素严格同色同族)
            if (_lblAxisX != null) _lblAxisX.color = _cAxis;
            if (_lblAxisY != null) _lblAxisY.color = _cAxis;
            if (_lblAxisZ != null) _lblAxisZ.color = _cAxis;
            if (_lblEquator != null) _lblEquator.color = _cAxis;
            if (_lblOrigin != null) _lblOrigin.color = _cAxis;
            if (_lblNodeAn != null) _lblNodeAn.color = _cNode;
            if (_lblNodeDn != null) _lblNodeDn.color = _cNode;
            if (_lblArcOmega != null) _lblArcOmega.color = _cElemLan;
            if (_lblArcInc != null) _lblArcInc.color = _cElemI;
            if (_lblVectorH != null) _lblVectorH.color = _cVectorH;
            if (_lblVectorE != null) _lblVectorE.color = _cVectorE;
            if (_lblArcAop != null) _lblArcAop.color = _cElemAop;
            if (_lblApoapsis != null) _lblApoapsis.color = _cApPe;
            if (_lblPeriapsis != null) _lblPeriapsis.color = _cApPe;
            if (_lblArcTa != null) _lblArcTa.color = _cElemTa;
            if (_lblVectorR != null) _lblVectorR.color = _cVectorR;
            if (_lblVectorV != null) _lblVectorV.color = _cVectorV;
            if (_lblSpacecraft != null) _lblSpacecraft.color = _cVessel;
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-004C: 数据心跳独立解算循环 (受 HeartBeatTier 严格节流)
        // 专用于开普勒轨道六根数物理计算、Principia 探针查询与状态评估 (0 UI 绘制)
        // ═════════════════════════════════════════════════════════════════
        private double _cachedSma = double.NaN;
        private double _cachedEcc = double.NaN;
        private double _cachedInc = double.NaN;
        private double _cachedLan = double.NaN;
        private double _cachedAop = double.NaN;
        private double _cachedPeriod = double.NaN;
        private bool _cachedIsPrincipia = false;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);

            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _hasVessel = false;
                _dataBadgeText = I18n.Tr("ORBIT_NO_VESSEL", "NO VESSEL");
                _dataBadgeRole = TextStyleRole.Muted;
                return;
            }

            _hasVessel = true;

            // 1. 基础拱点与时钟采样
            _dataAp = telemetry.Apoapsis;
            _dataPe = telemetry.Periapsis;
            _dataTAp = telemetry.TimeToAp;
            _dataTPe = telemetry.TimeToPe;

            // 2. 开普勒六根数：稳态两体节拍守卫与外部探针按需查询
            bool isManeuvering = telemetry.Throttle > 0.001f || telemetry.DynamicPressure > 0.1 || telemetry.HasManeuverNode;
            bool needKeplerianRecalc = isManeuvering || double.IsNaN(_cachedSma) || context.Every(3);

            double sma, ecc, inc, lan, aop, period;
            bool isPrincipia;

            if (needKeplerianRecalc)
            {
                sma = telemetry.SemiMajorAxis;
                ecc = telemetry.Eccentricity;
                inc = telemetry.Inclination;
                lan = telemetry.LongitudeOfAscendingNode;
                aop = telemetry.ArgumentOfPeriapsis;
                period = telemetry.OrbitalPeriod;
                isPrincipia = false;

                bool prinAvail = ExternalProbeRegistry.NumericResolver != null;
                if (prinAvail)
                {
                    double pSma = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "SMA");
                    if (!double.IsNaN(pSma) && pSma > 0.0) { sma = pSma; isPrincipia = true; }
                    double pEcc = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "ECC");
                    if (!double.IsNaN(pEcc) && pEcc >= 0.0) { ecc = pEcc; isPrincipia = true; }
                    double pInc = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "INC");
                    if (!double.IsNaN(pInc)) { inc = pInc; isPrincipia = true; }
                    double pLan = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "LAN");
                    if (!double.IsNaN(pLan)) { lan = pLan; isPrincipia = true; }
                    double pAop = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "LPE");
                    if (!double.IsNaN(pAop)) { aop = pAop; isPrincipia = true; }

                    double pNodal = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "NODALPERIOD");
                    if (!double.IsNaN(pNodal) && pNodal > 0.0) { period = pNodal; isPrincipia = true; }
                    else
                    {
                        double pSidereal = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "SIDEREALPERIOD");
                        if (!double.IsNaN(pSidereal) && pSidereal > 0.0) { period = pSidereal; isPrincipia = true; }
                    }
                }

                lan = NormalizeDegrees(lan);
                aop = NormalizeDegrees(aop);
                if (double.IsNaN(inc) || double.IsInfinity(inc)) inc = 0.0;
                inc = Math.Min(180.0, Math.Max(0.0, inc));

                // 几何回退
                if (double.IsNaN(ecc) || ecc < 0.0)
                {
                    double rA = Math.Max(10000.0, DefaultKerbinRadius + _dataAp);
                    double rP = DefaultKerbinRadius + _dataPe;
                    ecc = rP <= 0.0 ? 1.05 : Math.Max(0.0, (rA - rP) / (rA + rP));
                }

                if (double.IsNaN(sma) || sma <= 0.0)
                {
                    double rA = DefaultKerbinRadius + _dataAp;
                    double rP = DefaultKerbinRadius + _dataPe;
                    sma = (rA + rP) * 0.5;
                }

                if (double.IsNaN(period) || period <= 0.0)
                    period = ecc < 1.0 ? Math.Abs(_dataTAp - _dataTPe) * 2.0 : 0.0;

                _cachedSma = sma;
                _cachedEcc = ecc;
                _cachedInc = inc;
                _cachedLan = lan;
                _cachedAop = aop;
                _cachedPeriod = period;
                _cachedIsPrincipia = isPrincipia;
            }
            else
            {
                sma = _cachedSma;
                ecc = _cachedEcc;
                inc = _cachedInc;
                lan = _cachedLan;
                aop = _cachedAop;
                period = _cachedPeriod;
                isPrincipia = _cachedIsPrincipia;
            }

            _dataSma = sma;
            _dataEcc = ecc;
            _dataInc = inc;
            _dataLan = lan;
            _dataAop = aop;
            _dataPeriod = period;

            // 真近点角（连续平滑演进）
            double tra = telemetry.TrueAnomaly;
            if (isPrincipia && ExternalProbeRegistry.NumericResolver != null)
            {
                double pTra = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "TRA");
                if (!double.IsNaN(pTra)) tra = pTra;
            }
            _dataTra = NormalizeDegrees(tra);

            // Principia 参考系标题解算 (纯字符串，不更新 UI)
            bool hasPrinFrame = false;
            if (isPrincipia && ExternalProbeRegistry.StringResolver != null)
            {
                string prinFrame = ExternalProbeRegistry.ResolveString("PRINCIPIA", "NAVBALLNAME", "");
                if (string.IsNullOrEmpty(prinFrame) || prinFrame == "---")
                    prinFrame = ExternalProbeRegistry.ResolveString("PRINCIPIA", "FRAME", "");
                if (!string.IsNullOrEmpty(prinFrame) && prinFrame != "---")
                {
                    _dataTitleText = $"ORBIT [{prinFrame.ToUpperInvariant()}]";
                    hasPrinFrame = true;
                }
            }
            if (!hasPrinFrame)
            {
                _dataTitleText = I18n.Tr("ORBIT_TITLE", "ORBIT ELEMENTS");
            }

            // 3. 轨道能量状态解算 (纯物理状态判定)
            ComputeOrbitStateBadge(_dataAp, _dataPe, _dataEcc, telemetry.AtmosphereDepth, telemetry.HasAtmosphere, isPrincipia,
                out _dataBadgeText, out _dataBadgeRole);
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-004D: UI 独立绘制循环 (随 RefreshTier 满频触发)
        // 专注于 UGUI 文本刷新、矢量硬件覆盖层 Transform 补间与网格渲染 (0 物理采样)
        // ═════════════════════════════════════════════════════════════════
        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);

            Title.Text = _dataTitleText;
            OrbitBadge.Text = _dataBadgeText;
            OrbitBadge.SetRole(_dataBadgeRole);

            if (!_hasVessel) return;

            if (_isFullMode)
            {
                if (_labelsRoot != null && !_labelsRoot.activeSelf) _labelsRoot.SetActive(true);
                UpdateFullModeReadouts(_dataAp, _dataPe, _dataTAp, _dataSma, _dataEcc, _dataInc, _dataLan, _dataAop, _dataTra, _dataPeriod);
                float now = context.UnscaledTime;
                bool geomDirty = CheckDirty(_dataSma, _dataEcc, _dataInc, _dataLan, _dataAop);
                bool traDirty = double.IsNaN(_lastDrawnTra) || Math.Abs(NormalizeDegrees(_dataTra) - NormalizeDegrees(_lastDrawnTra)) > 1.5;

                if ((now - _lastMeshRebuildTime >= 0.25f || _lastMeshRebuildTime < 0f) && (geomDirty || traDirty))
                {
                    _lastMeshRebuildTime = now;
                    _lastDrawnSma = _dataSma; _lastDrawnEcc = _dataEcc; _lastDrawnInc = _dataInc;
                    _lastDrawnLan = _dataLan; _lastDrawnAop = _dataAop;
                    _lastDrawnAp = _dataAp; _lastDrawnPe = _dataPe;
                    _lastDrawnTra = _dataTra;

                    UpdateDiagramLabels(_dataTra, _dataSma, _dataEcc, _dataInc, _dataLan, _dataAop, _dataAp, _dataPe);
                    if (_diagramGraphic != null) _diagramGraphic.SetVerticesDirty();
                }
                UpdateSpacecraftOverlay(_dataTra, _dataSma, _dataEcc, _dataInc, _dataLan, _dataAop);
            }
            else
            {
                if (_scMarker != null && _scMarker.activeSelf) _scMarker.SetActive(false);
                if (_scRadiusLine != null && _scRadiusLine.activeSelf) _scRadiusLine.SetActive(false);
                if (_scVelocityArrow != null && _scVelocityArrow.activeSelf) _scVelocityArrow.SetActive(false);
                if (_labelsRoot != null && _labelsRoot.activeSelf) _labelsRoot.SetActive(false);
                UpdateCompactModeReadouts(_dataAp, _dataPe, _dataTAp, _dataTPe, _dataSma, _dataEcc, _dataInc, _dataLan, _dataAop, _dataTra, _dataPeriod);
            }
        }

        private static void ComputeOrbitStateBadge(double ap, double pe, double ecc, double atmDepth, bool hasAtm, bool isPrincipia,
            out string badgeText, out TextStyleRole badgeRole)
        {
            // 优先接入 Principia 轨道分析高阶物理描述
            if (isPrincipia)
            {
                string pDesc = ExternalProbeRegistry.ResolveString("PRINCIPIA", "ORBITDESC", "");
                if (!string.IsNullOrEmpty(pDesc) && pDesc != "---")
                {
                    string clean = pDesc.Replace("\n", " ").Trim();
                    if (clean.Length > 15) clean = clean.Substring(0, 15).Trim();
                    badgeText = clean.ToUpperInvariant();
                    badgeRole = TextStyleRole.Accent;
                    return;
                }
            }

            double safeAlt = hasAtm ? atmDepth : 0.0;

            // 获取天体名称用于标题增强 (通过探针查表，SPEC-007 禁止场景查询)
            string bodyName = ExternalProbeRegistry.ResolveString("ORBIT", "BODY", "");
            bool hasBodyName = !string.IsNullOrEmpty(bodyName) && bodyName != "---";

            if (ecc >= 1.0)
            {
                badgeText = I18n.Tr("ORBIT_BADGE_ESCAPE", "ESCAPE");
                badgeRole = TextStyleRole.Danger;
            }
            else if (pe < 0.0)
            {
                badgeText = I18n.Tr("ORBIT_BADGE_BALLISTIC", "BALLISTIC");
                badgeRole = TextStyleRole.Danger;
            }
            else if (pe < safeAlt)
            {
                badgeText = I18n.Tr("ORBIT_BADGE_SUBORBIT", "SUBORBIT");
                badgeRole = TextStyleRole.Warning;
            }
            else if (ecc < 0.015)
            {
                badgeText = I18n.Tr("ORBIT_BADGE_CIRCULAR", "CIRCULAR");
                badgeRole = TextStyleRole.Accent;
            }
            else
            {
                badgeText = I18n.Tr("ORBIT_BADGE_ELLIPTIC", "ELLIPTIC");
                badgeRole = TextStyleRole.PrimaryValue;
            }

            // 天体名称联动到 OrbitBadge 后缀 (如 "圆轨道 EARTH" → 增强态势感知)
            if (hasBodyName)
            {
                badgeText = $"{badgeText} ({bodyName.ToUpperInvariant()})";
            }
        }

        private void UpdateCompactModeReadouts(double ap, double pe, double tAp, double tPe,
            double sma, double ecc, double inc, double lan, double aop, double tra, double period)
        {
            if (double.IsNaN(_lastCompactAp) || Math.Abs(ap - _lastCompactAp) >= 5.0)
            {
                _lastCompactAp = ap;
                SetTextIfChanged(_apVal, FormatMetricDistance(ap));
            }
            if (double.IsNaN(_lastCompactPe) || Math.Abs(pe - _lastCompactPe) >= 5.0)
            {
                _lastCompactPe = pe;
                SetTextIfChanged(_peVal, pe < -100000.0 ? I18n.Tr("ORBIT_VAL_IMPACT", "IMPACT") : FormatMetricDistance(pe));
            }

            int curSec = (int)tAp;
            if (curSec != _lastCompactSec)
            {
                _lastCompactSec = curSec;
                string tApLabel = I18n.Tr("ORBIT_FMT_T_AP", "T-AP");
                string tPeLabel = I18n.Tr("ORBIT_FMT_T_PE", "T-PE");
                SetTextIfChanged(_tApPeReadout, $"{tApLabel} {FormatDurationCompact(tAp)}  {tPeLabel} {FormatDurationCompact(tPe)}");
            }

            if (double.IsNaN(_lastCompactSma) || Math.Abs(sma - _lastCompactSma) >= 5.0)
            {
                _lastCompactSma = sma;
                SetTextIfChanged(_smaVal, FormatMetricDistance(sma));
            }

            if (double.IsNaN(_lastCompactEcc) || Math.Abs(ecc - _lastCompactEcc) >= 0.0001)
            {
                _lastCompactEcc = ecc;
                if (ecc < 0.0001 && ecc > 0.0)
                    SetTextIfChanged(_eccVal, ecc.ToString("E2"));
                else
                    SetTextIfChanged(_eccVal, ecc.ToString("F4"));
            }

            if (double.IsNaN(_lastCompactInc) || Math.Abs(inc - _lastCompactInc) >= 0.05)
            {
                _lastCompactInc = inc;
                SetTextIfChanged(_incVal, FormatAngleSmart(inc));
                if (_incDirVal != null)
                {
                    _incDirVal.text = inc > 90.0 ? I18n.Tr("ORBIT_DIR_RET", "RET") : I18n.Tr("ORBIT_DIR_PRO", "PRO");
                    _incDirVal.color = inc > 90.0 ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, null) : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, null);
                }
            }

            if (double.IsNaN(_lastCompactLan) || Math.Abs(lan - _lastCompactLan) >= 0.05)
            {
                _lastCompactLan = lan;
                SetTextIfChanged(_lanVal, FormatAngleSmart(lan));
            }
            if (double.IsNaN(_lastCompactAop) || Math.Abs(aop - _lastCompactAop) >= 0.05)
            {
                _lastCompactAop = aop;
                SetTextIfChanged(_aopVal, FormatAngleSmart(aop));
            }
            if (double.IsNaN(_lastCompactTa) || Math.Abs(tra - _lastCompactTa) >= 0.1)
            {
                _lastCompactTa = tra;
                SetTextIfChanged(_taVal, FormatAngleSmart(tra));
            }
            if (double.IsNaN(_lastCompactPer) || Math.Abs(period - _lastCompactPer) >= 1.0)
            {
                _lastCompactPer = period;
                SetTextIfChanged(_perVal, FormatPeriodCompact(period));
            }
        }

        private void UpdateFullModeReadouts(double ap, double pe, double tAp,
            double sma, double ecc, double inc, double lan, double aop, double tra, double period)
        {
            if (double.IsNaN(_lastFullAp) || Math.Abs(ap - _lastFullAp) >= 5.0)
            {
                _lastFullAp = ap;
                string apLabel = I18n.Tr("ORBIT_LABEL_AP", "AP");
                SetTextIfChanged(_fApVal, $"{apLabel} {FormatMetricDistance(ap)}");
            }
            if (double.IsNaN(_lastFullPe) || Math.Abs(pe - _lastFullPe) >= 5.0)
            {
                _lastFullPe = pe;
                string peLabel = I18n.Tr("ORBIT_LABEL_PE", "PE");
                SetTextIfChanged(_fPeVal, $"{peLabel} {FormatMetricDistance(pe)}");
            }

            int curSec = (int)tAp;
            if (curSec != _lastFullSec)
            {
                _lastFullSec = curSec;
                SetTextIfChanged(_fTimeVal, $"{I18n.Tr("ORBIT_FMT_T_AP", "T-AP")} {FormatDurationCompact(tAp)}");
            }

            if (double.IsNaN(_lastFullSma) || Math.Abs(sma - _lastFullSma) >= 5.0)
            {
                _lastFullSma = sma;
                string elemA = I18n.Tr("ORBIT_ELEM_SMA", "a");
                SetTextIfChanged(_fSmaVal, $"{elemA} {FormatMetricDistance(sma)}");
            }

            if (double.IsNaN(_lastFullEcc) || Math.Abs(ecc - _lastFullEcc) >= 0.0001)
            {
                _lastFullEcc = ecc;
                string elemE = I18n.Tr("ORBIT_ELEM_ECC", "e");
                if (ecc < 0.0001 && ecc > 0.0)
                    SetTextIfChanged(_fEccVal, $"{elemE} {ecc:E2}");
                else
                    SetTextIfChanged(_fEccVal, $"{elemE} {ecc:F4}");
            }

            if (double.IsNaN(_lastFullPer) || Math.Abs(period - _lastFullPer) >= 1.0)
            {
                _lastFullPer = period;
                SetTextIfChanged(_fPeriodVal, $"{I18n.Tr("ORBIT_LABEL_PERIOD", "P")} {FormatPeriodCompact(period)}");
            }

            if (double.IsNaN(_lastFullLan) || Math.Abs(lan - _lastFullLan) >= 0.05)
            {
                _lastFullLan = lan;
                SetTextIfChanged(_fLanVal, $"{I18n.Tr("ORBIT_ELEM_LAN", "Ω")} {FormatAngleSmart(lan)}");
            }
            if (double.IsNaN(_lastFullAop) || Math.Abs(aop - _lastFullAop) >= 0.05)
            {
                _lastFullAop = aop;
                SetTextIfChanged(_fAopVal, $"{I18n.Tr("ORBIT_ELEM_AOP", "ω")} {FormatAngleSmart(aop)}");
            }
            if (double.IsNaN(_lastFullInc) || Math.Abs(inc - _lastFullInc) >= 0.05)
            {
                _lastFullInc = inc;
                SetTextIfChanged(_fIncVal, $"{I18n.Tr("ORBIT_ELEM_INC", "i")} {FormatAngleSmart(inc)}");
            }
            if (double.IsNaN(_lastFullTa) || Math.Abs(tra - _lastFullTa) >= 0.1)
            {
                _lastFullTa = tra;
                SetTextIfChanged(_fTaVal, $"{I18n.Tr("ORBIT_ELEM_TA", "ν")} {FormatAngleSmart(tra)}");
            }
        }

        /// <summary>
        /// 渲染脏标记防抖：开普勒轨道几何要素 (a, e, i, Ω, ω) 变化超过阈值才重绘静态全息图。
        /// 动静分离：真近点角 ν (航天器运动) 完全从网格脏标记中剔除，由动态硬件图元独立平滑驱动，彻底根除高频 CPU 网格全量重建。
        /// </summary>
        private bool CheckDirty(double sma, double ecc, double inc, double lan, double aop)
        {
            if (double.IsNaN(_lastDrawnSma)) return true;
            double relSma = Math.Abs(sma - _lastDrawnSma) / Math.Max(1.0, _lastDrawnSma);

            bool dirty = relSma > 0.001
                || Math.Abs(ecc - _lastDrawnEcc) > 0.001
                || Math.Abs(inc - _lastDrawnInc) > 0.5;

            // 对于近圆轨道 (ecc < 0.02)，近地点辐角 aop 在数学上是奇异点/数值噪音，忽略其高频抖动
            if (ecc > 0.02 && Math.Abs(aop - _lastDrawnAop) > 0.5) dirty = true;

            // 对于近赤道轨道 (inc < 0.5°)，升交点经度 lan 在数学上是奇异点/数值噪音，忽略其高频抖动
            if (inc > 0.5 && Math.Abs(lan - _lastDrawnLan) > 0.5) dirty = true;

            return dirty;
        }

        /// <summary>
        /// 动静分离：以 UGUI 硬件 Transform 独立更新航天器空间投影位点与瞬时速度矢量 (0 CPU 网格重建，60FPS 满帧运行)
        /// </summary>
        private void UpdateSpacecraftOverlay(double tra, double sma, double ecc, double inc, double lan, double aop)
        {
            if (_scMarker == null || double.IsNaN(sma) || sma <= 0.0)
            {
                if (_scMarker != null && _scMarker.activeSelf) _scMarker.SetActive(false);
                if (_scRadiusLine != null && _scRadiusLine.activeSelf) _scRadiusLine.SetActive(false);
                if (_scVelocityArrow != null && _scVelocityArrow.activeSelf) _scVelocityArrow.SetActive(false);
                if (_lblSpacecraft != null && _lblSpacecraft.gameObject.activeSelf) _lblSpacecraft.gameObject.SetActive(false);
                if (_lblVectorV != null && _lblVectorV.gameObject.activeSelf) _lblVectorV.gameObject.SetActive(false);
                if (_lblVectorR != null && _lblVectorR.gameObject.activeSelf) _lblVectorR.gameObject.SetActive(false);
                return;
            }

            float s = _cachedDpiScale > 0.01f ? _cachedDpiScale : 1f;
            float cx = 0f;
            float cy = -8f * s;
            double camPitch = 25.0 * Math.PI / 180.0;
            double camYaw = -115.0 * Math.PI / 180.0;
            double cosCp = Math.Cos(camPitch), sinCp = Math.Sin(camPitch);
            double cosCy = Math.Cos(camYaw), sinCy = Math.Sin(camYaw);

            double diskR = 135.0 * s;
            double maxOrbitR = diskR * 1.25;
            bool closed = ecc < 1.0;
            double eDraw = closed ? Math.Min(Math.Max(0.0, ecc), 0.96) : Math.Min(Math.Max(1.0, ecc), 4.0);
            double pShp = sma * (1.0 - eDraw * eDraw);
            double scale = closed 
                ? ((sma > 1.0) ? (maxOrbitR / (sma * (1.0 + eDraw))) : 1.0) 
                : ((diskR * 0.55) * (1.0 + eDraw) / Math.Max(1.0, pShp));
            double pScale = pShp * scale;

            double iRad = inc * Math.PI / 180.0;
            double oRad = lan * Math.PI / 180.0;
            double wRad = aop * Math.PI / 180.0;
            double vRad = tra * Math.PI / 180.0;

            double nx = Math.Cos(oRad), ny = Math.Sin(oRad);
            double hx = Math.Sin(iRad) * Math.Sin(oRad);
            double hy = -Math.Sin(iRad) * Math.Cos(oRad);
            double hz = Math.Cos(iRad);

            double hCrossNx = hy * 0.0 - hz * ny;
            double hCrossNy = hz * nx - hx * 0.0;
            double hCrossNz = hx * ny - hy * nx;

            double edirX = Math.Cos(wRad) * nx + Math.Sin(wRad) * hCrossNx;
            double edirY = Math.Cos(wRad) * ny + Math.Sin(wRad) * hCrossNy;
            double edirZ = Math.Cos(wRad) * 0.0 + Math.Sin(wRad) * hCrossNz;

            double qdirX = hy * edirZ - hz * edirY;
            double qdirY = hz * edirX - hx * edirZ;
            double qdirZ = hx * edirY - hy * edirX;

            double denSc = 1.0 + eDraw * Math.Cos(vRad);
            if (denSc < 1e-6) denSc = 1e-6;
            double rSc = pScale / denSc;
            double scWx = rSc * (Math.Cos(vRad) * edirX + Math.Sin(vRad) * qdirX);
            double scWy = rSc * (Math.Cos(vRad) * edirY + Math.Sin(vRad) * qdirY);
            double scWz = rSc * (Math.Cos(vRad) * edirZ + Math.Sin(vRad) * qdirZ);

            ProjectWorldToScreenFloat(scWx, scWy, scWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float scX, out float scY, out _);

            if (!_scMarker.activeSelf) _scMarker.SetActive(true);
            SetAnchoredPositionIfChanged(_scMarkerRt, new Vector2(scX, scY));

            // 半径矢量线 (原点 -> 航天器)
            if (_scRadiusLine != null)
            {
                if (!_scRadiusLine.activeSelf) _scRadiusLine.SetActive(true);
                float rDx = scX - cx;
                float rDy = scY - cy;
                float rLen = Mathf.Sqrt(rDx * rDx + rDy * rDy);
                float rAngle = Mathf.Atan2(rDy, rDx) * Mathf.Rad2Deg;
                SetAnchoredPositionIfChanged(_scRadiusLineRt, new Vector2(cx, cy));
                SetSizeDeltaIfChanged(_scRadiusLineRt, new Vector2(rLen, 1.5f * s));
                _scRadiusLineRt.localRotation = Quaternion.Euler(0f, 0f, rAngle);
            }

            // 速度矢量箭头 (航天器 -> 切线动量)
            double dThetaX = -Math.Sin(vRad) * edirX + (eDraw + Math.Cos(vRad)) * qdirX;
            double dThetaY = -Math.Sin(vRad) * edirY + (eDraw + Math.Cos(vRad)) * qdirY;
            double dThetaZ = -Math.Sin(vRad) * edirZ + (eDraw + Math.Cos(vRad)) * qdirZ;
            double vMag = Math.Sqrt(dThetaX * dThetaX + dThetaY * dThetaY + dThetaZ * dThetaZ);
            float vEndX = scX, vEndY = scY;
            if (vMag > 0.001 && _scVelocityArrow != null)
            {
                if (!_scVelocityArrow.activeSelf) _scVelocityArrow.SetActive(true);
                dThetaX /= vMag; dThetaY /= vMag; dThetaZ /= vMag;
                double vLen = 30.0 * s;
                ProjectWorldToScreenFloat(scWx + vLen * dThetaX, scWy + vLen * dThetaY, scWz + vLen * dThetaZ,
                    cosCp, sinCp, cosCy, sinCy, cx, cy, out vEndX, out vEndY, out _);

                float dx = vEndX - scX;
                float dy = vEndY - scY;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                SetAnchoredPositionIfChanged(_scVelocityArrowRt, new Vector2(scX, scY));
                SetSizeDeltaIfChanged(_scVelocityArrowRt, new Vector2(len, 2.0f * s));
                _scVelocityArrowRt.localRotation = Quaternion.Euler(0f, 0f, angle);

                if (_scVelocityHeadRt != null)
                {
                    SetAnchoredPositionIfChanged(_scVelocityHeadRt, new Vector2(len, 0f));
                    _scVelocityHeadRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
                }
            }

            // 联动标注航天器、速度矢量与径向矢量文本
            if (_lblSpacecraft != null)
            {
                if (!_lblSpacecraft.gameObject.activeSelf) _lblSpacecraft.gameObject.SetActive(true);
                float scLblX = scX + (scX >= cx ? 14f : -26f) * s;
                float scLblY = scY + 8f * s;
                SetAnchoredPositionIfChanged(_lblSpacecraft.rectTransform, new Vector2(scLblX, scLblY));
            }
            if (_lblVectorV != null)
            {
                if (!_lblVectorV.gameObject.activeSelf) _lblVectorV.gameObject.SetActive(true);
                float vLblX = vEndX + (vEndX >= scX ? 8f : -12f) * s;
                float vLblY = vEndY + 4f * s;
                SetAnchoredPositionIfChanged(_lblVectorV.rectTransform, new Vector2(vLblX, vLblY));
            }
            if (_lblVectorR != null)
            {
                if (!_lblVectorR.gameObject.activeSelf) _lblVectorR.gameObject.SetActive(true);
                float rMidX = (cx + scX) * 0.5f - 8f * s;
                float rMidY = (cy + scY) * 0.5f + 8f * s;
                SetAnchoredPositionIfChanged(_lblVectorR.rectTransform, new Vector2(rMidX, rMidY));
            }
        }

        /// <summary>
        /// 权威开普勒拓扑 UGUI 矢量标注位置智能更新与防重叠解算 (仅在轨道几何要素变动或 ν 扫掠时调用)
        /// </summary>
        private void UpdateDiagramLabels(double tra, double sma, double ecc, double inc, double lan, double aop, double ap, double pe)
        {
            if (_labelsRoot == null || double.IsNaN(sma) || sma <= 0.0) return;

            float s = _cachedDpiScale > 0.01f ? _cachedDpiScale : 1f;
            float cx = 0f;
            float cy = -8f * s;
            double camPitch = 25.0 * Math.PI / 180.0;
            double camYaw = -115.0 * Math.PI / 180.0;
            double cosCp = Math.Cos(camPitch), sinCp = Math.Sin(camPitch);
            double cosCy = Math.Cos(camYaw), sinCy = Math.Sin(camYaw);

            double diskR = 135.0 * s;
            double maxOrbitR = diskR * 1.25;
            bool closed = ecc < 1.0;
            double eDraw = closed ? Math.Min(Math.Max(0.0, ecc), 0.96) : Math.Min(Math.Max(1.0, ecc), 4.0);
            double pShp = sma * (1.0 - eDraw * eDraw);
            double scale = closed
                ? ((sma > 1.0) ? (maxOrbitR / (sma * (1.0 + eDraw))) : 1.0)
                : ((diskR * 0.55) * (1.0 + eDraw) / Math.Max(1.0, pShp));
            double pScale = pShp * scale;

            double iRad = inc * Math.PI / 180.0;
            double oRad = lan * Math.PI / 180.0;
            double wRad = aop * Math.PI / 180.0;
            double vRad = tra * Math.PI / 180.0;

            double nx = Math.Cos(oRad), ny = Math.Sin(oRad);
            double hx = Math.Sin(iRad) * Math.Sin(oRad);
            double hy = -Math.Sin(iRad) * Math.Cos(oRad);
            double hz = Math.Cos(iRad);

            double hCrossNx = hy * 0.0 - hz * ny;
            double hCrossNy = hz * nx - hx * 0.0;
            double hCrossNz = hx * ny - hy * nx;

            double edirX = Math.Cos(wRad) * nx + Math.Sin(wRad) * hCrossNx;
            double edirY = Math.Cos(wRad) * ny + Math.Sin(wRad) * hCrossNy;
            double edirZ = Math.Cos(wRad) * 0.0 + Math.Sin(wRad) * hCrossNz;

            double qdirX = hy * edirZ - hz * edirY;
            double qdirY = hz * edirX - hx * edirZ;
            double qdirZ = hx * edirY - hy * edirX;

            // 1. 惯性坐标系 X, Y, Z 与原点 O
            ProjectWorldToScreenFloat(diskR * 1.35, 0.0, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float xX, out float xY, out _);
            if (_lblAxisX != null) SetAnchoredPositionIfChanged(_lblAxisX.rectTransform, new Vector2(xX - 12f * s, xY - 14f * s));

            ProjectWorldToScreenFloat(0.0, diskR * 1.30, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float yX, out float yY, out _);
            if (_lblAxisY != null) SetAnchoredPositionIfChanged(_lblAxisY.rectTransform, new Vector2(yX + 14f * s, yY - 2f * s));

            ProjectWorldToScreenFloat(0.0, 0.0, diskR * 1.40, cosCp, sinCp, cosCy, sinCy, cx, cy, out float zX, out float zY, out _);
            if (_lblAxisZ != null) SetAnchoredPositionIfChanged(_lblAxisZ.rectTransform, new Vector2(zX, zY + 14f * s));

            if (_lblOrigin != null) SetAnchoredPositionIfChanged(_lblOrigin.rectTransform, new Vector2(cx - 16f * s, cy - 14f * s));

            // 2. 赤道参考面标注 (位于盘边缘)
            ProjectWorldToScreenFloat(diskR * 0.75, diskR * 0.55, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eqX, out float eqY, out _);
            if (_lblEquator != null) SetAnchoredPositionIfChanged(_lblEquator.rectTransform, new Vector2(eqX + 14f * s, eqY + 8f * s));

            // 3. 升交点与降交点
            double rAn = pScale / (1.0 + eDraw * Math.Cos(-wRad));
            ProjectWorldToScreenFloat(rAn * nx, rAn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float anX, out float anY, out _);

            double rDn = pScale / (1.0 + eDraw * Math.Cos(Math.PI - wRad));
            ProjectWorldToScreenFloat(-rDn * nx, -rDn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float dnX, out float dnY, out _);

            // 4. 拱点 PE 与 AP
            double rPe = pScale / (1.0 + eDraw);
            ProjectWorldToScreenFloat(rPe * edirX, rPe * edirY, rPe * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out float peX, out float peY, out _);

            float apX = 0f, apY = 0f;
            if (closed)
            {
                double rAp = pScale / (1.0 - eDraw);
                ProjectWorldToScreenFloat(-rAp * edirX, -rAp * edirY, -rAp * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out apX, out apY, out _);
                if (_lblApoapsis != null && !_lblApoapsis.gameObject.activeSelf) _lblApoapsis.gameObject.SetActive(true);
            }
            else
            {
                if (_lblApoapsis != null && _lblApoapsis.gameObject.activeSelf) _lblApoapsis.gameObject.SetActive(false);
            }

            // 智能防重叠偏移：升交点 AN vs 远拱点 AP / 近拱点 PE
            float anOffX = 16f * s, anOffY = -12f * s;
            float apOffX = -16f * s, apOffY = 12f * s;
            float peOffX = 16f * s, peOffY = 10f * s;

            if (closed && Vector2.Distance(new Vector2(anX, anY), new Vector2(apX, apY)) < 36f * s)
            {
                anOffY = -22f * s;
                apOffY = 22f * s;
            }
            if (Vector2.Distance(new Vector2(anX, anY), new Vector2(peX, peY)) < 36f * s)
            {
                anOffX = -20f * s;
                peOffX = 20f * s;
            }

            if (_lblNodeAn != null) SetAnchoredPositionIfChanged(_lblNodeAn.rectTransform, new Vector2(anX + anOffX, anY + anOffY));
            if (_lblNodeDn != null) SetAnchoredPositionIfChanged(_lblNodeDn.rectTransform, new Vector2(dnX - 16f * s, dnY + 10f * s));
            if (_lblPeriapsis != null) SetAnchoredPositionIfChanged(_lblPeriapsis.rectTransform, new Vector2(peX + peOffX, peY + peOffY));
            if (closed && _lblApoapsis != null) SetAnchoredPositionIfChanged(_lblApoapsis.rectTransform, new Vector2(apX + apOffX, apY + apOffY));

            // 5. Ω 升交点赤经弧中点
            double lanArcR = diskR * 0.55;
            double oMidAng = oRad * 0.5;
            ProjectWorldToScreenFloat(lanArcR * Math.Cos(oMidAng), lanArcR * Math.Sin(oMidAng), 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float oMidX, out float oMidY, out _);
            if (_lblArcOmega != null) SetAnchoredPositionIfChanged(_lblArcOmega.rectTransform, new Vector2(oMidX - 6f * s, oMidY - 12f * s));

            // 6. i 轨道倾角二面角弧中点 (位于升交点处)
            double baseDist = rAn * 0.88;
            double baseX = baseDist * nx, baseY = baseDist * ny;
            double uxDih = -ny, uyDih = nx;
            double arcRDih = 14.0 * s;
            double halfI = iRad * 0.5;
            double iMidWx = baseX + arcRDih * uxDih * Math.Cos(halfI);
            double iMidWy = baseY + arcRDih * uyDih * Math.Cos(halfI);
            double iMidWz = arcRDih * Math.Sin(halfI);
            ProjectWorldToScreenFloat(iMidWx, iMidWy, iMidWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float iMidX, out float iMidY, out _);
            if (_lblArcInc != null) SetAnchoredPositionIfChanged(_lblArcInc.rectTransform, new Vector2(iMidX + 14f * s, iMidY + 2f * s));

            // 7. h 轨道角动量矢量与 e 近拱点矢量
            double hLen = diskR * 1.15;
            ProjectWorldToScreenFloat(hLen * hx, hLen * hy, hLen * hz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float hX, out float hY, out _);
            if (_lblVectorH != null) SetAnchoredPositionIfChanged(_lblVectorH.rectTransform, new Vector2(hX - 12f * s, hY + 6f * s));

            double eLen = maxOrbitR + 22.0 * s;
            ProjectWorldToScreenFloat(eLen * edirX, eLen * edirY, eLen * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eX, out float eY, out _);
            if (_lblVectorE != null) SetAnchoredPositionIfChanged(_lblVectorE.rectTransform, new Vector2(eX + 12f * s, eY + 4f * s));

            // 8. ω 近拱点辐角弧中点 (在轨道面内从 AN 指向 e)
            double aopArcR = diskR * 0.40;
            double halfW = wRad * 0.5;
            double wMidWx = aopArcR * (Math.Cos(halfW) * nx + Math.Sin(halfW) * hCrossNx);
            double wMidWy = aopArcR * (Math.Cos(halfW) * ny + Math.Sin(halfW) * hCrossNy);
            double wMidWz = aopArcR * (Math.Cos(halfW) * 0.0 + Math.Sin(halfW) * hCrossNz);
            ProjectWorldToScreenFloat(wMidWx, wMidWy, wMidWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float wMidX, out float wMidY, out _);
            if (_lblArcAop != null) SetAnchoredPositionIfChanged(_lblArcAop.rectTransform, new Vector2(wMidX + 10f * s, wMidY - 6f * s));

            // 9. ν 真近点角弧中点 (在轨道面内从 e 指向航天器矢径)
            double taArcR = diskR * 0.32;
            double halfV = vRad * 0.5;
            double taMidWx = taArcR * (Math.Cos(halfV) * edirX + Math.Sin(halfV) * qdirX);
            double taMidWy = taArcR * (Math.Cos(halfV) * edirY + Math.Sin(halfV) * qdirY);
            double taMidWz = taArcR * (Math.Cos(halfV) * edirZ + Math.Sin(halfV) * qdirZ);
            ProjectWorldToScreenFloat(taMidWx, taMidWy, taMidWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float taMidX, out float taMidY, out _);
            if (_lblArcTa != null) SetAnchoredPositionIfChanged(_lblArcTa.rectTransform, new Vector2(taMidX + 10f * s, taMidY - 6f * s));
        }

        // ═════════════════════════════════════════════════════════════════
        // 核心渲染：天体力学开普勒六根数经典权威定义图 (Classical Keplerian Diagram)
        // 1. 惯性空间基准坐标系：X 轴 (春分点), Y 轴 (赤道正交), Z 轴 (自转极轴) 带矢量箭头
        // 2. 赤道参考面 (Equatorial Plane)：半透明椭圆投影盘，引线标注 "EQ"
        // 3. 升交线 (Line of Nodes)：穿透赤道盘与轨道面的交线，升交方向实线箭头，降交方向虚线
        // 4. 升交点赤经 Ω：赤道面上 X 轴至升交线的夹角圆弧 (显式角度扫掠，任意 Ω 均正确)
        // 5. 轨道倾角 i：Z 轴与角动量矢量 h 的球面夹角弧 + 升交点处赤道面到轨道面的真实二面角弧
        // 6. 轨道角动量矢量 h：垂直于轨道平面的法向长矢量，带箭头标注 "h"
        // 7. 偏心率/近拱点矢量 e：原点沿长轴指向近拱点的矢量，带箭头标注 "e"
        // 8. 近拱点辐角 ω：轨道面内升交线至近拱点矢量 e 的夹角弧 (显式角度扫掠，ω>180° 不失真)
        // 9. 航天器与位置矢径 r：原点至航天器的径向矢量，中间标注 "r"，航天器实心圆点标注 "SC"
        // 10. 速度矢量 v：航天器沿轨道瞬时切线方向射出的矢量 (dP/dν 解析方向)
        // 11. 真近点角 ν：轨道面内近拱点矢量 e 至航天器矢径 r 的夹角弧 (显式角度扫掠)
        // 12. 轨道曲线：按**相机空间深度**前后分层 —— 近侧半周高亮发光线，远侧半周纤细暗线，
        //     中央行星球体严格遮挡远侧半周 (深度为唯一判据，不再用赤道面上下符号近似)
        // 13. 远拱点 (AP) 与 近拱点 (PE)：空间实测节点高亮标记并附带 AP / PE 动态标识
        // ═════════════════════════════════════════════════════════════════
        internal void PopulateOrbitMesh(VertexHelper vh)
        {
            vh.Clear();
            if (double.IsNaN(_lastDrawnSma)) return;

            double sma = _lastDrawnSma;
            double ecc = _lastDrawnEcc;
            double inc = _lastDrawnInc;
            double lan = _lastDrawnLan;
            double aop = _lastDrawnAop;
            double ap = _lastDrawnAp;
            double pe = _lastDrawnPe;
            double tra = double.IsNaN(_lastDrawnTra) ? 0.0 : _lastDrawnTra;

            // 观察视角配置 (正交轴测相机，对齐教科书经典定义图视角：X 向左下，Y 向右，Z 向上)
            float s = _cachedDpiScale > 0.01f ? _cachedDpiScale : 1f;
            float cx = 0f;
            float cy = -8f * s;
            double camPitch = 25.0 * Math.PI / 180.0;     // 25° 俯仰角：立体且直观
            double camYaw = -115.0 * Math.PI / 180.0;
            double cosCp = Math.Cos(camPitch), sinCp = Math.Sin(camPitch);
            double cosCy = Math.Cos(camYaw), sinCy = Math.Sin(camYaw);

            double diskR = 140.0 * s;            // 赤道参考盘半径基准
            double maxOrbitR = diskR * 1.20;     // 轨道最大径

            // 轨道形状：闭合椭圆 / 开放双曲线·抛物线 双路径解算
            bool closed = ecc < 1.0;
            double eDraw = closed ? Math.Min(Math.Max(0.0, ecc), 0.96) : Math.Min(Math.Max(1.0, ecc), 4.0);
            double pShp = sma * (1.0 - eDraw * eDraw);   // 焦点参数 p = a(1-e²)；双曲线 sma<0 → p>0
            double scale;
            if (closed)
            {
                scale = (sma > 1.0) ? (maxOrbitR / (sma * (1.0 + eDraw))) : 1.0;
            }
            else
            {
                if (!(pShp > 0.0) || double.IsInfinity(pShp))
                    pShp = Math.Max(1.0, DefaultKerbinRadius + pe) * (1.0 + eDraw);
                scale = (diskR * 0.55) * (1.0 + eDraw) / pShp;
            }
            double pScale = pShp * scale;

            // 角度弧度
            double iRad = inc * Math.PI / 180.0;
            double oRad = lan * Math.PI / 180.0;
            double wRad = aop * Math.PI / 180.0;
            double vRad = tra * Math.PI / 180.0;

            // 矢量图元尺寸 (随 DPI 等比缩放，确保高分屏锐利可辨)
            float arrowHead = 7f * s;
            float lineW = 1.0f * s;
            float lineWThick = 1.25f * s;
            float lineWGlow = 2.6f * s;
            float dotR = 1.6f * s;
            float markerR = 3.6f * s;
            float markerW = 1.1f * s;
            float planetR = 20.0f * s;

            // ─────────────────────────────────────────────────────────────
            // 1. 绘制底座：赤道参考面 (Equatorial Plane Disk)
            // ─────────────────────────────────────────────────────────────
            DrawEquatorialDiskFilled(vh, cx, cy, diskR, cosCp, sinCp, cosCy, sinCy, s);
            ProjectWorldToScreenFloat(diskR * 0.40, diskR * 0.75, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eqP0X, out float eqP0Y, out _);
            float eqP1X = eqP0X + 18f * s;
            float eqP1Y = eqP0Y + 8f * s;
            DrawFilledCircle(vh, eqP0X, eqP0Y, dotR, _cAxis);
            DrawAALine(vh, eqP0X, eqP0Y, eqP1X, eqP1Y, _cAxis, 0.9f * s);

            // ─────────────────────────────────────────────────────────────
            // 2. 绘制惯性参考坐标轴 X, Y, Z
            // ─────────────────────────────────────────────────────────────
            double axisLenX = diskR * 1.35;    // X 轴：春分点方向
            double axisLenY = diskR * 1.30;    // Y 轴：赤道正交
            double axisLenZ = diskR * 1.40;    // Z 轴：自转极轴

            ProjectWorldToScreenFloat(axisLenX, 0.0, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float xEndX, out float xEndY, out _);
            DrawArrow(vh, cx, cy, xEndX, xEndY, _cAxis, lineW, arrowHead);

            ProjectWorldToScreenFloat(0.0, axisLenY, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float yEndX, out float yEndY, out _);
            DrawArrow(vh, cx, cy, yEndX, yEndY, _cAxis, lineW, arrowHead);

            ProjectWorldToScreenFloat(0.0, 0.0, axisLenZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out float zEndX, out float zEndY, out _);
            DrawArrow(vh, cx, cy, zEndX, zEndY, _cAxis, lineW, arrowHead);

            // ─────────────────────────────────────────────────────────────
            // 3. 升交线与 Ω 弧
            // ─────────────────────────────────────────────────────────────
            double nodeLen = diskR * 1.20;
            double nx = Math.Cos(oRad), ny = Math.Sin(oRad);
            ProjectWorldToScreenFloat(nodeLen * nx, nodeLen * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float anEndX, out float anEndY, out _);
            DrawArrow(vh, cx, cy, anEndX, anEndY, _cNode, lineWThick, arrowHead);

            ProjectWorldToScreenFloat(-diskR * 0.95 * nx, -diskR * 0.95 * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float dnEndX, out float dnEndY, out _);
            DrawDashedLine(vh, cx, cy, dnEndX, dnEndY, _cNode, 0.9f * s);

            double lanArcR = diskR * 0.55;
            DrawEquatorialArc(vh, cx, cy, lanArcR, 0.0, oRad, cosCp, sinCp, cosCy, sinCy, _cElemLan, lineW);

            // ─────────────────────────────────────────────────────────────
            // 4. 空间开普勒轨道三维单位基底解算
            // ─────────────────────────────────────────────────────────────
            double hx = Math.Sin(iRad) * Math.Sin(oRad);
            double hy = -Math.Sin(iRad) * Math.Cos(oRad);
            double hz = Math.Cos(iRad);

            double hCrossNx = hy * 0.0 - hz * ny;
            double hCrossNy = hz * nx - hx * 0.0;
            double hCrossNz = hx * ny - hy * nx;

            double edirX = Math.Cos(wRad) * nx + Math.Sin(wRad) * hCrossNx;
            double edirY = Math.Cos(wRad) * ny + Math.Sin(wRad) * hCrossNy;
            double edirZ = Math.Cos(wRad) * 0.0 + Math.Sin(wRad) * hCrossNz;

            double qdirX = hy * edirZ - hz * edirY;
            double qdirY = hz * edirX - hx * edirZ;
            double qdirZ = hx * edirY - hy * edirX;

            // ─────────────────────────────────────────────────────────────
            // 5. 轨道角动量矢量 h 与 倾角 i 空间二面角弧
            // ─────────────────────────────────────────────────────────────
            double hLen = diskR * 1.15;
            ProjectWorldToScreenFloat(hLen * hx, hLen * hy, hLen * hz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float hEndX, out float hEndY, out _);
            DrawArrow(vh, cx, cy, hEndX, hEndY, _cVectorH, 1.3f * s, arrowHead);

            // 二面角 i 弧线 (在升交点处，赤道平面向轨道平面扫掠)
            double rAnArc = diskR * 0.85;
            DrawDihedralInclinationArc(vh, nx, ny, rAnArc, iRad, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemI, lineW, s);

            // ─────────────────────────────────────────────────────────────
            // 6. 偏心率/近拱点矢量 e 与 近拱点辐角 ω 弧
            // ─────────────────────────────────────────────────────────────
            double eLen = maxOrbitR + 20.0 * s;
            ProjectWorldToScreenFloat(eLen * edirX, eLen * edirY, eLen * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eEndX, out float eEndY, out _);
            DrawArrow(vh, cx, cy, eEndX, eEndY, _cVectorE, lineWThick, arrowHead);

            DrawPlanarSweepArc(vh, nx, ny, 0.0, hCrossNx, hCrossNy, hCrossNz, wRad, diskR * 0.40, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemAop, lineW);

            // ─────────────────────────────────────────────────────────────
            // 6.5 真近点角 ν 弧 (轨道面内从近地点方向 e 扫向当前航天器矢量)
            // ─────────────────────────────────────────────────────────────
            if (vRad > 0.05 || vRad < -0.05)
            {
                DrawPlanarSweepArc(vh, edirX, edirY, edirZ, qdirX, qdirY, qdirZ, vRad, diskR * 0.28, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemTa, lineW);
            }

            // ─────────────────────────────────────────────────────────────
            // 7. 空间轨道曲线采样 (192 步高效平滑采样)
            // ─────────────────────────────────────────────────────────────
            int segments = closed ? 192 : 128;
            double nuStart, nuEnd;
            if (closed)
            {
                nuStart = 0.0;
                nuEnd = 2.0 * Math.PI;
            }
            else
            {
                double rMax = diskR * 2.7;
                double cosLim = Math.Min(1.0, Math.Max(-1.0, (pScale / rMax - 1.0) / eDraw));
                double nuLim = Math.Acos(cosLim);
                nuStart = -nuLim;
                nuEnd = nuLim;
            }

            for (int k = 0; k <= segments; k++)
            {
                double nu = nuStart + (nuEnd - nuStart) * (k / (double)segments);
                double den = 1.0 + eDraw * Math.Cos(nu);
                if (den < 1e-6) den = 1e-6;
                double rCur = pScale / den;

                double px = rCur * (Math.Cos(nu) * edirX + Math.Sin(nu) * qdirX);
                double py = rCur * (Math.Cos(nu) * edirY + Math.Sin(nu) * qdirY);
                double pz = rCur * (Math.Cos(nu) * edirZ + Math.Sin(nu) * qdirZ);

                ProjectWorldToScreenFloat(px, py, pz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out double depth);
                _orbitPts[k] = new Vector2(sx, sy);
                _orbitFront[k] = depth <= 0.0;
            }

            // 远侧半周
            for (int k = 0; k < segments; k++)
            {
                if (!_orbitFront[k] || !_orbitFront[k + 1])
                {
                    DrawAALine(vh, _orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitBack, 0.9f * s);
                }
            }

            // 8. 原点实体引力天体 (小行星球，遮挡远侧半周)
            DrawMiniPlanetSphere(vh, cx, cy, planetR, s);

            // 近侧高亮发光轨道段
            for (int k = 0; k < segments; k++)
            {
                if (_orbitFront[k] && _orbitFront[k + 1])
                {
                    DrawAALine(vh, _orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitGlow, lineWGlow);
                    DrawAALine(vh, _orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitFront, lineWThick);
                }
            }

            // 8.5 近拱点 (PE) 与 远拱点 (AP)
            double rPe = pScale / (1.0 + eDraw);
            double peWx = rPe * edirX;
            double peWy = rPe * edirY;
            double peWz = rPe * edirZ;
            ProjectWorldToScreenFloat(peWx, peWy, peWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float peX, out float peY, out _);

            DrawHollowCircle(vh, peX, peY, markerR, _cApPe, markerW);
            DrawFilledCircle(vh, peX, peY, dotR, _cApPe);

            if (closed)
            {
                double rAp = pScale / (1.0 - eDraw);
                double apWx = -rAp * edirX;
                double apWy = -rAp * edirY;
                double apWz = -rAp * edirZ;
                ProjectWorldToScreenFloat(apWx, apWy, apWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float apX, out float apY, out _);

                DrawDashedLine(vh, cx, cy, apX, apY, _cApPe, 0.9f * s);
                DrawHollowCircle(vh, apX, apY, markerR, _cApPe, markerW);
                DrawFilledCircle(vh, apX, apY, dotR, _cApPe);

                double rAn = pScale / (1.0 + eDraw * Math.Cos(-wRad));
                ProjectWorldToScreenFloat(rAn * nx, rAn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float anNodeX, out float anNodeY, out _);
                DrawHollowCircle(vh, anNodeX, anNodeY, markerR, _cNode, markerW);

                double rDn = pScale / (1.0 + eDraw * Math.Cos(Math.PI - wRad));
                ProjectWorldToScreenFloat(-rDn * nx, -rDn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float dnNodeX, out float dnNodeY, out _);
                DrawHollowCircle(vh, dnNodeX, dnNodeY, markerR, _cNode, markerW);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 3D 空间到屏幕轴测投影引擎 (亚像素高精浮点版)
        // ═════════════════════════════════════════════════════════════════

        private static void ProjectWorldToScreenFloat(
            double wx, double wy, double wz,
            double cosCp, double sinCp, double cosCy, double sinCy,
            float cx, float cy,
            out float sx, out float sy, out double depth)
        {
            // 偏航 (Yaw 绕 Z)
            double xCam1 = wx * cosCy - wy * sinCy;
            double yCam1 = wx * sinCy + wy * cosCy;
            double zCam1 = wz;

            // 俯仰 (Pitch 绕 X)
            double xScreen = xCam1;
            double yScreen = yCam1 * sinCp + zCam1 * cosCp;
            depth = yCam1 * cosCp - zCam1 * sinCp;

            sx = cx + (float)xScreen;
            sy = cy + (float)yScreen;
        }

        // ═════════════════════════════════════════════════════════════════
        // UGUI GPU 矢量网格原语 (0 CPU 像素光栅化，Direct VertexHelper Pipeline)
        // ═════════════════════════════════════════════════════════════════
        private static void DrawAALine(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c, float width = 1.0f)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return;

            float nx = -dy / len;
            float ny = dx / len;

            // 真正的亚像素级边缘羽化抗锯齿 (Fringe Anti-Aliasing)
            // 核心保持纯色实体，两侧 0.85px 硬件插值至透明
            float fringe = 0.85f;
            float halfWidth = width * 0.5f;

            Color32 cFade = c;
            cFade.a = 0;

            if (halfWidth <= 0.5f)
            {
                // 超细亚像素线段：6 顶点双条带 (左羽化 -> 中心脊线 -> 右羽化)
                byte spineAlpha = (byte)Mathf.Clamp((int)(c.a * (halfWidth / 0.5f)), 0, 255);
                Color32 cSpine = c;
                cSpine.a = spineAlpha;

                int idx = vh.currentVertCount;
                vh.AddVert(new Vector3(x0 - nx * fringe, y0 - ny * fringe, 0f), cFade, Vector2.zero);
                vh.AddVert(new Vector3(x0, y0, 0f), cSpine, Vector2.zero);
                vh.AddVert(new Vector3(x0 + nx * fringe, y0 + ny * fringe, 0f), cFade, Vector2.zero);

                vh.AddVert(new Vector3(x1 - nx * fringe, y1 - ny * fringe, 0f), cFade, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1, 0f), cSpine, Vector2.zero);
                vh.AddVert(new Vector3(x1 + nx * fringe, y1 + ny * fringe, 0f), cFade, Vector2.zero);

                vh.AddTriangle(idx, idx + 1, idx + 4);
                vh.AddTriangle(idx, idx + 4, idx + 3);

                vh.AddTriangle(idx + 1, idx + 2, idx + 5);
                vh.AddTriangle(idx + 1, idx + 5, idx + 4);
            }
            else
            {
                // 标准/宽幅线段：8 顶点三条带 (左羽化 -> 实体核心 -> 右羽化)
                float rCore = halfWidth - 0.4f;
                float rOut = rCore + fringe;
                int idx = vh.currentVertCount;

                vh.AddVert(new Vector3(x0 - nx * rOut, y0 - ny * rOut, 0f), cFade, Vector2.zero);
                vh.AddVert(new Vector3(x0 - nx * rCore, y0 - ny * rCore, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x0 + nx * rCore, y0 + ny * rCore, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x0 + nx * rOut, y0 + ny * rOut, 0f), cFade, Vector2.zero);

                vh.AddVert(new Vector3(x1 - nx * rOut, y1 - ny * rOut, 0f), cFade, Vector2.zero);
                vh.AddVert(new Vector3(x1 - nx * rCore, y1 - ny * rCore, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x1 + nx * rCore, y1 + ny * rCore, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x1 + nx * rOut, y1 + ny * rOut, 0f), cFade, Vector2.zero);

                // 左羽化 Quad
                vh.AddTriangle(idx, idx + 1, idx + 5);
                vh.AddTriangle(idx, idx + 5, idx + 4);

                // 核心实心 Quad
                vh.AddTriangle(idx + 1, idx + 2, idx + 6);
                vh.AddTriangle(idx + 1, idx + 6, idx + 5);

                // 右羽化 Quad
                vh.AddTriangle(idx + 2, idx + 3, idx + 7);
                vh.AddTriangle(idx + 2, idx + 7, idx + 6);
            }
        }

        private static void DrawFilledCircle(VertexHelper vh, float cx, float cy, float r, Color32 c, int segs = 32)
        {
            if (r < 0.1f) return;
            int centerIdx = vh.currentVertCount;
            vh.AddVert(new Vector3(cx, cy, 0f), c, Vector2.zero);

            float rInner = Mathf.Max(0f, r - 0.75f);
            float rOuter = r + 0.75f;
            Color32 cFade = c;
            cFade.a = 0;

            float step = (Mathf.PI * 2f) / segs;

            if (rInner < 0.2f)
            {
                for (int i = 0; i <= segs; i++)
                {
                    float ang = i * step;
                    vh.AddVert(new Vector3(cx + Mathf.Cos(ang) * rOuter, cy + Mathf.Sin(ang) * rOuter, 0f), cFade, Vector2.zero);
                    if (i > 0)
                    {
                        vh.AddTriangle(centerIdx, centerIdx + i, centerIdx + i + 1);
                    }
                }
            }
            else
            {
                int innerStart = vh.currentVertCount;
                for (int i = 0; i <= segs; i++)
                {
                    float ang = i * step;
                    vh.AddVert(new Vector3(cx + Mathf.Cos(ang) * rInner, cy + Mathf.Sin(ang) * rInner, 0f), c, Vector2.zero);
                    if (i > 0)
                    {
                        vh.AddTriangle(centerIdx, innerStart + i - 1, innerStart + i);
                    }
                }

                int outerStart = vh.currentVertCount;
                for (int i = 0; i <= segs; i++)
                {
                    float ang = i * step;
                    vh.AddVert(new Vector3(cx + Mathf.Cos(ang) * rOuter, cy + Mathf.Sin(ang) * rOuter, 0f), cFade, Vector2.zero);
                    if (i > 0)
                    {
                        int in0 = innerStart + i - 1;
                        int in1 = innerStart + i;
                        int out0 = outerStart + i - 1;
                        int out1 = outerStart + i;
                        vh.AddTriangle(in0, in1, out1);
                        vh.AddTriangle(in0, out1, out0);
                    }
                }
            }
        }

        private static void DrawHollowCircle(VertexHelper vh, float cx, float cy, float r, Color32 c, float width = 1.0f)
        {
            int segs = 48;
            float prevX = 0f, prevY = 0f;
            for (int i = 0; i <= segs; i++)
            {
                float ang = (i / (float)segs) * Mathf.PI * 2f;
                float px = cx + Mathf.Cos(ang) * r;
                float py = cy + Mathf.Sin(ang) * r;
                if (i > 0)
                {
                    DrawAALine(vh, prevX, prevY, px, py, c, width);
                }
                prevX = px; prevY = py;
            }
        }

        private static void DrawFilledTriangle(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Color32 c)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(p0.x, p0.y, 0f), c, Vector2.zero);
            vh.AddVert(new Vector3(p1.x, p1.y, 0f), c, Vector2.zero);
            vh.AddVert(new Vector3(p2.x, p2.y, 0f), c, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
        }

        private static void DrawArrow(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c, float width, float arrowHeadSize)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.1f) return;

            float udx = dx / len;
            float udy = dy / len;

            float stemEndX = x1 - udx * (arrowHeadSize * 0.7f);
            float stemEndY = y1 - udy * (arrowHeadSize * 0.7f);
            DrawAALine(vh, x0, y0, stemEndX, stemEndY, c, width);

            float halfBase = arrowHeadSize * 0.42f;
            float baseCenterX = x1 - udx * arrowHeadSize;
            float baseCenterY = y1 - udy * arrowHeadSize;
            float perpX = -udy * halfBase;
            float perpY = udx * halfBase;

            Vector2 tip = new Vector2(x1, y1);
            Vector2 b0 = new Vector2(baseCenterX + perpX, baseCenterY + perpY);
            Vector2 b1 = new Vector2(baseCenterX - perpX, baseCenterY - perpY);

            DrawFilledTriangle(vh, tip, b0, b1, c);
        }

        private static void DrawDashedLine(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c, float width = 0.9f, float dash = 3.5f, float gap = 2.5f)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.1f) return;

            float ux = dx / len;
            float uy = dy / len;
            float total = dash + gap;
            float cur = 0f;

            while (cur < len)
            {
                float end = Mathf.Min(cur + dash, len);
                float segX0 = x0 + ux * cur;
                float segY0 = y0 + uy * cur;
                float segX1 = x0 + ux * end;
                float segY1 = y0 + uy * end;
                DrawAALine(vh, segX0, segY0, segX1, segY1, c, width);
                cur += total;
            }
        }

        private void DrawMiniPlanetSphere(VertexHelper vh, float cx, float cy, float r, float dpiScale)
        {
            if (r < 0.1f) return;
            int centerIdx = vh.currentVertCount;
            vh.AddVert(new Vector3(cx - r * 0.25f, cy + r * 0.25f, 0f), _cPlanetSun, Vector2.zero);

            int segs = 48;
            float step = (Mathf.PI * 2f) / segs;
            for (int i = 0; i <= segs; i++)
            {
                float ang = i * step;
                float px = cx + Mathf.Cos(ang) * r;
                float py = cy + Mathf.Sin(ang) * r;
                float fx = (px - cx) / r;
                float fy = (py - cy) / r;
                float fz = Mathf.Sqrt(Mathf.Max(0f, 1f - fx * fx - fy * fy));
                float ndotl = Mathf.Clamp01(-fx * 0.5f + fy * 0.6f + fz * 0.6f);
                Color32 col = Color32.Lerp(_cPlanetShadow, _cPlanetSun, ndotl);

                vh.AddVert(new Vector3(px, py, 0f), col, Vector2.zero);
                if (i > 0)
                {
                    vh.AddTriangle(centerIdx, centerIdx + i, centerIdx + i + 1);
                }
            }
            // 赤道与极轴网格线
            DrawAALine(vh, cx - r * 0.95f, cy, cx + r * 0.95f, cy, _cPlanetGrid, 0.9f * dpiScale);
            DrawAALine(vh, cx, cy - r - 2f * dpiScale, cx, cy + r + 2f * dpiScale, _cPlanetGrid, 0.9f * dpiScale);
            // 原点实心标记
            DrawFilledCircle(vh, cx, cy, 1.8f * dpiScale, _cAxis);
        }

        private void DrawEquatorialDiskFilled(VertexHelper vh, float cx, float cy, double diskR,
            double cosCp, double sinCp, double cosCy, double sinCy, float dpiScale)
        {
            int centerIdx = vh.currentVertCount;
            vh.AddVert(new Vector3(cx, cy, 0f), _cEquatorPlane, Vector2.zero);

            int segs = 96;
            for (int i = 0; i <= segs; i++)
            {
                double th = (i / (double)segs) * Math.PI * 2.0;
                double wx = diskR * Math.Cos(th);
                double wy = diskR * Math.Sin(th);
                ProjectWorldToScreenFloat(wx, wy, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                vh.AddVert(new Vector3(sx, sy, 0f), _cEquatorPlane, Vector2.zero);
                if (i > 0)
                {
                    vh.AddTriangle(centerIdx, centerIdx + i, centerIdx + i + 1);
                }
            }

            float prevX = 0f, prevY = 0f;
            for (int i = 0; i <= segs; i++)
            {
                double th = (i / (double)segs) * Math.PI * 2.0;
                double wx = diskR * Math.Cos(th);
                double wy = diskR * Math.Sin(th);
                ProjectWorldToScreenFloat(wx, wy, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                if (i > 0)
                {
                    DrawAALine(vh, prevX, prevY, sx, sy, _cAxis, 0.9f * dpiScale);
                }
                prevX = sx; prevY = sy;
            }
        }

        private static void DrawEquatorialArc(VertexHelper vh, float cx, float cy, double r, double startAng, double endAng,
            double cosCp, double sinCp, double cosCy, double sinCy, Color32 c, float lineWidth)
        {
            double diff = endAng - startAng;
            while (diff < 0) diff += 2.0 * Math.PI;
            while (diff > 2.0 * Math.PI) diff -= 2.0 * Math.PI;

            int steps = Math.Max(24, (int)(diff * 36.0));
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double cur = startAng + diff * (k / (double)steps);
                double wx = r * Math.Cos(cur);
                double wy = r * Math.Sin(cur);
                ProjectWorldToScreenFloat(wx, wy, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);

                if (k > 0) DrawAALine(vh, prevX, prevY, sx, sy, c, lineWidth);
                if (k == steps && prevX >= 0f)
                {
                    float adx = sx - prevX;
                    float ady = sy - prevY;
                    float alen = Mathf.Sqrt(adx * adx + ady * ady);
                    if (alen > 0.001f)
                    {
                        adx /= alen; ady /= alen;
                        float ah = 6.0f * (lineWidth > 1f ? lineWidth : 1.2f);
                        float hb = ah * 0.42f;
                        DrawFilledTriangle(vh,
                            new Vector2(sx, sy),
                            new Vector2(sx - adx * ah - ady * hb, sy - ady * ah + adx * hb),
                            new Vector2(sx - adx * ah + ady * hb, sy - ady * ah - adx * hb),
                            c);
                    }
                }
                prevX = sx; prevY = sy;
            }
        }

        private static void DrawPlanarSweepArc(VertexHelper vh, double ux, double uy, double uz, double vx, double vy, double vz,
            double sweep, double r, double cosCp, double sinCp, double cosCy, double sinCy, float cx, float cy, Color32 c, float lineWidth)
        {
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (ul < 1e-9) return;
            ux /= ul; uy /= ul; uz /= ul;

            double dot = ux * vx + uy * vy + uz * vz;
            double wx = vx - dot * ux, wy = vy - dot * uy, wz = vz - dot * uz;
            double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            if (wl < 1e-9) return;
            wx /= wl; wy /= wl; wz /= wl;

            int steps = Mathf.Clamp((int)(Math.Abs(sweep) * 36.0), 24, 192);
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double a = sweep * (k / (double)steps);
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double px = r * (ca * ux + sa * wx);
                double py = r * (ca * uy + sa * wy);
                double pz = r * (ca * uz + sa * wz);

                ProjectWorldToScreenFloat(px, py, pz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                if (k > 0) DrawAALine(vh, prevX, prevY, sx, sy, c, lineWidth);
                if (k == steps && prevX >= 0f)
                {
                    float adx = sx - prevX;
                    float ady = sy - prevY;
                    float alen = Mathf.Sqrt(adx * adx + ady * ady);
                    if (alen > 0.001f)
                    {
                        adx /= alen; ady /= alen;
                        float ah = 6.0f * (lineWidth > 1f ? lineWidth : 1.2f);
                        float hb = ah * 0.42f;
                        DrawFilledTriangle(vh,
                            new Vector2(sx, sy),
                            new Vector2(sx - adx * ah - ady * hb, sy - ady * ah + adx * hb),
                            new Vector2(sx - adx * ah + ady * hb, sy - ady * ah - adx * hb),
                            c);
                    }
                }
                prevX = sx; prevY = sy;
            }
        }

        private static void DrawDihedralInclinationArc(VertexHelper vh, double nx, double ny, double baseDist, double iRad,
            double cosCp, double sinCp, double cosCy, double sinCy, float cx, float cy, Color32 c, float lineWidth, float dpiScale)
        {
            double baseX = baseDist * nx;
            double baseY = baseDist * ny;

            double ux = -ny, uy = nx;
            double arcR = 14.0 * dpiScale;
            int steps = Mathf.Clamp((int)(Math.Abs(iRad) * 36.0), 24, 96);
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double a = iRad * (k / (double)steps);
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double wx = baseX + arcR * ux * ca;
                double wy = baseY + arcR * uy * ca;
                double wz = arcR * sa;

                ProjectWorldToScreenFloat(wx, wy, wz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                if (k > 0) DrawAALine(vh, prevX, prevY, sx, sy, c, lineWidth);
                if (k == steps && prevX >= 0f)
                {
                    float adx = sx - prevX;
                    float ady = sy - prevY;
                    float alen = Mathf.Sqrt(adx * adx + ady * ady);
                    if (alen > 0.001f)
                    {
                        adx /= alen; ady /= alen;
                        float ah = 6.0f * (lineWidth > 1f ? lineWidth : 1.2f);
                        float hb = ah * 0.42f;
                        DrawFilledTriangle(vh,
                            new Vector2(sx, sy),
                            new Vector2(sx - adx * ah - ady * hb, sy - ady * ah + adx * hb),
                            new Vector2(sx - adx * ah + ady * hb, sy - ady * ah - adx * hb),
                            c);
                    }
                }
                prevX = sx; prevY = sy;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 航电标准紧凑格式化工具
        // ═════════════════════════════════════════════════════════════════

        /// <summary>角度归一化到 [0, 360) —— 兼容 NaN/Inf 与负值、超 360° 的外部探针返回</summary>
        private static double NormalizeDegrees(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return 0.0;
            double m = degrees % 360.0;
            return m < 0.0 ? m + 360.0 : m;
        }

        private static string FormatPeriodCompact(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0) return "---";
            if (seconds >= 86400.0) return $"{seconds / 86400.0:F1}d";
            int sec = (int)seconds;
            int h = sec / 3600;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            if (h > 0) return $"{h}h{m:D2}m";
            return $"{m:D2}:{s:D2}";
        }

        /// <summary>
        /// 智能角度格式化：根据角度值自适应精度显示。
        /// 整数附近 (≤0.05° 偏差) 显示整数度，
        /// 常规精度显示 1 位小数，
        /// 精细操控 (≤10°) 显示 2 位小数以支持精确调姿。
        /// </summary>
        private static string FormatAngleSmart(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "---°";

            // 整数附近自动取整 (避免 "90.0°" 冗余小数)
            double rounded = Math.Round(degrees);
            if (Math.Abs(degrees - rounded) < 0.05)
                return $"{(int)rounded}°";

            // 精细角度 (≤10°)：F2 精度以支持精确调姿
            if (degrees <= 10.0 && degrees >= -10.0)
                return $"{degrees:F2}°";

            // 常规精度
            return $"{degrees:F1}°";
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-005: 资源释放与生命周期
        // ═════════════════════════════════════════════════════════════════
        protected override void OnDestroy()
        {
            if (_modeButton != null)
                _modeButton.onClick.RemoveListener(OnModeToggle);

            base.OnDestroy();
        }
    }

    /// <summary>
    /// UGUI GPU 矢量网格宿主组件，替代传统的 CPU 像素软件光栅化与 SetPixels32
    /// </summary>
    public class OrbitalDiagramGraphic : MaskableGraphic
    {
        public OrbitalElementsWidget Widget { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Widget != null)
            {
                Widget.PopulateOrbitMesh(vh);
            }
        }
    }
}
