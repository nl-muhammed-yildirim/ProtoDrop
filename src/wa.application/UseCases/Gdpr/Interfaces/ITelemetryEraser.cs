namespace wa.application.UseCases.Gdpr;

/// <summary>
/// US-047-02 (FR-047-5): removes a deleted user's userId-keyed telemetry. Takes a Guid only, so an email cannot be passed (EC-047-5).
/// Throws <see cref="TelemetryUnavailableException"/> when the telemetry store is down (EC-047-4).
/// </summary>
public interface ITelemetryEraser
{
    /// <summary>Deletes the telemetry that is keyed by the user id from the telemetry store.</summary>
    /// <param name="userId">The deleted account whose telemetry is erased.</param>
    /// <param name="sinceUtc">Lower bound of the erase window (the account's deletion time).</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct);
}
