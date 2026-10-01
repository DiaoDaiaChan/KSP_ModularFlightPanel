using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;
using AnnunciatorState = ModularFlightPanel.UI.Framework.AnnunciatorState;

namespace ModularFlightPanel.Core.Avionics
{
    /// <summary>
    /// 复合自由面板子控件图层不可变状态快照 (0 GC 纯值结构体，SPEC-012)
    /// </summary>
    public struct CompositeElementState
    {
        public bool HasValue;
        public double NumericValue;
        public float NormalizedFraction;
        public bool IsActive;
        public CardStyleRole CardRole;
        public TextStyleRole TextRole;
        public AnnunciatorState LampState;
        public string FormattedText;
        public string UnitLabel;
    }

    /// <summary>
    /// 复合自由面板整盘业务状态快照 (0 GC 纯值结构体，SPEC-012)
    /// </summary>
    public struct CompositePanelState : IEquatable<CompositePanelState>
    {
        public bool HasVessel;
        public int ElementCount;
        public int Version;

        public bool Equals(CompositePanelState other)
        {
            return HasVessel == other.HasVessel &&
                   ElementCount == other.ElementCount &&
                   Version == other.Version;
        }

        public override bool Equals(object obj) => obj is CompositePanelState other && Equals(other);
        public override int GetHashCode() => (HasVessel, ElementCount, Version).GetHashCode();
    }

    /// <summary>
    /// 复合自由航电面板业务解耦大脑 (Composite Freeform Avionics Panel Logic)
    /// 核心职责：
    /// 1. 脱离 UnityEngine 视图渲染，纯 C# 数据心跳 (OnDataHeartBeat, 10Hz) 解算全部动态图层；
    /// 2. 批量查表与解算 736+ 参数与单位量纲换算 (AvionicsUnitSystem)；
    /// 3. 0 GC 扁平化纯值结构体数组就地复用更新，绝不产生高频堆垃圾 (SPEC-010 / SPEC-012)。
    /// </summary>
    public class CompositePanelLogic : WidgetLogic<CompositePanelState>
    {
        public CompositePanelConfig Config { get; set; }

        private CompositeElementState[] _elementStates = new CompositeElementState[64];
        private int _version = 0;

        public CompositeElementState GetElementState(int index)
        {
            if (index >= 0 && index < _elementStates.Length)
            {
                return _elementStates[index];
            }
            return default;
        }

        public override void Reset()
        {
            CurrentState = default;
            _version = 0;
            for (int i = 0; i < _elementStates.Length; i++)
            {
                _elementStates[i] = default;
            }
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (Config == null || telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    Reset();
                }
                return;
            }

            var elements = Config.Elements;
            int count = elements != null ? elements.Count : 0;

            if (_elementStates.Length < count)
            {
                _elementStates = new CompositeElementState[Math.Max(count, _elementStates.Length * 2)];
            }

            for (int i = 0; i < count; i++)
            {
                var elem = elements[i];
                if (elem == null || !elem.IsVisible)
                {
                    _elementStates[i] = default;
                    continue;
                }

                _elementStates[i] = EvaluateElement(elem, telemetry);
            }

            _version++;
            CurrentState = new CompositePanelState
            {
                HasVessel = true,
                ElementCount = count,
                Version = _version
            };
        }

        private CompositeElementState EvaluateElement(CompositeElementConfig elem, IFlightTelemetry telemetry)
        {
            string token = !string.IsNullOrEmpty(elem.Token) ? elem.Token : "{SPD}";
            double val = BaseFlightWidget.EvalNumeric(token, telemetry, double.NaN);
            string rawStr = BaseFlightWidget.EvalToken(token, telemetry, "---");

            // 1. 量纲转换与自适应格式化 (AvionicsUnitSystem)
            string formattedText = rawStr;
            string unitLabel = elem.Unit ?? "";

            if (!double.IsNaN(val) && !string.IsNullOrEmpty(elem.UnitDimension))
            {
                UnitDimension dim = UnitDimension.None;
                string uDim = elem.UnitDimension.ToUpperInvariant();
                if (uDim == "SPEED" || uDim == "VELOCITY") dim = UnitDimension.Velocity;
                else if (uDim == "ALT" || uDim == "ALTITUDE" || uDim == "LENGTH" || uDim == "DIST") dim = UnitDimension.Length;
                else if (uDim == "PRESSURE" || uDim == "Q") dim = UnitDimension.Pressure;
                else if (uDim == "TEMPERATURE" || uDim == "TEMP") dim = UnitDimension.Temperature;
                else if (uDim == "MASS") dim = UnitDimension.Mass;
                else if (uDim == "ACCEL" || uDim == "ACCELERATION" || uDim == "G") dim = UnitDimension.Acceleration;
                else if (Enum.TryParse<UnitDimension>(elem.UnitDimension, true, out var parsedDim)) dim = parsedDim;

                if (dim != UnitDimension.None)
                {
                    double converted = AvionicsUnitSystem.Convert(val, dim, AvionicsUnitSystem.GlobalMode, out string dimSymbol);
                    formattedText = AvionicsUnitSystem.FormatAdaptive(val, dim, AvionicsUnitSystem.GlobalMode);
                    if (string.IsNullOrEmpty(unitLabel)) unitLabel = dimSymbol;
                }
            }

            // 2. 归一化比例 (0.0 ~ 1.0)
            float fraction = 0f;
            if (!double.IsNaN(val) && elem.MaxValue > elem.MinValue)
            {
                fraction = BaseFlightWidget.NormalizeValue(val, (float)elem.MinValue, (float)elem.MaxValue);
            }

            // 3. 告警阈值与角色求值
            CardStyleRole cardRole = CardStyleRole.Normal;
            TextStyleRole textRole = TextStyleRole.PrimaryValue;
            AnnunciatorState lampState = AnnunciatorState.Off;

            if (!double.IsNaN(val))
            {
                if (elem.WarningThreshold > 0 && val >= elem.WarningThreshold)
                {
                    cardRole = CardStyleRole.Danger;
                    textRole = TextStyleRole.Danger;
                    lampState = AnnunciatorState.Warning;
                }
                else if (elem.CautionThreshold > 0 && val >= elem.CautionThreshold)
                {
                    cardRole = CardStyleRole.Warning;
                    textRole = TextStyleRole.Warning;
                    lampState = AnnunciatorState.Caution;
                }
                else
                {
                    lampState = AnnunciatorState.Normal;
                }
            }

            // 4. 动作与激活状态求值
            bool isActive = false;
            string action = elem.ActionType ?? "";
            if (!string.IsNullOrEmpty(action))
            {
                string uAct = action.ToUpperInvariant();
                if (uAct == "RCS") isActive = telemetry.IsRCSEnabled;
                else if (uAct == "SAS") isActive = telemetry.IsSASEnabled;
                else if (uAct == "STAGE_LOCK") isActive = telemetry.IsStageLocked;
                else if (uAct == "PRECISION") isActive = telemetry.IsPrecisionControl;
                else if (uAct == "DOCKING") isActive = telemetry.IsDockingMode;
            }
            else if (!string.IsNullOrEmpty(token))
            {
                string uTok = token.ToUpperInvariant();
                if (uTok.Contains("GEAR"))
                {
                    isActive = val > 0.5;
                    lampState = isActive ? AnnunciatorState.Normal : AnnunciatorState.Off;
                }
                else if (uTok.Contains("BRAKE"))
                {
                    isActive = val > 0.5;
                    lampState = isActive ? AnnunciatorState.Warning : AnnunciatorState.Off;
                }
                else if (uTok.Contains("LIGHT"))
                {
                    isActive = val > 0.5;
                    lampState = isActive ? AnnunciatorState.Normal : AnnunciatorState.Off;
                }
            }

            return new CompositeElementState
            {
                HasValue = !double.IsNaN(val) || (rawStr != "---" && !string.IsNullOrEmpty(rawStr)),
                NumericValue = val,
                NormalizedFraction = fraction,
                IsActive = isActive,
                CardRole = cardRole,
                TextRole = textRole,
                LampState = lampState,
                FormattedText = formattedText,
                UnitLabel = unitLabel
            };
        }
    }
}
