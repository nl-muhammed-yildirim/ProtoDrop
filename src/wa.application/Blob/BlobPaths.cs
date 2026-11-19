namespace wa.application.Blob;

/// <summary>
/// TA-3.5 — the single source of truth for on-storage layout. Every blob path
/// in the codebase comes from this helper (no string interpolation outside it);
/// a new row is an architecture change (TA-3.5 table + ADR note).
/// </summary>
public static class BlobPaths
{
    /// <summary>Staging container name (TA-3.5).</summary>
    public const string StagingContainer = "staging";

    /// <summary>Transfers container name (TA-3.5).</summary>
    public const string TransfersContainer = "transfers";

    /// <summary>Exports container name (US-047-01, TA-3.5 data-export row).</summary>
    public const string ExportsContainer = "exports";

    /// <summary>Avatars container name (US-021-04, TA-3.5 avatars row).</summary>
    public const string AvatarsContainer = "avatars";

    /// <summary>
    /// TA-3.5 avatars row (US-021-04): the org logo of one user. Lifetime: with the user (deleted with the account); no lifecycle rule.
    /// </summary>
    public static string OrgLogo(Guid userId)
        => $"avatars/{userId:N}/logo";

    /// <summary>
    /// TA-3.5 staging row: a file staged against a draft — the browser uploads here as block blobs before finalize creates the transfer. Lifetime: 24 h lifecycle (safety net). The only path exercised at M0 (AC-055-3).
    /// </summary>
    public static string StagingFile(Guid draftId, Guid fileId)
        => $"staging/{draftId:N}/f/{fileId:N}";

    /// <summary>
    /// TA-3.5 transfer-file row: a file moved from staging into a transfer by the server-side blob copy on finalize (T-010). Lifetime: with the transfer (job).
    /// </summary>
    public static string TransferFile(Guid transferId, Guid fileId)
        => $"transfers/{transferId:N}/files/{fileId:N}";

    /// <summary>
    /// TA-3.5 transfer-zip row: the whole-transfer zip produced by the zip function (T-017). Lifetime: with the transfer (job).
    /// </summary>
    public static string TransferZip(Guid transferId)
        => $"transfers/{transferId:N}/all.zip";

    /// <summary>
    /// TA-3.5 data-export row (US-047-01): the GDPR export zip built by f-data-export. Lifetime: 7 days, purged by the same job.
    /// </summary>
    public static string DataExportZip(Guid userId, Guid exportId)
        => $"exports/{userId:N}/{exportId:N}.zip";
}
