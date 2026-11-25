namespace wa.application.UseCases.Gdpr;

/// <summary>US-047-02 (FR-047-5): persistence port of the f-gdpr-sweep job (JobRun claim + Erasure markers).</summary>
public interface IGdprSweepStore
{
    /// <summary>Claims the window via the JobRun composite PK (TA-6.1); false when another instance already did.</summary>
    /// <param name="windowStartUtc">Start of the hourly window being claimed.</param>
    /// <param name="lockUntilUtc">UTC instant until which other instances must not run the job.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this instance claimed the window; <see langword="false"/> when another already did.</returns>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Marks the claimed window finished so that the run is recorded as complete.</summary>
    /// <param name="windowStartUtc">Start of the claimed window.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>User ids of pending markers (SweptAtUtc null) deleted before <paramref name="deletedBeforeUtc"/>, oldest first.</summary>
    /// <param name="deletedBeforeUtc">Only accounts deleted before this instant are due.</param>
    /// <param name="batchSize">Maximum number of ids to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>User ids with pending markers, oldest first.</returns>
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deletedBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Idempotent: only a still-pending marker is updated, so a swept row keeps its first time.</summary>
    /// <param name="userId">The account whose marker is completed.</param>
    /// <param name="sweptAtUtc">UTC instant stored as the sweep time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkSweptAsync(Guid userId, DateTime sweptAtUtc, CancellationToken ct);
}
