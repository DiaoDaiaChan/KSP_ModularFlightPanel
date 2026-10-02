using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI.Framework;

using ModularFlightPanel.Core.Rendering;
namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 环形航电 SAS 朝向罗盘 (Avionics Circular SAS Orientation Dial)
    /// 包含：
    /// 1. 纯圆形激光蚀刻底盘 (带外环金属光泽与暗色航电玻璃背衬)
    /// 2. 中央飞船/火箭动态二维矢量剪影 (跟随 Roll 滚转角实时旋转，带前缘前向定位针，点击可设 STAB)
    /// 3. 9 大空间方位微型按键 (STAB, PRO, RET, NRM, ANT, R-IN, R-OUT, TGT, MAN)，
    ///    按空间力学对称辐射排布，零重叠交叉，激活时发光脉冲高亮与反色文字
    /// 4. 底部微型模式状态胶囊标牌 (SAS: PROGRADE / SAS: OFF)
    /// 严格继承 BaseFlightWidget，零硬编码。
    /// </summary>
    public enum SASDialDisplayMode
    {
        Mode2D = 0,
        Mode3D = 1
    }

    public enum DirectorVisualState
    {
        Inactive,
        Locked,
        Guiding,
        Dim
    }

    public readonly struct SASDialState : IEquatable<SASDialState>
    {
        public readonly bool HasVessel;
        public readonly bool SasOn;
        public readonly FlightSASMode CurrentMode;
        public readonly double Roll;
        public readonly double Pitch;
        public readonly double Heading;
        public readonly bool AttDirty;
        public readonly bool ModeChanged;
        public readonly float TargetRotZ;
        public readonly DirectorVisualState DirectorState;
        public readonly bool IsDirectorLocked;
        public readonly Vector2 TargetDirectorNormPos;
        public readonly float TargetDirectorRotZ;
        public readonly string StatusText;
        public readonly TextStyleRole BadgeRole;

        public SASDialState(
            bool hasVessel,
            bool sasOn,
            FlightSASMode currentMode,
            double roll,
            double pitch,
            double heading,
            bool attDirty,
            bool modeChanged,
            float targetRotZ,
            DirectorVisualState directorState,
            bool isDirectorLocked,
            Vector2 targetDirectorNormPos,
            float targetDirectorRotZ,
            string statusText,
            TextStyleRole badgeRole)
        {
            HasVessel = hasVessel;
            SasOn = sasOn;
            CurrentMode = currentMode;
            Roll = roll;
            Pitch = pitch;
            Heading = heading;
            AttDirty = attDirty;
            ModeChanged = modeChanged;
            TargetRotZ = targetRotZ;
            DirectorState = directorState;
            IsDirectorLocked = isDirectorLocked;
            TargetDirectorNormPos = targetDirectorNormPos;
            TargetDirectorRotZ = targetDirectorRotZ;
            StatusText = statusText;
            BadgeRole = badgeRole;
        }

        public bool Equals(SASDialState other)
        {
            return HasVessel == other.HasVessel &&
                   SasOn == other.SasOn &&
                   CurrentMode == other.CurrentMode &&
                   Math.Abs(Roll - other.Roll) < 0.001 &&
                   Math.Abs(Pitch - other.Pitch) < 0.001 &&
                   Math.Abs(Heading - other.Heading) < 0.001 &&
                   AttDirty == other.AttDirty &&
                   ModeChanged == other.ModeChanged &&
                   Mathf.Abs(TargetRotZ - other.TargetRotZ) < 0.001f &&
                   DirectorState == other.DirectorState &&
                   IsDirectorLocked == other.IsDirectorLocked &&
                   TargetDirectorNormPos == other.TargetDirectorNormPos &&
                   Mathf.Abs(TargetDirectorRotZ - other.TargetDirectorRotZ) < 0.001f &&
                   string.Equals(StatusText, other.StatusText, StringComparison.Ordinal) &&
                   BadgeRole == other.BadgeRole;
        }

        public override bool Equals(object obj) => obj is SASDialState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 397) ^ HasVessel.GetHashCode();
                hash = (hash * 397) ^ SasOn.GetHashCode();
                hash = (hash * 397) ^ ((int)CurrentMode).GetHashCode();
                hash = (hash * 397) ^ Roll.GetHashCode();
                hash = (hash * 397) ^ Pitch.GetHashCode();
                hash = (hash * 397) ^ Heading.GetHashCode();
                hash = (hash * 397) ^ AttDirty.GetHashCode();
                hash = (hash * 397) ^ ModeChanged.GetHashCode();
                hash = (hash * 397) ^ TargetRotZ.GetHashCode();
                hash = (hash * 397) ^ ((int)DirectorState).GetHashCode();
                hash = (hash * 397) ^ IsDirectorLocked.GetHashCode();
                hash = (hash * 397) ^ TargetDirectorNormPos.GetHashCode();
                hash = (hash * 397) ^ TargetDirectorRotZ.GetHashCode();
                hash = (hash * 397) ^ (StatusText != null ? StatusText.GetHashCode() : 0);
                hash = (hash * 397) ^ ((int)BadgeRole).GetHashCode();
                return hash;
            }
        }
    }

    public class SASDialLogic : WidgetLogic<SASDialState>
    {
        private double _lastRoll = -9999.0;
        private double _lastPitch = -9999.0;
        private double _lastHeading = -9999.0;
        private FlightSASMode _lastMode = (FlightSASMode)(-1);
        private bool _lastSasOn = false;
        private readonly Cached<bool> _hasInitializedState = new Cached<bool>(false);
        private bool _isDirectorLocked = false;
        private Vector3 _currentMarkerDir = Vector3.forward;
        private bool _currentMarkerVisible = false;
        private bool _currentMarkerHasDir = false;
        private float _currentMarkerAngleDeg = 0f;

        public string OffLabel { get; set; } = string.Empty;
        public string BadgePrefix { get; set; } = string.Empty;

        public override void Reset()
        {
            _lastRoll = -9999.0;
            _lastPitch = -9999.0;
            _lastHeading = -9999.0;
            _lastMode = (FlightSASMode)(-1);
            _lastSasOn = false;
            _hasInitializedState.Value = false;
            _isDirectorLocked = false;
            _currentMarkerDir = Vector3.forward;
            _currentMarkerVisible = false;
            _currentMarkerHasDir = false;
            _currentMarkerAngleDeg = 0f;
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                string offText = !string.IsNullOrEmpty(OffLabel) ? OffLabel : I18n.Tr("SAS_STATUS_OFF", "SAS: OFF");
                CurrentState = new SASDialState(
                    hasVessel: false,
                    sasOn: false,
                    currentMode: FlightSASMode.StabilityAssist,
                    roll: 0.0,
                    pitch: 0.0,
                    heading: 0.0,
                    attDirty: false,
                    modeChanged: false,
                    targetRotZ: 0f,
                    directorState: DirectorVisualState.Inactive,
                    isDirectorLocked: false,
                    targetDirectorNormPos: Vector2.zero,
                    targetDirectorRotZ: 0f,
                    statusText: offText,
                    badgeRole: TextStyleRole.Warning
                );
                return;
            }

            bool sasOn = telemetry.IsSASEnabled;
            FlightSASMode currentMode = telemetry.CurrentSASMode;
            bool modeChanged = !_hasInitializedState.Value || currentMode != _lastMode || sasOn != _lastSasOn;
            bool attDirty = Math.Abs(telemetry.Roll - _lastRoll) > 0.05 ||
                            Math.Abs(telemetry.Pitch - _lastPitch) > 0.05 ||
                            Math.Abs(telemetry.Heading - _lastHeading) > 0.05;

            // 1. 目标航向导引计算 (纯几何与向量判定)
            ComputeSASFlightDirectorData(telemetry, sasOn, currentMode, attDirty, modeChanged,
                out var directorState, out bool isDirectorLocked, out var targetDirectorNormPos, out float targetDirectorRotZ);

            // 2. 飞船剪影目标旋转角计算 (纯数学)
            float targetRotZ = ComputeSilhouetteTargetRotZ(telemetry, sasOn, currentMode, isDirectorLocked);

            // 3. 底部状态指示胶囊文本与样式角色 (纯文本格式化)
            ComputeStatusBadgeData(currentMode, sasOn, isDirectorLocked, out string statusText, out var badgeRole);

            _lastRoll = telemetry.Roll;
            _lastPitch = telemetry.Pitch;
            _lastHeading = telemetry.Heading;
            _lastMode = currentMode;
            _lastSasOn = sasOn;
            _hasInitializedState.Value = true;

            CurrentState = new SASDialState(
                hasVessel: true,
                sasOn: sasOn,
                currentMode: currentMode,
                roll: telemetry.Roll,
                pitch: telemetry.Pitch,
                heading: telemetry.Heading,
                attDirty: attDirty,
                modeChanged: modeChanged,
                targetRotZ: targetRotZ,
                directorState: directorState,
                isDirectorLocked: isDirectorLocked,
                targetDirectorNormPos: targetDirectorNormPos,
                targetDirectorRotZ: targetDirectorRotZ,
                statusText: statusText,
                badgeRole: badgeRole
            );
        }

        private float ComputeSilhouetteTargetRotZ(IFlightTelemetry telemetry, bool sasOn, FlightSASMode mode, bool isDirectorLocked)
        {
            float targetRotZ;
            if (!sasOn || mode == FlightSASMode.StabilityAssist)
            {
                targetRotZ = (float)-telemetry.Roll;
            }
            else
            {
                float baseRotZ = GetSASModeDialAngle(mode) - 90f;
                if (isDirectorLocked || !_currentMarkerHasDir || !_currentMarkerVisible)
                {
                    targetRotZ = baseRotZ;
                }
                else
                {
                    float projLen = Mathf.Sqrt(_currentMarkerDir.x * _currentMarkerDir.x + _currentMarkerDir.y * _currentMarkerDir.y);
                    float beta;
                    if (_currentMarkerDir.z > 0.05f && projLen > 0.001f)
                    {
                        beta = Mathf.Atan2(_currentMarkerDir.x, _currentMarkerDir.y) * Mathf.Rad2Deg;
                        beta = Mathf.Clamp(beta, -60f, 60f);
                    }
                    else
                    {
                        beta = 0f;
                    }
                    targetRotZ = baseRotZ + beta;
                }
            }
            return targetRotZ;
        }

        private void ComputeSASFlightDirectorData(IFlightTelemetry telemetry, bool sasOn, FlightSASMode mode, bool attDirty, bool modeChanged,
            out DirectorVisualState visualState, out bool isLocked, out Vector2 targetNormPos, out float targetRotZ)
        {
            if (!sasOn)
            {
                isLocked = false;
                _isDirectorLocked = false;
                _currentMarkerHasDir = false;
                _currentMarkerVisible = false;
                _currentMarkerAngleDeg = 0f;
                visualState = DirectorVisualState.Inactive;
                targetNormPos = Vector2.zero;
                targetRotZ = 0f;
                return;
            }

            if (mode == FlightSASMode.StabilityAssist)
            {
                isLocked = true;
                _isDirectorLocked = true;
                _currentMarkerHasDir = true;
                _currentMarkerVisible = false;
                _currentMarkerAngleDeg = 0f;
                visualState = DirectorVisualState.Inactive;
                targetNormPos = Vector2.zero;
                targetRotZ = 0f;
                return;
            }

            string markerKey = GetMarkerKeyForSASMode(mode);
            Vector3 dir = Vector3.forward;
            bool isVisible = false;
            bool hasDir = false;

            var hook = NavBallHookService.Provider;
            if (hook != null && !string.IsNullOrEmpty(markerKey))
            {
                hasDir = hook.GetMarkerDirection(markerKey, out dir, out isVisible);
            }
            if (!hasDir && NavBallHookService.MarkerDirectionFallback != null && !string.IsNullOrEmpty(markerKey))
            {
                hasDir = NavBallHookService.MarkerDirectionFallback(markerKey, out dir, out isVisible);
            }

            _currentMarkerDir = dir;
            _currentMarkerHasDir = hasDir;
            _currentMarkerVisible = isVisible;

            if (hasDir && isVisible)
            {
                Vector2 screenDir = new Vector2(dir.x, dir.y);
                float screenDist = screenDir.magnitude;
                float angleDeg = Mathf.Atan2(screenDist, Mathf.Max(0.001f, dir.z)) * Mathf.Rad2Deg;
                if (dir.z < 0f)
                {
                    angleDeg = 180f - angleDeg;
                }
                _currentMarkerAngleDeg = angleDeg;

                if (_isDirectorLocked)
                {
                    if (angleDeg > 3.5f) _isDirectorLocked = false;
                }
                else
                {
                    if (angleDeg <= 1.5f) _isDirectorLocked = true;
                }

                isLocked = _isDirectorLocked;

                if (_isDirectorLocked)
                {
                    targetNormPos = Vector2.zero;
                    targetRotZ = 0f;
                    visualState = DirectorVisualState.Locked;
                }
                else
                {
                    float normDist = Mathf.Clamp01(angleDeg / 45f);
                    targetNormPos = screenDist > 0.001f ? (screenDir / screenDist) * normDist : Vector2.zero;
                    targetRotZ = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg - 90f;
                    visualState = DirectorVisualState.Guiding;
                }
            }
            else
            {
                _isDirectorLocked = false;
                isLocked = false;
                _currentMarkerAngleDeg = 0f;
                targetNormPos = Vector2.zero;
                targetRotZ = 0f;
                visualState = DirectorVisualState.Dim;
            }
        }

        private void ComputeStatusBadgeData(FlightSASMode currentMode, bool sasOn, bool isLocked, out string statusText, out TextStyleRole textRole)
        {
            if (!sasOn)
            {
                statusText = !string.IsNullOrEmpty(OffLabel) ? OffLabel : I18n.Tr("SAS_STATUS_OFF", "SAS: OFF");
                textRole = TextStyleRole.Warning;
            }
            else
            {
                string prefix = !string.IsNullOrEmpty(BadgePrefix) ? BadgePrefix : "SAS: ";
                string modeStr = GetSASModeDisplayName(currentMode);
                string lockSuffix = isLocked ? I18n.Tr("SAS_STATUS_LOCK", " [LOCK]") : string.Empty;
                statusText = $"{prefix}{modeStr}{lockSuffix}";
                textRole = isLocked ? TextStyleRole.Accent : TextStyleRole.PrimaryValue;
            }
        }

        public static float GetSASModeDialAngle(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return 90f;
                case FlightSASMode.Prograde: return 45f;
                case FlightSASMode.Retrograde: return 135f;
                case FlightSASMode.Normal: return 0f;
                case FlightSASMode.Antinormal: return 180f;
                case FlightSASMode.RadialIn: return 315f;
                case FlightSASMode.RadialOut: return 225f;
                case FlightSASMode.Maneuver: return 285f;
                case FlightSASMode.Target: return 255f;
                default: return 90f;
            }
        }

        public static string GetMarkerKeyForSASMode(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.Prograde: return "prograde";
                case FlightSASMode.Retrograde: return "retrograde";
                case FlightSASMode.Normal: return "normal";
                case FlightSASMode.Antinormal: return "antinormal";
                case FlightSASMode.RadialIn: return "radialin";
                case FlightSASMode.RadialOut: return "radialout";
                case FlightSASMode.Target: return "target";
                case FlightSASMode.Maneuver: return "maneuver";
                default: return null;
            }
        }

        public static string GetSASModeDisplayName(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return I18n.Tr("SAS_MODE_STABILITY", "STABILITY");
                case FlightSASMode.Prograde: return I18n.Tr("SAS_MODE_PROGRADE", "PROGRADE");
                case FlightSASMode.Retrograde: return I18n.Tr("SAS_MODE_RETROGRADE", "RETROGRADE");
                case FlightSASMode.Normal: return I18n.Tr("SAS_MODE_NORMAL", "NORMAL");
                case FlightSASMode.Antinormal: return I18n.Tr("SAS_MODE_ANTINORMAL", "ANTINORMAL");
                case FlightSASMode.RadialIn: return I18n.Tr("SAS_MODE_RADIAL_IN", "RADIAL IN");
                case FlightSASMode.RadialOut: return I18n.Tr("SAS_MODE_RADIAL_OUT", "RADIAL OUT");
                case FlightSASMode.Target: return I18n.Tr("SAS_MODE_TARGET", "TARGET");
                case FlightSASMode.Maneuver: return I18n.Tr("SAS_MODE_MANEUVER", "MANEUVER");
                default: return mode.ToString().ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// 双键交互分发器：左键快速切换 STAB / SAS，右键无缝切换 2D 纯矢量剪影 / 3D 姿态地平视差模式
    /// </summary>
    public class SASDialInteractionRouter : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public Action OnLeftClick;
        public Action OnRightClick;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
            {
                OnLeftClick?.Invoke();
            }
            else if (eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
            {
                OnRightClick?.Invoke();
            }
        }
    }

    [FlightWidget("sas_dial", "sas_compass", "sas", Category = WidgetCategory.Controls, DisplayName = "环形 SAS 模式选择罗盘", Description = "10 向全功能快速 SAS 模式选择罗盘，带飞船实时滚转与级间剪影。", DefaultWidgetId = "core.sas_dial", DefaultX = 0f, DefaultY = -100f, IsSingleton = true, ExactIds = new[] { "core.sas_dial" })]
    public class SASDialWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(96f, 116f);
        protected override bool AutoCreateCardFrame => false;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Slow;
        public override WidgetRefreshTier HeartBeatTier => WidgetRefreshTier.Relaxed;

        // 声明式微控件
        public TextWidget StatusBadge = TextWidget.Badge("SAS: OFF");

        private class SASButtonUI
        {
            public FlightSASMode Mode;
            public Button Button;
            public Image Image;
            public Outline Outline;
            public Text Label;
            public float Angle;
        }

        private readonly List<SASButtonUI> _buttons = new List<SASButtonUI>();
        private GameObject _attitudeAssemblyRoot;
        private GameObject _shipSilhouette;
        private RectTransform _horizonRoot;
        private RawImage _horizonRawImage;
        private RectTransform _sasDirectorRoot;
        private RawImage _sasDirectorRawImage;
        private RawImage _dialBgRawImage;
        private RawImage _silhouetteRawImage;
        private RawImage _noseTipRawImage;
        private Text _statusLabel;
        private Outline _statusOutline;
        private Image _statusBg;

        private static Texture2D _circularDialTexture;
        private static Texture2D _fallbackRocketTexture;
        private static Texture2D _nosePointerTexture;
        private static Texture2D _attitudeHorizonTexture;
        private static Texture2D _flightDirectorTexture;

        private readonly Cached<SASDialDisplayMode> _displayMode = new Cached<SASDialDisplayMode>(SASDialDisplayMode.Mode2D);
        private readonly Cached<bool> _is3DMode = new Cached<bool>(false);
        private readonly SASDialLogic _logic = new SASDialLogic();
        protected override IWidgetLogic LogicCore => _logic;
        private readonly Cached<FlightSASMode> _lastRenderedMode = new Cached<FlightSASMode>((FlightSASMode)(-1));
        private readonly Cached<bool> _lastRenderedSasOn = new Cached<bool>(false);
        private readonly Cached<string> _lastStatusText = new Cached<string>(null);
        private readonly Cached<bool> _hasInitializedState = new Cached<bool>(false);

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            float dialRadius = 48f * s; // 半径 48px, 直径 96px, 容纳于双带 188px 间隙内并契合 -215 视口安全区
            float dialDiameter = dialRadius * 2f;
            RectTransform.sizeDelta = new Vector2(dialDiameter, dialDiameter + 20f * s);

            // 模式判定：默认纯 2D 矢量剪影；若配置 MODE=3D 或组件 ID 为 core.sas_dial_3d，则激活 3D 姿态视差
            string modeStr = GetTemplateChannel("MODE", "2D");
            if (modeStr.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
                (config != null && config.WidgetId.Equals("core.sas_dial_3d", StringComparison.OrdinalIgnoreCase)))
            {
                _displayMode.Value = SASDialDisplayMode.Mode3D;
            }
            else
            {
                _displayMode.Value = SASDialDisplayMode.Mode2D;
            }

            _logic.OffLabel = GetTemplateChannel("OFF_LABEL", I18n.Tr("SAS_STATUS_OFF", "SAS: OFF"));
            _logic.BadgePrefix = GetTemplateChannel("BADGE_PREFIX", "SAS: ");

            if (_fallbackRocketTexture == null)
            {
                _fallbackRocketTexture = SASDialVisualGenerator.GetOrCreateSpacecraftTexture();
            }

            // 1. 纯圆形激光蚀刻底盘
            CreateCircularBackplate(dialDiameter, s, theme);

            // 2. 中央姿态汇聚机构 (2D 矢量剪影 / 3D 姿态地平双模态)
            Create3DAttitudeAssembly(dialRadius, s, theme);

            // 3. 径向 9 大 SAS 方位模式按钮 (对称几何辐射，绝无重叠)
            CreateSASModeButtons(dialRadius, s, theme);

            // 4. 底部微型模式状态标牌
            CreateStatusBadge(dialRadius, s, theme);

            // 标准化组件内部控件注册至管理器
            if (_dialBgRawImage != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "dial_backplate", "圆环底盘背板", _dialBgRawImage.gameObject);
            }
            if (_attitudeAssemblyRoot != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "ship_silhouette", "飞船姿态剪影视窗", _attitudeAssemblyRoot);
            }
            if (_statusLabel != null && _statusLabel.transform.parent != null)
            {
                ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "status_badge", "SAS 模式状态胶囊", _statusLabel.transform.parent.gameObject);
            }

            ApplyDisplayMode();
            ApplyTheme(theme);
        }

        private void CreateCircularBackplate(float diameter, float s, ThemeConfig theme)
        {
            if (_circularDialTexture == null)
            {
                _circularDialTexture = SASDialVisualGenerator.GetOrCreateDialTexture(theme);
            }

            _dialBgRawImage = CreateChild<RawImage>("Dial_Circular_Backplate", transform,
                new Vector2(diameter, diameter), Vector2.zero);
            _dialBgRawImage.texture = _circularDialTexture;
            _dialBgRawImage.color = WidgetStyleManager.NeutralOpaque;
            _dialBgRawImage.raycastTarget = false;
        }

        private void Create3DAttitudeAssembly(float dialRadius, float s, ThemeConfig theme)
        {
            // 根节点：姿态汇聚容器 (居中挂载)
            RectTransform rootRt = CreateContainer("Attitude_Assembly_Root", transform,
                new Vector2(dialRadius * 2f, dialRadius * 2f), Vector2.zero);
            _attitudeAssemblyRoot = rootRt.gameObject;

            // 1. 动态 3D 人造地平仪与俯仰阶梯层 (Attitude Horizon & Pitch Ladder)
            _horizonRawImage = CreateChild<RawImage>("Attitude_Horizon_Root", _attitudeAssemblyRoot.transform,
                new Vector2(74f * s, 74f * s), Vector2.zero);
            _horizonRoot = _horizonRawImage.rectTransform;
            _horizonRawImage.raycastTarget = false;
            if (_attitudeHorizonTexture == null)
            {
                _attitudeHorizonTexture = SASDialVisualGenerator.GetOrCreateAttitudeHorizonTexture();
            }
            _horizonRawImage.texture = _attitudeHorizonTexture;
            _horizonRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.70f);

            // 2. 中央飞船 3D/2D 姿态云台机构 (Spacecraft Attitude Gimbal)
            float shipSize = 48f * s;
            Button shipBtn = CreateButton("Ship_Silhouette_Root", _attitudeAssemblyRoot.transform,
                out RectTransform sRt, out Image clickTarget, new Vector2(shipSize, shipSize), Vector2.zero);
            _shipSilhouette = sRt.gameObject;

            // 为中央剪影根节点挂载透明点击响应层，确保鼠标交互稳定触发 STAB 切换
            clickTarget.color = Color.clear;
            clickTarget.raycastTarget = true;

            // 挂载双键交互分发器：左键切换 STAB，右键无缝切换 2D 纯矢量剪影 / 3D 姿态地平视差模式 (保护 2D 效果绝不覆盖)
            var sRouter = _shipSilhouette.AddComponent<SASDialInteractionRouter>();
            sRouter.OnLeftClick = () =>
            {
                if (FlightTelemetryContext.Current != null)
                {
                    if (!FlightTelemetryContext.Current.IsSASEnabled)
                    {
                        FlightTelemetryContext.Current.ToggleSAS();
                        FlightTelemetryContext.Current.SetSASMode(FlightSASMode.StabilityAssist);
                    }
                    else if (FlightTelemetryContext.Current.CurrentSASMode == FlightSASMode.StabilityAssist)
                    {
                        FlightTelemetryContext.Current.ToggleSAS();
                    }
                    else
                    {
                        FlightTelemetryContext.Current.SetSASMode(FlightSASMode.StabilityAssist);
                    }
                }
            };
            sRouter.OnRightClick = () => ToggleDisplayMode();

            // 飞船高清图形层 (优先 3D 离屏纹理，保底 2D 剪影与程序化矢量)
            _silhouetteRawImage = CreateChild<RawImage>("Silhouette_Graphic", _shipSilhouette.transform,
                new Vector2(shipSize, shipSize), Vector2.zero);
            _silhouetteRawImage.raycastTarget = false;
            _silhouetteRawImage.color = theme.AccentSecondary;

            UpdateActiveTexture();

            if (Vessel3DService.Provider != null)
            {
                Vessel3DService.Provider.OnTexture3DUpdated += OnTexture3DUpdated;
            }
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 机头朝向前缘高精矢量前视光标 (Precision Boresight Chevron Pointer)
            float tipSize = 8f * s;
            Vector2 tipPos = new Vector2(0f, 21.5f * s);
            _noseTipRawImage = CreateChild<RawImage>("Nose_Tip", _shipSilhouette.transform,
                new Vector2(tipSize, tipSize), tipPos);
            _noseTipRawImage.raycastTarget = false;
            if (_nosePointerTexture == null)
            {
                _nosePointerTexture = SASDialVisualGenerator.GetOrCreateBoresightPointerTexture();
            }
            _noseTipRawImage.texture = _nosePointerTexture;
            _noseTipRawImage.color = theme.WarningColor;

            // 3. 3D SAS 目标航向指引十字/倒V光标 (SAS Target Flight Director)
            _sasDirectorRawImage = CreateChild<RawImage>("SAS_FlightDirector", _attitudeAssemblyRoot.transform,
                new Vector2(14f * s, 14f * s), Vector2.zero);
            _sasDirectorRoot = _sasDirectorRawImage.rectTransform;
            _sasDirectorRawImage.raycastTarget = false;
            if (_flightDirectorTexture == null)
            {
                _flightDirectorTexture = SASDialVisualGenerator.GetOrCreateFlightDirectorChevronTexture();
            }
            _sasDirectorRawImage.texture = _flightDirectorTexture;
            _sasDirectorRawImage.color = theme.AccentPrimary;
        }

        public void ToggleDisplayMode()
        {
            _displayMode.Value = (_displayMode.Value == SASDialDisplayMode.Mode2D) ? SASDialDisplayMode.Mode3D : SASDialDisplayMode.Mode2D;
            ApplyDisplayMode();
            _logic.Reset();
            _hasInitializedState.Value = false;
            _lastStatusText.Reset(null);
            _lastRenderedMode.Reset((FlightSASMode)(-1));
            _lastRenderedSasOn.Reset(false);
        }

        private void ApplyDisplayMode()
        {
            bool is3D = (_displayMode.Value == SASDialDisplayMode.Mode3D);
            if (_horizonRoot != null)
            {
                _horizonRoot.gameObject.SetActive(is3D);
            }
            if (_sasDirectorRoot != null)
            {
                _sasDirectorRoot.gameObject.SetActive(FlightTelemetryContext.Current?.IsSASEnabled ?? false);
            }
            if (_shipSilhouette != null && !is3D)
            {
                _shipSilhouette.transform.localScale = Vector3.one;
            }
            UpdateActiveTexture();
        }

        private void UpdateActiveTexture()
        {
            if (_silhouetteRawImage == null) return;
            if (_displayMode.Value == SASDialDisplayMode.Mode3D && Vessel3DService.Provider?.Texture3D != null)
            {
                _silhouetteRawImage.texture = Vessel3DService.Provider.Texture3D;
                _is3DMode.Value = true;
            }
            else
            {
                Texture tex2D = VesselSilhouetteService.Provider?.SilhouetteTexture;
                if (tex2D == null)
                {
                    tex2D = _fallbackRocketTexture;
                }
                _silhouetteRawImage.texture = tex2D;
                _is3DMode.Value = false;
            }
        }

        private void OnTexture3DUpdated(Texture rt)
        {
            if (_silhouetteRawImage != null && rt != null && _displayMode.Value == SASDialDisplayMode.Mode3D)
            {
                _silhouetteRawImage.texture = rt;
                _is3DMode.Value = true;
            }
        }

        private void OnSilhouetteUpdated(Texture rt)
        {
            if (_silhouetteRawImage != null && rt != null && (_displayMode.Value == SASDialDisplayMode.Mode2D || !_is3DMode.Value))
            {
                _silhouetteRawImage.texture = rt;
            }
        }

        private void CreateSASModeButtons(float dialRadius, float s, ThemeConfig theme)
        {
            _buttons.Clear();

            // 严格对称的空间动力学排布：
            // 顶端顶点：STAB (90°)
            // 右上半部：PRO (45°), NRM (0°)
            // 右下半部：R-IN (315°), MAN (285°)
            // 左上半部：RET (135°), ANT (180°)
            // 左下半部：R-OUT (225°), TGT (255°)
            // 此排布关于 Y 轴 100% 绝对镜像对称，任意相邻按钮角间隙 >= 30°，零交叉重叠！
            var modes = new (FlightSASMode mode, string icon, float angle)[]
            {
                (FlightSASMode.StabilityAssist, "STAB", 90f),
                (FlightSASMode.Prograde, "PRO", 45f),
                (FlightSASMode.Retrograde, "RET", 135f),
                (FlightSASMode.Normal, "NRM", 0f),
                (FlightSASMode.Antinormal, "ANT", 180f),
                (FlightSASMode.RadialIn, "R-IN", 315f),
                (FlightSASMode.RadialOut, "R-OUT", 225f),
                (FlightSASMode.Maneuver, "MAN", 285f),
                (FlightSASMode.Target, "TGT", 255f)
            };

            float ringRadius = 39f * s;
            Vector2 btnSize = new Vector2(19f * s, 13f * s);

            foreach (var m in modes)
            {
                float rad = m.angle * Mathf.Deg2Rad;
                Vector2 btnPos = new Vector2(Mathf.Cos(rad) * ringRadius, Mathf.Sin(rad) * ringRadius);

                Button btn = UIFactory.CreateButton(transform, $"SAS_{m.mode}", btnSize, btnPos, () => OnSASButtonClicked(m.mode));
                Image img = btn.GetComponent<Image>();
                img.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);

                Outline ol = btn.gameObject.AddComponent<Outline>();
                Color borderCol = theme.FrameBorderColor;
                ol.effectColor = WidgetStyleManager.Weighted(borderCol, LineWeight.Normal);
                ol.effectDistance = new Vector2(1f * s, 1f * s);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * s));
                Text lbl = UIFactory.CreateText(btn.transform, "Label", m.icon, fontSize, TextAnchor.MiddleCenter,
                    theme.TextPrimaryColor);
                RectTransform lblRt = lbl.GetComponent<RectTransform>();
                lblRt.sizeDelta = btnSize;
                lblRt.anchoredPosition = Vector2.zero;

                _buttons.Add(new SASButtonUI
                {
                    Mode = m.mode,
                    Button = btn,
                    Image = img,
                    Outline = ol,
                    Label = lbl,
                    Angle = m.angle
                });
            }
        }

        private void CreateStatusBadge(float dialRadius, float s, ThemeConfig theme)
        {
            Vector2 tagSize = new Vector2(78f * s, 14f * s);
            Vector2 pos = new Vector2(0f, -dialRadius - 8f * s);

            GameObject tagBox = UIFactory.CreatePanel(transform, "SAS_Mode_Badge", tagSize, pos,
                theme.FrameBgColor);
            _statusBg = tagBox.GetComponent<Image>();

            // 底部标牌支持双键：左键切换 SAS 开关，右键切换 2D / 3D 模式
            Button badgeBtn = tagBox.GetComponent<Button>() ?? tagBox.AddComponent<Button>();
            badgeBtn.transition = Selectable.Transition.None;
            var badgeRouter = tagBox.AddComponent<SASDialInteractionRouter>();
            badgeRouter.OnLeftClick = () => FlightTelemetryContext.Current?.ToggleSAS();
            badgeRouter.OnRightClick = () => ToggleDisplayMode();

            _statusOutline = tagBox.AddComponent<Outline>();
            Color border = theme.FrameBorderColor;
            _statusOutline.effectColor = WidgetStyleManager.Weighted(border, LineWeight.Strong);
            _statusOutline.effectDistance = new Vector2(1f * s, 1f * s);

            int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * s));
            _statusLabel = UIFactory.CreateText(tagBox.transform, "Text", "SAS: " + I18n.Tr("SAS_MODE_STABILITY", "稳定"), fontSize, TextAnchor.MiddleCenter,
                theme.AccentSecondary);
            RectTransform trt = _statusLabel.GetComponent<RectTransform>();
            trt.sizeDelta = tagSize;
            trt.anchoredPosition = Vector2.zero;
        }

        private void OnSASButtonClicked(FlightSASMode mode)
        {
            FlightTelemetryContext.Current?.SetSASMode(mode);
        }

        // ── 平滑阻尼状态 (消除跳变) ──
        private struct SASDampingSnapshot
        {
            public float SmoothedRotZ;
            public float RotZVelocity;
            public Vector2 SmoothedDirectorPos;
            public Vector2 DirectorPosVelocity;
            public float SmoothedDirectorRotZ;
            public float DirectorRotZVelocity;
        }
        private SASDampingSnapshot _dampingSnapshot;
        private const float kSilhouetteSmoothTime = 0.12f; // 剪影旋转平滑时间常数
        private const float kDirectorSmoothTime = 0.08f;   // 导引标平滑时间常数

        public override void OnDataHeartBeat(in FlightHeartbeatContext context) => base.OnDataHeartBeat(in context);

        public override void OnUIDrawLoop(ref FlightUIDrawContext context) => base.OnUIDrawLoop(ref context);

        protected override void OnRenderState()
        {
            SASDialState state = _logic.CurrentState;
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme;
            float s = CurrentDpiScale;
            float dt = Time.unscaledDeltaTime;

            if (!state.HasVessel)
            {
                if (_sasDirectorRoot != null && _sasDirectorRoot.gameObject.activeSelf)
                    _sasDirectorRoot.gameObject.SetActive(false);
                if (_horizonRoot != null && _horizonRoot.gameObject.activeSelf)
                    _horizonRoot.gameObject.SetActive(false);
                RenderStatusBadge(state.StatusText, state.BadgeRole, theme);
                return;
            }

            // 1. 动态 3D 人造地平仪与俯仰阶梯解算 (仅在 3D 模式下激活)
            if (_displayMode.Value == SASDialDisplayMode.Mode3D)
            {
                if (_horizonRoot != null)
                {
                    _horizonRoot.SetActiveSafe(true);
                    if (state.AttDirty)
                    {
                        float pitchOffset = Mathf.Clamp((float)-state.Pitch * 0.16f * s, -16f * s, 16f * s);
                        _horizonRoot.SetAnchoredPositionSafe(new Vector2(0f, pitchOffset));
                        _horizonRoot.SetLocalRotationSafe(Quaternion.Euler(0f, 0f, (float)state.Roll));
                    }
                }
            }
            else
            {
                if (_horizonRoot != null && _horizonRoot.gameObject.activeSelf)
                {
                    _horizonRoot.SetActiveSafe(false);
                }
            }

            // 2. 导引标平滑位移与旋转驱动
            Vector2 targetDirectorPos = state.TargetDirectorNormPos * (22f * s);
            RenderDirectorVisuals(state.DirectorState, targetDirectorPos, state.TargetDirectorRotZ, dt, theme);

            // 3. 飞船剪影旋转与 3D 俯仰透视收缩 (平滑阻尼)
            RenderSilhouetteVisuals(state.TargetRotZ, (float)state.Pitch, state.AttDirty, state.ModeChanged, dt);

            // 4. 纹理保底检查
            if (_silhouetteRawImage != null && _silhouetteRawImage.texture == null)
            {
                UpdateActiveTexture();
            }

            // 5. 机头朝向标动态微调
            RenderNoseTipVisuals(s);

            // 6. 当前 SAS 模式与开关高亮指示 (Dirty Checking + 100% 语义化驱动)
            bool modeDirty = _lastRenderedMode.Update(state.CurrentMode);
            bool sasDirty = _lastRenderedSasOn.Update(state.SasOn);
            if (!_hasInitializedState.Value || modeDirty || sasDirty)
            {
                for (int i = 0; i < _buttons.Count; i++)
                {
                    var b = _buttons[i];
                    bool isCurrent = (b.Mode == state.CurrentMode) && state.SasOn;
                    ButtonVisualRole role = isCurrent ? ButtonVisualRole.ActiveToggle : ButtonVisualRole.Normal;
                    ApplyButton(b.Button, b.Image, b.Label, role, isCurrent, theme);
                }

                _hasInitializedState.Value = true;
            }

            // 7. 底部状态指示胶囊文本与描边
            RenderStatusBadge(state.StatusText, state.BadgeRole, theme);
        }

        private void RenderSilhouetteVisuals(float targetRotZ, float pitch, bool attDirty, bool modeChanged, float dt)
        {
            if (_shipSilhouette == null) return;

            if (attDirty || modeChanged || Mathf.Abs(_dampingSnapshot.RotZVelocity) > 0.001f || Mathf.Abs(Mathf.DeltaAngle(_dampingSnapshot.SmoothedRotZ, targetRotZ)) > 0.02f)
            {
                _dampingSnapshot.SmoothedRotZ = Mathf.SmoothDampAngle(_dampingSnapshot.SmoothedRotZ, targetRotZ, ref _dampingSnapshot.RotZVelocity, kSilhouetteSmoothTime, Mathf.Infinity, dt);
                _shipSilhouette.transform.SetLocalRotationSafe(Quaternion.Euler(0f, 0f, _dampingSnapshot.SmoothedRotZ), 0.05f);

                if (_displayMode.Value == SASDialDisplayMode.Mode3D)
                {
                    float pitchRad = pitch * Mathf.Deg2Rad;
                    float foreshortenY = Mathf.Clamp(Mathf.Cos(pitchRad * 0.6f), 0.76f, 1.0f);
                    _shipSilhouette.transform.SetLocalScaleSafe(new Vector3(1.0f, foreshortenY, 1.0f), 0.005f);
                }
                else
                {
                    _shipSilhouette.transform.SetLocalScaleSafe(Vector3.one, 0.005f);
                }
            }
        }

        private void RenderDirectorVisuals(DirectorVisualState visualState, Vector2 targetPos, float targetRotZ, float dt, ThemeConfig theme)
        {
            if (_sasDirectorRoot == null || _sasDirectorRawImage == null) return;

            if (visualState == DirectorVisualState.Inactive)
            {
                _sasDirectorRoot.SetActiveSafe(false);
                return;
            }

            _sasDirectorRoot.SetActiveSafe(true);

            Color targetColor;
            if (visualState == DirectorVisualState.Locked)
            {
                targetColor = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.PrimaryValue, theme);
            }
            else if (visualState == DirectorVisualState.Guiding)
            {
                targetColor = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Warning, theme);
            }
            else
            {
                targetColor = WidgetStyleManager.WithAlpha(WidgetStyleManager.Instance.GetTextColor(TextStyleRole.Muted, theme), 0.35f);
            }

            _dampingSnapshot.SmoothedDirectorPos.x = Mathf.SmoothDamp(_dampingSnapshot.SmoothedDirectorPos.x, targetPos.x, ref _dampingSnapshot.DirectorPosVelocity.x, kDirectorSmoothTime, Mathf.Infinity, dt);
            _dampingSnapshot.SmoothedDirectorPos.y = Mathf.SmoothDamp(_dampingSnapshot.SmoothedDirectorPos.y, targetPos.y, ref _dampingSnapshot.DirectorPosVelocity.y, kDirectorSmoothTime, Mathf.Infinity, dt);
            _dampingSnapshot.SmoothedDirectorRotZ = Mathf.SmoothDampAngle(_dampingSnapshot.SmoothedDirectorRotZ, targetRotZ, ref _dampingSnapshot.DirectorRotZVelocity, kDirectorSmoothTime, Mathf.Infinity, dt);

            _sasDirectorRoot.SetAnchoredPositionSafe(_dampingSnapshot.SmoothedDirectorPos, 0.05f);
            _sasDirectorRoot.SetLocalRotationSafe(Quaternion.Euler(0f, 0f, _dampingSnapshot.SmoothedDirectorRotZ), 0.05f);
            Color targetLerped = Color.Lerp(_sasDirectorRawImage.color, targetColor, Mathf.Clamp01(dt / kDirectorSmoothTime));
            _sasDirectorRawImage.SetColor(targetLerped);
        }

        private void RenderNoseTipVisuals(float s)
        {
            if (_noseTipRawImage == null) return;

            bool showTip = (_displayMode.Value == SASDialDisplayMode.Mode3D);
            if (_noseTipRawImage.gameObject.activeSelf != showTip)
            {
                _noseTipRawImage.gameObject.SetActive(showTip);
            }

            if (showTip)
            {
                float tipY = 21.5f * s;
                if (VesselSilhouetteService.Provider != null)
                {
                    float halfSpan = 22f * s;
                    float normY = VesselSilhouetteService.Provider.NormalizedNoseTipY;
                    tipY = Mathf.Clamp(normY * halfSpan, 10f * s, halfSpan + 1f * s);
                }
                _noseTipRawImage.rectTransform.SetAnchoredPositionSafe(new Vector2(0f, tipY));
            }
        }

        private void RenderStatusBadge(string statusText, TextStyleRole textRole, ThemeConfig theme)
        {
            if (_statusLabel == null) return;

            if (_lastStatusText.Update(statusText))
            {
                _statusLabel.SetTextSafe(statusText);
                ApplyText(_statusLabel, textRole, theme);
                if (_statusOutline != null)
                {
                    _statusOutline.SetColor(WidgetStyleManager.Instance.GetTextColor(textRole, theme));
                }
            }
        }

        public static float GetSASModeDialAngle(FlightSASMode mode) => SASDialLogic.GetSASModeDialAngle(mode);

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            _hasInitializedState.Value = false;
            _lastStatusText.Reset(null);
            _lastRenderedMode.Reset((FlightSASMode)(-1));
            _lastRenderedSasOn.Reset(false);

            if (_silhouetteRawImage != null) _silhouetteRawImage.color = theme.AccentSecondary;
            if (_noseTipRawImage != null) _noseTipRawImage.color = theme.WarningColor;
            if (_dialBgRawImage != null)
            {
                _circularDialTexture = SASDialVisualGenerator.GetOrCreateDialTexture(theme);
                _dialBgRawImage.texture = _circularDialTexture;
                _dialBgRawImage.color = WidgetStyleManager.NeutralOpaque;
            }
            if (_horizonRawImage != null)
            {
                _horizonRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.70f);
            }
            if (_sasDirectorRawImage != null)
            {
                _sasDirectorRawImage.color = _logic.CurrentState.IsDirectorLocked ? theme.AccentPrimary : theme.WarningColor;
            }

            ApplyCard(_statusBg, _statusOutline, CardStyleRole.Normal, theme);
            ApplyText(_statusLabel, TextStyleRole.Accent, theme);

            Material btnMat = WidgetStyleManager.Instance.GetUiMaterial(isText: false);
            Material txtMat = WidgetStyleManager.Instance.GetUiMaterial(isText: true);
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Image != null) _buttons[i].Image.material = btnMat;
                if (_buttons[i].Label != null) _buttons[i].Label.material = txtMat;
            }
        }


        protected override void OnLanguageChanged()
        {
            base.OnLanguageChanged();
            _logic.OffLabel = GetTemplateChannel("OFF_LABEL", I18n.Tr("SAS_STATUS_OFF", "SAS: OFF"));
            _logic.BadgePrefix = GetTemplateChannel("BADGE_PREFIX", "SAS: ");
            _logic.Reset();
            _hasInitializedState.Value = false;
            _lastStatusText.Reset(null);
            _lastRenderedMode.Reset((FlightSASMode)(-1));
            _lastRenderedSasOn.Reset(false);
        }

        protected override void OnDestroy()
        {
            if (Vessel3DService.Provider != null)
            {
                Vessel3DService.Provider.OnTexture3DUpdated -= OnTexture3DUpdated;
            }
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Button != null) _buttons[i].Button.onClick.RemoveAllListeners();
            }
            if (_shipSilhouette != null)
            {
                var sBtn = _shipSilhouette.GetComponent<Button>();
                if (sBtn != null) sBtn.onClick.RemoveAllListeners();
            }
            base.OnDestroy();
        }
    }
}
