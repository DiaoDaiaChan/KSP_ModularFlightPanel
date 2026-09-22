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
        public string ThemeId = "cyber_neon";
        public string DisplayName = "Cyber Neon (赛博点阵)";
        public NavballStyleType Style = NavballStyleType.Cyber_Neon;
        public NavballRenderMode RenderMode = NavballRenderMode.Texture;

        // 着色器配置
        public string ShaderName = "ModularFlightPanel/NavballHalftone";
        public bool EnableHalftoneDither = true;
        public float DotDensity = 38f;
        public float DotMinRadius = 0.04f;
        public float DotMaxRadius = 0.46f;
        public bool EnableScanlines = true;

        // 姿态球色彩
        public ColorHex SkyColor = ColorHex.FromColor(new Color(0.0f, 0.88f, 0.85f, 1.0f));
        public ColorHex GroundColor = ColorHex.FromColor(new Color(0.0f, 0.32f, 0.48f, 1.0f));
        public ColorHex HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 0.25f, 0.25f, 1.0f));
        public ColorHex GridColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.85f, 0.7f));
        public ColorHex DitherDotColor = ColorHex.FromColor(new Color(0.0f, 0.12f, 0.22f, 0.85f));
        public ColorHex RimGlowColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.9f, 0.5f));

        // UI 核心色标
        public ColorHex AccentPrimary = ColorHex.FromColor(new Color(0.14f, 1.0f, 0.0f, 1.0f));    // 亮绿 (油门/正常)
        public ColorHex AccentSecondary = ColorHex.FromColor(new Color(0.0f, 0.95f, 1.0f, 1.0f));  // 亮青 (主要线条)
        public ColorHex AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.08f, 0.58f, 1.0f));  // 洋红 (高度框/警示)
        public ColorHex WarningColor = ColorHex.FromColor(new Color(1.0f, 0.75f, 0.0f, 1.0f));   // 亮橙黄
        public ColorHex DangerColor = ColorHex.FromColor(new Color(1.0f, 0.2f, 0.2f, 1.0f));      // 警报红

        // 仪表背景与边框
        public ColorHex FrameBgColor = ColorHex.FromColor(new Color(0.02f, 0.04f, 0.07f, 0.88f));
        public ColorHex FrameBorderColor = ColorHex.FromColor(new Color(0.0f, 0.85f, 0.95f, 0.85f));
        public ColorHex InactiveMeterColor = ColorHex.FromColor(new Color(0.05f, 0.15f, 0.12f, 0.65f));

        // 文本色彩
        public ColorHex TextPrimaryColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 1.0f));
        public ColorHex TextAccentColor = ColorHex.FromColor(new Color(0.0f, 1.0f, 0.85f, 1.0f));

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
                DisplayName = "Modern Glass Cockpit (现代航电)",
                Style = NavballStyleType.Modern_Aero,
                ShaderName = "ModularFlightPanel/NavballModern",
                EnableHalftoneDither = false,
                EnableScanlines = false,
                SkyColor = ColorHex.FromColor(new Color(0.06f, 0.25f, 0.52f, 1.0f)),
                GroundColor = ColorHex.FromColor(new Color(0.24f, 0.18f, 0.14f, 1.0f)),
                HorizonLineColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 1.0f)),
                GridColor = ColorHex.FromColor(new Color(0.85f, 0.95f, 1.0f, 0.8f)),
                RimGlowColor = ColorHex.FromColor(new Color(0.3f, 0.75f, 1.0f, 0.4f)),
                AccentPrimary = ColorHex.FromColor(new Color(0.0f, 0.9f, 0.42f, 1.0f)),
                AccentSecondary = ColorHex.FromColor(new Color(0.44f, 0.56f, 1.0f, 1.0f)),
                AccentMagenta = ColorHex.FromColor(new Color(1.0f, 0.24f, 0.74f, 1.0f)),
                WarningColor = ColorHex.FromColor(new Color(1.0f, 0.82f, 0.1f, 1.0f)),
                DangerColor = ColorHex.FromColor(new Color(1.0f, 0.18f, 0.3f, 1.0f)),
                FrameBgColor = ColorHex.FromColor(new Color(0.008f, 0.012f, 0.04f, 0.94f)),
                FrameBorderColor = ColorHex.FromColor(new Color(0.24f, 0.31f, 0.9f, 0.9f)),
                InactiveMeterColor = ColorHex.FromColor(new Color(0.055f, 0.07f, 0.18f, 0.8f)),
                TextPrimaryColor = ColorHex.FromColor(new Color(1.0f, 1.0f, 1.0f, 1.0f)),
                TextAccentColor = ColorHex.FromColor(new Color(0.43f, 0.7f, 1.0f, 1.0f))
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
