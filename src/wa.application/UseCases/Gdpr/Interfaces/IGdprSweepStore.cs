namespace wa.application.UseCases.Gdpr;

/// <summary>US-047-02 (FR-047-5): persistence port of the f-gdpr-sweep job (JobRun claim + Erasure markers).</summary>
public interface IGdprSweepStore
{
    /// <summary>Claims the window via the JobRun composite PK (TA-6.1); false when another instance already did.</summary>
    /// <param name="windowStartUtc">The window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Completes the claim.</summary>
    /// <param name="windowStartUtc">The window start UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>User ids of pending markers (SweptAtUtc null) deleted before <paramref name="deletedBeforeUtc"/>, oldest first.</summary>
    /// <param name="deletedBeforeUtc">The deleted before UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching identifier items.</returns>
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deletedBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Idempotent: only a still-pending marker is updated, so a swept row keeps its first time.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="sweptAtUtc">The swept at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkSweptAsync(Guid userId, DateTime sweptAtUtc, CancellationToken ct);
}
