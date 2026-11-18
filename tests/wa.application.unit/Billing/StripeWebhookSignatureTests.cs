using System.Security.Cryptography;
using System.Text;
using wa.application.UseCases.Billing;

namespace wa.application.unit.Billing;

/// <summary>US-019-03 (FR-019-5): Stripe-Signature verification — BCL HMAC, fail closed.</summary>
public class StripeWebhookSignatureTests
{
    private const string Secret = "whsec_test_secret";
    private const string Body = "{\"id\":\"evt_1\",\"type\":\"customer.created\"}";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static string Sign(string payload, long t, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant();
    }

    [Fact]
    public void ValidHeader_ReturnsTrue()
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.True(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void WrongSecret_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t, "other")}", Body, Secret, Now));
    }

    [Fact]
    public void PayloadChangedByOneByte_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds();
        var header = $"t={t},v1={Sign(Body, t)}";
        Assert.False(StripeWebhookSignature.IsValid(header, Body + " ", Secret, Now));
    }

    [Fact]
    public void TimestampOlderThanTolerance_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds() - 301;
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void TimestampTooFarInFuture_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds() + 301;
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void TimestampAtToleranceEdge_ReturnsTrue()
    {
        var t = Now.ToUnixTimeSeconds() - 300;
        Assert.True(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void TwoV1Entries_SecondValid_ReturnsTrue()
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.True(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t, "old")},v1={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void HeaderWithoutV1_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v0={Sign(Body, t)}", Body, Secret, Now));
    }

    [Fact]
    public void GarbledTimestamp_ReturnsFalse()
    {
        Assert.False(StripeWebhookSignature.IsValid($"t=abc,v1={Sign(Body, 1)}", Body, Secret, Now));
    }

    [Fact]
    public void NonHexV1_ReturnsFalse()
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v1=zzzz", Body, Secret, Now));
    }

    [Fact]
    public void NullHeader_ReturnsFalse() =>
        Assert.False(StripeWebhookSignature.IsValid(null, Body, Secret, Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingSecret_ReturnsFalse(string? secret)
    {
        var t = Now.ToUnixTimeSeconds();
        Assert.False(StripeWebhookSignature.IsValid($"t={t},v1={Sign(Body, t)}", Body, secret, Now));
    }
}
