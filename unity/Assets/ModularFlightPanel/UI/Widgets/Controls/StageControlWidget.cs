using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 现代化多功能航电分级与飞行姿态操纵台 (Modern Flight Control & Staging Suite)
    /// 遵照 MFP-SPEC-001..007 标准航电规范与现代全玻璃座舱 HUD 交互标准：
    /// 1. 顶栏安全联动：专业分级安全锁 (SAFE / ARMED) 与防误触分级点火触发总成 (FIRE STAGE ▶)；
    /// 2. 分级性能中枢：深晶数显分级窗 (STAGE 07) 结合单级 Δv、燃烧时序 (01:24)、推重比 (TWR) 与发动机计数；
    /// 3. 三轴姿态仪表：PITCH / ROLL / YAW 双向微光导轨、零位基准中轴线、极限量程刻线与机械配平游标 (Trim Pip)；
    /// 4. 推进剂监测槽：动态识别推进剂名称 (LIQUID FUEL / METHALOX 等)、剩余百分比与三段式预警变色；
    /// 5. 模式快速切换：NORM/PREC 微调操纵模式、STG/DCK 飞行/对接口模式与 KSP HUD 原生面板无缝显隐切换；
    /// 6. 严格落实 0 颜色字面量、零场景查询与 Critical (60Hz) 阶梯高保真刷新。
    /// </summary>
    [FlightWidget("stage_control", "staging_ctrl", Category = WidgetCategory.Controls, DisplayName = "操纵量指示与分级锁控制台", Description = "Pitch/Roll/Yaw 实时舵量标尺与分级安全锁定 (Alt+L) 防误触操作台。", DefaultWidgetId = "core.stage_control", DefaultX = -360f, DefaultY = -180f, IsSingleton = true, ExactIds = new[] { "core.stage_control" })]
    public class StageControlWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(204f, 186f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // 声明式微控件
        public TextWidget Title = TextWidget.Title("STAGE CONTROL");

        // 顶栏安全联动总成
        private Text _subStatusText;
        private Button _lockBtn;
        private Image _lockBtnBg;
        private Text _lockBtnText;
        private Button _fireBtn;
        private Image _fireBtnBg;
        private Text _fireBtnText;

        // 分级遥测中枢凹槽窗
        private GameObject _stageBayPanel;
        private Image _stageBayBg;
        private Outline _stageBayOutline;
        private Text _stageLabelText;
        private Text _stageNumText;
        private Text _stageDvText;
        private Text _stageTwrEngText;

        // 三轴操纵量标尺 (Pitch, Roll, Yaw)
        private struct AxisMeterUI
        {
            public Text Label;
            public RectTransform FillRt;
            public Image FillImg;
            public RectTransform TrimRt;
            public Text ValText;
        }

        private AxisMeterUI _pitchMeter;
        private AxisMeterUI _rollMeter;
        private AxisMeterUI _yawMeter;

        private const float TrackWidth = 104f;

        // 分级推进剂计量槽
        private Text _propNameText;
        private Text _propPctText;
        private RectTransform _propFillRt;
        private Image _propFillImg;
        private Image _propTrackBg;

        // 底部快捷切换按键组
        private Button _precBtn;
        private Image _precImg;
        private Text _precText;

        private Button _modeBtn;
        private Image _modeImg;
        private Text _modeText;

        private Button _stockToggleBtn;
        private Image _stockToggleImg;
        private Text _stockToggleText;
        private bool _stockHidden = true;

        // 脏检查与缓存守卫
        private string _lastStageNumStr = string.Empty;
        private string _lastStageDvStr = string.Empty;
        private string _lastStageTwrStr = string.Empty;
        private string _lastPropNameStr = string.Empty;
        private bool _lastLockedState = false;
        private float _lastPropFrac = -1f;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 panelSize = BaseSize * s;
            RectTransform.sizeDelta = panelSize;

            WidgetStyleManager style = WidgetStyleManager.Instance;

            // ==========================================
            // 2. 顶栏安全联动总成 (Safety Header)
            // ==========================================
            float topY = panelSize.y * 0.5f - 14f * s;

            if (Title != null && Title.TextComponent != null)
            {
                Title.Text = I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL");
                Title.SetRole(TextStyleRole.Cardinal);
                RectTransform titleRt = Title.TextComponent.rectTransform;
                if (titleRt != null)
                {
                    titleRt.sizeDelta = new Vector2(82f * s, 14f * s);
                    titleRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 48f * s, topY + 4f * s);
                }
            }

            _subStatusText = UIFactory.CreateText(transform, "SubStatus", "SAFETY INTERLOCK",
                Mathf.Max(5, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform subRt = _subStatusText.GetComponent<RectTransform>();
            subRt.sizeDelta = new Vector2(82f * s, 11f * s);
            subRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 48f * s, topY - 7f * s);

            // 安全锁按键 (SAFE / ARMED)
            Vector2 lockBtnSize = new Vector2(42f * s, 18f * s);
            _lockBtn = UIFactory.CreateButton(transform, "Btn_Lock", lockBtnSize,
                new Vector2(panelSize.x * 0.5f - 72f * s, topY), OnToggleLock);
            _lockBtnBg = _lockBtn.GetComponent<Image>();
            _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _lockBtnText = UIFactory.CreateText(_lockBtn.transform, "Text", "ARMED",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.Accent, theme));
            _lockBtnText.fontStyle = FontStyle.Bold;
            _lockBtnText.GetComponent<RectTransform>().sizeDelta = lockBtnSize;
            _lockBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_LOCK_TITLE", "分级安全锁 (Alt+L)"),
                I18n.Tr("TOOLTIP_STAGE_LOCK_DESC", "切换火箭分级保险回路。处于锁定状态时阻断一切误触发操作。"), "Alt+L");

            // 分级触发键 (STAGE ▶)
            Vector2 fireBtnSize = new Vector2(46f * s, 18f * s);
            _fireBtn = UIFactory.CreateButton(transform, "Btn_Fire", fireBtnSize,
                new Vector2(panelSize.x * 0.5f - 26f * s, topY), OnFireStage);
            _fireBtnBg = _fireBtn.GetComponent<Image>();
            _fireBtnBg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
            _fireBtnText = UIFactory.CreateText(_fireBtn.transform, "Text", "STAGE ▶",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _fireBtnText.fontStyle = FontStyle.Bold;
            _fireBtnText.GetComponent<RectTransform>().sizeDelta = fireBtnSize;
            _fireBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_FIRE_TITLE", "分级触发 (Space)"),
                I18n.Tr("TOOLTIP_STAGE_FIRE_DESC", "手动执行下一分级点火分离序列。"), "Space");

            // ==========================================
            // 3. 分级遥测中枢凹槽窗 (Stage Telemetry Bay)
            // ==========================================
            Vector2 baySize = new Vector2(panelSize.x - 14f * s, 36f * s);
            float bayY = panelSize.y * 0.5f - 44f * s;
            _stageBayPanel = UIFactory.CreatePanel(transform, "StageBay", baySize,
                new Vector2(0f, bayY),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme),
                WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost), 1f * s);
            _stageBayBg = _stageBayPanel.GetComponent<Image>();
            _stageBayOutline = _stageBayPanel.GetComponent<Outline>();

            // 左部：级数数显小窗 (内嵌黑底)
            Vector2 numBoxSize = new Vector2(30f * s, 28f * s);
            GameObject numBox = UIFactory.CreatePanel(_stageBayPanel.transform, "NumBox", numBoxSize,
                new Vector2(-baySize.x * 0.5f + 20f * s, 0f),
                WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme),
                WidgetStyleManager.Weighted(theme.FrameBorderColor, LineWeight.Ghost), 1f * s);

            _stageLabelText = UIFactory.CreateText(numBox.transform, "Label", "STAGE",
                Mathf.Max(5, Mathf.RoundToInt(5.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform slRt = _stageLabelText.GetComponent<RectTransform>();
            slRt.sizeDelta = new Vector2(numBoxSize.x, 9f * s);
            slRt.anchoredPosition = new Vector2(0f, 8f * s);

            _stageNumText = UIFactory.CreateText(numBox.transform, "StageNum", "07",
                Mathf.Max(8, Mathf.RoundToInt(11.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageNumText.fontStyle = FontStyle.Bold;
            RectTransform snRt = _stageNumText.GetComponent<RectTransform>();
            snRt.sizeDelta = new Vector2(numBoxSize.x, 16f * s);
            snRt.anchoredPosition = new Vector2(0f, -4f * s);

            // 右部：单级性能 (Δv, 燃烧时间, TWR, 发动机数)
            _stageDvText = UIFactory.CreateText(_stageBayPanel.transform, "StageDv", "2,350 m/s",
                Mathf.Max(8, Mathf.RoundToInt(10.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _stageDvText.fontStyle = FontStyle.Bold;
            RectTransform dvRt = _stageDvText.GetComponent<RectTransform>();
            dvRt.sizeDelta = new Vector2(136f * s, 16f * s);
            dvRt.anchoredPosition = new Vector2(-baySize.x * 0.5f + 108f * s, 6.5f * s);

            _stageTwrEngText = UIFactory.CreateText(_stageBayPanel.transform, "StageTwrEng", "01:24 · 1.45 TWR · 4 ENG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform twrRt = _stageTwrEngText.GetComponent<RectTransform>();
            twrRt.sizeDelta = new Vector2(136f * s, 13f * s);
            twrRt.anchoredPosition = new Vector2(-baySize.x * 0.5f + 108f * s, -7f * s);

            // ==========================================
            // 4. 三轴姿态操纵量仪表区 (Pitch, Roll, Yaw)
            // ==========================================
            float axisStartY = bayY - 29f * s;
            float axisSpacing = 16f * s;

            _pitchMeter = BuildAxisMeter(transform, "Pitch", I18n.Tr("WIDGET_AXIS_PITCH", "PITCH"), axisStartY, s, theme);
            _rollMeter = BuildAxisMeter(transform, "Roll", I18n.Tr("WIDGET_AXIS_ROLL", "ROLL"), axisStartY - axisSpacing, s, theme);
            _yawMeter = BuildAxisMeter(transform, "Yaw", I18n.Tr("WIDGET_AXIS_YAW", "YAW"), axisStartY - axisSpacing * 2f, s, theme);

            // ==========================================
            // 5. 分级推进剂监测槽 (Propellant Tank)
            // ==========================================
            float propY = axisStartY - axisSpacing * 2f - 20f * s;

            _propNameText = UIFactory.CreateText(transform, "PropName", I18n.Tr("WIDGET_PROP_PROPELLANT", "PROPELLANT"),
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform pNameRt = _propNameText.GetComponent<RectTransform>();
            pNameRt.sizeDelta = new Vector2(110f * s, 12f * s);
            pNameRt.anchoredPosition = new Vector2(-panelSize.x * 0.5f + 62f * s, propY + 7f * s);

            _propPctText = UIFactory.CreateText(transform, "PropPct", "100.0%",
                Mathf.Max(6, Mathf.RoundToInt(7.5f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            _propPctText.fontStyle = FontStyle.Bold;
            RectTransform pPctRt = _propPctText.GetComponent<RectTransform>();
            pPctRt.sizeDelta = new Vector2(60f * s, 12f * s);
            pPctRt.anchoredPosition = new Vector2(panelSize.x * 0.5f - 38f * s, propY + 7f * s);

            // 推进剂槽轨道 (宽 188px, 高 3.5px)
            GameObject propTrack = UIFactory.CreatePanel(transform, "PropTrack",
                new Vector2(188f * s, 3.5f * s), new Vector2(0f, propY - 3f * s),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _propTrackBg = propTrack.GetComponent<Image>();

            // 推进剂填充条
            GameObject propFill = UIFactory.CreatePanel(propTrack.transform, "Fill",
                new Vector2(188f * s, 3.5f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            _propFillRt = propFill.GetComponent<RectTransform>();
            _propFillRt.anchorMin = new Vector2(0f, 0.5f);
            _propFillRt.anchorMax = new Vector2(0f, 0.5f);
            _propFillRt.pivot = new Vector2(0f, 0.5f);
            _propFillRt.anchoredPosition = Vector2.zero;
            _propFillImg = propFill.GetComponent<Image>();

            // ==========================================
            // 6. 底部快捷模式切换按键组
            // ==========================================
            float bottomY = -panelSize.y * 0.5f + 14f * s;
            Vector2 miniBtnSize = new Vector2(56f * s, 18f * s);

            _precBtn = UIFactory.CreateButton(transform, "Btn_Prec", miniBtnSize,
                new Vector2(-60f * s, bottomY), OnTogglePrecision);
            _precImg = _precBtn.GetComponent<Image>();
            _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _precText = UIFactory.CreateText(_precBtn.transform, "Text", "NORM",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _precText.fontStyle = FontStyle.Bold;
            _precText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;
            _precBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_PREC_TITLE", "操纵微调 (Caps Lock)"),
                I18n.Tr("TOOLTIP_STAGE_PREC_DESC", "切换常规舵面操纵与精密微调操纵模式。"), "Caps Lock");

            _modeBtn = UIFactory.CreateButton(transform, "Btn_Mode", miniBtnSize,
                new Vector2(0f, bottomY), OnToggleMode);
            _modeImg = _modeBtn.GetComponent<Image>();
            _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _modeText = UIFactory.CreateText(_modeBtn.transform, "Text", "STG",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _modeText.fontStyle = FontStyle.Bold;
            _modeText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;
            _modeBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_MODE_TITLE", "控制模式切换"),
                I18n.Tr("TOOLTIP_STAGE_MODE_DESC", "在常规分级飞行姿态模式与对接口平移操纵模式间切换。"), "Flight Mode");

            _stockToggleBtn = UIFactory.CreateButton(transform, "Btn_Stock", miniBtnSize,
                new Vector2(60f * s, bottomY), OnToggleStockVisibility);
            _stockToggleImg = _stockToggleBtn.GetComponent<Image>();
            _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            _stockToggleText = UIFactory.CreateText(_stockToggleBtn.transform, "Text", "KSP HUD",
                Mathf.Max(6, Mathf.RoundToInt(6.5f * s)), TextAnchor.MiddleCenter, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _stockToggleText.fontStyle = FontStyle.Bold;
            _stockToggleText.GetComponent<RectTransform>().sizeDelta = miniBtnSize;
            _stockToggleBtn.gameObject.SetTooltip(I18n.Tr("TOOLTIP_STAGE_STOCK_TITLE", "原生界面显隐"),
                I18n.Tr("TOOLTIP_STAGE_STOCK_DESC", "隐藏或还原游戏左下角原版分级控制面板。"), "HUD Toggle");

            // 初始化时默认执行静默隐藏原版左下角
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden);

            // 标准化组件内部控件注册至管理器
            if (_fireBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_fire_btn", "分级点火触发键", _fireBtn.gameObject, _fireBtn, _fireBtnBg, null, _fireBtnText, null, "STAGE", OnFireStage, false));
            }
            if (_lockBtn != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.Register(this,
                    new ModularFlightPanel.UI.Framework.WidgetActionButtonControl(this, "stage_lock_btn", "分级安全锁按键", _lockBtn.gameObject, _lockBtn, _lockBtnBg, null, _lockBtnText, null, "ARMED", OnToggleLock, true));
            }
            if (_stageBayPanel != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "stage_telemetry_bay", "分级数据凹槽窗", _stageBayPanel);
            }
            if (_propTrackBg != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "propellant_meter", "推进剂余量槽", _propTrackBg.gameObject);
            }

            ApplyTheme(theme);
        }

        private AxisMeterUI BuildAxisMeter(Transform parent, string name, string label, float yPos, float s, ThemeConfig theme)
        {
            AxisMeterUI meter = new AxisMeterUI();
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 轴标签 (PITCH / ROLL / YAW)
            meter.Label = UIFactory.CreateText(parent, name + "_Label", label,
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleLeft, style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            RectTransform lblRt = meter.Label.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(36f * s, 14f * s);
            lblRt.anchoredPosition = new Vector2(-76f * s, yPos);

            // 标尺轨道背景 (宽 104px, 高 3.5px)
            GameObject track = UIFactory.CreatePanel(parent, name + "_Track",
                new Vector2(TrackWidth * s, 3.5f * s), new Vector2(4f * s, yPos),
                WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));

            // 两端极限量程刻线 (-100% / +100%)
            Color tickCol = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            UIFactory.CreatePanel(track.transform, "TickNeg",
                new Vector2(1f * s, 5f * s), new Vector2(-(TrackWidth * 0.5f) * s, 0f), tickCol);
            UIFactory.CreatePanel(track.transform, "TickPos",
                new Vector2(1f * s, 5f * s), new Vector2((TrackWidth * 0.5f) * s, 0f), tickCol);

            // 零位中心基准刻线 (中轴线高光)
            UIFactory.CreatePanel(track.transform, "CenterTick",
                new Vector2(1.5f * s, 7f * s), Vector2.zero, style.GetTextColor(TextStyleRole.PrimaryValue, theme));

            // 双向偏转填充条
            GameObject fill = UIFactory.CreatePanel(track.transform, "Fill",
                new Vector2(0f, 3.5f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Primary, theme));
            meter.FillRt = fill.GetComponent<RectTransform>();
            meter.FillImg = fill.GetComponent<Image>();

            // 配平指示游标 (Trim Pip)
            GameObject trim = UIFactory.CreatePanel(track.transform, "TrimPip",
                new Vector2(2f * s, 7f * s), Vector2.zero, style.GetMeterColor(MeterStyleRole.Warning, theme));
            meter.TrimRt = trim.GetComponent<RectTransform>();

            // 偏转读数百分比 (+24%)
            meter.ValText = UIFactory.CreateText(parent, name + "_Val", "0%",
                Mathf.Max(6, Mathf.RoundToInt(7f * s)), TextAnchor.MiddleRight, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform valRt = meter.ValText.GetComponent<RectTransform>();
            valRt.sizeDelta = new Vector2(34f * s, 14f * s);
            valRt.anchoredPosition = new Vector2(80f * s, yPos);

            return meter;
        }

        private void OnToggleLock()
        {
            FlightTelemetryContext.Current?.ToggleStageLock();
        }

        private void OnFireStage()
        {
            FlightTelemetryContext.Current?.ActivateNextStage();
        }

        private void OnTogglePrecision()
        {
            FlightTelemetryContext.Current?.TogglePrecisionMode();
        }

        private void OnToggleMode()
        {
            FlightTelemetryContext.Current?.ToggleFlightMode();
        }

        private void OnToggleStockVisibility()
        {
            _stockHidden = !_stockHidden;
            NavBallHookService.HideStockBottomLeftAction?.Invoke(_stockHidden);
            if (_stockToggleText != null)
            {
                _stockToggleText.text = _stockHidden ? "KSP HUD" : "KSP [ON]";
            }
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telem)
        {
            if (telem == null || !telem.HasVessel) return;
            float s = CurrentDpiScale;

            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 1. 分级安全锁与就绪联动
            bool isLocked = telem.IsStageLocked;

            if (isLocked != _lastLockedState)
            {
                _lastLockedState = isLocked;
                string lockLabel = isLocked ? "LOCKED" : "ARMED";
                SetTextIfChanged(_lockBtnText, lockLabel);

                if (isLocked)
                {
                    _lockBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                    ApplyText(_lockBtnText, TextStyleRole.Danger, theme);
                    _fireBtnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    ApplyText(_fireBtnText, TextStyleRole.SecondaryValue, theme);
                    SetTextIfChanged(_subStatusText, "SAFETY LOCKED");
                    ApplyText(_subStatusText, TextStyleRole.Danger, theme);
                }
                else
                {
                    _lockBtnBg.color = WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
                    ApplyText(_lockBtnText, TextStyleRole.Accent, theme);
                    _fireBtnBg.color = style.GetMeterColor(MeterStyleRole.Warning, theme);
                    ApplyText(_fireBtnText, TextStyleRole.PrimaryValue, theme);
                    SetTextIfChanged(_subStatusText, "SYSTEM ARMED");
                    ApplyText(_subStatusText, TextStyleRole.Accent, theme);
                }
            }

            // 2. 分级数字读数与性能参数 (Dirty Checking)
            string sNumStr = $"{telem.CurrentStage:D2}";
            if (sNumStr != _lastStageNumStr)
            {
                _lastStageNumStr = sNumStr;
                SetTextIfChanged(_stageNumText, sNumStr);
            }

            double dv = telem.StageDeltaV;
            string dvStr = $"{dv:N0} m/s";
            if (dvStr != _lastStageDvStr)
            {
                _lastStageDvStr = dvStr;
                SetTextIfChanged(_stageDvText, dvStr);
            }

            int burnSec = Mathf.Max(0, (int)telem.StageBurnTime);
            int m = burnSec / 60;
            int sec = burnSec % 60;
            string twrStr = telem.TWR > 0.01 
                ? $"{m:00}:{sec:00} · {telem.TWR:F2} TWR · {telem.ActiveEngines} ENG" 
                : $"{m:00}:{sec:00} · {telem.ActiveEngines} ENG";
            if (twrStr != _lastStageTwrStr)
            {
                _lastStageTwrStr = twrStr;
                SetTextIfChanged(_stageTwrEngText, twrStr);
            }

            // 3. 三轴舵面偏转与配平
            UpdateAxisVisuals(telem.PitchInput, telem.PitchTrim, _pitchMeter.FillRt, _pitchMeter.TrimRt, _pitchMeter.ValText, s);
            UpdateAxisVisuals(telem.RollInput, telem.RollTrim, _rollMeter.FillRt, _rollMeter.TrimRt, _rollMeter.ValText, s);
            UpdateAxisVisuals(telem.YawInput, telem.YawTrim, _yawMeter.FillRt, _yawMeter.TrimRt, _yawMeter.ValText, s);

            // 4. 分级推进剂指示条 (100% 语义驱动)
            float propFrac = Mathf.Clamp01(telem.StagePropellantFraction);
            string rawName = telem.StagePropellantName;
            if (string.IsNullOrEmpty(rawName)) rawName = "PROPELLANT";
            if (rawName.StartsWith("PROP:", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(5).Trim();
            else if (rawName.StartsWith("PROP", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(4).Trim();
            if (string.IsNullOrEmpty(rawName)) rawName = "PROPELLANT";

            string pNameStr = rawName.ToUpperInvariant();
            if (pNameStr != _lastPropNameStr)
            {
                _lastPropNameStr = pNameStr;
                SetTextIfChanged(_propNameText, pNameStr);
            }

            if (Math.Abs(propFrac - _lastPropFrac) > 0.005f)
            {
                _lastPropFrac = propFrac;
                string pPctStr = $"{propFrac * 100f:F1}%";
                SetTextIfChanged(_propPctText, pPctStr);

                TextStyleRole pRole = (propFrac > 0.25f) ? TextStyleRole.PrimaryValue : ((propFrac > 0.10f) ? TextStyleRole.Warning : TextStyleRole.Danger);
                ApplyText(_propPctText, pRole, theme);

                if (_propFillRt != null)
                {
                    _propFillRt.sizeDelta = new Vector2(188f * s * propFrac, 3.5f * s);
                }
                if (_propFillImg != null)
                {
                    MeterStyleRole fillRole = (propFrac > 0.25f) ? MeterStyleRole.Primary : ((propFrac > 0.10f) ? MeterStyleRole.Warning : MeterStyleRole.Danger);
                    _propFillImg.color = style.GetMeterColor(fillRole, theme);
                }
            }

            // 5. 底部模式按键
            if (_precText != null)
            {
                string pStr = telem.IsPrecisionControl ? "PREC" : "NORM";
                SetTextIfChanged(_precText, pStr);
                ApplyText(_precText, telem.IsPrecisionControl ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
            }
            if (_modeText != null)
            {
                string mStr = telem.IsDockingMode ? "DCK" : "STG";
                SetTextIfChanged(_modeText, mStr);
                ApplyText(_modeText, telem.IsDockingMode ? TextStyleRole.Warning : TextStyleRole.SecondaryValue, theme);
            }
        }

        private void UpdateAxisVisuals(float input, float trim, RectTransform fillRt, RectTransform trimRt, Text valText, float s)
        {
            float halfWidth = (TrackWidth * 0.5f) * s;
            float clampedInput = Mathf.Clamp(input, -1f, 1f);
            float fillWidth = Mathf.Abs(clampedInput) * halfWidth;
            float fillCenterOffset = (clampedInput >= 0f) ? (fillWidth * 0.5f) : (-fillWidth * 0.5f);

            if (fillRt != null)
            {
                fillRt.sizeDelta = new Vector2(fillWidth, 3.5f * s);
                fillRt.anchoredPosition = new Vector2(fillCenterOffset, 0f);
            }

            if (trimRt != null)
            {
                float clampedTrim = Mathf.Clamp(trim, -1f, 1f);
                trimRt.anchoredPosition = new Vector2(clampedTrim * halfWidth, 0f);
            }

            if (valText != null)
            {
                int pct = Mathf.RoundToInt(clampedInput * 100f);
                string str = (pct > 0) ? $"+{pct}%" : $"{pct}%";
                SetTextIfChanged(valText, str);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            theme = WidgetStyleManager.ResolveTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            // 顶栏
            if (Title != null)
            {
                Title.Text = GetTemplateChannel("TITLE", I18n.Tr("WIDGET_STAGE_CTRL_TITLE", "STAGE CONTROL"));
                Title.SetRole(TextStyleRole.Cardinal);
            }
            if (_subStatusText != null) ApplyText(_subStatusText, _lastLockedState ? TextStyleRole.Danger : TextStyleRole.SecondaryValue, theme);

            if (_lockBtnBg != null) _lockBtnBg.color = _lastLockedState ? WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme) : WidgetStyleManager.StatusPanel(StatusSurfaceRole.Success);
            if (_lockBtnText != null) ApplyText(_lockBtnText, _lastLockedState ? TextStyleRole.Danger : TextStyleRole.Accent, theme);

            if (_fireBtnBg != null) _fireBtnBg.color = _lastLockedState ? WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme) : style.GetMeterColor(MeterStyleRole.Warning, theme);
            if (_fireBtnText != null) ApplyText(_fireBtnText, TextStyleRole.PrimaryValue, theme);

            // 分级中枢凹槽窗
            if (_stageBayBg != null) _stageBayBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stageBayOutline != null) _stageBayOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_stageLabelText != null) ApplyText(_stageLabelText, TextStyleRole.SecondaryValue, theme);
            if (_stageNumText != null) ApplyText(_stageNumText, TextStyleRole.PrimaryValue, theme);
            if (_stageDvText != null) ApplyText(_stageDvText, TextStyleRole.PrimaryValue, theme);
            if (_stageTwrEngText != null) ApplyText(_stageTwrEngText, TextStyleRole.SecondaryValue, theme);

            // 三轴仪表
            ApplyText(_pitchMeter.Label, TextStyleRole.SecondaryValue, theme);
            ApplyText(_pitchMeter.ValText, TextStyleRole.PrimaryValue, theme);
            if (_pitchMeter.FillImg != null) _pitchMeter.FillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_rollMeter.Label, TextStyleRole.SecondaryValue, theme);
            ApplyText(_rollMeter.ValText, TextStyleRole.PrimaryValue, theme);
            if (_rollMeter.FillImg != null) _rollMeter.FillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            ApplyText(_yawMeter.Label, TextStyleRole.SecondaryValue, theme);
            ApplyText(_yawMeter.ValText, TextStyleRole.PrimaryValue, theme);
            if (_yawMeter.FillImg != null) _yawMeter.FillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            // 推进剂槽
            if (_propNameText != null) ApplyText(_propNameText, TextStyleRole.SecondaryValue, theme);
            if (_propPctText != null) ApplyText(_propPctText, TextStyleRole.PrimaryValue, theme);
            if (_propTrackBg != null) _propTrackBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_propFillImg != null) _propFillImg.color = style.GetMeterColor(MeterStyleRole.Primary, theme);

            // 底部按键
            if (_precImg != null) _precImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_precText != null) ApplyText(_precText, TextStyleRole.SecondaryValue, theme);

            if (_modeImg != null) _modeImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_modeText != null) ApplyText(_modeText, TextStyleRole.SecondaryValue, theme);

            if (_stockToggleImg != null) _stockToggleImg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
            if (_stockToggleText != null) ApplyText(_stockToggleText, TextStyleRole.SecondaryValue, theme);
        }

        protected override void OnDestroy()
        {
            if (_lockBtn != null) _lockBtn.onClick.RemoveAllListeners();
            if (_fireBtn != null) _fireBtn.onClick.RemoveAllListeners();
            if (_precBtn != null) _precBtn.onClick.RemoveAllListeners();
            if (_modeBtn != null) _modeBtn.onClick.RemoveAllListeners();
            if (_stockToggleBtn != null) _stockToggleBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
