using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        /// 旧版过程式命令组装实现 (待标准化改造: 依赖 UIFactory 过程式布局 / 缺少 BaseSize)
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

        public List<WidgetModernizationItem> Items { get; } = new List<WidgetModernizationItem>();

        public IEnumerable<WidgetModernizationItem> LegacyWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.LegacyImperative);

        public IEnumerable<WidgetModernizationItem> ModernWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.ModernDSL);

        public IEnumerable<WidgetModernizationItem> Core3DWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.Core3D);
    }

    /// <summary>
    /// 航电组件架构合规性与现代化改造审计内核 (Avionics Widget Modernization Auditor)
    /// 核心功能：
    /// 1. 自动扫描全量飞行仪表组件；
    /// 2. 判定组件是否接入现代声明式微控件 DSL (BaseSize / AutoCreateCardFrame / Controls)；
    /// 3. 精准列出仍使用旧版过程式命令拼装 (UIFactory) 的遗留组件清单及具体缺失项；
    /// 4. 在编译期 (MSBuild Target) 与无头门禁中发出醒目提醒，指导开发者逐步完成标准化改造。
    /// </summary>
    public static class WidgetModernizationAudit
    {
        private static readonly Regex BaseSizeRegex = new Regex(@"override\s+Vector2\s+BaseSize", RegexOptions.Compiled);
        private static readonly Regex ControlsDslRegex = new Regex(@"(TextWidget\.|LinearBarWidget\.|ToggleButtonWidget\.|ActionButtonWidget\.|Controls\.Add)", RegexOptions.Compiled);

        /// <summary>
        /// 执行全量组件现代化合规性扫描
        /// </summary>
        public static WidgetModernizationReport Scan(string repoRoot)
        {
            var report = new WidgetModernizationReport();
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
            {
                repoRoot = ResolveRepositoryRoot();
            }

            string widgetsDir = Path.Combine(repoRoot, "src", "ModularFlightPanel", "UI", "Widgets");
            if (!Directory.Exists(widgetsDir)) return report;

            string[] csFiles = Directory.GetFiles(widgetsDir, "*.cs", SearchOption.AllDirectories);

            for (int i = 0; i < csFiles.Length; i++)
            {
                string filePath = csFiles[i];
                string fileName = Path.GetFileName(filePath);

                // 排除抽象基类与规范模板自身
                if (fileName.Equals("BaseNavballSphereWidget.cs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("StandardFlightWidgetTemplate.cs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("WidgetDslControls.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rawText;
                try
                {
                    rawText = File.ReadAllText(filePath);
                }
                catch
                {
                    continue;
                }

                // 必须是具体的组件类定义
                if (!rawText.Contains(": BaseFlightWidget") &&
                    !rawText.Contains(": BaseNavballSphereWidget") &&
                    !rawText.Contains(": BaseAvionicsWidget"))
                {
                    continue;
                }

                string cleanText = CSharpSourceLinter.Sanitize(rawText);
                string widgetName = Path.GetFileNameWithoutExtension(filePath);

                var item = new WidgetModernizationItem
                {
                    WidgetName = widgetName,
                    FileName = fileName,
                    FilePath = filePath
                };

                // 核心特征侦测
                item.HasBaseSize = BaseSizeRegex.IsMatch(cleanText);
                item.HasAutoCardFrame = cleanText.Contains("AutoCreateCardFrame");
                item.UsesMicroControlsDsl = ControlsDslRegex.IsMatch(cleanText);
                item.UsesImperativeUiFactory = cleanText.Contains("UIFactory.");
                bool isCore3D = cleanText.Contains("BaseNavballSphereWidget");

                if (isCore3D)
                {
                    item.Status = WidgetModernizationStatus.Core3D;
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
            var legacyItems = report.LegacyWidgets.ToList();

            sb.AppendLine($"ModularFlightPanel.csproj : warning MFP_MODERNIZATION: [Avionics Modernization Audit] Standardized: {report.ModernCount}/{report.TotalCount} ({report.ModernizationPercentage:F1}%), Core3D: {report.Core3DCount}, Legacy to Modernize: {report.LegacyCount}");

            for (int i = 0; i < legacyItems.Count; i++)
            {
                var item = legacyItems[i];
                sb.AppendLine($"{item.FilePath}(1,1): warning MFP_LEGACY_WIDGET: [Legacy Widget / Needs Modernization] {item.WidgetName}: {item.GetMissingSummary()}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成供终端控制台与报表展示的高清文本摘要
        /// </summary>
        public static string GenerateTerminalSummary(WidgetModernizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================================");
            sb.AppendLine("         MODULAR FLIGHT PANEL 组件架构合规性与现代化改造审计");
            sb.AppendLine("=======================================================================");
            sb.AppendLine($"已审计组件总数: {report.TotalCount} 个");
            sb.AppendLine($"现代微控件架构 (ModernDSL):   {report.ModernCount} 个 ({report.ModernCount * 100f / Math.Max(1, report.TotalCount):F1}%)");
            sb.AppendLine($"核心 3D 渲染引擎 (Core3D):    {report.Core3DCount} 个");
            sb.AppendLine($"待改造旧版组件 (Legacy):     {report.LegacyCount} 个 ({report.LegacyCount * 100f / Math.Max(1, report.TotalCount):F1}%)");
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

            sb.AppendLine("=======================================================================");
            return sb.ToString();
        }

        private static string ResolveRepositoryRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "KSP_naviball.sln")) ||
                    Directory.Exists(Path.Combine(dir, "GameData", "ModularFlightPanel")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
