using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Transfers;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the session user's transfer routes: list, detail, analytics and delete, moved verbatim from Program.cs in source order.
/// </summary>
internal static class MyTransferEndpoints
{
    /// <summary>
    /// Maps the session user's transfer routes: list, detail, analytics and delete. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapMyTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's My Files history list, cursor-paged at 25 rows (TA-4.1.4).
        endpoints.MapGet("/api/v1/transfers", async (HttpContext ctx, HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            var limitParam = request.Query["limit"].FirstOrDefault();
            // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
            if (limitParam is null)
                limitParam = "25";
            if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
                throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

            string? cursor = request.Query["cursor"].FirstOrDefault();
            // US-045-01 (FR-045-1/5): a present q goes to SearchMyTransfersQuery, which owns the feature.search gate and the search cursor.
            var rawQ = request.Query["q"].FirstOrDefault();
            // TA-4.1.4: decode base64("{createdAtUtc:O}:{id:N}") early so a malformed cursor → 400 VALIDATION (existing closed-list member).
            if (rawQ is null && !string.IsNullOrEmpty(cursor))
                try
                {
                    _ = wa.domain.Transfers.CursorCodec.Decode(cursor);
                }
                catch (wa.domain.Transfers.CursorFormatException ex)
                {
                    throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
                }

            // T-009-02 (US-009-02): optional ?status=all|active|expired (case-insensitive; absent → all).
            // Resolved byte set threaded into the query — null = no status predicate (all), {1,5} = active (Active + Scheduled), {2,3} = expired.
            static IReadOnlyCollection<byte>? ResolveStatusFilter(HttpRequest request)
            {
                var raw = request.Query["status"].FirstOrDefault();
                if (raw is null) return null;
                return raw.Trim().ToLowerInvariant() switch
                {
                    "all" => null,
                    "active" => new byte[] { 1, wa.domain.TransferStatus.Scheduled }, // F-PRF-002: Scheduled (5) rows are live, so "active" shows them
                    "expired" => new byte[] { 2, 3 }, // US-009-02: Status IN (2,3) — download-limit included
                    _ => throw new BusinessException(ProblemCodes.Validation, "status must be one of all / active / expired.")
                };
            }

            ListMyTransfersResult result;
            try
            {
                // US-045-01: q present → SearchMyTransfersQuery (gate + search cursor inside); absent → the untouched plain list path.
                result = rawQ is null
                    ? await sender.Send(new ListMyTransfersQuery(userId, cursor, parsedLimit, ResolveStatusFilter(request)), ct)
                    : await sender.Send(new SearchMyTransfersQuery(userId, rawQ, cursor, parsedLimit, ResolveStatusFilter(request)), ct);
            }
            catch (wa.domain.Transfers.CursorFormatException ex)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);    // TA-4.1.4: malformed / foreign search cursor
            }
            catch (wa.domain.Transfers.SearchTermTooLongException ex)
            {
                throw new BusinessException(ProblemCodes.QTooLong, ex.Message, ex);      // US-045-01 (EC-045-3)
            }
            // TA-4.2a single-construction-site rule: 'n'-form id (signup/login style); opaque nextCursor as returned (null = last page).
            return Results.Json(new
            {
                items = result.Items.Select(r => new
                {
                    id = r.Id.ToString("n"),
                    name = r.Name,
                    fileCount = r.FileCount,
                    totalBytes = r.TotalBytes,
                    status = r.Status,
                    expiresAtUtc = r.ExpiresAtUtc,
                    downloadsCount = r.DownloadsCount,
                    maxDownloads = r.MaxDownloads,
                    recipientCount = r.RecipientCount,
                    // T-009-06 (FR-009-3): linkId — Copy/View build {origin}/t/{linkId} client-side (no per-row round-trip).
                    linkId = r.LinkId
                }),
                nextCursor = result.NextCursor
            }, statusCode: 200);
        });

        // T-009-03 (TA-4.2#15): GET /api/v1/transfers/{id} — the session user's transfer detail: row + per-file rows + recipients.
        endpoints.MapGet("/api/v1/transfers/{id}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            // T-009-03: null result = foreign owner / guest-created / deleted / unknown — ONE indistinguishable case (no FORBIDDEN).
            var result = await sender.Send(new GetMyTransferQuery(id, userId), ct);
            if (result is null)
                throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

            // TA-4.2a single-construction-site rule: T-009-01's exact row wire names (UI treats list rows and detail identically)
            // PLUS the per-file rows + recipient addresses (FR-009-3 row actions copy-link / view / re-send / delete).
            return Results.Json(new
            {
                id = result.Row.Id.ToString("n"),
                name = result.Row.Name,
                fileCount = result.Row.FileCount,
                totalBytes = result.Row.TotalBytes,
                status = result.Row.Status,
                expiresAtUtc = result.Row.ExpiresAtUtc,
                downloadsCount = result.Row.DownloadsCount,
                maxDownloads = result.Row.MaxDownloads,
                recipientCount = result.Row.RecipientCount,
                // T-009-06 (FR-009-3): linkId — Copy/View build {origin}/t/{linkId} client-side (no per-row round-trip).
                linkId = result.Row.LinkId,
                files = result.Files.Select(f => new { name = f.Name, sizeBytes = f.SizeBytes }),
                recipients = result.Recipients
            }, statusCode: 200);
        });

        // US-023-01 (TA-4.2#24, FR-023-1/5): GET /api/v1/transfers/{id}/analytics — owner, Pro+ (403), phase gate (404 FEATURE_DISABLED).
        endpoints.MapGet("/api/v1/transfers/{id}/analytics", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);
            wa.domain.Analytics.TransferAnalytics? r;
            try { r = await sender.Send(new GetTransferAnalyticsQuery(id, userId), ct); }
            catch (AnalyticsForbiddenException ex) { throw new BusinessException(ProblemCodes.Forbidden, ex.Message); }
            if (r is null) throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
            // AC-023-2: aggregates only — no IpHash / UaHash / raw IP / raw UA in the body.
            return Results.Json(new
            {
                total = r.Total,
                daily = r.Daily.Select(d => new { date = d.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), count = d.Count }),
                countries = r.Countries.Select(c => new { code = c.Key, count = c.Count }),
                browsers = r.Browsers.Select(b => new { family = b.Key, count = b.Count }),
                files = r.Files.Files.Select(f => new { fileId = f.FileId, name = f.Name, count = f.Count }),
                downloadAll = r.Files.DownloadAll,
                topFiles = r.Files.Top.Select(f => new { fileId = f.FileId, name = f.Name, count = f.Count })
            }, statusCode: 200);
        }).RequireFeature(FeatureGates.Analytics);

        // T-009-04 (TA-4.2#17 / FR-009-3): DELETE /api/v1/transfers/{id} — owner delete, soft-delete + refcount path.
        endpoints.MapDelete("/api/v1/transfers/{id}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            // T-009-04: null result = foreign owner / guest-created / already-deleted / unknown — ONE indistinguishable case (EC-009-2).
            var result = await sender.Send(new DeleteTransferCommand(id, userId, "user"), ct);
            if (result is null)
                throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

            // FR-009-3: 204 No Content — no body (the client treats 404 on a second delete as success per EC-009-2).
            return Results.NoContent();
        });

        return endpoints;
    }
}
