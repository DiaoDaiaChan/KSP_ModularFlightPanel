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
                 t.StartsWith("HashSet<", StringComparison.Ordinal)) && t.EndsWith(">", StringComparison.Ordinal))
            {
                int start = t.IndexOf('<');
                t = t.Substring(start + 1, t.Length - start - 2).Trim();
            }
            return t;
        }

        public static FieldKind ClassifyField(string typeName, string fieldName, bool isConst, bool isReadOnly)
        {
            if (isConst) return FieldKind.ConfigToken;

            string baseType = UnwrapType(typeName);

            // 1. 全托管缓存
            if (WidgetSpecRules.ValidCacheTypes.Any(t => string.Equals(t, baseType, StringComparison.Ordinal)) ||
                baseType.StartsWith("Cached<", StringComparison.Ordinal) ||
                baseType.StartsWith("DirtyField<", StringComparison.Ordinal))
            {
                return FieldKind.ManagedCache;
            }

            // 2. 视觉 UI 句柄
            if (KnownUiTypeNames.Contains(baseType) ||
                baseType.EndsWith("UI", StringComparison.Ordinal) ||
                baseType.EndsWith("Widget", StringComparison.Ordinal) ||
                baseType.EndsWith("Graphic", StringComparison.Ordinal) ||
                baseType.EndsWith("View", StringComparison.Ordinal) ||
                baseType.EndsWith("Feedback", StringComparison.Ordinal) ||
                baseType.EndsWith("Item", StringComparison.Ordinal) ||
                baseType.EndsWith("Proxy", StringComparison.Ordinal) ||
                baseType.EndsWith("Transform", StringComparison.Ordinal) ||
                baseType == "ApplicationLauncherButton")
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

            // 4. 零 GC 遥测快照结构体
            if (baseType.EndsWith("Snapshot", StringComparison.Ordinal) || baseType == "IFlightTelemetry")
            {
                return FieldKind.SnapshotStruct;
            }

            // 5. 主题与样式配置
            if (baseType == "ThemeConfig" || baseType == "WidgetConfig" ||
                baseType.EndsWith("Role", StringComparison.Ordinal) ||
                baseType.EndsWith("Palette", StringComparison.Ordinal) ||
                baseType == "LineWeight")
            {
                return FieldKind.ConfigTheme;
            }

            // 6. 静态 Token / 模板配置 / Shader Property ID
            if (isReadOnly && (fieldName.EndsWith("Token", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.EndsWith("Template", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.EndsWith("Prefix", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.EndsWith("Format", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.EndsWith("Aliases", StringComparison.OrdinalIgnoreCase) ||
                               fieldName.StartsWith("_Prop", StringComparison.Ordinal) ||
                               fieldName.StartsWith("Prop", StringComparison.Ordinal)))
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

        public static WidgetFieldPenetrationReport Scan(string repoRoot)
        {
            var report = new WidgetFieldPenetrationReport();
            string widgetsDir = Path.Combine(repoRoot, "src", "ModularFlightPanel", "UI", "Widgets");
            if (!Directory.Exists(widgetsDir)) return report;

            var files = Directory.GetFiles(widgetsDir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                string text = File.ReadAllText(filePath);
                var root = RoslynAstHelper.ParseRoot(text);

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    string className = classDecl.Identifier.Text;
                    var fieldList = new List<AuditedFieldInfo>();

                    foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
                    {
                        bool isConst = field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword));
                        bool isReadOnly = field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword));
                        string typeName = RoslynAstHelper.GetSimpleTypeName(field.Declaration.Type);

                        foreach (var v in field.Declaration.Variables)
                        {
                            string fieldName = v.Identifier.Text;
                            int line = RoslynAstHelper.GetLine(v);

                            FieldKind kind = ClassifyField(typeName, fieldName, isConst, isReadOnly);
                            var info = new AuditedFieldInfo
                            {
                                FileName = fileName,
                                ClassName = className,
                                FieldName = fieldName,
                                TypeName = typeName,
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
