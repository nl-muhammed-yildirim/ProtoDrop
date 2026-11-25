namespace wa.application.UseCases.Gdpr;

/// <summary>
/// US-047-02 (FR-047-5): removes a deleted user's userId-keyed telemetry. Takes a Guid only, so an email cannot be passed (EC-047-5).
/// Throws <see cref="TelemetryUnavailableException"/> when the telemetry store is down (EC-047-4).
/// </summary>
public interface ITelemetryEraser
{
    Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct);
}
