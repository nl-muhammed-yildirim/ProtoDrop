using MediatR;
using System.Runtime.CompilerServices;
using wa.application.UseCases.Transfers;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the anonymous recipient routes: transfer metadata, unlock, file download URL and download-all URL, moved verbatim from Program.cs in source order.
/// </summary>
internal static class PublicTransferEndpoints
{
    /// <summary>
    /// Maps the anonymous recipient routes: transfer metadata, unlock, file download URL and download-all URL. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapPublicTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // T-014 (TA-4.2#4): recipient metadata for GET /api/v1/public/transfers/{linkId} — no auth, no cookie; page views never mint SAS or consume a download.
        endpoints.MapGet("/api/v1/public/transfers/{linkId}", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            // T-003-04 (TA-9.6#1): the ?t= unlock token is read here and passed to the handler — a valid token flips passwordRequired to false.
            var result = await sender.Send(new GetPublicTransferQuery(linkId, request.Query["t"].FirstOrDefault()), ct);
            return result.State switch
            {
                PublicTransferState.Active => Results.Json(new
                {
                    from = result.From,
                    note = result.Note,
                    files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
                    hasDownloadAll = result.HasDownloadAll,
                    downloadsLeft = result.DownloadsLeft,
                    passwordRequired = result.PasswordRequired,
                    orgName = result.OrgName,
                    orgLogoUrl = result.OrgLogoUrl,
                    scheduledSendAt = result.ScheduledSendAtUtc is { } due ? EndpointFormatting.AsUtc(due) : (DateTime?)null,
                    adsEnabled = result.AdsEnabled
                }), // TA-4.2#4 exact wire shape — property names pinned at the single construction site (same style as endpoints 1–3)

                // D8: status pin = 410 for both terminal expired shapes; the unknown case overrides to 404 below (FR-003-9: same body, different status line only).
                PublicTransferState.Expired => Results.Json(ProblemWriter.Build(
                    ProblemCodes.StatusOf(ProblemCodes.TransferExpired), ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),

                PublicTransferState.DownloadLimit => Results.Json(
                    ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410), // 409→410 explicit override (task doc status mapping)

                PublicTransferState.NotFound => Results.Json(
                    ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404), // FR-003-9: same body, different status line only

                _ => throw new SwitchExpressionException(result.State)
            };
        });

        // T-003-04 (TA-4.2#5): unlock a gated transfer — PBKDF2-SHA256 verify (same hasher + user-id keying as send time) → 7-day HS256 JWT.
        // No Set-Cookie, no extra properties (T-003-05: the token lives client-side in sessionStorage); endpoint 4's ?t= accepts it.
        endpoints.MapPost("/api/v1/public/transfers/{linkId}/unlock", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            // Same JsonDocument style as the send endpoint: empty body → password stays null → the handler throws the VALIDATION application-level exception (D1).
            string? password = null;
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, CancellationToken.None);
            if (ms.Length > 0)
            {
                ms.Position = 0;
                using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
                password = bodyDoc.RootElement.TryGetProperty("password", out var pw) ? pw.GetString() : null;
            }

            try
            {
                var result = await sender.Send(new UnlockTransferCommand(linkId, password), ct);
                return result.State switch
                {
                    // TA-4.2#5 exact wire shape — { token } only.
                    UnlockState.Active => Results.Json(new { token = result.Token }),
                    // D2.6: the auth failure is a result-state (not an exception) — Program.cs owns the 401 body; title pinned by the closed table.
                    UnlockState.WrongPassword => Results.Json(ProblemWriter.Build(401, ProblemCodes.WrongPassword, ProblemCodes.TitleOf(ProblemCodes.WrongPassword)), statusCode: 401),
                    // FR-003-9 single-body invariant — byte-equal to endpoint 4's unknown-linkId case (status line is the only diff).
                    UnlockState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
                    // D6: status-before-password — terminal rows map to their canonical bodies regardless of credentials.
                    UnlockState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
                    UnlockState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410),
                    _ => throw new SwitchExpressionException(result.State)
                };
            }
            catch (wa.application.UseCases.Transfers.UnlockPasswordMissingException vex)
            {
                // D1: application-level exception → VALIDATION with errors[] — the only code with per-field details; BusinessException stays in wa.api.Middleware.
                throw new BusinessException(ProblemCodes.Validation, vex.Message) { Errors = [new ProblemFieldError("password", vex.Message)] };
            }
        });

        // T-003-06 (TA-4.2#6): mint a 30-minute read SAS on one transfer file; the browser streams directly from storage.
        // Terminal states map to endpoint 4's canonical Problem+JSON bodies (FR-003-9 byte identity); ?t= unlock JWT accepted.
        endpoints.MapGet("/api/v1/public/transfers/{linkId}/files/{fileId}/download-url", async (string linkId, string fileId, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFileDownloadUrlQuery(
                linkId,
                fileId,
                Token: request.Query["t"].FirstOrDefault(),
                ClientIp: request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: request.Headers.UserAgent.ToString()), ct);
            return result.State switch
            {
                // TA-4.2#6 exact wire shape — exactly { url, expiresAt } (02-feature-plan §F-TRF-003 pinned); nothing else.
                FileDownloadState.Ok => Results.Json(new { url = result.Url!.ToString(), expiresAt = result.ExpiresAt!.Value }),

                // D6: terminal-before-auth — canonical bodies, same as endpoint 4 (FR-003-9).
                FileDownloadState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
                FileDownloadState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
                FileDownloadState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410),

                // TA-9.6#1: gated transfer minted without a valid ?t= → 401 UNAUTHENTICATED (never a file-list-shaped error).
                FileDownloadState.Unauthenticated => Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401),

                _ => throw new SwitchExpressionException(result.State)
            };
        });

        // T-004-02 (TA-4.2#7): mint a 30-minute read SAS on the whole-transfer zip if it exists, otherwise trigger f-zip generation
        // (fire-and-forget) and return 202 with pollAfterSec. Terminal states map to endpoint 4's canonical Problem+JSON bodies (FR-003-9 byte identity); ?t= unlock JWT accepted.
        endpoints.MapGet("/api/v1/public/transfers/{linkId}/download-all-url", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDownloadAllUrlQuery(
                linkId,
                Token: request.Query["t"].FirstOrDefault(),
                ClientIp: request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: request.Headers.UserAgent.ToString()), ct);
            return result.State switch
            {
                // TA-4.2#7 exact wire shape — exactly { url } when the zip is ready (200).
                DownloadAllState.Ok => Results.Json(new { url = result.Url!.ToString() }),

                // TA-4.2#7 exact wire shape — exactly { pollAfterSec } when the zip is being generated (202).
                DownloadAllState.Generating => Results.Json(new { pollAfterSec = result.PollAfterSec!.Value }, statusCode: 202),

                // D6: terminal-before-auth — canonical bodies, same as endpoint 4 (FR-003-9).
                DownloadAllState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
                DownloadAllState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
                DownloadAllState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410),

                // TA-9.6#1: gated transfer minted without a valid ?t= → 401 UNAUTHENTICATED (never a file-list-shaped error).
                DownloadAllState.Unauthenticated => Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401),

                // T-013-03 (FR-013-3): blob account down at mint time — 503 + code INTERNAL (no new closed-list code; TA-4.1.3's INTERNAL row).
                DownloadAllState.StorageUnavailable => Results.Json(ProblemWriter.Build(503, ProblemCodes.Internal, "A required component is unreachable."), statusCode: 503),

                _ => throw new SwitchExpressionException(result.State)
            };
        });

        return endpoints;
    }

    // FR-003-8/9 details text — the single string slot in the TA-4.1.3 Problem+JSON closed shape (the writer's signature has no per-field override).
    private static string ExpiredDetails(string? senderEmail) =>
        senderEmail is null
            ? "This transfer has expired. Contact the sender for a new link."   // US-003-05 alternative flow: no fabricated address
            : $"This transfer has expired. Contact {senderEmail} for a new link.";

    // AC-003-5 / F-TRF-005-4 details text — sender email when provided; plain message when null (no fabricated address, same style as ExpiredDetails).
    private static string DownloadLimitDetails(string? senderEmail) =>
        senderEmail is null
            ? "All downloads have been used."
            : $"All downloads have been used. Contact {senderEmail} for a new link.";
}
