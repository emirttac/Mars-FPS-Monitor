using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    public sealed class OverclockManager : IDisposable
    {
        private readonly OverlayConfig _config;
        private readonly HardwareMonitorManager _hw;
        private readonly Computer? _computer;
        private IGpuOverclockProvider _gpuProvider;
        private readonly OcProfileStore _profileStore;
        private readonly OcProfileEngine _engine;
        private readonly GameDetectionEngine _gameDetection;
        private readonly NotificationService _notifications;
        private readonly StartupHardwareReconciler _reconciler = new();
        private readonly object _sync = new();
        private readonly System.Threading.Timer? _timer;
        private Guid _appliedProfileId = OcProfileStore.SafeStock.Id;
        private bool _disposed;
        private bool _wasGameActive;
        private GpuThermalSample? _thermalOverride;
        private float? _lastNotifiedCore;
        private float? _lastNotifiedHot;
        private string? _lastNotifiedProfile;
        private string? _lastNotifiedCoreOffset;
        private string? _lastNotifiedMemOffset;
        private string? _lastNotifiedPower;
        private bool? _lastNotifiedGameActive;
        private string? _lastNotifiedGameName;

        public event Action? StatusChanged;

        public OverclockStatus Status { get; } = new();
        public OcProfileStore ProfileStore => _profileStore;
        public GameDetectionEngine GameDetection => _gameDetection;

        public OverclockManager(
            OverlayConfig config,
            HardwareMonitorManager hw,
            Computer? computer,
            IGpuOverclockProvider? provider = null,
            bool startTimer = true,
            string? profileStorePath = null)
        {
            _config = config;
            _hw = hw;
            _computer = computer;
            _gpuProvider = provider ?? GpuOverclockProviderFactory.Create(computer, config.SelectedGpuName);
            _profileStore = new OcProfileStore(profileStorePath);
            _engine = new OcProfileEngine(_profileStore);
            _gameDetection = new GameDetectionEngine();
            _notifications = new NotificationService(new NotificationManager());
            _profileStore.ProfilesChanged += () => { try { Refresh(); } catch { } };

            Status.GpuProviderName = _gpuProvider.Name;
            Status.GpuVendor = _gpuProvider.Vendor;
            Status.GpuSupported = _gpuProvider.IsAvailable;
            Status.GpuStatusMessage = _gpuProvider.StatusMessage;
            Status.ControlMode = config.OcControlMode;
            Status.GpuAutoEnabled = config.OcControlMode != OcControlMode.Off;

            OcSafetyHook.Restore = RestoreAllQuiet;
            _timer = startTimer
                ? new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
                : null;
        }

        /// <summary>Restores the previous adapter, then binds clocks to the newly selected GPU.</summary>
        public void RebindSelectedGpu()
        {
            lock (_sync)
            {
                if (_disposed || HardwareRelease.IsReleased)
                    return;
                try { _gpuProvider.RestoreDefaults(); } catch { }
                if (_gpuProvider is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }

                _gpuProvider = GpuOverclockProviderFactory.Create(_computer, _config.SelectedGpuName);
                Status.GpuProviderName = _gpuProvider.Name;
                Status.GpuVendor = _gpuProvider.Vendor;
                Status.GpuSupported = _gpuProvider.IsAvailable;
                Status.GpuStatusMessage = _gpuProvider.StatusMessage;
                _reconciler.Reset();
                _appliedProfileId = OcProfileStore.SafeStock.Id;
                RefreshLocked();
            }
        }

        public void SyncFromConfig()
        {
            lock (_sync)
            {
                Status.ControlMode = _config.OcControlMode;
                Status.GpuAutoEnabled = _config.OcControlMode != OcControlMode.Off;
                if (_config.OcControlMode != OcControlMode.AutoThermal)
                {
                    _engine.Reset();
                    if (_wasGameActive)
                    {
                        _wasGameActive = false;
                        _gameDetection.Reset();
                        ClearGameStatus();
                    }
                }
                RefreshLocked();
            }
        }

        public void Refresh()
        {
            if (_disposed || HardwareRelease.IsReleased) return;
            lock (_sync) RefreshLocked();
        }

        /// <summary>One-second path for every OC mode. Failed restore or apply is retried here.</summary>
        internal void Tick()
        {
            if (_disposed || HardwareRelease.IsReleased) return;
            lock (_sync)
            {
                if (_disposed || HardwareRelease.IsReleased) return;
                RefreshLocked();
            }
        }

        /// <summary>Test seam. A set sample replaces the hardware monitor for the next ticks.</summary>
        internal void SetThermalSampleForTests(GpuThermalSample sample) => _thermalOverride = sample;

        /// <summary>
        /// Manual mode holds stock when the core sensor is missing or the hotspot is critical.
        /// The control mode itself stays Manual.
        /// </summary>
        internal static bool ManualMustHoldStock(GpuThermalSample sample, out string reason)
        {
            if (!sample.IsValid)
            {
                reason = "manual sensor invalid -> Safe/Off";
                return true;
            }

            if (sample.HotspotTempC is float hs && hs >= OcProfileEngine.DefaultHotspotForceSafeC)
            {
                reason = $"manual hotspot {hs:F0}C -> Safe/Off";
                return true;
            }

            reason = "";
            return false;
        }

        private void UpdateThermalSample()
        {
            var sample = _thermalOverride ?? _hw.GetGpuThermalSample(_config.SelectedGpuName);
            Status.LastCoreTempC = sample.CoreTempC;
            Status.LastHotspotTempC = sample.HotspotTempC;
        }

        /// <summary>Don't spam UI every second if OC status fields didn't actually change.</summary>
        private void NotifyUiIfChanged(bool force = false)
        {
            var gpu = Status.LastGpuTarget ?? OcProfileStore.SafeStock.ToTarget();
            string coreOff = $"+{gpu.GpuCoreOffsetMhz}";
            string memOff = $"+{gpu.GpuMemoryOffsetMhz}";
            string power = gpu.GpuPowerLimitPercent is int pl ? $"{pl}%" : "stock";

            bool changed = force
                || !Nullable.Equals(_lastNotifiedCore, Status.LastCoreTempC)
                || !Nullable.Equals(_lastNotifiedHot, Status.LastHotspotTempC)
                || !string.Equals(_lastNotifiedProfile, Status.ActiveProfileName, StringComparison.Ordinal)
                || !string.Equals(_lastNotifiedCoreOffset, coreOff, StringComparison.Ordinal)
                || !string.Equals(_lastNotifiedMemOffset, memOff, StringComparison.Ordinal)
                || !string.Equals(_lastNotifiedPower, power, StringComparison.Ordinal)
                || _lastNotifiedGameActive != Status.GameActive
                || !string.Equals(_lastNotifiedGameName, Status.DetectedGameExe, StringComparison.Ordinal);

            if (!changed) return;

            _lastNotifiedCore = Status.LastCoreTempC;
            _lastNotifiedHot = Status.LastHotspotTempC;
            _lastNotifiedProfile = Status.ActiveProfileName;
            _lastNotifiedCoreOffset = coreOff;
            _lastNotifiedMemOffset = memOff;
            _lastNotifiedPower = power;
            _lastNotifiedGameActive = Status.GameActive;
            _lastNotifiedGameName = Status.DetectedGameExe;
            StatusChanged?.Invoke();
        }

        private void RefreshLocked()
        {
            if (_disposed || HardwareRelease.IsReleased)
                return;

            if (!PrepareStartupReconcile())
            {
                NotifyUiIfChanged();
                return;
            }

            Status.ControlMode = _config.OcControlMode;
            Status.GpuSupported = _gpuProvider.IsAvailable;
            Status.GpuProviderName = _gpuProvider.Name;
            Status.GpuVendor = _gpuProvider.Vendor;
            Status.GpuAutoEnabled = _config.OcControlMode != OcControlMode.Off;

            // sample temps every mode so ACTIVE NOW isn't empty sadge
            UpdateThermalSample();

            OcProfile profile;
            string reason;

            switch (_config.OcControlMode)
            {
                case OcControlMode.AutoThermal:
                {
                    var game = EvaluateGameDetection();
                    HandleGameLifecycleNotifications(game);

                    if (!game.IsGameActive)
                    {
                        // Auto armed but idle — keep LHM live, hold Stock, write no OC offsets
                        _engine.Reset();
                        profile = OcProfileStore.SafeStock;
                        reason = $"auto standby · {game.Reason}";
                        Status.ThermalReason = reason;
                        Status.GameActive = false;
                        Status.DetectedGameExe = null;
                        Status.GameDetectionReason = game.Reason;
                        break;
                    }

                    Status.GameActive = true;
                    Status.DetectedGameExe = game.ProcessName;
                    Status.GameDetectionReason = game.Reason;

                    var sample = new GpuThermalSample
                    {
                        CoreTempC = Status.LastCoreTempC,
                        HotspotTempC = Status.LastHotspotTempC
                    };
                    var decision = _engine.Evaluate(sample, DateTime.UtcNow);
                    profile = decision.Profile;
                    reason = $"{decision.Reason} · {game.ProcessName}.exe";
                    Status.ThermalReason = reason;
                    break;
                }
                case OcControlMode.ManualFixed:
                {
                    ClearGameStatus();
                    var manualSample = new GpuThermalSample
                    {
                        CoreTempC = Status.LastCoreTempC,
                        HotspotTempC = Status.LastHotspotTempC
                    };
                    if (ManualMustHoldStock(manualSample, out string holdReason))
                    {
                        profile = OcProfileStore.SafeStock;
                        reason = holdReason;
                    }
                    else
                    {
                        profile = _profileStore.GetById(_config.ManualProfileId) ?? OcProfileStore.SafeStock;
                        reason = $"manual -> {profile.ProfileName}";
                    }
                    Status.ThermalReason = reason;
                    break;
                }
                default:
                {
                    ClearGameStatus();
                    profile = OcProfileStore.SafeStock;
                    reason = "OC off";
                    Status.ThermalReason = reason;
                    _engine.Reset();
                    break;
                }
            }

            ApplyProfile(profile, reason);
        }

        private GameDetectionResult EvaluateGameDetection()
        {
            return _gameDetection.Evaluate(
                () => _hw.GetGpuLoadPercent(_config.SelectedGpuName),
                DateTime.UtcNow);
        }

        /// <summary>
        /// Toasts only on game session edges (start / exit after detector cooldown).
        /// In-session thermal profile transitions never notify.
        /// </summary>
        private void HandleGameLifecycleNotifications(GameDetectionResult game)
        {
            if (game.IsGameActive && !_wasGameActive)
            {
                _wasGameActive = true;
                _notifications.OnGameStarted(_config.Language, game.ProcessName ?? "game");
                OcDebugLog.Write($"game detect ON: {game.ProcessName}.exe · {game.Reason}");
            }
            else if (!game.IsGameActive && _wasGameActive)
            {
                _wasGameActive = false;
                _notifications.OnGameExited(_config.Language);
                OcDebugLog.Write($"game detect OFF · {game.Reason}");
            }
        }

        private void ClearGameStatus()
        {
            Status.GameActive = false;
            Status.DetectedGameExe = null;
            Status.GameDetectionReason = "";
            _wasGameActive = false;
            _gameDetection.Reset();
        }

        private void ApplyProfile(OcProfile profile, string reason)
        {
            Status.ActiveProfileId = profile.Id;
            Status.ActiveProfileName = profile.ProfileName;

            bool shouldApply = _config.OcControlMode != OcControlMode.Off && _gpuProvider.IsAvailable;
            bool changed = profile.Id != _appliedProfileId;

            if (!shouldApply)
            {
                // restore ONCE when leaving a profile — not every tick please
                if (_appliedProfileId != OcProfileStore.SafeStock.Id)
                {
                    var restore = _gpuProvider.RestoreDefaults();
                    if (restore.Success)
                    {
                        _appliedProfileId = OcProfileStore.SafeStock.Id;
                        Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                        Status.GpuStatusMessage = _gpuProvider.IsAvailable
                            ? $"Off · {restore.Message}"
                            : _gpuProvider.StatusMessage;
                    }
                    else
                    {
                        // Keep previous _appliedProfileId so the next tick retries restore.
                        Status.GpuStatusMessage = _gpuProvider.IsAvailable
                            ? $"Off · restore failed: {restore.Message}"
                            : _gpuProvider.StatusMessage;
                    }
                    OcDebugLog.Write(Status.GpuStatusMessage);
                }
                else if (string.IsNullOrEmpty(Status.GpuStatusMessage))
                {
                    Status.GpuStatusMessage = _gpuProvider.IsAvailable ? "Off" : _gpuProvider.StatusMessage;
                }

                // Desired UI state while Off is Stock even if HW restore is still retrying.
                Status.ActiveProfileId = OcProfileStore.SafeStock.Id;
                Status.ActiveProfileName = OcProfileStore.SafeStock.ProfileName;
                if (_appliedProfileId == OcProfileStore.SafeStock.Id)
                    Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                NotifyUiIfChanged();
                return;
            }

            // Auto standby (no game): keep Stock without rewriting NVAPI every tick
            if (_config.OcControlMode == OcControlMode.AutoThermal &&
                !Status.GameActive &&
                profile.Id == OcProfileStore.SafeStock.Id)
            {
                if (_appliedProfileId != OcProfileStore.SafeStock.Id)
                {
                    var restore = _gpuProvider.RestoreDefaults();
                    if (restore.Success)
                    {
                        _appliedProfileId = OcProfileStore.SafeStock.Id;
                        Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                        Status.GpuStatusMessage = $"auto standby · {restore.Message}";
                    }
                    else
                    {
                        Status.GpuStatusMessage = $"auto standby · restore failed: {restore.Message}";
                    }
                    OcDebugLog.Write(Status.GpuStatusMessage);
                }
                else
                {
                    Status.GpuStatusMessage = reason;
                }

                Status.ActiveProfileId = OcProfileStore.SafeStock.Id;
                Status.ActiveProfileName = OcProfileStore.SafeStock.ProfileName;
                if (_appliedProfileId == OcProfileStore.SafeStock.Id)
                    Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                NotifyUiIfChanged();
                return;
            }

            if (!changed && Status.LastGpuTarget != null)
            {
                Status.GpuStatusMessage = reason;
                NotifyUiIfChanged();
                return;
            }

            var clamped = profile.Clone();
            if (OcHardwareLimits.ClampProfile(clamped))
            {
                OcDebugLog.Write(
                    $"OC clamp before apply: {profile.ProfileName} " +
                    $"core {profile.CoreOffsetMhz}→{clamped.CoreOffsetMhz} " +
                    $"mem {profile.MemoryOffsetMhz}→{clamped.MemoryOffsetMhz} " +
                    $"pl {profile.PowerLimitPercent?.ToString() ?? "stock"}→{clamped.PowerLimitPercent?.ToString() ?? "stock"}");
            }

            var target = clamped.ToTarget();
            OverclockApplyResult result;
            if (profile.Id == OcProfileStore.SafeStock.Id)
                result = _gpuProvider.RestoreDefaults();
            else
                result = _gpuProvider.Apply(target);

            if (result.Success)
            {
                _appliedProfileId = profile.Id;
                Status.LastGpuTarget = result.Applied ?? target;
                Status.GpuStatusMessage = $"{reason} · {result.Message}";
            }
            else
            {
                // Do not advance _appliedProfileId — next tick retries while desired ≠ applied.
                Status.GpuStatusMessage = $"{reason} · FAILED: {result.Message}";
            }

            OcDebugLog.Write(Status.GpuStatusMessage);
            NotifyUiIfChanged(force: true);
        }

        /// <summary>
        /// Restores driver defaults once before any new offset. Failure blocks Apply until a later tick succeeds.
        /// </summary>
        private bool PrepareStartupReconcile()
        {
            return _reconciler.Prepare(() =>
            {
                if (!_gpuProvider.IsAvailable)
                    return true;
                try
                {
                    var restore = _gpuProvider.RestoreDefaults();
                    if (!restore.Success)
                    {
                        Status.GpuStatusMessage = "startup reconcile failed: " + restore.Message;
                        OcDebugLog.Write(Status.GpuStatusMessage);
                        return false;
                    }

                    _appliedProfileId = OcProfileStore.SafeStock.Id;
                    Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                    Status.GpuStatusMessage = "startup reconcile · " + restore.Message;
                    return true;
                }
                catch (Exception ex)
                {
                    Status.GpuStatusMessage = "startup reconcile failed: " + ex.Message;
                    OcDebugLog.LogError(OcLogCategory.Oc, "startup reconcile failed", ex);
                    return false;
                }
            });
        }

        public void RestoreAll()
        {
            lock (_sync)
            {
                RestoreHardwareOnly();
                NotifyUiIfChanged(force: true);
            }
        }

        private void RestoreAllQuiet()
        {
            lock (_sync)
            {
                if (_disposed) return;
                RestoreHardwareOnly();
            }
        }

        private void RestoreHardwareOnly()
        {
            try { _gpuProvider.RestoreDefaults(); }
            catch (Exception ex) { OcDebugLog.LogError(OcLogCategory.Oc, "RestoreDefaults failed", ex); }
            _appliedProfileId = OcProfileStore.SafeStock.Id;
            _engine.Reset();
            ClearGameStatus();
            Status.ActiveProfileId = OcProfileStore.SafeStock.Id;
            Status.ActiveProfileName = OcProfileStore.SafeStock.ProfileName;
            Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
            Status.GpuStatusMessage = "Restored defaults";
            Status.ThermalReason = "restore";
            Status.ControlMode = OcControlMode.Off;
            OcDebugLog.Write(Status.GpuStatusMessage);
        }

        public string GetOverlaySummary(string language)
        {
            if (_config.OcControlMode == OcControlMode.Off ||
                Status.ActiveProfileId == OcProfileStore.SafeStock.Id)
            {
                if (_config.OcControlMode == OcControlMode.AutoThermal && !Status.GameActive)
                {
                    return language switch
                    {
                        "TR" => "OC: Auto · oyun bekleniyor",
                        "DE" => "OC: Auto · warte auf Spiel",
                        "RU" => "OC: Auto · ожидание игры",
                        "AZ" => "OC: Auto · oyun gözlənilir",
                        "ZH" => "OC: Auto · 等待游戏",
                        _ => "OC: Auto · waiting for game"
                    };
                }

                return language switch
                {
                    "TR" => "OC: Kapalı",
                    "DE" => "OC: Aus",
                    "RU" => "OC: Выкл",
                    _ => "OC: Off"
                };
            }

            var t = Status.LastGpuTarget ?? new OverclockTarget();
            string modeTag = _config.OcControlMode == OcControlMode.AutoThermal ? "AUTO" : "MAN";
            string temp = Status.LastCoreTempC is float c ? $" · {c:F0}°C" : "";
            string pl = t.GpuPowerLimitPercent is int p ? $" · PL{p}%" : "";
            return $"OC: {Status.ActiveProfileName} {modeTag} (+{t.GpuCoreOffsetMhz}/+{t.GpuMemoryOffsetMhz}{pl}){temp}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer?.Dispose(); } catch { }
            lock (_sync) { RestoreHardwareOnly(); }
            OcSafetyHook.Restore = null;
            if (_gpuProvider is IDisposable d)
                d.Dispose();
        }
    }
}
