using System.Text.Json;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-04 (FR-019-2, AC-019-2, EC-019-2): BillingEmailSchedule — pure schedule, eligibility, keys, envelope.</summary>
public class BillingEmailScheduleTests
{
    private static readonly DateTime Anchor = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NextDueStep_Step1Claimed_BeforeThreeDays_IsNull() =>
        Assert.Null(BillingEmailSchedule.NextDueStep(Anchor, [1], Anchor.AddDays(2).AddHours(23)));

    [Fact]
    public void NextDueStep_Step1Claimed_AtThreeDays_IsStep2() =>
        Assert.Equal(2, BillingEmailSchedule.NextDueStep(Anchor, [1], Anchor.AddDays(3)));

    [Fact]
    public void NextDueStep_Steps1And2Claimed_BeforeSevenDays_IsNull() =>
        Assert.Null(BillingEmailSchedule.NextDueStep(Anchor, [1, 2], Anchor.AddDays(6)));

    [Fact]
    public void NextDueStep_Steps1And2Claimed_AtSevenDays_IsStep3() =>
        Assert.Equal(3, BillingEmailSchedule.NextDueStep(Anchor, [1, 2], Anchor.AddDays(7)));

    [Fact]
    public void NextDueStep_AllClaimed_IsNull() =>
        Assert.Null(BillingEmailSchedule.NextDueStep(Anchor, [1, 2, 3], Anchor.AddDays(30)));

    [Fact]
    public void NextDueStep_MissedPass_DoesNotSkipAhead() =>
        Assert.Equal(2, BillingEmailSchedule.NextDueStep(Anchor, [1], Anchor.AddDays(8)));

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("incomplete", false)]
    [InlineData("incomplete_expired", false)]
    [InlineData("unpaid", false)]
    [InlineData("paused", false)]
    public void IsEligible_ByStatus(string status, bool expected) =>
        Assert.Equal(expected, BillingEmailSchedule.IsEligible(status));

    [Fact]
    public void StepJobKey_Format_AndLengthWithinJobKeyLimit()
    {
        var id = Guid.NewGuid();
        var key = BillingEmailSchedule.StepJobKey(id, 2);

        Assert.Equal($"billing-pf:{id:n}:2", key);
        Assert.True(key.Length <= 64);
    }

    [Fact]
    public void InvoiceDedupKey_IsPrefixed() =>
        Assert.Equal("pf:in_x", BillingEmailSchedule.InvoiceDedupKey("in_x"));

    [Fact]
    public void BuildEnvelope_HasTa52Shape_AndPayload()
    {
        var userId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var env = BillingEmailSchedule.BuildEnvelope(userId, "a@example.com", subId, 3, Anchor, correlation);

        Assert.Equal(EventType.BillingEmailRequested, env.Type);
        Assert.Equal(1, env.Version);
        Assert.Equal(userId.ToString("n"), env.PartitionKey);
        Assert.Equal(correlation, env.CorrelationId);
        Assert.Equal(Anchor, env.OccurredAtUtc);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        var p = doc.RootElement;
        Assert.Equal(userId, p.GetProperty("userId").GetGuid());
        Assert.Equal("a@example.com", p.GetProperty("email").GetString());
        Assert.Equal(subId, p.GetProperty("subscriptionId").GetGuid());
        Assert.Equal("billing_payment_failed", p.GetProperty("kind").GetString());
        Assert.Equal(3, p.GetProperty("step").GetInt32());
    }
}
