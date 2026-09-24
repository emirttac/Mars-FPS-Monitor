namespace FPSOverlay.Tests;

public class AiOcSafetyClampTests
{
    [Fact]
    public void EmptyRecommendations_AddsWarning()
    {
        var result = AiOcSafetyClamp.Apply(new AiOcResponse { Recommendations = new() });
        Assert.Contains("empty_recommendations", result.Response.Warnings);
        Assert.Empty(result.Response.Recommendations);
    }

    [Fact]
    public void AbsoluteCeilings_ClampExtremeOffsets()
    {
        var raw = new AiOcResponse
        {
            Recommendations =
            {
                new AiOcRecommendation
                {
                    Mode = "Extreme",
                    CoreOffsetMhz = 500,
                    MemoryOffsetMhz = 999,
                    PowerLimitPercent = 200,
                    MinTemp = 0,
                    MaxTemp = 70
                }
            }
        };

        var result = AiOcSafetyClamp.Apply(raw);
        var rec = Assert.Single(result.Response.Recommendations);
        Assert.Equal(AiOcSafetyClamp.MaxCoreOffsetMhz, rec.CoreOffsetMhz);
        Assert.Equal(AiOcSafetyClamp.MaxMemoryOffsetMhz, rec.MemoryOffsetMhz);
        Assert.Equal(AiOcSafetyClamp.MaxPowerLimitPercent, rec.PowerLimitPercent);
        Assert.NotEmpty(result.ClampLog);
    }

    [Fact]
    public void EcoSoftCaps_ForceStockClocks()
    {
        var raw = new AiOcResponse
        {
            Recommendations =
            {
                new AiOcRecommendation
                {
                    Mode = "Eco",
                    CoreOffsetMhz = 80,
                    MemoryOffsetMhz = 200,
                    PowerLimitPercent = 110,
                    MinTemp = 80,
                    MaxTemp = 100
                }
            }
        };

        var rec = Assert.Single(AiOcSafetyClamp.Apply(raw).Response.Recommendations);
        Assert.Equal(0, rec.CoreOffsetMhz);
        Assert.Equal(0, rec.MemoryOffsetMhz);
        Assert.Equal(100, rec.PowerLimitPercent);
    }

    [Fact]
    public void HardwarePlCap_TightensPowerLimit()
    {
        var raw = new AiOcResponse
        {
            Recommendations =
            {
                new AiOcRecommendation
                {
                    Mode = "Performance",
                    CoreOffsetMhz = 40,
                    MemoryOffsetMhz = 80,
                    PowerLimitPercent = 110,
                    MinTemp = 0,
                    MaxTemp = 80
                }
            }
        };
        var hw = new AiOcHardwareSnapshot { MaxPowerLimitPercent = 100 };
        var rec = Assert.Single(AiOcSafetyClamp.Apply(raw, hw).Response.Recommendations);
        Assert.Equal(100, rec.PowerLimitPercent);
    }

    [Fact]
    public void NormalizeMode_MapsAliases()
    {
        Assert.Equal("Eco", AiOcSafetyClamp.NormalizeMode("Economy"));
        Assert.Equal("Performance", AiOcSafetyClamp.NormalizeMode("Balanced"));
        Assert.Equal("Extreme", AiOcSafetyClamp.NormalizeMode("Max"));
        Assert.Equal("Eco", AiOcSafetyClamp.NormalizeMode("nonsense"));
    }

    [Fact]
    public void DeduplicateModes_KeepsLastPerMode_Ordered()
    {
        var raw = new AiOcResponse
        {
            Recommendations =
            {
                new AiOcRecommendation { Mode = "Eco", CoreOffsetMhz = 0, MemoryOffsetMhz = 0, MinTemp = 80, MaxTemp = 100, ProfileName = "first" },
                new AiOcRecommendation { Mode = "Extreme", CoreOffsetMhz = 10, MemoryOffsetMhz = 10, MinTemp = 0, MaxTemp = 70, ProfileName = "x" },
                new AiOcRecommendation { Mode = "Eco", CoreOffsetMhz = 0, MemoryOffsetMhz = 0, MinTemp = 82, MaxTemp = 100, ProfileName = "second" },
                new AiOcRecommendation { Mode = "Performance", CoreOffsetMhz = 20, MemoryOffsetMhz = 20, MinTemp = 70, MaxTemp = 81, ProfileName = "p" }
            }
        };

        var list = AiOcSafetyClamp.Apply(raw).Response.Recommendations;
        Assert.Equal(3, list.Count);
        Assert.Equal("Eco", list[0].Mode);
        Assert.Equal("second", list[0].ProfileName);
        Assert.Equal("Performance", list[1].Mode);
        Assert.Equal("Extreme", list[2].Mode);
    }

    [Fact]
    public void SwapsInvertedTempBand()
    {
        var raw = new AiOcResponse
        {
            Recommendations =
            {
                new AiOcRecommendation
                {
                    Mode = "Performance",
                    MinTemp = 90,
                    MaxTemp = 70,
                    CoreOffsetMhz = 10,
                    MemoryOffsetMhz = 10
                }
            }
        };
        var rec = Assert.Single(AiOcSafetyClamp.Apply(raw).Response.Recommendations);
        Assert.True(rec.MinTemp <= rec.MaxTemp);
        Assert.Equal(70, rec.MinTemp);
        Assert.Equal(90, rec.MaxTemp);
    }
}
