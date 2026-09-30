using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 3D 姿态球视觉呈现与官方挂钩抽象接口 (Pure Unity Contract)
    /// </summary>
    public interface INavBallVisualHook
    {
        bool HasStockNavBall { get; }
        Mesh StockMesh { get; }
        Vector2 TextureScale { get; }
        Vector2 TextureOffset { get; }
        Quaternion CameraRotation { get; }
        Quaternion BallRotation { get; }
        Quaternion ViewRotation { get; }
        Texture BallTexture { get; }
        string HeadingText { get; }
        string FrameName { get; }
        string ReferenceFrameCategory { get; }
        float HeadingAngle { get; }
        bool GetMarkerDirection(string markerType, out Vector3 dir, out bool isVisible);
    }

    public static class NavBallHookService
    {
        public static INavBallVisualHook Provider { get; set; }
        public static Action<bool> HideStockNavballAction { get; set; }
        public static Action<bool> HideStockAltimeterAction { get; set; }
        public static Action<bool> HideStockBottomLeftAction { get; set; }
        public static Action<bool> HideStockTimeWarpAction { get; set; }
        public static Action<bool> HideStockCommNetAction { get; set; }
        public static Action<bool> HideStockToolbarAction { get; set; }
        public static Action ReskinStockToolbarAction { get; set; }
        public static Action RestoreStockToolbarAction { get; set; }
        public static Action RestoreAllStockUIAction { get; set; }
        public static Action<bool> SetStockNavballCleanAction { get; set; }
        public static Action<RectTransform, float> SyncStockNavballAction { get; set; }
        public static Action ResetStockNavballAction { get; set; }
        public static Func<bool> IsCleanStockNavballActiveFunc { get; set; }
        public delegate bool MarkerDirectionFallbackDelegate(string markerKey, out Vector3 dir, out bool isVisible);
        public static MarkerDirectionFallbackDelegate MarkerDirectionFallback { get; set; }
    }

    /// <summary>
    /// 飞船剪影生成提供者抽象接口 (Pure Unity Contract)
    /// </summary>
    public interface IVesselSilhouetteProvider
    {
        Texture SilhouetteTexture { get; }
        float NormalizedNoseTipY { get; }
        float NormalizedEngineBottomY { get; }
        event Action<Texture> OnSilhouetteUpdated;
        void TriggerBurst(float duration = 3.0f);
    }

    public static class VesselSilhouetteService
    {
        private static IVesselSilhouetteProvider _provider;
        public static IVesselSilhouetteProvider Provider
        {
            get
            {
                if (_provider is UnityEngine.Object obj && obj == null)
                {
                    _provider = null;
                }
                return _provider;
            }
            set => _provider = value;
        }
    }

    /// <summary>
    /// 3D 飞船离屏渲染投影与观察视角模式
    /// </summary>
    public enum Vessel3DViewMode
    {
        /// <summary>经典航空航天轴测 3/4 视角 (俯仰 ~25°, 偏航 ~-35°)</summary>
        Isometric = 0,
        /// <summary>真实三维透视视角 (具备自然纵深与视锥收敛感)</summary>
        Perspective = 1,
        /// <summary>姿态联动视角 (跟随飞船世界/轨道姿态实时偏转)</summary>
        AttitudeSync = 2,
        /// <summary>权威俯视视角 (带三维表面法线与边缘发光)</summary>
        TopDown = 3,
        /// <summary>机尾正视追随视角 (从尾部正视机头，座舱背侧朝上，天然对齐飞行姿态仪)</summary>
        TailChase = 4
    }

    /// <summary>
    /// 低代价 3D 飞船离屏渲染提供者抽象接口 (Pure Unity Contract)
    /// </summary>
    public interface IVessel3DProvider
    {
        Texture Texture3D { get; }
        event Action<Texture> OnTexture3DUpdated;
        void TriggerBurst(float duration = 3.0f);
        void BakeNow();

        Vessel3DViewMode ViewMode { get; set; }
        Vector3 CameraAngles { get; set; }
        bool IsTurntableActive { get; set; }
        float TurntableSpeed { get; set; }
        float CameraFov { get; set; }
        float ZoomMargin { get; set; }
    }

    public static class Vessel3DService
    {
        private static IVessel3DProvider _provider;
        public static IVessel3DProvider Provider
        {
            get
            {
                if (_provider is UnityEngine.Object obj && obj == null)
                {
                    _provider = null;
                }
                return _provider;
            }
            set => _provider = value;
        }
    }

    /// <summary>
    /// 导航球标线类型枚举（消除热循环字符串分配）
    /// </summary>
    public enum NavballMarkerType
    {
        Unknown = 0,
        Prograde,
        Retrograde,
        VelocityVector,
        AntiVelocityVector,
        Normal,
        AntiNormal,
        RadialIn,
        RadialOut,
        Target,
        AntiTarget,
        Maneuver
    }

    /// <summary>
    /// 导航标线类型辅助解析器（解耦 Unity 无头预览与 KSP 运行时）
    /// </summary>
    public static class NavballMarkerHelper
    {
        private static readonly Dictionary<string, NavballMarkerType> _markerKeyToType = new Dictionary<string, NavballMarkerType>(StringComparer.OrdinalIgnoreCase)
        {
            { "prograde", NavballMarkerType.Prograde },
            { "retrograde", NavballMarkerType.Retrograde },
            { "velocity_vector", NavballMarkerType.VelocityVector },
            { "anti_velocity_vector", NavballMarkerType.AntiVelocityVector },
            { "normal", NavballMarkerType.Normal },
            { "antinormal", NavballMarkerType.AntiNormal },
            { "radialin", NavballMarkerType.RadialIn },
            { "radialout", NavballMarkerType.RadialOut },
            { "target", NavballMarkerType.Target },
            { "antitarget", NavballMarkerType.AntiTarget },
            { "maneuver", NavballMarkerType.Maneuver }
        };

        public static NavballMarkerType GetMarkerType(string markerKey)
        {
            if (!string.IsNullOrEmpty(markerKey) && _markerKeyToType.TryGetValue(markerKey, out var type))
            {
                return type;
            }
            return NavballMarkerType.Unknown;
        }
    }
}
