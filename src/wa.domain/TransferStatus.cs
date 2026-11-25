namespace wa.domain;

/// <summary>
/// Closed set of <see cref="Transfer.Status"/> values (TA-3.2). Byte constants (not an enum) so they compare with the
/// raw <c>byte</c> column property without casts. F-PRF-002 adds <see cref="Scheduled"/>.
/// </summary>
public static class TransferStatus
{
    /// <summary>Created but not yet finalized; files may still be uploaded (stored as 0).</summary>
    public const byte Draft = 0;
    /// <summary>Finalized and downloadable (stored as 1).</summary>
    public const byte Active = 1;
    /// <summary>Past its expiry time; no longer downloadable (stored as 2).</summary>
    public const byte Expired = 2;
    /// <summary>Closed because the maximum number of downloads was reached (stored as 3).</summary>
    public const byte DownloadLimit = 3;
    /// <summary>Deleted by the owner or by cleanup (stored as 4).</summary>
    public const byte Deleted = 4;
    /// <summary>F-PRF-002 (FR-022-4): created with a future send time — the link is live now, emails go out at the due time.</summary>
    public const byte Scheduled = 5;
}
