using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 支持独立图层不透明度调节的微控件基类
    /// </summary>
    public abstract class CompositeLayerControlBase : BaseWidgetControl
    {
        private CanvasGroup _canvasGroup;

        public float Opacity
        {
            get => _canvasGroup != null ? _canvasGroup.alpha : 1.0f;
            set
            {
                EnsureCanvasGroup();
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = Mathf.Clamp01(value);
                }
            }
        }

        protected void EnsureCanvasGroup()
        {
            if (_canvasGroup == null && RootGameObject != null)
            {
                _canvasGroup = RootGameObject.GetComponent<CanvasGroup>() ?? RootGameObject.AddComponent<CanvasGroup>();
            }
        }

        protected CompositeLayerControlBase(BaseFlightWidget parent, string id, string displayName, WidgetControlCategory category, GameObject rootGo)
            : base(parent, id, displayName, category, rootGo)
        {
            EnsureCanvasGroup();
        }
    }

    /// <summary>
    /// 航电系统机载动作与开关控件 (RCS, SAS, GEAR, BRAKES, LIGHTS, ABORT, AG1~AG10)
    /// </summary>
    public class AvionicsSystemSwitchControl : CompositeLayerControlBase
    {
        public Button Button { get; private set; }
        public Image BackgroundImage { get; private set; }
        public Outline BorderOutline { get; private set; }
        public Text LabelText { get; private set; }
        public Image StatusLed { get; private set; }
        public AvionicsButtonFeedback Feedback { get; private set; }

        public string ActionType { get; set; } = "RCS";
        public bool IsToggle { get; set; } = true;
        private bool _isActive = false;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    UpdateLedVisual();
                }
            }
        }

        public AvionicsSystemSwitchControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Button btn, Image bg, Outline outline, Text label, Image led, string actionType)
            : base(parent, id, displayName, WidgetControlCategory.ActionButton, rootGo)
        {
            Button = btn;
            BackgroundImage = bg;
            BorderOutline = outline;
            LabelText = label;
            StatusLed = led;
            ActionType = actionType ?? "RCS";

            if (Button != null)
            {
                Button.onClick.RemoveAllListeners();
                Button.onClick.AddListener(OnButtonClicked);
            }

            if (rootGo != null)
            {
                Feedback = rootGo.GetComponent<AvionicsButtonFeedback>() ?? rootGo.AddComponent<AvionicsButtonFeedback>();
            }
        }

        private void OnButtonClicked()
        {
            // 通过 IFlightControl 调度飞控指令
            var ctrl = FlightTelemetryContext.Current as IFlightControl;
            string u = (ActionType ?? "").ToUpperInvariant();

            if (u == "RCS") ctrl?.ToggleRCS();
            else if (u == "SAS") ctrl?.ToggleSAS();
            else if (u == "STAGE_LOCK") ctrl?.ToggleStageLock();
            else if (u == "PRECISION") ctrl?.TogglePrecisionMode();
            else if (u == "CYCLE_FRAME") ctrl?.CycleSpeedMode();
            else
            {
#if KSP_RUNTIME
                try
                {
                    var vessel = FlightGlobals.ActiveVessel;
                    if (vessel != null)
                    {
                        if (u == "GEAR") vessel.ActionGroups.ToggleGroup(KSPActionGroup.Gear);
                        else if (u == "BRAKES") vessel.ActionGroups.ToggleGroup(KSPActionGroup.Brakes);
                        else if (u == "LIGHTS") vessel.ActionGroups.ToggleGroup(KSPActionGroup.Light);
                        else if (u == "ABORT") vessel.ActionGroups.ToggleGroup(KSPActionGroup.Abort);
                        else if (u == "SOLAR") vessel.ActionGroups.ToggleGroup(KSPActionGroup.Custom01);
                        else if (u.StartsWith("AG") && int.TryParse(u.Substring(2), out int agNum))
                        {
                            var ag = (KSPActionGroup)Enum.Parse(typeof(KSPActionGroup), "Custom" + agNum.ToString("D2"));
                            vessel.ActionGroups.ToggleGroup(ag);
                        }
                    }
                }
                catch { }
#endif
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;
            if (BackgroundImage != null)
            {
                BackgroundImage.material = style.GetUiMaterial(isText: false);
                BackgroundImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            }
            if (BorderOutline != null)
            {
                BorderOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            }
            if (LabelText != null)
            {
                style.ApplyTextStyle(LabelText, TextStyleRole.SecondaryValue, theme);
            }
            UpdateLedVisual();
            if (Feedback != null) Feedback.ApplyTheme(theme);
        }

        private void UpdateLedVisual()
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;
            if (StatusLed != null)
            {
                StatusLed.color = _isActive
                    ? style.GetTextColor(TextStyleRole.Accent, theme)
                    : WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Faint);
            }
            if (Feedback != null)
            {
                Feedback.SetToggleActive(_isActive);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                IsActive = false;
                return;
            }

            string u = (ActionType ?? "").ToUpperInvariant();
            if (u == "RCS") IsActive = telemetry.IsRCSEnabled;
            else if (u == "SAS") IsActive = telemetry.IsSASEnabled;
            else if (u == "STAGE_LOCK") IsActive = telemetry.IsStageLocked;
            else if (u == "PRECISION") IsActive = telemetry.IsPrecisionControl;
        }
    }

    /// <summary>
    /// 双重防误触安全分级器 (Safety Arm & Stage Trigger Control)
    /// </summary>
    public class SafetyArmStageControl : CompositeLayerControlBase
    {
        public Button StageButton { get; private set; }
        public Image StageBg { get; private set; }
        public Text StageLabel { get; private set; }

        public Button ArmButton { get; private set; }
        public Image ArmBg { get; private set; }
        public Text ArmLabel { get; private set; }
        public Image ArmLed { get; private set; }

        private bool _isArmed = false;
        public bool IsArmed
        {
            get => _isArmed;
            set
            {
                _isArmed = value;
                UpdateArmVisuals();
            }
        }

        public SafetyArmStageControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Button stageBtn, Image stageBg, Text stageLabel,
            Button armBtn, Image armBg, Text armLabel, Image armLed)
            : base(parent, id, displayName, WidgetControlCategory.ActionButton, rootGo)
        {
            StageButton = stageBtn;
            StageBg = stageBg;
            StageLabel = stageLabel;
            ArmButton = armBtn;
            ArmBg = armBg;
            ArmLabel = armLabel;
            ArmLed = armLed;

            if (ArmButton != null)
            {
                ArmButton.onClick.RemoveAllListeners();
                ArmButton.onClick.AddListener(() => IsArmed = !IsArmed);
            }

            if (StageButton != null)
            {
                StageButton.onClick.RemoveAllListeners();
                StageButton.onClick.AddListener(OnStageTriggered);
            }

            UpdateArmVisuals();
        }

        private void OnStageTriggered()
        {
            if (!_isArmed) return;

            var ctrl = FlightTelemetryContext.Current as IFlightControl;
            ctrl?.ActivateNextStage();

            // 触发后自动重置保险
            IsArmed = false;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            if (ArmLabel != null) style.ApplyTextStyle(ArmLabel, TextStyleRole.SecondaryValue, theme);
            if (StageLabel != null) style.ApplyTextStyle(StageLabel, TextStyleRole.PrimaryValue, theme);
            UpdateArmVisuals();
        }

        private void UpdateArmVisuals()
        {
            ThemeConfig theme = WidgetStyleManager.ResolveTheme(null);
            var style = WidgetStyleManager.Instance;

            if (ArmLed != null)
            {
                ArmLed.color = _isArmed ? theme.WarningColor : style.GetTextColor(TextStyleRole.Muted, theme);
            }

            if (ArmBg != null)
            {
                ArmBg.color = _isArmed
                    ? WidgetStyleManager.WithAlpha(theme.WarningColor, 0.35f)
                    : style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            }

            if (StageBg != null)
            {
                StageBg.color = _isArmed
                    ? theme.DangerColor
                    : WidgetStyleManager.WithAlpha(theme.DangerColor, 0.25f);
            }

            if (StageButton != null)
            {
                StageButton.interactable = _isArmed;
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            // 心跳中可同步分级锁状态
        }
    }

    /// <summary>
    /// 微型 SAS 姿态按键排 (Mini SAS Orientation Pad Control)
    /// </summary>
    public class MiniSasPadControl : CompositeLayerControlBase
    {
        private class SasBtnItem
        {
            public FlightSASMode Mode;
            public Button Btn;
            public Image Bg;
            public Text Label;
        }

        private readonly List<SasBtnItem> _buttons = new List<SasBtnItem>();
        private FlightSASMode _currentActiveMode = (FlightSASMode)(-1);

        public MiniSasPadControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo)
            : base(parent, id, displayName, WidgetControlCategory.ActionButton, rootGo)
        {
        }

        public void AddSasButton(FlightSASMode mode, Button btn, Image bg, Text label)
        {
            var item = new SasBtnItem { Mode = mode, Btn = btn, Bg = bg, Label = label };
            _buttons.Add(item);

            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    var ctrl = FlightTelemetryContext.Current as IFlightControl;
                    ctrl?.SetSASMode(mode);
                });
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            for (int i = 0; i < _buttons.Count; i++)
            {
                var item = _buttons[i];
                bool isSel = (item.Mode == _currentActiveMode);

                if (item.Bg != null)
                {
                    item.Bg.color = isSel
                        ? theme.AccentPrimary
                        : style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
                }
                if (item.Label != null)
                {
                    item.Label.color = isSel
                        ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme)
                        : style.GetTextColor(TextStyleRole.SecondaryValue, theme);
                }
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            FlightSASMode cur = telemetry.CurrentSASMode;
            if (cur != _currentActiveMode)
            {
                _currentActiveMode = cur;
                ApplyTheme(null);
            }
        }
    }

    /// <summary>
    /// 结构修饰图层控件 (凹槽卡片、发丝分割线、静态文本)
    /// </summary>
    public class StructuralShapeControl : CompositeLayerControlBase
    {
        public Image ShapeImage { get; private set; }
        public Outline ShapeOutline { get; private set; }
        public Text StaticText { get; private set; }

        public StructuralShapeControl(BaseFlightWidget parent, string id, string displayName, GameObject rootGo,
            Image img, Outline outline, Text txt = null)
            : base(parent, id, displayName, WidgetControlCategory.Misc, rootGo)
        {
            ShapeImage = img;
            ShapeOutline = outline;
            StaticText = txt;
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            var style = WidgetStyleManager.Instance;

            if (ShapeImage != null)
            {
                ShapeImage.material = style.GetUiMaterial(isText: false);
                ShapeImage.color = style.GetSurfaceColor(SurfaceStyleRole.Inset, theme);
            }
            if (ShapeOutline != null)
            {
                ShapeOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost, theme);
            }
            if (StaticText != null)
            {
                style.ApplyTextStyle(StaticText, TextStyleRole.Label, theme);
            }
        }

        public override void UpdateTelemetry(IFlightTelemetry telemetry)
        {
            // 纯结构装饰件无需遥测更新
        }
    }
}
