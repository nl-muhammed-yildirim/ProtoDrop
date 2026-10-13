using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using wa.application.Ports;
using Xunit;

namespace wa.api.integration;

public class HealthFailureTests
{
    // Program.cs builds the blob client eagerly from §5.2 config (T-055-02); the test host has no launchSettings, and ConfigureAppConfiguration in the factory lands too late for top-level statements — env vars are always visible to CreateBuilder's configuration.
    static HealthFailureTests()
    {
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", "http://127.0.0.1:10000/devstoreaccount1");
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");
    }

    private static readonly TestFactory factory = new();

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Program.cs registers the real probes before this runs — remove, then swap.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbHealthProbe>();
                services.AddSingleton<IDbHealthProbe, ThrowingDbProbe>();
            });
        }
    }

    [Fact]
    public async Task Health_DatabaseDown_Returns503WithProblemJson()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        // TA-4.1.3 shape: type/title/status/code/details/correlationId — code list is closed (no DB_DOWN → INTERNAL).
        Assert.Equal("INTERNAL", root.GetProperty("code").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("type", out _));
        Assert.True(root.TryGetProperty("title", out _));
        Assert.True(root.TryGetProperty("details", out _));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("correlationId").GetString()));
    }

    private sealed class ThrowingDbProbe : IDbHealthProbe
    {
        public Task PingAsync(CancellationToken cancellationToken) => Task.FromException(new IOException("simulated DB down"));
    }
}
