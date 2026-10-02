using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.Core.Diagnostics;
using ModularFlightPanel.Core.Telemetry;
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
    /// 单个槽位渲染快照 (0 GC, SPEC-012)
    /// </summary>
    public struct SpaceXHeaderSlotState : IEquatable<SpaceXHeaderSlotState>
    {
        public string Id;
        public string Title;
        public string Value;

        public bool Equals(SpaceXHeaderSlotState other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   string.Equals(Title, other.Title, StringComparison.Ordinal) &&
                   string.Equals(Value, other.Value, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// SpaceX 顶栏遥测零-GC快照 (MFP-SPEC-012)
    /// </summary>
    public struct SpaceXHeaderState : IEquatable<SpaceXHeaderState>
    {
        public bool HasVessel;
        public int SlotCount;
        public SpaceXHeaderSlotState[] Slots;

        public bool Equals(SpaceXHeaderState other)
        {
            if (HasVessel != other.HasVessel || SlotCount != other.SlotCount) return false;
            if (Slots == null && other.Slots == null) return true;
            if (Slots == null || other.Slots == null) return false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Slots[i].Equals(other.Slots[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is SpaceXHeaderState other && Equals(other);
        public override int GetHashCode() => (HasVessel, SlotCount).GetHashCode();
    }

    public struct SpaceXHeaderSlotConfig
    {
        public string Id;
        public SpaceXSlotType Type;
        public string Title;
        public string Token;
    }

    /// <summary>
    /// SpaceX 顶栏遥测业务解耦大脑 (MFP-SPEC-012)
    /// </summary>
    public class SpaceXHeaderLogic : WidgetLogic<SpaceXHeaderState>
    {
        private readonly List<SpaceXHeaderSlotConfig> _configuredSlots = new List<SpaceXHeaderSlotConfig>();
        private SpaceXHeaderSlotState[] _slotsBuffer = new SpaceXHeaderSlotState[32];

        public void SyncSlots(IReadOnlyList<SpaceXSlotItem> slots)
        {
            _configuredSlots.Clear();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    _configuredSlots.Add(new SpaceXHeaderSlotConfig
                    {
                        Id = s.Id,
                        Type = s.Type,
                        Title = s.Title,
                        Token = s.Token
                    });
                }
            }
        }

        public override void Reset()
        {
            CurrentState = default;
        }

        public override void Evaluate(IFlightTelemetry telemetry, float deltaTime)
        {
            if (telemetry == null || !telemetry.HasVessel)
            {
                CurrentState = default;
                return;
            }

            int count = _configuredSlots.Count;
            if (_slotsBuffer.Length < count)
            {
                _slotsBuffer = new SpaceXHeaderSlotState[Math.Max(count, _slotsBuffer.Length * 2)];
            }

            for (int i = 0; i < count; i++)
            {
                var slot = _configuredSlots[i];
                string title = slot.Title;
                string val = string.Empty;

                if (slot.Type == SpaceXSlotType.PhaseBadge)
                {
                    if (string.IsNullOrEmpty(slot.Token) || slot.Token == "{SITUATION}")
                    {
                        val = InferFlightPhase(telemetry);
                    }
                    else
                    {
                        val = TelemetryTokenEngine.Evaluate(slot.Token, telemetry);
                    }
                }
                else if (slot.Type == SpaceXSlotType.Timer)
                {
                    if (string.IsNullOrEmpty(slot.Token) || slot.Token == "{MET}")
                    {
                        bool isNode = telemetry.HasManeuverNode && telemetry.ManeuverTimeToNode > 0;
                        string tLbl = isNode ? "TIME TO NODE" : (slot.Title ?? "MET");
                        title = tLbl;
                        val = isNode ? ("T-" + AvionicsFormatting.FormatDuration(telemetry.ManeuverTimeToNode)) : ("MET " + AvionicsFormatting.FormatDuration(telemetry.MissionTime));
                    }
                    else
                    {
                        val = TelemetryTokenEngine.Evaluate(slot.Token, telemetry);
                    }
                }
                else
                {
                    if (slot.Id == "vel" && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{SPD}"))
                    {
                        double spd = telemetry.OrbitalSpeed > 10.0 ? telemetry.OrbitalSpeed : telemetry.CurrentSpeed;
                        val = AvionicsFormatting.FormatMetricSpeed(spd);
                    }
                    else if (slot.Id == "alt" && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{ALT:ASL:DIST}"))
                    {
                        val = AvionicsFormatting.FormatMetricDistance(telemetry.AltitudeASL);
                    }
                    else if ((slot.Id == "ap" || slot.Id == "apo") && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{AP:DIST}"))
                    {
                        val = AvionicsFormatting.FormatMetricDistance(telemetry.Apoapsis);
                    }
                    else if ((slot.Id == "pe" || slot.Id == "peri") && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{PE:DIST}"))
                    {
                        double pe = telemetry.Periapsis;
                        if (pe < -100000.0) val = "IMPACT";
                        else val = AvionicsFormatting.FormatMetricDistance(pe);
                    }
                    else if (slot.Id == "inc" && (string.IsNullOrEmpty(slot.Token) || slot.Token == "{INC}"))
                    {
                        val = $"{telemetry.Inclination:F2}°";
                    }
                    else
                    {
                        string tok = !string.IsNullOrEmpty(slot.Token) ? slot.Token : "{ALT:ASL:DIST}";
                        val = TelemetryTokenEngine.Evaluate(tok, telemetry);
                    }
                }

                _slotsBuffer[i] = new SpaceXHeaderSlotState
                {
                    Id = slot.Id,
                    Title = title,
                    Value = val
                };
            }

            CurrentState = new SpaceXHeaderState
            {
                HasVessel = true,
                SlotCount = count,
                Slots = _slotsBuffer
            };
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

        // UI 视图容器
        private Image _bgImage;
        private Outline _outline;
        private Image _bottomAccentLine;
        private Transform _slotsContainer;

        // 动态槽位列表
        private readonly List<SpaceXSlotItem> _slots = new List<SpaceXSlotItem>();
        public IReadOnlyList<SpaceXSlotItem> Slots => _slots;

        // 业务大脑 (MFP-SPEC-012)
        private readonly SpaceXHeaderLogic _logic = new SpaceXHeaderLogic();
        protected override IWidgetLogic LogicCore => _logic;

        // ── 智能私有缓存与脏检查 (MFP-SPEC-009) ──
        private readonly Cached<float> _cachedWidth = new Cached<float>(960f);
        private readonly Cached<float> _cachedHeight = new Cached<float>(42f);
        private readonly Cached<SpaceXHeaderState> _cachedState = new Cached<SpaceXHeaderState>();

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
                RectTransform cRt = CreateContainer("SlotsContainer", transform);
                cRt.anchorMin = Vector2.zero;
                cRt.anchorMax = Vector2.one;
                cRt.sizeDelta = Vector2.zero;
                cRt.anchoredPosition = Vector2.zero;
                _slotsContainer = cRt;
            }

            // 2. 加载槽位并构建 UI
            LoadSlotsFromConfig(config);
            BuildSlotsUI(theme);

            _logic.SyncSlots(_slots);

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

                slot.Control = new WidgetReadoutControl(
                    slot.Id,
                    slot.Title ?? slot.Id,
                    slot.Root,
                    slot.ValueText,
                    slot.TitleLabel,
                    slot.Type == SpaceXSlotType.PhaseBadge ? TextStyleRole.Accent : TextStyleRole.PrimaryValue,
                    null
                );
                this.Controls.Register(slot.Control);
            }

            ApplyDynamicLayout();

            _logic.SyncSlots(_slots);

            this.Controls.BindConfigToControls(Config);
            this.Controls.ApplyThemeToControls(theme);
        }

        public void OnAdaptiveResize(Vector2 pixelSize)
        {
            _currentWidth = pixelSize.x;
            _currentHeight = pixelSize.y;
            ApplyDynamicLayout();
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

            // 2. 刷新布局中各列位置
            float curX = -w * 0.5f + pad;
            for (int i = 0; i < n; i++)
            {
                var slot = _slots[i];
                float colW = widths[i];
                float colCenterX = curX + colW * 0.5f;

                if (slot.Root != null)
                {
                    RectTransform rRt = slot.Root.GetComponent<RectTransform>();
                    if (rRt != null)
                    {
                        rRt.anchoredPosition = new Vector2(colCenterX, 0f);
                        rRt.sizeDelta = new Vector2(colW, h);
                    }
                }

                if (slot.BadgeBg != null)
                {
                    slot.BadgeBg.rectTransform.sizeDelta = new Vector2(colW - 8f * s, 26f * s);
                }

                if (slot.TitleLabel != null)
                {
                    slot.TitleLabel.rectTransform.sizeDelta = new Vector2(colW, 14f * s);
                }

                if (slot.ValueText != null)
                {
                    slot.ValueText.rectTransform.sizeDelta = new Vector2(colW, 20f * s);
                }

                curX += colW;

                if (slot.SeparatorImage != null && i < n - 1)
                {
                    float sepX = curX + sepSpacing * 0.5f;
                    slot.SeparatorImage.rectTransform.anchoredPosition = new Vector2(sepX, 0f);
                    slot.SeparatorImage.rectTransform.sizeDelta = new Vector2(1f * s, h * 0.55f);
                    curX += sepSpacing;
                }
            }
        }

        private void CreatePhaseBadge(Transform parent, SpaceXSlotItem slot, float centerX, float width, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            RectTransform rRt = CreateContainer($"Slot_{slot.Id}", parent, new Vector2(width, _currentHeight), new Vector2(centerX, 0f));
            GameObject root = rRt.gameObject;
            slot.Root = root;

            // 药丸微标背景
            GameObject badgeObj = UIFactory.CreatePanel(root.transform, "Phase_Badge", new Vector2(width - 8f * s, 26f * s), new Vector2(0f, 0f),
                style.GetSurfaceColor(SurfaceStyleRole.SlotActive, theme));
            slot.BadgeBg = badgeObj.GetComponent<Image>();

            Outline outline = badgeObj.AddComponent<Outline>();
            outline.effectColor = style.GetLineColor(theme.AccentSecondary, LineWeight.Subtle, theme);
            outline.effectDistance = new Vector2(1f * s, 1f * s);
            slot.BadgeOutline = outline;

            // 标题微标
            slot.TitleLabel = UIFactory.CreateText(badgeObj.transform, "Title", slot.Title ?? "PHASE", Mathf.RoundToInt(6.5f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            slot.TitleLabel.fontStyle = FontStyle.Bold;
            RectTransform titRt = slot.TitleLabel.rectTransform;
            titRt.sizeDelta = new Vector2(width - 8f * s, 10f * s);
            titRt.anchoredPosition = new Vector2(0f, 6.5f * s);

            // 状态值
            slot.ValueText = UIFactory.CreateText(badgeObj.transform, "Value", "---", Mathf.RoundToInt(10.5f * s), TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.Accent, theme));
            slot.ValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = slot.ValueText.rectTransform;
            valRt.sizeDelta = new Vector2(width - 8f * s, 16f * s);
            valRt.anchoredPosition = new Vector2(0f, -4f * s);
        }

        private void CreateReadoutColumn(Transform parent, SpaceXSlotItem slot, float centerX, float width, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            RectTransform rRt = CreateContainer($"Slot_{slot.Id}", parent, new Vector2(width, _currentHeight), new Vector2(centerX, 0f));
            GameObject root = rRt.gameObject;
            slot.Root = root;

            slot.TitleLabel = UIFactory.CreateText(root.transform, "Title", slot.Title ?? slot.Id.ToUpperInvariant(), Mathf.RoundToInt(7.5f * s), TextAnchor.UpperCenter,
                style.GetTextColor(TextStyleRole.Label, theme));
            slot.TitleLabel.fontStyle = FontStyle.Bold;
            RectTransform titRt = slot.TitleLabel.rectTransform;
            titRt.sizeDelta = new Vector2(width, 14f * s);
            titRt.anchoredPosition = new Vector2(0f, 9f * s);

            int valFontSize = Mathf.RoundToInt((slot.Type == SpaceXSlotType.Timer ? 13f : 14.5f) * s);
            slot.ValueText = UIFactory.CreateText(root.transform, "Value", "---", valFontSize, TextAnchor.LowerCenter,
                style.GetTextColor(TextStyleRole.PrimaryValue, theme));
            slot.ValueText.fontStyle = FontStyle.Bold;
            RectTransform valRt = slot.ValueText.rectTransform;
            valRt.sizeDelta = new Vector2(width, 20f * s);
            valRt.anchoredPosition = new Vector2(0f, -7f * s);
        }

        private Image CreateSeparator(Transform parent, float x, float height, float s, ThemeConfig theme)
        {
            WidgetStyleManager style = WidgetStyleManager.Instance;
            GameObject sepObj = UIFactory.CreatePanel(parent, "Sep", new Vector2(1f * s, height * 0.55f), new Vector2(x, 0f),
                style.GetLineColor(theme.FrameBgColor, LineWeight.Faint, theme));
            return sepObj.GetComponent<Image>();
        }

        public override void OnDataHeartBeat(in FlightHeartbeatContext context)
        {
            base.OnDataHeartBeat(in context);
        }

        public override void OnUIDrawLoop(ref FlightUIDrawContext context)
        {
            base.OnUIDrawLoop(ref context);
            SpaceXHeaderState state = _logic.CurrentState;
            if (!state.HasVessel) return;

            for (int i = 0; i < state.SlotCount && i < _slots.Count; i++)
            {
                var slot = _slots[i];
                ref var slotState = ref state.Slots[i];

                if (slot.TitleLabel != null && slotState.Title != null && slotState.Title != slot.LastTitle)
                {
                    slot.LastTitle = slotState.Title;
                    slot.TitleLabel.SetTextSafe(slotState.Title);
                }

                if (slotState.Value != null && slotState.Value != slot.LastValue)
                {
                    slot.LastValue = slotState.Value;
                    if (slot.Control != null)
                    {
                        slot.Control.SetValue(slotState.Value);
                    }
                    else if (slot.ValueText != null)
                    {
                        slot.ValueText.SetTextSafe(slotState.Value);
                    }
                }
            }
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
                _logic.SyncSlots(_slots);
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
        public string SlotOrchestratorTitle => I18n.Tr("SPX_HEADER_SLOT_TITLE", "SpaceX 顶栏动态槽位: 可自由加减数据列、调整顺序与分隔线");

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
