namespace FPSOverlay.Tests;

public class FanCurveEngineTests
{
    private static FanCurve SampleCurve() => new()
    {
        Name = "Test",
        Points =
        {
            new FanCurvePoint { TempC = 40, PwmPercent = 20 },
            new FanCurvePoint { TempC = 60, PwmPercent = 40 },
            new FanCurvePoint { TempC = 80, PwmPercent = 80 }
        }
    };

    [Fact]
    public void Interpolate_BelowFirst_ReturnsFirstPwm()
    {
        var eng = new FanCurveEngine();
        Assert.Equal(20, eng.Interpolate(SampleCurve(), 10));
    }

    [Fact]
    public void Interpolate_AboveLast_ReturnsLastPwm()
    {
        var eng = new FanCurveEngine();
        Assert.Equal(80, eng.Interpolate(SampleCurve(), 100));
    }

    [Fact]
    public void Interpolate_Midpoint_Linear()
    {
        var eng = new FanCurveEngine();
        Assert.Equal(40, eng.Interpolate(SampleCurve(), 60));
        Assert.Equal(30, eng.Interpolate(SampleCurve(), 50));
    }

    [Fact]
    public void Interpolate_EmptyPoints_Defaults40()
    {
        var eng = new FanCurveEngine();
        Assert.Equal(40, eng.Interpolate(new FanCurve { Points = new() }, 55));
    }

    [Fact]
    public void Hysteresis_HoldsOnCooldownInsideBand()
    {
        var eng = new FanCurveEngine();
        int held = eng.ApplyHysteresis(desired: 30, lastPwm: 50, tempC: 55, lastTempC: 56, hysteresisC: 3);
        Assert.Equal(50, held);
    }

    [Fact]
    public void Hysteresis_IgnoresOnePercentFlicker()
    {
        var eng = new FanCurveEngine();
        int held = eng.ApplyHysteresis(desired: 41, lastPwm: 40, tempC: 60, lastTempC: 59, hysteresisC: 2);
        Assert.Equal(40, held);
    }

    [Fact]
    public void Hysteresis_AllowsIncrease()
    {
        var eng = new FanCurveEngine();
        int next = eng.ApplyHysteresis(desired: 70, lastPwm: 40, tempC: 75, lastTempC: 60, hysteresisC: 2);
        Assert.Equal(70, next);
    }
}
