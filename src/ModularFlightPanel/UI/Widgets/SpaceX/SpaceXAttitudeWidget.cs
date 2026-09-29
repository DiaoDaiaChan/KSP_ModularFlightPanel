using System;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    /// <summary>
    /// SpaceX 星舰飞船姿态指示器 (SpaceX Webcast Attitude & Orientation Dial)
    /// 包含：
    ///   1. 纯圆形暗色航电表盘与真北 "N" 导航罗盘标
    ///   2. 3D 透视机动参考平环 (Gimbal Reference Ring)，随飞船 Pitch / Roll 动态透视倾斜
    ///   3. 轴测 3D 星舰飞船剪影 (带前缘鼻锥舵翼、不锈钢筒身、尾部大舵翼与中心脊线)，随飞船姿态旋转
    ///   4. 实时姿态数字角读数 (PITCH / ROLL)
    /// 严格遵循 MFP 架构规范：
    ///   - RefreshTier 为 Critical 60Hz 保证姿态响应极致平滑
    ///   - 零硬编码与零颜色字面量 (MFP-SPEC-006)
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
        private RawImage _dialBackdropRawImage;
        private Text _northIndicatorText;

        // 3D 姿态参考环与星舰剪影
        private RectTransform _gimbalRingRt;
        private RawImage _gimbalRingRawImage;
        private RectTransform _shipSilhouetteRt;
        private RawImage _shipSilhouetteRawImage;

        private Text _attitudeLabelText;

        // 静态共享程序化纹理
        private static Texture2D _sharedDialBezelTexture;
        private static Texture2D _sharedGimbalRingTexture;
        private static Texture2D _sharedStarshipTexture;

        // 姿态缓存与脏标记
        private readonly CachedFloat _lastPitch = new CachedFloat(float.NaN, 0.1f);
        private readonly CachedFloat _lastRoll = new CachedFloat(float.NaN, 0.1f);
        private readonly CachedFloat _lastHeading = new CachedFloat(float.NaN, 0.1f);
        private readonly Cached<string> _lastAttitudeStr = new Cached<string>(string.Empty);

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

            // 1. 组件包围盒 (基准 96x96 逻辑像素圆形表盘)
            float diameter = 96f * s;
            RectTransform.sizeDelta = new Vector2(diameter, diameter);

            _bgImage = CardBackground;
            _bgOutline = CardOutline;
            if (_bgOutline != null)
                _bgOutline.effectDistance = new Vector2(1f * s, 1f * s);

            EnsureSharedTextures();

            // 2. 外部圆形深色底盘 (Bezel)
            _dialBackdropRawImage = CreateChild<RawImage>("Attitude_Bezel", transform, new Vector2(diameter, diameter), Vector2.zero);
            GameObject bezelGo = _dialBackdropRawImage.gameObject;
            RectTransform bezelRt = _dialBackdropRawImage.rectTransform;
            _dialBackdropRawImage.texture = _sharedDialBezelTexture;
            _dialBackdropRawImage.raycastTarget = false;

            // 3. 顶部真北标 (支持自定义)
            _northIndicatorText = UIFactory.CreateText(transform, "North_Mark", _northLabel, Mathf.RoundToInt(9f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Cardinal, theme));
            _northIndicatorText.fontStyle = FontStyle.Bold;
            RectTransform nRt = _northIndicatorText.rectTransform;
            nRt.sizeDelta = new Vector2(20f * s, 16f * s);
            nRt.anchoredPosition = new Vector2(0f, (diameter * 0.5f) - 4f * s);

            // 4. 3D 姿态透视环 (Gimbal Reference Horizon Ring)
            _gimbalRingRawImage = CreateChild<RawImage>("Gimbal_Ring", transform, new Vector2(76f * s, 42f * s), Vector2.zero);
            _gimbalRingRt = _gimbalRingRawImage.rectTransform;
            GameObject ringGo = _gimbalRingRawImage.gameObject;
            _gimbalRingRawImage.texture = _sharedGimbalRingTexture;
            _gimbalRingRawImage.raycastTarget = false;

            // 5. 中央高精飞船 2D 剪影 (优先联动 VesselSilhouetteBaker 真实剪影，保底使用程序化星舰矢量)
            _shipSilhouetteRawImage = CreateChild<RawImage>("Ship_Silhouette", transform, new Vector2(56f * s, 56f * s), Vector2.zero);
            _shipSilhouetteRt = _shipSilhouetteRawImage.rectTransform;
            GameObject shipGo = _shipSilhouetteRawImage.gameObject;
            _shipSilhouetteRawImage.raycastTarget = false;

            Texture shipTex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            if (shipTex == null)
            {
                shipTex = _sharedStarshipTexture;
            }
            _shipSilhouetteRawImage.texture = shipTex;

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
            if (_dialBackdropRawImage != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "bezel", "Dial Bezel", _dialBackdropRawImage.gameObject, "姿态圆形深色底盘", t => { if (_dialBackdropRawImage != null) _dialBackdropRawImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep); }));
            }
            this.Controls.Register(ModularFlightPanel.UI.Framework.WidgetControlManager.WrapElement(this, "north_mark", "North Mark", _northIndicatorText != null ? _northIndicatorText.gameObject : null));
            if (_gimbalRingRawImage != null)
            {
                this.Controls.Register(new WidgetGraphicViewportControl(_gimbalRingRawImage, "Gimbal Ring", "3D空间姿态地平参考环"));
            }
            if (_shipSilhouetteRawImage != null)
            {
                this.Controls.Register(new WidgetGraphicViewportControl(_shipSilhouetteRawImage, "Ship Silhouette", "中央飞船剪影视窗"));
            }
            this.Controls.Register(new WidgetReadoutControl("attitude_readout", "底部俯仰滚转角读数", _attitudeLabelText != null ? _attitudeLabelText.gameObject : null, _attitudeLabelText, null, TextStyleRole.SecondaryValue, "{PITCH}"));
            this.Controls.BindConfigToControls(config);
            this.Controls.ApplyThemeToControls(theme);

            ApplyTheme(theme);
        }

        private void OnSilhouetteUpdated(Texture tex)
        {
            if (_shipSilhouetteRawImage != null && tex != null)
            {
                _shipSilhouetteRawImage.texture = tex;
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _bgOutline, CardStyleRole.Normal, theme);

            if (_dialBackdropRawImage != null)
            {
                _dialBackdropRawImage.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep);
            }

            if (_gimbalRingRawImage != null)
            {
                _gimbalRingRawImage.color = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Strong);
            }

            if (_shipSilhouetteRawImage != null)
            {
                _shipSilhouetteRawImage.color = WidgetStyleManager.NeutralOpaque;
            }

            ApplyText(_northIndicatorText, TextStyleRole.Cardinal, theme);
            ApplyText(_attitudeLabelText, TextStyleRole.SecondaryValue, theme);
        }

        private float _dataPitch;
        private float _dataRoll;
        private float _dataHeading;
        private bool _dataHasVessel;

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
            IFlightTelemetry telemetry = context.Telemetry;
            if (telemetry == null || !telemetry.HasVessel)
            {
                _dataHasVessel = false;
                return;
            }
            _dataHasVessel = true;

            _dataPitch = telemetry.Pitch;
            _dataRoll = telemetry.Roll;
            _dataHeading = telemetry.Heading;
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            if (!_dataHasVessel) return;

            // 纹理保底与热插拔自愈检查
            if (_shipSilhouetteRawImage != null && _shipSilhouetteRawImage.texture == null)
            {
                Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
                if (tex == null)
                {
                    tex = _sharedStarshipTexture;
                }
                _shipSilhouetteRawImage.texture = tex;
            }

            float pitch = _dataPitch;
            float roll = _dataRoll;
            float heading = _dataHeading;

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
            if (_shipSilhouetteRt != null)
            {
                _shipSilhouetteRt.localEulerAngles = new Vector3(0f, 0f, -roll);
                float pitchFactor = Mathf.Clamp(pitch / 90f, -1f, 1f);
                _shipSilhouetteRt.anchoredPosition = new Vector2(0f, pitchFactor * 5f * s);
            }

            // 3. 罗盘真北微标：沿外圆周动态环绕旋转指向真实北方
            if (_northIndicatorText != null)
            {
                float radius = (96f * s * 0.5f) - 10f * s;
                float rad = -heading * Mathf.Deg2Rad;
                _northIndicatorText.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);
            }

            // 4. 底部文字简报 (支持自定义格式与脏缓存)
            if (_attitudeLabelText != null)
            {
                string attStr = string.Format(_attitudeFormat, pitch, roll);
                if (_lastAttitudeStr.Update(attStr))
                {
                    _attitudeLabelText.text = attStr;
                }
            }
        }

        private static void EnsureSharedTextures()
        {
            if (_sharedDialBezelTexture != null) return;

            // 1. 圆形底盘 (256x256)
            const int size = 256;
            _sharedDialBezelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _sharedDialBezelTexture.filterMode = FilterMode.Bilinear;
            _sharedDialBezelTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] bezelCols = new Color[size * size];
            float half = size * 0.5f;
            Color opaque = WidgetStyleManager.NeutralOpaque;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    if (r > 1.0f)
                    {
                        bezelCols[y * size + x] = Color.clear;
                        continue;
                    }

                    // 圆盘本体深色 + 边缘抗锯齿 + 0.94 外环金属高光
                    float edgeAlpha = Mathf.Clamp01((1.0f - r) / (2f / half));
                    float ringAlpha = Mathf.Clamp01((0.03f - Mathf.Abs(r - 0.93f)) / (1.5f / half));

                    Color c = opaque;
                    c.a = Mathf.Max(0.25f, ringAlpha * 0.85f) * edgeAlpha;
                    bezelCols[y * size + x] = c;
                }
            }
            _sharedDialBezelTexture.SetPixels(bezelCols);
            _sharedDialBezelTexture.Apply(false, true);

            // 2. 3D 空间姿态参考环 (256x128)
            const int rw = 256;
            const int rh = 128;
            _sharedGimbalRingTexture = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            _sharedGimbalRingTexture.filterMode = FilterMode.Bilinear;
            _sharedGimbalRingTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] ringCols = new Color[rw * rh];
            float rHalfX = rw * 0.5f;
            float rHalfY = rh * 0.5f;

            for (int y = 0; y < rh; y++)
            {
                float dy = (y - rHalfY) / rHalfY;
                for (int x = 0; x < rw; x++)
                {
                    float dx = (x - rHalfX) / rHalfX;
                    float ell = dx * dx + dy * dy;

                    // 椭圆细线 (0.88 .. 0.96)
                    float dist = Mathf.Abs(Mathf.Sqrt(ell) - 0.92f);
                    float feather = 3f / rHalfY;

                    if (dist > 0.05f + feather)
                    {
                        ringCols[y * rw + x] = Color.clear;
                        continue;
                    }

                    float a = Mathf.Clamp01((0.05f + feather - dist) / feather);
                    Color c = opaque;
                    c.a = a * 0.75f;
                    ringCols[y * rw + x] = c;
                }
            }
            _sharedGimbalRingTexture.SetPixels(ringCols);
            _sharedGimbalRingTexture.Apply(false, true);

            // 3. 星舰 3D 轴测矢量剪影 (256x128 俯仰横向轴测)
            _sharedStarshipTexture = CreateProceduralStarshipTexture();
        }

        private static Texture2D CreateProceduralStarshipTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] cols = new Color[size * size];
            float half = size * 0.5f;
            Color opaque = WidgetStyleManager.NeutralOpaque;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) / half; // -1..1 (从尾部 -0.75 到鼻锥 +0.75)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) / half; // -1..1 (横向翼展)
                    bool inside = false;
                    bool isHeatshield = (nx > 0f); // 腹部隔热瓦侧 (顺时针旋转90度后朝向地球侧)
                    bool isRidge = false;

                    // 星舰几何：竖直放置，顶部为鼻锥 (ny: -0.75 到 +0.75)
                    if (ny >= -0.75f && ny <= 0.75f)
                    {
                        float bodyHalfW = 0.14f;

                        // 1. 鼻锥收窄 (ny: 0.45 .. 0.75)
                        if (ny > 0.45f)
                        {
                            float t = (ny - 0.45f) / 0.30f;
                            float curW = Mathf.Lerp(bodyHalfW, 0.015f, Mathf.Pow(t, 0.8f));
                            if (Mathf.Abs(nx) <= curW) inside = true;
                        }
                        else
                        {
                            if (Mathf.Abs(nx) <= bodyHalfW) inside = true;
                        }

                        // 2. 前端空气舵翼 (Forward Flaps) (ny: 0.40 .. 0.55, 展至 nx: 0.26)
                        if (ny >= 0.40f && ny <= 0.55f)
                        {
                            if (Mathf.Abs(nx) <= 0.26f) inside = true;
                        }

                        // 3. 尾部大空气舵翼 (Aft Aero Flaps) (ny: -0.72 .. -0.45, 展至 nx: 0.32)
                        if (ny >= -0.72f && ny <= -0.45f)
                        {
                            float flapT = (ny - (-0.72f)) / 0.27f;
                            float flapW = Mathf.Lerp(0.32f, 0.15f, flapT);
                            if (Mathf.Abs(nx) <= flapW) inside = true;
                        }

                        // 4. 中心背脊线刻纹
                        if (inside && Mathf.Abs(nx) < 0.02f)
                        {
                            isRidge = true;
                        }
                    }

                    if (!inside)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    Color pixel = opaque;
                    if (isHeatshield)
                    {
                        pixel.a = 0.65f; // 隔热瓦深色面
                    }
                    else if (isRidge)
                    {
                        pixel.a = 0.95f; // 中心高光脊线
                    }
                    else
                    {
                        pixel.a = 0.85f; // 不锈钢亮面
                    }

                    cols[y * size + x] = pixel;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(false, true);
            return tex;
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
}
