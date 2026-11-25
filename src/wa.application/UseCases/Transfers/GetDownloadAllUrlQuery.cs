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
/// <param name="LinkId">Identifier of the link of the get download all URL query.</param>
/// <param name="Token">The token of the get download all URL query.</param>
/// <param name="ClientIp">The client IP of the get download all URL query.</param>
/// <param name="UserAgent">The user agent of the get download all URL query.</param>
public sealed record GetDownloadAllUrlQuery(
    string LinkId,
    string? Token = null,
    string? ClientIp = null,
    string? UserAgent = null) : IRequest<DownloadAllResult>;
