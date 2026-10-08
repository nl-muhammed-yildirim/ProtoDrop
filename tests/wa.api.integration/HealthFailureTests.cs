using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using wa.application.Ports;
using Xunit;

namespace wa.api.integration;

public class HealthFailureTests
{
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
