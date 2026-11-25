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
    public int CurrentCount { get; }
    public int MaxActiveTransfers { get; }

    internal ActiveTransfersExceededException(int currentCount, int maxActiveTransfers)
        : base($"You have {currentCount} active transfers (limit {maxActiveTransfers}). Upgrade to keep sending.")
    {
        CurrentCount = currentCount;
        MaxActiveTransfers = maxActiveTransfers;
    }
}
