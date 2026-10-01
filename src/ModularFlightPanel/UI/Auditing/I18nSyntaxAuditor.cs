using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModularFlightPanel.UI;
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
            "SAS", "RCS", "HUD", "AP", "PREC", "DCK", "DOCK", "TGT", "ECAM", "EICAS", "ND", "SC", "GPWS", "ECLSS",
            // 飞行参数与动力装置读数
            "SPD", "ALT", "VSI", "VERTSPD", "HDG", "THR", "TWR", "MACH", "G", "Q", "MAX-Q", "PE", "EC", "COMM", "STG", "IGN",
            "ECC", "INC", "PER", "PRO", "RET", "NRM", "RAD", "ANT", "SOI", "TRAJ", "AN", "DN",
            "N1", "N2", "EPR", "EGT", "FF", "RPM", "ENG", "VIB", "REV", "MECO", "TO", "GA", "JETT", "ACC",
            "TAS", "IAS", "GS", "RA", "WT", "QTY", "PRESS", "TEMP", "SAT", "TAT", "CAB", "LDG", "RDR", "MON", "CH",
            // 时间 / 机构 / 项目 / 外部模组代号
            "UT", "MET", "MFP", "KSP", "SPX", "TDRS", "ISS", "DSN", "AFT", "FWD", "LO", "HI", "T", "RF", "FAR", "TF", "MJ",
            // 标准告警与状态代号
            "NORM", "CAUT", "WARN", "OK", "ERR", "ON", "OFF",
            // 单位与量纲符号 (含长距离天文单位与角度)
            "M", "KM", "MM", "GM", "TM", "AU", "LY", "DEG", "RAD", "S", "SEC", "MIN", "H", "D", "Y", "KN", "KPA", "ATM", "MS", "HZ", "FPS", "PX",
            "KB", "MB", "GB", "V", "A", "W", "k", "KG", "KGS", "C", "F", "PSI", "BPS", "KBPS", "MBPS", "DV",
            // 坐标 / 罗盘 / 通道 / 界面缩写 / 航电通用缩写 / 项目标识
            "X", "Y", "Z", "R", "B", "N", "E", "UI", "GUI", "ID", "OBT", "LAG", "ORBIT", "LOG", "PARTS", "LAYOUT", "CREW", "MODULAR", "FLIGHT", "PANEL", "HEX"
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

        public static bool IsAuthoritativeAbbreviation(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            return AuthoritativeAbbreviations.Contains(token);
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

            string withoutTags = RichTextTagRegex.Replace(text, " ");
            string withoutFiles = FileNameRegex.Replace(withoutTags, " ");
            string core = TokenHoleRegex.Replace(withoutFiles, " ");
            core = TrimDecorations(core);
            if (core.Length == 0) return true;

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

            if (Tokenize(core).Count == 0) return true;
            if (NumericOnlyRegex.IsMatch(core)) return true;
            if (IsAllAbbreviated(core)) return true;
            if (IsEngineStyleName(core)) return true;

            return false;
        }
    }

    /// <summary>
    /// 基于 Roslyn 抽象语法树与 SemanticCompilationProvider 语义编译的
    /// 穿透性全局国际化与文本硬编码审计套件（直达 Unity 底层父类与 C# 底层符号）
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
            { "AvionicsBarGaugeWidget.cs", 14 },
            { "B747EicasWidget.cs", 2 },
            { "B747LowerEicasWidget.cs", 2 },
            { "B787EicasWidget.cs", 2 },
            { "BaseFlightWidget.cs", 1 },
            { "CustomTokenTextWidget.cs", 3 },
            { "EcamAlertLogWidget.cs", 25 },
            { "ElectricalSystemWidget.cs", 2 },
            { "FavoriteToolbarWidget.cs", 3 },
            { "LifeSupportWidget.cs", 4 },
            { "ManeuverTimelineWidget.cs", 12 },
            { "ModernToolbarWidget.cs", 3 },
            { "NavballSphereWidget.cs", 33 },
            { "NDNavigationWidget.cs", 4 },
            { "PerformanceMonitorWidget.cs", 8 },
            { "RectangularNavballWidget.cs", 31 },
            { "ReferenceFrameWidget.cs", 1 },
            { "Rocket2DWidget.cs", 14 },
            { "SASDialWidget.cs", 12 },
            { "SignalStatusWidget.cs", 5 },
            { "SpaceXArcGaugeWidget.cs", 1 },
            { "SpaceXBottomBarWidget.cs", 2 },
            { "SpaceXDockingReticleWidget.cs", 6 },
            { "SpaceXHeaderWidget.cs", 21 },
            { "SpaceXOverviewWidget.cs", 25 },
            { "SpaceXTimelineWidget.cs", 2 },
            { "StageControlWidget.cs", 7 },
            { "StageDeltaVWidget.cs", 1 },
            { "StagingSequenceWidget.cs", 3 },
            { "VesselAttitudeSphereWidget.cs", 36 },
            { "WidgetDslControls.cs", 12 },
        };

        /// <summary>
        /// 可汉化英文的棘轮上限（冻结值）：基线永远不得高于此表。此表为空 = 任何文件都不允许登记基线。
        /// </summary>
        private static readonly Dictionary<string, int> EnglishRatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "AvionicsBarGaugeWidget.cs", 14 },
            { "B747EicasWidget.cs", 2 },
            { "B747LowerEicasWidget.cs", 2 },
            { "B787EicasWidget.cs", 2 },
            { "BaseFlightWidget.cs", 1 },
            { "CustomTokenTextWidget.cs", 3 },
            { "EcamAlertLogWidget.cs", 25 },
            { "ElectricalSystemWidget.cs", 2 },
            { "FavoriteToolbarWidget.cs", 3 },
            { "LifeSupportWidget.cs", 4 },
            { "ManeuverTimelineWidget.cs", 12 },
            { "ModernToolbarWidget.cs", 3 },
            { "NavballSphereWidget.cs", 33 },
            { "NDNavigationWidget.cs", 4 },
            { "PerformanceMonitorWidget.cs", 8 },
            { "RectangularNavballWidget.cs", 31 },
            { "ReferenceFrameWidget.cs", 1 },
            { "Rocket2DWidget.cs", 14 },
            { "SASDialWidget.cs", 12 },
            { "SignalStatusWidget.cs", 5 },
            { "SpaceXArcGaugeWidget.cs", 1 },
            { "SpaceXBottomBarWidget.cs", 2 },
            { "SpaceXDockingReticleWidget.cs", 6 },
            { "SpaceXHeaderWidget.cs", 21 },
            { "SpaceXOverviewWidget.cs", 25 },
            { "SpaceXTimelineWidget.cs", 2 },
            { "StageControlWidget.cs", 7 },
            { "StageDeltaVWidget.cs", 1 },
            { "StagingSequenceWidget.cs", 3 },
            { "VesselAttitudeSphereWidget.cs", 36 },
            { "WidgetDslControls.cs", 12 },
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

        public static int GetAllowedEnglishOccurrences(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            return EnglishBaselineTable.TryGetValue(fileName, out int allowed) ? allowed : 0;
        }

        public static bool IsEnglishBaselineRegistered(string fileName) =>
            !string.IsNullOrEmpty(fileName) && EnglishBaselineTable.ContainsKey(fileName);

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

        // ==========================================================================================
        // 中文硬编码棘轮（2026-09-29 新增）
        //
        // 背景：此前 `I18nAuditReport.TotalErrors`（中文硬编码 / 未登记键名）**只被打印，从不参与判定**
        // —— 实测可任意新增中文硬编码而门禁仍全绿（见 docs/SEMANTIC_COMPILATION_AUDIT.md §8.9.3）。
        // 现按英文文案的同一套口径收口，但两类问题的严重度不同：
        //
        //   · 未登记键名（代码调用了词典里没有的键）→ **零容忍**：这是硬错误，界面会直接显示原始 KEY。
        //   · 中文硬编码 → **棘轮**：存量冻结为基线，只拦"超出基线的新增"；基线只允许下降。
        //
        // 基线数据禁止手工估算，必须由 `--i18n-baseline-dump` 生成后回填（与英文表同一工具、同一口径）。
        // ==========================================================================================
        private static readonly Dictionary<string, int> ChineseBaselineTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 由 `--i18n-baseline-dump` 于 2026-10-01 生成。禁止手工估算。
            { "CompositePanelConfig.cs", 20 },
            { "CustomTokenTextWidget.cs", 1 },
            { "FlightHUDManager.cs", 1 },
            { "HUDEditModeToolbar.cs", 86 },
            { "MFPSafetyFallback.cs", 2 },
            { "StageDeltaVWidget.cs", 2 },
            { "TabAssembler.cs", 5 },
            { "TabStudio.cs", 104 },
            { "TelemetryMatrixData.cs", 22 },
            { "WidgetControlCatalog.cs", 16 },
            { "WidgetRenderManager.cs", 1 },
        };

        private static readonly Dictionary<string, int> ChineseRatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 冻结上限必须 ≥ 基线且此后只允许下调；ValidateChineseRatchet 会拒绝任何上调。
            { "CompositePanelConfig.cs", 20 },
            { "CustomTokenTextWidget.cs", 1 },
            { "FlightHUDManager.cs", 1 },
            { "HUDEditModeToolbar.cs", 86 },
            { "MFPSafetyFallback.cs", 2 },
            { "StageDeltaVWidget.cs", 2 },
            { "TabAssembler.cs", 5 },
            { "TabStudio.cs", 104 },
            { "TelemetryMatrixData.cs", 22 },
            { "WidgetControlCatalog.cs", 16 },
            { "WidgetRenderManager.cs", 1 },
        };

        public static int TotalRegisteredChineseDebt
        {
            get
            {
                int total = 0;
                foreach (var kv in ChineseBaselineTable) total += kv.Value;
                return total;
            }
        }

        public static int GetAllowedChineseOccurrences(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            return ChineseBaselineTable.TryGetValue(fileName, out int allowed) ? allowed : 0;
        }

        public static bool IsChineseBaselineRegistered(string fileName) =>
            !string.IsNullOrEmpty(fileName) && ChineseBaselineTable.ContainsKey(fileName);

        public static List<string> ValidateChineseRatchet()
        {
            var failures = new List<string>();

            foreach (var kv in ChineseBaselineTable)
            {
                if (!ChineseRatchetCeilingTable.TryGetValue(kv.Key, out int ceiling))
                {
                    failures.Add("中文基线未登记棘轮上限（禁止新增基线文件）: " + kv.Key + " = " + kv.Value
                               + "；确需登记时必须同时写入 ChineseRatchetCeilingTable 与 ChineseBaselineTable");
                    continue;
                }
                if (kv.Value > ceiling)
                {
                    failures.Add("中文基线被上调（棘轮只允许下降）: " + kv.Key + " = " + kv.Value + " 高于冻结上限 " + ceiling);
                }
            }

            foreach (var kv in ChineseRatchetCeilingTable)
            {
                if (!ChineseBaselineTable.ContainsKey(kv.Key) && kv.Value != 0)
                {
                    failures.Add("中文棘轮上限登记了非零值但基线表里没有对应条目: " + kv.Key + " = " + kv.Value);
                }
            }

            return failures;
        }

        public static List<string> ValidateChineseBudget(I18nAuditReport report)
        {
            var failures = new List<string>();
            if (report == null) return failures;

            // ── 1. 未登记键名：硬错误，零容忍（不允许登记基线）──
            if (report.MissingKeyCount > 0)
            {
                var missingSamples = string.Join(" | ", report.Issues
                    .Where(i => i.IssueType == I18nIssueType.MissingDictionaryKey)
                    .Take(3)
                    .Select(i => Path.GetFileName(i.FilePath) + ":L" + i.Line + " \"" + i.OffendingText + "\""));
                failures.Add("[零容忍] 源码调用了词典未定义的键名 " + report.MissingKeyCount
                           + " 处（界面会直接显示原始 KEY）；样本: " + missingSamples);
            }

            // ── 2. 中文硬编码：棘轮，只拦超出冻结基线的新增 ──
            var byFile = report.Issues
                .Where(i => i.IssueType == I18nIssueType.HardcodedChinese)
                .GroupBy(i => Path.GetFileName(i.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (var group in byFile)
            {
                int allowed = GetAllowedChineseOccurrences(group.Key);
                int actual = group.Count();
                if (actual <= allowed) continue;

                var samples = string.Join(" | ", group.Take(3).Select(i => $"L{i.Line} \"{i.OffendingText}\""));
                failures.Add($"[{group.Key}] 新增硬编码中文 {actual - allowed} 处 (实测 {actual} / 基线 {allowed})；"
                           + "中文主语言下应接入 I18n.Tr 词典或登记棘轮基线。样本: " + samples);
            }

            return failures;
        }

        /// <summary>
        /// 扫描指定目录及其子目录下的所有 C# 源码文件，统一构建全量语义编译环境并派发 SemanticModel
        /// </summary>
        public static I18nAuditReport AuditDirectory(string sourceDir, HashSet<string> validKeysInDictionary)
        {
            var report = new I18nAuditReport();
            if (!Directory.Exists(sourceDir)) return report;

            var files = Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExemptFile(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sources = new List<WidgetSourceFile>(files.Count);
            foreach (var file in files)
            {
                try
                {
                    sources.Add(new WidgetSourceFile
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Text = File.ReadAllText(file, Encoding.UTF8)
                    });
                }
                catch (Exception ex)
                {
                    report.Issues.Add(new I18nIssue
                    {
                        FilePath = file,
                        IssueType = I18nIssueType.HardcodedChinese,
                        Description = $"无法读取文件: {ex.Message}"
                    });
                }
            }

            string repoRoot = null;
            try
            {
                var dir = new DirectoryInfo(sourceDir);
                repoRoot = dir.Parent?.Parent?.FullName;
            }
            catch { }

            // 统一构建全量语义编译环境，提供权威的类型图与跨符号引用
            var compilationContext = SemanticCompilationProvider.BuildCompilation(sources, repoRoot);

            foreach (var file in files)
            {
                var model = compilationContext?.GetSemanticModel(file);
                AuditFile(file, validKeysInDictionary, report, model);
            }

            return report;
        }

        /// <summary>
        /// 扫描单个 C# 文件的语法树与语义模型
        /// </summary>
        public static void AuditFile(string filePath, HashSet<string> validKeysInDictionary, I18nAuditReport report, SemanticModel semanticModel = null)
        {
            report.ScannedFilesCount++;
            SemanticModel model = semanticModel;
            SyntaxNode root;

            if (model != null)
            {
                root = model.SyntaxTree.GetRoot();
            }
            else
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

                model = EnsureSemanticModel(filePath, code);
                root = model != null ? model.SyntaxTree.GetRoot() : RoslynAstHelper.ParseTree(code, filePath).GetRoot();
            }

            var walker = new I18nAstWalker(filePath, validKeysInDictionary, report, model);
            walker.Visit(root);
        }

        private static SemanticModel EnsureSemanticModel(string filePath, string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return null;

            const string prelude = "global using global::System;\nglobal using global::UnityEngine;\n";
            var sources = new[]
            {
                new WidgetSourceFile { Name = "GlobalUsings.cs", Path = "GlobalUsings.cs", Text = prelude },
                new WidgetSourceFile { Name = Path.GetFileName(filePath), Path = filePath, Text = sourceText }
            };

            return SemanticCompilationProvider.BuildCompilation(sources)?.GetSemanticModel(filePath);
        }

        private static bool IsExemptFile(string filePath)
        {
            string norm = filePath.Replace('\\', '/');

            if (norm.Contains("/obj/") || norm.Contains("/bin/")) return true;
            if (norm.Contains("/Auditing/") || norm.Contains("HeadlessValidator") || norm.Contains("WidgetColorLiteralAudit")) return true;

            if (norm.EndsWith("I18nManager.cs", StringComparison.OrdinalIgnoreCase) ||
                norm.EndsWith("I18nJsonParser.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (norm.Contains("/Core/Probes/") || norm.EndsWith("TelemetryCatalog.cs", StringComparison.OrdinalIgnoreCase) || norm.Contains("/Core/Rendering/"))
            {
                return true;
            }

            return false;
        }

        private static class I18nCalls
        {
            public const string Tr = "I18n.Tr";
            public const string TrFormat = "I18n.TrFormat";
            public const string ManagerTr = "I18nManager.Tr";
            public const string ManagerTrFormat = "I18nManager.TrFormat";
            public const string GetWidgetName = "I18n.GetWidgetName";
            public const string ManagerGetWidgetName = "I18nManager.GetWidgetName";

            public static bool IsKeyLookup(string expr) => expr == Tr || expr == ManagerTr;
            public static bool IsWidgetNameLookup(string expr) => expr == GetWidgetName || expr == ManagerGetWidgetName;
            public static bool IsKeyLookupFormat(string expr) => expr == TrFormat || expr == ManagerTrFormat;
            public static bool IsDictionaryKeyLookup(string expr) => IsKeyLookup(expr) || IsKeyLookupFormat(expr);
        }

        /// <summary>
        /// 穿透性语义语法树访问器：直达 UnityEngine 底层类型与 C# 底层符号
        /// </summary>
        private class I18nAstWalker : CSharpSyntaxWalker
        {
            private readonly string _filePath;
            private readonly HashSet<string> _validKeys;
            private readonly I18nAuditReport _report;
            private readonly SemanticModel _semanticModel;

            public I18nAstWalker(string filePath, HashSet<string> validKeys, I18nAuditReport report, SemanticModel semanticModel = null)
            {
                _filePath = filePath;
                _validKeys = validKeys;
                _report = report;
                _semanticModel = semanticModel;
            }

            public override void VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                base.VisitLiteralExpression(node);
                _report.ScannedAstNodesCount++;

                if (!node.IsKind(SyntaxKind.StringLiteralExpression)) return;

                string text = node.Token.ValueText;
                if (string.IsNullOrEmpty(text)) return;

                // 1. I18n 查表槽位判定（键名 / 兜底文案豁免，格式化实参不豁免）
                if (IsExemptI18nSlot(node)) return;

                // 2. 日志、诊断、调试或异常
                if (IsInsideLogOrDiagnostic(node)) return;

                // 3. 特性元数据
                if (node.Ancestors().OfType<AttributeSyntax>().Any()) return;

                // 4. 引擎底层 IO / 资源路径 / 正则
                if (IsInsideEngineResourceOrPathCall(node)) return;

                // 5. 字符串比较 / 匹配 (==, !=, switch case, Contains 等)
                if (IsInsideStringComparison(node)) return;

                // 6. 微控件与组件注册 (Controls.Register, WrapElement, new Widget*Control)
                if (IsInsideMicroControlRegistration(node)) return;

                // 7. 遥测探针读取调用
                if (IsInsideProbeOrTelemetryQuery(node)) return;

                var lineSpan = node.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                int col = lineSpan.StartLinePosition.Character + 1;

                // 【严重规则 A】非 I18n 上下文中的硬编码中文
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

                // 【穿透判定 B】Unity UI 表现层与 C# 组件作用域内的未汉化英文
                if (IsUntranslatedUiText(node, text, _filePath, out string channelName))
                {
                    _report.Issues.Add(new I18nIssue
                    {
                        FilePath = _filePath,
                        Line = line,
                        Column = col,
                        IssueType = I18nIssueType.UntranslatedEnglish,
                        OffendingText = text,
                        CodeSnippet = node.ToString(),
                        Description = $"UI 表现层 ({channelName}) 存在未汉化英文: \"{text}\"，"
                                    + "中文主语言下应使用 I18n.Tr(\"KEY\", \"{text}\") 接入词典。"
                    });
                }
            }

            public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            {
                base.VisitInterpolatedStringExpression(node);
                _report.ScannedAstNodesCount++;

                if (IsInsideLogOrDiagnostic(node)) return;
                if (IsExemptI18nSlot(node)) return;
                if (IsInsideMicroControlRegistration(node)) return;
                if (IsInsideProbeOrTelemetryQuery(node)) return;
                if (node.Ancestors().OfType<AttributeSyntax>().Any()) return;

                bool hasChinese = false;
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
                        hasChinese = true;
                        break;
                    }
                }

                if (hasChinese) return;

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

                if (IsUntranslatedUiText(node, templateText, _filePath, out string channelName))
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
                        Description = $"UI 表现层 ({channelName}) 插值字符串直接传入未汉化英文: \"{templateText.Trim()}\"，"
                                    + "中文主语言下应使用 I18n.TrFormat(\"KEY\", ...) 接入词典。"
                    });
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

            private bool IsExemptI18nSlot(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is ArgumentSyntax arg)) continue;
                    if (!(arg.Parent is ArgumentListSyntax argList)) continue;
                    if (!(argList.Parent is InvocationExpressionSyntax inv)) continue;

                    int index = argList.Arguments.IndexOf(arg);

                    if (_semanticModel != null)
                    {
                        var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                        if (symbol != null)
                        {
                            string type = symbol.ContainingType?.Name ?? string.Empty;
                            if (type == "I18n" || type == "I18nManager")
                            {
                                if (symbol.Name == "Tr" || symbol.Name == "GetWidgetName")
                                {
                                    return index <= 1;
                                }
                                if (symbol.Name == "TrFormat")
                                {
                                    return index == 0;
                                }
                            }
                        }
                    }

                    string expr = inv.Expression.ToString();
                    if (I18nCalls.IsKeyLookup(expr) || I18nCalls.IsWidgetNameLookup(expr))
                    {
                        return index <= 1;
                    }
                    if (I18nCalls.IsKeyLookupFormat(expr))
                    {
                        return index == 0;
                    }

                    return false;
                }
                return false;
            }

            private bool IsInsideLogOrDiagnostic(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        if (_semanticModel != null)
                        {
                            var type = _semanticModel.GetTypeInfo(oce).Type;
                            if (SemanticCompilationProvider.IsOrInheritsFrom(type, "System.Exception")) return true;
                        }
                        if (oce.Type.ToString().EndsWith("Exception", StringComparison.Ordinal)) return true;
                    }

                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    if (IsLogOrDiagnosticInvocation(inv)) return true;
                    if (IsStringAssemblyInvocation(inv)) continue;

                    return false;
                }
                return false;
            }

            private bool IsLogOrDiagnosticInvocation(InvocationExpressionSyntax inv)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    if (symbol != null)
                    {
                        var type = symbol.ContainingType;
                        if (type != null)
                        {
                            string fullType = type.ToDisplayString();
                            if (fullType == "UnityEngine.Debug" ||
                                fullType == "System.Console" ||
                                fullType == "System.Diagnostics.Debug" ||
                                fullType == "System.Diagnostics.Trace" ||
                                fullType == "ModularFlightPanel.Core.MFPLogger" ||
                                fullType == "KSPLog" ||
                                SemanticCompilationProvider.IsOrInheritsFrom(type, "System.Exception"))
                            {
                                return true;
                            }
                            if (type.Name.EndsWith("Logger", StringComparison.Ordinal) ||
                                type.Name.EndsWith("Diagnostic", StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                        if (symbol.Name == "print" && SemanticCompilationProvider.IsOrInheritsFrom(symbol.ContainingType, "UnityEngine.MonoBehaviour"))
                        {
                            return true;
                        }
                    }
                }

                string expr = inv.Expression.ToString();
                return expr.StartsWith("MFPLogger.", StringComparison.Ordinal)
                    || expr.StartsWith("Debug.", StringComparison.Ordinal)
                    || expr.StartsWith("Console.", StringComparison.Ordinal)
                    || expr.StartsWith("KSPLog.", StringComparison.Ordinal)
                    || expr == "print";
            }

            private bool IsStringAssemblyInvocation(InvocationExpressionSyntax inv)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    var containingType = symbol?.ContainingType;
                    if (containingType != null &&
                        (containingType.SpecialType == SpecialType.System_String ||
                         containingType.ToDisplayString() == "string" ||
                         containingType.ToDisplayString() == "System.String"))
                    {
                        return symbol.Name == "Format" || symbol.Name == "Concat" || symbol.Name == "Join";
                    }
                }

                string expr = inv.Expression.ToString();
                return expr.StartsWith("string.Format", StringComparison.Ordinal)
                    || expr.StartsWith("string.Concat", StringComparison.Ordinal)
                    || expr.StartsWith("string.Join", StringComparison.Ordinal)
                    || expr.StartsWith("String.Format", StringComparison.Ordinal)
                    || expr.StartsWith("String.Concat", StringComparison.Ordinal)
                    || expr.StartsWith("String.Join", StringComparison.Ordinal);
            }

            private bool IsInsideEngineResourceOrPathCall(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    if (IsEngineResourceOrPathInvocation(inv)) return true;
                    if (IsStringAssemblyInvocation(inv)) continue;

                    return false;
                }
                return false;
            }

            private bool IsEngineResourceOrPathInvocation(InvocationExpressionSyntax inv)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    if (symbol != null)
                    {
                        string ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                        if (ns == "System.IO" || ns == "System.Text.RegularExpressions") return true;

                        string containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
                        if (containingType == "UnityEngine.Shader" && symbol.Name == "Find") return true;
                        if (containingType == "UnityEngine.Resources" && symbol.Name == "Load") return true;
                        if (containingType == "UnityEngine.AssetBundle" || containingType == "GameDatabase") return true;
                    }
                }

                var receiver = RoslynAstHelper.GetInvocationReceiver(inv);
                string rName = receiver != null ? RoslynAstHelper.GetRightmostIdentifier(receiver) : string.Empty;
                if (rName == "Path" || rName == "Directory" || rName == "File" ||
                    rName == "AssetBundle" || rName == "GameDatabase" || rName == "Regex" ||
                    rName == "Shader" || rName == "Resources")
                {
                    return true;
                }

                string expr = inv.Expression.ToString();
                return expr.StartsWith("Path.", StringComparison.Ordinal)
                    || expr.StartsWith("Directory.", StringComparison.Ordinal)
                    || expr.StartsWith("File.", StringComparison.Ordinal)
                    || expr.StartsWith("Shader.Find", StringComparison.Ordinal)
                    || expr.StartsWith("AssetBundle.", StringComparison.Ordinal)
                    || expr.StartsWith("Resources.Load", StringComparison.Ordinal)
                    || expr.StartsWith("GameDatabase.", StringComparison.Ordinal)
                    || expr.StartsWith("Regex.", StringComparison.Ordinal);
            }

            private bool IsInsideStringComparison(SyntaxNode node)
            {
                if (node.Parent is BinaryExpressionSyntax binary)
                {
                    if (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))
                    {
                        return true;
                    }
                }

                if (node.Ancestors().OfType<SwitchLabelSyntax>().Any())
                {
                    return true;
                }

                foreach (var ancestor in node.Ancestors())
                {
                    if (!(ancestor is InvocationExpressionSyntax inv)) continue;

                    if (IsStringComparisonInvocation(inv)) return true;
                    if (IsStringAssemblyInvocation(inv)) continue;

                    return false;
                }
                return false;
            }

            private bool IsStringComparisonInvocation(InvocationExpressionSyntax inv)
            {
                if (_semanticModel != null)
                {
                    var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    if (symbol != null)
                    {
                        var containingType = symbol.ContainingType;
                        if (containingType != null &&
                            (containingType.SpecialType == SpecialType.System_String ||
                             containingType.ToDisplayString() == "string" ||
                             containingType.ToDisplayString() == "System.String" ||
                             containingType.ToDisplayString() == "System.Text.RegularExpressions.Regex"))
                        {
                            string mName = symbol.Name;
                            if (mName == "Contains" || mName == "Equals" || mName == "IndexOf" ||
                                mName == "StartsWith" || mName == "EndsWith" || mName == "IsMatch")
                            {
                                return true;
                            }
                        }
                    }
                }

                string expr = inv.Expression.ToString();
                return expr.EndsWith(".Contains", StringComparison.Ordinal)
                    || expr.EndsWith(".Equals", StringComparison.Ordinal)
                    || expr.EndsWith(".IndexOf", StringComparison.Ordinal)
                    || expr.EndsWith(".StartsWith", StringComparison.Ordinal)
                    || expr.EndsWith(".EndsWith", StringComparison.Ordinal);
            }

            private bool IsInsideMicroControlRegistration(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        if (_semanticModel != null)
                        {
                            var type = _semanticModel.GetTypeInfo(oce).Type;
                            if (SemanticCompilationProvider.IsOrInheritsFrom(type, "ModularFlightPanel.UI.Framework.WidgetControl"))
                            {
                                return true;
                            }
                        }
                        string typeName = oce.Type.ToString();
                        if (typeName.EndsWith("Control", StringComparison.Ordinal) && typeName.Contains("Widget"))
                        {
                            return true;
                        }
                        return false;
                    }

                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        if (_semanticModel != null)
                        {
                            var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                            if (symbol != null)
                            {
                                string mName = symbol.Name;
                                if (mName == "Register" || mName == "Wrap" || mName == "WrapElement")
                                {
                                    return true;
                                }
                            }
                        }
                        string expr = inv.Expression.ToString();
                        if (expr.Contains("WrapElement") || expr.Contains("Controls.Wrap") || expr.Contains("Controls.Register"))
                        {
                            return true;
                        }
                        if (IsStringAssemblyInvocation(inv)) continue;

                        return false;
                    }
                }
                return false;
            }

            private static readonly Regex NumberFormatRegex = new Regex(@"^[0#\.\,\:\;\-\+\s\%FfEeGgNnPpxX]+$", RegexOptions.Compiled);
            private static readonly Regex CommonFormatStrings = new Regex(@"^(F\d|N\d|P\d|G\d|D\d|0\.0+|00|\+00:00:00|HH:mm:ss|yyyy-MM-dd)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            private static bool IsMachineOrEnginePrimitive(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return true;
                string trimmed = text.Trim();

                if (CommonFormatStrings.IsMatch(trimmed) || NumberFormatRegex.IsMatch(trimmed)) return true;
                if (trimmed.StartsWith("_", StringComparison.Ordinal)) return true;

                if (trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (trimmed.Contains("_") && !trimmed.Contains(" ") && trimmed.ToUpperInvariant() == trimmed)
                {
                    return true;
                }

                if ((trimmed.Contains("=") || trimmed.EndsWith(";")) && !trimmed.Contains(" ") && trimmed.ToUpperInvariant() == trimmed)
                {
                    return true;
                }

                if (I18nLexicon.IsExemptDisplayText(trimmed)) return true;

                return false;
            }

            private bool IsInsideProbeOrTelemetryQuery(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        if (_semanticModel != null)
                        {
                            var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                            if (symbol != null)
                            {
                                string typeName = symbol.ContainingType?.Name ?? string.Empty;
                                string mName = symbol.Name;
                                if (typeName.Contains("Probe") || typeName.Contains("Telemetry") ||
                                    mName.StartsWith("ResolveNumeric", StringComparison.Ordinal) ||
                                    mName.StartsWith("ResolveString", StringComparison.Ordinal) ||
                                    mName.StartsWith("ResolveBoolean", StringComparison.Ordinal) ||
                                    mName == "HasProbe" || mName == "RegisterProbe")
                                {
                                    return true;
                                }
                            }
                        }

                        string expr = inv.Expression.ToString();
                        string methodName = RoslynAstHelper.GetInvokedMethodName(inv);
                        if (expr.Contains("ExternalProbeRegistry") ||
                            methodName.StartsWith("ResolveNumeric", StringComparison.Ordinal) ||
                            methodName.StartsWith("ResolveString", StringComparison.Ordinal) ||
                            methodName.StartsWith("ResolveBoolean", StringComparison.Ordinal) ||
                            methodName == "HasProbe" || methodName == "RegisterProbe")
                        {
                            return true;
                        }
                    }
                    if (ancestor is StatementSyntax) break;
                }
                return false;
            }

            private bool IsGameObjectHierarchyName(SyntaxNode node, string text)
            {
                // 1. 赋值给 .name (如 _sphereObject.name = "...") 或 key/id 变量
                if (node.Parent is AssignmentExpressionSyntax assign)
                {
                    if (_semanticModel != null)
                    {
                        var symbol = _semanticModel.GetSymbolInfo(assign.Left).Symbol;
                        if (symbol != null)
                        {
                            string sName = symbol.Name;
                            if (sName.EndsWith("name", StringComparison.OrdinalIgnoreCase) ||
                                sName.EndsWith("key", StringComparison.OrdinalIgnoreCase) ||
                                sName.EndsWith("id", StringComparison.OrdinalIgnoreCase) ||
                                sName.Equals("tag", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    string left = assign.Left.ToString();
                    if (left.EndsWith(".name", StringComparison.OrdinalIgnoreCase) ||
                        left.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                        left.EndsWith("name", StringComparison.OrdinalIgnoreCase) ||
                        left.EndsWith("Key", StringComparison.Ordinal) ||
                        left.EndsWith("Id", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                // 2. 结构前缀
                if (!text.Contains(" "))
                {
                    if (text.StartsWith("Btn_", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("Slot_", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("Row_", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("Pip_", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("Panel_", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("KSP_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ParameterSyntax param)
                    {
                        string paramName = param.Identifier.ValueText;
                        if (paramName.EndsWith("name", StringComparison.OrdinalIgnoreCase) ||
                            paramName.EndsWith("key", StringComparison.OrdinalIgnoreCase) ||
                            paramName.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    if (ancestor is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax argList)
                    {
                        int index = argList.Arguments.IndexOf(arg);
                        if (argList.Parent is InvocationExpressionSyntax inv)
                        {
                            if (_semanticModel != null)
                            {
                                var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                                if (symbol != null)
                                {
                                    if (symbol.Parameters.Length > index)
                                    {
                                        string pName = symbol.Parameters[index].Name;
                                        if (pName.EndsWith("name", StringComparison.OrdinalIgnoreCase) ||
                                            pName.EndsWith("key", StringComparison.OrdinalIgnoreCase) ||
                                            pName.EndsWith("id", StringComparison.OrdinalIgnoreCase) ||
                                            pName.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                                            pName.Equals("tag", StringComparison.OrdinalIgnoreCase))
                                        {
                                            return true;
                                        }
                                    }
                                    if (symbol.Name == "Find" || symbol.Name == "FindChild" || symbol.Name == "GetChild")
                                    {
                                        if (SemanticCompilationProvider.IsOrInheritsFrom(symbol.ContainingType, "UnityEngine.Component") ||
                                            SemanticCompilationProvider.IsOrInheritsFrom(symbol.ContainingType, "UnityEngine.GameObject"))
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }

                            string methodName = RoslynAstHelper.GetInvokedMethodName(inv);
                            string expr = inv.Expression.ToString();

                            if ((expr.StartsWith("UIFactory.Create", StringComparison.Ordinal) || methodName.StartsWith("Create", StringComparison.Ordinal)) && index == 1)
                            {
                                return true;
                            }
                            if ((methodName == "CreateChild" || methodName == "CreateContainer" || methodName == "CreatePrimitive" ||
                                 methodName == "CreateButton" || methodName == "CreateViewport" || methodName == "CreateRawImage" ||
                                 methodName == "CreatePanel") && index == 0)
                            {
                                return true;
                            }
                            if (methodName == "Find" || methodName == "FindChild" || methodName == "GetChild")
                            {
                                return true;
                            }
                        }
                        if (argList.Parent is ObjectCreationExpressionSyntax oce)
                        {
                            if (_semanticModel != null)
                            {
                                var type = _semanticModel.GetTypeInfo(oce).Type;
                                if (type?.ToDisplayString() == "UnityEngine.GameObject") return true;
                            }
                            string typeName = oce.Type.ToString();
                            if (typeName == "GameObject" || typeName.EndsWith(".GameObject", StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }
                    if (ancestor is MethodDeclarationSyntax || ancestor is ClassDeclarationSyntax) break;
                }
                return false;
            }

            private static bool IsTemplateChannelKey(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax argList)
                    {
                        int index = argList.Arguments.IndexOf(arg);
                        if (argList.Parent is InvocationExpressionSyntax inv)
                        {
                            string methodName = RoslynAstHelper.GetInvokedMethodName(inv);
                            if (methodName.StartsWith("GetTemplateChannel", StringComparison.Ordinal) && index == 0)
                            {
                                return true;
                            }
                        }
                        break;
                    }
                    if (ancestor is MethodDeclarationSyntax || ancestor is ClassDeclarationSyntax) break;
                }
                return false;
            }

            private static bool IsConstantOrStaticFieldDefinition(SyntaxNode node)
            {
                var field = node.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
                if (field != null)
                {
                    bool isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);
                    bool isStatic = field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
                    if (isConst || isStatic) return true;
                }
                return false;
            }

            private static bool IsInsideIndexerAccess(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is BracketedArgumentListSyntax) return true;
                    if (ancestor is StatementSyntax) break;
                }
                return false;
            }

            private bool IsTerminalUiSink(SyntaxNode node, out string sinkDescription)
            {
                sinkDescription = string.Empty;
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is AssignmentExpressionSyntax assign)
                    {
                        if (_semanticModel != null)
                        {
                            var symbol = _semanticModel.GetSymbolInfo(assign.Left).Symbol;
                            if (symbol is IPropertySymbol || symbol is IFieldSymbol)
                            {
                                string propName = symbol.Name;
                                var containingType = symbol.ContainingType;
                                if (propName.Equals("text", StringComparison.OrdinalIgnoreCase) ||
                                    propName.Equals("tooltip", StringComparison.OrdinalIgnoreCase) ||
                                    propName.Equals("label", StringComparison.OrdinalIgnoreCase) ||
                                    propName.Equals("title", StringComparison.OrdinalIgnoreCase) ||
                                    propName == "Text")
                                {
                                    if (SemanticCompilationProvider.IsOrInheritsFrom(containingType, "UnityEngine.UI.Graphic") ||
                                        SemanticCompilationProvider.IsOrInheritsFrom(containingType, "UnityEngine.Component") ||
                                        containingType?.ToDisplayString() == "UnityEngine.GUIContent" ||
                                        containingType?.ToDisplayString() == "UnityEngine.ScreenMessage" ||
                                        containingType?.Name == "TextWidget")
                                    {
                                        sinkDescription = symbol.ToDisplayString();
                                        return true;
                                    }
                                }
                            }
                        }

                        string left = assign.Left.ToString();
                        if (left.EndsWith(".text", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".tooltip", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".label", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".title", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".Text", StringComparison.Ordinal))
                        {
                            sinkDescription = left;
                            return true;
                        }
                    }

                    if (ancestor is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax argList)
                    {
                        int index = argList.Arguments.IndexOf(arg);
                        if (argList.Parent is InvocationExpressionSyntax inv)
                        {
                            if (_semanticModel != null)
                            {
                                var symbol = _semanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                                if (symbol != null)
                                {
                                    var containingType = symbol.ContainingType;
                                    string fullType = containingType?.ToDisplayString() ?? string.Empty;
                                    if (fullType == "UnityEngine.GUILayout" || fullType == "UnityEngine.GUI")
                                    {
                                        sinkDescription = fullType + "." + symbol.Name;
                                        return true;
                                    }
                                    if (symbol.Name.StartsWith("SetText", StringComparison.Ordinal))
                                    {
                                        sinkDescription = symbol.Name;
                                        return true;
                                    }
                                    if (containingType?.Name == "TextWidget")
                                    {
                                        sinkDescription = "TextWidget." + symbol.Name;
                                        return true;
                                    }
                                    if (symbol.Name == "PushLogEntry")
                                    {
                                        sinkDescription = symbol.Name;
                                        return true;
                                    }
                                    if (symbol.Name == "GetTemplateChannel")
                                    {
                                        if (index == 1)
                                        {
                                            sinkDescription = symbol.Name;
                                            return true;
                                        }
                                        return false;
                                    }
                                    if ((containingType?.Name == "UIFactory" || symbol.Name.StartsWith("Create", StringComparison.Ordinal)) && index >= 2)
                                    {
                                        sinkDescription = symbol.Name + " 第" + index + "参";
                                        return true;
                                    }
                                }
                            }

                            string expr = inv.Expression.ToString();
                            string methodName = RoslynAstHelper.GetInvokedMethodName(inv);

                            if (expr.StartsWith("GUILayout.", StringComparison.Ordinal) || expr.StartsWith("GUI.", StringComparison.Ordinal))
                            {
                                sinkDescription = expr;
                                return true;
                            }
                            if (methodName.StartsWith("SetText", StringComparison.Ordinal) ||
                                expr.StartsWith("TextWidget.", StringComparison.Ordinal) ||
                                methodName == "PushLogEntry")
                            {
                                sinkDescription = methodName;
                                return true;
                            }
                            if (methodName == "GetTemplateChannel")
                            {
                                if (index == 1)
                                {
                                    sinkDescription = methodName;
                                    return true;
                                }
                                return false;
                            }
                            if ((expr.StartsWith("UIFactory.Create", StringComparison.Ordinal) || methodName.StartsWith("Create", StringComparison.Ordinal)) && index >= 2)
                            {
                                sinkDescription = expr + " 第" + index + "参";
                                return true;
                            }
                        }
                        if (argList.Parent is ObjectCreationExpressionSyntax oce)
                        {
                            if (_semanticModel != null)
                            {
                                var type = _semanticModel.GetTypeInfo(oce).Type;
                                if (type != null)
                                {
                                    string tName = type.Name;
                                    if (tName == "GUIContent" || tName == "ScreenMessage" || tName == "TextWidget" ||
                                        tName == "AlertItem" || tName == "EcamLogEntry")
                                    {
                                        sinkDescription = "new " + tName;
                                        return true;
                                    }
                                }
                            }

                            string typeName = oce.Type.ToString();
                            if (typeName == "GUIContent" || typeName.EndsWith(".GUIContent", StringComparison.Ordinal) ||
                                typeName == "ScreenMessage" || typeName.EndsWith(".ScreenMessage", StringComparison.Ordinal) ||
                                typeName == "TextWidget" || typeName.EndsWith(".TextWidget", StringComparison.Ordinal) ||
                                typeName == "AlertItem" || typeName.EndsWith(".AlertItem", StringComparison.Ordinal) ||
                                typeName == "EcamLogEntry" || typeName.EndsWith(".EcamLogEntry", StringComparison.Ordinal))
                            {
                                sinkDescription = "new " + typeName;
                                return true;
                            }
                        }
                    }
                }
                return false;
            }

            private bool IsUnderFlightWidgetOrUiScope(SyntaxNode node, string filePath)
            {
                var classDecl = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDecl != null)
                {
                    if (_semanticModel != null)
                    {
                        var typeSymbol = _semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
                        if (typeSymbol != null)
                        {
                            if (SemanticCompilationProvider.IsOrInheritsFrom(typeSymbol, "ModularFlightPanel.UI.Framework.BaseFlightWidget") ||
                                SemanticCompilationProvider.IsOrInheritsFrom(typeSymbol, "BaseFlightWidget"))
                            {
                                return true;
                            }
                            if (typeSymbol.Name.EndsWith("Widget", StringComparison.Ordinal) && !typeSymbol.IsAbstract)
                            {
                                return true;
                            }
                        }
                    }

                    if (classDecl.BaseList != null)
                    {
                        foreach (var baseType in classDecl.BaseList.Types)
                        {
                            string baseName = baseType.Type.ToString();
                            if (baseName == "BaseFlightWidget" || baseName.EndsWith(".BaseFlightWidget", StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }
                    string className = classDecl.Identifier.ValueText;
                    if (className.EndsWith("Widget", StringComparison.Ordinal) && !className.StartsWith("IWidget", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                string norm = filePath.Replace('\\', '/');
                if (norm.Contains("/UI/Widgets/"))
                {
                    return true;
                }

                return false;
            }

            private static bool HasNaturalLanguageEnglishWord(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                if (ChineseRegex.IsMatch(text)) return false;

                string stripped = Regex.Replace(text, @"</?[A-Za-z0-9]+(?:=[^>]*?)?>", " ");
                var tokens = Regex.Matches(stripped, @"[A-Za-z]{2,}")
                                  .Cast<Match>()
                                  .Select(m => m.Value)
                                  .ToList();
                if (tokens.Count == 0) return false;

                int nonAbbrCount = 0;
                foreach (var token in tokens)
                {
                    if (!I18nLexicon.IsAuthoritativeAbbreviation(token))
                    {
                        nonAbbrCount++;
                    }
                }
                return nonAbbrCount > 0;
            }

            private bool IsUntranslatedUiText(SyntaxNode node, string text, string filePath, out string channelName)
            {
                channelName = string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return false;

                if (IsMachineOrEnginePrimitive(text)) return false;
                if (IsConstantOrStaticFieldDefinition(node)) return false;
                if (IsGameObjectHierarchyName(node, text)) return false;
                if (IsTemplateChannelKey(node)) return false;
                if (IsInsideIndexerAccess(node)) return false;
                if (!HasNaturalLanguageEnglishWord(text)) return false;

                if (IsTerminalUiSink(node, out string sinkDescription))
                {
                    channelName = sinkDescription;
                    return true;
                }

                if (IsUnderFlightWidgetOrUiScope(node, filePath))
                {
                    channelName = "飞行仪表组件作用域穿透";
                    return true;
                }

                return false;
            }
        }

        public static int LastSelfTestCaseCount { get; private set; }

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

            // 用例 6: 插值字符串里的硬编码中文同样必须检出
            cases++;
            string case6 = "class C { void M() { var x = $\"未翻译{1}中文\"; } }";
            var rep6 = AuditSnippet(case6, validKeys);
            if (rep6.HardcodedChineseCount != 1)
            {
                failures.Add($"[用例 6 失败] 插值字符串中的硬编码中文应检出 1 处，实际 {rep6.HardcodedChineseCount}");
            }

            // 用例 7: I18n 查表调用的"非键名槽位"不是豁免区
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

            // 用例 9: 直接作为日志实参的中文仍然豁免
            cases++;
            string case9 = "class C { void M() { MFPLogger.Info(\"中文日志\"); Debug.Log(string.Format(\"中文 {0}\", 1)); } }";
            var rep9 = AuditSnippet(case9, validKeys);
            if (rep9.HardcodedChineseCount != 0)
            {
                failures.Add($"[用例 9 失败] 日志直接实参与 string.Format 组装应当豁免，实际 {rep9.HardcodedChineseCount}");
            }

            // 用例 10: UGUI 文案通道的可汉化英文必须检出
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

            // 用例 12: 飞行仪表组件作用域穿透
            cases++;
            string case12 = "class MyFlightWidget : BaseFlightWidget { void M() { "
                          + "string alert = \"LOCAL TEXT NOT IN SLOT\"; "
                          + "var data = new CustomDomainStruct(\"STRUCT TEXT NOT IN SLOT\"); } }";
            var rep12 = AuditSnippet(case12, validKeys);
            if (rep12.UntranslatedEnglishCount != 2)
            {
                failures.Add($"[用例 12 失败] 期望穿透检出 2 处未汉化英文 (局部变量 / 任意结构体构造)，实际检出 {rep12.UntranslatedEnglishCount}");
            }

            // 用例 13: 日志 / I18n 兜底槽中的英文不算源码文案
            cases++;
            string case13 = "class C { void M() { MFPLogger.Info(\"EARTH POINTING\"); var s = I18n.Tr(\"TEST_KEY_HELLO\", \"POWER DISTRIBUTION\"); } }";
            var rep13 = AuditSnippet(case13, validKeys);
            if (rep13.UntranslatedEnglishCount != 0)
            {
                failures.Add($"[用例 13 失败] 日志与 I18n 兜底槽中的英文不应计为源码文案，实际 {rep13.UntranslatedEnglishCount}");
            }

            // 用例 14: 豁免判定必须按"最近宿主"生效
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

            // 用例 15: 三类豁免的正向对照
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

            // 用例 16: 棘轮完整性
            cases++;
            var ratchetFailures = ValidateEnglishRatchet();
            if (ratchetFailures.Count != 0)
            {
                failures.Add("[用例 16 失败] 英文棘轮被破坏: " + ratchetFailures[0]);
            }

            // 用例 17: 飞行组件内的 SetTextSafe 终端调用与领域结构体 AlertItem/EcamLogEntry 中的未汉化英文必须检出
            cases++;
            string case17 = "class AnnunciatorWidget : BaseFlightWidget { void M() { "
                          + "_lbl.SetTextSafe(\"NO COMM\"); "
                          + "var a = new AlertItem(\"LOW FUEL\", \"OFF\", false); "
                          + "var b = new EcamLogEntry(EcamAlertSeverity.Warning, \"ENG\", \"!\", \"MIN FUEL EMERGENCY\", \"5%\", \"+00:00:00\", 0.0, true); } }";
            var rep17 = AuditSnippet(case17, validKeys);
            if (rep17.UntranslatedEnglishCount != 3)
            {
                failures.Add($"[用例 17 失败] 期望检出 3 处未汉化英文 (SetTextSafe / AlertItem / EcamLogEntry)，实际检出 {rep17.UntranslatedEnglishCount}");
            }

            // 用例 18: 中文硬编码棘轮 —— 完整性 + 判定确实生效
            // 背景：该项此前"只打印不判定"，可任意新增中文硬编码而门禁全绿（docs/…§8.9.3）。
            // 本用例同时锁住三件事：基线表自洽、基线非空、以及越界/缺键必须被拦下。
            cases++;
            var chineseRatchetFailures = ValidateChineseRatchet();
            if (chineseRatchetFailures.Count != 0)
            {
                failures.Add("[用例 18 失败] 中文硬编码棘轮被破坏: " + chineseRatchetFailures[0]);
            }
            if (TotalRegisteredChineseDebt <= 0)
            {
                failures.Add("[用例 18 失败] 中文硬编码基线为空 —— 棘轮形同虚设（存量欠账必须有冻结基线，否则等于零容忍未达成）");
            }
            var overBudgetProbe = new I18nAuditReport();
            overBudgetProbe.Issues.Add(new I18nIssue
            {
                FilePath = "NeverRegisteredProbe.cs",
                Line = 1,
                IssueType = I18nIssueType.HardcodedChinese,
                OffendingText = "未登记文件里的新增中文",
            });
            if (ValidateChineseBudget(overBudgetProbe).Count == 0)
            {
                failures.Add("[用例 18 失败] 未登记文件中的新增硬编码中文未被拦下（判定退化为只打印）");
            }
            var missingKeyProbe = new I18nAuditReport();
            missingKeyProbe.Issues.Add(new I18nIssue
            {
                FilePath = "AnyFile.cs",
                Line = 1,
                IssueType = I18nIssueType.MissingDictionaryKey,
                OffendingText = "NO_SUCH_KEY",
            });
            if (ValidateChineseBudget(missingKeyProbe).Count == 0)
            {
                failures.Add("[用例 18 失败] 未登记键名未按零容忍拦下（界面会直接显示原始 KEY）");
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }

        private static I18nAuditReport AuditSnippet(string snippet, HashSet<string> validKeys)
        {
            var report = new I18nAuditReport();
            var model = EnsureSemanticModel("snippet.cs", snippet);
            var root = model != null ? model.SyntaxTree.GetRoot() : RoslynAstHelper.ParseTree(snippet, "snippet.cs").GetRoot();
            var walker = new I18nAstWalker("snippet.cs", validKeys, report, model);
            walker.Visit(root);
            return report;
        }
    }

    /// <summary>
    /// 词典"未汉化词条"审计 (Dictionary Value Localization Audit)
    /// </summary>
    public static class I18nDictionaryValueAudit
    {
        private static readonly Dictionary<string, int> BaselineTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zh-CN.json", 2 },
        };

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

        public static List<UntranslatedEntry> Scan(Dictionary<string, string> primaryDict, Dictionary<string, string> referenceDict)
        {
            var result = new List<UntranslatedEntry>();
            if (primaryDict == null || referenceDict == null) return result;

            foreach (var kv in primaryDict)
            {
                if (kv.Key.Contains('.') && primaryDict.ContainsKey(kv.Key.Substring(kv.Key.LastIndexOf('.') + 1))) continue;

                if (!referenceDict.TryGetValue(kv.Key, out string reference)) continue;
                if (!string.Equals(kv.Value, reference, StringComparison.Ordinal)) continue;
                if (I18nLexicon.IsExemptDisplayText(kv.Value)) continue;

                result.Add(new UntranslatedEntry { Key = kv.Key, Value = kv.Value });
            }

            return result;
        }

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

        public static int LastSelfTestCaseCount { get; private set; }

        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            cases++;
            var zh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", "POWER DISTRIBUTION" },
                { "translations.A", "POWER DISTRIBUTION" },
                { "B", "SPX" },
                { "C", "SAS: OFF" },
                { "D", "电源分配" },
                { "E", "Σ {DV:TOTALTIME}" },
                { "F", "PAGE 1/1" },
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

            cases++;
            var hits2 = Scan(new Dictionary<string, string> { { "X", "NO TELEMETRY LINK" } },
                             new Dictionary<string, string>());
            if (hits2.Count != 0)
            {
                failures.Add("[词典用例 2 失败] 参照词典无该键时不应判定未汉化");
            }

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