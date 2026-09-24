using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 标准化单根天线工况与遥测快照 (CommNet / RealAntennas Antenna Telemetry Snapshot)
    /// </summary>
    public struct AntennaTelemetryInfo
    {
        public string Name;
        public string TypeStr; // "DIRECT", "RELAY", "INTERNAL"
        public double Power;
        public string PowerFormatted; // "5.0k", "2.0M", "100G"
        public float SignalStrength; // 0.0f .. 1.0f
        public string Status; // "LINKED", "SEARCHING", "RETRACTED", "OFFLINE"
        public bool IsOperational;

        public AntennaTelemetryInfo(string name, string typeStr, double power, string powerFormatted, float signalStrength, string status, bool isOperational)
        {
            Name = name ?? "ANTENNA";
            TypeStr = typeStr ?? "DIRECT";
            Power = power;
            PowerFormatted = powerFormatted ?? "---";
            SignalStrength = signalStrength;
            Status = status ?? "OFFLINE";
            IsOperational = isOperational;
        }

        public string SpecSummary => $"{TypeStr}  ·  {PowerFormatted} POWER";
    }
}
