using System.IO;

namespace FPSOverlay.Tests;

public class OcProfileEngineTests
{
    private static string TempStorePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "mars-oc-test-" + Guid.NewGuid().ToString("N") + ".json");
        return path;
    }

    private static OcProfileEngine CreateEngine(out string path)
    {
        path = TempStorePath();
        var store = new OcProfileStore(path);
        return new OcProfileEngine(store);
    }

    [Fact]
    public void InvalidSample_FailClosedToSafe()
    {
        var engine = CreateEngine(out var path);
        try
        {
            var d = engine.Evaluate(new GpuThermalSample { CoreTempC = null, HotspotTempC = null }, DateTime.UtcNow);
            Assert.True(d.IsFailClosed);
            Assert.Equal(OcProfileStore.SafeStock.Id, d.Profile.Id);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void HotspotCritical_FailClosedToSafe()
    {
        var engine = CreateEngine(out var path);
        try
        {
            var d = engine.Evaluate(new GpuThermalSample { CoreTempC = 60, HotspotTempC = 95 }, DateTime.UtcNow);
            Assert.True(d.IsFailClosed);
            Assert.Equal(OcProfileStore.SafeStock.Id, d.Profile.Id);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void InitialTransition_SelectsBand()
    {
        var engine = CreateEngine(out var path);
        try
        {
            var d = engine.Evaluate(new GpuThermalSample { CoreTempC = 60, HotspotTempC = 70 }, DateTime.UtcNow);
            Assert.False(d.IsFailClosed);
            Assert.True(d.Changed);
            Assert.Contains("initial", d.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Hysteresis_BlocksUpgradeNearBandEdge()
    {
        var engine = CreateEngine(out var path);
        try
        {
            var t0 = DateTime.UtcNow;
            // Warm into Eco band first (82-100 default)
            engine.Evaluate(new GpuThermalSample { CoreTempC = 90, HotspotTempC = 95 }, t0);
            // Hotspot 95 fail-closes — use 94
            engine.Reset();
            engine.Evaluate(new GpuThermalSample { CoreTempC = 90, HotspotTempC = 90 }, t0);

            // After cooldown, try cooler temp that wants higher OC but not deep enough past hysteresis
            var t1 = t0.AddSeconds(10);
            // Core at 76: Performance band is 75-81; Extreme is 0-74. Desired might be Performance.
            // Sit on Performance first
            engine.Reset();
            var init = engine.Evaluate(new GpuThermalSample { CoreTempC = 78, HotspotTempC = 80 }, t0);
            Assert.False(init.IsFailClosed);

            // Want Extreme (cooler) — core 74 is edge of Extreme max; upgrade from Perf needs ≤ MaxTemp-4 = 70
            var hold = engine.Evaluate(new GpuThermalSample { CoreTempC = 73, HotspotTempC = 75 }, t1);
            // At 73°C Extreme matches (0-74). Current is Performance. Safer? Extreme has higher offset — upgrade.
            // upgradeGate = 74 - 4 = 70; core 73 > 70 → hysteresis hold
            Assert.False(hold.Changed);
            Assert.Contains("hysteresis", hold.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Cooldown_BlocksRapidChanges()
    {
        var engine = CreateEngine(out var path);
        try
        {
            var t0 = DateTime.UtcNow;
            engine.Evaluate(new GpuThermalSample { CoreTempC = 60, HotspotTempC = 70 }, t0);
            // Force safer downgrade quickly — go hot into Eco
            var mid = engine.Evaluate(new GpuThermalSample { CoreTempC = 90, HotspotTempC = 92 }, t0.AddSeconds(1));
            // Downgrade should bypass upgrade hysteresis; may still hit cooldown on second change
            var again = engine.Evaluate(new GpuThermalSample { CoreTempC = 60, HotspotTempC = 70 }, t0.AddSeconds(2));
            Assert.False(again.Changed);
            Assert.True(
                again.Reason.Contains("cooldown", StringComparison.OrdinalIgnoreCase) ||
                again.Reason.Contains("hysteresis", StringComparison.OrdinalIgnoreCase) ||
                again.Reason.Contains("hold", StringComparison.OrdinalIgnoreCase));
        }
        finally { TryDelete(path); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
