using System;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Widgets.Navigation;

namespace ModularFlightPanel.UI.Widgets
{
    // ====================================================================================================
    // Modular Flight Panel (MFP) - 官方标准蓝本：机载底控台组件 (Avionics Bottom Controls Blueprint)
    // 【现代声明式 UI 对象范式 (Object-DSL)】在类头部集中声明高阶交互控件，基类自动构建 UGUI、事件与主题管道
    // ====================================================================================================

    /// <summary>
    /// 一体化机载快速控制底控台 (Avionics Flight Control Bar - Unified)
    /// 严丝合缝嵌合于姿态球正下方 (宽度 184px, 高度 22px)。
    /// 严格对称标准布局：[RCS] [REF] [SAS]
    /// 1. 左侧：RCS 姿控动力开关 (自保持状态高亮，快捷键 R)
    /// 2. 中央：REF FRAME 权威导航参考系指示卡 (高反差矢量微标 + 参考系全称，左键循环切换 / 右键呼出 Principia 窗口)
    /// 3. 右侧：SAS 稳定性增益开关 (自保持状态高亮，快捷键 T)
    /// 严格遵照 MFP 标准：0 颜色字面量、0 场景查询、统一语义主题管线。
    /// </summary>
    /// <summary>
    /// 底控台零 GC 不可变遥测快照 (MFP-SPEC-012)
    /// </summary>
    public struct BottomControlsState : IEquatable<BottomControlsState>
    {
        public bool HasVessel;
        public bool IsRcsActive;
        public bool IsSasActive;
        public string RefCategory;
        public string RefTitle;

        public bool Equals(BottomControlsState other)
        {
            return HasVessel == other.HasVessel &&
                   IsRcsActive == other.IsRcsActive &&
                   IsSasActive == other.IsSasActive &&
                   RefCategory == other.RefCategory &&
                   RefTitle == other.RefTitle;
        }

        public override bool Equals(object obj) => obj is BottomControlsState other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasVessel.GetHashCode();
                hash = (hash * 397) ^ IsRcsActive.GetHashCode();
                hash = (hash * 397) ^ IsSasActive.GetHashCode();
                if (RefCategory != null) hash = (hash * 397) ^ RefCategory.GetHashCode();
                if (RefTitle != null) hash = (hash * 397) ^ RefTitle.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 底控台纯 C# 业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class BottomControlsLogic : WidgetLogic<BottomControlsState>
    {
        public string TypeToken = "{FRAME:TYPE}";
        public string FrameToken = "{FRAME}";
        public string FramePrefix = "";

        private SpeedDisplayMode _lastSpeedMode = (SpeedDisplayMode)(-1);
        private string _lastNavHookCategory = null;
        private string _lastNavHookTitle = null;
        private string _cachedCategory = "ORBIT";
        private string _cachedTitle = "ORBIT";

        public override void Reset()
        {
            CurrentState = default;
            _lastSpeedMode = (SpeedDisplayMode)(-1);
            _lastNavHookCategory = null;
            _lastNavHookTitle = null;
            _cachedCategory = "ORBIT";
            _cachedTitle = "ORBIT";
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                if (CurrentState.HasVessel)
                {
                    Reset();
                }
                return;
            }

            bool rcs = telemetry.IsRCSEnabled;
            bool sas = telemetry.IsSASEnabled;

            SpeedDisplayMode curSpeedMode = telemetry.CurrentSpeedMode;
            var navHook = NavBallHookService.Provider;
            string curHookCat = navHook != null ? navHook.ReferenceFrameCategory : null;
            string curHookTitle = navHook != null ? navHook.FrameName : null;

            bool frameDirty = curSpeedMode != _lastSpeedMode 
                || curHookCat != _lastNavHookCategory 
                || curHookTitle != _lastNavHookTitle;

            if (frameDirty)
            {
                _lastSpeedMode = curSpeedMode;
                _lastNavHookCategory = curHookCat;
                _lastNavHookTitle = curHookTitle;

                string curSpeedModeName = telemetry.SpeedModeName;

                string category = BaseFlightWidget.EvalToken(TypeToken, telemetry);
                if (string.IsNullOrEmpty(category) || category == "---")
                {
                    if (!string.IsNullOrEmpty(curHookCat))
                    {
                        category = curHookCat;
                    }
                    else
                    {
                        category = !string.IsNullOrEmpty(curSpeedModeName) ? curSpeedModeName.ToUpperInvariant() : "ORBIT";
                    }
                }

                string title = BaseFlightWidget.EvalToken(FrameToken, telemetry);
                if (string.IsNullOrEmpty(title) || title == "---")
                {
                    if (!string.IsNullOrEmpty(curHookTitle))
                    {
                        title = curHookTitle;
                    }
                    else
                    {
                        title = curSpeedModeName ?? category;
                    }
                }

                _cachedCategory = category;
                _cachedTitle = !string.IsNullOrEmpty(FramePrefix) ? $"{FramePrefix}{title}" : title;
            }

            CurrentState = new BottomControlsState
            {
                HasVessel = true,
                IsRcsActive = rcs,
                IsSasActive = sas,
                RefCategory = _cachedCategory,
                RefTitle = _cachedTitle
            };
        }
    }

    [FlightWidget("bottom_controls", "bottom_bar_controls", "rcs_ref_sas", "ref_rcs_sas",
        Category = WidgetCategory.Controls,
        DisplayName = "RCS/REF/SAS 底控台",
        Description = "整合 RCS 姿控动力开关、权威导航参考系矢量微标指示卡与 SAS 增益开关的航电底控台。",
        DefaultWidgetId = "core.bottom_controls",
        DefaultX = 0f,
        DefaultY = -88f,
        IsSingleton = true,
        ExactIds = new[] { "core.bottom_controls", "core.ref_rcs_sas", "core.rcs_ref_sas" })]
    public class BottomControlsWidget : BaseFlightWidget
    {
        // ── 头部集中声明区：尺寸、刷新率与全部交互微控件 (一屏之内尽收眼底) ──
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;
        public override Vector2 BaseSize => new Vector2(184f, 22f);
        protected override bool AutoCreateCardFrame => false; // 紧凑型浮动药丸底控栏，微控件自带胶囊插槽

        public ToggleButtonWidget Rcs = new ToggleButtonWidget("RCS", x: -71f, y: 0f, w: 38f, h: 18f, font: 8f)
        {
            OnClick = () => FlightTelemetryContext.Current?.ToggleRCS()
        };

        public ReferenceFrameButtonWidget Ref = new ReferenceFrameButtonWidget(x: 0f, y: 0f, w: 92f, h: 18f, font: 8f, iconSize: 15f)
        {
            OnClick = () => CycleReferenceFrame(),
            OnRightClick = () => TogglePrincipiaWindow()
        };

        public ToggleButtonWidget Sas = new ToggleButtonWidget("SAS", x: 71f, y: 0f, w: 38f, h: 18f, font: 8f)
        {
            OnClick = () => FlightTelemetryContext.Current?.ToggleSAS()
        };

        // 向后兼容别名引用
        public ReferenceFrameButtonWidget Frame => Ref;

        public static Action OnTogglePrincipiaWindowAction;
        public static Action OnCycleReferenceFrameAction;

        public static void CycleReferenceFrame()
        {
            if (OnCycleReferenceFrameAction != null)
            {
                OnCycleReferenceFrameAction.Invoke();
            }
            else if (ReferenceFrameWidget.OnCycleReferenceFrameAction != null)
            {
                ReferenceFrameWidget.OnCycleReferenceFrameAction.Invoke();
            }
            else
            {
                FlightTelemetryContext.Current?.CycleSpeedMode();
            }
        }

        public static void TogglePrincipiaWindow()
        {
            if (OnTogglePrincipiaWindowAction != null)
            {
                OnTogglePrincipiaWindowAction.Invoke();
            }
            else if (ReferenceFrameWidget.OnToggleReferenceFrameWindowAction != null)
            {
                ReferenceFrameWidget.OnToggleReferenceFrameWindowAction.Invoke();
            }
            else
            {
                CycleReferenceFrame();
            }
        }

        // ── 业务解耦大脑与私有缓存槽 ──
        private readonly BottomControlsLogic _logic = new BottomControlsLogic();
        protected override IWidgetLogic LogicCore => _logic;
        private readonly CachedDouble _lastRcsVal = new CachedDouble(0);

        // ── 视图初始化钩子：绑定多语言悬浮提示 ──
        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            ApplyTooltips();
            _logic.TypeToken = GetTemplateChannel("TYPE_TOKEN", "{FRAME:TYPE}");
            _logic.FrameToken = GetTemplateChannel("FRAME_TOKEN", "{FRAME}");
            _logic.FramePrefix = GetTemplateChannel("FRAME_PREFIX", "");
        }

        // ── 航电数据心跳与绘制循环 ──
        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
        }

        protected override void OnRenderState()
        {
            var state = _logic.CurrentState;
            if (!state.HasVessel) return;

            if (Rcs.IsActive != state.IsRcsActive)
                Rcs.IsActive = state.IsRcsActive;
            if (Sas.IsActive != state.IsSasActive)
                Sas.IsActive = state.IsSasActive;

            Ref.UpdateFrame(state.RefCategory, state.RefTitle, ThemeManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null));
        }

        protected override void OnResetPrivateCache()
        {
            base.OnResetPrivateCache();
            _lastRcsVal.Reset(0);
            _logic.Reset();
        }

        // ── 视觉主题与通道文本应用 ──
        public override void ApplyTheme(ThemeConfig theme)
        {
            base.ApplyTheme(theme);
            _logic.TypeToken = GetTemplateChannel("TYPE_TOKEN", "{FRAME:TYPE}");
            _logic.FrameToken = GetTemplateChannel("FRAME_TOKEN", "{FRAME}");
            _logic.FramePrefix = GetTemplateChannel("FRAME_PREFIX", "");
            Rcs.Text = GetTemplateChannel("RCS_LABEL", "RCS");
            Sas.Text = GetTemplateChannel("SAS_LABEL", "SAS");
            Ref.ApplyTheme(theme);
        }

        // ── 国际化与悬浮提示系统 ──
        private void ApplyTooltips()
        {
            Rcs.SetTooltip(
                I18n.Tr("WIDGET_BOTTOM_RCS_TITLE", "RCS 姿态推力系统"),
                I18n.Tr("WIDGET_BOTTOM_RCS_DESC", "开启/关闭反作用姿控喷气动力 (RCS)"),
                "R"
            );
            Ref.SetTooltip(
                I18n.Tr("TOOLTIP_REF_FRAME_TITLE", "导航参考系 (Reference Frame)"),
                I18n.Tr("TOOLTIP_REF_FRAME_DESC", "显示当前绘图与速度解算参考系。左键循环切换参考系，右键呼出 Principia 权威参考系设置窗口。"),
                I18n.Tr("TOOLTIP_REF_FRAME_HOTKEY", "[L-Click] 切换 [R-Click] 窗口")
            );
            Sas.SetTooltip(
                I18n.Tr("WIDGET_BOTTOM_SAS_TITLE", "SAS 稳定性增益系统"),
                I18n.Tr("WIDGET_BOTTOM_SAS_DESC", "开启/关闭姿态稳定增益系统 (SAS)"),
                "T"
            );
        }

        protected override void OnLanguageChanged()
        {
            ApplyTooltips();
        }

        // ── 安全生命周期销毁清理 ──
        protected override void OnDestroy()
        {
            if (Rcs?.ButtonComponent != null) Rcs.ButtonComponent.onClick.RemoveAllListeners();
            if (Sas?.ButtonComponent != null) Sas.ButtonComponent.onClick.RemoveAllListeners();
            if (Ref?.ButtonComponent != null) Ref.ButtonComponent.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
