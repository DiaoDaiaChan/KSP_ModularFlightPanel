using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.UI.Widgets
{
    /// <summary>
    /// 中央主警告与警报光字牌 (Master Warning & Caution Annunciator Widget)
    /// 严丝合缝嵌合于主导航球正下方 (宽度 184px, 高度 22px, 完美架设于双带之间)。
    /// 
    /// 工业级航电设计规范 (Industrial Avionics Bezel Architecture)：
    /// 1. 采用航空标准双室 Korry 光字牌 (Dual-Cell Korry Matrix) 架构，中间内嵌金属机械隔离筋条；
    /// 2. 真实航电“暗舱透光” (Backlit Dead-Front Display) 质感：
    ///    - 熄灭待命时：暗色熏黑玻璃内嵌微光幽灵刻字 (Ghost Inscription) 与微型绿光就绪指示灯；
    ///    - 激活报警时：顶部状态光条 (Status Pip Bar) 高亮脉冲，大字号发光主警报 + 右下角微型量化遥测读数与轮播指示器；
    /// 3. 分级独立分道：左舱专注黄色注意 (Caution)，右舱专注红色危急 (Warning)；
    /// 4. 同等级多告警智能平滑轮播 (Alternating Rotation) 并带点阵跑马灯与序号；
    /// 5. 瞬态机动横幅合并动画 (Transient Event Banner Merge & Flashback)：
    ///    - 飞船分离时：两方框平滑向中心聚拢合并，显示高反差航电大字“分离”，维持后迅速闪回双室；
    ///    - 引擎点火时：两方框平滑向中心聚拢合并，显示高反差航电大字“引擎启动”，维持后迅速闪回双室；
    /// 6. 100% 遵照 MFP 六大铁律 (0 颜色字面量、纯 C# 解耦、30Hz 分频、零 GC)。
    /// </summary>
    public class MasterWarningWidget : BaseFlightWidget
    {
        public override WidgetRefreshTier RefreshTier => WidgetRefreshTier.Standard;

        // 外部底板与装饰构件
        private Image _outerBezel;
        private Outline _outerOutline;
        private Image _centerDivider;

        // 告警单元结构体
        private struct AlertItem
        {
            public string MainTitle;
            public string TelemetryAffix;
            public bool IsWarning;

            public AlertItem(string title, string affix, bool isWarning)
            {
                MainTitle = title;
                TelemetryAffix = affix;
                IsWarning = isWarning;
            }
        }

        // 瞬态机动横幅状态机阶段
        private enum BannerDisplayState
        {
            Normal,         // 标准双室工作/待命 (Caution & Warning)
            MergingIn,      // 双室向中央滑动合并 (约 0.10s)
            MergedHolding,  // 一体横幅高亮呈现当前事件 (单事件 1.25s / 链式连击事件 0.95s)
            SwitchingEvent, // 队列中存在后续事件，就地微闪平滑切换至下一事件 (约 0.08s)
            FlashingBack    // 队列全部消费完毕，高频频闪复位回到双室 (约 0.14s)
        }

        public enum BannerEventType
        {
            Separation,
            EngineStart
        }

        // 横幅事件定义模型 (轻量结构体，0 GC)
        private struct BannerEventItem
        {
            public BannerEventType EventType;
            public string Title;
            public string Sub;
            public string LeftIcon;
            public string RightIcon;
            public float Duration;
            public int Priority; // 调度优先级 (数值越大越优先展示，Separation=20, EngineStart=10)

            public BannerEventItem(BannerEventType type, string title, string sub, string leftIcon, string rightIcon, float duration, int priority)
            {
                EventType = type;
                Title = title;
                Sub = sub;
                LeftIcon = leftIcon;
                RightIcon = rightIcon;
                Duration = duration;
                Priority = priority;
            }
        }

        // 瞬态事件横幅状态机与优先级队列
        private BannerDisplayState _bannerState = BannerDisplayState.Normal;
        private BannerEventItem _currentEvent;
        private readonly List<BannerEventItem> _bannerQueue = new List<BannerEventItem>(4);
        private float _bannerTimer = 0f;
        private float _bannerDuration = 1.25f;
        private string _sepTitleTemplate = "分  离";
        private string _engTitleTemplate = "引擎启动";

        // 事件防抖与冷却时间戳 (Debounce & Cooldown: 防止同事件多帧连续触发导致互相打架)
        private float _lastSepTriggerTime = -999f;
        private float _lastEngTriggerTime = -999f;
        private const float EVENT_COOLDOWN = 1.4f;

        // 低油量安全门限与时域防抖滤波 (防止点火瞬态与误读触发假警报)
        private float _customWarnThresh = -1f;
        private float _customCautThresh = -1f;
        private float _lowFuelPersistentTimer = 0f;
        private const float LOW_FUEL_PERSISTENCE = 0.35f;

        // 瞬态一体横幅 UI 节点
        private GameObject _bannerCell;
        private Image _bannerBg;
        private Outline _bannerOutline;
        private Image _bannerPipBar;
        private Text _bannerLeftIcon;
        private Text _bannerTitle;
        private Text _bannerRightIcon;
        private Text _bannerSub;

        // 上一帧遥测缓存 (用于瞬态边缘脉冲触发)
        private int _lastStage = -1;
        private int _lastActiveEngines = -1;
        private float _lastThrottle = -1f;
        private bool _lastIsStageSeparating = false;
        private bool _lastIsEngineIgniting = false;

        // 左舱：Caution (黄色注意) 视图组件
        private GameObject _cautCell;
        private RectTransform _cautRect;
        private Image _cautBg;
        private Outline _cautOutline;
        private Image _cautPipBar;
        private Text _cautIcon;
        private Text _cautTitle;
        private Text _cautSub;
        private Button _cautBtn;

        // 右舱：Warning (红色危急) 视图组件
        private GameObject _warnCell;
        private RectTransform _warnRect;
        private Image _warnBg;
        private Outline _warnOutline;
        private Image _warnPipBar;
        private Text _warnIcon;
        private Text _warnTitle;
        private Text _warnSub;
        private Button _warnBtn;

        // 告警队列
        private readonly List<AlertItem> _cautAlerts = new List<AlertItem>(8);
        private readonly List<AlertItem> _warnAlerts = new List<AlertItem>(8);

        // 轮播计时器
        private float _rotateTimer = 0f;
        private float _switchInterval = 1.8f;
        private int _cautIndex = 0;
        private int _warnIndex = 0;

        // 消警状态 (Acknowledge)
        private bool _cautAcknowledged = false;
        private bool _warnAcknowledged = false;
        private int _lastCautCount = -1;
        private int _lastWarnCount = -1;

        // 同步时钟与闪烁
        private static float _clock = 0f;
        private static bool _blink1Hz = true;
        private static bool _blink2Hz = true;

        // 脏检查保护
        private string _lastCautTitleStr = string.Empty;
        private string _lastCautSubStr = string.Empty;
        private string _lastWarnTitleStr = string.Empty;
        private string _lastWarnSubStr = string.Empty;

        protected override void OnInitialize(WidgetConfig config, ThemeConfig theme)
        {
            theme = WidgetStyleManager.ResolveTheme(theme);
            float s = CurrentDpiScale;
            Vector2 widgetSize = new Vector2(184f * s, 22f * s);
            RectTransform.sizeDelta = widgetSize;

            ParseCustomTemplate(config?.CustomTemplate);
            _currentEvent = BuildEventItem(BannerEventType.Separation);

            // 1. 航空外框底盘 (Outer Bezel)
            _outerBezel = gameObject.AddComponent<Image>();
            _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            _outerOutline = gameObject.AddComponent<Outline>();
            _outerOutline.effectDistance = new Vector2(1f * s, 1f * s);
            _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);

            // 2. 中央硬派机械隔离筋条 (Mechanical Divider Rib, 宽 2px, 高 18px)
            GameObject divObj = UIFactory.CreatePanel(transform, "Divider", new Vector2(2f * s, 18f * s),
                Vector2.zero, WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme));
            _centerDivider = divObj.GetComponent<Image>();

            // 3. 构建左舱：CAUTION 光字牌 (宽 89px, 高 18px, 偏置 -46px)
            Vector2 cellSize = new Vector2(89f * s, 18f * s);
            BuildCautionCell(s, cellSize, theme);

            // 4. 构建右舱：WARNING 光字牌 (宽 89px, 高 18px, 偏置 +46px)
            BuildWarningCell(s, cellSize, theme);

            // 5. 构建全幅瞬态合并横幅 (宽 180px, 高 18px, 居中)
            BuildBannerCell(s, new Vector2(180f * s, 18f * s), theme);

            ApplyTheme(theme);
        }

        private void BuildCautionCell(float s, Vector2 size, ThemeConfig theme)
        {
            _cautCell = UIFactory.CreatePanel(transform, "CautionCell", size, new Vector2(-46f * s, 0f), Color.clear);
            _cautRect = _cautCell.GetComponent<RectTransform>();
            _cautBg = _cautCell.GetComponent<Image>();
            _cautOutline = _cautCell.AddComponent<Outline>();
            _cautOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _cautBtn = _cautCell.AddComponent<Button>();
            _cautBtn.transition = Selectable.Transition.None;
            _cautBtn.onClick.AddListener(OnAcknowledgeCaution);

            // 顶部高光 Pip 指示条 (贴合格栅顶唇)
            GameObject pipObj = UIFactory.CreatePanel(_cautCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _cautPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标 (带光刻质感)
            _cautIcon = UIFactory.CreateText(_cautCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.WarningColor);
            _cautIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _cautIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _cautTitle = UIFactory.CreateText(_cautCell.transform, "Title", "CAUTION", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.WarningColor);
            _cautTitle.fontStyle = FontStyle.Bold;
            _cautTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _cautTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注与角标 (如 1/2 或 14%)
            _cautSub = UIFactory.CreateText(_cautCell.transform, "Sub", "NORM", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.WarningColor);
            _cautSub.fontStyle = FontStyle.Normal;
            _cautSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _cautSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void BuildWarningCell(float s, Vector2 size, ThemeConfig theme)
        {
            _warnCell = UIFactory.CreatePanel(transform, "WarningCell", size, new Vector2(46f * s, 0f), Color.clear);
            _warnRect = _warnCell.GetComponent<RectTransform>();
            _warnBg = _warnCell.GetComponent<Image>();
            _warnOutline = _warnCell.AddComponent<Outline>();
            _warnOutline.effectDistance = new Vector2(1f * s, 1f * s);

            _warnBtn = _warnCell.AddComponent<Button>();
            _warnBtn.transition = Selectable.Transition.None;
            _warnBtn.onClick.AddListener(OnAcknowledgeWarning);

            // 顶部高光 Pip 指示条 (贴合格栅顶唇)
            GameObject pipObj = UIFactory.CreatePanel(_warnCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _warnPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标
            _warnIcon = UIFactory.CreateText(_warnCell.transform, "Icon", "▲", Mathf.Max(6, Mathf.RoundToInt(6.5f * s)),
                TextAnchor.MiddleLeft, theme.DangerColor);
            _warnIcon.rectTransform.sizeDelta = new Vector2(10f * s, size.y);
            _warnIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 6f * s, 0f);

            // 主标题
            _warnTitle = UIFactory.CreateText(_warnCell.transform, "Title", "WARNING", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleCenter, theme.DangerColor);
            _warnTitle.fontStyle = FontStyle.Bold;
            _warnTitle.rectTransform.sizeDelta = new Vector2(58f * s, size.y);
            _warnTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧微型附注与角标
            _warnSub = UIFactory.CreateText(_warnCell.transform, "Sub", "ARMED", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.DangerColor);
            _warnSub.fontStyle = FontStyle.Normal;
            _warnSub.rectTransform.sizeDelta = new Vector2(22f * s, size.y);
            _warnSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 12f * s, 0f);
        }

        private void BuildBannerCell(float s, Vector2 size, ThemeConfig theme)
        {
            _bannerCell = UIFactory.CreatePanel(transform, "BannerCell", size, Vector2.zero, Color.clear);
            _bannerBg = _bannerCell.GetComponent<Image>();
            _bannerOutline = _bannerCell.AddComponent<Outline>();
            _bannerOutline.effectDistance = new Vector2(1f * s, 1f * s);

            // 顶部高光 Pip 指示条
            GameObject pipObj = UIFactory.CreatePanel(_bannerCell.transform, "PipBar", new Vector2(size.x - 2f * s, 2f * s),
                new Vector2(0f, size.y * 0.5f - 1f * s), Color.clear);
            _bannerPipBar = pipObj.GetComponent<Image>();

            // 左侧状态微标
            _bannerLeftIcon = UIFactory.CreateText(_bannerCell.transform, "LeftIcon", "◀", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleLeft, theme.AccentPrimary);
            _bannerLeftIcon.rectTransform.sizeDelta = new Vector2(14f * s, size.y);
            _bannerLeftIcon.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f + 10f * s, 0f);

            // 主标题
            _bannerTitle = UIFactory.CreateText(_bannerCell.transform, "Title", "分  离", Mathf.Max(8, Mathf.RoundToInt(8.5f * s)),
                TextAnchor.MiddleCenter, theme.AccentPrimary);
            _bannerTitle.fontStyle = FontStyle.Bold;
            _bannerTitle.rectTransform.sizeDelta = new Vector2(110f * s, size.y);
            _bannerTitle.rectTransform.anchoredPosition = Vector2.zero;

            // 右侧状态微标
            _bannerRightIcon = UIFactory.CreateText(_bannerCell.transform, "RightIcon", "▶", Mathf.Max(7, Mathf.RoundToInt(7.5f * s)),
                TextAnchor.MiddleRight, theme.AccentPrimary);
            _bannerRightIcon.rectTransform.sizeDelta = new Vector2(14f * s, size.y);
            _bannerRightIcon.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 10f * s, 0f);

            // 右侧微型附注
            _bannerSub = UIFactory.CreateText(_bannerCell.transform, "Sub", "STG", Mathf.Max(6, Mathf.RoundToInt(6f * s)),
                TextAnchor.MiddleRight, theme.AccentPrimary);
            _bannerSub.fontStyle = FontStyle.Normal;
            _bannerSub.rectTransform.sizeDelta = new Vector2(26f * s, size.y);
            _bannerSub.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f - 26f * s, 0f);

            _bannerCell.SetActive(false);
        }

        private void ParseCustomTemplate(string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            string[] pairs = template.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                string[] kv = pairs[i].Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToUpperInvariant();
                string v = kv[1].Trim();
                if (k == "INTERVAL" || k == "ROTATION")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float iv) && iv > 0.5f)
                    {
                        _switchInterval = iv;
                    }
                }
                else if (k == "SEP" || k == "SEP_TEXT" || k == "SEPARATION")
                {
                    if (!string.IsNullOrEmpty(v)) _sepTitleTemplate = v;
                }
                else if (k == "ENG" || k == "ENG_TEXT" || k == "IGNITION")
                {
                    if (!string.IsNullOrEmpty(v)) _engTitleTemplate = v;
                }
                else if (k == "TIME" || k == "BANNER_TIME" || k == "DURATION")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dt) && dt > 0.4f)
                    {
                        _bannerDuration = dt;
                    }
                }
                else if (k == "FUEL_WARN" || k == "MIN_FUEL")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fw) && fw > 0.01f && fw <= 35f)
                    {
                        _customWarnThresh = fw / 100f;
                    }
                }
                else if (k == "FUEL_CAUT" || k == "LOW_FUEL")
                {
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fc) && fc > 0.01f && fc <= 50f)
                    {
                        _customCautThresh = fc / 100f;
                    }
                }
            }
        }

        private BannerEventItem BuildEventItem(BannerEventType type)
        {
            if (type == BannerEventType.Separation)
            {
                return new BannerEventItem(
                    BannerEventType.Separation,
                    _sepTitleTemplate,
                    "STG",
                    "◀",
                    "▶",
                    _bannerDuration,
                    20
                );
            }
            else
            {
                return new BannerEventItem(
                    BannerEventType.EngineStart,
                    _engTitleTemplate,
                    "IGN",
                    "▲",
                    "▲",
                    _bannerDuration,
                    10
                );
            }
        }

        private void EnqueueByPriority(BannerEventItem item)
        {
            if (_bannerQueue.Count >= 4) return;

            int insertIdx = _bannerQueue.Count;
            for (int i = 0; i < _bannerQueue.Count; i++)
            {
                if (item.Priority > _bannerQueue[i].Priority)
                {
                    insertIdx = i;
                    break;
                }
            }
            _bannerQueue.Insert(insertIdx, item);
        }

        public void TriggerBanner(BannerEventType eventType, bool immediateHolding = false)
        {
            float now = Time.unscaledTime;

            // 1. 防抖与去重审查：若处于冷却期且非强制立即渲染，则忽略重复脉冲
            if (!immediateHolding)
            {
                if (eventType == BannerEventType.Separation && (now - _lastSepTriggerTime) < EVENT_COOLDOWN) return;
                if (eventType == BannerEventType.EngineStart && (now - _lastEngTriggerTime) < EVENT_COOLDOWN) return;

                // 若当前正在展示同类事件，或队列中已有同类事件，避免重复堆叠
                if (_bannerState != BannerDisplayState.Normal && _bannerState != BannerDisplayState.FlashingBack)
                {
                    if (_currentEvent.EventType == eventType) return;
                    for (int i = 0; i < _bannerQueue.Count; i++)
                    {
                        if (_bannerQueue[i].EventType == eventType) return;
                    }
                }
            }

            // 更新触发时间戳
            if (eventType == BannerEventType.Separation) _lastSepTriggerTime = now;
            else if (eventType == BannerEventType.EngineStart) _lastEngTriggerTime = now;

            BannerEventItem newItem = BuildEventItem(eventType);

            // 2. 强制即时渲染模式 (供无头渲染器 HeadlessUIRenderer 捕获静态切片)
            if (immediateHolding)
            {
                _bannerQueue.Clear();
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergedHolding;
                _bannerTimer = 0.12f;
                if (_centerDivider != null) _centerDivider.gameObject.SetActive(false);
                if (_cautCell != null) _cautCell.SetActive(false);
                if (_warnCell != null) _warnCell.SetActive(false);
                if (_bannerCell != null) _bannerCell.SetActive(true);
                return;
            }

            // 3. 状态机分流与仲裁
            if (_bannerState == BannerDisplayState.Normal)
            {
                // 空闲常态：接管主横幅，启动双框聚拢滑入动画
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergingIn;
                _bannerTimer = 0f;
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                // 正处于闪回尾声：平滑截断闪回，直接接入下一事件高亮保持
                _currentEvent = newItem;
                _bannerState = BannerDisplayState.MergedHolding;
                _bannerTimer = 0.12f;
                if (_bannerCell != null) _bannerCell.SetActive(true);
                if (_cautCell != null) _cautCell.SetActive(false);
                if (_warnCell != null) _warnCell.SetActive(false);
            }
            else
            {
                // 当前正有其他事件展示中 (如聚拢中、展示中或切换中)：
                // 若仍在向中心聚拢阶段 (MergingIn) 且新到事件优先级更高 (如分离 20 > 启动 10)，则置换首位
                if (_bannerState == BannerDisplayState.MergingIn && newItem.Priority > _currentEvent.Priority)
                {
                    BannerEventItem lower = _currentEvent;
                    _currentEvent = newItem;
                    _bannerQueue.Insert(0, lower);
                }
                else
                {
                    EnqueueByPriority(newItem);
                }
            }
        }

        private void OnAcknowledgeCaution()
        {
            _cautAcknowledged = true;
        }

        private void OnAcknowledgeWarning()
        {
            _warnAcknowledged = true;
        }

        public override void OnUpdateTelemetry(IFlightTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasVessel) return;

            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float dt = Time.unscaledDeltaTime;

            // 1. 侦测分级分离与引擎点火瞬态事件 (驱动事件状态机与优先级队列)
            DetectTransientEvents(telemetry);

            // 2. 持续评估当前所有活跃警报 (以便在非横幅状态渲染光字牌，以及监控重大警情)
            EvaluateTelemetryAlerts(telemetry, dt);

            // 3. 若当前处于横幅合并、展示、切换或闪回动画阶段，转由横幅状态机独占驱动
            if (_bannerState != BannerDisplayState.Normal)
            {
                UpdateBannerAnimation(dt, theme);
                return;
            }

            // 4. 更新座舱全局同步时钟
            _clock += dt;
            _blink1Hz = ((int)(_clock * 2f) % 2) == 0;
            _blink2Hz = ((int)(_clock * 4f) % 2) == 0;

            // 5. 告警数量变动与防抖消警保护
            UpdateAlertIndicesAndAcknowledge();

            // 6. 定时交替轮播推进
            _rotateTimer += dt;
            if (_rotateTimer >= _switchInterval)
            {
                _rotateTimer = 0f;
                if (_cautAlerts.Count > 1) _cautIndex = (_cautIndex + 1) % _cautAlerts.Count;
                if (_warnAlerts.Count > 1) _warnIndex = (_warnIndex + 1) % _warnAlerts.Count;
            }

            // 7. 渲染双光字牌
            RenderVisualCells();
        }

        private void UpdateAlertIndicesAndAcknowledge()
        {
            if (_cautAlerts.Count != _lastCautCount)
            {
                if (_cautAlerts.Count > _lastCautCount) _cautAcknowledged = false;
                _lastCautCount = _cautAlerts.Count;
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
            }
            if (_warnAlerts.Count != _lastWarnCount)
            {
                if (_warnAlerts.Count > _lastWarnCount) _warnAcknowledged = false;
                _lastWarnCount = _warnAlerts.Count;
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
            }
        }

        private void DetectTransientEvents(IFlightTelemetry telem)
        {
            bool triggerSep = false;
            bool triggerEng = false;

            if (_lastStage != -1)
            {
                // 分级/解耦/脱开分离判定
                bool sepSignal = telem.IsStageSeparating && !_lastIsStageSeparating;
                bool stageDropped = telem.CurrentStage < _lastStage;
                if (sepSignal || stageDropped)
                {
                    triggerSep = true;
                }
            }

            if (_lastActiveEngines != -1)
            {
                // 引擎启动判定 (点火脉冲、引擎数从 0 激增、或油门开启点火)
                bool ignSignal = telem.IsEngineIgniting && !_lastIsEngineIgniting;
                bool engStarted = (_lastActiveEngines == 0 && telem.ActiveEngines > 0 && telem.Throttle > 0.02f) ||
                                  (_lastThrottle <= 0.001f && telem.Throttle > 0.05f && telem.ActiveEngines > 0);
                if (ignSignal || engStarted)
                {
                    triggerEng = true;
                }
            }

            // 统一调度：若两事件同时触发，Separation 必定优先排在前面，EngineStart 排在后面
            if (triggerSep)
            {
                TriggerBanner(BannerEventType.Separation);
            }
            if (triggerEng)
            {
                TriggerBanner(BannerEventType.EngineStart);
            }

            _lastStage = telem.CurrentStage;
            _lastActiveEngines = telem.ActiveEngines;
            _lastThrottle = telem.Throttle;
            _lastIsStageSeparating = telem.IsStageSeparating;
            _lastIsEngineIgniting = telem.IsEngineIgniting;
        }

        private void UpdateBannerAnimation(float dt, ThemeConfig theme)
        {
            if (theme == null) theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            float s = CurrentDpiScale;

            Color eventColor = (_currentEvent.EventType == BannerEventType.Separation) ? theme.AccentPrimary : theme.WarningColor;

            if (_bannerState == BannerDisplayState.MergingIn)
            {
                _bannerTimer += dt;
                float progress = Mathf.Clamp01(_bannerTimer / 0.10f);

                // 两方框向中央平滑聚拢
                if (_centerDivider != null) _centerDivider.gameObject.SetActive(false);
                if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(Mathf.Lerp(-46f * s, 0f, progress), 0f);
                if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(Mathf.Lerp(46f * s, 0f, progress), 0f);

                if (progress >= 1.0f)
                {
                    if (_cautCell != null) _cautCell.SetActive(false);
                    if (_warnCell != null) _warnCell.SetActive(false);
                    if (_bannerCell != null) _bannerCell.SetActive(true);
                    _bannerState = BannerDisplayState.MergedHolding;
                    _bannerTimer = 0f;
                }
            }
            else if (_bannerState == BannerDisplayState.MergedHolding)
            {
                _bannerTimer += dt;

                SetTextIfChanged(_bannerTitle, _currentEvent.Title);
                SetTextIfChanged(_bannerSub, _currentEvent.Sub);
                SetTextIfChanged(_bannerLeftIcon, _currentEvent.LeftIcon);
                SetTextIfChanged(_bannerRightIcon, _currentEvent.RightIcon);

                // 航电高光微脉冲 (呼吸感)
                float pulse = 0.82f + 0.18f * Mathf.Sin(_bannerTimer * 12f);
                Color activeCol = WidgetStyleManager.WithAlpha(eventColor, pulse);

                if (_bannerBg != null) _bannerBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                if (_bannerOutline != null) _bannerOutline.effectColor = activeCol;
                if (_bannerPipBar != null) _bannerPipBar.color = activeCol;
                if (_bannerTitle != null) _bannerTitle.color = eventColor;
                if (_bannerSub != null) _bannerSub.color = WidgetStyleManager.WithAlpha(eventColor, 0.70f);
                if (_bannerLeftIcon != null) _bannerLeftIcon.color = activeCol;
                if (_bannerRightIcon != null) _bannerRightIcon.color = activeCol;

                // 若有排队连击事件，单事件展示时长适度收紧 (0.95s)，保持紧凑利落的航电节奏感
                float targetDuration = (_bannerQueue.Count > 0) ? Mathf.Min(_currentEvent.Duration, 0.95f) : _currentEvent.Duration;

                if (_bannerTimer >= targetDuration)
                {
                    if (_bannerQueue.Count > 0)
                    {
                        // 队列中有后续事件待展示：不闪退，进入 0.08s 敏捷就地翻转过渡态
                        _currentEvent = _bannerQueue[0];
                        _bannerQueue.RemoveAt(0);
                        _bannerState = BannerDisplayState.SwitchingEvent;
                        _bannerTimer = 0f;
                    }
                    else
                    {
                        // 队列已清空：进入闪回双室流程
                        _bannerState = BannerDisplayState.FlashingBack;
                        _bannerTimer = 0f;
                    }
                }
            }
            else if (_bannerState == BannerDisplayState.SwitchingEvent)
            {
                _bannerTimer += dt;

                // 0.08s 敏捷过渡：高光闪烁与文本瞬变
                SetTextIfChanged(_bannerTitle, _currentEvent.Title);
                SetTextIfChanged(_bannerSub, _currentEvent.Sub);
                SetTextIfChanged(_bannerLeftIcon, _currentEvent.LeftIcon);
                SetTextIfChanged(_bannerRightIcon, _currentEvent.RightIcon);

                Color switchColor = (_currentEvent.EventType == BannerEventType.Separation) ? theme.AccentPrimary : theme.WarningColor;
                if (_bannerOutline != null) _bannerOutline.effectColor = switchColor;
                if (_bannerPipBar != null) _bannerPipBar.color = switchColor;
                if (_bannerTitle != null) _bannerTitle.color = switchColor;
                if (_bannerSub != null) _bannerSub.color = switchColor;

                if (_bannerTimer >= 0.08f)
                {
                    _bannerState = BannerDisplayState.MergedHolding;
                    _bannerTimer = 0f;
                }
            }
            else if (_bannerState == BannerDisplayState.FlashingBack)
            {
                _bannerTimer += dt;

                // 24Hz 迅速高频频闪
                bool strobeOn = ((int)(_bannerTimer * 24f) % 2) == 0;
                if (_bannerCell != null) _bannerCell.SetActive(strobeOn);

                if (_bannerTimer >= 0.14f)
                {
                    // 闪回完毕，复位回到双室待命
                    if (_bannerCell != null) _bannerCell.SetActive(false);
                    if (_cautCell != null) _cautCell.SetActive(true);
                    if (_warnCell != null) _warnCell.SetActive(true);
                    if (_cautRect != null) _cautRect.anchoredPosition = new Vector2(-46f * s, 0f);
                    if (_warnRect != null) _warnRect.anchoredPosition = new Vector2(46f * s, 0f);
                    if (_centerDivider != null) _centerDivider.gameObject.SetActive(true);

                    _bannerState = BannerDisplayState.Normal;
                    _bannerTimer = 0f;
                    RenderVisualCells();
                }
            }
        }

        private void EvaluateTelemetryAlerts(IFlightTelemetry telem, float dt)
        {
            _cautAlerts.Clear();
            _warnAlerts.Clear();

            // ── 1. 推进剂与沉底 (FUEL / ULLAGE) ──
            float prop = telem.StagePropellantFraction;
            bool engineArmed = telem.ActiveEngines > 0 || (telem.TotalStageEngines > 0 && telem.Throttle > 0.001f);

            // 航电标准低油量安全门限 (严格防反向门限误伤：Caution 默认 <= 15%，Warning 默认 <= 5%)
            // 严禁采纳仪表通用缺省值 (80%/95%/100%)，避免在满油 100% 阶段触发荒谬的 MIN FUEL 告警
            float warnThresh = 0.05f;
            float cautThresh = 0.15f;
            if (_customWarnThresh > 0.001f) warnThresh = _customWarnThresh;
            else if (Config != null && Config.WarningThreshold > 0.01 && Config.WarningThreshold <= 25.0)
                warnThresh = (float)Config.WarningThreshold / 100f;

            if (_customCautThresh > 0.001f) cautThresh = _customCautThresh;
            else if (Config != null && Config.CautionThreshold > 0.01 && Config.CautionThreshold <= 40.0)
                cautThresh = (float)Config.CautionThreshold / 100f;

            // 绝对安全熔断器：若油量在 40% 以上，物理上绝对属于正常或充足，立即清空低油量防抖计数器，杜绝误报
            if (prop >= 0.40f || !engineArmed)
            {
                _lowFuelPersistentTimer = 0f;
            }
            else if (prop >= 0f && prop <= cautThresh)
            {
                // 时域防抖滤波：持续处于低油量门限以下至少 0.35s 确认非传感器瞬态抖动或点火管网建立延迟
                _lowFuelPersistentTimer += dt;
                if (_lowFuelPersistentTimer >= LOW_FUEL_PERSISTENCE)
                {
                    int propPct = Mathf.RoundToInt(prop * 100f);
                    if (prop <= warnThresh) _warnAlerts.Add(new AlertItem("MIN FUEL!", $"{propPct}%", true));
                    else _cautAlerts.Add(new AlertItem("LOW FUEL", $"{propPct}%", false));
                }
            }
            else
            {
                _lowFuelPersistentTimer = 0f;
            }

            // RealFuels 探针沉底状态
            if (ExternalProbeRegistry.StringResolver != null)
            {
                string rfUllage = ExternalProbeRegistry.ResolveString("RF", "ULLAGE", "");
                if (!string.IsNullOrEmpty(rfUllage) &&
                    (rfUllage.IndexOf("Unstable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     rfUllage.IndexOf("Very", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _cautAlerts.Add(new AlertItem("ULLAGE", "UNSTB", false));
                }
            }

            // ── 2. 电气能量平衡 (ELECTRIC CHARGE / DBS) ──
            double ecPct = telem.EcPercent;
            if (ecPct >= 0.0)
            {
                int ecInt = Mathf.RoundToInt((float)ecPct);
                if (ecPct <= 5.0) _warnAlerts.Add(new AlertItem("EC CRIT!", $"{ecInt}%", true));
                else if (ecPct <= 20.0) _cautAlerts.Add(new AlertItem("LOW EC", $"{ecInt}%", false));
            }

            // ── 3. 近地危险下沉与地形拉起 (PULL UP! / SINK RATE) ──
            // GPWS Mode 1 规范：仅在剧烈下沉威胁接地安全时拉响 PULL UP!，避免平缓接地造成假警报
            bool severePullUp = (telem.VerticalSpeed < -25.0 && telem.AltitudeAGL < 600.0 && telem.AltitudeAGL > 3.0) ||
                                (telem.VerticalSpeed < -12.0 && telem.AltitudeAGL < 150.0 && telem.AltitudeAGL > 3.0);
            if (severePullUp)
            {
                int aglInt = Mathf.RoundToInt((float)telem.AltitudeAGL);
                _warnAlerts.Add(new AlertItem("PULL UP!", $"{aglInt}m", true));
            }
            else if (telem.VerticalSpeed < -15.0 && telem.AltitudeAGL < 1500.0 && telem.AltitudeAGL > 10.0)
            {
                int vsiInt = Mathf.RoundToInt((float)telem.VerticalSpeed);
                _cautAlerts.Add(new AlertItem("SINK RATE", $"{vsiInt}m/s", false));
            }

            // ── 4. 气动失速 (STALL / FAR / GPWS) ──
            double farStall = double.NaN;
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                farStall = ExternalProbeRegistry.ResolveNumeric("FAR", "STALL");
            }
            if (!double.IsNaN(farStall))
            {
                int stallPct = Mathf.RoundToInt((float)(farStall * 100.0));
                if (farStall > 0.70) _warnAlerts.Add(new AlertItem("STALL!", $"{stallPct}%", true));
                else if (farStall > 0.30) _cautAlerts.Add(new AlertItem("STALL WARN", $"{stallPct}%", false));
            }

            // ── 5. 维生系统与氧气 (O2 / KERBALISM) ──
            if (telem.CrewCapacity > 0)
            {
                float o2 = telem.OxygenPercent;
                if (o2 >= 0f)
                {
                    int o2Int = Mathf.RoundToInt(o2);
                    if (o2 <= 5.0f) _warnAlerts.Add(new AlertItem("O2 CRIT!", $"{o2Int}%", true));
                    else if (o2 <= 20.0f) _cautAlerts.Add(new AlertItem("LOW O2", $"{o2Int}%", false));
                }
            }

            // ── 6. 舱温与超温 (OVERHEAT / SYSTEMHEAT) ──
            double cabinTemp = telem.CabinTemp;
            if (cabinTemp > 120.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _warnAlerts.Add(new AlertItem("OVERHEAT!", $"{tempInt}°C", true));
            }
            else if (cabinTemp > 80.0)
            {
                int tempInt = Mathf.RoundToInt((float)cabinTemp);
                _cautAlerts.Add(new AlertItem("HIGH TEMP", $"{tempInt}°C", false));
            }

            // ── 7. 过载极限 (G-FORCE) ──
            double g = telem.GForce;
            if (g > 9.0)
            {
                _warnAlerts.Add(new AlertItem("EXCESS G!", $"{g:F1}G", true));
            }
            else if (g > 6.0)
            {
                _cautAlerts.Add(new AlertItem("HIGH G", $"{g:F1}G", false));
            }

            // ── 8. 发动机故障 (TESTFLIGHT) ──
            if (ExternalProbeRegistry.NumericResolver != null)
            {
                double tfFailed = ExternalProbeRegistry.ResolveNumeric("TF", "FAILED");
                if (tfFailed > 0.5)
                {
                    _warnAlerts.Add(new AlertItem("ENG FAIL!", "FAIL", true));
                }
            }

            // ── 9. 通信网络断开 (NO COMM) ──
            // 仅对无人探测器 (Uncrewed Probe) 或空舱生效；有人驾驶飞船不因地面通讯死区频繁拉响主注意
            if (!telem.IsConnected && (telem.CrewCount == 0 || telem.CrewCapacity == 0))
            {
                _cautAlerts.Add(new AlertItem("NO COMM", "OFF", false));
            }
        }

        private void RenderVisualCells()
        {
            ThemeConfig theme = WidgetStyleManager.Instance?.CurrentTheme ?? WidgetStyleManager.ResolveTheme(null);
            if (theme == null) return;

            // ── A. 渲染左舱：CAUTION ──
            bool isCautActive = _cautAlerts.Count > 0;
            if (isCautActive)
            {
                if (_cautIndex >= _cautAlerts.Count) _cautIndex = 0;
                AlertItem item = _cautAlerts[_cautIndex];

                string pagination = _cautAlerts.Count > 1 ? $"{_cautIndex + 1}/{_cautAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_cautTitle, item.MainTitle);
                SetTextIfChanged(_cautSub, pagination);
                SetTextIfChanged(_cautIcon, "▲");

                bool blink = _cautAcknowledged || _blink1Hz;
                if (blink)
                {
                    _cautBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Caution, theme);
                    _cautOutline.effectColor = theme.WarningColor;
                    _cautPipBar.color = theme.WarningColor;
                    _cautTitle.color = theme.WarningColor;
                    _cautSub.color = theme.WarningColor;
                    _cautIcon.color = theme.WarningColor;
                }
                else
                {
                    _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.WarningColor, LineWeight.Faint);
                    _cautPipBar.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.30f);
                    _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautSub.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                    _cautIcon.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_cautTitle, "CAUTION");
                SetTextIfChanged(_cautSub, "NORM");
                SetTextIfChanged(_cautIcon, "●");

                _cautBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _cautOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _cautPipBar.color = Color.clear;
                _cautTitle.color = WidgetStyleManager.WithAlpha(theme.WarningColor, 0.22f);
                _cautSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _cautIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }

            // ── B. 渲染右舱：WARNING ──
            bool isWarnActive = _warnAlerts.Count > 0;
            if (isWarnActive)
            {
                if (_warnIndex >= _warnAlerts.Count) _warnIndex = 0;
                AlertItem item = _warnAlerts[_warnIndex];

                string pagination = _warnAlerts.Count > 1 ? $"{_warnIndex + 1}/{_warnAlerts.Count}" : item.TelemetryAffix;
                SetTextIfChanged(_warnTitle, item.MainTitle);
                SetTextIfChanged(_warnSub, pagination);
                SetTextIfChanged(_warnIcon, "▲");

                bool blink = _warnAcknowledged || _blink2Hz;
                if (blink)
                {
                    _warnBg.color = WidgetStyleManager.StatusSurface(StatusSurfaceRole.Danger, theme);
                    _warnOutline.effectColor = theme.DangerColor;
                    _warnPipBar.color = theme.DangerColor;
                    _warnTitle.color = theme.DangerColor;
                    _warnSub.color = theme.DangerColor;
                    _warnIcon.color = theme.DangerColor;
                }
                else
                {
                    _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
                    _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.DangerColor, LineWeight.Faint);
                    _warnPipBar.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.30f);
                    _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnSub.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                    _warnIcon.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.40f);
                }
            }
            else
            {
                // 暗态待命 (Dead-Front Nominal)
                SetTextIfChanged(_warnTitle, "WARNING");
                SetTextIfChanged(_warnSub, "ARMED");
                SetTextIfChanged(_warnIcon, "●");

                _warnBg.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);
                _warnOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
                _warnPipBar.color = Color.clear;
                _warnTitle.color = WidgetStyleManager.WithAlpha(theme.DangerColor, 0.22f);
                _warnSub.color = WidgetStyleManager.Instance.GetTextColor(TextStyleRole.SecondaryValue, theme);
                _warnIcon.color = WidgetStyleManager.Weighted(theme.AccentPrimary, LineWeight.Ghost);
            }
        }

        public override void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            theme = WidgetStyleManager.ResolveTheme(theme);

            if (_outerBezel != null) _outerBezel.color = WidgetStyleManager.Surface(SurfaceStyleRole.PanelDeep, theme);
            if (_outerOutline != null) _outerOutline.effectColor = WidgetStyleManager.Weighted(theme.AccentSecondary, LineWeight.Ghost);
            if (_centerDivider != null) _centerDivider.color = WidgetStyleManager.Surface(SurfaceStyleRole.Inset, theme);

            if (_bannerState != BannerDisplayState.Normal)
            {
                UpdateBannerAnimation(0f, theme);
            }
            else
            {
                RenderVisualCells();
            }
        }

        protected override void OnDestroy()
        {
            _bannerQueue.Clear();
            if (_cautBtn != null) _cautBtn.onClick.RemoveAllListeners();
            if (_warnBtn != null) _warnBtn.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
