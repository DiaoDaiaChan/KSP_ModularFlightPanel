using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 视觉风格、姿态球开关与全局偏好配置 (Themes, Navball & Preferences)
    /// </summary>
    public static class TabThemeSettings
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _lastSpecAuditSummary = "未运行 (含源码级颜色字面量棘轮审计)";
        private static bool _showWorkshop = false;
        private static string _customThemeId = "my_custom_theme";
        private static string _customThemeName = "自定义航电主题";
        private static string _shareCodeInput = "";
        private static string _workshopStatusMsg = "";

        public static void Draw()
        {
            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 1. 核心姿态球显隐与系统原生控制 (用户重点需求: 关闭/显示自定义导航球及原版原生组件)
            GUILayout.Label("<b>1. 仪表系统显隐与原生 UI 深度融合控制 (Display & Stock UI Controls)</b>");

            var navballCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isNavballEnabled = navballCfg == null || navballCfg.IsEnabled;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>自定义 3D 姿态球 (Modular Navball):</b>", GUILayout.Width(220f));

            GUI.color = isNavballEnabled ? Color.green : Color.yellow;
            string ballBtnText = isNavballEnabled ? "● [显示中] 点击立即隐藏自定义姿态球" : "○ [已隐藏] 点击开启并显示自定义姿态球";
            if (GUILayout.Button(ballBtnText, GUILayout.Height(28f)))
            {
                if (navballCfg != null)
                {
                    navballCfg.IsEnabled = !navballCfg.IsEnabled;
                }
                else
                {
                    navballCfg = new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navballCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance.RebuildHUD();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版 Navball 视觉模型隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生底栏导航球:</b>", GUILayout.Width(220f));
            bool hideStockBall = HarmonyPatches.IsStockNavballHidden;
            GUI.color = hideStockBall ? Color.cyan : Color.white;
            string stockBallBtnText = hideStockBall ? "✔ [已彻底隐藏] 原版导航球外壳与滑块已屏蔽" : "✖ [显示原生] 原版屏幕底栏导航球正常显示";
            if (GUILayout.Button(stockBallBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockNavballHidden = !HarmonyPatches.IsStockNavballHidden;
                StockNavBallHook.HideStockNavballCompletely(HarmonyPatches.IsStockNavballHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版顶部高度计隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生顶部高度计盒:</b>", GUILayout.Width(220f));
            bool hideStockAlti = HarmonyPatches.IsStockAltimeterHidden;
            GUI.color = hideStockAlti ? Color.cyan : Color.white;
            string stockAltiBtnText = hideStockAlti ? "✔ [已彻底隐藏] 顶部高度滚轮/大气计/垂直速度表已屏蔽" : "✖ [显示原生] 原版顶部高度计与仪表盒正常显示";
            if (GUILayout.Button(stockAltiBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockAltimeterHidden = !HarmonyPatches.IsStockAltimeterHidden;
                StockNavBallHook.HideStockAltimeter(HarmonyPatches.IsStockAltimeterHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左下角分级与控制台隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生左下操纵分级台:</b>", GUILayout.Width(220f));
            bool hideStockBottom = HarmonyPatches.IsStockBottomLeftHidden;
            GUI.color = hideStockBottom ? Color.cyan : Color.white;
            string stockBottomBtnText = hideStockBottom ? "✔ [已彻底隐藏] 左下分级框/操纵量标尺/堆栈已屏蔽" : "✖ [显示原生] 原版左下角操纵与分级正常显示";
            if (GUILayout.Button(stockBottomBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockBottomLeftHidden = !HarmonyPatches.IsStockBottomLeftHidden;
                StockNavBallHook.HideStockBottomLeft(HarmonyPatches.IsStockBottomLeftHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左上角时间加速与时钟隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生时间加速/时钟:</b>", GUILayout.Width(220f));
            bool hideStockTime = HarmonyPatches.IsStockTimeWarpHidden;
            GUI.color = hideStockTime ? Color.cyan : Color.white;
            string stockTimeBtnText = hideStockTime ? "✔ [已彻底隐藏] 原生时间加速条/MET时钟已屏蔽" : "✖ [显示原生] 原生时间加速条与时钟正常显示";
            if (GUILayout.Button(stockTimeBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockTimeWarpHidden = !HarmonyPatches.IsStockTimeWarpHidden;
                StockNavBallHook.HideStockTimeWarp(HarmonyPatches.IsStockTimeWarpHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 原版左上角 CommNet 信号栏隐藏开关
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>KSP 原生通信信号栏:</b>", GUILayout.Width(220f));
            bool hideStockComm = HarmonyPatches.IsStockCommNetHidden;
            GUI.color = hideStockComm ? Color.cyan : Color.white;
            string stockCommBtnText = hideStockComm ? "✔ [已彻底隐藏] 原生 CommNet 信号条/浮窗已屏蔽" : "✖ [显示原生] 原生通信信号指示正常显示";
            if (GUILayout.Button(stockCommBtnText, GUILayout.Height(26f)))
            {
                HarmonyPatches.IsStockCommNetHidden = !HarmonyPatches.IsStockCommNetHidden;
                StockNavBallHook.HideStockCommNet(HarmonyPatches.IsStockCommNetHidden);
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 右侧工具栏现代化风格控制
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>右侧工具栏现代化风格:</b>", GUILayout.Width(200f));
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;

            // 模式 0: 原版经典
            bool isMode0 = (curTbMode == 0);
            GUI.color = isMode0 ? Color.green : Color.white;
            string text0 = isMode0 ? "● 原版经典 (Stock)" : "○ 原版经典 (Stock)";
            if (GUILayout.Button(text0, GUILayout.Height(26f)))
            {
                if (curTbMode != 0)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 0;
                    StockToolbarHook.ApplyStyleMode(0);
                    ThemeManager.Instance.SaveSettings();
                    NavballHUD.Instance?.RebuildHUD();
                }
            }

            // 模式 1: 黑晶重肤
            bool isMode1 = (curTbMode == 1);
            GUI.color = isMode1 ? Color.cyan : Color.white;
            string text1 = isMode1 ? "● 黑晶重肤 (Reskin)" : "○ 黑晶重肤 (Reskin)";
            if (GUILayout.Button(text1, GUILayout.Height(26f)))
            {
                if (curTbMode != 1)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 1;
                    StockToolbarHook.ApplyStyleMode(1);
                    ThemeManager.Instance.SaveSettings();
                    NavballHUD.Instance?.RebuildHUD();
                }
            }

            // 模式 2: 折叠收纳坞
            bool isMode2 = (curTbMode == 2);
            GUI.color = isMode2 ? Color.cyan : Color.white;
            string text2 = isMode2 ? "● 折叠收纳坞 (Dock)" : "○ 折叠收纳坞 (Dock)";
            if (GUILayout.Button(text2, GUILayout.Height(26f)))
            {
                if (curTbMode != 2)
                {
                    ThemeManager.Instance.ToolbarStyleMode = 2;
                    StockToolbarHook.ApplyStyleMode(2);
                    ThemeManager.Instance.SaveSettings();
                    NavballHUD.Instance?.RebuildHUD();
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (curTbMode == 2)
            {
                DrawDockCustomization();
            }

            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // 性能诊断与全局主干旁路 (Master Bypass & Hardware Profiler)
            GUILayout.Label("<b>2. 性能诊断与主干旁路 (Performance Profiler & Master Bypass)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUI.color = bypassed ? Color.red : Color.green;
            string bypassLabel = bypassed ? "● [已完全旁路 Bypass] 所有 MFP 逻辑/渲染已关闭 (0.00ms 开销)" : "○ [正常运行中] 点击完全 Bypass (或按 F11) 查看原生纯净性能";
            if (GUILayout.Button(bypassLabel, GUILayout.Height(28f)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>实时 MFP 帧耗时:</b> <color=#00E5FF>{MFPProfiler.AvgTotalMs:F2} ms</color> (整帧占比: {MFPProfiler.FrameBudgetPercent:F1}%) | <b>游戏帧率:</b> {MFPProfiler.CurrentFPS:F0} FPS", GUILayout.ExpandWidth(true));
            GUI.color = MFPProfiler.ShowOverlay ? Color.cyan : Color.white;
            if (GUILayout.Button(MFPProfiler.ShowOverlay ? "✔ 隐藏性能探针 HUD (F10)" : "显示性能探针 HUD (F10)", GUILayout.Width(170f), GUILayout.Height(24f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance.SaveSettings();
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 组件规范自检 (MFP-SPEC-001..007)：让规范审计可现场触发，而不是躺在代码库里的死规则
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>组件规范自检:</b> {_lastSpecAuditSummary}", GUILayout.ExpandWidth(true));
            GUI.color = Color.cyan;
            if (GUILayout.Button("运行 MFP-SPEC 规范审计", GUILayout.Width(190f), GUILayout.Height(24f)))
            {
                WidgetValidationReport auditReport = WidgetSpecificationValidator.RunDevelopmentAudit();
                _lastSpecAuditSummary = auditReport.IsCompliant
                    ? $"✔ 合规 ({auditReport.TotalWidgetsAudited} 组件 / {auditReport.TotalChecksPerformed} 项检查 / 告警 {auditReport.WarningCount})"
                    : $"✘ 违规 {auditReport.ErrorCount} 项 / 告警 {auditReport.WarningCount} 项";
                Debug.Log("[ModularFlightPanel] " + auditReport.GenerateSummary());
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // 2. 姿态球渲染生成模式
            GUILayout.Label("<b>2. 姿态球生成模式 (Navball Render Mode)</b>");
            GUILayout.BeginHorizontal();
            bool isTex = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Texture;
            GUI.color = isTex ? Color.cyan : Color.gray;
            if (GUILayout.Button(isTex ? "● 贴图模式 (原版/Principia 素材高动态增强)" : "○ 贴图模式 (原版/Principia 素材高动态增强)", GUILayout.Height(30f)))
            {
                if (!isTex)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Texture;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            bool isProc = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;
            GUI.color = isProc ? Color.cyan : Color.gray;
            if (GUILayout.Button(isProc ? "● 程序化模式 (现代超清矢量)" : "○ 程序化模式 (现代超清矢量)", GUILayout.Height(30f)))
            {
                if (!isProc)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 3. 视觉主题预设
            GUILayout.Label("<b>3. 视觉主题预设风格 (Aero Themes)</b>");
            var themes = ThemeManager.Instance.AvailableThemes;
            for (int i = 0; i < themes.Count; i++)
            {
                var t = themes[i];
                bool isCurrent = ThemeManager.Instance.CurrentTheme?.ThemeId == t.ThemeId;

                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();
                string label = isCurrent ? $"✔ <b>{t.DisplayName}</b> (当前使用中)" : $"   {t.DisplayName}";
                GUI.color = isCurrent ? Color.green : Color.white;
                if (GUILayout.Button(label, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    ThemeManager.Instance.SetTheme(t.ThemeId);
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(10f);
            DrawThemeWorkshop();

            GUILayout.Space(10f);

            // 5. 大屏物理缩放比例
            GUILayout.Label("<b>5. 界面全局物理缩放 (Display Scale)</b>");
            if (NavballHUD.Instance != null)
            {
                float currentScale = NavballHUD.Instance.CustomScale;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"尺寸比例: {currentScale:F2}x", GUILayout.Width(110f));
                float newScale = GUILayout.HorizontalSlider(currentScale, 0.7f, 1.8f);
                GUILayout.EndHorizontal();

                if (Mathf.Abs(newScale - currentScale) > 0.01f)
                {
                    NavballHUD.Instance.CustomScale = newScale;
                    NavballHUD.Instance.RebuildHUD();
                }
            }

            GUILayout.Space(10f);

            // 5. 智能自适应分辨率与超采样倍率 (Smart Resolution & Supersampling)
            GUILayout.Label("<b>5. 渲染分辨率自适应与超采样控制 (Smart Resolution & Supersampling)</b>");
            GUILayout.BeginVertical("box");

            var renderMgr = WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                // A. 刚刚好自适应渲染开关
                bool autoAdapt = renderMgr.AutoAdaptResolution;
                GUI.color = autoAdapt ? Color.green : Color.white;
                bool newAutoAdapt = GUILayout.Toggle(autoAdapt, " <b>启用自适应刚刚好渲染分辨率 (Auto-Adapt Optimal Resolution)</b>");
                GUI.color = Color.white;

                if (newAutoAdapt != autoAdapt)
                {
                    renderMgr.SetAutoAdaptResolution(newAutoAdapt);
                    ThemeManager.Instance.SaveSettings();
                }

                GUILayout.Label("<color=#AAAAAA><size=11>说明：姿态球等 3D 离屏组件通过 RenderTexture 渲染，此开关与倍率控制其 512/1024 视网膜超采样；其余 33 个航电组件为纯原生 2D UGUI 矢量与文字，天生直接以显示器物理分辨率 (1080p/2K/4K) 满血 1:1 输出。</size></color>");

                GUILayout.Space(6f);

                // B. 超采样 / 降采样倍率选择
                float currentRenderScale = renderMgr.GlobalRenderScaleMultiplier;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>渲染倍率 (Render Scale):</b> <color=#00E5FF>{currentRenderScale:F2}x</color>", GUILayout.Width(220f));

                // 快捷预设按钮
                float[] presets = new float[] { 0.8f, 1.0f, 1.25f, 1.5f, 2.0f };
                string[] presetLabels = new string[] { "0.8x 节能", "1.0x 原生", "1.25x 细腻", "1.5x 视网膜", "2.0x 极致" };
                for (int pIdx = 0; pIdx < presets.Length; pIdx++)
                {
                    float pVal = presets[pIdx];
                    bool isSelected = Mathf.Abs(currentRenderScale - pVal) < 0.05f;
                    GUI.color = isSelected ? Color.cyan : Color.white;
                    if (GUILayout.Button(presetLabels[pIdx], GUILayout.Height(22f)))
                    {
                        renderMgr.SetGlobalRenderScale(pVal);
                        ThemeManager.Instance.SaveSettings();
                    }
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                // 滑块微调
                GUILayout.BeginHorizontal();
                GUILayout.Label("滑块微调:", GUILayout.Width(70f));
                float sliderVal = GUILayout.HorizontalSlider(currentRenderScale, 0.5f, 2.0f);
                if (Mathf.Abs(sliderVal - currentRenderScale) > 0.01f)
                {
                    renderMgr.SetGlobalRenderScale(sliderVal);
                    ThemeManager.Instance.SaveSettings();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);

                // C. 实时诊断视窗 (Live Diagnostic Inspector)
                GUILayout.Label("<b>📊 动态分辨率实时诊断视窗 (Live Diagnostic Stats):</b>");
                GUI.color = new Color(0.08f, 0.12f, 0.18f, 1f);
                GUILayout.BeginVertical("box");
                GUI.color = Color.white;

                int screenW = Screen.width;
                int screenH = Screen.height;
                float canvasScale = renderMgr.GetCanvasScaleFactor();
                float ballDiameter = 150f * UIFactory.GetKspNavballUiScale() * UIFactory.GetScreenDpiScale();
                float ballPhysical = renderMgr.CalculatePhysicalPixelSize(new Vector2(ballDiameter, ballDiameter));
                int optimalTex = renderMgr.CalculateOptimalResolution(new Vector2(ballDiameter, ballDiameter));

                GUILayout.Label($"• 游戏物理屏幕: <color=#00FF88>{screenW} x {screenH} px</color>  (Canvas 缩放比率: <color=#00E5FF>{canvasScale:F2}x</color>)");
                GUILayout.Label($"• 姿态球屏幕占用: <color=#FFAA00>~{ballPhysical:F0} x {ballPhysical:F0} px</color>  (有效渲染乘数: <color=#00E5FF>{currentRenderScale:F2}x</color>)");
                GUILayout.Label($"• 动态分配贴图: <color=#00FF88>{optimalTex} x {optimalTex} px</color>  (显存占用: ~{(optimalTex * optimalTex * 4 / (1024f * 1024f)):F2} MB, 2ⁿ 对齐)");
                GUILayout.Label($"• UGUI 矢量与字体光栅化密度: <color=#00E5FF>{(2.5f * currentRenderScale):F2} dynamicPixelsPerUnit</color>");

                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static Vector2 _dockRulesScroll = Vector2.zero;
        private static bool _showDockSettingsFold = true;

        private static void DrawDockCustomization()
        {
            GUILayout.Space(6f);
            GUILayout.BeginVertical("box");

            // 1. 自动发现当前存在的模组按钮 (若无则自动装载 Mock)
#if KSP_RUNTIME
            try
            {
                if (KSP.UI.Screens.ApplicationLauncher.Instance != null)
                {
                    var launcher = KSP.UI.Screens.ApplicationLauncher.Instance;
                    var allBtns = new System.Collections.Generic.List<KSP.UI.Screens.ApplicationLauncherButton>();
                    var stockBtns = StockToolbarHook.GetStockButtons(launcher);
                    var modBtns = StockToolbarHook.GetModButtons(launcher);
                    if (stockBtns != null) allBtns.AddRange(stockBtns);
                    if (modBtns != null) allBtns.AddRange(modBtns);
                    for (int i = 0; i < allBtns.Count; i++)
                    {
                        Widgets.ModernToolbarWidget.GetButtonIdentity(allBtns[i], i, out string k, out string defName);
                        ThemeManager.Instance.GetOrCreateDockRule(k, defName);
                    }
                }
            }
            catch { }
#endif
            if (ThemeManager.Instance.DockRules.Count == 0)
            {
                string[] mockNames = { "RES", "COMM", "TIME", "MJ", "FAR", "RA", "ALARM", "MFP" };
                foreach (var m in mockNames)
                {
                    ThemeManager.Instance.GetOrCreateDockRule("MOCK_" + m, m);
                }
            }

            var rules = ThemeManager.Instance.DockRules;
            int visibleCount = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].IsVisible) visibleCount++;
            }

            // 折叠条与标题
            GUILayout.BeginHorizontal();
            string foldSymbol = _showDockSettingsFold ? "▼" : "▶";
            if (GUILayout.Button($"<b>{foldSymbol} 折叠收纳坞 (Dock) 自定义管理:</b> 显示 [{visibleCount}/{rules.Count}] 项", "label", GUILayout.ExpandWidth(true)))
            {
                _showDockSettingsFold = !_showDockSettingsFold;
            }

            // 批处理快捷按钮
            if (GUILayout.Button("✔ 全选", GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                foreach (var r in rules) r.IsVisible = true;
                ThemeManager.Instance.SaveSettings();
                Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            if (GUILayout.Button("✖ 全消", GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                foreach (var r in rules) r.IsVisible = false;
                ThemeManager.Instance.SaveSettings();
                Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            if (GUILayout.Button("↺ 重置", GUILayout.Width(50f), GUILayout.Height(20f)))
            {
                foreach (var r in rules)
                {
                    r.IsVisible = true;
                    r.CustomLabel = "";
                }
                ThemeManager.Instance.SaveSettings();
                Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            GUILayout.EndHorizontal();

            if (_showDockSettingsFold)
            {
                GUILayout.Space(4f);

                // 排布构型选择 (0 = 纵向双列, 1 = 横向双行, 2 = 横向单行)
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>排布构型:</b>", GUILayout.Width(70f));
                int curOrient = ThemeManager.Instance.DockOrientation;

                GUI.color = (curOrient == 0) ? Color.cyan : Color.white;
                if (GUILayout.Button((curOrient == 0 ? "● " : "○ ") + "纵向双列", GUILayout.Height(22f)))
                {
                    if (curOrient != 0)
                    {
                        ThemeManager.Instance.DockOrientation = 0;
                        ThemeManager.Instance.SaveSettings();
                        Widgets.ModernToolbarWidget.Instance?.RebuildDockLayout();
                    }
                }

                GUI.color = (curOrient == 1) ? Color.cyan : Color.white;
                if (GUILayout.Button((curOrient == 1 ? "● " : "○ ") + "横向双行", GUILayout.Height(22f)))
                {
                    if (curOrient != 1)
                    {
                        ThemeManager.Instance.DockOrientation = 1;
                        ThemeManager.Instance.SaveSettings();
                        Widgets.ModernToolbarWidget.Instance?.RebuildDockLayout();
                    }
                }

                GUI.color = (curOrient == 2) ? Color.cyan : Color.white;
                if (GUILayout.Button((curOrient == 2 ? "● " : "○ ") + "横向单行", GUILayout.Height(22f)))
                {
                    if (curOrient != 2)
                    {
                        ThemeManager.Instance.DockOrientation = 2;
                        ThemeManager.Instance.SaveSettings();
                        Widgets.ModernToolbarWidget.Instance?.RebuildDockLayout();
                    }
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);

                // 抽屉开关选项
                GUILayout.BeginHorizontal();
                bool drawerOn = ThemeManager.Instance.DockShowHiddenDrawer;
                GUI.color = drawerOn ? Color.cyan : Color.white;
                string drawerText = drawerOn 
                    ? "✔ 更多抽屉已开启 (隐藏项可通过坞边 '··· 更多' 抽屉访问)" 
                    : "✖ 更多抽屉已关闭 (未勾选项被彻底剔除，坞面最精简)";
                if (GUILayout.Button(drawerText, GUILayout.Height(22f)))
                {
                    ThemeManager.Instance.DockShowHiddenDrawer = !drawerOn;
                    ThemeManager.Instance.SaveSettings();
                    Widgets.ModernToolbarWidget.Instance?.RebuildDockLayout();
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);
                GUILayout.Label("<color=#888888>说明: 勾选【✔】加入收纳坞；在【显示别名】输入自定义缩写可替代图标下方名称。</color>");

                // 按钮列表滚动视口
                _dockRulesScroll = GUILayout.BeginScrollView(_dockRulesScroll, GUILayout.Height(Mathf.Min(220f, rules.Count * 28f + 10f)));
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    GUILayout.BeginHorizontal();

                    // 显隐开关
                    GUI.color = rule.IsVisible ? Color.green : Color.gray;
                    string toggleBtnText = rule.IsVisible ? "✔ 显示" : "✖ 隐藏";
                    if (GUILayout.Button(toggleBtnText, GUILayout.Width(58f), GUILayout.Height(22f)))
                    {
                        rule.IsVisible = !rule.IsVisible;
                        ThemeManager.Instance.SaveSettings();
                        Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                    }
                    GUI.color = Color.white;

                    // 模组/原名标签
                    string displayDefName = rule.DefaultName;
                    if (displayDefName.Length > 16) displayDefName = displayDefName.Substring(0, 14) + "..";
                    GUILayout.Label($"<b>{displayDefName}</b>", GUILayout.Width(130f));

                    // 自定义别名输入
                    GUILayout.Label("别名:", GUILayout.Width(35f));
                    string newLabel = GUILayout.TextField(rule.CustomLabel ?? "", GUILayout.Width(75f), GUILayout.Height(20f));
                    if (newLabel != (rule.CustomLabel ?? ""))
                    {
                        rule.CustomLabel = newLabel;
                        ThemeManager.Instance.SaveSettings();
                        Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                    }

                    // 还原单项别名按钮
                    if (!string.IsNullOrEmpty(rule.CustomLabel))
                    {
                        if (GUILayout.Button("✕", GUILayout.Width(22f), GUILayout.Height(20f)))
                        {
                            rule.CustomLabel = "";
                            ThemeManager.Instance.SaveSettings();
                            Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                        }
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private static void DrawThemeWorkshop()
        {
            var cur = ThemeManager.Instance.CurrentTheme;
            if (cur == null) return;

            bool isBuiltin = ThemeManager.Instance.IsBuiltinTheme(cur.ThemeId);

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUI.color = _showWorkshop ? Color.cyan : Color.white;
            string toggleText = _showWorkshop ? "▼ <b>4. 航电主题自由调色工坊与社区分享 (点击收起)</b>" : "▶ <b>4. 航电主题自由调色工坊与社区分享 (点击展开自由定制与分享)</b>";
            if (GUILayout.Button(toggleText, GUILayout.Height(28f)))
            {
                _showWorkshop = !_showWorkshop;
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (!_showWorkshop)
            {
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("<color=#CCCCCC><size=11>核心原理说明：底层 Shader 已由 Unity 预编译为高性能光电管线，所有调色与微粒参数实时注入材质属性（0 帧开销，无需重新编译）。支持将参数无损压缩为单行分享码在社区互传！</size></color>");

            // 1. 社区分享码导入 / 导出区
            GUILayout.Space(6f);
            GUILayout.Label("<b>1. 社区分享码互传 (Share Hub - 零依赖免编译)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUI.color = Color.green;
            if (GUILayout.Button("复制当前主题分享码", GUILayout.Height(26f)))
            {
                string code = ThemeShareHub.ExportShareCode(cur);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    _shareCodeInput = code;
                    _workshopStatusMsg = $"✔ 主题分享码已复制到系统剪贴板！长度: {code.Length} 字符。";
                }
            }
            GUI.color = Color.cyan;
            if (GUILayout.Button("从剪贴板粘贴", GUILayout.Width(110f), GUILayout.Height(26f)))
            {
                _shareCodeInput = GUIUtility.systemCopyBuffer;
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _shareCodeInput = GUILayout.TextField(_shareCodeInput, GUILayout.Height(24f));
            GUI.color = Color.yellow;
            if (GUILayout.Button("解析并导入", GUILayout.Width(100f), GUILayout.Height(24f)))
            {
                if (ThemeShareHub.TryImportShareCode(_shareCodeInput, out ThemeConfig imported, out string err))
                {
                    ThemeManager.Instance.SaveCustomTheme(imported);
                    _workshopStatusMsg = $"✔ 成功导入并激活主题: {imported.DisplayName}！";
                }
                else
                {
                    _workshopStatusMsg = $"✘ 导入失败: {err}";
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_workshopStatusMsg))
            {
                GUILayout.Label($"<color=#00E5FF><b>{_workshopStatusMsg}</b></color>");
            }
            GUILayout.EndVertical();

            // 2. 主题身份与另存为
            GUILayout.Space(6f);
            GUILayout.Label("<b>2. 自定义主题保存与管理 (Theme Identity)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUILayout.Label("主题名称:", GUILayout.Width(65f));
            _customThemeName = GUILayout.TextField(_customThemeName, GUILayout.Width(160f));
            GUILayout.Label("主题ID:", GUILayout.Width(50f));
            _customThemeId = GUILayout.TextField(_customThemeId, GUILayout.Width(120f));

            GUI.color = Color.yellow;
            if (GUILayout.Button("保存为新主题", GUILayout.Height(24f)))
            {
                if (string.IsNullOrEmpty(_customThemeId)) _customThemeId = "custom_" + DateTime.Now.Ticks;
                ThemeConfig cloned = ThemeManager.Instance.CloneTheme(cur, _customThemeId, _customThemeName);
                ThemeManager.Instance.SaveCustomTheme(cloned);
                _workshopStatusMsg = $"✔ 已保存为独立自定义主题: {_customThemeName}！";
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (!isBuiltin)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"当前处于自定义主题: <color=#00E5FF>{cur.DisplayName}</color>", GUILayout.ExpandWidth(true));
                GUI.color = Color.cyan;
                if (GUILayout.Button("覆盖保存修改", GUILayout.Width(110f)))
                {
                    ThemeManager.Instance.SaveCustomTheme(cur);
                    _workshopStatusMsg = "✔ 已覆盖保存当前自定义主题修改！";
                }
                GUI.color = Color.red;
                if (GUILayout.Button("删除此主题", GUILayout.Width(90f)))
                {
                    ThemeManager.Instance.DeleteCustomTheme(cur.ThemeId);
                    _workshopStatusMsg = "已删除该自定义主题。";
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            // 3. 硬件着色器引擎模式切换 (Shader Engine)
            GUILayout.Space(6f);
            GUILayout.Label("<b>3. 硬件着色器引擎切换 (Shader Engine Selection)</b>");
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUILayout.Label("UI 渲染模式:", GUILayout.Width(90f));
            DrawUiStyleBtn("物理点阵", UiShaderStyle.Dot_Matrix, cur);
            DrawUiStyleBtn("全息HUD", UiShaderStyle.Phosphor_HUD, cur);
            DrawUiStyleBtn("暗晶玻璃", UiShaderStyle.Modern_Glass, cur);
            DrawUiStyleBtn("赛博霓虹", UiShaderStyle.Cyber_Neon, cur);
            DrawUiStyleBtn("数码管", UiShaderStyle.Digital_Segment, cur);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("姿态球引擎:", GUILayout.Width(90f));
            DrawBallShaderBtn("半色调点阵球", "ModularFlightPanel/NavballHalftone", cur, true);
            DrawBallShaderBtn("现代矢量球", "ModularFlightPanel/NavballModern", cur, false);
            DrawBallShaderBtn("程序化全矢量", "ModularFlightPanel/NavballProcedural", cur, false);
            GUILayout.EndHorizontal();

            // 点阵与扫描线专属参数调节
            if (cur.UiStyle == UiShaderStyle.Dot_Matrix)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"点阵微粒间距: {cur.UiDotSpacing:F1}px", GUILayout.Width(150f));
                float newSpacing = GUILayout.HorizontalSlider(cur.UiDotSpacing, 3.0f, 9.0f);
                if (Mathf.Abs(newSpacing - cur.UiDotSpacing) > 0.05f)
                {
                    cur.UiDotSpacing = newSpacing;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"点阵辉光强度: {cur.UiGlowStrength:F2}", GUILayout.Width(150f));
                float newGlow = GUILayout.HorizontalSlider(cur.UiGlowStrength, 0.1f, 1.0f);
                if (Mathf.Abs(newGlow - cur.UiGlowStrength) > 0.02f)
                {
                    cur.UiGlowStrength = newGlow;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
                GUILayout.EndHorizontal();
            }
            else if (cur.UiStyle == UiShaderStyle.Phosphor_HUD)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"CRT 扫描线深度: {cur.UiScanlineStrength:F2}", GUILayout.Width(150f));
                float newScan = GUILayout.HorizontalSlider(cur.UiScanlineStrength, 0.0f, 0.45f);
                if (Mathf.Abs(newScan - cur.UiScanlineStrength) > 0.02f)
                {
                    cur.UiScanlineStrength = newScan;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"全息磷光辉光: {cur.UiGlowStrength:F2}", GUILayout.Width(150f));
                float newGlow = GUILayout.HorizontalSlider(cur.UiGlowStrength, 0.1f, 1.0f);
                if (Mathf.Abs(newGlow - cur.UiGlowStrength) > 0.02f)
                {
                    cur.UiGlowStrength = newGlow;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            // 4. 实时色彩矩阵调节 (Live Palette Sliders)
            GUILayout.Space(6f);
            GUILayout.Label("<b>4. 实时调色板 (Live Color Matrix - 拖动滑条秒级变色)</b>");
            GUILayout.BeginVertical("box");

            DrawColorEditor("主强调色 (Accent Primary - 核心指针/激活态)", ref cur.AccentPrimary);
            DrawColorEditor("副强调色 (Accent Secondary - 次级标识/刻度)", ref cur.AccentSecondary);
            DrawColorEditor("面板底板色与透明度 (Frame Background & Alpha)", ref cur.FrameBgColor);
            DrawColorEditor("面板边框色 (Frame Border)", ref cur.FrameBorderColor);
            DrawColorEditor("姿态球天际色 (Sky Color)", ref cur.SkyColor);
            DrawColorEditor("姿态球地平色 (Ground Color)", ref cur.GroundColor);
            DrawColorEditor("姿态球地平线与高光 (Horizon Line)", ref cur.HorizonLineColor);
            DrawColorEditor("姿态球经纬网格 (Grid Color)", ref cur.GridColor);
            DrawColorEditor("主读数文字色 (Text Primary)", ref cur.TextPrimaryColor);

            GUILayout.EndVertical();

            GUILayout.EndVertical();
        }

        private static void DrawUiStyleBtn(string title, UiShaderStyle style, ThemeConfig cur)
        {
            bool isSel = cur.UiStyle == style;
            GUI.color = isSel ? Color.green : Color.white;
            if (GUILayout.Button(title, GUILayout.Height(22f)))
            {
                if (cur.UiStyle != style)
                {
                    cur.UiStyle = style;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawBallShaderBtn(string title, string shaderName, ThemeConfig cur, bool halftone)
        {
            bool isSel = cur.ShaderName == shaderName;
            GUI.color = isSel ? Color.green : Color.white;
            if (GUILayout.Button(title, GUILayout.Height(22f)))
            {
                if (cur.ShaderName != shaderName)
                {
                    cur.ShaderName = shaderName;
                    cur.EnableHalftoneDither = halftone;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUI.color = Color.white;
        }

        private static void DrawColorEditor(string label, ref ColorHex ch)
        {
            Color c = ch.ToColor();
            GUILayout.BeginHorizontal();
            string hexStr = ColorUtility.ToHtmlStringRGBA(c);
            GUILayout.Label($"<color=#{hexStr}>■</color> <b>{label}:</b>", GUILayout.Width(250f));

            GUILayout.Label("R", GUILayout.Width(14f));
            float r = GUILayout.HorizontalSlider(c.r, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("G", GUILayout.Width(14f));
            float g = GUILayout.HorizontalSlider(c.g, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("B", GUILayout.Width(14f));
            float b = GUILayout.HorizontalSlider(c.b, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("A", GUILayout.Width(14f));
            float a = GUILayout.HorizontalSlider(c.a, 0f, 1f, GUILayout.Width(45f));

            GUILayout.Label($"#{hexStr}", GUILayout.Width(85f));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(r - c.r) > 0.005f || Mathf.Abs(g - c.g) > 0.005f || Mathf.Abs(b - c.b) > 0.005f || Mathf.Abs(a - c.a) > 0.005f)
            {
                ch = ColorHex.FromColor(new Color(r, g, b, a));
                ThemeManager.Instance.NotifyThemeChanged();
            }
        }
    }
}
