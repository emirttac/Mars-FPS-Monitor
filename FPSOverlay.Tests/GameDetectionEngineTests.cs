namespace FPSOverlay.Tests;

public class GameDetectionEngineTests
{
    [Theory]
    [InlineData("explorer")]
    [InlineData("chrome.exe")]
    [InlineData("Discord")]
    [InlineData("steam")]
    [InlineData("FPSOverlay")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsExcluded_KnownSystemAndLaunchers(string name)
    {
        Assert.True(GameDetectionEngine.IsExcluded(name));
    }

    [Theory]
    [InlineData("Cyberpunk2077")]
    [InlineData("eldenring.exe")]
    [InlineData("MyCoolGame")]
    public void IsExcluded_AllowsGameLikeNames(string name)
    {
        Assert.False(GameDetectionEngine.IsExcluded(name));
    }
}
