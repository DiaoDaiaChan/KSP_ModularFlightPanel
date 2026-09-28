using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI
{
    using ModularFlightPanel.UI.Auditing;

    /// <summary>
    /// 组件继承图节点：一个类声明在审计模型中的全部结构事实（唯一事实来源）。
    /// </summary>
    public sealed class WidgetClassNode
    {
        public string Name;
        public string FileName;
        public string FilePath;
        public ClassDeclarationSyntax Decl;
        public List<string> BaseNames = new List<string>();

        /// <summary>与 BaseNames 同序：源码里的原始写法（可能带命名空间限定），用于同名类消歧</summary>
        public List<string> QualifiedBaseNames = new List<string>();

        /// <summary>完整命名空间（无命名空间为空串）</summary>
        public string Namespace = string.Empty;

        /// <summary>全限定类名（Namespace.Name）</summary>
        public string FullName = string.Empty;

        public WidgetClassNode Base;              // 解析后的直接基类（若声明在本次扫描集合内）

        public bool IsAbstract;
        public bool IsContractRoot;               // 契约根自身（BaseFlightWidget）
        public bool IsWidgetContractClass;        // 契约真后代（直接或间接派生自契约根）
        public bool IsObsoleteShim;               // 继承链上出现 [Obsolete] 的向后兼容垫片
        internal bool? DescendantCache;           // 契约归属判定缓存（链断裂时需沿已解析基类回溯）

        /// <summary>源文件通过 using 指令导入的命名空间列表，用于跨命名空间继承消歧</summary>
        public List<string> Usings = new List<string>();

        /// <summary>源文件通过 using 别名定义的类型映射 (别名 -> 目标类型名)</summary>
        public Dictionary<string, string> UsingAliases = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Roslyn 语义符号（当已构建 CSharpCompilation 时非空）</summary>
        public INamedTypeSymbol Symbol;

        /// <summary>所属语法树的语义模型（当已构建 CSharpCompilation 时非空）</summary>
        public SemanticModel SemanticModel;

        public bool HasMetadataAttribute;         // 声明了 [FlightWidget]
        public bool DeclaresHighFrequency;        // [FlightWidget(..., HighFrequency = true)]
        public PropertyDeclarationSyntax TierProperty;
        public MethodDeclarationSyntax ThemeMethod;
        public MethodDeclarationSyntax TelemetryMethod;
        public MethodDeclarationSyntax OnDestroyMethod;

        /// <summary>自身 → 直接基类 → … 的链（不含接口/未解析的外部基类）</summary>
        public IEnumerable<WidgetClassNode> SelfAndAncestors()
        {
            for (WidgetClassNode n = this; n != null; n = n.Base) yield return n;
        }

        public bool AnyInChain(Func<WidgetClassNode, bool> predicate)
        {
            foreach (var n in SelfAndAncestors())
            {
                if (predicate(n)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 继承链消歧失败记录。存在此类记录时，该类的 Base 会保持为 null，
    /// 依赖继承链的判定（SPEC-002/003/004/008）应视为不可靠并显式上报。
    /// </summary>
    public sealed class BaseResolutionIssue
    {
        public string FileName;
        public int Line;
        public string Description;

        public override string ToString() =>
            Line > 0 ? $"{FileName}:L{Line} {Description}" : $"{FileName} {Description}";
    }

    /// <summary>
    /// 组件继承图：全部规则判定都基于这份结构模型，而不是文件名 / 目录 / 文本包含关系。
    /// </summary>
    public sealed class WidgetClassGraph
    {
        public readonly List<WidgetClassNode> All = new List<WidgetClassNode>();

        /// <summary>全工程语义编译上下文（若激活则提供绝对权威的符号与类型决议）</summary>
        public SemanticCompilationContext SemanticContext;

        /// <summary>
        /// 类简名 → 全部同名声明。
        /// 旧实现是 Dictionary&lt;string, WidgetClassNode&gt;，跨命名空间同名类会"后声明者胜出"被静默覆盖，
        /// 导致继承链解析错位、依赖继承链的 SPEC-001/002/003/004/008 全部错判 —— 而合成用例只有单一
        /// 命名空间，永远测不到这条路径。现在保留全部候选，消歧交给 ResolveBase。
        /// </summary>
        public readonly Dictionary<string, List<WidgetClassNode>> ByName = new Dictionary<string, List<WidgetClassNode>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>全限定名 → 类节点（跨命名空间同名类消歧的唯一索引）</summary>
        public readonly Dictionary<string, WidgetClassNode> ByFullName = new Dictionary<string, WidgetClassNode>(StringComparer.Ordinal);

        /// <summary>出现多个同名声明的简名集合（报告用）</summary>
        public readonly HashSet<string> AmbiguousTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>继承链消歧失败记录：无法唯一确定基类时显式上报，绝不静默取一个</summary>
        public readonly List<BaseResolutionIssue> BaseResolutionIssues = new List<BaseResolutionIssue>();

        /// <summary>
        /// 直接基类由语义符号（Symbol.BaseType）决议成功的次数。
        /// 这是"语义点位 2 还在不在"的覆盖率指标：为 0 说明该点位已被拆除、继承链退化回语法消歧。
        /// </summary>
        public int SemanticResolvedBaseCount;

        /// <summary>
        /// 契约归属判定由语义分支（Symbol != null 时独占）给出的次数。
        /// 这是"语义点位 3 还在不在"的覆盖率指标。
        /// </summary>
        public int SemanticDescendantVerdictCount;

        public readonly Dictionary<string, CompilationUnitSyntax> Roots = new Dictionary<string, CompilationUnitSyntax>(StringComparer.OrdinalIgnoreCase);

        /// <summary>刷新阶梯合法取值集合 —— 直接从源码里的枚举声明派生（不再写死成员名）</summary>
        public readonly HashSet<string> TierMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>审计作用域：契约真后代中排除 [Obsolete] 兼容垫片</summary>
        public readonly List<WidgetClassNode> ContractClasses = new List<WidgetClassNode>();

        /// <summary>审计作用域文件：声明了作用域内组件的源文件（按内容判定，与目录位置无关）</summary>
        public readonly HashSet<string> ScopedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>作用域文件名集合（仅文件名，用于计数展示）</summary>
        public readonly HashSet<string> ScopedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static WidgetClassGraph Build(IList<WidgetSourceFile> files, SemanticCompilationContext semanticContext = null)
        {
            var graph = new WidgetClassGraph();
            graph.SemanticContext = semanticContext;
            if (files == null) return graph;

            // ── 1. 解析全部源文件并建立类节点 ──
            for (int i = 0; i < files.Count; i++)
            {
                WidgetSourceFile file = files[i];
                if (file == null || string.IsNullOrEmpty(file.Text)) continue;

                string fileKey = file.Path ?? file.Name ?? string.Empty;
                CompilationUnitSyntax root = semanticContext?.GetRoot(fileKey);
                if (root == null)
                {
                    root = RoslynAstHelper.ParseRoot(file.Text);
                }

                graph.Roots[fileKey] = root;
                graph.ExtractTierMembers(root);
                RoslynAstHelper.ExtractUsingDirectives(root, out List<string> fileUsings, out Dictionary<string, string> fileAliases);

                SemanticModel semanticModel = semanticContext?.GetSemanticModel(fileKey);

                foreach (var cd in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    string ns = RoslynAstHelper.GetNamespaceName(cd);
                    var node = new WidgetClassNode
                    {
                        Name = cd.Identifier.Text,
                        FileName = file.Name,
                        FilePath = file.Path,
                        Decl = cd,
                        BaseNames = RoslynAstHelper.GetBaseTypeNames(cd),
                        QualifiedBaseNames = RoslynAstHelper.GetQualifiedBaseTypeNames(cd),
                        Namespace = ns,
                        FullName = string.IsNullOrEmpty(ns) ? cd.Identifier.Text : ns + "." + cd.Identifier.Text,
                        IsAbstract = RoslynAstHelper.HasModifier(cd, SyntaxKind.AbstractKeyword),
                        IsContractRoot = string.Equals(cd.Identifier.Text, WidgetSpecRules.ContractRootType, StringComparison.Ordinal),
                        Usings = fileUsings,
                        UsingAliases = fileAliases,
                        SemanticModel = semanticModel
                    };

                    if (semanticModel != null)
                    {
                        node.Symbol = semanticModel.GetDeclaredSymbol(cd) as INamedTypeSymbol;
                    }

                    node.HasMetadataAttribute = RoslynAstHelper.HasAttribute(cd, WidgetSpecRules.MetadataAttribute);
                    node.DeclaresHighFrequency = node.HasMetadataAttribute &&
                        RoslynAstHelper.HasTrueNamedArgument(
                            RoslynAstHelper.GetAttribute(cd, WidgetSpecRules.MetadataAttribute),
                            WidgetSpecRules.HighFrequencyMetadata);

                    node.TierProperty = RoslynAstHelper.GetProperty(cd, WidgetSpecRules.TierProperty);
                    node.ThemeMethod = RoslynAstHelper.GetMethod(cd, WidgetSpecRules.ThemeMethod);
                    node.TelemetryMethod = RoslynAstHelper.GetMethod(cd, WidgetSpecRules.TelemetryMethod);
                    node.OnDestroyMethod = RoslynAstHelper.GetMethod(cd, WidgetSpecRules.LifecycleMethod);

                    graph.All.Add(node);
                    graph.AddToNameIndex(node);
                }
            }

            // ── 2. 解析继承链（优先走权威语义符号 BaseType；回退走语法消歧）──
            for (int i = 0; i < graph.All.Count; i++)
            {
                WidgetClassNode node = graph.All[i];

                // 2.1 语义符号精确决议直接基类
                if (node.Symbol?.BaseType != null)
                {
                    string baseFullName = node.Symbol.BaseType.ToDisplayString();
                    if (graph.ByFullName.TryGetValue(baseFullName, out WidgetClassNode resolvedBySymbol))
                    {
                        if (resolvedBySymbol != node)
                        {
                            node.Base = resolvedBySymbol;
                            graph.SemanticResolvedBaseCount++;
                            continue;
                        }
                    }
                }

                // 2.2 语法消歧回退
                for (int b = 0; b < node.BaseNames.Count; b++)
                {
                    WidgetClassNode resolved = graph.ResolveBase(node, b, out string issue);
                    if (issue != null)
                    {
                        graph.BaseResolutionIssues.Add(new BaseResolutionIssue
                        {
                            FileName = node.FileName,
                            Line = RoslynAstHelper.GetLine(node.Decl),
                            Description = issue
                        });
                        continue;
                    }
                    if (resolved != null && resolved != node)
                    {
                        node.Base = resolved;
                        break;
                    }
                }
            }

            // ── 3. 派生契约归属与兼容垫片标记（自根向下传播）──
            for (int i = 0; i < graph.All.Count; i++)
            {
                WidgetClassNode node = graph.All[i];
                node.IsWidgetContractClass = !node.IsContractRoot
                    && node.IsDescendantOfContractRoot(() => graph.SemanticDescendantVerdictCount++);
                node.IsObsoleteShim = node.AnyInChain(IsObsoleteClass);
            }

            // ── 4. 作用域 = 非垫片的契约真后代 ──
            for (int i = 0; i < graph.All.Count; i++)
            {
                WidgetClassNode node = graph.All[i];
                if (!node.IsWidgetContractClass || node.IsObsoleteShim) continue;
                graph.ContractClasses.Add(node);
                if (!string.IsNullOrEmpty(node.FileName))
                {
                    graph.ScopedFiles.Add(node.FileName);
                    graph.ScopedFileNames.Add(node.FileName);
                }
                if (!string.IsNullOrEmpty(node.FilePath)) graph.ScopedFiles.Add(node.FilePath);
            }

            return graph;
        }

        /// <summary>把类节点登记进简名索引与全限定名索引，并标记同名多候选</summary>
        private void AddToNameIndex(WidgetClassNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Name)) return;

            if (!ByName.TryGetValue(node.Name, out List<WidgetClassNode> list))
            {
                list = new List<WidgetClassNode>();
                ByName[node.Name] = list;
            }
            list.Add(node);
            if (list.Count > 1) AmbiguousTypeNames.Add(node.Name);

            if (!string.IsNullOrEmpty(node.FullName) && !ByFullName.ContainsKey(node.FullName))
            {
                ByFullName[node.FullName] = node;
            }
        }

        /// <summary>取某个基类名的全部候选声明</summary>
        public List<WidgetClassNode> CandidatesOf(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return new List<WidgetClassNode>();
            return ByName.TryGetValue(simpleTypeName, out List<WidgetClassNode> list)
                ? list
                : new List<WidgetClassNode>();
        }

        /// <summary>
        /// 解析 node 的第 baseIndex 个基类。返回 null 且 issue 为 null 表示"该基类声明在扫描集合之外"（正常，如 Unity 基类）。
        /// issue 非空表示同名多候选且无法唯一消歧 —— 调用方必须显式上报，不得静默取一个。
        /// </summary>
        private WidgetClassNode ResolveBase(WidgetClassNode node, int baseIndex, out string issue)
        {
            issue = null;
            if (node == null || baseIndex < 0 || baseIndex >= node.BaseNames.Count) return null;

            string simpleName = node.BaseNames[baseIndex];
            List<WidgetClassNode> candidates = CandidatesOf(simpleName);
            if (candidates.Count == 0) return null;                       // 声明在扫描集合外
            if (candidates.Count == 1) return candidates[0];

            // 同一类内自引用（部分类拆分声明）不算歧义
            List<WidgetClassNode> distinct = candidates.Where(c => !ReferenceEquals(c, node)).ToList();
            if (distinct.Count == 0) return node;
            if (distinct.Count == 1) return distinct[0];

            // 别名消歧：源码顶部 using AliasBase = Target.Full.Name;
            if (node.UsingAliases != null && node.UsingAliases.TryGetValue(simpleName, out string aliasedTarget))
            {
                WidgetClassNode aliasMatch = distinct.FirstOrDefault(c => string.Equals(c.FullName, aliasedTarget, StringComparison.Ordinal));
                if (aliasMatch != null) return aliasMatch;
            }

            // 多候选：优先用源码里的全限定写法消歧
            string qualified = baseIndex < node.QualifiedBaseNames.Count ? node.QualifiedBaseNames[baseIndex] : null;
            if (!string.IsNullOrEmpty(qualified))
            {
                List<WidgetClassNode> qualifiedMatches = distinct
                    .Where(c => string.Equals(c.FullName, qualified, StringComparison.Ordinal)
                             || c.FullName.EndsWith("." + qualified, StringComparison.Ordinal))
                    .ToList();
                if (qualifiedMatches.Count == 1) return qualifiedMatches[0];
            }

            // 再尝试用"候选自身所在命名空间与派生类相同"消歧（同命名空间优先）
            List<WidgetClassNode> sameNs = distinct
                .Where(c => string.Equals(c.Namespace, node.Namespace, StringComparison.Ordinal))
                .ToList();
            if (sameNs.Count == 1) return sameNs[0];

            // 再尝试通过文件顶部的 using 声明导入的命名空间消歧
            if (node.Usings != null && node.Usings.Count > 0)
            {
                List<WidgetClassNode> usingMatches = distinct
                    .Where(c => !string.IsNullOrEmpty(c.Namespace) && node.Usings.Contains(c.Namespace))
                    .ToList();
                if (usingMatches.Count == 1) return usingMatches[0];
            }

            issue = "基类名 '" + simpleName + "' 在扫描集合内有 "
                  + distinct.Count + " 个同名声明（"
                  + string.Join(" / ", distinct.Select(c => c.FullName).Distinct())
                  + "），无法唯一确定继承链。请改用全限定基类名，否则依赖继承链的规则判定不可靠";
            return null;
        }

        private static bool IsObsoleteClass(WidgetClassNode node) =>
            node.Decl != null && RoslynAstHelper.HasAttribute(node.Decl, WidgetSpecRules.CompatibilityShimAttribute);

        private void ExtractTierMembers(CompilationUnitSyntax root)
        {
            foreach (var ed in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                if (!string.Equals(ed.Identifier.Text, WidgetSpecRules.TierEnumType, StringComparison.Ordinal)) continue;
                foreach (var member in ed.Members)
                {
                    TierMembers.Add(member.Identifier.Text);
                }
            }
        }

        /// <summary>沿链向上寻找提供指定成员的最近声明者；契约根本身不算提供者（它的默认实现不是组件的合规实现）</summary>
        public static WidgetClassNode FindDeclarer(WidgetClassNode node, Func<WidgetClassNode, bool> declares)
        {
            foreach (var n in node.SelfAndAncestors())
            {
                if (n.IsContractRoot || n.Decl == null) continue;
                if (declares(n)) return n;
            }
            return null;
        }

        /// <summary>对象的生效刷新阶梯取值（来自最近的阶梯声明者）</summary>
        public IEnumerable<string> EffectiveTierMembers(WidgetClassNode node)
        {
            WidgetClassNode owner = FindDeclarer(node, n => n.TierProperty != null);
            return owner == null
                ? (IEnumerable<string>)new string[0]
                : RoslynAstHelper.CollectReturnedMemberNames(owner.TierProperty);
        }

        /// <summary>该组件（含继承链）是否通过声明式元数据声明了满帧刷新需求</summary>
        public static bool IsHighFrequencyDeclared(WidgetClassNode node) =>
            node != null && node.AnyInChain(n => n.DeclaresHighFrequency);
    }

    internal static class WidgetClassNodeExtensions
    {
        /// <summary>
        /// 契约真后代判定：优先沿已解析的继承链回溯；链断裂（基类未在扫描集合内）时回退到基类名匹配，
        /// 并沿"已解析到的基类"继续向上递归，使"只扫描局部文件"的合成场景同样成立。
        ///
        /// onSemanticVerdict：语义分支（点位 3）被采用时的回调，仅用于覆盖率计数 ——
        /// 语义结论与语法回退结论可能恰好相同，所以"结果对不对"测不出这条点位是否还在，
        /// 必须靠"被采用过"来测。
        /// </summary>
        public static bool IsDescendantOfContractRoot(this WidgetClassNode node, Action onSemanticVerdict = null)
        {
            if (node == null) return false;
            if (node.DescendantCache.HasValue) return node.DescendantCache.Value;

            // 1. 若拥有语义符号，直接使用语义模型进行绝对权威判定
            if (node.Symbol != null)
            {
                onSemanticVerdict?.Invoke();
                bool semanticResult = SemanticCompilationProvider.InheritsFrom(node.Symbol, "ModularFlightPanel.UI." + WidgetSpecRules.ContractRootType)
                                   || SemanticCompilationProvider.InheritsFrom(node.Symbol, WidgetSpecRules.ContractRootType);
                node.DescendantCache = semanticResult;
                return semanticResult;
            }

            // 2. 否则降级回退到 AST 继承链回溯
            node.DescendantCache = false;   // 防环护栏：循环继承按非组件处理
            bool result = false;

            foreach (var n in node.SelfAndAncestors())
            {
                if (n != node && n.IsContractRoot) { result = true; break; }
            }

            if (!result)
            {
                for (int i = 0; i < node.BaseNames.Count; i++)
                {
                    if (string.Equals(node.BaseNames[i], WidgetSpecRules.ContractRootType, StringComparison.Ordinal))
                    {
                        result = true;
                        break;
                    }
                }
            }

            if (!result && node.Base != null && node.Base != node)
            {
                result = IsDescendantOfContractRoot(node.Base, onSemanticVerdict);
            }

            node.DescendantCache = result;
            return result;
        }
    }

    /// <summary>发现层状态：审计范围不可用 / 不可信时绝不允许静默放行</summary>
    public enum WidgetDiscoveryStatus
    {
        /// <summary>扫描成功，作用域内存在组件</summary>
        Ok,

        /// <summary>仓库根不可解析（发布版插件只有 DLL）——属于可预期的环境降级</summary>
        RepositoryRootUnresolved,

        /// <summary>仓库根存在但源码目录缺失</summary>
        SourceScopeMissing,

        /// <summary>部分源文件读取失败（扫描集合不完整）</summary>
        SourceReadFailed,

        /// <summary>
        /// 部分源文件解析失败（语法错误 → 类声明从语法树中丢失，审计作用域不完整）。
        /// 这一状态是审计工具唯一无法自证的一类失效：坏语法会让组件"凭空消失"，
        /// 若放行就会得到"零违规"的全绿结论。因此它与读取失败同级，一律判 ERROR。
        /// </summary>
        SourceParseFailed,

        /// <summary>扫描完成但未发现任何组件类</summary>
        NoWidgetClasses
    }

    /// <summary>发现层结果：源集合 + 继承图 + 作用域 + 状态说明</summary>
    public sealed class WidgetDiscoveryResult
    {
        public WidgetDiscoveryStatus Status = WidgetDiscoveryStatus.Ok;
        public string Detail = string.Empty;
        public WidgetClassGraph Graph;
        public SemanticCompilationContext SemanticContext;
        public readonly List<WidgetSourceFile> Sources = new List<WidgetSourceFile>();
        public readonly List<string> ReadErrors = new List<string>();

        /// <summary>被排除在语义编译之外的"仅无头侧编译"审计文件数（用于证明编译集与插件一致）</summary>
        public int PluginExcludedFileCount;

        /// <summary>语法级失败明细（"文件 行:列 错误码 信息"）；非空即代表审计作用域不可信</summary>
        public readonly List<string> ParseErrors = new List<string>();

        public bool CanAuditSource => Status == WidgetDiscoveryStatus.Ok;

        /// <summary>是否属于"发布环境无源码"的可降级场景（游戏内判 WARNING，CI 仍判 ERROR）</summary>
        public bool IsEnvironmentDegradation => Status == WidgetDiscoveryStatus.RepositoryRootUnresolved;

        public int WidgetClassCount => Graph?.ContractClasses.Count ?? 0;
        public int ScopedFileCount => Graph?.ScopedFileNames.Count ?? 0;
    }

    /// <summary>
    /// 组件源码规范审计内核 (WidgetSourceAudit - Roslyn AST + 继承图驱动)
    ///
    /// 【架构要点】规则判定只依赖两样东西：
    ///   1. WidgetSpecRules 里的契约锚点 / 黑名单表（唯一数据源）；
    ///   2. 本文件构建的组件继承图（按内容发现，与目录位置无关）。
    /// 因此不存在"按文件名特判""按目录决定扫谁""按下限数字兜底"这类硬编码判断。
    /// </summary>
    public static class WidgetSourceAudit
    {
        /// <summary>仓库根定位：从程序集路径向上寻找插件源码目录（发布环境必然失败 → 由发现层状态显式上报）</summary>
        public static string ResolveRepositoryRoot()
        {
            try
            {
                string asmPath = typeof(WidgetSourceAudit).Assembly.Location;
                if (string.IsNullOrEmpty(asmPath)) return null;

                var dir = new DirectoryInfo(Path.GetDirectoryName(asmPath) ?? string.Empty);
                for (int i = 0; i < WidgetSpecRules.RepositoryRootProbeDepth && dir != null; i++)
                {
                    string probe = Path.Combine(dir.FullName,
                        WidgetSpecRules.RepositoryRootProbeRelative.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(probe)) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 扫描仓库内的全部插件源码并建立组件继承图。
        /// 作用域由"类的继承关系"决定而不是目录：组件被移动到任何目录都不会脱离审计。
        /// </summary>
        public static WidgetDiscoveryResult Discover(string repoRoot)
        {
            var result = new WidgetDiscoveryResult();

            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
            {
                result.Status = WidgetDiscoveryStatus.RepositoryRootUnresolved;
                result.Detail = "无法定位仓库源码根 (repoRoot = " + (repoRoot ?? "null") + ")";
                return result;
            }

            string sourceRoot = Path.Combine(repoRoot, WidgetSpecRules.SourceRootFolder, WidgetSpecRules.PluginProjectFolder);
            if (!Directory.Exists(sourceRoot))
            {
                result.Status = WidgetDiscoveryStatus.SourceScopeMissing;
                result.Detail = "源码目录不存在: " + sourceRoot;
                return result;
            }

            foreach (string path in SafeEnumerateSourceFiles(sourceRoot, "*.cs", result.ReadErrors))
            {
                if (IsBuildArtifactPath(path)) continue;

                // 语义编译集必须等于插件的真实编译集：这些文件只由无头验证器编译（csproj 的 <Compile Remove>），
                // 它们引用 Microsoft.CodeAnalysis，而 KSP Managed 目录不提供该程序集。
                // 混进来会退化为错误类型并污染整张类型图（历史实测 253 处 CS0246）。
                if (WidgetSpecRules.IsPluginExcludedAuditFile(path))
                {
                    result.PluginExcludedFileCount++;
                    continue;
                }
                try
                {
                    string text = File.ReadAllText(path);
                    result.Sources.Add(new WidgetSourceFile
                    {
                        Name = Path.GetFileName(path),
                        Path = path,
                        Text = text
                    });

                    // 语法级守卫：坏语法会让 ClassDeclarationSyntax 从语法树中消失，
                    // 组件随之脱离审计作用域，报告会给出"零违规"的全绿结论。
                    // 注意这里只取语法诊断（不建 Compilation），因此"找不到类型/缺少引用"
                    // 这类语义错误不会误触发，只有真正的语法损坏才会进入 ParseErrors。
                    RoslynAstHelper.ParseTreeChecked(text, out List<string> parseErrors);
                    for (int i = 0; i < parseErrors.Count; i++)
                    {
                        result.ParseErrors.Add(Path.GetFileName(path) + " " + parseErrors[i]);
                    }
                }
                catch (Exception ex)
                {
                    result.ReadErrors.Add("读取失败 " + path + ": " + ex.Message);
                }
            }

            result.Sources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            
            // 构建全量 CSharpCompilation 并链接 Unity 与 KSP 程序集
            result.SemanticContext = SemanticCompilationProvider.BuildCompilation(result.Sources, repoRoot);
            result.Graph = WidgetClassGraph.Build(result.Sources, result.SemanticContext);

            if (result.ReadErrors.Count > 0)
            {
                result.Status = WidgetDiscoveryStatus.SourceReadFailed;
                result.Detail = result.ReadErrors.Count + " 个源文件读取失败，扫描集合不完整";
            }
            else if (result.ParseErrors.Count > 0)
            {
                // 解析失败与读取失败同级：扫描集合不可信时绝不允许给出合规结论。
                result.Status = WidgetDiscoveryStatus.SourceParseFailed;
                result.Detail = result.ParseErrors.Count + " 处语法解析失败，类声明可能从语法树中丢失，"
                              + "审计作用域不可信（首例: " + result.ParseErrors[0] + "）";
            }
            else if (result.Graph.ContractClasses.Count == 0)
            {
                result.Status = WidgetDiscoveryStatus.NoWidgetClasses;
                result.Detail = "扫描 " + result.Sources.Count + " 个源文件后未发现任何组件类（继承契约根 " + WidgetSpecRules.ContractRootType + " 的后代）";
            }
            else
            {
                result.Status = WidgetDiscoveryStatus.Ok;
            }

            return result;
        }

        private static IEnumerable<string> SafeEnumerateSourceFiles(string root, string pattern, List<string> readErrors)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] subDirs = null;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    readErrors?.Add("枚举目录失败 " + current + ": " + ex.Message);
                }

                if (subDirs != null)
                {
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        if (!IsBuildArtifactPath(subDirs[i]))
                        {
                            stack.Push(subDirs[i]);
                        }
                    }
                }

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(current, pattern);
                }
                catch (Exception ex)
                {
                    readErrors?.Add("枚举文件失败 " + current + ": " + ex.Message);
                }

                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        yield return files[i];
                    }
                }
            }
        }

        private static bool IsBuildArtifactPath(string path)
        {
            string norm = path.Replace('\\', '/');
            for (int i = 0; i < WidgetSpecRules.BuildArtifactPathFragments.Count; i++)
            {
                if (norm.Contains(WidgetSpecRules.BuildArtifactPathFragments[i])) return true;
            }
            return false;
        }

        /// <summary>主入口：对发现层结果执行 SPEC-001..008 全量审计</summary>
        public static WidgetSourceAuditReport Scan(WidgetDiscoveryResult discovery)
        {
            if (discovery == null || discovery.Graph == null) return new WidgetSourceAuditReport();
            return Scan(discovery.Graph, discovery.Sources);
        }

        /// <summary>自检入口：对任意合成文件集合执行同一套规则</summary>
        public static WidgetSourceAuditReport Scan(IEnumerable<WidgetSourceFile> files)
        {
            var list = (files ?? Enumerable.Empty<WidgetSourceFile>()).ToList();
            return Scan(WidgetClassGraph.Build(list), list);
        }

        /// <summary>语义通道入口：允许显式传入语义编译上下文（自检用；门禁侧走 Scan(discovery) 已含上下文）</summary>
        public static WidgetSourceAuditReport Scan(IEnumerable<WidgetSourceFile> files, SemanticCompilationContext semanticContext)
        {
            var list = (files ?? Enumerable.Empty<WidgetSourceFile>()).ToList();
            return Scan(WidgetClassGraph.Build(list, semanticContext), list);
        }

        private static WidgetSourceAuditReport Scan(WidgetClassGraph graph, IList<WidgetSourceFile> files)
        {
            var report = new WidgetSourceAuditReport { WidgetsScanned = graph.ContractClasses.Count };

            // 覆盖率计数：语义点位 1（每类 GetDeclaredSymbol → node.Symbol）被采用。
            // 为 0 = 语义上下文缺失或该点位被拆除，L4 判定已退化为纯语法。
            for (int i = 0; i < graph.All.Count; i++)
            {
                if (graph.All[i].Symbol != null) report.SemanticVerdictCount++;
            }

            // ── 判定依据自检：存在组件时，阶梯枚举必须能从源码派生，否则取值校验会退化为"全部非法" ──
            if (graph.ContractClasses.Count > 0 && graph.TierMembers.Count == 0)
            {
                report.Violations.Add(new WidgetSourceViolation
                {
                    FileName = WidgetSpecRules.KernelReportName,
                    RuleCode = WidgetSpecRules.RefreshTier,
                    Severity = "ERROR",
                    Line = 0,
                    Description = "无法从源码定位枚举 " + WidgetSpecRules.TierEnumType + " 的声明，刷新阶梯取值校验无法执行"
                });
            }

            // ── 内核级守卫 1：继承链消歧失败必须显式上报 ──
            // 否则依赖继承链的 SPEC-001/002/003/004/008 会基于错位的基类给出看似合理的结论。
            for (int i = 0; i < graph.BaseResolutionIssues.Count; i++)
            {
                BaseResolutionIssue issue = graph.BaseResolutionIssues[i];
                Add(report, issue.FileName, WidgetSpecRules.KernelInheritanceAmbiguity, "ERROR", issue.Line, issue.Description);
            }

            // ── 内核级守卫 2：微控件构造函数签名表必须与真实源码声明一致 ──
            // 这是把"重构构造函数后审计规则静默翻转"变成"门禁显式报错"的那道锁。
            VerifyControlCtorShapes(graph, report);

            // ── 内核级守卫 3：语义编译集必须与插件真实编译集一致（两份清单不得漂移）──
            VerifySemanticCompilationSetConsistency(report);

            // ── 内核级守卫 4：语义编译健康度（错误数棘轮 + 组件作用域文件零容忍）──
            // 没有这道锁时，"成功链接 17 个程序集"会被当成 L4 可用，而编译单元里其实藏着 2217 个错误。
            VerifySemanticCompilationHealth(graph, report);

            // ── SPEC-001 继承契约（全局不变量：声明了组件元数据的类必须是契约真后代）──
            foreach (var node in graph.All)
            {
                if (!node.HasMetadataAttribute) continue;
                if (node.IsWidgetContractClass) continue;

                Add(report, node.FileName, WidgetSpecRules.Inheritance, "ERROR", RoslynAstHelper.GetLine(node.Decl),
                    "声明了 [" + WidgetSpecRules.MetadataAttribute + "] 组件元数据但未继承 " + WidgetSpecRules.ContractRootType
                    + "（泛型约束 where T : " + WidgetSpecRules.ContractRootType + " 不算继承），无法被组件框架纳管");
            }

            // ── 逐类规则 ──
            foreach (var node in graph.ContractClasses)
            {
                ScanWidgetClass(node, graph, report);
            }

            // ── 逐文件规则（仅组件源文件；色值 / 场景查询属于"源码文本事实"，与类归属无关）──
            var scopedPaths = new HashSet<string>(graph.ScopedFiles, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                WidgetSourceFile file = files[i];
                if (file == null) continue;
                if (!scopedPaths.Contains(file.Path ?? string.Empty) && !scopedPaths.Contains(file.Name ?? string.Empty)) continue;
                ScanWidgetFile(file, graph, report);
            }

            return report;
        }

        /// <summary>
        /// 内核级守卫：语义编译集必须等于插件的真实编译集。
        ///
        /// 【为什么要对照两份清单】`UI/Auditing` 下有一批文件只由无头验证器编译（它们引用
        /// Microsoft.CodeAnalysis，而 KSP Managed 目录不提供该程序集，插件 csproj 用
        /// &lt;Compile Remove&gt; 把它们排除）。语义编译若把它们混进来，就会退化为错误类型并污染
        /// 整张类型图 —— 而语义图正是 SPEC 规则的判定依据。
        /// 本方法把"C# 侧清单"与"csproj 侧清单"逐项对照，任何一侧单方面增删都会变成显式 ERROR。
        ///
        /// 仓库根不可解析（发布环境）时静默跳过：属于可预期的环境降级，与非语义资源同规矩。
        /// </summary>
        private static void VerifySemanticCompilationSetConsistency(WidgetSourceAuditReport report)
        {
            string repoRoot = ResolveRepositoryRoot();
            if (string.IsNullOrEmpty(repoRoot)) return;

            string csproj = Path.Combine(repoRoot,
                WidgetSpecRules.PluginCsprojRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(csproj)) return;

            List<string> fromCsproj;
            try
            {
                fromCsproj = WidgetSpecRules.ParseCompileRemoveFileNames(File.ReadAllText(csproj));
            }
            catch (Exception ex)
            {
                Add(report, Path.GetFileName(csproj), WidgetSpecRules.KernelSemanticUnhealthy, "ERROR", 0,
                    "无法读取插件项目文件以校验语义编译集: " + ex.Message);
                return;
            }

            var declared = new HashSet<string>(WidgetSpecRules.PluginExcludedAuditFiles, StringComparer.OrdinalIgnoreCase);
            var built = new HashSet<string>(fromCsproj, StringComparer.OrdinalIgnoreCase);

            foreach (string name in declared)
            {
                if (built.Contains(name)) continue;
                Add(report, Path.GetFileName(csproj), WidgetSpecRules.KernelSemanticUnhealthy, "ERROR", 0,
                    "语义编译集清单与插件 csproj 分裂：" + name
                    + " 已声明为『仅无头侧编译』，但 csproj 的 <Compile Remove> 里没有它"
                    + "（该文件会被插件本体编译，运行期可能报缺程序集）");
            }

            foreach (string name in built)
            {
                if (declared.Contains(name)) continue;
                Add(report, Path.GetFileName(csproj), WidgetSpecRules.KernelSemanticUnhealthy, "ERROR", 0,
                    "插件 csproj 排除了 " + name
                    + "，但语义编译集清单未登记 → 该文件会混入语义编译，把整张类型图污染成错误类型");
            }
        }

        /// <summary>
        /// 内核级守卫：语义编译健康度。
        ///
        /// 【为什么必须有】门禁此前只有"成功链接 N 个外部程序集"这一句证据，且从不调用
        /// GetDiagnostics()。于是语义编译里同时藏着影子 UnityEngine.Vector2（来自
        /// Config/WidgetConfig.cs 的 `#if !KSP_RUNTIME` 测试垫片）与 2217 个错误时，
        /// 报告照旧写 "Full L4 真实符号语义与常量折叠激活" —— 这是本次审计发现的最严重问题。
        ///
        /// 三道锁：
        ///   1. 未构建语义上下文 → 显式 WARNING（判定已退化为语法回退，不得宣称 L4 权威）；
        ///   2. 未链接 Unity/KSP 程序集 → 显式 WARNING（Unity 类型无法决议）；
        ///   3. 诊断错误数超棘轮上限，或**任一组件作用域文件**自身带错误 → ERROR。
        ///      第 3 条后半句是关键：作用域文件的符号就是 SPEC-001..007 的判定依据，
        ///      它一旦不可信，那些"合规"结论就都不成立。
        /// </summary>
        private static void VerifySemanticCompilationHealth(WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            SemanticCompilationContext context = graph.SemanticContext;

            if (context == null)
            {
                Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelSemanticDegraded, "WARNING", 0,
                    "未构建语义编译上下文：全部判定已退化为语法回退路径"
                    + "（无法识别类型别名、无法跨命名空间消歧、无语义常量折叠），结论强度低于 L4");
                return;
            }

            if (!context.IsFullSemanticActive)
            {
                Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelSemanticDegraded, "WARNING", 0,
                    "未探测到 KSP_x64_Data/Managed：语义编译仅链接 " + context.ResolvedReferencePaths.Count
                    + " 个主机基础程序集，Unity 类型无法决议，SPEC-006/SPEC-007 已退化为语法回退路径");
            }

            if (context.CompilationErrorCount > WidgetSpecRules.SemanticCompilationErrorCeiling)
            {
                Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelSemanticUnhealthy, "ERROR", 0,
                    "语义编译存在 " + context.CompilationErrorCount + " 处诊断 ERROR（棘轮上限 "
                    + WidgetSpecRules.SemanticCompilationErrorCeiling + "）：符号决议可能落在错误类型上，"
                    + "L4 权威性不成立，不得据此宣称语义结论");
            }

            // 组件作用域文件零容忍：逐类去重后按文件统计
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < graph.ContractClasses.Count; i++)
            {
                WidgetClassNode node = graph.ContractClasses[i];
                string key = !string.IsNullOrEmpty(node.FilePath) ? node.FilePath : node.FileName;
                if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;

                int errors = context.ErrorCountForFile(key);
                if (errors <= 0) continue;

                Add(report, node.FileName, WidgetSpecRules.KernelSemanticUnhealthy, "ERROR", 0,
                    "组件作用域文件在语义编译中有 " + errors + " 处诊断 ERROR：该文件的符号决议不可信，"
                    + "其 SPEC 判定必须视为失效（先修编译错误，再看合规结论）");
            }
        }

        /// <summary>
        /// 交叉校验：从被扫描源码里派生微控件构造函数的真实声明，与 WidgetSpecRules.ControlCtorShapes 逐条比对。
        ///
        /// 这道锁的意义：旧实现把"哪一参是遥测 Token"以参数下标形式写死在规则体里，等于对另一个文件里
        /// 构造函数签名的人肉镜像。重载一增删，判定就会静默翻转（违规被放过，或合规被全量误报）。
        /// 现在签名表是显式数据，本方法负责证明它与真实源码一致。
        ///
        /// 三个方向都要拦：
        ///   1. 表里登记的形状在源码里找不到对应参数个数的构造函数 → 表已过期；
        ///   2. 表中标了 TokenIndex 的形状，该位置形参名不再是 ControlTokenParameterName → 索引失效；
        ///   3. 源码里出现"带 Token 形参"的重载但表中未登记 → 新重载会静默落进未登记分支。
        /// </summary>
        private static void VerifyControlCtorShapes(WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            for (int t = 0; t < WidgetSpecRules.AuditedControlTypes.Count; t++)
            {
                string controlType = WidgetSpecRules.AuditedControlTypes[t];
                List<WidgetSpecRules.ControlCtorShape> shapes = WidgetSpecRules.ShapesOfControlType(controlType);

                var ctors = new List<ConstructorDeclarationSyntax>();
                for (int i = 0; i < graph.All.Count; i++)
                {
                    WidgetClassNode node = graph.All[i];
                    if (!string.Equals(node.Name, controlType, StringComparison.Ordinal)) continue;
                    ctors.AddRange(node.Decl.Members.OfType<ConstructorDeclarationSyntax>());
                }

                // 该控件不在被扫描源码集合内（合成用例场景）→ 无可比对，交由签名表独立承担判定
                if (ctors.Count == 0) continue;

                for (int s = 0; s < shapes.Count; s++)
                {
                    WidgetSpecRules.ControlCtorShape shape = shapes[s];
                    var match = ctors.FirstOrDefault(c => c.ParameterList.Parameters.Count == shape.ParameterCount);

                    if (match == null)
                    {
                        Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelContractDrift, "ERROR", 0,
                            "微控件构造函数签名表已过期：" + controlType + " 在表中登记了 " + shape.ParameterCount
                            + " 参重载，但源码里找不到对应声明。请同步 " + nameof(WidgetSpecRules.ControlCtorShapes));
                        continue;
                    }

                    if (shape.TokenIndex < 0) continue;

                    if (shape.TokenIndex >= match.ParameterList.Parameters.Count)
                    {
                        Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelContractDrift, "ERROR",
                            RoslynAstHelper.GetLine(match),
                            controlType + " 的 " + shape.ParameterCount + " 参重载：签名表 TokenIndex=" + shape.TokenIndex
                            + " 越界（实际形参仅 " + match.ParameterList.Parameters.Count + " 个）");
                        continue;
                    }

                    string actualName = match.ParameterList.Parameters[shape.TokenIndex].Identifier.Text;
                    if (!string.Equals(actualName, WidgetSpecRules.ControlTokenParameterName, StringComparison.Ordinal))
                    {
                        Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelContractDrift, "ERROR",
                            RoslynAstHelper.GetLine(match),
                            controlType + " 的 " + shape.ParameterCount + " 参重载第 " + (shape.TokenIndex + 1)
                            + " 个形参现在是 '" + actualName + "'，不再是 '" + WidgetSpecRules.ControlTokenParameterName
                            + "'。签名表的 TokenIndex 已失效，遥测装配倒查会给出错误判定。请同步 "
                            + nameof(WidgetSpecRules.ControlCtorShapes));
                    }
                }

                // 反向：源码里凡带 Token 形参的重载，都必须在表中登记
                for (int c = 0; c < ctors.Count; c++)
                {
                    ConstructorDeclarationSyntax ctor = ctors[c];
                    var parameters = ctor.ParameterList.Parameters;

                    int tokenIndex = -1;
                    for (int p = 0; p < parameters.Count; p++)
                    {
                        if (string.Equals(parameters[p].Identifier.Text, WidgetSpecRules.ControlTokenParameterName, StringComparison.Ordinal))
                        {
                            tokenIndex = p;
                            break;
                        }
                    }
                    if (tokenIndex < 0) continue;

                    bool registered = shapes.Any(d => d.ParameterCount == parameters.Count && d.TokenIndex == tokenIndex);
                    if (registered) continue;

                    Add(report, WidgetSpecRules.KernelReportName, WidgetSpecRules.KernelContractDrift, "ERROR",
                        RoslynAstHelper.GetLine(ctor),
                        controlType + " 存在带 '" + WidgetSpecRules.ControlTokenParameterName + "' 形参的 "
                        + parameters.Count + " 参重载（Token 位于第 " + (tokenIndex + 1)
                        + " 参），但审计签名表未登记。不登记的话，调用该重载的未绑 Token 控件会被静默放过。"
                        + "请补登 " + nameof(WidgetSpecRules.ControlCtorShapes));
                }
            }
        }

        private static void ScanWidgetClass(WidgetClassNode node, WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            // ── SPEC-002 刷新阶梯（形状 + 取值 + 声明式满帧依据）──
            ScanTierContract(node, graph, report);

            // ── SPEC-003 语义主题管道（签名 + 缺失）──
            ScanThemeContract(node, report);

            // ── SPEC-004 遥测契约（签名 + 缺失）──
            ScanTelemetryContract(node, report);

            // ── SPEC-005 安全生命周期（仅约束组件类，Unity 助手类的消息式 OnDestroy 不在契约内）──
            ScanLifecycle(node, report);

            // ── 遥测装配警告：扫描未接入标准化遥测装配的微控件 ──
            ScanTelemetryAssembly(node, report);

            // ── SPEC-008 自动注册元数据（本类或继承链上已声明即可）──
            if (!node.IsAbstract && WidgetClassGraph.FindDeclarer(node, n => n.HasMetadataAttribute) == null)
            {
                Add(report, node.FileName, WidgetSpecRules.AutoRegistration, "ERROR", RoslynAstHelper.GetLine(node.Decl),
                    "未声明 [" + WidgetSpecRules.MetadataAttribute + "] 自动注册与预设库元数据特性（具体组件必须声明元数据以便自动挂载至游戏内预设库与验证器）");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════
        // SPEC-002 / 003 / 004：契约由"最近的合规声明者"提供，与反射级判定口径一致
        // ══════════════════════════════════════════════════════════════════════════════════════════

        private static void ScanTierContract(WidgetClassNode node, WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            if (node.TierProperty != null)
            {
                int line = RoslynAstHelper.GetLine(node.TierProperty);

                if (node.TierProperty.DescendantNodes().OfType<CastExpressionSyntax>().Any())
                {
                    Add(report, node.FileName, WidgetSpecRules.RefreshTier, "ERROR", line,
                        "RefreshTier 禁止用强制转换（如 (" + WidgetSpecRules.TierEnumType + ")999）伪造阶梯，必须直接返回枚举成员之一");
                }
                else
                {
                    var validNames = RoslynAstHelper.CollectReturnedMemberNames(node.TierProperty)
                        .Where(n => graph.TierMembers.Contains(n))
                        .Distinct()
                        .ToList();

                    if (validNames.Count == 0)
                    {
                        Add(report, node.FileName, WidgetSpecRules.RefreshTier, "ERROR", line,
                            "RefreshTier 必须直接返回 " + WidgetSpecRules.TierEnumType + " 的枚举成员（"
                            + string.Join(" / ", graph.TierMembers) + "）；不得用字段、变量或常量间接回填");
                    }
                    else if (!node.IsAbstract
                             && validNames.Contains(WidgetSpecRules.FullFrameTierMember)
                             && !WidgetClassGraph.IsHighFrequencyDeclared(node))
                    {
                        Add(report, node.FileName, WidgetSpecRules.RefreshTier, "WARNING", line,
                            "声明了满帧阶梯 " + WidgetSpecRules.TierEnumType + "." + WidgetSpecRules.FullFrameTierMember
                            + " (60Hz 满帧) 但未在 [" + WidgetSpecRules.MetadataAttribute + "] 上声明 "
                            + WidgetSpecRules.HighFrequencyMetadata + " = true。"
                            + "若该组件确实是高速姿态/操纵类，请显式声明该元数据；否则请改用 Standard (30Hz) 或 Relaxed (10Hz)");
                    }
                }
            }

            if (WidgetClassGraph.FindDeclarer(node, n => n.TierProperty != null) == null)
            {
                Add(report, node.FileName, WidgetSpecRules.RefreshTier, "ERROR", RoslynAstHelper.GetLine(node.Decl),
                    "未显式重写 RefreshTier 阶梯（必须由本类或继承链上的组件类提供，禁止依赖 " + WidgetSpecRules.ContractRootType + " 的默认阶梯）");
            }

            // CPU 软件光栅化反模式侦测（SPEC-002 效能红线，数据单点声明于 WidgetSpecRules）
            bool hasCpuRasterizer = node.Decl.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression.ToString().EndsWith(WidgetSpecRules.CpuRasterizerApiSuffix, StringComparison.Ordinal))
                || node.Decl.Members.OfType<FieldDeclarationSyntax>()
                .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == WidgetSpecRules.CpuRasterizerPixelField));
            if (hasCpuRasterizer)
            {
                Add(report, node.FileName, WidgetSpecRules.RefreshTier, "WARNING", RoslynAstHelper.GetLine(node.Decl),
                    "检测到运行时 CPU 软件光栅化反模式 (" + WidgetSpecRules.CpuRasterizerApiSuffix.TrimStart('.') + "/" + WidgetSpecRules.CpuRasterizerPixelField
                    + ")。根据 SPEC-002 效能规范，严禁在 UI 帧循环中手写 CPU 像素光栅化与阻塞式显存提交，必须迁移至 GPU 矢量网格/Shader 或 Core3D 离屏相机");
            }
        }

        private static void ScanThemeContract(WidgetClassNode node, WidgetSourceAuditReport report)
        {
            if (node.ThemeMethod != null && !RoslynAstHelper.IsPublicOverrideWithSingleParam(node.ThemeMethod, WidgetSpecRules.ThemeParameterType))
            {
                Add(report, node.FileName, WidgetSpecRules.SemanticTheming, "ERROR", RoslynAstHelper.GetLine(node.ThemeMethod),
                    "声明了 " + WidgetSpecRules.ThemeMethod + " 但签名不符合规范（应为 public override void "
                    + WidgetSpecRules.ThemeMethod + "(" + WidgetSpecRules.ThemeParameterType + " theme)）");
            }

            // 缺失判定只看"链上有没有声明"；签名是否合规由上面的逐声明校验负责，避免同一个问题报两条
            bool declaredAnywhere = WidgetClassGraph.FindDeclarer(node, n => n.ThemeMethod != null) != null;

            if (!declaredAnywhere)
            {
                Add(report, node.FileName, WidgetSpecRules.SemanticTheming, "ERROR", RoslynAstHelper.GetLine(node.Decl),
                    "未重写 " + WidgetSpecRules.ThemeMethod + "(" + WidgetSpecRules.ThemeParameterType + ") 语义主题着色方法（本类或继承链上的组件类必须提供合规实现）");
            }
        }

        private static void ScanTelemetryContract(WidgetClassNode node, WidgetSourceAuditReport report)
        {
            if (node.TelemetryMethod != null && !RoslynAstHelper.IsPublicOverrideWithSingleParam(node.TelemetryMethod, WidgetSpecRules.TelemetryParameterType))
            {
                Add(report, node.FileName, WidgetSpecRules.TelemetryContract, "ERROR", RoslynAstHelper.GetLine(node.TelemetryMethod),
                    "声明了 " + WidgetSpecRules.TelemetryMethod + " 但签名不符合规范（应为 public override void "
                    + WidgetSpecRules.TelemetryMethod + "(" + WidgetSpecRules.TelemetryParameterType + " telemetry)）");
            }

            bool declaredAnywhere = WidgetClassGraph.FindDeclarer(node, n => n.TelemetryMethod != null) != null;

            if (!declaredAnywhere)
            {
                Add(report, node.FileName, WidgetSpecRules.TelemetryContract, "ERROR", RoslynAstHelper.GetLine(node.Decl),
                    "未重写 " + WidgetSpecRules.TelemetryMethod + "(" + WidgetSpecRules.TelemetryParameterType + ") 遥测驱动接口（本类或继承链上的组件类必须提供合规实现）");
            }
        }

        private static void ScanLifecycle(WidgetClassNode node, WidgetSourceAuditReport report)
        {
            MethodDeclarationSyntax destroy = node.OnDestroyMethod;
            if (destroy == null) return;

            int line = RoslynAstHelper.GetLine(destroy);
            if (!RoslynAstHelper.HasModifier(destroy, SyntaxKind.OverrideKeyword))
            {
                Add(report, node.FileName, WidgetSpecRules.SafeLifecycle, "ERROR", line,
                    "声明了 " + WidgetSpecRules.LifecycleMethod + " 但未 override 基类，会隐藏基类方法并阻断销毁清理（应写作 protected override void "
                    + WidgetSpecRules.LifecycleMethod + "() 并调用 base." + WidgetSpecRules.LifecycleMethod + "()）");
                return;
            }

            bool callsBase = destroy.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression.ToString() == "base." + WidgetSpecRules.LifecycleMethod);
            if (!callsBase)
            {
                Add(report, node.FileName, WidgetSpecRules.SafeLifecycle, "ERROR", line,
                    "override 了 " + WidgetSpecRules.LifecycleMethod + " 但方法体内未调用 base." + WidgetSpecRules.LifecycleMethod
                    + "()，基类解注册/回收链会被整段截断");
            }
        }

        private static void ScanTelemetryAssembly(WidgetClassNode node, WidgetSourceAuditReport report)
        {
            if (node.IsAbstract) return;

            // ── 1. 声明式 DSL 微控件字段：public TextWidget Value = TextWidget.Value("token"); ──
            foreach (var field in node.Decl.Members.OfType<FieldDeclarationSyntax>())
            {
                string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);
                if (!WidgetSpecRules.IsTelemetryTokenDslType(typeName)) continue;

                foreach (var v in field.Declaration.Variables)
                {
                    if (!(v.Initializer?.Value is InvocationExpressionSyntax inv)) continue;
                    if (!WidgetSpecRules.IsDslTokenFactory(inv.Expression.ToString())) continue;

                    var dslArgs = inv.ArgumentList.Arguments;
                    bool dslHasToken = dslArgs.Count > 0 && IsTokenArgumentProvided(dslArgs[0].Expression);
                    if (!dslHasToken)
                    {
                        AddAssemblyWarning(report, node, RoslynAstHelper.GetLine(v), v.Identifier.Text);
                    }
                }
            }

            // ── 2. 方法体中的 Controls.Register / WidgetControlManager.Register（收集式微控件注册）──
            foreach (var inv in node.Decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string expr = inv.Expression.ToString();
                bool isRegisterCall = expr == WidgetSpecRules.ControlsRegisterMethod
                    || expr.EndsWith("." + WidgetSpecRules.ControlsRegisterMethod, StringComparison.Ordinal);
                if (!isRegisterCall) continue;

                var args = inv.ArgumentList.Arguments;
                if (args.Count == 0) continue;

                ExpressionSyntax ctrlExpr = null;
                if (args.Count == 1) ctrlExpr = args[0].Expression;
                else if (args.Count >= 2 && IsSelfOrWidgetArgument(args[0].Expression)) ctrlExpr = args[1].Expression;
                if (ctrlExpr == null) continue;
                if (!(ctrlExpr is ObjectCreationExpressionSyntax objCreation)) continue;

                string createdType = RoslynAstHelper.GetSimpleTypeName(objCreation.Type);
                if (!WidgetSpecRules.IsAuditedControlType(createdType)) continue;
                if (objCreation.ArgumentList == null) continue;

                var cArgs = objCreation.ArgumentList.Arguments;

                // 形状查表：判定依据全部来自 WidgetSpecRules.ControlCtorShapes，规则体里不再出现参数下标。
                // 该表由 VerifyControlCtorShapes 与真实构造函数声明双向交叉校验。
                WidgetSpecRules.ControlCtorShape shape = WidgetSpecRules.FindControlCtorShape(createdType, cArgs.Count);

                if (shape == null)
                {
                    // 未登记的重载形状：旧实现在这里直接落空（isMissingToken 保持 false）→ 静默放过。
                    // 现在显式上报，让"签名表过期"变得可见。
                    Add(report, node.FileName, WidgetSpecRules.TelemetryAssemblyWarning, "WARNING",
                        RoslynAstHelper.GetLine(objCreation),
                        $"{node.Name}组件调用了 {createdType} 的 {cArgs.Count} 参构造重载，但审计签名表 "
                        + nameof(WidgetSpecRules.ControlCtorShapes) + " 未登记该形状，无法判定遥测 Token 装配情况。"
                        + "请先登记形状，否则此类调用不会被审计");
                    continue;
                }

                // TokenIndex < 0 表示该重载本身就没有 Token 绑定形参 → 直接判定为未装配
                bool isMissingToken = shape.TokenIndex < 0
                    || !IsTokenArgumentProvided(cArgs[shape.TokenIndex].Expression);

                if (isMissingToken)
                {
                    AddAssemblyWarning(report, node, RoslynAstHelper.GetLine(objCreation),
                        ResolveControlDisplayName(objCreation.ArgumentList, shape, createdType));
                }
            }
        }

        /// <summary>
        /// Token 实参是否确实提供了绑定：null 字面量与空字符串视为未提供，其余表达式（含变量）视为已提供。
        /// 与旧实现口径一致，仅把判定集中到一处，避免两处各写一遍。
        /// </summary>
        private static bool IsTokenArgumentProvided(ExpressionSyntax expression)
        {
            if (expression == null) return false;
            if (expression is LiteralExpressionSyntax literal)
            {
                if (literal.IsKind(SyntaxKind.NullLiteralExpression)) return false;
                if (literal.IsKind(SyntaxKind.StringLiteralExpression)) return !string.IsNullOrEmpty(literal.Token.ValueText);
                return true;
            }
            return true;
        }

        /// <summary>Register 调用第一参是否为"注册主体"（this 或 xxxWidget），用于定位真正的控件实参</summary>
        private static bool IsSelfOrWidgetArgument(ExpressionSyntax expression)
        {
            if (expression == null) return false;
            string s = expression.ToString();
            return s == "this" || s.EndsWith("widget", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>按签名表登记的 NameIndex 取控件显示名，取不到则回落到类型兜底名</summary>
        private static string ResolveControlDisplayName(ArgumentListSyntax argList, WidgetSpecRules.ControlCtorShape shape, string controlType)
        {
            var args = argList.Arguments;
            int index = shape.NameIndex;

            if (index >= 0 && index < args.Count)
            {
                ExpressionSyntax expr = args[index].Expression;
                if (expr is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.StringLiteralExpression)
                    && !string.IsNullOrEmpty(lit.Token.ValueText))
                {
                    return lit.Token.ValueText;
                }
                if (expr is IdentifierNameSyntax id) return id.Identifier.Text;
                if (expr is MemberAccessExpressionSyntax ma) return ma.Name.Identifier.Text;
            }

            return WidgetSpecRules.DefaultControlDisplayName(controlType);
        }

        private static void AddAssemblyWarning(WidgetSourceAuditReport report, WidgetClassNode node, int line, string ctrlName)
        {
            Add(report, node.FileName, WidgetSpecRules.TelemetryAssemblyWarning, "WARNING", line,
                $"{node.Name}组件'{ctrlName}'控件未支持标准化遥测装配 (未提供遥测 Token 绑定或缺少通道装配契约)");
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════
        // 文件级规则：SPEC-006 颜色字面量 / SPEC-007 场景查询
        // ══════════════════════════════════════════════════════════════════════════════════════════

        private static void ScanWidgetFile(WidgetSourceFile file, WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            SemanticModel semanticModel = graph.SemanticContext?.GetSemanticModel(file.Path ?? file.Name);

            // ── SPEC-006 零颜色字面量（棘轮：允许存量只降不升）──
            int colorOccurrences = WidgetColorLiteralAudit.CountOccurrences(file.Text, semanticModel);
            int allowed = WidgetColorLiteralAudit.GetAllowedOccurrences(file.Name);
            if (colorOccurrences > allowed)
            {
                var samples = string.Join(" | ", WidgetColorLiteralAudit.CollectOffendingLines(file.Text, 3, semanticModel).ToArray());
                Add(report, file.Name, WidgetSpecRules.NoHardcodedColors, "ERROR", 0,
                    $"新增颜色字面量 {colorOccurrences - allowed} 处 (实测 {colorOccurrences} / 基线 {allowed})。"
                    + "颜色必须取自 WidgetStyleManager 语义角色或 ThemeConfig；占位色用 Color.clear。样本: " + samples);
            }

            // ── SPEC-007 零场景查询（表驱动：判定数据全部来自 WidgetSpecRules.SceneQueryApis）──
            if (!graph.Roots.TryGetValue(file.Path ?? string.Empty, out CompilationUnitSyntax root)) return;

            foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (!u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)) continue;
                string ownerName = u.Name.ToString().Split('.').Last();
                if (!WidgetSpecRules.IsBannedStaticImportOwner(ownerName)) continue;

                Add(report, file.Name, WidgetSpecRules.NoSceneQueries, "ERROR", RoslynAstHelper.GetLine(u),
                    "禁止 using static（" + u.Name + "）：它会让裸名场景查询调用绕过 SPEC-007 黑名单表，必须改由 ProbeManager 统一纳管");
            }

            // 去重键 = 行号 + 表达式文本，而不是"行号相同就丢弃"。
            // 旧实现用一个跨两次遍历共享的 lastReportedLine：同一行上两个不同的场景查询只会报一个
            // （例如 `var a = Camera.main; var b = FindObjectOfType<Camera>();` 写在同行时漏报其一），
            // 且行为还依赖遍历顺序。现在按"同一处调用"去重，语义明确且与遍历顺序无关。
            var reportedSceneQueries = new HashSet<string>(StringComparer.Ordinal);

            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                bool isBanned = false;
                if (semanticModel != null)
                {
                    // 覆盖率计数：语义点位 5（场景查询符号决议）· 调用分支
                    report.SemanticVerdictCount++;
                    report.SemanticInvocationVerdictCount++;
                    var symbol = semanticModel.GetSymbolInfo(inv).Symbol;
                    if (SemanticCompilationProvider.IsSceneQuerySymbol(symbol))
                    {
                        isBanned = true;
                    }
                }

                if (!isBanned)
                {
                    string expr = inv.Expression.ToString();
                    if (WidgetSpecRules.IsSceneQueryApi(expr, true))
                    {
                        isBanned = true;
                    }
                }

                if (!isBanned) continue;

                string exprText = inv.Expression.ToString();
                int line = RoslynAstHelper.GetLine(inv);
                if (!reportedSceneQueries.Add(line + "|" + exprText)) continue;
                Add(report, file.Name, WidgetSpecRules.NoSceneQueries, "ERROR", line,
                    "组件内出现场景查询 API (" + exprText + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
            }

            foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                bool isBanned = false;
                if (semanticModel != null)
                {
                    // 覆盖率计数：语义点位 5（场景查询符号决议）· 成员访问分支
                    report.SemanticVerdictCount++;
                    report.SemanticMemberAccessVerdictCount++;
                    var symbol = semanticModel.GetSymbolInfo(ma).Symbol;
                    if (SemanticCompilationProvider.IsSceneQuerySymbol(symbol))
                    {
                        isBanned = true;
                    }
                }

                if (!isBanned)
                {
                    string expr = ma.ToString();
                    if (WidgetSpecRules.IsSceneQueryApi(expr, false))
                    {
                        isBanned = true;
                    }
                }

                if (!isBanned) continue;

                string exprText = ma.ToString();
                int line = RoslynAstHelper.GetLine(ma);
                if (!reportedSceneQueries.Add(line + "|" + exprText)) continue;
                Add(report, file.Name, WidgetSpecRules.NoSceneQueries, "ERROR", line,
                    "组件内出现场景查询 API (" + exprText + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
            }
        }

        private static void Add(WidgetSourceAuditReport report, string fileName, string rule, string severity, int line, string description)
        {
            report.Violations.Add(new WidgetSourceViolation
            {
                FileName = fileName,
                RuleCode = rule,
                Severity = severity,
                Line = line,
                Description = description
            });
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════
        // 自检：规则正反用例 + 端到端合成组件
        // ══════════════════════════════════════════════════════════════════════════════════════════

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

            // ── 1. SPEC-007 场景查询表正反用例 ──
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
                @"var q = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);",
                @"var r = Camera.current;",
                @"var s = Camera.allCamerasCount;",
                @"var t = Camera.GetAllCameras(null);",
                @"var u = UnityEngine.Camera.current;"
            };
            for (int i = 0; i < sceneQueries.Length; i++)
            {
                check(ContainsSceneQueryAst(sceneQueries[i]), "SPEC-007 漏检: " + sceneQueries[i].Replace("\n", " "));
            }

            string[] staticImports =
            {
                @"using static UnityEngine.GameObject;",
                @"using static UnityEngine.Object;",
                @"using static UnityEngine.Resources;",
                @"using static UnityEngine.Camera;"
            };
            for (int i = 0; i < staticImports.Length; i++)
            {
                check(ContainsSceneQueryAst(staticImports[i]), "SPEC-007 static import 漏检: " + staticImports[i]);
            }

            string[] notQueries =
            {
                @"// FindObjectOfType<Camera>()",
                @"/* FindObjectOfType<Camera>() */",
                @"string s = ""FindObjectOfType"";",
                @"var c = Probe.FindObjectsByTypeSafe<Camera>();",
                @"var d = transform.Find(""Child"");",
                @"var e = Camera.mainCameraCountField;"
            };
            for (int i = 0; i < notQueries.Length; i++)
            {
                check(!ContainsSceneQueryAst(notQueries[i]), "SPEC-007 误报: " + notQueries[i]);
            }

            // ── 2. 端到端：合规合成组件必须零违规 ──
            string compliant = BuildSyntheticWidget(null);
            var okReport = Scan(new[] { MakeFile("FakeOk.cs", compliant) });
            check(okReport.ErrorCount == 0, "合规合成组件被误判 -> " + Describe(okReport));

            // ── 3. SPEC-001 继承契约（正向：声明元数据但未继承；反向：无元数据的辅助类不参与）──
            string fakeBase = compliant.Replace(": BaseFlightWidget", string.Empty);
            var inheritReport = Scan(new[] { MakeFile("FakeBase.cs", fakeBase) });
            check(inheritReport.CountByRule(WidgetSpecRules.Inheritance) == 1, "SPEC-001 未继承基类未拦下");

            string forgery = "namespace N { [FlightWidget(\"sneaky\")] public class Sneaky : MonoBehaviour { void M<T>() where T : BaseFlightWidget { } } }";
            var forgeryReport = Scan(new[] { MakeFile("Sneaky.cs", forgery) });
            check(forgeryReport.CountByRule(WidgetSpecRules.Inheritance) == 1, "SPEC-001 泛型约束伪造继承未拦下");

            var helperOnlyReport = Scan(new[] { MakeFile("HelperOnly.cs", "namespace N { public static class WidgetUtils { } }") });
            check(helperOnlyReport.ErrorCount == 0 && helperOnlyReport.WidgetsScanned == 0, "非组件文件不应参与组件契约判定");

            // ── 4. 跨文件间接派生：契约由继承链提供，不要求同类同文件 ──
            string midSrc = compliant.Replace("class FakeWidget", "class FakeMid");
            string leafSrc = "namespace N { [FlightWidget(\"leaf\")] public class FakeLeaf : FakeMid { } }";
            var indirectReport = Scan(new[] { MakeFile("FakeMid.cs", midSrc), MakeFile("FakeLeaf.cs", leafSrc) });
            check(indirectReport.ErrorCount == 0, "跨文件间接派生被误判 -> " + Describe(indirectReport));
            check(indirectReport.WidgetsScanned == 2, "跨文件间接派生的组件数统计不正确");

            // ── 5. 全限定类型名不得误判 ──
            string qualified = compliant
                .Replace("ApplyTheme(ThemeConfig theme)", "ApplyTheme(ModularFlightPanel.UI.ThemeConfig theme)")
                .Replace("OnUpdateTelemetry(IFlightTelemetry telemetry)", "OnUpdateTelemetry(ModularFlightPanel.Core.IFlightTelemetry telemetry)")
                .Replace("override WidgetRefreshTier RefreshTier", "override ModularFlightPanel.UI.WidgetRefreshTier RefreshTier");
            var qualifiedReport = Scan(new[] { MakeFile("Qualified.cs", qualified) });
            check(qualifiedReport.ErrorCount == 0, "全限定类型名被误判 -> " + Describe(qualifiedReport));

            // ── 6. SPEC-002 阶梯：缺失 / 强转 / 注释伪造 / 块状 get / 字段回填 / 满帧声明 ──
            var tierMissing = Scan(new[] { MakeFile("TierMissing.cs", compliant.Replace("        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;\n", string.Empty)) });
            check(tierMissing.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 缺失 RefreshTier 未拦下");

            var tierCast = Scan(new[] { MakeFile("TierCast.cs", compliant.Replace("=> WidgetRefreshTier.Standard", "=> (WidgetRefreshTier)999")) });
            check(tierCast.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 强转伪造阶梯未拦下");

            var tierComment = Scan(new[] { MakeFile("TierComment.cs", compliant.Replace("=> WidgetRefreshTier.Standard", "=> WidgetRefreshTier.Legacy /* Standard */")) });
            check(tierComment.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 注释伪造取值未拦下（注释里的阶梯名不得充当取值）");

            var tierIndirect = Scan(new[] { MakeFile("TierIndirect.cs", compliant.Replace("=> WidgetRefreshTier.Standard", "=> _cachedTier")) });
            check(tierIndirect.CountByRule(WidgetSpecRules.RefreshTier) == 1, "SPEC-002 字段间接回填未拦下");

            string tierBlock = compliant.Replace(
                "public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;",
                "public override WidgetRefreshTier RefreshTier { get { return WidgetRefreshTier.Relaxed; } }");
            var tierBlockReport = Scan(new[] { MakeFile("TierBlock.cs", tierBlock) });
            check(tierBlockReport.CountByRule(WidgetSpecRules.RefreshTier) == 0, "SPEC-002 块状 get 被误判");

            string criticalNoMeta = compliant.Replace("=> WidgetRefreshTier.Standard", "=> WidgetRefreshTier.Critical");
            var criticalWarn = Scan(new[] { MakeFile("CriticalNoMeta.cs", criticalNoMeta) });
            check(criticalWarn.CountByRule(WidgetSpecRules.RefreshTier) == 1
                  && criticalWarn.Violations.Any(v => v.Severity == "WARNING"), "SPEC-002 满帧未声明 HighFrequency 应只告警");

            string criticalWithMeta = criticalNoMeta.Replace("[FlightWidget(\"fake_widget\")]", "[FlightWidget(\"fake_widget\", HighFrequency = true)]");
            var criticalOk = Scan(new[] { MakeFile("CriticalWithMeta.cs", criticalWithMeta) });
            check(criticalOk.CountByRule(WidgetSpecRules.RefreshTier) == 0, "SPEC-002 已声明 HighFrequency 的满帧组件被误判");

            // ── 7. SPEC-003 / SPEC-004：缺失与签名 ──
            var themeMissing = Scan(new[] { MakeFile("ThemeMissing.cs", compliant.Replace("        public override void ApplyTheme(ThemeConfig theme) { }\n", string.Empty)) });
            check(themeMissing.CountByRule(WidgetSpecRules.SemanticTheming) == 1, "SPEC-003 缺失 ApplyTheme 未拦下");

            var themeBadSig = Scan(new[] { MakeFile("ThemeBadSig.cs", compliant.Replace("public override void ApplyTheme(ThemeConfig theme)", "public void ApplyTheme(ThemeConfig theme)")) });
            check(themeBadSig.CountByRule(WidgetSpecRules.SemanticTheming) == 1, "SPEC-003 非 override 签名未拦下");

            var telemMissing = Scan(new[] { MakeFile("TelemMissing.cs", compliant.Replace("        public override void OnUpdateTelemetry(IFlightTelemetry telemetry) { }\n", string.Empty)) });
            check(telemMissing.CountByRule(WidgetSpecRules.TelemetryContract) == 1, "SPEC-004 缺失 OnUpdateTelemetry 未拦下");

            // ── 8. SPEC-005 安全生命周期 ──
            var destroyPriv = Scan(new[] { MakeFile("DestroyPriv.cs", compliant.Replace("protected override void OnDestroy()", "private void OnDestroy()")) });
            check(destroyPriv.CountByRule(WidgetSpecRules.SafeLifecycle) == 1, "SPEC-005 private OnDestroy 未拦下");

            var destroyNoBase = Scan(new[] { MakeFile("DestroyNoBase.cs", compliant.Replace("base.OnDestroy();", string.Empty)) });
            check(destroyNoBase.CountByRule(WidgetSpecRules.SafeLifecycle) == 1, "SPEC-005 缺失 base.OnDestroy 未拦下");

            string helperDestroy = compliant.Replace(
                "        protected override void OnDestroy() { base.OnDestroy(); }\n",
                "        protected override void OnDestroy() { base.OnDestroy(); }\n"
                + "\n        protected class DragHelper : MonoBehaviour\n        {\n            private void OnDestroy() { }\n        }\n");
            var helperReport = Scan(new[] { MakeFile("HelperDestroy.cs", helperDestroy) });
            check(helperReport.CountByRule(WidgetSpecRules.SafeLifecycle) == 0, "Unity 助手类的消息式 OnDestroy 不应被组件契约误判");

            // ── 9. SPEC-006 颜色字面量 ──
            var colorReport = Scan(new[] { MakeFile("FakeColor.cs", BuildSyntheticWidget("        private UnityEngine.Color _c = new UnityEngine.Color(1f, 0f, 0f, 1f);")) });
            check(colorReport.CountByRule(WidgetSpecRules.NoHardcodedColors) == 1, "SPEC-006 颜色字面量未拦下");

            string aliasSrc = BuildSyntheticWidget("        private void Q() { var c = C.red; }")
                              .Replace("using System;", "using System;\nusing C = UnityEngine.Color;");
            var aliasReport = Scan(new[] { MakeFile("Alias.cs", aliasSrc) });
            check(aliasReport.CountByRule(WidgetSpecRules.NoHardcodedColors) == 1, "SPEC-006 using 别名颜色未拦下");

            // ── 10. SPEC-007 文件级扫描（注释 / 插值 / static import）──
            var sceneReport = Scan(new[] { MakeFile("FakeScene.cs", BuildSyntheticWidget("        private void Q() { var c = FindObjectOfType<Camera>(); }")) });
            check(sceneReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 真实调用未拦下");

            var commentReport = Scan(new[] { MakeFile("FakeComment.cs", BuildSyntheticWidget("        // FindObjectOfType<Camera> 只在注释")) });
            check(commentReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 0, "SPEC-007 注释被误判");

            var holeReport = Scan(new[] { MakeFile("Hole.cs", BuildSyntheticWidget("        private string Q() { return $\"{FindObjectOfType<Camera>()}\"; }")) });
            check(holeReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 插值洞内真实调用未拦下");

            var holeOkReport = Scan(new[] { MakeFile("HoleOk.cs", BuildSyntheticWidget("        private string Q() { return $\"x{1 + 2}y\"; }")) });
            check(holeOkReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 0, "SPEC-007 普通插值被误判");

            string staticSrc = BuildSyntheticWidget("        private void Q() { var x = Find(\"HUD\"); }")
                               .Replace("using System;", "using System;\nusing static UnityEngine.GameObject;");
            var staticReport = Scan(new[] { MakeFile("StaticImport.cs", staticSrc) });
            check(staticReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 static import 漏检");

            var cameraCurrentReport = Scan(new[] { MakeFile("CameraCurrent.cs", BuildSyntheticWidget("        private void Q() { var c = Camera.current; }")) });
            check(cameraCurrentReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1, "SPEC-007 Camera.current 漏检");

            // ── 11. SPEC-008 自动注册元数据（链上已声明即可，垫片类豁免）──
            var attrMissing = Scan(new[] { MakeFile("AttrMissing.cs", compliant.Replace("    [FlightWidget(\"fake_widget\")]\n", string.Empty)) });
            check(attrMissing.CountByRule(WidgetSpecRules.AutoRegistration) == 1, "SPEC-008 缺失 [FlightWidget] 未拦下");

            string shimSrc = "namespace N { [Obsolete] public class LegacyShim : BaseFlightWidget { } }";
            var shimReport = Scan(new[] { MakeFile("LegacyShim.cs", shimSrc) });
            check(shimReport.ErrorCount == 0, "兼容垫片类（继承链 [Obsolete]）应豁免重新实现契约 -> " + Describe(shimReport));

            // ── 12. 判定依据自检：阶梯枚举缺失必须显式报内核错误 ──
            string noEnum = compliant.Replace(SyntheticTierEnumLine, string.Empty);
            var noEnumReport = Scan(new[] { MakeFile("NoEnum.cs", noEnum) });
            check(noEnumReport.Violations.Any(v => v.FileName == WidgetSpecRules.KernelReportName), "阶梯枚举缺失时未上报内核错误");

            // ── 13. 发现层状态（决定 CI 判 ERROR / 运行时判 WARNING）──
            check(WidgetSourceAudit.Discover(null).Status == WidgetDiscoveryStatus.RepositoryRootUnresolved, "发现层护栏失效: repoRoot 为空必须报不可解析");
            check(WidgetSourceAudit.Discover(@"C:\nonexistent-repo-root").Status == WidgetDiscoveryStatus.RepositoryRootUnresolved, "发现层护栏失效: 仓库根不存在必须报不可解析");
            check(WidgetSourceAudit.Discover(null).IsEnvironmentDegradation, "发布环境降级标记失效");

            // ── 14. 遥测装配倒查验证（反例精准捕获、正例放行、非数据控件豁免）──
            string unboundReadoutSrc = BuildSyntheticWidget(
                "        public void InitControls() {\n" +
                "            this.Controls.Register(new WidgetReadoutControl(null, null, TextStyleRole.PrimaryValue, \"UnboundSpeed\", \"未绑读数\"));\n" +
                "        }");
            var unboundReport = Scan(new[] { MakeFile("UnboundReadout.cs", unboundReadoutSrc) });
            check(unboundReport.CountByRule(WidgetSpecRules.TelemetryAssemblyWarning) == 1, "未装配遥测 Token 的 WidgetReadoutControl 倒查未触发警告");
            check(unboundReport.Violations.Any(v => v.Description.Contains("UnboundSpeed") && v.Description.Contains("未支持标准化遥测装配")), "未装配警告文案未包含微控件名或未命中标准文案");

            string boundReadoutSrc = BuildSyntheticWidget(
                "        public void InitControls() {\n" +
                "            this.Controls.Register(new WidgetReadoutControl(\"spd\", \"Speed\", null, null, null, TextStyleRole.PrimaryValue, \"{SPD}\"));\n" +
                "        }");
            var boundReport = Scan(new[] { MakeFile("BoundReadout.cs", boundReadoutSrc) });
            check(boundReport.CountByRule(WidgetSpecRules.TelemetryAssemblyWarning) == 0, "已装配有效 Token 的微控件被误报警告 (假阳性)");

            string unboundDslSrc = BuildSyntheticWidget("        public TextWidget Value = TextWidget.Value();");
            var unboundDslReport = Scan(new[] { MakeFile("UnboundDsl.cs", unboundDslSrc) });
            check(unboundDslReport.CountByRule(WidgetSpecRules.TelemetryAssemblyWarning) == 1, "未指定 Token 的 TextWidget.Value() 倒查未触发警告");

            string nonDataSrc = BuildSyntheticWidget(
                "        public void InitControls() {\n" +
                "            this.Controls.Register(WidgetControlManager.WrapElement(this, \"card_bg\", \"Background\", null));\n" +
                "        }");
            var nonDataReport = Scan(new[] { MakeFile("NonData.cs", nonDataSrc) });
            check(nonDataReport.CountByRule(WidgetSpecRules.TelemetryAssemblyWarning) == 0, "非数据驱动型底板图元控件被误报警告 (假阳性)");

            // ── 15. 语法级守卫：坏语法必须被捕获（否则类声明从语法树消失 → 组件脱离作用域 → 假绿）──
            RoslynAstHelper.ParseTreeChecked("class A { void M() { var x = ; } }", out List<string> brokenErrors);
            check(brokenErrors.Count > 0, "语法错误未被 ParseTreeChecked 捕获（解析失败会被静默放行，审计结论不可信）");

            RoslynAstHelper.ParseTreeChecked("class A { void M() { var x = 1; } }", out List<string> cleanErrors);
            check(cleanErrors.Count == 0, "合法源码被 ParseTreeChecked 误判为语法错误");

            // ── 16. 继承链消歧：跨命名空间同名基类无法唯一确定时必须显式上报 ──
            string dupA = "namespace A { public class Dup : BaseFlightWidget { } }";
            string dupB = "namespace B { public class Dup : BaseFlightWidget { } }";
            string leafUnresolvable = "namespace C { [FlightWidget(\"leaf_c\")] public class LeafC : Dup { } }";
            var ambiguousReport = Scan(new[]
            {
                MakeFile("DupA.cs", dupA),
                MakeFile("DupB.cs", dupB),
                MakeFile("LeafC.cs", leafUnresolvable)
            });
            check(ambiguousReport.CountByRule(WidgetSpecRules.KernelInheritanceAmbiguity) >= 1,
                "跨命名空间同名基类未被显式上报（继承链会静默错位，SPEC-001..008 全部不可靠）");

            // 反向：写了全限定基类名时可唯一消歧，不得误报歧义
            string leafResolvable = "namespace C { [FlightWidget(\"leaf_d\")] public class LeafD : A.Dup { } }";
            var resolvedReport = Scan(new[]
            {
                MakeFile("DupA2.cs", dupA),
                MakeFile("DupB2.cs", dupB),
                MakeFile("LeafD.cs", leafResolvable)
            });
            check(resolvedReport.CountByRule(WidgetSpecRules.KernelInheritanceAmbiguity) == 0,
                "全限定基类名可唯一消歧时被误报为歧义");

            // 反向 2：写了 using A; 导入基类命名空间时可唯一消歧，不得误报歧义
            string leafUsingResolvable = "using A;\nnamespace C { [FlightWidget(\"leaf_e\")] public class LeafE : Dup { } }";
            var usingResolvedReport = Scan(new[]
            {
                MakeFile("DupA3.cs", dupA),
                MakeFile("DupB3.cs", dupB),
                MakeFile("LeafE.cs", leafUsingResolvable)
            });
            check(usingResolvedReport.CountByRule(WidgetSpecRules.KernelInheritanceAmbiguity) == 0,
                "文件顶部 using 命名空间可唯一消歧时被误报为歧义");

            // 反向 3：写了 using 别名时可唯一消歧，不得误报歧义
            string leafAliasResolvable = "using TargetDup = B.Dup;\nnamespace C { [FlightWidget(\"leaf_f\")] public class LeafF : TargetDup { } }";
            var aliasResolvedReport = Scan(new[]
            {
                MakeFile("DupA4.cs", dupA),
                MakeFile("DupB4.cs", dupB),
                MakeFile("LeafF.cs", leafAliasResolvable)
            });
            check(aliasResolvedReport.CountByRule(WidgetSpecRules.KernelInheritanceAmbiguity) == 0,
                "文件顶部 using 别名可唯一消歧时被误报为歧义");

            // ── 17. 构造函数签名表交叉校验：源码出现未登记的带 token 重载必须报内核错误 ──
            string ctorDrift = "namespace N { public class WidgetReadoutControl { "
                             + "public WidgetReadoutControl(string id, string token) { } } }";
            var driftReport = Scan(new[] { MakeFile("CtorDrift.cs", ctorDrift) });
            check(driftReport.CountByRule(WidgetSpecRules.KernelContractDrift) > 0,
                "构造函数签名表与实际声明不一致时未报内核错误（重载变更会让遥测判定静默翻转）");

            // ── 18. 未登记的构造重载形状必须显式上报，不得静默放过 ──
            string unknownOverload = BuildSyntheticWidget(
                "        public void InitControls() {\n"
                + "            this.Controls.Register(new WidgetReadoutControl(null, null, null, null, null, null, null, null, null, null, null, null, null));\n"
                + "        }");
            var unknownOverloadReport = Scan(new[] { MakeFile("UnknownOverload.cs", unknownOverload) });
            check(unknownOverloadReport.CountByRule(WidgetSpecRules.TelemetryAssemblyWarning) == 1
                  && unknownOverloadReport.Violations.Any(v => v.Description.Contains("未登记")),
                "未登记的构造重载形状未被显式上报（旧实现在此处静默落空 → 漏洞）");

            // ── 19. 同行去重修复：同一行上两个不同的场景查询必须各报一条 ──
            string sameLineSrc = BuildSyntheticWidget(
                "        private void Q() { var a = Camera.main; var b = FindObjectOfType<Camera>(); }");
            var sameLineReport = Scan(new[] { MakeFile("SameLine.cs", sameLineSrc) });
            check(sameLineReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 2,
                "同一行上的两个不同场景查询应各报一条（旧实现的 lastReportedLine 会丢掉第二条），实际 "
                + sameLineReport.CountByRule(WidgetSpecRules.NoSceneQueries));

            // ── 20. 全符号 CSharpCompilation 与 SemanticModel 语义编译自检 ──
            var semanticFailures = SemanticCompilationProvider.SelfTest();
            check(semanticFailures.Count == 0,
                "SemanticCompilationProvider 语义编译自检失败: " + string.Join("; ", semanticFailures));

            // ── 21. 语义继承图通道自证 ──
            // 【为什么必须有】此前 121 条自检全部走 Scan(IEnumerable) → WidgetClassGraph.Build(list)，
            // semanticContext 取默认 null —— 也就是说语义判定点位（Symbol 决议 / 语义分支 / GetSymbolInfo）
            // 被改坏时门禁照样全绿。下面这组用例专门把它们拉进覆盖范围。
            string semanticWidget =
                "using System;\n"
                + "namespace N\n"
                + "{\n"
                + SyntheticTierEnumLine
                + "    public class MidWidget : BaseFlightWidget { }\n"
                + "    [FlightWidget(\"fake_semantic\")]\n"
                + "    public class SemanticWidget : MidWidget\n"
                + "    {\n"
                + "        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;\n"
                + "        public override void ApplyTheme(ThemeConfig theme) { }\n"
                + "        public override void OnUpdateTelemetry(IFlightTelemetry t) { }\n"
                + "        public override void OnDestroy() { base.OnDestroy(); }\n"
                + "    }\n"
                + "}\n";

            var semanticFiles = new List<WidgetSourceFile> { MakeFile("SemanticWidget.cs", semanticWidget) };
            var semanticContext = SemanticCompilationProvider.BuildCompilation(semanticFiles);
            var semanticGraph = WidgetClassGraph.Build(semanticFiles, semanticContext);

            // 21.1 语义通道必须真的被启用：每个类都要解析出 ISymbol。
            // 否则 IsDescendantOfContractRoot 会静默走语法回退分支，SPEC-001..008 的"语义权威"名不副实。
            var noSymbol = semanticGraph.All.Where(n => n.Symbol == null).Select(n => n.Name).ToList();
            check(noSymbol.Count == 0,
                "语义继承图未启用：以下类未解析出 ISymbol（" + string.Join(", ", noSymbol) + "）→ 语义判定点位已退化为语法回退");

            // 21.2 两层间接继承必须经 Symbol.BaseType 链正确判定为契约真后代
            var targetNode = semanticGraph.All.FirstOrDefault(n => n.Name == "SemanticWidget");
            check(targetNode != null && targetNode.IsWidgetContractClass,
                "语义继承链判定失效：SemanticWidget（经 MidWidget 间接继承 " + WidgetSpecRules.ContractRootType
                + "）未被识别为契约真后代 → Symbol.BaseType 决议或语义分支已失效");

            // 21.3 语义与语法两条继承判定路径的结论必须一致（任一侧被改坏都会暴露）
            var syntaxGraph = WidgetClassGraph.Build(semanticFiles);
            check(semanticGraph.ContractClasses.Count == syntaxGraph.ContractClasses.Count,
                "语义/语法两条继承判定路径结论不一致：语义=" + semanticGraph.ContractClasses.Count
                + " / 语法=" + syntaxGraph.ContractClasses.Count);

            // 21.4 语义编译健康度指标必须接上 —— 合成源引用了未定义的 ThemeConfig/IFlightTelemetry，
            // 编译必然有错误；若计数仍为 0，说明 MFP-KERNEL-SEMANTIC-UNHEALTHY 这道门形同虚设。
            check(semanticContext.CompilationErrorCount > 0,
                "语义编译健康度指标未接上：注入未定义类型的合成源后 CompilationErrorCount 仍为 0");
            check(semanticContext.ErrorCountForFile("SemanticWidget.cs") > 0,
                "语义编译健康度未按文件归集：ErrorCountForFile 返回 0 → 组件作用域零错误守卫失效");

            // 21.5 语义编译集一致性：csproj 解析器必须与 C# 侧清单逐项一致（任一侧单方面增删都要暴露）
            string repoRootForParity = ResolveRepositoryRoot();
            if (!string.IsNullOrEmpty(repoRootForParity))
            {
                string csprojPath = Path.Combine(repoRootForParity,
                    WidgetSpecRules.PluginCsprojRelativePath.Replace('/', Path.DirectorySeparatorChar));
                check(File.Exists(csprojPath), "语义编译集一致性守卫：找不到插件 csproj: " + csprojPath);
                if (File.Exists(csprojPath))
                {
                    var fromCsproj = WidgetSpecRules.ParseCompileRemoveFileNames(File.ReadAllText(csprojPath));
                    bool parity = fromCsproj.Count == WidgetSpecRules.PluginExcludedAuditFiles.Count
                                  && !WidgetSpecRules.PluginExcludedAuditFiles.Any(
                                        n => !fromCsproj.Contains(n, StringComparer.OrdinalIgnoreCase));
                    check(parity,
                        "语义编译集两份清单分裂：csproj <Compile Remove> = [" + string.Join(", ", fromCsproj)
                        + "] vs PluginExcludedAuditFiles = [" + string.Join(", ", WidgetSpecRules.PluginExcludedAuditFiles) + "]");
                }
            }

            // 21.6 降级必须可见：无语义上下文时 MFP-KERNEL-SEMANTIC-DEGRADED 必须出现在报告里，
            // 而不是只打一行控制台提示（这条是"降级静默"缺陷的回归守卫）。
            var consistencyReport = Scan(new[] { MakeFile("Consistency.cs", BuildSyntheticWidget(string.Empty)) });
            check(consistencyReport.CountByRule(WidgetSpecRules.KernelSemanticDegraded) == 1,
                "语义降级不可见：Scan(IEnumerable) 不构建语义上下文，报告里应出现 1 条 MFP-KERNEL-SEMANTIC-DEGRADED，实测 "
                + consistencyReport.CountByRule(WidgetSpecRules.KernelSemanticDegraded));

            // ── 22. 五个语义点位的"被采用"覆盖率守卫 ──
            // 【为什么必须是覆盖率而不是结果】语义结论与语法回退结论可能恰好相同，
            // 所以"判定结果对不对"无法证明某个语义点位还在。只有"被采用过"能证明。
            // 把任一语义分支整段删掉时，下面的计数就会归零 → 门禁变红。
            var semanticScanReport = Scan(semanticFiles, semanticContext);

            check(semanticScanReport.SemanticVerdictCount > 0,
                "语义点位 1 已失效：语义上下文存在但报告 SemanticVerdictCount = 0（GetDeclaredSymbol → node.Symbol 链路被拆除）");
            check(semanticGraph.SemanticResolvedBaseCount > 0,
                "语义点位 2 已失效：直接基类未经 Symbol.BaseType 决议（SemanticResolvedBaseCount = 0），继承链已退化回语法消歧");
            check(semanticGraph.SemanticDescendantVerdictCount > 0,
                "语义点位 3 已失效：契约归属判定未走语义分支（SemanticDescendantVerdictCount = 0）");

            // 22.1 语义点位 5（场景查询符号决议）的覆盖率 + 能力增量双断言。
            // 用类型别名把 GameObject 改名：语法表按"接收者最右标识符 == GameObject"匹配，结构上必然漏检；
            // 语义符号决议拿到的是真正的 UnityEngine.GameObject，应当捕获。
            string aliasQuerySrc =
                "using System;\n"
                + "using UnityEngine;\n"
                + "using GO = UnityEngine.GameObject;\n"
                + "namespace N\n{\n"
                + SyntheticTierEnumLine
                + "    [FlightWidget(\"fake_alias_query\")]\n"
                + "    public class AliasQueryWidget : BaseFlightWidget\n    {\n"
                + "        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;\n"
                + "        public override void ApplyTheme(ThemeConfig theme) { }\n"
                + "        public override void OnUpdateTelemetry(IFlightTelemetry t) { }\n"
                + "        public override void OnDestroy() { base.OnDestroy(); }\n"
                + "        private void Q() { var o = GO.Find(\"HUD\"); }\n"
                + "    }\n}\n";

            var aliasFiles = new List<WidgetSourceFile> { MakeFile("AliasQueryWidget.cs", aliasQuerySrc) };
            var aliasContext = SemanticCompilationProvider.BuildCompilation(aliasFiles);
            var aliasSemanticReport = Scan(aliasFiles, aliasContext);
            var aliasSyntaxReport = Scan(aliasFiles);

            check(aliasSemanticReport.SemanticVerdictCount > aliasSyntaxReport.SemanticVerdictCount,
                "SPEC-007 语义点位 5 已失效：带语义上下文与纯语法两次扫描的语义决议计数无差异");
            check(aliasSemanticReport.SemanticInvocationVerdictCount > 0,
                "SPEC-007 语义点位 5 · 调用分支已失效：InvocationExpression 遍历未产生语义决议计数");
            check(aliasSemanticReport.SemanticMemberAccessVerdictCount > 0,
                "SPEC-007 语义点位 5 · 成员访问分支已失效：MemberAccessExpression 遍历未产生语义决议计数");
            check(aliasSyntaxReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 0,
                "SPEC-007 语法回退基线漂移：类型别名 GO.Find(\"HUD\") 本应漏检 —— 若已能捕获，说明别名场景变了，"
                + "需重新评估语义增量后再更新本用例，实测 " + aliasSyntaxReport.CountByRule(WidgetSpecRules.NoSceneQueries) + " 条");
            if (aliasContext.IsFullSemanticActive)
            {
                check(aliasSemanticReport.CountByRule(WidgetSpecRules.NoSceneQueries) == 1,
                    "SPEC-007 语义能力退化：类型别名下的场景查询 GO.Find(\"HUD\") 未被语义符号决议捕获，实测 "
                    + aliasSemanticReport.CountByRule(WidgetSpecRules.NoSceneQueries) + " 条（期望 1 条）");
            }

            LastSelfTestCaseCount = cases;
            return failures;
        }

        private static string Describe(WidgetSourceAuditReport report)
        {
            var parts = new List<string>();
            for (int i = 0; i < report.Violations.Count; i++)
                parts.Add(report.Violations[i].ToString());
            return parts.Count == 0 ? "无违规" : string.Join("; ", parts);
        }

        private static WidgetSourceFile MakeFile(string name, string text) =>
            new WidgetSourceFile { Name = name, Path = name, Text = text };

        /// <summary>合成源里的阶梯枚举声明行（自检用例需要按整行移除，故单独声明）</summary>
        private const string SyntheticTierEnumLine = "    enum WidgetRefreshTier { Critical, Standard, Relaxed, UltraLow }\n";

        /// <summary>合成组件：自带阶梯枚举声明，便于验证"取值集合从源码派生"的判定链路</summary>
        private static string BuildSyntheticWidget(string extra)
        {
            return "using System;\n"
                 + "namespace N\n"
                 + "{\n"
                 + SyntheticTierEnumLine
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

        private static bool ContainsSceneQueryAst(string snippet)
        {
            var root = RoslynAstHelper.ParseRoot(snippet);

            bool hasStatic = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Any(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) &&
                          WidgetSpecRules.IsBannedStaticImportOwner(u.Name.ToString().Split('.').Last()));
            if (hasStatic) return true;

            bool hasInv = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => WidgetSpecRules.IsSceneQueryApi(inv.Expression.ToString(), true));
            if (hasInv) return true;

            return root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(ma => WidgetSpecRules.IsSceneQueryApi(ma.ToString(), false));
        }
    }
}