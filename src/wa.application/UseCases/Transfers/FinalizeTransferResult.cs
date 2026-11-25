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

/// <summary>POST /api/v1/transfers/draft/{draftId}/finalize result (TA-4.2#2 response shape).</summary>
public sealed record FinalizeTransferResult(
    Guid TransferId,
    string LinkId,
    string PublicUrl,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    IReadOnlyList<FinalizedFile> Files);
