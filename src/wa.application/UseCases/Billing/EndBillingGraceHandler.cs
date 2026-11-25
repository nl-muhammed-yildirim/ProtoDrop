using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-05 (D-4, TA-6.3, FR-019-6): f-expire sub-step — downgrades users to Free once a paused subscription's grace has ended.
/// The store re-reads the subscription under a row lock and claims the JobRun (EC-019-3 / TA-6.1); events publish after commit.
/// </summary>
public sealed class EndBillingGraceHandler
{
    private readonly IBillingGraceStore _store;
    private readonly IEventPublisher _events;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<EndBillingGraceHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="EndBillingGraceHandler"/> class.</summary>
    /// <param name="store">Store that finds and downgrades expired graces.</param>
    /// <param name="events">Publishes the <c>plan.changed</c> and billing email events.</param>
    /// <param name="telemetry">Reporter for the <c>plan_changed</c> telemetry event.</param>
    /// <param name="logger">Logger for diagnostics (never logs emails).</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EndBillingGraceHandler(
        IBillingGraceStore store, IEventPublisher events, WaTelemetryReporter telemetry, ILogger<EndBillingGraceHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Downgrades every user whose paused subscription has passed its grace end and publishes the resulting events.</summary>
    /// <param name="nowUtc">Current UTC time used to find expired graces.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The number of users that were downgraded to the free plan.</returns>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var downgraded = 0;
        foreach (var candidate in await _store.GetExpiredGracesAsync(nowUtc, ct))
        {
            var result = await _store.TryDowngradeAsync(candidate.SubscriptionId, candidate.GraceEndsAtUtc, nowUtc, ct);
            if (result.Outcome != GraceDowngradeOutcome.Downgraded)
            {
                _logger.LogInformation(
                    "Billing grace skipped: subscription {SubscriptionId} outcome {Outcome}", candidate.SubscriptionId, result.Outcome);
                continue;
            }

            var correlationId = Guid.NewGuid();
            await _events.PublishAsync(BillingGrace.BuildPlanChangedEnvelope(
                result.UserId, result.FromPlanCode!, result.ToPlanCode!, nowUtc, correlationId), ct);
            _telemetry.LogEvent(TelemetryEvents.PlanChanged);

            if (!string.IsNullOrWhiteSpace(result.Email))
                await _events.PublishAsync(BillingGrace.BuildGraceEndedEnvelope(
                    result.UserId, result.Email, candidate.SubscriptionId, nowUtc, correlationId), ct);

            downgraded++;
            _logger.LogInformation("Billing grace ended: subscription {SubscriptionId} downgraded", candidate.SubscriptionId);
        }
        return downgraded;
    }
}
