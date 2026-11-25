using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Gdpr;
using wa.domain.Correlation;

namespace wa.workers;

/// <summary>
/// US-047-02 (FR-047-5/8, TA-6.2): f-gdpr-sweep Azure Function — nightly 02:00 UTC, concurrency 1.
/// Claims its window via JobRun (TA-6.1); sweeps Erasure markers older than 24 h. The signed HTTP trigger lets E2E/QA run a pass on demand.
/// </summary>
public class GdprSweepFunction
{
    private readonly SweepErasedTelemetryHandler _handler;
    private readonly ILogger<GdprSweepFunction> _logger;
    private readonly IConfiguration _config;

    /// <summary>Initializes a new instance of the <see cref="GdprSweepFunction"/> class.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <param name="config">The config.</param>
    public GdprSweepFunction(SweepErasedTelemetryHandler handler, ILogger<GdprSweepFunction> logger, IConfiguration config)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
    }

    /// <summary>Runs the operation.</summary>
    /// <param name="context">The context.</param>
    /// <param name="trigger">The trigger.</param>
    [Function("GdprSweep")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 0 2 * * *")] TimerTriggerAttribute trigger)
    {
        // Timer-triggered — no inbound header, so always generate a fresh valid traceparent (EC-056-1).
        var tp = W3CTraceParser.New();
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = tp.CorrelationId, ["TraceParent"] = tp.ToHeader() });

        try
        {
            var count = await _handler.RunAsync(DateTime.UtcNow, context.CancellationToken);
            _logger.LogInformation("f-gdpr-sweep completed: {Count} markers swept", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-gdpr-sweep failed");
            throw; // JobRun claim prevents double-processing on retry.
        }
    }

    /// <summary>HTTP trigger for E2E manual invocation (outside the 02:00 timer).</summary>
    /// <param name="context">The context.</param>
    /// <param name="req">The req.</param>
    /// <returns>The HTTP response data.</returns>
    [Function("GdprSweepHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/gdpr-sweep")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(DateTime.UtcNow, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { swept = count });
    }
}
