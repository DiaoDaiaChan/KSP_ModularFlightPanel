using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 官方原生 NavBall 深度桥接与挂钩器 (Zero-Calculation Hook)
    /// 
    /// 核心理念：
    /// 1. 姿态旋转：直接读取官方/Principia 权威 world rotation（不做任何多余计算，完美兼容 Principia 任意参考系及载具控向点）
    /// 2. 贴图支持：直接探测 _MainTexture 与 _MainTex（天然兼容 Principia 动态多参考系贴图及 TextureReplacer）
    /// 3. 矢量标线：挂钩官方与第三方 (Trajectories, Maneuver, Principia) 实时标线向量并投影呈现
    /// </summary>
    public static class StockNavBallHook
    {
        static StockNavBallHook()
        {
            NavBallHookService.HideStockNavballAction = HideStockNavballCompletely;
            NavBallHookService.HideStockAltimeterAction = HideStockAltimeter;
            NavBallHookService.HideStockBottomLeftAction = HideStockBottomLeft;
            NavBallHookService.HideStockTimeWarpAction = HideStockTimeWarp;
            NavBallHookService.HideStockCommNetAction = HideStockCommNet;
            NavBallHookService.RestoreAllStockUIAction = RestoreAllStockUI;
        }

        public static void RestoreAllStockUI()
        {
            HideStockNavballCompletely(false);
            HideStockAltimeter(false);
            HideStockBottomLeft(false);
            HideStockTimeWarp(false);
            HideStockCommNet(false);
            NavBallHookService.RestoreStockToolbarAction?.Invoke();
        }

        private static NavBall _stockInstance;
        public static NavBall StockInstance
        {
            get
            {
                if (_stockInstance == null)
                {
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
                return _stockInstance;
            }
            set => _stockInstance = value;
        }
        public static bool HasStockNavBall => StockInstance != null && StockInstance.navBall != null;

        private static NavBallBurnVector _cachedBurnVector;
        private static Transform _cachedManeuverTransform;

        public static void RegisterStockNavBall(NavBall instance)
        {
            if (instance == null) return;
            if (_stockInstance != instance)
            {
                _stockInstance = instance;
                _cachedBurnVector = null;
                _cachedManeuverTransform = null;
                _cachedAllStockRenderers.Clear();
                _cachedAllStockGraphics.Clear();
                _lastScanTime = -1f;
                NavBallHookService.Provider = new StockNavBallVisualHook();
                Debug.Log("[ModularFlightPanel] Successfully hooked Stock NavBall instance!");
            }
        }

        public static void UnregisterStockNavBall(NavBall instance)
        {
            if (_stockInstance == instance)
            {
                _stockInstance = null;
                _cachedBurnVector = null;
                _cachedManeuverTransform = null;
                _cachedAllStockRenderers.Clear();
                _cachedAllStockGraphics.Clear();
                _lastScanTime = -1f;
                NavBallHookService.Provider = null;
            }
        }

        /// <summary>
        /// 获取经由官方/Principia 权威解算的姿态四元数（0计算量，采用世界坐标旋转）
        /// </summary>
        public static Quaternion GetRotation()
        {
            if (HasStockNavBall && StockInstance.gameObject.activeInHierarchy)
            {
                // Principia 每帧将解算姿态写入 navBall.rotation (World Rotation)
                // 原版 KSP 亦通过世界坐标驱动姿态球网格
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
                        // 1. Principia 显式注入的 "_MainTexture" (质心/惯性/地表/目标/罗盘贴图)
                        if (mat.HasProperty("_MainTexture"))
                        {
                            Texture tex = mat.GetTexture("_MainTexture");
                            if (tex != null) return tex;
                        }
                        // 2. 原生 Unity 规范 "_MainTex"
                        if (mat.HasProperty("_MainTex"))
                        {
                            Texture tex = mat.GetTexture("_MainTex");
                            if (tex != null) return tex;
                        }
                        // 3. Unity 标准 mainTexture 属性读取 (仅当存在 _MainTex 时安全读取)
                        if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                        {
                            return mat.mainTexture;
                        }
                    }
                }
            }

            // 4. 原版与 Principia GameDatabase 资源多重安全后备
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
            return Camera.main;
        }

        /// <summary>
        /// 获取官方/Principia 矢量标线 (Prograde, Retrograde, Normal, Target, Maneuver 等) 的前向视口单位方向与可见性
        /// 双重保障：优先官方活跃 GameObject，一旦原版隐藏/折叠/停用立即无缝启用数学解耦解算
        /// </summary>
        public static bool GetMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            // 1. 优先尝试直接从原版活跃的 Marker Transform 获取 (仅当原版处于激活状态时)
            if (HasStockNavBall && StockInstance.gameObject.activeInHierarchy)
            {
                Transform marker = GetMarkerTransformByKey(markerKey);
                if (marker != null && marker.gameObject.activeSelf)
                {
                    Vector3 localPos = marker.localPosition;
                    if (localPos.sqrMagnitude > 0.0001f)
                    {
                        // KSP 原版 NavBall 内部 3D 模型存在 90 度旋转基准偏移 (rotationOffset = (90, 0, 0)):
                        // localPos.x -> HUD X (水平偏航左右，右为正)
                        // localPos.z -> HUD Y (垂直俯仰上下，上为正)
                        // -localPos.y -> HUD Z (视口前向深度，>0 位于可见前半球面对玩家)
                        Vector3 hudDir = new Vector3(localPos.x, localPos.z, -localPos.y).normalized;
                        float cutoff = StockInstance.VectorUnitCutoff != 0f ? StockInstance.VectorUnitCutoff : 0.022f;
                        isVisible = (hudDir.z > cutoff);
                        dir = hudDir;
                        return true;
                    }
                }
            }

            // 2. 权威解耦数学模型兜底解算 (Bulletproof Math Fallback)
            return CalculateMarkerDirectionMath(markerKey, out dir, out isVisible);
        }

        private static Transform GetMarkerTransformByKey(string markerKey)
        {
            if (!HasStockNavBall) return null;
            switch (markerKey.ToLowerInvariant())
            {
                case "prograde": return StockInstance.progradeVector;
                case "retrograde": return StockInstance.retrogradeVector;
                case "normal": return StockInstance.normalVector;
                case "antinormal": return StockInstance.antiNormalVector;
                case "radialin": return StockInstance.radialInVector;
                case "radialout": return StockInstance.radialOutVector;
                case "target": return StockInstance.progradeWaypoint;
                case "antitarget": return StockInstance.retrogradeWaypoint;
                case "maneuver": return GetManeuverTransform();
                default: return null;
            }
        }

        private static bool CalculateMarkerDirectionMath(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                // 无活跃载具时（如主菜单仿真/离线渲染），从当前仿真遥测上下文获取
                return CalculateSimulatedMarkerDirection(markerKey, out dir, out isVisible);
            }

            Transform refTransform = vessel.ReferenceTransform ?? vessel.transform;
            if (refTransform == null) return false;

            Vector3 worldVec = Vector3.zero;
            bool hasValidVector = false;

            string key = markerKey.ToLowerInvariant();
            switch (key)
            {
                case "prograde":
                case "retrograde":
                {
                    Vector3d vel = Vector3d.zero;
                    switch (FlightGlobals.speedDisplayMode)
                    {
                        case FlightGlobals.SpeedDisplayModes.Surface:
                            vel = vessel.srf_velocity;
                            break;
                        case FlightGlobals.SpeedDisplayModes.Orbit:
                            vel = vessel.obt_velocity;
                            break;
                        case FlightGlobals.SpeedDisplayModes.Target:
                            if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                            {
                                vel = vessel.obt_velocity - FlightGlobals.fetch.VesselTarget.GetObtVelocity();
                            }
                            else
                            {
                                vel = vessel.srf_velocity;
                            }
                            break;
                    }

                    if (vel.sqrMagnitude > 0.01) // 速度大于 0.1 m/s 时激活
                    {
                        worldVec = (key == "prograde") ? (Vector3)vel.normalized : -(Vector3)vel.normalized;
                        hasValidVector = true;
                    }
                    break;
                }

                case "normal":
                case "antinormal":
                {
                    if (vessel.orbit != null)
                    {
                        Vector3d pos = vessel.orbit.pos;
                        Vector3d vel = vessel.orbit.vel;
                        Vector3d norm = Vector3d.Cross(pos, vel);
                        if (norm.sqrMagnitude > 0.0001)
                        {
                            Vector3 n = (Vector3)norm.normalized;
                            worldVec = (key == "normal") ? n : -n;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case "radialin":
                case "radialout":
                {
                    if (vessel.orbit != null)
                    {
                        Vector3d pos = vessel.orbit.pos;
                        Vector3d vel = vessel.orbit.vel;
                        Vector3d norm = Vector3d.Cross(pos, vel);
                        if (norm.sqrMagnitude > 0.0001)
                        {
                            Vector3d rad = Vector3d.Cross(vel, norm);
                            if (rad.sqrMagnitude > 0.0001)
                            {
                                Vector3 r = (Vector3)rad.normalized;
                                worldVec = (key == "radialin") ? r : -r;
                                hasValidVector = true;
                            }
                        }
                    }
                    break;
                }

                case "target":
                case "antitarget":
                {
                    if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                    {
                        Vector3 targetDir = FlightGlobals.fetch.vesselTargetDirection;
                        if (targetDir.sqrMagnitude < 0.001f)
                        {
                            targetDir = (FlightGlobals.fetch.VesselTarget.GetTransform().position - vessel.transform.position).normalized;
                        }
                        if (targetDir.sqrMagnitude > 0.001f)
                        {
                            worldVec = (key == "target") ? targetDir : -targetDir;
                            hasValidVector = true;
                        }
                    }
                    break;
                }

                case "maneuver":
                {
                    if (vessel.patchedConicSolver != null && vessel.patchedConicSolver.maneuverNodes != null && vessel.patchedConicSolver.maneuverNodes.Count > 0)
                    {
                        var node = vessel.patchedConicSolver.maneuverNodes[0];
                        if (node != null)
                        {
                            Vector3 burnVec = (Vector3)node.GetBurnVector(vessel.orbit);
                            if (burnVec.sqrMagnitude > 0.001f)
                            {
                                worldVec = burnVec.normalized;
                                hasValidVector = true;
                            }
                        }
                    }
                    break;
                }
            }

            if (!hasValidVector) return false;

            // 将世界坐标矢量精确投影至载具基准座舱坐标系 (Cockpit HUD Space):
            // refTransform.right -> HUD X (水平左右，右正)
            // refTransform.up    -> HUD Y (垂直俯仰，上正)
            // refTransform.forward -> HUD Z (视口前向深度，>0 位于可见前半球面对玩家)
            Vector3 bodyVec = refTransform.InverseTransformDirection(worldVec);
            dir = bodyVec.normalized;
            isVisible = (dir.z > 0.022f);
            return true;
        }

        private static bool CalculateSimulatedMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;

            switch (markerKey.ToLowerInvariant())
            {
                case "prograde":
                    dir = new Vector3(0.04f, 0.18f, 0.98f).normalized;
                    isVisible = true;
                    return true;
                case "retrograde":
                    dir = new Vector3(-0.04f, -0.18f, -0.98f).normalized;
                    isVisible = false;
                    return true;
                case "normal":
                    dir = new Vector3(0f, 0.94f, 0.34f).normalized;
                    isVisible = true;
                    return true;
                case "antinormal":
                    dir = new Vector3(0f, -0.94f, -0.34f).normalized;
                    isVisible = false;
                    return true;
                case "radialin":
                    dir = new Vector3(-0.92f, 0f, 0.38f).normalized;
                    isVisible = false;
                    return true;
                case "radialout":
                    dir = new Vector3(0.92f, 0f, 0.38f).normalized;
                    isVisible = true;
                    return true;
                case "target":
                    dir = new Vector3(0.28f, 0.32f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                case "maneuver":
                    dir = new Vector3(-0.24f, 0.36f, 0.90f).normalized;
                    isVisible = true;
                    return true;
                default:
                    return false;
            }
        }

        private static Transform GetManeuverTransform()
        {
            if (_cachedManeuverTransform != null) return _cachedManeuverTransform;
            if (_cachedBurnVector == null)
            {
                _cachedBurnVector = UnityEngine.Object.FindObjectOfType<NavBallBurnVector>();
            }
            if (_cachedBurnVector != null)
            {
                _cachedManeuverTransform = _cachedBurnVector.vectorProgr;
            }
            return _cachedManeuverTransform;
        }

        /// <summary>
        /// 获取当前权威导航参考系名称 (如 BARYCENTRIC, INERTIAL, SURFACE, ORBIT, TARGET)
        /// </summary>
        public static string GetReferenceFrameName()
        {
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
        /// 获取当前权威参考系所属宏观类别 (SURFACE, INERTIAL, BARYCENTRIC, TARGET, BODY_DIRECTION)
        /// 完整支持 Principia 动态参考系贴图及原生速度模式，为程序化与着色器提供权威变色依据
        /// </summary>
        public static string GetReferenceFrameCategory()
        {
            // 1. Principia 权威参考系探针探测
            if (PrincipiaProbe.IsAvailable)
            {
                if (PrincipiaProbe.IsTargetFrameSelected)
                {
                    return "TARGET";
                }

                string pType = PrincipiaProbe.FrameTypeString;
                if (!string.IsNullOrEmpty(pType))
                {
                    if (pType.IndexOf("SURFACE", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "SURFACE";
                    if (pType.IndexOf("NON_ROTATING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("INERTIAL", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "INERTIAL";
                    if (pType.IndexOf("BARYCENTRIC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("PULSATING", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "BARYCENTRIC";
                    if (pType.IndexOf("PARENT_DIRECTION", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pType.IndexOf("BODY_CENTRED", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "BODY_DIRECTION";
                }
            }

            // 2. 贴图素材与像素指纹多重比对 (针对 Principia 动态注入的 _MainTexture)
            Texture tex = GetTexture();
            if (tex != null)
            {
                string tName = tex.name;
                if (!string.IsNullOrEmpty(tName))
                {
                    if (tName.IndexOf("inertial", StringComparison.OrdinalIgnoreCase) >= 0) return "INERTIAL";
                    if (tName.IndexOf("barycentric", StringComparison.OrdinalIgnoreCase) >= 0) return "BARYCENTRIC";
                    if (tName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0) return "TARGET";
                    if (tName.IndexOf("body_direction", StringComparison.OrdinalIgnoreCase) >= 0) return "BODY_DIRECTION";
                    if (tName.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0) return "SURFACE";
                }

                if (tex is Texture2D t2d)
                {
                    try
                    {
                        // 采样北半球 (+45°) 与南半球 (-45°) 像素
                        Color north = t2d.GetPixelBilinear(0.5f, 0.75f);
                        Color south = t2d.GetPixelBilinear(0.5f, 0.25f);

                        // Barycentric: 显著紫/品红 (R > 0.40, B > 0.40, G < 0.35)
                        if (north.r > 0.40f && north.b > 0.40f && north.g < 0.35f) return "BARYCENTRIC";

                        // Inertial: 灰天黑地 (R ≈ G ≈ B, south 极暗)
                        if (Mathf.Abs(north.r - north.g) < 0.08f && Mathf.Abs(north.g - north.b) < 0.08f && south.r < 0.15f && south.g < 0.15f && south.b < 0.15f)
                            return "INERTIAL";

                        // Target: 玫瑰粉天红地 (R > 0.55, R > B + 0.15)
                        if (north.r > 0.55f && north.r > north.b + 0.15f && north.g > 0.30f)
                            return "TARGET";

                        // Body Direction: 暖沙黄/琥珀色 (R > 0.55, G > 0.38, B < 0.35)
                        if (north.r > 0.55f && north.g > 0.38f && north.b < 0.35f)
                            return "BODY_DIRECTION";

                        // Surface: 蓝天棕地 (B > R + 0.15)
                        if (north.b > north.r + 0.15f)
                            return "SURFACE";
                    }
                    catch { }
                }
            }

            // 3. 原版 KSP 模式兜底
            switch (FlightGlobals.speedDisplayMode)
            {
                case FlightGlobals.SpeedDisplayModes.Target: return "TARGET";
                case FlightGlobals.SpeedDisplayModes.Orbit: return "INERTIAL";
                case FlightGlobals.SpeedDisplayModes.Surface: return "SURFACE";
                default: return "SURFACE";
            }
        }

        /// <summary>
        /// 获取官方结算的真北罗盘航向文本
        /// </summary>
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

        /// <summary>
        /// 获取官方权威连续高精度航向角 (0° ~ 360° 浮点数，杜绝 TextMeshPro 整数截断跳变)
        /// 完整支持 Principia、轨道、地表、目标等所有参考系下的相对万向节姿态
        /// </summary>
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

        private static float _lastScanTime = -1f;
        private static List<Renderer> _cachedAllStockRenderers = new List<Renderer>();
        private static List<Graphic> _cachedAllStockGraphics = new List<Graphic>();

        private static void SafeAddRenderersFromTransform(Transform t, List<Renderer> list)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null && !list.Contains(r)) list.Add(r);
            var arr = t.GetComponentsInChildren<Renderer>(true);
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null && !list.Contains(arr[i])) list.Add(arr[i]);
                }
            }
        }

        private static void ApplyCanvasGroup(GameObject go, bool hide)
        {
            if (go == null) return;
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = go.AddComponent<CanvasGroup>();
            }
            float targetAlpha = hide ? 0f : 1f;
            if (Mathf.Abs(cg.alpha - targetAlpha) > 0.01f)
            {
                cg.alpha = targetAlpha;
            }
            bool targetInteractable = !hide;
            if (cg.blocksRaycasts != targetInteractable)
            {
                cg.blocksRaycasts = targetInteractable;
            }
            if (cg.interactable != targetInteractable)
            {
                cg.interactable = targetInteractable;
            }
        }

        private static float _lastAltimeterScanTime = -1f;
        private static Renderer[] _cachedAltimeterRenderers;
        private static KSP.UI.Screens.AltimeterSliderButtons _cachedAltimeterSlider;

        /// <summary>
        /// 彻底隐藏/打开官方顶部高度计、垂直速度表、大气计与关联动作组面板，保持系统逻辑和物理更新完整运转
        /// </summary>
        public static void HideStockAltimeter(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.altimeterFrame != null)
                {
                    ApplyCanvasGroup(FlightUIModeController.Instance.altimeterFrame.gameObject, hide);

                    float now = Time.unscaledTime;
                    if (_cachedAltimeterRenderers == null || (now - _lastAltimeterScanTime) > 2.0f)
                    {
                        _lastAltimeterScanTime = now;
                        _cachedAltimeterRenderers = FlightUIModeController.Instance.altimeterFrame.GetComponentsInChildren<Renderer>(true);
                        if (_cachedAltimeterSlider == null)
                        {
                            _cachedAltimeterSlider = UnityEngine.Object.FindObjectOfType<KSP.UI.Screens.AltimeterSliderButtons>();
                        }
                    }

                    if (_cachedAltimeterRenderers != null)
                    {
                        for (int i = 0; i < _cachedAltimeterRenderers.Length; i++)
                        {
                            var r = _cachedAltimeterRenderers[i];
                            if (r != null && r.enabled == hide)
                            {
                                r.enabled = !hide;
                            }
                        }
                    }
                }

                if (_cachedAltimeterSlider != null)
                {
                    ApplyCanvasGroup(_cachedAltimeterSlider.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockAltimeter warning: {ex.Message}");
            }
        }

        private static float _lastBottomLeftScanTime = -1f;
        private static Renderer[] _cachedBottomLeftRenderers;

        /// <summary>
        /// 彻底隐藏/打开官方左下角分级框、微调操纵量标尺、模式切换按钮与分级资源堆栈，保持空格分级与键盘操控无损运行
        /// </summary>
        public static void HideStockBottomLeft(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null)
                {
                    var ctrl = FlightUIModeController.Instance;
                    if (ctrl.stagingQuadrant != null) ApplyCanvasGroup(ctrl.stagingQuadrant.gameObject, hide);
                    if (ctrl.dockingRotQuadrant != null) ApplyCanvasGroup(ctrl.dockingRotQuadrant.gameObject, hide);
                    if (ctrl.dockingLinQuadrant != null) ApplyCanvasGroup(ctrl.dockingLinQuadrant.gameObject, hide);
                    if (ctrl.uiModeFrame != null) ApplyCanvasGroup(ctrl.uiModeFrame.gameObject, hide);
                    if (ctrl.UIScaleModeFrame != null) ApplyCanvasGroup(ctrl.UIScaleModeFrame, hide);
                    if (ctrl.UIScaleStageManager != null) ApplyCanvasGroup(ctrl.UIScaleStageManager, hide);

                    float now = Time.unscaledTime;
                    if (_cachedBottomLeftRenderers == null || (now - _lastBottomLeftScanTime) > 2.0f)
                    {
                        _lastBottomLeftScanTime = now;
                        if (ctrl.stagingQuadrant != null)
                        {
                            _cachedBottomLeftRenderers = ctrl.stagingQuadrant.GetComponentsInChildren<Renderer>(true);
                        }
                    }

                    if (_cachedBottomLeftRenderers != null)
                    {
                        for (int i = 0; i < _cachedBottomLeftRenderers.Length; i++)
                        {
                            var r = _cachedBottomLeftRenderers[i];
                            if (r != null && r.enabled == hide)
                            {
                                r.enabled = !hide;
                            }
                        }
                    }
                }

                if (KSP.UI.Screens.StageManager.Instance != null)
                {
                    ApplyCanvasGroup(KSP.UI.Screens.StageManager.Instance.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockBottomLeft warning: {ex.Message}");
            }
        }

        private static KSP.UI.Screens.Flight.METDisplay _cachedMETDisplay;

        /// <summary>
        /// 彻底隐藏/打开官方左上角时间加速面板与 MET 任务时钟，同时保障物理时钟与时间加速内部逻辑正常运转
        /// </summary>
        public static void HideStockTimeWarp(bool hide)
        {
            try
            {
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.timeFrame != null)
                {
                    ApplyCanvasGroup(FlightUIModeController.Instance.timeFrame.gameObject, hide);
                }

                if (_cachedMETDisplay == null)
                {
                    _cachedMETDisplay = UnityEngine.Object.FindObjectOfType<KSP.UI.Screens.Flight.METDisplay>();
                }
                if (_cachedMETDisplay != null && _cachedMETDisplay.transform.parent != null)
                {
                    ApplyCanvasGroup(_cachedMETDisplay.transform.parent.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockTimeWarp warning: {ex.Message}");
            }
        }

        /// <summary>
        /// 彻底隐藏/打开官方左上角 CommNet 通信网络信号图标与 Tooltip，同时保障底层通信中继路由正常运转
        /// </summary>
        public static void HideStockCommNet(bool hide)
        {
            try
            {
                if (KSP.UI.Screens.Flight.TelemetryUpdate.Instance != null)
                {
                    ApplyCanvasGroup(KSP.UI.Screens.Flight.TelemetryUpdate.Instance.gameObject, hide);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockCommNet warning: {ex.Message}");
            }
        }

        /// <summary>
        /// 彻底隐藏官方屏幕底栏导航球及其外壳、滑块、折叠按钮，同时保障姿态数据结算脚本正常运转
        /// </summary>
        public static void HideStockNavballCompletely(bool hide)
        {
            if (StockInstance == null) return;

            try
            {
                // 1. 通过 CanvasGroup 隐藏官方 UI 容器 (完全透明、阻断射线响应，但不中断其内部状态更新)
                if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                {
                    ApplyCanvasGroup(FlightUIModeController.Instance.navBall.gameObject, hide);
                }
                if (StockInstance != null && (FlightUIModeController.Instance == null || StockInstance != FlightUIModeController.Instance.navBall))
                {
                    ApplyCanvasGroup(StockInstance.gameObject, hide);
                }

                // 2. 原版 NavBallToggle 折叠面板联动 (收起原生托盘，双重保障物理视口零遮挡)
                if (KSP.UI.Screens.Flight.NavBallToggle.Instance != null && KSP.UI.Screens.Flight.NavBallToggle.Instance.panel != null)
                {
                    var panel = KSP.UI.Screens.Flight.NavBallToggle.Instance.panel;
                    if (hide)
                    {
                        if (panel.expanded)
                        {
                            panel.CollapseImmediate();
                        }
                    }
                    else
                    {
                        if (panel.collapsed)
                        {
                            panel.ExpandImmediate();
                        }
                    }
                }

                float now = Time.unscaledTime;
                if (_cachedAllStockRenderers.Count == 0 || (now - _lastScanTime) > 1.5f)
                {
                    _lastScanTime = now;
                    _cachedAllStockRenderers.Clear();
                    _cachedAllStockGraphics.Clear();

                    // A. StockInstance 自身及子级全部 Renderers & Graphics
                    var stockR = StockInstance.GetComponentsInChildren<Renderer>(true);
                    if (stockR != null) _cachedAllStockRenderers.AddRange(stockR);

                    var stockG = StockInstance.GetComponentsInChildren<Graphic>(true);
                    if (stockG != null) _cachedAllStockGraphics.AddRange(stockG);

                    // B. FlightUIModeController navBall 节点额外安全扫描
                    if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
                    {
                        var fuimR = FlightUIModeController.Instance.navBall.GetComponentsInChildren<Renderer>(true);
                        if (fuimR != null)
                        {
                            for (int i = 0; i < fuimR.Length; i++)
                            {
                                if (fuimR[i] != null && !_cachedAllStockRenderers.Contains(fuimR[i]))
                                    _cachedAllStockRenderers.Add(fuimR[i]);
                            }
                        }
                    }

                    // C. 核心 3D 球体本体 Transform (重点：navBall 可能处于平级或外挂节点，必须显式扫描！)
                    if (StockInstance.navBall != null)
                    {
                        SafeAddRenderersFromTransform(StockInstance.navBall, _cachedAllStockRenderers);
                        if (StockInstance.navBall.parent != null && StockInstance.navBall.parent != StockInstance.transform)
                        {
                            SafeAddRenderersFromTransform(StockInstance.navBall.parent, _cachedAllStockRenderers);
                        }
                    }

                    // C. 官方全部矢量标线 (Prograde, Retrograde, Normal, AntiNormal, Radial, Waypoint, Target 等)
                    SafeAddRenderersFromTransform(StockInstance.progradeVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.retrogradeVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.normalVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.antiNormalVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.radialInVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.radialOutVector, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.progradeWaypoint, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.retrogradeWaypoint, _cachedAllStockRenderers);
                    SafeAddRenderersFromTransform(StockInstance.target, _cachedAllStockRenderers);

                    // D. 机动节点烧蚀矢量物体
                    if (_cachedBurnVector == null)
                    {
                        _cachedBurnVector = UnityEngine.Object.FindObjectOfType<NavBallBurnVector>();
                    }
                    if (_cachedBurnVector != null)
                    {
                        SafeAddRenderersFromTransform(_cachedBurnVector.transform, _cachedAllStockRenderers);
                        var bg = _cachedBurnVector.GetComponentsInChildren<Graphic>(true);
                        if (bg != null) _cachedAllStockGraphics.AddRange(bg);
                    }
                }

                bool targetState = !hide;

                // 3. 彻底启闭所有 3D 姿态球主球体网格以及所有官方标线物体的 Renderer
                for (int i = 0; i < _cachedAllStockRenderers.Count; i++)
                {
                    var r = _cachedAllStockRenderers[i];
                    if (r != null && r.enabled != targetState)
                    {
                        r.enabled = targetState;
                    }
                }

                // 4. 彻底启闭所有原版 UI Graphics (Image, TextMeshProUGUI 等)
                for (int i = 0; i < _cachedAllStockGraphics.Count; i++)
                {
                    var g = _cachedAllStockGraphics[i];
                    if (g != null && g.enabled != targetState)
                    {
                        g.enabled = targetState;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] HideStockNavballCompletely warning: {ex.Message}");
            }
        }
    }

    public class StockNavBallVisualHook : INavBallVisualHook
    {
        public bool HasStockNavBall => StockNavBallHook.HasStockNavBall;

        public Mesh StockMesh
        {
            get
            {
                if (!HasStockNavBall) return null;
                MeshFilter mf = StockNavBallHook.StockInstance.navBall.GetComponent<MeshFilter>() ?? StockNavBallHook.StockInstance.navBall.GetComponentInChildren<MeshFilter>(true);
                return mf != null ? mf.sharedMesh : null;
            }
        }

        public Vector2 TextureScale
        {
            get
            {
                if (!HasStockNavBall) return Vector2.one;
                Renderer r = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>() ?? StockNavBallHook.StockInstance.navBall.GetComponentInChildren<Renderer>(true);
                Material mat = (r != null) ? (r.sharedMaterial ?? r.material) : null;
                return (mat != null && mat.HasProperty("_MainTex")) ? mat.mainTextureScale : Vector2.one;
            }
        }

        public Vector2 TextureOffset
        {
            get
            {
                if (!HasStockNavBall) return Vector2.zero;
                Renderer r = StockNavBallHook.StockInstance.navBall.GetComponent<Renderer>() ?? StockNavBallHook.StockInstance.navBall.GetComponentInChildren<Renderer>(true);
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
