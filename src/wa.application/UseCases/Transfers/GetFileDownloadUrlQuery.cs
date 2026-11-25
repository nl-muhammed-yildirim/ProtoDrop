using MediatR;
using System.Security.Cryptography;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-003-06 (TA-4.2#6): GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t= — mint a 30-minute read SAS
/// on the file blob; the browser streams directly against storage (no server-side proxying). Token = the ?t= unlock JWT
/// (T-003-04, TA-9.6#1); ClientIp/UserAgent are raw request values — transformed here via the reporter's PII slots (TA-9.4).
/// </summary>
/// <param name="LinkId">Public 8-character link id from the route.</param>
/// <param name="FileId">Id of the file to download, from the route; malformed or unknown ids answer not found.</param>
/// <param name="Token">Unlock token from the <c>t</c> query parameter; null when the transfer has no password.</param>
/// <param name="ClientIp">Raw client IP, hashed before it is stored; PII (TA-9.4).</param>
/// <param name="UserAgent">Raw User-Agent header, reduced to a family and a hash before it is stored.</param>
public sealed record GetFileDownloadUrlQuery(
    string LinkId,
    string FileId,
    string? Token = null,
    string? ClientIp = null,
    string? UserAgent = null) : IRequest<FileDownloadResult>;
