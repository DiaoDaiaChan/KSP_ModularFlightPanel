using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;

namespace ModularFlightPanel.UI.Workbench.Tabs
{
    /// <summary>
    /// 现代航电全局偏好与系统设置视图 (WorkbenchTabPreferences)
    /// </summary>
    public class WorkbenchTabPreferences : IWorkbenchTabView
    {
        private RectTransform _container;
        private RectTransform _contentRt;

        public void Build(RectTransform container)
        {
            _container = container;

            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                var c = _container.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }

            GameObject card = WorkbenchControls.CreateCard(_container, "PrefsCard", Vector2.zero);
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
            titleTxt.text = "⚙️ 航电系统全局偏好与运行参数";

            _contentRt = WorkbenchControls.CreateScrollView(card.transform, "PrefsScrollView", new Vector2(0f, 480f), out GameObject scrollObj);
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

            var tm = ThemeManager.Instance;
            var lm = WidgetLayoutManager.Instance;

            // 1. 语言切换
            GameObject langRow = new GameObject("LangRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            langRow.transform.SetParent(_contentRt, false);
            langRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);
            var lrLe = langRow.AddComponent<LayoutElement>();
            lrLe.preferredHeight = 32f;
            lrLe.minHeight = 32f;

            var lHlg = langRow.GetComponent<HorizontalLayoutGroup>();
            lHlg.childForceExpandWidth = false;
            lHlg.childForceExpandHeight = true;
            lHlg.spacing = 10f;

            GameObject lTitle = new GameObject("LTitle", typeof(RectTransform), typeof(Text));
            lTitle.transform.SetParent(langRow.transform, false);
            lTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 28f);
            var ltLe = lTitle.AddComponent<LayoutElement>();
            ltLe.preferredWidth = 160f;
            ltLe.minWidth = 140f;
            Text lt = lTitle.GetComponent<Text>();
            lt.font = WorkbenchControls.MainFont;
            lt.fontSize = 12;
            lt.color = WorkbenchStyleEngine.ColorTextPrimary;
            lt.text = "界面主语言 (Language):";

            string curLang = I18nManager.Instance?.CurrentLanguage ?? "zh-CN";
            bool isZh = curLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);

            WorkbenchControls.CreateButton(langRow.transform, "BtnZh", "🇨🇳 简体中文", new Vector2(110f, 28f), () =>
            {
                I18nManager.Instance?.SetLanguage("zh-CN");
                ThemeManager.Instance?.SaveSettings();
                Refresh();
            }, isZh, 11);

            WorkbenchControls.CreateButton(langRow.transform, "BtnEn", "🇺🇸 English", new Vector2(110f, 28f), () =>
            {
                I18nManager.Instance?.SetLanguage("en-US");
                ThemeManager.Instance?.SaveSettings();
                Refresh();
            }, !isZh, 11);

            // 2. 全局缩放比率
            if (lm?.CurrentLayout != null)
            {
                WorkbenchControls.CreateSlider(_contentRt, "GlobalScale", "HUD 全局缩放基准", 0.5f, 2.0f, lm.CurrentLayout.GlobalScale, (v) =>
                {
                    lm.CurrentLayout.GlobalScale = v;
                    lm.SaveLayout();
                    FlightHUDManager.Instance?.RebuildHUD();
                }, "F2");
            }

            // 3. 姿态球超采样倍率
            var renderMgr = ModularFlightPanel.UI.WidgetRenderManager.Instance;
            if (renderMgr != null)
            {
                WorkbenchControls.CreateSlider(_contentRt, "NavballSuperSample", "3D 姿态球超采样渲染倍率", 0.5f, 2.0f, renderMgr.GlobalRenderScaleMultiplier, (v) =>
                {
                    renderMgr.SetGlobalRenderScale(v);
                    ThemeManager.Instance?.SaveSettings();
                }, "F2");
            }
        }

        public void OnUpdate()
        {
        }
    }
}
