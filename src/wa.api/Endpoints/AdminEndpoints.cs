using MediatR;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.domain.Telemetry; // T-006-03: admin_action event name for suppression removal
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the admin routes (suppressions, audit, overview, transfers, users, erasures, flags), moved verbatim from Program.cs in source order.
/// </summary>
internal static class AdminEndpoints
{
    /// <summary>
    /// Maps the admin routes (suppressions, audit, overview, transfers, users, erasures, flags). Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="adminGate">Role gate shared with the auth endpoints.</param>
    /// <param name="configuration">Configuration source read per request by the overview route.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints, AdminGate adminGate, IConfiguration configuration)
    {
        // T-011-09 (TA-4.2#24 / FR-011-2): admin suppression list — thin endpoint over MediatR (was inline EF from T-006-03).
        endpoints.MapGet("/api/v1/admin/suppressions", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles — every GET of 20–23 passes Operator + SuperAdmin (AC-011-3 server half).

            var rows = await sender.Send(new GetSuppressionsQuery(), ct);
            return Results.Json(rows.Select(s => new { id = s.Id.ToString("n"), address = s.Address, senderEmail = s.SenderEmail, createdAtUtc = s.CreatedAtUtc })); // bare array — UnsubscribeEndpointTests L145 pins the wire shape
        });

        // T-011-09 (TA-4.2#24 / FR-011-5): admin suppression remove — thin endpoint over MediatR; audits every removal + emits admin_action telemetry.
        endpoints.MapDelete("/api/v1/admin/suppressions/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, wa.application.Telemetry.WaTelemetryReporter telemetry, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: true, ct); // T-011-01: suppression remove is in the exact SuperAdmin-only write set (AC-011-3 server half).

            // T-011-09: null result = unknown id — ONE case (endpoint maps to 404 NOT_FOUND via BusinessException; mirrors T-011-05/07).
            var result = await sender.Send(new RemoveSuppressionCommand(id), ct);
            if (result is null)
                throw new BusinessException(ProblemCodes.NotFound, "Not found.");

            telemetry.LogEvent(TelemetryEvents.AdminAction); // F-TRF-011: admin removal is auditable via App Insights (T-006-03 FR-006-5 surface — kept)

            // FR-011-5: audit only on successful removal — re-read actor email via IAppUserStore (the gate returns void).
            // The removed row's address comes from the handler RESULT (captured PRE-delete) — the row is gone and unreadable now.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
            {
                var user = await users.GetByIdAsync(userId, ct);
                if (user?.Email is not null)
                    await auditWriter.WriteAsync(user.Email, "suppression.removed", "emailSuppression", id.ToString("n"), new { email = result.Address }, ct);
            }

            // KEEP 200 OK — UnsubscribeEndpointTests L180 asserts OK (NOT NoContent like T-011-05/07).
            return Results.Ok();
        });

        // T-011-09 (TA-4.2#25 / US-011-04 happy path 5): admin recent actions — last 50 rows, newest first; both roles read.
        endpoints.MapGet("/api/v1/admin/audit-recent", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L459.

            var rows = await sender.Send(new GetRecentAuditQuery(), ct);
            return Results.Json(rows.Select(a => new { id = a.Id, actorEmail = a.ActorEmail, action = a.Action, entityType = a.EntityType, entityId = a.EntityId, detailsJson = a.DetailsJson, createdAtUtc = a.CreatedAtUtc })); // bare array, camelCase — new route (no existing wire shape)
        });

        // T-011-03 (TA-4.2#20 / FR-011-6): admin overview — four aggregates + recent transfers list, cached 5 min in-memory.
        endpoints.MapGet("/api/v1/admin/overview", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L450.

            // T-013-05 (AC-013-3): DLQ count is broker-side — ServiceBusAdministrationClient from Wa:ServiceBus:ConnectionString (same key as SbEventPublisher/SbHealthProbe).
            // Defensive: a dummy/unreachable connection (dev/test) must never 5xx the overview — FR-013-4 in spirit.
            long dlqCount = 0;
            var sbConnString = configuration["Wa:ServiceBus:ConnectionString"];
            if (!string.IsNullOrEmpty(sbConnString))
            {
                try
                {
                    var adminClient = new Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient(sbConnString);
                    var response = await adminClient.GetSubscriptionRuntimePropertiesAsync("core", "email", CancellationToken.None);
                    dlqCount = response.Value.DeadLetterMessageCount;
                }
                catch (Exception)
                {
                    // Broker unreachable or subscription missing — leave 0; the overview must not be blocked by SB being down.
                }
            }

            var result = await sender.Send(new wa.application.UseCases.Admin.GetOverviewQuery(), ct);
            return Results.Json(new
            {
                activeTransfers = result.ActiveTransfers,
                storageBytesUsed = result.StorageBytesUsed,
                newUsersToday = result.NewUsersToday,
                emailsSent7d = result.EmailsSent7d,
                emailsFailed7d = result.EmailsFailed7d,
                emailPendingCount = result.EmailPendingCount,
                emailFailedTotal = result.EmailFailedTotal,
                lastSuccessfulDeliveryAge = result.LastSuccessfulDeliveryAge,
                dlqCount,
                snapshotAtUtc = result.SnapshotAtUtc,
                recentTransfers = result.RecentTransfers.Select(r => new { linkId = r.LinkId, ownerEmail = r.OwnerEmail, status = r.Status, totalBytes = r.TotalBytes, createdAtUtc = r.CreatedAtUtc })
            });
        });

        // T-011-04 (TA-4.2#21 / FR-011-3): admin transfer search — GET /api/v1/admin/transfers?q=&status=&cursor=&minBytes=&maxBytes=&from=&to=&limit=.
        // Cursor-paged at 25 rows (TA-4.1.4); thin endpoint gated by T-011-01's RequireAdminAsync (superadminOnly:false — both roles read).
        endpoints.MapGet("/api/v1/admin/transfers", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L475.

            // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
            var limitParam = request.Query["limit"].FirstOrDefault();
            if (limitParam is null)
                limitParam = "25";
            if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
                throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

            // T-011-04: optional filters — q/status/minBytes/maxBytes/from/to (all optional; absent → no predicate).
            var q = request.Query["q"].FirstOrDefault();
            var status = request.Query["status"].FirstOrDefault();
            long? minBytes = ParseLongParam(request, "minBytes");
            long? maxBytes = ParseLongParam(request, "maxBytes");
            DateTime? fromUtc = ParseDateTimeParam(request, "from");
            DateTime? toUtc = ParseDateTimeParam(request, "to");

            string? cursor = request.Query["cursor"].FirstOrDefault();

            try
            {
                var result = await sender.Send(new wa.application.UseCases.Admin.SearchTransfersQuery(
                    q, status, minBytes, maxBytes, fromUtc, toUtc, cursor, parsedLimit), ct);
                return Results.Json(new
                {
                    items = result.Items.Select(r => new
                    {
                        id = r.Id,
                        linkId = r.LinkId,
                        ownerEmail = r.OwnerEmail,
                        status = r.Status,
                        fileCount = r.FileCount,
                        totalBytes = r.TotalBytes,
                        createdAtUtc = r.CreatedAtUtc,
                        downloadsCount = r.DownloadsCount
                    }),
                    nextCursor = result.NextCursor
                });
            }
            catch (wa.domain.Transfers.CursorFormatException ex)
            {
                // TA-4.1.4: malformed cursor → 400 VALIDATION (existing closed-list member) — same as Program.cs L715-723.
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
            catch (wa.application.UseCases.Admin.SearchTransfersValidationException ex)
            {
                // T-011-04: handler validation (status vocabulary / range pairing) → 400 VALIDATION (existing closed-list member).
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
            catch (wa.domain.Transfers.SearchTermTooLongException ex)
            {
                // US-045-01 (EC-045-3): flag-on admin q over 100 chars → 400 Q_TOO_LONG.
                throw new BusinessException(ProblemCodes.QTooLong, ex.Message, ex);
            }
        });

        // T-011-06 (TA-4.2#22 / FR-011-2): admin users list — GET /api/v1/admin/users?q=&cursor=&limit=.
        // Cursor-paged at 25 rows (TA-4.1.4); thin endpoint gated by T-011-01's RequireAdminAsync (superadminOnly:false — both roles read).
        endpoints.MapGet("/api/v1/admin/users", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L498.

            // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
            var limitParam = request.Query["limit"].FirstOrDefault();
            if (limitParam is null)
                limitParam = "25";
            if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
                throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

            // T-011-06: optional filters — q (email substring) + cursor (all optional; absent → no predicate).
            var q = request.Query["q"].FirstOrDefault();
            string? cursor = request.Query["cursor"].FirstOrDefault();

            try
            {
                var result = await sender.Send(new wa.application.UseCases.Admin.GetUsersQuery(q, cursor, parsedLimit), ct);
                return Results.Json(new
                {
                    items = result.Items.Select(r => new
                    {
                        id = r.Id,
                        email = r.Email,
                        planName = r.PlanName,
                        storageBytesUsed = r.StorageBytesUsed,
                        activeTransferCount = r.ActiveTransferCount,
                        createdAtUtc = r.CreatedAtUtc
                    }),
                    nextCursor = result.NextCursor
                });
            }
            catch (wa.domain.Transfers.CursorFormatException ex)
            {
                // TA-4.1.4: malformed cursor → 400 VALIDATION (existing closed-list member) — same as Program.cs L538-542.
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
        });

        // US-047-03 (FR-047-6, TA-4.1.4): erasure report — both roles read; email revealed to SuperAdmin (DPO) only, masked for Operator (TA-9.4).
        endpoints.MapGet("/api/v1/admin/erasures", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            var role = await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct);
            var pageSize = wa.application.UseCases.Admin.ErasureReportPolicy.PageSize;
            var limitParam = request.Query["limit"].FirstOrDefault() ?? pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!int.TryParse(limitParam, out var limit) || limit <= 0 || limit > pageSize)
                throw new BusinessException(ProblemCodes.Validation, $"limit must be a positive integer at most {pageSize}.");
            try
            {
                var result = await sender.Send(new wa.application.UseCases.Admin.GetErasuresQuery(
                    request.Query["cursor"].FirstOrDefault(), limit, RevealEmail: role == AdminRole.SuperAdmin), ct);
                return Results.Json(new
                {
                    items = result.Items.Select(r => new
                    {
                        userId = r.UserId.ToString("n"),
                        email = r.Email,
                        deletedAtUtc = EndpointFormatting.AsUtc(r.DeletedAtUtc),
                        reHomedTransferCount = r.ReHomedTransferCount,
                        sweepStatus = r.SweepStatus,
                        sweptAtUtc = r.SweptAtUtc is { } swept ? EndpointFormatting.AsUtc(swept) : (DateTime?)null
                    }),
                    nextCursor = result.NextCursor
                });
            }
            catch (wa.domain.Transfers.CursorFormatException ex)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
        });

        // T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — DELETE /api/v1/admin/transfers/{id} — SuperAdmin-only write.
        // Reuses T-009-04's delete semantics verbatim with reason "admin" — row marked Deleted (soft, Status=4), transfer.deleted
        // emitted once { transferId, reason:"admin" }, RefCount decremented per BlobRef, sole-ref blob gets PhysicallyDeletedAtUtc = now+24h.
        endpoints.MapDelete("/api/v1/admin/transfers/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
        {
            // T-011-01 gate: SuperAdmin-only write (TA-4.2#21). The gate resolves the role internally and returns nothing —
            // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: true, ct);

            // T-011-05: null result = already-deleted / unknown — ONE indistinguishable case (EC-009-2 idempotency reused).
            var result = await sender.Send(new AdminDeleteTransferCommand(id), ct);
            if (result is null)
                throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

            // FR-011-5: audit only on successful delete — re-read actor email via IAppUserStore (the gate returns void).
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
            {
                var user = await users.GetByIdAsync(userId, ct);
                if (user?.Email is not null)
                    await auditWriter.WriteAsync(user.Email, "transfer.force_delete", "transfer", id.ToString("n"), new { reason = "admin", transferId = id }, ct);
            }

            // FR-011-4: 204 No Content — no body (same as endpoint 17 at Program.cs L883).
            return Results.NoContent();
        });

        // T-011-07 (TA-4.2#22 / FR-011-2): set a user's plan — PATCH /api/v1/admin/users/{id}?planId= — SuperAdmin-only write.
        // Limits take effect at the NEXT finalize (FR-BIL-001-4 "applies to new transfers") — no limit recompute here.
        endpoints.MapPatch("/api/v1/admin/users/{id:guid}", async (Guid id, HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
        {
            // T-011-01 gate: SuperAdmin-only write (TA-4.2#22). The gate resolves the role internally and returns nothing —
            // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: true, ct);

            // T-011-07: planId from the query string — bad format → 400 VALIDATION (existing closed-list member).
            var planIdParam = request.Query["planId"].FirstOrDefault();
            if (!Guid.TryParse(planIdParam, out var planId))
                throw new BusinessException(ProblemCodes.Validation, "planId must be a GUID.");

            try
            {
                var result = await sender.Send(new SetUserPlanCommand(id, planId), ct);

                // FR-011-5: audit only on successful write — re-read actor email via IAppUserStore (the gate returns void).
                if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
                {
                    var user = await users.GetByIdAsync(userId, ct);
                    if (user?.Email is not null)
                        await auditWriter.WriteAsync(user.Email, "user.plan_changed", "user", id.ToString("n"), new { oldPlanId = result.OldPlanId, newPlanId = result.NewPlanId }, ct);
                }

                // FR-011-2: 204 No Content — no body (same as endpoint 21 at Program.cs L616).
                return Results.NoContent();
            }
            catch (SetUserPlanException ex)
            {
                // T-011-07 (Decision 2): unknown user OR unknown planId → 404 NOT_FOUND (existing closed-list member).
                throw new BusinessException(ProblemCodes.NotFound, ex.Message, ex);
            }
        });

        // T-011-07 (TA-4.2#22 / FR-011-2): force-delete a user — DELETE /api/v1/admin/users/{id} — SuperAdmin-only write.
        // Reuses T-008's GDPR soft-delete + re-home verbatim (reason "admin" is the admin context, not a column).
        endpoints.MapDelete("/api/v1/admin/users/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
        {
            // T-011-01 gate: SuperAdmin-only write (TA-4.2#22). The gate resolves the role internally and returns nothing —
            // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: true, ct);

            // T-011-07: null result = unknown user OR already soft-deleted — ONE indistinguishable case (EC-009-2 idempotency reused).
            var result = await sender.Send(new AdminForceDeleteUserCommand(id), ct);
            if (result is null)
                throw new BusinessException(ProblemCodes.NotFound, "User not found.");

            // FR-011-5: audit only on successful delete — re-read actor email via IAppUserStore (the gate returns void).
            // The target user's email comes from the result (captured PRE-delete) — the row is soft-deleted and unreadable now.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
            {
                var user = await users.GetByIdAsync(userId, ct);
                if (user?.Email is not null)
                    await auditWriter.WriteAsync(user.Email, "user.force_delete", "user", id.ToString("n"), new { email = result.Email }, ct);
            }

            // FR-011-2: 204 No Content — no body (same as endpoint 21 at Program.cs L616).
            return Results.NoContent();
        });

        // T-011-08 (TA-4.2#23 / FR-011-2): admin flags list — GET /api/v1/admin/flags — both roles read.
        // 52 fixed seeded rows — no paging; bare JSON array of {key,value}, camelCase, ordered by key ascending.
        endpoints.MapGet("/api/v1/admin/flags", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
        {
            // T-011-01 gate: both roles read — same as the GET at L556.
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: false, ct);

            var rows = await sender.Send(new GetFlagsQuery(), ct);
            // US-044-01: effective-value view — type, plan default and overridden marker drive the Flags screen Limits group.
            return Results.Json(rows.Select(f => new { key = f.Key, value = f.Value, updatedAt = f.UpdatedAtUtc, type = f.Type, planDefault = f.PlanDefault, overridden = f.Overridden, description = f.Description })); // reads write NO audit row
        });

        // T-011-08 (TA-4.2#23 / FR-011-2): set a flag's value — PUT /api/v1/admin/flags/{key} — SuperAdmin-only write.
        // The value takes effect within 30 s via the EXISTING FlagsCache TTL (TA-3.4) — no invalidation code here.
        endpoints.MapPut("/api/v1/admin/flags/{key}", async (string key, HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
        {
            // T-011-01 gate: SuperAdmin-only write (TA-4.2#23). The gate resolves the role internally and returns nothing —
            // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
            await adminGate.RequireAdminAsync(ctx, users, superadminOnly: true, ct);

            // T-011-08: the raw body string is the new value — passed through to the handler (TA-4.2a thin endpoint).
            var body = await new StreamReader(request.Body).ReadToEndAsync(ct);

            try
            {
                var result = await sender.Send(new SetFlagCommand(key, body), ct);

                // FR-011-5: audit only on successful save — re-read actor email via IAppUserStore (the gate returns void).
                if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
                {
                    var user = await users.GetByIdAsync(userId, ct);
                    if (user?.Email is not null)
                        await auditWriter.WriteAsync(user.Email, "flag.set", "flag", key, new { oldValue = result.OldValue, newValue = result.NewValue }, ct);
                }

                // US-044-01 (FR-044-5): 200 with the new value ({key,value}).
                return Results.Json(new { key = result.Key, value = result.NewValue });
            }
            catch (SetFlagException ex)
            {
                // US-044-01 (AC-044-3): unknown key → 404 FLAG_UNKNOWN (checked first); invalid typed value → 400 FLAG_INVALID.
                throw new BusinessException(ex.Code, ex.Message, ex);
            }
        });

        return endpoints;
    }

    // T-011-04: helper — parse an optional long query param; bad int → 400 VALIDATION.
    private static long? ParseLongParam(HttpRequest request, string name)
    {
        var raw = request.Query[name].FirstOrDefault();
        if (raw is null || raw.Length == 0) return null;
        if (!long.TryParse(raw, out var value))
            throw new BusinessException(ProblemCodes.Validation, $"{name} must be an integer.");
        return value;
    }

    // T-011-04: helper — parse an optional DateTime query param (RoundtripKind); bad date → 400 VALIDATION.
    private static DateTime? ParseDateTimeParam(HttpRequest request, string name)
    {
        var raw = request.Query[name].FirstOrDefault();
        if (raw is null || raw.Length == 0) return null;
        if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value))
            throw new BusinessException(ProblemCodes.Validation, $"{name} must be a valid date/time.");
        return value;
    }
}
