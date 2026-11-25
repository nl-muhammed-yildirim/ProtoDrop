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
/// <param name="DraftId">Identifier of the draft of the finalize transfer command.</param>
/// <param name="IdempotencyKey">The idempotency key of the finalize transfer command.</param>
/// <param name="Files">The files of the finalize transfer command.</param>
/// <param name="OwnerAppUserId">Identifier of the owner app user of the finalize transfer command.</param>
public sealed record FinalizeTransferCommand(Guid DraftId, string IdempotencyKey, IReadOnlyList<FinalizeFile> Files, Guid? OwnerAppUserId = null) : IRequest<FinalizeTransferResult?>;
