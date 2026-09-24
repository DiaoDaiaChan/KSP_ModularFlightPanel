# Modular Flight Panel 项目全面评估报告

**评估日期：** 2026-09-24  
**评估基线：** `main` / `8da5480` (`refactor: reorganize C# source folders and rename NavballHUD to FlightHUDManager`)  
**评估范围：** KSP1 插件源码、Unity 预览工程、遥测与 UI 核心、配置/分享码、构建和部署脚本、仓库文档与产物  
**评估方式：** 静态审阅、关键调用路径追踪、配置/仓库状态检查。未执行构建、自动化检查、游戏运行或性能基准。

## 1. 总体结论

项目已经形成可辨识的模块边界：KSP 遥测、第三方 Mod 探针、UI 生命周期/刷新调度、样式、配置和 Unity 预览分别有相应组件；源代码到 Unity 预览工程也有明确的镜像清单。多个性能优化已经落入当前源码，包括模板预编译缓存、数值 token 解析缓存、控件刷新分级、只读控件按需关闭射线检测，以及 ElectricCharge 优先使用 KSP 资源总量 API。

当前主要短板在**交付链路的失败判定、用户配置恢复能力、导入数据的资源限制、跨机器构建复现和证据可信度**。部署脚本可在编译失败后继续并打印成功；布局读取失败后会将默认数据写回原配置；分享码解压没有大小上限。仓库内跟踪有构建产物，但本次没有证据证明它们由当前源码和指定工具链生成。此前性能报告给出明确毫秒数和 GC 收益，缺少可复核的测试条件与原始采样，且部分根因描述已不符合当前源码。

**综合评级：中等成熟度，功能架构基础较好，工程交付与数据可靠性仍需加固。** 当前可用于持续迭代；在修复发布失败误报、保护配置和建立可复现构建记录之前，不宜仅凭当前工作树将其认定为已验证的发布候选。

## 2. 维度评估

| 维度 | 评价 | 主要依据 |
|---|---|---|
| 架构与可维护性 | 中上 | 遥测接口、控件框架、渲染调度、探针和主题模块化；部分文档仍按旧目录/名称描述，需保持同步。 |
| 工程化与交付 | 中下 | 有解决方案、部署脚本和镜像验证机制；部署会吞掉构建失败信号，构建依赖本机 KSP 路径，没有发现 CI 工作流。 |
| 正确性与数据严谨性 | 中 | 有配置迁移、异常日志和静态验证工具；写配置非原子，坏配置会触发默认布局回写，自动验证尚未在本次确认通过。 |
| 性能设计 | 中上（设计）/ 未验证（结果） | 有调度、缓存和脏检查设计；实际帧耗时、GC 与 GPU 成本无本次实测，历史精确指标不可复核。 |
| 安全与鲁棒性 | 中 | 未发现直接网络摄入或远程执行链路；压缩分享码无解压上限，第三方反射读取边界宽，配置输入校验仍需加强。 |
| 文档与发布可信度 | 中下 | README 仍含本机文件链接和过期路径；既有审计报告存在夸张/过时的性能断言。 |

## 3. 主要发现

### P1 — 部署脚本在构建失败后仍可能报告部署成功

`deploy.ps1` 调用 `dotnet build` 后没有检查 `$LASTEXITCODE`，也没有在项目文件不存在时中止；随后仍会同步仓库中的 `GameData` 并输出成功信息。[deploy.ps1](../deploy.ps1#L15) [deploy.ps1](../deploy.ps1#L56)

插件项目的 PostBuild 复制步骤使用 `ContinueOnError="true"`，本地 `GameData` 或指定 KSP 目录复制失败可能不使构建失败。[ModularFlightPanel.csproj](../src/ModularFlightPanel/ModularFlightPanel.csproj#L92)

**影响：** 编译或复制失败时，用户可能得到旧 DLL，却看到成功提示。发布结果和构建结果无法可靠对应。

**建议：** 将构建失败设为立即终止；验证构建产物存在并记录 SHA-256；部署完成后校验目标 DLL 与源产物一致。将 PostBuild 的多目标复制收敛到一个显式发布步骤，并对每个目标复制失败返回非零状态。

### P1 — 布局配置损坏时会被默认配置覆盖，保存也不是原子操作

读取 `layout.json` 异常或得到空布局后，加载逻辑创建默认布局并立即调用 `SaveLayout()`；保存则直接 `File.WriteAllText` 覆盖唯一文件。[WidgetLayoutManager.cs](../src/ModularFlightPanel/Config/WidgetLayoutManager.cs#L38) [WidgetLayoutManager.cs](../src/ModularFlightPanel/Config/WidgetLayoutManager.cs#L116)

**影响：** 崩溃中断写入、磁盘错误或旧版/手工编辑配置不兼容时，原有布局可能丢失，而且错误恢复路径会覆盖现场，难以诊断和恢复。

**建议：** 写入同目录临时文件并在成功后替换；保留上一份有效备份；解析失败时保留原文件并只在内存中使用默认布局；加载前验证版本、坐标/缩放边界、组件数量和 ID 唯一性，并在 UI 或日志明确提示恢复状态。

### P2 — 分享码解压没有输入与解压输出上限

布局和主题导入会将 Base64 全量解码，再用 `GZipStream.CopyTo` 解压到无界 `MemoryStream`，之后再整体转字符串并反序列化；导入前没有限制分享码长度、解压字节数或对象规模。[LayoutShareHub.cs](../src/ModularFlightPanel/Config/LayoutShareHub.cs#L60) [LayoutShareHub.cs](../src/ModularFlightPanel/Config/LayoutShareHub.cs#L331)

**影响：** 用户粘贴体积异常或高压缩率数据时，主线程可能产生大额内存分配、长时间卡顿或进程内存压力。该风险依赖用户主动导入，并非自动远程攻击入口。

**建议：** 限制 Base64 长度、压缩数据长度、流式解压总字节数和布局/主题条目数；在模型加载后验证数值范围、字符串长度和必需字段。

### P2 — 构建依赖开发机路径，难以跨环境复现

插件项目默认将 `KSPRoot` 指向某一台机器上的 Steam 安装目录，并从该路径加载 KSP/Unity 程序集；HeadlessValidator 的 Mono.Cecil 引用也使用绝对本机路径。[ModularFlightPanel.csproj](../src/ModularFlightPanel/ModularFlightPanel.csproj#L12) [HeadlessValidator.csproj](../tools/HeadlessValidator/HeadlessValidator.csproj#L1)

仓库未发现 GitHub Actions 或其他 CI 配置、统一 SDK 固定文件、项目自有测试程序集。虽然 `KSPRoot` 可覆盖，但新开发环境必须自行配置相同版本的 KSP 程序集。

**影响：** 新机器构建容易失败或引用不匹配程序集；代码合并后缺少自动化门禁，无法证明镜像同步、编译和打包均成功。

**建议：** 通过环境变量/命令行统一传入 KSP 根目录，并在构建前检查必需程序集及版本；固定 .NET SDK；建立 CI 执行镜像检查、HeadlessValidator、插件编译和产物清单校验。

### P2 — 仓库中的性能结论和部分文档未与当前源码、证据对齐

旧性能报告宣称固定帧耗时、GC 分配量和提升比例，但未提供硬件、场景、载具规模、采样周期、统计口径或 Profiler 原始输出。[PERFORMANCE_AUDIT_REPORT.md](PERFORMANCE_AUDIT_REPORT.md#L357)

报告中的部分问题在当前源码中已变化：例如电量采样已优先调用 `GetConnectedResourceTotals`，仅在失败时回退到部件扫描；token 模板也已有编译缓存；只读控件射线检测已有按需处理。[TelemetryHub.cs](../src/ModularFlightPanel/Core/Telemetry/TelemetryHub.cs#L1271) [TelemetryTokenEngine.cs](../src/ModularFlightPanel/Core/Telemetry/TelemetryTokenEngine.cs#L143) [BaseFlightWidget.cs](../src/ModularFlightPanel/UI/Framework/BaseFlightWidget.cs#L93)

README 仍保留本机 `file:///` 链接、将主题路径写作 `GameData/ModularFlightPanel/Themes/`，目录树也引用与当前源码布局不一致的旧类位置。[README.md](../README.md#L26) [README.md](../README.md#L72)

**影响：** 读者无法区分已测结果、设计目标和推算数据；过期文档会误导贡献者或安装者。本次检查未发现 README 中存在字面 Git 冲突标记，历史审计对此的描述不适用于当前文件。

**建议：** 将性能报告明确标为历史/假设数据，或补齐基准条件和可复现采样步骤；仅发布带原始数据的实测收益。更新 README 路径、目录和安装说明，删除本机绝对链接。

### P2 — 运行二进制与源码同库，但缺少来源和一致性证明

`GameData` 跟踪 DLL、PDB 和 AssetBundle，插件项目又会在 PostBuild 将产物复制回 `GameData`。当前仓库未看到发布清单、生成提交、构建工具版本或哈希记录，无法仅凭文件状态确认运行产物由对应源码生成。

**影响：** 审阅源码不一定能审阅到游戏实际运行的二进制；手工替换产物可能导致源代码、预览 Unity 镜像和发布包不一致。

**建议：** 每次发布附带源提交、KSP/Unity/.NET 工具链版本和 SHA-256 清单；用干净工作区生成 DLL/AssetBundle，并在发布门禁中比较仓库产物与流水线产物。

### P3 — 反射探针会执行宽泛枚举到的无参方法

通用探针遍历器会把公开无参且有返回值的方法注册为潜在遥测读取器，再通过反射调用。第三方 Mod 的无参方法未必是轻量、无副作用的读取操作；异常吞噬也可能使探针故障只表现为读数缺失。[ProbeReflectionTraverser.cs](../src/ModularFlightPanel/Core/Probes/ProbeReflectionTraverser.cs#L133) [ProbeReflectionTraverser.cs](../src/ModularFlightPanel/Core/Probes/ProbeReflectionTraverser.cs#L223)

**影响：** 可选 Mod API 变化可能引入额外主线程耗时或意外副作用；调用频率若不受控，会放大性能问题。未发现由此直接加载外部程序集或执行远程代码的证据。

**建议：** 默认仅读取白名单属性/字段；需要方法时逐个显式注册并设定采样周期；记录探针耗时、失败次数和最后成功时间，对持续失败探针熔断。

## 4. 性能与安全性专项判断

### 性能

- **已有正向设计：** `WidgetRenderManager` 集中调度各控件刷新；模板编译缓存上限为 512；数值 token 解析缓存上限为 512；控件提供文本/填充脏检查；TelemetryHub 对若干昂贵采样做节流或缓存。
- **仍需运行时验证：** KSP 主线程中各探针反射、引擎/资源循环、离屏相机与剪影烘焙的总耗时；多组件和大型载具场景下的 GC Alloc、帧时间 P95/P99、GPU 时间及场景切换释放情况。
- **证据边界：** Profiler 代码和运行日志不等同于可复核基准。现有文档的 `0.12 ms`、`<10 KB/s` 等数字，本次不采纳为当前版本性能结论。

### 安全与鲁棒性

- 静态检索未发现插件主动发起网络请求、启动外部进程或执行下载内容的路径。
- 分享码只反序列化布局/主题数据；当前可确认的输入风险是无界解压和字段/规模校验不足。
- 配置、日志、预设主要写入 KSP 插件数据目录；需结合原子保存与导入约束防止用户数据损坏和本地资源耗尽。
- 项目启用了 `AllowUnsafeBlocks`，但本次静态检索没有发现相关代码实际使用 `unsafe`；可移除不必要的编译权限声明以减少误导。

## 5. 已具备的工程基础

- `tools/unity_mirror.manifest` 明确列出预览工程排除项，并规定其余源码必须镜像一致；该镜像方案有验证器支持。
- 主插件按 `net472`/KSP1 环境编译，Unity 预览工程单独组织；第三方 Mod 通过可选探针集成，降低缺少某个 Mod 时的硬依赖。
- `MFPProfiler`、统一日志、生命周期注销、配置迁移等机制已存在，为后续量化与故障定位提供基础。
- `.gitignore` 已排除 Unity Library、原型依赖目录等常见生成物。

## 6. 建议整改顺序

1. **修复发布失败误报**：构建退出码、缺失项目/产物、复制结果和目标哈希全部纳入失败条件。
2. **保护用户布局**：原子写入、保留备份、坏配置不自动覆盖，并加入模型边界校验。
3. **约束导入资源**：分享码长度、GZip 解压字节、对象数量和字段范围设硬上限。
4. **稳定构建环境**：消除硬编码依赖路径，固定工具链并引入 CI 门禁。
5. **清理文档与产物证据**：更新 README；性能结论补数据；DLL/AssetBundle 记录来源与哈希。
6. **运行时性能与兼容性回归**：在标准场景和大型载具下采集 CPU、GC、GPU 指标，并覆盖场景切换、坏配置恢复和可选 Mod 缺失。

## 7. 评估限制

本报告基于源码与仓库配置静态审阅。没有运行 `dotnet build`、HeadlessValidator、UI 渲染、Unity/KSP、外部 Mod 组合或性能采样。因此不对当前构建成功、功能无缺陷、镜像检查通过或具体性能收益作保证。工作区在检查开始时位于 `main` 且没有未提交改动；报告反映提交 `8da5480` 对应的状态。
