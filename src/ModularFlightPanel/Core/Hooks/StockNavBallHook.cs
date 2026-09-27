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

        public static void RegisterStockNavBall(NavBall instance)
        {
            if (instance == null) return;
            if (_stockInstance != instance)
            {
                _stockInstance = instance;
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
        /// 获取经由官方/Principia 权威解算的姿态四元数（0计算量，采用世界坐标旋转）
        /// </summary>
        public static Quaternion GetRotation()
        {
            PulseAttitudeConsumerHeartbeat();
            if (HasStockNavBall && StockInstance.navBall != null)
            {
                return StockInstance.navBall.rotation;
            }
            return TelemetryHub.Instance != null ? TelemetryHub.Instance.AttitudeRotation : Quaternion.identity;
        }

        /// <summary>
        /// 获取官方或 Principia / TextureReplacer 加载的高保真姿态球贴图
        /// </summary>
        public static Texture GetTexture()
        {
            if (HasStockNavBall)
            {
                Renderer r = StockInstance.navBall.GetComponent<Renderer>() ?? StockInstance.navBall.GetComponentInChildren<Renderer>(true);
                if (r != null)
                {
                    Material mat = r.sharedMaterial ?? r.material;
                    if (mat != null)
                    {
                        if (mat.HasProperty("_MainTexture"))
                        {
                            Texture tex = mat.GetTexture("_MainTexture");
                            if (tex != null) return tex;
                        }
                        if (mat.HasProperty("_MainTex"))
                        {
                            Texture tex = mat.GetTexture("_MainTex");
                            if (tex != null) return tex;
                        }
                        if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                        {
                            return mat.mainTexture;
                        }
                    }
                }
            }

            if (GameDatabase.Instance != null)
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
                    if (tex != null) return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取渲染官方 NavBall 的权威 UI 摄像机
        /// </summary>
        public static Camera GetNavBallCamera()
        {
            if (HasStockNavBall)
            {
                Canvas canvas = StockInstance.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.worldCamera != null)
                {
                    return canvas.worldCamera;
                }
            }
            if (UIMasterController.Instance != null && UIMasterController.Instance.uiCamera != null)
            {
                return UIMasterController.Instance.uiCamera;
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

        /// <summary>
        /// 获取当前权威导航参考系名称 (如 BARYCENTRIC, INERTIAL, SURFACE, ORBIT, TARGET)
        /// </summary>
        public static string GetReferenceFrameName()
        {
            PulseSpeedConsumerHeartbeat();
            if (PrincipiaProbe.IsAvailable)
            {
                string pNav = PrincipiaProbe.NavballFrameName;
                if (!string.IsNullOrEmpty(pNav)) return pNav.Trim();
                string pFrame = PrincipiaProbe.FrameName;
                if (!string.IsNullOrEmpty(pFrame)) return pFrame.Trim();
            }

            if (SpeedDisplay.Instance != null && SpeedDisplay.Instance.textTitle != null)
            {
                string title = SpeedDisplay.Instance.textTitle.text;
                if (!string.IsNullOrEmpty(title))
                {
                    return title.Trim();
                }
            }

            switch (FlightGlobals.speedDisplayMode)
            {
                case FlightGlobals.SpeedDisplayModes.Surface: return "SURFACE";
                case FlightGlobals.SpeedDisplayModes.Orbit: return "ORBIT";
                case FlightGlobals.SpeedDisplayModes.Target: return "TARGET";
                default: return "ORBIT";
            }
        }

        /// <summary>
        /// 从原版姿态球/Principia 权威速度指示牌 (SpeedDisplay) 读取并解析当前显示的实际速度 (m/s)
        /// 动态优化：在原生 KSP 环境下直接读取引擎双精度遥测（0 GC、0.0001ms、无需字符串反解）
        /// </summary>
        public static bool TryGetNavballSpeed(out double speed)
        {
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
                            return true;
                        case FlightGlobals.SpeedDisplayModes.Orbit:
                            speed = FlightGlobals.ship_obtSpeed;
                            return true;
                        case FlightGlobals.SpeedDisplayModes.Target:
                            speed = FlightGlobals.ship_tgtSpeed;
                            return true;
                    }
                }
                speed = FlightGlobals.GetDisplaySpeed() * SpeedDisplay.speedMultiplier;
                return true;
            }

            // 2. Principia 场景：优先通过探针直接读取双精度底层物理速率 (0 GC, 纳秒级)
            if (PrincipiaProbe.GetActiveVesselSpeed(out double pSpeed))
            {
                speed = pSpeed;
                return true;
            }

            // 3. Principia 文本兜底回退
            if (SpeedDisplay.Instance != null && SpeedDisplay.Instance.textSpeed != null)
            {
                string raw = SpeedDisplay.Instance.textSpeed.text;
                if (!string.IsNullOrEmpty(raw) && FastParseSpeed(raw, out speed))
                {
                    return true;
                }
            }

            speed = FlightGlobals.GetDisplaySpeed() * SpeedDisplay.speedMultiplier;
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

        /// <summary>
        /// 获取当前权威参考系所属宏观类别 (SURFACE, BODY_FIXED, INERTIAL, ORBIT, LAGRANGE, TARGET)
        /// </summary>
        public static string GetReferenceFrameCategory()
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

        public static string GetHeadingText()
        {
            if (HasStockNavBall && StockInstance.headingText != null)
            {
                string txt = StockInstance.headingText.text;
                if (!string.IsNullOrEmpty(txt)) return txt;
            }
            int h = TelemetryHub.Instance != null ? Mathf.RoundToInt(TelemetryHub.Instance.Heading) % 360 : 0;
            return $"{h:D3}°";
        }

        public static bool GetContinuousHeading(out float heading)
        {
            if (HasStockNavBall)
            {
                try
                {
                    Quaternion invGymbal = Quaternion.Inverse(StockInstance.relativeGymbal);
                    heading = (invGymbal.eulerAngles.y % 360f + 360f) % 360f;
                    return true;
                }
                catch { }
            }
            heading = 0f;
            return false;
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
                MeshFilter mf = StockNavBallHook.StockInstance.navBall.GetComponent<MeshFilter>() ??
                    StockNavBallHook.StockInstance.navBall.GetComponentInChildren<MeshFilter>(true);
                return mf != null ? mf.sharedMesh : null;
            }
        }

        public Vector2 TextureScale
        {
            get
            {
                if (!HasStockNavBall) return Vector2.one;
                Renderer r = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>() ??
                    StockNavBallHook.StockInstance.navBall.GetComponentInChildren<Renderer>(true);
                Material mat = (r != null) ? (r.sharedMaterial ?? r.material) : null;
                return (mat != null && mat.HasProperty("_MainTex")) ? mat.mainTextureScale : Vector2.one;
            }
        }

        public Vector2 TextureOffset
        {
            get
            {
                if (!HasStockNavBall) return Vector2.zero;
                Renderer r = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>() ??
                    StockNavBallHook.StockInstance.navBall.GetComponentInChildren<Renderer>(true);
                Material mat = (r != null) ? (r.sharedMaterial ?? r.material) : null;
                return (mat != null && mat.HasProperty("_MainTex")) ? mat.mainTextureOffset : Vector2.zero;
            }
        }

        public Quaternion CameraRotation
        {
            get
            {
                Camera cam = StockNavBallHook.GetNavBallCamera();
                return cam != null ? cam.transform.rotation : Quaternion.identity;
            }
        }

        public Quaternion BallRotation => StockNavBallHook.GetRotation();

        public Texture BallTexture => StockNavBallHook.GetTexture();

        public string HeadingText => StockNavBallHook.GetHeadingText();

        public string FrameName => StockNavBallHook.GetReferenceFrameName();
        public string ReferenceFrameCategory => StockNavBallHook.GetReferenceFrameCategory();
        public bool TryGetNavballSpeed(out double speed) => StockNavBallHook.TryGetNavballSpeed(out speed);
        public float HeadingAngle
        {
            get
            {
                if (StockNavBallHook.GetContinuousHeading(out float hdg)) return hdg;
                return FlightTelemetryContext.Current?.Heading ?? 0f;
            }
        }

        public bool GetMarkerDirection(string markerType, out Vector3 dir, out bool isVisible)
        {
            return StockNavBallHook.GetMarkerDirection(markerType, out dir, out isVisible);
        }
    }
}
