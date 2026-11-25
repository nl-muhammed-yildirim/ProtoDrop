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
public partial class GdprSweepFunction
{
    private readonly SweepErasedTelemetryHandler _handler;
    private readonly ILogger<GdprSweepFunction> _logger;
    private readonly IConfiguration _config;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="GdprSweepFunction"/> class.</summary>
    /// <param name="handler">Handler that erases telemetry of deleted accounts and marks their markers swept.</param>
    /// <param name="logger">Logger for the job run (carries the generated correlation ids).</param>
    /// <param name="config">Configuration source for the <c>Wa:Jobs:SignKey</c> that guards the HTTP trigger.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public GdprSweepFunction(SweepErasedTelemetryHandler handler, ILogger<GdprSweepFunction> logger, IConfiguration config, TimeProvider timeProvider)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
        _timeProvider = timeProvider;
    }

    /// <summary>Timer entry point (nightly at 02:00 UTC): runs one GDPR sweep pass under a freshly generated traceparent.</summary>
    /// <param name="context">Function invocation context.</param>
    /// <param name="trigger">Timer trigger metadata (schedule status); not used.</param>
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
            var count = await _handler.RunAsync(_timeProvider.GetUtcNow().UtcDateTime, context.CancellationToken);
            LogCompleted(_logger, count);
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex);
            throw; // JobRun claim prevents double-processing on retry.
        }
    }

    /// <summary>HTTP trigger for E2E manual invocation (outside the 02:00 timer).</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="req">The HTTP request; it must carry the <c>x-job-sign</c> header.</param>
    /// <returns>200 with the number of markers swept, or a 401 problem when the signature is missing or wrong.</returns>
    [Function("GdprSweepHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/gdpr-sweep")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(_timeProvider.GetUtcNow().UtcDateTime, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { swept = count });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "f-gdpr-sweep completed: {Count} markers swept")]
    private static partial void LogCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-gdpr-sweep failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
