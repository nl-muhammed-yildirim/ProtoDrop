using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Thrown when the account's active storage + this transfer exceeds STORAGE_QUOTA.</summary>
public sealed class StorageQuotaExceededException : Exception
{
    /// <summary>Gets the current bytes of the storage quota exceeded exception.</summary>
    public long CurrentBytes { get; }
    /// <summary>Gets the new transfer bytes of the storage quota exceeded exception.</summary>
    public long NewTransferBytes { get; }
    /// <summary>Gets the quota bytes of the storage quota exceeded exception.</summary>
    public long QuotaBytes { get; }

    /// <summary>Initializes a new instance of the <see cref="StorageQuotaExceededException"/> class.</summary>
    /// <param name="currentBytes">The current bytes.</param>
    /// <param name="newTransferBytes">The new transfer bytes.</param>
    /// <param name="quotaBytes">The quota bytes.</param>
    internal StorageQuotaExceededException(long currentBytes, long newTransferBytes, long quotaBytes)
        : base($"Storage full — {(currentBytes + newTransferBytes) / 1073741824.0:F1} GB of {quotaBytes / 1073741824.0:F1} GB in use.")
    {
        CurrentBytes = currentBytes;
        NewTransferBytes = newTransferBytes;
        QuotaBytes = quotaBytes;
    }
}
