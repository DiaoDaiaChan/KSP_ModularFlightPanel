using System;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 标准化通信网络对端链路遥测快照 (CommNet / RealAntennas Active Link Info)
    /// </summary>
    public struct CommLinkInfo
    {
        public string PeerName;
        public double DataRateBps;
        public float SignalStrength; // 0.0f .. 1.0f
        public bool IsDirectHome;

        public CommLinkInfo(string peerName, double dataRateBps, float signalStrength, bool isDirectHome = false)
        {
            PeerName = peerName;
            DataRateBps = dataRateBps;
            SignalStrength = signalStrength;
            IsDirectHome = isDirectHome;
        }

        public string FormattedDataRate => FormatRate(DataRateBps);

        public static string FormatRate(double bps)
        {
            if (bps <= 0.0) return "0.0 bps";
            if (bps >= 1000000.0)
                return $"{(bps / 1000000.0):F1} Mbps";
            if (bps >= 1000.0)
                return $"{(bps / 1000.0):F1} Kbps";
            return $"{bps:F0} bps";
        }
    }
}
