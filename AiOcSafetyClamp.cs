using System;
using System.Collections.Generic;
using System.Linq;

namespace FPSOverlay
{
    /// <summary>
    /// Hardcoded safety firewall. AI numbers? we don't trust them raw —
    /// every field gets clamped to our chill software ceilings.
    /// </summary>
    public static class AiOcSafetyClamp
    {
        // hard ceilings — API/config cannot juice them. period.
        public const int MaxCoreOffsetMhz = 100;
        public const int MaxMemoryOffsetMhz = 300;
        public const int MinCoreOffsetMhz = 0;      // AI path: no underclock suggestions (we're chill not evil)
        public const int MinMemoryOffsetMhz = 0;
        public const int MinPowerLimitPercent = 80; // don't let AI nuke power limit into the dirt
        public const int MaxPowerLimitPercent = 110;

        public const int MinTempC = 0;
        public const int MaxTempC = 105;

        /// <summary>Per-mode soft caps on top of absolute ceilings (extra paranoia, healthy).</summary>
        public static readonly IReadOnlyDictionary<string, (int Core, int Mem, int? MaxPl)> ModeSoftCaps =
            new Dictionary<string, (int, int, int?)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Eco"] = (0, 0, 100),
                ["Performance"] = (50, 100, 105),
                ["Extreme"] = (100, 300, 110)
            };

        public static AiOcClampResult Apply(AiOcResponse? raw, AiOcHardwareSnapshot? hardware = null)
        {
            var log = new List<string>();
            var output = new AiOcResponse
            {
                SchemaVersion = 1,
                Model = raw?.Model,
                Notes = raw?.Notes,
                Warnings = raw?.Warnings?.ToList() ?? new List<string>()
            };

            if (raw?.Recommendations == null || raw.Recommendations.Count == 0)
            {
                output.Warnings.Add("empty_recommendations");
                return new AiOcClampResult { Response = output, ClampLog = log };
            }

            int hwPlCap = hardware?.MaxPowerLimitPercent > 0
                ? Math.Min(hardware.MaxPowerLimitPercent, MaxPowerLimitPercent)
                : MaxPowerLimitPercent;

            foreach (var src in raw.Recommendations)
            {
                var dst = ClampOne(src, hwPlCap, log);
                if (dst != null)
                    output.Recommendations.Add(dst);
            }

            // keep only Eco/Perf/Extreme — last one wins per mode
            output.Recommendations = DeduplicateModes(output.Recommendations);
            return new AiOcClampResult { Response = output, ClampLog = log };
        }

        public static AiOcRecommendation? ClampOne(AiOcRecommendation src, int hwPlCap, List<string>? log = null)
        {
            if (src == null) return null;

            string mode = NormalizeMode(src.Mode);
            var soft = ModeSoftCaps.TryGetValue(mode, out var caps)
                ? caps
                : (MaxCoreOffsetMhz, MaxMemoryOffsetMhz, MaxPowerLimitPercent);

            int core = ClampInt(src.CoreOffsetMhz, MinCoreOffsetMhz, Math.Min(MaxCoreOffsetMhz, soft.Core),
                $"{mode}.core_offset_mhz", log);
            int mem = ClampInt(src.MemoryOffsetMhz, MinMemoryOffsetMhz, Math.Min(MaxMemoryOffsetMhz, soft.Mem),
                $"{mode}.memory_offset_mhz", log);

            int? pl = src.PowerLimitPercent;
            if (pl.HasValue)
            {
                int plMax = Math.Min(hwPlCap, soft.MaxPl ?? MaxPowerLimitPercent);
                int clampedPl = ClampInt(pl.Value, MinPowerLimitPercent, plMax, $"{mode}.power_limit_percent", log);
                pl = clampedPl;
            }

            int minTemp = ClampInt(src.MinTemp, MinTempC, MaxTempC, $"{mode}.min_temp", log);
            int maxTemp = ClampInt(src.MaxTemp, MinTempC, MaxTempC, $"{mode}.max_temp", log);
            if (maxTemp < minTemp)
            {
                log?.Add($"{mode}.temp_band: max_temp {maxTemp} < min_temp {minTemp} → swapped");
                (minTemp, maxTemp) = (maxTemp, minTemp);
            }

            string name = string.IsNullOrWhiteSpace(src.ProfileName)
                ? $"AI {mode}"
                : src.ProfileName.Trim();
            if (name.Length > 48) name = name[..48];

            return new AiOcRecommendation
            {
                Mode = mode,
                ProfileName = name,
                MinTemp = minTemp,
                MaxTemp = maxTemp,
                CoreOffsetMhz = core,
                MemoryOffsetMhz = mem,
                PowerLimitPercent = pl,
                Rationale = src.Rationale
            };
        }

        public static string NormalizeMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "Eco";
            string m = mode.Trim();
            if (m.Equals("Eco", StringComparison.OrdinalIgnoreCase) ||
                m.Equals("Economy", StringComparison.OrdinalIgnoreCase))
                return "Eco";
            if (m.Equals("Performance", StringComparison.OrdinalIgnoreCase) ||
                m.Equals("Perf", StringComparison.OrdinalIgnoreCase) ||
                m.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
                return "Performance";
            if (m.Equals("Extreme", StringComparison.OrdinalIgnoreCase) ||
                m.Equals("Max", StringComparison.OrdinalIgnoreCase))
                return "Extreme";
            return "Eco";
        }

        private static int ClampInt(int value, int min, int max, string field, List<string>? log)
        {
            int c = Math.Clamp(value, min, max);
            if (c != value)
                log?.Add($"{field}: {value} → {c} (limits {min}..{max})");
            return c;
        }

        private static List<AiOcRecommendation> DeduplicateModes(List<AiOcRecommendation> list)
        {
            var order = new[] { "Eco", "Performance", "Extreme" };
            var map = new Dictionary<string, AiOcRecommendation>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in list)
                map[r.Mode] = r;

            var result = new List<AiOcRecommendation>();
            foreach (var key in order)
            {
                if (map.TryGetValue(key, out var rec))
                    result.Add(rec);
            }
            return result;
        }
    }
}
