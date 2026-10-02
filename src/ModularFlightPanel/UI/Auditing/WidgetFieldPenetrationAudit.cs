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
    public enum FieldKind
    {
        ManagedCache,         // 全托管缓存 (Cached<T> / CachedFloat / CachedDouble / DirtyField)
        UiHandle,             // 视觉图元句柄 (Text, Image, Button, RectTransform, UI结构体)
        EventCallback,        // 事件委托与回调 (Action, UnityAction, 按钮代理)
        SnapshotStruct,       // 统一遥测快照结构体 (StageSnapshot, CommNetSnapshot 等零GC结构体)
        ConfigTheme,          // 主题与样式契约 (ThemeConfig, WidgetConfig, StyleRole)
        ConfigToken,          // 初始化静态配置 (Token, Template, 常量, ShaderPropertyID)
        ResidualDirtyField,   // 重点漏网之鱼：伪装为本地缓存的未纳管脏标记 (_last*, _cached*, _prev*, _dirty*, _pending*)
        RawScalarLeak         // 穿透捕获：未纳管的裸私有状态/遥测标量 (float, double, string, bool, Vector, Color 等)
    }

    public class AuditedFieldInfo
    {
        public string FileName { get; set; }
        public string ClassName { get; set; }
        public string FieldName { get; set; }
        public string TypeName { get; set; }
        public int Line { get; set; }
        public FieldKind Kind { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsConst { get; set; }

        /// <summary>语义符号给出的全限定类型名（无语义上下文时为 null）——证明类型决议真的走了 Roslyn 符号，而非字符串</summary>
        public string SemanticTypeFullName { get; set; }

        /// <summary>该字段的归类是否由 IFieldSymbol 语义决议给出；false = 已降级到旧的类型简名匹配</summary>
        public bool ResolvedBySymbol { get; set; }
    }

    public class WidgetFieldPenetrationReport
    {
        public int TotalWidgetsScanned { get; set; }
        public int TotalFieldsScanned { get; set; }
        public int TotalManagedCaches { get; set; }
        public int TotalUiHandles { get; set; }
        public int TotalEventCallbacks { get; set; }
        public int TotalSnapshotStructs { get; set; }
        public int TotalConfigs { get; set; }
        public int TotalResidualLeaks { get; set; }
        public int TotalScalarLeaks { get; set; }

        /// <summary>
        /// 本次扫描是否持有语义编译上下文。
        /// false = 全部字段归类已退化为类型简名匹配，结论强度低于 L4，报告必须显式标注而不是照常给数。
        /// </summary>
        public bool SemanticActive { get; set; }

        /// <summary>
        /// 由 IFieldSymbol 语义决议完成归类的字段数（覆盖率计数）。
        /// 为 0 即代表"穿透性符号判定"这一语义点位已被拆除，本审计退化回字符串流派。
        /// </summary>
        public int SemanticResolvedFieldCount { get; set; }

        public int TotalAllLeaks => TotalResidualLeaks + TotalScalarLeaks;

        public float OverallManagedRatio => (TotalManagedCaches + TotalAllLeaks) > 0
            ? (float)TotalManagedCaches / (TotalManagedCaches + TotalAllLeaks) * 100f
            : 100f;

        public Dictionary<string, int> LeakedTypeHistogram { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public Dictionary<string, List<AuditedFieldInfo>> FieldsByWidget { get; } =
            new Dictionary<string, List<AuditedFieldInfo>>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 全局私有变量穿透审计器 (Penetrating Private Variable & Field Auditor)
    /// 遍历所有飞行仪表组件中的所有私有/受保护字段与数据类型，分类出 UI 图元句柄、全托管缓存与未纳管裸变量。
    /// 并提供一键落地的工业级整改实施意见指南。
    /// </summary>
    public static class WidgetFieldPenetrationAudit
    {
        private static readonly HashSet<string> KnownUiTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Text", "Image", "RawImage", "Button", "Outline", "Shadow", "RectTransform", "Transform",
            "GameObject", "Canvas", "CanvasGroup", "ScrollRect", "Slider", "Toggle", "Dropdown", "InputField",
            "Texture", "Texture2D", "Sprite", "Material", "Font", "Shader", "RenderTexture", "Camera",
            "ContentSizeFitter", "LayoutElement", "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup",
            "ColorBlock", "SpriteState", "Navigation", "AvionicsButtonFeedback"
        };

        public static string UnwrapType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return string.Empty;
            string t = typeName.Trim();
            while (t.EndsWith("[]", StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - 2).Trim();
            }
            if ((t.StartsWith("List<", StringComparison.Ordinal) ||
                 t.StartsWith("IList<", StringComparison.Ordinal) ||
                 t.StartsWith("IEnumerable<", StringComparison.Ordinal) ||
                 t.StartsWith("HashSet<", StringComparison.Ordinal) ||
                 t.StartsWith("Queue<", StringComparison.Ordinal) ||
                 t.StartsWith("Stack<", StringComparison.Ordinal)) && t.EndsWith(">", StringComparison.Ordinal))
            {
                int start = t.IndexOf('<');
                t = t.Substring(start + 1, t.Length - start - 2).Trim();
            }
            if ((t.StartsWith("Dictionary<", StringComparison.Ordinal) ||
                 t.StartsWith("IDictionary<", StringComparison.Ordinal)) && t.EndsWith(">", StringComparison.Ordinal))
            {
                int comma = t.LastIndexOf(',');
                if (comma >= 0)
                {
                    t = t.Substring(comma + 1, t.Length - comma - 2).Trim();
                }
            }
            return t;
        }

        public static FieldKind ClassifyField(string typeName, string fieldName, bool isConst, bool isReadOnly, bool isStatic = false)
        {
            if (isConst || isStatic) return FieldKind.ConfigToken;

            string baseType = UnwrapType(typeName);

            // 1. 全托管缓存
            if (WidgetSpecRules.ValidCacheTypes.Any(t => string.Equals(t, baseType, StringComparison.Ordinal)) ||
                baseType.StartsWith("Cached<", StringComparison.Ordinal) ||
                baseType.StartsWith("DirtyField<", StringComparison.Ordinal))
            {
                return FieldKind.ManagedCache;
            }

            // 2. 视觉 UI 句柄
            if (IsUiHandleName(baseType))
            {
                return FieldKind.UiHandle;
            }

            // 3. 事件委托与回调
            if (baseType.StartsWith("Action", StringComparison.Ordinal) ||
                baseType.StartsWith("Func", StringComparison.Ordinal) ||
                baseType.StartsWith("UnityAction", StringComparison.Ordinal) ||
                baseType.StartsWith("UnityEvent", StringComparison.Ordinal))
            {
                return FieldKind.EventCallback;
            }

            // 4. 零 GC 遥测快照结构体与解耦业务大脑
            if (baseType.EndsWith("Snapshot", StringComparison.Ordinal) ||
                baseType.EndsWith("Logic", StringComparison.Ordinal) ||
                baseType == "IFlightTelemetry" ||
                baseType == "IWidgetLogic")
            {
                return FieldKind.SnapshotStruct;
            }

            // 5. 主题与样式配置及本地调色板缓存
            if (baseType == "ThemeConfig" || baseType == "WidgetConfig" ||
                baseType.EndsWith("Config", StringComparison.Ordinal) ||
                baseType.EndsWith("Settings", StringComparison.Ordinal) ||
                baseType.EndsWith("Role", StringComparison.Ordinal) ||
                baseType.EndsWith("Palette", StringComparison.Ordinal) ||
                baseType == "LineWeight" ||
                ((baseType == "Color" || baseType == "Color32") &&
                 (fieldName.StartsWith("_c", StringComparison.Ordinal) ||
                  fieldName.EndsWith("Color", StringComparison.OrdinalIgnoreCase) ||
                  fieldName.EndsWith("Col", StringComparison.OrdinalIgnoreCase) ||
                  fieldName.EndsWith("Palette", StringComparison.OrdinalIgnoreCase))))
            {
                return FieldKind.ConfigTheme;
            }

            // 6. 静态 Token / 模板配置 / 标签 / Shader Property ID
            if (fieldName.StartsWith("_hasProp", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("hasProp", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_comm", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Descriptors", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("ArcColors", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Token", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Template", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Prefix", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Format", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Aliases", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Label", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("LabelStr", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("LabelText", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Title", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Affix", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Custom", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Units", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("UnitStr", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Names", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_Prop", StringComparison.Ordinal) ||
                fieldName.StartsWith("Prop", StringComparison.Ordinal))
            {
                return FieldKind.ConfigToken;
            }

            // 7. 伪装为裸私有变量的残留脏缓存 (Residual Cache Leaks) —— 重点抓取
            if (WidgetSpecRules.IsResidualDirtyField(fieldName) ||
                fieldName.StartsWith("_cached", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_pending", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_old", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_has", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_showing", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.ResidualDirtyField;
            }

            // 8. 其它所有未纳管的动态状态标量
            return FieldKind.RawScalarLeak;
        }

        // ==========================================================================================
        // 穿透性语义分类 (Roslyn IFieldSymbol / ITypeSymbol)
        //
        // 与上面的 ClassifyField 相比，本通道把"字段是什么类型"从字符串猜测升级为符号决议：
        //   · 类型别名 (using GO = UnityEngine.GameObject;) 不再伪装成裸标量；
        //   · 自定义派生类 (MyGauge : UnityEngine.UI.Image) 沿 BaseType 链穿透到 Unity 底层父类；
        //   · 自定义委托沿 BaseType 链直达 System.MulticastDelegate；
        //   · 泛型 / 可空 / 数组包装统一解包后再判定。
        // 命名约定层 (IsUiHandleName) 仍然保留，但它现在作用在**符号给出的真实类型名**上，
        // 而不是源码文本，因此命名混淆与跨命名空间同名类型都无法再绕过判定。
        // ==========================================================================================

        /// <summary>穿透探针：沿继承链直达这些 Unity 底层父类/接口即判定为视觉图元句柄</summary>
        private static readonly string[] UiBaseTypeProbes =
        {
            "UnityEngine.UI.Graphic",
            "UnityEngine.UI.Selectable",
            "UnityEngine.RectTransform",
            "UnityEngine.Transform",
            "UnityEngine.GameObject",
            "UnityEngine.Canvas",
            "UnityEngine.CanvasGroup",
            "UnityEngine.Material",
            "UnityEngine.Sprite",
            "UnityEngine.Font",
            "UnityEngine.Camera",
            "UnityEngine.Texture",
            "UnityEngine.Texture2D",
            "UnityEngine.RenderTexture",
            "UnityEngine.Shader",
            "UnityEngine.UI.ScrollRect",
            "UnityEngine.UI.Slider",
            "UnityEngine.UI.Toggle",
            "UnityEngine.UI.Dropdown",
            "UnityEngine.UI.InputField",
            "UnityEngine.UI.Outline",
            "UnityEngine.UI.Shadow",
            "UnityEngine.UI.ContentSizeFitter",
            "UnityEngine.UI.LayoutElement",
            "UnityEngine.UI.HorizontalLayoutGroup",
            "UnityEngine.UI.VerticalLayoutGroup",
            "UnityEngine.UI.GridLayoutGroup"
        };

        /// <summary>剥离数组与 Nullable&lt;T&gt; 包装，得到参与归类判定的元素类型</summary>
        private static ITypeSymbol UnwrapToElementType(ITypeSymbol type)
        {
            while (type != null)
            {
                if (type is IArrayTypeSymbol array)
                {
                    type = array.ElementType;
                    continue;
                }
                if (type is INamedTypeSymbol named)
                {
                    if (named.Arity == 1 &&
                        named.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T)
                    {
                        type = named.TypeArguments[0];
                        continue;
                    }
                    if (named.Arity >= 1 && !WidgetSpecRules.IsValidCacheType(named.OriginalDefinition?.Name ?? named.Name))
                    {
                        if (named.Name == "List" || named.Name == "IList" || named.Name == "IEnumerable" ||
                            named.Name == "HashSet" || named.Name == "Queue" || named.Name == "Stack")
                        {
                            type = named.TypeArguments[0];
                            continue;
                        }
                        if (named.Name == "Dictionary" || named.Name == "IDictionary")
                        {
                            type = named.TypeArguments[named.TypeArguments.Length - 1];
                            continue;
                        }
                    }
                }
                return type;
            }
            return null;
        }

        /// <summary>是否为全托管缓存类型（Cached&lt;T&gt; / CachedFloat / DirtyField&lt;T&gt; …），按泛型原始定义名判定</summary>
        private static bool IsManagedCacheType(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named)) return false;
            INamedTypeSymbol definition = named.OriginalDefinition ?? named;
            return WidgetSpecRules.IsValidCacheType(definition.Name);
        }

        /// <summary>命名约定层：项目内自定义句柄/包装类型（无语义基类可穿透时的兜底层）</summary>
        private static bool IsUiHandleName(string baseType)
        {
            if (string.IsNullOrEmpty(baseType)) return false;
            return KnownUiTypeNames.Contains(baseType) ||
                   baseType.EndsWith("UI", StringComparison.Ordinal) ||
                   baseType.EndsWith("Widget", StringComparison.Ordinal) ||
                   baseType.EndsWith("Graphic", StringComparison.Ordinal) ||
                   baseType.EndsWith("View", StringComparison.Ordinal) ||
                   baseType.EndsWith("Feedback", StringComparison.Ordinal) ||
                   baseType.EndsWith("Item", StringComparison.Ordinal) ||
                   baseType.EndsWith("Proxy", StringComparison.Ordinal) ||
                   baseType.EndsWith("Transform", StringComparison.Ordinal) ||
                   baseType.EndsWith("Handles", StringComparison.Ordinal) ||
                   baseType.EndsWith("Slot", StringComparison.Ordinal) ||
                   baseType.EndsWith("Row", StringComparison.Ordinal) ||
                   baseType.EndsWith("Node", StringComparison.Ordinal) ||
                   baseType.EndsWith("Accents", StringComparison.Ordinal) ||
                   baseType == "ApplicationLauncherButton";
        }

        /// <summary>语义判定：字段类型是否（直接或间接）是 Unity 图元/几何/材质句柄</summary>
        private static bool IsUiHandleType(ITypeSymbol type, string simpleName)
        {
            if (type != null)
            {
                for (int i = 0; i < UiBaseTypeProbes.Length; i++)
                {
                    if (SemanticCompilationProvider.IsOrInheritsOrImplements(type, UiBaseTypeProbes[i])) return true;
                }
            }
            return IsUiHandleName(simpleName);
        }

        /// <summary>语义判定：字段类型是否为委托（沿 BaseType 直达 System.MulticastDelegate）</summary>
        private static bool IsDelegateType(ITypeSymbol type, string simpleName)
        {
            if (type != null && SemanticCompilationProvider.IsOrInheritsFrom(type, "System.MulticastDelegate"))
            {
                return true;
            }
            if (string.IsNullOrEmpty(simpleName)) return false;
            return simpleName.StartsWith("Action", StringComparison.Ordinal) ||
                   simpleName.StartsWith("Func", StringComparison.Ordinal) ||
                   simpleName.StartsWith("UnityAction", StringComparison.Ordinal) ||
                   simpleName.StartsWith("UnityEvent", StringComparison.Ordinal);
        }

        /// <summary>语义判定：字段类型是否为遥测契约 / 零 GC 快照结构体</summary>
        private static bool IsTelemetrySnapshotType(ITypeSymbol type, string simpleName)
        {
            if (type != null &&
                (SemanticCompilationProvider.IsOrInheritsOrImplements(type, "IFlightTelemetry") ||
                 SemanticCompilationProvider.IsOrInheritsOrImplements(type, "ModularFlightPanel.Core.Avionics.IWidgetLogic") ||
                 SemanticCompilationProvider.IsOrInheritsOrImplements(type, "ModularFlightPanel.UI.Framework.WidgetLogic") ||
                 SemanticCompilationProvider.IsOrInheritsOrImplements(type, "IWidgetLogic")))
            {
                return true;
            }
            return !string.IsNullOrEmpty(simpleName) &&
                   (simpleName.EndsWith("Snapshot", StringComparison.Ordinal) ||
                    simpleName.EndsWith("Logic", StringComparison.Ordinal));
        }

        /// <summary>
        /// 穿透性字段归类：优先使用 Roslyn 语义符号直达 Unity 底层父类与 C# 底层基类。
        /// 判定顺序与 ClassifyField 保持一致，保证分类口径可比。
        /// </summary>
        public static FieldKind ClassifyFieldBySymbol(IFieldSymbol fieldSymbol, string fieldName, bool isConst, bool isReadOnly)
        {
            if (isConst || (fieldSymbol != null && fieldSymbol.IsStatic)) return FieldKind.ConfigToken;

            ITypeSymbol type = UnwrapToElementType(fieldSymbol?.Type);
            string baseType = type?.Name ?? string.Empty;

            // 1. 全托管缓存
            if (IsManagedCacheType(type)) return FieldKind.ManagedCache;

            // 2. 视觉 UI 句柄 (语义穿透 Unity 底层父类)
            if (IsUiHandleType(type, baseType)) return FieldKind.UiHandle;

            // 3. 事件委托与回调 (语义穿透 System.MulticastDelegate)
            if (IsDelegateType(type, baseType)) return FieldKind.EventCallback;

            // 4. 零 GC 遥测快照结构体
            if (IsTelemetrySnapshotType(type, baseType)) return FieldKind.SnapshotStruct;

            // 5. 主题与样式配置及本地调色板缓存
            if (baseType == "ThemeConfig" || baseType == "WidgetConfig" ||
                baseType.EndsWith("Config", StringComparison.Ordinal) ||
                baseType.EndsWith("Settings", StringComparison.Ordinal) ||
                baseType.EndsWith("Role", StringComparison.Ordinal) ||
                baseType.EndsWith("Palette", StringComparison.Ordinal) ||
                baseType == "LineWeight" ||
                ((baseType == "Color" || baseType == "Color32") &&
                 (fieldName.StartsWith("_c", StringComparison.Ordinal) ||
                  fieldName.EndsWith("Color", StringComparison.OrdinalIgnoreCase) ||
                  fieldName.EndsWith("Col", StringComparison.OrdinalIgnoreCase) ||
                  fieldName.EndsWith("Palette", StringComparison.OrdinalIgnoreCase))))
            {
                return FieldKind.ConfigTheme;
            }

            // 6. 静态 Token / 模板配置 / 标签 / Shader Property ID
            if (fieldName.StartsWith("_hasProp", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("hasProp", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_comm", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Descriptors", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("ArcColors", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Token", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Template", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Prefix", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Format", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Aliases", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Label", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("LabelStr", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("LabelText", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Title", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Affix", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Custom", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Units", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("UnitStr", StringComparison.OrdinalIgnoreCase) ||
                fieldName.EndsWith("Names", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_Prop", StringComparison.Ordinal) ||
                fieldName.StartsWith("Prop", StringComparison.Ordinal))
            {
                return FieldKind.ConfigToken;
            }

            // 7. 伪装为裸私有变量的残留脏缓存 (Residual Cache Leaks)
            if (WidgetSpecRules.IsResidualDirtyField(fieldName) ||
                fieldName.StartsWith("_cached", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_pending", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_old", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_has", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("_showing", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.ResidualDirtyField;
            }

            // 8. 其它所有未纳管的动态状态标量
            return FieldKind.RawScalarLeak;
        }

        public static string GetFieldRemediation(string typeName, string fieldName, FieldKind kind)
        {
            string baseType = UnwrapType(typeName);
            switch (baseType)
            {
                case "float":
                    return $"private readonly CachedFloat {fieldName} = new CachedFloat(0f, tolerance: 0.001f);";
                case "double":
                    return $"private readonly CachedDouble {fieldName} = new CachedDouble(0.0, tolerance: 0.01);";
                case "int":
                    return $"private readonly Cached<int> {fieldName} = new Cached<int>(-1);";
                case "bool":
                    return $"private readonly Cached<bool> {fieldName} = new Cached<bool>(false);";
                case "string":
                    return $"private readonly Cached<string> {fieldName} = new Cached<string>(string.Empty);";
                case "Vector2":
                    return $"private readonly Cached<Vector2> {fieldName} = new Cached<Vector2>(Vector2.zero);";
                case "Vector3":
                    return $"private readonly Cached<Vector3> {fieldName} = new Cached<Vector3>(Vector3.zero);";
                case "Quaternion":
                    return $"private readonly Cached<Quaternion> {fieldName} = new Cached<Quaternion>(Quaternion.identity);";
                case "Color":
                    return $"private readonly Cached<Color> {fieldName} = new Cached<Color>(Color.clear);";
                case "Color32":
                    return $"private readonly Cached<Color32> {fieldName} = new Cached<Color32>(new Color32(0, 0, 0, 0));";
                default:
                    if (baseType.StartsWith("List<") || baseType == "List")
                        return $"池化复用集合，严禁热循环 new/Clear 集合；通过脏标记增量同步";
                    if (baseType.StartsWith("Dictionary<") || baseType == "Dictionary")
                        return $"只读/静态配置查表，避免每帧高频写入与查找开销";
                    return $"private readonly Cached<{baseType}> {fieldName} = new Cached<{baseType}>();";
            }
        }

        /// <summary>
        /// 门禁 / CLI 入口：先经发现层构建语义编译上下文，再执行穿透扫描。
        /// </summary>
        public static WidgetFieldPenetrationReport Scan(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot))
            {
                repoRoot = WidgetSourceAudit.ResolveRepositoryRoot();
            }

            SemanticCompilationContext semanticContext = null;
            WidgetDiscoveryResult discovery = WidgetSourceAudit.Discover(repoRoot);
            if (discovery != null)
            {
                semanticContext = discovery.SemanticContext ?? discovery.Graph?.SemanticContext;
            }

            return ScanCore(repoRoot, semanticContext);
        }

        /// <summary>
        /// 语义通道入口：直接复用调用方已构建的语义编译上下文，不做二次编译。
        /// </summary>
        public static WidgetFieldPenetrationReport Scan(WidgetDiscoveryResult discovery)
        {
            SemanticCompilationContext semanticContext =
                discovery?.SemanticContext ?? discovery?.Graph?.SemanticContext;
            return ScanCore(WidgetSourceAudit.ResolveRepositoryRoot(), semanticContext);
        }

        /// <summary>
        /// 穿透性字段扫描核心。
        ///
        /// 语义可用时：字段类型一律由 IFieldSymbol.Type 决议，再沿 BaseType / AllInterfaces 穿透到
        /// Unity 底层父类与 System 底层基类，彻底摆脱"类型简名字符串猜测"。
        /// 语义不可用时：才降级到 RoslynAstHelper.GetSimpleTypeName 的字面量匹配，
        /// 并在报告里把 SemanticActive 置 false（结论强度降级必须可感知，不得静默给数）。
        /// </summary>
        private static WidgetFieldPenetrationReport ScanCore(string repoRoot, SemanticCompilationContext semanticContext)
        {
            var report = new WidgetFieldPenetrationReport
            {
                SemanticActive = semanticContext != null
            };

            string widgetsDir = Path.Combine(repoRoot ?? string.Empty, "src", "ModularFlightPanel", "UI", "Widgets");
            if (!Directory.Exists(widgetsDir)) return report;

            var files = Directory.GetFiles(widgetsDir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var filePath in files)
            {
                string fileName = Path.GetFileName(filePath);

                // 语义树优先：必须复用语义编译里的同一棵解析树（共用 RoslynAstHelper.UnifiedParseOptions），
                // 否则受 #if KSP_RUNTIME 保护的字段会与门禁其余判定看到不同的代码集。
                SemanticModel model = semanticContext?.GetSemanticModel(filePath)
                    ?? semanticContext?.GetSemanticModel(fileName);
                CompilationUnitSyntax root = model != null
                    ? model.SyntaxTree.GetRoot() as CompilationUnitSyntax
                    : RoslynAstHelper.ParseRoot(File.ReadAllText(filePath));
                if (root == null) continue;

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    INamedTypeSymbol classSymbol = model?.GetDeclaredSymbol(classDecl);
                    bool isWidgetClass;
                    if (classSymbol != null)
                    {
                        isWidgetClass = SemanticCompilationProvider.IsOrInheritsFrom(classSymbol, "ModularFlightPanel.UI.Framework.BaseFlightWidget") ||
                                        SemanticCompilationProvider.IsOrInheritsFrom(classSymbol, "BaseFlightWidget");
                    }
                    else
                    {
                        string cName = classDecl.Identifier.Text;
                        isWidgetClass = cName.EndsWith("Widget", StringComparison.Ordinal) ||
                                        cName.StartsWith("BaseNavball", StringComparison.Ordinal);
                    }

                    if (!isWidgetClass) continue;

                    string className = classDecl.Identifier.Text;
                    var fieldList = new List<AuditedFieldInfo>();

                    foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
                    {
                        bool isConst = field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword));
                        bool isReadOnly = field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword));
                        bool isStatic = field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword));
                        string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);

                        foreach (var v in field.Declaration.Variables)
                        {
                            string fieldName = v.Identifier.Text;
                            int line = RoslynAstHelper.GetLine(v);

                            IFieldSymbol fieldSymbol = model?.GetDeclaredSymbol(v) as IFieldSymbol;

                            FieldKind kind = fieldSymbol != null
                                ? ClassifyFieldBySymbol(fieldSymbol, fieldName, isConst, isReadOnly)
                                : ClassifyField(typeName, fieldName, isConst, isReadOnly, isStatic);

                            if (fieldSymbol != null) report.SemanticResolvedFieldCount++;

                            var info = new AuditedFieldInfo
                            {
                                FileName = fileName,
                                ClassName = className,
                                FieldName = fieldName,
                                TypeName = typeName,
                                SemanticTypeFullName = fieldSymbol?.Type?.ToDisplayString(),
                                ResolvedBySymbol = fieldSymbol != null,
                                Line = line,
                                Kind = kind,
                                IsReadOnly = isReadOnly,
                                IsConst = isConst
                            };
                            fieldList.Add(info);

                            report.TotalFieldsScanned++;
                            switch (kind)
                            {
                                case FieldKind.ManagedCache: report.TotalManagedCaches++; break;
                                case FieldKind.UiHandle: report.TotalUiHandles++; break;
                                case FieldKind.EventCallback: report.TotalEventCallbacks++; break;
                                case FieldKind.SnapshotStruct: report.TotalSnapshotStructs++; break;
                                case FieldKind.ConfigTheme:
                                case FieldKind.ConfigToken: report.TotalConfigs++; break;
                                case FieldKind.ResidualDirtyField:
                                    report.TotalResidualLeaks++;
                                    TrackLeakType(report, typeName);
                                    break;
                                case FieldKind.RawScalarLeak:
                                    report.TotalScalarLeaks++;
                                    TrackLeakType(report, typeName);
                                    break;
                            }
                        }
                    }

                    if (fieldList.Count > 0)
                    {
                        report.TotalWidgetsScanned++;
                        report.FieldsByWidget[className] = fieldList;
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// 语义通道自证（防"语义点位被拆除而门禁依然全绿"）。
        ///
        /// 合成用例刻意只用**字符串口径一定判错**的写法：
        ///   1. 类型别名 `using GO = UnityEngine.GameObject;` → 语法简名是 "GO"，必落裸标量；
        ///   2. 自定义派生类 `MyCustomReadout : UnityEngine.UI.Image` → 语法简名没有任何 Unity 后缀；
        ///   3. 自定义委托 `CustomHandler` → 语法简名既不是 Action 也不是 Func。
        /// 三条都必须被语义判定救回来。任何一条被拆掉，本自检立即变红。
        /// </summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            LastSelfTestCaseCount = 0;

            const string src = @"
using System;
using UnityEngine;
using UnityEngine.UI;
using GO = UnityEngine.GameObject;

namespace TestNs
{
    public delegate void CustomHandler(int value);

    public class MyCustomReadout : Image { }

    public class Holder
    {
        private MyCustomReadout _readout;
        private GO _anchor;
        private CustomHandler _onChanged;
        private readonly Cached<float> _cachedSpeed = new Cached<float>(0f);
        private float _lastSpeed;
    }
}

namespace ModularFlightPanel.UI.Framework
{
    public class Cached<T>
    {
        public Cached(T value) { }
    }
}";

            var sources = new List<WidgetSourceFile>
            {
                new WidgetSourceFile
                {
                    Name = "FieldPenetrationSelfTest.cs",
                    Path = "FieldPenetrationSelfTest.cs",
                    Text = src
                }
            };

            SemanticCompilationContext context = SemanticCompilationProvider.BuildCompilation(sources);
            SemanticModel model = context?.GetSemanticModel("FieldPenetrationSelfTest.cs");
            if (model == null)
            {
                failures.Add("字段穿透自检：未能构建语义编译上下文（语义通道整体不可用）");
                return failures;
            }

            var holder = model.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == "Holder");
            if (holder == null)
            {
                failures.Add("字段穿透自检：合成源码里找不到 Holder 类声明");
                return failures;
            }

            var expectations = new Dictionary<string, FieldKind>(StringComparer.Ordinal)
            {
                { "_readout",    FieldKind.UiHandle },
                { "_anchor",     FieldKind.UiHandle },
                { "_onChanged",  FieldKind.EventCallback },
                { "_cachedSpeed", FieldKind.ManagedCache },
                { "_lastSpeed",  FieldKind.ResidualDirtyField }
            };

            int checkedCount = 0;
            foreach (var variable in holder.Members.OfType<FieldDeclarationSyntax>()
                         .SelectMany(f => f.Declaration.Variables))
            {
                string fieldName = variable.Identifier.Text;
                if (!expectations.TryGetValue(fieldName, out FieldKind expected)) continue;

                LastSelfTestCaseCount++;
                IFieldSymbol symbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (symbol == null)
                {
                    failures.Add("字段穿透自检：" + fieldName + " 未能取得 IFieldSymbol（语义点位已被拆除）");
                    continue;
                }

                FieldKind actual = ClassifyFieldBySymbol(symbol, fieldName, symbol.IsConst, symbol.IsReadOnly);
                if (actual != expected)
                {
                    failures.Add("字段穿透自检：" + fieldName + " 归类错误，期望 " + expected + " 实得 " + actual
                               + "（符号类型 " + symbol.Type.ToDisplayString() + "）");
                }
                checkedCount++;
            }

            if (checkedCount != expectations.Count)
            {
                failures.Add("字段穿透自检：用例覆盖不完整，仅命中 " + checkedCount + "/" + expectations.Count + " 个字段");
            }

            // 反例自证：同一份源码若走旧的类型简名口径，三条语义用例必然判错。
            // 这一条同时锁死"删掉语义分支 → 上面三条断言失败 → 自检变红"的因果链。
            if (ClassifyField("MyCustomReadout", "_readout", false, true) != FieldKind.RawScalarLeak)
            {
                failures.Add("字段穿透自检：类型简名口径未把 MyCustomReadout 判为裸标量，"
                           + "说明反例已失效，本自检将无法证明语义通道仍在生效");
            }
            if (ClassifyField("GO", "_anchor", false, true) != FieldKind.RawScalarLeak)
            {
                failures.Add("字段穿透自检：类型简名口径未把别名 GO 判为裸标量，反例已失效");
            }

            return failures;
        }

        /// <summary>最近一次 SelfTest 实际执行的用例条数（禁止写死数字）</summary>
        public static int LastSelfTestCaseCount { get; private set; }

        private static void TrackLeakType(WidgetFieldPenetrationReport report, string typeName)
        {
            string clean = UnwrapType(typeName);
            if (report.LeakedTypeHistogram.TryGetValue(clean, out int count))
            {
                report.LeakedTypeHistogram[clean] = count + 1;
            }
            else
            {
                report.LeakedTypeHistogram[clean] = 1;
            }
        }

        public static string RenderConsoleReport(WidgetFieldPenetrationReport report, bool showAll = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔═══════════════════════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║       MFP 航电组件全数据类型与私有变量穿透审计大盘 (Private Field Penetration Radar)       ║");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  ├─ 扫描组件类: {report.TotalWidgetsScanned} 个 | 总私有字段数: {report.TotalFieldsScanned} 个");
            sb.AppendLine("  ├─ 字段归类通道: "
                          + (report.SemanticActive
                                ? "穿透性语义符号决议 (IFieldSymbol → Unity 底层父类 / System 基类)"
                                : "⚠ 无语义编译上下文，已降级为类型简名匹配 (结论强度低于 L4)")
                          + $" | 语义决议字段 {report.SemanticResolvedFieldCount}/{report.TotalFieldsScanned}");
            sb.AppendLine($"  ├─ 全托管缓存槽位 (Managed): {report.TotalManagedCaches} 处 ({report.OverallManagedRatio:F1}%)");
            sb.AppendLine($"  ├─ 视觉图元句柄 (UiHandle): {report.TotalUiHandles} 处 | 零GC遥测快照: {report.TotalSnapshotStructs} 处");
            sb.AppendLine($"  ├─ 静态配置与主题 (Config): {report.TotalConfigs} 处 | 事件委托回调: {report.TotalEventCallbacks} 处");
            sb.AppendLine($"  └─ ✘ 裸私有状态残留 (Total Leaks): {report.TotalAllLeaks} 处");
            sb.AppendLine($"      • 手动脏缓存残留 (Residual Cache Leaks): {report.TotalResidualLeaks} 处 (如 _last*, _cached*, _dirty*)");
            sb.AppendLine($"      • 裸标量状态暴露 (Raw Scalar Leaks):     {report.TotalScalarLeaks} 处 (未接入纳管)");
            sb.AppendLine();

            sb.AppendLine("┌─ [穿透全数据类型分布直方图] ──────────────────────────────────────────────────────────────┐");
            foreach (var kvp in report.LeakedTypeHistogram.OrderByDescending(k => k.Value).Take(12))
            {
                string bar = new string('█', Math.Min(30, (kvp.Value + 9) / 10));
                sb.AppendLine($"│  • {kvp.Key,-16} : {kvp.Value,4} 个  {bar,-30}│");
            }
            sb.AppendLine("└──────────────────────────────────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            sb.AppendLine("╔═══════════════════════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                    MFP 航电架构规范化整改实施指南 (Actionable Playbook)                    ║");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine("  【原则 1 - 彻底根除残留脏缓存】");
            sb.AppendLine("    严禁在组件中手写 _last*, _cached*, _dirty* 等裸变量。必须全量迁移至 Cached<T> / CachedFloat，");
            sb.AppendLine("    享受切船全自动复位与框架生命周期保护，杜绝跨载具状态污染。");
            sb.AppendLine("  【原则 2 - 浮点/双精度遥测必须配置死区容差】");
            sb.AppendLine("    连续物理量必须携带容差 (Tolerance) 抑制高频传感器微抖动，避免无感知微变引发整树重排：");
            sb.AppendLine("    • 姿态角度 (Pitch/Roll/Yaw): tolerance = 0.05f (度)");
            sb.AppendLine("    • 速度与垂直速率 (Speed/VS):  tolerance = 0.05 ~ 0.1 (m/s)");
            sb.AppendLine("    • 推进剂余量与滑条 (Fraction): tolerance = 0.001f ~ 0.005f");
            sb.AppendLine("    • 推重比与加速度 (TWR/Acc):    tolerance = 0.01f / 0.02");
            sb.AppendLine("    • 屏幕物理尺寸与坐标 (px):     tolerance = 0.5f");
            sb.AppendLine("  【原则 3 - 绘制热循环统一使用 Update 守卫】");
            sb.AppendLine("    UI 文本与几何写入必须包裹在 if (_cached.Update(nextValue)) 内部：");
            sb.AppendLine("    • 字符串更新：使用 SetTextIfChanged(text, CacheManager.FastFormat(...))");
            sb.AppendLine("    • 几何形变更新：使用 if (_dirtySize.Update(...)) { rt.sizeDelta = ...; } 阻断重排");
            sb.AppendLine("  【原则 4 - 遥测数据解耦快照化】");
            sb.AppendLine("    热循环绘制前只读取只读 Snapshot 结构体，严禁在 OnUIDrawLoop 中跨系统遍历场景对象。");
            sb.AppendLine();

            var compliantClasses = new List<string>();
            var leakingClasses = new List<string>();

            foreach (var kvp in report.FieldsByWidget.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string className = kvp.Key;
                var fields = kvp.Value;
                var leaks = fields.Where(f => f.Kind == FieldKind.ResidualDirtyField || f.Kind == FieldKind.RawScalarLeak).ToList();
                var managed = fields.Where(f => f.Kind == FieldKind.ManagedCache).ToList();

                float ratio = (managed.Count + leaks.Count) > 0
                    ? (float)managed.Count / (managed.Count + leaks.Count) * 100f
                    : 100f;

                string statusIcon = leaks.Count == 0 ? "✔ [100% 满分托管]" : $"✘ [{leaks.Count} 处裸私有变量]";
                sb.AppendLine($"• {className} ({fields[0].FileName}) -> {statusIcon} 托管率: {ratio:F1}% (Cache: {managed.Count}, Residual: {fields.Count(f => f.Kind == FieldKind.ResidualDirtyField)}, Raw: {fields.Count(f => f.Kind == FieldKind.RawScalarLeak)})");

                if (leaks.Count > 0)
                {
                    leakingClasses.Add(className);
                    sb.AppendLine("   ├─ 穿透发现未纳管裸变量与推荐改造代码:");
                    foreach (var leak in leaks)
                    {
                        string tag = leak.Kind == FieldKind.ResidualDirtyField ? "[残留脏缓存]" : "[未纳管标量]";
                        string remedy = GetFieldRemediation(leak.TypeName, leak.FieldName, leak.Kind);
                        sb.AppendLine($"   │   • L{leak.Line,-4} {leak.TypeName,-10} {leak.FieldName,-24} {tag}");
                        sb.AppendLine($"   │       ↳ 建议改造: {remedy}");
                    }

                    int rLeaks = fields.Count(f => f.Kind == FieldKind.ResidualDirtyField);
                    int numFloats = fields.Count(f => UnwrapType(f.TypeName) == "float" || UnwrapType(f.TypeName) == "double");
                    int numStrings = fields.Count(f => UnwrapType(f.TypeName) == "string");
                    int numBools = fields.Count(f => UnwrapType(f.TypeName) == "bool");

                    sb.AppendLine("   └─ 💡 专属整改意见:");
                    int adviceIdx = 1;
                    if (rLeaks > 0)
                    {
                        sb.AppendLine($"       {adviceIdx++}. 【淘汰手写脏标记】移除 {rLeaks} 处手写的 _last*/_cached* 标量，替换为 Cached<T>，享受切船生命周期自动复位与统一判定。");
                    }
                    if (numFloats > 0)
                    {
                        sb.AppendLine($"       {adviceIdx++}. 【引入浮点死区容差】数值型遥测 ({numFloats} 处) 必须配置合理 tolerance (如角度 0.05f、速度 0.1、推重比 0.01f)，阻断浮点微小抖动引发 Canvas 重绘。");
                    }
                    if (numStrings > 0)
                    {
                        sb.AppendLine($"       {adviceIdx++}. 【阻断字符串堆分配】文本更新由 _cached.Update() 守卫并搭配 SetTextIfChanged，杜绝热循环每帧重复产生 GC 压力。");
                    }
                    if (numBools > 0)
                    {
                        sb.AppendLine($"       {adviceIdx++}. 【布尔状态驱动】使用 Cached<bool>.Update() 统一驱动 UI 激活态或高亮切换，避免无条件写入组件样式。");
                    }
                }
                else
                {
                    compliantClasses.Add(className);
                    if (showAll && managed.Count > 0)
                    {
                        sb.AppendLine("   └─ 全量纳管槽位:");
                        foreach (var m in managed)
                        {
                            sb.AppendLine($"       • L{m.Line,-4} {m.TypeName,-18} {m.FieldName}");
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"═══════════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine($"穿透扫描总结: 满分纳管组件: {compliantClasses.Count} 个 | 存在裸变量泄漏组件: {leakingClasses.Count} 个");
            sb.AppendLine($"═══════════════════════════════════════════════════════════════════════════════════════════");

            return sb.ToString();
        }
    }
}
