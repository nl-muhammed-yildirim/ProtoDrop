using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft/{draftId}/finalize request (TA-4.2#2).</summary>
public sealed record FinalizeTransferCommand(Guid DraftId, string IdempotencyKey, IReadOnlyList<FinalizeFile> Files, Guid? OwnerAppUserId = null) : IRequest<FinalizeTransferResult?>;
