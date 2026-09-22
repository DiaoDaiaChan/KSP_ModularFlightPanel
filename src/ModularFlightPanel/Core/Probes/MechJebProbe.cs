using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// MechJeb 2 (MJ) 软依赖遥测反射探针
    /// 零硬编码依赖，当玩家安装 MechJeb 时自动对接获取其飞行状态机与 Delta-V 计算结果
    /// </summary>
    public static class MechJebProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        private static Type _mechJebCoreType;
        private static FieldInfo _vesselStateField;
        private static FieldInfo _deltaVStageField;
        private static FieldInfo _deltaVTotalField;
        private static FieldInfo _termVelField;
        private static FieldInfo _thrustCurrentField;
        private static FieldInfo _twrField;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("MechJeb2"))
                    {
                        _mechJebCoreType = asm.GetType("MuMech.MechJebCore");
                        Type vsType = asm.GetType("MuMech.VesselState");

                        if (_mechJebCoreType != null && vsType != null)
                        {
                            _vesselStateField = _mechJebCoreType.GetField("vesselState", BindingFlags.Public | BindingFlags.Instance);
                            _deltaVStageField = vsType.GetField("deltaVStage", BindingFlags.Public | BindingFlags.Instance);
                            _deltaVTotalField = vsType.GetField("deltaVTotal", BindingFlags.Public | BindingFlags.Instance);
                            _termVelField = vsType.GetField("terminalVelocity", BindingFlags.Public | BindingFlags.Instance);
                            _thrustCurrentField = vsType.GetField("thrustCurrent", BindingFlags.Public | BindingFlags.Instance);
                            _twrField = vsType.GetField("twr", BindingFlags.Public | BindingFlags.Instance);

                            _isAvailable = true;
                            Debug.Log("[ModularFlightPanel] Successfully hooked MechJeb 2 Core!");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] MechJeb probe initialization notice: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static object GetVesselState()
        {
            if (!_isAvailable || FlightGlobals.ActiveVessel == null) return null;

            try
            {
                var core = FlightGlobals.ActiveVessel.FindPartModuleImplementing<PartModule>();
                // 遍历 vessel 上的 PartModules 寻找继承自 MechJebCore 的组件
                var modules = FlightGlobals.ActiveVessel.FindPartModulesImplementing<PartModule>();
                for (int i = 0; i < modules.Count; i++)
                {
                    if (_mechJebCoreType.IsAssignableFrom(modules[i].GetType()))
                    {
                        return _vesselStateField?.GetValue(modules[i]);
                    }
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        public static double StageDeltaV
        {
            get
            {
                object vs = GetVesselState();
                if (vs == null || _deltaVStageField == null) return double.NaN;
                return Convert.ToDouble(_deltaVStageField.GetValue(vs));
            }
        }

        public static double TotalDeltaV
        {
            get
            {
                object vs = GetVesselState();
                if (vs == null || _deltaVTotalField == null) return double.NaN;
                return Convert.ToDouble(_deltaVTotalField.GetValue(vs));
            }
        }

        public static double TerminalVelocity
        {
            get
            {
                object vs = GetVesselState();
                if (vs == null || _termVelField == null) return double.NaN;
                return Convert.ToDouble(_termVelField.GetValue(vs));
            }
        }

        public static double CurrentThrust
        {
            get
            {
                object vs = GetVesselState();
                if (vs == null || _thrustCurrentField == null) return double.NaN;
                return Convert.ToDouble(_thrustCurrentField.GetValue(vs));
            }
        }

        public static double CurrentTWR
        {
            get
            {
                object vs = GetVesselState();
                if (vs == null || _twrField == null) return double.NaN;
                return Convert.ToDouble(_twrField.GetValue(vs));
            }
        }
    }
}
