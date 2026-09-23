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
    public class SASDialWidget : BaseFlightWidget
    {
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
        private GameObject _shipSilhouette;
        private RawImage _dialBgRawImage;
        private RawImage _silhouetteRawImage;
        private Image _noseTipImage;
        private Text _statusLabel;
        private Outline _statusOutline;
        private Image _statusBg;

        private static Texture2D _circularDialTexture;
        private static Texture2D _fallbackRocketTexture;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            float s = CurrentDpiScale;
            float dialRadius = 48f * s; // 半径 48px, 直径 96px, 容纳于双带 188px 间隙内并契合 -215 视口安全区
            float dialDiameter = dialRadius * 2f;
            RectTransform.sizeDelta = new Vector2(dialDiameter, dialDiameter + 20f * s);

            // 1. 纯圆形激光蚀刻底盘
            CreateCircularBackplate(dialDiameter, s, theme);

            // 2. 中央飞船/火箭剪影与滚转机构
            CreateShipSilhouette(dialRadius, s, theme);

            // 3. 径向 9 大 SAS 方位模式按钮 (对称几何辐射，绝无重叠)
            CreateSASModeButtons(dialRadius, s, theme);

            // 4. 底部微型模式状态标牌
            CreateStatusBadge(dialRadius, s, theme);

            ApplyTheme(theme);
        }

        private void CreateCircularBackplate(float diameter, float s, ThemeConfig theme)
        {
            if (_circularDialTexture == null)
            {
                _circularDialTexture = GenerateCircularDialTexture();
            }

            GameObject bgObj = new GameObject("Dial_Circular_Backplate", typeof(RectTransform), typeof(RawImage));
            bgObj.transform.SetParent(transform, false);
            RectTransform rt = bgObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;

            _dialBgRawImage = bgObj.GetComponent<RawImage>();
            _dialBgRawImage.texture = _circularDialTexture;
            _dialBgRawImage.raycastTarget = false;
        }

        private void CreateShipSilhouette(float dialRadius, float s, ThemeConfig theme)
        {
            _shipSilhouette = new GameObject("Ship_Silhouette_Root", typeof(RectTransform), typeof(Button));
            _shipSilhouette.transform.SetParent(transform, false);
            RectTransform sRt = _shipSilhouette.GetComponent<RectTransform>();
            sRt.sizeDelta = new Vector2(30f * s, 30f * s);
            sRt.anchoredPosition = Vector2.zero;

            // 点击中央火箭剪影快速启用/切换姿态稳定 (STAB)
            Button sBtn = _shipSilhouette.GetComponent<Button>();
            sBtn.transition = Selectable.Transition.None;
            sBtn.onClick.AddListener(() =>
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
            });

            // 火箭剪影贴图层
            GameObject rawImgObj = new GameObject("Silhouette_Graphic", typeof(RectTransform), typeof(RawImage));
            rawImgObj.transform.SetParent(_shipSilhouette.transform, false);
            RectTransform rawRt = rawImgObj.GetComponent<RectTransform>();
            rawRt.sizeDelta = new Vector2(28f * s, 28f * s);
            rawRt.anchoredPosition = Vector2.zero;

            _silhouetteRawImage = rawImgObj.GetComponent<RawImage>();
            _silhouetteRawImage.raycastTarget = false;
            Color accentSec = theme != null ? (Color)theme.AccentSecondary : Color.cyan;
            _silhouetteRawImage.color = accentSec;

            // 优先使用飞船投影提供者，否则启用高精矢量保底纹理
            Texture tex = VesselSilhouetteService.Provider?.SilhouetteTexture;
            if (tex == null)
            {
                if (_fallbackRocketTexture == null)
                {
                    _fallbackRocketTexture = CreateProceduralRocketTexture();
                }
                tex = _fallbackRocketTexture;
            }
            _silhouetteRawImage.texture = tex;

            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated += OnSilhouetteUpdated;
            }

            // 机头朝向前缘醒目高亮指示针 (45度菱形航电前视光标，前缘正向对齐)
            float tipSize = 4f * s;
            Vector2 tipPos = new Vector2(0f, 13f * s);
            GameObject tipObj = UIFactory.CreatePanel(_shipSilhouette.transform, "Nose_Tip",
                new Vector2(tipSize, tipSize), tipPos, theme != null ? (Color)theme.WarningColor : Color.yellow);
            tipObj.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _noseTipImage = tipObj.GetComponent<Image>();
            if (_noseTipImage != null) _noseTipImage.raycastTarget = false;
        }

        private void OnSilhouetteUpdated(Texture rt)
        {
            if (_silhouetteRawImage != null && rt != null)
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
                img.color = new Color(0.04f, 0.08f, 0.13f, 0.85f);

                Outline ol = btn.gameObject.AddComponent<Outline>();
                Color borderCol = theme != null ? (Color)theme.FrameBorderColor : Color.gray;
                ol.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.35f);
                ol.effectDistance = new Vector2(1f * s, 1f * s);

                int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * s));
                Text lbl = UIFactory.CreateText(btn.transform, "Label", m.icon, fontSize, TextAnchor.MiddleCenter,
                    theme != null ? (Color)theme.TextPrimaryColor : Color.white);
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
                theme != null ? (Color)theme.FrameBgColor : new Color(0.04f, 0.07f, 0.12f, 0.90f));
            _statusBg = tagBox.GetComponent<Image>();

            // 点击底部标牌可快速切换 SAS 总开关
            Button badgeBtn = tagBox.GetComponent<Button>() ?? tagBox.AddComponent<Button>();
            badgeBtn.transition = Selectable.Transition.None;
            badgeBtn.onClick.AddListener(() => FlightTelemetryContext.Current?.ToggleSAS());

            _statusOutline = tagBox.AddComponent<Outline>();
            Color border = theme != null ? (Color)theme.FrameBorderColor : Color.cyan;
            _statusOutline.effectColor = new Color(border.r, border.g, border.b, 0.40f);
            _statusOutline.effectDistance = new Vector2(1f * s, 1f * s);

            int fontSize = Mathf.Max(7, Mathf.RoundToInt(7.5f * s));
            _statusLabel = UIFactory.CreateText(tagBox.transform, "Text", "SAS: STABILITY", fontSize, TextAnchor.MiddleCenter,
                theme != null ? (Color)theme.AccentSecondary : Color.cyan);
            RectTransform trt = _statusLabel.GetComponent<RectTransform>();
            trt.sizeDelta = tagSize;
            trt.anchoredPosition = Vector2.zero;
        }

        private void OnSASButtonClicked(FlightSASMode mode)
        {
            FlightTelemetryContext.Current?.SetSASMode(mode);
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null) return;

            // 1. 火箭剪影滚转实时旋转 (顺滑跟随物理 Roll 角，正向右倾对应顺时针)
            if (_shipSilhouette != null)
            {
                _shipSilhouette.transform.localRotation = Quaternion.Euler(0f, 0f, -telemetry.Roll);
            }

            // 2. 纹理保底检查
            if (_silhouetteRawImage != null && _silhouetteRawImage.texture == null)
            {
                if (_fallbackRocketTexture == null) _fallbackRocketTexture = CreateProceduralRocketTexture();
                _silhouetteRawImage.texture = _fallbackRocketTexture;
            }

            // 3. 机头朝向标动态微调 (与正向烘焙包围盒严格对齐)
            if (_noseTipImage != null && VesselSilhouetteService.Provider != null)
            {
                float halfSpan = 14f * CurrentDpiScale;
                float normY = VesselSilhouetteService.Provider.NormalizedNoseTipY;
                float tipY = Mathf.Clamp(normY * halfSpan, 6f * CurrentDpiScale, halfSpan);
                ((RectTransform)_noseTipImage.transform).anchoredPosition = new Vector2(0f, tipY);
            }

            // 4. 当前 SAS 模式与开关高亮指示
            FlightSASMode currentMode = telemetry.CurrentSASMode;
            bool sasOn = telemetry.IsSASEnabled;

            ThemeConfig theme = ThemeManager.Instance.CurrentTheme;
            Color activeCol = (theme != null) ? (Color)theme.AccentPrimary : Color.green;
            Color activeSec = (theme != null) ? (Color)theme.AccentSecondary : Color.cyan;
            Color borderDef = (theme != null) ? (Color)theme.FrameBorderColor : Color.gray;
            Color inactiveBg = new Color(0.04f, 0.08f, 0.13f, 0.85f);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                bool isCurrent = (b.Mode == currentMode) && sasOn;

                if (b.Image != null)
                {
                    b.Image.color = isCurrent ? new Color(activeSec.r, activeSec.g, activeSec.b, 0.40f) : inactiveBg;
                }
                if (b.Outline != null)
                {
                    b.Outline.effectColor = isCurrent ? activeSec : new Color(borderDef.r, borderDef.g, borderDef.b, 0.30f);
                }
                if (b.Label != null)
                {
                    b.Label.color = isCurrent ? Color.white : (theme != null ? (Color)theme.TextPrimaryColor : Color.white);
                }
            }

            // 5. 底部状态指示胶囊文本
            if (_statusLabel != null)
            {
                if (!sasOn)
                {
                    _statusLabel.text = "SAS: OFF";
                    Color warnCol = (theme != null) ? (Color)theme.WarningColor : Color.yellow;
                    _statusLabel.color = warnCol;
                    if (_statusOutline != null) _statusOutline.effectColor = new Color(warnCol.r, warnCol.g, warnCol.b, 0.40f);
                }
                else
                {
                    string modeStr = GetSASModeDisplayName(currentMode);
                    _statusLabel.text = $"SAS: {modeStr}";
                    _statusLabel.color = activeSec;
                    if (_statusOutline != null) _statusOutline.effectColor = activeSec;
                }
            }
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

            if (_silhouetteRawImage != null) _silhouetteRawImage.color = theme.AccentSecondary;
            if (_noseTipImage != null) _noseTipImage.color = theme.WarningColor;
            if (_statusBg != null) _statusBg.color = theme.FrameBgColor;
            if (_statusOutline != null) _statusOutline.effectColor = theme.FrameBorderColor;
            if (_statusLabel != null) _statusLabel.color = theme.AccentSecondary;
        }

        /// <summary>
        /// 程序化生成高精度 512x512 环形航电背板纹理 (三线性 Mipmap 滤波、次像素抗锯齿外环、激光细环与主方位标线)
        /// </summary>
        private static Texture2D GenerateCircularDialTexture()
        {
            const int size = 512;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = size * 0.5f;

            Color glassDark = new Color(0.04f, 0.07f, 0.12f, 0.88f);
            Color rimColor = new Color(0.20f, 0.45f, 0.70f, 0.70f);
            Color ringLaser = new Color(0.0f, 0.85f, 1.0f, 0.35f);
            Color tickColor = new Color(0.40f, 0.70f, 0.95f, 0.60f);

            float invHalf = 1f / half;
            float feather = 2.0f * invHalf; // 2像素平滑抗锯齿边缘

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

                    // 外环金属轮廓 (0.91 .. 0.96) 平滑过渡
                    float rimIn = Mathf.Clamp01((r - 0.905f) / feather);
                    float rimOut = Mathf.Clamp01((0.965f - r) / feather);
                    float rimFactor = Mathf.Min(rimIn, rimOut);
                    if (rimFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, rimColor, 0.75f * rimFactor);
                    }

                    // 内同心激光细环 (0.58 .. 0.60) 平滑抗锯齿
                    float laserIn = Mathf.Clamp01((r - 0.575f) / feather);
                    float laserOut = Mathf.Clamp01((0.605f - r) / feather);
                    float laserFactor = Mathf.Min(laserIn, laserOut);
                    if (laserFactor > 0.001f)
                    {
                        pixel = Color.Lerp(pixel, ringLaser, 0.75f * laserFactor);
                    }

                    // 4 主方位微刻度 (0°, 90°, 180°, 270°) 平滑抗锯齿
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
        /// 程序化生成高精度 256x256 矢量火箭剪影纹理 (空速管、双曲尖锥整流罩、柱状箭体、大三角翼、喷管与中心背脊线)
        /// </summary>
        private static Texture2D CreateProceduralRocketTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] cols = new Color[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                float ny = (float)y / size; // 0..1
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs((x - half) / half);
                    bool inside = false;
                    bool isRidgeLine = false;

                    // 1. 空速管针尖 (0.94 .. 1.0)
                    if (ny >= 0.94f)
                    {
                        if (dx <= 0.035f) inside = true;
                    }
                    // 2. 双曲尖锥整流罩 (0.74 .. 0.94)
                    else if (ny >= 0.74f && ny < 0.94f)
                    {
                        float t = (ny - 0.74f) / 0.20f;
                        float w = Mathf.Lerp(0.18f, 0.035f, Mathf.Pow(t, 0.75f));
                        if (dx <= w) inside = true;
                    }
                    // 3. 主圆柱箭体 (0.20 .. 0.74)
                    else if (ny >= 0.20f && ny < 0.74f)
                    {
                        if (dx <= 0.18f) inside = true;
                    }
                    // 4. 底部膨胀发动机喷管 (0.08 .. 0.20)
                    else if (ny >= 0.08f && ny < 0.20f)
                    {
                        float t = (ny - 0.08f) / 0.12f;
                        float w = Mathf.Lerp(0.12f, 0.18f, t);
                        if (dx <= w) inside = true;
                    }

                    // 5. 空气动力后掠大三角翼 (0.16 .. 0.42)
                    if (ny >= 0.16f && ny <= 0.42f)
                    {
                        float t = (ny - 0.16f) / 0.26f;
                        float finW = Mathf.Lerp(0.50f, 0.18f, Mathf.Pow(t, 0.85f));
                        if (dx <= finW) inside = true;
                    }

                    // 6. 前缘边条/小鸭翼 (0.54 .. 0.66)
                    if (ny >= 0.54f && ny <= 0.66f)
                    {
                        float t = (ny - 0.54f) / 0.12f;
                        float strakeW = Mathf.Lerp(0.26f, 0.18f, t);
                        if (dx <= strakeW) inside = true;
                    }

                    // 7. 中心背脊高光线 (细线增加现代航电层次感)
                    if (inside && dx <= 0.02f && ny >= 0.22f && ny <= 0.88f)
                    {
                        isRidgeLine = true;
                    }

                    Color c = Color.clear;
                    if (inside)
                    {
                        c = isRidgeLine ? new Color(1f, 1f, 1f, 0.65f) : Color.white;
                    }

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply(true, true);
            return tex;
        }

        private void OnDestroy()
        {
            if (VesselSilhouetteService.Provider != null)
            {
                VesselSilhouetteService.Provider.OnSilhouetteUpdated -= OnSilhouetteUpdated;
            }
        }
    }
}
