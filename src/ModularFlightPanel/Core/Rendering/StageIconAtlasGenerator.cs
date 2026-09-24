using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 高反差航空航天 HUD 分级部件图标图集生成器 (Procedural Avionics Stage Icon Atlas)
    /// 1. 为无头渲染、测试沙盒以及原版图集未就绪时提供 100% 矢量的 256x128 HUD 图标图集；
    /// 2. 严格对齐 KSP 原版 DefaultIcons 枚举布局 (8 列 x 4 行网格，每单元格 32x32)；
    /// 3. 严格遵循 MFP-SPEC-006 零颜色字面量铁律，像素纯由 NeutralOpaque 与 Color.clear 驱动；
    /// 4. 纯 Unity 核心，杜绝任何 KSP 运行时类依赖。
    /// </summary>
    public static class StageIconAtlasGenerator
    {
        private static Texture2D _cachedAtlas;
        private const int AtlasWidth = 256;
        private const int AtlasHeight = 128;
        private const int TileSize = 32;
        private const int Columns = 8;
        private const int Rows = 4;

        // DefaultIcons 命名映射
        private static readonly Dictionary<string, int> IconNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "CUSTOM", 0 },
            { "MYSTERY_PART", 1 },
            { "LIQUID_ENGINE", 2 },
            { "SOLID_BOOSTER", 3 },
            { "COMMAND_POD", 4 },
            { "DECOUPLER_VERT", 5 },
            { "DECOUPLER_HOR", 6 },
            { "FUEL_TANK", 7 },
            { "PARACHUTES", 8 },
            { "WINGLETS", 9 },
            { "SAS", 10 },
            { "STRUT", 11 },
            { "STRUT_CONNECTOR", 12 },
            { "ADV_SAS", 13 },
            { "RCS_TANK", 14 },
            { "RCS_MODULE", 15 },
            { "FUEL_LINE", 16 },
            { "LANDING_LEG", 17 },
            { "REACTION_WHEEL", 18 },
            { "WHEEL", 19 },
            { "LANDING_GEAR", 20 },
            { "PROBE", 21 },
            { "SCIENCE_GENERIC", 22 }
        };

        public static int GetIconIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return 2; // Default LIQUID_ENGINE
            if (IconNameToIndex.TryGetValue(name, out int idx)) return idx;
            return 2;
        }

        public static Rect GetIconUv(string name)
        {
            return GetIconUv(GetIconIndex(name));
        }

        public static Rect GetIconUv(int index)
        {
            if (index < 0 || index >= Columns * Rows) index = 2;
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
            tex.name = "MFP_AvionicsStageIconAtlas";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[AtlasWidth * AtlasHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            Color white = WidgetStyleManager.NeutralOpaque;

            // 绘制支持的核心图标
            DrawTile(pixels, 2, (px, py) => DrawLiquidEngine(px, py, white));
            DrawTile(pixels, 3, (px, py) => DrawSolidBooster(px, py, white));
            DrawTile(pixels, 4, (px, py) => DrawCommandPod(px, py, white));
            DrawTile(pixels, 5, (px, py) => DrawDecouplerVert(px, py, white));
            DrawTile(pixels, 6, (px, py) => DrawDecouplerHor(px, py, white));
            DrawTile(pixels, 7, (px, py) => DrawFuelTank(px, py, white));
            DrawTile(pixels, 8, (px, py) => DrawParachute(px, py, white));
            DrawTile(pixels, 9, (px, py) => DrawWinglet(px, py, white));
            DrawTile(pixels, 15, (px, py) => DrawRcsModule(px, py, white));
            DrawTile(pixels, 17, (px, py) => DrawLandingLeg(px, py, white));
            DrawTile(pixels, 21, (px, py) => DrawProbe(px, py, white));
            DrawTile(pixels, 0, (px, py) => DrawFairingPayload(px, py, white));

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            _cachedAtlas = tex;
            return _cachedAtlas;
        }

        private static void DrawTile(Color[] pixels, int index, Action<Action<int, int, float>, Action<int, int, int, int, float>> drawer)
        {
            int col = index % Columns;
            int rowFromTop = index / Columns;
            int originX = col * TileSize;
            int originY = (Rows - 1 - rowFromTop) * TileSize;

            Action<int, int, float> setPixel = (lx, ly, a) =>
            {
                if (lx < 0 || lx >= TileSize || ly < 0 || ly >= TileSize) return;
                int gx = originX + lx;
                int gy = originY + ly;
                int pidx = gy * AtlasWidth + gx;
                Color current = pixels[pidx];
                float newA = Mathf.Clamp01(current.a + a);
                pixels[pidx] = WidgetStyleManager.NeutralOpaque * newA;
            };

            Action<int, int, int, int, float> fillRect = (rx, ry, rw, rh, a) =>
            {
                for (int y = ry; y < ry + rh; y++)
                {
                    for (int x = rx; x < rx + rw; x++)
                    {
                        setPixel(x, y, a);
                    }
                }
            };

            drawer(setPixel, fillRect);
        }

        // 2: 液体发动机 (喷管钟形 + 燃烧室 + 矢量尾焰细线)
        private static void DrawLiquidEngine(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 顶部安装座与喷注器盘
            fillRect(12, 25, 8, 2, 1.0f);
            fillRect(14, 23, 4, 2, 0.9f);

            // 喉道收敛-扩散喷管
            for (int y = 9; y <= 22; y++)
            {
                float t = (y - 9) / 13.0f; // 0 at exit, 1 at throat
                // 钟形轮廓方程
                int halfW = Mathf.RoundToInt(Mathf.Lerp(8.5f, 2.5f, Mathf.Sqrt(t)));
                for (int x = 15 - halfW; x <= 16 + halfW; x++)
                {
                    // 空心钟形边缘高亮
                    bool isEdge = (x == 15 - halfW || x == 16 + halfW || x == 15 - halfW + 1 || x == 16 + halfW - 1);
                    float alpha = isEdge ? 1.0f : 0.45f;
                    setPixel(x, y, alpha);
                }
            }

            // 喷管出口加固环
            fillRect(6, 8, 20, 2, 1.0f);

            // 推进中心尾焰导引微线
            fillRect(15, 3, 2, 4, 0.8f);
        }

        // 3: 固体助推器 (尖锥整流罩 + 圆柱壳体 + 环焊缝 + 扩散喷口)
        private static void DrawSolidBooster(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 尖锥头部
            for (int y = 23; y <= 28; y++)
            {
                float t = (28 - y) / 5.0f;
                int halfW = Mathf.RoundToInt(t * 4.5f);
                for (int x = 15 - halfW; x <= 16 + halfW; x++)
                {
                    setPixel(x, y, 0.9f);
                }
            }

            // 圆柱主体壳体
            fillRect(11, 9, 10, 14, 0.7f);
            // 左右轮廓线加强
            fillRect(11, 9, 2, 14, 1.0f);
            fillRect(19, 9, 2, 14, 1.0f);

            // 分段环焊缝
            fillRect(11, 19, 10, 1, 1.0f);
            fillRect(11, 14, 10, 1, 1.0f);

            // 底部固体扩散喷管
            fillRect(13, 7, 6, 2, 0.9f);
            fillRect(12, 4, 8, 3, 1.0f);
        }

        // 4: 载人指令舱 (锥台形阿波罗/龙飞船造型 + 舷窗)
        private static void DrawCommandPod(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 锥体轮廓
            for (int y = 10; y <= 24; y++)
            {
                float t = (y - 10) / 14.0f; // 0 at bottom, 1 at top
                int halfW = Mathf.RoundToInt(Mathf.Lerp(9.5f, 3.5f, t));
                for (int x = 15 - halfW; x <= 16 + halfW; x++)
                {
                    bool isEdge = (x <= 16 - halfW + 1 || x >= 15 + halfW - 1);
                    setPixel(x, y, isEdge ? 1.0f : 0.45f);
                }
            }

            // 顶部对接机构圈
            fillRect(13, 24, 6, 2, 1.0f);
            // 底部隔热大底
            fillRect(5, 8, 22, 2, 1.0f);

            // 舱中央观察舷窗
            fillRect(14, 16, 4, 3, 1.0f);
        }

        // 5: 垂直级间分离器 (上下双级接环 + 爆炸螺栓分离箭头 [ = ])
        private static void DrawDecouplerVert(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 上下双级分离接环
            fillRect(6, 22, 20, 3, 1.0f);
            fillRect(6, 7, 20, 3, 1.0f);

            // 左右限位卡钳
            fillRect(6, 10, 3, 12, 0.75f);
            fillRect(23, 10, 3, 12, 0.75f);

            // 中央双向垂直分离箭头 [ ↑ ↓ ]
            fillRect(15, 12, 2, 8, 1.0f);
            setPixel(14, 18, 1.0f); setPixel(17, 18, 1.0f);
            setPixel(13, 17, 1.0f); setPixel(18, 17, 1.0f);

            setPixel(14, 13, 1.0f); setPixel(17, 13, 1.0f);
            setPixel(13, 14, 1.0f); setPixel(18, 14, 1.0f);
        }

        // 6: 径向分离挂架 (双侧导轨 + 径向外推箭头 [ || ])
        private static void DrawDecouplerHor(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 左右双立柱导轨
            fillRect(7, 6, 3, 20, 1.0f);
            fillRect(22, 6, 3, 20, 1.0f);

            // 中横拉杆
            fillRect(10, 15, 12, 2, 0.8f);

            // 径向向外推开箭头 [ < > ]
            setPixel(12, 17, 1.0f); setPixel(12, 14, 1.0f);
            setPixel(11, 16, 1.0f); setPixel(11, 15, 1.0f);

            setPixel(19, 17, 1.0f); setPixel(19, 14, 1.0f);
            setPixel(20, 16, 1.0f); setPixel(20, 15, 1.0f);
        }

        // 7: 推进剂贮箱 (圆角圆柱 + 贮箱加强筋)
        private static void DrawFuelTank(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            fillRect(10, 8, 12, 16, 0.65f);
            fillRect(10, 8, 2, 16, 1.0f);
            fillRect(20, 8, 2, 16, 1.0f);

            fillRect(11, 23, 10, 2, 0.9f);
            fillRect(11, 7, 10, 2, 0.9f);
            fillRect(12, 15, 8, 2, 1.0f);
        }

        // 8: 降落伞 (半圆穹顶 + 伞绳汇聚)
        private static void DrawParachute(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 伞衣半圆弧
            for (int x = 6; x <= 25; x++)
            {
                float dx = (x - 15.5f) / 9.5f;
                if (Mathf.Abs(dx) <= 1.0f)
                {
                    float dy = Mathf.Sqrt(1.0f - dx * dx);
                    int y = Mathf.RoundToInt(17 + dy * 8.5f);
                    setPixel(x, y, 1.0f);
                    setPixel(x, y - 1, 0.9f);
                    if (y - 2 >= 17) setPixel(x, y - 2, 0.5f);
                }
            }

            // 伞绳汇聚到底部承重环
            fillRect(15, 5, 2, 2, 1.0f);
            // 4 根主要伞绳
            DrawLine(setPixel, 6, 17, 15, 6, 0.8f);
            DrawLine(setPixel, 11, 18, 15, 6, 0.8f);
            DrawLine(setPixel, 20, 18, 16, 6, 0.8f);
            DrawLine(setPixel, 25, 17, 16, 6, 0.8f);
        }

        // 9: 气动舵面/三角翼 (后掠式控制面)
        private static void DrawWinglet(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            fillRect(8, 7, 3, 18, 1.0f);
            for (int y = 7; y <= 25; y++)
            {
                float t = (y - 7) / 18.0f;
                int w = Mathf.RoundToInt(t * 14f);
                for (int x = 8; x <= 8 + w; x++)
                {
                    bool isEdge = (x == 8 + w || y == 7);
                    setPixel(x, y, isEdge ? 1.0f : 0.5f);
                }
            }
        }

        // 15: RCS 四向姿控喷气十字舵
        private static void DrawRcsModule(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 中心阀体
            fillRect(13, 13, 6, 6, 1.0f);

            // 上下左右 4 个锥形微型喷嘴
            fillRect(14, 20, 4, 4, 0.85f); fillRect(13, 24, 6, 2, 1.0f);
            fillRect(14, 8, 4, 4, 0.85f); fillRect(13, 6, 6, 2, 1.0f);
            fillRect(7, 14, 4, 4, 0.85f); fillRect(5, 13, 2, 6, 1.0f);
            fillRect(21, 14, 4, 4, 0.85f); fillRect(25, 13, 2, 6, 1.0f);
        }

        // 17: 着陆缓冲支架 (主承力斜柱 + 折叠连杆 + 接地脚盘)
        private static void DrawLandingLeg(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            fillRect(14, 21, 4, 4, 1.0f);
            DrawLine(setPixel, 15, 21, 9, 8, 1.0f);
            DrawLine(setPixel, 16, 21, 10, 8, 1.0f);
            DrawLine(setPixel, 17, 16, 14, 8, 0.8f);
            fillRect(6, 6, 10, 2, 1.0f);
        }

        // 21: 探测器核心 (六边形卫星本体 + 折叠天线)
        private static void DrawProbe(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            for (int y = 9; y <= 21; y++)
            {
                int dy = Mathf.Abs(y - 15);
                int halfW = Mathf.RoundToInt(7 - dy * 0.5f);
                for (int x = 15 - halfW; x <= 16 + halfW; x++)
                {
                    bool isEdge = (x == 15 - halfW || x == 16 + halfW || dy == 6);
                    setPixel(x, y, isEdge ? 1.0f : 0.6f);
                }
            }
            fillRect(15, 22, 2, 5, 1.0f);
            fillRect(13, 27, 6, 2, 1.0f);
        }

        // 0: 整流罩与有效载荷
        private static void DrawFairingPayload(Action<int, int, float> setPixel, Action<int, int, int, int, float> fillRect, Color c)
        {
            // 整流罩左半壳
            DrawLine(setPixel, 15, 27, 8, 19, 1.0f);
            DrawLine(setPixel, 8, 19, 8, 8, 1.0f);
            // 整流罩右半壳
            DrawLine(setPixel, 16, 27, 23, 19, 1.0f);
            DrawLine(setPixel, 23, 19, 23, 8, 1.0f);
            // 底座支撑环
            fillRect(7, 6, 18, 2, 1.0f);
            // 内部卫星剪影
            fillRect(13, 11, 6, 6, 0.6f);
        }

        private static void DrawLine(Action<int, int, float> setPixel, int x0, int y0, int x1, int y1, float alpha)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                setPixel(x0, y0, alpha);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}
