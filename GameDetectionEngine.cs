using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FPSOverlay
{
    public sealed class GameDetectionResult
    {
        /// <summary>True while a game is latched — includes the exit hysteresis window.</summary>
        public bool IsGameActive { get; init; }

        /// <summary>Raw condition snapshot for this tick (no hysteresis).</summary>
        public bool ConditionsMet { get; init; }

        public string? ProcessName { get; init; }
        public string Reason { get; init; } = "";
        public float Gpu3DLoadPercent { get; init; }
    }

    /// <summary>
    /// Process + fullscreen + GPU 3D load inspector for Auto Smart OC.
    /// Exit path uses a 10s hysteresis so Alt-Tab doesn't yank clocks instantly.
    /// </summary>
    public sealed class GameDetectionEngine
    {
        public float MinGpu3DLoadPercent { get; set; } = 30f;
        public int ExitHysteresisSec { get; set; } = 10;

        private bool _latchedActive;
        private DateTime? _conditionsLostUtc;
        private string? _latchedProcessName;

        private uint _cachedPid;
        private string? _cachedProcessName;
        private DateTime _cacheUtc = DateTime.MinValue;
        private static readonly TimeSpan ProcessCacheTtl = TimeSpan.FromSeconds(2);

        private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // shell / system
            "explorer", "SearchHost", "SearchUI", "ShellExperienceHost", "StartMenuExperienceHost",
            "ApplicationFrameHost", "SystemSettings", "TextInputHost", "LockApp", "LogonUI",
            "dwm", "sihost", "taskmgr", "Taskmgr", "RuntimeBroker", "ApplicationFrameHost",
            "Widgets", "WidgetService", "PhoneExperienceHost", "GameBar", "GameBarFTServer",
            "XboxGameBar", "XboxApp", "WinStore.App", "Video.UI",

            // browsers
            "chrome", "msedge", "msedgewebview2", "firefox", "opera", "brave", "vivaldi",
            "iexplore", "waterfox", "librewolf",

            // chat / productivity / our stack
            "Discord", "DiscordPTB", "DiscordCanary", "Slack", "Telegram", "WhatsApp",
            "Teams", "ms-teams", "OUTLOOK", "WINWORD", "EXCEL", "POWERPNT",
            "Code", "Cursor", "devenv", "notepad", "Notepad", "mspaint",
            "FPSOverlay", "RTSS", "RTSSHooksLoader64", "EncoderServer",
            "MSIAfterburner", "GPUTweakII", "PrecisionX_x64", "FanControl",

            // launchers (not the game itself)
            "steam", "steamwebhelper", "SteamService", "EpicGamesLauncher", "EpicWebHelper",
            "Battle.net", "Agent", "GalaxyClient", "Origin", "EADesktop", "UbisoftConnect",
            "upc", "IGOProxy64"
        };

        public bool IsGameRunning => _latchedActive;
        public string? ActiveGameProcessName => _latchedActive ? _latchedProcessName : null;

        public void Reset()
        {
            _latchedActive = false;
            _conditionsLostUtc = null;
            _latchedProcessName = null;
            _cachedPid = 0;
            _cachedProcessName = null;
        }

        public GameDetectionResult Evaluate(Func<float> getGpu3DLoad, DateTime utcNow)
        {
            float gpuLoad = 0f;
            try { gpuLoad = getGpu3DLoad(); } catch { gpuLoad = 0f; }

            var inspect = InspectForeground(utcNow);
            bool fullscreenOk = inspect.IsFullscreenOrBorderless;
            bool processOk = inspect.ProcessName != null && !IsExcluded(inspect.ProcessName);
            bool gpuOk = gpuLoad > MinGpu3DLoadPercent;
            bool conditionsMet = processOk && fullscreenOk && gpuOk;

            string rawReason;
            if (!processOk)
                rawReason = inspect.ProcessName == null
                    ? "no foreground process"
                    : $"excluded process ({inspect.ProcessName}.exe)";
            else if (!fullscreenOk)
                rawReason = $"not fullscreen ({inspect.WindowW}x{inspect.WindowH} vs {inspect.ScreenW}x{inspect.ScreenH})";
            else if (!gpuOk)
                rawReason = $"GPU 3D {gpuLoad:F0}% ≤ {MinGpu3DLoadPercent:F0}%";
            else
                rawReason = $"game conditions met ({inspect.ProcessName}.exe, GPU {gpuLoad:F0}%)";

            if (conditionsMet)
            {
                bool wasInactive = !_latchedActive;
                _latchedActive = true;
                _conditionsLostUtc = null;
                _latchedProcessName = inspect.ProcessName;
                return new GameDetectionResult
                {
                    IsGameActive = true,
                    ConditionsMet = true,
                    ProcessName = inspect.ProcessName,
                    Gpu3DLoadPercent = gpuLoad,
                    Reason = wasInactive ? $"game engaged · {rawReason}" : rawReason
                };
            }

            if (_latchedActive)
            {
                _conditionsLostUtc ??= utcNow;
                double heldSec = (utcNow - _conditionsLostUtc.Value).TotalSeconds;
                if (heldSec < ExitHysteresisSec)
                {
                    return new GameDetectionResult
                    {
                        IsGameActive = true,
                        ConditionsMet = false,
                        ProcessName = _latchedProcessName,
                        Gpu3DLoadPercent = gpuLoad,
                        Reason = $"exit hysteresis {heldSec:F0}/{ExitHysteresisSec}s · {rawReason}"
                    };
                }

                string? ended = _latchedProcessName;
                _latchedActive = false;
                _conditionsLostUtc = null;
                _latchedProcessName = null;
                return new GameDetectionResult
                {
                    IsGameActive = false,
                    ConditionsMet = false,
                    ProcessName = ended,
                    Gpu3DLoadPercent = gpuLoad,
                    Reason = $"game disengaged after hysteresis · {rawReason}"
                };
            }

            return new GameDetectionResult
            {
                IsGameActive = false,
                ConditionsMet = false,
                ProcessName = inspect.ProcessName,
                Gpu3DLoadPercent = gpuLoad,
                Reason = rawReason
            };
        }

        private sealed class ForegroundInspect
        {
            public string? ProcessName { get; init; }
            public bool IsFullscreenOrBorderless { get; init; }
            public int WindowW { get; init; }
            public int WindowH { get; init; }
            public int ScreenW { get; init; }
            public int ScreenH { get; init; }
        }

        private ForegroundInspect InspectForeground(DateTime utcNow)
        {
            int screenW = Win32Api.GetSystemMetrics(Win32Api.SM_CXSCREEN);
            int screenH = Win32Api.GetSystemMetrics(Win32Api.SM_CYSCREEN);
            if (screenW <= 0 || screenH <= 0)
            {
                try
                {
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    if (screen != null)
                    {
                        screenW = screen.Bounds.Width;
                        screenH = screen.Bounds.Height;
                    }
                }
                catch { }
            }

            IntPtr hwnd = Win32Api.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return new ForegroundInspect
                {
                    ScreenW = screenW,
                    ScreenH = screenH
                };
            }

            Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
            string? processName = ResolveProcessName(pid, utcNow);

            bool fullscreen = false;
            int ww = 0, wh = 0;
            if (Win32Api.GetWindowRect(hwnd, out Win32Api.RECT rect))
            {
                ww = Math.Abs(rect.Width);
                wh = Math.Abs(rect.Height);
                // fullscreen / borderless: equal or larger than primary monitor
                fullscreen = screenW > 0 && screenH > 0 && ww >= screenW && wh >= screenH;
            }

            // exclusive D3D fullscreen often still reports full-size; also accept OS flag
            if (!fullscreen && Win32Api.IsExclusiveD3DFullscreen())
                fullscreen = processName != null && !IsExcluded(processName);

            return new ForegroundInspect
            {
                ProcessName = processName,
                IsFullscreenOrBorderless = fullscreen,
                WindowW = ww,
                WindowH = wh,
                ScreenW = screenW,
                ScreenH = screenH
            };
        }

        private string? ResolveProcessName(uint pid, DateTime utcNow)
        {
            if (pid == 0) return null;
            if (pid == _cachedPid &&
                _cachedProcessName != null &&
                utcNow - _cacheUtc < ProcessCacheTtl)
            {
                return _cachedProcessName;
            }

            try
            {
                using var p = Process.GetProcessById((int)pid);
                string name = p.ProcessName;
                _cachedPid = pid;
                _cachedProcessName = name;
                _cacheUtc = utcNow;
                return name;
            }
            catch
            {
                _cachedPid = 0;
                _cachedProcessName = null;
                return null;
            }
        }

        public static bool IsExcluded(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;
            string bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
            return ExcludedProcesses.Contains(bare);
        }
    }
}
