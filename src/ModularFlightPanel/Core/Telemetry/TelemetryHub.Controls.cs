using System;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    public partial class TelemetryHub
    {
        // -------------------------------------------------------------
        // 飞行控制与操纵量 (Flight Controls & Inputs) - 按需惰性解算
        // -------------------------------------------------------------
        private int _controlsFrame = -1;
        private bool _isRCSEnabled = false;
        private bool _isSASEnabled = false;
        private FlightSASMode _currentSASMode = FlightSASMode.StabilityAssist;
        private float _pitchInput = 0f;
        private float _rollInput = 0f;
        private float _yawInput = 0f;
        private float _pitchTrim = 0f;
        private float _rollTrim = 0f;
        private float _yawTrim = 0f;
        private float _xInput = 0f;
        private float _yInput = 0f;
        private float _zInput = 0f;
        private bool _isStageLocked = false;
        private bool _isPrecisionControl = false;
        private bool _isDockingMode = false;

        public bool IsRCSEnabled
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsRCSEnabled;
                EnsureControlsUpdated();
                return _isRCSEnabled;
            }
            private set => _isRCSEnabled = value;
        }

        public bool IsSASEnabled
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsSASEnabled;
                EnsureControlsUpdated();
                return _isSASEnabled;
            }
            private set => _isSASEnabled = value;
        }

        public FlightSASMode CurrentSASMode
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.CurrentSASMode;
                EnsureControlsUpdated();
                return _currentSASMode;
            }
            private set => _currentSASMode = value;
        }

        public VesselAutopilot.AutopilotMode KspSASMode => ToKspAutopilotMode(CurrentSASMode);

        public float PitchInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.PitchInput;
                EnsureControlsUpdated();
                return _pitchInput;
            }
        }

        public float RollInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.RollInput;
                EnsureControlsUpdated();
                return _rollInput;
            }
        }

        public float YawInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.YawInput;
                EnsureControlsUpdated();
                return _yawInput;
            }
        }

        public float PitchTrim
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.PitchTrim;
                EnsureControlsUpdated();
                return _pitchTrim;
            }
        }

        public float RollTrim
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.RollTrim;
                EnsureControlsUpdated();
                return _rollTrim;
            }
        }

        public float YawTrim
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.YawTrim;
                EnsureControlsUpdated();
                return _yawTrim;
            }
        }

        public float XInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.XInput;
                EnsureControlsUpdated();
                return _xInput;
            }
        }

        public float YInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.YInput;
                EnsureControlsUpdated();
                return _yInput;
            }
        }

        public float ZInput
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.ZInput;
                EnsureControlsUpdated();
                return _zInput;
            }
        }

        public bool IsStageLocked
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsStageLocked;
                EnsureControlsUpdated();
                return _isStageLocked;
            }
            private set => _isStageLocked = value;
        }

        public bool IsPrecisionControl
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsPrecisionControl;
                EnsureControlsUpdated();
                return _isPrecisionControl;
            }
            private set => _isPrecisionControl = value;
        }

        public bool IsDockingMode
        {
            get
            {
                if (IsSimulationMode) return SimulationEngine.IsDockingMode;
                EnsureControlsUpdated();
                return _isDockingMode;
            }
            private set => _isDockingMode = value;
        }

        private void EnsureControlsUpdated()
        {
            if (_controlsFrame == Time.frameCount) return;
            _controlsFrame = Time.frameCount;
            if (!HasVessel) return;
            UpdateFlightControls();
        }

        private void UpdateFlightControls()
        {
            if (ActiveVessel != null)
            {
                if (ActiveVessel.ActionGroups != null)
                {
                    _isRCSEnabled = ActiveVessel.ActionGroups[KSPActionGroup.RCS];
                    _isSASEnabled = ActiveVessel.ActionGroups[KSPActionGroup.SAS];
                }

                if (ActiveVessel.Autopilot != null)
                {
                    _currentSASMode = ToFlightSASMode(ActiveVessel.Autopilot.Mode);
                }

                FlightCtrlState ctrl = ActiveVessel.ctrlState;
                _pitchInput = Mathf.Clamp(ctrl.pitch, -1f, 1f);
                _rollInput = Mathf.Clamp(ctrl.roll, -1f, 1f);
                _yawInput = Mathf.Clamp(ctrl.yaw, -1f, 1f);
                _pitchTrim = Mathf.Clamp(ctrl.pitchTrim, -1f, 1f);
                _rollTrim = Mathf.Clamp(ctrl.rollTrim, -1f, 1f);
                _yawTrim = Mathf.Clamp(ctrl.yawTrim, -1f, 1f);
                _xInput = Mathf.Clamp(ctrl.X, -1f, 1f);
                _yInput = Mathf.Clamp(ctrl.Y, -1f, 1f);
                _zInput = Mathf.Clamp(ctrl.Z, -1f, 1f);
            }

            if (FlightInputHandler.fetch != null)
            {
                _isStageLocked = FlightInputHandler.fetch.stageLock;
                _isPrecisionControl = FlightInputHandler.fetch.precisionMode;
            }

            if (FlightUIModeController.Instance != null)
            {
                _isDockingMode = (FlightUIModeController.Instance.Mode == FlightUIMode.DOCKING);
            }
        }

        public static VesselAutopilot.AutopilotMode ToKspAutopilotMode(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return VesselAutopilot.AutopilotMode.StabilityAssist;
                case FlightSASMode.Prograde: return VesselAutopilot.AutopilotMode.Prograde;
                case FlightSASMode.Retrograde: return VesselAutopilot.AutopilotMode.Retrograde;
                case FlightSASMode.Normal: return VesselAutopilot.AutopilotMode.Normal;
                case FlightSASMode.Antinormal: return VesselAutopilot.AutopilotMode.Antinormal;
                // KSP 原版内核反转：AutopilotMode.RadialIn (5) 内部实际控制飞船指向 +upAxis / -Cross 即径向向外(Radial Out)，
                // 而 AutopilotMode.RadialOut (6) 内部实际控制飞船指向 -upAxis / +Cross 即径向向内(Radial In)
                case FlightSASMode.RadialIn: return VesselAutopilot.AutopilotMode.RadialOut;
                case FlightSASMode.RadialOut: return VesselAutopilot.AutopilotMode.RadialIn;
                case FlightSASMode.Target: return VesselAutopilot.AutopilotMode.Target;
                case FlightSASMode.AntiTarget: return VesselAutopilot.AutopilotMode.AntiTarget;
                case FlightSASMode.Maneuver: return VesselAutopilot.AutopilotMode.Maneuver;
                default: return VesselAutopilot.AutopilotMode.StabilityAssist;
            }
        }

        public static FlightSASMode ToFlightSASMode(VesselAutopilot.AutopilotMode mode)
        {
            switch (mode)
            {
                case VesselAutopilot.AutopilotMode.StabilityAssist: return FlightSASMode.StabilityAssist;
                case VesselAutopilot.AutopilotMode.Prograde: return FlightSASMode.Prograde;
                case VesselAutopilot.AutopilotMode.Retrograde: return FlightSASMode.Retrograde;
                case VesselAutopilot.AutopilotMode.Normal: return FlightSASMode.Normal;
                case VesselAutopilot.AutopilotMode.Antinormal: return FlightSASMode.Antinormal;
                // 对应 KSP 原版反转：当 KSP 处于 AutopilotMode.RadialIn 时，飞船实际指向径向向外
                case VesselAutopilot.AutopilotMode.RadialIn: return FlightSASMode.RadialOut;
                // 当 KSP 处于 AutopilotMode.RadialOut 时，飞船实际指向径向向内
                case VesselAutopilot.AutopilotMode.RadialOut: return FlightSASMode.RadialIn;
                case VesselAutopilot.AutopilotMode.Target: return FlightSASMode.Target;
                case VesselAutopilot.AutopilotMode.AntiTarget: return FlightSASMode.AntiTarget;
                case VesselAutopilot.AutopilotMode.Maneuver: return FlightSASMode.Maneuver;
                default: return FlightSASMode.StabilityAssist;
            }
        }

        // -------------------------------------------------------------
        // 控制指令回调 (Control Actions & Callbacks)
        // -------------------------------------------------------------
        public void SetSASMode(FlightSASMode mode)
        {
            _currentSASMode = mode;
            if (IsSimulationMode)
            {
                SimulationEngine.SetSASMode(mode);
                return;
            }
            if (ActiveVessel != null && ActiveVessel.Autopilot != null)
            {
                var kspMode = ToKspAutopilotMode(mode);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                _isSASEnabled = true;
                ActiveVessel.Autopilot.Enable(kspMode);
                ActiveVessel.Autopilot.SetMode(kspMode);
            }
        }

        public void SetSASMode(VesselAutopilot.AutopilotMode mode)
        {
            SetSASMode(ToFlightSASMode(mode));
        }

        public void ToggleSAS()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleSAS();
                _isSASEnabled = SimulationEngine.IsSASEnabled;
                return;
            }
            if (ActiveVessel != null && ActiveVessel.ActionGroups != null)
            {
                ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.SAS);
                _isSASEnabled = ActiveVessel.ActionGroups[KSPActionGroup.SAS];
            }
        }

        public void ToggleRCS()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleRCS();
                _isRCSEnabled = SimulationEngine.IsRCSEnabled;
                return;
            }
            if (ActiveVessel != null && ActiveVessel.ActionGroups != null)
            {
                ActiveVessel.ActionGroups.ToggleGroup(KSPActionGroup.RCS);
                _isRCSEnabled = ActiveVessel.ActionGroups[KSPActionGroup.RCS];
            }
        }

        public void ActivateNextStage()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ActivateNextStage();
                return;
            }
            if (_isStageLocked) return;
            if (KSP.UI.Screens.StageManager.Instance != null)
            {
                KSP.UI.Screens.StageManager.ActivateNextStage();
                if (ModularFlightPanel.Config.ThemeManager.IsStockBottomLeftHidden)
                {
                    StockUIHider.HideStockBottomLeft(true);
                }
            }
        }

        public void ToggleStageLock()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleStageLock();
                _isStageLocked = SimulationEngine.IsStageLocked;
                return;
            }
            if (FlightInputHandler.fetch != null)
            {
                FlightInputHandler.fetch.stageLock = !FlightInputHandler.fetch.stageLock;
                _isStageLocked = FlightInputHandler.fetch.stageLock;
            }
        }

        public void TogglePrecisionMode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.TogglePrecisionMode();
                _isPrecisionControl = SimulationEngine.IsPrecisionControl;
                return;
            }
            if (FlightInputHandler.fetch != null)
            {
                FlightInputHandler.fetch.precisionMode = !FlightInputHandler.fetch.precisionMode;
                _isPrecisionControl = FlightInputHandler.fetch.precisionMode;
            }
        }

        public void ToggleFlightMode()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.ToggleFlightMode();
                _isDockingMode = SimulationEngine.IsDockingMode;
                return;
            }
            if (FlightUIModeController.Instance != null)
            {
                FlightUIMode current = FlightUIModeController.Instance.Mode;
                FlightUIMode target = (current == FlightUIMode.DOCKING) ? FlightUIMode.STAGING : FlightUIMode.DOCKING;
                FlightUIModeController.Instance.SetMode(target);
                _isDockingMode = (target == FlightUIMode.DOCKING);
            }
        }

        public void IncreaseTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.IncreaseTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(TimeWarp.CurrentRateIndex + 1, false);
            }
        }

        public void DecreaseTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.DecreaseTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(TimeWarp.CurrentRateIndex - 1, false);
            }
        }

        public void CancelTimeWarp()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.CancelTimeWarp();
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(0, false);
            }
        }

        public void TogglePause()
        {
            if (IsSimulationMode)
            {
                SimulationEngine.TogglePause();
                return;
            }
            if (FlightGlobals.ready)
            {
                if (PauseMenu.isOpen) PauseMenu.Close();
                else PauseMenu.Display();
            }
        }

        public void SetTimeWarpRateIndex(int index)
        {
            if (IsSimulationMode)
            {
                SimulationEngine.SetTimeWarpRateIndex(index);
                return;
            }
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(index, false);
            }
        }
    }
}
