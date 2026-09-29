# SemanticCompilation 落实审计报告

审计对象：`src/ModularFlightPanel/UI/Auditing/`（重点 `SemanticCompilationProvider.cs`）
审计日期：2026-09-28
审计方式：源码走读 + 门禁实跑 + 独立对照探针（`scratch/semantic_probe/`）

---

> 注：本报告 §四/§五 描述的缺陷**已全部修复**，修复记录与验收证据见文末 §七。

---

## 一、总体结论

**语义编译已真实落地并在门禁中生效，不是空壳。** 但它建立在一个**不合格的编译单元**之上：
编译选项缺少 `KSP_RUNTIME` 预处理器符号，导致 2217 个编译错误与一处**影子核心类型**被静默注入，
使得"绝对权威的类型图"实际是一个带错误兜底的类型图。

| 维度 | 结论 |
| --- | --- |
| 是否落地 | ✅ 是。门禁实跑输出 `Full L4 真实符号语义与常量折叠激活`，链接 17 个 Unity/KSP 程序集 |
| 是否被真实消费 | ✅ 是。5 个判定点位使用 `SemanticModel`，且能捕获语法表抓不到的违规（实测见 §三） |
| 编译单元是否干净 | ❌ 否。2217 个 ERROR，含 `UnityEngine.Vector2` 重复定义 |
| 是否自带回归保护 | ❌ 否。121 条自检用例**全部**跑在无语义回退路径上 |
| 降级是否可感知 | ⚠️ 仅打印一行提示，不影响退出码，无规则以 `IsFullSemanticActive` 为门 |

---

## 二、落位架构（设计正确，值得保留）

Roslyn 不进游戏插件，审计内核由两个项目共享同一份源码，**零副本漂移**：

- `src/ModularFlightPanel/ModularFlightPanel.csproj` 用 `<Compile Remove>` 剔除 6 个 Roslyn 依赖文件
  （`SemanticCompilationProvider.cs` / `RoslynAstHelper.cs` / `WidgetSourceAudit.cs` /
  `WidgetModernizationAudit.cs` / `WidgetColorLiteralAudit.cs` / `I18nSyntaxAuditor.cs`）
- `tools/HeadlessValidator/HeadlessValidator.csproj`（net6.0 + `Microsoft.CodeAnalysis.CSharp 4.0.1`）
  以 `<Compile Include>` 回指这些源文件
- `WidgetSpecRules.cs` 双端编译，保证规则码/黑名单表只有一份定义

### 生效证据（`test-ui.ps1` → HeadlessValidator 实跑）

```
[6/10] 全量飞行仪表组件架构与代码规范合法性校验
  ├─ 扫描范围: src/ModularFlightPanel 全量源码 170 个文件 → 组件类 46 个 (作用域文件 46 个)
  ├─ 编译语义模型: 成功链接 17 个外部 Unity/KSP 程序集 (Full L4 真实符号语义与常量折叠激活)
...
✔ [ALL CHECKS PASSED]
```

### 语义实际被消费的 5 个点位

| # | 位置 | 用途 |
| --- | --- | --- |
| 1 | `WidgetSourceAudit.cs:154,176-179` | 每文件 `SemanticModel` → 每类 `GetDeclaredSymbol` → `node.Symbol` |
| 2 | `WidgetSourceAudit.cs:203-214` | 继承链解析优先走 `Symbol.BaseType` 语义决议，失败才回退语法消歧 |
| 3 | `WidgetSourceAudit.cs:402-408` | `IsDescendantOfContractRoot`：有 `Symbol` 时**独占**语义结论 |
| 4 | `WidgetColorLiteralAudit.cs:182-189 / 211-219` | SPEC-006：`GetTypeInfo` / `GetSymbolInfo` 判 `Color`/`Color32` |
| 5 | `WidgetSourceAudit.cs:1145-1152 / 1175-1182` | SPEC-007：`GetSymbolInfo` + `IsSceneQuerySymbol` 判场景查询 |

---

## 三、语义层的真实增量（实测，非宣称）

探针用例 C：用别名绕开表驱动匹配

```csharp
using UnityEngine;
using GO = UnityEngine.GameObject;
class Q { void M() { var o = GO.Find("HUD"); } }
```

```
表达式                  = GO.Find
语义符号 ContainingType = UnityEngine.GameObject
语义判定(IsSceneQuerySymbol) = True
语法判定(IsSceneQueryApi)    = False   ← 表驱动回退漏检
```

**结论：L4 层不是装饰，它补上了语法表在"类型别名/命名混淆"下的结构性漏检。**
这条同时也说明——后面 §四 的 P0 缺陷恰好打在最有价值的通道上。

---

## 四、缺陷清单

### P0-1　语义编译缺少 `KSP_RUNTIME`，注入影子类型并造成类可见性漂移

`SemanticCompilationProvider.BuildCompilation`（`SemanticCompilationProvider.cs:296-299`）：

```csharp
var parseOptions = new CSharpParseOptions(
    languageVersion: LanguageVersion.Latest,
    documentationMode: DocumentationMode.None      // ← 没有 WithPreprocessorSymbols("KSP_RUNTIME")
);
```

而回退路径 `RoslynAstHelper.DefaultOptions`（`RoslynAstHelper.cs:16-19`）**显式定义了该宏**。
插件本体（`ModularFlightPanel.csproj`）也定义了 `KSP_RUNTIME`。**三处口径，两套语义。**

#### 后果 1：影子 `UnityEngine.Vector2` 被编译进来

`Config/WidgetConfig.cs:194-205` 存在测试垫片：

```csharp
#if !KSP_RUNTIME && !UNITY_5_3_OR_NEWER && !UNITY_EDITOR
namespace UnityEngine { public struct Vector2 { ... } }
#endif
```

语义编译里 `KSP_RUNTIME` 未定义 → `!KSP_RUNTIME` 为真 → 垫片生效，
与 `UnityEngine.CoreModule.dll` 里真实的 `Vector2` **重名冲突**：

```
诊断: ERROR = 2217 ; WARNING = 0
  CS0029  1690   ← 全部形如：
    无法将类型"UnityEngine.Vector2 [src\...\WidgetConfig.cs(197)]"隐式转换为
           "UnityEngine.Vector2 [...\UnityEngine.CoreModule.dll]"
  CS0246   253
  CS1503    88
  CS0019    65
  CS0117    48
  CS0103    33
```

门禁从不调用 `GetDiagnostics()`，因此**这 2217 个错误对使用者完全不可见**，
而 `IsFullSemanticActive == true` 只反映"Managed 目录探测成功"，不代表"编译干净"。

#### 后果 2：部分代码在语义树里"消失"

对照实验（同一份源码，两套 parseOptions）：

| 对象 | 语义树（门禁实际使用） | 语法树（回退路径） |
| --- | --- | --- |
| `DockAnchorTracker.cs` 类声明数 | 2 | 7 |
| `StockToolbarHook.cs` 类声明数 | 1 | 2 |
| 全仓库类声明合计 | **267** | **273** |

合成用例（`#if KSP_RUNTIME` 内放一处 `new Color(...)`）：

```
语义树 ObjectCreation 节点数 = 1
语法树 ObjectCreation 节点数 = 2
CountOccurrences(text, semanticModel) = 1
CountOccurrences(text, null  回退)    = 2
期望值 = 2
```

→ **语义路径比语法回退少报 1 处真实违规。**

#### 后果 3：组件作用域文件里已存在 500+ 行受宏保护代码

| 文件（均为作用域内组件） | 受 `#if` 保护块 | 最大块 |
| --- | --- | --- |
| `UI/Widgets/Controls/ModernToolbarWidget.cs` | 8 块 | **L450-906，455 行** |
| `UI/Widgets/Controls/FavoriteToolbarWidget.cs` | 9 块 | L464-522，57 行 |
| `UI/Widgets/Navigation/NavballSphereWidget.cs` | 1 块 | L1349-1368 |

这些区域对 SPEC-006（颜色字面量）与 SPEC-007（场景查询）**完全不可见**。
当前经全量扫描确认其中尚未埋有被禁 API，**属潜伏盲区而非已发生的漏检**；
但 `WidgetClassGraph.Build:144` 是"语义树优先、语法树兜底"，
意味着一旦有人在受保护块里加一行 `new Color(...)` 或 `Camera.main`，
门禁会**静默放行**——而带宏的语法诊断树只报 ERROR 级语法问题，不会提示可见性漂移。

#### 已验证的修复效果

探针 F 复刻同一构建流程，仅补 `preprocessorSymbols: new[] { "KSP_RUNTIME" }`：

```
修复后 诊断 ERROR = 323   (修复前 2217，↓85%)
修复后 类声明合计 = 273   (与语法树完全一致，漂移消除)
```

**一行改动同时解决三个后果。**

---

### P0-2　121 条自检用例全部跑在无语义回退路径上

`--self-test` 实跑输出：

```
✔ 审计内核自检通过: 规则 80 条 + 颜色字面量 22 条 + I18n 语法树 16 条 + I18n 词典值 3 条
```

覆盖分析：

- `WidgetColorLiteralAudit.SelfTest`（`WidgetColorLiteralAudit.cs:415-471`）的
  `expectCount` 一律调用 `CountOccurrences(snippet)`，**不传 `SemanticModel`** →
  `ColorLiteralAstWalker._semanticModel == null` → `GetTypeInfo` / `GetSymbolInfo` 分支永不执行。
- `WidgetSourceAudit.SelfTest` 的合成用例走
  `Scan(IEnumerable<WidgetSourceFile>)` → `WidgetClassGraph.Build(list)`
  （`WidgetSourceAudit.cs:670-674`），`semanticContext` 取默认值 `null` →
  继承链语义决议与 `IsDescendantOfContractRoot` 的语义分支、`ScanWidgetFile` 的
  `semanticModel` 分支**全部为 null**。
- 唯一的语义自检 `SemanticCompilationProvider.SelfTest`（`SemanticCompilationProvider.cs:442-495`）
  只对一个 6 行合成源验证 `InheritsFrom` 一条路径，
  不覆盖：`IsSceneQuerySymbol` / `IsColorOrColor32` / 程序集链接失败降级 / 预处理器宏行为。

**净效果：把 §二 表格里 1～5 号语义点位全部改坏，门禁依然全绿。**

---

### P1-1　`--color-baseline-dump` 与门禁不同源

`Program.cs:1216`：`WidgetColorLiteralAudit.CountOccurrences(File.ReadAllText(file))` —— 不传语义模型。
于是 dump 走**带 `KSP_RUNTIME` 宏**的语法树，门禁走**无该宏**的语义树。
两端口径不一致时，导出的棘轮基数可能偏离门禁实测值，而基数是"只降不升"的，
一旦偏低会把后续合法提交卡成违规。

### P1-2　降级不可感知

`IsFullSemanticActive` 全仓库只有一处消费：`Program.cs:530-537` 的打印分支。
没有任何规则、也没有退出码以它为门 —— 主机环境降级为"仅 .NET 基础程序集"时，
审计结论静默改变而最后仍然打印 `ALL CHECKS PASSED`。

### P1-3　语义只做"或"叠加，未做权威替换

`WidgetColorLiteralAudit.cs:182-194` 与 `211-224`：

```csharp
if (_semanticModel != null) { ...语义判定，命中则 isColor = true; }
if (!isColor) { ...语法名匹配兜底... }
```

语义结论只用于**扩大**判定，从不用于**否决**。与 `roslyn` 化的宣称
（"彻底消除基于文本正则与字符清洗的脆弱性"）不符。

具名反例：`VisitIdentifierName`（`:378-399`）在存在 `using static UnityEngine.Color;` 时，
对**任何**名为 `red`/`green`/`blue`/`white`/... 的标识符报违规，
不区分它是静态调色板属性还是局部变量/字段。语义模型本可区分 `ILocalSymbol`
与 `IPropertySymbol`，但该分支完全没用语义。自检里只有正例
（`using static UnityEngine.Color;\nvar c = white;` → 1），**没有反例**。

### P2　其他

1. `VisitCastExpression`（`WidgetColorLiteralAudit.cs:261-277`）完全无语义参与，纯 `GetSimpleTypeName` + 同文件别名表。
2. 语义编译把 `UI/Auditing/**` 自身也编了进去（`Discover` 扫全目录），
   这些文件 `using Microsoft.CodeAnalysis` 而 `CollectMetadataReferences` 不提供该引用 →
   253 个 `CS0246`。补齐宏后残留的 323 个错误基本由这批文件贡献。
3. 编译结果从不体检（无 `GetDiagnostics`），无"错误类型占比"健康度指标，
   无法区分"真实符号决议"与"错误类型兜底"。
4. `pathMap` 以"先到先得"去重（`SemanticCompilationProvider.cs:315-322`），
   跨目录同名文件会静默让先注册者胜出。
5. `IsContractRoot` 仅按类简名匹配（`WidgetSourceAudit.cs:170`），
   跨命名空间同名 `BaseFlightWidget` 会被误认作契约根。
6. MSBuild 阶段钩子（`ModularFlightPanel.csproj` 的 `AuditWidgetModernization`，
   `AfterTargets=CoreCompile`）只调用 `WidgetModernizationAudit`，而后者**不使用语义层**。
   即：每次构建跑的门禁不含 L4，只有 `test-ui.ps1` 跑完整链路时才生效。

---

## 五、修复建议（按性价比排序）

1. **【一行·P0】** `BuildCompilation` 的 `CSharpParseOptions` 补 `preprocessorSymbols: new[] { "KSP_RUNTIME" }`。
   更稳妥的做法是把 parseOptions 提成一个单点常量，由 `RoslynAstHelper.DefaultOptions`
   与 `BuildCompilation` 共用，杜绝口径分裂。已验证：错误 2217 → 323，类声明漂移归零。
2. **【P0】** 给语义路径补自检：
   - `WidgetColorLiteralAudit.SelfTest` 增加语义通道（该文件只在 headless 侧编译，可直接用 Roslyn 类型），
     至少覆盖一条 `#if KSP_RUNTIME` 内违规、一条类型别名；
   - `WidgetSourceAudit.SelfTest` 补 `SemanticCompilationProvider.BuildCompilation` 参与的用例，
     验证 `Symbol.BaseType` 决议与 `IsDescendantOfContractRoot` 的语义独占分支；
   - 补 `using static UnityEngine.Color` + 同名局部变量 的反例。
3. **【P1】** `DumpColorLiteralBaseline` 改走 `Discover()` + `SemanticContext` 同一入口，与门禁同源。
4. **【P1】** 把 `IsFullSemanticActive` 纳入报告摘要；CI 模式下降级应计入错误数。
5. **【P2】** 语义编译排除 `UI/Auditing/**`（或补 Roslyn 引用），并在 `IsFullSemanticActive`
   之外增加 `CompilationErrorCount` 指标，作为"语义是否真的可用"的硬信号。

---

## 六、复现方式

```bash
# 1) 门禁全链路（含语义状态打印）
dotnet tools/HeadlessValidator/bin/Debug/net6.0/HeadlessValidator.dll

# 2) 自检条数
dotnet tools/HeadlessValidator/bin/Debug/net6.0/HeadlessValidator.dll --self-test

# 3) 对照探针（A~F 六组实验，含修复效果验证）
cd scratch/semantic_probe
dotnet build -c Release
dotnet bin/Release/net6.0/SemanticProbe.dll "C:\Users\43701\Documents\github\KSP_naviball"
```

> 注：Git Bash 下 `dotnet build` 需补齐 `PROGRAMFILES` / `PROGRAMFILES(X86)` / `PROGRAMDATA`
> 等环境变量，否则 NuGet 报 `Value cannot be null. (Parameter 'path1')`。

---

## 七、修复记录（2026-09-28 落地）

### 7.1 逐条修复

| 编号 | 缺陷 | 修复 | 涉及文件 |
| --- | --- | --- | --- |
| P0-1 | 语义编译缺 `KSP_RUNTIME`，注入影子 `Vector2` + 类可见性漂移 | 新增 `RoslynAstHelper.UnifiedParseOptions` 作为**全仓唯一解析口径**（含 `WidgetSpecRules.RuntimePreprocessorSymbol`），`BuildCompilation` 与 `ParseRoot` 共用同一实例 | `RoslynAstHelper.cs`、`SemanticCompilationProvider.cs`、`WidgetSpecRules.cs` |
| P0-1b | 语义编译混入 `UI/Auditing/**`（引用 Microsoft.CodeAnalysis 而引用列表无此程序集） | `WidgetSpecRules.PluginExcludedAuditFiles` 单点声明，`Discover` 排除；新增 `ParseCompileRemoveFileNames` 解析 csproj，内核守卫逐项对照两份清单，任一单侧增删即 ERROR | `WidgetSpecRules.cs`、`WidgetSourceAudit.cs` |
| P0-1c | 从不调 `GetDiagnostics()`，"链接成功"被误当成"编译干净" | `SemanticCompilationContext` 新增 `CompilationErrorCount` / `ErrorCountForFile`；新增内核守卫 `MFP-KERNEL-SEMANTIC-UNHEALTHY`（错误数超棘轮上限，或**任一组件作用域文件**自身带错误 → ERROR）；门禁与基线导出均打印体检行 | `SemanticCompilationProvider.cs`、`WidgetSpecRules.cs`、`WidgetSourceAudit.cs`、`Program.cs` |
| P0-2 | 121 条自检全部跑在无语义回退路径 | 自检增至 **144 条**；新增"覆盖率计数"体系（`SemanticVerdictCount` / `SemanticInvocationVerdictCount` / `SemanticMemberAccessVerdictCount` / `SemanticResolvedBaseCount` / `SemanticDescendantVerdictCount` / `LastSemanticConsultCount`），把"语义点位还在不在"变成可断言的事实 | 全部审计文件 |
| P1-1 | `--color-baseline-dump` 与门禁不同源 | 改走 `Discover()` 的同一份语义上下文，并显式打印口径来源；若拿不到上下文则警告"禁止据此登记棘轮基数" | `Program.cs` |
| P1-2 | 语义降级只打印一行、不影响结论 | 新增 `MFP-KERNEL-SEMANTIC-DEGRADED`（无上下文 / 未链接 Unity 程序集时进报告，WARNING） | `WidgetSourceAudit.cs`、`WidgetSpecRules.cs` |
| P1-3 | 语义只做 OR 叠加；`using static Color` 下同名局部变量被误报 | `VisitIdentifierName` 引入 `CouldBeColorPaletteMember` 语义否决（局部变量/形参/非 Color 成员一律否决；**符号解析不出时保持保守照报**）；`VisitCastExpression` 改用 `GetTypeInfo` 判定目标类型 | `WidgetColorLiteralAudit.cs` |
| P2 | `pathMap` 同名文件先到先得 | 简名索引改为"仅无歧义时注册"，歧义简名不进索引（调用侧走完整路径） | `SemanticCompilationProvider.cs` |

### 7.2 量化结果

| 指标 | 修复前 | 修复后 |
| --- | --- | --- |
| 语义编译诊断 ERROR | **2217**（CS0029 1690 处为影子 Vector2 冲突） | **0** |
| 语义树 / 语法树 类声明数 | 267 / 273（漂移 6） | 273 / 273（一致） |
| 语义编译文件数 | 170（含 6 个无关审计文件） | 164（与插件真实编译集一致） |
| 自检用例 | 80 + 22 + 16 + 3 = **121** | 96 + 29 + 16 + 3 = **144** |
| 语义路径覆盖 | 0 条 | 23 条 |
| 门禁退出的可信度 | 语义层全拆也全绿 | 见 7.3 |

### 7.3 突变验收（关键证据）

逐个拆掉 5 个语义点位（部分点位拆到子分支），确认 `--self-test` **必须变红**：

```
基线（未突变）                                    自检退出码 = 0
[突变] 点位1 Symbol 赋值（GetDeclaredSymbol）       退出码 = 1  变红 ✔
[突变] 点位2 继承链 Symbol.BaseType 语义决议        退出码 = 1  变红 ✔
[突变] 点位3 契约归属的语义独占分支                 退出码 = 1  变红 ✔
[突变] 点位4 SPEC-006 颜色 GetTypeInfo/GetSymbolInfo 退出码 = 1  变红 ✔
[突变] 点位5a SPEC-007 场景查询 · 调用分支          退出码 = 1  变红 ✔
[突变] 点位5b SPEC-007 场景查询 · 成员访问分支      退出码 = 1  变红 ✔
还原后                                            自检退出码 = 0
```

- 复跑脚本：`scratch/semantic_probe/mutation_acceptance.py`（自动备份/突变/构建/自检/还原）。
- 点位 5 最初**未变红**——因为只拆调用分支时，成员访问分支仍捕获同一处违规且被去重合并。
  这说明"合并计数"无法证明两个子分支都还在，于是拆成两个独立计数器，现在两分支各自受守卫。
- 脚本注意事项：还原必须用 `shutil.copy` + `os.utime`，**不能用 `shutil.copy2`**
  （保留旧 mtime 会让 MSBuild 判定无变更、跑在突变版 DLL 上，产生假红）。

### 7.4 修复后的门禁实跑

```
[6/10] 全量飞行仪表组件架构与代码规范合法性校验
  ├─ 扫描范围: src/ModularFlightPanel 全量源码 164 个文件 → 组件类 46 个 (作用域文件 46 个)
  ├─ 语义编译集: 已排除 6 个仅无头侧编译的审计文件 (与插件 csproj 的 <Compile Remove> 逐项一致)
  ├─ 编译语义模型: 成功链接 17 个外部 Unity/KSP 程序集 (Full L4 真实符号语义与常量折叠激活)
  ├─ 语义编译体检: 诊断 ERROR 0 (棘轮上限 0)
✔ 审计内核自检通过: 规则 96 条 + 颜色字面量 29 条 + I18n 语法树 16 条 + I18n 词典值 3 条 全部符合预期
✔ [ALL CHECKS PASSED]   （退出码 0）

--color-baseline-dump 现在自带口径声明：
  基线口径与门禁一致: 语义模型 Full L4 激活 / 已链接 17 个程序集 / 编译诊断 ERROR 0
```

`src/ModularFlightPanel/ModularFlightPanel.csproj`（net472）同步构建通过（0 错误），
确认 `WidgetSpecRules.cs` 的改动符合"双端编译、不得引用 Roslyn 类型"的约束。

---

## 八、全面语义穿透化改造（2026-09-29）

目标口径：**抛弃所有老旧的审计模式，全面使用穿透性审计直达 Unity 底层父类与 C# 底层；
保留审计核心，移除老旧低效代码。**

### 8.1 逐项落地

| 项目 | 改造前 | 改造后 |
| --- | --- | --- |
| `WidgetFieldPenetrationAudit` | `File.ReadAllText` + `ParseRoot`，字段类型靠 `GetSimpleTypeName` 字符串；只遍历 `classDecl.Members` | 新增 `Scan(WidgetDiscoveryResult)` 语义通道；字段类型由 `IFieldSymbol.Type` 决议，沿 `BaseType` / `AllInterfaces` 穿透到 `UnityEngine.UI.Graphic/Selectable/RectTransform/Material/Texture2D…` 与 `System.MulticastDelegate`；新增覆盖率计数 `SemanticResolvedFieldCount` |
| `WidgetInternalLayoutAudit` | 几何数值只认 `NumericLiteralExpression` | 新增语义常量折叠：`SemanticModel.GetConstantValue` 优先，可解析 `BASE_CARD_HEIGHT * 0.5f` / const 引用 / 括号嵌套；新增覆盖率计数 `LastSemanticFoldedCount` |
| `WidgetColorLiteralAudit` | 保留 4 个恒返回 0 / 空表的"假 API"（`TotalRegisteredDebt` / `GetAllowedOccurrences` / `IsRegistered` / `ValidateRatchet`）与 4 处调用点 | 全部删除；SPEC-006 口径简化为**零容忍**（任何一处颜色字面量即 ERROR） |
| `WidgetSpecificationValidator` | 文档声称"反射 + 源码 AST 双重机制"；规则 7 只 `TotalChecksPerformed++` 却什么都不检查 | 明确重新定性为**运行期反射通道 / L2 降级通道**（Roslyn 不进 KSP 运行时）；删除虚无的规则 7 计数；报告口径改为 SPEC-001..011 |
| 语义降级 | 只打一行 WARNING，不影响退出码 | **fail-closed**：门禁路径（`Scan(discovery)`）下语义上下文缺失 / Unity 程序集未链接一律判 **ERROR**；自检合成路径仍为 WARNING（否则每条合规断言都会被降级噪声打红）；离机环境用 `--allow-semantic-degradation` 显式放行 |

### 8.2 新增的语义自证（防"点位被拆而门禁依然全绿"）

| 自检 | 断言 |
| --- | --- |
| `WidgetFieldPenetrationAudit.SelfTest`（5 条） | `using GO = UnityEngine.GameObject;` 的字段必须是 `UiHandle`（语法简名 "GO" 必落裸标量）；`class MyCustomReadout : Image` 必须是 `UiHandle`；自定义委托 `CustomHandler` 必须是 `EventCallback`；`Cached<float>` 是 `ManagedCache`；`_lastSpeed` 是 `ResidualDirtyField` |
| `WidgetInternalLayoutAudit.RunSelfTest`（+5 条） | 无语义模型时 `HALF (BASE * 0.5f)` 必须折不出来（证明折叠真来自语义）；持有模型时须折成 160 / 70；**枚举成员 `Role.Value` 不得被当作几何数值** |
| `WidgetSourceAudit.SelfTest`（+2 条，21.7） | 门禁路径下语义缺失必须判 ERROR；`AllowSemanticDegradation = true` 时必须放行 |

自检总条数 **144 → 168**（规则 115→117 + 颜色 29 + I18n 语法树 17 + I18n 词典 3 + 内部几何 4→9 + 字段穿透 0→5），
`RuleCount` 13 → 14。

### 8.3 突变验收（实测）

```
基线                                                    自检 exit = 0
[突变] 拆掉 WidgetFieldPenetrationAudit 的 UI 句柄语义分支
        → _readout / _anchor 归类错误，exit = 1            变红 ✔
[突变] 把 SemanticCompilationHealth 的降级严重度强制为 WARNING
        → "fail-closed 失效" 断言失败，exit = 1            变红 ✔
还原后                                                  自检 exit = 0
```

### 8.4 改造引入并修正的一个真实陷阱

语义常量折叠里 `SemanticModel.GetConstantValue` 对**枚举成员**同样返回编译期常量
（`TextStyleRole.Label` / `TextAnchor.MiddleLeft` / `WidgetDock.TopLeft` …）。
若不显式过滤 `TypeKind.Enum`，`new TextWidget(role, x, y, w, h, size, anchor, text)` 的参数位置
会被枚举常量顶掉，几何坐标整段错位 —— 实测会让 `OrbitalInfoWidget` 凭空多出 2 处
"控件重叠"误报。现已加 `TypeKind.Enum` 过滤 + 自检反向用例锁死。

### 8.5 改造后门禁实跑

```
[6/10] 扫描 177 文件 → 组件类 47 个；语义编译集排除 8 个审计文件（与 csproj 逐项一致）
  ├─ 编译语义模型: 成功链接 18 个外部 Unity/KSP 程序集 (Full L4)
  ├─ 语义编译体检: 诊断 ERROR 0 (棘轮上限 0)
  └─ 审计统计: ERROR 0 / WARNING 9
[7/10] 审计内核自检通过: 117 + 29 + 17 + 3 + 9 + 5 条用例
✔ [ALL CHECKS PASSED]（退出码 0）
```

- `--audit-fields`：字段归类通道显示 `穿透性语义符号决议 | 语义决议字段 2910/2910`；
  与改造前同population（74 类 / 2910 字段），事件委托 21 → **25** 处、裸标量泄漏 699 → **695** 处
  （差额正好是 4 个自定义委托被语义正确归类，详见 §8.6）。
- `--audit-internal`：`语义常量折叠命中 111 处`，0 内部几何重叠。

#### 门禁基线登记（2026-09-29 冻结）

本轮审计**有意**提高了灵敏度（`IsTextureRasterizerSymbol` 语义判定替换后缀匹配），
因此门禁指标发生一次**跳变**。经确认，此跳变属"审计变强 → 检出变多"，非质量回归，
故**登记为新基线**，此后门禁以本表为准：

| 指标 | 旧基线（HEAD 34d2a8c） | **新基线（2026-09-29 起）** | 变化原因 |
| --- | --- | --- | --- |
| 语义编译集 | 17 程序集 | 18 程序集 | 排除 8 个审计文件后仍需链入 Roslyn 相关程序集 |
| 编译诊断 ERROR | 0 | **0** | 不变（棘轮上限 0） |
| 门禁 ERROR | 0 | **0** | 不变 |
| 门禁 WARNING | 0 | **9** | SPEC-002 CPU 光栅化真实检出（见下） |
| 架构现代率 | 100.0%（现代 44 / Core3D 2 / 旧版 0） | **82.6%**（现代 36 / Core3D 2 / 旧版 8） | 同上，8 个组件由 ModernDSL 降级 |
| 字段穿透语义决议率 | —（当时无此通道） | **2910/2910 = 100%** | 本轮新增通道 |
| 内部几何重叠 | 0 | **0** | 不变（阻断口径） |
| 自检用例总数 | 144 | **168** | 117 + 29 + 17 + 3 + 9 + 5 |

> **9 条 WARNING 的性质（勿当误报清理）**：`SemanticCompilationProvider.IsTextureRasterizerSymbol`
> 把宿主类型穿透到 `UnityEngine.Texture2D`（方法名 `SetPixel/SetPixels/SetPixels32/SetPixelData/Apply/LoadRawTextureData`），
> 取代了旧口径 `EndsWith(".SetPixels32")`。这 9 个组件写的是 `tex.SetPixels(cols)` 且**都没有 `_texPixels` 字段**，
> 旧口径完全抓不到，故属**真实检出**。
> `WidgetModernizationAudit`（L283 `else if (hasCpuRasterizer) → LegacyImperative`）随之把这 8 个组件
> 从 ModernDSL 降级。
>
> **该基线已获用户确认接受。** 若日后要打回 100%，正确做法是**真正把这 8 个组件迁移到
> GPU 程序化网格 / Core3D 绘制**，而不是放宽 `IsTextureRasterizerSymbol` 的判定 ——
> 放宽判定等于把"真实检出"改回"抓不到"，属于审计能力退化。

### 8.6 尚未语义化的残留（诚实留档，非本轮目标）

1. ~~`WidgetFieldPenetrationAudit` 与 `WidgetInternalLayoutAudit` 仍**不在 `test-ui.ps1` 主门禁链路**内~~
   → **2026-09-29 已按「布局阻断 + 字段仅报告」接入，详见 §8.8。**
2. `WidgetFieldPenetrationAudit` 仍未沿基类链收集**继承字段**（只看 `classDecl.Members`）。
   语义通道只解决了"类型如何归类"，未解决"哪些字段算这个组件的状态"。
3. `RoslynAstHelper.IsTelemetryDataExpression` / `IsUIDrawExpression` 内约 60 处
   `StartsWith/EndsWith` 文本兜底、`WidgetModernizationAudit.IsCpuRasterizerCall` 的语法兜底分支仍在。
   现行策略是"语义优先 + 语法兜底"，**未 fail-closed**：语义缺失时这些分支会静默接管。
4. `WidgetSpecificationValidator` 的反射规则在游戏进程内仍是唯一可用通道，
   结构上不可能覆盖 SPEC-007/009/010/011（纯源码规则）。

### 8.7 使用说明

```bash
# 标准门禁（语义必须可用，否则报 ERROR）
dotnet tools/HeadlessValidator/bin/Debug/net6.0/HeadlessValidator.dll

# 离机环境放行语义降级（结论强度低于 L4，会在报告里显式声明）
dotnet tools/HeadlessValidator/bin/Debug/net6.0/HeadlessValidator.dll --allow-semantic-degradation
```

### 8.8 两个几何/字段审计的门禁接入口径（2026-09-29）

两者接入**同一处** `test-ui.ps1` → `HeadlessValidator.Main()`，但阻断强度**刻意不同**：

| 审计 | 接入点 | 阻断 | 理由 |
| --- | --- | --- | --- |
| `WidgetInternalLayoutAudit`（内部控件几何） | `[3/10]` → `AuditInternalControlOverlaps(repoRoot)`，返回的 ERROR 数计入 `overallErrors` | **阻断**（ERROR 级；WARNING 级仅打印） | 当前实测 0 重叠，指标健康；几何穿模是**可判定对错**的硬缺陷，适合做门禁 |
| `WidgetFieldPenetrationAudit`（字段穿透） | 末尾 `[附加] 字段穿透性语义审计 (Field Penetration Report, 仅报告不阻断)`，**不**计入 `overallErrors` | **仅报告** | 全库仍有 1059 处存量裸字段泄漏（历史技术债），阻断会使门禁**恒红**，信号价值归零。改为**趋势观测**：每次门禁打印真实数字，供人工比对是否恶化 |

- 该段落**刻意只打印 4 行紧凑摘要，不调 `RenderConsoleReport`**：完整大盘含逐组件字段明细与
  「整改实施指南」（约 2500 行），直接倒进主门禁只会淹没真正的失败信号。深挖走旁路 `--audit-fields`。
- 摘要打印 `字段归类通道: 穿透性语义符号决议 2910/2910` 与纳管率；
  语义上下文缺失时会打印 **WARNING 显式声明"数字仅供参考，不参与门禁判定"** —— **不静默降级**。
- 该段落被 `try/catch` 包裹：审计自身异常只降级为 WARNING，**不得**把整条门禁链拖红。
- 旁路 CLI（`--audit-fields` / `--audit-internal`）保留，用于单独深挖；
  `--audit-fields` 的退出码仍按 `TotalAllLeaks == 0 ? 0 : 1` 定义（全库现状 **exit=1 属预期**，非回归）。
- 何时可从"仅报告"升级为"阻断"：当 `TotalAllLeaks` 收敛到 0（或先立棘轮上限）后，
  再把该段改为 `overallErrors += fieldReport.TotalAllLeaks;`。


### 8.9 门禁真实性实证：「100% pass」是真判断还是作弊？（2026-09-29）

**结论：是真判断，不是作弊；但存在 1 个实质漏洞与 3 处措辞高估。**

方法：不改判据，只**故意破坏输入**，看门禁是否如实变红（突变测试）。
每次改动前 `cp` 备份，跑完 `md5sum -c` 逐字节还原（本机 `git stash` 不可靠，见 §8.3）。

#### 8.9.1 突变矩阵（全部如实变红）

| # | 突变内容 | 目标文件 | 实测结果 |
| --- | --- | --- | --- |
| T1 | 组件方法体内写入字段声明（非法 C#） | `ArcMeterWidget.cs` | `✘ 发现层未通过: 2 处语法解析失败…状态 SourceParseFailed` → `ERROR 1` → exit 1 ✔ |
| T1c | 注入白名单外颜色 `Color.gray` | 同上 | `✘ [ArcMeterWidget.cs] 规则 MFP-SPEC-006 违规: 检测到颜色字面量 1 处（零容忍）…样本 L212` → `ERROR 1`，且 **「100% 通过」横幅不再打印** ✔ |
| T2 | 注入 1 处硬编码中文 | 同上 | `源码硬编码中文 120 → 121 处`，但 [9/10] 仍 `✔`、**不阻断** ← **漏洞，见 8.9.3** |
| T3 | `zh-CN.json` 写成非法 JSON | `GameData/…/Localization/` | `✘ [I18n] zh-CN.json 标准 JSON 解析异常: 'T' is an invalid start of a property name` → exit 1 ✔ |
| T4 | 布局中塞入未知通配符 | `PluginData/layout.json` | `✘ 组件 '多通道遥测综合矩阵卡' 模板包含未知通配符: '{ZZBOGUS_TAG_QQ}'` → exit 1 ✔ |
| T5 | 改 `src` 源码但不同步 unity 镜像 | 任意被镜像的 `.cs` | `✘ 镜像漂移: …（Unity 预览跑的是旧代码）` ✔ |

**归因隔离**：T3/T4 只动 `GameData/`（不进 `src/`），故 `exit=1` 可**干净归因**于被检验项，不受镜像漂移污染；
T1c/T2 动的是被镜像文件，故按其**分段局部读数**（`[6/10] 审计统计` / `[9/10]`）归因。

#### 8.9.2 两处曾被怀疑、实测证伪的"静默放行"

1. `Program.cs:1117` 的 `try { I18nJsonParser.Parse } catch { }` —— 疑似会吞掉词典损坏后静默放绿。
   **T3 证伪**：解析异常被显式报 FAIL 并 exit 1。
2. "扫不到就跳过" —— **T1 证伪**：发现层 fail-closed（`CanAuditSource = false` 判 ERROR），
   且语义编译诊断超 `SemanticCompilationErrorCeiling` 同样判 ERROR。

> **反面教训（实验设计）**：首次尝试用 `Color.clear` 做 T1，结果**没有变红**。
> 这不是门禁失守 —— `Color.clear` 是**豁免项**（自检矩阵明写 `("var c = Color.clear;", 0)`，占位色专用）。
> **做突变测试前必须先读被判据的豁免清单**，否则会把自己的设计错误误判成门禁漏洞。

#### 8.9.3 实质漏洞：硬编码中文**无任何门禁约束**（已于同日封堵，见 §8.10）

- 主门禁 `[9/10]` 走的是 `ValidateI18nLocalization`，它把
  `源码硬编码中文 N 处 / 未登记键名 M 处` 归入**纯打印**分支 —— **不计入该函数返回的 `errors`**。
  （仓库里另有一个 `--i18n-ast` 旁路函数用 `ratchetErrors` 做判定，但**主门禁不走它**；
  早期把这一项当成"已受棘轮保护"属于误判，行号也因此一度写错。）
- 对照：英文文案有棘轮（当时实测 339 / 基线 364，只拦新增），词典未汉化有棘轮（2 / 基线 2），
  **唯独中文硬编码是"纯统计"** —— 可以任意新增而不被拦下（T2 实证：120 → 121，门禁仍绿）。

#### 8.9.4 三处「口号 > 事实」（透明，但不该被误读）

1. `✔ 规范合规审计 100% 通过 (47 个组件类完全合规)` —— 同一屏上方正列着 **9 条 `MFP-SPEC-002 违规`**；
   47 个里 9 个并非"完全合规"。**措辞与同屏事实矛盾**。
   （此处为**当时**数字；仓库并发重构后现为 44 个组件 / 8 条 WARNING，见 §8.10.3。措辞已于 §8.10.2 修正。）
2. SPEC-002 光栅化消息自称"效能**红线**""**严禁**"，但严重度硬编码为 `"WARNING"`
   （`WidgetSourceAudit.cs:1207`）→ **永不阻断**。判据自述与执行不一致。
3. `ALL CHECKS PASSED` 的准确含义 = **无 ERROR 级发现 + 无超出冻结基线的新增欠账**，
   **不等于"零问题"**。当前同屏共存的存量：9 条 SPEC-002 WARNING、339 处可汉化英文、
   120 处硬编码中文、1059 处裸私有字段（仅报告）。

#### 8.9.5 可复现性

同环境**连续两次**运行门禁，输出**逐字节一致**（`diff` 为空），`exit=0` 稳定。
曾观察到一次基线漂移，溯源为**并发会话**的 Unity 无头渲染在 19:05:40 重写了
`layout.json` / `layout.backup.json` / `unity_headless_render.png` ——
**非门禁不确定性**，也非本轮突变残留（全部还原后 `md5sum -c` 通过）。

### 8.10 漏洞封堵与措辞修正（2026-09-29，紧接 §8.9）

#### 8.10.1 中文硬编码：立棘轮 + 未登记键名零容忍

`I18nSyntaxAuditor.cs` 新增（沿用英文文案的同一套口径与同名结构）：

| 成员 | 作用 |
| --- | --- |
| `ChineseBaselineTable` / `ChineseRatchetCeilingTable` | 存量冻结基线（由 `--i18n-baseline-dump` 生成，**禁止手工估算**） |
| `TotalRegisteredChineseDebt` / `GetAllowedChineseOccurrences` / `IsChineseBaselineRegistered` | 取值入口 |
| `ValidateChineseRatchet()` | 基线表自洽性（缺上限 / 基线上调 均判失败） |
| `ValidateChineseBudget(report)` | ① **未登记键名零容忍**（界面会直接显示原始 KEY）② 中文硬编码棘轮（只拦超基线新增） |

冻结基线（2026-09-29 生成，合计 **120 处 / 8 文件**）：
`HUDEditModeToolbar.cs 86`、`TelemetryMatrixData.cs 22`、`TabAssembler.cs 5`、
`StageDeltaVWidget.cs 2`、`MFPSafetyFallback.cs 2`、`CustomTokenTextWidget.cs 1`、
`FlightHUDManager.cs 1`、`WidgetRenderManager.cs 1`。

接入点为主门禁 `ValidateI18nLocalization`：在英文棘轮之后追加两个 foreach，失败计入 `errors`；
打印行改为常显 `源码硬编码中文: N 处存量 / 基线 M 处 (只降不升) | 未登记键名 K 处 (零容忍)`。
自检新增 **用例 18**（3 条断言：棘轮自洽 / 基线非空 / 越界与缺键必须被拦下），I18n 自检 17 → **18** 条。

**封堵后突变复验（T2b）**：向 `ArcMeterWidget.cs`（基线 0）注入 1 处硬编码中文 →

```
✘ [FAIL] [ArcMeterWidget.cs] 新增硬编码中文 1 处 (实测 1 / 基线 0)；… 样本: L212 "突变测试硬编码中文"
✘ [FAIL] I18n 多语言词典审计发现 1 处异常!
```

同一动作在封堵**前**不会让门禁变红（T2）—— 前后对照即封堵生效的证据。

#### 8.10.2 措辞与同屏事实对齐（只改措辞，判据不动）

1. `✔ 规范合规审计 100% 通过 (47 个组件类完全合规)` →
   `✔ 规范合规审计通过: ERROR 级 0 违规 (N 个组件类)；另有 M 条 WARNING 级发现未阻断（见下）`
2. `ALL CHECKS PASSED` 之后新增一行**通过口径声明**：
   `通过口径: 无 ERROR 级发现 + 无超基线新增欠账；不等于零问题（未阻断的 WARNING 级发现 M 条…）`

> SPEC-002 的 `"WARNING"` 严重度**按用户决定保留不改**。正因判据不阻断，
> 才必须把"不阻断"写在结论旁边 —— 而不是让人从"完全合规"四个字里去猜。

#### 8.10.3 复核与并发现象

- 插件 net472 不受影响：`I18nSyntaxAuditor.cs` 本就在 `ModularFlightPanel.csproj` 的 `<Compile Remove>`
  与 `WidgetSpecRules.PluginExcludedAuditFiles` 之内（已核对两处清单均含该文件）。
- 门禁复跑：`EXIT=0`、`ERROR 0 / WARNING 8`、I18n 自检 18 条、`✔ [ALL CHECKS PASSED]`。
- ⚠ **同期另有会话在并发重构本仓库**（已 `git rm` 3 个组件 `EcamDialGaugeWidget` / `OrbitalInfoWidget` /
  `EcamStatusWidget`，并新增 `SpacecraftAttitudeVisualGenerator.cs`），
  故组件数 **47→44**、英文基线 **364→360**、扫描文件 **148→145** 等漂移来自**该重构**，与本次改动无关。
  **解读本文件里的绝对数字时务必注意仓库处于并发修改中。**

### 7.5 尚存的非阻塞项（未改，留档）

- `IsContractRoot` 仍按类简名匹配（`WidgetSourceAudit.cs:170`）：跨命名空间同名 `BaseFlightRoot`
  会被误判为契约根。改它会牵动 80+ 条合成用例的命名空间约定，收益低于风险。

> **以下两条已于 2026-09-29 的"全面语义穿透化"改造中处理完毕，详见 §八：**
> - MSBuild 阶段钩子 `AuditWidgetModernization` 已**包含语义层**（它调用的
>   `WidgetModernizationAudit.Scan(discovery)` 会在上下文缺失时自愈构建编译），
>   实测插件 build 已能报出只可能来自语义路径的 `MFP_LEGACY_WIDGET ... CPU software rasterizer`。
> - 语义降级已由"仅打印一行"改为 **fail-closed**（门禁路径下判 ERROR）。

