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
public class DataExportFunction
{
    private readonly BuildDataExportsHandler _handler;
    private readonly ILogger<DataExportFunction> _logger;
    private readonly IConfiguration _config;

    public DataExportFunction(BuildDataExportsHandler handler, ILogger<DataExportFunction> logger, IConfiguration config)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
    }

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
            var count = await _handler.RunAsync(DateTime.UtcNow, context.CancellationToken);
            _logger.LogInformation("f-data-export completed: {Count} exports built", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-data-export failed");
            throw; // JobRun claim prevents double-processing on retry.
        }
    }

    /// <summary>HTTP trigger for E2E manual invocation (accelerates the 5-min timer).</summary>
    [Function("DataExportHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/data-export")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(DateTime.UtcNow, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { built = count });
    }
}
