using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 全新社区分享中枢与预设模板库 (Avionics Sharing Hub & Presets)
    /// 核心功能：
    /// 1. 一键生成单行 Base64 GZip 压缩分享码，支持系统剪贴板互通。
    /// 2. 预设库卡片化呈现（出厂精选与本地预设）。
    /// 3. 全量接入 MFPGuiSkin 现代黑晶设计系统 2.0。
    /// </summary>
    public static class TabSharePresets
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static string _inputShareCode = "";
        private static string _savePresetName = "";
        private static string _toastMsg = "";
        private static float _toastTimer = 0f;

        public static void Draw()
        {
            MFPGuiSkin.EnsureInitialized();

            GUILayout.BeginVertical();

            // Toast 提示
            MFPGuiSkin.DrawToast(ref _toastMsg, ref _toastTimer);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 1. 导出分享码
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("📋 导出当前座舱排版分享码 (Export Share Code)", "一键复制到剪贴板，发送给社区好友");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 复制当前布局分享码到剪贴板", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    _toastMsg = "已成功复制分享码至系统剪贴板！可直接粘贴发送 (Ctrl+V)";
                    _toastTimer = 3.5f;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.Label("<color=#7088A8><size=10>生成的分享码包含所有组件坐标、尺寸、阈值与显隐状态，采用 GZip 压缩为紧凑单行文本。</size></color>");
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 2. 导入分享码
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("📥 导入社区分享码 (Import Share Code)", "粘贴他人分享的 Base64 编码并套用");

            GUILayout.BeginHorizontal();
            GUILayout.Label("分享码:", GUILayout.Width(60f));
            _inputShareCode = GUILayout.TextField(_inputShareCode ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(24f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("粘贴剪贴板", MFPGuiSkin.SecondaryButtonStyle, GUILayout.Width(90f), GUILayout.Height(24f)))
            {
                _inputShareCode = GUIUtility.systemCopyBuffer;
            }

            if (GUILayout.Button("📥 导入并套用", MFPGuiSkin.SuccessButtonStyle, GUILayout.Width(110f), GUILayout.Height(24f)))
            {
                if (LayoutShareHub.TryImportShareCode(_inputShareCode, out WidgetLayoutData importedLayout, out string error))
                {
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets = importedLayout.Widgets;
                    WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = importedLayout.GlobalScale;
                    WidgetLayoutManager.Instance.SaveLayout();
                    NavballHUD.Instance?.RebuildHUD();
                    _toastMsg = $"成功导入并套用布局！(共加载 {importedLayout.Widgets.Count} 个组件)";
                    _toastTimer = 3.0f;
                }
                else
                {
                    _toastMsg = $"<color=#FF4444>导入失败: {error}</color>";
                    _toastTimer = 3.5f;
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 3. 预设模板库
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("🌟 精选出厂与本地预设模板库 (Preset Library)", "一键套用调校好的工效学座舱");

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
                        WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = presetData.GlobalScale;
                        WidgetLayoutManager.Instance.SaveLayout();
                        NavballHUD.Instance?.RebuildHUD();
                        _toastMsg = $"已成功套用预设「{p.Name}」！";
                        _toastTimer = 3.0f;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"<color=#7088A8><size=10>{p.Description}</size></color>");
                MFPGuiSkin.EndInset();
                GUILayout.Space(3f);
            }
            MFPGuiSkin.EndCard();

            GUILayout.Space(6f);

            // 4. 另存为本地新预设
            MFPGuiSkin.BeginCard();
            MFPGuiSkin.DrawHeader("💾 另存为新预设文件 (Save Preset File)", "将当前排版保存至 PluginData/Presets 文件夹");

            GUILayout.BeginHorizontal();
            GUILayout.Label("预设名称:", GUILayout.Width(75f));
            _savePresetName = GUILayout.TextField(_savePresetName ?? "", MFPGuiSkin.SearchFieldStyle, GUILayout.Height(24f), GUILayout.Width(220f));

            if (GUILayout.Button("💾 保存到本地 Presets 库", MFPGuiSkin.PrimaryButtonStyle, GUILayout.Width(180f), GUILayout.Height(24f)))
            {
                if (LayoutShareHub.SavePresetToFile(_savePresetName, WidgetLayoutManager.Instance.CurrentLayout, out string error))
                {
                    _toastMsg = $"已保存预设「{_savePresetName}」至 Presets 目录！";
                    _toastTimer = 3.0f;
                    _savePresetName = "";
                }
                else
                {
                    _toastMsg = $"<color=#FF4444>保存失败: {error}</color>";
                    _toastTimer = 3.0f;
                }
            }
            GUILayout.EndHorizontal();
            MFPGuiSkin.EndCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
