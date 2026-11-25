using Microsoft.AspNetCore.Http;
using wa.application.UseCases.Auth;

namespace wa.api.Middleware;

/// <summary>
/// T-008-02 (FR-008-2 / TA-9.1): session middleware — reads wa.session cookie, parses via ISessionTokenMinter.Parse.
/// Valid → sets user id in HttpContext.Items['wa.session.userId'] for endpoints 13–15; refreshes cookie when lifetime &lt; 15 days (rolling).
/// Missing/invalid/expired → guest (item null). No response writes except Set-Cookie refresh.
/// </summary>
public sealed class SessionMiddleware
{
    /// <summary>Key under which the middleware stores the session user id (a nullable <see cref="Guid"/>) in <c>HttpContext.Items</c>; absent or null for guests.</summary>
    public const string SessionUserIdKey = "wa.session.userId";
    private const string CookieName = "wa.session";

    private readonly RequestDelegate _next;
    private readonly ISessionTokenMinter _tokens; // production = SessionTokenService (minter + reader in one class so format can't drift)

    /// <summary>Initializes a new instance of the <see cref="SessionMiddleware"/> class.</summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="tokens">Reads session cookies and decides when a cookie needs renewal.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokens"/> is null.</exception>
    public SessionMiddleware(RequestDelegate next, ISessionTokenMinter tokens)
    {
        _next = next;
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <summary>Reads the session cookie, publishes the user id for later middleware and endpoints, and renews the cookie when it is close to expiry.</summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var sessionCookie = context.Request.Cookies[CookieName];

        // TA-9.1: single parsing site — the middleware owns the read; endpoints 13–15 just read the Items value (T-008-06).
        if (_tokens.Parse(sessionCookie) is { } parsed)
        {
            var (userId, expUtc) = parsed;
            context.Items[SessionUserIdKey] = userId;

            // FR-008-2 / TA-9.1 30-day rolling: re-mint when remaining lifetime < RenewRemainingDays (frozen threshold).
            if (_tokens.NeedsRenewal(expUtc, DateTime.UtcNow))
            {
                var newToken = _tokens.Mint(userId);
                SetSessionCookie(context, newToken);
            }
        }

        await _next(context);
    }

    private static void SetSessionCookie(HttpContext context, string token) =>
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // TA-9.1 + FR-008-2 (not asserted in local plain-HTTP tests — no prior test does)
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(SessionTokenService.LifetimeDays) // frozen 30 d — no magic number
        });
}
