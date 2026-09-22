using System;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// 外部模组遥测数据探针中枢管理器
    /// 负责在场景加载和模组启动时探测并安全挂载外部 Mod (FAR / KER / MechJeb / Principia)
    /// </summary>
    public static class TelemetryProbeManager
    {
        private static bool _initialized = false;

        public static void InitializeAll()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                FarProbe.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] FarProbe init error: {ex.Message}");
            }

            try
            {
                KerbalEngineerProbe.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] KerbalEngineerProbe init error: {ex.Message}");
            }

            try
            {
                MechJebProbe.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] MechJebProbe init error: {ex.Message}");
            }
        }
    }
}
