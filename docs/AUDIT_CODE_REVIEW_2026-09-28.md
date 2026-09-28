# 审计内核代码复核报告 — `src/ModularFlightPanel/UI/Auditing`

> 复核范围：`UI/Auditing/` 全目录 7 个文件（约 4,200 行）
> `I18nSyntaxAuditor.cs` · `WidgetSourceAudit.cs` · `WidgetModernizationAudit.cs` · `WidgetSpecificationValidator.cs` · `WidgetColorLiteralAudit.cs` · `WidgetSpecRules.cs` · `RoslynAstHelper.cs`
> 复核方式：逐文件通读 + 结构度量（if 分布、方法体量、裸字面量提取）+ 门禁接线核查（`tools/HeadlessValidator/Program.cs`）
> 日期：2026-09-28

---

## 0. 结论

**"大量使用 if" 不是它的病根。**

| 度量 | 数值 |
|---|---|
| `if` 语句总数 | 310 |
| 其中守卫子句（`if (x) return/continue/break`） | 115（**37%**） |
| 单文件最多 if | `I18nSyntaxAuditor.cs` 105 条（守卫 46 条） |
| 规则体裸字面量 | 已回收进 `WidgetSpecRules` 的约 30 个；**仍散落在规则体里的约 45 个** |

守卫子句占比 37%、判定数据基本表驱动（`SceneQueryApis` / `DisplayTextSlots` / 各黑名单数组），整体**不是 if 汤**。

真正的不靠性在另一个轴上：**它做的是"语法文本匹配"，但命名与注释声称"工业级语义审计"**。它只用 Roslyn 当**解析器**，全程没有 `SemanticModel` / `ISymbol`。于是它无法区分下面两种情形，只能一律判"合规"：

- (a) 代码确实合规；
- (b) 审计**没看懂**这段代码。

这就是"看起来可靠、实际不可靠"的来源。下面按 静默假绿 → 漏报/误报 → 可维护性腐化 三级列出，每条都带 `文件:行` 证据。

---

## 1. P0 — 会导致「静默假绿」的问题

> 审计工具最大的失败模式不是误报，是**漏报且报告全绿**。这一级最该先修。

### 1.1 解析失败没有守卫

**证据链**

- `RoslynAstHelper.ParseTree` (RoslynAstHelper.cs:24-38) 返回 `SyntaxTree`，**从不检查 `tree.GetDiagnostics()`**。
- `WidgetClassGraph.Build` (WidgetSourceAudit.cs:86) 直接 `RoslynAstHelper.ParseRoot(file.Text)` 就建图。
- `Discover` (WidgetSourceAudit.cs:329-345) 只捕获 **文件 I/O** 异常；语法层面的失败没有任何捕获点。
- `WidgetDiscoveryStatus` (WidgetSourceAudit.cs:240-256) 定义了 5 个状态（`Ok` / `RepositoryRootUnresolved` / `SourceScopeMissing` / `SourceReadFailed` / `NoWidgetClasses`），**没有 `SourceParseFailed`**。

**后果**

被审源码语法损坏时（未提交完的文件、临时语法错误、C# 版本不兼容语法）：

```
坏语法 → ClassDeclarationSyntax 部分/全部丢失 → ContractClasses 变小
      → 若恰好为 0 会落到 NoWidgetClasses（ERROR，运气好）
      → 若只是部分丢失 → 该组件凭空消失 → 报告全绿（运气差）
```

**这条与它自己的设计宗旨直接冲突。** `WidgetSourceAudit.cs:239` 的文件头注释写着"发现层状态：审计范围不可用 / 不可信时绝不允许静默放行"——它防住了"读不到文件"，没防住"读到了但没解析成功"。

**修法**

```csharp
// WidgetDiscoveryStatus 增加 SourceParseFailed
var tree = RoslynAstHelper.ParseTree(text);
if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
{
    result.ParseErrors.Add(path + ": " + tree.GetDiagnostics().First(d => d.Severity == DiagnosticSeverity.Error));
}
```

并按 `IsEnvironmentDegradation` 之外的状态一律判 ERROR（与既有约定一致）。**这是本目录 ROI 最高的一条改动。**

### 1.2 六个豁免判定里有三个仍用「任意祖先」语义，中文零容忍存在盲区

`I18nAstWalker.VisitLiteralExpression` (I18nSyntaxAuditor.cs:389-459) 的执行顺序是：**先过 6 个豁免判定，最后才查中文**（L426）。

这 6 个判定的语义**不统一**：

| 判定 | 行号 | 语义 | 风险 |
|---|---|---|---|
| `IsExemptI18nSlot` | L549-576 | 最近宿主 ✓ | — |
| `IsInsideLogOrDiagnostic` | L593-613 | 最近宿主 ✓ | — |
| `IsInsideDisplayTextSlot` | L714-782 | 最近宿主 ✓ | — |
| `IsInsideStringComparison` | **L647-665** | **任意祖先** ✗ | 漏报 |
| `IsInsideEngineResourceOrPathCall` | **L624-645** | **任意祖先** ✗ | 漏报 |
| `IsInsideMicroControlRegistration` | **L667-683** | **任意祖先** ✗ | 漏报 |

前三个是**已经修过的**——注释明确记录："旧实现只要祖先链上出现过日志调用就整段豁免，于是 `MFPLogger.Info(Other("中文"))` 这类嵌套调用里的未本地化中文被静默放过"。`SelfTest` 用例 8 (L862-868) 就是这个洞的回归用例。

**但同类的洞在另外三条路径上原封不动地存在。** 最直接的证据是它自己的测试夹具：

```csharp
// I18nSyntaxAuditor.SelfTest 用例 14 (L1008-1011)
public void InitControls() {
    this.Controls.Register(new WidgetReadoutControl(null, null, TextStyleRole.PrimaryValue, "UnboundSpeed", "未绑读数"));
}
```

`"未绑读数"` 是硬编码中文，按「严重规则 A」应当报 `HardcodedChinese`。但因为整串落在 `new WidgetReadoutControl(...)` 里，被 `IsInsideMicroControlRegistration` (L667-683) 的"任意祖先"判定整段豁免——该用例只断言了 `TelemetryAssemblyWarning == 1`。**审计器的夹具自己演示了这个盲区。**

**修法**：三条判定统一改为"最近宿主"语义（遇到第一个决定性节点就 `return`），并补 3 条自检反例：

```csharp
// 微控件注册参里嵌套调用中的中文必须报
"Controls.Register(new WidgetReadoutControl(null,null,r,\"N\", Helper(\"中文\")));"
// 字符串比较参里嵌套调用中的中文必须报
"var b = s.Contains(Helper(\"中文\"));"
// 路径调用参里嵌套调用中的中文必须报
"var p = Path.Combine(dir, Helper(\"中文\"));"
```

### 1.3 规则体裸字面量偏离「单一声明点」

`WidgetSpecRules.cs:10-13` 的声明是硬约束：

> 本文件是唯一允许写死"契约名 / 黑名单 API / 元数据名"的地方 …… 禁止在规则体里再写字符串或按文件名/类名做特判

实测**并未做到**（用脚本提取规则体字符串常量，已剔除注释）：

| 文件 | 仍散落的契约名 |
|---|---|
| `WidgetSourceAudit.cs` | `"TextWidget"` `"LinearBarWidget"`(L589)、`"Register"`(L629)、`"WidgetReadoutControl"`(L645)、`"WidgetLinearBarControl"`(L695)、`"base."`(L572)、`"src"/"ModularFlightPanel"/"Widgets"`(L297) |
| `WidgetModernizationAudit.cs` | `MicroControlTypes` 8 个 DSL 类型名(L129-133)、`"BaseSize"` `"AutoCreateCardFrame"` `"Controls.Add"` `"TextWidget."` `"LinearBarWidget."` `"UIFactory."`(L178-214)、`"_texPixels"`(L221)、`"Image"` `"GetUiMaterial"`(L234-236)、`"CustomTemplate"`(L290)、`"_last"/"Text"/"Str"/"Val"`(L320-323)、`"GameObject"/"UnityEngine.GameObject"`(L403) |
| `WidgetSpecificationValidator.cs` | `"OnInitialize"` `"RefreshTier"` `"ApplyTheme"` `"OnUpdateTelemetry"` `"OnDestroy"` `"Update"` `"Vessel"` `"Part"` `"CelestialBody"`(L332-447) |

**最有说服力的一条**：`WidgetSpecificationValidator.cs:152-159` 刚写完

```csharp
// 规则码只有一份定义（WidgetSpecRules），此处仅作兼容别名，禁止再写字面量
public const string Rule_RefreshTier = WidgetSpecRules.RefreshTier;
```

紧接着 L347 就是

```csharp
var tierProp = type.GetProperty("RefreshTier", BindingFlags.Public | BindingFlags.Instance);
```

`RefreshTier` 同时以 `WidgetSpecRules.TierProperty`(L52) 和裸字面量 `"RefreshTier"` 存在两份。**属性一旦改名，源码审计器与反射校验器会给出互相矛盾的结论**——正是该设计宣称要消灭的漂移。

**修法**：把上述裸字面量回收进 `WidgetSpecRules`（新增 `BaseSizeProperty` / `AutoCardFrameProperty` / `MicroControlTypeNames` / `InheritanceCheckMethod` 等常量），并加一条自检：**规则体文件里不得出现 `WidgetSpecRules` 已知常量的原始文本**（可用源码扫描实现，meta 但有效）。

---

## 2. P1 — 会导致误报 / 漏报（非静默，容易发现）

### 2.1 构造函数参数下标硬编码 —— 重构即静默翻转

`WidgetSourceAudit.ScanTelemetryAssembly` (WidgetSourceAudit.cs:650-728) 对两个构造函数的签名做了**人肉镜像**：

```csharp
// WidgetReadoutControl 带 Token 重载仅有两个: 12 参数版本 (第10参) 与 7 参数版本 (第7参)
// 其余 1~6 参数的重载均无 Token 绑定参数!
if (cArgs.Count < 7)        { isMissingToken = true; ... }   // L655
else if (cArgs.Count == 7)  { var tokenArg = cArgs[6].Expression; ... }   // L665-667
else if (cArgs.Count == 12) { var tokenArg = cArgs[9].Expression; ... }   // L676-678

// WidgetLinearBarControl 带 Token 重载仅有 11 参数版本 (第7参)
if (cArgs.Count < 11)       { isMissingToken = true; ... }   // L705
else if (cArgs.Count == 11) { var tokenArg = cArgs[6].Expression; ... }   // L711-713
```

注释自己承认这只靠人肉维护（"其余 1~6 参数的重载均无 Token 绑定参数!"）。

**失效方向二选一，都很糟：**

- **静默放过**：`WidgetReadoutControl` 新增一个 8 参重载 → 既不 `< 7` 也不 `== 7/== 12` → `isMissingToken` 保持 `false` → 该重载的未绑 Token 控件**静默通过**。
- **整体误报**：7 参版本前面插入新参数 → 第 7 参不再是 token → 所有合规控件被误报。

**修法（优选）**：用语义模型按**参数名**定位，而不是按下标：

```csharp
var sym = semanticModel.GetSymbolInfo(objCreation).Symbol as IMethodSymbol;
var tokenParam = sym?.Parameters.FirstOrDefault(p => p.Name == "telemetryToken" || p.Name == "token");
int idx = tokenParam?.Ordinal ?? -1;
```

**退化方案**：把"哪个重载的第几参是 token"提成 `WidgetSpecRules` 里的数据表，并加一条 **反射校验自检**：`typeof(WidgetReadoutControl).GetConstructors()` 逐个体检参数名，与实际签名不符即自检失败。这样重构时门禁会响，而不是静默翻转。

### 2.2 继承图同名类静默覆盖

`WidgetSourceAudit.cs:119-131`，注释自认：

> 同类名跨命名空间时后声明者胜出，与旧实现一致

`ByName` 是 `Dictionary<string, WidgetClassNode>`(L60)，L115 `graph.ByName[node.Name] = node;` 直接覆盖。

**后果**：两个命名空间各有一个 `FooWidget` → 后扫描的文件胜出 → 另一个类的 `Base` 指向错误节点 → 依赖继承链的 **SPEC-001/002/003/004/008 全部错判**。

而 `SelfTest` 的合成用例（`BuildSyntheticWidget`，L1053-1069）只声明单一命名空间 `N`，**永远走不到这条路径**。

**修法**：`ByName` 改为 `Dictionary<string, List<WidgetClassNode>>`；继承链解析时若同名多候选，用 `BaseNames` 里的全限定名（`QualifiedNameSyntax`）消歧；仍无法消歧则上报 WARNING，而不是默默取一个。

### 2.3 `lastReportedLine` 跨两次语义不同的遍历共享

`WidgetSourceAudit.cs:764-787`：

```csharp
int lastReportedLine = -1;
foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) { ... lastReportedLine = line; }
foreach (var ma  in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()) { ... if (line == lastReportedLine) continue; ... }
```

去重条件是"**行号相同**"，而非"同一表达式"。同一行上两个不同的场景查询只报一个；且这个状态跨两个语义不同的遍历共享，行为依赖遍历顺序。

**修法**：改成 `HashSet<string>`（键 = 行号 + 表达式文本），或每趟遍历独立的局部变量。

### 2.4 `using` 别名绕过 —— 作者知道这个问题，但只在一个规则里解决了

`WidgetColorLiteralAudit` 专门处理了别名与 static import：

- `_colorAliases` (WidgetColorLiteralAudit.cs:144)
- `using C = UnityEngine.Color;` 收集别名 (L158-165)
- 别名参与对象创建 / 成员访问 / 强转判定 (L173, L189, L229)
- `using static Color;` 裸调色板名判定 (L292-307)

**但同样的手段没有推广到其它规则**，于是：

```csharp
using U = UIFactory;
U.CreateText(t, "PointingValue", "EARTH POINTING", 9, ...);   // expr = "U.CreateText"
// → 不匹配 DisplayTextSlots["UIFactory.CreateText"] → 英文文案漏检

using L = MFPLogger;
L.Info("中文日志");    // expr = "L.Info"
// → IsLogOrDiagnosticCall 只认 "MFPLogger." 前缀 → 不豁免 → 误报硬编码中文
```

同理，任何项目内包装函数（`SafeFind()` 包 `FindObjectOfType`）对 SPEC-007 完全隐形。

**修法（短期）**：在扫描前建立 `using` 别名映射表（I18n 侧已经在收集 static import，可复用），把 `expr` 规范化成真实限定名后再查表。
**修法（中期）**：见 §4。

---

## 3. P2 — 可维护性腐化风险

### 3.1 方法体量：if 数的真正来源

| 方法 | 行数 | if 数 |
|---|---|---|
| `WidgetModernizationAudit.Scan(WidgetDiscoveryResult)` (L151-523) | **370** | 41 |
| `WidgetSourceAudit.SelfTest()` (L808-1036) | 230 | — |
| `WidgetSourceAudit.ScanTelemetryAssembly()` (L581-732) | 157 | 深嵌套 4 层 |
| `WidgetSpecificationValidator.ValidateWidgetType()` (L325-463) | 142 | 7 条顺序规则 |

**if 多不是设计问题，是"没有规则对象抽象"的结果**——每条规则都内联在一个大方法里，规则数增长 → 单方法线性膨胀。

**修法**：引入规则对象

```csharp
public interface IWidgetRule
{
    string Code { get; }
    string Severity { get; }
    IEnumerable<WidgetSourceViolation> Check(WidgetClassNode node, WidgetClassGraph graph);
}
```

在 `WidgetSpecRules` 里登记成表。附带收益：天然产生"逐规则正反用例"的对齐点，`SelfTest` 不必再手写 30 个检查。

### 3.2 `WidgetModernizationAudit` 全程只产出 WARNING，进程永远 `return 0`

- `GenerateMSBuildOutput` 输出全部是 `warning MFP_...` (WidgetModernizationAudit.cs:532-543)
- `tools/HeadlessValidator/Program.cs:182-191`：`--audit-legacy` / `--audit-modernization` 模式执行完 **直接 `return 0`**

所以 90+ 个反模式指标（裸 `new GameObject`、高频循环堆分配、私有格式化方法重复、`DockAnchorTracker.SyncAll` 私调……）**不阻塞任何门禁**。

如果这是有意的"顾问模式"，建议在文件头写明契约；如果不是，应把 `HasUnmanagedCore3DUgui`、`RawGameObjectAllocs > 0` 这类升级为参与 `overallErrors`。

### 3.3 双模态 `#if` 造成的空自增

`WidgetSpecificationValidator.ValidateWidgetType` 规则 7 (L461-462)：

```csharp
// 规则 7: 零场景查询 (反射计数对齐，真实扫描在源码阶段执行)
report.TotalChecksPerformed++;
```

不做任何检查，只累加计数。报告里"已执行规则检查"这个数字因此包含空自增项。建议要么真做（反射能查 `Camera.main` 之类静态属性的引用是查不到的，那就不该计数），要么在摘要里标注该项为占位。

### 3.4 门禁靠手动触发

仓库无 `.github/workflows`，`scripts/` 只有一个 `sync_to_unity.ps1`。`docs/PROJECT_ENGINEERING_REVIEW.md:60` 已经指出过这一点。自检确实接进了 `HeadlessValidator` [7/10] 并会 `return 1`，但**没有任何自动化在提交时跑它**——审计器的可靠性最终取决于"人记得跑"。

---

## 4. 值得肯定之处（不是客套）

这几条是同类项目里很少见、且我在复核中逐条验证过的：

1. **用 AST 而非正则**。天然免疫注释 / 字符串字面量 / 内插洞混淆，且**有正反用例证明**（`WidgetSourceAudit.SelfTest` L861-873：`@"// FindObjectOfType<Camera>()"`、`@"string s = ""FindObjectOfType"";"` 必须不报）。
2. **判定数据表化**。`SceneQueryApis`(WidgetSpecRules.cs:95-115) / `DisplayTextSlots`(I18nSyntaxAuditor.cs:690-707) / `SceneQueryStaticImportOwners`，扫描与自检共用同一份口径，从结构上排除了"两份正则各说各话"。
3. **主动拒绝假绿**。`WidgetDiscoveryStatus` + `Report.SourceAuditExecuted`(WidgetSpecificationValidator.cs:41) + `GenerateSummary` L86-100 那段"源码不可见时绝不输出 100% 合规"，并显式打印"未执行原因"。**这是很多商业审计工具都不做的一步。**
4. **棘轮双表**。`BaselineTable` + `RatchetCeilingTable` 让技术债只降不升，`ValidateRatchet` 有正反自检。（当前两表均为空 = 零容忍，是健康状态。）
5. **自检真接进了门禁**。`Program.cs:406-425` 的 [7/10] 步把 4 组 `SelfTest()` 失败累加进 `overallErrors` → `return 1`。不是摆设。用例条数还刻意由内核回传（避免写死数字漂移）。
6. **主动修掉过自己的洞**。`IsExemptI18nSlot` 从"祖先里有 I18n 就算"改成"按实参槽位判定"，堵掉了 `TrFormat("K", 1, "中文参数")` 的漏检，并留了回归用例 7。
7. **颜色强转判定收紧了**。`ProducesColorFromLiteral`(WidgetColorLiteralAudit.cs:252-287) 从"只认 `(Color)new Vector4(...)`"扩到"操作数子树里有数值字面量或向量构造"，堵掉了 `(Color32)0xFFFFFF` 整类漏检。

---

## 5. 建议的加固顺序（按 ROI）

| # | 动作 | 目标 | 量级 |
|---|---|---|---|
| 1 | 增加 `SourceParseFailed` 状态并接入门禁 | 堵住唯一的"静默全绿"通道 | 半天 |
| 2 | 三个"任意祖先"豁免判定统一为"最近宿主" + 3 条反例用例 | 消除中文零容忍盲区 | 半天 |
| 3 | `ScanTelemetryAssembly` 参数下标改为按参数名解析 + 反射校验自检 | 让重构时门禁会响 | 1 天 |
| 4 | `ByName` 支持同名多候选（消歧或告警） | 防止继承链错位导致规则整体错判 | 半天 |
| 5 | 规则体裸字面量回收进 `WidgetSpecRules` | 让"单一声明点"名副其实 | 1 天 |
| 6 | 引入 `SemanticModel`（`tools/` 下有 66 个 dll 可作元数据引用），ban-list 判定从"文本后缀匹配"改为"符号归属判定" | **唯一能同时解决 `using` 别名绕过、wrapper 绕过、重载下标三个问题的手段** | 3–5 天 |
| 7 | 建立 CI（至少 `dotnet build` + `HeadlessValidator`） | 让上述所有护栏真正生效 | 1 天 |

第 6 条是把"工业级"三个字兑现的唯一路径：现在的判定全部依赖 `node.ToString()` 的字符串形状（`expr.EndsWith(".SetPixels32")`、`expr.StartsWith("UIFactory.")`、`obj.Type.ToString().Contains("Image")`……），这是本目录所有漏报的共同根因。在引入语义模型之前，每加一条规则都只是在同一个薄弱层上再加一个 `if`。

---

## 附：本次复核的一处方法说明

`WidgetColorLiteralAudit.BaselineTable`(L30) 与 `I18nSyntaxAuditor` 的英文基线表(L208) 均为空、对应 `RatchetCeilingTable` 亦为空，因此这两处的 `ValidateRatchet()` 恒返回空集合——**该两处棘轮机制处于"未启用"状态**。这不构成缺陷（零容忍是更好的状态），但意味着该机制在生产路径上没有被真实数据验证过，仅靠 `SelfTest` 用空表断言"返回 0 失败"。若后续要登记新欠账，建议同时加一条"非空基线下棘轮能拦住上调"的正向用例。

> **更正**：`I18nDictionaryValueAudit.BaselineTable`(L965) 并非空表 —— 它登记了 `{ "zh-CN.json", 2 }`（两条无语言信息的记法/专名），对应的 `RatchetCeilingTable` 同样为 2。**词典未汉化棘轮是活跃的、有真实数据在跑的**，实测运行也确认"2 条存量 / 基线 2 条"。上文初稿把它与另两处空表混为一谈，此处更正。

---

## 6. 实施进度（2026-09-28）

按「止血 → 根因基建 → 根因修复 → 兜底校验 → 结构整理」五阶段推进，**阶段 0 已完成并通过验证**。

### 6.1 已落地

| 编号 | 修复内容 | 验证方式 | 结果 |
|---|---|---|---|
| #1 | **解析失败守卫**：新增 `WidgetDiscoveryStatus.SourceParseFailed` 与 `WidgetDiscoveryResult.ParseErrors`；新增 `RoslynAstHelper.ParseTreeChecked`（只取语法诊断，不建 Compilation，故不会因缺少类型引用误报） | 注入探针文件 `var x = ;` 后运行门禁 | 状态 `SourceParseFailed`，报 `__ParseGuardProbe.cs L1:C86 CS1525`，**退出码 1**，并明确输出"未取得可审计的组件源码，源码级规则完全未执行" |
| #2 | **豁免判定统一为"最近宿主"**：`IsInsideEngineResourceOrPathCall` / `IsInsideStringComparison` / `IsInsideMicroControlRegistration` 三条由"任意祖先"改为"最近宿主 + 字符串组装透传" | 新增用例 14（3 处嵌套调用内中文必须检出）+ 用例 15（3 处直接实参必须放过） | I18n 用例 14 → **16 条全绿**；真实仓库硬编码中文计数 **43 处不变**（潜在漏洞关闭且零回归） |
| #3 | **裸字面量回收**：`BaseSize` / `AutoCreateCardFrame` / `UIFactory.` / `_texPixels` / `MicroControlTypes` 8 类名 / `Sync` / `_last*` / `GameObject` / `OnInitialize` / `RefreshTier` / `ApplyTheme` / `OnUpdateTelemetry` / `OnDestroy` / `Update` / `Vessel·Part·CelestialBody` / I18n 调用名等全部回收进 `WidgetSpecRules`（新增约 40 个常量 + 3 张表） | 全量运行 + 现代化审计 | 输出与修复前一致；`MicroControlTypes` 现由 `WidgetSpecRules.MicroControlDslTypes` 派生 |
| #4 | **构造函数签名表 + 双向交叉校验**：`ControlCtorShapes` 表；`ScanTelemetryAssembly` 改查表；未登记形状显式上报；`VerifyControlCtorShapes` 从被扫描源码派生真实构造函数声明并反向校验 | **突变测试**：把 Readout 12 参的 `TokenIndex` 由 9 改为 6 | 立即报出 2 条 `MFP-KERNEL-CONTRACT-DRIFT`（正向："第 7 个形参现在是 `title`"；反向："存在带 `token` 形参的 12 参重载但未登记"），**退出码 1**；还原后恢复 0 错误 |
| #5 | **继承图同名类消歧**：`ByName` 改为多候选 + `ByFullName` 索引；`ResolveBase` 按全限定名 → 同命名空间逐级消歧，仍无法唯一确定则上报 `MFP-KERNEL-INHERIT-AMBIGUOUS` | 新增用例 16：跨命名空间同名基类无法消歧必须上报；写全限定名时必须不误报 | 自检全绿；真实仓库 **0 条歧义**（无同名类，无假阳性） |
| #6 | **去重状态修复**：`lastReportedLine` 跨两次遍历共享 → 改为 `HashSet`，键为「行号 + 表达式文本」 | 新增用例 19：同一行两个不同场景查询必须各报一条 | 自检全绿（旧实现此处会漏报第二条） |
| 附 | `tools/HeadlessValidator` 新增 `--self-test` 独立入口 | — | 不必跑完整 10 步链路即可回归审计内核 |

### 6.2 回归证据

```
全量运行:     退出码 0 · 46 个组件 · 78 条 MFP-WARN-TELEM-ASSEMBLY · 0 ERROR
内核自检:     规则 77 条 + 颜色 20 条 + I18n 语法树 16 条 + I18n 词典 3 条 全部通过
I18n 存量:    硬编码中文 43 处 · 未登记键名 52 处 · 可汉化英文 0 处（均与修复前一致）
现代化审计:   43 ModernDSL / 2 Core3D / 0 Legacy（100%，与修复前一致）
新增内核规则: MFP-KERNEL-CONTRACT-DRIFT 与 MFP-KERNEL-INHERIT-AMBIGUOUS 在真实仓库上均 0 触发
```

78 条 `MFP-WARN-TELEM-ASSEMBLY` 均为**修复前既有存量**（控件未绑定遥测 Token 的装配警告），非本次引入。

### 6.3 尚未开始

| 编号 | 内容 | 阻塞于 |
|---|---|---|
| #10 | **语义层基建** `SemanticAuditContext`（根因，你补的缺口） | — 可立即开工 |
| #11 | 规则迁移到符号判定（SPEC-007 / 颜色 / I18n / 微控件构造） | #10 |
| #7 | using 别名映射（定位为 Degraded 模式兜底） | #10 |
| #8 | 现代化审计门禁契约（当前全程 WARNING 且进程恒 `return 0`） | — |
| #9 | `IWidgetRule` 规则对象抽象，拆分 370 行 `Scan` | — |

### 6.4 实施中修正的两处自身失误（记录备查）

1. **`obj.Type.ToString().Contains("Image")` → 误改为 `GetSimpleTypeName(...) == "Image"`**。这会漏掉 `RawImage`，属于把"常量回收"做成了"判定收紧"。已回退为 `.Contains(...)` 并加注释说明此处按类型文本包含匹配。
2. **`IsAnyLookup` 一度把 `GetWidgetName` 并入缺键校验**。`GetWidgetName(widgetId, ...)` 的首参是组件注册 id、不是词典键，并入会误报。已拆分为 `IsSlotHost`（槽位豁免，含 GetWidgetName）与 `IsDictionaryKeyLookup`（缺键校验，不含）。

两条都是"看似等价的重构实际改变了语义"，说明本目录的常量回收必须逐条比对判定口径，不能靠命名直觉。

### 6.5 环境备注

- **`git` 在本会话的沙箱下不可用**（`git status` 报 `not a git repository`，而 `ls .git/` 正常）。因此原计划的"stash 前后 A/B 对比"改为**突变测试**：直接构造违规输入、确认门禁报错、再还原。对"证明守卫有效"而言这比 A/B 更强。
- 编译需带环境变量（详见 `ksp-mod-dotnet-build` skill）：
  `env 'PROGRAMFILES(X86)=C:\Program Files (x86)' 'PROGRAMFILES=C:\Program Files' dotnet build tools/HeadlessValidator/HeadlessValidator.csproj`
- `GameData/ModularFlightPanel/Plugins/*.dll|pdb` 在本会话开始前即为已修改状态，本次未触碰插件构建。

### 6.6 与本次审计无关的并发改动（重要，避免误判）

复核过程中检测到**有会话外的进程正在实时编辑本仓库**（非本次审计所为）。证据：

```
10:07:02  src/ModularFlightPanel/UI/Widgets/Navigation/HeadingArcWidget.cs
10:07:24  src/ModularFlightPanel/Config/WidgetLayoutManager.cs
          （随后 LayoutShareHub.cs 亦被写入）
```

`HeadingArcWidget.cs` 的 mtime 在两次连续命令之间从 10:06 变为 10:07，且其内容比 `unity/` 镜像新（新增 `BaseSize`、`ARC_Y_CENTER_OFFSET`、`TextWidget` DSL 字段）—— 属于开发中的重构。

**因此全量校验的退出码为 1，唯一失败项是 3 条 `镜像漂移`（src 已改、`unity/` 预览镜像未同步）**，与审计内核无关：

```
✘ [FAIL] 镜像漂移: Config/LayoutShareHub.cs
✘ [FAIL] 镜像漂移: Config/WidgetLayoutManager.cs
✘ [FAIL] 镜像漂移: UI/Widgets/Navigation/HeadingArcWidget.cs
```

同一提交上审计内核自身的结果是 **`[6/10] 规范合规审计 100% 通过 (46 个组件类完全合规)` / ERROR 0 / WARNING 78 / `[7/10]` 内核自检全绿**。

**处置建议**：不要在此刻执行 `--mirror-fix`。它会把预览镜像对齐到**尚未完成的** src 状态；等这批重构告一段落、编译通过后再同步更稳妥。本报告不代替仓库所有者做这个决定。


