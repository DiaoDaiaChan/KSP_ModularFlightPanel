# Modular Flight Panel (模块化飞行面板 / MFP)
## 坎巴拉太空计划 1 (KSP1) 现代化飞行仪表系统架构与设计规范

> **Modular Flight Panel (MFP)** 是专为坎巴拉太空计划 1（KSP1 1.12.x / Unity 2019.4 LTS）打造的全新一代、高度解耦、支持**动态插拔小组件、自由鼠标拖拽排版与参数通配符自定义**的飞行仪表开发框架。

---

## 目录

1. [模块化地基与设计哲学](#1-模块化地基与设计哲学)
2. [核心子系统架构](#2-核心子系统架构)
   - [2.1 遥测通配符引擎 (TelemetryTokenEngine)](#21-遥测通配符引擎-telemetrytokenengine)
   - [2.2 组件抽象基类 (BaseFlightWidget)](#22-组件抽象基类-baseflightwidget)
   - [2.3 自由拖拽与吸附交互 (WidgetDragHandler)](#23-自由拖拽与吸附交互-widgetdraghandler)
   - [2.4 自定义通配符卡片 (CustomTokenTextWidget)](#24-自定义通配符卡片-customtokentextwidget)
3. [着色器与渲染表现层](#3-着色器与渲染表现层)
4. [4 大内置主题预设](#4-4-大内置主题预设)
5. [游戏内操作指南 (Alt+N 拖拽与组件管理)](#5-游戏内操作指南-altn-拖拽与组件管理)
6. [项目目录结构](#6-项目目录结构)

---

## 1. 模块化地基与设计哲学

Modular Flight Panel 彻底抛弃了将各个表盘“静态焊死在同一块画布”上的传统做法，将整套仪表盘重构为**真正的“航电积木”**：
- **万物皆组件 (Everything is a Widget)**：中心 3D 姿态球、弧形油门表、速度盒、高度盒、SAS 罗盘，乃至用户自己新建的文本卡片，全部继承自统一抽象基类 [`BaseFlightWidget`](file:///c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel/UI/BaseFlightWidget.cs)。
- **所见即所得自由拖拽**：在飞行中按 `Alt+N` 点击“开启自由拖拽模式”，即可用鼠标在屏幕上任意拖拽移动每一个小组件，松手自动 10px 网格对齐并保存至 JSON！
- **通配符参数自由配置**：支持像编写代码表达式一样配置界面参数（如 `"{SPD:SURF:F1} m/s | Q: {Q} | TWR: {TWR}"`），实时计算更新。
- **全面标准化铁律（无特例、无分层妥协）**：MFP 严禁任何组件内部硬编码读数或写死界面文案标签。不论是简单表盘还是高度集成的波音 747 EICAS 等成套复杂航电，全部读数与文案必须 100% 接入 `TelemetryTokenEngine`，支持用户在配置文件中自由热替换任意通道与标签。

---

## 2. 核心子系统架构

### 2.1 遥测通配符引擎 (`TelemetryTokenEngine.cs`)
支持对任意字符串模板进行高性能正则替换计算，支持的通配符包括：
- **速度类**：`{SPD}` (当前模式)、`{SPD:SURF}` (地面速)、`{SPD:OBT}` (轨道速)、`{SPD:TGT}` (相对目标速)、`{MACH}` (马赫数)
- **高度类**：`{ALT:AGL}` (真高)、`{ALT:ASL}` (海拔)、支持智能距离后缀 `{ALT:AGL:DIST}` (自动显示 km / m)
- **姿态与动力学**：`{HDG}` (真北航向 000°~360°)、`{PITCH}` (俯仰)、`{ROLL}` (滚转)、`{VSI}` (爬升率)
- **力学与推进**：`{TWR}` (实时推重比)、`{GFORCE}` (重力过载 G)、`{Q}` (大气动压 kPa)、`{THROTTLE}` (油门百分比)、`{PROP}` (分级推进剂余量)
- **轨道参量**：`{AP}` (远地点)、`{PE}` (近地点)、`{TAP}` (远地点倒计时 T-00:00:00)、`{TPE}` (近地点倒计时)
- **飞控状态**：`{SAS}` (当前锁定朝向)、`{RCS}` (开启状态)、`{BODY}` (天体名)、`{SITUATION}` (飞行状态)

### 2.2 组件抽象基类 (`BaseFlightWidget.cs`)
任何新组件只要继承 `BaseFlightWidget` 并实现 `OnInitialize`、`ApplyTheme` 与 `OnUpdateTelemetry`，即可自动获得：
1. 自动挂载 `WidgetDragHandler` 自由拖拽把手；
2. 自动跟随屏幕 2K/4K 分辨率 DPI 缩放；
3. 自动同步调色板与主题切换；
4. 坐标与可见性状态自动读写 `layout.json`。

### 2.3 自由拖拽与吸附交互 (`WidgetDragHandler.cs`)
在普通飞行时，交互把手完全隐形且不阻挡点击射线；进入编辑模式后，小组件被荧光边框包裹并浮现标题把手，鼠标拖动时以 `delta / canvas.scaleFactor` 精确跟随鼠标移动，松手自动按 10px 网格吸附对齐。

### 2.4 自定义通配符卡片 (`CustomTokenTextWidget.cs`)
玩家无需编写一行 C# 代码，直接在游戏内输入标题与参数模板，即可瞬间在屏幕上派生出一个全新的航电监控小卡片！

---

## 3. 着色器与渲染表现层

基于 Unity 2019.4 LTS 无头构建出的 `modularflightpanel.ksp` 包含 5 大着色器：
1. **`ModularFlightPanel/NavballModern`**：现代航电玻璃座舱风格（平滑梯度天顶/地平线、现代俯仰精密刻度梯、菲涅尔边缘微光）。
2. **`ModularFlightPanel/NavballHalftone`**：赛博向量示波器风格（屏幕空间点阵半色调 Dither 渐变）。
3. **`ModularFlightPanel/RadialSegmentedMeter`**：极坐标弧形分段表（油门、垂直速度、推进剂）。
4. **`ModularFlightPanel/GlassCockpitUI`**：现代磨砂玻璃质感边框与微光发热条。
5. **`ModularFlightPanel/NeonGlowUI`**：切角科技感线框与细微 CRT 扫描线。

---

## 4. 4 大内置主题预设

存放在 `GameData/ModularFlightPanel/Themes/`：
- **`modern_aero.json`**：Modern Glass Cockpit (波音 787 / 空客 A350 航电冰蓝纯白)
- **`cyber_neon.json`**：Cyber Neon (赛博点阵亮青/亮绿/洋红高反差)
- **`classic_aero.json`**：Classic Aero (经典蓝棕原版复刻)
- **`apollo_1969.json`**：Apollo 1969 AGC (阿波罗登月舱绿色荧光管)

---

## 5. 游戏内操作指南 (Alt+N 拖拽与组件管理)

1. 进入任意飞行场景（Flight Scene），仪表板自动挂载并替换原版 Navball。
2. **按下 `Alt + N`**：
   - **进入拖拽编辑**：点击顶部绿色大按钮 `▶ [开启自由拖拽模式]`，此时屏幕上所有小组件均浮现把手，直接用鼠标拖动摆放到任意顺手的位置！
   - **新建自定义小组件**：在面板中输入组件标题与参数模板（例如输入标题 `巡航动力`，模板 `Q: {Q:F2} | TWR: {TWR:F2} | G: {GFORCE}`），点击 `+ 创建并在屏幕上生成新小组件`，屏幕上立即出现该卡片并可随意拖动！
   - **大屏缩放**：随意拖动“尺寸比例”滑块（0.7x ~ 1.8x）缩放全局尺寸。
   - **主题热切换**：一键无缝切换风格。
   - **保存与退出**：再次按下 `Alt + N` 或点击底部“保存并关闭”，编辑模式退出，最新布局坐标自动持久化存储。

---

## 6. 项目目录结构

```
KSP_naviball/
│
├── README.md                           <- 本技术规范文档
├── .gitignore
│
├── src/
│   ├── ModularFlightPanel.sln          <- Visual Studio 解决方案
│   └── ModularFlightPanel/
│       ├── ModularFlightPanel.csproj
│       ├── Core/
│       │   ├── NavballPlugin.cs        <- KSPAddon 插件主入口
│       │   ├── TelemetryHub.cs         <- 遥测数据中心
│       │   ├── TelemetryTokenEngine.cs <- 通配符与变量求值引擎
│       │   ├── HarmonyPatches.cs       <- 原版 Navball 隐藏接管
│       │   └── AssetLoader.cs          <- AssetBundle 加载器
│       ├── Config/
│       │   ├── ThemeConfig.cs          <- 主题定义模型
│       │   ├── ThemeManager.cs         <- 主题配置管理器
│       │   ├── WidgetConfig.cs         <- 组件坐标与模板模型
│       │   ├── WidgetLayoutManager.cs  <- 布局持久化存储管理器
│       │   └── LayoutConfig.cs
│       └── UI/
│           ├── BaseFlightWidget.cs     <- 统一小组件抽象基类
│           ├── WidgetDragHandler.cs    <- 自由鼠标拖拽与网格吸附
│           ├── FlightHUDManager.cs     <- 画布容器与小组件装配总管
│           ├── UIFactory.cs            <- 响应式 UGUI 辅助类
│           ├── SettingsGUI.cs          <- 游戏内设置面板 (Alt+N)
│           └── Widgets/
│               ├── CustomTokenTextWidget.cs <- 通配符自定义飞行卡片
│               ├── NavballSphereWidget.cs   <- 3D 姿态球
│               ├── ArcMeterWidget.cs        <- 弧形分段表
│               ├── DigitalBoxWidget.cs      <- 速度/高度数显盒
│               ├── OrbitalInfoWidget.cs     <- 轨道参数栏
│               ├── SASDialWidget.cs         <- 环形 SAS 罗盘
│               └── BottomControlsWidget.cs  <- RCS/SAS 控制底栏
│
├── unity/                              <- Unity 2019.4.18f1 资产工程
│   ├── Assets/
│   │   ├── Shaders/                    <- 5 款专属 Shader
│   │   └── Editor/BuildAssetBundles.cs
│
└── GameData/                           <- KSP 安装分发产物
    └── ModularFlightPanel/
        ├── Plugins/ModularFlightPanel.dll
        ├── AssetBundles/modularflightpanel.ksp
        └── Themes/*.json
```
