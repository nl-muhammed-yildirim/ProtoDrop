namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (TA-6.1/6.3): worker-side surface of the f-data-export timer job.</summary>
public interface IDataExportJobStore
{
    /// <summary>Claims the 5-min window via JobRun (JobKey 'data-export'); false = another instance holds it.</summary>
    Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Marks the claimed JobRun finished.</summary>
    Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct);

    /// <summary>Processing rows, oldest first.</summary>
    Task<IReadOnlyList<wa.domain.DataExport>> GetProcessingAsync(int batchSize, CancellationToken ct);

    /// <summary>The owned-data snapshot for one user; null when the user row is missing or soft-deleted (EC-047-3).</summary>
    Task<ExportSnapshot?> ReadSnapshotAsync(Guid userId, CancellationToken ct);

    /// <summary>Processing → Ready with the blob path (only if still Processing).</summary>
    Task MarkReadyAsync(Guid id, string blobPath, DateTime readyAtUtc, CancellationToken ct);

    /// <summary>Processing → Failed (EC-047-2).</summary>
    Task MarkFailedAsync(Guid id, CancellationToken ct);

    /// <summary>Ready rows with ReadyAtUtc before the cutoff (7-day retention purge), plus ready rows whose owner is no longer a live account (soft-deleted or missing) — their zips must not outlive the deletion (US-047-04).</summary>
    Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct);

    /// <summary>Deletes the row.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
