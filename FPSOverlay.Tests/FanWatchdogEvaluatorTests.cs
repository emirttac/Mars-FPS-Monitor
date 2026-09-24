namespace FPSOverlay.Tests;

public class FanWatchdogEvaluatorTests
{
    private static FanWatchdogChannelSample CpuStalled() => new()
    {
        Kind = FanKind.Cpu,
        CanWritePwm = true,
        CanReadRpm = true,
        Rpm = 50
    };

    private static FanWatchdogChannelSample CpuSpinning() => new()
    {
        Kind = FanKind.Cpu,
        CanWritePwm = true,
        CanReadRpm = true,
        Rpm = 1200
    };

    [Fact]
    public void CoolTemps_ResetStreak()
    {
        var r = FanWatchdogEvaluator.Evaluate(40, 40, new[] { CpuStalled() }, currentStreak: 3);
        Assert.Equal(0, r.NewStreak);
        Assert.False(r.ShouldFailClosed);
    }

    [Fact]
    public void HotButSpinning_ResetStreak()
    {
        var r = FanWatchdogEvaluator.Evaluate(90, 90, new[] { CpuSpinning() }, currentStreak: 2);
        Assert.Equal(0, r.NewStreak);
        Assert.False(r.ShouldFailClosed);
    }

    [Fact]
    public void HotAndStalled_IncrementsUntilFailClosed()
    {
        int streak = 0;
        FanWatchdogResult r = default;
        for (int i = 0; i < FanWatchdogEvaluator.SecondsRequired; i++)
        {
            r = FanWatchdogEvaluator.Evaluate(90, 50, new[] { CpuStalled() }, streak);
            streak = r.NewStreak;
        }
        Assert.True(r.ShouldFailClosed);
        Assert.Equal(FanWatchdogEvaluator.SecondsRequired, r.NewStreak);
        Assert.Contains("watchdog", r.ThermalReason);
    }

    [Fact]
    public void FourSeconds_NotYetFailClosed()
    {
        int streak = 0;
        FanWatchdogResult r = default;
        for (int i = 0; i < FanWatchdogEvaluator.SecondsRequired - 1; i++)
        {
            r = FanWatchdogEvaluator.Evaluate(90, 50, new[] { CpuStalled() }, streak);
            streak = r.NewStreak;
        }
        Assert.False(r.ShouldFailClosed);
        Assert.Equal(4, r.NewStreak);
    }

    [Fact]
    public void ReadOnlyChannel_Ignored()
    {
        var ro = new FanWatchdogChannelSample
        {
            Kind = FanKind.Cpu,
            CanWritePwm = false,
            CanReadRpm = true,
            Rpm = 0
        };
        var r = FanWatchdogEvaluator.Evaluate(90, 90, new[] { ro }, 0);
        Assert.Equal(0, r.NewStreak);
        Assert.False(r.ShouldFailClosed);
    }
}
