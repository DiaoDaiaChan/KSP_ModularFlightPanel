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
            int cx = 64, cy = 64;

            // 1. 天球坐标外导轨环与赤道椭圆面 (Celestial Reference Ring & Equator Disc)
            r.DrawCircle(cx, cy, 52, 0.60f, 1.6f);
            for (int deg = 0; deg < 360; deg += 30)
            {
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                r.DrawLine(cx + cos * 48f, cy + sin * 48f, cx + cos * 52f, cy + sin * 52f, 0.50f, 1.2f);
            }
            r.DrawEllipse(cx, cy, 52, 17, 0.40f, 1.2f);

            // 2. 远方四象限导航不动恒星 (Four Fixed Sidereal Stars)
            r.DrawStar(24, 24, 6, 0.75f);
            r.DrawStar(104, 24, 6, 0.75f);
            r.DrawStar(24, 104, 6, 0.75f);
            r.DrawStar(104, 104, 6, 0.75f);

            // 3. 贯通天球的惯性主极轴 (南北天极垂直极轴 Polar Vertical Axis: Z-Axis)
            // 南半球极轴: 从南天极 (y=18) 穿入至天球中心 (y=cy)，使用工程虚线体现 3D 球体纵深遮挡
            r.DrawDashedLine(cx, 18, cx, cy - 2, 4, 3, 0.70f, 1.6f);
            r.DrawLine(cx - 4, 18, cx + 4, 18, 0.70f, 1.4f); // 南天极刻度基底

            // 北半球极轴: 从天球中心 (cy) 垂直高耸向上，穿出天球北极，纯净高亮指向天顶 (+Z North Celestial Pole)
            r.DrawArrow(cx, cy, cx, 114, 9f, 24f, 1.0f, 2.4f);
            r.DrawGlyph(cx + 6, 108, 'Z', 1.0f);

            // 4. 赤道平面正交两轴 (+X / +Y，朝斜前下延伸形成正交笛卡尔动基底)
            // +X 轴 (春分点 Vernal Equinox Vector): 斜右下方延伸
            r.DrawArrow(cx, cy, 102, 48, 8.5f, 25f, 1.0f, 2.2f);
            r.DrawGlyph(109, 46, 'X', 1.0f);

            // +Y 轴 (正交补全轴 Completing Orthogonal Frame): 斜左下方延伸
            r.DrawArrow(cx, cy, 26, 48, 8.5f, 25f, 1.0f, 2.2f);
            r.DrawGlyph(18, 46, 'Y', 1.0f);

            // 5. 原点质心微标 (Inertial Origin Hub)
            r.DrawCircle(cx, cy, 4, 1.0f, 1.6f);
            r.FillCircle(cx, cy, 2, 0.6f);
        }

        // =========================================================================
        // 2. 体固坐标系 (Body-Centred Body-Fixed / Rotating Frame / ECEF)
        // =========================================================================
        private static void DrawBodyFixedFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 1. 中心行星自转球体与赤道/经纬网格 (Central Rotating Celestial Globe)
            r.FillCircle(cx, cy, 38, 0.12f);
            r.DrawCircle(cx, cy, 38, 0.95f, 2.0f);

            // 赤道与南北回归线 (Equator & Parallels)
            r.DrawLine(26, cy, 102, cy, 0.90f, 2.0f);
            r.DrawEllipse(cx, 48, 33, 9, 0.45f, 1.0f);
            r.DrawEllipse(cx, 80, 33, 9, 0.45f, 1.0f);

            // 经线自旋网格 (Meridians)
            r.DrawEllipse(cx, cy, 18, 38, 0.65f, 1.2f);
            r.DrawEllipse(cx, cy, 30, 38, 0.40f, 1.0f);

            // 2. 穿透南北两极的自转极轴 (Polar Rotation Axis)
            r.DrawDashedLine(cx, 12, cx, 116, 5, 3, 0.85f, 1.8f);

            // 3. 北极自转动量角速度矢量环弧 (Angular Velocity ω Spin Direction)
            r.DrawArc(cx, 22, 16, -40f, 140f, 0.95f, 1.8f);
            r.DrawArrowHead(78, 24, 12f, 30f, 1.0f, 2.0f);
            r.DrawGlyph(88, 20, 'W', 1.0f); // ω-like W (Angular Velocity)
        }

        // =========================================================================
        // 3. 轨道坐标系 (Orbital Frame / LVLH)
        // =========================================================================
        private static void DrawOrbitalFrame(Rasterizer r)
        {
            // 1. 引力焦点主天体 (Focus Body at left focal point)
            int focusX = 42, focusY = 64;
            r.FillCircle(focusX, focusY, 12, 0.85f);
            r.DrawCircle(focusX, focusY, 12, 1.0f, 1.8f);
            r.DrawCircle(focusX, focusY, 16, 0.35f, 1.0f);

            // 2. 开普勒闭合椭圆轨道 (Keplerian Orbit: center at 58, major a=52, minor b=34)
            int orbCenterX = 58, orbCenterY = 64;
            r.DrawEllipse(orbCenterX, orbCenterY, 52, 34, 0.85f, 1.8f);

            // 近拱点 Pe 与远拱点 Ap 标线
            r.DrawLine(6, 61, 6, 67, 0.7f, 1.4f);
            r.DrawGlyph(7, 56, 'P', 0.85f);
            r.DrawLine(110, 61, 110, 67, 0.7f, 1.4f);
            r.DrawGlyph(111, 56, 'A', 0.85f);

            // 3. 轨道飞行器节点 (Spacecraft Node at top quadrant: 58, 30)
            int scX = 58, scY = 30;
            r.FillCircle(scX, scY, 4, 1.0f);
            r.DrawCircle(scX, scY, 7, 0.6f, 1.2f);

            // 4. 局部轨道参考系三向矢量 (Local Orbital Frame: V / R / N)
            // V (Velocity / Prograde / Tangent): 切向右方
            r.DrawArrow(scX, scY, 96, scY, 8f, 26f, 1.0f, 2.2f);
            r.DrawGlyph(102, scY - 1, 'V', 1.0f);

            // R (Radius / Radial-In): 指向引力焦点
            r.DrawArrow(scX, scY, 46, 56, 7f, 24f, 0.95f, 1.8f);
            r.DrawGlyph(51, 44, 'R', 0.95f);

            // N (Normal / Angular Momentum h): 指向轨道平面外
            r.DrawArrow(scX, scY, 42, 14, 7f, 24f, 0.90f, 1.8f);
            r.DrawGlyph(38, 9, 'N', 0.90f);
        }

        // =========================================================================
        // 4. 拉格朗日点坐标系 (Lagrange Point / Barycentric Frame)
        // =========================================================================
        private static void DrawLagrangeFrame(Rasterizer r)
        {
            int m1X = 38, m1Y = 64; // Primary Body (M1)
            int m2X = 88, m2Y = 64; // Secondary Body (M2)

            // 1. 基线坐标轴与双天体 (Connecting Axis & Masses M1, M2)
            r.DrawLine(10, 64, 118, 64, 0.40f, 1.2f);
            r.FillCircle(m1X, m1Y, 13, 0.90f);
            r.DrawCircle(m1X, m1Y, 13, 1.0f, 1.6f);
            r.FillCircle(m2X, m2Y, 6, 0.90f);
            r.DrawCircle(m2X, m2Y, 6, 1.0f, 1.4f);

            // 2. 质心原点 (System Barycenter ⊕ at 48, 64)
            int baryX = 48, baryY = 64;
            r.DrawCircle(baryX, baryY, 5, 0.95f, 1.4f);
            r.DrawCrosshair(baryX, baryY, 7, 0.95f, 1.2f);

            // 3. 洛希瓣等势面哑铃双叶轮廓 (Roche Lobe Equipotential Curves)
            r.DrawDumbbellContour(m1X, m1Y, m2X, m2Y, 0.35f, 1.2f);

            // 4. 等边三角形稳定性导引虚线 (L4 / L5 Equilateral Triangles at ±60°)
            int l4X = 63, l4Y = 21;
            int l5X = 63, l5Y = 107;
            r.DrawDashedLine(m1X, m1Y, l4X, l4Y, 4, 3, 0.45f, 1.0f);
            r.DrawDashedLine(m2X, m2Y, l4X, l4Y, 4, 3, 0.45f, 1.0f);
            r.DrawDashedLine(m1X, m1Y, l5X, l5Y, 4, 3, 0.45f, 1.0f);
            r.DrawDashedLine(m2X, m2Y, l5X, l5Y, 4, 3, 0.45f, 1.0f);

            // 5. 五大平动点标示 (All Five Lagrange Points L1~L5)
            // L1 (Between M1 and M2)
            r.DrawCrosshair(65, 64, 5, 1.0f, 1.4f);
            r.DrawGlyph(65, 54, '1', 1.0f);

            // L2 (Beyond M2)
            r.DrawCrosshair(107, 64, 5, 1.0f, 1.4f);
            r.DrawGlyph(107, 54, '2', 1.0f);

            // L3 (Opposite M1)
            r.DrawCrosshair(17, 64, 5, 1.0f, 1.4f);
            r.DrawGlyph(17, 54, '3', 1.0f);

            // L4 (+60° Triangle Apex)
            r.DrawCrosshair(l4X, l4Y, 5, 1.0f, 1.4f);
            r.DrawGlyph(l4X + 7, l4Y - 1, '4', 1.0f);

            // L5 (-60° Triangle Apex)
            r.DrawCrosshair(l5X, l5Y, 5, 1.0f, 1.4f);
            r.DrawGlyph(l5X + 7, l5Y - 1, '5', 1.0f);
        }

        // =========================================================================
        // 5. 目标坐标系 (Target Frame)
        // =========================================================================
        private static void DrawTargetFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 双环瞄准标度圈
            r.DrawCircle(cx, cy, 46, 0.80f, 1.8f);
            r.DrawCircle(cx, cy, 22, 0.60f, 1.4f);

            // 四向雷达测距刻度线
            r.DrawLine(cx - 52, cy, cx - 36, cy, 0.9f, 2.0f);
            r.DrawLine(cx + 36, cy, cx + 52, cy, 0.9f, 2.0f);
            r.DrawLine(cx, cy - 52, cx, cy - 36, 0.9f, 2.0f);
            r.DrawLine(cx, cy + 36, cx, cy + 52, 0.9f, 2.0f);

            // 核心对接十字与菱形瞄具
            r.DrawCrosshair(cx, cy, 14, 1.0f, 1.8f);
            r.DrawDiamond(cx, cy, 8, 0.85f, 1.4f);

            // 相对接近视线导引折线与三角游标
            r.DrawArrow(cx - 30, cy + 30, cx - 8, cy + 8, 7f, 26f, 0.95f, 2.0f);
            r.DrawGlyph(cx - 36, cy + 36, 'T', 0.9f);
        }

        // =========================================================================
        // 6. 地表坐标系 (Local Topocentric / Surface Horizon Frame)
        // =========================================================================
        private static void DrawSurfaceFrame(Rasterizer r)
        {
            int cx = 64;

            // 1. 行星地表微弧地平线 (Curved Planetary Horizon Arc)
            r.DrawArc(cx, -16, 62, 48f, 132f, 0.90f, 2.0f);

            // 2. 地表测站基座与切线基线 (Ground Station & Tangent Baseline)
            r.DrawLine(32, 44, 96, 44, 0.45f, 1.2f);
            r.FillCircle(cx, 44, 4, 1.0f);
            r.DrawCircle(cx, 44, 8, 0.7f, 1.4f);
            r.DrawLine(cx - 8, 38, cx, 44, 0.8f, 1.6f);
            r.DrawLine(cx + 8, 38, cx, 44, 0.8f, 1.6f);

            // 3. 局部铅垂天顶轴 (+Up / Zenith Vector)
            r.DrawArrow(cx, 44, cx, 110, 8f, 24f, 1.0f, 2.2f);
            r.DrawGlyph(cx + 7, 104, 'U', 1.0f); // U for Up/Zenith

            // 4. 局部地平切向水平速度矢量 (+East / Horizon Vector)
            r.DrawArrow(cx, 44, 108, 44, 7.5f, 24f, 0.95f, 2.0f);
            r.DrawGlyph(110, 50, 'H', 0.95f); // H for Horizon

            // 5. 地表网格虚线提示 (Subsurface Terrain Hash Marks)
            r.DrawLine(40, 38, 36, 28, 0.40f, 1.0f);
            r.DrawLine(52, 41, 48, 31, 0.40f, 1.0f);
            r.DrawLine(64, 42, 64, 32, 0.40f, 1.0f);
            r.DrawLine(76, 41, 80, 31, 0.40f, 1.0f);
            r.DrawLine(88, 38, 92, 28, 0.40f, 1.0f);
        }

        // =========================================================================
        // 7. 天体定向坐标系 (Body Direction Frame)
        // =========================================================================
        private static void DrawBodyDirectionFrame(Rasterizer r)
        {
            int cx = 64, cy = 64;

            // 中心天体
            r.FillCircle(cx, cy, 16, 0.8f);
            r.DrawCircle(cx, cy, 16, 1.0f, 1.8f);

            // 定向射线与指向箭头
            r.DrawArrow(cx, cy, cx + 46, cy - 34, 8f, 26f, 1.0f, 2.2f);
            r.DrawDashedLine(cx - 34, cy + 26, cx, cy, 4, 3, 0.5f, 1.4f);

            // 目标天体外框
            r.DrawCircle(cx + 46, cy - 34, 8, 0.8f, 1.4f);
            r.DrawGlyph(cx + 46, cy - 46, 'D', 0.9f);
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

                DrawLine(x1, y1, hx1, hy1, alpha, width);
                DrawLine(x1, y1, hx2, hy2, alpha, width);
            }

            public void DrawArrowHead(float tipX, float tipY, float headLen, float headAngleDeg, float alpha, float width = 2.0f)
            {
                float a1 = Mathf.PI - headAngleDeg * Mathf.Deg2Rad;
                float a2 = Mathf.PI + headAngleDeg * Mathf.Deg2Rad;
                DrawLine(tipX, tipY, tipX + Mathf.Cos(a1) * headLen, tipY + Mathf.Sin(a1) * headLen, alpha, width);
                DrawLine(tipX, tipY, tipX + Mathf.Cos(a2) * headLen, tipY + Mathf.Sin(a2) * headLen, alpha, width);
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
                // 中心亮核
                FillCircle(cx, cy, 1.8f, alpha);
                // 十字射线衰减
                for (int d = 1; d <= r; d++)
                {
                    float a = alpha * (1f - (float)d / (r + 1));
                    SetPixel(cx + d, cy, a);
                    SetPixel(cx - d, cy, a);
                    SetPixel(cx, cy + d, a);
                    SetPixel(cx, cy - d, a);
                }
            }

            public void DrawDumbbellContour(int m1X, int m1Y, int m2X, int m2Y, float alpha, float width = 1.2f)
            {
                int steps = 100;
                float prevX = 0, prevY = 0;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (i * 2f * Mathf.PI) / steps;
                    // 双引力中心势能等势线方程参数逼近
                    float baseR1 = 22f;
                    float baseR2 = 12f;
                    float px, py;

                    if (t < Mathf.PI)
                    {
                        // 上半周：从 M1 绕经 L4 跨向 M2
                        float u = t / Mathf.PI;
                        float cx = Mathf.Lerp(m1X, m2X, u);
                        float cy = m1Y;
                        float rSpan = Mathf.Lerp(baseR1, baseR2, u);
                        // 在中心鞍点 (u ≈ 0.5) 发生轻微内凹颈缩
                        float waist = 1f - 0.35f * Mathf.Sin(u * Mathf.PI);
                        px = cx + Mathf.Cos(t) * rSpan;
                        py = cy - Mathf.Sin(t) * (rSpan * waist);
                    }
                    else
                    {
                        // 下半周：从 M2 绕经 L5 跨回 M1
                        float u = (t - Mathf.PI) / Mathf.PI;
                        float cx = Mathf.Lerp(m2X, m1X, u);
                        float cy = m1Y;
                        float rSpan = Mathf.Lerp(baseR2, baseR1, u);
                        float waist = 1f - 0.35f * Mathf.Sin(u * Mathf.PI);
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
