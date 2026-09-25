using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 高保真航电矢量标线与遮罩贴图生成工厂 (SDF 亚像素超清抗锯齿)
    /// 零外部素材依赖，运行时程序化生成顺向/逆向/法向/反法向/向内/向外/目标/机动等高保真矢量标
    /// </summary>
    public static class NavballMarkerFactory
    {
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static Sprite _circleMaskSprite;
        private static Sprite _circleRingSprite;
        private static Sprite _reticleSprite;

        public static void ClearCache()
        {
            SpriteCache.Clear();
            _circleMaskSprite = null;
            _circleRingSprite = null;
            _reticleSprite = null;
        }

        public static Sprite GetReticleSprite()
        {
            if (_reticleSprite != null) return _reticleSprite;

            const int w = 256;
            const int h = 128;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[w * h];
            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;

            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            Color baseCol = theme != null ? (Color)theme.WarningColor : new Color(1.0f, 0.65f, 0.05f, 1.0f);
            Color amberBright = Color.Lerp(baseCol, Color.white, 0.22f);
            Color amberDark = Color.Lerp(baseCol, Color.black, 0.06f);
            Color shadowCol = new Color(0.04f, 0.03f, 0.02f, 1.0f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = x - cx;
                    float py = y - cy;

                    float dfg = ReticleSdf(px, py);
                    // 360 度暗色环境光遮蔽轮廓 (Dark Ambient Occlusion Halo)
                    float dHalo = dfg - 1.2f;
                    // 触觉深度投影 (Directional Drop Shadow: dx = +1.5f, dy = -2.5f)
                    float dSh = ReticleSdf(px - 1.5f, py + 2.5f);

                    float afg = Mathf.Clamp01(0.5f - dfg);
                    float aHalo = Mathf.Clamp01(0.5f - dHalo * 0.8f) * 0.70f;
                    float aSh = Mathf.Clamp01(0.5f - dSh * 0.65f) * 0.85f;
                    float aShadowTotal = Mathf.Max(aHalo, aSh) * (1.0f - afg);

                    float t = Mathf.Clamp01((py + 20f) / 40f);
                    float ridge = Mathf.Clamp01(1.0f - Mathf.Abs(py - 1.2f) * 0.9f);
                    Color fgCol = Color.Lerp(amberDark, amberBright, t) + Color.white * (ridge * 0.15f);

                    float outA = afg + aShadowTotal;
                    if (outA > 0.005f)
                    {
                        Color outRgb = (fgCol * afg + shadowCol * aShadowTotal) / outA;
                        outRgb.a = outA;
                        pixels[y * w + x] = outRgb;
                    }
                    else
                    {
                        pixels[y * w + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _reticleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _reticleSprite;
        }

        public static Sprite GetMarkerSprite(string markerType)
        {
            if (string.IsNullOrEmpty(markerType)) return null;

            if (SpriteCache.TryGetValue(markerType, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite generated = GenerateMarkerSprite(markerType.ToLowerInvariant());
            if (generated != null)
            {
                SpriteCache[markerType] = generated;
            }
            return generated;
        }

        public static Sprite GetCircleMaskSprite()
        {
            if (_circleMaskSprite != null) return _circleMaskSprite;

            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = (size - 2) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    Color c = WidgetStyleManager.NeutralOpaque;
                    c.a = alpha;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _circleMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleMaskSprite;
        }

        public static Sprite GetCircleRingSprite()
        {
            if (_circleRingSprite != null) return _circleRingSprite;

            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float outerR = (size - 4) * 0.5f;
            float innerR = outerR - 3.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float dOuter = outerR - dist + 0.5f;
                    float dInner = dist - innerR + 0.5f;
                    float alpha = Mathf.Clamp01(Mathf.Min(dOuter, dInner));
                    Color ringCol = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, null);
                    ringCol.a = alpha;
                    pixels[y * size + x] = ringCol;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _circleRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleRingSprite;
        }

        private static Sprite GenerateMarkerSprite(string type)
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            Color clear = Color.clear;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            float center = (size - 1) * 0.5f;
            float rCircle = 13f;
            float strokeW = 2.5f;

            // 航电标准色彩 (严格遵循 KSP 官方原版航电矢量标配色方案与主题自适应)
            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            // 原版顺向/逆向色标: 官方经典高亮黄绿/荧光绿 (Stock Lime/Chartreuse #8FE800)
            Color colPrograde = new Color(0.68f, 0.98f, 0.12f, 1.0f);
            // 现代航空航天地表速度矢量/航迹标 (Surface Velocity Vector / Flight Path Marker / FPM): 航空高亮薄荷绿/翠绿 (#2EE59D)
            Color colVelocityVector = new Color(0.18f, 0.90f, 0.62f, 1.0f);
            // 原版法线/反法线色标: 官方经典品红/洋红 (Stock Magenta #EA1EE5)
            Color colNormal = new Color(0.92f, 0.14f, 0.88f, 1.0f);
            // 原版径向向外/向内色标: 官方经典天青/蓝绿 (Stock Cyan #18E8D4)
            Color colRadial = new Color(0.10f, 0.91f, 0.83f, 1.0f);
            Color colTarget = theme != null ? (Color)theme.AccentMagenta : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Danger, null);
            Color colManeuver = theme != null ? (Color)theme.AccentMagenta : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Danger, null);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x - center;
                    float py = y - center;
                    float dist = Mathf.Sqrt(px * px + py * py);
                    float alpha = 0f;
                    Color markerColor = WidgetStyleManager.NeutralOpaque;

                    switch (type)
                    {
                        case "prograde":
                            markerColor = colPrograde;
                            // 1. 中间空心圆环
                            float dRing = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRing = Mathf.Clamp01(0.5f - dRing);

                            // 2. 上、左、右三向延伸翼 (0°, 90°, 180° 方向，采用 SegmentSdf 亚像素圆角端点平滑解算)
                            float rWing1 = rCircle + 0.5f;
                            float rWing2 = rCircle + 5.5f;
                            float dWingTop = SegmentSdf(Mathf.Abs(px), py, 0f, rWing1, 0f, rWing2) - (strokeW * 0.5f);
                            float dWingSides = SegmentSdf(Mathf.Abs(px), py, rWing1, 0f, rWing2, 0f) - (strokeW * 0.5f);
                            float aWings = Mathf.Clamp01(0.5f - Mathf.Min(dWingTop, dWingSides));

                            alpha = Mathf.Clamp01(Mathf.Max(aRing, aWings));
                            break;

                        case "retrograde":
                            markerColor = colPrograde;
                            // 1. 中间空心圆环与三向翼
                            float dRingRet = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRingRet = Mathf.Clamp01(0.5f - dRingRet);
                            float rWingRet1 = rCircle + 0.5f;
                            float rWingRet2 = rCircle + 5.5f;
                            float dWTopRet = SegmentSdf(Mathf.Abs(px), py, 0f, rWingRet1, 0f, rWingRet2) - (strokeW * 0.5f);
                            float dWSidesRet = SegmentSdf(Mathf.Abs(px), py, rWingRet1, 0f, rWingRet2, 0f) - (strokeW * 0.5f);
                            float aWingsRet = Mathf.Clamp01(0.5f - Mathf.Min(dWTopRet, dWSidesRet));

                            // 2. 内部 X 交叉线
                            float dDiag1 = (Mathf.Abs(px - py) / 1.4142f) - (strokeW * 0.45f);
                            float dDiag2 = (Mathf.Abs(px + py) / 1.4142f) - (strokeW * 0.45f);
                            float aCross = (dist < rCircle - 1.5f) ? Mathf.Max(Mathf.Clamp01(0.5f - dDiag1), Mathf.Clamp01(0.5f - dDiag2)) : 0f;

                            alpha = Mathf.Clamp01(Mathf.Max(aRingRet, Mathf.Max(aCross, aWingsRet)));
                            break;

                        case "velocity_vector":
                        case "velocity":
                        case "fpm":
                        case "surface_prograde":
                            markerColor = colVelocityVector;
                            // 航空航电标准飞行航迹标 (Flight Path Marker: 两平翼一立尾微型航空矢量标构型)
                            // 1. 中心精巧空心圆环 (r = 8.5px，精巧通透，不遮挡跑道与地标)
                            float rFpm = 8.5f;
                            float strokeFpm = 2.4f;
                            float dRingFpm = Mathf.Abs(dist - rFpm) - (strokeFpm * 0.5f);
                            float aRingFpm = Mathf.Clamp01(0.5f - dRingFpm);

                            // 2. 左右平直机翼 (自 rFpm + 0.5f 延伸至 20f, y = 0)
                            float dWingH = SegmentSdf(Mathf.Abs(px), py, rFpm + 0.5f, 0f, 20f, 0f) - (strokeFpm * 0.5f);
                            // 3. 顶部垂直稳定尾翼 (自 rFpm + 0.5f 向上延伸至 17.5f, x = 0)
                            float dFinV = SegmentSdf(Mathf.Abs(px), py, 0f, rFpm + 0.5f, 0f, 17.5f) - (strokeFpm * 0.5f);
                            float aWingsFpm = Mathf.Clamp01(0.5f - Mathf.Min(dWingH, dFinV));

                            alpha = Mathf.Clamp01(Mathf.Max(aRingFpm, aWingsFpm));
                            break;

                        case "anti_velocity_vector":
                        case "surface_retrograde":
                            markerColor = colVelocityVector;
                            // 航空标准反向航迹标 (Anti-Flight Path Marker)
                            float rAntiFpm = 8.5f;
                            float strokeAntiFpm = 2.4f;
                            float dRingAFpm = Mathf.Abs(dist - rAntiFpm) - (strokeAntiFpm * 0.5f);
                            float aRingAFpm = Mathf.Clamp01(0.5f - dRingAFpm);

                            float dWingHAFpm = SegmentSdf(Mathf.Abs(px), py, rAntiFpm + 0.5f, 0f, 20f, 0f) - (strokeAntiFpm * 0.5f);
                            float dFinVAFpm = SegmentSdf(Mathf.Abs(px), py, 0f, rAntiFpm + 0.5f, 0f, 17.5f) - (strokeAntiFpm * 0.5f);
                            float aWingsAFpm = Mathf.Clamp01(0.5f - Mathf.Min(dWingHAFpm, dFinVAFpm));

                            // 内部反向水平短横标
                            float dMidDash = SegmentSdf(Mathf.Abs(px), py, 0f, 0f, 5.5f, 0f) - (strokeAntiFpm * 0.45f);
                            float aMidDash = (dist < rAntiFpm - 1.2f) ? Mathf.Clamp01(0.5f - dMidDash) : 0f;

                            alpha = Mathf.Clamp01(Mathf.Max(aRingAFpm, Mathf.Max(aWingsAFpm, aMidDash)));
                            break;

                        case "normal":
                            markerColor = colNormal;
                            // 向上正三角形 (顶点向上，底边水平，高度几何居中)
                            float aTriNorm = EquilateralTriangleSdf(px, py, 16f, strokeW, true);
                            // 中心准直实心瞄准点 (Pip)
                            float aDotNorm = Mathf.Clamp01(0.5f - (dist - 2.2f));
                            alpha = Mathf.Clamp01(Mathf.Max(aTriNorm, aDotNorm));
                            break;

                        case "antinormal":
                            markerColor = colNormal;
                            // 1. 向下正三角形 (顶点向下，底边水平，高度几何居中)
                            float rAnti = 16f;
                            float aTriAnti = EquilateralTriangleSdf(px, py, rAnti, strokeW, false);

                            // 2. 三边向外放射延伸的 3 个突刺/刻度翼 (Spikes at 90°, 210°, 330°)
                            float yTopAnti = rAnti * 0.57735027f; // r / sqrt(3)
                            float lSpikeTop = 6.8f;
                            float lSpikeSide = 6.5f;

                            // 顶部中点垂直向上突刺 (px = 0, y 自 yTopAnti 向上至 yTopAnti + lSpikeTop)
                            float dSpikeTop = SegmentSdf(Mathf.Abs(px), py, 0f, yTopAnti, 0f, yTopAnti + lSpikeTop) - (strokeW * 0.5f);

                            // 左右两翼自侧边约 56% 处向外下方放射突刺 (利用 Abs(px) 轴对称合并计算)
                            float tAnti = 0.56f;
                            float xMidAnti = rAnti * (1.0f - tAnti);
                            float yMidAnti = yTopAnti - tAnti * (3.0f * yTopAnti);
                            const float cos30 = 0.8660254f;
                            const float sin30 = 0.5f;
                            float dSpikeSides = SegmentSdf(Mathf.Abs(px), py, xMidAnti, yMidAnti, xMidAnti + cos30 * lSpikeSide, yMidAnti - sin30 * lSpikeSide) - (strokeW * 0.5f);

                            float aSpikesAnti = Mathf.Clamp01(0.5f - Mathf.Min(dSpikeTop, dSpikeSides));
                            // 反法线内部为空心（无中心点），由倒三角与三向放射突刺组成
                            alpha = Mathf.Clamp01(Mathf.Max(aTriAnti, aSpikesAnti));
                            break;

                        case "radialin":
                            markerColor = colRadial;
                            // 1. 中间空心圆环
                            float dRingRadIn = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRingRadIn = Mathf.Clamp01(0.5f - dRingRadIn);

                            // 2. 四角 45°、135°、225°、315° 向内汇聚四向刻度 (向内延伸至半径 5.5f 留空，中心为空心孔径)
                            float rRadIn1 = rCircle - 0.5f;
                            float rRadIn2 = 5.5f;
                            const float c45In = 0.70710678f;
                            float dProngIn = SegmentSdf(Mathf.Abs(px), Mathf.Abs(py), rRadIn1 * c45In, rRadIn1 * c45In, rRadIn2 * c45In, rRadIn2 * c45In) - (strokeW * 0.5f);
                            float aProngIn = Mathf.Clamp01(0.5f - dProngIn);

                            alpha = Mathf.Clamp01(Mathf.Max(aRingRadIn, aProngIn));
                            break;

                        case "radialout":
                            markerColor = colRadial;
                            // 1. 中间空心圆环
                            float dRingRadOut = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRingRadOut = Mathf.Clamp01(0.5f - dRingRadOut);

                            // 2. 正中心实心瞄准点 (Pip)
                            float aDotRadOut = Mathf.Clamp01(0.5f - (dist - 2.2f));

                            // 3. 四角 45°、135°、225°、315° 向外发散四向突刺 (利用 Abs(px), Abs(py) 四象限对称合并解算)
                            float rRadOut1 = rCircle + 0.5f;
                            float rRadOut2 = rCircle + 5.2f;
                            const float c45Out = 0.70710678f;
                            float dProngOut = SegmentSdf(Mathf.Abs(px), Mathf.Abs(py), rRadOut1 * c45Out, rRadOut1 * c45Out, rRadOut2 * c45Out, rRadOut2 * c45Out) - (strokeW * 0.5f);
                            float aProngOut = Mathf.Clamp01(0.5f - dProngOut);

                            alpha = Mathf.Clamp01(Mathf.Max(aRingRadOut, Mathf.Max(aDotRadOut, aProngOut)));
                            break;

                        case "target":
                            markerColor = colTarget;
                            // 同心圆瞄准圈
                            float dOuterTgt = Mathf.Abs(dist - 14f) - (strokeW * 0.5f);
                            float aOuterTgt = Mathf.Clamp01(0.5f - dOuterTgt);
                            float aCenterDot = Mathf.Clamp01(3.0f - dist);
                            float aCrossTgtH = (Mathf.Abs(py) < strokeW * 0.4f && Mathf.Abs(px) > 8f && Mathf.Abs(px) < 20f) ? 1f : 0f;
                            float aCrossTgtV = (Mathf.Abs(px) < strokeW * 0.4f && Mathf.Abs(py) > 8f && Mathf.Abs(py) < 20f) ? 1f : 0f;
                            alpha = Mathf.Clamp01(Mathf.Max(aOuterTgt, Mathf.Max(aCenterDot, Mathf.Max(aCrossTgtH, aCrossTgtV))));
                            break;

                        case "antitarget":
                            markerColor = colTarget;
                            // 反目标：同心圆瞄准圈 + 内部 X 交叉线
                            float dOuterAnti = Mathf.Abs(dist - 14f) - (strokeW * 0.5f);
                            float aOuterAnti = Mathf.Clamp01(0.5f - dOuterAnti);
                            float dDiag1A = (Mathf.Abs(px - py) / 1.4142f) - (strokeW * 0.45f);
                            float dDiag2A = (Mathf.Abs(px + py) / 1.4142f) - (strokeW * 0.45f);
                            float aCrossAnti = (dist < 12.5f) ? Mathf.Max(Mathf.Clamp01(0.5f - dDiag1A), Mathf.Clamp01(0.5f - dDiag2A)) : 0f;
                            alpha = Mathf.Clamp01(Mathf.Max(aOuterAnti, aCrossAnti));
                            break;

                        case "maneuver":
                            markerColor = colManeuver;
                            // 原版机动节点：倒正三角 (顶点向下) + 顶部双翼刻度 + 中心瞄准点
                            float aTriMan = EquilateralTriangleSdf(px, py + 1f, 16f, strokeW, false);
                            float aDotMan = Mathf.Clamp01(3.2f - dist);
                            // 顶部两侧外延横向小翼 (两翼横线)
                            float aWingManL = (Mathf.Abs(py - 7f) < strokeW * 0.5f && px < -10f && px > -19f) ? 1f : 0f;
                            float aWingManR = (Mathf.Abs(py - 7f) < strokeW * 0.5f && px > 10f && px < 19f) ? 1f : 0f;
                            alpha = Mathf.Clamp01(Mathf.Max(aTriMan, Mathf.Max(aDotMan, Mathf.Max(aWingManL, aWingManR))));
                            break;

                        default:
                            alpha = Mathf.Clamp01(6f - dist);
                            break;
                    }

                    if (alpha > 0.001f)
                    {
                        Color col = markerColor;
                        col.a = alpha * markerColor.a;
                        pixels[y * size + x] = col;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static float BoxSdf(float x, float y, float halfW, float halfH)
        {
            float dx = Mathf.Abs(x) - halfW;
            float dy = Mathf.Abs(y) - halfH;
            float d = Mathf.Max(dx, dy);
            return Mathf.Clamp01(0.5f - d);
        }

        private static float EquilateralTriangleSdf(float px, float py, float r, float strokeW, bool pointingUp)
        {
            if (!pointingUp) py = -py;
            // 正三角形边缘与线框检测
            float k = Mathf.Sqrt(3.0f);
            float pxAbs = Mathf.Abs(px) - r;
            float pyOffset = py + r / k;
            if (pxAbs + k * pyOffset > 0.0f)
            {
                float newX = (pxAbs - k * pyOffset) * 0.5f;
                float newY = (-k * pxAbs - pyOffset) * 0.5f;
                pxAbs = newX;
                pyOffset = newY;
            }
            pxAbs -= Mathf.Clamp(pxAbs, -2.0f * r, 0.0f);
            float d = -Mathf.Sqrt(pxAbs * pxAbs + pyOffset * pyOffset) * Mathf.Sign(pyOffset);
            float wireD = Mathf.Abs(d) - (strokeW * 0.5f);
            return Mathf.Clamp01(0.5f - wireD);
        }

        private static float ReticleSdf(float px, float py)
        {
            float sx = Mathf.Abs(px);

            // 1. 向下 45 度精密托槽 (Downward V-Cradle: 自 (18, 0) 向下汇聚至 (0, -18))
            float dV = SegmentSdf(sx, py, 0f, -18f, 18f, 0f) - 3.2f;

            // 2. 现代水线双翼基底 (Main Wings: x 从 18 到 58)
            float dW = SegmentSdf(sx, py, 18f, 0f, 58f, 0f) - 3.2f;
            float dCap = sx - 58f;
            // 外翼尖 45 度倒角斜切 (45-degree Aerodynamic Wingtip Chamfer)
            float dBevel = (sx + py - 58f) / 1.41421356f;
            dW = Mathf.Max(dW, Mathf.Max(dCap, dBevel));

            float dFrame = Mathf.Min(dV, dW);

            // 3. 正中心准直光学瞄准标具 (Precision Optics Boresight Pip with Optical Aperture `◈`)
            // 外菱形边界 (Outer Diamond, r = 6.6)
            float dOuterDia = (Mathf.Abs(px) + Mathf.Abs(py) - 6.6f) / 1.41421356f;
            // 内部光学视窗孔径 (Inner Aperture Hole, r = 2.6)
            float dInnerHole = (2.6f - (Mathf.Abs(px) + Mathf.Abs(py))) / 1.41421356f;
            float dRingDia = Mathf.Max(dOuterDia, dInnerHole);
            // 绝对原点 (0, 0) 处亚像素准直中心针尖点 (Center Pinpoint Boresight Dot)
            float dPin = Mathf.Sqrt(px * px + py * py) - 1.2f;
            float dPip = Mathf.Min(dRingDia, dPin);

            return Mathf.Min(dFrame, dPip);
        }

        private static float SegmentSdf(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float l2 = dx * dx + dy * dy;
            if (l2 <= 0.00001f) return Mathf.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
            float t = Mathf.Clamp01(((px - x1) * dx + (py - y1) * dy) / l2);
            float qx = x1 + t * dx;
            float qy = y1 + t * dy;
            float ex = px - qx;
            float ey = py - qy;
            return Mathf.Sqrt(ex * ex + ey * ey);
        }
    }
}
