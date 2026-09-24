using System;
using LibreHardwareMonitor.Hardware;

namespace FPSOverlay
{
    public static class GpuOverclockProviderFactory
    {
        /// <summary>
        /// Binds the provider to <paramref name="selectedGpuName"/>.
        /// A named NVIDIA card is never written through AMD or Intel, and the reverse is true.
        /// No name match means the provider stays unavailable.
        /// </summary>
        public static IGpuOverclockProvider Create(Computer? computer, string? selectedGpuName = null)
        {
            var vendor = GpuNameMatch.VendorOf(selectedGpuName);

            if (GpuNameMatch.ShouldUseProvider(vendor, GpuNameMatch.Vendor.Nvidia))
            {
                var nvidia = new NvidiaGpuOverclockProvider(selectedGpuName);
                if (nvidia.IsAvailable || vendor == GpuNameMatch.Vendor.Nvidia)
                    return nvidia;
            }

            if (GpuNameMatch.ShouldUseProvider(vendor, GpuNameMatch.Vendor.Amd))
            {
                var amd = new AmdGpuOverclockProvider(computer, selectedGpuName);
                if (amd.IsAvailable || vendor == GpuNameMatch.Vendor.Amd)
                    return amd;
                amd.Dispose();
            }

            if (GpuNameMatch.ShouldUseProvider(vendor, GpuNameMatch.Vendor.Intel))
            {
                var intel = new IntelArcGpuOverclockProvider(computer, selectedGpuName);
                if (intel.IsAvailable || vendor == GpuNameMatch.Vendor.Intel)
                    return intel;
                intel.Dispose();
            }

            return new NvidiaGpuOverclockProvider(selectedGpuName);
        }
    }
}
