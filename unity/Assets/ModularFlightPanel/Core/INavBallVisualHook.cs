using System;
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
        public static IVesselSilhouetteProvider Provider { get; set; }
    }
}
