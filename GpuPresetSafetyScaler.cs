using System;
using System.Collections.Generic;
using System.Linq;

namespace FPSOverlay
{
    /// <summary>
    /// Projects a remote GPU catalog into the per-mode safety caps while keeping
    /// card-to-card order. A flagship stays above a laptop, and neither exceeds the cap.
    /// </summary>
    public static class GpuPresetSafetyScaler
    {
        public readonly record struct Band(int CoreMin, int CoreMax, int MemMin, int MemMax, int PowerMin, int PowerMax);

        public static readonly Band Performance = new(20, 50, 40, 100, 100, 105);
        public static readonly Band Extreme = new(40, 100, 80, 300, 100, 110);

        public static GpuPresetTiers Scale(GpuPresetTiers source, IEnumerable<GpuPresetTiers>? population)
        {
            var all = new List<GpuPresetTiers>();
            if (population != null)
                all.AddRange(population.Where(t => t != null));
            if (!all.Contains(source))
                all.Add(source);

            return new GpuPresetTiers
            {
                Eco = ScaleEco(source.Eco),
                Performance = ScaleBand(source.Performance, all.Select(t => t.Performance), Performance),
                Extreme = ScaleBand(source.Extreme, all.Select(t => t.Extreme), Extreme)
            };
        }

        private static GpuPresetOffsets? ScaleEco(GpuPresetOffsets? source)
        {
            if (source == null)
                return null;

            return new GpuPresetOffsets
            {
                Core = 0,
                Mem = 0,
                Power = Math.Clamp(source.Power <= 0 ? 100 : source.Power, AiOcSafetyClamp.MinPowerLimitPercent, 100)
            };
        }

        private static GpuPresetOffsets? ScaleBand(
            GpuPresetOffsets? source,
            IEnumerable<GpuPresetOffsets?> population,
            Band band)
        {
            if (source == null)
                return null;

            var rows = population.Where(t => t != null).Cast<GpuPresetOffsets>().ToList();
            if (rows.Count == 0)
                rows.Add(source);

            return new GpuPresetOffsets
            {
                Core = Map(source.Core, Span(rows.Select(r => r.Core)), band.CoreMin, band.CoreMax),
                Mem = Map(source.Mem, Span(rows.Select(r => r.Mem)), band.MemMin, band.MemMax),
                Power = MapPower(source.Power, Span(rows.Select(r => r.Power)), band.PowerMin, band.PowerMax)
            };
        }

        private static (int Min, int Max) Span(IEnumerable<int> values)
        {
            int min = int.MaxValue;
            int max = 0;
            foreach (int value in values)
            {
                if (value <= 0)
                    continue;
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (min == int.MaxValue)
                return (0, 0);
            return (min, max);
        }

        private static int Map(int value, (int Min, int Max) span, int outMin, int outMax)
        {
            if (value <= 0)
                return 0;
            if (span.Max <= span.Min)
                return Math.Clamp(value, 0, outMax);

            double t = (value - span.Min) / (double)(span.Max - span.Min);
            t = Math.Clamp(t, 0, 1);
            return Math.Clamp((int)Math.Round(outMin + t * (outMax - outMin)), 0, outMax);
        }

        private static int MapPower(int value, (int Min, int Max) span, int outMin, int outMax)
        {
            if (value <= 0)
                return 100;
            if (value <= outMin)
                return Math.Clamp(value, AiOcSafetyClamp.MinPowerLimitPercent, outMin);
            if (span.Max <= span.Min)
                return Math.Clamp(value, outMin, outMax);

            double t = (value - span.Min) / (double)(span.Max - span.Min);
            t = Math.Clamp(t, 0, 1);
            return Math.Clamp((int)Math.Round(outMin + t * (outMax - outMin)), outMin, outMax);
        }
    }
}
