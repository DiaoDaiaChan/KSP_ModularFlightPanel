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
            { "HELIOCENTRIC", INDEX_INERTIAL },
            { "HELIOCENTRIC_INERTIAL", INDEX_INERTIAL },
            { "J2000", INDEX_INERTIAL },
            { "ECI", INDEX_INERTIAL },
            { "ICRS", INDEX_INERTIAL },

            { "BODY_FIXED", INDEX_BODY_FIXED },
            { "BODY_SURFACE", INDEX_BODY_FIXED },
            { "ROTATING", INDEX_BODY_FIXED },
            { "ECEF", INDEX_BODY_FIXED },

            { "SURFACE", INDEX_SURFACE },
            { "GROUND", INDEX_SURFACE },
            { "TOPOCENTRIC", INDEX_SURFACE },
            { "HORIZON", INDEX_SURFACE },

            { "ORBIT", INDEX_ORBITAL },
            { "ORBITAL", INDEX_ORBITAL },
            { "LVLH", INDEX_ORBITAL },
            { "FRENET", INDEX_ORBITAL },
            { "PERIFOCAL", INDEX_ORBITAL },
            { "VVLH", INDEX_ORBITAL },

            { "LAGRANGE", INDEX_LAGRANGE },
            { "BARYCENTRIC", INDEX_LAGRANGE },
            { "PULSATING", INDEX_LAGRANGE },
            { "PULSATING_BARYCENTRIC", INDEX_LAGRANGE },
            { "ROTATING_BARYCENTRIC", INDEX_LAGRANGE },
            { "ROTATING_PULSATING", INDEX_LAGRANGE },
            { "THREE_BODY", INDEX_LAGRANGE },
            { "L1", INDEX_LAGRANGE },
            { "L2", INDEX_LAGRANGE },
            { "L3", INDEX_LAGRANGE },
            { "L4", INDEX_LAGRANGE },
            { "L5", INDEX_LAGRANGE },

            { "TARGET", INDEX_TARGET },
            { "TARGET_ORBITAL", INDEX_TARGET },
            { "DOCKING", INDEX_TARGET },
            { "RELATIVE", INDEX_TARGET },

            { "BODY_DIRECTION", INDEX_BODY_DIRECTION },
            { "PARENT_DIRECTION", INDEX_BODY_DIRECTION }
        };

        public static int GetIconIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return INDEX_INERTIAL;
            name = name.Trim().ToUpperInvariant();

            if (FrameNameToIndex.TryGetValue(name, out int idx)) return idx;

            if (name.Contains("BARYCENTRIC") || name.Contains("LAGRANGE") || name.Contains("PULSATING") || name.Contains("L1") || name.Contains("L2") || name.Contains("L3") || name.Contains("L4") || name.Contains("L5") || name.Contains("L点"))
                return INDEX_LAGRANGE;
            if (name.Contains("FIXED") || name.Contains("ROTATING") || name.Contains("ECEF") || name.Contains("体固"))
                return INDEX_BODY_FIXED;
            if (name.Contains("SURFACE") || name.Contains("GROUND") || name.Contains("地表"))
                return INDEX_SURFACE;
            if (name.Contains("ORBIT") || name.Contains("LVLH") || name.Contains("FRENET") || name.Contains("轨道"))
                return INDEX_ORBITAL;
            if (name.Contains("TARGET") || name.Contains("DOCK") || name.Contains("目标"))
                return INDEX_TARGET;
            if (name.Contains("INERTIAL") || name.Contains("NON_ROTATING") || name.Contains("惯性"))
                return INDEX_INERTIAL;
            if (name.Contains("BODY_DIRECTION") || name.Contains("PARENT_DIRECTION"))
                return INDEX_BODY_DIRECTION;

            return INDEX_INERTIAL;
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

            Texture2D tex = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false);
            tex.name = "MFP_AvionicsReferenceFrameIconAtlas";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

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
            tex.Apply(false, true);
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
        }

        #region Vector Icon Drawers

        // =========================================================================
        // 1. 惯性坐标系 (Inertial Frame)
        // =========================================================================
        private static void DrawInertialFrame(Rasterizer r)
        {
            // 三维空间右手正交惯性笛卡尔坐标三脚架 (3D Orthogonal Inertial Triad Frame)
            int ox = 64, oy = 52;

            // 1. 垂直天顶主极轴 (+Z 轴，北天极恒星基准)
            r.DrawArrow(ox, oy, ox, 114, 18f, 26f, 1.0f, 7.0f);

            // 2. 斜右下主坐标轴 (+X 轴，春分点基准)
            r.DrawArrow(ox, oy, 110, 22, 17f, 26f, 1.0f, 6.5f);

            // 3. 斜左下正交坐标轴 (+Y 轴，空间正交补全)
            r.DrawArrow(ox, oy, 18, 22, 17f, 26f, 1.0f, 6.5f);

            // 4. 坐标原点质心高亮实心球核
            r.FillCircle(ox, oy, 7.5f, 1.0f);

            // 5. 左上与右上象限空间不动恒星 (Fixed Sidereal Stars)
            r.DrawStar(26, 96, 9, 0.95f);
            r.DrawStar(102, 96, 9, 0.95f);
        }

        // =========================================================================
        // 2. 体固坐标系 (Body-Centred Body-Fixed / Rotating Frame / ECEF)
        // =========================================================================
        private static void DrawBodyFixedFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 1. 中心行星自转球体轮廓与赤道
            r.FillCircle(cx, cy, 34, 0.15f);
            r.DrawCircle(cx, cy, 34, 0.95f, 6.0f);

            // 南北贯通自转极轴
            r.DrawLine(cx, 16, cx, 112, 0.75f, 4.0f);

            // 赤道与主经线
            r.DrawLine(cx - 31, cy, cx + 31, cy, 0.90f, 5.0f);
            r.DrawEllipse(cx, cy, 14, 34, 0.55f, 4.0f);

            // 2. 环绕行星的粗自转动量弧与大箭头 (Spin Rotation Ring & Arrow ω)
            r.DrawArc(cx, cy, 48, -25f, 185f, 1.0f, 7.0f);
            // 自转箭头
            float headRad = -25f * Mathf.Deg2Rad;
            float ax = cx + Mathf.Cos(headRad) * 48f;
            float ay = cy + Mathf.Sin(headRad) * 48f;
            r.DrawArrow(ax - 12, ay - 6, ax + 8, ay + 6, 16f, 28f, 1.0f, 6.0f);
        }

        // =========================================================================
        // 3. 轨道坐标系 (Orbital Frame / LVLH)
        // =========================================================================
        private static void DrawOrbitalFrame(Rasterizer r)
        {
            // 1. 引力焦点中心主天体 (Focus Body at left focal point)
            int focusX = 44, focusY = 64;
            r.FillCircle(focusX, focusY, 16, 1.0f);
            r.DrawCircle(focusX, focusY, 22, 0.35f, 3.0f);

            // 2. 粗开普勒闭合椭圆轨道 (Keplerian Orbit Ring)
            int orbCenterX = 62, orbCenterY = 64;
            r.DrawEllipse(orbCenterX, orbCenterY, 50, 28, 0.95f, 6.0f);

            // 3. 轨道航天器节点与切向顺向速度箭头 (Spacecraft Node & Prograde Vector)
            int scX = 68, scY = 92;
            r.FillDiamond(scX, scY, 7.5f, 1.0f);

            // 切向速度前向箭头
            r.DrawArrow(scX + 6, scY, 112, scY, 15f, 26f, 1.0f, 5.5f);
        }

        // =========================================================================
        // 4. 拉格朗日点坐标系 (Lagrange Point / Barycentric Frame)
        // =========================================================================
        private static void DrawLagrangeFrame(Rasterizer r)
        {
            int m1X = 36, m1Y = 64; // Primary Body (M1)
            int m2X = 94, m2Y = 64; // Secondary Body (M2)

            // 1. 基线坐标轴与双天体 (Connecting Axis & Masses M1, M2)
            r.DrawLine(12, 64, 116, 64, 0.40f, 3.0f);

            // 2. 洛希瓣等势面哑铃双叶轮廓 (Roche Lobe Equipotential Curves)
            r.DrawDumbbellContour(m1X, m1Y, m2X, m2Y, 0.85f, 5.0f);

            // 3. 主次双天体高光实心球核
            r.FillCircle(m1X, m1Y, 16, 1.0f);
            r.FillCircle(m2X, m2Y, 10, 1.0f);

            // 4. 平动点：L1 (两体质心平衡点), L4 (+60°), L5 (-60°)
            r.FillDiamond(65, 64, 6f, 1.0f);
            r.FillDiamond(65, 106, 6f, 1.0f);
            r.FillDiamond(65, 22, 6f, 1.0f);

            // 5. 等边三角形稳定性导引虚线
            r.DrawDashedLine(m1X, m1Y, 65, 106, 5, 4, 0.40f, 2.0f);
            r.DrawDashedLine(m2X, m2Y, 65, 106, 5, 4, 0.40f, 2.0f);
            r.DrawDashedLine(m1X, m1Y, 65, 22, 5, 4, 0.40f, 2.0f);
            r.DrawDashedLine(m2X, m2Y, 65, 22, 5, 4, 0.40f, 2.0f);
        }

        // =========================================================================
        // 5. 目标坐标系 (Target Frame)
        // =========================================================================
        private static void DrawTargetFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 1. 粗瞄准标度主环 (Tactical Targeting Reticle)
            r.DrawCircle(cx, cy, 38, 0.90f, 6.0f);

            // 2. 上下左右四向穿透雷达测距刻度线 (Four Quadrant Ticks)
            r.DrawLine(cx, 96, cx, 116, 1.0f, 6.0f);
            r.DrawLine(cx, 12, cx, 32, 1.0f, 6.0f);
            r.DrawLine(12, cy, 32, cy, 1.0f, 6.0f);
            r.DrawLine(96, cy, 116, cy, 1.0f, 6.0f);

            // 3. 核心对接对中实心菱形 (Docking Target Center)
            r.FillDiamond(cx, cy, 9f, 1.0f);

            // 4. 相对交会视线导引箭头 (Relative Approach Vector)
            r.DrawArrow(28, 28, 52, 52, 14f, 28f, 1.0f, 5.5f);
        }

        // =========================================================================
        // 6. 地表坐标系 (Local Topocentric / Surface Horizon Frame)
        // =========================================================================
        private static void DrawSurfaceFrame(Rasterizer r)
        {
            int cx = 64;
            int groundY = 38;

            // 1. 坚实的大地水平基线 (Earth Surface Baseline)
            r.DrawLine(14, groundY, 114, groundY, 1.0f, 7.0f);

            // 2. 地下地质斜向阴影标线 (Subsurface Ground Hatch)
            for (int hx = 28; hx <= 100; hx += 18)
            {
                r.DrawLine(hx, groundY - 4, hx - 10, groundY - 18, 0.60f, 4.0f);
            }

            // 3. 地表发射台/测站实心底座 (Launch Pad Station Base)
            r.FillTriangle(50, groundY, 78, groundY, 64, groundY + 12, 1.0f);

            // 4. 局部铅垂天顶粗箭头 (+Up / Zenith Vector)
            r.DrawArrow(cx, groundY + 10, cx, 114, 18f, 26f, 1.0f, 7.0f);

            // 5. 局部地平切向水平速度粗箭头 (+Horizon Vector)
            r.DrawArrow(cx + 6, groundY + 18, 108, groundY + 18, 16f, 26f, 0.95f, 6.0f);
        }

        // =========================================================================
        // 7. 天体定向坐标系 (Body Direction Frame)
        // =========================================================================
        private static void DrawBodyDirectionFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 中心天体实心核与外环
            r.FillCircle(cx, cy, 18, 1.0f);
            r.DrawCircle(cx, cy, 24, 0.40f, 3.0f);

            // 定向射线与指向箭头
            r.DrawArrow(cx, cy, cx + 46, cy - 34, 16f, 26f, 1.0f, 6.0f);
            r.DrawDashedLine(cx - 34, cy + 26, cx, cy, 5, 4, 0.6f, 3.0f);

            // 目标天体实心核
            r.FillCircle(cx + 46, cy - 34, 9, 1.0f);
        }

        #endregion

        #region Procedural Rasterizer Engine

        /// <summary>
        /// 纯 C# 抗锯齿与数学图元像素光栅化器
        /// </summary>
        private class Rasterizer
        {
            private readonly Color[] _pixels;
            private readonly int _ox, _oy, _size, _atlasW, _atlasH;

            public Rasterizer(Color[] pixels, int ox, int oy, int size, int atlasW, int atlasH)
            {
                _pixels = pixels;
                _ox = ox;
                _oy = oy;
                _size = size;
                _atlasW = atlasW;
                _atlasH = atlasH;
            }

            public void SetPixel(int x, int y, float alpha)
            {
                if (x < 0 || x >= _size || y < 0 || y >= _size) return;
                int gx = _ox + x;
                int gy = _oy + y;
                if (gx < 0 || gx >= _atlasW || gy < 0 || gy >= _atlasH) return;

                int idx = gy * _atlasW + gx;
                float currentA = _pixels[idx].a;
                float newA = Mathf.Clamp01(currentA + alpha);
                _pixels[idx] = WidgetStyleManager.NeutralOpaque * newA;
            }

            public void DrawLine(float x0, float y0, float x1, float y1, float alpha, float width = 1.0f)
            {
                float dx = x1 - x0;
                float dy = y1 - y0;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 0.001f) { SetPixel(Mathf.RoundToInt(x0), Mathf.RoundToInt(y0), alpha); return; }

                float step = 0.5f;
                int count = Mathf.CeilToInt(len / step);
                float halfW = width * 0.5f;

                for (int i = 0; i <= count; i++)
                {
                    float t = (float)i / count;
                    float px = Mathf.Lerp(x0, x1, t);
                    float py = Mathf.Lerp(y0, y1, t);

                    if (width <= 1.2f)
                    {
                        int ix = Mathf.RoundToInt(px);
                        int iy = Mathf.RoundToInt(py);
                        SetPixel(ix, iy, alpha);
                    }
                    else
                    {
                        int minX = Mathf.FloorToInt(px - halfW);
                        int maxX = Mathf.CeilToInt(px + halfW);
                        int minY = Mathf.FloorToInt(py - halfW);
                        int maxY = Mathf.CeilToInt(py + halfW);

                        for (int y = minY; y <= maxY; y++)
                        {
                            for (int x = minX; x <= maxX; x++)
                            {
                                float dist = DistanceToSegment(x, y, x0, y0, x1, y1);
                                if (dist <= halfW)
                                {
                                    float a = Mathf.Clamp01((halfW - dist + 0.5f)) * alpha;
                                    SetPixel(x, y, a);
                                }
                            }
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

            public void DrawCircle(int cx, int cy, float radius, float alpha, float width = 1.2f)
            {
                float halfW = width * 0.5f;
                int minX = Mathf.FloorToInt(cx - radius - halfW - 1);
                int maxX = Mathf.CeilToInt(cx + radius + halfW + 1);
                int minY = Mathf.FloorToInt(cy - radius - halfW - 1);
                int maxY = Mathf.CeilToInt(cy + radius + halfW + 1);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float delta = Mathf.Abs(d - radius);
                        if (delta <= halfW + 0.5f)
                        {
                            float a = Mathf.Clamp01(halfW + 0.5f - delta) * alpha;
                            SetPixel(x, y, a);
                        }
                    }
                }
            }

            public void FillCircle(int cx, int cy, float radius, float alpha)
            {
                int minX = Mathf.FloorToInt(cx - radius - 1);
                int maxX = Mathf.CeilToInt(cx + radius + 1);
                int minY = Mathf.FloorToInt(cy - radius - 1);
                int maxY = Mathf.CeilToInt(cy + radius + 1);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d <= radius + 0.5f)
                        {
                            float a = Mathf.Clamp01(radius + 0.5f - d) * alpha;
                            SetPixel(x, y, a);
                        }
                    }
                }
            }

            public void DrawArc(int cx, int cy, float radius, float startDeg, float endDeg, float alpha, float width = 1.4f)
            {
                float step = 2.0f;
                float halfW = width * 0.5f;
                for (float deg = startDeg; deg <= endDeg; deg += step)
                {
                    float rad = deg * Mathf.Deg2Rad;
                    float px = cx + Mathf.Cos(rad) * radius;
                    float py = cy + Mathf.Sin(rad) * radius;

                    int minX = Mathf.FloorToInt(px - halfW);
                    int maxX = Mathf.CeilToInt(px + halfW);
                    int minY = Mathf.FloorToInt(py - halfW);
                    int maxY = Mathf.CeilToInt(py + halfW);

                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                            if (d <= halfW + 0.5f)
                            {
                                SetPixel(x, y, Mathf.Clamp01(halfW + 0.5f - d) * alpha);
                            }
                        }
                    }
                }
            }

            public void DrawEllipse(int cx, int cy, float rx, float ry, float alpha, float width = 1.2f)
            {
                float perimeter = 2f * Mathf.PI * Mathf.Sqrt((rx * rx + ry * ry) * 0.5f);
                int steps = Mathf.Max(64, Mathf.CeilToInt(perimeter * 1.5f));
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
                int minX = Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2)) - 1);
                int maxX = Mathf.CeilToInt(Mathf.Max(x0, Mathf.Max(x1, x2)) + 1);
                int minY = Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2)) - 1);
                int maxY = Mathf.CeilToInt(Mathf.Max(y0, Mathf.Max(y1, y2)) + 1);

                float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                if (Mathf.Abs(denom) < 0.0001f) return;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float w0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / denom;
                        float w1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / denom;
                        float w2 = 1.0f - w0 - w1;
                        if (w0 >= -0.02f && w1 >= -0.02f && w2 >= -0.02f)
                        {
                            SetPixel(x, y, alpha);
                        }
                    }
                }
            }

            public void FillDiamond(int cx, int cy, float size, float alpha)
            {
                int minX = Mathf.FloorToInt(cx - size - 1);
                int maxX = Mathf.CeilToInt(cx + size + 1);
                int minY = Mathf.FloorToInt(cy - size - 1);
                int maxY = Mathf.CeilToInt(cy + size + 1);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float manhattan = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                        if (manhattan <= size + 0.5f)
                        {
                            float a = Mathf.Clamp01(size + 0.5f - manhattan) * alpha;
                            SetPixel(x, y, a);
                        }
                    }
                }
            }

            public void DrawArrowHead(float tipX, float tipY, float headLen, float headAngleDeg, float alpha, float width = 2.0f)
            {
                float a1 = Mathf.PI - headAngleDeg * Mathf.Deg2Rad;
                float a2 = Mathf.PI + headAngleDeg * Mathf.Deg2Rad;
                float hx1 = tipX + Mathf.Cos(a1) * headLen;
                float hy1 = tipY + Mathf.Sin(a1) * headLen;
                float hx2 = tipX + Mathf.Cos(a2) * headLen;
                float hy2 = tipY + Mathf.Sin(a2) * headLen;
                FillTriangle(tipX, tipY, hx1, hy1, hx2, hy2, alpha);
                DrawLine(tipX, tipY, hx1, hy1, alpha, width);
                DrawLine(tipX, tipY, hx2, hy2, alpha, width);
                DrawLine(hx1, hy1, hx2, hy2, alpha, width);
            }

            public void DrawCrosshair(int cx, int cy, int size, float alpha, float width = 1.2f)
            {
                DrawLine(cx - size, cy, cx + size, cy, alpha, width);
                DrawLine(cx, cy - size, cx, cy + size, alpha, width);
            }

            public void DrawDiamond(int cx, int cy, int size, float alpha, float width = 1.2f)
            {
                DrawLine(cx, cy - size, cx + size, cy, alpha, width);
                DrawLine(cx + size, cy, cx, cy + size, alpha, width);
                DrawLine(cx, cy + size, cx - size, cy, alpha, width);
                DrawLine(cx - size, cy, cx, cy - size, alpha, width);
            }

            public void DrawStar(int cx, int cy, int r, float alpha)
            {
                FillCircle(cx, cy, 3.5f, alpha);
                DrawLine(cx - r, cy, cx + r, cy, alpha, 3.5f);
                DrawLine(cx, cy - r, cx, cy + r, alpha, 3.5f);
            }

            public void DrawDumbbellContour(int m1X, int m1Y, int m2X, int m2Y, float alpha, float width = 5.0f)
            {
                int steps = 120;
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

            public void DrawGlyph(int cx, int cy, char c, float alpha)
            {
                // 简洁高反差 3x5 点阵微文字体
                int x0 = cx - 1;
                int y0 = cy - 2;

                switch (char.ToUpperInvariant(c))
                {
                    case 'X':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'Y':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0 + 1, y0 + 2, alpha);
                        break;
                    case 'Z':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 1, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'V':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0 + 1, y0 + 2, alpha);
                        break;
                    case 'R':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'N':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 1, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case '1':
                        SetPixel(x0 + 1, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0 + 1, y0 + 2, alpha);
                        break;
                    case '2':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 1, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case '3':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 1, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case '4':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 1, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case '5':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 1, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'T':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0 + 1, y0 + 1, alpha);
                        SetPixel(x0 + 1, y0 + 2, alpha);
                        break;
                    case 'P':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha);
                        break;
                    case 'A':
                        SetPixel(x0 + 1, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 1, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'W':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 2, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 1, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 2, y0 + 2, alpha);
                        break;
                    case 'D':
                        SetPixel(x0, y0, alpha); SetPixel(x0 + 1, y0, alpha);
                        SetPixel(x0, y0 + 1, alpha); SetPixel(x0 + 2, y0 + 1, alpha);
                        SetPixel(x0, y0 + 2, alpha); SetPixel(x0 + 1, y0 + 2, alpha);
                        break;
                }
            }

            private static float DistanceToSegment(float px, float py, float x0, float y0, float x1, float y1)
            {
                float dx = x1 - x0;
                float dy = y1 - y0;
                float l2 = dx * dx + dy * dy;
                if (l2 < 0.0001f) return Mathf.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));
                float t = Mathf.Clamp01(((px - x0) * dx + (py - y0) * dy) / l2);
                float projX = x0 + t * dx;
                float projY = y0 + t * dy;
                return Mathf.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
            }
        }

        #endregion
    }
}
