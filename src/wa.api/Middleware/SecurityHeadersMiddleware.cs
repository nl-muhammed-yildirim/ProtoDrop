namespace wa.api.Middleware;

/// <summary>
/// CHORE-deploy-hardening (TA-9.5): response security headers on every API response. The API serves JSON only, so the CSP
/// is the locked-down <c>default-src 'none'</c>. <c>Strict-Transport-Security</c> is written directly (not via <c>UseHsts</c>,
/// which needs <c>Request.IsHttps</c> and the app terminates no TLS behind Front Door); browsers ignore HSTS over plain HTTP
/// (RFC 6797 section 8.1), so emitting it is safe. It is skipped in Development and Local so localhost is never pinned to HTTPS.
/// Registered first in the pipeline so preflight 204s and limiter 429s carry the headers too.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string HstsValue = "max-age=2592000"; // 30 days = HstsOptions' framework default; no includeSubDomains/preload

    private readonly RequestDelegate _next;
    private readonly bool _emitHsts;

    /// <summary>Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.</summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="environment">Host environment; HSTS is emitted unless it is Development or Local.</param>
    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _emitHsts = !environment.IsDevelopment()
            && !string.Equals(environment.EnvironmentName, "Local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sets the security headers, then continues the pipeline.</summary>
    /// <param name="context">The current HTTP context.</param>
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        if (_emitHsts)
            headers["Strict-Transport-Security"] = HstsValue;

        return _next(context);
    }
}
