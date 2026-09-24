# Modular Flight Panel 项目工程化 / 严谨性 / 性能 / 安全评估

**评估日期：** 2026-09-24  
**评估对象：** 当前工作树中的 KSP1 / Unity 2019.4 插件、Unity 预览工程、原型与构建部署工具  
**评估方式：** 静态代码与配置审查、目录/版本状态盘点、关键调用链追踪。未执行构建、测试、Unity/KSP 运行或性能基准。

## 一、结论摘要

项目具备清晰的功能分层，已经开始引入统一控件生命周期、刷新率调度、缓存、反射探针隔离和 Unity 镜像一致性验证；这些设计方向有助于控制扩展成本。当前也存在可复现性与交付完整性方面的短板：部署脚本不可靠地检查编译退出码，MSBuild 复制错误被忽略；用户配置以非原子方式写入且读取失败会重建并覆盖；社区分享码解压没有尺寸上限；根 README 保留 Git 合并标记并描述了已删除的目录结构。仓库中源代码与随仓库维护的 DLL/AssetBundle 同时变更，使得本次工作树的源代码、二进制和实际游戏安装内容不能仅凭文件状态确认一致。

**综合判断：中等成熟度，适合持续迭代，不建议在修复交付链路并建立可复现验证前，把当前工作树视为可审计的正式发布候选。** 主要风险在工程交付和本地输入鲁棒性；本次静态审查没有发现可直接确认的远程攻击面或任意代码执行链路。

## 二、评估范围与边界

- 检查了 `src/ModularFlightPanel`、`unity/Assets`、`GameData`、`tools`、`prototype`、README/架构文档、项目文件、部署脚本及已有性能审计文档。
- 工作树在评估开始时已有大量未提交变更，涉及源代码重组、Unity 镜像、DLL/PDB、AssetBundle、配置和文档。结论描述的是**当前工作树**，不代表干净主分支或某个已发布版本。
- 仓库含 `HeadlessValidator` 与 UI 预览/渲染工具，也含 Unity Test Framework 依赖；但没有发现项目自己的自动化测试用例或 CI 工作流。本次未运行工具，因此不能确认当前提交能否完整构建、镜像校验通过或在 KSP 中正常工作。
- 性能判断只针对代码结构与已有文档的证据质量，不对帧耗时、GC、帧率改善给出实测结论。

## 三、分级问题

### P1 — 发布链路可能报告成功但交付旧 DLL

`deploy.ps1` 调用 `dotnet build` 后没有检查 `$LASTEXITCODE`，即使编译失败也继续同步仓库里的 `GameData` 并打印部署成功（[deploy.ps1:15-20](../deploy.ps1#L15)、[deploy.ps1:56-60](../deploy.ps1#L56)）。项目的 PostBuild 复制任务又使用 `ContinueOnError="true"`，本地或目标游戏目录复制失败可被吞掉（[ModularFlightPanel.csproj:92-106](../src/ModularFlightPanel/ModularFlightPanel.csproj#L92)）。因此脚本可能把旧的已跟踪 DLL 部署出去，却给出成功提示，形成高风险的发布误判。

**建议：** 让编译非零退出立即终止；复制失败作为构建失败；部署前后校验 DLL 存在、时间戳或 SHA-256，并在日志明确报告目标路径和构建版本。避免 PostBuild 同时写仓库、游戏目录和部署目标，改由一个显式发布步骤负责产物拷贝。

### P1 — 重要用户配置存在损坏/覆盖风险

布局保存直接 `File.WriteAllText` 覆盖唯一配置文件，没有临时文件、原子替换或备份（[WidgetLayoutManager.cs:116-130](../src/ModularFlightPanel/Config/WidgetLayoutManager.cs#L116)）。读取失败或空布局时会创建默认布局并立即保存（[WidgetLayoutManager.cs:38-59](../src/ModularFlightPanel/Config/WidgetLayoutManager.cs#L38)），这会覆盖损坏文件，使原布局无法恢复。保存异常虽有日志，但没有对用户可见的失败状态。

**建议：** 写临时文件后原子替换；保留上一份有效配置；读取失败时保留损坏文件并进入默认内存态，不自动覆盖；验证版本号、数值范围、重复 ID 与空字段后再加载。

### P2 — 分享码导入没有解压大小限制

布局与主题分享码由 UI 粘贴输入，Base64 后经 GZip 解压到 `MemoryStream`，再整体转成字符串和 JSON 对象；`CopyTo` 前没有对输入长度或解压输出设上限（[LayoutShareHub.cs:59-101](../src/ModularFlightPanel/Config/LayoutShareHub.cs#L59)、[LayoutShareHub.cs:330-373](../src/ModularFlightPanel/Config/LayoutShareHub.cs#L330)）。小型压缩输入可膨胀成很大的分配，造成游戏卡死、内存压力或崩溃。风险局限于用户主动导入本地/社区分享码，未发现自动远程摄取。

**建议：** 限制编码输入长度、压缩数据长度、解压最大字节数和组件数量；流式解压并在达到上限时中止；反序列化后校验主题/布局字段及有限数值。

### P2 — 根 README 有合并残留且文档与当前资源布局不符

README 末尾包含字面量 `>>>>>>> c218bb8 ...`（[README.md:142](../README.md#L142)），并用 `file:///` 指向开发者本机路径（[README.md:26](../README.md#L26)）。文档仍称主题位于 `GameData/ModularFlightPanel/Themes/`，而当前目录已删除该主题目录、预设放在 `PluginData/Presets`。这会误导贡献者与使用者，也降低仓库审阅质量。

**建议：** 清除冲突标记和本机绝对链接；以当前仓库目录和实际功能重新整理 README，明确构建、安装、兼容范围及配置迁移方式。

### P2 — 性能审计中的数字尚不可复核

现有 [性能审计报告](PERFORMANCE_AUDIT_REPORT.md) 声称基线 `1.04 ms / 6.1%`、优化后 `0.12 ms`，还给出 KSP 原生 HUD 的对照区间，但报告未附 Profiler 原始数据、采样场景/飞船规模、机器配置、采样时长、统计口径或复测步骤。报告中若干“优化前”代码描述与当前源码已不一致，例如遥测的 ElectricCharge 汇总已经存在 `GetConnectedResourceTotals` 路径（[TelemetryHub.cs:1059-1085](../src/ModularFlightPanel/Core/TelemetryHub.cs#L1059)）。所以这些数字与历史根因判断不应直接当作当前版本的性能事实。

**建议：** 保留历史报告但标注代码版本/提交；补齐采样方法和原始 Profiler/GC 数据；对代表性场景做前后对比，报告平均值、P95、GC Alloc、GPU 时间与采样条件。避免在证据缺失时宣称固定性能收益或“零 GC”。

### P2 — 多份运行产物与源代码同库，当前变更难以追溯

`GameData` 中跟踪了 DLL/PDB 与 AssetBundle；本次工作树里这些二进制也被修改，同时源码有大量变更/迁移。没有 CI 发布记录或产物清单能证明这些二进制由当前源码和指定 Unity 版本生成。仓库有 `unity_mirror.manifest` 和 `HeadlessValidator --mirror-check` 的一致性机制，这是积极措施，但它只约束 `src` 与预览工程镜像，并不能证明发布 DLL/Bundle 可复现。

**建议：** 给每个发布产物记录源提交、工具链版本和 SHA-256；提供从干净工作区生成包的单一命令；在 CI 中验证 Unity 镜像、编译、静态审计及打包清单。若必须跟踪二进制，要求发布流水线生成并校验，而非手工替换。

### P3 — 构建依赖本机路径且缺少门禁

主插件目标为 `net472`，引用程序集来自硬编码的默认 KSP 安装路径（[ModularFlightPanel.csproj:3-22](../src/ModularFlightPanel/ModularFlightPanel.csproj#L3)）；HeadlessValidator 的 Mono.Cecil 路径也硬编码到同一安装目录。项目没有发现 `.github/workflows`、其他 CI 配置、`Directory.Build.*`、锁定 SDK 文件或自有测试程序集。此配置能服务当前开发机，但新贡献者和自动化构建需先猜测环境并修正路径。

**建议：** 通过 `KSPRoot` 环境变量/命令行统一提供依赖根目录，启动时做前置检查并给出可操作错误；固定 SDK/编译器和依赖版本；把镜像校验、验证器、构建和最小运行回归纳入 CI。

### P3 — 通用反射遍历器把无参方法当遥测读取器调用

`ProbeReflectionTraverser` 不止枚举字段和属性，还注册所有公开无参且有返回值的方法，并在读取成员时通过 `Invoke` 执行它们（[ProbeReflectionTraverser.cs:133-155](../src/ModularFlightPanel/Core/Probes/ProbeReflectionTraverser.cs#L133)、[ProbeReflectionTraverser.cs:223-247](../src/ModularFlightPanel/Core/Probes/ProbeReflectionTraverser.cs#L223)）。第三方 Mod 的无参方法并不一定是纯读取操作，可能消耗较高或产生副作用；广泛吞异常也会让不兼容情况静默表现为缺失数据。

**建议：** 默认只枚举明确认可的字段/属性或白名单 API；方法通过显式注册且附带缓存周期；为探针记录限频、耗时和错误摘要，避免每帧反射调用。

## 四、优势与已建立的基础

- 核心遥测、配置、UI 控件与可选第三方 Mod 探针有独立边界；探针缺席时可降级，方向合理。
- `WidgetRenderManager` 集中管理控件刷新频率与生命周期，遥测任务已有多档轮询；性能预算具备继续量化的基础。
- `unity_mirror.manifest` 明确规定镜像一致性，并由验证器提供 `--mirror-check/--mirror-fix`，比人工同步源码可靠。
- `.gitignore` 排除了 Unity `Library`、临时渲染和原型依赖目录；配置/预设为 JSON，便于检查和迁移。
- 分享码只反序列化布局/主题数据；本次没有发现通过分享码加载程序集或执行脚本的路径。

## 五、建议整改顺序

1. **先堵发布假成功**：处理 `dotnet` 退出码、复制错误与 DLL/Bundle 版本校验。
2. **保护配置数据**：原子保存、备份、坏配置恢复与明确的失败反馈。
3. **限制导入资源**：为分享码输入、解压输出和 JSON 对象规模设上限。
4. **清理可信度问题**：修订 README 与性能审计，给性能结果补方法和原始证据。
5. **建立可复现门禁**：固定工具链/依赖路径，将验证器、镜像检查、构建及代表性回归加入 CI。

## 六、结论

当前架构已有可复用的模块化基础，但工程严谨性仍受发布流程静默失败、配置恢复策略、输入资源限制与性能证据可追溯性约束。完成前四项后，再以干净环境构建和 KSP 实测验证发布候选；在此之前，性能报告中的精确收益数字以及当前二进制与源代码的一致性均应视为**未验证**。
