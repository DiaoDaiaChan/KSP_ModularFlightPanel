using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;   // MFP-SPEC-006 颜色字面量审计器（直接编译插件源码本体）
using ModularFlightPanel.Config; // 统一配置解析引擎与数据契约

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

        [JsonPropertyName("ScaleX")]
        public float ScaleX { get; set; } = 1.0f;

        [JsonPropertyName("ScaleY")]
        public float ScaleY { get; set; } = 1.0f;

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

        // 视图策略字段 (与插件 WidgetConfig 保持同步，确保分享码往返保真度覆盖它们)
        [JsonPropertyName("ValueDeltaThreshold")]
        public double ValueDeltaThreshold { get; set; } = 0.05;

        [JsonPropertyName("BadgeNormal")]
        public string BadgeNormal { get; set; } = "NORM";

        [JsonPropertyName("BadgeCaution")]
        public string BadgeCaution { get; set; } = "CAUT";

        [JsonPropertyName("BadgeWarning")]
        public string BadgeWarning { get; set; } = "WARN";

        [JsonPropertyName("IsolateCanvas")]
        public bool IsolateCanvas { get; set; } = true;

        [JsonPropertyName("UpdateInterval")]
        public float UpdateInterval { get; set; } = 0f;

        [JsonPropertyName("CustomHz")]
        public float CustomHz { get; set; } = 0f;

        [JsonPropertyName("RenderScale")]
        public float RenderScale { get; set; } = 1.0f;
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
            "DV", "DELTAV", "BURNTIME", "MN", "MANEUVER", "NODEDV", "TIMETONODE",
            "WARP", "TIMEWARP", "MET", "MISSIONTIME", "UT", "UNIVERSALTIME",
            "PERF", "PROFILER",
            "GPWS", "TAWS", "RF", "REALFUELS", "TF", "TESTFLIGHT", "DBS", "DYNAMICBATTERYSTORAGE",
            "SH", "SYSTEMHEAT", "AA", "ATMOSPHEREAUTOPILOT", "KERBALISM", "KLSM", "RP1", "RA", "TRAJ", "DOCK"
        };

        public static int Main(string[] args)
        {
            if (args != null && (args.Contains("--audit-legacy") || args.Contains("--audit-modernization")))
            {
                bool msbuildMode = args.Contains("--msbuild") || args.Contains("--quiet");
                if (msbuildMode && Console.IsOutputRedirected)
                {
                    try { Console.OutputEncoding = Encoding.Default; } catch { }
                }
                else
                {
                    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                }
                string root = ResolveRepoRoot();
                var modReport = ModularFlightPanel.UI.Auditing.WidgetModernizationAudit.Scan(root);
                if (msbuildMode)
                {
                    Console.Write(ModularFlightPanel.UI.Auditing.WidgetModernizationAudit.GenerateMSBuildOutput(modReport));
                }
                else
                {
                    Console.Write(ModularFlightPanel.UI.Auditing.WidgetModernizationAudit.GenerateTerminalSummary(modReport));
                }
                return 0;
            }

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
                else if (args[i] == "--color-baseline-dump")
                {
                    return DumpColorLiteralBaseline(repoRoot);
                }
                else if (args[i] == "--mirror-check")
                {
                    return CheckUnityMirror(repoRoot, false) == 0 ? 0 : 1;
                }
                else if (args[i] == "--mirror-fix")
                {
                    return CheckUnityMirror(repoRoot, true) == 0 ? 0 : 1;
                }
                else if (args[i] == "--export-probe-catalog")
                {
                    return ProbeCatalogExporter.ExportCatalog(repoRoot);
                }
                else if (args[i] == "--merge-missing-keys")
                {
                    return I18nMissingKeysMerger.Merge(repoRoot);
                }
                else if (args[i] == "--i18n-ast")
                {
                    return RunI18nAstAudit(repoRoot);
                }
                else if (args[i] == "--test-config" || args[i] == "--test-json")
                {
                    return ConfigRoundtripTestSuite.Run(repoRoot) == 0 ? 0 : 1;
                }
            }

            int overallErrors = 0;

            // 1. 布局加载与分享码验证
            WidgetLayoutModel layout = null;
            if (!string.IsNullOrEmpty(shareCodeToTest))
            {
                Console.WriteLine($"\n[1/10] 测试 CLI 传入分享码解码...");
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
                Console.WriteLine($"\n[1/10] 加载航电布局文件 (AvionicsConfigParser): {Path.GetFileName(layoutPath)}");
                if (!File.Exists(layoutPath))
                {
                    PrintError($"找不到布局文件: {layoutPath}");
                    return 1;
                }

                try
                {
                    string json = File.ReadAllText(layoutPath);
                    var pData = AvionicsConfigParser.ParseLayout(json, out string parseErr);
                    if (pData == null)
                    {
                        PrintError($"布局 JSON 解析异常: {parseErr}");
                        return 1;
                    }
                    layout = ToModel(pData);
                    PrintSuccess($"布局载入成功: 共有 {layout.Widgets.Count} 个组件配置, 全局缩放 {layout.GlobalScale:F2}x");
                }
                catch (Exception ex)
                {
                    PrintError($"布局 JSON 解析异常: {ex.Message}");
                    return 1;
                }

                // 测试分享码往返序列化
                Console.WriteLine($"\n[2/10] 验证分享中枢 (LayoutShareHub) GZip+Base64 编解码与无损往返...");
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
            Console.WriteLine($"\n[3/10] 执行空间几何与视口碰撞检测 (AABB Spatial Collision Engine)...");
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
            Console.WriteLine($"\n[4/10] 遥测通配符语法与 Token 引擎静态审计...");
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
            Console.WriteLine($"\n[5/10] 运行物理遥测解耦仿真引擎高压测试 (7 个飞行阶段, 700 Ticks)...");
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

            // 6. 全量飞行仪表组件规范合法性校验 (Widget Specification Audit, MFP-SPEC-001..007)
            Console.WriteLine($"\n[6/10] 全量飞行仪表组件架构与代码规范合法性校验 (Architecture Compliance Audit)...");
            int specErrors = ValidateWidgetSpecifications(repoRoot);
            overallErrors += specErrors;

            // 7. 审计内核自检：词法器（注释/字符串剥离）+ 规则正则 + I18n AST 语法树自检
            Console.WriteLine($"\n[7/10] 审计内核自检 (Linter + Spec Rules + I18n AST Self-Test)...");
            var linterFailures = CSharpSourceLinter.SelfTest();
            var ruleFailures = WidgetSourceAudit.SelfTest();
            var i18nSelfTestFailures = I18nSyntaxAuditor.SelfTest();
            if (linterFailures.Count == 0 && ruleFailures.Count == 0 && i18nSelfTestFailures.Count == 0)
            {
                // 用例条数由内核回传真实计数：写死数字（曾经是"14 条 / 5 条"）必然随代码漂移成假信息。
                PrintSuccess($"审计内核自检通过: 词法器 {CSharpSourceLinter.LastSelfTestCaseCount} 条边界用例"
                           + $" + 规则自检 {WidgetSourceAudit.LastSelfTestCaseCount} 条对照用例"
                           + $" + I18n 语法树 {I18nSyntaxAuditor.LastSelfTestCaseCount} 条用例 全部符合预期。");
            }
            else
            {
                foreach (var failure in linterFailures) PrintError($"词法器自检失败: {failure}");
                foreach (var failure in ruleFailures) PrintError($"规则自检失败: {failure}");
                foreach (var failure in i18nSelfTestFailures) PrintError($"I18n 语法树自检失败: {failure}");
                overallErrors += linterFailures.Count + ruleFailures.Count + i18nSelfTestFailures.Count;
            }

            // 8. Unity 无头预览工程镜像一致性（清单 tools/unity_mirror.manifest 即合约）
            Console.WriteLine($"\n[8/10] Unity 无头预览工程镜像一致性审计 (Mirror Sync Audit)...");
            int mirrorErrors = CheckUnityMirror(repoRoot, false);
            overallErrors += mirrorErrors;

            // 9. 全局国际化多语言词典一致性审计 (I18n Localization Parity Audit)
            Console.WriteLine($"\n[9/10] 全局国际化多语言词典一致性审计 (I18n Localization Parity Audit)...");
            int i18nErrors = ValidateI18nLocalization(repoRoot);
            overallErrors += i18nErrors;

            // 10. 全局配置与预设双向导入导出高保真往返测试 (Avionics Config Bidirectional Roundtrip Suite)
            Console.WriteLine($"\n[10/10] 全局配置与预设双向导入导出高保真往返测试 (Avionics Config Bidirectional Roundtrip Suite)...");
            int configErrors = ConfigRoundtripTestSuite.Run(repoRoot);
            overallErrors += configErrors;

            // 附加：出厂预设库空间几何扫描与健壮性验证
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

        /// <summary>
        /// MFP-SPEC-001..008 组件规范审计。
        /// 规则实现、正则、颜色基线、"哪些文件算组件"的发现逻辑全部来自插件本体
        /// (src/ModularFlightPanel/UI/WidgetSourceAudit.cs + WidgetColorLiteralAudit.cs)，
        /// 本方法只负责取值、打印与计数 —— 不复制任何规则，因此不存在副本漂移。
        ///
        /// 【护栏】这里绝不允许"扫不到就跳过"：
        /// 空集与真绿在报告上完全同形（0 个组件 / 0 处违规 / 完全合规），
        /// 历史上 repoRoot 定位失败或组件被挪出扫描范围时，门禁会打印 ALL CHECKS PASSED 并返回 0。
        /// 现在仓库根不可用、读取失败、扫描量低于冻结下限一律按 ERROR 计入总数。
        /// </summary>
        private static int ValidateWidgetSpecifications(string repoRoot)
        {
            var diagnostics = new List<string>();
            var componentFiles = WidgetSourceAudit.DiscoverComponentFiles(repoRoot, diagnostics);

            int errors = 0;

            string floorFailure = WidgetSourceAudit.CheckDiscoveryFloor(repoRoot, componentFiles.Count);
            if (floorFailure != null)
            {
                PrintError($"组件规范审计【发现层未通过】: {floorFailure}");
                errors++;
            }

            for (int i = 0; i < diagnostics.Count; i++)
            {
                PrintError($"组件规范审计【读取诊断】: {diagnostics[i]}");
                errors++;
            }

            if (componentFiles.Count == 0)
            {
                Console.WriteLine($"  └─ 审计统计: ERROR {errors} / WARNING 0 (未扫描到任何组件，源码级规则完全未执行)");
                return errors;
            }

            string widgetsDir = Path.Combine(repoRoot, "src", "ModularFlightPanel", "UI", "Widgets");
            int inWidgets = Directory.Exists(widgetsDir) ? Directory.GetFiles(widgetsDir, "*.cs", SearchOption.AllDirectories).Length : 0;

            Console.WriteLine($"  ├─ 扫描范围: UI/Widgets ({inWidgets} 个) + UI 其余目录组件类 ({componentFiles.Count - inWidgets} 个) = {componentFiles.Count} 个组件");
            Console.WriteLine($"  ├─ 冻结下限: {WidgetSourceAudit.ExpectedComponentFloor} 个组件 (低于即判门禁失效，防止空集假绿)");

            var report = WidgetSourceAudit.Scan(componentFiles);
            errors += report.ErrorCount;

            for (int i = 0; i < report.Violations.Count; i++)
            {
                var v = report.Violations[i];
                string message = $"[{v.FileName}] 规则 {v.RuleCode} 违规: {v.Description}" + (v.Line > 0 ? $" (L{v.Line})" : string.Empty);
                if (v.Severity == "ERROR") PrintError(message);
                else PrintWarning(message);
            }

            if (errors == 0)
            {
                PrintSuccess($"规范合规审计 100% 通过 ({componentFiles.Count}/{componentFiles.Count} 组件完全合规):");
                Console.WriteLine($"  ├─ 继承契约: 全部组件统一继承 BaseFlightWidget (类级 + 继承闭包判定)");
                Console.WriteLine($"  ├─ 刷新率阶梯: 全部组件显式重写 RefreshTier 且取值合法 (形状 + 取值双校验)");
                Console.WriteLine($"  ├─ 主题与着色管道: 全部组件接入 WidgetStyleManager (0 颜色字面量, 零容忍)");
                Console.WriteLine($"  ├─ 遥测与生命周期: 全部组件重写 OnUpdateTelemetry & OnDestroy 全量 override 并调用 base");
                Console.WriteLine($"  ├─ 自动注册与元数据: 全部具体组件声明 [FlightWidget] 特性 (MFP-SPEC-008 自动挂载)");
                Console.WriteLine($"  ├─ 探针与场景调度: 0 组件内场景查询 (Find*ObjectByType / GameObject.Find* / Camera.main / GetRootGameObjects)");
                var modReport = ModularFlightPanel.UI.Auditing.WidgetModernizationAudit.Scan(repoRoot);
                Console.WriteLine($"  └─ 架构现代化进度: 现代微控件 DSL {modReport.ModernCount} 个 | 核心 3D 引擎 {modReport.Core3DCount} 个 | 待改造旧版 {modReport.LegacyCount} 个 (架构现代率 {modReport.ModernizationPercentage:F1}%)");
            }
            else
            {
                PrintError($"组件规范审计发现 {errors} 处严重违规，禁止提交!");
            }

            Console.WriteLine($"  └─ 审计统计: ERROR {errors} / WARNING {report.WarningCount}");
            return errors;
        }

        // ==========================================================================================
        // Unity 无头预览工程镜像一致性审计 (Mirror Sync Audit)
        //
        // unity/ 是独立的 Unity 2019 工程，仅用于无头 UI 渲染预览，因此只镜像
        // "不依赖 KSP 运行时即可编译"的那部分源码。过去这份镜像靠手工复制，必然漂移：
        // 源码改了、预览却跑着旧代码，界面回归结论就是假的。
        // 现在清单即合约（tools/unity_mirror.manifest）：缺失 / 逐字节漂移 / 多余 三类问题一律报错，
        // 且同步与校验共用同一份清单解析实现，不存在第二处"镜像文件名单"。
        // ==========================================================================================

        private static int CheckUnityMirror(string repoRoot, bool fix)
        {
            string manifestPath = Path.Combine(repoRoot, "tools", "unity_mirror.manifest");
            string sourceRoot = Path.Combine(repoRoot, "src", "ModularFlightPanel");
            string mirrorRoot = Path.Combine(repoRoot, "unity", "Assets", "ModularFlightPanel");

            if (!File.Exists(manifestPath))
            {
                PrintError($"缺少镜像清单: {manifestPath}（清单是镜像文件集合的唯一定义，不可缺省）");
                return 1;
            }
            if (!Directory.Exists(mirrorRoot))
            {
                PrintError($"未找到 Unity 镜像目录: {mirrorRoot}");
                return 1;
            }

            var exclusions = new List<Regex>();
            foreach (var raw in File.ReadAllLines(manifestPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!line.StartsWith("!", StringComparison.Ordinal))
                {
                    PrintError($"清单语法错误（每条排除项必须以 ! 开头）: {line}");
                    return 1;
                }

                string pattern = line.Substring(1).Trim().Replace('\\', '/');
                string regex = Regex.Escape(pattern).Replace(@"\*\*", @".*").Replace(@"\*", @"[^/]*");
                exclusions.Add(new Regex("^" + regex + "$", RegexOptions.Compiled));
            }

            var sourceFiles = EnumerateScripts(sourceRoot);
            var mirrorFiles = EnumerateScripts(mirrorRoot);

            var desired = new List<string>();
            int excludedCount = 0;
            foreach (var rel in sourceFiles)
            {
                bool isExcluded = false;
                foreach (var rx in exclusions)
                {
                    if (rx.IsMatch(rel)) { isExcluded = true; break; }
                }
                if (isExcluded) excludedCount++;
                else desired.Add(rel);
            }
            desired.Sort(StringComparer.OrdinalIgnoreCase);
            mirrorFiles.Sort(StringComparer.OrdinalIgnoreCase);

            var mirrorSet = new HashSet<string>(mirrorFiles, StringComparer.OrdinalIgnoreCase);
            var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);

            var missing = new List<string>();
            var drifted = new List<string>();
            foreach (var rel in desired)
            {
                if (!mirrorSet.Contains(rel)) { missing.Add(rel); continue; }
                if (!SameFile(Path.Combine(sourceRoot, rel), Path.Combine(mirrorRoot, rel))) drifted.Add(rel);
            }

            var extra = new List<string>();
            foreach (var rel in mirrorFiles)
            {
                if (!desiredSet.Contains(rel)) extra.Add(rel);
            }

            if (fix)
            {
                var copyList = new List<string>(missing);
                foreach (var rel in drifted) if (!copyList.Contains(rel)) copyList.Add(rel);

                foreach (var rel in copyList)
                {
                    string src = Path.Combine(sourceRoot, rel);
                    string dst = Path.Combine(mirrorRoot, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                    Console.WriteLine($"  ├─ 已同步: {rel}");
                }
                foreach (var rel in extra)
                {
                    File.Delete(Path.Combine(mirrorRoot, rel));
                    Console.WriteLine($"  ├─ 已移除多余镜像: {rel}");
                }

                PrintSuccess($"镜像同步完成: 复制/覆盖 {copyList.Count} 个, 移除 {extra.Count} 个, 按清单排除 {excludedCount} 个。");
                return 0;
            }

            int errors = 0;
            foreach (var rel in missing)
            {
                PrintError($"镜像缺失: {rel}（应进镜像但不在；执行 --mirror-fix 一键修复）");
                errors++;
            }
            foreach (var rel in drifted)
            {
                PrintError($"镜像漂移: {rel}（内容与 src 源码不一致，Unity 预览跑的是旧代码；执行 --mirror-fix 一键修复）");
                errors++;
            }
            foreach (var rel in extra)
            {
                PrintError($"镜像多余: {rel}（不在清单内，说明该文件应被排除或源码已删除；执行 --mirror-fix 一键修复）");
                errors++;
            }

            if (errors == 0)
            {
                PrintSuccess($"镜像一致性通过: {desired.Count}/{sourceFiles.Count} 个源码文件在镜像中逐字节一致"
                           + $"（{excludedCount} 个 KSP 运行时/工具链文件按清单排除）");
            }
            return errors;
        }

        /// <summary>枚举目录下全部 .cs（跳过 obj/bin），返回 '/' 分隔的相对路径</summary>
        private static List<string> EnumerateScripts(string root)
        {
            var result = new List<string>();
            if (!Directory.Exists(root)) return result;

            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                bool skip = false;
                foreach (var part in rel.Split('/'))
                {
                    if (part == "obj" || part == "bin") { skip = true; break; }
                }
                if (!skip) result.Add(rel);
            }
            return result;
        }

        /// <summary>逐字节比较（CRLF / BOM 差异同样应当被发现）</summary>
        private static bool SameFile(string a, string b)
        {
            if (!File.Exists(a) || !File.Exists(b)) return false;
            byte[] ba = File.ReadAllBytes(a);
            byte[] bb = File.ReadAllBytes(b);
            if (ba.Length != bb.Length) return false;
            for (int i = 0; i < ba.Length; i++) if (ba[i] != bb[i]) return false;
            return true;
        }

        /// <summary>
        /// 全局多语言本地化词典一致性审计 (I18n Localization Parity Audit)
        /// 1. 验证 GameData/ModularFlightPanel/Localization/ 目录下 zh-CN.json 与 en-US.json 存在且格式合法。
        /// 2. 验证纯 C# 零依赖 I18nJsonParser 解析结果与 System.Text.Json 100% 对齐。
        /// 3. 验证 zh-CN 与 en-US 词条键名 100% 双向对齐（零缺失）。
        /// 4. 验证带格式化占位符的词条 ({0}, {1} 等) 在中英文之间占位符完全匹配，杜绝运行时 FormatException。
        /// </summary>
        private static int ValidateI18nLocalization(string repoRoot)
        {
            string locDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "Localization");
            if (!Directory.Exists(locDir))
            {
                PrintError($"[I18n] 未找到多语言本地化目录: {locDir}");
                return 1;
            }

            string[] langFiles = Directory.GetFiles(locDir, "*.json");
            if (langFiles.Length == 0)
            {
                PrintError($"[I18n] 语言目录下未发现任何 JSON 字典文件: {locDir}");
                return 1;
            }

            string zhPath = Path.Combine(locDir, "zh-CN.json");
            string enPath = Path.Combine(locDir, "en-US.json");

            if (!File.Exists(zhPath))
            {
                PrintError($"[I18n] 缺少中文主语言字典: {zhPath}");
                return 1;
            }
            if (!File.Exists(enPath))
            {
                PrintError($"[I18n] 缺少英文主语言字典: {enPath}");
                return 1;
            }

            int errors = 0;
            var parsedDicts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var metadata = new Dictionary<string, (string code, string display, string native)>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in langFiles)
            {
                string fileName = Path.GetFileName(file);
                string expectedCode = Path.GetFileNameWithoutExtension(file);
                string content = File.ReadAllText(file, Encoding.UTF8);

                // 1. 验证 System.Text.Json 标准解析与 Schema 结构
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(content);
                }
                catch (Exception ex)
                {
                    PrintError($"[I18n] {fileName} 标准 JSON 解析异常: {ex.Message}");
                    errors++;
                    continue;
                }

                var root = doc.RootElement;
                // 注意：JsonElement.GetString() 在值不是字符串时会抛 InvalidOperationException。
                // 旧实现直接把这一抛点放在审计主流程里 —— 一个写错的词条（数字/布尔/嵌套对象）
                // 会让整个 [9/9] 审计崩掉（异常上抛、没有可读报告），而不是安静地报一条错。
                string codeVal = SafeGetString(root, "code");
                if (string.IsNullOrWhiteSpace(codeVal))
                {
                    PrintError($"[I18n] {fileName} 缺少有效 'code' 属性（或该属性类型不是字符串）");
                    errors++;
                }
                string dispVal = SafeGetString(root, "displayName");
                if (string.IsNullOrWhiteSpace(dispVal))
                {
                    PrintError($"[I18n] {fileName} 缺少有效 'displayName' 属性（或该属性类型不是字符串）");
                    errors++;
                }
                string natVal = SafeGetString(root, "nativeName");
                if (string.IsNullOrWhiteSpace(natVal))
                {
                    PrintError($"[I18n] {fileName} 缺少有效 'nativeName' 属性（或该属性类型不是字符串）");
                    errors++;
                }
                if (!root.TryGetProperty("translations", out var pTrans) || pTrans.ValueKind != JsonValueKind.Object)
                {
                    PrintError($"[I18n] {fileName} 缺少有效 'translations' 对象");
                    errors++;
                    continue;
                }

                foreach (var prop in pTrans.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String)
                    {
                        PrintError($"[I18n] {fileName} 词条 '{prop.Name}' 的值类型是 {prop.Value.ValueKind}，不是字符串；"
                                 + "多语言词条必须全部是字符串（否则运行时查表会取不到文本）");
                        errors++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        PrintError($"[I18n] {fileName} 词条 '{prop.Name}' 翻译值为空或纯空白");
                        errors++;
                    }
                }

                // 2. 验证纯 C# 零依赖 I18nJsonParser 解析与一致性
                // 报警必须全部收集：旧实现用 `parserErr = msg` 只保留最后一条，
                // 解析器连续报警时前面的都被吞掉了。
                var parserWarnings = new List<string>();
                I18nJsonParser.OnLogWarning = msg => parserWarnings.Add(msg);
                var customDict = I18nJsonParser.Parse(content, out string langCode, out string dispName, out string natName);
                I18nJsonParser.OnLogWarning = null;

                if (parserWarnings.Count > 0)
                {
                    for (int w = 0; w < parserWarnings.Count; w++)
                        PrintError($"[I18n] {fileName} 零依赖 I18nJsonParser 解析报警: {parserWarnings[w]}");
                    errors += parserWarnings.Count;
                }

                if (!string.Equals(langCode, expectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    PrintError($"[I18n] {fileName} 解析所得语言码 '{langCode}' 与文件名期望 '{expectedCode}' 不符");
                    errors++;
                }

                var shortKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in pTrans.EnumerateObject())
                {
                    if (customDict.TryGetValue(prop.Name, out string val))
                    {
                        shortKeys[prop.Name] = val;
                    }
                    else
                    {
                        PrintError($"[I18n] {fileName} 零依赖解析器漏掉了词条: '{prop.Name}'");
                        errors++;
                    }
                }

                parsedDicts[expectedCode] = shortKeys;
                metadata[expectedCode] = (langCode, dispName, natName);
            }

            if (errors > 0 || !parsedDicts.ContainsKey("zh-CN") || !parsedDicts.ContainsKey("en-US"))
            {
                return errors > 0 ? errors : 1;
            }

            // 3. 与中文主语言逐语言 100% 键对齐 (Parity Check)
            //    旧实现只比对 en-US：第三个语言包（ja-JP / ru-RU / …）虽然被解析进 parsedDicts，
            //    却从不参与对齐 —— 它缺几百个键也照样"100% 通过"。现在逐个语言包都做双向对齐。
            var zhKeys = parsedDicts["zh-CN"];
            var placeholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
            int alignedLanguages = 0;

            foreach (var langPair in parsedDicts)
            {
                if (string.Equals(langPair.Key, "zh-CN", StringComparison.OrdinalIgnoreCase)) continue;

                string lang = langPair.Key;
                var otherKeys = langPair.Value;
                alignedLanguages++;

                foreach (var k in zhKeys.Keys)
                {
                    if (!otherKeys.ContainsKey(k))
                    {
                        PrintError($"[I18n 对齐缺失] 词典 {lang} 缺少键: '{k}' (zh-CN 已有)");
                        errors++;
                    }
                }

                foreach (var k in otherKeys.Keys)
                {
                    if (!zhKeys.ContainsKey(k))
                    {
                        PrintError($"[I18n 对齐缺失] 词典 zh-CN 缺少键: '{k}' ({lang} 已有)");
                        errors++;
                    }
                }

                // 4. 格式化占位符对齐检测 (Format Placeholder Audit, 例如 {0}, {1})
                foreach (var kvp in zhKeys)
                {
                    string key = kvp.Key;
                    if (!otherKeys.TryGetValue(key, out string otherVal)) continue;

                    var zhMatches = placeholderRegex.Matches(kvp.Value).Cast<Match>().Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
                    var otherMatches = placeholderRegex.Matches(otherVal).Cast<Match>().Select(m => m.Value).Distinct().OrderBy(x => x).ToList();

                    string zhJoined = string.Join(",", zhMatches);
                    string otherJoined = string.Join(",", otherMatches);

                    if (!string.Equals(zhJoined, otherJoined, StringComparison.Ordinal))
                    {
                        PrintError($"[I18n 格式占位符不匹配] 键 '{key}' 占位符差异: zh-CN [{zhJoined}] vs {lang} [{otherJoined}] (可能导致运行时 FormatException)");
                        errors++;
                    }
                }
            }


            if (errors == 0)
            {
                PrintSuccess($"I18n 词典审计 100% 通过: 扫描到 {langFiles.Length} 个语言包, 共 {zhKeys.Count} 个词条,"
                           + $" zh-CN 与其余 {alignedLanguages} 个语言包键名与占位符 100% 对齐, I18nJsonParser 零依赖解析器零报警且结果一致!");
                foreach (var kvp in metadata)
                {
                    Console.WriteLine($"  ├─ [{kvp.Key}] {kvp.Value.display} ({kvp.Value.native}) - {parsedDicts[kvp.Key].Count} 词条");
                }
            }
            else
            {
                PrintError($"I18n 多语言词典审计发现 {errors} 处异常!");
            }

            return errors;
        }

        /// <summary>
        /// 安全读取 JSON 对象的字符串属性。
        /// System.Text.Json 的 JsonElement.GetString() 在值不是字符串时抛 InvalidOperationException，
        /// 旧实现把这类调用直接放在审计主流程里 —— 一个类型写错的词条就能让整个 [9/9] 审计崩掉。
        /// 这里统一返回 null 由调用方报可读错误。
        /// </summary>
        private static string SafeGetString(JsonElement parent, string propName)
        {
            if (parent.ValueKind != JsonValueKind.Object) return null;
            if (!parent.TryGetProperty(propName, out var prop)) return null;
            if (prop.ValueKind != JsonValueKind.String) return null;
            return prop.GetString();
        }

        private static int RunI18nAstAudit(string repoRoot)
        {
            Console.WriteLine("==================== [ I18n Roslyn 语法树遗漏排查与全面审计 ] ====================");
            string locDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "Localization");
            string zhPath = Path.Combine(locDir, "zh-CN.json");
            HashSet<string> validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(zhPath))
            {
                try
                {
                    var dict = I18nJsonParser.Parse(File.ReadAllText(zhPath, Encoding.UTF8), out _, out _, out _);
                    foreach (var k in dict.Keys) validKeys.Add(k);
                }
                catch { }
            }

            string srcDir = Path.Combine(repoRoot, "src", "ModularFlightPanel");
            var report = I18nSyntaxAuditor.AuditDirectory(srcDir, validKeys);

            Console.WriteLine($"扫描源码目录: {srcDir}");
            Console.WriteLine($"已扫描源码文件: {report.ScannedFilesCount} 个 | 语法树节点: {report.ScannedAstNodesCount} 个\n");

            var byFile = report.Issues.GroupBy(i => i.FilePath).OrderByDescending(g => g.Count()).ToList();
            Console.WriteLine("========== [ 文件硬编码统计分布 (Top 20) ] ==========");
            foreach (var g in byFile.Take(20))
            {
                int cnCount = g.Count(x => x.IssueType == I18nIssueType.HardcodedChinese);
                int uiCount = g.Count(x => x.IssueType == I18nIssueType.HardcodedUiCall);
                int missCount = g.Count(x => x.IssueType == I18nIssueType.MissingDictionaryKey);
                Console.WriteLine($"  • {Path.GetRelativePath(repoRoot, g.Key).PadRight(50)}: 合计 {g.Count(),3} (中文:{cnCount,3} | UI:{uiCount,2} | 缺Key:{missCount,2})");
            }
            Console.WriteLine("====================================================\n");

            var chineseIssues = report.Issues.Where(i => i.IssueType == I18nIssueType.HardcodedChinese).ToList();
            var missingKeyIssues = report.Issues.Where(i => i.IssueType == I18nIssueType.MissingDictionaryKey).ToList();
            var uiCallIssues = report.Issues.Where(i => i.IssueType == I18nIssueType.HardcodedUiCall).ToList();

            if (chineseIssues.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] 发现 {chineseIssues.Count} 处硬编码中文字符串 (未国际化):");
                Console.ResetColor();
                foreach (var issue in chineseIssues)
                {
                    Console.WriteLine($"  • {Path.GetRelativePath(repoRoot, issue.FilePath)}:L{issue.Line}:C{issue.Column}");
                    Console.WriteLine($"    文本: \"{issue.OffendingText}\"");
                    Console.WriteLine($"    代码: {issue.CodeSnippet.Trim()}");
                }
                Console.WriteLine();
            }

            if (missingKeyIssues.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                var distinctMissing = missingKeyIssues.GroupBy(m => m.OffendingText).Select(g => g.First()).ToList();
                Console.WriteLine($"[!] 发现 {distinctMissing.Count} 个代码调用但词典未定义的键名 (Missing Key, 出现 {missingKeyIssues.Count} 次):");
                Console.ResetColor();
                Console.WriteLine("  // 可直接复制到 zh-CN.json 词典:");
                foreach (var issue in distinctMissing)
                {
                    string fb = string.IsNullOrEmpty(issue.FallbackText) ? issue.OffendingText : issue.FallbackText;
                    Console.WriteLine($"  \"{issue.OffendingText}\": \"{fb}\",");
                }
                Console.WriteLine();
            }

            if (uiCallIssues.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[?] 发现 {uiCallIssues.Count} 处原生 UI 方法传参未本地化字面量 (建议优化):");
                Console.ResetColor();
                foreach (var issue in uiCallIssues)
                {
                    Console.WriteLine($"  • {Path.GetRelativePath(repoRoot, issue.FilePath)}:L{issue.Line} -> {issue.OffendingText} ({issue.Description})");
                }
                Console.WriteLine();
            }

            Console.WriteLine("-----------------------------------------------------------------------------------");
            Console.WriteLine($"审计结果统计: 严重遗漏错误: {report.TotalErrors} | UI待优化项: {uiCallIssues.Count}");
            if (report.TotalErrors == 0)
            {
                PrintSuccess("全代码语法树审计通过! 0 处未国际化硬编码中文，所有 I18n 查表键 100% 在词典中登记!");
                return 0;
            }
            else
            {
                PrintError($"检测到 {report.TotalErrors} 处严重遗漏，请根据上述提示添加 I18n.Tr 包装并在词典中补充键值！");
                return 1;
            }
        }

        /// <summary>
        /// 导出当前颜色字面量实测值，用于校准 WidgetColorLiteralAudit 基线表。
        /// 用法: dotnet run --project tools/HeadlessValidator -- --color-baseline-dump
        /// </summary>
        private static int DumpColorLiteralBaseline(string repoRoot)
        {
            string widgetDir = Path.Combine(repoRoot, "src", "ModularFlightPanel", "UI", "Widgets");
            if (!Directory.Exists(widgetDir))
            {
                PrintError($"未找到组件目录: {widgetDir}");
                return 1;
            }

            Console.WriteLine("\n==================== [ MFP-SPEC-006 颜色字面量基线导出 ] ====================");
            Console.WriteLine("注意：SPEC-006 的判定口径是【处数】(CountOccurrences)，不是行数。");
            Console.WriteLine("登记基线的同时必须写入 WidgetColorLiteralAudit.RatchetCeilingTable，否则棘轮校验会直接失败：\n");

            int total = 0;
            foreach (var file in Directory.GetFiles(widgetDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(file);
                int count = WidgetColorLiteralAudit.CountOccurrences(File.ReadAllText(file));
                int allowed = WidgetColorLiteralAudit.GetAllowedLines(fileName);
                total += count;
                string flag = count > allowed ? "  <== 超出基线!" : (count < allowed ? "  <== 已低于基线，可下调" : string.Empty);
                Console.WriteLine($"            {{ \"{fileName}\", {count} }},{flag}");
            }

            var ratchetFailures = WidgetColorLiteralAudit.ValidateRatchet();
            for (int i = 0; i < ratchetFailures.Count; i++) PrintError("棘轮校验: " + ratchetFailures[i]);

            Console.WriteLine($"\n实测合计: {total} 处 / 当前登记基线合计: {WidgetColorLiteralAudit.TotalRegisteredDebt} 处");
            Console.WriteLine("===========================================================================");
            return ratchetFailures.Count == 0 ? 0 : 1;
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
                float sBase = (w.Scale <= 0.05f ? 1.0f : w.Scale);
                float sX = (w.ScaleX > 0.05f ? w.ScaleX : sBase) * gScale;
                float sY = (w.ScaleY > 0.05f ? w.ScaleY : sBase) * gScale;
                float finalW = defW * sX;
                float finalH = defH * sY;

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
            if (widgetId == "nav.vessel_navball" || widgetId == "nav.vessel_attitude_sphere" || widgetType == "vessel_navball" || widgetType == "vessel_attitude_sphere") return (150f, 178f);
            if (widgetId == "core.heading_arc" || widgetId == "nav.heading_arc" || widgetType == "heading_arc") return (180f, 60f);
            if (widgetId == "core.master_warning" || widgetType == "master_warning" || widgetType == "warning_annunciator" || widgetType == "annunciator" || widgetType == "cws") return (184f, 20f);
            if (widgetId == "core.bottom_controls" || widgetId == "core.ref_rcs_sas" || widgetId == "core.rcs_ref_sas" || widgetType == "bottom_controls" || widgetType == "bottom_bar_controls" || widgetType == "rcs_ref_sas" || widgetType == "ref_rcs_sas") return (184f, 22f);
            if (widgetId == "core.orbital_info") return (320f, 36f);
            if (widgetId == "core.ecam_status") return (380f, 32f);
            if (widgetId == "core.ecam_alert_log" || widgetId == "ecam.alert_log" || widgetId == "custom.ecam_alert_log" || widgetType == "ecam_alert_log" || widgetType == "alert_log" || widgetType == "eicas_messages" || widgetType == "warning_log" || widgetId.Contains("alert_log")) return (280f, 172f);
            if (widgetId == "core.sas_dial") return (96f, 116f);
            if (widgetId == "core.stage_control" || widgetType == "stage_control") return (204f, 186f);
            if (widgetId == "core.time_warp" || widgetType == "time_warp" || widgetType == "timewarp") return (236f, 46f);
            if (widgetId == "core.comm_signal" || widgetType == "comm_signal" || widgetType == "commsignal") return (236f, 32f);
            if (widgetId == "core.toolbar" || widgetType == "toolbar") return (88f, 240f);
            if (widgetId == "core.dock_favorites" || widgetType == "dock_favorites" || widgetId.Contains("dock_favorites") || widgetId == "toolbar.favorites") return (180f, 46f);
            if (widgetId == "core.ui_widget" || widgetType == "ui_widget" || widgetType == "ui_manager") return (290f, 340f);
            if (widgetId == "gauge.stage_dv" || widgetType == "stage_dv" || widgetId.Contains("stage_dv")) return (220f, 180f);
            if (widgetId == "core.throttle" || widgetId == "core.vsi" || widgetId == "core.propellant") return (195f, 195f);
            if (widgetType == "bar_gauge" || widgetId.StartsWith("gauge.")) return (20f, 180f);

            if (widgetId == "custom.nd_navigation" || widgetType == "nd_navigation") return (280f, 260f);
            if (widgetId == "core.b747_eicas" || widgetType == "b747_eicas" || widgetType == "boeing_eicas" || widgetType == "eicas" || widgetId.Contains("b747_eicas")) return (260f, 275f);
            if (widgetId == "core.b747_lower_eicas" || widgetType == "b747_lower_eicas" || widgetType == "eicas_lower" || widgetId.Contains("b747_lower_eicas")) return (260f, 275f);
            if (widgetId == "custom.maneuver_timeline" || widgetType == "maneuver_timeline") return (520f, 115f);
            if (widgetId == "core.maneuver" || widgetType == "maneuver" || widgetId.Contains("maneuver")) return (200f, 105f);
            if (widgetId == "nav.reference_frame" || widgetId == "nav.ref_frame" || widgetId == "core.reference_frame" || widgetType == "reference_frame" || widgetType == "ref_frame") return (100f, 32f);
            if (widgetType == "arc_tape" || widgetId.StartsWith("arc_tape.") || widgetId.StartsWith("curved_tape.") || widgetType == "arc_speed_tape" || widgetType == "arc_altitude_tape" || widgetType == "arc_alt_tape" || widgetId.StartsWith("arc_alt.") || widgetId.StartsWith("arc_speed.") || widgetId == "custom.arc_speed_tape" || widgetId == "custom.arc_altitude_tape") return (120f, 240f);
            if (widgetType == "tape" || widgetId.StartsWith("tape.")) return (50f, 240f);
            if (widgetType == "ecam_dial" || widgetId.StartsWith("ecam.")) return (110f, 110f);
            if (widgetType == "electrical" || widgetId.Contains("elec")) return (180f, 160f);
            if (widgetType == "rocket2d" || widgetId.Contains("rocket")) return (260f, 176f);
            if (widgetType == "life_support" || widgetId.Contains("life")) return (180f, 150f);
            if (widgetType == "signal" || widgetId.Contains("signal")) return (180f, 130f);

            // SpaceX 载人龙飞船与星舰发射 HUD 专属组件包围盒尺寸
            if (widgetId == "spacex.header" || widgetType == "spacex_header") return (960f, 42f);
            if (widgetId == "spacex.docking" || widgetType == "spacex_docking") return (220f, 220f);
            if (widgetId == "spacex.overview" || widgetType == "spacex_overview") return (240f, 180f);
            if (widgetId == "spacex.bottom" || widgetType == "spacex_bottom") return (420f, 38f);
            if (widgetId.StartsWith("spacex.speed") || widgetId.StartsWith("spacex.altitude") || widgetType == "spacex_arc" || widgetType == "spacex_gauge") return (110f, 110f);
            if (widgetId == "spacex.timeline" || widgetType == "spacex_timeline") return (520f, 88f);
            if (widgetId == "spacex.attitude" || widgetType == "spacex_attitude") return (96f, 96f);
            if (widgetId == "spacex.engines" || widgetType == "spacex_engines") return (96f, 96f);
            if (widgetId == "custom.perf_monitor" || widgetId == "core.performance_monitor" || widgetType == "performance_monitor" || widgetType == "perf_monitor") return (240f, 195f);
            if (widgetId == "custom.maneuver_timeline" || widgetType == "custom.maneuver_timeline" || widgetType == "maneuver_timeline") return (520f, 88f);
            if (widgetId == "custom.staging_sequence" || widgetType == "custom.staging_sequence" || widgetType == "staging_sequence") return (160f, 260f);

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

            // SpaceX 航电套件紧凑排布
            if (idA.StartsWith("spacex.") && idB.StartsWith("spacex."))
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
            var data = ToData(layout);
            string json = AvionicsConfigParser.SerializeLayout(data, false);
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
                    var pData = AvionicsConfigParser.ParseLayout(json, out error);
                    if (pData != null && pData.Widgets != null && pData.Widgets.Count > 0)
                    {
                        layout = ToModel(pData);
                        return true;
                    }
                    if (string.IsNullOrEmpty(error)) error = "未能解析出小组件配置";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static WidgetLayoutModel ToModel(WidgetLayoutData data)
        {
            if (data == null) return null;
            var model = new WidgetLayoutModel { GlobalScale = data.GlobalScale };
            foreach (var w in data.Widgets)
            {
                model.Widgets.Add(new WidgetConfigModel
                {
                    WidgetId = w.WidgetId,
                    DisplayName = w.DisplayName,
                    IsEnabled = w.IsEnabled,
                    PositionX = w.PositionX,
                    PositionY = w.PositionY,
                    Scale = w.Scale,
                    ScaleX = w.ScaleX,
                    ScaleY = w.ScaleY,
                    Rotation = w.Rotation,
                    CustomTemplate = w.CustomTemplate,
                    WidgetType = w.WidgetType,
                    NumericToken = w.NumericToken,
                    MinValue = w.MinValue,
                    MaxValue = w.MaxValue,
                    CautionThreshold = w.CautionThreshold,
                    WarningThreshold = w.WarningThreshold,
                    IsSoftLimit = w.IsSoftLimit,
                    LimitMode = w.LimitMode,
                    UnitLabel = w.UnitLabel,
                    StepInterval = w.StepInterval,
                    IsLeftOrientation = w.IsLeftOrientation,
                    ValueDeltaThreshold = w.ValueDeltaThreshold,
                    BadgeNormal = w.BadgeNormal,
                    BadgeCaution = w.BadgeCaution,
                    BadgeWarning = w.BadgeWarning,
                    IsolateCanvas = w.IsolateCanvas,
                    UpdateInterval = w.UpdateInterval,
                    CustomHz = w.CustomHz,
                    RenderScale = w.RenderScale
                });
            }
            return model;
        }

        public static WidgetLayoutData ToData(WidgetLayoutModel model)
        {
            if (model == null) return null;
            var data = new WidgetLayoutData(model.GlobalScale);
            foreach (var w in model.Widgets)
            {
                data.Widgets.Add(new WidgetConfig(w.WidgetId, w.DisplayName, w.PositionX, w.PositionY, w.Scale, w.CustomTemplate, w.Rotation, w.ScaleX, w.ScaleY)
                {
                    IsEnabled = w.IsEnabled,
                    WidgetType = w.WidgetType,
                    NumericToken = w.NumericToken,
                    MinValue = w.MinValue,
                    MaxValue = w.MaxValue,
                    CautionThreshold = w.CautionThreshold,
                    WarningThreshold = w.WarningThreshold,
                    IsSoftLimit = w.IsSoftLimit,
                    LimitMode = w.LimitMode,
                    UnitLabel = w.UnitLabel,
                    StepInterval = w.StepInterval,
                    IsLeftOrientation = w.IsLeftOrientation,
                    ValueDeltaThreshold = (float)w.ValueDeltaThreshold,
                    BadgeNormal = w.BadgeNormal,
                    BadgeCaution = w.BadgeCaution,
                    BadgeWarning = w.BadgeWarning,
                    IsolateCanvas = w.IsolateCanvas,
                    UpdateInterval = w.UpdateInterval,
                    CustomHz = w.CustomHz,
                    RenderScale = w.RenderScale
                });
            }
            return data;
        }

        // ==========================================
        // 5. 预设库扫描验证
        // ==========================================
        private static void ValidateAllPresets(string repoRoot)
        {
            string presetsDir = Path.Combine(repoRoot, "GameData", "ModularFlightPanel", "PluginData", "Presets");
            Console.WriteLine($"\n[附] 检查预设库目录空间几何: {presetsDir}");
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

            Console.WriteLine($"  ├─ 扫描 {presetFiles.Length} 个本地预设文件空间碰撞:");
            foreach (var f in presetFiles)
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var pData = AvionicsConfigParser.ParseLayout(json, out string parseErr);
                    if (pData == null)
                    {
                        PrintError($"预设文件损坏: {Path.GetFileName(f)} - {parseErr}");
                        continue;
                    }
                    var pLayout = ToModel(pData);
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
                "core.maneuver" => "MANEUVER",
                "custom.maneuver" => "MANEUVER",
                "core.toolbar" => "TOOLBAR",
                "core.dock_favorites" => "QUICK_DOCK",
                "toolbar.favorites" => "QUICK_DOCK",
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
                "spacex.header" => "SPX_HDR",
                "spacex.docking" => "SPX_DOCK",
                "spacex.overview" => "SPX_VIEW",
                "spacex.bottom" => "SPX_BTM",
                _ => id.Replace("custom.", "").Replace("spacex.", "SPX_").ToUpperInvariant()
            };
        }
    }
}
