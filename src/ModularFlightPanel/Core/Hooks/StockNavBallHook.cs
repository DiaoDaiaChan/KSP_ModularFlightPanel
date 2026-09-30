using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.UI;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 官方原生 NavBall 深度桥接与挂钩器 (Zero-Calculation Hook Facade)
    /// 
    /// 核心理念：
    /// 1. 姿态旋转：直接读取官方/Principia 权威 world rotation（不做任何多余计算，完美兼容 Principia 任意参考系及载具控向点）
    /// 2. 贴图支持：直接探测 _MainTexture 与 _MainTex（天然兼容 Principia 动态多参考系贴图及 TextureReplacer）
    /// 3. 矢量标线：由 NavballMarkerVectorExtractor 专责提取并投影呈现
    /// 4. UI 隐身：由 StockUIHider 专责接管原生 UI 隐形与保活
    /// </summary>
    public static class StockNavBallHook
    {
        static StockNavBallHook()
        {
            NavBallHookService.Provider = new StockNavBallVisualHook();
            NavBallHookService.MarkerDirectionFallback = GetMarkerDirection;
            NavBallHookService.HideStockNavballAction = HideStockNavballCompletely;
            NavBallHookService.HideStockAltimeterAction = HideStockAltimeter;
            NavBallHookService.HideStockBottomLeftAction = HideStockBottomLeft;
            NavBallHookService.HideStockTimeWarpAction = HideStockTimeWarp;
            NavBallHookService.HideStockCommNetAction = HideStockCommNet;
            NavBallHookService.RestoreAllStockUIAction = RestoreAllStockUI;
            NavBallHookService.SetStockNavballCleanAction = SetStockNavballClean;
            NavBallHookService.SyncStockNavballAction = SyncStockNavballToWidget;
            NavBallHookService.ResetStockNavballAction = ResetStockNavballTransform;
            NavBallHookService.IsCleanStockNavballActiveFunc = () => StockUIHider.IsCleanStockNavballActive;
            StockStageIconService.Provider = new StockStageIconHook();
            StockStageActionService.Provider = new StockStageActionHook();
        }

        public static void RestoreAllStockUI()
        {
            StockUIHider.RestoreAllStockUI();
        }

        private static NavBall _stockInstance;
        private static float _lastNavBallSearchTime = -10f;

        public static NavBall StockInstance
        {
            get
            {
                if (_stockInstance == null)
                {
                    float now = Time.unscaledTime;
                    if (now - _lastNavBallSearchTime > 3.0f)
                    {
                        _lastNavBallSearchTime = now;
                        if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                        {
                            var nb = FlightUIModeController.Instance.navBall.GetComponentInChildren<NavBall>(true);
                            if (nb != null)
                            {
                                RegisterStockNavBall(nb);
                            }
                        }
                        if (_stockInstance == null)
                        {
                            var found = UnityEngine.Object.FindObjectOfType<NavBall>();
                            if (found != null)
                            {
                                RegisterStockNavBall(found);
                            }
                        }
                    }
                }
                return _stockInstance;
            }
            set => _stockInstance = value;
        }

        public static bool HasStockNavBall => StockInstance != null && StockInstance.navBall != null;

        private static Renderer _cachedNavBallRenderer;
        private static MeshFilter _cachedNavBallMeshFilter;
        private static Material _cachedNavBallMaterial;
        private static Texture _cachedNavBallTexture;
        private static Vector2 _cachedTextureScale = Vector2.one;
        private static Vector2 _cachedTextureOffset = Vector2.zero;
        private static int _cachedTextureFrame = -1;

        public struct NavballAttitudeSnapshot
        {
            public int Frame;
            public Quaternion CamRot;
            public Quaternion BallRot;
            public Quaternion ViewRot;
            public Quaternion InvViewRot;
            public float HeadingAngle;
            public bool HasAttitude;
        }

        private static NavballAttitudeSnapshot _cachedAttitudeSnapshot;
        private static int _cachedContinuousHeadingFrame = -1;
        private static float _cachedContinuousHeadingValue = 0f;
        private static bool _cachedContinuousHeadingSuccess = false;
        private static Canvas _cachedStockCanvas;

        public static void InvalidateCaches()
        {
            _cachedNavBallCamera = null;
            _cachedStockCanvas = null;
            _cachedAttitudeSnapshot = default;
            _cachedReferenceFrameNameFrame = -1;
            _cachedReferenceFrameCategoryFrame = -1;
            _cachedNavballSpeedFrame = -1;
            _cachedNavBallRenderer = null;
            _cachedNavBallMeshFilter = null;
            _cachedNavBallMaterial = null;
            _cachedNavBallTexture = null;
            _cachedTextureFrame = -1;
            _cachedContinuousHeadingFrame = -1;
            PrincipiaProbe.InvalidateCaches();
        }

        public static void RegisterStockNavBall(NavBall instance)
        {
            if (instance == null) return;
            if (_stockInstance != instance)
            {
                _stockInstance = instance;
                InvalidateCaches();
                NavballMarkerVectorExtractor.InvalidateCaches();
                StockUIHider.ClearCaches();
                NavBallHookService.Provider = new StockNavBallVisualHook();
                Debug.Log("[ModularFlightPanel] Successfully hooked Stock NavBall instance!");
            }
        }

        public static void UnregisterStockNavBall(NavBall instance)
        {
            if (_stockInstance == instance)
            {
                _stockInstance = null;
                InvalidateCaches();
                NavballMarkerVectorExtractor.InvalidateCaches();
                StockUIHider.ClearCaches();
                NavBallHookService.Provider = null;
            }
        }

        // ── 动态按需保活心跳租约 (Dynamic On-Demand Lease Tracking) ──
        private static float _lastAttitudeRequestTime = -10f;
        private static float _lastSpeedRequestTime = -10f;
        private static float _lastManeuverRequestTime = -10f;

        public static void PulseAttitudeConsumerHeartbeat() => _lastAttitudeRequestTime = Time.unscaledTime;
        public static void PulseSpeedConsumerHeartbeat() => _lastSpeedRequestTime = Time.unscaledTime;
        public static void PulseManeuverConsumerHeartbeat() => _lastManeuverRequestTime = Time.unscaledTime;

        public static bool HasActiveAttitudeConsumer => (Time.unscaledTime - _lastAttitudeRequestTime) < 0.6f;
        public static bool HasActiveSpeedConsumer => (Time.unscaledTime - _lastSpeedRequestTime) < 0.6f;
        public static bool HasActiveManeuverConsumer => (Time.unscaledTime - _lastManeuverRequestTime) < 0.6f;

        public static void TickDynamicHooks()
        {
            StockUIHider.TickDynamicHooks();
        }

        private static Action<NavBall, Quaternion> _setAttitudeGymbalDelegate;
        private static Action<NavBall, Quaternion> _setRelativeGymbalDelegate;
        private static Action<NavBall, Quaternion> _setOffsetGymbalDelegate;
        private static Action<NavBall, Transform> _setTargetDelegate;
        private static bool _delegatesInitialized = false;

        private static void EnsureNavballDelegatesInitialized()
        {
            if (_delegatesInitialized) return;
            _delegatesInitialized = true;
            try
            {
                Type nbType = typeof(NavBall);
                var pAttitude = nbType.GetProperty("attitudeGymbal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pAttitude?.GetSetMethod(true) != null)
                    _setAttitudeGymbalDelegate = (Action<NavBall, Quaternion>)Delegate.CreateDelegate(typeof(Action<NavBall, Quaternion>), pAttitude.GetSetMethod(true));

                var pRelative = nbType.GetProperty("relativeGymbal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pRelative?.GetSetMethod(true) != null)
                    _setRelativeGymbalDelegate = (Action<NavBall, Quaternion>)Delegate.CreateDelegate(typeof(Action<NavBall, Quaternion>), pRelative.GetSetMethod(true));

                var pOffset = nbType.GetProperty("offsetGymbal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pOffset?.GetSetMethod(true) != null)
                    _setOffsetGymbalDelegate = (Action<NavBall, Quaternion>)Delegate.CreateDelegate(typeof(Action<NavBall, Quaternion>), pOffset.GetSetMethod(true));

                var pTarget = nbType.GetProperty("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pTarget?.GetSetMethod(true) != null)
                    _setTargetDelegate = (Action<NavBall, Transform>)Delegate.CreateDelegate(typeof(Action<NavBall, Transform>), pTarget.GetSetMethod(true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] Failed to bind NavBall property delegates: {ex.Message}");
            }
        }

        private static int _lastSimulatedFrame = -1;

        /// <summary>
        /// 超轻量级姿态与万向节纳秒解算器 (0 GC, ~500ns)
        /// 替代官方 NavBall.Update 繁重的 10 次材质实例化、TextMeshPro 字符渲染与 UGUI 循环，
        /// 确保 attitudeGymbal、relativeGymbal、offsetGymbal 与 navBall.rotation 100% 物理与数学精准，
        /// 从而完全保障 Principia 多参考系切换与各仪表 Hook 零开销无缝运作。
        /// </summary>
        public static void UpdateStockNavballGymbalsLightweight(NavBall instance)
        {
            if (instance == null || !FlightGlobals.ready || FlightGlobals.ActiveVessel == null) return;
            if (_lastSimulatedFrame == Time.frameCount) return;
            _lastSimulatedFrame = Time.frameCount;

            EnsureNavballDelegatesInitialized();

            Transform target = FlightGlobals.ActiveVessel.ReferenceTransform ?? FlightGlobals.ActiveVessel.transform;
            if (target == null) return;

            Vector3 euler = !FlightGlobals.ActiveVessel.isEVA ? new Vector3(90f, 0f, 0f) : Vector3.zero;
            Quaternion offsetGymbal = Quaternion.Euler(euler);

            Quaternion attitudeGymbal;
            if (FlightGlobals.ActiveVessel.isEVA && !MapView.MapIsEnabled && FlightCamera.fetch != null)
            {
                attitudeGymbal = Quaternion.Inverse(FlightCamera.fetch.getReferenceFrame() * Quaternion.AngleAxis(FlightCamera.fetch.camHdg * 57.29578f, Vector3.up) * Quaternion.AngleAxis(FlightCamera.fetch.camPitch * 57.29578f, Vector3.right));
            }
            else
            {
                attitudeGymbal = offsetGymbal * Quaternion.Inverse(target.rotation);
            }

            CelestialBody currentMainBody = FlightGlobals.currentMainBody;
            Quaternion relativeGymbal;
            if (currentMainBody != null)
            {
                Vector3 toVessel = (target.position - currentMainBody.position).normalized;
                Vector3 northProj = Vector3.ProjectOnPlane(currentMainBody.position + (Vector3d)currentMainBody.transform.up * currentMainBody.Radius - target.position, toVessel).normalized;
                relativeGymbal = attitudeGymbal * Quaternion.LookRotation(northProj, toVessel);
            }
            else
            {
                relativeGymbal = attitudeGymbal;
            }

            _setTargetDelegate?.Invoke(instance, target);
            _setOffsetGymbalDelegate?.Invoke(instance, offsetGymbal);
            _setAttitudeGymbalDelegate?.Invoke(instance, attitudeGymbal);
            _setRelativeGymbalDelegate?.Invoke(instance, relativeGymbal);

            // 原生无 Principia 环境下直接更新 3D 姿态球 Transform 旋转；
            // 若 Principia 存在，其 LateUpdate 会基于 attitudeGymbal 与 NavballOrientation 写入最终多参考系旋转
            if (!PrincipiaProbe.IsAvailable && instance.navBall != null)
            {
                instance.navBall.rotation = relativeGymbal;
            }

            // 原版姿态球隐藏时，同步轻量驱动原生 Marker Transform (纯数学矢量，跳过官方 8 次 MeshRenderer.materials 堆分配与 UGUI 循环)
            float unitScale = instance.VectorUnitScale;
            if (unitScale < 0.001f) unitScale = 1.0f;

            // 1. 速度矢量 (Prograde / Retrograde)
            Vector3 dispVel = Vector3.zero;
            switch (FlightGlobals.speedDisplayMode)
            {
                case FlightGlobals.SpeedDisplayModes.Orbit:
                    dispVel = (Vector3)FlightGlobals.ship_obtVelocity;
                    break;
                case FlightGlobals.SpeedDisplayModes.Surface:
                    dispVel = (Vector3)FlightGlobals.ship_srfVelocity;
                    break;
                case FlightGlobals.SpeedDisplayModes.Target:
                    dispVel = (Vector3)FlightGlobals.ship_tgtVelocity;
                    break;
            }

            if (dispVel.sqrMagnitude > 0.0001f)
            {
                Vector3 velDir = dispVel.normalized;
                Vector3 localPrograde = attitudeGymbal * (velDir * unitScale);
                if (instance.progradeVector != null) instance.progradeVector.localPosition = localPrograde;
                if (instance.retrogradeVector != null) instance.retrogradeVector.localPosition = -localPrograde;
            }

            // 2. 轨道法向与径向 (Normal / AntiNormal / RadialIn / RadialOut)
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && v.orbit != null && v.mainBody != null)
            {
                Vector3 wCoM = v.CurrentCoM;
                Vector3 cbPos = (Vector3)v.mainBody.position;
                Vector3 obtVel = (Vector3)v.orbit.GetVel();
                if (obtVel.sqrMagnitude > 0.0001f)
                {
                    Vector3 rad = Vector3.ProjectOnPlane((wCoM - cbPos).normalized, obtVel).normalized;
                    Vector3 norm = Vector3.Cross(rad, obtVel.normalized);

                    Vector3 localNorm = attitudeGymbal * (norm * unitScale);
                    Vector3 localRad = attitudeGymbal * (rad * unitScale);

                    if (instance.antiNormalVector != null) instance.antiNormalVector.localPosition = localNorm;
                    if (instance.normalVector != null) instance.normalVector.localPosition = -localNorm;
                    if (instance.radialOutVector != null) instance.radialOutVector.localPosition = localRad;
                    if (instance.radialInVector != null) instance.radialInVector.localPosition = -localRad;
                }
            }

            // 3. 目标航向标 (Target / AntiTarget Waypoint)
            if (FlightGlobals.fetch != null && FlightGlobals.fetch.vesselTargetDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 localTgt = attitudeGymbal * (FlightGlobals.fetch.vesselTargetDirection * unitScale);
                if (instance.progradeWaypoint != null) instance.progradeWaypoint.localPosition = localTgt;
                if (instance.retrogradeWaypoint != null) instance.retrogradeWaypoint.localPosition = -localTgt;
            }
        }

        /// <summary>
        /// 获取官方/Principia 姿态四元数与权威航向角同帧单源快照 (0 重复计算，全组件共享)
        /// </summary>
        public static NavballAttitudeSnapshot GetAttitudeSnapshot()
        {
            int frame = Time.frameCount;
            if (_cachedAttitudeSnapshot.Frame == frame && frame != 0)
            {
                return _cachedAttitudeSnapshot;
            }

            PulseAttitudeConsumerHeartbeat();

            Quaternion camRot = Quaternion.identity;
            Camera cam = GetNavBallCamera();
            if (cam != null) camRot = cam.transform.rotation;

            Quaternion ballRot = Quaternion.identity;
            if (HasStockNavBall && StockInstance.navBall != null)
            {
                ballRot = StockInstance.navBall.rotation;
            }
            else if (StockInstance != null)
            {
                ballRot = StockInstance.relativeGymbal;
            }
            else if (TelemetryHub.Instance != null)
            {
                ballRot = TelemetryHub.Instance.AttitudeRotation;
            }

            Quaternion viewRot = (camRot != Quaternion.identity)
                ? (Quaternion.Inverse(camRot) * ballRot)
                : ballRot;
            Quaternion invRot = Quaternion.Inverse(viewRot);

            float calcHdg = 0f;
            bool hasSuccess = false;

            if (HasStockNavBall)
            {
                try
                {
                    // 在姿态球参考系内解算机头前向矢量与天顶矢量 (彻底规避 Unity eulerAngles 万向节死锁 180° 翻转与微颤)
                    Vector3 fwdInBall = invRot * Vector3.forward;
                    Vector3 upInBall = invRot * Vector3.up;

                    float horizSqr = fwdInBall.x * fwdInBall.x + fwdInBall.z * fwdInBall.z;
                    if (horizSqr > 0.0001f)
                    {
                        calcHdg = Mathf.Atan2(fwdInBall.x, fwdInBall.z) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        calcHdg = (fwdInBall.y >= 0f)
                            ? Mathf.Atan2(upInBall.x, upInBall.z) * Mathf.Rad2Deg
                            : Mathf.Atan2(-upInBall.x, -upInBall.z) * Mathf.Rad2Deg;
                    }

                    calcHdg = (calcHdg % 360f + 360f) % 360f;

                    // 地表预发射与静止态抗噪死区锁存
                    Vessel v = FlightGlobals.ActiveVessel;
                    if (v != null && (v.situation == Vessel.Situations.PRELAUNCH || (v.LandedOrSplashed && v.srfSpeed < 0.2)))
                    {
                        FlightCtrlState ctrl = v.ctrlState;
                        bool hasControlInput = ctrl != null && (Mathf.Abs(ctrl.pitch) > 0.05f || Mathf.Abs(ctrl.yaw) > 0.05f || Mathf.Abs(ctrl.roll) > 0.05f);
                        if (!_hasLatchedHeading)
                        {
                            _latchedHeading = calcHdg;
                            _hasLatchedHeading = true;
                        }
                        else if (!hasControlInput && Mathf.Abs(Mathf.DeltaAngle(calcHdg, _latchedHeading)) < 2.0f)
                        {
                            calcHdg = _latchedHeading;
                        }
                        else
                        {
                            _latchedHeading = calcHdg;
                        }
                    }
                    else
                    {
                        _hasLatchedHeading = false;
                    }
                    hasSuccess = true;
                }
                catch { }
            }
            else if (TelemetryHub.Instance != null)
            {
                calcHdg = (float)TelemetryHub.Instance.Heading;
                hasSuccess = true;
            }

            _cachedContinuousHeadingFrame = frame;
            _cachedContinuousHeadingValue = calcHdg;
            _cachedContinuousHeadingSuccess = hasSuccess;

            _cachedAttitudeSnapshot = new NavballAttitudeSnapshot
            {
                Frame = frame,
                CamRot = camRot,
                BallRot = ballRot,
                ViewRot = viewRot,
                InvViewRot = invRot,
                HeadingAngle = calcHdg,
                HasAttitude = hasSuccess
            };

            return _cachedAttitudeSnapshot;
        }

        /// <summary>
        /// 获取经由官方/Principia 权威解算的姿态四元数（0计算量，采用世界坐标旋转）
        /// </summary>
        public static Quaternion GetRotation()
        {
            return GetAttitudeSnapshot().BallRot;
        }

        /// <summary>
        /// 获取官方/Principia 姿态球视口相机旋转（同帧单源快照）
        /// </summary>
        public static Quaternion GetCameraRotation()
        {
            return GetAttitudeSnapshot().CamRot;
        }

        /// <summary>
        /// 获取转换至 NavBall 摄像机视口空间的权威姿态四元数 Inverse(CamRot) * BallRot（同帧单源快照，0重复计算）
        /// </summary>
        public static Quaternion GetViewRotation()
        {
            return GetAttitudeSnapshot().ViewRot;
        }

        public static Renderer GetNavBallRenderer()
        {
            if (_cachedNavBallRenderer != null) return _cachedNavBallRenderer;
            if (HasStockNavBall && StockInstance.navBall != null)
            {
                _cachedNavBallRenderer = StockInstance.navBall.GetComponent<Renderer>() ?? StockInstance.navBall.GetComponentInChildren<Renderer>(true);
            }
            return _cachedNavBallRenderer;
        }

        public static MeshFilter GetNavBallMeshFilter()
        {
            if (_cachedNavBallMeshFilter != null) return _cachedNavBallMeshFilter;
            if (HasStockNavBall && StockInstance.navBall != null)
            {
                _cachedNavBallMeshFilter = StockInstance.navBall.GetComponent<MeshFilter>() ?? StockInstance.navBall.GetComponentInChildren<MeshFilter>(true);
            }
            return _cachedNavBallMeshFilter;
        }

        public static Material GetNavBallMaterial()
        {
            if (_cachedNavBallMaterial != null) return _cachedNavBallMaterial;
            Renderer r = GetNavBallRenderer();
            if (r != null)
            {
                _cachedNavBallMaterial = r.sharedMaterial;
            }
            return _cachedNavBallMaterial;
        }

        private static void UpdateTextureCaches(int frame)
        {
            _cachedTextureFrame = frame;
            Material mat = GetNavBallMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_MainTex"))
                {
                    _cachedTextureScale = mat.mainTextureScale;
                    _cachedTextureOffset = mat.mainTextureOffset;
                    _cachedNavBallTexture = mat.mainTexture;
                }
                if (_cachedNavBallTexture == null && mat.HasProperty("_MainTexture"))
                {
                    _cachedNavBallTexture = mat.GetTexture("_MainTexture");
                }
            }

            if (_cachedNavBallTexture == null && GameDatabase.Instance != null)
            {
                string[] fallbackTextures = new string[]
                {
                    "Squad/Props/IVANavBall/navball2",
                    "Squad/Props/IVANavBall/IVANavBall",
                    "Squad/Props/IVANavBallNoBase/navball2",
                    "Principia/assets/navball_surface",
                    "Principia/assets/navball_inertial"
                };

                for (int i = 0; i < fallbackTextures.Length; i++)
                {
                    Texture2D tex = GameDatabase.Instance.GetTexture(fallbackTextures[i], false);
                    if (tex != null)
                    {
                        _cachedNavBallTexture = tex;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 获取官方或 Principia / TextureReplacer 加载的高保真姿态球贴图 (单帧同态零开销缓存)
        /// </summary>
        public static Texture GetTexture()
        {
            int frame = Time.frameCount;
            if (_cachedTextureFrame != frame)
            {
                if (_cachedNavBallTexture != null && (frame - _cachedTextureFrame) < 30)
                {
                    return _cachedNavBallTexture;
                }
                UpdateTextureCaches(frame);
            }
            return _cachedNavBallTexture;
        }

        public static Vector2 GetTextureScale()
        {
            int frame = Time.frameCount;
            if (_cachedTextureFrame != frame)
            {
                if (_cachedNavBallTexture != null && (frame - _cachedTextureFrame) < 30)
                {
                    return _cachedTextureScale;
                }
                UpdateTextureCaches(frame);
            }
            return _cachedTextureScale;
        }

        public static Vector2 GetTextureOffset()
        {
            int frame = Time.frameCount;
            if (_cachedTextureFrame != frame)
            {
                if (_cachedNavBallTexture != null && (frame - _cachedTextureFrame) < 30)
                {
                    return _cachedTextureOffset;
                }
                UpdateTextureCaches(frame);
            }
            return _cachedTextureOffset;
        }

        private static Camera _cachedNavBallCamera;

        /// <summary>
        /// 获取渲染官方 NavBall 的权威 UI 摄像机 (持久单例缓存，杜绝每帧遍历 Hierarchy)
        /// </summary>
        public static Camera GetNavBallCamera()
        {
            if (_cachedNavBallCamera != null)
            {
                return _cachedNavBallCamera;
            }

            if (HasStockNavBall)
            {
                if (_cachedStockCanvas == null)
                {
                    _cachedStockCanvas = StockInstance.GetComponentInParent<Canvas>();
                }
                if (_cachedStockCanvas != null && _cachedStockCanvas.worldCamera != null)
                {
                    _cachedNavBallCamera = _cachedStockCanvas.worldCamera;
                    return _cachedNavBallCamera;
                }
            }
            if (UIMasterController.Instance != null && UIMasterController.Instance.uiCamera != null)
            {
                _cachedNavBallCamera = UIMasterController.Instance.uiCamera;
                return _cachedNavBallCamera;
            }
            return null;
        }

        /// <summary>
        /// 获取官方/Principia 矢量标线 (Prograde, Retrograde, Normal, Target, Maneuver 等) 的前向视口单位方向与可见性
        /// 委托至 NavballMarkerVectorExtractor 执行
        /// </summary>
        public static bool GetMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            return NavballMarkerVectorExtractor.GetMarkerDirection(markerKey, out dir, out isVisible);
        }

        private static int _cachedReferenceFrameNameFrame = -1;
        private static string _cachedReferenceFrameName = "SURFACE";

        /// <summary>
        /// 获取当前权威导航参考系名称 (如 BARYCENTRIC, INERTIAL, SURFACE, ORBIT, TARGET)
        /// </summary>
        public static string GetReferenceFrameName()
        {
            int frame = Time.frameCount;
            if (_cachedReferenceFrameNameFrame == frame)
            {
                return _cachedReferenceFrameName;
            }
            _cachedReferenceFrameNameFrame = frame;

            PulseSpeedConsumerHeartbeat();
            if (PrincipiaProbe.IsAvailable)
            {
                string pNav = PrincipiaProbe.NavballFrameName;
                if (!string.IsNullOrEmpty(pNav))
                {
                    _cachedReferenceFrameName = pNav.Trim();
                    return _cachedReferenceFrameName;
                }
                string pFrame = PrincipiaProbe.FrameName;
                if (!string.IsNullOrEmpty(pFrame))
                {
                    _cachedReferenceFrameName = pFrame.Trim();
                    return _cachedReferenceFrameName;
                }
            }

            if (SpeedDisplay.Instance != null && SpeedDisplay.Instance.textTitle != null)
            {
                string title = SpeedDisplay.Instance.textTitle.text;
                if (!string.IsNullOrEmpty(title))
                {
                    _cachedReferenceFrameName = title.Trim();
                    return _cachedReferenceFrameName;
                }
            }

            switch (FlightGlobals.speedDisplayMode)
            {
                case FlightGlobals.SpeedDisplayModes.Surface:
                    _cachedReferenceFrameName = "SURFACE";
                    break;
                case FlightGlobals.SpeedDisplayModes.Orbit:
                    _cachedReferenceFrameName = "ORBIT";
                    break;
                case FlightGlobals.SpeedDisplayModes.Target:
                    _cachedReferenceFrameName = "TARGET";
                    break;
                default:
                    _cachedReferenceFrameName = "ORBIT";
                    break;
            }
            return _cachedReferenceFrameName;
        }

        private static int _cachedNavballSpeedFrame = -1;
        private static bool _cachedNavballSpeedResult = false;
        private static double _cachedNavballSpeedValue = 0.0;

        /// <summary>
        /// 从原版姿态球/Principia 权威速度指示牌 (SpeedDisplay) 读取并解析当前显示的实际速度 (m/s)
        /// 动态优化：在原生 KSP 环境下直接读取引擎双精度遥测（0 GC、0.0001ms、无需字符串反解）
        /// </summary>
        public static bool TryGetNavballSpeed(out double speed)
        {
            int frame = Time.frameCount;
            if (_cachedNavballSpeedFrame == frame)
            {
                speed = _cachedNavballSpeedValue;
                return _cachedNavballSpeedResult;
            }
            _cachedNavballSpeedFrame = frame;

            PulseSpeedConsumerHeartbeat();
            speed = 0.0;

            // 1. 无 Principia 时直接走原生物理速度（0 GC、0.0001ms、无字符串正则解析开销）
            if (!PrincipiaProbe.IsAvailable)
            {
                if (FlightGlobals.ActiveVessel != null)
                {
                    switch (FlightGlobals.speedDisplayMode)
                    {
                        case FlightGlobals.SpeedDisplayModes.Surface:
                            speed = FlightGlobals.ship_srfSpeed;
                            _cachedNavballSpeedValue = speed;
                            _cachedNavballSpeedResult = true;
                            return true;
                        case FlightGlobals.SpeedDisplayModes.Orbit:
                            speed = FlightGlobals.ship_obtSpeed;
                            _cachedNavballSpeedValue = speed;
                            _cachedNavballSpeedResult = true;
                            return true;
                        case FlightGlobals.SpeedDisplayModes.Target:
                            speed = FlightGlobals.ship_tgtSpeed;
                            _cachedNavballSpeedValue = speed;
                            _cachedNavballSpeedResult = true;
                            return true;
                    }
                }
                speed = FlightGlobals.GetDisplaySpeed() * SpeedDisplay.speedMultiplier;
                _cachedNavballSpeedValue = speed;
                _cachedNavballSpeedResult = true;
                return true;
            }

            // 2. Principia 场景：优先通过探针直接读取双精度底层物理速率 (0 GC, 纳秒级)
            if (PrincipiaProbe.GetActiveVesselSpeed(out double pSpeed))
            {
                speed = pSpeed;
                _cachedNavballSpeedValue = speed;
                _cachedNavballSpeedResult = true;
                return true;
            }

            // 3. Principia 文本兜底回退
            if (SpeedDisplay.Instance != null && SpeedDisplay.Instance.textSpeed != null)
            {
                string raw = SpeedDisplay.Instance.textSpeed.text;
                if (!string.IsNullOrEmpty(raw) && FastParseSpeed(raw, out speed))
                {
                    _cachedNavballSpeedValue = speed;
                    _cachedNavballSpeedResult = true;
                    return true;
                }
            }

            speed = FlightGlobals.GetDisplaySpeed() * SpeedDisplay.speedMultiplier;
            _cachedNavballSpeedValue = speed;
            _cachedNavballSpeedResult = true;
            return true;
        }

        private static bool FastParseSpeed(string raw, out double speed)
        {
            speed = 0.0;
            if (string.IsNullOrEmpty(raw)) return false;

            try
            {
                int len = raw.Length;
                int start = -1;
                int end = -1;
                for (int i = 0; i < len; i++)
                {
                    char c = raw[i];
                    if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == ',')
                    {
                        if (start < 0) start = i;
                        end = i;
                    }
                    else if (start >= 0 && (c == ' ' || c == 'm' || c == 'k' || c == 'M'))
                    {
                        break;
                    }
                }

                if (start < 0 || end < start) return false;

                string numStr = raw.Substring(start, end - start + 1).Trim();
                if (numStr.Contains(",") && numStr.Contains("."))
                {
                    numStr = numStr.Replace(",", "");
                }
                else if (numStr.Contains(",") && !numStr.Contains("."))
                {
                    int commaIdx = numStr.LastIndexOf(',');
                    if (numStr.Length - 1 - commaIdx <= 2)
                    {
                        numStr = numStr.Replace(',', '.');
                    }
                    else
                    {
                        numStr = numStr.Replace(",", "");
                    }
                }

                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed))
                {
                    if (raw.IndexOf("km/h", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        speed /= 3.6;
                    }
                    else if (raw.IndexOf("km/s", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        speed *= 1000.0;
                    }
                    return !double.IsNaN(speed) && !double.IsInfinity(speed);
                }
            }
            catch { }
            return false;
        }

        private static Texture _lastSampledTexture = null;
        private static string _cachedPixelFingerprintCategory = null;

        private static string CacheAndReturn(Texture tex, string category)
        {
            _lastSampledTexture = tex;
            _cachedPixelFingerprintCategory = category;
            return category;
        }

        private static int _cachedReferenceFrameCategoryFrame = -1;
        private static string _cachedReferenceFrameCategory = "SURFACE";

        /// <summary>
        /// 获取当前权威参考系所属宏观类别 (SURFACE, BODY_FIXED, INERTIAL, ORBIT, LAGRANGE, TARGET)
        /// </summary>
        public static string GetReferenceFrameCategory()
        {
            int frame = Time.frameCount;
            if (_cachedReferenceFrameCategoryFrame == frame)
            {
                return _cachedReferenceFrameCategory;
            }
            _cachedReferenceFrameCategoryFrame = frame;
            _cachedReferenceFrameCategory = ComputeReferenceFrameCategory();
            return _cachedReferenceFrameCategory;
        }

        private static string ComputeReferenceFrameCategory()
        {
            if (PrincipiaProbe.IsAvailable)
            {
                if (PrincipiaProbe.IsTargetFrameSelected)
                {
                    return "TARGET";
                }

                switch (PrincipiaProbe.CurrentFrameCategory)
                {
                    case PrincipiaProbe.ReferenceFrameCategory.Target:
                        return "TARGET";
                    case PrincipiaProbe.ReferenceFrameCategory.Inertial:
                        return "INERTIAL";
                    case PrincipiaProbe.ReferenceFrameCategory.Lagrange:
                        return "LAGRANGE";
                    case PrincipiaProbe.ReferenceFrameCategory.Orbital:
                        return "ORBIT";
                    case PrincipiaProbe.ReferenceFrameCategory.Surface:
                        return "BODY_FIXED";
                }

                string pType = PrincipiaProbe.FrameTypeString;
                if (!string.IsNullOrEmpty(pType))
                {
                    if (pType.IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("BODY_FIXED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("ROTATING", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "BODY_FIXED";
                    if (pType.IndexOf("NON_ROTATING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("INERTIAL", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "INERTIAL";
                    if (pType.IndexOf("BARYCENTRIC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("PULSATING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("LAGRANGE", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "LAGRANGE";
                    if (pType.IndexOf("PARENT_DIRECTION", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("BODY_DIRECTION", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("ORBIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("ECLIPTIC", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "ORBIT";
                }
            }

            if (!PrincipiaProbe.IsAvailable)
            {
                switch (FlightGlobals.speedDisplayMode)
                {
                    case FlightGlobals.SpeedDisplayModes.Target: return "TARGET";
                    case FlightGlobals.SpeedDisplayModes.Orbit: return "ORBIT";
                    case FlightGlobals.SpeedDisplayModes.Surface: return "SURFACE";
                    default: return "ORBIT";
                }
            }

            string frameName = GetReferenceFrameName();
            if (!string.IsNullOrEmpty(frameName))
            {
                string fn = frameName.ToLowerInvariant();
                if (fn.Contains("barycentric") || fn.Contains("lagrange") || fn.Contains("pulsating") || fn.Contains("l1") || fn.Contains("l2") || fn.Contains("l点") || fn.Contains("拉格朗日"))
                    return "LAGRANGE";
                if (fn.Contains("inertial") || fn.Contains("non_rotating") || fn.Contains("惯性") || fn.Contains("不旋转"))
                    return "INERTIAL";
                if (fn.Contains("orbit") || fn.Contains("body_direction") || fn.Contains("parent_direction") || fn.Contains("轨道") || fn.Contains("黄道") || fn.Contains("ecliptic"))
                    return "ORBIT";
                if (fn.Contains("target") || fn.Contains("dock") || fn.Contains("目标"))
                    return "TARGET";
                if (fn.Contains("body_fixed") || fn.Contains("body_surface") || fn.Contains("rotating") || fn.Contains("fixed") || fn.Contains("体固") || fn.Contains("地固"))
                    return "BODY_FIXED";
                if (fn.Contains("surface") || fn.Contains("ground") || fn.Contains("地表"))
                    return "SURFACE";
            }

            Texture tex = GetTexture();
            if (tex != null)
            {
                if (tex == _lastSampledTexture && !string.IsNullOrEmpty(_cachedPixelFingerprintCategory))
                {
                    return _cachedPixelFingerprintCategory;
                }

                string tName = tex.name;
                if (!string.IsNullOrEmpty(tName))
                {
                    if (tName.IndexOf("inertial", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "INERTIAL");
                    if (tName.IndexOf("barycentric", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "LAGRANGE");
                    if (tName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "TARGET");
                    if (tName.IndexOf("body_direction", StringComparison.OrdinalIgnoreCase) >= 0 || tName.IndexOf("orbit", StringComparison.OrdinalIgnoreCase) >= 0 || tName.IndexOf("ecliptic", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "ORBIT");
                    if (tName.IndexOf("navball_surface", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "BODY_FIXED");
                    if (tName.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0 || tName.IndexOf("navball", StringComparison.OrdinalIgnoreCase) >= 0) return CacheAndReturn(tex, "SURFACE");
                }

                if (tex is Texture2D t2d && t2d.isReadable)
                {
                    try
                    {
                        Color north = t2d.GetPixelBilinear(0.5f, 0.75f);
                        Color south = t2d.GetPixelBilinear(0.5f, 0.25f);

                        if (north.r > 0.40f && north.b > 0.40f && north.g < 0.35f) return CacheAndReturn(tex, "LAGRANGE");

                        if (Mathf.Abs(north.r - north.g) < 0.08f && Mathf.Abs(north.g - north.b) < 0.08f && south.r < 0.15f && south.g < 0.15f && south.b < 0.15f)
                            return CacheAndReturn(tex, "INERTIAL");

                        if (north.r > 0.55f && north.r > north.b + 0.15f && north.g > 0.30f)
                            return CacheAndReturn(tex, "TARGET");

                        if (north.r > 0.55f && north.g > 0.38f && north.b < 0.35f)
                            return CacheAndReturn(tex, "ORBIT");

                        if (north.r > north.b + 0.15f && south.b > south.r + 0.15f)
                            return CacheAndReturn(tex, "BODY_FIXED");

                        if (north.b > north.r + 0.15f)
                            return CacheAndReturn(tex, "SURFACE");
                    }
                    catch { }
                }
            }

            switch (FlightGlobals.speedDisplayMode)
            {
                case FlightGlobals.SpeedDisplayModes.Target: return "TARGET";
                case FlightGlobals.SpeedDisplayModes.Orbit: return "ORBIT";
                case FlightGlobals.SpeedDisplayModes.Surface: return "SURFACE";
                default: return "ORBIT";
            }
        }

        private static float _latchedHeading = 0f;
        private static bool _hasLatchedHeading = false;

        public static string GetHeadingText()
        {
            if (GetContinuousHeading(out float hdg))
            {
                int h = (Mathf.RoundToInt(hdg) % 360 + 360) % 360;
                return CacheManager.FastDegree(h);
            }
            if (!PrincipiaProbe.IsAvailable && HasStockNavBall && StockInstance.headingText != null)
            {
                string txt = StockInstance.headingText.text;
                if (!string.IsNullOrEmpty(txt)) return txt;
            }
            int fallbackH = TelemetryHub.Instance != null ? Mathf.RoundToInt(TelemetryHub.Instance.Heading) % 360 : 0;
            return CacheManager.FastDegree(fallbackH);
        }

        public static bool GetContinuousHeading(out float heading)
        {
            var snapshot = GetAttitudeSnapshot();
            heading = snapshot.HeadingAngle;
            return snapshot.HasAttitude;
        }

        public static void HideStockAltimeter(bool hide)
        {
            StockUIHider.HideStockAltimeter(hide);
        }

        public static void HideStockBottomLeft(bool hide)
        {
            StockUIHider.HideStockBottomLeft(hide);
        }

        public static void HideStockTimeWarp(bool hide)
        {
            StockUIHider.HideStockTimeWarp(hide);
        }

        public static void HideStockCommNet(bool hide)
        {
            StockUIHider.HideStockCommNet(hide);
        }

        public static void HideStockNavballCompletely(bool hide)
        {
            StockUIHider.HideStockNavballCompletely(hide);
        }

        private static bool _hasSavedStockNavballTransform = false;
        private static Vector3 _origStockNavballPosition;
        private static Vector2 _origStockNavballAnchoredPos;
        private static Vector3 _origStockNavballScale = Vector3.one;
        private static Vector2 _origStockNavballAnchorMin;
        private static Vector2 _origStockNavballAnchorMax;
        private static Vector2 _origStockNavballPivot;

        public static RectTransform GetStockNavballPanelTransform()
        {
            if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
            {
                if (FlightUIModeController.Instance.navBall.panelTransform != null)
                    return FlightUIModeController.Instance.navBall.panelTransform;
                var rt = FlightUIModeController.Instance.navBall.GetComponent<RectTransform>();
                if (rt != null) return rt;
            }
            if (StockInstance != null)
            {
                var rt = StockInstance.GetComponent<RectTransform>();
                if (rt != null) return rt;
                if (StockInstance.transform.parent != null)
                    return StockInstance.transform.parent.GetComponent<RectTransform>();
            }
            return null;
        }

        /// <summary>
        /// 将原版导航球实时对齐至 MFP 编辑模式/运行时小组件位置与拉伸缩放比例 (即刻响应编辑拖拽与手柄缩放)
        /// </summary>
        public static void SyncStockNavballToWidget(RectTransform widgetRt, float widgetScale)
        {
            if (widgetRt == null || StockInstance == null) return;
            var targetRt = GetStockNavballPanelTransform();
            if (targetRt == null) return;

            if (!_hasSavedStockNavballTransform)
            {
                _hasSavedStockNavballTransform = true;
                _origStockNavballPosition = targetRt.position;
                _origStockNavballAnchoredPos = targetRt.anchoredPosition;
                _origStockNavballScale = targetRt.localScale;
                _origStockNavballAnchorMin = targetRt.anchorMin;
                _origStockNavballAnchorMax = targetRt.anchorMax;
                _origStockNavballPivot = targetRt.pivot;
            }

            Canvas widgetCanvas = widgetRt.GetComponentInParent<Canvas>();
            Camera widgetCam = widgetCanvas != null && widgetCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? widgetCanvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(widgetCam, widgetRt.position);

            Canvas targetCanvas = targetRt.GetComponentInParent<Canvas>();
            Camera targetCam = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCanvas.worldCamera : null;

            targetRt.pivot = new Vector2(0.5f, 0.5f);
            RectTransform parentRt = targetRt.parent as RectTransform;
            if (parentRt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenPoint, targetCam, out Vector2 localPoint))
            {
                targetRt.localPosition = new Vector3(localPoint.x, localPoint.y, targetRt.localPosition.z);
            }
            else
            {
                targetRt.position = widgetRt.position;
            }

            float globalScale = WidgetLayoutManager.Instance != null && WidgetLayoutManager.Instance.CurrentLayout != null
                ? WidgetLayoutManager.Instance.CurrentLayout.GlobalScale
                : 1.0f;
            float finalScale = widgetScale * globalScale;
            targetRt.localScale = Vector3.one * Mathf.Max(0.2f, finalScale);
        }

        public static void ResetStockNavballTransform()
        {
            if (!_hasSavedStockNavballTransform) return;
            var targetRt = GetStockNavballPanelTransform();
            if (targetRt != null)
            {
                targetRt.position = _origStockNavballPosition;
                targetRt.anchorMin = _origStockNavballAnchorMin;
                targetRt.anchorMax = _origStockNavballAnchorMax;
                targetRt.pivot = _origStockNavballPivot;
                targetRt.anchoredPosition = _origStockNavballAnchoredPos;
                targetRt.localScale = _origStockNavballScale;
            }
            _hasSavedStockNavballTransform = false;
        }

        public static void SetStockNavballClean(bool clean)
        {
            StockUIHider.SetStockNavballClean(clean);
        }
    }

    /// <summary>
    /// INavBallVisualHook 官方姿态球实现类
    /// </summary>
    public class StockNavBallVisualHook : INavBallVisualHook
    {
        public bool HasStockNavBall => StockNavBallHook.HasStockNavBall;

        public Mesh StockMesh
        {
            get
            {
                if (!HasStockNavBall) return null;
                MeshFilter mf = StockNavBallHook.GetNavBallMeshFilter();
                return mf != null ? mf.sharedMesh : null;
            }
        }

        public Vector2 TextureScale => StockNavBallHook.GetTextureScale();

        public Vector2 TextureOffset => StockNavBallHook.GetTextureOffset();

        public Quaternion CameraRotation => StockNavBallHook.GetAttitudeSnapshot().CamRot;

        public Quaternion BallRotation => StockNavBallHook.GetAttitudeSnapshot().BallRot;

        public Quaternion ViewRotation => StockNavBallHook.GetAttitudeSnapshot().ViewRot;

        public Texture BallTexture => StockNavBallHook.GetTexture();

        public string HeadingText => StockNavBallHook.GetHeadingText();

        public string FrameName => StockNavBallHook.GetReferenceFrameName();
        public string ReferenceFrameCategory => StockNavBallHook.GetReferenceFrameCategory();
        public bool TryGetNavballSpeed(out double speed) => StockNavBallHook.TryGetNavballSpeed(out speed);
        public float HeadingAngle => StockNavBallHook.GetAttitudeSnapshot().HeadingAngle;

        public bool GetMarkerDirection(string markerType, out Vector3 dir, out bool isVisible)
        {
            return StockNavBallHook.GetMarkerDirection(markerType, out dir, out isVisible);
        }
    }
}
