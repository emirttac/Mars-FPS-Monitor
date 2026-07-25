namespace FPSOverlay
{
    public enum OcControlMode
    {
        Off = 0,
        AutoThermal = 1,
        ManualFixed = 2
    }

    public readonly struct GpuThermalSample
    {
        public float? CoreTempC { get; init; }
        public float? HotspotTempC { get; init; }
        public bool IsValid => CoreTempC is > 0 and < 120;
        public static GpuThermalSample Invalid => default;
    }

    public sealed class OverclockTarget
    {
        public int GpuCoreOffsetMhz { get; init; }
        public int GpuMemoryOffsetMhz { get; init; }
        /// <summary>Null = leave power limit alone (stock is fine).</summary>
        public int? GpuPowerLimitPercent { get; init; }
    }

    public sealed class OverclockApplyResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public OverclockTarget? Applied { get; init; }
    }

    public sealed class OverclockStatus
    {
        public bool GpuAutoEnabled { get; set; }
        public OcControlMode ControlMode { get; set; } = OcControlMode.Off;
        public string ActiveProfileName { get; set; } = "Safe / Off";
        public Guid ActiveProfileId { get; set; } = OcProfileStore.SafeStock.Id;
        public string GpuProviderName { get; set; } = "None";
        public bool GpuSupported { get; set; }
        public string GpuVendor { get; set; } = "";
        public string GpuStatusMessage { get; set; } = "";
        public string ThermalReason { get; set; } = "";
        public float? LastCoreTempC { get; set; }
        public float? LastHotspotTempC { get; set; }
        public OverclockTarget? LastGpuTarget { get; set; }

        public int IntensityPercent
        {
            get
            {
                if (ControlMode == OcControlMode.Off || ActiveProfileId == OcProfileStore.SafeStock.Id)
                    return 0;
                var t = LastGpuTarget;
                if (t == null) return 0;
                // scale intensity vs our shy Extreme default (+50)
                return Math.Clamp((int)Math.Round(Math.Abs(t.GpuCoreOffsetMhz) / 50.0 * 100.0), 0, 100);
            }
        }
    }
}
