namespace wa.application.UseCases.Transfers;

/// <summary>Thrown when the account's active storage + this transfer exceeds STORAGE_QUOTA.</summary>
public sealed class StorageQuotaExceededException : Exception
{
    /// <summary>Bytes already held by the account's active transfers.</summary>
    public long CurrentBytes { get; }
    /// <summary>Size in bytes of the transfer that would be added.</summary>
    public long NewTransferBytes { get; }
    /// <summary>The plan's STORAGE_QUOTA in bytes.</summary>
    public long QuotaBytes { get; }

    /// <summary>Initializes a new instance of the <see cref="StorageQuotaExceededException"/> class.</summary>
    /// <param name="currentBytes">Bytes already held by the account's active transfers.</param>
    /// <param name="newTransferBytes">Size in bytes of the transfer that would be added.</param>
    /// <param name="quotaBytes">The plan's STORAGE_QUOTA in bytes.</param>
    internal StorageQuotaExceededException(long currentBytes, long newTransferBytes, long quotaBytes)
        : base($"Storage full — {(currentBytes + newTransferBytes) / 1073741824.0:F1} GB of {quotaBytes / 1073741824.0:F1} GB in use.")
    {
        CurrentBytes = currentBytes;
        NewTransferBytes = newTransferBytes;
        QuotaBytes = quotaBytes;
    }
}
