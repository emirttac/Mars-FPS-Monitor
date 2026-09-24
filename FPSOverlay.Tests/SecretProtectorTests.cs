using System;

namespace FPSOverlay.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        string original = "test-secret-" + Guid.NewGuid().ToString("N");
        string protectedValue = SecretProtector.Protect(original);
        Assert.True(SecretProtector.IsProtected(protectedValue));
        Assert.False(string.Equals(original, protectedValue, StringComparison.Ordinal));
        Assert.Equal(original, SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_Passthrough()
    {
        Assert.Equal("plain", SecretProtector.Unprotect("plain"));
    }

    [Fact]
    public void Protect_Empty_ReturnsEmpty()
    {
        Assert.Equal("", SecretProtector.Protect(""));
        Assert.Equal("", SecretProtector.Protect(null));
    }
}
