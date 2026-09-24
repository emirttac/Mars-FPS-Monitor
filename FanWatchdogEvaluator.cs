using System;
using System.Collections.Generic;

namespace FPSOverlay
{
    public readonly struct FanWatchdogChannelSample
    {
        public FanKind Kind { get; init; }
        public bool CanWritePwm { get; init; }
        public bool CanReadRpm { get; init; }
        public float? Rpm { get; init; }
    }

    public readonly struct FanWatchdogResult
    {
        public int NewStreak { get; init; }
        public int NewUnverifiedStreak { get; init; }
        public bool ShouldFailClosed { get; init; }
        public string? ThermalReason { get; init; }
    }

    /// <summary>
    /// Stalled-fan watchdog plus a hotter limit for writable channels that cannot report RPM.
    /// RPM channels: 85°C and at most 200 RPM for 5 seconds.
    /// Unverified channels: 90°C for 5 seconds. Missing RPM is not treated as a stall.
    /// </summary>
    public static class FanWatchdogEvaluator
    {
        public const float HotCpuC = 85f;
        public const float HotGpuC = 85f;
        public const float UnverifiedHotC = 90f;
        public const float StalledRpm = 200f;
        public const int SecondsRequired = 5;

        public static FanWatchdogResult Evaluate(
            float cpuC,
            float gpuC,
            IReadOnlyList<FanWatchdogChannelSample> channels,
            int currentStreak,
            int currentUnverifiedStreak = 0)
        {
            bool hotCpu = cpuC >= HotCpuC;
            bool hotGpu = gpuC >= HotGpuC;
            bool stalled = false;
            bool unverifiedHot = false;

            if ((hotCpu || hotGpu) && channels != null)
            {
                foreach (var row in channels)
                {
                    if (!row.CanWritePwm)
                        continue;

                    if (!row.CanReadRpm)
                    {
                        if (IsUnverifiedHot(row.Kind, cpuC, gpuC))
                            unverifiedHot = true;
                        continue;
                    }

                    if (!IsRelevant(row.Kind, hotCpu, hotGpu))
                        continue;
                    float rpm = row.Rpm ?? 0;
                    if (rpm <= StalledRpm)
                        stalled = true;
                }
            }

            int streak = stalled ? currentStreak + 1 : 0;
            int unverified = unverifiedHot ? currentUnverifiedStreak + 1 : 0;
            bool stallClosed = streak >= SecondsRequired;
            bool unverifiedClosed = unverified >= SecondsRequired;

            string? reason = null;
            if (stallClosed)
                reason = $"watchdog {streak}/{SecondsRequired} · hot + stalled RPM";
            else if (unverifiedClosed)
                reason = $"watchdog {unverified}/{SecondsRequired} · hot + unverified RPM";
            else if (unverified > 0)
                reason = $"watchdog {unverified}/{SecondsRequired} · hot + unverified RPM";
            else if (streak > 0)
                reason = $"watchdog {streak}/{SecondsRequired} · hot + stalled RPM";

            return new FanWatchdogResult
            {
                NewStreak = streak,
                NewUnverifiedStreak = unverified,
                ShouldFailClosed = stallClosed || unverifiedClosed,
                ThermalReason = reason
            };
        }

        private static bool IsRelevant(FanKind kind, bool hotCpu, bool hotGpu)
        {
            if (kind == FanKind.Cpu)
                return hotCpu;
            if (kind == FanKind.Gpu)
                return hotGpu;
            if (kind is FanKind.Chassis or FanKind.Unknown or FanKind.Pump)
                return hotCpu || hotGpu;
            return false;
        }

        private static bool IsUnverifiedHot(FanKind kind, float cpuC, float gpuC)
        {
            bool hotCpu = cpuC >= UnverifiedHotC;
            bool hotGpu = gpuC >= UnverifiedHotC;
            return IsRelevant(kind, hotCpu, hotGpu);
        }
    }
}
