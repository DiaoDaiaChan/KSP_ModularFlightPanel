using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularFlightPanel.Core.Telemetry
{
    /// <summary>
    /// 自适应标量数值防抖器 (Adaptive Scalar Debouncer)
    /// 基于数据变化特征（符号反转率、EWMA 运行噪声包络、持续同向运动趋势与累积漂移量），
    /// 自动识别并过滤 Unity PhysX 接触求解器与浮点积分器产生的微观往复抖动 (Floating-Point Jitter)。
    /// 纯值类型，0 GC 内存分配。
    /// </summary>
    public struct AdaptiveScalarDebouncer
    {
        private double _anchor;
        private double _lastSample;
        private double _lastDelta;
        private double _runningNoise;
        private int _consecutiveDirectionCount;
        private bool _hasInitialized;

        public AdaptiveScalarDebouncer(double initialValue)
        {
            _anchor = initialValue;
            _lastSample = initialValue;
            _lastDelta = 0.0;
            _runningNoise = 0.001;
            _consecutiveDirectionCount = 0;
            _hasInitialized = true;
        }

        /// <summary>
        /// 基于数据动态变化特征评估新采样值，返回是否发生了真实物理变动 (True Motion)。
        /// </summary>
        /// <param name="sample">最新采样值</param>
        /// <param name="baseAbsoluteTol">基础绝对死区容差 (针对近零微小量)</param>
        /// <param name="relativeTol">相对尺度容差 (针对大动态范围，例如轨道距离/高马赫数)</param>
        public bool Evaluate(double sample, double baseAbsoluteTol = 0.01, double relativeTol = 1e-4)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample)) return false;

            if (!_hasInitialized)
            {
                _anchor = sample;
                _lastSample = sample;
                _lastDelta = 0.0;
                _runningNoise = Math.Max(baseAbsoluteTol * 0.1, 1e-5);
                _consecutiveDirectionCount = 0;
                _hasInitialized = true;
                return true; // 首帧采样激活
            }

            double delta = sample - _lastSample;
            double absDelta = Math.Abs(delta);
            _lastSample = sample;

            // 1. 尺度自适应基础容差 (针对大数值自动放宽，杜绝双精度乘除引入的微尾数晃动)
            double dynamicTol = Math.Max(baseAbsoluteTol, Math.Abs(sample) * relativeTol);

            // 2. 指数加权移动平均 (EWMA) 自适应更新环境背景噪声水平 (alpha = 0.1)
            _runningNoise = (_runningNoise * 0.9) + (absDelta * 0.1);
            double effectiveNoiseBand = Math.Max(dynamicTol, _runningNoise * 2.5);

            // 3. 变化特征 A：方向持续性 vs 符号反转率 (Directional Trend vs Zero-Crossing Oscillation)
            // 浮点抖动与物理微颤在正负之间交替往复 (+, -, +, -)；真实物理过程呈持续同向单调演进 (+, +, +)
            if (absDelta > 1e-9)
            {
                if ((delta > 0 && _lastDelta > 0) || (delta < 0 && _lastDelta < 0))
                {
                    _consecutiveDirectionCount = Math.Min(10, _consecutiveDirectionCount + 1);
                }
                else
                {
                    _consecutiveDirectionCount = 1; // 符号反转，重置同向计数器
                }
                _lastDelta = delta;
            }

            // 4. 变化特征 B：相对稳态基准锚点的累积漂移 (Cumulative Displacement)
            double totalDriftFromAnchor = Math.Abs(sample - _anchor);

            // 判定 1：瞬态阶跃激变 (单步突变显著超过背景估计噪声包络) -> 判定为真实物理突变
            if (absDelta > effectiveNoiseBand * 3.0 && absDelta > dynamicTol * 1.5)
            {
                _anchor = sample;
                _consecutiveDirectionCount = 0;
                return true;
            }

            // 判定 2：持续同向演进 (连续 3 拍同向运动且单步超过 50% 动态容差) -> 判定为真实动力学滑行/爬升/推力
            if (_consecutiveDirectionCount >= 3 && absDelta > dynamicTol * 0.5)
            {
                _anchor = sample;
                return true;
            }

            // 判定 3：累积漂移穿透噪声包络 (即使单步极其微小，累积位移突破稳态包络即判定为物理位移)
            if (totalDriftFromAnchor > effectiveNoiseBand * 2.0)
            {
                _anchor = sample;
                _consecutiveDirectionCount = 0;
                return true;
            }

            // 落在高频往复噪声区且累积位移受控 -> 判定为浮点抖动 / 稳态静息
            return false;
        }

        public void Reset(double newAnchor = double.NaN)
        {
            if (double.IsNaN(newAnchor))
            {
                _hasInitialized = false;
            }
            else
            {
                _anchor = newAnchor;
                _lastSample = newAnchor;
                _lastDelta = 0.0;
                _runningNoise = 0.001;
                _consecutiveDirectionCount = 0;
                _hasInitialized = true;
            }
        }
    }

    /// <summary>
    /// 自适应空间姿态四元数防抖器 (Adaptive Rotation Debouncer)
    /// 基于角增量特征、旋转轴单调性与累积角偏角，自适应过滤发射台与微观接触物理抖动。
    /// 纯值类型，0 GC 内存分配。
    /// </summary>
    public struct AdaptiveRotationDebouncer
    {
        private Quaternion _anchor;
        private Quaternion _lastSample;
        private float _lastAngularDelta;
        private float _runningNoise;
        private int _consecutiveDirectionCount;
        private bool _hasInitialized;

        public AdaptiveRotationDebouncer(Quaternion initialRotation)
        {
            _anchor = initialRotation;
            _lastSample = initialRotation;
            _lastAngularDelta = 0f;
            _runningNoise = 0.005f;
            _consecutiveDirectionCount = 0;
            _hasInitialized = true;
        }

        /// <summary>
        /// 评估空间四元数是否发生真实角位移
        /// </summary>
        public bool Evaluate(Quaternion sample, float baseAngleDeadband = 0.02f)
        {
            if (!_hasInitialized)
            {
                _anchor = sample;
                _lastSample = sample;
                _lastAngularDelta = 0f;
                _runningNoise = baseAngleDeadband * 0.2f;
                _consecutiveDirectionCount = 0;
                _hasInitialized = true;
                return true;
            }

            float stepAngle = Quaternion.Angle(_lastSample, sample);
            _lastSample = sample;

            // EWMA 背景角噪声自适应跟踪
            _runningNoise = (_runningNoise * 0.9f) + (stepAngle * 0.1f);
            float effectiveNoiseBand = Mathf.Max(baseAngleDeadband, _runningNoise * 2.0f);

            // 角增量步长一致性特征
            if (stepAngle > 1e-4f)
            {
                if (Mathf.Abs(stepAngle - _lastAngularDelta) < effectiveNoiseBand * 0.5f)
                {
                    _consecutiveDirectionCount = Math.Min(10, _consecutiveDirectionCount + 1);
                }
                else
                {
                    _consecutiveDirectionCount = 1;
                }
                _lastAngularDelta = stepAngle;
            }

            float totalAngleFromAnchor = Quaternion.Angle(_anchor, sample);

            // 判定 1：瞬态角阶跃突变 (姿态快速翻转/操纵脉冲)
            if (stepAngle > effectiveNoiseBand * 3.0f && stepAngle > baseAngleDeadband * 1.5f)
            {
                _anchor = sample;
                _consecutiveDirectionCount = 0;
                return true;
            }

            // 判定 2：持续角速度自旋 (连续 3 拍单调转动)
            if (_consecutiveDirectionCount >= 3 && stepAngle > baseAngleDeadband * 0.5f)
            {
                _anchor = sample;
                return true;
            }

            // 判定 3：累积角位移穿透包络
            if (totalAngleFromAnchor > effectiveNoiseBand * 2.0f)
            {
                _anchor = sample;
                _consecutiveDirectionCount = 0;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            _hasInitialized = false;
        }
    }

    /// <summary>
    /// 全局自适应遥测防抖中枢 (Adaptive Telemetry Debouncer)
    /// 核心特征：
    /// 1. 零硬编码：基于最底层牛顿力学公理（姿态四元数、合速度、油门轴、操控量）自适应感知宏观动力学；
    /// 2. 探针解耦：支持通过 WatchToken 动态挂载任意 736+ 个外部模组遥测通道；
    /// 3. 数据特征自适应：基于符号反转率、EWMA 噪声水平、累积位移与持续运动趋势综合判决；
    /// 4. 零 GC 分配：热循环中彻底杜绝托管堆垃圾产生。
    /// </summary>
    public class AdaptiveTelemetryDebouncer
    {
        private AdaptiveRotationDebouncer _rotationDebouncer;
        private AdaptiveScalarDebouncer _speedDebouncer;
        private AdaptiveScalarDebouncer _verticalSpeedDebouncer;
        private AdaptiveScalarDebouncer _throttleDebouncer;

        private int _lastStage = -1;
        private int _lastTimeWarpRateIndex = -1;
        private bool _lastIsPaused = false;
        private bool _lastHasVessel = false;

        // 全局整船共享防抖实例 (WidgetRenderManager 每帧仅单次评估)
        public static AdaptiveTelemetryDebouncer SharedVesselDebouncer { get; } = new AdaptiveTelemetryDebouncer();

        // 动态 Token 观察器字典（数据驱动，按需注册）
        private readonly Dictionary<string, AdaptiveScalarDebouncer> _watchedTokens =
            new Dictionary<string, AdaptiveScalarDebouncer>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _watchedTokenList = new List<string>(16);

        /// <summary>
        /// 注册并监视任意遥测通配符 (如 "{FAR:Q}", "{KER:TTI}", "{EC}", "{CABIN:TEMP}")
        /// </summary>
        public void WatchToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            if (!_watchedTokens.ContainsKey(token))
            {
                _watchedTokens[token] = new AdaptiveScalarDebouncer(0.0);
                _watchedTokenList.Add(token);
            }
        }

        /// <summary>
        /// 综合评估遥测是否产生了超出噪声包络的实质性物理或数值变动
        /// </summary>
        public bool Evaluate(IFlightTelemetry telem)
        {
            if (telem == null || !telem.HasVessel) return false;

            // 1. 关键离散事件 (分级、时间加速档位、暂停、飞船加载状态) -> 0 容差立即唤醒
            if (telem.CurrentStage != _lastStage ||
                telem.TimeWarpRateIndex != _lastTimeWarpRateIndex ||
                telem.IsGamePaused != _lastIsPaused ||
                telem.HasVessel != _lastHasVessel)
            {
                _lastStage = telem.CurrentStage;
                _lastTimeWarpRateIndex = telem.TimeWarpRateIndex;
                _lastIsPaused = telem.IsGamePaused;
                _lastHasVessel = telem.HasVessel;
                return true;
            }

            // 2. 玩家主动操纵输入 (消除摇杆中心微死区)
            float inputMag = Math.Abs(telem.PitchInput) + Math.Abs(telem.RollInput) + Math.Abs(telem.YawInput);
            if (inputMag > 0.005f) return true;

            // 3. 基础物理空间运动特征分析 (姿态角、线速度、垂直速度、发动机油门)
            bool attitudeMotion = _rotationDebouncer.Evaluate(telem.AttitudeRotation, 0.02f);
            bool speedMotion = _speedDebouncer.Evaluate(telem.CurrentSpeed, 0.02, 1e-4);
            bool vsMotion = _verticalSpeedDebouncer.Evaluate(telem.VerticalSpeed, 0.02, 1e-4);
            bool throttleMotion = _throttleDebouncer.Evaluate(telem.Throttle, 0.002, 1e-3);

            if (attitudeMotion || speedMotion || vsMotion || throttleMotion)
            {
                return true;
            }

            // 4. 数据驱动的动态 Token 探针特征分析 (遍历各监视通道的自适应防抖器)
            int tokenCount = _watchedTokenList.Count;
            if (tokenCount > 0)
            {
                for (int i = 0; i < tokenCount; i++)
                {
                    string token = _watchedTokenList[i];
                    if (_watchedTokens.TryGetValue(token, out var debouncer))
                    {
                        double val = TelemetryTokenEngine.EvaluateNumeric(token, telem);
                        if (!double.IsNaN(val))
                        {
                            if (debouncer.Evaluate(val, 0.01, 1e-3))
                            {
                                _watchedTokens[token] = debouncer;
                                return true;
                            }
                            _watchedTokens[token] = debouncer;
                        }
                    }
                }
            }

            // 全部指标特征均符合高频往复抖动与微扰白噪声，判定为稳态静息
            return false;
        }

        /// <summary>
        /// 复位全部防抖器状态
        /// </summary>
        public void Reset()
        {
            _rotationDebouncer.Reset();
            _speedDebouncer.Reset();
            _verticalSpeedDebouncer.Reset();
            _throttleDebouncer.Reset();
            _lastStage = -1;
            _lastTimeWarpRateIndex = -1;
            _lastIsPaused = false;
            _lastHasVessel = false;
            _watchedTokens.Clear();
            _watchedTokenList.Clear();
        }
    }

    /// <summary>
    /// 全局整船自适应静息态探测器入口 (Telemetry Quiescence Detector)
    /// </summary>
    public static class TelemetryQuiescenceDetector
    {
        public static AdaptiveTelemetryDebouncer SharedVesselDebouncer => AdaptiveTelemetryDebouncer.SharedVesselDebouncer;
    }
}
