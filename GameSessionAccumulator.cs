using System;

namespace FPSOverlay
{
    /// <summary>Lifetime totals for one library game. Averages are weighted by sample seconds.</summary>
    public sealed class GameLifetimeStats
    {
        public string Key { get; set; } = "";
        public double AvgFps { get; set; }
        public int FpsSampleSeconds { get; set; }
        public float OnePercentLowFps { get; set; }
        public double AvgCpuC { get; set; }
        public int CpuSampleSeconds { get; set; }
        public int MaxCpuC { get; set; }
        public double AvgGpuC { get; set; }
        public int GpuSampleSeconds { get; set; }
        public int MaxGpuC { get; set; }
        public int PlaySeconds { get; set; }
        public int SessionCount { get; set; }
        public DateTime? LastPlayedUtc { get; set; }

        public bool HasDisplayableSamples =>
            FpsSampleSeconds > 0 || CpuSampleSeconds > 0 || GpuSampleSeconds > 0;

        public GameLifetimeStats Copy() => new()
        {
            Key = Key,
            AvgFps = AvgFps,
            FpsSampleSeconds = FpsSampleSeconds,
            OnePercentLowFps = OnePercentLowFps,
            AvgCpuC = AvgCpuC,
            CpuSampleSeconds = CpuSampleSeconds,
            MaxCpuC = MaxCpuC,
            AvgGpuC = AvgGpuC,
            GpuSampleSeconds = GpuSampleSeconds,
            MaxGpuC = MaxGpuC,
            PlaySeconds = PlaySeconds,
            SessionCount = SessionCount,
            LastPlayedUtc = LastPlayedUtc
        };
    }

    /// <summary>In-progress session written every 30s so a crash keeps the run.</summary>
    public sealed class OpenGameSession
    {
        public string Key { get; set; } = "";
        public int ElapsedSeconds { get; set; }
        public long FpsSum { get; set; }
        public int FpsSampleSeconds { get; set; }
        public float OnePercentLowFps { get; set; }
        public bool HasOnePercentLow { get; set; }
        public long CpuSum { get; set; }
        public int CpuSampleSeconds { get; set; }
        public int MaxCpuC { get; set; }
        public long GpuSum { get; set; }
        public int GpuSampleSeconds { get; set; }
        public int MaxGpuC { get; set; }
    }

    /// <summary>
    /// One play session. No sample lists: running sums, max, and the lowest 1% low.
    /// The first <see cref="WarmupSeconds"/> still count as play time but stay out of the averages.
    /// Ticks with conditionsMet false (Alt-Tab hysteresis) also count as time only.
    /// </summary>
    public sealed class GameSessionAccumulator
    {
        public const int WarmupSeconds = 8;

        public int ElapsedSeconds { get; private set; }
        public long FpsSum { get; private set; }
        public int FpsSampleSeconds { get; private set; }
        public float OnePercentLowFps { get; private set; }
        public bool HasOnePercentLow { get; private set; }
        public long CpuSum { get; private set; }
        public int CpuSampleSeconds { get; private set; }
        public int MaxCpuC { get; private set; }
        public long GpuSum { get; private set; }
        public int GpuSampleSeconds { get; private set; }
        public int MaxGpuC { get; private set; }

        public double AvgFps => FpsSampleSeconds == 0 ? 0 : (double)FpsSum / FpsSampleSeconds;
        public double AvgCpuC => CpuSampleSeconds == 0 ? 0 : (double)CpuSum / CpuSampleSeconds;
        public double AvgGpuC => GpuSampleSeconds == 0 ? 0 : (double)GpuSum / GpuSampleSeconds;

        public void Tick(bool conditionsMet, int fps, float onePercentLow, int cpuTempC, int gpuTempC)
        {
            ElapsedSeconds = AddCount(ElapsedSeconds, 1);
            if (ElapsedSeconds <= WarmupSeconds)
                return;
            if (!conditionsMet)
                return;

            if (fps > 0)
            {
                FpsSum += fps;
                FpsSampleSeconds = AddCount(FpsSampleSeconds, 1);
            }

            if (onePercentLow > 0 && float.IsFinite(onePercentLow))
            {
                if (!HasOnePercentLow || onePercentLow < OnePercentLowFps)
                    OnePercentLowFps = onePercentLow;
                HasOnePercentLow = true;
            }

            if (cpuTempC > 0)
            {
                CpuSum += cpuTempC;
                CpuSampleSeconds = AddCount(CpuSampleSeconds, 1);
                if (cpuTempC > MaxCpuC)
                    MaxCpuC = cpuTempC;
            }

            if (gpuTempC > 0)
            {
                GpuSum += gpuTempC;
                GpuSampleSeconds = AddCount(GpuSampleSeconds, 1);
                if (gpuTempC > MaxGpuC)
                    MaxGpuC = gpuTempC;
            }
        }

        public OpenGameSession ToSnapshot(string key) => new()
        {
            Key = key,
            ElapsedSeconds = ElapsedSeconds,
            FpsSum = FpsSum,
            FpsSampleSeconds = FpsSampleSeconds,
            OnePercentLowFps = OnePercentLowFps,
            HasOnePercentLow = HasOnePercentLow,
            CpuSum = CpuSum,
            CpuSampleSeconds = CpuSampleSeconds,
            MaxCpuC = MaxCpuC,
            GpuSum = GpuSum,
            GpuSampleSeconds = GpuSampleSeconds,
            MaxGpuC = MaxGpuC
        };

        public static GameSessionAccumulator FromSnapshot(OpenGameSession snapshot)
        {
            return new GameSessionAccumulator
            {
                ElapsedSeconds = Math.Max(0, snapshot.ElapsedSeconds),
                FpsSum = Math.Max(0, snapshot.FpsSum),
                FpsSampleSeconds = Math.Max(0, snapshot.FpsSampleSeconds),
                OnePercentLowFps = snapshot.OnePercentLowFps,
                HasOnePercentLow = snapshot.HasOnePercentLow && snapshot.OnePercentLowFps > 0,
                CpuSum = Math.Max(0, snapshot.CpuSum),
                CpuSampleSeconds = Math.Max(0, snapshot.CpuSampleSeconds),
                MaxCpuC = Math.Max(0, snapshot.MaxCpuC),
                GpuSum = Math.Max(0, snapshot.GpuSum),
                GpuSampleSeconds = Math.Max(0, snapshot.GpuSampleSeconds),
                MaxGpuC = Math.Max(0, snapshot.MaxGpuC)
            };
        }

        public static GameLifetimeStats Merge(GameLifetimeStats? prior, GameSessionAccumulator session, DateTime utcNow, string key)
        {
            prior ??= new GameLifetimeStats();

            int fpsSeconds = AddCount(prior.FpsSampleSeconds, session.FpsSampleSeconds);
            int cpuSeconds = AddCount(prior.CpuSampleSeconds, session.CpuSampleSeconds);
            int gpuSeconds = AddCount(prior.GpuSampleSeconds, session.GpuSampleSeconds);

            float onePercent = prior.OnePercentLowFps;
            if (prior.OnePercentLowFps > 0 && session.HasOnePercentLow)
                onePercent = Math.Min(prior.OnePercentLowFps, session.OnePercentLowFps);
            else if (session.HasOnePercentLow)
                onePercent = session.OnePercentLowFps;

            return new GameLifetimeStats
            {
                Key = key,
                AvgFps = fpsSeconds == 0 ? 0 : (prior.AvgFps * prior.FpsSampleSeconds + session.FpsSum) / fpsSeconds,
                FpsSampleSeconds = fpsSeconds,
                OnePercentLowFps = onePercent,
                AvgCpuC = cpuSeconds == 0 ? 0 : (prior.AvgCpuC * prior.CpuSampleSeconds + session.CpuSum) / cpuSeconds,
                CpuSampleSeconds = cpuSeconds,
                MaxCpuC = Math.Max(prior.MaxCpuC, session.MaxCpuC),
                AvgGpuC = gpuSeconds == 0 ? 0 : (prior.AvgGpuC * prior.GpuSampleSeconds + session.GpuSum) / gpuSeconds,
                GpuSampleSeconds = gpuSeconds,
                MaxGpuC = Math.Max(prior.MaxGpuC, session.MaxGpuC),
                PlaySeconds = AddCount(prior.PlaySeconds, session.ElapsedSeconds),
                SessionCount = AddCount(prior.SessionCount, 1),
                LastPlayedUtc = utcNow
            };
        }

        private static int AddCount(int current, int add)
        {
            long sum = (long)current + add;
            if (sum < 0) return 0;
            if (sum > int.MaxValue) return int.MaxValue;
            return (int)sum;
        }
    }
}
