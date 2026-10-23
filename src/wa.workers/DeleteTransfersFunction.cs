using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Transfers;

namespace wa.workers;

/// <summary>
/// T-005-02: f-delete-transfers Azure Function — timer every 15 min, concurrency 1 (TA-6.4).
/// Claims its window via JobRun (TA-6.1) so scale-out instances don't double-process (FR-005-9).
/// Host.json settings: concurrency 1, timeout 10 min.
/// T-005-04: HTTP trigger for E2E manual invocation.
/// </summary>
public class DeleteTransfersFunction
{
    private readonly DeleteExpiredTransfersHandler _handler;
    private readonly ILogger<DeleteTransfersFunction> _logger;
    private readonly IConfiguration _config;

    public DeleteTransfersFunction(DeleteExpiredTransfersHandler handler, ILogger<DeleteTransfersFunction> logger, IConfiguration config)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
    }

    [Function("DeleteTransfers")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 */15 * * * *")] TimerTriggerAttribute trigger)
    {
        var now = DateTime.UtcNow;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-delete-transfers completed: {Count} transfers deleted", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-delete-transfers failed");
            throw; // Azure Functions retries on unhandled exception; JobRun claim prevents double-processing.
        }
    }

    /// <summary>T-005-04: HTTP trigger for E2E manual invocation (accelerates the 15-min timer).</summary>
    [Function("DeleteTransfersHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/delete-transfers")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var count = await _handler.RunAsync(DateTime.UtcNow, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { deleted = count });
    }
}
