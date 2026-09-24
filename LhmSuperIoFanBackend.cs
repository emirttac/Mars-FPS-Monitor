using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace FPSOverlay
{
    /// <summary>
    /// Motherboard Super I/O / EC fans via LibreHardwareMonitor Control sensors.
    /// Read-only RPM channels are kept so laptops can still show speed.
    /// </summary>
    public sealed class LhmSuperIoFanBackend : IFanBackend
    {
        private readonly HardwareMonitorManager _hw;
        private readonly Dictionary<string, LhmFanBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

        public string Name => "Motherboard Super I/O";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not probed";

        public LhmSuperIoFanBackend(HardwareMonitorManager hw)
        {
            _hw = hw;
        }

        public IReadOnlyList<FanChannel> Probe()
        {
            _bindings.Clear();
            var channels = LhmFanDiscovery.Collect(_hw, gpuOnly: false, _bindings);
            IsAvailable = channels.Count > 0;
            bool pawn = false;
            try { pawn = PawnIo.IsInstalled; } catch { }

            if (!IsAvailable)
            {
                StatusMessage = pawn
                    ? "No motherboard fan / PWM sensors"
                    : "No motherboard fan sensors · PawnIO recommended for Super I/O";
                return channels;
            }

            int writable = channels.Count(c => c.CanWritePwm);
            StatusMessage = writable > 0
                ? (pawn ? $"Ready · {channels.Count} channel(s)" : $"Ready · PawnIO recommended for reliable PWM writes")
                : "RPM readable · PWM write not exposed (BIOS/EC auto)";
            return channels;
        }

        public FanLiveReading? Read(string channelId) => LhmFanDiscovery.Read(_hw, _bindings, channelId);

        public FanApplyResult SetPwm(string channelId, int percent)
            => LhmFanDiscovery.SetPwm(_hw, _bindings, channelId, percent);

        public FanApplyResult RestoreDefaults(string? channelId = null)
            => LhmFanDiscovery.Restore(_hw, _bindings, channelId);
    }

    /// <summary>
    /// GPU Control sensors from LHM — used when NVAPI/ADL did not claim that vendor.
    /// </summary>
    public sealed class LhmGpuFanBackend : IFanBackend
    {
        private readonly HardwareMonitorManager _hw;
        private readonly Dictionary<string, LhmFanBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

        public string Name => "GPU (LibreHardwareMonitor)";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not probed";

        public LhmGpuFanBackend(HardwareMonitorManager hw)
        {
            _hw = hw;
        }

        public IReadOnlyList<FanChannel> Probe()
        {
            _bindings.Clear();
            var channels = LhmFanDiscovery.Collect(_hw, gpuOnly: true, _bindings);
            IsAvailable = channels.Count > 0;
            StatusMessage = IsAvailable
                ? $"Ready · {channels.Count} GPU fan channel(s)"
                : "No GPU fan sensors in LibreHardwareMonitor";
            return channels;
        }

        public FanLiveReading? Read(string channelId) => LhmFanDiscovery.Read(_hw, _bindings, channelId);

        public FanApplyResult SetPwm(string channelId, int percent)
            => LhmFanDiscovery.SetPwm(_hw, _bindings, channelId, percent);

        public FanApplyResult RestoreDefaults(string? channelId = null)
            => LhmFanDiscovery.Restore(_hw, _bindings, channelId);
    }

    internal sealed class LhmFanBinding
    {
        public string ControlIdentifier { get; init; } = "";
        public string? FanIdentifier { get; init; }
        public bool CanWrite { get; init; }
        public FanKind Kind { get; init; }
    }

    internal static class LhmFanDiscovery
    {
        public static List<FanChannel> Collect(
            HardwareMonitorManager hw,
            bool gpuOnly,
            Dictionary<string, LhmFanBinding> bindings)
        {
            return hw.WithComputer(computer =>
            {
                var channels = new List<FanChannel>();
                if (computer?.Hardware == null) return channels;

                foreach (var hardware in computer.Hardware)
                    Walk(hardware, gpuOnly, channels, bindings);

                return channels;
            });
        }

        private static void Walk(
            IHardware hardware,
            bool gpuOnly,
            List<FanChannel> channels,
            Dictionary<string, LhmFanBinding> bindings)
        {
            bool isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
            if (gpuOnly != isGpu)
            {
                foreach (var sub in hardware.SubHardware)
                    Walk(sub, gpuOnly, channels, bindings);
                return;
            }

            if (!gpuOnly && hardware.HardwareType is not HardwareType.Motherboard and not HardwareType.SuperIO and not HardwareType.EmbeddedController)
            {
                foreach (var sub in hardware.SubHardware)
                    Walk(sub, gpuOnly, channels, bindings);
                return;
            }

            try { hardware.Update(); } catch { }

            var fans = hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            var controls = hardware.Sensors.Where(s => s.SensorType == SensorType.Control).ToList();
            var usedFans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ctrl in controls)
            {
                var fan = PairFan(ctrl, fans);
                string id = "lhm:" + ctrl.Identifier;
                if (fan != null)
                    usedFans.Add(fan.Identifier.ToString());

                FanKind kind = gpuOnly
                    ? FanKind.Gpu
                    : Prefer(FanKindClassifier.FromName(ctrl.Name), fan != null ? FanKindClassifier.FromName(fan.Name) : FanKind.Unknown);

                bool canWrite = ctrl.Control != null;
                bindings[id] = new LhmFanBinding
                {
                    ControlIdentifier = ctrl.Identifier.ToString(),
                    FanIdentifier = fan?.Identifier.ToString(),
                    CanWrite = canWrite,
                    Kind = kind
                };

                var vendor = hardware.HardwareType switch
                {
                    HardwareType.GpuNvidia => FanVendorHint.Nvidia,
                    HardwareType.GpuAmd => FanVendorHint.Amd,
                    HardwareType.GpuIntel => FanVendorHint.Intel,
                    _ => FanVendorHint.None
                };

                channels.Add(new FanChannel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(ctrl.Name) ? hardware.Name : $"{ctrl.Name}",
                    Kind = kind,
                    BackendName = gpuOnly ? "GPU (LibreHardwareMonitor)" : "Motherboard Super I/O",
                    CanReadRpm = fan != null,
                    CanWritePwm = canWrite,
                    PairedRpmId = fan != null ? "lhm-rpm:" + fan.Identifier : null,
                    PreferredTempSource = FanKindClassifier.PreferredSource(kind),
                    MinSafePwm = FanChannel.DefaultMinSafePwm(kind),
                    Capabilities = canWrite
                        ? "PWM via LibreHardwareMonitor Control"
                        : "Control sensor has no software PWM",
                    VendorHint = vendor
                });
            }

            foreach (var fan in fans)
            {
                if (usedFans.Contains(fan.Identifier.ToString())) continue;
                string id = "lhm-rpm:" + fan.Identifier;
                FanKind kind = gpuOnly
                    ? FanKind.Gpu
                    : FanKindClassifier.FromName(fan.Name);

                bindings[id] = new LhmFanBinding
                {
                    ControlIdentifier = "",
                    FanIdentifier = fan.Identifier.ToString(),
                    CanWrite = false,
                    Kind = kind
                };

                channels.Add(new FanChannel
                {
                    Id = id,
                    Name = fan.Name,
                    Kind = kind,
                    BackendName = gpuOnly ? "GPU (LibreHardwareMonitor)" : "Motherboard Super I/O",
                    CanReadRpm = true,
                    CanWritePwm = false,
                    PairedRpmId = id,
                    PreferredTempSource = FanKindClassifier.PreferredSource(kind),
                    MinSafePwm = FanChannel.DefaultMinSafePwm(kind),
                    Capabilities = "BIOS/EC auto — this device does not expose PWM write",
                    VendorHint = gpuOnly
                        ? hardware.HardwareType switch
                        {
                            HardwareType.GpuNvidia => FanVendorHint.Nvidia,
                            HardwareType.GpuAmd => FanVendorHint.Amd,
                            HardwareType.GpuIntel => FanVendorHint.Intel,
                            _ => FanVendorHint.None
                        }
                        : FanVendorHint.None
                });
            }

            foreach (var sub in hardware.SubHardware)
                Walk(sub, gpuOnly, channels, bindings);
        }

        private static ISensor? PairFan(ISensor control, List<ISensor> fans)
        {
            var byIndex = fans.FirstOrDefault(f => f.Index == control.Index);
            if (byIndex != null) return byIndex;

            string ctrlNum = TrailingNumber(control.Name);
            if (ctrlNum.Length > 0)
            {
                var byNum = fans.FirstOrDefault(f => TrailingNumber(f.Name) == ctrlNum);
                if (byNum != null) return byNum;
            }

            return fans.Count == 1 ? fans[0] : null;
        }

        private static string TrailingNumber(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            return i < name.Length - 1 ? name[(i + 1)..] : "";
        }

        private static FanKind Prefer(FanKind a, FanKind b)
        {
            if (a != FanKind.Unknown) return a;
            return b;
        }

        public static FanLiveReading? Read(
            HardwareMonitorManager hw,
            Dictionary<string, LhmFanBinding> bindings,
            string channelId)
        {
            if (!bindings.TryGetValue(channelId, out var bind))
                return null;

            return hw.WithComputer(computer =>
            {
                ISensor? control = null;
                ISensor? fan = null;
                if (computer?.Hardware == null)
                    return new FanLiveReading { ChannelId = channelId };

                WalkFind(computer.Hardware, bind, ref control, ref fan);
                try { control?.Hardware?.Update(); } catch { }
                try { fan?.Hardware?.Update(); } catch { }

                return new FanLiveReading
                {
                    ChannelId = channelId,
                    PwmPercent = control?.Value,
                    Rpm = fan?.Value
                };
            });
        }

        public static FanApplyResult SetPwm(
            HardwareMonitorManager hw,
            Dictionary<string, LhmFanBinding> bindings,
            string channelId,
            int percent)
        {
            if (!bindings.TryGetValue(channelId, out var bind) || !bind.CanWrite)
                return FanApplyResult.Fail("Channel is not writable");

            int floor = bind.Kind == FanKind.Gpu ? FanChannel.DefaultMinSafePwm(FanKind.Gpu) : 0;
            percent = Math.Clamp(percent, floor, 100);
            return hw.WithComputer(computer =>
            {
                ISensor? control = null;
                ISensor? fan = null;
                if (computer?.Hardware == null)
                    return FanApplyResult.Fail("Hardware monitor closed");

                WalkFind(computer.Hardware, bind, ref control, ref fan);
                if (control?.Control == null)
                    return FanApplyResult.Fail("LibreHardwareMonitor Control is missing");

                try
                {
                    control.Hardware?.Update();
                    control.Control.SetSoftware(percent);
                    return FanApplyResult.Ok($"LHM PWM {percent}%", percent);
                }
                catch (Exception ex)
                {
                    return FanApplyResult.Fail(ex.Message);
                }
            });
        }

        public static FanApplyResult Restore(
            HardwareMonitorManager hw,
            Dictionary<string, LhmFanBinding> bindings,
            string? channelId)
        {
            return hw.WithComputer(computer =>
            {
                if (computer?.Hardware == null)
                    return FanApplyResult.Fail("Hardware monitor closed");

                int restored = 0;
                foreach (var kv in bindings)
                {
                    if (channelId != null && !string.Equals(kv.Key, channelId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!kv.Value.CanWrite) continue;

                    ISensor? control = null;
                    ISensor? fan = null;
                    WalkFind(computer.Hardware, kv.Value, ref control, ref fan);
                    if (control?.Control == null) continue;
                    try
                    {
                        control.Control.SetDefault();
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        OcDebugLog.LogError($"[FAN] LHM SetDefault failed {kv.Key}", ex);
                    }
                }

                return restored > 0
                    ? FanApplyResult.Ok($"LHM restored {restored} fan(s) to BIOS auto")
                    : FanApplyResult.Ok("Nothing to restore");
            });
        }

        private static void WalkFind(IEnumerable<IHardware> hardware, LhmFanBinding bind, ref ISensor? control, ref ISensor? fan)
        {
            foreach (var h in hardware)
                WalkFindNode(h, bind, ref control, ref fan);
        }

        private static void WalkFindNode(IHardware hardware, LhmFanBinding bind, ref ISensor? control, ref ISensor? fan)
        {
            foreach (var s in hardware.Sensors)
            {
                string id = s.Identifier.ToString();
                if (control == null &&
                    !string.IsNullOrEmpty(bind.ControlIdentifier) &&
                    string.Equals(id, bind.ControlIdentifier, StringComparison.OrdinalIgnoreCase))
                    control = s;
                if (fan == null &&
                    !string.IsNullOrEmpty(bind.FanIdentifier) &&
                    string.Equals(id, bind.FanIdentifier, StringComparison.OrdinalIgnoreCase))
                    fan = s;
            }

            foreach (var sub in hardware.SubHardware)
                WalkFindNode(sub, bind, ref control, ref fan);
        }
    }
}
