using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>
    /// Fan curve orchestrator: 1s tick, min PWM floor, stalled-fan watchdog, restore on dispose.
    /// Writes only when the integer PWM target actually changes.
    /// </summary>
    public sealed class FanControlManager : IDisposable
    {
        public const float WatchdogHotCpuC = FanWatchdogEvaluator.HotCpuC;
        public const float WatchdogHotGpuC = FanWatchdogEvaluator.HotGpuC;
        public const float WatchdogStalledRpm = FanWatchdogEvaluator.StalledRpm;
        public const int WatchdogSeconds = FanWatchdogEvaluator.SecondsRequired;

        private readonly OverlayConfig _config;
        private readonly HardwareMonitorManager _hw;
        private readonly Computer? _computer;
        private FanBackendHost _host;
        private readonly FanCurveStore _store;
        private readonly FanCurveEngine _engine = new();
        private readonly NotificationManager _notifications = new();
        private readonly object _sync = new();
        private readonly System.Threading.Timer _timer;
        private readonly Dictionary<string, int> _appliedPwm = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastDesired = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lastEvalTemp = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _failStreak = new(StringComparer.OrdinalIgnoreCase);
        private readonly StartupHardwareReconciler _reconciler = new();
        private int _watchdogStreak;
        private int _unverifiedStreak;
        private bool _disposed;
        private bool _restoredWhileOff;
        private bool _gpuRebindRestorePending;

        public event Action? StatusChanged;

        public FanControlStatus Status { get; } = new();
        public FanCurveStore Store => _store;
        public FanBackendHost Host => _host;

        public FanControlManager(OverlayConfig config, HardwareMonitorManager hw)
        {
            _config = config;
            _hw = hw;
            _computer = hw.Computer;
            _store = new FanCurveStore();
            _host = FanBackendFactory.Create(hw, _computer, config.SelectedGpuName);
            FanSafetyHook.Restore = RestoreAllQuiet;

            Status.BackendSummary = _host.Summary;
            Status.HasWritableChannel = _host.Channels.Any(c => c.CanWritePwm);
            Status.HasReadableChannel = _host.Channels.Any(c => c.CanReadRpm);
            Status.ControlMode = config.FanControlMode;
            if (config.FanActiveCurveId == Guid.Empty)
                config.FanActiveCurveId = FanCurveStore.BalancedId;

            _store.Changed += () => { try { Refresh(); } catch { } };
            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Refresh();
        }

        public void SyncFromConfig()
        {
            lock (_sync)
            {
                Status.ControlMode = _config.FanControlMode;
                Status.FailClosed = false;
                if (_config.FanControlMode == FanControlMode.Off)
                    _restoredWhileOff = false;
                RefreshLocked();
            }
        }

        public void Refresh()
        {
            if (_disposed || HardwareRelease.IsReleased) return;
            lock (_sync) RefreshLocked();
        }

        /// <summary>Restores the previous GPU fan, then binds the new card. Chassis and OEM fans stay.</summary>
        public void RebindSelectedGpu()
        {
            lock (_sync)
            {
                if (_disposed || HardwareRelease.IsReleased)
                    return;
                bool alreadyReconciled = _reconciler.IsReconciled;
                try { RestoreGpuFans(); } catch { }
                ForgetGpuPwm();
                DisposeBackends(_host);
                _host = FanBackendFactory.Create(_hw, _computer, _config.SelectedGpuName);
                _watchdogStreak = 0;
                _unverifiedStreak = 0;
                if (alreadyReconciled)
                {
                    FanApplyResult gpu = RestoreGpuFans();
                    _gpuRebindRestorePending = !gpu.Success;
                }
                else
                {
                    _gpuRebindRestorePending = false;
                }
                RefreshLocked();
            }
        }

        private void Tick()
        {
            if (_disposed || HardwareRelease.IsReleased) return;
            lock (_sync)
            {
                if (_disposed || HardwareRelease.IsReleased) return;
                RefreshLocked();
            }
        }

        private void RefreshLocked()
        {
            if (_disposed || HardwareRelease.IsReleased)
                return;

            if (!PrepareStartupReconcile())
            {
                Status.StatusMessage = "startup reconcile pending";
                NotifyUi();
                return;
            }

            if (_gpuRebindRestorePending)
            {
                FanApplyResult gpuRestore = RestoreGpuFans();
                if (!gpuRestore.Success)
                {
                    Status.StatusMessage = "gpu rebind restore pending";
                    NotifyUi();
                    return;
                }
                _gpuRebindRestorePending = false;
            }

            Status.ControlMode = _config.FanControlMode;
            Status.HasWritableChannel = _host.Channels.Any(c => c.CanWritePwm);
            Status.HasReadableChannel = _host.Channels.Any(c => c.CanReadRpm);
            Status.BackendSummary = _host.Summary;

            float cpu = SafeCpuTemp();
            float gpu = SafeGpuTemp();
            Status.LastCpuTempC = cpu > 0 ? cpu : null;
            Status.LastGpuTempC = gpu > 0 ? gpu : null;
            Status.LastManualPwm = _config.FanManualPwmPercent;

            var curve = _store.GetById(_config.FanActiveCurveId) ?? _store.GetById(FanCurveStore.BalancedId);
            Status.ActiveCurveId = curve?.Id ?? Guid.Empty;
            Status.ActiveCurveName = _config.FanControlMode == FanControlMode.Off
                ? "BIOS / Auto"
                : _config.FanControlMode == FanControlMode.ManualFixed
                    ? $"Manual {_config.FanManualPwmPercent}%"
                    : curve?.Name ?? "Balanced";

            var live = new List<FanChannelStatus>();
            foreach (var ch in _host.Channels)
            {
                var reading = SafeRead(ch.Id);
                live.Add(new FanChannelStatus
                {
                    Id = ch.Id,
                    Name = ch.Name,
                    Kind = ch.Kind,
                    BackendName = ch.BackendName,
                    CanReadRpm = ch.CanReadRpm,
                    CanWritePwm = ch.CanWritePwm,
                    Rpm = reading?.Rpm,
                    PwmPercent = reading?.PwmPercent,
                    AppliedPwm = _appliedPwm.TryGetValue(ch.Id, out int ap) ? ap : null,
                    Capabilities = ch.Capabilities,
                    Status = ch.CanWritePwm ? "writable" : "read-only"
                });
            }
            Status.Channels = live;

            if (!Status.HasWritableChannel)
            {
                Status.StatusMessage = Status.HasReadableChannel
                    ? "RPM readable · this device does not expose PWM write (BIOS/EC auto)"
                    : "No fan sensors detected";
                NotifyUi();
                return;
            }

            if (Status.FailClosed)
            {
                Status.StatusMessage = "Fail-closed · BIOS auto (safety)";
                NotifyUi();
                return;
            }

            if (_config.FanControlMode == FanControlMode.Off)
            {
                if (!_restoredWhileOff)
                {
                    var restore = RestoreUnlocked(userOff: true);
                    Status.StatusMessage = restore.Success
                        ? $"Off · {restore.Message}"
                        : $"Off · restore failed: {restore.Message}";
                    _restoredWhileOff = restore.Success;
                    OcDebugLog.Write("[FAN] " + Status.StatusMessage);
                }
                else
                {
                    Status.StatusMessage = "Off · BIOS/EC auto";
                }
                NotifyUi();
                return;
            }

            _restoredWhileOff = false;

            if (WatchdogTripped(live, cpu, gpu))
            {
                FailClosed("stalled fan while hot");
                NotifyUi();
                return;
            }

            if (curve == null && _config.FanControlMode == FanControlMode.AutoCurve)
            {
                Status.StatusMessage = "No fan curve loaded";
                NotifyUi();
                return;
            }

            int applied = 0;
            foreach (var ch in _host.Channels.Where(c => c.CanWritePwm))
            {
                int desired;
                float temp = TempFor(ch, cpu, gpu, curve);

                if (_config.FanControlMode == FanControlMode.ManualFixed)
                {
                    desired = ch.ClampPwm(_config.FanManualPwmPercent, _config.FanMinPwmFloor);
                }
                else
                {
                    int interpolated = _engine.Interpolate(curve!, temp);
                    if (_lastDesired.TryGetValue(ch.Id, out int lastPwm) &&
                        _lastEvalTemp.TryGetValue(ch.Id, out float lastTemp))
                    {
                        interpolated = _engine.ApplyHysteresis(
                            interpolated, lastPwm, temp, lastTemp, curve!.HysteresisC);
                    }
                    desired = ch.ClampPwm(interpolated, _config.FanMinPwmFloor);
                }

                _lastEvalTemp[ch.Id] = temp;

                if (_appliedPwm.TryGetValue(ch.Id, out int already) && already == desired)
                    continue;

                var backend = _host.BackendFor(ch.Id);
                if (backend == null)
                    continue;

                var result = backend.SetPwm(ch.Id, desired);
                if (result.Success)
                {
                    _appliedPwm[ch.Id] = result.AppliedPwm ?? desired;
                    _lastDesired[ch.Id] = desired;
                    _failStreak[ch.Id] = 0;
                    applied++;
                }
                else
                {
                    int fails = _failStreak.TryGetValue(ch.Id, out int n) ? n + 1 : 1;
                    _failStreak[ch.Id] = fails;
                    OcDebugLog.Write($"[FAN] apply failed {ch.Name}: {result.Message}");
                    if (fails >= 3)
                    {
                        FailClosed($"apply failed: {result.Message}");
                        NotifyUi();
                        return;
                    }
                }
            }

            Status.ThermalReason = _config.FanControlMode == FanControlMode.AutoCurve
                ? $"curve {curve!.Name} @ CPU {(cpu > 0 ? cpu.ToString("F0") : "—")}°C / GPU {(gpu > 0 ? gpu.ToString("F0") : "—")}°C"
                : $"manual {_config.FanManualPwmPercent}%";
            Status.StatusMessage = applied > 0
                ? $"{Status.ThermalReason} · wrote {applied}"
                : $"{Status.ThermalReason} · hold";
            NotifyUi();
        }

        private bool WatchdogTripped(List<FanChannelStatus> live, float cpu, float gpu)
        {
            var samples = new List<FanWatchdogChannelSample>(live.Count);
            foreach (var row in live)
            {
                samples.Add(new FanWatchdogChannelSample
                {
                    Kind = row.Kind,
                    CanWritePwm = row.CanWritePwm,
                    CanReadRpm = ChannelCanReadRpm(row.CanReadRpm, row.Rpm),
                    Rpm = row.Rpm
                });
            }

            var result = FanWatchdogEvaluator.Evaluate(cpu, gpu, samples, _watchdogStreak, _unverifiedStreak);
            _watchdogStreak = result.NewStreak;
            _unverifiedStreak = result.NewUnverifiedStreak;
            if (!string.IsNullOrEmpty(result.ThermalReason))
                Status.ThermalReason = result.ThermalReason;
            return result.ShouldFailClosed;
        }

        private void FailClosed(string reason)
        {
            OcDebugLog.Write("[FAN] fail-closed: " + reason);
            RestoreUnlocked(userOff: false);
            _config.FanControlMode = FanControlMode.Off;
            try { _config.Save(); }
            catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "fail-closed config save", ex); }
            Status.ControlMode = FanControlMode.Off;
            Status.FailClosed = true;
            Status.ActiveCurveName = "BIOS / Auto";
            Status.StatusMessage = $"Fail-closed · {reason}";
            Status.ThermalReason = reason;
            _restoredWhileOff = true;
            try
            {
                var s = UiStrings.For(_config.Language);
                _notifications.ShowHighPriority(
                    string.IsNullOrWhiteSpace(s.ToastFanTitle) ? "Mars Fan Control" : s.ToastFanTitle,
                    string.IsNullOrWhiteSpace(s.ToastFanFailClosedDetail)
                        ? (s.ToastFanFailClosed ?? "Fan control restored to BIOS auto.")
                        : string.Format(s.ToastFanFailClosedDetail, reason),
                    tag: "mars-fan-safety",
                    group: "mars-fan-safety");
            }
            catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "fail-closed toast failed", ex); }
        }

        private bool PrepareStartupReconcile()
        {
            return _reconciler.Prepare(() =>
            {
                var restore = RestoreUnlocked(userOff: true);
                if (!restore.Success)
                {
                    OcDebugLog.Write("[FAN] startup reconcile failed: " + restore.Message);
                    return false;
                }

                _restoredWhileOff = _config.FanControlMode == FanControlMode.Off;
                return true;
            });
        }

        private static float TempFor(FanChannel ch, float cpu, float gpu, FanCurve? curve)
        {
            var source = ch.PreferredTempSource;
            if (curve != null && ch.Kind is FanKind.Chassis or FanKind.Unknown or FanKind.Pump)
                source = curve.TempSource;

            return source switch
            {
                FanTempSource.CpuPackage => cpu > 0 ? cpu : Math.Max(cpu, gpu),
                FanTempSource.GpuCore => gpu > 0 ? gpu : Math.Max(cpu, gpu),
                FanTempSource.Motherboard => cpu > 0 ? cpu : gpu,
                _ => Math.Max(cpu, gpu)
            };
        }

        private float SafeCpuTemp()
        {
            try
            {
                float raw = _hw.ReadCpuTemperatureC();
                if (raw > 0) return raw;
                int smoothed = _hw.GetCpuTemperature();
                return smoothed > 0 ? smoothed : 0;
            }
            catch { return 0; }
        }

        private float SafeGpuTemp()
        {
            try
            {
                var sample = _hw.GetGpuThermalSample(_config.SelectedGpuName);
                if (sample.CoreTempC is float c && c > 0) return c;
                int smoothed = _hw.GetGpuTemperature(_config.SelectedGpuName);
                return smoothed > 0 ? smoothed : 0;
            }
            catch { return 0; }
        }

        private FanLiveReading? SafeRead(string channelId)
        {
            try
            {
                return _host.BackendFor(channelId)?.Read(channelId);
            }
            catch
            {
                return null;
            }
        }

        public void RestoreAll()
        {
            lock (_sync)
            {
                _config.FanControlMode = FanControlMode.Off;
                Status.ControlMode = FanControlMode.Off;
                Status.FailClosed = false;
                var result = RestoreUnlocked(userOff: true);
                Status.StatusMessage = result.Success ? result.Message : "restore failed: " + result.Message;
                Status.ActiveCurveName = "BIOS / Auto";
                _restoredWhileOff = result.Success;
                OcDebugLog.Write("[FAN] RestoreAll · " + Status.StatusMessage);
                NotifyUi(force: true);
            }
        }

        private void RestoreAllQuiet()
        {
            try
            {
                lock (_sync)
                    RestoreUnlocked(userOff: true);
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError("[FAN] crash restore failed", ex);
            }
        }

        /// <summary>
        /// A live RPM above zero means this tick can verify the fan, even if probe marked it unreadable.
        /// </summary>
        internal static bool ChannelCanReadRpm(bool flagged, float? liveRpm)
            => flagged || liveRpm is > 0;

        /// <summary>
        /// Unavailable backends are ignored. Every available backend must succeed.
        /// No available backend is success: there is nothing to clear.
        /// </summary>
        internal static FanApplyResult CombineRestores(
            IReadOnlyList<(bool Available, FanApplyResult Result)> items,
            bool userOff)
        {
            string lastFail = "";
            bool failed = false;
            foreach (var item in items)
            {
                if (!item.Available)
                    continue;
                if (item.Result.Success)
                    continue;
                failed = true;
                lastFail = string.IsNullOrWhiteSpace(item.Result.Message) ? "restore failed" : item.Result.Message;
            }

            if (!failed)
                return FanApplyResult.Ok(userOff ? "Restored BIOS/EC auto" : "Restored after fail-closed");
            return FanApplyResult.Fail(lastFail);
        }

        private FanApplyResult RestoreUnlocked(bool userOff)
        {
            FanApplyResult result = RestoreBackends(_host.Backends, userOff);
            ClearAllPwmState();
            return result;
        }

        private FanApplyResult RestoreGpuFans()
            => RestoreBackends(_host.BackendsOwning(FanKind.Gpu).ToList(), userOff: true);

        private FanApplyResult RestoreBackends(IReadOnlyList<IFanBackend> backends, bool userOff)
        {
            var items = new List<(bool Available, FanApplyResult Result)>(backends.Count);
            foreach (var backend in backends)
            {
                if (!backend.IsAvailable)
                {
                    items.Add((false, FanApplyResult.Fail(backend.StatusMessage)));
                    continue;
                }

                try
                {
                    items.Add((true, backend.RestoreDefaults()));
                }
                catch (Exception ex)
                {
                    items.Add((true, FanApplyResult.Fail(ex.Message)));
                }
            }

            return CombineRestores(items, userOff);
        }

        private void ClearAllPwmState()
        {
            _appliedPwm.Clear();
            _lastDesired.Clear();
            _failStreak.Clear();
            _watchdogStreak = 0;
            _unverifiedStreak = 0;
        }

        private void ForgetGpuPwm()
        {
            foreach (var ch in _host.Channels)
            {
                if (ch.Kind != FanKind.Gpu)
                    continue;
                _appliedPwm.Remove(ch.Id);
                _lastDesired.Remove(ch.Id);
                _lastEvalTemp.Remove(ch.Id);
                _failStreak.Remove(ch.Id);
            }
        }

        public string GetOverlaySummary()
        {
            if (!Status.HasReadableChannel && !Status.HasWritableChannel)
                return "FAN: N/A";

            var rpmBits = new List<string>();
            foreach (var ch in Status.Channels)
            {
                if (ch.Kind == FanKind.Cpu && ch.Rpm is > 0)
                    rpmBits.Add($"CPU {ch.Rpm:F0}");
                else if (ch.Kind == FanKind.Gpu && ch.Rpm is > 0)
                    rpmBits.Add($"GPU {ch.Rpm:F0}");
            }
            if (rpmBits.Count == 0)
            {
                var any = Status.Channels.FirstOrDefault(c => c.Rpm is > 0);
                if (any != null)
                    rpmBits.Add($"{any.Name} {any.Rpm:F0}");
            }

            string rpm = rpmBits.Count > 0 ? string.Join(" · ", rpmBits.Take(2)) : "—";
            if (_config.FanControlMode == FanControlMode.Off)
                return $"FAN: {rpm}";
            string mode = _config.FanControlMode == FanControlMode.AutoCurve ? "AUTO" : "MAN";
            return $"FAN: {rpm} {mode}";
        }

        private void NotifyUi(bool force = false)
        {
            StatusChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer.Dispose(); }
            catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "timer dispose", ex); }
            try { RestoreAllQuiet(); }
            catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "dispose restore", ex); }
            FanSafetyHook.Restore = null;
            DisposeBackends(_host);
        }

        private static void DisposeBackends(FanBackendHost host)
        {
            foreach (var backend in host.Backends)
            {
                if (backend is IDisposable d)
                {
                    try { d.Dispose(); }
                    catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Fan, "backend dispose", ex); }
                }
            }
        }
    }
}
