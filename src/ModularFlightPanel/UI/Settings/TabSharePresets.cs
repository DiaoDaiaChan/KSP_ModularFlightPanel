using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Settings
{
    /// <summary>
    /// 社区分享中枢与预设模板库 (Sharing Hub & Presets)
    /// 一键生成单行 Base64 GZip 压缩分享码，并支持导入还原与出厂预设一键套用
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
            GUILayout.BeginVertical();

            // Toast 提示
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toastMsg))
            {
                _toastTimer -= Time.deltaTime;
                GUI.color = Color.green;
                GUILayout.Label($"<b>✔ {_toastMsg}</b>");
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label("<color=#AAAAAA><size=11>使用分享码可将当前全套仪表排版一键复制发送给社区好友；亦可一键套用出厂调校好的工效学座舱。</size></color>");
            }

            GUILayout.Space(6f);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // 1. 导出分享码
            GUILayout.Label("<b>▼ 导出当前座舱排版分享码 (Export Share Code)</b>");
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("📋 复制当前布局分享码到剪贴板", GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    _toastMsg = "已成功复制分享码至系统剪贴板！可直接在聊天软件或论坛粘贴 (Ctrl+V)";
                    _toastTimer = 3.5f;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#888888><size=10>生成的分享码包含所有组件坐标、尺寸、阈值与显隐状态，采用 GZip 压缩为紧凑单行文本。</size></color>");
            GUILayout.EndVertical();

            GUILayout.Space(12f);

            // 2. 导入分享码
            GUILayout.Label("<b>▼ 导入社区分享码 (Import Share Code)</b>");
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>分享码:</b>", GUILayout.Width(60f));
            _inputShareCode = GUILayout.TextField(_inputShareCode, GUILayout.Height(22f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("粘贴剪贴板", GUILayout.Width(90f), GUILayout.Height(22f)))
            {
                _inputShareCode = GUIUtility.systemCopyBuffer;
            }

            GUI.color = Color.cyan;
            if (GUILayout.Button("📥 导入并套用", GUILayout.Width(110f), GUILayout.Height(22f)))
            {
                if (LayoutShareHub.TryImportShareCode(_inputShareCode, out WidgetLayoutData importedLayout, out string error))
                {
                    WidgetLayoutManager.Instance.CurrentLayout.Widgets = importedLayout.Widgets;
                    WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = importedLayout.GlobalScale;
                    WidgetLayoutManager.Instance.SaveLayout();
                    NavballHUD.Instance.RebuildHUD();
                    _toastMsg = $"成功导入并套用布局！(共加载 {importedLayout.Widgets.Count} 个组件)";
                    _toastTimer = 3.0f;
                }
                else
                {
                    _toastMsg = $"<color=#FF4444>导入失败: {error}</color>";
                    _toastTimer = 3.5f;
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(12f);

            // 3. 预设模板库
            GUILayout.Label("<b>▼ 精选预设模板库 (Preset Library)</b>");
            List<PresetInfo> presets = LayoutShareHub.GetAvailablePresets();

            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo p = presets[i];
                GUILayout.BeginVertical("box");
                GUILayout.BeginHorizontal();

                string nameColor = p.IsBuiltIn ? "#00E5FF" : "#00FF88";
                string tag = p.IsBuiltIn ? "[出厂预置]" : "[本地文件]";
                GUILayout.Label($"<color={nameColor}><b>{p.Name}</b></color> <color=#888888>{tag}</color>", GUILayout.ExpandWidth(true));

                if (GUILayout.Button("⚡ 一键套用此预设", GUILayout.Width(140f), GUILayout.Height(22f)))
                {
                    WidgetLayoutData presetData = LayoutShareHub.LoadPreset(p);
                    if (presetData != null && presetData.Widgets != null)
                    {
                        WidgetLayoutManager.Instance.CurrentLayout.Widgets = presetData.Widgets;
                        WidgetLayoutManager.Instance.CurrentLayout.GlobalScale = presetData.GlobalScale;
                        WidgetLayoutManager.Instance.SaveLayout();
                        NavballHUD.Instance.RebuildHUD();
                        _toastMsg = $"已成功套用预设「{p.Name}」！";
                        _toastTimer = 3.0f;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"<color=#CCCCCC><size=10>{p.Description}</size></color>");
                GUILayout.EndVertical();
            }

            GUILayout.Space(12f);

            // 4. 另存为本地新预设
            GUILayout.Label("<b>▼ 另存为新预设文件 (Save Preset File)</b>");
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>预设名称:</b>", GUILayout.Width(75f));
            _savePresetName = GUILayout.TextField(_savePresetName, GUILayout.Height(22f), GUILayout.Width(220f));

            if (GUILayout.Button("💾 保存到本地 Presets 库", GUILayout.Width(180f), GUILayout.Height(22f)))
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
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
