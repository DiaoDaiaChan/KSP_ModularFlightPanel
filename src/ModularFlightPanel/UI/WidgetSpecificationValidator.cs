using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 组件违规详细记录
    /// </summary>
    public class WidgetViolation
    {
        public string WidgetName { get; set; }
        public string RuleCode { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; } // "ERROR" or "WARNING"
        public string Location { get; set; }

        public override string ToString()
        {
            return $"[{Severity}] [{RuleCode}] {WidgetName}: {Description} ({Location})";
        }
    }

    /// <summary>
    /// 规范审计报告
    /// </summary>
    public class WidgetValidationReport
    {
        public int ReflectionWidgetsAudited { get; set; }
        public int SourceWidgetsAudited { get; set; }

        private int _totalWidgetsAudited;
        public int TotalWidgetsAudited
        {
            get => _totalWidgetsAudited > 0 ? _totalWidgetsAudited : Math.Max(ReflectionWidgetsAudited, SourceWidgetsAudited);
            set => _totalWidgetsAudited = value;
        }

        public int TotalChecksPerformed { get; set; }
        public int ErrorCount => Violations.Count(v => v.Severity == "ERROR");
        public int WarningCount => Violations.Count(v => v.Severity == "WARNING");
        public List<WidgetViolation> Violations { get; } = new List<WidgetViolation>();
        public bool IsCompliant => ErrorCount == 0;

        public string GenerateSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=======================================================================");
            sb.AppendLine("           MODULAR FLIGHT PANEL 组件规范合法性校验报告 (SPEC AUDIT)");
            sb.AppendLine("=======================================================================");
            sb.AppendLine($"已审计组件总数: {TotalWidgetsAudited} (反射检查: {ReflectionWidgetsAudited}, 源码扫描: {SourceWidgetsAudited})");
            sb.AppendLine($"已执行规则检查: {TotalChecksPerformed}");
            sb.AppendLine($"错误违规项 (ERROR):   {ErrorCount}");
            sb.AppendLine($"潜在风险项 (WARNING): {WarningCount}");
            sb.AppendLine($"颜色基线债务 (DEBT):  {WidgetColorLiteralAudit.TotalRegisteredDebt} 行 (当前目标: 0 容忍)");
            sb.AppendLine("-----------------------------------------------------------------------");

            if (ErrorCount == 0 && WarningCount == 0)
            {
                sb.AppendLine("✔ [100% COMPLIANT] 全量飞行仪表组件架构与代码规范完全符合最高标准 (0 错误, 0 告警):");
                sb.AppendLine("  ├─ [MFP-SPEC-001] 统一继承 BaseFlightWidget 并重写装配 (OnInitialize)");
                sb.AppendLine("  ├─ [MFP-SPEC-002] 显式声明刷新阶梯 (RefreshTier: Critical / Standard / Relaxed / UltraLow)");
                sb.AppendLine("  ├─ [MFP-SPEC-003] 全面实现 ApplyTheme(ThemeConfig) 接入 WidgetStyleManager 单向管道");
                sb.AppendLine("  ├─ [MFP-SPEC-004] 遥测契约解耦 (OnUpdateTelemetry) 与脏标记阈值重绘保护 (0 强引用 Vessel/Part)");
                sb.AppendLine("  ├─ [MFP-SPEC-005] 严格 override OnDestroy / Update，生命周期无泄漏、无成员隐藏 (0 CS0114)");
                sb.AppendLine("  ├─ [MFP-SPEC-006] 零颜色字面量 (0 Color Literals, 零容忍)，100% 接入 WidgetStyleManager 语义角色");
                sb.AppendLine("  └─ [MFP-SPEC-007] 杜绝组件内场景查询 (0 FindObjectOfType / FindObjectsByType / GameObject.Find 家族)");
            }
            else if (ErrorCount == 0)
            {
                sb.AppendLine($"✔ [COMPLIANT WITH WARNINGS] 组件核心契约达标，但存在 {WarningCount} 处潜在风险建议关注:");
                foreach (var v in Violations.Where(v => v.Severity == "WARNING"))
                {
                    sb.AppendLine($"  {v}");
                }
            }
            else
            {
                sb.AppendLine($"✘ [SPEC VIOLATIONS DETECTED] 检测到 {ErrorCount} 处严重违规，禁止提交或合并:");
                foreach (var v in Violations.Where(v => v.Severity == "ERROR"))
                {
                    sb.AppendLine($"  {v}");
                }
                if (WarningCount > 0)
                {
                    sb.AppendLine("-----------------------------------------------------------------------");
                    sb.AppendLine($"[WARNINGS] 另有 {WarningCount} 处潜在风险:");
                    foreach (var v in Violations.Where(v => v.Severity == "WARNING"))
                    {
                        sb.AppendLine($"  {v}");
                    }
                }
            }
            sb.AppendLine("=======================================================================");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 飞行仪表组件规范合法性校验引擎 (WidgetSpecificationValidator)
    /// 
    /// 严格把关全项目所有继承自 BaseFlightWidget 的组件，
    /// 包含反射层级分析与源码 AST 静态扫描双重机制：
    /// 
    /// 1. 继承契约规则 (Rule_Inheritance): 必须派生自 BaseFlightWidget 并重写 OnInitialize。
    /// 2. 刷新阶梯规则 (Rule_RefreshTier): 必须显式声明或重写 RefreshTier 属性 (Critical/Standard/Relaxed/UltraLow)。
    /// 3. 主题管道规则 (Rule_SemanticTheming): 必须实现 ApplyTheme 并接入 WidgetStyleManager。
    /// 4. 遥测解耦规则 (Rule_TelemetryContract): 必须实现 OnUpdateTelemetry(IFlightTelemetry)，禁止私自持有 Vessel/Part 强引用。
    /// 5. 安全生命周期规则 (Rule_SafeLifecycle): 严格 override OnDestroy() 与 Update()，禁止隐式成员隐藏 (0 CS0114)。
    /// 6. 反硬编码偷懒规则 (Rule_NoHardcodedColors): 零容忍颜色字面量 (0 Color Literals)，禁止内部脱离 ThemeConfig 定义色彩常量。
    /// 7. 零场景查询规则 (Rule_NoSceneQueriesInUpdate): 组件内不得出现任何 Unity 场景查询 API
    ///    (FindObjectOfType / FindObjectsByType / FindFirstObjectByType / FindAnyObjectByType /
    ///     FindObjectsByType / GameObject.Find 家族)，一律改由 ProbeManager 统一调度。
    ///
    /// 【单一定义】规则码与规则实现（含"哪些文件算组件"的发现逻辑）唯一存在于 UI/WidgetSourceAudit.cs，
    /// 本类只负责反射级校验与结果汇总；无头验证器调用同一内核，因此不存在两份正则各说各话的可能。
    /// </summary>
    public static class WidgetSpecificationValidator
    {
        // 规则码只有一份定义（WidgetSpecRules），此处仅作兼容别名，禁止再写字面量
        public const string Rule_Inheritance = WidgetSpecRules.Inheritance;
        public const string Rule_RefreshTier = WidgetSpecRules.RefreshTier;
        public const string Rule_SemanticTheming = WidgetSpecRules.SemanticTheming;
        public const string Rule_TelemetryContract = WidgetSpecRules.TelemetryContract;
        public const string Rule_SafeLifecycle = WidgetSpecRules.SafeLifecycle;
        public const string Rule_NoHardcodedColors = WidgetSpecRules.NoHardcodedColors;
        public const string Rule_NoSceneQueriesInUpdate = WidgetSpecRules.NoSceneQueries;

        /// <summary>
        /// 审计内核自检（与 tools/HeadlessValidator [7/8] 同步调用纯 C# 词法器与规则正反用例）
        /// </summary>
        public static List<string> RunSelfTest()
        {
            var failures = new List<string>();
            try
            {
                failures.AddRange(CSharpSourceLinter.SelfTest());
                failures.AddRange(WidgetSourceAudit.SelfTest());
            }
            catch (Exception ex)
            {
                failures.Add("自检执行异常: " + ex.Message);
            }
            return failures;
        }

        /// <summary>
        /// 开发机/游戏内一键审计：反射规则 + 源码级规则（MFP-SPEC-001..007）+ 内核自检
        /// </summary>
        public static WidgetValidationReport RunDevelopmentAudit()
        {
            var report = ValidateAllWidgets(WidgetSourceAudit.ResolveRepositoryRoot());

            // 运行审计内核自检，杜绝规则与词法清洗器静默失效
            var selfTestFailures = RunSelfTest();
            report.TotalChecksPerformed += 16;
            foreach (var failure in selfTestFailures)
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = "AuditKernel",
                    RuleCode = "KERNEL-SELFTEST",
                    Severity = "ERROR",
                    Description = "审计内核自检失败: " + failure,
                    Location = "WidgetSourceAudit.SelfTest"
                });
            }

            return report;
        }

        /// <summary>
        /// 执行全量规范校验
        /// </summary>
        /// <param name="repositoryRoot">
        /// 可选的仓库根目录；若为空则自动通过程序集路径上溯定位。
        /// 规则实现与"哪些文件算组件"的发现逻辑唯一存在于 WidgetSourceAudit，本类不复制任何正则。
        /// </param>
        public static WidgetValidationReport ValidateAllWidgets(string repositoryRoot = null)
        {
            var report = new WidgetValidationReport();

            // 1. 获取所有 BaseFlightWidget 派生具体类 (增强加载容错，抵御 Unity/KSP 程序集不完整问题)
            var baseType = typeof(BaseFlightWidget);
            Assembly asm = baseType.Assembly;
            List<Type> widgetTypes;

            try
            {
                widgetTypes = asm.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    .OrderBy(t => t.Name)
                    .ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                widgetTypes = ex.Types
                    .Where(t => t != null && t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    .OrderBy(t => t.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                widgetTypes = new List<Type>();
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = "AssemblyReflection",
                    RuleCode = Rule_Inheritance,
                    Severity = "WARNING",
                    Description = $"反射读取组件类型异常: {ex.Message}",
                    Location = asm.FullName
                });
            }

            report.ReflectionWidgetsAudited = widgetTypes.Count;

            foreach (var type in widgetTypes)
            {
                ValidateWidgetType(type, report);
            }

            // 2. 源码级规则静态扫描（与无头验证器共用同一内核，杜绝副本漂移）
            if (string.IsNullOrEmpty(repositoryRoot))
            {
                repositoryRoot = WidgetSourceAudit.ResolveRepositoryRoot();
            }

            var componentFiles = WidgetSourceAudit.DiscoverComponentFiles(repositoryRoot);
            report.SourceWidgetsAudited = componentFiles.Count;

            if (componentFiles.Count > 0)
            {
                var sourceReport = WidgetSourceAudit.Scan(componentFiles);
                report.TotalChecksPerformed += sourceReport.WidgetsScanned * 7;

                foreach (var v in sourceReport.Violations)
                {
                    report.Violations.Add(new WidgetViolation
                    {
                        WidgetName = v.FileName,
                        RuleCode = v.RuleCode,
                        Severity = v.Severity,
                        Description = v.Description,
                        Location = v.Location
                    });
                }
            }

            return report;
        }

        private static void ValidateWidgetType(Type type, WidgetValidationReport report)
        {
            string name = type.Name;

            // 规则 1: 必须继承 BaseFlightWidget 并实现 OnInitialize(WidgetConfig, ThemeConfig)
            report.TotalChecksPerformed++;
            if (!typeof(BaseFlightWidget).IsAssignableFrom(type))
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_Inheritance,
                    Severity = "ERROR",
                    Description = "组件未继承 BaseFlightWidget 统一基类",
                    Location = type.FullName
                });
            }

            report.TotalChecksPerformed++;
            var initMethod = type.GetMethod("OnInitialize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(WidgetConfig), typeof(ThemeConfig) }, null);
            if (initMethod == null || initMethod.DeclaringType == typeof(BaseFlightWidget))
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_Inheritance,
                    Severity = "ERROR",
                    Description = "组件未重写 protected override void OnInitialize(WidgetConfig, ThemeConfig) 装配方法",
                    Location = $"{type.Name}.OnInitialize"
                });
            }

            // 规则 2: 必须显式声明/重写 RefreshTier (与源码规范对齐，缺失即判 ERROR)
            report.TotalChecksPerformed++;
            var tierProp = type.GetProperty("RefreshTier", BindingFlags.Public | BindingFlags.Instance);
            if (tierProp == null || tierProp.DeclaringType == typeof(BaseFlightWidget))
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_RefreshTier,
                    Severity = "ERROR",
                    Description = "组件未显式重写 RefreshTier 阶梯 (必须声明 Critical / Standard / Relaxed / UltraLow)",
                    Location = $"{type.Name}.RefreshTier"
                });
            }

            // 规则 3: 必须重写 ApplyTheme(ThemeConfig) 接入样式管道
            report.TotalChecksPerformed++;
            var applyThemeMethod = type.GetMethod("ApplyTheme", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(ThemeConfig) }, null);
            if (applyThemeMethod == null || applyThemeMethod.DeclaringType == typeof(BaseFlightWidget) || !applyThemeMethod.IsVirtual || applyThemeMethod.GetBaseDefinition().DeclaringType != typeof(BaseFlightWidget))
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_SemanticTheming,
                    Severity = "ERROR",
                    Description = "组件未实现或未正确 override ApplyTheme(ThemeConfig) 语义主题着色方法",
                    Location = $"{type.Name}.ApplyTheme"
                });
            }

            // 规则 4: 遥测解耦规则 - 必须重写 OnUpdateTelemetry(IFlightTelemetry) 且不得私自持有 Vessel/Part 强引用
            report.TotalChecksPerformed++;
            var updateTelemetryMethod = type.GetMethod("OnUpdateTelemetry", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(IFlightTelemetry) }, null);
            if (updateTelemetryMethod == null || updateTelemetryMethod.DeclaringType == typeof(BaseFlightWidget) || !updateTelemetryMethod.IsVirtual || updateTelemetryMethod.GetBaseDefinition().DeclaringType != typeof(BaseFlightWidget))
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_TelemetryContract,
                    Severity = "ERROR",
                    Description = "组件未实现或未正确 override OnUpdateTelemetry(IFlightTelemetry) 遥测驱动接口",
                    Location = $"{type.Name}.OnUpdateTelemetry"
                });
            }

            // 检查组件是否直接持有名为 Vessel、Part、CelestialBody 的字段（破坏解耦，导致场景切换泄漏）
            report.TotalChecksPerformed++;
            var rogueFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(f => f.FieldType.Name == "Vessel" || f.FieldType.Name == "Part" || f.FieldType.Name == "CelestialBody")
                .ToList();
            foreach (var f in rogueFields)
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_TelemetryContract,
                    Severity = "ERROR",
                    Description = $"组件直接持有 '{f.FieldType.Name}' 强引用字段 '{f.Name}'，破坏遥测契约解耦，易导致跨场景切换内存泄漏。必须统一由 IFlightTelemetry 提供数据",
                    Location = $"{type.Name}.{f.Name}"
                });
            }

            // 规则 5: 安全生命周期检查 - 检查是否存在非 override 的 OnDestroy 与 Update
            report.TotalChecksPerformed++;
            var onDestroyMethod = type.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (onDestroyMethod != null && onDestroyMethod.DeclaringType == type)
            {
                var baseDefinition = onDestroyMethod.GetBaseDefinition();
                if (baseDefinition.DeclaringType != typeof(BaseFlightWidget) || !onDestroyMethod.IsVirtual)
                {
                    report.Violations.Add(new WidgetViolation
                    {
                        WidgetName = name,
                        RuleCode = Rule_SafeLifecycle,
                        Severity = "ERROR",
                        Description = "组件私自定义了 OnDestroy() 且未 override 基类，会导致基类销毁清理被阻断 (引发 CS0114 警告及内存泄漏)",
                        Location = $"{type.Name}.OnDestroy"
                    });
                }
            }

            report.TotalChecksPerformed++;
            var updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (updateMethod != null && updateMethod.DeclaringType == type)
            {
                var baseDefinition = updateMethod.GetBaseDefinition();
                if (baseDefinition.DeclaringType != typeof(BaseFlightWidget) || !updateMethod.IsVirtual)
                {
                    report.Violations.Add(new WidgetViolation
                    {
                        WidgetName = name,
                        RuleCode = Rule_SafeLifecycle,
                        Severity = "ERROR",
                        Description = "组件私自定义了 Update() 且未 override 基类，会绕过基类分频调度机制 (引发 CS0114 警告及每帧无效计算)",
                        Location = $"{type.Name}.Update"
                    });
                }
            }

            // 规则 6: 零颜色字面量反射兜底 - 检查是否定义了静态硬编码 Color/Color32 字段
            report.TotalChecksPerformed++;
            var staticColorFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Color) || f.FieldType == typeof(Color32))
                .ToList();
            foreach (var f in staticColorFields)
            {
                report.Violations.Add(new WidgetViolation
                {
                    WidgetName = name,
                    RuleCode = Rule_NoHardcodedColors,
                    Severity = "ERROR",
                    Description = $"组件声明了静态硬编码颜色字段 '{f.Name}'，违背零颜色字面量铁律，必须改走 WidgetStyleManager 语义角色或 ThemeConfig",
                    Location = $"{type.Name}.{f.Name}"
                });
            }

            // 规则 7: 零场景查询 (反射计数对齐，真实扫描在源码阶段执行)
            report.TotalChecksPerformed++;
        }
    }
}
