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
