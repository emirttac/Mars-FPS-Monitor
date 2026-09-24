using System;
using System.Globalization;

namespace FPSOverlay
{
    public static class GameStatsFormatter
    {
        public static void Apply(GameItem item, GameLifetimeStats? stats, UiStrings strings)
        {
            string empty = string.IsNullOrEmpty(strings.LibraryStatsEmpty) ? "—" : strings.LibraryStatsEmpty;
            item.StatsLowLabel = string.IsNullOrEmpty(strings.LibraryStatsLowLabel) ? "1%" : strings.LibraryStatsLowLabel;

            if (stats == null || !stats.HasDisplayableSamples)
            {
                item.StatsFpsValue = empty;
                item.StatsLowValue = empty;
                item.StatsGpuMax = empty;
                item.StatsGpuAvg = "";
                item.StatsCpuMax = empty;
                item.StatsCpuAvg = "";
                return;
            }

            item.StatsFpsValue = stats.FpsSampleSeconds > 0
                ? Math.Round(stats.AvgFps).ToString(CultureInfo.InvariantCulture)
                : empty;
            item.StatsLowValue = stats.OnePercentLowFps > 0
                ? Math.Round(stats.OnePercentLowFps).ToString(CultureInfo.InvariantCulture)
                : empty;

            item.StatsGpuMax = Degrees(stats.GpuSampleSeconds, stats.MaxGpuC, empty);
            item.StatsGpuAvg = Average(stats.GpuSampleSeconds, stats.AvgGpuC, strings);
            item.StatsCpuMax = Degrees(stats.CpuSampleSeconds, stats.MaxCpuC, empty);
            item.StatsCpuAvg = Average(stats.CpuSampleSeconds, stats.AvgCpuC, strings);
        }

        private static string Degrees(int samples, double value, string empty)
        {
            if (samples <= 0)
                return empty;
            return Math.Round(value).ToString(CultureInfo.InvariantCulture) + "°";
        }

        private static string Average(int samples, double value, UiStrings strings)
        {
            if (samples <= 0)
                return "";
            string degrees = Math.Round(value).ToString(CultureInfo.InvariantCulture) + "°";
            string format = string.IsNullOrEmpty(strings.LibraryStatsAvg) ? "avg {0}" : strings.LibraryStatsAvg;
            return string.Format(CultureInfo.InvariantCulture, format, degrees);
        }
    }
}
