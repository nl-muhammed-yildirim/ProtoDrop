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
/// <param name="DraftId">Id of the draft whose staged files are finalized, from the route.</param>
/// <param name="IdempotencyKey">Client key that makes a repeated finalize return the first result.</param>
/// <param name="Files">The staged files with their declared sizes.</param>
/// <param name="OwnerAppUserId">The signed-in owner; null for guests, who skip account-level limits.</param>
public sealed record FinalizeTransferCommand(Guid DraftId, string IdempotencyKey, IReadOnlyList<FinalizeFile> Files, Guid? OwnerAppUserId = null) : IRequest<FinalizeTransferResult?>;
