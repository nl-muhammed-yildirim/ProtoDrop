using System.Text;
using System.Text.RegularExpressions;
using wa.infrastructure.Events;
using Xunit;

namespace wa.application.unit.Events;

/// <summary>CHORE-local-email: the shared HMAC contract of the local email hand-off.</summary>
public class EmailHandOffTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"eventId\":\"x\"}");
    private const string Key = "local-email-handoff-key-dev-only";

    [Fact]
    public void Sign_IsDeterministicLowercase64Hex()
    {
        var first = EmailHandOff.Sign(Body, Key);
        var second = EmailHandOff.Sign(Body, Key);

        Assert.Equal(first, second);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), first);
    }

    [Fact]
    public void Verify_RightBodyAndKey_IsTrue()
    {
        Assert.True(EmailHandOff.Verify(Body, EmailHandOff.Sign(Body, Key), Key));
    }

    [Fact]
    public void Verify_TamperedBody_IsFalse()
    {
        var signature = EmailHandOff.Sign(Body, Key);
        var tampered = Encoding.UTF8.GetBytes("{\"eventId\":\"y\"}");

        Assert.False(EmailHandOff.Verify(tampered, signature, Key));
    }

    [Fact]
    public void Verify_WrongKey_IsFalse()
    {
        Assert.False(EmailHandOff.Verify(Body, EmailHandOff.Sign(Body, Key), "another-key"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_MissingSignature_IsFalse(string? provided)
    {
        Assert.False(EmailHandOff.Verify(Body, provided, Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_MissingKey_IsFalse(string? key)
    {
        Assert.False(EmailHandOff.Verify(Body, EmailHandOff.Sign(Body, Key), key));
    }

    [Fact]
    public void Contract_ConstantsArePinned()
    {
        Assert.Equal("Wa:EmailFunction:BaseUrl", EmailHandOff.BaseUrlKey);
        Assert.Equal("Wa:Email:HandOffSignKey", EmailHandOff.SignKeyKey);
        Assert.Equal("x-email-sign", EmailHandOff.HeaderName);
        Assert.Equal("events/email", EmailHandOff.Route);
        Assert.Equal("api/events/email", EmailHandOff.RelativeUrl);
    }
}
