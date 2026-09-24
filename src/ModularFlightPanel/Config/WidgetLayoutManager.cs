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
        public float GlobalScale = 1.25f;
        public List<WidgetConfig> Widgets = new List<WidgetConfig>();
    }

    public class WidgetLayoutManager
    {
        private static WidgetLayoutManager _instance;
        public static WidgetLayoutManager Instance => _instance ?? (_instance = new WidgetLayoutManager());

        public WidgetLayoutData CurrentLayout { get; private set; } = new WidgetLayoutData();

        public string ConfigPath => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/layout.json");
        public string BackupPath => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/layout.backup.json");
        public string VesselsDir => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/Vessels");

        public string CurrentVesselName { get; private set; } = string.Empty;
        public bool HasBackup => File.Exists(BackupPath);

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

                // 1. 若现有 layout.json 有效，自动克隆一份为 layout.backup.json 作为防丢失保护副本
                if (File.Exists(ConfigPath))
                {
                    try
                    {
                        File.Copy(ConfigPath, BackupPath, true);
                    }
                    catch { }
                }

                // 2. 原子写入模式 (Atomic Write via .tmp)：防止并发冲突或 KSP 崩溃导致生成 0 字节损坏文件
                string tmpPath = ConfigPath + ".tmp";
                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(tmpPath, json);

                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                File.Move(tmpPath, ConfigPath);

                // 3. 若当前载具开启了独立配置绑定，同步保存其独立文件
                if (!string.IsNullOrEmpty(CurrentVesselName) && HasVesselProfile(CurrentVesselName))
                {
                    SaveVesselLayout(CurrentVesselName);
                }

                MFPLogger.Info(MFPLogger.CatUI, "Saved widget layout to layout.json (Atomic + Backup)");
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to save layout");
            }
        }

        public bool CreateManualBackup()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(BackupPath, json);
                return true;
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to create manual backup");
                return false;
            }
        }

        public bool ReloadFromDisk()
        {
            if (!File.Exists(ConfigPath)) return false;
            try
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonUtility.FromJson<WidgetLayoutData>(json);
                if (loaded != null && loaded.Widgets != null && loaded.Widgets.Count > 0)
                {
                    CurrentLayout = loaded;
                    MigrateToUnifiedPfdLayout();
                    MFPLogger.Info(MFPLogger.CatUI, "Reloaded layout from disk successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to reload layout from disk");
            }
            return false;
        }

        public bool RestoreFromBackup()
        {
            if (!File.Exists(BackupPath)) return false;
            try
            {
                string json = File.ReadAllText(BackupPath);
                var loaded = JsonUtility.FromJson<WidgetLayoutData>(json);
                if (loaded != null && loaded.Widgets != null && loaded.Widgets.Count > 0)
                {
                    CurrentLayout = loaded;
                    SaveLayout();
                    MFPLogger.Info(MFPLogger.CatUI, "Restored layout from backup successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to restore layout from backup");
            }
            return false;
        }

        #region Per-Vessel Profile Management

        public string GetVesselConfigPath(string vesselName)
        {
            if (string.IsNullOrEmpty(vesselName)) return string.Empty;
            if (!Directory.Exists(VesselsDir)) Directory.CreateDirectory(VesselsDir);
            string safeName = string.Join("_", vesselName.Split(Path.GetInvalidFileNameChars())).Trim();
            return Path.Combine(VesselsDir, $"{safeName}.json");
        }

        public bool HasVesselProfile(string vesselName)
        {
            string path = GetVesselConfigPath(vesselName);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public bool SaveVesselLayout(string vesselName)
        {
            if (string.IsNullOrEmpty(vesselName)) return false;
            try
            {
                string path = GetVesselConfigPath(vesselName);
                string json = JsonUtility.ToJson(CurrentLayout, true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, $"Failed to save vessel layout for {vesselName}");
                return false;
            }
        }

        public bool LoadVesselLayout(string vesselName)
        {
            if (!HasVesselProfile(vesselName)) return false;
            try
            {
                string path = GetVesselConfigPath(vesselName);
                string json = File.ReadAllText(path);
                var loaded = JsonUtility.FromJson<WidgetLayoutData>(json);
                if (loaded != null && loaded.Widgets != null && loaded.Widgets.Count > 0)
                {
                    CurrentLayout = loaded;
                    MigrateToUnifiedPfdLayout();
                    return true;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, $"Failed to load vessel layout for {vesselName}");
            }
            return false;
        }

        public bool DeleteVesselProfile(string vesselName)
        {
            try
            {
                string path = GetVesselConfigPath(vesselName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public bool OnActiveVesselChanged(string newVesselName)
        {
            CurrentVesselName = newVesselName ?? string.Empty;
            if (string.IsNullOrEmpty(CurrentVesselName)) return false;

            if (HasVesselProfile(CurrentVesselName))
            {
                MFPLogger.Info(MFPLogger.CatUI, $"Switching to vessel '{CurrentVesselName}' dedicated layout.");
                return LoadVesselLayout(CurrentVesselName);
            }
            return false;
        }

        #endregion

        #region File Metadata Queries

        public void GetLayoutFileInfo(out bool exists, out long size, out string lastWrite)
        {
            if (!File.Exists(ConfigPath))
            {
                exists = false;
                size = 0;
                lastWrite = "不存在";
                return;
            }
            FileInfo fi = new FileInfo(ConfigPath);
            exists = true;
            size = fi.Length;
            lastWrite = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void GetBackupFileInfo(out bool exists, out long size, out string lastWrite)
        {
            if (!File.Exists(BackupPath))
            {
                exists = false;
                size = 0;
                lastWrite = "无备份";
                return;
            }
            FileInfo fi = new FileInfo(BackupPath);
            exists = true;
            size = fi.Length;
            lastWrite = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public string GetLayoutFileInfo()
        {
            GetLayoutFileInfo(out bool exists, out long size, out string lastWrite);
            if (!exists) return "不存在 (使用内存默认)";
            return $"大小: {size / 1024f:F1} KB | 修改: {lastWrite}";
        }

        public string GetBackupFileInfo()
        {
            GetBackupFileInfo(out bool exists, out long size, out string lastWrite);
            if (!exists) return "无备份副本";
            return $"大小: {size / 1024f:F1} KB | 修改: {lastWrite}";
        }

        public void ResetToDefault()
        {
            CreateDefaultLayout();
            SaveLayout();
        }

        #endregion

        private void CreateDefaultLayout()
        {
            CurrentLayout = new WidgetLayoutData { GlobalScale = 1.25f };

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

            // 现代化分级与飞行姿态操纵台 (左下角标准航电柱)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.stage_control", "分级与飞行操纵台", -360f, -120f)
            {
                WidgetType = "stage_control",
                IsEnabled = true
            });

            // 现代化垂直分级时序序列仪 (垂直挂载于操纵台上方)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.staging_sequence", "垂直分级时序序列仪", -360f, 110f)
            {
                WidgetType = "staging_sequence",
                IsEnabled = true
            });

            // 现代化平滑时间加速控制器 (左上角状态栏)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.time_warp", "AVIONICS 时间加速与任务时钟", -560f, 460f)
            {
                WidgetType = "time_warp",
                IsEnabled = true
            });

            // 现代化天线通信网络监控仪 (右上角通信网络)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.signal", "AVIONICS 通信网络与天线探针", 560f, 460f)
            {
                WidgetType = "signal",
                IsEnabled = true
            });

            // 现代化折叠工具栏 (右侧边栏)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.toolbar", "AVIONICS 现代折叠工具栏", 890f, 0f)
            {
                WidgetType = "toolbar",
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
