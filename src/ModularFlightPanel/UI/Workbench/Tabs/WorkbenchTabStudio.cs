using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.HUD;

namespace ModularFlightPanel.UI.Workbench.Tabs
{
    public interface IWorkbenchTabView
    {
        void Build(RectTransform container);
        void Refresh();
        void OnUpdate();
    }

    /// <summary>
    /// 现代航电一体化工坊视图 (WorkbenchTabStudio)
    /// 融合组件库 (Palette)、已挂载层级树 (Hierarchy) 与全功能实时属性检查器 (Inspector)。
    /// </summary>
    public class WorkbenchTabStudio : IWorkbenchTabView
    {
        private RectTransform _container;
        private RectTransform _leftContent;
        private RectTransform _inspectorContent;

        // 左栏模式：0 = 组件库 (Palette), 1 = 已挂载层级 (Hierarchy)
        private int _leftMode = 0;
        private string _searchFilter = "";
        private string _lastSelectedId = null;

        public void Build(RectTransform container)
        {
            _container = container;

            // 清理既有子节点
            for (int i = _container.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_container.GetChild(i).gameObject);
            }

            // 创建水平左右双栏分割容器
            GameObject splitObj = new GameObject("StudioSplit", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            splitObj.transform.SetParent(_container, false);

            RectTransform splitRt = splitObj.GetComponent<RectTransform>();
            splitRt.anchorMin = Vector2.zero;
            splitRt.anchorMax = Vector2.one;
            splitRt.sizeDelta = Vector2.zero;

            HorizontalLayoutGroup hlg = splitObj.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(6, 6, 6, 6);

            // =========================================================================
            // 1. 左栏：组件库 / 层级树 (定宽 340px)
            // =========================================================================
            GameObject leftPanel = WorkbenchControls.CreateCard(splitObj.transform, "LeftPanel", new Vector2(340f, 0f));
            RectTransform leftRt = leftPanel.GetComponent<RectTransform>();
            leftRt.sizeDelta = new Vector2(340f, 0f);

            BuildLeftPanel(leftPanel.transform);

            // =========================================================================
            // 2. 右栏：实时属性检查器 (自适应剩余宽度)
            // =========================================================================
            GameObject rightPanel = WorkbenchControls.CreateCard(splitObj.transform, "RightPanel", new Vector2(0f, 0f));
            RectTransform rightRt = rightPanel.GetComponent<RectTransform>();
            rightRt.sizeDelta = new Vector2(0f, 0f);
            var le = rightPanel.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            BuildInspectorPanel(rightPanel.transform);

            Refresh();
        }

        private void BuildLeftPanel(Transform parent)
        {
            // 垂直排版
            VerticalLayoutGroup vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            // 模式切换分段按钮栏
            GameObject segRow = new GameObject("SegmentRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            segRow.transform.SetParent(parent, false);
            HorizontalLayoutGroup segHlg = segRow.GetComponent<HorizontalLayoutGroup>();
            segHlg.childForceExpandWidth = true;
            segHlg.childForceExpandHeight = true;
            segHlg.spacing = 6f;
            segRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 28f);

            WorkbenchControls.CreateButton(segRow.transform, "BtnPalette", "📦 航电组件库", new Vector2(150f, 28f), () =>
            {
                _leftMode = 0;
                RefreshLeftList();
            }, _leftMode == 0);

            WorkbenchControls.CreateButton(segRow.transform, "BtnHierarchy", "📋 已挂载层级", new Vector2(150f, 28f), () =>
            {
                _leftMode = 1;
                RefreshLeftList();
            }, _leftMode == 1);

            // 快速搜索框
            WorkbenchControls.CreateTextField(parent, "SearchInput", _searchFilter, "🔍 检索名称 / 类型...", new Vector2(0f, 28f), (val) =>
            {
                _searchFilter = val;
                RefreshLeftList();
            });

            // 滚动列表视口 (自适应剩余高度)
            _leftContent = WorkbenchControls.CreateScrollView(parent, "LeftScrollView", new Vector2(0f, 400f), out GameObject scrollObj);
            var le = scrollObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
        }

        private void BuildInspectorPanel(Transform parent)
        {
            VerticalLayoutGroup vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(14, 14, 14, 14);

            // 检查器顶栏
            GameObject header = new GameObject("InspectorHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(parent, false);
            header.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 28f);
            var hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = true;
            hHlg.spacing = 8f;

            GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(header.transform, false);
            Text titleTxt = titleObj.GetComponent<Text>();
            titleTxt.font = WorkbenchControls.MainFont;
            titleTxt.fontSize = 14;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            titleTxt.text = "🛠️ 实时属性检查器 (Inspector)";

            _inspectorContent = WorkbenchControls.CreateScrollView(parent, "InspectorScrollView", new Vector2(0f, 440f), out GameObject scrollObj);
            var le = scrollObj.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
        }

        public void Refresh()
        {
            RefreshLeftList();
            RefreshInspector();
        }

        private void RefreshLeftList()
        {
            if (_leftContent == null) return;

            // 清理既有条目
            for (int i = _leftContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_leftContent.GetChild(i).gameObject);
            }

            if (_leftMode == 0)
            {
                // 渲染组件库 (Palette)
                var allDefs = WidgetRegistry.AllDescriptors;
                var filtered = allDefs.Where(w =>
                    (string.IsNullOrEmpty(_searchFilter) ||
                     (w.DisplayName != null && w.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (w.TypeName != null && w.TypeName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                ).ToList();

                foreach (var def in filtered)
                {
                    BuildPaletteCard(def);
                }
            }
            else
            {
                // 渲染已挂载层级树 (Hierarchy)
                var layout = WidgetLayoutManager.Instance?.CurrentLayout;
                if (layout != null && layout.Widgets != null)
                {
                    var widgets = layout.Widgets.Where(w =>
                        (string.IsNullOrEmpty(_searchFilter) ||
                         w.WidgetId.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    ).ToList();

                    foreach (var w in widgets)
                    {
                        BuildHierarchyItem(w);
                    }
                }
            }
        }

        private void BuildPaletteCard(WidgetDescriptor def)
        {
            GameObject card = WorkbenchControls.CreateCard(_leftContent, "Card_" + def.TypeName, new Vector2(0f, 62f), true);
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(8, 8, 6, 6);

            // 顶行：标题 + 分类徽标
            GameObject row1 = new GameObject("Row1", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row1.transform.SetParent(card.transform, false);
            row1.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 20f);
            var hlg1 = row1.GetComponent<HorizontalLayoutGroup>();
            hlg1.childForceExpandWidth = false;
            hlg1.childForceExpandHeight = true;
            hlg1.spacing = 6f;

            GameObject nameObj = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameObj.transform.SetParent(row1.transform, false);
            Text nameTxt = nameObj.GetComponent<Text>();
            nameTxt.font = WorkbenchControls.MainFont;
            nameTxt.fontSize = 12;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.color = WorkbenchStyleEngine.ColorTextPrimary;
            nameTxt.text = def.DisplayName ?? def.TypeName;

            WorkbenchControls.CreatePill(row1.transform, "CatPill", def.Category.ToString(), WorkbenchStyleEngine.ColorAccentSecondary, 10);

            // 底行：简介 + 一键添加按钮
            GameObject row2 = new GameObject("Row2", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row2.transform.SetParent(card.transform, false);
            row2.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 24f);
            var hlg2 = row2.GetComponent<HorizontalLayoutGroup>();
            hlg2.childForceExpandWidth = false;
            hlg2.childForceExpandHeight = true;
            hlg2.spacing = 6f;

            GameObject descObj = new GameObject("Desc", typeof(RectTransform), typeof(Text));
            descObj.transform.SetParent(row2.transform, false);
            descObj.GetComponent<RectTransform>().sizeDelta = new Vector2(190f, 20f);
            Text descTxt = descObj.GetComponent<Text>();
            descTxt.font = WorkbenchControls.MainFont;
            descTxt.fontSize = 10;
            descTxt.color = WorkbenchStyleEngine.ColorTextMuted;
            descTxt.text = def.Description ?? def.TypeName;

            WorkbenchControls.CreateButton(row2.transform, "AddBtn", "➕ 添加", new Vector2(65f, 22f), () =>
            {
                AddWidgetToHud(def.TypeName);
            }, true, 11);
        }

        private void BuildHierarchyItem(WidgetConfig cfg)
        {
            bool isSelected = WidgetSelectionManager.SelectedWidgets.Any(w => w.Config?.WidgetId == cfg.WidgetId);

            GameObject item = WorkbenchControls.CreateCard(_leftContent, "Item_" + cfg.WidgetId, new Vector2(0f, 34f), true);
            var hlg = item.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(8, 8, 4, 4);

            // 名字与选择按钮
            WorkbenchControls.CreateButton(item.transform, "SelectBtn", cfg.WidgetId, new Vector2(170f, 26f), () =>
            {
                SelectWidgetOnHud(cfg.WidgetId);
                Refresh();
            }, isSelected, 11);

            // 显隐开关
            WorkbenchControls.CreateButton(item.transform, "VisBtn", cfg.IsEnabled ? "👁 显" : "🚫 隐", new Vector2(36f, 26f), () =>
            {
                cfg.IsEnabled = !cfg.IsEnabled;
                WidgetLayoutManager.Instance?.SaveLayout();
                FlightHUDManager.Instance?.RebuildHUD(true);
                RefreshLeftList();
            }, cfg.IsEnabled, 10);

            // 删除按钮
            WorkbenchControls.CreateButton(item.transform, "DelBtn", "✕", new Vector2(26f, 26f), () =>
            {
                WidgetLayoutManager.Instance?.RemoveWidget(cfg.WidgetId);
                FlightHUDManager.Instance?.RebuildHUD(true);
                Refresh();
            }, false, 11);
        }

        private void RefreshInspector()
        {
            if (_inspectorContent == null) return;

            for (int i = _inspectorContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_inspectorContent.GetChild(i).gameObject);
            }

            var sel = WidgetSelectionManager.SelectedWidgets.FirstOrDefault();
            if (sel == null || sel.Config == null)
            {
                // 空状态占位提示
                GameObject placeholder = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
                placeholder.transform.SetParent(_inspectorContent, false);
                Text pTxt = placeholder.GetComponent<Text>();
                pTxt.font = WorkbenchControls.MainFont;
                pTxt.fontSize = 13;
                pTxt.alignment = TextAnchor.MiddleCenter;
                pTxt.color = WorkbenchStyleEngine.ColorTextMuted;
                pTxt.text = "\n\n👈 在左侧层级或画布上选择一个组件以精调属性\n(支持 8 点控制手柄实时同步联动)";
                return;
            }

            var cfg = sel.Config;
            _lastSelectedId = cfg.WidgetId;

            // 选中组件标题卡片
            GameObject headerCard = WorkbenchControls.CreateCard(_inspectorContent, "TargetHeader", new Vector2(0f, 38f));
            var hhLg = headerCard.AddComponent<HorizontalLayoutGroup>();
            hhLg.childForceExpandWidth = false;
            hhLg.childForceExpandHeight = true;
            hhLg.spacing = 8f;
            hhLg.padding = new RectOffset(10, 10, 6, 6);

            GameObject idObj = new GameObject("IdText", typeof(RectTransform), typeof(Text));
            idObj.transform.SetParent(headerCard.transform, false);
            Text idTxt = idObj.GetComponent<Text>();
            idTxt.font = WorkbenchControls.MainFont;
            idTxt.fontSize = 13;
            idTxt.fontStyle = FontStyle.Bold;
            idTxt.color = WorkbenchStyleEngine.ColorAccentPrimary;
            idTxt.text = $"🎯 选中: {cfg.WidgetId}";

            // 1. 变换调整 (Transform)
            WorkbenchControls.CreateSlider(_inspectorContent, "PosX", "位置 X", 0f, Screen.width, cfg.PositionX, (v) =>
            {
                cfg.PositionX = v;
                sel.UpdateTransform(cfg.PositionX, cfg.PositionY, cfg.Scale, cfg.Rotation);
            }, "F0");

            WorkbenchControls.CreateSlider(_inspectorContent, "PosY", "位置 Y", 0f, Screen.height, cfg.PositionY, (v) =>
            {
                cfg.PositionY = v;
                sel.UpdateTransform(cfg.PositionX, cfg.PositionY, cfg.Scale, cfg.Rotation);
            }, "F0");

            WorkbenchControls.CreateSlider(_inspectorContent, "Scale", "缩放比例", 0.4f, 2.5f, cfg.Scale, (v) =>
            {
                cfg.Scale = v;
                sel.UpdateTransform(cfg.PositionX, cfg.PositionY, cfg.Scale, cfg.Rotation);
            }, "F2");

            WorkbenchControls.CreateSlider(_inspectorContent, "Rot", "旋转角度", -180f, 180f, cfg.Rotation, (v) =>
            {
                cfg.Rotation = v;
                sel.UpdateTransform(cfg.PositionX, cfg.PositionY, cfg.Scale, cfg.Rotation);
            }, "F0");

            WorkbenchControls.CreateSlider(_inspectorContent, "Opacity", "不透明度", 0.1f, 1.0f, cfg.Opacity, (v) =>
            {
                cfg.Opacity = v;
                sel.UpdateTransform(cfg.PositionX, cfg.PositionY, cfg.Scale, cfg.Rotation);
            }, "F2");

            WorkbenchControls.CreateToggle(_inspectorContent, "IsolateCanvas", "独立 Canvas 隔离加速", cfg.IsolateCanvas, (v) =>
            {
                cfg.IsolateCanvas = v;
                sel.ApplyCanvasIsolation(v);
            });

            // 2. 遥测插槽 (Telemetry Token)
            string curToken = cfg.NumericToken ?? "{SPD}";
            WorkbenchControls.CreateTextField(_inspectorContent, "TokenInput", curToken, "输入遥测 Token (如 {SPD}, {ALT})...", new Vector2(0f, 28f), (val) =>
            {
                cfg.NumericToken = val;
                WidgetLayoutManager.Instance?.SaveLayout();
            });

            // 快捷 Token 胶囊候选栏
            GameObject tokenPills = new GameObject("TokenPills", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tokenPills.transform.SetParent(_inspectorContent, false);
            tokenPills.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 24f);
            var tpHlg = tokenPills.GetComponent<HorizontalLayoutGroup>();
            tpHlg.childForceExpandWidth = false;
            tpHlg.childForceExpandHeight = true;
            tpHlg.spacing = 6f;

            string[] quickTokens = new string[] { "{SPD}", "{ALT}", "{THR}", "{TWR}", "{APO}", "{PER}", "{COMM}" };
            foreach (var tok in quickTokens)
            {
                WorkbenchControls.CreateButton(tokenPills.transform, "Btn_" + tok, tok, new Vector2(50f, 22f), () =>
                {
                    cfg.NumericToken = tok;
                    WidgetLayoutManager.Instance?.SaveLayout();
                    RefreshInspector();
                }, tok == curToken, 10);
            }
        }

        private void AddWidgetToHud(string widgetTypeId)
        {
            var layout = WidgetLayoutManager.Instance?.CurrentLayout;
            if (layout == null) return;

            string newId = $"{widgetTypeId}_{DateTime.Now.Ticks % 10000}";
            var newCfg = new WidgetConfig(newId, newId, Screen.width * 0.5f, Screen.height * 0.5f)
            {
                WidgetType = widgetTypeId,
                Scale = 1.0f,
                Opacity = 1.0f,
                IsEnabled = true
            };

            layout.Widgets.Add(newCfg);
            WidgetLayoutManager.Instance.SaveLayout();
            FlightHUDManager.Instance?.RebuildHUD(true);
            SelectWidgetOnHud(newId);
            Refresh();
        }

        private void SelectWidgetOnHud(string widgetId)
        {
            if (FlightHUDManager.Instance?.ModularWidgets == null) return;
            var w = FlightHUDManager.Instance.ModularWidgets.FirstOrDefault(m => m != null && m.Config?.WidgetId == widgetId);
            if (w != null)
            {
                WidgetSelectionManager.Select(w, false);
            }
        }

        public void OnUpdate()
        {
            var sel = WidgetSelectionManager.SelectedWidgets.FirstOrDefault();
            string selId = sel?.Config?.WidgetId;
            if (selId != _lastSelectedId)
            {
                _lastSelectedId = selId;
                RefreshInspector();
            }
        }
    }
}
