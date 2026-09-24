using System;
using System.Collections.Generic;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.GPU;

namespace FPSOverlay
{
    /// <summary>NVIDIA GPU fans via NVAPI cooler policy. Preferred over LHM GPU Control.</summary>
    public sealed class NvidiaNvapiFanBackend : IFanBackend
    {
        private PhysicalGPU? _gpu;
        private readonly List<int> _coolerIds = new();
        private readonly string? _selectedGpuName;

        public NvidiaNvapiFanBackend(string? selectedGpuName = null)
        {
            _selectedGpuName = selectedGpuName;
        }

        public string Name => "NVIDIA NVAPI";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public IReadOnlyList<FanChannel> Probe()
        {
            _coolerIds.Clear();
            var channels = new List<FanChannel>();
            var wanted = GpuNameMatch.VendorOf(_selectedGpuName);
            if (!GpuNameMatch.ShouldUseProvider(wanted, GpuNameMatch.Vendor.Nvidia))
            {
                IsAvailable = false;
                StatusMessage = "Selected GPU is not NVIDIA";
                return channels;
            }

            try
            {
                var gpus = PhysicalGPU.GetPhysicalGPUs()?.ToList();
                if (gpus == null || gpus.Count == 0)
                {
                    IsAvailable = false;
                    StatusMessage = "No NVIDIA GPU detected";
                    return channels;
                }

                int? index = GpuNameMatch.IndexOfBest(_selectedGpuName, gpus.Select(g => g.FullName ?? "").ToList());
                if (index == null)
                {
                    IsAvailable = false;
                    StatusMessage = $"No NVIDIA GPU matches '{_selectedGpuName}'";
                    return channels;
                }

                _gpu = gpus[index.Value];

                IReadOnlyList<GPUCooler>? coolers = null;
                try
                {
                    coolers = _gpu.CoolerInformation?.Coolers?.ToList();
                }
                catch (Exception ex)
                {
                    IsAvailable = false;
                    StatusMessage = $"GPU fan locked / NVAPI cooler unavailable: {ex.Message}";
                    return channels;
                }

                if (coolers == null || coolers.Count == 0)
                {
                    IsAvailable = false;
                    StatusMessage = $"NVIDIA GPU detected · no software fan control ({_gpu.FullName})";
                    return channels;
                }

                int i = 0;
                foreach (var cooler in coolers)
                {
                    int coolerId = cooler.CoolerId;
                    _coolerIds.Add(coolerId);
                    string id = $"nvapi:gpu0:cooler{coolerId}";
                    channels.Add(new FanChannel
                    {
                        Id = id,
                        Name = coolers.Count == 1 ? $"{_gpu.FullName} Fan" : $"{_gpu.FullName} Fan {++i}",
                        Kind = FanKind.Gpu,
                        BackendName = Name,
                        CanReadRpm = true,
                        CanWritePwm = true,
                        PreferredTempSource = FanTempSource.GpuCore,
                        MinSafePwm = FanChannel.DefaultMinSafePwm(FanKind.Gpu),
                        Capabilities = "NVAPI cooler policy (manual PWM)",
                        VendorHint = FanVendorHint.Nvidia
                    });
                }

                IsAvailable = channels.Count > 0;
                StatusMessage = IsAvailable
                    ? $"Ready · {_gpu.FullName}"
                    : "NVIDIA GPU detected · fan control not exposed";
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                StatusMessage = $"NVAPI unavailable: {ex.Message}";
            }

            return channels;
        }

        public FanLiveReading? Read(string channelId)
        {
            if (_gpu == null || !TryParseCoolerId(channelId, out int coolerId))
                return null;

            try
            {
                var cooler = _gpu.CoolerInformation?.Coolers?.FirstOrDefault(c => c.CoolerId == coolerId);
                if (cooler == null) return new FanLiveReading { ChannelId = channelId };
                return new FanLiveReading
                {
                    ChannelId = channelId,
                    Rpm = cooler.CurrentFanSpeedInRPM > 0 ? cooler.CurrentFanSpeedInRPM : null,
                    PwmPercent = cooler.CurrentLevel
                };
            }
            catch
            {
                return new FanLiveReading { ChannelId = channelId };
            }
        }

        public FanApplyResult SetPwm(string channelId, int percent)
        {
            if (_gpu == null)
                return FanApplyResult.Fail("NVIDIA GPU / NVAPI not available");
            if (!TryParseCoolerId(channelId, out int coolerId))
                return FanApplyResult.Fail("Unknown NVIDIA fan channel");

            percent = Math.Clamp(percent, FanChannel.DefaultMinSafePwm(FanKind.Gpu), 100);
            try
            {
                _gpu.CoolerInformation.SetCoolerSettings(coolerId, CoolerPolicy.Manual, percent);
                return FanApplyResult.Ok($"NVAPI PWM {percent}%", percent);
            }
            catch (Exception ex)
            {
                return FanApplyResult.Fail(ex.Message);
            }
        }

        public FanApplyResult RestoreDefaults(string? channelId = null)
        {
            if (_gpu == null)
                return FanApplyResult.Fail("NVIDIA GPU / NVAPI not available");

            try
            {
                if (channelId != null && TryParseCoolerId(channelId, out int coolerId))
                    _gpu.CoolerInformation.RestoreCoolerSettingsToDefault(new[] { coolerId });
                else
                    _gpu.CoolerInformation.RestoreCoolerSettingsToDefault();
                return FanApplyResult.Ok("NVAPI cooler restored to default");
            }
            catch (Exception ex)
            {
                return FanApplyResult.Fail(ex.Message);
            }
        }

        private static bool TryParseCoolerId(string channelId, out int coolerId)
        {
            coolerId = 0;
            const string marker = "cooler";
            int idx = channelId.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            return int.TryParse(channelId[(idx + marker.Length)..], out coolerId);
        }
    }
}
