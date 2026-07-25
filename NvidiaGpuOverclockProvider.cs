using System;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using static NvAPIWrapper.Native.GPU.Structures.PerformanceStates20InfoV1;
using static NvAPIWrapper.Native.GPU.Structures.PrivatePowerPoliciesStatusV1;

namespace FPSOverlay
{
    /// <summary>
    /// NVIDIA GPU overclock via NVAPI (core/mem offset + power limit). green team go.
    /// </summary>
    public sealed class NvidiaGpuOverclockProvider : IGpuOverclockProvider
    {
        private PhysicalGPU? _gpu;

        public string Name => "NVIDIA NVAPI";
        public string Vendor => "NVIDIA";
        public bool IsAvailable { get; private set; }
        public string StatusMessage { get; private set; } = "Not initialized";

        public NvidiaGpuOverclockProvider()
        {
            TryInitialize();
        }

        private void TryInitialize()
        {
            try
            {
                var gpus = PhysicalGPU.GetPhysicalGPUs();
                _gpu = gpus?.FirstOrDefault();
                if (_gpu == null)
                {
                    IsAvailable = false;
                    StatusMessage = "No NVIDIA GPU detected";
                    return;
                }

                IsAvailable = true;
                StatusMessage = $"Ready · {_gpu.FullName}";
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                StatusMessage = $"NVAPI unavailable: {ex.Message}";
            }
        }

        public OverclockApplyResult Apply(OverclockTarget target)
        {
            if (!IsAvailable || _gpu == null)
                return Fail("NVIDIA GPU / NVAPI not available");

            try
            {
                ApplyClockOffsets(target.GpuCoreOffsetMhz, target.GpuMemoryOffsetMhz);
                string plMsg = "PL stock";
                if (target.GpuPowerLimitPercent is int pl)
                {
                    ApplyPowerLimitPercent(pl);
                    plMsg = $"PL {pl}%";
                }

                return new OverclockApplyResult
                {
                    Success = true,
                    Message = $"Applied Core +{target.GpuCoreOffsetMhz} / Mem +{target.GpuMemoryOffsetMhz} / {plMsg}",
                    Applied = target
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        public OverclockApplyResult RestoreDefaults()
        {
            return Apply(OcProfileStore.SafeStock.ToTarget());
        }

        private void ApplyClockOffsets(int coreOffsetMhz, int memOffsetMhz)
        {
            if (_gpu == null) return;

            var handle = _gpu.Handle;
            // NVAPI deltas live in kHz world
            int coreDeltaKHz = coreOffsetMhz * 1000;
            int memDeltaKHz = memOffsetMhz * 1000;

            var coreClock = new PerformanceStates20ClockEntryV1(
                PublicClockDomain.Graphics,
                new PerformanceStates20ParameterDelta(coreDeltaKHz));

            var memClock = new PerformanceStates20ClockEntryV1(
                PublicClockDomain.Memory,
                new PerformanceStates20ParameterDelta(memDeltaKHz));

            var p0 = new PerformanceState20(
                PerformanceStateId.P0_3DPerformance,
                new[] { coreClock, memClock },
                Array.Empty<PerformanceStates20BaseVoltageEntryV1>());

            var info = new PerformanceStates20InfoV1(new[] { p0 }, 2, 0);
            GPUApi.SetPerformanceStates20(handle, info);
        }

        private void ApplyPowerLimitPercent(int percent)
        {
            if (_gpu == null) return;

            percent = Math.Clamp(percent, 50, 130);
            var handle = _gpu.Handle;

            var policyInfo = GPUApi.ClientPowerPoliciesGetInfo(handle);
            var entries = policyInfo.PowerPolicyInfoEntries;
            if (entries == null || entries.Length == 0)
                return;

            var infoEntry = entries[0];
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].PerformanceStateId == PerformanceStateId.P0_3DPerformance)
                {
                    infoEntry = entries[i];
                    break;
                }
            }

            uint min = infoEntry.MinimumPowerInPCM;
            uint max = infoEntry.MaximumPowerInPCM;
            uint def = infoEntry.DefaultPowerInPCM;

            // % vs default, clamped to HW min/max so we don't cook the silicon
            ulong target = (ulong)def * (ulong)percent / 100UL;
            if (target < min) target = min;
            if (target > max) target = max;

            var statusEntry = new PowerPolicyStatusEntry((uint)target);
            var status = new PrivatePowerPoliciesStatusV1(new[] { statusEntry });
            GPUApi.ClientPowerPoliciesSetStatus(handle, status);
        }

        private static OverclockApplyResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };
    }
}
