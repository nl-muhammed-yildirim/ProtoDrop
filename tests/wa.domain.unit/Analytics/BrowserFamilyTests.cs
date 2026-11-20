using wa.domain.Analytics;

namespace wa.domain.unit.Analytics;

/// <summary>US-023-01 (EC-023-3): UA → closed browser-family set; never throws.</summary>
public class BrowserFamilyTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "Chrome")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0", "Edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0", "Opera")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0", "Firefox")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/121.0 Mobile/15E148 Safari/605.1.15", "Firefox")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0.0.0 Mobile/15E148 Safari/604.1", "Chrome")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15", "Safari")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1", "Safari")]
    public void FromUserAgent_KnownBrowsers_MapToFamily(string ua, string expected) =>
        Assert.Equal(expected, BrowserFamily.FromUserAgent(ua));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromUserAgent_NullOrBlank_ReturnsNull(string? ua) =>
        Assert.Null(BrowserFamily.FromUserAgent(ua));

    [Theory]
    [InlineData("curl/8.0")]
    [InlineData("\u0000\u0001 garbage ???")]
    public void FromUserAgent_Unknown_ReturnsOther_AlwaysInClosedSet(string ua)
    {
        var family = BrowserFamily.FromUserAgent(ua);
        Assert.Equal("Other", family);
        Assert.Contains(family, BrowserFamily.All);
    }
}
