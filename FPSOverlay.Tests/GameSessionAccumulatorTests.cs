namespace FPSOverlay.Tests;

public class GameSessionAccumulatorTests
{
    [Fact]
    public void Warmup_CountsTime_AndSkipsAverages()
    {
        var session = new GameSessionAccumulator();
        for (int i = 0; i < GameSessionAccumulator.WarmupSeconds; i++)
            session.Tick(conditionsMet: true, fps: 200, onePercentLow: 100, cpuTempC: 90, gpuTempC: 95);

        Assert.Equal(GameSessionAccumulator.WarmupSeconds, session.ElapsedSeconds);
        Assert.Equal(0, session.FpsSampleSeconds);
        Assert.Equal(0, session.AvgFps);
        Assert.False(session.HasOnePercentLow);
        Assert.Equal(0, session.MaxCpuC);
        Assert.Equal(0, session.MaxGpuC);

        session.Tick(conditionsMet: true, fps: 100, onePercentLow: 40, cpuTempC: 70, gpuTempC: 60);

        Assert.Equal(1, session.FpsSampleSeconds);
        Assert.Equal(100, session.AvgFps);
        Assert.Equal(40, session.OnePercentLowFps);
        Assert.Equal(70, session.MaxCpuC);
        Assert.Equal(60, session.MaxGpuC);
        Assert.Equal(70, session.AvgCpuC);
        Assert.Equal(60, session.AvgGpuC);
    }

    [Fact]
    public void HysteresisTick_DoesNotSample_ButAdvancesTime()
    {
        var session = Warmed();
        session.Tick(conditionsMet: true, fps: 120, onePercentLow: 80, cpuTempC: 65, gpuTempC: 72);
        session.Tick(conditionsMet: false, fps: 10, onePercentLow: 5, cpuTempC: 99, gpuTempC: 99);

        Assert.Equal(1, session.FpsSampleSeconds);
        Assert.Equal(120, session.AvgFps);
        Assert.Equal(80, session.OnePercentLowFps);
        Assert.Equal(65, session.MaxCpuC);
        Assert.Equal(72, session.MaxGpuC);
        Assert.Equal(GameSessionAccumulator.WarmupSeconds + 2, session.ElapsedSeconds);
    }

    [Fact]
    public void InvalidFpsAndMissingTemps_AreIgnored()
    {
        var session = Warmed();
        session.Tick(conditionsMet: true, fps: 80, onePercentLow: 50, cpuTempC: 60, gpuTempC: 70);
        session.Tick(conditionsMet: true, fps: 0, onePercentLow: 0, cpuTempC: 0, gpuTempC: 0);
        session.Tick(conditionsMet: true, fps: -1, onePercentLow: -1, cpuTempC: -5, gpuTempC: -5);
        session.Tick(conditionsMet: true, fps: 40, onePercentLow: 20, cpuTempC: 0, gpuTempC: 80);

        Assert.Equal(2, session.FpsSampleSeconds);
        Assert.Equal(60, session.AvgFps);
        Assert.Equal(20, session.OnePercentLowFps);
        Assert.Equal(1, session.CpuSampleSeconds);
        Assert.Equal(60, session.AvgCpuC);
        Assert.Equal(60, session.MaxCpuC);
        Assert.Equal(2, session.GpuSampleSeconds);
        Assert.Equal(75, session.AvgGpuC);
        Assert.Equal(80, session.MaxGpuC);
    }

    [Fact]
    public void Merge_WeightsAverages_TakesMaxTemp_AndMinOnePercent()
    {
        var session = Warmed();
        for (int i = 0; i < 10; i++)
            session.Tick(conditionsMet: true, fps: 40, onePercentLow: 30, cpuTempC: 70, gpuTempC: 60);

        var prior = new GameLifetimeStats
        {
            Key = "exe:game",
            AvgFps = 100,
            FpsSampleSeconds = 10,
            OnePercentLowFps = 80,
            AvgCpuC = 50,
            CpuSampleSeconds = 10,
            MaxCpuC = 55,
            AvgGpuC = 60,
            GpuSampleSeconds = 10,
            MaxGpuC = 65,
            PlaySeconds = 100,
            SessionCount = 2
        };

        var utc = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var merged = GameSessionAccumulator.Merge(prior, session, utc, "exe:game");

        Assert.Equal(70, merged.AvgFps, 3);
        Assert.Equal(20, merged.FpsSampleSeconds);
        Assert.Equal(30, merged.OnePercentLowFps);
        Assert.Equal(60, merged.AvgCpuC, 3);
        Assert.Equal(20, merged.CpuSampleSeconds);
        Assert.Equal(70, merged.MaxCpuC);
        Assert.Equal(60, merged.AvgGpuC, 3);
        Assert.Equal(65, merged.MaxGpuC);
        Assert.Equal(100 + GameSessionAccumulator.WarmupSeconds + 10, merged.PlaySeconds);
        Assert.Equal(3, merged.SessionCount);
        Assert.Equal(utc, merged.LastPlayedUtc);
        Assert.Equal("exe:game", merged.Key);
    }

    [Fact]
    public void Merge_WarmupOnlySession_KeepsAverages_AndAddsPlayTime()
    {
        var session = new GameSessionAccumulator();
        for (int i = 0; i < GameSessionAccumulator.WarmupSeconds; i++)
            session.Tick(conditionsMet: true, fps: 400, onePercentLow: 10, cpuTempC: 95, gpuTempC: 95);

        var prior = new GameLifetimeStats
        {
            AvgFps = 144,
            FpsSampleSeconds = 4,
            OnePercentLowFps = 90,
            AvgCpuC = 61,
            CpuSampleSeconds = 4,
            MaxCpuC = 70,
            AvgGpuC = 68,
            GpuSampleSeconds = 4,
            MaxGpuC = 75,
            PlaySeconds = 40,
            SessionCount = 1
        };

        var merged = GameSessionAccumulator.Merge(prior, session, DateTime.UtcNow, "exe:only-warmup");

        Assert.Equal(144, merged.AvgFps, 3);
        Assert.Equal(4, merged.FpsSampleSeconds);
        Assert.Equal(90, merged.OnePercentLowFps);
        Assert.Equal(70, merged.MaxCpuC);
        Assert.Equal(75, merged.MaxGpuC);
        Assert.Equal(40 + GameSessionAccumulator.WarmupSeconds, merged.PlaySeconds);
        Assert.Equal(2, merged.SessionCount);
    }

    [Fact]
    public void Merge_NullPrior_UsesSessionAlone()
    {
        var session = Warmed();
        session.Tick(conditionsMet: true, fps: 90, onePercentLow: 45, cpuTempC: 55, gpuTempC: 66);

        var merged = GameSessionAccumulator.Merge(null, session, DateTime.UtcNow, "exe:first");

        Assert.Equal(90, merged.AvgFps, 3);
        Assert.Equal(1, merged.FpsSampleSeconds);
        Assert.Equal(45, merged.OnePercentLowFps);
        Assert.Equal(55, merged.MaxCpuC);
        Assert.Equal(66, merged.MaxGpuC);
        Assert.Equal(1, merged.SessionCount);
        Assert.Equal(GameSessionAccumulator.WarmupSeconds + 1, merged.PlaySeconds);
    }

    private static GameSessionAccumulator Warmed()
    {
        var session = new GameSessionAccumulator();
        for (int i = 0; i < GameSessionAccumulator.WarmupSeconds; i++)
            session.Tick(conditionsMet: false, fps: 0, onePercentLow: 0, cpuTempC: 0, gpuTempC: 0);
        return session;
    }
}
