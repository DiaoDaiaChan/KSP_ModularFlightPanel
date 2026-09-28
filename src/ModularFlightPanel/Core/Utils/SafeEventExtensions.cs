using System;
using UnityEngine;
using ModularFlightPanel.Core.Diagnostics;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 全局委托与事件安全派发与死代理自动净化基础设施 (Safe Event Dispatch & Dead Delegate Pruner)
    /// 核心设计目标：
    /// 1. 彻底解决 Unity MonoBehaviour 销毁时，原生 C++ 指针变 null 但 C# 多播委托仍强引用的顽疾；
    /// 2. 替代全工程业务层中散乱的手写 GetInvocationList() 循环与防御性 try-catch；
    /// 3. 单个监听器内部异常被完全沙箱隔离，绝不打断多播委托链或反向炸飞事件发布者；
    /// 4. 零 GC 快速通道：当事件为 null 时直接内联返回，零额外堆内存分配。
    /// </summary>
    public static class SafeEventExtensions
    {
        public static void SafeInvoke(this Action action, string eventName = null)
        {
            if (action == null) return;

            var invocationList = action.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                var d = (Action)invocationList[i];
                try
                {
                    if (d.Target is UnityEngine.Object u && u == null)
                    {
                        continue; // 自动跳过原生已被销毁的 Unity 死代理
                    }
                    d();
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn(MFPLogger.CatCore, $"[SafeEvent] 监听器执行异常 ({eventName ?? d.Method.Name}): {ex.Message}");
                }
            }
        }

        public static void SafeInvoke<T>(this Action<T> action, T arg, string eventName = null)
        {
            if (action == null) return;

            var invocationList = action.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                var d = (Action<T>)invocationList[i];
                try
                {
                    if (d.Target is UnityEngine.Object u && u == null)
                    {
                        continue; // 自动跳过原生已被销毁的 Unity 死代理
                    }
                    d(arg);
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn(MFPLogger.CatCore, $"[SafeEvent] 监听器执行异常 ({eventName ?? d.Method.Name}): {ex.Message}");
                }
            }
        }

        public static void SafeInvoke<T1, T2>(this Action<T1, T2> action, T1 arg1, T2 arg2, string eventName = null)
        {
            if (action == null) return;

            var invocationList = action.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                var d = (Action<T1, T2>)invocationList[i];
                try
                {
                    if (d.Target is UnityEngine.Object u && u == null)
                    {
                        continue;
                    }
                    d(arg1, arg2);
                }
                catch (Exception ex)
                {
                    MFPLogger.Warn(MFPLogger.CatCore, $"[SafeEvent] 监听器执行异常 ({eventName ?? d.Method.Name}): {ex.Message}");
                }
            }
        }
    }
}
