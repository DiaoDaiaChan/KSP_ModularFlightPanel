using System;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 轻量全局 Toast 提示桥接器。
    /// 允许纯 UGUI 交互组件（如历史记录、对齐辅助线、网格等）与游戏内 IMGUI 提示层解耦通信，
    /// 避免在无头 Unity 预览工程中产生对 Settings/* 的硬依赖。
    /// </summary>
    public static class MFPToastBridge
    {
        public static Action<string> OnShowToast;

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            OnShowToast?.Invoke(message);
        }
    }
}
