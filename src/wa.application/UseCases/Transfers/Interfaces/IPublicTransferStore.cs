using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-014 (TA-4.2#4): the read surface for GET /api/v1/public/transfers/{linkId}.
/// Implemented by a thin singleton adapter that opens a fresh WaDbContext per call (same style as ITransferStore).
/// </summary>
public interface IPublicTransferStore
{
    /// <summary>Return (transfer, files ordered by SortOrder) or (null, empty) when no row matches.</summary>
    /// <param name="linkId">Public link id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The transfer and its files, or a null transfer and no files when the link is unknown.</returns>
    Task<(Transfer? Transfer, IReadOnlyList<PublicFileItem> Files)> GetByLinkIdAsync(string linkId, CancellationToken ct);

    /// <summary>
    /// T-003-06 (TA-7.2 step 4): atomically record a single-file download against the transfer's cap.
    /// The increment + DownloadEvent insert are transactional; the status flip to 3 (DownloadLimit) happens idempotently
    /// when the cap is reached — no worker owns this transition (US-005-03 technical note).
    /// </summary>
    /// <param name="transferId">The transfer being downloaded.</param>
    /// <param name="fileId">The file being downloaded.</param>
    /// <param name="ipHash">HMAC hash of the client IP, or null when unavailable.</param>
    /// <param name="uaHash">HMAC hash of the User-Agent, or null when unavailable.</param>
    /// <param name="userAgentFamily">Browser family derived from the User-Agent, or null.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>Whether the download was counted or the cap had already been reached.</returns>
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
    /// <param name="transferId">The transfer being downloaded.</param>
    /// <param name="ipHash">HMAC hash of the client IP, or null when unavailable.</param>
    /// <param name="uaHash">HMAC hash of the User-Agent, or null when unavailable.</param>
    /// <param name="userAgentFamily">Browser family derived from the User-Agent, or null.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>Whether the download was counted or the cap had already been reached.</returns>
    Task<SingleFileDownloadResult> RecordZipDownloadAsync(
        Guid transferId,
        string? ipHash,
        string? uaHash,
        string? userAgentFamily,
        CancellationToken ct);
}
