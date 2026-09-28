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
        /// 统计源码中的颜色字面量**处数** (基于 Roslyn AST 节点访问与语义模型)。
        /// SPEC-006 的唯一计数入口。
        /// </summary>
        public static int CountOccurrences(string sourceText, SemanticModel semanticModel = null)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;
            var walker = new ColorLiteralAstWalker(semanticModel);
            var root = semanticModel != null ? (CompilationUnitSyntax)semanticModel.SyntaxTree.GetRoot() : RoslynAstHelper.ParseRoot(sourceText);
            walker.Visit(root);
            PublishSemanticConsults(walker);
            return walker.Violations.Count;
        }

        /// <summary>
        /// 兼容入口：按行统计（历史口径）
        /// </summary>
        public static int CountLines(string sourceText, SemanticModel semanticModel = null)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;
            var walker = new ColorLiteralAstWalker(semanticModel);
            var root = semanticModel != null ? (CompilationUnitSyntax)semanticModel.SyntaxTree.GetRoot() : RoslynAstHelper.ParseRoot(sourceText);
            walker.Visit(root);
            PublishSemanticConsults(walker);
            return walker.Violations.Select(v => v.Line).Distinct().Count();
        }

        /// <summary>
        /// 列出违规点（形如 "L123: xxx"，最多 limit 条，用于审计报告定位）
        /// </summary>
        public static List<string> CollectOffendingLines(string sourceText, int limit = 8, SemanticModel semanticModel = null)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return new List<string>();
            var walker = new ColorLiteralAstWalker(semanticModel);
            var root = semanticModel != null ? (CompilationUnitSyntax)semanticModel.SyntaxTree.GetRoot() : RoslynAstHelper.ParseRoot(sourceText);
            walker.Visit(root);
            PublishSemanticConsults(walker);
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
        /// 语法树与语义模型访问器：遍历所有可能产生颜色字面量的语法与语义节点
        /// </summary>
        private class ColorLiteralAstWalker : CSharpSyntaxWalker
        {
            public List<ColorViolationInfo> Violations { get; } = new List<ColorViolationInfo>();

            /// <summary>语义分支被咨询的次数（自检据此断言"语义点位没有被静默拆除"）</summary>
            public int SemanticConsultCount { get; private set; }

            /// <summary>语义分支否决语法候选的次数（语义权威确实在起作用的直接证据）</summary>
            public int SemanticRejectCount { get; private set; }

            private readonly SemanticModel _semanticModel;
            private bool _hasStaticColorImport = false;
            private readonly HashSet<string> _colorAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public ColorLiteralAstWalker(SemanticModel semanticModel = null)
            {
                _semanticModel = semanticModel;
            }

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
                bool isColor = false;
                if (_semanticModel != null)
                {
                    SemanticConsultCount++;
                    var typeInfo = _semanticModel.GetTypeInfo(node);
                    if (SemanticCompilationProvider.IsColorOrColor32(typeInfo.Type))
                    {
                        isColor = true;
                    }
                }
                if (!isColor)
                {
                    string typeName = RoslynAstHelper.GetSimpleTypeName(node.Type);
                    isColor = (typeName == "Color" || typeName == "Color32" || _colorAliases.Contains(typeName));
                }

                if (isColor)
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
                bool isColorType = false;
                if (_semanticModel != null)
                {
                    SemanticConsultCount++;
                    var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                    if (symbol != null && SemanticCompilationProvider.IsColorOrColor32(symbol.ContainingType))
                    {
                        isColorType = true;
                    }
                }
                if (!isColorType)
                {
                    string exprStr = node.Expression.ToString().Split('.').Last();
                    isColorType = exprStr == "Color" || exprStr == "Color32" || _colorAliases.Contains(exprStr);
                }

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
                else if (node.Expression.ToString().EndsWith("ColorUtility", StringComparison.Ordinal) && node.Name.Identifier.Text == "TryParseHtmlString")
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
                // 语义权威：先问"这个转换的目标类型到底是不是 UnityEngine.Color/Color32"。
                // 旧实现只看源码里写的类型简名 + 同文件 using 别名表，`(Color)` 这种跨文件别名或
                // 自定义同名类型会误判；语义决议直接给出真实类型。
                bool isColorCast = false;
                if (_semanticModel != null)
                {
                    SemanticConsultCount++;
                    var typeInfo = _semanticModel.GetTypeInfo(node.Type);
                    if (SemanticCompilationProvider.IsColorOrColor32(typeInfo.Type))
                    {
                        isColorCast = true;
                    }
                }
                if (!isColorCast)
                {
                    string targetType = RoslynAstHelper.GetSimpleTypeName(node.Type);
                    isColorCast = targetType == "Color" || targetType == "Color32" || _colorAliases.Contains(targetType);
                }

                if (isColorCast && ProducesColorFromLiteral(node.Expression))
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

            private readonly HashSet<string> _localNumericLiterals = new HashSet<string>(StringComparer.Ordinal);

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var prev = new HashSet<string>(_localNumericLiterals);
                CollectLocalNumericLiterals(node);
                base.VisitMethodDeclaration(node);
                _localNumericLiterals.Clear();
                _localNumericLiterals.UnionWith(prev);
            }

            public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                var prev = new HashSet<string>(_localNumericLiterals);
                CollectLocalNumericLiterals(node);
                base.VisitLocalFunctionStatement(node);
                _localNumericLiterals.Clear();
                _localNumericLiterals.UnionWith(prev);
            }

            public override void VisitAccessorDeclaration(AccessorDeclarationSyntax node)
            {
                var prev = new HashSet<string>(_localNumericLiterals);
                CollectLocalNumericLiterals(node);
                base.VisitAccessorDeclaration(node);
                _localNumericLiterals.Clear();
                _localNumericLiterals.UnionWith(prev);
            }

            private void CollectLocalNumericLiterals(SyntaxNode bodyNode)
            {
                if (bodyNode == null) return;
                foreach (var declarator in bodyNode.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.Initializer != null && ProducesColorFromLiteral(declarator.Initializer.Value))
                    {
                        _localNumericLiterals.Add(declarator.Identifier.Text);
                    }
                }
            }

            /// <summary>
            /// 操作数是否由"代码里的颜色字面量"产生，只认结构形状：
            ///   · 数值字面量直转（(Color32)0xFFFFFF 或通过局部变量 (Color32)hex）
            ///   · 向量构造直转（(Color)new Vector4(1,0,0,1)）
            ///   · Switch 表达式分支穿透与条件访问穿透
            ///   · 上述各形式的括号 / 强转 / 一元 / 三元包装
            /// 由语义 API（如 WidgetStyleManager.WithAlpha）或变量计算出的颜色不是字面量，不计数；
            /// 内层若已是 Color/Color32 构造则交给 VisitObjectCreationExpression 计数，避免同一处数两次。
            /// </summary>
            private bool ProducesColorFromLiteral(ExpressionSyntax expression)
            {
                if (expression == null) return false;

                if (expression is LiteralExpressionSyntax literal)
                {
                    return literal.IsKind(SyntaxKind.NumericLiteralExpression);
                }
                if (expression is IdentifierNameSyntax id && _localNumericLiterals.Contains(id.Identifier.Text))
                {
                    return true;
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
                if (expression is SwitchExpressionSyntax switchExpr)
                {
                    return switchExpr.Arms.Any(arm => ProducesColorFromLiteral(arm.Expression));
                }
                if (expression is ConditionalAccessExpressionSyntax condAccess)
                {
                    return ProducesColorFromLiteral(condAccess.WhenNotNull);
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
                            // 语义权威（只用于否决，不用于放行）：
                            //   · 符号能解析出来、且确认不是 Color/Color32 的成员
                            //     → 这是同名局部变量 / 字段 / 参数，不是调色板成员，必须否决（消除假阳性）；
                            //   · 符号解析不出来（降级环境或确实未绑定）
                            //     → 保持旧口径照报：宁可误报，不可漏报。
                            if (_semanticModel != null)
                            {
                                SemanticConsultCount++;
                                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                                if (!CouldBeColorPaletteMember(symbol))
                                {
                                    SemanticRejectCount++;
                                    base.VisitIdentifierName(node);
                                    return;
                                }
                            }

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
                return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            }

            /// <summary>
            /// 裸标识符是否"可能是 Color/Color32 的静态调色板成员"。
            ///
            /// 语义信息可用时用它否决假阳性：局部变量 / 形参 / 非 Color 类型的字段与属性
            /// 都不可能是具名调色板成员。语义无法判定归属时一律返回 true（保守照报）——
            /// 这条"宁可误报不可漏报"的方向是刻意的：降级环境下 Unity 类型解析不出来，
            /// 此时放宽等于给 `using static UnityEngine.Color; var c = white;` 开后门。
            /// </summary>
            private static bool CouldBeColorPaletteMember(ISymbol symbol)
            {
                if (symbol == null) return true;
                if (symbol is ILocalSymbol || symbol is IParameterSymbol) return false;

                if (symbol is IFieldSymbol || symbol is IPropertySymbol)
                {
                    return SemanticCompilationProvider.IsColorOrColor32(symbol.ContainingType);
                }

                // 方法 / 事件 / 类型等都不可能以裸名形式充当颜色字面量
                return false;
            }
        }

        /// <summary>最近一次 SelfTest 实际执行的用例数（供门禁打印真实条数，避免写死数字漂移）</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// 最近一次计数调用中，语义分支（GetTypeInfo / GetSymbolInfo）被咨询的次数。
        ///
        /// 【为什么需要】这是"语义点位是否还在"的覆盖率指标，而不是结果指标：
        /// 结果可能因环境降级而与语法回退一致，但**被咨询过**这件事与环境无关。
        /// 自检据此断言"语义分支没被静默拆除"，否则把语义判定整段删掉，门禁照样全绿。
        /// </summary>
        public static int LastSemanticConsultCount { get; private set; }

        private static void PublishSemanticConsults(ColorLiteralAstWalker walker)
        {
            LastSemanticConsultCount = walker != null ? walker.SemanticConsultCount : 0;
        }

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

            // ── 现代语法扩展与局部数据流追踪 ──
            expectCount("var c = (Color32)(mode switch { 1 => 0xFF0000, _ => 0x00FF00 });\n", 1);
            expectCount("void M() { uint hex = 0xFF00FF; var c = (Color32)hex; }\n", 1);

            // ── 语义通道自证 ──
            // 【为什么补这一组】此前 22 条用例一律调用 CountOccurrences(snippet) 而不传 SemanticModel，
            // 于是 ColorLiteralAstWalker 里 GetTypeInfo / GetSymbolInfo 两条语义分支从不执行 ——
            // 把它们整段改坏，门禁依然全绿。下面每条都强制走语义路径。
            // (b) 逐条语义判定：假阳性抑制 + 真违规保留（用同一份语义模型分别核对两个方向）
            //     语法回退基线 = 2（1 处 new Color + 1 处裸名 red）；全量语义下必须收敛为 1。
            //     注意 red 必须出现在**表达式位置**才知道它是标识符：`int red = 1;` 的声明名是 token，不是 IdentifierName 节点。
            const string StaticImportSnippet =
                "using UnityEngine;\n"
                + "using static UnityEngine.Color;\n"
                + "namespace S { class C { void M() { int red = 1; var x = red; var c = new Color(1f, 0f, 0f, 1f); } } }\n";
            {
                cases++;
                var context = SemanticCompilationProvider.BuildCompilation(new List<WidgetSourceFile>
                {
                    new WidgetSourceFile { Name = "SemColor2.cs", Path = "SemColor2.cs", Text = StaticImportSnippet }
                });
                SemanticModel model = context.GetSemanticModel("SemColor2.cs");
                if (model == null)
                {
                    failures.Add("SPEC-006 语义通道不可用：SemColor2.cs 未取得 SemanticModel");
                }
                else if (context.IsFullSemanticActive)
                {
                    int semantic = CountOccurrences(StaticImportSnippet, model);
                    if (semantic != 1)
                    {
                        failures.Add("SPEC-006 语义权威未生效：全量语义下同名局部变量 red 必须被否决、"
                                   + "真实违规 new Color 必须保留，期望 1 处，实测 " + semantic + " 处");
                    }
                }
                cases++;
                if (CountOccurrences(StaticImportSnippet) != 2)
                {
                    failures.Add("SPEC-006 语法回退基线漂移：期望 2 处（1 处 new Color + 1 处同名裸名），实测 "
                               + CountOccurrences(StaticImportSnippet) + " 处");
                }
            }

            // (c) 解析口径一致性：#if KSP_RUNTIME 内的颜色字面量在两条路径上必须同样可见
            //     （历史缺陷：语义编译的 parseOptions 漏了 KSP_RUNTIME → 宏内代码整段脱审）
            {
                cases++;
                string snippet =
                    "using UnityEngine;\n"
                    + "class G\n{\n#if " + WidgetSpecRules.RuntimePreprocessorSymbol + "\n"
                    + "    void M() { var g = new Color(0f, 1f, 0f, 1f); }\n"
                    + "#endif\n}\n";
                var context = SemanticCompilationProvider.BuildCompilation(new List<WidgetSourceFile>
                {
                    new WidgetSourceFile { Name = "GuardedColor.cs", Path = "GuardedColor.cs", Text = snippet }
                });
                SemanticModel model = context.GetSemanticModel("GuardedColor.cs");
                if (model == null)
                {
                    failures.Add("SPEC-006 解析口径守卫：GuardedColor.cs 未取得 SemanticModel");
                }
                else
                {
                    int viaSemantic = CountOccurrences(snippet, model);
                    int viaFallback = CountOccurrences(snippet);

                    cases++;
                    if (viaSemantic != viaFallback)
                    {
                        failures.Add($"SPEC-006 解析口径分裂：#if {WidgetSpecRules.RuntimePreprocessorSymbol} 内的颜色字面量"
                                   + $" 语义路径={viaSemantic} / 语法回退={viaFallback}（必须相等，"
                                   + "否则组件内受宏保护的代码会脱审）");
                    }
                    cases++;
                    if (viaSemantic != 1)
                    {
                        failures.Add($"SPEC-006 漏检受宏保护的代码：#if {WidgetSpecRules.RuntimePreprocessorSymbol} 内的"
                                   + $"颜色字面量实测 {viaSemantic} 处，期望 1 处");
                    }
                }
            }

            // (d) 强转的语义判定：别名强转必须拦下（旧实现只用源码类型简名 + 同文件别名表）
            {
                cases++;
                string snippet =
                    "using UnityEngine;\n"
                    + "using C = UnityEngine.Color;\n"
                    + "class D { void M() { var c = (C)0xFF0000; } }\n";
                var context = SemanticCompilationProvider.BuildCompilation(new List<WidgetSourceFile>
                {
                    new WidgetSourceFile { Name = "CastColor.cs", Path = "CastColor.cs", Text = snippet }
                });
                SemanticModel model = context.GetSemanticModel("CastColor.cs");
                if (model == null)
                {
                    failures.Add("SPEC-006 强转语义通道不可用：CastColor.cs 未取得 SemanticModel");
                }
                else
                {
                    int viaSemantic = CountOccurrences(snippet, model);
                    if (viaSemantic != 1)
                    {
                        failures.Add("SPEC-006 强转语义判定失效：别名强转 (C)0xFF0000 未被识别，实测 "
                                   + viaSemantic + " 处（期望 1 处）");
                    }

                    // 覆盖率断言：语义点位 4（GetTypeInfo / GetSymbolInfo）必须真的被采用过。
                    // 语义结论与语法回退结论可能恰好相同，所以只能靠"被咨询"来证明它还在。
                    cases++;
                    if (LastSemanticConsultCount <= 0)
                    {
                        failures.Add("SPEC-006 语义点位 4 已失效：带 SemanticModel 计数后 LastSemanticConsultCount = 0，"
                                   + "GetTypeInfo / GetSymbolInfo 分支已从判定链路中消失");
                    }
                }
            }

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
