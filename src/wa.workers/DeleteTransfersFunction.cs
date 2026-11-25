using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Transfers;
using wa.domain.Correlation;

namespace wa.workers;

/// <summary>
/// T-005-02: f-delete-transfers Azure Function — timer every 15 min, concurrency 1 (TA-6.4).
/// Claims its window via JobRun (TA-6.1) so scale-out instances don't double-process (FR-005-9).
/// Host.json settings: concurrency 1, timeout 10 min.
/// T-005-04: HTTP trigger for E2E manual invocation.
/// </summary>
public partial class DeleteTransfersFunction
{
    private readonly DeleteExpiredTransfersHandler _handler;
    private readonly ILogger<DeleteTransfersFunction> _logger;
    private readonly IConfiguration _config;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="DeleteTransfersFunction"/> class.</summary>
    /// <param name="handler">Handler that deletes transfers that are past their grace period.</param>
    /// <param name="logger">Logger for the job run (carries the generated correlation ids).</param>
    /// <param name="config">Configuration source for the <c>Wa:Jobs:SignKey</c> that guards the HTTP trigger.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public DeleteTransfersFunction(DeleteExpiredTransfersHandler handler, ILogger<DeleteTransfersFunction> logger, IConfiguration config, TimeProvider timeProvider)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
        _timeProvider = timeProvider;
    }

    /// <summary>Timer entry point (every 15 minutes): runs one transfer deletion pass under a freshly generated traceparent.</summary>
    /// <param name="context">Function invocation context.</param>
    /// <param name="trigger">Timer trigger metadata (schedule status); not used.</param>
    [Function("DeleteTransfers")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 */15 * * * *")] TimerTriggerAttribute trigger)
    {
        // T-012-01: timer-triggered — no inbound header, so always generate a fresh valid traceparent (EC-056-1).
        var tp = W3CTraceParser.New();
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = tp.CorrelationId, ["TraceParent"] = tp.ToHeader() });

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            LogCompleted(_logger, count);
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex);
            throw; // Azure Functions retries on unhandled exception; JobRun claim prevents double-processing.
        }
    }

    /// <summary>T-005-04: HTTP trigger for E2E manual invocation (accelerates the 15-min timer).</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="req">The HTTP request; it must carry the <c>x-job-sign</c> header.</param>
    /// <returns>200 with the number of transfers deleted, or a 401 problem when the signature is missing or wrong.</returns>
    [Function("DeleteTransfersHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/delete-transfers")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(_timeProvider.GetUtcNow().UtcDateTime, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { deleted = count });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "f-delete-transfers completed: {Count} transfers deleted")]
    private static partial void LogCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-delete-transfers failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
