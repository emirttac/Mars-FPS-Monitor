using System.Globalization;
using System.IO;

namespace FPSOverlay.Tests;

public class ReleaseSafetyFollowUpTests
{
    [Fact]
    public void Startup_RestoresEvenWhenAppliedIdIsAlreadyStock()
    {
        string path = TempProfile();
        var fake = new FakeGpuProvider();
        var config = new OverlayConfig { OcControlMode = OcControlMode.Off };
        using var oc = Create(config, fake, path);

        oc.Refresh();
        oc.Refresh();

        Assert.Equal(1, fake.RestoreCalls);
        Assert.Equal(0, fake.ApplyCalls);
    }

    [Fact]
    public void Startup_FailedRestore_BlocksApplyUntilItSucceeds()
    {
        string path = TempProfile();
        var fake = new FakeGpuProvider { RestoreSucceeds = false };
        var config = new OverlayConfig { OcControlMode = OcControlMode.ManualFixed };
        using var oc = Create(config, fake, path);
        config.ManualProfileId = oc.ProfileStore.Profiles.First(p => p.CoreOffsetMhz > 0).Id;
        oc.SetThermalSampleForTests(new GpuThermalSample { CoreTempC = 70, HotspotTempC = 60 });

        oc.Refresh();
        Assert.Equal(0, fake.ApplyCalls);
        Assert.Equal(1, fake.RestoreCalls);

        fake.RestoreSucceeds = true;
        oc.Refresh();
        Assert.Equal(1, fake.ApplyCalls);
        Assert.Equal(2, fake.RestoreCalls);

        oc.Refresh();
        Assert.Equal(1, fake.ApplyCalls);
        Assert.Equal(2, fake.RestoreCalls);
    }

    [Fact]
    public void Selected4070_DoesNotBind3060()
    {
        string[] names = { "NVIDIA GeForce RTX 3060", "NVIDIA GeForce RTX 4070" };
        Assert.Equal(1, GpuNameMatch.IndexOfBest("NVIDIA GeForce RTX 4070", names));
        Assert.Null(GpuNameMatch.IndexOfBest("NVIDIA GeForce RTX 4070", new[] { "NVIDIA GeForce RTX 3060" }));
        Assert.False(GpuNameMatch.ShouldUseProvider(GpuNameMatch.VendorOf("NVIDIA GeForce RTX 4070"), GpuNameMatch.Vendor.Amd));
    }

    [Fact]
    public void Tick_OffMode_RetriesFailedRestoreWithoutApply()
    {
        string path = TempProfile();
        var fake = new FakeGpuProvider { RestoreSucceeds = false };
        var config = new OverlayConfig { OcControlMode = OcControlMode.Off };
        using var oc = Create(config, fake, path);

        oc.Tick();
        Assert.Equal(1, fake.RestoreCalls);
        Assert.Equal(0, fake.ApplyCalls);

        fake.RestoreSucceeds = true;
        oc.Tick();
        Assert.Equal(2, fake.RestoreCalls);
        Assert.Equal(0, fake.ApplyCalls);

        oc.Tick();
        Assert.Equal(2, fake.RestoreCalls);
        Assert.Equal(0, fake.ApplyCalls);
    }

    [Fact]
    public void Tick_Manual_RetriesFailedApply()
    {
        string path = TempProfile();
        var fake = new FakeGpuProvider { ApplySucceeds = false };
        var config = new OverlayConfig { OcControlMode = OcControlMode.ManualFixed };
        using var oc = Create(config, fake, path);
        config.ManualProfileId = oc.ProfileStore.Profiles.First(p => p.CoreOffsetMhz > 0).Id;
        oc.SetThermalSampleForTests(new GpuThermalSample { CoreTempC = 70, HotspotTempC = 60 });

        oc.Tick();
        Assert.Equal(1, fake.RestoreCalls);
        Assert.Equal(1, fake.ApplyCalls);

        oc.Tick();
        Assert.Equal(2, fake.ApplyCalls);

        fake.ApplySucceeds = true;
        oc.Tick();
        Assert.Equal(3, fake.ApplyCalls);

        oc.Tick();
        Assert.Equal(3, fake.ApplyCalls);
        Assert.Equal(1, fake.RestoreCalls);
    }

    [Fact]
    public void ModelNames_DoNotAliasShorterCards()
    {
        Assert.Null(GpuNameMatch.IndexOfBest("NVIDIA GeForce RTX 4070 Ti", new[] { "NVIDIA GeForce RTX 4070" }));
        Assert.Null(GpuNameMatch.IndexOfBest("NVIDIA GeForce RTX 4070 Super", new[] { "NVIDIA GeForce RTX 4070" }));
        Assert.Null(GpuNameMatch.IndexOfBest("AMD Radeon RX 7800 XT", new[] { "AMD Radeon RX 7800" }));
        Assert.Equal(0, GpuNameMatch.IndexOfBest("RTX 4070", new[] { "NVIDIA GeForce RTX 4070 Laptop GPU" }));
        Assert.Null(GpuNameMatch.IndexOfBest("RTX 4070", new[]
        {
            "NVIDIA GeForce RTX 4070",
            "NVIDIA GeForce RTX 4070 Laptop GPU"
        }));
        Assert.True(GpuNameMatch.Matches("NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070 Fan"));
        Assert.True(GpuNameMatch.Matches("NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070 Fan 1"));
    }

    [Fact]
    public void FanRestore_RequiresEveryAvailableBackend()
    {
        var mixed = FanControlManager.CombineRestores(new[]
        {
            (true, FanApplyResult.Ok("chassis")),
            (true, FanApplyResult.Fail("gpu"))
        }, userOff: true);
        Assert.False(mixed.Success);

        var skipped = FanControlManager.CombineRestores(new[]
        {
            (true, FanApplyResult.Ok("gpu")),
            (false, FanApplyResult.Fail("no amd"))
        }, userOff: true);
        Assert.True(skipped.Success);

        var none = FanControlManager.CombineRestores(new[]
        {
            (false, FanApplyResult.Fail("absent"))
        }, userOff: true);
        Assert.True(none.Success);
    }

    [Fact]
    public void Manual_HotspotOrInvalidSensor_HoldsStockWithoutLeavingManual()
    {
        Assert.True(OverclockManager.ManualMustHoldStock(
            new GpuThermalSample { CoreTempC = 70, HotspotTempC = 96 }, out _));
        Assert.True(OverclockManager.ManualMustHoldStock(default, out _));
        Assert.False(OverclockManager.ManualMustHoldStock(
            new GpuThermalSample { CoreTempC = 70, HotspotTempC = 70 }, out _));

        string path = TempProfile();
        var fake = new FakeGpuProvider();
        var config = new OverlayConfig { OcControlMode = OcControlMode.ManualFixed };
        using var oc = Create(config, fake, path);
        config.ManualProfileId = oc.ProfileStore.Profiles.First(p => p.CoreOffsetMhz > 0).Id;

        oc.Refresh();
        Assert.Equal(0, fake.ApplyCalls);
        Assert.Equal(OcControlMode.ManualFixed, config.OcControlMode);

        oc.SetThermalSampleForTests(new GpuThermalSample { CoreTempC = 70, HotspotTempC = 60 });
        oc.Refresh();
        Assert.Equal(1, fake.ApplyCalls);
        Assert.Equal(OcControlMode.ManualFixed, config.OcControlMode);

        oc.SetThermalSampleForTests(new GpuThermalSample { CoreTempC = 80, HotspotTempC = 96 });
        oc.Refresh();
        Assert.Equal(1, fake.ApplyCalls);
        Assert.Equal(2, fake.RestoreCalls);
        Assert.Equal(OcControlMode.ManualFixed, config.OcControlMode);
    }

    [Fact]
    public void LiveRpm_MakesUnflaggedChannelReadable()
    {
        Assert.False(FanControlManager.ChannelCanReadRpm(false, null));
        Assert.False(FanControlManager.ChannelCanReadRpm(false, 0));
        Assert.True(FanControlManager.ChannelCanReadRpm(false, 1500));
        Assert.True(FanControlManager.ChannelCanReadRpm(true, 0));
    }

    [Fact]
    public void GpuRebind_SelectsGpuBackendOnly()
    {
        var gpu = new FakeFanBackend();
        var chassis = new FakeFanBackend();
        var host = new FanBackendHost(
            new IFanBackend[] { gpu, chassis },
            new Dictionary<string, IFanBackend>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpu"] = gpu,
                ["case"] = chassis
            },
            new FanChannel[]
            {
                new() { Id = "gpu", Kind = FanKind.Gpu },
                new() { Id = "case", Kind = FanKind.Chassis }
            });

        IFanBackend[] selected = host.BackendsOwning(FanKind.Gpu).ToArray();
        Assert.Single(selected);
        Assert.Same(gpu, selected[0]);
    }

    [Fact]
    public void UnverifiedHotChannel_FailsClosedAt90C()
    {
        var channel = new FanWatchdogChannelSample
        {
            Kind = FanKind.Gpu,
            CanWritePwm = true,
            CanReadRpm = false
        };

        var warm = FanWatchdogEvaluator.Evaluate(40, 85, new[] { channel }, 4, 4);
        Assert.False(warm.ShouldFailClosed);
        Assert.Equal(0, warm.NewUnverifiedStreak);

        int stall = 0;
        int unverified = 0;
        FanWatchdogResult hot = default;
        for (int i = 0; i < FanWatchdogEvaluator.SecondsRequired; i++)
        {
            hot = FanWatchdogEvaluator.Evaluate(40, 91, new[] { channel }, stall, unverified);
            stall = hot.NewStreak;
            unverified = hot.NewUnverifiedStreak;
        }

        Assert.True(hot.ShouldFailClosed);
        Assert.Equal(0, hot.NewStreak);
        Assert.Equal(FanWatchdogEvaluator.SecondsRequired, hot.NewUnverifiedStreak);
    }

    [Fact]
    public void GpuPwm_NeverDropsBelow30()
    {
        var gpu = new FanChannel
        {
            Kind = FanKind.Gpu,
            MinSafePwm = FanChannel.DefaultMinSafePwm(FanKind.Gpu)
        };
        var cpu = new FanChannel
        {
            Kind = FanKind.Cpu,
            MinSafePwm = FanChannel.DefaultMinSafePwm(FanKind.Cpu)
        };

        Assert.Equal(30, gpu.ClampPwm(0, 0));
        Assert.Equal(30, gpu.ClampPwm(10, 15));
        Assert.Equal(25, cpu.ClampPwm(0, 15));
        Assert.Equal(40, cpu.ClampPwm(10, 40));
    }

    [Theory]
    [InlineData("tr-TR", "TR")]
    [InlineData("pt-BR", "BR")]
    [InlineData("zh-CN", "ZH")]
    [InlineData("en-US", "EN")]
    [InlineData("fr-FR", "FR")]
    public void UiLanguage_MapsWindowsCulture(string culture, string expected)
    {
        Assert.Equal(expected, UiLanguage.FromCulture(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void CrashReport_StripsProfileAndSecrets()
    {
        string raw = "at C:\\Users\\emir\\Desktop\\app.cs dpapi:AQIDBA== Authorization: Bearer abc.def-123";
        string clean = CrashReportSanitizer.Sanitize(raw);
        Assert.DoesNotContain(@"C:\Users\emir", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQIDBA", clean);
        Assert.DoesNotContain("abc.def", clean);
        Assert.Contains("%USERPROFILE%", clean);
        Assert.Contains("dpapi:[redacted]", clean);
        Assert.Contains("Bearer [redacted]", clean);
    }

    [Fact]
    public void SecretSave_FailedProtect_DoesNotKeepPlaintext()
    {
        Assert.Equal("dpapi:abc", SecretProtector.KeepPreviousOnFailure("dpapi:abc"));
        Assert.Equal("", SecretProtector.KeepPreviousOnFailure("plain-secret"));
        Assert.Equal("", SecretProtector.KeepPreviousOnFailure(null));
    }

    [Fact]
    public void Migrate_CopiesThenDeletes_AndKeepsSourceWhenDestinationExists()
    {
        string root = Path.Combine(Path.GetTempPath(), "mars-migrate-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "legacy");
        string dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(legacy);
        string source = Path.Combine(legacy, "config.json");
        File.WriteAllText(source, "{\"Language\":\"TR\"}");

        try
        {
            Assert.True(AppPaths.TryMigrateFile(source, Path.Combine(dest, "config.json")));
            Assert.False(File.Exists(source));
            Assert.Equal("{\"Language\":\"TR\"}", File.ReadAllText(Path.Combine(dest, "config.json")));

            string blocked = Path.Combine(legacy, "oc_profiles.json");
            string existing = Path.Combine(dest, "oc_profiles.json");
            File.WriteAllText(blocked, "new");
            File.WriteAllText(existing, "old");
            Assert.False(AppPaths.TryMigrateFile(blocked, existing));
            Assert.Equal("new", File.ReadAllText(blocked));
            Assert.Equal("old", File.ReadAllText(existing));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static OverclockManager Create(OverlayConfig config, FakeGpuProvider fake, string path)
        => new(config, new HardwareMonitorManager(new LibreHardwareMonitor.Hardware.Computer()), null, fake, startTimer: false, profileStorePath: path);

    private static string TempProfile()
        => Path.Combine(Path.GetTempPath(), "mars-oc-" + Guid.NewGuid().ToString("N") + ".json");

    private sealed class FakeFanBackend : IFanBackend
    {
        public string Name => "Fake fan";
        public bool IsAvailable => true;
        public string StatusMessage => "fake";
        public IReadOnlyList<FanChannel> Probe() => Array.Empty<FanChannel>();
        public FanLiveReading? Read(string channelId) => null;
        public FanApplyResult SetPwm(string channelId, int percent) => FanApplyResult.Ok("pwm", percent);
        public FanApplyResult RestoreDefaults(string? channelId = null) => FanApplyResult.Ok("restored");
    }

    private sealed class FakeGpuProvider : IGpuOverclockProvider
    {
        public bool RestoreSucceeds { get; set; } = true;
        public bool ApplySucceeds { get; set; } = true;
        public int RestoreCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public string Name => "Fake";
        public string Vendor => "NVIDIA";
        public bool IsAvailable { get; set; } = true;
        public string StatusMessage => "fake";

        public OverclockApplyResult Apply(OverclockTarget target)
        {
            ApplyCalls++;
            if (!ApplySucceeds)
                return new OverclockApplyResult { Success = false, Message = "apply-failed" };
            return new OverclockApplyResult { Success = true, Message = "applied", Applied = target };
        }

        public OverclockApplyResult RestoreDefaults()
        {
            RestoreCalls++;
            return new OverclockApplyResult
            {
                Success = RestoreSucceeds,
                Message = RestoreSucceeds ? "restored" : "restore-failed",
                Applied = OcProfileStore.SafeStock.ToTarget()
            };
        }
    }
}
