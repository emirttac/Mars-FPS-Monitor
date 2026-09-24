using System.IO;
using System.Text.Json;

namespace FPSOverlay.Tests;

public class ReleaseSafetyTests
{
    [Fact]
    public void AmdOffset_UsesBaseline_SoLaterProfilesDoNotStack()
    {
        const int baseline = 250_000; // 2500 MHz in ADL 10 kHz units
        int extreme = AmdOverdriveClockMath.ApplyOffset(baseline, 50);
        int performance = AmdOverdriveClockMath.ApplyOffset(baseline, 25);
        int stock = AmdOverdriveClockMath.ApplyOffset(baseline, 0);

        Assert.Equal(255_000, extreme);
        Assert.Equal(252_500, performance);
        Assert.Equal(baseline, stock);
        Assert.NotEqual(extreme + 25 * AmdOverdriveClockMath.TenKhzPerMhz, performance);
    }

    [Fact]
    public void AmdOffset_NeverGoesNegative()
    {
        Assert.Equal(0, AmdOverdriveClockMath.ApplyOffset(100, -5));
    }

    [Fact]
    public void PowerPercent_UsesCapturedBaseline()
    {
        Assert.Equal(200, OcHardwareLimits.UnitsFromPercent(200, 100, 100, 250));
        Assert.Equal(220, OcHardwareLimits.UnitsFromPercent(200, 110, 100, 250));
        Assert.Equal(210, OcHardwareLimits.UnitsFromPercent(200, 110, 100, 210));
        Assert.Equal(115.5, OcHardwareLimits.WattsFromPercent(105, 110), 1);
    }

    [Fact]
    public void HardwareLimits_ClampManualProfile()
    {
        var profile = new OcProfile
        {
            ProfileName = "Unsafe",
            MinTemp = 120,
            MaxTemp = -4,
            CoreOffsetMhz = 500,
            MemoryOffsetMhz = 900,
            PowerLimitPercent = 40
        };

        Assert.True(OcHardwareLimits.ClampProfile(profile));
        Assert.Equal(OcHardwareLimits.MaxCoreOffsetMhz, profile.CoreOffsetMhz);
        Assert.Equal(OcHardwareLimits.MaxMemoryOffsetMhz, profile.MemoryOffsetMhz);
        Assert.Equal(OcHardwareLimits.MinPowerLimitPercent, profile.PowerLimitPercent);
        Assert.True(profile.MaxTemp >= profile.MinTemp);
        Assert.InRange(profile.MinTemp, OcHardwareLimits.MinTempC, OcHardwareLimits.MaxTempC);
        Assert.InRange(profile.MaxTemp, OcHardwareLimits.MinTempC, OcHardwareLimits.MaxTempC);
    }

    [Fact]
    public void HardwareLimits_LeaveStockPowerUnset()
    {
        var profile = new OcProfile
        {
            ProfileName = "Eco",
            MinTemp = 80,
            MaxTemp = 100,
            PowerLimitPercent = null
        };

        Assert.False(OcHardwareLimits.ClampProfile(profile));
        Assert.Null(profile.PowerLimitPercent);
    }

    [Fact]
    public void ProfileStore_ClampsValuesLoadedFromDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), "mars-oc-clamp-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var dto = new
            {
                Version = 1,
                Profiles = new[]
                {
                    new
                    {
                        Id = Guid.NewGuid(),
                        profile_name = "Imported",
                        min_temp = 0,
                        max_temp = 70,
                        core_offset_mhz = 500,
                        memory_offset_mhz = 900,
                        power_limit_percent = 200
                    }
                }
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = null }));

            var store = new OcProfileStore(path);
            var loaded = Assert.Single(store.Profiles);
            Assert.Equal(100, loaded.CoreOffsetMhz);
            Assert.Equal(300, loaded.MemoryOffsetMhz);
            Assert.Equal(110, loaded.PowerLimitPercent);

            string saved = File.ReadAllText(path);
            Assert.Contains("\"core_offset_mhz\": 100", saved);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void FrameSelector_CountsOnePresentPerFrame()
    {
        var selector = new FrameSourceSelector();

        var dxgi = selector.Observe(FrameProviderKind.Dxgi, FrameSourceSelector.DxgiPresentId);
        var sameFrame = selector.Observe(FrameProviderKind.DxgKrnl, FrameSourceSelector.DxgKrnlPresentId);
        var flip = selector.Observe(FrameProviderKind.DxgKrnl, 168);

        Assert.True(dxgi.CountFrame);
        Assert.False(sameFrame.CountFrame);
        Assert.False(flip.CountFrame);
    }

    [Fact]
    public void FrameSelector_DxgKrnlPresentCountsWhenItIsTheOnlySource()
    {
        var selector = new FrameSourceSelector();
        var present = selector.Observe(FrameProviderKind.DxgKrnl, FrameSourceSelector.DxgKrnlPresentId);
        Assert.True(present.CountFrame);
    }

    [Fact]
    public void FrameSelector_HigherPrioritySourceResetsLowerSamples()
    {
        var selector = new FrameSourceSelector();
        Assert.True(selector.Observe(FrameProviderKind.DxgKrnl, FrameSourceSelector.DxgKrnlPresentId).CountFrame);

        var upgraded = selector.Observe(FrameProviderKind.Dxgi, FrameSourceSelector.DxgiPresentId);
        Assert.True(upgraded.CountFrame);
        Assert.True(upgraded.ResetSamples);

        Assert.False(selector.Observe(FrameProviderKind.DxgKrnl, FrameSourceSelector.DxgKrnlPresentId).CountFrame);
    }

    [Fact]
    public void FrameSelector_ReleasesLockAfterASilentWindow()
    {
        var selector = new FrameSourceSelector();
        selector.Observe(FrameProviderKind.Dxgi, FrameSourceSelector.DxgiPresentId);
        selector.CompleteWindow();
        selector.CompleteWindow();

        var fallback = selector.Observe(FrameProviderKind.DxgKrnl, FrameSourceSelector.DxgKrnlPresentId);
        Assert.True(fallback.CountFrame);
    }

    [Fact]
    public void RetiredPresetUrl_IsRecognized()
    {
        Assert.True(GpuRemotePresetFetcher.IsRetiredDefaultUrl(GpuRemotePresetFetcher.RetiredUrl));
        Assert.True(GpuRemotePresetFetcher.IsRetiredDefaultUrl("  " + GpuRemotePresetFetcher.RetiredUrl.ToUpperInvariant() + " "));
        Assert.False(GpuRemotePresetFetcher.IsRetiredDefaultUrl(GpuRemotePresetFetcher.DefaultUrl));
        Assert.False(GpuRemotePresetFetcher.IsRetiredDefaultUrl("https://example.com/presets.json"));
        Assert.False(GpuRemotePresetFetcher.IsRetiredDefaultUrl(""));
        Assert.True(GpuRemotePresetFetcher.ShouldUseOfficialCatalog(null));
        Assert.True(GpuRemotePresetFetcher.ShouldUseOfficialCatalog(GpuRemotePresetFetcher.RetiredUrl));
        Assert.False(GpuRemotePresetFetcher.ShouldUseOfficialCatalog(GpuRemotePresetFetcher.DefaultUrl));
    }

    [Fact]
    public void CatalogMatch_PrefersSpecificKeyAndKeepsDesktopOffLaptop()
    {
        string[] keys =
        {
            "RTX 3060",
            "RTX 3060 Ti",
            "RTX 3060 Laptop GPU",
            "RTX 2050 Laptop GPU",
            "RTX 4070",
            "RTX 4070 Super",
            "RX 9060XT"
        };

        Assert.Equal("RTX 3060", GpuRemotePresetFetcher.FindBestMatch("NVIDIA GeForce RTX 3060", keys));
        Assert.Equal("RTX 3060 Ti", GpuRemotePresetFetcher.FindBestMatch("NVIDIA GeForce RTX 3060 Ti", keys));
        Assert.Equal("RTX 3060 Laptop GPU", GpuRemotePresetFetcher.FindBestMatch("NVIDIA GeForce RTX 3060 Laptop GPU", keys));
        Assert.Equal("RTX 2050 Laptop GPU", GpuRemotePresetFetcher.FindBestMatch("NVIDIA GeForce RTX 2050", keys));
        Assert.Equal("RTX 4070 Super", GpuRemotePresetFetcher.FindBestMatch("NVIDIA GeForce RTX 4070 Super", keys));
        Assert.Equal("RX 9060XT", GpuRemotePresetFetcher.FindBestMatch("AMD Radeon RX 9060 XT", keys));
    }

    [Fact]
    public async Task EmptyPresetUrl_DoesNotFetch()
    {
        var result = await new GpuRemotePresetFetcher().TryResolveAsync("NVIDIA GeForce RTX 4080", "  ");
        Assert.False(result.Success);
        Assert.Equal("no_presets_url", result.Reason);
    }

    [Fact]
    public void CatalogScale_KeepsCardOrderInsideSafetyCaps()
    {
        var laptop = Tier(40, 200, 100, 60, 300, 100);
        var mid = Tier(75, 600, 110, 110, 900, 115);
        var flagship = Tier(125, 1000, 112, 200, 1500, 120);
        GpuPresetTiers[] population = { laptop, mid, flagship };

        var safeLaptop = GpuPresetSafetyScaler.Scale(laptop, population);
        var safeMid = GpuPresetSafetyScaler.Scale(mid, population);
        var safeFlagship = GpuPresetSafetyScaler.Scale(flagship, population);

        Assert.Equal(0, safeFlagship.Eco!.Core);
        Assert.Equal(0, safeFlagship.Eco.Mem);
        Assert.Equal(90, safeFlagship.Eco.Power);

        Assert.Equal(100, safeLaptop.Performance!.Power);
        Assert.Equal(100, safeLaptop.Extreme!.Power);
        Assert.InRange(safeFlagship.Performance!.Power, 100, 105);
        Assert.InRange(safeFlagship.Extreme!.Power, 100, 110);
        Assert.True(safeFlagship.Extreme.Power > safeLaptop.Extreme.Power);

        Assert.True(safeFlagship.Extreme.Core > safeMid.Extreme!.Core);
        Assert.True(safeMid.Extreme.Core > safeLaptop.Extreme.Core);
        Assert.InRange(safeFlagship.Extreme.Core, 1, AiOcSafetyClamp.MaxCoreOffsetMhz);

        Assert.True(safeFlagship.Extreme.Mem > safeMid.Extreme.Mem);
        Assert.True(safeMid.Extreme.Mem > safeLaptop.Extreme.Mem);
        Assert.InRange(safeFlagship.Extreme.Mem, 1, AiOcSafetyClamp.MaxMemoryOffsetMhz);

        Assert.InRange(safeFlagship.Performance.Core, 1, 50);
        Assert.InRange(safeFlagship.Performance.Mem, 1, 100);
        Assert.True(safeFlagship.Performance.Core > safeLaptop.Performance.Core);

        Assert.Equal(200, flagship.Extreme!.Core);
        Assert.Equal(1500, flagship.Extreme.Mem);

        var response = GpuRemotePresetFetcher.ToAiOcResponse("RTX 4090", flagship, population);
        var clamped = AiOcSafetyClamp.Apply(response);
        var extreme = Assert.Single(clamped.Response.Recommendations, r => r.Mode == "Extreme");
        Assert.Equal(safeFlagship.Extreme.Core, extreme.CoreOffsetMhz);
        Assert.Equal(safeFlagship.Extreme.Mem, extreme.MemoryOffsetMhz);
        Assert.Equal(safeFlagship.Extreme.Power, extreme.PowerLimitPercent);
    }

    private static GpuPresetTiers Tier(int perfCore, int perfMem, int perfPower, int extCore, int extMem, int extPower)
        => new()
        {
            Eco = new GpuPresetOffsets { Core = 0, Mem = 0, Power = 90 },
            Performance = new GpuPresetOffsets { Core = perfCore, Mem = perfMem, Power = perfPower },
            Extreme = new GpuPresetOffsets { Core = extCore, Mem = extMem, Power = extPower }
        };

    [Fact]
    public void CrashRestore_ReleasesGpuClocksBeforeFans()
    {
        int step = 0;
        int ocStep = 0;
        int fanStep = 0;
        OcSafetyHook.Restore = () => ocStep = ++step;
        FanSafetyHook.Restore = () => fanStep = ++step;
        try
        {
            HardwareSafety.RestoreAfterFault();
            Assert.Equal(1, ocStep);
            Assert.Equal(2, fanStep);
        }
        finally
        {
            OcSafetyHook.Restore = null;
            FanSafetyHook.Restore = null;
        }
    }
}
