using wa.application.UseCases.Transfers;

namespace wa.application.unit.Transfers;

// T-014-04: pure template locale resolution (ADR-011 per-recipient attribute) — no side effects.
public class TemplateLocaleResolverTests
{
    [Fact]
    public void Resolve_Null_ReturnsEn() => Assert.Equal("en", TemplateLocaleResolver.Resolve(null));

    [Fact]
    public void Resolve_EmptyString_ReturnsEn() => Assert.Equal("en", TemplateLocaleResolver.Resolve(string.Empty));

    [Fact]
    public void Resolve_WhitespaceOnly_ReturnsEn() => Assert.Equal("en", TemplateLocaleResolver.Resolve("   "));

    [Theory]
    // Every shipped locale round-trips (lowercased, trimmed).
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("tr")]
    public void Resolve_ValidLocale_ReturnsSameLocale_Lowercased(string locale) =>
        Assert.Equal(locale, TemplateLocaleResolver.Resolve(locale));

    [Fact]
    public void Resolve_UpperCaseValidLocale_ReturnsLowercase() =>
        Assert.Equal("de", TemplateLocaleResolver.Resolve("DE"));

    [Fact]
    public void Resolve_PaddedValidLocale_TrimsAndReturns() =>
        Assert.Equal("nl", TemplateLocaleResolver.Resolve("  NL "));

    [Theory]
    // Unknown locales fall back to English (EC-014-4: no failure, en template).
    [InlineData("jp")]
    [InlineData("xx")]
    [InlineData("en-US")]
    public void Resolve_InvalidLocale_ReturnsEn(string locale) =>
        Assert.Equal("en", TemplateLocaleResolver.Resolve(locale));

    [Theory]
    // Parity: every shipped locale is supported.
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("tr")]
    public void IsSupported_AllShippedLocales_ReturnsTrue(string locale) =>
        Assert.True(TemplateLocaleResolver.IsSupported(locale));

    [Theory]
    [InlineData("jp")]
    [InlineData("")]
    [InlineData("DE")] // case-sensitive — callers must resolve first
    public void IsSupported_UnsupportedLocale_ReturnsFalse(string locale) =>
        Assert.False(TemplateLocaleResolver.IsSupported(locale));
}
