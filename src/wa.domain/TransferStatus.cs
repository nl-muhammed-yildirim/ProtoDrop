namespace wa.domain;

/// <summary>
/// Closed set of <see cref="Transfer.Status"/> values (TA-3.2). Byte constants (not an enum) so they compare with the
/// raw <c>byte</c> column property without casts. F-PRF-002 adds <see cref="Scheduled"/>.
/// </summary>
public static class TransferStatus
{
    /// <summary>The draft constant; value <c>0</c>.</summary>
    public const byte Draft = 0;
    /// <summary>The active constant; value <c>1</c>.</summary>
    public const byte Active = 1;
    /// <summary>The expired constant; value <c>2</c>.</summary>
    public const byte Expired = 2;
    /// <summary>The download limit constant; value <c>3</c>.</summary>
    public const byte DownloadLimit = 3;
    /// <summary>The deleted constant; value <c>4</c>.</summary>
    public const byte Deleted = 4;
    /// <summary>F-PRF-002 (FR-022-4): created with a future send time — the link is live now, emails go out at the due time.</summary>
    public const byte Scheduled = 5;
}
