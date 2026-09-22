using System;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Kerbal Engineer Redux (KER) 软依赖遥测反射探针
    /// 零硬编码依赖，当玩家安装 KER 时自动提取专业级分级 Delta-V、TWR 与燃烧时间
    /// </summary>
    public static class KerbalEngineerProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        private static PropertyInfo _lastStageProp;
        private static FieldInfo _deltaVField;
        private static FieldInfo _totalDeltaVField;
        private static FieldInfo _twrField;
        private static FieldInfo _timeField;
        private static FieldInfo _totalTimeField;
        private static FieldInfo _ispField;
        private static FieldInfo _actualThrustField;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Type simManagerType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("KerbalEngineer"))
                    {
                        simManagerType = asm.GetType("KerbalEngineer.VesselSimulator.SimManager");
                        if (simManagerType != null) break;
                    }
                }

                if (simManagerType != null)
                {
                    _lastStageProp = simManagerType.GetProperty("LastStage", BindingFlags.Public | BindingFlags.Static);
                    if (_lastStageProp != null)
                    {
                        Type stageType = _lastStageProp.PropertyType;
                        _deltaVField = stageType.GetField("deltaV", BindingFlags.Public | BindingFlags.Instance);
                        _totalDeltaVField = stageType.GetField("totalDeltaV", BindingFlags.Public | BindingFlags.Instance);
                        _twrField = stageType.GetField("actualThrustToWeight", BindingFlags.Public | BindingFlags.Instance) 
                                   ?? stageType.GetField("thrustToWeight", BindingFlags.Public | BindingFlags.Instance);
                        _timeField = stageType.GetField("time", BindingFlags.Public | BindingFlags.Instance);
                        _totalTimeField = stageType.GetField("totalTime", BindingFlags.Public | BindingFlags.Instance);
                        _ispField = stageType.GetField("isp", BindingFlags.Public | BindingFlags.Instance);
                        _actualThrustField = stageType.GetField("actualThrust", BindingFlags.Public | BindingFlags.Instance);

                        _isAvailable = true;
                        Debug.Log("[ModularFlightPanel] Successfully hooked Kerbal Engineer Redux (KER) SimManager!");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] KER probe initialization notice: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static object GetLastStage()
        {
            if (!_isAvailable || _lastStageProp == null) return null;
            try
            {
                return _lastStageProp.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        public static double StageDeltaV
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _deltaVField == null) return double.NaN;
                return Convert.ToDouble(_deltaVField.GetValue(stage));
            }
        }

        public static double TotalDeltaV
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _totalDeltaVField == null) return double.NaN;
                return Convert.ToDouble(_totalDeltaVField.GetValue(stage));
            }
        }

        public static double StageTWR
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _twrField == null) return double.NaN;
                return Convert.ToDouble(_twrField.GetValue(stage));
            }
        }

        public static double StageBurnTime
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _timeField == null) return double.NaN;
                return Convert.ToDouble(_timeField.GetValue(stage));
            }
        }

        public static double TotalBurnTime
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _totalTimeField == null) return double.NaN;
                return Convert.ToDouble(_totalTimeField.GetValue(stage));
            }
        }

        public static double StageIsp
        {
            get
            {
                object stage = GetLastStage();
                if (stage == null || _ispField == null) return double.NaN;
                return Convert.ToDouble(_ispField.GetValue(stage));
            }
        }
    }
}
