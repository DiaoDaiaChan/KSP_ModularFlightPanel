using System;
using UnityEngine;

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
        Texture = 0,    // 贴图模式 (使用官方/TextureReplacer原版素材 + 增强高动态 Shader)
        Procedural = 1  // 程序化生成模式 (现代航电超清无极矢量解算)
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

    [Serializable]
    public class ThemeConfig
    {
        public string ThemeId = "modern_aero";
        public string DisplayName = "Modern Glass Cockpit (极简全息)";
        public NavballStyleType Style = NavballStyleType.Modern_Aero;
        public NavballRenderMode RenderMode = NavballRenderMode.Texture;

        // 着色器配置
        public string ShaderName = "ModularFlightPanel/NavballModern";
        public bool EnableHalftoneDither = false;
        public float DotDensity = 38f;
        public float DotMinRadius = 0.04f;
        public float DotMaxRadius = 0.46f;
        public bool EnableScanlines = false;

        // 姿态球色彩
        public ColorHex SkyColor = ColorHex.FromColor(new Color(0.06f, 0.20f, 0.40f, 1.0f));
        public ColorHex GroundColor = ColorHex.FromColor(new Color(0.12f, 0.12f, 0.12f, 1.0f));
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
        public ColorHex DangerColor = ColorHex.FromColor(new Color(1.0f, 0.28f, 0.34f, 1.0f));      // 警报珊瑚红 (#FF4757)

        // 仪表背景与边框
        public ColorHex FrameBgColor = ColorHex.FromColor(new Color(0.04f, 0.06f, 0.09f, 0.80f));   // 半透深石墨航电座舱玻璃
        public ColorHex FrameBorderColor = ColorHex.FromColor(new Color(0.35f, 0.65f, 0.95f, 0.28f)); // 细腻极细冰蓝边框
        public ColorHex InactiveMeterColor = ColorHex.FromColor(new Color(0.12f, 0.18f, 0.25f, 0.45f));

        // 文本色彩
        public ColorHex TextPrimaryColor = ColorHex.FromColor(new Color(0.94f, 0.96f, 0.99f, 1.0f));  // 纯净高对比白
        public ColorHex TextAccentColor = ColorHex.FromColor(new Color(0.55f, 0.65f, 0.78f, 1.0f));   // 技术钛钢灰

        // 默认预设生成器
        public static ThemeConfig CreateCyberNeon()
        {
            return new ThemeConfig
            {
                ThemeId = "cyber_neon",
                DisplayName = "Cyber Neon (赛博点阵)",
                Style = NavballStyleType.Cyber_Neon,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = true,
                EnableScanlines = true
            };
        }

        public static ThemeConfig CreateModernAero()
        {
            return new ThemeConfig
            {
                ThemeId = "modern_aero",
                DisplayName = "Modern Glass Cockpit (极简全息)",
                Style = NavballStyleType.Modern_Aero,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.06f, 0.20f, 0.40f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.12f, 0.12f, 0.12f, 1.0f)),
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
                TextPrimaryColor = ColorHex.FromColor(new Color(0.94f, 0.96f, 0.99f, 1.0f)),  // 纯净高对比白
                TextAccentColor = ColorHex.FromColor(new Color(0.55f, 0.65f, 0.78f, 1.0f))   // 技术钛钢灰
            };
        }

        public static ThemeConfig CreateClassicAero()
        {
            return new ThemeConfig
            {
                ThemeId = "classic_aero",
                DisplayName = "Classic Aero (经典蓝棕)",
                Style = NavballStyleType.Classic_Aero,
                ShaderName = "ModularFlightPanel/NavballHalftone",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.12f, 0.38f, 0.72f, 1.0f)),     // 经典深蓝
                GroundColor = ColorHex.FromColor(new Color(0.48f, 0.28f, 0.12f, 1.0f)),  // 经典棕褐
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
                DisplayName = "Apollo 1969 AGC (阿波罗复古)",
                Style = NavballStyleType.Apollo_1969,
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
    }
}
