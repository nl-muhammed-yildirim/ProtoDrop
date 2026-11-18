namespace wa.application.UseCases.Gdpr;

/// <summary>US-047-02 (FR-047-5): persistence port of the f-gdpr-sweep job (JobRun claim + Erasure markers).</summary>
public interface IGdprSweepStore
{
    /// <summary>Claims the window via the JobRun composite PK (TA-6.1); false when another instance already did.</summary>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>User ids of pending markers (SweptAtUtc null) deleted before <paramref name="deletedBeforeUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deletedBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Idempotent: only a still-pending marker is updated, so a swept row keeps its first time.</summary>
    Task MarkSweptAsync(Guid userId, DateTime sweptAtUtc, CancellationToken ct);
}

/// <summary>
/// US-047-02 (FR-047-5): removes a deleted user's userId-keyed telemetry. Takes a Guid only, so an email cannot be passed (EC-047-5).
/// Throws <see cref="TelemetryUnavailableException"/> when the telemetry store is down (EC-047-4).
/// </summary>
public interface ITelemetryEraser
{
    Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct);
}

/// <summary>The telemetry store could not be reached; the sweep leaves the marker pending and retries next night (EC-047-4).</summary>
public sealed class TelemetryUnavailableException : Exception
{
    public TelemetryUnavailableException() { }
    public TelemetryUnavailableException(string message) : base(message) { }
    public TelemetryUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>US-047-02: architecture constants of the sweep (TA-6.3 style, not Limits Registry values).</summary>
public static class GdprSweepPolicy
{
    /// <summary>A marker becomes due this long after the deletion.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    /// <summary>Telemetry retention (TA-11.2, wa-prod-ai 90 d) — the sweep scope.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <summary>Markers handled per run (TA-6.3 batch).</summary>
    public const int BatchSize = 500;
}
