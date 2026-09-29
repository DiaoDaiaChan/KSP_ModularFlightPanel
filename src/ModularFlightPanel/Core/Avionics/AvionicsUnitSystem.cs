using System;
using System.Globalization;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 物理量纲分类枚举
    /// </summary>
    public enum UnitDimension
    {
        None = 0,
        Length,        // 长度 / 距离 / 高度 (SI: 米 m)
        Velocity,      // 速度 (SI: 米/秒 m/s)
        Acceleration,  // 加速度 / 过载 (SI: G 或 m/s²)
        Pressure,      // 动态气压 / 静态大气压 (SI: 千帕 kPa)
        Temperature,   // 零件与环境温度 (SI: 摄氏度 °C 或 开尔文 K)
        Mass,          // 载荷与飞船质量 (SI: 吨 t 或 千克 kg)
        Angle          // 角度 (度 °)
    }

    /// <summary>
    /// 航电量纲制式体系枚举
    /// </summary>
    public enum UnitSystemMode
    {
        /// <summary>航天标准公制 (m, km, m/s, kPa, °C, t)</summary>
        MetricSI = 0,

        /// <summary>经典航空英制 (ft, FL, kts, inHg, °F, lbs)</summary>
        AviationImperial = 1,

        /// <summary>现代航空混搭制 (m/km 高度, kts 航速, hPa 气压, °C 温度)</summary>
        AviationHybrid = 2,

        /// <summary>航海导航制式 (NM 海里, kts 节, m 深度/高度)</summary>
        Nautical = 3
    }

    /// <summary>
    /// 全局航电量纲与单位换算中枢 (Avionics Unit & Dimension Context)
    /// 1. 消除各个小组件手写 if (alt > 1000) val / 1000 + "km" 的散装换算；
    /// 2. 支持公制、航空英制（英尺/海里/节/FL飞行高度层）与航海制全局一键切换；
    /// 3. 0 托管堆垃圾分配，纯数学无头计算，支持离线测试。
    /// </summary>
    public static class AvionicsUnitSystem
    {
        public static UnitSystemMode GlobalMode { get; set; } = UnitSystemMode.MetricSI;

        // 常量换算因子
        public const double MetersToFeet = 3.280839895;
        public const double MetersToNauticalMiles = 0.0005399568;
        public const double MetersPerSecondToKnots = 1.9438444924;
        public const double MetersPerSecondToKmH = 3.6;
        public const double KpaToInHg = 0.2952998307;
        public const double KpaToAtm = 0.00986923267;
        public const double KpaToHpa = 10.0;
        public const double TonsToPounds = 2204.62262;

        /// <summary>
        /// 将 SI 基础量转换为目标单位制式下的标量数值与单位符号
        /// </summary>
        public static double Convert(double siValue, UnitDimension dimension, UnitSystemMode mode, out string unitSymbol)
        {
            if (double.IsNaN(siValue) || double.IsInfinity(siValue))
            {
                unitSymbol = "--";
                return 0.0;
            }

            switch (dimension)
            {
                case UnitDimension.Length:
                    return ConvertLength(siValue, mode, out unitSymbol);

                case UnitDimension.Velocity:
                    return ConvertVelocity(siValue, mode, out unitSymbol);

                case UnitDimension.Pressure:
                    return ConvertPressure(siValue, mode, out unitSymbol);

                case UnitDimension.Temperature:
                    return ConvertTemperature(siValue, mode, out unitSymbol);

                case UnitDimension.Mass:
                    return ConvertMass(siValue, mode, out unitSymbol);

                case UnitDimension.Acceleration:
                    unitSymbol = "G";
                    return siValue;

                case UnitDimension.Angle:
                    unitSymbol = "°";
                    return siValue;

                case UnitDimension.None:
                default:
                    unitSymbol = string.Empty;
                    return siValue;
            }
        }

        /// <summary>
        /// 默认使用当前全局模式换算
        /// </summary>
        public static double Convert(double siValue, UnitDimension dimension, out string unitSymbol)
        {
            return Convert(siValue, dimension, GlobalMode, out unitSymbol);
        }

        private static double ConvertLength(double meters, UnitSystemMode mode, out string unit)
        {
            double absVal = Math.Abs(meters);

            switch (mode)
            {
                case UnitSystemMode.AviationImperial:
                    double feet = meters * MetersToFeet;
                    if (Math.Abs(feet) >= 18000.0)
                    {
                        unit = "FL";
                        return Math.Round(feet / 100.0);
                    }
                    unit = "ft";
                    return feet;

                case UnitSystemMode.Nautical:
                    if (absVal >= 1852.0)
                    {
                        unit = "NM";
                        return meters * MetersToNauticalMiles;
                    }
                    unit = "m";
                    return meters;

                case UnitSystemMode.MetricSI:
                case UnitSystemMode.AviationHybrid:
                default:
                    if (absVal >= 1000000000.0) // 1 Gm (百万公里)
                    {
                        unit = "Gm";
                        return meters * 1e-9;
                    }
                    if (absVal >= 1000000.0) // 1 Mm (千公里)
                    {
                        unit = "Mm";
                        return meters * 1e-6;
                    }
                    if (absVal >= 1000.0) // 1 km
                    {
                        unit = "km";
                        return meters * 0.001;
                    }
                    unit = "m";
                    return meters;
            }
        }

        private static double ConvertVelocity(double mps, UnitSystemMode mode, out string unit)
        {
            switch (mode)
            {
                case UnitSystemMode.AviationImperial:
                case UnitSystemMode.AviationHybrid:
                case UnitSystemMode.Nautical:
                    unit = "kts";
                    return mps * MetersPerSecondToKnots;

                case UnitSystemMode.MetricSI:
                default:
                    double absMps = Math.Abs(mps);
                    if (absMps >= 100000.0)
                    {
                        unit = "km/s";
                        return mps * 0.001;
                    }
                    unit = "m/s";
                    return mps;
            }
        }

        private static double ConvertPressure(double kpa, UnitSystemMode mode, out string unit)
        {
            switch (mode)
            {
                case UnitSystemMode.AviationImperial:
                    unit = "inHg";
                    return kpa * KpaToInHg;

                case UnitSystemMode.AviationHybrid:
                    unit = "hPa";
                    return kpa * KpaToHpa;

                case UnitSystemMode.Nautical:
                case UnitSystemMode.MetricSI:
                default:
                    if (Math.Abs(kpa) >= 1000.0)
                    {
                        unit = "MPa";
                        return kpa * 0.001;
                    }
                    unit = "kPa";
                    return kpa;
            }
        }

        private static double ConvertTemperature(double degCelsius, UnitSystemMode mode, out string unit)
        {
            switch (mode)
            {
                case UnitSystemMode.AviationImperial:
                    unit = "°F";
                    return (degCelsius * 1.8) + 32.0;

                case UnitSystemMode.MetricSI:
                case UnitSystemMode.AviationHybrid:
                case UnitSystemMode.Nautical:
                default:
                    unit = "°C";
                    return degCelsius;
            }
        }

        private static double ConvertMass(double tons, UnitSystemMode mode, out string unit)
        {
            switch (mode)
            {
                case UnitSystemMode.AviationImperial:
                    unit = "lbs";
                    return tons * TonsToPounds;

                case UnitSystemMode.MetricSI:
                case UnitSystemMode.AviationHybrid:
                case UnitSystemMode.Nautical:
                default:
                    if (Math.Abs(tons) < 1.0)
                    {
                        unit = "kg";
                        return tons * 1000.0;
                    }
                    unit = "t";
                    return tons;
            }
        }

        /// <summary>
        /// 格式化数值与单位为标准的自适应航电字符串
        /// </summary>
        public static string FormatAdaptive(double siValue, UnitDimension dimension, UnitSystemMode? modeOverride = null, string format = null)
        {
            if (double.IsNaN(siValue) || double.IsInfinity(siValue)) return "--";

            UnitSystemMode mode = modeOverride ?? GlobalMode;
            double val = Convert(siValue, dimension, mode, out string unit);

            string fmt = format;
            if (string.IsNullOrEmpty(fmt))
            {
                double abs = Math.Abs(val);
                if (abs >= 1000.0) fmt = "N0";
                else if (abs >= 100.0) fmt = "N1";
                else fmt = "N2";
            }

            string numStr = val.ToString(fmt, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(unit) ? numStr : $"{numStr} {unit}";
        }
    }
}
