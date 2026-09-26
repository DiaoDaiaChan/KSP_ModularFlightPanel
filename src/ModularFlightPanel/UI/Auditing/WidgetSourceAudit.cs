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
        public WidgetClassNode Base;              // 解析后的直接基类（若声明在本次扫描集合内）

        public bool IsAbstract;
        public bool IsContractRoot;               // 契约根自身（BaseFlightWidget）
        public bool IsWidgetContractClass;        // 契约真后代（直接或间接派生自契约根）
        public bool IsObsoleteShim;               // 继承链上出现 [Obsolete] 的向后兼容垫片
        internal bool? DescendantCache;           // 契约归属判定缓存（链断裂时需沿已解析基类回溯）

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
    /// 组件继承图：全部规则判定都基于这份结构模型，而不是文件名 / 目录 / 文本包含关系。
    /// </summary>
    public sealed class WidgetClassGraph
    {
        public readonly List<WidgetClassNode> All = new List<WidgetClassNode>();
        public readonly Dictionary<string, WidgetClassNode> ByName = new Dictionary<string, WidgetClassNode>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, CompilationUnitSyntax> Roots = new Dictionary<string, CompilationUnitSyntax>(StringComparer.OrdinalIgnoreCase);

        /// <summary>刷新阶梯合法取值集合 —— 直接从源码里的枚举声明派生（不再写死成员名）</summary>
        public readonly HashSet<string> TierMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>审计作用域：契约真后代中排除 [Obsolete] 兼容垫片</summary>
        public readonly List<WidgetClassNode> ContractClasses = new List<WidgetClassNode>();

        /// <summary>审计作用域文件：声明了作用域内组件的源文件（按内容判定，与目录位置无关）</summary>
        public readonly HashSet<string> ScopedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>作用域文件名集合（仅文件名，用于计数展示）</summary>
        public readonly HashSet<string> ScopedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static WidgetClassGraph Build(IList<WidgetSourceFile> files)
        {
            var graph = new WidgetClassGraph();
            if (files == null) return graph;

            // ── 1. 解析全部源文件并建立类节点 ──
            for (int i = 0; i < files.Count; i++)
            {
                WidgetSourceFile file = files[i];
                if (file == null || string.IsNullOrEmpty(file.Text)) continue;

                CompilationUnitSyntax root = RoslynAstHelper.ParseRoot(file.Text);
                graph.Roots[file.Path ?? file.Name ?? string.Empty] = root;
                graph.ExtractTierMembers(root);

                foreach (var cd in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var node = new WidgetClassNode
                    {
                        Name = cd.Identifier.Text,
                        FileName = file.Name,
                        FilePath = file.Path,
                        Decl = cd,
                        BaseNames = RoslynAstHelper.GetBaseTypeNames(cd),
                        IsAbstract = RoslynAstHelper.HasModifier(cd, SyntaxKind.AbstractKeyword),
                        IsContractRoot = string.Equals(cd.Identifier.Text, WidgetSpecRules.ContractRootType, StringComparison.Ordinal)
                    };

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
                    graph.ByName[node.Name] = node;
                }
            }

            // ── 2. 解析继承链（同类名跨命名空间时后声明者胜出，与旧实现一致）──
            for (int i = 0; i < graph.All.Count; i++)
            {
                WidgetClassNode node = graph.All[i];
                for (int b = 0; b < node.BaseNames.Count; b++)
                {
                    if (graph.ByName.TryGetValue(node.BaseNames[b], out WidgetClassNode baseNode) && baseNode != node)
                    {
                        node.Base = baseNode;
                        break;
                    }
                }
            }

            // ── 3. 派生契约归属与兼容垫片标记（自根向下传播）──
            for (int i = 0; i < graph.All.Count; i++)
            {
                WidgetClassNode node = graph.All[i];
                node.IsWidgetContractClass = !node.IsContractRoot && node.IsDescendantOfContractRoot();
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
        /// </summary>
        public static bool IsDescendantOfContractRoot(this WidgetClassNode node)
        {
            if (node == null) return false;
            if (node.DescendantCache.HasValue) return node.DescendantCache.Value;

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
                result = IsDescendantOfContractRoot(node.Base);
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

        /// <summary>扫描完成但未发现任何组件类</summary>
        NoWidgetClasses
    }

    /// <summary>发现层结果：源集合 + 继承图 + 作用域 + 状态说明</summary>
    public sealed class WidgetDiscoveryResult
    {
        public WidgetDiscoveryStatus Status = WidgetDiscoveryStatus.Ok;
        public string Detail = string.Empty;
        public WidgetClassGraph Graph;
        public readonly List<WidgetSourceFile> Sources = new List<WidgetSourceFile>();
        public readonly List<string> ReadErrors = new List<string>();

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
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    string probe = Path.Combine(dir.FullName, "src", "ModularFlightPanel", "UI", "Widgets");
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

            string sourceRoot = Path.Combine(repoRoot, "src", "ModularFlightPanel");
            if (!Directory.Exists(sourceRoot))
            {
                result.Status = WidgetDiscoveryStatus.SourceScopeMissing;
                result.Detail = "源码目录不存在: " + sourceRoot;
                return result;
            }

            foreach (string path in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifactPath(path)) continue;
                try
                {
                    result.Sources.Add(new WidgetSourceFile
                    {
                        Name = Path.GetFileName(path),
                        Path = path,
                        Text = File.ReadAllText(path)
                    });
                }
                catch (Exception ex)
                {
                    result.ReadErrors.Add("读取失败 " + path + ": " + ex.Message);
                }
            }

            result.Sources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            result.Graph = WidgetClassGraph.Build(result.Sources);

            if (result.ReadErrors.Count > 0)
            {
                result.Status = WidgetDiscoveryStatus.SourceReadFailed;
                result.Detail = result.ReadErrors.Count + " 个源文件读取失败，扫描集合不完整";
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

        private static bool IsBuildArtifactPath(string path)
        {
            string norm = path.Replace('\\', '/');
            return norm.Contains("/obj/") || norm.Contains("/bin/");
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

        private static WidgetSourceAuditReport Scan(WidgetClassGraph graph, IList<WidgetSourceFile> files)
        {
            var report = new WidgetSourceAuditReport { WidgetsScanned = graph.ContractClasses.Count };

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

        // ══════════════════════════════════════════════════════════════════════════════════════════
        // 文件级规则：SPEC-006 颜色字面量 / SPEC-007 场景查询
        // ══════════════════════════════════════════════════════════════════════════════════════════

        private static void ScanWidgetFile(WidgetSourceFile file, WidgetClassGraph graph, WidgetSourceAuditReport report)
        {
            // ── SPEC-006 零颜色字面量（棘轮：允许存量只降不升）──
            int colorOccurrences = WidgetColorLiteralAudit.CountOccurrences(file.Text);
            int allowed = WidgetColorLiteralAudit.GetAllowedOccurrences(file.Name);
            if (colorOccurrences > allowed)
            {
                var samples = string.Join(" | ", WidgetColorLiteralAudit.CollectOffendingLines(file.Text, 3).ToArray());
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

            int lastReportedLine = -1;
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string expr = inv.Expression.ToString();
                if (!WidgetSpecRules.IsSceneQueryApi(expr, true)) continue;

                int line = RoslynAstHelper.GetLine(inv);
                if (line == lastReportedLine) continue;
                lastReportedLine = line;
                Add(report, file.Name, WidgetSpecRules.NoSceneQueries, "ERROR", line,
                    "组件内出现场景查询 API (" + expr + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
            }

            foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                string expr = ma.ToString();
                if (!WidgetSpecRules.IsSceneQueryApi(expr, false)) continue;

                int line = RoslynAstHelper.GetLine(ma);
                if (line == lastReportedLine) continue;
                lastReportedLine = line;
                Add(report, file.Name, WidgetSpecRules.NoSceneQueries, "ERROR", line,
                    "组件内出现场景查询 API (" + expr + ")，必须改由 ProbeManager 全局排队节流调度器统一纳管");
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