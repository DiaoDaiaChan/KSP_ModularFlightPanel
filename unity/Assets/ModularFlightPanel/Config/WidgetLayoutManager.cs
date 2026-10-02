using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Config
{
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
                    CurrentLayout = AvionicsConfigParser.ParseLayout(json, out _);
                    if (CurrentLayout != null && CurrentLayout.Widgets != null && CurrentLayout.Widgets.Count > 0)
                    {
                        EnsureValidDrawOrders();
                        MigrateToUnifiedPfdLayout();
                        MFPLogger.Info(MFPLogger.CatUI, $"Successfully loaded layout with {CurrentLayout.Widgets.Count} widgets via AvionicsConfigParser.");
                        return;
                    }
                    MFPLogger.Warn(MFPLogger.CatUI, "layout.json existed but contained 0 widgets. Attempting recovery from backup...");
                }
                catch (Exception ex)
                {
                    MFPLogger.Exception(MFPLogger.CatUI, ex, "Error reading layout config");
                }
            }

            // 自动从备份副本中抢救布局
            if (File.Exists(BackupPath))
            {
                try
                {
                    string backupJson = File.ReadAllText(BackupPath);
                    var backup = AvionicsConfigParser.ParseLayout(backupJson, out _);
                    if (backup != null && backup.Widgets != null && backup.Widgets.Count > 0)
                    {
                        CurrentLayout = backup;
                        EnsureValidDrawOrders();
                        MigrateToUnifiedPfdLayout();
                        SaveLayout();
                        MFPLogger.Warn(MFPLogger.CatUI, $"Successfully recovered layout ({CurrentLayout.Widgets.Count} widgets) from backup!");
                        return;
                    }
                }
                catch { }
            }

            // 初始化默认布局 (含预置核心组件及通配符示例组件)
            CreateDefaultLayout();
            EnsureValidDrawOrders();
            SaveLayout();
        }

        private void MigrateToUnifiedPfdLayout()
        {
            bool changed = false;

            WidgetConfig speedTape = GetConfig("tape.speed");
            if (speedTape == null)
            {
                speedTape = new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -225f, 0f)
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
                altTape = new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 高度标尺带"), 225f, 0f)
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
                // [防数据丢失终极红线] 严禁在组件列表为空时覆盖任何磁盘布局文件！
                if (CurrentLayout == null || CurrentLayout.Widgets == null || CurrentLayout.Widgets.Count == 0)
                {
                    MFPLogger.Warn(MFPLogger.CatUI, "CRITICAL: Attempted to save layout with 0 widgets! Aborting save to protect user configuration from data loss.");
                    return;
                }

                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 1. 若现有 layout.json 有效且非空，自动克隆一份为 layout.backup.json 作为防丢失保护副本
                if (File.Exists(ConfigPath))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(ConfigPath);
                        if (fi.Length > 200) // 确保源文件是实质性配置文件 (避免将空文件备份)
                        {
                            File.Copy(ConfigPath, BackupPath, true);
                        }
                    }
                    catch { }
                }

                // 2. 原子写入模式 (Atomic Write via .tmp)：防止并发冲突或 KSP 崩溃导致生成 0 字节损坏文件
                string tmpPath = ConfigPath + ".tmp";
                string json = AvionicsConfigParser.SerializeLayout(CurrentLayout, true);
                File.WriteAllText(tmpPath, json);

                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                File.Move(tmpPath, ConfigPath);

                // 3. 若当前载具开启了独立配置绑定，同步保存其独立文件
                if (!string.IsNullOrEmpty(CurrentVesselName) && HasVesselProfile(CurrentVesselName))
                {
                    SaveVesselLayout(CurrentVesselName);
                }

                MFPLogger.Info(MFPLogger.CatUI, $"Saved widget layout ({CurrentLayout.Widgets.Count} widgets) to layout.json (Atomic + Backup)");
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to save layout");
            }
        }

        public bool CreateManualBackup()
        {
            if (CurrentLayout == null || CurrentLayout.Widgets == null || CurrentLayout.Widgets.Count == 0)
            {
                MFPLogger.Warn(MFPLogger.CatUI, "Cannot create backup: Current layout has 0 widgets.");
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = AvionicsConfigParser.SerializeLayout(CurrentLayout, true);
                File.WriteAllText(BackupPath, json);
                return true;
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatUI, ex, "Failed to create manual backup");
                return false;
            }
        }

        public void EnsureValidDrawOrders()
        {
            if (CurrentLayout?.Widgets == null || CurrentLayout.Widgets.Count == 0) return;
            bool allZero = CurrentLayout.Widgets.TrueForAll(w => w.DrawOrder == 0);
            if (allZero && CurrentLayout.Widgets.Count > 1)
            {
                for (int i = 0; i < CurrentLayout.Widgets.Count; i++)
                {
                    CurrentLayout.Widgets[i].DrawOrder = i;
                }
            }
        }

        public bool ApplyLayout(WidgetLayoutData newLayout)
        {
            if (newLayout == null || newLayout.Widgets == null || newLayout.Widgets.Count == 0)
            {
                MFPLogger.Warn(MFPLogger.CatUI, "Cannot apply layout: Layout is null or contains 0 widgets.");
                return false;
            }

            CurrentLayout = newLayout;
            EnsureValidDrawOrders();
            if (CurrentLayout.GlobalScale <= 0.1f) CurrentLayout.GlobalScale = 1.25f;
            SaveLayout();
            MFPLogger.Info(MFPLogger.CatUI, $"Applied new layout successfully with {CurrentLayout.Widgets.Count} widgets.");
            return true;
        }

        public bool ReloadFromDisk()
        {
            if (!File.Exists(ConfigPath)) return false;
            try
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = AvionicsConfigParser.ParseLayout(json, out _);
                if (loaded != null && loaded.Widgets != null && loaded.Widgets.Count > 0)
                {
                    CurrentLayout = loaded;
                    EnsureValidDrawOrders();
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
                var loaded = AvionicsConfigParser.ParseLayout(json, out _);
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
            if (CurrentLayout == null || CurrentLayout.Widgets == null || CurrentLayout.Widgets.Count == 0)
            {
                MFPLogger.Warn(MFPLogger.CatUI, $"CRITICAL: Attempted to save vessel layout for '{vesselName}' with 0 widgets! Aborting save.");
                return false;
            }

            try
            {
                string path = GetVesselConfigPath(vesselName);
                string json = AvionicsConfigParser.SerializeLayout(CurrentLayout, true);
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
                var loaded = AvionicsConfigParser.ParseLayout(json, out _);
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
                lastWrite = I18n.Tr("CFG_FILE_NOT_FOUND", "不存在");
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
                lastWrite = I18n.Tr("CFG_FILE_NO_BACKUP", "无备份");
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
            if (!exists) return I18n.Tr("CFG_LAYOUT_NOT_EXISTS_MEM", "不存在 (使用内存默认)");
            return I18n.TrFormat("CFG_FILE_SIZE_MODIFIED_FMT", size / 1024f, lastWrite);
        }

        public string GetBackupFileInfo()
        {
            GetBackupFileInfo(out bool exists, out long size, out string lastWrite);
            if (!exists) return I18n.Tr("CFG_LAYOUT_NO_BACKUP_COPY", "无备份副本");
            return I18n.TrFormat("CFG_FILE_SIZE_MODIFIED_FMT", size / 1024f, lastWrite);
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
            CurrentLayout.Widgets.Add(new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "姿态球 (Navball)"), 0f, 0f));
            CurrentLayout.Widgets.Add(new WidgetConfig("core.heading_arc", I18n.GetWidgetName("core.heading_arc", "PFD 航向指示弧 (Set 2)"), 0f, 76f)
            {
                WidgetType = "heading_arc",
                IsEnabled = true
            });

            // 全新高精垂直光柱推力带与动压气压带 (严丝合缝外切速度/高度带两翼)
            var thrGauge = new WidgetConfig("gauge.throttle", I18n.GetWidgetName("gauge.throttle", "AVIONICS 油门推力带"), -158f, 0f, 1.0f)
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

            var baroGauge = new WidgetConfig("gauge.barometer", I18n.GetWidgetName("gauge.barometer", "AVIONICS 大气压强带"), 158f, 0f, 1.0f)
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
            var speedTape = new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -120f, 0f, 1.0f)
            {
                WidgetType = "tape",
                NumericToken = "{SPD}",
                StepInterval = 10f,
                IsLeftOrientation = true,
                UnitLabel = "m/s",
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(speedTape);

            var altTape = new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 高度标尺带"), 120f, 0f, 1.0f)
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
            CurrentLayout.Widgets.Add(new WidgetConfig("core.bottom_controls", I18n.GetWidgetName("core.bottom_controls", "RCS与SAS底控台"), 0f, -88f)
            {
                IsEnabled = true
            });

            // 环形 SAS 航向罗盘 (位于底控栏正下方，带飞船滚转剪影与 9 大模式按键)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.sas_dial", I18n.GetWidgetName("core.sas_dial", "环形 SAS 罗盘"), 0f, -150f)
            {
                IsEnabled = true
            });

            // 现代化分级与飞行姿态操纵台 (左下角标准航电柱)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.stage_control", I18n.GetWidgetName("core.stage_control", "分级与飞行操纵台"), -360f, -120f)
            {
                WidgetType = "stage_control",
                IsEnabled = true
            });

            // 现代化垂直分级时序序列仪 (垂直挂载于操纵台上方)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.staging_sequence", I18n.GetWidgetName("custom.staging_sequence", "垂直分级时序序列仪"), -360f, 110f)
            {
                WidgetType = "staging_sequence",
                IsEnabled = true
            });

            // 现代化平滑时间加速控制器 (左上角状态栏)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.time_warp", I18n.GetWidgetName("core.time_warp", "AVIONICS 时间加速与任务时钟"), -560f, 460f)
            {
                WidgetType = "time_warp",
                IsEnabled = true
            });

            // 现代化天线通信网络监控仪 (右上角通信网络)
            CurrentLayout.Widgets.Add(new WidgetConfig("custom.signal", I18n.GetWidgetName("custom.signal", "AVIONICS 通信网络与天线探针"), 560f, 460f)
            {
                WidgetType = "signal",
                IsEnabled = true
            });

            // 现代化折叠工具栏 (右侧边栏)
            CurrentLayout.Widgets.Add(new WidgetConfig("core.toolbar", I18n.GetWidgetName("core.toolbar", "AVIONICS 现代折叠工具栏"), 890f, 0f)
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
            var cfg = new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f, template)
            {
                DrawOrder = CurrentLayout.Widgets.Count
            };
            CurrentLayout.Widgets.Add(cfg);
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
                DrawOrder = CurrentLayout.Widgets.Count,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
            return id;
        }

        public string AddEcamDialWidget(string title, string token, double min, double max, double caution, double warning, bool isSoftLimit, string unit, Vector2 initialPos)
        {
            string id = "custom." + Guid.NewGuid().ToString().Substring(0, 8);
            var cfg = new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f)
            {
                WidgetType = "arc_meter",
                NumericToken = token,
                MinValue = (float)min,
                MaxValue = (float)max,
                CautionThreshold = (float)caution,
                WarningThreshold = (float)warning,
                IsSoftLimit = isSoftLimit,
                UnitLabel = unit,
                CustomTemplate = $"TITLE={title};TOKEN={token};MIN={min};MAX={max};WARN={caution};CRIT={warning};UNIT={unit};SOFT={isSoftLimit}",
                DrawOrder = CurrentLayout.Widgets.Count,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
            return id;
        }

        public string AddArcTapeWidget(string title, string token, bool isSpeedTape, float curvature, float radius, float span, bool isLeft, Vector2 initialPos)
        {
            string prefix = isSpeedTape ? "arc_speed." : "arc_alt.";
            string id = prefix + Guid.NewGuid().ToString().Substring(0, 8);
            string template = isSpeedTape
                ? $"CURVATURE={curvature:F2};RADIUS={radius:F0};SPAN={span:F0};SIDE={(isLeft ? "LEFT" : "RIGHT")};TYPE=SPEED;VAL={token};MODE={{SPD:MODE}};ACC={{ACC}}"
                : $"CURVATURE={curvature:F2};RADIUS={radius:F0};SPAN={span:F0};SIDE={(isLeft ? "LEFT" : "RIGHT")};TYPE=ALT;VAL={token};MODE=ALT;BOTTOM={{ALT:AGL:DIST}};TREND={{VSI}}";

            var cfg = new WidgetConfig(id, title, initialPos.x, initialPos.y, 1.0f, template)
            {
                WidgetType = isSpeedTape ? "arc_speed_tape" : "arc_altitude_tape",
                NumericToken = token,
                StepInterval = isSpeedTape ? 10f : 100f,
                IsLeftOrientation = isLeft,
                UnitLabel = isSpeedTape ? "m/s" : "m",
                DrawOrder = CurrentLayout.Widgets.Count,
                IsEnabled = true
            };
            CurrentLayout.Widgets.Add(cfg);
            SaveLayout();
            return id;
        }

        public string AddArcSpeedTapeWidget(string title, string token, float curvature, float radius, float span, bool isLeft, Vector2 initialPos)
        {
            return AddArcTapeWidget(title, token, true, curvature, radius, span, isLeft, initialPos);
        }

        public string AddArcAltitudeTapeWidget(string title, string token, float curvature, float radius, float span, bool isLeft, Vector2 initialPos)
        {
            return AddArcTapeWidget(title, token, false, curvature, radius, span, isLeft, initialPos);
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

            string prefix = src.WidgetType == "tape" ? "tape." : (src.WidgetType == "arc_tape" ? "arc_tape." : "custom.");
            string newId = prefix + Guid.NewGuid().ToString().Substring(0, 8);

            var clone = new WidgetConfig(newId, src.DisplayName + I18n.Tr("CFG_DUPLICATE_SUFFIX", " (副本)"), src.PositionX + 25f, src.PositionY + 25f, src.Scale, src.CustomTemplate)
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
                ScaleX = src.ScaleX,
                ScaleY = src.ScaleY,
                DrawOrder = CurrentLayout.Widgets.Count,
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
            CurrentLayout.Widgets.RemoveAll(w => w.WidgetId.StartsWith("custom.") || w.WidgetId.StartsWith("tape.") || w.WidgetId.StartsWith("arc_tape."));
            SaveLayout();
        }

        /// <summary>
        /// 全局创建自由航电画板 (PS 自由搭建工坊)
        /// </summary>
        public WidgetConfig CreateNewArtboard(bool blank = false)
        {
            if (CurrentLayout == null) return null;

            string baseId = "custom.artboard";
            string newId = baseId;
            int counter = 1;
            while (CurrentLayout.Widgets.Any(x => x.WidgetId == newId))
            {
                newId = $"{baseId}_{counter++}";
            }

            float px = Mathf.Round((Screen.width - 380f) * 0.5f / 10f) * 10f;
            float py = Mathf.Round((Screen.height - 220f) * 0.5f / 10f) * 10f;

            var demoCfg = blank ? CompositePanelConfig.CreateBlankPanel() : CompositePanelConfig.CreateDefaultDemoPanel();

            var w = new WidgetConfig(newId, I18n.Tr("COMP_ARTBOARD_DEFAULT_NAME", "自由航电仪表板"), px, py)
            {
                WidgetType = "composite_panel",
                Scale = 1.0f,
                Rotation = 0f,
                IsEnabled = true,
                CustomTemplate = demoCfg.ToJson()
            };

            CurrentLayout.Widgets.Add(w);
            SaveLayout();
            ModularFlightPanel.UI.FlightHUDManager.Instance?.RebuildHUD();

            ModularFlightPanel.UI.WidgetDragHandler.IsEditModeActive = true;
            if (ModularFlightPanel.UI.FlightHUDManager.Instance?.ModularWidgets != null)
            {
                for (int i = 0; i < ModularFlightPanel.UI.FlightHUDManager.Instance.ModularWidgets.Count; i++)
                {
                    var live = ModularFlightPanel.UI.FlightHUDManager.Instance.ModularWidgets[i];
                    if (live != null && live.Config?.WidgetId == newId)
                    {
                        ModularFlightPanel.UI.WidgetSelectionManager.Select(live, false);
                        break;
                    }
                }
            }

            return w;
        }

        public static WidgetConfig CreateArtboard(bool blank = false) => Instance.CreateNewArtboard(blank);
    }
}
