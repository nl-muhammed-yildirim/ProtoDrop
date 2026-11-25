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
    public long CurrentBytes { get; }
    public long NewTransferBytes { get; }
    public long QuotaBytes { get; }

    internal StorageQuotaExceededException(long currentBytes, long newTransferBytes, long quotaBytes)
        : base($"Storage full — {(currentBytes + newTransferBytes) / 1073741824.0:F1} GB of {quotaBytes / 1073741824.0:F1} GB in use.")
    {
        CurrentBytes = currentBytes;
        NewTransferBytes = newTransferBytes;
        QuotaBytes = quotaBytes;
    }
}
