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
    /// 极简 C# 词法预处理器（注释 / 字符串字面量剥离器）。
    ///
    /// 作用：把注释与字符串字面量的内容整体替换为空格，**完整保留换行与列位置**，
    /// 使上层正则规则只在"真代码"上匹配。这样既杜绝
    ///   - "注释里提了一句 FindObjectOfType 就被判违规"的误报，
    ///   - "字符串里写着 new Color(...) 被计入颜色字面量"的假阳性，
    /// 又能让报错行号保持准确。
    ///
    /// 覆盖：// 行注释、/* */ 块注释、"..." 普通字符串（\ 转义）、@"..." 逐字字符串（"" 转义）、
    ///      $"..." / $@"..." / @$"..." 内插字符串、'c' 字符字面量。
    ///
    /// 已声明的近似（由 SelfTest 固化）：内插字符串 { } 洞内的表达式一并清空。
    /// 组件中不存在"把颜色或场景查询写进插值洞"的写法，故不为此引入插值洞嵌套词法状态机。
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
                    i = ConsumeString(sourceText, sb, i + 2, true);
                    continue;
                }
                if (c == '$' && i + 2 < n && sourceText[i + 1] == '@' && sourceText[i + 2] == '"')
                {
                    sb.Append("$@");
                    i = ConsumeString(sourceText, sb, i + 2, true);
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
                    i = ConsumeString(sourceText, sb, i + 1, false);
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
        /// 词法器自检（无头验证器的常驻检查项）。返回失败描述列表，空列表 = 全部通过。
        /// 每条用例都对应一个真实风险：注释误报、字符串假阳性、转义/逐字/字符字面量边界。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();

            Action<string, string, bool> expect = (name, snippet, shouldSurvive) =>
            {
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
            expect("内插字符串提及", "string s = $\"a{FindObjectOfType<b>()}c\";\n", false);
            expect("真实调用保留", "var x = FindObjectOfType<Vessel>();\n", true);
            expect("同行调用+注释", "var x = FindObjectOfType<Vessel>(); // 仅这一次\n", true);
            expect("转义引号", "string s = \"a\\\" FindObjectOfType\";\n", false);
            expect("字符字面量后", "char q = '\\''; var x = FindObjectOfType<Vessel>();\n", true);

            // 颜色字面量的假阳性 / 漏报边界（复核 SPEC-006 正则）
            if (WidgetColorLiteralAudit.CountLines("// new Color(1f,0f,0f)\n") != 0)
                failures.Add("注释中的颜色字面量被误计入 SPEC-006");
            if (WidgetColorLiteralAudit.CountLines("string s = \"new Color(1f,0f,0f)\";\n") != 0)
                failures.Add("字符串中的颜色字面量被误计入 SPEC-006");
            if (WidgetColorLiteralAudit.CountLines("var c = new Color(1f, 0f, 0f, 1f);\n") != 1)
                failures.Add("真实颜色字面量未被 SPEC-006 捕获（漏报）");
            if (WidgetColorLiteralAudit.CountLines("var c = Color.clear;\n") != 0)
                failures.Add("Color.clear 豁免失效");

            // 行数一致性：清洗不得改变行数（报错行号可信的前提）
            string sample = "line1\n/* a\nb */\n\"s\n\"\nline5\n";
            if (Sanitize(sample).Split('\n').Length != sample.Split('\n').Length)
                failures.Add("清洗后行数与原文不一致，报错行号将不可信");

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
        // ── 规则 005：OnDestroy 声明 ──
        private static readonly Regex OnDestroyDeclRegex = new Regex(@"\bvoid\s+OnDestroy\s*\(", RegexOptions.Compiled);
        private static readonly Regex OverrideKeywordRegex = new Regex(@"\boverride\b", RegexOptions.Compiled);

        // ── 规则 007：Unity 场景查询 API 家族（组件内一律禁止，统一走 ProbeManager）──
        // 命名推导（务必按此展开，别再写成 Find(?:Object|Objects|First|Any)Object... 那种
        // 需要"Object"出现两次的写法 —— 它匹配不上 FindObjectOfType，门禁会静默失效）：
        //   Find + [First|Any]? + Object[s]? + [OfType|ByType] + [All]?
        // 覆盖：FindObjectOfType / FindObjectsOfType / FindObjectsOfTypeAll /
        //       FindFirstObjectByType / FindAnyObjectByType / FindObjectsByType
        // 以及 GameObject.Find / FindWithTag / FindGameObjectsWithTag。
        private static readonly Regex SceneQueryRegex = new Regex(
            @"\bFind(?:First|Any)?Object(?:s)?(?:OfType|ByType)(?:All)?\s*[<(]"
            + @"|\bGameObject\.Find(?:WithTag|GameObjectsWithTag)?\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex InheritanceRegex = new Regex(@":\s*(?:[A-Za-z0-9_.]+\.)?(?:BaseFlightWidget|BaseNavballSphereWidget)\b", RegexOptions.Compiled);
        private static readonly Regex ClassInheritanceRegex = new Regex(@"\bclass\s+\w+\s*:\s*BaseFlightWidget\b", RegexOptions.Compiled);
        private static readonly Regex RefreshTierRegex = new Regex(@"override\s+WidgetRefreshTier\s+RefreshTier\s*(?:=>|\{|\r|\n|$)", RegexOptions.Compiled);
        private static readonly Regex ApplyThemeRegex = new Regex(@"public\s+override\s+void\s+ApplyTheme\s*\(\s*ThemeConfig\b", RegexOptions.Compiled);
        private static readonly Regex UpdateTelemetryRegex = new Regex(@"public\s+override\s+void\s+OnUpdateTelemetry\s*\(\s*IFlightTelemetry\b", RegexOptions.Compiled);

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
        /// 组件源文件发现（唯一定义）：
        ///   - src/ModularFlightPanel/UI/Widgets/**/*.cs → 全部按组件对待；
        ///   - src/ModularFlightPanel/UI/*.cs           → 仅当声明了 ": BaseFlightWidget" 才算组件。
        /// 管理器 / 工具类 / IMGUI 设置面板不是组件，不得套用组件规则。
        /// </summary>
        public static List<WidgetSourceFile> DiscoverComponentFiles(string repositoryRoot)
        {
            var result = new List<WidgetSourceFile>();
            string uiDir = GetUiDirectory(repositoryRoot);
            if (string.IsNullOrEmpty(uiDir) || !Directory.Exists(uiDir)) return result;

            string widgetsDir = Path.Combine(uiDir, "Widgets");
            if (Directory.Exists(widgetsDir))
            {
                var files = Directory.GetFiles(widgetsDir, "*.cs", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++) result.Add(Load(files[i]));
            }

            var nonWidgetFiles = Directory.GetFiles(uiDir, "*.cs", SearchOption.AllDirectories);
            Array.Sort(nonWidgetFiles, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nonWidgetFiles.Length; i++)
            {
                string filePath = nonWidgetFiles[i];
                if (filePath.StartsWith(widgetsDir, StringComparison.OrdinalIgnoreCase)) continue;
                string text = File.ReadAllText(filePath);
                if (ClassInheritanceRegex.IsMatch(CSharpSourceLinter.Sanitize(text)))
                {
                    result.Add(Load(filePath, text));
                }
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

        /// <summary>扫描一批组件源文件，产出统一报告（含 SPEC-006 基线债务汇总）</summary>
        public static WidgetSourceAuditReport Scan(IEnumerable<WidgetSourceFile> files)
        {
            var report = new WidgetSourceAuditReport();
            int pendingDebtFiles = 0;
            int pendingDebtLines = 0;

            foreach (var file in files)
            {
                report.WidgetsScanned++;
                ScanOne(file, report, ref pendingDebtFiles, ref pendingDebtLines);
            }

            if (pendingDebtFiles > 0)
            {
                report.Violations.Add(new WidgetSourceViolation
                {
                    FileName = pendingDebtFiles + " 个组件",
                    RuleCode = WidgetSpecRules.NoHardcodedColors,
                    Severity = "WARNING",
                    Line = 0,
                    Description = "仍有 " + pendingDebtLines + " 行颜色字面量未迁入 WidgetStyleManager 语义 API。"
                                + "迁移完成后必须下调 WidgetColorLiteralAudit 基线（基线只允许下降）。"
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
        public static List<string> SelfTest()
        {
            var failures = new List<string>();

            // ── (1) SPEC-007 必须命中的写法：Unity 场景查询 API 家族全集 ──
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
                @"var i = UnityEngine.Object.FindObjectOfType<Camera>();"
            };
            for (int i = 0; i < sceneQueries.Length; i++)
            {
                if (!SceneQueryRegex.IsMatch(sceneQueries[i]))
                    failures.Add("SPEC-007 漏检（门禁形同虚设）: " + sceneQueries[i]);
            }

            // ── (2) SPEC-007 必须放过的写法（合法调用 / 注释 / 字符串）──
            string[] notQueries =
            {
                @"var a = ProbeManager.Instance.ResetSceneSearchGate();",
                @"var b = probe.HasVessel;",
                @"// FindObjectOfType<Camera>() 已废弃",
                @"string s = ""FindObjectOfType"";"
            };
            for (int i = 0; i < notQueries.Length; i++)
            {
                if (SceneQueryRegex.IsMatch(CSharpSourceLinter.Sanitize(notQueries[i])))
                    failures.Add("SPEC-007 误报（合法调用 / 注释 / 字符串被当成场景查询）: " + notQueries[i]);
            }

            // ── (3) 端到端：合成组件源码跑完整 Scan() ──
            string compliant = BuildSyntheticWidget(null);

            var okReport = Scan(new[] { MakeFile("FakeOk.cs", compliant) });
            if (okReport.ErrorCount != 0)
                failures.Add("合规合成组件被误判: " + okReport.ErrorCount + " 处 -> " + Describe(okReport));

            var sceneReport = Scan(new[] { MakeFile("FakeScene.cs", BuildSyntheticWidget("        private void Q() { var c = FindObjectOfType<Camera>(); }")) });
            if (sceneReport.CountByRule(WidgetSpecRules.NoSceneQueries) != 1)
                failures.Add("SPEC-007 端到端失效: 真实调用未被拦下 -> " + Describe(sceneReport));

            var commentReport = Scan(new[] { MakeFile("FakeComment.cs", BuildSyntheticWidget("        // FindObjectOfType<Camera> 只出现在注释里")) });
            if (commentReport.CountByRule(WidgetSpecRules.NoSceneQueries) != 0)
                failures.Add("SPEC-007 端到端误报: 注释提及被当成违规");

            var destroyReport = Scan(new[] { MakeFile("FakeDestroy.cs", compliant.Replace("protected override void OnDestroy()", "private void OnDestroy()")) });
            if (destroyReport.CountByRule(WidgetSpecRules.SafeLifecycle) != 1)
                failures.Add("SPEC-005 端到端失效: private void OnDestroy() 未被拦下 -> " + Describe(destroyReport));

            var tierReport = Scan(new[] { MakeFile("FakeTier.cs", compliant.Replace("public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;", string.Empty)) });
            if (tierReport.CountByRule(WidgetSpecRules.RefreshTier) != 1)
                failures.Add("SPEC-002 端到端失效: 缺失 RefreshTier 未被拦下");

            var colorReport = Scan(new[] { MakeFile("FakeColor.cs", BuildSyntheticWidget("        private UnityEngine.Color _c = new UnityEngine.Color(1f, 0f, 0f, 1f);")) });
            if (colorReport.CountByRule(WidgetSpecRules.NoHardcodedColors) != 1)
                failures.Add("SPEC-006 端到端失效: 颜色字面量未被拦下");

            var inheritReport = Scan(new[] { MakeFile("FakeBase.cs", compliant.Replace(": BaseFlightWidget", string.Empty)) });
            if (inheritReport.CountByRule(WidgetSpecRules.Inheritance) != 1)
                failures.Add("SPEC-001 端到端失效: 未继承基类未被拦下");

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

        private static void ScanOne(WidgetSourceFile file, WidgetSourceAuditReport report, ref int debtFiles, ref int debtLines)
        {
            // 关键：所有规则都在词法清洗后的骨架上判定 —— 注释与字符串不参与
            string code = CSharpSourceLinter.Sanitize(file.Text);
            string[] lines = SplitLines(code);

            // ── SPEC-001 继承契约 ──
            if (!InheritanceRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.Inheritance, "ERROR", 0,
                    "未继承 BaseFlightWidget 统一基类");
            }

            // ── SPEC-002 刷新阶梯（表达式体与块状实现均合法）──
            if (!RefreshTierRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.RefreshTier, "ERROR", 0,
                    "未显式重写 RefreshTier (必须声明 Critical / Standard / Relaxed / UltraLow)");
            }

            // ── SPEC-003 主题管道 ──
            if (!ApplyThemeRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.SemanticTheming, "ERROR", 0,
                    "未实现 ApplyTheme(ThemeConfig) 接入 WidgetStyleManager 主题管道");
            }

            // ── SPEC-004 遥测契约 ──
            if (!UpdateTelemetryRegex.IsMatch(code))
            {
                Add(report, file, WidgetSpecRules.TelemetryContract, "ERROR", 0,
                    "未重写 OnUpdateTelemetry(IFlightTelemetry) 遥测驱动接口");
            }

            // ── SPEC-005 安全生命周期：OnDestroy 必须 override（否则隐藏并阻断基类清理）──
            var destroyMatch = OnDestroyDeclRegex.Match(code);
            if (destroyMatch.Success && !HasOverrideModifier(code, destroyMatch.Index))
            {
                Add(report, file, WidgetSpecRules.SafeLifecycle, "ERROR", LineOf(code, destroyMatch.Index),
                    "声明了 OnDestroy 但未 override 基类，会隐藏基类方法并阻断销毁清理"
                    + "（应写作 protected override void OnDestroy() 并调用 base.OnDestroy()）");
            }

            // ── SPEC-006 零颜色字面量（基线表为空，任何新增即拦下）──
            int colorLiterals = WidgetColorLiteralAudit.CountLines(file.Text);
            int allowed = WidgetColorLiteralAudit.GetAllowedLines(file.Name);
            if (colorLiterals > allowed)
            {
                var samples = string.Join(" | ", WidgetColorLiteralAudit.CollectOffendingLines(file.Text, 3).ToArray());
                Add(report, file, WidgetSpecRules.NoHardcodedColors, "ERROR", 0,
                    "新增颜色字面量 " + (colorLiterals - allowed) + " 处 (实测 " + colorLiterals + " / 基线 " + allowed + ")。"
                    + "颜色必须取自 WidgetStyleManager 语义角色或 ThemeConfig；占位色用 Color.clear。样本: " + samples);
            }
            else if (colorLiterals > 0)
            {
                debtFiles++;
                debtLines += colorLiterals;
            }

            // ── SPEC-007 零场景查询：组件内不得出现任何 Unity 场景查询 API ──
            for (int i = 0; i < lines.Length; i++)
            {
                var m = SceneQueryRegex.Match(lines[i]);
                if (!m.Success) continue;
                Add(report, file, WidgetSpecRules.NoSceneQueries, "ERROR", i + 1,
                    "组件内出现场景查询 API (" + m.Value.Trim() + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
            }
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

        private static string[] SplitLines(string code)
        {
            return code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
