using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// RealAntennas 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 RACommNetVessel、RACommNode、
    /// RealAntenna (活动天线)、RACommLink (活跃通信链路) 以及 Physics 射频物理常数，零遗漏。
    /// </summary>
    public static class RealAntennasProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("RA");

        private static Type _raVesselType;
        private static Type _raNodeType;
        private static Type _realAntennaType;
        private static Type _raLinkType;
        private static Type _physicsType;

        private static PropertyInfo _commProp;
        private static FieldInfo _antennaListField;
        private static MethodInfo _idlePowerMethod;
        private static MethodInfo _antennaTowardsHomeMethod;
        private static PropertyInfo _isConnectedHomeProp;
        private static readonly Dictionary<string, PropertyInfo> _propCache = new Dictionary<string, PropertyInfo>();

        public static PropertyInfo GetCachedProperty(object obj, string propName)
        {
            if (obj == null) return null;
            string key = obj.GetType().FullName + "." + propName;
            if (!_propCache.TryGetValue(key, out var prop))
            {
                prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                _propCache[key] = prop;
            }
            return prop;
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly raAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.Equals("RealAntennas", StringComparison.OrdinalIgnoreCase))
                    {
                        raAssembly = asm;
                        break;
                    }
                }

                if (raAssembly != null)
                {
                    _raVesselType = raAssembly.GetType("RealAntennas.RACommNetVessel");
                    _raNodeType = raAssembly.GetType("RealAntennas.RACommNode");
                    _realAntennaType = raAssembly.GetType("RealAntennas.RealAntenna");
                    _raLinkType = raAssembly.GetType("RealAntennas.Network.RACommLink") ?? raAssembly.GetType("RealAntennas.RACommLink");
                    _physicsType = raAssembly.GetType("RealAntennas.Physics");

                    // 1. 遍历 Physics 静态射频常数与公式
                    if (_physicsType != null)
                    {
                        Traverser.TraverseStatic(_physicsType, "RealAntennas 射频物理常数 (Physics)");
                    }

                    // 2. 遍历 RACommNetVessel
                    if (_raVesselType != null)
                    {
                        Traverser.TraverseInstance(_raVesselType, GetActiveVesselRA, "RealAntennas 载具通信中枢 (RACommNetVessel)");

                        _antennaListField = _raVesselType.GetField("antennaList", BindingFlags.Public | BindingFlags.Instance);
                        _idlePowerMethod = _raVesselType.GetMethod("IdlePowerDraw", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        _commProp = _raVesselType.GetProperty("Comm", BindingFlags.Public | BindingFlags.Instance)
                                    ?? typeof(CommNet.CommNetVessel).GetProperty("Comm", BindingFlags.Public | BindingFlags.Instance);
                        _isConnectedHomeProp = _raVesselType.GetProperty("IsConnectedHome", BindingFlags.Public | BindingFlags.Instance)
                                               ?? typeof(CommNet.CommNetVessel).GetProperty("IsConnectedHome", BindingFlags.Public | BindingFlags.Instance);
                    }

                    // 3. 遍历 RACommNode
                    if (_raNodeType != null)
                    {
                        Traverser.TraverseInstance(_raNodeType, GetActiveNode, "RealAntennas 通信节点 (RACommNode)");
                        _antennaTowardsHomeMethod = _raNodeType.GetMethod("AntennaTowardsHome", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    }

                    // 4. 遍历 RealAntenna
                    if (_realAntennaType != null)
                    {
                        Traverser.TraverseInstance(_realAntennaType, GetActiveAntenna, "RealAntennas 活动天线 (RealAntenna)");
                    }

                    // 5. 遍历 RACommLink
                    if (_raLinkType != null)
                    {
                        Traverser.TraverseInstance(_raLinkType, GetActiveLink, "RealAntennas 活跃通信链路 (RACommLink)");
                    }

                    // 6. 注册高阶通用别名与动态计算
                    RegisterDynamicRAMembers();

                    _isAvailable = true;
                    SafeLog($"[ModularFlightPanel] RealAntennasProbe successfully hooked RealAntennas! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] RealAntennas probe initialization warning: {ex.Message}");
                _isAvailable = false;
            }
        }

        private static void SafeLog(string msg)
        {
            try { Debug.Log(msg); }
            catch { Console.WriteLine(msg); }
        }

        private static void SafeLogWarning(string msg)
        {
            try { Debug.LogWarning(msg); }
            catch { Console.WriteLine("[WARN] " + msg); }
        }

        public static object GetActiveVesselRA()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return null;

            if (v.Connection != null && _raVesselType != null && _raVesselType.IsInstanceOfType(v.Connection))
            {
                return v.Connection;
            }

            if (_raVesselType != null)
            {
                try
                {
                    var comp = v.GetComponent(_raVesselType);
                    if (comp != null) return comp;
                }
                catch { }
            }

            return null;
        }

        public static object GetActiveNode()
        {
            object cnv = GetActiveVesselRA();
            if (cnv == null) return null;

            if (_commProp != null)
            {
                try { return _commProp.GetValue(cnv, null); } catch { }
            }

            if (cnv is CommNet.CommNetVessel baseCNV)
            {
                return baseCNV.Comm;
            }

            return null;
        }

        public static object GetActiveAntenna()
        {
            object node = GetActiveNode();
            if (node != null && _antennaTowardsHomeMethod != null)
            {
                try
                {
                    object ant = _antennaTowardsHomeMethod.Invoke(node, null);
                    if (ant != null) return ant;
                }
                catch { }
            }

            object cnv = GetActiveVesselRA();
            if (cnv != null && _antennaListField != null)
            {
                try
                {
                    var list = _antennaListField.GetValue(cnv) as IList;
                    if (list != null && list.Count > 0) return list[0];
                }
                catch { }
            }

            return null;
        }

        public static object GetActiveLink()
        {
            object node = GetActiveNode();
            if (node is CommNet.CommNode commNode && commNode.Net != null)
            {
                try
                {
                    var path = new CommNet.CommPath();
                    if (commNode.Net.FindHome(commNode, path) && path.Count > 0 && path.First != null)
                    {
                        return commNode[path.First.end];
                    }
                }
                catch { }
            }

            return null;
        }

        private static void RegisterDynamicRAMembers()
        {
            // 数据传输速率 (DataRate)
            Traverser.RegisterCustom("ActiveDataRate", typeof(double), () =>
            {
                object link = GetActiveLink();
                if (link != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(link, "FwdDataRate");
                        if (prop != null)
                        {
                            double rate = Convert.ToDouble(prop.GetValue(link, null));
                            if (rate > 0) return rate;
                        }
                    }
                    catch { }
                }

                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "DataRate");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "RealAntennas 通信中枢", "当前前向通信链路或天线最大数据传输速率 (bps)", new[] { "DATARATE", "RATE", "BITRATE" });

            // 天线增益 (Gain)
            Traverser.RegisterCustom("AntennaGain", typeof(float), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "Gain");
                        if (prop != null) return Convert.ToSingle(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0f;
            }, "RealAntennas 活动天线", "当前天线增益 (dBi)", new[] { "GAIN", "ANTENNAGAIN" });

            // 发射功率 (TxPower)
            Traverser.RegisterCustom("TxPower", typeof(float), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "TxPower");
                        if (prop != null) return Convert.ToSingle(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0f;
            }, "RealAntennas 活动天线", "当前发射功率 (dBm)", new[] { "TXPOWER", "POWER", "TRANSMITPOWER" });

            // 射频工作频率 (Frequency)
            Traverser.RegisterCustom("Frequency", typeof(float), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "Frequency");
                        if (prop != null) return Convert.ToSingle(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0f;
            }, "RealAntennas 活动天线", "射频载波频率 (Hz)", new[] { "FREQ", "FREQUENCY", "RFFREQ" });

            // 射频信道带宽 (Bandwidth)
            Traverser.RegisterCustom("Bandwidth", typeof(double), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "Bandwidth");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0.0;
            }, "RealAntennas 活动天线", "射频信道占用带宽 (Hz)", new[] { "BANDWIDTH", "BW" });

            // 静态功耗 (IdlePowerDraw)
            Traverser.RegisterCustom("IdlePowerDraw", typeof(double), () =>
            {
                object cnv = GetActiveVesselRA();
                if (cnv != null && _idlePowerMethod != null)
                {
                    try { return Convert.ToDouble(_idlePowerMethod.Invoke(cnv, null)); } catch { }
                }
                return 0.0;
            }, "RealAntennas 载具通信中枢", "当前天线系统静态待机电力消耗 (EC/s 或 W)", new[] { "IDLEPOWER", "IDLEEC", "IDLEPOWERDRAW" });

            // 动态发射功耗 (PowerDraw)
            Traverser.RegisterCustom("PowerDraw", typeof(float), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "PowerDraw");
                        if (prop != null) return Convert.ToSingle(prop.GetValue(ant, null));
                    }
                    catch { }
                }
                return 0f;
            }, "RealAntennas 活动天线", "天线处于发射状态时的总电力消耗", new[] { "POWERDRAW", "TXPOWERDRAW" });

            // 链路品质与信号强度 (SignalQuality / Metric)
            Traverser.RegisterCustom("SignalStrength", typeof(double), () =>
            {
                object link = GetActiveLink();
                if (link != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(link, "FwdMetric");
                        if (prop != null) return Convert.ToDouble(prop.GetValue(link, null));
                    }
                    catch { }
                }

                object cnv = GetActiveVesselRA();
                if (cnv is CommNet.CommNetVessel baseCNV)
                {
                    return baseCNV.SignalStrength;
                }
                return 0.0;
            }, "RealAntennas 活跃通信链路", "归一化链路信号品质或信噪比裕度 (0..1)", new[] { "SIGNALSTRENGTH", "STRENGTH", "METRIC", "QUALITY" });

            // 对地通信连接状态 (IsConnectedHome)
            Traverser.RegisterCustom("IsConnectedHome", typeof(bool), () =>
            {
                object cnv = GetActiveVesselRA();
                if (cnv != null && _isConnectedHomeProp != null)
                {
                    try { return (bool)_isConnectedHomeProp.GetValue(cnv, null); } catch { }
                }
                if (cnv is CommNet.CommNetVessel baseCNV)
                {
                    return baseCNV.IsConnectedHome;
                }
                return false;
            }, "RealAntennas 载具通信中枢", "是否已建立连通至地面站的可靠通信链路", new[] { "CONNECTED", "LINKED", "ISCONNECTEDHOME", "ISCONNECTED" });

            // 天线名称 (AntennaName)
            Traverser.RegisterCustom("AntennaName", typeof(string), () =>
            {
                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "Name");
                        if (prop != null) return prop.GetValue(ant, null) as string;
                    }
                    catch { }
                }
                return "NONE";
            }, "RealAntennas 活动天线", "当前起作用的通信天线名称", new[] { "ANTENNANAME", "ANTNAME" });

            // 目标站/对端名称 (TargetName)
            Traverser.RegisterCustom("TargetName", typeof(string), () =>
            {
                object link = GetActiveLink();
                if (link is CommNet.CommLink commLink && commLink.end != null)
                {
                    return commLink.end.displayName ?? commLink.end.name;
                }

                object ant = GetActiveAntenna();
                if (ant != null)
                {
                    try
                    {
                        var prop = GetCachedProperty(ant, "Target");
                        if (prop != null)
                        {
                            object tgt = prop.GetValue(ant, null);
                            if (tgt != null) return tgt.ToString();
                        }
                    }
                    catch { }
                }
                return "NONE";
            }, "RealAntennas 活跃通信链路", "当前通信链路终点站或天线瞄准目标名称", new[] { "TARGET", "TARGETNAME", "STATION" });

            // 总可用天线数量 (AntennaCount)
            Traverser.RegisterCustom("AntennaCount", typeof(int), () =>
            {
                object cnv = GetActiveVesselRA();
                if (cnv != null && _antennaListField != null)
                {
                    try
                    {
                        var list = _antennaListField.GetValue(cnv) as IList;
                        if (list != null) return list.Count;
                    }
                    catch { }
                }
                return 0;
            }, "RealAntennas 载具通信中枢", "载具上已启用的 RealAntenna 天线总数", new[] { "ANTENNACOUNT", "ANTCOUNT" });
        }

        public static double ResolveNumeric(string memberName, string componentModifier = null)
        {
            if (!_isAvailable) return double.NaN;
            return Traverser.ResolveNumeric(memberName, componentModifier);
        }

        public static string ResolveString(string memberName, string format = null, string componentModifier = null)
        {
            if (!_isAvailable) return "---";
            return Traverser.ResolveString(memberName, format, componentModifier);
        }

        public static List<CommLinkInfo> GetActiveCommLinks()
        {
            var result = new List<CommLinkInfo>();
            if (!_isAvailable) return result;

            try
            {
                object node = GetActiveNode();
                if (node is CommNet.CommNode commNode)
                {
                    if (commNode.Count > 0)
                    {
                        foreach (var kvp in commNode)
                        {
                            var peer = kvp.Key;
                            var link = kvp.Value;
                            if (peer == null || link == null) continue;

                            string name = peer.displayName;
                            if (string.IsNullOrEmpty(name)) name = peer.name;

                            double rate = 0.0;
                            try
                            {
                                var prop = GetCachedProperty(link, "FwdDataRate");
                                if (prop != null) rate = Convert.ToDouble(prop.GetValue(link, null));
                            }
                            catch { }

                            float metric = 1.0f;
                            try
                            {
                                var prop = GetCachedProperty(link, "FwdMetric");
                                if (prop != null) metric = Mathf.Clamp01(Convert.ToSingle(prop.GetValue(link, null)));
                            }
                            catch { }

                            bool isHome = peer.isHome;
                            result.Add(new CommLinkInfo(name, rate, metric, isHome));
                        }
                    }

                    if (result.Count == 0 && commNode.Net != null)
                    {
                        var path = new CommNet.CommPath();
                        if (commNode.Net.FindHome(commNode, path))
                        {
                            foreach (var link in path)
                            {
                                if (link == null || link.end == null) continue;
                                string name = link.end.displayName ?? link.end.name;
                                double rate = 0.0;
                                try
                                {
                                    var prop = GetCachedProperty(link, "FwdDataRate");
                                    if (prop != null) rate = Convert.ToDouble(prop.GetValue(link, null));
                                }
                                catch { }
                                float metric = 1.0f;
                                try
                                {
                                    var prop = GetCachedProperty(link, "FwdMetric");
                                    if (prop != null) metric = Mathf.Clamp01(Convert.ToSingle(prop.GetValue(link, null)));
                                }
                                catch { }
                                result.Add(new CommLinkInfo(name, rate, metric, link.end.isHome));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[RealAntennasProbe] GetActiveCommLinks error: {ex.Message}");
            }

            return result;
        }
    }
}
