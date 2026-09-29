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
        public const string HeartBeatTier = "MFP-SPEC-002B";     // 心跳频率不得高于刷新率 (HeartBeatTier >= RefreshTier)
        public const string SemanticTheming = "MFP-SPEC-003";    // 必须实现 ApplyTheme(ThemeConfig)
        public const string TelemetryContract = "MFP-SPEC-004";  // 必须实现 OnUpdateTelemetry(IFlightTelemetry)
        public const string DataHeartBeatContract = "MFP-SPEC-004C"; // 必须在组件内显式重写 OnDataHeartBeat(in FlightHeartbeatContext)
        public const string UIDrawLoopContract = "MFP-SPEC-004D";    // 必须在组件内显式重写 OnUIDrawLoop(ref FlightUIDrawContext)
        public const string SafeLifecycle = "MFP-SPEC-005";      // OnDestroy 必须 override
        public const string NoHardcodedColors = "MFP-SPEC-006";  // 禁止颜色字面量（零容忍）
        public const string NoSceneQueries = "MFP-SPEC-007";     // 禁止组件内场景查询，统一走 ProbeManager
        public const string AutoRegistration = "MFP-SPEC-008";   // 必须声明 [FlightWidget] 自动注册与预设库元数据
        public const string WidgetPrivateCacheContract = "MFP-SPEC-009"; // 必须声明或使用智能私有缓存与死区脏检查 (Cached<T> / CachedFloat / CachedDouble 等)
        public const string HotLoopUnguardedOperation = "MFP-SPEC-010";   // 高频生命周期禁止无守卫堆分配、字符串插值与 UGUI 几何写入
        public const string TelemetryAssemblyWarning = "MFP-WARN-TELEM-ASSEMBLY"; // 微控件未支持标准化遥测装配警告

        public const int RuleCount = 13;

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

        /// <summary>数据心跳阶梯属性名</summary>
        public const string HeartBeatTierProperty = "HeartBeatTier";

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

        /// <summary>数据心跳方法名与参数类型</summary>
        public const string DataHeartBeatMethod = "OnDataHeartBeat";
        public const string DataHeartBeatParameterType = "FlightHeartbeatContext";

        /// <summary>UI绘制循环方法名与参数类型</summary>
        public const string UIDrawLoopMethod = "OnUIDrawLoop";
        public const string UIDrawLoopParameterType = "FlightUIDrawContext";

        /// <summary>生命周期方法名（override 且必须回链 base）</summary>
        public const string LifecycleMethod = "OnDestroy";

        /// <summary>
        /// SPEC-009 智能私有缓存与死区脏检查合法类型清单（声明为字段或显式实例化调用）：
        /// Cached<T> / CachedFloat / CachedDouble (全托管类，支持弱引用与反射全自动重置)
        /// DirtyField<T> / DirtyFloat / DirtyDouble (零 GC 局部值死区脏检查结构体)
        /// </summary>
        public static readonly IReadOnlyList<string> ValidCacheTypes = Array.AsReadOnly(new[]
        {
            "Cached",
            "CachedFloat",
            "CachedDouble",
            "DirtyField",
            "DirtyFloat",
            "DirtyDouble"
        });

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

        public static readonly IReadOnlyList<SceneQueryApiRule> SceneQueryApis = Array.AsReadOnly(new[]
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
        });

        /// <summary>
        /// 禁止 using static 的 owner 类型：一旦引入，裸名 Find("x") / main 会绕过上面整张表。
        /// 与 SceneQueryApis 的 Owner 集合保持同源，新增 API 家族时只需维护上表。
        /// </summary>
        public static readonly IReadOnlyList<string> SceneQueryStaticImportOwners = Array.AsReadOnly(new[] { "GameObject", "Object", "Resources", "Camera" });

        /// <summary>
        /// 显式豁免的调用前缀：实例子物体查找与项目内的探针门面（ProbeManager）属于合规通道。
        /// </summary>
        public static readonly IReadOnlyList<string> SceneQueryExemptPrefixes = Array.AsReadOnly(new[] { "transform.Find", "Probe." });

        /// <summary>
        /// 判定一个调用/成员访问表达式是否命中场景查询 API 表。
        /// expression 形如 "Camera.main" / "UnityEngine.Object.FindObjectsByType<Camera>" / "Find("HUD")"。
        /// </summary>
        public static bool IsSceneQueryApi(string expression, bool isInvocation)
        {
            if (string.IsNullOrEmpty(expression)) return false;

            for (int i = 0; i < SceneQueryExemptPrefixes.Count; i++)
            {
                if (expression.StartsWith(SceneQueryExemptPrefixes[i], StringComparison.Ordinal)) return false;
            }

            string[] parts = expression.Split('.');
            string member = parts[parts.Length - 1];
            string owner = parts.Length >= 2 ? parts[parts.Length - 2] : null;

            for (int i = 0; i < SceneQueryApis.Count; i++)
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
            for (int i = 0; i < SceneQueryStaticImportOwners.Count; i++)
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
        public static readonly IReadOnlyList<string> BannedFormattingMethods = Array.AsReadOnly(new[]
        {
            "FormatDuration",
            "FormatDurationCompact",
            "FormatTimeCompact",
            "FormatSeconds",
            "FormatDistanceMetric",
            "FormatDistanceKm"
        });

        /// <summary>
        /// 推荐的标准化基类通道检索 API 集合
        /// </summary>
        public static readonly IReadOnlyList<string> StandardTemplateChannelApis = Array.AsReadOnly(new[]
        {
            "GetTemplateChannel",
            "GetTemplateChannelDouble",
            "GetTemplateChannelFloat",
            "GetTemplateChannelInt",
            "GetTemplateChannelBool"
        });

        /// <summary>
        /// 推荐的统一格式化套件类名与方法名 (AvionicsFormatting 与 AvionicsFastFormat 零 GC 快速常量池)
        /// </summary>
        public const string StandardFormattingClass = "AvionicsFormatting";
        public const string FastFormatClass = "AvionicsFastFormat";
        public static readonly IReadOnlyList<string> StandardFormattingClasses = Array.AsReadOnly(new[] { "AvionicsFormatting", "AvionicsFastFormat" });
        public static readonly IReadOnlyList<string> StandardFormattingApis = Array.AsReadOnly(new[]
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
        });

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
        public static readonly IReadOnlyList<string> SmartUIExtensionApis = Array.AsReadOnly(new[]
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
        });

        /// <summary>
        /// 禁止在具体组件帧循环内调用的集中式基础设施 API (应交由 FlightHUDManager.LateUpdateSync 集中调度)
        /// </summary>
        public const string BannedWidgetDockSyncApi = "DockAnchorTracker.SyncAll";

        // ==========================================================================================
        // 航电效能与反模式规则：高频生命周期方法与堆分配/裸 UGUI 逃逸
        // ==========================================================================================
        public static readonly IReadOnlyList<string> HotLoopMethodNames = Array.AsReadOnly(new[] { "LateUpdate", "Update", "OnUIDrawLoop", "OnUpdateTelemetry", "FixedUpdate" });
        public static readonly IReadOnlyList<string> HotLoopUguiProperties = Array.AsReadOnly(new[] { "anchoredPosition", "localScale", "color", "sizeDelta" });

        /// <summary>
        /// 触发 UGUI Canvas 网格或布局全量重建的高危 API（反模式：禁止在高频生命周期热路径内无节制调用）
        /// </summary>
        public static readonly IReadOnlyList<string> HotLoopMeshRebuildApis = Array.AsReadOnly(new[]
        {
            "SetVerticesDirty",
            "SetAllDirty",
            "SetLayoutDirty",
            "SetMaterialDirty",
            "MarkLayoutForRebuild"
        });

        // ==========================================================================================
        // 契约成员名：反射级校验器与源码级审计器共用的唯一名字来源。
        // 别名常量（Rule_*）已在 WidgetSpecificationValidator 里声明，那里禁止再写裸字面量。
        // ==========================================================================================

        /// <summary>装配入口方法名（反射级 SPEC-001 判定必须由本类重写）</summary>
        public const string InitializeMethod = "OnInitialize";

        /// <summary>分频调度入口方法名（反射级 SPEC-005 判定不得被非 override 隐藏）</summary>
        public const string FrameUpdateMethod = "Update";

        /// <summary>禁止组件直接持有的引擎强引用类型名（破坏 IFlightTelemetry 解耦，跨场景切换易泄漏）</summary>
        public static readonly IReadOnlyList<string> BannedStrongReferenceTypes = Array.AsReadOnly(new[] { "Vessel", "Part", "CelestialBody" });

        // ==========================================================================================
        // 微控件现代化（SPEC-002 效能 / 反模式治理）判定锚点
        // ==========================================================================================

        /// <summary>声明式尺寸契约属性名（现代化判定要求 override）</summary>
        public const string BaseSizeProperty = "BaseSize";

        /// <summary>自动卡片底板契约属性名</summary>
        public const string AutoCardFrameProperty = "AutoCreateCardFrame";

        /// <summary>微控件 DSL 类型名集合（唯一数据源：新增 DSL 控件只需在此登记）</summary>
        public static readonly IReadOnlyList<string> MicroControlDslTypes = Array.AsReadOnly(new[]
        {
            "TextWidget", "GaugeWidget", "LinearBarWidget", "TapeWidget",
            "StateWidget", "IconWidget", "ToggleButtonWidget", "ActionButtonWidget"
        });

        /// <summary>承载遥测 Token 形参的 DSL 控件类型（遥测装配倒查的判定范围）</summary>
        public static readonly IReadOnlyList<string> TelemetryTokenDslTypes = Array.AsReadOnly(new[] { "TextWidget", "LinearBarWidget" });

        /// <summary>
        /// 微控件 DSL "工厂调用"识别的类型前缀集合（Controls.Add / XxxWidget.Method(...) 形态）。
        ///
        /// 【已知缺口，勿在重构中静默放宽】当前只覆盖 TextWidget / LinearBarWidget 两个类型，
        /// 与旧实现口径一致。其余 6 个 DSL 类型（GaugeWidget / TapeWidget / StateWidget /
        /// IconWidget / ToggleButtonWidget / ActionButtonWidget）的工厂调用尚未纳入识别，
        /// 因此"只在方法体里用工厂方法、没有声明 DSL 字段"的组件可能被判为 LegacyImperative。
        /// 放宽识别范围会改变现有现代化统计结果，属于独立决策，不在常量回收范围内。
        /// </summary>
        public static readonly IReadOnlyList<string> DslFactoryInvocationTypes = Array.AsReadOnly(new[] { "TextWidget", "LinearBarWidget" });

        /// <summary>DSL 控件承载 Token 的工厂方法后缀（TextWidget.Value(...) / LinearBarWidget.BottomBar(...)）</summary>
        public static readonly IReadOnlyList<string> DslTokenFactorySuffixes = Array.AsReadOnly(new[] { ".Value", ".BottomBar" });

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
        public static readonly IReadOnlyList<string> DirtyTrackingFieldSuffixes = Array.AsReadOnly(new[] { "Text", "Str", "Val" });

        /// <summary>残留手工脏标记前缀集合：检测未纳管私有状态</summary>
        public static readonly IReadOnlyList<string> ResidualDirtyFieldPrefixes = Array.AsReadOnly(new[] { "_last", "_prev", "_dirty" });

        /// <summary>
        /// 判定字段名是否属于手工脏检查残留字段（例如 _lastPitch, _prevAlt, _dirtyText 等）
        /// </summary>
        public static bool IsResidualDirtyField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return false;
            for (int i = 0; i < ResidualDirtyFieldPrefixes.Count; i++)
            {
                if (fieldName.StartsWith(ResidualDirtyFieldPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

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

        public static readonly IReadOnlyList<string> AuditedControlTypes = Array.AsReadOnly(new[]
        {
            "WidgetReadoutControl",
            "WidgetLinearBarControl"
        });

        /// <summary>构造函数形状表（唯一数据源；与源码声明不一致时门禁报错）</summary>
        public static readonly IReadOnlyList<ControlCtorShape> ControlCtorShapes = Array.AsReadOnly(new[]
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
        });

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
            for (int i = 0; i < TelemetryTokenDslTypes.Count; i++)
            {
                if (string.Equals(TelemetryTokenDslTypes[i], simpleTypeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>该调用表达式是否为 DSL 的 Token 承载工厂方法（TextWidget.Value / LinearBarWidget.BottomBar）</summary>
        public static bool IsDslTokenFactory(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;
            for (int i = 0; i < DslTokenFactorySuffixes.Count; i++)
            {
                if (expression.EndsWith(DslTokenFactorySuffixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsAuditedControlType(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return false;
            for (int i = 0; i < AuditedControlTypes.Count; i++)
            {
                if (string.Equals(AuditedControlTypes[i], simpleTypeName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>按"类型名 + 实参个数"查找构造函数形状；未登记的重载返回 null（调用方必须显式上报，不得静默放过）</summary>
        public static ControlCtorShape FindControlCtorShape(string simpleTypeName, int argumentCount)
        {
            if (string.IsNullOrEmpty(simpleTypeName)) return null;
            for (int i = 0; i < ControlCtorShapes.Count; i++)
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
            for (int i = 0; i < ControlCtorShapes.Count; i++)
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
        public static readonly IReadOnlyList<string> BuildArtifactPathFragments = Array.AsReadOnly(new[] { "/obj/", "/bin/" });

        /// <summary>程序集路径向上探测的最大层数</summary>
        public const int RepositoryRootProbeDepth = 10;

        // ==========================================================================================
        // 审计解析/编译口径：唯一声明点
        // ==========================================================================================

        /// <summary>
        /// 审计侧统一启用的预编译符号。插件本体（ModularFlightPanel.csproj 的 DefineConstants）
        /// 定义了它，因此两条解析路径（语义编译树 / 语法回退树）必须取同一值。
        ///
        /// 【为什么是单点】历史缺陷：语义编译漏了本符号而语法回退带上了它，三处口径两套语义，后果有二 ——
        ///   1) `#if !KSP_RUNTIME` 包围的 Vector2 测试垫片（Config/WidgetConfig.cs）被单侧激活，
        ///      与 UnityEngine.CoreModule.dll 的真实 Vector2 冲突（实测 1690 处 CS0029）；
        ///   2) `#if KSP_RUNTIME` 内的代码在语义树上凭空消失，而 WidgetClassGraph 是"语义树优先"，
        ///      于是组件内受宏保护的代码整段脱审。
        /// </summary>
        public const string RuntimePreprocessorSymbol = "KSP_RUNTIME";

        /// <summary>
        /// 只由无头验证器编译、不参与插件本体编译的审计文件（与 ModularFlightPanel.csproj 的
        /// &lt;Compile Remove&gt; 逐项对应）。
        ///
        /// 语义编译集必须与插件真实编译集一致：这些文件引用 Microsoft.CodeAnalysis，
        /// 而 KSP Managed 目录不提供该程序集，混入后会退化为错误类型并污染整张类型图。
        /// </summary>
        public static readonly IReadOnlyList<string> PluginExcludedAuditFiles = Array.AsReadOnly(new[]
        {
            "RoslynAstHelper.cs",
            "SemanticCompilationProvider.cs",
            "WidgetSourceAudit.cs",
            "WidgetModernizationAudit.cs",
            "WidgetColorLiteralAudit.cs",
            "WidgetFieldPenetrationAudit.cs",
            "I18nSyntaxAuditor.cs"
        });

        /// <summary>插件本体的项目文件（用于校验 PluginExcludedAuditFiles 与 csproj 不漂移）</summary>
        public const string PluginCsprojRelativePath = "src/ModularFlightPanel/ModularFlightPanel.csproj";

        /// <summary>按文件名判定是否属于"仅无头侧编译"的审计文件</summary>
        public static bool IsPluginExcludedAuditFile(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return false;

            string name = pathOrName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);

            for (int i = 0; i < PluginExcludedAuditFiles.Count; i++)
            {
                if (string.Equals(PluginExcludedAuditFiles[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ==========================================================================================
        // 语义编译健康度：错误数棘轮（只降不升）
        // ==========================================================================================

        /// <summary>内核级规则码：语义编译存在诊断错误 —— 符号决议可能落在错误类型上，L4 权威性不成立</summary>
        public const string KernelSemanticUnhealthy = "MFP-KERNEL-SEMANTIC-UNHEALTHY";

        /// <summary>内核级规则码：语义层降级 —— 未链接到 Unity/KSP 程序集，判定已退化为语法回退</summary>
        public const string KernelSemanticDegraded = "MFP-KERNEL-SEMANTIC-DEGRADED";

        /// <summary>
        /// 全量语义编译允许的诊断错误数上限（棘轮：只允许下调，与颜色基线同规矩）。
        ///
        /// 【当前值 0 的来历】修复"语义编译缺 KSP_RUNTIME + 编译集混入仅无头侧编译的审计文件"之前，
        /// 实测为 2217（其中 CS0029 1690 处全部是影子 UnityEngine.Vector2 与 CoreModule 真实类型的冲突）。
        /// 两处修复后实测为 0，故钉死在 0 —— 任何新增编译错误都必须先讨论再抬高本值。
        /// </summary>
        public const int SemanticCompilationErrorCeiling = 0;

        // ==========================================================================================
        // 语义编译集一致性守卫：两份清单（本文件 vs 插件 csproj）必须逐项相等
        // ==========================================================================================

        /// <summary>
        /// 从插件 csproj 文本中解析 &lt;Compile Remove="..."&gt; 的文件名集合。
        /// 供自检断言"语义编译集 == 插件真实编译集"，杜绝 C# 清单与 csproj 清单各自漂移。
        /// </summary>
        public static List<string> ParseCompileRemoveFileNames(string csprojText)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(csprojText)) return names;

            const string tag = "Compile";
            const string remove = "Remove";

            int cursor = 0;
            while (true)
            {
                int tagAt = csprojText.IndexOf("<" + tag, cursor, StringComparison.OrdinalIgnoreCase);
                if (tagAt < 0) break;
                cursor = tagAt + 1;

                int tagEnd = csprojText.IndexOf('>', tagAt);
                if (tagEnd < 0) break;
                string element = csprojText.Substring(tagAt, tagEnd - tagAt);

                int removeAt = element.IndexOf(remove, StringComparison.OrdinalIgnoreCase);
                if (removeAt < 0) continue;

                int quoteStart = element.IndexOf('"', removeAt);
                if (quoteStart < 0) continue;
                int quoteEnd = element.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) continue;

                string raw = element.Substring(quoteStart + 1, quoteEnd - quoteStart - 1)
                                    .Replace('\\', '/');
                int slash = raw.LastIndexOf('/');
                if (slash >= 0) raw = raw.Substring(slash + 1);
                raw = raw.Trim();

                if (!string.IsNullOrEmpty(raw) && !names.Contains(raw)) names.Add(raw);
            }
            return names;
        }
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

        /// <summary>
        /// 本次审计中语义符号决议被采用（咨询）的次数。
        /// 覆盖率指标，不是结果指标：语义结论与文本回退结论可能恰好相同，
        /// 因此"结果对不对"无法证明语义点位还在，"被采用过"才可以。
        /// 为 0 即表示 L4 语义层已从判定链路中静默消失。
        /// </summary>
        public int SemanticVerdictCount;

        /// <summary>
        /// 语义决议在"调用表达式"分支被采用的次数（SPEC-007 的 InvocationExpression 遍历）。
        /// 与 MemberAccess 分支分开计数：两个分支能捕获同一处违规且会被去重合并，
        /// 所以合并计数无法证明"两个分支都还在"。
        /// </summary>
        public int SemanticInvocationVerdictCount;

        /// <summary>语义决议在"成员访问"分支被采用的次数（SPEC-007 的 MemberAccessExpression 遍历）</summary>
        public int SemanticMemberAccessVerdictCount;

        public int ErrorCount => Violations.Count(v => v.Severity == "ERROR");
        public int WarningCount => Violations.Count(v => v.Severity == "WARNING");
        public bool IsCompliant => ErrorCount == 0;

        public int CountByRule(string ruleCode) =>
            Violations.Count(v => v.RuleCode == ruleCode);
    }
}
