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
        public string Id { get; set; }
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
                string json = AvionicsConfigParser.SerializeLayout(layout, false);
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
        /// 从多种来源导入并还原布局数据：支持单行社区分享码 (MFP:v1:)、原始 JSON 文本、或本地预设文件路径/文件名
        /// </summary>
        public static bool TryImportShareCode(string input, out WidgetLayoutData layout, out string error)
        {
            layout = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(input))
            {
                error = I18n.Tr("ERR_INPUT_EMPTY", "输入内容为空");
                return false;
            }

            string clean = input.Trim();

            // 1. 本地 JSON 配置文件路径或文件名检测 (例如 "diao.json"、"layout.json" 或绝对路径)
            if (clean.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || clean.IndexOfAny(new char[] { '/', '\\' }) >= 0)
            {
                string targetPath = clean;
                if (!File.Exists(targetPath))
                {
                    // 尝试在 Presets 目录下查找
                    string presetCandidate = Path.Combine(PresetsDir, clean);
                    if (File.Exists(presetCandidate)) targetPath = presetCandidate;
                    else
                    {
                        // 尝试在 PluginData 根目录下查找
                        string pluginDataCandidate = Path.Combine(ModularFlightPanel.Core.AppPathHelper.RootPath, "GameData/ModularFlightPanel/PluginData", clean);
                        if (File.Exists(pluginDataCandidate)) targetPath = pluginDataCandidate;
                    }
                }

                if (File.Exists(targetPath))
                {
                    try
                    {
                        string json = File.ReadAllText(targetPath);
                        layout = AvionicsConfigParser.ParseLayout(json, out error);
                        if (layout != null && layout.Widgets != null && layout.Widgets.Count > 0)
                        {
                            return true;
                        }
                        error = I18n.TrFormat("ERR_FILE_NO_WIDGETS", Path.GetFileName(targetPath));
                        return false;
                    }
                    catch (Exception ex)
                    {
                        error = I18n.TrFormat("ERR_READ_CONFIG_FAIL", ex.Message);
                        return false;
                    }
                }
            }

            // 2. 原始 JSON 字符串格式检测 (例如从文本直接粘贴 { "GlobalScale": ..., "Widgets": [...] })
            if (clean.StartsWith("{") && clean.EndsWith("}"))
            {
                try
                {
                    layout = AvionicsConfigParser.ParseLayout(clean, out error);
                    if (layout != null && layout.Widgets != null && layout.Widgets.Count > 0)
                    {
                        return true;
                    }
                    error = I18n.Tr("ERR_JSON_NO_WIDGETS", "JSON 解析成功，但未发现有效小组件配置 (Widgets 列表为空)");
                    return false;
                }
                catch (Exception ex)
                {
                    error = I18n.TrFormat("ERR_JSON_PARSE_FAIL", ex.Message);
                    return false;
                }
            }

            // 3. 原生 MFP 单行社区分享码 (Base64 + GZip)
            if (!clean.StartsWith(CodePrefix))
            {
                error = I18n.Tr("ERR_INVALID_CONFIG_FORMAT", "无效的配置格式 (支持 MFP:v1: 分享码、原始 JSON 文本或本地 .json 文件名)");
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
                    layout = AvionicsConfigParser.ParseLayout(json, out error);

                    if (layout == null || layout.Widgets == null || layout.Widgets.Count == 0)
                    {
                        error = I18n.Tr("ERR_PARSE_NO_WIDGETS", "解析成功但未发现有效小组件配置");
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = I18n.TrFormat("ERR_DECODE_FAIL", ex.Message);
                return false;
            }
        }

        private struct BuiltInPresetMeta
        {
            public string FileName;
            public string NameKey;
            public string FallbackName;
            public string DescKey;
            public string FallbackDesc;
            public string FallbackId;
        }

        private static readonly BuiltInPresetMeta[] KnownBuiltIns = new[]
        {
            new BuiltInPresetMeta { FileName = "01_Default_Avionics.json", NameKey = "PRESET_NAME_DEFAULT", FallbackName = I18n.Tr("PRESET_NAME_DEFAULT", "双翼标准航电"), DescKey = "PRESET_DESC_DEFAULT", FallbackDesc = I18n.Tr("PRESET_DESC_DEFAULT", "经典双翼工效学布局，中央姿态球与伴生仪表，两侧速度/高度标尺带与大动压/G力表"), FallbackId = "default" },
            new BuiltInPresetMeta { FileName = "02_Modern_Glass_Cockpit.json", NameKey = "PRESET_NAME_MODERN", FallbackName = I18n.Tr("PRESET_NAME_MODERN", "现代全玻璃化座舱"), DescKey = "PRESET_DESC_MODERN", FallbackDesc = I18n.Tr("PRESET_DESC_MODERN", "高信息密度玻璃化中控，包含 ELEC 电力分布图与 ROCKET 2D 多级推进栈"), FallbackId = "modern" },
            new BuiltInPresetMeta { FileName = "03_Apollo_Retro_Moon.json", NameKey = "PRESET_NAME_APOLLO", FallbackName = I18n.Tr("PRESET_NAME_APOLLO", "阿波罗复古登月"), DescKey = "PRESET_DESC_APOLLO", FallbackDesc = I18n.Tr("PRESET_DESC_APOLLO", "聚焦登月降落推重比、雷达真高、阶段燃料余量与姿控网格"), FallbackId = "apollo" },
            new BuiltInPresetMeta { FileName = "04_Deep_Space_Probe.json", NameKey = "PRESET_NAME_DEEP_SPACE", FallbackName = I18n.Tr("PRESET_NAME_DEEP_SPACE", "深空无人远征探测"), DescKey = "PRESET_DESC_DEEP_SPACE", FallbackDesc = I18n.Tr("PRESET_DESC_DEEP_SPACE", "深空探测器专用：太阳能净充电监测、CommNet 长波天线阵列与轨道力学参数"), FallbackId = "deep_space" },
            new BuiltInPresetMeta { FileName = "05_SpaceX_Dragon_Cockpit.json", NameKey = "PRESET_NAME_SPACEX", FallbackName = I18n.Tr("PRESET_NAME_SPACEX", "SpaceX 载人龙飞船"), DescKey = "PRESET_DESC_SPACEX", FallbackDesc = I18n.Tr("PRESET_DESC_SPACEX", "极简全息触控座舱：顶部全景遥测横幅、中央对接与姿态准星 HUD、侧边 ECLSS 维生面板与底部药丸触控条"), FallbackId = "spacex" },
            new BuiltInPresetMeta { FileName = "05_SpaceX_Starship_HUD.json", NameKey = "PRESET_NAME_STARSHIP", FallbackName = I18n.Tr("PRESET_NAME_STARSHIP", "SpaceX 星舰抬头显示"), DescKey = "PRESET_DESC_STARSHIP", FallbackDesc = I18n.Tr("PRESET_DESC_STARSHIP", "极简 Starship HUD：弧形过载指示带、对接准星与全景姿态"), FallbackId = "starship" }
        };

        /// <summary>
        /// 获取所有可用预设列表 (包含出厂预设与用户本地文件预设，无重复项)
        /// </summary>
        public static List<PresetInfo> GetAvailablePresets()
        {
            List<PresetInfo> list = new List<PresetInfo>();
            HashSet<string> handledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!Directory.Exists(PresetsDir))
                {
                    Directory.CreateDirectory(PresetsDir);
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn(MFPLogger.CatPresets, $"Error ensuring preset directory: {ex.Message}");
            }

            // 1. 标准出厂精选预设 (自动优先绑定本地 Presets 文件夹中的对应完整 json 文件)
            for (int i = 0; i < KnownBuiltIns.Length; i++)
            {
                var meta = KnownBuiltIns[i];
                string expectedPath = Path.Combine(PresetsDir, meta.FileName);
                bool fileExists = File.Exists(expectedPath);
                if (fileExists) handledFiles.Add(meta.FileName);

                list.Add(new PresetInfo
                {
                    Id = meta.FallbackId,
                    Name = I18n.Tr(meta.NameKey, meta.FallbackName),
                    Description = I18n.Tr(meta.DescKey, meta.FallbackDesc),
                    IsBuiltIn = true,
                    FilePath = fileExists ? expectedPath : null
                });
            }

            // 2. 用户本地自定义预设 (排除已绑定的出厂预设文件)
            try
            {
                if (Directory.Exists(PresetsDir))
                {
                    string[] files = Directory.GetFiles(PresetsDir, "*.json");
                    foreach (string file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        if (handledFiles.Contains(fileName)) continue;

                        string presetName = Path.GetFileNameWithoutExtension(file);
                        list.Add(new PresetInfo
                        {
                            Id = presetName,
                            Name = presetName,
                            Description = I18n.TrFormat("PRESET_DESC_LOCAL", fileName),
                            IsBuiltIn = false,
                            FilePath = file
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MFPLogger.Warn(MFPLogger.CatPresets, $"Error listing preset directory: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// 加载指定预设并返回完整布局 (严格验证小组件数量，杜绝加载空白损坏文件)
        /// </summary>
        public static WidgetLayoutData LoadPreset(PresetInfo preset)
        {
            if (preset == null) return null;

            // 1. 优先从磁盘上的完整预设文件载入 (无论是出厂还是自定义)
            if (!string.IsNullOrEmpty(preset.FilePath) && File.Exists(preset.FilePath))
            {
                try
                {
                    string json = File.ReadAllText(preset.FilePath);
                    var data = AvionicsConfigParser.ParseLayout(json, out string parseErr);
                    if (data != null && data.Widgets != null && data.Widgets.Count > 0)
                    {
                        return data;
                    }
                    MFPLogger.Warn(MFPLogger.CatPresets, $"Preset '{preset.Name}' file exists but contains 0 widgets ({parseErr}). Falling back to built-in generator.");
                }
                catch (Exception ex)
                {
                    MFPLogger.Exception(MFPLogger.CatPresets, ex, "Failed to load preset file from disk");
                }
            }

            // 2. 出厂预设兜底生成
            if (preset.IsBuiltIn)
            {
                return CreateBuiltInPreset(preset.Id ?? preset.Name);
            }

            return null;
        }

        /// <summary>
        /// 将当前布局另存为独立预设文件 (严格拦截空组件配置保存)
        /// </summary>
        public static bool SavePresetToFile(string presetName, WidgetLayoutData layout, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(presetName))
            {
                error = I18n.Tr("ERR_PRESET_NAME_EMPTY", "预设名称不能为空");
                return false;
            }

            if (layout == null || layout.Widgets == null || layout.Widgets.Count == 0)
            {
                error = I18n.Tr("ERR_LAYOUT_EMPTY_INTERCEPTED", "当前小组件布局为空，已自动拦截保存以保护现有模板");
                return false;
            }

            try
            {
                if (!Directory.Exists(PresetsDir)) Directory.CreateDirectory(PresetsDir);
                string cleanName = string.Join("_", presetName.Split(Path.GetInvalidFileNameChars())).Trim();
                string filePath = Path.Combine(PresetsDir, $"{cleanName}.json");

                string json = AvionicsConfigParser.SerializeLayout(layout, true);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static WidgetLayoutData CreateBuiltInPreset(string presetKey)
        {
            WidgetLayoutData data = new WidgetLayoutData { GlobalScale = 1.25f };
            string key = (presetKey ?? string.Empty).ToLowerInvariant();

            if (key.Contains("starship") || key.Contains("星舰"))
            {
                data.Widgets.Add(new WidgetConfig("spacex.attitude", I18n.GetWidgetName("spacex.attitude", "SpaceX 极简水平仪姿态视窗"), 0f, 100f, 1.0f) { WidgetType = "spacex_attitude", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.timeline", I18n.GetWidgetName("spacex.timeline", "SpaceX 时序飞行时间轴"), 0f, -100f, 1.0f) { WidgetType = "spacex_timeline", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.engines", I18n.GetWidgetName("spacex.engines", "SpaceX 引擎阵列工况矩阵"), 0f, -220f, 1.0f) { WidgetType = "spacex_engines", IsEnabled = true });
                return data;
            }

            if (key.Contains("spacex") || key.Contains("dragon") || key.Contains("龙飞船"))
            {
                // SpaceX 载人龙飞船极简全息触控座舱
                data.Widgets.Add(new WidgetConfig("spacex.header", I18n.GetWidgetName("spacex.header", "SpaceX 任务阶段与遥测顶栏"), 0f, 420f, 1.0f) { WidgetType = "spacex_header", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.docking", I18n.GetWidgetName("spacex.docking", "SpaceX 对接与姿态准星 HUD"), 0f, 170f, 1.0f) { WidgetType = "spacex_docking", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "姿态球 (Navball)"), 0f, -40f, 1.0f) { IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.bottom", I18n.GetWidgetName("spacex.bottom", "SpaceX 底部控制与链路栏"), 0f, -150f, 1.0f) { WidgetType = "spacex_bottom", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("spacex.overview", I18n.GetWidgetName("spacex.overview", "SpaceX 综合工况与维生监控"), -460f, 120f, 1.0f) { WidgetType = "spacex_overview", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.nd_navigation", I18n.GetWidgetName("custom.nd_navigation", "AERO ND 综合导航屏"), 460f, 120f, 1.0f) { WidgetType = "nd_navigation", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -220f, 40f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 高度标尺带"), 220f, 40f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                return data;
            }

            // 核心基础组件
            data.Widgets.Add(new WidgetConfig("core.navball", I18n.GetWidgetName("core.navball", "姿态球 (Navball)"), 0f, 0f) { IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.heading_arc", I18n.GetWidgetName("core.heading_arc", "PFD 航向指示弧 (Set 2)"), 0f, 76f) { WidgetType = "heading_arc", IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.bottom_controls", I18n.GetWidgetName("core.bottom_controls", "RCS与SAS底控台"), 0f, -88f) { IsEnabled = true });
            data.Widgets.Add(new WidgetConfig("core.sas_dial", I18n.GetWidgetName("core.sas_dial", "环形 SAS 罗盘"), 0f, -150f) { IsEnabled = true });

            if (key.Contains("modern") || key.Contains("glass") || key.Contains("现代") || key.Contains("玻璃"))
            {
                // 现代全玻璃化：开启两侧子系统
                data.Widgets.Add(new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 高度标尺带"), 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.electrical", I18n.GetWidgetName("custom.electrical", "ELEC 电力分配系统"), -440f, 160f, 1.0f) { WidgetType = "electrical", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.rocket", I18n.GetWidgetName("custom.rocket", "ROCKET 2D 分级姿态卡"), 440f, 160f, 1.0f) { WidgetType = "rocket2d", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("gauge.throttle", I18n.GetWidgetName("gauge.throttle", "AVIONICS 油门推力带"), -158f, 0f, 1.0f) { WidgetType = "bar_gauge", NumericToken = "{THROTTLE}", MinValue = 0, MaxValue = 100, CautionThreshold = 85, WarningThreshold = 100, UnitLabel = "%", IsLeftOrientation = true, IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("gauge.barometer", I18n.GetWidgetName("gauge.barometer", "AVIONICS 大气压强带"), 158f, 0f, 1.0f) { WidgetType = "bar_gauge", NumericToken = "{Q}", MinValue = 0, MaxValue = 35, CautionThreshold = 20, WarningThreshold = 28, UnitLabel = "kPa", IsLeftOrientation = false, IsEnabled = true });
            }
            else if (key.Contains("apollo") || key.Contains("登月") || key.Contains("阿波罗"))
            {
                // 阿波罗复古：强调推重比与降落真高
                data.Widgets.Add(new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD:SURF}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 雷达真高带"), 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT:AGL}", StepInterval = 50f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("gauge.throttle", I18n.GetWidgetName("gauge.throttle", "AVIONICS 油门推力带"), -158f, 0f, 1.0f) { WidgetType = "bar_gauge", NumericToken = "{THROTTLE}", MinValue = 0, MaxValue = 100, CautionThreshold = 85, WarningThreshold = 100, UnitLabel = "%", IsLeftOrientation = true, IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.life", I18n.GetWidgetName("custom.life", "LIFE SUPPORT 维生监控"), -440f, -80f, 1.0f) { WidgetType = "life_support", IsEnabled = true });
            }
            else if (key.Contains("deep") || key.Contains("probe") || key.Contains("深空") || key.Contains("探测"))
            {
                // 深空探测：强调太阳能电力与天线通信
                data.Widgets.Add(new WidgetConfig("custom.electrical", I18n.GetWidgetName("custom.electrical", "ELEC 电力分配系统"), -440f, 140f, 1.0f) { WidgetType = "electrical", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("custom.signal", I18n.GetWidgetName("custom.signal", "COMMNET 天线通信网络"), 440f, 140f, 1.0f) { WidgetType = "signal", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("core.stage_control", I18n.GetWidgetName("core.stage_control", "分级操纵台"), -360f, -120f, 1.0f) { IsEnabled = true });
            }
            else
            {
                // 双翼标准预设
                data.Widgets.Add(new WidgetConfig("tape.speed", I18n.GetWidgetName("tape.speed", "PFD 速度标尺带"), -225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{SPD}", StepInterval = 10f, IsLeftOrientation = true, UnitLabel = "m/s", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("tape.altitude", I18n.GetWidgetName("tape.altitude", "PFD 高度标尺带"), 225f, 0f, 1.0f) { WidgetType = "tape", NumericToken = "{ALT}", StepInterval = 100f, IsLeftOrientation = false, UnitLabel = "m", IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("gauge.throttle", I18n.GetWidgetName("gauge.throttle", "AVIONICS 油门推力带"), -158f, 0f, 1.0f) { WidgetType = "bar_gauge", NumericToken = "{THROTTLE}", MinValue = 0, MaxValue = 100, CautionThreshold = 85, WarningThreshold = 100, UnitLabel = "%", IsLeftOrientation = true, IsEnabled = true });
                data.Widgets.Add(new WidgetConfig("gauge.barometer", I18n.GetWidgetName("gauge.barometer", "AVIONICS 大气压强带"), 158f, 0f, 1.0f) { WidgetType = "bar_gauge", NumericToken = "{Q}", MinValue = 0, MaxValue = 35, CautionThreshold = 20, WarningThreshold = 28, UnitLabel = "kPa", IsLeftOrientation = false, IsEnabled = true });
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
                error = I18n.Tr("ERR_THEME_CODE_EMPTY", "主题分享码为空");
                return false;
            }

            string clean = shareCode.Trim();
            if (!clean.StartsWith(CodePrefix))
            {
                error = I18n.Tr("ERR_THEME_CODE_INVALID", "无效的主题分享码格式 (必须以 MFP-THEME:v1: 开头)");
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
                        error = I18n.Tr("ERR_THEME_NO_ID", "解析成功但未发现有效主题 ID");
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = I18n.TrFormat("ERR_THEME_DECODE_FAIL", ex.Message);
                return false;
            }
        }
    }
}
