using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.Core.Probes;

namespace ModularFlightPanel.Core
{
    public interface IFlightProbe
    {
        string ProbeId { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        bool IsEnabled { get; set; }
        float PollingInterval { get; }
        float LastPollTime { get; set; }
        int ConsecutiveErrors { get; set; }

        void Initialize();
        void UpdateTelemetry(Vessel activeVessel, IFlightTelemetry telemetry);
        void Shutdown();
    }

    /// <summary>
    /// 全局探针统一中枢与场景搜索排队调度器 (ProbeManager)
    /// 解决核心性能痛点：
    /// 1. 严格排队节流全场景对象查找 (FindObjectOfType)，全局 + 逐探针双重冷却，
    ///    彻底根绝同帧多探针抢跑昂贵场景查询造成的微卡顿
    /// 2. 统一管理 15 大外部 Mod 反射探针与内置官方 UI Hook
    /// 3. 健康监控与异常熔断机制（连续报错自动降频/隔离，防止异常风暴）
    /// 
    /// 所有节流与熔断参数集中在 Tuning Policy 区，禁止在逻辑中散落字面量。
    /// </summary>
    public class ProbeManager
    {
        #region Tuning Policy (节流与熔断参数的唯一出处)

        /// <summary>全局场景搜索冷却（秒）：任意两次 FindObjectOfType 的最小间隔</summary>
        public const float DefaultSceneSearchInterval = 3.0f;

        /// <summary>连续异常次数达到该值时进入熔断降频</summary>
        public const int CircuitBreakerErrorThreshold = 5;

        /// <summary>熔断后的慢速重试间隔（秒）</summary>
        public const float CircuitBreakerRetryInterval = 10.0f;

        /// <summary>标准探针轮询间隔（秒）</summary>
        public const float DefaultProbePollingInterval = 0.1f;

        /// <summary>船体剪影烘焙探针轮询间隔（秒）：渲染侧成本更高，单独降频</summary>
        public const float SilhouetteProbePollingInterval = 0.2f;

        #endregion

        private static ProbeManager _instance;
        public static ProbeManager Instance => _instance ?? (_instance = new ProbeManager());

        private readonly List<IFlightProbe> _probes = new List<IFlightProbe>();
        private readonly Dictionary<string, IFlightProbe> _probeMap = new Dictionary<string, IFlightProbe>(StringComparer.OrdinalIgnoreCase);

        // 全局场景搜索排队调度器 + 逐探针记账
        private readonly Dictionary<string, float> _lastSceneSearchByProbe = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private float _lastGlobalSceneSearchTime = -10f;

        /// <summary>全局排队冷却（秒），默认见 DefaultSceneSearchInterval</summary>
        public float SceneSearchInterval { get; set; } = DefaultSceneSearchInterval;

        /// <summary>场景搜索累计批准次数（诊断用）</summary>
        public int SceneSearchGrantedCount { get; private set; }

        /// <summary>场景搜索被节流拒绝的次数（诊断用）</summary>
        public int SceneSearchThrottledCount { get; private set; }

        public IReadOnlyList<IFlightProbe> Probes => _probes;
        public int TotalProbeCount => _probes.Count;
        public int AvailableProbeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _probes.Count; i++)
                {
                    if (_probes[i].IsAvailable) count++;
                }
                return count;
            }
        }

        private bool _isInitialized = false;

        private ProbeManager()
        {
        }

        #region Scene Search Scheduler (Anti-Stutter Throttling)

        /// <summary>
        /// 询问全局排队调度器当前是否被允许执行昂贵的全场景搜索 (FindObjectOfType)。
        /// 双重门：全局冷却（所有探针共享）+ 逐探针冷却（同一探针不得连续抢跑）。
        /// 两者都通过才批准，并同时刷新两本账。
        /// </summary>
        /// <param name="probeId">发起查询的探针 Id；为空时仅受全局冷却约束（兼容无身份调用）</param>
        public bool CanExecuteSceneSearch(string probeId)
        {
            float now = Time.unscaledTime;
            bool hasIdentity = !string.IsNullOrEmpty(probeId);

            bool globalOk = now - _lastGlobalSceneSearchTime >= SceneSearchInterval;

            bool probeOk = true;
            if (hasIdentity && _lastSceneSearchByProbe.TryGetValue(probeId, out float lastProbeSearch))
            {
                probeOk = now - lastProbeSearch >= SceneSearchInterval;
            }

            if (!globalOk || !probeOk)
            {
                SceneSearchThrottledCount++;
                return false;
            }

            _lastGlobalSceneSearchTime = now;
            if (hasIdentity) _lastSceneSearchByProbe[probeId] = now;
            SceneSearchGrantedCount++;
            return true;
        }

        /// <summary>查询某探针上次获批场景搜索的时刻（诊断用；无记录时返回 float.NegativeInfinity）</summary>
        public float GetLastSceneSearchTime(string probeId)
        {
            if (string.IsNullOrEmpty(probeId)) return _lastGlobalSceneSearchTime;
            return _lastSceneSearchByProbe.TryGetValue(probeId, out float t) ? t : float.NegativeInfinity;
        }

        /// <summary>重置节流门（切场景 / 重新初始化时调用，避免旧账跨越生命周期）</summary>
        public void ResetSceneSearchGate()
        {
            _lastGlobalSceneSearchTime = -10f;
            _lastSceneSearchByProbe.Clear();
            SceneSearchGrantedCount = 0;
            SceneSearchThrottledCount = 0;
        }

        #endregion

        #region Probe Lifecycle & Registration

        public void InitializeAll()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            ResetSceneSearchGate();

            // 1. 初始化外部 Mod 遍历探针中枢
            try
            {
                TelemetryProbeManager.InitializeAll();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] TelemetryProbeManager init error: {ex.Message}");
            }

            // 2. 注册并装配核心探针（统一绑定式适配器，替代过去 8 份逐字重复的样板类）
            RegisterProbe(new ProbeBinding("Principia", "Principia N-Body Orbit & Navball Hook",
                DefaultProbePollingInterval, () => PrincipiaProbe.IsAvailable, PrincipiaProbe.Initialize));

            RegisterProbe(new ProbeBinding("GPWS", "KSP Ground Proximity Warning System",
                DefaultProbePollingInterval, () => GPWSProbe.IsAvailable, GPWSProbe.Initialize));

            RegisterProbe(new ProbeBinding("DPAI", "Docking Port Alignment Indicator",
                DefaultProbePollingInterval, () => DockingAlignmentProbe.IsAvailable, DockingAlignmentProbe.Initialize));

            RegisterProbe(new ProbeBinding("Trajectories", "Trajectories Atmospheric Descent & Impact",
                DefaultProbePollingInterval, () => TrajectoriesProbe.IsAvailable, TrajectoriesProbe.Initialize));

            RegisterProbe(new ProbeBinding("FAR", "Ferram Aerospace Research",
                DefaultProbePollingInterval, () => FarProbe.IsAvailable, FarProbe.Initialize));

            RegisterProbe(new ProbeBinding("MechJeb", "MechJeb Autopilot & Vessel Stats",
                DefaultProbePollingInterval, () => MechJebProbe.IsAvailable, MechJebProbe.Initialize));

            RegisterProbe(new ProbeBinding("KER", "Kerbal Engineer Redux",
                DefaultProbePollingInterval, () => KerbalEngineerProbe.IsAvailable, KerbalEngineerProbe.Initialize));

            RegisterProbe(new ProbeBinding("VesselSilhouette", "Dynamic Vessel Silhouette Baker",
                SilhouetteProbePollingInterval, () => true, BindSilhouetteProvider));

            // 3. 执行已注册探针的启动钩子
            for (int i = 0; i < _probes.Count; i++)
            {
                try
                {
                    _probes[i].Initialize();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ModularFlightPanel] Probe {_probes[i].ProbeId} init warning: {ex.Message}");
                }
            }

            Debug.Log($"[ModularFlightPanel] ProbeManager initialized with {_probes.Count} flight telemetry probes ({AvailableProbeCount} available).");
        }

        public void RegisterProbe(IFlightProbe probe)
        {
            if (probe == null) return;
            if (_probeMap.ContainsKey(probe.ProbeId)) return;

            _probes.Add(probe);
            _probeMap[probe.ProbeId] = probe;
        }

        public IFlightProbe GetProbe(string probeId)
        {
            if (string.IsNullOrEmpty(probeId)) return null;
            _probeMap.TryGetValue(probeId, out var probe);
            return probe;
        }

        private static void BindSilhouetteProvider()
        {
            if (VesselSilhouetteService.Provider == null)
            {
                VesselSilhouetteService.Provider = VesselSilhouetteBaker.Instance;
            }
        }

        #endregion

        #region Polling Loop & Circuit Breaker

        /// <summary>
        /// 由 TelemetryHub.UpdateSubsystemTelemetry (10Hz) 单点调用的探针分发
        /// 具备连续异常熔断保护与独立步长调度
        /// </summary>
        public void UpdateAllProbes(Vessel activeVessel, IFlightTelemetry telemetry)
        {
            if (activeVessel == null || telemetry == null) return;

            float now = Time.unscaledTime;
            MFPProfiler.BeginSample(ProfilerSection.Probes);
            try
            {
                int count = _probes.Count;
                for (int i = 0; i < count; i++)
                {
                    var p = _probes[i];
                    if (!p.IsEnabled || !p.IsAvailable) continue;

                    // 异常熔断保护：连续错误达到阈值后，进入慢速降频重试模式
                    float effectiveInterval = p.ConsecutiveErrors >= CircuitBreakerErrorThreshold
                        ? CircuitBreakerRetryInterval
                        : p.PollingInterval;

                    if (now - p.LastPollTime < effectiveInterval)
                    {
                        continue;
                    }

                    p.LastPollTime = now;

                    UnityEngine.Profiling.Profiler.BeginSample("MFP.Probe." + p.ProbeId);
                    try
                    {
                        p.UpdateTelemetry(activeVessel, telemetry);
                        p.ConsecutiveErrors = 0; // 成功则重置错误计数
                    }
                    catch (Exception ex)
                    {
                        p.ConsecutiveErrors++;
                        if (p.ConsecutiveErrors == 1 || p.ConsecutiveErrors == CircuitBreakerErrorThreshold)
                        {
                            Debug.LogWarning($"[ModularFlightPanel] Probe '{p.ProbeId}' warning (fail count: {p.ConsecutiveErrors}): {ex.Message}");
                        }
                    }
                    finally
                    {
                        UnityEngine.Profiling.Profiler.EndSample();
                    }
                }
            }
            finally
            {
                MFPProfiler.EndSample(ProfilerSection.Probes);
            }
        }

        #endregion

        #region Built-in Probe Adapters

        /// <summary>
        /// 统一探针绑定适配器：把静态探针类（IsAvailable / Initialize / UpdateTelemetry）
        /// 绑定为 IFlightProbe，替代过去 8 份逐字重复的私有样板类。
        /// update 为 null 表示该探针数据由事件驱动或按需反射读取，轮询循环仅承担健康心跳与熔断统计。
        /// </summary>
        private sealed class ProbeBinding : IFlightProbe
        {
            private readonly Func<bool> _availability;
            private readonly Action _initialize;
            private readonly Action<Vessel, IFlightTelemetry> _update;

            public ProbeBinding(string probeId, string displayName, float pollingInterval,
                Func<bool> availability, Action initialize = null, Action<Vessel, IFlightTelemetry> update = null)
            {
                ProbeId = probeId;
                DisplayName = displayName;
                PollingInterval = pollingInterval;
                _availability = availability;
                _initialize = initialize;
                _update = update;
            }

            public string ProbeId { get; }
            public string DisplayName { get; }
            public bool IsAvailable => _availability == null || _availability();
            public bool IsEnabled { get; set; } = true;
            public float PollingInterval { get; }
            public float LastPollTime { get; set; } = -10f;
            public int ConsecutiveErrors { get; set; } = 0;

            public void Initialize() => _initialize?.Invoke();
            public void UpdateTelemetry(Vessel v, IFlightTelemetry t) => _update?.Invoke(v, t);
            public void Shutdown() { }
        }

        #endregion
    }
}
