using System.Text.Json;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Billing;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Billing;

/// <summary>US-019-05 (D-4, AC-019-2, EC-019-3): EndBillingGraceHandler — the f-expire grace sub-step.</summary>
public class EndBillingGraceHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private const string Email = "payer@example.com";

    private sealed class Rig
    {
        public StubGraceStore Store { get; } = new();
        public RecordingPublisher Events { get; } = new();
        public CapturingLogger<WaTelemetryReporter> TelemetryLog { get; } = new();
        public CapturingLogger<EndBillingGraceHandler> Log { get; } = new();

        public EndBillingGraceHandler Handler() =>
            new(Store, Events, new WaTelemetryReporter(TelemetryLog, [1, 2, 3]), Log);
    }

    private static GraceDowngradeResult Downgraded(Guid userId) =>
        new(GraceDowngradeOutcome.Downgraded, userId, Email, "pro", "free");

    [Fact]
    public async Task Downgraded_PublishesPlanChanged_ThenGraceEnded_AndTelemetry()
    {
        var rig = new Rig();
        var subId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        rig.Store.Candidates.Add(new GraceCandidate(subId, Now.AddHours(-1)));
        rig.Store.Results[subId] = Downgraded(userId);

        var count = await rig.Handler().RunAsync(Now, default);

        Assert.Equal(1, count);
        Assert.Equal(2, rig.Events.Published.Count);

        var first = rig.Events.Published[0];
        Assert.Equal(EventType.PlanChanged, first.Type);
        Assert.Equal(userId.ToString("n"), first.PartitionKey);
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(first.Payload)))
        {
            Assert.Equal("pro", doc.RootElement.GetProperty("fromPlan").GetString());
            Assert.Equal("free", doc.RootElement.GetProperty("toPlan").GetString());
            Assert.Equal("grace_ended", doc.RootElement.GetProperty("reason").GetString());
        }

        var second = rig.Events.Published[1];
        Assert.Equal(EventType.BillingEmailRequested, second.Type);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(second.Payload)))
        {
            Assert.Equal("billing_grace_ended", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(Email, doc.RootElement.GetProperty("email").GetString());
            Assert.Equal(subId, doc.RootElement.GetProperty("subscriptionId").GetGuid());
        }

        Assert.Single(rig.TelemetryLog.Entries, e => e.Message == TelemetryEvents.PlanChanged);
    }

    [Theory]
    [InlineData(GraceDowngradeOutcome.NotDue)]        // EC-019-3: resumed / grace moved before the timer ran
    [InlineData(GraceDowngradeOutcome.AlreadyClaimed)] // double run
    [InlineData(GraceDowngradeOutcome.NoChange)]
    public async Task NotDowngraded_PublishesNothing(GraceDowngradeOutcome outcome)
    {
        var rig = new Rig();
        var subId = Guid.NewGuid();
        rig.Store.Candidates.Add(new GraceCandidate(subId, Now.AddHours(-1)));
        rig.Store.Results[subId] = new GraceDowngradeResult(outcome);

        var count = await rig.Handler().RunAsync(Now, default);

        Assert.Equal(0, count);
        Assert.Empty(rig.Events.Published);
        Assert.False(rig.TelemetryLog.Mentions(TelemetryEvents.PlanChanged));
    }

    [Fact]
    public async Task TwoCandidates_OneDowngraded_EachGetsItsOwnAnchor()
    {
        var rig = new Rig();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var anchorA = Now.AddDays(-2);
        var anchorB = Now.AddHours(-3);
        rig.Store.Candidates.Add(new GraceCandidate(a, anchorA));
        rig.Store.Candidates.Add(new GraceCandidate(b, anchorB));
        rig.Store.Results[a] = new GraceDowngradeResult(GraceDowngradeOutcome.NotDue);
        rig.Store.Results[b] = Downgraded(Guid.NewGuid());

        var count = await rig.Handler().RunAsync(Now, default);

        Assert.Equal(1, count);
        Assert.Equal([(a, anchorA), (b, anchorB)], rig.Store.Calls);
    }

    [Fact]
    public async Task Logs_CarryNoEmail()
    {
        var rig = new Rig();
        var subId = Guid.NewGuid();
        rig.Store.Candidates.Add(new GraceCandidate(subId, Now.AddHours(-1)));
        rig.Store.Results[subId] = Downgraded(Guid.NewGuid());

        await rig.Handler().RunAsync(Now, default);

        Assert.NotEmpty(rig.Log.Entries);
        Assert.False(rig.Log.Mentions(Email));
        Assert.False(rig.TelemetryLog.Mentions(Email));
    }

    private sealed class StubGraceStore : IBillingGraceStore
    {
        public List<GraceCandidate> Candidates { get; } = [];
        public Dictionary<Guid, GraceDowngradeResult> Results { get; } = [];
        public List<(Guid SubscriptionId, DateTime GraceEndsAtUtc)> Calls { get; } = [];

        public Task<IReadOnlyList<GraceCandidate>> GetExpiredGracesAsync(DateTime nowUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GraceCandidate>>(Candidates);

        public Task<GraceDowngradeResult> TryDowngradeAsync(Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, CancellationToken ct)
        {
            Calls.Add((subscriptionId, graceEndsAtUtc));
            return Task.FromResult(Results[subscriptionId]);
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }
}
