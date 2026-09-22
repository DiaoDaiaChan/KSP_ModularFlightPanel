using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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

        private string ConfigPath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ModularFlightPanel/PluginData/layout.json");

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
                        Debug.Log($"[ModularFlightPanel] Successfully loaded layout with {CurrentLayout.Widgets.Count} widgets.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModularFlightPanel] Error reading layout config: {ex.Message}");
                }
            }

            // 初始化默认布局 (含预置核心组件及通配符示例组件)
            CreateDefaultLayout();
            SaveLayout();
        }

        public void SaveLayout()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(ConfigPath, json);
                Debug.Log("[ModularFlightPanel] Saved widget layout to layout.json");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModularFlightPanel] Failed to save layout: {ex.Message}");
            }
        }

        private void CreateDefaultLayout()
        {
            CurrentLayout = new WidgetLayoutData { GlobalScale = 1.0f };

            // 核心组件配置 (紧凑人体工学布局，预留合理间隙杜绝遮挡)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.throttle", "油门推力弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.vsi", "垂直速度弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.propellant", "推进剂消耗弧形表", 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.speed_box", "速度读数盒", -118f, -5f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.alt_box", "高度读数盒", 118f, -5f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.bottom_controls", "RCS与SAS底控", 0f, -78f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.orbital_info", "轨道数据面板", 0f, -108f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.ecam_status", "ECAM 飞行状态", 0f, -185f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.sas_dial", "环形 SAS 罗盘", 185f, -15f));

            // 通配符扩展小组件示例 (默认不遮挡核心飞行面板，用户可在设置界面自由添加开启并拖拽)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.aero", "大气与气动参数", -360f, 110f, 1.0f, "动压: {Q} | 马赫: {MACH} | G: {GFORCE}") { IsEnabled = false });
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.power", "推重比与推进", 360f, 110f, 1.0f, "TWR: {TWR:F2} | 燃料: {PROP} | 油门: {THROTTLE}") { IsEnabled = false });

            // PFD 风格滚动速度带与高度带套件预设
            var speedTape = new WidgetConfig("tape.speed", "PFD 速度标尺带", -225f, 0f, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = "{SPD}",
                StepInterval = 10f,
                IsLeftOrientation = true,
                UnitLabel = "m/s",
                IsEnabled = false
            };
            CurrentLayout.Widgets.Add(speedTape);

            var altTape = new WidgetConfig("tape.altitude", "PFD 高度标尺带", 225f, 0f, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = "{ALT}",
                StepInterval = 100f,
                IsLeftOrientation = false,
                UnitLabel = "m",
                IsEnabled = false
            };
            CurrentLayout.Widgets.Add(altTape);

            // ECAM 风格圆弧仪表套件预设 (含软上限 10G 爆表模式与有上限油门模式)
            var gDial = new WidgetConfig("ecam.gforce", "ECAM G力过载表", -345f, 90f, 1.0f)
            {
                WidgetType = "ecam_dial",
                NumericToken = "{GFORCE}",
                MinValue = 0.0,
                MaxValue = 10.0,
                CautionThreshold = 5.0,
                WarningThreshold = 8.0,
                IsSoftLimit = true, // 软上限爆表模式 (10G满格告警, 超过继续如实数显)
                UnitLabel = "G",
                IsEnabled = false
            };
            CurrentLayout.Widgets.Add(gDial);

            var qDial = new WidgetConfig("ecam.q", "ECAM 动压监控表", -345f, -35f, 1.0f)
            {
                WidgetType = "ecam_dial",
                NumericToken = "{Q}",
                MinValue = 0.0,
                MaxValue = 35.0,
                CautionThreshold = 25.0,
                WarningThreshold = 32.0,
                IsSoftLimit = true,
                UnitLabel = "kPa",
                IsEnabled = false
            };
            CurrentLayout.Widgets.Add(qDial);

            var thrDial = new WidgetConfig("ecam.throttle", "ECAM 引擎推力表", 345f, 90f, 1.0f)
            {
                WidgetType = "ecam_dial",
                NumericToken = "{THROTTLE}",
                MinValue = 0.0,
                MaxValue = 100.0,
                CautionThreshold = 85.0,
                WarningThreshold = 100.0,
                IsSoftLimit = false, // 有上限型 (严格截断 0~100%)
                UnitLabel = "%",
                IsEnabled = false
            };
            CurrentLayout.Widgets.Add(thrDial);
        }

        public WidgetConfig GetConfig(string widgetId)
        {
            return CurrentLayout.Widgets.Find(w => w.WidgetId == widgetId);
        }

        public void AddCustomWidget(string title, string template, Vector2 initialPos)
        {
            string id = "custom." + Guid.NewGuid().ToString().Substring(0, 8);
            CurrentLayout.Widgets.Add(new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f, template));
            SaveLayout();
        }

        public void AddTapeWidget(string title, string token, bool isLeft, float step, Vector2 initialPos)
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
        }

        public void AddEcamDialWidget(string title, string token, double min, double max, double caution, double warning, bool isSoftLimit, string unit, Vector2 initialPos)
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
                UnitLabel = unit,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
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
