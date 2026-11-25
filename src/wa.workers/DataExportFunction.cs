using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.DataExport;
using wa.domain.Correlation;

namespace wa.workers;

/// <summary>
/// US-047-01 (FR-047-1/2/8, TA-6.2): f-data-export Azure Function — timer every 5 min, concurrency 1.
/// Claims its window via JobRun (TA-6.1); builds processing exports and purges ready ones past 7 days.
/// The signed HTTP trigger lets E2E/QA run a pass on demand.
/// </summary>
public partial class DataExportFunction
{
    private readonly BuildDataExportsHandler _handler;
    private readonly ILogger<DataExportFunction> _logger;
    private readonly IConfiguration _config;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="DataExportFunction"/> class.</summary>
    /// <param name="handler">Handler that builds pending exports and purges old ones.</param>
    /// <param name="logger">Logger for the job run (carries the generated correlation ids).</param>
    /// <param name="config">Configuration source for the <c>Wa:Jobs:SignKey</c> that guards the HTTP trigger.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public DataExportFunction(BuildDataExportsHandler handler, ILogger<DataExportFunction> logger, IConfiguration config, TimeProvider timeProvider)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
        _timeProvider = timeProvider;
    }

    /// <summary>Timer entry point (every 5 minutes): runs one build and purge pass under a freshly generated traceparent.</summary>
    /// <param name="context">Function invocation context.</param>
    /// <param name="trigger">Timer trigger metadata (schedule status); not used.</param>
    [Function("DataExport")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 */5 * * * *")] TimerTriggerAttribute trigger)
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

    /// <summary>HTTP trigger for E2E manual invocation (accelerates the 5-min timer).</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="req">The HTTP request; it must carry the <c>x-job-sign</c> header.</param>
    /// <returns>200 with the number of exports built, or a 401 problem when the signature is missing or wrong.</returns>
    [Function("DataExportHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/data-export")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(_timeProvider.GetUtcNow().UtcDateTime, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { built = count });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "f-data-export completed: {Count} exports built")]
    private static partial void LogCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-data-export failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
