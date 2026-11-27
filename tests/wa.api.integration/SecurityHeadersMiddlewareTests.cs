using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using wa.api.Middleware;
using Xunit;

namespace wa.api.integration;

// CHORE-deploy-hardening (TA-9.5): API response security headers; HSTS only outside Development/Local.
public class SecurityHeadersMiddlewareTests
{
    // Program.cs builds the blob client eagerly and ApiServices requires Wa:Jwt:Secret (same pattern as PipelineTests).
    static SecurityHeadersMiddlewareTests()
    {
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", "http://127.0.0.1:10000/devstoreaccount1");
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "wa.api.tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<DefaultHttpContext> InvokeAsync(string environmentName)
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new StubEnvironment(environmentName));
        await middleware.InvokeAsync(context);
        return context;
    }

    private static void AssertBaseHeaders(IHeaderDictionary headers)
    {
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task Staging_SetsAllHeadersAndHsts()
    {
        var context = await InvokeAsync("Staging");

        AssertBaseHeaders(context.Response.Headers);
        Assert.Equal("max-age=2592000", context.Response.Headers["Strict-Transport-Security"].ToString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Local")]
    [InlineData("local")]
    public async Task DevelopmentAndLocal_SetHeadersWithoutHsts(string environmentName)
    {
        var context = await InvokeAsync(environmentName);

        AssertBaseHeaders(context.Response.Headers);
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Pipeline_NotFoundResponse_CarriesHeaders()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/no-such-route-hardening");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.False(response.Headers.Contains("Strict-Transport-Security")); // factory environment is Development
    }
}
