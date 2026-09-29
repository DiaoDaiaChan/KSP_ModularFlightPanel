using System;
using UnityEngine;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core.Rendering
{
    /// <summary>
    /// 3D 飞船姿态仪程序化视觉资产生成器 (Spacecraft Attitude Visual Asset Generator)
    /// 解耦自 VesselAttitudeSphereWidget，负责静态程序化纹理烘焙 (单次生成，零运行时 GC 与显存开销)。
    /// </summary>
    public static class SpacecraftAttitudeVisualGenerator
    {
        private static Texture2D _spacecraftTex;
        private static Texture2D _flightDirectorTex;
        private static Texture2D _bezelTex;

        public static Texture2D GetOrCreateSpacecraftTexture()
        {
            if (_spacecraftTex == null)
            {
                _spacecraftTex = CreateProcedural3DSpacecraftTexture();
            }
            return _spacecraftTex;
        }

        public static Texture2D GetOrCreateFlightDirectorTexture()
        {
            if (_flightDirectorTex == null)
            {
                _flightDirectorTex = CreateFlightDirectorChevronTexture();
            }
            return _flightDirectorTex;
        }

        public static Texture2D GetOrCreateBezelTexture()
        {
            if (_bezelTex == null)
            {
                _bezelTex = CreateBezelRingTexture();
            }
            return _bezelTex;
        }

        /// <summary>
        /// 程序化生成 256x256 高精度 3D 航天器纹理 (Half-Lambert 漫反射 + 边缘高光 + 驾驶舱 + 编队灯 + 离子羽流)
        /// </summary>
        private static Texture2D CreateProcedural3DSpacecraftTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "MFP_SpacecraftProcedural3D";
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 1.6f * invHalf;

            // 虚拟光源方向 (从左上方照向机身)
            Vector3 lightDir = new Vector3(-0.45f, 0.55f, 0.70f).normalized;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);

                    float alpha = 0f;
                    Vector3 normal = Vector3.forward;
                    float albedoR = 0.85f, albedoG = 0.88f, albedoB = 0.95f;
                    bool isCanopy = false;
                    bool isPlume = false;
                    bool isLight = false;
                    Color emissiveCol = Color.clear;

                    // 1. 中央机身轮廓 (Fuselage: 机头 ny ~ 0.84 至机尾 ny ~ -0.56)
                    float bodyHalfW = 0f;
                    if (ny >= 0.20f && ny <= 0.84f)
                    {
                        float t = (0.84f - ny) / 0.64f; // 0 .. 1
                        bodyHalfW = Mathf.Lerp(0.02f, 0.17f, Mathf.Sqrt(t));
                    }
                    else if (ny >= -0.56f && ny < 0.20f)
                    {
                        bodyHalfW = Mathf.Lerp(0.17f, 0.21f, (0.20f - ny) / 0.76f);
                    }

                    if (bodyHalfW > 0.001f && absX <= bodyHalfW + feather)
                    {
                        float dist = absX - bodyHalfW;
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > alpha)
                        {
                            alpha = a;
                            float u = Mathf.Clamp(nx / Mathf.Max(0.01f, bodyHalfW), -1f, 1f);
                            normal = new Vector3(u * 0.85f, 0.20f, Mathf.Sqrt(Mathf.Max(0f, 1f - u * u * 0.72f))).normalized;
                            albedoR = 0.82f; albedoG = 0.86f; albedoB = 0.92f;
                        }
                    }

                    // 2. 三角主翼 (Swept Delta Wings with elevon break line)
                    if (ny >= -0.52f && ny <= 0.32f)
                    {
                        float wingT = (0.32f - ny) / 0.84f; // 0 .. 1
                        float wingSpan = Mathf.Lerp(0.12f, 0.78f, Mathf.Pow(wingT, 1.25f));
                        float wingInner = Mathf.Max(0f, bodyHalfW - 0.02f);
                        if (absX >= wingInner && absX <= wingSpan + feather)
                        {
                            float dist = absX - wingSpan;
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > alpha)
                            {
                                alpha = a;
                                normal = new Vector3(Mathf.Sign(nx) * 0.30f, -0.15f, 0.94f).normalized;
                                albedoR = 0.72f; albedoG = 0.78f; albedoB = 0.86f;
                                if (Mathf.Abs(ny - (-0.40f)) < 0.015f && absX > 0.22f)
                                {
                                    albedoR *= 0.55f; albedoG *= 0.55f; albedoB *= 0.55f;
                                }
                            }
                        }
                    }

                    // 3. 翼尖航行灯与编队灯 (Wingtip Navigation Lights: Red port, Green starboard)
                    if (ny >= -0.50f && ny <= -0.40f && absX >= 0.72f && absX <= 0.79f)
                    {
                        alpha = 1.0f;
                        isLight = true;
                        if (nx < 0f)
                        {
                            emissiveCol = new Color(1.0f, 0.18f, 0.18f, 1.0f);
                        }
                        else
                        {
                            emissiveCol = new Color(0.15f, 1.0f, 0.35f, 1.0f);
                        }
                    }

                    // 4. 水滴形座舱盖 (Cockpit Canopy with high glass specular)
                    if (ny >= 0.24f && ny <= 0.58f && absX <= 0.082f)
                    {
                        float cT = (0.58f - ny) / 0.34f;
                        float cW = Mathf.Sin(cT * Mathf.PI) * 0.078f;
                        if (absX <= cW + feather)
                        {
                            float dist = absX - cW;
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > 0.01f)
                            {
                                alpha = Mathf.Max(alpha, a);
                                normal = new Vector3(nx * 7.5f, 0.35f, 0.88f).normalized;
                                albedoR = 0.08f; albedoG = 0.48f; albedoB = 0.72f;
                                isCanopy = true;
                            }
                        }
                    }

                    // 5. 双发尾喷管金属钟罩 (Twin Engine Exhaust Bells)
                    if (ny >= -0.66f && ny <= -0.52f && (Mathf.Abs(absX - 0.115f) <= 0.045f))
                    {
                        alpha = 1.0f;
                        normal = new Vector3(nx * 3f, -0.6f, 0.75f).normalized;
                        albedoR = 0.32f; albedoG = 0.35f; albedoB = 0.40f;
                    }

                    // 6. 等离子推进羽流微光 (Electric Blue Plasma Thruster Plume)
                    if (ny >= -0.88f && ny < -0.66f && (Mathf.Abs(absX - 0.115f) <= 0.048f))
                    {
                        float plumeT = (-0.66f - ny) / 0.22f;
                        float plumeW = (1.0f - plumeT) * 0.045f + 0.005f;
                        float pDist = Mathf.Abs(absX - 0.115f);
                        if (pDist <= plumeW + feather)
                        {
                            float pAlpha = (1.0f - plumeT) * Mathf.Clamp01(1.0f - pDist / plumeW);
                            if (pAlpha > 0.02f)
                            {
                                isPlume = true;
                                alpha = Mathf.Max(alpha, pAlpha);
                                emissiveCol = Color.Lerp(new Color(0.85f, 0.96f, 1.0f, 1.0f), new Color(0.12f, 0.72f, 1.0f, 0.85f), plumeT);
                            }
                        }
                    }

                    if (alpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else if (isLight || isPlume)
                    {
                        emissiveCol.a = alpha;
                        cols[y * size + x] = emissiveCol;
                    }
                    else
                    {
                        // Half-Lambert 漫反射光照模型
                        float nDotL = Vector3.Dot(normal, lightDir);
                        float halfLambert = Mathf.Clamp01(nDotL * 0.5f + 0.5f);
                        float diff = halfLambert * 0.75f + 0.25f;

                        // Blinn-Phong 高光
                        Vector3 viewDir = Vector3.forward;
                        Vector3 halfVec = (lightDir + viewDir).normalized;
                        float specPower = isCanopy ? 36f : 12f;
                        float specIntensity = isCanopy ? 0.75f : 0.25f;
                        float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, halfVec)), specPower) * specIntensity;

                        float edgeOcclusion = Mathf.Clamp01(alpha * 1.5f);

                        Color shaded = WidgetStyleManager.NeutralOpaque;
                        shaded.r = Mathf.Clamp01((albedoR * diff + spec) * edgeOcclusion);
                        shaded.g = Mathf.Clamp01((albedoG * diff + spec) * edgeOcclusion);
                        shaded.b = Mathf.Clamp01((albedoB * diff + spec) * edgeOcclusion);
                        shaded.a = Mathf.Clamp01(alpha);
                        cols[y * size + x] = shaded;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D CreateFlightDirectorChevronTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "MFP_FlightDirectorChevron";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf;
                    float absX = Mathf.Abs(nx);

                    float targetY = 0.55f - absX * 0.95f;
                    float distY = Mathf.Abs(ny - targetY);
                    float distX = Mathf.Max(0f, absX - 0.72f);
                    float dist = Mathf.Max(distY - 0.12f, distX);

                    float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                    if (a <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = a;
                        cols[y * size + x] = c;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D CreateBezelRingTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "MFP_BezelRingTicks";
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];

            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    float alpha = 0f;
                    // 极简纤细刻度环 (r: 0.93 .. 0.995)
                    if (r >= 0.93f && r <= 0.995f)
                    {
                        float dist = Mathf.Max(0.95f - r, r - 0.98f);
                        alpha = (dist <= 0f) ? 0.90f : Mathf.Clamp01(1.0f - dist / feather) * 0.90f;
                    }

                    // 12 点钟航向主标三角形 (Top Notch Triangle)
                    if (ny >= 0.86f && Mathf.Abs(nx) <= (0.98f - ny) * 0.70f)
                    {
                        alpha = 1.0f;
                    }

                    // 滚转角指示标尺 (Bank Angle Ticks at ±30°, ±60°, ±90°)
                    float angDeg = Mathf.Atan2(nx, ny) * Mathf.Rad2Deg;
                    float absAng = Mathf.Abs(angDeg);
                    bool isTick = (Mathf.Abs(absAng - 30f) < 0.9f || Mathf.Abs(absAng - 60f) < 0.9f || Mathf.Abs(absAng - 90f) < 0.9f);
                    if (isTick && r >= 0.88f && r <= 0.96f)
                    {
                        alpha = 0.88f;
                    }

                    if (alpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color c = WidgetStyleManager.NeutralOpaque;
                        c.a = alpha;
                        cols[y * size + x] = c;
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }
    }
}
