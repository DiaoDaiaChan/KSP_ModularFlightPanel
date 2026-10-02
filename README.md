# Modular Flight Panel (模块化飞行面板 / MFP)

> **Modular Flight Panel (MFP 3.0)** 是专为**坎巴拉太空计划 1 (KSP1 1.12.x / Unity 2019.4 LTS)** 打造的全新一代、纯 C# 大脑解耦、六中枢协同、全硬件加速、支持**自由画板搭建、鼠标自由拖拽排版、全座舱量纲统一与外部模组自愈探针**的现代化航电仪表系统。

---

## 目录

1. [项目核心特性与技术亮点](#1-项目核心特性与技术亮点)
2. [核心架构与六大中枢协同](#2-核心架构与六大中枢协同)
3. [自由航电设计工坊 (Freeform Avionics Studio)](#3-自由航电设计工坊-freeform-avionics-studio)
4. [现代航电暗晶工程工作台 3.0 (Alt+N Workbench)](#4-现代航电暗晶工程工作台-30-altn-workbench)
5. [统一物理量纲与通用右键交互](#5-统一物理量纲与通用右键交互)
6. [16 大出厂高精主题预设与 15 款 GPU 着色器](#6-16-大出厂高精主题预设与-15-款-gpu-着色器)
7. [48 款标准化航电组件一览](#7-48-款标准化航电组件一览)
8. [遥测通配符引擎与 15 大模组探针网络](#8-遥测通配符引擎与-15-大模组探针网络)
9. [游戏内操作指南 (Alt+N 交互与社区分享码)](#9-游戏内操作指南-altn-交互与社区分享码)
10. [开发者指南与 4 步交付工作流](#10-开发者指南与-4-步交付工作流)
11. [项目工程目录全貌](#11-项目工程目录全貌)

---

## 1. 项目核心特性与技术亮点

Modular Flight Panel 彻底抛弃了将各个表盘“静态焊死在同一块画布”上的传统做法，构建了真正的**“工业级航电积木生态”**：

- **万物皆组件 (Everything is a Widget)**：中心 3D 姿态球、矩形平显框、SAS 罗盘、龙飞船对接光标、波音 747/787 EICAS，乃至玩家自定义画板，全部继承自统一抽象基类 [`BaseFlightWidget`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/Framework/BaseFlightWidget.cs)。
- **100% 架构现代化与大脑解耦 (WidgetLogic<TState>)**：全仓库 48 个组件类 100% 实现纯 C# 业务大脑解耦，物理计算与 UGUI 渲染彻底分离，数据传递采用 0 GC 纯值结构体快照，支持 100% 离线脱机单测。
- **自由航电设计工坊 (Photoshop 级画板)**：不仅能拖拽现有组件，更支持像 Photoshop/Figma 一样自由创建画板！支持 8 点拉伸形变、独立图层不透明度（0%~100%）、图层旋转与层级管理，自动遍历并复用现有 45+ 款仪表中的任意局部子构件。
- **现代暗晶工程工作台 3.0 (Alt+N Workbench)**：彻底淘汰 Unity 遗留 IMGUI，全量采用 UGUI + GPU 磨砂玻璃着色器（`ModernWorkbenchGlass.shader`）重构。提供 IDE 级 5 大工作视口与极简悬浮药丸 Dock，实现 0 GC、0 掉帧的丝滑交互。
- **3D 姿态球极限性能重构 (<0.05ms)**：单源姿态快照结合扁平 Slot 数组，彻底取代哈希字典与每帧冗余四元数逆变换，帧耗时从 0.187ms 骤降至 `<0.05ms`！
- **全舱量纲归一 (AvionicsUnitSystem)**：覆盖 7 大物理量纲与 4 种全局制式（公制 SI / 英制航空 / 混合航空 / 航海制），支持全座舱制式广播与自适应阶梯换算。
- **自愈外部探针网络**：无缝对接 FAR、KER、MechJeb、RealAntennas 等 15 大模组 736+ 参数，配备 3 秒冷却队列与连续 5 次异常熔断降级机制。

---

## 2. 核心架构与六大中枢协同

MFP 航电系统的核心运转建立在六大相互解耦、权责明确的中枢网格之上：

```
               ┌────────────────────────────────────────────────────────┐
               │          KSP 物理内核 / 15 大外部模组探针网络          │
               └───────────────────────────┬────────────────────────────┘
                                           │
                 ┌─────────────────────────┴─────────────────────────┐
                 ▼                                                   ▼
     【低频数据心跳 OnDataHeartBeat】                       【高频 UI 绘制 OnUIDrawLoop】
     (HeartBeatTier: 10Hz 物理解算)                         (RefreshTier: 60Hz 视觉呈现)
                 │                                                   │
    ① 大脑推演 (LogicCore.Evaluate)                                  │
                 ▼                                                   │
    ┌───────────────────────────┐                                    │
    │    WidgetLogic<TState>    │                                    │
    │  • 纯 C# 业务大脑 (0 Engine)│                                    │
    │  • 100% 离线无头单测覆盖  │                                    │
    └─────────────┬─────────────┘                                    │
                  │                                                  │
    ② 统一换算 (AvionicsUnitSystem)                                   │
                  ▼                                                  │
    ┌───────────────────────────┐                                    │
    │ 7 大量纲 × 4 大全局制式   │                                    │
    │ 零 GC 快速转换与自适应升降│                                    │
    └─────────────┬─────────────┘                                    │
                  │                                                  │
                  ▼                                                  ▼
       0 GC struct 状态快照 ─────────── 内存只读交付 ──────────► ③ 状态消费 (OnRenderState)
                                                                       ▼
                                                            ┌─────────────────────┐
                                                            │ UGUI 视图与语义微控件│
                                                            │ • Cached<T> 防抖脏检│
                                                            │ • SmartUI 零开销链式│
                                                            └─────────────────────┘
                                                                       ▲
                                                                       │ ④ 右键交互路由
                                                            ┌─────────────────────┐
                                                            │WidgetInteractionRtr │
                                                            │ 锁定/重置/切制式/探针│
                                                            └─────────────────────┘
```

1. **动力学与遥测中枢 ([`TelemetryHub.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Telemetry/TelemetryHub.cs))**：早期执行序 (`Order: -500`) 采样，动力学分部解算，推力拓扑事件驱动缓存（滑行 0 开销）；
2. **全局物理量纲中枢 ([`AvionicsUnitSystem.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Avionics/AvionicsUnitSystem.cs))**：7 大量纲 × 4 种全局制式快速换算与阶梯自适应工程格式化；
3. **外部探针协调中枢 ([`TelemetryProbeManager.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Probes/TelemetryProbeManager.cs))**：跨程序集安全反射，736+ 参数映射，带冷却队列与异常熔断断路器；
4. **统一缓存与零 GC 中枢 ([`CacheManager.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Core/Diagnostics/CacheManager.cs))**：P1 静态整数/百分比/度数常量表与浮点死区量化池，P2 载具拓扑缓存，P3 目标对接 15Hz 外推，P4 主题色彩连续内存 $O(1)$ 直读；
5. **双轨时钟与调度中枢 ([`WidgetRenderManager.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/Framework/WidgetRenderManager.cs))**：驱动 10Hz 物理解算与 60Hz 画面刷新，具备视口裁剪与 GraphicRaycaster 动态休眠；
6. **样式与着色管道中枢 ([`WidgetStyleManager.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/Framework/WidgetStyleManager.cs))**：SPEC-006 零颜色字面量红线，管理 16 款主题与 15 款 GPU 着色器复用池。

---

## 3. 自由航电设计工坊 (Freeform Avionics Studio)

MFP 3.0 首创了对标专业图形软件（Photoshop / Figma）的自由航电设计工坊：

- **自由画板小组件 ([`CustomCompositePanelWidget.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/Widgets/Gauges/CustomCompositePanelWidget.cs))**：
  - 支持 **8 点变换手柄 (Gizmo)** 非等比自由拖拉伸缩；
  - 每个子图层支持 **独立不透明度 (0% ~ 100%)** 调节与旋转；
  - 支持类似 PS 图层面板的 **Z-Index 层级升降、图层锁定、显隐切换与重命名**。
- **全量构件自动提取 ([`WidgetControlCatalog.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/Framework/WidgetControlCatalog.cs))**：
  - 自动反射扫描全仓库 45+ 款已有组件中的微控件；
  - 提供**按来源组件溯源**（例如直接提取波音 747 的襟翼指示条、SpaceX 的对接误差标尺）与**按功能类型分类**（读数盒、进度条、告警器、切换按键）双重视图；
  - 选中任何构件即可一键“盖印”到当前画板上，随意摆放与修改绑定数据！
- **完整持久化与分享 ([`CompositePanelConfig.cs`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/Config/CompositePanelConfig.cs))**：
  - 所有自由图层位置、尺寸、旋转、Token、动作与风格完整序列化至 JSON，支持随全局布局一键导出分享码。

---

## 4. 现代航电暗晶工程工作台 3.0 (Alt+N Workbench)

在飞行中按下 `Alt + N`，即可唤出全新全 UGUI 硬件加速工作台：

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [🚀 MFP 航电工作台 3.0]  [载具: Kerbal-X] [参考系: ORBIT] [FPS: 60.0]  [💾 保存并关闭] │
├──────┬───────────────────────────────────────────────────────────────────────┤
│ 🛠️  │  [工作视口: 航电设计工坊 / 视觉风格 / 档案预设 / 诊断沙盒 / 系统偏好]     │
│ 设计 │                                                                       │
├──────┤  • 8 点形变 Gizmo 自由排版与磁吸对齐                                  │
│ 🎨  │  • 16 款主题与 15 款 GPU 着色器实时预览                                │
│ 主题 │  • 载具专属预设管理与 GZip+Base64 社区分享码                          │
├──────┤  • 微秒级性能大盘采样与 7 阶段动力学注水仿真                          │
│ 💾  │                                                                       │
│ 预设 │                                                                       │
├──────┤                                                                       │
│ 🚀  │                                                                       │
│ 诊断 │                                                                       │
├──────┤                                                                       │
│ ⚙️   │                                                                       │
│ 偏好 │                                                                       │
├──────┴───────────────────────────────────────────────────────────────────────┤
│ [极简悬浮药丸 Dock]  ▶ [开启自由排版]  🎯 [重置布局]  📐 [网格吸附: 开]  [退出排版]   │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **5 大 IDE 级工作视口**：
  1. **🛠️ 航电设计工坊 (Studio)**：自由画板搭建、构件库提取、图层列表管理、活动组件大纲树；
  2. **🎨 视觉风格与主题 (Themes)**：16 款出厂高精主题无缝热切换、15 款 Shader 选用、语义调色板调节、平滑/点阵字体切换；
  3. **💾 档案与预设中枢 (Profiles)**：载具专属布局自动绑定、出厂预设一键恢复、GZip+Base64 社区分享码往返无损导入导出；
  4. **🚀 诊断与遥测沙盒 (Diagnostics)**：纳秒级性能大盘采样、Top Offender 分析、7 飞行阶段 700 Ticks 离线注水仿真测试；
  5. **⚙️ 系统偏好与按键 (Preferences)**：全局比例缩放、DPI 适配、节电模式、按键重映射、13 种语言多语言切换。
- **解耦画布排版模式 (Canvas Layout Mode)**：
  - 点击“开启自由排版”，工作台自动缩拢为底部**极简悬浮药丸 Dock**，彻底消除主窗口遮挡，让您边拖动边观察全屏仪表；
  - 鼠标直接拖拽任意小组件，支持 10px 网格磁吸与动态智能对齐辅助线（Smart Guides）。

---

## 5. 统一物理量纲与通用右键交互

### 5.1 全局物理量纲系统 (`AvionicsUnitSystem`)
彻底杜绝各个组件私有计算换算公式，由全局中枢统一纳管：
- **7 大物理量纲**：速度 (Speed)、高度 (Altitude)、垂直速度 (VerticalSpeed)、大气动压 (DynamicPressure)、质量 (Mass)、温度 (Temperature)、距离 (Distance)；
- **4 种全局制式**：
  - **公制 SI (Metric SI)**：m/s, km, m/s, kPa, t, °C
  - **英制航空 (Aviation Imperial)**：knots, ft, ft/min, psi, klb, °F
  - **混合航空 (Aviation Hybrid)**：Mach/knots, ft, ft/min, kPa, t, °C
  - **航海制 (Nautical)**：knots, nm, ft/min, hPa, t, °C
- **自适应工程格式化**：调用 `FormatAdaptive` 自动按阶梯升降单位（如 800m 显式为 `800m`，24000m 自动升阶为 `24.0km`）。

### 5.2 全组件通用右键上下文菜单 (`WidgetInteractionRouter`)
所有组件由基类自动赋予右键交互能力。在任意组件上点击鼠标右键，即可呼出专属上下文菜单：
- 🔒 **锁定/解锁位置**：锁定后穿透鼠标点击，防止激烈操纵中误触拖拽；
- ↺ **复位形变与坐标**：一键清除缩放、旋转或位移，重置为出厂标准；
- 🌓 **不透明度调节**：快速调整当前卡片在座舱中的半透明暗晶通透感；
- 🌐 **切换量纲制式**：支持脱离全局设置，为单表指定专属物理制式；
- 🔍 **呼出遥测探针**：一键查看该表盘底层读取的全部原生与外部模组原始读数；
- 🛠️ **专属动作声明**：组件可通过 `RegisterAction` 声明专属操作（如重置三轴配平、切换显示模式）。

---

## 6. 16 大出厂高精主题预设与 15 款 GPU 着色器

### 16 款精心调校的官方出厂主题：
1. **波音 787 梦想客机 (`CreateBoeing787`)**：现代商用客机冰蓝高对比玻璃座舱风格；
2. **SpaceX 载人龙飞船 (`CreateSpaceXDragon`)**：极简黑白暗色调、纯净灰白文字与高亮黄色状态标；
3. **SpaceX 星舰火星开拓者 (`CreateStarshipMars`)**：火星炽红与碳纤维暗晶质感；
4. **现代航空玻璃座舱 (`CreateModernAero`)**：经典航电纯白/天蓝现代 PFD 调色板；
5. **经典航空原版复刻 (`CreateClassicAero`)**：经典蓝天棕地与暖黄高反差复古航电；
6. **阿波罗 1969 登月舱 (`CreateApollo1969`)**：阿波罗 AGC 绿色荧光管与高亮发光体质感；
7. **阿波罗 DSKY 导航计算机 (`CreateApolloDsky`)**：经典数字按键与琥珀/荧光多段管；
8. **战斗机衍射绿光平显 (`CreateDiffractiveHud`)**：F-16 / F-35 衍射全息平显 HUD 翠绿质感；
9. **复古琥珀单色屏 (`CreateVintageAmber`)**：70 年代单色示波器暖琥珀阴极荧光屏；
10. **赛博点阵 LED 示波器 (`CreateCyberMatrix`)**：高对比物理点阵与高反差向量图形；
11. **赛博霓虹脉冲 (`CreateCyberNeon`)**：高饱和亮青、洋红与亮绿高反差未来感；
12. **旅行者深空探针 (`CreateDeepSpaceVoyager`)**：深邃幽蓝与极微弱微光；
13. **SR-71 黑鸟高空侦察机 (`CreateSr71Blackbird`)**：暗夜红光与高空隐身仪表；
14. **东方一号加加林 (`CreateVostok1961`)**：人类首次载人航天粗犷工业质感；
15. **黑客帝国矩阵流 (`CreateMatrixHacker`)**：深黑背景与数字雨荧光绿代码风；
16. **EVA 初号机战术界面 (`CreateEvaUnit01`)**：经典紫绿机体配色与暴走警示风格。

### 15 款专有 GPU 硬件加速着色器：
- `ModernWorkbenchGlass.shader`：现代工程工作台磨砂毛玻璃、色散光晕与边缘流光；
- `AvionicsProceduralUI.shader`：纯 GPU 程序化矢量圆角、细线与边框直出；
- `CrispAvionicsText.shader`：超视网膜级抗锯齿高对比度矢量字体着色器；
- `DigitalSegmentUI.shader`：数码液晶荧光管 7 段数码管着色器；
- `DotMatrixUI.shader`：物理点阵与 LED 阵列发光着色器；
- `GlassCockpitUI.shader`：现代座舱微光、边缘发热条与漫反射；
- `NeonGlowUI.shader`：赛博切角硬核线框与 CRT 扫描线；
- `PhosphorHoloUI.shader`：全息平显绿/琥珀色磷光与衍射扫描线；
- `RadialSegmentedMeter.shader`：多段式圆弧刻度标尺与动态范围条；
- `MinimalistAttitudeSphere.shader`：极简扁平态势球着色器；
- `NavballModern.shader`：现代天顶/地平线梯度与菲涅尔微光姿态球；
- `NavballHalftone.shader`：屏幕空间半色调 Dither 渐变姿态球；
- `NavballRaymarch.shader`：纯数学光线投射 3D 姿态球着色器；
- `NavballEnhanced.shader`：高保真原版贴图解析姿态球着色器；
- `Vessel3DTechnical.shader`：载具 3D 线框技术透视着色器。

---

## 7. 48 款标准化航电组件一览

仓库内 48 个航电组件类现已 **100% 达成架构现代化**（全部挂载纯 C# 解耦大脑，0 违规，0 遗留旧版）：

### 姿态导航族 (Navigation - 11 款)
- `core.navball` (`NavballSphereWidget`): 3D 物理姿态球，离屏渲染，`<0.05ms` 极速 Slot 渲染架构；
- `nav.rectangular_navball` (`RectangularNavballWidget`): 现代矩形平显 3D 姿态框、航向带与俯仰梯投影；
- `nav.vessel_navball` (`VesselAttitudeSphereWidget`): 载具姿态指示球与三维空间矢量投影；
- `core.heading_arc` (`HeadingArcWidget`): 现代平显航向指示圆弧 (PFD 核心)；
- `custom.nd_navigation` (`NDNavigationWidget`): 综合水平态势导航罗盘、航点与交会对接标识；
- `core.sas_dial` (`SASDialWidget`): 环形 SAS 罗盘、程序化刻度与多态指向环；
- `nav.orbital_elements` (`OrbitalElementsWidget`): 轨道六根数精准解析、半长轴与离心率卡片；
- `nav.reference_frame` (`ReferenceFrameWidget`): 地表/轨道/目标参考系切换与天体标识徽标；
- `core.maneuver` (`ManeuverNodeWidget`): 变轨机动节点矢量分量与点火时长读数盒；
- `core.maneuver_v2` (`ManeuverNodeWidgetv2`): 极简现代化机动节点卡片 (纯微控件 DSL)；
- `custom.maneuver_timeline` (`ManeuverTimelineWidget`): 机动时序时间线与节点倒计时进度标尺。

### 表盘滚带族 (Gauges - 6 款)
- `tape.speed` / `tape.alt` (`TapeGaugeWidget`): 双侧速度/气压高度物理滚带表 (带趋势矢量)；
- `custom.arc_speed_tape` (`ArcTapeWidget`): 弯曲弧形滚带表盘 (高端拟物)；
- `gauge.throttle` (`AvionicsBarGaugeWidget`): 垂直多段式动力学柱状条；
- `core.throttle` / `vsi` (`ArcMeterWidget`): 120°/180°/270° 圆弧刻度推力与垂直速度表；
- `custom.*` (`CustomTokenTextWidget`): 自由通配符文本卡片 (支持多槽位与自定义格式)；
- `custom.composite_panel` (`CustomCompositePanelWidget`): **自由航电搭建画板** (PS 级图层/8点形变/任意构件拼装)。

### SpaceX 航电族 (SpaceX - 8 款)
- `spacex.docking` (`SpaceXDockingReticleWidget`): 龙飞船 ISS 对接光标 HUD (运动学平滑外推)；
- `spacex.attitude` (`SpaceXAttitudeWidget`): 极简数字姿态三轴读数盒 (GPU 程序化图元)；
- `spacex.overview` (`SpaceXOverviewWidget`): 综合全船状态轮廓看板与多系统健康摘要；
- `spacex.header` (`SpaceXHeaderWidget`): 顶部任务时钟 (T- / T+) 与天体运行状态条；
- `spacex.bottom` (`SpaceXBottomBarWidget`): 触控式航电功能控制底栏；
- `spacex.timeline` (`SpaceXTimelineWidget`): 发射时序里程碑与任务推进阶段指示器；
- `spacex.engines` (`SpaceXEngineWidget`): 发动机集群状态矩阵与室压监视；
- `spacex.arc` (`SpaceXArcGaugeWidget`): 龙飞船专属圆弧平滑表盘。

### 系统监视族 (Systems - 14 款)
- `custom.electrical` (`ElectricalSystemWidget`): 电力拓扑图 (差分电量与资源网络总计)；
- `custom.life_support` (`LifeSupportWidget`): 氧气/水/气压维生监视 (支持 Kerbalism / TAC-LS)；
- `custom.rocket` (`Rocket2DWidget`): 2D 分级轮廓剪影与级间状态指示；
- `custom.signal` (`SignalStatusWidget`): 深空通信天线指向与增益列表；
- `core.comm_signal` (`CommSignalWidget`): 通信速率、信号强度与丢包率监视；
- `core.comm_signal_v2` (`CommSignalWidgetv2`): 现代柱状图通信信号监视卡；
- `core.ecam_alert_log` (`EcamAlertLogWidget`): 空客风格 ECAM 历史告警回溯日志记录器；
- `core.master_warning` (`MasterWarningWidget`): 航空级主警告与主注意灯闪烁器；
- `core.time_comm_hub` (`TimeCommHubWidget`): 时间与通信一体化微型集成仪表；
- `custom.b747_eicas` (`B747EicasWidget`): 波音 747 主发动机参数指示 EICAS；
- `custom.b747_lower_eicas` (`B747LowerEicasWidget`): 波音 747 辅助动力与控制面状态 EICAS；
- `custom.b787_eicas` (`B787EicasWidget`): 波音 787 综合航电发动机监视卡片；
- `core.performance_monitor` (`PerformanceMonitorWidget`): 游戏实时帧率 (FPS) 与系统性能开销监视卡；
- `core.stage_dv` (`StageDeltaVWidget`): 实时分级 $\Delta V$ 列表与分级控制条目。

### 操纵控制族 (Controls - 8 款)
- `core.stage_control` (`StageControlWidget`): 分级触发、倒计时与安全防误触锁；
- `core.staging_sequence` (`StagingSequenceWidget`): 多级分离时序控制与级间延时监视器；
- `core.bottom_controls` (`BottomControlsWidget`): SAS / RCS / 参考系 / 齿轮 / 刹车快速切换底栏；
- `core.toolbar` (`ModernToolbarWidget`): 悬浮功能呼出工具栏 (带折叠与图标收纳)；
- `core.dock_favorites` (`FavoriteToolbarWidget`): 玩家收藏小组件快速泊靠停靠坞；
- `core.time_warp` (`TimeWarpWidget`): 时间加速倍率步进控制器与物理加急开关；
- `core.ui_widget` (`UIWidget`): 通用 UI 容器与背景底板占位控件；
- `BaseNavballSphereWidget` & `StandardFlightWidgetTemplate`: 框架基础设施与黄金模板。

---

## 8. 遥测通配符引擎与 15 大模组探针网络

### 8.1 遥测通配符引擎 (`TelemetryTokenEngine.cs`)
支持对任意字符串模板进行高性能正则替换计算，支持的典型通配符包括：
- **速度参量**：`{SPD}` (当前模式)、`{SPD:SURF}` (地面速)、`{SPD:OBT}` (轨道速)、`{SPD:TGT}` (相对目标速)、`{MACH}` (马赫数)
- **高度参量**：`{ALT:AGL}` (真高)、`{ALT:ASL}` (海拔)、`{ALT:AGL:DIST}` (智能距离后缀，自动显示 km / m)
- **姿态与动力**：`{HDG}` (真北航向 000°~360°)、`{PITCH}` (俯仰)、`{ROLL}` (滚转)、`{VSI}` (垂直速度)
- **力学与推进**：`{TWR}` (实时推重比)、`{GFORCE}` (重力过载 G)、`{Q}` (大气动压 kPa)、`{THROTTLE}` (油门百分比)、`{PROP}` (推进剂余量)
- **轨道参量**：`{AP}` (远地点)、`{PE}` (近地点)、`{TAP}` (远地点倒计时 T-00:00:00)、`{TPE}` (近地点倒计时)
- **飞控状态**：`{SAS}` (当前锁定朝向)、`{RCS}` (开启状态)、`{BODY}` (天体名)、`{SITUATION}` (飞行状态)

### 8.2 15 大外部模组探针网络
探针协调中枢纳管以下模组的动态参数，所有探针未安装或异常时自动回落为安全占位符：
1. **FAR (Ferram Aerospace Research)**: 气动升阻比、动压、音速比、气流攻角、失速告警
2. **KER (Kerbal Engineer Redux)**: 级间精细 $\Delta V$、自适应推重比、燃烧倒计时
3. **MechJeb**: 自动驾驶仪状态、目标交会脱靶量、预计轨道切点
4. **Principia**: $N$ 体数值轨道摄动、非两体积分轨道参量
5. **RealFuels / ModularFuelTanks**: 真实推进剂密度、油箱增压状态、低温沸腾蒸发率
6. **Kerbalism**: 宇宙辐射剂量、微气压泄漏、磁层防护、生命保障余量
7. **RealAntennas**: 射频增益、载波信噪比、天线指向对准角
8. **Trajectories**: 气动减速着陆落点精确坐标、再入大气受热峰值
9. **SystemHeat**: 反应堆冷却回路温度、热通量与散热器负荷
10. **DBSI (Dynamic Battery Storage)**: 发电机与用电设备实时充放电差分功率
11. **RP-1 / RealismOverhaul**: 早期航电可用性、载具质量级数、发动机点火次数余量
12. **GPWS**: 近地警告系统机体下沉率过快警报、拉起警告
13. **AtmosphereAutopilot**: 电传操纵飞控攻角限制与偏航阻尼
14. **DockingPortAlignmentIndicator**: 对接口六自由度对准角度与横向偏移
15. **TestFlight / TestLite**: 发动机故障率、平均无故障点火时间

---

## 9. 游戏内操作指南 (Alt+N 交互与社区分享码)

1. **自动挂载与运行**：
   - 进入飞行场景（Flight Scene），仪表系统自动挂载，优雅替代原版界面。
2. **呼出/关闭工作台 (`Alt + N`)**：
   - 键盘按下 `Alt + N`，瞬间呼出基于 GPU 磨砂玻璃渲染的现代暗晶工作台；
   - 再次按下 `Alt + N` 或按 `ESC` 键，工作台平滑关闭并自动保存布局。
3. **自由拖拽与排版模式**：
   - 在工作台底部点击绿色药丸按钮 `▶ [开启自由排版]`，主窗口自动折叠为底部悬浮药丸 Dock；
   - 此时屏幕上所有小组件均浮现手柄，直接用鼠标拖动摆放到任意顺手的位置；
   - 松手时自动执行 **10px 网格对齐** 与 **智能参考线磁吸**。
4. **自由画板搭建 (Studio)**：
   - 在工作台左侧点击 `🛠️ 设计工坊`，选择 `+ 创建新画板`；
   - 从构件库中任意挑选所需的微控件（如速度读数、油门条、SAS 指示），拖拽到画板上；
   - 选中图层后，可自由拉伸 8 点手柄、调节不透明度与旋转角度。
5. **右键上下文菜单**：
   - 在任何小组件上点击鼠标右键，即可锁定拖拽、复位形变、调节透明度、切换单表量纲制式或呼出遥测探针。
6. **社区预设分享码**：
   - 在 `💾 档案预设` 中点击“导出分享码”，即可获得一段紧凑的 `MFP:v1:...` 文本代码；
   - 粘贴好友分享的代号，点击“导入”，瞬间无损还原全套座舱航电排版与自由画板配置！

---

## 10. 开发者指南与 4 步交付工作流

开发任何新的航电小组件，只需遵循标准化契约：

### 黄金模板代码范式：
```csharp
using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    // 1. [SPEC-010] 纯值状态快照 (0 GC struct，严禁堆分配与引用类型)
    public struct NavSensorState
    {
        public bool HasVessel;
        public double ConvertedSpeed;
        public float FillRatio;
        public string FormattedSpeed;
        public string UnitLabel;
        public CardStyleRole CardRole;
    }

    // 2. [SPEC-004C / SPEC-012] 纯 C# 业务解耦大脑 (脱离 UnityEngine，只算物理，100% 离线单测)
    public class NavSensorLogic : WidgetLogic<NavSensorState>
    {
        public override void Reset() => CurrentState = default;

        public override void Evaluate(IFlightTelemetry telemetry, float dt)
        {
            if (telemetry == null || !telemetry.HasVessel) { Reset(); return; }

            double spd = telemetry.SurfaceSpeed;
            // 全舱量纲中枢换算 (7 大量纲 × 4 种全局制式)
            double converted = AvionicsUnitSystem.Convert(
                spd, UnitDimension.Speed, AvionicsUnitSystem.CurrentMode, out string symbol);

            CurrentState = new NavSensorState
            {
                HasVessel = true,
                ConvertedSpeed = converted,
                FillRatio = (float)Math.Max(0.0, Math.Min(1.0, spd / 500.0)),
                FormattedSpeed = AvionicsUnitSystem.FormatAdaptive(spd, UnitDimension.Speed, AvionicsUnitSystem.CurrentMode),
                UnitLabel = symbol,
                CardRole = spd > 400.0 ? CardStyleRole.Danger : (spd > 300.0 ? CardStyleRole.Warning : CardStyleRole.Normal)
            };
        }
    }

    // 3. 视图组件 (挂载大脑，消费状态并刷渲染)
    [FlightWidget("nav_sensor", Category = WidgetCategory.Gauges,
        DisplayName = "航速传感器", Description = "第二代航电标准卡片：WidgetLogic 业务解耦与全舱量纲联动。")]
    public class NavSensorWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(160f, 50f);
        protected override bool AutoCreateCardFrame => true;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard; // 60Hz 渲染
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed; // 10Hz 物理

        private readonly NavSensorLogic _logic = new NavSensorLogic();
        protected override IWidgetLogic LogicCore => _logic;

        public TextWidget Title = TextWidget.Title("SPEED");
        public TextWidget Value = TextWidget.Value("{SPD}", "0.0");
        public TextWidget Unit  = TextWidget.Unit("m/s");
        public LinearBarWidget Bar = LinearBarWidget.BottomBar(MeterStyleRole.Primary);

        private readonly Cached<string> _lastValStr = new Cached<string>(string.Empty);
        private readonly Cached<string> _lastUnitStr = new Cached<string>(string.Empty);

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            if (!state.HasVessel) return;

            Bar.FillAmount = state.FillRatio;
            if (_lastValStr.Update(state.FormattedSpeed)) { Value.TextComponent.SetTextSafe(state.FormattedSpeed); }
            if (_lastUnitStr.Update(state.UnitLabel)) { Unit.TextComponent.SetTextSafe(state.UnitLabel); }
        }
    }
}
```

### 4 步无头质量门禁交付：
```powershell
# 1. 编译 C# 程序集
dotnet build src/ModularFlightPanel/ModularFlightPanel.csproj -c Release

# 2. 逐字节同步 Unity 无头镜像
dotnet run --project tools/HeadlessValidator/HeadlessValidator.csproj -- --mirror-fix

# 3. 运行 10/10 自动化门禁测试套件
dotnet run --project tools/HeadlessValidator/HeadlessValidator.csproj

# 4. 执行 Unity Batchmode 离线像素级切片验收
pwsh ./test-ui.ps1 -Render -Widget "custom.composite_panel" -Screen "dark"
# 切片产物位于: GameData/ModularFlightPanel/PluginData/isolated_custom_composite_panel.png
```

---

## 11. 项目工程目录全貌

```
KSP_naviball/
│
├── README.md                           <- 本技术规范与用户指南
├── ARCHITECTURE.md                     <- 航电架构全景蓝图 (v3.0)
├── .gitignore
├── deploy.ps1                          <- NTFS 原子更名热部署脚本
├── test-ui.ps1                         <- Unity Batchmode 离线渲染与门禁驱动脚本
│
├── src/                                <- C# 源码工程
│   ├── ModularFlightPanel.sln          <- 解决方案
│   └── ModularFlightPanel/
│       ├── ModularFlightPanel.csproj
│       ├── Core/                       <- 核心中枢
│       │   ├── Avionics/               <- AvionicsUnitSystem 全局量纲中枢 / CompositePanelLogic
│       │   ├── Contracts/              <- IFlightTelemetry / IWidgetLogic / INavBallVisualHook
│       │   ├── Diagnostics/            <- CacheManager 缓存中枢 / MFPProfiler / MFPLogger
│       │   ├── Hooks/                  <- StockNavBallHook 单源姿态快照 / HarmonyPatches
│       │   ├── Probes/                 <- TelemetryProbeManager 15 大模组探针网络
│       │   ├── Rendering/              <- AssetLoader / 离屏烘焙器 / 矢量图集生成器
│       │   └── Telemetry/              <- TelemetryHub 动力学中枢 / TelemetryTokenEngine
│       ├── Config/                     <- 配置与序列化模型
│       │   ├── CompositePanelConfig.cs <- 自由画板图层数据模型
│       │   ├── ThemeConfig.cs          <- 16 款主题定义与色彩模型
│       │   ├── WidgetConfig.cs         <- 布局与坐标模型
│       │   └── LayoutShareHub.cs       <- GZip+Base64 社区分享码编解码器
│       └── UI/                         <- 视觉呈现层
│           ├── Auditing/               <- SPEC-001..012 Roslyn AST 静态门禁扫描器
│           ├── EditMode/               <- 拖拽句柄 / 网格吸附 / 辅助线磁吸 / 变换 Gizmo
│           ├── Framework/              <- BaseFlightWidget 基类 / WidgetControlCatalog 构件中枢 / WidgetInteractionRouter 右键交互
│           ├── HUD/                    <- FlightHUDManager 画布总管 / HUDEditModeToolbar
│           ├── Settings/               <- 偏好设置 / 遥测参数目录
│           ├── Workbench/              <- 现代暗晶工程工作台 3.0 (WorkbenchCanvasView + 5 大视口)
│           └── Widgets/                <- 48 个标准化航电组件
│               ├── Controls/           <- 操纵控制族 (8 款)
│               ├── Gauges/             <- 表盘滚带族 (6 款，含自由画板 CustomCompositePanelWidget)
│               ├── Navigation/         <- 姿态导航族 (11 款，含 3D 姿态球 NavballSphereWidget)
│               ├── SpaceX/             <- SpaceX 航电族 (8 款)
│               └── Systems/            <- 系统监视族 (14 款，含 B747/B787 EICAS)
│
├── tools/                              <- 质量门禁工具链
│   └── HeadlessValidator/              <- 10/10 自动化门禁验证器 (Roslyn AST / 离线注水仿真)
│
├── unity/                              <- Unity 2019.4.18f1 资产工程
│   └── Assets/
│       ├── Shaders/                    <- 15 款专属 GPU 硬件加速 Shader
│       └── Editor/                     <- HeadlessUIRenderer 离线切片渲染器
│
└── GameData/                           <- KSP 发行产物
    └── ModularFlightPanel/
        ├── Plugins/ModularFlightPanel.dll
        ├── AssetBundles/modularflightpanel.ksp
        ├── Localization/               <- 13 种语言多语言本地化包
        └── PluginData/
            ├── Presets/*.json          <- 7 大官方出厂航电预设
            └── layout.json             <- 玩家运行时布局与自定义画板
```

---

## 许可证 (License)

本项目采用 [MIT 许可证](LICENSE) 分发与开源。
