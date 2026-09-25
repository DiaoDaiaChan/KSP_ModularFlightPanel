using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

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
    [FlightWidget("bottom_controls", "bottom_bar_controls", Category = WidgetCategory.Controls, DisplayName = "底部快捷操纵条", Description = "RCS/SAS/刹车/起落架/车灯综合药丸式状态切换条。", DefaultWidgetId = "core.bottom_controls", DefaultX = 0f, DefaultY = -210f, IsSingleton = true, ExactIds = new[] { "core.bottom_controls" })]
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
            ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "background", "底板边框", panel);

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
            _rcsOutline = _rcsBtn.GetComponent<Outline>();

            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "Text", "RCS", Mathf.Max(8, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _rcsText.fontStyle = FontStyle.Bold;
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = toggleBtnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            // 2. SAS 主开关
            _sasBtn = UIFactory.CreateButton(parent, "Btn_SAS", toggleBtnSize, new Vector2(-26f * s, 0f), OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasOutline = _sasBtn.GetComponent<Outline>();

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
            _frameOutline = _frameBtn.GetComponent<Outline>();

            var clickHandler = _frameBtn.gameObject.AddComponent<RefFrameButtonHandler>();
            clickHandler.OnLeftClick = OnCycleSpeedMode;
            clickHandler.OnRightClick = OnTogglePrincipiaWindow;

            ApplyTooltips();

            _frameText = UIFactory.CreateText(_frameBtn.transform, "Text", "REF: SURFACE ▾", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter, WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _frameText.fontStyle = FontStyle.Bold;
            RectTransform frt = _frameText.GetComponent<RectTransform>();
            frt.sizeDelta = frameBtnSize;
            frt.anchoredPosition = Vector2.zero;

            // 标准化组件控件注册至管理器
            ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "rcs_btn", "RCS 控制开关", _rcsBtn.gameObject, _rcsBtn, _rcsImg, _rcsOutline, _rcsText, null, "RCS", OnRCSToggle, true));
            ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "sas_btn", "SAS 主控开关", _sasBtn.gameObject, _sasBtn, _sasImg, _sasOutline, _sasText, null, "SAS", OnSASToggle, true));
            ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "ref_frame", "参考系切换胶囊", _frameBtn.gameObject);
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
            if (telem == null || !telem.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. RCS 状态 (统一航电反馈状态机驱动)
            bool rcs = telem.IsRCSEnabled;
            if (!_hasInitializedState || rcs != _lastRcs)
            {
                _lastRcs = rcs;
                if (_rcsBtn != null)
                {
                    _rcsBtn.SetToggleActive(rcs);
                }
            }

            // 2. SAS 状态 (统一航电反馈状态机驱动)
            bool sas = telem.IsSASEnabled;
            if (!_hasInitializedState || sas != _lastSas)
            {
                _lastSas = sas;
                if (_sasBtn != null)
                {
                    _sasBtn.SetToggleActive(sas);
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
                _rcsBtn.GetFeedback()?.ApplyTheme(theme);
                _rcsBtn.SetToggleActive(_lastRcs);
            }
            if (_sasBtn != null)
            {
                _sasBtn.GetFeedback()?.ApplyTheme(theme);
                _sasBtn.SetToggleActive(_lastSas);
            }
            if (_frameBtn != null)
            {
                _frameBtn.GetFeedback()?.ApplyTheme(theme);
                if (_frameText != null) ApplyText(_frameText, TextStyleRole.SecondaryValue, theme);
            }
        }

        private void ApplyTooltips()
        {
            if (_rcsBtn != null)
                _rcsBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_RCS_TITLE", "RCS 姿态推力系统"), I18n.Tr("WIDGET_BOTTOM_RCS_DESC", "开启/关闭反作用姿控喷气动力 (RCS)"), "R");
            if (_sasBtn != null)
                _sasBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_SAS_TITLE", "SAS 稳定性增益系统"), I18n.Tr("WIDGET_BOTTOM_SAS_DESC", "开启/关闭姿态稳定增益系统 (SAS)"), "T");
            if (_frameBtn != null)
                _frameBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_FRAME_TITLE", "速度参考系模式"), I18n.Tr("WIDGET_BOTTOM_FRAME_DESC", "左键点击轮换 SURFACE / ORBIT / TARGET 参考系；右键呼出 Principia 权威参考系设置窗口。"));
        }

        protected override void OnLanguageChanged()
        {
            ApplyTooltips();
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
