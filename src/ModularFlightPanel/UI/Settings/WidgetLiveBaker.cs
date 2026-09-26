using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 航电组件游戏内实机离屏烘焙器 (Avionics In-Game Live Snapshot Baker)
    /// 核心职责：
    /// 1. 脱离 Unity Editor 批处理，在游戏运行时（KSP 进程内）真实无头实例化组件；
    /// 2. 使用离屏相机 (Offscreen Camera) 与独立 RenderTexture (256x256) 渲染组件真实外观；
    /// 3. 自动导出为 PluginData/isolated_{safeId}.png 永久持久化至磁盘；
    /// 4. 支持一键全量实机烘焙 (Bake All Widgets In-Game)，实时捕获当前配色主题的高清快照。
    /// </summary>
    public static class WidgetLiveBaker
    {
        private static bool _isBaking = false;
        public static bool IsBaking => _isBaking;

        /// <summary>
        /// 实机渲染单个组件一次，并保存为 isolated_{safeId}.png 到 PluginData 目录
        /// </summary>
        public static Texture2D BakeWidget(WidgetDescriptor desc)
        {
            if (desc == null) return null;

            string targetDir = Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData");
            if (!Directory.Exists(targetDir))
            {
                try { Directory.CreateDirectory(targetDir); } catch { }
            }

            string safeId = (desc.DefaultWidgetId ?? desc.TypeName ?? "widget").Replace('.', '_').ToLowerInvariant();
            string outFileName = $"isolated_{safeId}.png";
            string outFilePath = Path.Combine(targetDir, outFileName);

            const int width = 256;
            const int height = 256;

            // 1. 创建离屏 RenderTexture
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();

            // 2. 创建专用离屏渲染相机
            GameObject camObj = new GameObject("MFP_LiveBakeCamera", typeof(Camera));
            Camera cam = camObj.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 现代深空航电背板底色 (#050912)，确保高对比度荧光与激光标度清晰呈现
            cam.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1.0f);
            cam.orthographic = true;

            // 根据组件默认尺寸自适应正交视口大小，确保组件位于中心并适度留白
            float w = desc.DefaultWidth > 20f ? desc.DefaultWidth : 160f;
            float h = desc.DefaultHeight > 20f ? desc.DefaultHeight : 100f;
            float maxDim = Mathf.Max(w, h, 100f) * 1.25f;
            cam.orthographicSize = maxDim * 0.5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.cullingMask = ~(1 << 31); // 排除全局 3D 姿态球图层
            cam.targetTexture = rt;

            // 3. 创建临时无头 Canvas 与根节点
            GameObject canvasObj = new GameObject("MFP_LiveBakeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GameObject rootObj = new GameObject("MFP_LiveBakeRoot", typeof(RectTransform));
            rootObj.transform.SetParent(canvasObj.transform, false);
            RectTransform rootRt = rootObj.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;

            Texture2D resultTex = null;

            try
            {
                // 4. 实例化组件配置与组件本体
                WidgetConfig cfg = desc.CreateConfig();
                cfg.PositionX = 0f;
                cfg.PositionY = 0f;
                ThemeConfig theme = ThemeManager.Instance.CurrentTheme;

                BaseFlightWidget widget = WidgetRegistry.Spawn(cfg, theme, rootObj.transform, canvas, 1.0f);
                if (widget != null)
                {
                    // 5. 驱动遥测数据仿真 (确保仪表具备饱满的度数、指针与指示灯状态)
                    TelemetrySimulationEngine sim = TelemetryHub.Instance?.SimulationEngine ?? new TelemetrySimulationEngine();
                    sim.ApplyScenario(FlightScenario.AscentTransonic);
                    sim.SetFlightParameters(245.0, 12500.0, 15f, 90f, 0.85f, 5, 6, 450.0);

                    try
                    {
                        widget.OnUpdateTelemetry(sim);
                    }
                    catch { }

                    // 6. 强制刷新 UGUI 排版与网格
                    Canvas.ForceUpdateCanvases();

                    // 7. 渲染到 RenderTexture
                    cam.Render();

                    // 8. 读取显存像素至 Texture2D
                    RenderTexture prevActive = RenderTexture.active;
                    RenderTexture.active = rt;

                    resultTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    resultTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    resultTex.Apply();

                    RenderTexture.active = prevActive;

                    // 9. 持久化到 PluginData 目录
                    byte[] pngBytes = resultTex.EncodeToPNG();
                    File.WriteAllBytes(outFilePath, pngBytes);
                    MFPLogger.Info("WidgetLiveBaker", $"Successfully live-baked widget: {desc.TypeName} -> {outFilePath}");
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn("WidgetLiveBaker", $"Error during live bake of {desc.TypeName}: {ex.Message}");
            }
            finally
            {
                // 10. 资源清理
                if (canvasObj != null) UnityEngine.Object.DestroyImmediate(canvasObj);
                if (camObj != null) UnityEngine.Object.DestroyImmediate(camObj);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }

            return resultTex;
        }

        /// <summary>
        /// 一键实机渲染全量组件快照 (Bake All Registered Widgets In-Game)
        /// </summary>
        public static int BakeAllWidgets()
        {
            if (_isBaking) return 0;
            _isBaking = true;
            int count = 0;

            try
            {
                var descriptors = WidgetRegistry.AllDescriptors;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    var desc = descriptors[i];
                    try
                    {
                        var tex = BakeWidget(desc);
                        if (tex != null)
                        {
                            WidgetPreviewLoader.RegisterBakedTexture(desc, tex);
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Warn("WidgetLiveBaker", $"Failed baking {desc.TypeName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _isBaking = false;
                WidgetPreviewLoader.RefreshIndex();
            }

            MFPLogger.Info("WidgetLiveBaker", $"Completed full in-game bake: {count} widgets rendered.");
            return count;
        }
    }
}
