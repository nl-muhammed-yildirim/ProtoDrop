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
    /// <summary>Number of active transfers the account already has.</summary>
    public int CurrentCount { get; }
    /// <summary>The plan's ACTIVE_TRANSFERS_MAX that was reached.</summary>
    public int MaxActiveTransfers { get; }

    /// <summary>Initializes a new instance of the <see cref="ActiveTransfersExceededException"/> class.</summary>
    /// <param name="currentCount">Number of active transfers the account already has.</param>
    /// <param name="maxActiveTransfers">The plan's ACTIVE_TRANSFERS_MAX that was reached.</param>
    internal ActiveTransfersExceededException(int currentCount, int maxActiveTransfers)
        : base($"You have {currentCount} active transfers (limit {maxActiveTransfers}). Upgrade to keep sending.")
    {
        CurrentCount = currentCount;
        MaxActiveTransfers = maxActiveTransfers;
    }
}
