using System.Collections.Generic;

namespace FPSOverlay
{
    /// <summary>
    /// Absolute ceilings applied before any GPU register write.
    /// AI suggestions use stricter per-mode caps in <see cref="AiOcSafetyClamp"/>.
    /// Manual profiles, imported JSON, and provider calls all stop here.
    /// </summary>
    public static class OcHardwareLimits
    {
        public const int MinCoreOffsetMhz = AiOcSafetyClamp.MinCoreOffsetMhz;
        public const int MaxCoreOffsetMhz = AiOcSafetyClamp.MaxCoreOffsetMhz;
        public const int MinMemoryOffsetMhz = AiOcSafetyClamp.MinMemoryOffsetMhz;
        public const int MaxMemoryOffsetMhz = AiOcSafetyClamp.MaxMemoryOffsetMhz;
        public const int MinPowerLimitPercent = AiOcSafetyClamp.MinPowerLimitPercent;
        public const int MaxPowerLimitPercent = AiOcSafetyClamp.MaxPowerLimitPercent;
        public const int MinTempC = AiOcSafetyClamp.MinTempC;
        public const int MaxTempC = AiOcSafetyClamp.MaxTempC;

        public static OverclockTarget ClampTarget(OverclockTarget target)
        {
            var profile = new OcProfile
            {
                ProfileName = "apply",
                CoreOffsetMhz = target.GpuCoreOffsetMhz,
                MemoryOffsetMhz = target.GpuMemoryOffsetMhz,
                PowerLimitPercent = target.GpuPowerLimitPercent
            };
            ClampProfile(profile);
            return profile.ToTarget();
        }

        /// <summary>Clamps in place. Returns true when any field changed.</summary>
        public static bool ClampProfile(OcProfile profile, ICollection<string>? log = null)
        {
            bool changed = false;
            profile.CoreOffsetMhz = ClampInt(profile.CoreOffsetMhz, MinCoreOffsetMhz, MaxCoreOffsetMhz, "core_offset_mhz", log, ref changed);
            profile.MemoryOffsetMhz = ClampInt(profile.MemoryOffsetMhz, MinMemoryOffsetMhz, MaxMemoryOffsetMhz, "memory_offset_mhz", log, ref changed);

            if (profile.PowerLimitPercent is int pl)
            {
                int clamped = ClampInt(pl, MinPowerLimitPercent, MaxPowerLimitPercent, "power_limit_percent", log, ref changed);
                profile.PowerLimitPercent = clamped;
            }

            int min = ClampInt(profile.MinTemp, MinTempC, MaxTempC, "min_temp", log, ref changed);
            int max = ClampInt(profile.MaxTemp, MinTempC, MaxTempC, "max_temp", log, ref changed);
            if (max < min)
            {
                log?.Add($"temp_band: max_temp {max} < min_temp {min} → swapped");
                (min, max) = (max, min);
                changed = true;
            }

            profile.MinTemp = min;
            profile.MaxTemp = max;
            return changed;
        }

        /// <summary>Percent of a baseline power value, then clamped to the hardware min/max when those are known.</summary>
        public static int UnitsFromPercent(int baseline, int percent, int min, int max)
        {
            percent = Math.Clamp(percent, MinPowerLimitPercent, MaxPowerLimitPercent);
            long target = (long)baseline * percent / 100L;
            if (max > min && max > 0)
            {
                if (target < min) target = min;
                if (target > max) target = max;
            }
            if (target < 0) target = 0;
            if (target > int.MaxValue) target = int.MaxValue;
            return (int)target;
        }

        public static double WattsFromPercent(double baselineWatts, int percent)
        {
            percent = Math.Clamp(percent, MinPowerLimitPercent, MaxPowerLimitPercent);
            return baselineWatts * percent / 100.0;
        }

        private static int ClampInt(int value, int min, int max, string field, ICollection<string>? log, ref bool changed)
        {
            int clamped = Math.Clamp(value, min, max);
            if (clamped != value)
            {
                changed = true;
                log?.Add($"{field}: {value} → {clamped} (limits {min}..{max})");
            }
            return clamped;
        }
    }
}
