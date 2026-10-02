using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;

namespace ModularFlightPanel.UI.Workbench.Tabs
{
    /// <summary>
    /// 现代航电档案、出厂预设与社区分享中枢 (WorkbenchTabProfiles)
    /// </summary>
    public class WorkbenchTabProfiles : IWorkbenchTabView
    {
        private RectTransform _container;
        private RectTransform _contentRt;
        private string _shareCodeInput = "";
        private string _feedbackMsg = "";

        public void Build(RectTransform container)
        {
            _container = container;

            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                var c = _container.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }

            GameObject card = WorkbenchControls.CreatePanel(_container, "ProfilesCard", Vector2.zero);
            RectTransform cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = Vector2.zero;
            cardRt.anchorMax = Vector2.one;
            cardRt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 10f;
            vlg.padding = new RectOffset(16, 16, 16, 16);

            // 标题
            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(card.transform, false);
            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = WorkbenchControls.MainFont;
            titleTxt.fontSize = 15;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            titleTxt.text = "💾 航电档案、出厂预设与社区分享中枢";

            _contentRt = WorkbenchControls.CreateScrollView(card.transform, "ProfilesScrollView", new Vector2(0f, 480f), out GameObject scrollObj);
            var le = scrollObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            Refresh();
        }

        public void Refresh()
        {
            if (_contentRt == null) return;

            for (int i = _contentRt.childCount - 1; i >= 0; i--)
            {
                var c = _contentRt.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }

            var lm = WidgetLayoutManager.Instance;
            int widgetCount = lm?.CurrentLayout?.Widgets?.Count ?? 0;

            // 1. 当前活动档案概览
            GameObject curCard = WorkbenchControls.CreateCard(_contentRt, "CurrentStatusCard", new Vector2(0f, 42f));
            var hlg = curCard.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(12, 12, 6, 6);

            GameObject infoObj = new GameObject("CurInfo", typeof(RectTransform), typeof(Text));
            infoObj.transform.SetParent(curCard.transform, false);
            var infoLe = infoObj.AddComponent<LayoutElement>();
            infoLe.flexibleWidth = 1f;

            Text infoTxt = infoObj.GetComponent<Text>();
            infoTxt.font = WorkbenchControls.MainFont;
            infoTxt.fontSize = 12;
            infoTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            infoTxt.text = $"📋 当前机载布局: <b>{widgetCount}</b> 个组件 | 全局缩放: {lm?.CurrentLayout?.GlobalScale:F2}x";

            WorkbenchControls.CreateButton(curCard.transform, "SaveBtn", "💾 即时保存到磁盘", new Vector2(140f, 28f), () =>
            {
                lm?.SaveLayout();
                _feedbackMsg = "✔ 已成功保存当前航电布局到 layout.json";
                Refresh();
            }, true, 11);

            // 反馈提示
            if (!string.IsNullOrEmpty(_feedbackMsg))
            {
                GameObject fbObj = new GameObject("Feedback", typeof(RectTransform), typeof(Text));
                fbObj.transform.SetParent(_contentRt, false);
                var fbLe = fbObj.AddComponent<LayoutElement>();
                fbLe.preferredHeight = 20f;

                Text fbTxt = fbObj.GetComponent<Text>();
                fbTxt.font = WorkbenchControls.MainFont;
                fbTxt.fontSize = 11;
                fbTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
                fbTxt.text = _feedbackMsg;
            }

            // 2. 社区分享码 (GZip Base64)
            GameObject subHeader1 = new GameObject("SubHeader1", typeof(RectTransform), typeof(Text));
            subHeader1.transform.SetParent(_contentRt, false);
            var sh1Le = subHeader1.AddComponent<LayoutElement>();
            sh1Le.preferredHeight = 28f;
            sh1Le.minHeight = 28f;

            Text sh1Txt = subHeader1.GetComponent<Text>();
            sh1Txt.font = WorkbenchControls.MainFont;
            sh1Txt.fontSize = 13;
            sh1Txt.fontStyle = FontStyle.Bold;
            sh1Txt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            sh1Txt.text = "\n🌐 社区航电分享码 (MFP Share Code)";

            GameObject shareRow = new GameObject("ShareRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            shareRow.transform.SetParent(_contentRt, false);
            shareRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);
            var srLe = shareRow.AddComponent<LayoutElement>();
            srLe.preferredHeight = 32f;
            srLe.minHeight = 32f;

            var sHlg = shareRow.GetComponent<HorizontalLayoutGroup>();
            sHlg.childForceExpandWidth = false;
            sHlg.childForceExpandHeight = true;
            sHlg.spacing = 8f;

            WorkbenchControls.CreateButton(shareRow.transform, "ExportBtn", "📋 生成并复制分享码", new Vector2(160f, 30f), () =>
            {
                string code = LayoutShareHub.ExportShareCode(WidgetLayoutManager.Instance.CurrentLayout);
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    _feedbackMsg = "✔ 分享码已复制到剪贴板！可粘贴给任何玩家分享完整座舱布局。";
                    Refresh();
                }
            }, false, 11);

            WorkbenchControls.CreateTextField(shareRow.transform, "CodeInput", _shareCodeInput, "在此粘贴社区分享码 (MFP:v1:...)", new Vector2(300f, 30f), (v) =>
            {
                _shareCodeInput = v;
            });

            WorkbenchControls.CreateButton(shareRow.transform, "ImportBtn", "📥 解析并导入", new Vector2(100f, 30f), () =>
            {
                if (!string.IsNullOrEmpty(_shareCodeInput))
                {
                    bool ok = LayoutShareHub.TryImportShareCode(_shareCodeInput, out var imported, out var err);
                    if (ok && imported != null)
                    {
                        WidgetSelectionManager.ClearSelection();
                        WidgetLayoutManager.Instance.ApplyLayout(imported);
                        FlightHUDManager.Instance?.RebuildHUD();
                        _feedbackMsg = "✔ 社区分享码导入成功！";
                    }
                    else
                    {
                        _feedbackMsg = $"✖ 导入失败: {err}";
                    }
                    Refresh();
                }
            }, false, 11);

            // 3. 出厂经典预设库
            GameObject subHeader2 = new GameObject("SubHeader2", typeof(RectTransform), typeof(Text));
            subHeader2.transform.SetParent(_contentRt, false);
            var sh2Le = subHeader2.AddComponent<LayoutElement>();
            sh2Le.preferredHeight = 28f;
            sh2Le.minHeight = 28f;

            Text sh2Txt = subHeader2.GetComponent<Text>();
            sh2Txt.font = WorkbenchControls.MainFont;
            sh2Txt.fontSize = 13;
            sh2Txt.fontStyle = FontStyle.Bold;
            sh2Txt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            sh2Txt.text = "\n📦 出厂航电布局预设库 (Factory Presets)";

            var presets = LayoutShareHub.GetAvailablePresets();
            foreach (var preset in presets)
            {
                BuildPresetRow(preset);
            }
        }

        private void BuildPresetRow(PresetInfo preset)
        {
            GameObject row = WorkbenchControls.CreateCard(_contentRt, "Preset_" + preset.Name, new Vector2(0f, 36f), true);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(12, 12, 4, 4);

            GameObject nameObj = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameObj.transform.SetParent(row.transform, false);
            nameObj.GetComponent<RectTransform>().sizeDelta = new Vector2(380f, 26f);
            var nameLe = nameObj.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;

            Text nameTxt = nameObj.GetComponent<Text>();
            nameTxt.font = WorkbenchControls.MainFont;
            nameTxt.fontSize = 12;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            nameTxt.text = $"📁 {preset.Name}";

            WorkbenchControls.CreateButton(row.transform, "LoadBtn", "📥 载入该预设", new Vector2(110f, 26f), () =>
            {
                var data = LayoutShareHub.LoadPreset(preset);
                if (data != null && data.Widgets != null)
                {
                    WidgetSelectionManager.ClearSelection();
                    WidgetLayoutManager.Instance.ApplyLayout(data);
                    FlightHUDManager.Instance?.RebuildHUD();
                    _feedbackMsg = $"✔ 成功载入预设 [{preset.Name}]！";
                }
                else
                {
                    _feedbackMsg = $"✖ 载入预设 [{preset.Name}] 失败";
                }
                Refresh();
            }, false, 11);
        }

        public void OnUpdate()
        {
        }
    }
}
