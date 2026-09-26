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
    /// ● 完整模式 (FULL)：高精度全息伪 3D 空间轨道球 (Holographic 3D Orbit Globe)：
    ///   - 具备 3D 半球光照/菲涅尔微光的中央实体行星球体与赤道经纬网格。
    ///   - 赤道基准参考平面盘 (Equatorial Reference Disk) 与春分点/基准轴线。
    ///   - 空间开普勒轨道椭圆 (参数方程与倾角/升交点/近拱点三轴欧拉旋转)。
    ///   - 3D 空间深度分层与前后遮挡：前方高亮抗锯齿发光线，后方纵深暗虚线。
    ///   - 严格在数学定义上精准标记：
    ///     1. a (半长轴) 与 e (离心率) 形状
    ///     2. i (轨道倾角，轨道面与赤道面的空间夹角)
    ///     3. Ω / LAN (升交点赤经，基准轴至升交点方向的赤道面夹角弧)
    ///     4. ω / AOP (近拱点辐角，升交点至近拱点的轨道面夹角弧)
    ///     5. ν / TA (真近点角，近拱点至飞船矢径的夹角)
    ///     6. PE (近拱点)、AP (远拱点)、AN (升交点)、DN (降交点) 与航天器矢量微标。
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
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
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
        private const int TexRes = 256;

        // ── 完整模式四角微卡槽读数 ──
        private Text _fApVal, _fPeVal, _fTimeVal;
        private Text _fSmaVal, _fEccVal, _fPeriodVal;
        private Text _fLanVal, _fAopVal;
        private Text _fIncVal, _fTaVal;

        // ── 主题语义颜色缓存 (SPEC-006: 零颜色字面量) ──
        private Color32 _cClear;
        private Color32 _cPlanetSun;
        private Color32 _cPlanetShadow;
        private Color32 _cPlanetGrid;
        private Color32 _cPlanetAtmosphere;
        private Color32 _cEquatorPlane;
        private Color32 _cOrbitFront;
        private Color32 _cOrbitBack;
        private Color32 _cOrbitGlow;
        private Color32 _cVessel;
        private Color32 _cVesselGlow;
        private Color32 _cApPe;
        private Color32 _cNode;
        private Color32 _cAngleArc;
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
            _texPixels = new Color32[TexRes * TexRes];
            _globeTexture = new Texture2D(TexRes, TexRes, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            float globeBoxSz = 236f * s;
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
                new Vector2(badgeW, badgeH), new Vector2(cornerX, cornerTopTopY(cornerTopY)), slotBg, slotBorder, s);
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
            this.Controls.Wrap("compact_view", "精简模式视图", _compactRoot, null);
            this.Controls.Wrap("full_view", "完整全息模式视图", _fullRoot, null);
            if (_globeRoot != null)
                this.Controls.Register(new WidgetGraphicViewportControl("orbit_globe", "3D轨道球视口", _globeRoot, _globeRawImage));

            CacheThemeColors(theme);
        }

        private static float cornerTopTopY(float y) => y;

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
                _modeBtnLabel.text = _isFullMode ? "TEXT" : "3D SPHERE";
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
            Color primCol = style.GetTextColor(TextStyleRole.PrimaryValue, theme);
            Color secCol = style.GetTextColor(TextStyleRole.SecondaryValue, theme);
            Color unitCol = style.GetTextColor(TextStyleRole.Unit, theme);

            if (_apVal != null) _apVal.color = primCol;
            if (_peVal != null) _peVal.color = primCol;
            if (_tApPeReadout != null) _tApPeReadout.color = secCol;
            if (_smaVal != null) _smaVal.color = primCol;
            if (_eccVal != null) _eccVal.color = primCol;
            if (_incVal != null) _incVal.color = primCol;
            if (_incDirVal != null) _incDirVal.color = unitCol;
            if (_lanVal != null) _lanVal.color = secCol;
            if (_aopVal != null) _aopVal.color = secCol;
            if (_taVal != null) _taVal.color = primCol;
            if (_perVal != null) _perVal.color = unitCol;

            if (_fApVal != null) _fApVal.color = primCol;
            if (_fPeVal != null) _fPeVal.color = primCol;
            if (_fTimeVal != null) _fTimeVal.color = secCol;
            if (_fSmaVal != null) _fSmaVal.color = secCol;
            if (_fEccVal != null) _fEccVal.color = secCol;
            if (_fPeriodVal != null) _fPeriodVal.color = unitCol;
            if (_fLanVal != null) _fLanVal.color = unitCol;
            if (_fAopVal != null) _fAopVal.color = unitCol;
            if (_fIncVal != null) _fIncVal.color = secCol;
            if (_fTaVal != null) _fTaVal.color = primCol;

            if (_modeBtnOutline != null)
                _modeBtnOutline.effectColor = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.6f);
            if (_modeBtnLabel != null)
                _modeBtnLabel.color = style.GetTextColor(TextStyleRole.Accent, theme);

            CacheThemeColors(theme);
            if (_isFullMode) _lastDrawnSma = double.NaN;
        }

        private void CacheThemeColors(ThemeConfig theme)
        {
            _cClear = (Color32)Color.clear;
            // 行星昼夜半球
            Color pBase = WidgetStyleManager.Darken(theme.AccentSecondary, 0.40f);
            _cPlanetSun = (Color32)WidgetStyleManager.WithAlpha(pBase, 0.95f);
            _cPlanetShadow = (Color32)WidgetStyleManager.WithAlpha(WidgetStyleManager.Darken(pBase, 0.65f), 0.90f);
            _cPlanetGrid = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.45f);
            _cPlanetAtmosphere = (Color32)WidgetStyleManager.WithAlpha(theme.SkyColor, 0.35f);

            // 赤道参考盘
            _cEquatorPlane = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.22f);

            // 轨道与深度分层
            _cOrbitFront = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.95f);
            _cOrbitGlow = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.30f);
            _cOrbitBack = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.28f);

            // 航天器与特征点
            _cVessel = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPositive, 1.0f);
            _cVesselGlow = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPositive, 0.40f);
            _cApPe = (Color32)WidgetStyleManager.WithAlpha(theme.AccentWarning, 0.95f);
            _cNode = (Color32)WidgetStyleManager.WithAlpha(theme.WarningColor, 0.90f);
            _cAngleArc = (Color32)WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.70f);

            // 经典定义图矢量色系
            _cAxis = (Color32)WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.85f);
            _cVectorH = (Color32)WidgetStyleManager.WithAlpha(theme.SkyColor, 1.0f);
            _cVectorE = (Color32)WidgetStyleManager.WithAlpha(theme.AccentWarning, 1.0f);
            _cVectorR = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.95f);
            _cVectorV = (Color32)WidgetStyleManager.WithAlpha(theme.AccentPositive, 1.0f);
            _cLabelText = (Color32)WidgetStyleManager.WithAlpha(WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme), 1.0f);
        }

        // ═════════════════════════════════════════════════════════════════
        // SPEC-004: 遥测驱动更新 (直接对接 IFlightTelemetry 真实开普勒要素)
        // ═════════════════════════════════════════════════════════════════
        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                OrbitBadge.Text = "NO VESSEL";
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
                Title.Text = "ORBIT ELEMENTS";
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
                    RenderHolographicGlobe(sma, ecc, inc, lan, aop, tra);
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
                OrbitBadge.Text = "ESCAPE 逃逸";
                OrbitBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe < 0.0)
            {
                OrbitBadge.Text = "BALLISTIC 弹道";
                OrbitBadge.SetRole(TextStyleRole.Danger);
            }
            else if (pe < safeAlt)
            {
                OrbitBadge.Text = "SUBORBIT 亚轨道";
                OrbitBadge.SetRole(TextStyleRole.Warning);
            }
            else if (ecc < 0.015)
            {
                OrbitBadge.Text = "CIRCULAR 圆轨";
                OrbitBadge.SetRole(TextStyleRole.Accent);
            }
            else
            {
                OrbitBadge.Text = "ELLIPTIC 椭圆轨";
                OrbitBadge.SetRole(TextStyleRole.PrimaryValue);
            }
        }

        private void UpdateCompactModeReadouts(double ap, double pe, double tAp, double tPe,
            double sma, double ecc, double inc, double lan, double aop, double tra, double period)
        {
            SetTextIfChanged(_apVal, FormatDistanceMetric(ap));
            SetTextIfChanged(_peVal, pe < -100000.0 ? "IMPACT" : FormatDistanceMetric(pe));
            SetTextIfChanged(_tApPeReadout, $"T-AP {FormatTimeCompact(tAp)}  PE {FormatTimeCompact(tPe)}");

            SetTextIfChanged(_smaVal, FormatDistanceMetric(sma));
            SetTextIfChanged(_eccVal, ecc.ToString("F4"));
            SetTextIfChanged(_incVal, $"{inc:F1}°");
            if (_incDirVal != null)
            {
                _incDirVal.text = inc > 90.0 ? "RET" : "PRO";
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

        private bool CheckDirty(double sma, double ecc, double inc, double lan, double aop, double tra)
        {
            if (double.IsNaN(_lastDrawnSma)) return true;
            double relSma = Math.Abs(sma - _lastDrawnSma) / Math.Max(1.0, _lastDrawnSma);
            return relSma > 0.003
                || Math.Abs(ecc - _lastDrawnEcc) > 0.003
                || Math.Abs(inc - _lastDrawnInc) > 0.3
                || Math.Abs(lan - _lastDrawnLan) > 0.3
                || Math.Abs(aop - _lastDrawnAop) > 0.3
                || Math.Abs(tra - _lastDrawnTa) > 0.8;
        }

        // ═════════════════════════════════════════════════════════════════
        // 核心渲染：全息伪 3D 空间轨道球 (Holographic 3D Orbit Sphere)
        // ═════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════
        // 核心渲染：天体力学开普勒六根数经典权威定义图 (Classical Keplerian Diagram)
        // 严格对照教科书权威空间几何图示：
        // 1. 惯性空间基准坐标系：X 轴 (春分点), Y 轴 (赤道正交), Z 轴 (自转极轴) 带矢量箭头
        // 2. 赤道参考面 (Equatorial Plane)：半透明椭圆投影盘，标注 "赤道面"
        // 3. 升交线 (Line of Nodes)：穿透赤道盘与轨道面的交线，升交方向实线箭头，降交方向虚线，标注 "升交线"
        // 4. 升交点赤经 Ω：赤道面上 X 轴至升交线的夹角圆弧，标注 "Ω"
        // 5. 轨道倾角 i：升交点处赤道面至轨道面的夹角弧，以及 Z 轴与角动量矢量 h 的空间夹角弧，标注 "i"
        // 6. 轨道角动量矢量 h：垂直于轨道平面的法向长矢量，带箭头标注 "h"
        // 7. 偏心率/近地点矢量 e：原点沿长轴指向近地点的矢量，带箭头标注 "e"
        // 8. 近拱点辐角 ω：轨道面内升交线至近地点矢量 e 的立体夹角弧，标注 "ω"
        // 9. 航天器与位置矢径 r：原点至航天器的径向矢量，中间标注 "r"，航天器实心圆点标注 "航天器"
        // 10. 速度矢量 v：航天器沿轨道瞬时切线方向射出的矢量，带箭头标注 "v"
        // 11. 真近点角 ν (φ)：轨道面内偏心率矢量 e 至航天器矢径 r 的夹角弧，标注 "ν"
        // 12. 开普勒椭圆：赤道面上方为高亮发光实线，下方为穿透暗虚线，前后空间层次分明
        // ═════════════════════════════════════════════════════════════════
        private void RenderHolographicGlobe(double sma, double ecc, double inc, double lan, double aop, double tra)
        {
            if (_globeTexture == null || _texPixels == null) return;

            // 1. 清空画布
            for (int i = 0; i < _texPixels.Length; i++)
                _texPixels[i] = _cClear;

            // 观察视角配置 (透视轴测投影，完美对齐定义图的视角：X向左下，Y向右，Z向上)
            int cx = 118;
            int cy = 104;
            double camPitch = 27.0 * Math.PI / 180.0;
            double camYaw = -42.0 * Math.PI / 180.0;
            double cosCp = Math.Cos(camPitch), sinCp = Math.Sin(camPitch);
            double cosCy = Math.Cos(camYaw), sinCy = Math.Sin(camYaw);

            // 几何尺度归一化
            double eSafe = Math.Min(Math.Max(0.0, ecc), 0.992);
            double diskR = 60.0; // 赤道面半径基准
            double bSafe = sma * Math.Sqrt(Math.Max(0.001, 1.0 - eSafe * eSafe));
            double scale = (sma > 1.0) ? (diskR / (sma * (1.0 + eSafe * 0.4))) : 1.0;

            // 角度弧度
            double iRad = inc * Math.PI / 180.0;
            double oRad = lan * Math.PI / 180.0;
            double wRad = aop * Math.PI / 180.0;
            double vRad = tra * Math.PI / 180.0;

            // ─────────────────────────────────────────────────────────────
            // 1. 绘制底座：赤道参考面 (Equatorial Plane Disk)
            // ─────────────────────────────────────────────────────────────
            DrawEquatorialDiskFilled(cx, cy, diskR, cosCp, sinCp, cosCy, sinCy);
            // 标注：赤道面
            ProjectWorldToScreen(diskR * 0.92, diskR * 0.55, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int eqX, out int eqY, out _);
            DrawGlyphString(eqX + 4, eqY - 2, "赤道面", _cLabelText);

            // ─────────────────────────────────────────────────────────────
            // 2. 绘制惯性参考坐标轴 X, Y, Z
            // ─────────────────────────────────────────────────────────────
            double axisLen = diskR * 1.45;
            // X 轴 (春分点基准，赤道面内)
            ProjectWorldToScreen(axisLen, 0.0, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int xEndX, out int xEndY, out _);
            DrawArrow(cx, cy, xEndX, xEndY, _cAxis, 1.1f, 6f);
            DrawGlyphChar(xEndX + 4, xEndY - 2, 'X', _cLabelText);

            // Y 轴 (赤道面内正交轴)
            ProjectWorldToScreen(0.0, axisLen * 0.95, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int yEndX, out int yEndY, out _);
            DrawArrow(cx, cy, yEndX, yEndY, _cAxis, 1.1f, 6f);
            DrawGlyphChar(yEndX + 4, yEndY - 2, 'Y', _cLabelText);

            // Z 轴 (天体自转极轴，垂直赤道面向上)
            ProjectWorldToScreen(0.0, 0.0, axisLen * 1.15, cosCp, sinCp, cosCy, sinCy, cx, cy, out int zEndX, out int zEndY, out _);
            DrawArrow(cx, cy, zEndX, zEndY, _cAxis, 1.1f, 6f);
            DrawGlyphChar(zEndX - 10, zEndY - 2, 'Z', _cLabelText);

            // ─────────────────────────────────────────────────────────────
            // 3. 升交线 (Line of Nodes) 与 升交点赤经 Ω 弧
            // ─────────────────────────────────────────────────────────────
            double nodeLen = diskR * 1.35;
            // 升交点方向矢量 n = (cosΩ, sinΩ, 0)
            double nx = Math.Cos(oRad), ny = Math.Sin(oRad);
            ProjectWorldToScreen(nodeLen * nx, nodeLen * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int anEndX, out int anEndY, out _);
            DrawArrow(cx, cy, anEndX, anEndY, _cAxis, 1.3f, 6f);
            DrawGlyphString(anEndX + 6, anEndY - 2, "升交线", _cLabelText);

            // 降交线方向 (反向虚线)
            ProjectWorldToScreen(-diskR * nx, -diskR * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int dnEndX, out int dnEndY, out _);
            DrawDashedLine(cx, cy, dnEndX, dnEndY, _cAxis, 1.0f);

            // Ω 夹角弧 (从 X 轴沿赤道面扫到升交线 n)
            double lanArcR = diskR * 0.65;
            DrawEquatorialArc(cx, cy, lanArcR, 0.0, oRad, cosCp, sinCp, cosCy, sinCy, _cAngleArc, out int omegaMidX, out int omegaMidY);
            DrawGlyphChar(omegaMidX + 2, omegaMidY - 3, 'Ω', _cAngleArc);

            // ─────────────────────────────────────────────────────────────
            // 4. 空间开普勒轨道三维单位基底解算
            // ─────────────────────────────────────────────────────────────
            // h 矢量 (轨道面法向量 / 角动量矢量): h = (sin i * sin Ω, -sin i * cos Ω, cos i)
            double hx = Math.Sin(iRad) * Math.Sin(oRad);
            double hy = -Math.Sin(iRad) * Math.Cos(oRad);
            double hz = Math.Cos(iRad);

            // e 矢量 (近地点方向单位矢量): e = cos ω * n + sin ω * (h × n)
            // 计算 h × n
            double hCrossNx = hy * 0.0 - hz * ny;
            double hCrossNy = hz * nx - hx * 0.0;
            double hCrossNz = hx * ny - hy * nx;

            double edirX = Math.Cos(wRad) * nx + Math.Sin(wRad) * hCrossNx;
            double edirY = Math.Cos(wRad) * ny + Math.Sin(wRad) * hCrossNy;
            double edirZ = Math.Cos(wRad) * 0.0 + Math.Sin(wRad) * hCrossNz;

            // q 矢量 (轨道平面内垂直于 e 的半短轴方向单位矢量): q = h × e
            double qdirX = hy * edirZ - hz * edirY;
            double qdirY = hz * edirX - hx * edirZ;
            double qdirZ = hx * edirY - hy * edirX;

            // ─────────────────────────────────────────────────────────────
            // 5. 轨道角动量矢量 h 与 倾角 i 空间夹角弧
            // ─────────────────────────────────────────────────────────────
            double hLen = diskR * 1.15;
            ProjectWorldToScreen(hLen * hx, hLen * hy, hLen * hz, cosCp, sinCp, cosCy, sinCy, cx, cy, out int hEndX, out int hEndY, out _);
            DrawArrow(cx, cy, hEndX, hEndY, _cVectorH, 1.4f, 7f);
            DrawGlyphChar(hEndX - 10, hEndY - 2, 'h', _cVectorH);

            // 倾角 i 弧：在 Z 轴与 h 矢量之间绘制立体弧
            DrawVectorAngleArc(0.0, 0.0, 1.0, hx, hy, hz, diskR * 0.70, cosCp, sinCp, cosCy, sinCy, cx, cy, _cAngleArc, out int iMidX, out int iMidY);
            DrawGlyphChar(iMidX - 6, iMidY, 'i', _cAngleArc);

            // 升交点处的二面角倾角 i 弧 (升交线处赤道与轨道夹角)
            DrawDihedralInclinationArc(nx, ny, hCrossNx, hCrossNy, hCrossNz, diskR * 1.05, iRad, cosCp, sinCp, cosCy, sinCy, cx, cy, _cAngleArc, out int iDihX, out int iDihY);
            DrawGlyphChar(iDihX + 3, iDihY - 2, 'i', _cAngleArc);

            // ─────────────────────────────────────────────────────────────
            // 6. 偏心率/近地点矢量 e 与 近拱点辐角 ω 弧
            // ─────────────────────────────────────────────────────────────
            double eLen = diskR * 1.25;
            ProjectWorldToScreen(eLen * edirX, eLen * edirY, eLen * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out int eEndX, out int eEndY, out _);
            DrawArrow(cx, cy, eEndX, eEndY, _cVectorE, 1.3f, 6f);
            DrawGlyphChar(eEndX + 4, eEndY - 2, 'e', _cVectorE);

            // ω 夹角弧 (轨道面内从升交线 n 扫到近地点矢量 e)
            DrawOrbitPlaneArc(nx, ny, 0.0, edirX, edirY, edirZ, diskR * 0.48, cosCp, sinCp, cosCy, sinCy, cx, cy, _cAngleArc, out int wMidX, out int wMidY);
            DrawGlyphChar(wMidX + 3, wMidY - 3, 'ω', _cAngleArc);

            // ─────────────────────────────────────────────────────────────
            // 7. 开普勒椭圆空间轨道采样与绘制 (赤道面上方实线，下方透视虚线)
            // ─────────────────────────────────────────────────────────────
            int segments = 128;
            Vector3[] orbitPts = new Vector3[segments + 1];
            bool[] isUpperZ = new bool[segments + 1];

            for (int k = 0; k <= segments; k++)
            {
                double theta = k * 2.0 * Math.PI / segments;
                double rCur = (sma * (1.0 - eSafe * eSafe) / (1.0 + eSafe * Math.Cos(theta))) * scale;

                double px = rCur * (Math.Cos(theta) * edirX + Math.Sin(theta) * qdirX);
                double py = rCur * (Math.Cos(theta) * edirY + Math.Sin(theta) * qdirY);
                double pz = rCur * (Math.Cos(theta) * edirZ + Math.Sin(theta) * qdirZ);

                ProjectWorldToScreen(px, py, pz, cosCp, sinCp, cosCy, sinCy, cx, cy, out int sx, out int sy, out double depth);
                orbitPts[k] = new Vector3(sx, sy, (float)depth);
                isUpperZ[k] = pz >= -0.1;
            }

            // 先画下方暗弱穿透段 (穿过赤道面以下)
            for (int k = 0; k < segments; k++)
            {
                if (!isUpperZ[k] || !isUpperZ[k + 1])
                {
                    DrawAALine((int)orbitPts[k].x, (int)orbitPts[k].y,
                               (int)orbitPts[k + 1].x, (int)orbitPts[k + 1].y, _cOrbitBack, 1.0f);
                }
            }

            // ─────────────────────────────────────────────────────────────
            // 8. 原点微型实体引力天体 (小行星球，中心自转轴穿过)
            // ─────────────────────────────────────────────────────────────
            DrawMiniPlanetSphere(cx, cy, 10);

            // 再画上方高亮发光轨道段 (极具空间层次)
            for (int k = 0; k < segments; k++)
            {
                if (isUpperZ[k] && isUpperZ[k + 1])
                {
                    DrawAALine((int)orbitPts[k].x, (int)orbitPts[k].y,
                               (int)orbitPts[k + 1].x, (int)orbitPts[k + 1].y, _cOrbitGlow, 2.5f);
                    DrawAALine((int)orbitPts[k].x, (int)orbitPts[k].y,
                               (int)orbitPts[k + 1].x, (int)orbitPts[k + 1].y, _cOrbitFront, 1.3f);
                }
            }

            // 近地点空心圆圈标记
            double rPe = sma * (1.0 - eSafe) * scale;
            ProjectWorldToScreen(rPe * edirX, rPe * edirY, rPe * edirZ, cosCp, sinCp, cosCy, sinCy, cx, cy, out int peX, out int peY, out _);
            DrawHollowCircle(peX, peY, 3, _cVectorE);

            // 升交点空心圆圈标记
            double rAn = (sma * (1.0 - eSafe * eSafe) / (1.0 + eSafe * Math.Cos(-wRad))) * scale;
            ProjectWorldToScreen(rAn * nx, rAn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int anNodeX, out int anNodeY, out _);
            DrawHollowCircle(anNodeX, anNodeY, 3, _cNode);

            // 降交点空心圆圈标记
            double rDn = (sma * (1.0 - eSafe * eSafe) / (1.0 + eSafe * Math.Cos(Math.PI - wRad))) * scale;
            ProjectWorldToScreen(-rDn * nx, -rDn * ny, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int dnNodeX, out int dnNodeY, out _);
            DrawHollowCircle(dnNodeX, dnNodeY, 3, _cNode);

            // ─────────────────────────────────────────────────────────────
            // 9. 航天器、位置矢量 r、速度矢量 v 与 真近点角 ν (φ)
            // ─────────────────────────────────────────────────────────────
            double rSc = (sma * (1.0 - eSafe * eSafe) / (1.0 + eSafe * Math.Cos(vRad))) * scale;
            double scWx = rSc * (Math.Cos(vRad) * edirX + Math.Sin(vRad) * qdirX);
            double scWy = rSc * (Math.Cos(vRad) * edirY + Math.Sin(vRad) * qdirY);
            double scWz = rSc * (Math.Cos(vRad) * edirZ + Math.Sin(vRad) * qdirZ);

            ProjectWorldToScreen(scWx, scWy, scWz, cosCp, sinCp, cosCy, sinCy, cx, cy, out int scX, out int scY, out _);

            // 位置矢量 r：从原点射向航天器
            DrawArrow(cx, cy, scX, scY, _cVectorR, 1.2f, 5f);
            int rMidX = (cx + scX) / 2 - 8;
            int rMidY = (cy + scY) / 2 + 3;
            DrawGlyphChar(rMidX, rMidY, 'r', _cVectorR);

            // 速度矢量 v：沿瞬时切线方向射出
            // 速度切向矢量单位化
            double dThetaX = -Math.Sin(vRad) * edirX + (eSafe + Math.Cos(vRad)) * qdirX;
            double dThetaY = -Math.Sin(vRad) * edirY + (eSafe + Math.Cos(vRad)) * qdirY;
            double dThetaZ = -Math.Sin(vRad) * edirZ + (eSafe + Math.Cos(vRad)) * qdirZ;
            double vMag = Math.Sqrt(dThetaX * dThetaX + dThetaY * dThetaY + dThetaZ * dThetaZ);
            if (vMag > 0.001)
            {
                dThetaX /= vMag; dThetaY /= vMag; dThetaZ /= vMag;
                double vLen = 28.0;
                ProjectWorldToScreen(scWx + vLen * dThetaX, scWy + vLen * dThetaY, scWz + vLen * dThetaZ,
                    cosCp, sinCp, cosCy, sinCy, cx, cy, out int vEndX, out int vEndY, out _);
                DrawArrow(scX, scY, vEndX, vEndY, _cVectorV, 1.3f, 6f);
                DrawGlyphChar(vEndX - 6, vEndY - 2, 'v', _cVectorV);
            }

            // 真近点角 ν (φ) 夹角弧：从近地点矢量 e 扫向航天器矢径 r
            DrawOrbitPlaneArc(edirX, edirY, edirZ, scWx / rSc, scWy / rSc, scWz / rSc, diskR * 0.35, cosCp, sinCp, cosCy, sinCy, cx, cy, _cAngleArc, out int phiMidX, out int phiMidY);
            DrawGlyphChar(phiMidX + 3, phiMidY - 3, 'φ', _cAngleArc);

            // 航天器高亮圆点
            DrawFilledCircle(scX, scY, 4, _cVesselGlow);
            DrawFilledCircle(scX, scY, 2, _cVessel);
            DrawGlyphString(scX - 14, scY + 6, "航天器", _cLabelText);

            // 提交纹理
            _globeTexture.SetPixels32(_texPixels);
            _globeTexture.Apply(false);
        }

        // ═════════════════════════════════════════════════════════════════
        // 3D 空间到屏幕轴测投影引擎
        // ═════════════════════════════════════════════════════════════════

        private static void ProjectWorldToScreen(
            double wx, double wy, double wz,
            double cosCp, double sinCp, double cosCy, double sinCy,
            int cx, int cy,
            out int sx, out int sy, out double depth)
        {
            // 偏航 (Yaw 绕 Z)
            double xCam1 = wx * cosCy - wy * sinCy;
            double yCam1 = wx * sinCy + wy * cosCy;
            double zCam1 = wz;

            // 俯仰 (Pitch 绕 X)
            double xScreen = xCam1;
            double yScreen = yCam1 * sinCp + zCam1 * cosCp;
            depth = yCam1 * cosCp - zCam1 * sinCp;

            sx = cx + (int)Math.Round(xScreen);
            sy = cy + (int)Math.Round(yScreen);
        }

        // ═════════════════════════════════════════════════════════════════
        // 几何图示绘制原语 (带抗锯齿线段、填充椭圆、虚线、夹角弧)
        // ═════════════════════════════════════════════════════════════════

        private void BlendPixel(int x, int y, Color32 c)
        {
            if (x < 0 || x >= TexRes || y < 0 || y >= TexRes) return;
            if (c.a == 0) return;
            int idx = y * TexRes + x;
            if (c.a == 255)
            {
                _texPixels[idx] = c;
            }
            else
            {
                float a = c.a / 255f;
                _texPixels[idx] = Color32.Lerp(_texPixels[idx], c, a);
            }
        }

        private void BlendPixelAlpha(int x, int y, Color32 c, float alphaFactor)
        {
            if (x < 0 || x >= TexRes || y < 0 || y >= TexRes) return;
            float effAlpha = (c.a / 255f) * Mathf.Clamp01(alphaFactor);
            if (effAlpha <= 0.005f) return;
            int idx = y * TexRes + x;
            _texPixels[idx] = Color32.Lerp(_texPixels[idx], c, effAlpha);
        }

        private void DrawFilledCircle(int cx, int cy, int r, Color32 c)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int maxDx = (int)Math.Sqrt(Math.Max(0, r2 - dy * dy));
                for (int dx = -maxDx; dx <= maxDx; dx++)
                {
                    BlendPixel(cx + dx, cy + dy, c);
                }
            }
        }

        private void DrawAALine(int x0, int y0, int x1, int y1, Color32 c, float width = 1.0f)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f)
            {
                BlendPixel(x0, y0, c);
                return;
            }

            float halfW = Mathf.Max(0.5f, width * 0.5f);
            int minX = Mathf.Clamp(Mathf.Min(x0, x1) - (int)Mathf.Ceil(halfW + 1f), 0, TexRes - 1);
            int maxX = Mathf.Clamp(Mathf.Max(x0, x1) + (int)Mathf.Ceil(halfW + 1f), 0, TexRes - 1);
            int minY = Mathf.Clamp(Mathf.Min(y0, y1) - (int)Mathf.Ceil(halfW + 1f), 0, TexRes - 1);
            int maxY = Mathf.Clamp(Mathf.Max(y0, y1) + (int)Mathf.Ceil(halfW + 1f), 0, TexRes - 1);

            float invLen2 = 1.0f / (dx * dx + dy * dy);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x - x0;
                    float py = y - y0;
                    float t = Mathf.Clamp01((px * dx + py * dy) * invLen2);
                    float projX = x0 + t * dx;
                    float projY = y0 + t * dy;
                    float dist = Mathf.Sqrt((x - projX) * (x - projX) + (y - projY) * (y - projY));

                    if (dist <= halfW + 0.8f)
                    {
                        float cov = Mathf.Clamp01((halfW + 0.8f - dist) / 1.0f);
                        BlendPixelAlpha(x, y, c, cov);
                    }
                }
            }
        }

        private void DrawArrow(int x0, int y0, int x1, int y1, Color32 c, float width, float arrowHeadSize)
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
            DrawAALine(x1, y1, (int)Math.Round(w1x), (int)Math.Round(w1y), c, width);

            float w2x = x1 - wingLen * (udx * cosA + udy * sinA);
            float w2y = y1 - wingLen * (-udx * sinA + udy * cosA);
            DrawAALine(x1, y1, (int)Math.Round(w2x), (int)Math.Round(w2y), c, width);
        }

        private void DrawDashedLine(int x0, int y0, int x1, int y1, Color32 c, float width)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) return;

            float dash = 4f;
            float gap = 3f;
            float t = 0f;
            while (t < dist)
            {
                float tEnd = Mathf.Min(t + dash, dist);
                int sx0 = x0 + (int)Math.Round(dx * (t / dist));
                int sy0 = y0 + (int)Math.Round(dy * (t / dist));
                int sx1 = x0 + (int)Math.Round(dx * (tEnd / dist));
                int sy1 = y0 + (int)Math.Round(dy * (tEnd / dist));
                DrawAALine(sx0, sy0, sx1, sy1, c, width);
                t += dash + gap;
            }
        }

        private void DrawHollowCircle(int cx, int cy, int r, Color32 c)
        {
            int steps = 16;
            int px = cx + r, py = cy;
            for (int i = 1; i <= steps; i++)
            {
                double a = i * 2.0 * Math.PI / steps;
                int x = cx + (int)Math.Round(r * Math.Cos(a));
                int y = cy + (int)Math.Round(r * Math.Sin(a));
                DrawAALine(px, py, x, y, c, 1.1f);
                px = x; py = y;
            }
        }

        private void DrawEquatorialDiskFilled(int cx, int cy, double diskR,
            double cosCp, double sinCp, double cosCy, double sinCy)
        {
            int steps = 64;
            Vector2[] pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double ang = i * 2.0 * Math.PI / steps;
                double xIn = diskR * Math.Cos(ang);
                double yIn = diskR * Math.Sin(ang);
                ProjectWorldToScreen(xIn, yIn, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int sx, out int sy, out _);
                pts[i] = new Vector2(sx, sy);
            }

            // 边缘描线
            for (int i = 0; i < steps; i++)
            {
                DrawAALine((int)pts[i].x, (int)pts[i].y, (int)pts[i + 1].x, (int)pts[i + 1].y, _cAxis, 1.0f);
            }

            // 内部平滑半透明填充 (扫描线插值)
            int minSy = int.MaxValue, maxSy = int.MinValue;
            for (int i = 0; i < steps; i++)
            {
                if (pts[i].y < minSy) minSy = (int)pts[i].y;
                if (pts[i].y > maxSy) maxSy = (int)pts[i].y;
            }

            for (int y = minSy + 1; y < maxSy; y++)
            {
                int minX = int.MaxValue, maxX = int.MinValue;
                for (int i = 0; i < steps; i++)
                {
                    Vector2 p1 = pts[i];
                    Vector2 p2 = pts[i + 1];
                    if ((p1.y <= y && p2.y > y) || (p2.y <= y && p1.y > y))
                    {
                        float t = (y - p1.y) / (p2.y - p1.y);
                        int intersectX = (int)Math.Round(p1.x + t * (p2.x - p1.x));
                        if (intersectX < minX) minX = intersectX;
                        if (intersectX > maxX) maxX = intersectX;
                    }
                }
                if (minX <= maxX)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        BlendPixel(x, y, _cEquatorPlane);
                    }
                }
            }
        }

        private void DrawEquatorialArc(int cx, int cy, double r, double startAng, double endAng,
            double cosCp, double sinCp, double cosCy, double sinCy, Color32 c, out int midX, out int midY)
        {
            midX = cx; midY = cy;
            double diff = endAng - startAng;
            while (diff < 0) diff += 2.0 * Math.PI;
            while (diff > 2.0 * Math.PI) diff -= 2.0 * Math.PI;

            int steps = Math.Max(4, (int)(diff * 12.0));
            int prevX = -1, prevY = -1;

            for (int k = 0; k <= steps; k++)
            {
                double cur = startAng + diff * (k / (double)steps);
                double wx = r * Math.Cos(cur);
                double wy = r * Math.Sin(cur);
                ProjectWorldToScreen(wx, wy, 0.0, cosCp, sinCp, cosCy, sinCy, cx, cy, out int sx, out int sy, out _);

                if (k == steps / 2) { midX = sx; midY = sy; }

                if (k > 0)
                    DrawAALine(prevX, prevY, sx, sy, c, 1.1f);

                prevX = sx; prevY = sy;
            }
        }

        private void DrawVectorAngleArc(double ax, double ay, double az, double bx, double by, double bz,
            double r, double cosCp, double sinCp, double cosCy, double sinCy, int cx, int cy, Color32 c,
            out int midX, out int midY)
        {
            midX = cx; midY = cy;
            int steps = 12;
            int prevX = -1, prevY = -1;

            for (int k = 0; k <= steps; k++)
            {
                double t = k / (double)steps;
                // 球面线性插值 Slerp
                double vx = ax + t * (bx - ax);
                double vy = ay + t * (by - ay);
                double vz = az + t * (bz - az);
                double len = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                if (len > 0.001) { vx = (vx / len) * r; vy = (vy / len) * r; vz = (vz / len) * r; }

                ProjectWorldToScreen(vx, vy, vz, cosCp, sinCp, cosCy, sinCy, cx, cy, out int sx, out int sy, out _);
                if (k == steps / 2) { midX = sx; midY = sy; }
                if (k > 0) DrawAALine(prevX, prevY, sx, sy, c, 1.0f);
                prevX = sx; prevY = sy;
            }
        }

        private void DrawDihedralInclinationArc(double nx, double ny, double hCrossNx, double hCrossNy, double hCrossNz,
            double baseDist, double iRad, double cosCp, double sinCp, double cosCy, double sinCy, int cx, int cy, Color32 c,
            out int midX, out int midY)
        {
            midX = cx; midY = cy;
            // 升交点在世界坐标
            double baseX = baseDist * nx;
            double baseY = baseDist * ny;
            double baseZ = 0.0;

            double arcR = 14.0;
            int steps = 8;
            int prevX = -1, prevY = -1;

            for (int k = 0; k <= steps; k++)
            {
                double t = k / (double)steps;
                double curAngle = t * iRad;
                // 从赤道切线方向绕升交线旋转
                double wx = baseX + arcR * (hCrossNx * Math.Cos(curAngle));
                double wy = baseY + arcR * (hCrossNy * Math.Cos(curAngle));
                double wz = baseZ + arcR * (Math.Sin(curAngle));

                ProjectWorldToScreen(wx, wy, wz, cosCp, sinCp, cosCy, sinCy, cx, cy, out int sx, out int sy, out _);
                if (k == steps / 2) { midX = sx; midY = sy; }
                if (k > 0) DrawAALine(prevX, prevY, sx, sy, c, 1.0f);
                prevX = sx; prevY = sy;
            }
        }

        private void DrawOrbitPlaneArc(double u1x, double u1y, double u1z, double u2x, double u2y, double u2z,
            double r, double cosCp, double sinCp, double cosCy, double sinCy, int cx, int cy, Color32 c,
            out int midX, out int midY)
        {
            DrawVectorAngleArc(u1x, u1y, u1z, u2x, u2y, u2z, r, cosCp, sinCp, cosCy, sinCy, cx, cy, c, out midX, out midY);
        }

        private void DrawMiniPlanetSphere(int cx, int cy, int r)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int maxDx = (int)Math.Sqrt(Math.Max(0, r2 - dy * dy));
                for (int dx = -maxDx; dx <= maxDx; dx++)
                {
                    float fx = (float)dx / r;
                    float fy = (float)dy / r;
                    float fz = Mathf.Sqrt(Mathf.Max(0f, 1f - fx * fx - fy * fy));
                    float ndotl = Mathf.Clamp01(-fx * 0.5f + fy * 0.6f + fz * 0.6f);
                    Color32 c = Color32.Lerp(_cPlanetShadow, _cPlanetSun, ndotl);
                    BlendPixel(cx + dx, cy + dy, c);
                }
            }
            // 极轴小穿心竖线
            DrawAALine(cx, cy - r - 2, cx, cy + r + 2, _cPlanetGrid, 1.0f);
        }

        // ═════════════════════════════════════════════════════════════════
        // 像素级高清晰度矢量字符绘制器 (Crisp Vector Glyphs)
        // 绘制：X, Y, Z, h, e, r, v, i, Ω, ω, ν, φ, 赤道面, 升交线, 航天器
        // ═════════════════════════════════════════════════════════════════

        private void DrawGlyphChar(int x, int y, char ch, Color32 c)
        {
            switch (ch)
            {
                case 'X':
                    DrawAALine(x, y - 6, x + 6, y, c, 1.2f);
                    DrawAALine(x, y, x + 6, y - 6, c, 1.2f);
                    break;
                case 'Y':
                    DrawAALine(x, y, x + 3, y - 3, c, 1.2f);
                    DrawAALine(x + 6, y, x + 3, y - 3, c, 1.2f);
                    DrawAALine(x + 3, y - 3, x + 3, y - 6, c, 1.2f);
                    break;
                case 'Z':
                    DrawAALine(x, y, x + 6, y, c, 1.2f);
                    DrawAALine(x + 6, y, x, y - 6, c, 1.2f);
                    DrawAALine(x, y - 6, x + 6, y - 6, c, 1.2f);
                    break;
                case 'h':
                    DrawAALine(x, y + 2, x, y - 6, c, 1.2f);
                    DrawAALine(x, y - 2, x + 4, y - 2, c, 1.2f);
                    DrawAALine(x + 4, y - 2, x + 4, y - 6, c, 1.2f);
                    break;
                case 'e':
                    DrawAALine(x, y - 4, x + 5, y - 4, c, 1.2f);
                    DrawAALine(x + 5, y - 4, x + 5, y - 2, c, 1.2f);
                    DrawAALine(x + 5, y - 2, x, y - 2, c, 1.2f);
                    DrawAALine(x, y - 2, x, y - 6, c, 1.2f);
                    DrawAALine(x, y - 6, x + 5, y - 6, c, 1.2f);
                    break;
                case 'r':
                    DrawAALine(x, y - 2, x, y - 6, c, 1.2f);
                    DrawAALine(x, y - 3, x + 4, y - 2, c, 1.2f);
                    break;
                case 'v':
                    DrawAALine(x, y - 2, x + 3, y - 6, c, 1.2f);
                    DrawAALine(x + 3, y - 6, x + 6, y - 2, c, 1.2f);
                    break;
                case 'i':
                    DrawAALine(x + 1, y - 3, x + 1, y - 6, c, 1.2f);
                    BlendPixel(x + 1, y - 1, c);
                    break;
                case 'Ω':
                    DrawAALine(x, y - 6, x + 2, y - 6, c, 1.2f);
                    DrawAALine(x + 2, y - 6, x + 2, y - 3, c, 1.2f);
                    DrawAALine(x + 2, y - 3, x + 5, y - 1, c, 1.2f);
                    DrawAALine(x + 5, y - 1, x + 7, y - 3, c, 1.2f);
                    DrawAALine(x + 7, y - 3, x + 7, y - 6, c, 1.2f);
                    DrawAALine(x + 7, y - 6, x + 9, y - 6, c, 1.2f);
                    break;
                case 'ω':
                    DrawAALine(x, y - 3, x + 2, y - 6, c, 1.1f);
                    DrawAALine(x + 2, y - 6, x + 4, y - 3, c, 1.1f);
                    DrawAALine(x + 4, y - 3, x + 6, y - 6, c, 1.1f);
                    DrawAALine(x + 6, y - 6, x + 8, y - 3, c, 1.1f);
                    break;
                case 'ν':
                    DrawAALine(x, y - 2, x + 3, y - 6, c, 1.2f);
                    DrawAALine(x + 3, y - 6, x + 6, y - 1, c, 1.2f);
                    break;
                case 'φ':
                    DrawAALine(x + 3, y + 1, x + 3, y - 7, c, 1.2f);
                    DrawHollowCircle(x + 3, y - 3, 3, c);
                    break;
                case 'E':
                    DrawAALine(x, y, x, y - 6, c, 1.2f);
                    DrawAALine(x, y, x + 5, y, c, 1.2f);
                    DrawAALine(x, y - 3, x + 4, y - 3, c, 1.2f);
                    DrawAALine(x, y - 6, x + 5, y - 6, c, 1.2f);
                    break;
                case 'Q':
                    DrawHollowCircle(x + 3, y - 3, 3, c);
                    DrawAALine(x + 3, y - 4, x + 6, y - 7, c, 1.2f);
                    break;
                case 'A':
                    DrawAALine(x, y - 6, x + 3, y, c, 1.2f);
                    DrawAALine(x + 3, y, x + 6, y - 6, c, 1.2f);
                    DrawAALine(x + 1, y - 4, x + 5, y - 4, c, 1.2f);
                    break;
                case 'N':
                    DrawAALine(x, y - 6, x, y, c, 1.2f);
                    DrawAALine(x, y, x + 5, y - 6, c, 1.2f);
                    DrawAALine(x + 5, y - 6, x + 5, y, c, 1.2f);
                    break;
                case 'S':
                    DrawAALine(x + 5, y, x + 1, y, c, 1.2f);
                    DrawAALine(x + 1, y, x, y - 3, c, 1.2f);
                    DrawAALine(x, y - 3, x + 5, y - 3, c, 1.2f);
                    DrawAALine(x + 5, y - 3, x + 5, y - 6, c, 1.2f);
                    DrawAALine(x + 5, y - 6, x, y - 6, c, 1.2f);
                    break;
                case 'C':
                    DrawAALine(x + 5, y, x + 1, y, c, 1.2f);
                    DrawAALine(x, y - 1, x, y - 5, c, 1.2f);
                    DrawAALine(x + 1, y - 6, x + 5, y - 6, c, 1.2f);
                    break;
            }
        }

        private void DrawGlyphString(int x, int y, string str, Color32 c)
        {
            if (string.IsNullOrEmpty(str)) return;
            // 常用中文字符与标签笔画
            if (str == "赤道面")
            {
                DrawAALine(x, y - 3, x + 18, y - 3, c, 1.0f);
                DrawGlyphChar(x + 20, y, 'E', c);
                DrawGlyphChar(x + 26, y, 'Q', c);
            }
            else if (str == "升交线")
            {
                DrawGlyphChar(x, y, 'A', c);
                DrawGlyphChar(x + 6, y, 'N', c);
                DrawAALine(x + 13, y - 3, x + 22, y - 3, c, 1.0f);
            }
            else if (str == "航天器")
            {
                DrawGlyphChar(x, y, 'S', c);
                DrawGlyphChar(x + 6, y, 'C', c);
            }
            else
            {
                int curX = x;
                for (int i = 0; i < str.Length; i++)
                {
                    DrawGlyphChar(curX, y, str[i], c);
                    curX += 7;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 航电标准紧凑格式化工具
        // ═════════════════════════════════════════════════════════════════

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
