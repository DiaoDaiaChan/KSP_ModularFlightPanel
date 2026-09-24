using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Config
{
    [Serializable]
    public class WidgetLayoutData
    {
        public float GlobalScale = 1.0f;
        public List<WidgetConfig> Widgets = new List<WidgetConfig>();
    }

    public class WidgetLayoutManager
    {
        private static WidgetLayoutManager _instance;
        public static WidgetLayoutManager Instance => _instance ?? (_instance = new WidgetLayoutManager());

        public WidgetLayoutData CurrentLayout { get; private set; } = new WidgetLayoutData();

        private string ConfigPath => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/layout.json");

        public void Initialize()
        {
            LoadLayout();
        }

        public void LoadLayout()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    CurrentLayout = JsonUtility.FromJson<WidgetLayoutData>(json);
                    if (CurrentLayout != null && CurrentLayout.Widgets != null && CurrentLayout.Widgets.Count > 0)
                    {
                        MigrateToUnifiedPfdLayout();
                        MFPLogger.Info(MFPLogger.CatUI, $"Successfully loaded layout with {CurrentLayout.Widgets.Count} widgets.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MFPLogger.Exception(MFPLogger.CatUI, ex, "Error reading layout config");
                }
            }

            // 初始化默认布局 (含预置核心组件及通配符示例组件)
            CreateDefaultLayout();
            SaveLayout();
        }

        private void MigrateToUnifiedPfdLayout()
        {
            bool changed = false;

            WidgetConfig speedTape = GetConfig("tape.speed");
            if (speedTape == null)
            {
                speedTape = new WidgetConfig("tape.speed", "PFD 速度标尺带", -225f, 0f)
                {
                    WidgetType = "tape",
                    NumericToken = "{SPD}",
                    StepInterval = 10f,
                    IsLeftOrientation = true,
                    UnitLabel = "m/s",
                    IsEnabled = true
                };
                CurrentLayout.Widgets.Add(speedTape);
                changed = true;
            }

            WidgetConfig altTape = GetConfig("tape.altitude");
            if (altTape == null)
            {
                altTape = new WidgetConfig("tape.altitude", "PFD 高度标尺带", 225f, 0f)
                {
                    WidgetType = "tape",
                    NumericToken = "{ALT}",
                    StepInterval = 100f,
                    IsLeftOrientation = false,
                    UnitLabel = "m",
                    IsEnabled = true
                };
                CurrentLayout.Widgets.Add(altTape);
                changed = true;
            }

            // 标尺带已经包含中央实时读数，关闭旧版独立数值盒，避免出现两套读数。
            WidgetConfig speedBox = GetConfig("core.speed_box");
            if (speedBox != null && speedBox.IsEnabled)
            {
                speedBox.IsEnabled = false;
                changed = true;
            }

            WidgetConfig altBox = GetConfig("core.alt_box");
            if (altBox != null && altBox.IsEnabled)
            {
                altBox.IsEnabled = false;
                changed = true;
            }

            if (changed) SaveLayout();
        }

        public void SaveLayout()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(ConfigPath, json);
                MFPLogger.Info(MFPLogger.CatUI, "Saved widget layout to layout.json");
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to save layout");
            }
        }

        private void CreateDefaultLayout()
        {
            CurrentLayout = new WidgetLayoutData { GlobalScale = 1.0f };

            // 核心飞行仪表集群 (紧凑人体工学布局：姿态球、速度高度带、航向指示器、全新光柱油门/气压计、一体化底控与SAS控制台)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.heading_arc", "PFD 航向指示弧 (Set 2)", 0f, 0f)
            {
                WidgetType = "heading_arc",
                IsEnabled = true
            });

            // 全新高精垂直光柱推力带与动压气压带 (严丝合缝外切速度/高度带两翼)
            var thrGauge = new WidgetConfig("gauge.throttle", "AVIONICS 油门推力带", -158f, 0f, 1.0f)
            {
                WidgetType = "bar_gauge",
                NumericToken = "{THROTTLE}",
                MinValue = 0f,
                MaxValue = 100f,
                CautionThreshold = 85f,
                WarningThreshold = 100f,
                UnitLabel = "%",
                IsLeftOrientation = true,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(thrGauge);

            var baroGauge = new WidgetConfig("gauge.barometer", "AVIONICS 大气压强带", 158f, 0f, 1.0f)
            {
                WidgetType = "bar_gauge",
                NumericToken = "{ATM}",
                MinValue = 0f,
                MaxValue = 1.0f,
                CautionThreshold = 0.8f,
                WarningThreshold = 0.95f,
                UnitLabel = "atm",
                StepInterval = 0.25f,
                IsLeftOrientation = false,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(baroGauge);

            // PFD 风格窄体滚动速度带与高度带套件预设 (外切光柱与姿态球，紧凑一体化)
            var speedTape = new WidgetConfig("tape.speed", "PFD 速度标尺带", -120f, 0f, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = "{SPD}",
                StepInterval = 10f,
                IsLeftOrientation = true,
                UnitLabel = "m/s",
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(speedTape);

            var altTape = new WidgetConfig("tape.altitude", "PFD 高度标尺带", 120f, 0f, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = "{ALT}",
                StepInterval = 100f,
                IsLeftOrientation = false,
                UnitLabel = "m",
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(altTape);

            // 一体化紧凑底控栏 (嵌合在姿态球正下方与双带之间)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.bottom_controls", "RCS与SAS底控台", 0f, -88f)
            {
                IsEnabled = true
            });

            // 环形 SAS 航向罗盘 (位于底控栏正下方，带飞船滚转剪影与 9 大模式按键)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.sas_dial", "环形 SAS 罗盘", 0f, -150f)
            {
                IsEnabled = true
            });

            // 现代化分级与飞行姿态操纵台 (完美替代原版左下角粗糙方框)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.stage_control", "分级与飞行操纵台", -340f, -120f)
            {
                WidgetType = "stage_control",
                IsEnabled = true
            });
        }

        public WidgetConfig GetConfig(string widgetId)
        {
            return CurrentLayout.Widgets.Find(w => w.WidgetId == widgetId);
        }

        public string AddCustomWidget(string title, string template, Vector2 initialPos)
        {
            string id = "custom." + Guid.NewGuid().ToString().Substring(0, 8);
            CurrentLayout.Widgets.Add(new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f, template));
            SaveLayout();
            return id;
        }

        public string AddTapeWidget(string title, string token, bool isLeft, float step, Vector2 initialPos)
        {
            string id = "tape." + Guid.NewGuid().ToString().Substring(0, 8);
            var cfg = new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = token,
                StepInterval = step,
                IsLeftOrientation = isLeft,
                UnitLabel = isLeft ? "m/s" : "m",
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
            return id;
        }

        public string AddEcamDialWidget(string title, string token, double min, double max, double caution, double warning, bool isSoftLimit, string unit, Vector2 initialPos)
        {
            string id = "ecam." + Guid.NewGuid().ToString().Substring(0, 8);
            var cfg = new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f)
            {
                WidgetType = "ecam_dial",
                NumericToken = token,
                MinValue = min,
                MaxValue = max,
                CautionThreshold = caution,
                WarningThreshold = warning,
                IsSoftLimit = isSoftLimit,
                LimitMode = isSoftLimit ? "soft" : "hard",
                UnitLabel = unit,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
            return id;
        }

        public void RemoveWidget(string widgetId)
        {
            CurrentLayout.Widgets.RemoveAll(w => w.WidgetId == widgetId);
            SaveLayout();
        }

        public void DuplicateWidget(string widgetId)
        {
            var src = GetConfig(widgetId);
            if (src == null) return;

            string prefix = src.WidgetType == "ecam_dial" ? "ecam." : (src.WidgetType == "tape" ? "tape." : "custom.");
            string newId = prefix + Guid.NewGuid().ToString().Substring(0, 8);

            var clone = new WidgetConfig(newId, src.DisplayName + " (副本)", src.PositionX + 25f, src.PositionY + 25f, src.Scale, src.CustomTemplate)
            {
                WidgetType = src.WidgetType,
                NumericToken = src.NumericToken,
                MinValue = src.MinValue,
                MaxValue = src.MaxValue,
                CautionThreshold = src.CautionThreshold,
                WarningThreshold = src.WarningThreshold,
                IsSoftLimit = src.IsSoftLimit,
                LimitMode = src.LimitMode,
                UnitLabel = src.UnitLabel,
                StepInterval = src.StepInterval,
                IsLeftOrientation = src.IsLeftOrientation,
                IsEnabled = true
            };

            CurrentLayout.Widgets.Add(clone);
            SaveLayout();
        }

        public void ResetToDefaultLayout()
        {
            CreateDefaultLayout();
            SaveLayout();
        }

        public void ClearAllCustomWidgets()
        {
            CurrentLayout.Widgets.RemoveAll(w => w.WidgetId.StartsWith("custom.") || w.WidgetId.StartsWith("ecam.") || w.WidgetId.StartsWith("tape."));
            SaveLayout();
        }
    }
}
