using System;
using System.Collections;
using System.Globalization;
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
        private static Type _interfaceType;
        private static MethodInfo _hasVesselMethod;
        private static MethodInfo _vesselVelocityMethod;
        private static MethodInfo _unmanageableVesselVelocityMethod;
        private static MethodInfo _vesselTangentMethod;
        private static MethodInfo _vesselNormalMethod;
        private static MethodInfo _vesselBinormalMethod;
        private static MethodInfo _flightPlanExistsMethod;
        private static MethodInfo _flightPlanNumManoeuvresMethod;
        private static MethodInfo _flightPlanGetManoeuvreMethod;

        private static Type _qpType;
        private static Type _xyzType;
        private static FieldInfo _qpQField;
        private static FieldInfo _qpPField;
        private static FieldInfo _xyzXField;
        private static FieldInfo _xyzYField;
        private static FieldInfo _xyzZField;

        private static FieldInfo _pluginField;
        private static MethodInfo _pluginMethod;
        private static MethodInfo _hasActiveManageableVesselMethod;
        private static PropertyInfo _plannerPluginProp;
        private static PropertyInfo _plannerPredictedVesselProp;
        private static PropertyInfo _analyserPredictedVesselProp;

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
        private static MethodInfo _toggleMethod;

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
                    _interfaceType = principiaAssembly.GetType("principia.ksp_plugin_adapter.Interface");
                    if (_interfaceType != null)
                    {
                        _hasVesselMethod = _interfaceType.GetMethod("HasVessel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null);
                        _vesselVelocityMethod = _interfaceType.GetMethod("VesselVelocity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null)
                                                ?? _interfaceType.GetMethod("VesselVelocity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _unmanageableVesselVelocityMethod = _interfaceType.GetMethod("UnmanageableVesselVelocity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _vesselTangentMethod = _interfaceType.GetMethod("VesselTangent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null)
                                               ?? _interfaceType.GetMethod("VesselTangent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _vesselNormalMethod = _interfaceType.GetMethod("VesselNormal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null)
                                              ?? _interfaceType.GetMethod("VesselNormal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _vesselBinormalMethod = _interfaceType.GetMethod("VesselBinormal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null)
                                                ?? _interfaceType.GetMethod("VesselBinormal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        _flightPlanExistsMethod = _interfaceType.GetMethod("FlightPlanExists", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null);
                        _flightPlanNumManoeuvresMethod = _interfaceType.GetMethod("FlightPlanNumberOfManoeuvres", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string) }, null);
                        _flightPlanGetManoeuvreMethod = _interfaceType.GetMethod("FlightPlanGetManoeuvre", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(string), typeof(int) }, null);
                    }

                    _qpType = principiaAssembly.GetType("principia.ksp_plugin_adapter.QP");
                    _xyzType = principiaAssembly.GetType("principia.ksp_plugin_adapter.XYZ");
                    if (_qpType != null)
                    {
                        _qpQField = _qpType.GetField("q", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        _qpPField = _qpType.GetField("p", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    }
                    if (_xyzType != null)
                    {
                        _xyzXField = _xyzType.GetField("x", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        _xyzYField = _xyzType.GetField("y", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        _xyzZField = _xyzType.GetField("z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    }

                    _adapterType = principiaAssembly.GetType("principia.ksp_plugin_adapter.PrincipiaPluginAdapter");
                    if (_adapterType != null)
                    {
                        _pluginField = _adapterType.GetField("plugin_", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _pluginMethod = _adapterType.GetMethod("Plugin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                        _hasActiveManageableVesselMethod = _adapterType.GetMethod("has_active_manageable_vessel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

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
                            _toggleMethod = selectorType.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                                            ?? selectorType.GetMethod("ToggleButton", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        }

                        // 3. 遍历飞行计划与机动编辑器 (FlightPlanner)
                        if (_flightPlannerField != null)
                        {
                            Type plannerType = _flightPlannerField.FieldType;
                            Traverser.TraverseInstance(plannerType, GetFlightPlannerInstance, "Principia 飞行计划 (FlightPlanner)");

                            _plannerPluginProp = plannerType.GetProperty("plugin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            _plannerPredictedVesselProp = plannerType.GetProperty("predicted_vessel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            _showGuidanceProp = plannerType.GetProperty("show_guidance", BindingFlags.Public | BindingFlags.Instance);
                            _getManoeuvreMethod = plannerType.GetMethod("GetManœuvre", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                            _burnEditorsField = plannerType.GetField("burn_editors_", BindingFlags.NonPublic | BindingFlags.Instance);
                        }

                        // 4. 遍历轨道摄动分析器 (OrbitAnalyser)
                        if (_orbitAnalyserField != null)
                        {
                            Type analyserType = _orbitAnalyserField.FieldType;
                            Traverser.TraverseInstance(analyserType, GetOrbitAnalyserInstance, "Principia 轨道摄动分析 (OrbitAnalyser)");

                            _analyserPredictedVesselProp = analyserType.GetProperty("predicted_vessel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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

            Traverser.RegisterCustom("PlottingFrameSpeed", typeof(double), () =>
            {
                if (GetActiveVesselSpeed(out double s)) return s;
                return double.NaN;
            }, "Principia 权威参考系 (ReferenceFrame)", "当前绘制参考系下载具速率 (m/s)", new[] { "FRAME_SPD", "FRAMESPEED", "PRINCIPIA_SPD" });

            Traverser.RegisterCustom("PlottingFrameVelocity", typeof(Vector3d), () =>
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null && GetVesselVelocity(v.id.ToString(), out Vector3d vel)) return vel;
                return Vector3d.zero;
            }, "Principia 权威参考系 (ReferenceFrame)", "当前绘制参考系下载具速度矢量", new[] { "FRAME_VEL", "FRAMEVELOCITY" });

            // 机动计划 (Next Planned Burn)
            Traverser.RegisterCustom("ManeuverDeltaV", typeof(double), () =>
            {
                object man = GetCurrentManoeuvreOrEditor();
                if (man != null)
                {
                    // 1. 尝试通过 Δv() 方法或 Δv 属性取值 (BurnEditor)
                    MethodInfo mi = man.GetType().GetMethod("Δv", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (mi != null) { try { return Convert.ToDouble(mi.Invoke(man, null)); } catch { } }
                    PropertyInfo pi = man.GetType().GetProperty("Δv", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (pi != null) { try { return Convert.ToDouble(pi.GetValue(man, null)); } catch { } }
                    FieldInfo fi = man.GetType().GetField("Δv", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { try { return Convert.ToDouble(fi.GetValue(man)); } catch { } }

                    // 2. 尝试从 NavigationManoeuvre.burn.delta_v 读取
                    FieldInfo burnFi = man.GetType().GetField("burn", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (burnFi != null)
                    {
                        object burn = burnFi.GetValue(man);
                        if (burn != null)
                        {
                            FieldInfo dvFi = burn.GetType().GetField("delta_v", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            if (dvFi != null)
                            {
                                object xyz = dvFi.GetValue(burn);
                                if (xyz != null)
                                {
                                    FieldInfo xf = xyz.GetType().GetField("x", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                    FieldInfo yf = xyz.GetType().GetField("y", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                    FieldInfo zf = xyz.GetType().GetField("z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                    if (xf != null && yf != null && zf != null)
                                    {
                                        double x = Convert.ToDouble(xf.GetValue(xyz));
                                        double y = Convert.ToDouble(yf.GetValue(xyz));
                                        double z = Convert.ToDouble(zf.GetValue(xyz));
                                        return Math.Sqrt(x * x + y * y + z * z);
                                    }
                                }
                            }
                        }
                    }
                }
                return double.NaN;
            }, "Principia 飞行计划 (FlightPlanner)", "Principia 计划变轨 Delta-V", new[] { "DV", "MANEUVERDV", "DELTAV" });

            Traverser.RegisterCustom("ManeuverDeltaVPrograde", typeof(double), () => ManeuverDeltaVPrograde, "Principia 飞行计划 (FlightPlanner)", "计划变轨切向/前向 Delta-V", new[] { "PROGRADE", "TANGENT", "DV_PRO" });
            Traverser.RegisterCustom("ManeuverDeltaVNormal", typeof(double), () => ManeuverDeltaVNormal, "Principia 飞行计划 (FlightPlanner)", "计划变轨法向/平面外 Delta-V", new[] { "NORMAL", "BINORMAL", "DV_NORM" });
            Traverser.RegisterCustom("ManeuverDeltaVRadial", typeof(double), () => ManeuverDeltaVRadial, "Principia 飞行计划 (FlightPlanner)", "计划变轨径向 Delta-V", new[] { "RADIAL", "DV_RAD" });

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

                    FieldInfo burnFi = man.GetType().GetField("burn", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (burnFi != null)
                    {
                        object burn = burnFi.GetValue(man);
                        if (burn != null)
                        {
                            FieldInfo initFi = burn.GetType().GetField("initial_time", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            if (initFi != null) { try { return Convert.ToDouble(initFi.GetValue(burn)); } catch { } }
                        }
                    }
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

            // 开普勒高精度摄动要素提取 (优先供航电六根数面板联动)
            Traverser.RegisterCustom("SMA", typeof(double), () => ExtractDoubleFromElements("semi_major_axis", "semimajor_axis", "a"), "Principia 轨道要素", "轨道半长轴 (米)", new[] { "SEMIMAJORAXIS", "A" });
            Traverser.RegisterCustom("ECC", typeof(double), () => ExtractDoubleFromElements("eccentricity", "e"), "Principia 轨道要素", "轨道离心率", new[] { "ECCENTRICITY", "E" });
            Traverser.RegisterCustom("INC", typeof(double), () => ExtractDoubleFromElements("inclination", "i"), "Principia 轨道要素", "轨道倾角 (度)", new[] { "INCLINATION", "I" });
            Traverser.RegisterCustom("LAN", typeof(double), () => ExtractDoubleFromElements("longitude_of_ascending_node", "lan", "raan", "Ω"), "Principia 轨道要素", "升交点赤经 (度)", new[] { "LONGITUDEOFASCENDINGNODE", "RAAN" });
            Traverser.RegisterCustom("LPE", typeof(double), () => ExtractDoubleFromElements("argument_of_periapsis", "argument_of_perigee", "aop", "lpe", "ω"), "Principia 轨道要素", "近拱点辐角 (度)", new[] { "ARGUMENTOFPERIAPSIS", "AOP" });
            Traverser.RegisterCustom("TRA", typeof(double), () => ExtractDoubleFromElements("true_anomaly", "ta", "tra", "ν"), "Principia 轨道要素", "真近点角 (度)", new[] { "TRUEANOMALY", "TA" });
            Traverser.RegisterCustom("APA", typeof(double), () => ExtractDoubleFromElements("apoapsis_distance", "apoapsis_altitude", "apoapsis", "apa"), "Principia 轨道要素", "远拱点高度 (米)", new[] { "APOAPSIS", "AP" });
            Traverser.RegisterCustom("PEA", typeof(double), () => ExtractDoubleFromElements("periapsis_distance", "periapsis_altitude", "periapsis", "pea"), "Principia 轨道要素", "近拱点高度 (米)", new[] { "PERIAPSIS", "PE" });
        }

        private static object _cachedAdapterInstance;

        private static object GetAdapterInstance()
        {
            if (_adapterType == null) return null;
            if (_cachedAdapterInstance != null && ((UnityEngine.Object)_cachedAdapterInstance) != null)
            {
                return _cachedAdapterInstance;
            }

            try
            {
                // 1. 黄金路径：PrincipiaPluginAdapter 恒定挂载在 ScenarioRunner 单例上，O(1) 获取零开销
                if (ScenarioRunner.Instance != null)
                {
                    var comp = ScenarioRunner.Instance.GetComponent(_adapterType);
                    if (comp != null)
                    {
                        _cachedAdapterInstance = comp;
                        return _cachedAdapterInstance;
                    }
                }

                // 2. 从场景对象查找
                var obj = UnityEngine.Object.FindObjectOfType(_adapterType);
                if (obj != null)
                {
                    _cachedAdapterInstance = obj;
                    return _cachedAdapterInstance;
                }

                // 3. 备选：从 HighLogic 剧本模块列表查找
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.scenarios != null)
                {
                    for (int i = 0; i < HighLogic.CurrentGame.scenarios.Count; i++)
                    {
                        var sc = HighLogic.CurrentGame.scenarios[i];
                        if (sc != null && sc.moduleRef != null && _adapterType.IsAssignableFrom(sc.moduleRef.GetType()))
                        {
                            _cachedAdapterInstance = sc.moduleRef;
                            return _cachedAdapterInstance;
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

        private static IntPtr _cachedPluginPointer = IntPtr.Zero;
        private static int _cachedPluginPointerFrame = -1;

        private static IntPtr GetPluginPointer()
        {
            int currentFrame = Time.frameCount;
            if (_cachedPluginPointerFrame == currentFrame && _cachedPluginPointer != IntPtr.Zero)
            {
                return _cachedPluginPointer;
            }
            _cachedPluginPointerFrame = currentFrame;

            // 1. 从 flight_planner_ 的 plugin 属性获取
            object planner = GetFlightPlannerInstance();
            if (planner != null && _plannerPluginProp != null)
            {
                try
                {
                    IntPtr ptr = (IntPtr)_plannerPluginProp.GetValue(planner, null);
                    if (ptr != IntPtr.Zero)
                    {
                        _cachedPluginPointer = ptr;
                        return ptr;
                    }
                }
                catch { }
            }

            // 2. 从 adapter 的 plugin_ 字段或 Plugin() 方法获取
            object adapter = GetAdapterInstance();
            if (adapter != null)
            {
                if (_pluginField != null)
                {
                    try
                    {
                        IntPtr ptr = (IntPtr)_pluginField.GetValue(adapter);
                        if (ptr != IntPtr.Zero)
                        {
                            _cachedPluginPointer = ptr;
                            return ptr;
                        }
                    }
                    catch { }
                }
                if (_pluginMethod != null)
                {
                    try
                    {
                        IntPtr ptr = (IntPtr)_pluginMethod.Invoke(adapter, null);
                        if (ptr != IntPtr.Zero)
                        {
                            _cachedPluginPointer = ptr;
                            return ptr;
                        }
                    }
                    catch { }
                }
            }

            _cachedPluginPointer = IntPtr.Zero;
            return IntPtr.Zero;
        }

        private static int _lastFlightPlanFrame = -1;
        private static bool _cachedFlightPlanResult = false;

        /// <summary>
        /// 权威检测当前活动载具在 Principia 中是否存在有效的飞行计划与机动节点。
        /// 严格执行三重守卫：
        /// 1. burn_editors_ 列表探测 (最安全低开销)
        /// 2. Interface.HasVessel 验证 (防止未跟踪载具调用引发 native CHECK 失败)
        /// 3. Interface.FlightPlanExists 与 FlightPlanNumberOfManoeuvres 验证
        /// 彻底杜绝 native C++ std::abort 崩溃。
        /// </summary>
        public static bool HasFlightPlan()
        {
            if (!_isAvailable) return false;
            int currentFrame = Time.frameCount;
            if (_lastFlightPlanFrame == currentFrame)
            {
                return _cachedFlightPlanResult;
            }
            _lastFlightPlanFrame = currentFrame;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                _cachedFlightPlanResult = false;
                return false;
            }

            // 1. 如果已能从 burn_editors_ 读到活动编辑器且数量大于0，直接确认为 true
            object planner = GetFlightPlannerInstance();
            if (planner != null && _burnEditorsField != null)
            {
                try
                {
                    var list = _burnEditorsField.GetValue(planner) as IList;
                    if (list != null && list.Count > 0)
                    {
                        _cachedFlightPlanResult = true;
                        return true;
                    }
                }
                catch { }
            }

            // 2. 通过 Principia native 接口权威查询
            IntPtr plugin = GetPluginPointer();
            if (plugin == IntPtr.Zero)
            {
                _cachedFlightPlanResult = false;
                return false;
            }

            string guid = v.id.ToString();

            // 必须先验证载具在 Principia 中存在，避免 native CHECK 失败
            if (_hasVesselMethod != null)
            {
                try
                {
                    bool hasVessel = (bool)_hasVesselMethod.Invoke(null, new object[] { plugin, guid });
                    if (!hasVessel)
                    {
                        _cachedFlightPlanResult = false;
                        return false;
                    }
                }
                catch
                {
                    _cachedFlightPlanResult = false;
                    return false;
                }
            }

            // 检查 FlightPlan 是否存在
            if (_flightPlanExistsMethod != null)
            {
                try
                {
                    bool exists = (bool)_flightPlanExistsMethod.Invoke(null, new object[] { plugin, guid });
                    if (!exists)
                    {
                        _cachedFlightPlanResult = false;
                        return false;
                    }
                }
                catch
                {
                    _cachedFlightPlanResult = false;
                    return false;
                }
            }
            else
            {
                _cachedFlightPlanResult = false;
                return false;
            }

            // 检查计划机动数量是否大于 0
            if (_flightPlanNumManoeuvresMethod != null)
            {
                try
                {
                    int count = (int)_flightPlanNumManoeuvresMethod.Invoke(null, new object[] { plugin, guid });
                    bool hasManoeuvre = count > 0;
                    _cachedFlightPlanResult = hasManoeuvre;
                    return hasManoeuvre;
                }
                catch
                {
                    _cachedFlightPlanResult = false;
                    return false;
                }
            }

            _cachedFlightPlanResult = false;
            return false;
        }

        public static bool HasActiveFlightPlan => HasFlightPlan();

        private static object GetCurrentManoeuvreOrEditor()
        {
            object planner = GetFlightPlannerInstance();
            if (planner == null) return null;

            // 1. 优先从 burn_editors_ 列表读取第一个活动编辑器 (最安全、开销最低)
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

            // 2. 只有在严格确认载具存在、FlightPlan 存在且机动数 > 0 的情况下，才允许调用 GetManœuvre(0)
            if (HasFlightPlan())
            {
                if (_getManoeuvreMethod != null)
                {
                    try
                    {
                        return _getManoeuvreMethod.Invoke(planner, new object[] { 0 });
                    }
                    catch { }
                }

                if (_flightPlanGetManoeuvreMethod != null)
                {
                    try
                    {
                        IntPtr plugin = GetPluginPointer();
                        Vessel v = FlightGlobals.ActiveVessel;
                        if (plugin != IntPtr.Zero && v != null)
                        {
                            return _flightPlanGetManoeuvreMethod.Invoke(null, new object[] { plugin, v.id.ToString(), 0 });
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static object _cachedAnalysisElements;
        private static float _lastAnalysisElementsTime = -10f;

        private static object GetAnalysisElements()
        {
            if (!_isAvailable) return null;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return null;

            float now = Time.unscaledTime;
            if (now - _lastAnalysisElementsTime < 1.0f)
            {
                return _cachedAnalysisElements;
            }
            _lastAnalysisElementsTime = now;

            object analyser = GetOrbitAnalyserInstance();
            if (analyser == null || _getAnalysisMethod == null) return null;

            // 检查 Principia 是否已跟踪该载具
            string guid = v.id.ToString();
            IntPtr plugin = GetPluginPointer();
            if (plugin == IntPtr.Zero) return null;

            if (_hasVesselMethod != null)
            {
                try
                {
                    bool hasVessel = (bool)_hasVesselMethod.Invoke(null, new object[] { plugin, guid });
                    if (!hasVessel) return null;
                }
                catch { return null; }
            }

            try
            {
                object analysis = _getAnalysisMethod.Invoke(analyser, null);
                if (analysis != null)
                {
                    FieldInfo fi = analysis.GetType().GetField("elements", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null)
                    {
                        _cachedAnalysisElements = fi.GetValue(analysis);
                        return _cachedAnalysisElements;
                    }
                }
            }
            catch { }
            return null;
        }

        private static double ExtractDoubleFromElements(params string[] candidateNames)
        {
            object el = GetAnalysisElements();
            if (el == null) return double.NaN;

            double val = TryGetFieldOrPropertyDouble(el, candidateNames);
            if (!double.IsNaN(val)) return val;

            FieldInfo[] fields = el.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                Type ft = fields[i].FieldType;
                if (!ft.IsPrimitive && ft != typeof(string) && ft != typeof(decimal))
                {
                    object subObj = fields[i].GetValue(el);
                    if (subObj != null)
                    {
                        val = TryGetFieldOrPropertyDouble(subObj, candidateNames);
                        if (!double.IsNaN(val)) return val;
                    }
                }
            }
            return double.NaN;
        }

        private static double TryGetFieldOrPropertyDouble(object target, string[] candidateNames)
        {
            if (target == null) return double.NaN;
            Type t = target.GetType();
            for (int i = 0; i < candidateNames.Length; i++)
            {
                string name = candidateNames[i];
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (fi != null)
                {
                    object fv = fi.GetValue(target);
                    if (fv != null)
                    {
                        try { return Convert.ToDouble(fv); } catch { }
                    }
                }
                PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pi != null)
                {
                    object pv = pi.GetValue(target, null);
                    if (pv != null)
                    {
                        try { return Convert.ToDouble(pv); } catch { }
                    }
                }
            }
            return double.NaN;
        }

        public static double ResolveNumeric(string subTag, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveNumeric(subTag, modifier) : double.NaN;
        }

        public static string ResolveString(string subTag, string format = null, string modifier = null)
        {
            return _isAvailable ? Traverser.ResolveString(subTag, format, modifier) : "---";
        }

        private static int _lastFrameNameFrame = -1;
        private static string _cachedFrameName = "SURFACE";
        private static int _lastNavballNameFrame = -1;
        private static string _cachedNavballName = "SURFACE";

        // 强类型便捷属性与参考系控制
        public static string FrameName
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastFrameNameFrame == frame) return _cachedFrameName;
                _lastFrameNameFrame = frame;
                _cachedFrameName = ResolveString("FrameName");
                return _cachedFrameName;
            }
        }

        public static string NavballFrameName
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastNavballNameFrame == frame) return _cachedNavballName;
                _lastNavballNameFrame = frame;
                _cachedNavballName = ResolveString("NavballFrameName");
                return _cachedNavballName;
            }
        }

        public static double ManeuverDeltaV => HasFlightPlan() ? ResolveNumeric("ManeuverDeltaV") : double.NaN;
        public static double ManeuverDuration => HasFlightPlan() ? ResolveNumeric("ManeuverDuration") : double.NaN;
        public static double TimeToManeuver => HasFlightPlan() ? ResolveNumeric("TimeToManeuver") : double.NaN;
        public static string OrbitDescription => ResolveString("OrbitDescription");
        public static double ManeuverDeltaVPrograde => TryGetManeuverVector(out double p, out _, out _) ? p : double.NaN;
        public static double ManeuverDeltaVNormal => TryGetManeuverVector(out _, out double n, out _) ? n : double.NaN;
        public static double ManeuverDeltaVRadial => TryGetManeuverVector(out _, out _, out double r) ? r : double.NaN;

        private static int _lastManeuverVectorFrame = -1;
        private static bool _cachedManeuverVectorResult = false;
        private static double _cachedMdvPrograde, _cachedMdvNormal, _cachedMdvRadial;

        public static bool TryGetManeuverVector(out double prograde, out double normal, out double radial)
        {
            int currentFrame = Time.frameCount;
            if (_lastManeuverVectorFrame == currentFrame)
            {
                prograde = _cachedMdvPrograde;
                normal = _cachedMdvNormal;
                radial = _cachedMdvRadial;
                return _cachedManeuverVectorResult;
            }
            _lastManeuverVectorFrame = currentFrame;

            prograde = 0.0;
            normal = 0.0;
            radial = 0.0;
            if (!HasFlightPlan())
            {
                _cachedManeuverVectorResult = false;
                return false;
            }

            object man = GetCurrentManoeuvreOrEditor();
            if (man == null)
            {
                _cachedManeuverVectorResult = false;
                return false;
            }

            // 1. 尝试从 burn.delta_v 读取 (Frenet frame: x=Tangent/Prograde, y=Normal/Radial, z=Binormal/Normal)
            try
            {
                FieldInfo burnFi = man.GetType().GetField("burn", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (burnFi != null)
                {
                    object burn = burnFi.GetValue(man);
                    if (burn != null)
                    {
                        FieldInfo dvFi = burn.GetType().GetField("delta_v", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        if (dvFi != null)
                        {
                            object xyz = dvFi.GetValue(burn);
                            if (xyz != null)
                            {
                                FieldInfo xf = xyz.GetType().GetField("x", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                FieldInfo yf = xyz.GetType().GetField("y", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                FieldInfo zf = xyz.GetType().GetField("z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                if (xf != null && yf != null && zf != null)
                                {
                                    prograde = Convert.ToDouble(xf.GetValue(xyz));
                                    radial = Convert.ToDouble(yf.GetValue(xyz));
                                    normal = Convert.ToDouble(zf.GetValue(xyz));
                                    _cachedMdvPrograde = prograde;
                                    _cachedMdvNormal = normal;
                                    _cachedMdvRadial = radial;
                                    _cachedManeuverVectorResult = true;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. 尝试从 BurnEditor 的各个分量读取
            try
            {
                FieldInfo fiTangent = man.GetType().GetField("Δv_tangent_", BindingFlags.NonPublic | BindingFlags.Instance)
                                   ?? man.GetType().GetField("Δv_tangent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo fiNormal = man.GetType().GetField("Δv_normal_", BindingFlags.NonPublic | BindingFlags.Instance)
                                  ?? man.GetType().GetField("Δv_normal", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo fiBinormal = man.GetType().GetField("Δv_binormal_", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?? man.GetType().GetField("Δv_binormal", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (fiTangent != null && fiNormal != null && fiBinormal != null)
                {
                    prograde = Convert.ToDouble(fiTangent.GetValue(man));
                    radial = Convert.ToDouble(fiNormal.GetValue(man));
                    normal = Convert.ToDouble(fiBinormal.GetValue(man));
                    _cachedMdvPrograde = prograde;
                    _cachedMdvNormal = normal;
                    _cachedMdvRadial = radial;
                    _cachedManeuverVectorResult = true;
                    return true;
                }
            }
            catch { }

            // 3. 尝试从 first_component_, second_component_, third_component_ 读取 (Principia GUI 输入框)
            try
            {
                FieldInfo fc1 = man.GetType().GetField("first_component_", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fc2 = man.GetType().GetField("second_component_", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fc3 = man.GetType().GetField("third_component_", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fc1 != null && fc2 != null && fc3 != null)
                {
                    object v1 = fc1.GetValue(man);
                    object v2 = fc2.GetValue(man);
                    object v3 = fc3.GetValue(man);
                    if (v1 != null && v2 != null && v3 != null)
                    {
                        if (double.TryParse(v1.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d1) &&
                            double.TryParse(v2.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d2) &&
                            double.TryParse(v3.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d3))
                        {
                            prograde = d1;
                            normal = d2;
                            radial = d3;
                            _cachedMdvPrograde = prograde;
                            _cachedMdvNormal = normal;
                            _cachedMdvRadial = radial;
                            _cachedManeuverVectorResult = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            _cachedManeuverVectorResult = false;
            return false;
        }

        private static int _lastTargetFrameCheckFrame = -1;
        private static bool _cachedIsTargetFrame;

        public static bool IsTargetFrameSelected
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastTargetFrameCheckFrame == frame) return _cachedIsTargetFrame;
                _lastTargetFrameCheckFrame = frame;

                object sel = GetFrameSelectorInstance();
                if (sel != null && _targetFrameSelectedProp != null)
                {
                    try { _cachedIsTargetFrame = (bool)_targetFrameSelectedProp.GetValue(sel, null); return _cachedIsTargetFrame; } catch { }
                }
                _cachedIsTargetFrame = false;
                return false;
            }
        }

        private static int _lastFrameTypeCheckFrame = -1;
        private static string _cachedFrameTypeString;

        public static string FrameTypeString
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastFrameTypeCheckFrame == frame) return _cachedFrameTypeString;
                _lastFrameTypeCheckFrame = frame;

                object sel = GetFrameSelectorInstance();
                if (sel != null && _frameTypeProp != null)
                {
                    try { _cachedFrameTypeString = _frameTypeProp.GetValue(sel, null)?.ToString(); return _cachedFrameTypeString; } catch { }
                }
                _cachedFrameTypeString = null;
                return null;
            }
        }

        private static int _lastSurfaceFrameCheckFrame = -1;
        private static bool _cachedIsSurfaceFrame;

        public static bool IsSurfaceFrameSelected
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastSurfaceFrameCheckFrame == frame) return _cachedIsSurfaceFrame;
                _lastSurfaceFrameCheckFrame = frame;

                object sel = GetFrameSelectorInstance();
                if (sel != null && _isSurfaceFrameMethod != null)
                {
                    try { _cachedIsSurfaceFrame = (bool)_isSurfaceFrameMethod.Invoke(sel, null); return _cachedIsSurfaceFrame; } catch { }
                }
                _cachedIsSurfaceFrame = false;
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

        public enum ReferenceFrameCategory
        {
            Inertial,
            Surface,
            Orbital,
            Lagrange,
            Target
        }

        private static int _lastCategoryCheckFrame = -1;
        private static ReferenceFrameCategory _cachedCategoryResult;

        public static ReferenceFrameCategory CurrentFrameCategory
        {
            get
            {
                int frame = Time.frameCount;
                if (_lastCategoryCheckFrame == frame) return _cachedCategoryResult;
                _lastCategoryCheckFrame = frame;

                if (IsTargetFrameSelected) { _cachedCategoryResult = ReferenceFrameCategory.Target; return _cachedCategoryResult; }
                object sel = GetFrameSelectorInstance();
                if (sel != null && _frameTypeProp != null)
                {
                    try
                    {
                        object val = _frameTypeProp.GetValue(sel, null);
                        if (val != null)
                        {
                            int intVal = Convert.ToInt32(val);
                            switch (intVal)
                            {
                                case 6000: _cachedCategoryResult = ReferenceFrameCategory.Inertial; return _cachedCategoryResult;
                                case 6001: _cachedCategoryResult = ReferenceFrameCategory.Lagrange; return _cachedCategoryResult;
                                case 6002: _cachedCategoryResult = ReferenceFrameCategory.Orbital; return _cachedCategoryResult;
                                case 6003: _cachedCategoryResult = ReferenceFrameCategory.Surface; return _cachedCategoryResult;
                                case 6004: _cachedCategoryResult = ReferenceFrameCategory.Lagrange; return _cachedCategoryResult;
                            }
                        }
                    }
                    catch { }
                }
                if (IsSurfaceFrameSelected) { _cachedCategoryResult = ReferenceFrameCategory.Surface; return _cachedCategoryResult; }
                _cachedCategoryResult = ReferenceFrameCategory.Inertial;
                return _cachedCategoryResult;
            }
        }

        public static void ToggleReferenceFrameWindow()
        {
            object sel = GetFrameSelectorInstance();
            if (sel != null && _toggleMethod != null)
            {
                try { _toggleMethod.Invoke(sel, null); } catch { }
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

        private static object CreateQp(Vector3d pos, Vector3d vel)
        {
            if (_qpType == null || _xyzType == null || _qpQField == null || _qpPField == null ||
                _xyzXField == null || _xyzYField == null || _xyzZField == null) return null;
            try
            {
                object qp = Activator.CreateInstance(_qpType);
                object q = Activator.CreateInstance(_xyzType);
                object p = Activator.CreateInstance(_xyzType);

                _xyzXField.SetValue(q, pos.x);
                _xyzYField.SetValue(q, pos.y);
                _xyzZField.SetValue(q, pos.z);

                _xyzXField.SetValue(p, vel.x);
                _xyzYField.SetValue(p, vel.y);
                _xyzZField.SetValue(p, vel.z);

                _qpQField.SetValue(qp, q);
                _qpPField.SetValue(qp, p);
                return qp;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryExtractXyz(object xyz, out Vector3d result)
        {
            result = Vector3d.zero;
            if (xyz == null) return false;
            try
            {
                if (xyz is Vector3d v)
                {
                    result = v;
                    return true;
                }
                if (_xyzXField != null && _xyzYField != null && _xyzZField != null)
                {
                    double x = Convert.ToDouble(_xyzXField.GetValue(xyz));
                    double y = Convert.ToDouble(_xyzYField.GetValue(xyz));
                    double z = Convert.ToDouble(_xyzZField.GetValue(xyz));
                    result = new Vector3d(x, y, z);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static int _lastActiveVesselVelocityFrame = -1;
        private static bool _lastActiveVesselVelocityResult = false;
        private static Vector3d _cachedActiveVesselVelocity;

        /// <summary>
        /// 获取载具在当前 Principia 绘制参考系下的速度矢量 (m/s)
        /// 严格遵循 Principia 官方解算策略：
        /// 1. 若当前载具被 Principia 纳管，优先调用原生 VesselVelocity(plugin, guid)；
        /// 2. 若当前载具未纳管或处于过渡态，调用原生 UnmanageableVesselVelocity 兜底计算，0 盲目估算，100% 绝对物理线速度。
        /// </summary>
        public static bool GetVesselVelocity(Vessel vessel, out Vector3d velocity)
        {
            velocity = Vector3d.zero;
            if (!_isAvailable || vessel == null) return false;

            bool isActiveVessel = (FlightGlobals.ActiveVessel == vessel);
            int frame = Time.frameCount;
            if (isActiveVessel && _lastActiveVesselVelocityFrame == frame)
            {
                velocity = _cachedActiveVesselVelocity;
                return _lastActiveVesselVelocityResult;
            }

            IntPtr plugin = GetPluginPointer();
            if (plugin == IntPtr.Zero)
            {
                if (isActiveVessel)
                {
                    _lastActiveVesselVelocityFrame = frame;
                    _lastActiveVesselVelocityResult = false;
                }
                return false;
            }

            string guid = vessel.id.ToString();
            object adapter = GetAdapterInstance();

            bool hasManageable = false;
            if (adapter != null && _hasActiveManageableVesselMethod != null)
            {
                try { hasManageable = (bool)_hasActiveManageableVesselMethod.Invoke(adapter, null); }
                catch { }
            }
            else
            {
                hasManageable = true;
            }

            bool hasVessel = false;
            if (_hasVesselMethod != null)
            {
                try { hasVessel = (bool)_hasVesselMethod.Invoke(null, new object[] { plugin, guid }); }
                catch { }
            }

            // 1. 如果 Principia 已纳管该活动载具，直接从 C++ 核心获取当前参考系速度矢量
            if (hasManageable && hasVessel && _vesselVelocityMethod != null)
            {
                try
                {
                    object xyz = _vesselVelocityMethod.Invoke(null, new object[] { plugin, guid });
                    if (TryExtractXyz(xyz, out velocity))
                    {
                        if (isActiveVessel)
                        {
                            _lastActiveVesselVelocityFrame = frame;
                            _cachedActiveVesselVelocity = velocity;
                            _lastActiveVesselVelocityResult = true;
                        }
                        return true;
                    }
                }
                catch { }
            }

            // 2. 兜底策略：调用 Principia 官方导出的 UnmanageableVesselVelocity 接口
            if (_unmanageableVesselVelocityMethod != null && vessel.orbit != null && vessel.orbit.referenceBody != null)
            {
                try
                {
                    object qp = CreateQp(vessel.orbit.pos, vessel.orbit.vel);
                    if (qp != null)
                    {
                        int bodyIndex = vessel.orbit.referenceBody.flightGlobalsIndex;
                        object xyz = _unmanageableVesselVelocityMethod.Invoke(null, new object[] { plugin, qp, bodyIndex });
                        if (TryExtractXyz(xyz, out velocity))
                        {
                            if (isActiveVessel)
                            {
                                _lastActiveVesselVelocityFrame = frame;
                                _cachedActiveVesselVelocity = velocity;
                                _lastActiveVesselVelocityResult = true;
                            }
                            return true;
                        }
                    }
                }
                catch { }
            }

            if (isActiveVessel)
            {
                _lastActiveVesselVelocityFrame = frame;
                _lastActiveVesselVelocityResult = false;
            }
            return false;
        }

        public static bool GetVesselVelocity(string guid, out Vector3d velocity)
        {
            velocity = Vector3d.zero;
            if (string.IsNullOrEmpty(guid)) return false;

            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && v.id.ToString() == guid)
            {
                return GetVesselVelocity(v, out velocity);
            }

            if (FlightGlobals.fetch != null && FlightGlobals.Vessels != null)
            {
                for (int i = 0; i < FlightGlobals.Vessels.Count; i++)
                {
                    var cand = FlightGlobals.Vessels[i];
                    if (cand != null && cand.id.ToString() == guid)
                    {
                        return GetVesselVelocity(cand, out velocity);
                    }
                }
            }
            return false;
        }

        private static int _lastFrenetFrame = -1;
        private static bool _lastFrenetResult = false;
        private static Vector3d _cachedFrenetTangent;
        private static Vector3d _cachedFrenetNormal;
        private static Vector3d _cachedFrenetBinormal;

        /// <summary>
        /// 获取当前绘制参考系下 Frenet 三轴基底（切向 Prograde、法向 Normal、副法向/径向 Radial）
        /// 支持单帧高保真快照缓存，彻底消除一帧内 10 余个标线重复 P/Invoke 原生 C++ 接口造成的性能雪崩。
        /// </summary>
        public static bool GetVesselFrenetTrihedron(out Vector3d tangent, out Vector3d normal, out Vector3d binormal)
        {
            int currentFrame = Time.frameCount;
            if (_lastFrenetFrame == currentFrame)
            {
                tangent = _cachedFrenetTangent;
                normal = _cachedFrenetNormal;
                binormal = _cachedFrenetBinormal;
                return _lastFrenetResult;
            }
            _lastFrenetFrame = currentFrame;

            tangent = Vector3d.zero;
            normal = Vector3d.zero;
            binormal = Vector3d.zero;
            if (!_isAvailable || _vesselTangentMethod == null || _vesselNormalMethod == null || _vesselBinormalMethod == null)
            {
                _lastFrenetResult = false;
                return false;
            }

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                _lastFrenetResult = false;
                return false;
            }

            IntPtr plugin = GetPluginPointer();
            if (plugin == IntPtr.Zero)
            {
                _lastFrenetResult = false;
                return false;
            }

            string guid = v.id.ToString();
            if (_hasVesselMethod != null)
            {
                try
                {
                    bool hasVessel = (bool)_hasVesselMethod.Invoke(null, new object[] { plugin, guid });
                    if (!hasVessel)
                    {
                        _lastFrenetResult = false;
                        return false;
                    }
                }
                catch
                {
                    _lastFrenetResult = false;
                    return false;
                }
            }

            try
            {
                object tObj = _vesselTangentMethod.Invoke(null, new object[] { plugin, guid });
                object nObj = _vesselNormalMethod.Invoke(null, new object[] { plugin, guid });
                object bObj = _vesselBinormalMethod.Invoke(null, new object[] { plugin, guid });
                if (TryExtractXyz(tObj, out tangent) && TryExtractXyz(nObj, out normal) && TryExtractXyz(bObj, out binormal))
                {
                    _cachedFrenetTangent = tangent;
                    _cachedFrenetNormal = normal;
                    _cachedFrenetBinormal = binormal;
                    _lastFrenetResult = true;
                    return true;
                }
            }
            catch { }

            _lastFrenetResult = false;
            return false;
        }

        /// <summary>
        /// 获取活动载具在当前 Principia 权威绘制参考系下的标量速率 (m/s)
        /// 支持日心惯性系、地月质心系、脉动系、地表系及目标系
        /// </summary>
        public static bool GetActiveVesselSpeed(out double speed)
        {
            speed = 0.0;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return false;

            if (GetVesselVelocity(v, out Vector3d vel))
            {
                speed = vel.magnitude;
                return !double.IsNaN(speed) && !double.IsInfinity(speed);
            }
            return false;
        }

        /// <summary>
        /// 场景切换或载具切换时彻底失效所有缓存
        /// </summary>
        public static void InvalidateCaches()
        {
            _cachedPluginPointer = IntPtr.Zero;
            _cachedPluginPointerFrame = -1;
            _lastFlightPlanFrame = -1;
            _lastManeuverVectorFrame = -1;
            _lastTargetFrameCheckFrame = -1;
            _lastFrameTypeCheckFrame = -1;
            _lastSurfaceFrameCheckFrame = -1;
            _lastCategoryCheckFrame = -1;
            _lastFrameNameFrame = -1;
            _lastNavballNameFrame = -1;
            _lastActiveVesselVelocityFrame = -1;
            _lastFrenetFrame = -1;
            _lastAnalysisElementsTime = -10f;
            _cachedAdapterInstance = null;
        }
    }
}
