using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
#if KSP_RUNTIME
using KSP.UI.Screens.Flight;
#endif

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// MFP 底层故障熔断与原版 UI 自动降级接管架构 (Safety Fallback & Circuit Breaker)
    /// 核心设计目标：
    /// 无论 MFP 在启动、装配、布局重载或逐帧渲染中由于任何原因出现致命故障或未捕获异常，
    /// 底层立即自动熔断并 100% 恢复 KSP 原版 UI (3D 姿态球、速度读数、高度表、分级序列、原版工具栏、时间加速器、通信网)，
    /// 确保玩家的飞行仪表与核心操纵界面永不缺失或黑屏。
    /// </summary>
    public static class MFPSafetyFallback
    {
        public static bool IsFaulted { get; private set; } = false;
        public static string FaultReason { get; private set; } = string.Empty;
        public static string FaultDetails { get; private set; } = string.Empty;
        public static DateTime FaultTimestamp { get; private set; }

        public static event Action<bool> OnFaultStateChanged;

        /// <summary>
        /// 触发故障熔断：全面切断 MFP 运算与渲染，彻底恢复所有原版 UI
        /// </summary>
        public static void TriggerFaultFallback(string reason, Exception ex = null)
        {
            if (IsFaulted) return; // 避免由于连续异常重复触发级联

            IsFaulted = true;
            FaultReason = reason ?? "未知航电异常 (Unknown Avionics Fault)";
            FaultDetails = ex != null ? $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : "无异常调用栈 (No Exception Details)";
            FaultTimestamp = DateTime.Now;

            MFPLogger.Error(MFPLogger.CatCore, $"[MFPSafetyFallback] Circuit breaker TRIPPED! Reason: {FaultReason}");
            if (ex != null)
            {
                MFPLogger.Exception(MFPLogger.CatCore, ex, "Fault exception details:");
            }

            try
            {
                // 1. 全局 Master Bypass 开启，旁路所有 Harmony Hook 并让原版 Update/LateUpdate 正常运行
                MFPProfiler.IsMasterBypassed = true;

                // 1.5. 退出自由拖拽编辑模式、清理框选与变换手柄
                try
                {
                    WidgetDragHandler.IsEditModeActive = false;
                    WidgetSelectionManager.ClearSelection();
                }
                catch { }

                // 1.6. 彻底解除全部键盘与鼠标控制锁，确保玩家操作绝不被挂死
                try
                {
                    ModularFlightPanel.UI.Settings.MFPInputLock.ReleaseAllLocks();
                }
                catch { }

                // 2. 停用 MFP HUD 与画布，切断所有 Widget 渲染与射线拾取
                if (FlightHUDManager.Instance != null)
                {
                    try
                    {
                        FlightHUDManager.Instance.SetUIVisible(false);
                    }
                    catch (Exception hudEx)
                    {
                        MFPLogger.Error(MFPLogger.CatUI, $"[MFPSafetyFallback] Failed to hide FlightHUDManager: {hudEx.Message}");
                    }
                }

                // 3. 完整恢复所有 KSP 原生界面与功能 (100% 原版保活)
#if KSP_RUNTIME
                try
                {
                    StockNavBallHook.RestoreAllStockUI();
                    StockToolbarHook.RestoreStockToolbar();
                }
                catch (Exception stockEx)
                {
                    MFPLogger.Error(MFPLogger.CatNavball, $"[MFPSafetyFallback] Failed to restore stock UI: {stockEx.Message}");
                }
#endif

                // 4. 发送屏幕告警广播通知玩家
#if KSP_RUNTIME
                try
                {
                    ScreenMessages.PostScreenMessage(
                        new ScreenMessage(
                            $"[ModularFlightPanel] 检测到航电系统故障，已自动熔断降级并完整恢复原版界面！\n(按 Alt+N 打开设置面板可查看详情或尝试恢复)",
                            8.0f,
                            ScreenMessageStyle.UPPER_CENTER
                        )
                    );
                }
                catch { }
#endif
            }
            catch (Exception fallbackEx)
            {
                MFPLogger.Error(MFPLogger.CatCore, $"[MFPSafetyFallback] Critical error during fallback execution: {fallbackEx.Message}");
            }

            OnFaultStateChanged.SafeInvoke(true, "OnFaultStateChanged");
        }

        /// <summary>
        /// 尝试重置熔断并恢复 MFP 正常运行
        /// </summary>
        public static bool TryRecoverFromFault()
        {
            if (!IsFaulted) return true;

            MFPLogger.Info(MFPLogger.CatCore, "[MFPSafetyFallback] Attempting to recover from fault...");

            try
            {
                IsFaulted = false;
                FaultReason = string.Empty;
                FaultDetails = string.Empty;

                // 1. 解除 Master Bypass 与重置隔离组件
                MFPProfiler.IsMasterBypassed = false;
                WidgetRenderManager.Instance?.ResetQuarantinedWidgets();

                // 2. 重新构建并显示 HUD
                if (FlightHUDManager.Instance != null)
                {
                    FlightHUDManager.Instance.SetUIVisible(true);
                    FlightHUDManager.Instance.RebuildHUD();
                }

                OnFaultStateChanged.SafeInvoke(false, "OnFaultStateChanged");

#if KSP_RUNTIME
                try
                {
                    ScreenMessages.PostScreenMessage(
                        new ScreenMessage(
                            "[ModularFlightPanel] 航电系统已尝试重新装配并恢复运行。",
                            4.0f,
                            ScreenMessageStyle.UPPER_CENTER
                        )
                    );
                }
                catch { }
#endif
                return true;
            }
            catch (Exception ex)
            {
                MFPLogger.Error(MFPLogger.CatCore, $"[MFPSafetyFallback] Recovery attempt failed: {ex.Message}");
                TriggerFaultFallback("重新恢复航电系统时再次发生异常 (Recovery Failed)", ex);
                return false;
            }
        }

        /// <summary>
        /// 手动降级回原版界面
        /// </summary>
        public static void ForceFallbackToStock(string reason = "用户手动降级至原版界面")
        {
            TriggerFaultFallback(reason, null);
        }
    }
}
