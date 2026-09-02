namespace wa.domain;

/// <summary>
/// TA-3.5 - blob path helpers, the single source of truth for on-storage
/// layout (<c>staging/{draftId}/f/{fileId}</c>,
/// <c>transfers/{transferId}/files/{fileId}</c>,
/// <c>transfers/{transferId}/all.zip</c>).
/// </summary>
/// <remarks>
/// <c>draftId</c> is the upload-draft identifier (the draft the browser
/// uploads block blobs under before finalize creates the transfer). All
/// ids are the domain's <see cref="Guid"/>-shaped ids, lower-invariant —
/// matching how EF stores <c>CHAR(36)</c> ids.
/// </remarks>
public static class BlobPaths
{
    /// <summary>
    /// TA-3.5 staging path: a file staged against a draft — the browser
    /// uploads here before the draft is finalized into a transfer.
    /// Lifetime: 24 h lifecycle (safety net), browser-written block blob.
    /// </summary>
    public static string Staging(string draftId, string fileId)
        => $"staging/{draftId}/f/{fileId}";

    /// <summary>
    /// TA-3.5 transfer file path: a file moved from staging into a transfer
    /// by the server-side blob copy on finalize. Lifetime: with the transfer
    /// (job).
    /// </summary>
    public static string TransferFile(string transferId, string fileId)
        => $"transfers/{transferId}/files/{fileId}";

    /// <summary>
    /// TA-3.5 transfer zip path: the whole-transfer zip produced by the zip
    /// function. Lifetime: with the transfer (job).
    /// </summary>
    public static string TransferZip(string transferId)
        => $"transfers/{transferId}/all.zip";
}
