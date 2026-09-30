using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Telemetry;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 姿态指示器零-GC遥测快照 (MFP-SPEC-012)
    /// </summary>
    public struct SpaceXAttitudeState : IEquatable<SpaceXAttitudeState>
    {
        public bool HasVessel;
        public float Pitch;
        public float Roll;
        public float Heading;
        public string AttitudeText;

        public bool Equals(SpaceXAttitudeState other)
        {
            return HasVessel == other.HasVessel &&
                   Math.Abs(Pitch - other.Pitch) < 0.05f &&
                   Math.Abs(Roll - other.Roll) < 0.05f &&
                   Math.Abs(Heading - other.Heading) < 0.05f &&
                   string.Equals(AttitudeText, other.AttitudeText, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is SpaceXAttitudeState other && Equals(other);
        public override int GetHashCode() => (Pitch, Roll, Heading, HasVessel).GetHashCode();
    }

    /// <summary>
    /// SpaceX 姿态指示器业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class SpaceXAttitudeLogic : WidgetLogic<SpaceXAttitudeState>
    {
        public string AttitudeFormat { get; set; }
        private float _lastFormattedPitch = float.NaN;
        private float _lastFormattedRoll = float.NaN;
        private string _cachedAttitudeText = string.Empty;

        public override void Reset()
        {
            CurrentState = default;
            _lastFormattedPitch = float.NaN;
            _lastFormattedRoll = float.NaN;
            _cachedAttitudeText = string.Empty;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            float pitch = telemetry.Pitch;
            float roll = telemetry.Roll;
            float heading = telemetry.Heading;

            if (float.IsNaN(_lastFormattedPitch) || Math.Abs(pitch - _lastFormattedPitch) >= 0.1f ||
                float.IsNaN(_lastFormattedRoll) || Math.Abs(roll - _lastFormattedRoll) >= 0.1f)
            {
                string fmt = !string.IsNullOrEmpty(AttitudeFormat) ? AttitudeFormat : "P {0:+0;-0;0}° R {1:+0;-0;0}°";
                _cachedAttitudeText = string.Format(fmt, pitch, roll);
                _lastFormattedPitch = pitch;
                _lastFormattedRoll = roll;
            }

            CurrentState = new SpaceXAttitudeState
            {
                HasVessel = true,
                Pitch = pitch,
                Roll = roll,
                Heading = heading,
                AttitudeText = _cachedAttitudeText
            };
        }
    }

    /// <summary>
    /// SpaceX 星舰飞船姿态指示器 (SpaceX Webcast Attitude & Orientation Dial)
    /// 包含：
    ///   1. 纯圆形暗色航电表盘与真北 "N" 导航罗盘标
    ///   2. 3D 透视机动参考平环 (Gimbal Reference Ring)，随飞船 Pitch / Roll 动态透视倾斜 (GPU 矢量网格，SPEC-002)
    ///   3. 轴测 3D 星舰飞船矢量剪影 (纯 GPU 矢量网格，零 CPU 光栅化，SPEC-002)
    ///   4. 实时姿态数字角读数 (PITCH / ROLL)
    /// 严格遵循 MFP 架构规范：
    ///   - RefreshTier 为 Critical 60Hz 保证姿态响应极致平滑
    ///   - 零硬编码与零颜色字面量 (MFP-SPEC-006)
    ///   - 纯业务大脑解耦 (MFP-SPEC-012)
    /// </summary>
    [FlightWidget("spacex_attitude", "dragon_attitude", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 载人龙飞船姿态指示器", Description = "SpaceX 极简黑白双轴陀螺姿态仪，显示俯仰、滚转与偏航微步。", DefaultWidgetId = "spacex.attitude", DefaultX = -360f, DefaultY = 0f, IsSingleton = true, HighFrequency = true, ExactIds = new[] { "spacex.attitude" })]
    public class SpaceXAttitudeWidget : BaseFlightWidget
    {
        public override Vector2 BaseSize => new Vector2(96f, 96f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Critical;

        // UI 视图节点
        private Image _bgImage;
        private Outline _bgOutline;
        private ProceduralAttitudeBezelImage _dialBezel;
        private Text _northIndicatorText;

        // 3D 姿态参考环与星舰剪影
        private RectTransform _gimbalRingRt;
        private ProceduralAttitudeRingImage _gimbalRing;
        private RectTransform _shipSilhouetteContainerRt;
        private RawImage _shipSilhouetteRawImage;
        private ProceduralStarshipImage _proceduralStarship;

        private Text _attitudeLabelText;

        // 姿态缓存与脏标记 (MFP-SPEC-009)
        private readonly CachedFloat _lastPitch = new CachedFloat(float.NaN, 0.1f);
        private readonly CachedFloat _lastRoll = new CachedFloat(float.NaN, 0.1f);
        private readonly CachedFloat _lastHeading = new CachedFloat(float.NaN, 0.1f);
        private readonly Cached<string> _lastAttitudeStr = new Cached<string>(string.Empty);

        // 业务大脑 (MFP-SPEC-012)
        private readonly SpaceXAttitudeLogic _logic = new SpaceXAttitudeLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // CustomTemplate 自定义通道
        private string _northLabel = "N";
        private string _attitudeFormat = "P {0:+0;-0;0}° R {1:+0;-0;0}°";

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            WidgetStyleManager style = WidgetStyleManager.Instance;
            _northLabel = GetTemplateChannel("NORTH", "N");
            _attitudeFormat = GetTemplateChannel("FORMAT", I18n.Tr("WIDGET_SPX_ATTITUDE_FORMAT", "俯仰 {0:+0;-0;0}° 滚转 {1:+0;-0;0}°"));

            _logic.AttitudeFormat = _attitudeFormat;

            // 1. 组件包围盒 (基准 96x96 逻辑像素圆形表盘)
            float diameter = 96f * s;
            RectTransform.sizeDelta = new Vector2(diameter, diameter);

            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 2. 外部圆形深色底盘 (Bezel，GPU 矢量网格)
            _dialBezel = CreateChild<ProceduralAttitudeBezelImage>("Attitude_Bezel", transform, new Vector2(diameter, diameter), Vector2.zero);
            _dialBezel.raycastTarget = false;

            // 3. 顶部真北标
            _northIndicatorText = UIFactory.CreateText(transform, "North_Mark", _northLabel, Mathf.RoundToInt(9f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            _northIndicatorText.fontStyle = FontStyle.Bold;
            RectTransform nRt = _northIndicatorText.rectTransform;
            nRt.sizeDelta = new Vector2(20f * s, 16f * s);
            nRt.anchoredPosition = new Vector2(0f, (diameter * 0.5f) - 4f * s);

            // 4. 3D 姿态透视环 (Gimbal Reference Horizon Ring，GPU 矢量网格)
            _gimbalRing = CreateChild<ProceduralAttitudeRingImage>("Gimbal_Ring", transform, new Vector2(76f * s, 42f * s), Vector2.zero);
            _gimbalRingRt = _gimbalRing.rectTransform;
            _gimbalRing.raycastTarget = false;

            // 5. 中央飞船剪影容器：支持 VesselSilhouetteBaker 真实剪影与 ProceduralStarshipImage GPU 矢量双模
            var shipContainer = CreateChild<RectTransform>("Ship_Silhouette_Container", transform, new Vector2(56f * s, 56f * s), Vector2.zero);
            _shipSilhouetteContainerRt = shipContainer;

            _shipSilhouetteRawImage = CreateChild<RawImage>("Vessel_Baker_Silhouette", _shipSilhouetteContainerRt, new Vector2(56f * s, 56f * s), Vector2.zero);
            _shipSilhouetteRawImage.raycastTarget = false;

            _proceduralStarship = CreateChild<ProceduralStarshipImage>("Procedural_Starship", _shipSilhouetteContainerRt, new Vector2(56f * s, 56f * s), Vector2.zero);
            _proceduralStarship.raycastTarget = false;

            UpdateSilhouetteTexture(VesselSilhouetteService.Provider?.SilhouetteTexture);

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 6. 底部微型姿态读数 (PITCH / ROLL)
            _attitudeLabelText = UIFactory.CreateText(transform, "Attitude_Label", "", Mathf.RoundToInt(6.5f * s), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.SecondaryValue, theme));
            _attitudeLabelText.fontStyle = FontStyle.Bold;
            RectTransform attRt = _attitudeLabelText.rectTransform;
            attRt.sizeDelta = new Vector2(diameter, 12f * s);
            attRt.anchoredPosition = new Vector2(0f, (-diameter * 0.5f) + 6f * s);

            // 注册微控件至标准化管理器
            this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Attitude Dial Background", _bgImage.gameObject, "SpaceX姿态球表盘底板", t => ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, t)));
            if (_dialBezel != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel", "Dial Bezel", _dialBezel.gameObject, "姿态圆形深色底盘", t => { if (_dialBezel != null) _dialBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep); }));
            }
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "north_mark", "North Mark", _northIndicatorText != null ? _northIndicatorText.gameObject : null));
            if (_gimbalRing != null)
            {
                this.Controls.Register(new WidgetGraphicViewportControl("gimbal_ring", "Gimbal Ring", _gimbalRing.gameObject));
            }
            if (_shipSilhouetteContainerRt != null)
            {
                this.Controls.Register(new WidgetGraphicViewportControl("ship_silhouette", "Ship Silhouette", _shipSilhouetteContainerRt.gameObject, _shipSilhouetteRawImage));
            }
            this.Controls.Register(new WidgetReadoutControl("attitude_readout", "底部俯仰滚转角读数", _attitudeLabelText != null ? _attitudeLabelText.gameObject : null, _attitudeLabelText, null, TextStyleRole.SecondaryValue, "{PITCH}"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            UpdateSilhouetteTexture(tex);
        }

        private void UpdateSilhouetteTexture(Texture tex)
        {
            if (tex != null)
            {
                if (_shipSilhouetteRawImage != null)
                {
                    _shipSilhouetteRawImage.texture = tex;
                    if (!_shipSilhouetteRawImage.gameObject.activeSelf) _shipSilhouetteRawImage.gameObject.SetActive(true);
                }
                if (_proceduralStarship != null && _proceduralStarship.gameObject.activeSelf)
                {
                    _proceduralStarship.gameObject.SetActive(false);
                }
            }
            else
            {
                if (_shipSilhouetteRawImage != null && _shipSilhouetteRawImage.gameObject.activeSelf)
                {
                    _shipSilhouetteRawImage.gameObject.SetActive(false);
                }
                if (_proceduralStarship != null && !_proceduralStarship.gameObject.activeSelf)
                {
                    _proceduralStarship.gameObject.SetActive(true);
                }
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            if (_dialBezel != null)
            {
                _dialBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
            }

            if (_gimbalRing != null)
            {
                _gimbalRing.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Strong);
            }

            if (_shipSilhouetteRawImage != null)
            {
                _shipSilhouetteRawImage.color = WidgetStyleManager.NeutralOpaque;
            }

            if (_proceduralStarship != null)
            {
                _proceduralStarship.color = WidgetStyleManager.NeutralOpaque;
            }

            ApplyText(_northIndicatorText, TextStyleRole.Cardinal, theme);
            ApplyText(_attitudeLabelText, TextStyleRole.SecondaryValue, theme);
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            SpaceXAttitudeState state = _logic.CurrentState;
            if (!state.HasVessel)
            {
                if (_lastAttitudeStr.Update(string.Empty))
                {
                    _attitudeLabelText?.SetTextSafe(string.Empty);
                }
                return;
            }

            float pitch = state.Pitch;
            float roll = state.Roll;
            float heading = state.Heading;

            // 姿态变动脏标记检查 (0.1 度分辨率)
            bool pDirty = _lastPitch.Update(pitch);
            bool rDirty = _lastRoll.Update(roll);
            bool hDirty = _lastHeading.Update(heading);
            if (!pDirty && !rDirty && !hDirty)
            {
                return;
            }

            float s = CurrentDpiScale;

            // 1. 3D 姿态参考环：随 Roll 倾斜，随 Pitch 改变扁平透视率与垂直偏移
            if (_gimbalRingRt != null)
            {
                _gimbalRingRt.localEulerAngles = new Vector3(0f, 0f, -roll);
                float pitchFactor = Mathf.Clamp(pitch / 90f, -1f, 1f);
                float ringHeight = Mathf.Lerp(42f, 14f, Mathf.Abs(pitchFactor)) * s;
                _gimbalRingRt.sizeDelta = new Vector2(76f * s, ringHeight);
                _gimbalRingRt.anchoredPosition = new Vector2(0f, pitchFactor * 10f * s);
            }

            // 2. 飞船剪影姿态联动：中心剪影随纵滚俯仰透视平移
            if (_shipSilhouetteContainerRt != null)
            {
                _shipSilhouetteContainerRt.localEulerAngles = new Vector3(0f, 0f, -roll);
                float pitchFactor = Mathf.Clamp(pitch / 90f, -1f, 1f);
                _shipSilhouetteContainerRt.anchoredPosition = new Vector2(0f, pitchFactor * 5f * s);
            }

            // 3. 罗盘真北微标：沿外圆周动态环绕旋转指向真实北方
            if (_northIndicatorText != null)
            {
                float radius = (96f * s * 0.5f) - 10f * s;
                float rad = -heading * Mathf.Deg2Rad;
                _northIndicatorText.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);
            }

            // 4. 底部文字简报 (支持自定义格式与脏缓存)
            if (_attitudeLabelText != null && _lastAttitudeStr.Update(state.AttitudeText))
            {
                _attitudeLabelText.SetTextSafe(state.AttitudeText);
            }
        }

        protected override void OnDestroy()
        {
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// GPU 程序化圆形航电外圈底盘与金属光晕边缘 (零 CPU 软件光栅化，SPEC-002)
    /// </summary>
    public class ProceduralAttitudeBezelImage : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            if (radius <= 0.001f) return;
            Vector2 center = r.center;
            const int segments = 48;

            Color cDisc = color;
            Color cRing = WidgetStyleManager.Lighten(cDisc, 0.35f);

            // 内部深色底盘
            float rInner = radius * 0.92f;
            int centerIdx = vh.currentVertCount;
            vh.AddVert(center, cDisc, Vector2.zero);

            for (int i = 0; i <= segments; i++)
            {
                float deg = i * (360f / segments);
                float rad = deg * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(rad) * rInner, Mathf.Sin(rad) * rInner);
                vh.AddVert(pos, cDisc, Vector2.zero);
                if (i > 0)
                {
                    vh.AddTriangle(centerIdx, centerIdx + i, centerIdx + i + 1);
                }
            }

            // 外部高光边缘环
            for (int i = 0; i <= segments; i++)
            {
                float deg = i * (360f / segments);
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                Vector2 vIn = center + new Vector2(cos * rInner, sin * rInner);
                Vector2 vOut = center + new Vector2(cos * radius, sin * radius);

                vh.AddVert(vIn, cDisc, Vector2.zero);
                vh.AddVert(vOut, cRing, Vector2.zero);

                if (i > 0)
                {
                    int baseIdx = centerIdx + 1 + (segments + 1) + (i - 1) * 2;
                    vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                    vh.AddTriangle(baseIdx + 3, baseIdx + 2, baseIdx);
                }
            }
        }
    }

    /// <summary>
    /// GPU 程序化 3D 姿态地平仪参考平环 (零 CPU 软件光栅化，SPEC-002)
    /// </summary>
    public class ProceduralAttitudeRingImage : MaskableGraphic
    {
        public float InnerRadiusRatio = 0.88f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float rx = r.width * 0.5f;
            float ry = r.height * 0.5f;
            if (rx <= 0.001f || ry <= 0.001f) return;
            Vector2 center = r.center;
            const int segments = 48;
            Color c = color;

            for (int i = 0; i <= segments; i++)
            {
                float deg = i * (360f / segments);
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                Vector2 vIn = center + new Vector2(cos * rx * InnerRadiusRatio, sin * ry * InnerRadiusRatio);
                Vector2 vOut = center + new Vector2(cos * rx, sin * ry);

                vh.AddVert(vIn, c, Vector2.zero);
                vh.AddVert(vOut, c, Vector2.zero);

                if (i > 0)
                {
                    int baseIdx = (i - 1) * 2;
                    vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                    vh.AddTriangle(baseIdx + 3, baseIdx + 2, baseIdx);
                }
            }
        }
    }

    /// <summary>
    /// GPU 程序化星舰 3D 轴测矢量剪影 (纯 GPU 顶点网格，零 CPU 光栅化，SPEC-002)
    /// </summary>
    public class ProceduralStarshipImage : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float hw = r.width * 0.5f;
            float hh = r.height * 0.5f;
            Vector2 c = r.center;

            Color baseCol = color;
            Color heatshieldCol = WidgetStyleManager.WithAlpha(baseCol, baseCol.a * 0.70f);
            Color ridgeCol = WidgetStyleManager.Lighten(baseCol, 0.25f);
            Color stainlessCol = baseCol;

            float bodyHalfW = hw * 0.28f;
            float noseTopY = hh * 0.75f;
            float noseBaseY = hh * 0.45f;
            float fwdFlapTopY = hh * 0.55f;
            float fwdFlapBaseY = hh * 0.40f;
            float fwdFlapSpan = hw * 0.52f;
            float aftFlapTopY = -hh * 0.45f;
            float aftFlapBaseY = -hh * 0.72f;
            float aftFlapSpanTop = hw * 0.30f;
            float aftFlapSpanBase = hw * 0.64f;
            float hullBaseY = -hh * 0.75f;

            void AddQuad(Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br, Color col)
            {
                int idx = vh.currentVertCount;
                vh.AddVert(bl, col, Vector2.zero);
                vh.AddVert(tl, col, Vector2.zero);
                vh.AddVert(tr, col, Vector2.zero);
                vh.AddVert(br, col, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx, idx + 2, idx + 3);
            }

            void AddTri(Vector2 v0, Vector2 v1, Vector2 v2, Color col)
            {
                int idx = vh.currentVertCount;
                vh.AddVert(v0, col, Vector2.zero);
                vh.AddVert(v1, col, Vector2.zero);
                vh.AddVert(v2, col, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2);
            }

            // 1. Nose Cone
            Vector2 noseTipL = c + new Vector2(-hw * 0.03f, noseTopY);
            Vector2 noseTipR = c + new Vector2(hw * 0.03f, noseTopY);
            Vector2 noseMidL = c + new Vector2(0f, noseTopY);
            Vector2 noseBaseL = c + new Vector2(-bodyHalfW, noseBaseY);
            Vector2 noseBaseMid = c + new Vector2(0f, noseBaseY);
            Vector2 noseBaseR = c + new Vector2(bodyHalfW, noseBaseY);

            AddTri(noseBaseL, noseTipL, noseBaseMid, heatshieldCol);
            AddTri(noseBaseMid, noseTipR, noseBaseR, stainlessCol);

            // 2. Main Hull Body
            Vector2 hullMidL = c + new Vector2(-bodyHalfW, aftFlapTopY);
            Vector2 hullMidC = c + new Vector2(0f, aftFlapTopY);
            Vector2 hullMidR = c + new Vector2(bodyHalfW, aftFlapTopY);

            AddQuad(hullMidL, noseBaseL, noseBaseMid, hullMidC, heatshieldCol);
            AddQuad(hullMidC, noseBaseMid, noseBaseR, hullMidR, stainlessCol);

            // 3. Aft Body
            Vector2 hullBotL = c + new Vector2(-bodyHalfW, hullBaseY);
            Vector2 hullBotC = c + new Vector2(0f, hullBaseY);
            Vector2 hullBotR = c + new Vector2(bodyHalfW, hullBaseY);

            AddQuad(hullBotL, hullMidL, hullMidC, hullBotC, heatshieldCol);
            AddQuad(hullBotC, hullMidC, hullMidR, hullBotR, stainlessCol);

            // 4. Forward Flaps
            Vector2 fwdLT = c + new Vector2(-fwdFlapSpan, fwdFlapTopY);
            Vector2 fwdLB = c + new Vector2(-fwdFlapSpan, fwdFlapBaseY);
            Vector2 fwdRT = c + new Vector2(fwdFlapSpan, fwdFlapTopY);
            Vector2 fwdRB = c + new Vector2(fwdFlapSpan, fwdFlapBaseY);
            Vector2 fwdBodyLT = c + new Vector2(-bodyHalfW, fwdFlapTopY);
            Vector2 fwdBodyLB = c + new Vector2(-bodyHalfW, fwdFlapBaseY);
            Vector2 fwdBodyRT = c + new Vector2(bodyHalfW, fwdFlapTopY);
            Vector2 fwdBodyRB = c + new Vector2(bodyHalfW, fwdFlapBaseY);

            AddQuad(fwdLB, fwdLT, fwdBodyLT, fwdBodyLB, heatshieldCol);
            AddQuad(fwdBodyRB, fwdBodyRT, fwdRT, fwdRB, stainlessCol);

            // 5. Aft Flaps
            Vector2 aftLT = c + new Vector2(-aftFlapSpanTop, aftFlapTopY);
            Vector2 aftLB = c + new Vector2(-aftFlapSpanBase, aftFlapBaseY);
            Vector2 aftRT = c + new Vector2(aftFlapSpanTop, aftFlapTopY);
            Vector2 aftRB = c + new Vector2(aftFlapSpanBase, aftFlapBaseY);
            Vector2 aftBodyLT = c + new Vector2(-bodyHalfW, aftFlapTopY);
            Vector2 aftBodyLB = c + new Vector2(-bodyHalfW, aftFlapBaseY);
            Vector2 aftBodyRT = c + new Vector2(bodyHalfW, aftFlapTopY);
            Vector2 aftBodyRB = c + new Vector2(bodyHalfW, aftFlapBaseY);

            AddQuad(aftLB, aftLT, aftBodyLT, aftBodyLB, heatshieldCol);
            AddQuad(aftBodyRB, aftBodyRT, aftRT, aftRB, stainlessCol);

            // 6. Center Ridge Line
            float ridgeHalfW = hw * 0.02f;
            Vector2 rBotL = c + new Vector2(-ridgeHalfW, hullBaseY);
            Vector2 rTopL = c + new Vector2(-ridgeHalfW, noseTopY - hh * 0.05f);
            Vector2 rTopR = c + new Vector2(ridgeHalfW, noseTopY - hh * 0.05f);
            Vector2 rBotR = c + new Vector2(ridgeHalfW, hullBaseY);
            AddQuad(rBotL, rTopL, rTopR, rBotR, ridgeCol);
        }
    }
}
