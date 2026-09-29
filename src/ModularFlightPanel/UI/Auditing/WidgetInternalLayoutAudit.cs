using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModularFlightPanel.UI.Auditing
{
    /// <summary>
    /// 组件内部控件分类
    /// </summary>
    public enum InternalControlKind
    {
        DslText,
        DslBar,
        DslButton,
        DslCustom,
        ManualText,
        ManualButton,
        ManualPanel
    }

    /// <summary>
    /// 组件内部单个控件空间几何包围盒 (以组件中心 (0, 0) 为原点的局部参考坐标系)
    /// </summary>
    public sealed class InternalControlBox
    {
        public string ControlName { get; set; } = string.Empty;
        public InternalControlKind Kind { get; set; }
        public string Dock { get; set; } = "Custom";
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }
        public int LineNumber { get; set; }
        public string SourceSnippet { get; set; } = string.Empty;

        public float Width => Math.Max(0f, MaxX - MinX);
        public float Height => Math.Max(0f, MaxY - MinY);
        public float Area => Width * Height;

        public override string ToString() =>
            $"[{ControlName} ({Kind}, Dock={Dock}) X:[{MinX:F0}..{MaxX:F0}], Y:[{MinY:F0}..{MaxY:F0}], Area={Area:F0}]";
    }

    /// <summary>
    /// 组件内部控件重叠违规记录
    /// </summary>
    public sealed class InternalOverlapViolation
    {
        public string WidgetName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Severity { get; set; } = "ERROR";
        public string RuleCode { get; set; } = WidgetSpecRules.InternalControlOverlap;
        public InternalControlBox ControlA { get; set; }
        public InternalControlBox ControlB { get; set; }
        public float OverlapWidth { get; set; }
        public float OverlapHeight { get; set; }
        public float OverlapArea { get; set; }
        public float OverlapRatio { get; set; }
        public string Description { get; set; } = string.Empty;

        public override string ToString() =>
            $"[{FileName}:L{LineNumber}] {RuleCode} ({Severity}): {Description}";
    }

    /// <summary>
    /// 全局组件内部布局几何审计报告
    /// </summary>
    public sealed class WidgetInternalLayoutReport
    {
        public int WidgetsScanned { get; set; }
        public int TotalControlsEvaluated { get; set; }
        public List<InternalOverlapViolation> Violations { get; } = new List<InternalOverlapViolation>();
        public int ErrorCount => Violations.Count(v => v.Severity == "ERROR");
        public int WarningCount => Violations.Count(v => v.Severity == "WARNING");
    }

    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 组件内部微控件空间几何与重叠冲突审计内核
    /// ====================================================================================
    /// 核心检测能力 (MFP-SPEC-011 内部几何不变量)：
    /// 1. 双轨标题/顶栏重叠 (Dual Header / Title Overlap)：
    ///    组件声明了 DSL 顶栏控件 (TextWidget Title 等，自动挂载 TopLeft)，但在 OnInitialize / BuildHeader
    ///    中又通过 UIFactory.CreateText 重复创建标题图元，导致游戏内文字穿模叠加；
    /// 2. 双轨状态标牌重叠 (Dual Badge / Status Overlap)：
    ///    组件声明了 DSL 右上角标牌 (TextWidget StatusBadge 等，自动挂载 TopRight)，但在 OnInitialize 中
    ///    又手动创建同位置数值或标牌图元；
    /// 3. DSL 同泊靠冲突 (DSL Same-Dock Collision)：
    ///    同一组件内存在 2 个或以上未指定自定义坐标的 DSL 控件共享相同 Dock 锚点 (如多个默认 TopLeft)；
    /// 4. 自定义坐标包围盒相交 (Custom Coordinate AABB Spatial Collision)：
    ///    对显式设置 (X, Y, Width, Height) 的 DSL 与手动图元，计算 2D AABB 面积相交率，
    ///    相交率超过 15% 时触发重叠报警。
    /// </summary>
    public static class WidgetInternalLayoutAudit
    {
        public static int LastSelfTestCaseCount { get; private set; }

        /// <summary>
        /// 当前正在审计的组件所属语义模型（每次 AuditWidget 前刷新）。
        /// 几何数值一律先走 SemanticModel.GetConstantValue 做**语义常量折叠**，
        /// 才能吃到 `BASE_CARD_HEIGHT * 0.5f`、const 引用、括号嵌套这类纯字面量抠值抓不到的写法。
        /// </summary>
        private static SemanticModel _semanticModel;

        /// <summary>
        /// 由语义常量折叠（而非字面量直读）解析出的几何数值次数。
        /// 覆盖率计数：为 0 说明常量折叠点位已被拆除、几何取值退化回字面量扫描。
        /// </summary>
        public static int LastSemanticFoldedCount { get; private set; }

        /// <summary>
        /// 扫描全量组件源码中的内部控件空间几何与重叠冲突
        /// </summary>
        public static WidgetInternalLayoutReport Scan(WidgetDiscoveryResult discovery)
        {
            var report = new WidgetInternalLayoutReport();
            if (discovery == null || !discovery.CanAuditSource || discovery.Graph == null) return report;

            SemanticCompilationContext semanticContext =
                discovery.SemanticContext ?? discovery.Graph.SemanticContext;
            LastSemanticFoldedCount = 0;

            try
            {
                foreach (var node in discovery.Graph.ContractClasses)
                {
                    if (node.IsAbstract) continue;
                    report.WidgetsScanned++;

                    // 语义模型优先取类节点自带的（构建继承图时已绑定），缺位时按路径回查编译上下文。
                    _semanticModel = node.SemanticModel
                        ?? semanticContext?.GetSemanticModel(node.FilePath ?? node.FileName);

                    var violations = AuditWidget(node);
                    report.Violations.AddRange(violations);
                }
            }
            finally
            {
                _semanticModel = null;
            }

            return report;
        }

        /// <summary>
        /// 审计单个具体组件类的内部控件几何与重叠
        /// </summary>
        public static List<InternalOverlapViolation> AuditWidget(WidgetClassNode node)
        {
            var violations = new List<InternalOverlapViolation>();
            if (node == null || node.Decl == null) return violations;

            // 直接调用本方法的场景（非 Scan 入口）也要能拿到语义模型，否则常量折叠会被静默跳过。
            if (node.SemanticModel != null) _semanticModel = node.SemanticModel;

            var widgetClass = node.Decl;
            string widgetName = node.Name;
            string fileName = node.FileName;

            // 1. 获取组件参考尺寸 (BaseSize)
            var (panelW, panelH) = ResolveWidgetBaseSize(node);
            float halfW = panelW * 0.5f;
            float halfH = panelH * 0.5f;

            // 2. 收集所有声明式 DSL 微控件
            var dslControls = CollectDslControls(widgetClass, panelW, panelH);

            // 3. 收集所有手动命令式创建的文本与顶栏图元
            var manualControls = CollectManualControls(widgetClass, panelW, panelH);

            // ── 规则 1: 双轨标题/顶栏穿模重叠检测 (最典型缺陷，如图中火箭2D) ──
            var dslTitles = dslControls.Where(c => c.Dock == "TopLeft" || c.ControlName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var manualTitles = manualControls.Where(c => IsHeaderTitleManualControl(c)).ToList();

            if (dslTitles.Count > 0 && manualTitles.Count > 0)
            {
                foreach (var dslT in dslTitles)
                {
                    foreach (var manT in manualTitles)
                    {
                        violations.Add(new InternalOverlapViolation
                        {
                            WidgetName = widgetName,
                            FileName = fileName,
                            LineNumber = manT.LineNumber > 0 ? manT.LineNumber : dslT.LineNumber,
                            Severity = "ERROR",
                            RuleCode = WidgetSpecRules.InternalControlOverlap,
                            ControlA = dslT,
                            ControlB = manT,
                            Description = $"组件存在双轨标题重叠穿模: 头部声明了 DSL 控件 '{dslT.ControlName}' (Dock=TopLeft)，同时在初始化流程中通过 UIFactory 重复创建标题图元 '{manT.ControlName}'，导致游戏内文字完全重叠!"
                        });
                    }
                }
            }

            // ── 规则 2: 双轨右上状态标牌穿模重叠检测 ──
            var dslBadges = dslControls.Where(c => c.Dock == "TopRight" || c.ControlName.IndexOf("Badge", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var manualTopRight = manualControls.Where(c => IsHeaderTopRightManualControl(c, halfW, halfH)).ToList();

            if (dslBadges.Count > 0 && manualTopRight.Count > 0)
            {
                foreach (var dslB in dslBadges)
                {
                    foreach (var manR in manualTopRight)
                    {
                        violations.Add(new InternalOverlapViolation
                        {
                            WidgetName = widgetName,
                            FileName = fileName,
                            LineNumber = manR.LineNumber > 0 ? manR.LineNumber : dslB.LineNumber,
                            Severity = "ERROR",
                            RuleCode = WidgetSpecRules.InternalControlOverlap,
                            ControlA = dslB,
                            ControlB = manR,
                            Description = $"组件存在双轨右上标牌重叠穿模: 头部声明了 DSL 控件 '{dslB.ControlName}' (Dock=TopRight)，同时在初始化流程中手动创建右上图元 '{manR.ControlName}'，导致视觉交叉!"
                        });
                    }
                }
            }

            // ── 规则 3: DSL 同向泊靠冲突 (Same-Dock Collision) ──
            for (int i = 0; i < dslControls.Count; i++)
            {
                for (int j = i + 1; j < dslControls.Count; j++)
                {
                    var a = dslControls[i];
                    var b = dslControls[j];

                    // 非 Custom 泊靠且 Dock 完全相同时，必重叠
                    if (a.Dock != "Custom" && b.Dock != "Custom" && string.Equals(a.Dock, b.Dock, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(new InternalOverlapViolation
                        {
                            WidgetName = widgetName,
                            FileName = fileName,
                            LineNumber = b.LineNumber > 0 ? b.LineNumber : a.LineNumber,
                            Severity = "ERROR",
                            RuleCode = WidgetSpecRules.InternalControlOverlap,
                            ControlA = a,
                            ControlB = b,
                            Description = $"DSL 微控件锚点冲突: '{a.ControlName}' 与 '{b.ControlName}' 均使用未偏移 Dock={a.Dock} 泊靠，导致在同一坐标完全重合!"
                        });
                    }
                }
            }

            // ── 规则 4: AABB 2D 空间包围盒碰撞检测 (覆盖自定义坐标与已测算图元) ──
            var allBoxes = new List<InternalControlBox>();
            allBoxes.AddRange(dslControls);
            allBoxes.AddRange(manualControls);

            for (int i = 0; i < allBoxes.Count; i++)
            {
                for (int j = i + 1; j < allBoxes.Count; j++)
                {
                    var a = allBoxes[i];
                    var b = allBoxes[j];

                    // 避免重复记录已知双轨/同泊靠错误
                    if (violations.Any(v => (v.ControlA == a && v.ControlB == b) || (v.ControlA == b && v.ControlB == a)))
                        continue;

                    // 计算 AABB 相交矩形
                    float overlapW = Math.Max(0f, Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX));
                    float overlapH = Math.Max(0f, Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY));

                    if (overlapW > 4f && overlapH > 4f)
                    {
                        float area = overlapW * overlapH;
                        float minArea = Math.Min(a.Area, b.Area);
                        float ratio = minArea > 0.01f ? area / minArea : 0f;

                        // 仅当重叠面积超过较小控件面积 18% 时报警，规避边缘 1~2px 边框接触
                        if (ratio > 0.18f)
                        {
                            violations.Add(new InternalOverlapViolation
                            {
                                WidgetName = widgetName,
                                FileName = fileName,
                                LineNumber = b.LineNumber > 0 ? b.LineNumber : a.LineNumber,
                                Severity = "WARNING",
                                RuleCode = WidgetSpecRules.InternalControlOverlap,
                                ControlA = a,
                                ControlB = b,
                                OverlapWidth = overlapW,
                                OverlapHeight = overlapH,
                                OverlapArea = area,
                                OverlapRatio = ratio,
                                Description = $"内部控件空间几何干涉: '{a.ControlName}' 与 '{b.ControlName}' 存在 {ratio * 100:F1}% 空间重叠 (相交区域 {overlapW:F0}x{overlapH:F0}px)!"
                            });
                        }
                    }
                }
            }

            return violations;
        }

        private static bool IsHeaderTitleManualControl(InternalControlBox c)
        {
            if (c.Kind != InternalControlKind.ManualText) return false;
            string n = c.ControlName.ToLowerInvariant();
            if (n.Contains("subtitle") || n.Contains("baytitle") || n.Contains("frametitle") || n.Contains("unittitle") || n.Contains("notetitle"))
                return false;
            return n.Contains("title") || n.Contains("headertitle") || n.Contains("toplabel");
        }

        private static bool IsHeaderTopRightManualControl(InternalControlBox c, float halfW, float halfH)
        {
            if (c.Kind != InternalControlKind.ManualText) return false;
            string n = c.ControlName.ToLowerInvariant();
            if (n.Contains("badge") || n.Contains("status") || n.Contains("mode") || n.Contains("armed") || n.Contains("summarydv"))
                return true;
            // 位于右上象限
            return c.MinX > (halfW * 0.2f) && c.MinY > (halfH * 0.5f);
        }

        private static (float width, float height) ResolveWidgetBaseSize(WidgetClassNode node)
        {
            var baseSizeProp = node.Decl.Members.OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(p => p.Identifier.Text == WidgetSpecRules.BaseSizeProperty);

            if (baseSizeProp != null)
            {
                var nums = baseSizeProp.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Where(lit => lit.IsKind(SyntaxKind.NumericLiteralExpression))
                    .Select(lit => Convert.ToSingle(lit.Token.Value))
                    .ToList();

                if (nums.Count >= 2 && nums[0] > 0f && nums[1] > 0f)
                {
                    return (nums[0], nums[1]);
                }

                // 检查类内部是否引用了常量 DefaultWidth / DefaultHeight
                var identifiers = baseSizeProp.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier.Text).ToList();

                float w = 0f, h = 0f;
                foreach (var field in node.Decl.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        // 走统一的数值解析入口：既能读字面量，也能对 `300f * 0.5f` / const 引用做语义折叠，
                        // 不再要求初始值必须长得像个字面量。
                        if (identifiers.Contains(v.Identifier.Text) && v.Initializer != null &&
                            TryParseFloatExpression(v.Initializer.Value, out float val))
                        {
                            if (v.Identifier.Text.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0) w = val;
                            else if (v.Identifier.Text.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0) h = val;
                        }
                    }
                }
                if (w > 0f && h > 0f) return (w, h);
            }

            return (220f, 50f);
        }

        private static List<InternalControlBox> CollectDslControls(ClassDeclarationSyntax widgetClass, float panelW, float panelH)
        {
            var list = new List<InternalControlBox>();
            float halfW = panelW * 0.5f;
            float halfH = panelH * 0.5f;

            var dslTypes = new HashSet<string>(WidgetSpecRules.MicroControlDslTypes, StringComparer.OrdinalIgnoreCase);

            foreach (var field in widgetClass.Members.OfType<FieldDeclarationSyntax>())
            {
                string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);
                if (!dslTypes.Contains(typeName)) continue;

                foreach (var variable in field.Declaration.Variables)
                {
                    string ctrlName = variable.Identifier.Text;
                    int line = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    string initStr = variable.Initializer?.Value?.ToString() ?? string.Empty;

                    var box = new InternalControlBox
                    {
                        ControlName = ctrlName,
                        Kind = typeName.Contains("Bar") ? InternalControlKind.DslBar :
                               typeName.Contains("Button") ? InternalControlKind.DslButton : InternalControlKind.DslText,
                        LineNumber = line,
                        SourceSnippet = initStr
                    };

                    // 解析 Dock 锚点与坐标
                    if (initStr.Contains(".Title("))
                    {
                        box.Dock = "TopLeft";
                        box.MinX = -halfW + 8f;
                        box.MaxX = Math.Min(0f, -halfW + 8f + 95f);
                        box.MinY = halfH - 24f;
                        box.MaxY = halfH - 6f;
                    }
                    else if (initStr.Contains(".Badge("))
                    {
                        box.Dock = "TopRight";
                        box.MinX = Math.Max(0f, halfW - 80f);
                        box.MaxX = halfW - 8f;
                        box.MinY = halfH - 24f;
                        box.MaxY = halfH - 6f;
                    }
                    else if (initStr.Contains(".Value("))
                    {
                        box.Dock = "Center";
                        box.MinX = -halfW + 8f;
                        box.MaxX = halfW * 0.44f;
                        box.MinY = -halfH * 0.5f;
                        box.MaxY = halfH * 0.48f;
                    }
                    else if (initStr.Contains(".Unit("))
                    {
                        box.Dock = "BottomRight";
                        box.MinX = halfW * 0.44f;
                        box.MaxX = halfW;
                        box.MinY = -halfH * 0.4f;
                        box.MaxY = halfH * 0.3f;
                    }
                    else if (initStr.Contains(".BottomBar("))
                    {
                        box.Dock = "Bottom";
                        box.MinX = -halfW + 8f;
                        box.MaxX = halfW - 8f;
                        box.MinY = -halfH + 8f;
                        box.MaxY = -halfH + 12f;
                    }
                    else if (initStr.Contains("WidgetDock.TopLeft"))
                    {
                        box.Dock = "TopLeft";
                        box.MinX = -halfW + 8f;
                        box.MaxX = Math.Min(0f, -halfW + 8f + 95f);
                        box.MinY = halfH - 24f;
                        box.MaxY = halfH - 6f;
                    }
                    else if (initStr.Contains("WidgetDock.TopRight"))
                    {
                        box.Dock = "TopRight";
                        box.MinX = Math.Max(0f, halfW - 80f);
                        box.MaxX = halfW - 8f;
                        box.MinY = halfH - 24f;
                        box.MaxY = halfH - 6f;
                    }
                    else
                    {
                        // 提取 new TextWidget(..., x, y, w, h, ...)
                        var creation = variable.Initializer?.Value as ObjectCreationExpressionSyntax;
                        var floatArgs = new List<float>();
                        if (creation?.ArgumentList != null)
                        {
                            foreach (var arg in creation.ArgumentList.Arguments)
                            {
                                if (TryParseFloatExpression(arg.Expression, out float val))
                                {
                                    floatArgs.Add(val);
                                }
                            }
                        }

                        if (floatArgs.Count >= 4)
                        {
                            float x = floatArgs[0];
                            float y = floatArgs[1];
                            float w = floatArgs[2];
                            float h = floatArgs[3];
                            box.Dock = "Custom";
                            box.MinX = x;
                            box.MaxX = x + Math.Max(w, 0f);
                            box.MinY = y - Math.Max(h, 16f) * 0.5f;
                            box.MaxY = y + Math.Max(h, 16f) * 0.5f;
                        }
                        else
                        {
                            box.Dock = "Custom";
                            box.MinX = -halfW + 10f;
                            box.MaxX = halfW - 10f;
                            box.MinY = -halfH + 10f;
                            box.MaxY = halfH - 10f;
                        }
                    }

                    list.Add(box);
                }
            }

            return list;
        }

        private static List<InternalControlBox> CollectManualControls(ClassDeclarationSyntax widgetClass, float panelW, float panelH)
        {
            var list = new List<InternalControlBox>();
            float halfW = panelW * 0.5f;
            float halfH = panelH * 0.5f;

            // 查找所有 UIFactory.CreateText 调用
            var invocations = widgetClass.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(inv => inv.Expression.ToString().StartsWith("UIFactory.CreateText", StringComparison.Ordinal) ||
                              inv.Expression.ToString().EndsWith(".CreateText", StringComparison.Ordinal));

            foreach (var inv in invocations)
            {
                var args = inv.ArgumentList?.Arguments;
                if (args == null || args.Value.Count < 2) continue;

                // 第二个参数通常是控件 name / id
                string ctrlName = "ManualText";
                var rawParentArg = args.Value[0].Expression.ToString();
                var parentLower = rawParentArg.ToLowerInvariant();
                bool isIgnoredSubContainer = parentLower.StartsWith("btn") ||
                                             parentLower.Contains("row") || parentLower.Contains("item") || parentLower.Contains("slot") ||
                                             parentLower.Contains("tooltip") || parentLower.Contains("popup") ||
                                             parentLower.Contains("cell") || parentLower.Contains("box") || parentLower.Contains("capsule") ||
                                             parentLower.Contains("badge") || parentLower.Contains("escort") ||
                                             parentLower.Contains("dropdown") || parentLower.Contains("matrix");
                if (isIgnoredSubContainer) continue;

                bool isRootOrHeaderLevel = rawParentArg == "transform" || rawParentArg == "parent" ||
                                           rawParentArg == "_panelBg.transform" || rawParentArg == "gameObject.transform" ||
                                           rawParentArg == "this.transform" || parentLower.Contains("header");
                if (!isRootOrHeaderLevel) continue;

                var nameArg = args.Value[1].Expression;
                if (nameArg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    ctrlName = lit.Token.ValueText;
                }

                int line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var box = new InternalControlBox
                {
                    ControlName = ctrlName,
                    Kind = InternalControlKind.ManualText,
                    LineNumber = line,
                    SourceSnippet = inv.ToString()
                };

                // 判断是否为顶栏标题或右上标牌
                string lowerName = ctrlName.ToLowerInvariant();
                if (lowerName.Contains("title") && !lowerName.Contains("sub") && !lowerName.Contains("bay") && !lowerName.Contains("frame"))
                {
                    box.Dock = "TopLeft";
                    box.MinX = -halfW + 10f;
                    box.MaxX = -halfW + 10f + 76f;
                    box.MinY = halfH - 24f;
                    box.MaxY = halfH - 6f;
                    list.Add(box);
                }
                else if (lowerName.Contains("badge") || lowerName.Contains("summarydv"))
                {
                    box.Dock = "TopRight";
                    box.MinX = halfW - 80f;
                    box.MaxX = halfW - 8f;
                    box.MinY = halfH - 24f;
                    box.MaxY = halfH - 6f;
                    list.Add(box);
                }
            }

            return list;
        }

        /// <summary>
        /// 审计内核自检：验证内部几何与重叠冲突规则的假阳性与假阴性
        /// </summary>
        public static List<string> RunSelfTest()
        {
            var failures = new List<string>();
            int testCount = 0;

            Action<bool, string> check = (cond, msg) =>
            {
                testCount++;
                if (!cond) failures.Add(msg);
            };

            // 测试 1: 双轨标题冲突 (声明了 DSL Title，又手动 CreateText("Title"))
            string dualHeaderSrc = @"
using UnityEngine;
using ModularFlightPanel.UI.Framework;
public class DualHeaderWidget : BaseFlightWidget
{
    public override Vector2 BaseSize => new Vector2(260f, 176f);
    public TextWidget Title = TextWidget.Title(""ROCKET 2D"");
    protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
    {
        var t = UIFactory.CreateText(transform, ""Title"", ""ROCKET 2D"", 12, TextAnchor.MiddleLeft, Color.white);
    }
}";
            var nodeDual = ParseTestClass(dualHeaderSrc);
            var violationsDual = AuditWidget(nodeDual);
            check(violationsDual.Any(v => v.RuleCode == WidgetSpecRules.InternalControlOverlap && v.Description.Contains("双轨标题重叠")),
                "自检失败: 未拦截双轨标题重叠冲突 (DSL Title + UIFactory Title)");

            // 测试 2: DSL 相同非偏移 Dock 冲突 (两个 TopLeft)
            string sameDockSrc = @"
using UnityEngine;
using ModularFlightPanel.UI.Framework;
public class SameDockWidget : BaseFlightWidget
{
    public override Vector2 BaseSize => new Vector2(260f, 176f);
    public TextWidget N2Title = TextWidget.Title(""N2"");
    public TextWidget FfTitle = TextWidget.Title(""FF"");
}";
            var nodeSame = ParseTestClass(sameDockSrc);
            var violationsSame = AuditWidget(nodeSame);
            check(violationsSame.Any(v => v.RuleCode == WidgetSpecRules.InternalControlOverlap && v.Description.Contains("锚点冲突")),
                "自检失败: 未拦截 DSL 同向锚点重叠冲突 (两个 TopLeft)");

            // 测试 3: 合规无重叠组件 (单一 DSL Title + 单一 DSL Badge，无手动顶栏重复)
            string cleanDslSrc = @"
using UnityEngine;
using ModularFlightPanel.UI.Framework;
public class CleanDslWidget : BaseFlightWidget
{
    public override Vector2 BaseSize => new Vector2(260f, 176f);
    public TextWidget Title = TextWidget.Title(""ROCKET 2D"");
    public TextWidget StatusBadge = TextWidget.Badge(""ARMED"");
}";
            var nodeClean = ParseTestClass(cleanDslSrc);
            var violationsClean = AuditWidget(nodeClean);
            check(violationsClean.Count == 0,
                $"自检失败: 合规声明式 DSL 组件被误判有重叠 (误判数: {violationsClean.Count})");

            // 测试 4: 合规自定义坐标微控件 (坐标隔离，如 OrbitalInfoWidget)
            string cleanCoordsSrc = @"
using UnityEngine;
using ModularFlightPanel.UI.Framework;
public class CleanCoordsWidget : BaseFlightWidget
{
    public override Vector2 BaseSize => new Vector2(320f, 36f);
    public TextWidget PeLabel = new TextWidget(TextStyleRole.Label, -120f, -6f, 26f, 16f, 8.5f, TextAnchor.MiddleLeft, ""PE"");
    public TextWidget PeVal = new TextWidget(TextStyleRole.PrimaryValue, -94f, -6f, 80f, 16f, 11f, TextAnchor.MiddleRight, ""---"");
    public TextWidget PeUnit = new TextWidget(TextStyleRole.Unit, -14f, -6f, 0f, 16f, 8f, TextAnchor.MiddleLeft, """");
}";
            var nodeCoords = ParseTestClass(cleanCoordsSrc);
            var violationsCoords = AuditWidget(nodeCoords);
            check(violationsCoords.Count == 0,
                $"自检失败: 合规自定义坐标微控件被误判有重叠 (误判数: {violationsCoords.Count})");

            // 测试 5: 语义常量折叠 —— 纯字面量抠值永远读不出 `BASE * 0.5f` 这类写法
            const string constFoldSrc = @"
namespace FoldTest
{
    public enum Role { Label, Value }

    public class Sizes
    {
        public const float BASE = 320f;
        public const float HALF = BASE * 0.5f;
        public float A = HALF;
        public float B = BASE / 4f - 10f;
        public Role R = Role.Value;
    }
}";
            var foldSources = new List<WidgetSourceFile>
            {
                new WidgetSourceFile { Name = "FoldTest.cs", Path = "FoldTest.cs", Text = constFoldSrc }
            };
            SemanticCompilationContext foldContext = SemanticCompilationProvider.BuildCompilation(foldSources);
            SemanticModel foldModel = foldContext?.GetSemanticModel("FoldTest.cs");
            if (foldModel == null)
            {
                check(false, "自检失败: 语义常量折叠用例未能取得 SemanticModel");
            }
            else
            {
                var sizesClass = foldModel.SyntaxTree.GetRoot().DescendantNodes()
                    .OfType<ClassDeclarationSyntax>().First();
                var initializers = sizesClass.Members.OfType<FieldDeclarationSyntax>()
                    .SelectMany(f => f.Declaration.Variables)
                    .Where(v => v.Initializer != null)
                    .ToList();
                var fieldA = initializers.FirstOrDefault(v => v.Identifier.Text == "A");
                var fieldB = initializers.FirstOrDefault(v => v.Identifier.Text == "B");
                var fieldR = initializers.FirstOrDefault(v => v.Identifier.Text == "R");

                // 无语义模型时必须折不出来：否则说明"折叠成功"是被别的兜底蒙对的，本点位等于没生效。
                if (fieldA == null || fieldB == null || fieldR == null)
                {
                    check(false, "自检失败: 常量折叠用例的合成字段缺失");
                }
                else
                {
                    _semanticModel = null;
                    check(!TryParseFloatExpression(fieldA.Initializer.Value, out _),
                        "自检失败: 无语义模型时 HALF 不应被解析出数值（常量折叠点位未真正生效）");

                    _semanticModel = foldModel;
                    int foldedBefore = LastSemanticFoldedCount;
                    check(TryParseFloatExpression(fieldA.Initializer.Value, out float foldedHalf) && Math.Abs(foldedHalf - 160f) < 0.001f,
                        "自检失败: 语义常量折叠未把 HALF (BASE * 0.5f) 折成 160");
                    check(TryParseFloatExpression(fieldB.Initializer.Value, out float foldedB) && Math.Abs(foldedB - 70f) < 0.001f,
                        "自检失败: 语义常量折叠未把 BASE / 4f - 10f 折成 70");
                    check(LastSemanticFoldedCount - foldedBefore >= 2,
                        "自检失败: 语义常量折叠覆盖率计数未增长");

                    // 枚举陷阱：Roslyn 对枚举成员同样返回编译期常量，若不显式过滤，
                    // DSL 构造参数位置会被 TextStyleRole / TextAnchor / WidgetDock 顶掉，几何整段错位。
                    check(!TryParseFloatExpression(fieldR.Initializer.Value, out _),
                        "自检失败: 枚举成员 Role.Value 被误当作几何数值（会导致 DSL 参数错位）");

                    _semanticModel = null;
                }
            }

            LastSelfTestCaseCount = testCount;
            return failures;
        }

        private static bool TryParseFloatExpression(ExpressionSyntax expr, out float value)
        {
            value = 0f;
            if (expr == null) return false;

            // ① 穿透性语义常量折叠优先：支持 `BASE_CARD_HEIGHT * 0.5f`、const 引用、括号嵌套、
            //    类型转换等一切编译期可求值写法，取值与 Roslyn 折出的真值完全一致。
            if (TryFoldSemanticConstant(expr, out value)) return true;

            // ② 无语义模型时才降级为字面量抠值（含一元负号）
            if (expr is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                if (TryParseFloatExpression(unary.Operand, out float inner))
                {
                    value = -inner;
                    return true;
                }
                return false;
            }

            if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                try
                {
                    value = Convert.ToSingle(lit.Token.Value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 语义常量折叠：把表达式交给 SemanticModel 在编译期求值。
        /// 这是本审计器"直达 C# 底层"的那一层 —— 不再靠语法形状猜数值。
        /// </summary>
        private static bool TryFoldSemanticConstant(ExpressionSyntax expr, out float value)
        {
            value = 0f;
            SemanticModel model = _semanticModel;
            if (model == null || expr == null) return false;

            // 语义模型只对属于自己的那棵语法树有效；跨树查询会抛异常而非返回空值。
            if (expr.SyntaxTree != model.SyntaxTree) return false;

            try
            {
                // 【陷阱】枚举成员 (TextStyleRole.Label / TextAnchor.MiddleLeft / WidgetDock.TopLeft …)
                // 在 Roslyn 里也是编译期常量，GetConstantValue 会对它们返回底层整数值。
                // 若不过滤，DSL 构造参数的位置会被枚举常量顶掉，几何坐标整段错位。
                ITypeSymbol exprType = model.GetTypeInfo(expr).Type;
                if (exprType != null && exprType.TypeKind == TypeKind.Enum) return false;

                Optional<object> constant = model.GetConstantValue(expr);
                if (!constant.HasValue || constant.Value == null) return false;

                object raw = constant.Value;
                if (raw is float || raw is double || raw is int || raw is long || raw is short ||
                    raw is sbyte || raw is byte || raw is uint || raw is ulong || raw is ushort)
                {
                    value = Convert.ToSingle(raw);
                    LastSemanticFoldedCount++;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static WidgetClassNode ParseTestClass(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            return new WidgetClassNode
            {
                Name = classDecl.Identifier.Text,
                FileName = classDecl.Identifier.Text + ".cs",
                FilePath = classDecl.Identifier.Text + ".cs",
                Decl = classDecl
            };
        }
    }
}
