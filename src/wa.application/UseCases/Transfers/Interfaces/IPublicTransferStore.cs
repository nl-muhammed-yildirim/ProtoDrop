using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-014 (TA-4.2#4): the read surface for GET /api/v1/public/transfers/{linkId}.
/// Implemented by a thin singleton adapter over the adapter-owned WaDbContext (same style as ITransferStore).
/// </summary>
public interface IPublicTransferStore
{
    /// <summary>Return (transfer, files ordered by SortOrder) or (null, empty) when no row matches.</summary>
    /// <param name="linkId">Identifier of the link.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>A tuple holding the transfer and files values.</returns>
    Task<(Transfer? Transfer, IReadOnlyList<PublicFileItem> Files)> GetByLinkIdAsync(string linkId, CancellationToken ct);

    /// <summary>
    /// T-003-06 (TA-7.2 step 4): atomically record a single-file download against the transfer's cap.
    /// The increment + DownloadEvent insert are transactional; the status flip to 3 (DownloadLimit) happens idempotently
    /// when the cap is reached — no worker owns this transition (US-005-03 technical note).
    /// </summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="fileId">Identifier of the file.</param>
    /// <param name="ipHash">The IP hash.</param>
    /// <param name="uaHash">The ua hash.</param>
    /// <param name="userAgentFamily">The user agent family.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The single file download result.</returns>
    Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(
        Guid transferId,
        Guid fileId,
        string? ipHash,
        string? uaHash,
        string? userAgentFamily,
        CancellationToken ct);

    /// <summary>
    /// T-004-02 (TA-7.2): atomically record a zip download against the transfer's cap.
    /// Same as RecordSingleFileDownloadAsync but with fileId = null (download-all).
    /// The increment + DownloadEvent insert are transactional; the status flip to 3 (DownloadLimit) happens idempotently
    /// when the cap is reached — no worker owns this transition (US-005-03 technical note).
    /// </summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ipHash">The IP hash.</param>
    /// <param name="uaHash">The ua hash.</param>
    /// <param name="userAgentFamily">The user agent family.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The single file download result.</returns>
    Task<SingleFileDownloadResult> RecordZipDownloadAsync(
        Guid transferId,
        string? ipHash,
        string? uaHash,
        string? userAgentFamily,
        CancellationToken ct);
}
