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

### 7.5 尚存的非阻塞项（未改，留档）

- `IsContractRoot` 仍按类简名匹配（`WidgetSourceAudit.cs:170`）：跨命名空间同名 `BaseFlightRoot`
  会被误判为契约根。改它会牵动 80+ 条合成用例的命名空间约定，收益低于风险。
- `MSBuild` 阶段的 `AuditWidgetModernization`（`AfterTargets=CoreCompile`）仍只跑
  `WidgetModernizationAudit`，**不含语义层**——即每次构建的门禁强度低于 `test-ui.ps1` 全链路。
  若要统一，需把 `HeadlessValidator` 的语义链路接进构建钩子，代价是构建耗时明显上升。
