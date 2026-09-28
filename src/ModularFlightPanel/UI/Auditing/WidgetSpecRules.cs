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
        public const string TelemetryAssemblyWarning = "MFP-WARN-TELEM-ASSEMBLY"; // 微控件未支持标准化遥测装配警告

        public const int RuleCount = 8;

        /// <summary>审计内核自身问题（发现层失效 / 判定依据缺失）的统一报告名</summary>
        public const string KernelReportName = "AuditKernel";

        /// <summary>内核级规则码：继承链消歧失败（同名类多候选，无法唯一确定基类）</summary>
        public const string KernelInheritanceAmbiguity = "MFP-KERNEL-INHERIT-AMBIGUOUS";

        /// <summary>内核级规则码：微控件构造函数签名表与实际源码声明不一致（审计表漂移）</summary>
        public const string KernelContractDrift = "MFP-KERNEL-CONTRACT-DRIFT";

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

        // ==========================================================================================
        // 契约成员名：反射级校验器与源码级审计器共用的唯一名字来源。
        // 别名常量（Rule_*）已在 WidgetSpecificationValidator 里声明，那里禁止再写裸字面量。
        // ==========================================================================================

        /// <summary>装配入口方法名（反射级 SPEC-001 判定必须由本类重写）</summary>
        public const string InitializeMethod = "OnInitialize";

        /// <summary>分频调度入口方法名（反射级 SPEC-005 判定不得被非 override 隐藏）</summary>
        public const string FrameUpdateMethod = "Update";

        /// <summary>禁止组件直接持有的引擎强引用类型名（破坏 IFlightTelemetry 解耦，跨场景切换易泄漏）</summary>
        public static readonly string[] BannedStrongReferenceTypes = { "Vessel", "Part", "CelestialBody" };

        // ==========================================================================================
        // 微控件现代化（SPEC-002 效能 / 反模式治理）判定锚点
        // ==========================================================================================

        /// <summary>声明式尺寸契约属性名（现代化判定要求 override）</summary>
        public const string BaseSizeProperty = "BaseSize";

        /// <summary>自动卡片底板契约属性名</summary>
        public const string AutoCardFrameProperty = "AutoCreateCardFrame";

        /// <summary>微控件 DSL 类型名集合（唯一数据源：新增 DSL 控件只需在此登记）</summary>
        public static readonly string[] MicroControlDslTypes =
        {
            "TextWidget", "GaugeWidget", "LinearBarWidget", "TapeWidget",
            "StateWidget", "IconWidget", "ToggleButtonWidget", "ActionButtonWidget"
        };

        /// <summary>承载遥测 Token 形参的 DSL 控件类型（遥测装配倒查的判定范围）</summary>
        public static readonly string[] TelemetryTokenDslTypes = { "TextWidget", "LinearBarWidget" };

        /// <summary>
        /// 微控件 DSL "工厂调用"识别的类型前缀集合（Controls.Add / XxxWidget.Method(...) 形态）。
        ///
        /// 【已知缺口，勿在重构中静默放宽】当前只覆盖 TextWidget / LinearBarWidget 两个类型，
        /// 与旧实现口径一致。其余 6 个 DSL 类型（GaugeWidget / TapeWidget / StateWidget /
        /// IconWidget / ToggleButtonWidget / ActionButtonWidget）的工厂调用尚未纳入识别，
        /// 因此"只在方法体里用工厂方法、没有声明 DSL 字段"的组件可能被判为 LegacyImperative。
        /// 放宽识别范围会改变现有现代化统计结果，属于独立决策，不在常量回收范围内。
        /// </summary>
        public static readonly string[] DslFactoryInvocationTypes = { "TextWidget", "LinearBarWidget" };

        /// <summary>DSL 控件承载 Token 的工厂方法后缀（TextWidget.Value(...) / LinearBarWidget.BottomBar(...)）</summary>
        public static readonly string[] DslTokenFactorySuffixes = { ".Value", ".BottomBar" };

        /// <summary>微控件注册入口调用前缀（收集式 DSL）</summary>
        public const string ControlsAddPrefix = "Controls.Add";

        /// <summary>DSL 类型名限定前缀（用于 TextWidget.xxx / LinearBarWidget.xxx 调用识别）</summary>
        public const string ControlsRegisterMethod = "Register";

        /// <summary>命令式 UGUI 工厂类型名与调用前缀（未标准化改造的判据）</summary>
        public const string UiFactoryType = "UIFactory";
        public const string UiFactoryCallPrefix = "UIFactory.";

        /// <summary>2D UI Shader 材质管线入口（Core3D 是否已纳管的判据）</summary>
        public const string UiMaterialApi = "GetUiMaterial";

        /// <summary>UGUI 图元类型名片段（用于裸 UGUI 逃逸侦测；按简单类型名 == Image 判定）</summary>
        public const string UguiImageType = "Image";

        /// <summary>裸节点拼装类型名（完整限定名与简单名两种写法都要拦）</summary>
        public const string RawGameObjectType = "GameObject";
        public const string RawGameObjectFullType = "UnityEngine.GameObject";

        /// <summary>手工脏标记字段命名约定：_last* 前缀 + 下列后缀</summary>
        public const string DirtyTrackingFieldPrefix = "_last";
        public static readonly string[] DirtyTrackingFieldSuffixes = { "Text", "Str", "Val" };

        /// <summary>高频方法名判定用的同步入口前缀（Sync* 视为高频受染入口）</summary>
        public const string HotSyncMethodPrefix = "Sync";

        /// <summary>私有模板通配符解析器反模式的切分调用后缀</summary>
        public const string CustomTemplateSplitSuffix = ".Split";

        /// <summary>私有模板通配符解析器的参数字面量片段</summary>
        public const string CustomTemplateNameFragment = "CustomTemplate";

        // ==========================================================================================
        // 微控件构造函数签名表 —— 唯一的"哪一参是遥测 Token"声明点。
        //
        // 【为什么需要它】旧实现把参数下标写死在规则体里（"7 参数版本第 7 参 / 12 参数版本第 10 参"），
        // 这是对另一个文件里构造函数签名的人肉镜像：一旦重载增删或参数顺序变化，
        // 判定会"静默翻转"（把违规放过，或把合规全量误报）。
        //
        // 【如何自证】WidgetSourceAudit.VerifyControlCtorShapes 会从被扫描源码里派生真实构造函数声明，
        // 与本表逐条交叉校验；不一致（缺重载 / 参数名不符 / 出现未登记的带 Token 重载）一律报 ERROR。
        // ==========================================================================================

        /// <summary>遥测 Token 形参名 —— 判定"该重载是否具备 Token 绑定能力"的唯一依据</summary>
        public const string ControlTokenParameterName = "token";

        /// <summary>
        /// 单条微控件构造函数形状。
        /// TokenIndex = -1 表示该重载没有 Token 绑定形参（即"未装配遥测"）。
        /// NameIndex 为报告控件显示名时优先读取的实参下标（即 displayName 形参的位置）。
        /// </summary>
        public sealed class ControlCtorShape
        {
            public readonly string ControlType;
            public readonly int ParameterCount;
            public readonly int TokenIndex;
            public readonly int NameIndex;

            public ControlCtorShape(string controlType, int parameterCount, int tokenIndex, int nameIndex)
            {
                ControlType = controlType;
                ParameterCount = parameterCount;
                TokenIndex = tokenIndex;
                NameIndex = nameIndex;
            }
        }

        /// <summary>被审计的微控件类型名（应与其构造函数形状一并登记）</summary>
        public static readonly string[] AuditedControlTypes =
        {
            "WidgetReadoutControl",
            "WidgetLinearBarControl"
        };

        /// <summary>构造函数形状表（唯一数据源；与源码声明不一致时门禁报错）</summary>
        public static readonly ControlCtorShape[] ControlCtorShapes =
        {
            // WidgetReadoutControl
            // (parent, id, displayName, rootGo, bg, outline, title, value, unit, token, titleStr, unitStr)
            new ControlCtorShape("WidgetReadoutControl", 12, 9, 2),
            // (id, displayName, rootGo, value, label, role, token = null)
            new ControlCtorShape("WidgetReadoutControl", 7, 6, 1),
            // (id, displayName, rootGo, value, unit = null, role = ...)   —— 无 Token 形参
            new ControlCtorShape("WidgetReadoutControl", 6, -1, 1),
            // (value, label, role, displayName = "Readout", description = "") —— 无 Token 形参
            new ControlCtorShape("WidgetReadoutControl", 5, -1, 3),

            // WidgetLinearBarControl
            // (parent, id, displayName, rootGo, track, fill, token, min, max, spanLength, isVertical)
            new ControlCtorShape("WidgetLinearBarControl", 11, 6, 2),
            // (id, displayName, rootGo, fill, track = null, role = ..., isVertical = false) —— 无 Token 形参
            new ControlCtorShape("WidgetLinearBarControl", 7, -1, 1),
            // (fill, track, role = ..., isVertical = false, displayName = "Bar Gauge", description = "")
            new ControlCtorShape("WidgetLinearBarControl", 6, -1, 4)
        };

        /// <summary>控件显示名解析失败时的兜底名（键 = 类型名）</summary>
        public static string DefaultControlDisplayName(string controlType)
        {
            if (controlType == "WidgetReadoutControl") return "ReadoutControl";
            if (controlType == "WidgetLinearBarControl") return "LinearBar";
            return controlType ?? "Control";
        }

        /// <summary>该 DSL 控件类型是否属于"承载遥测 Token"的判定范围</summary>
        public static bool IsTelemetryTokenDslType(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return false;
            for (int i = 0; i < TelemetryTokenDslTypes.Length; i++)
            {
                if (string.Equals(TelemetryTokenDslTypes[i], simpleTypeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>该调用表达式是否为 DSL 的 Token 承载工厂方法（TextWidget.Value / LinearBarWidget.BottomBar）</summary>
        public static bool IsDslTokenFactory(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;
            for (int i = 0; i < DslTokenFactorySuffixes.Length; i++)
            {
                if (expression.EndsWith(DslTokenFactorySuffixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsAuditedControlType(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return false;
            for (int i = 0; i < AuditedControlTypes.Length; i++)
            {
                if (string.Equals(AuditedControlTypes[i], simpleTypeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>按"类型名 + 实参个数"查找构造函数形状；未登记的重载返回 null（调用方必须显式上报，不得静默放过）</summary>
        public static ControlCtorShape FindControlCtorShape(string simpleTypeName, int argumentCount)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return null;
            for (int i = 0; i < ControlCtorShapes.Length; i++)
            {
                ControlCtorShape shape = ControlCtorShapes[i];
                if (shape.ParameterCount == argumentCount &&
                    string.Equals(shape.ControlType, simpleTypeName, StringComparison.Ordinal))
                {
                    return shape;
                }
            }
            return null;
        }

        /// <summary>取某控件类型在表中登记的全部形状</summary>
        public static System.Collections.Generic.List<ControlCtorShape> ShapesOfControlType(string simpleTypeName)
        {
            var list = new System.Collections.Generic.List<ControlCtorShape>();
            if (string.IsNullOrEmpty(simpleTypeName)) return list;
            for (int i = 0; i < ControlCtorShapes.Length; i++)
            {
                if (string.Equals(ControlCtorShapes[i].ControlType, simpleTypeName, StringComparison.Ordinal))
                {
                    list.Add(ControlCtorShapes[i]);
                }
            }
            return list;
        }

        // ==========================================================================================
        // 发现层路径锚点：仓库根定位与源码作用域的唯一声明点
        // ==========================================================================================

        public const string SourceRootFolder = "src";
        public const string PluginProjectFolder = "ModularFlightPanel";

        /// <summary>仓库根探测用的相对探针路径（存在即认定该目录为仓库根）</summary>
        public const string RepositoryRootProbeRelative = "src/ModularFlightPanel/UI/Widgets";

        /// <summary>构建产物目录片段（不参与审计）</summary>
        public static readonly string[] BuildArtifactPathFragments = { "/obj/", "/bin/" };

        /// <summary>程序集路径向上探测的最大层数</summary>
        public const int RepositoryRootProbeDepth = 10;
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
