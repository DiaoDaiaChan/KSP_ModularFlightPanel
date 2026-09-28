using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI.Auditing
{
    /// <summary>
    /// 组件架构演进状态分类
    /// </summary>
    public enum WidgetModernizationStatus
    {
        /// <summary>
        /// 现代声明式微控件体系 (已标准化: 显式 BaseSize + 语义泊靠微控件 DSL / 自动卡片底板)
        /// </summary>
        ModernDSL,

        /// <summary>
        /// 核心 3D 姿态仿真与渲染引擎 (姿态球等特殊 3D 渲染组件)
        /// </summary>
        Core3D,

        /// <summary>
        /// 旧版过程式命令组装实现 (待标准化改造: 依赖 UIFactory 过程式布局 / 缺少 BaseSize / 运行时 CPU 软光栅)
        /// </summary>
        LegacyImperative
    }

    /// <summary>
    /// 单个组件现代化审计明细项
    /// </summary>
    public class WidgetModernizationItem
    {
        public string WidgetName { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public WidgetModernizationStatus Status { get; set; }
        public bool HasBaseSize { get; set; }
        public bool HasAutoCardFrame { get; set; }
        public bool UsesMicroControlsDsl { get; set; }
        public bool UsesImperativeUiFactory { get; set; }
        public List<string> MissingModernFeatures { get; } = new List<string>();

        // ── 航电代码质量与反模式治理指标 (Code Reuse & Anti-Pattern Metrics) ──
        public bool HasRedundantTemplateParser { get; set; }
        public List<string> RedundantFormattingMethods { get; } = new List<string>();
        public int RedundantDirtyTrackingFields { get; set; }
        public bool UsesStandardizedChannels { get; set; }
        public bool UsesStandardizedFormatting { get; set; }
        public bool UsesFastFormat { get; set; }
        public bool UsesSmartUIExtensions { get; set; }
        public bool UsesSetTextIfChanged { get; set; }
        public bool HasBannedDockSyncCall { get; set; }
        public int HotLoopHeapAllocations { get; set; }
        public int HotLoopUguiSetters { get; set; }
        public bool HasUnmanagedCore3DUgui { get; set; }
        public int RawGameObjectAllocs { get; set; }
        public int ReachableHotMethodCount { get; set; }
        public List<string> ReachableHotMethodNames { get; } = new List<string>();
        public List<string> StandardizationSuggestions { get; } = new List<string>();

        public string GetMissingSummary()
        {
            if (MissingModernFeatures.Count == 0) return "Standardized (已符合现代微控件架构规范)";
            return string.Join("; ", MissingModernFeatures);
        }
    }

    /// <summary>
    /// 全局航电组件现代化合规性审计汇总报告
    /// </summary>
    public class WidgetModernizationReport
    {
        public int TotalCount => Items.Count;
        public int ModernCount => Items.Count(i => i.Status == WidgetModernizationStatus.ModernDSL);
        public int Core3DCount => Items.Count(i => i.Status == WidgetModernizationStatus.Core3D);
        public int LegacyCount => Items.Count(i => i.Status == WidgetModernizationStatus.LegacyImperative);

        public float ModernizationPercentage => TotalCount > 0
            ? ((float)(ModernCount + Core3DCount) / TotalCount) * 100f
            : 0f;

        // ── 代码治理宏观指标 ──
        public int RedundantTemplateParserCount => Items.Count(i => i.HasRedundantTemplateParser);
        public int RedundantFormattingMethodCount => Items.Count(i => i.RedundantFormattingMethods.Count > 0);
        public int StandardizedChannelAdoptionCount => Items.Count(i => i.UsesStandardizedChannels);
        public int StandardizedFormattingAdoptionCount => Items.Count(i => i.UsesStandardizedFormatting);
        public int FastFormatAdoptionCount => Items.Count(i => i.UsesFastFormat);
        public int SmartUIExtensionAdoptionCount => Items.Count(i => i.UsesSmartUIExtensions);
        public int BannedDockSyncCallCount => Items.Count(i => i.HasBannedDockSyncCall);
        public int HotLoopHeapAllocationCount => Items.Count(i => i.HotLoopHeapAllocations > 0);
        public int UnmanagedCore3DUguiCount => Items.Count(i => i.HasUnmanagedCore3DUgui);
        public int HotLoopUguiSetterAbuseCount => Items.Count(i => i.HotLoopUguiSetters > 5);
        public int TotalReachableHotMethodsScanned => Items.Sum(i => i.ReachableHotMethodCount);
        public int ZeroRawGameObjectCount => Items.Count(i => i.RawGameObjectAllocs == 0);
        public int TotalRawGameObjectCount => Items.Sum(i => i.RawGameObjectAllocs);

        public List<WidgetModernizationItem> Items { get; } = new List<WidgetModernizationItem>();

        public IEnumerable<WidgetModernizationItem> LegacyWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.LegacyImperative);

        public IEnumerable<WidgetModernizationItem> ModernWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.ModernDSL);

        public IEnumerable<WidgetModernizationItem> Core3DWidgets =>
            Items.Where(i => i.Status == WidgetModernizationStatus.Core3D);

        public IEnumerable<WidgetModernizationItem> WidgetsWithAntiPatterns =>
            Items.Where(i => i.HasRedundantTemplateParser || 
                             i.RedundantFormattingMethods.Count > 0 || 
                             i.RedundantDirtyTrackingFields > 3 ||
                             i.HotLoopHeapAllocations > 0 ||
                             i.HasUnmanagedCore3DUgui ||
                             i.HotLoopUguiSetters > 5 ||
                             i.HasBannedDockSyncCall ||
                             i.RawGameObjectAllocs > 0);
    }

    /// <summary>
    /// 航电组件架构合规性与现代化改造审计内核 (Avionics Widget Modernization Auditor - Roslyn AST 驱动)
    /// </summary>
    public static class WidgetModernizationAudit
    {
        /// <summary>
        /// 微控件 DSL 类型集合。
        /// 唯一数据源在 WidgetSpecRules.MicroControlDslTypes —— 此处不再重写名单，
        /// 否则新增 DSL 控件时会出现"框架能创建、审计看不见"的静默失配。
        /// </summary>
        private static readonly HashSet<string> MicroControlTypes =
            new HashSet<string>(WidgetSpecRules.MicroControlDslTypes, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 执行全量组件现代化合规性扫描（复用 WidgetSourceAudit 的组件继承图，与文件所在目录无关）
        /// </summary>
        public static WidgetModernizationReport Scan(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
            {
                repoRoot = WidgetSourceAudit.ResolveRepositoryRoot();
            }
            return Scan(WidgetSourceAudit.Discover(repoRoot));
        }

        /// <summary>
        /// 基于发现层结果扫描：作用域 = 继承图里的具体组件类
        /// （旧实现只按 UI/Widgets 目录 + 直接基类名 + 每文件首个匹配类来取样，会静默漏掉别名类与跨目录组件）
        /// </summary>
        public static WidgetModernizationReport Scan(WidgetDiscoveryResult discovery)
        {
            var report = new WidgetModernizationReport();
            if (discovery == null || !discovery.CanAuditSource || discovery.Graph == null) return report;

            var graph = discovery.Graph;
            foreach (var node in graph.ContractClasses)
            {
                if (node.IsAbstract) continue;   // 抽象基类不直接出货，不计入现代化进度

                ClassDeclarationSyntax widgetClass = node.Decl;
                string widgetName = node.Name;
                string fileName = node.FileName;
                string filePath = node.FilePath;

                var item = new WidgetModernizationItem
                {
                    WidgetName = widgetName,
                    FileName = fileName,
                    FilePath = filePath
                };

                // Core3D：由继承链结构判定（旧实现按"类名里是否含 NavballSphereWidget"猜测）
                bool isCore3D = node.AnyInChain(n => string.Equals(n.Name, WidgetSpecRules.Core3DBaseType, StringComparison.Ordinal));

                // ── 语法树 AST 深度特征检测 ──
                // 1. BaseSize 属性重写检测
                var baseSizeProp = widgetClass.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == WidgetSpecRules.BaseSizeProperty && RoslynAstHelper.HasModifier(p, SyntaxKind.OverrideKeyword));
                item.HasBaseSize = baseSizeProp != null;

                // 2. AutoCreateCardFrame 属性重写检测
                var cardFrameProp = widgetClass.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == WidgetSpecRules.AutoCardFrameProperty);
                item.HasAutoCardFrame = cardFrameProp != null;

                // 3. 微控件 DSL 字段与声明检测
                int microControlFields = 0;
                foreach (var field in widgetClass.Members.OfType<FieldDeclarationSyntax>())
                {
                    string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);
                    if (MicroControlTypes.Contains(typeName)) microControlFields++;
                }
                foreach (var prop in widgetClass.Members.OfType<PropertyDeclarationSyntax>())
                {
                    string typeName = RoslynAstHelper.GetSimpleTypeName(prop.Type);
                    if (MicroControlTypes.Contains(typeName)) microControlFields++;
                }

                // 也检测方法体中是否调用了 Controls.Add / TextWidget.* / LinearBarWidget.*
                bool hasControlsInvocation = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv => IsMicroControlsDslInvocation(inv.Expression.ToString()));

                item.UsesMicroControlsDsl = microControlFields > 0 || hasControlsInvocation;

                // 4. 命令式 UIFactory 调用检测
                int uiFactoryCalls = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Count(inv => inv.Expression.ToString().StartsWith(WidgetSpecRules.UiFactoryCallPrefix, StringComparison.Ordinal));
                item.UsesImperativeUiFactory = uiFactoryCalls > 0;

                // 5. 运行时 CPU 像素级软光栅化反模式侦测 (SetPixels32 / _texPixels 动态贴图)
                bool hasCpuRasterizer = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv => RoslynAstHelper.GetInvokedMethodName(inv) == "SetPixels32" ||
                                inv.Expression.ToString().EndsWith(WidgetSpecRules.CpuRasterizerApiSuffix, StringComparison.Ordinal)) ||
                    widgetClass.Members.OfType<FieldDeclarationSyntax>()
                    .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == WidgetSpecRules.CpuRasterizerPixelField));

                // 6. 刷新率阶梯滥用侦测：生效取值 + 声明式满帧依据与 SPEC-002 复用同一份判定
                //    （旧实现自带一份类名名单，与 SPEC-002 的文件名名单各说各话，两处都可能漂移）
                bool isCritical = graph.EffectiveTierMembers(node).Contains(WidgetSpecRules.FullFrameTierMember);
                bool abusesCriticalTier = isCritical && !WidgetClassGraph.IsHighFrequencyDeclared(node);

                // ── 综合现代化架构分类判定 ──
                if (isCore3D)
                {
                    item.Status = WidgetModernizationStatus.Core3D;
                    // 深度审查 Core3D 内部是否混入了命令式裸 UGUI 或未受管图元
                    // 注意：此处按"类型文本包含 Image"匹配（覆盖 RawImage / UnityEngine.UI.Image 等限定写法），
                    // 与旧口径保持一致；本项只做常量回收，不改变判定行为。
                    int rawImageAllocs = widgetClass.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                        .Count(obj => obj.Type.ToString().Contains(WidgetSpecRules.UguiImageType));
                    bool usesUiMaterial = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(inv => inv.ToString().Contains(WidgetSpecRules.UiMaterialApi));
                    if (!usesUiMaterial && (item.UsesImperativeUiFactory || rawImageAllocs > 0))
                    {
                        item.HasUnmanagedCore3DUgui = true;
                        item.StandardizationSuggestions.Add($"Core3D 内部混杂未纳管裸 UGUI (UIFactory 调用: {uiFactoryCalls} 处, Image 图元: {rawImageAllocs} 处; 未接入 2D UI Shader 材质管线)");
                    }
                }
                else if (hasCpuRasterizer)
                {
                    // 命中 CPU 纯软光栅反模式，坚决不能判为已标准化
                    item.Status = WidgetModernizationStatus.LegacyImperative;
                    item.MissingModernFeatures.Add("Contains unstandardized CPU software rasterizer (SetPixels32/_texPixels; must migrate to GPU procedural mesh or Core3D)");
                    if (abusesCriticalTier)
                    {
                        item.MissingModernFeatures.Add("RefreshTier.Critical is invalid for non-attitude widget (violates SPEC-002; must use Standard/Relaxed)");
                    }
                    if (item.UsesImperativeUiFactory)
                    {
                        item.MissingModernFeatures.Add("Incomplete DSL modernization (imperative UIFactory readouts remain unstandardized)");
                    }
                }
                else if (item.HasBaseSize && (item.UsesMicroControlsDsl || item.HasAutoCardFrame))
                {
                    item.Status = WidgetModernizationStatus.ModernDSL;
                }
                else
                {
                    item.Status = WidgetModernizationStatus.LegacyImperative;

                    if (!item.HasBaseSize)
                    {
                        item.MissingModernFeatures.Add("Missing BaseSize override");
                    }
                    if (item.UsesImperativeUiFactory)
                    {
                        item.MissingModernFeatures.Add("Uses imperative UIFactory layout");
                    }
                    if (!item.UsesMicroControlsDsl)
                    {
                        item.MissingModernFeatures.Add("Missing micro-controls DSL");
                    }
                    if (!item.HasAutoCardFrame)
                    {
                        item.MissingModernFeatures.Add("Missing AutoCreateCardFrame");
                    }
                }
                // 7. 私有模板解析器反模式检测 (ParseCustomTemplate / CustomTemplate.Split)
                bool hasRedundantParser = widgetClass.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => string.Equals(m.Identifier.Text, WidgetSpecRules.BannedTemplateParserMethod, StringComparison.Ordinal));
                bool hasSplitCustomTemplate = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        string expr = inv.Expression.ToString();
                        return expr.EndsWith(WidgetSpecRules.CustomTemplateSplitSuffix, StringComparison.Ordinal) &&
                               inv.ToString().Contains(WidgetSpecRules.CustomTemplateNameFragment);
                    });
                item.HasRedundantTemplateParser = hasRedundantParser || hasSplitCustomTemplate;
                if (item.HasRedundantTemplateParser)
                {
                    item.StandardizationSuggestions.Add("Contains private ParseCustomTemplate (migrate to BaseFlightWidget.GetTemplateChannel)");
                }

                // 8. 私有时序/度量格式化方法反模式检测
                foreach (var method in widgetClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    string mName = method.Identifier.Text;
                    for (int f = 0; f < WidgetSpecRules.BannedFormattingMethods.Count; f++)
                    {
                        if (string.Equals(mName, WidgetSpecRules.BannedFormattingMethods[f], StringComparison.Ordinal))
                        {
                            item.RedundantFormattingMethods.Add(mName);
                            item.StandardizationSuggestions.Add($"Contains private {mName} (migrate to AvionicsFormatting / BaseFlightWidget)");
                            break;
                        }
                    }
                }

                // 9. 冗余手工脏标记字段统计 (_last*Text, _last*Val, _last*Str)
                int dirtyFields = 0;
                foreach (var field in widgetClass.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        if (IsManualDirtyTrackingField(v.Identifier.Text)) dirtyFields++;
                    }
                }
                item.RedundantDirtyTrackingFields = dirtyFields;
                if (dirtyFields > 3)
                {
                    item.StandardizationSuggestions.Add($"Declares {dirtyFields} manual dirty-tracking fields (recommend SmartUIExtensions or micro-controls)");
                }

                // 10. 标准化 API 与基础设施扩展深度检测
                var allInvocations = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
                item.UsesStandardizedChannels = allInvocations.Any(inv =>
                {
                    string mName = RoslynAstHelper.GetInvokedMethodName(inv);
                    for (int c = 0; c < WidgetSpecRules.StandardTemplateChannelApis.Count; c++)
                    {
                        if (string.Equals(mName, WidgetSpecRules.StandardTemplateChannelApis[c], StringComparison.Ordinal)) return true;
                    }
                    return false;
                });

                item.UsesStandardizedFormatting = allInvocations.Any(inv =>
                {
                    string mName = RoslynAstHelper.GetInvokedMethodName(inv);
                    var receiver = RoslynAstHelper.GetInvocationReceiver(inv);
                    string receiverName = receiver != null ? RoslynAstHelper.GetRightmostIdentifier(receiver) : string.Empty;

                    for (int s = 0; s < WidgetSpecRules.StandardFormattingClasses.Count; s++)
                    {
                        if (string.Equals(receiverName, WidgetSpecRules.StandardFormattingClasses[s], StringComparison.Ordinal)) return true;
                    }
                    for (int f = 0; f < WidgetSpecRules.StandardFormattingApis.Count; f++)
                    {
                        if (string.Equals(mName, WidgetSpecRules.StandardFormattingApis[f], StringComparison.Ordinal)) return true;
                    }
                    return false;
                });

                item.UsesFastFormat = allInvocations.Any(inv =>
                {
                    var receiver = RoslynAstHelper.GetInvocationReceiver(inv);
                    string receiverName = receiver != null ? RoslynAstHelper.GetRightmostIdentifier(receiver) : string.Empty;
                    return string.Equals(receiverName, WidgetSpecRules.FastFormatClass, StringComparison.Ordinal);
                });

                item.UsesSmartUIExtensions = allInvocations.Any(inv =>
                {
                    string mName = RoslynAstHelper.GetInvokedMethodName(inv);
                    for (int s = 0; s < WidgetSpecRules.SmartUIExtensionApis.Count; s++)
                    {
                        if (string.Equals(mName, WidgetSpecRules.SmartUIExtensionApis[s], StringComparison.Ordinal)) return true;
                    }
                    return false;
                });

                item.UsesSetTextIfChanged = allInvocations.Any(inv =>
                    inv.Expression.ToString().EndsWith(WidgetSpecRules.SetTextIfChangedApi, StringComparison.Ordinal));

                // 基础设施与集中调度红线：检测源文件中是否违规调用了集中式调度 API (如 DockAnchorTracker.SyncAll)
                var fileInvocations = widgetClass.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
                item.HasBannedDockSyncCall = fileInvocations.Any(inv =>
                    inv.ToString().Contains(WidgetSpecRules.BannedWidgetDockSyncApi));
                if (item.HasBannedDockSyncCall)
                {
                    item.StandardizationSuggestions.Add($"包含集中式调度 API 调用 ({WidgetSpecRules.BannedWidgetDockSyncApi}; 应交由 FlightHUDManager.LateUpdateSync 统一调度，禁止组件私自调用)");
                }

                // 10. 裸 new GameObject 视觉节点拼装反模式检测
                int rawGoAllocs = widgetClass.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                    .Count(oce =>
                    {
                        string t = oce.Type.ToString();
                        return t == WidgetSpecRules.RawGameObjectType || t == WidgetSpecRules.RawGameObjectFullType;
                    });
                item.RawGameObjectAllocs = rawGoAllocs;
                if (rawGoAllocs > 0)
                {
                    item.StandardizationSuggestions.Add($"存在 {rawGoAllocs} 处裸 new GameObject 视觉拼装 (建议改用 BaseFlightWidget 语义节点工厂或 MicroControls DSL)");
                }

                // 11. 高频生命周期帧循环调用图可达闭包 (Call Graph Reachability Closure)
                // 从 HotLoop 入口方法出发，递归跟踪所有被调用的内部私有方法/局部函数连通闭包，
                // 彻底杜绝违规堆分配与裸 UGUI 逃逸到私有方法中。
                var allClassMethods = widgetClass.Members.OfType<MethodDeclarationSyntax>().ToList();
                var allLocalFunctions = widgetClass.DescendantNodes().OfType<LocalFunctionStatementSyntax>().ToList();
                var allProperties = widgetClass.Members.OfType<PropertyDeclarationSyntax>().ToList();

                var callablesByName = new Dictionary<string, List<SyntaxNode>>(StringComparer.Ordinal);
                void RegisterCallable(string name, SyntaxNode node)
                {
                    if (string.IsNullOrEmpty(name) || node == null) return;
                    if (!callablesByName.TryGetValue(name, out var list))
                    {
                        list = new List<SyntaxNode>();
                        callablesByName[name] = list;
                    }
                    list.Add(node);
                }

                foreach (var m in allClassMethods) RegisterCallable(m.Identifier.Text, m);
                foreach (var lf in allLocalFunctions) RegisterCallable(lf.Identifier.Text, lf);
                foreach (var p in allProperties) RegisterCallable(p.Identifier.Text, p);

                var entryMethods = allClassMethods
                    .Where(m => WidgetSpecRules.HotLoopMethodNames.Contains(m.Identifier.Text) ||
                                m.Identifier.Text.StartsWith(WidgetSpecRules.HotSyncMethodPrefix, StringComparison.OrdinalIgnoreCase))
                    .Cast<SyntaxNode>()
                    .ToList();

                var reachableHotBodies = new HashSet<SyntaxNode>();
                var methodQueue = new Queue<SyntaxNode>();

                foreach (var entry in entryMethods)
                {
                    if (reachableHotBodies.Add(entry))
                    {
                        methodQueue.Enqueue(entry);
                    }
                }

                while (methodQueue.Count > 0)
                {
                    var current = methodQueue.Dequeue();

                    // 入口/方法内部声明的局部函数直接属于热路径闭包
                    foreach (var localFunc in current.ChildNodes().OfType<LocalFunctionStatementSyntax>())
                    {
                        if (reachableHotBodies.Add(localFunc))
                        {
                            methodQueue.Enqueue(localFunc);
                        }
                    }

                    foreach (var inv in current.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        string invokedName = RoslynAstHelper.GetInvokedMethodName(inv);
                        if (!string.IsNullOrEmpty(invokedName) && callablesByName.TryGetValue(invokedName, out var targets))
                        {
                            foreach (var target in targets)
                            {
                                if (reachableHotBodies.Add(target))
                                {
                                    methodQueue.Enqueue(target);
                                }
                            }
                        }
                    }

                    // 检查对类内属性的调用/访问
                    foreach (var ma in current.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                    {
                        string memberName = ma.Name.Identifier.Text;
                        if ((ma.Expression is ThisExpressionSyntax || ma.Expression is IdentifierNameSyntax)
                            && callablesByName.TryGetValue(memberName, out var propTargets))
                        {
                            foreach (var target in propTargets)
                            {
                                if (target is PropertyDeclarationSyntax && reachableHotBodies.Add(target))
                                {
                                    methodQueue.Enqueue(target);
                                }
                            }
                        }
                    }
                }

                item.ReachableHotMethodCount = reachableHotBodies.Count;
                item.ReachableHotMethodNames.AddRange(reachableHotBodies.Select(b =>
                {
                    if (b is MethodDeclarationSyntax m) return m.Identifier.Text;
                    if (b is LocalFunctionStatementSyntax lf) return lf.Identifier.Text;
                    if (b is PropertyDeclarationSyntax p) return p.Identifier.Text;
                    return b.ToString();
                }).Distinct());

                int hotArrayAllocs = 0;
                int hotUguiSetters = 0;
                foreach (var body in reachableHotBodies)
                {
                    // 纳管显式数组与隐式类型数组 new[] { ... }
                    hotArrayAllocs += body.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Count();
                    hotArrayAllocs += body.DescendantNodes().OfType<ImplicitArrayCreationExpressionSyntax>().Count();

                    var assignments = body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
                    foreach (var assign in assignments)
                    {
                        string propName = null;
                        if (assign.Left is MemberAccessExpressionSyntax ma)
                        {
                            propName = ma.Name.Identifier.ValueText;
                        }
                        else
                        {
                            string left = assign.Left.ToString();
                            int dot = left.LastIndexOf('.');
                            if (dot >= 0) propName = left.Substring(dot + 1).Trim();
                        }

                        if (!string.IsNullOrEmpty(propName))
                        {
                            for (int p = 0; p < WidgetSpecRules.HotLoopUguiProperties.Count; p++)
                            {
                                if (string.Equals(propName, WidgetSpecRules.HotLoopUguiProperties[p], StringComparison.Ordinal))
                                {
                                    bool inIf = assign.Ancestors().OfType<IfStatementSyntax>().Any();
                                    if (!inIf)
                                    {
                                        hotUguiSetters++;
                                    }
                                }
                            }
                        }
                    }
                }
                item.HotLoopHeapAllocations = hotArrayAllocs;
                item.HotLoopUguiSetters = hotUguiSetters;

                if (hotArrayAllocs > 0)
                {
                    item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotArrayAllocs} 处运行时堆数组分配 (new T[]; 增加 GC 停顿压力)");
                }
                if (hotUguiSetters > 5)
                {
                    item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotUguiSetters} 处无死区保护的 UGUI 直接赋值 (触发 Canvas 频繁重建; 建议改用 SmartUIExtensions 或建立 Deadband Guard)");
                }

                report.Items.Add(item);
            }

            // 按状态排序：旧版在前方便审计整改，然后按文件名排序
            report.Items.Sort((a, b) =>
            {
                int sa = a.Status == WidgetModernizationStatus.LegacyImperative ? 0 : (a.Status == WidgetModernizationStatus.ModernDSL ? 1 : 2);
                int sb = b.Status == WidgetModernizationStatus.LegacyImperative ? 0 : (b.Status == WidgetModernizationStatus.ModernDSL ? 1 : 2);
                if (sa != sb) return sa.CompareTo(sb);
                return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            return report;
        }

        /// <summary>微控件 DSL 工厂调用形态：Controls.Add 或 已登记的 DSL 类型名 + "." 限定调用</summary>
        private static bool IsMicroControlsDslInvocation(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;
            if (expression.StartsWith(WidgetSpecRules.ControlsAddPrefix, StringComparison.Ordinal)) return true;

            for (int i = 0; i < WidgetSpecRules.DslFactoryInvocationTypes.Count; i++)
            {
                if (expression.StartsWith(WidgetSpecRules.DslFactoryInvocationTypes[i] + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>手工脏标记字段命名判定：_last* 前缀 + 已登记后缀之一</summary>
        private static bool IsManualDirtyTrackingField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return false;
            if (!fieldName.StartsWith(WidgetSpecRules.DirtyTrackingFieldPrefix, StringComparison.OrdinalIgnoreCase)) return false;

            for (int i = 0; i < WidgetSpecRules.DirtyTrackingFieldSuffixes.Count; i++)
            {
                if (fieldName.EndsWith(WidgetSpecRules.DirtyTrackingFieldSuffixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 生成供 MSBuild 编译期输出的标准编译器警告 / 提醒文本
        /// 格式: FilePath(1,1): warning MFP_LEGACY_WIDGET: ...
        /// </summary>
        public static string GenerateMSBuildOutput(WidgetModernizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ModularFlightPanel.csproj : warning MFP_MODERNIZATION: [Avionics Modernization Audit] Standardized: {report.ModernCount}/{report.TotalCount} ({report.ModernizationPercentage:F1}%), Core3D: {report.Core3DCount}, Legacy to Modernize: {report.LegacyCount}");

            foreach (var item in report.LegacyWidgets)
            {
                sb.AppendLine($"{item.FilePath}(1,1): warning MFP_LEGACY_WIDGET: [Legacy Widget / Needs Modernization] {item.WidgetName}: {item.GetMissingSummary()}");
            }

            if (report.BannedDockSyncCallCount > 0)
            {
                foreach (var item in report.Items.Where(i => i.HasBannedDockSyncCall))
                {
                    sb.AppendLine($"{item.FilePath}(1,1): warning MFP_BANNED_DOCK_SYNC: [Anti-Pattern] {item.WidgetName}: Calls {WidgetSpecRules.BannedWidgetDockSyncApi}; must delegate to FlightHUDManager.LateUpdateSync");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成终端人类可读的彩色/格式化审计汇总
        /// </summary>
        public static string GenerateTerminalSummary(WidgetModernizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================================");
            sb.AppendLine("         MODULAR FLIGHT PANEL 组件架构合规性与现代化改造审计 (Roslyn AST)");
            sb.AppendLine("=======================================================================");
            sb.AppendLine($"已审计组件总数: {report.TotalCount} 个");
            sb.AppendLine($"现代微控件架构 (ModernDSL):   {report.ModernCount} 个 ({((float)report.ModernCount / Math.Max(1, report.TotalCount) * 100f):F1}%)");
            sb.AppendLine($"核心 3D 渲染引擎 (Core3D):    {report.Core3DCount} 个");
            sb.AppendLine($"待改造旧版组件 (Legacy):     {report.LegacyCount} 个 ({((float)report.LegacyCount / Math.Max(1, report.TotalCount) * 100f):F1}%)");
            sb.AppendLine($"整体现代化合规率:            {report.ModernizationPercentage:F1}%");
            sb.AppendLine("-----------------------------------------------------------------------");

            var modernList = report.ModernWidgets.Select(w => w.WidgetName).ToList();
            if (modernList.Count > 0)
            {
                sb.AppendLine($"✔ 已完成微控件 DSL 标准化改造 ({modernList.Count} 个):");
                sb.AppendLine($"  └─ {string.Join(", ", modernList)}");
            }

            var legacyItems = report.LegacyWidgets.ToList();
            if (legacyItems.Count > 0)
            {
                sb.AppendLine($"⚠ 待改造旧版命令式组件 ({legacyItems.Count} 个):");
                for (int i = 0; i < legacyItems.Count; i++)
                {
                    var item = legacyItems[i];
                    sb.AppendLine($"  ├─ [{i + 1:D2}] {item.FileName,-28} => {item.GetMissingSummary()}");
                }
            }

            sb.AppendLine("-----------------------------------------------------------------------");
            sb.AppendLine("    航电代码精简与标准化治理质量雷达 (Cleanliness & Anti-Pattern Radar)");
            sb.AppendLine("-----------------------------------------------------------------------");
            sb.AppendLine($"标准通道提取率:               {report.StandardizedChannelAdoptionCount}/{report.TotalCount} 个组件已接入 GetTemplateChannel 系列");
            sb.AppendLine($"零私有模板解析达成率:         {(report.TotalCount - report.RedundantTemplateParserCount)}/{report.TotalCount} 个组件已消除私有 ParseCustomTemplate");
            sb.AppendLine($"统一格式化套件复用率:         {(report.TotalCount - report.RedundantFormattingMethodCount)}/{report.TotalCount} 个组件已接入 AvionicsFormatting / AvionicsFastFormat");
            sb.AppendLine($"高速格式化缓存 (FastFormat):  {report.FastFormatAdoptionCount}/{report.TotalCount} 个组件已采纳 AvionicsFastFormat 零 GC 查表");
            sb.AppendLine($"智能脏检扩展 (SmartUI):       {report.SmartUIExtensionAdoptionCount}/{report.TotalCount} 个组件已接入 SmartUIExtensions 安全赋值");
            sb.AppendLine($"手工脏标记字段全面纳管率:     {report.Items.Count(i => i.RedundantDirtyTrackingFields <= 3)}/{report.TotalCount} 个组件已消除字段级脏标记膨胀");
            sb.AppendLine($"零高频循环堆分配达成率:       {(report.TotalCount - report.HotLoopHeapAllocationCount)}/{report.TotalCount} 个组件已消除帧循环 new T[]");
            sb.AppendLine($"集中调度合规率 (零私自Sync):  {(report.TotalCount - report.BannedDockSyncCallCount)}/{report.TotalCount} 个组件符合集中停靠调度");
            sb.AppendLine($"高频调用图连通闭包覆盖:       {report.TotalReachableHotMethodsScanned} 个方法已纳入高频生命周期穿透审计");
            sb.AppendLine($"3D 引擎 UGUI 纯净度:          {(report.Core3DCount - report.UnmanagedCore3DUguiCount)}/{Math.Max(1, report.Core3DCount)} 个 3D 组件无裸 UGUI 逃逸");
            sb.AppendLine($"零裸节点拼装达成率:           {report.ZeroRawGameObjectCount}/{report.TotalCount} 个组件已消除私自 new GameObject (全局残余 {report.TotalRawGameObjectCount} 处)");

            var antiPatternItems = report.WidgetsWithAntiPatterns.ToList();
            if (antiPatternItems.Count > 0)
            {
                sb.AppendLine("-----------------------------------------------------------------------");
                sb.AppendLine($"▲ 待治理高频重复代码组件清单 ({antiPatternItems.Count} 个):");
                for (int i = 0; i < antiPatternItems.Count; i++)
                {
                    var item = antiPatternItems[i];
                    sb.AppendLine($"  ├─ [{i + 1:D2}] {item.FileName,-28} => {string.Join("; ", item.StandardizationSuggestions)}");
                }
            }

            sb.AppendLine("=======================================================================");
            return sb.ToString();
        }
    }
}
