using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModularFlightPanel.UI.Auditing;

namespace ModularFlightPanel.HeadlessValidator
{
    /// <summary>
    /// I18n 语法树代码审查发现的问题类型
    /// </summary>
    public enum I18nIssueType
    {
        /// <summary>
        /// 发现硬编码中文字符串（未通过 I18n.Tr / I18n.TrFormat 包装）
        /// </summary>
        HardcodedChinese,

        /// <summary>
        /// UI 文案槽位（UIFactory.CreateText / TextWidget DSL / GetTemplateChannel 兜底值 /
        /// SetTextIfChanged / GUILayout·GUI / .text 赋值 …）中直接传入的可汉化英文字面量。
        /// 中文主语言下这些文本本应接入词典，只有权威缩写（SAS / RCS / VSI …）才允许保留原文。
        /// </summary>
        UntranslatedEnglish,

        /// <summary>
        /// 代码中调用了 I18n.Tr("KEY")，但该 KEY 在 zh-CN.json 或 en-US.json 词典中不存在
        /// </summary>
        MissingDictionaryKey
    }

    /// <summary>
    /// 单条 I18n 审查发现项
    /// </summary>
    public class I18nIssue
    {
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public I18nIssueType IssueType { get; set; }
        public string OffendingText { get; set; } = string.Empty;
        public string FallbackText { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public override string ToString() =>
            $"[{Path.GetFileName(FilePath)}:L{Line}:C{Column}] ({IssueType}) {Description} -> {OffendingText}";
    }

    /// <summary>
    /// I18n 审查报告
    /// </summary>
    public class I18nAuditReport
    {
        public List<I18nIssue> Issues { get; set; } = new List<I18nIssue>();
        public int ScannedFilesCount { get; set; }
        public int ScannedAstNodesCount { get; set; }

        public int HardcodedChineseCount => Issues.Count(i => i.IssueType == I18nIssueType.HardcodedChinese);
        public int UntranslatedEnglishCount => Issues.Count(i => i.IssueType == I18nIssueType.UntranslatedEnglish);
        public int MissingKeyCount => Issues.Count(i => i.IssueType == I18nIssueType.MissingDictionaryKey);

        /// <summary>"必须修"的严重遗漏（中文硬编码 / 缺失键名）；可汉化英文按棘轮预算单独判定</summary>
        public int TotalErrors => HardcodedChineseCount + MissingKeyCount;
    }

    /// <summary>
    /// I18n 判定词库（唯一数据源：源码审计与词典审计共用同一份口径）
    ///
    /// 1. AuthoritativeAbbreviations —— 权威缩写白名单：只收"航电界通用原文"的系统代号 / 飞行参数 / 单位，
    ///    例如 SAS / RCS / VSI / SPD / ALT / HDG / THR / EC / NORM / CAUT。
    ///    任何整词英文与短语（SEARCHING / STANDBY / POINTING MODE / NO TELEMETRY LINK / STAGE CONTROL …）
    ///    都不属于权威缩写，必须进入"可汉化"审计范围。
    ///    确需保留原文时应当登记棘轮基线（可审计的存量），而不是往本表里塞词 —— 塞词会让规则永久失明。
    ///
    /// 2. EngineStyleTokens —— Unity 引擎内建 GUIStyle / 控件名（GUILayout.BeginHorizontal("box")），
    ///    属于引擎内部标识而非界面文案。
    /// </summary>
    internal static class I18nLexicon
    {
        private static readonly HashSet<string> AuthoritativeAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 飞行控制与航电系统代号
            "SAS", "RCS", "HUD", "AP", "PREC", "DCK", "DOCK", "TGT", "ECAM", "EICAS", "ND",
            // 飞行参数与动力装置读数
            "SPD", "ALT", "VSI", "VERTSPD", "HDG", "THR", "TWR", "MACH", "G", "Q", "PE", "EC", "COMM", "STG",
            "N1", "N2", "EPR", "EGT", "FF", "RPM", "ENG", "VIB", "REV", "MECO", "TO", "GA", "JETT", "ACC",
            "TAS", "IAS", "GS", "RA", "WT", "QTY", "PRESS", "TEMP", "SAT", "TAT", "CAB", "LDG", "RDR", "MON", "CH",
            // 时间 / 机构 / 项目代号
            "UT", "MET", "MFP", "KSP", "SPX", "TDRS", "ISS", "DSN", "AFT", "FWD", "LO", "HI", "T",
            // 标准告警与状态代号
            "NORM", "CAUT", "WARN", "OK", "ERR", "ON", "OFF",
            // 单位与量纲符号
            "M", "KM", "S", "MIN", "H", "D", "Y", "KN", "KPA", "ATM", "MS", "HZ", "FPS", "PX",
            "KB", "MB", "GB", "V", "A", "W", "k", "KG", "KGS", "C", "F", "PSI", "BPS", "KBPS", "MBPS", "DV",
            // 坐标 / 罗盘 / 通道 / 界面缩写 / 航电通用缩写 / 项目标识
            "X", "Y", "Z", "R", "B", "N", "E", "UI", "GUI", "ID", "OBT", "LAG", "ORBIT", "LOG", "PARTS", "LAYOUT", "CREW", "MODULAR", "FLIGHT", "PANEL"
        };

        /// <summary>数字+短单位后缀的读数记号（"0G" / "8K" / "00x" / "3D" / "+15c"），不是可汉化文案</summary>
        private static readonly Regex NumericWithUnitRegex = new Regex(@"^\d+[A-Za-z]{1,2}$", RegexOptions.Compiled);

        private static readonly HashSet<string> EngineStyleTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "label", "Button", "box", "textfield", "window", "button", "toggle"
        };

        private static readonly Regex TokenHoleRegex = new Regex(@"\{[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex RichTextTagRegex = new Regex(@"</?[A-Za-z0-9]+(?:=[^>]*?)?>", RegexOptions.Compiled);
        private static readonly Regex FileNameRegex = new Regex(@"\b[A-Za-z0-9_\-]+\.(?:json|cfg|png|dds|csv)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TokenSplitRegex = new Regex(@"[^A-Za-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex NumericOnlyRegex = new Regex(@"^[\d\.\,\+\-\%\s\:\/\#\<\>\=]+(px|%|ms|hz|fps|x)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LeadingDecorationRegex = new Regex(
            @"^[\s\u25A0-\u25FF\u2B00-\u2BFF\u2190-\u21FF\u00AB\u00BB\u2022\u25CF\u25CB\u25B6\u25C0\u23F8\u23F5\.\,\-\+\#\:\/\[\]\(\)\|]+", RegexOptions.Compiled);
        private static readonly Regex TrailingDecorationRegex = new Regex(
            @"[\s\u25A0-\u25FF\u2B00-\u2BFF\u2190-\u21FF\u00AB\u00BB\u2022\u25CF\u25CB\u25B6\u25C0\u23F8\u23F5\.\,\-\+\#\:\/\[\]\(\)\|]+$", RegexOptions.Compiled);

        private static List<string> Tokenize(string text) =>
            TokenSplitRegex.Split(text).Where(t => t.Length > 0).ToList();

        /// <summary>
        /// 整串拆词后每个词都是"读数记号"：权威缩写（"SAS: OFF" / "RCS/SAS"）、
        /// 纯数字（"0" / "100"）或数字+短单位（"0G" / "8K" / "00x"）。
        /// 这样的串没有可翻译的英文词汇，属于读数而非文案。
        /// </summary>
        public static bool IsAllAbbreviated(string text)
        {
            // 词数上限只用于挡住"整句由缩写拼装"的畸形串；多行读数（如 X 0.0m / Y 0.0m / Z 0.0m）
            // 本身没有可翻译词汇，不该因为换行拆词多而被误判，故上限放宽到 12。
            var tokens = Tokenize(text);
            if (tokens.Count == 0 || tokens.Count > 12) return false;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                bool isNumeric = token.All(char.IsDigit);
                if (isNumeric || NumericWithUnitRegex.IsMatch(token)) continue;
                if (!AuthoritativeAbbreviations.Contains(token)) return false;
            }
            return true;
        }

        private static bool IsEngineStyleName(string text)
        {
            var tokens = Tokenize(text);
            if (tokens.Count == 0 || tokens.Count > 2) return false;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!EngineStyleTokens.Contains(tokens[i])) return false;
            }
            return true;
        }

        /// <summary>剥掉首尾装饰符号（▲ ● ○ ▶ « » · 等排版修饰）</summary>
        public static string TrimDecorations(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string trimmed = LeadingDecorationRegex.Replace(text.Trim(), string.Empty).Trim();
            return TrailingDecorationRegex.Replace(trimmed, string.Empty).Trim();
        }

        /// <summary>
        /// 文案豁免判定（源码字面量与词典词条共用同一口径）：
        /// 纯通配符模板（"{SPD}" / "Σ {DV:TOTALTIME}"）/ 无字母 / 纯数字单位 /
        /// 权威缩写（"SAS" / "RCS/SAS"）/ 引擎内建样式名（"box"）→ 无汉化必要，放行。
        /// </summary>
        public static bool IsExemptDisplayText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            // 剔除 Unity 富文本标签 (<b>, </b>, <color=...>, </color>, <size=...>, </size> 等)
            string withoutTags = RichTextTagRegex.Replace(text, " ");

            // 剔除嵌入的配置文件名与数据资源标识符
            string withoutFiles = FileNameRegex.Replace(withoutTags, " ");

            // 通配符模板洞先剔除：剩下若只是分隔符，说明该串是模板而非文案
            string core = TokenHoleRegex.Replace(withoutFiles, " ");
            core = TrimDecorations(core);
            if (core.Length == 0) return true;

            // 配置文件扩展名与数据标识符豁免
            if (core.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                core.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                core.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool hasLetter = false;
            for (int i = 0; i < core.Length; i++)
            {
                if (char.IsLetter(core[i])) { hasLetter = true; break; }
            }
            if (!hasLetter) return true;

            // 拆不出任何 ASCII 字母数字词（只剩 Σ ▲ ⟲ 等非 ASCII 符号）→ 没有英文可汉化
            if (Tokenize(core).Count == 0) return true;

            if (NumericOnlyRegex.IsMatch(core)) return true;
            if (IsAllAbbreviated(core)) return true;
            if (IsEngineStyleName(core)) return true;

            return false;
        }
    }

    /// <summary>
    /// 基于 Roslyn 抽象语法树 (AST) 的全局国际化与文本硬编码审计套件
    /// </summary>
    public static class I18nSyntaxAuditor
    {
        private static readonly Regex ChineseRegex = new Regex(@"[\u4e00-\u9fa5]", RegexOptions.Compiled);

        /// <summary>
        /// 可汉化英文的棘轮基线：文件名 -> 允许的"未汉化英文文案"处数上限。
        /// 中文主语言下这些文案应当汉化；存量欠账按文件冻结，只降不升，新增即门禁失败。
        /// 数值由 --i18n-baseline-dump 导出后登记，禁止手工估算。
        /// </summary>
        private static readonly Dictionary<string, int> EnglishBaselineTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // ── 205 处存量已于本轮全部汉化（接入 I18n.Tr 词典），此表保持为空 = 零容忍 ──
        };

        /// <summary>
        /// 可汉化英文的棘轮上限（冻结值）：基线永远不得高于此表。此表为空 = 任何文件都不允许登记基线。
        /// </summary>
        private static readonly Dictionary<string, int> EnglishRatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 结构保留、内容为空：登记新欠账时必须同时写入本表，并由 ValidateEnglishRatchet() 校验。
        };

        public static int TotalRegisteredEnglishDebt
        {
            get
            {
                int total = 0;
                foreach (var kv in EnglishBaselineTable) total += kv.Value;
                return total;
            }
        }

        /// <summary>该文件允许的可汉化英文处数上限</summary>
        public static int GetAllowedEnglishOccurrences(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            return EnglishBaselineTable.TryGetValue(fileName, out int allowed) ? allowed : 0;
        }

        public static bool IsEnglishBaselineRegistered(string fileName) =>
            !string.IsNullOrEmpty(fileName) && EnglishBaselineTable.ContainsKey(fileName);

        /// <summary>棘轮完整性校验：基线必须登记过冻结上限，且只允许下调</summary>
        public static List<string> ValidateEnglishRatchet()
        {
            var failures = new List<string>();

            foreach (var kv in EnglishBaselineTable)
            {
                if (!EnglishRatchetCeilingTable.TryGetValue(kv.Key, out int ceiling))
                {
                    failures.Add("英文基线未登记棘轮上限（禁止新增基线文件）: " + kv.Key + " = " + kv.Value
                               + "；确需登记时必须同时写入 EnglishRatchetCeilingTable 与 EnglishBaselineTable");
                    continue;
                }
                if (kv.Value > ceiling)
                {
                    failures.Add("英文基线被上调（棘轮只允许下降）: " + kv.Key + " = " + kv.Value + " 高于冻结上限 " + ceiling);
                }
            }

            foreach (var kv in EnglishRatchetCeilingTable)
            {
                if (!EnglishBaselineTable.ContainsKey(kv.Key) && kv.Value != 0)
                {
                    failures.Add("英文棘轮上限登记了非零值但基线表里没有对应条目: " + kv.Key + " = " + kv.Value);
                }
            }

            return failures;
        }

        /// <summary>
        /// 棘轮预算判定：逐文件比对"实测未汉化英文处数 vs 基线"。超出即新增欠账 → 门禁失败。
        /// </summary>
        public static List<string> ValidateEnglishBudget(I18nAuditReport report)
        {
            var failures = new List<string>();
            if (report == null) return failures;

            var byFile = report.Issues
                .Where(i => i.IssueType == I18nIssueType.UntranslatedEnglish)
                .GroupBy(i => Path.GetFileName(i.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (var group in byFile)
            {
                int allowed = GetAllowedEnglishOccurrences(group.Key);
                int actual = group.Count();
                if (actual <= allowed) continue;

                var samples = string.Join(" | ", group.Take(3).Select(i => $"L{i.Line} \"{i.OffendingText}\""));
                failures.Add($"[{group.Key}] 新增未汉化英文文案 {actual - allowed} 处 (实测 {actual} / 基线 {allowed})；"
                           + "中文主语言下应接入 I18n.Tr 词典，权威缩写除外。样本: " + samples);
            }

            return failures;
        }

        /// <summary>
        /// 扫描指定目录及其子目录下的所有 C# 源码文件
        /// </summary>
        public static I18nAuditReport AuditDirectory(string sourceDir, HashSet<string> validKeysInDictionary)
        {
            var report = new I18nAuditReport();
            if (!Directory.Exists(sourceDir)) return report;

            var files = Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExemptFile(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                AuditFile(file, validKeysInDictionary, report);
            }

            return report;
        }

        /// <summary>
        /// 扫描单个 C# 文件的语法树
        /// </summary>
        public static void AuditFile(string filePath, HashSet<string> validKeysInDictionary, I18nAuditReport report)
        {
            string code;
            try
            {
                code = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                report.Issues.Add(new I18nIssue
                {
                    FilePath = filePath,
                    IssueType = I18nIssueType.HardcodedChinese,
                    Description = $"无法读取文件: {ex.Message}"
                });
                return;
            }

            report.ScannedFilesCount++;
            var tree = CSharpSyntaxTree.ParseText(code, path: filePath);
            var root = tree.GetRoot();

            var walker = new I18nAstWalker(filePath, validKeysInDictionary, report);
            walker.Visit(root);
        }

        private static bool IsExemptFile(string filePath)
        {
            string norm = filePath.Replace('\\', '/');

            // 跳过 obj / bin
            if (norm.Contains("/obj/") || norm.Contains("/bin/")) return true;

            // 跳过审计工具链自身
            if (norm.Contains("/Auditing/") || norm.Contains("HeadlessValidator") || norm.Contains("WidgetColorLiteralAudit")) return true;

            // 跳过内置兜底字典本身与底层零依赖解析器本身
            if (norm.EndsWith("I18nManager.cs", StringComparison.OrdinalIgnoreCase) ||
                norm.EndsWith("I18nJsonParser.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 跳过 15 大外部模组探针与遥测静态参数字典 (736+ 参数已在 PROBE_TELEMETRY_API_CATALOG 统管) 以及底层程序化纹理烘焙器
            if (norm.Contains("/Core/Probes/") || norm.EndsWith("TelemetryCatalog.cs", StringComparison.OrdinalIgnoreCase) || norm.Contains("/Core/Rendering/"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 语法树遍历访问器
        /// </summary>
        /// <summary>
        /// I18n 查表调用名集合 —— 本文件内的唯一声明点。
        /// 源码里出现这些调用时，其"键名 / 兜底文案"实参属于合法的本地化输入；
        /// 规则体不再重复写这些字符串，避免调用名变更时两处漂移。
        /// </summary>
        private static class I18nCalls
        {
            public const string Tr = "I18n.Tr";
            public const string TrFormat = "I18n.TrFormat";
            public const string ManagerTr = "I18nManager.Tr";
            public const string ManagerTrFormat = "I18nManager.TrFormat";
            public const string GetWidgetName = "I18n.GetWidgetName";
            public const string ManagerGetWidgetName = "I18nManager.GetWidgetName";

            /// <summary>Tr 家族：Tr(key, fallback)，前两个槽位都是查表输入</summary>
            public static bool IsKeyLookup(string expr)
            {
                return expr == Tr || expr == ManagerTr;
            }

            /// <summary>GetWidgetName 家族：GetWidgetName(widgetId, defaultDisplayName)</summary>
            public static bool IsWidgetNameLookup(string expr)
            {
                return expr == GetWidgetName || expr == ManagerGetWidgetName;
            }

            /// <summary>TrFormat 家族：TrFormat(key, params args)，仅第 0 槽是键名</summary>
            public static bool IsKeyLookupFormat(string expr)
            {
                return expr == TrFormat || expr == ManagerTrFormat;
            }

            /// <summary>实参槽位豁免判定宿主：任一 I18n 查表调用</summary>
            public static bool IsSlotHost(string expr)
            {
                return IsKeyLookup(expr) || IsWidgetNameLookup(expr) || IsKeyLookupFormat(expr);
            }

            /// <summary>
            /// 需要校验"键名存在于词典"的调用。
            /// GetWidgetName 家族不在此列 —— 它的首参是组件注册 id，不是词典键。
            /// </summary>
            public static bool IsDictionaryKeyLookup(string expr)
            {
                return IsKeyLookup(expr) || IsKeyLookupFormat(expr);
            }
        }

        private class I18nAstWalker : CSharpSyntaxWalker
        {
            private readonly string _filePath;
            private readonly HashSet<string> _validKeys;
            private readonly I18nAuditReport _report;

            public I18nAstWalker(string filePath, HashSet<string> validKeys, I18nAuditReport report)
            {
                _filePath = filePath;
                _validKeys = validKeys;
                _report = report;
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                base.VisitLiteralExpression(node);
                _report.ScannedAstNodesCount++;

                if (!node.IsKind(SyntaxKind.StringLiteralExpression)) return;

                string text = node.Token.ValueText;
                if (string.IsNullOrEmpty(text)) return;

                // 1. 仅当字符串落在 I18n 查表调用的"键名 / 兜底文案"槽位时合法放行
                //    （TrFormat 的格式化实参不属于查表输入，出现中文同样必须本地化）
                if (IsExemptI18nSlot(node))
                {
                    return;
                }

                // 2. 若处于日志、诊断、调试或异常构造函数中，合法放行
                if (IsInsideLogOrDiagnostic(node)) return;

                // 3. 若处于属性元数据 (Attribute) 中，如 [JsonPropertyName("...")], [KSPAddon(...)]，合法放行
                if (node.Ancestors().OfType<AttributeSyntax>().Any()) return;

                // 4. 若处于 Shader.Find / AssetBundle / Resource / IO Path 等引擎底层路径调用中，合法放行
                if (IsInsideEngineResourceOrPathCall(node)) return;

                // 5. 若处于字符串比对匹配 (Contains / Equals / IndexOf / StartsWith / EndsWith)，合法放行
                if (IsInsideStringComparison(node)) return;

                // 6. 若处于微控件/内部组件注册描述符中 (Controls.Register, WrapElement, new Widget*Control)，合法放行
                if (IsInsideMicroControlRegistration(node)) return;

                var lineSpan = node.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                int col = lineSpan.StartLinePosition.Character + 1;

                // 【严重规则 A】非 I18n 上下文中的硬编码中文字符串
                if (ChineseRegex.IsMatch(text))
                {
                    _report.Issues.Add(new I18nIssue
                    {
                        FilePath = _filePath,
                        Line = line,
                        Column = col,
                        IssueType = I18nIssueType.HardcodedChinese,
                        OffendingText = text,
                        CodeSnippet = node.ToString(),
                        Description = $"发现硬编码中文文本: \"{text}\"，必须使用 I18n.Tr(\"KEY\", \"{text}\") 包装！"
                    });
                    return;
                }

                // 【警告规则 B】UI 文案槽位中直接传入的可汉化英文（权威缩写 / 通配符模板 / 纯符号除外）
                if (IsInsideDisplayTextSlot(node, out string channelName))
                {
                    if (!I18nLexicon.IsExemptDisplayText(text))
                    {
                        _report.Issues.Add(new I18nIssue
                        {
                            FilePath = _filePath,
                            Line = line,
                            Column = col,
                            IssueType = I18nIssueType.UntranslatedEnglish,
                            OffendingText = text,
                            CodeSnippet = node.ToString(),
                            Description = $"UI 文案槽位 ({channelName}) 直接传入未汉化英文: \"{text}\"，"
                                        + "中文主语言下应使用 I18n.Tr(\"KEY\", \"{text}\") 接入词典。"
                        });
                    }
                }
            }

            public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            {
                base.VisitInterpolatedStringExpression(node);
                _report.ScannedAstNodesCount++;

                if (IsInsideLogOrDiagnostic(node)) return;
                if (IsExemptI18nSlot(node)) return;
                if (IsInsideMicroControlRegistration(node)) return;
                if (node.Ancestors().OfType<AttributeSyntax>().Any()) return;

                // 检查插值字符串中除 {...} 表达式之外的纯文本部分 (InterpolatedStringTextSyntax)
                foreach (var content in node.Contents.OfType<InterpolatedStringTextSyntax>())
                {
                    string text = content.TextToken.ValueText;
                    if (ChineseRegex.IsMatch(text))
                    {
                        var lineSpan = content.GetLocation().GetLineSpan();
                        int line = lineSpan.StartLinePosition.Line + 1;
                        int col = lineSpan.StartLinePosition.Character + 1;

                        _report.Issues.Add(new I18nIssue
                        {
                            FilePath = _filePath,
                            Line = line,
                            Column = col,
                            IssueType = I18nIssueType.HardcodedChinese,
                            OffendingText = text,
                            CodeSnippet = node.ToString(),
                            Description = $"插值字符串包含硬编码中文文本 \"{text}\"，应使用 I18n.TrFormat(\"KEY\", ...) 包装！"
                        });
                        break;
                    }
                }

                if (IsInsideDisplayTextSlot(node, out string channelName))
                {
                    var sb = new StringBuilder();
                    int holeIndex = 0;
                    foreach (var content in node.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax textSyntax)
                        {
                            sb.Append(textSyntax.TextToken.ValueText);
                        }
                        else
                        {
                            sb.Append("{" + (holeIndex++) + "}");
                        }
                    }
                    string templateText = sb.ToString();

                    if (!I18nLexicon.IsExemptDisplayText(templateText))
                    {
                        var lineSpan = node.GetLocation().GetLineSpan();
                        int line = lineSpan.StartLinePosition.Line + 1;
                        int col = lineSpan.StartLinePosition.Character + 1;

                        _report.Issues.Add(new I18nIssue
                        {
                            FilePath = _filePath,
                            Line = line,
                            Column = col,
                            IssueType = I18nIssueType.UntranslatedEnglish,
                            OffendingText = templateText.Trim(),
                            CodeSnippet = node.ToString(),
                            Description = $"UI 文案槽位 ({channelName}) 插值字符串直接传入未汉化英文: \"{templateText.Trim()}\"，"
                                        + "中文主语言下应使用 I18n.TrFormat(\"KEY\", ...) 接入词典。"
                        });
                    }
                }
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                base.VisitInvocationExpression(node);
                _report.ScannedAstNodesCount++;

                string expr = node.Expression.ToString();
                if (I18nCalls.IsDictionaryKeyLookup(expr))
                {
                    if (node.ArgumentList.Arguments.Count > 0)
                    {
                        var firstArg = node.ArgumentList.Arguments[0].Expression;
                        if (firstArg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            string key = lit.Token.ValueText;

                            if (_validKeys != null && !_validKeys.Contains(key))
                            {
                                var lineSpan = lit.GetLocation().GetLineSpan();
                                int line = lineSpan.StartLinePosition.Line + 1;
                                int col = lineSpan.StartLinePosition.Character + 1;

                                string fallback = string.Empty;
                                if (node.ArgumentList.Arguments.Count > 1)
                                {
                                    var secondArg = node.ArgumentList.Arguments[1].Expression;
                                    if (secondArg is LiteralExpressionSyntax lit2 && lit2.IsKind(SyntaxKind.StringLiteralExpression))
                                    {
                                        fallback = lit2.Token.ValueText;
                                    }
                                }

                                _report.Issues.Add(new I18nIssue
                                {
                                    FilePath = _filePath,
                                    Line = line,
                                    Column = col,
                                    IssueType = I18nIssueType.MissingDictionaryKey,
                                    OffendingText = key,
                                    FallbackText = fallback,
                                    CodeSnippet = node.ToString(),
                                    Description = $"代码中调用的 I18n 键名 '{key}' 未在本地化字典中登记！"
                                });
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// 判定字符串是否落在 I18n 查表调用的豁免槽位（键名 / 兜底文案）。
            /// 旧实现只看"祖先里有没有 I18n 调用"，于是 I18n.TrFormat("K", 1, "中文参数") 的格式化实参也被放过；
            /// 现在按实参槽位精确判定：TrFormat 只豁免第 0 槽（键名）。
            /// </summary>
            private static bool IsExemptI18nSlot(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is ArgumentSyntax arg)) continue;
                    if (!(arg.Parent is ArgumentListSyntax argList)) continue;
                    if (!(argList.Parent is InvocationExpressionSyntax inv)) continue;

                    string expr = inv.Expression.ToString();
                    int index = argList.Arguments.IndexOf(arg);

                    if (I18nCalls.IsKeyLookup(expr) || I18nCalls.IsWidgetNameLookup(expr))
                    {
                        // Tr(key, fallback) / GetWidgetName(widgetId, defaultDisplayName)：前两个槽位都是查表输入
                        return index <= 1;
                    }
                    if (I18nCalls.IsKeyLookupFormat(expr))
                    {
                        // TrFormat(key, params args)：仅键名槽位豁免，格式化实参必须本地化
                        return index == 0;
                    }

                    // 最近的外层实参不属于 I18n 查表调用 → 不在豁免之列
                    return false;
                }
                return false;
            }

            /// <summary>
            /// 字符串组装包装（视为透明，继续向外层寻找真正的宿主调用）。
            /// </summary>
            private static readonly HashSet<string> StringAssemblyPassThroughCalls = new HashSet<string>(StringComparer.Ordinal)
            {
                "string.Format", "string.Concat", "string.Join",
                "String.Format", "String.Concat", "String.Join",
                "System.String.Format", "System.String.Concat"
            };

            /// <summary>
            /// 日志 / 诊断 / 异常文案判定。
            /// 判定口径是"最近的外层调用"：旧实现只要祖先链上出现过日志调用就整段豁免，
            /// 于是 MFPLogger.Info(Other("中文")) 这类嵌套调用里的未本地化中文被静默放过。
            /// </summary>
            private static bool IsInsideLogOrDiagnostic(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        string typeName = oce.Type.ToString();
                        if (typeName.EndsWith("Exception", StringComparison.Ordinal)) return true;
                    }

                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    string expr = inv.Expression.ToString();
                    if (IsLogOrDiagnosticCall(expr)) return true;
                    if (StringAssemblyPassThroughCalls.Contains(expr)) continue;

                    // 最近的外层调用不是日志/诊断且不是字符串组装包装 → 不豁免
                    return false;
                }
                return false;
            }

            /// <summary>日志 / 诊断调用前缀与裸调用名（唯一声明点）</summary>
            private static readonly string[] LogCallPrefixes =
            {
                "MFPLogger.", "Debug.", "Console.", "KSPLog."
            };

            private const string BarePrintCall = "print";

            private static bool IsLogOrDiagnosticCall(string expr)
            {
                for (int i = 0; i < LogCallPrefixes.Length; i++)
                {
                    if (expr.StartsWith(LogCallPrefixes[i], StringComparison.Ordinal)) return true;
                }
                return expr == BarePrintCall;
            }

            /// <summary>
            /// 引擎资源 / 路径类调用判定。
            /// 判定口径同样是"最近的外层调用"：旧实现遍历整条祖先链，只要链上任何一层出现过
            /// Path./File./Regex. 就整段豁免 —— 于是 Path.Combine(dir, Helper("中文")) 里
            /// 嵌套调用中的中文被静默放过。现在只有"字面量直接挂在被豁免调用的实参位置"才放行。
            /// </summary>
            private static bool IsInsideEngineResourceOrPathCall(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    if (IsEngineResourceOrPathInvocation(inv)) return true;
                    string expr = inv.Expression.ToString();
                    if (StringAssemblyPassThroughCalls.Contains(expr)) continue;

                    // 最近的外层调用既不是路径/资源类调用，也不是纯字符串组装 → 不外扩
                    return false;
                }
                return false;
            }

            private static bool IsEngineResourceOrPathInvocation(InvocationExpressionSyntax inv)
            {
                if (inv == null) return false;
                var receiver = RoslynAstHelper.GetInvocationReceiver(inv);
                string receiverName = receiver != null ? RoslynAstHelper.GetRightmostIdentifier(receiver) : string.Empty;
                string methodName = RoslynAstHelper.GetInvokedMethodName(inv);

                if (receiverName == "Path" || receiverName == "Directory" || receiverName == "File" ||
                    receiverName == "AssetBundle" || receiverName == "GameDatabase" || receiverName == "Regex")
                {
                    return true;
                }
                if (receiverName == "Shader" && methodName == "Find") return true;
                if (receiverName == "Resources" && methodName == "Load") return true;

                return IsEngineResourceOrPathCall(inv.Expression.ToString());
            }

            private static bool IsEngineResourceOrPathCall(string expr)
            {
                return expr.StartsWith("Path.", StringComparison.Ordinal)
                    || expr.StartsWith("Directory.", StringComparison.Ordinal)
                    || expr.StartsWith("File.", StringComparison.Ordinal)
                    || expr.StartsWith("Shader.Find", StringComparison.Ordinal)
                    || expr.StartsWith("AssetBundle.", StringComparison.Ordinal)
                    || expr.StartsWith("Resources.Load", StringComparison.Ordinal)
                    || expr.StartsWith("GameDatabase.", StringComparison.Ordinal)
                    || expr.StartsWith("Regex.", StringComparison.Ordinal);
            }

            /// <summary>
            /// 字符串比对 / 匹配调用判定。
            /// 判定口径同样是"最近的外层调用"：旧实现遍历整条祖先链，任何一层出现
            /// .Contains/.Equals/... 就整段豁免 —— 于是 Helper("中文").Contains(x) 与
            /// Foo(a.Contains(b) ? "中文" : "x") 这类写法会把嵌套的真实文案一并放过。
            /// </summary>
            private static bool IsInsideStringComparison(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    string expr = inv.Expression.ToString();
                    if (IsStringComparisonCall(expr)) return true;
                    if (StringAssemblyPassThroughCalls.Contains(expr)) continue;

                    // 最近的外层调用不是字符串比对 → 不外扩
                    return false;
                }
                return false;
            }

            private static bool IsStringComparisonCall(string expr)
            {
                return expr.EndsWith(".Contains", StringComparison.Ordinal)
                    || expr.EndsWith(".Equals", StringComparison.Ordinal)
                    || expr.EndsWith(".IndexOf", StringComparison.Ordinal)
                    || expr.EndsWith(".StartsWith", StringComparison.Ordinal)
                    || expr.EndsWith(".EndsWith", StringComparison.Ordinal);
            }

            /// <summary>
            /// 微控件 / 内部组件注册描述符判定。
            /// 判定口径同样是"最近的外层调用"：旧实现遍历整条祖先链，只要外面套着任意一层
            /// new *Widget*Control(...) 或 Controls.Register(...) 就整段豁免 ——
            /// 于是 new WidgetReadoutControl(null, null, r, "N", Helper("中文")) 里
            /// 嵌套调用中的中文被静默放过（与用例 8 修掉的是同一类洞）。
            /// 现在只豁免"字面量直接作为注册描述符的实参"这一层。
            /// </summary>
            private static bool IsInsideMicroControlRegistration(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        string type = oce.Type.ToString();
                        if (IsMicroControlType(type)) return true;

                        // 其它对象构造不是注册描述符 → 不外扩
                        return false;
                    }

                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        string expr = inv.Expression.ToString();
                        if (IsMicroControlRegistrationCall(expr)) return true;
                        if (StringAssemblyPassThroughCalls.Contains(expr)) continue;

                        // 最近的外层调用不是注册入口 → 不外扩
                        return false;
                    }
                }
                return false;
            }

            private static bool IsMicroControlType(string typeName)
            {
                if (string.IsNullOrEmpty(typeName)) return false;
                return typeName.EndsWith("Control", StringComparison.Ordinal)
                    && typeName.Contains("Widget");
            }

            private static bool IsMicroControlRegistrationCall(string expr)
            {
                if (string.IsNullOrEmpty(expr)) return false;
                return expr.Contains("WrapElement")
                    || expr.Contains("Controls.Wrap")
                    || expr.Contains("Controls.Register");
            }

            /// <summary>
            /// UI 显示文案槽位表（唯一数据源）：调用表达式 → 承载"界面文案"的实参下标。
            /// 这些形参最终会进入 UGUI Text/IMGUI 控件，属于玩家可见文本；同调用里的其它实参
            /// （对象名 / 键名 / 样式名）不在此列，因此不会误伤 CreateText 的第 1 参对象名。
            /// </summary>
            private static readonly Dictionary<string, int[]> DisplayTextSlots = new Dictionary<string, int[]>(StringComparer.Ordinal)
            {
                // 现代微控件 DSL
                { "TextWidget.Title", new[] { 0 } },
                { "TextWidget.Badge", new[] { 0 } },
                { "TextWidget.Value", new[] { 0 } },
                { "TextWidget.Unit", new[] { 0 } },
                { "new TextWidget", new[] { 0 } },
                // UGUI 工厂
                { "UIFactory.CreateText", new[] { 2 } },
                { "UIFactory.CreateCockpitButton", new[] { 2 } },
                { "UIFactory.CreateValueBox", new[] { 4, 5, 6 } },
                { "UIFactory.CreateAnnunciator", new[] { 4 } },
                { "UIFactory.CreateSegmentedControl", new[] { 4 } },
                // 结构化通道兜底文案 与 文本写入助手
                { "GetTemplateChannel", new[] { 1 } },
                { "SetTextIfChanged", new[] { 1 } },
            };

            /// <summary>
            /// 判定字符串是否落在 UI 显示文案槽位（含 .text/.tooltip 赋值与 GUIContent）。
            /// 判定口径是"最近的外层调用"：内层若是未知调用（数据加工）则不外扩，
            /// 避免把 DTO / 键名 / 路径当成文案；string.Format 等纯组装包装视为透明继续外扩。
            /// </summary>
            private static bool IsInsideDisplayTextSlot(SyntaxNode node, out string channelName)
            {
                channelName = string.Empty;

                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax argList)
                    {
                        int index = argList.Arguments.IndexOf(arg);

                        if (argList.Parent is InvocationExpressionSyntax inv)
                        {
                            string expr = inv.Expression.ToString();

                            if (DisplayTextSlots.TryGetValue(expr, out int[] slots))
                            {
                                if (Array.IndexOf(slots, index) >= 0)
                                {
                                    channelName = expr + " 第" + index + "参";
                                    return true;
                                }
                                return false;   // 落在同一调用的非文案槽位（如对象名 / 键名）
                            }

                            if (expr.StartsWith("GUILayout.", StringComparison.Ordinal) || expr.StartsWith("GUI.", StringComparison.Ordinal))
                            {
                                channelName = expr;
                                return true;    // IMGUI 调用内的字面量按旧口径整体视为文案
                            }

                            if (StringAssemblyPassThroughCalls.Contains(expr)) continue;

                            return false;       // 最近的外层调用是未知数据加工 → 不外扩
                        }

                        if (argList.Parent is ObjectCreationExpressionSyntax oce)
                        {
                            string typeName = oce.Type.ToString();
                            if (typeName == "GUIContent") { channelName = "new GUIContent"; return true; }
                            if (DisplayTextSlots.TryGetValue("new " + typeName, out int[] ctorSlots))
                            {
                                if (Array.IndexOf(ctorSlots, index) >= 0) { channelName = "new " + typeName + " 第" + index + "参"; return true; }
                                return false;
                            }
                        }
                        continue;
                    }

                    if (ancestor is ObjectCreationExpressionSyntax guiContent && guiContent.Type.ToString() == "GUIContent")
                    {
                        channelName = "new GUIContent";
                        return true;
                    }

                    if (ancestor is AssignmentExpressionSyntax assign)
                    {
                        string left = assign.Left.ToString();
                        if (left.EndsWith(".text", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".tooltip", StringComparison.OrdinalIgnoreCase))
                        {
                            channelName = left;
                            return true;
                        }
                        continue;
                    }
                }

                return false;
            }
        }

        /// <summary>最近一次 SelfTest 实际执行的用例数（供门禁打印真实条数，避免写死数字漂移）</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// 审计内核自检测试用例 (Self-Test Suite)
        /// 保证规则在边界条件下的命中与放过行为完全符合预期。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;
            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TEST_KEY_HELLO", "TEST_KEY_BTN" };

            // 用例 1: 必须检出硬编码中文
            cases++;
            string case1 = "class C { void M() { var x = \"这是未翻译中文\"; } }";
            var rep1 = AuditSnippet(case1, validKeys);
            if (rep1.HardcodedChineseCount != 1)
            {
                failures.Add($"[用例 1 失败] 期望检出 1 处硬编码中文，实际检出 {rep1.HardcodedChineseCount}");
            }

            // 用例 2: I18n.Tr 规范包装必须放过
            cases++;
            string case2 = "class C { void M() { var x = I18n.Tr(\"TEST_KEY_HELLO\", \"这是兜底中文\"); } }";
            var rep2 = AuditSnippet(case2, validKeys);
            if (rep2.Issues.Count != 0)
            {
                failures.Add($"[用例 2 失败] I18n.Tr 应当合法放过，实际报告了 {rep2.Issues.Count} 处违规: {rep2.Issues[0]}");
            }

            // 用例 3: 日志方法中的中文必须放过
            cases++;
            string case3 = "class C { void M() { MFPLogger.Warn(MFPLogger.CatUI, \"这是日志文本\"); Debug.Log(\"调试信息\"); } }";
            var rep3 = AuditSnippet(case3, validKeys);
            if (rep3.HardcodedChineseCount != 0)
            {
                failures.Add($"[用例 3 失败] 日志调用中的中文应当被排除，实际检出 {rep3.HardcodedChineseCount}");
            }

            // 用例 4: 调用不存在的字典 Key 必须报错
            cases++;
            string case4 = "class C { void M() { var x = I18n.Tr(\"UNKNOWN_MISSING_KEY\", \"兜底\"); } }";
            var rep4 = AuditSnippet(case4, validKeys);
            if (rep4.MissingKeyCount != 1)
            {
                failures.Add($"[用例 4 失败] 期望检出 1 处未定义字典 Key，实际检出 {rep4.MissingKeyCount}");
            }

            // 用例 5: UI 调用中的排版纯符号与权威缩写必须放过
            cases++;
            string case5 = "class C { void M() { GUILayout.Label(\" - \"); GUILayout.Label(\" | \"); GUILayout.Button(\"X\"); GUILayout.Label(\"SAS\"); GUILayout.Label(\"RCS/SAS\"); GUILayout.BeginHorizontal(\"box\"); } }";
            var rep5 = AuditSnippet(case5, validKeys);
            if (rep5.Issues.Count != 0)
            {
                failures.Add($"[用例 5 失败] UI 排版符号 / 权威缩写 / 引擎样式名应当放过，实际报告了: {rep5.Issues[0]}");
            }

            // 用例 6: 插值字符串里的硬编码中文同样必须检出（旧实现只看 InterpolatedStringText，容易漏）
            cases++;
            string case6 = "class C { void M() { var x = $\"未翻译{1}中文\"; } }";
            var rep6 = AuditSnippet(case6, validKeys);
            if (rep6.HardcodedChineseCount != 1)
            {
                failures.Add($"[用例 6 失败] 插值字符串中的硬编码中文应检出 1 处，实际 {rep6.HardcodedChineseCount}");
            }

            // 用例 7: I18n 查表调用的"非键名槽位"不是豁免区（TrFormat 的格式化实参出现中文必须报）
            cases++;
            string case7 = "class C { void M() { var x = I18n.TrFormat(\"TEST_KEY_HELLO\", 1, \"中文参数\"); } }";
            var rep7 = AuditSnippet(case7, validKeys);
            if (rep7.HardcodedChineseCount != 1)
            {
                failures.Add($"[用例 7 失败] TrFormat 格式化实参中的硬编码中文应检出 1 处，实际 {rep7.HardcodedChineseCount}");
            }

            // 用例 8: 嵌套在日志调用里但宿主调用不是日志 → 不豁免
            cases++;
            string case8 = "class C { void M() { MFPLogger.Info(Other(\"中文\")); } }";
            var rep8 = AuditSnippet(case8, validKeys);
            if (rep8.HardcodedChineseCount != 1)
            {
                failures.Add($"[用例 8 失败] 日志调用内嵌套的其它调用中的中文应检出 1 处，实际 {rep8.HardcodedChineseCount}");
            }

            // 用例 9: 直接作为日志实参的中文仍然豁免（诊断文案不受影响）
            cases++;
            string case9 = "class C { void M() { MFPLogger.Info(\"中文日志\"); Debug.Log(string.Format(\"中文 {0}\", 1)); } }";
            var rep9 = AuditSnippet(case9, validKeys);
            if (rep9.HardcodedChineseCount != 0)
            {
                failures.Add($"[用例 9 失败] 日志直接实参与 string.Format 组装应当豁免，实际 {rep9.HardcodedChineseCount}");
            }

            // 用例 10: UGUI 文案通道的可汉化英文必须检出（旧实现只认 GUI/GUILayout，整类漏检）
            cases++;
            string case10 = "class C { void M() { "
                          + "UIFactory.CreateText(t, \"PointingValue\", \"EARTH POINTING\", 9, TextAnchor.MiddleLeft, c); "
                          + "TextWidget.Title(\"POINTING MODE\"); "
                          + "SetTextIfChanged(_x, \"AWAITING MANEUVER FLIGHT PLAN\"); "
                          + "var y = GetTemplateChannel(\"SUBTITLE\", \"POWER DISTRIBUTION\"); "
                          + "_lbl.text = \"FREE MANUAL\"; } }";
            var rep10 = AuditSnippet(case10, validKeys);
            if (rep10.UntranslatedEnglishCount != 5)
            {
                failures.Add($"[用例 10 失败] 期望检出 5 处可汉化英文，实际检出 {rep10.UntranslatedEnglishCount}");
            }

            // 用例 11: 通道内的权威缩写 / 纯通配符模板 / 对象名参数不得误报
            cases++;
            string case11 = "class C { void M() { "
                          + "UIFactory.CreateText(t, \"POINTING MODE\", I18n.Tr(\"TEST_KEY_HELLO\", \"x\"), 9, TextAnchor.MiddleLeft, c); "
                          + "TextWidget.Value(\"{THROTTLE:PERCENT}\", \"0%\"); "
                          + "var y = GetTemplateChannel(\"TITLE\", \"SPX\"); "
                          + "SetTextIfChanged(_x, \"NORM\"); "
                          + "var z = GetTemplateChannel(\"TPL\", \"Σ {DV:TOTALTIME}\"); } }";
            var rep11 = AuditSnippet(case11, validKeys);
            if (rep11.UntranslatedEnglishCount != 0)
            {
                failures.Add($"[用例 11 失败] 对象名参数 / 通配符模板 / 权威缩写不应误报，实际 {rep11.UntranslatedEnglishCount}: {rep11.Issues.FirstOrDefault()}");
            }

            // 用例 12: 数据加工调用内的英文字面量不算文案（最近调用为未知方法 → 不外扩）
            cases++;
            string case12 = "class C { void M() { string s = Build(\"EARTH POINTING\"); UIFactory.CreateText(t, \"N\", Make(\"FREE MANUAL\"), 9, TextAnchor.MiddleLeft, c); } }";
            var rep12 = AuditSnippet(case12, validKeys);
            if (rep12.UntranslatedEnglishCount != 0)
            {
                failures.Add($"[用例 12 失败] 未知数据加工调用内的英文不应计为文案，实际 {rep12.UntranslatedEnglishCount}");
            }

            // 用例 13: 日志 / I18n 兜底槽中的英文不算源码文案（词典层单独审计）
            cases++;
            string case13 = "class C { void M() { MFPLogger.Info(\"EARTH POINTING\"); var s = I18n.Tr(\"TEST_KEY_HELLO\", \"POWER DISTRIBUTION\"); } }";
            var rep13 = AuditSnippet(case13, validKeys);
            if (rep13.UntranslatedEnglishCount != 0)
            {
                failures.Add($"[用例 13 失败] 日志与 I18n 兜底槽中的英文不应计为源码文案，实际 {rep13.UntranslatedEnglishCount}");
            }

            // 用例 14: 豁免判定必须按"最近宿主"生效 —— 嵌套调用里的中文不得被外层豁免规则整段放过
            //         （微控件注册 / 字符串比对 / 路径调用三条路径与用例 8 修掉的是同一类洞）
            cases++;
            string case14 = "class C { void M() { "
                          + "Controls.Register(new WidgetReadoutControl(null, null, r, \"N\", Helper(\"中文\"))); "
                          + "var b = s.Contains(Helper(\"中文\")); "
                          + "var p = Path.Combine(dir, Helper(\"中文\")); } }";
            var rep14 = AuditSnippet(case14, validKeys);
            if (rep14.HardcodedChineseCount != 3)
            {
                failures.Add($"[用例 14 失败] 微控件注册 / 字符串比对 / 路径调用中嵌套调用内的中文应各检出 1 处（共 3），实际 {rep14.HardcodedChineseCount}");
            }

            // 用例 15: 三类豁免的正向对照 —— 字面量"直接"作为被豁免调用的实参时必须放过
            cases++;
            string case15 = "class C { void M() { "
                          + "Controls.Register(new WidgetReadoutControl(null, null, r, \"N\", \"未绑读音\")); "
                          + "var b = s.Contains(\"中文比较\"); "
                          + "var p = Path.Combine(dir, \"中文路径\"); } }";
            var rep15 = AuditSnippet(case15, validKeys);
            if (rep15.HardcodedChineseCount != 0)
            {
                failures.Add($"[用例 15 失败] 字面量直接作为豁免调用实参时应放过，实际检出 {rep15.HardcodedChineseCount}: {rep15.Issues.FirstOrDefault()}");
            }

            // 用例 16: 棘轮完整性（基线必须登记冻结上限）
            cases++;
            var ratchetFailures = ValidateEnglishRatchet();
            if (ratchetFailures.Count != 0)
            {
                failures.Add("[用例 14 失败] 英文棘轮被破坏: " + ratchetFailures[0]);
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }

        private static I18nAuditReport AuditSnippet(string snippet, HashSet<string> validKeys)
        {
            var report = new I18nAuditReport();
            var tree = CSharpSyntaxTree.ParseText(snippet, path: "snippet.cs");
            var walker = new I18nAstWalker("snippet.cs", validKeys, report);
            walker.Visit(tree.GetRoot());
            return report;
        }
    }

    /// <summary>
    /// 词典"未汉化词条"审计 (Dictionary Value Localization Audit)
    ///
    /// 中文主语言的直接体现是 zh-CN.json：若某词条在 zh-CN 与 en-US 中逐字相同（且非权威缩写、
    /// 非纯通配符模板），说明该词条只是把英文原文抄了一遍 —— 玩家在中文界面看到的就是英文。
    /// 这类"抄写式未汉化"在键名对齐审计中完全隐身（键存在、占位符一致），必须单独判定。
    ///
    /// 判定口径与源码审计共用 I18nLexicon，避免出现第二份"权威缩写白名单"。
    /// 存量欠账按棘轮基线冻结，只降不升。
    /// </summary>
    public static class I18nDictionaryValueAudit
    {
        /// <summary>
        /// 未汉化词条棘轮基线：语言文件 -> 允许的未汉化词条数上限（只降不升）。
        /// 本轮 36 条存量已全部汉化；仅剩 2 条无语言信息的记法/专名（--i18n-baseline-dump 导出）：
        ///   WIDGET_SPX_CLOCK_PLACEHOLDER = "T+ 00:00:00"（时钟占位记法）
        ///   WIDGET_NAV_WP_NAME          = "PP518"（航点呼号专名）
        /// </summary>
        private static readonly Dictionary<string, int> BaselineTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zh-CN.json", 2 },
        };

        /// <summary>棘轮上限（冻结值）：基线永远不得高于此表</summary>
        private static readonly Dictionary<string, int> RatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zh-CN.json", 2 },
        };

        public sealed class UntranslatedEntry
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        public static int TotalRegisteredDebt
        {
            get
            {
                int total = 0;
                foreach (var kv in BaselineTable) total += kv.Value;
                return total;
            }
        }

        public static int GetAllowedOccurrences(string languageFileName)
        {
            if (string.IsNullOrEmpty(languageFileName)) return 0;
            return BaselineTable.TryGetValue(languageFileName, out int allowed) ? allowed : 0;
        }

        /// <summary>
        /// 扫描主语言词典中"与参照语言逐字相同"的词条（权威缩写 / 通配符模板除外）。
        /// </summary>
        public static List<UntranslatedEntry> Scan(Dictionary<string, string> primaryDict, Dictionary<string, string> referenceDict)
        {
            var result = new List<UntranslatedEntry>();
            if (primaryDict == null || referenceDict == null) return result;

            foreach (var kv in primaryDict)
            {
                // I18nJsonParser 为嵌套词条同时写入 "translations.KEY" 全路径与 "KEY" 短别名，
                // 全路径是同一条词条的副本；不跳过会让同一词条被数两次，基线直接翻倍。
                if (kv.Key.Contains('.') && primaryDict.ContainsKey(kv.Key.Substring(kv.Key.LastIndexOf('.') + 1))) continue;

                if (!referenceDict.TryGetValue(kv.Key, out string reference)) continue;
                if (!string.Equals(kv.Value, reference, StringComparison.Ordinal)) continue;
                if (I18nLexicon.IsExemptDisplayText(kv.Value)) continue;

                result.Add(new UntranslatedEntry { Key = kv.Key, Value = kv.Value });
            }

            return result;
        }

        /// <summary>棘轮预算判定：实测未汉化词条数超出基线 → 门禁失败</summary>
        public static List<string> ValidateBudget(int actualCount, string languageFileName)
        {
            var failures = new List<string>();
            int allowed = GetAllowedOccurrences(languageFileName);
            if (actualCount > allowed)
            {
                failures.Add($"[{languageFileName}] 新增未汉化词条 {actualCount - allowed} 条 (实测 {actualCount} / 基线 {allowed})；"
                           + "中文主语言词条必须汉化，权威缩写除外。可运行 --i18n-ast 查看明细。");
            }
            return failures;
        }

        /// <summary>棘轮完整性校验：基线必须登记冻结上限，且只允许下调</summary>
        public static List<string> ValidateRatchet()
        {
            var failures = new List<string>();

            foreach (var kv in BaselineTable)
            {
                if (!RatchetCeilingTable.TryGetValue(kv.Key, out int ceiling))
                {
                    failures.Add("词典基线未登记棘轮上限（禁止新增基线文件）: " + kv.Key + " = " + kv.Value);
                    continue;
                }
                if (kv.Value > ceiling)
                {
                    failures.Add("词典基线被上调（棘轮只允许下降）: " + kv.Key + " = " + kv.Value + " 高于冻结上限 " + ceiling);
                }
            }

            foreach (var kv in RatchetCeilingTable)
            {
                if (!BaselineTable.ContainsKey(kv.Key) && kv.Value != 0)
                {
                    failures.Add("词典棘轮上限登记了非零值但基线表里没有对应条目: " + kv.Key + " = " + kv.Value);
                }
            }

            return failures;
        }

        /// <summary>最近一次 SelfTest 实际执行的用例数</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// 词典值审计的正反用例自检 + 棘轮完整性校验。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            // 用例 1: 抄写式英文（整词）必须检出
            cases++;
            var zh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", "POWER DISTRIBUTION" },   // 抄写式英文 → 违规
                { "translations.A", "POWER DISTRIBUTION" },  // 解析器全路径副本 → 不得重复计数
                { "B", "SPX" },                  // 权威缩写 → 放行
                { "C", "SAS: OFF" },             // 缩写组合 → 放行
                { "D", "电源分配" },              // 已汉化 → 放行
                { "E", "Σ {DV:TOTALTIME}" },     // 通配符模板 → 放行
                { "F", "PAGE 1/1" },             // 英文文案 → 违规
            };
            var en = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", "POWER DISTRIBUTION" },
                { "translations.A", "POWER DISTRIBUTION" },
                { "B", "SPX" },
                { "C", "SAS: OFF" },
                { "D", "POWER DISTRIBUTION" },
                { "E", "Σ {DV:TOTALTIME}" },
                { "F", "PAGE 1/1" },
            };
            var hits = Scan(zh, en);
            if (hits.Count != 2 || hits.All(h => h.Key != "A") || hits.All(h => h.Key != "F"))
            {
                failures.Add($"[词典用例 1 失败] 期望检出 A / F 两条未汉化词条（全路径副本不重复计数），实际 {hits.Count} 条: "
                           + string.Join(",", hits.Select(h => h.Key)));
            }

            // 用例 2: 参照词典缺失该键时不判定
            cases++;
            var hits2 = Scan(new Dictionary<string, string> { { "X", "NO TELEMETRY LINK" } },
                             new Dictionary<string, string>());
            if (hits2.Count != 0)
            {
                failures.Add("[词典用例 2 失败] 参照词典无该键时不应判定未汉化");
            }

            // 用例 3: 棘轮完整性
            cases++;
            var ratchetFailures = ValidateRatchet();
            if (ratchetFailures.Count != 0)
            {
                failures.Add("[词典用例 3 失败] 棘轮被破坏: " + ratchetFailures[0]);
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }
    }
}