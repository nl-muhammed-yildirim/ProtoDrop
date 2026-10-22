using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-014 (TA-4.2#4): the read surface for GET /api/v1/public/transfers/{linkId}.
/// Implemented by a thin singleton adapter over the adapter-owned WaDbContext (same style as ITransferStore).
/// </summary>
public interface IPublicTransferStore
{
    /// <summary>Return (transfer, files ordered by SortOrder) or (null, empty) when no row matches.</summary>
    Task<(Transfer? Transfer, IReadOnlyList<PublicFileItem> Files)> GetByLinkIdAsync(string linkId, CancellationToken ct);

    /// <summary>
    /// T-003-06 (TA-7.2 step 4): atomically record a single-file download against the transfer's cap.
    /// The increment + DownloadEvent insert are transactional; the status flip to 3 (DownloadLimit) happens idempotently
    /// when the cap is reached — no worker owns this transition (US-005-03 technical note).
    /// </summary>
    Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(
        Guid transferId,
        Guid fileId,
        string? ipHash,
        string? uaHash,
        CancellationToken ct);

    /// <summary>
    /// T-004-02 (TA-7.2): atomically record a zip download against the transfer's cap.
    /// Same as RecordSingleFileDownloadAsync but with fileId = null (download-all).
    /// The increment + DownloadEvent insert are transactional; the status flip to 3 (DownloadLimit) happens idempotently
    /// when the cap is reached — no worker owns this transition (US-005-03 technical note).
    /// </summary>
    Task<SingleFileDownloadResult> RecordZipDownloadAsync(
        Guid transferId,
        string? ipHash,
        string? uaHash,
        CancellationToken ct);
}

/// <summary>T-003-06 (TA-7.2 step 4) — the outcome of an atomic cap-checked increment.</summary>
public enum SingleFileDownloadOutcome { Recorded, CapReached }

/// <summary>Result of <see cref="IPublicTransferStore.RecordSingleFileDownloadAsync"/> — true iff the download was counted.</summary>
public sealed record SingleFileDownloadResult(SingleFileDownloadOutcome Outcome);
