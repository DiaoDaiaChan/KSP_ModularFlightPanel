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

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            Vector2 panelSize = new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = panelSize;

            GameObject panel = UIFactory.CreatePanel(transform, "FlightControlBar", panelSize,
                Vector2.zero, theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.90f),
                theme != null ? (Color)theme.FrameBorderColor : Color.cyan, 1f * s);
            _panelImage = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();

            // 构建控制按键: RCS (-68), SAS (-26), REF FRAME (+43)
            BuildControlBar(panel.transform, s, theme);

            ApplyTheme(theme);
        }

        private void BuildControlBar(Transform parent, float s, ThemeConfig theme)
        {
            Vector2 toggleBtnSize = new Vector2(36f * s, 16f * s);

            // 1. RCS 开关
            _rcsBtn = UIFactory.CreateButton(parent, "Btn_RCS", toggleBtnSize, new Vector2(-68f * s, 0f), OnRCSToggle);
            _rcsImg = _rcsBtn.GetComponent<Image>();
            _rcsImg.color = theme != null ? (Color)theme.FrameBgColor : new Color(0.06f, 0.09f, 0.14f, 0.95f);
            _rcsOutline = _rcsBtn.gameObject.AddComponent<Outline>();
            Color borderDef = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            _rcsOutline.effectColor = new Color(borderDef.r, borderDef.g, borderDef.b, 0.40f);
            _rcsOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "Text", "RCS", Mathf.Max(8, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = toggleBtnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            // 2. SAS 主开关
            _sasBtn = UIFactory.CreateButton(parent, "Btn_SAS", toggleBtnSize, new Vector2(-26f * s, 0f), OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasImg.color = theme != null ? (Color)theme.FrameBgColor : new Color(0.06f, 0.09f, 0.14f, 0.95f);
            _sasOutline = _sasBtn.gameObject.AddComponent<Outline>();
            _sasOutline.effectColor = new Color(borderDef.r, borderDef.g, borderDef.b, 0.40f);
            _sasOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _sasText = UIFactory.CreateText(_sasBtn.transform, "Text", "SAS", Mathf.Max(8, Mathf.RoundToInt(8.5f * s)), TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            RectTransform sasRt = _sasText.GetComponent<RectTransform>();
            sasRt.sizeDelta = toggleBtnSize;
            sasRt.anchoredPosition = Vector2.zero;

            // 3. REF FRAME 模式胶囊按钮
            // 3. REF FRAME 模式胶囊按钮 (左键切换, 右键展开 Principia 参考系窗口)
            Vector2 frameBtnSize = new Vector2(86f * s, 16f * s);
            _frameBtn = UIFactory.CreateButton(parent, "Btn_RefFrame", frameBtnSize, new Vector2(43f * s, 0f), null);
            _frameImg = _frameBtn.GetComponent<Image>();
            _frameImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);
            _frameOutline = _frameBtn.gameObject.AddComponent<Outline>();
            Color accentSec = theme != null ? (Color)theme.AccentSecondary : Color.cyan;
            _frameOutline.effectColor = accentSec;
            _frameOutline.effectDistance = new Vector2(1f * s, 1f * s);

            var clickHandler = _frameBtn.gameObject.AddComponent<RefFrameButtonHandler>();
            clickHandler.OnLeftClick = OnCycleSpeedMode;
            clickHandler.OnRightClick = OnTogglePrincipiaWindow;

            _frameText = UIFactory.CreateText(_frameBtn.transform, "Text", "REF: SURFACE", Mathf.Max(7, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter, accentSec);
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

        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color activeCol = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color activeSecCol = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color inactiveBg = new Color(0.05f, 0.08f, 0.12f, 0.90f);
            Color borderDef = (theme != null) ? (Color)theme.FrameBorderColor : Color.gray;

            // 1. RCS 状态
            bool rcs = telem.IsRCSEnabled;
            if (_rcsImg != null) _rcsImg.color = rcs ? activeCol : inactiveBg;
            if (_rcsText != null) _rcsText.color = rcs ? Color.black : (theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            if (_rcsOutline != null) _rcsOutline.effectColor = rcs ? activeCol : new Color(borderDef.r, borderDef.g, borderDef.b, 0.35f);

            // 2. SAS 状态
            bool sas = telem.IsSASEnabled;
            if (_sasImg != null) _sasImg.color = sas ? activeCol : inactiveBg;
            if (_sasText != null) _sasText.color = sas ? Color.black : (theme != null ? (Color)theme.TextPrimaryColor : Color.white);
            if (_sasOutline != null) _sasOutline.effectColor = sas ? activeCol : new Color(borderDef.r, borderDef.g, borderDef.b, 0.35f);

            // 3. 参考系模式 (联动 Principia 权威参考系与原生 KSP 参考系)
            if (_frameText != null)
            {
                string frameName = telem.SpeedModeName ?? "SURFACE";
                if (string.IsNullOrEmpty(frameName)) frameName = "SURFACE";
                _frameText.text = $"REF: {frameName}";

                // 模式色彩：SURF (电光青蓝), ORB / BARY / INERTIAL (翡翠绿), TGT (亮金)
                Color frameColor = activeSecCol;
                if (frameName.StartsWith("ORB", StringComparison.OrdinalIgnoreCase) ||
                    frameName.StartsWith("BARY", StringComparison.OrdinalIgnoreCase) ||
                    frameName.StartsWith("INER", StringComparison.OrdinalIgnoreCase))
                {
                    frameColor = activeCol;
                }
                else if (frameName.StartsWith("TGT", StringComparison.OrdinalIgnoreCase) ||
                         frameName.StartsWith("TAR", StringComparison.OrdinalIgnoreCase))
                {
                    frameColor = (theme != null) ? (Color)theme.WarningColor : Color.yellow;
                }

                _frameText.color = frameColor;
                if (_frameOutline != null) _frameOutline.effectColor = frameColor;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;

            if (_panelImage != null) _panelImage.color = theme.FrameBgColor;
            if (_panelOutline != null) _panelOutline.effectColor = theme.FrameBorderColor;
            if (_frameOutline != null) _frameOutline.effectColor = theme.AccentSecondary;
            if (_frameText != null) _frameText.color = theme.AccentSecondary;
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
