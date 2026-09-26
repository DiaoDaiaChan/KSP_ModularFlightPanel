using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Widgets;

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
        private static string _lastSpecAuditSummary = I18n.Tr("THM_SPEC_NOT_RUN", "未运行 (含源码级规范审计)");
        private static bool _showDockSettingsFold = false;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight));

            // =========================================================================
            // 模块 0: 语言与国际化 (Language & i18n)
            // =========================================================================
            DrawLanguageCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 1: 视觉主题预设风格 (Aero Themes)
            // =========================================================================
            DrawThemesCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 1.5: 航电显示着色器与物理字体控制 (Font & UI Shader Controls)
            // =========================================================================
            DrawDisplayShaderAndFontCard();

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
            // 模块 3.5: 底层渲染管线 (Low-Level Rendering Pipeline)
            // =========================================================================
            DrawRenderPipelineCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 模块 4: 性能探针诊断与全局主干旁路 (Profiler & Master Bypass)
            // =========================================================================
            DrawPerformanceAndSpecCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Module 0: Language & i18n

        private static void DrawLanguageCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_LANG", "🌐 语言设置"),
                I18n.Tr("THM_DESC_LANG", "实时切换航电系统界面与组件语言，即刻生效无需重启"));

            GUILayout.BeginHorizontal();

            var languages = I18nManager.Instance.AvailableLanguages;
            string currentLang = I18nManager.Instance.CurrentLanguage;

            for (int i = 0; i < languages.Count; i++)
            {
                var lang = languages[i];
                bool isCur = currentLang.Equals(lang.Code, StringComparison.OrdinalIgnoreCase);
                GUIStyle bStyle = isCur ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;

                string label = lang.Code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                    ? I18n.Tr("THM_LANG_ZH_CN", "🇨🇳 简体中文")
                    : lang.Code.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                        ? I18n.Tr("THM_LANG_EN_US", "🇺🇸 English")
                        : $"{lang.DisplayName} ({lang.Code})";

                if (GUILayout.Button(label, bStyle, GUILayout.Height(28f)))
                {
                    I18nManager.Instance.SetLanguage(lang.Code);
                    ThemeManager.Instance.SaveSettings();
                }
            }

            if (GUILayout.Button(I18n.Tr("THM_LANG_AUTO_DETECT", "🔄 自动检测语言"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(28f)))
            {
                string detected = I18nManager.Instance.DetectSystemLanguage();
                I18nManager.Instance.SetLanguage(detected);
                ThemeManager.Instance.SaveSettings();
            }

            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 1: Themes

        private static void DrawThemesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PALETTES", "🎨 视觉主题风格预设"),
                I18n.Tr("THM_SUBHEADER_PALETTES", "点击即刻全局动态换肤"));

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
                string fontDesc = current.FontStyle == AvionicsFontStyle.RetroPixel
                    ? I18n.Tr("THM_FONT_PIXEL_TAG", "硬件等宽点阵像素")
                    : I18n.Tr("THM_FONT_SMOOTH_TAG", "现代平滑矢量");
                string activeFmt = I18n.Tr("THM_ACTIVE_THEME", "当前生效主题: <color=#00E5FF><b>{0}</b></color> | 着色器模式: <color=#00FF88>{1}</color> | 航电字模: <color=#FFA502>{2}</color>");
                GUILayout.Label($"<color=#7088A8><size=11>{string.Format(activeFmt, current.DisplayName, current.UiStyle, fontDesc)}</size></color>");
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 1.5: Font & Display Shader Controls

        private static void DrawDisplayShaderAndFontCard()
        {
            var current = ThemeManager.Instance.CurrentTheme;
            if (current == null) return;

            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_FONT_SHADER", "🔤 航电字体与显示管线风格"),
                I18n.Tr("THM_SUBHEADER_FONT_SHADER", "自由切换物理微点阵、数码液晶、矢量平滑与硬件等宽像素字体"));

            // 1. 字体风格选择
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_FONT_STYLE_LABEL", "航电字模风格:")}</b>", GUILayout.Width(130f));

            bool isSmooth = current.FontStyle == AvionicsFontStyle.ModernSmooth;
            GUIStyle smoothStyle = isSmooth ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string smoothLabel = isSmooth ? I18n.Tr("THM_FONT_SMOOTH_ON", "● 现代平滑矢量 (Smooth Vector)") : I18n.Tr("THM_FONT_SMOOTH_OFF", "○ 现代平滑矢量 (Smooth Vector)");
            if (GUILayout.Button(smoothLabel, smoothStyle, GUILayout.Height(24f)))
            {
                current.FontStyle = AvionicsFontStyle.ModernSmooth;
                WidgetStyleManager.Instance.ClearMaterialCache();
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            bool isPixel = current.FontStyle == AvionicsFontStyle.RetroPixel;
            GUIStyle pixelStyle = isPixel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string pixelLabel = isPixel ? I18n.Tr("THM_FONT_PIXEL_ON", "● 硬件等宽点阵像素 (Retro Pixel)") : I18n.Tr("THM_FONT_PIXEL_OFF", "○ 硬件等宽点阵像素 (Retro Pixel)");
            if (GUILayout.Button(pixelLabel, pixelStyle, GUILayout.Height(24f)))
            {
                current.FontStyle = AvionicsFontStyle.RetroPixel;
                WidgetStyleManager.Instance.ClearMaterialCache();
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. 着色器渲染管线风格 (UiShaderStyle)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_SHADER_STYLE_LABEL", "面板着色器管线:")}</b>", GUILayout.Width(130f));

            var styles = new (UiShaderStyle style, string name)[]
            {
                (UiShaderStyle.Modern_Glass, I18n.Tr("THM_SHADER_GLASS", "现代玻璃")),
                (UiShaderStyle.Dot_Matrix, I18n.Tr("THM_SHADER_DOT", "物理微点阵")),
                (UiShaderStyle.Phosphor_HUD, I18n.Tr("THM_SHADER_HOLO", "全息磷光")),
                (UiShaderStyle.Digital_Segment, I18n.Tr("THM_SHADER_SEG", "7段数码管")),
                (UiShaderStyle.Cyber_Neon, I18n.Tr("THM_SHADER_NEON", "赛博霓虹"))
            };

            for (int i = 0; i < styles.Length; i++)
            {
                var s = styles[i];
                bool isSel = (current.UiStyle == s.style);
                GUIStyle bStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
                if (GUILayout.Button(s.name, bStyle, GUILayout.Height(24f)))
                {
                    current.UiStyle = s.style;
                    // 若切换为点阵或数码管，自动联动切换为等宽点阵像素字体，带来极致沉浸感
                    if (s.style == UiShaderStyle.Dot_Matrix || s.style == UiShaderStyle.Digital_Segment)
                    {
                        current.FontStyle = AvionicsFontStyle.RetroPixel;
                    }
                    WidgetStyleManager.Instance.ClearMaterialCache();
                    ThemeManager.Instance.SaveSettings();
                    FlightHUDManager.Instance?.RebuildHUD();
                }
            }
            GUILayout.EndHorizontal();

            // 3. 点阵屏专用参数微调 (当处于 Dot_Matrix 时显式展开)
            if (current.UiStyle == UiShaderStyle.Dot_Matrix)
            {
                GUILayout.Space(4f);
                MFPGuiSkin.BeginInset();

                // 点阵间距
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.Tr("THM_DOT_SPACING", "点阵网格间距:")}</b> <color=#00E5FF>{current.UiDotSpacing:F1} px</color>", GUILayout.Width(200f));
                float[] spacingPresets = { 0.9f, 1.3f, 1.8f, 2.5f };
                string[] spacingLabels = {
                    I18n.Tr("THM_DOT_RETINA", "0.9px 视网膜"),
                    I18n.Tr("THM_DOT_REC", "1.3px 极密 (推荐)"),
                    I18n.Tr("THM_DOT_MED", "1.8px 经典"),
                    I18n.Tr("THM_DOT_COARSE", "2.5px 粗粒")
                };
                for (int spIdx = 0; spIdx < spacingPresets.Length; spIdx++)
                {
                    float p = spacingPresets[spIdx];
                    bool isCur = Mathf.Abs(current.UiDotSpacing - p) < 0.2f;
                    GUIStyle pStyle = isCur ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(spacingLabels[spIdx], pStyle, GUILayout.Height(20f)))
                    {
                        current.UiDotSpacing = p;
                        WidgetStyleManager.Instance.ClearMaterialCache();
                        ThemeManager.Instance.SaveSettings();
                        FlightHUDManager.Instance?.RebuildHUD();
                    }
                }
                GUILayout.EndHorizontal();

                // 荧光辉光强度
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.Tr("THM_GLOW_LABEL", "荧光光晕微扩散:")}</b> <color=#00E5FF>{current.UiGlowStrength:F2}</color>", GUILayout.Width(200f));
                float[] glowPresets = { 0.15f, 0.35f, 0.50f, 0.75f };
                string[] glowLabels = {
                    I18n.Tr("THM_GLOW_OFF", "微弱"),
                    I18n.Tr("THM_GLOW_STD", "标准"),
                    I18n.Tr("THM_GLOW_WARM", "饱和"),
                    I18n.Tr("THM_GLOW_HI", "强过载")
                };
                for (int gIdx = 0; gIdx < glowPresets.Length; gIdx++)
                {
                    float gVal = glowPresets[gIdx];
                    bool isCur = Mathf.Abs(current.UiGlowStrength - gVal) < 0.08f;
                    GUIStyle gStyle = isCur ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(glowLabels[gIdx], gStyle, GUILayout.Height(20f)))
                    {
                        current.UiGlowStrength = gVal;
                        WidgetStyleManager.Instance.ClearMaterialCache();
                        ThemeManager.Instance.SaveSettings();
                        FlightHUDManager.Instance?.RebuildHUD();
                    }
                }
                GUILayout.EndHorizontal();

                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 2: Stock UI Integration

        private static void DrawStockIntegrationCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_STOCK", "🔌 KSP 原生界面深度融合控制"),
                I18n.Tr("THM_SUBHEADER_STOCK", "彻底隐藏原版老旧组件，由 MFP 航电全面接管"));

            // 1. 自定义姿态球
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isBallOn = navCfg == null || navCfg.IsEnabled;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_MODULAR_NAVBALL", "自定义 3D 姿态球:")}</b>", GUILayout.Width(240f));
            string ballText = isBallOn ? I18n.Tr("THM_NAVBALL_SHOWING", "● [显示中] 点击隐藏自定义姿态球") : I18n.Tr("THM_NAVBALL_HIDDEN", "○ [已隐藏] 点击开启自定义姿态球");
            GUIStyle ballStyle = isBallOn ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.WarningButtonStyle;
            if (GUILayout.Button(ballText, ballStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (navCfg != null) navCfg.IsEnabled = !navCfg.IsEnabled;
                else
                {
                    navCfg = new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "3D 姿态球"), 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. 原版底栏导航球
            bool hideStockBall = HarmonyPatches.IsStockNavballHidden;
            DrawStockToggleRow(I18n.Tr("THM_STOCK_NAVBALL", "KSP 原生底栏导航球:"), hideStockBall,
                I18n.Tr("THM_STOCK_NAVBALL_HIDE", "已屏蔽原生导航球"), I18n.Tr("THM_STOCK_NAVBALL_SHOW", "显示原生导航球"), val =>
            {
                HarmonyPatches.IsStockNavballHidden = val;
                StockNavBallHook.HideStockNavballCompletely(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 3. 原版顶部高度计
            bool hideStockAlti = HarmonyPatches.IsStockAltimeterHidden;
            DrawStockToggleRow(I18n.Tr("THM_STOCK_ALTI", "KSP 原生顶部高度计盒:"), hideStockAlti,
                I18n.Tr("THM_STOCK_ALTI_HIDE", "已屏蔽原生高度计"), I18n.Tr("THM_STOCK_ALTI_SHOW", "显示原生高度计"), val =>
            {
                HarmonyPatches.IsStockAltimeterHidden = val;
                StockNavBallHook.HideStockAltimeter(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 4. 原版左下操纵分级台
            bool hideStockBottom = HarmonyPatches.IsStockBottomLeftHidden;
            DrawStockToggleRow(I18n.Tr("THM_STOCK_STAGE", "KSP 原生左下操纵分级台:"), hideStockBottom,
                I18n.Tr("THM_STOCK_STAGE_HIDE", "已屏蔽原生分级操纵台"), I18n.Tr("THM_STOCK_STAGE_SHOW", "显示原生分级操纵台"), val =>
            {
                HarmonyPatches.IsStockBottomLeftHidden = val;
                StockNavBallHook.HideStockBottomLeft(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 5. 原版时间加速与时钟
            bool hideStockTime = HarmonyPatches.IsStockTimeWarpHidden;
            DrawStockToggleRow(I18n.Tr("THM_STOCK_TIME", "KSP 原生时间加速/时钟:"), hideStockTime,
                I18n.Tr("THM_STOCK_TIME_HIDE", "已屏蔽原生加速与时钟"), I18n.Tr("THM_STOCK_TIME_SHOW", "显示原生加速与时钟"), val =>
            {
                HarmonyPatches.IsStockTimeWarpHidden = val;
                StockNavBallHook.HideStockTimeWarp(val);
                ThemeManager.Instance.SaveSettings();
            });

            // 6. 原版通信信号栏
            bool hideStockComm = HarmonyPatches.IsStockCommNetHidden;
            DrawStockToggleRow(I18n.Tr("THM_STOCK_COMM", "KSP 原生通信信号栏:"), hideStockComm,
                I18n.Tr("THM_STOCK_COMM_HIDE", "已屏蔽原生 CommNet"), I18n.Tr("THM_STOCK_COMM_SHOW", "显示原生 CommNet"), val =>
            {
                HarmonyPatches.IsStockCommNetHidden = val;
                StockNavBallHook.HideStockCommNet(val);
                ThemeManager.Instance.SaveSettings();
            });

            GUILayout.Space(4f);

            // 7. 工具栏现代化模式
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_TOOLBAR_MODE", "右侧工具栏接管模式:")}</b>", GUILayout.Width(240f));
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;

            string tb0 = curTbMode == 0 ? I18n.Tr("THM_TB_CLASSIC_ON", "● 原版经典 (0)") : I18n.Tr("THM_TB_CLASSIC_OFF", "○ 原版经典 (0)");
            if (GUILayout.Button(tb0, curTbMode == 0 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 0;
                StockToolbarHook.ApplyStyleMode(0);
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            string tb1 = curTbMode == 1 ? I18n.Tr("THM_TB_SKIN_ON", "● 黑晶重肤 (1)") : I18n.Tr("THM_TB_SKIN_OFF", "○ 黑晶重肤 (1)");
            if (GUILayout.Button(tb1, curTbMode == 1 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 1;
                StockToolbarHook.ApplyStyleMode(1);
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            string tb2 = curTbMode == 2 ? I18n.Tr("THM_TB_DOCK_ON", "● 折叠收纳坞 (2)") : I18n.Tr("THM_TB_DOCK_OFF", "○ 折叠收纳坞 (2)");
            if (GUILayout.Button(tb2, curTbMode == 2 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f)))
            {
                ThemeManager.Instance.ToolbarStyleMode = 2;
                StockToolbarHook.ApplyStyleMode(2);
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
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

        private static Vector2 _dockRulesScrollPos = Vector2.zero;
        private static int _dockFilterCategory = 0; // 0 = 全部, 1 = 常用, 2 = 主坞, 3 = 隐藏

        private static void DrawDockRulesDrawer()
        {
            MFPGuiSkin.BeginInset();
            var rules = ThemeManager.Instance.DockRules;
            if (rules == null) rules = new List<DockButtonRule>();

            int favCount = 0;
            int hiddenCount = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].IsFavorite) favCount++;
                if (!rules[i].IsVisible) hiddenCount++;
            }
            int mainCount = rules.Count - favCount;
            if (mainCount < 0) mainCount = 0;

            GUILayout.BeginHorizontal();
            string foldSymbol = _showDockSettingsFold ? "▼" : "▶";
            string dockTitle = I18n.TrFormat("THM_DOCK_FILTER", rules.Count, favCount);
            GUIStyle foldBtnStyle = MFPGuiSkin.HeaderLabelStyle ?? GUI.skin.button;
            if (GUILayout.Button($"<b>{foldSymbol} {dockTitle}</b>", foldBtnStyle, GUILayout.ExpandWidth(true)))
            {
                _showDockSettingsFold = !_showDockSettingsFold;
            }

            if (GUILayout.Button(I18n.Tr("THM_DOCK_BTN_RECOMMEND", "★ 推荐常用"), MFPGuiSkin.WarningButtonStyle, GUILayout.Width(95f), GUILayout.Height(22f)))
            {
                ThemeManager.Instance.AutoRecommendFavorites();
                ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            if (GUILayout.Button(I18n.Tr("THM_BTN_SHOW_ALL", "全显"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(22f)))
            {
                foreach (var r in rules) r.IsVisible = true;
                ThemeManager.Instance.SaveSettings();
                ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            if (GUILayout.Button(I18n.Tr("THM_BTN_HIDE_ALL", "全隐"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(45f), GUILayout.Height(22f)))
            {
                foreach (var r in rules) r.IsVisible = false;
                ThemeManager.Instance.SaveSettings();
                ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
            }
            GUILayout.EndHorizontal();

            if (_showDockSettingsFold)
            {
                GUILayout.Space(6f);

                // 模块 A: 常用 MOD 独立快捷面板全局设置
                MFPGuiSkin.BeginCard();
                GUILayout.BeginHorizontal();
                bool favEnabled = GUILayout.Toggle(ThemeManager.Instance.DockEnableFavoritePanel,
                    $" <b>{I18n.Tr("THM_DOCK_FAV_ENABLE", "启用常用 MOD 独立快捷面板")}</b>", GUILayout.Width(250f));
                if (favEnabled != ThemeManager.Instance.DockEnableFavoritePanel)
                {
                    ThemeManager.Instance.DockEnableFavoritePanel = favEnabled;
                    ThemeManager.Instance.SaveSettings();
                    ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                    FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                }

                if (ThemeManager.Instance.DockEnableFavoritePanel)
                {
                    GUILayout.Label(I18n.Tr("THM_DOCK_FAV_ORIENT", "构型:"), GUILayout.Width(45f));
                    int curOrient = ThemeManager.Instance.DockFavoriteOrientation;
                    string[] orientLabels = {
                        I18n.Tr("THM_DOCK_FAV_VERT", "纵向单列"),
                        I18n.Tr("THM_DOCK_FAV_HORIZ", "横向单行"),
                        I18n.Tr("THM_DOCK_FAV_DUAL", "横向双行")
                    };
                    for (int oIdx = 0; oIdx < orientLabels.Length; oIdx++)
                    {
                        bool isSel = (curOrient == oIdx);
                        GUIStyle oStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                        if (GUILayout.Button(orientLabels[oIdx], oStyle, GUILayout.Height(20f)))
                        {
                            ThemeManager.Instance.DockFavoriteOrientation = oIdx;
                            ThemeManager.Instance.SaveSettings();
                            FavoriteToolbarWidget.Instance?.RebuildFavoritesLayout();
                        }
                    }

                    GUILayout.Space(10f);
                    bool keepInMain = GUILayout.Toggle(ThemeManager.Instance.DockKeepFavoritesInMain,
                        $" {I18n.Tr("THM_DOCK_KEEP_MAIN", "主收纳坞同时保留常用项")}");
                    if (keepInMain != ThemeManager.Instance.DockKeepFavoritesInMain)
                    {
                        ThemeManager.Instance.DockKeepFavoritesInMain = keepInMain;
                        ThemeManager.Instance.SaveSettings();
                        ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                    }
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndCard();

                GUILayout.Space(4f);

                // 模块 B: 过滤分类标签与快捷操作
                GUILayout.BeginHorizontal();
                string[] catLabels = {
                    I18n.TrFormat("THM_DOCK_TAB_ALL", rules.Count),
                    I18n.TrFormat("THM_DOCK_TAB_FAV", favCount),
                    I18n.TrFormat("THM_DOCK_TAB_MAIN", mainCount),
                    I18n.TrFormat("THM_DOCK_TAB_HIDDEN", hiddenCount)
                };
                for (int cIdx = 0; cIdx < catLabels.Length; cIdx++)
                {
                    bool isSel = (_dockFilterCategory == cIdx);
                    GUIStyle catStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(catLabels[cIdx], catStyle, GUILayout.Height(22f)))
                    {
                        _dockFilterCategory = cIdx;
                    }
                }

                GUILayout.FlexibleSpace();
                if (favCount > 0 && GUILayout.Button(I18n.Tr("THM_DOCK_BTN_CLEAR_FAV", "清空常用"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
                {
                    foreach (var r in rules) r.IsFavorite = false;
                    ThemeManager.Instance.SaveSettings();
                    ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                    FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);

                // 模块 C: 模组条目列表
                _dockRulesScrollPos = GUILayout.BeginScrollView(_dockRulesScrollPos, GUILayout.MaxHeight(260f));
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (_dockFilterCategory == 1 && !r.IsFavorite) continue;
                    if (_dockFilterCategory == 2 && r.IsFavorite && !ThemeManager.Instance.DockKeepFavoritesInMain) continue;
                    if (_dockFilterCategory == 3 && r.IsVisible) continue;

                    GUILayout.BeginHorizontal();

                    // 常用面板切换按键
                    GUIStyle favStyle = r.IsFavorite ? MFPGuiSkin.WarningButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    string favText = r.IsFavorite ? I18n.Tr("THM_DOCK_ITEM_FAV", "★ 常用") : I18n.Tr("THM_DOCK_ITEM_UNFAV", "☆ 普通");
                    if (GUILayout.Button(favText, favStyle, GUILayout.Width(62f), GUILayout.Height(20f)))
                    {
                        r.IsFavorite = !r.IsFavorite;
                        if (r.IsFavorite) r.IsVisible = true;
                        ThemeManager.Instance.SaveSettings();
                        ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                        FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                    }

                    // 显隐开关
                    bool vis = GUILayout.Toggle(r.IsVisible, "", GUILayout.Width(20f));
                    if (vis != r.IsVisible)
                    {
                        r.IsVisible = vis;
                        ThemeManager.Instance.SaveSettings();
                        ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                        FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                    }

                    // 模组 Key 与名称
                    GUILayout.Label($"<b>{r.Key}</b> <color=#7088A8>({r.DefaultName})</color>", GUILayout.Width(200f));

                    // 自定义别名文本框
                    GUILayout.Label(I18n.Tr("THM_DOCK_ALIAS_PLACEHOLDER", "别名:"), GUILayout.Width(35f));
                    string newLabel = GUILayout.TextField(r.CustomLabel ?? "", GUILayout.Width(75f));
                    if (newLabel != (r.CustomLabel ?? ""))
                    {
                        r.CustomLabel = newLabel;
                        ThemeManager.Instance.SaveSettings();
                        ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                        FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            MFPGuiSkin.EndInset();
        }

        #endregion

        #region Module 3: Navball Quality & Resolution

        private static void DrawNavballQualityCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_QUALITY", "🎯 姿态球生成模式与视网膜超采样"));

            // 1. 姿态球渲染模式选择器 (3 模式全量选择器)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_NAVBALL_MODE_LABEL", "渲染模式选择:")}</b>", GUILayout.Width(110f));

            var curMode = ThemeManager.Instance.GlobalRenderMode;
            if (curMode == NavballRenderMode.ProceduralBake) curMode = NavballRenderMode.ProceduralVector;

            // 0: 原版贴图 (Stock Texture)
            bool isStockTex = (curMode == NavballRenderMode.StockTexture);
            string stockTexLabel = isStockTex ? I18n.Tr("THM_MODE_STOCK_TEX_ON", "● 原版贴图") : I18n.Tr("THM_MODE_STOCK_TEX_OFF", "○ 原版贴图");
            if (GUILayout.Button(stockTexLabel, isStockTex ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.StockTexture;
                ThemeManager.Instance.SaveSettings();
            }

            // 1: 程序化导航球 (Procedural Vector)
            bool isProcVec = (curMode == NavballRenderMode.ProceduralVector);
            string procVecLabel = isProcVec ? I18n.Tr("THM_MODE_PROC_VEC_ON", "● 程序化导航球") : I18n.Tr("THM_MODE_PROC_VEC_OFF", "○ 程序化导航球");
            if (GUILayout.Button(procVecLabel, isProcVec ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.ProceduralVector;
                ThemeManager.Instance.SaveSettings();
            }

            // 3: 原版导航球 (Stock Direct)
            bool isStockDirect = (curMode == NavballRenderMode.StockDirect);
            string stockDirectLabel = isStockDirect ? I18n.Tr("THM_MODE_STOCK_DIRECT_ON", "● 原版导航球") : I18n.Tr("THM_MODE_STOCK_DIRECT_OFF", "○ 原版导航球");
            if (GUILayout.Button(stockDirectLabel, isStockDirect ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.StockDirect;
                ThemeManager.Instance.SaveSettings();
            }

            GUILayout.EndHorizontal();

            // 模式特性与架构说明视窗
            MFPGuiSkin.BeginInset();
            string modeDesc = "";
            switch (curMode)
            {
                case NavballRenderMode.StockTexture:
                    modeDesc = I18n.Tr("THM_MODE_STOCK_TEX_DESC", "• 采用 KSP 原版 / TextureReplacer 材质贴图，经单四边形 GPU 光线投射与硬件双线性采样实时映射，100% 还原官方质感，零摄像机与 3D 网格开销。");
                    break;
                case NavballRenderMode.ProceduralVector:
                    modeDesc = I18n.Tr("THM_MODE_PROC_VEC_DESC", "• 单四边形 GPU 光线步进纯数学矢量直出，超清视网膜级画质，全字号/刻度边缘平滑无畸变，零摄像机与 3D 网格开销。");
                    break;
                case NavballRenderMode.StockDirect:
                    modeDesc = I18n.Tr("THM_MODE_STOCK_DIRECT_DESC", "• 直接调用官方 3D 导航球，剔除侧边仪表与装饰杂物仅保留纯净姿态球；无缝接入 MFP 编辑模式，支持拖拽与 8 向拉动手柄放大缩小。");
                    break;
            }
            GUILayout.Label($"<color=#00E5FF><b>{I18n.Tr("THM_MODE_INFO", "当前特性:")}</b></color> <color=#7088A8><size=11>{modeDesc}</size></color>");
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 2. 超采样倍率
            var renderMgr = WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                float currentRenderScale = renderMgr.GlobalRenderScaleMultiplier;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.Tr("THM_RENDER_SCALE", "渲染倍率:")}</b> <color=#00E5FF>{currentRenderScale:F2}x</color>", GUILayout.Width(220f));

                float[] presets = new float[] { 0.8f, 1.0f, 1.25f, 1.5f, 2.0f };
                string[] presetLabels = new string[] { I18n.Tr("THM_SCALE_08", "0.8x 节能"), I18n.Tr("THM_SCALE_10", "1.0x 原生"), I18n.Tr("THM_SCALE_125", "1.25x 细腻"), I18n.Tr("THM_SCALE_15", "1.5x 视网膜"), I18n.Tr("THM_SCALE_20", "2.0x 极致") };
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

                string screenDiag = I18n.TrFormat("THM_DIAG_SCREEN", "• 物理屏幕: <color=#00FF88>{0} x {1} px</color> (Canvas: <color=#00E5FF>{2:F2}x</color>) | 3D 姿态球屏幕占用: <color=#FFAA00>~{3:F0} x {4:F0} px</color>", screenW, screenH, canvasScale, ballPhysical, ballPhysical);
                GUILayout.Label(screenDiag);

                float vramMb = optimalTex * optimalTex * 4 / (1024f * 1024f);
                string texDiag = I18n.TrFormat("THM_DIAG_TEX", "• 动态分配贴图: <color=#00FF88>{0} x {1} px</color> (显存占用: ~{2:F2} MB) | 其余 34 个航电小组件为原生 UGUI 1:1 满血矢量输出。", optimalTex, optimalTex, vramMb);
                GUILayout.Label(texDiag);
                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 3.5: Low-Level Render Pipeline

        private static void DrawRenderPipelineCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PIPELINE", "🚀 底层航电渲染管线 (Low-Level Rendering Pipeline)"));

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_PIPELINE_MODE_LABEL", "渲染管线模式:")}</b>", GUILayout.Width(110f));

            bool isGpu = ThemeManager.Instance != null && ThemeManager.Instance.EnableGpu2DUIAcceleration;

            // 1: 现代 2D GPU 程序化管线
            string gpuLabel = isGpu ? I18n.Tr("THM_PIPELINE_GPU_ON", "● 现代 GPU 单 Quad 程序化管线") : I18n.Tr("THM_PIPELINE_GPU_OFF", "○ 现代 GPU 单 Quad 程序化管线");
            if (GUILayout.Button(gpuLabel, isGpu ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f)))
            {
                if (ThemeManager.Instance != null && !isGpu)
                {
                    ThemeManager.Instance.EnableGpu2DUIAcceleration = true;
                    ThemeManager.Instance.SaveSettings();
                    WidgetStyleManager.Instance?.ClearMaterialCache();
                    ThemeManager.Instance.SetTheme(ThemeManager.Instance.CurrentTheme);
                }
            }

            // 0: 经典 UGUI 网格兼容管线
            string uguiLabel = !isGpu ? I18n.Tr("THM_PIPELINE_UGUI_ON", "● 经典 UGUI 网格兼容管线") : I18n.Tr("THM_PIPELINE_UGUI_OFF", "○ 经典 UGUI 网格兼容管线");
            if (GUILayout.Button(uguiLabel, !isGpu ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f)))
            {
                if (ThemeManager.Instance != null && isGpu)
                {
                    ThemeManager.Instance.EnableGpu2DUIAcceleration = false;
                    ThemeManager.Instance.SaveSettings();
                    WidgetStyleManager.Instance?.ClearMaterialCache();
                    ThemeManager.Instance.SetTheme(ThemeManager.Instance.CurrentTheme);
                }
            }

            GUILayout.EndHorizontal();

            MFPGuiSkin.BeginInset();
            string desc = isGpu
                ? I18n.Tr("THM_PIPELINE_GPU_DESC", "• [现代 GPU 单 Quad 程序化管线] 启用底层着色器加速，背景卡片面板与仪表图元由片元着色器纯数学求值，零额外 Mesh 顶点与画布重绘开销，性能极致通透。")
                : I18n.Tr("THM_PIPELINE_UGUI_DESC", "• [经典 UGUI 网格兼容管线] 使用标准 Unity UGUI 原生图像与网格渲染，提供最广泛的老旧显卡与传统模式兼容性。");
            GUILayout.Label(desc, MFPGuiSkin.SubtitleStyle);
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 4: Performance & SPEC Validator

        private static void DrawPerformanceAndSpecCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PROFILER", "⚡ 性能探针诊断与全局主干旁路"));

            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUIStyle bypassStyle = bypassed ? MFPGuiSkin.DangerButtonStyle : MFPGuiSkin.SuccessButtonStyle;
            string bypassLabel = bypassed ? I18n.Tr("THM_BYPASS_ON", "● [已完全旁路] 所有 MFP 逻辑/渲染已关闭 (0.00ms)") : I18n.Tr("THM_BYPASS_OFF", "○ [正常运行中] 点击完全 Bypass (或按 F11) 查看原生纯净性能");
            if (GUILayout.Button(bypassLabel, bypassStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance.SaveSettings();
            }

            GUIStyle hudStyle = MFPProfiler.ShowOverlay ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
            string hudText = MFPProfiler.ShowOverlay ? I18n.Tr("THM_HIDE_PROFILER", "✔ 隐藏性能探针 HUD (F10)") : I18n.Tr("THM_SHOW_PROFILER", "显示性能探针 HUD (F10)");
            if (GUILayout.Button(hudText, hudStyle, GUILayout.Width(180f), GUILayout.Height(26f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 规范审计
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_SPEC_AUDIT", "组件规范自检:")}</b> {_lastSpecAuditSummary}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(I18n.Tr("THM_BTN_RUN_SPEC", "运行 MFP-SPEC 规范审计"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(190f), GUILayout.Height(24f)))
            {
                WidgetValidationReport auditReport = WidgetSpecificationValidator.RunDevelopmentAudit();
                _lastSpecAuditSummary = auditReport.IsCompliant
                    ? I18n.TrFormat("THM_SPEC_OK", "✔ 合规 ({0} 组件 / {1} 项检查 / 告警 {2})", auditReport.TotalWidgetsAudited, auditReport.TotalChecksPerformed, auditReport.WarningCount)
                    : I18n.TrFormat("THM_SPEC_FAIL", "✘ 违规 {0} 项 / 告警 {1} 项", auditReport.ErrorCount, auditReport.WarningCount);
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        #endregion
    }
}
