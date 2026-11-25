using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-04 (D-6, TA-6.3): f-expire sub-step — queues billing email #2 (+3 d) and #3 (+7 d). Each send is gated by its own
/// JobRun step claim, so scale-out and retries never double-send.
/// </summary>
public sealed class SendBillingEmailsHandler
{
    private readonly IBillingSequenceStore _store;
    private readonly IEventPublisher _events;
    private readonly ILogger<SendBillingEmailsHandler> _logger;

    public SendBillingEmailsHandler(IBillingSequenceStore store, IEventPublisher events, ILogger<SendBillingEmailsHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns the number of emails queued.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var queued = 0;
        foreach (var row in await _store.GetOpenSequencesAsync(ct))
        {
            var step = BillingEmailSchedule.NextDueStep(row.AnchorUtc, row.ClaimedSteps, nowUtc);
            if (step is null)
                continue;

            if (!await _store.TryClaimStepAsync(row.SubscriptionId, step.Value, row.AnchorUtc, nowUtc, ct))
                continue;

            var envelope = BillingEmailSchedule.BuildEnvelope(
                row.UserId, row.Email, row.SubscriptionId, step.Value, nowUtc, Guid.NewGuid());
            await _events.PublishAsync(envelope, ct);
            queued++;

            _logger.LogInformation(
                "Billing email queued: subscription {SubscriptionId} step {Step}", row.SubscriptionId, step.Value);
        }
        return queued;
    }
}
