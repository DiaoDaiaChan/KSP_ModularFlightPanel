using System;
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
    /// 现代航电视觉主题与着色器调节视图 (WorkbenchTabThemes)
    /// </summary>
    public class WorkbenchTabThemes : IWorkbenchTabView
    {
        private RectTransform _container;
        private RectTransform _contentRt;

        private struct ThemePresetItem
        {
            public string Id;
            public string Name;
            public string Desc;
            public Color Primary;
            public Color Border;
        }

        private readonly ThemePresetItem[] _presets = new ThemePresetItem[]
        {
            new ThemePresetItem { Id = "modern_aero", Name = "✈ 现代航空暗晶 (Modern Aero)", Desc = "冷蓝航空微光, 标杆现代航电质感", Primary = new Color(0.0f, 0.88f, 1.0f), Border = new Color(0.2f, 0.4f, 0.6f) },
            new ThemePresetItem { Id = "cyber_dark", Name = "🌌 深空赛博黑晶 (Cyber Dark)", Desc = "深黑座舱沉浸底色, 鲜明高反差霓虹", Primary = new Color(0.0f, 0.95f, 0.6f), Border = new Color(0.15f, 0.25f, 0.35f) },
            new ThemePresetItem { Id = "spacex_dragon", Name = "🐉 载人龙飞船极简 (SpaceX Dragon)", Desc = "洁白微光, 扁平化多功能触控大屏", Primary = new Color(0.9f, 0.95f, 1.0f), Border = new Color(0.3f, 0.4f, 0.5f) },
            new ThemePresetItem { Id = "starship_hud", Name = "🚀 星舰全息平显 (Starship HUD)", Desc = "纯净半透全息 HUD, 无边框无底色", Primary = new Color(1.0f, 0.55f, 0.1f), Border = new Color(0.4f, 0.3f, 0.2f) },
            new ThemePresetItem { Id = "apollo_retro", Name = "🌕 阿波罗复古机械 (Apollo Retro)", Desc = "经典灰绿机载仪表, 机械荧光绿刻度", Primary = new Color(0.2f, 0.9f, 0.4f), Border = new Color(0.25f, 0.35f, 0.25f) }
        };

        public void Build(RectTransform container)
        {
            _container = container;

            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_container.GetChild(i).gameObject);
            }

            GameObject card = WorkbenchControls.CreateCard(_container, "ThemesCard", Vector2.zero);
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
            titleTxt.text = "🎨 航电视觉风格与 GPU 着色器调色盘";

            _contentRt = WorkbenchControls.CreateScrollView(card.transform, "ThemesScrollView", new Vector2(0f, 480f), out GameObject scrollObj);
            var le = scrollObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            Refresh();
        }

        public void Refresh()
        {
            if (_contentRt == null) return;

            for (int i = _contentRt.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_contentRt.GetChild(i).gameObject);
            }

            var tm = ThemeManager.Instance;
            var curTheme = tm?.CurrentTheme;

            // 1. 出厂主题画廊
            GameObject subHeader1 = new GameObject("SubHeader1", typeof(RectTransform), typeof(Text));
            subHeader1.transform.SetParent(_contentRt, false);
            Text sh1Txt = subHeader1.GetComponent<Text>();
            sh1Txt.font = WorkbenchControls.MainFont;
            sh1Txt.fontSize = 13;
            sh1Txt.fontStyle = FontStyle.Bold;
            sh1Txt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            sh1Txt.text = "💎 出厂精品主题预设 (1-Click Apply)";

            foreach (var item in _presets)
            {
                BuildThemePresetCard(item, curTheme?.ThemeId == item.Id);
            }

            // 2. 着色器与玻璃质感参数精调
            GameObject subHeader2 = new GameObject("SubHeader2", typeof(RectTransform), typeof(Text));
            subHeader2.transform.SetParent(_contentRt, false);
            Text sh2Txt = subHeader2.GetComponent<Text>();
            sh2Txt.font = WorkbenchControls.MainFont;
            sh2Txt.fontSize = 13;
            sh2Txt.fontStyle = FontStyle.Bold;
            sh2Txt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            sh2Txt.text = "\n🧪 GPU 程序化着色器质感调节";

            if (curTheme != null)
            {
                WorkbenchControls.CreateSlider(_contentRt, "GlassOpacity", "暗晶磨砂透明度", 0.1f, 1.0f, ((Color)curTheme.FrameBgColor).a, (v) =>
                {
                    Color bg = curTheme.FrameBgColor;
                    bg.a = v;
                    curTheme.FrameBgColor = ColorHex.FromColor(bg);
                    ApplyAndRefreshTheme();
                }, "F2");

                WorkbenchControls.CreateSlider(_contentRt, "GlowStrength", "边缘霓虹发光辉光", 0.0f, 1.5f, curTheme.UiGlowStrength, (v) =>
                {
                    curTheme.UiGlowStrength = v;
                    ApplyAndRefreshTheme();
                }, "F2");

                WorkbenchControls.CreateSlider(_contentRt, "ScanlineDepth", "座舱网格扫描线深度", 0.0f, 0.3f, curTheme.UiScanlineStrength, (v) =>
                {
                    curTheme.UiScanlineStrength = v;
                    ApplyAndRefreshTheme();
                }, "F2");

                WorkbenchControls.CreateToggle(_contentRt, "Gpu2D", "启用 GPU 2D 硬件材质加速", tm?.EnableGpu2DUIAcceleration ?? true, (v) =>
                {
                    if (tm != null) tm.EnableGpu2DUIAcceleration = v;
                    ApplyAndRefreshTheme();
                });
            }
        }

        private void BuildThemePresetCard(ThemePresetItem item, bool isActive)
        {
            GameObject card = WorkbenchControls.CreateCard(_contentRt, "Preset_" + item.Id, new Vector2(0f, 54f), true);
            var hlg = card.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(12, 12, 6, 6);

            // 色块预览圆点
            GameObject colorDot = new GameObject("ColorDot", typeof(RectTransform), typeof(Image));
            colorDot.transform.SetParent(card.transform, false);
            colorDot.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            colorDot.GetComponent<Image>().color = item.Primary;

            // 描述列
            GameObject infoCol = new GameObject("InfoCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            infoCol.transform.SetParent(card.transform, false);
            infoCol.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 40f);
            var vlg = infoCol.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            GameObject nameObj = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameObj.transform.SetParent(infoCol.transform, false);
            Text nameTxt = nameObj.GetComponent<Text>();
            nameTxt.font = WorkbenchControls.MainFont;
            nameTxt.fontSize = 12;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.color = item.Primary;
            nameTxt.text = item.Name;

            GameObject descObj = new GameObject("Desc", typeof(RectTransform), typeof(Text));
            descObj.transform.SetParent(infoCol.transform, false);
            Text descTxt = descObj.GetComponent<Text>();
            descTxt.font = WorkbenchControls.MainFont;
            descTxt.fontSize = 10;
            descTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            descTxt.text = item.Desc;

            // 应用按钮
            WorkbenchControls.CreateButton(card.transform, "ApplyBtn", isActive ? "✔ 当前使用中" : "应用主题", new Vector2(100f, 28f), () =>
            {
                ThemeManager.Instance?.SetTheme(item.Id);
                ThemeManager.Instance?.SaveSettings();
                WorkbenchStyleEngine.ClearCache();
                FlightHUDManager.Instance?.RebuildHUD();
                Refresh();
            }, isActive, 11);
        }

        private void ApplyAndRefreshTheme()
        {
            ThemeManager.Instance?.SaveSettings();
            WorkbenchStyleEngine.ClearCache();
            FlightHUDManager.Instance?.RebuildHUD();
        }

        public void OnUpdate()
        {
        }
    }
}
