using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// MFP 组件规范规则码 —— 全项目单一定义。
    ///
    /// 插件内验证器 (WidgetSpecificationValidator)、无头验证器 (tools/HeadlessValidator)、
    /// 任何脚本工具都只能引用此处常量，禁止各自再抄一份字符串字面量。
    /// (历史教训: 规则码 / 正则 / Token 白名单曾被手抄成多份副本，副本漂移直接导致审计假绿。)
    /// </summary>
    public static class WidgetSpecRules
    {
        public const string Inheritance = "MFP-SPEC-001";        // 必须继承 BaseFlightWidget
        public const string RefreshTier = "MFP-SPEC-002";        // 必须显式声明刷新阶梯
        public const string SemanticTheming = "MFP-SPEC-003";    // 必须实现 ApplyTheme(ThemeConfig)
        public const string TelemetryContract = "MFP-SPEC-004";  // 必须实现 OnUpdateTelemetry(IFlightTelemetry)
        public const string SafeLifecycle = "MFP-SPEC-005";      // OnDestroy 必须 override
        public const string NoHardcodedColors = "MFP-SPEC-006";  // 禁止颜色字面量（零容忍）
        public const string NoSceneQueries = "MFP-SPEC-007";     // 禁止组件内场景查询，统一走 ProbeManager
        public const string AutoRegistration = "MFP-SPEC-008";   // 必须声明 [FlightWidget] 自动注册与预设库元数据

        /// <summary>
        /// 源码级规则条数。用于统计"已执行规则检查"次数，避免各调用点各写一个数字
        /// （历史上写死的 `* 7` 在新增 SPEC-008 之后就变成少算一条）。
        /// 新增规则时必须同步 +1。
        /// </summary>
        public const int RuleCount = 8;
    }

    /// <summary>源码级规则违规记录（插件与无头验证器共用的统一 DTO）</summary>
    public class WidgetSourceViolation
    {
        public string FileName;
        public string RuleCode;
        public string Severity;      // "ERROR" / "WARNING"
        public string Description;
        public int Line;             // 1 起行号；0 表示文件级/汇总级

        public string Location
        {
            get { return Line > 0 ? FileName + ":L" + Line : FileName; }
        }

        public override string ToString()
        {
            return "[" + Severity + "] [" + RuleCode + "] " + FileName + ": " + Description
                 + (Line > 0 ? " (L" + Line + ")" : string.Empty);
        }
    }

    /// <summary>参与审计的组件源文件</summary>
    public class WidgetSourceFile
    {
        public string Name;   // 文件名（含扩展名），用于基线与报错定位
        public string Path;   // 绝对路径
        public string Text;   // 原始源码文本
    }

    /// <summary>组件源码审计汇总报告</summary>
    public class WidgetSourceAuditReport
    {
        public int WidgetsScanned;
        public readonly List<WidgetSourceViolation> Violations = new List<WidgetSourceViolation>();

        public int ErrorCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Violations.Count; i++)
                    if (Violations[i].Severity == "ERROR") n++;
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Violations.Count; i++)
                    if (Violations[i].Severity == "WARNING") n++;
                return n;
            }
        }

        public bool IsCompliant { get { return ErrorCount == 0; } }

        /// <summary>按规则码统计命中次数（用于报表分项）</summary>
        public int CountByRule(string ruleCode)
        {
            int n = 0;
            for (int i = 0; i < Violations.Count; i++)
                if (Violations[i].RuleCode == ruleCode) n++;
            return n;
        }
    }

    /// <summary>
    /// 极简 C# 词法预处理器（注释 / 字符串字面量剥离器 + using 别名归一化）。
    ///
    /// 作用：把注释与字符串字面量的内容整体替换为空格，**完整保留换行与列位置**，
    /// 使上层正则规则只在"真代码"上匹配。这样既杜绝
    ///   - "注释里提了一句 FindObjectOfType 就被判违规"的误报，
    ///   - "字符串里写着 new Color(...) 被计入颜色字面量"的假阳性，
    /// 又能让报错行号保持准确。
    ///
    /// 覆盖：// 行注释、/* */ 块注释、"..." 普通字符串（\ 转义）、@"..." 逐字字符串（"" 转义）、
    ///      $"..." / $@"..." / @$"..." 内插字符串（**洞内代码保留**）、'c' 字符字面量。
    ///
    /// 【内插洞语义修正】旧实现把 `$"..."` 整段清空，于是
    ///   `string s = $"{FindObjectOfType&lt;Camera&gt;()}";`
    /// 这种"把违规调用写进插值洞"的写法可以整段藏起来（文件头只能把它记成"已声明的近似"）。
    /// 现在改为：洞 `{...}` 内的代码**按代码对待**（递归处理洞内嵌套字符串/字符/注释），
    /// 只清空真正的文本片段与格式说明符（`{x:0.00}` 的 `:` 之后部分，
    /// 用"洞内是否出现过 `?`"来区分三元运算符，避免把 `cond ? a : b` 误当格式串）。
    ///
    /// 【别名归一化】`using TC = ModularFlightPanel.Config.ThemeConfig;` 会把 `TC` 还原成
    /// `ThemeConfig`，因此"用别名写基类/参数类型"既不会再绕过规则，也不会再被误判为未实现。
    ///
    /// 仍未覆盖（已声明）：C# 11 原始字符串 `"""..."""`（Unity 2019 / C# 8 不可用，
    /// 且仓库内零使用）；`#if` 不做条件编译求值（保守：未激活分支同样参与审计，宁多勿少）。
    /// </summary>
    public static class CSharpSourceLinter
    {
        /// <summary>剥离注释与字符串内容；返回与输入等长、行数一致的"骨架文本"</summary>
        public static string Sanitize(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return sourceText ?? string.Empty;

            var sb = new StringBuilder(sourceText.Length);
            int i = 0;
            int n = sourceText.Length;

            while (i < n)
            {
                char c = sourceText[i];

                // ── 行注释 ──
                if (c == '/' && i + 1 < n && sourceText[i + 1] == '/')
                {
                    while (i < n && sourceText[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }

                // ── 块注释 ──
                if (c == '/' && i + 1 < n && sourceText[i + 1] == '*')
                {
                    sb.Append(' ').Append(' ');
                    i += 2;
                    while (i < n)
                    {
                        if (sourceText[i] == '*' && i + 1 < n && sourceText[i + 1] == '/')
                        {
                            sb.Append(' ').Append(' ');
                            i += 2;
                            break;
                        }
                        sb.Append(sourceText[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    continue;
                }

                // ── 字符串字面量（@ / $ 前缀各组合，先长后短）──
                if (c == '@' && i + 2 < n && sourceText[i + 1] == '$' && sourceText[i + 2] == '"')
                {
                    sb.Append("@$");
                    i = ConsumeInterpolatedString(sourceText, sb, i + 2, true);
                    continue;
                }
                if (c == '$' && i + 2 < n && sourceText[i + 1] == '@' && sourceText[i + 2] == '"')
                {
                    sb.Append("$@");
                    i = ConsumeInterpolatedString(sourceText, sb, i + 2, true);
                    continue;
                }
                if (c == '@' && i + 1 < n && sourceText[i + 1] == '"')
                {
                    sb.Append('@');
                    i = ConsumeString(sourceText, sb, i + 1, true);
                    continue;
                }
                if (c == '$' && i + 1 < n && sourceText[i + 1] == '"')
                {
                    sb.Append('$');
                    i = ConsumeInterpolatedString(sourceText, sb, i + 1, false);
                    continue;
                }
                if (c == '"')
                {
                    i = ConsumeString(sourceText, sb, i, false);
                    continue;
                }

                // ── 字符字面量 ──
                if (c == '\'')
                {
                    i = ConsumeChar(sourceText, sb, i);
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>剥离后按行切分（统一 \n），便于逐行正则与行号定位</summary>
        public static string[] SanitizeLines(string sourceText)
        {
            return Sanitize(sourceText).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>
        /// 审计专用入口：清洗 + using 别名归一化。
        /// 所有规则判定都必须走这里，否则 `using TC = ...ThemeConfig;` 这类别名会变成绕过通道。
        /// </summary>
        public static string SanitizeAndNormalize(string sourceText)
        {
            return ExpandUsingAliases(Sanitize(sourceText));
        }

        /// <summary>清洗 + 别名归一化后按行切分</summary>
        public static string[] SanitizeAndNormalizeLines(string sourceText)
        {
            return SanitizeAndNormalize(sourceText).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>`using Alias = Some.Namespace.Type;` 的别名声明</summary>
        private static readonly Regex UsingAliasRegex = new Regex(
            @"\busing\s+([A-Za-z_]\w*)\s*=\s*([A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*;",
            RegexOptions.Compiled);

        /// <summary>
        /// 把 using 别名替换回目标类型的简名（`using TC = A.B.ThemeConfig;` → 文本里的 `TC` 变成 `ThemeConfig`）。
        /// 这是"类型名归一化"的一半：另一半是规则正则允许"可选命名空间前缀"。
        /// 两者合起来的效果 —— 别名与全限定名都不再影响判定（既不漏，也不误报）。
        /// </summary>
        private static string ExpandUsingAliases(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            MatchCollection aliases = UsingAliasRegex.Matches(code);
            if (aliases.Count == 0) return code;

            string result = code;
            for (int i = 0; i < aliases.Count; i++)
            {
                string alias = aliases[i].Groups[1].Value;
                string target = aliases[i].Groups[2].Value.Replace(" ", string.Empty);
                int dot = target.LastIndexOf('.');
                string simple = dot >= 0 ? target.Substring(dot + 1) : target;
                if (simple.Length == 0) continue;
                if (string.Equals(simple, alias, StringComparison.Ordinal)) continue;
                result = Regex.Replace(result, @"\b" + Regex.Escape(alias) + @"\b", simple);
            }
            return result;
        }

        /// <summary>
        /// 内插字符串：i 指向开引号。文本片段清空，但 `{...}` 洞内的**代码保留**（那才是会执行的东西）。
        /// `{{` / `}}` 是转义花括号（文本），清空；`$@"..."` 的 `""` 是转义引号（文本）。
        /// </summary>
        private static int ConsumeInterpolatedString(string s, StringBuilder sb, int i, bool verbatim)
        {
            sb.Append(' ');   // 开引号
            i++;
            while (i < s.Length)
            {
                char d = s[i];

                if (d == '{')
                {
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        sb.Append(' ').Append(' ');   // 转义花括号 = 文本
                        i += 2;
                        continue;
                    }
                    i = ConsumeHole(s, sb, i);
                    continue;
                }

                if (d == '}')
                {
                    if (i + 1 < s.Length && s[i + 1] == '}')
                    {
                        sb.Append(' ').Append(' ');
                        i += 2;
                        continue;
                    }
                    sb.Append(' ');
                    i++;
                    continue;
                }

                if (verbatim)
                {
                    if (d == '"' && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append(' ').Append(' ');
                        i += 2;
                        continue;
                    }
                    if (d == '"')
                    {
                        sb.Append(' ');
                        return i + 1;
                    }
                }
                else
                {
                    if (d == '\\' && i + 1 < s.Length)
                    {
                        sb.Append(' ');
                        sb.Append(s[i + 1] == '\n' ? '\n' : ' ');
                        i += 2;
                        continue;
                    }
                    if (d == '"')
                    {
                        sb.Append(' ');
                        return i + 1;
                    }
                    if (d == '\n') return i;   // 未闭合（异常写法）：安全退出
                }

                sb.Append(d == '\n' ? '\n' : ' ');
                i++;
            }
            return i;
        }

        /// <summary>
        /// 内插洞：i 指向 `{`。洞内按**代码**保留（含嵌套字符串/字符/注释/嵌套插值），
        /// 只清空 `{x:0.00}` 的格式说明符部分；用"洞内是否出现过 `?`"区分三元运算符的 `:`。
        /// 返回越过配对 `}` 的位置。
        /// </summary>
        private static int ConsumeHole(string s, StringBuilder sb, int i)
        {
            sb.Append('{');   // 保留花括号，使上层正则看到完整语法形状
            i++;
            int depth = 1;
            bool sawQuestion = false;

            while (i < s.Length)
            {
                char c = s[i];

                if (c == '{') { sb.Append('{'); i++; depth++; continue; }
                if (c == '}')
                {
                    depth--;
                    sb.Append('}');
                    i++;
                    if (depth == 0) return i;
                    continue;
                }

                if (c == ':' && depth == 1 && !sawQuestion)
                {
                    // 格式说明符（对齐/数字格式）：整段清空到本洞收尾
                    while (i < s.Length && s[i] != '}')
                    {
                        sb.Append(s[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    continue;
                }

                if (c == '?' && depth == 1) sawQuestion = true;

                if (c == '"') { i = ConsumeString(s, sb, i, false); continue; }
                if (c == '\'') { i = ConsumeChar(s, sb, i); continue; }
                if (c == '@' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append('@'); i = ConsumeString(s, sb, i + 1, true); continue; }
                if (c == '@' && i + 2 < s.Length && s[i + 1] == '$' && s[i + 2] == '"') { sb.Append("@$"); i = ConsumeInterpolatedString(s, sb, i + 2, true); continue; }
                if (c == '$' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append('$'); i = ConsumeInterpolatedString(s, sb, i + 1, false); continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    sb.Append(' ').Append(' ');
                    i += 2;
                    while (i < s.Length)
                    {
                        if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/') { sb.Append(' ').Append(' '); i += 2; break; }
                        sb.Append(s[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    continue;
                }

                sb.Append(c == '\n' ? '\n' : c);
                i++;
            }
            return i;
        }

        /// <summary>i 指向开引号；清空字符串内容，返回越过收尾引号的位置</summary>
        private static int ConsumeString(string s, StringBuilder sb, int i, bool verbatim)
        {
            sb.Append(' ');   // 开引号
            i++;
            while (i < s.Length)
            {
                char d = s[i];
                if (verbatim)
                {
                    // 逐字字符串："" 是转义双引号，单个 " 结束
                    if (d == '"' && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append(' ').Append(' ');
                        i += 2;
                        continue;
                    }
                    if (d == '"')
                    {
                        sb.Append(' ');
                        return i + 1;
                    }
                }
                else
                {
                    if (d == '\\' && i + 1 < s.Length)
                    {
                        sb.Append(' ');
                        sb.Append(s[i + 1] == '\n' ? '\n' : ' ');
                        i += 2;
                        continue;
                    }
                    if (d == '"')
                    {
                        sb.Append(' ');
                        return i + 1;
                    }
                    if (d == '\n') return i;   // 未闭合（异常写法）：安全退出
                }

                sb.Append(d == '\n' ? '\n' : ' ');
                i++;
            }
            return i;
        }

        /// <summary>i 指向开单引号；清空字符字面量，返回越过收尾单引号的位置</summary>
        private static int ConsumeChar(string s, StringBuilder sb, int i)
        {
            sb.Append(' ');
            i++;
            while (i < s.Length)
            {
                char d = s[i];
                if (d == '\\' && i + 1 < s.Length)
                {
                    sb.Append(' ');
                    sb.Append(s[i + 1] == '\n' ? '\n' : ' ');
                    i += 2;
                    continue;
                }
                if (d == '\'')
                {
                    sb.Append(' ');
                    return i + 1;
                }
                if (d == '\n') return i;       // 未闭合：安全退出
                sb.Append(' ');
                i++;
            }
            return i;
        }

        /// <summary>
        /// 最近一次 SelfTest 实际执行的对照用例数。
        /// 门禁打印"14 条边界用例"这种写死数字，必然随代码漂移变成假信息（加/删用例都不影响它），
        /// 因此统一改为回传真实条数。
        /// </summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// 词法器自检（无头验证器的常驻检查项）。返回失败描述列表，空列表 = 全部通过。
        /// 每条用例都对应一个真实风险：注释误报、字符串假阳性、转义/逐字/字符字面量边界、
        /// 内插洞语义、using 别名归一化。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            Action<string, string, bool> expect = (name, snippet, shouldSurvive) =>
            {
                cases++;
                string sanitized = Sanitize(snippet);
                bool survived = sanitized.IndexOf("FindObjectOfType", StringComparison.Ordinal) >= 0;
                if (survived != shouldSurvive)
                {
                    failures.Add(name + " -> " + (shouldSurvive
                        ? "真实调用被误清空（漏报）: " + sanitized
                        : "注释/字符串未被清空（误报）: " + sanitized));
                }
            };

            expect("行注释提及", "// 这里不要调用 FindObjectOfType\n", false);
            expect("行尾注释提及", "int a = 1; // FindObjectOfType 已弃用\n", false);
            expect("块注释提及", "/* FindObjectOfType\n   仍然在注释里 */\nint a;\n", false);
            expect("字符串提及", "string s = \"FindObjectOfType\";\n", false);
            expect("逐字字符串提及", "string p = @\"C:\\FindObjectOfType\\x\";\n", false);
            expect("真实调用保留", "var x = FindObjectOfType<Vessel>();\n", true);
            expect("同行调用+注释", "var x = FindObjectOfType<Vessel>(); // 仅这一次\n", true);
            expect("转义引号", "string s = \"a\\\" FindObjectOfType\";\n", false);
            expect("字符字面量后", "char q = '\\''; var x = FindObjectOfType<Vessel>();\n", true);

            // ── 内插洞：洞内是"会真实执行的代码"，必须保留（旧实现整段清空 = 真实藏违规通道）──
            expect("内插洞内的真实调用必须保留", "string s = $\"a{FindObjectOfType<b>()}c\";\n", true);
            expect("内插洞内的三元分支必须保留", "string s = $\"{c ? FindObjectOfType<Camera>() : null}\";\n", true);
            expect("内插洞外的文本必须清空", "string s = $\"{0} FindObjectOfType<Camera>()\";\n", false);
            expect("格式说明符内容必须清空", "string s = $\"{n:FindObjectOfType}\";\n", false);
            expect("转义花括号是文本必须清空", "string s = $\"{{FindObjectOfType}}\";\n", false);
            expect("洞内嵌套花括号不破坏配对", "string s = $\"{ new[] { 1, 2 }.Length } FindObjectOfType<Camera>()\";\n", false);
            expect("洞内注释必须清空", "string s = $\"{1 /* FindObjectOfType */}\";\n", false);

            // ── using 别名归一化：别名既不放过也不误报 ──
            Action<string, string, string> expectToken = (name, snippet, token) =>
            {
                cases++;
                string normalized = SanitizeAndNormalize(snippet);
                if (normalized.IndexOf(token, StringComparison.Ordinal) < 0)
                    failures.Add(name + " -> 别名未归一化（规则会漏判）: " + normalized.Replace("\n", "\\n"));
            };
            expectToken("类型别名归一化", "using TC = ModularFlightPanel.Config.ThemeConfig;\nTC t;\n", "ThemeConfig");
            expectToken("颜色别名归一化", "using C = UnityEngine.Color;\nvar c = C.red;\n", "Color.red");
            expectToken("场景查询别名归一化", "using GO = UnityEngine.GameObject;\nGO.Find(\"x\");\n", "GameObject.Find");
            cases++;
            if (SanitizeAndNormalize("class A { }").IndexOf("class A", StringComparison.Ordinal) < 0)
                failures.Add("无别名时归一化不得改变代码文本");

            // 颜色字面量的假阳性 / 漏报边界（复核 SPEC-006 正则与计数口径）
            Action<string, int> expectColorCount = (snippet, expected) =>
            {
                cases++;
                int actual = WidgetColorLiteralAudit.CountOccurrences(snippet);
                if (actual != expected)
                    failures.Add("SPEC-006 计数不符（期望 " + expected + " 处，实测 " + actual + " 处）: " + snippet.Replace("\n", "\\n"));
            };
            expectColorCount("// new Color(1f,0f,0f,1f)\n", 0);
            expectColorCount("string s = \"new Color(1f,0f,0f,1f)\";\n", 0);
            expectColorCount("var c = new Color(1f, 0f, 0f, 1f);\n", 1);
            expectColorCount("var c = Color.clear;\n", 0);
            expectColorCount("Color[] a = new Color[16];\n", 0);
            expectColorCount("var c = $\"{new Color(1f,0f,0f,1f)}\";\n", 1);
            expectColorCount("var a = new Color(1,0,0,1); var b = new Color(0,1,0,1);\n", 2);
            expectColorCount("var c = Color.HSVToRGB(1f,1f,1f);\n", 1);
            expectColorCount("ColorUtility.TryParseHtmlString(\"#F00\", out var c);\n", 1);
            expectColorCount("var c = (Color)new Vector4(1f,0f,0f,1f);\n", 1);
            expectColorCount("using C = UnityEngine.Color;\nvar c = C.red;\n", 1);
            expectColorCount("using static UnityEngine.Color;\nvar c = white;\n", 1);
            expectColorCount("var c = Color.Lerp(a, b, 0.5f);\n", 0);

            // 行数一致性：清洗不得改变行数（报错行号可信的前提）
            string sample = "line1\n/* a\nb */\n\"s\n\"\nline5\n";
            cases++;
            if (Sanitize(sample).Split('\n').Length != sample.Split('\n').Length)
                failures.Add("清洗后行数与原文不一致，报错行号将不可信");
            string holeSample = "a\n$\"{x}\"\n/*\nb\n*/\n";
            cases++;
            if (Sanitize(holeSample).Split('\n').Length != holeSample.Split('\n').Length)
                failures.Add("含内插洞的清洗改变了行数，报错行号将不可信");

            LastSelfTestCaseCount = cases;
            return failures;
        }
    }

    /// <summary>
    /// 组件源码规范审计内核 (WidgetSourceAudit) —— MFP-SPEC-001..007 的唯一定义处。
    ///
    /// 设计红线：
    ///   1. 规则、正则、"哪些文件算组件"的扫描范围只允许存在这一份实现；
    ///      插件内验证器与无头验证器都必须调用本类，禁止各自复制（副本必然漂移）。
    ///   2. 只在"词法清洗后的骨架文本"上匹配，注释与字符串一律不参与判定。
    ///   3. 不写死任何绝对路径：仓库根通过程序集位置逐级上溯推导。
    /// </summary>
    public static class WidgetSourceAudit
    {
        /// <summary>
        /// 已识别的组件基类种子。**继承契约正则与"哪些文件算组件"的发现逻辑共用这同一份名单**，
        /// 历史上这两处曾各写一份（一处含 BaseNavballSphereWidget、一处不含），直接导致
        /// "UI 根目录下继承抽象姿态球基类的组件彻底不被审计"这种静默漏检。
        /// </summary>
        internal static readonly string[] WidgetBaseTypeNames = { "BaseFlightWidget", "BaseNavballSphereWidget" };

        // ── 规则 005：OnDestroy 声明 ──
        private static readonly Regex OnDestroyDeclRegex = new Regex(@"\bvoid\s+OnDestroy\s*\(", RegexOptions.Compiled);
        private static readonly Regex OverrideKeywordRegex = new Regex(@"\boverride\b", RegexOptions.Compiled);
        private static readonly Regex BaseOnDestroyCallRegex = new Regex(@"\bbase\s*\.\s*OnDestroy\s*\(", RegexOptions.Compiled);

        // ── 规则 007：Unity 场景/全局查询 API 家族（组件内一律禁止，统一走 ProbeManager）──
        // 命名推导（务必按此逐项展开，别再写成 Find(?:Object|Objects|First|Any)Object... 那种
        // 需要"Object"出现两次的写法 —— 它匹配不上 FindObjectOfType，门禁会静默失效）：
        //   Find + [First|Any]? + Object[s]? + [OfType|ByType] + [All]?
        // 覆盖清单（每一项都必须保留，历史上漏掉 GameObjectWithTag / Camera.main 就是靠"看清单"发现的）：
        //   FindObjectOfType / FindObjectsOfType / FindObjectsOfTypeAll /
        //   FindFirstObjectByType / FindAnyObjectByType / FindObjectsByType(/All)
        //   GameObject.Find / FindWithTag / FindGameObjectWithTag / FindGameObjectsWithTag
        //   Camera.main / Camera.allCameras     —— 按标签查主摄像机，等价场景查询
        //   *.GetRootGameObjects()              —— 遍历场景全部根，全场景分配
        //   Resources.FindObjectsOfTypeAll      —— 全资源扫描
        //
        // 【匹配对象是整份清洗后的骨架文本，而不是逐行】—— 因此
        //   var c = FindObjectOfType
        //           <Camera>();
        // 这种换行写法也拦得住（旧实现逐行匹配，"必须同行才有 ( 或 <" 是真实漏检面）。
        //
        // 有意不纳入 transform.Find("Child")：它是"已知父节点下的子节点查找"，
        // 不是场景级查询，ProbeManager 也不是它的替代方案，纳管它只会制造误报。
        private static readonly Regex SceneQueryRegex = new Regex(
            @"\bFind(?:First|Any)?Object(?:s)?(?:OfType|ByType)(?:All)?\s*[<(]"
            + @"|\bGameObject\s*\.\s*Find(?:GameObject)?(?:WithTag|GameObjectsWithTag)?\s*\("
            + @"|\bCamera\s*\.\s*(?:main|allCameras)\b"
            + @"|\bGetRootGameObjects\s*\("
            + @"|\bResources\s*\.\s*FindObjectsOfTypeAll\b",
            RegexOptions.Compiled);

        // ── 场景查询的"使能写法"：using static 之后裸名 Find("x") 就绕过了上面那条正则 ──
        // 与其去做符号解析，不如直接封掉使能条件本身（一条正则即可，零误报风险）。
        private static readonly Regex StaticSceneImportRegex = new Regex(
            @"\busing\s+static\s+(?:[A-Za-z_]\w*\s*\.\s*)*(?:GameObject|Object|Resources)\s*;",
            RegexOptions.Compiled);

        // ── 类型名一律写成"可选命名空间前缀 + 简名" ──
        // 这样 ModularFlightPanel.UI.ThemeConfig 这种全限定写法不再被误判为"未实现"；
        // using 别名（using TC = ...ThemeConfig;）由 CSharpSourceLinter.SanitizeAndNormalize
        // 在清洗阶段归一化回简名，两处配合即可同时消掉"别名绕过"和"全限定误报"。
        private static readonly Regex InheritDeclRegex = new Regex(
            @"\bclass\s+[A-Za-z_]\w*\s*(?:<[^<>]*>)?\s*:\s*(?:[A-Za-z_]\w*\s*\.\s*)*[A-Za-z_]\w*",
            RegexOptions.Compiled);
        private static readonly Regex RefreshTierDeclRegex = new Regex(
            @"\boverride\s+(?:[A-Za-z_]\w*\s*\.\s*)*WidgetRefreshTier\s+RefreshTier\b",
            RegexOptions.Compiled);
        private static readonly Regex RefreshTierValueRegex = new Regex(
            @"\b(?:WidgetRefreshTier\s*\.\s*)?(?:Critical|Standard|Relaxed|UltraLow)\b",
            RegexOptions.Compiled);
        private static readonly Regex RefreshTierCastRegex = new Regex(
            @"\(\s*(?:[A-Za-z_]\w*\s*\.\s*)*WidgetRefreshTier\s*\)",
            RegexOptions.Compiled);
        private static readonly Regex ApplyThemeRegex = new Regex(
            @"\bpublic\s+override\s+void\s+ApplyTheme\s*\(\s*(?:[A-Za-z_]\w*\s*\.\s*)*ThemeConfig\b",
            RegexOptions.Compiled);
        private static readonly Regex AnyApplyThemeDeclRegex = new Regex(
            @"\bvoid\s+ApplyTheme\s*\(",
            RegexOptions.Compiled);
        private static readonly Regex UpdateTelemetryRegex = new Regex(
            @"\bpublic\s+override\s+void\s+OnUpdateTelemetry\s*\(\s*(?:[A-Za-z_]\w*\s*\.\s*)*IFlightTelemetry\b",
            RegexOptions.Compiled);
        private static readonly Regex FlightWidgetAttrRegex = new Regex(@"\[\s*FlightWidget\s*\(", RegexOptions.Compiled);
        private static readonly Regex AbstractClassRegex = new Regex(@"\babstract\s+class\b", RegexOptions.Compiled);

        /// <summary>
        /// 类声明解析器：抓 `class Name[&lt;T&gt;] [: Base, IThing]`，bases 捕获到 `{` 之前。
        /// 只认"紧跟着 { 的类头"，因此 `where T : BaseFlightWidget` 这种泛型约束
        /// **不会**被当成继承（旧实现用文件级 `:\s*BaseFlightWidget` 判定，
        /// 一句泛型约束就能把"没有继承基类"的类洗成合规）。
        /// </summary>
        private static readonly Regex ClassDeclRegex = new Regex(
            @"\bclass\s+([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*(?::\s*(?<bases>[^;{]+?))?\s*(?=\{)",
            RegexOptions.Compiled);

        /// <summary>
        /// 从程序集位置逐级上溯定位仓库根（其下存在 src/ModularFlightPanel/UI/Widgets）。
        /// 开发机返回真实根目录；发布环境（只有 DLL）返回 null，调用方据此降级为反射级审计。
        /// </summary>
        public static string ResolveRepositoryRoot()
        {
            try
            {
                string asmPath = typeof(WidgetSourceAudit).Assembly.Location;
                if (string.IsNullOrEmpty(asmPath)) return null;

                var dir = new DirectoryInfo(Path.GetDirectoryName(asmPath) ?? string.Empty);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    string probe = Path.Combine(dir.FullName, "src", "ModularFlightPanel", "UI", "Widgets");
                    if (Directory.Exists(probe)) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch
            {
                // 反射 / IO 受限：静默降级
            }
            return null;
        }

        /// <summary>UI 目录绝对路径</summary>
        public static string GetUiDirectory(string repositoryRoot)
        {
            if (string.IsNullOrEmpty(repositoryRoot)) return null;
            return Path.Combine(repositoryRoot, "src", "ModularFlightPanel", "UI");
        }

        /// <summary>
        /// 组件源文件数量的冻结下限（棘轮：只允许上升，不允许下降）。
        ///
        /// 为什么必须存在：门禁最省事的作弊方式不是写花式代码，而是**让审计扫不到东西**
        /// —— repoRoot 错位、目录改名、把组件挪到 UI 之外，都会让报告变成
        /// "扫描 0 个组件 / 0 处违规 / 完全合规"。空集与真绿在报告上完全同形，
        /// 因此必须用一个人工冻结的数量下限把它们区分开：**低于下限 = 门禁失效 = 硬失败**。
        ///
        /// 新增组件后请同步上调本值（改动会出现在 diff 里，等于强制一次显式确认）。
        /// </summary>
        public const int ExpectedComponentFloor = 44;

        /// <summary>
        /// 校验"发现层是否真的扫到了东西"。返回 null 表示正常，否则返回失败原因。
        /// 调用方（无头验证器 [6/9]、插件内验证器）必须把非 null 结果当成 ERROR 计入总数，
        /// 绝不允许"扫不到就跳过"。
        /// </summary>
        public static string CheckDiscoveryFloor(string repositoryRoot, int discoveredCount)
        {
            if (string.IsNullOrEmpty(repositoryRoot))
            {
                return "无法定位仓库根目录（src/ModularFlightPanel/UI/Widgets 不存在）：源码级规范审计完全未执行，"
                     + "本次报告的\"合规\"不成立。请在仓库根目录下运行，或用 --repo 指定正确路径。";
            }

            string uiDir = GetUiDirectory(repositoryRoot);
            if (string.IsNullOrEmpty(uiDir) || !Directory.Exists(uiDir))
            {
                return "仓库根目录下找不到 " + (uiDir ?? "src/ModularFlightPanel/UI") + "：源码级规范审计完全未执行。";
            }

            if (discoveredCount < ExpectedComponentFloor)
            {
                return "发现层退化：只扫到 " + discoveredCount + " 个组件，低于冻结下限 " + ExpectedComponentFloor
                     + " 个。空集/少量扫描与\"真绿\"在报告上无法区分，因此按门禁失效处理。"
                     + "若确实删除了组件，请同步下调 WidgetSourceAudit.ExpectedComponentFloor。";
            }

            return null;
        }

        /// <summary>
        /// 组件源文件发现（唯一定义），采用**继承闭包不动点**判定，而不是单条文本正则：
        ///   - src/ModularFlightPanel/UI/Widgets/**/*.cs → 全部按组件对待（与既有约定一致）；
        ///   - UI 其余 *.cs → 只有当它声明的某个类的基类**落在组件继承闭包内**才算组件。
        ///
        /// 用不动点迭代的好处：`class A : B`、而 B 才是 widget 的"间接派生"文件同样会被发现；
        /// 而 `class Sneaky : MonoBehaviour { void M&lt;T&gt;() where T : BaseFlightWidget {} }`
        /// 这种"靠泛型约束蹭正则"的写法再也不可能被当成组件（旧实现会漏）。
        ///
        /// 目录判定同时修掉了 `Widgets` 前缀陷阱（旧实现用 StartsWith，`UI/WidgetsLegacy` 会被误吞）。
        /// </summary>
        public static List<WidgetSourceFile> DiscoverComponentFiles(string repositoryRoot)
        {
            return DiscoverComponentFiles(repositoryRoot, null);
        }

        /// <summary>带诊断输出的发现入口：读取失败的文件不再抛异常炸掉整个门禁，而是记入 diagnostics 并跳过。</summary>
        public static List<WidgetSourceFile> DiscoverComponentFiles(string repositoryRoot, List<string> diagnostics)
        {
            var result = new List<WidgetSourceFile>();
            string uiDir = GetUiDirectory(repositoryRoot);
            if (string.IsNullOrEmpty(uiDir) || !Directory.Exists(uiDir)) return result;

            string widgetsDir = Path.Combine(uiDir, "Widgets").TrimEnd('\\', '/');

            var all = Directory.GetFiles(uiDir, "*.cs", SearchOption.AllDirectories);
            Array.Sort(all, StringComparer.OrdinalIgnoreCase);

            var loaded = new List<WidgetSourceFile>();
            var decls = new List<KeyValuePair<string, string[]>>[all.Length];
            var inWidgetsDir = new bool[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                WidgetSourceFile file;
                try
                {
                    file = Load(all[i]);
                }
                catch (Exception ex)
                {
                    // 不抛：单个文件不可读不应该让整条门禁崩掉，但也绝不能静默 —— 记入诊断，
                    // 由调用方按 ERROR 处理（"未覆盖该文件"本身就是风险）。
                    if (diagnostics != null) diagnostics.Add("组件源码读取失败（该文件未被任何规则覆盖）: " + all[i] + " -> " + ex.Message);
                    continue;
                }

                loaded.Add(file);
                string dir = Path.GetDirectoryName(file.Path) ?? string.Empty;
                inWidgetsDir[loaded.Count - 1] =
                    string.Equals(dir, widgetsDir, StringComparison.OrdinalIgnoreCase)
                    || dir.StartsWith(widgetsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || dir.StartsWith(widgetsDir + "/", StringComparison.OrdinalIgnoreCase);

                decls[loaded.Count - 1] = CollectClassDecls(CSharpSourceLinter.SanitizeAndNormalize(file.Text));
            }

            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < WidgetBaseTypeNames.Length; i++) closure.Add(WidgetBaseTypeNames[i]);

            var included = new bool[loaded.Count];
            for (int i = 0; i < loaded.Count; i++) included[i] = inWidgetsDir[i];

            // 不动点迭代：任一类的基类落进闭包 → 该类进闭包、其所在文件进组件集。
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 32)
            {
                changed = false;
                for (int i = 0; i < loaded.Count; i++)
                {
                    var fileDecls = decls[i];
                    if (fileDecls == null) continue;
                    for (int d = 0; d < fileDecls.Count; d++)
                    {
                        if (closure.Contains(fileDecls[d].Key)) continue;
                        var bases = fileDecls[d].Value;
                        for (int b = 0; b < bases.Length; b++)
                        {
                            if (!closure.Contains(bases[b])) continue;
                            closure.Add(fileDecls[d].Key);
                            included[i] = true;
                            changed = true;
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < loaded.Count; i++)
            {
                if (included[i]) result.Add(loaded[i]);
            }

            return result;
        }

        private static WidgetSourceFile Load(string path, string knownText = null)
        {
            return new WidgetSourceFile
            {
                Name = Path.GetFileName(path),
                Path = path,
                Text = knownText ?? File.ReadAllText(path)
            };
        }

        /// <summary>
        /// 解析清洗后文本里的全部类声明，得到 (类名, 基类简名数组)。
        /// 泛型实参与命名空间前缀都会被剥掉，只留简名，便于与继承闭包比对。
        /// </summary>
        private static List<KeyValuePair<string, string[]>> CollectClassDecls(string code)
        {
            var list = new List<KeyValuePair<string, string[]>>();
            foreach (Match m in ClassDeclRegex.Matches(code))
            {
                var bases = new List<string>();
                Group g = m.Groups["bases"];
                if (g.Success)
                {
                    string raw = g.Value;
                    int depth = 0;
                    int start = 0;
                    for (int i = 0; i <= raw.Length; i++)
                    {
                        char c = i < raw.Length ? raw[i] : ',';
                        if (c == '<') depth++;
                        else if (c == '>') { if (depth > 0) depth--; }
                        else if (c == ',' && depth == 0)
                        {
                            string piece = raw.Substring(start, i - start);
                            start = i + 1;
                            int lt = piece.IndexOf('<');
                            if (lt >= 0) piece = piece.Substring(0, lt);
                            piece = piece.Trim();
                            int dot = piece.LastIndexOf('.');
                            if (dot >= 0) piece = piece.Substring(dot + 1).Trim();
                            if (piece.Length > 0) bases.Add(piece);
                        }
                    }
                }
                list.Add(new KeyValuePair<string, string[]>(m.Groups[1].Value, bases.ToArray()));
            }
            return list;
        }

        /// <summary>
        /// 由一批组件源文件推导"组件继承闭包"：种子 = WidgetBaseTypeNames，反复迭代直到无新增。
        /// SPEC-001 用它与 CollectClassDecls 做**类级**判定，因此
        /// "间接派生的独立文件"不再误报、"泛型约束蹭正则"不再漏报。
        /// </summary>
        private static HashSet<string> BuildWidgetClosure(IEnumerable<WidgetSourceFile> files)
        {
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < WidgetBaseTypeNames.Length; i++) closure.Add(WidgetBaseTypeNames[i]);

            var allDecls = new List<KeyValuePair<string, string[]>>();
            foreach (var f in files)
            {
                allDecls.AddRange(CollectClassDecls(CSharpSourceLinter.SanitizeAndNormalize(f.Text)));
            }

            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 32)
            {
                changed = false;
                for (int i = 0; i < allDecls.Count; i++)
                {
                    if (closure.Contains(allDecls[i].Key)) continue;
                    var bases = allDecls[i].Value;
                    for (int b = 0; b < bases.Length; b++)
                    {
                        if (!closure.Contains(bases[b])) continue;
                        closure.Add(allDecls[i].Key);
                        changed = true;
                        break;
                    }
                }
            }
            return closure;
        }

        /// <summary>扫描一批组件源文件，产出统一报告（含 SPEC-006 基线债务汇总）</summary>
        public static WidgetSourceAuditReport Scan(IEnumerable<WidgetSourceFile> files)
        {
            var report = new WidgetSourceAuditReport();
            int pendingDebtFiles = 0;
            int pendingDebtOccurrences = 0;

            // 第一遍：由整批文件推导组件继承闭包（SPEC-001 的类级判定依据）。
            // 之所以要两遍：`class A : B` 只有当 B 也是组件时才成立，而 B 可能声明在另一个文件里。
            var list = new List<WidgetSourceFile>();
            foreach (var f in files) list.Add(f);
            var closure = BuildWidgetClosure(list);

            foreach (var file in list)
            {
                report.WidgetsScanned++;
                ScanOne(file, report, closure, ref pendingDebtFiles, ref pendingDebtOccurrences);
            }

            if (pendingDebtFiles > 0)
            {
                report.Violations.Add(new WidgetSourceViolation
                {
                    FileName = pendingDebtFiles + " 个组件",
                    RuleCode = WidgetSpecRules.NoHardcodedColors,
                    Severity = "WARNING",
                    Line = 0,
                    Description = "仍有 " + pendingDebtOccurrences + " 处颜色字面量未迁入 WidgetStyleManager 语义 API。"
                                + "迁移完成后必须下调 WidgetColorLiteralAudit 基线（基线只允许下降，且由代码强制）。"
                });
            }

            return report;
        }

        /// <summary>
        /// 规则自检（无头验证器常驻检查项）。返回失败描述列表，空列表 = 全部通过。
        ///
        /// 为什么必须有：规则正则一旦写错（例如把命名结构写成需要 "Object" 出现两次，
        /// 于是 FindObjectOfType 根本匹配不上），门禁会**永远静默通过** —— 看着全绿，实际什么都没测。
        /// 因此每条规则都要有"必须命中"与"必须放过"两类对照用例，且能端到端跑通 Scan()。
        /// </summary>
        /// <summary>最近一次 SelfTest 实际执行的对照用例数（供门禁打印真实条数，避免写死数字漂移）</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            Action<bool, string> check = (ok, message) =>
            {
                cases++;
                if (!ok) failures.Add(message);
            };

            // ── (1) SPEC-007 必须命中的写法：Unity 场景查询 API 家族全集 ──
            // 自检清单本身曾经漏掉 GameObjectWithTag / Camera.main / GetRootGameObjects / 换行写法，
            // 而"自检清单漏项"恰恰是最危险的失效：门禁永远静默通过，看着全绿其实没测。
            // 因此下面每一项都对应一次真实漏检回归，只允许增加、不允许删除。
            string[] sceneQueries =
            {
                @"var a = FindObjectOfType<Camera>();",
                @"var b = FindObjectsOfType<Camera>();",
                @"var c = FindObjectsOfTypeAll(typeof(Camera));",
                @"var d = FindFirstObjectByType<Camera>();",
                @"var e = FindAnyObjectByType<Camera>();",
                @"var f = FindObjectsByType<Camera>(FindObjectsSortMode.None);",
                @"var g = GameObject.Find(""HUD"");",
                @"var h = GameObject.FindWithTag(""MainCamera"");",
                @"var i = UnityEngine.Object.FindObjectOfType<Camera>();",
                @"var j = GameObject.FindGameObjectWithTag(""Player"");",
                @"var k = GameObject.FindGameObjectsWithTag(""Probe"");",
                @"var l = Camera.main;",
                @"var m = Camera.allCameras;",
                @"var n = SceneManager.GetActiveScene().GetRootGameObjects();",
                @"var o = Resources.FindObjectsOfTypeAll<Camera>();",
                "var p = FindObjectOfType\n        <Camera>();",
                @"var q = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);"
            };
            for (int i = 0; i < sceneQueries.Length; i++)
                check(SceneQueryRegex.IsMatch(sceneQueries[i]), "SPEC-007 漏检（门禁形同虚设）: " + Flatten(sceneQueries[i]));

            // using static 是"裸名绕过"的使能写法（using static GameObject; 之后 Find("x") 就查不到了），
            // 与其做符号解析，不如直接封掉使能条件本身。
            string[] staticImports =
            {
                @"using static UnityEngine.GameObject;",
                @"using static UnityEngine.Object;",
                @"using static UnityEngine.Resources;"
            };
            for (int i = 0; i < staticImports.Length; i++)
                check(StaticSceneImportRegex.IsMatch(staticImports[i]), "SPEC-007 漏检（using static 绕过通道未封堵）: " + staticImports[i]);

            // ── (2) SPEC-007 必须放过的写法（合法调用 / 注释 / 字符串 / 明确豁免项）──
            string[] notQueries =
            {
                @"var a = ProbeManager.Instance.ResetSceneSearchGate();",
                @"var b = probe.HasVessel;",
                @"// FindObjectOfType<Camera>() 已废弃",
                @"string s = ""FindObjectOfType"";",
                @"var c = Probe.FindObjectsByTypeSafe<Camera>();",
                @"var d = transform.Find(""Child"");"
            };
            for (int i = 0; i < notQueries.Length; i++)
                check(!SceneQueryRegex.IsMatch(CSharpSourceLinter.Sanitize(notQueries[i])),
                      "SPEC-007 误报（合法调用 / 注释 / 字符串被当成场景查询）: " + Flatten(notQueries[i]));

            // ── (3) 端到端：合成组件源码跑完整 Scan() ──
            string compliant = BuildSyntheticWidget(null);

            var okReport = Scan(new[] { MakeFile("FakeOk.cs", compliant) });
            check(okReport.ErrorCount == 0, "合规合成组件被误判: " + okReport.ErrorCount + " 处 -> " + Describe(okReport));

            var sceneReport = Scan(new[] { MakeFile("FakeScene.cs", BuildSyntheticWidget("        private void Q() { var c = FindObjectOfType<Camera>(); }")) });
            check(sceneReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 端到端失效: 真实调用未被拦下 -> " + Describe(sceneReport));

            var commentReport = Scan(new[] { MakeFile("FakeComment.cs", BuildSyntheticWidget("        // FindObjectOfType<Camera> 只出现在注释里")) });
            check(commentReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 0, "SPEC-007 端到端误报: 注释提及被当成违规");

            var holeReport = Scan(new[] { MakeFile("Hole.cs", BuildSyntheticWidget("        private string Q() { return $\"{FindObjectOfType<Camera>()}\"; }")) });
            check(holeReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 漏检: 写进内插洞的场景查询未被拦下（洞内是真实代码）");

            var holeOkReport = Scan(new[] { MakeFile("HoleOk.cs", BuildSyntheticWidget("        private string Q() { return $\"x{1 + 2}y\"; }")) });
            check(holeOkReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 0, "SPEC-007 误报: 普通内插洞被当成场景查询");

            string staticSrc = BuildSyntheticWidget("        private void Q() { var x = Find(\"HUD\"); }")
                               .Replace("using System;", "using System;\nusing static UnityEngine.GameObject;");
            var staticReport = Scan(new[] { MakeFile("StaticImport.cs", staticSrc) });
            check(staticReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 漏检: using static 之后的裸名 Find(...) 未被拦下");

            var destroyReport = Scan(new[] { MakeFile("FakeDestroy.cs", compliant.Replace("protected override void OnDestroy()", "private void OnDestroy()")) });
            check(destroyReport.CountByRule(WidgetSpecRules.SafeLifecycle) == 1, "SPEC-005 端到端失效: private void OnDestroy() 未被拦下 -> " + Describe(destroyReport));

            var noBaseReport = Scan(new[] { MakeFile("NoBaseCall.cs", compliant.Replace("base.OnDestroy();", string.Empty)) });
            check(noBaseReport.CountByRule(WidgetSpecRules.SafeLifecycle) == 1, "SPEC-005 漏检: override 了 OnDestroy 但没调 base.OnDestroy() 未被拦下");

            string multiDestroy = compliant.Replace(
                "        protected override void OnDestroy() { base.OnDestroy(); }\n",
                "        protected override void OnDestroy() { base.OnDestroy(); }\n"
                + "\n        protected class Helper : MonoBehaviour\n        {\n            private void OnDestroy() { }\n        }\n");
            var multiReport = Scan(new[] { MakeFile("MultiDestroy.cs", multiDestroy) });
            check(multiReport.CountByRule(WidgetSpecRules.SafeLifecycle) == 1, "SPEC-005 漏检: 只检查了第一个 OnDestroy（后续类里的坏 OnDestroy 未被拦下）");

            var tierReport = Scan(new[] { MakeFile("FakeTier.cs", compliant.Replace("public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;", string.Empty)) });
            check(tierReport.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 端到端失效: 缺失 RefreshTier 未被拦下");

            var tierCastReport = Scan(new[] { MakeFile("TierCast.cs", compliant.Replace("=> WidgetRefreshTier.Standard", "=> (WidgetRefreshTier)999")) });
            check(tierCastReport.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 漏检: 强制转换 (WidgetRefreshTier)999 伪造阶梯未被拦下");

            string tierBlock = compliant.Replace(
                "public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;",
                "public override WidgetRefreshTier RefreshTier { get { return WidgetRefreshTier.Relaxed; } }");
            var tierBlockReport = Scan(new[] { MakeFile("TierBlock.cs", tierBlock) });
            check(tierBlockReport.CountByRule(WidgetSpecRules.RefreshTier) == 0, "SPEC-002 误报: 块状 get 返回合法枚举成员被判违规");

            var colorReport = Scan(new[] { MakeFile("FakeColor.cs", BuildSyntheticWidget("        private UnityEngine.Color _c = new UnityEngine.Color(1f, 0f, 0f, 1f);")) });
            check(colorReport.CountByRule(WidgetSpecRules.NoHardcodedColors) == 1, "SPEC-006 端到端失效: 颜色字面量未被拦下");

            string aliasSrc = BuildSyntheticWidget("        private void Q() { var c = C.red; }")
                              .Replace("using System;", "using System;\nusing C = UnityEngine.Color;");
            var aliasReport = Scan(new[] { MakeFile("Alias.cs", aliasSrc) });
            check(aliasReport.CountByRule(WidgetSpecRules.NoHardcodedColors) == 1, "SPEC-006 漏检: using 别名写法（using C = UnityEngine.Color; C.red）未被拦下");

            var inheritReport = Scan(new[] { MakeFile("FakeBase.cs", compliant.Replace(": BaseFlightWidget", string.Empty)) });
            check(inheritReport.CountByRule(WidgetSpecRules.Inheritance) == 1, "SPEC-001 端到端失效: 未继承基类未被拦下");

            string forgery = "namespace N { public class Sneaky : MonoBehaviour { void M<T>() where T : BaseFlightWidget { } } }";
            var forgeryReport = Scan(new[] { MakeFile("Sneaky.cs", forgery) });
            check(forgeryReport.CountByRule(WidgetSpecRules.Inheritance) == 1, "SPEC-001 漏检: 泛型约束 where T : BaseFlightWidget 被当成了继承");

            string midSrc = compliant.Replace("class FakeWidget", "class FakeMid");
            string leafSrc = "namespace N { public class FakeLeaf : FakeMid { } }";
            var indirectReport = Scan(new[] { MakeFile("FakeMid.cs", midSrc), MakeFile("FakeLeaf.cs", leafSrc) });
            check(indirectReport.CountByRule(WidgetSpecRules.Inheritance) == 0, "SPEC-001 误报: 间接派生（FakeLeaf : FakeMid : BaseFlightWidget）被判未继承");

            string qualified = compliant
                .Replace("ApplyTheme(ThemeConfig theme)", "ApplyTheme(ModularFlightPanel.UI.ThemeConfig theme)")
                .Replace("OnUpdateTelemetry(IFlightTelemetry telemetry)", "OnUpdateTelemetry(ModularFlightPanel.Core.IFlightTelemetry telemetry)")
                .Replace("override WidgetRefreshTier RefreshTier", "override ModularFlightPanel.UI.WidgetRefreshTier RefreshTier");
            var qualifiedReport = Scan(new[] { MakeFile("Qualified.cs", qualified) });
            check(qualifiedReport.ErrorCount == 0, "误报回归: 类型名写全限定不应判违规 -> " + Describe(qualifiedReport));

            var attrReport = Scan(new[] { MakeFile("FakeAttr.cs", compliant.Replace("[FlightWidget(\"fake_widget\")]", string.Empty)) });
            check(attrReport.CountByRule(WidgetSpecRules.AutoRegistration) == 1, "SPEC-008 端到端失效: 缺失 [FlightWidget] 未被拦下");

            // ── (4) SPEC-006 棘轮必须由代码强制，而不是注释里的口头约定 ──
            var ratchetFailures = WidgetColorLiteralAudit.ValidateRatchet();
            for (int i = 0; i < ratchetFailures.Count; i++)
            {
                cases++;
                failures.Add("SPEC-006 棘轮被破坏: " + ratchetFailures[i]);
            }
            cases++;

            // ── (5) 发现层护栏必须真的能拦住"空集假绿" ──
            check(CheckDiscoveryFloor(null, 44) != null, "护栏失效: repoRoot 为空（源码审计根本不会执行）必须报错");
            check(CheckDiscoveryFloor(@"C:\nonexistent-repo-root", 44) != null, "护栏失效: 仓库根不存在必须报错");
            check(CheckDiscoveryFloor(@"C:\nonexistent-repo-root", 0) != null, "护栏失效: 扫描量低于冻结下限必须报错");

            LastSelfTestCaseCount = cases;
            return failures;
        }

        private static string Describe(WidgetSourceAuditReport report)
        {
            var parts = new List<string>();
            for (int i = 0; i < report.Violations.Count; i++)
                parts.Add(report.Violations[i].ToString());
            return string.Join(" ; ", parts.ToArray());
        }

        private static WidgetSourceFile MakeFile(string name, string text)
        {
            return new WidgetSourceFile { Name = name, Path = name, Text = text };
        }

        /// <summary>构造一份"完全合规"的合成组件源码（自检基准），extra 为附加在类体末尾的代码</summary>
        private static string BuildSyntheticWidget(string extra)
        {
            return "using System;\n"
                 + "namespace ModularFlightPanel.UI\n"
                 + "{\n"
                 + "    [FlightWidget(\"fake_widget\")]\n"
                 + "    public class FakeWidget : BaseFlightWidget\n"
                 + "    {\n"
                 + "        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;\n"
                 + "        public override void ApplyTheme(ThemeConfig theme) { }\n"
                 + "        public override void OnUpdateTelemetry(IFlightTelemetry telemetry) { }\n"
                 + "        protected override void OnDestroy() { base.OnDestroy(); }\n"
                 + (string.IsNullOrEmpty(extra) ? string.Empty : extra + "\n")
                 + "    }\n"
                 + "}\n";
        }

        private static void ScanOne(WidgetSourceFile file, WidgetSourceAuditReport report, HashSet<string> widgetClosure,
            ref int debtFiles, ref int debtOccurrences)
        {
            // 关键：所有规则都在"词法清洗 + 别名归一化"后的骨架上判定 —— 注释与字符串不参与，
            // 且 `using TC = ...ThemeConfig;` 这类别名会被还原成简名，不再成为绕过通道。
            string code = CSharpSourceLinter.SanitizeAndNormalize(file.Text);

            // ── SPEC-001 继承契约（类级 + 继承闭包）──
            // 旧实现是文件级 `:\s*BaseFlightWidget`，一句 `where T : BaseFlightWidget` 泛型约束
            // 就能把"压根没继承"的类洗成合规；同时"间接派生的独立文件"又被误判。
            // 现在改为：本文件声明的类里，至少有一个真的落在组件继承闭包内。
            if (!DeclaresWidgetClass(code, widgetClosure))
            {
                Add(report, file, WidgetSpecRules.Inheritance, "ERROR", 0,
                    "未继承 BaseFlightWidget 统一基类（本文件未声明任何可归入组件继承闭包的类；"
                    + "注意泛型约束 where T : BaseFlightWidget 不算继承）");
            }

            // ── SPEC-002 刷新阶梯：形状 + 取值双重校验（表达式体与块状实现均合法）──
            var tierDecl = RefreshTierDeclRegex.Match(code);
            if (!tierDecl.Success)
            {
                Add(report, file, WidgetSpecRules.RefreshTier, "ERROR", 0,
                    "未显式重写 RefreshTier (必须声明 Critical / Standard / Relaxed / UltraLow)");
            }
            else
            {
                string tierBody = ExtractMemberBody(code, tierDecl.Index);
                if (RefreshTierCastRegex.IsMatch(tierBody))
                {
                    Add(report, file, WidgetSpecRules.RefreshTier, "ERROR", LineOf(code, tierDecl.Index),
                        "RefreshTier 禁止用强制转换（如 (WidgetRefreshTier)999）伪造阶梯，必须直接返回枚举成员之一");
                }
                else if (!RefreshTierValueRegex.IsMatch(tierBody))
                {
                    Add(report, file, WidgetSpecRules.RefreshTier, "ERROR", LineOf(code, tierDecl.Index),
                        "RefreshTier 取值必须是 Critical / Standard / Relaxed / UltraLow 之一（表达式体或 get 块内直接返回枚举成员）");
                }
            }

            // ── SPEC-003 主题管道 ──
            // BaseFlightWidget 已提供 virtual 默认实现（自动将主题分发至 Controls 注册的所有微控件）。
            // 若组件声明了自定义 ApplyTheme，则必须符合 public override void ApplyTheme(ThemeConfig) 签名以保证管道接入。
            if (AnyApplyThemeDeclRegex.IsMatch(code) && !ApplyThemeRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.SemanticTheming, "ERROR", 0,
                    "声明了 ApplyTheme 但签名不符合规范（应为 public override void ApplyTheme(ThemeConfig theme)）");
            }

            // ── SPEC-004 遥测契约 ──
            if (!UpdateTelemetryRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.TelemetryContract, "ERROR", 0,
                    "未重写 OnUpdateTelemetry(IFlightTelemetry) 遥测驱动接口");
            }

            // ── SPEC-005 安全生命周期 ──
            // BaseFlightWidget 已提供 virtual 默认实现（自动解绑 I18n、RenderManager、DragHandler 并清理 Controls）。
            // 当派生组件未显式声明 OnDestroy 时直接安全继承基类托管；若显式声明则必须 override 且调用 base.OnDestroy()。
            foreach (Match m in OnDestroyDeclRegex.Matches(code))
            {
                if (!HasOverrideModifier(code, m.Index))
                {
                    Add(report, file, WidgetSpecRules.SafeLifecycle, "ERROR", LineOf(code, m.Index),
                        "声明了 OnDestroy 但未 override 基类，会隐藏基类方法并阻断销毁清理"
                        + "（应写作 protected override void OnDestroy() 并调用 base.OnDestroy()）");
                    continue;
                }

                string body = ExtractMemberBody(code, m.Index);
                if (!BaseOnDestroyCallRegex.IsMatch(body))
                {
                    Add(report, file, WidgetSpecRules.SafeLifecycle, "ERROR", LineOf(code, m.Index),
                        "override 了 OnDestroy 但方法体内未调用 base.OnDestroy()，基类解注册/回收链会被整段截断");
                }
            }

            // ── SPEC-006 零颜色字面量（基线表为空，任何新增即拦下）──
            // 计数单位由"行"改为"**处**"：旧实现同一行写两个颜色字面量只算 1 行，
            // 一旦某文件登记了基线，往"已计数行"上再挂一个字面量即可白嫖额度。
            int colorOccurrences = WidgetColorLiteralAudit.CountOccurrences(file.Text);
            int allowed = WidgetColorLiteralAudit.GetAllowedLines(file.Name);
            if (colorOccurrences > allowed)
            {
                var samples = string.Join(" | ", WidgetColorLiteralAudit.CollectOffendingLines(file.Text, 3).ToArray());
                Add(report, file, WidgetSpecRules.NoHardcodedColors, "ERROR", 0,
                    "新增颜色字面量 " + (colorOccurrences - allowed) + " 处 (实测 " + colorOccurrences + " / 基线 " + allowed + ")。"
                    + "颜色必须取自 WidgetStyleManager 语义角色或 ThemeConfig；占位色用 Color.clear。样本: " + samples);
            }
            else if (colorOccurrences > 0)
            {
                debtFiles++;
                debtOccurrences += colorOccurrences;
            }

            // ── SPEC-007 零场景查询：在整份骨架文本上匹配（换行写法同样拦得住），按行去重报错 ──
            int lastReportedLine = -1;
            foreach (Match m in SceneQueryRegex.Matches(code))
            {
                int line = LineOf(code, m.Index);
                if (line == lastReportedLine) continue;
                lastReportedLine = line;
                Add(report, file, WidgetSpecRules.NoSceneQueries, "ERROR", line,
                    "组件内出现场景查询 API (" + Flatten(m.Value) + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
            }

            var staticImport = StaticSceneImportRegex.Match(code);
            if (staticImport.Success)
            {
                Add(report, file, WidgetSpecRules.NoSceneQueries, "ERROR", LineOf(code, staticImport.Index),
                    "禁止 using static UnityEngine.GameObject/Object/Resources：它会让裸名 Find(\"x\") 绕过场景查询门禁");
            }

            // ── SPEC-008 自动发现与注册元数据契约（抽象基类与标杆模板豁免）──
            if (!AbstractClassRegex.IsMatch(code)
                && !string.Equals(file.Name, "BaseNavballSphereWidget.cs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.Name, "StandardFlightWidgetTemplate.cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!FlightWidgetAttrRegex.IsMatch(code))
                {
                    Add(report, file, WidgetSpecRules.AutoRegistration, "ERROR", 0,
                        "未显式声明 [FlightWidget] 自动注册与预设库元数据特性 (所有具体航电组件必须声明元数据以便自动挂载至游戏内预设库与验证器)");
                }
            }
        }

        /// <summary>本文件是否声明了至少一个落在组件继承闭包内的类（SPEC-001 的类级判定）</summary>
        private static bool DeclaresWidgetClass(string normalizedCode, HashSet<string> widgetClosure)
        {
            var decls = CollectClassDecls(normalizedCode);
            for (int i = 0; i < decls.Count; i++)
            {
                if (widgetClosure.Contains(decls[i].Key)) return true;
            }
            return false;
        }

        /// <summary>
        /// 取 index 处成员声明的"实现体"：
        ///   - 表达式体 (=> x;)      → 取到分号
        ///   - 块体     ({ ... })    → 花括号配对取到对应 }
        /// 无体（抽象/仅分号）返回空串。用于确认成员体内"真的调用了 base.OnDestroy()"、
        /// "真的返回了合法枚举成员"，而不是只看声明存在与否。
        /// </summary>
        private static string ExtractMemberBody(string code, int index)
        {
            int n = code.Length;
            int i = index;
            while (i < n && code[i] != '{' && code[i] != ';' && code[i] != '=') i++;
            if (i >= n) return string.Empty;
            if (code[i] == ';') return string.Empty;
            if (code[i] == '=')
            {
                int semi = code.IndexOf(';', i);
                return semi < 0 ? code.Substring(i) : code.Substring(i, semi - i);
            }

            int depth = 0;
            int j = i;
            for (; j < n; j++)
            {
                if (code[j] == '{') depth++;
                else if (code[j] == '}')
                {
                    depth--;
                    if (depth == 0) { j++; break; }
                }
            }
            return code.Substring(i, j - i);
        }

        /// <summary>把可能跨行的匹配值压成单行，便于日志与报告定位</summary>
        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// 判断 index 处的方法声明是否为 override：
        /// 向前回溯到最近的成员边界（{ } ;）为止，只在该"声明前缀"范围内找 override 关键字。
        /// 这样既支持多行签名，又不会被上一个成员的 override 误伤。
        /// </summary>
        private static bool HasOverrideModifier(string code, int index)
        {
            int start = index;
            while (start > 0)
            {
                char c = code[start - 1];
                if (c == '{' || c == '}' || c == ';') break;
                start--;
            }
            return OverrideKeywordRegex.IsMatch(code.Substring(start, index - start));
        }

        private static int LineOf(string code, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < code.Length; i++)
                if (code[i] == '\n') line++;
            return line;
        }

        private static void Add(WidgetSourceAuditReport report, WidgetSourceFile file, string rule, string severity, int line, string description)
        {
            report.Violations.Add(new WidgetSourceViolation
            {
                FileName = file.Name,
                RuleCode = rule,
                Severity = severity,
                Line = line,
                Description = description
            });
        }
    }
}
