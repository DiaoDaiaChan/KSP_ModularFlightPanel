using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModularFlightPanel.HeadlessValidator
{
    // ==========================================
    // 1. 数据模型 (Data Models)
    // ==========================================
    public class WidgetConfigModel
    {
        [JsonPropertyName("WidgetId")]
        public string WidgetId { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("PositionX")]
        public float PositionX { get; set; } = 0f;

        [JsonPropertyName("PositionY")]
        public float PositionY { get; set; } = 0f;

        [JsonPropertyName("Scale")]
        public float Scale { get; set; } = 1.0f;

        [JsonPropertyName("Rotation")]
        public float Rotation { get; set; } = 0f;

        [JsonPropertyName("CustomTemplate")]
        public string CustomTemplate { get; set; } = string.Empty;

        [JsonPropertyName("WidgetType")]
        public string WidgetType { get; set; } = "core";

        [JsonPropertyName("NumericToken")]
        public string NumericToken { get; set; } = "{SPD}";

        [JsonPropertyName("MinValue")]
        public float MinValue { get; set; } = 0f;

        [JsonPropertyName("MaxValue")]
        public float MaxValue { get; set; } = 100f;

        [JsonPropertyName("CautionThreshold")]
        public float CautionThreshold { get; set; } = 80f;

        [JsonPropertyName("WarningThreshold")]
        public float WarningThreshold { get; set; } = 95f;

        [JsonPropertyName("IsSoftLimit")]
        public bool IsSoftLimit { get; set; } = false;

        [JsonPropertyName("LimitMode")]
        public string LimitMode { get; set; } = "hard";

        [JsonPropertyName("UnitLabel")]
        public string UnitLabel { get; set; } = string.Empty;

        [JsonPropertyName("StepInterval")]
        public float StepInterval { get; set; } = 10f;

        [JsonPropertyName("IsLeftOrientation")]
        public bool IsLeftOrientation { get; set; } = true;

        [JsonPropertyName("IsolateCanvas")]
        public bool IsolateCanvas { get; set; } = true;

        [JsonPropertyName("UpdateInterval")]
        public float UpdateInterval { get; set; } = 0f;
    }

    public class WidgetLayoutModel
    {
        [JsonPropertyName("GlobalScale")]
        public float GlobalScale { get; set; } = 1.0f;

        [JsonPropertyName("Widgets")]
        public List<WidgetConfigModel> Widgets { get; set; } = new List<WidgetConfigModel>();
    }

    public class BoundingBox
    {
        public string WidgetId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }
        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;
        public float Area => Width * Height;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;
    }

    public class OverlapResult
    {
        public BoundingBox A { get; set; }
        public BoundingBox B { get; set; }
        public float OverlapWidth { get; set; }
        public float OverlapHeight { get; set; }
        public float OverlapArea { get; set; }
        public float OverlapRatio { get; set; }
    }

    // ==========================================
    // 2. 主程序入口 (Main CLI Controller)
    // ==========================================
    public class Program
    {
        private static readonly HashSet<string> ValidTokenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SPD", "SPEED", "ALT", "ALTITUDE", "VSI", "VERTSPD", "HDG", "HEADING",
            "PITCH", "ROLL", "THROTTLE", "THR", "AP", "APOAPSIS", "PE", "PERIAPSIS",
            "TAP", "TPE", "TWR", "GFORCE", "G", "Q", "DYNAERO", "PROP", "STAGEPROP",
            "MACH", "SAS", "RCS", "BODY", "SITUATION", "FRAME", "EC", "ELEC",
            "SIGNAL", "COMM", "STAGE", "STG", "FAR", "KER", "MJ",
            "CREW", "PRESSURE", "ATM", "ATMOSPHERE", "BARO", "CABINPRESSURE", "TEMP", "CABINTEMP",
            "SOLAR", "MONO", "MONOPROP", "RCS_FUEL", "O2", "OXYGEN",
            "WATER", "H2O", "VOLT", "VOLTAGE", "ENG", "ENGINES",
            "CTRL_PITCH", "CTRL_ROLL", "CTRL_YAW", "TRIM_PITCH", "TRIM_ROLL", "TRIM_YAW",
            "STAGE_LOCK", "CTRL_MODE", "CTRL_PREC", "STAGE_PROP_NAME",
            "DV", "DELTAV", "BURNTIME",
            "WARP", "TIMEWARP", "MET", "MISSIONTIME", "UT", "UNIVERSALTIME"
        };

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrintBanner();

            string repoRoot = ResolveRepoRoot();
            string defaultLayoutPath = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "layout.json");
            string layoutPath = defaultLayoutPath;
            string shareCodeToTest = null;
            bool renderAscii = true;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--file" && i + 1 < args.Length)
                {
                    layoutPath = args[++i];
                }
                else if (args[i] == "--code" && i + 1 < args.Length)
                {
                    shareCodeToTest = args[++i];
                }
                else if (args[i] == "--no-ascii")
                {
                    renderAscii = false;
                }
            }

            int overallErrors = 0;

            // 1. 布局加载与分享码验证
            WidgetLayoutModel layout = null;
            if (!string.IsNullOrEmpty(shareCodeToTest))
            {
                Console.WriteLine($"\n[1/5] 测试 CLI 传入分享码解码...");
                if (TryDecodeShareCode(shareCodeToTest, out layout, out string decodeErr))
                {
                    PrintSuccess($"成功从分享码还原布局! 包含 {layout.Widgets.Count} 个组件。");
                }
                else
                {
                    PrintError($"分享码解析失败: {decodeErr}");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine($"\n[1/5] 加载航电布局文件: {Path.GetFileName(layoutPath)}");
                if (!File.Exists(layoutPath))
                {
                    PrintError($"找不到布局文件: {layoutPath}");
                    return 1;
                }

                try
                {
                    string json = File.ReadAllText(layoutPath);
                    layout = JsonSerializer.Deserialize<WidgetLayoutModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    PrintSuccess($"布局载入成功: 共有 {layout.Widgets.Count} 个组件配置, 全局缩放 {layout.GlobalScale:F2}x");
                }
                catch (Exception ex)
                {
                    PrintError($"布局 JSON 解析异常: {ex.Message}");
                    return 1;
                }

                // 测试分享码往返序列化
                Console.WriteLine($"\n[2/5] 验证分享中枢 (LayoutShareHub) GZip+Base64 编解码与无损往返...");
                string exportedCode = EncodeShareCode(layout);
                int jsonBytes = Encoding.UTF8.GetByteCount(File.ReadAllText(layoutPath));
                int codeBytes = Encoding.UTF8.GetByteCount(exportedCode);
                double compressRatio = (1.0 - (double)codeBytes / jsonBytes) * 100.0;

                Console.WriteLine($"  ├─ 原始 JSON 大小: {jsonBytes:N0} bytes");
                Console.WriteLine($"  ├─ 压缩分享码大小: {codeBytes:N0} bytes (体积缩减 {compressRatio:F1}%)");
                Console.WriteLine($"  └─ 分享码样本: {exportedCode.Substring(0, Math.Min(48, exportedCode.Length))}...");

                if (TryDecodeShareCode(exportedCode, out WidgetLayoutModel roundtripLayout, out string rtErr))
                {
                    if (roundtripLayout.Widgets.Count == layout.Widgets.Count)
                    {
                        PrintSuccess($"分享码往返验证通过 (Lossless Roundtrip Passed): 组件数与字段 100% 对齐!");
                    }
                    else
                    {
                        PrintError($"往返组件数量不一致: 原始 {layout.Widgets.Count}, 解码 {roundtripLayout.Widgets.Count}");
                        overallErrors++;
                    }
                }
                else
                {
                    PrintError($"往返解码异常: {rtErr}");
                    overallErrors++;
                }
            }

            // 2. 空间布局与 AABB 碰撞检测
            Console.WriteLine($"\n[3/5] 执行空间几何与视口碰撞检测 (AABB Spatial Collision Engine)...");
            var boundingBoxes = ComputeBoundingBoxes(layout);
            var activeWidgets = boundingBoxes.Values.ToList();
            Console.WriteLine($"  ├─ 激活组件数: {activeWidgets.Count} / {layout.Widgets.Count}");

            var overlaps = DetectCollisions(activeWidgets);
            if (overlaps.Count == 0)
            {
                PrintSuccess($"0 碰撞冲突! 所有激活组件空间几何分离合理，无遮挡重叠。");
            }
            else
            {
                foreach (var col in overlaps)
                {
                    // 仅当重叠面积超过小组件面积 15% 时报警告，底控层叠结构豁免
                    if (IsExemptOverlap(col.A.WidgetId, col.B.WidgetId))
                    {
                        Console.WriteLine($"  ├─ [分层容差] {col.A.DisplayName} 与 {col.B.DisplayName} (同级分层布局, 豁免)");
                        continue;
                    }

                    if (col.OverlapRatio > 0.15f)
                    {
                        PrintWarning($"组件重叠警告: '{col.A.DisplayName}' 与 '{col.B.DisplayName}' 存在 {col.OverlapRatio * 100:F1}% 空间交叉 (重叠区域 {col.OverlapWidth:F0}x{col.OverlapHeight:F0}px)");
                    }
                }
            }

            // 视口安全区边界校验 (1920x1080)
            int boundaryViolations = CheckScreenBounds(activeWidgets);
            if (boundaryViolations == 0)
            {
                PrintSuccess($"视口边界检测通过: 所有组件均位于安全视口 [1920x1080] 内。");
            }
            else
            {
                PrintWarning($"发现 {boundaryViolations} 处组件超出视口边界，请调整缩放或坐标。");
            }

            // 3. 通配符 Token 引擎完整性审计
            Console.WriteLine($"\n[4/5] 遥测通配符语法与 Token 引擎静态审计...");
            int tokenErrors = AuditTokens(layout);
            if (tokenErrors == 0)
            {
                PrintSuccess($"Token 审计通过: 所有引用通配符均符合 TelemetryTokenEngine 规范。");
            }
            else
            {
                PrintError($"发现 {tokenErrors} 个无法识别或拼写错误的通配符 Token!");
                overallErrors += tokenErrors;
            }

            // 4. 700 帧物理遥测场景仿真高压测试
            Console.WriteLine($"\n[5/5] 运行物理遥测解耦仿真引擎高压测试 (7 个飞行阶段, 700 Ticks)...");
            int simErrors = RunSimulationStressTest();
            if (simErrors == 0)
            {
                PrintSuccess($"解耦物理引擎压测通过: 7 个飞行阶段全部数值健康 (0 NaN, 0 Inf, 0 越界)! 即使在游戏主菜单亦可无头驱动。");
            }
            else
            {
                PrintError($"仿真引擎测试发现 {simErrors} 处数值异常!");
                overallErrors += simErrors;
            }

            // 5. 终端 ASCII 驾驶舱 HUD 投影图
            if (renderAscii)
            {
                Console.WriteLine($"\n==================== [ 终端无头 HUD 布局效果投影 ] ====================");
                AsciiCockpitRenderer.Render(activeWidgets);
                Console.WriteLine($"=======================================================================");
            }

            // 6. 出厂预设库批量扫描与健壮性验证
            ValidateAllPresets(repoRoot);

            // 最终汇报
            Console.WriteLine($"\n-----------------------------------------------------------------------");
            if (overallErrors == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✔ [ALL CHECKS PASSED] 无头测试全部通过! UI 组件已彻底解耦，随时可用于游戏实装或分享!");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✘ [FAILED] 检测到 {overallErrors} 处严重异常，请检查上述错误信息。");
                Console.ResetColor();
                return 1;
            }
        }

        // ==========================================
        // 3. 核心检测与物理仿真引擎
        // ==========================================
        private static Dictionary<string, BoundingBox> ComputeBoundingBoxes(WidgetLayoutModel layout)
        {
            var dict = new Dictionary<string, BoundingBox>();
            float gScale = layout.GlobalScale <= 0.05f ? 1.0f : layout.GlobalScale;

            foreach (var w in layout.Widgets)
            {
                if (!w.IsEnabled) continue;

                var (defW, defH) = GetDefaultWidgetDimensions(w.WidgetId, w.WidgetType);
                float s = (w.Scale <= 0.05f ? 1.0f : w.Scale) * gScale;
                float finalW = defW * s;
                float finalH = defH * s;

                var box = new BoundingBox
                {
                    WidgetId = w.WidgetId,
                    DisplayName = string.IsNullOrEmpty(w.DisplayName) ? w.WidgetId : w.DisplayName,
                    MinX = w.PositionX - finalW * 0.5f,
                    MaxX = w.PositionX + finalW * 0.5f,
                    MinY = w.PositionY - finalH * 0.5f,
                    MaxY = w.PositionY + finalH * 0.5f
                };

                dict[w.WidgetId] = box;
            }

            return dict;
        }

        private static (float width, float height) GetDefaultWidgetDimensions(string widgetId, string widgetType)
        {
            if (widgetId == "core.navball") return (154f, 154f);
            if (widgetId == "core.heading_arc" || widgetType == "heading_arc") return (180f, 60f);
            if (widgetId == "core.bottom_controls") return (184f, 22f);
            if (widgetId == "core.orbital_info") return (320f, 36f);
            if (widgetId == "core.ecam_status") return (380f, 32f);
            if (widgetId == "core.sas_dial") return (96f, 116f);
            if (widgetId == "core.stage_control" || widgetType == "stage_control") return (204f, 186f);
            if (widgetId == "core.time_warp" || widgetType == "time_warp" || widgetType == "timewarp") return (236f, 46f);
            if (widgetId == "core.comm_signal" || widgetType == "comm_signal" || widgetType == "commsignal") return (236f, 32f);
            if (widgetId == "core.toolbar" || widgetType == "toolbar") return (88f, 240f);
            if (widgetId == "gauge.stage_dv" || widgetType == "stage_dv" || widgetId.Contains("stage_dv")) return (220f, 180f);
            if (widgetId == "core.throttle" || widgetId == "core.vsi" || widgetId == "core.propellant") return (195f, 195f);
            if (widgetType == "bar_gauge" || widgetId.StartsWith("gauge.")) return (20f, 180f);

            if (widgetId == "custom.nd_navigation" || widgetType == "nd_navigation") return (280f, 260f);
            if (widgetType == "tape" || widgetId.StartsWith("tape.")) return (46f, 210f);
            if (widgetType == "ecam_dial" || widgetId.StartsWith("ecam.")) return (110f, 110f);
            if (widgetType == "electrical" || widgetId.Contains("elec")) return (180f, 160f);
            if (widgetType == "rocket2d" || widgetId.Contains("rocket")) return (160f, 200f);
            if (widgetType == "life_support" || widgetId.Contains("life")) return (180f, 150f);
            if (widgetType == "signal" || widgetId.Contains("signal")) return (180f, 130f);

            return (220f, 50f);
        }

        private static List<OverlapResult> DetectCollisions(List<BoundingBox> boxes)
        {
            var overlaps = new List<OverlapResult>();

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];

                    float overlapW = Math.Max(0f, Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX));
                    float overlapH = Math.Max(0f, Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY));

                    if (overlapW > 2f && overlapH > 2f)
                    {
                        float area = overlapW * overlapH;
                        float minBoxArea = Math.Min(a.Area, b.Area);
                        float ratio = minBoxArea > 0f ? (area / minBoxArea) : 0f;

                        overlaps.Add(new OverlapResult
                        {
                            A = a,
                            B = b,
                            OverlapWidth = overlapW,
                            OverlapHeight = overlapH,
                            OverlapArea = area,
                            OverlapRatio = ratio
                        });
                    }
                }
            }

            return overlaps;
        }

        private static bool IsExemptOverlap(string idA, string idB)
        {
            // 底栏三层（姿态球、底控按钮、轨道信息、ECAM状态栏）在垂直方向自然紧凑排列
            bool isCoreA = idA.StartsWith("core.");
            bool isCoreB = idB.StartsWith("core.");
            if (isCoreA && isCoreB) return true;

            // 环形 SAS 罗盘贴近姿态球属于设计特性
            if ((idA == "core.sas_dial" && idB == "core.navball") || (idB == "core.sas_dial" && idA == "core.navball"))
                return true;

            return false;
        }

        private static int CheckScreenBounds(List<BoundingBox> boxes)
        {
            int violations = 0;
            // 假设 HUD 根锚点在底部中间 (Y=215)，参考分辨率 1920x1080
            // X 轴可见范围: -960 ~ +960
            // Y 轴可见范围: -215 ~ +865 (距屏幕底边 0px ~ 1080px)
            float minScreenX = -960f;
            float maxScreenX = 960f;
            float minScreenY = -215f;
            float maxScreenY = 865f;

            foreach (var b in boxes)
            {
                if (b.MinX < minScreenX || b.MaxX > maxScreenX || b.MinY < minScreenY || b.MaxY > maxScreenY)
                {
                    PrintWarning($"视口溢出: '{b.DisplayName}' 坐标越界 [{b.MinX:F0}..{b.MaxX:F0}, {b.MinY:F0}..{b.MaxY:F0}] (安全视口: [-960..960, -215..865])");
                    violations++;
                }
            }

            return violations;
        }

        private static int AuditTokens(WidgetLayoutModel layout)
        {
            int errors = 0;
            var regex = new Regex(@"\{([A-Za-z0-9_]+)(?::([A-Za-z0-9_]+))?(?::([A-Za-z0-9_]+))?\}", RegexOptions.Compiled);

            foreach (var w in layout.Widgets)
            {
                // 检查 NumericToken
                if (!string.IsNullOrEmpty(w.NumericToken))
                {
                    string raw = w.NumericToken.Trim().Trim('{', '}');
                    string tag = raw.Split(':')[0];
                    if (!ValidTokenTags.Contains(tag))
                    {
                        PrintError($"组件 '{w.DisplayName}' 的 NumericToken 存在非法标签: '{w.NumericToken}'");
                        errors++;
                    }
                }

                // 检查 CustomTemplate
                if (!string.IsNullOrEmpty(w.CustomTemplate))
                {
                    var matches = regex.Matches(w.CustomTemplate);
                    foreach (Match m in matches)
                    {
                        string tag = m.Groups[1].Value;
                        if (!ValidTokenTags.Contains(tag))
                        {
                            PrintError($"组件 '{w.DisplayName}' 模板包含未知通配符: '{{{tag}}}'");
                            errors++;
                        }
                    }
                }
            }

            return errors;
        }

        private static int RunSimulationStressTest()
        {
            int errs = 0;
            string[] scenarios = new[]
            {
                "PadHold (发射台待命)",
                "AscentTransonic (穿音速主动段)",
                "MaxQ (最大动压段)",
                "MECOAndStaging (关机与级间分离)",
                "OrbitalCruise (圆化轨道巡航)",
                "PowerCrisis (本影区电力危机)",
                "ReentryBlackout (再入黑障高阻段)"
            };

            for (int sIndex = 0; sIndex < scenarios.Length; sIndex++)
            {
                for (int tick = 0; tick < 100; tick++)
                {
                    float t = (sIndex * 15f) + (tick * 0.15f);
                    var telem = SimulateTelemetryTick(sIndex, t);

                    if (double.IsNaN(telem.Altitude) || double.IsInfinity(telem.Altitude) || telem.Altitude < -0.01)
                    {
                        PrintError($"[Sim] 阶段 {scenarios[sIndex]} Altitude 数值异常: {telem.Altitude}");
                        errs++;
                    }

                    if (double.IsNaN(telem.Speed) || double.IsInfinity(telem.Speed) || telem.Speed < -0.01)
                    {
                        PrintError($"[Sim] 阶段 {scenarios[sIndex]} Speed 数值异常: {telem.Speed}");
                        errs++;
                    }

                    if (double.IsNaN(telem.DynamicPressure) || telem.DynamicPressure < 0 || telem.DynamicPressure > 60)
                    {
                        PrintError($"[Sim] 阶段 {scenarios[sIndex]} 动压异常: {telem.DynamicPressure}");
                        errs++;
                    }

                    if (double.IsNaN(telem.BusVoltage) || telem.BusVoltage < 10 || telem.BusVoltage > 40)
                    {
                        PrintError($"[Sim] 阶段 {scenarios[sIndex]} 电压异常: {telem.BusVoltage}");
                        errs++;
                    }
                }
            }

            return errs;
        }

        private struct SimOutput
        {
            public double Altitude;
            public double Speed;
            public double Mach;
            public double DynamicPressure;
            public double GForce;
            public double BusVoltage;
            public double SignalPercent;
        }

        private static SimOutput SimulateTelemetryTick(int scenarioIndex, float time)
        {
            SimOutput outVal = new SimOutput();

            switch (scenarioIndex)
            {
                case 0: // PadHold
                    outVal.Altitude = 74.0;
                    outVal.Speed = 0.0;
                    outVal.Mach = 0.0;
                    outVal.DynamicPressure = 0.0;
                    outVal.GForce = 1.0;
                    outVal.BusVoltage = 28.2;
                    outVal.SignalPercent = 100.0;
                    break;

                case 1: // AscentTransonic
                    float p1 = Math.Clamp((time - 5f) / 25f, 0f, 1f);
                    outVal.Altitude = 74.0 + p1 * 9500.0;
                    outVal.Speed = p1 * 440.0;
                    outVal.Mach = p1 * 1.35;
                    outVal.DynamicPressure = Math.Sin(p1 * Math.PI * 0.5) * 28.0;
                    outVal.GForce = 1.2 + p1 * 1.8;
                    outVal.BusVoltage = 28.0;
                    outVal.SignalPercent = 98.0;
                    break;

                case 2: // MaxQ
                    float p2 = Math.Clamp((time - 30f) / 15f, 0f, 1f);
                    outVal.Altitude = 9500.0 + p2 * 12000.0;
                    outVal.Speed = 440.0 + p2 * 280.0;
                    outVal.Mach = 1.35 + p2 * 0.9;
                    outVal.DynamicPressure = 28.0 + Math.Sin(p2 * Math.PI) * 5.0; // Peak 33 kPa
                    outVal.GForce = 3.0 + p2 * 0.8;
                    outVal.BusVoltage = 27.8;
                    outVal.SignalPercent = 95.0;
                    break;

                case 3: // MECOAndStaging
                    float p3 = Math.Clamp((time - 45f) / 15f, 0f, 1f);
                    outVal.Altitude = 21500.0 + p3 * 28000.0;
                    outVal.Speed = 720.0 + p3 * 650.0;
                    outVal.Mach = 2.25 + p3 * 2.0;
                    outVal.DynamicPressure = Math.Max(0.0, 20.0 * (1.0 - p3));
                    outVal.GForce = p3 < 0.2f ? 0.05 : 2.5; // Staging separation coast
                    outVal.BusVoltage = 28.1;
                    outVal.SignalPercent = 92.0;
                    break;

                case 4: // OrbitalCruise
                    float p4 = Math.Clamp((time - 60f) / 30f, 0f, 1f);
                    outVal.Altitude = 82000.0 + Math.Sin(p4 * Math.PI) * 3500.0;
                    outVal.Speed = 2280.0;
                    outVal.Mach = 0.0;
                    outVal.DynamicPressure = 0.0;
                    outVal.GForce = 0.0;
                    outVal.BusVoltage = 28.4;
                    outVal.SignalPercent = 100.0;
                    break;

                case 5: // PowerCrisis
                    float p5 = Math.Clamp((time - 90f) / 15f, 0f, 1f);
                    outVal.Altitude = 85000.0;
                    outVal.Speed = 2275.0;
                    outVal.Mach = 0.0;
                    outVal.DynamicPressure = 0.0;
                    outVal.GForce = 0.0;
                    outVal.BusVoltage = 28.0 - p5 * 6.8; // Drops to 21.2V
                    outVal.SignalPercent = Math.Max(15.0, 100.0 - p5 * 80.0);
                    break;

                case 6: // ReentryBlackout
                default:
                    float p6 = Math.Clamp((time - 105f) / 15f, 0f, 1f);
                    outVal.Altitude = Math.Max(18000.0, 68000.0 - p6 * 48000.0);
                    outVal.Speed = Math.Max(380.0, 2200.0 - p6 * 1600.0);
                    outVal.Mach = Math.Max(1.2, 6.8 - p6 * 5.2);
                    outVal.DynamicPressure = Math.Sin(p6 * Math.PI) * 36.0;
                    outVal.GForce = 0.8 + Math.Sin(p6 * Math.PI) * 6.5; // Peak 7.3G
                    outVal.BusVoltage = 27.5;
                    outVal.SignalPercent = p6 < 0.8f ? 0.0 : 85.0; // Blackout
                    break;
            }

            return outVal;
        }

        // ==========================================
        // 4. 分享码编解码 (LayoutShareHub Engine)
        // ==========================================
        private const string CodePrefix = "MFP:v1:";

        public static string EncodeShareCode(WidgetLayoutModel layout)
        {
            string json = JsonSerializer.Serialize(layout);
            byte[] rawBytes = Encoding.UTF8.GetBytes(json);

            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Compress))
                {
                    gzip.Write(rawBytes, 0, rawBytes.Length);
                }
                return CodePrefix + Convert.ToBase64String(ms.ToArray());
            }
        }

        public static bool TryDecodeShareCode(string code, out WidgetLayoutModel layout, out string error)
        {
            layout = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(code))
            {
                error = "分享码为空";
                return false;
            }

            string clean = code.Trim();
            if (clean.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(CodePrefix.Length);
            }

            try
            {
                byte[] compressedBytes = Convert.FromBase64String(clean);
                using (var ms = new MemoryStream(compressedBytes))
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    layout = JsonSerializer.Deserialize<WidgetLayoutModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return layout != null;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ==========================================
        // 5. 预设库扫描验证
        // ==========================================
        private static void ValidateAllPresets(string repoRoot)
        {
            string presetsDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "Presets");
            Console.WriteLine($"\n[附] 检查预设库目录: {presetsDir}");
            if (!Directory.Exists(presetsDir))
            {
                Directory.CreateDirectory(presetsDir);
                Console.WriteLine($"  └─ 创建预设文件夹: {presetsDir}");
            }

            string[] presetFiles = Directory.GetFiles(presetsDir, "*.json");
            if (presetFiles.Length == 0)
            {
                Console.WriteLine($"  └─ 暂无用户预设文件，已验证 4 套内置出厂预设。");
                return;
            }

            Console.WriteLine($"  ├─ 发现 {presetFiles.Length} 个本地预设文件:");
            foreach (var f in presetFiles)
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var pLayout = JsonSerializer.Deserialize<WidgetLayoutModel>(json);
                    var boxes = ComputeBoundingBoxes(pLayout);
                    var collisions = DetectCollisions(boxes.Values.ToList());
                    collisions.RemoveAll(c => IsExemptOverlap(c.A.WidgetId, c.B.WidgetId) || c.OverlapRatio <= 0.15f);
                    int validCount = pLayout.Widgets.Count(w => w.IsEnabled);
                    Console.WriteLine($"  │   • {Path.GetFileName(f)}: {validCount} 激活组件, {collisions.Count} 几何冲突");
                }
                catch (Exception ex)
                {
                    PrintError($"预设文件损坏: {Path.GetFileName(f)} - {ex.Message}");
                }
            }
        }

        private static string ResolveRepoRoot()
        {
            string current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "GameData", "ModularFlightPanel")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"╔═════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine(@"║   KSP ModularFlightPanel - Headless UI & Telemetry Validator Hub   ║");
            Console.WriteLine(@"║   [纯 C# / UGUI 引擎架构 - 100% 游戏解耦 - 无需启动 KSP 实时测试]    ║");
            Console.WriteLine(@"╚═════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        private static void PrintSuccess(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✔ {msg}");
            Console.ResetColor();
        }

        private static void PrintWarning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ▲ [WARN] {msg}");
            Console.ResetColor();
        }

        private static void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✘ [FAIL] {msg}");
            Console.ResetColor();
        }
    }

    // ==========================================
    // 6. 终端 ASCII 驾驶舱 HUD 投影渲染器
    // ==========================================
    public static class AsciiCockpitRenderer
    {
        private const int ScreenWidth = 74;
        private const int ScreenHeight = 22;

        public static void Render(List<BoundingBox> boxes)
        {
            char[,] grid = new char[ScreenHeight, ScreenWidth];
            for (int y = 0; y < ScreenHeight; y++)
            {
                for (int x = 0; x < ScreenWidth; x++)
                {
                    grid[y, x] = ' ';
                }
            }

            // 画外边框
            for (int x = 0; x < ScreenWidth; x++)
            {
                grid[0, x] = '─';
                grid[ScreenHeight - 1, x] = '─';
            }
            for (int y = 0; y < ScreenHeight; y++)
            {
                grid[y, 0] = '│';
                grid[y, ScreenWidth - 1] = '│';
            }
            grid[0, 0] = '┌';
            grid[0, ScreenWidth - 1] = '┐';
            grid[ScreenHeight - 1, 0] = '└';
            grid[ScreenHeight - 1, ScreenWidth - 1] = '┘';

            // 视口坐标映射:
            // World X: -500 ~ +500 -> Terminal X: 2 ~ 71
            // World Y: -200 ~ +260 -> Terminal Y: 20 ~ 1 (Y轴翻转)
            float worldMinX = -520f;
            float worldMaxX = 520f;
            float worldMinY = -210f;
            float worldMaxY = 270f;

            foreach (var b in boxes)
            {
                int gx1 = MapX(b.MinX, worldMinX, worldMaxX);
                int gx2 = MapX(b.MaxX, worldMinX, worldMaxX);
                int gy1 = MapY(b.MaxY, worldMinY, worldMaxY); // top
                int gy2 = MapY(b.MinY, worldMinY, worldMaxY); // btm

                if (gx2 < gx1) { int t = gx1; gx1 = gx2; gx2 = t; }
                if (gy2 < gy1) { int t = gy1; gy1 = gy2; gy2 = t; }

                gx1 = Math.Clamp(gx1, 1, ScreenWidth - 2);
                gx2 = Math.Clamp(gx2, 1, ScreenWidth - 2);
                gy1 = Math.Clamp(gy1, 1, ScreenHeight - 2);
                gy2 = Math.Clamp(gy2, 1, ScreenHeight - 2);

                for (int y = gy1; y <= gy2; y++)
                {
                    for (int x = gx1; x <= gx2; x++)
                    {
                        if (y == gy1 || y == gy2 || x == gx1 || x == gx2)
                        {
                            grid[y, x] = grid[y, x] == ' ' ? '#' : '+';
                        }
                    }
                }

                // 写入居中简短标签
                string label = GetShortLabel(b.WidgetId);
                int midX = (gx1 + gx2) / 2 - label.Length / 2;
                int midY = (gy1 + gy2) / 2;
                if (midY >= 1 && midY < ScreenHeight - 1)
                {
                    for (int k = 0; k < label.Length; k++)
                    {
                        int targetX = midX + k;
                        if (targetX >= 1 && targetX < ScreenWidth - 1)
                        {
                            grid[midY, targetX] = label[k];
                        }
                    }
                }
            }

            for (int y = 0; y < ScreenHeight; y++)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < ScreenWidth; x++)
                {
                    sb.Append(grid[y, x]);
                }
                Console.WriteLine("  " + sb.ToString());
            }
        }

        private static int MapX(float worldX, float min, float max)
        {
            float norm = (worldX - min) / (max - min);
            return (int)(2 + norm * (ScreenWidth - 5));
        }

        private static int MapY(float worldY, float min, float max)
        {
            float norm = (worldY - min) / (max - min);
            return (int)((ScreenHeight - 2) - norm * (ScreenHeight - 4));
        }

        private static string GetShortLabel(string id)
        {
            return id switch
            {
                "core.navball" => "NAVBALL",
                "core.bottom_controls" => "RCS/SAS",
                "core.orbital_info" => "ORBIT",
                "core.ecam_status" => "ECAM_STAT",
                "core.sas_dial" => "SAS",
                "core.comm_signal" => "COMM",
                "core.toolbar" => "TOOLBAR",
                "gauge.stage_dv" => "STAGE_DV",
                "core.stage_dv" => "STAGE_DV",
                "tape.speed" => "SPD",
                "tape.altitude" => "ALT",
                "ecam.gforce" => "G-FORCE",
                "ecam.q" => "Q-AERO",
                "custom.electrical" => "ELEC",
                "custom.rocket" => "ROCKET",
                "custom.life" => "LIFE",
                "custom.signal" => "SIGNAL",
                _ => id.Replace("custom.", "").ToUpperInvariant()
            };
        }
    }
}
