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
        /// <summary>
        /// 审计统一解析选项 —— 全仓库唯一的 C# 解析口径。
        ///
        /// 【必须共用】语义编译（SemanticCompilationProvider.BuildCompilation）与语法回退解析
        /// 都要用本实例。历史缺陷：语义编译自建了一套不含 KSP_RUNTIME 的 CSharpParseOptions，
        /// 结果 `#if` 条件编译把两条路径的可见代码集切开了 ——
        /// 语义树看不到 `#if KSP_RUNTIME` 内的代码，却看到了 `#if !KSP_RUNTIME` 内的 Vector2 测试垫片。
        /// </summary>
        public static CSharpParseOptions UnifiedParseOptions { get; } =
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(WidgetSpecRules.RuntimePreprocessorSymbol);

        /// <summary>
        /// 将 C# 源码解析为 Roslyn 语法树（默认已穿透激活 KSP_RUNTIME 宏）
        /// </summary>
        public static SyntaxTree ParseTree(string sourceCode)
        {
            return CSharpSyntaxTree.ParseText(sourceCode ?? string.Empty, UnifiedParseOptions);
        }

        /// <summary>
        /// 将 C# 源码解析为 Roslyn 语法树（支持自定义预编译宏符号）
        /// </summary>
        public static SyntaxTree ParseTree(string sourceCode, params string[] preprocessorSymbols)
        {
            var options = preprocessorSymbols != null && preprocessorSymbols.Length > 0
                ? CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithPreprocessorSymbols(preprocessorSymbols)
                : UnifiedParseOptions;
            return CSharpSyntaxTree.ParseText(sourceCode ?? string.Empty, options);
        }

        /// <summary>
        /// 以统一口径解析指定路径的源码（语义编译建树必须走这里，保证与回退树的可见代码集一致）
        /// </summary>
        public static SyntaxTree ParseTree(string sourceCode, string filePath)
        {
            return CSharpSyntaxTree.ParseText(sourceCode ?? string.Empty, UnifiedParseOptions, path: filePath);
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
                if (current is NamespaceDeclarationSyntax ns) parts.Add(ns.Name.ToString().Trim());
                else if (current is FileScopedNamespaceDeclarationSyntax fns) parts.Add(fns.Name.ToString().Trim());
            }
            if (parts.Count == 0) return string.Empty;
            parts.Reverse();
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
        /// 从语法树提取所有命名空间 using 指令及类型别名映射
        /// </summary>
        public static void ExtractUsingDirectives(SyntaxNode root, out List<string> importedNamespaces, out Dictionary<string, string> aliases)
        {
            importedNamespaces = new List<string>();
            aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root == null) return;

            var usings = root.DescendantNodesAndSelf().OfType<UsingDirectiveSyntax>();
            foreach (var u in usings)
            {
                if (u.Alias != null)
                {
                    string aliasName = u.Alias.Name.Identifier.ValueText;
                    string target = u.Name.ToString().Trim();
                    aliases[aliasName] = target;
                }
                else if (u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    // using static Xxx;
                }
                else
                {
                    string ns = u.Name.ToString().Trim();
                    if (!importedNamespaces.Contains(ns))
                    {
                        importedNamespaces.Add(ns);
                    }
                }
            }
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
                case ArrayTypeSyntax a:
                    return GetSimpleTypeName(a.ElementType) + "[]";
                case TupleTypeSyntax t:
                    return "(" + string.Join(", ", t.Elements.Select(e => GetSimpleTypeName(e.Type))) + ")";
                case PointerTypeSyntax pt:
                    return GetSimpleTypeName(pt.ElementType) + "*";
                default:
                    string str = typeSyntax.ToString();
                    int dot = str.LastIndexOf('.');
                    if (dot >= 0) str = str.Substring(dot + 1);
                    int angle = str.IndexOf('<');
                    if (angle >= 0) str = str.Substring(0, angle);
                    return str.Trim();
            }
        }

        /// <summary>
        /// 从表达式中安全解构出最右侧的未限定标识符（例如 从 a.b.MyMethod 中提取 MyMethod，不受注释、空白或换行影响）
        /// </summary>
        public static string GetRightmostIdentifier(ExpressionSyntax expr)
        {
            if (expr == null) return string.Empty;
            switch (expr)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                case GenericNameSyntax g:
                    return g.Identifier.ValueText;
                case MemberAccessExpressionSyntax ma:
                    return ma.Name.Identifier.ValueText;
                case MemberBindingExpressionSyntax mb:
                    return mb.Name.Identifier.ValueText;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 提取调用表达式的目标方法名（无论是直接调用 M() 还是成员访问 a.M()、a?.M()）
        /// </summary>
        public static string GetInvokedMethodName(InvocationExpressionSyntax inv)
        {
            if (inv == null) return string.Empty;
            return GetRightmostIdentifier(inv.Expression);
        }

        /// <summary>
        /// 获取调用的接收者表达式（例如 expr.Method() 中的 expr；若为裸调用或条件访问则返回对应接收者）
        /// </summary>
        public static ExpressionSyntax GetInvocationReceiver(InvocationExpressionSyntax inv)
        {
            if (inv == null) return null;
            if (inv.Expression is MemberAccessExpressionSyntax ma)
            {
                return ma.Expression;
            }
            if (inv.Expression is MemberBindingExpressionSyntax && inv.Parent is ConditionalAccessExpressionSyntax ca)
            {
                return ca.Expression;
            }
            return null;
        }

        /// <summary>
        /// 结构化匹配方法调用：比对 methodName，若指定 receiverTypeSimpleName 则进一步比对接收者最右侧标识符
        /// </summary>
        public static bool MatchesInvocation(InvocationExpressionSyntax inv, string receiverTypeSimpleName, string methodName)
        {
            if (inv == null) return false;
            if (GetInvokedMethodName(inv) != methodName) return false;
            if (string.IsNullOrEmpty(receiverTypeSimpleName)) return true;

            var receiver = GetInvocationReceiver(inv);
            if (receiver == null) return false;
            return GetRightmostIdentifier(receiver) == receiverTypeSimpleName;
        }

        /// <summary>
        /// 结构化匹配成员访问（属性/字段）：比对 memberName，若指定 receiverTypeSimpleName 则比对接收者最右侧标识符
        /// </summary>
        public static bool MatchesMemberAccess(MemberAccessExpressionSyntax ma, string receiverTypeSimpleName, string memberName)
        {
            if (ma == null) return false;
            if (ma.Name.Identifier.ValueText != memberName) return false;
            if (string.IsNullOrEmpty(receiverTypeSimpleName)) return true;
            return GetRightmostIdentifier(ma.Expression) == receiverTypeSimpleName;
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
        public static bool IsPublicOverrideWithSingleParam(MethodDeclarationSyntax method, string parameterTypeSimpleName, SyntaxKind? expectedModifier = null)
        {
            if (method == null) return false;
            if (!HasModifier(method, SyntaxKind.PublicKeyword)) return false;
            if (!HasModifier(method, SyntaxKind.OverrideKeyword)) return false;

            var parameters = method.ParameterList.Parameters;
            if (parameters.Count != 1) return false;
            if (expectedModifier.HasValue && !parameters[0].Modifiers.Any(m => m.IsKind(expectedModifier.Value)))
                return false;
            return GetSimpleTypeName(parameters[0].Type) == parameterTypeSimpleName;
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

        public struct AstMatchResult
        {
            public SyntaxNode Node;
            public string MatchedText;
            public AstMatchResult(SyntaxNode node, string matchedText)
            {
                Node = node;
                MatchedText = matchedText;
            }
        }

        /// <summary>
        /// 判定指定语法节点是否属于遥测数据采样/物理量计算表达式
        /// </summary>
        public static bool IsTelemetryDataExpression(SyntaxNode node, out string matchedText)
        {
            matchedText = null;
            if (node == null) return false;

            if (node is MemberAccessExpressionSyntax ma)
            {
                string text = ma.ToString();
                if (text.StartsWith("telemetry.", StringComparison.Ordinal) ||
                    text.StartsWith("_telemetry.", StringComparison.Ordinal) ||
                    text.StartsWith("context.Telemetry.", StringComparison.Ordinal) ||
                    text.StartsWith("ctx.Telemetry.", StringComparison.Ordinal) ||
                    text.StartsWith("FlightGlobals.", StringComparison.Ordinal) ||
                    text.StartsWith("ExternalProbeRegistry.", StringComparison.Ordinal) ||
                    text.StartsWith("TelemetryProbeManager.", StringComparison.Ordinal) ||
                    text.StartsWith("TelemetryTokenEngine.", StringComparison.Ordinal) ||
                    text.StartsWith("Planetarium.", StringComparison.Ordinal))
                {
                    matchedText = text;
                    return true;
                }
            }
            else if (node is InvocationExpressionSyntax inv)
            {
                string expr = inv.Expression.ToString();
                if (expr.EndsWith("EvalNumeric", StringComparison.Ordinal) ||
                    expr.EndsWith("EvalToken", StringComparison.Ordinal) ||
                    expr.Contains("GetTemplateChannel") ||
                    expr.EndsWith("TryGetPublishedChannel", StringComparison.Ordinal) ||
                    expr.EndsWith("ResolveNumeric", StringComparison.Ordinal) ||
                    expr.EndsWith("ResolveString", StringComparison.Ordinal) ||
                    expr.EndsWith("IsProbeTagAvailable", StringComparison.Ordinal))
                {
                    matchedText = expr + "(...)";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判定指定语法节点是否属于 UI 绘制、文本更新或材质/动效渲染表达式
        /// </summary>
        public static bool IsUIDrawExpression(SyntaxNode node, out string matchedText)
        {
            matchedText = null;
            if (node == null) return false;

            if (node is AssignmentExpressionSyntax assign && assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                string leftText = assign.Left.ToString();
                if (leftText.EndsWith(".Text", StringComparison.Ordinal) ||
                    leftText.EndsWith(".text", StringComparison.Ordinal) ||
                    leftText.EndsWith(".color", StringComparison.Ordinal) ||
                    leftText.EndsWith(".fillAmount", StringComparison.Ordinal) ||
                    leftText.EndsWith(".material", StringComparison.Ordinal) ||
                    leftText.EndsWith(".anchoredPosition", StringComparison.Ordinal) ||
                    leftText.EndsWith(".sizeDelta", StringComparison.Ordinal) ||
                    leftText.EndsWith(".localScale", StringComparison.Ordinal) ||
                    leftText.EndsWith(".localEulerAngles", StringComparison.Ordinal) ||
                    leftText.EndsWith(".localRotation", StringComparison.Ordinal))
                {
                    matchedText = leftText + " = ...";
                    return true;
                }
            }
            else if (node is InvocationExpressionSyntax inv)
            {
                string expr = inv.Expression.ToString();
                if (expr.EndsWith("SetText", StringComparison.Ordinal) ||
                    expr.EndsWith("SetTextSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetTextIfChanged", StringComparison.Ordinal) ||
                    expr.EndsWith("SetRole", StringComparison.Ordinal) ||
                    expr.EndsWith("SetColor", StringComparison.Ordinal) ||
                    expr.EndsWith("SetAlpha", StringComparison.Ordinal) ||
                    expr.EndsWith("SetFillAmountSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetImageFillIfChanged", StringComparison.Ordinal) ||
                    expr.EndsWith("SetBarFill", StringComparison.Ordinal) ||
                    expr.EndsWith(".SetFloat", StringComparison.Ordinal) ||
                    expr.EndsWith(".SetColor", StringComparison.Ordinal) ||
                    expr.EndsWith(".SetVector", StringComparison.Ordinal) ||
                    expr.EndsWith(".SetTexture", StringComparison.Ordinal) ||
                    expr.EndsWith("ApplyUiMaterial", StringComparison.Ordinal) ||
                    expr.EndsWith("GetUiMaterial", StringComparison.Ordinal) ||
                    expr.EndsWith("ApplyCard", StringComparison.Ordinal) ||
                    expr.EndsWith("ApplyText", StringComparison.Ordinal) ||
                    expr.EndsWith("ApplyButton", StringComparison.Ordinal) ||
                    expr.EndsWith("ApplyMeter", StringComparison.Ordinal) ||
                    expr.EndsWith("SetVerticesDirty", StringComparison.Ordinal) ||
                    expr.EndsWith("SetLayoutDirty", StringComparison.Ordinal) ||
                    expr.EndsWith("SetMaterialDirty", StringComparison.Ordinal) ||
                    expr.EndsWith("SetAllDirty", StringComparison.Ordinal) ||
                    expr.EndsWith("SetAnchoredPositionSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetSizeDeltaSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetLocalScaleSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetLocalRotationSafe", StringComparison.Ordinal) ||
                    expr.EndsWith("SetLocalEulerAnglesSafe", StringComparison.Ordinal) ||
                    expr.StartsWith("GL.", StringComparison.Ordinal) ||
                    expr.StartsWith("Graphics.Draw", StringComparison.Ordinal))
                {
                    matchedText = expr + "(...)";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从指定入口方法出发，沿类内方法调用图（Call Graph）递归收集所有可达的本类成员方法（含入口方法自身）。
        /// 专用于穿透审计包装在私有/内部 helper 方法中的违规操作。
        /// </summary>
        public static List<MethodDeclarationSyntax> CollectReachableLocalMethods(ClassDeclarationSyntax classDecl, MethodDeclarationSyntax entryMethod)
        {
            var result = new List<MethodDeclarationSyntax>();
            if (classDecl == null || entryMethod == null) return result;

            var methodsByName = new Dictionary<string, List<MethodDeclarationSyntax>>(StringComparer.Ordinal);
            foreach (var m in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                string name = m.Identifier.ValueText;
                if (!methodsByName.TryGetValue(name, out var list))
                {
                    list = new List<MethodDeclarationSyntax>();
                    methodsByName[name] = list;
                }
                list.Add(m);
            }

            var visited = new HashSet<MethodDeclarationSyntax>();
            var queue = new Queue<MethodDeclarationSyntax>();

            visited.Add(entryMethod);
            queue.Enqueue(entryMethod);
            result.Add(entryMethod);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var inv in current.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    string invokedName = GetInvokedMethodName(inv);
                    if (string.IsNullOrEmpty(invokedName)) continue;

                    // 仅对裸调用 DoHelper() 或 this.DoHelper() 追踪类内方法
                    var receiver = GetInvocationReceiver(inv);
                    if (receiver != null && !(receiver is ThisExpressionSyntax))
                    {
                        continue;
                    }

                    if (methodsByName.TryGetValue(invokedName, out var matchingMethods))
                    {
                        foreach (var target in matchingMethods)
                        {
                            if (visited.Add(target))
                            {
                                queue.Enqueue(target);
                                result.Add(target);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 扫描语法节点内出现的所有遥测数据采样/物理计算表达式
        /// </summary>
        public static List<AstMatchResult> FindTelemetryUpdateExpressions(SyntaxNode root)
        {
            var list = new List<AstMatchResult>();
            if (root == null) return list;

            foreach (var node in root.DescendantNodes())
            {
                if (IsTelemetryDataExpression(node, out string matched))
                {
                    list.Add(new AstMatchResult(node, matched));
                }
            }
            return list;
        }

        /// <summary>
        /// 扫描语法节点内出现的所有 UI 绘制与图元操作表达式
        /// </summary>
        public static List<AstMatchResult> FindUIDrawExpressions(SyntaxNode root)
        {
            var list = new List<AstMatchResult>();
            if (root == null) return list;

            foreach (var node in root.DescendantNodes())
            {
                if (IsUIDrawExpression(node, out string matched))
                {
                    list.Add(new AstMatchResult(node, matched));
                }
            }
            return list;
        }
    }
}
