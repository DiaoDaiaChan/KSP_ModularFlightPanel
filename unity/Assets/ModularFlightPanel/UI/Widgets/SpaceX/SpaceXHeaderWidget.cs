using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.UI.Widgets.SpaceX
{
    public enum SpaceXSlotType
    {
        Readout,
        PhaseBadge,
        Timer
    }

    public class SpaceXSlotItem
    {
        public string Id;
        public SpaceXSlotType Type;
        public string Title;
        public string Token;
        public bool HasSeparator;
        public float CustomWidth;

        // UI nodes
        public GameObject Root;
        public Image BadgeBg;
        public Outline BadgeOutline;
        public Text TitleLabel;
        public Text ValueText;
        public Image SeparatorImage;
        public WidgetReadoutControl Control;

        // Dirty tracking
        public string LastValue = string.Empty;
        public string LastTitle = string.Empty;
        public double LastNumeric = double.NaN;
        public int LastSec = -1;
    }

    /// <summary>
    /// SpaceX 载人龙飞船/星舰全景动态任务遥测顶栏 (SpaceX Panoramic Dynamic Telemetry Banner)
    /// 支持动态槽位列表模式 (Dynamic Data Slots Flow Stack)：用户可自由增删列、调整顺序、控制分隔线，并装配 736+ 参数。
    /// 纯 UGUI 高对比度排版，遵循 MFP 规范，0 颜色字面量，分阶梯低开销刷新。
    /// </summary>
    [FlightWidget("spacex_header", "dragon_header", Category = WidgetCategory.SpaceX, DisplayName = "SpaceX 任务遥测顶栏", Description = "SpaceX 顶部贯通式航电状态栏：动态流式槽位架构，支持自由加减列、独立分割线与 736+ 参数灵活装配。", DefaultWidgetId = "spacex.header", DefaultX = 0f, DefaultY = 420f, IsSingleton = false, ExactIds = new[] { "spacex.header" })]
    public class SpaceXHeaderWidget : BaseFlightWidget, IDynamicSlotWidget, IAdaptiveSizeWidget
    {
        public override Vector2 BaseSize => new Vector2(960f, 42f);
        protected override bool AutoCreateCardFrame => true;
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Relaxed;

        // 声明式自适应物理尺寸契约接口 (IAdaptiveSizeWidget - 允许编辑模式自由拉动长宽比)
        public bool AllowNonUniformScale => true;
        public Vector2 MinBaseSize => new Vector2(480f, 36f);
        public Vector2 MaxBaseSize => new Vector2(2560f, 60f);

        private float _currentWidth = 960f;
        private float _currentHeight = 42f;

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            _currentWidth = pixelSize.x;
            _currentHeight = pixelSize.y;
            ApplyDynamicLayout();
        }

        // UI 视图容器
        private Image _bgImage;
        private Outline _outline;
        private Image _bottomAccentLine;
        private Transform _slotsContainer;

        // 动态槽位列表
        private readonly List<SpaceXSlotItem> _slots = new List<SpaceXSlotItem>();
        public IReadOnlyList<SpaceXSlotItem> Slots => _slots;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;

            // 1. 顶栏包围盒尺寸
            if (RectTransform.sizeDelta.x > 0.01f && RectTransform.sizeDelta.y > 0.01f)
            {
                _currentWidth = RectTransform.sizeDelta.x;
                _currentHeight = RectTransform.sizeDelta.y;
            }
            else
            {
                _currentWidth = 960f * s;
                _currentHeight = 42f * s;
                RectTransform.sizeDelta = new Vector2(_currentWidth, _currentHeight);
            }

            _bgImage = CardBackground;
            _outline = CardOutline;
            if (_outline != null)
                _outline.effectDistance = new Vector2(1f * s, 1f * s);

            // 底部电光青色细强调线 (1.5px)
            if (_bottomAccentLine == null)
            {
                GameObject lineObj = UIFactory.CreatePanel(transform, "BottomAccentLine", new Vector2(_currentWidth, 1.5f * s), new Vector2(0f, -_currentHeight * 0.5f + 0.75f * s), theme.AccentPrimary);
                _bottomAccentLine = lineObj.GetComponent<Image>();
            }

            // 槽位挂载根节点
            if (_slotsContainer == null)
            {
                GameObject containerGo = new GameObject("SlotsContainer", typeof(RectTransform));
                containerGo.transform.SetParent(transform, false);
                RectTransform cRt = containerGo.GetComponent<RectTransform>();
                cRt.anchorMin = Vector2.zero;
                cRt.anchorMax = Vector2.one;
                cRt.sizeDelta = Vector2.zero;
                cRt.anchoredPosition = Vector2.zero;
                _slotsContainer = containerGo.transform;
            }

            // 2. 加载槽位并构建 UI
            LoadSlotsFromConfig(config);
            BuildSlotsUI(theme);

            ApplyTheme(theme);
        }

        private void LoadSlotsFromConfig(WidgetConfig config)
        {
            _slots.Clear();
            string rawTemplate = config?.CustomTemplate;

            if (!string.IsNullOrEmpty(rawTemplate) && rawTemplate.IndexOf("SLOTS=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var dict = ParseChannelsIntoDictionary(rawTemplate);
                if (dict.TryGetValue("SLOTS", out string slotsStr) && !string.IsNullOrEmpty(slotsStr))
                {
                    string[] slotIds = slotsStr.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < slotIds.Length; i++)
                    {
                        string id = slotIds[i].Trim();
                        if (string.IsNullOrEmpty(id)) continue;

                        string typeStr = dict.TryGetValue($"SLOT_{id}_TYPE", out string tVal) ? tVal : "readout";
                        SpaceXSlotType type = SpaceXSlotType.Readout;
                        if (typeStr.Equals("phase", StringComparison.OrdinalIgnoreCase) || typeStr.Equals("phasebadge", StringComparison.OrdinalIgnoreCase))
                            type = SpaceXSlotType.PhaseBadge;
                        else if (typeStr.Equals("timer", StringComparison.OrdinalIgnoreCase))
                            type = SpaceXSlotType.Timer;

                        string title = dict.TryGetValue($"SLOT_{id}_TITLE", out string titVal) ? titVal : id;
                        string token = dict.TryGetValue($"SLOT_{id}_TOKEN", out string tokVal) ? tokVal : null;
                        bool hasSep = !dict.TryGetValue($"SLOT_{id}_SEP", out string sepVal) || sepVal != "0";
                        float customW = 0f;
                        if (dict.TryGetValue($"SLOT_{id}_W", out string wVal))
                        {
                            float.TryParse(wVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out customW);
                        }

                        _slots.Add(new SpaceXSlotItem
                        {
                            Id = id,
                            Type = type,
                            Title = title,
                            Token = token,
                            HasSeparator = hasSep,
                            CustomWidth = customW
                        });
                    }
                }
            }

            // 无自定义槽位配置时，回退到出厂默认经典 7 列布局 (100% 向后兼容)
            if (_slots.Count == 0)
            {
                InitDefaultSlots();
            }
        }

        private void InitDefaultSlots()
        {
            string pTitle = GetTemplateChannel("PHASE_LABEL", I18n.Tr("WIDGET_SPX_ACTIVE_PHASE", "活动段"));
            string tTitle = GetTemplateChannel("TIMER_LABEL", I18n.Tr("WIDGET_SPX_SPLASHDOWN_MET", "溅落 / 任务时间"));
            string vTitle = GetTemplateChannel("VEL_LABEL", I18n.Tr("WIDGET_SPX_INERTIAL_VELOCITY", "惯性速度"));
            string aTitle = GetTemplateChannel("ALT_LABEL", I18n.Tr("WIDGET_SPX_ALTITUDE", "高度"));
            string apTitle = GetTemplateChannel("AP_LABEL", I18n.Tr("WIDGET_SPX_APOGEE", "远地点"));
            string peTitle = GetTemplateChannel("PE_LABEL", I18n.Tr("WIDGET_SPX_PERIGEE", "近地点"));
            string iTitle = GetTemplateChannel("INC_LABEL", I18n.Tr("WIDGET_SPX_INCLINATION", "倾角"));

            _slots.Add(new SpaceXSlotItem { Id = "phase", Type = SpaceXSlotType.PhaseBadge, Title = pTitle, Token = "{SITUATION}", HasSeparator = true, CustomWidth = 170f });
            _slots.Add(new SpaceXSlotItem { Id = "timer", Type = SpaceXSlotType.Timer, Title = tTitle, Token = "{MET}", HasSeparator = true, CustomWidth = 120f });
            _slots.Add(new SpaceXSlotItem { Id = "vel", Type = SpaceXSlotType.Readout, Title = vTitle, Token = GetTemplateChannel("VEL_TOKEN", "{SPD}"), HasSeparator = true, CustomWidth = 110f });
            _slots.Add(new SpaceXSlotItem { Id = "alt", Type = SpaceXSlotType.Readout, Title = aTitle, Token = GetTemplateChannel("ALT_TOKEN", "{ALT:ASL:DIST}"), HasSeparator = true, CustomWidth = 100f });
            _slots.Add(new SpaceXSlotItem { Id = "ap", Type = SpaceXSlotType.Readout, Title = apTitle, Token = GetTemplateChannel("AP_TOKEN", "{AP:DIST}"), HasSeparator = true, CustomWidth = 100f });
            _slots.Add(new SpaceXSlotItem { Id = "pe", Type = SpaceXSlotType.Readout, Title = peTitle, Token = GetTemplateChannel("PE_TOKEN", "{PE:DIST}"), HasSeparator = true, CustomWidth = 100f });
            _slots.Add(new SpaceXSlotItem { Id = "inc", Type = SpaceXSlotType.Readout, Title = iTitle, Token = GetTemplateChannel("INC_TOKEN", "{INC}"), HasSeparator = false, CustomWidth = 90f });
        }

        private static Dictionary<string, string> ParseChannelsIntoDictionary(string template)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return dict;

            string[] pairs = template.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i].Trim();
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string v = (eq < p.Length - 1) ? p.Substring(eq + 1).Trim() : string.Empty;
                    dict[k] = v;
                }
            }
            return dict;
        }

        private void BuildSlotsUI(ThemeConfig theme)
        {
            if (_slotsContainer == null) return;

            // 1. 清理旧节点与微控件
            for (int i = _slotsContainer.childCount - 1; i >= 0; i--)
            {
                var child = _slotsContainer.GetChild(i);
                if (child != null)
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }
            this.Controls.UnregisterAll();

            // 重新注册卡片底板与装饰线
            if (_bgImage != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "card_bg", "Header Bar Background", _bgImage.gameObject, "SpaceX顶栏全景面板底板", t => ApplyCard(_bgImage, _outline, CardStyleRole.Normal, t)));
            }
            if (_bottomAccentLine != null)
            {
                this.Controls.Register(WidgetControlManager.WrapElement(this, "accent_line", "Bottom Accent Line", _bottomAccentLine.gameObject, "底部电光青色细强调线", t => { if (_bottomAccentLine != null) _bottomAccentLine.color = t.AccentPrimary; }));
            }

            float s = CurrentDpiScale;
            float panelW = _currentWidth > 0.01f ? _currentWidth : 960f * s;
            float panelH = _currentHeight > 0.01f ? _currentHeight : 42f * s;
            float pad = 16f * s;
            float availW = Mathf.Max(60f * s, panelW - pad * 2f);

            int n = _slots.Count;
            if (n == 0) return;

            float sepSpacing = 12f * s;
            float[] widths = new float[n];
            float fixedSum = 0f;
            int flexCount = 0;
            float totalSepWidth = 0f;

            for (int i = 0; i < n; i++)
            {
                if (_slots[i].HasSeparator && i < n - 1)
                {
                    totalSepWidth += sepSpacing;
                }
                if (_slots[i].CustomWidth > 0f)
                {
                    widths[i] = _slots[i].CustomWidth * s;
                    fixedSum += widths[i];
                }
                else
                {
                    flexCount++;
                }
            }

            float remainingForFlex = Mathf.Max(0f, availW - fixedSum - totalSepWidth);
            float flexW = flexCount > 0 ? (remainingForFlex / flexCount) : (100f * s);
            if (flexW < 50f * s) flexW = 50f * s;

            for (int i = 0; i < n; i++)
            {
                if (_slots[i].CustomWidth <= 0f)
                {
                    widths[i] = flexW;
                }
            }

            // 2. 从左至右构建图元
            float curX = -panelW * 0.5f + pad;
            for (int i = 0; i < n; i++)
            {
                var slot = _slots[i];
                float w = widths[i];
                float colCenterX = curX + w * 0.5f;

                if (slot.Type == SpaceXSlotType.PhaseBadge)
                {
                    CreatePhaseBadge(_slotsContainer, slot, colCenterX, w, s, theme);
                }
                else
                {
                    CreateReadoutColumn(_slotsContainer, slot, colCenterX, w, s, theme);
                }

                curX += w;

                // 垂直微光分割线
                if (slot.HasSeparator && i < n - 1)
                {
                    float sepX = curX + sepSpacing * 0.5f;
                    slot.SeparatorImage = CreateSeparator(_slotsContainer, sepX, panelH, s, theme);
                    curX += sepSpacing;
                }
                else
                {
                    slot.SeparatorImage = null;
                }

                // 注册进标准化微控件治理体系
                slot.Control = new WidgetReadoutControl(
                    slot.Id,
                    slot.Title ?? slot.Id,
                    slot.Root,
                    slot.ValueText,
                    slot.TitleLabel,
                    slot.Type == SpaceXSlotType.PhaseBadge ? TextStyleRole.Accent : TextStyleRole.PrimaryValue,
                    slot.Token ?? "{ALT:ASL:DIST}"
                );
                this.Controls.Register(slot.Control);
            }

            ApplyDynamicLayout();

            this.Controls.BindConfigToControls(Config);
            this.Controls.ApplyThemeToControls(theme);
        }

        public void ApplyDynamicLayout()
        {
            float s = CurrentDpiScale;
            float w = _currentWidth > 0.01f ? _currentWidth : 960f * s;
            float h = _currentHeight > 0.01f ? _currentHeight : 42f * s;

            // 1. 调整底部细线与尺寸
            if (_bottomAccentLine != null)
            {
                RectTransform lineRt = _bottomAccentLine.rectTransform;
                lineRt.sizeDelta = new Vector2(w, 1.5f * s);
                lineRt.anchoredPosition = new Vector2(0f, -h * 0.5f + 0.75f * s);
            }

            int n = _slots.Count;
            if (n == 0) return;

            float pad = 16f * s;
            float availW = Mathf.Max(60f * s, w - pad * 2f);
            float sepSpacing = 12f * s;
            float[] widths = new float[n];
            float fixedSum = 0f;
            int flexCount = 0;
            float totalSepWidth = 0f;

            for (int i = 0; i < n; i++)
            {
                if (_slots[i].HasSeparator && i < n - 1)
                {
                    totalSepWidth += sepSpacing;
                }
                if (_slots[i].CustomWidth > 0f)
                {
                    widths[i] = _slots[i].CustomWidth * s;
                    fixedSum += widths[i];
                }
                else
                {
                    flexCount++;
                }
            }

            float remainingForFlex = Mathf.Max(0f, availW - fixedSum - totalSepWidth);
            float flexW = flexCount > 0 ? (remainingForFlex / flexCount) : (100f * s);
            if (flexW < 50f * s) flexW = 50f * s;

            for (int i = 0; i < n; i++)
            {
                if (_slots[i].CustomWidth <= 0f)
                {
                    widths[i] = flexW;
                }
            }

            // 2. 依序应用坐标与尺寸
            float curX = -w * 0.5f + pad;
            for (int i = 0; i < n; i++)
            {
                var slot = _slots[i];
                float colW = widths[i];
                float colCenterX = curX + colW * 0.5f;

                if (slot.Root != null)
                {
                    RectTransform rt = slot.Root.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(colW, slot.Type == SpaceXSlotType.PhaseBadge ? 30f * s : h);
                        rt.anchoredPosition = new Vector2(colCenterX, 0f);
                    }
                }

                curX += colW;

                if (slot.SeparatorImage != null)
                {
                    RectTransform sepRt = slot.SeparatorImage.rectTransform;
                    float sepX = curX + sepSpacing * 0.5f;
                    sepRt.sizeDelta = new Vector2(1f * s, h * 0.55f);
                    sepRt.anchoredPosition = new Vector2(sepX, 0f);
                    curX += sepSpacing;
                }
            }
        }

        private void CreatePhaseBadge(Transform parent, SpaceXSlotItem slot, float centerX, float width, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject badgeGo = UIFactory.CreatePanel(parent, $"PhaseBadgeBg_{slot.Id}", new Vector2(width, 30f * s), new Vector2(centerX, 0f), style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme));
            slot.Root = badgeGo;
            slot.BadgeBg = badgeGo.GetComponent<Image>();
            slot.BadgeOutline = badgeGo.AddComponent<Outline>();
            slot.BadgeOutline.effectDistance = new Vector2(1f * s, 1f * s);
            slot.BadgeOutline.effectColor = style.GetLineColor(theme.AccentSecondary, LineWeight.Subtle, theme);

            slot.TitleLabel = UIFactory.CreateText(badgeGo.transform, $"{slot.Id}_Label", slot.Title ?? I18n.Tr("WIDGET_SPX_ACTIVE_PHASE", "活动段"), Mathf.RoundToInt(8f * s), TextAnchor.UpperLeft, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform plRt = slot.TitleLabel.rectTransform;
            plRt.anchorMin = new Vector2(0f, 0.5f);
            plRt.anchorMax = new Vector2(1f, 1f);
            plRt.anchoredPosition = new Vector2(8f * s, -2f * s);
            plRt.sizeDelta = new Vector2(-16f * s, 0f);

            slot.ValueText = UIFactory.CreateText(badgeGo.transform, $"{slot.Id}_Value", I18n.Tr("WIDGET_SPX_ORBITAL_COAST", "轨道滑行"), Mathf.RoundToInt(11f * s), TextAnchor.LowerLeft, style.GetTextColor(TextStyleRole.Accent, theme));
            RectTransform pvRt = slot.ValueText.rectTransform;
            pvRt.anchorMin = new Vector2(0f, 0f);
            pvRt.anchorMax = new Vector2(1f, 0.65f);
            pvRt.anchoredPosition = new Vector2(8f * s, 3f * s);
            pvRt.sizeDelta = new Vector2(-16f * s, 0f);
        }

        private void CreateReadoutColumn(Transform parent, SpaceXSlotItem slot, float centerX, float width, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject colGo = new GameObject($"Col_{slot.Id}", typeof(RectTransform));
            colGo.transform.SetParent(parent, false);
            RectTransform colRt = colGo.GetComponent<RectTransform>();
            colRt.anchorMin = new Vector2(0.5f, 0.5f);
            colRt.anchorMax = new Vector2(0.5f, 0.5f);
            colRt.pivot = new Vector2(0.5f, 0.5f);
            colRt.anchoredPosition = new Vector2(centerX, 0f);
            colRt.sizeDelta = new Vector2(width, 42f * s);
            slot.Root = colGo;

            slot.TitleLabel = UIFactory.CreateText(colGo.transform, $"{slot.Id}_Label", slot.Title ?? "", Mathf.RoundToInt(8f * s), TextAnchor.UpperCenter, style.GetTextColor(TextStyleRole.Label, theme));
            RectTransform lRt = slot.TitleLabel.rectTransform;
            lRt.sizeDelta = new Vector2(width, 14f * s);
            lRt.anchoredPosition = new Vector2(0f, 8f * s);

            int valFontSize = width < 75f * s ? Mathf.RoundToInt(10f * s) : Mathf.RoundToInt(12f * s);
            slot.ValueText = UIFactory.CreateText(colGo.transform, $"{slot.Id}_Value", "--", valFontSize, TextAnchor.LowerCenter, style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            RectTransform vRt = slot.ValueText.rectTransform;
            vRt.sizeDelta = new Vector2(width, 18f * s);
            vRt.anchoredPosition = new Vector2(0f, -6f * s);
        }

        private Image CreateSeparator(Transform parent, float posX, float height, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject sepGo = UIFactory.CreatePanel(parent, "Separator", new Vector2(1f * s, height * 0.55f), new Vector2(posX, 0f), style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            return sepGo.GetComponent<Image>();
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.ValueText == null) continue;

                if (slot.Type == SpaceXSlotType.PhaseBadge)
                {
                    string phase;
                    if (string.IsNullOrEmpty(slot.Token) || slot.Token == "{SITUATION}")
                    {
                        phase = InferFlightPhase(telemetry);
                    }
                    else
                    {
                        phase = TelemetryTokenEngine.Evaluate(slot.Token, telemetry);
                    }

                    if (phase != slot.LastValue)
                    {
                        slot.LastValue = phase;
                        slot.ValueText.text = phase;
                    }
                }
                else if (slot.Type == SpaceXSlotType.Timer)
                {
                    if (string.IsNullOrEmpty(slot.Token) || slot.Token == "{MET}")
                    {
                        bool isNode = telemetry.HasManeuverNode && telemetry.ManeuverTimeToNode > 0;
                        int curSec = isNode ? (int)telemetry.ManeuverTimeToNode : (int)telemetry.MissionTime;
                        string tLbl = isNode ? "TIME TO NODE" : (slot.Title ?? "MET");

                        if (tLbl != slot.LastTitle && slot.TitleLabel != null)
                        {
                            slot.LastTitle = tLbl;
                            slot.TitleLabel.text = tLbl;
                        }

                        if (curSec != slot.LastSec)
                        {
                            slot.LastSec = curSec;
                            string timer = isNode ? ("T-" + FormatDuration(telemetry.ManeuverTimeToNode)) : ("MET " + FormatDuration(telemetry.MissionTime));
                            slot.LastValue = timer;
                            slot.ValueText.text = timer;
                        }
                    }
                    else
                    {
                        string timer = TelemetryTokenEngine.Evaluate(slot.Token, telemetry);
                        if (timer != slot.LastValue)
                        {
                            slot.LastValue = timer;
                            slot.ValueText.text = timer;
                        }
                    }
                }
                else
                {
                    if (slot.Id == "vel" && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{SPD}"))
                    {
                        double spd = telemetry.OrbitalSpeed > 10.0 ? telemetry.OrbitalSpeed : telemetry.CurrentSpeed;
                        if (double.IsNaN(slot.LastNumeric) || Math.Abs(spd - slot.LastNumeric) >= 0.5)
                        {
                            slot.LastNumeric = spd;
                            string val = FormatMetricSpeed(spd);
                            if (val != slot.LastValue)
                            {
                                slot.LastValue = val;
                                slot.ValueText.text = val;
                            }
                        }
                    }
                    else if (slot.Id == "inc" && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{INC}"))
                    {
                        double inc = telemetry.Inclination;
                        if (double.IsNaN(slot.LastNumeric) || Math.Abs(inc - slot.LastNumeric) >= 0.05)
                        {
                            slot.LastNumeric = inc;
                            string val = $"{inc:F2}°";
                            if (val != slot.LastValue)
                            {
                                slot.LastValue = val;
                                slot.ValueText.text = val;
                            }
                        }
                    }
                    else
                    {
                        string tok = !string.IsNullOrEmpty(slot.Token) ? slot.Token : "{ALT:ASL:DIST}";
                        string val = TelemetryTokenEngine.Evaluate(tok, telemetry);
                        if (val != slot.LastValue)
                        {
                            slot.LastValue = val;
                            slot.ValueText.text = val;
                        }
                    }
                }
            }
        }

        private static string InferFlightPhase(IFlightTelemetry t)
        {
            if (t.FlightSituation == "PRELAUNCH" || (t.CurrentSpeed < 1.0 && t.AltitudeASL < 150.0))
                return "PAD HOLD";

            if (t.FlightSituation == "SPLASHED")
                return "SPLASHDOWN NOMINAL";

            if (t.FlightSituation == "LANDED")
                return "TOUCHDOWN NOMINAL";

            if (t.IsTouchdownAlert)
                return "TERMINAL DESCENT";

            if (t.IsStageSeparating)
                return "STAGE SEPARATION";

            if (t.IsEngineIgniting)
                return "IGNITION SEQUENCE";

            if (t.IsDockingMode)
                return t.HasTarget && t.TargetDistance < 50.0 ? "DOCKING FINAL" : "DOCKING APPROACH";

            double atmDepth = t.HasAtmosphere ? t.AtmosphereDepth : 0.0;

            if (t.HasAtmosphere && t.AltitudeASL < atmDepth)
            {
                if (t.VerticalSpeed < -50.0) return "REENTRY ENTRY";
                if (t.Mach >= 0.8 && t.Mach <= 1.3) return "TRANSONIC PASS";
                if (t.VerticalSpeed > 10.0) return "ASCENT POWERED";
                if (t.AltitudeAGL < 400.0 && t.VerticalSpeed < -2.0) return "CHUTE DESCENT";
            }

            if (t.Periapsis > atmDepth && t.Eccentricity < 1.0)
            {
                if (t.HasManeuverNode) return "APPROACH / BURN";
                return "ORBITAL COAST";
            }

            if (t.HasManeuverNode) return "MANEUVER BURN";
            return "SUBORBITAL FLIGHT";
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            base.ApplyTheme(theme);
            WidgetStyleManager style = WidgetStyleManager.Instance;

            this.Controls.ApplyThemeToControls(theme);

            ApplyCard(_bgImage, _outline, CardStyleRole.Normal, theme);
            if (_bottomAccentLine != null) _bottomAccentLine.color = theme.AccentPrimary;

            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.BadgeBg != null) s.BadgeBg.color = style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme);
                if (s.BadgeOutline != null) s.BadgeOutline.effectColor = style.GetLineColor(theme.AccentSecondary, LineWeight.Subtle, theme);
                if (s.TitleLabel != null) ApplyText(s.TitleLabel, TextStyleRole.Label, theme);
                if (s.ValueText != null)
                {
                    ApplyText(s.ValueText, s.Type == SpaceXSlotType.PhaseBadge ? TextStyleRole.Accent : TextStyleRole.PrimaryValue, theme);
                }
                if (s.SeparatorImage != null)
                {
                    s.SeparatorImage.color = style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme);
                }
            }
        }

        // ==========================================
        // 动态槽位管理公共 API (支持所见即所得编辑)
        // ==========================================

        public string SerializeSlots()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("SLOTS=");
            for (int i = 0; i < _slots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_slots[i].Id);
            }
            sb.Append(';');

            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                sb.Append($"SLOT_{s.Id}_TYPE=").Append(s.Type.ToString().ToLowerInvariant()).Append(';');
                if (!string.IsNullOrEmpty(s.Title)) sb.Append($"SLOT_{s.Id}_TITLE=").Append(s.Title).Append(';');
                if (!string.IsNullOrEmpty(s.Token)) sb.Append($"SLOT_{s.Id}_TOKEN=").Append(s.Token).Append(';');
                sb.Append($"SLOT_{s.Id}_SEP=").Append(s.HasSeparator ? "1" : "0").Append(';');
                if (s.CustomWidth > 0f) sb.Append($"SLOT_{s.Id}_W=").Append(s.CustomWidth.ToString("F0")).Append(';');
            }
            return sb.ToString();
        }

        public void RebuildDynamicSlots(ThemeConfig theme = null)
        {
            theme = theme ?? WidgetStyleManager.ResolveTheme(null);
            BuildSlotsUI(theme);
            ApplyTheme(theme);
        }

        private void SaveAndRebuild()
        {
            if (Config != null)
            {
                Config.CustomTemplate = SerializeSlots();
            }
            RebuildDynamicSlots();
            WidgetLayoutManager.Instance?.SaveLayout();
        }

        public void AddSlot(string token, string title = null)
        {
            var meta = TelemetryCatalog.FindByToken(token);
            if (string.IsNullOrEmpty(title))
            {
                title = meta != null ? meta.DisplayName : token;
            }

            string id = "col_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            _slots.Add(new SpaceXSlotItem
            {
                Id = id,
                Type = SpaceXSlotType.Readout,
                Title = title,
                Token = token,
                HasSeparator = true,
                CustomWidth = 0f
            });

            SaveAndRebuild();
        }

        public void RemoveSlot(int index)
        {
            if (index >= 0 && index < _slots.Count)
            {
                _slots.RemoveAt(index);
                SaveAndRebuild();
            }
        }

        public void MoveSlot(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && fromIndex < _slots.Count && toIndex >= 0 && toIndex < _slots.Count && fromIndex != toIndex)
            {
                var item = _slots[fromIndex];
                _slots.RemoveAt(fromIndex);
                _slots.Insert(toIndex, item);
                SaveAndRebuild();
            }
        }

        public void ToggleSlotSeparator(int index)
        {
            if (index >= 0 && index < _slots.Count)
            {
                _slots[index].HasSeparator = !_slots[index].HasSeparator;
                SaveAndRebuild();
            }
        }

        public void UpdateSlotToken(int index, string newToken)
        {
            if (index >= 0 && index < _slots.Count)
            {
                _slots[index].Token = newToken;
                _slots[index].LastValue = string.Empty;
                SaveAndRebuild();
            }
        }

        public void UpdateSlotTitle(int index, string newTitle)
        {
            if (index >= 0 && index < _slots.Count)
            {
                _slots[index].Title = newTitle;
                if (_slots[index].TitleLabel != null)
                {
                    _slots[index].TitleLabel.text = newTitle;
                }
                if (Config != null)
                {
                    Config.CustomTemplate = SerializeSlots();
                }
                WidgetLayoutManager.Instance?.SaveLayout();
            }
        }

        public void ResetToDefaultSlots()
        {
            _slots.Clear();
            InitDefaultSlots();
            if (Config != null)
            {
                Config.CustomTemplate = string.Empty;
            }
            RebuildDynamicSlots();
            WidgetLayoutManager.Instance?.SaveLayout();
        }

        // ==========================================
        // IDynamicSlotWidget 契约接口显式实现
        // ==========================================
        public string SlotOrchestratorTitle => "SpaceX 顶栏动态槽位: 可自由加减数据列、调整顺序与分隔线";

        private readonly List<DynamicSlotDescriptor> _cachedDescriptors = new List<DynamicSlotDescriptor>();
        public IReadOnlyList<DynamicSlotDescriptor> DynamicSlots
        {
            get
            {
                _cachedDescriptors.Clear();
                for (int i = 0; i < _slots.Count; i++)
                {
                    var s = _slots[i];
                    _cachedDescriptors.Add(new DynamicSlotDescriptor(s.Id, s.Title, s.Token, s.HasSeparator, s.CustomWidth, s.Type.ToString().ToLowerInvariant()));
                }
                return _cachedDescriptors;
            }
        }

        public void AddDynamicSlot(string token, string title = null) => AddSlot(token, title);
        public void RemoveDynamicSlot(int index) => RemoveSlot(index);
        public void MoveDynamicSlot(int fromIndex, int toIndex) => MoveSlot(fromIndex, toIndex);
        public void ToggleDynamicSlotSeparator(int index) => ToggleSlotSeparator(index);
        public void UpdateDynamicSlotToken(int index, string newToken) => UpdateSlotToken(index, newToken);
        public void UpdateDynamicSlotTitle(int index, string newTitle) => UpdateSlotTitle(index, newTitle);
        public void ResetToDefaultDynamicSlots() => ResetToDefaultSlots();

        protected override void OnDestroy()
        {
            this.Controls.UnregisterAll();
            base.OnDestroy();
        }
    }
}
