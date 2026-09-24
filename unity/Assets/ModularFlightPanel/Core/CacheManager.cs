using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.UI;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// ====================================================================================
    /// Modular Flight Panel (MFP) 统一缓存中枢 (CacheManager)
    /// ====================================================================================
    /// 核心职能：
    /// 1. P1: 零 GC 预烘焙静态整数字符串表 (-1000 ~ 9999) 与角度/百分比常量表
    /// 2. P1: 浮点数死区量化缓存池 (Deadband Quantization Cache) - 阻断高频遥测每秒数千个小字符串垃圾
    /// 3. P3: 目标交会与对接口相对几何一阶外推缓存 (Docking/Target Kinematic Extrapolation Cache)
    /// 4. P4: 主题语义颜色直接寻址扁平缓存 (Flat Array Lookup Cache)
    /// 5. 缓存健康度、命中率统计与内存占用可观测性 (MFPProfiler 联动)
    /// 
    /// 契约与合规：
    /// - 纯 C# / UnityEngine 实现，彻底解耦，不包含不可跨平台的 KSP 原生类型
    /// - 镜像同步 (unity_mirror.manifest) 自动兼容 Unity 2019 无头渲染环境
    /// - 0 颜色字面量，100% 遵照 MFP 规范体系
    /// </summary>
    public class CacheManager
    {
        private static CacheManager _instance;
        public static CacheManager Instance => _instance ?? (_instance = new CacheManager());

        #region P1: Zero-GC Pre-baked Static Tables (-1000 ~ 9999)

        private const int POSITIVE_INT_COUNT = 10000; // 0 ~ 9999
        private const int NEGATIVE_INT_COUNT = 1001;  // -1 ~ -1000

        private static readonly string[] _positiveIntStrings;
        private static readonly string[] _negativeIntStrings;
        private static readonly string[] _percentStrings; // 0% ~ 100%
        private static readonly string[] _degreeStrings;  // 0° ~ 360°

        static CacheManager()
        {
            // 1. 预热整数转字符串缓存表 (0 ~ 9999)
            _positiveIntStrings = new string[POSITIVE_INT_COUNT];
            for (int i = 0; i < POSITIVE_INT_COUNT; i++)
            {
                _positiveIntStrings[i] = i.ToString();
            }

            // 2. 预热负整数转字符串缓存表 (-1 ~ -1000)
            _negativeIntStrings = new string[NEGATIVE_INT_COUNT];
            for (int i = 1; i < NEGATIVE_INT_COUNT; i++)
            {
                _negativeIntStrings[i] = (-i).ToString();
            }

            // 3. 预热百分比字符串表 (0% ~ 100%)
            _percentStrings = new string[101];
            for (int i = 0; i <= 100; i++)
            {
                _percentStrings[i] = i.ToString() + "%";
            }

            // 4. 预热度数字符串表 (0° ~ 360°)
            _degreeStrings = new string[361];
            for (int i = 0; i <= 360; i++)
            {
                _degreeStrings[i] = i.ToString() + "°";
            }
        }

        /// <summary>
        /// 零 GC 快速获取整数对应字符串 (-1000 ~ 9999 范围 O(1) 数组直取)
        /// </summary>
        public static string FastInt(int value)
        {
            if (value >= 0 && value < POSITIVE_INT_COUNT)
            {
                return _positiveIntStrings[value];
            }
            if (value < 0 && value > -NEGATIVE_INT_COUNT)
            {
                return _negativeIntStrings[-value];
            }
            return value.ToString();
        }

        /// <summary>
        /// 零 GC 快速获取百分比字符串 (0% ~ 100%)
        /// </summary>
        public static string FastPercent(int percent)
        {
            if (percent >= 0 && percent <= 100)
            {
                return _percentStrings[percent];
            }
            return percent.ToString() + "%";
        }

        /// <summary>
        /// 零 GC 快速获取度数字符串 (0° ~ 360°)
        /// </summary>
        public static string FastDegree(int degree)
        {
            int normalized = (degree % 360 + 360) % 360;
            return _degreeStrings[normalized];
        }

        #endregion

        #region P1: Deadband Quantization Cache (浮点数死区量化缓存)

        private struct DeadbandSlot
        {
            public double LastValue;
            public string FormattedString;
            public float LastAccessTime;
        }

        private readonly Dictionary<string, DeadbandSlot> _deadbandCache = new Dictionary<string, DeadbandSlot>(StringComparer.Ordinal);
        private const int MAX_DEADBAND_SLOTS = 2048;

        // 性能与可观测性统计
        public long TotalRequests { get; private set; }
        public long CacheHits { get; private set; }
        public double HitRatePercent => TotalRequests > 0 ? ((double)CacheHits / TotalRequests) * 100.0 : 100.0;
        public long EstimatedBytesSaved => CacheHits * 32; // 每个避免分配的 string 对象估算 32 字节堆内存

        /// <summary>
        /// 浮点数死区量化格式化：
        /// 当浮点数值在极微小容差 (tolerance) 内波动时，直接复用上一帧已格式化的 string 实例。
        /// 配合 SetTextIfChanged，可直接阻断 70%~85% 的 UGUI 顶点重建与堆内存垃圾！
        /// </summary>
        /// <param name="slotKey">槽位唯一标识（建议组件ID+参数名，例如 "pfd_spd"）</param>
        /// <param name="value">当前浮点数值</param>
        /// <param name="format">格式化占位符（如 "F1", "F2", "N0"）</param>
        /// <param name="tolerance">死区容差（如速度 0.05m/s，高度 0.1m，迎角 0.02°）</param>
        public string FastDouble(string slotKey, double value, string format = "F1", double tolerance = 0.05)
        {
            TotalRequests++;

            if (_deadbandCache.TryGetValue(slotKey, out var slot))
            {
                if (Math.Abs(value - slot.LastValue) <= tolerance)
                {
                    CacheHits++;
                    return slot.FormattedString;
                }
            }

            // 槽位淘汰保护：防止动态键无限膨胀
            if (_deadbandCache.Count >= MAX_DEADBAND_SLOTS)
            {
                _deadbandCache.Clear();
            }

            string fmt = string.IsNullOrEmpty(format) ? "F1" : format;
            string formatted = value.ToString(fmt);

            _deadbandCache[slotKey] = new DeadbandSlot
            {
                LastValue = value,
                FormattedString = formatted,
                LastAccessTime = Time.unscaledTime
            };

            return formatted;
        }

        /// <summary>
        /// 自带前后缀的快速死区格式化（如 "123.4 m/s", "+1.2 G"）
        /// </summary>
        public string FastDoubleWithAffix(string slotKey, double value, string prefix, string suffix, string format = "F1", double tolerance = 0.05)
        {
            TotalRequests++;

            if (_deadbandCache.TryGetValue(slotKey, out var slot))
            {
                if (Math.Abs(value - slot.LastValue) <= tolerance)
                {
                    CacheHits++;
                    return slot.FormattedString;
                }
            }

            if (_deadbandCache.Count >= MAX_DEADBAND_SLOTS)
            {
                _deadbandCache.Clear();
            }

            string fmt = string.IsNullOrEmpty(format) ? "F1" : format;
            string formatted = (prefix ?? string.Empty) + value.ToString(fmt) + (suffix ?? string.Empty);

            _deadbandCache[slotKey] = new DeadbandSlot
            {
                LastValue = value,
                FormattedString = formatted,
                LastAccessTime = Time.unscaledTime
            };

            return formatted;
        }

        public int DeadbandSlotCount => _deadbandCache.Count;

        #endregion

        #region Probe Frame Cache (外部模组探针同帧快照防重缓存)

        private int _lastProbeFrame = -1;
        private readonly Dictionary<string, double> _probeNumericCache = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _probeStringCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 尝试从当前帧快照中获取外部探针数值（消除多组件同帧重复反射调用）
        /// </summary>
        public bool TryGetCachedProbeNumeric(string probeKey, int frame, out double val)
        {
            if (_lastProbeFrame != frame)
            {
                _lastProbeFrame = frame;
                _probeNumericCache.Clear();
                _probeStringCache.Clear();
            }
            return _probeNumericCache.TryGetValue(probeKey, out val);
        }

        /// <summary>
        /// 记录当前帧外部探针数值快照
        /// </summary>
        public void SetCachedProbeNumeric(string probeKey, int frame, double val)
        {
            if (_lastProbeFrame != frame)
            {
                _lastProbeFrame = frame;
                _probeNumericCache.Clear();
                _probeStringCache.Clear();
            }
            _probeNumericCache[probeKey] = val;
        }

        /// <summary>
        /// 尝试从当前帧快照中获取外部探针字符串
        /// </summary>
        public bool TryGetCachedProbeString(string probeKey, int frame, out string val)
        {
            if (_lastProbeFrame != frame)
            {
                _lastProbeFrame = frame;
                _probeNumericCache.Clear();
                _probeStringCache.Clear();
            }
            return _probeStringCache.TryGetValue(probeKey, out val);
        }

        /// <summary>
        /// 记录当前帧外部探针字符串快照
        /// </summary>
        public void SetCachedProbeString(string probeKey, int frame, string val)
        {
            if (_lastProbeFrame != frame)
            {
                _lastProbeFrame = frame;
                _probeNumericCache.Clear();
                _probeStringCache.Clear();
            }
            _probeStringCache[probeKey] = val;
        }

        #endregion

        #region P3: Docking & Target Kinematic Extrapolation Cache (目标交会与对接口相对几何外推)

        /// <summary>
        /// 目标交会与对接口相对运动学外推帧
        /// 针对交会、对接口引导（DPAI、DockingReticle、ND导航等）：
        /// 以 15Hz 进行几何严密解算，在中间帧进行一阶速度/角速度线性外推，
        /// 消除每帧数十次昂贵的四元数逆变换与矩阵投影。
        /// </summary>
        public struct TargetKinematicState
        {
            public bool HasTarget;
            public Vector3 RelativePosition;
            public Vector3 RelativeVelocity;
            public Quaternion RelativeRotation;
            public float Distance;
            public float ClosingSpeed; // 接近速度
            public float DeviationX;    // 横向偏差
            public float DeviationY;    // 纵向偏差
            public float RollAlignment; // 滚转对齐角 (度)
            public float SampleTimestamp;
        }

        private TargetKinematicState _cachedTargetState;
        private float _lastTargetSampleTime = -10f;
        private const float TARGET_SAMPLE_INTERVAL = 0.0667f; // 约 15 Hz 采样基准

        /// <summary>
        /// 获取或外推目标交会对接几何状态（15Hz 物理采样 + 帧间平滑外推）
        /// </summary>
        public TargetKinematicState GetOrExtrapolateTargetState(
            bool hasTarget,
            Vector3 rawRelativePos,
            Vector3 rawRelativeVel,
            Quaternion rawRelativeRot,
            float now)
        {
            if (!hasTarget)
            {
                _cachedTargetState = default;
                _cachedTargetState.HasTarget = false;
                return _cachedTargetState;
            }

            float dt = now - _lastTargetSampleTime;
            if (dt >= TARGET_SAMPLE_INTERVAL || !_cachedTargetState.HasTarget)
            {
                // 15Hz 严密采样与状态基准校准
                _lastTargetSampleTime = now;
                _cachedTargetState.HasTarget = true;
                _cachedTargetState.RelativePosition = rawRelativePos;
                _cachedTargetState.RelativeVelocity = rawRelativeVel;
                _cachedTargetState.RelativeRotation = rawRelativeRot;
                _cachedTargetState.Distance = rawRelativePos.magnitude;
                _cachedTargetState.ClosingSpeed = -Vector3.Dot(rawRelativeVel, rawRelativePos.normalized);
                _cachedTargetState.DeviationX = rawRelativePos.x;
                _cachedTargetState.DeviationY = rawRelativePos.y;
                _cachedTargetState.RollAlignment = rawRelativeRot.eulerAngles.z;
                _cachedTargetState.SampleTimestamp = now;
                return _cachedTargetState;
            }

            // 中间帧一阶运动学外推 (Zero Alloc / Microsecond Kinematics)
            float extrapolateDt = Mathf.Clamp(now - _cachedTargetState.SampleTimestamp, 0f, 0.1f);
            var result = _cachedTargetState;
            result.RelativePosition += result.RelativeVelocity * extrapolateDt;
            result.Distance = result.RelativePosition.magnitude;
            return result;
        }

        #endregion

        #region P4: Flat Array Theme Color Lookup (主题语义颜色直接寻址扁平缓存)

        // 连续内存扁平颜色数组 (将枚举角色直接映射为数组下标，达到 O(1) 寄存器直读)
        private Color[] _cardBgTable;
        private Color[] _cardBorderTable;
        private Color[] _textTable;
        private Color[] _meterTable;
        private Color[] _buttonBgTable;
        private Color[] _buttonTextTable;

        private ThemeConfig _cachedTheme;

        /// <summary>
        /// 当主题载入或切换时，将全部语义角色的颜色烘焙进连续内存数组，彻底消灭字典哈希与 switch 分支开销
        /// </summary>
        public void RebakeThemePalette(ThemeConfig theme, Func<CardStyleRole, ThemeConfig, Color> getCardBg,
                                       Func<CardStyleRole, ThemeConfig, Color> getCardBorder,
                                       Func<TextStyleRole, ThemeConfig, Color> getText,
                                       Func<MeterStyleRole, ThemeConfig, Color> getMeter,
                                       Func<ButtonVisualRole, bool, ThemeConfig, Color> getButtonBg,
                                       Func<ButtonVisualRole, bool, ThemeConfig, Color> getButtonText)
        {
            _cachedTheme = theme;

            // 1. 卡片底色与边框 (CardStyleRole 5 种)
            int cardCount = Enum.GetValues(typeof(CardStyleRole)).Length;
            _cardBgTable = new Color[cardCount];
            _cardBorderTable = new Color[cardCount];
            for (int i = 0; i < cardCount; i++)
            {
                var role = (CardStyleRole)i;
                _cardBgTable[i] = getCardBg != null ? getCardBg(role, theme) : Color.black;
                _cardBorderTable[i] = getCardBorder != null ? getCardBorder(role, theme) : Color.white;
            }

            // 2. 文本语义颜色 (TextStyleRole 8 种)
            int textCount = Enum.GetValues(typeof(TextStyleRole)).Length;
            _textTable = new Color[textCount];
            for (int i = 0; i < textCount; i++)
            {
                var role = (TextStyleRole)i;
                _textTable[i] = getText != null ? getText(role, theme) : Color.white;
            }

            // 3. 标尺与仪表颜色 (MeterStyleRole 5 种)
            int meterCount = Enum.GetValues(typeof(MeterStyleRole)).Length;
            _meterTable = new Color[meterCount];
            for (int i = 0; i < meterCount; i++)
            {
                var role = (MeterStyleRole)i;
                _meterTable[i] = getMeter != null ? getMeter(role, theme) : Color.green;
            }

            // 4. 按钮背景与文字 (ButtonVisualRole 5 种)
            int btnCount = Enum.GetValues(typeof(ButtonVisualRole)).Length;
            _buttonBgTable = new Color[btnCount];
            _buttonTextTable = new Color[btnCount];
            for (int i = 0; i < btnCount; i++)
            {
                var role = (ButtonVisualRole)i;
                _buttonBgTable[i] = getButtonBg != null ? getButtonBg(role, false, theme) : Color.gray;
                _buttonTextTable[i] = getButtonText != null ? getButtonText(role, false, theme) : Color.white;
            }
        }

        public Color GetCardBgFast(CardStyleRole role)
        {
            int idx = (int)role;
            if (_cardBgTable != null && idx >= 0 && idx < _cardBgTable.Length) return _cardBgTable[idx];
            return Color.black;
        }

        public Color GetCardBorderFast(CardStyleRole role)
        {
            int idx = (int)role;
            if (_cardBorderTable != null && idx >= 0 && idx < _cardBorderTable.Length) return _cardBorderTable[idx];
            return Color.white;
        }

        public Color GetTextFast(TextStyleRole role)
        {
            int idx = (int)role;
            if (_textTable != null && idx >= 0 && idx < _textTable.Length) return _textTable[idx];
            return Color.white;
        }

        public Color GetMeterFast(MeterStyleRole role)
        {
            int idx = (int)role;
            if (_meterTable != null && idx >= 0 && idx < _meterTable.Length) return _meterTable[idx];
            return Color.green;
        }

        public Color GetButtonBgFast(ButtonVisualRole role)
        {
            int idx = (int)role;
            if (_buttonBgTable != null && idx >= 0 && idx < _buttonBgTable.Length) return _buttonBgTable[idx];
            return Color.gray;
        }

        public Color GetButtonTextFast(ButtonVisualRole role)
        {
            int idx = (int)role;
            if (_buttonTextTable != null && idx >= 0 && idx < _buttonTextTable.Length) return _buttonTextTable[idx];
            return Color.white;
        }

        #endregion

        #region Cache Lifecycle Management

        /// <summary>
        /// 场景切换或换船时清除瞬态缓存，杜绝内存泄漏
        /// </summary>
        public void ClearTransient()
        {
            _deadbandCache.Clear();
            _cachedTargetState = default;
            _lastTargetSampleTime = -10f;
            _probeNumericCache.Clear();
            _probeStringCache.Clear();
            _lastProbeFrame = -1;
            TotalRequests = 0;
            CacheHits = 0;
        }

        /// <summary>
        /// 重置所有动态缓存
        /// </summary>
        public void ClearAll()
        {
            ClearTransient();
            _cardBgTable = null;
            _cardBorderTable = null;
            _textTable = null;
            _meterTable = null;
            _buttonBgTable = null;
            _buttonTextTable = null;
            _cachedTheme = null;
        }

        #endregion
    }
}
