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
    /// 核心功能：
    /// 1. 当前活动布局档案 (PluginData/layout.json) 元数据、全盘缩放倍率滑块及快捷预设。
    /// 2. 磁盘持久化管理：保存、热重载、创建独立备份 (layout.backup.json)、恢复备份、恢复出厂。
    /// 3. 载具专属配置引擎 (Per-Vessel Layout Engine)：切船自动切换独立配置，彻底杜绝切船丢配置。
    /// 4. 预设模板库 (Preset Library)：出厂精选与本地 Presets/*.json 预设库导入与导出。
    /// 5. 社区分享码中心 (Share Code Hub)：单行 Base64 GZip 压缩码剪贴板互通。
    /// 6. 主题配置文件 (PluginData/theme_settings.json) 与调色板工坊。
    /// </summary>
    public static class TabProfilesConfig
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;
        private static string _inputShareCode = "";
        private static string _savePresetName = "";
        private static bool _showThemeWorkshop = false;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical(GUILayout.Height(SettingsGUI.ContentHeight));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(SettingsGUI.ContentHeight));

            // 1. Toast 状态通知
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            // =========================================================================
            // 卡片 1: 活动主布局文件与全局缩放倍率 (Active Layout & Global Scale)
            // =========================================================================
            DrawActiveLayoutCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 卡片 2: 载具专属配置引擎 (Per-Vessel Layout Engine)
            // =========================================================================
            DrawPerVesselEngineCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 卡片 3: 精选出厂与本地预设模板库 (Preset Templates Library)
            // =========================================================================
            DrawPresetTemplatesCard();

            GUILayout.Space(6f);

            // =========================================================================
            // 卡片 4: 社区分享码中心 (Share Code Hub)
            // =========================================================================
            DrawShareCodeCard();

            GUILayout.Space(6f);

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
            MFPGuiSkin.DrawHeader("📋 当前活动座舱排版 (Active Layout: layout.json)", "主持久化配置文件与全局缩放倍率");

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
            GUILayout.Label($"<b>主配置文件:</b> <color=#00E5FF>layout.json</color> ({fileInfo})", GUILayout.ExpandWidth(true));
            MFPGuiSkin.DrawBadge($"已挂载 {enabledWidgets}/{totalWidgets} 组件", Color.white, MFPGuiSkin.AccentCyan);
            GUILayout.EndHorizontal();

            string backupInfo = layoutMgr.GetBackupFileInfo();
            GUILayout.Label($"<color=#7088A8><size=10>• 自动备份状态: {backupInfo}</size></color>");
            MFPGuiSkin.EndInset();

            GUILayout.Space(5f);

            // 全局缩放倍率滑块与推荐倍率快捷按钮
            float currentScale = layoutData != null ? layoutData.GlobalScale : 1.25f;
            if (currentScale < 0.5f) currentScale = 1.25f;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>全局尺寸倍率 (Global Scale):</b> <color=#00E5FF><b>{currentScale:F2}x</b></color>", GUILayout.Width(250f));
            float newScale = GUILayout.HorizontalSlider(currentScale, 0.8f, 2.0f, GUILayout.ExpandWidth(true));
            newScale = Mathf.Round(newScale * 20f) / 20f; // 0.05 步进
            if (Mathf.Abs(newScale - currentScale) > 0.01f)
            {
                ApplyGlobalScale(newScale);
            }
            GUILayout.EndHorizontal();

            // 预设快捷缩放按钮
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#7088A8>推荐快捷尺寸:</color>", GUILayout.Width(100f));
            float[] scalePresets = new float[] { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };
            string[] scaleLabels = new string[] { "1.0x (原生紧凑)", "1.25x (推荐清晰★)", "1.5x (视网膜大字)", "1.75x (大屏)", "2.0x (巨幕)" };

            for (int i = 0; i < scalePresets.Length; i++)
            {
                float sp = scalePresets[i];
                bool isSelected = Mathf.Abs(currentScale - sp) < 0.02f;
                GUIStyle btnStyle = isSelected ? MFPGuiSkin.PrimaryButtonStyle : MFPGuiSkin.StepperButtonStyle;
                if (GUILayout.Button(scaleLabels[i], btnStyle, GUILayout.Height(22f)))
                {
                    ApplyGlobalScale(sp);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 核心持久化操作按钮组
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("💾 保存当前布局到磁盘", MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
            {
                layoutMgr.SaveLayout();
                SetToast("✔ 布局已成功保存至磁盘 (含自动备份副本 layout.backup.json)！");
            }

            if (GUILayout.Button("🔄 从磁盘热重载", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(130f)))
            {
                bool ok = layoutMgr.ReloadFromDisk();
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast("✔ 已成功从磁盘热重载 layout.json！");
                }
                else
                {
                    SetToast("<color=#FF4444>重载失败: 磁盘文件不存在或损坏</color>");
                }
            }

            if (GUILayout.Button("📑 创建独立备份", MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(120f)))
            {
                bool ok = layoutMgr.CreateManualBackup();
                if (ok) SetToast("✔ 已成功生成独立备份副本 layout.backup.json！");
                else SetToast("<color=#FF4444>创建备份失败</color>");
            }

            if (GUILayout.Button("⏪ 恢复备份", MFPGuiSkin.SecondaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(90f)))
            {
                bool ok = layoutMgr.RestoreFromBackup();
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast("✔ 已成功从备份文件恢复并刷新界面！");
                }
                else
                {
                    SetToast("<color=#FF4444>未找到有效的备份文件 layout.backup.json</color>");
                }
            }

            if (GUILayout.Button("⚠ 恢复默认", MFPGuiSkin.DangerButtonStyle, GUILayout.Height(26f), GUILayout.Width(90f)))
            {
                layoutMgr.ResetToDefault();
                FlightHUDManager.Instance?.RebuildHUD();
                SetToast("已恢复出厂默认布局配置！");
            }

            GUILayout.EndHorizontal();

            MFPGuiSkin.EndCard();
        }

        private static void ApplyGlobalScale(float scale)
        {
            var layoutMgr = WidgetLayoutManager.Instance;
            if (layoutMgr.CurrentLayout != null)
            {
                layoutMgr.CurrentLayout.GlobalScale = scale;
                layoutMgr.SaveLayout();
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
            MFPGuiSkin.DrawHeader("🚀 载具专属配置引擎 (Per-Vessel Layout Engine)", "支持为不同飞船保存专属座舱，切船自动响应");

            var layoutMgr = WidgetLayoutManager.Instance;
            string currentVesselName = GetCurrentVesselName();
            bool hasVessel = !string.IsNullOrEmpty(currentVesselName) && currentVesselName != "未处于飞行状态";
            bool hasProfile = hasVessel && layoutMgr.HasVesselProfile(currentVesselName);

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>当前载具名称:</b> <color=#00E5FF><b>{(string.IsNullOrEmpty(currentVesselName) ? "无" : currentVesselName)}</b></color>", GUILayout.ExpandWidth(true));

            if (!hasVessel)
            {
                MFPGuiSkin.DrawBadge("地面整备 / 航天中心", Color.gray, MFPGuiSkin.TextMuted);
            }
            else if (hasProfile)
            {
                MFPGuiSkin.DrawBadge("已绑定载具专属配置", Color.white, MFPGuiSkin.AccentGreen);
            }
            else
            {
                MFPGuiSkin.DrawBadge("使用通用主配置 (Default)", Color.white, MFPGuiSkin.AccentCyan);
            }
            GUILayout.EndHorizontal();

            if (hasVessel)
            {
                string vesselPath = Path.Combine(layoutMgr.VesselsDir, $"{currentVesselName}.json");
                string detail = hasProfile ? $"独立配置文件: PluginData/Vessels/{currentVesselName}.json" : "当前使用主配置 layout.json，点击下方按钮可为此飞船脱钩保存独立配置。";
                GUILayout.Label($"<color=#7088A8><size=10>• {detail}</size></color>");
            }
            MFPGuiSkin.EndInset();

            GUILayout.Space(5f);

            // 操作按钮
            GUILayout.BeginHorizontal();

            GUI.enabled = hasVessel;
            if (GUILayout.Button("📌 为当前载具保存独立布局", MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
            {
                bool ok = layoutMgr.SaveVesselLayout(currentVesselName);
                if (ok)
                {
                    SetToast($"✔ 已为「{currentVesselName}」生成专属独立配置！切船将自动载入。");
                }
                else
                {
                    SetToast("<color=#FF4444>保存载具专属配置失败</color>");
                }
            }

            if (GUILayout.Button("🔄 重新载入载具专属配置", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.Width(170f)))
            {
                bool ok = layoutMgr.LoadVesselLayout(currentVesselName);
                if (ok)
                {
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast($"✔ 已成功加载「{currentVesselName}」专属配置！");
                }
                else
                {
                    SetToast($"<color=#FF4444>该载具尚无独立配置，已维持通用布局</color>");
                }
            }

            GUI.enabled = hasVessel && hasProfile;
            if (GUILayout.Button("🗑 解绑并恢复通用配置", MFPGuiSkin.DangerButtonStyle, GUILayout.Height(26f), GUILayout.Width(160f)))
            {
                bool ok = layoutMgr.DeleteVesselProfile(currentVesselName);
                if (ok)
                {
                    layoutMgr.ReloadFromDisk();
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast($"已解除「{currentVesselName}」独立配置，恢复使用通用主布局！");
                }
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label("<color=#7088A8><size=10>说明：开启载具专属配置后，每次切入该飞船会自动优先加载 Vessels/{飞船名}.json；普通保存也会同步更新该载具配置，彻底根除切船丢失布局问题。</size></color>");

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
            return "未处于飞行状态";
        }

        #endregion

        #region Card 3: Preset Templates Library

        private static void DrawPresetTemplatesCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🌟 精选出厂与本地预设模板库 (Preset Library)", "一键套用调校好的工效学座舱与本地模板");

            List<PresetInfo> presets = LayoutShareHub.GetAvailablePresets();
            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo p = presets[i];
                MFPGuiSkin.BeginInset();
                GUILayout.BeginHorizontal();

                string tag = p.IsBuiltIn ? "[出厂预置]" : "[本地模板]";
                Color tagCol = p.IsBuiltIn ? MFPGuiSkin.AccentCyan : MFPGuiSkin.AccentGreen;
                GUILayout.Label($"<b>{p.Name}</b>", GUILayout.ExpandWidth(true));
                MFPGuiSkin.DrawBadge(tag, Color.white, tagCol);

                if (GUILayout.Button("⚡ 一键套用此预设", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(140f), GUILayout.Height(24f)))
                {
                    WidgetLayoutData presetData = LayoutShareHub.LoadPreset(p);
                    if (presetData != null && presetData.Widgets != null)
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets = presetData.Widgets;
                        WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = presetData.GlobalScale > 0.1f ? presetData.GlobalScale : 1.25f;
                        WidgetLayoutManager.Instance.SaveLayout();
                        FlightHUDManager.Instance?.RebuildHUD();
                        SetToast($"已成功套用预设「{p.Name}」！");
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"<color=#7088A8><size=10>{p.Description}</size></color>");
                MFPGuiSkin.EndInset();
                GUILayout.Space(3f);
            }

            GUILayout.Space(4f);

            // 另存为新预设文件
            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label("另存新预设:", GUILayout.Width(75f));
            _savePresetName = GUILayout.TextField(_savePresetName ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(24f), GUILayout.Width(220f));

            if (GUILayout.Button("💾 保存到本地 Presets 文件夹", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
            {
                if (LayoutShareHub.SavePresetToFile(_savePresetName, WidgetLayoutManager.Instance.CurrentLayout, out string error))
                {
                    SetToast($"✔ 已保存预设「{_savePresetName}」至 Presets 目录！");
                    _savePresetName = "";
                }
                else
                {
                    SetToast($"<color=#FF4444>保存失败: {error}</color>");
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            MFPGuiSkin.EndCard();
        }

        #endregion

        #region Card 4: Share Code Hub

        private static void DrawShareCodeCard()
        {
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🔗 社区分享码中心 (Layout Share Code Hub)", "一键复制/粘贴单行 GZip 压缩 Base64 编码");

            // 导出分享码
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 复制当前布局分享码到剪贴板", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    SetToast("✔ 已成功复制分享码至剪贴板！可直接粘贴发送给社区好友 (Ctrl+V)");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            // 导入分享码
            GUILayout.BeginHorizontal();
            GUILayout.Label("分享码:", GUILayout.Width(55f));
            _inputShareCode = GUILayout.TextField(_inputShareCode ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("粘贴剪贴板", MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(85f), GUILayout.Height(24f)))
            {
                _inputShareCode = GUIUtility.systemCopyBuffer;
            }

            if (GUILayout.Button("📥 导入并套用", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(105f), GUILayout.Height(24f)))
            {
                if (LayoutShareHub.TryImportShareCode(_inputShareCode, out WidgetLayoutData importedLayout, out string error))
                {
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets = importedLayout.Widgets;
                    WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = importedLayout.GlobalScale > 0.1f ? importedLayout.GlobalScale : 1.25f;
                    WidgetLayoutManager.Instance.SaveLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                    SetToast($"✔ 成功导入并套用布局！(共加载 {importedLayout.Widgets.Count} 个组件)");
                }
                else
                {
                    SetToast($"<color=#FF4444>导入失败: {error}</color>");
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
            MFPGuiSkin.DrawHeader("🎨 主题配置与调色板 (Theme Settings & Palette)", "管理 theme_settings.json 存储与色彩微调");

            string themeFile = Path.Combine(ModularFlightPanel.Core.AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/theme_settings.json");
            string themeInfo = "文件未创建 (使用内置默认)";
            if (File.Exists(themeFile))
            {
                FileInfo fi = new FileInfo(themeFile);
                themeInfo = $"大小: {fi.Length / 1024f:F1} KB | 修改: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
            }

            MFPGuiSkin.BeginInset();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>主题配置文件:</b> <color=#00E5FF>theme_settings.json</color> ({themeInfo})", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("💾 保存主题设置到磁盘", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(160f), GUILayout.Height(22f)))
            {
                ThemeManager.Instance.SaveSettings();
                SetToast("✔ 主题偏好设置已成功保存至 theme_settings.json！");
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndInset();

            GUILayout.Space(4f);

            // 可折叠主题调色板微调
            string fold = _showThemeWorkshop ? "▼" : "▶";
            if (GUILayout.Button($"<b>{fold} 自定义调色板微调工坊 (Theme Palette Workshop)</b>", "label", GUILayout.ExpandWidth(true)))
            {
                _showThemeWorkshop = !_showThemeWorkshop;
            }

            if (_showThemeWorkshop)
            {
                var cur = ThemeManager.Instance.CurrentTheme;
                if (cur != null)
                {
                    MFPGuiSkin.BeginInset();
                    DrawColorEditorRow("主强调色 (Accent Primary)", ref cur.AccentPrimary);
                    DrawColorEditorRow("副强调色 (Accent Secondary)", ref cur.AccentSecondary);
                    DrawColorEditorRow("面板底板色 (Frame Background)", ref cur.FrameBgColor);
                    DrawColorEditorRow("面板边框色 (Frame Border)", ref cur.FrameBorderColor);
                    DrawColorEditorRow("主读数文字色 (Text Primary)", ref cur.TextPrimaryColor);

                    GUILayout.Space(4f);
                    if (GUILayout.Button("应用调色板并保存", MFPGuiSkin.SuccessButtonStyle, GUILayout.Height(22f)))
                    {
                        ThemeManager.Instance.SaveSettings();
                        ThemeManager.Instance.NotifyThemeChanged();
                        SetToast("✔ 主题颜色微调已保存并应用！");
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
