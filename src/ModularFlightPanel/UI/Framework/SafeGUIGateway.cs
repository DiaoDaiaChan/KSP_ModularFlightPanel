using System;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Diagnostics;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 全局 IMGUI 安全网关与防漏锁沙箱 (Safe IMGUI Gateway & Input Lock Guard)
    /// 核心设计目标：
    /// 1. 统一拦截所有 IMGUI 根入口 (OnGUI, GUILayout.Window, Floating Toolbar) 的未捕获异常；
    /// 2. 发生异常时，底层无条件安全释放 MFP 占用的全部键盘与鼠标控制锁，绝不造成玩家操控失灵；
    /// 3. 限流日志记录，防止每帧重复打印 60 次导致游戏卡顿；
    /// 4. 异常自动隔离展示：当窗口内部发生渲染异常时，在窗口内呈现优雅的自愈错误卡片，而不是打垮 Unity GUI 引擎。
    /// </summary>
    public static class SafeGUIGateway
    {
        private static long _lastLogTick = 0;
        private static string _lastErrorMessage = null;

        /// <summary>
        /// 执行顶层 OnGUI 根沙箱
        /// </summary>
        public static void ExecuteRoot(Action onGuiAction, string contextName = "RootGUI")
        {
            if (onGuiAction == null) return;

            try
            {
                onGuiAction();
            }
            catch (Exception ex)
            {
                HandleGuiException(contextName, ex);
                MFPInputLock.ReleaseAllLocks();
            }
        }

        /// <summary>
        /// 执行带有窗口状态保护的 Window 沙箱
        /// </summary>
        public static void ExecuteWindowContent(int windowId, Action contentAction, Action onRecover = null, string windowName = "Window")
        {
            if (contentAction == null) return;

            try
            {
                contentAction();
            }
            catch (Exception ex)
            {
                HandleGuiException(windowName, ex);

                // 呈现窗口内自愈卡片，保障窗口仍然可操作、可关闭、可重置
                GUILayout.BeginVertical();
                MFPGuiSkin.BeginCard();
                GUILayout.Label($"<color=#FF5555><b>{I18n.Tr("ERR_GUI_RENDER_ERROR", "⚠️ 界面绘制遇到异常 (GUI Render Error)")}</b></color>");
                GUILayout.Label($"<color=#CCCCCC><size=11>{ex.GetType().Name}: {ex.Message}</size></color>");
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (onRecover != null && GUILayout.Button(I18n.Tr("ERR_BTN_RESET_GUI", "🔄 尝试重置界面"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    try { onRecover(); } catch { }
                }
                if (GUILayout.Button(I18n.Tr("ERR_BTN_RELEASE_LOCKS", "✖ 释放控制锁"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    MFPInputLock.ReleaseAllLocks();
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndCard();
                GUILayout.EndVertical();
            }
        }

        private static void HandleGuiException(string contextName, Exception ex)
        {
            long now = DateTime.UtcNow.Ticks;
            // 节流：同类错误 2 秒内仅记录一次，彻底防止 60FPS 刷屏卡死
            if (now - _lastLogTick > TimeSpan.FromSeconds(2).Ticks || _lastErrorMessage != ex.Message)
            {
                _lastLogTick = now;
                _lastErrorMessage = ex.Message;
                MFPLogger.Error(MFPLogger.CatUI, $"[SafeGUIGateway] IMGUI 异常捕获 ({contextName}): {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
