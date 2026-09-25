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
            public Quaternion BallRotation => Quaternion.Euler(12f, 0f, 0f);
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
                        if (n.Contains("barycentric") || n.Contains("lagrange") || n.Contains("pulsating") || n.Contains("l1") || n.Contains("l2")) return "LAGRANGE";
                        if (n.Contains("inertial") || n.Contains("non_rotating")) return "INERTIAL";
                        if (n.Contains("orbit")) return "ORBIT";
                        if (n.Contains("target") || n.Contains("dock")) return "TARGET";
                        if (n.Contains("body_direction")) return "BODY_DIRECTION";
                        if (n.Contains("body_fixed") || n.Contains("body_surface") || n.Contains("rotating") || n.Contains("fixed")) return "BODY_FIXED";
                        if (n.Contains("surface") || n.Contains("ground")) return "SURFACE";
                    }
                    return "SURFACE";
                }
            }
            public float HeadingAngle => _sim != null ? _sim.Heading : 0f;

            public static bool MarkerDemoMode = false;

            public bool GetMarkerDirection(string markerType, out Vector3 dir, out bool isVisible)
            {
                if (MarkerDemoMode)
                {
                    switch (markerType.ToLowerInvariant())
                    {
                        case "prograde":
                            // 顺向标记 (Orbital Prograde): 放置在左上方清爽空域，荧光黄绿 (#8FE800)
                            dir = new Vector3(-0.28f, 0.22f, 0.93f).normalized;
                            isVisible = true;
                            return true;
                        case "velocity_vector":
                            // 速度矢量标记 (Flight Path Marker): 放置在右上方清爽空域，翡翠薄荷绿 (#2EE59D)
                            dir = new Vector3(0.28f, 0.22f, 0.93f).normalized;
                            isVisible = true;
                            return true;
                        case "retrograde":
                        case "anti_velocity_vector":
                            dir = new Vector3(0f, -0.20f, -0.98f).normalized;
                            isVisible = false;
                            return true;
                        case "normal":
                            dir = new Vector3(0f, 0.58f, 0.81f).normalized;
                            isVisible = true;
                            return true;
                        case "antinormal":
                            dir = new Vector3(0f, -0.58f, 0.81f).normalized;
                            isVisible = true;
                            return true;
                        case "radialin":
                            dir = new Vector3(-0.58f, 0f, 0.81f).normalized;
                            isVisible = true;
                            return true;
                        case "radialout":
                            dir = new Vector3(0.58f, 0f, 0.81f).normalized;
                            isVisible = true;
                            return true;
                        default:
                            dir = Vector3.forward;
                            isVisible = false;
                            return false;
                    }
                }

                switch (markerType.ToLowerInvariant())
                {
                    case "prograde":
                        dir = new Vector3(-0.10f, 0.18f, 0.98f).normalized;
                        isVisible = true;
                        return true;
                    case "velocity_vector":
                        dir = new Vector3(0.12f, 0.14f, 0.98f).normalized;
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

        private class HeadlessStageIconHook : IStockStageIconProvider
        {
            private Texture2D _atlas;
            public HeadlessStageIconHook()
            {
                _atlas = StageIconAtlasGenerator.GetAtlas();
            }

            public Texture StockAtlas => _atlas;
            public bool HasStockAtlas => _atlas != null;
            public Rect GetStockIconUv(string iconType) => StageIconAtlasGenerator.GetIconUv(iconType);
            public Rect GetStockIconUv(int iconIndex) => StageIconAtlasGenerator.GetIconUv(iconIndex);
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
            string targetFrameType = "surface";
            string targetPreset = null;
            string targetTheme = null;
            string targetScenario = null;
            string artifactDir = @"C:\Users\43701\.gemini\antigravity\brain\18f4211a-21db-4b60-901c-abc74392326e";
            string outputName = "unity_headless_render.png";
            int animFrames = 0;
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
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                }
                if ((cmdArgs[i] == "-frame" || cmdArgs[i] == "--frame") && i + 1 < cmdArgs.Length)
                {
                    string f = cmdArgs[i + 1].Trim().ToLowerInvariant();
                    targetFrameType = f;
                }
                if ((cmdArgs[i] == "-preset" || cmdArgs[i] == "--preset" || cmdArgs[i] == "-layout" || cmdArgs[i] == "--layout" || cmdArgs[i] == "-file" || cmdArgs[i] == "--file") && i + 1 < cmdArgs.Length)
                {
                    targetPreset = cmdArgs[i + 1].Trim();
                }
                if ((cmdArgs[i] == "-scenario" || cmdArgs[i] == "--scenario") && i + 1 < cmdArgs.Length)
                {
                    targetScenario = cmdArgs[i + 1].Trim().ToLowerInvariant();
                }
                if ((cmdArgs[i] == "-theme" || cmdArgs[i] == "--theme") && i + 1 < cmdArgs.Length)
                {
                    targetTheme = cmdArgs[i + 1].Trim();
                }
                if ((cmdArgs[i] == "-artifactDir" || cmdArgs[i] == "--artifactDir") && i + 1 < cmdArgs.Length)
                {
                    artifactDir = cmdArgs[i + 1].Trim();
                }
                if ((cmdArgs[i] == "-outputName" || cmdArgs[i] == "--outputName") && i + 1 < cmdArgs.Length)
                {
                    outputName = cmdArgs[i + 1].Trim();
                }
                if ((cmdArgs[i] == "-animFrames" || cmdArgs[i] == "--animFrames") && i + 1 < cmdArgs.Length)
                {
                    int.TryParse(cmdArgs[i + 1].Trim(), out animFrames);
                }
            }

            if (!string.IsNullOrEmpty(targetTheme))
            {
                ThemeManager.Instance.SetTheme(targetTheme);
                Debug.Log($"[HeadlessUIRenderer] Applied Theme: {targetTheme}");
            }

            // 3. 构建高保真机载遥测物理仿真引擎
            TelemetrySimulationEngine simEngine = new TelemetrySimulationEngine();
            bool isSpaceXMode = (!string.IsNullOrEmpty(targetScenario) && targetScenario.Contains("spacex")) ||
                                (!string.IsNullOrEmpty(targetPreset) && targetPreset.ToLowerInvariant().Contains("spacex")) ||
                                (!string.IsNullOrEmpty(targetWidgetId) && targetWidgetId.ToLowerInvariant().Contains("spacex"));

            if (isSpaceXMode)
            {
                simEngine.ApplyScenario(FlightScenario.MECOAndStaging);
                simEngine.SetFlightParameters(5642.0 / 3.6, 132000.0, 14f, 90f, 0.95f, 5, 6, 504.0);
            }
            else if (!string.IsNullOrEmpty(targetScenario))
            {
                if (targetScenario.IndexOf("pad", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.PadHold);
                else if (targetScenario.IndexOf("power", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.PowerCrisis);
                else if (targetScenario.IndexOf("reentry", StringComparison.OrdinalIgnoreCase) >= 0 || targetScenario.IndexOf("blackout", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.ReentryBlackout);
                else if (targetScenario.IndexOf("maxq", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.MaxQ);
                else if (targetScenario.IndexOf("meco", StringComparison.OrdinalIgnoreCase) >= 0 || targetScenario.IndexOf("stage", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.MECOAndStaging);
                else if (targetScenario.IndexOf("cruise", StringComparison.OrdinalIgnoreCase) >= 0 || targetScenario.IndexOf("orbit", StringComparison.OrdinalIgnoreCase) >= 0)
                    simEngine.ApplyScenario(FlightScenario.OrbitalCruise);
                else if (targetScenario.IndexOf("marker", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    HeadlessNavBallHook.MarkerDemoMode = true;
                    simEngine.ApplyScenario(FlightScenario.OrbitalCruise);
                }
                else
                    simEngine.ApplyScenario(FlightScenario.AscentTransonic);
            }
            else if (!string.IsNullOrEmpty(targetWidgetId) && targetWidgetId.IndexOf("maneuver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                simEngine.ApplyScenario(FlightScenario.OrbitalCruise);
            }
            else
            {
                simEngine.ApplyScenario(FlightScenario.AscentTransonic);
            }
            FlightTelemetryContext.Current = simEngine;

            // 4. 挂载真实姿态球纹理
            Texture2D navballTex = null;
            string navballPath = Path.Combine(projectRoot, "navball_barycentric.png");
            if (File.Exists(navballPath))
            {
                byte[] imgBytes = File.ReadAllBytes(navballPath);
                navballTex = new Texture2D(512, 256, TextureFormat.RGBA32, false);
                navballTex.name = "navball_" + targetFrameType;
                navballTex.LoadImage(imgBytes);
                navballTex.name = "navball_" + targetFrameType;
                navballTex.filterMode = FilterMode.Trilinear;
                Debug.Log($"[HeadlessUIRenderer] Loaded Navball Texture for frame {targetFrameType}");
            }
            NavBallHookService.Provider = new HeadlessNavBallHook(navballTex, simEngine);
            StockStageIconService.Provider = new HeadlessStageIconHook();
            StockStageActionService.Provider = new HeadlessStageActionHook(simEngine);

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
            GameObject hudHost = new GameObject("FlightHUDManager_HeadlessHost", typeof(FlightHUDManager));
            FlightHUDManager hud = hudHost.GetComponent<FlightHUDManager>();
            hud.Initialize(renderCam);

            bool triggerSep = false;
            bool triggerEng = false;
            bool triggerNode = false;
            bool triggerMeco = false;
            bool triggerDeorb = false;
            bool triggerEsc = false;
            bool forceM2 = false;
            bool forceM3 = false;
            bool triggerSts = false;

            if (!string.IsNullOrEmpty(targetPreset))
            {
                string presetPath = targetPreset;
                if (!File.Exists(presetPath))
                {
                    string pRoot = Path.Combine(projectRoot, targetPreset);
                    if (File.Exists(pRoot)) presetPath = pRoot;
                    else
                    {
                        string p1 = Path.Combine(projectRoot, "GameData", "ModularFlightPanel", "PluginData", "Presets", targetPreset);
                        if (File.Exists(p1)) presetPath = p1;
                        else if (File.Exists(p1 + ".json")) presetPath = p1 + ".json";
                    }
                }

                if (File.Exists(presetPath))
                {
                    string pJson = File.ReadAllText(presetPath);
                    var layout = JsonUtility.FromJson<WidgetLayoutData>(pJson);
                    if (layout != null && layout.Widgets != null && layout.Widgets.Count > 0)
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = layout.GlobalScale;
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets = layout.Widgets;
                        Debug.Log($"[HeadlessUIRenderer] Applied Preset: {presetPath} with {layout.Widgets.Count} widgets.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[HeadlessUIRenderer] Preset not found: {targetPreset}");
                }
                hud.RebuildHUD();
            }
            else if (!string.IsNullOrEmpty(targetWidgetId))
            {
                string cleanTargetId = targetWidgetId;

                bool loop = true;
                while (loop)
                {
                    loop = false;
                    if (cleanTargetId.EndsWith(".sep", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_sep", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 4);
                        triggerSep = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".eng", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_eng", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 4);
                        triggerEng = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".node", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_node", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 5);
                        triggerNode = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".meco", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_meco", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 5);
                        triggerMeco = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".deorb", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_deorb", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 6);
                        triggerDeorb = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".esc", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_esc", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 4);
                        triggerEsc = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_m2", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 3);
                        forceM2 = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".m3", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_m3", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 3);
                        forceM3 = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith("_3mod", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 5);
                        forceM3 = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith("_2mod", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 5);
                        forceM2 = true;
                        loop = true;
                    }
                    else if (cleanTargetId.EndsWith(".sts", StringComparison.OrdinalIgnoreCase) || cleanTargetId.EndsWith("_sts", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTargetId = cleanTargetId.Substring(0, cleanTargetId.Length - 4);
                        triggerSts = true;
                        loop = true;
                    }
                }

                Debug.Log($"[HeadlessUIRenderer] >>> Isolating single widget for drawing optimization: {cleanTargetId} (sep={triggerSep}, eng={triggerEng}, node={triggerNode}, meco={triggerMeco}, deorb={triggerDeorb}, esc={triggerEsc}, m2={forceM2}, m3={forceM3})");
                if (cleanTargetId.Equals("core.toolbar", StringComparison.OrdinalIgnoreCase) || cleanTargetId.Equals("toolbar", StringComparison.OrdinalIgnoreCase) ||
                    cleanTargetId.Equals("core.dock_favorites", StringComparison.OrdinalIgnoreCase) || cleanTargetId.Equals("dock_favorites", StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.Instance.ToolbarStyleMode = 2;
                    ThemeManager.Instance.DockEnableFavoritePanel = true;
                }
                EnablePreviewWidgets();
                SetWidgetState(cleanTargetId, true, 0f, 0f);
                foreach (var w in WidgetLayoutManager.Instance.CurrentLayout.Widgets)
                {
                    bool isTarget = w.WidgetId.Equals(cleanTargetId, StringComparison.OrdinalIgnoreCase);
                    w.IsEnabled = isTarget;
                    if (isTarget)
                    {
                        // 居中呈现单组件以便高清独立校验
                        w.PositionX = 0f;
                        w.PositionY = 0f;
                    }
                }
                hud.RebuildHUD();

                BaseFlightWidget[] earlyWidgets = UnityEngine.Object.FindObjectsOfType<BaseFlightWidget>();
                foreach (var ew in earlyWidgets)
                {
                    if (ew is MasterWarningWidget mww)
                    {
                        if (forceM2) mww.SetModulesCount(2);
                        if (forceM3) mww.SetModulesCount(3);
                        if (triggerSep) mww.TriggerBanner(MasterWarningWidget.BannerEventType.Separation, true);
                        else if (triggerEng) mww.TriggerBanner(MasterWarningWidget.BannerEventType.EngineStart, true);
                        else if (triggerNode) mww.TriggerBanner(MasterWarningWidget.BannerEventType.ManeuverApproach, true);
                        else if (triggerMeco) mww.TriggerBanner(MasterWarningWidget.BannerEventType.MECO, true);
                        else if (triggerDeorb) mww.TriggerBanner(MasterWarningWidget.BannerEventType.Deorbit, true);
                        else if (triggerEsc) mww.TriggerBanner(MasterWarningWidget.BannerEventType.Escape, true);
                    }
                }
            }
            else
            {
                EnableAllPreviewWidgets();
                hud.RebuildHUD();
            }

            // 7. 驱动遥测数据更新至所有小组件
            Canvas.ForceUpdateCanvases();

            // 模拟高保真航电性能剖面数据 (供探针与性能面板在无头批处理下展示满幅真实读数)
            MFPProfiler.InjectSimulatedMetrics(0.38, 0.05, 0.12, 0.17, 0.03, 0.01, 60.0f, 134.5, 12, 0);

            // 刷新所有小组件遥测数值与状态 (所有标准化小组件均继承 BaseFlightWidget)
            BaseFlightWidget[] widgets = UnityEngine.Object.FindObjectsOfType<BaseFlightWidget>();
            int subCanvasCount = 0;
            foreach (var w in widgets)
            {
                if (w is MasterWarningWidget mww2 && !string.IsNullOrEmpty(targetWidgetId))
                {
                    if (forceM2) mww2.SetModulesCount(2);
                    if (forceM3) mww2.SetModulesCount(3);

                    if (triggerSep) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.Separation, true);
                    else if (triggerEng) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.EngineStart, true);
                    else if (triggerNode) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.ManeuverApproach, true);
                    else if (triggerMeco) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.MECO, true);
                    else if (triggerDeorb) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.Deorbit, true);
                    else if (triggerEsc) mww2.TriggerBanner(MasterWarningWidget.BannerEventType.Escape, true);
                }
                if (w is ModularFlightPanel.UI.Widgets.EcamAlertLogWidget ecamWidget && !string.IsNullOrEmpty(targetWidgetId) && targetWidgetId.IndexOf("ecam", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ecamWidget.PushLogEntry(new ModularFlightPanel.UI.Widgets.EcamLogEntry(
                        ModularFlightPanel.UI.Widgets.EcamAlertSeverity.Caution, "DEORB", "▲",
                        ModularFlightPanel.Core.I18n.Tr("WIDGET_ALERT_DEORBIT", "飞船离轨"), "Pe 32km", "+00:14:22", 862.0));
                    ecamWidget.PushLogEntry(new ModularFlightPanel.UI.Widgets.EcamLogEntry(
                        ModularFlightPanel.UI.Widgets.EcamAlertSeverity.Advisory, "NODE", "◆",
                        ModularFlightPanel.Core.I18n.Tr("WIDGET_ALERT_MANEUVER_APPROACH", "接近机动节点"), "Δv 412", "+00:08:45", 525.0));
                    ecamWidget.PushLogEntry(new ModularFlightPanel.UI.Widgets.EcamLogEntry(
                        ModularFlightPanel.UI.Widgets.EcamAlertSeverity.Memo, "STG", "●",
                        ModularFlightPanel.Core.I18n.Tr("WIDGET_ALERT_SEPARATION", "级间分级分离"), "STG 01", "+00:02:18", 138.0));
                    if (targetWidgetId.IndexOf("sts", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ecamWidget.ToggleStatusPage();
                    }
                }
                w.OnUpdateTelemetry(simEngine);
                var update = w.GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (update != null)
                {
                    update.Invoke(w, null);
                }
                var lateUpdate = w.GetType().GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (lateUpdate != null)
                {
                    lateUpdate.Invoke(w, null);
                }
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
            string gameDataOut = Path.Combine(projectRoot, "GameData", "ModularFlightPanel", "PluginData", outputName);
            string toolsOut = Path.Combine(projectRoot, "tools", outputName);

            SafeWriteAllBytes(gameDataOut, pngBytes);
            Debug.Log($"[HeadlessUIRenderer] Exported render to: {gameDataOut}");

            if (!string.IsNullOrEmpty(artifactDir))
            {
                string artifactOut = Path.Combine(artifactDir, outputName);
                SafeWriteAllBytes(artifactOut, pngBytes);
                Debug.Log($"[HeadlessUIRenderer] Exported render to Artifact: {artifactOut}");
            }

            SafeWriteAllBytes(toolsOut, pngBytes);

            if (!string.IsNullOrEmpty(targetWidgetId))
            {
                byte[] targetBytes = pngBytes;
                // 查找目标组件的屏幕包围盒进行高精度中心聚焦切片导出
                BaseFlightWidget targetWidget = null;
                foreach (var w in widgets)
                {
                    if (w.Config != null && (w.Config.WidgetId.Equals(targetWidgetId, StringComparison.OrdinalIgnoreCase) || targetWidgetId.StartsWith(w.Config.WidgetId, StringComparison.OrdinalIgnoreCase)))
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
                SafeWriteAllBytes(isolatedOut, targetBytes);
                if (!string.IsNullOrEmpty(artifactDir))
                {
                    string isolatedArtifact = Path.Combine(artifactDir, $"isolated_{safeName}.png");
                    SafeWriteAllBytes(isolatedArtifact, targetBytes);
                }

                if (targetWidgetId.Equals("core.navball", StringComparison.OrdinalIgnoreCase))
                {
                    string navballPreviewArtifact = @"C:\Users\43701\.gemini\antigravity\brain\8433aea0-30e8-4e13-bc9f-7a2a96b2c85c\navball_preview.png";
                    SafeWriteAllBytes(navballPreviewArtifact, targetBytes);
                }
                Debug.Log($"[HeadlessUIRenderer] Exported isolated single-widget render to: {isolatedOut}");

                if (animFrames > 1)
                {
                    float animDuration = 1.6f;
                    float frameStep = animDuration / animFrames;
                    for (int f = 0; f < animFrames; f++)
                    {
                        float simTime = f * frameStep;
                        ModularFlightPanel.UI.Widgets.Controls.StagingSequenceWidget.CustomAnimationTime = simTime;
                        ModularFlightPanel.UI.Widgets.Controls.StagingSequenceWidget.CustomAnimationDeltaTime = frameStep;

                        foreach (var w in widgets)
                        {
                            var update = w.GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (update != null) update.Invoke(w, null);
                            var lateUpdate = w.GetType().GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (lateUpdate != null) lateUpdate.Invoke(w, null);
                        }

                        Canvas.ForceUpdateCanvases();
                        renderCam.Render();

                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prevActive;

                        byte[] frameTargetBytes = tex.EncodeToPNG();
                        if (targetWidget != null && targetWidget.RectTransform != null)
                        {
                            Vector3[] cArr = new Vector3[4];
                            targetWidget.RectTransform.GetWorldCorners(cArr);
                            float mX = float.MaxValue, mY = float.MaxValue, maX = float.MinValue, maY = float.MinValue;
                            for (int c = 0; c < 4; c++)
                            {
                                Vector3 sp = renderCam.WorldToScreenPoint(cArr[c]);
                                if (sp.x < mX) mX = sp.x;
                                if (sp.x > maX) maX = sp.x;
                                if (sp.y < mY) mY = sp.y;
                                if (sp.y > maY) maY = sp.y;
                            }
                            float pad = 40f;
                            float wW = (maX - mX) + pad * 2f;
                            float wH = (maY - mY) + pad * 2f;
                            float side = Mathf.Max(wW, wH, 300f);
                            float cX = (mX + maX) * 0.5f;
                            float cY = (mY + maY) * 0.5f;
                            int sX = Mathf.Clamp(Mathf.RoundToInt(cX - side * 0.5f), 0, width - Mathf.RoundToInt(side));
                            int sY = Mathf.Clamp(Mathf.RoundToInt(cY - side * 0.5f), 0, height - Mathf.RoundToInt(side));
                            int cSide = Mathf.Min(Mathf.RoundToInt(side), width - sX, height - sY);
                            if (cSide > 10)
                            {
                                Color[] px = tex.GetPixels(sX, sY, cSide, cSide);
                                Texture2D cTex = new Texture2D(cSide, cSide, TextureFormat.RGB24, false);
                                cTex.SetPixels(px);
                                cTex.Apply();
                                frameTargetBytes = cTex.EncodeToPNG();
                                UnityEngine.Object.DestroyImmediate(cTex);
                            }
                        }

                        string frameFileName = $"staging_anim_frame_{f:D2}.png";
                        if (!string.IsNullOrEmpty(artifactDir))
                        {
                            SafeWriteAllBytes(Path.Combine(artifactDir, frameFileName), frameTargetBytes);
                        }
                        SafeWriteAllBytes(Path.Combine(projectRoot, "GameData", "ModularFlightPanel", "PluginData", frameFileName), frameTargetBytes);
                    }
                    Debug.Log($"[HeadlessUIRenderer] Exported {animFrames} animation frames for {targetWidgetId}");
                    ModularFlightPanel.UI.Widgets.Controls.StagingSequenceWidget.CustomAnimationTime = -1f;
                    ModularFlightPanel.UI.Widgets.Controls.StagingSequenceWidget.CustomAnimationDeltaTime = -1f;
                }
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
            SetWidgetState("core.stage_control", true, -360f, -120f);
            SetWidgetState("custom.staging_sequence", true, -360f, 110f);
            SetWidgetState("core.time_warp", true, -560f, 460f);
            SetWidgetState("custom.signal", true, 560f, 460f);

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
                else if (id == "custom.signal" || id == "signal" || id == "custom.signal_list")
                {
                    var sig = new WidgetConfig("custom.signal", "天线通信网络", x, y, 1.0f)
                    {
                        WidgetType = "signal",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(sig);
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
                else if (id == "core.dock_favorites" || id == "dock_favorites" || id == "toolbar.favorites")
                {
                    var fav = new WidgetConfig("core.dock_favorites", "AVIONICS 常用快捷工具栏", x, y, 1.0f)
                    {
                        WidgetType = "dock_favorites",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(fav);
                }
                else if (id == "core.ui_widget" || id == "ui_widget")
                {
                    var ui = new WidgetConfig("core.ui_widget", "AVIONICS 全局 UI 控制中枢", x, y, 1.0f)
                    {
                        WidgetType = "ui_widget",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(ui);
                }
                else if (id == "nav.reference_frame" || id == "nav.ref_frame" || id == "reference_frame" || id == "ref_frame")
                {
                    var rf = new WidgetConfig("nav.reference_frame", "REF FRAME 导航参考系指示卡", x, y, 1.0f)
                    {
                        WidgetType = "reference_frame",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(rf);
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
                        MinValue = 0.0f,
                        MaxValue = 100.0f,
                        CautionThreshold = 85.0f,
                        WarningThreshold = 100.0f,
                        IsSoftLimit = false,
                        LimitMode = "hard",
                        UnitLabel = "%",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(thrDial);
                }
                else if (id == "nav.vessel_navball" || id == "nav.vessel_attitude_sphere" || id == "core.vessel_navball" || id == "nav.attitude_sphere_3d")
                {
                    var vnav = new WidgetConfig(id, "3D 飞船球形姿态仪", x, y, 1.0f)
                    {
                        WidgetType = "vessel_navball",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(vnav);
                }
                else if (id == "core.sas_dial" || id == "core.sas_dial_3d")
                {
                    bool is3D = (id == "core.sas_dial_3d");
                    var sas = new WidgetConfig(id, is3D ? "3D SAS 姿态罗盘" : "环形 SAS 罗盘", x, y, 1.0f)
                    {
                        WidgetType = "core",
                        IsEnabled = enabled,
                        CustomTemplate = is3D ? "MODE=3D" : "MODE=2D"
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(sas);
                }
                else if (id == "custom.b747_eicas" || id == "core.b747_eicas" || id == "b747_eicas")
                {
                    var eicas = new WidgetConfig("custom.b747_eicas", "波音 747 EICAS 航电组件", x, y, 1.0f)
                    {
                        WidgetType = "b747_eicas",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(eicas);
                }
                else if (id == "custom.b747_lower_eicas" || id == "core.b747_lower_eicas" || id == "b747_lower_eicas")
                {
                    var leicas = new WidgetConfig("custom.b747_lower_eicas", "波音 747 下部辅助发动机 EICAS", x, y, 1.0f)
                    {
                        WidgetType = "b747_lower_eicas",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(leicas);
                }
                else if (id == "custom.perf_monitor" || id == "core.performance_monitor" || id == "performance_monitor")
                {
                    var pm = new WidgetConfig("custom.perf_monitor", "SYS PERF 航电性能探针监控屏", x, y, 1.0f)
                    {
                        WidgetType = "performance_monitor",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(pm);
                }
                else if (id == "custom.ecam_alert_log" || id == "ecam.alert_log" || id == "core.ecam_alert_log" || id == "ecam_alert_log" || id == "alert_log" || id.StartsWith("ecam.alert_log"))
                {
                    var logWidget = new WidgetConfig("ecam.alert_log", "ECAM 飞行告警与备忘日志", x, y, 1.0f)
                    {
                        WidgetType = "ecam_alert_log",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(logWidget);
                }
                else if (id == "custom.maneuver_timeline" || id == "maneuver_timeline")
                {
                    var mt = new WidgetConfig("custom.maneuver_timeline", "MANEUVER 轨道机动时序与三轴矢量轴", x, y, 1.0f)
                    {
                        WidgetType = "maneuver_timeline",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(mt);
                }
                else if (id == "custom.staging_sequence" || id == "staging_sequence" || id == "stage_sequence")
                {
                    var stg = new WidgetConfig("custom.staging_sequence", "STAGE 垂直火箭分级序列仪", x, y, 1.0f)
                    {
                        WidgetType = "staging_sequence",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(stg);
                }
                else if (id == "core.maneuver" || id == "maneuver")
                {
                    var mn = new WidgetConfig("core.maneuver", "AVIONICS 机动节点指示器", x, y, 1.0f)
                    {
                        WidgetType = "maneuver",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(mn);
                }
                else if (id == "custom.rocket" || id == "rocket2d" || id == "rocket")
                {
                    var rkt = new WidgetConfig("custom.rocket", "ROCKET 2D 分级姿态卡", x, y, 1.0f)
                    {
                        WidgetType = "rocket2d",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(rkt);
                }
                else if (id == "spacex.header" || id == "spacex_header")
                {
                    cfg = new WidgetConfig("spacex.header", "SpaceX 任务阶段与遥测顶栏", x, y, 1.0f)
                    {
                        WidgetType = "spacex_header",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.docking" || id == "spacex_docking")
                {
                    cfg = new WidgetConfig("spacex.docking", "SpaceX 对接与姿态准星 HUD", x, y, 1.0f)
                    {
                        WidgetType = "spacex_docking",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.overview" || id == "spacex_overview")
                {
                    cfg = new WidgetConfig("spacex.overview", "SpaceX 综合工况与维生监控", x, y, 1.0f)
                    {
                        WidgetType = "spacex_overview",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.bottom" || id == "spacex_bottom")
                {
                    cfg = new WidgetConfig("spacex.bottom", "SpaceX 底部控制与链路栏", x, y, 1.0f)
                    {
                        WidgetType = "spacex_bottom",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.speed" || id == "spacex_speed")
                {
                    cfg = new WidgetConfig("spacex.speed", "SPEED", x, y, 1.0f)
                    {
                        WidgetType = "spacex_arc",
                        NumericToken = "{SPD:SURF:KMH}",
                        MinValue = 0,
                        MaxValue = 28000,
                        CautionThreshold = 22000,
                        WarningThreshold = 27000,
                        LimitMode = "soft",
                        UnitLabel = "KM/H",
                        ValueDeltaThreshold = 0.05f,
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.altitude" || id == "spacex_altitude")
                {
                    cfg = new WidgetConfig("spacex.altitude", "ALTITUDE", x, y, 1.0f)
                    {
                        WidgetType = "spacex_arc",
                        NumericToken = "{ALT:ASL:KM}",
                        MinValue = 0,
                        MaxValue = 250,
                        CautionThreshold = 180,
                        WarningThreshold = 240,
                        LimitMode = "soft",
                        UnitLabel = "KM",
                        ValueDeltaThreshold = 0.05f,
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.arc" || id == "spacex_arc")
                {
                    cfg = new WidgetConfig("spacex.arc", "SpaceX 速度与高度双弧线仪", x, y, 1.0f)
                    {
                        WidgetType = "spacex_arc",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.attitude" || id == "spacex_attitude")
                {
                    cfg = new WidgetConfig("spacex.attitude", "SpaceX 极简水平仪姿态视窗", x, y, 1.0f)
                    {
                        WidgetType = "spacex_attitude",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.engine" || id == "spacex_engine" || id == "spacex.engines" || id == "spacex_engines")
                {
                    cfg = new WidgetConfig("spacex.engines", "SpaceX 引擎阵列工况矩阵", x, y, 1.0f)
                    {
                        WidgetType = "spacex_engines",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "spacex.timeline" || id == "spacex_timeline")
                {
                    cfg = new WidgetConfig("spacex.timeline", "SpaceX 时序飞行时间轴", x, y, 1.0f)
                    {
                        WidgetType = "spacex_timeline",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "custom.arc_speed_tape" || id == "arc_speed_tape" || id == "arc_tape")
                {
                    cfg = new WidgetConfig("custom.arc_speed_tape", "弧形速度带", x, y, 1.0f, "CURVATURE=0.5;RADIUS=200;SPAN=80;SIDE=LEFT;TYPE=SPEED;VAL={SPD};MODE={SPD:MODE};ACC={ACC}")
                    {
                        WidgetType = "arc_speed_tape",
                        NumericToken = "{SPD}",
                        StepInterval = 10f,
                        IsLeftOrientation = true,
                        UnitLabel = "m/s",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
                else if (id == "custom.arc_altitude_tape" || id == "arc_altitude_tape" || id == "arc_alt_tape")
                {
                    cfg = new WidgetConfig("custom.arc_altitude_tape", "弧形高度带", x, y, 1.0f, "CURVATURE=0.5;RADIUS=200;SPAN=80;SIDE=RIGHT;TYPE=ALT;VAL={ALT};MODE=ALT;BOTTOM={ALT:AGL:DIST};TREND={VSI}")
                    {
                        WidgetType = "arc_altitude_tape",
                        NumericToken = "{ALT}",
                        StepInterval = 100f,
                        IsLeftOrientation = false,
                        UnitLabel = "m",
                        IsEnabled = enabled
                    };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(cfg);
                }
            }
        }

        private static void EnableAllPreviewWidgets()
        {
            EnablePreviewWidgets();
        }
    }
}
