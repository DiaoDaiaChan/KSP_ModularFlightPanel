using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        /// UI 渲染方法（如 GUILayout.Button / Label）中直接传入了未国际化的英文字符串字面量
        /// </summary>
        HardcodedUiCall,

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
        public int HardcodedUiCallCount => Issues.Count(i => i.IssueType == I18nIssueType.HardcodedUiCall);
        public int MissingKeyCount => Issues.Count(i => i.IssueType == I18nIssueType.MissingDictionaryKey);
        public int TotalErrors => HardcodedChineseCount + MissingKeyCount;
    }

    /// <summary>
    /// 基于 Roslyn 抽象语法树 (AST) 的全局国际化与文本硬编码审计套件
    /// </summary>
    public static class I18nSyntaxAuditor
    {
        private static readonly Regex ChineseRegex = new Regex(@"[\u4e00-\u9fa5]", RegexOptions.Compiled);



        private static readonly HashSet<string> ExemptShortTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SPD", "SPEED", "ALT", "ALTITUDE", "VSI", "VERTSPD", "HDG", "HEADING",
            "PITCH", "ROLL", "YAW", "THROTTLE", "THR", "AP", "PE", "TWR", "G", "Q",
            "MACH", "SAS", "RCS", "BODY", "FRAME", "EC", "COMM", "STAGE", "STG", "PROP", "SURFACE", "k",
            "NORM", "CAUT", "WARN", "OK", "ERR", "ON", "OFF", "X", "Y", "Z", "W",
            "AUTO", "MANUAL", "LOCK", "FREE", "ARM", "DISARM", "RCS/SAS", "HUD",
            "GUI", "UI", "ID", "FPS", "HZ", "MS", "KB", "MB", "GB", "V", "A", "W",
            "KN", "KPA", "ATM", "M/S", "KM", "M", "S", "MIN", "H", "D", "Y",
            // Unity built-in GUIStyle & control names
            "label", "Button", "box", "textfield",
            // Channels, states, & abbreviations
            "R", "G", "B", "A", "hard", "soft", "none", "UT", "MET", "MFP", "KSP", "PREC", "DCK", "DOCK",
            "TITLE", "SUBTITLE", "ELEC", "COMMNET", "LINKED", "SEARCHING", "STANDBY", "BURNING!",
            "LIVE", "BYPASS", "WARNING", "CAUTION", "NOMINAL", "NO NODE", "BURN IN",
            "RESUME MFP HUD", "BYPASS MFP (ZERO OVERHEAD)", "PRIMARY COMM DISH", "DIRECT · 5.0k POWER", "NO TELEMETRY LINK"
        };

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

                // 【警告规则 B】直接传入 GUI/GUILayout/UGUI 的原生英文字面量
                if (IsInsideUiCall(node, out string uiMethodName))
                {
                    if (!IsExemptUiLiteral(text))
                    {
                        _report.Issues.Add(new I18nIssue
                        {
                            FilePath = _filePath,
                            Line = line,
                            Column = col,
                            IssueType = I18nIssueType.HardcodedUiCall,
                            OffendingText = text,
                            CodeSnippet = node.ToString(),
                            Description = $"UI 渲染方法 ({uiMethodName}) 直接传入未国际化字面量: \"{text}\"，建议使用 I18n.Tr 进行多语言接入。"
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

                // 仅检查插值字符串中除 {...} 表达式之外的纯文本部分 (InterpolatedStringTextSyntax)
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
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                base.VisitInvocationExpression(node);
                _report.ScannedAstNodesCount++;

                string expr = node.Expression.ToString();
                if (expr == "I18n.Tr" || expr == "I18n.TrFormat" || expr == "I18nManager.Tr" || expr == "I18nManager.TrFormat")
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

                    if (expr == "I18n.Tr" || expr == "I18nManager.Tr" ||
                        expr == "I18n.GetWidgetName" || expr == "I18nManager.GetWidgetName")
                    {
                        // Tr(key, fallback) / GetWidgetName(widgetId, defaultDisplayName)：前两个槽位都是查表输入
                        return index <= 1;
                    }
                    if (expr == "I18n.TrFormat" || expr == "I18nManager.TrFormat")
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
            /// 判定字符串是否属于日志 / 诊断 / 异常文案。
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

            private static bool IsLogOrDiagnosticCall(string expr)
            {
                return expr.StartsWith("MFPLogger.", StringComparison.Ordinal)
                    || expr.StartsWith("Debug.", StringComparison.Ordinal)
                    || expr.StartsWith("Console.", StringComparison.Ordinal)
                    || expr.StartsWith("KSPLog.", StringComparison.Ordinal)
                    || expr == "print";
            }

            private static bool IsInsideEngineResourceOrPathCall(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        string expr = inv.Expression.ToString();
                        if (expr.StartsWith("Path.", StringComparison.Ordinal) ||
                            expr.StartsWith("Directory.", StringComparison.Ordinal) ||
                            expr.StartsWith("File.", StringComparison.Ordinal) ||
                            expr.StartsWith("Shader.Find", StringComparison.Ordinal) ||
                            expr.StartsWith("AssetBundle.", StringComparison.Ordinal) ||
                            expr.StartsWith("Resources.Load", StringComparison.Ordinal) ||
                            expr.StartsWith("GameDatabase.", StringComparison.Ordinal) ||
                            expr.StartsWith("Regex.", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            private static bool IsInsideStringComparison(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        string expr = inv.Expression.ToString();
                        if (expr.EndsWith(".Contains") ||
                            expr.EndsWith(".Equals") ||
                            expr.EndsWith(".IndexOf") ||
                            expr.EndsWith(".StartsWith") ||
                            expr.EndsWith(".EndsWith"))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            private static bool IsInsideMicroControlRegistration(SyntaxNode node)
            {
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        string type = oce.Type.ToString();
                        if (type.EndsWith("Control", StringComparison.Ordinal) && type.Contains("Widget")) return true;
                    }
                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        string expr = inv.Expression.ToString();
                        if (expr.Contains("WrapElement") || expr.Contains("Controls.Wrap") || expr.Contains("Controls.Register")) return true;
                    }
                }
                return false;
            }

            private static bool IsInsideUiCall(SyntaxNode node, out string uiMethodName)
            {
                uiMethodName = string.Empty;
                foreach (var ancestor in node.Ancestors())
                {
                    if (ancestor is InvocationExpressionSyntax inv)
                    {
                        string expr = inv.Expression.ToString();
                        if (expr.StartsWith("GUILayout.") || expr.StartsWith("GUI."))
                        {
                            uiMethodName = expr;
                            return true;
                        }
                    }

                    if (ancestor is ObjectCreationExpressionSyntax oce)
                    {
                        string typeName = oce.Type.ToString();
                        if (typeName == "GUIContent")
                        {
                            uiMethodName = "new GUIContent";
                            return true;
                        }
                    }

                    if (ancestor is AssignmentExpressionSyntax assign)
                    {
                        string left = assign.Left.ToString();
                        if (left.EndsWith(".text", StringComparison.OrdinalIgnoreCase) ||
                            left.EndsWith(".tooltip", StringComparison.OrdinalIgnoreCase))
                        {
                            // 若字面量处于赋值右侧调用的某个方法参数中（如 GetTemplateChannel("KEY", "DEF")），非直接字面量赋值
                            if (node.Ancestors().TakeWhile(a => a != assign).OfType<InvocationExpressionSyntax>().Any())
                            {
                                continue;
                            }

                            uiMethodName = left;
                            return true;
                        }
                    }
                }
                return false;
            }

            private static bool IsExemptUiLiteral(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return true;
                if (ExemptShortTokens.Contains(text)) return true;

                // 若剥离前导与后置装饰符号（如 ▲, ●, ○, ▶, ⏸, «, », · 等）后命中豁免列表，合法放行
                string stripped = Regex.Replace(text.Trim(), @"^[\s\u25A0-\u25FF\u2B00-\u2BFF\u2190-\u21FF\u00AB\u00BB\u2022\u25CF\u25CB\u25B6\u25C0\u23F8\u23F5\.\,\-\+\#\:\/\[\]]+", "").Trim();
                stripped = Regex.Replace(stripped, @"[\s\u25A0-\u25FF\u2B00-\u2BFF\u2190-\u21FF\u00AB\u00BB\u2022\u25CF\u25CB\u25B6\u25C0\u23F8\u23F5\.\,\-\+\#\:\/\[\]]+$", "").Trim();
                if (ExemptShortTokens.Contains(stripped)) return true;

                // 若字符串内完全没有字母（全为标点、符号、数字、空格、Emoji），属于排版修饰，直接放行
                bool hasLetter = false;
                for (int i = 0; i < text.Length; i++)
                {
                    if (char.IsLetter(text[i]))
                    {
                        hasLetter = true;
                        break;
                    }
                }
                if (!hasLetter) return true;

                // 纯数字或带简单单位如 "100%", "0.0", "12px", "60hz", "100ms", "60fps", "0.8x", "1.0x"
                if (Regex.IsMatch(text, @"^[\d\.\,\+\-\%\s\:\/\#\<\>\=]+(px|%|ms|hz|fps|x)?$", RegexOptions.IgnoreCase)) return true;

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

            // 用例 5: UI 调用中的排版纯符号必须放过
            cases++;
            string case5 = "class C { void M() { GUILayout.Label(\" - \"); GUILayout.Label(\" | \"); GUILayout.Button(\"X\"); } }";
            var rep5 = AuditSnippet(case5, validKeys);
            if (rep5.Issues.Count != 0)
            {
                failures.Add($"[用例 5 失败] UI 排版符号应当放过，实际报告了: {rep5.Issues[0]}");
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
}
