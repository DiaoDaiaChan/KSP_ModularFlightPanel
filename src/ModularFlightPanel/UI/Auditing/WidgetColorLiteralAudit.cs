using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// MFP-SPEC-006 颜色字面量审计器 (Color Literal Auditor)
    /// ====================================================================================
    /// 本文件是【纯 C# / 不依赖 UnityEngine】的单一定义，被两处共同编译使用：
    ///   1. 插件本体（UI/WidgetSpecificationValidator.cs 的游戏内规范审计）
    ///   2. tools/HeadlessValidator（无头 CLI 的 [6/9] 规范审计，通过 &lt;Compile Include&gt; 直接编译本文件）
    /// 因此规则与基线永远只有一份，不存在"两份正则各说各话"的漂移。
    ///
    /// 【判定语义】违规点 = 源码里出现下列任一"颜色值直写"写法（注释与字符串内容一律不参与判定）：
    ///   · new Color(...) / new Color32(...) / UnityEngine.Color(...) 等带命名空间或别名的写法
    ///   · Color.white|black|red|green|blue|yellow|cyan|magenta|gray|grey（静态调色板成员）
    ///   · Color.HSVToRGB(...) —— 由裸数字直接生成颜色，是"硬编码调色板"的等效写法
    ///   · ColorUtility.TryParseHtmlString("#RRGGBB") —— 十六进制字面量的入口
    ///   · (Color)new Vector4(1,0,0,1) 这类强制转换构造
    ///   · using static UnityEngine.Color; 之后的裸成员名（white / red / ...）
    ///
    /// 【明确豁免】只有两条，都是"语义上不构成调色板复制品"的写法：
    ///   · Color.clear —— 表达"无颜色/占位透明"，不携带任何语义色
    ///   · new Color[n] —— 纹理/像素缓冲的**数组分配**，不是颜色值（要求紧跟 `(` 才判定）
    ///   注意 new Color(0f,0f,0f,0f) 不豁免：它是绕开 clear 语义的写法，仍按违规处理。
    ///
    /// 【有意不纳入】Color.Lerp(a, b, t) / Color32.Lerp —— 它的入参本身是颜色对象，
    /// 插值运算不产生新字面量；若两个入参是硬编码字面量，那两处字面量自己就会被本规则拦下。
    ///
    /// 【计数单位 = 处，不是行】
    ///   旧实现按"行"计数：同一行写两个颜色字面量只算 1，一旦某文件登记了基线，
    ///   往"已计数行"上再挂一个字面量即可白嫖额度。现在按出现次数计数，基线语义同步收紧。
    ///
    /// 【零容忍 + 代码强制的棘轮】
    ///   334 处历史颜色字面量已全部逐一语义化迁移完毕，BaselineTable 当前为空 —— 全部组件基线 0，
    ///   没有任何豁免名单：新建组件、范式模板、任何既有组件出现颜色字面量都是 ERROR。
    ///   "基线只允许下降"过去只是注释里的口头约定，现在由 RatchetCeilingTable + ValidateRatchet()
    ///   用代码强制：任何未登记上限的基线、或超过上限的基线，都会直接让审计内核自检失败。
    ///
    ///   迁移不是"把数字搬进 WidgetStyleManager"，而是语义化 ——
    ///     · 面板/行/单元格/磁贴/LED 底色 -> SurfaceStyleRole
    ///     · 状态徽标底/状态面板底        -> StatusSurfaceRole
    ///     · 警告/危险/强调等前景色        -> TextStyleRole
    ///     · 描边/刻度的透明度              -> LineWeight 视觉权重档位
    ///     · 剪影/遮罩贴图与图标直通        -> NeutralOpaque（亮度通道，非调色板颜色）
    /// </summary>
    public static class WidgetColorLiteralAudit
    {
        /// <summary>
        /// 颜色字面量匹配（Color.clear 与 new Color[n] 两条豁免）。
        /// 同时覆盖简单名与全限定/别名前缀写法；using 别名（using C = UnityEngine.Color;）
        /// 由 CSharpSourceLinter.SanitizeAndNormalize 在清洗阶段还原成 Color，因此不会成为绕过通道。
        /// ColorBlock 之类以 Color 开头的其它类型不会误伤（要求紧跟括号或点号）。
        /// </summary>
        private static readonly Regex ColorLiteralRegex = new Regex(
            @"\bnew\s+(?:[A-Za-z_]\w*\s*\.\s*)*Color(?:32)?\s*\("
            + @"|\b(?:[A-Za-z_]\w*\s*\.\s*)*Color\s*\.\s*(?!clear\b)(?:white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b"
            + @"|\b(?:[A-Za-z_]\w*\s*\.\s*)*Color\s*\.\s*(?:HSVToRGB|HSVToRGBA)\s*\("
            + @"|\b(?:[A-Za-z_]\w*\s*\.\s*)*ColorUtility\s*\.\s*TryParseHtmlString\s*\("
            + @"|\(\s*(?:[A-Za-z_]\w*\s*\.\s*)*Color(?:32)?\s*\)\s*new\s+(?:Vector4|Vector3)\s*\(",
            RegexOptions.Compiled);

        /// <summary>`using static UnityEngine.Color;` 会让裸成员名（white / red / ...）变成颜色字面量</summary>
        private static readonly Regex StaticColorImportRegex = new Regex(
            @"\busing\s+static\s+(?:[A-Za-z_]\w*\s*\.\s*)*Color(?:32)?\s*;",
            RegexOptions.Compiled);

        /// <summary>裸调色板成员名（仅在存在 using static ...Color 时才启用，避免与普通变量名冲突）</summary>
        private static readonly Regex BareColorMemberRegex = new Regex(
            @"\b(?:white|black|red|green|blue|yellow|cyan|magenta|gray|grey)\b",
            RegexOptions.Compiled);

        /// <summary>
        /// 颜色字面量存量基线：文件名 -> 允许的违规**处数**上限。
        /// 当前为空（全部组件基线 0，零容忍）。数值只允许下调，且必须同步登记 RatchetCeilingTable；
        /// 新增组件一律不得进入本表。
        /// </summary>
        private static readonly Dictionary<string, int> BaselineTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // ── 334 处历史存量已于本轮全部迁移完毕，此表保持为空 ──
        };

        /// <summary>
        /// 棘轮上限（冻结值）：基线永远不得高于此表。此表为空 = 任何文件都不允许登记基线。
        /// 这样"临时欠账"依然保留为一条可行的逃生通道，但它必须同时改两张表、
        /// 在 diff 里留下显式痕迹，而不会再出现"悄悄把数字调大"的静默放水。
        /// </summary>
        private static readonly Dictionary<string, int> RatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 结构保留、内容为空：登记新欠账时必须同时写入本表，并由 ValidateRatchet() 校验。
        };

        /// <summary>全部已登记文件的存量欠账处数合计（诊断与报表用）</summary>
        public static int TotalRegisteredDebt
        {
            get
            {
                int total = 0;
                foreach (var kv in BaselineTable) total += kv.Value;
                return total;
            }
        }

        /// <summary>某文件的允许上限（未登记 = 0，即零容忍）</summary>
        public static int GetAllowedLines(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            return BaselineTable.TryGetValue(fileName, out int allowed) ? allowed : 0;
        }

        /// <summary>该文件是否被登记为"存量欠账"（用于报表区分 ERROR 与 WARNING）</summary>
        public static bool IsRegistered(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) && BaselineTable.ContainsKey(fileName);
        }

        /// <summary>
        /// 棘轮校验（无头验证器的常驻自检项，由 WidgetSourceAudit.SelfTest 调用）。
        /// 返回失败描述列表，空列表 = 棘轮未被破坏。
        /// </summary>
        public static List<string> ValidateRatchet()
        {
            var failures = new List<string>();

            foreach (var kv in BaselineTable)
            {
                if (!RatchetCeilingTable.TryGetValue(kv.Key, out int ceiling))
                {
                    failures.Add("基线未登记棘轮上限（禁止新增基线文件）: " + kv.Key + " = " + kv.Value
                               + "；确需登记时必须同时写入 RatchetCeilingTable 与 BaselineTable");
                    continue;
                }
                if (kv.Value > ceiling)
                {
                    failures.Add("基线被上调（棘轮只允许下降）: " + kv.Key + " = " + kv.Value + " 高于冻结上限 " + ceiling);
                }
            }

            foreach (var kv in RatchetCeilingTable)
            {
                if (!BaselineTable.ContainsKey(kv.Key) && kv.Value != 0)
                {
                    failures.Add("棘轮上限登记了非零值但基线表里没有对应条目: " + kv.Key + " = " + kv.Value);
                }
            }

            return failures;
        }

        /// <summary>
        /// 统计源码中的颜色字面量**处数**（注释与字符串内容已由共享词法器剥离，using 别名已归一化）。
        /// SPEC-006 的唯一计数入口。
        /// </summary>
        public static int CountOccurrences(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return 0;

            string code = CSharpSourceLinter.SanitizeAndNormalize(sourceText);
            int count = ColorLiteralRegex.Matches(code).Count;

            // 只有在文件真的 using static 了 Color 时，裸成员名才当作颜色字面量
            if (StaticColorImportRegex.IsMatch(code))
            {
                count += BareColorMemberRegex.Matches(code).Count;
            }
            return count;
        }

        /// <summary>
        /// 兼容入口：按"行"统计颜色字面量（历史报表口径）。
        /// SPEC-006 已改用 CountOccurrences，行口径不再参与门禁判定。
        /// </summary>
        public static int CountLines(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return 0;

            int count = 0;
            string[] lines = CSharpSourceLinter.SanitizeAndNormalizeLines(sourceText);
            for (int i = 0; i < lines.Length; i++)
            {
                if (ColorLiteralRegex.IsMatch(lines[i])) count++;
            }
            return count;
        }

        /// <summary>列出违规点（形如 "L123: xxx"，最多 limit 条，用于审计报告定位）</summary>
        public static List<string> CollectOffendingLines(string sourceText, int limit = 8)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(sourceText)) return result;

            string code = CSharpSourceLinter.SanitizeAndNormalize(sourceText);
            foreach (Match m in ColorLiteralRegex.Matches(code))
            {
                if (result.Count >= limit) break;
                result.Add("L" + LineOf(code, m.Index) + ": " + Flatten(m.Value));
            }

            if (result.Count < limit && StaticColorImportRegex.IsMatch(code))
            {
                foreach (Match m in BareColorMemberRegex.Matches(code))
                {
                    if (result.Count >= limit) break;
                    result.Add("L" + LineOf(code, m.Index) + ": " + Flatten(m.Value) + " (using static Color)");
                }
            }

            return result;
        }

        private static int LineOf(string code, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < code.Length; i++)
                if (code[i] == '\n') line++;
            return line;
        }

        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
