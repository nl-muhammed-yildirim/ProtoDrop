using MediatR;
using wa.domain; // T-006-03: EmailSuppression entity type for admin routes
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the billing routes: Stripe webhook, checkout, portal and plan usage, moved verbatim from Program.cs in source order.
/// </summary>
internal static class BillingEndpoints
{
    /// <summary>
    /// Maps the billing routes: Stripe webhook, checkout, portal and plan usage. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configuration">Configuration source read per request by the webhook route.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        // US-019-03 (TA-4.2#19 / FR-019-5): POST /api/v1/billing/webhooks/stripe — Stripe-Signature is the auth (no session gate).
        endpoints.MapPost("/api/v1/billing/webhooks/stripe", async (HttpRequest request, ISender sender, TimeProvider timeProvider, CancellationToken ct) =>
        {
            string payload;
            using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
                payload = await reader.ReadToEndAsync(ct);

            var secret = configuration["Wa:Stripe:WebhookSecret"];
            if (!wa.application.UseCases.Billing.StripeWebhookSignature.IsValid(
                    request.Headers["Stripe-Signature"].ToString(), payload, secret, timeProvider.GetUtcNow()))
                throw new BusinessException(ProblemCodes.StripeWebhookMismatch, "Stripe signature verification failed.");

            try
            {
                await sender.Send(new wa.application.UseCases.Billing.HandleStripeWebhookCommand(payload), ct);
            }
            catch (wa.application.UseCases.Billing.StripeWebhookException ex) when (!ex.Retryable)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
            // Retryable StripeWebhookException → GlobalErrorMiddleware → 500 INTERNAL → Stripe redelivers (EC-019-1).

            return Results.Ok();
        });

        // US-019-01 (TA-4.2#18 / FR-019-1): POST /api/v1/billing/checkout — cookie auth; returns the Stripe-hosted Checkout URL. Persists nothing.
        endpoints.MapPost("/api/v1/billing/checkout", async (wa.application.UseCases.Billing.CheckoutRequest req, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            if (string.IsNullOrWhiteSpace(req?.PlanCode))
                throw new BusinessException(ProblemCodes.Validation, "planCode is required.");

            BillingInterval interval;
            if (req.Interval is null || string.Equals(req.Interval, "monthly", StringComparison.OrdinalIgnoreCase))
                interval = BillingInterval.Monthly;
            else if (string.Equals(req.Interval, "annual", StringComparison.OrdinalIgnoreCase))
                interval = BillingInterval.Annual;
            else
                throw new BusinessException(ProblemCodes.Validation, "interval must be 'monthly' or 'annual'.");

            try
            {
                var session = await sender.Send(
                    new wa.application.UseCases.Billing.CreateCheckoutSessionCommand(userId, req.PlanCode.Trim(), interval), ct);
                return Results.Ok(new { url = session.Url, sessionId = session.SessionId });
            }
            catch (wa.application.UseCases.Billing.CheckoutException ex)
            {
                var code = ex.Code == wa.application.UseCases.Billing.CheckoutException.FailureCode.NotFound
                    ? ProblemCodes.NotFound
                    : ProblemCodes.Validation;
                throw new BusinessException(code, ex.Message, ex);
            }
            catch (wa.application.UseCases.Billing.BillingUnavailableException ex)
            {
                throw new BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex);
            }
        });

        // US-019-02 (TA-4.2#18): GET /api/v1/billing/portal — cookie auth; Stripe Billing Portal URL for the user's customer, or the upgrade fallback. Persists nothing.
        endpoints.MapGet("/api/v1/billing/portal", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var result = await sender.Send(new wa.application.UseCases.Billing.GetPortalSessionQuery(userId), ct);
                return Results.Ok(new { url = result.Url, fallback = result.Url is null ? "upgrade" : (string?)null });
            }
            catch (wa.application.UseCases.Billing.BillingUnavailableException ex)
            {
                throw new BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex);
            }
        });

        // US-020-03 (FR-020-3 / AC-020-3): GET /api/v1/billing/plan. Cookie auth. Current plan + live usage vs. PlanContext limits. Persists nothing.
        endpoints.MapGet("/api/v1/billing/plan", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            var r = await sender.Send(new wa.application.UseCases.Billing.GetPlanUsageQuery(userId), ct);
            return Results.Ok(new
            {
                planCode = r.PlanCode,
                storage = new { usedBytes = r.StorageUsedBytes, allowedBytes = r.StorageAllowedBytes },
                activeTransfers = new { used = r.ActiveTransfersUsed, allowed = r.ActiveTransfersAllowed },
                limits = new { maxTransferSize = r.MaxTransferSize, maxSingleFile = r.MaxSingleFile },
                features = new { scheduling = r.Scheduling, analytics = r.Analytics },   // US-022-01 (FR-022-3, D-6)
                scheduleWindow = new { minMinutes = wa.domain.Transfers.ScheduleWindow.MinMinutes, maxDays = wa.domain.Transfers.ScheduleWindow.MaxDays }   // US-022-03 (FR-022-2)
            });
        });

        return endpoints;
    }
}
