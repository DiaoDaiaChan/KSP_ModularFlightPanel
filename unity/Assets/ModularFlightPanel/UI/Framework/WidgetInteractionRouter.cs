using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.UI.Framework
{
    // =========================================================================
    // 1. 航电音频与触觉反馈 (Avionics Feedback Service)
    // =========================================================================
    public static class AvionicsFeedbackService
    {
        public static bool IsMuted { get; set; } = false;

        public static void PlayClick()
        {
            if (IsMuted) return;
            // 静默安全降级：如宿主游戏环境注入了 UI 点击音效则触发，否则安全静默
        }
    }

    // =========================================================================
    // 2. 通用右键上下文菜单 (Widget Context Menu)
    // =========================================================================
    public class WidgetContextMenu : MonoBehaviour
    {
        public static WidgetContextMenu Instance { get; private set; }

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Image _bgImage;
        private Outline _outline;
        private Text _headerTitle;
        private Transform _itemsContainer;

        private BaseFlightWidget _targetWidget;
        private readonly List<GameObject> _menuItemButtons = new List<GameObject>();

        public static void EnsureInstance(Canvas parentCanvas)
        {
            if (Instance != null || parentCanvas == null) return;

            var go = new GameObject("WidgetContextMenu", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            go.transform.SetParent(parentCanvas.transform, false);
            Instance = go.AddComponent<WidgetContextMenu>();
            Instance.Initialize();
        }

        private void Initialize()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _bgImage = GetComponent<Image>();
            _outline = GetComponent<Outline>();

            _rectTransform.sizeDelta = new Vector2(190f, 340f);
            _rectTransform.pivot = new Vector2(0f, 1f);

            var theme = WidgetStyleManager.Instance?.CurrentTheme ?? ThemeConfig.CreateBoeing787();
            _bgImage.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.96f);
            _outline.effectColor = WidgetStyleManager.WithAlpha(theme.FrameBorderColor, 0.8f);
            _outline.effectDistance = new Vector2(1.5f, 1.5f);

            // Title
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleGo.transform.SetParent(transform, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, 28f);
            titleRt.anchoredPosition = new Vector2(0f, -4f);

            _headerTitle = titleGo.GetComponent<Text>();
            _headerTitle.font = UIFactory.GetActiveFont(theme);
            _headerTitle.fontSize = 12;
            _headerTitle.alignment = TextAnchor.MiddleCenter;
            _headerTitle.color = theme.AccentPrimary;

            // Items Container
            var containerGo = new GameObject("ItemsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            containerGo.transform.SetParent(transform, false);
            var contRt = containerGo.GetComponent<RectTransform>();
            contRt.anchorMin = Vector2.zero;
            contRt.anchorMax = new Vector2(1f, 1f);
            contRt.offsetMin = new Vector2(6f, 6f);
            contRt.offsetMax = new Vector2(-6f, -34f);

            var vlg = containerGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 3f;

            _itemsContainer = containerGo.transform;

            Close();
        }

        public void Show(BaseFlightWidget widget, Vector2 screenPos)
        {
            if (widget == null) return;
            _targetWidget = widget;
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }

            if (_headerTitle != null)
            {
                _headerTitle.text = widget.DisplayName;
            }

            RebuildMenuItems();

            // 防出界自适应定位
            Vector2 clampedPos = screenPos;
            if (clampedPos.x + _rectTransform.sizeDelta.x > Screen.width)
                clampedPos.x = Screen.width - _rectTransform.sizeDelta.x - 10f;
            if (clampedPos.y - _rectTransform.sizeDelta.y < 0)
                clampedPos.y = _rectTransform.sizeDelta.y + 10f;

            _rectTransform.position = clampedPos;
        }

        public void Close()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
            _targetWidget = null;
        }

        private void RebuildMenuItems()
        {
            for (int i = 0; i < _menuItemButtons.Count; i++)
            {
                if (_menuItemButtons[i] != null)
                {
                    Destroy(_menuItemButtons[i]);
                }
            }
            _menuItemButtons.Clear();

            if (_targetWidget == null) return;
            bool isLocked = _targetWidget.Config?.IsLocked == true;

            // 1. 锁定/解锁
            string lockLabel = isLocked
                ? I18n.Tr("CTX_UNLOCK_POS", "🔓 解锁位置")
                : I18n.Tr("CTX_LOCK_POS", "🔒 锁定位置");
            CreateMenuItem(lockLabel, () =>
            {
                if (_targetWidget != null && _targetWidget.Config != null)
                {
                    _targetWidget.Config.IsLocked = !isLocked;
                    _targetWidget.DragHandler?.UpdateSelectionAppearance();
                }
                Close();
            });

            // 2. 复位坐标与形变
            CreateMenuItem(I18n.Tr("CTX_RESET_TRANSFORM", "↺ 复位形变"), () =>
            {
                if (_targetWidget != null)
                {
                    _targetWidget.UpdateTransform(x: 0f, y: 0f, scale: 1.0f, rotation: 0f, scaleX: 1.0f, scaleY: 1.0f);
                }
                Close();
            });

            // 3. 不透明度调节
            int curOpPct = Mathf.RoundToInt(_targetWidget.Opacity * 100f);
            CreateMenuItem(I18n.TrFormat("CTX_OPACITY_CYCLE", curOpPct), () =>
            {
                if (_targetWidget != null)
                {
                    float[] opSteps = new[] { 1.0f, 0.85f, 0.70f, 0.50f, 0.35f };
                    int curIdx = 0;
                    float cur = _targetWidget.Opacity;
                    for (int i = 0; i < opSteps.Length; i++)
                    {
                        if (Math.Abs(cur - opSteps[i]) < 0.06f) { curIdx = i; break; }
                    }
                    float nextOp = opSteps[(curIdx + 1) % opSteps.Length];
                    _targetWidget.SetOpacity(nextOp);
                    MFPToastBridge.Show(I18n.TrFormat("TOAST_OPACITY_CHANGED", Mathf.RoundToInt(nextOp * 100f)));
                }
                Close();
            });

            // 4. 配色主题切换
            string curThemeOvr = _targetWidget.Config?.ThemeOverride;
            string themeDisplay = string.IsNullOrEmpty(curThemeOvr) ? I18n.Tr("CTX_THEME_DEFAULT", "全局跟随") : (_targetWidget.WidgetTheme?.DisplayName ?? curThemeOvr);
            CreateMenuItem(I18n.TrFormat("CTX_THEME_CYCLE", themeDisplay), () =>
            {
                if (_targetWidget != null)
                {
                    string[] cycleThemes = new[] { "", "boeing_787", "spacex_dragon", "cyber_neon", "diffractive_hud", "vintage_amber", "starship_mars", "sr71_blackbird" };
                    int idx = Array.IndexOf(cycleThemes, curThemeOvr ?? "");
                    string nextTheme = cycleThemes[(idx + 1) % cycleThemes.Length];
                    _targetWidget.SetThemeOverride(nextTheme);
                    string newName = string.IsNullOrEmpty(nextTheme) ? I18n.Tr("CTX_THEME_DEFAULT", "全局跟随") : (_targetWidget.WidgetTheme?.DisplayName ?? nextTheme);
                    MFPToastBridge.Show(I18n.TrFormat("TOAST_THEME_CHANGED", newName));
                }
                Close();
            });

            // 5. 长宽比快速切换
            CreateMenuItem(I18n.Tr("CTX_ASPECT_RATIO_CYCLE", "📐 切换长宽比"), () =>
            {
                if (_targetWidget != null && _targetWidget.Config != null)
                {
                    float ar = _targetWidget.Config.EffectiveScaleX / _targetWidget.Config.EffectiveScaleY;
                    float[] ratios = new[] { 1.0f, 4f / 3f, 16f / 9f, 2.0f, 3.0f };
                    string[] names = new[] { "1:1", "4:3", "16:9", "2:1", "3:1" };
                    int curIdx = 0;
                    for (int i = 0; i < ratios.Length; i++)
                    {
                        if (Math.Abs(ar - ratios[i]) < 0.15f) { curIdx = i; break; }
                    }
                    int nextIdx = (curIdx + 1) % ratios.Length;
                    _targetWidget.SetAspectRatio(ratios[nextIdx]);
                    MFPToastBridge.Show(I18n.TrFormat("TOAST_ASPECT_CHANGED", names[nextIdx]));
                }
                Close();
            });

            // 6. 紧凑模式切换
            CreateMenuItem(I18n.Tr("CTX_COMPACT_MODE", "⛶ 紧凑模式"), () =>
            {
                if (_targetWidget != null && _targetWidget.Config != null)
                {
                    float currentScale = _targetWidget.Config.Scale;
                    float targetScale = Math.Abs(currentScale - 0.85f) < 0.05f ? 1.0f : 0.85f;
                    _targetWidget.UpdateTransform(scale: targetScale);
                }
                Close();
            });

            // 7. 切换单位制式
            CreateMenuItem(I18n.Tr("CTX_UNIT_CYCLE", "📏 切换单位"), () =>
            {
                int nextMode = ((int)AvionicsUnitSystem.GlobalMode + 1) % 4;
                AvionicsUnitSystem.GlobalMode = (UnitSystemMode)nextMode;
                MFPToastBridge.Show(I18n.TrFormat("CTX_UNIT_CHANGED_FMT", AvionicsUnitSystem.GlobalMode));
                Close();
            });

            // 组件专属自定义扩展菜单项 (如分级序列仪模式切换)
            _targetWidget.PopulateContextMenu((label, act) =>
            {
                CreateMenuItem(label, () =>
                {
                    act?.Invoke();
                    Close();
                });
            });

            // 5. 遥测检视
            CreateMenuItem(I18n.Tr("CTX_INSPECT_TELEM", "🔍 遥测检视"), () =>
            {
                if (_targetWidget != null)
                {
                    string info = I18n.TrFormat(
                        "CTX_TELEM_INSPECT_FMT",
                        _targetWidget.DisplayName,
                        _targetWidget.WidgetId,
                        _targetWidget.Config?.PositionX ?? 0f,
                        _targetWidget.Config?.PositionY ?? 0f,
                        _targetWidget.Config?.Scale ?? 1f);
                    MFPToastBridge.Show(info);
                }
                Close();
            });

            // 6. 隐藏小组件
            CreateMenuItem(I18n.Tr("CTX_HIDE_WIDGET", "✕ 隐藏组件"), () =>
            {
                if (_targetWidget != null)
                {
                    _targetWidget.SetVisible(false);
                }
                Close();
            });
        }

        private void CreateMenuItem(string label, Action onClick)
        {
            var btnGo = new GameObject("Item_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(_itemsContainer, false);

            var rt = btnGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 26f);

            var theme = WidgetStyleManager.Instance?.CurrentTheme ?? ThemeConfig.CreateBoeing787();
            var img = btnGo.GetComponent<Image>();
            img.color = WidgetStyleManager.WithAlpha(theme.FrameBgColor, 0.85f);

            var btn = btnGo.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = img.color;
            colors.highlightedColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.4f);
            colors.pressedColor = WidgetStyleManager.WithAlpha(theme.AccentPrimary, 0.7f);
            btn.colors = colors;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(btnGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(8f, 0f);
            txtRt.offsetMax = new Vector2(-8f, 0f);

            var txt = txtGo.GetComponent<Text>();
            txt.font = UIFactory.GetActiveFont(theme);
            txt.fontSize = 11;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = theme.TextPrimaryColor;
            txt.text = label;

            btn.onClick.AddListener(() =>
            {
                AvionicsFeedbackService.PlayClick();
                onClick?.Invoke();
            });

            _menuItemButtons.Add(btnGo);
        }
    }

    // =========================================================================
    // 3. 统一交互行为分发器 (Widget Action Router)
    // =========================================================================
    public static class WidgetActionRouter
    {
        public static void HandlePointerDown(BaseFlightWidget widget, PointerEventData eventData)
        {
            if (widget == null || eventData == null) return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OpenContextMenu(widget, eventData.position);
                eventData.Use();
            }
        }

        public static void OpenContextMenu(BaseFlightWidget widget, Vector2 screenPos)
        {
            if (widget == null) return;
            Canvas rootCanvas = widget.GetComponentInParent<Canvas>();
            if (rootCanvas == null && widget.SubCanvas != null)
            {
                rootCanvas = widget.SubCanvas.rootCanvas;
            }
            if (rootCanvas == null) return;

            WidgetContextMenu.EnsureInstance(rootCanvas);
            WidgetContextMenu.Instance?.Show(widget, screenPos);
        }

        public static void CloseContextMenu()
        {
            WidgetContextMenu.Instance?.Close();
        }
    }
}
