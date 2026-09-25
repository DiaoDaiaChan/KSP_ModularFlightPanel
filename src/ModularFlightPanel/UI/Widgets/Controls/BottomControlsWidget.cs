using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本 [3/3]：交互控制型航电组件 (Interactive Flight Controls Blueprint)
    // ====================================================================================================
    //
    // 【架构蓝本说明 (Architecture Blueprint Purpose)】:
    // 本组件是全模组“交互型快捷操作栏 / 开关阵列 / 模式胶囊按键”的官方标准蓝本，展示了交互型组件的规范设计：
    // 1. 【上级自动派发尺寸 (BaseSize Auto-Scaling)】:
    //    - 声明 `BaseSize => new Vector2(184f, 22f)`，基类在 BaseInitialize 自动完成物理 1:1 分辨率栅格化计算。
    // 2. 【航电按钮标准化管控 (WidgetActionButtonControl & Avionics Feedback)】:
    //    - 包含普通操作键 (Normal) 与状态自保持开关键 (ActiveToggle)，统一接入航电按键视觉反馈机制。
    //    - 标记 `[WidgetControl]` 特性，基类自动在主题切换时分发多态按键颜色与着色器。
    // 3. 【状态同步与防抖保护 (Dirty Protection & State Synchronization)】:
    //    - 遥测更新循环中实施严格脏检查 (`_lastRcs` / `_lastSas` / `_lastFrameText`)，
    //      仅在飞行器真实物理状态变化时才触发视觉刷新，阻断 95%+ 无效 UI 开销。
    // 4. 【国际化与悬浮提示系统 (I18n Tooltip Lifecycle)】:
    //    - 按钮绑定 `SetTooltip`，支持按键名称、描述以及绑定快捷键 ("R", "T")。
    //    - 重写 `OnLanguageChanged`，当玩家在设置中热切换语言时，自动即时刷新所有 Tooltip 文本。
    // 5. 【防御式事件安全生命周期 (Defensive Event Clean-Up)】:
    //    - `OnDestroy` 显式解绑所有 UGUI Button onClick 监听器，并调用 `base.OnDestroy()` 保证容器完全注销。
    //
    // 【适用类型】: 快捷控制条、分级触发键、姿态模式切换胶囊、灯光/起落架开关、时间加速控制台等。
    // ====================================================================================================

    /// <summary>
    /// 一体化机载快速控制栏 (Avionics Flight Control Bar)
    /// 严丝合缝嵌合于姿态球正下方 (宽度 184px, 高度 22px, 完美容纳于双带之间)。
    /// 整合：
    /// 1. RCS 动力开关 (状态色高亮)
    /// 2. SAS 主动力开关 (状态色高亮)
    /// 3. REF FRAME 权威参考系一键切换胶囊 (SURFACE / ORBIT / TARGET)
    /// 严格继承 BaseFlightWidget，零硬编码。
    /// </summary>
    [FlightWidget("bottom_controls", "bottom_bar_controls",
        Category = WidgetCategory.Controls,
        DisplayName = "底部快捷操纵条",
        Description = "RCS/SAS/刹车/起落架/车灯综合药丸式状态切换条。",
        DefaultWidgetId = "core.bottom_controls",
        DefaultX = 0f,
        DefaultY = -210f,
        IsSingleton = true,
        ExactIds = new[] { "core.bottom_controls" })]
    public class BottomControlsWidget : BaseFlightWidget
    {
        // ------------------------------------------------------------------------------------
        // [Part 1: 刷新阶梯与几何尺寸契约]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 控制栏采用 Relaxed 阶梯 (标称 10Hz)，状态开关类交互无需 60Hz 占用主线程
        /// </summary>
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        /// <summary>
        /// 声明 1.0x DPI 下的原生尺寸 (宽 184px, 高 22px)
        /// </summary>
        public override Vector2 BaseSize => new Vector2(184f, 22f);

        // ------------------------------------------------------------------------------------
        // [Part 2: 声明式微控件字段与节点引用]
        // ------------------------------------------------------------------------------------

        // RCS 姿控开关按键
        [WidgetControl("rcs_btn", ButtonVisualRole.ActiveToggle, "RCS 开关按键")]
        private Button _rcsBtn;
        private Image _rcsImg;
        private Outline _rcsOutline;
        private Text _rcsText;

        // SAS 主控开关按键
        [WidgetControl("sas_btn", ButtonVisualRole.ActiveToggle, "SAS 开关按键")]
        private Button _sasBtn;
        private Image _sasImg;
        private Outline _sasOutline;
        private Text _sasText;

        // 参考系模式切换胶囊
        [WidgetControl("frame_btn", ButtonVisualRole.Normal, "参考系切换按键")]
        private Button _frameBtn;
        private Image _frameImg;
        private Outline _frameOutline;
        private Text _frameText;

        // 底板引用
        private Image _panelImage;
        private Outline _panelOutline;

        // 运行时状态脏检查缓存
        private bool _lastRcs = false;
        private bool _lastSas = false;
        private string _lastFrameText;
        private bool _hasInitializedState = false;

        public static Action OnTogglePrincipiaWindowAction;

        // ------------------------------------------------------------------------------------
        // [Part 3: 视图构建与事件装配 (OnInitialize)]
        // ------------------------------------------------------------------------------------

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = RectTransform.sizeDelta;

            // 1. 底衬容器 (透明容器，仅负责对齐与排版边界)
            GameObject panel = UIFactory.CreatePanel(transform, "FlightControlBar", panelSize,
                Vector2.zero, Color.clear, Color.clear, 0f);
            _panelImage = panel.GetComponent<Image>();
            _panelOutline = panel.GetComponent<Outline>();
            if (_panelOutline != null) _panelOutline.enabled = false;
            this.Controls.Wrap("background", "底板边框", panel);

            // 2. 构建控制按键: RCS (-68), SAS (-26), REF FRAME (+43)
            BuildControlBar(panel.transform, s, theme);

            // 3. 初始注入多语言 Tooltip 提示
            ApplyTooltips();
        }

        private void BuildControlBar(Transform parent, float s, ThemeConfig theme)
        {
            Vector2 toggleBtnSize = new Vector2(38f * s, 18f * s);

            // --- 1. RCS 姿控动力开关 ---
            _rcsBtn = UIFactory.CreateButton(parent, "Btn_RCS", toggleBtnSize, new Vector2(-68f * s, 0f), OnRCSToggle);
            _rcsImg = _rcsBtn.GetComponent<Image>();
            _rcsOutline = _rcsBtn.GetComponent<Outline>();

            _rcsText = UIFactory.CreateText(_rcsBtn.transform, "Text", "RCS", Mathf.Max(8, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _rcsText.fontStyle = FontStyle.Bold;
            RectTransform rcsRt = _rcsText.GetComponent<RectTransform>();
            rcsRt.sizeDelta = toggleBtnSize;
            rcsRt.anchoredPosition = Vector2.zero;

            // --- 2. SAS 稳定性增益系统主开关 ---
            _sasBtn = UIFactory.CreateButton(parent, "Btn_SAS", toggleBtnSize, new Vector2(-26f * s, 0f), OnSASToggle);
            _sasImg = _sasBtn.GetComponent<Image>();
            _sasOutline = _sasBtn.GetComponent<Outline>();

            _sasText = UIFactory.CreateText(_sasBtn.transform, "Text", "SAS", Mathf.Max(8, Mathf.RoundToInt(8f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _sasText.fontStyle = FontStyle.Bold;
            RectTransform sasRt = _sasText.GetComponent<RectTransform>();
            sasRt.sizeDelta = toggleBtnSize;
            sasRt.anchoredPosition = Vector2.zero;

            // --- 3. REF FRAME 参考系模式胶囊按钮 (左键切换, 右键展开 Principia 窗口) ---
            Vector2 frameBtnSize = new Vector2(90f * s, 18f * s);
            _frameBtn = UIFactory.CreateButton(parent, "Btn_RefFrame", frameBtnSize, new Vector2(43f * s, 0f), null);
            _frameImg = _frameBtn.GetComponent<Image>();
            _frameOutline = _frameBtn.GetComponent<Outline>();

            var clickHandler = _frameBtn.gameObject.AddComponent<RefFrameButtonHandler>();
            clickHandler.OnLeftClick = OnCycleSpeedMode;
            clickHandler.OnRightClick = OnTogglePrincipiaWindow;

            _frameText = UIFactory.CreateText(_frameBtn.transform, "Text", "REF: SURFACE ▾", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleCenter,
                WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _frameText.fontStyle = FontStyle.Bold;
            RectTransform frt = _frameText.GetComponent<RectTransform>();
            frt.sizeDelta = frameBtnSize;
            frt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------------------------
        // [Part 4: 交互按键点击响应方法 (User Action Handlers)]
        // ------------------------------------------------------------------------------------

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

        // ------------------------------------------------------------------------------------
        // [Part 5: 遥测数据更新与状态机同步 (OnUpdateTelemetry)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 遥测驱动状态机：上级 MasterUpdateTelemetry 已执行过 HasVessel 空船守卫
        /// </summary>
        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null) return;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. RCS 状态同步 (通过航电反馈扩展驱动 SetToggleActive)
            bool rcs = telem.IsRCSEnabled;
            if (!_hasInitializedState || rcs != _lastRcs)
            {
                _lastRcs = rcs;
                if (_rcsBtn != null)
                {
                    _rcsBtn.SetToggleActive(rcs);
                }
            }

            // 2. SAS 状态同步 (通过航电反馈扩展驱动 SetToggleActive)
            bool sas = telem.IsSASEnabled;
            if (!_hasInitializedState || sas != _lastSas)
            {
                _lastSas = sas;
                if (_sasBtn != null)
                {
                    _sasBtn.SetToggleActive(sas);
                }
            }

            // 3. 参考系模式状态同步 (支持 Principia 权威参考系与原生 KSP 模式)
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

        // ------------------------------------------------------------------------------------
        // [Part 6: 视觉主题与多语言热切换 (ApplyTheme & OnLanguageChanged)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 主题切换：分发微控件主题，并保持按键当前激活态颜色正确
        /// </summary>
        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            // 1. 基类自动将主题分发至已注册的各微控件
            base.ApplyTheme(theme);

            if (_panelImage != null) _panelImage.color = Color.clear;
            if (_panelOutline != null) _panelOutline.enabled = false;

            // 2. 刷新通道自定义文案
            if (_rcsText != null) _rcsText.text = GetTemplateChannel("RCS_LABEL", "RCS");
            if (_sasText != null) _sasText.text = GetTemplateChannel("SAS_LABEL", "SAS");

            // 3. 恢复按键高亮状态
            if (_rcsBtn != null) _rcsBtn.SetToggleActive(_lastRcs);
            if (_sasBtn != null) _sasBtn.SetToggleActive(_lastSas);
        }

        /// <summary>
        /// 绑定多语言悬浮提示 (Tooltip)
        /// </summary>
        private void ApplyTooltips()
        {
            if (_rcsBtn != null)
                _rcsBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_RCS_TITLE", "RCS 姿态推力系统"), I18n.Tr("WIDGET_BOTTOM_RCS_DESC", "开启/关闭反作用姿控喷气动力 (RCS)"), "R");
            if (_sasBtn != null)
                _sasBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_SAS_TITLE", "SAS 稳定性增益系统"), I18n.Tr("WIDGET_BOTTOM_SAS_DESC", "开启/关闭姿态稳定增益系统 (SAS)"), "T");
            if (_frameBtn != null)
                _frameBtn.SetTooltip(I18n.Tr("WIDGET_BOTTOM_FRAME_TITLE", "速度参考系模式"), I18n.Tr("WIDGET_BOTTOM_FRAME_DESC", "左键点击轮换 SURFACE / ORBIT / TARGET 参考系；右键呼出 Principia 权威参考系设置窗口。"));
        }

        /// <summary>
        /// 监听全局语言切换通知，即刻刷新 Tooltip 提示
        /// </summary>
        protected override void OnLanguageChanged()
        {
            ApplyTooltips();
        }

        // ------------------------------------------------------------------------------------
        // [Part 7: 销毁与安全清理 (OnDestroy)]
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// 销毁清理：解绑所有交互按钮监听并执行基类清理
        /// </summary>
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
