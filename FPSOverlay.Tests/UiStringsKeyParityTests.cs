using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FPSOverlay.Tests;

public class UiStringsKeyParityTests
{
    public static IEnumerable<object[]> Languages()
    {
        yield return new object[] { "EN" };
        yield return new object[] { "TR" };
        yield return new object[] { "AZ" };
        yield return new object[] { "DE" };
        yield return new object[] { "ES" };
        yield return new object[] { "FR" };
        yield return new object[] { "PT" };
        yield return new object[] { "BR" };
        yield return new object[] { "RU" };
        yield return new object[] { "ZH" };
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void AllStringProperties_NonEmpty(string lang)
    {
        var s = UiStrings.For(lang);
        var props = typeof(UiStrings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .ToList();

        Assert.NotEmpty(props);
        foreach (var p in props)
        {
            string? value = p.GetValue(s) as string;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}.{p.Name} is empty");
        }
    }

    [Fact]
    public void TranslatedLanguages_ShareSameExplicitKeySet()
    {
        var en = UiStrings.En();
        var props = typeof(UiStrings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.Name != "LanguageCode")
            .ToList();

        HashSet<string>? baseline = null;
        foreach (var code in new[] { "TR", "AZ", "DE", "ES", "FR", "PT", "BR", "RU", "ZH" })
        {
            var s = UiStrings.For(code);
            var translated = props
                .Where(p => !string.Equals(p.GetValue(s) as string, p.GetValue(en) as string, System.StringComparison.Ordinal))
                .Select(p => p.Name)
                .ToHashSet();

            baseline ??= translated;
            // Allow subsets that only differ by a few keys still being English defaults,
            // but require a strong overlap so new keys are not forgotten in one locale.
            int overlap = translated.Intersect(baseline).Count();
            double ratio = baseline.Count == 0 ? 1 : (double)overlap / baseline.Count;
            Assert.True(ratio >= 0.85,
                $"{code} translated-key overlap with TR baseline is {ratio:P0} (expected ≥ 85%)");
        }
    }
}
