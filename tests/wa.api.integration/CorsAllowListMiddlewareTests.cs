using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using wa.api.Middleware;

namespace wa.api.integration;

/// <summary>
/// CHORE-code-style-2 (DECISION-2, TA-4.1.6): an allow-listed origin is echoed AND the response carries <c>Vary: Origin</c>
/// (the statement used to be a no-op LINQ <c>Append</c>), so a shared cache never serves one origin's echoed response to another.
/// Middleware-level facts over a <see cref="DefaultHttpContext"/> — no containers, no Docker.
/// </summary>
public class CorsAllowListMiddlewareTests
{
    private const string Allowed = "http://localhost:5173";

    private static CorsAllowListMiddleware NewMiddleware(RequestDelegate next, string? allowed = Allowed)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Cors:AllowedOrigins"] = allowed })
            .Build();
        return new CorsAllowListMiddleware(next, config);
    }

    private static DefaultHttpContext NewContext(string origin, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Headers.Origin = origin;
        return context;
    }

    [Fact]
    public async Task AllowedOrigin_IsEchoed_AndResponseVariesByOrigin()
    {
        var context = NewContext(Allowed);

        await NewMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(Allowed, context.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("Origin", context.Response.Headers[HeaderNames.Vary].ToString());
    }

    [Fact]
    public async Task AllowedOrigin_Preflight_AlsoVariesByOrigin()
    {
        var context = NewContext(Allowed, HttpMethods.Options);

        await NewMiddleware(_ => throw new InvalidOperationException("preflight must not reach routing")).InvokeAsync(context);

        Assert.Equal(204, context.Response.StatusCode);
        Assert.Equal("Origin", context.Response.Headers[HeaderNames.Vary].ToString());
    }

    [Fact]
    public async Task AllowedOrigin_KeepsAnExistingVaryValue()
    {
        var context = NewContext(Allowed);
        context.Response.Headers[HeaderNames.Vary] = "Accept-Encoding";

        await NewMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        var vary = context.Response.Headers[HeaderNames.Vary].ToString();
        Assert.Contains("Accept-Encoding", vary);
        Assert.Contains("Origin", vary);
    }

    [Fact]
    public async Task UnlistedOrigin_GetsNoCorsHeaders_AndNoVary()
    {
        var context = NewContext("http://evil.example");

        await NewMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.AccessControlAllowOrigin));
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.Vary));
    }
}
