using wa.domain.Privacy;

namespace wa.domain.unit.Privacy;

// US-047-03 (TA-9.4): the shared admin email mask.
public class EmailMaskTests
{
    [Fact]
    public void Mask_Typical_KeepsFirstCharAndDomain() =>
        Assert.Equal("d****@x.com", EmailMask.Mask("dana@x.com"));

    [Fact]
    public void Mask_SingleCharLocal() =>
        Assert.Equal("a****@x.com", EmailMask.Mask("a@x.com"));

    [Fact]
    public void Mask_NoAt_ReturnsStars() =>
        Assert.Equal("****", EmailMask.Mask("not-an-email"));

    [Fact]
    public void Mask_LeadingAt_ReturnsStars() =>
        Assert.Equal("****", EmailMask.Mask("@x.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_NullOrEmpty_ReturnsNull(string? input) =>
        Assert.Null(EmailMask.Mask(input));

    [Fact]
    public void Mask_NeverContainsOriginalLocalPart()
    {
        var masked = EmailMask.Mask("danaverysecret@x.com")!;
        Assert.DoesNotContain("anaverysecret", masked);
        Assert.EndsWith("@x.com", masked);
    }
}
