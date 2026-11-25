using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/send result (TA-4.2#3 response shape).</summary>
/// <param name="TransferId">Identifier of the transfer of the send transfer result.</param>
/// <param name="LinkId">Identifier of the link of the send transfer result.</param>
/// <param name="Status">The status of the send transfer result.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the send transfer result.</param>
/// <param name="TotalBytes">The total bytes of the send transfer result.</param>
/// <param name="FileCount">The file count of the send transfer result.</param>
/// <param name="Recipients">The recipients of the send transfer result.</param>
/// <param name="ScheduledSendAtUtc">The scheduled send at UTC of the send transfer result.</param>
public sealed record SendTransferResult(
    Guid TransferId,
    string LinkId,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> Recipients,
    DateTime? ScheduledSendAtUtc = null);
