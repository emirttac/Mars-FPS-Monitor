using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FPSOverlay
{
    /// <summary>
    /// One-second sampler. Uses its own <see cref="GameDetectionEngine"/> so stats run while OC is off,
    /// and reads the existing FPS counter and smoothed temperatures. No second ETW session.
    /// </summary>
    public sealed class GameSessionTracker : IDisposable
    {
        private readonly HardwareMonitorManager _hardware;
        private readonly Func<string?> _selectedGpuName;
        private readonly GameDetectionEngine _detection = new();
        private readonly GameLibraryStatsStore _stats;
        private readonly object _sync = new();
        private readonly System.Threading.Timer _timer;

        private Dictionary<string, List<LibraryProcessMatch>> _byProcess = new(StringComparer.OrdinalIgnoreCase);
        private GameSessionAccumulator? _active;
        private string? _activeKey;
        private string? _activeProcess;
        private DateTime _lastOpenSaveUtc;
        private int _busy;
        private bool _disposed;

        public event Action? SessionRecorded;

        public GameSessionTracker(HardwareMonitorManager hardware, Func<string?> selectedGpuName, string? statsPath = null)
        {
            _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
            _selectedGpuName = selectedGpuName ?? throw new ArgumentNullException(nameof(selectedGpuName));
            _stats = new GameLibraryStatsStore(statsPath);

            try
            {
                SetLibrary(new GameLibraryStore().GetAll());
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("game stats library: " + ex.Message);
            }

            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void SetLibrary(IEnumerable<GameItem> games)
        {
            var map = new Dictionary<string, List<LibraryProcessMatch>>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in games)
            {
                if (string.IsNullOrWhiteSpace(game.ExePath))
                    continue;

                string name = Path.GetFileNameWithoutExtension(game.ExePath);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!map.TryGetValue(name, out var list))
                {
                    list = new List<LibraryProcessMatch>();
                    map[name] = list;
                }

                string key = GameLibraryStore.DedupKey(game);
                if (list.Any(match => string.Equals(match.Key, key, StringComparison.OrdinalIgnoreCase)))
                    continue;

                list.Add(new LibraryProcessMatch(key, game.ExePath));
            }

            lock (_sync)
                _byProcess = map;
        }

        public GameLifetimeStats? TryGet(string key)
        {
            lock (_sync)
                return _stats.TryGet(key)?.Copy();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
            catch { }

            for (int i = 0; i < 200 && Volatile.Read(ref _busy) == 1; i++)
                Thread.Sleep(10);

            lock (_sync)
                CloseLocked(DateTime.UtcNow);

            try { _timer.Dispose(); }
            catch { }
        }

        private void Tick()
        {
            if (_disposed)
                return;
            if (Interlocked.Exchange(ref _busy, 1) == 1)
                return;

            try
            {
                if (_disposed)
                    return;
                Sample();
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("game stats: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void Sample()
        {
            string gpuName = "";
            try { gpuName = _selectedGpuName() ?? ""; }
            catch { }

            GameDetectionResult detected;
            try
            {
                detected = _detection.Evaluate(() => _hardware.GetGpuLoadPercent(gpuName), DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                OcDebugLog.Write("game stats detect: " + ex.Message);
                return;
            }

            string? resolved = detected.IsGameActive ? ResolveLibraryKey(detected) : null;

            int fps = 0;
            float low = 0;
            int cpu = 0;
            int gpu = 0;
            if (detected.IsGameActive && resolved != null && detected.ConditionsMet)
            {
                try
                {
                    var monitor = _hardware.FpsMonitor;
                    monitor.RefreshFps();
                    fps = monitor.CurrentFps;
                    low = monitor.OnePercentLowFps;
                }
                catch { }

                try { cpu = _hardware.GetCpuTemperature(); }
                catch { }

                try { gpu = _hardware.GetGpuTemperature(gpuName); }
                catch { }
            }

            bool ended;
            lock (_sync)
            {
                if (_disposed)
                    return;
                ended = ApplySampleLocked(detected, resolved, fps, low, cpu, gpu, DateTime.UtcNow);
            }

            if (ended)
                SessionRecorded?.Invoke();
        }

        private bool ApplySampleLocked(
            GameDetectionResult detected,
            string? resolvedKey,
            int fps,
            float onePercentLow,
            int cpuTempC,
            int gpuTempC,
            DateTime utcNow)
        {
            if (!detected.IsGameActive || string.IsNullOrEmpty(resolvedKey))
                return CloseLocked(utcNow);

            bool ended = false;
            if (!string.Equals(_activeKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
            {
                ended = CloseLocked(utcNow);
                _activeKey = resolvedKey;
                _activeProcess = detected.ProcessName;
                _active = new GameSessionAccumulator();
                _lastOpenSaveUtc = utcNow;
            }

            _active!.Tick(detected.ConditionsMet, fps, onePercentLow, cpuTempC, gpuTempC);

            if ((utcNow - _lastOpenSaveUtc).TotalSeconds >= 30)
            {
                _stats.WriteOpen(_active.ToSnapshot(_activeKey!));
                _lastOpenSaveUtc = utcNow;
            }

            return ended;
        }

        private bool CloseLocked(DateTime utcNow)
        {
            if (_active == null || string.IsNullOrEmpty(_activeKey))
            {
                _active = null;
                _activeKey = null;
                _activeProcess = null;
                return false;
            }

            string key = _activeKey;
            var merged = GameSessionAccumulator.Merge(_stats.TryGet(key), _active, utcNow, key);
            _stats.Commit(merged);
            _active = null;
            _activeKey = null;
            _activeProcess = null;
            return true;
        }

        private string? ResolveLibraryKey(GameDetectionResult detected)
        {
            string? process = detected.ProcessName;
            if (string.IsNullOrWhiteSpace(process))
                return null;

            process = BareName(process);
            List<LibraryProcessMatch> matches;
            lock (_sync)
            {
                if (!_byProcess.TryGetValue(process, out var found) || found.Count == 0)
                    return null;
                matches = found.ToList();
            }

            if (matches.Count == 1)
                return matches[0].Key;

            if (!detected.ConditionsMet)
                return StickyKey(process);

            string? path = Win32Api.TryGetForegroundProcessImagePath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                foreach (var match in matches)
                {
                    if (!string.IsNullOrWhiteSpace(match.ExePath) &&
                        string.Equals(match.ExePath, path, StringComparison.OrdinalIgnoreCase))
                        return match.Key;
                }
            }

            return StickyKey(process);
        }

        private string? StickyKey(string process)
        {
            lock (_sync)
            {
                if (_activeKey != null &&
                    string.Equals(_activeProcess, process, StringComparison.OrdinalIgnoreCase))
                    return _activeKey;
            }

            return null;
        }

        private static string BareName(string processName)
        {
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
        }

        private readonly record struct LibraryProcessMatch(string Key, string? ExePath);
    }
}
