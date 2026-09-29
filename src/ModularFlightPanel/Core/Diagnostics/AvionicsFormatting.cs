using System;
using System.Globalization;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 航电标准化高频度量与时序格式化中枢 (Avionics Standardized Formatting Utility)
    /// 集中收敛全仓库 45 个小组件中重复的倒计时、持续时间、紧凑时间、国际单位制 (SI) 距离与速度格式化逻辑，
    /// 彻底消除组件内部私有静态格式化方法的重复代码与离散维护。
    /// </summary>
    public static class AvionicsFormatting
    {
        /// <summary>
        /// 标准秒数持续时间格式化 ("hh:mm:ss" 或 "mm:ss")
        /// </summary>
        public static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "00:00";
            long totalSec = (long)Math.Max(0.0, seconds);
            long hours = totalSec / 3600;
            long minutes = (totalSec % 3600) / 60;
            long secs = totalSec % 60;
            if (hours > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", hours, minutes, secs);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", minutes, secs);
        }

        /// <summary>
        /// 紧凑型飞行时序格式化（例如 "12d", "04:15:30", "08:42"）
        /// </summary>
        public static string FormatDurationCompact(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "--:--";
            if (seconds >= 86400.0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:F0}d", seconds / 86400.0);
            }
            long totalSec = (long)seconds;
            long hours = totalSec / 3600;
            long minutes = (totalSec % 3600) / 60;
            long secs = totalSec % 60;
            if (hours > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", hours, minutes, secs);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", minutes, secs);
        }

        /// <summary>
        /// 标准机动/任务倒计时格式化（支持正负时序，如 "T-01:23", "T+00:45"）
        /// </summary>
        public static string FormatCountdown(double seconds, string prefix = "T-")
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return prefix + "00:00";
            if (seconds < 0.0)
            {
                return "T+" + FormatDuration(-seconds);
            }
            return prefix + FormatDuration(seconds);
        }

        /// <summary>
        /// 国际单位制 (SI) 距离自适应量程格式化 (Gm / Mm / km / m)
        /// </summary>
        public static string FormatMetricDistance(double meters, string format = "F1")
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters)) return "---";
            double abs = Math.Abs(meters);
            if (abs >= 1e9) return (meters * 1e-9).ToString(format, CultureInfo.InvariantCulture) + " Gm";
            if (abs >= 1e6) return (meters * 1e-6).ToString(format, CultureInfo.InvariantCulture) + " Mm";
            if (abs >= 1e3) return (meters * 1e-3).ToString(format, CultureInfo.InvariantCulture) + " km";
            return meters.ToString(format, CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>
        /// 国际单位制 (SI) 速度自适应量程格式化 (km/s / m/s)
        /// </summary>
        public static string FormatMetricSpeed(double mps, string format = "F1")
        {
            if (double.IsNaN(mps) || double.IsInfinity(mps)) return "---";
            double abs = Math.Abs(mps);
            if (abs >= 1e3) return (mps * 1e-3).ToString(format, CultureInfo.InvariantCulture) + " km/s";
            return mps.ToString(format, CultureInfo.InvariantCulture) + " m/s";
        }
    }

    /// <summary>
    /// 全局零 GC 快速文本与数字缓存格式化中枢 (Avionics Fast Numeric & String Cache)
    /// </summary>
    public static class AvionicsFastFormat
    {
        /// <summary>
        /// 零 GC 快速整数转字符串：直接委托 CacheManager 静态常驻表 (-1000 ~ 9999)，0 堆分配
        /// </summary>
        public static string FastInt(int value)
        {
            return CacheManager.FastInt(value);
        }

        /// <summary>
        /// 零 GC 快速浮点取整转字符串 (如仪表标尺带刻度 100, 200, 300)
        /// </summary>
        public static string FastRoundInt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "---";
            long rounded = (long)Math.Round(value);
            return CacheManager.FastInt((int)rounded);
        }

        /// <summary>
        /// 零 GC 快速百分比转字符串 (输入 0.0 ~ 1.0 或 0 ~ 100)
        /// </summary>
        public static string FastPercent(float ratio, bool isZeroToOne = true)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "0%";
            int pct = Mathf.Clamp(Mathf.RoundToInt(isZeroToOne ? ratio * 100f : ratio), 0, 100);
            return CacheManager.FastPercent(pct);
        }

        /// <summary>
        /// 零 GC 常用两位数补零格式化 (00 ~ 99)
        /// </summary>
        public static string FastTwoDigits(int value)
        {
            if (value >= 0 && value < 10)
            {
                return "0" + CacheManager.FastInt(value);
            }
            if (value >= 10 && value <= 99)
            {
                return CacheManager.FastInt(value);
            }
            return value.ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
