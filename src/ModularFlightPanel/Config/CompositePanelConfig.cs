using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using ModularFlightPanel.Core;
using ModularFlightPanel.UI.Framework;

namespace ModularFlightPanel.Config
{
    /// <summary>
    /// 复合自由面板子控件图层配置 (Composite Element Layer Config)
    /// 对标 Photoshop 图层概念：包含像素级绝对位置、宽高、旋转角、独立不透明度、图层顺序与数据绑定
    /// </summary>
    [Serializable]
    public class CompositeElementConfig
    {
        public string LayerId = "layer_1";
        public string Name = "新图层";
        public string PrototypeId = "native.readout";
        public string SourceWidgetTypeName = "native";
        public WidgetControlCategory Category = WidgetControlCategory.GenericElement;

        // 几何变换与图层属性 (Photoshop-grade Transforms)
        public float X = 0f;
        public float Y = 0f;
        public float Width = 120f;
        public float Height = 40f;
        public float Rotation = 0f;
        public float Opacity = 1.0f; // 0.0f ~ 1.0f 独立不透明度
        public int DrawOrder = 0;    // 图层堆叠顺序 (数值越大越靠顶层)
        public bool IsLocked = false;// 图层是否锁定 (锁定后穿透鼠标点击)
        public bool IsVisible = true;// 图层显隐

        // 遥测与数据绑定
        public string Token = "{SPD}";
        public string Title = "";
        public string Unit = "";
        public string UnitDimension = ""; // 对应 UnitDimension (Speed, Altitude, DynamicPressure, etc.)
        public double MinValue = 0.0;
        public double MaxValue = 100.0;
        public double CautionThreshold = 80.0;
        public double WarningThreshold = 95.0;

        // 控制与动作绑定 (针对按键/开关)
        public string ActionType = ""; // "RCS", "SAS", "GEAR", "BRAKES", "LIGHTS", "ABORT", "STAGE", "AG1"..
        public bool IsToggle = false;

        // 扩展自定义参数 (Key-Value)
        public Dictionary<string, string> CustomParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CompositeElementConfig Clone()
        {
            var clone = (CompositeElementConfig)this.MemberwiseClone();
            clone.Category = this.Category;
            clone.CustomParams = new Dictionary<string, string>(this.CustomParams, StringComparer.OrdinalIgnoreCase);
            return clone;
        }

        public JsonObject ToJsonObject()
        {
            var obj = new JsonObject();
            obj.Add("LayerId", LayerId ?? "");
            obj.Add("Name", Name ?? "");
            obj.Add("PrototypeId", PrototypeId ?? "");
            obj.Add("SourceWidgetTypeName", SourceWidgetTypeName ?? "");
            obj.Add("Category", Category.ToString());
            obj.Add("X", X);
            obj.Add("Y", Y);
            obj.Add("Width", Width);
            obj.Add("Height", Height);
            obj.Add("Rotation", Rotation);
            obj.Add("Opacity", Opacity);
            obj.Add("DrawOrder", DrawOrder);
            obj.Add("IsLocked", IsLocked);
            obj.Add("IsVisible", IsVisible);

            obj.Add("Token", Token ?? "");
            obj.Add("Title", Title ?? "");
            obj.Add("Unit", Unit ?? "");
            obj.Add("UnitDimension", UnitDimension ?? "");
            obj.Add("MinValue", MinValue);
            obj.Add("MaxValue", MaxValue);
            obj.Add("CautionThreshold", CautionThreshold);
            obj.Add("WarningThreshold", WarningThreshold);

            obj.Add("ActionType", ActionType ?? "");
            obj.Add("IsToggle", IsToggle);

            if (CustomParams != null && CustomParams.Count > 0)
            {
                var paramsObj = new JsonObject();
                foreach (var kvp in CustomParams)
                {
                    paramsObj.Add(kvp.Key, kvp.Value ?? "");
                }
                obj.Add("CustomParams", paramsObj);
            }

            return obj;
        }

        public static CompositeElementConfig FromJsonObject(JsonObject obj)
        {
            if (obj == null) return new CompositeElementConfig();
            var elem = new CompositeElementConfig
            {
                LayerId = obj.GetString("LayerId", "layer_" + Guid.NewGuid().ToString().Substring(0, 6)),
                Name = obj.GetString("Name", "图层"),
                PrototypeId = obj.GetString("PrototypeId", "native.readout"),
                SourceWidgetTypeName = obj.GetString("SourceWidgetTypeName", "native"),
                X = obj.GetFloat("X", 0f),
                Y = obj.GetFloat("Y", 0f),
                Width = obj.GetFloat("Width", 120f),
                Height = obj.GetFloat("Height", 40f),
                Rotation = obj.GetFloat("Rotation", 0f),
                Opacity = Mathf.Clamp01(obj.GetFloat("Opacity", 1.0f)),
                DrawOrder = obj.GetInt("DrawOrder", 0),
                IsLocked = obj.GetBool("IsLocked", false),
                IsVisible = obj.GetBool("IsVisible", true),

                Token = obj.GetString("Token", "{SPD}"),
                Title = obj.GetString("Title", ""),
                Unit = obj.GetString("Unit", ""),
                UnitDimension = obj.GetString("UnitDimension", ""),
                MinValue = obj.GetDouble("MinValue", 0.0),
                MaxValue = obj.GetDouble("MaxValue", 100.0),
                CautionThreshold = obj.GetDouble("CautionThreshold", 80.0),
                WarningThreshold = obj.GetDouble("WarningThreshold", 95.0),

                ActionType = obj.GetString("ActionType", ""),
                IsToggle = obj.GetBool("IsToggle", false)
            };

            string catStr = obj.GetString("Category", "");
            if (!string.IsNullOrEmpty(catStr) && Enum.TryParse<WidgetControlCategory>(catStr, true, out var parsedCat))
            {
                elem.Category = parsedCat;
            }
            else
            {
                elem.Category = elem.ResolveCategory();
            }

            var paramsObj = obj.GetObject("CustomParams");
            if (paramsObj != null)
            {
                foreach (var key in paramsObj.Keys)
                {
                    elem.CustomParams[key] = paramsObj.GetString(key, "");
                }
            }

            return elem;
        }

        /// <summary>
        /// 智能分类决议通道 (Heuristic & Catalog Category Resolution)
        /// 根绝所有图层被降级为单一方框的根本原因：
        /// 1. 优先采用已明确持久化的 Category；
        /// 2. 查验构件目录 WidgetControlCatalog 元数据原型分类；
        /// 3. 启发式依据 PrototypeId、Name、Title 及长宽比进行高精度航电类型分流。
        /// </summary>
        public WidgetControlCategory ResolveCategory()
        {
            if (Category != WidgetControlCategory.GenericElement && Category != WidgetControlCategory.Misc)
            {
                return Category;
            }

            var proto = WidgetControlCatalog.FindPrototype(PrototypeId);
            if (proto != null && proto.Category != WidgetControlCategory.GenericElement && proto.Category != WidgetControlCategory.Misc)
            {
                return proto.Category;
            }

            string p = (PrototypeId ?? "").ToLowerInvariant();
            string n = (Name ?? "").ToLowerInvariant();
            string t = (Title ?? "").ToLowerInvariant();

            // 1. 标题与卡片顶栏
            if (p.Contains("header") || n.Contains("title") || n.Contains("header") || t.Contains("header"))
                return WidgetControlCategory.Header;

            // 2. 弧形度量表盘
            if (p.Contains("arc") || p.Contains("radial") || p.Contains("dial") || p.Contains("gforce") || n.Contains("arc") || n.Contains("dial"))
                return WidgetControlCategory.ArcGauge;

            // 3. 状态告警光字牌
            if (p.Contains("annunciator") || p.Contains("lamp") || p.Contains("warn") || p.Contains("caution") || n.Contains("light") || n.Contains("lamp"))
                return WidgetControlCategory.Annunciator;

            // 4. 模式胶囊
            if (p.Contains("capsule") || p.Contains("badge") || p.Contains("mode") || p.Contains("frame") || n.Contains("badge") || n.Contains("mode"))
                return WidgetControlCategory.ModeCapsule;

            // 5. 线性柱条与进度条
            if (p.Contains("bar") || p.Contains("gauge") || p.Contains("slider") || p.Contains("progress") || p.Contains("thr") || n.Contains("bar") || n.Contains("gauge"))
                return WidgetControlCategory.LinearGauge;

            // 6. 交互按键与系统开关
            if (p.Contains("switch") || p.Contains("button") || p.Contains("btn") || p.Contains("action") || p.Contains("stage") || p.Contains("toggle") || p.Contains("warp"))
                return WidgetControlCategory.ActionButton;

            // 7. 纯数值数显
            if (p.Contains("readout") || p.Contains("digit") || p.Contains("val") || p.Contains("speed") || p.Contains("alt") || !string.IsNullOrEmpty(Token))
                return WidgetControlCategory.Readout;

            // 8. 几何长宽比兜底
            if (Height > 0f)
            {
                float ratio = Width / Height;
                if (ratio > 5.0f && Height <= 4f) return WidgetControlCategory.Misc;
                if (ratio >= 4.0f && Height <= 32f) return WidgetControlCategory.Header;
            }

            return WidgetControlCategory.Readout;
        }
    }

    /// <summary>
    /// 复合自由面板整盘配置 (Composite Freeform Avionics Panel Config)
    /// 承载于 WidgetConfig.CustomTemplate 中，支持纯 C# 无损双向 JSON 往返
    /// </summary>
    [Serializable]
    public class CompositePanelConfig
    {
        public int Version = 1;
        public string PanelTitle = "自由航电仪表板";
        public float BaseWidth = 380f;
        public float BaseHeight = 220f;
        public float PanelOpacity = 0.95f; // 画板底框不透明度
        public string BackgroundStyle = "DarkGlass"; // DarkGlass, Clear, Framed, MetalOutline
        public List<CompositeElementConfig> Elements = new List<CompositeElementConfig>();

        public CompositePanelConfig Clone()
        {
            var clone = new CompositePanelConfig
            {
                Version = this.Version,
                PanelTitle = this.PanelTitle,
                BaseWidth = this.BaseWidth,
                BaseHeight = this.BaseHeight,
                PanelOpacity = this.PanelOpacity,
                BackgroundStyle = this.BackgroundStyle,
                Elements = new List<CompositeElementConfig>(this.Elements.Count)
            };
            for (int i = 0; i < this.Elements.Count; i++)
            {
                clone.Elements.Add(this.Elements[i].Clone());
            }
            return clone;
        }

        public string ToJson(bool pretty = false)
        {
            var root = new JsonObject();
            root.Add("Version", Version);
            root.Add("PanelTitle", PanelTitle ?? "");
            root.Add("BaseWidth", BaseWidth);
            root.Add("BaseHeight", BaseHeight);
            root.Add("PanelOpacity", PanelOpacity);
            root.Add("BackgroundStyle", BackgroundStyle ?? "DarkGlass");

            var arr = new JsonArray();
            for (int i = 0; i < Elements.Count; i++)
            {
                arr.Add(Elements[i].ToJsonObject());
            }
            root.Add("Elements", arr);

            return root.ToString(pretty);
        }

        public static CompositePanelConfig FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return CreateDefaultDemoPanel();
            }

            try
            {
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    var cfg = new CompositePanelConfig
                    {
                        Version = obj.GetInt("Version", 1),
                        PanelTitle = obj.GetString("PanelTitle", "自由航电仪表板"),
                        BaseWidth = obj.GetFloat("BaseWidth", 380f),
                        BaseHeight = obj.GetFloat("BaseHeight", 220f),
                        PanelOpacity = Mathf.Clamp01(obj.GetFloat("PanelOpacity", 0.95f)),
                        BackgroundStyle = obj.GetString("BackgroundStyle", "DarkGlass")
                    };

                    var arr = obj.GetArray("Elements");
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            if (arr[i] is JsonObject elemObj)
                            {
                                cfg.Elements.Add(CompositeElementConfig.FromJsonObject(elemObj));
                            }
                        }
                    }

                    if (cfg.Elements.Count == 0)
                    {
                        cfg = CreateDefaultDemoPanel();
                    }
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn("CompositePanelConfig", $"Error parsing composite panel JSON: {ex.Message}");
            }

            return CreateDefaultDemoPanel();
        }

        public static CompositePanelConfig CreateDefaultDemoPanel()
        {
            var cfg = new CompositePanelConfig
            {
                Version = 1,
                PanelTitle = I18n.Tr("COMP_PANEL_DEMO_TITLE", "飞控与动力综合中枢"),
                BaseWidth = 380f,
                BaseHeight = 180f,
                PanelOpacity = 0.95f,
                BackgroundStyle = "DarkGlass"
            };

            // 1. 顶部标题栏
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_header",
                Name = I18n.Tr("COMP_LAYER_HEADER", "主标题栏"),
                PrototypeId = "native.header",
                SourceWidgetTypeName = "native",
                X = 0f,
                Y = 70f,
                Width = 360f,
                Height = 26f,
                Title = I18n.Tr("COMP_DEMO_HEADER", "FLIGHT & PROPULSION CORE"),
                Opacity = 1.0f,
                DrawOrder = 0,
                IsLocked = true
            });

            // 2. 航速读数盒 (来自姿态球)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spd_box",
                Name = I18n.Tr("COMP_LAYER_SPD", "地表速度读数盒"),
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "core.navball",
                X = -95f,
                Y = 20f,
                Width = 160f,
                Height = 50f,
                Token = "{SPD}",
                Title = "AIRSPEED",
                UnitDimension = "Speed",
                Opacity = 1.0f,
                DrawOrder = 1
            });

            // 3. 推力与油门条 (来自推力计)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_thr_bar",
                Name = I18n.Tr("COMP_LAYER_THR", "主推力计量槽"),
                PrototypeId = "gauge.throttle_bar",
                SourceWidgetTypeName = "gauge.throttle",
                X = 85f,
                Y = 20f,
                Width = 170f,
                Height = 50f,
                Token = "{THROTTLE}",
                Title = "THRUST",
                MinValue = 0,
                MaxValue = 100,
                CautionThreshold = 85,
                WarningThreshold = 95,
                Opacity = 1.0f,
                DrawOrder = 2
            });

            // 4. RCS 动作开关 (来自底控台)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_rcs_btn",
                Name = "RCS 姿控开关",
                PrototypeId = "bottom_controls.rcs_button",
                SourceWidgetTypeName = "core.bottom_controls",
                X = -120f,
                Y = -50f,
                Width = 65f,
                Height = 26f,
                ActionType = "RCS",
                Title = "RCS",
                Opacity = 0.95f,
                DrawOrder = 3
            });

            // 5. SAS 稳定性开关 (来自底控台)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_sas_btn",
                Name = "SAS 增稳开关",
                PrototypeId = "bottom_controls.sas_button",
                SourceWidgetTypeName = "core.bottom_controls",
                X = -45f,
                Y = -50f,
                Width = 65f,
                Height = 26f,
                ActionType = "SAS",
                Title = "SAS",
                Opacity = 0.95f,
                DrawOrder = 4
            });

            // 6. 起落架状态光字牌
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_gear_lamp",
                Name = "GEAR 起落架光字牌",
                PrototypeId = "native.annunciator",
                SourceWidgetTypeName = "native",
                X = 40f,
                Y = -50f,
                Width = 80f,
                Height = 26f,
                Token = "{VESSEL:GEAR}",
                Title = "GEAR",
                Opacity = 0.95f,
                DrawOrder = 5
            });

            // 7. 刹车状态光字牌
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_brakes_lamp",
                Name = "BRAKES 刹车光字牌",
                PrototypeId = "native.annunciator",
                SourceWidgetTypeName = "native",
                X = 130f,
                Y = -50f,
                Width = 80f,
                Height = 26f,
                Token = "{VESSEL:BRAKES}",
                Title = "BRAKES",
                Opacity = 0.95f,
                DrawOrder = 6
            });

            return cfg;
        }

        public static CompositePanelConfig CreateBlankPanel()
        {
            return new CompositePanelConfig
            {
                Version = 1,
                PanelTitle = I18n.Tr("COMP_PANEL_BLANK_TITLE", "空白自由航电画板"),
                BaseWidth = 380f,
                BaseHeight = 220f,
                PanelOpacity = 0.95f,
                BackgroundStyle = "DarkGlass",
                Elements = new List<CompositeElementConfig>()
            };
        }

        public static CompositePanelConfig CreateSpaceXPropulsionPanel()
        {
            var cfg = new CompositePanelConfig
            {
                Version = 1,
                PanelTitle = I18n.Tr("COMP_PANEL_SPACEX_TITLE", "SpaceX 风格推进与分级台"),
                BaseWidth = 420f,
                BaseHeight = 220f,
                PanelOpacity = 0.95f,
                BackgroundStyle = "DarkGlass"
            };

            // 1. 标题
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_header",
                Name = "推进中枢标题",
                PrototypeId = "native.header",
                SourceWidgetTypeName = "native",
                X = 0f,
                Y = 85f,
                Width = 400f,
                Height = 24f,
                Title = "SPACEX PROPULSION & STAGING",
                Opacity = 1.0f,
                DrawOrder = 0,
                IsLocked = true
            });

            // 2. 本级剩余 dV
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_dv",
                Name = "本级 Delta-V 读数",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = -105f,
                Y = 40f,
                Width = 190f,
                Height = 48f,
                Token = "{STAGE_DV}",
                Title = "STAGE DELTA-V",
                Unit = "m/s",
                UnitDimension = "Velocity",
                Opacity = 1.0f,
                DrawOrder = 1
            });

            // 3. 实时 TWR
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_twr",
                Name = "推重比 TWR 读数",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = 105f,
                Y = 40f,
                Width = 190f,
                Height = 48f,
                Token = "{TWR}",
                Title = "THRUST / WEIGHT",
                Unit = "G",
                Opacity = 1.0f,
                DrawOrder = 2
            });

            // 4. 油门推力条
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_thr",
                Name = "主推力槽",
                PrototypeId = "gauge.throttle_bar",
                SourceWidgetTypeName = "native",
                X = -105f,
                Y = -20f,
                Width = 190f,
                Height = 36f,
                Token = "{THROTTLE}",
                Title = "THROTTLE",
                MinValue = 0,
                MaxValue = 100,
                CautionThreshold = 90,
                WarningThreshold = 100,
                Opacity = 1.0f,
                DrawOrder = 3
            });

            // 5. 推进剂剩余百分比条
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_fuel",
                Name = "本级燃料余量槽",
                PrototypeId = "gauge.throttle_bar",
                SourceWidgetTypeName = "native",
                X = 105f,
                Y = -20f,
                Width = 190f,
                Height = 36f,
                Token = "{FUEL_STAGE_PCT}",
                Title = "PROPELLANT",
                MinValue = 0,
                MaxValue = 100,
                CautionThreshold = 15,
                WarningThreshold = 5,
                Opacity = 1.0f,
                DrawOrder = 4
            });

            // 6. 双重安全分级器
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_spx_stage_arm",
                Name = "双重安全分级器",
                PrototypeId = "native.safety_stage",
                SourceWidgetTypeName = "native",
                X = 0f,
                Y = -75f,
                Width = 380f,
                Height = 38f,
                ActionType = "Stage",
                Title = "STAGE ARM & FIRE",
                Opacity = 1.0f,
                DrawOrder = 5
            });

            return cfg;
        }

        public static CompositePanelConfig CreateOrbitalManeuverPanel()
        {
            var cfg = new CompositePanelConfig
            {
                Version = 1,
                PanelTitle = I18n.Tr("COMP_PANEL_ORBITAL_TITLE", "轨道机动领航综合板"),
                BaseWidth = 440f,
                BaseHeight = 240f,
                PanelOpacity = 0.95f,
                BackgroundStyle = "DarkGlass"
            };

            // 1. 标题
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_header",
                Name = "轨道领航标题",
                PrototypeId = "native.header",
                SourceWidgetTypeName = "native",
                X = 0f,
                Y = 95f,
                Width = 420f,
                Height = 24f,
                Title = "ORBITAL MANEUVER CONSOLE",
                Opacity = 1.0f,
                DrawOrder = 0,
                IsLocked = true
            });

            // 2. 远拱点高度 (Apoapsis)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_ap",
                Name = "远拱点高度 Ap",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = -110f,
                Y = 50f,
                Width = 200f,
                Height = 46f,
                Token = "{ORB_AP}",
                Title = "APOAPSIS (AP)",
                UnitDimension = "Length",
                Opacity = 1.0f,
                DrawOrder = 1
            });

            // 3. 近拱点高度 (Periapsis)
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_pe",
                Name = "近拱点高度 Pe",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = 110f,
                Y = 50f,
                Width = 200f,
                Height = 46f,
                Token = "{ORB_PE}",
                Title = "PERIAPSIS (PE)",
                UnitDimension = "Length",
                Opacity = 1.0f,
                DrawOrder = 2
            });

            // 4. 机动节点剩余 dV
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_node",
                Name = "机动节点剩余 dV",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = -110f,
                Y = -5f,
                Width = 200f,
                Height = 46f,
                Token = "{NODE_DV}",
                Title = "MANEUVER NODE dV",
                Unit = "m/s",
                UnitDimension = "Velocity",
                Opacity = 1.0f,
                DrawOrder = 3
            });

            // 5. 轨道倾角与离心率
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_inc",
                Name = "轨道倾角 Inc",
                PrototypeId = "navball.speed_box",
                SourceWidgetTypeName = "native",
                X = 110f,
                Y = -5f,
                Width = 200f,
                Height = 46f,
                Token = "{ORB_INC}",
                Title = "INCLINATION",
                Unit = "°",
                Opacity = 1.0f,
                DrawOrder = 4
            });

            // 6. 微型 SAS 姿态定向排
            cfg.Elements.Add(new CompositeElementConfig
            {
                LayerId = "elem_orb_sas_pad",
                Name = "微型 SAS 定向排",
                PrototypeId = "native.mini_sas",
                SourceWidgetTypeName = "native",
                X = 0f,
                Y = -65f,
                Width = 360f,
                Height = 32f,
                Opacity = 0.95f,
                DrawOrder = 5
            });

            return cfg;
        }
    }
}
