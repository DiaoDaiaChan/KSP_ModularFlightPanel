using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ModularFlightPanel.Core.Probes
{
    /// <summary>
    /// Principia (princia) 软依赖遥测反射探针
    /// 采用 ProbeReflectionTraverser 彻底遍历 PrincipiaPluginAdapter、ReferenceFrameSelector、
    /// FlightPlanner (计划机动) 以及 OrbitAnalyser (高精度非开普勒摄动轨道分析)，零遗漏。
    /// </summary>
    public static class PrincipiaProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        public static bool IsAvailable => _isAvailable;

        public static ProbeReflectionTraverser Traverser { get; } = new ProbeReflectionTraverser("PRINCIPIA");

        private static Type _adapterType;
        private static FieldInfo _frameSelectorField;
        private static FieldInfo _flightPlannerField;
        private static FieldInfo _orbitAnalyserField;

        private static MethodInfo _frameNameMethod;
        private static MethodInfo _navballNameMethod;
        private static MethodInfo _refPlaneDescMethod;
        private static PropertyInfo _frameTypeProp;
        private static MethodInfo _centreMethod;
        private static MethodInfo _primaryMethod;
        private static MethodInfo _isSurfaceFrameMethod;
        private static PropertyInfo _targetFrameSelectedProp;
        private static MethodInfo _setToSurfaceFrameMethod;
        private static MethodInfo _setToOrbitalFrameMethod;
        private static MethodInfo _setTargetFrameMethod;
        private static MethodInfo _unsetTargetFrameMethod;
        private static MethodInfo _toggleButtonMethod;

        private static PropertyInfo _showGuidanceProp;
        private static MethodInfo _getManoeuvreMethod;
        private static FieldInfo _burnEditorsField;

        private static FieldInfo _orbitDescField;
        private static MethodInfo _getAnalysisMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Assembly principiaAssembly = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (asmName.StartsWith("principia.ksp_plugin_adapter", StringComparison.OrdinalIgnoreCase) ||
                        asmName.Equals("ksp_plugin_adapter", StringComparison.OrdinalIgnoreCase))
                    {
                        principiaAssembly = asm;
                        break;
                    }
                }

                if (principiaAssembly != null)
                {
                    _adapterType = principiaAssembly.GetType("principia.ksp_plugin_adapter.PrincipiaPluginAdapter");
                    if (_adapterType != null)
                    {
                        // 1. 遍历 PrincipiaPluginAdapter 公开与内部组件
                        _frameSelectorField = _adapterType.GetField("plotting_frame_selector_", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _flightPlannerField = _adapterType.GetField("flight_planner_", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _orbitAnalyserField = _adapterType.GetField("orbit_analyser_", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                        // 遍历 Adapter 自身的成员
                        Traverser.TraverseInstance(_adapterType, GetAdapterInstance, "Principia 核心中枢 (Adapter)");

                        // 2. 遍历参考系选择器 (ReferenceFrameSelector)
                        if (_frameSelectorField != null)
                        {
                            Type selectorType = _frameSelectorField.FieldType;
                            Traverser.TraverseInstance(selectorType, GetFrameSelectorInstance, "Principia 权威参考系 (ReferenceFrame)");

                            _frameNameMethod = selectorType.GetMethod("Name", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _navballNameMethod = selectorType.GetMethod("NavballName", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _refPlaneDescMethod = selectorType.GetMethod("ReferencePlaneDescription", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _frameTypeProp = selectorType.GetProperty("frame_type", BindingFlags.Public | BindingFlags.Instance);
                            _centreMethod = selectorType.GetMethod("Centre", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _primaryMethod = selectorType.GetMethod("Primary", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _isSurfaceFrameMethod = selectorType.GetMethod("IsSurfaceFrame", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _targetFrameSelectedProp = selectorType.GetProperty("target_frame_selected", BindingFlags.Public | BindingFlags.Instance);
                            _setToSurfaceFrameMethod = selectorType.GetMethod("SetToSurfaceFrame", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _setToOrbitalFrameMethod = selectorType.GetMethod("SetToOrbitalFrame", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _setTargetFrameMethod = selectorType.GetMethod("SetTargetFrame", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _unsetTargetFrameMethod = selectorType.GetMethod("UnsetTargetFrame", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                            _toggleButtonMethod = selectorType.GetMethod("ToggleButton", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        }

                        // 3. 遍历飞行计划与机动编辑器 (FlightPlanner)
                        if (_flightPlannerField != null)
                        {
                            Type plannerType = _flightPlannerField.FieldType;
                            Traverser.TraverseInstance(plannerType, GetFlightPlannerInstance, "Principia 飞行计划 (FlightPlanner)");

                            _showGuidanceProp = plannerType.GetProperty("show_guidance", BindingFlags.Public | BindingFlags.Instance);
                            _getManoeuvreMethod = plannerType.GetMethod("GetManœuvre", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                            _burnEditorsField = plannerType.GetField("burn_editors_", BindingFlags.NonPublic | BindingFlags.Instance);
                        }

                        // 4. 遍历轨道摄动分析器 (OrbitAnalyser)
                        if (_orbitAnalyserField != null)
                        {
                            Type analyserType = _orbitAnalyserField.FieldType;
                            Traverser.TraverseInstance(analyserType, GetOrbitAnalyserInstance, "Principia 轨道摄动分析 (OrbitAnalyser)");

                            _orbitDescField = analyserType.GetField("orbit_description_", BindingFlags.NonPublic | BindingFlags.Instance);
                            _getAnalysisMethod = analyserType.GetMethod("GetAnalysis", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        }

                        // 5. 注册高阶提取项与动态计算属性
                        RegisterDynamicPrincipiaMembers();

                        _isAvailable = true;
                        SafeLog($"[ModularFlightPanel] PrincipiaProbe successfully hooked Principia! Traversed {Traverser.DiscoveredCount} public API telemetry members.");
                    }
                }
                else
                {
                    _isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"[ModularFlightPanel] Principia probe initialization warning: {ex.Message}");
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

        private static void RegisterDynamicPrincipiaMembers()
        {
            // 参考系名称与类型
            Traverser.RegisterCustom("FrameName", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _frameNameMethod != null)
                {
                    try { return _frameNameMethod.Invoke(sel, null) as string; } catch { }
                }
                return "SURFACE";
            }, "Principia 权威参考系 (ReferenceFrame)", "当前 Principia 绘制参考系全称", new[] { "FRAME", "FRAMENAME" });

            Traverser.RegisterCustom("NavballFrameName", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _navballNameMethod != null)
                {
                    try { return _navballNameMethod.Invoke(sel, null) as string; } catch { }
                }
                return "SURFACE";
            }, "Principia 权威参考系 (ReferenceFrame)", "当前姿态球专用参考系简称", new[] { "NAVBALLNAME", "NAVBALLFRAME" });

            Traverser.RegisterCustom("FrameType", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _frameTypeProp != null)
                {
                    try { return _frameTypeProp.GetValue(sel, null)?.ToString(); } catch { }
                }
                return "BODY_SURFACE";
            }, "Principia 权威参考系 (ReferenceFrame)", "当前参考系拓扑几何类型 (Barycentric/Surface等)", new[] { "FRAMETYPE" });

            Traverser.RegisterCustom("ReferencePlaneDescription", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _refPlaneDescMethod != null)
                {
                    try { return _refPlaneDescMethod.Invoke(sel, null) as string; } catch { }
                }
                return "";
            }, "Principia 权威参考系 (ReferenceFrame)", "参考面物理描述", new[] { "REFPLANEDESC" });

            Traverser.RegisterCustom("CentreBody", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _centreMethod != null)
                {
                    try
                    {
                        var body = _centreMethod.Invoke(sel, null) as CelestialBody;
                        if (body != null) return body.bodyName;
                    }
                    catch { }
                }
                return "";
            }, "Principia 权威参考系 (ReferenceFrame)", "当前参考系中心引力天体", new[] { "CENTRE", "CENTER", "CENTREBODY" });

            Traverser.RegisterCustom("PrimaryBody", typeof(string), () =>
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _primaryMethod != null)
                {
                    try
                    {
                        var body = _primaryMethod.Invoke(sel, null) as CelestialBody;
                        if (body != null) return body.bodyName;
                    }
                    catch { }
                }
                return "";
            }, "Principia 权威参考系 (ReferenceFrame)", "当前参考系主参考天体", new[] { "PRIMARY", "PRIMARYBODY" });

            // 机动计划 (Next Planned Burn)
            Traverser.RegisterCustom("ManeuverDeltaV", typeof(double), () =>
            {
                object man = GetCurrentManoeuvreOrEditor();
                if (man != null)
                {
                    // 尝试通过 Δv() 方法或 Δv 属性取值
                    MethodInfo mi = man.GetType().GetMethod("Δv", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (mi != null) { try { return Convert.ToDouble(mi.Invoke(man, null)); } catch { } }
                    PropertyInfo pi = man.GetType().GetProperty("Δv", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (pi != null) { try { return Convert.ToDouble(pi.GetValue(man, null)); } catch { } }
                    FieldInfo fi = man.GetType().GetField("Δv", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(man)); } catch { } }
                }
                return double.NaN;
            }, "Principia 飞行计划 (FlightPlanner)", "Principia 计划变轨 Delta-V", new[] { "DV", "MANEUVERDV", "DELTAV" });

            Traverser.RegisterCustom("ManeuverDuration", typeof(double), () =>
            {
                object man = GetCurrentManoeuvreOrEditor();
                if (man != null)
                {
                    FieldInfo fi = man.GetType().GetField("duration_", BindingFlags.NonPublic | BindingFlags.Instance)
                                   ?? man.GetType().GetField("duration", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(man)); } catch { } }
                }
                return double.NaN;
            }, "Principia 飞行计划 (FlightPlanner)", "计划变轨点火持续时长 (秒)", new[] { "BURNTIME", "DURATION", "MANEUVERDURATION" });

            Traverser.RegisterCustom("ManeuverInitialTime", typeof(double), () =>
            {
                object man = GetCurrentManoeuvreOrEditor();
                if (man != null)
                {
                    PropertyInfo pi = man.GetType().GetProperty("initial_time", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (pi != null) { try { return Convert.ToDouble(pi.GetValue(man, null)); } catch { } }
                    FieldInfo fi = man.GetType().GetField("initial_time_", BindingFlags.NonPublic | BindingFlags.Instance)
                                   ?? man.GetType().GetField("initial_time", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(man)); } catch { } }
                }
                return double.NaN;
            }, "Principia 飞行计划 (FlightPlanner)", "计划变轨点火起始世界时 (UT)", new[] { "BURNUT", "INITIALTIME" });

            Traverser.RegisterCustom("TimeToManeuver", typeof(double), () =>
            {
                double ut = Traverser.ResolveNumeric("ManeuverInitialTime");
                if (double.IsNaN(ut) || ut <= 0.0) return double.NaN;
                double curUt = Planetarium.GetUniversalTime();
                return Math.Max(0.0, ut - curUt);
            }, "Principia 飞行计划 (FlightPlanner)", "距离下次机动点火倒计时 (秒)", new[] { "TIMETOBURN", "TIMETONODE" });

            // 轨道摄动分析与周期
            Traverser.RegisterCustom("OrbitDescription", typeof(string), () =>
            {
                object oa = GetOrbitAnalyserInstance();
                if (oa != null && _orbitDescField != null)
                {
                    try { return _orbitDescField.GetValue(oa) as string; } catch { }
                }
                return "";
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "高保真轨道物理状态描述 (同步/共振/冻结等)", new[] { "ORBITDESC", "ORBITDESCRIPTION" });

            Traverser.RegisterCustom("NodalPeriod", typeof(double), () =>
            {
                object el = GetAnalysisElements();
                if (el != null)
                {
                    FieldInfo fi = el.GetType().GetField("nodal_period", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(el)); } catch { } }
                }
                return double.NaN;
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "交点公转周期 (秒)", new[] { "NODALPERIOD" });

            Traverser.RegisterCustom("AnomalisticPeriod", typeof(double), () =>
            {
                object el = GetAnalysisElements();
                if (el != null)
                {
                    FieldInfo fi = el.GetType().GetField("anomalistic_period", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(el)); } catch { } }
                }
                return double.NaN;
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "近点公转周期 (秒)", new[] { "ANOMALISTICPERIOD" });

            Traverser.RegisterCustom("SiderealPeriod", typeof(double), () =>
            {
                object el = GetAnalysisElements();
                if (el != null)
                {
                    FieldInfo fi = el.GetType().GetField("sidereal_period", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(el)); } catch { } }
                }
                return double.NaN;
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "恒星公转周期 (秒)", new[] { "SIDEREALPERIOD" });

            Traverser.RegisterCustom("FirstCollisionTime", typeof(double), () =>
            {
                object el = GetAnalysisElements();
                if (el != null)
                {
                    FieldInfo fi = el.GetType().GetField("first_collision_time", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null)
                    {
                        object val = fi.GetValue(el);
                        if (val != null) { try { return Convert.ToDouble(val); } catch { } }
                    }
                }
                return double.NaN;
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "预估撞击地形世界时 (UT)", new[] { "COLLISIONTIME" });

            Traverser.RegisterCustom("FirstReentryTime", typeof(double), () =>
            {
                object el = GetAnalysisElements();
                if (el != null)
                {
                    FieldInfo fi = el.GetType().GetField("first_reentry_time", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null)
                    {
                        object val = fi.GetValue(el);
                        if (val != null) { try { return Convert.ToDouble(val); } catch { } }
                    }
                }
                return double.NaN;
            }, "Principia 轨道摄动分析 (OrbitAnalyser)", "预估再入大气层世界时 (UT)", new[] { "REENTRYTIME" });
        }

        private static object GetAdapterInstance()
        {
            if (_adapterType == null) return null;
            try
            {
                // 1. 优先从场景对象查找
                var obj = UnityEngine.Object.FindObjectOfType(_adapterType);
                if (obj != null) return obj;

                // 2. 备选：从 HighLogic 剧本模块列表查找
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.scenarios != null)
                {
                    for (int i = 0; i < HighLogic.CurrentGame.scenarios.Count; i++)
                    {
                        var sc = HighLogic.CurrentGame.scenarios[i];
                        if (sc != null && sc.moduleRef != null && _adapterType.IsAssignableFrom(sc.moduleRef.GetType()))
                        {
                            return sc.moduleRef;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static object GetFrameSelectorInstance()
        {
            object adapter = GetAdapterInstance();
            if (adapter == null || _frameSelectorField == null) return null;
            try { return _frameSelectorField.GetValue(adapter); }
            catch { return null; }
        }

        private static object GetFlightPlannerInstance()
        {
            object adapter = GetAdapterInstance();
            if (adapter == null || _flightPlannerField == null) return null;
            try { return _flightPlannerField.GetValue(adapter); }
            catch { return null; }
        }

        private static object GetOrbitAnalyserInstance()
        {
            object adapter = GetAdapterInstance();
            if (adapter == null || _orbitAnalyserField == null) return null;
            try { return _orbitAnalyserField.GetValue(adapter); }
            catch { return null; }
        }

        private static object GetCurrentManoeuvreOrEditor()
        {
            object planner = GetFlightPlannerInstance();
            if (planner == null) return null;

            // 1. 尝试从 burn_editors_ 列表读取第一个活动编辑器
            if (_burnEditorsField != null)
            {
                try
                {
                    var list = _burnEditorsField.GetValue(planner) as IList;
                    if (list != null && list.Count > 0 && list[0] != null)
                    {
                        return list[0];
                    }
                }
                catch { }
            }

            // 2. 尝试调用 GetManœuvre(0)
            if (_getManoeuvreMethod != null)
            {
                try
                {
                    return _getManoeuvreMethod.Invoke(planner, new object[] { 0 });
                }
                catch { }
            }

            return null;
        }

        private static object GetAnalysisElements()
        {
            object analyser = GetOrbitAnalyserInstance();
            if (analyser == null || _getAnalysisMethod == null) return null;
            try
            {
                object analysis = _getAnalysisMethod.Invoke(analyser, null);
                if (analysis != null)
                {
                    FieldInfo fi = analysis.GetType().GetField("elements", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) return fi.GetValue(analysis);
                }
            }
            catch { }
            return null;
        }

        public static double ResolveNumeric(string subTag, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveNumeric(subTag, modifier) : double.NaN;
        }

        public static string ResolveString(string subTag, string format = null, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveString(subTag, format, modifier) : "---";
        }

        // 强类型便捷属性与参考系控制
        public static string FrameName => ResolveString("FrameName");
        public static string NavballFrameName => ResolveString("NavballFrameName");
        public static double ManeuverDeltaV => ResolveNumeric("ManeuverDeltaV");
        public static double ManeuverDuration => ResolveNumeric("ManeuverDuration");
        public static double TimeToManeuver => ResolveNumeric("TimeToManeuver");
        public static string OrbitDescription => ResolveString("OrbitDescription");

        public static bool IsTargetFrameSelected
        {
            get
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _targetFrameSelectedProp != null)
                {
                    try { return (bool)_targetFrameSelectedProp.GetValue(sel, null); } catch { }
                }
                return false;
            }
        }

        public static string FrameTypeString
        {
            get
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _frameTypeProp != null)
                {
                    try { return _frameTypeProp.GetValue(sel, null)?.ToString(); } catch { }
                }
                return null;
            }
        }

        public static bool IsSurfaceFrameSelected
        {
            get
            {
                object sel = GetFrameSelectorInstance();
                if (sel != null && _isSurfaceFrameMethod != null)
                {
                    try { return (bool)_isSurfaceFrameMethod.Invoke(sel, null); } catch { }
                }
                return false;
            }
        }

        public static void SetToSurfaceFrame()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _setToSurfaceFrameMethod != null)
            {
                try { _setToSurfaceFrameMethod.Invoke(sel, null); } catch { }
            }
        }

        public static void SetToOrbitalFrame()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _setToOrbitalFrameMethod != null)
            {
                try { _setToOrbitalFrameMethod.Invoke(sel, null); } catch { }
            }
        }

        public static void SetTargetFrame()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _setTargetFrameMethod != null)
            {
                try { _setTargetFrameMethod.Invoke(sel, null); } catch { }
            }
        }

        public static void UnsetTargetFrame()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _unsetTargetFrameMethod != null)
            {
                try { _unsetTargetFrameMethod.Invoke(sel, null); } catch { }
            }
        }

        public static void ToggleReferenceFrameWindow()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _toggleButtonMethod != null)
            {
                try { _toggleButtonMethod.Invoke(sel, null); } catch { }
            }
        }

        public static void CycleReferenceFrame()
        {
            if (!_isAvailable) return;

            try
            {
                bool isTarget = IsTargetFrameSelected;
                bool isSurface = IsSurfaceFrameSelected;

                if (isTarget)
                {
                    // Target -> Surface
                    UnsetTargetFrame();
                    SetToSurfaceFrame();
                }
                else if (isSurface)
                {
                    // Surface -> Orbital
                    SetToOrbitalFrame();
                }
                else
                {
                    // Orbital -> Target (若已选定目标) 或 Surface
                    bool hasTarget = FlightGlobals.fetch != null && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.targetObject != null;
                    if (hasTarget && _setTargetFrameMethod != null)
                    {
                        SetTargetFrame();
                    }
                    else
                    {
                        SetToSurfaceFrame();
                    }
                }

                // 同步驱动 KSP 原生速度显示模式，确保底层物理与原生 UI 绝对对齐
                if (FlightGlobals.fetch != null)
                {
                    FlightGlobals.CycleSpeedModes();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModularFlightPanel] CycleReferenceFrame warning: {ex.Message}");
            }
        }
    }
}
