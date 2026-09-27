using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI
{
    /// <summary>
    /// 航电悬浮工具提示触发器 (Avionics Tooltip Trigger)
    /// 挂载于任何 UI 元素上，鼠标悬停时自动请求全局提示浮层
    /// </summary>
    public class AvionicsTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Title;
        public string Description;
        public string ShortcutKey;
        public float DelaySeconds = 0.32f;

        private bool _isHovered = false;
        private float _hoverTimer = 0f;

        public void SetContent(string title, string description = null, string shortcut = null)
        {
            Title = title;
            Description = description;
            ShortcutKey = shortcut;
        }

        private void Awake()
        {
            enabled = false; // 默认严格休眠，消灭几十个 Trigger 的每帧 Update 轮询
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            _hoverTimer = 0f;
            enabled = true; // 仅悬停时唤醒

            // 若全局提示浮层当前已处于激活显示态，立即切换内容，消除二次悬停等待迟滞
            if (AvionicsTooltipOverlay.Instance != null && AvionicsTooltipOverlay.Instance.IsVisible)
            {
                ShowTooltip();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            _hoverTimer = 0f;
            enabled = false; // 离开即休眠
            if (AvionicsTooltipOverlay.Instance != null)
            {
                AvionicsTooltipOverlay.Instance.Hide(this);
            }
        }

        private void Update()
        {
            if (_isHovered)
            {
                _hoverTimer += Time.unscaledDeltaTime;
                if (_hoverTimer >= DelaySeconds)
                {
                    ShowTooltip();
                    enabled = false; // 弹出后无需继续轮询
                }
            }
            else
            {
                enabled = false;
            }
        }

        private void OnDisable()
        {
            _isHovered = false;
            _hoverTimer = 0f;
            enabled = false;
            if (AvionicsTooltipOverlay.Instance != null)
            {
                AvionicsTooltipOverlay.Instance.Hide(this);
            }
        }

        private void ShowTooltip()
        {
            if (string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Description)) return;
            AvionicsTooltipOverlay.EnsureInstance();
            if (AvionicsTooltipOverlay.Instance != null)
            {
                AvionicsTooltipOverlay.Instance.Show(this, Title, Description, ShortcutKey);
            }
        }
    }

    /// <summary>
    /// 航电全局工具提示覆盖浮层 (Avionics Tooltip Overlay)
    /// 单例常驻，暗晶毛玻璃质感，支持防穿出屏幕边界自适应吸附
    /// </summary>
    public class AvionicsTooltipOverlay : MonoBehaviour
    {
        public static AvionicsTooltipOverlay Instance { get; private set; }

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private Image _bgImage;
        private Outline _outline;

        private Text _titleText;
        private Text _shortcutText;
        private GameObject _shortcutBadge;
        private Text _descText;

        private AvionicsTooltipTrigger _currentTrigger;
        private bool _isVisible = false;
        public bool IsVisible => _isVisible;

        public static void EnsureInstance()
        {
            if (Instance != null) return;

            // 寻找最顶层 Canvas
            Canvas topCanvas = null;
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            foreach (var c in canvases)
            {
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    topCanvas = c;
                    break;
                }
            }

            if (topCanvas == null && canvases.Length > 0)
            {
                topCanvas = canvases[0];
            }

            GameObject go = new GameObject("AvionicsTooltipOverlay", typeof(RectTransform), typeof(CanvasGroup));
            if (topCanvas != null)
            {
                go.transform.SetParent(topCanvas.transform, false);
            }
            Instance = go.AddComponent<AvionicsTooltipOverlay>();
            Instance.BuildUI();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void BuildUI()
        {
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.zero;
            _rectTransform.pivot = new Vector2(0f, 1f); // 默认左上角锚定

            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;

            // 1. 暗晶底板与科技外发光边线
            _bgImage = gameObject.AddComponent<Image>();
            _bgImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);

            _outline = gameObject.AddComponent<Outline>();
            Color borderCol = theme != null ? theme.AccentSecondary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset);
            _outline.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Ghost);
            _outline.effectDistance = new Vector2(1f, 1f);

            // 2. 垂直自适应布局
            var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var csf = gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 3. 顶行：标题与快捷键徽章
            GameObject headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headerRow.transform.SetParent(transform, false);
            var hlg = headerRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // 标题
            _titleText = UIFactory.CreateText(headerRow.transform, "Title", "", 12, TextAnchor.MiddleLeft,
                theme != null ? theme.AccentPrimary : WidgetStyleManager.NeutralOpaque);
            _titleText.fontStyle = FontStyle.Bold;

            // 快捷键微型徽章
            _shortcutBadge = UIFactory.CreatePanel(headerRow.transform, "ShortcutBadge", new Vector2(28f, 16f),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            var badgeHlg = _shortcutBadge.AddComponent<HorizontalLayoutGroup>();
            badgeHlg.padding = new RectOffset(4, 4, 1, 1);
            badgeHlg.childControlWidth = true;
            badgeHlg.childControlHeight = true;
            badgeHlg.childForceExpandWidth = false;
            badgeHlg.childForceExpandHeight = false;
            var badgeCsf = _shortcutBadge.AddComponent<ContentSizeFitter>();
            badgeCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            badgeCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _shortcutText = UIFactory.CreateText(_shortcutBadge.transform, "Key", "", 9, TextAnchor.MiddleCenter,
                theme != null ? theme.AccentSecondary : WidgetStyleManager.NeutralOpaque);
            _shortcutText.fontStyle = FontStyle.Bold;

            // 4. 正文描述
            _descText = UIFactory.CreateText(transform, "Desc", "", 10, TextAnchor.UpperLeft,
                theme != null ? theme.TextPrimaryColor : WidgetStyleManager.NeutralOpaque);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var descLe = _descText.gameObject.AddComponent<LayoutElement>();
            descLe.preferredWidth = 240f;

            gameObject.SetActive(false);
        }

        public void Show(AvionicsTooltipTrigger trigger, string title, string description, string shortcut)
        {
            _currentTrigger = trigger;
            _isVisible = true;

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
            ApplyTheme(theme);

            if (_titleText != null)
            {
                _titleText.text = title ?? "";
                _titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            }

            if (_shortcutBadge != null && _shortcutText != null)
            {
                bool hasShortcut = !string.IsNullOrEmpty(shortcut);
                _shortcutBadge.SetActive(hasShortcut);
                if (hasShortcut) _shortcutText.text = shortcut;
            }

            if (_descText != null)
            {
                _descText.text = description ?? "";
                _descText.gameObject.SetActive(!string.IsNullOrEmpty(description));
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // 置顶

            // 刷新布局并定位
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
            PositionTooltip();

            _canvasGroup.alpha = 1f;
        }

        public void Hide(AvionicsTooltipTrigger trigger)
        {
            if (_currentTrigger == trigger)
            {
                _currentTrigger = null;
                _isVisible = false;
                if (_canvasGroup != null) _canvasGroup.alpha = 0f;
                gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_isVisible && _currentTrigger != null)
            {
                PositionTooltip();
            }
        }

        private void PositionTooltip()
        {
            Vector2 mousePos = Input.mousePosition;
            float padding = 14f;
            Vector2 targetPos = new Vector2(mousePos.x + padding, mousePos.y - padding);

            // 屏幕边界防穿出保护 (Clamp to Screen)
            float screenW = Screen.width;
            float screenH = Screen.height;
            Vector2 size = _rectTransform.sizeDelta;

            if (targetPos.x + size.x > screenW - 10f)
            {
                // 超出右边界，翻折至鼠标左侧
                targetPos.x = mousePos.x - size.x - padding;
            }

            if (targetPos.y - size.y < 10f)
            {
                // 超出下边界，翻折至鼠标上方
                targetPos.y = mousePos.y + size.y + padding;
            }

            targetPos.x = Mathf.Clamp(targetPos.x, 10f, screenW - size.x - 10f);
            targetPos.y = Mathf.Clamp(targetPos.y, size.y + 10f, screenH - 10f);

            _rectTransform.position = targetPos;
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            if (theme == null) return;

            if (_bgImage != null)
            {
                _bgImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            }

            if (_outline != null)
            {
                _outline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }

            if (_titleText != null)
            {
                _titleText.color = theme.AccentPrimary;
            }

            if (_shortcutText != null)
            {
                _shortcutText.color = theme.AccentSecondary;
            }

            if (_descText != null)
            {
                _descText.color = theme.TextPrimaryColor;
            }
        }
    }

    /// <summary>
    /// 工具提示扩展方法便捷 API
    /// </summary>
    public static class AvionicsTooltipExtensions
    {
        public static void SetTooltip(this Component comp, string title, string description = null, string shortcut = null)
        {
            if (comp == null) return;
            SetTooltip(comp.gameObject, title, description, shortcut);
        }

        public static void SetTooltip(this GameObject go, string title, string description = null, string shortcut = null)
        {
            if (go == null) return;
            var trigger = go.GetComponent<AvionicsTooltipTrigger>() ?? go.AddComponent<AvionicsTooltipTrigger>();
            trigger.SetContent(title, description, shortcut);
        }
    }
}
