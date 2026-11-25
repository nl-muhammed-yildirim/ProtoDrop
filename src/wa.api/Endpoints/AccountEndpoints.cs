using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the account routes: delete account, org logo and GDPR data export, moved verbatim from Program.cs in source order.
/// </summary>
internal static class AccountEndpoints
{
    /// <summary>
    /// Maps the account routes: delete account, org logo and GDPR data export. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers + account_deleted once.
        endpoints.MapDelete("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var result = await sender.Send(new DeleteAccountCommand(userId), ct);

                // account_deleted already went out via the outbox (EC-008-2: exactly one event for one deletion — dedup on EventId).
                ctx.Response.Cookies.Delete("wa.session"); // clear cookie exactly like logout
                return Results.Json(new { userId = userId.ToString("n") }, statusCode: 200); // body mirrors GET's shape minus profile fields
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                // Same as GET — valid cookie, no row → 404 NOT_FOUND.
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        });

        // US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo (idempotent; not plan-gated).
        endpoints.MapDelete("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                await sender.Send(new RemoveOrgLogoCommand(userId), ct);
                return Results.NoContent();
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        });

        // US-021-04 (FR-021-3..5): PUT /api/v1/auth/me/logo — raw image body; the handler bounds the read, sniffs the content and stores it.
        endpoints.MapPut("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var view = await sender.Send(new UploadOrgLogoCommand(userId, ctx.Request.Body), ct);
                return Results.Json(ToLogoBody(view), statusCode: 200);
            }
            catch (OrgLogoRejectedException ex)
            {
                throw new BusinessException(ex.Code, ex.Message, ex);
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        });

        // US-021-04 (D-B5): GET /api/v1/auth/me/logo — owner view { logoUrl, maxLogoSize, brandingEnabled }.
        endpoints.MapGet("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var view = await sender.Send(new GetOrgLogoQuery(userId), ct);
                return Results.Json(ToLogoBody(view), statusCode: 200);
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        });

        // US-047-01 (FR-047-1/2/4): POST /api/v1/auth/me/export — request a GDPR data export (gated by feature.dataExport); 202 + processing view.
        endpoints.MapPost("/api/v1/auth/me/export", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var view = await sender.Send(new wa.application.UseCases.DataExport.ExportMyDataCommand(userId), ct);
                return Results.Json(ToExportBody(view), statusCode: 202);
            }
            catch (wa.application.UseCases.DataExport.DataExportInFlightException)
            {
                throw new BusinessException(ProblemCodes.ExportInFlight, "Your export is already preparing.");
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        }).RequireFeature(FeatureGates.DataExport);

        // US-047-01 (FR-047-2/4): GET /api/v1/auth/me/export — latest export state (+ fresh 24 h downloadUrl when ready). NOT gated: in-flight jobs still complete and stay downloadable.
        endpoints.MapGet("/api/v1/auth/me/export", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            var view = await sender.Send(new wa.application.UseCases.DataExport.GetDataExportQuery(userId), ct);
            return view is null ? Results.NoContent() : Results.Json(ToExportBody(view), statusCode: 200);
        });

        return endpoints;
    }

    private static object ToLogoBody(OrgLogoView view) =>
        new { logoUrl = view.LogoUrl?.ToString(), maxLogoSize = view.MaxLogoSize, brandingEnabled = view.BrandingEnabled };

    private static Dictionary<string, object?> ToExportBody(wa.application.UseCases.DataExport.DataExportView view)
    {
        var body = new Dictionary<string, object?> { ["state"] = view.State, ["createdAtUtc"] = EndpointFormatting.AsUtc(view.CreatedAtUtc) };
        if (view.State == "ready")
        {
            body["readyAtUtc"] = view.ReadyAtUtc is { } readyAt ? EndpointFormatting.AsUtc(readyAt) : null;
            body["downloadUrl"] = view.DownloadUrl?.ToString();
        }
        return body;
    }
}
