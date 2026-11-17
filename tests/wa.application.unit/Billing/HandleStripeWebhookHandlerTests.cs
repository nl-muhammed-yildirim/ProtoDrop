using System.Text.Json;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Billing;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Billing;

/// <summary>US-019-03 (AC-019-3 / AC-019-4 unit halves, FR-019-4, FR-019-8): HandleStripeWebhookHandler.</summary>
public class HandleStripeWebhookHandlerTests
{
    private static readonly Guid FreeId = Guid.NewGuid();
    private static readonly Guid ProId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class Rig
    {
        public StubBillingStore Store { get; } = new();
        public StubPlansStore Plans { get; } = new();
        public RecordingPublisher Events { get; } = new();
        public CapturingLogger<WaTelemetryReporter> TelemetryLog { get; } = new();
        public CapturingLogger<HandleStripeWebhookHandler> HandlerLog { get; } = new();

        public Rig()
        {
            Store.PricePlan = new Plan { Id = ProId, Code = "pro" };
            Plans.Free = new Plan { Id = FreeId, Code = Plan.DefaultCode };
            Store.Result = new StripeApplyResult(StripeApplyOutcome.Applied, UserId, FreeId, ProId, Plan.DefaultCode, "pro");
        }

        public HandleStripeWebhookHandler Handler() =>
            new(Store, Plans, Events, new WaTelemetryReporter(TelemetryLog, [1, 2, 3]), HandlerLog);
    }

    private static string Event(string type, string status = "active", string id = "evt_1", string price = "price_pro",
        bool topLevelPeriod = true, bool itemPeriod = false, string? userId = null)
    {
        var period = topLevelPeriod ? ",\"current_period_end\":1800000000" : "";
        var itemP = itemPeriod ? ",\"current_period_end\":1900000000" : "";
        var meta = userId is null ? "" : $",\"metadata\":{{\"userId\":\"{userId}\"}}";
        return $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"data\":{{\"object\":{{\"id\":\"sub_1\",\"customer\":\"cus_1\",\"status\":\"{status}\"{period}{meta},\"items\":{{\"data\":[{{\"price\":{{\"id\":\"{price}\"}}{itemP}}}]}}}}}}}}";
    }

    [Fact]
    public async Task UnknownType_IsIgnored_NoStoreCall_NoEvent_LogsIds()
    {
        var rig = new Rig();
        var outcome = await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.created", id: "evt_unk")), default);

        Assert.Equal(StripeWebhookOutcome.Ignored, outcome);
        Assert.Equal(0, rig.Store.ApplyCalls);
        Assert.Empty(rig.Events.Published);
        Assert.Contains(rig.HandlerLog.Entries, e => e.Message.Contains("evt_unk") && e.Message.Contains("customer.created"));
    }

    [Fact]
    public async Task Duplicate_ReturnsDuplicate_NoEvent_NoTelemetry()
    {
        var rig = new Rig();
        rig.Store.Result = StripeApplyResult.Of(StripeApplyOutcome.Duplicate);

        var outcome = await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated")), default);

        Assert.Equal(StripeWebhookOutcome.Duplicate, outcome);
        Assert.Empty(rig.Events.Published);
        Assert.False(rig.TelemetryLog.Mentions(TelemetryEvents.PlanChanged));
    }

    [Fact]
    public async Task FreeToPro_PublishesOnePlanChanged_AndTelemetry()
    {
        var rig = new Rig();
        var outcome = await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated")), default);

        Assert.Equal(StripeWebhookOutcome.Applied, outcome);
        var env = Assert.Single(rig.Events.Published);
        Assert.Equal(EventType.PlanChanged, env.Type);
        Assert.Equal(1, env.Version);
        Assert.Equal(UserId.ToString("n"), env.PartitionKey);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal(UserId, doc.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal("free", doc.RootElement.GetProperty("fromPlan").GetString());
        Assert.Equal("pro", doc.RootElement.GetProperty("toPlan").GetString());
        Assert.Equal("customer.subscription.updated", doc.RootElement.GetProperty("reason").GetString());
        Assert.Single(rig.TelemetryLog.Entries, e => e.Message == TelemetryEvents.PlanChanged);
    }

    [Fact]
    public async Task SamePlan_PublishesNothing()
    {
        var rig = new Rig();
        rig.Store.Result = new StripeApplyResult(StripeApplyOutcome.Applied, UserId, ProId, ProId, "pro", "pro");

        var outcome = await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated")), default);

        Assert.Equal(StripeWebhookOutcome.Applied, outcome);
        Assert.Empty(rig.Events.Published);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("incomplete_expired", false)]
    [InlineData("unpaid", false)]
    public async Task Status_MapsToTargetPlan(string status, bool expectPricePlan)
    {
        var rig = new Rig();
        await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated", status)), default);

        Assert.Equal(expectPricePlan ? ProId : FreeId, rig.Store.CapturedTargetPlanId);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("paused")]
    public async Task Status_IncompleteOrPaused_NoPlanChange(string status)
    {
        var rig = new Rig();
        await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated", status)), default);

        Assert.Equal(1, rig.Store.ApplyCalls);
        Assert.Null(rig.Store.CapturedTargetPlanId);
    }

    [Fact]
    public async Task UnmappedPrice_ActiveStatus_ThrowsRetryable_StoreApplyNotCalled()
    {
        var rig = new Rig();
        rig.Store.PricePlan = null;

        var ex = await Assert.ThrowsAsync<StripeWebhookException>(() =>
            rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.created")), default));

        Assert.True(ex.Retryable);
        Assert.Equal(0, rig.Store.ApplyCalls);
    }

    [Fact]
    public async Task UserUnresolved_ThrowsRetryable()
    {
        var rig = new Rig();
        rig.Store.Result = StripeApplyResult.Of(StripeApplyOutcome.UserUnresolved);

        var ex = await Assert.ThrowsAsync<StripeWebhookException>(() =>
            rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.created")), default));

        Assert.True(ex.Retryable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"customer.subscription.created\",\"data\":{\"object\":{}}}")]
    [InlineData("{\"id\":\"evt_1\",\"type\":\"customer.subscription.created\"}")]
    [InlineData("[]")]
    public async Task Malformed_ThrowsNonRetryable(string payload)
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<StripeWebhookException>(() =>
            rig.Handler().Handle(new HandleStripeWebhookCommand(payload), default));

        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task CurrentPeriodEnd_ReadFromTopLevel()
    {
        var rig = new Rig();
        await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated", topLevelPeriod: true, itemPeriod: true)), default);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000).UtcDateTime, rig.Store.CapturedChange!.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task CurrentPeriodEnd_FallsBackToItem()
    {
        var rig = new Rig();
        await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.updated", topLevelPeriod: false, itemPeriod: true)), default);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1900000000).UtcDateTime, rig.Store.CapturedChange!.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task Mapping_CarriesIdsAndMetadataUser()
    {
        var rig = new Rig();
        await rig.Handler().Handle(new HandleStripeWebhookCommand(Event("customer.subscription.created", userId: UserId.ToString())), default);

        var c = rig.Store.CapturedChange!;
        Assert.Equal("evt_1", c.StripeEventId);
        Assert.Equal("sub_1", c.StripeSubId);
        Assert.Equal("cus_1", c.StripeCustomerId);
        Assert.Equal("price_pro", c.PriceId);
        Assert.Equal(UserId, c.MetadataUserId);
    }

    // ---- Stubs (hand-rolled, no Moq) ----

    private sealed class StubBillingStore : IStripeBillingStore
    {
        public Plan? PricePlan { get; set; }
        public StripeApplyResult Result { get; set; } = StripeApplyResult.Of(StripeApplyOutcome.Applied);
        public int ApplyCalls { get; private set; }
        public Guid? CapturedTargetPlanId { get; private set; }
        public StripeSubscriptionChange? CapturedChange { get; private set; }

        public Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct) => Task.FromResult(PricePlan);

        public Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct)
        {
            ApplyCalls++;
            CapturedChange = change;
            CapturedTargetPlanId = targetPlanId;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Plan Free { get; set; } = null!;

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => Task.FromResult(Free);

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult<Plan?>(Free.Id == planId ? Free : null);
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
