# Modular Flight Panel (MFP) 全架构全景蓝图 (Architecture Blueprint)

> **Modular Flight Panel (MFP)** 是面向 **坎巴拉太空计划 1 (KSP1 1.12.x)** 与 **Unity 2019.4 LTS** 的新一代高度解耦、全模块化、五中枢协同、100% 数据与文案通配符双驱动的飞行航电仪表系统。

---

## 1. 核心五中枢架构协同全景 (Core Five-Pillars Mesh)

MFP 彻底打破传统插件“单个脚本从头算到尾”的混沌结构，采用**严格的五中枢分离与契约化隔离**体系：

```mermaid
flowchart TD
    subgraph DataSources["一、数据源与物理输入层"]
        KSP["KSP 原生物理/轨道内核<br/>(Vessel, Orbit, FlightGlobals)"]
        KSP_Events["KSP 事件总线<br/>(onVesselModified, onStageActivate, onVesselChange)"]
        ExternalMods["15 大外部模组反射源<br/>(FAR, KER, MJ, Principia, RealFuels, Trajectories 等)"]
        SimEngine["离线无头仿真引擎<br/>(TelemetrySimulationEngine 7 飞行阶段)"]
    end

    subgraph CorePillars["二、核心五中枢架构 (The Five Pillars)"]
        TH["【中枢 1: 遥测与动力学中枢】<br/>TelemetryHub [Order: -500]<br/>• 原生对象完全隔离<br/>• 纯 C# IFlightTelemetry 契约<br/>• 差分电量与资源总量解算"]
        
        TPM["【中枢 2: 外部探针中枢】<br/>ProbeManager & TelemetryProbeManager<br/>• 3秒双重排队冷却<br/>• 异常连续5次自动熔断<br/>• 跨模组 API 动态反射遍历"]
        
        CM["【中枢 3: 统一缓存与零 GC 中枢】<br/>CacheManager (Unified Caching Hub)<br/>• P1: 浮点死区量化池 + 静态字符串常量表<br/>• P2: 载具拓扑与推力事件驱动缓存<br/>• P3: 目标交会对接 15Hz 平滑外推<br/>• P4: 主题色彩连续内存扁平直查<br/>• 探针同帧快照防重快查 (Probe Frame Cache)"]
        
        WRM["【中枢 4: 分级渲染与调度中枢】<br/>WidgetRenderManager<br/>• 4 级刷新阶梯 (60Hz / 30Hz / 10Hz / 2Hz)<br/>• 视口裁剪与离屏 RenderTexture 分辨率管控<br/>• 全自动化 GraphicRaycaster 休眠治理"]
        
        WSM["【中枢 5: 样式与着色管道中枢】<br/>WidgetStyleManager & ThemeManager<br/>• 0 颜色字面量语义角色通道<br/>• 8 款 Shader 材质复用缓存池<br/>• 点阵 / CRT / 全息 / 玻璃实时换肤"]
    end

    subgraph AssemblyAndWidgets["三、装配与组件呈现层"]
        HUD["【HUD 装配总管】FlightHUDManager [Order: +100]<br/>MasterUpdate 单点调度分发 / Sub-Canvas 隔离 / 拖拽把手"]
        
        BFW["【组件通用基类】BaseFlightWidget<br/>FastFormat / SetTextIfChanged / SetImageFillIfChanged"]
        
        subgraph WidgetFamilies["34 个标准化飞行仪表组件族"]
            W_Nav["导航与姿态<br/>(Navball 3D球, PFD 航向弧, ND 罗盘, 轨道参数)"]
            W_Gauges["表盘与带状仪表<br/>(双速带, 高度计, ECAM, 动压动压表)"]
            W_SpaceX["SpaceX 现代座舱 HUD<br/>(龙飞船准星, 星舰瓦片热工, Raptor 网格, 航迹弧)"]
            W_Systems["系统与动力<br/>(电力拓扑图, 维生系统, 分级 ΔV, 通信链路)"]
            W_Controls["控制与交互<br/>(现代顶底工具栏, 分级保险锁, 时间加速器)"]
        end
    end

    subgraph ProfilerMonitor["四、可观测性与性能大盘"]
        PRF["MFPProfiler 硬件级分析仪<br/>• 纳秒级 Stopwatch 采样<br/>• 实时 FPS / 帧预算占比 / Top Offender 组件<br/>• 缓存命中率与 GC 堆内存节约大盘"]
    end

    %% 拓扑连线
    KSP --> TH
    KSP_Events -->|事件驱动脏标记| TH
    ExternalMods --> TPM
    SimEngine -.->|无头离线注入| TH
    
    TH -->|P2 稳态拓扑 / P3 目标运动学| CM
    TPM -->|探针帧快照| CM
    WSM -->|P4 主题调色板重烘焙| CM
    
    TH -->|IFlightTelemetry| HUD
    TPM -->|外部参数注入| HUD
    
    HUD --> WRM
    WRM -->|分级 Tick 分发| BFW
    CM -->|零 GC 字符串与外推几何| BFW
    WSM -->|语义样式与材质| BFW
    
    BFW --> W_Nav & W_Gauges & W_SpaceX & W_Systems & W_Controls
    
    BFW -.->|组件耗时采样| PRF
    CM -.->|命中率与节约量| PRF
    TH -.->|采样耗时| PRF
```

---

## 2. 全生命周期时钟与执行序编排 (Execution Order & Clock Sequence)

为杜绝采样与渲染之间的时序混乱、清零漂移与重复遍历，MFP 建立了严格的帧序锁链：

```mermaid
sequenceDiagram
    autonumber
    participant Unity as Unity Engine
    participant TH as TelemetryHub<br/>[Order: -500]
    participant CM as CacheManager<br/>(统一缓存)
    participant HUD as FlightHUDManager<br/>[Order: +100]
    participant WRM as WidgetRenderManager<br/>(阶梯调度)
    participant W as BaseFlightWidget<br/>(34 个小组件)
    participant PRF as MFPProfiler<br/>(性能采集)

    Note over Unity,PRF: ── 阶段 1: 遥测数据采集与拓扑缓存判定 (Pre-Update) ──
    Unity->>TH: Update() [Order: -500]
    TH->>PRF: BeginSample(ProfilerSection.Telemetry)
    alt 载具事件触发 (拓扑脏) 或 油门变动
        TH->>TH: 重新扫描活跃引擎与推进剂分配
    else 稳态滑行 (油门=0)
        TH->>CM: 快速复用 P2 静态推力与质量缓存 (0 遍历)
    end
    TH->>CM: P3: 15Hz 物理采样 + 帧间平滑一阶外推目标相对几何
    TH->>PRF: EndSample(ProfilerSection.Telemetry)

    Note over Unity,PRF: ── 阶段 2: 主干装配与阶梯刷新分发 (Master Dispatch) ──
    Unity->>HUD: LateUpdate() [Order: +100]
    HUD->>PRF: BeginSample(ProfilerSection.Widgets)
    HUD->>WRM: MasterUpdate(now)
    loop 遍历已注册组件
        WRM->>WRM: 判定刷新阶梯 (Critical / Standard / Relaxed / UltraLow)
        opt 满足时间步长且未被视口裁剪
            WRM->>W: UpdateTelemetry(telem)
            W->>CM: P1: FastDouble / FastFormat (死区量化)
            alt 数值波动在微小容差 (Deadband) 内
                CM-->>W: 直接返回上一帧已格式化 string 引用 (0 内存分配)
            else 真实变化
                CM-->>W: 分配新 string 并更新死区槽位
            end
            W->>W: SetTextIfChanged (指针相同直接跳过 UGUI 顶点重建)
        end
    end
    HUD->>PRF: EndSample(ProfilerSection.Widgets)

    Note over Unity,PRF: ── 阶段 3: 帧尾耗时统计与可观测性归集 (Post-Frame) ──
    HUD->>PRF: EndFrame() (滑动窗口汇总、GC 次数与缓存大盘归集)
```

---

## 3. 统一缓存中枢 (CacheManager) 核心支柱实现拓扑

```mermaid
graph LR
    subgraph P1["P1: 零 GC 死区量化与静态表"]
        direction TB
        T1["静态常驻整数字符串表<br/>FastInt: -1000 ~ 9999 (11000 Slots)"]
        T2["静态常驻百分比表<br/>FastPercent: 0% ~ 100% (101 Slots)"]
        T3["静态常驻度数表<br/>FastDegree: 0° ~ 360° (361 Slots)"]
        T4["浮点死区量化池<br/>FastDouble(key, val, fmt, tolerance)<br/>容差内命中率 > 75%"]
    end

    subgraph P2["P2: 载具拓扑与推力事件缓存"]
        direction TB
        E1["GameEvents.onVesselWasModified"]
        E2["GameEvents.onStageActivate"]
        E3["GameEvents.onVesselChange"]
        E_Logic["稳态滑行 0 油门推力直接置 0<br/>跳过 100% 全船部件与发动机循环"]
        E1 & E2 & E3 --> E_Logic
    end

    subgraph P3["P3: 目标交会对接外推缓存"]
        direction TB
        K1["15Hz 物理严密基准采样"]
        K2["帧间线性平滑一阶外推<br/>(RelativePosition, Velocity, Rotation)"]
        K3["消灭每帧数十次四元数逆变换与矩阵投影"]
        K1 --> K2 --> K3
    end

    subgraph P4["P4: 主题色彩扁平数组直接寻址"]
        direction TB
        C1["ThemeManager.OnThemeChanged"]
        C2["RebakeThemePalette<br/>连续内存数组预烘焙"]
        C3["GetCardBgFast / GetTextFast / GetMeterFast<br/>下标寻址 O(1) 纳秒级直读"]
        C1 --> C2 --> C3
    end

    subgraph ProbeCache["探针帧快照防重缓存"]
        direction TB
        PC1["Time.frameCount 瞬态校验"]
        PC2["跨组件同帧共享外部 Mod 反射结果<br/>(FAR, KER, MechJeb, Trajectories)"]
        PC3["反射查询开销降至 1/N"]
        PC1 --> PC2 --> PC3
    end
```

---

## 4. 小组件渲染流水线与零 GC 过滤机制 (Widget Render Pipeline)

在任何单个 `BaseFlightWidget` 内部，数据从原始遥测到 UGUI 屏幕网格的流转遵循以下**“零损耗四重防线”**：

```mermaid
flowchart TD
    Raw["原始遥测输入 (IFlightTelemetry 双精度/三维矢量)"] --> DeltaCheck{"防线 1: 几何脏检查<br/>Math.Abs(val - _lastValue) > Config.ValueDeltaThreshold"}
    
    DeltaCheck -- 未超阈值 --> Sleep1["[拦截] 保持指针与几何不变 (0 CPU 运算)"]
    
    DeltaCheck -- 超过阈值 --> DeadbandCheck{"防线 2: 浮点死区量化<br/>CacheManager.FastDouble(tolerance)"}
    
    DeadbandCheck -- 容差内波动 --> ReuseRef["[命中] 复用上一帧 string 引用 (0 堆内存 GC 分配)"]
    DeadbandCheck -- 显著变化 --> FormatNew["格式化并更新死区槽位缓存"]
    
    ReuseRef & FormatNew --> TextDirty{"防线 3: 文本脏标记守卫<br/>SetTextIfChanged(textComp, newStr)"}
    
    TextDirty -- 引用或内容相同 --> Sleep2["[拦截] 阻断 UGUI Text.text 写入<br/>(避免 SetVerticesDirty 与 Canvas Rebatching)"]
    TextDirty -- 内容改变 --> ApplyUGUI["写入 UGUI Text 顶点缓冲区"]
    
    subgraph RaycastDefense["防线 4: EventSystem 射线裁剪"]
        Detect["BaseInitialize 深度扫描 Selectable / PointerHandler"] --> IsInteract{"是否具交互性或处于 Alt+N 编辑拖拽？"}
        IsInteract -- 否 (纯读数仪表) --> DisableRaycast["关闭 SubRaycaster<br/>纯视觉元件 raycastTarget = false<br/>(消除鼠标滑过全屏射线遍历)"]
        IsInteract -- 是 (按钮/拖拽) --> EnableRaycast["按需激活 GraphicRaycaster"]
    end
```

---

## 5. 34 个全量航电小组件谱系表 (Widget Family Taxonomy)

全仓库 34 个小组件统一置于 `src/ModularFlightPanel/UI/Widgets/`，完全合规通过 8/8 无头自动化审计：

```
UI/Widgets/
├── Navigation/              # [姿态与轨道导航族]
│   ├── NavballSphereWidget        # 3D 离屏相机渲染姿态球 (节流对齐 + 静止 10Hz 降频)
│   ├── HeadingArcWidget           # PFD 航向指示弧 (Critical 60Hz)
│   ├── SASDialWidget              # 环形 SAS 航向罗盘 (Standard 30Hz)
│   ├── NDNavigationWidget         # 综合导航罗盘 (Standard 30Hz)
│   └── OrbitalInfoWidget          # 轨道根数与交会参数面板 (Relaxed 10Hz)
│
├── Gauges/                  # [表盘与垂直滚带族]
│   ├── TapeGaugeWidget            # 双侧速度 / 气压高度物理滚带 (Standard 30Hz)
│   ├── AvionicsBarGaugeWidget     # 垂直多段式动力学柱状条 (Standard 30Hz)
│   ├── ArcMeterWidget             # 弧形油门与推力百分比表 (Standard 30Hz)
│   └── EcamDialGaugeWidget        # 空客风格 ECAM 圆形指针表盘 (Standard 30Hz)
│
├── SpaceX/                  # [SpaceX 载人龙飞船与星舰航电族]
│   ├── SpaceXDockingReticleWidget # ISS 空间站对接光标 HUD (P3 运动学外推联动)
│   ├── SpaceXStarshipHudWidget    # 星舰全息平显 HUD
│   ├── SpaceXRaptorGridWidget     # 33 发动机环形推力阵列监视器
│   ├── SpaceXTileThermalWidget    # 隔热瓦表面温度热力场图
│   ├── SpaceXTrajectoryArcWidget  # 大气重返与着陆轨迹预测弧
│   ├── SpaceXAttitudeIndicatorWidget # 龙飞船触摸屏人工地平线
│   ├── SpaceXPropellantVerticalWidget # 甲烷/液氧垂直推进剂余量柱
│   ├── SpaceXMotorArcWidget       # SuperDraco 逃逸发动机推力弧
│   ├── SpaceXFlapWidget           # 前后气动翼面偏转角监视器
│   ├── SpaceXGForceWidget         # 载荷三轴过载读数卡
│   ├── SpaceXPfdRollWidget        # 滚转对齐精密微调环
│   ├── SpaceXClockWidget          # 任务发射时钟 (T- / T+)
│   ├── SpaceXDeltaVWidget         # 分级入轨 ΔV 剩余预算卡
│   └── SpaceXOverviewWidget       # 综合全船状态轮廓看板
│
├── Systems/                 # [飞船子系统与工程监视族]
│   ├── ElectricalSystemWidget     # 电力拓扑图 (GetConnectedResourceTotals 优化)
│   ├── LifeSupportWidget          # 氧气/水/气压维生环境监视 (Relaxed 10Hz)
│   ├── Rocket2DWidget             # 2D 分级轮廓剪影与级间状态 (Relaxed 10Hz)
│   ├── SignalStatusWidget         # 深空通信天线指向与天线增益 (Relaxed 10Hz)
│   ├── CommSignalWidget           # 通信数据速率与丢包率监视 (Relaxed 10Hz)
│   ├── EcamStatusWidget           # 报警备忘清单与系统状态卡 (Relaxed 10Hz)
│   └── CustomTokenTextWidget      # 自由通配符文本卡片 (100% 用户自定义)
│
└── Controls/                # [交互控制台与操纵族]
    ├── StageControlWidget         # 分级触发、倒计时与安全防误触锁 (IsInteractive)
    ├── BottomControlsWidget       # SAS / RCS / 模式快速切换底栏 (IsInteractive)
    ├── ModernToolbarWidget        # 悬浮功能呼出工具栏 (IsInteractive)
    └── TimeWarpWidget             # 时间加速倍率步进控制器 (IsInteractive)
```

---

## 6. 无头验证、镜像同步与 CI 闭环链路 (Headless CI & Verification)

```mermaid
flowchart LR
    Dev["开发/重构组件代码<br/>src/ModularFlightPanel/"] --> Build["dotnet build<br/>0 警告 0 错误"]
    
    Build --> SyncMirror["HeadlessValidator --mirror-fix<br/>按 unity_mirror.manifest 逐字节同步镜像"]
    
    SyncMirror --> Gate["test-ui.ps1 -NoAscii<br/>执行全量 8/8 无头门禁"]
    
    subgraph EightGates["8/8 无头自动化门禁"]
        direction TB
        G1["[1/8] layout.json 语法载入"]
        G2["[2/8] LayoutShareHub GZip+Base64 编解码无损往返"]
        G3["[3/8] AABB 视口几何与重叠碰撞检测"]
        G4["[4/8] TelemetryTokenEngine 通配符语法审计"]
        G5["[5/8] 7 阶段 700 Ticks 物理遥测解耦仿真高压测试"]
        G6["[6/8] 34/34 组件架构合法性校验 (0 颜色字面量 / 0 场景查询)"]
        G7["[7/8] 审计内核 Linter 自检"]
        G8["[8/8] Unity 镜像一致性校验 (逐字节对齐)"]
        G1 --> G2 --> G3 --> G4 --> G5 --> G6 --> G7 --> G8
    end
    
    Gate --> EightGates
    EightGates --> Deploy["deploy.ps1<br/>发布到 GameData/ModularFlightPanel"]
    Deploy --> KSP_Run["KSP 运行时实装验证 (极致满帧 / 零 GC 顿挫)"]
```
