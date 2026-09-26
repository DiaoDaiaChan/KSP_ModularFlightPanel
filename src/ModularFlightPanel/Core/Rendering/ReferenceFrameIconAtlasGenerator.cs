using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core.Rendering
{
    /// <summary>
    /// 航天导航参考系/坐标系高反差矢量图集生成器 (Procedural Reference Frame Icon Atlas)
    /// 为全流程无头渲染、HUD 仪表盘及设置菜单提供 100% 矢量的 512x256 航电图集：
    /// 1. 惯性坐标系 (Inertial Frame): 天球经纬导轨 + 三维正交右手笛卡尔轴系 (X/Y/Z) + 恒星四象限不动基准；
    /// 2. 体固坐标系 (Body-Fixed / Surface Frame): 中心行星球体 + 纬线圈与经线圈 + 极轴自转导引矢量 (ω) + 地表固定测站；
    /// 3. 轨道坐标系 (Orbital Frame / LVLH): 引力焦点天体 + 开普勒闭合椭圆 + 远/近拱点 + 局部三轴矢量 (V 速度切向 / R 径向 / N 法向)；
    /// 4. 拉格朗日点坐标系 (Lagrange Point / Barycentric Frame): 双天体质量系统 (M1/M2) + 共同质心 (⊕) + 洛希瓣等势哑铃轮廓 + L1~L5 五大平动点；
    /// 5. 目标坐标系 (Target Frame): 目标对准雷达瞄准环 + 相对交会视线导引。
    /// 严格遵循 MFP-SPEC-006 零颜色字面量铁律，像素纯由 NeutralOpaque 与 Color.clear 驱动，支持全套主题实时变色。
    /// </summary>
    public static class ReferenceFrameIconAtlasGenerator
    {
        private static Texture2D _cachedAtlas;
        private const int AtlasWidth = 512;
        private const int AtlasHeight = 256;
        private const int TileSize = 128;
        private const int Columns = 4;
        private const int Rows = 2;

        public const int INDEX_INERTIAL = 0;
        public const int INDEX_BODY_FIXED = 1;
        public const int INDEX_ORBITAL = 2;
        public const int INDEX_LAGRANGE = 3;
        public const int INDEX_TARGET = 4;
        public const int INDEX_SURFACE = 5;
        public const int INDEX_BODY_DIRECTION = 6;
        public const int INDEX_BODY_SURFACE = 1; // 兼容历史别名

        private static readonly Dictionary<string, int> FrameNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "INERTIAL", INDEX_INERTIAL },
            { "NON_ROTATING", INDEX_INERTIAL },
            { "BODY_CENTRED_NON_ROTATING", INDEX_INERTIAL },
            { "HELIOCENTRIC", INDEX_INERTIAL },
            { "HELIOCENTRIC_INERTIAL", INDEX_INERTIAL },
            { "J2000", INDEX_INERTIAL },
            { "ECI", INDEX_INERTIAL },
            { "ICRS", INDEX_INERTIAL },
            { "惯性", INDEX_INERTIAL },
            { "惯性系", INDEX_INERTIAL },
            { "质心惯性", INDEX_INERTIAL },
            { "不旋转", INDEX_INERTIAL },

            { "BODY_FIXED", INDEX_BODY_FIXED },
            { "BODY_SURFACE", INDEX_BODY_FIXED },
            { "ROTATING", INDEX_BODY_FIXED },
            { "ECEF", INDEX_BODY_FIXED },
            { "体固", INDEX_BODY_FIXED },
            { "体固系", INDEX_BODY_FIXED },
            { "地固", INDEX_BODY_FIXED },
            { "地心地固", INDEX_BODY_FIXED },
            { "地心体固", INDEX_BODY_FIXED },

            { "SURFACE", INDEX_SURFACE },
            { "GROUND", INDEX_SURFACE },
            { "TOPOCENTRIC", INDEX_SURFACE },
            { "HORIZON", INDEX_SURFACE },
            { "地表", INDEX_SURFACE },
            { "地表系", INDEX_SURFACE },

            { "ORBIT", INDEX_ORBITAL },
            { "ORBITAL", INDEX_ORBITAL },
            { "BODY_DIRECTION", INDEX_ORBITAL },
            { "PARENT_DIRECTION", INDEX_ORBITAL },
            { "BODY_CENTRED_PARENT_DIRECTION", INDEX_ORBITAL },
            { "LVLH", INDEX_ORBITAL },
            { "FRENET", INDEX_ORBITAL },
            { "PERIFOCAL", INDEX_ORBITAL },
            { "VVLH", INDEX_ORBITAL },
            { "轨道", INDEX_ORBITAL },
            { "轨道系", INDEX_ORBITAL },

            { "LAGRANGE", INDEX_LAGRANGE },
            { "BARYCENTRIC", INDEX_LAGRANGE },
            { "PULSATING", INDEX_LAGRANGE },
            { "PULSATING_BARYCENTRIC", INDEX_LAGRANGE },
            { "ROTATING_BARYCENTRIC", INDEX_LAGRANGE },
            { "ROTATING_PULSATING", INDEX_LAGRANGE },
            { "BARYCENTRIC_ROTATING", INDEX_LAGRANGE },
            { "THREE_BODY", INDEX_LAGRANGE },
            { "L1", INDEX_LAGRANGE },
            { "L2", INDEX_LAGRANGE },
            { "L3", INDEX_LAGRANGE },
            { "L4", INDEX_LAGRANGE },
            { "L5", INDEX_LAGRANGE },
            { "拉格朗日", INDEX_LAGRANGE },
            { "拉格朗日点", INDEX_LAGRANGE },
            { "L点", INDEX_LAGRANGE },
            { "L点系", INDEX_LAGRANGE },

            { "TARGET", INDEX_TARGET },
            { "TARGET_ORBITAL", INDEX_TARGET },
            { "DOCKING", INDEX_TARGET },
            { "RELATIVE", INDEX_TARGET },
            { "目标", INDEX_TARGET },
            { "目标系", INDEX_TARGET },
            { "目标轨道", INDEX_TARGET }
        };

        public static int GetIconIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return INDEX_ORBITAL;
            name = name.Trim().ToUpperInvariant();

            if (FrameNameToIndex.TryGetValue(name, out int idx)) return idx;

            if (name.Contains("TARGET") || name.Contains("DOCK") || name.Contains("目标"))
                return INDEX_TARGET;
            if (name.Contains("BARYCENTRIC") || name.Contains("LAGRANGE") || name.Contains("PULSATING") || name.Contains("L1") || name.Contains("L2") || name.Contains("L3") || name.Contains("L4") || name.Contains("L5") || name.Contains("L点") || name.Contains("拉格朗日"))
                return INDEX_LAGRANGE;
            if (name.Contains("BODY_FIXED") || name.Contains("BODY_SURFACE") || name.Contains("ROTATING") || name.Contains("ECEF") || name.Contains("体固") || name.Contains("地固"))
                return INDEX_BODY_FIXED;
            if (name.Contains("SURFACE") || name.Contains("GROUND") || name.Contains("地表"))
                return INDEX_SURFACE;
            if (name.Contains("ORBIT") || name.Contains("LVLH") || name.Contains("FRENET") || name.Contains("轨道") || name.Contains("BODY_DIRECTION") || name.Contains("PARENT_DIRECTION"))
                return INDEX_ORBITAL;
            if (name.Contains("INERTIAL") || name.Contains("NON_ROTATING") || name.Contains("惯性") || name.Contains("不旋转"))
                return INDEX_INERTIAL;

            return INDEX_ORBITAL;
        }

        public static Rect GetIconUv(string name)
        {
            return GetIconUv(GetIconIndex(name));
        }

        public static Rect GetIconUv(int index)
        {
            if (index < 0 || index >= Columns * Rows) index = INDEX_INERTIAL;
            int col = index % Columns;
            int rowFromTop = index / Columns;
            float u = (float)col / Columns;
            float v = 1f - (float)(rowFromTop + 1) / Rows;
            float w = 1f / Columns;
            float h = 1f / Rows;
            return new Rect(u, v, w, h);
        }

        public static Texture2D GetAtlas()
        {
            if (_cachedAtlas != null) return _cachedAtlas;

            Texture2D tex = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, true);
            tex.name = "MFP_AvionicsReferenceFrameIconAtlas";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 2;

            Color[] pixels = new Color[AtlasWidth * AtlasHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            // 绘制 7 大核心坐标系矢量图标
            DrawTile(pixels, INDEX_INERTIAL, DrawInertialFrame);
            DrawTile(pixels, INDEX_BODY_FIXED, DrawBodyFixedFrame);
            DrawTile(pixels, INDEX_ORBITAL, DrawOrbitalFrame);
            DrawTile(pixels, INDEX_LAGRANGE, DrawLagrangeFrame);
            DrawTile(pixels, INDEX_TARGET, DrawTargetFrame);
            DrawTile(pixels, INDEX_SURFACE, DrawSurfaceFrame);
            DrawTile(pixels, INDEX_BODY_DIRECTION, DrawBodyDirectionFrame);

            tex.SetPixels(pixels);
            tex.Apply(true, false);
            _cachedAtlas = tex;
            return _cachedAtlas;
        }

        private static void DrawTile(Color[] pixels, int index, Action<Rasterizer> drawer)
        {
            int col = index % Columns;
            int rowFromTop = index / Columns;
            int originX = col * TileSize;
            int originY = (Rows - 1 - rowFromTop) * TileSize;

            var rast = new Rasterizer(pixels, originX, originY, TileSize, AtlasWidth, AtlasHeight);
            drawer(rast);
            rast.Flush();
        }

        #region Vector Icon Drawers

        // =========================================================================
        // 1. 惯性坐标系 (Inertial Frame)
        // =========================================================================
        private static void DrawInertialFrame(Rasterizer r)
        {
            // 三维空间右手正交惯性笛卡尔坐标三脚架 (3D Orthogonal Inertial Triad Frame)
            int ox = 64, oy = 50;

            // 1. 垂直天顶主极轴 (+Z 轴，北天极恒星基准)
            r.DrawArrow(ox, oy, ox, 116, 20f, 26f, 1.0f, 8.5f);

            // 2. 斜右下主坐标轴 (+X 轴，春分点基准)
            r.DrawArrow(ox, oy, 114, 18, 20f, 26f, 1.0f, 8.0f);

            // 3. 斜左下正交坐标轴 (+Y 轴，空间正交补全)
            r.DrawArrow(ox, oy, 14, 18, 20f, 26f, 1.0f, 8.0f);

            // 4. 坐标原点质心高亮实心球核
            r.FillCircle(ox, oy, 9.5f, 1.0f);

            // 5. 左上与右上象限空间不动恒星 (Fixed Sidereal Stars)
            r.DrawStar(24, 96, 12, 1.0f);
            r.DrawStar(104, 96, 12, 1.0f);
        }

        // =========================================================================
        // 2. 体固坐标系 (Body-Centred Body-Fixed / Rotating Frame / ECEF)
        // =========================================================================
        private static void DrawBodyFixedFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 1. 中心行星自转球体轮廓与赤道经纬线
            r.DrawCircle(cx, cy, 34, 1.0f, 7.5f);

            // 南北贯通自转极轴
            r.DrawLine(cx, 16, cx, 112, 1.0f, 6.5f);

            // 赤道与主经线
            r.DrawLine(cx - 31, cy, cx + 31, cy, 1.0f, 7.5f);
            r.DrawEllipse(cx, cy, 14, 34, 0.90f, 6.0f);

            // 2. 环绕行星的粗自转动量弧与大箭头 (Spin Rotation Ring & Arrow ω)
            r.DrawArc(cx, cy, 48, -25f, 185f, 1.0f, 8.5f);
            // 自转箭头
            float headRad = -25f * Mathf.Deg2Rad;
            float ax = cx + Mathf.Cos(headRad) * 48f;
            float ay = cy + Mathf.Sin(headRad) * 48f;
            r.DrawArrow(ax - 12, ay - 6, ax + 8, ay + 6, 18f, 28f, 1.0f, 7.5f);
        }

        // =========================================================================
        // 3. 轨道坐标系 (Orbital Frame / LVLH)
        // =========================================================================
        private static void DrawOrbitalFrame(Rasterizer r)
        {
            // 1. 引力焦点中心主天体 (Focus Body at left focal point)
            int focusX = 42, focusY = 64;
            r.FillCircle(focusX, focusY, 18, 1.0f);
            r.DrawCircle(focusX, focusY, 24, 0.65f, 4.5f);

            // 2. 粗开普勒闭合椭圆轨道 (Keplerian Orbit Ring)
            int orbCenterX = 62, orbCenterY = 64;
            r.DrawEllipse(orbCenterX, orbCenterY, 50, 28, 1.0f, 8.0f);

            // 3. 轨道航天器节点与切向顺向速度箭头 (Spacecraft Node & Prograde Vector)
            int scX = 68, scY = 92;
            r.FillDiamond(scX, scY, 10f, 1.0f);

            // 切向速度前向箭头
            r.DrawArrow(scX + 6, scY, 116, scY, 18f, 28f, 1.0f, 7.5f);
        }

        // =========================================================================
        // 4. 拉格朗日点坐标系 (Lagrange Point / Barycentric Frame)
        // =========================================================================
        private static void DrawLagrangeFrame(Rasterizer r)
        {
            int m1X = 36, m1Y = 64; // Primary Body (M1)
            int m2X = 94, m2Y = 64; // Secondary Body (M2)

            // 1. 基线坐标轴与双天体 (Connecting Axis & Masses M1, M2)
            r.DrawLine(12, 64, 116, 64, 0.65f, 4.5f);

            // 2. 洛希瓣等势面哑铃双叶轮廓 (Roche Lobe Equipotential Curves)
            r.DrawDumbbellContour(m1X, m1Y, m2X, m2Y, 1.0f, 7.0f);

            // 3. 主次双天体高光实心球核
            r.FillCircle(m1X, m1Y, 18, 1.0f);
            r.FillCircle(m2X, m2Y, 12, 1.0f);

            // 4. 平动点：L1 (两体质心平衡点), L4 (+60°), L5 (-60°)
            r.FillDiamond(65, 64, 7.5f, 1.0f);
            r.FillDiamond(65, 106, 7.5f, 1.0f);
            r.FillDiamond(65, 22, 7.5f, 1.0f);

            // 5. 等边三角形稳定性导引粗虚线
            r.DrawDashedLine(m1X, m1Y, 65, 106, 5, 4, 0.65f, 4.0f);
            r.DrawDashedLine(m2X, m2Y, 65, 106, 5, 4, 0.65f, 4.0f);
            r.DrawDashedLine(m1X, m1Y, 65, 22, 5, 4, 0.65f, 4.0f);
            r.DrawDashedLine(m2X, m2Y, 65, 22, 5, 4, 0.65f, 4.0f);
        }

        // =========================================================================
        // 5. 目标坐标系 (Target Frame)
        // =========================================================================
        private static void DrawTargetFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 1. 粗瞄准标度主环 (Tactical Targeting Reticle)
            r.DrawCircle(cx, cy, 38, 1.0f, 8.0f);

            // 2. 上下左右四向穿透雷达测距刻度线 (Four Quadrant Ticks)
            r.DrawLine(cx, 96, cx, 118, 1.0f, 8.0f);
            r.DrawLine(cx, 10, cx, 32, 1.0f, 8.0f);
            r.DrawLine(10, cy, 32, cy, 1.0f, 8.0f);
            r.DrawLine(96, cy, 118, cy, 1.0f, 8.0f);

            // 3. 核心对接对中实心菱形 (Docking Target Center)
            r.FillDiamond(cx, cy, 9.5f, 1.0f);

            // 4. 相对交会视线导引箭头 (Relative Approach Vector)
            r.DrawArrow(26, 26, 52, 52, 16f, 28f, 1.0f, 7.5f);
        }

        // =========================================================================
        // 6. 地表坐标系 (Local Topocentric / Surface Horizon Frame)
        // =========================================================================
        private static void DrawSurfaceFrame(Rasterizer r)
        {
            int cx = 64;
            int groundY = 38;

            // 1. 坚实的大地水平基线 (Earth Surface Baseline)
            r.DrawLine(12, groundY, 116, groundY, 1.0f, 9.0f);

            // 2. 地下地质斜向阴影标线 (Subsurface Ground Hatch)
            for (int hx = 26; hx <= 102; hx += 18)
            {
                r.DrawLine(hx, groundY - 4, hx - 10, groundY - 18, 0.85f, 5.5f);
            }

            // 3. 地表发射台/测站实心底座 (Launch Pad Station Base)
            r.FillTriangle(48, groundY, 80, groundY, 64, groundY + 14, 1.0f);

            // 4. 局部铅垂天顶粗箭头 (+Up / Zenith Vector)
            r.DrawArrow(cx, groundY + 10, cx, 116, 20f, 26f, 1.0f, 8.5f);

            // 5. 局部地平切向水平速度粗箭头 (+Horizon Vector)
            r.DrawArrow(cx + 6, groundY + 18, 110, groundY + 18, 18f, 26f, 1.0f, 7.5f);
        }

        // =========================================================================
        // 7. 天体定向坐标系 (Body Direction Frame)
        // =========================================================================
        private static void DrawBodyDirectionFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 中心天体实心核与外环
            r.FillCircle(cx, cy, 19, 1.0f);
            r.DrawCircle(cx, cy, 25, 0.45f, 4.0f);

            // 定向射线与指向箭头
            r.DrawArrow(cx, cy, cx + 46, cy - 34, 18f, 26f, 1.0f, 7.5f);
            r.DrawDashedLine(cx - 34, cy + 26, cx, cy, 5, 4, 0.65f, 4.0f);

            // 目标天体实心核
            r.FillCircle(cx + 46, cy - 34, 10, 1.0f);
        }

        #endregion

        #region Procedural Rasterizer Engine

        /// <summary>
        /// 4x 超采样抗锯齿 (4x SSAA) 与单次解析几何光栅化内核
        /// 先在 512x512 高清缓冲区上进行单次包围盒解析距离场绘制（采用 Mathf.Max 彻底消除多次步进过饱和），
        /// 随后通过 4x4 区域盒状滤波 (Area Box Filtering) 均值降采样至 128x128 图集切片，输出丝滑无阶梯边缘。
        /// </summary>
        private class Rasterizer
        {
            public const int SsaaScale = 4;
            private const int HiSize = TileSize * SsaaScale; // 128 * 4 = 512
            private static readonly float[] _sharedHiBuffer = new float[HiSize * HiSize];

            private readonly Color[] _atlasPixels;
            private readonly int _ox, _oy, _size, _atlasW, _atlasH;

            public Rasterizer(Color[] pixels, int ox, int oy, int size, int atlasW, int atlasH)
            {
                _atlasPixels = pixels;
                _ox = ox;
                _oy = oy;
                _size = size;
                _atlasW = atlasW;
                _atlasH = atlasH;
                Array.Clear(_sharedHiBuffer, 0, _sharedHiBuffer.Length);
            }

            public void Flush()
            {
                Color baseColor = WidgetStyleManager.NeutralOpaque;
                float invSamples = 1.0f / (SsaaScale * SsaaScale);

                for (int ty = 0; ty < _size; ty++)
                {
                    int gy = _oy + ty;
                    if (gy < 0 || gy >= _atlasH) continue;
                    int rowBase = gy * _atlasW;
                    int hiRowStart = ty * SsaaScale;

                    for (int tx = 0; tx < _size; tx++)
                    {
                        int gx = _ox + tx;
                        if (gx < 0 || gx >= _atlasW) continue;

                        float sumAlpha = 0f;
                        int hiColStart = tx * SsaaScale;

                        for (int sy = 0; sy < SsaaScale; sy++)
                        {
                            int hiY = hiRowStart + sy;
                            int hiOffset = hiY * HiSize + hiColStart;
                            sumAlpha += _sharedHiBuffer[hiOffset]
                                      + _sharedHiBuffer[hiOffset + 1]
                                      + _sharedHiBuffer[hiOffset + 2]
                                      + _sharedHiBuffer[hiOffset + 3];
                        }

                        float finalAlpha = Mathf.Clamp01(sumAlpha * invSamples);
                        if (finalAlpha > 0.001f)
                        {
                            _atlasPixels[rowBase + gx] = new Color(baseColor.r, baseColor.g, baseColor.b, finalAlpha);
                        }
                    }
                }
            }

            private void SetSubPixel(int hx, int hy, float alpha)
            {
                if (hx < 0 || hx >= HiSize || hy < 0 || hy >= HiSize) return;
                int idx = hy * HiSize + hx;
                if (alpha > _sharedHiBuffer[idx])
                {
                    _sharedHiBuffer[idx] = alpha;
                }
            }

            public void DrawLine(float x0, float y0, float x1, float y1, float alpha, float width = 1.0f)
            {
                float hx0 = x0 * SsaaScale;
                float hy0 = y0 * SsaaScale;
                float hx1 = x1 * SsaaScale;
                float hy1 = y1 * SsaaScale;
                float hw = (width * SsaaScale) * 0.5f;

                float minX = Mathf.Min(hx0, hx1) - hw - 2f;
                float maxX = Mathf.Max(hx0, hx1) + hw + 2f;
                float minY = Mathf.Min(hy0, hy1) - hw - 2f;
                float maxY = Mathf.Max(hy0, hy1) + hw + 2f;

                int iMinX = Mathf.Clamp(Mathf.FloorToInt(minX), 0, HiSize - 1);
                int iMaxX = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, HiSize - 1);
                int iMinY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, HiSize - 1);
                int iMaxY = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, HiSize - 1);

                float dx = hx1 - hx0;
                float dy = hy1 - hy0;
                float lenSq = dx * dx + dy * dy;

                for (int y = iMinY; y <= iMaxY; y++)
                {
                    float py = y + 0.5f;
                    for (int x = iMinX; x <= iMaxX; x++)
                    {
                        float px = x + 0.5f;
                        float dist;
                        if (lenSq < 0.0001f)
                        {
                            float ex = px - hx0;
                            float ey = py - hy0;
                            dist = Mathf.Sqrt(ex * ex + ey * ey);
                        }
                        else
                        {
                            float t = Mathf.Clamp01(((px - hx0) * dx + (py - hy0) * dy) / lenSq);
                            float projX = hx0 + t * dx;
                            float projY = hy0 + t * dy;
                            float ex = px - projX;
                            float ey = py - projY;
                            dist = Mathf.Sqrt(ex * ex + ey * ey);
                        }

                        float delta = dist - hw;
                        if (delta <= 0.5f)
                        {
                            float cov = Mathf.Clamp01(0.5f - delta) * alpha;
                            SetSubPixel(x, y, cov);
                        }
                    }
                }
            }

            public void DrawDashedLine(float x0, float y0, float x1, float y1, float dashLen, float gapLen, float alpha, float width = 1.0f)
            {
                float dx = x1 - x0;
                float dy = y1 - y0;
                float totalLen = Mathf.Sqrt(dx * dx + dy * dy);
                if (totalLen < 0.001f) return;

                float cur = 0f;
                bool isDash = true;
                while (cur < totalLen)
                {
                    float next = cur + (isDash ? dashLen : gapLen);
                    if (next > totalLen) next = totalLen;

                    if (isDash)
                    {
                        float t0 = cur / totalLen;
                        float t1 = next / totalLen;
                        DrawLine(Mathf.Lerp(x0, x1, t0), Mathf.Lerp(y0, y1, t0), Mathf.Lerp(x0, x1, t1), Mathf.Lerp(y0, y1, t1), alpha, width);
                    }
                    cur = next;
                    isDash = !isDash;
                }
            }

            public void DrawCircle(float cx, float cy, float radius, float alpha, float width = 1.2f)
            {
                float hcx = cx * SsaaScale;
                float hcy = cy * SsaaScale;
                float hr = radius * SsaaScale;
                float hw = (width * SsaaScale) * 0.5f;

                int iMinX = Mathf.Clamp(Mathf.FloorToInt(hcx - hr - hw - 2f), 0, HiSize - 1);
                int iMaxX = Mathf.Clamp(Mathf.CeilToInt(hcx + hr + hw + 2f), 0, HiSize - 1);
                int iMinY = Mathf.Clamp(Mathf.FloorToInt(hcy - hr - hw - 2f), 0, HiSize - 1);
                int iMaxY = Mathf.Clamp(Mathf.CeilToInt(hcy + hr + hw + 2f), 0, HiSize - 1);

                for (int y = iMinY; y <= iMaxY; y++)
                {
                    float py = y + 0.5f;
                    float dy = py - hcy;
                    for (int x = iMinX; x <= iMaxX; x++)
                    {
                        float px = x + 0.5f;
                        float dx = px - hcx;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float delta = Mathf.Abs(d - hr) - hw;
                        if (delta <= 0.5f)
                        {
                            float cov = Mathf.Clamp01(0.5f - delta) * alpha;
                            SetSubPixel(x, y, cov);
                        }
                    }
                }
            }

            public void FillCircle(float cx, float cy, float radius, float alpha)
            {
                float hcx = cx * SsaaScale;
                float hcy = cy * SsaaScale;
                float hr = radius * SsaaScale;

                int iMinX = Mathf.Clamp(Mathf.FloorToInt(hcx - hr - 2f), 0, HiSize - 1);
                int iMaxX = Mathf.Clamp(Mathf.CeilToInt(hcx + hr + 2f), 0, HiSize - 1);
                int iMinY = Mathf.Clamp(Mathf.FloorToInt(hcy - hr - 2f), 0, HiSize - 1);
                int iMaxY = Mathf.Clamp(Mathf.CeilToInt(hcy + hr + 2f), 0, HiSize - 1);

                for (int y = iMinY; y <= iMaxY; y++)
                {
                    float py = y + 0.5f;
                    float dy = py - hcy;
                    for (int x = iMinX; x <= iMaxX; x++)
                    {
                        float px = x + 0.5f;
                        float dx = px - hcx;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float delta = d - hr;
                        if (delta <= 0.5f)
                        {
                            float cov = Mathf.Clamp01(0.5f - delta) * alpha;
                            SetSubPixel(x, y, cov);
                        }
                    }
                }
            }

            public void DrawArc(float cx, float cy, float radius, float startDeg, float endDeg, float alpha, float width = 1.4f)
            {
                float span = endDeg - startDeg;
                int count = Mathf.Max(16, Mathf.CeilToInt(Mathf.Abs(span) / 2.5f));
                float prevX = cx + Mathf.Cos(startDeg * Mathf.Deg2Rad) * radius;
                float prevY = cy + Mathf.Sin(startDeg * Mathf.Deg2Rad) * radius;

                for (int i = 1; i <= count; i++)
                {
                    float t = (float)i / count;
                    float deg = Mathf.Lerp(startDeg, endDeg, t);
                    float rad = deg * Mathf.Deg2Rad;
                    float curX = cx + Mathf.Cos(rad) * radius;
                    float curY = cy + Mathf.Sin(rad) * radius;
                    DrawLine(prevX, prevY, curX, curY, alpha, width);
                    prevX = curX;
                    prevY = curY;
                }
            }

            public void DrawEllipse(float cx, float cy, float rx, float ry, float alpha, float width = 1.2f)
            {
                int steps = 96;
                float prevX = cx + rx;
                float prevY = cy;

                for (int i = 1; i <= steps; i++)
                {
                    float rad = (i * 2f * Mathf.PI) / steps;
                    float curX = cx + Mathf.Cos(rad) * rx;
                    float curY = cy + Mathf.Sin(rad) * ry;
                    DrawLine(prevX, prevY, curX, curY, alpha, width);
                    prevX = curX;
                    prevY = curY;
                }
            }

            public void DrawArrow(float x0, float y0, float x1, float y1, float headLen, float headAngleDeg, float alpha, float width = 2.0f)
            {
                DrawLine(x0, y0, x1, y1, alpha, width);

                float dx = x1 - x0;
                float dy = y1 - y0;
                float angle = Mathf.Atan2(dy, dx);
                float a1 = angle + Mathf.PI - headAngleDeg * Mathf.Deg2Rad;
                float a2 = angle + Mathf.PI + headAngleDeg * Mathf.Deg2Rad;

                float hx1 = x1 + Mathf.Cos(a1) * headLen;
                float hy1 = y1 + Mathf.Sin(a1) * headLen;
                float hx2 = x1 + Mathf.Cos(a2) * headLen;
                float hy2 = y1 + Mathf.Sin(a2) * headLen;

                FillTriangle(x1, y1, hx1, hy1, hx2, hy2, alpha);
                DrawLine(x1, y1, hx1, hy1, alpha, width);
                DrawLine(x1, y1, hx2, hy2, alpha, width);
                DrawLine(hx1, hy1, hx2, hy2, alpha, width);
            }

            public void FillTriangle(float x0, float y0, float x1, float y1, float x2, float y2, float alpha)
            {
                float hx0 = x0 * SsaaScale;
                float hy0 = y0 * SsaaScale;
                float hx1 = x1 * SsaaScale;
                float hy1 = y1 * SsaaScale;
                float hx2 = x2 * SsaaScale;
                float hy2 = y2 * SsaaScale;

                int iMinX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(hx0, Mathf.Min(hx1, hx2)) - 2f), 0, HiSize - 1);
                int iMaxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(hx0, Mathf.Max(hx1, hx2)) + 2f), 0, HiSize - 1);
                int iMinY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(hy0, Mathf.Min(hy1, hy2)) - 2f), 0, HiSize - 1);
                int iMaxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(hy0, Mathf.Max(hy1, hy2)) + 2f), 0, HiSize - 1);

                float cross = (hx1 - hx0) * (hy2 - hy0) - (hy1 - hy0) * (hx2 - hx0);
                if (Mathf.Abs(cross) < 0.001f) return;
                float sign = cross > 0 ? 1f : -1f;

                float len0 = Mathf.Sqrt((hx1 - hx0) * (hx1 - hx0) + (hy1 - hy0) * (hy1 - hy0));
                float len1 = Mathf.Sqrt((hx2 - hx1) * (hx2 - hx1) + (hy2 - hy1) * (hy2 - hy1));
                float len2 = Mathf.Sqrt((hx0 - hx2) * (hx0 - hx2) + (hy0 - hy2) * (hy0 - hy2));
                if (len0 < 0.001f || len1 < 0.001f || len2 < 0.001f) return;

                for (int y = iMinY; y <= iMaxY; y++)
                {
                    float py = y + 0.5f;
                    for (int x = iMinX; x <= iMaxX; x++)
                    {
                        float px = x + 0.5f;

                        float d0 = ((hx1 - hx0) * (py - hy0) - (hy1 - hy0) * (px - hx0)) / len0 * sign;
                        float d1 = ((hx2 - hx1) * (py - hy1) - (hy2 - hy1) * (px - hx1)) / len1 * sign;
                        float d2 = ((hx0 - hx2) * (py - hy2) - (hy0 - hy2) * (px - hx2)) / len2 * sign;

                        float minD = Mathf.Min(d0, Mathf.Min(d1, d2));
                        float delta = -minD;
                        if (delta <= 0.5f)
                        {
                            float cov = Mathf.Clamp01(0.5f - delta) * alpha;
                            SetSubPixel(x, y, cov);
                        }
                    }
                }
            }

            public void FillDiamond(float cx, float cy, float size, float alpha)
            {
                float hcx = cx * SsaaScale;
                float hcy = cy * SsaaScale;
                float hs = size * SsaaScale;

                int iMinX = Mathf.Clamp(Mathf.FloorToInt(hcx - hs - 2f), 0, HiSize - 1);
                int iMaxX = Mathf.Clamp(Mathf.CeilToInt(hcx + hs + 2f), 0, HiSize - 1);
                int iMinY = Mathf.Clamp(Mathf.FloorToInt(hcy - hs - 2f), 0, HiSize - 1);
                int iMaxY = Mathf.Clamp(Mathf.CeilToInt(hcy + hs + 2f), 0, HiSize - 1);

                const float invSqrt2 = 0.70710678f;

                for (int y = iMinY; y <= iMaxY; y++)
                {
                    float py = y + 0.5f;
                    float dy = Mathf.Abs(py - hcy);
                    for (int x = iMinX; x <= iMaxX; x++)
                    {
                        float px = x + 0.5f;
                        float dx = Mathf.Abs(px - hcx);
                        float delta = (dx + dy - hs) * invSqrt2;
                        if (delta <= 0.5f)
                        {
                            float cov = Mathf.Clamp01(0.5f - delta) * alpha;
                            SetSubPixel(x, y, cov);
                        }
                    }
                }
            }

            public void DrawStar(float cx, float cy, float r, float alpha)
            {
                FillCircle(cx, cy, 4.0f, alpha);
                DrawLine(cx - r, cy, cx + r, cy, alpha, 4.0f);
                DrawLine(cx, cy - r, cx, cy + r, alpha, 4.0f);
            }

            public void DrawDumbbellContour(float m1X, float m1Y, float m2X, float m2Y, float alpha, float width = 5.0f)
            {
                int steps = 140;
                float prevX = 0, prevY = 0;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (i * 2f * Mathf.PI) / steps;
                    float baseR1 = 25f;
                    float baseR2 = 17f;
                    float px, py;

                    if (t <= Mathf.PI)
                    {
                        float u = t / Mathf.PI;
                        float cx = Mathf.Lerp(m1X, m2X, u);
                        float cy = m1Y;
                        float rSpan = Mathf.Lerp(baseR1, baseR2, u);
                        float waist = 1f - 0.45f * Mathf.Sin(u * Mathf.PI);
                        px = cx + Mathf.Cos(t) * rSpan;
                        py = cy + Mathf.Sin(t) * (rSpan * waist);
                    }
                    else
                    {
                        float u = (t - Mathf.PI) / Mathf.PI;
                        float cx = Mathf.Lerp(m2X, m1X, u);
                        float cy = m1Y;
                        float rSpan = Mathf.Lerp(baseR2, baseR1, u);
                        float waist = 1f - 0.45f * Mathf.Sin(u * Mathf.PI);
                        px = cx + Mathf.Cos(t) * rSpan;
                        py = cy - Mathf.Sin(t) * (rSpan * waist);
                    }

                    if (i > 0) DrawLine(prevX, prevY, px, py, alpha, width);
                    prevX = px;
                    prevY = py;
                }
            }
        }

        #endregion
    }
}
