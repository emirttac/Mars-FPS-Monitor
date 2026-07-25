using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    public sealed class OverclockManager : IDisposable
    {
        private readonly OverlayConfig _config;
        private readonly HardwareMonitorManager _hw;
        private readonly IGpuOverclockProvider _gpuProvider;
        private readonly OcProfileStore _profileStore;
        private readonly OcProfileEngine _engine;
        private readonly object _sync = new();
        private readonly System.Threading.Timer _timer;
        private Guid _appliedProfileId = OcProfileStore.SafeStock.Id;
        private bool _disposed;
        private float? _lastNotifiedCore;
        private float? _lastNotifiedHot;
        private string? _lastNotifiedProfile;
        private string? _lastNotifiedCoreOffset;
        private string? _lastNotifiedMemOffset;
        private string? _lastNotifiedPower;

        public event Action? StatusChanged;

        public OverclockStatus Status { get; } = new();
        public OcProfileStore ProfileStore => _profileStore;

        public OverclockManager(OverlayConfig config, HardwareMonitorManager hw, Computer? computer)
        {
            _config = config;
            _hw = hw;
            _gpuProvider = GpuOverclockProviderFactory.Create(computer);
            _profileStore = new OcProfileStore();
            _engine = new OcProfileEngine(_profileStore);
            _profileStore.ProfilesChanged += () => { try { Refresh(); } catch { } };

            Status.GpuProviderName = _gpuProvider.Name;
            Status.GpuVendor = _gpuProvider.Vendor;
            Status.GpuSupported = _gpuProvider.IsAvailable;
            Status.GpuStatusMessage = _gpuProvider.StatusMessage;
            Status.ControlMode = config.OcControlMode;
            Status.GpuAutoEnabled = config.OcControlMode != OcControlMode.Off;

            _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void SyncFromConfig()
        {
            lock (_sync)
            {
                Status.ControlMode = _config.OcControlMode;
                Status.GpuAutoEnabled = _config.OcControlMode != OcControlMode.Off;
                if (_config.OcControlMode != OcControlMode.AutoThermal)
                    _engine.Reset();
                RefreshLocked();
            }
        }

        public void Refresh()
        {
            if (_disposed) return;
            lock (_sync) RefreshLocked();
        }

        private void Tick()
        {
            if (_disposed) return;
            lock (_sync)
            {
                if (_config.OcControlMode == OcControlMode.AutoThermal)
                {
                    RefreshLocked();
                }
                else
                {
                    // temps stay live even Off/Manual — users wanna SEE heat
                    UpdateThermalSample();
                    NotifyUiIfChanged();
                }
            }
        }

        private void UpdateThermalSample()
        {
            var sample = _hw.GetGpuThermalSample(_config.SelectedGpuName);
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
                || !string.Equals(_lastNotifiedPower, power, StringComparison.Ordinal);

            if (!changed) return;

            _lastNotifiedCore = Status.LastCoreTempC;
            _lastNotifiedHot = Status.LastHotspotTempC;
            _lastNotifiedProfile = Status.ActiveProfileName;
            _lastNotifiedCoreOffset = coreOff;
            _lastNotifiedMemOffset = memOff;
            _lastNotifiedPower = power;
            StatusChanged?.Invoke();
        }

        private void RefreshLocked()
        {
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
                    var sample = new GpuThermalSample
                    {
                        CoreTempC = Status.LastCoreTempC,
                        HotspotTempC = Status.LastHotspotTempC
                    };
                    var decision = _engine.Evaluate(sample, DateTime.UtcNow);
                    profile = decision.Profile;
                    reason = decision.Reason;
                    Status.ThermalReason = reason;
                    break;
                }
                case OcControlMode.ManualFixed:
                {
                    profile = _profileStore.GetById(_config.ManualProfileId) ?? OcProfileStore.SafeStock;
                    reason = $"manual -> {profile.ProfileName}";
                    Status.ThermalReason = reason;
                    break;
                }
                default:
                {
                    profile = OcProfileStore.SafeStock;
                    reason = "OC off";
                    Status.ThermalReason = reason;
                    _engine.Reset();
                    break;
                }
            }

            ApplyProfile(profile, reason);
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
                    Status.GpuStatusMessage = _gpuProvider.IsAvailable
                        ? $"Off · {restore.Message}"
                        : _gpuProvider.StatusMessage;
                    _appliedProfileId = OcProfileStore.SafeStock.Id;
                    OcDebugLog.Write(Status.GpuStatusMessage);
                }
                else if (string.IsNullOrEmpty(Status.GpuStatusMessage))
                {
                    Status.GpuStatusMessage = _gpuProvider.IsAvailable ? "Off" : _gpuProvider.StatusMessage;
                }

                Status.ActiveProfileId = OcProfileStore.SafeStock.Id;
                Status.ActiveProfileName = OcProfileStore.SafeStock.ProfileName;
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

            var target = profile.ToTarget();
            OverclockApplyResult result;
            if (profile.Id == OcProfileStore.SafeStock.Id)
                result = _gpuProvider.RestoreDefaults();
            else
                result = _gpuProvider.Apply(target);

            _appliedProfileId = profile.Id;
            Status.GpuStatusMessage = $"{reason} · {result.Message}";
            Status.LastGpuTarget = result.Applied ?? target;
            OcDebugLog.Write(Status.GpuStatusMessage);
            NotifyUiIfChanged(force: true);
        }

        public void RestoreAll()
        {
            lock (_sync)
            {
                try { _gpuProvider.RestoreDefaults(); } catch { }
                _appliedProfileId = OcProfileStore.SafeStock.Id;
                _engine.Reset();
                Status.ActiveProfileId = OcProfileStore.SafeStock.Id;
                Status.ActiveProfileName = OcProfileStore.SafeStock.ProfileName;
                Status.LastGpuTarget = OcProfileStore.SafeStock.ToTarget();
                Status.GpuStatusMessage = "Restored defaults";
                Status.ThermalReason = "restore";
                Status.ControlMode = OcControlMode.Off;
                OcDebugLog.Write(Status.GpuStatusMessage);
                NotifyUiIfChanged(force: true);
            }
        }

        public string GetOverlaySummary(string language)
        {
            if (_config.OcControlMode == OcControlMode.Off ||
                Status.ActiveProfileId == OcProfileStore.SafeStock.Id)
            {
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
            try { _timer.Dispose(); } catch { }
            RestoreAll();
            if (_gpuProvider is IDisposable d)
                d.Dispose();
        }
    }
}
