using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core.Rendering
{
    /// <summary>
    /// SAS 模式选择罗盘静态程序化视觉资产生成器 (SAS Dial Visual Asset Generator)
    /// 解耦自 SASDialWidget，负责离线/单次程序化纹理烘焙 (零运行时 GC 与显存重复分配，MFP-SPEC-002)。
    /// </summary>
    public static class SASDialVisualGenerator
    {
        private static Texture2D _circularDialTexture;
        private static Texture2D _spacecraftTexture;
        private static Texture2D _boresightPointerTexture;
        private static Texture2D _attitudeHorizonTexture;
        private static Texture2D _flightDirectorChevronTexture;

        public static Texture2D GetOrCreateDialTexture(ThemeConfig theme = null, bool forceRegenerate = false)
        {
            if (_circularDialTexture == null || forceRegenerate)
            {
                _circularDialTexture = GenerateCircularDialTexture(theme);
            }
            return _circularDialTexture;
        }

        public static Texture2D GetOrCreateSpacecraftTexture()
        {
            if (_spacecraftTexture == null)
            {
                _spacecraftTexture = CreateProceduralSpacecraftTexture();
            }
            return _spacecraftTexture;
        }

        public static Texture2D GetOrCreateBoresightPointerTexture()
        {
            if (_boresightPointerTexture == null)
            {
                _boresightPointerTexture = CreateBoresightPointerTexture();
            }
            return _boresightPointerTexture;
        }

        public static Texture2D GetOrCreateAttitudeHorizonTexture()
        {
            if (_attitudeHorizonTexture == null)
            {
                _attitudeHorizonTexture = CreateAttitudeHorizonTexture();
            }
            return _attitudeHorizonTexture;
        }

        public static Texture2D GetOrCreateFlightDirectorChevronTexture()
        {
            if (_flightDirectorChevronTexture == null)
            {
                _flightDirectorChevronTexture = CreateFlightDirectorChevronTexture();
            }
            return _flightDirectorChevronTexture;
        }

        /// <summary>
        /// 程序化生成高精度 512x512 环形航电背板纹理 (三线性 Mipmap 滤波、次像素抗锯齿外环、激光细环、姿态基准十字与主方位标线)
        /// </summary>
        public static Texture2D GenerateCircularDialTexture(ThemeConfig theme = null)
        {
            const int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;

            Color glassDark = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            Color rimColor = WidgetStyleManager.Line(
                WidgetStyleManager.Darken(WidgetStyleManager.CardBorder(CardStyleRole.Normal, theme), 0.25f), LineWeight.Heavy, theme);
            Color ringLaser = WidgetStyleManager.Ring(LineWeight.Normal, theme);
            Color tickColor = WidgetStyleManager.Ring(LineWeight.Bold, theme);
            Color reticleColor = WidgetStyleManager.Ring(LineWeight.Subtle, theme);

            float invHalf = 1f / half;
            float feather = 2.5f * invHalf; // 2.5像素平滑抗锯齿边缘

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    if (r > 0.995f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 外边缘次像素平滑羽化
                    float edgeAlpha = Mathf.Clamp01((0.995f - r) / feather);

                    Color pixel = glassDark;

                    // 1. 外环金属轮廓 (0.91 .. 0.96) 平滑过渡
                    float rimIn = Mathf.Clamp01((r - 0.905f) / feather);
                    float rimOut = Mathf.Clamp01((0.965f - r) / feather);
                    float rimFactor = Mathf.Min(rimIn, rimOut);
                    if (rimFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, rimColor, 0.75f * rimFactor);
                    }

                    // 2. 内同心激光细环 (0.58 .. 0.60) 平滑抗锯齿
                    float laserIn = Mathf.Clamp01((r - 0.575f) / feather);
                    float laserOut = Mathf.Clamp01((0.605f - r) / feather);
                    float laserFactor = Mathf.Min(laserIn, laserOut);
                    if (laserFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, ringLaser, 0.75f * laserFactor);
                    }

                    // 3. 姿态参考环 (0.380 .. 0.395) 幽暗视口刻线 (Attitude Horizon Ring)
                    float reticleIn = Mathf.Clamp01((r - 0.380f) / feather);
                    float reticleOut = Mathf.Clamp01((0.395f - r) / feather);
                    float reticleFactor = Mathf.Min(reticleIn, reticleOut);
                    if (reticleFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, reticleColor, 0.45f * reticleFactor);
                    }

                    // 4. 水平地平基准刻线 (Horizontal Horizon Reference Marks, 9点与3点方向, r: 0.45 .. 0.58)
                    if (r >= 0.45f && r <= 0.58f)
                    {
                        float horizY = Mathf.Clamp01((0.007f - Mathf.Abs(dy)) / feather);
                        if (horizY > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.75f * horizY);
                        }
                    }

                    // 5. 12 点钟与 6 点钟纵向基准标线 (Vertical Datum Ticks, r: 0.52 .. 0.58)
                    if (r >= 0.52f && r <= 0.58f)
                    {
                        float vertX = Mathf.Clamp01((0.007f - Mathf.Abs(dx)) / feather);
                        if (vertX > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.75f * vertX);
                        }
                    }

                    // 6. 45° 滚转微刻度点 (Roll Index Ticks: 45°, 135°, 225°, 315°, r: 0.53 .. 0.58)
                    if (r >= 0.53f && r <= 0.58f)
                    {
                        float diagDist = Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) * 0.7071f;
                        float diagFactor = Mathf.Clamp01((0.007f - diagDist) / feather);
                        if (diagFactor > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.65f * diagFactor);
                        }
                    }

                    // 7. 4 主方位微刻度 (0°, 90°, 180°, 270°) 平滑抗锯齿
                    if (r >= 0.83f && r <= 0.91f)
                    {
                        float tickX = Mathf.Clamp01((0.012f - Mathf.Abs(dx)) / feather);
                        float tickY = Mathf.Clamp01((0.012f - Mathf.Abs(dy)) / feather);
                        float tickFactor = Mathf.Max(tickX, tickY);
                        if (tickFactor > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, tickColor, 0.85f * tickFactor);
                        }
                    }

                    pixel.a *= edgeAlpha;
                    cols[y * size + x] = pixel;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 256x256 高精度全矢量双三角翼航电飞船剪影 (Precision Double-Delta Spacecraft Silhouette)
        /// 具备屏幕自适应次像素抗锯齿 (SDF Calibrated Anti-Aliasing)，在 48px 表盘显示下实现 1.15 物理像素的平滑过渡；
        /// 彻底消除低分辨率阶梯走样与锯齿，提供高反差外轮廓光辉、座舱盖航电玻璃反光条、中央背脊线与双发推进喷口。
        /// </summary>
        public static Texture2D CreateProceduralSpacecraftTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;

            // 针对 48px 标准表盘显示尺寸校准边缘次像素羽化宽度 (~1.15 物理屏幕像素，彻底终结锯齿与虚化)
            const float targetDisplaySize = 48f;
            float feather = 1.15f * (size / targetDisplaySize) * invHalf;

            for (int y = 0; y < size; y++)
            {
                // ny: -1.0 (尾部喷口) .. +1.0 (机头鼻锥)
                float ny = (y - half) * invHalf;
                int rowOffset = y * size;

                for (int x = 0; x < size; x++)
                {
                    // nx: -1.0 (左翼尖) .. +1.0 (右翼尖)
                    float nx = (x - half) * invHalf;
                    float adx = Mathf.Abs(nx);

                    // 1. 几何拓扑定义 (Double-Delta Aerospace Interceptor)
                    float bodyW = 0f;
                    float wingW = 0f;
                    float nozzleDist = 0f;
                    bool inNozzle = false;

                    // A. 机头雷达罩与双曲边条 (ny: 0.35 .. 0.88)
                    if (ny >= 0.35f && ny <= 0.88f)
                    {
                        float t = (0.88f - ny) / 0.53f; // 0 at nose, 1 at chine base
                        bodyW = 0.035f + 0.165f * Mathf.Pow(t, 0.85f);
                    }
                    else if (ny >= -0.65f && ny < 0.35f)
                    {
                        bodyW = 0.20f; // 核心机身中段
                    }

                    // B. 双三角后掠主机翼 (ny: -0.55 .. 0.35)
                    if (ny >= -0.55f && ny <= 0.35f)
                    {
                        if (ny >= -0.38f)
                        {
                            // 前缘后掠段 (0.35 .. -0.38)
                            float wt = (0.35f - ny) / 0.73f;
                            wingW = 0.20f + 0.54f * Mathf.Pow(wt, 0.88f);
                        }
                        else
                        {
                            // 机翼后缘前掠切角与升降副翼内收 (-0.55 .. -0.38)
                            float wt = (ny - (-0.55f)) / 0.17f;
                            wingW = 0.20f + 0.54f * Mathf.Pow(wt, 0.55f);
                        }
                    }

                    // C. 翼尖防颤滑轨 / RCS 姿态喷口滑块 (ny: -0.46 .. -0.30, adx: 0.71 .. 0.76)
                    bool tipRail = (ny >= -0.46f && ny <= -0.30f && adx >= 0.71f && adx <= 0.76f);

                    // D. 双发尾喷管外廓 (ny: -0.76 .. -0.60)
                    float nozzleCenter = 0.105f;
                    nozzleDist = Mathf.Abs(adx - nozzleCenter);
                    if (ny >= -0.76f && ny <= -0.60f && nozzleDist <= 0.062f)
                    {
                        inNozzle = true;
                    }

                    float hullW = Mathf.Max(bodyW, wingW);
                    if (tipRail) hullW = Mathf.Max(hullW, 0.76f);

                    // 2. 次像素反走样因子解算 (SDF Coverage)
                    float alpha = 0f;
                    if (hullW > 0.001f)
                    {
                        alpha = Mathf.Clamp01((hullW - adx) / feather);
                    }

                    // 机头尖端平滑裁切
                    if (ny > 0.86f)
                    {
                        alpha = Mathf.Min(alpha, Mathf.Clamp01((0.88f - ny) / feather));
                    }

                    // 尾部平滑裁切
                    if (!inNozzle)
                    {
                        if (ny < -0.55f)
                        {
                            alpha = Mathf.Min(alpha, Mathf.Clamp01((ny - (-0.58f)) / feather));
                        }
                    }
                    else
                    {
                        float nAlpha = Mathf.Clamp01((0.062f - nozzleDist) / feather);
                        float nYAlpha = Mathf.Clamp01((ny - (-0.76f)) / feather);
                        alpha = Mathf.Max(alpha, Mathf.Min(nAlpha, nYAlpha));
                    }

                    if (alpha <= 0.001f)
                    {
                        cols[rowOffset + x] = Color.clear;
                        continue;
                    }

                    // 3. 几何分层与高对比度结构光线 (High-Contrast Avionics Detailing)
                    float lum = 0.52f; // 基准蒙皮亮度

                    // A. 外轮廓矢量强化描边 (1.4 物理屏幕像素高亮边缘，大幅强化微型表盘视认度)
                    float distToRim = hullW - adx;
                    float rimWidth = 1.4f * feather;
                    if (distToRim >= 0f && distToRim <= rimWidth)
                    {
                        float rf = 1f - (distToRim / rimWidth);
                        lum = Mathf.Max(lum, 0.52f + 0.48f * rf);
                    }

                    // B. 座舱盖高反差航电深色玻璃与高光反射条 (ny: 0.38 .. 0.70)
                    bool isCanopy = false;
                    if (ny >= 0.38f && ny <= 0.70f)
                    {
                        float ct = (0.70f - ny) / 0.32f;
                        float cw = 0.068f * Mathf.Pow(Mathf.Sin(ct * Mathf.PI), 0.8f);
                        if (adx <= cw)
                        {
                            isCanopy = true;
                            float cdist = cw - adx;
                            if (cdist <= 1.1f * feather)
                            {
                                lum = 1.0f; // 座舱框架亮线
                            }
                            else
                            {
                                if (nx >= -0.04f && nx <= -0.01f) lum = 0.88f; // 左前侧高光反射 (Glint)
                                else lum = 0.18f; // 深邃航电玻璃底色
                            }
                        }
                    }

                    // C. 飞船中心脊背高光线 (Dorsal Spine Ridge)
                    if (!isCanopy && adx <= 0.75f * feather && ny >= -0.50f && ny <= 0.84f)
                    {
                        lum = 0.95f;
                    }

                    // D. 机翼边条折痕阴影线
                    if (!isCanopy && adx >= 0.19f && adx <= hullW)
                    {
                        if (Mathf.Abs(adx - 0.20f) <= 0.6f * feather)
                        {
                            lum = 0.28f;
                        }
                    }

                    // E. 尾喷管喉部内腔发光环
                    if (inNozzle && ny <= -0.68f)
                    {
                        lum = 0.95f;
                    }

                    // 4. 语义着色映射 (100% 遵照 MFP-SPEC-006 零颜色字面量，纯由 NeutralOpaque 衍生)
                    Color baseCol = WidgetStyleManager.NeutralOpaque;
                    if (lum < 0.999f)
                    {
                        baseCol = WidgetStyleManager.Darken(baseCol, 1.0f - lum);
                    }
                    else if (lum > 1.001f)
                    {
                        baseCol = WidgetStyleManager.Lighten(baseCol, (lum - 1.0f) * 0.5f);
                    }
                    cols[rowOffset + x] = WidgetStyleManager.WithAlpha(baseCol, alpha * 0.98f);
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 64x64 高精度矢量机头朝向标 (Precision Boresight Chevron Pointer)
        /// 带有次像素平滑抗锯齿的前视光标倒 V 导引箭头，替代粗糙的旋转正方形。
        /// </summary>
        public static Texture2D CreateBoresightPointerTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.5f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1 (顶部是 +1, 底部是 -1)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float dx = Mathf.Abs(nx);

                    // 倒 V 航电指引前向箭头 (Apex 顶点位于 ny = 0.75, dx = 0)
                    // 外缘斜率: ny_outer = 0.75 - 1.45 * dx
                    // 内缘斜率: ny_inner = 0.32 - 1.35 * dx
                    float outerY = 0.75f - 1.45f * dx;
                    float innerY = 0.32f - 1.35f * dx;

                    float distTop = outerY - ny;
                    float distBot = ny - innerY;
                    float distSide = 0.70f - dx;

                    float alphaTop = Mathf.Clamp01(distTop / feather);
                    float alphaBot = Mathf.Clamp01(distBot / feather);
                    float alphaSide = Mathf.Clamp01(distSide / feather);
                    float alphaBottomCut = Mathf.Clamp01((ny - (-0.45f)) / feather);

                    float inChevronAlpha = Mathf.Min(Mathf.Min(alphaTop, alphaBot), Mathf.Min(alphaSide, alphaBottomCut));

                    if (inChevronAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 边框高亮，内部柔和
                    float edgeDist = Mathf.Min(Mathf.Min(distTop, distBot), distSide);
                    float lum = (edgeDist <= 0.06f) ? 1.0f : 0.80f;

                    Color baseCol = WidgetStyleManager.NeutralOpaque;
                    if (lum < 0.999f)
                    {
                        baseCol = WidgetStyleManager.Darken(baseCol, 1.0f - lum);
                    }
                    cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, inChevronAlpha);
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 256x256 高精度 3D 人造地平仪与俯仰阶梯纹理 (Attitude Horizon & Pitch Ladder)
        /// 包含水平基准线、两端下垂刻标、+10°/+20° 仰角实线折角梯、-10°/-20° 俯角虚线梯。
        /// 次像素反走样，零颜色字面量。
        /// </summary>
        public static Texture2D CreateAttitudeHorizonTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);
                    float absY = Mathf.Abs(ny);

                    float maxAlpha = 0f;

                    // 1. 水平基准地平线 (Center Horizon Bar, ny = 0, 留出中心飞船空隙)
                    if (absX >= 0.18f && absX <= 0.76f)
                    {
                        float dY = absY;
                        float dX = Mathf.Max(0.18f - absX, absX - 0.76f);
                        float dist = Mathf.Max(dY - 0.022f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 2. 地平线两端下垂刻标 (Horizon End Ticks, nx = ±0.74, ny: -0.08 .. 0.0)
                    if (absX >= 0.72f && absX <= 0.76f && ny <= 0.01f && ny >= -0.08f)
                    {
                        float dist = Mathf.Max(Mathf.Abs(absX - 0.74f) - 0.020f, -0.08f - ny);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 3. +10° 俯仰仰角梯线 (ny ~ 0.28, 带折角)
                    if (absX >= 0.14f && absX <= 0.46f)
                    {
                        float dY = Mathf.Abs(ny - 0.28f);
                        float dX = Mathf.Max(0.14f - absX, absX - 0.46f);
                        float dist = Mathf.Max(dY - 0.018f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);

                        // 两端下垂折角 (0.22 .. 0.28)
                        if (absX >= 0.43f && absX <= 0.47f && ny <= 0.29f && ny >= 0.22f)
                        {
                            float tickDist = Mathf.Abs(absX - 0.45f) - 0.018f;
                            float ta = (tickDist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - tickDist / feather);
                            a = Mathf.Max(a, ta);
                        }
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 4. -10° 俯仰俯角虚线梯线 (ny ~ -0.28)
                    if (absX >= 0.14f && absX <= 0.46f)
                    {
                        float dashPhase = Mathf.Repeat(absX * 24f, 1f);
                        if (dashPhase < 0.65f)
                        {
                            float dY = Mathf.Abs(ny - (-0.28f));
                            float dX = Mathf.Max(0.14f - absX, absX - 0.46f);
                            float dist = Mathf.Max(dY - 0.018f, dX);
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);

                            // 两端上翘折角 (-0.28 .. -0.22)
                            if (absX >= 0.43f && absX <= 0.47f && ny >= -0.29f && ny <= -0.22f)
                            {
                                float tickDist = Mathf.Abs(absX - 0.45f) - 0.018f;
                                float ta = (tickDist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - tickDist / feather);
                                a = Mathf.Max(a, ta);
                            }
                            if (a > maxAlpha) maxAlpha = a;
                        }
                    }

                    // 5. +20° 仰角短梯线 (ny ~ 0.56)
                    if (absX >= 0.16f && absX <= 0.36f)
                    {
                        float dY = Mathf.Abs(ny - 0.56f);
                        float dX = Mathf.Max(0.16f - absX, absX - 0.36f);
                        float dist = Mathf.Max(dY - 0.018f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 6. -20° 俯角短虚线梯线 (ny ~ -0.56)
                    if (absX >= 0.16f && absX <= 0.36f)
                    {
                        float dashPhase = Mathf.Repeat(absX * 24f, 1f);
                        if (dashPhase < 0.65f)
                        {
                            float dY = Mathf.Abs(ny - (-0.56f));
                            float dX = Mathf.Max(0.16f - absX, absX - 0.36f);
                            float dist = Mathf.Max(dY - 0.018f, dX);
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > maxAlpha) maxAlpha = a;
                        }
                    }

                    if (maxAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color baseCol = WidgetStyleManager.NeutralOpaque;
                        cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, Mathf.Clamp01(maxAlpha));
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 64x64 高精度 3D 飞行指引仪导引标纹理 (Flight Director Chevron Cue)
        /// 包含次像素平滑倒 V 框架、中心精确瞄准点与水平翼基准刻标，零颜色字面量。
        /// </summary>
        public static Texture2D CreateFlightDirectorChevronTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.5f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);

                    // 1. 倒 V 导引前视指示框 (Chevron Cue)
                    float outDist = (0.68f - 1.25f * absX) - ny;
                    float inDist = ny - (0.32f - 1.25f * absX);
                    float sideDist = 0.62f - absX;
                    float bottomDist = ny - (-0.48f);

                    float chevAlpha = 0f;
                    if (outDist >= -feather && inDist >= -feather && sideDist >= -feather && bottomDist >= -feather)
                    {
                        float aOut = Mathf.Clamp01(outDist / feather);
                        float aIn = Mathf.Clamp01(inDist / feather);
                        float aSide = Mathf.Clamp01(sideDist / feather);
                        float aBot = Mathf.Clamp01(bottomDist / feather);
                        chevAlpha = Mathf.Min(Mathf.Min(aOut, aIn), Mathf.Min(aSide, aBot));
                    }

                    // 2. 中心十字瞄准微点 (Boresight Center Pip, 半径 0.12)
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float pipAlpha = Mathf.Clamp01((0.14f - r) / feather);

                    // 3. 左右水平翼展基准线 (ny: -0.06 .. 0.06, absX: 0.58 .. 0.88)
                    float wingAlpha = 0f;
                    if (absX >= 0.58f && absX <= 0.88f && Mathf.Abs(ny) <= 0.06f)
                    {
                        float wy = Mathf.Clamp01((0.022f - Mathf.Abs(ny)) / feather);
                        float wx = Mathf.Clamp01((absX - 0.58f) / feather) * Mathf.Clamp01((0.88f - absX) / feather);
                        wingAlpha = Mathf.Min(wy, wx);
                    }

                    float finalAlpha = Mathf.Max(Mathf.Max(chevAlpha, pipAlpha), wingAlpha);

                    if (finalAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color baseCol = WidgetStyleManager.NeutralOpaque;
                        cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, finalAlpha);
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

    }
}
