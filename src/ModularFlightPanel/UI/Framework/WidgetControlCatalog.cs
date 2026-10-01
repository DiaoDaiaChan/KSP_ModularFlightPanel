using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ModularFlightPanel.Config;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Widgets;
using ModularFlightPanel.UI.Widgets.Controls;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 可装配微控件原型描述符 (Micro-Control Prototype Descriptor)
    /// 承载从现有 44+ 款组件或高频原生库中遍历出的积木构件元数据
    /// </summary>
    public class ControlPrototypeDescriptor
    {
        public string PrototypeId { get; set; }           // 唯一原型 ID (如 "core.bottom_controls.rcs", "native.readout")
        public string DisplayName { get; set; }           // 显示名称 (如 "RCS 姿控开关", "标准航电读数盒")
        public string Description { get; set; }           // 说明
        public string SourceWidgetTypeName { get; set; }  // 来源组件 TypeName (如 "core.bottom_controls", "native")
        public string SourceWidgetDisplayName { get; set; }// 来源组件显示名 (如 "RCS/REF/SAS 底控台", "官方高频构件")
        public WidgetControlCategory Category { get; set; } // 分类 (Readout, LinearGauge, Annunciator, ActionButton...)
        public Type ControlType { get; set; }

        public string DefaultToken { get; set; }
        public string DefaultTitle { get; set; }
        public string DefaultUnit { get; set; }
        public string DefaultUnitDimension { get; set; }
        public Vector2 DefaultSize { get; set; } = new Vector2(120f, 40f);
        public float DefaultOpacity { get; set; } = 1.0f;
        public double DefaultMinValue { get; set; } = 0.0;
        public double DefaultMaxValue { get; set; } = 100.0;
        public double DefaultCaution { get; set; } = 80.0;
        public double DefaultWarning { get; set; } = 95.0;
        public string DefaultAction { get; set; }
        public bool IsToggle { get; set; }
        public TextStyleRole TextRole { get; set; } = TextStyleRole.PrimaryValue;
        public MeterStyleRole MeterRole { get; set; } = MeterStyleRole.Primary;
    }

    /// <summary>
    /// 全量现有组件控件自动遍历、反射提取与构件目录中枢 (Widget Control Traversal & Catalog Hub)
    /// 核心架构职责：
    /// 1. 自动反射扫描 44+ 款已有飞行仪表组件内部所使用的全部微控件 (IWidgetDslControl / [WidgetControl])；
    /// 2. 运行时动态自省活跃实例的 Controls.All，无死角纳管所有动态装配的子控件；
    /// 3. 提供按「来源组件溯源」与按「功能类别分类」双重视图，供自由搭建工坊像 Photoshop 拾取素材一样任意组装！
    /// </summary>
    public static class WidgetControlCatalog
    {
        private static readonly List<ControlPrototypeDescriptor> _prototypes = new List<ControlPrototypeDescriptor>();
        private static readonly Dictionary<string, ControlPrototypeDescriptor> _prototypeMap = new Dictionary<string, ControlPrototypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<ControlPrototypeDescriptor>> _bySourceWidget = new Dictionary<string, List<ControlPrototypeDescriptor>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<WidgetControlCategory, List<ControlPrototypeDescriptor>> _byCategory = new Dictionary<WidgetControlCategory, List<ControlPrototypeDescriptor>>();
        private static readonly List<string> _sourceWidgetTypes = new List<string>();

        private static bool _isInitialized = false;

        public static IReadOnlyList<ControlPrototypeDescriptor> AllPrototypes
        {
            get
            {
                EnsureInitialized();
                return _prototypes;
            }
        }

        public static IReadOnlyList<string> AllSourceWidgetTypes
        {
            get
            {
                EnsureInitialized();
                return _sourceWidgetTypes;
            }
        }

        public static void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            TraverseAllExistingControls();
        }

        public static void ForceRefresh()
        {
            _isInitialized = false;
            EnsureInitialized();
        }

        public static IReadOnlyList<ControlPrototypeDescriptor> GetBySourceWidget(string sourceWidgetTypeName)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(sourceWidgetTypeName)) return _prototypes;
            if (_bySourceWidget.TryGetValue(sourceWidgetTypeName, out var list))
            {
                return list;
            }
            return Array.Empty<ControlPrototypeDescriptor>();
        }

        public static IReadOnlyList<ControlPrototypeDescriptor> GetByCategory(WidgetControlCategory category)
        {
            EnsureInitialized();
            if (_byCategory.TryGetValue(category, out var list))
            {
                return list;
            }
            return Array.Empty<ControlPrototypeDescriptor>();
        }

        public static ControlPrototypeDescriptor FindPrototype(string prototypeId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(prototypeId)) return null;
            if (_prototypeMap.TryGetValue(prototypeId, out var desc))
            {
                return desc;
            }
            return null;
        }

        /// <summary>
        /// 全量遍历扫描现有全部 44 款预制组件内部控件与高频原生构件
        /// </summary>
        public static void TraverseAllExistingControls()
        {
            _prototypes.Clear();
            _prototypeMap.Clear();
            _bySourceWidget.Clear();
            _byCategory.Clear();
            _sourceWidgetTypes.Clear();

            // 1. 注册核心原生高频航电积木构件 (Native High-Frequency Avionics Building Blocks)
            RegisterNativeAvionicsPrototypes();

            // 2. 静态反射遍历全量组件程序集 (Static Reflection Traversal)
            WidgetRegistry.EnsureInitialized();
            var descriptors = WidgetRegistry.AllDescriptors;

            for (int i = 0; i < descriptors.Count; i++)
            {
                var desc = descriptors[i];
                if (desc == null || desc.WidgetType == null) continue;

                TraverseWidgetType(desc);
            }

            // 3. 运行时自省动态补充 (Runtime Introspection)
            if (FlightHUDManager.Instance?.ModularWidgets != null)
            {
                var activeWidgets = FlightHUDManager.Instance.ModularWidgets;
                for (int i = 0; i < activeWidgets.Count; i++)
                {
                    var w = activeWidgets[i];
                    if (w == null || w.Controls == null) continue;
                    TraverseRuntimeWidget(w);
                }
            }

            // 4. 汇总所有来源组件清单
            foreach (var key in _bySourceWidget.Keys)
            {
                if (!_sourceWidgetTypes.Contains(key)) _sourceWidgetTypes.Add(key);
            }

            MFPLogger.Info("WidgetControlCatalog", $"Successfully traversed and indexed {_prototypes.Count} micro-controls across {_sourceWidgetTypes.Count} sources.");
        }

        private static void AddPrototype(ControlPrototypeDescriptor proto)
        {
            if (proto == null || string.IsNullOrEmpty(proto.PrototypeId)) return;
            if (_prototypeMap.ContainsKey(proto.PrototypeId)) return;

            _prototypes.Add(proto);
            _prototypeMap[proto.PrototypeId] = proto;

            string srcKey = proto.SourceWidgetTypeName ?? "native";
            if (!_bySourceWidget.TryGetValue(srcKey, out var srcList))
            {
                srcList = new List<ControlPrototypeDescriptor>();
                _bySourceWidget[srcKey] = srcList;
            }
            srcList.Add(proto);

            if (!_byCategory.TryGetValue(proto.Category, out var catList))
            {
                catList = new List<ControlPrototypeDescriptor>();
                _byCategory[proto.Category] = catList;
            }
            catList.Add(proto);
        }

        public static string ExtractCleanTitle(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "VALUE";
            string s = rawName;
            string[] suffixes = new string[] { "Control", "Widget", "Text", "Image", "Button", "Btn", "Label", "Value", "Bar", "Meter", "Toggle", "Indicator", "Item", "Box" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suf = suffixes[i];
                if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase) && s.Length > suf.Length)
                {
                    s = s.Substring(0, s.Length - suf.Length);
                    break;
                }
            }
            if (s.StartsWith("_")) s = s.TrimStart('_');
            if (s.StartsWith("m_")) s = s.Substring(2);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(char.ToUpperInvariant(s[i]));
            }
            string res = sb.ToString().Trim();
            return string.IsNullOrEmpty(res) ? "VALUE" : res;
        }

        private static void TraverseWidgetType(WidgetDescriptor desc)
        {
            Type t = desc.WidgetType;
            string widgetTypeName = desc.TypeName ?? t.Name;
            string widgetDisplayName = desc.DisplayName ?? widgetTypeName;

            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                // 1. 扫描 IWidgetDslControl 声明式对象 (如 TextWidget, LinearBarWidget, ToggleButtonWidget, ActionButtonWidget)
                if (typeof(IWidgetDslControl).IsAssignableFrom(f.FieldType))
                {
                    string protoId = $"{widgetTypeName}.{f.Name.ToLowerInvariant()}";
                    string protoName = $"{widgetDisplayName} - {f.Name}";
                    WidgetControlCategory cat = WidgetControlCategory.GenericElement;
                    Vector2 defSize = new Vector2(120f, 32f);
                    string defToken = "";
                    string defUnitDim = "";
                    bool isToggle = false;
                    string defAction = "";

                    if (f.FieldType == typeof(TextWidget))
                    {
                        cat = WidgetControlCategory.Readout;
                        defSize = new Vector2(110f, 36f);
                        string upperName = f.Name.ToUpperInvariant();
                        if (upperName.Contains("TITLE") || upperName.Contains("HEADER"))
                        {
                            cat = WidgetControlCategory.Header;
                            defSize = new Vector2(160f, 24f);
                        }
                        else if (upperName.Contains("BADGE"))
                        {
                            cat = WidgetControlCategory.ModeCapsule;
                            defSize = new Vector2(60f, 20f);
                        }
                        else if (upperName.Contains("SPEED") || upperName.Contains("SPD"))
                        {
                            defToken = "{SPD}";
                            defUnitDim = "Speed";
                        }
                        else if (upperName.Contains("ALT"))
                        {
                            defToken = "{ALT}";
                            defUnitDim = "Altitude";
                        }
                    }
                    else if (f.FieldType == typeof(LinearBarWidget))
                    {
                        cat = WidgetControlCategory.LinearGauge;
                        defSize = new Vector2(140f, 16f);
                        defToken = "{THROTTLE}";
                    }
                    else if (f.FieldType == typeof(ToggleButtonWidget))
                    {
                        cat = WidgetControlCategory.ActionButton;
                        defSize = new Vector2(64f, 24f);
                        isToggle = true;
                        string upperName = f.Name.ToUpperInvariant();
                        if (upperName.Contains("RCS")) defAction = "RCS";
                        else if (upperName.Contains("SAS")) defAction = "SAS";
                        else if (upperName.Contains("GEAR")) defAction = "GEAR";
                        else if (upperName.Contains("BRAKE")) defAction = "BRAKES";
                    }
                    else if (f.FieldType == typeof(ActionButtonWidget))
                    {
                        cat = WidgetControlCategory.ActionButton;
                        defSize = new Vector2(64f, 24f);
                    }
                    else if (f.FieldType == typeof(ReferenceFrameButtonWidget))
                    {
                        cat = WidgetControlCategory.ModeCapsule;
                        defSize = new Vector2(80f, 24f);
                        defAction = "CYCLE_FRAME";
                    }

                    AddPrototype(new ControlPrototypeDescriptor
                    {
                        PrototypeId = protoId,
                        DisplayName = protoName,
                        Description = $"来自组件「{widgetDisplayName}」的内部控件 {f.Name}",
                        SourceWidgetTypeName = widgetTypeName,
                        SourceWidgetDisplayName = widgetDisplayName,
                        Category = cat,
                        ControlType = f.FieldType,
                        DefaultSize = defSize,
                        DefaultToken = defToken,
                        DefaultTitle = ExtractCleanTitle(f.Name),
                        DefaultUnitDimension = defUnitDim,
                        IsToggle = isToggle,
                        DefaultAction = defAction
                    });
                }
                // 2. 扫描 [WidgetControlAttribute] 注解控件
                else
                {
                    var attrs = f.GetCustomAttributes(typeof(WidgetControlAttribute), true);
                    if (attrs != null && attrs.Length > 0)
                    {
                        var attr = (WidgetControlAttribute)attrs[0];
                        string protoId = $"{widgetTypeName}.{attr.Id.ToLowerInvariant()}";
                        string protoName = $"{widgetDisplayName} - {attr.DisplayName}";
                        WidgetControlCategory cat = WidgetControlCategory.GenericElement;
                        Vector2 sz = new Vector2(attr.Width > 0 ? attr.Width : 120f, attr.Height > 0 ? attr.Height : 36f);

                        if (f.FieldType == typeof(UnityEngine.UI.Text)) cat = WidgetControlCategory.Readout;
                        else if (f.FieldType == typeof(UnityEngine.UI.Image)) cat = WidgetControlCategory.LinearGauge;
                        else if (f.FieldType == typeof(UnityEngine.UI.Button)) cat = WidgetControlCategory.ActionButton;

                        AddPrototype(new ControlPrototypeDescriptor
                        {
                            PrototypeId = protoId,
                            DisplayName = protoName,
                            Description = $"来自组件「{widgetDisplayName}」的注解控件 {attr.DisplayName}",
                            SourceWidgetTypeName = widgetTypeName,
                            SourceWidgetDisplayName = widgetDisplayName,
                            Category = cat,
                            ControlType = f.FieldType,
                            DefaultSize = sz,
                            DefaultToken = attr.Token ?? "",
                            DefaultTitle = !string.IsNullOrEmpty(attr.DisplayName) ? attr.DisplayName.ToUpperInvariant() : ExtractCleanTitle(attr.Id),
                            TextRole = attr.TextRole,
                            MeterRole = attr.MeterRole
                        });
                    }
                }
            }
        }

        private static void TraverseRuntimeWidget(BaseFlightWidget w)
        {
            if (w == null || w.Config == null || w.Controls == null) return;
            string widgetTypeName = w.Config.WidgetType ?? w.GetType().Name;
            string widgetDisplayName = w.DisplayName ?? widgetTypeName;

            var list = w.Controls.All;
            for (int i = 0; i < list.Count; i++)
            {
                var ctrl = list[i];
                if (ctrl == null || string.IsNullOrEmpty(ctrl.Id)) continue;

                string protoId = $"{widgetTypeName}.{ctrl.Id.ToLowerInvariant()}";
                if (_prototypeMap.ContainsKey(protoId)) continue; // 已通过静态反射捕获

                Vector2 sz = ctrl.RectTransform != null ? ctrl.RectTransform.sizeDelta : new Vector2(100f, 32f);
                if (sz.x <= 1f || sz.y <= 1f) sz = new Vector2(120f, 32f);

                string token = "";
                string unitDim = "";
                if (ctrl is ITelemetryBindableControl bindable && bindable.HasTelemetryBinding)
                {
                    token = bindable.TelemetryToken ?? "";
                }

                AddPrototype(new ControlPrototypeDescriptor
                {
                    PrototypeId = protoId,
                    DisplayName = $"{widgetDisplayName} - {ctrl.DisplayName}",
                    Description = $"在运行时组件「{widgetDisplayName}」中纳管的子构件",
                    SourceWidgetTypeName = widgetTypeName,
                    SourceWidgetDisplayName = widgetDisplayName,
                    Category = ctrl.Category,
                    ControlType = ctrl.GetType(),
                    DefaultSize = sz,
                    DefaultToken = token,
                    DefaultTitle = !string.IsNullOrEmpty(ctrl.DisplayName) ? ctrl.DisplayName.ToUpperInvariant() : ExtractCleanTitle(ctrl.Id),
                    DefaultUnitDimension = unitDim
                });
            }
        }

        private static void RegisterNativeAvionicsPrototypes()
        {
            string nativeSrc = "native";
            string nativeSrcName = I18n.Tr("COMP_SRC_NATIVE", "⭐ 官方高频航电构件库");

            // 1. 核心读数盒
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.readout",
                DisplayName = I18n.Tr("COMP_PROTO_READOUT", "标准航电数显读数盒"),
                Description = "包含标题、等宽高亮读数、工程单位角标与阈值变色 (Normal/Warn/Crit)",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(140f, 45f),
                DefaultToken = "{SPD}",
                DefaultTitle = "SPEED",
                DefaultUnitDimension = "Speed"
            });

            // 2. 紧凑单值大字卡
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.large_digit",
                DisplayName = I18n.Tr("COMP_PROTO_LARGE_DIGIT", "大号主读数单值卡"),
                Description = "超大字体核心飞行数值监控，适合雷达真高 AGL、垂直速度 VSI 等",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(160f, 50f),
                DefaultToken = "{ALT:AGL:DIST}",
                DefaultTitle = "RADAR ALT",
                DefaultUnitDimension = "Altitude"
            });

            // 3. 水平线性计量槽
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.linear_bar_h",
                DisplayName = I18n.Tr("COMP_PROTO_BAR_H", "水平线性进度计量槽"),
                Description = "底槽与动态高亮填充条，内置 Min/Max 范围与越限警戒双阈值",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.LinearGauge,
                DefaultSize = new Vector2(160f, 32f),
                DefaultToken = "{THROTTLE}",
                DefaultTitle = "THRUST",
                DefaultMinValue = 0,
                DefaultMaxValue = 100,
                DefaultCaution = 85,
                DefaultWarning = 95
            });

            // 4. 垂直线性计量槽
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.linear_bar_v",
                DisplayName = I18n.Tr("COMP_PROTO_BAR_V", "垂直柱状计量槽"),
                Description = "纵向推力光柱/气压带，适合紧凑翼展排版",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.LinearGauge,
                DefaultSize = new Vector2(36f, 120f),
                DefaultToken = "{Q}",
                DefaultTitle = "Q",
                DefaultMinValue = 0,
                DefaultMaxValue = 40,
                DefaultCaution = 25,
                DefaultWarning = 35
            });

            // 5. 扇形弧度计 (Arc Meter)
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.arc_meter",
                DisplayName = I18n.Tr("COMP_PROTO_ARC_METER", "极坐标弧度仪表盘"),
                Description = "极坐标刻度盘，支持弧度角与发光指针，适合过载 G-Force 与攻角 AoA",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.ArcGauge,
                DefaultSize = new Vector2(100f, 100f),
                DefaultToken = "{GFORCE}",
                DefaultTitle = "G-FORCE",
                DefaultMinValue = 0,
                DefaultMaxValue = 10,
                DefaultCaution = 4,
                DefaultWarning = 6
            });

            // 6. 航电状态光字牌
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.annunciator",
                DisplayName = I18n.Tr("COMP_PROTO_ANNUNCIATOR", "航电状态告警光字牌"),
                Description = "四态警示灯珠 (Off/Normal绿/Caution黄/Warning红闪烁)，支持布尔表达式",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Annunciator,
                DefaultSize = new Vector2(75f, 26f),
                DefaultToken = "{VESSEL:GEAR}",
                DefaultTitle = "GEAR"
            });

            // 7. 机载系统动作开关 (RCS / SAS / Gear / Brakes / Lights / Abort)
            string[] sysActions = new string[] { "RCS", "SAS", "GEAR", "BRAKES", "LIGHTS", "ABORT", "SOLAR", "STAGE_LOCK" };
            for (int i = 0; i < sysActions.Length; i++)
            {
                string act = sysActions[i];
                AddPrototype(new ControlPrototypeDescriptor
                {
                    PrototypeId = $"native.action_{act.ToLowerInvariant()}",
                    DisplayName = string.Format(I18n.Tr("COMP_PROTO_ACTION_FMT", "{0} 机载动作开关"), act),
                    Description = $"一键控制 {act} 飞控系统，自带触控反馈与实时保持高亮状态",
                    SourceWidgetTypeName = nativeSrc,
                    SourceWidgetDisplayName = nativeSrcName,
                    Category = WidgetControlCategory.ActionButton,
                    DefaultSize = new Vector2(65f, 26f),
                    DefaultAction = act,
                    DefaultTitle = act,
                    IsToggle = true
                });
            }

            // 8. 双重防误触安全分级器 (Arm & Stage)
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.safety_stage",
                DisplayName = I18n.Tr("COMP_PROTO_SAFETY_STAGE", "防误触双重安全分级器"),
                Description = "包含「解除保险 (ARM)」滑块与大红色「STAGE」分级按键，彻底防止误分级",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.ActionButton,
                DefaultSize = new Vector2(130f, 32f),
                DefaultAction = "STAGE",
                DefaultTitle = "STAGE"
            });

            // 9. 微型 SAS 朝向按键排 (Mini SAS Pad)
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.mini_sas_pad",
                DisplayName = I18n.Tr("COMP_PROTO_MINI_SAS", "微型 SAS 朝向按键排"),
                Description = "紧凑 7 键姿态阵列 (STAB, PRO, RET, NRM, ANT, RAD, TGT, MAN) 快速定向",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.ActionButton,
                DefaultSize = new Vector2(210f, 28f),
                DefaultAction = "SAS_PAD",
                DefaultTitle = "SAS ORIENTATION"
            });

            // 10. 时间加速调节器 (Time Warp Stepper)
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.time_warp_stepper",
                DisplayName = I18n.Tr("COMP_PROTO_TIME_WARP", "时间加速调节器"),
                Description = "包含停滞 ⏹、1x 正常、加减速 ▶/◀ 与物理加速切换按键",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.ActionButton,
                DefaultSize = new Vector2(110f, 26f),
                DefaultAction = "TIME_WARP",
                DefaultTitle = "WARP"
            });

            // 11. 结构修饰：标准卡片标题栏
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.header",
                DisplayName = I18n.Tr("COMP_PROTO_HEADER", "航电卡片标题栏"),
                Description = "主标题、副标题、右侧状态徽标与底部分割发丝线",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Header,
                DefaultSize = new Vector2(360f, 26f),
                DefaultTitle = "AVIONICS PANEL"
            });

            // 12. 结构修饰：凹槽装饰框
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.structural.box",
                DisplayName = I18n.Tr("COMP_PROTO_BOX", "凹槽分区装饰卡片框"),
                Description = "半透明暗晶航电玻璃凹槽卡片，用于视觉区域归类与衬托",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Misc,
                DefaultSize = new Vector2(180f, 90f),
                DefaultOpacity = 0.4f
            });

            // 13. 结构修饰：发丝分割线
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.structural.divider",
                DisplayName = I18n.Tr("COMP_PROTO_DIVIDER", "发丝级分割线"),
                Description = "纯净高反差航电分割线，支持水平或垂直排版",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Misc,
                DefaultSize = new Vector2(180f, 2f),
                DefaultOpacity = 0.6f
            });

            // 14. 速度参考系状态胶囊
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.mode_capsule",
                DisplayName = I18n.Tr("COMP_PROTO_MODE_CAPSULE", "速度参考系状态胶囊"),
                Description = "切换航速基准模式 (轨道 ORBIT / 航向地面 SURF / 目标 TGT)，带发光微圆角胶囊药丸",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.ModeCapsule,
                DefaultSize = new Vector2(85f, 26f),
                DefaultToken = "{SPEED_MODE}",
                DefaultTitle = "ORBIT",
                DefaultAction = "CYCLE_FRAME"
            });

            // 15. 远拱点高度读数盒
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.apoapsis",
                DisplayName = I18n.Tr("COMP_PROTO_APOAPSIS", "远拱点高度读数盒"),
                Description = "轨道最高顶点海拔高度 (Ap)，支持自适应单位量纲转换 (m/km/Mm)",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(120f, 42f),
                DefaultToken = "{AP}",
                DefaultTitle = "APOAPSIS",
                DefaultUnit = "km",
                DefaultUnitDimension = "Altitude"
            });

            // 16. 近拱点高度读数盒
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.periapsis",
                DisplayName = I18n.Tr("COMP_PROTO_PERIAPSIS", "近拱点高度读数盒"),
                Description = "轨道最低过点海拔高度 (Pe)，支持自适应单位量纲转换 (m/km/Mm)",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(120f, 42f),
                DefaultToken = "{PE}",
                DefaultTitle = "PERIAPSIS",
                DefaultUnit = "km",
                DefaultUnitDimension = "Altitude"
            });

            // 17. 垂直升降速度表
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.vsi",
                DisplayName = I18n.Tr("COMP_PROTO_VSI", "垂直升降速度表"),
                Description = "垂直升降速率 (VSI)，实时监测着陆下降速度或爬升率",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(120f, 42f),
                DefaultToken = "{VSI}",
                DefaultTitle = "VERT SPEED",
                DefaultUnit = "m/s",
                DefaultUnitDimension = "Speed"
            });

            // 18. 推重比计量条
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.twr",
                DisplayName = I18n.Tr("COMP_PROTO_TWR", "推重比计量条"),
                Description = "当前发动机推重比 (TWR) 线性条，红线警示不足 1.0 临界工况",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.LinearGauge,
                DefaultSize = new Vector2(140f, 26f),
                DefaultToken = "{TWR}",
                DefaultTitle = "TWR",
                DefaultMinValue = 0,
                DefaultMaxValue = 5,
                DefaultCaution = 1.0,
                DefaultWarning = 0.95
            });

            // 19. 主母线电量状态条
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.battery",
                DisplayName = I18n.Tr("COMP_PROTO_BATTERY", "主母线电量状态条"),
                Description = "舰载电力百分比动态指示条，跌破警戒线黄色/红色预警",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.LinearGauge,
                DefaultSize = new Vector2(130f, 24f),
                DefaultToken = "{ELEC:PCT}",
                DefaultTitle = "BATTERY",
                DefaultMinValue = 0,
                DefaultMaxValue = 100,
                DefaultCaution = 25,
                DefaultWarning = 10
            });

            // 20. 姿控单推燃料状态条
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.monoprop",
                DisplayName = I18n.Tr("COMP_PROTO_MONOPROP", "姿控单推燃料状态条"),
                Description = "单组元推进剂 (RCS Monopropellant) 存量计量条",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.LinearGauge,
                DefaultSize = new Vector2(130f, 24f),
                DefaultToken = "{MONO:PCT}",
                DefaultTitle = "MONOPROP",
                DefaultMinValue = 0,
                DefaultMaxValue = 100,
                DefaultCaution = 20,
                DefaultWarning = 10
            });

            // 21. 飞行马赫数读数盒
            AddPrototype(new ControlPrototypeDescriptor
            {
                PrototypeId = "native.mach",
                DisplayName = I18n.Tr("COMP_PROTO_MACH", "飞行马赫数读数盒"),
                Description = "音速比马赫数 (Mach Number) 读数卡",
                SourceWidgetTypeName = nativeSrc,
                SourceWidgetDisplayName = nativeSrcName,
                Category = WidgetControlCategory.Readout,
                DefaultSize = new Vector2(110f, 42f),
                DefaultToken = "{MACH}",
                DefaultTitle = "MACH",
                DefaultUnit = "M"
            });
        }
    }
}
