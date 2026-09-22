using System;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens.Flight;
using ModularFlightPanel.Config;

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
        public static NavBall StockInstance { get; set; }
        public static bool HasStockNavBall => StockInstance != null && StockInstance.navBall != null;

        private static NavBallBurnVector _cachedBurnVector;
        private static Transform _cachedManeuverTransform;

        public static void RegisterStockNavBall(NavBall instance)
        {
            if (instance == null) return;
            if (StockInstance != instance)
            {
                StockInstance = instance;
                _cachedBurnVector = null;
                _cachedManeuverTransform = null;
                Debug.Log("[ModularFlightPanel] Successfully hooked Stock NavBall instance!");
            }
        }

        public static void UnregisterStockNavBall(NavBall instance)
        {
            if (StockInstance == instance)
            {
                StockInstance = null;
                _cachedBurnVector = null;
                _cachedManeuverTransform = null;
            }
        }

        /// <summary>
        /// 获取经由官方/Principia 权威解算的姿态四元数（0计算量，采用世界坐标旋转）
        /// </summary>
        public static Quaternion GetRotation()
        {
            if (HasStockNavBall)
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
                Renderer r = StockInstance.navBall.GetComponent<Renderer>();
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
                        // 3. Unity 标准 mainTexture 属性读取
                        if (mat.mainTexture != null)
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
        /// 获取官方/Principia 矢量标线 (Prograde, Retrograde, Normal, Target, Maneuver 等) 的前向局部单位方向
        /// </summary>
        public static bool GetMarkerDirection(string markerKey, out Vector3 dir, out bool isVisible)
        {
            dir = Vector3.forward;
            isVisible = false;
            if (!HasStockNavBall) return false;

            Transform marker = null;
            switch (markerKey.ToLowerInvariant())
            {
                case "prograde":
                    marker = StockInstance.progradeVector;
                    break;
                case "retrograde":
                    marker = StockInstance.retrogradeVector;
                    break;
                case "normal":
                    marker = StockInstance.normalVector;
                    break;
                case "antinormal":
                    marker = StockInstance.antiNormalVector;
                    break;
                case "radialin":
                    marker = StockInstance.radialInVector;
                    break;
                case "radialout":
                    marker = StockInstance.radialOutVector;
                    break;
                case "target":
                    marker = StockInstance.target;
                    break;
                case "maneuver":
                    marker = GetManeuverTransform();
                    break;
            }

            if (marker == null) return false;

            Vector3 localPos = marker.localPosition;
            if (localPos.sqrMagnitude < 0.0001f) return false;

            dir = localPos.normalized;
            // 当标线在姿态球可见前半球面且处于激活态时判定为可见
            float cutoff = StockInstance.VectorUnitCutoff;
            isVisible = marker.gameObject.activeInHierarchy && (dir.z > cutoff || dir.z > -0.05f);
            return true;
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
        /// 彻底隐藏官方屏幕底栏导航球及其外壳、滑块、折叠按钮，同时保障姿态数据结算脚本正常运转
        /// </summary>
        public static void HideStockNavballCompletely(bool hide)
        {
            if (StockInstance == null) return;

            // 1. 通过 CanvasGroup 隐藏官方 UI 容器 (完全透明、阻断射线响应，但不中断其内部状态更新)
            if (FlightUIModeController.Instance != null && FlightUIModeController.Instance.navBall != null)
            {
                CanvasGroup cg = FlightUIModeController.Instance.navBall.GetComponent<CanvasGroup>();
                if (cg == null)
                {
                    cg = FlightUIModeController.Instance.navBall.gameObject.AddComponent<CanvasGroup>();
                }
                cg.alpha = hide ? 0f : 1f;
                cg.blocksRaycasts = !hide;
                cg.interactable = !hide;
            }

            // 2. 仅隐藏原版 3D 姿态球主球体网格，杜绝重叠渲染；标线物体与骨架依然在后台持续更新坐标
            if (StockInstance.navBall != null)
            {
                Renderer r = StockInstance.navBall.GetComponent<Renderer>();
                if (r != null && r.enabled == hide)
                {
                    r.enabled = !hide;
                }
            }
        }
    }
}
