namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (TA-6.1/6.3): worker-side surface of the f-data-export timer job.</summary>
public interface IDataExportJobStore
{
    /// <summary>Claims the 5-min window via JobRun (JobKey 'data-export'); false = another instance holds it.</summary>
    /// <param name="windowStartUtc">Start of the five-minute window being claimed.</param>
    /// <param name="lockUntilUtc">UTC instant until which other instances must not run the job.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this instance claimed the window; <see langword="false"/> when another already did.</returns>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Marks the claimed JobRun finished.</summary>
    /// <param name="windowStartUtc">Start of the claimed window.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>Processing rows, oldest first.</summary>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>Up to <paramref name="batchSize"/> exports still in the processing state, oldest first.</returns>
    Task<IReadOnlyList<wa.domain.DataExport>> GetProcessingAsync(int batchSize, CancellationToken ct);

    /// <summary>The owned-data snapshot for one user; null when the user row is missing or soft-deleted (EC-047-3).</summary>
    /// <param name="userId">The account to export.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The profile and transfers, or <see langword="null"/> when the user is missing or soft-deleted.</returns>
    Task<ExportSnapshot?> ReadSnapshotAsync(Guid userId, CancellationToken ct);

    /// <summary>Processing → Ready with the blob path (only if still Processing).</summary>
    /// <param name="id">Primary key of the export row.</param>
    /// <param name="blobPath">Blob path of the finished zip.</param>
    /// <param name="readyAtUtc">UTC instant stored as the completion time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkReadyAsync(Guid id, string blobPath, DateTime readyAtUtc, CancellationToken ct);

    /// <summary>Processing → Failed (EC-047-2).</summary>
    /// <param name="id">Primary key of the export row.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkFailedAsync(Guid id, CancellationToken ct);

    /// <summary>Ready rows with ReadyAtUtc before the cutoff (7-day retention purge), plus ready rows whose owner is no longer a live account (soft-deleted or missing) — their zips must not outlive the deletion (US-047-04).</summary>
    /// <param name="readyBeforeUtc">Retention cutoff: ready exports completed before this instant are purgeable.</param>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The purgeable exports with their blob paths; empty when none.</returns>
    Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Deletes the export row after its blob was removed.</summary>
    /// <param name="id">Primary key of the export row.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
