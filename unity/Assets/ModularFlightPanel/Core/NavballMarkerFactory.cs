using System;
using System.Collections.Generic;
using UnityEngine;

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
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
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
                    pixels[y * size + x] = new Color(0f, 1f, 0.8f, alpha);
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
            Color clear = new Color(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            float center = (size - 1) * 0.5f;
            float rCircle = 13f;
            float strokeW = 2.5f;

            // 航电标准色彩
            Color colPrograde = new Color(0.35f, 1.0f, 0.15f, 1.0f);   // 亮绿黄
            Color colNormal = new Color(0.85f, 0.25f, 1.0f, 1.0f);     // 亮紫色
            Color colRadial = new Color(0.0f, 0.88f, 1.0f, 1.0f);      // 亮青色
            Color colTarget = new Color(1.0f, 0.2f, 0.65f, 1.0f);      // 亮品红
            Color colManeuver = new Color(0.15f, 0.65f, 1.0f, 1.0f);   // 亮钴蓝

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x - center;
                    float py = y - center;
                    float dist = Mathf.Sqrt(px * px + py * py);
                    float alpha = 0f;
                    Color markerColor = Color.white;

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

                        case "maneuver":
                            markerColor = colManeuver;
                            // 机动节点星形矢量
                            float dManRing = Mathf.Abs(dist - 12f) - (strokeW * 0.5f);
                            float aManRing = Mathf.Clamp01(0.5f - dManRing);
                            float aManH = BoxSdf(px, py, 19f, strokeW * 0.5f);
                            float aManV = BoxSdf(px, py, strokeW * 0.5f, 19f);
                            alpha = Mathf.Clamp01(Mathf.Max(aManRing, Mathf.Max(aManH, aManV)));
                            break;

                        default:
                            alpha = Mathf.Clamp01(6f - dist);
                            break;
                    }

                    if (alpha > 0.001f)
                    {
                        pixels[y * size + x] = new Color(markerColor.r, markerColor.g, markerColor.b, alpha * markerColor.a);
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
    }
}
