using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI
{
    using ModularFlightPanel.UI.Auditing;

    /// <summary>
    /// MFP-SPEC-006 颜色字面量审计器 (Color Literal Auditor - Roslyn AST 驱动)
    /// ====================================================================================
    /// 基于微软官方 Roslyn 抽象语法树构建的工业级颜色字面量审计器。
    /// 彻底消除基于文本正则与字符清洗的脆弱性（天然免受注释、字符串字面量、内插洞或命名混淆干扰）。
    /// </summary>
    public static class WidgetColorLiteralAudit
    {
        private static readonly HashSet<string> BannedPaletteMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "white", "black", "red", "green", "blue", "yellow", "cyan", "magenta", "gray", "grey"
        };

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
        /// </summary>
        private static readonly Dictionary<string, int> RatchetCeilingTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 结构保留、内容为空：登记新欠账时必须同时写入本表，并由 ValidateRatchet() 校验。
        };

        public static int TotalRegisteredDebt
        {
            get
            {
                int total = 0;
                foreach (var kv in BaselineTable) total += kv.Value;
                return total;
            }
        }

        /// <summary>该文件允许的颜色字面量**处数**上限（SPEC-006 判定口径是"处数"而非行数）</summary>
        public static int GetAllowedOccurrences(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 0;
            return BaselineTable.TryGetValue(fileName, out int allowed) ? allowed : 0;
        }

        public static bool IsRegistered(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) && BaselineTable.ContainsKey(fileName);
        }

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
        /// 统计源码中的颜色字面量**处数** (基于 Roslyn AST 节点访问)。
        /// SPEC-006 的唯一计数入口。
        /// </summary>
        public static int CountOccurrences(string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;
            var walker = new ColorLiteralAstWalker();
            walker.Visit(RoslynAstHelper.ParseRoot(sourceText));
            return walker.Violations.Count;
        }

        /// <summary>
        /// 兼容入口：按行统计（历史口径）
        /// </summary>
        public static int CountLines(string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;
            var walker = new ColorLiteralAstWalker();
            walker.Visit(RoslynAstHelper.ParseRoot(sourceText));
            return walker.Violations.Select(v => v.Line).Distinct().Count();
        }

        /// <summary>
        /// 列出违规点（形如 "L123: xxx"，最多 limit 条，用于审计报告定位）
        /// </summary>
        public static List<string> CollectOffendingLines(string sourceText, int limit = 8)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return new List<string>();
            var walker = new ColorLiteralAstWalker();
            walker.Visit(RoslynAstHelper.ParseRoot(sourceText));
            return walker.Violations
                .Take(limit)
                .Select(v => $"L{v.Line}: {v.Snippet}")
                .ToList();
        }

        public class ColorViolationInfo
        {
            public int Line;
            public string Snippet;
        }

        /// <summary>
        /// 语法树遍历器：遍历所有可能产生颜色字面量的语法节点
        /// </summary>
        private class ColorLiteralAstWalker : CSharpSyntaxWalker
        {
            public List<ColorViolationInfo> Violations { get; } = new List<ColorViolationInfo>();
            private bool _hasStaticColorImport = false;
            private readonly HashSet<string> _colorAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public override void VisitUsingDirective(UsingDirectiveSyntax node)
            {
                // 检查 using static UnityEngine.Color / using static Color
                if (node.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    string name = node.Name.ToString().Split('.').Last();
                    if (name == "Color" || name == "Color32")
                    {
                        _hasStaticColorImport = true;
                    }
                }
                // 检查 using C = UnityEngine.Color 别名
                else if (node.Alias != null)
                {
                    string target = node.Name.ToString().Split('.').Last();
                    if (target == "Color" || target == "Color32")
                    {
                        _colorAliases.Add(node.Alias.Name.Identifier.Text);
                    }
                }

                base.VisitUsingDirective(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                string typeName = RoslynAstHelper.GetSimpleTypeName(node.Type);
                if (typeName == "Color" || typeName == "Color32" || _colorAliases.Contains(typeName))
                {
                    // new Color(...) / new Color32(...) - 数组分配 new Color[n] 是 ArrayCreationExpression，天然免除
                    Violations.Add(new ColorViolationInfo
                    {
                        Line = RoslynAstHelper.GetLine(node),
                        Snippet = Flatten(node.ToString())
                    });
                }

                base.VisitObjectCreationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                string exprStr = node.Expression.ToString().Split('.').Last();
                bool isColorType = exprStr == "Color" || exprStr == "Color32" || _colorAliases.Contains(exprStr);

                if (isColorType)
                {
                    string memberName = node.Name.Identifier.Text;

                    // Color.clear 是明确合法豁免的占位透明色
                    if (string.Equals(memberName, "clear", StringComparison.OrdinalIgnoreCase))
                    {
                        base.VisitMemberAccessExpression(node);
                        return;
                    }

                    // 静态调色板成员或由参数生成颜色的方法
                    if (BannedPaletteMembers.Contains(memberName) ||
                        string.Equals(memberName, "HSVToRGB", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(memberName, "HSVToRGBA", StringComparison.OrdinalIgnoreCase))
                    {
                        Violations.Add(new ColorViolationInfo
                        {
                            Line = RoslynAstHelper.GetLine(node),
                            Snippet = Flatten(node.ToString())
                        });
                    }
                }
                else if (exprStr == "ColorUtility" && node.Name.Identifier.Text == "TryParseHtmlString")
                {
                    Violations.Add(new ColorViolationInfo
                    {
                        Line = RoslynAstHelper.GetLine(node),
                        Snippet = Flatten(node.ToString())
                    });
                }

                base.VisitMemberAccessExpression(node);
            }

            public override void VisitCastExpression(CastExpressionSyntax node)
            {
                string targetType = RoslynAstHelper.GetSimpleTypeName(node.Type);
                if ((targetType == "Color" || targetType == "Color32" || _colorAliases.Contains(targetType))
                    && ProducesColorFromLiteral(node.Expression))
                {
                    // 旧实现只识别 (Color)new Vector4(...)：于是 (Color32)0xFFFFFF 这类"字面量直转颜色"整类漏检。
                    // 现在的判定口径是"操作数子树里存在数值字面量或颜色/向量构造"——即颜色值确实来自代码字面量。
                    Violations.Add(new ColorViolationInfo
                    {
                        Line = RoslynAstHelper.GetLine(node),
                        Snippet = Flatten(node.ToString())
                    });
                }

                base.VisitCastExpression(node);
            }

            /// <summary>
            /// 操作数是否由"代码里的颜色字面量"产生，只认结构形状：
            ///   · 数值字面量直转（(Color32)0xFFFFFF）
            ///   · 向量构造直转（(Color)new Vector4(1,0,0,1)）
            ///   · 上述两者的括号 / 强转 / 一元 / 三元包装
            /// 由语义 API（如 WidgetStyleManager.WithAlpha）或变量计算出的颜色不是字面量，不计数；
            /// 内层若已是 Color/Color32 构造则交给 VisitObjectCreationExpression 计数，避免同一处数两次。
            /// </summary>
            private static bool ProducesColorFromLiteral(ExpressionSyntax expression)
            {
                if (expression == null) return false;

                if (expression is LiteralExpressionSyntax literal)
                {
                    return literal.IsKind(SyntaxKind.NumericLiteralExpression);
                }
                if (expression is ObjectCreationExpressionSyntax creation)
                {
                    string created = RoslynAstHelper.GetSimpleTypeName(creation.Type);
                    return created == "Vector2" || created == "Vector3" || created == "Vector4";
                }
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    return ProducesColorFromLiteral(parenthesized.Expression);
                }
                if (expression is CastExpressionSyntax cast)
                {
                    return ProducesColorFromLiteral(cast.Expression);
                }
                if (expression is PrefixUnaryExpressionSyntax unary)
                {
                    return ProducesColorFromLiteral(unary.Operand);
                }
                if (expression is BinaryExpressionSyntax binary)
                {
                    return ProducesColorFromLiteral(binary.Left) || ProducesColorFromLiteral(binary.Right);
                }
                if (expression is ConditionalExpressionSyntax conditional)
                {
                    return ProducesColorFromLiteral(conditional.WhenTrue) || ProducesColorFromLiteral(conditional.WhenFalse);
                }

                return false;
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // 若存在 using static Color; 则直接引用的裸静态调色板成员也是违规
                if (_hasStaticColorImport)
                {
                    string id = node.Identifier.Text;
                    if (BannedPaletteMembers.Contains(id))
                    {
                        // 确保不是作为 MemberAccess 的右侧（已经在 VisitMemberAccessExpression 统计过）
                        if (!(node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node))
                        {
                            Violations.Add(new ColorViolationInfo
                            {
                                Line = RoslynAstHelper.GetLine(node),
                                Snippet = Flatten(node.ToString()) + " (using static Color)"
                            });
                        }
                    }
                }

                base.VisitIdentifierName(node);
            }

            private static string Flatten(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Replace("\r", " ").Replace("\n", " ").Trim();
            }
        }

        /// <summary>最近一次 SelfTest 实际执行的用例数（供门禁打印真实条数，避免写死数字漂移）</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// SPEC-006 计数器的正反用例自检（含棘轮完整性校验）。
        /// 本计数器是 SPEC-006 的唯一判定入口，因此其边界行为必须自证。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            Action<string, int> expectCount = (snippet, expected) =>
            {
                cases++;
                int actual = CountOccurrences(snippet);
                if (actual != expected)
                {
                    failures.Add($"SPEC-006 计数不符（期望 {expected} 处，实测 {actual} 处）: {snippet.Replace("\n", "\\n")}");
                }
            };

            // ── 基础口径：注释 / 字符串 / 数组分配不计数 ──
            expectCount("// new Color(1f,0f,0f,1f)\n", 0);
            expectCount("string s = \"new Color(1f,0f,0f,1f)\";\n", 0);
            expectCount("var c = new Color(1f, 0f, 0f, 1f);\n", 1);
            expectCount("var c = Color.clear;\n", 0);
            expectCount("Color[] a = new Color[16];\n", 0);
            expectCount("var c = $\"{new Color(1f,0f,0f,1f)}\";\n", 1);
            expectCount("var a = new Color(1,0,0,1); var b = new Color(0,1,0,1);\n", 2);

            // ── 由参数生成颜色 / 字符串解析 / 具名调色板 ──
            expectCount("var c = Color.HSVToRGB(1f,1f,1f);\n", 1);
            expectCount("ColorUtility.TryParseHtmlString(\"#F00\", out var c);\n", 1);
            expectCount("var c = Color.gray;\n", 1);
            expectCount("var c = Color.Lerp(a, b, 0.5f);\n", 0);

            // ── 命名混淆：using 别名与 using static ──
            expectCount("using C = UnityEngine.Color;\nvar c = C.red;\n", 1);
            expectCount("using C = UnityEngine.Color;\nvar c = (C)0xFF0000;\n", 1);
            expectCount("using static UnityEngine.Color;\nvar c = white;\n", 1);

            // ── 强转家族：字面量直转必须拦下，纯变量转换不算字面量 ──
            expectCount("var c = (Color)new Vector4(1f,0f,0f,1f);\n", 1);
            expectCount("var c = (Color32)0xFFFFFF;\n", 1);
            expectCount("var c = (Color)(new Color32(255,0,0,255));\n", 1);
            expectCount("var c = (Color)v4;\n", 0);
            expectCount("var c = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.28f);\n", 0);
            expectCount("var c = (Color)theme.FrameBorderColor;\n", 0);

            var ratchetFailures = ValidateRatchet();
            for (int i = 0; i < ratchetFailures.Count; i++)
            {
                cases++;
                failures.Add("SPEC-006 棘轮被破坏: " + ratchetFailures[i]);
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }
    }
}
