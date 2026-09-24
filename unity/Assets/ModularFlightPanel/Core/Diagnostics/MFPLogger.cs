using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace ModularFlightPanel.Core
{
    /// <summary>
    /// 日志分级严重度 (Log Severity Level)
    /// </summary>
    public enum LogLevel
    {
        Verbose = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Warning = 3,
        Error = 4,
        Exception = 5,
        None = 6
    }

    /// <summary>
    /// 结构化日志快照条目 (Structured Log Entry)
    /// </summary>
    public struct LogEntry
    {
        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public string StackTrace { get; }

        public LogEntry(DateTime timestamp, LogLevel level, string category, string message, string stackTrace = null)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
            StackTrace = stackTrace;
        }

        public override string ToString()
        {
            string catStr = string.IsNullOrEmpty(Category) ? "" : $":{Category}";
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [MFP{catStr}] [{Level.ToString().ToUpperInvariant()}] {Message}";
        }
    }

    /// <summary>
    /// ModularFlightPanel 核心日志中枢 (Centralized Logging Hub)
    /// 集中管理全项目的分级日志、内存环形缓冲区、高频调用限流防刷、文件滚动持久化与 Unity/KSP 控制台桥接。
    /// </summary>
    public static class MFPLogger
    {
        // ==========================================
        // 预定义模块分类常量 (Subsystem Categories)
        // ==========================================
        public const string CatCore = "Core";
        public const string CatUI = "UI";
        public const string CatTelemetry = "Telemetry";
        public const string CatNavball = "Navball";
        public const string CatTheme = "Theme";
        public const string CatPresets = "Presets";
        public const string CatProfiler = "Profiler";
        public const string CatRender = "Render";
        public const string CatIO = "IO";
        public const string CatSimulation = "Sim";

        // ==========================================
        // 配置与运行时开关 (Configuration)
        // ==========================================
        private static LogLevel _minLevel = LogLevel.Info;
        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static bool IsConsoleLoggingEnabled { get; set; } = true;
        public static bool IsFileLoggingEnabled { get; set; } = true;
        public static int MaxBufferCapacity { get; set; } = 500;

        // 快速判断通道开闭 (避免无谓的字符串插值分配)
        public static bool IsVerboseEnabled => _minLevel <= LogLevel.Verbose;
        public static bool IsDebugEnabled => _minLevel <= LogLevel.Debug;
        public static bool IsInfoEnabled => _minLevel <= LogLevel.Info;
        public static bool IsWarnEnabled => _minLevel <= LogLevel.Warn;
        public static bool IsErrorEnabled => _minLevel <= LogLevel.Error;

        // ==========================================
        // 内存环形缓冲与事件中枢 (In-Memory Buffer)
        // ==========================================
        private static readonly object _bufferLock = new object();
        private static readonly Queue<LogEntry> _recentLogs = new Queue<LogEntry>();
        public static event Action<LogEntry> OnLogEmitted;

        // ==========================================
        // 高频调用限流防刷屏字典 (Anti-Spam Throttling)
        // ==========================================
        private static readonly object _throttleLock = new object();
        private static readonly Dictionary<string, long> _throttleTicks = new Dictionary<string, long>();

        // ==========================================
        // 本地日志文件持久化 (File Logging & Rotation)
        // ==========================================
        private static readonly object _fileLock = new object();
        private static string _cachedLogFilePath = null;
        private static bool _sessionHeaderWritten = false;
        private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB 自动滚动

        public static string LogFilePath
        {
            get
            {
                if (_cachedLogFilePath == null)
                {
                    try
                    {
                        string dir = Path.Combine(AppPathHelper.RootPath, "GameData", "ModularFlightPanel", "PluginData", "Logs");
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        _cachedLogFilePath = Path.Combine(dir, "mfp.log");
                    }
                    catch
                    {
                        _cachedLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mfp.log");
                    }
                }
                return _cachedLogFilePath;
            }
        }

        // ==========================================
        // 核心日志发射入口 (Emit Core Pipeline)
        // ==========================================
        public static void Log(LogLevel level, string category, string message, string stackTrace = null)
        {
            if (level < _minLevel || level == LogLevel.None) return;

            DateTime now = DateTime.Now;
            LogEntry entry = new LogEntry(now, level, category, message, stackTrace);

            // 1. 推入内存环形缓冲
            lock (_bufferLock)
            {
                if (_recentLogs.Count >= MaxBufferCapacity)
                {
                    _recentLogs.Dequeue();
                }
                _recentLogs.Enqueue(entry);
            }

            // 2. 触发外部侦听器（例如在飞行界面的调试面板或 Toast 提示）
            try
            {
                OnLogEmitted?.Invoke(entry);
            }
            catch { }

            // 3. 桥接至 Unity / KSP 控制台
            if (IsConsoleLoggingEnabled)
            {
                ForwardToUnityConsole(entry);
            }

            // 4. 持久化写入本地滚动文件
            if (IsFileLoggingEnabled)
            {
                AppendToFile(entry);
            }
        }

        private static void ForwardToUnityConsole(LogEntry entry)
        {
            string catTag = string.IsNullOrEmpty(entry.Category) ? "[ModularFlightPanel]" : $"[ModularFlightPanel:{entry.Category}]";
            string formatted = $"{catTag} {entry.Message}";

            switch (entry.Level)
            {
                case LogLevel.Verbose:
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(formatted);
                    break;
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(formatted);
                    break;
                case LogLevel.Error:
                    if (!string.IsNullOrEmpty(entry.StackTrace))
                    {
                        UnityEngine.Debug.LogError($"{formatted}\n{entry.StackTrace}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError(formatted);
                    }
                    break;
                case LogLevel.Exception:
                    UnityEngine.Debug.LogError($"{formatted}\n{entry.StackTrace}");
                    break;
            }
        }

        private static void AppendToFile(LogEntry entry)
        {
            lock (_fileLock)
            {
                try
                {
                    string path = LogFilePath;
                    if (string.IsNullOrEmpty(path)) return;

                    // 检查文件滚动 (Rotation)
                    if (File.Exists(path))
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length > MaxFileSizeBytes)
                        {
                            string oldPath = path + ".old";
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(path, oldPath);
                            _sessionHeaderWritten = false;
                        }
                    }

                    using (var sw = new StreamWriter(path, true, Encoding.UTF8))
                    {
                        if (!_sessionHeaderWritten)
                        {
                            sw.WriteLine("================================================================================");
                            sw.WriteLine($"[ModularFlightPanel] Unified Avionics Log Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            sw.WriteLine($"[OS]: {SystemInfo.operatingSystem} | [Unity]: {Application.unityVersion}");
                            sw.WriteLine("================================================================================");
                            _sessionHeaderWritten = true;
                        }

                        sw.WriteLine(entry.ToString());
                        if (!string.IsNullOrEmpty(entry.StackTrace))
                        {
                            sw.WriteLine($"    [StackTrace]: {entry.StackTrace}");
                        }
                    }
                }
                catch
                {
                    // 即使磁盘不可写亦不可向外抛出未捕获异常
                }
            }
        }

        // ==========================================
        // 常用分级快捷调用 (Public API)
        // ==========================================
        public static void Verbose(string message) => Log(LogLevel.Verbose, null, message);
        public static void Verbose(string category, string message) => Log(LogLevel.Verbose, category, message);

        public static void Debug(string message) => Log(LogLevel.Debug, null, message);
        public static void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
        public static void DebugFormat(string category, string format, params object[] args)
        {
            if (IsDebugEnabled) Log(LogLevel.Debug, category, string.Format(format, args));
        }

        public static void Info(string message) => Log(LogLevel.Info, null, message);
        public static void Info(string category, string message) => Log(LogLevel.Info, category, message);
        public static void InfoFormat(string category, string format, params object[] args)
        {
            if (IsInfoEnabled) Log(LogLevel.Info, category, string.Format(format, args));
        }

        public static void Warn(string message) => Log(LogLevel.Warn, null, message);
        public static void Warn(string category, string message) => Log(LogLevel.Warn, category, message);
        public static void Warning(string message) => Log(LogLevel.Warn, null, message);
        public static void Warning(string category, string message) => Log(LogLevel.Warn, category, message);
        public static void WarnFormat(string category, string format, params object[] args)
        {
            if (IsWarnEnabled) Log(LogLevel.Warn, category, string.Format(format, args));
        }

        public static void Error(string message) => Log(LogLevel.Error, null, message);
        public static void Error(string category, string message) => Log(LogLevel.Error, category, message);
        public static void ErrorFormat(string category, string format, params object[] args)
        {
            if (IsErrorEnabled) Log(LogLevel.Error, category, string.Format(format, args));
        }

        public static void Exception(Exception ex, string contextMessage = null)
        {
            if (ex == null) return;
            string msg = string.IsNullOrEmpty(contextMessage)
                ? $"Exception: {ex.GetType().Name}: {ex.Message}"
                : $"{contextMessage} -> {ex.GetType().Name}: {ex.Message}";
            Log(LogLevel.Exception, null, msg, ex.StackTrace);
        }

        public static void Exception(string category, Exception ex, string contextMessage = null)
        {
            if (ex == null) return;
            string msg = string.IsNullOrEmpty(contextMessage)
                ? $"Exception: {ex.GetType().Name}: {ex.Message}"
                : $"{contextMessage} -> {ex.GetType().Name}: {ex.Message}";
            Log(LogLevel.Exception, category, msg, ex.StackTrace);
        }

        // ==========================================
        // 高频防刷限流日志 (Throttled Logging for 60Hz Loops)
        // ==========================================
        /// <summary>
        /// 限流日志输出：同一 throttleKey 在指定 intervalSeconds 内仅打印一次，避免在 60Hz 帧循环中轰炸控制台
        /// </summary>
        public static bool LogThrottled(LogLevel level, string throttleKey, string category, string message, float intervalSeconds = 1.0f)
        {
            if (level < _minLevel || level == LogLevel.None) return false;

            long nowTicks = Stopwatch.GetTimestamp();
            long intervalTicks = (long)(intervalSeconds * Stopwatch.Frequency);

            lock (_throttleLock)
            {
                if (_throttleTicks.TryGetValue(throttleKey, out long lastTicks))
                {
                    if (nowTicks - lastTicks < intervalTicks)
                    {
                        return false; // 限流拦截
                    }
                }
                _throttleTicks[throttleKey] = nowTicks;
            }

            Log(level, category, message);
            return true;
        }

        public static bool WarnThrottled(string throttleKey, string message, float intervalSeconds = 1.0f)
        {
            return LogThrottled(LogLevel.Warn, throttleKey, null, message, intervalSeconds);
        }

        public static bool WarnThrottled(string throttleKey, string category, string message, float intervalSeconds = 1.0f)
        {
            return LogThrottled(LogLevel.Warn, throttleKey, category, message, intervalSeconds);
        }

        public static bool ErrorThrottled(string throttleKey, string message, float intervalSeconds = 1.0f)
        {
            return LogThrottled(LogLevel.Error, throttleKey, null, message, intervalSeconds);
        }

        public static bool ErrorThrottled(string throttleKey, string category, string message, float intervalSeconds = 1.0f)
        {
            return LogThrottled(LogLevel.Error, throttleKey, category, message, intervalSeconds);
        }

        // ==========================================
        // 内存日志快照与维护 (Diagnostic Utilities)
        // ==========================================
        /// <summary>
        /// 获取当前内存环形缓冲内的全部日志快照（供调试工作台或检视器使用）
        /// </summary>
        public static List<LogEntry> GetRecentLogs()
        {
            lock (_bufferLock)
            {
                return new List<LogEntry>(_recentLogs);
            }
        }

        /// <summary>
        /// 清空内存中的日志记录
        /// </summary>
        public static void ClearMemoryLogs()
        {
            lock (_bufferLock)
            {
                _recentLogs.Clear();
            }
        }
    }

    /// <summary>
    /// 兼容用户命名习惯的静态全小写别名类 (Alias for MFPlogger)
    /// </summary>
    public static class MFPlogger
    {
        public static LogLevel MinLevel
        {
            get => MFPLogger.MinLevel;
            set => MFPLogger.MinLevel = value;
        }

        public static void Verbose(string msg) => MFPLogger.Verbose(msg);
        public static void Verbose(string cat, string msg) => MFPLogger.Verbose(cat, msg);

        public static void Debug(string msg) => MFPLogger.Debug(msg);
        public static void Debug(string cat, string msg) => MFPLogger.Debug(cat, msg);

        public static void Info(string msg) => MFPLogger.Info(msg);
        public static void Info(string cat, string msg) => MFPLogger.Info(cat, msg);

        public static void Warn(string msg) => MFPLogger.Warn(msg);
        public static void Warn(string cat, string msg) => MFPLogger.Warn(cat, msg);
        public static void Warning(string msg) => MFPLogger.Warning(msg);
        public static void Warning(string cat, string msg) => MFPLogger.Warning(cat, msg);

        public static void Error(string msg) => MFPLogger.Error(msg);
        public static void Error(string cat, string msg) => MFPLogger.Error(cat, msg);

        public static void Exception(Exception ex, string msg = null) => MFPLogger.Exception(ex, msg);
        public static void Exception(string cat, Exception ex, string msg = null) => MFPLogger.Exception(cat, ex, msg);

        public static bool WarnThrottled(string key, string msg, float interval = 1f) => MFPLogger.WarnThrottled(key, msg, interval);
        public static bool ErrorThrottled(string key, string msg, float interval = 1f) => MFPLogger.ErrorThrottled(key, msg, interval);

        public static List<LogEntry> GetRecentLogs() => MFPLogger.GetRecentLogs();
        public static void ClearMemoryLogs() => MFPLogger.ClearMemoryLogs();
    }
}
