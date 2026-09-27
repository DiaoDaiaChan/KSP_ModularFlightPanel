using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI.Auditing
{
    /// <summary>
    /// 组件架构演进状态分类
    /// </summary>
    public enum WidgetModernizationStatus
    {
        /// <summary>
        /// 现代声明式微控件体系 (已标准化: 显式 BaseSize + 语义泊靠微控件 DSL / 自动卡片底板)
        /// </summary>
        ModernDSL,

        /// <summary>
        /// 核心 3D 姿态仿真与渲染引擎 (姿态球等特殊 3D 渲染组件)
        /// </summary>
        Core3D,

        /// <summary>
        /// 旧版过程式命令组装实现 (待标准化改造: 依赖 UIFactory 过程式布局 / 缺少 BaseSize / 运行时 CPU 软光栅)
        /// </summary>
        LegacyImperative
    }

    /// <summary>
    /// 单个组件现代化审计明细项
    /// </summary>
    public class WidgetModernizationItem
    {
        public string WidgetName { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public WidgetModernizationStatus Status { get; set; }
        public bool HasBaseSize { get; set; }
        public bool HasAutoCardFrame { get; set; }
        public bool UsesMicroControlsDsl { get; set; }
        public bool UsesImperativeUiFactory { get; set; }
        public List<string> MissingModernFeatures { get; } = new List<string>();

        // ── 航电代码质量与反模式治理指标 (Code Reuse & Anti-Pattern Metrics) ──
        public bool HasRedundantTemplateParser { get; set; }
        public List<string> RedundantFormattingMethods { get; } = new List<string>();
        public int RedundantDirtyTrackingFields { get; set; }
        public bool UsesStandardizedChannels { get; set; }
        public bool UsesStandardizedFormatting { get; set; }
        public bool UsesSetTextIfChanged { get; set; }
        public List<string> StandardizationSuggestions { get; } = new List<string>();

        public string GetMissingSummary()
        {
            if (MissingModernFeatures.Count == 0) return "Standardized (已符合现代微控件架构规范)";
            return string.Join("; ", MissingModernFeatures);
        }
    }

    /// <summary>
    /// 全局航电组件现代化合规性审计汇总报告
    /// </summary>
    public class WidgetModernizationReport
    {
        public int TotalCount => Items.Count;
        public int ModernCount => Items.Count(i => i.Status == WidgetModernizationStatus.ModernDSL);
        public int Core3DCount => Items.Count(i => i.Status == WidgetModernizationStatus.Core3D);
        public int LegacyCount => Items.Count(i => i.Status == WidgetModernizationStatus.LegacyImperative);

        public float ModernizationPercentage => TotalCount > 0
            ? ((float)(ModernCount + Core3DCount) / TotalCount) * 100f
            : 0f;

        // ── 代码治理宏观指标 ──
        public int RedundantTemplateParserCount => Items.Count(i => i.HasRedundantTemplateParser);
        public int RedundantFormattingMethodCount => Items.Count(i => i.RedundantFormattingMethods.Count > 0);
        public int StandardizedChannelAdoptionCount => Items.Count(i => i.UsesStandardizedChannels);
        public int StandardizedFormattingAdoptionCount => Items.Count(i => i.UsesStandardizedFormatting);

        public List<WidgetModernizationItem> Items { get; } = new List<WidgetModernizationItem>();

        public IEnumerable<WidgetModernizationItem> LegacyWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.LegacyImperative);

        public IEnumerable<WidgetModernizationItem> ModernWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.ModernDSL);

        public IEnumerable<WidgetModernizationItem> Core3DWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.Core3D);

        public IEnumerable<WidgetModernizationItem> WidgetsWithAntiPatterns =>
            Items.Where(i => i.HasRedundantTemplateParser || i.RedundantFormattingMethods.Count > 0 || i.RedundantDirtyTrackingFields > 3);
    }

    /// <summary>
    /// 航电组件架构合规性与现代化改造审计内核 (Avionics Widget Modernization Auditor - Roslyn AST 驱动)
    /// </summary>
    public static class WidgetModernizationAudit
    {
        private static readonly HashSet<string> MicroControlTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TextWidget", "GaugeWidget", "LinearBarWidget", "TapeWidget",
            "StateWidget", "IconWidget", "ToggleButtonWidget", "ActionButtonWidget"
        };

        /// <summary>
        /// 执行全量组件现代化合规性扫描（复用 WidgetSourceAudit 的组件继承图，与文件所在目录无关）
        /// </summary>
        public static WidgetModernizationReport Scan(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
            {
                repoRoot = WidgetSourceAudit.ResolveRepositoryRoot();
            }
            return Scan(WidgetSourceAudit.Discover(repoRoot));
        }

        /// <summary>
        /// 基于发现层结果扫描：作用域 = 继承图里的具体组件类
        /// （旧实现只按 UI/Widgets 目录 + 直接基类名 + 每文件首个匹配类来取样，会静默漏掉别名类与跨目录组件）
        /// </summary>
        public static WidgetModernizationReport Scan(WidgetDiscoveryResult discovery)
        {
            var report = new WidgetModernizationReport();
            if (discovery == null || !discovery.CanAuditSource || discovery.Graph == null) return report;

            var graph = discovery.Graph;
            foreach (var node in graph.ContractClasses)
            {
                if (node.IsAbstract) continue;   // 抽象基类不直接出货，不计入现代化进度

                ClassDeclarationSyntax widgetClass = node.Decl;
                string widgetName = node.Name;
                string fileName = node.FileName;
                string filePath = node.FilePath;

                var item = new WidgetModernizationItem
                {
                    WidgetName = widgetName,
                    FileName = fileName,
                    FilePath = filePath
                };

                // Core3D：由继承链结构判定（旧实现按"类名里是否含 NavballSphereWidget"猜测）
                bool isCore3D = node.AnyInChain(n => string.Equals(n.Name, WidgetSpecRules.Core3DBaseType, StringComparison.Ordinal));

                // ── 语法树 AST 深度特征检测 ──
                // 1. BaseSize 属性重写检测
                var baseSizeProp = widgetClass.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == "BaseSize" && RoslynAstHelper.HasModifier(p, SyntaxKind.OverrideKeyword));
                item.HasBaseSize = baseSizeProp != null;

                // 2. AutoCreateCardFrame 属性重写检测
                var cardFrameProp = widgetClass.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == "AutoCreateCardFrame");
                item.HasAutoCardFrame = cardFrameProp != null;

                // 3. 微控件 DSL 字段与声明检测
                int microControlFields = 0;
                foreach (var field in widgetClass.Members.OfType<FieldDeclarationSyntax>())
                {
                    string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);
                    if (MicroControlTypes.Contains(typeName)) microControlFields++;
                }
                foreach (var prop in widgetClass.Members.OfType<PropertyDeclarationSyntax>())
                {
                    string typeName = RoslynAstHelper.GetSimpleTypeName(prop.Type);
                    if (MicroControlTypes.Contains(typeName)) microControlFields++;
                }

                // 也检测方法体中是否调用了 Controls.Add / TextWidget.* / LinearBarWidget.*
                bool hasControlsInvocation = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        string expr = inv.Expression.ToString();
                        return expr.StartsWith("Controls.Add", StringComparison.Ordinal) ||
                               expr.StartsWith("TextWidget.", StringComparison.Ordinal) ||
                               expr.StartsWith("LinearBarWidget.", StringComparison.Ordinal);
                    });

                item.UsesMicroControlsDsl = microControlFields > 0 || hasControlsInvocation;

                // 4. 命令式 UIFactory 调用检测
                int uiFactoryCalls = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Count(inv => inv.Expression.ToString().StartsWith("UIFactory.", StringComparison.Ordinal));
                item.UsesImperativeUiFactory = uiFactoryCalls > 0;

                // 5. 运行时 CPU 像素级软光栅化反模式侦测 (SetPixels32 / _texPixels 动态贴图)
                bool hasCpuRasterizer = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression.ToString().EndsWith(".SetPixels32", StringComparison.Ordinal)) ||
                    widgetClass.Members.OfType<FieldDeclarationSyntax>()
                    .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == "_texPixels"));

                // 6. 刷新率阶梯滥用侦测：生效取值 + 声明式满帧依据与 SPEC-002 复用同一份判定
                //    （旧实现自带一份类名名单，与 SPEC-002 的文件名名单各说各话，两处都可能漂移）
                bool isCritical = graph.EffectiveTierMembers(node).Contains(WidgetSpecRules.FullFrameTierMember);
                bool abusesCriticalTier = isCritical && !WidgetClassGraph.IsHighFrequencyDeclared(node);

                // ── 综合现代化架构分类判定 ──
                if (isCore3D)
                {
                    item.Status = WidgetModernizationStatus.Core3D;
                }
                else if (hasCpuRasterizer)
                {
                    // 命中 CPU 纯软光栅反模式，坚决不能判为已标准化
                    item.Status = WidgetModernizationStatus.LegacyImperative;
                    item.MissingModernFeatures.Add("Contains unstandardized CPU software rasterizer (SetPixels32/_texPixels; must migrate to GPU procedural mesh or Core3D)");
                    if (abusesCriticalTier)
                    {
                        item.MissingModernFeatures.Add("RefreshTier.Critical is invalid for non-attitude widget (violates SPEC-002; must use Standard/Relaxed)");
                    }
                    if (item.UsesImperativeUiFactory)
                    {
                        item.MissingModernFeatures.Add("Incomplete DSL modernization (imperative UIFactory readouts remain unstandardized)");
                    }
                }
                else if (item.HasBaseSize && (item.UsesMicroControlsDsl || item.HasAutoCardFrame))
                {
                    item.Status = WidgetModernizationStatus.ModernDSL;
                }
                else
                {
                    item.Status = WidgetModernizationStatus.LegacyImperative;

                    if (!item.HasBaseSize)
                    {
                        item.MissingModernFeatures.Add("Missing BaseSize override");
                    }
                    if (item.UsesImperativeUiFactory)
                    {
                        item.MissingModernFeatures.Add("Uses imperative UIFactory layout");
                    }
                    if (!item.UsesMicroControlsDsl)
                    {
                        item.MissingModernFeatures.Add("Missing micro-controls DSL");
                    }
                    if (!item.HasAutoCardFrame)
                    {
                        item.MissingModernFeatures.Add("Missing AutoCreateCardFrame");
                    }
                }
                // 7. 私有模板解析器反模式检测 (ParseCustomTemplate / CustomTemplate.Split)
                bool hasRedundantParser = widgetClass.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => string.Equals(m.Identifier.Text, WidgetSpecRules.BannedTemplateParserMethod, StringComparison.Ordinal));
                bool hasSplitCustomTemplate = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        string expr = inv.Expression.ToString();
                        return expr.EndsWith(".Split", StringComparison.Ordinal) &&
                               inv.ToString().Contains("CustomTemplate");
                    });
                item.HasRedundantTemplateParser = hasRedundantParser || hasSplitCustomTemplate;
                if (item.HasRedundantTemplateParser)
                {
                    item.StandardizationSuggestions.Add("Contains private ParseCustomTemplate (migrate to BaseFlightWidget.GetTemplateChannel)");
                }

                // 8. 私有时序/度量格式化方法反模式检测
                foreach (var method in widgetClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    string mName = method.Identifier.Text;
                    for (int f = 0; f < WidgetSpecRules.BannedFormattingMethods.Length; f++)
                    {
                        if (string.Equals(mName, WidgetSpecRules.BannedFormattingMethods[f], StringComparison.Ordinal))
                        {
                            item.RedundantFormattingMethods.Add(mName);
                            item.StandardizationSuggestions.Add($"Contains private {mName} (migrate to AvionicsFormatting / BaseFlightWidget)");
                            break;
                        }
                    }
                }

                // 9. 冗余手工脏标记字段统计 (_last*Text, _last*Val, _last*Str)
                int dirtyFields = 0;
                foreach (var field in widgetClass.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        string vName = v.Identifier.Text;
                        if (vName.StartsWith("_last", StringComparison.OrdinalIgnoreCase) &&
                            (vName.EndsWith("Text", StringComparison.OrdinalIgnoreCase) ||
                             vName.EndsWith("Str", StringComparison.OrdinalIgnoreCase) ||
                             vName.EndsWith("Val", StringComparison.OrdinalIgnoreCase)))
                        {
                            dirtyFields++;
                        }
                    }
                }
                item.RedundantDirtyTrackingFields = dirtyFields;
                if (dirtyFields > 3)
                {
                    item.StandardizationSuggestions.Add($"Declares {dirtyFields} manual dirty-tracking fields (recommend SetTextIfChanged or micro-controls)");
                }

                // 10. 标准化 API 调用深度检测
                var allInvocations = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
                item.UsesStandardizedChannels = allInvocations.Any(inv =>
                {
                    string expr = inv.Expression.ToString();
                    for (int c = 0; c < WidgetSpecRules.StandardTemplateChannelApis.Length; c++)
                    {
                        if (expr.EndsWith(WidgetSpecRules.StandardTemplateChannelApis[c], StringComparison.Ordinal)) return true;
                    }
                    return false;
                });

                item.UsesStandardizedFormatting = allInvocations.Any(inv =>
                {
                    string expr = inv.Expression.ToString();
                    if (expr.StartsWith(WidgetSpecRules.StandardFormattingClass + ".", StringComparison.Ordinal)) return true;
                    for (int f = 0; f < WidgetSpecRules.StandardFormattingApis.Length; f++)
                    {
                        if (expr.EndsWith(WidgetSpecRules.StandardFormattingApis[f], StringComparison.Ordinal)) return true;
                    }
                    return false;
                });

                item.UsesSetTextIfChanged = allInvocations.Any(inv =>
                    inv.Expression.ToString().EndsWith(WidgetSpecRules.SetTextIfChangedApi, StringComparison.Ordinal));

                report.Items.Add(item);
            }

            // 按状态排序：旧版在前方便审计整改，然后按文件名排序
            report.Items.Sort((a, b) =>
            {
                int sa = a.Status == WidgetModernizationStatus.LegacyImperative ? 0 : (a.Status == WidgetModernizationStatus.ModernDSL ? 1 : 2);
                int sb = b.Status == WidgetModernizationStatus.LegacyImperative ? 0 : (b.Status == WidgetModernizationStatus.ModernDSL ? 1 : 2);
                if (sa != sb) return sa.CompareTo(sb);
                return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            return report;
        }

        /// <summary>
        /// 生成供 MSBuild 编译期输出的标准编译器警告 / 提醒文本
        /// 格式: FilePath(1,1): warning MFP_LEGACY_WIDGET: ...
        /// </summary>
        public static string GenerateMSBuildOutput(WidgetModernizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ModularFlightPanel.csproj : warning MFP_MODERNIZATION: [Avionics Modernization Audit] Standardized: {report.ModernCount}/{report.TotalCount} ({report.ModernizationPercentage:F1}%), Core3D: {report.Core3DCount}, Legacy to Modernize: {report.LegacyCount}");

            foreach (var item in report.LegacyWidgets)
            {
                sb.AppendLine($"{item.FilePath}(1,1): warning MFP_LEGACY_WIDGET: [Legacy Widget / Needs Modernization] {item.WidgetName}: {item.GetMissingSummary()}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成终端人类可读的彩色/格式化审计汇总
        /// </summary>
        public static string GenerateTerminalSummary(WidgetModernizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================================");
            sb.AppendLine("         MODULAR FLIGHT PANEL 组件架构合规性与现代化改造审计 (Roslyn AST)");
            sb.AppendLine("=======================================================================");
            sb.AppendLine($"已审计组件总数: {report.TotalCount} 个");
            sb.AppendLine($"现代微控件架构 (ModernDSL):   {report.ModernCount} 个 ({((float)report.ModernCount / Math.Max(1, report.TotalCount) * 100f):F1}%)");
            sb.AppendLine($"核心 3D 渲染引擎 (Core3D):    {report.Core3DCount} 个");
            sb.AppendLine($"待改造旧版组件 (Legacy):     {report.LegacyCount} 个 ({((float)report.LegacyCount / Math.Max(1, report.TotalCount) * 100f):F1}%)");
            sb.AppendLine($"整体现代化合规率:            {report.ModernizationPercentage:F1}%");
            sb.AppendLine("-----------------------------------------------------------------------");

            var modernList = report.ModernWidgets.Select(w => w.WidgetName).ToList();
            if (modernList.Count > 0)
            {
                sb.AppendLine($"✔ 已完成微控件 DSL 标准化改造 ({modernList.Count} 个):");
                sb.AppendLine($"  └─ {string.Join(", ", modernList)}");
            }

            var legacyItems = report.LegacyWidgets.ToList();
            if (legacyItems.Count > 0)
            {
                sb.AppendLine($"⚠ 待改造旧版命令式组件 ({legacyItems.Count} 个):");
                for (int i = 0; i < legacyItems.Count; i++)
                {
                    var item = legacyItems[i];
                    sb.AppendLine($"  ├─ [{i + 1:D2}] {item.FileName,-28} => {item.GetMissingSummary()}");
                }
            }

            sb.AppendLine("-----------------------------------------------------------------------");
            sb.AppendLine("    航电代码精简与标准化治理质量雷达 (Cleanliness & Anti-Pattern Radar)");
            sb.AppendLine("-----------------------------------------------------------------------");
            sb.AppendLine($"标准通道提取率:               {report.StandardizedChannelAdoptionCount}/{report.TotalCount} 个组件已接入 GetTemplateChannel 系列");
            sb.AppendLine($"零私有模板解析达成率:         {(report.TotalCount - report.RedundantTemplateParserCount)}/{report.TotalCount} 个组件已消除私有 ParseCustomTemplate");
            sb.AppendLine($"统一格式化套件复用率:         {(report.TotalCount - report.RedundantFormattingMethodCount)}/{report.TotalCount} 个组件已接入 AvionicsFormatting");
            sb.AppendLine($"手工脏标记字段全面纳管率:     {report.Items.Count(i => i.RedundantDirtyTrackingFields <= 3)}/{report.TotalCount} 个组件已消除字段级脏标记膨胀");

            var antiPatternItems = report.WidgetsWithAntiPatterns.ToList();
            if (antiPatternItems.Count > 0)
            {
                sb.AppendLine("-----------------------------------------------------------------------");
                sb.AppendLine($"▲ 待治理高频重复代码组件清单 ({antiPatternItems.Count} 个):");
                for (int i = 0; i < antiPatternItems.Count; i++)
                {
                    var item = antiPatternItems[i];
                    sb.AppendLine($"  ├─ [{i + 1:D2}] {item.FileName,-28} => {string.Join("; ", item.StandardizationSuggestions)}");
                }
            }

            sb.AppendLine("=======================================================================");
            return sb.ToString();
        }
    }
}
