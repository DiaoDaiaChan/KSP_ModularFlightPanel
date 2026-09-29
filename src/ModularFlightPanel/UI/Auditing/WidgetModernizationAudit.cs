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
        public int HotLoopMeshRebuilds { get; set; }
        public bool HasCustomVertexHelperMesh { get; set; }
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
        public int HotLoopMeshRebuildCount => Items.Count(i => i.HotLoopMeshRebuilds > 0);
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
                             i.HotLoopMeshRebuilds > 0 ||
                             i.HasUnmanagedCore3DUgui ||
                             i.HotLoopUguiSetters > 5 ||
                             i.HasBannedDockSyncCall ||
                             i.RawGameObjectAllocs > 0);
    }

    /// <summary>
    /// 航电组件架构合规性与现代化穿透审计内核 (Avionics Widget Modernization Auditor)
    /// ====================================================================================
    /// 全面采用 Roslyn 符号图、SemanticCompilationProvider 与语义模型穿透直达底层。
    /// 彻底废弃浅层 AST 字符串启发式匹配、流水账循环与 ifelse 梯子，直达 UnityEngine 底层基类与 C# CLR 类型体系。
    /// </summary>
    public static class WidgetModernizationAudit
    {
        private static readonly HashSet<string> MicroControlNames =
            new HashSet<string>(WidgetSpecRules.MicroControlDslTypes, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 执行全量组件现代化合规性扫描
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
        /// 基于发现层结果执行穿透性语义扫描
        /// </summary>
        public static WidgetModernizationReport Scan(WidgetDiscoveryResult discovery)
        {
            var report = new WidgetModernizationReport();
            if (discovery == null || !discovery.CanAuditSource || discovery.Graph == null) return report;

            var graph = discovery.Graph;
            SemanticCompilationContext semanticContext = discovery.SemanticContext ?? graph.SemanticContext;

            // 自动化自愈：若外部未预置语义上下文但有源文件，瞬时构建全量编译环境
            if (semanticContext == null && discovery.Sources != null && discovery.Sources.Count > 0)
            {
                semanticContext = SemanticCompilationProvider.BuildCompilation(discovery.Sources);
            }

            foreach (var node in graph.ContractClasses)
            {
                if (node.IsAbstract) continue;

                var item = AnalyzeWidgetNode(node, graph, semanticContext);
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

        private static WidgetModernizationItem AnalyzeWidgetNode(
            WidgetClassNode node,
            WidgetClassGraph graph,
            SemanticCompilationContext semanticContext)
        {
            ClassDeclarationSyntax widgetClass = node.Decl;
            SemanticModel model = node.SemanticModel;
            if (model == null && semanticContext != null)
            {
                var candidate = semanticContext.GetSemanticModel(node.FilePath ?? node.FileName);
                if (candidate != null && candidate.SyntaxTree == widgetClass.SyntaxTree)
                {
                    model = candidate;
                }
            }

            var item = new WidgetModernizationItem
            {
                WidgetName = node.Name,
                FileName = node.FileName,
                FilePath = node.FilePath
            };

            INamedTypeSymbol classSymbol = node.Symbol;
            if (classSymbol == null && model != null && model.SyntaxTree == widgetClass.SyntaxTree)
            {
                classSymbol = model.GetDeclaredSymbol(widgetClass) as INamedTypeSymbol;
            }

            // 1. 继承体系穿透判定：Core3D 姿态球特殊引擎识别 (BaseNavballSphereWidget)
            bool isCore3D = classSymbol != null
                ? IsSubclassOf(classSymbol, "ModularFlightPanel.UI.Widgets." + WidgetSpecRules.Core3DBaseType, WidgetSpecRules.Core3DBaseType)
                : node.AnyInChain(n => string.Equals(n.Name, WidgetSpecRules.Core3DBaseType, StringComparison.Ordinal));

            // 2. 单遍穿透语义访问器：在单次 AST 遍历中统合收集成员特性、标准化 API 采纳、调用关系边与每个方法的局部违规
            var walker = new PenetratingModernizationWalker(model, classSymbol, widgetClass);
            walker.Visit(widgetClass);

            // 3. 将单遍收集的结果赋值到明细项
            item.HasBaseSize = walker.HasBaseSize;
            item.HasAutoCardFrame = walker.HasAutoCardFrame;
            item.UsesMicroControlsDsl = walker.MicroControlDeclarationCount > 0 || walker.MicroControlInvocationCount > 0;
            item.UsesImperativeUiFactory = walker.ImperativeUiFactoryCalls > 0;
            item.UsesStandardizedChannels = walker.StandardizedChannelCalls > 0;
            item.UsesStandardizedFormatting = walker.StandardizedFormattingCalls > 0;
            item.UsesFastFormat = walker.FastFormatCalls > 0;
            item.UsesSmartUIExtensions = walker.SmartUIExtensionCalls > 0;
            item.UsesSetTextIfChanged = walker.SetTextIfChangedCalls > 0;
            item.HasBannedDockSyncCall = walker.BannedDockSyncCalls > 0;
            item.RawGameObjectAllocs = walker.RawGameObjectAllocs;
            item.HasCustomVertexHelperMesh = walker.HasVertexHelperMesh;
            item.HasRedundantTemplateParser = walker.HasRedundantTemplateParser;
            item.RedundantDirtyTrackingFields = walker.RedundantDirtyTrackingFields;
            item.RedundantFormattingMethods.AddRange(walker.RedundantFormattingMethodNames);

            foreach (var mName in walker.RedundantFormattingMethodNames)
            {
                item.StandardizationSuggestions.Add($"Contains private {mName} (migrate to AvionicsFormatting / BaseFlightWidget)");
            }

            if (item.HasBannedDockSyncCall)
            {
                item.StandardizationSuggestions.Add($"包含集中式调度 API 调用 ({WidgetSpecRules.BannedWidgetDockSyncApi}; 应交由 FlightHUDManager.LateUpdateSync 统一调度，禁止组件私自调用)");
            }

            if (item.RawGameObjectAllocs > 0)
            {
                item.StandardizationSuggestions.Add($"存在 {item.RawGameObjectAllocs} 处裸 new GameObject 视觉拼装 (建议改用 BaseFlightWidget 语义节点工厂或 MicroControls DSL)");
            }

            if (item.HasRedundantTemplateParser)
            {
                item.StandardizationSuggestions.Add("Contains private ParseCustomTemplate (migrate to BaseFlightWidget.GetTemplateChannel)");
            }

            if (item.RedundantDirtyTrackingFields > 3)
            {
                item.StandardizationSuggestions.Add($"Declares {item.RedundantDirtyTrackingFields} manual dirty-tracking fields (recommend SmartUIExtensions or micro-controls)");
            }

            // 4. 刷新阶梯滥用检测
            bool isCritical = graph.EffectiveTierMembers(node).Contains(WidgetSpecRules.FullFrameTierMember);
            bool abusesCriticalTier = isCritical && !WidgetClassGraph.IsHighFrequencyDeclared(node);

            // 5. 现代化架构状态判定 (模式匹配消除冗余 ifelse)
            bool hasCpuRasterizer = walker.HasCpuRasterizer;
            if (isCore3D)
            {
                item.Status = WidgetModernizationStatus.Core3D;
                if (!walker.UsesUiMaterial && (item.UsesImperativeUiFactory || walker.RawImageAllocs > 0))
                {
                    item.HasUnmanagedCore3DUgui = true;
                    item.StandardizationSuggestions.Add($"Core3D 内部混杂未纳管裸 UGUI (UIFactory 调用: {walker.ImperativeUiFactoryCalls} 处, Image 图元: {walker.RawImageAllocs} 处; 未接入 2D UI Shader 材质管线)");
                }
            }
            else if (hasCpuRasterizer)
            {
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
                if (!item.HasBaseSize) item.MissingModernFeatures.Add("Missing BaseSize override");
                if (item.UsesImperativeUiFactory) item.MissingModernFeatures.Add("Uses imperative UIFactory layout");
                if (!item.UsesMicroControlsDsl) item.MissingModernFeatures.Add("Missing micro-controls DSL");
                if (!item.HasAutoCardFrame) item.MissingModernFeatures.Add("Missing AutoCreateCardFrame");
            }

            // 6. 符号级高频调用图闭包求值：直接基于 Walker 收集的符号有向边与指标字典进行 O(V+E) BFS 闭包求解
            walker.ResolveHotLoopReachabilityClosure(item);

            return item;
        }

        // =========================================================================
        // 符号方法键值结构 (Symbolic Method Identity)
        // =========================================================================

        private readonly struct MethodSymbolKey : IEquatable<MethodSymbolKey>
        {
            public readonly IMethodSymbol Symbol;
            public readonly string FallbackName;

            public MethodSymbolKey(IMethodSymbol symbol, string fallbackName = null)
            {
                Symbol = symbol;
                FallbackName = fallbackName ?? symbol?.Name ?? string.Empty;
            }

            public string DisplayName => Symbol != null ? Symbol.Name : FallbackName;

            public bool Equals(MethodSymbolKey other)
            {
                if (Symbol != null && other.Symbol != null)
                {
                    return SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);
                }
                return string.Equals(FallbackName, other.FallbackName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is MethodSymbolKey other && Equals(other);

            public override int GetHashCode()
            {
                if (Symbol != null)
                {
                    return SymbolEqualityComparer.Default.GetHashCode(Symbol);
                }
                return StringComparer.Ordinal.GetHashCode(FallbackName);
            }

            public override string ToString() => DisplayName;
        }

        // =========================================================================
        // 单遍穿透语义访问器 (Penetrating Single-Pass Modernization Walker)
        // =========================================================================

        private sealed class PenetratingModernizationWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _model;
            private readonly INamedTypeSymbol _classSymbol;
            private readonly ClassDeclarationSyntax _widgetClass;

            // 调用图与闭包度量容器 (完全基于 MethodSymbolKey 符号索引)
            private MethodSymbolKey? _currentMethod;
            private readonly Dictionary<MethodSymbolKey, HashSet<MethodSymbolKey>> _callGraph = new();
            private readonly Dictionary<MethodSymbolKey, int> _methodArrayAllocs = new();
            private readonly Dictionary<MethodSymbolKey, int> _methodMeshRebuilds = new();
            private readonly Dictionary<MethodSymbolKey, int> _methodUguiSetters = new();
            private readonly HashSet<MethodSymbolKey> _entryMethods = new();

            // 语法 fallback 映射 (仅当无语义模型时作保底查找)
            private readonly Dictionary<string, List<MethodSymbolKey>> _callablesByName = new(StringComparer.Ordinal);

            // 类级特征指标
            public bool HasBaseSize { get; private set; }
            public bool HasAutoCardFrame { get; private set; }
            public int MicroControlDeclarationCount { get; private set; }
            public int MicroControlInvocationCount { get; private set; }
            public int ImperativeUiFactoryCalls { get; private set; }
            public int StandardizedChannelCalls { get; private set; }
            public int StandardizedFormattingCalls { get; private set; }
            public int FastFormatCalls { get; private set; }
            public int SmartUIExtensionCalls { get; private set; }
            public int SetTextIfChangedCalls { get; private set; }
            public int BannedDockSyncCalls { get; private set; }
            public int RawGameObjectAllocs { get; private set; }
            public int RawImageAllocs { get; private set; }
            public bool UsesUiMaterial { get; private set; }
            public bool HasCpuRasterizer { get; private set; }
            public bool HasRedundantTemplateParser { get; private set; }
            public bool HasVertexHelperMesh { get; private set; }
            public int RedundantDirtyTrackingFields { get; private set; }
            public List<string> RedundantFormattingMethodNames { get; } = new List<string>();

            public PenetratingModernizationWalker(SemanticModel model, INamedTypeSymbol classSymbol, ClassDeclarationSyntax widgetClass)
            {
                _model = model;
                _classSymbol = classSymbol;
                _widgetClass = widgetClass;
            }

            public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                var typeSymbol = _model?.GetTypeInfo(node.Declaration.Type).Type;
                string typeName = RoslynAstHelper.GetSimpleTypeName(node.Declaration.Type);

                if (IsMicroControlType(typeSymbol, typeName))
                {
                    MicroControlDeclarationCount++;
                }

                if (IsCpuRasterizerPixelField(node))
                {
                    HasCpuRasterizer = true;
                }

                foreach (var v in node.Declaration.Variables)
                {
                    if (IsManualDirtyTrackingField(v.Identifier.Text))
                    {
                        RedundantDirtyTrackingFields++;
                    }
                }

                base.VisitFieldDeclaration(node);
            }

            public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                // 1. 契约属性检测 (BaseSize override / AutoCreateCardFrame)
                if (node.Identifier.Text == WidgetSpecRules.BaseSizeProperty && RoslynAstHelper.HasModifier(node, SyntaxKind.OverrideKeyword))
                {
                    HasBaseSize = true;
                }
                if (node.Identifier.Text == WidgetSpecRules.AutoCardFrameProperty)
                {
                    HasAutoCardFrame = true;
                }

                // 2. 微控件属性声明
                var typeSymbol = _model?.GetTypeInfo(node.Type).Type;
                string typeName = RoslynAstHelper.GetSimpleTypeName(node.Type);
                if (IsMicroControlType(typeSymbol, typeName))
                {
                    MicroControlDeclarationCount++;
                }

                // 3. 记录属性访问器符号供调用图使用
                if (_model != null)
                {
                    if (node.AccessorList != null)
                    {
                        foreach (var acc in node.AccessorList.Accessors)
                        {
                            var accSymbol = _model.GetDeclaredSymbol(acc) as IMethodSymbol;
                            if (accSymbol != null)
                            {
                                var key = new MethodSymbolKey(accSymbol);
                                RegisterCallable(key);
                            }
                        }
                    }
                }
                else
                {
                    RegisterCallable(new MethodSymbolKey(null, node.Identifier.Text));
                }

                base.VisitPropertyDeclaration(node);
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                string methodName = node.Identifier.Text;

                // 1. 废弃模板解析器与冗余格式化方法判定
                if (string.Equals(methodName, WidgetSpecRules.BannedTemplateParserMethod, StringComparison.Ordinal))
                {
                    HasRedundantTemplateParser = true;
                }

                if (!RoslynAstHelper.HasModifier(node, SyntaxKind.OverrideKeyword) &&
                    IsRedundantFormattingMethodName(methodName))
                {
                    RedundantFormattingMethodNames.Add(methodName);
                }

                // 2. 获取符号并注册到可调用集与调用图节点
                IMethodSymbol symbol = _model?.GetDeclaredSymbol(node) as IMethodSymbol;
                var key = new MethodSymbolKey(symbol, methodName);
                RegisterCallable(key);

                // 3. 入口方法识别 (HotLoopMethodNames / HotSync*)
                bool isHotEntry = WidgetSpecRules.HotLoopMethodNames.Contains(methodName) ||
                                  methodName.StartsWith(WidgetSpecRules.HotSyncMethodPrefix, StringComparison.OrdinalIgnoreCase);
                if (isHotEntry)
                {
                    _entryMethods.Add(key);
                }

                // 4. 切换上下文进入方法体，单遍收集其内部违规调用与子调用边
                var prevMethod = _currentMethod;
                _currentMethod = key;

                base.VisitMethodDeclaration(node);

                _currentMethod = prevMethod;
            }

            public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                string lfName = node.Identifier.Text;
                IMethodSymbol symbol = _model?.GetDeclaredSymbol(node) as IMethodSymbol;
                var key = new MethodSymbolKey(symbol, lfName);
                RegisterCallable(key);

                // 局部函数天然被外层方法直接持有/调用
                if (_currentMethod.HasValue)
                {
                    RecordCallEdge(_currentMethod.Value, key);
                }

                var prevMethod = _currentMethod;
                _currentMethod = key;

                base.VisitLocalFunctionStatement(node);

                _currentMethod = prevMethod;
            }

            public override void VisitParameter(ParameterSyntax node)
            {
                if (node.Type != null)
                {
                    var typeSymbol = _model?.GetTypeInfo(node.Type).Type;
                    if (IsVertexHelperType(typeSymbol, node.Type.ToString()))
                    {
                        HasVertexHelperMesh = true;
                    }
                }
                base.VisitParameter(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                var typeSymbol = _model?.GetTypeInfo(node).Type;
                string typeText = node.Type.ToString();

                if (IsGameObjectType(typeSymbol, typeText))
                {
                    RawGameObjectAllocs++;
                }
                else if (IsUguiGraphicType(typeSymbol, typeText))
                {
                    RawImageAllocs++;
                }

                base.VisitObjectCreationExpression(node);
            }

            public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
            {
                if (_currentMethod.HasValue)
                {
                    IncrementMethodMetric(_methodArrayAllocs, _currentMethod.Value);
                }
                base.VisitArrayCreationExpression(node);
            }

            public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
            {
                if (_currentMethod.HasValue)
                {
                    IncrementMethodMetric(_methodArrayAllocs, _currentMethod.Value);
                }
                base.VisitImplicitArrayCreationExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbol = _model?.GetSymbolInfo(node).Symbol as IMethodSymbol;
                string invokedName = RoslynAstHelper.GetInvokedMethodName(node);
                string receiverName = RoslynAstHelper.GetInvocationReceiver(node) is { } recv
                    ? RoslynAstHelper.GetRightmostIdentifier(recv)
                    : string.Empty;

                // 1. 若位于方法体内，追踪符号调用边与方法级网格重建违规
                if (_currentMethod.HasValue)
                {
                    TrackCallEdgeFromInvocation(_currentMethod.Value, symbol, invokedName);

                    if (IsMeshRebuildCall(symbol, invokedName))
                    {
                        IncrementMethodMetric(_methodMeshRebuilds, _currentMethod.Value);
                    }
                }

                // 2. 微控件 DSL 调用检测
                if (IsMicroControlInvocation(symbol, node))
                {
                    MicroControlInvocationCount++;
                }

                // 3. UIFactory 命令式调用
                if (IsUiFactoryCall(symbol, node))
                {
                    ImperativeUiFactoryCalls++;
                }

                // 4. CPU 软光栅化调用
                if (IsCpuRasterizerCall(symbol, invokedName, node))
                {
                    HasCpuRasterizer = true;
                }

                // 5. 标准通道提取算子
                if (IsStandardChannelCall(symbol, invokedName))
                {
                    StandardizedChannelCalls++;
                }

                // 6. 航电标准格式化套件
                if (IsStandardFormattingCall(symbol, receiverName, invokedName))
                {
                    StandardizedFormattingCalls++;
                }

                // 7. FastFormat
                if (IsFastFormatCall(symbol, receiverName))
                {
                    FastFormatCalls++;
                }

                // 8. SmartUIExtensions
                if (IsSmartUIExtensionCall(symbol, invokedName))
                {
                    SmartUIExtensionCalls++;
                }

                // 9. SetTextIfChanged
                if (string.Equals(invokedName, WidgetSpecRules.SetTextIfChangedApi, StringComparison.Ordinal) ||
                    (symbol != null && symbol.Name == WidgetSpecRules.SetTextIfChangedApi))
                {
                    SetTextIfChangedCalls++;
                }

                // 10. 集中停靠违规调用
                if (IsBannedDockSyncCall(symbol, node))
                {
                    BannedDockSyncCalls++;
                }

                // 11. UI 材质管线调用 (精确符号/调用比对，消除 ToString().Contains)
                if (invokedName == WidgetSpecRules.UiMaterialApi ||
                    (symbol != null && symbol.Name == WidgetSpecRules.UiMaterialApi))
                {
                    UsesUiMaterial = true;
                }

                // 12. CustomTemplate.Split 模板解析反模式 (精确检查接收者符号或文本，消除 ToString().Contains)
                if (invokedName == "Split")
                {
                    var invRecv = RoslynAstHelper.GetInvocationReceiver(node);
                    if (invRecv != null)
                    {
                        var recvSymbol = _model?.GetSymbolInfo(invRecv).Symbol;
                        string recvText = RoslynAstHelper.GetRightmostIdentifier(invRecv);
                        if (recvText == WidgetSpecRules.CustomTemplateNameFragment ||
                            (recvSymbol != null && recvSymbol.Name == WidgetSpecRules.CustomTemplateNameFragment))
                        {
                            HasRedundantTemplateParser = true;
                        }
                    }
                }

                base.VisitInvocationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                // 追踪属性读写引发的方法/属性调用边
                if (_currentMethod.HasValue && _model != null)
                {
                    var memberSymbol = _model.GetSymbolInfo(node).Symbol;
                    if (memberSymbol is IPropertySymbol propSymbol)
                    {
                        if (propSymbol.GetMethod != null && IsMemberOfCurrentClass(propSymbol.ContainingType))
                        {
                            RecordCallEdge(_currentMethod.Value, new MethodSymbolKey(propSymbol.GetMethod));
                        }
                    }
                }
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                // 检查位于热生命周期中的无死区 UGUI 裸属性直接写
                if (_currentMethod.HasValue)
                {
                    var leftSymbol = _model?.GetSymbolInfo(node.Left).Symbol;
                    string propName = RoslynAstHelper.GetRightmostIdentifier(node.Left);

                    if (IsUguiGraphicOrTransformProperty(leftSymbol, propName))
                    {
                        if (!IsDeadbandGuarded(node))
                        {
                            IncrementMethodMetric(_methodUguiSetters, _currentMethod.Value);
                        }
                    }
                }

                base.VisitAssignmentExpression(node);
            }

            // ── 内部调用边与闭包求解核心 ──

            private void RegisterCallable(MethodSymbolKey key)
            {
                if (!_callGraph.ContainsKey(key))
                {
                    _callGraph[key] = new HashSet<MethodSymbolKey>();
                }
                if (!string.IsNullOrEmpty(key.DisplayName))
                {
                    if (!_callablesByName.TryGetValue(key.DisplayName, out var list))
                    {
                        list = new List<MethodSymbolKey>();
                        _callablesByName[key.DisplayName] = list;
                    }
                    if (!list.Contains(key))
                    {
                        list.Add(key);
                    }
                }
            }

            private void RecordCallEdge(MethodSymbolKey caller, MethodSymbolKey callee)
            {
                if (!_callGraph.TryGetValue(caller, out var set))
                {
                    set = new HashSet<MethodSymbolKey>();
                    _callGraph[caller] = set;
                }
                set.Add(callee);
            }

            private void TrackCallEdgeFromInvocation(MethodSymbolKey caller, IMethodSymbol invokedSymbol, string invokedName)
            {
                if (invokedSymbol != null && IsMemberOfCurrentClass(invokedSymbol.ContainingType))
                {
                    var targetKey = new MethodSymbolKey(invokedSymbol);
                    RecordCallEdge(caller, targetKey);
                    return;
                }

                // 退化路径：按名字匹配类内部定义的方法
                if (!string.IsNullOrEmpty(invokedName) && _callablesByName.TryGetValue(invokedName, out var candidates))
                {
                    foreach (var cand in candidates)
                    {
                        RecordCallEdge(caller, cand);
                    }
                }
            }

            private bool IsMemberOfCurrentClass(INamedTypeSymbol containingType)
            {
                if (containingType == null) return false;
                if (_classSymbol != null)
                {
                    return SymbolEqualityComparer.Default.Equals(_classSymbol, containingType) ||
                           _classSymbol.GetTypeMembers().Any(t => SymbolEqualityComparer.Default.Equals(t, containingType));
                }
                return containingType.Name == _widgetClass.Identifier.Text;
            }

            private static void IncrementMethodMetric(Dictionary<MethodSymbolKey, int> dict, MethodSymbolKey key)
            {
                dict.TryGetValue(key, out int count);
                dict[key] = count + 1;
            }

            /// <summary>
            /// 单遍遍历完成后，基于符号图广度优先遍历 (BFS) 计算所有高频受染闭包方法并聚合指标，
            /// 彻底消除对 AST 的任何重复 DescendantNodes 扫描！
            /// </summary>
            public void ResolveHotLoopReachabilityClosure(WidgetModernizationItem item)
            {
                var reachableHotSymbols = new HashSet<MethodSymbolKey>();
                var queue = new Queue<MethodSymbolKey>();

                foreach (var entry in _entryMethods)
                {
                    if (reachableHotSymbols.Add(entry))
                    {
                        queue.Enqueue(entry);
                    }
                }

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (_callGraph.TryGetValue(current, out var neighbors))
                    {
                        foreach (var next in neighbors)
                        {
                            if (reachableHotSymbols.Add(next))
                            {
                                queue.Enqueue(next);
                            }
                        }
                    }
                }

                int hotArrayAllocs = 0;
                int hotMeshRebuilds = 0;
                int hotUguiSetters = 0;

                foreach (var m in reachableHotSymbols)
                {
                    if (_methodArrayAllocs.TryGetValue(m, out int a)) hotArrayAllocs += a;
                    if (_methodMeshRebuilds.TryGetValue(m, out int r)) hotMeshRebuilds += r;
                    if (_methodUguiSetters.TryGetValue(m, out int u)) hotUguiSetters += u;
                }

                item.ReachableHotMethodCount = reachableHotSymbols.Count;
                item.ReachableHotMethodNames.AddRange(reachableHotSymbols.Select(k => k.DisplayName).Distinct());
                item.HotLoopHeapAllocations = hotArrayAllocs;
                item.HotLoopMeshRebuilds = hotMeshRebuilds;
                item.HotLoopUguiSetters = hotUguiSetters;

                if (hotArrayAllocs > 0)
                {
                    item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotArrayAllocs} 处运行时堆数组分配 (new T[]; 增加 GC 停顿压力)");
                }
                if (hotMeshRebuilds > 0)
                {
                    if (item.HasCustomVertexHelperMesh)
                    {
                        item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotMeshRebuilds} 处 CPU 重型矢量网格全量重建调用 (SetVerticesDirty; 命中 VertexHelper 纯代码重度网格生成反模式，必须动静分离或做严格防抖节流)");
                    }
                    else
                    {
                        item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotMeshRebuilds} 处 UGUI 网格/布局脏标记调用 (SetVerticesDirty/SetLayoutDirty; 触发 Canvas 频繁重建，建议动静分离或建立防抖)");
                    }
                }
                if (hotUguiSetters > 5)
                {
                    item.StandardizationSuggestions.Add($"高频受染闭包存在 {hotUguiSetters} 处无死区保护的 UGUI 直接赋值 (触发 Canvas 频繁重建; 建议改用 SmartUIExtensions 或建立 Deadband Guard)");
                }
            }
        }

        // =========================================================================
        // 底层类型与符号穿透判定工具集 (Semantic Penetration Helpers)
        // =========================================================================

        private static bool IsSubclassOf(ITypeSymbol type, string fullName, string simpleName)
        {
            if (type == null) return false;
            if (SemanticCompilationProvider.IsOrInheritsFrom(type, fullName)) return true;
            for (ITypeSymbol curr = type; curr != null; curr = curr.BaseType)
            {
                if (curr.Name == simpleName || curr.ToDisplayString() == fullName) return true;
            }
            return false;
        }

        private static bool IsMicroControlType(ITypeSymbol type, string typeName)
        {
            if (type != null)
            {
                // 1. 穿透检查基类继承：派生自 WidgetControl
                if (SemanticCompilationProvider.IsOrInheritsFrom(type, "ModularFlightPanel.UI.Framework.WidgetControl"))
                    return true;

                // 2. 穿透检查接口实现：实现 ITelemetryBindableControl 契约
                foreach (var iface in type.AllInterfaces)
                {
                    if (iface.Name == "ITelemetryBindableControl" ||
                        string.Equals(iface.ToDisplayString(), "ModularFlightPanel.UI.Framework.ITelemetryBindableControl", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                // 3. 常见微控件类名模式匹配
                if (type.Name is "TextWidget" or "LinearBarWidget" or "ToggleButtonWidget" or "ActionButtonWidget")
                    return true;
            }
            return MicroControlNames.Contains(typeName);
        }

        private static bool IsMicroControlInvocation(IMethodSymbol symbol, InvocationExpressionSyntax node)
        {
            if (symbol != null)
            {
                var containingType = symbol.ContainingType;
                if (containingType != null && IsMicroControlType(containingType, containingType.Name))
                    return true;

                if (containingType?.Name == "WidgetControlContainer" && (symbol.Name is "Add" or "Register"))
                    return true;
            }
            string expr = node.Expression.ToString();
            return expr.StartsWith(WidgetSpecRules.ControlsAddPrefix, StringComparison.Ordinal) ||
                   expr.StartsWith("this.Controls.Add", StringComparison.Ordinal) ||
                   expr.StartsWith("Controls.Add", StringComparison.Ordinal) ||
                   expr.StartsWith("this.Controls.Register", StringComparison.Ordinal) ||
                   expr.StartsWith("Controls.Register", StringComparison.Ordinal) ||
                   expr.StartsWith("TextWidget.", StringComparison.Ordinal) ||
                   expr.StartsWith("LinearBarWidget.", StringComparison.Ordinal) ||
                   expr.StartsWith("ToggleButtonWidget.", StringComparison.Ordinal) ||
                   expr.StartsWith("ActionButtonWidget.", StringComparison.Ordinal);
        }

        private static bool IsUiFactoryCall(IMethodSymbol symbol, InvocationExpressionSyntax node)
        {
            if (symbol != null)
            {
                return symbol.ContainingType?.Name == "UIFactory" &&
                       (symbol.ContainingNamespace?.ToDisplayString().StartsWith("ModularFlightPanel.UI", StringComparison.Ordinal) ?? false);
            }
            return node.Expression.ToString().StartsWith(WidgetSpecRules.UiFactoryCallPrefix, StringComparison.Ordinal);
        }

        private static bool IsCpuRasterizerCall(IMethodSymbol symbol, string invokedName, InvocationExpressionSyntax node)
        {
            if (symbol != null)
            {
                if (symbol.Name is "SetPixels32" or "SetPixels" or "Apply")
                {
                    return IsSubclassOf(symbol.ContainingType, "UnityEngine.Texture2D", "Texture2D") ||
                           IsSubclassOf(symbol.ContainingType, "UnityEngine.Texture", "Texture");
                }
                return false;
            }
            return invokedName is "SetPixels32" or "SetPixels" ||
                   node.Expression.ToString().EndsWith(WidgetSpecRules.CpuRasterizerApiSuffix, StringComparison.Ordinal);
        }

        private static bool IsCpuRasterizerPixelField(FieldDeclarationSyntax field)
        {
            return field.Declaration.Variables.Any(v => v.Identifier.Text == WidgetSpecRules.CpuRasterizerPixelField);
        }

        private static bool IsStandardChannelCall(IMethodSymbol symbol, string invokedName)
        {
            if (symbol != null)
            {
                if (symbol.Name.StartsWith("GetTemplateChannel", StringComparison.Ordinal)) return true;
                if (IsSubclassOf(symbol.ContainingType, "ModularFlightPanel.UI.Framework.BaseFlightWidget", "BaseFlightWidget"))
                {
                    return symbol.Name.StartsWith("GetTemplateChannel", StringComparison.Ordinal);
                }
            }
            return invokedName.StartsWith("GetTemplateChannel", StringComparison.Ordinal);
        }

        private static bool IsStandardFormattingCall(IMethodSymbol symbol, string receiverName, string invokedName)
        {
            if (symbol != null)
            {
                string cName = symbol.ContainingType?.Name;
                if (cName is "AvionicsFormatting") return true;
                if (IsSubclassOf(symbol.ContainingType, "ModularFlightPanel.UI.Framework.BaseFlightWidget", "BaseFlightWidget"))
                {
                    return symbol.Name.StartsWith("Format", StringComparison.Ordinal);
                }
            }
            return receiverName is "AvionicsFormatting" ||
                   invokedName is "FormatDuration" or "FormatDurationCompact" or "FormatCountdown" or "FormatMetricDistance" or "FormatMetricSpeed";
        }

        private static bool IsFastFormatCall(IMethodSymbol symbol, string receiverName)
        {
            if (symbol != null) return symbol.ContainingType?.Name is "AvionicsFastFormat";
            return receiverName is "AvionicsFastFormat";
        }

        private static bool IsSmartUIExtensionCall(IMethodSymbol symbol, string invokedName)
        {
            if (symbol != null)
            {
                return symbol.ContainingType?.Name is "SmartUIExtensions";
            }
            return invokedName is "SetTextSafe" or "SetColor" or "SetAlpha" or "SetAnchoredPositionSafe" or "SetSizeDeltaSafe" or "SetActiveSafe" or "SetFillAmountSafe";
        }

        private static bool IsBannedDockSyncCall(IMethodSymbol symbol, InvocationExpressionSyntax node)
        {
            if (symbol != null)
            {
                return symbol.Name == "SyncAll" &&
                       (symbol.ContainingType?.Name == "DockAnchorTracker" ||
                        symbol.ContainingType?.ToDisplayString() == "ModularFlightPanel.UI.Framework.DockAnchorTracker");
            }
            string invokedName = RoslynAstHelper.GetInvokedMethodName(node);
            string receiverName = RoslynAstHelper.GetInvocationReceiver(node) is { } recv
                ? RoslynAstHelper.GetRightmostIdentifier(recv)
                : string.Empty;
            return invokedName == "SyncAll" && receiverName == "DockAnchorTracker";
        }

        private static bool IsGameObjectType(ITypeSymbol type, string typeText)
        {
            if (type != null)
            {
                return IsSubclassOf(type, "UnityEngine.GameObject", "GameObject");
            }
            return typeText is "GameObject" or "UnityEngine.GameObject";
        }

        private static bool IsUguiGraphicType(ITypeSymbol type, string typeText)
        {
            if (type != null)
            {
                return IsSubclassOf(type, "UnityEngine.UI.Graphic", "Graphic");
            }
            return typeText is "Image" or "RawImage" or "Text" or "Graphic" or
                   "UnityEngine.UI.Image" or "UnityEngine.UI.RawImage" or "UnityEngine.UI.Text" or "UnityEngine.UI.Graphic";
        }

        private static bool IsVertexHelperType(ITypeSymbol type, string typeText)
        {
            if (type != null)
            {
                return IsSubclassOf(type, "UnityEngine.UI.VertexHelper", "VertexHelper");
            }
            return typeText is "VertexHelper" or "UnityEngine.UI.VertexHelper";
        }

        private static bool IsMeshRebuildCall(IMethodSymbol symbol, string mName)
        {
            if (symbol != null)
            {
                if (symbol.Name is "SetVerticesDirty" or "SetLayoutDirty" or "SetMaterialDirty")
                {
                    return IsSubclassOf(symbol.ContainingType, "UnityEngine.UI.Graphic", "Graphic");
                }
                if (symbol.Name is "MarkLayoutForRebuild")
                {
                    return IsSubclassOf(symbol.ContainingType, "UnityEngine.UI.LayoutRebuilder", "LayoutRebuilder");
                }
            }
            return mName is "SetVerticesDirty" or "SetLayoutDirty" or "SetMaterialDirty" or "MarkLayoutForRebuild";
        }

        private static bool IsUguiGraphicOrTransformProperty(ISymbol symbol, string propName)
        {
            if (symbol is IPropertySymbol prop)
            {
                if (prop.Name is "anchoredPosition" or "sizeDelta" or "offsetMin" or "offsetMax")
                {
                    return IsSubclassOf(prop.ContainingType, "UnityEngine.RectTransform", "RectTransform");
                }
                if (prop.Name is "color" or "material")
                {
                    return IsSubclassOf(prop.ContainingType, "UnityEngine.UI.Graphic", "Graphic");
                }
                if (prop.Name is "text")
                {
                    return IsSubclassOf(prop.ContainingType, "UnityEngine.UI.Text", "Text");
                }
                if (prop.Name is "localScale")
                {
                    return IsSubclassOf(prop.ContainingType, "UnityEngine.Transform", "Transform");
                }
            }
            return propName is "anchoredPosition" or "sizeDelta" or "localScale" or "color" or "text" or "fillAmount";
        }

        /// <summary>
        /// 穿透判定赋值语句是否处于有效的死区/变动检查保护之下（Deadband / Dirty Guard）
        /// 彻底排除单纯判空 (if (x != null)) 的伪死区！
        /// </summary>
        private static bool IsDeadbandGuarded(AssignmentExpressionSyntax assign)
        {
            var ifAncestors = assign.Ancestors().OfType<IfStatementSyntax>();
            foreach (var ifStmt in ifAncestors)
            {
                var cond = ifStmt.Condition;
                if (cond == null) continue;

                // 1. 检查是否存在调用变动检测方法 (如 Cached.Update, Equals, Changed, SetIfChanged)
                var invocations = cond.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
                foreach (var inv in invocations)
                {
                    string mName = RoslynAstHelper.GetInvokedMethodName(inv);
                    if (mName is "Update" or "Equals" or "SetIfChanged" or "HasChanged" or "IsDirty")
                    {
                        return true;
                    }
                }

                // 2. 检查条件表达式中的二元比较：排除单纯的 != null 或 == null 判空
                var binaries = cond.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>();
                foreach (var bin in binaries)
                {
                    if (bin.IsKind(SyntaxKind.NotEqualsExpression) ||
                        bin.IsKind(SyntaxKind.EqualsExpression) ||
                        bin.IsKind(SyntaxKind.GreaterThanExpression) ||
                        bin.IsKind(SyntaxKind.LessThanExpression) ||
                        bin.IsKind(SyntaxKind.GreaterThanOrEqualExpression) ||
                        bin.IsKind(SyntaxKind.LessThanOrEqualExpression))
                    {
                        bool leftIsNull = bin.Left is LiteralExpressionSyntax lLit && lLit.IsKind(SyntaxKind.NullLiteralExpression);
                        bool rightIsNull = bin.Right is LiteralExpressionSyntax rLit && rLit.IsKind(SyntaxKind.NullLiteralExpression);

                        // 只要不是单纯的与 null 比较，就代表对数值或状态做了变动/阈值对比！
                        if (!leftIsNull && !rightIsNull)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsRedundantFormattingMethodName(string name)
        {
            return name is "FormatTime" or "FormatDuration" or "FormatDurationCompact" or "FormatCountdown"
                        or "FormatMetricDistance" or "FormatMetricSpeed" or "FormatDistanceKm"
                        or "FormatSpeed" or "FormatAltitude" or "FormatSeconds" or "FormatRate";
        }

        private static bool IsManualDirtyTrackingField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return false;
            return (fieldName.StartsWith(WidgetSpecRules.DirtyTrackingFieldPrefix, StringComparison.OrdinalIgnoreCase) ||
                    fieldName.StartsWith("_prev", StringComparison.OrdinalIgnoreCase)) &&
                   (fieldName.EndsWith("Text", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("Val", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("Value", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("Str", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("String", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("State", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("Role", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.EndsWith("Flag", StringComparison.OrdinalIgnoreCase));
        }

        // =========================================================================
        // 报告与格式化输出 (MSBuild & Terminal Summaries)
        // =========================================================================

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

            if (report.HotLoopMeshRebuildCount > 0)
            {
                foreach (var item in report.Items.Where(i => i.HotLoopMeshRebuilds > 0))
                {
                    sb.AppendLine($"{item.FilePath}(1,1): warning MFP_HOT_LOOP_MESH_REBUILD: [Anti-Pattern] {item.WidgetName}: Calls SetVerticesDirty/SetLayoutDirty in hot loop call graph ({item.HotLoopMeshRebuilds} times); triggers CPU mesh rebuild / Canvas invalidation");
                }
            }

            return sb.ToString();
        }

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
            sb.AppendLine($"零高频网格重建达成率:         {(report.TotalCount - report.HotLoopMeshRebuildCount)}/{report.TotalCount} 个组件已消除高频网格/布局脏标记");
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

        // =========================================================================
        // 自检套件：验证符号级穿透与高频闭包规则正反用例
        // =========================================================================

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

            // 1. 合规组件在热路径中无网格重建
            string cleanSrc = @"
using System;
using UnityEngine;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""clean_widget"")]
    public class CleanWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        public override void OnUpdateTelemetry(IFlightTelemetry t) {}
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var cleanFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "CleanWidget.cs", Path = "CleanWidget.cs", Text = cleanSrc } };
            var cleanCtx = SemanticCompilationProvider.BuildCompilation(cleanFiles);
            var cleanGraph = WidgetClassGraph.Build(cleanFiles, cleanCtx);
            var cleanReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = cleanGraph, SemanticContext = cleanCtx });
            check(cleanReport.Items.Count == 1 && cleanReport.Items[0].HotLoopMeshRebuilds == 0, "合规组件不应误报 HotLoopMeshRebuilds");

            // 2. 在 OnUpdateTelemetry 中直接调用 SetVerticesDirty 应被捕获
            string dirtySrc = @"
using System;
using UnityEngine;
using UnityEngine.UI;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""dirty_widget"")]
    public class DirtyWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        private Graphic _g;
        public override void OnUpdateTelemetry(IFlightTelemetry t) {
            if (_g != null) _g.SetVerticesDirty();
        }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var dirtyFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "DirtyWidget.cs", Path = "DirtyWidget.cs", Text = dirtySrc } };
            var dirtyCtx = SemanticCompilationProvider.BuildCompilation(dirtyFiles);
            var dirtyGraph = WidgetClassGraph.Build(dirtyFiles, dirtyCtx);
            var dirtyReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = dirtyGraph, SemanticContext = dirtyCtx });
            check(dirtyReport.Items.Count == 1 && dirtyReport.Items[0].HotLoopMeshRebuilds == 1, "OnUpdateTelemetry 直接调用 SetVerticesDirty 漏检");

            // 3. 在受染私有方法中调用 SetVerticesDirty 应被调用图闭包捕获
            string closureDirtySrc = @"
using System;
using UnityEngine;
using UnityEngine.UI;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""closure_dirty"")]
    public class ClosureDirtyWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        private Graphic _g;
        public override void OnUpdateTelemetry(IFlightTelemetry t) {
            InternalUpdate();
        }
        private void InternalUpdate() {
            _g.SetVerticesDirty();
        }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var closureFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "ClosureDirtyWidget.cs", Path = "ClosureDirtyWidget.cs", Text = closureDirtySrc } };
            var closureCtx = SemanticCompilationProvider.BuildCompilation(closureFiles);
            var closureGraph = WidgetClassGraph.Build(closureFiles, closureCtx);
            var closureReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = closureGraph, SemanticContext = closureCtx });
            check(closureReport.Items.Count == 1 && closureReport.Items[0].HotLoopMeshRebuilds == 1, "调用图闭包私有方法调用 SetVerticesDirty 漏检");

            // 4. VertexHelper 矢量网格生成特征识别与重型网格全量重建告警
            string vhDirtySrc = @"
using System;
using UnityEngine;
using UnityEngine.UI;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""vh_dirty"")]
    public class VhDirtyWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        private Graphic _g;
        public override void OnUpdateTelemetry(IFlightTelemetry t) {
            _g.SetVerticesDirty();
        }
        private void PopulateCustomMesh(VertexHelper vh) {
            vh.Clear();
        }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var vhFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "VhDirtyWidget.cs", Path = "VhDirtyWidget.cs", Text = vhDirtySrc } };
            var vhCtx = SemanticCompilationProvider.BuildCompilation(vhFiles);
            var vhGraph = WidgetClassGraph.Build(vhFiles, vhCtx);
            var vhReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = vhGraph, SemanticContext = vhCtx });
            check(vhReport.Items.Count == 1 && vhReport.Items[0].HasCustomVertexHelperMesh, "VertexHelper 参数特征漏检");
            check(vhReport.Items.Count == 1 && vhReport.Items[0].StandardizationSuggestions.Any(s => s.Contains("CPU 重型矢量网格全量重建")), "重型矢量网格反模式告警未触发");

            // 5. 冷路径 OnInitialize 调用 SetVerticesDirty 不应算入高频受染热路径
            string coldSrc = @"
using System;
using UnityEngine;
using UnityEngine.UI;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""cold_dirty"")]
    public class ColdDirtyWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        protected override void OnInitialize(WidgetConfig c, ThemeConfig t) {
            _g.SetVerticesDirty();
        }
        public override void ApplyTheme(ThemeConfig theme) {}
        private Graphic _g;
        public override void OnUpdateTelemetry(IFlightTelemetry t) {}
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var coldFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "ColdDirtyWidget.cs", Path = "ColdDirtyWidget.cs", Text = coldSrc } };
            var coldCtx = SemanticCompilationProvider.BuildCompilation(coldFiles);
            var coldGraph = WidgetClassGraph.Build(coldFiles, coldCtx);
            var coldReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = coldGraph, SemanticContext = coldCtx });
            check(coldReport.Items.Count == 1 && coldReport.Items[0].HotLoopMeshRebuilds == 0, "冷路径 OnInitialize 中的 SetVerticesDirty 不应被误报为热循环违规");

            // 6. 符号级穿透测试：集中停靠与裸 GameObject 分配识别
            string bannedSyncSrc = @"
using System;
using UnityEngine;
namespace N {
    enum WidgetRefreshTier { Standard }
    public static class DockAnchorTracker { public static void SyncAll() {} }
    [FlightWidget(""sync_widget"")]
    public class SyncWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        public override void OnUpdateTelemetry(IFlightTelemetry t) {
            DockAnchorTracker.SyncAll();
            var go = new GameObject(""test"");
        }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var syncFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "SyncWidget.cs", Path = "SyncWidget.cs", Text = bannedSyncSrc } };
            var syncCtx = SemanticCompilationProvider.BuildCompilation(syncFiles);
            var syncGraph = WidgetClassGraph.Build(syncFiles, syncCtx);
            var syncReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = syncGraph, SemanticContext = syncCtx });
            check(syncReport.Items.Count == 1 && syncReport.Items[0].HasBannedDockSyncCall, "集中调度 DockAnchorTracker.SyncAll 符号漏检");
            check(syncReport.Items.Count == 1 && syncReport.Items[0].RawGameObjectAllocs == 1, "裸 GameObject 分配漏检");

            // 7. 符号级方法重载消歧测试 (同名安全重载 vs 受染重载：精准消歧)
            string overloadSrc = @"
using System;
using UnityEngine;
using UnityEngine.UI;
namespace N {
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""overload_widget"")]
    public class OverloadWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public override void ApplyTheme(ThemeConfig theme) {}
        private Graphic _g;
        public override void OnUpdateTelemetry(IFlightTelemetry t) {
            SafeHelper(42); // 调用安全的 int 重载，不触碰 string 重载
        }
        private void SafeHelper(int count) {
            // 安全操作：无网格重建
        }
        private void SafeHelper(string taintedMsg) {
            _g.SetVerticesDirty(); // 受染重载：未被调用
        }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var overloadFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "OverloadWidget.cs", Path = "OverloadWidget.cs", Text = overloadSrc } };
            var overloadCtx = SemanticCompilationProvider.BuildCompilation(overloadFiles);
            var overloadGraph = WidgetClassGraph.Build(overloadFiles, overloadCtx);
            var overloadReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = overloadGraph, SemanticContext = overloadCtx });
            check(overloadReport.Items.Count == 1 && overloadReport.Items[0].HotLoopMeshRebuilds == 0, "符号级重载消歧失败：未调用的同名受染重载被错误纳入闭包");

            // 8. 微控件实现 ITelemetryBindableControl 契约的穿透性语义识别 (无需名字硬编码)
            string customControlSrc = @"
using System;
using UnityEngine;
namespace ModularFlightPanel.UI.Framework {
    public interface ITelemetryBindableControl {}
}
namespace N {
    using ModularFlightPanel.UI.Framework;
    public class MyExoticGauge : ITelemetryBindableControl {}
    enum WidgetRefreshTier { Standard }
    [FlightWidget(""custom_ctrl_widget"")]
    public class CustomCtrlWidget : BaseFlightWidget {
        public override Vector2 BaseSize => new Vector2(100, 100);
        public override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;
        public MyExoticGauge Gauge = new MyExoticGauge();
        public override void ApplyTheme(ThemeConfig theme) {}
        public override void OnUpdateTelemetry(IFlightTelemetry t) {}
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}";
            var customCtrlFiles = new List<WidgetSourceFile> { new WidgetSourceFile { Name = "CustomCtrlWidget.cs", Path = "CustomCtrlWidget.cs", Text = customControlSrc } };
            var customCtrlCtx = SemanticCompilationProvider.BuildCompilation(customCtrlFiles);
            var customCtrlGraph = WidgetClassGraph.Build(customCtrlFiles, customCtrlCtx);
            var customCtrlReport = Scan(new WidgetDiscoveryResult { Status = WidgetDiscoveryStatus.Ok, Graph = customCtrlGraph, SemanticContext = customCtrlCtx });
            check(customCtrlReport.Items.Count == 1 && customCtrlReport.Items[0].UsesMicroControlsDsl, "微控件 ITelemetryBindableControl 语义接口继承识别失败");

            LastSelfTestCaseCount = cases;
            return failures;
        }
    }
}
