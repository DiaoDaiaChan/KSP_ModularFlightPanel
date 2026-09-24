using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电多段模式选择胶囊 (Avionics Segmented Control)
    /// 统一治理全系统多态单选切换（参考系 SURFACE/ORBIT/TARGET、时钟 MET/UT、SAS模式选择等）。
    /// 内置互斥状态机、高光胶囊选区与零色面字面量主题联动。
    /// </summary>
    public class AvionicsSegmentedControl : MonoBehaviour
    {
        public Action<int, string> OnSelectionChanged;

        private Image _frameBg;
        private Outline _frameOutline;

        private readonly List<Button> _segmentButtons = new List<Button>();
        private readonly List<Text> _segmentLabels = new List<Text>();
        private readonly List<string> _segmentTexts = new List<string>();

        private int _selectedIndex = 0;
        public int SelectedIndex => _selectedIndex;

        public string SelectedOption => (_selectedIndex >= 0 && _selectedIndex < _segmentTexts.Count)
            ? _segmentTexts[_selectedIndex]
            : null;

        public void Initialize(string[] options, int defaultIndex, Vector2 totalSize, Action<int, string> onSelect, ThemeConfig theme = null)
        {
            OnSelectionChanged = onSelect;
            _selectedIndex = Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, options.Length - 1));

            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = UIFactory.GetScreenDpiScale();

            // 1. 外层内凹胶囊槽
            _frameBg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            _frameBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            _frameOutline = gameObject.GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
            Color borderCol = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);
            _frameOutline.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Ghost);
            _frameOutline.effectDistance = new Vector2(1f * s, 1f * s);

            var hlg = gameObject.GetComponent<HorizontalLayoutGroup>() ?? gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(1, 1, 1, 1);
            hlg.spacing = 1f * s;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            // 2. 构建分段子按键
            _segmentButtons.Clear();
            _segmentLabels.Clear();
            _segmentTexts.Clear();

            int count = options.Length;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string label = options[i];
                _segmentTexts.Add(label);

                GameObject segGo = new GameObject($"Segment_{i}_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
                segGo.transform.SetParent(transform, false);

                Button btn = segGo.GetComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SetSelectedIndex(index, true));

                Image segImg = segGo.GetComponent<Image>();
                Outline segOutline = segGo.AddComponent<Outline>();
                segOutline.effectDistance = new Vector2(1f * s, 1f * s);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(totalSize.y * 0.44f));
                Text segText = UIFactory.CreateText(segGo.transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                    theme.TextPrimaryColor);
                segText.fontStyle = FontStyle.Bold;

                RectTransform textRt = segText.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;

                var fb = segGo.AddComponent<AvionicsButtonFeedback>();
                fb.Initialize(btn, segImg, segOutline, segText, theme);

                _segmentButtons.Add(btn);
                _segmentLabels.Add(segText);
            }

            ApplyTheme(theme);
            RefreshSelectionVisual();
        }

        public void SetSelectedIndex(int index, bool triggerCallback = true)
        {
            if (index < 0 || index >= _segmentButtons.Count) return;
            if (_selectedIndex != index)
            {
                _selectedIndex = index;
                RefreshSelectionVisual();
                if (triggerCallback)
                {
                    OnSelectionChanged?.Invoke(_selectedIndex, _segmentTexts[_selectedIndex]);
                }
            }
        }

        public void SetSelectedOption(string option, bool triggerCallback = true)
        {
            for (int i = 0; i < _segmentTexts.Count; i++)
            {
                if (string.Equals(_segmentTexts[i], option, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedIndex(i, triggerCallback);
                    return;
                }
            }
        }

        private void RefreshSelectionVisual()
        {
            for (int i = 0; i < _segmentButtons.Count; i++)
            {
                bool isSelected = (i == _selectedIndex);
                _segmentButtons[i].SetToggleActive(isSelected);
            }
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            if (_frameBg != null)
            {
                _frameBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }

            if (_frameOutline != null)
            {
                _frameOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            for (int i = 0; i < _segmentButtons.Count; i++)
            {
                var fb = _segmentButtons[i].GetComponent<AvionicsButtonFeedback>();
                if (fb != null) fb.ApplyTheme(theme);
            }

            RefreshSelectionVisual();
        }
    }
}
