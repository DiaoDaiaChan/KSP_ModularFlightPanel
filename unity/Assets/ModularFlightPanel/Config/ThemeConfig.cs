using System;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Config
{
    public enum NavballStyleType
    {
        Cyber_Neon,
        Modern_Aero,
        Classic_Aero,
        Apollo_1969
    }

    public enum NavballRenderMode
    {
        StockTexture = 0,       // 0: 原版贴图 (使用官方/TextureReplacer原版素材 + 现代解析光线投射着色器)
        ProceduralVector = 1,   // 1: 程序化导航球 (纯数学矢量直出，超清视网膜级最高画质，零片元失真)
        ProceduralBake = 2,     // 2: (已废弃/回退至 ProceduralVector)
        StockDirect = 3,        // 3: 原版导航球 (直接使用原版 3D 导航球，隐藏周围杂乱 UI 并接入编辑模式拖拽缩放)

        // 兼容别名
        Texture = 0,
        Procedural = 1
    }

    public enum UiShaderStyle
    {
        Modern_Glass,     // 现代玻璃暗晶 (GlassCockpitUI)
        Dot_Matrix,       // 物理点阵 / LED 荧光屏 (DotMatrixUI)
        Phosphor_HUD,     // 矢量磷光 CRT & 衍射全息 HUD (PhosphorHoloUI)
        Digital_Segment,  // 7段数码液晶管 (DigitalSegmentUI)
        Cyber_Neon        // 赛博霓虹 (NeonGlowUI)
    }

    public enum AvionicsFontStyle
    {
        ModernSmooth = 0,    // 现代平滑高保真矢量字体 (Segoe UI / 微软雅黑 / Arial)
        RetroPixel = 1       // 硬件等宽微点阵像素字体 (Consolas / Lucida Console / 等宽点阵)
    }

    [Serializable]
    public class ColorHex
    {
        public string r;
        public string g;
        public string b;
        public string a;

        public ColorHex() { }
        public ColorHex(Color col)
        {
            r = col.r.ToString("F3");
            g = col.g.ToString("F3");
            b = col.b.ToString("F3");
            a = col.a.ToString("F3");
        }

        public Color ToColor()
        {
            float red = 1f, green = 1f, blue = 1f, alpha = 1f;
            float.TryParse(r, out red);
            float.TryParse(g, out green);
            float.TryParse(b, out blue);
            if (!float.TryParse(a, out alpha)) alpha = 1f;
            return new Color(red, green, blue, alpha);
        }

        public static ColorHex FromColor(Color c) => new ColorHex(c);
        public static implicit operator Color(ColorHex ch) => ch?.ToColor() ?? Color.white;
    }

    /// <summary>
    /// UI 样式策略参数 (UiStylePolicy)
    /// WidgetStyleManager 的全部叠色 / 淡化 / 亮度系数集中于此，彻底杜绝散落在渲染代码里的魔法数字。
    /// 每一项都可按主题（JSON）独立调节；字段缺失时 JsonUtility 保留此处的默认值，旧主题无需迁移。
    /// </summary>
    [Serializable]
    public class UiStylePolicy
    {
        // ---- 卡片底色叠加强度 (0~1，越大越偏向语义色) ----
        public float CardEmphasizedTint = 0.08f;
        public float CardWarningTint = 0.15f;
        public float CardDangerTint = 0.22f;
        public float CardInteractiveLighten = 0.06f;   // 交互控件底色向白提亮量
        public float CardPressedDim = 0.35f;           // 卡片按下态：语义色亮度乘子
        public float SubtleSlotAlphaScale = 0.50f;     // 次级插槽底色透明度乘子

        // ---- 卡片底色透明度增量 ----
        public float CardEmphasizedAlphaBoost = 0.05f;
        public float CardWarningAlphaBoost = 0.10f;
        public float CardDangerAlphaBoost = 0.15f;

        // ---- 卡片边框透明度 (按语义角色) ----
        public float BorderEmphasizedAlpha = 0.70f;
        public float BorderWarningAlpha = 0.85f;
        public float BorderDangerAlpha = 0.90f;
        public float BorderInteractiveAlpha = 0.50f;
        public float BorderPressedAlpha = 0.95f;
        public float BorderTransparentAlpha = 0.20f;
        public float BorderSubtleAlpha = 0.15f;

        // ---- 按钮 ----
        public float ButtonPressedDim = 0.45f;          // 按下态：语义色亮度乘子
        public float ButtonActiveDim = 0.25f;           // 激活锁态：语义色亮度乘子
        public float ButtonPlainPressedLighten = 0.12f; // 普通按钮按下：向白提亮量
        public float ButtonWarningTint = 0.30f;         // 警告按钮底色：向警告色混合量
        public float ButtonDangerTint = 0.30f;          // 危险按钮底色：向危险色混合量
        public float ButtonBgAlpha = 0.92f;
        public float ButtonPressedAlpha = 0.95f;

        // ---- 文本 ----
        public float TextUnitAlphaScale = 0.88f;       // 保证单位文本清晰明快
        public float TextMutedAlphaScale = 0.70f;      // 原 0.45f 升至 0.70f，彻底消除次级遥测低对比度发虚导致的阅读疲劳

        // ---- 表面材质系数 (SurfaceStyleRole) ----
        // 底色一律由 ThemeConfig.FrameBgColor 派生：向 AccentSecondary 混色 + 指定透明度。
        // 各组件不得再私藏 "深蓝面板 / 暗绿徽标" 之类的独立调色板。
        public float SurfacePanelTint = 0.030f;          // 标准面板玻璃底：向副强调色混合量
        public float SurfacePanelAlpha = 0.90f;
        public float SurfacePanelDeepDarken = 0.25f;     // 深色内嵌底：向黑压暗量
        public float SurfacePanelDeepAlpha = 0.85f;
        public float SurfaceSlotTint = 0.055f;           // 行/单元格/条目底
        public float SurfaceSlotAlpha = 0.45f;
        public float SurfaceSlotActiveTint = 0.140f;     // 激活条目底
        public float SurfaceSlotActiveAlpha = 0.55f;
        public float SurfaceControlTint = 0.120f;        // 按钮/控件底
        public float SurfaceControlAlpha = 0.95f;
        public float SurfaceInsetTint = 0.210f;          // 深色插槽衬底（刻度槽、沉槽）
        public float SurfaceInsetAlpha = 0.85f;
        public float SurfaceTileTint = 0.090f;           // 工具栏磁贴底
        public float SurfaceTileAlpha = 0.88f;
        public float SurfaceLedOffTint = 0.310f;         // 熄灭 LED：向 TextAccentColor 靠拢的中性灰
        public float SurfaceLedOffAlpha = 0.45f;
        public float SurfaceLedStandbyTint = 0.480f;     // 待机/未选中 LED
        public float SurfaceLedStandbyAlpha = 0.60f;

        // ---- 状态表面系数 (StatusSurfaceRole) ----
        public float StatusSurfaceSuccessDim = 0.25f;    // 状态徽标底：语义色亮度乘子
        public float StatusSurfaceCautionDim = 0.25f;
        public float StatusSurfaceDangerDim = 0.25f;
        public float StatusSurfaceAlpha = 0.95f;
        public float StatusSurfaceInactiveAlpha = 0.90f; // 未激活状态底透明度
        public float StatusPanelTint = 0.180f;           // 状态面板：在标准面板底上叠语义色的混合量
        public float StatusPanelAlpha = 0.95f;

        // ---- 线条/描边视觉权重 (LineWeight) ----
        public float LineAlphaGhost = 0.25f;   // 幽灵级：水印底纹、弱指示 (原 0.15f)
        public float LineAlphaFaint = 0.30f;   // 极弱：栅格线、弱分隔 (原 0.18f)
        public float LineAlphaSubtle = 0.36f;  // 次级：次级描边、内圈 (原 0.22f)
        public float LineAlphaLight = 0.42f;   // 轻：装饰环、外圈过渡 (原 0.28f)
        public float LineAlphaNormal = 0.50f;  // 标准：常规边框 (原 0.35f)
        public float LineAlphaStrong = 0.65f;  // 强化：强调描边、主框线 (原 0.45f)
        public float LineAlphaBold = 0.78f;    // 粗显：刻度线、主导引线 (原 0.60f)
        public float LineAlphaHeavy = 0.88f;   // 重：显著前景线 (原 0.75f)
        public float LineAlphaSolid = 0.95f;   // 实心：接近不透明 (原 0.90f)

        // ---- 姿态球参考系调色板派生系数 ----
        // 惯性 / 质心 / 目标 / 机体 四个抽象参考系只提供"身份色"（主题语义色），
        // 天地明暗层次全部由下列系数派生，组件内不得再内联整套调色板。
        public float FrameSkyZenithDarken = 0.72f;     // 天顶：身份色压暗量
        public float FrameSkyHorizonDarken = 0.42f;    // 天际：身份色压暗量
        public float FrameGroundHorizonDarken = 0.76f; // 地平：身份色压暗量
        public float FrameGroundNadirDarken = 0.90f;   // 地底：身份色压暗量
        public float FrameEquatorLighten = 0.80f;      // 赤道线：身份色提亮量
        public float FramePitchLighten = 0.75f;        // 俯仰梯：身份色提亮量
    }

    [Serializable]
    public class ThemeConfig
    {
        public string ThemeId = "modern_aero";
        public string DisplayName = I18n.Tr("THEME_MODERN_GLASS", "现代极简全息玻璃座舱");
        public NavballStyleType Style = NavballStyleType.Modern_Aero;
        public NavballRenderMode RenderMode = NavballRenderMode.Procedural;
        public UiShaderStyle UiStyle = UiShaderStyle.Modern_Glass;
        public AvionicsFontStyle FontStyle = AvionicsFontStyle.ModernSmooth;

        // UI 专属着色器参数 (点阵、全息、数码管、玻璃)
        public float UiDotSpacing = 2.0f;
        public float UiScanlineStrength = 0.10f;
        public float UiGlowStrength = 0.40f;
        public ColorHex UiGhostColor = ColorHex.FromColor(new Color(0.04f, 0.08f, 0.05f, 0.22f));
        public float UiGlassChamfer = 0.0f;
        public float UiGlassBorderWidth = 0.0f;
        public float UiGlassGradientStrength = 0.06f;

        // 着色器配置
        public string ShaderName = "ModularFlightPanel/NavballModern";
        public bool EnableHalftoneDither = false;
        public float DotDensity = 38f;
        public float DotMinRadius = 0.04f;
        public float DotMaxRadius = 0.46f;
        public bool EnableScanlines = false;

        // 姿态球色彩 (航空标准：上半球湛蓝天穹，下半球暖橙大地)
        public ColorHex SkyColor = ColorHex.FromColor(new Color(0.04f, 0.35f, 0.80f, 1.0f));
        public ColorHex GroundColor = ColorHex.FromColor(new Color(0.76f, 0.38f, 0.08f, 1.0f));
        public ColorHex HorizonLineColor = ColorHex.FromColor(new Color(0.95f, 0.98f, 1.0f, 0.95f));
        public ColorHex GridColor = ColorHex.FromColor(new Color(0.40f, 0.70f, 0.95f, 0.35f));
        public ColorHex DitherDotColor = ColorHex.FromColor(new Color(0.0f, 0.0f, 0.0f, 0.0f));
        public ColorHex RimGlowColor = ColorHex.FromColor(new Color(0.20f, 0.65f, 1.0f, 0.30f));

        // UI 核心色标
        public ColorHex AccentPrimary = ColorHex.FromColor(new Color(0.18f, 0.84f, 0.45f, 1.0f));    // FLIR 翡翠绿 (#2ED573)
        public ColorHex AccentSecondary = ColorHex.FromColor(new Color(0.33f, 0.63f, 1.0f, 1.0f));  // 航电钛冰蓝 (#54A0FF)
        public ColorHex AccentMagenta = ColorHex.FromColor(new Color(0.95f, 0.30f, 0.60f, 1.0f));
        public ColorHex WarningColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.05f, 1.0f));     // 琥珀金 (#FFA502)
        public ColorHex CautionColor => WarningColor;                                               // 注意黄 (航空规范别名)
        public ColorHex AccentPositive => AccentPrimary;                                            // 语义正向绿 (航空规范别名)
        public ColorHex AccentWarning => WarningColor;                                              // 语义警示黄 (航空规范别名)
        public ColorHex DangerColor = ColorHex.FromColor(new Color(1.0f, 0.28f, 0.34f, 1.0f));      // 警报珊瑚红 (#FF4757)

        // 仪表背景与边框
        public ColorHex FrameBgColor = ColorHex.FromColor(new Color(0.04f, 0.06f, 0.09f, 0.80f));   // 半透深石墨航电座舱玻璃
        public ColorHex FrameBorderColor = ColorHex.FromColor(new Color(0.35f, 0.65f, 0.95f, 0.28f)); // 细腻极细冰蓝边框
        public ColorHex InactiveMeterColor = ColorHex.FromColor(new Color(0.12f, 0.18f, 0.25f, 0.45f));

        // 文本色彩
        public ColorHex TextPrimaryColor = ColorHex.FromColor(new Color(0.98f, 0.99f, 1.0f, 1.0f));  // 纯净高对比冷白
        public ColorHex TextAccentColor = ColorHex.FromColor(new Color(0.76f, 0.86f, 0.95f, 1.0f));   // 航电冰蓝银钛 (~85% 明度，高反差无疲劳)
        public ColorHex TextInverseColor = ColorHex.FromColor(new Color(0.03f, 0.05f, 0.08f, 1.0f));  // 反色墨字（绘制在强调色实底之上）

        /// <summary>
        /// UI 样式策略（叠色/淡化/亮度系数）。WidgetStyleManager 只从此处取值，
        /// 渲染代码内不允许再出现独立的策略数字。缺失时为内置默认策略。
        /// </summary>
        public UiStylePolicy UiPolicy = new UiStylePolicy();

        // 默认预设生成器
        public static ThemeConfig CreateCyberNeon()
        {
            return new ThemeConfig
            {
                ThemeId = "cyber_neon",
                DisplayName = I18n.Tr("THEME_CYBER_NEON", "赛博霓虹点阵"),
                Style = NavballStyleType.Cyber_Neon,
                UiStyle = UiShaderStyle.Cyber_Neon,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                EnableScanlines = true,
                UiGlowStrength = 0.60f,
                UiScanlineStrength = 0.18f,
                SkyColor = ColorHex.FromColor(new Color(0.0f, 0.88f, 0.85f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.0f, 0.32f, 0.48f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.25f, 0.25f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.85f, 0.70f)),
                DitherDotColor = ColorHex.FromColor(new Color(0.0f, 0.12f, 0.22f, 0.85f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.90f, 0.50f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.40f, 1.0f)),    // 电光毒药绿 (#00FF66)
                AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.90f, 1.0f, 1.0f)),  // 霓虹青 (#00E5FF)
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.08f, 0.58f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.75f, 0.0f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.20f, 0.20f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.02f, 0.04f, 0.07f, 0.88f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.80f, 0.85f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.05f, 0.15f, 0.12f, 0.65f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.95f, 1.0f, 0.98f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.85f, 1.0f))
            };
        }

        public static ThemeConfig CreateSpaceXDragon()
        {
            return new ThemeConfig
            {
                ThemeId = "spacex_dragon",
                DisplayName = I18n.Tr("THEME_SPACEX_DRAGON", "SpaceX 龙飞船全息"),
                Style = NavballStyleType.Modern_Aero,
                UiStyle = UiShaderStyle.Modern_Glass,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.02f, 0.08f, 0.22f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.03f, 0.06f, 0.12f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.95f, 0.98f, 1.0f, 0.95f)),
                GridColor = ColorHex.FromColor(new Color(0.20f, 0.55f, 0.95f, 0.35f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.0f, 0.65f, 1.0f, 0.40f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.94f, 0.96f, 1.0f, 1.0f)),    // 极简冷白钛金
                AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.55f, 1.0f, 1.0f)),  // SpaceX 电光蓝
                AccentMagenta = ColorHex.FromColor(new Color(0.85f, 0.30f, 0.90f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.82f, 0.18f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.20f, 0.25f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.015f, 0.025f, 0.045f, 0.92f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.28f, 0.38f, 0.52f, 0.50f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.08f, 0.11f, 0.18f, 0.45f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.55f, 0.65f, 0.80f, 1.0f))
            };
        }

        public static ThemeConfig CreateModernAero()
        {
            return new ThemeConfig
            {
                ThemeId = "modern_aero",
                DisplayName = I18n.Tr("THEME_MODERN_GLASS", "现代极简全息玻璃座舱"),
                Style = NavballStyleType.Modern_Aero,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.04f, 0.35f, 0.80f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.76f, 0.38f, 0.08f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.95f, 0.98f, 1.0f, 0.95f)),
                GridColor = ColorHex.FromColor(new Color(0.40f, 0.70f, 0.95f, 0.35f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.20f, 0.65f, 1.0f, 0.30f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.18f, 0.84f, 0.45f, 1.0f)),    // FLIR 翡翠绿 (#2ED573)
                AccentSecondary = ColorHex.FromColor(new Color(0.33f, 0.63f, 1.0f, 1.0f)),  // 航电钛冰蓝 (#54A0FF)
                AccentMagenta = ColorHex.FromColor(new Color(0.95f, 0.30f, 0.60f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.05f, 1.0f)),     // 琥珀金 (#FFA502)
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.28f, 0.34f, 1.0f)),      // 警报珊瑚红 (#FF4757)
                FrameBgColor = ColorHex.FromColor(new Color(0.04f, 0.06f, 0.09f, 0.80f)),   // 半透深石墨航电座舱玻璃
                FrameBorderColor = ColorHex.FromColor(new Color(0.35f, 0.65f, 0.95f, 0.28f)), // 细腻极细冰蓝边框
                InactiveMeterColor = ColorHex.FromColor(new Color(0.12f, 0.18f, 0.25f, 0.45f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.98f, 0.99f, 1.0f, 1.0f)),  // 纯净高对比冷白
                TextAccentColor = ColorHex.FromColor(new Color(0.76f, 0.86f, 0.95f, 1.0f))   // 航电冰蓝银钛
            };
        }

        public static ThemeConfig CreateClassicAero()
        {
            return new ThemeConfig
            {
                ThemeId = "classic_aero",
                DisplayName = I18n.Tr("THEME_CLASSIC_AERO", "经典航空蓝棕"),
                Style = NavballStyleType.Classic_Aero,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.04f, 0.35f, 0.80f, 1.0f)),     // 经典天空湛蓝
                GroundColor = ColorHex.FromColor(new Color(0.76f, 0.38f, 0.08f, 1.0f)),  // 经典大地暖橙
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.9f, 0.2f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 0.75f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.2f, 0.9f, 0.3f, 1.0f)),
                AccentSecondary = ColorHex.FromColor(new Color(0.3f, 0.7f, 1.0f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.6f, 0.1f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.1f, 0.12f, 0.14f, 0.9f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.5f, 0.6f, 0.7f, 0.8f))
            };
        }

        public static ThemeConfig CreateApollo1969()
        {
            return new ThemeConfig
            {
                ThemeId = "apollo_1969",
                DisplayName = I18n.Tr("THEME_APOLLO_1969", "阿波罗 1969 AGC 复古"),
                Style = NavballStyleType.Apollo_1969,
                UiStyle = UiShaderStyle.Digital_Segment,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                EnableScanlines = true,
                SkyColor = ColorHex.FromColor(new Color(0.0f, 0.35f, 0.15f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.0f, 0.15f, 0.05f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.3f, 1.0f, 0.4f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.2f, 0.9f, 0.3f, 0.6f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.1f, 0.9f, 0.2f, 0.4f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.2f, 1.0f, 0.35f, 1.0f)),
                AccentSecondary = ColorHex.FromColor(new Color(0.44f, 0.56f, 1.0f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.24f, 0.74f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.82f, 0.1f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.18f, 0.3f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.008f, 0.012f, 0.04f, 0.95f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.24f, 0.31f, 0.9f, 0.9f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.055f, 0.07f, 0.18f, 0.8f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.85f, 0.9f, 1.0f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.43f, 0.7f, 1.0f, 1.0f))
            };
        }

        public static ThemeConfig CreateApolloDsky()
        {
            return new ThemeConfig
            {
                ThemeId = "apollo_dsky",
                DisplayName = I18n.Tr("THEME_APOLLO_DSKY", "阿波罗 DSKY 荧光绿点阵"),
                Style = NavballStyleType.Apollo_1969,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.45f,
                UiScanlineStrength = 0.12f,
                UiGhostColor = ColorHex.FromColor(new Color(0.02f, 0.08f, 0.03f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.0f, 0.28f, 0.12f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.0f, 0.10f, 0.04f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.2f, 1.0f, 0.4f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.15f, 0.85f, 0.35f, 0.5f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.1f, 0.95f, 0.3f, 0.35f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.15f, 1.0f, 0.45f, 1.0f)),    // 经典荧光绿
                AccentSecondary = ColorHex.FromColor(new Color(0.2f, 0.85f, 0.6f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.4f, 0.2f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.85f, 0.1f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.2f, 0.2f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.01f, 0.025f, 0.015f, 0.94f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.1f, 0.65f, 0.25f, 0.6f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.03f, 0.1f, 0.05f, 0.6f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.25f, 1.0f, 0.55f, 1.0f)),  // 纯净高亮点阵绿
                TextAccentColor = ColorHex.FromColor(new Color(0.12f, 0.65f, 0.35f, 1.0f))
            };
        }

        public static ThemeConfig CreateDiffractiveHud()
        {
            return new ThemeConfig
            {
                ThemeId = "diffractive_hud",
                DisplayName = I18n.Tr("THEME_F16_DIFFRACTIVE_HUD", "F-16 衍射全息冰蓝 HUD"),
                Style = NavballStyleType.Modern_Aero,
                UiStyle = UiShaderStyle.Phosphor_HUD,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = true,
                UiScanlineStrength = 0.20f,
                UiGlowStrength = 0.40f,
                SkyColor = ColorHex.FromColor(new Color(0.02f, 0.12f, 0.28f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.04f, 0.08f, 0.14f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.4f, 0.95f, 1.0f, 0.95f)),
                GridColor = ColorHex.FromColor(new Color(0.2f, 0.7f, 1.0f, 0.35f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.0f, 0.85f, 1.0f, 0.45f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.0f, 0.92f, 1.0f, 1.0f)),      // 冰蓝全息 (#00EBFF)
                AccentSecondary = ColorHex.FromColor(new Color(0.35f, 0.65f, 1.0f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(0.85f, 0.35f, 1.0f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.75f, 0.1f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.25f, 0.35f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.015f, 0.035f, 0.06f, 0.76f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.0f, 0.85f, 1.0f, 0.45f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.05f, 0.12f, 0.22f, 0.5f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.85f, 0.96f, 1.0f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.35f, 0.75f, 1.0f, 1.0f))
            };
        }

        public static ThemeConfig CreateVintageAmber()
        {
            return new ThemeConfig
            {
                ThemeId = "vintage_amber",
                DisplayName = I18n.Tr("THEME_VINTAGE_AMBER_CRT", "复古等离子琥珀金 CRT"),
                Style = NavballStyleType.Classic_Aero,
                UiStyle = UiShaderStyle.Phosphor_HUD,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = false,
                EnableScanlines = true,
                UiScanlineStrength = 0.18f,
                UiGlowStrength = 0.45f,
                SkyColor = ColorHex.FromColor(new Color(0.28f, 0.16f, 0.02f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.12f, 0.06f, 0.01f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.82f, 0.2f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.1f, 0.4f)),
                RimGlowColor = ColorHex.FromColor(new Color(1.0f, 0.6f, 0.05f, 0.4f)),
                AccentPrimary = ColorHex.FromColor(new Color(1.0f, 0.68f, 0.05f, 1.0f)),     // 等离子琥珀金
                AccentSecondary = ColorHex.FromColor(new Color(1.0f, 0.48f, 0.15f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.3f, 0.5f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.85f, 0.1f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.25f, 0.2f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.04f, 0.022f, 0.008f, 0.92f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.9f, 0.6f, 0.15f, 0.55f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.18f, 0.09f, 0.02f, 0.55f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(1.0f, 0.88f, 0.65f, 1.0f)),  // 纯暖色琥珀白
                TextAccentColor = ColorHex.FromColor(new Color(0.85f, 0.55f, 0.18f, 1.0f))
            };
        }

        public static ThemeConfig CreateCyberMatrix()
        {
            return new ThemeConfig
            {
                ThemeId = "cyber_matrix",
                DisplayName = I18n.Tr("THEME_CYBER_MATRIX", "赛博黑客矩阵"),
                Style = NavballStyleType.Cyber_Neon,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.50f,
                UiScanlineStrength = 0.14f,
                UiGhostColor = ColorHex.FromColor(new Color(0.12f, 0.02f, 0.10f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.25f, 0.04f, 0.22f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.08f, 0.01f, 0.07f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.15f, 0.65f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.9f, 0.2f, 0.7f, 0.45f)),
                RimGlowColor = ColorHex.FromColor(new Color(1.0f, 0.1f, 0.6f, 0.45f)),
                AccentPrimary = ColorHex.FromColor(new Color(1.0f, 0.12f, 0.62f, 1.0f)),    // 霓虹品红 (#FF1F9E)
                AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.95f, 0.82f, 1.0f)),  // 霓虹薄荷青
                AccentMagenta = ColorHex.FromColor(new Color(0.75f, 0.2f, 1.0f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.85f, 0.0f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.1f, 0.25f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.035f, 0.01f, 0.045f, 0.92f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.95f, 0.15f, 0.65f, 0.6f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.15f, 0.04f, 0.12f, 0.6f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.98f, 0.85f, 0.95f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.85f, 0.25f, 0.65f, 1.0f))
            };
        }

        public static ThemeConfig CreateStarshipMars()
        {
            return new ThemeConfig
            {
                ThemeId = "starship_mars",
                DisplayName = I18n.Tr("THEME_STARSHIP_MARS", "星舰火星开拓者"),
                Style = NavballStyleType.Modern_Aero,
                UiStyle = UiShaderStyle.Phosphor_HUD,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = true,
                UiScanlineStrength = 0.16f,
                UiGlowStrength = 0.55f,
                SkyColor = ColorHex.FromColor(new Color(0.015f, 0.020f, 0.038f, 1.0f)),      // 深空黑天
                GroundColor = ColorHex.FromColor(new Color(0.28f, 0.08f, 0.03f, 1.0f)),     // 火星赤铁矿熔岩暗赭
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.75f, 0.30f, 1.0f)), // 猛禽白热赤金
                GridColor = ColorHex.FromColor(new Color(1.0f, 0.45f, 0.15f, 0.35f)),       // 火星暖金细栅格
                RimGlowColor = ColorHex.FromColor(new Color(1.0f, 0.30f, 0.05f, 0.45f)),    // 猛禽等离子炽橙辉光
                AccentPrimary = ColorHex.FromColor(new Color(1.0f, 0.45f, 0.05f, 1.0f)),    // 猛禽炽烈橙金 (#FF730D)
                AccentSecondary = ColorHex.FromColor(new Color(1.0f, 0.72f, 0.20f, 1.0f)),  // 火星地平金黄 (#FFB833)
                AccentMagenta = ColorHex.FromColor(new Color(0.95f, 0.22f, 0.45f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.85f, 0.15f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.18f, 0.18f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.018f, 0.014f, 0.018f, 0.93f)), // 火山黑曜石黑晶底板
                FrameBorderColor = ColorHex.FromColor(new Color(1.0f, 0.45f, 0.08f, 0.50f)), // 猛禽炽橙微晶描边
                InactiveMeterColor = ColorHex.FromColor(new Color(0.12f, 0.06f, 0.04f, 0.55f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.98f, 0.95f, 0.90f, 1.0f)), // 炽白高对比文本
                TextAccentColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.22f, 1.0f))   // 猛禽焰橙单位文本
            };
        }

        public static ThemeConfig CreateDeepSpaceVoyager()
        {
            return new ThemeConfig
            {
                ThemeId = "deep_space_voyager",
                DisplayName = I18n.Tr("THEME_DEEP_SPACE_VOYAGER", "深空旅行者金黄"),
                Style = NavballStyleType.Classic_Aero,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                DotDensity = 36f,
                DotMinRadius = 0.04f,
                DotMaxRadius = 0.45f,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.52f,
                UiScanlineStrength = 0.15f,
                UiGhostColor = ColorHex.FromColor(new Color(0.12f, 0.09f, 0.02f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.01f, 0.015f, 0.03f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.06f, 0.05f, 0.03f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.84f, 0.22f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.85f, 0.70f, 0.20f, 0.45f)),
                DitherDotColor = ColorHex.FromColor(new Color(0.15f, 0.10f, 0.02f, 0.70f)),
                RimGlowColor = ColorHex.FromColor(new Color(1.0f, 0.78f, 0.10f, 0.40f)),
                AccentPrimary = ColorHex.FromColor(new Color(1.0f, 0.82f, 0.12f, 1.0f)),    // 纯金点阵高光 (#FFD11F)
                AccentSecondary = ColorHex.FromColor(new Color(0.92f, 0.88f, 0.75f, 1.0f)),  // 铂金白光
                AccentMagenta = ColorHex.FromColor(new Color(0.95f, 0.45f, 0.25f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.05f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.22f, 0.20f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.018f, 0.016f, 0.012f, 0.94f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.95f, 0.78f, 0.18f, 0.55f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.12f, 0.10f, 0.04f, 0.55f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(1.0f, 0.90f, 0.40f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.80f, 0.68f, 0.32f, 1.0f))
            };
        }

        public static ThemeConfig CreateSr71Blackbird()
        {
            return new ThemeConfig
            {
                ThemeId = "sr71_blackbird",
                DisplayName = I18n.Tr("THEME_SR71_BLACKBIRD", "SR-71 黑鸟战术暗红"),
                Style = NavballStyleType.Modern_Aero,
                UiStyle = UiShaderStyle.Modern_Glass,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                UiGlassChamfer = 0.035f,
                UiGlassBorderWidth = 0.025f,
                UiGlassGradientStrength = 0.06f,
                SkyColor = ColorHex.FromColor(new Color(0.035f, 0.050f, 0.080f, 1.0f)),      // 85000英尺平流层天顶深邃炭黑蓝
                GroundColor = ColorHex.FromColor(new Color(0.110f, 0.045f, 0.060f, 1.0f)),   // 红外前视夜视暗地表
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.28f, 0.32f, 1.0f)), // 战术高光鲜红地平线
                GridColor = ColorHex.FromColor(new Color(0.70f, 0.25f, 0.30f, 0.38f)),       // 战术雷达经纬微刻线
                RimGlowColor = ColorHex.FromColor(new Color(1.0f, 0.15f, 0.22f, 0.40f)),    // 超音速激波血红光晕
                AccentPrimary = ColorHex.FromColor(new Color(1.0f, 0.22f, 0.28f, 1.0f)),    // 战术激光鲜红 (#FF3847, 关键指示与活动状态)
                AccentSecondary = ColorHex.FromColor(new Color(0.95f, 0.42f, 0.30f, 1.0f)),  // 战术次级暖珊瑚红 (#F26B4D)
                AccentMagenta = ColorHex.FromColor(new Color(0.92f, 0.18f, 0.55f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.72f, 0.12f, 1.0f)),     // 告警琥珀金 (#FFB81F, 保证绝佳辨识度)
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.12f, 0.16f, 1.0f)),      // 极限超速闪烁红 (#FF1F29)
                FrameBgColor = ColorHex.FromColor(new Color(0.022f, 0.018f, 0.022f, 0.92f)), // 吸波碳纤纯黑微晶底板
                FrameBorderColor = ColorHex.FromColor(new Color(0.75f, 0.16f, 0.22f, 0.52f)), // 战术阳极氧化红细边框
                InactiveMeterColor = ColorHex.FromColor(new Color(0.15f, 0.06f, 0.08f, 0.55f)), // 暗炭灰红未激活槽位
                TextPrimaryColor = ColorHex.FromColor(new Color(0.96f, 0.94f, 0.95f, 1.0f)), // 极高对比冷白读数 (一眼即可辨识数值)
                TextAccentColor = ColorHex.FromColor(new Color(1.0f, 0.40f, 0.45f, 1.0f))   // 战术高亮红单位标签 (#FF6673)
            };
        }

        public static ThemeConfig CreateVostok1961()
        {
            return new ThemeConfig
            {
                ThemeId = "vostok_1961",
                DisplayName = I18n.Tr("THEME_VOSTOK_1961", "东方一号苏联机械青"),
                Style = NavballStyleType.Classic_Aero,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                DotDensity = 40f,
                DotMinRadius = 0.03f,
                DotMaxRadius = 0.48f,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.42f,
                UiScanlineStrength = 0.14f,
                UiGhostColor = ColorHex.FromColor(new Color(0.01f, 0.06f, 0.06f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.02f, 0.22f, 0.25f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.04f, 0.14f, 0.12f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.95f, 0.98f, 0.88f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.10f, 0.80f, 0.75f, 0.45f)),
                DitherDotColor = ColorHex.FromColor(new Color(0.01f, 0.08f, 0.09f, 0.75f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.0f, 0.85f, 0.80f, 0.38f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.05f, 0.95f, 0.85f, 1.0f)),    // 东方号高亮绿松石水鸭青 (#0DF2D9)
                AccentSecondary = ColorHex.FromColor(new Color(0.20f, 0.75f, 0.95f, 1.0f)),  // 贝加尔湖冰蓝
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.25f, 0.25f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.70f, 0.10f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.20f, 0.20f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.015f, 0.035f, 0.035f, 0.93f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.08f, 0.75f, 0.68f, 0.55f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.03f, 0.12f, 0.12f, 0.60f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.75f, 1.0f, 0.95f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.25f, 0.75f, 0.70f, 1.0f))
            };
        }

        public static ThemeConfig CreateMatrixHacker()
        {
            return new ThemeConfig
            {
                ThemeId = "matrix_hacker",
                DisplayName = I18n.Tr("THEME_MATRIX_TERMINAL", "矩阵终端绿点阵"),
                Style = NavballStyleType.Cyber_Neon,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                DotDensity = 38f,
                DotMinRadius = 0.04f,
                DotMaxRadius = 0.46f,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.48f,
                UiScanlineStrength = 0.15f,
                UiGhostColor = ColorHex.FromColor(new Color(0.01f, 0.08f, 0.03f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.008f, 0.015f, 0.010f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.015f, 0.08f, 0.03f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(0.20f, 1.0f, 0.40f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.15f, 0.85f, 0.35f, 0.45f)),
                DitherDotColor = ColorHex.FromColor(new Color(0.02f, 0.15f, 0.05f, 0.80f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.10f, 1.0f, 0.35f, 0.40f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.40f, 1.0f)),     // 纯正黑客终端绿 (#00FF66)
                AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.90f, 0.64f, 1.0f)),   // 矩阵青绿 (#00E5A3)
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.20f, 0.60f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.80f, 0.05f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.18f, 0.18f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.010f, 0.016f, 0.012f, 0.95f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.10f, 0.85f, 0.35f, 0.55f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.04f, 0.12f, 0.06f, 0.55f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.35f, 1.0f, 0.60f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.15f, 0.75f, 0.35f, 1.0f))
            };
        }

        public static ThemeConfig CreateEvaUnit01()
        {
            return new ThemeConfig
            {
                ThemeId = "eva_unit01",
                DisplayName = I18n.Tr("THEME_EVA_UNIT01", "EVA 初号机暴走电光点阵"),
                Style = NavballStyleType.Cyber_Neon,
                UiStyle = UiShaderStyle.Dot_Matrix,
                FontStyle = AvionicsFontStyle.RetroPixel,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                DotDensity = 36f,
                DotMinRadius = 0.04f,
                DotMaxRadius = 0.45f,
                EnableScanlines = true,
                UiDotSpacing = 2.0f,
                UiGlowStrength = 0.50f,
                UiScanlineStrength = 0.14f,
                UiGhostColor = ColorHex.FromColor(new Color(0.08f, 0.02f, 0.10f, 0.25f)),
                SkyColor = ColorHex.FromColor(new Color(0.12f, 0.03f, 0.18f, 1.0f)),       // 初号机装甲深邃战术紫 (#1F082E)
                GroundColor = ColorHex.FromColor(new Color(0.05f, 0.01f, 0.08f, 1.0f)),    // 暴走夜战墨暗紫
                HorizonLineColor = ColorHex.FromColor(new Color(0.20f, 1.0f, 0.35f, 1.0f)), // 暴走高亮荧光电光绿 (#33FF59)
                GridColor = ColorHex.FromColor(new Color(0.20f, 0.90f, 0.40f, 0.45f)),      // 初号机荧绿点阵网格
                DitherDotColor = ColorHex.FromColor(new Color(0.06f, 0.02f, 0.10f, 0.75f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.25f, 1.0f, 0.30f, 0.45f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.15f, 1.0f, 0.35f, 1.0f)),    // 初号机标志暴走荧光绿 (#26FF59)
                AccentSecondary = ColorHex.FromColor(new Color(0.70f, 0.30f, 1.0f, 1.0f)),  // 初号机机体战术电光紫 (#B34DFF)
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.15f, 0.65f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.65f, 0.05f, 1.0f)),     // 警备使徒橙黄
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.15f, 0.15f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.022f, 0.012f, 0.030f, 0.94f)),// 初号机纯黑紫装甲底板
                FrameBorderColor = ColorHex.FromColor(new Color(0.20f, 0.95f, 0.40f, 0.60f)),// 暴走荧绿点阵框
                InactiveMeterColor = ColorHex.FromColor(new Color(0.10f, 0.04f, 0.14f, 0.60f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(0.85f, 1.0f, 0.90f, 1.0f)),  // 高亮白绿点阵字
                TextAccentColor = ColorHex.FromColor(new Color(0.25f, 0.90f, 0.45f, 1.0f))
            };
        }

        public static ThemeConfig CreateBoeing787()
        {
            return new ThemeConfig
            {
                ThemeId = "boeing_787",
                DisplayName = I18n.Tr("THEME_BOEING_787", "波音 787 梦想客机航电"),
                Style = NavballStyleType.Modern_Aero,
                RenderMode = NavballRenderMode.ProceduralVector,
                UiStyle = UiShaderStyle.Modern_Glass,
                FontStyle = AvionicsFontStyle.ModernSmooth,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                UiDotSpacing = 2.0f,
                UiScanlineStrength = 0.0f,
                UiGlowStrength = 0.15f,
                UiGhostColor = ColorHex.FromColor(new Color(0.02f, 0.03f, 0.04f, 0.20f)),
                UiGlassChamfer = 0.0f,
                UiGlassBorderWidth = 0.0f,
                UiGlassGradientStrength = 0.03f,
                SkyColor = ColorHex.FromColor(new Color(0.043f, 0.400f, 0.761f, 1.0f)),        // 波音经典航电湛蓝 (#0A66C2)
                GroundColor = ColorHex.FromColor(new Color(0.471f, 0.267f, 0.082f, 1.0f)),     // 波音地平暖赭褐 (#784415)
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 1.0f)),       // 纯白高对比地平仪基准线
                GridColor = ColorHex.FromColor(new Color(0.95f, 0.98f, 1.0f, 0.78f)),           // 纯白清晰俯仰标尺
                RimGlowColor = ColorHex.FromColor(new Color(0.10f, 0.55f, 0.95f, 0.28f)),       // 姿态球边缘微蓝光晕
                AccentPrimary = ColorHex.FromColor(new Color(0.0f, 0.898f, 0.322f, 1.0f)),      // 波音航电指示绿 (#00E552, 活化/正常/指令标)
                AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.835f, 0.957f, 1.0f)),    // 波音航电青蓝 (#00D5F4, 系统标签与模式文案)
                AccentMagenta = ColorHex.FromColor(new Color(0.961f, 0.161f, 0.678f, 1.0f)),    // 波音 FMC 目标品红 (#F529AD, 选定航向/速度/高度)
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.627f, 0.0f, 1.0f)),         // 波音警示琥珀金 (#FFA000, 警戒限值与注意状态)
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.180f, 0.180f, 1.0f)),         // 波音警戒超速/超温红线 (#FF2E2E)
                FrameBgColor = ColorHex.FromColor(new Color(0.012f, 0.015f, 0.020f, 0.90f)),    // 梦想客机深邃纯黑晶液晶底板
                FrameBorderColor = ColorHex.FromColor(new Color(0.55f, 0.65f, 0.75f, 0.38f)),    // 细腻钛灰仪表框架边框
                InactiveMeterColor = ColorHex.FromColor(new Color(0.18f, 0.22f, 0.28f, 0.50f)),// 刻度标尺暗炭灰底轨
                TextPrimaryColor = ColorHex.FromColor(new Color(0.98f, 0.99f, 1.0f, 1.0f)),     // 纯净高对比冷白读数大字
                TextAccentColor = ColorHex.FromColor(new Color(0.0f, 0.835f, 0.957f, 1.0f)),    // 系统参数青蓝标识标签
                TextInverseColor = ColorHex.FromColor(new Color(0.02f, 0.03f, 0.04f, 1.0f))     // 高光药丸反色黑字
            };
        }

        public static System.Collections.Generic.List<ThemeConfig> GetAllBuiltinThemes()
        {
            return new System.Collections.Generic.List<ThemeConfig>
            {
                CreateBoeing787(),
                CreateModernAero(),
                CreateClassicAero(),
                CreateSpaceXDragon(),
                CreateDiffractiveHud(),
                CreateVintageAmber(),
                CreateApolloDsky(),
                CreateApollo1969(),
                CreateStarshipMars(),
                CreateDeepSpaceVoyager(),
                CreateSr71Blackbird(),
                CreateVostok1961(),
                CreateMatrixHacker(),
                CreateEvaUnit01(),
                CreateCyberMatrix(),
                CreateCyberNeon()
            };
        }
    }

    /// <summary>
    /// 收纳坞按钮自定义过滤与别名配置项 (Dock Button Rule)
    /// </summary>
    [Serializable]
    public class DockButtonRule
    {
        public string Key = "";
        public string DefaultName = "";
        public string CustomLabel = "";
        public bool IsVisible = true;
        public bool IsFavorite = false;

        public string ButtonKey { get => Key; set => Key = value; }
        public string DisplayName { get => string.IsNullOrEmpty(CustomLabel) ? DefaultName : CustomLabel; set => CustomLabel = value; }
    }

    /// <summary>
    /// 主题与全局航电工作台配置契约 (Theme & Global Avionics Settings Data Contract)
    /// 纯 C# 解耦数据模型，无 UnityEngine 依赖，用于持久化、无头门禁验证与跨版本双向往返。
    /// </summary>
    [Serializable]
    public class ThemeSettingsData
    {
        public string SelectedThemeId = "modern_aero";
        public string SelectedLanguage = "auto"; // "auto", "zh-CN", "en-US"
        public int RenderMode = 1; // 0 = StockTexture, 1 = ProceduralVector, 2 = ProceduralBake, 3 = StockDirect
        public bool HideStockNavball = true;
        public bool HideStockAltimeter = false;
        public bool HideStockBottomLeft = false;
        public bool HideStockTimeWarp = false;
        public bool HideStockCommNet = false;
        public bool HideStockToolbar = false;
        public int ToolbarStyleMode = 1; // 0 = Stock, 1 = Reskin, 2 = ModernWidget
        public int NonFlightToolbarMode = 1; // 0 = Stock (恢复原版经典), 1 = Reskin (保持黑晶重肤 Hook)
        public bool MasterBypass = false;
        public bool ShowPerformanceBadge = false;
        public bool EnableGpu2DUIAcceleration = true; // 开启 2D 仪表 GPU 单 Quad 程序化渲染加速

        // 自适应渲染分辨率与超采样倍率设置 (Smart Resolution & Supersampling)
        public bool AutoAdaptResolution = true;
        public float GlobalRenderScaleMultiplier = 1.0f;

        // 全局双轨刷新率与心跳调度配置 (Global Dual-Track Refresh & Heartbeat Scheduling)
        public int GlobalRefreshProfile = 1; // 0 = Ultra60Hz, 1 = Balanced, 2 = EcoPowerSaver
        public int RefreshControlMode = 0;   // 0 = VSync_GameFPS, 1 = CustomHz_FreeTier
        public float GlobalStandardHz = 60.0f;
        public float GlobalSlowHz = 30.0f;
        public float GlobalRelaxedHz = 10.0f;
        public float GlobalUltraLowHz = 2.0f;
        public float GlobalDataHeartbeatHz = 0f; // 0f = 遵循各组件阶梯/独立定义, >0f = 全局数据心跳统一频率

        // 收纳坞按钮自定义过滤与别名配置
        public System.Collections.Generic.List<DockButtonRule> DockRules = new System.Collections.Generic.List<DockButtonRule>();
        public bool DockShowHiddenDrawer = false;
        public int DockOrientation = 0; // 0 = 纵向双列, 1 = 横向双行, 2 = 横向单行

        // 常用 MOD 独立快捷面板设置 (已废弃并整合至编辑UI)
        public bool DockEnableFavoritePanel = false;
        public int DockFavoriteOrientation = 1; // 0 = 纵向单列, 1 = 横向单行, 2 = 横向双行
        public bool DockKeepFavoritesInMain = false;
        public float DockFavoritePosX = 0f;
        public float DockFavoritePosY = -380f;

        // Alt+N 航电工作台窗口几何状态持久化 (Window Geometry Persistence)
        public float SettingsWindowX = -1f;
        public float SettingsWindowY = -1f;
        public float SettingsWindowWidth = 1040f;
        public float SettingsWindowHeight = 650f;
        public bool SettingsWindowMaximized = false;
    }
}

#if !KSP_RUNTIME && !UNITY_5_3_OR_NEWER && !UNITY_EDITOR
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
    }
}

namespace ModularFlightPanel.Core
{
    public static class I18n
    {
        public static string Tr(string key, string fallback) => fallback;
    }
}
#endif
