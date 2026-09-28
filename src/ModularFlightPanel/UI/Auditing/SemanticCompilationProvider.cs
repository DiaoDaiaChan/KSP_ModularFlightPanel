using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI.Auditing
{
    /// <summary>
    /// 语义编译上下文：持有一整套完整的 CSharpCompilation 与全量 SemanticModel，
    /// 链接 Unity 引擎与 KSP 核心程序集，提供绝对权威的类型图、符号决议与常量折叠。
    /// </summary>
    public sealed class SemanticCompilationContext
    {
        public CSharpCompilation Compilation { get; }
        public IReadOnlyList<MetadataReference> References { get; }
        public IReadOnlyList<string> ResolvedReferencePaths { get; }
        public IReadOnlyList<string> AssemblyWarnings { get; }
        public bool IsFullSemanticActive { get; }

        /// <summary>
        /// 全量语义编译的诊断 ERROR 数。
        /// 【为什么必须暴露】门禁此前从不调用 GetDiagnostics()，"链接到 17 个程序集"被当作 L4 可用的证据，
        /// 而实际编译单元里含影子类型与 2217 个错误 —— 符号决议可能落在错误类型上。
        /// 现在它是可计数、可棘轮化的一等指标。
        /// </summary>
        public int CompilationErrorCount { get; }

        private readonly Dictionary<string, SyntaxTree> _pathOrNameToTree = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SyntaxTree, SemanticModel> _treeToModel = new Dictionary<SyntaxTree, SemanticModel>();
        private readonly Dictionary<string, int> _errorCountByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _errorCountByFileName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public SemanticCompilationContext(
            CSharpCompilation compilation,
            IReadOnlyList<MetadataReference> references,
            IReadOnlyList<string> resolvedPaths,
            IReadOnlyList<string> warnings,
            Dictionary<string, SyntaxTree> trees,
            bool isFullSemanticActive,
            int compilationErrorCount = 0,
            Dictionary<string, int> errorCountByPath = null)
        {
            Compilation = compilation;
            References = references;
            ResolvedReferencePaths = resolvedPaths;
            AssemblyWarnings = warnings;
            IsFullSemanticActive = isFullSemanticActive;
            CompilationErrorCount = compilationErrorCount;

            foreach (var kv in trees)
            {
                _pathOrNameToTree[kv.Key] = kv.Value;
            }

            if (errorCountByPath != null)
            {
                foreach (var kv in errorCountByPath)
                {
                    _errorCountByPath[kv.Key] = kv.Value;

                    // 同名文件取最大值（保守）：宁可把该文件判为"符号不可信"，也不漏报
                    string name = Path.GetFileName(kv.Key);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!_errorCountByFileName.TryGetValue(name, out int existing) || kv.Value > existing)
                    {
                        _errorCountByFileName[name] = kv.Value;
                    }
                }
            }
        }

        /// <summary>
        /// 指定源文件在语义编译中的 ERROR 数。0 表示该文件的符号决议可信；
        /// 大于 0 表示该文件里至少有一个符号可能落在错误类型上，依赖它的 SPEC 判定必须视为不可靠。
        /// </summary>
        public int ErrorCountForFile(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return 0;
            if (_errorCountByPath.TryGetValue(pathOrName, out int byPath)) return byPath;

            string name = Path.GetFileName(pathOrName);
            if (!string.IsNullOrEmpty(name) && _errorCountByFileName.TryGetValue(name, out int byName)) return byName;
            return 0;
        }

        /// <summary>根据源文件名或文件完整路径获取其对应的 SemanticModel</summary>
        public SemanticModel GetSemanticModel(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName) || Compilation == null) return null;

            if (_pathOrNameToTree.TryGetValue(pathOrName, out SyntaxTree tree))
            {
                return GetModelForTree(tree);
            }

            // 尝试以文件名检索
            string fileName = Path.GetFileName(pathOrName);
            if (!string.IsNullOrEmpty(fileName) && _pathOrNameToTree.TryGetValue(fileName, out tree))
            {
                return GetModelForTree(tree);
            }

            return null;
        }

        /// <summary>根据源文件名或路径获取所属的 SyntaxTree</summary>
        public SyntaxTree GetSyntaxTree(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;
            if (_pathOrNameToTree.TryGetValue(pathOrName, out SyntaxTree tree)) return tree;
            string fileName = Path.GetFileName(pathOrName);
            if (!string.IsNullOrEmpty(fileName) && _pathOrNameToTree.TryGetValue(fileName, out tree)) return tree;
            return null;
        }

        /// <summary>根据源文件名或路径获取根节点 CompilationUnitSyntax</summary>
        public CompilationUnitSyntax GetRoot(string pathOrName)
        {
            var tree = GetSyntaxTree(pathOrName);
            return tree != null ? (CompilationUnitSyntax)tree.GetRoot() : null;
        }

        /// <summary>根据语法节点直接获取其所属语法树的 SemanticModel</summary>
        public SemanticModel GetSemanticModel(SyntaxNode node)
        {
            if (node == null || Compilation == null) return null;
            return GetModelForTree(node.SyntaxTree);
        }

        private SemanticModel GetModelForTree(SyntaxTree tree)
        {
            if (tree == null || Compilation == null) return null;
            lock (_treeToModel)
            {
                if (!_treeToModel.TryGetValue(tree, out SemanticModel model))
                {
                    model = Compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                    _treeToModel[tree] = model;
                }
                return model;
            }
        }
    }

    /// <summary>
    /// Roslyn 语义编译提供者：负责探测 Unity / KSP 依赖、构建 CSharpCompilation 并派发 SemanticModel
    /// </summary>
    public static class SemanticCompilationProvider
    {
        private static readonly ConcurrentDictionary<string, MetadataReference> ReferenceCache =
            new ConcurrentDictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultManagedProbeFolders = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed",
            @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed",
            @"C:\Program Files\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed"
        };

        private static readonly string[] CriticalKspAssemblyNames = new[]
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "UnityEngine.dll",
            "UnityEngine.CoreModule.dll",
            "UnityEngine.UI.dll",
            "UnityEngine.IMGUIModule.dll",
            "UnityEngine.TextRenderingModule.dll",
            "UnityEngine.InputLegacyModule.dll",
            "UnityEngine.PhysicsModule.dll",
            "UnityEngine.ImageConversionModule.dll",
            "Assembly-CSharp.dll"
        };

        /// <summary>探测并解析 KSP 与 Unity Managed 目录路径</summary>
        public static string ResolveManagedDirectory(string repoRoot = null)
        {
            // 1. 优先检查环境变量
            string env = Environment.GetEnvironmentVariable("KSP_MANAGED_PATH");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            string kspRootEnv = Environment.GetEnvironmentVariable("KSPRoot");
            if (!string.IsNullOrEmpty(kspRootEnv))
            {
                string p = Path.Combine(kspRootEnv, "KSP_x64_Data", "Managed");
                if (Directory.Exists(p)) return p;
            }

            // 2. 检查默认安装路径
            foreach (var probe in DefaultManagedProbeFolders)
            {
                if (Directory.Exists(probe) && File.Exists(Path.Combine(probe, "UnityEngine.dll")))
                {
                    return probe;
                }
            }

            // 3. 检查从仓库上层向上溯源
            if (!string.IsNullOrEmpty(repoRoot))
            {
                try
                {
                    var dir = new DirectoryInfo(repoRoot);
                    while (dir != null)
                    {
                        string p = Path.Combine(dir.FullName, "KSP_x64_Data", "Managed");
                        if (Directory.Exists(p) && File.Exists(Path.Combine(p, "UnityEngine.dll")))
                        {
                            return p;
                        }
                        dir = dir.Parent;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 收集 Unity / KSP 及运行时程序集的 MetadataReference
        /// </summary>
        public static List<MetadataReference> CollectMetadataReferences(
            string repoRoot,
            out List<string> resolvedPaths,
            out List<string> warnings,
            out bool isFullKspEnvironment)
        {
            resolvedPaths = new List<string>();
            warnings = new List<string>();
            var references = new List<MetadataReference>();

            string managedDir = ResolveManagedDirectory(repoRoot);
            isFullKspEnvironment = !string.IsNullOrEmpty(managedDir) && Directory.Exists(managedDir);

            if (isFullKspEnvironment)
            {
                // 加载 KSP 与 Unity 关键程序集
                foreach (string name in CriticalKspAssemblyNames)
                {
                    string path = Path.Combine(managedDir, name);
                    if (File.Exists(path))
                    {
                        var mr = GetOrCreateReference(path);
                        if (mr != null)
                        {
                            references.Add(mr);
                            resolvedPaths.Add(path);
                        }
                    }
                    else
                    {
                        warnings.Add($"KSP 程序集缺失: {name} (在 {managedDir})");
                    }
                }

                // 额外加载常用的可选模块
                string[] extraModules = new[]
                {
                    "UnityEngine.JSONSerializeModule.dll",
                    "UnityEngine.AssetBundleModule.dll",
                    "UnityEngine.TextCoreModule.dll",
                    "UnityEngine.UIModule.dll"
                };
                foreach (var extra in extraModules)
                {
                    string path = Path.Combine(managedDir, extra);
                    if (File.Exists(path))
                    {
                        var mr = GetOrCreateReference(path);
                        if (mr != null)
                        {
                            references.Add(mr);
                            resolvedPaths.Add(path);
                        }
                    }
                }

                // 加载 0Harmony (若存在)
                string harmonyPath = Path.Combine(managedDir, "..", "..", "GameData", "000_Harmony", "0Harmony.dll");
                try
                {
                    string fullHarmony = Path.GetFullPath(harmonyPath);
                    if (File.Exists(fullHarmony))
                    {
                        var mr = GetOrCreateReference(fullHarmony);
                        if (mr != null)
                        {
                            references.Add(mr);
                            resolvedPaths.Add(fullHarmony);
                        }
                    }
                }
                catch { }
            }
            else
            {
                warnings.Add("未探测到 KSP_x64_Data/Managed 目录，降级为加载当前主机 .NET 运行时程序集。");
                // 降级：加载当前 AppDomain 运行环境的基础库以维持基本语法编译
                var currentAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in currentAssemblies)
                {
                    if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                    try
                    {
                        var mr = GetOrCreateReference(asm.Location);
                        if (mr != null)
                        {
                            references.Add(mr);
                            resolvedPaths.Add(asm.Location);
                        }
                    }
                    catch { }
                }
            }

            return references;
        }

        private static MetadataReference GetOrCreateReference(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return ReferenceCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p));
        }

        /// <summary>
        /// 基于提供的源码集合构建完备的 CSharpCompilation 并返回语义上下文
        /// </summary>
        public static SemanticCompilationContext BuildCompilation(
            IList<WidgetSourceFile> sources,
            string repoRoot = null)
        {
            // 解析口径必须与语法回退路径完全一致（含 KSP_RUNTIME 预处理器符号），
            // 否则两条路径的可见代码集会被 #if 切开 —— 详见 RoslynAstHelper.UnifiedParseOptions。
            CSharpParseOptions parseOptions = RoslynAstHelper.UnifiedParseOptions;

            var trees = new List<SyntaxTree>();
            var pathMap = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);

            // 简名索引单独建：跨目录同名文件只允许"唯一"时才可用简名检索，
            // 旧实现是先到先得 → 同名文件静默让先注册者胜出，检索到错误的树。
            var nameToTree = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);
            var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    var file = sources[i];
                    if (file == null || string.IsNullOrEmpty(file.Text)) continue;

                    string path = file.Path ?? file.Name ?? $"source_{i}.cs";
                    var tree = RoslynAstHelper.ParseTree(file.Text, path);
                    trees.Add(tree);

                    if (!string.IsNullOrEmpty(file.Path) && !pathMap.ContainsKey(file.Path))
                    {
                        pathMap[file.Path] = tree;
                    }

                    if (!string.IsNullOrEmpty(file.Name))
                    {
                        if (nameToTree.TryGetValue(file.Name, out SyntaxTree existing) && existing != tree)
                        {
                            ambiguousNames.Add(file.Name);
                        }
                        else if (!nameToTree.ContainsKey(file.Name))
                        {
                            nameToTree[file.Name] = tree;
                        }
                    }
                }
            }

            // 简名只在无歧义时注册；歧义简名不进索引（调用侧请用完整路径），避免"取到别人的语法树"
            foreach (var kv in nameToTree)
            {
                if (!ambiguousNames.Contains(kv.Key) && !pathMap.ContainsKey(kv.Key))
                {
                    pathMap[kv.Key] = kv.Value;
                }
            }

            var references = CollectMetadataReferences(repoRoot, out var resolvedPaths, out var warnings, out bool isFullKsp);

            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release,
                platform: Platform.AnyCpu,
                warningLevel: 0
            );

            var compilation = CSharpCompilation.Create(
                assemblyName: "ModularFlightPanel.SemanticAuditAssembly",
                syntaxTrees: trees,
                references: references,
                options: compilationOptions
            );

            // 编译诊断体检：L4 的"权威性"必须可计数，否则"链接成功"会被误当成"编译干净"。
            int errorCount = 0;
            var errorCountByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Diagnostic diagnostic in compilation.GetDiagnostics())
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error) continue;
                errorCount++;

                string diagPath = diagnostic.Location.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(diagPath)) diagPath = "<no-tree>";
                errorCountByPath[diagPath] = errorCountByPath.TryGetValue(diagPath, out int c) ? c + 1 : 1;
            }

            return new SemanticCompilationContext(
                compilation,
                references,
                resolvedPaths,
                warnings,
                pathMap,
                isFullSemanticActive: isFullKsp,
                compilationErrorCount: errorCount,
                errorCountByPath: errorCountByPath
            );
        }

        // =========================================================================
        // 语义辅助工具集 (Semantic Query Helpers)
        // =========================================================================

        /// <summary>判断 typeSymbol 是否为指定全限定名类型的真派生类（沿继承链递归）</summary>
        public static bool InheritsFrom(ITypeSymbol typeSymbol, string targetFullMetadataName)
        {
            if (typeSymbol == null || string.IsNullOrEmpty(targetFullMetadataName)) return false;

            INamedTypeSymbol current = typeSymbol.BaseType;
            while (current != null)
            {
                if (string.Equals(current.ToDisplayString(), targetFullMetadataName, StringComparison.Ordinal))
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        /// <summary>判断 typeSymbol 是否等于目标类型，或继承自目标类型</summary>
        public static bool IsOrInheritsFrom(ITypeSymbol typeSymbol, string targetFullMetadataName)
        {
            if (typeSymbol == null || string.IsNullOrEmpty(targetFullMetadataName)) return false;
            if (string.Equals(typeSymbol.ToDisplayString(), targetFullMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
            return InheritsFrom(typeSymbol, targetFullMetadataName);
        }

        /// <summary>判断类型是否为 UnityEngine.Color 或 UnityEngine.Color32</summary>
        public static bool IsColorOrColor32(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return false;
            string name = typeSymbol.ToDisplayString();
            return name == "UnityEngine.Color" || name == "UnityEngine.Color32";
        }

        /// <summary>
        /// 精确判定符号是否属于场景查询黑名单（SPEC-007）：
        /// 包含 UnityEngine.GameObject.Find*, UnityEngine.Object.Find*, UnityEngine.Camera.main/current/allCameras 等
        /// </summary>
        public static bool IsSceneQuerySymbol(ISymbol symbol)
        {
            if (symbol == null) return false;

            string containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;

            // 1. UnityEngine.Camera 的黑名单属性
            if (containingType == "UnityEngine.Camera")
            {
                if (symbol is IPropertySymbol prop)
                {
                    if (prop.Name == "main" || prop.Name == "current" || prop.Name == "allCameras")
                    {
                        return true;
                    }
                }
                if (symbol is IMethodSymbol method && (method.Name == "GetAllCameras" || method.Name == "allCameras"))
                {
                    return true;
                }
            }

            // 2. UnityEngine.GameObject 静态查询
            if (containingType == "UnityEngine.GameObject")
            {
                if (symbol.Name.StartsWith("Find", StringComparison.Ordinal)) return true;
                if (symbol.Name == "sceneCount" || symbol.Name == "GetRootGameObjects") return true;
            }

            // 3. UnityEngine.Object 场景对象查询
            if (containingType == "UnityEngine.Object")
            {
                if (symbol.Name.StartsWith("Find", StringComparison.Ordinal)) return true;
            }

            // 4. UnityEngine.SceneManagement.Scene
            if (containingType == "UnityEngine.SceneManagement.Scene")
            {
                if (symbol.Name == "GetRootGameObjects") return true;
            }

            return false;
        }

        /// <summary>自检套件：验证 SemanticCompilationProvider 在有/无外部程序集下的工作状态</summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();

            string code = @"
                using System;
                namespace TestNs
                {
                    public class BaseClass {}
                    public class ChildClass : BaseClass {}
                }
            ";

            var sources = new List<WidgetSourceFile>
            {
                new WidgetSourceFile { Name = "Test.cs", Text = code }
            };

            var ctx = BuildCompilation(sources);
            if (ctx == null || ctx.Compilation == null)
            {
                failures.Add("SemanticCompilationContext 创建失败");
                return failures;
            }

            var model = ctx.GetSemanticModel("Test.cs");
            if (model == null)
            {
                failures.Add("GetSemanticModel 获取失败");
                return failures;
            }

            var root = model.SyntaxTree.GetRoot();
            var childDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == "ChildClass");
            if (childDecl == null)
            {
                failures.Add("未在语法树中定位到 ChildClass");
                return failures;
            }

            var childSymbol = model.GetDeclaredSymbol(childDecl) as INamedTypeSymbol;
            if (childSymbol == null)
            {
                failures.Add("ChildClass 符号解析失败");
                return failures;
            }

            if (!InheritsFrom(childSymbol, "TestNs.BaseClass"))
            {
                failures.Add("InheritsFrom 无法正确解析继承自 TestNs.BaseClass");
            }

            // ── 守卫 1：解析口径一致性 —— 语义树与语法回退树必须看到同一份代码 ──
            // 历史缺陷：语义编译自建 parseOptions（漏 KSP_RUNTIME），语法回退带宏 → 两棵树的可见代码集分裂，
            // 而 WidgetClassGraph 是"语义树优先"，受宏保护的组件代码整段脱审。
            string guarded = @"
                namespace TestNs
                {
                    public class Outside { }
            #if KSP_RUNTIME
                    public class Inside { }
            #endif
                }";
            var guardedSources = new List<WidgetSourceFile>
            {
                new WidgetSourceFile { Name = "Guarded.cs", Path = "Guarded.cs", Text = guarded }
            };
            var guardedCtx = BuildCompilation(guardedSources);
            var guardedModel = guardedCtx.GetSemanticModel("Guarded.cs");
            if (guardedModel == null)
            {
                failures.Add("解析口径守卫：Guarded.cs 未取得 SemanticModel");
            }
            else
            {
                int semanticClasses = guardedModel.SyntaxTree.GetRoot()
                    .DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
                int syntaxClasses = RoslynAstHelper.ParseRoot(guarded)
                    .DescendantNodes().OfType<ClassDeclarationSyntax>().Count();

                if (semanticClasses != 2)
                {
                    failures.Add($"解析口径守卫：#if {WidgetSpecRules.RuntimePreprocessorSymbol} 块内的类声明丢失"
                               + $"（语义树实测 {semanticClasses} 个，期望 2）→ 组件内受宏保护的代码会脱审");
                }
                if (semanticClasses != syntaxClasses)
                {
                    failures.Add($"解析口径分裂：#if 块内类声明 语义树={semanticClasses} / 语法回退树={syntaxClasses}"
                               + " → 两条解析路径必须共用 RoslynAstHelper.UnifiedParseOptions");
                }
            }

            // ── 守卫 2：体检指标自证 —— 注入真实编译错误，CompilationErrorCount 必须反映出来 ──
            // 否则"MFP-KERNEL-SEMANTIC-UNHEALTHY"这道门永远是绿的（等于没有这道门）。
            var brokenSources = new List<WidgetSourceFile>
            {
                new WidgetSourceFile
                {
                    Name = "Broken.cs",
                    Path = "Broken.cs",
                    Text = "namespace Broken { class C { void M() { var x = TotallyUndefined.Nope; } } }"
                }
            };
            var brokenCtx = BuildCompilation(brokenSources);
            if (brokenCtx.CompilationErrorCount <= 0)
            {
                failures.Add("CompilationErrorCount 未反映真实编译错误（注入未定义类型后仍 <= 0）→ 语义健康度门禁失效");
            }
            if (brokenCtx.ErrorCountForFile("Broken.cs") <= 0)
            {
                failures.Add("ErrorCountForFile 未按文件归集编译错误 → 组件作用域零错误守卫会失效");
            }

            return failures;
        }
    }
}
