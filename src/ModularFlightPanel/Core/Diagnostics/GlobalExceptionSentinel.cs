using System;
using UnityEngine;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.Core.Diagnostics
{
    /// <summary>
    /// KSP 全局未捕获异常监听与容灾自愈哨兵 (Global Unhandled Exception Sentinel)
    /// 核心设计目标：
    /// 1. 深度监听 Unity 全局异常抛出总线 (Application.logMessageReceived)；
    /// 2. 自动过滤并精确识别归属于 ModularFlightPanel 命名空间的所有未捕获异常；
    /// 3. 当任何异常穿透全部业务层逃逸至引擎时，哨兵在底层无条件释放所有输入锁定 (MFPInputLock)，确保玩家操作绝不瘫痪；
    /// 4. 连续多帧严重未捕获异常时自动联动 MFPSafetyFallback 触发平滑降级，100% 保活原版飞行仪表。
    /// </summary>
    public static class GlobalExceptionSentinel
    {
        private static bool _isInstalled = false;
        private static int _consecutiveMfpExceptions = 0;
        private static long _lastExceptionTick = 0;
        private const int MaxConsecutiveExceptionsBeforeFallback = 5;

        public static void Install()
        {
            if (_isInstalled) return;
            _isInstalled = true;
            Application.logMessageReceived += HandleLogMessage;
            MFPLogger.Info(MFPLogger.CatCore, "[GlobalExceptionSentinel] 全局异常容灾哨兵已成功挂载。");
        }

        public static void Uninstall()
        {
            if (!_isInstalled) return;
            _isInstalled = false;
            Application.logMessageReceived -= HandleLogMessage;
            MFPLogger.Info(MFPLogger.CatCore, "[GlobalExceptionSentinel] 全局异常容灾哨兵已卸载。");
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception) return;

            // 精准匹配属于 MFP 域的异常
            bool isMfpException = (!string.IsNullOrEmpty(stackTrace) && stackTrace.IndexOf("ModularFlightPanel", StringComparison.OrdinalIgnoreCase) >= 0)
                               || (!string.IsNullOrEmpty(condition) && condition.IndexOf("ModularFlightPanel", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isMfpException) return;

            long now = DateTime.UtcNow.Ticks;
            if (now - _lastExceptionTick < TimeSpan.FromSeconds(2).Ticks)
            {
                _consecutiveMfpExceptions++;
            }
            else
            {
                _consecutiveMfpExceptions = 1;
            }
            _lastExceptionTick = now;

            // 无论任何 MFP 未捕获异常发生，底层首要任务是保障玩家操作权：立即释放控制锁
            try
            {
                MFPInputLock.ReleaseAllLocks();
            }
            catch { }

            MFPLogger.Error(MFPLogger.CatCore, $"[GlobalExceptionSentinel] 截获未捕获异常 ({_consecutiveMfpExceptions}/{MaxConsecutiveExceptionsBeforeFallback}): {condition}\n{stackTrace}");

            // 若异常在短时间内持续恶化累积，主动启动系统级安全熔断
            if (_consecutiveMfpExceptions >= MaxConsecutiveExceptionsBeforeFallback && !MFPSafetyFallback.IsFaulted)
            {
                MFPSafetyFallback.TriggerFaultFallback("全局未捕获异常持续激增熔断 (Consecutive Unhandled Exceptions)", new Exception(condition));
            }
        }
    }
}
