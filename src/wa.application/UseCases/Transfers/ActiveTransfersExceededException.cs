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

/// <summary>Thrown when the account has reached ACTIVE_TRANSFERS_MAX (finalize + send, US-020-02).</summary>
public sealed class ActiveTransfersExceededException : Exception
{
    /// <summary>Gets the current count of the active transfers exceeded exception.</summary>
    public int CurrentCount { get; }
    /// <summary>Gets the max active transfers of the active transfers exceeded exception.</summary>
    public int MaxActiveTransfers { get; }

    /// <summary>Initializes a new instance of the <see cref="ActiveTransfersExceededException"/> class.</summary>
    /// <param name="currentCount">The current count.</param>
    /// <param name="maxActiveTransfers">The max active transfers.</param>
    internal ActiveTransfersExceededException(int currentCount, int maxActiveTransfers)
        : base($"You have {currentCount} active transfers (limit {maxActiveTransfers}). Upgrade to keep sending.")
    {
        CurrentCount = currentCount;
        MaxActiveTransfers = maxActiveTransfers;
    }
}
