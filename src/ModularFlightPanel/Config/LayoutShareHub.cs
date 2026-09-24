using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using ModularFlightPanel.Core;

namespace ModularFlightPanel.Config
{
    public class PresetInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsBuiltIn { get; set; }
        public string FilePath { get; set; }
    }

    /// <summary>
    /// 航电布局转换与社区分享中枢 (Layout Conversion & Sharing Hub)
    /// 提供：Base64 GZip 紧凑分享码编解码、出厂精选预设库、本地预设文件导入导出
    /// </summary>
    public static class LayoutShareHub
    {
        private const string CodePrefix = "MFP:v1:";
        private static string PresetsDir => Path.Combine(ModularFlightPanel.Core.AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData/Presets");

        /// <summary>
        /// 将指定布局数据压缩并编码为单行社区分享码
        /// </summary>
        public static string ExportShareCode(WidgetLayoutData layout)
        {
            if (layout == null) return string.Empty;

            try
            {
                string json = JsonUtility.ToJson(layout);
                byte[] rawBytes = Encoding.UTF8.GetBytes(json);

                using (MemoryStream outputStream = new MemoryStream())
                {
                    using (GZipStream gzip = new GZipStream(outputStream, CompressionMode.Compress))
                    {
                        gzip.Write(rawBytes, 0, rawBytes.Length);
                    }
                    string base64 = Convert.ToBase64String(outputStream.ToArray());
                    return CodePrefix + base64;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatPresets, ex, "Failed to export share code");
                return string.Empty;
            }
        }

        /// <summary>
        /// 从单行社区分享码解码并还原布局数据
        /// </summary>
        public static bool TryImportShareCode(string shareCode, out WidgetLayoutData layout, out string error)
        {
            layout = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(shareCode))
            {
                error = "分享码为空";
                return false;
            }

            string clean = shareCode.Trim();
            if (!clean.StartsWith(CodePrefix))
            {
                error = "无效的分享码格式 (必须以 MFP:v1: 开头)";
                return false;
            }

            try
            {
                string base64 = clean.Substring(CodePrefix.Length);
                byte[] compressedBytes = Convert.FromBase64String(base64);

                using (MemoryStream inputStream = new MemoryStream(compressedBytes))
                using (GZipStream gzip = new GZipStream(inputStream, CompressionMode.Decompress))
                using (MemoryStream outputStream = new MemoryStream())
                {
                    gzip.CopyTo(outputStream);
                    string json = Encoding.UTF8.GetString(outputStream.ToArray());
                    layout = JsonUtility.FromJson<WidgetLayoutData>(json);

                    if (layout == null || layout.Widgets == null || layout.Widgets.Count == 0)
                    {
                        error = "解析成功但未发现有效小组件配置";
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"解码失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 获取所有可用预设列表 (包含出厂预设与用户本地文件预设)
        /// </summary>
        public static List<PresetInfo> GetAvailablePresets()
        {
            List<PresetInfo> list = new List<PresetInfo>();

            // 1. 出厂内置预设
            list.Add(new PresetInfo
            {
                Name = "双翼标准航电 (Default Avionics)",
                Description = "经典双翼工效学布局，中央姿态球与伴生仪表，两侧速度/高度标尺带与大动压/G力表",
                IsBuiltIn = true
            });

            list.Add(new PresetInfo
            {
                Name = "现代全玻璃化座舱 (Modern Glass)",
                Description = "高信息密度玻璃化中控，包含 ELEC 电力分布图与 ROCKET 2D 多级推进栈",
                IsBuiltIn = true
            });

            list.Add(new PresetInfo
            {
                Name = "阿波罗复古登月 (Apollo Retro)",
                Description = "聚焦登月降落推重比、雷达真高、阶段燃料余量与姿控网格",
                IsBuiltIn = true
            });

            list.Add(new PresetInfo
            {
                Name = "深空无人远征探测 (Deep Space)",
                Description = "深空探测器专用：太阳能净充电监测、CommNet 长波天线阵列与轨道力学参数",
                IsBuiltIn = true
            });

            list.Add(new PresetInfo
            {
                Name = "SpaceX 载人龙飞船 (SpaceX Dragon)",
                Description = "极简全息触控座舱：顶部全景遥测横幅、中央对接与姿态准星 HUD、侧边 ECLSS 维生面板与底部药丸触控条",
                IsBuiltIn = true
            });

            // 2. 本地 Presets 目录预设
            try
            {
                if (!Directory.Exists(PresetsDir))
                {
                    Directory.CreateDirectory(PresetsDir);
                }

                string[] files = Directory.GetFiles(PresetsDir, "*.json");
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    list.Add(new PresetInfo
                    {
                        Name = fileName,
                        Description = $"本地预设文件 ({Path.GetFileName(file)})",
                        IsBuiltIn = false,
                        FilePath = file
                    });
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn(MFPLogger.CatPresets, $"Error listing preset directory: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// 加载指定预设并返回完整布局
        /// </summary>
        public static WidgetLayoutData LoadPreset(PresetInfo preset)
        {
            if (preset == null) return null;

            if (preset.IsBuiltIn)
            {
                return CreateBuiltInPreset(preset.Name);
            }

            if (!string.IsNullOrEmpty(preset.FilePath) && File.Exists(preset.FilePath))
            {
                try
                {
                    string json = File.ReadAllText(preset.FilePath);
                    return JsonUtility.FromJson<WidgetLayoutData>(json);
                }
                catch (Exception ex)
                {
                    MFPLogger.Exception(MFPLogger.CatPresets, ex, "Failed to load preset file");
                }
            }

            return null;
        }

        /// <summary>
        /// 将当前布局另存为独立预设文件
        /// </summary>
        public static bool SavePresetToFile(string presetName, WidgetLayoutData layout, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(presetName))
            {
                error = "预设名称不能为空";
                return false;
            }

            try
            {
                if (!Directory.Exists(PresetsDir)) Directory.CreateDirectory(PresetsDir);
                string cleanName = string.Join("_", presetName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(PresetsDir, $"{cleanName}.json");

                string json = JsonUtility.ToJson(layout, true);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static WidgetLayoutData CreateBuiltInPreset(string presetName)
        {
            WidgetLayoutData data = new WidgetLayoutData { GlobalScale = 1.0f };

            if (presetName.Contains("SpaceX") || presetName.Contains("Dragon") || presetName.Contains("龙飞船"))
            {
                // SpaceX 载人龙飞船极简全息触控座舱
                data.Widgets.Add(new WidgetConfig("spacex.header", "SpaceX 任务阶段与遥测顶栏", 0f, 420f, 1.0f) { WidgetType = "spacex_header", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.docking", "SpaceX 对接与姿态准星 HUD", 0f, 170f, 1.0f) { WidgetType = "spacex_docking", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, -40f, 1.0f) { IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.bottom", "SpaceX 底部控制与链路栏", 0f, -150f, 1.0f) { WidgetType = "spacex_bottom", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.overview", "SpaceX 综合工况与维生监控", -460f, 120f, 1.0f) { WidgetType = "spacex_overview", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.nd_navigation", "AERO ND 综合导航屏", 460f, 120f, 1.0f) { WidgetType = "nd_navigation", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.speed", "PFD 速度标尺带", -220f, 40f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", "PFD 高度标尺带", 220f, 40f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                return data;
            }

            // 核心基础组件
            data.Widgets.Add(new WidgetConfig("core.navball", "姿态球 (Navball)", 0f, 0f) { IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.bottom_controls", "RCS与SAS底控", 0f, -78f) { IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.orbital_info", "轨道数据面板", 0f, -108f) { IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.ecam_status", "ECAM 飞行状态", 0f, -185f) { IsEnabled = true });

            if (presetName.Contains("Modern Glass") || presetName.Contains("全玻璃化"))
            {
                // 现代全玻璃化：开启两侧子系统
                data.Widgets.Add(new WidgetConfig("tape.speed", "PFD 速度标尺带", -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", "PFD 高度标尺带", 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.electrical", "ELEC 电力分配系统", -440f, 160f, 1.0f) { WidgetType = "electrical", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.rocket", "ROCKET 2D 分级姿态卡", 440f, 160f, 1.0f) { WidgetType = "rocket2d", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("ecam.gforce", "ECAM G力过载表", -345f, -30f, 1.0f) { WidgetType = "ecam_dial", NumericToken = "{GFORCE}", MinValue = 0, MaxValue = 15, CautionThreshold = 5, WarningThreshold = 8, IsSoftLimit = true, UnitLabel = "G", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("ecam.q", "ECAM 动压监控表", 345f, -30f, 1.0f) { WidgetType = "ecam_dial", NumericToken = "{Q}", MinValue = 0, MaxValue = 35, CautionThreshold = 25, WarningThreshold = 32, IsSoftLimit = false, UnitLabel = "kPa", IsEnabled = true });
            }
            else if (presetName.Contains("Apollo") || presetName.Contains("阿波罗"))
            {
                // 阿波罗复古：强调推重比与降落真高
                data.Widgets.Add(new WidgetConfig("tape.speed", "PFD 速度标尺带", -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD:SURF}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", "PFD 雷达真高带", 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT:AGL}", StepInterval = 50f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("ecam.throttle", "ECAM 引擎推力表", -345f, 50f, 1.0f) { WidgetType = "ecam_dial", NumericToken = "{THROTTLE}", MinValue = 0, MaxValue = 100, CautionThreshold = 85, WarningThreshold = 100, IsSoftLimit = false, UnitLabel = "%", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.life", "LIFE SUPPORT 维生监控", -440f, -80f, 1.0f) { WidgetType = "life_support", IsEnabled = true });
            }
            else if (presetName.Contains("Deep Space") || presetName.Contains("深空"))
            {
                // 深空探测：强调太阳能电力与天线通信
                data.Widgets.Add(new WidgetConfig("custom.electrical", "ELEC 电力分配系统", -440f, 140f, 1.0f) { WidgetType = "electrical", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.signal", "COMMNET 天线通信网络", 440f, 140f, 1.0f) { WidgetType = "signal", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("core.sas_dial", "环形 SAS 罗盘", 185f, -15f, 1.0f) { IsEnabled = true });
            }
            else
            {
                // 双翼标准预设
                data.Widgets.Add(new WidgetConfig("tape.speed", "PFD 速度标尺带", -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", "PFD 高度标尺带", 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("ecam.gforce", "ECAM G力过载表", -345f, 90f, 1.0f) { WidgetType = "ecam_dial", NumericToken = "{GFORCE}", MinValue = 0, MaxValue = 15, CautionThreshold = 5, WarningThreshold = 8, IsSoftLimit = true, UnitLabel = "G", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("ecam.q", "ECAM 动压监控表", 345f, 90f, 1.0f) { WidgetType = "ecam_dial", NumericToken = "{Q}", MinValue = 0, MaxValue = 35, CautionThreshold = 25, WarningThreshold = 32, IsSoftLimit = false, UnitLabel = "kPa", IsEnabled = true });
            }

            return data;
        }
    }

    /// <summary>
    /// 航电主题色彩与 Shader 参数分享中枢 (Theme Share Hub)
    /// 支持一键 GZip + Base64 编解码为紧凑分享码 (MFP-THEME:v1:...)
    /// </summary>
    public static class ThemeShareHub
    {
        private const string CodePrefix = "MFP-THEME:v1:";

        public static string ExportShareCode(ThemeConfig theme)
        {
            if (theme == null) return string.Empty;
            try
            {
                string json = JsonUtility.ToJson(theme);
                byte[] rawBytes = Encoding.UTF8.GetBytes(json);

                using (MemoryStream outputStream = new MemoryStream())
                {
                    using (GZipStream gzip = new GZipStream(outputStream, CompressionMode.Compress))
                    {
                        gzip.Write(rawBytes, 0, rawBytes.Length);
                    }
                    string base64 = Convert.ToBase64String(outputStream.ToArray());
                    return CodePrefix + base64;
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Exception(MFPLogger.CatPresets, ex, "Failed to export theme share code");
                return string.Empty;
            }
        }

        public static bool TryImportShareCode(string shareCode, out ThemeConfig theme, out string error)
        {
            theme = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(shareCode))
            {
                error = "主题分享码为空";
                return false;
            }

            string clean = shareCode.Trim();
            if (!clean.StartsWith(CodePrefix))
            {
                error = "无效的主题分享码格式 (必须以 MFP-THEME:v1: 开头)";
                return false;
            }

            try
            {
                string base64 = clean.Substring(CodePrefix.Length);
                byte[] compressedBytes = Convert.FromBase64String(base64);

                using (MemoryStream inputStream = new MemoryStream(compressedBytes))
                using (GZipStream gzip = new GZipStream(inputStream, CompressionMode.Decompress))
                using (MemoryStream outputStream = new MemoryStream())
                {
                    gzip.CopyTo(outputStream);
                    string json = Encoding.UTF8.GetString(outputStream.ToArray());
                    theme = JsonUtility.FromJson<ThemeConfig>(json);

                    if (theme == null || string.IsNullOrEmpty(theme.ThemeId))
                    {
                        error = "解析成功但未发现有效主题 ID";
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"解码失败: {ex.Message}";
                return false;
            }
        }
    }
}
