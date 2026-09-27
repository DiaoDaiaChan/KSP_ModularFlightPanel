# 通配符/GUI 断层排查报告（重构前备忘）

> 2026-09-27 排查结论：用户判断**成立**。不是个别遗漏，而是**三层各自硬编码、无单一事实源**的结构性断层：
> 引擎注册表（求值层） / 组件通道 schema（消费层） / 装配台词典与编辑器（GUI 层）互不投影、互不校验。

---

## A. 三套并存的"通配符"方言

| 层 | 语法 | 定义处 |
|---|---|---|
| ① 引擎整串模板 | `{TAG:SUB:FMT}`，正则最多 4 段，整串求值 | `Core/Telemetry/TelemetryTokenEngine.cs:16`（Evaluate: 248） |
| ② 组件通道键值 | `KEY=VALUE;KEY2=VALUE2`（VALUE 再交给引擎二次求值） | `UI/Framework/BaseFlightWidget.cs:891-930`（GetTemplateChannel / EnsureTemplateChannelsParsed） |
| ③ 各组件通道键集合 | 各写各的：EcamDial=`VAL/TITLE/UNIT/MIN/MAX/CAUTION`；SpaceXArc=`TOKEN`；custom 卡=`CH1..CH6`（且必须含 `;` 才解析） | `EcamDialGaugeWidget.cs:228-260`、`SpaceXArcGaugeWidget.cs:76`、`CustomTokenTextWidget.cs:89-144` |

附注（重构需处理的语法异味）：
- 引擎第三段存在歧义：`X/Y/Z/MAG/MAGNITUDE` 被当子标签延续，其余当格式串（`TelemetryTokenEngine.cs:203-220`），无 schema 校验。
- 例：`ArcMeterWidget.cs:65` 写 `{VSI:NORM}`，`NORM` 被引擎静默忽略（VSI 无该子标签）——错误语法不报错。

## B. 量化缺口（GUI ↔ 引擎 ↔ 组件）

1. **词典 ≠ 引擎注册表**
   - 引擎注册 **62 个原生 tag**（`TelemetryTokenEngine.cs:320-767`）；装配台词典仅覆盖 **34 个**原生顶级 tag（`TelemetryCatalog.cs` 共 156 条，其中 100 条为外部探针条目）。
   - 词典完全缺失的原生 tag（27 个）：`ECC / SMA / PERIOD / SIGNAL(仅 COMM 别名) / WARP / MET / UT / DV / BURNTIME / NODEDV / TIMETONODE / STAGE / CTRL_PITCH|ROLL|YAW / CTRL_TRANSX|Y|Z / TRIM_PITCH|ROLL|YAW / STAGE_LOCK / CTRL_MODE / CTRL_PREC / STAGE_PROP_NAME / PRESSURE / VOLT / SEPARATING / IGNITING`。
   - 子标签级缺口（tag 有收录但子标签没有）：`{EC:MAX}`、`{COMM:TX|RX|RATE|STATUS}`、`{FRAME:TYPE|CATEGORY}`、`{ENG:CLUSTER}`、`{SOLAR:ACTIVE}`、`{CREW:CAP|PCT}`、`{SPD:MODE}`、`{ALT:MODE}`、`{TGT:HAS|STATUS}` 等。
2. **组件出厂默认模板就用了词典外 token**（说明缺口不是"用户想得太花"，是内建依赖也不可发现）
   - `ElectricalSystemWidget.cs:175` → `{EC}/{EC:MAX} EC`
   - `CommSignalWidget.cs:331` → `{COMM} | {COMM:RATE}`
   - `BottomControlsWidget.cs:110` → `{FRAME:TYPE}`
3. **GUI 编辑入口缺口**
   - 仅 `ecam_dial / tape / bar_gauge` 三类有"数据源绑定"卡（`TabAssembler.cs:245-247`），且只能点词典行写 `NumericToken`，**无自由文本输入框**（`TabAssembler.cs:437-448`）→ 词典外 token 无法在 GUI 绑定。
   - 有 `NumericToken` 支持但无绑定入口的反例：`spacex.*` 表盘（走 `TOKEN=` 通道）、`ArcMeterWidget`（`core.vsi`/`core.propellant`，`ArcMeterWidget.cs:62-84`）。
   - 模板文本域只对 `custom.*` 显示（`TabAssembler.cs:249-251`）；约 20 个使用 `GetTemplateChannel` 的组件（EICAS、ND、TimeWarp、LifeSupport、ElectricalSystem、StageDeltaV、CommSignal、BottomControls、SASDial、Toolbar…）的合法通道键（`SLOT0_TOKEN`、`UT_FORMAT`、`TOTAL_DV_FORMAT`、`TITLE`…）GUI 完全不暴露，只能手改 cfg。
4. **词典→组件的"插入模板"是死链**
   - 插入格式 `"DisplayName: {Token}"`（`TabAssembler.cs:711-720`）；目标 custom 卡只解析 `CHn=` 且需含 `;`（`CustomTokenTextWidget.cs:89-144`）→ 插入文本永远不会被消费。
5. **GUI 预览与实机不一致（掩盖问题）**
   - 模板卡预览用引擎整串求值（`TabAssembler.cs:552-568`），而实机按组件各自解析 → 预览"正常"不代表组件真渲染。
6. **双注册表**
   - 词典动态注入：`Core/Probes/TelemetryProbeManager.cs:108` → `TelemetryCatalog.RegisterDynamicParams`；引擎侧另有 `ExternalProbeRegistry` 软反射兜底（`TelemetryTokenEngine.cs:137-144, 308-315`）。两张表独立演化，无一致性校验。

## C. 重构锚点（供合并方案参考，不在本报告展开）

1. **单一事实源**：以引擎注册表为权威，把 tag/子标签/合法格式/默认单位/默认量程与告警门限登记在一处，`TelemetryCatalog` 自动生成（含探针条目走同一注册通道）。
2. **组件通道 schema 声明化**：如 `[WidgetChannel("TOTAL_DV_FORMAT", typeof(token模板), 描述)]`，GUI 按组件类型渲染通道编辑器并做键名/值校验。
3. **统一编辑动作**：词典行按目标组件能力给出"绑定 NumericToken / 插入通道 / 复制"，格式由 schema 决定，不再由 GUI 猜。
4. **预览与运行时同源**：预览复用组件的实际解析路径（或共用同一 Parse 层），消除 4/5 类不一致。
5. **语法校验**：对未知子标签/格式给出告警（如 `{VSI:NORM}`），取代静默忽略。

## D. 证据索引速查

- 引擎：`Core/Telemetry/TelemetryTokenEngine.cs`（16, 203-220, 248, 320-767）
- 通道解析：`UI/Framework/BaseFlightWidget.cs:552-566, 891-930`
- 词典：`UI/Settings/TelemetryCatalog.cs`（42-233 参数表；265-276 动态注入）
- 装配台：`UI/Settings/TabAssembler.cs`（245-256 分派；437-448 绑定卡；552-568 预览；583-738 词典与插入）
- 组件方言样例：`CustomTokenTextWidget.cs:89-144`、`EcamDialGaugeWidget.cs:199-260`、`SpaceXArcGaugeWidget.cs:60-88`、`ArcMeterWidget.cs:62-84`
- 内建模板依赖缺口：`ElectricalSystemWidget.cs:175`、`CommSignalWidget.cs:331`、`BottomControlsWidget.cs:110`