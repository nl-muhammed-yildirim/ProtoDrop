using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Runtime.CompilerServices;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.application.UseCases.Features;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain; // T-006-03: EmailSuppression entity type for admin routes
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.domain.Telemetry; // T-006-03: admin_action event name for suppression removal
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the sender write routes: draft, finalize, send and resend, moved verbatim from Program.cs in source order.
/// </summary>
internal static class TransferWriteEndpoints
{
    /// <summary>
    /// Maps the sender write routes: draft, finalize, send and resend. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapTransferWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/transfers/draft", async (CreateDraftCommand cmd, ISender sender, CancellationToken ct) =>
        {
            CreateDraftResult result;
            try
            {
                result = await sender.Send(cmd, ct);
            }
            catch (TransferSizeExceededException ex)
            {
                // The handler owns the size logic (TA-4.2a thin endpoint); 422 TRANSFER_SIZE_EXCEEDED via the closed list — GlobalErrorMiddleware already maps BusinessException → ProblemWriter bodies.
                throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
            }

            return Results.Json(new { draftId = result.DraftId.ToString("n"), expiresInSec = result.ExpiresInSec, files = result.Files.Select(f => new { fileId = f.FileId.ToString("n"), uploadUrl = f.UploadUrl.ToString() }) }, statusCode: 201); // TA-4.2#1 exact wire shape — property names pinned at the single construction site (no global JSON policy in this app).
        });

        // T-001-10 (TA-4.2#2): idempotent finalize — server-side blob copy staging→transfers, create Transfer(Draft)+FileItems+BlobRefs, EC-002-1 replay.
        endpoints.MapPost("/api/v1/transfers/draft/{draftId}/finalize", async (Guid draftId, HttpContext httpContext, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, CancellationToken.None);
            ms.Position = 0;
            using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
            var files = bodyDoc.RootElement.GetProperty("files").EnumerateArray().Select(e => new FinalizeFile
            {
                FileId = Guid.Parse(e.GetProperty("fileId").GetString()!),
                Name = e.GetProperty("name").GetString()!,
                SizeBytes = e.GetProperty("sizeBytes").GetInt64(),
                ContentType = e.TryGetProperty("contentType", out var ct) ? ct.GetString() : null,
                Retries = e.TryGetProperty("retries", out var r) ? r.GetInt32() : 0
            }).ToList();

            // T-008-02 (TA-9.1): read owner AppUserId from the session middleware's Items (single parsing site — SessionMiddleware).
            // Missing/invalid/expired → guest (item null); same wire behavior as before, no inline HMAC decode.
            Guid? ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

            try
            {
                var result = await sender.Send(new FinalizeTransferCommand(draftId, idempotencyKey, files, ownerAppUserId), ct);
                if (result is null)
                    throw new BusinessException(ProblemCodes.TransferNotFinalized, "Draft has no completed files.");
                return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, publicUrl = result.PublicUrl, status = "Draft", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, files = result.Files.Select(f => new { id = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes, contentType = f.ContentType }) }, statusCode: 201);
            }
            catch (StorageQuotaExceededException ex) when (ex is not null)
            {
                throw new BusinessException(ProblemCodes.StorageQuotaExceeded, ex.Message);
            }
            catch (ActiveTransfersExceededException ex) when (ex is not null)
            {
                // US-020-02 (FR-020-2): dedicated closed code, was VALIDATION.
                throw new BusinessException(ProblemCodes.ActiveTransfersExceeded, ex.Message);
            }
            catch (BlobSizeMismatchException ex) when (ex is not null)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message);
            }
        });

        // T-002-04 (TA-4.2#3): activate a Draft transfer — store recipients, emit transfer.created via outbox.
        endpoints.MapPost("/api/v1/transfers/{id}/send", async (Guid id, HttpContext httpContext, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            // T-008-08 (FR-008-7): owner AppUserId from the session middleware's Items — single parsing site (T-008-02). Missing/invalid/expired → guest (null).
            var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, CancellationToken.None);
            ms.Position = 0;
            using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
            var root = bodyDoc.RootElement;
            var emails = root.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : (IReadOnlyList<string>)[];
            var senderName = root.TryGetProperty("senderName", out var sn) ? sn.GetString() : null;
            var senderEmail = root.TryGetProperty("senderEmail", out var se) ? se.GetString() : null;
            var note = root.TryGetProperty("note", out var n) ? n.GetString() : null;
            var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;

            // US-022-01 (D-3): optional scheduledSendAt — ISO-8601 with offset; absent/null = send now; anything else → 400 VALIDATION.
            DateTimeOffset? scheduledSendAt = null;
            if (root.TryGetProperty("scheduledSendAt", out var sa) && sa.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                if (sa.ValueKind != System.Text.Json.JsonValueKind.String || !sa.TryGetDateTimeOffset(out var parsedSchedule))
                    throw new BusinessException(ProblemCodes.Validation, "scheduledSendAt must be an ISO 8601 date-time with an offset.");
                scheduledSendAt = parsedSchedule;
            }

            try
            {
                var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password, ownerAppUserId, scheduledSendAt?.UtcDateTime), ct);
                if (result is null)
                    throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
                return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, status = result.Status == wa.domain.TransferStatus.Scheduled ? "Scheduled" : "Active", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, fileCount = result.FileCount, recipients = result.Recipients, scheduledSendAt = result.ScheduledSendAtUtc }, statusCode: 201);
            }
            catch (wa.application.UseCases.Transfers.ScheduleForbiddenException ex)
            {
                // US-022-01 (FR-022-3): plan/gate does not allow scheduling → 403 FORBIDDEN (closed list unchanged).
                throw new BusinessException(ProblemCodes.Forbidden, ex.Message);
            }
            catch (wa.application.UseCases.Transfers.ScheduleOutOfWindowException ex)
            {
                // US-022-03 (FR-022-2): scheduledSendAt outside now+5 min .. now+14 days (server time) → 422 SCHEDULE_OUT_OF_WINDOW.
                throw new BusinessException(ProblemCodes.ScheduleOutOfWindow, ex.Message);
            }
            catch (TransferSizeExceededException ex)
            {
                // T-007-04 / AC-007-1: send-time size re-check violation — same closed-code mapping as the draft endpoint (TA-4.2a).
                throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
            }
            catch (ActiveTransfersExceededException ex)
            {
                // US-020-02 (FR-020-2): send-time active-transfers re-check (downgraded / at-cap account).
                throw new BusinessException(ProblemCodes.ActiveTransfersExceeded, ex.Message);
            }
            catch (wa.application.UseCases.Transfers.TransferValidationException vex)
            {
                throw new BusinessException(ProblemCodes.Validation, vex.Message);
            }
            catch (wa.application.UseCases.Transfers.FilesGoneException ex)
            {
                // T-010-02 (EC-010-2): a re-send draft's blobs are physically gone → 410 FILES_GONE.
                throw new BusinessException(ProblemCodes.FilesGone, ex.Message);
            }
        });

        // T-010-01 (endpoint 16): re-send — create a new Draft transfer sharing the original's blobs (no data copy, ADR-009).
        // Cookie required; no request body. Guest row → 403 FORBIDDEN (EC-010-4); foreign owner / unknown id / Status=4 → 404 NOT_FOUND.
        endpoints.MapPost("/api/v1/transfers/{id}/resend", async (Guid id, HttpContext httpContext, ISender sender, CancellationToken ct) =>
        {
            // T-008-08 (FR-008-7): owner AppUserId from the session middleware's Items — single parsing site (T-008-02). Missing/invalid/expired → guest (null).
            var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

            try
            {
                var result = await sender.Send(new wa.application.UseCases.Transfers.ResendTransferCommand(id, ownerAppUserId), ct);
                if (result is null)
                    throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
                return Results.Json(new
                {
                    id = result.TransferId.ToString("n"),
                    linkId = result.LinkId,
                    status = "Draft",
                    expiresAt = result.ExpiresAtUtc,
                    totalBytes = result.TotalBytes,
                    fileCount = result.FileCount,
                    files = result.Files.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name }),
                    recipients = result.Recipients,
                    note = result.Note,
                    senderName = result.SenderName,
                    senderEmail = result.SenderEmail,
                    passwordRequired = result.PasswordRequired
                }, statusCode: 201);
            }
            catch (wa.application.UseCases.Transfers.ResendForbiddenException ex)
            {
                // EC-010-4: a guest row (no owner) is re-send-ineligible → 403 FORBIDDEN — first usage of ProblemCodes.Forbidden.
                throw new BusinessException(ProblemCodes.Forbidden, ex.Message);
            }
            catch (wa.application.UseCases.Transfers.FilesGoneException ex)
            {
                // T-010-02 (AC-010-2): the original's blobs are physically gone → 410 FILES_GONE — zero new rows, no RefCount change.
                throw new BusinessException(ProblemCodes.FilesGone, ex.Message);
            }
        });

        return endpoints;
    }
}
