using System;
using System.Collections.Generic;

namespace ModularFlightPanel.UI.Settings
{
    public class TelemetryParam
    {
        public string Token { get; set; }
        public string Category { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string DefaultUnit { get; set; }
        public double DefaultMin { get; set; }
        public double DefaultMax { get; set; }
        public double DefaultCaution { get; set; }
        public double DefaultWarning { get; set; }
        public bool DefaultIsSoftLimit { get; set; }
        public float DefaultStep { get; set; }

        public TelemetryParam(string token, string category, string name, string desc, string unit, double min, double max, double caution, double warning, bool isSoft, float step = 10f)
        {
            Token = token;
            Category = category;
            DisplayName = name;
            Description = desc;
            DefaultUnit = unit;
            DefaultMin = min;
            DefaultMax = max;
            DefaultCaution = caution;
            DefaultWarning = warning;
            DefaultIsSoftLimit = isSoft;
            DefaultStep = step;
        }
    }

    /// <summary>
    /// 全量遥测参数菜单词典 (供装配台分类检索、一键填槽与参数说明)
    /// 支持动态注入外部模组 (FAR / KER / MechJeb / Principia) 反射遍历得出的海量遥测参数。
    /// </summary>
    public static class TelemetryCatalog
    {
        public static readonly List<TelemetryParam> Parameters = new List<TelemetryParam>
        {
            // 1. 速度与马赫
            new TelemetryParam("{SPD}", "🚀 速度与马赫", "当前模式速度", "当前参考系(地表/轨道/目标)的综合航速", "m/s", 0, 1000, 750, 900, true, 10f),
            new TelemetryParam("{SPD:SURF}", "🚀 速度与马赫", "地表速度 (Surf)", "相对于当前天体地表的线速度", "m/s", 0, 1000, 750, 900, true, 10f),
            new TelemetryParam("{SPD:SURF:KMH}", "🚀 速度与马赫", "地表速度 (km/h)", "SpaceX 官方广播规格地表时速", "km/h", 0, 28000, 20000, 26000, true, 500f),
            new TelemetryParam("{SPD:OBT}", "🚀 速度与马赫", "轨道速度 (Orbit)", "开普勒天体参考系下的真轨道速度", "m/s", 0, 3500, 2500, 3000, true, 50f),
            new TelemetryParam("{SPD:TGT}", "🚀 速度与马赫", "目标相对速度 (Target)", "相对于锁定目标的相对标量速度", "m/s", 0, 200, 50, 100, true, 5f),
            new TelemetryParam("{MACH}", "🚀 速度与马赫", "马赫数 (Mach)", "当前大气音速比率", "M", 0, 8, 4, 6, true, 0.5f),
            new TelemetryParam("{VSI}", "🚀 速度与马赫", "垂直速度 (VSI)", "爬升(+)或下沉(-)垂直线速度", "m/s", -50, 50, -30, -45, true, 5f),

            // 2. 高度与垂直
            new TelemetryParam("{ALT:ASL}", "🏔️ 高度与垂直", "海拔高度 (ASL)", "相对于海平面/基准水准面的绝对高度", "m", 0, 100000, 70000, 90000, true, 100f),
            new TelemetryParam("{ALT:ASL:KM}", "🏔️ 高度与垂直", "海拔高度 (km)", "SpaceX 官方广播规格海拔高度", "km", 0, 250, 150, 200, true, 5f),
            new TelemetryParam("{ALT:AGL}", "🏔️ 高度与垂直", "雷达真高 (AGL)", "探地雷达测得的离地净空高度", "m", 0, 5000, 300, 100, true, 50f),
            new TelemetryParam("{ALT:ASL:DIST}", "🏔️ 高度与垂直", "海拔高度 (智能单位)", "自动在 m / km 间切换的海拔高度文本", "km", 0, 100000, 70000, 90000, true),
            new TelemetryParam("{ALT:AGL:DIST}", "🏔️ 高度与垂直", "雷达真高 (智能单位)", "自动在 m / km 间切换的真高文本", "m", 0, 5000, 300, 100, true),

            // 3. 动力与推进
            new TelemetryParam("{THROTTLE}", "⚡ 动力与推进", "引擎油门", "当前发动机总指令输出开度", "%", 0, 100, 85, 100, false, 10f),
            new TelemetryParam("{ENG}", "⚡ 动力与推进", "运行中引擎数", "当前分级正在产生推力的发动机数量", "", 0, 33, 0, 0, false, 1f),
            new TelemetryParam("{ENG:TOTAL}", "⚡ 动力与推进", "当前级总引擎数", "当前分级挂载的全部发动机总数", "", 0, 33, 0, 0, false, 1f),
            new TelemetryParam("{PROP}", "⚡ 动力与推进", "当前级推进剂", "当前激活分级剩余燃料可用百分比", "%", 0, 100, 20, 10, false, 10f),
            new TelemetryParam("{TWR}", "⚡ 动力与推进", "推重比 (TWR)", "可用发动机总推力与当前重力比值", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{GFORCE}", "⚡ 动力与推进", "重力加速度 (G力)", "载具承受的即时过载 G 值", "G", 0, 15, 8, 12, true, 1f),
            new TelemetryParam("{Q}", "⚡ 动力与推进", "大气动压 (Q)", "高速穿过稠密大气层时的动力学迎面压力", "kPa", 0, 40, 25, 35, true, 5f),

            // 4. 轨道与机动
            new TelemetryParam("{AP:DIST}", "🌌 轨道与机动", "远地点高度 (Ap)", "轨道最高点至天体海平面的净距", "km", 0, 500000, 300000, 400000, true),
            new TelemetryParam("{PE:DIST}", "🌌 轨道与机动", "近地点高度 (Pe)", "轨道最低点至天体海平面的净距", "km", 0, 500000, 70000, 30000, true),
            new TelemetryParam("{TAP}", "🌌 轨道与机动", "到达远地点时间", "距离远地点 Ap 的倒计时", "", 0, 3600, 1800, 300, true),
            new TelemetryParam("{TPE}", "🌌 轨道与机动", "到达近地点时间", "距离近地点 Pe 的倒计时", "", 0, 3600, 1800, 300, true),
            new TelemetryParam("{FRAME}", "🌌 轨道与机动", "导航参考系名称", "Principia 或原生参考系 (SURFACE / BARYCENTRIC 等)", "", 0, 0, 0, 0, false),
            new TelemetryParam("{BODY}", "🌌 轨道与机动", "环绕主天体", "当前载具所属引力影响球 SOI 母星", "", 0, 0, 0, 0, false),
            new TelemetryParam("{SITUATION}", "🌌 轨道与机动", "飞行情景状态", "FLYING / SUB_ORBITAL / ORBITING / LANDED 等", "", 0, 0, 0, 0, false),
            new TelemetryParam("{MN:DV}", "🌌 轨道与机动", "机动剩余 Delta-V", "当前计划机动节点待消耗之速度增量", "m/s", 0, 2000, 1500, 1800, true, 20f),
            new TelemetryParam("{MN:TOTALDV}", "🌌 轨道与机动", "机动总计划 Delta-V", "当前机动节点初始总规划 Delta-V", "m/s", 0, 2000, 1500, 1800, true, 20f),
            new TelemetryParam("{MN:TIME}", "🌌 轨道与机动", "到达机动节点时间", "距离计划机动节点的到达倒计时", "", 0, 3600, 1800, 300, true),
            new TelemetryParam("{MN:BURNTIME}", "🌌 轨道与机动", "预计变轨燃烧时长", "根据当前引擎推力推算之燃烧持续秒数", "", 0, 600, 300, 60, true),
            new TelemetryParam("{MN:TIMETOBURN}", "🌌 轨道与机动", "提前点火倒计时", "按50%燃烧时长提前启动引擎之倒计时时刻", "", 0, 3600, 1800, 60, true),
            new TelemetryParam("{INC}", "🌌 轨道与机动", "轨道倾角 (Inc)", "航天器轨道平面相对于基准参考平面的夹角", "°", 0, 180, 90, 120, false, 5f),

            // 5. 目标交会对接
            new TelemetryParam("{TGT:DIST}", "🎯 目标交会对接", "目标距离 (智能单位)", "自动在 m / km 间切换的目标绝对间距", "m", 0, 50000, 2000, 500, true),
            new TelemetryParam("{TGT:RATE}", "🎯 目标交会对接", "目标接近率 (Closing Rate)", "相对于锁定目标的接近速度(+接近/-远离)", "m/s", -20, 20, 5, 10, true, 1f),
            new TelemetryParam("{TGT:X}", "🎯 目标交会对接", "目标横向偏差 (Dev X)", "沿对接口横向轴的线性横移偏离量", "m", -10, 10, 2, 5, false, 0.5f),
            new TelemetryParam("{TGT:Y}", "🎯 目标交会对接", "目标纵向偏差 (Dev Y)", "沿对接口垂直轴的线性垂向偏离量", "m", -10, 10, 2, 5, false, 0.5f),
            new TelemetryParam("{TGT:Z}", "🎯 目标交会对接", "目标轴向距离 (Dev Z)", "沿对接口前向轴的轴向接近间距", "m", 0, 100, 10, 2, false, 1f),
            new TelemetryParam("{TGT:ROLL}", "🎯 目标交会对接", "对接口滚转对齐角", "对接口滚转相位角偏差 (-180°~+180°)", "°", -180, 180, 5, 10, false, 5f),
            new TelemetryParam("{TGT:NAME}", "🎯 目标交会对接", "目标载具/对接口名称", "当前锁定的目标名称或空间站标识", "", 0, 0, 0, 0, false),

            // 6. 姿态与控制
            new TelemetryParam("{HDG}", "🧭 姿态与控制", "航向角 (HDG)", "真北罗盘方位角 (000°~359°)", "°", 0, 360, 360, 360, false, 10f),
            new TelemetryParam("{PITCH}", "🧭 姿态与控制", "俯仰角 (Pitch)", "相对于当地水平面的俯仰倾角 (-90°~+90°)", "°", -90, 90, 45, 75, false, 10f),
            new TelemetryParam("{ROLL}", "🧭 姿态与控制", "滚转角 (Roll)", "载具绕纵轴旋转角 (-180°~+180°)", "°", -180, 180, 90, 120, false, 15f),
            new TelemetryParam("{SAS}", "🧭 姿态与控制", "SAS 状态与模式", "自动稳定仪启用状态或引导模式", "", 0, 0, 0, 0, false),
            new TelemetryParam("{RCS}", "🧭 姿态与控制", "RCS 姿控状态", "反作用控制系统开关状态 (ON / OFF)", "", 0, 0, 0, 0, false),

            // 6. 外部 Mod 探针 (FAR / KER / MechJeb / Principia) - 核心常用预置
            new TelemetryParam("{FAR:IAS}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 指示空速 (IAS)", "FAR 空气动力学指示空速", "m/s", 0, 1000, 700, 850, true, 20f),
            new TelemetryParam("{FAR:EAS}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 等效空速 (EAS)", "FAR 空气动力学等效空速", "m/s", 0, 1000, 700, 850, true, 20f),
            new TelemetryParam("{FAR:AOA}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 气动迎角 (AoA)", "飞船纵轴与气流相对来流夹角", "°", -15, 25, 15, 20, true, 5f),
            new TelemetryParam("{FAR:SIDESLIP}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 侧滑角 (Sideslip)", "机体偏航与气流侧向偏角", "°", -20, 20, 10, 15, true, 5f),
            new TelemetryParam("{FAR:Q}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 真实动压 (Q)", "FAR 准确大气动压解算", "kPa", 0, 40, 25, 35, true, 5f),
            new TelemetryParam("{FAR:STALL}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 翼面失速百分比", "机翼表面气流分离失速比例 (0~100%)", "%", 0, 100, 50, 80, false, 10f),
            new TelemetryParam("{FAR:LD}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 升阻比 (L/D)", "即时气动升阻效率比值", "", 0, 15, 8, 12, true, 1f),
            new TelemetryParam("{FAR:dragForce}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 气动阻力", "当前总气动阻力大小", "kN", 0, 500, 300, 400, true, 10f),
            new TelemetryParam("{FAR:liftForce}", "📡 外部探针 (FAR/KER/MJ/Principia)", "FAR 气动升力", "当前总气动升力大小", "kN", 0, 500, 300, 400, true, 10f),

            new TelemetryParam("{KER:DV}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 当前级 Delta-V", "Kerbal Engineer 解算之当前分级真空 dV", "m/s", 0, 4000, 2500, 3500, true, 100f),
            new TelemetryParam("{KER:TOTALDV}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 全舰总 Delta-V", "Kerbal Engineer 解算之载具全部分级累计 dV", "m/s", 0, 12000, 8000, 10000, true, 200f),
            new TelemetryParam("{KER:TWR}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 权威推重比", "Kerbal Engineer 高精度实时 TWR", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{KER:BURNTIME}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 当前级燃烧时间", "当前级满推力持续燃烧预计时间", "", 0, 600, 300, 60, true),
            new TelemetryParam("{KER:ISP}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 平均比冲 (Isp)", "当前点火引擎综合比冲", "s", 0, 500, 350, 420, true, 20f),
            new TelemetryParam("{KER:SuicideCountdown}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 自杀式点火倒计时", "距离自杀式减速刹车最佳点火时刻倒计时", "s", 0, 300, 30, 10, true, 5f),
            new TelemetryParam("{KER:SuicideAltitude}", "📡 外部探针 (FAR/KER/MJ/Principia)", "KER 自杀式刹车高度", "自杀式减速预计启动的海拔/雷达真高", "m", 0, 10000, 1000, 500, true, 100f),

            new TelemetryParam("{MJ:DV}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 当前级 dV", "MechJeb 2 实时解算分级 Delta-V", "m/s", 0, 4000, 2500, 3500, true, 100f),
            new TelemetryParam("{MJ:TOTALDV}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 总 dV", "MechJeb 2 实时解算载具总可用 Delta-V", "m/s", 0, 12000, 8000, 10000, true, 200f),
            new TelemetryParam("{MJ:TWR}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 推重比", "MechJeb 2 动力状态机 TWR", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{MJ:TERMINALVEL}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 终端沉降速度", "当前大气阻力平衡状态下的终端沉降速率", "m/s", 0, 500, 300, 400, true, 10f),
            new TelemetryParam("{MJ:SurfaceTWR}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 地表 TWR", "基于地表基准重力的即时推重比", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{MJ:DragAcceleration}", "📡 外部探针 (FAR/KER/MJ/Principia)", "MechJeb 气动阻力加速度", "当前大气风向相对阻力减速度", "m/s²", 0, 50, 30, 40, true, 2f),

            new TelemetryParam("{PRINCIPIA:FRAME}", "📡 外部探针 (FAR/KER/MJ/Principia)", "Principia 绘制参考系", "当前选定的 Principia 绘制参考系全称", "", 0, 0, 0, 0, false),
            new TelemetryParam("{PRINCIPIA:NAVBALLNAME}", "📡 外部探针 (FAR/KER/MJ/Principia)", "Principia 姿态球参考系", "当前姿态球专用参考系简称", "", 0, 0, 0, 0, false),
            new TelemetryParam("{PRINCIPIA:DV}", "📡 外部探针 (FAR/KER/MJ/Principia)", "Principia 计划变轨 dV", "Principia 飞行计划下一次机动所需 Delta-V", "m/s", 0, 5000, 3000, 4500, true, 50f),
            new TelemetryParam("{PRINCIPIA:BURNTIME}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Principia 变轨燃时", "下一次计划机动的满推力点火持续时长", "s", 0, 600, 300, 60, true, 10f),
            new TelemetryParam("{PRINCIPIA:TIMETOBURN}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Principia 变轨倒计时", "距离下一次计划机动点火的倒计时", "", 0, 3600, 1800, 300, true),
            new TelemetryParam("{PRINCIPIA:ORBITDESC}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Principia 摄动轨道描述", "高精度摄动数值积分下的轨道类型描述", "", 0, 0, 0, 0, false),
            new TelemetryParam("{PRINCIPIA:NODALPERIOD}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Principia 交点公转周期", "轨道经过升交点的真实摄动交点周期", "s", 0, 100000, 50000, 80000, true, 100f),

            new TelemetryParam("{RA:DATARATE}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 下行速率", "RealAntennas 当前活跃通信链路数据传输速率", "bps", 0, 10000000, 5000000, 8000000, true, 1000f),
            new TelemetryParam("{RA:GAIN}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 天线增益", "指向对端/对地活动天线的物理增益", "dBi", 0, 60, 40, 50, true, 2f),
            new TelemetryParam("{RA:TXPOWER}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 发射功率", "当前活动发射天线射频功率", "dBm", 0, 70, 40, 60, true, 5f),
            new TelemetryParam("{RA:POWERDRAW}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 发射电耗", "天线处于发射工作状态下的实时电力消耗", "EC/s", 0, 20, 10, 15, true, 1f),
            new TelemetryParam("{RA:IDLEPOWER}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 静态电耗", "射频通信系统待机底噪电耗", "EC/s", 0, 5, 2, 4, true, 0.2f),
            new TelemetryParam("{RA:STRENGTH}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 信号质量", "归一化信噪比裕度与通信质量", "", 0, 1, 0.6, 0.8, true, 0.05f),
            new TelemetryParam("{RA:TARGET}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "RealAntennas 链路目标", "通信对端地面站或中继星名称", "", 0, 0, 0, 0, false),

            new TelemetryParam("{KERBALISM:RADIATION}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 空间辐射", "当前位置空间环境总辐射剂量率", "rad/h", 0, 10, 2, 5, true, 0.1f),
            new TelemetryParam("{KERBALISM:HABITATRADIATION}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 舱内有效辐射", "乘员舱经过防护屏蔽后的实际有效吸收辐射率", "rad/h", 0, 5, 0.5, 2, true, 0.05f),
            new TelemetryParam("{KERBALISM:PRESSURE}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 舱内气压", "乘员舱归一化气压", "atm", 0, 1.5, 1.0, 1.2, true, 0.1f),
            new TelemetryParam("{KERBALISM:POISONING}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism CO2 中毒度", "乘员舱内二氧化碳废气浓度比例", "%", 0, 100, 20, 50, true, 5f),
            new TelemetryParam("{KERBALISM:SHIELDING}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 辐射屏蔽率", "乘员舱辐射防护屏蔽系数", "%", 0, 100, 50, 80, true, 5f),
            new TelemetryParam("{KERBALISM:COMFORT}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 舒适度系数", "乘员活动空间与心理舒适度综合系数", "", 0, 5, 2, 4, true, 0.5f),
            new TelemetryParam("{KERBALISM:TEMPERATURE}", "📡 外部探针 (FAR/KER/MJ/Principia/RA/Kerbalism)", "Kerbalism 空间辐射平衡温", "载具所在空间位置有效辐射平衡温度", "K", 0, 500, 250, 350, true, 10f),
            new TelemetryParam("{KERBALISM:STORM}", "🛰️ 通信与维生 (RA/Kerbalism)", "Kerbalism 太阳风暴告警", "是否正遭遇 CME 太阳风暴轰击 (1/0)", "", 0, 1, 0, 1, true),
            new TelemetryParam("{KERBALISM:MALFUNCTION}", "🛰️ 通信与维生 (RA/Kerbalism)", "Kerbalism 部件故障告警", "载具是否存在部件机械或电气故障 (1/0)", "", 0, 1, 0, 1, true),
            new TelemetryParam("{KERBALISM:SOLAREXPOSURE}", "🛰️ 通信与维生 (RA/Kerbalism)", "Kerbalism 太阳能受光率", "全舰太阳能电池板综合受光平均效率", "%", 0, 100, 60, 90, true, 5f),

            // 7. 航迹与进近探针 (TRAJ / DOCK / GPWS)
            new TelemetryParam("{TRAJ:IMPACT_LAT}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 落点纬度", "大气减速积分落点地理纬度", "°", -90, 90, 0, 0, false),
            new TelemetryParam("{TRAJ:IMPACT_LON}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 落点经度", "大气减速积分落点地理经度", "°", -180, 180, 0, 0, false),
            new TelemetryParam("{TRAJ:DIST_TO_TARGET}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 目标偏差距", "预测着陆点与选定目标的测地线距离", "m", 0, 100000, 5000, 1000, true, 50f),
            new TelemetryParam("{TRAJ:TIME_TO_IMPACT}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 接地倒计时", "大气积分轨迹至着陆/撞击剩余时间", "s", 0, 1800, 300, 60, true, 10f),
            new TelemetryParam("{TRAJ:CORRECTED_AP}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 气阻修正远点", "经过大气减速消耗后的修正远地点", "km", 0, 500000, 70000, 30000, true),
            new TelemetryParam("{TRAJ:CORRECTED_PE}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "Trajectories 气阻修正近点", "经过大气减速消耗后的修正近地点", "km", 0, 500000, 70000, 30000, true),

            new TelemetryParam("{DOCK:DIST}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 对接净距离", "与目标对接口对接平面的法向间距", "m", 0, 500, 50, 10, true, 1f),
            new TelemetryParam("{DOCK:CVEL}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 闭合速率", "沿对接中心轴的相对靠拢速度", "m/s", -10, 10, 2, 5, true, 0.2f),
            new TelemetryParam("{DOCK:CDI_X}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 水平偏航针 (CDI-X)", "航向道水平偏移量 (米或度)", "m", -20, 20, 5, 10, true, 0.5f),
            new TelemetryParam("{DOCK:CDI_Y}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 垂直下滑针 (CDI-Y)", "下滑道垂直偏移量 (米或度)", "m", -20, 20, 5, 10, true, 0.5f),
            new TelemetryParam("{DOCK:ROLL_ERR}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 对接滚转角差", "与目标对接口对准标记的旋转夹角", "°", -180, 180, 30, 45, true, 5f),
            new TelemetryParam("{DOCK:IS_ALIGNED}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "DPAI 对齐绿灯", "姿态与位置是否完全进入容差对齐走廊", "", 0, 1, 0, 1, true),

            new TelemetryParam("{GPWS:AGL}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 离地真高", "近地警告解算的探地雷达垂直高度", "m", 0, 5000, 300, 100, true, 50f),
            new TelemetryParam("{GPWS:PULLUP_ALT}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 拉起决断高度", "根据下沉率解算的撞地前必须拉起的高度门限", "m", 0, 2000, 500, 200, true, 20f),
            new TelemetryParam("{GPWS:SINK_RATE}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 即时下沉率", "当前真实垂直负升速度", "m/s", 0, 100, 15, 30, true, 2f),
            new TelemetryParam("{GPWS:V1}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 起飞决断速 V1", "跑道起飞决断临界空速", "m/s", 0, 200, 80, 120, true, 5f),
            new TelemetryParam("{GPWS:VR}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 抬前轮速 Vr", "推荐抬机头离地空速", "m/s", 0, 200, 90, 130, true, 5f),
            new TelemetryParam("{GPWS:VREF}", "🛬 航迹与进近 (TRAJ/DOCK/GPWS)", "GPWS 进近基准速 Vref", "着陆五边最后进近基准空速", "m/s", 0, 200, 70, 110, true, 5f),

            // 8. 真实动力与可靠性探针 (RF / TF)
            new TelemetryParam("{RF:ULLAGE}", "🔥 真实动力与可靠性 (RF/TF)", "RealFuels 沉底状态码", "0=非常稳定, 1=较好, 2=不稳定, 3=非常不稳定", "", 0, 3, 1, 2, false),
            new TelemetryParam("{RF:ULLAGE_TEXT}", "🔥 真实动力与可靠性 (RF/TF)", "RealFuels 沉底文本", "Very Stable / Risky / Very Risky", "", 0, 0, 0, 0, false),
            new TelemetryParam("{RF:IGNITIONS}", "🔥 真实动力与可靠性 (RF/TF)", "RealFuels 剩余点火次数", "主引擎剩余可用点火启动配额 (-1为无限)", "", 0, 50, 2, 1, true, 1f),
            new TelemetryParam("{RF:BURNTIME}", "🔥 真实动力与可靠性 (RF/TF)", "RealFuels 额定燃时", "引擎设计最大允许连续工作时间", "s", 0, 1000, 200, 50, true, 10f),
            new TelemetryParam("{RF:CAN_IGNITE}", "🔥 真实动力与可靠性 (RF/TF)", "RealFuels 点火许可", "沉底与点火器是否均满足点火条件 (1/0)", "", 0, 1, 0, 1, true),

            new TelemetryParam("{TF:OPERATING_TIME}", "🔥 真实动力与可靠性 (RF/TF)", "TestFlight 累计燃时", "引擎当前已持续工作点火秒数", "s", 0, 1000, 400, 600, true, 10f),
            new TelemetryParam("{TF:FAIL_RATE}", "🔥 真实动力与可靠性 (RF/TF)", "TestFlight 瞬时失效率", "引擎当前点火工作点瞬时失效率", "", 0, 1, 0.05, 0.15, true, 0.01f),
            new TelemetryParam("{TF:FAIL_COUNT}", "🔥 真实动力与可靠性 (RF/TF)", "TestFlight 故障数量", "载具发生的未修复故障总数", "", 0, 10, 1, 2, true, 1f),
            new TelemetryParam("{TF:HAS_FAILED}", "🔥 真实动力与可靠性 (RF/TF)", "TestFlight 故障告警", "载具是否存在致命引擎或航电故障 (1/0)", "", 0, 1, 0, 1, true),

            // 9. 能量与热力探针 (DBS / SH)
            new TelemetryParam("{DBS:PROD}", "⚡ 能量与热力 (DBS/SH)", "DBS 总发电功率", "太阳能/核电/燃料电池综合发电", "EC/s", 0, 100, 20, 5, true, 2f),
            new TelemetryParam("{DBS:CONS}", "⚡ 能量与热力 (DBS/SH)", "DBS 总耗电功率", "全舰所有用电设备总负荷", "EC/s", 0, 100, 50, 80, true, 2f),
            new TelemetryParam("{DBS:NET}", "⚡ 能量与热力 (DBS/SH)", "DBS 净充放电率", "发电减耗电净差额 (正=充, 负=放)", "EC/s", -50, 50, -10, -25, true, 1f),
            new TelemetryParam("{DBS:DEPLETION_SEC}", "⚡ 能量与热力 (DBS/SH)", "DBS 电池耗尽秒数", "净放电状态下电量见底剩余时间", "s", 0, 7200, 600, 180, true, 30f),
            new TelemetryParam("{DBS:DEPLETION_TEXT}", "⚡ 能量与热力 (DBS/SH)", "DBS 耗尽倒计时文本", "智能格式化时间 (如 12m 34s)", "", 0, 0, 0, 0, false),

            new TelemetryParam("{SH:MAX_TEMP}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 最高回路温", "全舰各热力回路中最高工作温度", "K", 0, 2000, 1000, 1500, true, 50f),
            new TelemetryParam("{SH:LOOP_COUNT}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 回路总数", "载具构建的热力回路总数量", "", 0, 20, 0, 0, false),
            new TelemetryParam("{SH:OVERHEAT_PCT}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 过热比例", "当前回路温度超出额定温度的百分比", "%", 0, 100, 80, 95, true, 5f),
            new TelemetryParam("{SH:TOTAL_HEAT}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 总产热量", "全舰反应堆与产热部件综合功率", "kW", 0, 5000, 2500, 4000, true, 100f),
            new TelemetryParam("{SH:TOTAL_COOLING}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 总散热负荷", "辐射散热板有效散失热通量", "kW", 0, 5000, 2500, 4000, true, 100f),
            new TelemetryParam("{SH:NET_FLUX}", "⚡ 能量与热力 (DBS/SH)", "SystemHeat 净热通量", "产热与散热净差额 (正=积热, 负=降温)", "kW", -1000, 1000, 200, 500, true, 50f),

            // 10. 飞控与航电探针 (AA / RP1)
            new TelemetryParam("{AA:MASTER_SWITCH}", "✈️ 飞控与航电 (AA/RP1)", "AA 电传总开关", "AtmosphereAutopilot 电传飞控总开关 (1/0)", "", 0, 1, 0, 0, false),
            new TelemetryParam("{AA:STATUS_TEXT}", "✈️ 飞控与航电 (AA/RP1)", "AA 飞控工作模式", "Standard Fly-By-Wire / Cruise Flight 等", "", 0, 0, 0, 0, false),
            new TelemetryParam("{AA:LIFT_ACC}", "✈️ 飞控与航电 (AA/RP1)", "AA 升力加速度", "当前真实空气动力升力瞬时加速度", "m/s²", 0, 100, 40, 70, true, 2f),
            new TelemetryParam("{AA:DYN_PRESSURE}", "✈️ 飞控与航电 (AA/RP1)", "AA 高精度动压", "AA 解算之迎面气动动压", "Pa", 0, 100000, 50000, 80000, true, 1000f),
            new TelemetryParam("{AA:PITCH_AOA}", "✈️ 飞控与航电 (AA/RP1)", "AA 俯仰迎角", "机身俯仰迎角", "°", -30, 30, 15, 22, true, 2f),
            new TelemetryParam("{AA:MODERATE_AOA}", "✈️ 飞控与航电 (AA/RP1)", "AA 迎角保护限制", "电传飞控是否处于迎角极限保护中 (1/0)", "", 0, 1, 0, 0, false),
            new TelemetryParam("{AA:MODERATE_G}", "✈️ 飞控与航电 (AA/RP1)", "AA 过载保护限制", "电传飞控是否处于最大 G 力保护中 (1/0)", "", 0, 1, 0, 0, false),
            new TelemetryParam("{AA:MAX_AOA}", "✈️ 飞控与航电 (AA/RP1)", "AA 迎角门限设定", "电传保护允许的最大迎角", "°", 0, 45, 20, 25, true, 1f),
            new TelemetryParam("{AA:MAX_G}", "✈️ 飞控与航电 (AA/RP1)", "AA 过载门限设定", "电传保护允许的最大过载 G 值", "g", 0, 20, 9, 12, true, 1f),

            new TelemetryParam("{RP1:LOCK_LEVEL}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 航电锁控状态", "2=正常全控, 1=仅轴向, 0=失控锁定", "", 0, 2, 1, 0, false),
            new TelemetryParam("{RP1:STATUS_TEXT}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 航电状态文本", "UNLOCKED / AXIAL ONLY / INSUFFICIENT", "", 0, 0, 0, 0, false),
            new TelemetryParam("{RP1:CONTROLLABLE_MASS}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 航电支持吨位", "当前航电设备可控制的最大载具吨位", "t", 0, 5000, 500, 100, true, 50f),
            new TelemetryParam("{RP1:VESSEL_MASS}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 载具总质量", "航电系统计算之载具实时总吨位", "t", 0, 5000, 2500, 4000, true, 50f),
            new TelemetryParam("{RP1:MASS_MARGIN}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 控制吨位余量", "支持吨位减去载具总质量 (负为失控)", "t", -500, 500, 50, 0, true, 10f),
            new TelemetryParam("{RP1:MASS_RATIO}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 航电吨位占比", "载具质量 / 支持上限 (超过100%失控)", "%", 0, 200, 90, 100, true, 5f),
            new TelemetryParam("{RP1:POWER_DRAW}", "✈️ 飞控与航电 (AA/RP1)", "RP-1 航电总电耗", "全舰所有航电单元总消耗功率", "W", 0, 2000, 800, 1500, true, 50f),

            // 11. 资源、维生与通信系统
            new TelemetryParam("{EC}", "🔋 资源、维生与通信", "电力储量 (EC)", "飞船当前总可用电量", "EC", 0, 1000, 200, 50, true, 50f),
            new TelemetryParam("{EC:PCT}", "🔋 资源、维生与通信", "电力百分比", "飞船电池组剩余可用电量百分比", "%", 0, 100, 25, 10, false, 5f),
            new TelemetryParam("{EC:RATE}", "🔋 资源、维生与通信", "净充放电率", "当前全舰净供电(+)或净消耗(-)速率", "e/s", -20, 20, -5, -15, true, 1f),
            new TelemetryParam("{EC:VOLT}", "🔋 资源、维生与通信", "直流总线电压", "主航电设备 28V 标称母线电压", "V", 0, 36, 24, 20, true, 1f),
            new TelemetryParam("{SOLAR}", "🔋 资源、维生与通信", "太阳能发电量", "太阳能电池板总发电输出率", "e/s", 0, 50, 10, 2, true, 2f),
            new TelemetryParam("{COMM}", "🔋 资源、维生与通信", "通信网络信号", "CommNet / RealAntennas 链路综合信号强度", "%", 0, 100, 35, 10, false, 5f),
            new TelemetryParam("{CREW}", "🔋 资源、维生与通信", "乘员编制", "舱内当前搭乘乘员人数与定员容量", "", 0, 10, 0, 0, false),
            new TelemetryParam("{ATM}", "🔋 资源、维生与通信", "外部大气压强", "当前环境外部大气静压", "atm", 0, 1.2, 0.8, 0.95, true, 0.05f),
            new TelemetryParam("{TEMP}", "🔋 资源、维生与通信", "舱内/环境温度", "乘员舱生活区温度或外表面温", "°C", -50, 150, 50, 80, true, 5f),
            new TelemetryParam("{O2}", "🔋 资源、维生与通信", "氧气储量百分比", "维生氧气罐可用比例", "%", 0, 100, 25, 15, false, 5f),
            new TelemetryParam("{MONO}", "🔋 资源、维生与通信", "姿控单组元推进剂", "RCS 单组元推进剂剩余比例", "%", 0, 100, 20, 10, false, 5f),
            new TelemetryParam("{WATER}", "🔋 资源、维生与通信", "水资源储量百分比", "生活给水储罐可用比例", "%", 0, 100, 25, 15, false, 5f)
        };

        public static readonly string[] Categories = new string[]
        {
            "全部参数",
            "🚀 速度与马赫",
            "🏔️ 高度与垂直",
            "⚡ 动力与推进",
            "🌌 轨道与机动",
            "🧭 姿态与控制",
            "🔋 资源、维生与通信",
            "📡 外部探针 (FAR/KER/MJ/PRIN)",
            "🛰️ 通信与维生 (RA/Kerbalism)",
            "🛬 航迹与进近 (TRAJ/DOCK/GPWS)",
            "🔥 真实动力与可靠性 (RF/TF)",
            "⚡ 能量与热力 (DBS/SH)",
            "✈️ 飞控与航电 (AA/RP1)"
        };

        private static readonly HashSet<string> _registeredTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static TelemetryCatalog()
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                _registeredTokens.Add(Parameters[i].Token);
            }
        }

        /// <summary>
        /// 动态向词典追加探针遍历出的遥测参数，避免重复
        /// </summary>
        public static void RegisterDynamicParams(IEnumerable<TelemetryParam> extraParams)
        {
            if (extraParams == null) return;
            foreach (var p in extraParams)
            {
                if (p != null && !string.IsNullOrEmpty(p.Token) && !_registeredTokens.Contains(p.Token))
                {
                    _registeredTokens.Add(p.Token);
                    Parameters.Add(p);
                }
            }
        }

        public static TelemetryParam FindByToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string clean = token.Trim().ToUpperInvariant();
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Token.ToUpperInvariant() == clean)
                {
                    return Parameters[i];
                }
            }
            return null;
        }
    }
}
