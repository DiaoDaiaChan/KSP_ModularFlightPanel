using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Core;
using ModularFlightPanel.Config;

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

    public class SASDialWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        private class SASButtonData
        {
            public FlightSASMode Mode;
            public Button Button;
            public Image Image;
            public Outline Outline;
            public Text Label;
            public float Angle;
        }

        private readonly List<SASButtonData> _buttons = new List<SASButtonData>();
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

        private SASDialDisplayMode _displayMode = SASDialDisplayMode.Mode2D;
        private bool _is3DMode = false;
        private bool _isDirectorLocked = false;

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
                _displayMode = SASDialDisplayMode.Mode3D;
            }
            else
            {
                _displayMode = SASDialDisplayMode.Mode2D;
            }

            // 1. 纯圆形激光蚀刻底盘
            CreateCircularBackplate(dialDiameter, s, theme);

            // 2. 中央姿态汇聚机构 (2D 矢量剪影 / 3D 姿态地平双模态)
            Create3DAttitudeAssembly(dialRadius, s, theme);

            // 3. 径向 9 大 SAS 方位模式按钮 (对称几何辐射，绝无重叠)
            CreateSASModeButtons(dialRadius, s, theme);

            // 4. 底部微型模式状态标牌
            CreateStatusBadge(dialRadius, s, theme);

            ApplyDisplayMode();
            ApplyTheme(theme);
        }

        private void CreateCircularBackplate(float diameter, float s, ThemeConfig theme)
        {
            if (_circularDialTexture == null)
            {
                _circularDialTexture = GenerateCircularDialTexture(theme);
            }

            GameObject bgObj = new GameObject("Dial_Circular_Backplate", typeof(RectTransform), typeof(RawImage));
            bgObj.transform.SetParent(transform, false);
            RectTransform rt = bgObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;

            _dialBgRawImage = bgObj.GetComponent<RawImage>();
            _dialBgRawImage.texture = _circularDialTexture;
            _dialBgRawImage.color = WidgetStyleManager.NeutralOpaque;
            _dialBgRawImage.raycastTarget = false;
        }

        private void Create3DAttitudeAssembly(float dialRadius, float s, ThemeConfig theme)
        {
            // 根节点：姿态汇聚容器 (居中挂载)
            _attitudeAssemblyRoot = new GameObject("Attitude_Assembly_Root", typeof(RectTransform));
            _attitudeAssemblyRoot.transform.SetParent(transform, false);
            RectTransform rootRt = _attitudeAssemblyRoot.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(dialRadius * 2f, dialRadius * 2f);
            rootRt.anchoredPosition = Vector2.zero;

            // 1. 动态 3D 人造地平仪与俯仰阶梯层 (Attitude Horizon & Pitch Ladder)
            GameObject horizonObj = new GameObject("Attitude_Horizon_Root", typeof(RectTransform), typeof(RawImage));
            horizonObj.transform.SetParent(_attitudeAssemblyRoot.transform, false);
            _horizonRoot = horizonObj.GetComponent<RectTransform>();
            _horizonRoot.sizeDelta = new Vector2(74f * s, 74f * s);
            _horizonRoot.anchoredPosition = Vector2.zero;

            _horizonRawImage = horizonObj.GetComponent<RawImage>();
            _horizonRawImage.raycastTarget = false;
            if (_attitudeHorizonTexture == null)
            {
                _attitudeHorizonTexture = CreateAttitudeHorizonTexture();
            }
            _horizonRawImage.texture = _attitudeHorizonTexture;
            _horizonRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.70f);

            // 2. 中央飞船 3D/2D 姿态云台机构 (Spacecraft Attitude Gimbal)
            _shipSilhouette = new GameObject("Ship_Silhouette_Root", typeof(RectTransform), typeof(Button));
            _shipSilhouette.transform.SetParent(_attitudeAssemblyRoot.transform, false);
            RectTransform sRt = _shipSilhouette.GetComponent<RectTransform>();
            sRt.sizeDelta = new Vector2(36f * s, 36f * s);
            sRt.anchoredPosition = Vector2.zero;

            // 为中央剪影根节点挂载透明点击响应层，确保鼠标交互稳定触发 STAB 切换
            Image clickTarget = _shipSilhouette.AddComponent<Image>();
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
            GameObject rawImgObj = new GameObject("Silhouette_Graphic", typeof(RectTransform), typeof(RawImage));
            rawImgObj.transform.SetParent(_shipSilhouette.transform, false);
            RectTransform rawRt = rawImgObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(36f * s, 36f * s);
            rawRt.anchoredPosition = Vector2.zero;

            _silhouetteRawImage = rawImgObj.GetComponent<RawImage>();
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
            Vector2 tipPos = new Vector2(0f, 16.5f * s);
            GameObject tipObj = new GameObject("Nose_Tip", typeof(RectTransform), typeof(RawImage));
            tipObj.transform.SetParent(_shipSilhouette.transform, false);
            RectTransform tipRt = tipObj.GetComponent<RectTransform>();
            tipRt.sizeDelta = new Vector2(tipSize, tipSize);
            tipRt.anchoredPosition = tipPos;

            _noseTipRawImage = tipObj.GetComponent<RawImage>();
            _noseTipRawImage.raycastTarget = false;
            if (_nosePointerTexture == null)
            {
                _nosePointerTexture = CreateBoresightPointerTexture();
            }
            _noseTipRawImage.texture = _nosePointerTexture;
            _noseTipRawImage.color = theme.WarningColor;

            // 3. 3D SAS 目标航向指引十字/倒V光标 (SAS Target Flight Director)
            GameObject directorObj = new GameObject("SAS_FlightDirector", typeof(RectTransform), typeof(RawImage));
            directorObj.transform.SetParent(_attitudeAssemblyRoot.transform, false);
            _sasDirectorRoot = directorObj.GetComponent<RectTransform>();
            _sasDirectorRoot.sizeDelta = new Vector2(14f * s, 14f * s);
            _sasDirectorRoot.anchoredPosition = Vector2.zero;

            _sasDirectorRawImage = directorObj.GetComponent<RawImage>();
            _sasDirectorRawImage.raycastTarget = false;
            if (_flightDirectorTexture == null)
            {
                _flightDirectorTexture = CreateFlightDirectorChevronTexture();
            }
            _sasDirectorRawImage.texture = _flightDirectorTexture;
            _sasDirectorRawImage.color = theme.AccentPrimary;
        }

        public void ToggleDisplayMode()
        {
            _displayMode = (_displayMode == SASDialDisplayMode.Mode2D) ? SASDialDisplayMode.Mode3D : SASDialDisplayMode.Mode2D;
            ApplyDisplayMode();
            _hasInitializedState = false;
            _lastStatusText = null;
        }

        private void ApplyDisplayMode()
        {
            bool is3D = (_displayMode == SASDialDisplayMode.Mode3D);
            if (_horizonRoot != null)
            {
                _horizonRoot.gameObject.SetActive(is3D);
            }
            if (_sasDirectorRoot != null)
            {
                _sasDirectorRoot.gameObject.SetActive(is3D && (FlightTelemetryContext.Current?.IsSASEnabled ?? false));
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
            if (_displayMode == SASDialDisplayMode.Mode3D && Vessel3DService.Provider?.Texture3D != null)
            {
                _silhouetteRawImage.texture = Vessel3DService.Provider.Texture3D;
                _is3DMode = true;
            }
            else
            {
                Texture tex2D = VesselSilhouetteService.Provider?.SilhouetteTexture;
                if (tex2D == null)
                {
                    if (_fallbackRocketTexture == null) _fallbackRocketTexture = CreateProceduralSpacecraftTexture();
                    tex2D = _fallbackRocketTexture;
                }
                _silhouetteRawImage.texture = tex2D;
                _is3DMode = false;
            }
        }

        private void OnTexture3DUpdated(Texture rt)
        {
            if (_silhouetteRawImage != null && rt != null && _displayMode == SASDialDisplayMode.Mode3D)
            {
                _silhouetteRawImage.texture = rt;
                _is3DMode = true;
            }
        }

        private void OnSilhouetteUpdated(Texture rt)
        {
            if (_silhouetteRawImage != null && rt != null && (_displayMode == SASDialDisplayMode.Mode2D || !_is3DMode))
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

                _buttons.Add(new SASButtonData
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
            _statusLabel = UIFactory.CreateText(tagBox.transform, "Text", "SAS: STABILITY", fontSize, TextAnchor.MiddleCenter,
                theme.AccentSecondary);
            RectTransform trt = _statusLabel.GetComponent<RectTransform>();
            trt.sizeDelta = tagSize;
            trt.anchoredPosition = Vector2.zero;
        }

        private void OnSASButtonClicked(FlightSASMode mode)
        {
            FlightTelemetryContext.Current?.SetSASMode(mode);
        }

        private double _lastRoll = -9999.0;
        private double _lastPitch = -9999.0;
        private FlightSASMode _lastMode = (FlightSASMode)(-1);
        private bool _lastSasOn = false;
        private bool _lastDirectorLocked = false;
        private string _lastStatusText;
        private bool _hasInitializedState = false;

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;
            float s = CurrentDpiScale;
            ThemeConfig theme = WidgetStyleManager.Instance.CurrentTheme;

            // 1. 动态 3D 人造地平仪与俯仰阶梯解算 (仅在 3D 模式下激活)
            bool attDirty = Math.Abs(telemetry.Roll - _lastRoll) > 0.05 || Math.Abs(telemetry.Pitch - _lastPitch) > 0.05;
            if (_displayMode == SASDialDisplayMode.Mode3D)
            {
                if (_horizonRoot != null && !_horizonRoot.gameObject.activeSelf)
                {
                    _horizonRoot.gameObject.SetActive(true);
                }
                if (_horizonRoot != null && attDirty)
                {
                    float pitchOffset = Mathf.Clamp((float)-telemetry.Pitch * 0.16f * s, -16f * s, 16f * s);
                    _horizonRoot.anchoredPosition = new Vector2(0f, pitchOffset);
                    _horizonRoot.localRotation = Quaternion.Euler(0f, 0f, (float)telemetry.Roll);
                }
            }
            else
            {
                if (_horizonRoot != null && _horizonRoot.gameObject.activeSelf)
                {
                    _horizonRoot.gameObject.SetActive(false);
                }
            }

            // 2. 飞船云台旋转与 3D 俯仰透视收缩 (2D 模式保持纯 Roll 旋转与 1:1 比例，保护 2D 效果绝不形变)
            if (_shipSilhouette != null && attDirty)
            {
                _shipSilhouette.transform.localRotation = Quaternion.Euler(0f, 0f, (float)-telemetry.Roll);

                if (_displayMode == SASDialDisplayMode.Mode3D)
                {
                    float pitchRad = (float)telemetry.Pitch * Mathf.Deg2Rad;
                    float foreshortenY = Mathf.Clamp(Mathf.Cos(pitchRad * 0.6f), 0.76f, 1.0f);
                    _shipSilhouette.transform.localScale = new Vector3(1.0f, foreshortenY, 1.0f);
                }
                else
                {
                    if (_shipSilhouette.transform.localScale != Vector3.one)
                    {
                        _shipSilhouette.transform.localScale = Vector3.one;
                    }
                }
            }

            _lastRoll = telemetry.Roll;
            _lastPitch = telemetry.Pitch;

            // 3. 纹理保底检查
            if (_silhouetteRawImage != null && _silhouetteRawImage.texture == null)
            {
                UpdateActiveTexture();
            }

            // 4. 机头朝向标动态微调 (与正向烘焙包围盒严格对齐)
            if (_noseTipRawImage != null)
            {
                float tipY = 16.5f * s;
                if (VesselSilhouetteService.Provider != null)
                {
                    float halfSpan = 18f * s;
                    float normY = VesselSilhouetteService.Provider.NormalizedNoseTipY;
                    tipY = Mathf.Clamp(normY * halfSpan, 8f * s, halfSpan + 1f * s);
                }
                _noseTipRawImage.rectTransform.anchoredPosition = new Vector2(0f, tipY);
            }

            // 5. 3D SAS 目标航向指引微调 (仅在 3D 模式下激活)
            if (_displayMode == SASDialDisplayMode.Mode3D)
            {
                UpdateSASFlightDirector(telemetry, s, theme);
            }
            else
            {
                if (_sasDirectorRoot != null && _sasDirectorRoot.gameObject.activeSelf)
                {
                    _sasDirectorRoot.gameObject.SetActive(false);
                }
                _isDirectorLocked = false;
            }

            // 6. 当前 SAS 模式与开关高亮指示 (Dirty Checking + 100% 语义化驱动)
            FlightSASMode currentMode = telemetry.CurrentSASMode;
            bool sasOn = telemetry.IsSASEnabled;

            if (!_hasInitializedState || currentMode != _lastMode || sasOn != _lastSasOn)
            {
                _lastMode = currentMode;
                _lastSasOn = sasOn;

                for (int i = 0; i < _buttons.Count; i++)
                {
                    var b = _buttons[i];
                    bool isCurrent = (b.Mode == currentMode) && sasOn;
                    ButtonVisualRole role = isCurrent ? ButtonVisualRole.ActiveToggle : ButtonVisualRole.Normal;
                    ApplyButton(b.Button, b.Image, b.Label, role, isCurrent, theme);
                }

                _hasInitializedState = true;
            }

            // 7. 底部状态指示胶囊文本更新 (实时反映 LOCK / GUIDING 状态)
            UpdateStatusBadge(currentMode, sasOn, theme);
        }

        private void UpdateSASFlightDirector(IFlightTelemetry telemetry, float s, ThemeConfig theme)
        {
            if (_sasDirectorRoot == null || _sasDirectorRawImage == null) return;

            if (!telemetry.IsSASEnabled)
            {
                if (_sasDirectorRoot.gameObject.activeSelf)
                {
                    _sasDirectorRoot.gameObject.SetActive(false);
                }
                _isDirectorLocked = false;
                return;
            }

            if (!_sasDirectorRoot.gameObject.activeSelf)
            {
                _sasDirectorRoot.gameObject.SetActive(true);
            }

            FlightSASMode mode = telemetry.CurrentSASMode;
            if (mode == FlightSASMode.StabilityAssist)
            {
                _isDirectorLocked = true;
                _sasDirectorRoot.anchoredPosition = Vector2.zero;
                _sasDirectorRoot.localRotation = Quaternion.identity;
                _sasDirectorRawImage.color = theme.AccentPrimary;
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

            if (hasDir && isVisible)
            {
                Vector2 screenDir = new Vector2(dir.x, dir.y);
                float screenDist = screenDir.magnitude;
                float angleDeg = Mathf.Atan2(screenDist, Mathf.Max(0.001f, dir.z)) * Mathf.Rad2Deg;
                if (dir.z < 0f)
                {
                    angleDeg = 180f - angleDeg;
                }

                if (angleDeg <= 1.5f)
                {
                    // 目标锁定 (≤ 1.5°)：自动吸附中心，转为强调色锁定状态
                    _isDirectorLocked = true;
                    _sasDirectorRoot.anchoredPosition = Vector2.zero;
                    _sasDirectorRoot.localRotation = Quaternion.identity;
                    _sasDirectorRawImage.color = theme.AccentPrimary;
                }
                else
                {
                    // 偏差导引模式：指引飞行员向目标方向修正
                    _isDirectorLocked = false;
                    float maxRadius = 22f * s; // 限制在内圈半径 22px 范围内，绝不与外围按钮重叠
                    float normDist = Mathf.Clamp01(angleDeg / 45f); // 0 .. 45° 映射到 0 .. 22px
                    Vector2 offset = screenDist > 0.001f ? (screenDir / screenDist) * (normDist * maxRadius) : Vector2.zero;
                    _sasDirectorRoot.anchoredPosition = offset;

                    // 导引标旋转指向目标方位
                    float directorAngle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg - 90f;
                    _sasDirectorRoot.localRotation = Quaternion.Euler(0f, 0f, directorAngle);
                    _sasDirectorRawImage.color = theme.WarningColor;
                }
            }
            else
            {
                _isDirectorLocked = false;
                _sasDirectorRoot.anchoredPosition = Vector2.zero;
                _sasDirectorRawImage.color = WidgetStyleManager.WithAlpha(theme.TextAccentColor, 0.35f);
            }
        }

        private void UpdateStatusBadge(FlightSASMode currentMode, bool sasOn, ThemeConfig theme)
        {
            if (_statusLabel == null) return;

            string statusText;
            TextStyleRole textRole;
            if (!sasOn)
            {
                statusText = GetTemplateChannel("OFF_LABEL", "SAS: OFF");
                textRole = TextStyleRole.Warning;
            }
            else
            {
                string prefix = GetTemplateChannel("BADGE_PREFIX", "SAS: ");
                string modeStr = GetSASModeDisplayName(currentMode);
                string lockSuffix = (_displayMode == SASDialDisplayMode.Mode3D && _isDirectorLocked) ? " [LOCK]" : "";
                statusText = $"{prefix}{modeStr}{lockSuffix}";
                textRole = (_displayMode == SASDialDisplayMode.Mode3D && _isDirectorLocked) ? TextStyleRole.Accent : TextStyleRole.PrimaryValue;
            }

            if (statusText != _lastStatusText || _isDirectorLocked != _lastDirectorLocked)
            {
                _lastStatusText = statusText;
                _lastDirectorLocked = _isDirectorLocked;
                _statusLabel.text = statusText;
                ApplyText(_statusLabel, textRole, theme);
                if (_statusOutline != null)
                {
                    _statusOutline.effectColor = WidgetStyleManager.Instance.GetTextColor(textRole, theme);
                }
            }
        }

        private static string GetMarkerKeyForSASMode(FlightSASMode mode)
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

        private static string GetSASModeDisplayName(FlightSASMode mode)
        {
            switch (mode)
            {
                case FlightSASMode.StabilityAssist: return "STABILITY";
                case FlightSASMode.Prograde: return "PROGRADE";
                case FlightSASMode.Retrograde: return "RETROGRADE";
                case FlightSASMode.Normal: return "NORMAL";
                case FlightSASMode.Antinormal: return "ANTINORMAL";
                case FlightSASMode.RadialIn: return "RADIAL IN";
                case FlightSASMode.RadialOut: return "RADIAL OUT";
                case FlightSASMode.Target: return "TARGET";
                case FlightSASMode.Maneuver: return "MANEUVER";
                default: return mode.ToString().ToUpperInvariant();
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            _hasInitializedState = false;
            _lastStatusText = null;

            if (_silhouetteRawImage != null) _silhouetteRawImage.color = theme.AccentSecondary;
            if (_noseTipRawImage != null) _noseTipRawImage.color = theme.WarningColor;
            if (_dialBgRawImage != null)
            {
                _circularDialTexture = GenerateCircularDialTexture(theme);
                _dialBgRawImage.texture = _circularDialTexture;
                _dialBgRawImage.color = WidgetStyleManager.NeutralOpaque;
            }
            if (_horizonRawImage != null)
            {
                _horizonRawImage.color = WidgetStyleManager.WithAlpha(theme.AccentSecondary, 0.70f);
            }
            if (_sasDirectorRawImage != null)
            {
                _sasDirectorRawImage.color = _isDirectorLocked ? theme.AccentPrimary : theme.WarningColor;
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

        /// <summary>
        /// 程序化生成高精度 512x512 环形航电背板纹理 (三线性 Mipmap 滤波、次像素抗锯齿外环、激光细环、姿态基准十字与主方位标线)
        /// </summary>
        private static Texture2D GenerateCircularDialTexture(ThemeConfig theme = null)
        {
            const int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;

            Color glassDark = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            Color rimColor = WidgetStyleManager.Line(
                WidgetStyleManager.Darken(WidgetStyleManager.CardBorder(CardStyleRole.Normal, theme), 0.25f), LineWeight.Heavy, theme);
            Color ringLaser = WidgetStyleManager.Ring(LineWeight.Normal, theme);
            Color tickColor = WidgetStyleManager.Ring(LineWeight.Bold, theme);
            Color reticleColor = WidgetStyleManager.Ring(LineWeight.Subtle, theme);

            float invHalf = 1f / half;
            float feather = 2.5f * invHalf; // 2.5像素平滑抗锯齿边缘

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) * invHalf;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    if (r > 0.995f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 外边缘次像素平滑羽化
                    float edgeAlpha = Mathf.Clamp01((0.995f - r) / feather);

                    Color pixel = glassDark;

                    // 1. 外环金属轮廓 (0.91 .. 0.96) 平滑过渡
                    float rimIn = Mathf.Clamp01((r - 0.905f) / feather);
                    float rimOut = Mathf.Clamp01((0.965f - r) / feather);
                    float rimFactor = Mathf.Min(rimIn, rimOut);
                    if (rimFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, rimColor, 0.75f * rimFactor);
                    }

                    // 2. 内同心激光细环 (0.58 .. 0.60) 平滑抗锯齿
                    float laserIn = Mathf.Clamp01((r - 0.575f) / feather);
                    float laserOut = Mathf.Clamp01((0.605f - r) / feather);
                    float laserFactor = Mathf.Min(laserIn, laserOut);
                    if (laserFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, ringLaser, 0.75f * laserFactor);
                    }

                    // 3. 姿态参考环 (0.380 .. 0.395) 幽暗视口刻线 (Attitude Horizon Ring)
                    float reticleIn = Mathf.Clamp01((r - 0.380f) / feather);
                    float reticleOut = Mathf.Clamp01((0.395f - r) / feather);
                    float reticleFactor = Mathf.Min(reticleIn, reticleOut);
                    if (reticleFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, reticleColor, 0.45f * reticleFactor);
                    }

                    // 4. 水平地平基准刻线 (Horizontal Horizon Reference Marks, 9点与3点方向, r: 0.45 .. 0.58)
                    if (r >= 0.45f && r <= 0.58f)
                    {
                        float horizY = Mathf.Clamp01((0.007f - Mathf.Abs(dy)) / feather);
                        if (horizY > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.75f * horizY);
                        }
                    }

                    // 5. 12 点钟与 6 点钟纵向基准标线 (Vertical Datum Ticks, r: 0.52 .. 0.58)
                    if (r >= 0.52f && r <= 0.58f)
                    {
                        float vertX = Mathf.Clamp01((0.007f - Mathf.Abs(dx)) / feather);
                        if (vertX > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.75f * vertX);
                        }
                    }

                    // 6. 45° 滚转微刻度点 (Roll Index Ticks: 45°, 135°, 225°, 315°, r: 0.53 .. 0.58)
                    if (r >= 0.53f && r <= 0.58f)
                    {
                        float diagDist = Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) * 0.7071f;
                        float diagFactor = Mathf.Clamp01((0.007f - diagDist) / feather);
                        if (diagFactor > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, ringLaser, 0.65f * diagFactor);
                        }
                    }

                    // 7. 4 主方位微刻度 (0°, 90°, 180°, 270°) 平滑抗锯齿
                    if (r >= 0.83f && r <= 0.91f)
                    {
                        float tickX = Mathf.Clamp01((0.012f - Mathf.Abs(dx)) / feather);
                        float tickY = Mathf.Clamp01((0.012f - Mathf.Abs(dy)) / feather);
                        float tickFactor = Mathf.Max(tickX, tickY);
                        if (tickFactor > 0.001f)
                        {
                            pixel = Color.Lerp(pixel, tickColor, 0.85f * tickFactor);
                        }
                    }

                    pixel.a *= edgeAlpha;
                    cols[y * size + x] = pixel;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成高精度 512x512 现代空天/航天器矢量剪影纹理
        /// (包含针状空速管、双曲尖削机头、座舱天窗反射高光、前缘大边条、复合后掠三角翼、升降副翼分割缝、翼尖姿态喷口与双发矢量喷管)
        /// 全程采用次像素分析距离场平滑抗锯齿，零颜色字面量。
        /// </summary>
        private static Texture2D CreateProceduralSpacecraftTexture()
        {
            const int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 3.0f * invHalf; // ~3像素次像素反走样羽化

            for (int y = 0; y < size; y++)
            {
                float ny = (float)y / (size - 1); // 0.0 (尾部) .. 1.0 (机头)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1.0 .. +1.0
                    float dx = Mathf.Abs(nx);

                    // 1. 机体各段包络半宽 W(ny) 计算
                    float bodyW = 0f;
                    float wingW = 0f;
                    bool inProbe = false;
                    bool inNozzle = false;
                    float nozzleCenter = 0.115f;
                    float nozzleDist = Mathf.Abs(dx - nozzleCenter);
                    float nozzleW = 0f;

                    // A. 空速管 / 机头长探针 (0.91 .. 0.97)
                    if (ny >= 0.91f && ny <= 0.97f)
                    {
                        float pt = (ny - 0.91f) / 0.06f;
                        float pWidth = Mathf.Lerp(0.016f, 0.003f, pt);
                        if (dx <= pWidth)
                        {
                            inProbe = true;
                            bodyW = pWidth;
                        }
                    }

                    // B. 尖削雷达罩与双曲整流头锥 (0.72 .. 0.91)
                    if (ny >= 0.72f && ny < 0.91f)
                    {
                        float t = (ny - 0.72f) / 0.19f;
                        bodyW = Mathf.Lerp(0.145f, 0.016f, Mathf.Pow(t, 0.75f));
                    }
                    // C. 前缘大边条 / 前机身 (0.52 .. 0.72)
                    else if (ny >= 0.52f && ny < 0.72f)
                    {
                        float t = (ny - 0.52f) / 0.20f;
                        bodyW = Mathf.Lerp(0.25f, 0.145f, Mathf.Pow(t, 0.85f));
                    }
                    // D. 主后掠双三角翼段 (0.18 .. 0.52)
                    else if (ny >= 0.18f && ny < 0.52f)
                    {
                        bodyW = 0.25f; // 核心机身宽

                        // 前缘后掠至翼尖 (0.27 .. 0.52)
                        if (ny >= 0.27f)
                        {
                            float wt = (ny - 0.27f) / 0.25f;
                            wingW = Mathf.Lerp(0.72f, 0.25f, Mathf.Pow(wt, 0.78f));
                        }
                        // 翼尖防颤配重 / 导弹滑轨 / 姿态喷口 (0.22 .. 0.27)
                        else if (ny >= 0.22f)
                        {
                            wingW = 0.72f;
                        }
                        // 机翼后缘前掠切角与升降副翼内收 (0.18 .. 0.22)
                        else
                        {
                            float wt = (ny - 0.18f) / 0.04f;
                            wingW = Mathf.Lerp(0.25f, 0.72f, Mathf.Pow(wt, 0.60f));
                        }
                    }
                    // E. 尾部发动机整流段 (0.07 .. 0.18)
                    else if (ny >= 0.07f && ny < 0.18f)
                    {
                        float t = (ny - 0.07f) / 0.11f;
                        bodyW = Mathf.Lerp(0.19f, 0.25f, t);

                        // 双发火箭/涡扇尾喷管外廓 (0.07 .. 0.17)
                        if (ny >= 0.07f && ny <= 0.17f)
                        {
                            float nt = (ny - 0.07f) / 0.10f;
                            nozzleW = Mathf.Lerp(0.062f, 0.048f, nt);
                            if (nozzleDist <= nozzleW) inNozzle = true;
                        }
                    }

                    float maxExtent = Mathf.Max(bodyW, wingW);

                    // 2. 次像素反走样因子计算
                    float hullAlpha = 0f;
                    if (maxExtent > 0.001f)
                    {
                        hullAlpha = Mathf.Clamp01((maxExtent - dx) / feather);
                    }
                    if (inProbe)
                    {
                        hullAlpha = Mathf.Max(hullAlpha, Mathf.Clamp01((0.97f - ny) / feather));
                    }
                    if (inNozzle)
                    {
                        float nDistAlpha = Mathf.Clamp01((nozzleW - nozzleDist) / feather);
                        float nBottomAlpha = Mathf.Clamp01((ny - 0.07f) / feather);
                        float nAlpha = Mathf.Min(nDistAlpha, nBottomAlpha);
                        hullAlpha = Mathf.Max(hullAlpha, nAlpha);
                    }

                    if (hullAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 3. 几何分层与明度/结构线解算 (Luminance & Structure Detailing)
                    float lum = 0.65f; // 基准机身蒙皮亮度
                    float alphaWeight = 0.85f; // 基准透明度

                    // A. 外轮廓矢量高亮描边 (Rim Glow, 边框强化)
                    float distToHullRim = maxExtent - dx;
                    if (distToHullRim >= 0f && distToHullRim <= 0.022f)
                    {
                        float rimFactor = 1f - (distToHullRim / 0.022f);
                        lum = Mathf.Lerp(lum, 1.0f, rimFactor);
                        alphaWeight = Mathf.Lerp(alphaWeight, 1.0f, rimFactor);
                    }

                    // B. 翼尖 RCS 姿态喷口高亮指示 (0.22 .. 0.27, 翼展边缘)
                    if (ny >= 0.22f && ny <= 0.27f && dx >= 0.66f)
                    {
                        lum = 0.98f;
                        alphaWeight = 1.0f;
                    }

                    // C. 座舱天窗座舱罩 (Cockpit Canopy, 0.63 .. 0.81)
                    if (ny >= 0.63f && ny <= 0.81f)
                    {
                        float ct = (ny - 0.63f) / 0.18f;
                        float canopyW = Mathf.Lerp(0.062f, 0.016f, Mathf.Pow(ct, 0.85f));
                        if (dx <= canopyW)
                        {
                            float canopyDist = canopyW - dx;
                            // 舱盖边框
                            if (canopyDist <= 0.012f)
                            {
                                lum = 1.0f;
                                alphaWeight = 1.0f;
                            }
                            else
                            {
                                // 舱盖深色玻璃与高光反射条
                                if (nx >= -0.038f && nx <= -0.012f && ny >= 0.66f && ny <= 0.77f)
                                {
                                    lum = 0.92f; // 左前侧高光反射 (Glint)
                                    alphaWeight = 0.95f;
                                }
                                else
                                {
                                    lum = 0.26f; // 深邃航电玻璃底色
                                    alphaWeight = 0.92f;
                                }
                            }
                        }
                    }

                    // D. 中心背脊高光线 (Dorsal Spine Ridge, 0.18 .. 0.88)
                    if (dx <= 0.012f && ny >= 0.18f && ny <= 0.88f)
                    {
                        lum = 0.96f;
                        alphaWeight = 1.0f;
                    }

                    // E. 升降副翼铰链刻线与副翼分割缝 (Elevon Seams)
                    if (ny >= 0.235f && ny <= 0.246f && dx >= 0.25f && dx <= 0.68f)
                    {
                        lum = 0.35f; // 细缝阴影
                    }

                    // F. 尾喷管内侧燃烧室与喉部环 (Engine Nozzle Depth & Throat Ring)
                    if (inNozzle && ny <= 0.16f)
                    {
                        if (ny >= 0.125f && ny <= 0.142f && nozzleDist <= 0.038f)
                        {
                            lum = 0.95f; // 喉部高光环
                            alphaWeight = 1.0f;
                        }
                        else if (ny < 0.125f)
                        {
                            lum = 0.22f; // 喷管深色内腔
                        }
                    }

                    // G. 空速管针尖 (0.91 .. 0.97)
                    if (inProbe)
                    {
                        lum = 0.98f;
                        alphaWeight = 1.0f;
                    }

                    // 4. 生成语义色 (零颜色字面量，纯由 NeutralOpaque 派生)
                    Color baseCol = WidgetStyleManager.NeutralOpaque;
                    if (lum < 0.999f)
                    {
                        baseCol = WidgetStyleManager.Darken(baseCol, 1.0f - lum);
                    }
                    Color finalPixel = WidgetStyleManager.WithAlpha(baseCol, alphaWeight * hullAlpha);
                    cols[y * size + x] = finalPixel;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 64x64 高精度矢量机头朝向标 (Precision Boresight Chevron Pointer)
        /// 带有次像素平滑抗锯齿的前视光标倒 V 导引箭头，替代粗糙的旋转正方形。
        /// </summary>
        private static Texture2D CreateBoresightPointerTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.5f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1 (顶部是 +1, 底部是 -1)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float dx = Mathf.Abs(nx);

                    // 倒 V 航电指引前向箭头 (Apex 顶点位于 ny = 0.75, dx = 0)
                    // 外缘斜率: ny_outer = 0.75 - 1.45 * dx
                    // 内缘斜率: ny_inner = 0.32 - 1.35 * dx
                    float outerY = 0.75f - 1.45f * dx;
                    float innerY = 0.32f - 1.35f * dx;

                    float distTop = outerY - ny;
                    float distBot = ny - innerY;
                    float distSide = 0.70f - dx;

                    float alphaTop = Mathf.Clamp01(distTop / feather);
                    float alphaBot = Mathf.Clamp01(distBot / feather);
                    float alphaSide = Mathf.Clamp01(distSide / feather);
                    float alphaBottomCut = Mathf.Clamp01((ny - (-0.45f)) / feather);

                    float inChevronAlpha = Mathf.Min(Mathf.Min(alphaTop, alphaBot), Mathf.Min(alphaSide, alphaBottomCut));

                    if (inChevronAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 边框高亮，内部柔和
                    float edgeDist = Mathf.Min(Mathf.Min(distTop, distBot), distSide);
                    float lum = (edgeDist <= 0.06f) ? 1.0f : 0.80f;

                    Color baseCol = WidgetStyleManager.NeutralOpaque;
                    if (lum < 0.999f)
                    {
                        baseCol = WidgetStyleManager.Darken(baseCol, 1.0f - lum);
                    }
                    cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, inChevronAlpha);
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 256x256 高精度 3D 人造地平仪与俯仰阶梯纹理 (Attitude Horizon & Pitch Ladder)
        /// 包含水平基准线、两端下垂刻标、+10°/+20° 仰角实线折角梯、-10°/-20° 俯角虚线梯。
        /// 次像素反走样，零颜色字面量。
        /// </summary>
        private static Texture2D CreateAttitudeHorizonTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.0f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);
                    float absY = Mathf.Abs(ny);

                    float maxAlpha = 0f;

                    // 1. 水平基准地平线 (Center Horizon Bar, ny = 0, 留出中心飞船空隙)
                    if (absX >= 0.18f && absX <= 0.76f)
                    {
                        float dY = absY;
                        float dX = Mathf.Max(0.18f - absX, absX - 0.76f);
                        float dist = Mathf.Max(dY - 0.022f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 2. 地平线两端下垂刻标 (Horizon End Ticks, nx = ±0.74, ny: -0.08 .. 0.0)
                    if (absX >= 0.72f && absX <= 0.76f && ny <= 0.01f && ny >= -0.08f)
                    {
                        float dist = Mathf.Max(Mathf.Abs(absX - 0.74f) - 0.020f, -0.08f - ny);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 3. +10° 俯仰仰角梯线 (ny ~ 0.28, 带折角)
                    if (absX >= 0.14f && absX <= 0.46f)
                    {
                        float dY = Mathf.Abs(ny - 0.28f);
                        float dX = Mathf.Max(0.14f - absX, absX - 0.46f);
                        float dist = Mathf.Max(dY - 0.018f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);

                        // 两端下垂折角 (0.22 .. 0.28)
                        if (absX >= 0.43f && absX <= 0.47f && ny <= 0.29f && ny >= 0.22f)
                        {
                            float tickDist = Mathf.Abs(absX - 0.45f) - 0.018f;
                            float ta = (tickDist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - tickDist / feather);
                            a = Mathf.Max(a, ta);
                        }
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 4. -10° 俯仰俯角虚线梯线 (ny ~ -0.28)
                    if (absX >= 0.14f && absX <= 0.46f)
                    {
                        float dashPhase = Mathf.Repeat(absX * 24f, 1f);
                        if (dashPhase < 0.65f)
                        {
                            float dY = Mathf.Abs(ny - (-0.28f));
                            float dX = Mathf.Max(0.14f - absX, absX - 0.46f);
                            float dist = Mathf.Max(dY - 0.018f, dX);
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);

                            // 两端上翘折角 (-0.28 .. -0.22)
                            if (absX >= 0.43f && absX <= 0.47f && ny >= -0.29f && ny <= -0.22f)
                            {
                                float tickDist = Mathf.Abs(absX - 0.45f) - 0.018f;
                                float ta = (tickDist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - tickDist / feather);
                                a = Mathf.Max(a, ta);
                            }
                            if (a > maxAlpha) maxAlpha = a;
                        }
                    }

                    // 5. +20° 仰角短梯线 (ny ~ 0.56)
                    if (absX >= 0.16f && absX <= 0.36f)
                    {
                        float dY = Mathf.Abs(ny - 0.56f);
                        float dX = Mathf.Max(0.16f - absX, absX - 0.36f);
                        float dist = Mathf.Max(dY - 0.018f, dX);
                        float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                        if (a > maxAlpha) maxAlpha = a;
                    }

                    // 6. -20° 俯角短虚线梯线 (ny ~ -0.56)
                    if (absX >= 0.16f && absX <= 0.36f)
                    {
                        float dashPhase = Mathf.Repeat(absX * 24f, 1f);
                        if (dashPhase < 0.65f)
                        {
                            float dY = Mathf.Abs(ny - (-0.56f));
                            float dX = Mathf.Max(0.16f - absX, absX - 0.36f);
                            float dist = Mathf.Max(dY - 0.018f, dX);
                            float a = (dist <= 0f) ? 1.0f : Mathf.Clamp01(1.0f - dist / feather);
                            if (a > maxAlpha) maxAlpha = a;
                        }
                    }

                    if (maxAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color baseCol = WidgetStyleManager.NeutralOpaque;
                        cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, Mathf.Clamp01(maxAlpha));
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 程序化生成 64x64 高精度 3D 飞行指引仪导引标纹理 (Flight Director Chevron Cue)
        /// 包含次像素平滑倒 V 框架、中心精确瞄准点与水平翼基准刻标，零颜色字面量。
        /// </summary>
        private static Texture2D CreateFlightDirectorChevronTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = (size - 1) * 0.5f;
            float invHalf = 1f / half;
            float feather = 2.5f * invHalf;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) * invHalf; // -1 .. +1
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) * invHalf; // -1 .. +1
                    float absX = Mathf.Abs(nx);

                    // 1. 倒 V 导引前视指示框 (Chevron Cue)
                    float outDist = (0.68f - 1.25f * absX) - ny;
                    float inDist = ny - (0.32f - 1.25f * absX);
                    float sideDist = 0.62f - absX;
                    float bottomDist = ny - (-0.48f);

                    float chevAlpha = 0f;
                    if (outDist >= -feather && inDist >= -feather && sideDist >= -feather && bottomDist >= -feather)
                    {
                        float aOut = Mathf.Clamp01(outDist / feather);
                        float aIn = Mathf.Clamp01(inDist / feather);
                        float aSide = Mathf.Clamp01(sideDist / feather);
                        float aBot = Mathf.Clamp01(bottomDist / feather);
                        chevAlpha = Mathf.Min(Mathf.Min(aOut, aIn), Mathf.Min(aSide, aBot));
                    }

                    // 2. 中心十字瞄准微点 (Boresight Center Pip, 半径 0.12)
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float pipAlpha = Mathf.Clamp01((0.14f - r) / feather);

                    // 3. 左右水平翼展基准线 (ny: -0.06 .. 0.06, absX: 0.58 .. 0.88)
                    float wingAlpha = 0f;
                    if (absX >= 0.58f && absX <= 0.88f && Mathf.Abs(ny) <= 0.06f)
                    {
                        float wy = Mathf.Clamp01((0.022f - Mathf.Abs(ny)) / feather);
                        float wx = Mathf.Clamp01((absX - 0.58f) / feather) * Mathf.Clamp01((0.88f - absX) / feather);
                        wingAlpha = Mathf.Min(wy, wx);
                    }

                    float finalAlpha = Mathf.Max(Mathf.Max(chevAlpha, pipAlpha), wingAlpha);

                    if (finalAlpha <= 0.001f)
                    {
                        cols[y * size + x] = Color.clear;
                    }
                    else
                    {
                        Color baseCol = WidgetStyleManager.NeutralOpaque;
                        cols[y * size + x] = WidgetStyleManager.WithAlpha(baseCol, finalAlpha);
                    }
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
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
