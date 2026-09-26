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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;
        protected override bool AutoCreateCardFrame => true;

        // ── 尺寸规格：精简模式 290×116，完整模式 300×320 ──
        private static readonly Vector2 CompactSize = new Vector2(290f, 116f);
        private static readonly Vector2 FullSize = new Vector2(300f, 320f);
        public override Vector2 BaseSize => _isFullMode ? FullSize : CompactSize;

        // ── DSL 声明式微控件 (基类全自动构建与主题纳管) ──
        public TextWidget Title = TextWidget.Title("ORBIT ELEMENTS");
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

        // ── 完整模式 3D 球体视口 ──
        private GameObject _globeRoot;
        private RawImage _globeRawImage;
        private Texture2D _globeTexture;
        private Color32[] _texPixels;
        private const float LogicalRes = 256f;   // 逻辑绘制空间边长 (全部绘图常量以此为基准)
        private int _texRes = 256;               // 实际纹理分辨率 (随 DPI 自适应, 1:1 纹素-像素)
        private float _k = 1f;                   // 逻辑单位 → 纹理像素 缩放系数

        // ── 每帧复用的采样缓冲 (几何图元不再逐帧分配数组) ──
        private readonly Vector2[] _orbitPts = new Vector2[257];
        private readonly bool[] _orbitFront = new bool[257];
        private readonly Vector2[] _diskPts = new Vector2[129];

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
        private double _lastDrawnTa = double.NaN;

        private const double DefaultKerbinRadius = 600000.0;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;

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

            _modeBtnLabel = UIFactory.CreateText(btnGo.transform, "Label", "3D SPHERE",
                Mathf.RoundToInt(7.5f * s), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform btnLblRt = _modeBtnLabel.rectTransform;
            btnLblRt.anchorMin = Vector2.zero; btnLblRt.anchorMax = Vector2.one;
            btnLblRt.sizeDelta = Vector2.zero; btnLblRt.anchoredPosition = Vector2.zero;

            // ═════════════════════════════════════════════════════════════════
            // 2. 精简模式容器 (Compact Mode: 双嵌合卡槽 + 底部角度通栏)
            // ═════════════════════════════════════════════════════════════════
            _compactRoot = new GameObject("Compact_Root", typeof(RectTransform));
            _compactRoot.transform.SetParent(transform, false);
            RectTransform cRt = _compactRoot.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero; cRt.anchorMax = Vector2.one;
            cRt.sizeDelta = Vector2.zero; cRt.anchoredPosition = Vector2.zero;

            Color slotBg = WidgetStyleManager.Surface(SurfaceStyleRole.Slot, theme);
            Color slotBorder = WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost, theme);
            Color labelCol = style.GetTextColor(TextStyleRole.Label, theme);
            Color primCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);  // 仅作构建期占位色，OnInitialize 末尾由 ApplyReadoutColors 覆盖为六根数语义色
            Color secCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            Color unitCol = style.GetTextColor(TextStyleRole.Unit, theme);

            int fsLbl = Mathf.Max(6, Mathf.RoundToInt(8f * s));
            int fsVal = Mathf.Max(8, Mathf.RoundToInt(10.5f * s));
            int fsSmall = Mathf.Max(6, Mathf.RoundToInt(7.5f * s));

            // 左卡槽：拱点几何与倒计时 (w: 132, h: 54, pos: -69, 10)
            GameObject leftSlot = CreateAvionicsSlot(_compactRoot.transform, "Left_Apsides_Slot",
                new Vector2(132f * s, 54f * s), new Vector2(-69f * s, 9f * s), slotBg, slotBorder, s);

            CreateLabelValPair(leftSlot.transform, "AP", -42f * s, 14f * s, 32f * s, 14f * s,
                22f * s, 14f * s, 76f * s, 14f * s, "AP", "---", fsLbl, fsVal, labelCol, primCol, out _, out _apVal);
            CreateLabelValPair(leftSlot.transform, "PE", -42f * s, -1f * s, 32f * s, 14f * s,
                22f * s, -1f * s, 76f * s, 14f * s, "PE", "---", fsLbl, fsVal, labelCol, primCol, out _, out _peVal);

            _tApPeReadout = UIFactory.CreateText(leftSlot.transform, "T_ApPe", "T-AP --:--  PE --:--",
                fsSmall, TextAnchor.MiddleCenter, secCol);
            SetRect(_tApPeReadout.rectTransform, 0f, -17f * s, 122f * s, 13f * s);

            // 右卡槽：轨道形态几何 (w: 132, h: 54, pos: 69, 10)
            GameObject rightSlot = CreateAvionicsSlot(_compactRoot.transform, "Right_Shape_Slot",
                new Vector2(132f * s, 54f * s), new Vector2(69f * s, 9f * s), slotBg, slotBorder, s);

            CreateLabelValPair(rightSlot.transform, "SMA", -42f * s, 14f * s, 32f * s, 14f * s,
                22f * s, 14f * s, 76f * s, 14f * s, "SMA", "---", fsLbl, fsVal, labelCol, primCol, out _, out _smaVal);
            CreateLabelValPair(rightSlot.transform, "ECC", -42f * s, -1f * s, 32f * s, 14f * s,
                22f * s, -1f * s, 76f * s, 14f * s, "ECC", "0.000", fsLbl, fsVal, labelCol, primCol, out _, out _eccVal);
            CreateLabelValPair(rightSlot.transform, "INC", -42f * s, -17f * s, 32f * s, 14f * s,
                10f * s, -17f * s, 50f * s, 14f * s, "INC", "0.0°", fsLbl, fsVal, labelCol, primCol, out _, out _incVal);

            _incDirVal = UIFactory.CreateText(rightSlot.transform, "INC_DIR", "PRO", fsSmall, TextAnchor.MiddleRight, unitCol);
            SetRect(_incDirVal.rectTransform, 48f * s, -17f * s, 24f * s, 13f * s);

            // 底部横槽：开普勒空间三姿态角 + 周期 (w: 270, h: 22, pos: 0, -38)
            GameObject bottomSlot = CreateAvionicsSlot(_compactRoot.transform, "Bottom_Angles_Slot",
                new Vector2(270f * s, 22f * s), new Vector2(0f, -38f * s), slotBg, slotBorder, s);

            CreateLabelValPair(bottomSlot.transform, "LAN", -120f * s, 0f, 16f * s, 14f * s,
                -92f * s, 0f, 40f * s, 14f * s, "Ω", "---°", fsSmall, fsSmall, labelCol, secCol, out _, out _lanVal);
            CreateLabelValPair(bottomSlot.transform, "AOP", -53f * s, 0f, 16f * s, 14f * s,
                -25f * s, 0f, 40f * s, 14f * s, "ω", "---°", fsSmall, fsSmall, labelCol, secCol, out _, out _aopVal);
            CreateLabelValPair(bottomSlot.transform, "TA", 15f * s, 0f, 16f * s, 14f * s,
                43f * s, 0f, 40f * s, 14f * s, "ν", "---°", fsSmall, fsSmall, labelCol, primCol, out _, out _taVal);
            CreateLabelValPair(bottomSlot.transform, "PER", 80f * s, 0f, 22f * s, 14f * s,
                110f * s, 0f, 38f * s, 14f * s, "PER", "--:--", fsSmall, fsSmall, labelCol, unitCol, out _, out _perVal);

            // ═════════════════════════════════════════════════════════════════
            // 3. 完整模式容器 (Full Mode: 全息 3D 轨道球 + 四角悬浮卡槽)
            // ═════════════════════════════════════════════════════════════════
            _fullRoot = new GameObject("Full_Root", typeof(RectTransform));
            _fullRoot.transform.SetParent(transform, false);
            RectTransform fRt = _fullRoot.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
            fRt.sizeDelta = Vector2.zero; fRt.anchoredPosition = Vector2.zero;
            _fullRoot.SetActive(false);

            // 中央全息球视口 (RawImage + Texture2D)
            // 纹理分辨率按视口物理像素自适应 (逻辑绘制空间恒为 256)，保证任意 DPI 下 1:1 采样不再发虚
            float globeBoxSz = 236f * s;
            float viewPx = globeBoxSz - 4f * s;
            _texRes = Mathf.Clamp(Mathf.RoundToInt(viewPx * 0.25f) * 4, 256, 512);
            _k = _texRes / LogicalRes;

            _texPixels = new Color32[_texRes * _texRes];
            _globeTexture = new Texture2D(_texRes, _texRes, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            GameObject globeBox = CreateAvionicsSlot(_fullRoot.transform, "Globe_Viewport_Frame",
                new Vector2(globeBoxSz, globeBoxSz), new Vector2(0f, -8f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme), slotBorder, s);

            _globeRoot = new GameObject("Globe_RawImage", typeof(RectTransform), typeof(RawImage));
            _globeRoot.transform.SetParent(globeBox.transform, false);
            RectTransform gRt = _globeRoot.GetComponent<RectTransform>();
            gRt.sizeDelta = new Vector2(globeBoxSz - 4f * s, globeBoxSz - 4f * s);
            gRt.anchoredPosition = Vector2.zero;

            _globeRawImage = _globeRoot.GetComponent<RawImage>();
            _globeRawImage.texture = _globeTexture;
            _globeRawImage.raycastTarget = false;

            // 四角 HUD 读数卡槽
            float badgeW = 76f * s;
            float badgeH = 34f * s;
            float cornerX = 104f * s;
            float cornerTopY = 88f * s;
            float cornerBottomY = -106f * s;

            // [左上角] 拱点与时钟
            GameObject bTopL = CreateAvionicsSlot(_fullRoot.transform, "Badge_TopLeft",
                new Vector2(badgeW, badgeH), new Vector2(-cornerX, cornerTopY), slotBg, slotBorder, s);
            _fApVal = UIFactory.CreateText(bTopL.transform, "F_AP", "AP ---", fsSmall, TextAnchor.MiddleLeft, primCol);
            SetRect(_fApVal.rectTransform, 0f, 7f * s, badgeW - 12f * s, 12f * s);
            _fPeVal = UIFactory.CreateText(bTopL.transform, "F_PE", "PE ---", fsSmall, TextAnchor.MiddleLeft, primCol);
            SetRect(_fPeVal.rectTransform, 0f, -4f * s, badgeW - 12f * s, 12f * s);
            _fTimeVal = UIFactory.CreateText(bTopL.transform, "F_TIME", "T-AP --:--", Mathf.Max(5, fsSmall - 1), TextAnchor.MiddleLeft, secCol);
            SetRect(_fTimeVal.rectTransform, 0f, -14f * s, badgeW - 12f * s, 10f * s);

            // [右上角] 轨道尺度与周期
            GameObject bTopR = CreateAvionicsSlot(_fullRoot.transform, "Badge_TopRight",
                new Vector2(badgeW, badgeH), new Vector2(cornerX, cornerTopY), slotBg, slotBorder, s);
            _fSmaVal = UIFactory.CreateText(bTopR.transform, "F_SMA", "a ---", fsSmall, TextAnchor.MiddleRight, secCol);
            SetRect(_fSmaVal.rectTransform, 0f, 7f * s, badgeW - 12f * s, 12f * s);
            _fEccVal = UIFactory.CreateText(bTopR.transform, "F_ECC", "e 0.000", fsSmall, TextAnchor.MiddleRight, secCol);
            SetRect(_fEccVal.rectTransform, 0f, -4f * s, badgeW - 12f * s, 12f * s);
            _fPeriodVal = UIFactory.CreateText(bTopR.transform, "F_PER", "P --:--", Mathf.Max(5, fsSmall - 1), TextAnchor.MiddleRight, unitCol);
            SetRect(_fPeriodVal.rectTransform, 0f, -14f * s, badgeW - 12f * s, 10f * s);

            // [左下角] 赤道参考面要素
            GameObject bBotL = CreateAvionicsSlot(_fullRoot.transform, "Badge_BotLeft",
                new Vector2(badgeW, 26f * s), new Vector2(-cornerX, cornerBottomY), slotBg, slotBorder, s);
            _fLanVal = UIFactory.CreateText(bBotL.transform, "F_LAN", "Ω 0.0°", fsSmall, TextAnchor.MiddleLeft, unitCol);
            SetRect(_fLanVal.rectTransform, 0f, 4f * s, badgeW - 12f * s, 12f * s);
            _fAopVal = UIFactory.CreateText(bBotL.transform, "F_AOP", "ω 0.0°", fsSmall, TextAnchor.MiddleLeft, unitCol);
            SetRect(_fAopVal.rectTransform, 0f, -6f * s, badgeW - 12f * s, 12f * s);

            // [右下角] 空间倾角与当前真近点角
            GameObject bBotR = CreateAvionicsSlot(_fullRoot.transform, "Badge_BotRight",
                new Vector2(badgeW, 26f * s), new Vector2(cornerX, cornerBottomY), slotBg, slotBorder, s);
            _fIncVal = UIFactory.CreateText(bBotR.transform, "F_INC", "i 0.0°", fsSmall, TextAnchor.MiddleRight, secCol);
            SetRect(_fIncVal.rectTransform, 0f, 4f * s, badgeW - 12f * s, 12f * s);
            _fTaVal = UIFactory.CreateText(bBotR.transform, "F_TA", "ν 0.0°", fsSmall, TextAnchor.MiddleRight, primCol);
            SetRect(_fTaVal.rectTransform, 0f, -6f * s, badgeW - 12f * s, 12f * s);

            // 纳管至基类标准管理器
            this.Controls.Wrap("compact_view", I18n.Tr("ORBIT_VIEW_COMPACT", "精简模式视图"), _compactRoot, null);
            this.Controls.Wrap("full_view", I18n.Tr("ORBIT_VIEW_FULL", "完整全息模式视图"), _fullRoot, null);
            if (_globeRoot != null)
                this.Controls.Register(new WidgetGraphicViewportControl("orbit_globe", I18n.Tr("ORBIT_VIEW_GLOBE", "3D轨道球视口"), _globeRoot, _globeRawImage));

            ApplyReadoutColors(theme);   // 读数先着色 (与划线同色)
            CacheThemeColors(theme);
        }

        private static GameObject CreateAvionicsSlot(Transform parent, string name, Vector2 size, Vector2 pos, Color fill, Color border, float scale)
        {
            GameObject slot = UIFactory.CreatePanel(parent, name, size, pos, fill);
            Outline ol = slot.AddComponent<Outline>();
            ol.effectDistance = new Vector2(0.8f * scale, 0.8f * scale);
            ol.effectColor = border;
            return slot;
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
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-004: 遥测驱动更新 (直接对接 IFlightTelemetry 真实开普勒要素)
        // ═════════════════════════════════════════════════════════════════
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_NO_VESSEL", "NO VESSEL");
                OrbitBadge.SetRole(TextStyleRole.Muted);
                return;
            }

            // 1. 基础拱点与时钟
            double ap = telemetry.Apoapsis;
            double pe = telemetry.Periapsis;
            double tAp = telemetry.TimeToAp;
            double tPe = telemetry.TimeToPe;

            // 2. 开普勒六根数：优先 IFlightTelemetry 接口直连，其次外部探针，最后几何回退
            double sma = telemetry.SemiMajorAxis;
            double ecc = telemetry.Eccentricity;
            double inc = telemetry.Inclination;
            double lan = telemetry.LongitudeOfAscendingNode;
            double aop = telemetry.ArgumentOfPeriapsis;
            double tra = telemetry.TrueAnomaly;
            double period = telemetry.OrbitalPeriod;

            // 2. 开普勒六根数：优先 Principia 权威摄动分析 -> 其次机载遥测 -> 其次通用探针 -> 最后几何回退
            bool isPrincipia = false;
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
            double pTra = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "TRA");
            if (!double.IsNaN(pTra)) { tra = pTra; isPrincipia = true; }

            // 优先接入 Principia 高精度交点/恒星周期
            double pNodal = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "NODALPERIOD");
            if (!double.IsNaN(pNodal) && pNodal > 0.0) { period = pNodal; isPrincipia = true; }
            else
            {
                double pSidereal = ExternalProbeRegistry.ResolveNumeric("PRINCIPIA", "SIDEREALPERIOD");
                if (!double.IsNaN(pSidereal) && pSidereal > 0.0) { period = pSidereal; isPrincipia = true; }
            }

            // 角度归一化：外部探针（Principia 等）可能返回负值或超 360° 的等价写法，
            // 若不归一化，Ω 夹角弧会被画成反向长弧，且与精简模式读数口径不一致。
            // 倾角 i 定义域为 [0, 180]（>90° 即逆行轨道），只做有限性保护与区间钳制。
            lan = NormalizeDegrees(lan);
            aop = NormalizeDegrees(aop);
            tra = NormalizeDegrees(tra);
            if (double.IsNaN(inc) || double.IsInfinity(inc)) inc = 0.0;
            inc = Math.Min(180.0, Math.Max(0.0, inc));

            // 探针或几何度回退
            if (double.IsNaN(ecc) || ecc < 0.0)
                ecc = ExternalProbeRegistry.ResolveNumeric("ORBIT", "ECC");
            if (double.IsNaN(ecc) || ecc < 0.0)
            {
                double rA = Math.Max(10000.0, DefaultKerbinRadius + ap);
                double rP = DefaultKerbinRadius + pe;
                ecc = rP <= 0.0 ? 1.05 : Math.Max(0.0, (rA - rP) / (rA + rP));
            }

            if (double.IsNaN(sma) || sma <= 0.0)
                sma = ExternalProbeRegistry.ResolveNumeric("ORBIT", "SMA");
            if (double.IsNaN(sma) || sma <= 0.0)
            {
                double rA = DefaultKerbinRadius + ap;
                double rP = DefaultKerbinRadius + pe;
                sma = (rA + rP) * 0.5;
            }

            if (double.IsNaN(inc)) inc = ExternalProbeRegistry.ResolveNumeric("ORBIT", "INC");
            if (double.IsNaN(inc)) inc = 0.0;

            if (double.IsNaN(lan)) lan = ExternalProbeRegistry.ResolveNumeric("ORBIT", "LAN");
            if (double.IsNaN(lan)) lan = 0.0;

            if (double.IsNaN(aop)) aop = ExternalProbeRegistry.ResolveNumeric("ORBIT", "LPE");
            if (double.IsNaN(aop)) aop = 0.0;

            if (double.IsNaN(tra)) tra = ExternalProbeRegistry.ResolveNumeric("ORBIT", "TRA");
            if (double.IsNaN(tra)) tra = 0.0;

            if (double.IsNaN(period) || period <= 0.0)
                period = ExternalProbeRegistry.ResolveNumeric("ORBIT", "PERIOD");
            if (double.IsNaN(period) || period <= 0.0)
                period = ecc < 1.0 ? Math.Abs(tAp - tPe) * 2.0 : 0.0;

            // Principia 参考系标题联动
            string prinFrame = ExternalProbeRegistry.ResolveString("PRINCIPIA", "NAVBALLNAME", "");
            if (string.IsNullOrEmpty(prinFrame) || prinFrame == "---")
                prinFrame = ExternalProbeRegistry.ResolveString("PRINCIPIA", "FRAME", "");
            if (!string.IsNullOrEmpty(prinFrame) && prinFrame != "---")
            {
                Title.Text = $"ORBIT [{prinFrame.ToUpperInvariant()}]";
            }
            else
            {
                Title.Text = I18n.Tr("ORBIT_TITLE", "ORBIT ELEMENTS");
            }

            // 3. 轨道能量状态胶囊 (Principia 描述优先)
            UpdateOrbitStateBadge(ap, pe, ecc, telemetry, isPrincipia);

            // 4. 数值更新
            if (_isFullMode)
            {
                UpdateFullModeReadouts(ap, pe, tAp, sma, ecc, inc, lan, aop, tra, period);
                if (CheckDirty(sma, ecc, inc, lan, aop, tra))
                {
                    _lastDrawnSma = sma; _lastDrawnEcc = ecc; _lastDrawnInc = inc;
                    _lastDrawnLan = lan; _lastDrawnAop = aop; _lastDrawnTa = tra;
                    RenderHolographicGlobe(sma, ecc, inc, lan, aop, tra, ap, pe);
                }
            }
            else
            {
                UpdateCompactModeReadouts(ap, pe, tAp, tPe, sma, ecc, inc, lan, aop, tra, period);
            }
        }

        private void UpdateOrbitStateBadge(double ap, double pe, double ecc, IFlightTelemetry telemetry, bool isPrincipia)
        {
            // 优先接入 Principia 轨道分析高阶物理描述
            if (isPrincipia)
            {
                string pDesc = ExternalProbeRegistry.ResolveString("PRINCIPIA", "ORBITDESC", "");
                if (!string.IsNullOrEmpty(pDesc) && pDesc != "---")
                {
                    string clean = pDesc.Replace("\n", " ").Trim();
                    if (clean.Length > 15) clean = clean.Substring(0, 15).Trim();
                    OrbitBadge.Text = clean.ToUpperInvariant();
                    OrbitBadge.SetRole(TextStyleRole.Accent);
                    return;
                }
            }

            double atmDepth = telemetry.AtmosphereDepth;
            bool hasAtm = telemetry.HasAtmosphere;
            double safeAlt = hasAtm ? atmDepth : 0.0;

            if (ecc >= 1.0)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_ESCAPE", "ESCAPE 逃逸");
                OrbitBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe < 0.0)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_BALLISTIC", "BALLISTIC 弹道");
                OrbitBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe < safeAlt)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_SUBORBIT", "SUBORBIT 亚轨道");
                OrbitBadge.SetRole(TextStyleRole.Warning);
            }
            else if (ecc < 0.015)
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_CIRCULAR", "CIRCULAR 圆轨");
                OrbitBadge.SetRole(TextStyleRole.Accent);
            }
            else
            {
                OrbitBadge.Text = I18n.Tr("ORBIT_BADGE_ELLIPTIC", "ELLIPTIC 椭圆轨");
                OrbitBadge.SetRole(TextStyleRole.PrimaryValue);
            }
        }

        private void UpdateCompactModeReadouts(double ap, double pe, double tAp, double tPe,
            double sma, double ecc, double inc, double lan, double aop, double tra, double period)
        {
            SetTextIfChanged(_apVal, FormatDistanceMetric(ap));
            SetTextIfChanged(_peVal, pe < -100000.0 ? I18n.Tr("ORBIT_VAL_IMPACT", "IMPACT") : FormatDistanceMetric(pe));
            SetTextIfChanged(_tApPeReadout, $"T-AP {FormatTimeCompact(tAp)}  PE {FormatTimeCompact(tPe)}");

            SetTextIfChanged(_smaVal, FormatDistanceMetric(sma));
            SetTextIfChanged(_eccVal, ecc.ToString("F4"));
            SetTextIfChanged(_incVal, $"{inc:F1}°");
            if (_incDirVal != null)
            {
                _incDirVal.text = inc > 90.0 ? I18n.Tr("ORBIT_DIR_RET", "RET") : I18n.Tr("ORBIT_DIR_PRO", "PRO");
                _incDirVal.color = inc > 90.0 ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, null) : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Unit, null);
            }

            SetTextIfChanged(_lanVal, $"{lan:F1}°");
            SetTextIfChanged(_aopVal, $"{aop:F1}°");
            SetTextIfChanged(_taVal, $"{tra:F1}°");
            SetTextIfChanged(_perVal, FormatPeriodCompact(period));
        }

        private void UpdateFullModeReadouts(double ap, double pe, double tAp,
            double sma, double ecc, double inc, double lan, double aop, double tra, double period)
        {
            SetTextIfChanged(_fApVal, $"AP {FormatDistanceMetric(ap)}");
            SetTextIfChanged(_fPeVal, $"PE {FormatDistanceMetric(pe)}");
            SetTextIfChanged(_fTimeVal, $"T-AP {FormatTimeCompact(tAp)}");

            SetTextIfChanged(_fSmaVal, $"a {FormatDistanceMetric(sma)}");
            SetTextIfChanged(_fEccVal, $"e {ecc:F4}");
            SetTextIfChanged(_fPeriodVal, $"P {FormatPeriodCompact(period)}");

            SetTextIfChanged(_fLanVal, $"Ω {lan:F1}°");
            SetTextIfChanged(_fAopVal, $"ω {aop:F1}°");

            SetTextIfChanged(_fIncVal, $"i {inc:F1}°");
            SetTextIfChanged(_fTaVal, $"ν {tra:F1}°");
        }

        /// <summary>
        /// 渲染脏标记防抖：任一开普勒要素变化超过阈值才重绘全息图。
        /// 角度阈值取 0.2°（最外圈轨道点位移约 0.3px，仍在亚像素内），在保证画面连续的前提下压低重绘频率。
        /// </summary>
        private bool CheckDirty(double sma, double ecc, double inc, double lan, double aop, double tra)
        {
            if (double.IsNaN(_lastDrawnSma)) return true;
            double relSma = Math.Abs(sma - _lastDrawnSma) / Math.Max(1.0, _lastDrawnSma);
            double dTra = Math.Abs(tra - _lastDrawnTa);
            if (dTra > 180.0) dTra = 360.0 - dTra;

            return relSma > 0.0005
                || Math.Abs(ecc - _lastDrawnEcc) > 0.0005
                || Math.Abs(inc - _lastDrawnInc) > 0.2
                || Math.Abs(lan - _lastDrawnLan) > 0.2
                || Math.Abs(aop - _lastDrawnAop) > 0.2
                || dTra > 0.2;
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
        private void RenderHolographicGlobe(double sma, double ecc, double inc, double lan, double aop, double tra, double ap, double pe)
        {
            if (_globeTexture == null || _texPixels == null) return;

            // 1. 清空画布
            for (int i = 0; i < _texPixels.Length; i++)
                _texPixels[i] = _cClear;

            // 观察视角配置 (正交轴测相机，对齐教科书经典定义图视角：X 向左下，Y 向右，Z 向上)
            float cx = 128f;
            float cy = 118f;
            double camPitch = 22.0 * Math.PI / 180.0;
            double camYaw = -115.0 * Math.PI / 180.0;
            double cosCp = Math.Cos(camPitch), sinCp = Math.Sin(camPitch);
            double cosCy = Math.Cos(camYaw), sinCy = Math.Sin(camYaw);

            double diskR = 68.0;                 // 赤道参考盘半径基准
            double maxOrbitR = diskR * 1.15;

            // 轨道形状：闭合椭圆 / 开放双曲线·抛物线 双路径解算
            //   closed : 按远拱点归一化 (远拱点恒落在 maxOrbitR)
            //   open   : 按近拱点归一化并在 ν∞ 开区间内采样 → 轨迹自然冲出画幅，绝不虚假闭合
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
                    pShp = Math.Max(1.0, DefaultKerbinRadius + pe) * (1.0 + eDraw);   // 抛物线渐近退化保护
                scale = (diskR * 0.55) * (1.0 + eDraw) / pShp;
            }
            double pScale = pShp * scale;        // 已含尺度归一化的焦点参数

            // 角度弧度
            double iRad = inc * Math.PI / 180.0;
            double oRad = lan * Math.PI / 180.0;
            double wRad = aop * Math.PI / 180.0;
            double vRad = tra * Math.PI / 180.0;

            // ─────────────────────────────────────────────────────────────
            // 1. 绘制底座：赤道参考面 (Equatorial Plane Disk)
            // ─────────────────────────────────────────────────────────────
            DrawEquatorialDiskFilled(cx, cy, diskR, cosCp, sinCp, cosCy, sinCy);
            // 标注：赤道面 (带教科书引线与端点)
            ProjectWorldToScreenFloat(diskR * 0.35, diskR * 0.70, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eqP0X, out float eqP0Y, out _);
            float eqP1X = eqP0X + 16f;
            float eqP1Y = eqP0Y + 7f;
            DrawFilledCircle(eqP0X, eqP0Y, 1.4f, _cAxis);
            DrawAALine(eqP0X, eqP0Y, eqP1X, eqP1Y, _cAxis, 0.9f);
            DrawGlyphString(eqP1X + 4f, eqP1Y + 3f, "EQ", _cLabelText);

            // ─────────────────────────────────────────────────────────────
            // 2. 绘制惯性参考坐标轴 X, Y, Z
            // ─────────────────────────────────────────────────────────────
            double axisLen = diskR * 1.35;
            // X 轴 (春分点基准，赤道面内，指向左下)
            ProjectWorldToScreenFloat(axisLen, 0.0, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float xEndX, out float xEndY, out _);
            DrawArrow(cx, cy, xEndX, xEndY, _cAxis, 1.0f, 5.5f);
            DrawGlyphChar(xEndX - 9f, xEndY - 6f, 'X', _cLabelText);

            // Y 轴 (赤道面内正交轴，指向右)
            ProjectWorldToScreenFloat(0.0, axisLen * 0.95, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float yEndX, out float yEndY, out _);
            DrawArrow(cx, cy, yEndX, yEndY, _cAxis, 1.0f, 5.5f);
            DrawGlyphChar(yEndX + 4f, yEndY - 2f, 'Y', _cLabelText);

            // Z 轴 (天体自转极轴，垂直赤道面向上)
            ProjectWorldToScreenFloat(0.0, 0.0, axisLen * 1.15, cosCp, sinCp, cosCy, sinCy, cx, cy, out float zEndX, out float zEndY, out _);
            DrawArrow(cx, cy, zEndX, zEndY, _cAxis, 1.0f, 5.5f);
            DrawGlyphChar(zEndX - 9f, zEndY - 2f, 'Z', _cLabelText);

            // ─────────────────────────────────────────────────────────────
            // 3. 升交线 (Line of Nodes) 与 升交点赤经 Ω 弧
            // ─────────────────────────────────────────────────────────────
            double nodeLen = diskR * 1.30;
            double nx = Math.Cos(oRad), ny = Math.Sin(oRad);
            ProjectWorldToScreenFloat(nodeLen * nx, nodeLen * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float anEndX, out float anEndY, out _);
            DrawArrow(cx, cy, anEndX, anEndY, _cNode, 1.2f, 6.0f);

            // 降交线方向 (反向细虚线)
            ProjectWorldToScreenFloat(-diskR * nx, -diskR * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float dnEndX, out float dnEndY, out _);
            DrawDashedLine(cx, cy, dnEndX, dnEndY, _cNode, 0.9f);

            // Ω 夹角弧 (从 X 轴沿赤道面扫到升交线 n) —— 品红，与 Ω 读数同色
            double lanArcR = diskR * 0.65;
            DrawEquatorialArc(cx, cy, lanArcR, 0.0, oRad, cosCp, sinCp, cosCy, sinCy, _cElemLan, out float omegaMidX, out float omegaMidY);
            DrawGlyphChar(omegaMidX - 4f, omegaMidY - 7f, 'Ω', _cElemLan);

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
            // 5. 轨道角动量矢量 h 与 倾角 i 空间夹角弧
            // ─────────────────────────────────────────────────────────────
            double hLen = diskR * 1.25;
            ProjectWorldToScreenFloat(hLen * hx, hLen * hy, hLen * hz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float hEndX, out float hEndY, out _);
            DrawArrow(cx, cy, hEndX, hEndY, _cVectorH, 1.3f, 6.5f);
            DrawGlyphChar(hEndX - 9f, hEndY - 2f, 'h', _cVectorH);

            // 倾角 i 弧：自转极轴 Z 与轨道法向 h 之间的球面夹角弧 (扫掠角恒为 iRad) —— 冰蓝，与 i 读数同色
            DrawPlanarSweepArc(0.0, 0.0, 1.0, hx, hy, hz, iRad, diskR * 0.70, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemI, out float iMidX, out float iMidY);
            DrawGlyphChar(iMidX - 8f, iMidY, 'i', _cElemI);

            // ─────────────────────────────────────────────────────────────
            // 6. 偏心率/近拱点矢量 e 与 近拱点辐角 ω 弧
            // ─────────────────────────────────────────────────────────────
            double eLen = maxOrbitR + 24.0; // 充分延伸穿透轨道外侧
            ProjectWorldToScreenFloat(eLen * edirX, eLen * edirY, eLen * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out float eEndX, out float eEndY, out _);
            DrawArrow(cx, cy, eEndX, eEndY, _cVectorE, 1.2f, 5.5f);
            DrawGlyphChar(eEndX + 5f, eEndY + 1f, 'e', _cVectorE);

            // ω 夹角弧 (轨道面内从升交线 n 扫到近拱点矢量 e；显式角度扫掠，ω>180° 同样正确) —— 青碧，与 ω 读数同色
            DrawPlanarSweepArc(nx, ny, 0.0, hCrossNx, hCrossNy, hCrossNz, wRad, diskR * 0.48, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemAop, out float wMidX, out float wMidY);
            DrawGlyphChar(wMidX + 3f, wMidY - 3f, 'ω', _cElemAop);

            // ─────────────────────────────────────────────────────────────
            // 7. 空间轨道曲线采样 (闭合椭圆 256 步全周 / 开放轨道按 ν∞ 开区间采样)
            // ─────────────────────────────────────────────────────────────
            int segments = closed ? 256 : 192;
            double nuStart, nuEnd;
            if (closed)
            {
                nuStart = 0.0;
                nuEnd = 2.0 * Math.PI;
            }
            else
            {
                // 真近点角上限：曲线半径达到 rMax (冲出视口) 或逼近视界 ν∞ 即截断，避免 r → ∞
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
                _orbitFront[k] = depth <= 0.0;   // depth < 0 = 比行星中心更靠近相机
            }

            // 远侧半周先画 (纤细深空层次)，随后被中央行星球体严格遮挡
            for (int k = 0; k < segments; k++)
            {
                if (!_orbitFront[k] || !_orbitFront[k + 1])
                {
                    DrawAALine(_orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitBack, 0.9f);
                }
            }

            // ─────────────────────────────────────────────────────────────
            // 8. 原点微型实体引力天体 (小行星球，中心自转轴穿过)
            // ─────────────────────────────────────────────────────────────
            DrawMiniPlanetSphere(cx, cy, 9.0f);

            // 再画近侧高亮发光轨道段 (高锐利度核心线 + 柔化辉光基底)
            for (int k = 0; k < segments; k++)
            {
                if (_orbitFront[k] && _orbitFront[k + 1])
                {
                    DrawAALine(_orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitGlow, 2.2f);
                    DrawAALine(_orbitPts[k].x, _orbitPts[k].y, _orbitPts[k + 1].x, _orbitPts[k + 1].y, _cOrbitFront, 1.1f);
                }
            }

            // ─────────────────────────────────────────────────────────────
            // 8.5 近拱点 (PE) 与 远拱点 (AP) 空间标记与防压叠排版
            // ─────────────────────────────────────────────────────────────
            // 近拱点 PE (闭合椭圆与开放双曲线轨道均存在)
            double rPe = pScale / (1.0 + eDraw);
            double peWx = rPe * edirX;
            double peWy = rPe * edirY;
            double peWz = rPe * edirZ;
            ProjectWorldToScreenFloat(peWx, peWy, peWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float peX, out float peY, out _);

            // PE 数学解析纯圆 (拱点标记统一取 a 的半长轴色)
            DrawHollowCircle(peX, peY, 2.8f, _cApPe, 1.0f);
            DrawFilledCircle(peX, peY, 1.3f, _cApPe);

            // 标注 "PE" (沿轨道切向向外侧偏移 13px，绝对不与近拱点圆圈或 e 轴线重叠)
            ProjectWorldToScreenFloat(peWx + 20.0 * qdirX, peWy + 20.0 * qdirY, peWz + 20.0 * qdirZ,
                cosCp, sinCp, cosCy, sinCy, cx, cy, out float peTanX, out float peTanY, out _);
            float peTdx = peTanX - peX;
            float peTdy = peTanY - peY;
            float peTlen = Mathf.Sqrt(peTdx * peTdx + peTdy * peTdy);
            if (peTlen > 0.001f) { peTdx /= peTlen; peTdy /= peTlen; } else { peTdx = 0f; peTdy = 1f; }
            float peLblX = peX + peTdx * 13f - 4f;
            float peLblY = peY + peTdy * 13f + 3f;
            DrawGlyphString(peLblX, peLblY, "PE", _cApPe);

            // 远拱点 AP (仅闭合椭圆轨存在)
            if (closed)
            {
                double rAp = pScale / (1.0 - eDraw);
                double apWx = -rAp * edirX;
                double apWy = -rAp * edirY;
                double apWz = -rAp * edirZ;
                ProjectWorldToScreenFloat(apWx, apWy, apWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float apX, out float apY, out _);

                // 拱线虚线 (Line of Apsides)
                DrawDashedLine(cx, cy, apX, apY, _cApPe, 0.9f);

                // AP 数学解析纯圆
                DrawHollowCircle(apX, apY, 2.8f, _cApPe, 1.0f);
                DrawFilledCircle(apX, apY, 1.3f, _cApPe);

                // 标注 "AP" (沿远拱点切向外侧偏移 13px)
                ProjectWorldToScreenFloat(apWx - 20.0 * qdirX, apWy - 20.0 * qdirY, apWz - 20.0 * qdirZ,
                    cosCp, sinCp, cosCy, sinCy, cx, cy, out float apTanX, out float apTanY, out _);
                float apTdx = apTanX - apX;
                float apTdy = apTanY - apY;
                float apTlen = Mathf.Sqrt(apTdx * apTdx + apTdy * apTdy);
                if (apTlen > 0.001f) { apTdx /= apTlen; apTdy /= apTlen; } else { apTdx = -1f; apTdy = 0f; }
                float apLblX = apX + apTdx * 13f - 5f;
                float apLblY = apY + apTdy * 13f - 3f;
                DrawGlyphString(apLblX, apLblY, "AP", _cApPe);
            }

            // 升交点 AN / 降交点 DN / 升交点二面角弧 (仅闭合轨道存在确定的轨道面-赤道面交点)
            if (closed)
            {
                double rAn = pScale / (1.0 + eDraw * Math.Cos(-wRad));
                ProjectWorldToScreenFloat(rAn * nx, rAn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float anNodeX, out float anNodeY, out _);
                DrawHollowCircle(anNodeX, anNodeY, 2.8f, _cNode, 1.0f);

                // 标注：升交点 AN (引线，Ω 品红同色)
                float anL1X = anNodeX + 14f;
                float anL1Y = anNodeY - 10f;
                DrawAALine(anNodeX, anNodeY, anL1X, anL1Y, _cNode, 0.9f);
                DrawGlyphString(anL1X + 4f, anL1Y + 3f, "AN", _cNode);

                // 升交点处"赤道面 → 轨道面"的真实二面角 i 弧 —— 冰蓝，与 i 读数同色
                DrawDihedralInclinationArc(nx, ny, rAn * 0.88, iRad, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemI, out float iDihX, out float iDihY);
                DrawGlyphChar(iDihX + 4f, iDihY - 2f, 'i', _cElemI);

                // 降交点 DN
                double rDn = pScale / (1.0 + eDraw * Math.Cos(Math.PI - wRad));
                ProjectWorldToScreenFloat(-rDn * nx, -rDn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float dnNodeX, out float dnNodeY, out _);
                DrawHollowCircle(dnNodeX, dnNodeY, 2.8f, _cNode, 1.0f);
            }

            // ─────────────────────────────────────────────────────────────
            // 9. 航天器、位置矢量 r、速度矢量 v 与 真近点角 ν
            // ─────────────────────────────────────────────────────────────
            double denSc = 1.0 + eDraw * Math.Cos(vRad);
            if (denSc < 1e-6) denSc = 1e-6;
            double rSc = pScale / denSc;
            double scWx = rSc * (Math.Cos(vRad) * edirX + Math.Sin(vRad) * qdirX);
            double scWy = rSc * (Math.Cos(vRad) * edirY + Math.Sin(vRad) * qdirY);
            double scWz = rSc * (Math.Cos(vRad) * edirZ + Math.Sin(vRad) * qdirZ);

            ProjectWorldToScreenFloat(scWx, scWy, scWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float scX, out float scY, out _);

            // 位置矢量 r
            DrawArrow(cx, cy, scX, scY, _cVectorR, 1.1f, 5.0f);
            float rMidX = (cx + scX) * 0.5f - 9f;
            float rMidY = (cy + scY) * 0.5f + 2f;
            DrawGlyphChar(rMidX, rMidY, 'r', _cVectorR);

            // 速度矢量 v (dP/dν 解析方向，与离心率 e 的真实取值一致)
            double dThetaX = -Math.Sin(vRad) * edirX + (eDraw + Math.Cos(vRad)) * qdirX;
            double dThetaY = -Math.Sin(vRad) * edirY + (eDraw + Math.Cos(vRad)) * qdirY;
            double dThetaZ = -Math.Sin(vRad) * edirZ + (eDraw + Math.Cos(vRad)) * qdirZ;
            double vMag = Math.Sqrt(dThetaX * dThetaX + dThetaY * dThetaY + dThetaZ * dThetaZ);
            if (vMag > 0.001)
            {
                dThetaX /= vMag; dThetaY /= vMag; dThetaZ /= vMag;
                double vLen = 26.0;
                ProjectWorldToScreenFloat(scWx + vLen * dThetaX, scWy + vLen * dThetaY, scWz + vLen * dThetaZ,
                    cosCp, sinCp, cosCy, sinCy, cx, cy, out float vEndX, out float vEndY, out _);
                DrawArrow(scX, scY, vEndX, vEndY, _cVectorV, 1.2f, 5.5f);
                DrawGlyphChar(vEndX - 8f, vEndY - 2f, 'v', _cVectorV);
            }

            // 真近点角 ν 夹角弧 (轨道面内自近拱点矢量 e 扫到航天器矢径 r；显式角度扫掠，ν>180° 同样正确) —— 紫罗兰，与 ν 读数同色
            DrawPlanarSweepArc(edirX, edirY, edirZ, qdirX, qdirY, qdirZ, vRad, diskR * 0.38, cosCp, sinCp, cosCy, sinCy, cx, cy, _cElemTa, out float phiMidX, out float phiMidY);
            DrawGlyphChar(phiMidX + 3f, phiMidY - 3f, 'ν', _cElemTa);

            // 航天器高亮圆点
            DrawFilledCircle(scX, scY, 3.8f, _cVesselGlow);
            DrawFilledCircle(scX, scY, 1.8f, _cVessel);
            DrawGlyphString(scX - 11f, scY + 7f, "SC", _cLabelText);

            // 提交纹理
            _globeTexture.SetPixels32(_texPixels);
            _globeTexture.Apply(false);
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
        // 几何图示绘制原语 (锐利航电亚像素抗锯齿引擎)
        // 分两层：
        //   · 底层 *T 系列 —— 输入为**纹理像素**坐标，边界按 _texRes 裁剪；
        //   · 上层公开 API —— 输入为**逻辑**坐标 (256 逻辑空间)，内部乘 _k 映射到纹素。
        // 这样绘图常量与 DPI / 纹理分辨率彻底解耦，高 DPI 下 1:1 采样不发虚。
        // 合成统一为严格的 straight-alpha "over"：覆盖率只施加一次，AA 边缘与半透明色与设计值一致。
        // ═════════════════════════════════════════════════════════════════

        private void BlendPixel(int x, int y, Color32 c)
        {
            BlendPixelAlpha(x, y, c, 1f);
        }

        private void BlendPixelAlpha(int x, int y, Color32 c, float coverage)
        {
            if (x < 0 || x >= _texRes || y < 0 || y >= _texRes) return;

            float sa = (c.a * (1f / 255f)) * Mathf.Clamp01(coverage);
            if (sa <= 0.002f) return;

            int idx = y * _texRes + x;
            Color32 d = _texPixels[idx];
            float da = d.a * (1f / 255f);

            // out.a = as + ad(1-as) ； out.rgb = (cs·as + cd·ad·(1-as)) / out.a
            float oa = sa + da * (1f - sa);
            float w = da * (1f - sa);
            Color o = d;
            o.r = (c.r * sa + d.r * w) / (oa * 255f);
            o.g = (c.g * sa + d.g * w) / (oa * 255f);
            o.b = (c.b * sa + d.b * w) / (oa * 255f);
            o.a = oa;
            _texPixels[idx] = (Color32)o;
        }

        private void DrawFilledCircleT(float cx, float cy, float r, Color32 c)
        {
            float feather = 0.65f;
            int minX = Mathf.Clamp((int)Mathf.Floor(cx - r - feather), 0, _texRes - 1);
            int maxX = Mathf.Clamp((int)Mathf.Ceil(cx + r + feather), 0, _texRes - 1);
            int minY = Mathf.Clamp((int)Mathf.Floor(cy - r - feather), 0, _texRes - 1);
            int maxY = Mathf.Clamp((int)Mathf.Ceil(cy + r + feather), 0, _texRes - 1);

            for (int y = minY; y <= maxY; y++)
            {
                float dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= r)
                    {
                        BlendPixelAlpha(x, y, c, 1.0f);
                    }
                    else if (dist <= r + feather)
                    {
                        float cov = 1.0f - (dist - r) / feather;
                        BlendPixelAlpha(x, y, c, cov);
                    }
                }
            }
        }

        private void DrawFilledCircle(float cx, float cy, float r, Color32 c)
        {
            DrawFilledCircleT(cx * _k, cy * _k, r * _k, c);
        }

        private void DrawAALineT(float x0, float y0, float x1, float y1, Color32 c, float width)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001f)
            {
                BlendPixelAlpha(Mathf.RoundToInt(x0), Mathf.RoundToInt(y0), c, 1.0f);
                return;
            }

            float halfW = Mathf.Max(0.4f, width * 0.5f);
            float feather = 0.65f; // 紧凑锐利过渡带，彻底消除发虚
            float expand = halfW + feather;
            int minX = Mathf.Clamp((int)Mathf.Floor(Mathf.Min(x0, x1) - expand), 0, _texRes - 1);
            int maxX = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(x0, x1) + expand), 0, _texRes - 1);
            int minY = Mathf.Clamp((int)Mathf.Floor(Mathf.Min(y0, y1) - expand), 0, _texRes - 1);
            int maxY = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(y0, y1) + expand), 0, _texRes - 1);

            float invLenSq = 1.0f / lenSq;

            for (int y = minY; y <= maxY; y++)
            {
                float py = y - y0;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x - x0;
                    float t = Mathf.Clamp01((px * dx + py * dy) * invLenSq);
                    float projX = x0 + t * dx;
                    float projY = y0 + t * dy;
                    float dX = x - projX;
                    float dY = y - projY;
                    float dist = Mathf.Sqrt(dX * dX + dY * dY);

                    if (dist <= halfW)
                    {
                        BlendPixelAlpha(x, y, c, 1.0f); // 核心像素满强度，绝对锐利
                    }
                    else if (dist <= halfW + feather)
                    {
                        float cov = 1.0f - (dist - halfW) / feather;
                        BlendPixelAlpha(x, y, c, cov);
                    }
                }
            }
        }

        private void DrawAALine(float x0, float y0, float x1, float y1, Color32 c, float width = 1.0f)
        {
            DrawAALineT(x0 * _k, y0 * _k, x1 * _k, y1 * _k, c, width * _k);
        }

        private void DrawArrow(float x0, float y0, float x1, float y1, Color32 c, float width, float arrowHeadSize)
        {
            DrawAALine(x0, y0, x1, y1, c, width);
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.1f) return;

            float udx = dx / len;
            float udy = dy / len;
            float wingLen = arrowHeadSize;
            float cosA = 0.866f; // 30 度倒角
            float sinA = 0.500f;

            float w1x = x1 - wingLen * (udx * cosA - udy * sinA);
            float w1y = y1 - wingLen * (udx * sinA + udy * cosA);
            DrawAALine(x1, y1, w1x, w1y, c, width);

            float w2x = x1 - wingLen * (udx * cosA + udy * sinA);
            float w2y = y1 - wingLen * (-udx * sinA + udy * cosA);
            DrawAALine(x1, y1, w2x, w2y, c, width);
        }

        private void DrawDashedLine(float x0, float y0, float x1, float y1, Color32 c, float width = 0.9f, float dash = 3.5f, float gap = 2.5f)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) return;

            float t = 0f;
            while (t < dist)
            {
                float tEnd = Mathf.Min(t + dash, dist);
                float sx0 = x0 + dx * (t / dist);
                float sy0 = y0 + dy * (t / dist);
                float sx1 = x0 + dx * (tEnd / dist);
                float sy1 = y0 + dy * (tEnd / dist);
                DrawAALine(sx0, sy0, sx1, sy1, c, width);
                t += dash + gap;
            }
        }

        private void DrawHollowCircleT(float cx, float cy, float r, Color32 c, float width)
        {
            float halfW = Mathf.Max(0.4f, width * 0.5f);
            float feather = 0.65f;
            float expand = halfW + feather;
            int minX = Mathf.Clamp((int)Mathf.Floor(cx - r - expand), 0, _texRes - 1);
            int maxX = Mathf.Clamp((int)Mathf.Ceil(cx + r + expand), 0, _texRes - 1);
            int minY = Mathf.Clamp((int)Mathf.Floor(cy - r - expand), 0, _texRes - 1);
            int maxY = Mathf.Clamp((int)Mathf.Ceil(cy + r + expand), 0, _texRes - 1);

            for (int y = minY; y <= maxY; y++)
            {
                float dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float dFromR = Mathf.Abs(dist - r);
                    if (dFromR <= halfW)
                    {
                        BlendPixelAlpha(x, y, c, 1.0f);
                    }
                    else if (dFromR <= halfW + feather)
                    {
                        float cov = 1.0f - (dFromR - halfW) / feather;
                        BlendPixelAlpha(x, y, c, cov);
                    }
                }
            }
        }

        private void DrawHollowCircle(float cx, float cy, float r, Color32 c, float width = 1.0f)
        {
            DrawHollowCircleT(cx * _k, cy * _k, r * _k, c, width * _k);
        }

        private void DrawEquatorialDiskFilled(float cx, float cy, double diskR,
            double cosCp, double sinCp, double cosCy, double sinCy)
        {
            int steps = 128; // 128 步平滑赤道盘
            Vector2[] pts = _diskPts;
            for (int i = 0; i <= steps; i++)
            {
                double ang = i * 2.0 * Math.PI / steps;
                double xIn = diskR * Math.Cos(ang);
                double yIn = diskR * Math.Sin(ang);
                ProjectWorldToScreenFloat(xIn, yIn, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                pts[i] = new Vector2(sx * _k, sy * _k);   // 逻辑 → 纹素
            }

            // 内部平滑半透明填充 (纹素行扫描线插值)
            int minSy = _texRes - 1, maxSy = 0;
            for (int i = 0; i < steps; i++)
            {
                int yFloor = (int)Mathf.Floor(pts[i].y);
                int yCeil = (int)Mathf.Ceil(pts[i].y);
                if (yFloor < minSy) minSy = yFloor;
                if (yCeil > maxSy) maxSy = yCeil;
            }
            minSy = Mathf.Clamp(minSy, 0, _texRes - 1);
            maxSy = Mathf.Clamp(maxSy, 0, _texRes - 1);

            for (int y = minSy; y <= maxSy; y++)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int i = 0; i < steps; i++)
                {
                    Vector2 p1 = pts[i];
                    Vector2 p2 = pts[i + 1];
                    if ((p1.y <= y && p2.y > y) || (p2.y <= y && p1.y > y))
                    {
                        float t = (y - p1.y) / (p2.y - p1.y);
                        float intersectX = p1.x + t * (p2.x - p1.x);
                        if (intersectX < minX) minX = intersectX;
                        if (intersectX > maxX) maxX = intersectX;
                    }
                }
                if (minX <= maxX)
                {
                    int ix0 = Mathf.Clamp((int)Mathf.Ceil(minX), 0, _texRes - 1);
                    int ix1 = Mathf.Clamp((int)Mathf.Floor(maxX), 0, _texRes - 1);
                    for (int x = ix0; x <= ix1; x++)
                    {
                        BlendPixel(x, y, _cEquatorPlane);
                    }
                }
            }

            // 边缘描线 (纤细 0.9f 亚像素 AA，丝滑如丝绸)
            for (int i = 0; i < steps; i++)
            {
                DrawAALineT(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, _cAxis, 0.9f * _k);
            }
        }

        private void DrawEquatorialArc(float cx, float cy, double r, double startAng, double endAng,
            double cosCp, double sinCp, double cosCy, double sinCy, Color32 c, out float midX, out float midY)
        {
            midX = cx; midY = cy;
            double diff = endAng - startAng;
            while (diff < 0) diff += 2.0 * Math.PI;
            while (diff > 2.0 * Math.PI) diff -= 2.0 * Math.PI;

            int steps = Math.Max(16, (int)(diff * 24.0));
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double cur = startAng + diff * (k / (double)steps);
                double wx = r * Math.Cos(cur);
                double wy = r * Math.Sin(cur);
                ProjectWorldToScreenFloat(wx, wy, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);

                if (k == steps / 2) { midX = sx; midY = sy; }

                if (k > 0)
                    DrawAALine(prevX, prevY, sx, sy, c, 1.0f);

                prevX = sx; prevY = sy;
            }
        }

        /// <summary>
        /// 平面显式角度扫掠弧：自单位矢量 û 起，在 span(û, v̂) 平面内按 sweep 角扫掠 (cos a·û + sin a·ŵ)。
        /// 用于 ω / ν / i 夹角弧 —— 与"两点插值再归一化"不同，扫掠量取**真实角度值**，
        /// 因此 ω>180° 或 ν>180° 时不会被退化成劣弧 (开普勒六根数图示的正确性红线)。
        /// </summary>
        private void DrawPlanarSweepArc(double ux, double uy, double uz, double vx, double vy, double vz,
            double sweep, double r, double cosCp, double sinCp, double cosCy, double sinCy, float cx, float cy, Color32 c,
            out float midX, out float midY)
        {
            midX = cx; midY = cy;

            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (ul < 1e-9) return;
            ux /= ul; uy /= ul; uz /= ul;

            // v̂ 相对 û 正交化 → 扫掠平面内的第二个正交基 ŵ
            double dot = ux * vx + uy * vy + uz * vz;
            double wx = vx - dot * ux, wy = vy - dot * uy, wz = vz - dot * uz;
            double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            if (wl < 1e-9) return;
            wx /= wl; wy /= wl; wz /= wl;

            int steps = Mathf.Clamp((int)(Math.Abs(sweep) * 24.0), 16, 256);
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double a = sweep * (k / (double)steps);
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double px = r * (ca * ux + sa * wx);
                double py = r * (ca * uy + sa * wy);
                double pz = r * (ca * uz + sa * wz);

                ProjectWorldToScreenFloat(px, py, pz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                if (k == steps / 2) { midX = sx; midY = sy; }   // 取角平分线处作为标注锚点
                if (k > 0) DrawAALine(prevX, prevY, sx, sy, c, 1.0f);
                prevX = sx; prevY = sy;
            }
        }

        /// <summary>
        /// 升交点处"赤道面 → 轨道面"的真实二面角 i 弧。
        /// 该弧必须位于**垂直于升交线的平面**内，其正交基为 (ẑ×n̂, ẑ)：
        ///   φ=0 时落在赤道面内 (沿 ẑ×n̂)；φ=i 时恰好落在轨道面内 (等于 m̂ = ĥ×n̂)。
        /// 旧实现误把 ĥ×n̂ 的水平分量当水平基，使弧被横向压缩 cos i 倍，
        /// 端点既不在轨道面上、视觉夹角也变成 atan(sin i / cos²i)，高倾角时严重失真。
        /// </summary>
        private void DrawDihedralInclinationArc(double nx, double ny, double baseDist, double iRad,
            double cosCp, double sinCp, double cosCy, double sinCy, float cx, float cy, Color32 c,
            out float midX, out float midY)
        {
            midX = cx; midY = cy;
            double baseX = baseDist * nx;
            double baseY = baseDist * ny;

            double ux = -ny, uy = nx;    // ẑ × n̂ (单位矢量，赤道面内且垂直于升交线)
            double arcR = 14.0;
            int steps = Mathf.Clamp((int)(Math.Abs(iRad) * 24.0), 16, 128);
            float prevX = -1f, prevY = -1f;

            for (int k = 0; k <= steps; k++)
            {
                double a = iRad * (k / (double)steps);
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double wx = baseX + arcR * ux * ca;
                double wy = baseY + arcR * uy * ca;
                double wz = arcR * sa;

                ProjectWorldToScreenFloat(wx, wy, wz, cosCp, sinCp, cosCy, sinCy, cx, cy, out float sx, out float sy, out _);
                if (k == steps / 2) { midX = sx; midY = sy; }
                if (k > 0) DrawAALine(prevX, prevY, sx, sy, c, 1.0f);
                prevX = sx; prevY = sy;
            }
        }

        private void DrawMiniPlanetSphere(float cx, float cy, float r)
        {
            cx *= _k; cy *= _k; r *= _k;   // 逻辑 → 纹素
            float feather = 0.65f;
            int minX = Mathf.Clamp((int)Mathf.Floor(cx - r - feather), 0, _texRes - 1);
            int maxX = Mathf.Clamp((int)Mathf.Ceil(cx + r + feather), 0, _texRes - 1);
            int minY = Mathf.Clamp((int)Mathf.Floor(cy - r - feather), 0, _texRes - 1);
            int maxY = Mathf.Clamp((int)Mathf.Ceil(cy + r + feather), 0, _texRes - 1);

            for (int y = minY; y <= maxY; y++)
            {
                float dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= r + feather)
                    {
                        float cov = dist <= r ? 1.0f : (1.0f - (dist - r) / feather);
                        float fx = dx / r;
                        float fy = dy / r;
                        float fz = Mathf.Sqrt(Mathf.Max(0f, 1f - fx * fx - fy * fy));
                        float ndotl = Mathf.Clamp01(-fx * 0.5f + fy * 0.6f + fz * 0.6f);
                        Color32 c = Color32.Lerp(_cPlanetShadow, _cPlanetSun, ndotl);
                        BlendPixelAlpha(x, y, c, cov);
                    }
                }
            }
            // 极轴小竖线 (Z 轴在本正交投影下恒为屏幕正上方)
            DrawAALineT(cx, cy - r - 2f * _k, cx, cy + r + 2f * _k, _cPlanetGrid, 0.9f * _k);
        }

        // ═════════════════════════════════════════════════════════════════
        // 航电微矢量字形引擎 (Micro-Vector Glyphs 5×7 锐利骨架)
        // 专为高密度小界面定制，单像素笔画清晰分明，绝不粘连发糊
        // ═════════════════════════════════════════════════════════════════

        private void DrawGlyphChar(float x, float y, char ch, Color32 c)
        {
            switch (ch)
            {
                case 'X':
                    DrawAALine(x, y - 7f, x + 5f, y, c, 1.0f);
                    DrawAALine(x, y, x + 5f, y - 7f, c, 1.0f);
                    break;
                case 'Y':
                    DrawAALine(x, y, x + 2.5f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 5f, y, x + 2.5f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 2.5f, y - 3.5f, x + 2.5f, y - 7f, c, 1.0f);
                    break;
                case 'Z':
                    DrawAALine(x, y, x + 5f, y, c, 1.0f);
                    DrawAALine(x + 5f, y, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y - 7f, x + 5f, y - 7f, c, 1.0f);
                    break;
                case 'h':
                    DrawAALine(x, y + 2f, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y - 2.5f, x + 4f, y - 2.5f, c, 1.0f);
                    DrawAALine(x + 4f, y - 2.5f, x + 4f, y - 7f, c, 1.0f);
                    break;
                case 'e':
                    DrawAALine(x, y - 3.5f, x + 4.5f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 4.5f, y - 3.5f, x + 4.5f, y - 1f, c, 1.0f);
                    DrawAALine(x + 4.5f, y - 1f, x, y - 1f, c, 1.0f);
                    DrawAALine(x, y - 1f, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y - 7f, x + 4.5f, y - 7f, c, 1.0f);
                    break;
                case 'r':
                    DrawAALine(x, y - 2f, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y - 3.5f, x + 3.5f, y - 2f, c, 1.0f);
                    break;
                case 'v':
                    DrawAALine(x, y - 2f, x + 2.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 2.5f, y - 7f, x + 5f, y - 2f, c, 1.0f);
                    break;
                case 'i':
                    DrawAALine(x + 1f, y - 2f, x + 1f, y - 7f, c, 1.0f);
                    DrawFilledCircle(x + 1f, y, 0.7f, c);
                    break;
                case 'Ω':
                    DrawAALine(x, y - 7f, x + 1.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 1.5f, y - 7f, x + 1.5f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 1.5f, y - 3.5f, x + 3.5f, y - 1f, c, 1.0f);
                    DrawAALine(x + 3.5f, y - 1f, x + 5.5f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 5.5f, y - 3.5f, x + 5.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 5.5f, y - 7f, x + 7f, y - 7f, c, 1.0f);
                    break;
                case 'ω':
                    DrawAALine(x, y - 3.5f, x + 1.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 1.5f, y - 7f, x + 3f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 3f, y - 3.5f, x + 4.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 4.5f, y - 7f, x + 6f, y - 3.5f, c, 1.0f);
                    break;
                case 'ν':
                    DrawAALine(x, y - 2f, x + 2.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 2.5f, y - 7f, x + 5.5f, y, c, 1.0f);
                    break;
                case 'A':
                    DrawAALine(x, y - 7f, x + 2.5f, y, c, 1.0f);
                    DrawAALine(x + 2.5f, y, x + 5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 1f, y - 4f, x + 4f, y - 4f, c, 1.0f);
                    break;
                case 'P':
                    DrawAALine(x, y, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y, x + 4f, y, c, 1.0f);
                    DrawAALine(x + 4f, y, x + 4f, y - 3.5f, c, 1.0f);
                    DrawAALine(x + 4f, y - 3.5f, x, y - 3.5f, c, 1.0f);
                    break;
                case 'E':
                    DrawAALine(x, y, x, y - 7f, c, 1.0f);
                    DrawAALine(x, y, x + 4f, y, c, 1.0f);
                    DrawAALine(x, y - 3.5f, x + 3f, y - 3.5f, c, 1.0f);
                    DrawAALine(x, y - 7f, x + 4f, y - 7f, c, 1.0f);
                    break;
                case 'N':
                    DrawAALine(x, y - 7f, x, y, c, 1.0f);
                    DrawAALine(x, y, x + 4.5f, y - 7f, c, 1.0f);
                    DrawAALine(x + 4.5f, y - 7f, x + 4.5f, y, c, 1.0f);
                    break;
                case 'S':
                    DrawAALine(x + 4f, y, x + 1f, y, c, 1.0f);
                    DrawAALine(x + 1f, y, x, y - 3f, c, 1.0f);
                    DrawAALine(x, y - 3f, x + 4f, y - 4f, c, 1.0f);
                    DrawAALine(x + 4f, y - 4f, x + 4f, y - 7f, c, 1.0f);
                    DrawAALine(x + 4f, y - 7f, x, y - 7f, c, 1.0f);
                    break;
                case 'C':
                    DrawAALine(x + 4f, y, x + 1f, y, c, 1.0f);
                    DrawAALine(x, y - 1.5f, x, y - 5.5f, c, 1.0f);
                    DrawAALine(x + 1f, y - 7f, x + 4f, y - 7f, c, 1.0f);
                    break;
                case 'Q':
                    DrawHollowCircle(x + 2.5f, y - 3.5f, 2.5f, c, 1.0f);
                    DrawAALine(x + 2.5f, y - 4f, x + 4.5f, y - 7f, c, 1.0f);
                    break;
            }
        }

        /// <summary>逐字绘制微矢量标签 (字距 6px 逻辑单位，随 DPI 缩放)</summary>
        private void DrawGlyphString(float x, float y, string str, Color32 c)
        {
            if (string.IsNullOrEmpty(str)) return;
            float curX = x;
            for (int i = 0; i < str.Length; i++)
            {
                DrawGlyphChar(curX, y, str[i], c);
                curX += 6f;
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

        private static string FormatDistanceMetric(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters)) return "---";
            if (Math.Abs(meters) >= 1000000000.0) return (meters * 1e-9).ToString("F2") + "Gm";
            if (Math.Abs(meters) >= 1000000.0) return (meters * 1e-6).ToString("F1") + "Mm";
            if (Math.Abs(meters) >= 1000.0) return (meters * 1e-3).ToString("F1") + "km";
            return meters.ToString("F0") + "m";
        }

        private static string FormatTimeCompact(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "--:--";
            if (seconds > 86400.0) return $"{seconds / 86400.0:F0}d";
            int sec = (int)seconds;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            if (sec >= 3600)
            {
                int h = sec / 3600;
                return $"{h:D2}:{m:D2}";
            }
            return $"{m:D2}:{s:D2}";
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

        // ═════════════════════════════════════════════════════════════════
        // SPEC-005: 资源释放与生命周期
        // ═════════════════════════════════════════════════════════════════
        protected override void OnDestroy()
        {
            if (_modeButton != null)
                _modeButton.onClick.RemoveListener(OnModeToggle);

            if (_globeTexture != null)
            {
                Destroy(_globeTexture);
                _globeTexture = null;
            }
            _texPixels = null;
            base.OnDestroy();
        }
    }
}
