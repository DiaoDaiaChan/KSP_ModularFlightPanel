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
    /// </summary>
    public static class TelemetryCatalog
    {
        public static readonly List<TelemetryParam> Parameters = new List<TelemetryParam>
        {
            // 1. 速度与马赫
            new TelemetryParam("{SPD}", "🚀 速度与马赫", "当前模式速度", "当前参考系(地表/轨道/目标)的综合航速", "m/s", 0, 1000, 750, 900, true, 10f),
            new TelemetryParam("{SPD:SURF}", "🚀 速度与马赫", "地表速度 (Surf)", "相对于当前天体地表的线速度", "m/s", 0, 1000, 750, 900, true, 10f),
            new TelemetryParam("{SPD:OBT}", "🚀 速度与马赫", "轨道速度 (Orbit)", "开普勒天体参考系下的真轨道速度", "m/s", 0, 3500, 2500, 3000, true, 50f),
            new TelemetryParam("{SPD:TGT}", "🚀 速度与马赫", "目标相对速度 (Target)", "相对于锁定目标的相对标量速度", "m/s", 0, 200, 50, 100, true, 5f),
            new TelemetryParam("{MACH}", "🚀 速度与马赫", "马赫数 (Mach)", "当前大气音速比率", "M", 0, 8, 4, 6, true, 0.5f),
            new TelemetryParam("{VSI}", "🚀 速度与马赫", "垂直速度 (VSI)", "爬升(+)或下沉(-)垂直线速度", "m/s", -50, 50, -30, -45, true, 5f),

            // 2. 高度与垂直
            new TelemetryParam("{ALT:ASL}", "🏔️ 高度与垂直", "海拔高度 (ASL)", "相对于海平面/基准水准面的绝对高度", "m", 0, 100000, 70000, 90000, true, 100f),
            new TelemetryParam("{ALT:AGL}", "🏔️ 高度与垂直", "雷达真高 (AGL)", "探地雷达测得的离地净空高度", "m", 0, 5000, 300, 100, true, 50f),
            new TelemetryParam("{ALT:ASL:DIST}", "🏔️ 高度与垂直", "海拔高度 (智能单位)", "自动在 m / km 间切换的海拔高度文本", "km", 0, 100000, 70000, 90000, true),
            new TelemetryParam("{ALT:AGL:DIST}", "🏔️ 高度与垂直", "雷达真高 (智能单位)", "自动在 m / km 间切换的真高文本", "m", 0, 5000, 300, 100, true),

            // 3. 动力与推进
            new TelemetryParam("{THROTTLE}", "⚡ 动力与推进", "引擎油门", "当前发动机总指令输出开度", "%", 0, 100, 85, 100, false, 10f),
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

            // 5. 姿态与控制
            new TelemetryParam("{HDG}", "🧭 姿态与控制", "航向角 (HDG)", "真北罗盘方位角 (000°~359°)", "°", 0, 360, 360, 360, false, 10f),
            new TelemetryParam("{PITCH}", "🧭 姿态与控制", "俯仰角 (Pitch)", "相对于当地水平面的俯仰倾角 (-90°~+90°)", "°", -90, 90, 45, 75, false, 10f),
            new TelemetryParam("{ROLL}", "🧭 姿态与控制", "滚转角 (Roll)", "载具绕纵轴旋转角 (-180°~+180°)", "°", -180, 180, 90, 120, false, 15f),
            new TelemetryParam("{SAS}", "🧭 姿态与控制", "SAS 状态与模式", "自动稳定仪启用状态或引导模式", "", 0, 0, 0, 0, false),
            new TelemetryParam("{RCS}", "🧭 姿态与控制", "RCS 姿控状态", "反作用控制系统开关状态 (ON / OFF)", "", 0, 0, 0, 0, false),

            // 6. 外部 Mod 探针 (FAR / KER / MechJeb)
            new TelemetryParam("{FAR:IAS}", "📡 外部探针 (FAR/KER/MJ)", "FAR 指示空速 (IAS)", "FAR 空气动力学指示空速", "m/s", 0, 1000, 700, 850, true, 20f),
            new TelemetryParam("{FAR:EAS}", "📡 外部探针 (FAR/KER/MJ)", "FAR 等效空速 (EAS)", "FAR 空气动力学等效空速", "m/s", 0, 1000, 700, 850, true, 20f),
            new TelemetryParam("{FAR:AOA}", "📡 外部探针 (FAR/KER/MJ)", "FAR 气动迎角 (AoA)", "飞船纵轴与气流相对来流夹角", "°", -15, 25, 15, 20, true, 5f),
            new TelemetryParam("{FAR:SIDESLIP}", "📡 外部探针 (FAR/KER/MJ)", "FAR 侧滑角 (Sideslip)", "机体偏航与气流侧向偏角", "°", -20, 20, 10, 15, true, 5f),
            new TelemetryParam("{FAR:Q}", "📡 外部探针 (FAR/KER/MJ)", "FAR 真实动压 (Q)", "FAR 准确大气动压解算", "kPa", 0, 40, 25, 35, true, 5f),
            new TelemetryParam("{FAR:STALL}", "📡 外部探针 (FAR/KER/MJ)", "FAR 翼面失速百分比", "机翼表面气流分离失速比例 (0~100%)", "%", 0, 100, 50, 80, false, 10f),
            new TelemetryParam("{FAR:LD}", "📡 外部探针 (FAR/KER/MJ)", "FAR 升阻比 (L/D)", "即时气动升阻效率比值", "", 0, 15, 8, 12, true, 1f),
            new TelemetryParam("{KER:DV}", "📡 外部探针 (FAR/KER/MJ)", "KER 当前级 Delta-V", "Kerbal Engineer 解算之当前分级真空 dV", "m/s", 0, 4000, 2500, 3500, true, 100f),
            new TelemetryParam("{KER:TOTALDV}", "📡 外部探针 (FAR/KER/MJ)", "KER 全舰总 Delta-V", "Kerbal Engineer 解算之载具全部分级累计 dV", "m/s", 0, 12000, 8000, 10000, true, 200f),
            new TelemetryParam("{KER:TWR}", "📡 外部探针 (FAR/KER/MJ)", "KER 权威推重比", "Kerbal Engineer 高精度实时 TWR", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{KER:BURNTIME}", "📡 外部探针 (FAR/KER/MJ)", "KER 当前级燃烧时间", "当前级满推力持续燃烧预计时间", "", 0, 600, 300, 60, true),
            new TelemetryParam("{KER:ISP}", "📡 外部探针 (FAR/KER/MJ)", "KER 平均比冲 (Isp)", "当前点火引擎综合比冲", "s", 0, 500, 350, 420, true, 20f),
            new TelemetryParam("{MJ:DV}", "📡 外部探针 (FAR/KER/MJ)", "MechJeb 当前级 dV", "MechJeb 2 实时解算分级 Delta-V", "m/s", 0, 4000, 2500, 3500, true, 100f),
            new TelemetryParam("{MJ:TOTALDV}", "📡 外部探针 (FAR/KER/MJ)", "MechJeb 总 dV", "MechJeb 2 实时解算载具总可用 Delta-V", "m/s", 0, 12000, 8000, 10000, true, 200f),
            new TelemetryParam("{MJ:TWR}", "📡 外部探针 (FAR/KER/MJ)", "MechJeb 推重比", "MechJeb 2 动力状态机 TWR", "", 0, 5, 3.5, 4.5, true, 0.5f),
            new TelemetryParam("{MJ:TERMINALVEL}", "📡 外部探针 (FAR/KER/MJ)", "MechJeb 终端沉降速度", "当前大气阻力平衡状态下的终端沉降速率", "m/s", 0, 500, 300, 400, true, 10f)
        };

        public static readonly string[] Categories = new string[]
        {
            "全部参数",
            "🚀 速度与马赫",
            "🏔️ 高度与垂直",
            "⚡ 动力与推进",
            "🌌 轨道与机动",
            "🧭 姿态与控制",
            "📡 外部探针 (FAR/KER/MJ)"
        };

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
