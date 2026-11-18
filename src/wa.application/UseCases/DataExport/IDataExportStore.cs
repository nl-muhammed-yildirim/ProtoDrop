using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1/2): API-side DataExport surface (TA-2.3 dependency inversion). Adapter in wa.infrastructure.Persistence.</summary>
public interface IDataExportStore
{
    /// <summary>The user's most recent export row (by CreatedAtUtc), or null when none.</summary>
    Task<wa.domain.DataExport?> GetLatestAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts the row; false on a unique violation (2627 / 2601) = another processing export already exists (EC-047-6).</summary>
    Task<bool> TryCreateAsync(wa.domain.DataExport row, CancellationToken ct);
}

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

/// <summary>A ready export past retention: the row id and the blob to delete.</summary>
public sealed record PurgeableExport(Guid Id, string BlobPath);

/// <summary>profile.json source (FR-047-1).</summary>
public sealed record ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc);

/// <summary>One file's name and size in transfers.json.</summary>
public sealed record ExportFile(string Name, long SizeBytes);

/// <summary>One owned transfer in transfers.json (FR-047-1, AC-047-3).</summary>
public sealed record ExportTransfer(
    Guid Id,
    DateTime CreatedAtUtc,
    byte Status,
    DateTime ExpiresAtUtc,
    bool PasswordSet,
    int DownloadsCount,
    IReadOnlyList<ExportFile> Files,
    IReadOnlyList<string> Recipients);

/// <summary>Everything the zip builder needs.</summary>
public sealed record ExportSnapshot(ExportProfile Profile, IReadOnlyList<ExportTransfer> Transfers);
