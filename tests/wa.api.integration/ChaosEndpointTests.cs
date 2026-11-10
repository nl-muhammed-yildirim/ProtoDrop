using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace wa.api.integration;

// AC-013-1 / EC-013-1 (T-013-01): dev-only chaos endpoint forces both failure shapes — 500 INTERNAL and 429 RATE_LIMITED —
// with correlationId present, and two forced 500s carry distinct correlationIds.
public class ChaosEndpointTests
{
    // Program.cs builds the blob client eagerly from §5.2 config (T-055-02) — same env-var pattern as PipelineTests/HealthFailureTests.
    static ChaosEndpointTests()
    {
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", "http://127.0.0.1:10000/devstoreaccount1");
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");
        // ISessionTokenMinter (Program.cs L130) reads this eagerly during host build — set it here so the test is self-contained.
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
    }

    private readonly WebApplicationFactory<Program> _factory = new();

    [Fact]
    public async Task InternalKind_Returns500ProblemJson_WithCorrelationId()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/dev/chaos?kind=internal");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); // kind=internal → InvalidOperationException → GlobalErrorMiddleware → 500 INTERNAL
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("INTERNAL", root.GetProperty("code").GetString()); // TA-4.1.3 closed list — no new codes (FR-013-6)
        Assert.Equal(500, root.GetProperty("status").GetInt32());

        // correlationId = first 8 hex of the W3C trace id (CorrelationMiddleware).
        var correlationId = root.GetProperty("correlationId").GetString()!;
        Assert.Matches(new Regex(@"^[0-9a-f]{8}$"), correlationId);
    }

    [Fact]
    public async Task RateLimitedKind_Returns429ProblemJson_WithRateLimitedCode()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/dev/chaos?kind=rate_limited");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); // kind=rate_limited → ProblemWriter.WriteCompleteAsync(429, RATE_LIMITED)
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("RATE_LIMITED", root.GetProperty("code").GetString()); // same code as the real AspNetCoreRateLimit breach path (Program.cs L115)
        Assert.Equal(429, root.GetProperty("status").GetInt32());

        // correlationId present — CorrelationMiddleware stamps it before routing.
        var correlationId = root.GetProperty("correlationId").GetString()!;
        Assert.Matches(new Regex(@"^[0-9a-f]{8}$"), correlationId);
    }

    [Fact]
    public async Task TwoInternalRequests_CarryDistinctCorrelationIds()
    {
        var client = _factory.CreateClient();

        using var response1 = await client.GetAsync("/api/v1/dev/chaos?kind=internal");
        using var doc1 = JsonDocument.Parse(await response1.Content.ReadAsByteArrayAsync());
        var cid1 = doc1.RootElement.GetProperty("correlationId").GetString()!;

        using var response2 = await client.GetAsync("/api/v1/dev/chaos?kind=internal");
        using var doc2 = JsonDocument.Parse(await response2.Content.ReadAsByteArrayAsync());
        var cid2 = doc2.RootElement.GetProperty("correlationId").GetString()!;

        // EC-013-1: unique per request by CorrelationMiddleware (W3C trace id, 8-hex prefix).
        Assert.NotEqual(cid1, cid2);
    }
}
