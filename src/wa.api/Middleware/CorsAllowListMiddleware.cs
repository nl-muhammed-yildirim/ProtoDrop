using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace wa.api.Middleware;

/// <summary>
/// TA-4.1.6 CORS — only configured origins are allowed, no `*` wildcard (a wildcard would leak to any site). Config key
/// <c>Wa:Cors:AllowedOrigins</c>: comma-separated list in prod, single value locally. Preflight answers carry a 1 h cache;
/// unlisted origins get the plain pass-through (no CORS headers — the browser fails them), so non-browser traffic is unaffected.
/// </summary>
public sealed class CorsAllowListMiddleware
{
    private static readonly TimeSpan PreflightMaxAge = TimeSpan.FromHours(1); // TA-4.1.6: preflight cached 1 h

    private readonly RequestDelegate _next;
    private readonly string[] _allowedOrigins;

    public CorsAllowListMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var configured = configuration["Wa:Cors:AllowedOrigins"];
        // No config → CORS disabled (M0 local runs set the value via launchSettings; prod sets it per environment).
        _allowedOrigins = string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && _allowedOrigins.Contains(origin, StringComparer.Ordinal))
        {
            context.Response.Headers.AccessControlAllowOrigin = origin; // echo the exact origin — never "*", TA-4.1.6
            context.Response.Headers.AccessControlAllowCredentials = "true"; // httpOnly session cookie (TA-4.1.2) needs credentials
            if (context.Request.Method == HttpMethods.Options) // preflight: 1 h cache so the browser stops re-asking per request
            {
                context.Response.Headers.Vary.Append("Origin");
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Idempotency-Key";
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, PATCH, DELETE, OPTIONS";
                context.Response.Headers.AccessControlMaxAge = PreflightMaxAge.TotalSeconds.ToString(CultureInfo.InvariantCulture); // StringValues has no implicit int conversion
            }
        }

        await _next(context);
    }
}
