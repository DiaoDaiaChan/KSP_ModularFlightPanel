using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新视觉风格、原生融合与系统偏好配置中枢 (Avionics Themes & Visual Preferences - TabThemeSettings)
    /// 核心优化：
    /// 1. 主题分类矩阵化：4 大航电属性分类索引 + 4 列色标芯片网格，杜绝单行平铺拥挤与文字截断。
    /// 2. 原生界面 2×3 紧凑开关矩阵 + 一键批量接管/恢复，取代冗余通栏大按钮。
    /// 3. 显示着色器、点阵网格与物理微点阵/数码液晶字模精细调优。
    /// 4. 3D 姿态球渲染引擎与底层着色管线有机合流。
    /// </summary>
    public class TabThemeSettings : ISettingsTab
    {
        public string TabId => "themes";
        public string DisplayTitle => I18n.Tr("UI_TAB_THEMES", "🎨 视觉风格");

        private Vector2 _scrollPos = Vector2.zero;
        private int _themeSubTab = 0; // 0: 🎨 配色主题, 1: 🔤 显示与字模, 2: 🔌 原生融合与收纳, 3: 🌐 3D 姿态球
        private bool _showDockSettingsFold = false;
        private int _themeCategory = 1; // 默认聚焦当前主题分类 (1: 现代航电, 2: 经典历史, 3: 战术机载, 4: 赛博科幻, 5: 用户定制, 0: 全部)
        private Vector2 _dockRulesScrollPos = Vector2.zero;

        // 色彩精细调优状态 (Live Palette Customizer State)
        private int _colorPaletteCategory = 0; // 0: 核心色标, 1: 面板与字模, 2: 3D姿态球, 3: 全量色标
        private int _selectedColorSlotIndex = 0;
        private string _hexInputBuffer = "";
        private int _lastColorSlotForHex = -1;
        private string _lastThemeIdForHex = "";
        private bool _showSaveAsDialog = false;
        private string _newThemeNameInput = "";
        private ColorSlotDef[] _cachedColorSlots = null;

        public void OnEnter()
        {
            _scrollPos = Vector2.zero;
            _cachedColorSlots = null;
            _lastColorSlotForHex = -1;
            _lastThemeIdForHex = "";
            var cur = ThemeManager.Instance?.CurrentTheme;
            if (cur != null)
            {
                _themeCategory = GetThemeCategoryIndex(cur.ThemeId);
            }
        }
        public void OnExit() { }

        public void Draw(float availableHeight)
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();

            // =========================================================================
            // 顶部二级子导航切换条 (消除全内容一股脑堆砌平铺)
            // =========================================================================
            GUILayout.BeginHorizontal();
            string[] subTabs = new string[]
            {
                I18n.Tr("THM_SUBTAB_THEMES", "🎨 配色主题"),
                I18n.Tr("THM_SUBTAB_SHADERS", "🔤 显示与字模"),
                I18n.Tr("THM_SUBTAB_STOCK", "🔌 原生融合与收纳"),
                I18n.Tr("THM_SUBTAB_NAVBALL", "🌐 3D 姿态球")
            };

            for (int s = 0; s < subTabs.Length; s++)
            {
                bool isSel = (_themeSubTab == s);
                GUIStyle tabStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(subTabs[s], tabStyle, GUILayout.Height(25f)))
                {
                    _themeSubTab = s;
                    _scrollPos = Vector2.zero;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            float subAvailH = Mathf.Max(200f, availableHeight - 34f);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(subAvailH));

            switch (_themeSubTab)
            {
                case 0:
                    DrawThemesCard();
                    GUILayout.Space(6f);
                    DrawColorPaletteCustomizerCard();
                    break;
                case 1:
                    DrawDisplayShaderAndFontCard();
                    break;
                case 2:
                    DrawStockIntegrationCard();
                    break;
                case 3:
                    DrawNavballAndPipelineCard();
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Module 1: Themes Grid & Palette Hub

        private struct QuickColorPreset
        {
            public string Key;
            public string Fallback;
            public string Hex;
            public QuickColorPreset(string key, string fallback, string hex)
            {
                Key = key;
                Fallback = fallback;
                Hex = hex;
            }
        }

        private static readonly QuickColorPreset[] QuickColorPresets = new QuickColorPreset[]
        {
            new QuickColorPreset("THM_CLR_EMERALD", "Emerald", "#2ED573"),
            new QuickColorPreset("THM_CLR_NEON_GREEN", "Neon Green", "#00FF66"),
            new QuickColorPreset("THM_CLR_TITANIUM_BLUE", "Ice Blue", "#54A0FF"),
            new QuickColorPreset("THM_CLR_SPACEX_BLUE", "Electric Blue", "#0070FF"),
            new QuickColorPreset("THM_CLR_CYBER_CYAN", "Cyber Cyan", "#00E5FF"),
            new QuickColorPreset("THM_CLR_AMBER_GOLD", "Amber Gold", "#FFA502"),
            new QuickColorPreset("THM_CLR_CORAL_RED", "Coral Red", "#FF4757"),
            new QuickColorPreset("THM_CLR_AURORA_PURPLE", "Aurora Purple", "#A55EEA"),
            new QuickColorPreset("THM_CLR_MAGENTA_PINK", "Magenta Pink", "#FF2A85"),
            new QuickColorPreset("THM_CLR_COOL_WHITE", "Cool White", "#F0F4F8"),
            new QuickColorPreset("THM_CLR_TITANIUM_GRAY", "Titanium Gray", "#7088A8"),
            new QuickColorPreset("THM_CLR_DEEP_GRAPHITE", "Deep Graphite", "#060A10")
        };

        private class ColorSlotDef
        {
            public int Index;
            public int Category; // 0: 核心色标, 1: 面板与字模, 2: 3D姿态球
            public string Name;
            public string Description;
            public Func<ThemeConfig, Color> Getter;
            public Action<ThemeConfig, Color> Setter;
            public bool HasAlpha;
        }

        private ColorSlotDef[] GetColorSlots()
        {
            if (_cachedColorSlots != null) return _cachedColorSlots;

            _cachedColorSlots = new ColorSlotDef[]
            {
                // Category 0: 核心航电语义色彩 (Core Avionics)
                new ColorSlotDef
                {
                    Index = 0,
                    Category = 0,
                    Name = I18n.Tr("THM_SLOT_ACCENT_PRI", "主强调色"),
                    Description = I18n.Tr("THM_SLOT_ACCENT_PRI_DESC", "FLIR翡翠绿 / 核心遥测主读数 / 活跃高亮 / 主指针"),
                    Getter = t => t.AccentPrimary.ToColor(),
                    Setter = (t, c) => t.AccentPrimary = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 1,
                    Category = 0,
                    Name = I18n.Tr("THM_SLOT_ACCENT_SEC", "次级色标"),
                    Description = I18n.Tr("THM_SLOT_ACCENT_SEC_DESC", "航电钛冰蓝 / 状态标记 / 工程物理单位与辅助标度"),
                    Getter = t => t.AccentSecondary.ToColor(),
                    Setter = (t, c) => t.AccentSecondary = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 2,
                    Category = 0,
                    Name = I18n.Tr("THM_SLOT_WARN", "警示警戒"),
                    Description = I18n.Tr("THM_SLOT_WARN_DESC", "琥珀金 / 注意事项 / 接近安全临界黄色标记"),
                    Getter = t => t.WarningColor.ToColor(),
                    Setter = (t, c) => t.WarningColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 3,
                    Category = 0,
                    Name = I18n.Tr("THM_SLOT_DANG", "告警危险"),
                    Description = I18n.Tr("THM_SLOT_DANG_DESC", "珊瑚红 / 超限警告 / 紧急切断危险报警"),
                    Getter = t => t.DangerColor.ToColor(),
                    Setter = (t, c) => t.DangerColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 4,
                    Category = 0,
                    Name = I18n.Tr("THM_SLOT_MAGENTA", "航路品红"),
                    Description = I18n.Tr("THM_SLOT_MAGENTA_DESC", "品红 / 飞行指引仪 / 目标引向与轨道机动航向"),
                    Getter = t => t.AccentMagenta.ToColor(),
                    Setter = (t, c) => t.AccentMagenta = ColorHex.FromColor(c),
                    HasAlpha = false
                },

                // Category 1: 座舱面板与字模色彩 (Surfaces & Typography)
                new ColorSlotDef
                {
                    Index = 5,
                    Category = 1,
                    Name = I18n.Tr("THM_SLOT_FRAME_BG", "座舱玻璃底"),
                    Description = I18n.Tr("THM_SLOT_FRAME_BG_DESC", "半透深石墨航电座舱玻璃底色 (含 Alpha 透明度)"),
                    Getter = t => t.FrameBgColor.ToColor(),
                    Setter = (t, c) => t.FrameBgColor = ColorHex.FromColor(c),
                    HasAlpha = true
                },
                new ColorSlotDef
                {
                    Index = 6,
                    Category = 1,
                    Name = I18n.Tr("THM_SLOT_FRAME_BORDER", "轮廓边框色"),
                    Description = I18n.Tr("THM_SLOT_FRAME_BORDER_DESC", "细腻极细边框高光与仪表分隔线 (含 Alpha 透明度)"),
                    Getter = t => t.FrameBorderColor.ToColor(),
                    Setter = (t, c) => t.FrameBorderColor = ColorHex.FromColor(c),
                    HasAlpha = true
                },
                new ColorSlotDef
                {
                    Index = 7,
                    Category = 1,
                    Name = I18n.Tr("THM_SLOT_INACTIVE_METER", "刻度槽底暗色"),
                    Description = I18n.Tr("THM_SLOT_INACTIVE_METER_DESC", "计量槽未激活暗轨与待机指示背底 (含 Alpha 透明度)"),
                    Getter = t => t.InactiveMeterColor.ToColor(),
                    Setter = (t, c) => t.InactiveMeterColor = ColorHex.FromColor(c),
                    HasAlpha = true
                },
                new ColorSlotDef
                {
                    Index = 8,
                    Category = 1,
                    Name = I18n.Tr("THM_SLOT_TEXT_PRI", "关键主读数"),
                    Description = I18n.Tr("THM_SLOT_TEXT_PRI_DESC", "纯净高对比冷白主要遥测数值与大字"),
                    Getter = t => t.TextPrimaryColor.ToColor(),
                    Setter = (t, c) => t.TextPrimaryColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 9,
                    Category = 1,
                    Name = I18n.Tr("THM_SLOT_TEXT_ACC", "副文字与标签"),
                    Description = I18n.Tr("THM_SLOT_TEXT_ACC_DESC", "航电钛银辅助文字与遥测参数标签"),
                    Getter = t => t.TextAccentColor.ToColor(),
                    Setter = (t, c) => t.TextAccentColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },

                // Category 2: 3D 姿态球色彩 (3D Attitude Navball)
                new ColorSlotDef
                {
                    Index = 10,
                    Category = 2,
                    Name = I18n.Tr("THM_SLOT_SKY", "姿态球天穹"),
                    Description = I18n.Tr("THM_SLOT_SKY_DESC", "3D 姿态球上半球俯仰天空天顶色"),
                    Getter = t => t.SkyColor.ToColor(),
                    Setter = (t, c) => t.SkyColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 11,
                    Category = 2,
                    Name = I18n.Tr("THM_SLOT_GROUND", "姿态球大地"),
                    Description = I18n.Tr("THM_SLOT_GROUND_DESC", "3D 姿态球下半球俯角大地地表色"),
                    Getter = t => t.GroundColor.ToColor(),
                    Setter = (t, c) => t.GroundColor = ColorHex.FromColor(c),
                    HasAlpha = false
                },
                new ColorSlotDef
                {
                    Index = 12,
                    Category = 2,
                    Name = I18n.Tr("THM_SLOT_HORIZON", "地平线基准标"),
                    Description = I18n.Tr("THM_SLOT_HORIZON_DESC", "3D 姿态球人工地平线基准线标"),
                    Getter = t => t.HorizonLineColor.ToColor(),
                    Setter = (t, c) => t.HorizonLineColor = ColorHex.FromColor(c),
                    HasAlpha = true
                },
                new ColorSlotDef
                {
                    Index = 13,
                    Category = 2,
                    Name = I18n.Tr("THM_SLOT_GRID", "经纬网格线"),
                    Description = I18n.Tr("THM_SLOT_GRID_DESC", "3D 姿态球经纬度刻度与分度网格线"),
                    Getter = t => t.GridColor.ToColor(),
                    Setter = (t, c) => t.GridColor = ColorHex.FromColor(c),
                    HasAlpha = true
                }
            };

            return _cachedColorSlots;
        }

        private void SelectColorSlot(int index)
        {
            _selectedColorSlotIndex = index;
            var slots = GetColorSlots();
            if (index >= 0 && index < slots.Length)
            {
                var slot = slots[index];
                var current = ThemeManager.Instance.CurrentTheme;
                if (current != null)
                {
                    Color c = slot.Getter(current);
                    _hexInputBuffer = slot.HasAlpha
                        ? $"#{ColorUtility.ToHtmlStringRGBA(c)}"
                        : $"#{ColorUtility.ToHtmlStringRGB(c)}";
                    _lastColorSlotForHex = index;
                    _lastThemeIdForHex = current.ThemeId;
                }
            }
        }

        private void ApplyColor(ColorSlotDef slot, ThemeConfig current, Color newColor, bool updateHexBuffer)
        {
            slot.Setter(current, newColor);
            if (updateHexBuffer)
            {
                _hexInputBuffer = slot.HasAlpha
                    ? $"#{ColorUtility.ToHtmlStringRGBA(newColor)}"
                    : $"#{ColorUtility.ToHtmlStringRGB(newColor)}";
            }

            WidgetStyleManager.Instance?.ClearMaterialCache();
            WidgetStyleManager.Instance?.InvalidatePalette();
            MFPGuiSkin.EnsureInitialized(forceRebuild: true);
            ThemeManager.Instance?.NotifyThemeChanged();
        }

        private int GetThemeCategoryIndex(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) return 1;
            if (!ThemeManager.Instance.IsBuiltinTheme(themeId)) return 5;
            string id = themeId.ToLowerInvariant();
            if (id.Contains("787") || id.Contains("modern") || id.Contains("dragon") || id.Contains("starship")) return 1;
            if (id.Contains("classic") || id.Contains("apollo") || id.Contains("vostok") || id.Contains("amber")) return 2;
            if (id.Contains("hud") || id.Contains("diffractive") || id.Contains("blackbird") || id.Contains("voyager") || id.Contains("deep_space")) return 3;
            if (id.Contains("neon") || id.Contains("matrix") || id.Contains("eva")) return 4;
            return 1;
        }

        private void DrawThemesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PALETTES", "🎨 视觉主题风格预设"),
                I18n.Tr("THM_SUBHEADER_PALETTES", "点击即刻全局动态换肤"));

            // 1. 当前生效主题色板预览条 (置顶突出呈现，支持点击直接跳转微调对应色标)
            var current = ThemeManager.Instance.CurrentTheme;
            if (current != null)
            {
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();

                GUILayout.Label($"<b>{I18n.Tr("THM_ACTIVE_THEME_LABEL", "当前主题:")}</b> <color=#00E5FF><b>{current.DisplayName}</b></color>", GUILayout.Width(220f));

                string cPri = ColorUtility.ToHtmlStringRGB(current.AccentPrimary.ToColor());
                string cSec = ColorUtility.ToHtmlStringRGB(current.AccentSecondary.ToColor());
                string cWarn = ColorUtility.ToHtmlStringRGB(current.WarningColor.ToColor());
                string cDang = ColorUtility.ToHtmlStringRGB(current.DangerColor.ToColor());

                if (GUILayout.Button($"<color=#{cPri}>■ {I18n.Tr("THM_COLOR_PRI", "主强调")}</color>", MFPGuiSkin.HeaderStyle, GUILayout.Height(20f)))
                {
                    _colorPaletteCategory = 0;
                    SelectColorSlot(0);
                }
                if (GUILayout.Button($"<color=#{cSec}>■ {I18n.Tr("THM_COLOR_SEC", "次色标")}</color>", MFPGuiSkin.HeaderStyle, GUILayout.Height(20f)))
                {
                    _colorPaletteCategory = 0;
                    SelectColorSlot(1);
                }
                if (GUILayout.Button($"<color=#{cWarn}>■ {I18n.Tr("THM_COLOR_WARN", "警戒")}</color>", MFPGuiSkin.HeaderStyle, GUILayout.Height(20f)))
                {
                    _colorPaletteCategory = 0;
                    SelectColorSlot(2);
                }
                if (GUILayout.Button($"<color=#{cDang}>■ {I18n.Tr("THM_COLOR_DANG", "告警")}</color>", MFPGuiSkin.HeaderStyle, GUILayout.Height(20f)))
                {
                    _colorPaletteCategory = 0;
                    SelectColorSlot(3);
                }

                GUILayout.FlexibleSpace();
                string fontDesc = current.FontStyle == AvionicsFontStyle.RetroPixel
                    ? I18n.Tr("THM_FONT_PIXEL_TAG", "点阵像素")
                    : I18n.Tr("THM_FONT_SMOOTH_TAG", "平滑矢量");
                GUILayout.Label($"<color=#7088A8><size=11>{I18n.Tr("THM_PIPELINE_LABEL", "管线:")} <color=#00FF88>{current.UiStyle}</color> | {I18n.Tr("THM_FONT_LABEL", "字模:")} <color=#FFA502>{fontDesc}</color></size></color>");
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
            }

            GUILayout.Space(6f);

            var allThemes = ThemeManager.Instance.AvailableThemes;

            // 2. 风格分类过滤药丸条 (动态统计用户定制主题)
            int customCount = 0;
            for (int i = 0; i < allThemes.Count; i++)
            {
                if (!ThemeManager.Instance.IsBuiltinTheme(allThemes[i].ThemeId)) customCount++;
            }

            List<string> catNamesList = new List<string>
            {
                I18n.Tr("THM_CAT_MODERN", "✈ 现代航电 (4)"),
                I18n.Tr("THM_CAT_CLASSIC", "🚀 经典历史 (5)"),
                I18n.Tr("THM_CAT_TACTICAL", "🎯 战术机载 (3)"),
                I18n.Tr("THM_CAT_SCIFI", "⚡ 赛博科幻 (4)")
            };
            List<int> catIndicesList = new List<int> { 1, 2, 3, 4 };
            if (customCount > 0)
            {
                catNamesList.Add(I18n.TrFormat("THM_CAT_CUSTOM", customCount));
                catIndicesList.Add(5);
            }
            catNamesList.Add(I18n.TrFormat("THM_CAT_ALL", allThemes.Count));
            catIndicesList.Add(0);

            GUILayout.BeginHorizontal();
            for (int c = 0; c < catNamesList.Count; c++)
            {
                int targetCat = catIndicesList[c];
                bool isSel = (_themeCategory == targetCat);
                GUIStyle catStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(catNamesList[c], catStyle, GUILayout.Height(22f), GUILayout.MinWidth(85f)))
                {
                    _themeCategory = targetCat;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 3. 筛选对应分类下的主题 (只显示当前分类的 3-5 个，不再一股脑平铺 16 个)
            List<ThemeConfig> filtered = new List<ThemeConfig>();
            for (int i = 0; i < allThemes.Count; i++)
            {
                var t = allThemes[i];
                if (_themeCategory == 0 || GetThemeCategoryIndex(t.ThemeId) == _themeCategory)
                {
                    filtered.Add(t);
                }
            }

            // 4. 网格排版 (分类模式下使用 2 列宽卡片，视觉呼吸感极佳)
            int cols = _themeCategory == 0 ? 4 : 2;
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
                        string label = $"<color=#{hexAcc}>■</color> <b>{shortName}</b>  <color=#{MFPGuiSkin.HexTextSecondary}><size=10>({t.UiStyle})</size></color>";

                        if (GUILayout.Button(label, bStyle, GUILayout.Height(30f), GUILayout.ExpandWidth(true)))
                        {
                            ThemeManager.Instance.SetTheme(t.ThemeId);
                            SelectColorSlot(_selectedColorSlotIndex);
                        }
                    }
                    else
                    {
                        GUILayout.Space(0f);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(3f);
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawColorPaletteCustomizerCard()
        {
            var current = ThemeManager.Instance.CurrentTheme;
            if (current == null) return;

            MFPGuiSkin.BeginCard();

            // 1. 卡片标题与快捷操作栏
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("THM_HEADER_COLOR_STUDIO", "🎨 航电配色方案定制与微调"), MFPGuiSkin.SectionTitleStyle);
            GUILayout.FlexibleSpace();

            // 保存修改
            if (GUILayout.Button(I18n.Tr("THM_BTN_SAVE_THEME", "💾 保存当前配色"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(130f), GUILayout.Height(22f)))
            {
                ThemeManager.Instance.SaveCustomTheme(current);
                ThemeManager.Instance.SaveSettings();
                MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_THEME_SAVED", "当前主题配色已保存到磁盘"));
            }

            // 另存为新主题
            if (GUILayout.Button(I18n.Tr("THM_BTN_SAVE_AS_THEME", "⭐ 另存为新主题..."), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(135f), GUILayout.Height(22f)))
            {
                _showSaveAsDialog = !_showSaveAsDialog;
                if (_showSaveAsDialog)
                {
                    _newThemeNameInput = $"{current.DisplayName} ({I18n.Tr("THM_CUSTOM_SUFFIX", "定制")})";
                }
            }

            // 恢复默认 / 删除主题
            bool isBuiltin = ThemeManager.Instance.IsBuiltinTheme(current.ThemeId);
            if (isBuiltin)
            {
                if (GUILayout.Button(I18n.Tr("THM_BTN_RESET_THEME", "🔄 恢复默认"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(90f), GUILayout.Height(22f)))
                {
                    ThemeManager.Instance.ResetBuiltinTheme(current.ThemeId);
                    _lastColorSlotForHex = -1;
                    MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_RESET_DONE", "已恢复官方默认出厂配色"));
                }
            }
            else
            {
                if (GUILayout.Button(I18n.Tr("THM_BTN_DELETE_THEME", "🗑 删除主题"), MFPGuiSkin.DangerButtonStyle, GUILayout.Width(90f), GUILayout.Height(22f)))
                {
                    ThemeManager.Instance.DeleteCustomTheme(current.ThemeId);
                    _lastColorSlotForHex = -1;
                    MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_THEME_DELETED", "自定义主题已删除"));
                }
            }
            GUILayout.EndHorizontal();

            // 副标题说明
            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("THM_SUBHEADER_COLOR_STUDIO", "实时微调当前主题语义色标与座舱表面材质，即刻全局生效")}</size></color>");

            // 2. 另存为自定义主题内嵌面板 (展开状态)
            if (_showSaveAsDialog)
            {
                GUILayout.Space(4f);
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.Tr("THM_SAVE_AS_PROMPT", "新主题显示名称:")}</b>", GUILayout.Width(125f));
                _newThemeNameInput = GUILayout.TextField(_newThemeNameInput ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.ExpandWidth(true));

                if (GUILayout.Button(I18n.Tr("THM_BTN_CONFIRM_SAVE", "✔ 确认另存"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(95f), GUILayout.Height(22f)))
                {
                    string name = string.IsNullOrEmpty(_newThemeNameInput) ? $"{current.DisplayName} ({I18n.Tr("THM_CUSTOM_SUFFIX", "定制")})" : _newThemeNameInput.Trim();
                    string newId = $"custom_{DateTime.UtcNow.Ticks % 10000000}";
                    ThemeConfig cloned = ThemeManager.Instance.CloneTheme(current, newId, name);
                    ThemeManager.Instance.SaveCustomTheme(cloned);
                    _showSaveAsDialog = false;
                    _newThemeNameInput = "";
                    _lastColorSlotForHex = -1;
                    MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_NEW_THEME_CREATED", "已成功另存为新自定义主题并激活"));
                }

                if (GUILayout.Button(I18n.Tr("THM_BTN_CANCEL", "取消"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(60f), GUILayout.Height(22f)))
                {
                    _showSaveAsDialog = false;
                }
                GUILayout.EndHorizontal();
                MFPGuiSkin.EndInset();
            }

            GUILayout.Space(6f);

            // 3. 色标分类切换药丸条
            GUILayout.BeginHorizontal();
            string[] colorCatNames = new string[]
            {
                I18n.Tr("THM_PALETTE_CAT_CORE", "✈ 核心色标 (5)"),
                I18n.Tr("THM_PALETTE_CAT_SURFACE", "🪟 面板与字模 (5)"),
                I18n.Tr("THM_PALETTE_CAT_NAVBALL", "🌐 3D 姿态球 (4)"),
                I18n.Tr("THM_PALETTE_CAT_ALL", "全量色标 (14)")
            };
            for (int c = 0; c < colorCatNames.Length; c++)
            {
                bool isSel = (_colorPaletteCategory == c);
                GUIStyle bStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(colorCatNames[c], bStyle, GUILayout.Height(22f), GUILayout.MinWidth(95f)))
                {
                    _colorPaletteCategory = c;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 4. 筛选并呈现色标芯片网格 (3 列响应式网格)
            var slots = GetColorSlots();
            List<ColorSlotDef> visibleSlots = new List<ColorSlotDef>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (_colorPaletteCategory == 3 || slots[i].Category == _colorPaletteCategory)
                {
                    visibleSlots.Add(slots[i]);
                }
            }

            int colsPerGrid = 3;
            for (int r = 0; r < visibleSlots.Count; r += colsPerGrid)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < colsPerGrid; c++)
                {
                    int idx = r + c;
                    if (idx < visibleSlots.Count)
                    {
                        var slot = visibleSlots[idx];
                        Color slotColor = slot.Getter(current);
                        string hexStr = slot.HasAlpha
                            ? ColorUtility.ToHtmlStringRGBA(slotColor)
                            : ColorUtility.ToHtmlStringRGB(slotColor);

                        bool isSelected = (_selectedColorSlotIndex == slot.Index);
                        GUIStyle chipStyle = isSelected ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;

                        string hexRgb = ColorUtility.ToHtmlStringRGB(slotColor);
                        string chipLabel = $"<color=#{hexRgb}>■</color> <b>{slot.Name}</b> <color=#{MFPGuiSkin.HexTextSecondary}><size=10>#{hexStr}</size></color>";

                        if (GUILayout.Button(chipLabel, chipStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                        {
                            SelectColorSlot(slot.Index);
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

            GUILayout.Space(6f);

            // 5. 正在微调的色标控制台 (Live Color Tuning Inspector)
            DrawActiveColorInspector(current, slots);

            MFPGuiSkin.EndCard();
        }

        private void DrawActiveColorInspector(ThemeConfig current, ColorSlotDef[] slots)
        {
            if (_selectedColorSlotIndex < 0 || _selectedColorSlotIndex >= slots.Length)
            {
                _selectedColorSlotIndex = 0;
            }
            var slot = slots[_selectedColorSlotIndex];
            Color curCol = slot.Getter(current);

            // Sync Hex buffer if slot or theme changed
            if (_lastColorSlotForHex != slot.Index || _lastThemeIdForHex != current.ThemeId)
            {
                _lastColorSlotForHex = slot.Index;
                _lastThemeIdForHex = current.ThemeId;
                _hexInputBuffer = slot.HasAlpha
                    ? $"#{ColorUtility.ToHtmlStringRGBA(curCol)}"
                    : $"#{ColorUtility.ToHtmlStringRGB(curCol)}";
            }

            MFPGuiSkin.BeginInset();

            // 标题行与 Hex / 复制 / 粘贴
            GUILayout.BeginHorizontal();

            // 颜色色块预览 (28x22)
            Texture2D swatchTex = MFPGuiSkin.SolidTex(curCol);
            GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Width(28f), GUILayout.Height(22f));
            Rect lastR = GUILayoutUtility.GetLastRect();
            if (lastR.width > 1 && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(lastR, swatchTex);
                Color borderCol = new Color(1f, 1f, 1f, 0.45f);
                Texture2D bord = MFPGuiSkin.BorderedTex(Color.clear, borderCol, 1, 16);
                GUI.DrawTexture(lastR, bord);
            }

            GUILayout.Space(6f);

            // 正在微调信息
            string roleHex = ColorUtility.ToHtmlStringRGB(curCol);
            GUILayout.Label($"<b>{I18n.Tr("THM_EDITING_COLOR", "正在调节色标:")}</b> <color=#{roleHex}><b>{slot.Name}</b></color>", GUILayout.Width(220f));

            GUILayout.FlexibleSpace();

            // Hex 输入框
            GUILayout.Label($"<b>{I18n.Tr("THM_HEX_LABEL", "HEX 色标:")}</b>", GUILayout.Width(65f));
            string newHex = GUILayout.TextField(_hexInputBuffer ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Width(85f), GUILayout.Height(20f));
            if (newHex != _hexInputBuffer)
            {
                _hexInputBuffer = newHex;
                string clean = _hexInputBuffer.Trim();
                if (!clean.StartsWith("#")) clean = "#" + clean;
                if ((clean.Length == 7 || clean.Length == 9) && ColorUtility.TryParseHtmlString(clean, out Color parsed))
                {
                    if (!slot.HasAlpha) parsed.a = curCol.a;
                    ApplyColor(slot, current, parsed, false);
                }
            }

            // 复制按钮
            if (GUILayout.Button(I18n.Tr("THM_BTN_COPY_HEX", "📋 复制"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f), GUILayout.Height(20f)))
            {
                string hex = slot.HasAlpha ? $"#{ColorUtility.ToHtmlStringRGBA(curCol)}" : $"#{ColorUtility.ToHtmlStringRGB(curCol)}";
                GUIUtility.systemCopyBuffer = hex;
                MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_HEX_COPIED", "色标 Hex 已复制到剪贴板"));
            }

            // 粘贴按钮
            if (GUILayout.Button(I18n.Tr("THM_BTN_PASTE_HEX", "📥 粘贴"), MFPGuiSkin.StepperButtonStyle, GUILayout.Width(55f), GUILayout.Height(20f)))
            {
                string cb = (GUIUtility.systemCopyBuffer ?? "").Trim();
                if (!cb.StartsWith("#")) cb = "#" + cb;
                if (ColorUtility.TryParseHtmlString(cb, out Color pastedCol))
                {
                    if (!slot.HasAlpha && cb.Length == 7) pastedCol.a = 1f;
                    ApplyColor(slot, current, pastedCol, true);
                    MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_HEX_PASTED", "已从剪贴板粘贴色标"));
                }
                else
                {
                    MFPGuiSkin.ShowToast(I18n.Tr("THM_TOAST_HEX_INVALID", "剪贴板内容不是有效颜色代码 (需为 #RRGGBB 或 #RRGGBBAA)"));
                }
            }

            GUILayout.EndHorizontal();

            // 角色语义说明
            GUILayout.Space(2f);
            GUILayout.Label($"<color=#7088A8><size=11>• {slot.Description}</size></color>");
            GUILayout.Space(4f);

            // RGB(A) 滑条
            float r = curCol.r;
            float g = curCol.g;
            float b = curCol.b;
            float a = curCol.a;
            bool sliderChanged = false;

            // R
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#FF6B6B><b>R</b></color>", GUILayout.Width(55f));
            float newR = GUILayout.HorizontalSlider(r, 0f, 1f, GUILayout.ExpandWidth(true));
            if (Mathf.Abs(newR - r) > 0.002f) { r = newR; sliderChanged = true; }
            GUILayout.Label($"<b>{Mathf.RoundToInt(r * 255f),3}</b> <color=#7088A8><size=10>({r * 100f:F0}%)</size></color>", MFPGuiSkin.ValueFieldStyle, GUILayout.Width(72f));
            GUILayout.EndHorizontal();

            // G
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#2ED573><b>G</b></color>", GUILayout.Width(55f));
            float newG = GUILayout.HorizontalSlider(g, 0f, 1f, GUILayout.ExpandWidth(true));
            if (Mathf.Abs(newG - g) > 0.002f) { g = newG; sliderChanged = true; }
            GUILayout.Label($"<b>{Mathf.RoundToInt(g * 255f),3}</b> <color=#7088A8><size=10>({g * 100f:F0}%)</size></color>", MFPGuiSkin.ValueFieldStyle, GUILayout.Width(72f));
            GUILayout.EndHorizontal();

            // B
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#54A0FF><b>B</b></color>", GUILayout.Width(55f));
            float newB = GUILayout.HorizontalSlider(b, 0f, 1f, GUILayout.ExpandWidth(true));
            if (Mathf.Abs(newB - b) > 0.002f) { b = newB; sliderChanged = true; }
            GUILayout.Label($"<b>{Mathf.RoundToInt(b * 255f),3}</b> <color=#7088A8><size=10>({b * 100f:F0}%)</size></color>", MFPGuiSkin.ValueFieldStyle, GUILayout.Width(72f));
            GUILayout.EndHorizontal();

            // A (Alpha)
            if (slot.HasAlpha)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#FFFFFF><b>A</b></color>", GUILayout.Width(55f));
                float newA = GUILayout.HorizontalSlider(a, 0f, 1f, GUILayout.ExpandWidth(true));
                if (Mathf.Abs(newA - a) > 0.002f) { a = newA; sliderChanged = true; }
                GUILayout.Label($"<b>{Mathf.RoundToInt(a * 100f),3}%</b> <color=#7088A8><size=10>({a:F2})</size></color>", MFPGuiSkin.ValueFieldStyle, GUILayout.Width(72f));
                GUILayout.EndHorizontal();
            }

            if (sliderChanged)
            {
                Color updated = new Color(r, g, b, slot.HasAlpha ? a : 1f);
                ApplyColor(slot, current, updated, true);
            }

            GUILayout.Space(4f);

            // 6. 常用航电色标快速点选
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#7088A8><size=10>{I18n.Tr("THM_QUICK_PRESETS", "常用航电色标快速点选:")}</size></color>", GUILayout.Width(135f));

            for (int q = 0; q < QuickColorPresets.Length; q++)
            {
                var qp = QuickColorPresets[q];
                if (GUILayout.Button($"<color={qp.Hex}>■</color> <size=10>{I18n.Tr(qp.Key, qp.Fallback)}</size>", MFPGuiSkin.StepperButtonStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true)))
                {
                    if (ColorUtility.TryParseHtmlString(qp.Hex, out Color qc))
                    {
                        if (slot.HasAlpha) qc.a = curCol.a;
                        ApplyColor(slot, current, qc, true);
                    }
                }
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndInset();
        }

        #endregion

        #region Module 2: Font & Display Shader Controls

        private void ApplyDisplayAndFontChange(ThemeConfig current)
        {
            WidgetStyleManager.Instance?.ClearMaterialCache();
            WidgetStyleManager.Instance?.InvalidatePalette();
            ThemeManager.Instance?.SaveCustomTheme(current);
            ThemeManager.Instance?.NotifyThemeChanged();
            MFPGuiSkin.EnsureInitialized(forceRebuild: true);
            FlightHUDManager.Instance?.RebuildHUD(forceFullRebuild: false);
        }

        private void DrawDisplayShaderAndFontCard()
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
                ApplyDisplayAndFontChange(current);
            }

            bool isPixel = current.FontStyle == AvionicsFontStyle.RetroPixel;
            GUIStyle pixelStyle = isPixel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string pixelLabel = isPixel ? I18n.Tr("THM_FONT_PIXEL_ON", "● 硬件等宽点阵像素 (Retro Pixel)") : I18n.Tr("THM_FONT_PIXEL_OFF", "○ 硬件等宽点阵像素 (Retro Pixel)");
            if (GUILayout.Button(pixelLabel, pixelStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                current.FontStyle = AvionicsFontStyle.RetroPixel;
                ApplyDisplayAndFontChange(current);
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
                    ApplyDisplayAndFontChange(current);
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
                        ApplyDisplayAndFontChange(current);
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
                        ApplyDisplayAndFontChange(current);
                    }
                }
                GUILayout.EndHorizontal();

                MFPGuiSkin.EndInset();
            }

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Module 3: Stock UI Integration 2x3 Matrix

        private void DrawStockIntegrationCard()
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

            string tb2 = curTbMode == 2 ? I18n.Tr("THM_TB_DOCK_ON", "● 航电收纳坞 (2)") : I18n.Tr("THM_TB_DOCK_OFF", "○ 航电收纳坞 (2)");
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

            // 收纳坞折叠规则列表
            if (curTbMode == 2)
            {
                GUILayout.Space(4f);
                DrawDockRulesConfig();
            }

            MFPGuiSkin.EndCard();
        }

        private void DrawCustomNavballChip()
        {
            var navCfg = WidgetLayoutManager.Instance.GetConfig("core.navball");
            bool isCustomEnabled = navCfg == null || navCfg.IsEnabled;

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("THM_CUSTOM_NAVBALL", "MFP 姿态球 (核心)"), GUILayout.ExpandWidth(true));

            GUIStyle chipStyle = isCustomEnabled ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string chipText = isCustomEnabled ? I18n.Tr("THM_STATUS_ENABLED", "● 已启用") : I18n.Tr("THM_STATUS_DISABLED", "○ 已停用");

            if (GUILayout.Button(chipText, chipStyle, GUILayout.Width(75f), GUILayout.Height(22f)))
            {
                if (navCfg != null)
                {
                    navCfg.IsEnabled = !navCfg.IsEnabled;
                }
                else
                {
                    navCfg = new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "3D 姿态球"), 0f, 0f) { IsEnabled = false };
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets.Add(navCfg);
                }
                WidgetLayoutManager.Instance.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD();
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();
        }

        private void DrawStockToggleChip(string title, bool isHidden, Action<bool> onToggle)
        {
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.ExpandWidth(true));

            GUIStyle chipStyle = isHidden ? MFPGuiSkin.WarningButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
            string chipText = isHidden ? I18n.Tr("THM_STATUS_HIDDEN", "● 已隐藏") : I18n.Tr("THM_STATUS_VISIBLE", "○ 正常显示");

            if (GUILayout.Button(chipText, chipStyle, GUILayout.Width(85f), GUILayout.Height(22f)))
            {
                onToggle?.Invoke(!isHidden);
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();
        }

        private void DrawDockRulesConfig()
        {
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
                GUILayout.Space(4f);
                _dockRulesScrollPos = GUILayout.BeginScrollView(_dockRulesScrollPos, GUILayout.Height(180f));
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    GUILayout.BeginHorizontal();

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

                    GUIStyle visStyle = r.IsVisible ? MFPGuiSkin.SuccessButtonStyle : MFPGuiSkin.SecondaryButtonStyle;
                    string visText = r.IsVisible ? I18n.Tr("THM_DOCK_ITEM_SHOW", "显") : I18n.Tr("THM_DOCK_ITEM_HIDE", "隐");
                    if (GUILayout.Button(visText, visStyle, GUILayout.Width(35f), GUILayout.Height(20f)))
                    {
                        r.IsVisible = !r.IsVisible;
                        ThemeManager.Instance.SaveSettings();
                        ModernToolbarWidget.Instance?.RefreshToolbarButtons();
                        FavoriteToolbarWidget.Instance?.RefreshToolbarButtons();
                    }

                    string btnName = !string.IsNullOrEmpty(r.DisplayName) ? r.DisplayName : r.Key;
                    GUILayout.Label(btnName, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
        }

        #endregion

        #region Module 4: Navball & Pipeline Engine

        private void DrawNavballAndPipelineCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("THM_HEADER_PIPELINE", "🌐 3D 姿态球渲染引擎与底层着色管线"));

            // 1. 姿态球贴图与程序化矢量渲染模式
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("THM_NAVBALL_MODE_LABEL", "姿态球渲染模式:")}</b>", GUILayout.Width(130f));

            var curMode = ThemeManager.Instance.GlobalRenderMode;

            bool isStockTex = (curMode == NavballRenderMode.StockTexture);
            string stockTexLabel = isStockTex ? I18n.Tr("THM_MODE_STOCK_TEX_ON", "● 原版高保真贴图") : I18n.Tr("THM_MODE_STOCK_TEX_OFF", "○ 原版高保真贴图");
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

            MFPGuiSkin.EndCard();
        }

        #endregion
    }
}
