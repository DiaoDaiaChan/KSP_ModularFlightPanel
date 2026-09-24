# ModularFlightPanel 遥测数据源与探针全量词典 (Telemetry Data Sources & Probes Guide)

> **面向 AI Agent 与航电开发者的权威导引手册**  
> 本文档详尽罗列了 ModularFlightPanel (MFP) 架构内所有机载原生遥测、通配符计算引擎、解耦仿真系统以及全部 15 大外部 Mod 反射探针所提供的数据源。  
> 当 AI Agent 需要设计新仪表（Widget）、绑定遥测通配符、计算物理量、添加警报阈值或对接第三方模组时，**必须以此文件为权威基准检索并调用对应数据源**。

---

## 目录
- [一、遥测架构与消费模式 (Architecture & Consumption Models)](#一遥测架构与消费模式-architecture--consumption-models)
- [二、原生机载遥测数据源 (`IFlightTelemetry`)](#二原生机载遥测数据源-iflighttelemetry)
  - [2.1 空间动力学、姿态与高度 (Kinematics & Attitude)](#21-空间动力学姿态与高度-kinematics--attitude)
  - [2.2 推进系统、推重比与分级 (Propulsion & Staging)](#22-推进系统推重比与分级-propulsion--staging)
  - [2.3 轨道力学与变轨机动 (Orbital Mechanics & Maneuvers)](#23-轨道力学与变轨机动-orbital-mechanics--maneuvers)
  - [2.4 电气网络与直流母线 (Electrical Power)](#24-电气网络与直流母线-electrical-power)
  - [2.5 通信网络与链路 (Communications & CommNet)](#25-通信网络与链路-communications--commnet)
  - [2.6 维生环境与乘员状态 (Life Support & Environment)](#26-维生环境与乘员状态-life-support--environment)
  - [2.7 飞行控制系统与配平 (Flight Controls & Trims)](#27-飞行控制系统与配平-flight-controls--trims)
  - [2.8 任务时钟与时间加速 (Mission Time & Warp)](#28-任务时钟与时间加速-mission-time--warp)
- [三、通配符求值引擎规范 (`TelemetryTokenEngine`)](#三通配符求值引擎规范-telemetrytokenengine)
  - [3.1 语法规则与格式化后缀](#31-语法规则与格式化后缀)
  - [3.2 原生内置通配符速查表](#32-原生内置通配符速查表)
- [四、外部模组探针全量词典 (15 External Mod Probes)](#四外部模组探针全量词典-15-external-mod-probes)
  - [4.1 FAR (Ferram Aerospace Research) 气动解算探针](#41-far-ferram-aerospace-research-气动解算探针)
  - [4.2 KER (Kerbal Engineer Redux) 高精分级探针](#42-ker-kerbal-engineer-redux-高精分级探针)
  - [4.3 MechJeb 2 (MJ) 飞行计算状态机探针](#43-mechjeb-2-mj-飞行计算状态机探针)
  - [4.4 Principia 高精度摄动与变轨机动探针](#44-principia-高精度摄动与变轨机动探针)
  - [4.5 RealAntennas 真实射频通信探针](#45-realantennas-真实射频通信探针)
  - [4.6 Kerbalism 辐射与复杂维生探针](#46-kerbalism-辐射与复杂维生探针)
  - [4.7 Trajectories 气阻落点预测探针](#47-trajectories-气阻落点预测探针)
  - [4.8 DPAI (Docking Port Alignment) 对接引导探针](#48-dpai-docking-port-alignment-对接引导探针)
  - [4.9 GPWS (Ground Proximity Warning) 近地告警探针](#49-gpws-ground-proximity-warning-近地告警探针)
  - [4.10 RealFuels 沉底与点火可靠性探针](#410-realfuels-沉底与点火可靠性探针)
  - [4.11 TestFlight 引擎老化与失效率探针](#411-testflight-引擎老化与失效率探针)
  - [4.12 DynamicBatteryStorage 实时电网净充放探针](#412-dynamicbatterystorage-实时电网净充放探针)
  - [4.13 SystemHeat 复杂热力回路与温控探针](#413-systemheat-复杂热力回路与温控探针)
  - [4.14 AtmosphereAutopilot (AA) 电传飞控探针](#414-atmosphereautopilot-aa-电传飞控探针)
  - [4.15 RP-1 Avionics 真实航电质量锁探针](#415-rp-1-avionics-真实航电质量锁探针)
- [五、解耦仿真数据源 (`TelemetrySimulationEngine`)](#五解耦仿真数据源-telemetrysimulationengine)
- [六、AI Agent 典型代码范式与调用指南](#六ai-agent-典型代码范式与调用指南)

---

## 一、遥测架构与消费模式 (Architecture & Consumption Models)

MFP 采用严格分层的遥测管线，所有 UI 组件均 **100% 隔离底层真实游戏类**（禁止直接访问 `FlightGlobals`、`Vessel`、`Part`、`ModuleEngines`），确保在脱机测试与 Unity 无头预览中可完全运行：

```
[真实飞行场景 (FlightGlobals/Vessel)] ──┐
                                      ├──> [TelemetryHub] ──┐
[外部 15 大 Mod (反射探测)] ───────────┘                     │
                                                           ▼
[脱机无头测试 (7阶段解耦物理仿真)] ───────> [TelemetrySimulationEngine]
                                                           │
                                                           ▼
                                               [IFlightTelemetry 契约接口]
                                                           │
                      ┌────────────────────────────────────┼────────────────────────────────────┐
                      ▼                                    ▼                                    ▼
         【类型安全强类型直接读取】               【通配符文本动态解析求值】               【指针/表盘连续双精度求值】
          telemetry.SurfaceSpeed                TelemetryTokenEngine.Evaluate()       TelemetryTokenEngine.EvaluateNumeric()
```

### 消费途径总览：
1. **强类型只读访问**：在 `BaseFlightWidget.OnUpdateTelemetry(IFlightTelemetry telemetry)` 中直接读取 `telemetry.CurrentSpeed`、`telemetry.Apoapsis` 等属性。性能最高，零 GC 损耗。
2. **通配符文本驱动**：使用 `TelemetryTokenEngine.Evaluate("{SPD:SURF:F1} m/s", telemetry)` 将带占位符的字符串模板渲染为本地化 UI 文本。
3. **连续量表盘驱动**：使用 `TelemetryTokenEngine.EvaluateNumeric("{ALT:AGL}", telemetry)` 取得标量数值以驱动指针旋转、填充条进度或滚动尺。
4. **外部探针解析**：使用 `ExternalProbeRegistry.ResolveNumeric("FAR:AOA")` 或在通配符中使用 `{FAR:AOA}`，在外部 Mod 安装时自动命中高阶参数，未安装时安全回退。

---

## 二、原生机载遥测数据源 (`IFlightTelemetry`)

所有字段均可在实现 `BaseFlightWidget` 时通过 `telemetry.` 属性直接获取。

### 2.1 空间动力学、姿态与高度 (Kinematics & Attitude)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `CurrentSpeed` | `double` | m/s | 0 ~ 15000+ | `{SPD}` | 当前模式下的主速度（根据地表/轨道/目标模式自动切换） |
| `SurfaceSpeed` | `double` | m/s | 0 ~ 8000+ | `{SPD:SURF}` | 相对于当前母星地表的真地表线速度 |
| `OrbitalSpeed` | `double` | m/s | 0 ~ 12000+ | `{SPD:OBT}` | 开普勒惯性参考系下的轨道运行速度 |
| `TargetSpeed` | `double` | m/s | 0 ~ 2000+ | `{SPD:TGT}` | 相对于当前锁定目标的相对速度矢量标量 |
| `Mach` | `double` | M | 0 ~ 30+ | `{MACH}` | 当前大气音速比（地表马赫数） |
| `AltitudeASL` | `double` | m | -1000 ~ 100M+ | `{ALT:ASL}` | 海拔高度（相对于母星大地水准面） |
| `AltitudeAGL` | `double` | m | 0 ~ 50000+ | `{ALT:AGL}` | 雷达真高（探地雷达测量的地形净空高度） |
| `DisplayAltitude` | `double` | m | 变动 | `{ALT}` | KSP 顶栏当前选定模式的高度数值 |
| `VerticalSpeed` | `double` | m/s | -5000 ~ +5000 | `{VSI}` | 垂直爬升(+)或下沉(-)瞬时线速度 |
| `NormalizedVSI` | `float` | - | -1.0 ~ +1.0 | - | 非线性对数化 VSI 标量，专用于驱动航电竖直升降针 |
| `Heading` | `float` | ° (deg) | 0.0 ~ 359.9 | `{HDG}` | 罗盘航向角（以真北为 000° 顺时针计数） |
| `Pitch` | `float` | ° (deg) | -90.0 ~ +90.0 | `{PITCH}` | 相对于当地水平地平线的俯仰角（上抬为正，下俯为负） |
| `Roll` | `float` | ° (deg) | -180.0 ~ +180.0 | `{ROLL}` | 绕机身纵轴旋转角（右倾为正，左倾为负） |
| `AttitudeRotation` | `Quaternion`| - | 四元数 | - | 载具当前在世界空间的三维姿态四元数（用于驱动 3D 姿态球） |
| `DynamicPressure` | `double` | kPa | 0 ~ 150+ | `{Q}` | 空气动力学迎面动压（Max-Q 关键参数） |
| `AtmosphericPressure` | `double` | kPa | 0 ~ 101.325+ | `{ATM}` | 外部大气静压（1 atm ≈ 101.325 kPa） |
| `GForce` | `double` | G | 0 ~ 25+ | `{GFORCE}` | 载具乘员所承受的瞬时过载 G 值 |
| `IsTouchdownAlert` | `bool` | - | true / false | - | 近地接地警报状态（高度 < 50m 且下沉速度 > 5m/s 时触发） |

---

### 2.2 推进系统、推重比与分级 (Propulsion & Staging)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Throttle` | `float` | - | 0.0 ~ 1.0 | `{THROTTLE}` | 引擎总油门输出指令开度（0% ~ 100%） |
| `TWR` | `double` | - | 0.0 ~ 20.0+ | `{TWR}` | 当前有效推力与载具当前重力比值（地表起飞需 > 1.0） |
| `StagePropellantFraction`| `float` | - | 0.0 ~ 1.0 | `{PROP}` | 当前激活分级剩余推进剂可用百分比（0% ~ 100%） |
| `StageDeltaV` | `double` | m/s | 0 ~ 8000+ | `{STAGE:DV}` | 当前工作级在当前环境（海平面/真空）下的剩余 Delta-V |
| `TotalDeltaV` | `double` | m/s | 0 ~ 25000+ | `{TOTAL:DV}` | 载具所有未点火分级的总可用累加 Delta-V |
| `StageBurnTime` | `double` | s | 0 ~ 3600+ | `{STAGE:TIME}`| 当前级在当前油门下的预计剩余燃烧时间（秒） |
| `TotalBurnTime` | `double` | s | 0 ~ 10000+ | `{TOTAL:TIME}`| 全舰所有分级发动机累计最大连续点火时长 |
| `CurrentStage` | `int` | - | 0 ~ 30 | `{STAGE}` | 当前激活分级序号 |
| `ActiveEngines` | `int` | - | 0 ~ 64 | `{ENG}` | 当前正在点火产生推力的发动机数量 |
| `TotalStageEngines` | `int` | - | 0 ~ 64 | `{ENG:TOTAL}` | 当前分级挂载的全部发动机总数（如猎鹰9号为 9，星舰为 33） |
| `StageDeltaVList` | `IReadOnlyList` | - | 列表 | - | 每一级的分级推力结构体详情（含 `DeltaV`、`BurnTime`、`TWR`、`Isp` 等） |
| `StagePropellantName` | `string` | - | 文本 | `{STAGE:PROPNAME}` | 主推进剂名称（如 "LqdHydrogen+LqdOxygen"、"Kerosene"） |
| `DeltaVSource` | `string` | - | 文本 | - | Delta-V 解算数据源（"Stock" / "KER" / "MechJeb"） |
| `IsStageLocked` | `bool` | - | true / false | - | 原版分级锁定安全锁状态（Alt+L） |

---

### 2.3 轨道力学与变轨机动 (Orbital Mechanics & Maneuvers)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Apoapsis` | `double` | m | -天体半径 ~ ∞ | `{AP}` | 远地点高度（轨道最高点距海平面距离） |
| `Periapsis` | `double` | m | -天体半径 ~ ∞ | `{PE}` | 近地点高度（轨道最低点距海平面距离，负值表示撞击） |
| `TimeToAp` | `double` | s | 0 ~ 360000+ | `{TAP}` | 到达远地点剩余倒计时时刻 |
| `TimeToPe` | `double` | s | 0 ~ 360000+ | `{TPE}` | 到达近地点剩余倒计时时刻 |
| `HasManeuverNode` | `bool` | - | true / false | - | 载具当前是否存在计划的变轨机动节点 |
| `ManeuverDeltaV` | `double` | m/s | 0 ~ 10000+ | `{MN:DV}` | 计划机动节点当前待消耗的剩余速度增量 |
| `ManeuverTotalDeltaV` | `double` | m/s | 0 ~ 10000+ | `{MN:TOTALDV}`| 计划机动节点初始总规划速度增量 |
| `ManeuverTimeToNode` | `double` | s | 0 ~ 360000+ | `{MN:TIME}` | 距离计划机动节点的到达时间倒计时 |
| `ManeuverBurnTime` | `double` | s | 0 ~ 3600+ | `{MN:BURNTIME}` | 根据当前推力与质量解算的点火持续时长 |
| `ManeuverTimeToBurn` | `double` | s | 0 ~ 360000+ | `{MN:TIMETOBURN}`| 提前点火倒计时时刻（按 50% 燃烧时长前置点火计算） |
| `ManeuverDeltaVPrograde` | `double` | m/s | -10000 ~ +10000 | `{MN:DV_PRO}` | 机动节点沿前向/切向的分量 |
| `ManeuverDeltaVNormal` | `double` | m/s | -10000 ~ +10000 | `{MN:DV_NORM}` | 机动节点沿法向/平面外的分量 |
| `ManeuverDeltaVRadial` | `double` | m/s | -10000 ~ +10000 | `{MN:DV_RAD}` | 机动节点沿径向分量 |
| `ManeuverSource` | `string` | - | 文本 | - | 机动节点来源（"Stock" 或 "Principia"） |

---

### 2.4 电气网络与直流母线 (Electrical Power)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `ElectricCharge` | `double` | EC | 0 ~ 100000+ | `{EC}` | 当前全舰可用的蓄电池电量储备 |
| `MaxElectricCharge` | `double` | EC | 0 ~ 100000+ | `{EC:MAX}` | 全舰蓄电池总额定储电容量 |
| `EcPercent` | `double` | % | 0.0 ~ 100.0 | `{EC:PCT}` | 剩余电量百分比（低于 20% 应亮黄灯警告） |
| `NetEcRate` | `double` | EC/s | -500 ~ +500 | `{EC:RATE}` | 净充放电率（正数表示净充入，负数表示净亏电） |
| `BusVoltage` | `float` | V | 0.0 ~ 36.0 | `{EC:VOLT}` | 直流航电总线电压（标称 28V，低于 22V 属于欠压） |
| `SolarPower` | `double` | EC/s | 0 ~ 500+ | `{SOLAR}` | 太阳能帆板总发电功率 |

---

### 2.5 通信网络与链路 (Communications & CommNet)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `CommSignal` | `double` | - | 0.0 ~ 1.0 | `{COMM}` | 归一化综合通信信号强度（0% ~ 100%） |
| `IsConnected` | `bool` | - | true / false | - | 是否具备连接至 KSC 或中继卫星的有效链路 |
| `ControlLevelStr` | `string` | - | 文本 | `{COMM:CTRL}` | 飞控指令授权等级（"Full" / "Partial" / "None"） |
| `AntennaCount` | `int` | - | 0 ~ 32 | `{COMM:ANTCOUNT}`| 全舰激活天线部件数量 |
| `DataRateBps` | `double` | bps | 0 ~ 100M+ | `{COMM:DATARATE}`| 当前活动链路数据传输速率（如 5.2 Mbps） |
| `DirectLinkTarget` | `string` | - | 文本 | `{COMM:TARGET}` | 当前直接相连的地面站或中继星名称 |
| `ActiveCommLinks` | `IReadOnlyList` | - | 列表 | - | 活跃通信路径详情（含中继跳数与各级衰减） |

---

### 2.6 维生环境与乘员状态 (Life Support & Environment)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `CrewCount` | `int` | - | 0 ~ 32 | `{CREW}` | 当前驻留乘员总人数 |
| `CrewCapacity` | `int` | - | 0 ~ 32 | `{CREW:MAX}` | 载人舱体最大可容纳定员人数 |
| `CabinPressure` | `double` | kPa | 0.0 ~ 120.0 | `{CABIN:PRES}` | 乘员舱内部环境气压（标称 101.3 kPa） |
| `CabinTemp` | `double` | °C | -40.0 ~ +80.0 | `{CABIN:TEMP}` | 乘员舱生活区温度（舒适区间 18°C ~ 26°C） |
| `OxygenPercent` | `float` | % | 0.0 ~ 100.0 | `{O2}` | 维生氧气储备可用百分比 |
| `WaterPercent` | `float` | % | 0.0 ~ 100.0 | `{WATER}` | 饮用与水循环储备可用百分比 |
| `MonoPercent` | `float` | % | 0.0 ~ 100.0 | `{MONO}` | RCS 单组元姿控推进剂储备百分比 |

---

### 2.7 飞行控制系统与配平 (Flight Controls & Trims)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `IsRCSEnabled` | `bool` | - | true / false | `{RCS}` | RCS 姿控喷气总开关状态 |
| `IsSASEnabled` | `bool` | - | true / false | `{SAS}` | SAS 增稳与自动定向总开关状态 |
| `CurrentSASMode` | `FlightSASMode`| 枚举 | 见下表 | `{SAS:MODE}` | 当前 SAS 引导工作模式 |
| `SpeedModeName` | `string` | - | 文本 | `{SPEEDMODE}` | 速度显示模式（"Surface" / "Orbit" / "Target"） |
| `PitchInput` | `float` | - | -1.0 ~ +1.0 | `{INPUT:PITCH}` | 操纵杆俯仰当前输入量 |
| `RollInput` | `float` | - | -1.0 ~ +1.0 | `{INPUT:ROLL}` | 操纵杆滚转当前输入量 |
| `YawInput` | `float` | - | -1.0 ~ +1.0 | `{INPUT:YAW}` | 方向舵偏航当前输入量 |
| `PitchTrim` | `float` | - | -1.0 ~ +1.0 | `{TRIM:PITCH}` | 俯仰配平微调量 |
| `RollTrim` | `float` | - | -1.0 ~ +1.0 | `{TRIM:ROLL}` | 滚转配平微调量 |
| `YawTrim` | `float` | - | -1.0 ~ +1.0 | `{TRIM:YAW}` | 偏航配平微调量 |
| `IsPrecisionControl`| `bool` | - | true / false | `{PRECISION}` | 是否开启 CapsLock 灵敏微调精度控制 |
| `IsDockingMode` | `bool` | - | true / false | `{DOCKINGMODE}`| 是否处于对接操纵模式（平移控制） |

*`FlightSASMode` 枚举定义*：`StabilityAssist`, `Prograde`, `Retrograde`, `Normal`, `Antinormal`, `RadialIn`, `RadialOut`, `Target`, `AntiTarget`, `Maneuver`。

---

### 2.8 任务时钟与时间加速 (Mission Time & Warp)

| 属性名 (`IFlightTelemetry`) | 类型 | 单位 | 标准物理区间 | 对应通配符 | 说明与 Agent 引导 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `MissionTime` | `double` | s | 0 ~ ∞ | `{MET}` | 任务已执行时间 (MET)，格式化支持 `:TIME` |
| `UniversalTime` | `double` | s | 0 ~ ∞ | `{UT}` | 坎巴拉宇宙世界通用时间 (UT) |
| `TimeWarpRate` | `float` | x | 1.0 ~ 100000x | `{WARP:RATE}` | 当前时间加速倍率（如 1x, 5x, 10000x） |
| `TimeWarpRateIndex` | `int` | - | 0 ~ 7 | `{WARP:INDEX}`| 当前加速档位索引 |
| `MaxTimeWarpRateIndex`| `int` | - | 0 ~ 7 | `{WARP:MAX}` | 当前环境（大气层内或近地）所允许的最高加速档位 |
| `IsPhysicsWarp` | `bool` | - | true / false | `{WARP:ISPHYS}`| 当前是否属于物理时间加速（1x ~ 4x） |
| `IsGamePaused` | `bool` | - | true / false | - | 游戏是否处于暂停状态 |

---

## 三、通配符求值引擎规范 (`TelemetryTokenEngine`)

### 3.1 语法规则与格式化后缀

通配符表达式统一采用花括号包裹：`{TAG}` 或 `{TAG:SUBTAG}` 或 `{TAG:SUBTAG:FORMAT}`。

#### 支持的格式化后缀 (Format Modifiers)：
1. **标准数值修饰符**：
   - `:F0` / `:F1` / `:F2`：保留指定小数位（如 `{SPD:F1}` -> `175.4`）。
   - `:N0` / `:N1`：千分位逗号分割（如 `{ALT:ASL:N0}` -> `12,450`）。
   - `:P0` / `:P1`：百分比格式化（如 `{PROP:P0}` -> `85%`）。
   - `:D2` / `:D3`：前导零整型格式化（如 `{HDG:D3}` -> `085`，用于罗盘航向）。
2. **工程智能自适应修饰符**：
   - `:DIST`：智能距离阶梯缩放。
     - `< 1,000 m` 显示为 `XXX m`；
     - `1,000 m ~ 1,000,000 m` 显示为 `XXX.X km`；
     - `> 1,000,000 m` 显示为 `XXX.X Mm`。
     - *示例*：`{ALT:ASL:DIST}`, `{AP:DIST}`, `{PE:DIST}`, `{MN:DIST}`。
   - `:TIME`：智能倒计时/时间段。
     - `< 60 s` 显示为 `00:XX`；
     - `< 1 h` 显示为 `MM:SS`；
     - `> 1 h` 显示为 `HH:MM:SS`；
     - `> 24 h` 显示为 `Xd HH:MM`。
     - *示例*：`{TAP:TIME}`, `{TPE:TIME}`, `{MN:TIME}`。
   - `:KMH`：时速转换（速度乘以 3.6，符合 SpaceX 航电广播规范）。
     - *示例*：`{SPD:SURF:KMH}`。
   - `:KM`：千米转换（高度除以 1000）。
     - *示例*：`{ALT:ASL:KM}`。

---

### 3.2 原生内置通配符速查表

| 通配符语法 | 提取源 | 示例输出 | 典型应用组件 |
| :--- | :--- | :--- | :--- |
| `{SPD}` | `CurrentSpeed` | `350.2` | 航速滚带、数显航电表 |
| `{SPD:SURF}` | `SurfaceSpeed` | `240.0` | PFD 速度带、降落仪 |
| `{SPD:SURF:KMH}` | `SurfaceSpeed * 3.6` | `1024` | SpaceX 风格速度条 |
| `{SPD:OBT}` | `OrbitalSpeed` | `2250.4` | 轨道航电卡片 |
| `{MACH}` | `Mach` | `1.45` | 超音速指示针、跨音速警告 |
| `{ALT:ASL}` | `AltitudeASL` | `12500` | 高度计、主 PFD 滚带 |
| `{ALT:ASL:DIST}` | `AltitudeASL (智能)` | `12.5 km` | 导航综合信息板 |
| `{ALT:AGL}` | `AltitudeAGL` | `450.2` | 探地雷达、自杀刹车辅助 |
| `{VSI}` | `VerticalSpeed` | `+15.2` | 垂直爬升速率表 |
| `{HDG:D3}` | `Heading` | `090` | 罗盘、航向指引弧 |
| `{PITCH:F1}` | `Pitch` | `+12.4` | 俯仰指示、地平仪 |
| `{ROLL:F1}` | `Roll` | `-05.2` | 滚转指引、微调指示 |
| `{THROTTLE:F0}` | `Throttle * 100` | `100` | 油门推力条 |
| `{TWR:F2}` | `TWR` | `1.85` | 起飞推重比监视器 |
| `{GFORCE:F1}` | `GForce` | `2.4` | 过载安全告警仪 |
| `{Q:F1}` | `DynamicPressure` | `32.5` | Max-Q 动压仪表 |
| `{PROP:P0}` | `StagePropellantFraction` | `78%` | 分级燃料可用进度条 |
| `{AP:DIST}` | `Apoapsis` | `150.2 km` | 轨道参量列表 |
| `{PE:DIST}` | `Periapsis` | `85.4 km` | 轨道参量列表 |
| `{TAP:TIME}` | `TimeToAp` | `04:12` | 变轨倒计时 |
| `{TPE:TIME}` | `TimeToPe` | `32:15` | 近地点倒计时 |
| `{MN:DV:F0}` | `ManeuverDeltaV` | `420` | 机动变轨时间线 |
| `{MN:TIME}` | `ManeuverTimeToNode` | `01:30` | 机动倒计时指示器 |
| `{EC:PCT:F0}` | `EcPercent` | `94%` | 电量状态环、电池槽 |
| `{EC:RATE:+0.0;-0.0}` | `NetEcRate` | `+2.4` | 电网充放平衡指示 |
| `{COMM:PCT}` | `CommSignal * 100` | `85%` | 通信网络天线信号 |
| `{CREW}` | `CrewCount` | `3` | 乘员状态卡片 |

---

## 四、外部模组探针全量词典 (15 External Mod Probes)

MFP 内置 **15 大外部模组自动探针中枢**（`Core/Probes/*`），采用 `ProbeReflectionTraverser` 零遗漏动态遍历外部程序集。外部 Mod 未安装时，探针自动停用，通配符返回安全默认值或 `double.NaN`。

---

### 4.1 FAR (Ferram Aerospace Research) 气动解算探针
- **源码文件**：[`FarProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/FarProbe.cs)
- **探针标识**：`FAR`
- **状态判定**：`FarProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{FAR:IAS}` | `ActiveVesselIAS` | `double` | m/s | FAR 空气动力学指示空速 (Indicated Airspeed) |
| `{FAR:EAS}` | `ActiveVesselEAS` | `double` | m/s | FAR 等效空速 (Equivalent Airspeed) |
| `{FAR:Q}` / `{FAR:DYNAERO}` | `ActiveVesselDynPres` | `double` | kPa | FAR 精确环境大气动压解算 |
| `{FAR:AOA}` | `ActiveVesselAoA` | `double` | ° (deg) | 载具纵轴与气流真实来流攻角（迎角） |
| `{FAR:SIDESLIP}` | `ActiveVesselSideslip` | `double` | ° (deg) | 机身偏航与来流侧向偏角（侧滑角） |
| `{FAR:LIFT}` / `{FAR:LIFTCOEFF}` | `ActiveVesselLiftCoeff` | `double` | - | 当前翼面与机体总升力系数 $C_L$ |
| `{FAR:DRAG}` / `{FAR:DRAGCOEFF}` | `ActiveVesselDragCoeff` | `double` | - | 当前机体总气动阻力系数 $C_D$ |
| `{FAR:LD}` / `{FAR:LiftToDragRatio}` | 自定义注册 | `double` | - | 即时气动升阻效率比值 ($L/D = C_L / C_D$) |
| `{FAR:STALL}` | `ActiveVesselStallFrac` | `double` | % | 翼面气流分离失速比例 (0.0 ~ 1.0) |
| `{FAR:BALLISTIC}` | `ActiveVesselBallisticCoeff` | `double` | kg/m² | 弹道系数 (Ballistic Coefficient) |
| `{FAR:TSFC}` | `ActiveVesselTSFC` | `double` | kg/(N·h)| 喷气推力燃油消耗率 |
| `{FAR:REFAREA}` | `ActiveVesselRefArea` | `double` | m² | 气动参考受力截面积 |
| `{FAR:TERMVEL}` | `ActiveVesselTermVelEst` | `double` | m/s | 终端沉降平衡速率估计值 |
| `{FAR:AEROFORCE}` | `ActiveVesselAerodynamicForce` | `Vector3` | kN | 三轴气动力综合矢量 |

---

### 4.2 KER (Kerbal Engineer Redux) 高精分级探针
- **源码文件**：[`KerbalEngineerProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/KerbalEngineerProbe.cs)
- **探针标识**：`KER`
- **状态判定**：`KerbalEngineerProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{KER:DV}` / `{KER:STAGEDV}` | `deltaV` | `double` | m/s | KER 精确解算之当前工作级真空/海平面 Delta-V |
| `{KER:TOTALDV}` | `totalDeltaV` | `double` | m/s | 载具全部未激活级累计有效总 Delta-V |
| `{KER:TWR}` / `{KER:STAGETWR}` | `actualThrustToWeight` | `double` | - | KER 权威高精度实时推重比 |
| `{KER:BURNTIME}` | `time` | `double` | s | 当前分级满推力额定持续点火时长 |
| `{KER:TOTALBURNTIME}` | `totalTime` | `double` | s | 全部分级总计点火燃烧时长 |
| `{KER:ISP}` | `isp` | `double` | s | 当前点火引擎综合比冲 |
| `{KER:THRUST}` | `thrust` | `double` | kN | 发动机当前可用理论总推力 |
| `{KER:ACTUALTHRUST}` | `actualThrust` | `double` | kN | 发动机实际输出推力 |
| `{KER:SUICIDECD}` | `SuicideCountdown` | `double` | s | 自杀式减速刹车最佳点火时刻倒计时 |
| `{KER:SUICIDEALT}` | `SuicideAltitude` | `double` | m | 自杀式减速预计启动的海拔/雷达真高 |
| `{KER:SUICIDEDV}` | `SuicideDeltaV` | `double` | m/s | 自杀式刹车接地所需消耗的速度增量 |
| `{KER:IMPACTTIME}` | `Time` | `double` | s | 轨道坠落接地撞击倒计时 |
| `{KER:HOTTESTTEMP}` | `HottestTemperature` | `double` | K | 全舰最热零部件即时开尔文温度 |
| `{KER:NODEDV}` | `NodeDeltaV` | `double` | m/s | KER 解算之下一机动节点 Delta-V |

---

### 4.3 MechJeb 2 (MJ) 飞行计算状态机探针
- **源码文件**：[`MechJebProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/MechJebProbe.cs)
- **探针标识**：`MJ`
- **状态判定**：`MechJebProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{MJ:DV}` / `{MJ:STAGEDV}` | `deltaVStage` | `double` | m/s | MechJeb 分级状态机解算之当前级 Delta-V |
| `{MJ:TOTALDV}` | `deltaVTotal` | `double` | m/s | MechJeb 解算之载具全部分级总可用 Delta-V |
| `{MJ:TWR}` | `twr` | `double` | - | MechJeb 即时推重比 |
| `{MJ:SURFACETWR}` | `SurfaceTWR` | `double` | - | 基于母星地表标准重力加速度 ($g_0$) 的基准推重比 |
| `{MJ:LOCALTWR}` | `LocalTWR` | `double` | - | 基于当前高度真实微重力引力场的局部推重比 |
| `{MJ:TERMINALVEL}` | `terminalVelocity` | `double` | m/s | 当前大气密度与载具阻力平衡下的终端沉降速度 |
| `{MJ:NODEDV}` | `NextManeuverNodeDeltaV` | `double` | m/s | 自动驾驶仪计划机动节点待执行 Delta-V |
| `{MJ:TIMETONODE}` | `TimeToManeuverNode` | `double` | s | 到达 MechJeb 节点时刻倒计时 |
| `{MJ:NODEBURNTIME}` | `NextManeuverNodeBurnTime` | `double` | s | MechJeb 预估变轨点火时长 |
| `{MJ:COORDINATES}` | `GetCoordinateString` | `string` | 文本 | 经纬度格式化坐标文本（如 `0°12'S 74°33'W`） |
| `{MJ:ORBITSUMMARY}` | `CurrentOrbitSummary` | `string` | 文本 | 当前开普勒轨道特征简报（Ap / Pe / 倾角 / 周期） |

---

### 4.4 Principia 高精度摄动与变轨机动探针
- **源码文件**：[`PrincipiaProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/PrincipiaProbe.cs)
- **探针标识**：`PRINCIPIA` (别名 `PRIN`)
- **状态判定**：`PrincipiaProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{PRINCIPIA:FRAME}` | `PlottingFrameName` | `string` | 文本 | 当前选定的 Principia 轨迹绘制参考系全称 |
| `{PRINCIPIA:NAVBALLNAME}` | `NavballFrameName` | `string` | 文本 | 当前姿态球专用参考系简称（如 `ECI`, `Barycentric`） |
| `{PRINCIPIA:DV}` | `ManeuverDeltaV` | `double` | m/s | Principia 飞行计划中下一次机动所需总标量 Delta-V |
| `{PRINCIPIA:DV_PRO}` | `ManeuverDeltaVPrograde` | `double` | m/s | 切向/前向机动分量 (Tangent / Prograde) |
| `{PRINCIPIA:DV_NORM}` | `ManeuverDeltaVNormal` | `double` | m/s | 法向/平面外机动分量 (Normal / Binormal) |
| `{PRINCIPIA:DV_RAD}` | `ManeuverDeltaVRadial` | `double` | m/s | 径向机动分量 (Radial) |
| `{PRINCIPIA:BURNTIME}` | `ManeuverDuration` | `double` | s | 变轨满推力持续燃烧持续时间 |
| `{PRINCIPIA:TIMETOBURN}` | `TimeToManeuver` | `double` | s | 距离下一次机动点火时刻倒计时 |
| `{PRINCIPIA:ORBITDESC}` | `OrbitDescription` | `string` | 文本 | 高精非开普勒摄动数值积分下的轨道类型描述 |
| `{PRINCIPIA:NODALPERIOD}` | `NodalPeriod` | `double` | s | 轨道穿过升交点的真实摄动交点公转周期 |
| `{PRINCIPIA:COLLISION}` | `FirstCollisionTime` | `double` | s | 摄动数值积分下首次撞击天体表面的预测时间戳 |

---

### 4.5 RealAntennas 真实射频通信探针
- **源码文件**：[`RealAntennasProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/RealAntennasProbe.cs)
- **探针标识**：`RA`
- **状态判定**：`RealAntennasProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{RA:DATARATE}` | `ActiveDataRate` | `double` | bps | 当前链路真实数据吞吐带宽（支持智能换算为 kbps / Mbps） |
| `{RA:GAIN}` | `AntennaGain` | `float` | dBi | 对向天线物理增益 (Gain) |
| `{RA:TXPOWER}` | `TxPower` | `float` | dBm | 射频发射功率电平 |
| `{RA:FREQUENCY}` | `Frequency` | `float` | Hz | 射频载波工作频率（如 S-band, X-band, Ka-band） |
| `{RA:BANDWIDTH}` | `Bandwidth` | `double` | Hz | 信道传输带宽 |
| `{RA:POWERDRAW}` | `PowerDraw` | `float` | EC/s | 射频天线在发射工作状态下的实时电力消耗 |
| `{RA:IDLEPOWER}` | `IdlePowerDraw` | `double` | EC/s | 天线接收与待机状态底噪电耗 |
| `{RA:STRENGTH}` | `SignalStrength` | `double` | - | 归一化信噪比裕度 (0.0 ~ 1.0) |
| `{RA:TARGET}` | `TargetName` | `string` | 文本 | 当前建立通信的对端地面站或中继航天器名称 |
| `{RA:ISCONNECTED}` | `IsConnectedHome` | `bool` | - | 是否具备对地基准测控站完整通信链路 |

---

### 4.6 Kerbalism 辐射与复杂维生探针
- **源码文件**：[`KerbalismProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/KerbalismProbe.cs)
- **探针标识**：`KERBALISM`
- **状态判定**：`KerbalismProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{KERBALISM:RADIATION}` | `Radiation` | `double` | rad/h | 当前空间环境总辐射环境场强度 |
| `{KERBALISM:HABITATRADIATION}` | `HabitatRadiation` | `double` | rad/h | 经过屏蔽后乘员舱内部吸收辐射剂量率 |
| `{KERBALISM:PRESSURE}` | `Pressure` | `double` | atm | 舱内生命维持系统工作气压 (atm) |
| `{KERBALISM:POISONING}` | `Poisoning` | `double` | % | 二氧化碳废气积聚中毒度 (0% ~ 100%) |
| `{KERBALISM:SHIELDING}` | `Shielding` | `double` | % | 载具乘员舱辐射屏蔽防护系数 |
| `{KERBALISM:COMFORT}` | `Comfort` | `double` | - | 空间生活舒适度系数（影响乘员精神崩溃倒计时） |
| `{KERBALISM:TEMPERATURE}` | `Temperature` | `double` | K | 外部空间有效辐射热平衡温度 |
| `{KERBALISM:STORM}` | `InStorm` | `bool` | - | 是否正处于 CME 太阳日冕物质抛射风暴高能辐射轰击中 |
| `{KERBALISM:MALFUNCTION}` | `Malfunction` | `bool` | - | 全舰是否存在未修复的机械或电气设备故障 |
| `{KERBALISM:CRITICAL}` | `Critical` | `bool` | - | 是否存在危及乘员生命的致命恶性故障 |
| `{KERBALISM:SOLAREXPOSURE}` | `SolarPanelsExposure`| `double` | % | 太阳能帆板综合光照受光效率 |
| `{KERBALISM:DRIVESPACE}` | `DrivesFreeSpace` | `double` | MB | 科学数据硬盘剩余存储空间 |

---

### 4.7 Trajectories 气阻落点预测探针
- **源码文件**：[`TrajectoriesProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/TrajectoriesProbe.cs)
- **探针标识**：`TRAJ`
- **状态判定**：`TrajectoriesProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{TRAJ:TIME_TO_IMPACT}` | `TimeToImpact` | `double` | s | 经过大气气动减速数值积分后的接地/撞击倒计时 |
| `{TRAJ:IMPACT_LAT}` | `ImpactLatitude` | `double` | ° (deg) | 大气积分预测着陆落点地理纬度 (-90° ~ +90°) |
| `{TRAJ:IMPACT_LON}` | `ImpactLongitude` | `double` | ° (deg) | 大气积分预测着陆落点地理经度 (-180° ~ +180°) |
| `{TRAJ:DIST_TO_TARGET}` | `TargetDistance` | `double` | m | 预测着陆点与目标着陆标靶的地面测地线偏差间距 |
| `{TRAJ:IMPACT_VEL}` | `ImpactVelocity` | `double` | m/s | 最终接地撞击瞬时预测合速度 |
| `{TRAJ:CORRECTED_AP}` | `AerobrakeAp` | `double` | m | 经过大气减速后出气层轨道修正远地点 |
| `{TRAJ:CORRECTED_PE}` | `AerobrakePe` | `double` | m | 经过大气减速后出气层轨道修正近地点 |

---

### 4.8 DPAI (Docking Port Alignment) 对接引导探针
- **源码文件**：[`DockingAlignmentProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/DockingAlignmentProbe.cs)
- **探针标识**：`DOCK`
- **状态判定**：`DockingAlignmentProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{DOCK:DIST}` | `Distance` | `float` | m | 与目标对接口平面的法向直线净距离 |
| `{DOCK:CVEL}` | `ClosureRate` | `float` | m/s | 沿对接口法向中心轴的靠拢靠进速度（靠拢为正） |
| `{DOCK:CDI_X}` | `DevX` | `float` | m / ° | 水平航向道十字偏移量（横向偏航偏移） |
| `{DOCK:CDI_Y}` | `DevY` | `float` | m / ° | 垂直下滑道十字偏移量（垂直高度偏离） |
| `{DOCK:ROLL_ERR}` | `RollOffset` | `float` | ° (deg) | 对接标记旋转滚转角差 (-180° ~ +180°) |
| `{DOCK:TARGET}` | `TargetName` | `string` | 文本 | 目标对接口所属载具或端口名称 |
| `{DOCK:IS_ALIGNED}` | 自定义注册 | `bool` | - | 姿态角与横向偏移是否均进入容差走廊（对齐绿灯） |

---

### 4.9 GPWS (Ground Proximity Warning) 近地告警探针
- **源码文件**：[`GPWSProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/GPWSProbe.cs)
- **探针标识**：`GPWS`
- **状态判定**：`GPWSProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{GPWS:AGL}` | `RadarAltitude` | `double` | m | GPWS 内部解算的高可靠探地雷达垂直高度 |
| `{GPWS:SINK_RATE}` | `SinkRate` | `double` | m/s | 真实垂直下沉速率 |
| `{GPWS:HORSPEED}` | `HorSpeed` | `double` | m/s | 水平地速分量 |
| `{GPWS:V1}` | `V1Speed` | `float` | m/s | 起飞决断速度 V1（超过此速度必须起飞，不可刹停） |
| `{GPWS:VR}` | `TakeOffSpeed` | `float` | m/s | 抬前轮速度 Vr（推荐机头仰起离地空速） |
| `{GPWS:VREF}` | `LandingSpeed` | `float` | m/s | 进近着陆基准速度 Vref |
| `{GPWS:STALL_AOA}` | `StallAoa` | `float` | ° (deg) | 当前构型下的失速临界迎角门限 |
| `{GPWS:GEAR}` | `IsGearDown` | `bool` | - | 起落架是否已安全放下并锁好 |

---

### 4.10 RealFuels 沉底与点火可靠性探针
- **源码文件**：[`RealFuelsProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/RealFuelsProbe.cs)
- **探针标识**：`RF`
- **状态判定**：`RealFuelsProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{RF:IGNITIONS}` | `Ignitions` | `int` | 次 | 主发动机剩余可用点火器配额 (-1 表示无限制) |
| `{RF:ULLAGE}` | `Ullage` | `string` | 文本 | 燃料沉底状态文本 (`Very Stable`, `Risky`, `Very Risky`) |
| `{RF:ULLAGE_STABILITY}`| `UllageStability` | `double` | - | 沉底稳定性量化系数 (0.0 ~ 1.0) |
| `{RF:CAN_IGNITE}` | 自定义注册 | `bool` | - | 沉底与点火器是否均满足点火条件 (1/0) |
| `{RF:ENGINECONFIG}` | `EngineConfig` | `string` | 文本 | 当前发动机运作选定的真实化学动力配方模式 |

---

### 4.11 TestFlight 引擎老化与失效率探针
- **源码文件**：[`TestFlightProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/TestFlightProbe.cs)
- **探针标识**：`TF`
- **状态判定**：`TestFlightProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{TF:OPERATING_TIME}` | `OperatingTime` | `float` | s | 发动机点火启动后的累计连续运行秒数 |
| `{TF:FAIL_RATE}` | `FailureRate` | `double` | - | 发动机当前工况点的瞬时失效率（随运行时间指数爬升） |
| `{TF:FLIGHT_DATA}` | `FlightData` | `float` | du | 发动机型号积累的试飞数据量 (Data Units) |
| `{TF:STATUS}` | `Status` | `string` | 文本 | 引擎健康状态 (`Nominal`, `Warning`, `Failed`) |
| `{TF:HAS_FAILED}` | `Failed` | `bool` | - | 是否发生熄火、推力下降或爆炸等致命故障 |
| `{TF:FAIL_COUNT}` | `FailureCount` | `int` | - | 载具当前未排除的故障总数量 |

---

### 4.12 DynamicBatteryStorage 实时电网净充放探针
- **源码文件**：[`DynamicBatteryStorageProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/DynamicBatteryStorageProbe.cs)
- **探针标识**：`DBS`
- **状态判定**：`DynamicBatteryStorageProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{DBS:PROD}` | `TotalProduction` | `double` | EC/s | 全舰所有发电设备（太阳能/反应堆/燃料电池）综合发电功率 |
| `{DBS:CONS}` | `TotalConsumption` | `double` | EC/s | 全舰所有耗电设备（航电/通信/转子/生命支持）综合负载功率 |
| `{DBS:NET}` / `{DBS:NETRATE}` | `NetRate` | `double` | EC/s | 电网净流动差额速率（正数蓄电，负数放电） |
| `{DBS:DEPLETION_SEC}` | `TimeToDepletionSeconds`| `double` | s | 亏电状态下电池存量见底预计剩余秒数 |
| `{DBS:DEPLETION_TEXT}`| `TimeToDepletion` | `string` | 文本 | 电池见底倒计时文本（如 `12m 34s` 或 `INFINITE`） |
| `{DBS:ISDEPLETING}` | `IsDepleting` | `bool` | - | 当前全舰是否处于放电亏电状态 |

---

### 4.13 SystemHeat 复杂热力回路与温控探针
- **源码文件**：[`SystemHeatProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/SystemHeatProbe.cs)
- **探针标识**：`SH`
- **状态判定**：`SystemHeatProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{SH:MAX_TEMP}` / `{SH:LOOP_TEMP}` | `LoopTemp` | `float` | K | 所有热力回路中的最高实际开尔文工作温度 |
| `{SH:NOMINAL_TEMP}` | `NominalTemp` | `float` | K | 当前过热回路所允许的额定设计温度 |
| `{SH:OVERHEAT_PCT}` | `OverheatRatio` | `float` | % | 超出额定温度的过热比例 (0% ~ 100%) |
| `{SH:TOTAL_HEAT}` | `TotalHeatGen` | `float` | kW | 反应堆、发动机与电子元件综合产热通量 |
| `{SH:TOTAL_COOLING}` | `TotalHeatRej` | `float` | kW | 辐射散热片与热交换器最大排热耗散能力 |
| `{SH:NET_FLUX}` | `NetHeatFlux` | `float` | kW | 产热与散热净差额（正数为积热升温，负数为排热降温） |
| `{SH:LOOP_COUNT}` | `LoopCount` | `int` | - | 载具当前构建的独立封闭热力回路总数量 |
| `{SH:ISOVERHEATING}` | `IsOverheating` | `bool` | - | 是否存在回路处于过热警报阈值以上 |

---

### 4.14 AtmosphereAutopilot (AA) 电传飞控探针
- **源码文件**：[`AtmosphereAutopilotProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/AtmosphereAutopilotProbe.cs)
- **探针标识**：`AA`
- **状态判定**：`AtmosphereAutopilotProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{AA:MASTER_SWITCH}` | `MASTER_SWITCH` | `double` | 1/0 | AA 电传自动驾驶总开关状态（1=开启，0=关闭） |
| `{AA:STATUS_TEXT}` / `{AA:MODE}` | `STATUS_TEXT` | `string` | 文本 | 飞控当前工作模式（如 `Cruise Flight`, `Fly-By-Wire`） |
| `{AA:LIFT_ACC}` | `LIFT_ACC` | `double` | m/s² | 气动升力瞬时加速度 |
| `{AA:SLIDE_ACC}` | `SLIDE_ACC` | `double` | m/s² | 气动侧滑瞬时加速度 |
| `{AA:DYN_PRESSURE}` | `DYN_PRESSURE` | `double` | Pa | AA 飞控系统高精度动压解算 |
| `{AA:PITCH_AOA}` / `{AA:AOA}` | `PITCH_AOA` | `double` | ° (deg) | 俯仰迎角 |
| `{AA:SIDESLIP}` | `SIDESLIP` | `double` | ° (deg) | 侧滑角 |
| `{AA:PITCH_RATE}` | `PITCH_RATE` | `double` | °/s | 俯仰角速度 |
| `{AA:ROLL_RATE}` | `ROLL_RATE` | `double` | °/s | 滚转角速度 |
| `{AA:YAW_RATE}` | `YAW_RATE` | `double` | °/s | 偏航角速度 |
| `{AA:MODERATE_AOA}` | `MODERATE_AOA` | `double` | 1/0 | 电传是否处于迎角极限保护介入状态 |
| `{AA:MODERATE_G}` | `MODERATE_G` | `double` | 1/0 | 电传是否处于最大过载 G 保护介入状态 |
| `{AA:MAX_AOA}` | `MAX_AOA` | `double` | ° (deg) | 电传保护允许的最大迎角限制门限 |
| `{AA:MAX_G}` | `MAX_G` | `double` | g | 电传保护允许的最大 G 值限制门限 |

---

### 4.15 RP-1 Avionics 真实航电质量锁探针
- **源码文件**：[`RP1AvionicsProbe.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/RP1AvionicsProbe.cs)
- **探针标识**：`RP1`
- **状态判定**：`RP1AvionicsProbe.IsAvailable`
- **专用数据源速查**：

| 通配符 | 属性 / 方法 | 返回类型 | 单位 | 描述 |
| :--- | :--- | :--- | :--- | :--- |
| `{RP1:LOCK_LEVEL}` | `LOCK_LEVEL` | `double` | 0/1/2 | 航电控制锁定等级（2=正常全控, 1=仅轴向, 0=超重失控） |
| `{RP1:STATUS_TEXT}` | `STATUS_TEXT` | `string` | 文本 | 航电状态文本 (`UNLOCKED`, `AXIAL ONLY`, `LOCKED`) |
| `{RP1:CONTROLLABLE_MASS}` / `{RP1:MAX_MASS}` | `CONTROLLABLE_MASS` | `double` | t (吨) | 载具当前安装的航电所能控制的最大吨位上限 |
| `{RP1:VESSEL_MASS}` | `VESSEL_MASS` | `double` | t (吨) | 载具当前实际总起飞质量 |
| `{RP1:MASS_MARGIN}` | `MASS_MARGIN` | `double` | t (吨) | 吨位控制余量（最大吨位减当前总质量，负数即为失控） |
| `{RP1:MASS_RATIO}` | `MASS_RATIO` | `double` | % | 吨位负荷率（实际质量 / 支持质量，超 100% 即失控） |
| `{RP1:POWER_DRAW}` / `{RP1:WATTS}` | `POWER_DRAW` | `double` | W (瓦) | 全舰所有 RP-1 航电单元总耗电功率 |
| `{RP1:AVIONICS_COUNT}` | `AVIONICS_COUNT` | `double` | 个 | 全舰安装的 RP-1 航电部件总数量 |

---

## 五、解耦仿真数据源 (`TelemetrySimulationEngine`)

在没有加载真实 KSP 飞行场景（如主菜单、纯 Unity 编辑器预览、或无头命令行测试）时，系统自动使用 [`TelemetrySimulationEngine`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/TelemetrySimulationEngine.cs) 输出完全拟真的物理遥测。

仿真引擎预置了 **7 个典型航天飞行阶段**，每隔一定时间或手动切换可连续平滑演变：

1. **`PreLaunch` (发射台待命)**：海拔 74m，地面静止，油门 0%，主母线 28V 满电，发动机熄火。
2. **`AtmosphericAscent` (稠密大气爬升 / Max-Q)**：高度 11,200m，马赫 1.85，迎面动压 $Q = 34.2\text{ kPa}$，TWR 1.85，过载 2.4G。
3. **`StageSeparation` (一二级分级分离)**：高度 68,000m，速度 2,150 m/s，一子级关机，油门瞬间归零，级间段脱落。
4. **`OrbitalInsertion` (二级真空入轨加速)**：高度 145,000m，速度 7,650 m/s，真空比冲点火，近地点迅速抬出地面。
5. **`InOrbitManeuver` (轨道巡航与变轨节点)**：$150\text{ km} \times 150\text{ km}$ 圆轨道，机动节点剩余 $420\text{ m/s}$，太阳能充放平衡。
6. **`AtmosphericReentry` (再入返回与气动黑障)**：高度 42,000m，速度 6,800 m/s，动压高压，强过载减速。
7. **`TouchdownLanding` (动力反推着陆 / 触地)**：雷达真高 15m，垂直速度 $-2.1\text{ m/s}$，低空警报触发，最终平稳接地。

> **Agent 引导**：若需要在无头测试或批处理切片中测试极端边界值，可通过调用 `TelemetrySimulationEngine.Instance.SetPhase(FlightPhase.AtmosphericAscent)` 即时切换输入流。

---

## 六、AI Agent 典型代码范式与调用指南

### 范式 1：在飞行小部件 (`BaseFlightWidget`) 中强类型高效取值
```csharp
public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
{
    if (telemetry == null || !telemetry.HasVessel) return;

    // 1. 直接读取连续浮点数，零 GC 损耗驱动表盘指针
    float currentAlt = (float)telemetry.DisplayAltitude;
    float currentSpeed = (float)telemetry.CurrentSpeed;
    float currentTwr = (float)telemetry.TWR;

    // 2. 根据状态条件流转着色管道
    if (telemetry.IsTouchdownAlert)
    {
        // 触发危险警报色
        SetAlertState(true);
    }
}
```

### 范式 2：使用通配符表达式渲染动态多语言排版文本
```csharp
// 文本支持多段通配符混合语法，自动完成单位缩放
string formattedText = TelemetryTokenEngine.Evaluate(
    "ALT: {ALT:ASL:DIST} | SPD: {SPD:SURF:F1} m/s | Q: {Q:F1} kPa", 
    telemetry
);
_displayText.text = formattedText;
```

### 范式 3：在仪表条/指针中提取连续双精度数值
```csharp
// 提取连续双精度值用于计算角度或尺寸（支持内置通配符或外部探针）
double speedValue = TelemetryTokenEngine.EvaluateNumeric("{SPD:SURF}", telemetry);
if (!double.IsNaN(speedValue))
{
    float angle = Mathf.Lerp(0f, 270f, (float)(speedValue / 1000.0));
    _needleTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
}
```

### 范式 4：安全消费外部可选 Mod 探针（防御性容错设计）
```csharp
// 无论外部 Mod 是否安装，代码均能安全运行
double aoa = double.NaN;
if (ExternalProbeRegistry.NumericResolver != null)
{
    aoa = ExternalProbeRegistry.NumericResolver("FAR:AOA", null);
}

if (double.IsNaN(aoa))
{
    // 回退到原生根据航向与地速估算的简单迎角
    aoa = telemetry.Pitch; 
}
```

---
*文档版本：v2.0 标准版 | 状态：全量探针与词典 100% 同步通过 | 维护中枢：ModularFlightPanel.Core*
