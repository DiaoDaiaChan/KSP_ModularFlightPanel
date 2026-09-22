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
    /// 1. 姿态旋转：直接读取官方 navBall.localRotation（不做任何数学计算，天然支持 Principia 任意参考系及载具控向点）
    /// 2. 贴图支持：直接读取官方 Renderer.sharedMaterial.mainTexture（天然兼容 TextureReplacer / Principia 动态贴图）
    /// 3. 矢量标线：官方与第三方 Mod (Trajectories, NavBallDockingAlignmentIndicator, Maneuver) 的标线原生保留
    /// </summary>
    public static class StockNavBallHook
    {
        public static NavBall StockInstance { get; set; }
        public static bool HasStockNavBall => StockInstance != null && StockInstance.navBall != null;

        public static void RegisterStockNavBall(NavBall instance)
        {
            if (instance == null) return;
            if (StockInstance != instance)
            {
                StockInstance = instance;
                Debug.Log("[ModularFlightPanel] Successfully hooked Stock NavBall instance!");
            }
        }

        public static void UnregisterStockNavBall(NavBall instance)
        {
            if (StockInstance == instance)
            {
                StockInstance = null;
            }
        }

        /// <summary>
        /// 获取经由官方/Principia 结算后的权威姿态四元数（0计算量）
        /// </summary>
        public static Quaternion GetRotation()
        {
            if (HasStockNavBall)
            {
                return StockInstance.navBall.localRotation;
            }
            return TelemetryHub.Instance != null ? TelemetryHub.Instance.AttitudeRotation : Quaternion.identity;
        }

        /// <summary>
        /// 获取官方或第三方贴图替换插件 (TextureReplacer / Principia) 加载的原版展开贴图
        /// </summary>
        public static Texture GetTexture()
        {
            if (HasStockNavBall)
            {
                Renderer r = StockInstance.navBall.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.mainTexture != null)
                {
                    return r.sharedMaterial.mainTexture;
                }
            }
            if (GameDatabase.Instance != null)
            {
                Texture2D tex = GameDatabase.Instance.GetTexture("Squad/Props/NavBall/NavBall600", false);
                if (tex != null) return tex;
            }
            return null;
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
        /// 彻底隐藏官方屏幕底栏导航球及其外壳、滑块、折叠按钮与3D球体（由模块化飞行面板统一呈现）
        /// </summary>
        public static void HideStockNavballCompletely(bool hide)
        {
            if (StockInstance == null) return;

            // 1. 隐藏官方 3D 姿态球及所有原版 3D 矢量标线 MeshRenderer
            if (StockInstance.navBall != null)
            {
                Renderer[] renderers = StockInstance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled == hide)
                    {
                        renderers[i].enabled = !hide;
                    }
                }
            }

            // 2. 隐藏官方 UI 组件 (外框底图、油门滑动条、重力计滑动条、航向读数)
            Graphic[] graphics = StockInstance.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null && graphics[i].enabled == hide)
                {
                    graphics[i].enabled = !hide;
                }
            }

            // 3. 彻底隐藏官方导航球父级容器 (底栏 RCS / SAS 按钮、折叠箭头、外壳)
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
        }
    }
}
