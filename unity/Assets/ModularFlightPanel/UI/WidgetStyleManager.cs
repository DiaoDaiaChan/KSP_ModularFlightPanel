using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    public enum CardStyleRole
    {
        Normal,            // 标准半透玻璃卡片
        Emphasized,        // 高亮强调框（如主姿态、活动告警区）
        Warning,           // 琥珀警告框
        Danger,            // 红色警报框
        InteractiveButton, // 可交互按钮未按下
        ButtonPressed,     // 可交互按钮按下激活态
        TransparentHUD,    // 无底色/纯全息视口
        SubtleSlot         // 次级插槽或未激活背景
    }

    public enum TextStyleRole
    {
        PrimaryValue,   // 主遥测大字（翡翠绿/冰蓝，高对比，带 Shader 质感）
        SecondaryValue, // 次级读数
        Label,          // 指标标题标签（钛灰/暗绿，紧凑）
        Unit,           // 单位（m/s, km, kPa 等）
        Warning,        // 警告文字（琥珀金）
        Danger,         // 紧急危险文字（珊瑚红）
        Accent,         // 强调文字（主或副强调色）
        Muted,            // 弱化文字（不可用或占位符）
        Cardinal,         // 航向罗盘方位文字
        InverseOnAccent   // 反色墨字：绘制在强调色实底（激活按钮）之上
    }

    public enum ButtonVisualRole
    {
        Normal,       // 普通控制按钮
        Primary,      // 核心主动作（如分级确认、模式切换）
        Warning,      // 警告动作（如紧急切断）
        Danger,       // 危险动作（如强制终止、解体逃逸）
        ActiveToggle  // 激活锁/已开状态
    }

    public enum MeterStyleRole
    {
        Primary,   // 正常主计量条
        Secondary, // 辅助次级计量条
        Warning,   // 告警计量条
        Danger,    // 极危计量条
        Track      // 底槽轨道
    }

    /// <summary>
    /// 表面材质语义角色 (SurfaceStyleRole)
    /// 底色一律由 ThemeConfig.FrameBgColor 派生（向副强调色混色 + 指定透明度，系数来自 UiStylePolicy），
    /// 组件内严禁再出现 new Color(0.06f, 0.09f, 0.14f, 0.45f) 这类"私有暗蓝面板调色板"。
    /// </summary>
    public enum SurfaceStyleRole
    {
        Panel,       // 标准面板/卡片玻璃底
        PanelDeep,   // 深色内嵌底（凹槽、子面板、相机背景）
        Slot,        // 行/单元格/条目底（半透）
        SlotActive,  // 激活/选中条目底
        Control,     // 按钮与控件底
        Inset,       // 深色插槽衬底（刻度槽、沉槽）
        Tile,        // 工具栏磁贴底
        LedOff,      // 指示灯熄灭态
        LedStandby   // 指示灯待机/未选中态
    }

    /// <summary>
    /// 状态表面语义角色 (StatusSurfaceRole)：正常 / 注意 / 危险 / 未激活
    /// 用于状态徽标底与状态面板底，颜色取自主题的对应语义色。
    /// </summary>
    public enum StatusSurfaceRole
    {
        Success,
        Caution,
        Danger,
        Inactive
    }

    /// <summary>
    /// 线条/描边视觉权重 (LineWeight)：透明度档位由 UiStylePolicy 统一给出，
    /// 取代散落各处的 new Color(c.r, c.g, c.b, 0.35f) 魔法数字。
    /// </summary>
    public enum LineWeight
    {
        Ghost,   // 幽灵级：水印底纹、极弱指示
        Faint,   // 极弱：栅格线、弱分隔
        Subtle,  // 次级：次级描边、内圈
        Light,   // 轻：装饰环、外圈过渡
        Normal,  // 标准：常规边框
        Medium = Normal, // 兼容中等边框权重别名
        Strong,  // 强化：强调描边、主框线
        Bold,    // 粗显：刻度线、主导引线
        Heavy,   // 重：显著前景线
        Solid    // 实心：接近不透明
    }

    /// <summary>
    /// 姿态球参考系调色板 (NavballFramePalette)
    /// 全部字段由"身份色"（主题语义色）+ UiStylePolicy 派生系数生成，
    /// 组件内不得再出现整段内联的参考系配色表。
    /// </summary>
    public struct NavballFramePalette
    {
        public Color SkyZenith;
        public Color SkyHorizon;
        public Color GroundHorizon;
        public Color GroundNadir;
        public Color Equator;
        public Color PitchLadder;
        public Color HeadingLine;
        public Color Rim;
    }

    /// <summary>
    /// 全局 UI 控件统一样式与着色器管理器 (WidgetStyleManager)
    /// 架构通路：Shaders / ThemeConfig(+UiStylePolicy) -> WidgetStyleManager -> 各具体仪表组件
    /// 彻底消除组件内部的 new Color(...) 硬编码、透明度乱象与 Material 挂载遗漏。
    /// 渲染代码内不存在任何独立调色板或策略数字：颜色来自 ThemeConfig，系数来自 ThemeConfig.UiPolicy。
    /// </summary>
    public class WidgetStyleManager
    {
        private static WidgetStyleManager _instance;
        public static WidgetStyleManager Instance => _instance ?? (_instance = new WidgetStyleManager());

        private readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();

        public event Action<ThemeConfig> OnStyleRefreshed;

        private WidgetStyleManager()
        {
            if (ThemeManager.Instance != null)
            {
                ThemeManager.Instance.OnThemeChanged += HandleThemeChanged;
            }
        }

        private void HandleThemeChanged(ThemeConfig newTheme)
        {
            _materialCache.Clear();
            OnStyleRefreshed?.Invoke(newTheme);
        }

        public ThemeConfig CurrentTheme => ResolveTheme(null);

        #region Material Pipeline (Shader -> Material Cache)

        /// <summary>
        /// 获取针对当前主题配置生成的 UI 面板 / 文字专用 Material
        /// 带有自动缓存复用，避免产生 GC 垃圾与重复 Draw Call
        /// </summary>
        public Material GetUiMaterial(bool isText = false)
        {
            ThemeConfig theme = CurrentTheme;
            if (theme == null) return null;

            string key = $"{theme.ThemeId}_{(int)theme.UiStyle}_{(isText ? "txt" : "panel")}";
            if (_materialCache.TryGetValue(key, out Material mat) && mat != null)
            {
                return mat;
            }

            Shader s = null;
            switch (theme.UiStyle)
            {
                case UiShaderStyle.Dot_Matrix:
                    // 物理点阵主题背景与文字均使用 DotMatrixUI，呈现深空旅行者般的微粒点阵机械底板与发光点阵字符
                    s = AssetLoader.DotMatrixShader;
                    break;
                case UiShaderStyle.Phosphor_HUD:
                    // 全息磷光主题：文字与标度使用高动态磷光扫描线着色器，背景卡片面板使用座舱黑晶玻璃着色器，防止大面积白热过载
                    s = isText ? AssetLoader.PhosphorHoloShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Digital_Segment:
                    s = isText ? AssetLoader.DigitalSegmentShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Cyber_Neon:
                    s = isText ? AssetLoader.NeonGlowShader : AssetLoader.GlassCockpitShader;
                    break;
                case UiShaderStyle.Modern_Glass:
                default:
                    s = isText ? null : AssetLoader.GlassCockpitShader;
                    break;
            }

            if (s == null)
            {
                return null;
            }

            mat = new Material(s);
            if (mat.HasProperty("_DotSpacing")) mat.SetFloat("_DotSpacing", theme.UiDotSpacing);
            if (mat.HasProperty("_GlowStrength")) mat.SetFloat("_GlowStrength", theme.UiGlowStrength);
            if (mat.HasProperty("_BloomStrength")) mat.SetFloat("_BloomStrength", theme.UiGlowStrength);
            if (mat.HasProperty("_ScanlineStrength")) mat.SetFloat("_ScanlineStrength", theme.UiScanlineStrength);
            if (mat.HasProperty("_ScanlineDepth")) mat.SetFloat("_ScanlineDepth", theme.UiScanlineStrength);
            if (mat.HasProperty("_UnlitDotColor")) mat.SetColor("_UnlitDotColor", theme.UiGhostColor);
            if (mat.HasProperty("_LitDotColor")) mat.SetColor("_LitDotColor", theme.AccentPrimary);
            if (mat.HasProperty("_PhosphorColor")) mat.SetColor("_PhosphorColor", theme.AccentPrimary);
            if (mat.HasProperty("_CoreHotColor")) mat.SetColor("_CoreHotColor", Lighten((Color)theme.AccentPrimary, 0.40f));
            if (mat.HasProperty("_SegmentLitColor")) mat.SetColor("_SegmentLitColor", theme.AccentPrimary);
            if (mat.HasProperty("_GlassBgColor")) mat.SetColor("_GlassBgColor", theme.FrameBgColor);
            if (mat.HasProperty("_BackgroundColor")) mat.SetColor("_BackgroundColor", theme.FrameBgColor);
            if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", theme.FrameBorderColor);
            if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", theme.AccentPrimary);
            if (mat.HasProperty("_CornerColor")) mat.SetColor("_CornerColor", theme.AccentPrimary);
            if (mat.HasProperty("_GlowColor")) mat.SetColor("_GlowColor", theme.RimGlowColor);
            if (mat.HasProperty("_CornerChamfer")) mat.SetFloat("_CornerChamfer", theme.UiGlassChamfer);
            if (mat.HasProperty("_BorderWidth")) mat.SetFloat("_BorderWidth", theme.UiGlassBorderWidth);
            if (mat.HasProperty("_GlassGradientStrength")) mat.SetFloat("_GlassGradientStrength", theme.UiGlassGradientStrength);

            _materialCache[key] = mat;
            return mat;
        }

        #endregion

        #region Semantic Component Styling APIs

        /// <summary>
        /// 统一为卡片背景与边框挂载 Shader 材质并应用标准主题色调与透明度
        /// </summary>
        public void ApplyCardFrame(Image bg, Outline border, CardStyleRole role = CardStyleRole.Normal, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            Material panelMat = GetUiMaterial(isText: false);

            if (bg != null)
            {
                bg.material = panelMat;
                bg.color = GetCardBackgroundColor(role, theme);
            }

            if (border != null)
            {
                border.effectColor = GetCardBorderColor(role, theme);
            }
        }

        /// <summary>
        /// 统一为文字挂载文本专用 Shader 材质并应用标准排版颜色
        /// </summary>
        public void ApplyTextStyle(Text text, TextStyleRole role = TextStyleRole.PrimaryValue, ThemeConfig theme = null)
        {
            if (text == null) return;
            theme = ResolveTheme(theme);

            // 当主题属于点阵 (Dot_Matrix)、全息 (Phosphor_HUD)、数码管 (Digital_Segment) 或赛博霓虹时，文字统一挂载专属着色器，呈现纯正物理点阵或全息扫描线！
            bool isPixelOrHoloTheme = (theme.UiStyle == UiShaderStyle.Dot_Matrix ||
                                       theme.UiStyle == UiShaderStyle.Phosphor_HUD ||
                                       theme.UiStyle == UiShaderStyle.Digital_Segment ||
                                       theme.UiStyle == UiShaderStyle.Cyber_Neon);

            bool needsShader = isPixelOrHoloTheme || (role == TextStyleRole.PrimaryValue || role == TextStyleRole.SecondaryValue || role == TextStyleRole.Warning || role == TextStyleRole.Danger);
            if (needsShader)
            {
                text.material = GetUiMaterial(isText: true);
            }
            else
            {
                text.material = null;
            }

            text.color = GetTextColor(role, theme);
        }

        /// <summary>
        /// 统一为交互按钮应用规范的默认/按下/激活态视觉
        /// </summary>
        public void ApplyButtonStyle(Button btn, Image bg, Text label, ButtonVisualRole role = ButtonVisualRole.Normal, bool isPressed = false, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            Material panelMat = GetUiMaterial(isText: false);

            if (bg != null)
            {
                bg.material = panelMat;
                bg.color = GetButtonBackgroundColor(role, isPressed, theme);
            }

            if (label != null)
            {
                label.color = GetButtonTextColor(role, isPressed, theme);
            }
        }

        /// <summary>
        /// 统一为计量条/弧线/指针应用样式
        /// </summary>
        public void ApplyMeterStyle(Graphic track, Graphic fill, Graphic needle = null, MeterStyleRole role = MeterStyleRole.Primary, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);

            if (track != null)
            {
                track.color = GetMeterColor(MeterStyleRole.Track, theme);
            }
            if (fill != null)
            {
                fill.color = GetMeterColor(role, theme);
            }
            if (needle != null)
            {
                needle.color = theme.TextPrimaryColor;
            }
        }

        #endregion

        #region Semantic Colors Resolution (Theme-Driven, Zero Hidden Palette)

        /// <summary>内置默认样式策略：仅在所有主题都缺失 UiPolicy 时启用，数值与历史外观一致</summary>
        private static readonly UiStylePolicy DefaultPolicy = new UiStylePolicy();

        /// <summary>
        /// 解析有效主题：显式传入 > ThemeManager 当前主题 > 内置 ModernAero 预设。
        /// 严禁再使用内联字面量兜底调色板（那等于在主题系统之外私藏第二套配色）。
        /// </summary>
        public static ThemeConfig ResolveTheme(ThemeConfig theme)
        {
            if (theme != null) return theme;
            ThemeConfig current = ThemeManager.Instance?.CurrentTheme;
            return current ?? ThemeConfig.CreateModernAero();
        }

        /// <summary>解析有效样式策略（主题自带 > 内置默认）</summary>
        private static UiStylePolicy ResolvePolicy(ThemeConfig theme)
        {
            return theme != null && theme.UiPolicy != null ? theme.UiPolicy : DefaultPolicy;
        }

        /// <summary>叠加语义色：把 baseCol 按 amount 向 target 混合，并调整透明度</summary>
        private static Color Tint(Color baseCol, Color target, float amount, float alphaBoost = 0f, float? alphaOverride = null)
        {
            float a = alphaOverride ?? Mathf.Clamp01(baseCol.a + alphaBoost);
            return new Color(
                Mathf.Lerp(baseCol.r, target.r, amount),
                Mathf.Lerp(baseCol.g, target.g, amount),
                Mathf.Lerp(baseCol.b, target.b, amount),
                a);
        }

        /// <summary>按乘子压暗语义色（用于按下/激活态）</summary>
        private static Color Dim(Color semanticCol, float multiplier, float alpha)
        {
            return new Color(semanticCol.r * multiplier, semanticCol.g * multiplier, semanticCol.b * multiplier, alpha);
        }

        /// <summary>仅替换透明度，保留 RGB。公开给组件，替代 new Color(c.r, c.g, c.b, alpha) 派生色字面量。</summary>
        public static Color WithAlpha(Color col, float alpha)
        {
            return new Color(col.r, col.g, col.b, Mathf.Clamp01(alpha));
        }

        /// <summary>按比例向黑色压暗（保留透明度）。公开给组件，替代 Color.Lerp(c, Color.black, x)。</summary>
        public static Color Darken(Color col, float amount)
        {
            return Color.Lerp(col, Color.black, Mathf.Clamp01(amount));
        }

        /// <summary>按比例向白色提亮（保留透明度）。公开给组件，替代 Color.Lerp(c, Color.white, x)。</summary>
        public static Color Lighten(Color col, float amount)
        {
            return Color.Lerp(col, Color.white, Mathf.Clamp01(amount));
        }

        #region P4: Flat Array Theme Color Lookup Cache (纳秒级 O(1) 连续内存寻址)

        private readonly Color[] _cardBgTable = new Color[16];
        private readonly Color[] _cardBorderTable = new Color[16];
        private readonly Color[] _textTable = new Color[16];
        private readonly Color[] _meterTable = new Color[16];
        private readonly Color[] _buttonBgNormalTable = new Color[16];
        private readonly Color[] _buttonBgPressedTable = new Color[16];
        private readonly Color[] _buttonTextNormalTable = new Color[16];
        private readonly Color[] _buttonTextPressedTable = new Color[16];
        private ThemeConfig _lastBakedTheme = null;

        private void EnsurePaletteBaked(ThemeConfig theme)
        {
            if (theme == null) theme = ResolveTheme(null);
            if (_lastBakedTheme == theme && _textTable[(int)TextStyleRole.PrimaryValue] != Color.clear) return;
            _lastBakedTheme = theme;

            UiStylePolicy p = ResolvePolicy(theme);
            Color baseBg = theme.FrameBgColor;
            Color baseBorder = theme.FrameBorderColor;

            // 1. 卡片底色 (CardStyleRole)
            _cardBgTable[(int)CardStyleRole.Normal] = baseBg;
            _cardBgTable[(int)CardStyleRole.Emphasized] = Tint(baseBg, theme.AccentPrimary, p.CardEmphasizedTint, p.CardEmphasizedAlphaBoost);
            _cardBgTable[(int)CardStyleRole.Warning] = Tint(baseBg, theme.WarningColor, p.CardWarningTint, p.CardWarningAlphaBoost);
            _cardBgTable[(int)CardStyleRole.Danger] = Tint(baseBg, theme.DangerColor, p.CardDangerTint, p.CardDangerAlphaBoost);
            _cardBgTable[(int)CardStyleRole.InteractiveButton] = Tint(baseBg, Color.white, p.CardInteractiveLighten, 0f, p.ButtonBgAlpha);
            _cardBgTable[(int)CardStyleRole.ButtonPressed] = Dim(theme.AccentPrimary, p.CardPressedDim, p.ButtonPressedAlpha);
            _cardBgTable[(int)CardStyleRole.TransparentHUD] = Color.clear;
            _cardBgTable[(int)CardStyleRole.SubtleSlot] = WithAlpha(baseBg, baseBg.a * p.SubtleSlotAlphaScale);

            // 2. 卡片边框 (CardStyleRole)
            _cardBorderTable[(int)CardStyleRole.Normal] = baseBorder;
            _cardBorderTable[(int)CardStyleRole.Emphasized] = WithAlpha(theme.AccentSecondary, p.BorderEmphasizedAlpha);
            _cardBorderTable[(int)CardStyleRole.Warning] = WithAlpha(theme.WarningColor, p.BorderWarningAlpha);
            _cardBorderTable[(int)CardStyleRole.Danger] = WithAlpha(theme.DangerColor, p.BorderDangerAlpha);
            _cardBorderTable[(int)CardStyleRole.InteractiveButton] = WithAlpha(baseBorder, p.BorderInteractiveAlpha);
            _cardBorderTable[(int)CardStyleRole.ButtonPressed] = WithAlpha(theme.AccentPrimary, p.BorderPressedAlpha);
            _cardBorderTable[(int)CardStyleRole.TransparentHUD] = WithAlpha(baseBorder, p.BorderTransparentAlpha);
            _cardBorderTable[(int)CardStyleRole.SubtleSlot] = WithAlpha(baseBorder, p.BorderSubtleAlpha);

            // 3. 文本语义 (TextStyleRole)
            _textTable[(int)TextStyleRole.PrimaryValue] = theme.TextPrimaryColor;
            _textTable[(int)TextStyleRole.SecondaryValue] = theme.TextAccentColor;
            _textTable[(int)TextStyleRole.Label] = theme.TextAccentColor;
            _textTable[(int)TextStyleRole.Unit] = WithAlpha(theme.TextAccentColor, ((Color)theme.TextAccentColor).a * p.TextUnitAlphaScale);
            _textTable[(int)TextStyleRole.Warning] = theme.WarningColor;
            _textTable[(int)TextStyleRole.Danger] = theme.DangerColor;
            _textTable[(int)TextStyleRole.Accent] = theme.AccentPrimary;
            _textTable[(int)TextStyleRole.Muted] = WithAlpha(theme.TextAccentColor, ((Color)theme.TextAccentColor).a * p.TextMutedAlphaScale);
            _textTable[(int)TextStyleRole.Cardinal] = theme.AccentSecondary;
            _textTable[(int)TextStyleRole.InverseOnAccent] = theme.TextInverseColor;

            // 4. 仪表色彩 (MeterStyleRole)
            _meterTable[(int)MeterStyleRole.Primary] = theme.AccentPrimary;
            _meterTable[(int)MeterStyleRole.Secondary] = theme.AccentSecondary;
            _meterTable[(int)MeterStyleRole.Warning] = theme.WarningColor;
            _meterTable[(int)MeterStyleRole.Danger] = theme.DangerColor;
            _meterTable[(int)MeterStyleRole.Track] = theme.InactiveMeterColor;

            // 5. 按钮底色 (ButtonVisualRole)
            Color pri = theme.AccentPrimary;
            Color warn = theme.WarningColor;
            Color dang = theme.DangerColor;

            _buttonBgNormalTable[(int)ButtonVisualRole.Normal] = Tint(baseBg, Color.white, p.CardInteractiveLighten, 0f, p.ButtonBgAlpha);
            _buttonBgNormalTable[(int)ButtonVisualRole.Primary] = Tint(baseBg, Color.white, p.CardInteractiveLighten, 0f, p.ButtonBgAlpha);
            _buttonBgNormalTable[(int)ButtonVisualRole.ActiveToggle] = Dim(pri, p.ButtonActiveDim, p.ButtonBgAlpha);
            _buttonBgNormalTable[(int)ButtonVisualRole.Warning] = Tint(baseBg, warn, p.ButtonWarningTint, 0f, p.ButtonBgAlpha);
            _buttonBgNormalTable[(int)ButtonVisualRole.Danger] = Tint(baseBg, dang, p.ButtonDangerTint, 0f, p.ButtonBgAlpha);

            _buttonBgPressedTable[(int)ButtonVisualRole.Normal] = Tint(baseBg, Color.white, p.ButtonPlainPressedLighten, 0f, p.ButtonPressedAlpha);
            _buttonBgPressedTable[(int)ButtonVisualRole.Primary] = Dim(pri, p.ButtonPressedDim, p.ButtonPressedAlpha);
            _buttonBgPressedTable[(int)ButtonVisualRole.ActiveToggle] = Dim(pri, p.ButtonPressedDim, p.ButtonPressedAlpha);
            _buttonBgPressedTable[(int)ButtonVisualRole.Warning] = Dim(warn, p.ButtonPressedDim, p.ButtonPressedAlpha);
            _buttonBgPressedTable[(int)ButtonVisualRole.Danger] = Dim(dang, p.ButtonPressedDim, p.ButtonPressedAlpha);

            // 6. 按钮文字 (ButtonVisualRole)
            _buttonTextNormalTable[(int)ButtonVisualRole.Normal] = theme.TextPrimaryColor;
            _buttonTextNormalTable[(int)ButtonVisualRole.Primary] = theme.AccentPrimary;
            _buttonTextNormalTable[(int)ButtonVisualRole.ActiveToggle] = theme.AccentPrimary;
            _buttonTextNormalTable[(int)ButtonVisualRole.Warning] = theme.WarningColor;
            _buttonTextNormalTable[(int)ButtonVisualRole.Danger] = theme.DangerColor;

            _buttonTextPressedTable[(int)ButtonVisualRole.Normal] = theme.TextPrimaryColor;
            _buttonTextPressedTable[(int)ButtonVisualRole.Primary] = theme.TextPrimaryColor;
            _buttonTextPressedTable[(int)ButtonVisualRole.ActiveToggle] = theme.TextPrimaryColor;
            _buttonTextPressedTable[(int)ButtonVisualRole.Warning] = theme.TextPrimaryColor;
            _buttonTextPressedTable[(int)ButtonVisualRole.Danger] = theme.TextPrimaryColor;
        }

        #endregion

        public Color GetCardBackgroundColor(CardStyleRole role, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            if (idx >= 0 && idx < _cardBgTable.Length) return _cardBgTable[idx];
            return theme.FrameBgColor;
        }

        public Color GetCardBorderColor(CardStyleRole role, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            if (idx >= 0 && idx < _cardBorderTable.Length) return _cardBorderTable[idx];
            return theme.FrameBorderColor;
        }

        public Color GetTextColor(TextStyleRole role, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            if (idx >= 0 && idx < _textTable.Length) return _textTable[idx];
            return theme.TextPrimaryColor;
        }

        public Color GetButtonBackgroundColor(ButtonVisualRole role, bool isPressed, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            var tbl = isPressed ? _buttonBgPressedTable : _buttonBgNormalTable;
            if (idx >= 0 && idx < tbl.Length) return tbl[idx];
            return theme.FrameBgColor;
        }

        public Color GetButtonTextColor(ButtonVisualRole role, bool isPressed, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            var tbl = isPressed ? _buttonTextPressedTable : _buttonTextNormalTable;
            if (idx >= 0 && idx < tbl.Length) return tbl[idx];
            return theme.TextPrimaryColor;
        }

        public Color GetMeterColor(MeterStyleRole role, ThemeConfig theme)
        {
            theme = ResolveTheme(theme);
            EnsurePaletteBaked(theme);
            int idx = (int)role;
            if (idx >= 0 && idx < _meterTable.Length) return _meterTable[idx];
            return theme.AccentPrimary;
        }

        /// <summary>
        /// 表面材质语义色：底色由 FrameBgColor 派生，系数由 UiStylePolicy 给出。
        /// 组件内的所有面板/行/单元格/磁贴/LED 底色都必须走此接口。
        /// </summary>
        public Color GetSurfaceColor(SurfaceStyleRole role, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            UiStylePolicy p = ResolvePolicy(theme);

            switch (role)
            {
                case SurfaceStyleRole.Panel:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfacePanelTint), p.SurfacePanelAlpha);

                case SurfaceStyleRole.PanelDeep:
                    return WithAlpha(Darken(theme.FrameBgColor, p.SurfacePanelDeepDarken), p.SurfacePanelDeepAlpha);

                case SurfaceStyleRole.Slot:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfaceSlotTint), p.SurfaceSlotAlpha);

                case SurfaceStyleRole.SlotActive:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfaceSlotActiveTint), p.SurfaceSlotActiveAlpha);

                case SurfaceStyleRole.Control:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfaceControlTint), p.SurfaceControlAlpha);

                case SurfaceStyleRole.Inset:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfaceInsetTint), p.SurfaceInsetAlpha);

                case SurfaceStyleRole.Tile:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.AccentSecondary, p.SurfaceTileTint), p.SurfaceTileAlpha);

                case SurfaceStyleRole.LedOff:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.TextAccentColor, p.SurfaceLedOffTint), p.SurfaceLedOffAlpha);

                case SurfaceStyleRole.LedStandby:
                    return WithAlpha(Tint(theme.FrameBgColor, theme.TextAccentColor, p.SurfaceLedStandbyTint), p.SurfaceLedStandbyAlpha);

                default:
                    return theme.FrameBgColor;
            }
        }

        /// <summary>
        /// 状态徽标底语义色（正常/注意/危险/未激活）：由主题语义色压暗而成，替代组件内的
        /// new Color(0.25f, 0.06f, 0.06f, 0.95f) 之类"私有状态底色"。
        /// </summary>
        public Color GetStatusSurfaceColor(StatusSurfaceRole role, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            UiStylePolicy p = ResolvePolicy(theme);

            switch (role)
            {
                case StatusSurfaceRole.Success:
                    return Dim(theme.AccentPrimary, p.StatusSurfaceSuccessDim, p.StatusSurfaceAlpha);

                case StatusSurfaceRole.Caution:
                    return Dim(theme.WarningColor, p.StatusSurfaceCautionDim, p.StatusSurfaceAlpha);

                case StatusSurfaceRole.Danger:
                    return Dim(theme.DangerColor, p.StatusSurfaceDangerDim, p.StatusSurfaceAlpha);

                case StatusSurfaceRole.Inactive:
                default:
                    return WithAlpha(Darken(theme.FrameBgColor, p.SurfacePanelDeepDarken), p.StatusSurfaceInactiveAlpha);
            }
        }

        /// <summary>
        /// 状态面板底语义色：在标准面板底上叠一层语义色（用于连通/告警状态面板）。
        /// </summary>
        public Color GetStatusPanelColor(StatusSurfaceRole role, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            UiStylePolicy p = ResolvePolicy(theme);
            Color panel = GetSurfaceColor(SurfaceStyleRole.Panel, theme);

            switch (role)
            {
                case StatusSurfaceRole.Success:
                    return WithAlpha(Tint(panel, theme.AccentPrimary, p.StatusPanelTint), p.StatusPanelAlpha);

                case StatusSurfaceRole.Caution:
                    return WithAlpha(Tint(panel, theme.WarningColor, p.StatusPanelTint), p.StatusPanelAlpha);

                case StatusSurfaceRole.Danger:
                    return WithAlpha(Tint(panel, theme.DangerColor, p.StatusPanelTint), p.StatusPanelAlpha);

                case StatusSurfaceRole.Inactive:
                default:
                    return panel;
            }
        }

        /// <summary>
        /// 线条/描边语义色：给定基色 + 视觉权重档位（透明度来自 UiStylePolicy）。
        /// 替代 new Color(c.r, c.g, c.b, 0.45f) 这类魔法透明度字面量。
        /// </summary>
        public Color GetLineColor(Color baseCol, LineWeight weight, ThemeConfig theme = null)
        {
            return WithAlpha(baseCol, GetLineAlpha(weight, theme));
        }

        public Color GetLineColor(LineWeight weight, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            return WithAlpha(theme.AccentSecondary, GetLineAlpha(weight, theme));
        }

        /// <summary>取某权重档位的透明度（供需要自行混合的场合使用）</summary>
        public float GetLineAlpha(LineWeight weight, ThemeConfig theme = null)
        {
            UiStylePolicy p = ResolvePolicy(ResolveTheme(theme));

            switch (weight)
            {
                case LineWeight.Ghost: return p.LineAlphaGhost;
                case LineWeight.Faint: return p.LineAlphaFaint;
                case LineWeight.Subtle: return p.LineAlphaSubtle;
                case LineWeight.Light: return p.LineAlphaLight;
                case LineWeight.Normal: return p.LineAlphaNormal;
                case LineWeight.Strong: return p.LineAlphaStrong;
                case LineWeight.Bold: return p.LineAlphaBold;
                case LineWeight.Heavy: return p.LineAlphaHeavy;
                case LineWeight.Solid: return p.LineAlphaSolid;
                default: return p.LineAlphaNormal;
            }
        }

        /// <summary>
        /// 无量纲白：用于"无着色"语义场景——程序化生成的剪影/遮罩贴图像素（亮度通道，由渲染层着色）、
        /// 需保留原始质感的模组图标直通。它表达的是几何/亮度，不构成调色板颜色，故集中定义于此。
        /// </summary>
        public static readonly Color NeutralOpaque = Color.white;

        /// <summary>无量纲透明：遮罩贴图的空像素（等效 Color.clear，集中定义便于语义化书写）</summary>
        public static readonly Color NeutralTransparent = Color.clear;

        /// <summary>
        /// 姿态球参考系调色板：以身份色为基准做明度分层派生。
        /// 惯性 / 质心 / 目标 / 机体 四个抽象参考系各自只提供身份色（主题语义色），
        /// 层次与透明度全部由 UiStylePolicy 系数决定。
        /// </summary>
        public NavballFramePalette GetNavballFramePalette(Color identityColor, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            UiStylePolicy p = ResolvePolicy(theme);

            return new NavballFramePalette
            {
                SkyZenith = Darken(identityColor, p.FrameSkyZenithDarken),
                SkyHorizon = Darken(identityColor, p.FrameSkyHorizonDarken),
                GroundHorizon = Darken(identityColor, p.FrameGroundHorizonDarken),
                GroundNadir = Darken(identityColor, p.FrameGroundNadirDarken),
                Equator = Lighten(identityColor, p.FrameEquatorLighten),
                PitchLadder = GetLineColor(Lighten(identityColor, p.FramePitchLighten), LineWeight.Heavy, theme),
                HeadingLine = GetLineColor(identityColor, LineWeight.Strong, theme),
                Rim = identityColor
            };
        }

        /// <summary>参考系调色板插值（参考系切换时的平滑过渡，组件内不得自行展开逐通道 Lerp）</summary>
        public static NavballFramePalette LerpFramePalette(NavballFramePalette from, NavballFramePalette to, float t)
        {
            return new NavballFramePalette
            {
                SkyZenith = Color.Lerp(from.SkyZenith, to.SkyZenith, t),
                SkyHorizon = Color.Lerp(from.SkyHorizon, to.SkyHorizon, t),
                GroundHorizon = Color.Lerp(from.GroundHorizon, to.GroundHorizon, t),
                GroundNadir = Color.Lerp(from.GroundNadir, to.GroundNadir, t),
                Equator = Color.Lerp(from.Equator, to.Equator, t),
                PitchLadder = Color.Lerp(from.PitchLadder, to.PitchLadder, t),
                HeadingLine = Color.Lerp(from.HeadingLine, to.HeadingLine, t),
                Rim = Color.Lerp(from.Rim, to.Rim, t)
            };
        }

        /// <summary>
        /// 获取航向罗盘方位语义颜色 (0°/90°/180°/270°)
        /// 彻底杜绝组件内部写死 new Color(1f, 0.28f, ...)
        /// </summary>
        public Color GetCardinalColor(int degree, ThemeConfig theme = null)
        {
            theme = ResolveTheme(theme);
            int norm = (degree % 360 + 360) % 360;

            switch (norm)
            {
                case 0:   // N (北) -> 航电副强调色
                    return theme.AccentSecondary;

                case 90:  // E (东) -> 纯净高对比白
                case 270: // W (西) -> 纯净高对比白
                    return theme.TextPrimaryColor;

                case 180: // S (南) -> 告警珊瑚红 / 警示色
                    return theme.DangerColor;

                default:
                    return theme.TextAccentColor;
            }
        }

        /// <summary>
        /// 便捷工具：安全生成带指定透明度的派生颜色
        /// </summary>
        public static Color GetDerivedColor(Color baseCol, float alphaMultiplier)
        {
            return new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(baseCol.a * alphaMultiplier));
        }

        #endregion

        #region Static Shortcuts (主题内部解析，组件书写首选形式)

        // 说明：以下静态入口的 theme 省略时按 ThemeManager 当前主题解析。
        // ThemeManager 在触发 OnThemeChanged 之前已完成 CurrentTheme 赋值，
        // 因此即使在 ApplyTheme(newTheme) 内部调用也一定取到新主题，不存在旧主题滞后问题。

        /// <summary>表面材质语义色（面板/行/单元格/磁贴/LED 底色）</summary>
        public static Color Surface(SurfaceStyleRole role, ThemeConfig theme = null)
            => Instance.GetSurfaceColor(role, theme);

        /// <summary>状态徽标底语义色</summary>
        public static Color StatusSurface(StatusSurfaceRole role, ThemeConfig theme = null)
            => Instance.GetStatusSurfaceColor(role, theme);

        /// <summary>状态面板底语义色</summary>
        public static Color StatusPanel(StatusSurfaceRole role, ThemeConfig theme = null)
            => Instance.GetStatusPanelColor(role, theme);

        /// <summary>线条/描边语义色（基色 + 视觉权重档位）</summary>
        public static Color Line(Color baseCol, LineWeight weight, ThemeConfig theme = null)
            => Instance.GetLineColor(baseCol, weight, theme);

        /// <summary>某权重档位的透明度</summary>
        public static float Alpha(LineWeight weight, ThemeConfig theme = null)
            => Instance.GetLineAlpha(weight, theme);

        /// <summary>文字语义色</summary>
        public static Color Text(TextStyleRole role, ThemeConfig theme = null)
            => Instance.GetTextColor(role, theme);

        /// <summary>计量条语义色</summary>
        public static Color Meter(MeterStyleRole role, ThemeConfig theme = null)
            => Instance.GetMeterColor(role, theme);

        /// <summary>按钮底色语义色</summary>
        public static Color Button(ButtonVisualRole role, bool isPressed, ThemeConfig theme = null)
            => Instance.GetButtonBackgroundColor(role, isPressed, theme);

        /// <summary>卡片底色语义色</summary>
        public static Color CardBg(CardStyleRole role, ThemeConfig theme = null)
            => Instance.GetCardBackgroundColor(role, theme);

        /// <summary>卡片边框语义色</summary>
        public static Color CardBorder(CardStyleRole role, ThemeConfig theme = null)
            => Instance.GetCardBorderColor(role, theme);

        /// <summary>
        /// 装饰环 / 圆弧导轨语义色（取自主题副强调色）：激光环、罗盘环、表盘外圈等结构统一走此处。
        /// </summary>
        public static Color Ring(LineWeight weight, ThemeConfig theme = null)
            => Line(ResolveTheme(theme).AccentSecondary, weight, theme);

        /// <summary>
        /// 语义色 + 视觉权重档位：把"主题语义色按某个线条权重呈现"这一高频组合收成一次调用。
        /// </summary>
        public static Color Tinted(TextStyleRole role, LineWeight weight, ThemeConfig theme = null)
            => Line(Text(role, theme), weight, theme);

        /// <summary>
        /// 把任意基色按视觉权重档位赋予透明度，替代散落各处的 WithAlpha(x, 0.35f) 魔法数字。
        /// </summary>
        public static Color Weighted(Color baseCol, LineWeight weight, ThemeConfig theme = null)
            => WithAlpha(baseCol, Alpha(weight, theme));

        #endregion
    }
}
