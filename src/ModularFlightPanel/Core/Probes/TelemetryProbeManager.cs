using System;
using System.Collections.Generic;
using UnityEngine;
using ModularFlightPanel.UI.Settings;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// 外部模组遥测数据探针中枢管理器
    /// 负责在场景加载和模组启动时探测并安全挂载外部 15 大核心 Mod：
    /// FAR / KER / MechJeb / Principia / RealAntennas / Kerbalism /
    /// Trajectories / DPAI / GPWS / RealFuels / TestFlight / DynamicBatteryStorage / SystemHeat / AtmosphereAutopilot / RP-1 Avionics
    /// 通过 ProbeReflectionTraverser 完全遍历所有公开 API 内容，并动态注册至 ExternalProbeRegistry 与 TelemetryCatalog。
    /// </summary>
    public static class TelemetryProbeManager
    {
        private static bool _initialized = false;

        public static void InitializeAll()
        {
            if (_initialized) return;
            _initialized = true;

            // 原有 6 大探针
            SafeInit("FarProbe", FarProbe.Initialize);
            SafeInit("KerbalEngineerProbe", KerbalEngineerProbe.Initialize);
            SafeInit("MechJebProbe", MechJebProbe.Initialize);
            SafeInit("PrincipiaProbe", PrincipiaProbe.Initialize);
            SafeInit("RealAntennasProbe", RealAntennasProbe.Initialize);
            SafeInit("KerbalismProbe", KerbalismProbe.Initialize);

            // Phase 1: 航迹与近地导航探针
            SafeInit("TrajectoriesProbe", TrajectoriesProbe.Initialize);
            SafeInit("DockingAlignmentProbe", DockingAlignmentProbe.Initialize);
            SafeInit("GPWSProbe", GPWSProbe.Initialize);

            // Phase 2: 真实动力与可靠性探针
            SafeInit("RealFuelsProbe", RealFuelsProbe.Initialize);
            SafeInit("TestFlightProbe", TestFlightProbe.Initialize);

            // Phase 3: 能源与热力循环探针
            SafeInit("DynamicBatteryStorageProbe", DynamicBatteryStorageProbe.Initialize);
            SafeInit("SystemHeatProbe", SystemHeatProbe.Initialize);

            // Phase 4: 飞控与航电控制探针
            SafeInit("AtmosphereAutopilotProbe", AtmosphereAutopilotProbe.Initialize);
            SafeInit("RP1AvionicsProbe", RP1AvionicsProbe.Initialize);

            // 挂载至解耦注册表
            ExternalProbeRegistry.NumericResolver = ResolveNumericProbe;
            ExternalProbeRegistry.StringResolver = ResolveStringProbe;

            // 刷新全量探针可用性位图缓存 (Zero-Allocation Fast Path)
            RefreshAvailability();

            // 将所有探针遍历得出的所有遥测成员动态注入 TelemetryCatalog 词典
            SyncTraversedMembersToCatalog();
        }

        private static bool _anyExternalProbeAvailable = false;
        private static readonly Dictionary<string, bool> _probeAvailabilityByTag = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static void RefreshAvailability()
        {
            _probeAvailabilityByTag.Clear();
            void Reg(bool avail, params string[] tags)
            {
                for (int i = 0; i < tags.Length; i++)
                    _probeAvailabilityByTag[tags[i]] = avail;
            }

            Reg(FarProbe.IsAvailable, "FAR", "FARC");
            Reg(KerbalEngineerProbe.IsAvailable, "KER", "ENGINEER");
            Reg(MechJebProbe.IsAvailable, "MJ", "MECHJEB");
            Reg(PrincipiaProbe.IsAvailable, "PRINCIPIA", "PRINCIA", "PRIN");
            Reg(RealAntennasProbe.IsAvailable, "RA", "REALANTENNAS", "REALANTENNA");
            Reg(KerbalismProbe.IsAvailable, "KERBALISM", "KLSM");
            Reg(TrajectoriesProbe.IsAvailable, "TRAJ", "TRAJECTORIES");
            Reg(DockingAlignmentProbe.IsAvailable, "DOCK", "DPAI", "NAVYFISH");
            Reg(GPWSProbe.IsAvailable, "GPWS", "TAWS");
            Reg(RealFuelsProbe.IsAvailable, "RF", "REALFUELS", "REALFUEL");
            Reg(TestFlightProbe.IsAvailable, "TF", "TESTFLIGHT");
            Reg(DynamicBatteryStorageProbe.IsAvailable, "DBS", "DYNAMICBATTERYSTORAGE");
            Reg(SystemHeatProbe.IsAvailable, "SH", "SYSTEMHEAT");
            Reg(AtmosphereAutopilotProbe.IsAvailable, "AA", "ATMOSPHEREAUTOPILOT");
            Reg(RP1AvionicsProbe.IsAvailable, "RP1", "RP0", "AVIONICS");

            _anyExternalProbeAvailable = false;
            foreach (var kvp in _probeAvailabilityByTag)
            {
                if (kvp.Value)
                {
                    _anyExternalProbeAvailable = true;
                    break;
                }
            }
        }

        public static bool IsProbeTagAvailable(string tag)
        {
            if (!_anyExternalProbeAvailable || string.IsNullOrEmpty(tag)) return false;
            return _probeAvailabilityByTag.TryGetValue(tag, out bool avail) && avail;
        }

        private static void SafeInit(string probeName, Action initAction)
        {
            try
            {
                initAction();
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] {probeName} init error: {ex.Message}");
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

        private static void SyncTraversedMembersToCatalog()
        {
            try
            {
                var dynamicList = new List<TelemetryParam>();

                // 注入全部 15 大探针的动态成员
                AppendTraverserParams(FarProbe.Traverser, dynamicList);
                AppendTraverserParams(KerbalEngineerProbe.Traverser, dynamicList);
                AppendTraverserParams(MechJebProbe.Traverser, dynamicList);
                AppendTraverserParams(PrincipiaProbe.Traverser, dynamicList);
                AppendTraverserParams(RealAntennasProbe.Traverser, dynamicList);
                AppendTraverserParams(KerbalismProbe.Traverser, dynamicList);

                AppendTraverserParams(TrajectoriesProbe.Traverser, dynamicList);
                AppendTraverserParams(DockingAlignmentProbe.Traverser, dynamicList);
                AppendTraverserParams(GPWSProbe.Traverser, dynamicList);

                AppendTraverserParams(RealFuelsProbe.Traverser, dynamicList);
                AppendTraverserParams(TestFlightProbe.Traverser, dynamicList);

                AppendTraverserParams(DynamicBatteryStorageProbe.Traverser, dynamicList);
                AppendTraverserParams(SystemHeatProbe.Traverser, dynamicList);

                AppendTraverserParams(AtmosphereAutopilotProbe.Traverser, dynamicList);
                AppendTraverserParams(RP1AvionicsProbe.Traverser, dynamicList);

                TelemetryCatalog.RegisterDynamicParams(dynamicList);
                SafeLog($"[ModularFlightPanel] TelemetryProbeManager synchronized {dynamicList.Count} traversed API members into UI TelemetryCatalog across 15 external mod probes.");
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] Catalog synchronization warning: {ex.Message}");
            }
        }

        private static void AppendTraverserParams(ProbeReflectionTraverser traverser, List<TelemetryParam> list)
        {
            if (traverser == null) return;
            var all = traverser.AllMembers;
            for (int i = 0; i < all.Count; i++)
            {
                var m = all[i];
                string token = $"{{{m.ModTag}:{m.MemberName}}}";
                string category;
                switch (m.ModTag.ToUpperInvariant())
                {
                    case "FAR":
                    case "KER":
                    case "MJ":
                    case "PRIN":
                    case "PRINCIPIA":
                        category = "📡 外部探针 (FAR/KER/MJ/PRIN)";
                        break;
                    case "RA":
                    case "KERBALISM":
                        category = "🛰️ 通信与维生 (RA/Kerbalism)";
                        break;
                    case "TRAJ":
                    case "DOCK":
                    case "GPWS":
                        category = "🛬 航迹与进近 (TRAJ/DOCK/GPWS)";
                        break;
                    case "RF":
                    case "TF":
                        category = "🔥 真实动力与可靠性 (RF/TF)";
                        break;
                    case "DBS":
                    case "SH":
                        category = "⚡ 能量与热力 (DBS/SH)";
                        break;
                    case "AA":
                    case "RP1":
                        category = "✈️ 飞控与航电 (AA/RP1)";
                        break;
                    default:
                        category = $"📡 外部探针 ({m.ModTag})";
                        break;
                }

                list.Add(new TelemetryParam(
                    token,
                    category,
                    $"{m.ModTag} {m.MemberName}",
                    m.Description ?? $"{m.ModTag} {m.MemberName}",
                    "",
                    0, 1000, 750, 900, true
                ));
            }
        }

        private static double ResolveNumericProbe(string tag, string subTag)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(subTag)) return double.NaN;
            if (!IsProbeTagAvailable(tag)) return double.NaN;

            string probeKey = tag + ":" + subTag;
            int frame = Time.frameCount;
            if (CacheManager.Instance.TryGetCachedProbeNumeric(probeKey, frame, out double cachedVal))
            {
                return cachedVal;
            }

            // 支持三段式修饰符（如 {FAR:AEROFORCE:X}, {FAR:AEROFORCE:MAG}）
            string name = subTag;
            string modifier = null;
            int colonIdx = subTag.IndexOf(':');
            if (colonIdx > 0)
            {
                name = subTag.Substring(0, colonIdx);
                modifier = subTag.Substring(colonIdx + 1);
            }

            double result = ResolveNumericProbeInternal(tag, name, modifier);
            CacheManager.Instance.SetCachedProbeNumeric(probeKey, frame, result);
            return result;
        }

        private static double ResolveNumericProbeInternal(string tag, string name, string modifier)
        {
            switch (tag.ToUpperInvariant())
            {
                // 原有 6 大探针
                case "FAR":
                case "FARC":
                    if (!FarProbe.IsAvailable) return double.NaN;
                    return FarProbe.ResolveNumeric(name, modifier);

                case "KER":
                case "ENGINEER":
                    if (!KerbalEngineerProbe.IsAvailable) return double.NaN;
                    return KerbalEngineerProbe.ResolveNumeric(name, modifier);

                case "MJ":
                case "MECHJEB":
                    if (!MechJebProbe.IsAvailable) return double.NaN;
                    return MechJebProbe.ResolveNumeric(name, modifier);

                case "PRINCIPIA":
                case "PRINCIA":
                case "PRIN":
                    if (!PrincipiaProbe.IsAvailable) return double.NaN;
                    return PrincipiaProbe.ResolveNumeric(name, modifier);

                case "RA":
                case "REALANTENNAS":
                case "REALANTENNA":
                    if (!RealAntennasProbe.IsAvailable) return double.NaN;
                    return RealAntennasProbe.ResolveNumeric(name, modifier);

                case "KERBALISM":
                case "KLSM":
                    if (!KerbalismProbe.IsAvailable) return double.NaN;
                    return KerbalismProbe.ResolveNumeric(name, modifier);

                // Phase 1
                case "TRAJ":
                case "TRAJECTORIES":
                    if (!TrajectoriesProbe.IsAvailable) return double.NaN;
                    return TrajectoriesProbe.Traverser.TryResolveNumeric(name, out double trajVal) ? trajVal : double.NaN;

                case "DOCK":
                case "DPAI":
                case "NAVYFISH":
                    if (!DockingAlignmentProbe.IsAvailable) return double.NaN;
                    return DockingAlignmentProbe.Traverser.TryResolveNumeric(name, out double dockVal) ? dockVal : double.NaN;

                case "GPWS":
                case "TAWS":
                    if (!GPWSProbe.IsAvailable) return double.NaN;
                    return GPWSProbe.Traverser.TryResolveNumeric(name, out double gpwsVal) ? gpwsVal : double.NaN;

                // Phase 2
                case "RF":
                case "REALFUELS":
                case "REALFUEL":
                    if (!RealFuelsProbe.IsAvailable) return double.NaN;
                    return RealFuelsProbe.Traverser.TryResolveNumeric(name, out double rfVal) ? rfVal : double.NaN;

                case "TF":
                case "TESTFLIGHT":
                    if (!TestFlightProbe.IsAvailable) return double.NaN;
                    return TestFlightProbe.Traverser.TryResolveNumeric(name, out double tfVal) ? tfVal : double.NaN;

                // Phase 3
                case "DBS":
                case "DYNAMICBATTERYSTORAGE":
                    if (!DynamicBatteryStorageProbe.IsAvailable) return double.NaN;
                    return DynamicBatteryStorageProbe.Traverser.TryResolveNumeric(name, out double dbsVal) ? dbsVal : double.NaN;

                case "SH":
                case "SYSTEMHEAT":
                    if (!SystemHeatProbe.IsAvailable) return double.NaN;
                    return SystemHeatProbe.Traverser.TryResolveNumeric(name, out double shVal) ? shVal : double.NaN;

                // Phase 4
                case "AA":
                case "ATMOSPHEREAUTOPILOT":
                    if (!AtmosphereAutopilotProbe.IsAvailable) return double.NaN;
                    return AtmosphereAutopilotProbe.Traverser.TryResolveNumeric(name, out double aaVal) ? aaVal : double.NaN;

                case "RP1":
                case "RP0":
                case "AVIONICS":
                    if (!RP1AvionicsProbe.IsAvailable) return double.NaN;
                    return RP1AvionicsProbe.Traverser.TryResolveNumeric(name, out double rp1Val) ? rp1Val : double.NaN;

                default:
                    return double.NaN;
            }
        }

        private static string ResolveStringProbe(string tag, string subTag, string format)
        {
            if (string.IsNullOrEmpty(tag)) return "---";
            if (string.IsNullOrEmpty(subTag)) return tag;
            if (!IsProbeTagAvailable(tag)) return "---";

            string probeKey = tag + ":" + subTag + ":" + (format ?? string.Empty);
            int frame = Time.frameCount;
            if (CacheManager.Instance.TryGetCachedProbeString(probeKey, frame, out string cachedStr))
            {
                return cachedStr;
            }

            string name = subTag;
            string modifier = null;
            int colonIdx = subTag.IndexOf(':');
            if (colonIdx > 0)
            {
                name = subTag.Substring(0, colonIdx);
                modifier = subTag.Substring(colonIdx + 1);
            }

            string result = ResolveStringProbeInternal(tag, name, modifier, format);
            CacheManager.Instance.SetCachedProbeString(probeKey, frame, result);
            return result;
        }

        private static string ResolveStringProbeInternal(string tag, string name, string modifier, string format)
        {
            switch (tag.ToUpperInvariant())
            {
                // 原有 6 大探针
                case "FAR":
                case "FARC":
                    if (!FarProbe.IsAvailable) return "---";
                    return FarProbe.ResolveString(name, format, modifier);

                case "KER":
                case "ENGINEER":
                    if (!KerbalEngineerProbe.IsAvailable) return "---";
                    return KerbalEngineerProbe.ResolveString(name, format, modifier);

                case "MJ":
                case "MECHJEB":
                    if (!MechJebProbe.IsAvailable) return "---";
                    return MechJebProbe.ResolveString(name, format, modifier);

                case "PRINCIPIA":
                case "PRINCIA":
                case "PRIN":
                    if (!PrincipiaProbe.IsAvailable) return "---";
                    return PrincipiaProbe.ResolveString(name, format, modifier);

                case "RA":
                case "REALANTENNAS":
                case "REALANTENNA":
                    if (!RealAntennasProbe.IsAvailable) return "---";
                    return RealAntennasProbe.ResolveString(name, format, modifier);

                case "KERBALISM":
                case "KLSM":
                    if (!KerbalismProbe.IsAvailable) return "---";
                    return KerbalismProbe.ResolveString(name, format, modifier);

                // Phase 1
                case "TRAJ":
                case "TRAJECTORIES":
                    if (!TrajectoriesProbe.IsAvailable) return "---";
                    return ResolveTraverserString(TrajectoriesProbe.Traverser, name, format);

                case "DOCK":
                case "DPAI":
                case "NAVYFISH":
                    if (!DockingAlignmentProbe.IsAvailable) return "---";
                    return ResolveTraverserString(DockingAlignmentProbe.Traverser, name, format);

                case "GPWS":
                case "TAWS":
                    if (!GPWSProbe.IsAvailable) return "---";
                    return ResolveTraverserString(GPWSProbe.Traverser, name, format);

                // Phase 2
                case "RF":
                case "REALFUELS":
                case "REALFUEL":
                    if (!RealFuelsProbe.IsAvailable) return "---";
                    return ResolveTraverserString(RealFuelsProbe.Traverser, name, format);

                case "TF":
                case "TESTFLIGHT":
                    if (!TestFlightProbe.IsAvailable) return "---";
                    return ResolveTraverserString(TestFlightProbe.Traverser, name, format);

                // Phase 3
                case "DBS":
                case "DYNAMICBATTERYSTORAGE":
                    if (!DynamicBatteryStorageProbe.IsAvailable) return "---";
                    return ResolveTraverserString(DynamicBatteryStorageProbe.Traverser, name, format);

                case "SH":
                case "SYSTEMHEAT":
                    if (!SystemHeatProbe.IsAvailable) return "---";
                    return ResolveTraverserString(SystemHeatProbe.Traverser, name, format);

                // Phase 4
                case "AA":
                case "ATMOSPHEREAUTOPILOT":
                    if (!AtmosphereAutopilotProbe.IsAvailable) return "---";
                    return ResolveTraverserString(AtmosphereAutopilotProbe.Traverser, name, format);

                case "RP1":
                case "RP0":
                case "AVIONICS":
                    if (!RP1AvionicsProbe.IsAvailable) return "---";
                    return ResolveTraverserString(RP1AvionicsProbe.Traverser, name, format);

                default:
                    return $"{{{tag}}}";
            }
        }

        private static string ResolveTraverserString(ProbeReflectionTraverser traverser, string name, string format)
        {
            if (traverser == null) return "---";
            if (traverser.TryResolveString(name, out string strVal))
                return strVal;

            if (traverser.TryResolveNumeric(name, out double numVal))
            {
                if (double.IsNaN(numVal)) return "---";
                string fmt = string.IsNullOrEmpty(format) ? "F1" : format;
                return CacheManager.Instance.FastDouble(name, numVal, fmt);
            }

            return "---";
        }
    }
}
