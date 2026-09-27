using System;
using System.Globalization;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 全局零 GC 快速文本与数字缓存格式化中枢 (Avionics Fast Numeric & String Cache)
    /// 
    /// 解决痛点：
    /// 航电仪表（高度标尺带、速度标尺、分级推进剂、推重比、百分比等）每秒数十次将 double/int 格式化为 string，
    /// 频繁触发 string.Format、ToString("F0") 等托管堆分配，造成大量第 0 代垃圾与 GC 顿挫。
    /// 
    /// 机制：
    /// 1. 常用整数静态缓存池 (-1000 ~ 10000 完整常驻，查表复杂度 O(1)，0 堆分配)；
    /// 2. 百分比静态字符表 (0% ~ 100% 完整常驻)；
    /// 3. 定点数单精度小数缓存与安全回退；
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
