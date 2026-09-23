using System;
using UnityEngine;

namespace ModularFlightPanel.Config
{
    [Serializable]
    public class LayoutConfig
    {
        public float GlobalScale = 1.0f;
        public float SphereRadius = 130.0f;

        // 各模块可见性
        public bool ShowThrottleArc = true;
        public bool ShowVSIArc = true;
        public bool ShowPropellantArc = true;
        public bool ShowSpeedBox = true;
        public bool ShowAltitudeBox = true;
        public bool ShowOrbitalInfo = true;
        public bool ShowSASDial = true;
        public bool ShowCompassHeading = true;
        public bool ShowBottomControls = true;

        // 模块自定义偏移 (相对于 Navball 中心，单位像素基准)
        public float SASDialOffsetX = 220f;
        public float SASDialOffsetY = -20f;

        public float OrbitalInfoOffsetX = 0f;
        public float OrbitalInfoOffsetY = -165f;

        public float SpeedBoxOffsetX = -135f;
        public float SpeedBoxOffsetY = -10f;

        public float AltBoxOffsetX = 135f;
        public float AltBoxOffsetY = -10f;

        public float HeadingOffsetX = 0f;
        public float HeadingOffsetY = 145f;
    }
}
