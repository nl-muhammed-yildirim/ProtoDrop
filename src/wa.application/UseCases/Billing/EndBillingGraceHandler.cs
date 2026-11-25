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
    /// <param name="store">The store.</param>
    /// <param name="events">The events.</param>
    /// <param name="telemetry">The telemetry.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public EndBillingGraceHandler(
        IBillingGraceStore store, IEventPublisher events, WaTelemetryReporter telemetry, ILogger<EndBillingGraceHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns the number of users downgraded.</summary>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The resulting numeric value.</returns>
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
