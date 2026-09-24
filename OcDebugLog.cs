using System;
using System.Collections.Generic;
using System.IO;

namespace FPSOverlay
{
    public enum OcLogCategory
    {
        General,
        Config,
        Oc,
        Fan,
        Sensor,
        Library,
        Update,
        Crash
    }

    /// <summary>Technical OC/AI diagnostics — file only, never dump this on main UI.</summary>
    public static class OcDebugLog
    {
        private static readonly object Sync = new();
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly Dictionary<string, DateTime> RateLimit = new(StringComparer.Ordinal);

        public static string LogPath => AppPaths.DebugLogPath;

        public static string LogDirectory => AppPaths.Root;

        public static void Log(string message) => Write(OcLogCategory.General, message);

        public static void Log(OcLogCategory category, string message) => Write(category, message);

        public static void LogError(string message, Exception? ex = null)
            => LogError(OcLogCategory.General, message, ex);

        public static void LogError(OcLogCategory category, string message, Exception? ex = null)
        {
            if (ex == null)
            {
                Write(category, "[ERROR] " + message);
                return;
            }

            string brief = ex.GetType().Name + ": " + ex.Message;
            Write(category, "[ERROR] " + message + " · " + brief);
        }

        /// <summary>Layer-B poll noise control: same key at most once per window.</summary>
        public static void LogRateLimited(OcLogCategory category, string key, string message, TimeSpan window)
        {
            lock (Sync)
            {
                string fullKey = category + "|" + key;
                if (RateLimit.TryGetValue(fullKey, out var last) && DateTime.UtcNow - last < window)
                    return;
                RateLimit[fullKey] = DateTime.UtcNow;
            }
            Write(category, message);
        }

        public static void Write(string message) => Write(OcLogCategory.General, message);

        public static void Write(OcLogCategory category, string message)
        {
            try
            {
                lock (Sync)
                {
                    AppPaths.EnsureRoot();
                    RotateIfNeeded_NoLock();
                    string prefix = category == OcLogCategory.General
                        ? ""
                        : $"[{category.ToString().ToUpperInvariant()}] ";
                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {prefix}{message}{Environment.NewLine}");
                }
            }
            catch { /* logging must NEVER break the UI */ }
        }

        private static void RotateIfNeeded_NoLock()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                var info = new FileInfo(LogPath);
                if (info.Length < MaxBytes) return;
                string bak = LogPath + ".1";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(LogPath, bak);
            }
            catch { /* ignore rotate failures */ }
        }
    }
}
