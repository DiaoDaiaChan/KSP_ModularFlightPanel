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
    /// MFP-SPEC-006 颜色字面量穿透性语义审计器 (Penetrating Semantic Color Literal Auditor)
    /// ====================================================================================
    /// 纯粹基于 Roslyn 符号图、编译期常量折叠与 SemanticCompilationProvider 的穿透性审计。
    /// 杜绝字符串切分、简名猜测与手工局部遍历，全面直达 UnityEngine 底层色彩定义与 C# 符号层。
    /// </summary>
    public static class WidgetColorLiteralAudit
    {
        private enum ColorSymbolKind
        {
            None,
            ExemptClear,
            BannedPalette,
            BannedMethod,
            BannedUtility
        }

        private static readonly HashSet<string> ColorTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Color", "UnityEngine.Color32", "Color", "Color32"
        };

        private static readonly HashSet<string> VectorTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4",
            "Vector2", "Vector3", "Vector4"
        };

        /// <summary>
        /// 统计源码中的颜色字面量处数 (SPEC-006 唯一权威计数入口)。
        /// 全程由 Roslyn 语义模型直达 UnityEngine 底层类型与 C# 常量折叠。
        ///
        /// 【零容忍，无棘轮】历史存量 334 处清零后，BaselineTable / RatchetCeilingTable 与配套的
        /// TotalRegisteredDebt / GetAllowedOccurrences / IsRegistered / ValidateRatchet 四个空壳入口
        /// 已一并删除 —— 留着恒返回 0 的假 API 只会让调用方误以为"还有可配置的基线"。
        /// 现行口径：任何一处颜色字面量都直接判 ERROR。
        /// </summary>
        public static int CountOccurrences(string sourceText, SemanticModel semanticModel = null)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;
            var model = EnsureSemanticModel(sourceText, semanticModel);
            if (model == null) return 0;

            var walker = new PenetratingColorLiteralWalker(model);
            walker.Visit((CompilationUnitSyntax)model.SyntaxTree.GetRoot());

            LastSemanticConsultCount = walker.SemanticConsultCount;
            return walker.Violations.Count;
        }

        /// <summary>
        /// 列出违规点明细（形如 "L123: xxx"，最多 limit 条，用于门禁报告与审计定位）
        /// </summary>
        public static List<string> CollectOffendingLines(string sourceText, int limit = 8, SemanticModel semanticModel = null)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return new List<string>();
            var model = EnsureSemanticModel(sourceText, semanticModel);
            if (model == null) return new List<string>();

            var walker = new PenetratingColorLiteralWalker(model);
            walker.Visit((CompilationUnitSyntax)model.SyntaxTree.GetRoot());

            LastSemanticConsultCount = walker.SemanticConsultCount;
            return walker.Violations
                .Take(limit)
                .Select(v => $"L{v.Line}: {v.Snippet}")
                .ToList();
        }

        private static SemanticModel EnsureSemanticModel(string sourceText, SemanticModel semanticModel)
        {
            if (semanticModel != null) return semanticModel;
            if (string.IsNullOrWhiteSpace(sourceText)) return null;

            const string prelude = "global using global::System;\nglobal using global::UnityEngine;\n";
            var sources = new[]
            {
                new WidgetSourceFile { Name = "GlobalUsings.cs", Path = "GlobalUsings.cs", Text = prelude },
                new WidgetSourceFile { Name = "TransientAudit.cs", Path = "TransientAudit.cs", Text = sourceText }
            };

            return SemanticCompilationProvider.BuildCompilation(sources)?.GetSemanticModel("TransientAudit.cs");
        }

        public class ColorViolationInfo
        {
            public int Line;
            public string Snippet;
        }

        /// <summary>
        /// 穿透性语义 AST 访问器：直达 UnityEngine 底层类型与 C# 底层符号
        /// </summary>
        private class PenetratingColorLiteralWalker : CSharpSyntaxWalker
        {
            public List<ColorViolationInfo> Violations { get; } = new List<ColorViolationInfo>();
            public int SemanticConsultCount { get; private set; }

            private readonly SemanticModel _semanticModel;

            public PenetratingColorLiteralWalker(SemanticModel semanticModel)
            {
                _semanticModel = semanticModel;
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                SemanticConsultCount++;
                if (IsColorOrColor32Type(_semanticModel.GetTypeInfo(node).Type))
                {
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
                InspectSymbol(node, _semanticModel.GetSymbolInfo(node).Symbol, isStaticImport: false);
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (!(node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node))
                {
                    InspectSymbol(node, _semanticModel.GetSymbolInfo(node).Symbol, isStaticImport: true);
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitCastExpression(CastExpressionSyntax node)
            {
                if (IsColorOrColor32Type(_semanticModel.GetTypeInfo(node.Type).Type))
                {
                    SemanticConsultCount++;
                    if (ProducesColorFromLiteral(node.Expression))
                    {
                        Violations.Add(new ColorViolationInfo
                        {
                            Line = RoslynAstHelper.GetLine(node),
                            Snippet = Flatten(node.ToString())
                        });
                    }
                }

                base.VisitCastExpression(node);
            }

            private bool InspectSymbol(SyntaxNode node, ISymbol symbol, bool isStaticImport)
            {
                if (symbol == null) return false;
                SemanticConsultCount++;

                switch (ClassifySymbol(symbol))
                {
                    case ColorSymbolKind.ExemptClear:
                        return true;

                    case ColorSymbolKind.BannedPalette:
                    case ColorSymbolKind.BannedMethod:
                    case ColorSymbolKind.BannedUtility:
                        string suffix = isStaticImport ? " (using static)" : string.Empty;
                        Violations.Add(new ColorViolationInfo
                        {
                            Line = RoslynAstHelper.GetLine(node),
                            Snippet = Flatten(node.ToString()) + suffix
                        });
                        return true;

                    default:
                        return false;
                }
            }

            private bool ProducesColorFromLiteral(ExpressionSyntax expression) => expression switch
            {
                null => false,
                ObjectCreationExpressionSyntax creation => IsVectorCreation(creation),
                LiteralExpressionSyntax lit => lit.IsKind(SyntaxKind.NumericLiteralExpression) || lit.IsKind(SyntaxKind.StringLiteralExpression),
                IdentifierNameSyntax id => IsLiteralIdentifier(id),
                ParenthesizedExpressionSyntax p => ProducesColorFromLiteral(p.Expression),
                CastExpressionSyntax c => ProducesColorFromLiteral(c.Expression),
                PrefixUnaryExpressionSyntax u => ProducesColorFromLiteral(u.Operand),
                BinaryExpressionSyntax b => ProducesColorFromLiteral(b.Left) || ProducesColorFromLiteral(b.Right),
                ConditionalExpressionSyntax cond => ProducesColorFromLiteral(cond.WhenTrue) || ProducesColorFromLiteral(cond.WhenFalse),
                SwitchExpressionSyntax sw => sw.Arms.Any(arm => ProducesColorFromLiteral(arm.Expression)),
                ConditionalAccessExpressionSyntax ca => ProducesColorFromLiteral(ca.WhenNotNull),
                _ => _semanticModel.GetConstantValue(expression) is { HasValue: true, Value: not null }
            };

            private bool IsVectorCreation(ObjectCreationExpressionSyntax creation)
            {
                var type = _semanticModel.GetTypeInfo(creation).Type;
                if (IsColorOrColor32Type(type)) return false; // 交由 VisitObjectCreationExpression 统一计数

                return (type != null && VectorTypeNames.Contains(type.ToDisplayString()))
                    || VectorTypeNames.Contains(creation.Type.ToString());
            }

            private bool IsLiteralIdentifier(IdentifierNameSyntax id) =>
                _semanticModel.GetSymbolInfo(id).Symbol switch
                {
                    IFieldSymbol { HasConstantValue: true } => true,
                    ILocalSymbol { HasConstantValue: true } => true,
                    ILocalSymbol local => local.DeclaringSyntaxReferences
                        .Select(r => r.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>()
                        .FirstOrDefault()?.Initializer?.Value is { } init && ProducesColorFromLiteral(init),
                    _ => false
                };

            private static string Flatten(string s) =>
                string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        }

        private static bool IsColorOrColor32Type(ITypeSymbol type) =>
            type != null && (
                SemanticCompilationProvider.IsColorOrColor32(type) ||
                (type.OriginalDefinition != null && SemanticCompilationProvider.IsColorOrColor32(type.OriginalDefinition)) ||
                ColorTypeNames.Contains(type.ToDisplayString()) ||
                ColorTypeNames.Contains(type.Name));

        private static ColorSymbolKind ClassifySymbol(ISymbol symbol)
        {
            if (symbol == null) return ColorSymbolKind.None;

            if (IsColorUtilityMethod(symbol))
                return ColorSymbolKind.BannedUtility;

            if (!IsColorOrColor32Type(symbol.ContainingType))
                return ColorSymbolKind.None;

            return symbol switch
            {
                _ when string.Equals(symbol.Name, "clear", StringComparison.OrdinalIgnoreCase) => ColorSymbolKind.ExemptClear,
                IPropertySymbol or IFieldSymbol when symbol.IsStatic => ColorSymbolKind.BannedPalette,
                IMethodSymbol method when method.Name is "HSVToRGB" or "HSVToRGBA" => ColorSymbolKind.BannedMethod,
                _ => ColorSymbolKind.None
            };
        }

        private static bool IsColorUtilityMethod(ISymbol symbol) =>
            symbol is { Name: "TryParseHtmlString", ContainingType: not null } &&
            (symbol.ContainingType.ToDisplayString() == "UnityEngine.ColorUtility" || symbol.ContainingType.Name == "ColorUtility");

        /// <summary>最近一次 SelfTest 实际执行的用例数</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>最近一次计数中，Roslyn 语义查询被调用的次数</summary>
        public static int LastSemanticConsultCount { get; private set; }

        private static readonly (string Snippet, int Expected)[] SelfTestMatrix = new[]
        {
            // ── 基础口径：注释 / 字符串 / 数组分配不计数 ──
            ("// new Color(1f,0f,0f,1f)\n", 0),
            ("string s = \"new Color(1f,0f,0f,1f)\";\n", 0),
            ("var c = new Color(1f, 0f, 0f, 1f);\n", 1),
            ("var c = Color.clear;\n", 0),
            ("Color[] a = new Color[16];\n", 0),
            ("var c = $\"{new Color(1f,0f,0f,1f)}\";\n", 1),
            ("var a = new Color(1,0,0,1); var b = new Color(0,1,0,1);\n", 2),

            // ── 由参数生成颜色 / 字符串解析 / 具名调色板 ──
            ("var c = Color.HSVToRGB(1f,1f,1f);\n", 1),
            ("ColorUtility.TryParseHtmlString(\"#F00\", out var c);\n", 1),
            ("var c = Color.gray;\n", 1),
            ("var c = Color.Lerp(a, b, 0.5f);\n", 0),

            // ── 命名混淆：using 别名与 using static ──
            ("using C = UnityEngine.Color;\nvar c = C.red;\n", 1),
            ("using C = UnityEngine.Color;\nvar c = (C)0xFF0000;\n", 1),
            ("using static UnityEngine.Color;\nvar c = white;\n", 1),

            // ── 强转家族：字面量直转必须拦下，纯变量与语义方法转换不算字面量 ──
            ("var c = (Color)new Vector4(1f,0f,0f,1f);\n", 1),
            ("var c = (Color32)0xFFFFFF;\n", 1),
            ("var c = (Color)(new Color32(255,0,0,255));\n", 1),
            ("var c = (Color)v4;\n", 0),
            ("var c = (Color32)WidgetStyleManager.WithAlpha(aCol, 0.28f);\n", 0),
            ("var c = (Color)theme.FrameBorderColor;\n", 0),

            // ── 现代语法扩展与局部数据流追踪 ──
            ("var c = (Color32)(mode switch { 1 => 0xFF0000, _ => 0x00FF00 });\n", 1),
            ("void M() { uint hex = 0xFF00FF; var c = (Color32)hex; }\n", 1)
        };

        /// <summary>
        /// SPEC-006 穿透性语义审计正反例自检套件
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            int cases = 0;

            // 1. 表驱动基础测试矩阵
            foreach (var (snippet, expected) in SelfTestMatrix)
            {
                cases++;
                int actual = CountOccurrences(snippet);
                if (actual != expected)
                {
                    failures.Add($"SPEC-006 计数不符（期望 {expected} 处，实测 {actual} 处）: {snippet.Replace("\n", "\\n")}");
                }
            }

            // 2. 语义通道自证：同名局部变量抑制 + 真实违规保留
            const string staticImportSnippet =
                "using UnityEngine;\n"
                + "using static UnityEngine.Color;\n"
                + "namespace S { class C { void M() { int red = 1; var x = red; var c = new Color(1f, 0f, 0f, 1f); } } }\n";
            {
                cases++;
                var context = SemanticCompilationProvider.BuildCompilation(new[]
                {
                    new WidgetSourceFile { Name = "SemColor2.cs", Path = "SemColor2.cs", Text = staticImportSnippet }
                });
                var model = context.GetSemanticModel("SemColor2.cs");
                if (model == null)
                {
                    failures.Add("SPEC-006 语义通道不可用：SemColor2.cs 未取得 SemanticModel");
                }
                else
                {
                    int semantic = CountOccurrences(staticImportSnippet, model);
                    if (semantic != 1)
                    {
                        failures.Add($"SPEC-006 语义权威未生效：同名局部变量 red 必须被否决，期望 1 处，实测 {semantic} 处");
                    }
                }

                cases++;
                int standaloneCount = CountOccurrences(staticImportSnippet);
                if (standaloneCount != 1)
                {
                    failures.Add($"SPEC-006 穿透性语义判定漂移：期望 1 处，实测 {standaloneCount} 处");
                }
            }

            // 3. 解析口径一致性：#if KSP_RUNTIME 内的颜色字面量
            {
                cases++;
                string snippet =
                    "using UnityEngine;\n"
                    + "class G\n{\n#if " + WidgetSpecRules.RuntimePreprocessorSymbol + "\n"
                    + "    void M() { var g = new Color(0f, 1f, 0f, 1f); }\n"
                    + "#endif\n}\n";
                var context = SemanticCompilationProvider.BuildCompilation(new[]
                {
                    new WidgetSourceFile { Name = "GuardedColor.cs", Path = "GuardedColor.cs", Text = snippet }
                });
                var model = context.GetSemanticModel("GuardedColor.cs");
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
                        failures.Add($"SPEC-006 解析口径分裂：#if 块内颜色字面量 显式={viaSemantic} / 隐式={viaFallback}");
                    }
                    cases++;
                    if (viaSemantic != 1)
                    {
                        failures.Add($"SPEC-006 漏检受宏保护的代码：实测 {viaSemantic} 处，期望 1 处");
                    }
                }
            }

            // 4. 别名强转语义判定
            {
                cases++;
                string snippet =
                    "using UnityEngine;\n"
                    + "using C = UnityEngine.Color;\n"
                    + "class D { void M() { var c = (C)0xFF0000; } }\n";
                var context = SemanticCompilationProvider.BuildCompilation(new[]
                {
                    new WidgetSourceFile { Name = "CastColor.cs", Path = "CastColor.cs", Text = snippet }
                });
                var model = context.GetSemanticModel("CastColor.cs");
                if (model == null)
                {
                    failures.Add("SPEC-006 强转语义通道不可用：CastColor.cs 未取得 SemanticModel");
                }
                else
                {
                    int viaSemantic = CountOccurrences(snippet, model);
                    if (viaSemantic != 1)
                    {
                        failures.Add($"SPEC-006 强转语义判定失效：实测 {viaSemantic} 处，期望 1 处");
                    }

                    cases++;
                    if (LastSemanticConsultCount <= 0)
                    {
                        failures.Add("SPEC-006 语义点位已失效：LastSemanticConsultCount = 0");
                    }
                }
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }
    }
}
