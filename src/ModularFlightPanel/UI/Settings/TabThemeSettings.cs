using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新视觉风格、原生融合与系统偏好配置中枢 (Avionics Themes & System Preferences)
    /// 核心重构：
    /// 1. 5 大模块化现代黑晶卡片排版，彻底消灭冗余杂乱代码。
    /// 2. 原生 KSP UI 深度融合开关组 (Navball, Altimeter, Staging, TimeWarp, CommNet)。
    /// 3. 姿态球超清矢量/贴图生成与视网膜超采样实时诊断视窗。
    /// 4. 实时硬件帧预算探针与主干完全旁路 (Master Bypass)。
    /// 5. 交互式主题工坊 (Theme Workshop) 与分享码一键应用。
    /// </summary>
    public static class TabThemeSettings
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _lastSpecAuditSummary = "未运行 (含源码级规范审计)";
        private static bool _showWorkshop = false;
        private static bool _showDockSettingsFold = false;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // =========================================================================
            // 模块 1: 视觉主题预设风格 (Aero Themes)
            // =========================================================================
            DrawThemesCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 2: KSP 原生 UI 深度融合控制 (Stock UI Deep Integration)
            // =========================================================================
            DrawStockIntegrationCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 3: 3D 姿态球渲染生成与视网膜超采样 (Navball Generation & Supersampling)
            // =========================================================================
            DrawNavballQualityCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 4: 性能探针诊断与全局主干旁路 (Profiler & Master Bypass)
            // =========================================================================
            DrawPerformanceAndSpecCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 5: 自定义主题工坊 (Theme Workshop & Share)
            // =========================================================================
            DrawWorkshopCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Module 1: Themes

        private static void DrawThemesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🎨 视觉主题风格预设 (Visual Themes Palette)", "点击即刻全局动态换肤");

            var themes = ThemeManager.Instance.AvailableThemes;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < themes.Count; i++)
            {
                var t = themes[i];
                bool isCur = (ThemeManager.Instance.CurrentTheme?.ThemeId == t.ThemeId);
                GUIStyle bStyle = isCur ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;

                string shortName = t.DisplayName.Split('(')[0].Trim();
                if (GUILayout.Button(shortName, bStyle, GUILayout.Height(28f)))
                {
                    ThemeManager.Instance.SetTheme(t.ThemeId);
                }
            }
            GUILayout.EndHorizontal();

            var current = ThemeManager.Instance.CurrentTheme;
            if (current != null)
            {
                GUILayout.Space(4f);
                GUILayout.Label($"<color=#7088A8><size=11>当前生效主题: <color=#00E5FF><b>{current.DisplayName}</b></color> | 着色器模式: <color=#00FF88>{current.UiStyle}</color></size></color>");
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 2: Stock UI Integration

        private static void DrawStockIntegrationCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🌐 KSP 原生 UI 深度融合控制 (Stock UI Integration)", "彻底隐藏原版老旧组件，由 MFP 航电全面接管");

            // 1. 自定义姿态球
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isBallOn = navCfg == null || navCfg.IsEnabled;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>自定义 3D 姿态球 (Modular Navball):</b>", GUILayout.Width(240f));
            string ballText = isBallOn ? "● [显示中] 点击隐藏自定义姿态球" : "○ [已隐藏] 点击开启自定义姿态球";
            GUIStyle ballStyle = isBallOn ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.WarningButtonStyle;
            if (GUILayout.Button(ballText, ballStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (navCfg != null) navCfg.IsEnabled = !navCfg.IsEnabled;
                else
                {
                    navCfg = new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                NavballHUD.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. 原版底栏导航球
            bool hideStockBall = HarmonyPatches.IsStockNavballHidden;
            DrawStockToggleRow("KSP 原生底栏导航球:", hideStockBall, "已屏蔽原生导航球", "显示原生导航球", val =>
            {
                HarmonyPatches.IsStockNavballHidden = val;
                StockNavBallHook.HideStockNavballCompletely(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 3. 原版顶部高度计
            bool hideStockAlti = HarmonyPatches.IsStockAltimeterHidden;
            DrawStockToggleRow("KSP 原生顶部高度计盒:", hideStockAlti, "已屏蔽原生高度计", "显示原生高度计", val =>
            {
                HarmonyPatches.IsStockAltimeterHidden = val;
                StockNavBallHook.HideStockAltimeter(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 4. 原版左下操纵分级台
            bool hideStockBottom = HarmonyPatches.IsStockBottomLeftHidden;
            DrawStockToggleRow("KSP 原生左下操纵分级台:", hideStockBottom, "已屏蔽原生分级操纵台", "显示原生分级操纵台", val =>
            {
                HarmonyPatches.IsStockBottomLeftHidden = val;
                StockNavBallHook.HideStockBottomLeft(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 5. 原版时间加速与时钟
            bool hideStockTime = HarmonyPatches.IsStockTimeWarpHidden;
            DrawStockToggleRow("KSP 原生时间加速/时钟:", hideStockTime, "已屏蔽原生加速与时钟", "显示原生加速与时钟", val =>
            {
                HarmonyPatches.IsStockTimeWarpHidden = val;
                StockNavBallHook.HideStockTimeWarp(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 6. 原版通信信号栏
            bool hideStockComm = HarmonyPatches.IsStockCommNetHidden;
            DrawStockToggleRow("KSP 原生通信信号栏:", hideStockComm, "已屏蔽原生 CommNet", "显示原生 CommNet", val =>
            {
                HarmonyPatches.IsStockCommNetHidden = val;
                StockNavBallHook.HideStockCommNet(val);
                ThemeManager.Instance.SaveSettings();
            });

            GUILayout.Space(4f);

            // 7. 工具栏现代化模式
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>右侧工具栏接管模式:</b>", GUILayout.Width(240f));
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;

            if (GUILayout.Button(curTbMode == 0 ? "● 原版经典 (0)" : "○ 原版经典 (0)", curTbMode == 0 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 0;
                StockToolbarHook.ApplyStyleMode(0);
                ThemeManager.Instance.SaveSettings();
                NavballHUD.Instance?.RebuildHUD();
            }
            if (GUILayout.Button(curTbMode == 1 ? "● 黑晶重肤 (1)" : "○ 黑晶重肤 (1)", curTbMode == 1 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 1;
                StockToolbarHook.ApplyStyleMode(1);
                ThemeManager.Instance.SaveSettings();
                NavballHUD.Instance?.RebuildHUD();
            }
            if (GUILayout.Button(curTbMode == 2 ? "● 折叠收纳坞 (2)" : "○ 折叠收纳坞 (2)", curTbMode == 2 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 2;
                StockToolbarHook.ApplyStyleMode(2);
                ThemeManager.Instance.SaveSettings();
                NavballHUD.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            if (curTbMode == 2)
            {
                DrawDockRulesDrawer();
            }

            MFPGuiSkin.EndCard();
        }

        private static void DrawStockToggleRow(string label, bool isHidden, string hiddenText, string visibleText, Action<bool> onToggle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(240f));
            string btnText = isHidden ? $"✔ [{hiddenText}]" : $"✖ [{visibleText}]";
            GUIStyle style = isHidden ? MFPGuiSkin.SecondaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
            if (GUILayout.Button(btnText, style, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                onToggle(!isHidden);
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawDockRulesDrawer()
        {
            MFPGuiSkin.BeginInset();
            var rules = ThemeManager.Instance.DockRules;
            GUILayout.BeginHorizontal();
            string foldSymbol = _showDockSettingsFold ? "▼" : "▶";
            if (GUILayout.Button($"<b>{foldSymbol} 折叠收纳坞 (Dock) 按钮显隐过滤:</b> (共 {rules.Count} 项)", "label", GUILayout.ExpandWidth(true)))
            {
                _showDockSettingsFold = !_showDockSettingsFold;
            }

            if (GUILayout.Button("全显", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(20f)))
            {
                foreach (var r in rules) r.IsVisible = true;
                ThemeManager.Instance.SaveSettings();
                Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            if (GUILayout.Button("全隐", MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(20f)))
            {
                foreach (var r in rules) r.IsVisible = false;
                ThemeManager.Instance.SaveSettings();
                Widgets.ModernToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            GUILayout.EndHorizontal();

            if (_showDockSettingsFold)
            {
                GUILayout.Space(4f);
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    GUILayout.BeginHorizontal();
                    r.IsVisible = GUILayout.Toggle(r.IsVisible, $" {r.Key} ({(string.IsNullOrEmpty(r.CustomLabel) ? r.DefaultName : r.CustomLabel)})", GUILayout.Width(200f));
                    GUILayout.EndHorizontal();
                }
            }
            MFPGuiSkin.EndInset();
        }

        #endregion

        #region Module 3: Navball Quality & Resolution

        private static void DrawNavballQualityCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🎯 姿态球生成模式与视网膜超采样 (Navball Quality & Resolution)");

            // 1. 生成模式
            GUILayout.BeginHorizontal();
            GUILayout.Label("姿态球生成模式:", GUILayout.Width(130f));
            bool isTex = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Texture;
            bool isProc = ThemeManager.Instance.GlobalRenderMode == NavballRenderMode.Procedural;

            if (GUILayout.Button(isProc ? "● 矢量程序化模式 (现代超清)" : "○ 矢量程序化模式 (现代超清)", isProc ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                if (!isProc)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Procedural;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }

            if (GUILayout.Button(isTex ? "● 贴图增强模式 (原版材质+HDRP)" : "○ 贴图增强模式 (原版材质+HDRP)", isTex ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                if (!isTex)
                {
                    ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.Texture;
                    ThemeManager.Instance.NotifyThemeChanged();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 2. 超采样倍率
            var renderMgr = WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                float currentRenderScale = renderMgr.GlobalRenderScaleMultiplier;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>渲染倍率 (Render Scale):</b> <color=#00E5FF>{currentRenderScale:F2}x</color>", GUILayout.Width(220f));

                float[] presets = new float[] { 0.8f, 1.0f, 1.25f, 1.5f, 2.0f };
                string[] presetLabels = new string[] { "0.8x 节能", "1.0x 原生", "1.25x 细腻", "1.5x 视网膜", "2.0x 极致" };
                for (int pIdx = 0; pIdx < presets.Length; pIdx++)
                {
                    float pVal = presets[pIdx];
                    bool isSelected = Mathf.Abs(currentRenderScale - pVal) < 0.05f;
                    GUIStyle bStyle = isSelected ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(presetLabels[pIdx], bStyle, GUILayout.Height(22f)))
                    {
                        renderMgr.SetGlobalRenderScale(pVal);
                        ThemeManager.Instance.SaveSettings();
                    }
                }
                GUILayout.EndHorizontal();

                // 实时视网膜诊断视窗
                MFPGuiSkin.BeginInset();
                int screenW = Screen.width;
                int screenH = Screen.height;
                float canvasScale = renderMgr.GetCanvasScaleFactor();
                float ballDiameter = 150f * UIFactory.GetKspNavballUiScale() * UIFactory.GetScreenDpiScale();
                float ballPhysical = renderMgr.CalculatePhysicalPixelSize(new Vector2(ballDiameter, ballDiameter));
                int optimalTex = renderMgr.CalculateOptimalResolution(new Vector2(ballDiameter, ballDiameter));

                GUILayout.Label($"• 物理屏幕: <color=#00FF88>{screenW} x {screenH} px</color> (Canvas: <color=#00E5FF>{canvasScale:F2}x</color>) | 3D 姿态球屏幕占用: <color=#FFAA00>~{ballPhysical:F0} x {ballPhysical:F0} px</color>");
                GUILayout.Label($"• 动态分配贴图: <color=#00FF88>{optimalTex} x {optimalTex} px</color> (显存占用: ~{(optimalTex * optimalTex * 4 / (1024f * 1024f)):F2} MB) | 其余 34 个航电小组件为原生 UGUI 1:1 满血矢量输出。");
                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 4: Performance & SPEC Validator

        private static void DrawPerformanceAndSpecCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("⚡ 性能分析探针与主干旁路 (Profiler & Master Bypass)");

            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUIStyle bypassStyle = bypassed ? MFPGuiSkin.DangerButtonStyle : MFPGuiSkin.SuccessButtonStyle;
            string bypassLabel = bypassed ? "● [已完全旁路 Bypass] 所有 MFP 逻辑/渲染已关闭 (0.00ms)" : "○ [正常运行中] 点击完全 Bypass (或按 F11) 查看原生纯净性能";
            if (GUILayout.Button(bypassLabel, bypassStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance.SaveSettings();
            }

            GUIStyle hudStyle = MFPProfiler.ShowOverlay ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
            if (GUILayout.Button(MFPProfiler.ShowOverlay ? "✔ 隐藏性能探针 HUD (F10)" : "显示性能探针 HUD (F10)", hudStyle, GUILayout.Width(180f), GUILayout.Height(26f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 规范审计
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>组件规范自检:</b> {_lastSpecAuditSummary}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("运行 MFP-SPEC 规范审计", MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(190f), GUILayout.Height(24f)))
            {
                WidgetValidationReport auditReport = WidgetSpecificationValidator.RunDevelopmentAudit();
                _lastSpecAuditSummary = auditReport.IsCompliant
                    ? $"✔ 合规 ({auditReport.TotalWidgetsAudited} 组件 / {auditReport.TotalChecksPerformed} 项检查 / 告警 {auditReport.WarningCount})"
                    : $"✘ 违规 {auditReport.ErrorCount} 项 / 告警 {auditReport.WarningCount} 项";
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 5: Workshop

        private static void DrawWorkshopCard()
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            string fold = _showWorkshop ? "▼" : "▶";
            if (GUILayout.Button($"<b>{fold} 自定义航电主题工坊 (Theme Workshop & Color Customizer)</b>", "label", GUILayout.ExpandWidth(true)))
            {
                _showWorkshop = !_showWorkshop;
            }
            MFPGuiSkin.EndCard();

            if (!_showWorkshop) return;

            var cur = ThemeManager.Instance.CurrentTheme;
            if (cur == null) return;

            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🎨 主题调色板编辑 (Active Theme Palette)");

            DrawColorEditorRow("主强调色 (Accent Primary)", ref cur.AccentPrimary);
            DrawColorEditorRow("副强调色 (Accent Secondary)", ref cur.AccentSecondary);
            DrawColorEditorRow("面板底板色 (Frame Background)", ref cur.FrameBgColor);
            DrawColorEditorRow("面板边框色 (Frame Border)", ref cur.FrameBorderColor);
            DrawColorEditorRow("主读数文字色 (Text Primary)", ref cur.TextPrimaryColor);

            GUILayout.Space(6f);
            if (GUILayout.Button("保存主题修改", MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.SaveSettings();
                ThemeManager.Instance.NotifyThemeChanged();
            }

            MFPGuiSkin.EndCard();
        }

        private static void DrawColorEditorRow(string label, ref ColorHex ch)
        {
            Color c = ch.ToColor();
            GUILayout.BeginHorizontal();
            string hexStr = ColorUtility.ToHtmlStringRGBA(c);
            GUILayout.Label($"<color=#{hexStr}>■</color> <b>{label}:</b>", GUILayout.Width(220f));

            GUILayout.Label("R", GUILayout.Width(14f));
            float r = GUILayout.HorizontalSlider(c.r, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("G", GUILayout.Width(14f));
            float g = GUILayout.HorizontalSlider(c.g, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("B", GUILayout.Width(14f));
            float b = GUILayout.HorizontalSlider(c.b, 0f, 1f, GUILayout.Width(55f));
            GUILayout.Label("A", GUILayout.Width(14f));
            float a = GUILayout.HorizontalSlider(c.a, 0f, 1f, GUILayout.Width(45f));

            GUILayout.Label($"#{hexStr}", GUILayout.Width(75f));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(r - c.r) > 0.005f || Mathf.Abs(g - c.g) > 0.005f || Mathf.Abs(b - c.b) > 0.005f || Mathf.Abs(a - c.a) > 0.005f)
            {
                ch = ColorHex.FromColor(new Color(r, g, b, a));
                ThemeManager.Instance.NotifyThemeChanged();
            }
        }

        #endregion
    }
}
