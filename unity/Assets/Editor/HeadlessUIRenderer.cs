using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Widgets;

namespace ModularFlightPanel.Editor
{
    /// <summary>
    /// Unity 原生无头航电 UI 渲染中枢 (Native Headless UI Renderer)
    /// 彻底脱离 KSP 运行环境，在纯原生 Unity 引擎批处理模式 (-batchmode) 下
    /// 真实实例化所有解耦的 UGUI 航电小组件、3D 姿态球相机视口与仿真物理引擎，
    /// 支持全屏幕 1080P 预览或单组件隔离绘制优化渲染切片导出。
    /// </summary>
    public static class HeadlessUIRenderer
    {
        internal class HeadlessNavBallHook : INavBallVisualHook
        {
            private readonly Texture2D _texture;
            private readonly TelemetrySimulationEngine _sim;

            public HeadlessNavBallHook(Texture2D tex, TelemetrySimulationEngine sim)
            {
                _texture = tex;
                _sim = sim;
            }

            public bool HasStockNavBall => true;
            public Mesh StockMesh => null;
            public Vector2 TextureScale => new Vector2(1f, 1f);
            public Vector2 TextureOffset => Vector2.zero;
            public Quaternion CameraRotation => Quaternion.identity;
            public Quaternion BallRotation => _sim.AttitudeRotation;
            public Texture BallTexture => _texture;
            public string HeadingText => $"HDG {Mathf.RoundToInt(_sim.Heading) % 360:D3}°";
            public string FrameName => (_texture != null && !string.IsNullOrEmpty(_texture.name)) ? _texture.name.Replace("navball_", "").ToUpperInvariant() : "SURFACE";
            public string ReferenceFrameCategory
            {
                get
                {
                    if (_texture != null && !string.IsNullOrEmpty(_texture.name))
                    {
                        string n = _texture.name.ToLowerInvariant();
                        if (n.Contains("barycentric")) return "BARYCENTRIC";
                        if (n.Contains("inertial")) return "INERTIAL";
                        if (n.Contains("target")) return "TARGET";
                        if (n.Contains("body_direction")) return "BODY_DIRECTION";
                        if (n.Contains("surface")) return "SURFACE";
                    }
                    return "SURFACE";
                }
            }
            public float HeadingAngle => _sim != null ? _sim.Heading : 0f;

            public bool GetMarkerDirection(string markerType, out Vector3 dir, out bool isVisible)
            {
                switch (markerType.ToLowerInvariant())
                {
                    case "prograde":
                        dir = new Vector3(0.04f, 0.15f, 0.98f).normalized;
                        isVisible = true;
                        return true;
                    case "retrograde":
                        dir = new Vector3(-0.04f, -0.15f, -0.98f).normalized;
                        isVisible = false;
                        return true;
                    case "normal":
                        dir = new Vector3(0f, 0.92f, 0.38f).normalized;
                        isVisible = true;
                        return true;
                    case "antinormal":
                        dir = new Vector3(0f, -0.92f, -0.38f).normalized;
                        isVisible = false;
                        return true;
                    case "radialin":
                        dir = new Vector3(-0.88f, 0f, 0.47f).normalized;
                        isVisible = false;
                        return true;
                    case "radialout":
                        dir = new Vector3(0.88f, 0f, 0.47f).normalized;
                        isVisible = true;
                        return true;
                    case "target":
                        dir = new Vector3(0.32f, 0.38f, 0.86f).normalized;
                        isVisible = true;
                        return true;
                    case "maneuver":
                        dir = new Vector3(-0.28f, 0.42f, 0.86f).normalized;
                        isVisible = true;
                        return true;
                    default:
                        dir = Vector3.forward;
                        isVisible = false;
                        return false;
                }
            }
        }

        private static void SafeWriteAllBytes(string path, byte[] bytes)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush();
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HeadlessUIRenderer] Write warning for {path}: {ex.Message}");
                    return;
                }
            }
        }

        [MenuItem("ModularFlightPanel/Render Headless Preview")]
        public static void RenderHeadlessPreview()
        {
            Debug.Log("[HeadlessUIRenderer] === Starting Native Unity Headless UI Render ===");

            // 1. 定位项目根目录
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            AppPathHelper.SetRootPath(projectRoot);
            Debug.Log($"[HeadlessUIRenderer] Project Root Path: {projectRoot}");

            // 2. 加载着色器与初始化配置
            AssetLoader.LoadBundle();
            ThemeManager.Instance.Initialize();

            // 解析命令行参数 (支持按组件单独隔离绘制优化与切片导出: -targetWidget <widgetId>, 蓝幕/绿幕高反差背景: -screen <blue|green|dark>)
            string targetWidgetId = null;
            string screenType = "blue"; // 默认工业级蓝幕，高反差突显半透明玻璃UI与激光矢量标度
            string[] cmdArgs = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < cmdArgs.Length; i++)
            {
                if ((cmdArgs[i] == "-targetWidget" || cmdArgs[i] == "--widget" || cmdArgs[i] == "-widget") && i + 1 < cmdArgs.Length)
                {
                    targetWidgetId = cmdArgs[i + 1].Trim();
                }
                if ((cmdArgs[i] == "-screen" || cmdArgs[i] == "--screen") && i + 1 < cmdArgs.Length)
                {
                    screenType = cmdArgs[i + 1].Trim().ToLowerInvariant();
                }
                if ((cmdArgs[i] == "-renderMode" || cmdArgs[i] == "--renderMode") && i + 1 < cmdArgs.Length)
                {
                    string rm = cmdArgs[i + 1].Trim().ToLowerInvariant();
                    if (rm.Contains("proc")) ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                    else if (rm.Contains("tex")) ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Texture;
                }
            }

            // 3. 构建高保真机载遥测物理仿真引擎 (音障爬升场景：跨音速、大推力、气动压力抬升)
            TelemetrySimulationEngine simEngine = new TelemetrySimulationEngine();
            simEngine.ApplyScenario(FlightScenario.AscentTransonic);
            FlightTelemetryContext.Current = simEngine;

            // 4. 挂载真实姿态球纹理
            Texture2D navballTex = null;
            string navballPath = Path.Combine(projectRoot, "navball_barycentric.png");
            if (File.Exists(navballPath))
            {
                byte[] imgBytes = File.ReadAllBytes(navballPath);
                navballTex = new Texture2D(512, 256, TextureFormat.RGBA32, false);
                navballTex.name = "navball_barycentric";
                navballTex.LoadImage(imgBytes);
                navballTex.name = "navball_barycentric";
                navballTex.filterMode = FilterMode.Trilinear;
                Debug.Log($"[HeadlessUIRenderer] Loaded Navball Texture from {navballPath}");
            }
            NavBallHookService.Provider = new HeadlessNavBallHook(navballTex, simEngine);

            // 5. 创建专用 1080P 离屏渲染相机与 RenderTexture
            const int width = 1920;
            const int height = 1080;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "HeadlessPreview_RT",
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();

            GameObject camObj = new GameObject("Headless_RenderCamera", typeof(Camera));
            Camera renderCam = camObj.GetComponent<Camera>();
            renderCam.clearFlags = CameraClearFlags.SolidColor;

            // 设定蓝幕/绿幕/深空高反差背景 (避免背景色干扰，彻底凸显半透明航电面板轮廓与边界)
            Color backdropColor;
            switch (screenType)
            {
                case "green":
                    backdropColor = new Color(0.0f, 0.694f, 0.251f, 1.0f); // 影视工业级 Chroma Green (#00B140)
                    break;
                case "dark":
                case "black":
                    backdropColor = new Color(0.015f, 0.022f, 0.035f, 1.0f); // 纯深色夜光背景
                    break;
                case "blue":
                default:
                    backdropColor = new Color(0.0f, 0.278f, 0.733f, 1.0f); // 影视工业级纯色蓝幕 Chroma Blue (#0047BB)
                    break;
            }
            renderCam.backgroundColor = backdropColor;
            Debug.Log($"[HeadlessUIRenderer] Camera backdrop screen color set to: {screenType} ({backdropColor})");

            renderCam.orthographic = true;
            renderCam.orthographicSize = height * 0.5f;
            renderCam.nearClipPlane = 0.1f;
            renderCam.farClipPlane = 1000f;
            renderCam.cullingMask = ~(1 << 31); // 排除 3D 姿态球图层 (其由独立离屏摄像机渲染到纹理)
            renderCam.targetTexture = rt;

            // 6. 实例化 HUD 根管理器与全套 UGUI 组件
            GameObject hudHost = new GameObject("NavballHUD_HeadlessHost", typeof(NavballHUD));
            NavballHUD hud = hudHost.GetComponent<NavballHUD>();
            hud.Initialize(renderCam);

            if (!string.IsNullOrEmpty(targetWidgetId))
            {
                Debug.Log($"[HeadlessUIRenderer] >>> Isolating single widget for drawing optimization: {targetWidgetId}");
                EnablePreviewWidgets();
                SetWidgetState(targetWidgetId, true, 0f, 0f);
                foreach (var w in WidgetLayoutManager.Instance.CurrentLayout.Widgets)
                {
                    bool isTarget = w.WidgetId.Equals(targetWidgetId, StringComparison.OrdinalIgnoreCase);
                    w.IsEnabled = isTarget;
                    if (isTarget)
                    {
                        // 居中呈现单组件以便高清独立校验
                        w.PositionX = 0f;
                        w.PositionY = 0f;
                    }
                }
                hud.RebuildHUD();
            }
            else
            {
                EnableAllPreviewWidgets();
                hud.RebuildHUD();
            }

            // 7. 驱动遥测数据更新至所有小组件
            Canvas.ForceUpdateCanvases();

            // 刷新所有小组件遥测数值与状态 (所有标准化小组件均继承 BaseFlightWidget)
            BaseFlightWidget[] widgets = UnityEngine.Object.FindObjectsOfType<BaseFlightWidget>();
            int subCanvasCount = 0;
            foreach (var w in widgets)
            {
                w.OnUpdateTelemetry(simEngine);
                if (w.SubCanvas != null) subCanvasCount++;
            }
            Debug.Log($"[HeadlessUIRenderer] Render Optimization Metrics: {widgets.Length} active widgets, {subCanvasCount} isolated sub-canvases.");

            // 触发所有离屏相机 (如姿态球 3D Camera) 渲染至其内部 RenderTexture
            Camera[] allCameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            foreach (var c in allCameras)
            {
                if (c != renderCam)
                {
                    c.Render();
                }
            }

            // 8. 渲染主画布到 RenderTexture
            Canvas.ForceUpdateCanvases();
            renderCam.Render();

            // 9. 读取显存像素至 Texture2D 并编码为 PNG
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;
            byte[] pngBytes = tex.EncodeToPNG();

            // 10. 保存至多处目标：GameData、Artifact 路径与 tools 目录
            string gameDataOut = Path.Combine(projectRoot, "GameData", "ModularFlightPanel", "PluginData", "unity_headless_render.png");
            string artifactOut = @"C:\Users\43701\.gemini\antigravity\brain\923c5033-094b-42c4-9e9a-2c6c1bc5088a\headless_preview.png";
            string toolsOut = Path.Combine(projectRoot, "tools", "unity_headless_render.png");

            SafeWriteAllBytes(gameDataOut, pngBytes);
            Debug.Log($"[HeadlessUIRenderer] Exported render to: {gameDataOut}");

            SafeWriteAllBytes(artifactOut, pngBytes);
            Debug.Log($"[HeadlessUIRenderer] Exported render to Artifact: {artifactOut}");

            SafeWriteAllBytes(toolsOut, pngBytes);

            if (!string.IsNullOrEmpty(targetWidgetId))
            {
                byte[] targetBytes = pngBytes;
                // 查找目标组件的屏幕包围盒进行高精度中心聚焦切片导出
                BaseFlightWidget targetWidget = null;
                foreach (var w in widgets)
                {
                    if (w.Config != null && w.Config.WidgetId.Equals(targetWidgetId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetWidget = w;
                        break;
                    }
                }

                if (targetWidget != null && targetWidget.RectTransform != null)
                {
                    Vector3[] corners = new Vector3[4];
                    targetWidget.RectTransform.GetWorldCorners(corners);

                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 screenPoint = renderCam.WorldToScreenPoint(corners[c]);
                        if (screenPoint.x < minX) minX = screenPoint.x;
                        if (screenPoint.x > maxX) maxX = screenPoint.x;
                        if (screenPoint.y < minY) minY = screenPoint.y;
                        if (screenPoint.y > maxY) maxY = screenPoint.y;
                    }

                    float pad = 40f;
                    float wW = (maxX - minX) + pad * 2f;
                    float wH = (maxY - minY) + pad * 2f;
                    float side = Mathf.Max(wW, wH, 300f);

                    float cX = (minX + maxX) * 0.5f;
                    float cY = (minY + maxY) * 0.5f;

                    int startX = Mathf.Clamp(Mathf.RoundToInt(cX - side * 0.5f), 0, width - Mathf.RoundToInt(side));
                    int startY = Mathf.Clamp(Mathf.RoundToInt(cY - side * 0.5f), 0, height - Mathf.RoundToInt(side));
                    int cropSide = Mathf.Min(Mathf.RoundToInt(side), width - startX, height - startY);

                    if (cropSide > 10)
                    {
                        Color[] pixels = tex.GetPixels(startX, startY, cropSide, cropSide);
                        Texture2D cropTex = new Texture2D(cropSide, cropSide, TextureFormat.RGB24, false);
                        cropTex.SetPixels(pixels);
                        cropTex.Apply();
                        targetBytes = cropTex.EncodeToPNG();
                        UnityEngine.Object.DestroyImmediate(cropTex);
                        Debug.Log($"[HeadlessUIRenderer] Cropped high-res slice for {targetWidgetId}: {cropSide}x{cropSide} at ({startX},{startY})");
                    }
                }

                string safeName = targetWidgetId.Replace(".", "_");
                string isolatedOut = Path.Combine(projectRoot, "GameData", "ModularFlightPanel", "PluginData", $"isolated_{safeName}.png");
                string isolatedArtifact = $@"C:\Users\43701\.gemini\antigravity\brain\923c5033-094b-42c4-9e9a-2c6c1bc5088a\isolated_{safeName}.png";
                SafeWriteAllBytes(isolatedOut, targetBytes);
                SafeWriteAllBytes(isolatedArtifact, targetBytes);

                if (targetWidgetId.Equals("core.navball", StringComparison.OrdinalIgnoreCase))
                {
                    string navballPreviewArtifact = @"C:\Users\43701\.gemini\antigravity\brain\923c5033-094b-42c4-9e9a-2c6c1bc5088a\navball_preview.png";
                    SafeWriteAllBytes(navballPreviewArtifact, targetBytes);
                }
                Debug.Log($"[HeadlessUIRenderer] Exported isolated single-widget render to: {isolatedOut}");
            }

            // 11. 资源清理
            if (navballTex != null) UnityEngine.Object.DestroyImmediate(navballTex);
            UnityEngine.Object.DestroyImmediate(tex);
            if (hud.Canvas != null) UnityEngine.Object.DestroyImmediate(hud.Canvas.gameObject);
            UnityEngine.Object.DestroyImmediate(camObj);
            UnityEngine.Object.DestroyImmediate(hudHost);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Debug.Log("[HeadlessUIRenderer] === Headless UI Render Completed Successfully! ===");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void EnablePreviewWidgets()
        {
            SetWidgetState("core.navball", true, 0f, 0f);
            SetWidgetState("core.heading_arc", true, 0f, 0f);
            SetWidgetState("gauge.throttle", true, -158f, 0f);
            SetWidgetState("gauge.barometer", true, 158f, 0f);
            SetWidgetState("tape.speed", true, -120f, 0f);
            SetWidgetState("tape.altitude", true, 120f, 0f);
            SetWidgetState("core.bottom_controls", true, 0f, -88f);
            SetWidgetState("core.sas_dial", true, 0f, -150f);
            SetWidgetState("core.stage_control", true, -340f, -120f);

            // 隐藏其它非核心部件
            SetWidgetState("core.throttle", false, 0f, 0f);
            SetWidgetState("core.vsi", false, 0f, 0f);
            SetWidgetState("core.propellant", false, 0f, 0f);
            SetWidgetState("core.orbital_info", false, 0f, -128f);
            SetWidgetState("core.ecam_status", false, 0f, -188f);
            SetWidgetState("ecam.gforce", false, -205f, 55f);
            SetWidgetState("ecam.q", false, -205f, -48f);
            SetWidgetState("ecam.throttle", false, 205f, 55f);
            SetWidgetState("custom.nd_navigation", false, -420f, 25f);
            SetWidgetState("custom.electrical", false, -420f, -165f);
            SetWidgetState("custom.rocket", false, 420f, 95f);
            SetWidgetState("custom.signal", false, 420f, -85f);
            SetWidgetState("custom.life", false, 420f, -220f);
            SetWidgetState("gauge.stage_dv", false, 0f, 0f);
        }

        private static void SetWidgetState(string id, bool enabled, float x, float y)
        {
            var cfg = WidgetLayoutManager.Instance.GetConfig(id);
            if (cfg != null)
            {
                cfg.IsEnabled = enabled;
                cfg.PositionX = x;
                cfg.PositionY = y;
            }
            else
            {
                if (id == "core.heading_arc")
                {
                    var hArc = new WidgetConfig("core.heading_arc", "PFD 航向指示弧 (Set 2)", x, y, 1.0f)
                    {
                        WidgetType = "heading_arc",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(hArc);
                }
                else if (id == "gauge.stage_dv" || id == "stage_dv")
                {
                    var sdv = new WidgetConfig("gauge.stage_dv", "AVIONICS 分级 ΔV 与烧燃时序表", x, y, 1.0f)
                    {
                        WidgetType = "stage_dv",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(sdv);
                }
                else if (id == "core.time_warp" || id == "time_warp")
                {
                    var tw = new WidgetConfig("core.time_warp", "AVIONICS 时间加速与任务时钟", x, y, 1.0f)
                    {
                        WidgetType = "time_warp",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(tw);
                }
                else if (id == "core.comm_signal" || id == "comm_signal")
                {
                    var cs = new WidgetConfig("core.comm_signal", "AVIONICS 通信网络与真实天线探针", x, y, 1.0f)
                    {
                        WidgetType = "comm_signal",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cs);
                }
                else if (id == "core.toolbar" || id == "toolbar")
                {
                    var tb = new WidgetConfig("core.toolbar", "AVIONICS 现代航电折叠工具栏", x, y, 1.0f)
                    {
                        WidgetType = "toolbar",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(tb);
                }
                else if (id == "gauge.throttle")
                {
                    var thr = new WidgetConfig("gauge.throttle", "AVIONICS 油门推力带", x, y, 1.0f)
                    {
                        WidgetType = "bar_gauge",
                        NumericToken = "{THROTTLE}",
                        MinValue = 0f,
                        MaxValue = 100f,
                        IsLeftOrientation = true,
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(thr);
                }
                else if (id == "gauge.barometer")
                {
                    var baro = new WidgetConfig("gauge.barometer", "AVIONICS 大气压强带", x, y, 1.0f)
                    {
                        WidgetType = "bar_gauge",
                        NumericToken = "{ATM}",
                        MinValue = 0f,
                        MaxValue = 1.0f,
                        UnitLabel = "atm",
                        IsLeftOrientation = false,
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(baro);
                }
                else if (id == "custom.nd_navigation")
                {
                    var nd = new WidgetConfig("custom.nd_navigation", "AERO ND 综合导航屏 (Set 1)", x, y, 1.0f)
                    {
                        WidgetType = "nd_navigation",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(nd);
                }
                else if (id == "core.stage_control")
                {
                    var sc = new WidgetConfig("core.stage_control", "分级与飞行操纵台", x, y, 1.0f)
                    {
                        WidgetType = "stage_control",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(sc);
                }
                else if (id == "ecam.throttle")
                {
                    var thrDial = new WidgetConfig("ecam.throttle", "ECAM 引擎推力表", x, y, 1.0f)
                    {
                        WidgetType = "ecam_dial",
                        NumericToken = "{THROTTLE}",
                        MinValue = 0.0,
                        MaxValue = 100.0,
                        CautionThreshold = 85.0,
                        WarningThreshold = 100.0,
                        IsSoftLimit = false,
                        LimitMode = "hard",
                        UnitLabel = "%",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(thrDial);
                }
                else if (id == "core.sas_dial")
                {
                    var sas = new WidgetConfig("core.sas_dial", "环形 SAS 罗盘", x, y, 1.0f)
                    {
                        WidgetType = "core",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(sas);
                }
            }
        }

        private static void EnableAllPreviewWidgets()
        {
            EnablePreviewWidgets();
        }
    }
}
