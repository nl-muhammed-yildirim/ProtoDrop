using System.Text.Json;
using wa.application.Ports;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-04 (D-6, AC-019-2): SendBillingEmailsHandler — the f-expire sub-step, with a fake clock.</summary>
public class SendBillingEmailsHandlerTests
{
    private static readonly DateTime Anchor = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string Email = "payer@example.com";

    private static (SendBillingEmailsHandler Handler, StubSequenceStore Store, RecordingPublisher Events, CapturingLogger<SendBillingEmailsHandler> Log) Create()
    {
        var store = new StubSequenceStore();
        var events = new RecordingPublisher();
        var log = new CapturingLogger<SendBillingEmailsHandler>();
        return (new SendBillingEmailsHandler(store, events, log), store, events, log);
    }

    private static BillingSequenceRow Row(Guid subId, params int[] claimed) =>
        new(subId, Guid.NewGuid(), Email, Anchor, claimed);

    [Fact]
    public async Task DueStep2_ClaimsWithRowAnchor_PublishesOnce_ReturnsOne()
    {
        var (handler, store, events, _) = Create();
        var subId = Guid.NewGuid();
        store.Rows.Add(Row(subId, 1));

        var queued = await handler.RunAsync(Anchor.AddDays(3), default);

        Assert.Equal(1, queued);
        var claim = Assert.Single(store.Claims);
        Assert.Equal((subId, 2, Anchor), claim);
        var env = Assert.Single(events.Published);
        Assert.Equal(EventType.BillingEmailRequested, env.Type);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal(2, doc.RootElement.GetProperty("step").GetInt32());
    }

    [Fact]
    public async Task ClaimLost_PublishesNothing()
    {
        var (handler, store, events, _) = Create();
        store.Rows.Add(Row(Guid.NewGuid(), 1));
        store.ClaimResult = false;

        var queued = await handler.RunAsync(Anchor.AddDays(3), default);

        Assert.Equal(0, queued);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task NotYetDue_NoClaimCall()
    {
        var (handler, store, events, _) = Create();
        store.Rows.Add(Row(Guid.NewGuid(), 1));

        var queued = await handler.RunAsync(Anchor.AddDays(2), default);

        Assert.Equal(0, queued);
        Assert.Empty(store.Claims);
        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task TwoRows_OneDue_OneEnvelope()
    {
        var (handler, store, events, _) = Create();
        var due = Guid.NewGuid();
        store.Rows.Add(Row(due, 1));
        store.Rows.Add(Row(Guid.NewGuid(), 1, 2, 3));

        var queued = await handler.RunAsync(Anchor.AddDays(3), default);

        Assert.Equal(1, queued);
        Assert.Single(events.Published);
        Assert.Equal(due, Assert.Single(store.Claims).SubscriptionId);
    }

    [Fact]
    public async Task Logs_CarryNoEmail()
    {
        var (handler, store, _, log) = Create();
        store.Rows.Add(Row(Guid.NewGuid(), 1));

        await handler.RunAsync(Anchor.AddDays(3), default);

        Assert.NotEmpty(log.Entries);
        Assert.False(log.Mentions(Email));
    }

    private sealed class StubSequenceStore : IBillingSequenceStore
    {
        public List<BillingSequenceRow> Rows { get; } = [];
        public bool ClaimResult { get; set; } = true;
        public List<(Guid SubscriptionId, int Step, DateTime AnchorUtc)> Claims { get; } = [];

        public Task<PaymentFailedResult> StartPaymentFailedAsync(string eventId, string invoiceId, string stripeSubId, DateTime nowUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<InvoicePaidOutcome> CancelSequenceAsync(string eventId, string stripeSubId, DateTime nowUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BillingSequenceRow>> GetOpenSequencesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BillingSequenceRow>>(Rows);

        public Task<bool> TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, CancellationToken ct)
        {
            Claims.Add((subscriptionId, step, anchorUtc));
            return Task.FromResult(ClaimResult);
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
