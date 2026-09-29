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
    /// 核心优化：
    /// 1. 主题分类矩阵化：4 大航电属性分类索引 + 4 列色标芯片网格，杜绝单行平铺拥挤与文字截断。
    /// 2. 原生界面 2×3 紧凑开关矩阵 + 一键批量接管/恢复，取代冗余通栏大按钮。
    /// 3. 语言栏紧凑化集成，释放首屏黄金视口。
    /// 4. 姿态球渲染引擎与底层着色管线有机合流。
    /// 5. 性能探针诊断与全量主干规范审计轻量条。
    /// </summary>
    public static class TabThemeSettings
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _lastSpecAuditSummary = I18n.Tr("THM_SPEC_NOT_RUN", "未运行 (含源码级规范审计)");
        private static bool _showDockSettingsFold = false;
        private static int _themeCategory = 0; // 0: 全部, 1: 现代商业, 2: 经典历史, 3: 战术机载, 4: 赛博科幻

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight));

            // =========================================================================
            // 模块 0: 语言与国际化紧凑条 (Language & i18n)
            // =========================================================================
            DrawLanguageCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 模块 1: 视觉主题工坊矩阵 (Aero Themes Grid)
            // =========================================================================
            DrawThemesCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 模块 1.5: 航电显示着色器与物理字体控制 (Font & UI Shader Controls)
            // =========================================================================
            DrawDisplayShaderAndFontCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 模块 2: KSP 原生 UI 深度融合控制 (Stock UI Deep Integration 2x3 Matrix)
            // =========================================================================
            DrawStockIntegrationCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 模块 3: 3D 姿态球引擎与底层渲染管线 (Navball & Pipeline Engine)
            // =========================================================================
            DrawNavballAndPipelineCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 模块 4: 性能探针诊断与全局主干校验 (Profiler & Master Bypass)
            // =========================================================================
            DrawPerformanceAndSpecCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Module 0: Language & i18n

        private static void DrawLanguageCard()
        {
            MFPGuiSkin.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_HEADER_LANG", "🌐 界面语言:")}</b>", MFPGuiSkin.SectionTitleStyle, GUILayout.Width(110f));

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
                        : $"{lang.DisplayName}";

                if (GUILayout.Button(label, bStyle, GUILayout.Height(24f), GUILayout.MinWidth(110f)))
                {
                    I18nManager.Instance.SetLanguage(lang.Code);
                    ThemeManager.Instance.SaveSettings();
                }
            }

            if (GUILayout.Button(I18n.Tr("THM_LANG_AUTO_DETECT", "🔄 自动检测语言"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.MinWidth(110f)))
            {
                string detected = I18nManager.Instance.DetectSystemLanguage();
                I18nManager.Instance.SetLanguage(detected);
                ThemeManager.Instance.SaveSettings();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("THM_DESC_LANG", "实时切换即刻生效")}</size></color>");
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 1: Themes Grid & Palette Hub

        private static int GetThemeCategoryIndex(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) return 1;
            string id = themeId.ToLowerInvariant();
            if (id.Contains("787") || id.Contains("modern") || id.Contains("dragon") || id.Contains("starship")) return 1;
            if (id.Contains("classic") || id.Contains("apollo") || id.Contains("vostok") || id.Contains("amber")) return 2;
            if (id.Contains("hud") || id.Contains("diffractive") || id.Contains("blackbird") || id.Contains("voyager") || id.Contains("deep_space")) return 3;
            if (id.Contains("neon") || id.Contains("matrix") || id.Contains("eva")) return 4;
            return 1;
        }

        private static void DrawThemesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PALETTES", "🎨 视觉主题工坊矩阵"),
                I18n.Tr("THM_SUBHEADER_PALETTES", "按分类索引，点击芯片即刻全屏动态换肤"));

            var allThemes = ThemeManager.Instance.AvailableThemes;

            // 1. 风格分类过滤药丸条
            GUILayout.BeginHorizontal();
            string[] catNames = new string[]
            {
                I18n.TrFormat("THM_CAT_ALL", allThemes.Count),
                I18n.Tr("THM_CAT_MODERN", "✈ 现代航电 (4)"),
                I18n.Tr("THM_CAT_CLASSIC", "🚀 经典历史 (5)"),
                I18n.Tr("THM_CAT_TACTICAL", "🎯 战术机载 (3)"),
                I18n.Tr("THM_CAT_SCIFI", "⚡ 赛博科幻 (4)")
            };

            for (int c = 0; c < catNames.Length; c++)
            {
                bool isSel = (_themeCategory == c);
                GUIStyle catStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(catNames[c], catStyle, GUILayout.Height(22f), GUILayout.MinWidth(85f)))
                {
                    _themeCategory = c;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            // 2. 筛选对应分类下的主题
            List<ThemeConfig> filtered = new List<ThemeConfig>();
            for (int i = 0; i < allThemes.Count; i++)
            {
                var t = allThemes[i];
                if (_themeCategory == 0 || GetThemeCategoryIndex(t.ThemeId) == _themeCategory)
                {
                    filtered.Add(t);
                }
            }

            // 3. 4 列网格排版 (彻底消灭单行挤压)
            int cols = 4;
            for (int i = 0; i < filtered.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < cols; c++)
                {
                    int idx = i + c;
                    if (idx < filtered.Count)
                    {
                        var t = filtered[idx];
                        bool isCur = (ThemeManager.Instance.CurrentTheme?.ThemeId == t.ThemeId);
                        GUIStyle bStyle = isCur ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;

                        string shortName = t.DisplayName.Split('(')[0].Trim();
                        string hexAcc = ColorUtility.ToHtmlStringRGB(t.AccentPrimary.ToColor());
                        string label = $"<color=#{hexAcc}>■</color> {shortName}";

                        if (GUILayout.Button(label, bStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                        {
                            ThemeManager.Instance.SetTheme(t.ThemeId);
                        }
                    }
                    else
                    {
                        GUILayout.Space(0f);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            // 4. 当前生效主题色板预览条
            var current = ThemeManager.Instance.CurrentTheme;
            if (current != null)
            {
                GUILayout.Space(4f);
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();

                GUILayout.Label($"<b>{I18n.Tr("THM_ACTIVE_THEME_LABEL", "当前主题:")}</b> <color=#00E5FF><b>{current.DisplayName}</b></color>", GUILayout.Width(220f));

                string cPri = ColorUtility.ToHtmlStringRGB(current.AccentPrimary.ToColor());
                string cSec = ColorUtility.ToHtmlStringRGB(current.AccentSecondary.ToColor());
                string cWarn = ColorUtility.ToHtmlStringRGB(current.WarningColor.ToColor());
                string cDang = ColorUtility.ToHtmlStringRGB(current.DangerColor.ToColor());
                GUILayout.Label($"<color=#{cPri}>■ {I18n.Tr("THM_COLOR_PRI", "主强调")}</color>  <color=#{cSec}>■ {I18n.Tr("THM_COLOR_SEC", "次色标")}</color>  <color=#{cWarn}>■ {I18n.Tr("THM_COLOR_WARN", "警戒")}</color>  <color=#{cDang}>■ {I18n.Tr("THM_COLOR_DANG", "告警")}</color>", GUILayout.Width(240f));

                GUILayout.FlexibleSpace();
                string fontDesc = current.FontStyle == AvionicsFontStyle.RetroPixel
                    ? I18n.Tr("THM_FONT_PIXEL_TAG", "点阵像素")
                    : I18n.Tr("THM_FONT_SMOOTH_TAG", "平滑矢量");
                GUILayout.Label($"<color=#7088A8><size=11>{I18n.Tr("THM_PIPELINE_LABEL", "管线:")} <color=#00FF88>{current.UiStyle}</color> | {I18n.Tr("THM_FONT_LABEL", "字模:")} <color=#FFA502>{fontDesc}</color></size></color>");
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
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
            if (GUILayout.Button(smoothLabel, smoothStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                current.FontStyle = AvionicsFontStyle.ModernSmooth;
                WidgetStyleManager.Instance.ClearMaterialCache();
                ThemeManager.Instance.SaveSettings();
                FlightHUDManager.Instance?.RebuildHUD();
            }

            bool isPixel = current.FontStyle == AvionicsFontStyle.RetroPixel;
            GUIStyle pixelStyle = isPixel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string pixelLabel = isPixel ? I18n.Tr("THM_FONT_PIXEL_ON", "● 硬件等宽点阵像素 (Retro Pixel)") : I18n.Tr("THM_FONT_PIXEL_OFF", "○ 硬件等宽点阵像素 (Retro Pixel)");
            if (GUILayout.Button(pixelLabel, pixelStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
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
                if (GUILayout.Button(s.name, bStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    current.UiStyle = s.style;
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
                GUILayout.Label($"<b>{I18n.Tr("THM_DOT_SPACING", "点阵网格间距:")}</b> <color=#00E5FF>{current.UiDotSpacing:F1} px</color>", GUILayout.Width(190f));
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
                    if (GUILayout.Button(spacingLabels[spIdx], pStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
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
                GUILayout.Label($"<b>{I18n.Tr("THM_GLOW_LABEL", "荧光光晕微扩散:")}</b> <color=#00E5FF>{current.UiGlowStrength:F2}</color>", GUILayout.Width(190f));
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
                    if (GUILayout.Button(glowLabels[gIdx], gStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
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

        #region Module 2: Stock UI Integration 2x3 Matrix

        private static void DrawStockIntegrationCard()
        {
            MFPGuiSkin.BeginCard();

            // 头部标题与批量快捷操作
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("THM_HEADER_STOCK", "🔌 KSP 原生界面深度融合控制 (2×3 紧凑矩阵)"), MFPGuiSkin.SectionTitleStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.Tr("THM_BTN_TAKEOVER_ALL", "⚡ 一键全接管 (隐藏原生)"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(180f), GUILayout.Height(22f)))
            {
                HarmonyPatches.IsStockNavballHidden = true;
                StockNavBallHook.HideStockNavballCompletely(true);
                HarmonyPatches.IsStockAltimeterHidden = true;
                StockNavBallHook.HideStockAltimeter(true);
                HarmonyPatches.IsStockBottomLeftHidden = true;
                StockNavBallHook.HideStockBottomLeft(true);
                HarmonyPatches.IsStockTimeWarpHidden = true;
                StockNavBallHook.HideStockTimeWarp(true);
                HarmonyPatches.IsStockCommNetHidden = true;
                StockNavBallHook.HideStockCommNet(true);
                ThemeManager.Instance.SaveSettings();
            }

            if (GUILayout.Button(I18n.Tr("THM_BTN_RESTORE_STOCK", "🔄 恢复原生默认"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(120f), GUILayout.Height(22f)))
            {
                HarmonyPatches.IsStockNavballHidden = false;
                StockNavBallHook.HideStockNavballCompletely(false);
                HarmonyPatches.IsStockAltimeterHidden = false;
                StockNavBallHook.HideStockAltimeter(false);
                HarmonyPatches.IsStockBottomLeftHidden = false;
                StockNavBallHook.HideStockBottomLeft(false);
                HarmonyPatches.IsStockTimeWarpHidden = false;
                StockNavBallHook.HideStockTimeWarp(false);
                HarmonyPatches.IsStockCommNetHidden = false;
                StockNavBallHook.HideStockCommNet(false);
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 2 列 × 3 行紧凑控制矩阵
            GUILayout.BeginVertical();

            // 行 1: 自定义姿态球 vs 原生底栏导航球
            GUILayout.BeginHorizontal();
            DrawCustomNavballChip();
            GUILayout.Space(4f);
            DrawStockToggleChip(I18n.Tr("THM_STOCK_NAVBALL", "原生底栏导航球"), HarmonyPatches.IsStockNavballHidden, val =>
            {
                HarmonyPatches.IsStockNavballHidden = val;
                StockNavBallHook.HideStockNavballCompletely(val);
                ThemeManager.Instance.SaveSettings();
            });
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 行 2: 原生顶部高度计 vs 原生左下操纵分级台
            GUILayout.BeginHorizontal();
            DrawStockToggleChip(I18n.Tr("THM_STOCK_ALTI", "原生顶部高度计盒"), HarmonyPatches.IsStockAltimeterHidden, val =>
            {
                HarmonyPatches.IsStockAltimeterHidden = val;
                StockNavBallHook.HideStockAltimeter(val);
                ThemeManager.Instance.SaveSettings();
            });
            GUILayout.Space(4f);
            DrawStockToggleChip(I18n.Tr("THM_STOCK_STAGE", "原生左下操纵分级台"), HarmonyPatches.IsStockBottomLeftHidden, val =>
            {
                HarmonyPatches.IsStockBottomLeftHidden = val;
                StockNavBallHook.HideStockBottomLeft(val);
                ThemeManager.Instance.SaveSettings();
            });
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // 行 3: 原生时间加速/时钟 vs 原生通信信号栏
            GUILayout.BeginHorizontal();
            DrawStockToggleChip(I18n.Tr("THM_STOCK_TIME", "原生加速与任务时钟"), HarmonyPatches.IsStockTimeWarpHidden, val =>
            {
                HarmonyPatches.IsStockTimeWarpHidden = val;
                StockNavBallHook.HideStockTimeWarp(val);
                ThemeManager.Instance.SaveSettings();
            });
            GUILayout.Space(4f);
            DrawStockToggleChip(I18n.Tr("THM_STOCK_COMM", "原生 CommNet 信号栏"), HarmonyPatches.IsStockCommNetHidden, val =>
            {
                HarmonyPatches.IsStockCommNetHidden = val;
                StockNavBallHook.HideStockCommNet(val);
                ThemeManager.Instance.SaveSettings();
            });
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(4f);

            // 工具栏接管模式单行分段条
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_TOOLBAR_MODE", "右侧工具栏接管模式:")}</b>", GUILayout.Width(170f));
            int curTbMode = ThemeManager.Instance.ToolbarStyleMode;

            string tb0 = curTbMode == 0 ? I18n.Tr("THM_TB_CLASSIC_ON", "● 原版经典 (0)") : I18n.Tr("THM_TB_CLASSIC_OFF", "○ 原版经典 (0)");
            if (GUILayout.Button(tb0, curTbMode == 0 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (FlightHUDManager.Instance != null)
                {
                    FlightHUDManager.Instance.SwitchToolbarMode(0);
                }
                else
                {
                    ThemeManager.Instance.ToolbarStyleMode = 0;
                    ThemeManager.Instance.SaveSettings();
                    StockToolbarHook.ApplyStyleMode(0);
                }
            }

            string tb1 = curTbMode == 1 ? I18n.Tr("THM_TB_SKIN_ON", "● 黑晶重肤 (1)") : I18n.Tr("THM_TB_SKIN_OFF", "○ 黑晶重肤 (1)");
            if (GUILayout.Button(tb1, curTbMode == 1 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (FlightHUDManager.Instance != null)
                {
                    FlightHUDManager.Instance.SwitchToolbarMode(1);
                }
                else
                {
                    ThemeManager.Instance.ToolbarStyleMode = 1;
                    ThemeManager.Instance.SaveSettings();
                    StockToolbarHook.ApplyStyleMode(1);
                }
            }

            string tb2 = curTbMode == 2 ? I18n.Tr("THM_TB_DOCK_ON", "● 折叠收纳坞 (2)") : I18n.Tr("THM_TB_DOCK_OFF", "○ 折叠收纳坞 (2)");
            if (GUILayout.Button(tb2, curTbMode == 2 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (FlightHUDManager.Instance != null)
                {
                    FlightHUDManager.Instance.SwitchToolbarMode(2);
                }
                else
                {
                    ThemeManager.Instance.ToolbarStyleMode = 2;
                    ThemeManager.Instance.SaveSettings();
                    StockToolbarHook.ApplyStyleMode(2);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_NONFLIGHT_TB_MODE", "非飞行场景 (航天中心/VAB/SPH):")}</b>", GUILayout.Width(240f));
            int nonFlightMode = ThemeManager.Instance.NonFlightToolbarMode;

            string nftb1 = nonFlightMode == 1 ? I18n.Tr("THM_NFTB_RESKIN_ON", "● 保持黑晶重肤 (Hook)") : I18n.Tr("THM_NFTB_RESKIN_OFF", "○ 保持黑晶重肤 (Hook)");
            if (GUILayout.Button(nftb1, nonFlightMode == 1 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.NonFlightToolbarMode = 1;
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
                ThemeManager.Instance.SaveSettings();
            }

            string nftb0 = nonFlightMode == 0 ? I18n.Tr("THM_NFTB_STOCK_ON", "● 恢复原版经典 (Stock)") : I18n.Tr("THM_NFTB_STOCK_OFF", "○ 恢复原版经典 (Stock)");
            if (GUILayout.Button(nftb0, nonFlightMode == 0 ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.NonFlightToolbarMode = 0;
                StockToolbarHook.ApplyStyleMode(ThemeManager.Instance.ToolbarStyleMode);
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            if (curTbMode == 2)
            {
                DrawDockRulesDrawer();
            }

            MFPGuiSkin.EndCard();
        }

        private static void DrawCustomNavballChip()
        {
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isBallOn = navCfg == null || navCfg.IsEnabled;
            GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{I18n.Tr("THM_MODULAR_NAVBALL", "自定义 3D 姿态球:")}</b>", GUILayout.Width(140f));
            string ballText = isBallOn ? I18n.Tr("THM_NAVBALL_SHOWING", "✔ 已启用 (MFP接管)") : I18n.Tr("THM_NAVBALL_HIDDEN", "○ 已关闭自定义球");
            GUIStyle ballStyle = isBallOn ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.StepperButtonStyle;
            if (GUILayout.Button(ballText, ballStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
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
        }

        private static void DrawStockToggleChip(string label, bool isHidden, Action<bool> onToggle)
        {
            GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{label}:</b>", GUILayout.Width(140f));
            string btnText = isHidden ? I18n.Tr("THM_STOCK_SHIELDED", "✔ 已接管 (屏蔽原生)") : I18n.Tr("THM_STOCK_VISIBLE", "○ 原生显示中");
            GUIStyle style = isHidden ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.StepperButtonStyle;
            if (GUILayout.Button(btnText, style, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
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

        #region Module 3: Navball Quality & Pipeline Engine

        private static void DrawNavballAndPipelineCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_QUALITY", "🎯 姿态球渲染引擎与管线设置"),
                I18n.Tr("THM_SUBHEADER_QUALITY", "高精度 GPU 光线步进、视网膜超采样与底层加速管线"));

            // 1. 姿态球渲染模式选择器
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_NAVBALL_MODE_LABEL", "渲染模式选择:")}</b>", GUILayout.Width(130f));

            var curMode = ThemeManager.Instance.GlobalRenderMode;
            if (curMode == NavballRenderMode.ProceduralBake) curMode = NavballRenderMode.ProceduralVector;

            bool isStockTex = (curMode == NavballRenderMode.StockTexture);
            string stockTexLabel = isStockTex ? I18n.Tr("THM_MODE_STOCK_TEX_ON", "● 原版贴图") : I18n.Tr("THM_MODE_STOCK_TEX_OFF", "○ 原版贴图");
            if (GUILayout.Button(stockTexLabel, isStockTex ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.StockTexture;
                ThemeManager.Instance.SaveSettings();
            }

            bool isProcVec = (curMode == NavballRenderMode.ProceduralVector);
            string procVecLabel = isProcVec ? I18n.Tr("THM_MODE_PROC_VEC_ON", "● 程序化矢量 (推荐★)") : I18n.Tr("THM_MODE_PROC_VEC_OFF", "○ 程序化矢量 (推荐★)");
            if (GUILayout.Button(procVecLabel, isProcVec ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.ProceduralVector;
                ThemeManager.Instance.SaveSettings();
            }

            bool isStockDirect = (curMode == NavballRenderMode.StockDirect);
            string stockDirectLabel = isStockDirect ? I18n.Tr("THM_MODE_STOCK_DIRECT_ON", "● 原版 3D 姿态球") : I18n.Tr("THM_MODE_STOCK_DIRECT_OFF", "○ 原版 3D 姿态球");
            if (GUILayout.Button(stockDirectLabel, isStockDirect ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                ThemeManager.Instance.GlobalRenderMode = NavballRenderMode.StockDirect;
                ThemeManager.Instance.SaveSettings();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. 超采样倍率
            var renderMgr = WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                float currentRenderScale = renderMgr.GlobalRenderScaleMultiplier;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.Tr("THM_RENDER_SCALE", "姿态球超采样:")}</b> <color=#00E5FF>{currentRenderScale:F2}x</color>", GUILayout.Width(180f));

                float[] presets = new float[] { 0.8f, 1.0f, 1.25f, 1.5f, 2.0f };
                string[] presetLabels = new string[] {
                    I18n.Tr("THM_SCALE_08", "0.8x 节能"),
                    I18n.Tr("THM_SCALE_10", "1.0x 原生"),
                    I18n.Tr("THM_SCALE_125", "1.25x 细腻★"),
                    I18n.Tr("THM_SCALE_15", "1.5x 视网膜"),
                    I18n.Tr("THM_SCALE_20", "2.0x 极致")
                };

                for (int pIdx = 0; pIdx < presets.Length; pIdx++)
                {
                    float pVal = presets[pIdx];
                    bool isSelected = Mathf.Abs(currentRenderScale - pVal) < 0.05f;
                    GUIStyle bStyle = isSelected ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                    if (GUILayout.Button(presetLabels[pIdx], bStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
                    {
                        renderMgr.SetGlobalRenderScale(pVal);
                        ThemeManager.Instance.SaveSettings();
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(3f);

            // 3. 底层渲染管线模式
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_PIPELINE_MODE_LABEL", "底层着色管线:")}</b>", GUILayout.Width(130f));

            bool isGpu = ThemeManager.Instance != null && ThemeManager.Instance.EnableGpu2DUIAcceleration;

            string gpuLabel = isGpu ? I18n.Tr("THM_PIPELINE_GPU_ON", "● 现代 2D GPU 单 Quad 程序化加速 (推荐)") : I18n.Tr("THM_PIPELINE_GPU_OFF", "○ 现代 2D GPU 单 Quad 程序化加速");
            if (GUILayout.Button(gpuLabel, isGpu ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                if (ThemeManager.Instance != null && !isGpu)
                {
                    ThemeManager.Instance.EnableGpu2DUIAcceleration = true;
                    ThemeManager.Instance.SaveSettings();
                    WidgetStyleManager.Instance?.ClearMaterialCache();
                    ThemeManager.Instance.SetTheme(ThemeManager.Instance.CurrentTheme);
                }
            }

            string uguiLabel = !isGpu ? I18n.Tr("THM_PIPELINE_UGUI_ON", "● 经典 UGUI 网格兼容") : I18n.Tr("THM_PIPELINE_UGUI_OFF", "○ 经典 UGUI 网格兼容");
            if (GUILayout.Button(uguiLabel, !isGpu ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
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

            // 4. 诊断状态徽标条
            if (renderMgr != null)
            {
                GUILayout.Space(3f);
                MFPGuiSkin.BeginInset();
                int screenW = Screen.width;
                int screenH = Screen.height;
                float canvasScale = renderMgr.GetCanvasScaleFactor();
                float ballDiameter = 150f * UIFactory.GetKspNavballUiScale() * UIFactory.GetScreenDpiScale();
                float ballPhysical = renderMgr.CalculatePhysicalPixelSize(new Vector2(ballDiameter, ballDiameter));
                int optimalTex = renderMgr.CalculateOptimalResolution(new Vector2(ballDiameter, ballDiameter));
                float vramMb = optimalTex * optimalTex * 4 / (1024f * 1024f);

                string diag = I18n.TrFormat("THM_DIAG_COMPACT", screenW, screenH, canvasScale, ballPhysical, optimalTex, vramMb);
                GUILayout.Label(diag);
                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 4: Performance & SPEC Validator

        private static void DrawPerformanceAndSpecCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PROFILER", "⚡ 性能探针诊断与全局主干校验"));

            GUILayout.BeginHorizontal();
            bool bypassed = MFPProfiler.IsMasterBypassed;
            GUIStyle bypassStyle = bypassed ? MFPGuiSkin.DangerButtonStyle : MFPGuiSkin.SuccessButtonStyle;
            string bypassLabel = bypassed ? I18n.Tr("THM_BYPASS_ON", "● [已完全旁路] 所有 MFP 逻辑/渲染已关闭") : I18n.Tr("THM_BYPASS_OFF", "○ [正常运行中] 点击完全 Bypass (F11)");
            if (GUILayout.Button(bypassLabel, bypassStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                MFPProfiler.ToggleMasterBypass();
                ThemeManager.Instance.SaveSettings();
            }

            GUIStyle hudStyle = MFPProfiler.ShowOverlay ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
            string hudText = MFPProfiler.ShowOverlay ? I18n.Tr("THM_HIDE_PROFILER", "✔ 隐藏性能 HUD (F10)") : I18n.Tr("THM_SHOW_PROFILER", "显示性能 HUD (F10)");
            if (GUILayout.Button(hudText, hudStyle, GUILayout.Width(170f), GUILayout.Height(24f)))
            {
                MFPProfiler.ShowOverlay = !MFPProfiler.ShowOverlay;
                ThemeManager.Instance.SaveSettings();
            }

            if (GUILayout.Button(I18n.Tr("THM_BTN_RUN_SPEC", "运行规范自检"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(130f), GUILayout.Height(24f)))
            {
                WidgetValidationReport auditReport = WidgetSpecificationValidator.RunDevelopmentAudit();
                _lastSpecAuditSummary = auditReport.IsCompliant
                    ? I18n.TrFormat("THM_SPEC_OK", auditReport.TotalWidgetsAudited, auditReport.TotalChecksPerformed, auditReport.WarningCount)
                    : I18n.TrFormat("THM_SPEC_FAIL", auditReport.ErrorCount, auditReport.WarningCount);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("THM_SPEC_STATUS_PREFIX", "组件规范最新审计状态:")} {_lastSpecAuditSummary}</size></color>");

            MFPGuiSkin.EndCard();
        }

        #endregion
    }
}
