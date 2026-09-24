# Modular Flight Panel (MFP) 外部模组探针遥测 API 查表与速查手册

本文档为 **Modular Flight Panel (MFP)** 针对 KSP 外部 15 大主流 Mod 的遥测探针系统速查手册。配合同目录下的全量 JSON 字典文件 [`PROBE_TELEMETRY_API_CATALOG.json`](file:///c:/Users/43701/Documents/github/KSP_naviball/docs/PROBE_TELEMETRY_API_CATALOG.json)（共包含 736+ 个已遍历和注册的参数），供开发者在编写仪表组件（Widget）时直接查阅，杜绝查阅源码或反编译。

---

## 目录

1. [通配符 Token 语法规则](#1-通配符-token-语法规则)
2. [15 大探针中枢概览](#2-15-大探针中枢概览)
3. [核心探针高频指标速查表](#3-核心探针高频指标速查表)
   * [1. FAR 真实气动 (Ferram Aerospace Research)](#1-far-真实气动-ferram-aerospace-research)
   * [2. KER 航电解算 (Kerbal Engineer Redux)](#2-ker-航电解算-kerbal-engineer-redux)
   * [3. MJ 自动驾驶与飞行状态机 (MechJeb 2)](#3-mj-自动驾驶与飞行状态机-mechjeb-2)
   * [4. PRINCIPIA 非欧 N 体摄动动力学](#4-principia-非欧-n-体摄动动力学)
   * [5. KERBALISM 空间生存与辐射环境](#5-kerbalism-空间生存与辐射环境)
   * [6. RA 拟真深空射频通信 (RealAntennas)](#6-ra-拟真深空射频通信-realantennas)
   * [7. TRAJ 大气再入与落点预测 (Trajectories)](#7-traj-大气再入与落点预测-trajectories)
   * [8. DOCK 空间对接对准引导 (DPAI)](#8-dock-空间对接对准引导-dpai)
   * [9. GPWS 地形感知与起落警告 (GPWS / TAWS)](#9-gpws-地形感知与起落警告-gpws--taws)
   * [10. RF 拟真推进剂与沉底物理 (RealFuels)](#10-rf-拟真推进剂与沉底物理-realfuels)
   * [11. TF 动力可靠性与寿命 (TestFlight)](#11-tf-动力可靠性与寿命-testflight)
   * [12. DBS 电气能量平衡 (Dynamic Battery Storage)](#12-dbs-电气能量平衡-dynamic-battery-storage)
   * [13. SH 闭式废热管理 (SystemHeat)](#13-sh-闭式废热管理-systemheat)
   * [14. AA 电传飞控与巡航 (Atmosphere Autopilot)](#14-aa-电传飞控与巡航-atmosphere-autopilot)
   * [15. RP-1 真实航电吨位与失控 (RP-1 Avionics)](#15-rp-1-真实航电吨位与失控-rp-1-avionics)
4. [全量 JSON 字典说明与离线更新命令](#4-全量-json-字典说明与离线更新命令)

---

## 1. 通配符 Token 语法规则

在 MFP 任意仪表组件的文案（Text）模板或数值（NumericToken）驱动中，使用大括号包裹：

$$\text{\{TAG:PARAM\}} \quad \text{或} \quad \text{\{TAG:PARAM:MODIFIER\}}$$

* **TAG**：探针标识（不区分大小写），如 `FAR`, `KER`, `MJ`, `RA`, `GPWS`, `DBS` 等。
* **PARAM**：参数名或其别名（不区分大小写、自动剥离下划线与连字符），如 `ActiveVesselIAS`、`IAS`、`NetRate`。
* **三维矢量修饰符 (MODIFIER)**：当返回类型为 `Vector3` / `Vector2` 时支持分量提取：
  * `:X` / `:Y` / `:Z`：获取对应坐标轴分量；
  * `:MAG`：获取三维矢量的模长大小（标量）；
  * 示例：`{FAR:ActiveVesselAeroForce:MAG}`，`{TRAJ:ImpactVelocity:Z}`。

---

## 2. 15 大探针中枢概览

| 探针 ID | 探针类名 | 适用 Mod / 领域 | 默认别名 | 参数规模 | 本地 DLL 探测 |
| :--- | :--- | :--- | :--- | :---: | :---: |
| **FAR** | `FarProbe` | Ferram Aerospace Research (气动/跨音速) | `FAR`, `FARC` | 46+ | ✔ 已就绪 |
| **KER** | `KerbalEngineerProbe` | Kerbal Engineer Redux (分级dV/TWR) | `KER`, `ENGINEER` | 15+ | 源码注册 |
| **MJ** | `MechJebProbe` | MechJeb 2 (飞行状态机/轨道机动) | `MJ`, `MECHJEB` | 275+ | ✔ 已就绪 |
| **PRINCIPIA** | `PrincipiaProbe` | Principia (N体摄动/绘图系投影) | `PRINCIPIA`, `PRIN` | 25+ | ✔ 已就绪 |
| **KERBALISM** | `KerbalismProbe` | Kerbalism (生命维持/辐射/通信) | `KERBALISM`, `KLSM` | 129+ | ✔ 已就绪 |
| **RA** | `RealAntennasProbe` | RealAntennas (深空通信射频/信噪比) | `RA`, `REALANTENNAS` | 58+ | ✔ 已就绪 |
| **TRAJ** | `TrajectoriesProbe` | Trajectories (大气再入/落点预测) | `TRAJ`, `TRAJECTORIES` | 7+ | 源码注册 |
| **DOCK** | `DockingAlignmentProbe` | NavyFish DPAI (对接对准仪/CDI) | `DOCK`, `DPAI` | 8+ | 源码注册 |
| **GPWS** | `GPWSProbe` | KSP GPWS (近地告警/决断速度) | `GPWS`, `TAWS` | 8+ | 源码注册 |
| **RF** | `RealFuelsProbe` | RealFuels (推进剂沉底/点火重启) | `RF`, `REALFUELS` | 9+ | ✔ 已就绪 |
| **TF** | `TestFlightProbe` | TestFlight (发动机燃时/故障失效) | `TF`, `TESTFLIGHT` | 54+ | ✔ 已就绪 |
| **DBS** | `DynamicBatteryStorageProbe` | Dynamic Battery Storage (电网平衡) | `DBS`, `DYNAMICBATTERYSTORAGE` | 4+ | 源码注册 |
| **SH** | `SystemHeatProbe` | SystemHeat (回路废热消纳) | `SH`, `SYSTEMHEAT` | 7+ | 源码注册 |
| **AA** | `AtmosphereAutopilotProbe` | Atmosphere Autopilot (电传飞控/过载) | `AA`, `ATMOSPHEREAUTOPILOT` | 85+ | ✔ 已就绪 |
| **RP1** | `RP1AvionicsProbe` | RP-1 (航电控制吨位限制/失控) | `RP1`, `RP0`, `AVIONICS` | 6+ | ✔ 已就绪 |

---

## 3. 核心探针高频指标速查表

### 1. FAR 真实气动 (Ferram Aerospace Research)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{FAR:ActiveVesselIAS}` | `double` | m/s | 真实指示空速 (IAS) | `{FAR:IAS}` |
| `{FAR:ActiveVesselEAS}` | `double` | m/s | 等效空速 (EAS) | `{FAR:EAS}` |
| `{FAR:ActiveVesselMachNumber}` | `double` | Mach | 气动解算即时马赫数 | `{FAR:MACH}` |
| `{FAR:ActiveVesselDynamicPressure}` | `double` | Pa | 即时气动动压 (Q) | `{FAR:Q}`, `{FAR:DYNAMICPRESSURE}` |
| `{FAR:ActiveVesselAngleOfAttack}` | `double` | deg | 实际气动迎角攻角 (AoA) | `{FAR:AOA}` |
| `{FAR:ActiveVesselSideslipAngle}` | `double` | deg | 侧滑角 (Sideslip / Beta) | `{FAR:SIDESLIP}`, `{FAR:BETA}` |
| `{FAR:ActiveVesselStallPercentage}` | `double` | 0..1 | 机翼气动失速比例百分比 | `{FAR:STALL}`, `{FAR:STALLPERCENT}` |
| `{FAR:LiftToDragRatio}` | `double` | ratio | 即时升阻比 (L/D)（滑翔核心指标） | `{FAR:LD}`, `{FAR:LIFTTODRAG}` |
| `{FAR:AtmosphericPressureAtm}` | `double` | atm | 静态环境气压 (atm) | `{FAR:Q_ATM}`, `{FAR:PATM}` |
| `{FAR:AtmosphericTemperatureCelsius}` | `double` | °C | 静态环境摄氏温度 (°C) | `{FAR:TCELSIUS}`, `{FAR:TEMPC}` |
| `{FAR:ActiveVesselAeroForce}` | `Vector3`| kN | 气动三维合力 (支持 :X,:Y,:Z,:MAG) | `{FAR:AEROFORCE}` |

---

### 2. KER 航电解算 (Kerbal Engineer Redux)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{KER:deltaV}` | `double` | m/s | 当前级可用 Delta-V | `{KER:DV}`, `{KER:STAGEDV}` |
| `{KER:totalDeltaV}` | `double` | m/s | 全舰累计剩余总 Delta-V | `{KER:TOTALDV}` |
| `{KER:actualThrustToWeight}` | `double` | ratio | 当前级实际推重比 (TWR) | `{KER:TWR}`, `{KER:STAGETWR}` |
| `{KER:time}` | `double` | s | 当前级全推力剩余可用燃时 | `{KER:BURNTIME}`, `{KER:STAGEBURNTIME}` |
| `{KER:totalTime}` | `double` | s | 全部分级剩余可用总燃时 | `{KER:TOTALBURNTIME}` |
| `{KER:isp}` | `double` | s | 当前级有效等效比冲 (Isp) | `{KER:ISP}` |
| `{KER:SuicideCountdown}` | `double` | s | 自杀式着陆点火倒计时秒数 | `{KER:SUICIDECD}` |
| `{KER:SuicideAltitude}` | `double` | m | 自杀式点火的目标触发真高 | `{KER:SUICIDEALT}` |
| `{KER:SuicideDeltaV}` | `double` | m/s | 自杀式着陆消除地速所需 Delta-V | `{KER:SUICIDEDV}` |
| `{KER:ImpactTime}` | `double` | s | 地表碰撞着陆倒计时 | `{KER:IMPACTTIME}` |

---

### 3. MJ 自动驾驶与飞行状态机 (MechJeb 2)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{MJ:speedSurface}` | `double` | m/s | 相对地表线速度 | `{MJ:SPEEDSURFACE}` |
| `{MJ:speedOrbital}` | `double` | m/s | 轨道绝对速度 | `{MJ:SPEEDORBITAL}` |
| `{MJ:speedVertical}` | `double` | m/s | 垂直升降率 | `{MJ:SPEEDVERTICAL}` |
| `{MJ:machNumber}` | `double` | Mach | 飞行马赫数 | `{MJ:MACHNUMBER}` |
| `{MJ:altitudeASL}` | `double` | m | 平均海平面海拔高度 | `{MJ:ALTITUDEASL}` |
| `{MJ:altitudeTrue}` | `double` | m | 雷达对地真实地表净高度 | `{MJ:ALTITUDETRUE}` |
| `{MJ:twr}` | `double` | ratio | 实时推重比 | `{MJ:TWR}` |
| `{MJ:SurfaceTWR}` | `double` | ratio | 地表基准推重比 | `{MJ:SURFACETWR}` |
| `{MJ:NextManeuverNodeDeltaV}` | `double` | m/s | 下一个机动节点所需 Delta-V | `{MJ:NODEDV}` |
| `{MJ:TimeToManeuverNode}` | `double` | s | 距离机动节点倒计时秒数 | `{MJ:TIMETONODE}` |
| `{MJ:NextManeuverNodeBurnTime}` | `double` | s | 机动节点全推力预计燃烧时间 | `{MJ:NODEBURNTIME}` |
| `{MJ:GetCoordinateString}` | `string` | | 地理经纬度坐标文本 | `{MJ:COORDINATES}` |

---

### 4. PRINCIPIA 非欧 N 体摄动动力学

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{PRINCIPIA:FrameName}` | `string` | | 当前选定绘图参考系全名 | `{PRINCIPIA:FrameName}` |
| `{PRINCIPIA:NavballFrameName}` | `string` | | 导航球所同步的绘图系名称 | `{PRINCIPIA:NavballFrameName}` |
| `{PRINCIPIA:IsSurfaceFrame}` | `bool` | 0/1 | 是否为天体表面固联参考系 | `{PRINCIPIA:IsSurfaceFrame}` |
| `{PRINCIPIA:SpeedInPlottingFrame}` | `double` | m/s | 绘图系下的精确表观合成速度 | `{PRINCIPIA:SpeedInPlottingFrame}` |
| `{PRINCIPIA:VelocityX}` | `double` | m/s | 绘图系 X 轴投影速度 | `{PRINCIPIA:VelocityX}` |
| `{PRINCIPIA:VelocityY}` | `double` | m/s | 绘图系 Y 轴投影速度 | `{PRINCIPIA:VelocityY}` |
| `{PRINCIPIA:VelocityZ}` | `double` | m/s | 绘图系 Z 轴投影速度 | `{PRINCIPIA:VelocityZ}` |
| `{PRINCIPIA:PlottingFrameDistanceToCenter}` | `double` | m | 距离绘图参考系中心的直线距离 | `{PRINCIPIA:PlottingFrameDistanceToCenter}` |
| `{PRINCIPIA:FlightPlanDeltaV}` | `double` | m/s | 下一个机动点计划所需 Delta-V | `{PRINCIPIA:FlightPlanDeltaV}` |
| `{PRINCIPIA:FlightPlanBurnDuration}` | `double` | s | 下一个机动点推力段燃烧时长 | `{PRINCIPIA:FlightPlanBurnDuration}` |
| `{PRINCIPIA:OrbitDescription}` | `string` | | 摄动轨道特性综合描述文本 | `{PRINCIPIA:OrbitDescription}` |

---

### 5. KERBALISM 空间生存与辐射环境

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{KERBALISM:RadiationRate}` | `double` | rad/h | 即时宇宙辐射与辐射带剂量率 | `{KERBALISM:RAD}` |
| `{KERBALISM:HabitatVolume}` | `double` | m³ | 乘员加压活动栖息舱容积 | `{KERBALISM:HAB}` |
| `{KERBALISM:ShieldingRatio}` | `double` | 0..1 | 防辐射装甲防护层覆盖比率 | `{KERBALISM:SHIELD}` |
| `{KERBALISM:LifeSupportDays}` | `double` | days | 维生物资可用天数倒计时 | `{KERBALISM:LSDAYS}` |

---

### 6. RA 拟真深空射频通信 (RealAntennas)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{RA:Rate}` | `double` | b/s | 即时有效下行通信带宽数据速率 | `{RA:RATE}`, `{RA:DATARATE}` |
| `{RA:TxPower}` | `double` | W | 发射天线即时发射射频功率 | `{RA:TXPOWER}`, `{RA:POWER}` |
| `{RA:BestMargin}` | `double` | dB | 最佳通信链路信噪比裕度 | `{RA:MARGIN}`, `{RA:SNRMULT}` |
| `{RA:LinkCount}` | `int` | count | 当前建立的活跃微波通信链路数 | `{RA:LINKCOUNT}` |
| `{RA:BestAntennaName}` | `string` | | 当前主力活动高增益天线名称 | `{RA:ANTENNA}` |
| `{RA:IsConnectedHome}` | `bool` | 0/1 | 是否连通地面测控主站 (KSC) | `{RA:HOME}` |

---

### 7. TRAJ 大气再入与落点预测 (Trajectories)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{TRAJ:ImpactTime}` | `double` | s | 距离地表再入或撞击剩余秒数 | `{TRAJ:IMPACTTIME}` |
| `{TRAJ:ImpactAltitude}` | `double` | m | 预测落点的真实地表海拔高度 | `{TRAJ:IMPACTALT}` |
| `{TRAJ:ImpactVelocity}` | `Vector3`| m/s | 触地瞬间三维预测速度矢量 | `{TRAJ:IMPACTVEL}` |
| `{TRAJ:TargetDistance}` | `double` | m | 距离指定地面着陆靶点的直线误差 | `{TRAJ:TARGETDIST}` |
| `{TRAJ:ImpactLatitude}` | `double` | deg | 预测落点的真实地理纬度 | `{TRAJ:IMPACTLAT}` |
| `{TRAJ:ImpactLongitude}`| `double` | deg | 预测落点的真实地理经度 | `{TRAJ:IMPACTLON}` |
| `{TRAJ:HasTarget}` | `bool` | 0/1 | 是否已选定地面着陆目标点 | `{TRAJ:HASTARGET}` |

---

### 8. DOCK 空间对接对准引导 (DPAI)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{DOCK:DevX}` | `float` | -1..1 | 水平 CDI 偏航平移偏差量 | `{DOCK:DEVX}`, `{DOCK:CDIX}` |
| `{DOCK:DevY}` | `float` | -1..1 | 垂直 CDI 偏航平移偏差量 | `{DOCK:DEVY}`, `{DOCK:CDIY}` |
| `{DOCK:RollOffset}` | `float` | deg | 两端口对齐所需的滚转角度差 | `{DOCK:ROLLOFFSET}`, `{DOCK:ROLL}` |
| `{DOCK:PitchDev}` | `float` | deg | 姿态俯仰角朝向偏差 | `{DOCK:PITCHDEV}` |
| `{DOCK:YawDev}` | `float` | deg | 姿态偏航角朝向偏差 | `{DOCK:YAWDEV}` |
| `{DOCK:Distance}` | `float` | m | 端口相对直线表面净距离 | `{DOCK:DISTANCE}`, `{DOCK:DIST}` |
| `{DOCK:ClosureRate}` | `float` | m/s | 进近闭合速率 (正逼近，负远离) | `{DOCK:CLOSURERATE}`, `{DOCK:CLOSUREV}` |
| `{DOCK:TargetName}` | `string` | | 选定对准的目标端口部件名称 | `{DOCK:TARGETNAME}` |

---

### 9. GPWS 地形感知与起落警告 (GPWS / TAWS)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{GPWS:RadarAltitude}` | `double` | m | 无线电测距雷达真实对地高 (AGL) | `{GPWS:RADARALT}`, `{GPWS:RADAR}` |
| `{GPWS:SinkRate}` | `double` | m/s | 垂直急剧下沉速度 (PULL UP 触发源) | `{GPWS:SINKRATE}` |
| `{GPWS:HorSpeed}` | `double` | m/s | 对地水平实际地速 | `{GPWS:HORSPEED}` |
| `{GPWS:V1Speed}` | `float` | m/s | 起飞决断速度 V1 | `{GPWS:V1}`, `{GPWS:V1SPEED}` |
| `{GPWS:TakeOffSpeed}` | `float` | m/s | 起飞抬前轮速度 Vr | `{GPWS:VR}` |
| `{GPWS:LandingSpeed}` | `float` | m/s | 最终进场接地参考速度 Vref | `{GPWS:VREF}` |
| `{GPWS:StallAoa}` | `float` | deg | 临界气动失速迎角门限 | `{GPWS:STALLAOA}` |
| `{GPWS:GearDown}` | `bool` | 0/1 | 起落架是否已放下锁定 | `{GPWS:GEARDOWN}` |

---

### 10. RF 拟真推进剂与沉底物理 (RealFuels)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{RF:Ignitions}` | `int` | count | 剩余点火重启次数 (-1 代表无限) | `{RF:IGNITIONS}`, `{RF:IGNCOUNT}` |
| `{RF:Ullage}` | `string` | | 推进剂沉底状态 (Stable/Unstable) | `{RF:ULLAGE}`, `{RF:STATUS}` |
| `{RF:UllageStability}` | `double`| 0..1 | 储箱推进剂沉底贴底稳定性百分比 | `{RF:ULLAGESTABILITY}` |
| `{RF:EngineConfig}` | `string` | | 当前运行的发动机硬件型号代号 | `{RF:CONFIG}`, `{RF:ENGINECONFIG}` |

---

### 11. TF 动力可靠性与寿命 (TestFlight)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{TF:OperatingTime}` | `float` | s | 本次点火发动机累计持续工作燃时 | `{TF:OPERATINGTIME}`, `{TF:BURNTIME}` |
| `{TF:FailureRate}` | `double` | prob/s | 当前工况瞬时故障失效概率率值 | `{TF:FAILURERATE}` |
| `{TF:FlightData}` | `float` | DU | 发动机型号飞行验证累积数据点 | `{TF:FLIGHTDATA}`, `{TF:DU}` |
| `{TF:Status}` | `string` | | 发动机健康状态 (NOMINAL 或具体故障) | `{TF:STATUS}`, `{TF:HEALTH}` |
| `{TF:Failed}` | `bool` | 0/1 | 发动机当前是否发生异常故障 | `{TF:FAILED}` |

---

### 12. DBS 电气能量平衡 (Dynamic Battery Storage)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{DBS:NetRate}` | `double` | EC/s | 全舰电网净充放电速率 (正充负放) | `{DBS:NETRATE}`, `{DBS:NET}` |
| `{DBS:TimeToDepletionSeconds}` | `double` | s | 当前放电负荷下电池耗尽秒数 | `{DBS:DEPLETIONSECONDS}` |
| `{DBS:TimeToDepletion}` | `string` | HH:MM:SS| 电池断电倒计时文本 (如 01:23:45) | `{DBS:TIMETODEPLETION}` |
| `{DBS:IsDepleting}` | `bool` | 0/1 | 是否处于持续透支放电状态 | `{DBS:ISDEPLETING}` |

---

### 13. SH 闭式废热管理 (SystemHeat)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{SH:PrimaryLoopTemp}` | `float` | K | 主冷却回路工质实际温度 (K) | `{SH:LOOP_TEMP}`, `{SH:TEMP}` |
| `{SH:PrimaryLoopNominalTemp}` | `float` | K | 主回路标称设计最高工作温度 (K) | `{SH:LOOP_NOM_TEMP}` |
| `{SH:NetFluxKw}` | `float` | kW | 主回路净热通量 (正为升温，负为散热) | `{SH:NET_FLUX}`, `{SH:NETFLUX}` |
| `{SH:TotalGenerationKw}` | `float` | kW | 全舰设备总产生废热功率 (kW) | `{SH:TOTAL_GEN}`, `{SH:HEATGEN}` |
| `{SH:TotalRejectionKw}` | `float` | kW | 全舰活动散热片总散热功率 (kW) | `{SH:TOTAL_REJ}`, `{SH:HEATREJ}` |
| `{SH:PrimaryLoopOverheating}` | `bool` | 0/1 | 主回路是否发生超温报警 | `{SH:OVERHEAT}` |
| `{SH:PrimaryLoopOverheatRatio}` | `float`| 0..1 | 主回路超过标称温度的过热比例 | `{SH:OVERHEAT_RATIO}` |

---

### 14. AA 电传飞控与巡航 (Atmosphere Autopilot)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{AA:MASTER_SWITCH}` | `double` | 0/1 | 电传飞控顶层总开关状态 | `{AA:MASTER}` |
| `{AA:CRUISE_ACTIVE}` | `double` | 0/1 | 巡航自动驾驶仪启用状态 | `{AA:CRUISE}` |
| `{AA:CURRENT_AOA}` | `double` | deg | 传感器解算的即时迎角攻角 | `{AA:AOA}` |
| `{AA:MAX_AOA}` | `double` | deg | 飞控限制器允许的最大迎角门限 | `{AA:MAX_AOA}` |
| `{AA:CURRENT_GLOAD}` | `double` | G | 机体即时承受的法向加速度过载 | `{AA:GLOAD}` |
| `{AA:MAX_GLOAD}` | `double` | G | 飞控过载保护器设定的最大过载限制 | `{AA:MAX_GLOAD}` |

---

### 15. RP-1 真实航电吨位与失控 (RP-1 Avionics)

| Token | 类型 | 单位 | 中文说明 | 常用别名 |
| :--- | :--- | :--- | :--- | :--- |
| `{RP1:LockLevel}` | `int` | 0..2 | 航电控制级别 (2=全控, 1=仅轴向, 0=失控) | `{RP1:LOCKLEVEL}` |
| `{RP1:IsLocked}` | `bool` | 0/1 | 是否因航电不足而完全失去控制 | `{RP1:ISLOCKED}` |
| `{RP1:MaxMass}` | `float` | t | 航电允许控制的最大总质量上限 (吨) | `{RP1:MAXMASS}`, `{RP1:TONNAGE}` |
| `{RP1:VesselMass}` | `float` | t | 飞船当前实际总湿重 (吨) | `{RP1:VESSELMASS}` |
| `{RP1:MassUtilization}` | `float` | 0..1 | 航电吨位利用率 (VesselMass / MaxMass) | `{RP1:UTILIZATION}` |
| `{RP1:Watts}` | `float` | W | 航电系统总持续功率电力消耗 (瓦特) | `{RP1:WATTS}`, `{RP1:POWER}` |

---

## 4. 全量 JSON 字典说明与离线更新命令

* **全量 JSON 文件路径**：
  * 文档目录：[`docs/PROBE_TELEMETRY_API_CATALOG.json`](file:///c:/Users/43701/Documents/github/KSP_naviball/docs/PROBE_TELEMETRY_API_CATALOG.json)
  * 插件数据目录：[`GameData/ModularFlightPanel/PluginData/probe_telemetry_catalog.json`](file:///c:/Users/43701/Documents/github/KSP_naviball/GameData/ModularFlightPanel/PluginData/probe_telemetry_catalog.json)
* **包含字段**：
  * `name`：原始成员或合成属性名称；
  * `token`：即插即用的通配符表达式（如 `{FAR:ActiveVesselIAS}`）；
  * `type`：返回值数据类型（`System.Double`, `System.Boolean`, `UnityEngine.Vector3` 等）；
  * `unit`：工程计量单位（`m/s`, `m`, `kN`, `Pa`, `K`, `deg`, `t`, `s`, `W`, `EC/s` 等）；
  * `accessType`：访问机制（`PublicStaticProperty`, `PublicInstanceField`, `CustomSynthetic` 等）；
  * `sourceType`：外部 Mod 底层类源（如 `MuMech.VesselState`, `FerramAerospaceResearch.FARAPI`）；
  * `descriptionZh` / `descriptionEn`：中英文释义；
  * `aliases`：支持的简写别名列表；
  * `subModifiers`：三维分量修饰符列表（`:X`, `:Y`, `:Z`, `:MAG`）。
* **一键离线刷新/重新导出命令**：
  若未来引入新探针或更新了探针成员，只需在仓库根目录执行以下命令即可在 **2 秒内** 离线全量重刷：
  ```bash
  dotnet run --project tools/HeadlessValidator/HeadlessValidator.csproj -- --export-probe-catalog
  ```
