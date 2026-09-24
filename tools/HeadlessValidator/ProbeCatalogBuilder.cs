using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace ModularFlightPanel.HeadlessValidator
{
    public class ProbeCatalogBuilder
    {
        private readonly string _repoRoot;
        private readonly string _kspGameData;
        private readonly Dictionary<string, AssemblyDefinition> _loadedAssemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);

        public ProbeCatalogBuilder(string repoRoot, string kspGameData)
        {
            _repoRoot = repoRoot;
            _kspGameData = kspGameData;
        }

        public List<ProbeDefinitionModel> BuildAllProbes()
        {
            var probes = new List<ProbeDefinitionModel>();

            // 1. FarProbe (FAR / FARC)
            probes.Add(BuildFarProbe());

            // 2. KerbalEngineerProbe (KER)
            probes.Add(BuildKerbalEngineerProbe());

            // 3. MechJebProbe (MJ / MechJeb 2)
            probes.Add(BuildMechJebProbe());

            // 4. PrincipiaProbe (PRINCIPIA)
            probes.Add(BuildPrincipiaProbe());

            // 5. KerbalismProbe (KERBALISM)
            probes.Add(BuildKerbalismProbe());

            // 6. RealAntennasProbe (RA)
            probes.Add(BuildRealAntennasProbe());

            // 7. TrajectoriesProbe (TRAJ)
            probes.Add(BuildTrajectoriesProbe());

            // 8. DockingAlignmentProbe (DOCK / DPAI)
            probes.Add(BuildDockingAlignmentProbe());

            // 9. GPWSProbe (GPWS)
            probes.Add(BuildGPWSProbe());

            // 10. RealFuelsProbe (RF)
            probes.Add(BuildRealFuelsProbe());

            // 11. TestFlightProbe (TF)
            probes.Add(BuildTestFlightProbe());

            // 12. DynamicBatteryStorageProbe (DBS)
            probes.Add(BuildDynamicBatteryStorageProbe());

            // 13. SystemHeatProbe (SH)
            probes.Add(BuildSystemHeatProbe());

            // 14. AtmosphereAutopilotProbe (AA)
            probes.Add(BuildAtmosphereAutopilotProbe());

            // 15. RP1AvionicsProbe (RP1)
            probes.Add(BuildRP1AvionicsProbe());

            return probes;
        }

        #region Helper: Cecil Reflection & Type Extraction

        private AssemblyDefinition TryLoadAssembly(string dllRelativeOrFileName)
        {
            if (string.IsNullOrEmpty(_kspGameData) || !Directory.Exists(_kspGameData)) return null;

            if (_loadedAssemblies.TryGetValue(dllRelativeOrFileName, out var cached))
                return cached;

            string fullPath = Path.Combine(_kspGameData, dllRelativeOrFileName);
            if (!File.Exists(fullPath))
            {
                // 全局深层扫描匹配
                try
                {
                    string fileName = Path.GetFileName(dllRelativeOrFileName);
                    string[] found = Directory.GetFiles(_kspGameData, fileName, SearchOption.AllDirectories);
                    if (found.Length > 0) fullPath = found[0];
                    else return null;
                }
                catch { return null; }
            }

            try
            {
                var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(Path.GetDirectoryName(fullPath));
                string managedDir = Path.Combine(_kspGameData, "..", "KSP_x64_Data", "Managed");
                if (Directory.Exists(managedDir)) resolver.AddSearchDirectory(managedDir);

                var readerParams = new ReaderParameters { AssemblyResolver = resolver };
                var asm = AssemblyDefinition.ReadAssembly(fullPath, readerParams);
                _loadedAssemblies[dllRelativeOrFileName] = asm;
                return asm;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CatalogBuilder] 加载程序集 {dllRelativeOrFileName} 失败: {ex.Message}");
                return null;
            }
        }

        private TypeDefinition FindTypeInAssembly(AssemblyDefinition asm, string typeFullName)
        {
            if (asm == null) return null;
            foreach (var module in asm.Modules)
            {
                var type = module.GetType(typeFullName);
                if (type != null) return type;
                // 也尝试按 Simple Name 匹配
                foreach (var t in module.GetTypes())
                {
                    if (t.FullName.Equals(typeFullName, StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Equals(typeFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private void ExtractPublicMembersFromType(
            TypeDefinition typeDef,
            string modTag,
            List<TelemetryParameterModel> targetList,
            string categoryOverride = null,
            bool includeStatic = true,
            bool includeInstance = true)
        {
            if (typeDef == null) return;

            var existingNames = new HashSet<string>(targetList.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            // 1. 公开字段
            foreach (var field in typeDef.Fields)
            {
                if (!field.IsPublic || field.IsSpecialName) continue;
                if (field.IsStatic && !includeStatic) continue;
                if (!field.IsStatic && !includeInstance) continue;
                if (existingNames.Contains(field.Name)) continue;

                string returnType = CleanTypeName(field.FieldType.FullName);
                string unit = InferUnitFromName(field.Name, returnType);
                var subModifiers = InferSubModifiers(returnType);

                targetList.Add(new TelemetryParameterModel
                {
                    Name = field.Name,
                    Token = $"{{{modTag}:{field.Name}}}",
                    Type = returnType,
                    Unit = unit,
                    AccessType = field.IsStatic ? "PublicStaticField" : "PublicInstanceField",
                    SourceType = typeDef.FullName,
                    DescriptionZh = $"{categoryOverride ?? typeDef.Name} 字段: {field.Name}",
                    DescriptionEn = $"{typeDef.Name}.{field.Name} (Field)",
                    Aliases = new List<string>(),
                    SubModifiers = subModifiers
                });
                existingNames.Add(field.Name);
            }

            // 2. 公开属性
            foreach (var prop in typeDef.Properties)
            {
                if (prop.GetMethod == null || !prop.GetMethod.IsPublic) continue;
                if (prop.GetMethod.Parameters.Count > 0) continue; // 排除索引器
                if (prop.GetMethod.IsStatic && !includeStatic) continue;
                if (!prop.GetMethod.IsStatic && !includeInstance) continue;
                if (existingNames.Contains(prop.Name)) continue;

                string returnType = CleanTypeName(prop.PropertyType.FullName);
                string unit = InferUnitFromName(prop.Name, returnType);
                var subModifiers = InferSubModifiers(returnType);

                targetList.Add(new TelemetryParameterModel
                {
                    Name = prop.Name,
                    Token = $"{{{modTag}:{prop.Name}}}",
                    Type = returnType,
                    Unit = unit,
                    AccessType = prop.GetMethod.IsStatic ? "PublicStaticProperty" : "PublicInstanceProperty",
                    SourceType = typeDef.FullName,
                    DescriptionZh = $"{categoryOverride ?? typeDef.Name} 属性: {prop.Name}",
                    DescriptionEn = $"{typeDef.Name}.{prop.Name} (Property)",
                    Aliases = new List<string>(),
                    SubModifiers = subModifiers
                });
                existingNames.Add(prop.Name);
            }

            // 3. 无参公开方法 (返回非 void)
            foreach (var method in typeDef.Methods)
            {
                if (!method.IsPublic || method.IsSpecialName) continue;
                if (method.Parameters.Count > 0) continue;
                if (method.ReturnType.FullName == "System.Void") continue;
                if (method.IsStatic && !includeStatic) continue;
                if (!method.IsStatic && !includeInstance) continue;
                if (existingNames.Contains(method.Name)) continue;
                if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")) continue;

                string returnType = CleanTypeName(method.ReturnType.FullName);
                string unit = InferUnitFromName(method.Name, returnType);
                var subModifiers = InferSubModifiers(returnType);

                targetList.Add(new TelemetryParameterModel
                {
                    Name = method.Name,
                    Token = $"{{{modTag}:{method.Name}}}",
                    Type = returnType,
                    Unit = unit,
                    AccessType = method.IsStatic ? "PublicStaticMethod" : "PublicInstanceMethod",
                    SourceType = typeDef.FullName,
                    DescriptionZh = $"{categoryOverride ?? typeDef.Name} 无参方法: {method.Name}()",
                    DescriptionEn = $"{typeDef.Name}.{method.Name}() (Method)",
                    Aliases = new List<string>(),
                    SubModifiers = subModifiers
                });
                existingNames.Add(method.Name);
            }
        }

        private static string CleanTypeName(string full)
        {
            if (string.IsNullOrEmpty(full)) return "System.Object";
            if (full.EndsWith("&")) full = full.Substring(0, full.Length - 1);
            if (full.Contains("`"))
            {
                int idx = full.IndexOf('`');
                full = full.Substring(0, idx);
            }
            return full;
        }

        private static string InferUnitFromName(string name, string type)
        {
            string n = name.ToLowerInvariant();
            if (type == "System.Boolean" || n.StartsWith("is") || n.StartsWith("has")) return "bool (0/1)";
            if (n.Contains("altitude") || n.Contains("alt") || n.EndsWith("dist") || n.Contains("distance") || n.Contains("radius") || n.Contains("sma") || n.Contains("periapsis") || n.Contains("apoapsis")) return "m";
            if (n.Contains("speed") || n.Contains("velocity") || n.Contains("vel") || n.EndsWith("ias") || n.EndsWith("eas") || n.EndsWith("tas")) return "m/s";
            if (n.Contains("mach")) return "Mach";
            if (n.Contains("accel") || n.Contains("gforce") || n.Contains("g_force") || n.EndsWith("gload")) return "G";
            if (n.Contains("press") || n.Contains("q_") || n.EndsWith("q")) return "Pa";
            if (n.Contains("temp")) return "K";
            if (n.Contains("thrust") || n.Contains("force") || n.Contains("drag") || n.Contains("lift")) return "kN";
            if (n.Contains("twr")) return "ratio";
            if (n.Contains("angle") || n.Contains("pitch") || n.Contains("yaw") || n.Contains("roll") || n.Contains("heading") || n.Contains("aoa") || n.Contains("inc") || n.Contains("inclination") || n.Contains("lan") || n.Contains("latitude") || n.Contains("longitude") || n.Contains("lat") || n.Contains("lon")) return "deg";
            if (n.Contains("mass")) return "t";
            if (n.Contains("density")) return "kg/m³";
            if (n.Contains("time") || n.Contains("period") || n.Contains("duration") || n.Contains("countdown")) return "s";
            if (n.Contains("percent") || n.Contains("ratio") || n.Contains("utilization") || n.Contains("stability")) return "0..1 / %";
            if (n.Contains("flux") || n.Contains("power") || n.Contains("watt")) return "W / kW";
            if (n.Contains("rate") && n.Contains("data")) return "b/s";
            if (n.Contains("rate") && n.Contains("rad")) return "rad/h";
            return "";
        }

        private static List<string> InferSubModifiers(string returnType)
        {
            if (returnType.Contains("Vector3") || returnType.Contains("Vector3d"))
            {
                return new List<string> { ":X", ":Y", ":Z", ":MAG" };
            }
            if (returnType.Contains("Vector2") || returnType.Contains("Vector2d"))
            {
                return new List<string> { ":X", ":Y", ":MAG" };
            }
            return new List<string>();
        }

        #endregion

        #region 1. FarProbe (FAR / FARC)
        private ProbeDefinitionModel BuildFarProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "FAR",
                ProbeClassName = "FarProbe",
                DisplayName = "Ferram Aerospace Research (FAR / FARC)",
                TargetMod = "FerramAerospaceResearch",
                TargetAssembly = "FerramAerospaceResearch.dll",
                Category = "气动力学与跨音速分析",
                Description = "全面接管 KSP 气动模型的高逼真流体力学解算器，提供真实迎角、失速、马赫数、动压与气动系数",
                ModTagAliases = new List<string> { "FAR", "FARC" }
            };

            var asm = TryLoadAssembly(@"FerramAerospaceResearch\Plugins\FerramAerospaceResearch.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                var farApiType = FindTypeInAssembly(asm, "FerramAerospaceResearch.FARAPI");
                ExtractPublicMembersFromType(farApiType, "FAR", p.Parameters, "FARAPI 官方接口", true, false);

                var vfiType = FindTypeInAssembly(asm, "FerramAerospaceResearch.FARGUI.FARFlightGUI.VesselFlightInfo");
                ExtractPublicMembersFromType(vfiType, "FAR", p.Parameters, "FAR 气动解算结构体", false, true);

                var atmType = FindTypeInAssembly(asm, "FerramAerospaceResearch.FARAtmosphere");
                ExtractPublicMembersFromType(atmType, "FAR", p.Parameters, "FAR 大气物理模型", true, false);
            }

            // 合成指标与别名补全
            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "LiftToDragRatio",
                Token = "{FAR:LiftToDragRatio}",
                Type = "System.Double",
                Unit = "ratio",
                AccessType = "CustomSynthetic",
                SourceType = "FarProbe",
                DescriptionZh = "即时气动升阻效率比 (L/D)，高空滑翔与再入制导核心指标",
                DescriptionEn = "Instantaneous aerodynamic Lift-to-Drag ratio (L/D)",
                Aliases = new List<string> { "LD", "LIFTTODRAG" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "AtmosphericPressureAtm",
                Token = "{FAR:AtmosphericPressureAtm}",
                Type = "System.Double",
                Unit = "atm",
                AccessType = "CustomSynthetic",
                SourceType = "FarProbe",
                DescriptionZh = "当前环境真实大气静态压强 (atm)",
                DescriptionEn = "Ambient atmospheric static pressure (atm)",
                Aliases = new List<string> { "Q_ATM", "PATM" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "AtmosphericPressureKPa",
                Token = "{FAR:AtmosphericPressureKPa}",
                Type = "System.Double",
                Unit = "kPa",
                AccessType = "CustomSynthetic",
                SourceType = "FarProbe",
                DescriptionZh = "当前环境真实大气静态压强 (kPa)",
                DescriptionEn = "Ambient atmospheric static pressure (kPa)",
                Aliases = new List<string> { "QKPA", "PKPA" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "AtmosphericTemperatureCelsius",
                Token = "{FAR:AtmosphericTemperatureCelsius}",
                Type = "System.Double",
                Unit = "°C",
                AccessType = "CustomSynthetic",
                SourceType = "FarProbe",
                DescriptionZh = "当前环境大气静态摄氏温度 (°C)",
                DescriptionEn = "Ambient atmospheric static temperature (°C)",
                Aliases = new List<string> { "TCELSIUS", "TEMPC" }
            });

            // 高频别名映射
            AttachAliasesToParam(p.Parameters, "ActiveVesselIAS", new[] { "IAS", "INDICATEDAIRSPEED" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselEAS", new[] { "EAS", "EQUIVALENTAIRSPEED" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselMachNumber", new[] { "MACH", "MACHNUMBER" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselDynamicPressure", new[] { "Q", "DYNAMICTEMPERATURE", "DYNAMICPRESSURE" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselAngleOfAttack", new[] { "AOA", "ANGLEOFATTACK" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselSideslipAngle", new[] { "SIDESLIP", "BETA" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselStallPercentage", new[] { "STALL", "STALLPERCENT", "STALLRATIO" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselAeroForce", new[] { "AEROFORCE", "FORCE" });
            AttachAliasesToParam(p.Parameters, "ActiveVesselAirDensity", new[] { "AIRDENSITY", "RHO" });

            return p;
        }
        #endregion

        #region 2. KerbalEngineerProbe (KER)
        private ProbeDefinitionModel BuildKerbalEngineerProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "KER",
                ProbeClassName = "KerbalEngineerProbe",
                DisplayName = "Kerbal Engineer Redux (KER)",
                TargetMod = "KerbalEngineer",
                TargetAssembly = "KerbalEngineer.dll",
                Category = "分级遥测与飞行力学解算",
                Description = "KSP 经典仿真解算机，提供精准分级 Delta-V、燃时、TWR、自杀式点火距离与轨道交会参数",
                ModTagAliases = new List<string> { "KER", "ENGINEER" }
            };

            var asm = TryLoadAssembly(@"KerbalEngineer\KerbalEngineer.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KerbalEngineer.VesselSimulator.SimManager"), "KER", p.Parameters, "KER 仿真管理器", true, false);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KerbalEngineer.VesselSimulator.Stage"), "KER", p.Parameters, "KER 分级遥测", false, true);
                
                string[] procs = new[]
                {
                    "KerbalEngineer.Flight.Readouts.Vessel.SimulationProcessor",
                    "KerbalEngineer.Flight.Readouts.Vessel.AttitudeProcessor",
                    "KerbalEngineer.Flight.Readouts.Surface.AtmosphericProcessor",
                    "KerbalEngineer.Flight.Readouts.Surface.ImpactProcessor",
                    "KerbalEngineer.Flight.Readouts.Surface.SurfaceDistanceProcessor",
                    "KerbalEngineer.Flight.Readouts.Thermal.ThermalProcessor",
                    "KerbalEngineer.Flight.Readouts.Orbital.ManoeuvreNode.ManoeuvreProcessor",
                    "KerbalEngineer.Flight.Readouts.Rendezvous.RendezvousProcessor"
                };

                foreach (var pr in procs)
                {
                    var pt = FindTypeInAssembly(asm, pr);
                    if (pt != null) ExtractPublicMembersFromType(pt, "KER", p.Parameters, pt.Name, true, false);
                }
            }

            // 无论 DLL 是否在本地，填充标准遥测指标
            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "deltaV",
                Token = "{KER:deltaV}",
                Type = "System.Double",
                Unit = "m/s",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前激活级可用真空/大气 Delta-V 速度增量",
                DescriptionEn = "Current stage Delta-V capacity",
                Aliases = new List<string> { "DV", "STAGEDV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "totalDeltaV",
                Token = "{KER:totalDeltaV}",
                Type = "System.Double",
                Unit = "m/s",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "整舰全部剩余分级累计总 Delta-V",
                DescriptionEn = "Vessel total remaining Delta-V across all stages",
                Aliases = new List<string> { "TOTALDV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "actualThrustToWeight",
                Token = "{KER:actualThrustToWeight}",
                Type = "System.Double",
                Unit = "ratio",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前级实际推重比 (TWR)",
                DescriptionEn = "Current stage actual Thrust-to-Weight Ratio (TWR)",
                Aliases = new List<string> { "TWR", "STAGETWR" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "time",
                Token = "{KER:time}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前级发动机全推力剩余燃烧可用时长",
                DescriptionEn = "Current stage full throttle remaining burn time",
                Aliases = new List<string> { "BURNTIME", "STAGEBURNTIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SuicideCountdown",
                Token = "{KER:SuicideCountdown}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "动力着陆自杀式减速点火倒计时秒数",
                DescriptionEn = "Suicide burn ignition countdown in seconds",
                Aliases = new List<string> { "SUICIDECD" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SuicideAltitude",
                Token = "{KER:SuicideAltitude}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "动力减速自杀式点火的目标触发真实高度",
                DescriptionEn = "Suicide burn target trigger true altitude",
                Aliases = new List<string> { "SUICIDEALT" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SuicideDeltaV",
                Token = "{KER:SuicideDeltaV}",
                Type = "System.Double",
                Unit = "m/s",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "自杀式点火彻底消除地速所需的 Delta-V",
                DescriptionEn = "Delta-V required for suicide burn landing",
                Aliases = new List<string> { "SUICIDEDV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "totalTime",
                Token = "{KER:totalTime}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "整舰全部剩余分级累计燃烧可用总时长",
                DescriptionEn = "Total burn time across all remaining stages",
                Aliases = new List<string> { "TOTALBURNTIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "isp",
                Token = "{KER:isp}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前级发动机等效真空比冲 (Isp)",
                DescriptionEn = "Current stage engine effective specific impulse (Isp)",
                Aliases = new List<string> { "ISP", "STAGEISP" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "thrust",
                Token = "{KER:thrust}",
                Type = "System.Double",
                Unit = "kN",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前级发动机额定最大可用推力 (kN)",
                DescriptionEn = "Current stage engine rated maximum available thrust (kN)",
                Aliases = new List<string> { "THRUST" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "actualThrust",
                Token = "{KER:actualThrust}",
                Type = "System.Double",
                Unit = "kN",
                AccessType = "TraversedProperty",
                SourceType = "Stage",
                DescriptionZh = "当前级发动机当前油门开度下的即时实际推力 (kN)",
                DescriptionEn = "Current stage engine actual current delivered thrust (kN)",
                Aliases = new List<string> { "ACTUALTHRUST" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SuicideDistance",
                Token = "{KER:SuicideDistance}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "自杀式点火起始高度距离地面的直线距离 (m)",
                DescriptionEn = "Straight-line distance to ground at suicide burn trigger",
                Aliases = new List<string> { "SUICIDEDIST" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SuicideLength",
                Token = "{KER:SuicideLength}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "完成自杀式着陆减速点火全程预计所需秒数",
                DescriptionEn = "Total burn duration in seconds for suicide landing",
                Aliases = new List<string> { "SUICIDELEN" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactTime",
                Token = "{KER:ImpactTime}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "撞击地表或着陆预计剩余秒数 (s)",
                DescriptionEn = "Seconds remaining until terrain impact or touchdown",
                Aliases = new List<string> { "IMPACTTIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactAltitude",
                Token = "{KER:ImpactAltitude}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "TraversedProperty",
                SourceType = "ImpactProcessor",
                DescriptionZh = "预测落点所在的地表绝对真实海拔高度 (m)",
                DescriptionEn = "True terrain altitude at predicted impact site",
                Aliases = new List<string> { "IMPACTALT" }
            });

            return p;
        }
        #endregion

        #region 3. MechJebProbe (MJ / MechJeb 2)
        private ProbeDefinitionModel BuildMechJebProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "MJ",
                ProbeClassName = "MechJebProbe",
                DisplayName = "MechJeb 2 (MJ)",
                TargetMod = "MechJeb2",
                TargetAssembly = "MechJeb2.dll",
                Category = "自动导航机与飞行物理状态机",
                Description = "KSP 最庞大的自动飞行控制中枢，其 VesselState 实时维护 125+ 项核心物理量与机动节点解算",
                ModTagAliases = new List<string> { "MJ", "MECHJEB" }
            };

            var asm = TryLoadAssembly(@"MechJeb2\Plugins\MechJeb2.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                var vsType = FindTypeInAssembly(asm, "MuMech.VesselState");
                ExtractPublicMembersFromType(vsType, "MJ", p.Parameters, "VesselState 状态机", false, true);

                var mjCore = FindTypeInAssembly(asm, "MuMech.MechJebCore");
                ExtractPublicMembersFromType(mjCore, "MJ", p.Parameters, "MechJebCore 核心", false, true);

                var infoItems = FindTypeInAssembly(asm, "MuMech.MechJebModuleInfoItems");
                ExtractPublicMembersFromType(infoItems, "MJ", p.Parameters, "InfoItems 综合算法", false, true);
            }

            // 补充高频别名
            AttachAliasesToParam(p.Parameters, "deltaVStage", new[] { "DV", "STAGEDV" });
            AttachAliasesToParam(p.Parameters, "deltaVTotal", new[] { "TOTALDV" });
            AttachAliasesToParam(p.Parameters, "twr", new[] { "TWR" });
            AttachAliasesToParam(p.Parameters, "terminalVelocity", new[] { "TERMINALVEL" });
            AttachAliasesToParam(p.Parameters, "thrustCurrent", new[] { "CURRENTTHRUST" });
            AttachAliasesToParam(p.Parameters, "thrustAvailable", new[] { "THRUST" });
            AttachAliasesToParam(p.Parameters, "SurfaceTWR", new[] { "SURFACETWR" });
            AttachAliasesToParam(p.Parameters, "LocalTWR", new[] { "LOCALTWR" });
            AttachAliasesToParam(p.Parameters, "ThrottleTWR", new[] { "THROTTLETWR" });
            AttachAliasesToParam(p.Parameters, "NextManeuverNodeDeltaV", new[] { "NODEDV" });
            AttachAliasesToParam(p.Parameters, "TimeToManeuverNode", new[] { "TIMETONODE" });
            AttachAliasesToParam(p.Parameters, "NextManeuverNodeBurnTime", new[] { "NODEBURNTIME" });
            AttachAliasesToParam(p.Parameters, "GetCoordinateString", new[] { "COORDINATES" });
            AttachAliasesToParam(p.Parameters, "CurrentOrbitSummary", new[] { "ORBITSUMMARY" });
            AttachAliasesToParam(p.Parameters, "TargetOrbitSummary", new[] { "TARGETORBITSUMMARY" });

            return p;
        }
        #endregion

        #region 4. PrincipiaProbe (PRINCIPIA)
        private ProbeDefinitionModel BuildPrincipiaProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "PRINCIPIA",
                ProbeClassName = "PrincipiaProbe",
                DisplayName = "Principia (N-body Gravitation)",
                TargetMod = "Principia",
                TargetAssembly = "principia.dll / principia.ksp_plugin_adapter.dll",
                Category = "N体摄动天体力学与非开普勒制导",
                Description = "基于辛积分器的高保真 N 体引力解算引擎，支持拉格朗日点、低推力轨道、天体质心与动力学参考系投影",
                ModTagAliases = new List<string> { "PRINCIPIA", "PRIN", "PRINCIA" }
            };

            var asm = TryLoadAssembly(@"Principia\ksp_plugin_adapter.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                var adapterType = FindTypeInAssembly(asm, "principia.ksp_plugin_adapter.PrincipiaPluginAdapter");
                ExtractPublicMembersFromType(adapterType, "PRINCIPIA", p.Parameters, "Principia 适配器", false, true);
            }

            // Principia 核心封装参数
            string[] items = new[]
            {
                "FrameName|当前选定绘图参考系的正式全名 (如 Earth-Moon Barycentre)|System.String||PlottingFrame",
                "NavballFrameName|当前导航球所同步的 Principia 参考系名称|System.String||PlottingFrame",
                "IsSurfaceFrame|当前参考系是否为天体表面固联固连系 (ECEF / Body-Fixed)|System.Boolean|bool (0/1)|PlottingFrame",
                "HasTargetFrame|当前是否选定了目标物体相对绘图参考系|System.Boolean|bool (0/1)|PlottingFrame",
                "FrameType|参考系几何构型枚举代号|System.Int32||PlottingFrame",
                "FrameCenterName|当前参考系几何中心天体或目标名称|System.String||PlottingFrame",
                "FramePrimaryName|两体质心参考系中主天体的名称|System.String||PlottingFrame",
                "HasFlightPlan|当前载具是否存在有效的 Principia 飞行机动计划|System.Boolean|bool (0/1)|FlightPlanner",
                "FlightPlanManoeuvreCount|飞行计划中包含的机动点燃烧段总数量|System.Int32||FlightPlanner",
                "FlightPlanDeltaV|下一个计划机动点所需精确速度增量 Delta-V|System.Double|m/s|FlightPlanner",
                "FlightPlanBurnDuration|下一个计划机动点推力段持续燃烧时间|System.Double|s|FlightPlanner",
                "FlightPlanInitialTime|机动开始点时刻 (UT)|System.Double|s|FlightPlanner",
                "FlightPlanFinalTime|机动结束点时刻 (UT)|System.Double|s|FlightPlanner",
                "SpeedInPlottingFrame|飞船在当前绘图参考系下的精确表观合成速度大小|System.Double|m/s|Kinematics",
                "VelocityX|飞船在绘图参考系中的 X 轴线速度分量|System.Double|m/s|Kinematics",
                "VelocityY|飞船在绘图参考系中的 Y 轴线速度分量|System.Double|m/s|Kinematics",
                "VelocityZ|飞船在绘图参考系中的 Z 轴线速度分量|System.Double|m/s|Kinematics",
                "PlottingFrameDistanceToCenter|飞船距离绘图参考系中心天体/质心的直线距离|System.Double|m|Kinematics",
                "OrbitDescription|非开普勒摄动轨道文字特征综合描述|System.String||OrbitAnalyser",
                "AnalysisPeriod|摄动轨道分析解算得出的近似回归周期|System.Double|s|OrbitAnalyser",
                "AnalysisApoapsis|摄动轨道在选定参考系下的最高远地点|System.Double|m|OrbitAnalyser",
                "AnalysisPeriapsis|摄动轨道在选定参考系下的最低近地点|System.Double|m|OrbitAnalyser",
                "AnalysisInclination|摄动轨道在选定参考系下的参考轨道倾角|System.Double|deg|OrbitAnalyser",
                "AnalysisNodalPrecession|非球形引力摄动造成的升交点轨道进动速率|System.Double|deg/d|OrbitAnalyser"
            };

            foreach (var item in items)
            {
                var parts = item.Split('|');
                AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
                {
                    Name = parts[0],
                    Token = $"{{PRINCIPIA:{parts[0]}}}",
                    Type = parts[2],
                    Unit = parts[3],
                    AccessType = "PrincipiaAdapterBridge",
                    SourceType = parts[4],
                    DescriptionZh = parts[1],
                    DescriptionEn = $"Principia {parts[0]}",
                    Aliases = new List<string>(),
                    SubModifiers = InferSubModifiers(parts[2])
                });
            }

            return p;
        }
        #endregion

        #region 5. KerbalismProbe (KERBALISM)
        private ProbeDefinitionModel BuildKerbalismProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "KERBALISM",
                ProbeClassName = "KerbalismProbe",
                DisplayName = "Kerbalism (Life Support & Radiation)",
                TargetMod = "Kerbalism",
                TargetAssembly = "Kerbalism.dll",
                Category = "环境生命维持、辐射与乘员生理",
                Description = "KSP 最核心的拟真生存大修模组，提供空间辐射通量、辐射带、栖息舱空间、心理压力与生命维持日历",
                ModTagAliases = new List<string> { "KERBALISM", "KLSM" }
            };

            var asm = TryLoadAssembly(@"Kerbalism\Kerbalism.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KERBALISM.VesselData"), "KERBALISM", p.Parameters, "VesselData 载具状态", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KERBALISM.ConnectionInfo"), "KERBALISM", p.Parameters, "ConnectionInfo 链路", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KERBALISM.Features"), "KERBALISM", p.Parameters, "Features 功能开关", true, false);
            }

            // 核心别名与常用指标
            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "RadiationRate",
                Token = "{KERBALISM:RadiationRate}",
                Type = "System.Double",
                Unit = "rad/h",
                AccessType = "TraversedProperty",
                SourceType = "VesselData",
                DescriptionZh = "当前所受宇宙射线与范艾伦辐射带综合辐射剂量率 (rad/h)",
                DescriptionEn = "Instantaneous cosmic and belt radiation exposure rate (rad/h)",
                Aliases = new List<string> { "RAD", "RADIATION" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "HabitatVolume",
                Token = "{KERBALISM:HabitatVolume}",
                Type = "System.Double",
                Unit = "m³",
                AccessType = "TraversedProperty",
                SourceType = "VesselData",
                DescriptionZh = "乘员有效加压生存生活舱内部容积 (m³)",
                DescriptionEn = "Crew pressurized living space habitat volume (m³)",
                Aliases = new List<string> { "HAB", "VOLUME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ShieldingRatio",
                Token = "{KERBALISM:ShieldingRatio}",
                Type = "System.Double",
                Unit = "0..1",
                AccessType = "TraversedProperty",
                SourceType = "VesselData",
                DescriptionZh = "飞船防辐射主动/被动防护盾装甲覆盖比率",
                DescriptionEn = "Radiation shielding material coverage ratio (0..1)",
                Aliases = new List<string> { "SHIELD", "SHIELDING" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "LifeSupportDays",
                Token = "{KERBALISM:LifeSupportDays}",
                Type = "System.Double",
                Unit = "days",
                AccessType = "CustomSynthetic",
                SourceType = "KerbalismProbe",
                DescriptionZh = "现有氧气、水与食物消耗速度下剩余可维持生存天数",
                DescriptionEn = "Estimated remaining life support supply days",
                Aliases = new List<string> { "LSDAYS", "SUPPLYDAYS" }
            });

            return p;
        }
        #endregion

        #region 6. RealAntennasProbe (RA)
        private ProbeDefinitionModel BuildRealAntennasProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "RA",
                ProbeClassName = "RealAntennasProbe",
                DisplayName = "RealAntennas (Realistic RF Communications)",
                TargetMod = "RealAntennas",
                TargetAssembly = "RealAntennas.dll",
                Category = "深空射频通信与天线链路",
                Description = "基于香农信道容量与自由空间路径损耗的拟真通信系统，提供信噪比信噪比裕度、载频波段、带宽与误码率",
                ModTagAliases = new List<string> { "RA", "REALANTENNAS", "REALANTENNA" }
            };

            var asm = TryLoadAssembly(@"RealAntennas\Plugins\RealAntennas.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealAntennas.Physics"), "RA", p.Parameters, "射频物理常量", true, false);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealAntennas.RACommNetVessel"), "RA", p.Parameters, "载具通信中枢", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealAntennas.RACommNode"), "RA", p.Parameters, "通信节点", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealAntennas.RealAntenna"), "RA", p.Parameters, "活动天线", false, true);
            }

            // 核心别名与指标
            AttachAliasesToParam(p.Parameters, "Rate", new[] { "RATE", "DATARATE" });
            AttachAliasesToParam(p.Parameters, "TxPower", new[] { "TXPOWER", "POWER" });
            AttachAliasesToParam(p.Parameters, "BestMargin", new[] { "MARGIN", "SNRMULT" });
            AttachAliasesToParam(p.Parameters, "LinkCount", new[] { "LINKCOUNT", "LINKS" });
            AttachAliasesToParam(p.Parameters, "BestAntennaName", new[] { "ANTENNA", "BESTANT" });
            AttachAliasesToParam(p.Parameters, "IsConnectedHome", new[] { "HOME", "GROUNDLINK" });

            return p;
        }
        #endregion

        #region 7. TrajectoriesProbe (TRAJ)
        private ProbeDefinitionModel BuildTrajectoriesProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "TRAJ",
                ProbeClassName = "TrajectoriesProbe",
                DisplayName = "Trajectories (Atmospheric Entry & Impact Prediction)",
                TargetMod = "Trajectories",
                TargetAssembly = "Trajectories.dll",
                Category = "大气再入减速与落点预测",
                Description = "高精度模拟大气阻力与行星自转的气动弹道数值积分器，提供再入落点、撞击经纬度与着陆误差分析",
                ModTagAliases = new List<string> { "TRAJ", "TRAJECTORIES" }
            };

            var asm = TryLoadAssembly(@"Trajectories\Plugins\Trajectories.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "Trajectories.API"), "TRAJ", p.Parameters, "Trajectories API", true, false);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactTime",
                Token = "{TRAJ:ImpactTime}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "TraversedMethod",
                SourceType = "Trajectories.API",
                DescriptionZh = "距离大气再入落地或撞击地表预计剩余秒数 (s)",
                DescriptionEn = "Seconds remaining until atmospheric impact or landing",
                Aliases = new List<string> { "IMPACTTIME", "TIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactAltitude",
                Token = "{TRAJ:ImpactAltitude}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "CustomSynthetic",
                SourceType = "TrajectoriesProbe",
                DescriptionZh = "预测落点所在的地表绝对真实海拔高度 (m)",
                DescriptionEn = "True surface terrain altitude at predicted impact site",
                Aliases = new List<string> { "IMPACTALT" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactVelocity",
                Token = "{TRAJ:ImpactVelocity}",
                Type = "UnityEngine.Vector3",
                Unit = "m/s",
                AccessType = "TraversedMethod",
                SourceType = "Trajectories.API",
                DescriptionZh = "触地瞬间预测的三维撞击矢量速度 (m/s)",
                DescriptionEn = "3D predicted impact velocity vector at touchdown",
                Aliases = new List<string> { "IMPACTVEL" },
                SubModifiers = new List<string> { ":X", ":Y", ":Z", ":MAG" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TargetDistance",
                Token = "{TRAJ:TargetDistance}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "CustomSynthetic",
                SourceType = "TrajectoriesProbe",
                DescriptionZh = "预计触地点与玩家指定地面着陆靶点之间的直线偏差距离 (m)",
                DescriptionEn = "Deviation distance from predicted impact to landing target",
                Aliases = new List<string> { "TARGETDIST", "DIST" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactLatitude",
                Token = "{TRAJ:ImpactLatitude}",
                Type = "System.Double",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "TrajectoriesProbe",
                DescriptionZh = "预测着陆或撞击点所在的真实地理纬度 (deg)",
                DescriptionEn = "Predicted geographic impact latitude (deg)",
                Aliases = new List<string> { "IMPACTLAT", "LAT" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ImpactLongitude",
                Token = "{TRAJ:ImpactLongitude}",
                Type = "System.Double",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "TrajectoriesProbe",
                DescriptionZh = "预测着陆或撞击点所在的真实地理经度 (deg)",
                DescriptionEn = "Predicted geographic impact longitude (deg)",
                Aliases = new List<string> { "IMPACTLON", "LON" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "HasTarget",
                Token = "{TRAJ:HasTarget}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "TrajectoriesProbe",
                DescriptionZh = "当前是否选定了地面目标着陆靶点",
                DescriptionEn = "Whether a specific landing destination target is designated",
                Aliases = new List<string> { "HASTARGET", "TARGET" }
            });

            return p;
        }
        #endregion

        #region 8. DockingAlignmentProbe (DOCK / DPAI)
        private ProbeDefinitionModel BuildDockingAlignmentProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "DOCK",
                ProbeClassName = "DockingAlignmentProbe",
                DisplayName = "Docking Port Alignment Indicator (DPAI)",
                TargetMod = "NavyFish DPAI",
                TargetAssembly = "DockingPortAlignmentIndicator.dll",
                Category = "空间交会与对接对准指示仪",
                Description = "NavyFish 航天对接引导指示仪，提供 CDI 横纵平移十字指针偏差、滚转角配准、相对距离与进近闭合速率",
                ModTagAliases = new List<string> { "DOCK", "DPAI", "NAVYFISH" }
            };

            var asm = TryLoadAssembly(@"DockingPortAlignmentIndicator\Plugins\DockingPortAlignmentIndicator.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "NavyFish.DPAI.DockingPortAlignmentIndicator"), "DOCK", p.Parameters, "DPAI 引导器", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "DevX",
                Token = "{DOCK:DevX}",
                Type = "System.Single",
                Unit = "-1..1",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "目标对接端口中心水平向 CDI 偏航平移偏差量 (-1..1)",
                DescriptionEn = "Horizontal cross-pointer CDI translation deviation (-1..1)",
                Aliases = new List<string> { "DEVX", "CDIX", "TRANSLATIONX" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "DevY",
                Token = "{DOCK:DevY}",
                Type = "System.Single",
                Unit = "-1..1",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "目标对接端口中心垂直向 CDI 偏航平移偏差量 (-1..1)",
                DescriptionEn = "Vertical cross-pointer CDI translation deviation (-1..1)",
                Aliases = new List<string> { "DEVY", "CDIY", "TRANSLATIONY" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "RollOffset",
                Token = "{DOCK:RollOffset}",
                Type = "System.Single",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "与目标对接端口对准轴所需的滚转配准相对角度差 (deg)",
                DescriptionEn = "Roll orientation angular offset required for docking lock",
                Aliases = new List<string> { "ROLLOFFSET", "ROLL" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "PitchDev",
                Token = "{DOCK:PitchDev}",
                Type = "System.Single",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "与目标对接口朝向的姿态俯仰角对准偏差 (deg)",
                DescriptionEn = "Pitch angular alignment orientation deviation (deg)",
                Aliases = new List<string> { "PITCHDEV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "YawDev",
                Token = "{DOCK:YawDev}",
                Type = "System.Single",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "与目标对接口朝向的姿态偏航角对准偏差 (deg)",
                DescriptionEn = "Yaw angular alignment orientation deviation (deg)",
                Aliases = new List<string> { "YAWDEV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Distance",
                Token = "{DOCK:Distance}",
                Type = "System.Single",
                Unit = "m",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "当前飞船对准端口与目标对接口之间的直线表面净距离 (m)",
                DescriptionEn = "Net straight-line distance between docking port surfaces",
                Aliases = new List<string> { "DISTANCE", "DIST" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "ClosureRate",
                Token = "{DOCK:ClosureRate}",
                Type = "System.Single",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "对接端口进近闭合相对线速度 (m/s，正为逼近，负为远离)",
                DescriptionEn = "Relative approach closure rate along docking axis",
                Aliases = new List<string> { "CLOSURERATE", "CLOSUREV" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TargetName",
                Token = "{DOCK:TargetName}",
                Type = "System.String",
                Unit = "",
                AccessType = "CustomSynthetic",
                SourceType = "DockingAlignmentProbe",
                DescriptionZh = "当前选定对准的目标对接端口部件全名",
                DescriptionEn = "Display name of selected target docking port part",
                Aliases = new List<string> { "TARGETNAME", "TARGETPORT" }
            });

            return p;
        }
        #endregion

        #region 9. GPWSProbe (GPWS)
        private ProbeDefinitionModel BuildGPWSProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "GPWS",
                ProbeClassName = "GPWSProbe",
                DisplayName = "Ground Proximity Warning System (GPWS / TAWS)",
                TargetMod = "KSP GPWS",
                TargetAssembly = "GPWS.dll",
                Category = "近地警告、地形感知与起降基准",
                Description = "航空标准近地告警与防撞地安全系统，提供雷达真高、急剧下沉率、V1/Vr/Vref 起降决断速度与失速临界攻角",
                ModTagAliases = new List<string> { "GPWS", "TAWS" }
            };

            var asm = TryLoadAssembly(@"GPWS\Plugins\GPWS.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "KSP_GPWS.Gpws"), "GPWS", p.Parameters, "GPWS 告警中枢", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "RadarAltitude",
                Token = "{GPWS:RadarAltitude}",
                Type = "System.Double",
                Unit = "m",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "无线电测距雷达测得的机腹对地真实地表净高度 (m)",
                DescriptionEn = "Radio altimeter true terrain clearance height AGL (m)",
                Aliases = new List<string> { "RADARALT", "RADAR", "RADIOALT" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "SinkRate",
                Token = "{GPWS:SinkRate}",
                Type = "System.Double",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "即时垂直升降率与下沉速率 (m/s，下沉超标触发 PULL UP 告警)",
                DescriptionEn = "Instantaneous vertical sink rate (triggers PULL UP on excessive sink)",
                Aliases = new List<string> { "SINKRATE", "VERSPEED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "V1Speed",
                Token = "{GPWS:V1Speed}",
                Type = "System.Single",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "起飞滑跑决断速度 V1：超过该速度不可中断起飞，必须拉起升空",
                DescriptionEn = "Takeoff decision speed V1: beyond this takeoff must continue",
                Aliases = new List<string> { "V1", "V1SPEED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TakeOffSpeed",
                Token = "{GPWS:TakeOffSpeed}",
                Type = "System.Single",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "起飞抬前轮基准表速 Vr (m/s)",
                DescriptionEn = "Rotation speed Vr for raising nose gear",
                Aliases = new List<string> { "VR", "TAKEOFFSPEED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "LandingSpeed",
                Token = "{GPWS:LandingSpeed}",
                Type = "System.Single",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "最终进近接地着陆基准参考表速 Vref (m/s)",
                DescriptionEn = "Final landing approach reference speed Vref",
                Aliases = new List<string> { "VREF", "LANDINGSPEED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "StallAoa",
                Token = "{GPWS:StallAoa}",
                Type = "System.Single",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "机翼气动升力峰值临界失速迎角门限 (deg)",
                DescriptionEn = "Aerodynamic critical stall angle of attack limit (deg)",
                Aliases = new List<string> { "STALLAOA" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "HorSpeed",
                Token = "{GPWS:HorSpeed}",
                Type = "System.Double",
                Unit = "m/s",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "对地真实水平地速分量 (m/s)",
                DescriptionEn = "Horizontal ground speed component (m/s)",
                Aliases = new List<string> { "HORSPEED", "GROUNDSPEED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "GearDown",
                Token = "{GPWS:GearDown}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "GPWSProbe",
                DescriptionZh = "起落架当前是否已完全放下锁定",
                DescriptionEn = "Landing gear fully extended and locked status",
                Aliases = new List<string> { "GEARDOWN", "GEAR" }
            });

            return p;
        }
        #endregion

        #region 10. RealFuelsProbe (RF)
        private ProbeDefinitionModel BuildRealFuelsProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "RF",
                ProbeClassName = "RealFuelsProbe",
                DisplayName = "RealFuels (Propulsion & Ullage Physics)",
                TargetMod = "RealFuels",
                TargetAssembly = "RealFuels.dll",
                Category = "真实推进剂化学、沉底与点火重启",
                Description = "拟真火箭推进剂物理，包含储箱推进剂沉底状态 (Ullage)、发动机有限点火重启次数与混合比配置",
                ModTagAliases = new List<string> { "RF", "REALFUELS", "REALFUEL" }
            };

            var asm = TryLoadAssembly(@"RealFuels\Plugins\RealFuels.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealFuels.ModuleEngineConfigs"), "RF", p.Parameters, "发动机配置", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RealFuels.Ullage.UllageModule"), "RF", p.Parameters, "沉底模块", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Ignitions",
                Token = "{RF:Ignitions}",
                Type = "System.Int32",
                Unit = "count",
                AccessType = "CustomSynthetic",
                SourceType = "RealFuelsProbe",
                DescriptionZh = "当前处于工作状态的发动机剩余可点火重启次数 (-1 代表支持无限次重启)",
                DescriptionEn = "Remaining ignitions for active engine (-1 for unlimited)",
                Aliases = new List<string> { "IGNITIONS", "IGNITIONSLEFT", "IGNCOUNT" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Ullage",
                Token = "{RF:Ullage}",
                Type = "System.String",
                Unit = "state",
                AccessType = "CustomSynthetic",
                SourceType = "RealFuelsProbe",
                DescriptionZh = "推进剂在储箱底部的沉底贴紧状态 (Very Stable / Stable / Unstable / Dispersed)",
                DescriptionEn = "Propellant settling status in fuel tank (Stable / Unstable etc.)",
                Aliases = new List<string> { "ULLAGE", "ULLAGESTATE", "STATUS" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "UllageStability",
                Token = "{RF:UllageStability}",
                Type = "System.Double",
                Unit = "0..1",
                AccessType = "CustomSynthetic",
                SourceType = "RealFuelsProbe",
                DescriptionZh = "储箱推进剂沉底贴底程度的量化百分比稳定度 (0..1)",
                DescriptionEn = "Quantified propellant settling stability percentage (0..1)",
                Aliases = new List<string> { "ULLAGESTABILITY", "STABILITY" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "EngineConfig",
                Token = "{RF:EngineConfig}",
                Type = "System.String",
                Unit = "",
                AccessType = "CustomSynthetic",
                SourceType = "RealFuelsProbe",
                DescriptionZh = "当前活动的火箭发动机所选用的硬件改型配置名称 (如 LR87-AJ-5)",
                DescriptionEn = "Active rocket engine hardware configuration model name",
                Aliases = new List<string> { "CONFIG", "ENGINECONFIG", "CONFIGNAME" }
            });

            return p;
        }
        #endregion

        #region 11. TestFlightProbe (TF)
        private ProbeDefinitionModel BuildTestFlightProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "TF",
                ProbeClassName = "TestFlightProbe",
                DisplayName = "TestFlight (Engine Reliability & Failure Simulation)",
                TargetMod = "TestFlight",
                TargetAssembly = "TestFlightCore.dll",
                Category = "发动机可靠性、疲劳燃时与故障注入",
                Description = "RO/RP-1 核心可靠性模拟器，模拟发动机点火失败、燃烧室过热、涡轮泵卡死、工作燃时与飞行数据积累 (DU)",
                ModTagAliases = new List<string> { "TF", "TESTFLIGHT" }
            };

            var asm = TryLoadAssembly(@"TestFlight\Plugins\TestFlightCore.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "TestFlightCore.TestFlightCore"), "TF", p.Parameters, "TestFlight 核心", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "OperatingTime",
                Token = "{TF:OperatingTime}",
                Type = "System.Single",
                Unit = "s",
                AccessType = "CustomSynthetic",
                SourceType = "TestFlightProbe",
                DescriptionZh = "当前引擎本次点火点火后累计持续工作的实际物理秒数 (s)",
                DescriptionEn = "Current ignition engine accumulated continuous burn time (s)",
                Aliases = new List<string> { "OPERATINGTIME", "BURNTIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "FailureRate",
                Token = "{TF:FailureRate}",
                Type = "System.Double",
                Unit = "prob/s",
                AccessType = "CustomSynthetic",
                SourceType = "TestFlightProbe",
                DescriptionZh = "当前燃烧工况与累计燃时下引擎每秒发生灾难性故障的基础失效率",
                DescriptionEn = "Instantaneous failure probability rate under current burn conditions",
                Aliases = new List<string> { "FAILURERATE", "FAILCHANCE", "FAILRATE" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "FlightData",
                Token = "{TF:FlightData}",
                Type = "System.Single",
                Unit = "DU",
                AccessType = "CustomSynthetic",
                SourceType = "TestFlightProbe",
                DescriptionZh = "该型号发动机通过地面试车与实际飞行累计沉淀的技术成熟度数据点 (DU)",
                DescriptionEn = "Accumulated flight test research data units (DU)",
                Aliases = new List<string> { "FLIGHTDATA", "DATA", "DU" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Status",
                Token = "{TF:Status}",
                Type = "System.String",
                Unit = "",
                AccessType = "CustomSynthetic",
                SourceType = "TestFlightProbe",
                DescriptionZh = "发动机运行健康状况 (NOMINAL 标称正常，或具体故障如 Partial Thrust Loss)",
                DescriptionEn = "Engine health status (NOMINAL or specific active failure name)",
                Aliases = new List<string> { "STATUS", "HEALTH", "FAILURETYPE" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Failed",
                Token = "{TF:Failed}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "TestFlightProbe",
                DescriptionZh = "发动机当前是否发生了任何致命或性能受损故障",
                DescriptionEn = "Whether the engine has suffered any active operational failure",
                Aliases = new List<string> { "FAILED", "ISFAILED", "HASFAILURE" }
            });

            return p;
        }
        #endregion

        #region 12. DynamicBatteryStorageProbe (DBS)
        private ProbeDefinitionModel BuildDynamicBatteryStorageProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "DBS",
                ProbeClassName = "DynamicBatteryStorageProbe",
                DisplayName = "Dynamic Battery Storage (DBS)",
                TargetMod = "DynamicBatteryStorage",
                TargetAssembly = "DynamicBatteryStorage.dll",
                Category = "全舰电气电网平衡与充放电预测",
                Description = "精准计算飞船太阳能、燃料电池、核反应堆发电量与天线、飞轮、生命维持耗电量的能源平衡监控器",
                ModTagAliases = new List<string> { "DBS", "DYNAMICBATTERYSTORAGE" }
            };

            var asm = TryLoadAssembly(@"DynamicBatteryStorage\DynamicBatteryStorage.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "DynamicBatteryStorage.VesselElectricalData"), "DBS", p.Parameters, "电气数据", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "NetRate",
                Token = "{DBS:NetRate}",
                Type = "System.Double",
                Unit = "EC/s",
                AccessType = "CustomSynthetic",
                SourceType = "DynamicBatteryStorageProbe",
                DescriptionZh = "全舰电网即时净充放电差额速率 (EC/s，正值为蓄电池充电，负值为透支放电)",
                DescriptionEn = "Net electrical flow rate (EC/s, positive=charging, negative=discharging)",
                Aliases = new List<string> { "NETRATE", "NET", "FLOW" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TimeToDepletionSeconds",
                Token = "{DBS:TimeToDepletionSeconds}",
                Type = "System.Double",
                Unit = "s",
                AccessType = "CustomSynthetic",
                SourceType = "DynamicBatteryStorageProbe",
                DescriptionZh = "在当前亏电放电负荷下，全舰剩余储能电池彻底耗尽预计所需秒数",
                DescriptionEn = "Seconds remaining until onboard battery storage is depleted",
                Aliases = new List<string> { "DEPLETIONSECONDS", "TIMELEFTSEC" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TimeToDepletion",
                Token = "{DBS:TimeToDepletion}",
                Type = "System.String",
                Unit = "HH:MM:SS",
                AccessType = "CustomSynthetic",
                SourceType = "DynamicBatteryStorageProbe",
                DescriptionZh = "电池彻底断电倒计时的工程格式化文本 (如 02:45:12 或 INFINITE)",
                DescriptionEn = "Formatted countdown string until battery depletion (HH:MM:SS)",
                Aliases = new List<string> { "TIMETODEPLETION", "DEPLETION", "DRAINTIME" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "IsDepleting",
                Token = "{DBS:IsDepleting}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "DynamicBatteryStorageProbe",
                DescriptionZh = "全舰当前是否正处于总耗电大于总发电的持续亏电消耗状态",
                DescriptionEn = "Whether the ship is currently running a net electrical deficit",
                Aliases = new List<string> { "ISDEPLETING", "DRAINING" }
            });

            return p;
        }
        #endregion

        #region 13. SystemHeatProbe (SH)
        private ProbeDefinitionModel BuildSystemHeatProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "SH",
                ProbeClassName = "SystemHeatProbe",
                DisplayName = "SystemHeat (Closed-loop Thermal Management)",
                TargetMod = "SystemHeat",
                TargetAssembly = "SystemHeat.dll",
                Category = "闭式冷却液热循环与废热消纳",
                Description = "Nertea 的高级热力循环大修，模拟反应堆、核电推进、散热器与热交换回路的流体温度与热通量",
                ModTagAliases = new List<string> { "SH", "SYSTEMHEAT" }
            };

            var asm = TryLoadAssembly(@"SystemHeat\SystemHeat.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "SystemHeat.SystemHeatSimulator"), "SH", p.Parameters, "热力仿真机", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "SystemHeat.HeatLoop"), "SH", p.Parameters, "热回路", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "PrimaryLoopTemp",
                Token = "{SH:PrimaryLoopTemp}",
                Type = "System.Single",
                Unit = "K",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "主冷却热力循环回路当前的实时工质冷却液温度 (K)",
                DescriptionEn = "Primary heat loop active coolant temperature (K)",
                Aliases = new List<string> { "LOOP_TEMP", "TEMP", "PRIMARYTEMP" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "PrimaryLoopNominalTemp",
                Token = "{SH:PrimaryLoopNominalTemp}",
                Type = "System.Single",
                Unit = "K",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "主热回路标称最佳工作设计温度门限 (K)",
                DescriptionEn = "Primary heat loop nominal design operating temperature (K)",
                Aliases = new List<string> { "LOOP_NOM_TEMP", "NOMTEMP" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "NetFluxKw",
                Token = "{SH:NetFluxKw}",
                Type = "System.Single",
                Unit = "kW",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "主回路即时净热通量差额 (kW，产热减排热，正为回路升温，负为散热降温)",
                DescriptionEn = "Primary heat loop net thermal flux in kilowatts (kW)",
                Aliases = new List<string> { "NET_FLUX", "NETFLUX", "FLUX" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TotalGenerationKw",
                Token = "{SH:TotalGenerationKw}",
                Type = "System.Single",
                Unit = "kW",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "全舰所有发热部件（反应堆、发动机、电子设备）产生的总废热功率 (kW)",
                DescriptionEn = "Total vessel waste heat generation across all parts (kW)",
                Aliases = new List<string> { "TOTAL_GEN", "HEATGEN" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "TotalRejectionKw",
                Token = "{SH:TotalRejectionKw}",
                Type = "System.Single",
                Unit = "kW",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "全舰所有活动散热片向宇宙空间辐射排出的总散热功率 (kW)",
                DescriptionEn = "Total vessel active radiator waste heat rejection power (kW)",
                Aliases = new List<string> { "TOTAL_REJ", "HEATREJ" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "PrimaryLoopOverheating",
                Token = "{SH:PrimaryLoopOverheating}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "主冷却热力循环回路当前是否已发生超温过热报警",
                DescriptionEn = "Whether primary heat loop temperature exceeds nominal design limits",
                Aliases = new List<string> { "OVERHEAT", "ISOVERHEATING" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "PrimaryLoopOverheatRatio",
                Token = "{SH:PrimaryLoopOverheatRatio}",
                Type = "System.Single",
                Unit = "0..1",
                AccessType = "CustomSynthetic",
                SourceType = "SystemHeatProbe",
                DescriptionZh = "主热回路工质温度超过额定标称温度的过热比例 (0..1)",
                DescriptionEn = "Primary heat loop temperature exceedance ratio relative to nominal",
                Aliases = new List<string> { "OVERHEAT_RATIO", "OVERHEATRATIO" }
            });

            return p;
        }
        #endregion

        #region 14. AtmosphereAutopilotProbe (AA)
        private ProbeDefinitionModel BuildAtmosphereAutopilotProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "AA",
                ProbeClassName = "AtmosphereAutopilotProbe",
                DisplayName = "Atmosphere Autopilot (AA Fly-By-Wire)",
                TargetMod = "AtmosphereAutopilot",
                TargetAssembly = "AtmosphereAutopilot.dll",
                Category = "现代电传飞控、过载保护与巡航自驾",
                Description = "基于现代控制理论的飞机电传飞控 (FBW)，提供迎角限制器、G值过载保护、自动配平与高空巡航保持",
                ModTagAliases = new List<string> { "AA", "ATMOSPHEREAUTOPILOT" }
            };

            var asm = TryLoadAssembly(@"AtmosphereAutopilot\AtmosphereAutopilot.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "AtmosphereAutopilot.FlightModel"), "AA", p.Parameters, "气动模型", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "AtmosphereAutopilot.TopModuleManager"), "AA", p.Parameters, "飞控管理器", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "AtmosphereAutopilot.PitchAngularVelocityController"), "AA", p.Parameters, "过载保护器", false, true);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "AtmosphereAutopilot.CruiseController"), "AA", p.Parameters, "巡航控制器", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "MASTER_SWITCH",
                Token = "{AA:MASTER_SWITCH}",
                Type = "System.Double",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "Atmosphere Autopilot 顶层电传飞控总开关激活状态",
                DescriptionEn = "AA master fly-by-wire flight control activation state",
                Aliases = new List<string> { "MASTER", "ENABLED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "CRUISE_ACTIVE",
                Token = "{AA:CRUISE_ACTIVE}",
                Type = "System.Double",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "巡航自动驾驶仪（高度、航向、空速保持）启用状态",
                DescriptionEn = "Cruise controller hold autopilot active status",
                Aliases = new List<string> { "CRUISE" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "CURRENT_AOA",
                Token = "{AA:CURRENT_AOA}",
                Type = "System.Double",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "AA 内部传感器精确解算出的机体瞬时气动迎角攻角 (deg)",
                DescriptionEn = "AA internal dynamic flight model filtered Angle of Attack (deg)",
                Aliases = new List<string> { "AOA" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "MAX_AOA",
                Token = "{AA:MAX_AOA}",
                Type = "System.Double",
                Unit = "deg",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "电传飞控当前工况下允许的最大上仰迎角保护门限 (deg)",
                DescriptionEn = "FBW limiter maximum allowed positive Angle of Attack threshold (deg)",
                Aliases = new List<string> { "MAX_AOA" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "CURRENT_GLOAD",
                Token = "{AA:CURRENT_GLOAD}",
                Type = "System.Double",
                Unit = "G",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "机体垂直方向实际承受的法向加速度过载 (G)",
                DescriptionEn = "Current airframe normal structural G-load (G)",
                Aliases = new List<string> { "GLOAD", "G_FORCE" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "MAX_GLOAD",
                Token = "{AA:MAX_GLOAD}",
                Type = "System.Double",
                Unit = "G",
                AccessType = "CustomSynthetic",
                SourceType = "AtmosphereAutopilotProbe",
                DescriptionZh = "电传飞控机体结构过载保护器设定的最大允许安全过载限度 (G)",
                DescriptionEn = "FBW limiter structural safety maximum normal G-load limit (G)",
                Aliases = new List<string> { "MAX_GLOAD" }
            });

            return p;
        }
        #endregion

        #region 15. RP1AvionicsProbe (RP1)
        private ProbeDefinitionModel BuildRP1AvionicsProbe()
        {
            var p = new ProbeDefinitionModel
            {
                ProbeId = "RP1",
                ProbeClassName = "RP1AvionicsProbe",
                DisplayName = "RP-1 Avionics (Tonnage Limit & Control Locking)",
                TargetMod = "RP-1 (Realistic Progression One)",
                TargetAssembly = "RP0.dll",
                Category = "RP-1 真实航电系统、控制吨位与功耗",
                Description = "拟真太空竞赛 RP-1 核心机制，模拟航电控制吨位限制、超重姿态锁定、轴向失控、深空星际控制门限与待机瓦特功耗",
                ModTagAliases = new List<string> { "RP1", "RP0", "AVIONICS" }
            };

            var asm = TryLoadAssembly(@"RP-1\Plugins\CC_RP0.dll");
            if (asm == null) asm = TryLoadAssembly(@"RP-1\Plugins\RP0.dll");
            p.IsAssemblyPresent = asm != null;

            if (asm != null)
            {
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RP0.ControlLockerUtils"), "RP1", p.Parameters, "航电锁控中枢", true, false);
                ExtractPublicMembersFromType(FindTypeInAssembly(asm, "RP0.ModuleAvionics"), "RP1", p.Parameters, "航电部件模块", false, true);
            }

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "LockLevel",
                Token = "{RP1:LockLevel}",
                Type = "System.Int32",
                Unit = "enum (0..2)",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "航电控制受限等级：2=完全解锁可全控，1=仅轴向受控无法转向，0=完全失控锁死",
                DescriptionEn = "Avionics control lock level: 2=Unlocked, 1=Axial only, 0=Locked out",
                Aliases = new List<string> { "LOCKLEVEL", "CONTROLLEVEL" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "IsLocked",
                Token = "{RP1:IsLocked}",
                Type = "System.Boolean",
                Unit = "bool (0/1)",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "飞船当前是否因航电能力不足或超重而彻底失去姿态控制能力",
                DescriptionEn = "Whether the vessel has completely lost attitude control due to insufficient avionics",
                Aliases = new List<string> { "ISLOCKED", "LOCKED" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "MaxMass",
                Token = "{RP1:MaxMass}",
                Type = "System.Single",
                Unit = "t",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "当前全舰安装并激活的航电模块允许控制的飞船最大总质量吨位上限 (t)",
                DescriptionEn = "Maximum vessel mass capacity controllable by active avionics (tons)",
                Aliases = new List<string> { "MAXMASS", "TONNAGE", "CAPACITY" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "VesselMass",
                Token = "{RP1:VesselMass}",
                Type = "System.Single",
                Unit = "t",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "飞船当前实际结构与推进剂实时总质量 (t)",
                DescriptionEn = "Actual current total wet mass of the vessel (tons)",
                Aliases = new List<string> { "VESSELMASS", "MASS" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "MassUtilization",
                Token = "{RP1:MassUtilization}",
                Type = "System.Single",
                Unit = "0..1",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "航电吨位承载利用率 (VesselMass / MaxMass，超过 1.0 即触发展开锁定)",
                DescriptionEn = "Avionics capacity utilization ratio (locks out when > 1.0)",
                Aliases = new List<string> { "UTILIZATION", "MASSRATIO" }
            });

            AddOrUpdateParam(p.Parameters, new TelemetryParameterModel
            {
                Name = "Watts",
                Token = "{RP1:Watts}",
                Type = "System.Single",
                Unit = "W",
                AccessType = "CustomSynthetic",
                SourceType = "RP1AvionicsProbe",
                DescriptionZh = "全舰航电系统运行所需的基线持续功率电力功耗 (W)",
                DescriptionEn = "Total continuous electric power draw of avionics systems (Watts)",
                Aliases = new List<string> { "WATTS", "POWER", "DRAW" }
            });

            return p;
        }
        #endregion

        #region Common Parameter List Mutations

        private static void AddOrUpdateParam(List<TelemetryParameterModel> list, TelemetryParameterModel param)
        {
            int idx = list.FindIndex(p => p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                // 保留已有别名
                var existingAliases = list[idx].Aliases ?? new List<string>();
                foreach (var a in existingAliases)
                {
                    if (!param.Aliases.Contains(a, StringComparer.OrdinalIgnoreCase))
                        param.Aliases.Add(a);
                }
                list[idx] = param;
            }
            else
            {
                list.Add(param);
            }
        }

        private static void AttachAliasesToParam(List<TelemetryParameterModel> list, string paramName, string[] aliases)
        {
            var p = list.FirstOrDefault(item => item.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (p != null)
            {
                if (p.Aliases == null) p.Aliases = new List<string>();
                foreach (var a in aliases)
                {
                    if (!p.Aliases.Contains(a, StringComparer.OrdinalIgnoreCase))
                    {
                        p.Aliases.Add(a);
                    }
                }
            }
        }

        #endregion
    }
}
