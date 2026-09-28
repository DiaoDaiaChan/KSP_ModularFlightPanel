using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI.Auditing
{
    /// <summary>
    /// 工业级 Roslyn C# 抽象语法树 (AST) 解析与查询辅助库。
    /// 专用于航电架构门禁、规范校验器与现代化分析器。
    /// </summary>
    public static class RoslynAstHelper
    {
        private static readonly CSharpParseOptions DefaultOptions =
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols("KSP_RUNTIME");

        /// <summary>
        /// 将 C# 源码解析为 Roslyn 语法树（默认已穿透激活 KSP_RUNTIME 宏）
        /// </summary>
        public static SyntaxTree ParseTree(string sourceCode)
        {
            return CSharpSyntaxTree.ParseText(sourceCode ?? string.Empty, DefaultOptions);
        }

        /// <summary>
        /// 将 C# 源码解析为 Roslyn 语法树（支持自定义预编译宏符号）
        /// </summary>
        public static SyntaxTree ParseTree(string sourceCode, params string[] preprocessorSymbols)
        {
            var options = preprocessorSymbols != null && preprocessorSymbols.Length > 0
                ? CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithPreprocessorSymbols(preprocessorSymbols)
                : DefaultOptions;
            return CSharpSyntaxTree.ParseText(sourceCode ?? string.Empty, options);
        }

        /// <summary>
        /// 将 C# 源码解析为根编译单元语法节点 (CompilationUnitSyntax)
        /// </summary>
        public static CompilationUnitSyntax ParseRoot(string sourceCode)
        {
            return ParseTree(sourceCode).GetCompilationUnitRoot();
        }

        /// <summary>
        /// 将 C# 源码解析为根编译单元语法节点 (支持自定义预编译宏符号)
        /// </summary>
        public static CompilationUnitSyntax ParseRoot(string sourceCode, params string[] preprocessorSymbols)
        {
            return ParseTree(sourceCode, preprocessorSymbols).GetCompilationUnitRoot();
        }

        /// <summary>
        /// 带诊断的解析入口：解析失败时通过 errors 回传可读的"行:列 错误信息"列表。
        ///
        /// 【为什么必须有这个入口】坏语法会让 ClassDeclarationSyntax 部分或全部从语法树中消失，
        /// 组件因此脱离审计作用域 —— 报告会给出"零违规"的全绿结论。
        /// 这是审计工具唯一无法自证的一类失效，必须在调用侧显式拦下。
        /// </summary>
        public static SyntaxTree ParseTreeChecked(string sourceCode, out List<string> errors)
        {
            errors = new List<string>();
            SyntaxTree tree = ParseTree(sourceCode);
            if (tree == null)
            {
                errors.Add("解析器返回空语法树");
                return null;
            }

            foreach (Diagnostic diagnostic in tree.GetDiagnostics())
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error) continue;
                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                errors.Add($"L{span.StartLinePosition.Line + 1}:C{span.StartLinePosition.Character + 1} {diagnostic.Id} {diagnostic.GetMessage()}");
            }

            return tree;
        }

        /// <summary>
        /// 取类声明所在的完整命名空间（无命名空间返回空串）。
        /// 用于跨命名空间同名类的消歧：优先用全限定名匹配基类，而不是"后声明者胜出"。
        /// </summary>
        public static string GetNamespaceName(ClassDeclarationSyntax classDecl)
        {
            if (classDecl == null) return string.Empty;

            var parts = new List<string>();
            for (SyntaxNode current = classDecl.Parent; current != null; current = current.Parent)
            {
                if (current is NamespaceDeclarationSyntax ns) parts.Insert(0, ns.Name.ToString());
                else if (current is FileScopedNamespaceDeclarationSyntax fns) parts.Insert(0, fns.Name.ToString());
            }
            return string.Join(".", parts);
        }

        /// <summary>
        /// 取基类型列表的"全限定写法"（原样返回源码里写的限定名，未限定则不补命名空间）。
        /// 与 GetBaseTypeNames 一一对应同序，供消歧时按索引比对。
        /// </summary>
        public static List<string> GetQualifiedBaseTypeNames(ClassDeclarationSyntax classDecl)
        {
            var list = new List<string>();
            if (classDecl?.BaseList == null) return list;

            foreach (var baseType in classDecl.BaseList.Types)
            {
                string raw = baseType.Type?.ToString();
                if (!string.IsNullOrEmpty(raw)) list.Add(raw.Trim());
            }
            return list;
        }

        /// <summary>
        /// 获取语法节点的 1 起起始行号
        /// </summary>
        public static int GetLine(SyntaxNode node)
        {
            if (node == null) return 0;
            return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        }

        /// <summary>
        /// 获取语法 Token 的 1 起起始行号
        /// </summary>
        public static int GetLine(SyntaxToken token)
        {
            return token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        }

        /// <summary>
        /// 获取语法节点的位置字符串 (例如 "L12:C5")
        /// </summary>
        public static string GetLocationString(SyntaxNode node)
        {
            if (node == null) return string.Empty;
            var pos = node.GetLocation().GetLineSpan().StartLinePosition;
            return $"L{pos.Line + 1}:C{pos.Character + 1}";
        }

        /// <summary>
        /// 获取类型的简短未限定名称 (消除命名空间和泛型参数影响)
        /// </summary>
        public static string GetSimpleTypeName(TypeSyntax typeSyntax)
        {
            if (typeSyntax == null) return string.Empty;

            switch (typeSyntax)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.Text;
                case QualifiedNameSyntax q:
                    return q.Right.Identifier.Text;
                case GenericNameSyntax g:
                    return g.Identifier.Text;
                case PredefinedTypeSyntax p:
                    return p.Keyword.Text;
                case NullableTypeSyntax n:
                    return GetSimpleTypeName(n.ElementType);
                default:
                    return typeSyntax.ToString().Split('.').Last().Split('<').First().Trim();
            }
        }

        /// <summary>
        /// 获取特性的简短名称 (去除可选的 "Attribute" 后缀)
        /// </summary>
        public static string GetAttributeName(AttributeSyntax attr)
        {
            if (attr == null) return string.Empty;
            string raw = attr.Name.ToString().Split('.').Last();
            if (raw.EndsWith("Attribute", StringComparison.Ordinal) && raw.Length > 9)
            {
                return raw.Substring(0, raw.Length - 9);
            }
            return raw;
        }

        /// <summary>
        /// 获取类声明所直接继承或实现的所有基类型/接口简名列表
        /// </summary>
        public static List<string> GetBaseTypeNames(ClassDeclarationSyntax classDecl)
        {
            var list = new List<string>();
            if (classDecl?.BaseList == null) return list;

            foreach (var baseType in classDecl.BaseList.Types)
            {
                string name = GetSimpleTypeName(baseType.Type);
                if (!string.IsNullOrEmpty(name))
                {
                    list.Add(name);
                }
            }
            return list;
        }

        /// <summary>
        /// 判断类声明是否具有指定的修饰符 (如 SyntaxKind.AbstractKeyword)
        /// </summary>
        public static bool HasModifier(ClassDeclarationSyntax classDecl, SyntaxKind kind)
        {
            if (classDecl == null) return false;
            return classDecl.Modifiers.Any(m => m.IsKind(kind));
        }

        /// <summary>
        /// 判断方法声明是否具有指定的修饰符 (如 SyntaxKind.OverrideKeyword)
        /// </summary>
        public static bool HasModifier(MethodDeclarationSyntax methodDecl, SyntaxKind kind)
        {
            if (methodDecl == null) return false;
            return methodDecl.Modifiers.Any(m => m.IsKind(kind));
        }

        /// <summary>
        /// 判断属性声明是否具有指定的修饰符 (如 SyntaxKind.OverrideKeyword)
        /// </summary>
        public static bool HasModifier(PropertyDeclarationSyntax propDecl, SyntaxKind kind)
        {
            if (propDecl == null) return false;
            return propDecl.Modifiers.Any(m => m.IsKind(kind));
        }

        /// <summary>
        /// 取类声明上指定简名的特性 (如 "FlightWidget" / "Obsolete")；不存在返回 null
        /// </summary>
        public static AttributeSyntax GetAttribute(ClassDeclarationSyntax classDecl, string simpleName)
        {
            if (classDecl == null || string.IsNullOrEmpty(simpleName)) return null;
            return classDecl.AttributeLists.SelectMany(al => al.Attributes)
                .FirstOrDefault(a => GetAttributeName(a) == simpleName);
        }

        /// <summary>
        /// 类声明上是否存在指定简名的特性
        /// </summary>
        public static bool HasAttribute(ClassDeclarationSyntax classDecl, string simpleName) =>
            GetAttribute(classDecl, simpleName) != null;

        /// <summary>
        /// 特性上是否把指定命名参数显式写成了 true (如 [FlightWidget(..., HighFrequency = true)])
        /// </summary>
        public static bool HasTrueNamedArgument(AttributeSyntax attr, string argumentName)
        {
            if (attr == null || string.IsNullOrEmpty(argumentName) || attr.ArgumentList == null) return false;

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals == null || arg.NameEquals.Name.Identifier.Text != argumentName) continue;
                var value = arg.Expression as LiteralExpressionSyntax;
                if (value != null && value.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
            }
            return false;
        }

        /// <summary>
        /// 取类成员中指定名字的属性声明（仅本类直接成员，不含继承）
        /// </summary>
        public static PropertyDeclarationSyntax GetProperty(ClassDeclarationSyntax classDecl, string name)
        {
            if (classDecl == null || string.IsNullOrEmpty(name)) return null;
            return classDecl.Members.OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(p => p.Identifier.Text == name);
        }

        /// <summary>
        /// 取类成员中指定名字的方法声明（仅本类直接成员，不含继承）
        /// </summary>
        public static MethodDeclarationSyntax GetMethod(ClassDeclarationSyntax classDecl, string name)
        {
            if (classDecl == null || string.IsNullOrEmpty(name)) return null;
            return classDecl.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == name);
        }

        /// <summary>
        /// 判断方法是否为"public override + 单一指定类型参数"的契约实现形状
        /// </summary>
        public static bool IsPublicOverrideWithSingleParam(MethodDeclarationSyntax method, string parameterTypeSimpleName)
        {
            if (method == null) return false;
            if (!HasModifier(method, SyntaxKind.PublicKeyword)) return false;
            if (!HasModifier(method, SyntaxKind.OverrideKeyword)) return false;

            var parameters = method.ParameterList.Parameters;
            return parameters.Count == 1
                && GetSimpleTypeName(parameters[0].Type) == parameterTypeSimpleName;
        }

        /// <summary>
        /// 收集属性体内实际返回的成员名列表（仅取语法结构，不受注释 / 字符串影响）。
        /// 覆盖表达式体 (=> X) 与访问器体 (get { return X; })，用于阶梯取值的结构判定。
        /// </summary>
        public static List<string> CollectReturnedMemberNames(PropertyDeclarationSyntax propDecl)
        {
            var result = new List<string>();
            if (propDecl == null) return result;

            var returned = new List<ExpressionSyntax>();
            if (propDecl.ExpressionBody != null)
            {
                returned.Add(propDecl.ExpressionBody.Expression);
            }
            if (propDecl.AccessorList != null)
            {
                returned.AddRange(propDecl.AccessorList.Accessors
                    .SelectMany(a => a.DescendantNodes().OfType<ReturnStatementSyntax>())
                    .Select(r => r.Expression)
                    .Where(e => e != null));
            }

            for (int i = 0; i < returned.Count; i++)
            {
                foreach (var ma in returned[i].DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
                {
                    string name = ma.Name.Identifier.Text;
                    if (!string.IsNullOrEmpty(name) && !result.Contains(name)) result.Add(name);
                }
            }
            return result;
        }
    }
}
