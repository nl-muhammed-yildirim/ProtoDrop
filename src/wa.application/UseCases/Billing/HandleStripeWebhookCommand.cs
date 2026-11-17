using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-03 (TA-4.2a / FR-019-4): apply one signature-verified Stripe webhook body exactly once.
/// The endpoint verifies Stripe-Signature (it owns config + raw body); this command carries the verified raw payload.
/// </summary>
public sealed record HandleStripeWebhookCommand(string Payload) : IRequest<StripeWebhookOutcome>;

public enum StripeWebhookOutcome { Applied, Duplicate, Ignored }

/// <summary>US-019-03: house style — private ctor + static factories. Malformed → 400 VALIDATION; Retryable → 500 so Stripe redelivers (EC-019-1).</summary>
public sealed class StripeWebhookException : Exception
{
    private StripeWebhookException(string message, bool retryable) : base(message) => Retryable = retryable;

    public bool Retryable { get; }

    public static StripeWebhookException ForMalformed() =>
        new("Stripe webhook payload is malformed.", retryable: false);

    /// <summary>Message carries Stripe ids only (TA-9.4).</summary>
    public static StripeWebhookException ForRetryable(string message) => new(message, retryable: true);
}

public sealed class HandleStripeWebhookHandler : IRequestHandler<HandleStripeWebhookCommand, StripeWebhookOutcome>
{
    private const string SubscriptionCreated = "customer.subscription.created";
    private const string SubscriptionUpdated = "customer.subscription.updated";

    private readonly IStripeBillingStore _store;
    private readonly IPlansStore _plans;
    private readonly IEventPublisher _events;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<HandleStripeWebhookHandler> _logger;

    public HandleStripeWebhookHandler(
        IStripeBillingStore store,
        IPlansStore plans,
        IEventPublisher events,
        WaTelemetryReporter telemetry,
        ILogger<HandleStripeWebhookHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StripeWebhookOutcome> Handle(HandleStripeWebhookCommand request, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(request.Payload); }
        catch (JsonException) { throw StripeWebhookException.ForMalformed(); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw StripeWebhookException.ForMalformed();

            var eventId = GetString(root, "id");
            var type = GetString(root, "type");
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(type)
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("object", out var obj) || obj.ValueKind != JsonValueKind.Object)
                throw StripeWebhookException.ForMalformed();

            if (type != SubscriptionCreated && type != SubscriptionUpdated)
            {
                // AC-019-4: unknown type → 200 + log line, no mirror change, no StripeEvent row. Stripe ids only (TA-9.4).
                _logger.LogInformation("Stripe webhook event ignored: {StripeEventId} {StripeEventType}", eventId, type);
                return StripeWebhookOutcome.Ignored;
            }

            var subId = GetString(obj, "id");
            var status = GetString(obj, "status");
            if (string.IsNullOrEmpty(subId) || string.IsNullOrEmpty(status))
                throw StripeWebhookException.ForMalformed();

            JsonElement? firstItem = null;
            if (obj.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object
                && items.TryGetProperty("data", out var itemData) && itemData.ValueKind == JsonValueKind.Array
                && itemData.GetArrayLength() > 0)
                firstItem = itemData[0];

            string? priceId = null;
            if (firstItem is { ValueKind: JsonValueKind.Object } item
                && item.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object)
                priceId = GetString(price, "id");

            var periodEnd = ReadUnix(obj, "current_period_end")
                ?? (firstItem is { ValueKind: JsonValueKind.Object } item2 ? ReadUnix(item2, "current_period_end") : null);

            Guid? metadataUserId = null;
            if (obj.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                && Guid.TryParse(GetString(metadata, "userId"), out var parsed))
                metadataUserId = parsed;

            var change = new StripeSubscriptionChange(
                eventId, type, subId, GetString(obj, "customer"), status, priceId, periodEnd, metadataUserId);

            // D-4: status → target plan.
            var targetPlanId = await ResolveTargetPlanAsync(change, ct);

            var result = await _store.ApplySubscriptionAsync(change, targetPlanId, DateTime.UtcNow, ct);
            switch (result.Outcome)
            {
                case StripeApplyOutcome.Duplicate:
                    return StripeWebhookOutcome.Duplicate;
                case StripeApplyOutcome.UserUnresolved:
                    throw StripeWebhookException.ForRetryable(
                        $"Stripe event {eventId} (subscription {subId}) did not resolve to a user; awaiting redelivery.");
            }

            if (result.FromPlanId != result.ToPlanId)
            {
                var envelope = new EventEnvelope
                {
                    EventId = Guid.NewGuid(),
                    Type = EventType.PlanChanged,
                    Version = 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    CorrelationId = GetCorrelationGuid(),
                    PartitionKey = result.UserId.ToString("n"),
                    Payload = new
                    {
                        userId = result.UserId,
                        fromPlan = result.FromPlanCode,
                        toPlan = result.ToPlanCode,
                        reason = type
                    }
                };
                await _events.PublishAsync(envelope, ct);
                _telemetry.LogEvent(TelemetryEvents.PlanChanged);
            }

            return StripeWebhookOutcome.Applied;
        }
    }

    private async Task<Guid?> ResolveTargetPlanAsync(StripeSubscriptionChange change, CancellationToken ct)
    {
        switch (change.Status)
        {
            case "active":
            case "trialing":
            case "past_due":
                var plan = string.IsNullOrEmpty(change.PriceId)
                    ? null
                    : await _store.FindPlanByStripePriceIdAsync(change.PriceId, ct);
                if (plan is null)
                    throw StripeWebhookException.ForRetryable(
                        $"Stripe event {change.StripeEventId} (subscription {change.StripeSubId}) carries price '{change.PriceId}' that maps to no plan.");
                return plan.Id;
            case "canceled":
            case "incomplete_expired":
            case "unpaid":
                return (await _plans.GetAsync(Plan.DefaultCode, ct)).Id;
            default:
                return null; // incomplete / paused: mirror only (paused + grace belong to US-019-05)
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? ReadUnix(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
            : null;

    /// <summary>Mirrors AdminDeleteTransferHandler.GetCorrelationGuid verbatim.</summary>
    private static Guid GetCorrelationGuid()
    {
        var traceId = CorrelationContext.CurrentRequest?.TraceId;
        if (traceId is not null && traceId.Length == 32)
        {
            var hex = traceId.Replace("-", "");
            if (hex.Length == 32 && System.Linq.Enumerable.All(hex, c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return new Guid(hex);
        }
        return Guid.NewGuid();
    }
}
