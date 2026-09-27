using System;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.HUD
{
    /// <summary>
    /// 高精度性能探针悬浮徽章控制器 (HUD Profiler Overlay Controller)
    /// 核心职责：
    /// 独立宿主 F10 性能探针 IMGUI 悬浮窗口 (MFPProfiler.DrawGUI)；
    /// 仅在按 F10 开启性能探针时激活 (enabled = true)，平时严格休眠 (enabled = false)，
    /// 杜绝在正常飞行期间产生任何 IMGUI 轮询开销与 GC 垃圾。
    /// </summary>
    public class HUDProfilerOverlay : MonoBehaviour
    {
        private void Awake()
        {
            enabled = false; // 默认严格休眠
        }

        private void OnGUI()
        {
            if (!MFPProfiler.ShowOverlay || MFPProfiler.IsMasterBypassed) return;
            MFPProfiler.DrawGUI();
        }
    }
}
