namespace FPSOverlay.Tests;

public class ReleaseNotesCopyTests
{
    public static IEnumerable<object[]> Languages()
    {
        yield return new object[] { "EN" };
        yield return new object[] { "TR" };
        yield return new object[] { "DE" };
        yield return new object[] { "ES" };
        yield return new object[] { "FR" };
        yield return new object[] { "PT" };
        yield return new object[] { "BR" };
        yield return new object[] { "RU" };
        yield return new object[] { "AZ" };
        yield return new object[] { "ZH" };
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguage_HasV3Notes(string lang)
    {
        var notes = ReleaseNotesCopy.For(lang);

        Assert.Contains("v3.0", notes.Title);
        Assert.Contains("**", notes.FeaturesBody);
        Assert.Contains("[[issues]]", notes.BugsBody);
        Assert.False(string.IsNullOrWhiteSpace(notes.FeaturesHeader));
        Assert.False(string.IsNullOrWhiteSpace(notes.BugsHeader));
        Assert.False(string.IsNullOrWhiteSpace(notes.AckCheckbox));
        Assert.False(string.IsNullOrWhiteSpace(notes.ContinueButton));
        Assert.False(string.IsNullOrWhiteSpace(notes.LanguageLabel));
        Assert.True(notes.FeaturesBody.Length > 200);
        Assert.True(notes.BugsBody.Length > 200);
    }

    [Fact]
    public void Turkish_UsesTheReleaseCheckboxWording()
    {
        var notes = ReleaseNotesCopy.For("TR");
        Assert.Equal("Mars FPS Monitor v3.0 Güncelleme Notları", notes.Title);
        Assert.Equal("Okudum, anladım", notes.AckCheckbox);
        Assert.Equal("Devam et", notes.ContinueButton);
        Assert.Contains("Kütüphanem", notes.FeaturesBody);
        Assert.Contains("Fanlar", notes.FeaturesBody);
    }
}
