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
            Color amberBright = Color.Lerp(baseCol, Color.white, 0.15f);
            Color amberDark = Color.Lerp(baseCol, Color.black, 0.12f);
            Color shadowCol = new Color(0.06f, 0.05f, 0.04f, 1.0f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = x - cx;
                    float py = y - cy;

                    float dfg = ReticleSdf(px, py);
                    // 深度阴影 (向下偏右采样，SDF 柔和羽化)
                    float dsh = ReticleSdf(px - 1.2f, py + 2.2f);

                    float afg = Mathf.Clamp01(0.5f - dfg);
                    float ash = Mathf.Clamp01(0.5f - dsh * 0.75f) * 0.72f;

                    float t = Mathf.Clamp01((py + 20f) / 40f);
                    Color fgCol = Color.Lerp(amberDark, amberBright, t);

                    float outA = afg + ash * (1.0f - afg);
                    if (outA > 0.005f)
                    {
                        Color outRgb = (fgCol * afg + shadowCol * (ash * (1.0f - afg))) / outA;
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

            // 航电标准色彩 (与当前主题保持一致)
            ThemeConfig theme = ThemeManager.Instance?.CurrentTheme;
            Color colPrograde = theme != null ? (Color)theme.AccentPrimary : WidgetStyleManager.Instance.GetMeterColor(MeterStyleRole.Primary, null);
            Color colNormal = theme != null ? (Color)theme.AccentSecondary : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, null);
            Color colRadial = theme != null ? (Color)theme.AccentSecondary : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Cardinal, null);
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

                            // 2. 上、左、右三向延伸翼 (0°, 90°, 180°)
                            float aWingTop = BoxSdf(px, py - 18f, strokeW * 0.5f, 5f);
                            float aWingLeft = BoxSdf(px + 18f, py, 5f, strokeW * 0.5f);
                            float aWingRight = BoxSdf(px - 18f, py, 5f, strokeW * 0.5f);

                            alpha = Mathf.Clamp01(Mathf.Max(aRing, Mathf.Max(aWingTop, Mathf.Max(aWingLeft, aWingRight))));
                            break;

                        case "retrograde":
                            markerColor = colPrograde;
                            // 1. 中间空心圆环与三向翼
                            float dRingRet = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRingRet = Mathf.Clamp01(0.5f - dRingRet);
                            float aWTopRet = BoxSdf(px, py - 18f, strokeW * 0.5f, 5f);
                            float aWLeftRet = BoxSdf(px + 18f, py, 5f, strokeW * 0.5f);
                            float aWRightRet = BoxSdf(px - 18f, py, 5f, strokeW * 0.5f);

                            // 2. 内部 X 交叉线
                            float dDiag1 = (Mathf.Abs(px - py) / 1.4142f) - (strokeW * 0.45f);
                            float dDiag2 = (Mathf.Abs(px + py) / 1.4142f) - (strokeW * 0.45f);
                            float aCross = (dist < rCircle - 1.5f) ? Mathf.Max(Mathf.Clamp01(0.5f - dDiag1), Mathf.Clamp01(0.5f - dDiag2)) : 0f;

                            alpha = Mathf.Clamp01(Mathf.Max(aRingRet, Mathf.Max(aCross, Mathf.Max(aWTopRet, Mathf.Max(aWLeftRet, aWRightRet)))));
                            break;

                        case "normal":
                            markerColor = colNormal;
                            // 向上正三角形
                            float aTriNorm = EquilateralTriangleSdf(px, py - 1f, 18f, strokeW, true);
                            alpha = aTriNorm;
                            break;

                        case "antinormal":
                            markerColor = colNormal;
                            // 向下正三角形
                            float aTriAnti = EquilateralTriangleSdf(px, py + 1f, 18f, strokeW, false);
                            alpha = aTriAnti;
                            break;

                        case "radialin":
                            markerColor = colRadial;
                            // 圆环 + 中心实心点 + 内向刻度
                            float dRingRadIn = Mathf.Abs(dist - rCircle) - (strokeW * 0.5f);
                            float aRingRadIn = Mathf.Clamp01(0.5f - dRingRadIn);
                            float aDot = Mathf.Clamp01(3.5f - dist);
                            // 4向刻度 (上下左右)
                            float aTickH = (Mathf.Abs(py) < strokeW * 0.5f && Mathf.Abs(px) > 13f && Mathf.Abs(px) < 22f) ? 1f : 0f;
                            float aTickV = (Mathf.Abs(px) < strokeW * 0.5f && Mathf.Abs(py) > 13f && Mathf.Abs(py) < 22f) ? 1f : 0f;
                            alpha = Mathf.Clamp01(Mathf.Max(aRingRadIn, Mathf.Max(aDot, Mathf.Max(aTickH, aTickV))));
                            break;

                        case "radialout":
                            markerColor = colRadial;
                            // 小圆环 + 4向外发散刻度
                            float dRingRadOut = Mathf.Abs(dist - 10f) - (strokeW * 0.5f);
                            float aRingRadOut = Mathf.Clamp01(0.5f - dRingRadOut);
                            float aTickHOut = (Mathf.Abs(py) < strokeW * 0.5f && Mathf.Abs(px) > 10f && Mathf.Abs(px) < 22f) ? 1f : 0f;
                            float aTickVOut = (Mathf.Abs(px) < strokeW * 0.5f && Mathf.Abs(py) > 10f && Mathf.Abs(py) < 22f) ? 1f : 0f;
                            alpha = Mathf.Clamp01(Mathf.Max(aRingRadOut, Mathf.Max(aTickHOut, aTickVOut)));
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
            // 1. 水线左右水平翼 (Waterline Wings)
            float d1 = SegmentSdf(px, py, -56f, 0f, -18f, 0f);
            // 2. 左向下 45 度托槽斜边 (Downward V-Notch Cradle)
            float d2 = SegmentSdf(px, py, -18f, 0f, 0f, -18f);
            // 3. 右向上 45 度托槽斜边
            float d3 = SegmentSdf(px, py, 0f, -18f, 18f, 0f);
            // 4. 右侧水平翼
            float d4 = SegmentSdf(px, py, 18f, 0f, 56f, 0f);

            float dPath = Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4)) - 2.5f;
            // 翼梢平头垂直切割
            float dClip = Mathf.Max(0f, Mathf.Abs(px) - 56f);
            dPath = Mathf.Max(dPath, dClip);

            // 5. 正中心钻石瞄准点 (Boresight Diamond Pip, 精确位于 (0, 0))
            float dPip = (Mathf.Abs(px) + Mathf.Abs(py) - 5.5f) / 1.41421356f;

            return Mathf.Min(dPath, dPip);
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
