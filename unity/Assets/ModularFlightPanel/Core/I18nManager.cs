using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 语言元数据描述
    /// </summary>
    public class LanguageInfo
    {
        public string Code { get; set; } = "en-US";
        public string DisplayName { get; set; } = "English";
        public string NativeName { get; set; } = "English";
        public string FilePath { get; set; } = string.Empty;

        public LanguageInfo() { }

        public LanguageInfo(string code, string displayName, string nativeName, string filePath = "")
        {
            Code = code;
            DisplayName = displayName;
            NativeName = nativeName;
            FilePath = filePath;
        }

        public override string ToString() => $"{DisplayName} ({Code})";
    }

    /// <summary>
    /// 全局国际化与多语言管理中枢 (I18n Localization Manager)
    /// 核心职责：
    /// 1. 纯 C# 解耦架构：自动从 GameData/ModularFlightPanel/Localization/*.json 载入语言字典。
    /// 2. 动静结合双重安全网：内置高可靠兜底字典 (Built-in Fallbacks)，即使文件损坏或缺失亦绝无空字与异常。
    /// 3. 全局语言变更即刻响应：触发 OnLanguageChanged 事件，驱动 UI 设置面板与全量飞行仪表瞬态重绘。
    /// 4. 零开销查询与格式化支持：支持 I18n.Tr(key) 及 I18n.TrFormat(key, args)。
    /// </summary>
    public class I18nManager
    {
        private static I18nManager _instance;
        public static I18nManager Instance => _instance ?? (_instance = new I18nManager());

        public static event Action<string> OnLanguageChanged;

        private readonly Dictionary<string, LanguageInfo> _availableLanguages = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _loadedDictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private string _currentLanguage = "zh-CN";
        public string CurrentLanguage => _currentLanguage;

        public List<LanguageInfo> AvailableLanguages => new List<LanguageInfo>(_availableLanguages.Values);

        private Dictionary<string, string> _activeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 静态内置兜底字典 (Built-in Fallbacks)
        private static readonly Dictionary<string, string> BuiltinZhCN = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> BuiltinEnUS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string LocalizationDirectory => Path.Combine(AppPathHelper.RootPath, "GameData/ModularFlightPanel/Localization");

        static I18nManager()
        {
            I18nJsonParser.OnLogWarning = msg => MFPLogger.Warn(MFPLogger.CatUI, msg);
            PopulateBuiltinDictionaries();
        }

        public I18nManager()
        {
            Initialize();
        }

        public void Initialize()
        {
            DiscoverAvailableLanguages();

            // 自动侦测语言偏好
            string detectedLang = DetectSystemLanguage();
            SetLanguage(detectedLang, false);
        }

        public void Reload()
        {
            _loadedDictionaries.Clear();
            DiscoverAvailableLanguages();
            SetLanguage(_currentLanguage, true);
        }

        private void DiscoverAvailableLanguages()
        {
            _availableLanguages.Clear();

            // 1. 注册核心内置语言
            _availableLanguages["zh-CN"] = new LanguageInfo("zh-CN", "简体中文", "简体中文");
            _availableLanguages["en-US"] = new LanguageInfo("en-US", "English", "English");

            // 2. 扫描磁盘 Localization 目录下的所有 .json 语言文件
            string locDir = LocalizationDirectory;
            if (Directory.Exists(locDir))
            {
                string[] files = Directory.GetFiles(locDir, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    try
                    {
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        var dict = I18nJsonParser.Parse(content, out string code, out string dispName, out string nativeName);
                        if (!string.IsNullOrEmpty(code))
                        {
                            _availableLanguages[code] = new LanguageInfo(code, dispName, nativeName, file);
                            _loadedDictionaries[code] = dict;
                        }
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Warn(MFPLogger.CatUI, $"Failed to parse localization file '{file}': {ex.Message}");
                    }
                }
            }
        }

        public string DetectSystemLanguage()
        {
            // 1. 尝试从 KSP 原生配置读取
            try
            {
                Type gsType = Type.GetType("GameSettings, Assembly-CSharp");
                if (gsType != null)
                {
                    var field = gsType.GetField("LANGUAGE", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (field != null)
                    {
                        string kspLang = field.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(kspLang))
                        {
                            if (kspLang.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                kspLang.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return "zh-CN";
                            }
                            return "en-US";
                        }
                    }
                }
            }
            catch { }

            // 2. 尝试从操作系统文化读取
            try
            {
                var culture = System.Globalization.CultureInfo.CurrentUICulture;
                if (culture != null && culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return "zh-CN";
                }
            }
            catch { }

            return "zh-CN"; // MFP 优先默认中文 (原生开发者母语)
        }

        public bool SetLanguage(string languageCode, bool notify = true)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;

            // 规范化语言编码
            string code = NormalizeLanguageCode(languageCode);

            // 若尚未载入该语言字典，按需从文件载入
            if (!_loadedDictionaries.TryGetValue(code, out var dict))
            {
                string filePath = Path.Combine(LocalizationDirectory, $"{code}.json");
                if (File.Exists(filePath))
                {
                    try
                    {
                        string content = File.ReadAllText(filePath, Encoding.UTF8);
                        dict = I18nJsonParser.Parse(content, out _, out _, out _);
                        _loadedDictionaries[code] = dict;
                    }
                    catch (Exception ex)
                    {
                        MFPLogger.Warn(MFPLogger.CatUI, $"Failed to load language '{code}' from disk: {ex.Message}");
                    }
                }

                if (dict == null)
                {
                    // 若无文件，使用内置字典
                    dict = code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? BuiltinZhCN : BuiltinEnUS;
                    _loadedDictionaries[code] = dict;
                }
            }

            _activeDict = dict ?? BuiltinZhCN;
            _currentLanguage = code;

            if (notify)
            {
                OnLanguageChanged?.Invoke(_currentLanguage);
                MFPLogger.Info(MFPLogger.CatUI, $"Language switched to: {_currentLanguage}");
            }

            return true;
        }

        private string NormalizeLanguageCode(string rawCode)
        {
            if (string.IsNullOrEmpty(rawCode)) return "zh-CN";
            string clean = rawCode.Trim().Replace('_', '-');
            if (clean.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (clean.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
            return clean;
        }

        /// <summary>
        /// 翻译指定键名字符串
        /// </summary>
        public static string Tr(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;

            var mgr = Instance;
            // 1. 优先从当前活动字典查表
            if (mgr._activeDict != null && mgr._activeDict.TryGetValue(key, out string val) && !string.IsNullOrEmpty(val))
            {
                return val;
            }

            // 2. 从当前语言内置兜底字典查表
            var builtin = mgr._currentLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? BuiltinZhCN : BuiltinEnUS;
            if (builtin.TryGetValue(key, out string builtinVal) && !string.IsNullOrEmpty(builtinVal))
            {
                return builtinVal;
            }

            // 3. 从英文内置兜底字典查表
            if (BuiltinEnUS.TryGetValue(key, out string enVal) && !string.IsNullOrEmpty(enVal))
            {
                return enVal;
            }

            // 4. 返回显式回退值或键名本身
            return fallback ?? key;
        }

        /// <summary>
        /// 带格式化参数的翻译
        /// </summary>
        public static string TrFormat(string key, params object[] args)
        {
            string template = Tr(key);
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }

        /// <summary>
        /// 智能获取组件的本地化显示名称
        /// </summary>
        public static string GetWidgetName(string widgetId, string defaultDisplayName)
        {
            if (string.IsNullOrEmpty(widgetId)) return defaultDisplayName ?? string.Empty;

            string key = "WIDGET_NAME_" + widgetId.Replace('.', '_').ToUpperInvariant();
            if (Instance._activeDict != null && Instance._activeDict.TryGetValue(key, out string trName) && !string.IsNullOrEmpty(trName))
            {
                return trName;
            }

            return defaultDisplayName ?? widgetId;
        }

        private static void PopulateBuiltinDictionaries()
        {
            // ==========================================
            // 中文内置核心字典 (Built-in zh-CN)
            // ==========================================
            BuiltinZhCN["UI_WORKBENCH_TITLE"] = "MODULAR FLIGHT PANEL";
            BuiltinZhCN["UI_WORKBENCH_SUBTITLE"] = "航电工程工作台";
            BuiltinZhCN["UI_VESSEL"] = "载具";
            BuiltinZhCN["UI_REF_FRAME"] = "参考系";
            BuiltinZhCN["UI_DRAG_MODE_ACTIVE"] = "🎯 [拖拽模式中] 点击锁定";
            BuiltinZhCN["UI_DRAG_MODE_IDLE"] = "🎯 [开启自由拖拽]";
            BuiltinZhCN["UI_NAVBALL_ON"] = "🌐 姿态球: 开";
            BuiltinZhCN["UI_NAVBALL_OFF"] = "🌐 姿态球: 关";
            BuiltinZhCN["UI_TAB_LIBRARY"] = "📦 航电库";
            BuiltinZhCN["UI_TAB_ASSEMBLER"] = "🛠️ 遥测装配台";
            BuiltinZhCN["UI_TAB_MANAGER"] = "📋 挂载清单";
            BuiltinZhCN["UI_TAB_THEMES"] = "🎨 视觉风格";
            BuiltinZhCN["UI_TAB_PROFILES"] = "💾 档案与配置";
            BuiltinZhCN["UI_TAB_SANDBOX"] = "🚀 仿真沙盒";
            BuiltinZhCN["UI_FOOTER_STATUS"] = "当前布局: <b>{0}</b> 个组件 | 快捷键: <b>Alt+N / ESC</b> 关闭 | <b>F2</b> 隐藏全UI | <b>F10</b> 性能HUD | <b>F11</b> 纯净旁路";
            BuiltinZhCN["UI_SAVE_AND_CLOSE"] = "✔ 保存配置并关闭 (Alt+N)";

            BuiltinZhCN["WIDGET_ALERT_SEPARATION"] = "分  离";
            BuiltinZhCN["WIDGET_ALERT_ENGINE_START"] = "引擎启动";
            BuiltinZhCN["WIDGET_ALERT_CAUTION"] = "注意";
            BuiltinZhCN["WIDGET_ALERT_WARNING"] = "危急";
            BuiltinZhCN["WIDGET_ALERT_READY"] = "就绪";
            BuiltinZhCN["WIDGET_ALERT_ARMED"] = "待命";
            BuiltinZhCN["WIDGET_ALERT_NORM"] = "正常";

            // ==========================================
            // 英文内置核心字典 (Built-in en-US)
            // ==========================================
            BuiltinEnUS["UI_WORKBENCH_TITLE"] = "MODULAR FLIGHT PANEL";
            BuiltinEnUS["UI_WORKBENCH_SUBTITLE"] = "Avionics Workbench";
            BuiltinEnUS["UI_VESSEL"] = "Vessel";
            BuiltinEnUS["UI_REF_FRAME"] = "Frame";
            BuiltinEnUS["UI_DRAG_MODE_ACTIVE"] = "🎯 [Drag Mode] Click to Lock";
            BuiltinEnUS["UI_DRAG_MODE_IDLE"] = "🎯 [Enable Drag Mode]";
            BuiltinEnUS["UI_NAVBALL_ON"] = "🌐 Navball: ON";
            BuiltinEnUS["UI_NAVBALL_OFF"] = "🌐 Navball: OFF";
            BuiltinEnUS["UI_TAB_LIBRARY"] = "📦 Library";
            BuiltinEnUS["UI_TAB_ASSEMBLER"] = "🛠️ Assembler";
            BuiltinEnUS["UI_TAB_MANAGER"] = "📋 Manager";
            BuiltinEnUS["UI_TAB_THEMES"] = "🎨 Themes";
            BuiltinEnUS["UI_TAB_PROFILES"] = "💾 Profiles";
            BuiltinEnUS["UI_TAB_SANDBOX"] = "🚀 Sandbox";
            BuiltinEnUS["UI_FOOTER_STATUS"] = "Active Layout: <b>{0}</b> widgets | Hotkeys: <b>Alt+N / ESC</b> Close | <b>F2</b> Hide UI | <b>F10</b> Profiler | <b>F11</b> Bypass";
            BuiltinEnUS["UI_SAVE_AND_CLOSE"] = "✔ Save & Close (Alt+N)";

            BuiltinEnUS["WIDGET_ALERT_SEPARATION"] = "SEPARATION";
            BuiltinEnUS["WIDGET_ALERT_ENGINE_START"] = "ENGINE START";
            BuiltinEnUS["WIDGET_ALERT_CAUTION"] = "CAUTION";
            BuiltinEnUS["WIDGET_ALERT_WARNING"] = "WARNING";
            BuiltinEnUS["WIDGET_ALERT_READY"] = "READY";
            BuiltinEnUS["WIDGET_ALERT_ARMED"] = "ARMED";
            BuiltinEnUS["WIDGET_ALERT_NORM"] = "NORM";
        }
    }

    /// <summary>
    /// 全局极简翻译快捷语法糖 (I18n Syntactic Sugar)
    /// </summary>
    public static class I18n
    {
        public static string Tr(string key, string fallback = null) => I18nManager.Tr(key, fallback);
        public static string TrFormat(string key, params object[] args) => I18nManager.TrFormat(key, args);
        public static string GetWidgetName(string widgetId, string defaultDisplayName) => I18nManager.GetWidgetName(widgetId, defaultDisplayName);
    }
}
