using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    /// <summary>
    /// Discovers every fan backend that actually works and merges channels.
    /// OEM laptop WMI backends probe first (chassis match + WMI present).
    /// GPU fans prefer NVAPI/ADL; motherboard headers stay on LHM Super I/O
    /// unless an OEM backend already owns system-fan control.
    /// </summary>
    public static class FanBackendFactory
    {
        public static FanBackendHost Create(HardwareMonitorManager hw, Computer? computer, string? selectedGpuName = null)
        {
            var oemBackends = CreateOemBackends();
            var sio = new LhmSuperIoFanBackend(hw);
            var nvidia = new NvidiaNvapiFanBackend(selectedGpuName);
            var amd = new AmdAdlFanBackend(computer, selectedGpuName);
            var lhmGpu = new LhmGpuFanBackend(hw);

            var backends = new List<IFanBackend>();
            backends.AddRange(oemBackends);
            backends.Add(sio);
            backends.Add(nvidia);
            backends.Add(amd);
            backends.Add(lhmGpu);

            var owners = new Dictionary<string, IFanBackend>(StringComparer.OrdinalIgnoreCase);
            var channels = new List<FanChannel>();

            void Take(IFanBackend backend, IEnumerable<FanChannel> found)
            {
                foreach (var ch in found)
                {
                    if (string.IsNullOrWhiteSpace(ch.Id) || owners.ContainsKey(ch.Id))
                        continue;
                    if (!AllowsChannel(selectedGpuName, ch))
                        continue;
                    owners[ch.Id] = backend;
                    channels.Add(ch);
                }
            }

            bool oemWritable = false;
            foreach (var oem in oemBackends)
            {
                var found = SafeProbe(oem);
                Take(oem, found);
                if (found.Any(c => c.CanWritePwm))
                    oemWritable = true;
            }

            // When an OEM laptop backend owns system fans, skip Super I/O PWM writes
            // so we do not fight the EC with two controllers. RPM-only SIO channels stay.
            var sioChannels = SafeProbe(sio);
            if (oemWritable)
            {
                sioChannels = sioChannels
                    .Where(c => !c.CanWritePwm)
                    .ToList();
                OcDebugLog.Log("[FAN] OEM writable backend active · Super I/O PWM channels suppressed");
            }
            Take(sio, sioChannels);

            var nvChannels = SafeProbe(nvidia);
            Take(nvidia, nvChannels);

            var amdChannels = SafeProbe(amd);
            Take(amd, amdChannels);

            bool nvidiaClaimed = nvChannels.Any();
            bool amdClaimed = amdChannels.Any();
            var gpuFallback = SafeProbe(lhmGpu).Where(ch =>
            {
                if (ch.VendorHint == FanVendorHint.Nvidia && nvidiaClaimed) return false;
                if (ch.VendorHint == FanVendorHint.Amd && amdClaimed) return false;
                return true;
            });
            Take(lhmGpu, gpuFallback);

            OcDebugLog.Log($"[FAN] discovered {channels.Count} channel(s) · writable={channels.Count(c => c.CanWritePwm)} · backends={string.Join(", ", backends.Where(b => b.IsAvailable).Select(b => b.Name))}");
            foreach (var ch in channels)
                OcDebugLog.Log($"[FAN]   {ch.Id} · {ch.Name} · {ch.Kind} · rpm={ch.CanReadRpm} write={ch.CanWritePwm} · {ch.BackendName}");

            return new FanBackendHost(backends, owners, channels);
        }

        private static bool AllowsChannel(string? selectedGpuName, FanChannel ch)
        {
            if (ch.Kind != FanKind.Gpu)
                return true;

            var vendor = GpuNameMatch.VendorOf(selectedGpuName);
            if (vendor == GpuNameMatch.Vendor.Unknown)
                return true;
            if (ch.VendorHint == FanVendorHint.Nvidia && vendor != GpuNameMatch.Vendor.Nvidia)
                return false;
            if (ch.VendorHint == FanVendorHint.Amd && vendor != GpuNameMatch.Vendor.Amd)
                return false;
            if (ch.VendorHint == FanVendorHint.Intel && vendor != GpuNameMatch.Vendor.Intel)
                return false;
            if (GpuNameMatch.VendorOf(ch.Name) != GpuNameMatch.Vendor.Unknown &&
                !GpuNameMatch.Matches(selectedGpuName, ch.Name))
                return false;
            return true;
        }

        private static List<IFanBackend> CreateOemBackends()
        {
            // Only construct the backend for the detected chassis vendor (others would no-op anyway).
            return OemLaptopInfo.Vendor switch
            {
                OemLaptopVendor.Asus => new List<IFanBackend> { new AsusOemFanBackend() },
                OemLaptopVendor.Lenovo => new List<IFanBackend> { new LenovoOemFanBackend() },
                OemLaptopVendor.Hp => new List<IFanBackend> { new HpOemFanBackend() },
                OemLaptopVendor.Dell => new List<IFanBackend> { new DellOemFanBackend() },
                OemLaptopVendor.Msi => new List<IFanBackend> { new MsiOemFanBackend() },
                _ => new List<IFanBackend>()
            };
        }

        private static IReadOnlyList<FanChannel> SafeProbe(IFanBackend backend)
        {
            try
            {
                return backend.Probe() ?? Array.Empty<FanChannel>();
            }
            catch (Exception ex)
            {
                OcDebugLog.LogError($"[FAN] probe failed: {backend.Name}", ex);
                return Array.Empty<FanChannel>();
            }
        }
    }

    public sealed class FanBackendHost
    {
        private readonly Dictionary<string, IFanBackend> _owners;

        public FanBackendHost(
            IReadOnlyList<IFanBackend> backends,
            Dictionary<string, IFanBackend> owners,
            IReadOnlyList<FanChannel> channels)
        {
            Backends = backends;
            _owners = owners;
            Channels = channels;
        }

        public IReadOnlyList<IFanBackend> Backends { get; }
        public IReadOnlyList<FanChannel> Channels { get; }

        public IFanBackend? BackendFor(string channelId)
            => _owners.TryGetValue(channelId, out var b) ? b : null;

        /// <summary>Backends that own at least one channel of <paramref name="kind"/>.</summary>
        public IEnumerable<IFanBackend> BackendsOwning(FanKind kind)
        {
            var found = new HashSet<IFanBackend>();
            foreach (var ch in Channels)
            {
                if (ch.Kind != kind)
                    continue;
                if (_owners.TryGetValue(ch.Id, out var backend))
                    found.Add(backend);
            }
            return found;
        }

        public string Summary
        {
            get
            {
                var names = Backends
                    .Where(b => b.IsAvailable)
                    .Select(b => b.Name)
                    .Distinct()
                    .ToList();
                return names.Count == 0 ? "None" : string.Join(" + ", names);
            }
        }
    }
}
