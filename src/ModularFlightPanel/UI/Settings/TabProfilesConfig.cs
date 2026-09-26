using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新独立配置文件与档案管理中枢 (Avionics Profiles & Configuration Management)
    /// 核心优化：
    /// 1. 彻底移除重复的语言卡片，首屏直达核心档案管理。
    /// 2. 预设模板库采用紧凑过滤列表 (Master-Detail List)，由原先 400px+ 冗余铺展压缩至 160px，消除重复套用大按钮。
    /// 3. 全局缩放滑块与推荐药丸单行集成，磁盘操作工具条规整对齐，杜绝满宽拉伸。
    /// 4. 修复 TrFormat 格式化导致的「大小 [0.F1] KB」字符错乱。
    /// 5. 社区分享码中心与主题调色板顺畅上移，无需海量滚动即可操作。
    /// </summary>
    public static class TabProfilesConfig
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;
        private static string _inputShareCode = "";
        private static string _savePresetName = "";
        private static bool _showThemeWorkshop = false;
        private static int _presetFilterCategory = 0; // 0: 全部, 1: 出厂精选, 2: 本地自建
        private static Vector2 _presetListScrollPos = Vector2.zero;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight));

            // 1. Toast 状态通知
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            // =========================================================================
            // 卡片 1: 活动主布局文件与全局缩放倍率 (Active Layout & Global Scale)
            // =========================================================================
            DrawActiveLayoutCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 2: 载具专属配置引擎 (Per-Vessel Layout Engine)
            // =========================================================================
            DrawPerVesselEngineCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 3: 精选出厂与本地预设模板库 (Preset Templates Compact Hub)
            // =========================================================================
            DrawPresetTemplatesCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 4: 社区分享码中心 (Share Code Hub)
            // =========================================================================
            DrawShareCodeCard();

            GUILayout.Space(5f);

            // =========================================================================
            // 卡片 5: 主题配置文件与调色板工坊 (Theme Settings & Palette)
            // =========================================================================
            DrawThemeConfigCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Card 1: Active Layout & Global Scale

        private static void DrawActiveLayoutCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("PRF_HEADER_ACTIVE", "📋 当前活动座舱排版 (Active Layout: layout.json)"),
                I18n.Tr("PRF_SUBHEADER_ACTIVE", "主持久化配置文件与全局缩放倍率"));

            var layoutMgr = WidgetLayoutManager.Instance;
            var layoutData = layoutMgr.CurrentLayout;
            string fileInfo = layoutMgr.GetLayoutFileInfo();

            // 文件元数据与组件统计
            MFPGuiSkin.BeginInset();
            int totalWidgets = layoutData != null && layoutData.Widgets != null ? layoutData.Widgets.Count : 0;
            int enabledWidgets = 0;
            if (layoutData != null && layoutData.Widgets != null)
            {
                for (int i = 0; i < layoutData.Widgets.Count; i++)
                {
                    if (layoutData.Widgets[i].IsEnabled) enabledWidgets++;
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("PRF_MAIN_CONFIG", "主配置文件:")}</b> <color=#00E5FF>layout.json</color> ({fileInfo})", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge(I18n.TrFormat("PRF_MOUNTED_BADGE", enabledWidgets, totalWidgets), Color.white, MFPGuiSkin.AccentCyan);
            GUILayout.EndHorizontal();

            string backupInfo = layoutMgr.GetBackupFileInfo();
            GUILayout.Label($"<color=#7088A8><size=10>• {I18n.Tr("PRF_BACKUP_STATUS", "自动备份状态:")} {backupInfo}</size></color>");
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 全局缩放倍率滑块与推荐倍率快捷按钮（整合为紧凑单行）
            float currentScale = layoutData != null ? layoutData.GlobalScale : 1.25f;
            if (currentScale < 0.5f) currentScale = 1.25f;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("PRF_GLOBAL_SCALE", "全局尺寸倍率:")}</b> <color=#00E5FF><b>{currentScale:F2}x</b></color>", GUILayout.Width(170f));
            float newScale = GUILayout.HorizontalSlider(currentScale, 0.8f, 2.0f, GUILayout.Width(180f));
            newScale = Mathf.Round(newScale * 20f) / 20f; // 0.05 步进
            if (Mathf.Abs(newScale - currentScale) > 0.01f)
            {
                ApplyGlobalScale(newScale);
            }

            GUILayout.Space(8f);
            float[] scalePresets = new float[] { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };
            string[] scaleLabels = new string[] { "1.0x", "1.25x★", "1.5x", "1.75x", "2.0x" };
            for (int i = 0; i < scalePresets.Length; i++)
            {
                float sp = scalePresets[i];
                bool isSelected = Mathf.Abs(currentScale - sp) < 0.02f;
                GUIStyle btnStyle = isSelected ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(scaleLabels[i], btnStyle, GUILayout.Height(20f), GUILayout.Width(50f)))
                {
                    ApplyGlobalScale(sp);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            // 核心持久化操作工具条 (消除通栏拉伸大按钮)
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(I18n.Tr("PRF_BTN_SAVE_NOW", "💾 保存当前布局"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(25f), GUILayout.Width(150f)))
            {
                layoutMgr.SaveLayout();
                SetToast(I18n.Tr("PRF_TOAST_SAVED", "✔ 布局已成功保存至磁盘 (含自动备份副本 layout.backup.json)！"));
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_RELOAD", "🔄 磁盘热重载"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(25f), GUILayout.Width(110f)))
            {
                bool ok = layoutMgr.ReloadFromDisk();
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast(I18n.Tr("PRF_TOAST_RELOADED", "✔ 已成功从磁盘热重载 layout.json！"));
                }
                else
                {
                    SetToast(I18n.Tr("PRF_TOAST_RELOAD_FAIL", "<color=#FF4444>重载失败: 磁盘文件不存在或损坏</color>"));
                }
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_BACKUP", "📑 创建备份"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(25f), GUILayout.Width(100f)))
            {
                bool ok = layoutMgr.CreateManualBackup();
                if (ok) SetToast(I18n.Tr("PRF_TOAST_BACKUP_OK", "✔ 已成功生成独立备份副本 layout.backup.json！"));
                else SetToast(I18n.Tr("PRF_TOAST_BACKUP_FAIL", "<color=#FF4444>创建备份失败</color>"));
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_RESTORE", "⏪ 恢复备份"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(25f), GUILayout.Width(95f)))
            {
                bool ok = layoutMgr.RestoreFromBackup();
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast(I18n.Tr("PRF_TOAST_RESTORE_OK", "✔ 已成功从备份文件恢复并刷新界面！"));
                }
                else
                {
                    SetToast(I18n.Tr("PRF_TOAST_RESTORE_FAIL", "<color=#FF4444>未找到有效的备份文件 layout.backup.json</color>"));
                }
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_RESET_DEFAULT", "⚠ 恢复出厂默认"), MFPGuiSkin.DangerButtonStyle, GUILayout.Height(25f), GUILayout.Width(120f)))
            {
                layoutMgr.ResetToDefault();
                FlightHUDManager.Instance?.RebuildHUD();
                SetToast(I18n.Tr("PRF_TOAST_RESET_OK", "已恢复出厂默认布局配置！"));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void ApplyGlobalScale(float scale)
        {
            var layoutMgr = WidgetLayoutManager.Instance;
            if (layoutMgr.CurrentLayout != null)
            {
                layoutMgr.CurrentLayout.GlobalScale = scale;
                if (layoutMgr.CurrentLayout.Widgets != null && layoutMgr.CurrentLayout.Widgets.Count > 0)
                {
                    layoutMgr.SaveLayout();
                }
            }
            if (WidgetRenderManager.Instance != null)
            {
                WidgetRenderManager.Instance.SetGlobalRenderScale(scale);
            }
            FlightHUDManager.Instance?.RebuildHUD();
        }

        #endregion

        #region Card 2: Per-Vessel Profile Engine

        private static void DrawPerVesselEngineCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("PRF_HEADER_PER_VESSEL", "🚀 载具专属配置引擎"),
                I18n.Tr("PRF_SUBHEADER_PER_VESSEL", "支持为不同飞船保存专属座舱，切船自动响应"));

            var layoutMgr = WidgetLayoutManager.Instance;
            string currentVesselName = GetCurrentVesselName();
            bool hasVessel = !string.IsNullOrEmpty(currentVesselName) && currentVesselName != I18n.Tr("PRF_NOT_IN_FLIGHT", "未处于飞行状态");
            bool hasProfile = hasVessel && layoutMgr.HasVesselProfile(currentVesselName);

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("PRF_CURRENT_VESSEL", "当前载具名称:")}</b> <color=#00E5FF><b>{(string.IsNullOrEmpty(currentVesselName) ? I18n.Tr("COMMON_NONE", "无") : currentVesselName)}</b></color>", GUILayout.Width(280f));

            if (!hasVessel)
            {
                MFPGuiSkin.DrawBadge(I18n.Tr("PRF_VESSEL_GROUND", "地面整备 / 航天中心"), Color.gray, MFPGuiSkin.TextMuted);
            }
            else if (hasProfile)
            {
                MFPGuiSkin.DrawBadge(I18n.Tr("PRF_VESSEL_BOUND", "已绑定专属配置 (切船自动切)"), Color.white, MFPGuiSkin.AccentGreen);
            }
            else
            {
                MFPGuiSkin.DrawBadge(I18n.Tr("PRF_VESSEL_DEFAULT", "使用通用主配置"), Color.white, MFPGuiSkin.AccentCyan);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (hasVessel)
            {
                string detail = hasProfile
                    ? I18n.TrFormat("PRF_VESSEL_ISOLATED_PATH", currentVesselName)
                    : I18n.Tr("PRF_VESSEL_USE_MAIN_DESC", "当前使用通用配置 layout.json，点击下方按钮可为此飞船脱钩保存独立配置。");
                GUILayout.Label($"<color=#7088A8><size=10>• {detail}</size></color>");
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 操作按钮条
            GUILayout.BeginHorizontal();

            GUI.enabled = hasVessel;
            if (GUILayout.Button(I18n.Tr("PRF_BTN_SAVE_VESSEL", "📌 为当前载具保存独立布局"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(25f), GUILayout.Width(200f)))
            {
                bool ok = layoutMgr.SaveVesselLayout(currentVesselName);
                if (ok)
                {
                    SetToast(I18n.TrFormat("PRF_TOAST_VESSEL_SAVED", currentVesselName));
                }
                else
                {
                    SetToast(I18n.Tr("PRF_TOAST_SAVE_VESSEL_FAIL", "<color=#FF4444>保存载具专属配置失败</color>"));
                }
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_RELOAD_VESSEL", "🔄 重新载入载具专属"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(25f), GUILayout.Width(150f)))
            {
                bool ok = layoutMgr.LoadVesselLayout(currentVesselName);
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast(I18n.TrFormat("PRF_TOAST_VESSEL_LOADED", currentVesselName));
                }
                else
                {
                    SetToast(I18n.Tr("PRF_TOAST_NO_VESSEL_PROFILE", "<color=#FF4444>该载具尚无独立配置，已维持通用布局</color>"));
                }
            }

            GUI.enabled = hasVessel && hasProfile;
            if (GUILayout.Button(I18n.Tr("PRF_BTN_DELETE_VESSEL", "🗑 解绑并恢复通用"), MFPGuiSkin.DangerButtonStyle, GUILayout.Height(25f), GUILayout.Width(140f)))
            {
                bool ok = layoutMgr.DeleteVesselProfile(currentVesselName);
                if (ok)
                {
                    layoutMgr.ReloadFromDisk();
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast(I18n.TrFormat("PRF_TOAST_VESSEL_RESET", currentVesselName));
                }
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static string GetCurrentVesselName()
        {
#if KSP_RUNTIME
            try
            {
                if (FlightGlobals.ActiveVessel != null)
                {
                    return FlightGlobals.ActiveVessel.vesselName;
                }
            }
            catch { }
#endif
            return I18n.Tr("PRF_NOT_IN_FLIGHT", "未处于飞行状态");
        }

        #endregion

        #region Card 3: Preset Templates Compact Hub

        private static void DrawPresetTemplatesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("PRF_HEADER_PRESETS", "📚 精选出厂与本地预设模板库"),
                I18n.Tr("PRF_SUBHEADER_PRESETS", "紧凑列表选择，一键套用调校好的工效学座舱"));

            List<PresetInfo> presets = LayoutShareHub.GetAvailablePresets();
            int builtInCount = 0;
            int localCount = 0;
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].IsBuiltIn) builtInCount++;
                else localCount++;
            }

            // 1. 分类过滤药丸条
            GUILayout.BeginHorizontal();
            string[] filterTabs = new string[]
            {
                I18n.TrFormat("PRESET_TAB_ALL", presets.Count),
                I18n.TrFormat("PRESET_TAB_BUILTIN", builtInCount),
                I18n.TrFormat("PRESET_TAB_LOCAL", localCount)
            };

            for (int f = 0; f < filterTabs.Length; f++)
            {
                bool isSel = (_presetFilterCategory == f);
                GUIStyle tabStyle = isSel ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(filterTabs[f], tabStyle, GUILayout.Height(22f), GUILayout.MinWidth(95f)))
                {
                    _presetFilterCategory = f;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 2. 紧凑滚动列表 (高度限定 160px，消除 400px+ 纵向冗余)
            _presetListScrollPos = GUILayout.BeginScrollView(_presetListScrollPos, GUILayout.MaxHeight(160f));
            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo p = presets[i];
                if (_presetFilterCategory == 1 && !p.IsBuiltIn) continue;
                if (_presetFilterCategory == 2 && p.IsBuiltIn) continue;

                GUILayout.BeginHorizontal(MFPGuiSkin.InsetStyle, GUILayout.Height(26f));

                string tag = p.IsBuiltIn ? I18n.Tr("PRF_TAG_BUILTIN", "[出厂]") : I18n.Tr("PRF_TAG_LOCAL", "[本地]");
                Color tagCol = p.IsBuiltIn ? MFPGuiSkin.AccentCyan : MFPGuiSkin.AccentGreen;
                MFPGuiSkin.DrawBadge(tag, Color.white, tagCol, 52f);

                GUILayout.Label($"<b>{p.Name}</b>", GUILayout.Width(170f));
                GUILayout.Label($"<color=#7088A8><size=10>{p.Description}</size></color>", GUILayout.ExpandWidth(true));

                if (GUILayout.Button(I18n.Tr("PRF_BTN_APPLY_PRESET", "⚡ 一键套用"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(90f), GUILayout.Height(20f)))
                {
                    WidgetLayoutData presetData = LayoutShareHub.LoadPreset(p);
                    if (presetData != null && presetData.Widgets != null && presetData.Widgets.Count > 0)
                    {
                        WidgetSelectionManager.ClearSelection();
                        WidgetLayoutManager.Instance.ApplyLayout(presetData);
                        if (WidgetRenderManager.Instance != null)
                        {
                            WidgetRenderManager.Instance.SetGlobalRenderScale(presetData.GlobalScale > 0.1f ? presetData.GlobalScale : 1.25f);
                        }
                        FlightHUDManager.Instance?.RebuildHUD();
                        SetToast(I18n.TrFormat("PRF_TOAST_PRESET_APPLIED", p.Name, presetData.Widgets.Count));
                    }
                    else
                    {
                        SetToast(I18n.TrFormat("PRF_TOAST_APPLY_PRESET_FAIL", p.Name));
                    }
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);

            // 3. 另存为新预设文件 (紧凑单行)
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("PRF_LABEL_SAVE_PRESET", "另存新预设:"), GUILayout.Width(75f));
            _savePresetName = GUILayout.TextField(_savePresetName ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(22f), GUILayout.Width(220f));

            if (GUILayout.Button(I18n.Tr("PRF_BTN_SAVE_PRESET", "💾 保存到本地 Presets 文件夹"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(22f), GUILayout.Width(210f)))
            {
                if (LayoutShareHub.SavePresetToFile(_savePresetName, WidgetLayoutManager.Instance.CurrentLayout, out string error))
                {
                    SetToast(I18n.TrFormat("PRF_TOAST_PRESET_SAVED", _savePresetName));
                    _savePresetName = "";
                }
                else
                {
                    SetToast(I18n.TrFormat("PRF_TOAST_SAVE_PRESET_FAIL", error));
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 4: Share Code Hub

        private static void DrawShareCodeCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("PRF_HEADER_SHARE", "🔗 社区分享码中心"),
                I18n.Tr("PRF_SUBHEADER_SHARE", "支持 MFP:v1: 分享码、原始 JSON 文本或本地预设文件名"));

            // 导出分享码
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.Tr("PRF_BTN_COPY_SHARE", "📋 复制当前布局分享码到剪贴板"), MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(24f), GUILayout.Width(260f)))
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    SetToast(I18n.Tr("PRF_TOAST_SHARE_COPIED", "✔ 已成功复制分享码至剪贴板！可直接粘贴发送给社区好友 (Ctrl+V)"));
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);

            // 导入分享码或 JSON
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Tr("PRF_LABEL_SHARE_INPUT", "配置代码 / 路径:"), GUILayout.Width(115f));
            _inputShareCode = GUILayout.TextField(_inputShareCode ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(22f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button(I18n.Tr("PRF_BTN_PASTE_CLIPBOARD", "粘贴剪贴板"), MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(85f), GUILayout.Height(22f)))
            {
                _inputShareCode = GUIUtility.systemCopyBuffer;
            }

            if (GUILayout.Button(I18n.Tr("PRF_BTN_IMPORT_APPLY", "📥 导入并套用"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(105f), GUILayout.Height(22f)))
            {
                if (LayoutShareHub.TryImportShareCode(_inputShareCode, out WidgetLayoutData importedLayout, out string error))
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.ApplyLayout(importedLayout);
                    if (WidgetRenderManager.Instance != null)
                    {
                        WidgetRenderManager.Instance.SetGlobalRenderScale(importedLayout.GlobalScale > 0.1f ? importedLayout.GlobalScale : 1.25f);
                    }
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast(I18n.TrFormat("PRF_TOAST_IMPORT_OK", importedLayout.Widgets.Count));
                    _inputShareCode = "";
                }
                else
                {
                    SetToast(I18n.TrFormat("PRF_TOAST_IMPORT_FAIL", error));
                }
            }
            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 5: Theme Config & Workshop

        private static void DrawThemeConfigCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader(I18n.Tr("PRF_HEADER_THEME_CFG", "🎨 主题配置与调色板"),
                I18n.Tr("PRF_SUBHEADER_THEME_CFG", "管理 theme_settings.json 存储与色彩微调"));

            string themeFile = Path.Combine(ModularFlightPanel.Core.AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/theme_settings.json");
            string themeInfo = I18n.Tr("PRF_THEME_FILE_NOT_CREATED", "文件未创建 (使用内置默认)");
            if (File.Exists(themeFile))
            {
                FileInfo fi = new FileInfo(themeFile);
                themeInfo = I18n.TrFormat("CFG_FILE_SIZE_MODIFIED_FMT", fi.Length / 1024f, fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.Tr("PRF_LABEL_THEME_FILE", "主题配置文件:")}</b> <color=#00E5FF>theme_settings.json</color> ({themeInfo})", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(I18n.Tr("PRF_BTN_SAVE_THEME", "💾 保存主题设置到磁盘"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(160f), GUILayout.Height(22f)))
            {
                ThemeManager.Instance.SaveSettings();
                SetToast(I18n.Tr("PRF_TOAST_THEME_SAVED", "✔ 主题偏好设置已成功保存至 theme_settings.json！"));
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(3f);

            // 可折叠主题调色板微调
            string fold = _showThemeWorkshop ? "▼" : "▶";
            if (GUILayout.Button($"<b>{fold} {I18n.Tr("PRF_THEME_WORKSHOP", "自定义调色板微调工坊")}</b>", "label", GUILayout.ExpandWidth(true)))
            {
                _showThemeWorkshop = !_showThemeWorkshop;
            }

            if (_showThemeWorkshop)
            {
                var cur = ThemeManager.Instance.CurrentTheme;
                if (cur != null)
                {
                    MFPGuiSkin.BeginInset();
                    DrawColorEditorRow(I18n.Tr("THM_COLOR_ACCENT_PRI", "主强调色"), ref cur.AccentPrimary);
                    DrawColorEditorRow(I18n.Tr("THM_COLOR_ACCENT_SEC", "副强调色"), ref cur.AccentSecondary);
                    DrawColorEditorRow(I18n.Tr("THM_COLOR_FRAME_BG", "面板底板色"), ref cur.FrameBgColor);
                    DrawColorEditorRow(I18n.Tr("THM_COLOR_FRAME_BORDER", "面板边框色"), ref cur.FrameBorderColor);
                    DrawColorEditorRow(I18n.Tr("THM_COLOR_TEXT_PRI", "主读数文字色"), ref cur.TextPrimaryColor);

                    GUILayout.Space(4f);
                    if (GUILayout.Button(I18n.Tr("PRF_BTN_APPLY_PALETTE", "应用调色板并保存"), MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(22f)))
                    {
                        ThemeManager.Instance.SaveSettings();
                        ThemeManager.Instance.NotifyThemeChanged();
                        SetToast(I18n.Tr("PRF_TOAST_PALETTE_SAVED", "✔ 主题颜色微调已保存并应用！"));
                    }
                    MFPGuiSkin.EndInset();
                }
            }

            MFPGuiSkin.EndCard();
        }

        private static void DrawColorEditorRow(string label, ref ColorHex ch)
        {
            Color c = ch.ToColor();
            GUILayout.BeginHorizontal();
            string hexStr = ColorUtility.ToHtmlStringRGBA(c);
            GUILayout.Label($"<color=#{hexStr}>■</color> <b>{label}:</b>", GUILayout.Width(200f));

            GUILayout.Label("R", GUILayout.Width(14f));
            float r = GUILayout.HorizontalSlider(c.r, 0f, 1f, GUILayout.Width(50f));
            GUILayout.Label("G", GUILayout.Width(14f));
            float g = GUILayout.HorizontalSlider(c.g, 0f, 1f, GUILayout.Width(50f));
            GUILayout.Label("B", GUILayout.Width(14f));
            float b = GUILayout.HorizontalSlider(c.b, 0f, 1f, GUILayout.Width(50f));
            GUILayout.Label("A", GUILayout.Width(14f));
            float a = GUILayout.HorizontalSlider(c.a, 0f, 1f, GUILayout.Width(40f));

            GUILayout.Label($"#{hexStr}", GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(r - c.r) > 0.005f || Mathf.Abs(g - c.g) > 0.005f || Mathf.Abs(b - c.b) > 0.005f || Mathf.Abs(a - c.a) > 0.005f)
            {
                ch = ColorHex.FromColor(new Color(r, g, b, a));
                ThemeManager.Instance.NotifyThemeChanged();
            }
        }

        #endregion

        private static void SetToast(string msg)
        {
            _toastMsg = msg;
            _toastTimer = 3.5f;
        }
    }
}
