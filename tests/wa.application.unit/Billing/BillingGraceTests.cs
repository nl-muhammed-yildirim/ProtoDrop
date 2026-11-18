using System.Text.Json;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-05 (D-2, FR-019-6, EC-019-3): pure grace rules — date math, transition table, expiry predicate, keys, envelopes.</summary>
public class BillingGraceTests
{
    private static readonly DateTime T = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Duration_Is14Days() => Assert.Equal(TimeSpan.FromDays(14), BillingGrace.Duration);

    [Theory]
    [InlineData("active")]
    [InlineData("past_due")]
    [InlineData(null)]
    public void Pause_FromAnyOtherStatusOrNewRow_StartsGrace(string? previous)
    {
        Assert.Equal(T + TimeSpan.FromDays(14), BillingGrace.NextGraceEndsAt(previous, null, "paused", T));
    }

    [Fact]
    public void Pause_WhenAlreadyPausedWithGrace_DoesNotExtend()
    {
        var g = T.AddDays(14);
        Assert.Equal(g, BillingGrace.NextGraceEndsAt("paused", g, "paused", T.AddDays(3)));
    }

    [Fact]
    public void Pause_WhenAlreadyPausedWithoutGrace_StartsGrace() =>
        Assert.Equal(T + BillingGrace.Duration, BillingGrace.NextGraceEndsAt("paused", null, "paused", T));

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("canceled")]
    public void NonPausedStatus_ClearsGrace(string status)
    {
        var g = T.AddDays(14);
        Assert.Null(BillingGrace.NextGraceEndsAt("paused", g, status, T.AddDays(5)));
        Assert.Null(BillingGrace.NextGraceEndsAt("active", null, status, T));
    }

    [Fact]
    public void IsExpired_AfterResume_False()
    {
        var g = T.AddDays(14);
        var next = BillingGrace.NextGraceEndsAt("paused", g, "active", T.AddDays(5));
        Assert.False(BillingGrace.IsExpired("active", next, g.AddDays(1)));
    }

    [Fact]
    public void IsExpired_Boundary()
    {
        var g = T.AddDays(14);
        Assert.False(BillingGrace.IsExpired("paused", g, g.AddTicks(-1)));
        Assert.True(BillingGrace.IsExpired("paused", g, g));
        Assert.True(BillingGrace.IsExpired("paused", g, g.AddDays(1)));
    }

    [Fact]
    public void IsExpired_NotPausedOrNoGrace_False()
    {
        var g = T.AddDays(14);
        Assert.False(BillingGrace.IsExpired("active", g, g.AddDays(1))); // EC-019-3: resumed before the timer
        Assert.False(BillingGrace.IsExpired("paused", null, g.AddDays(1)));
    }

    [Fact]
    public void ClaimJobKey_Format_FitsLimit_AndDistinctFromEmailSteps()
    {
        var id = Guid.NewGuid();
        var key = BillingGrace.ClaimJobKey(id);

        Assert.Equal($"billing-grace:{id:n}", key);
        Assert.True(key.Length <= 64);
        Assert.NotEqual(BillingEmailSchedule.StepJobKey(id, 1), key);
        Assert.False(key.StartsWith("billing-pf:", StringComparison.Ordinal));
    }

    [Fact]
    public void GraceEndedEnvelope_Shape()
    {
        var userId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var corr = Guid.NewGuid();
        var env = BillingGrace.BuildGraceEndedEnvelope(userId, "a@example.com", subId, T, corr);

        Assert.Equal(EventType.BillingEmailRequested, env.Type);
        Assert.Equal(1, env.Version);
        Assert.Equal(userId.ToString("n"), env.PartitionKey);
        Assert.Equal(corr, env.CorrelationId);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("billing_grace_ended", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("step").GetInt32());
        Assert.Equal("a@example.com", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal(subId, doc.RootElement.GetProperty("subscriptionId").GetGuid());
    }

    [Fact]
    public void PlanChangedEnvelope_Shape()
    {
        var userId = Guid.NewGuid();
        var env = BillingGrace.BuildPlanChangedEnvelope(userId, "pro", "free", T, Guid.NewGuid());

        Assert.Equal(EventType.PlanChanged, env.Type);
        Assert.Equal(1, env.Version);
        Assert.Equal(userId.ToString("n"), env.PartitionKey);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("pro", doc.RootElement.GetProperty("fromPlan").GetString());
        Assert.Equal("free", doc.RootElement.GetProperty("toPlan").GetString());
        Assert.Equal("grace_ended", doc.RootElement.GetProperty("reason").GetString());
        Assert.Equal(userId, doc.RootElement.GetProperty("userId").GetGuid());
    }
}
