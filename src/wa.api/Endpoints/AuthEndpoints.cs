using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the auth routes (signup, magic link, forgot password, login, profile, logout), moved verbatim from Program.cs in source order.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>
    /// Maps the auth routes (signup, magic link, forgot password, login, profile, logout). Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="adminGate">Role gate used to derive the role field of the profile.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints, AdminGate adminGate)
    {
        // T-008-01 (TA-4.2#8 / FR-008-3): sign-up is sign-in — unknown email → create; known email (any case) → password-verified sign-in (wrong password → 401 WRONG_PASSWORD).
        endpoints.MapPost("/api/v1/auth/signup", async (SignupCommand cmd, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(cmd, ct);
                return Results.Json(new
                {
                    userId = result.UserId.ToString("n"),
                    email = result.Email,
                    displayName = result.DisplayName,
                    theme = result.Theme,
                    planCode = result.PlanCode,
                    sessionToken = result.SessionToken
                }, statusCode: 200);
            }
            catch (SignupValidationException ex)
            {
                // T-056 pipeline maps BusinessException → Problem+JSON; no new code — reuse VALIDATION (TA-4.1.3).
                // Preserve the exception chain: the inner keeps its stack trace (Serilog), wire shape still comes from Code + the pipeline.
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
            catch (LoginWrongPasswordException ex)
            {
                // CHORE-staging-blockers: known email + wrong/absent password or deleted account → 401 WRONG_PASSWORD, same as login (no new code).
                throw new BusinessException(ProblemCodes.WrongPassword, ex.Message, ex);
            }
        });

        // T-008-04 (FR-008-1 / TA-9.1): POST /api/v1/auth/magic-link — request a sign-in link. 202 Accepted with no body.
        endpoints.MapPost("/api/v1/auth/magic-link", async (RequestMagicLinkCommand cmd, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(cmd, ct);
                return Results.Accepted(); // 202 — link is in-flight; body carries nothing (TA-4.2#10)
            }
            catch (RequestMagicLinkValidationException ex)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
        });

        // T-008-05 (FR-008-6 / TA-4.2#11): POST /api/v1/auth/forgot-password — request a sign-in link with the forgot-password email.
        // Anti-enumeration: 202 Accepted, NO body, byte-identical for registered AND unregistered (D-5). Body: { email }.
        endpoints.MapPost("/api/v1/auth/forgot-password", async (ForgotPasswordCommand cmd, ISender sender, wa.application.Telemetry.WaTelemetryReporter telemetry, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(cmd, ct);
                telemetry.ForgotPasswordRequested(); // D-4: request-volume metric — only on the 202 path (a 400 isn't an abuse signal)
                return Results.Accepted(); // 202 — identical for registered/unregistered (anti-enumeration; TA-4.2#11)
            }
            catch (ForgotPasswordValidationException ex)
            {
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
        });

        // T-008-04 (FR-008-1 / FR-008-3 / AC-008-2 / EC-008-3): POST /api/v1/auth/magic-link/redeem — sign in via magic link.
        // Body: { token }. Success → 200 + same wire shape as login + wa.session cookie (TA-9.1).
        // LinkExpiredException (expired OR already redeemed) → 410 LINK_EXPIRED (identical state, don't leak which — EC-008-3).
        // Invalid token → also 410 LINK_EXPIRED (AC-008-2: second click invalid; don't leak "token not found" vs "expired").
        endpoints.MapPost("/api/v1/auth/magic-link/redeem", async (RedeemMagicLinkCommand cmd, ISender sender, HttpContext httpContext, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(cmd, ct);

                // TA-9.1: set wa.session cookie from the minted token (cookie value == body's sessionToken; httpOnly, SameSite=Lax).
                httpContext.Response.Cookies.Append("wa.session", result.SessionToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(SessionTokenService.LifetimeDays) // frozen 30 d — no magic number
                });

                return Results.Json(new
                {
                    userId = result.UserId.ToString("n"),
                    email = result.Email,
                    displayName = result.DisplayName,
                    theme = result.Theme,
                    planCode = result.PlanCode,
                    sessionToken = result.SessionToken
                }, statusCode: 200);
            }
            catch (RedeemValidationException ex)
            {
                // TA-4.1.3: malformed/missing token → VALIDATION (400), NOT LINK_EXPIRED (plan §ERROR CASES).
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
            catch (LinkExpiredException)
            {
                // EC-008-3 + AC-008-2: expired ≡ redeemed ≡ unknown token — all 410 LINK_EXPIRED. No leakage.
                return Results.Json(ProblemWriter.Build(410, ProblemCodes.LinkExpired, ProblemCodes.TitleOf(ProblemCodes.LinkExpired)), statusCode: 410);
            }
        });

        // T-008-02 (TA-4.2#9 / FR-008-1): login — signs in an existing user. Same wire shape as signup + sets wa.session cookie (TA-9.1).
        endpoints.MapPost("/api/v1/auth/login", async (LoginCommand cmd, ISender sender, HttpContext httpContext, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(cmd, ct);

                // TA-9.1: set wa.session cookie from the minted token (cookie value == body's sessionToken; httpOnly, SameSite=Lax).
                httpContext.Response.Cookies.Append("wa.session", result.SessionToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true, // TA-9.1 + FR-008-2 (not asserted in local plain-HTTP tests — no prior test does)
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(SessionTokenService.LifetimeDays) // frozen 30 d — no magic number
                });

                // TA-4.2a: exact wire shape as signup (userId 'n' form, email, displayName, theme, planCode, sessionToken).
                return Results.Json(new
                {
                    userId = result.UserId.ToString("n"),
                    email = result.Email,
                    displayName = result.DisplayName,
                    theme = result.Theme,
                    planCode = result.PlanCode,
                    sessionToken = result.SessionToken
                }, statusCode: 200);
            }
            catch (LoginWrongPasswordException ex)
            {
                // T-056 pipeline maps BusinessException → Problem+JSON; 401 WRONG_PASSWORD via the closed list (no new code).
                throw new BusinessException(ProblemCodes.WrongPassword, ex.Message, ex);
            }
            catch (LoginValidationException ex)
            {
                // T-056 pipeline maps BusinessException → Problem+JSON; no new code — reuse VALIDATION (TA-4.1.3).
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
            }
        });

        // T-008-06 (FR-008-4 / TA-4.2#13): GET /api/v1/auth/me — the session user's profile DTO.
        // Wire shape = login's minus sessionToken (the cookie already carries the session; no re-mint).
        endpoints.MapGet("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            try
            {
                var result = await sender.Send(new GetProfileCommand(userId), ct);
                // T-011-01 (TA-9.2 / UI-Reference §5.6): additive role field — "superadmin" or "operator"; ABSENT for non-admin so the UI renders disabled buttons with tooltip, not hidden elements.
                var roleField = AdminRolePolicy.RoleField(AdminRolePolicy.Resolve(result.Email.ToLowerInvariant(), adminGate.AdminEmails, adminGate.OperatorEmails));
                // TA-4.2a exact key set (signup/login verbatim order). Dictionary preserves insertion order — the additive 'role' key is present ONLY when non-null, so a non-admin's body carries no role key at all (not "role":null).
                var meBody = new Dictionary<string, object>
                {
                    ["userId"] = result.UserId.ToString("n"),
                    ["email"] = result.Email,
                    ["displayName"] = result.DisplayName,
                    ["theme"] = result.Theme,
                    ["planCode"] = result.PlanCode,
                    ["orgName"] = result.OrgName!, // F-PRF-001 (FR-021-1): null when unset; the profile page reads it
                    ["emailNotifications"] = result.EmailNotifications // F-XCT-003 (FR-046-2): effective value, null → true
                };
                if (roleField is not null)
                    meBody["role"] = roleField;
                return Results.Json(meBody, statusCode: 200);
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                // Valid cookie but no AppUser row (unknown OR soft-deleted) → 404 NOT_FOUND (spec's literal pin).
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
        });

        // T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial update (only provided fields change).
        endpoints.MapPatch("/api/v1/auth/me", async (UpdateProfileRequest req, HttpContext ctx, ISender sender, IFlagResolver flags, CancellationToken ct) =>
        {
            if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
                return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

            // F-XCT-003 (FR-046-6): the setting is editable only while feature.emailSettings is on — same 404 as .RequireFeature.
            if (req.EmailNotifications is not null && !await flags.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent, ct))
                throw new BusinessException(ProblemCodes.FeatureDisabled, ProblemCodes.TitleOf(ProblemCodes.FeatureDisabled));

            try
            {
                // Single construction site (TA-4.2a): UserId from the session cookie — NOT in the JSON body.
                var result = await sender.Send(new UpdateProfileCommand(userId, req.DisplayName, req.Theme, req.OrgName, req.EmailNotifications), ct);
                return Results.Json(new
                {
                    userId = result.UserId.ToString("n"),
                    email = result.Email,
                    displayName = result.DisplayName,
                    theme = result.Theme,
                    planCode = result.PlanCode,
                    orgName = result.OrgName,
                    emailNotifications = result.EmailNotifications
                }, statusCode: 200);
            }
            catch (OrgLogoRejectedException ex)
            {
                // Free plan setting an org name (branding is Pro) -> same 403 FORBIDDEN as the logo upload.
                throw new BusinessException(ex.Code, ex.Message, ex);
            }
            catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
            {
                // Same as GET — valid cookie, no row → 404 NOT_FOUND.
                return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
            }
            catch (MeProfileException ex)
            {
                // Validation family (invalid theme / too-long name / empty PATCH) → BusinessException(Validation, ...) —
                // inner-exception-preservation pattern from T-008-01 fix round.
                throw new BusinessException(ProblemCodes.Validation, ex.Message, ex)
                {
                    Errors = ex.Field is null ? null : [new ProblemFieldError(ex.Field, ex.Message)]
                };
            }
        });

        // T-008-06 (TA-4.2#12): POST /api/v1/auth/logout — clear the session cookie. No use case: cookie clearing is
        // endpoint-level (the middleware owns parsing — same as T-008-02). No telemetry beyond metrics (task spec §3).
        endpoints.MapPost("/api/v1/auth/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete("wa.session"); // the middleware only refreshes when a token is present; deleting leaves the next request as guest
            return Results.Ok(); // 200 no body — top bar returns to "Sign in" via the SPA's redirect
        });

        return endpoints;
    }
}
