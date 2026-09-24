namespace FPSOverlay.Tests;

public class AppVersionComparerTests
{
    [Theory]
    [InlineData("v2.0.0", "2.0.0")]
    [InlineData("2.1", "2.1")]
    [InlineData("release-1.2.3-beta", "1.2.3")]
    [InlineData("", "")]
    [InlineData("nope", "")]
    public void Normalize_ExtractsDottedVersion(string raw, string expected)
    {
        Assert.Equal(expected, AppVersionComparer.Normalize(raw));
    }

    [Theory]
    [InlineData("2.0.1", "2.0.0", 1)]
    [InlineData("2.0.0", "2.0.1", -1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("2.1", "2.0.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    public void Compare_OrdersSemVerLike(string a, string b, int expectedSign)
    {
        int cmp = AppVersionComparer.Compare(a, b);
        Assert.Equal(Math.Sign(expectedSign), Math.Sign(cmp));
    }
}
