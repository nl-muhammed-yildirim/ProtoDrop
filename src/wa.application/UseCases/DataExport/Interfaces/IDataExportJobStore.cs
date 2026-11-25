namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (TA-6.1/6.3): worker-side surface of the f-data-export timer job.</summary>
public interface IDataExportJobStore
{
    /// <summary>Claims the 5-min window via JobRun (JobKey 'data-export'); false = another instance holds it.</summary>
    /// <param name="windowStartUtc">The window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Marks the claimed JobRun finished.</summary>
    /// <param name="windowStartUtc">The window start UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>Processing rows, oldest first.</summary>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching data export items.</returns>
    Task<IReadOnlyList<wa.domain.DataExport>> GetProcessingAsync(int batchSize, CancellationToken ct);

    /// <summary>The owned-data snapshot for one user; null when the user row is missing or soft-deleted (EC-047-3).</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The export snapshot, or <see langword="null"/> when there is none.</returns>
    Task<ExportSnapshot?> ReadSnapshotAsync(Guid userId, CancellationToken ct);

    /// <summary>Processing → Ready with the blob path (only if still Processing).</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="blobPath">The blob path.</param>
    /// <param name="readyAtUtc">The ready at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkReadyAsync(Guid id, string blobPath, DateTime readyAtUtc, CancellationToken ct);

    /// <summary>Processing → Failed (EC-047-2).</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkFailedAsync(Guid id, CancellationToken ct);

    /// <summary>Ready rows with ReadyAtUtc before the cutoff (7-day retention purge), plus ready rows whose owner is no longer a live account (soft-deleted or missing) — their zips must not outlive the deletion (US-047-04).</summary>
    /// <param name="readyBeforeUtc">The ready before UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching purgeable export items.</returns>
    Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Deletes the row.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
