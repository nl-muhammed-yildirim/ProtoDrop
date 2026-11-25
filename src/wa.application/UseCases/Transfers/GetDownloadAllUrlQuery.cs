using MediatR;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Correlation;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-004-02 (TA-4.2#7): GET /api/v1/public/transfers/{linkId}/download-all-url?t= — mint a 30-minute read SAS on the
/// whole-transfer zip if it exists, otherwise trigger f-zip generation (fire-and-forget) and return 202 with pollAfterSec.
/// Token = the ?t= unlock JWT (T-003-04, TA-9.6#1); ClientIp/UserAgent are raw request values — transformed here via the
/// reporter's PII slots (TA-9.4).
/// </summary>
/// <param name="LinkId">Public 8-character link id from the route.</param>
/// <param name="Token">Unlock token from the <c>t</c> query parameter; null when the transfer has no password.</param>
/// <param name="ClientIp">Raw client IP, hashed before it is stored; PII (TA-9.4).</param>
/// <param name="UserAgent">Raw User-Agent header, reduced to a family and a hash before it is stored.</param>
public sealed record GetDownloadAllUrlQuery(
    string LinkId,
    string? Token = null,
    string? ClientIp = null,
    string? UserAgent = null) : IRequest<DownloadAllResult>;
