using System;
using System.Collections.Generic;
using System.Linq;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// MFP 组件规范规则码与判定数据 —— 全项目单一声明点。
    ///
    /// 【设计约束】本文件是唯一允许写死"契约名 / 黑名单 API / 元数据名"的地方：
    ///   · 规则实现（WidgetSourceAudit / WidgetColorLiteralAudit / WidgetModernizationAudit）
    ///     一律引用此处常量与表格，禁止在规则体里再写字符串或按文件名/类名做特判；
    ///   · 本文件同时被插件（net472，KSP_RUNTIME）编译，因此不得引用 Roslyn 类型。
    /// </summary>
    public static class WidgetSpecRules
    {
        public const string Inheritance = "MFP-SPEC-001";        // 必须继承 BaseFlightWidget
        public const string RefreshTier = "MFP-SPEC-002";        // 必须显式声明刷新阶梯
        public const string SemanticTheming = "MFP-SPEC-003";    // 必须实现 ApplyTheme(ThemeConfig)
        public const string TelemetryContract = "MFP-SPEC-004";  // 必须实现 OnUpdateTelemetry(IFlightTelemetry)
        public const string SafeLifecycle = "MFP-SPEC-005";      // OnDestroy 必须 override
        public const string NoHardcodedColors = "MFP-SPEC-006";  // 禁止颜色字面量（零容忍）
        public const string NoSceneQueries = "MFP-SPEC-007";     // 禁止组件内场景查询，统一走 ProbeManager
        public const string AutoRegistration = "MFP-SPEC-008";   // 必须声明 [FlightWidget] 自动注册与预设库元数据

        public const int RuleCount = 8;

        /// <summary>审计内核自身问题（发现层失效 / 判定依据缺失）的统一报告名</summary>
        public const string KernelReportName = "AuditKernel";

        // ==========================================================================================
        // 契约锚点：全部结构性判定只认这些名字，规则体里不再出现裸字符串
        // ==========================================================================================

        /// <summary>组件继承契约根：所有组件必须（直接或间接）派生自它</summary>
        public const string ContractRootType = "BaseFlightWidget";

        /// <summary>核心 3D 渲染组件基类（现代化审计据此判定 Core3D，替代旧的文件名包含判断）</summary>
        public const string Core3DBaseType = "BaseNavballSphereWidget";

        /// <summary>组件声明式元数据特性（SPEC-008 / 自动注册）</summary>
        public const string MetadataAttribute = "FlightWidget";

        /// <summary>向后兼容垫片标记：继承链上出现即视为兼容垫片，不参与重新实现契约的判定</summary>
        public const string CompatibilityShimAttribute = "Obsolete";

        /// <summary>声明式满帧标记：写在 [FlightWidget(..., HighFrequency = true)] 上，是"满帧阶梯合法"的唯一依据</summary>
        public const string HighFrequencyMetadata = "HighFrequency";

        /// <summary>刷新阶梯属性名</summary>
        public const string TierProperty = "RefreshTier";

        /// <summary>刷新阶梯枚举类型名（阶梯合法取值集合直接从源码里的枚举声明派生，不再写死 4 个字面量）</summary>
        public const string TierEnumType = "WidgetRefreshTier";

        /// <summary>满帧阶梯成员名（语义锚点：仅用于"满帧需 HighFrequency 声明"这条判定）</summary>
        public const string FullFrameTierMember = "Critical";

        /// <summary>语义主题方法名与唯一合法参数类型</summary>
        public const string ThemeMethod = "ApplyTheme";
        public const string ThemeParameterType = "ThemeConfig";

        /// <summary>遥测契约方法名与唯一合法参数类型</summary>
        public const string TelemetryMethod = "OnUpdateTelemetry";
        public const string TelemetryParameterType = "IFlightTelemetry";

        /// <summary>生命周期方法名（override 且必须回链 base）</summary>
        public const string LifecycleMethod = "OnDestroy";

        // ==========================================================================================
        // SPEC-007 场景查询 API 表：唯一数据源，扫描与自检共用同一份判定
        // ==========================================================================================

        /// <summary>
        /// 单条场景查询 API 规则。
        /// Owner 为 null 表示"任意限定者"（Find* 家族在 Unity 中均为静态，裸名或点号限定都必须拦截）。
        /// </summary>
        public sealed class SceneQueryApiRule
        {
            public string Owner;
            public string Member;
            public bool MemberIsPrefix;
            public bool IsInvocation;

            public SceneQueryApiRule(string owner, string member, bool isInvocation, bool memberIsPrefix)
            {
                Owner = owner;
                Member = member;
                IsInvocation = isInvocation;
                MemberIsPrefix = memberIsPrefix;
            }
        }

        public static readonly SceneQueryApiRule[] SceneQueryApis =
        {
            // UnityEngine.Object 家族（含 Camera / GameObject / Component 等全部派生类的静态查找）
            new SceneQueryApiRule(null, "FindObjectOfType", true, true),
            new SceneQueryApiRule(null, "FindObjectsOfType", true, true),
            new SceneQueryApiRule(null, "FindFirstObjectByType", true, true),
            new SceneQueryApiRule(null, "FindAnyObjectByType", true, true),
            new SceneQueryApiRule(null, "FindObjectsByType", true, true),
            // 场景层级枚举
            new SceneQueryApiRule(null, "GetRootGameObjects", true, true),
            // GameObject.Find 家族（Find / FindWithTag / FindGameObjectWithTag / FindGameObjectsWithTag）
            new SceneQueryApiRule("GameObject", "Find", true, true),
            // Resources 全量扫描
            new SceneQueryApiRule("Resources", "FindObjectsOfTypeAll", true, true),
            // Camera 静态查询（旧实现只拦 main/allCameras，current 与 GetAllCameras 是同类漏检）
            new SceneQueryApiRule("Camera", "GetAllCameras", true, false),
            new SceneQueryApiRule("Camera", "main", false, false),
            new SceneQueryApiRule("Camera", "current", false, false),
            new SceneQueryApiRule("Camera", "allCameras", false, false),
            new SceneQueryApiRule("Camera", "allCamerasCount", false, false)
        };

        /// <summary>
        /// 禁止 using static 的 owner 类型：一旦引入，裸名 Find("x") / main 会绕过上面整张表。
        /// 与 SceneQueryApis 的 Owner 集合保持同源，新增 API 家族时只需维护上表。
        /// </summary>
        public static readonly string[] SceneQueryStaticImportOwners = { "GameObject", "Object", "Resources", "Camera" };

        /// <summary>
        /// 显式豁免的调用前缀：实例子物体查找与项目内的探针门面（ProbeManager）属于合规通道。
        /// </summary>
        public static readonly string[] SceneQueryExemptPrefixes = { "transform.Find", "Probe." };

        /// <summary>
        /// 判定一个调用/成员访问表达式是否命中场景查询 API 表。
        /// expression 形如 "Camera.main" / "UnityEngine.Object.FindObjectsByType&lt;Camera&gt;" / "Find("HUD")"。
        /// </summary>
        public static bool IsSceneQueryApi(string expression, bool isInvocation)
        {
            if (string.IsNullOrEmpty(expression)) return false;

            for (int i = 0; i < SceneQueryExemptPrefixes.Length; i++)
            {
                if (expression.StartsWith(SceneQueryExemptPrefixes[i], StringComparison.Ordinal)) return false;
            }

            string[] parts = expression.Split('.');
            string member = parts[parts.Length - 1];
            string owner = parts.Length >= 2 ? parts[parts.Length - 2] : null;

            for (int i = 0; i < SceneQueryApis.Length; i++)
            {
                SceneQueryApiRule rule = SceneQueryApis[i];
                if (rule.IsInvocation != isInvocation) continue;

                bool memberOk = rule.MemberIsPrefix
                    ? member.StartsWith(rule.Member, StringComparison.Ordinal)
                    : string.Equals(member, rule.Member, StringComparison.Ordinal);
                if (!memberOk) continue;

                if (rule.Owner == null || string.Equals(owner, rule.Owner, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>判定 using static 的目标类型是否属于场景查询家族（会绕过黑名单表）</summary>
        public static bool IsBannedStaticImportOwner(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return false;
            for (int i = 0; i < SceneQueryStaticImportOwners.Length; i++)
            {
                if (string.Equals(SceneQueryStaticImportOwners[i], simpleTypeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ==========================================================================================
        // SPEC-002 效能红线：CPU 软件光栅化反模式（数据单点声明）
        // ==========================================================================================

        public const string CpuRasterizerApiSuffix = ".SetPixels32";
        public const string CpuRasterizerPixelField = "_texPixels";

        // ==========================================================================================
        // 航电代码质量与反模式规则：单点声明高频样板代码与推荐 API 规范
        // ==========================================================================================

        /// <summary>
        /// 禁止的私有通配符通道解析器方法名（反模式：应统一调用基类 GetTemplateChannel 系列方法）
        /// </summary>
        public const string BannedTemplateParserMethod = "ParseCustomTemplate";

        /// <summary>
        /// 禁止的私有时间/时序/度量格式化方法名（反模式：应统一调用 AvionicsFormatting 或基类 FormatDuration 等方法）
        /// </summary>
        public static readonly string[] BannedFormattingMethods =
        {
            "FormatDuration",
            "FormatDurationCompact",
            "FormatTimeCompact",
            "FormatSeconds",
            "FormatDistanceMetric",
            "FormatDistanceKm"
        };

        /// <summary>
        /// 推荐的标准化基类通道检索 API 集合
        /// </summary>
        public static readonly string[] StandardTemplateChannelApis =
        {
            "GetTemplateChannel",
            "GetTemplateChannelDouble",
            "GetTemplateChannelFloat",
            "GetTemplateChannelInt",
            "GetTemplateChannelBool"
        };

        /// <summary>
        /// 推荐的统一格式化套件类名与方法名 (AvionicsFormatting 与 AvionicsFastFormat 零 GC 快速常量池)
        /// </summary>
        public const string StandardFormattingClass = "AvionicsFormatting";
        public const string FastFormatClass = "AvionicsFastFormat";
        public static readonly string[] StandardFormattingClasses = { "AvionicsFormatting", "AvionicsFastFormat" };
        public static readonly string[] StandardFormattingApis =
        {
            "FormatDuration",
            "FormatDurationCompact",
            "FormatCountdown",
            "FormatMetricDistance",
            "FormatMetricSpeed",
            "FastInt",
            "FastRoundInt",
            "FastPercent",
            "FastTwoDigits"
        };

        /// <summary>
        /// 推荐的统一防抖与阈值评估 API
        /// </summary>
        public const string SetTextIfChangedApi = "SetTextIfChanged";
        public const string SetImageFillIfChangedApi = "SetImageFillIfChanged";
        public const string EvaluateThresholdRoleApi = "EvaluateThresholdRole";

        /// <summary>
        /// 推荐的智能 UI 扩展套件 (SmartUIExtensions 零开销链式脏检查)
        /// </summary>
        public const string SmartUIExtensionsClass = "SmartUIExtensions";
        public static readonly string[] SmartUIExtensionApis =
        {
            "SetTextSafe",
            "SetColor",
            "SetAlpha",
            "SetFillAmountSafe",
            "SetAnchoredPositionSafe",
            "SetSizeDeltaSafe",
            "SetLocalRotationSafe",
            "SetLocalEulerAnglesSafe",
            "SetLocalScaleSafe",
            "SetActiveSafe"
        };

        /// <summary>
        /// 禁止在具体组件帧循环内调用的集中式基础设施 API (应交由 FlightHUDManager.LateUpdateSync 集中调度)
        /// </summary>
        public const string BannedWidgetDockSyncApi = "DockAnchorTracker.SyncAll";

        // ==========================================================================================
        // 航电效能与反模式规则：高频生命周期方法与堆分配/裸 UGUI 逃逸
        // ==========================================================================================
        public static readonly string[] HotLoopMethodNames = { "LateUpdate", "Update", "OnUpdateTelemetry", "FixedUpdate" };
        public static readonly string[] HotLoopUguiProperties = { "anchoredPosition", "localScale", "color" };
    }

    /// <summary>源码级规则违规记录（插件与无头验证器共用的统一 DTO）</summary>
    public class WidgetSourceViolation
    {
        public string FileName;
        public string RuleCode;
        public string Severity;      // "ERROR" / "WARNING"
        public string Description;
        public int Line;             // 1 起行号；0 表示文件级/汇总级

        public string Location => Line > 0 ? $"{FileName}:L{Line}" : FileName;

        public override string ToString()
        {
            return $"[{Severity}] [{RuleCode}] {FileName}: {Description}"
                 + (Line > 0 ? $" (L{Line})" : string.Empty);
        }
    }

    /// <summary>参与审计的组件源文件</summary>
    public class WidgetSourceFile
    {
        public string Name;   // 文件名（含扩展名），用于基线与报错定位
        public string Path;   // 绝对路径
        public string Text;   // 原始源码文本
    }

    /// <summary>组件源码审计汇总报告</summary>
    public class WidgetSourceAuditReport
    {
        public int WidgetsScanned;
        public readonly List<WidgetSourceViolation> Violations = new List<WidgetSourceViolation>();

        public int ErrorCount => Violations.Count(v => v.Severity == "ERROR");
        public int WarningCount => Violations.Count(v => v.Severity == "WARNING");
        public bool IsCompliant => ErrorCount == 0;

        public int CountByRule(string ruleCode) =>
            Violations.Count(v => v.RuleCode == ruleCode);
    }
}
