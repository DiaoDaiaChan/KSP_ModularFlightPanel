using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 一体化机载快速控制栏 (Avionics Flight Control Bar)
    /// 严丝合缝嵌合于姿态球正下方 (宽度 184px, 高度 22px, 完美容纳于双带之间)。
    /// 整合：
    /// 1. RCS 动力开关 (状态色高亮)
    /// 2. SAS 主动力开关 (状态色高亮)
    /// 3. REF FRAME 权威参考系一键切换胶囊 (SURFACE / ORBIT / TARGET)
    /// 严格继承 BaseFlightWidget，零硬编码。
    /// </summary>
    public class BottomControlsWidget : BaseFlightWidget
    {
        private Image _panelImage;
        private Outline _panelOutline;

        // RCS / SAS 开关与参考系切换
        private Button _rcsBtn;
        private Image _rcsImg;
        private Outline _rcsOutline;
        private Text _rcsText;

        private Button _sasBtn;
        private Image _sasImg;
        private Outline _sasOutline;
        private Text _sasText;

        private Button _frameBtn;
        private Image _frameImg;
        private Outline _frameOutline;
        private Text _frameText;

        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = panelSize;

            GameObject panel = UIFactory.CreatePanel(transform, "FlightControlBar", panelSize,
                Vector2.zero, Color.clear, Color.clear, 0f);
            _panelImage = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();
            if (_panelOutline != null) _panelOutline.enabled = false;

            // 构建控制按键: RCS (-68), SAS (-26), REF FRAME (+43)
            BuildControlBar(panel.transform, s, theme);

            ApplyTheme(theme);
        }

        private void BuildControlBar(Transform parent, float s, ThemeConfig theme)
        {
            Vector2 toggleBtnSize = new Vector2(38f * s, 18f * s);

            // 1. RCS 开关
            _rcsBtn = UIFactory.CreateButton(parent, "Btn_RCS", toggleBtnSize, new Vector2(-68f * s, 0f), OnRCSToggle);
            _rcsImg = _rcsBtn.GetComponent<Image>();
            _rcsImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _rcsOutline = _rcsBtn.gameObject.AddComponent<Outline>();
            _rcsOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _rcsOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "Text", "RCS", Mathf.Max(8, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _rcsText.fontStyle = FontStyle.Bold;
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = toggleBtnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            // 2. SAS 主开关
            _sasBtn = UIFactory.CreateButton(parent, "Btn_SAS", toggleBtnSize, new Vector2(-26f * s, 0f), OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _sasOutline = _sasBtn.gameObject.AddComponent<Outline>();
            _sasOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _sasOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _sasText = UIFactory.CreateText(_sasBtn.transform, "Text", "SAS", Mathf.Max(8, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _sasText.fontStyle = FontStyle.Bold;
            RectTransform sasRt = _sasText.GetComponent<RectTransform>();
            sasRt.sizeDelta = toggleBtnSize;
            sasRt.anchoredPosition = Vector2.zero;

            // 3. REF FRAME 模式胶囊按钮 (左键切换, 右键展开 Principia 参考系窗口)
            Vector2 frameBtnSize = new Vector2(90f * s, 18f * s);
            _frameBtn = UIFactory.CreateButton(parent, "Btn_RefFrame", frameBtnSize, new Vector2(43f * s, 0f), null);
            _frameImg = _frameBtn.GetComponent<Image>();
            _frameImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _frameOutline = _frameBtn.gameObject.AddComponent<Outline>();
            _frameOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            _frameOutline.effectDistance = new Vector2(1f * s, 1f * s);

            var clickHandler = _frameBtn.gameObject.AddComponent<RefFrameButtonHandler>();
            clickHandler.OnLeftClick = OnCycleSpeedMode;
            clickHandler.OnRightClick = OnTogglePrincipiaWindow;

            _frameText = UIFactory.CreateText(_frameBtn.transform, "Text", "REF: SURFACE ▾", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _frameText.fontStyle = FontStyle.Bold;
            RectTransform frt = _frameText.GetComponent<RectTransform>();
            frt.sizeDelta = frameBtnSize;
            frt.anchoredPosition = Vector2.zero;
        }

        public static Action OnTogglePrincipiaWindowAction;

        private void OnRCSToggle()
        {
            FlightTelemetryContext.Current?.ToggleRCS();
        }

        private void OnSASToggle()
        {
            FlightTelemetryContext.Current?.ToggleSAS();
        }

        private void OnCycleSpeedMode()
        {
            FlightTelemetryContext.Current?.CycleSpeedMode();
        }

        private void OnTogglePrincipiaWindow()
        {
            if (OnTogglePrincipiaWindowAction != null)
            {
                OnTogglePrincipiaWindowAction.Invoke();
            }
            else
            {
                OnCycleSpeedMode();
            }
        }

        private bool _lastRcs = false;
        private bool _lastSas = false;
        private string _lastFrameText;
        private bool _hasInitializedState = false;

        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. RCS 状态 (高反差发光药丸微交互)
            bool rcs = telem.IsRCSEnabled;
            if (!_hasInitializedState || rcs != _lastRcs)
            {
                _lastRcs = rcs;
                if (_rcsBtn != null)
                {
                    _rcsImg.color = rcs ? theme.AccentPrimary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    _rcsText.color = rcs 
                        ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.InverseOnAccent, theme) 
                        : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    if (_rcsOutline != null)
                    {
                        _rcsOutline.effectColor = rcs ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    }
                }
            }

            // 2. SAS 状态 (高反差发光药丸微交互)
            bool sas = telem.IsSASEnabled;
            if (!_hasInitializedState || sas != _lastSas)
            {
                _lastSas = sas;
                if (_sasBtn != null)
                {
                    _sasImg.color = sas ? theme.AccentPrimary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    _sasText.color = sas 
                        ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.InverseOnAccent, theme) 
                        : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                    if (_sasOutline != null)
                    {
                        _sasOutline.effectColor = sas ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    }
                }
            }

            // 3. 参考系模式 (CustomTemplate 驱动 + 联动 Principia 权威参考系与原生 KSP 参考系)
            if (_frameText != null)
            {
                string frameName = telem.SpeedModeName ?? "SURFACE";
                if (string.IsNullOrEmpty(frameName)) frameName = "SURFACE";
                string prefix = GetTemplateChannel("FRAME_PREFIX", "REF: ");
                string newFrameText = $"{prefix}{frameName} ▾";

                if (!_hasInitializedState || newFrameText != _lastFrameText)
                {
                    _lastFrameText = newFrameText;
                    _frameText.text = newFrameText;

                    bool isSpecial = frameName.StartsWith("ORB", StringComparison.OrdinalIgnoreCase) ||
                                     frameName.StartsWith("BARY", StringComparison.OrdinalIgnoreCase) ||
                                     frameName.StartsWith("INER", StringComparison.OrdinalIgnoreCase);
                    bool isTarget = frameName.StartsWith("TGT", StringComparison.OrdinalIgnoreCase) ||
                                    frameName.StartsWith("TAR", StringComparison.OrdinalIgnoreCase);

                    TextStyleRole frameRole = isTarget ? TextStyleRole.Warning : (isSpecial ? TextStyleRole.Accent : TextStyleRole.SecondaryValue);
                    ApplyText(_frameText, frameRole, theme);
                    if (_frameOutline != null)
                    {
                        _frameOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                    }
                }
            }

            _hasInitializedState = true;
        }

        private string GetTemplateChannel(string key, string fallback)
        {
            if (string.IsNullOrEmpty(Config?.CustomTemplate)) return fallback;
            string[] pairs = Config.CustomTemplate.Split(';');
            foreach (string pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            return fallback;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_panelImage != null) _panelImage.color = Color.clear;
            if (_panelOutline != null) _panelOutline.enabled = false;

            if (_rcsText != null) _rcsText.text = GetTemplateChannel("RCS_LABEL", "RCS");
            if (_sasText != null) _sasText.text = GetTemplateChannel("SAS_LABEL", "SAS");

            if (_rcsBtn != null)
            {
                _rcsImg.color = _lastRcs ? theme.AccentPrimary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _rcsText.color = _lastRcs 
                    ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.InverseOnAccent, theme) 
                    : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                if (_rcsOutline != null) _rcsOutline.effectColor = _lastRcs ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }
            if (_sasBtn != null)
            {
                _sasImg.color = _lastSas ? theme.AccentPrimary : WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _sasText.color = _lastSas 
                    ? WidgetStyleManager.Instance.GetTextColor(TextStyleRole.InverseOnAccent, theme) 
                    : WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                if (_sasOutline != null) _sasOutline.effectColor = _lastSas ? theme.AccentPrimary : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }
            if (_frameBtn != null)
            {
                _frameImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                if (_frameOutline != null) _frameOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                if (_frameText != null) ApplyText(_frameText, TextStyleRole.SecondaryValue, theme);
            }
        }

        protected override void OnDestroy()
        {
            if (_rcsBtn != null) _rcsBtn.onClick.RemoveAllListeners();
            if (_sasBtn != null) _sasBtn.onClick.RemoveAllListeners();
            if (_frameBtn != null) _frameBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// 双向鼠标事件处理器：左键循环参考系，右键呼出 Principia 窗口
    /// </summary>
    public class RefFrameButtonHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
            else
            {
                OnLeftClick?.Invoke();
            }
        }
    }
}
