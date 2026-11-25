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
/// <param name="TransferId">Identifier of the transfer of the finalize transfer result.</param>
/// <param name="LinkId">Identifier of the link of the finalize transfer result.</param>
/// <param name="PublicUrl">The public URL of the finalize transfer result.</param>
/// <param name="Status">The status of the finalize transfer result.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the finalize transfer result.</param>
/// <param name="TotalBytes">The total bytes of the finalize transfer result.</param>
/// <param name="Files">The files of the finalize transfer result.</param>
public sealed record FinalizeTransferResult(
    Guid TransferId,
    string LinkId,
    string PublicUrl,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    IReadOnlyList<FinalizedFile> Files);
