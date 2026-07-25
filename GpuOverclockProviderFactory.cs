using System;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    public static class GpuOverclockProviderFactory
    {
        /// <summary>
        /// Picks the first GPU OC backend that actually works: NVIDIA → AMD → Intel Arc.
        /// </summary>
        public static IGpuOverclockProvider Create(Computer? computer)
        {
            var nvidia = new NvidiaGpuOverclockProvider();
            if (nvidia.IsAvailable)
                return nvidia;

            var amd = new AmdGpuOverclockProvider(computer);
            if (amd.IsAvailable)
                return amd;

            var intel = new IntelArcGpuOverclockProvider(computer);
            if (intel.IsAvailable)
                return intel;

            // if unsupported, say the MOST useful "detected but nope" message
            if (!string.IsNullOrEmpty(amd.StatusMessage) && amd.StatusMessage.Contains("detected", StringComparison.OrdinalIgnoreCase))
                return amd;
            if (!string.IsNullOrEmpty(intel.StatusMessage) && intel.StatusMessage.Contains("detected", StringComparison.OrdinalIgnoreCase))
                return intel;

            return nvidia.StatusMessage.Contains("No NVIDIA", StringComparison.OrdinalIgnoreCase)
                ? amd
                : nvidia;
        }
    }
}
